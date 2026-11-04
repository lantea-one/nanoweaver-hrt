using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace NanoWeaverHrt.Caching;

/// <summary>
/// Distributed mutual-exclusion locking service utilizing Valkey atomic SET-NX operations when connected,
/// with automatic, thread-safe in-memory SemaphoreSlim fallback when operating in standalone mode.
/// </summary>
public sealed class ValkeyLockService : IValkeyLockService
{
    /// <summary>
    /// Key prefix applied to all lock keys.
    /// </summary>
    private const string LockKeyPrefix = "weaver:lock:";

    /// <summary>
    /// Lua script for safely releasing a Valkey lock only if the current value matches the unique lock token.
    /// </summary>
    private const string ReleaseLuaScript = @"
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        else
            return 0
        end";

    /// <summary>
    /// Connection factory providing access to the underlying Valkey database.
    /// </summary>
    private readonly IValkeyConnectionFactory _connectionFactory;

    /// <summary>
    /// Thread-safe dictionary of in-memory semaphores used for local locking fallback.
    /// </summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _localLocks = new();

    /// <summary>
    /// Thread-safe dictionary tracking active local in-memory lock tokens.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _localLockTokens = new();

    /// <summary>
    /// Logger instance used to record lock lifecycle diagnostics.
    /// </summary>
    private readonly ILogger<ValkeyLockService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValkeyLockService"/> class.
    /// </summary>
    /// <param name="connectionFactory">The Valkey connection factory.</param>
    /// <param name="logger">Optional logger instance.</param>
    public ValkeyLockService(IValkeyConnectionFactory connectionFactory, ILogger<ValkeyLockService>? logger = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? NullLogger<ValkeyLockService>.Instance;
    }

    /// <inheritdoc/>
    public async ValueTask<IAsyncDisposable?> AcquireLockAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        // WHAT: Acquires a mutual exclusion lock across the cluster or within process memory.
        // WHY:  Prevents race conditions on share key redemption, setup initialization, and concurrent batch exports.
        // HOW:  Tries Valkey atomic SET NX PX in a loop; if Valkey is offline, falls back to SemaphoreSlim.
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        string lockKey = LockKeyPrefix + resource;
        string lockValue = Guid.NewGuid().ToString("N");

        IDatabase? db = _connectionFactory.GetDatabase();
        if (db != null)
        {
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                while (!ct.IsCancellationRequested && (sw.Elapsed < timeout || timeout == TimeSpan.Zero))
                {
                    bool acquired = await db.StringSetAsync(
                        lockKey,
                        lockValue,
                        expiry,
                        When.NotExists).ConfigureAwait(false);

                    if (acquired)
                    {
                        return new DistributedLockToken(db, lockKey, lockValue, _logger);
                    }

                    if (timeout == TimeSpan.Zero)
                    {
                        break;
                    }

                    await Task.Delay(50, ct).ConfigureAwait(false);
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Valkey lock acquisition failed for resource '{Resource}'. Falling back to local semaphore.", resource);
            }
        }

        // Local in-memory semaphore fallback
        SemaphoreSlim semaphore = _localLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        bool localAcquired = await semaphore.WaitAsync(timeout, ct).ConfigureAwait(false);
        if (localAcquired)
        {
            _localLockTokens[lockKey] = lockValue;
            return new LocalLockToken(semaphore, _localLockTokens, lockKey, lockValue);
        }

        return null;
    }

    /// <summary>
    /// Lock token releasing an atomic Valkey distributed lock via Lua script on disposal.
    /// </summary>
    private sealed class DistributedLockToken : IAsyncDisposable
    {
        /// <summary>
        /// Database instance where the lock was registered.
        /// </summary>
        private readonly IDatabase _database;

        /// <summary>
        /// Key name of the acquired lock.
        /// </summary>
        private readonly string _lockKey;

        /// <summary>
        /// Unique token value proving ownership of the lock.
        /// </summary>
        private readonly string _lockValue;

        /// <summary>
        /// Logger instance.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Flag tracking whether disposal has occurred.
        /// </summary>
        private int _isDisposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="DistributedLockToken"/> class.
        /// </summary>
        /// <param name="database">The target Valkey database.</param>
        /// <param name="lockKey">The key of the acquired lock.</param>
        /// <param name="lockValue">The token proving ownership.</param>
        /// <param name="logger">The logger instance.</param>
        public DistributedLockToken(IDatabase database, string lockKey, string lockValue, ILogger logger)
        {
            _database = database;
            _lockKey = lockKey;
            _lockValue = lockValue;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            // WHAT: Releases the lock atomically using a Lua script.
            // WHY:  Guarantees the lock is only deleted if its current value matches the token held by this process.
            // HOW:  Calls ScriptEvaluateAsync with ReleaseLuaScript.
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            {
                return;
            }

            try
            {
                await _database.ScriptEvaluateAsync(
                    ReleaseLuaScript,
                    new RedisKey[] { _lockKey },
                    new RedisValue[] { _lockValue }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to release distributed lock for '{LockKey}' cleanly.", _lockKey);
            }
        }
    }

    /// <summary>
    /// Lock token releasing an in-memory SemaphoreSlim on disposal after validating ownership.
    /// </summary>
    internal sealed class LocalLockToken : IAsyncDisposable
    {
        /// <summary>
        /// Semaphore instance held by the lock.
        /// </summary>
        private readonly SemaphoreSlim _semaphore;

        /// <summary>
        /// Local lock tokens dictionary.
        /// </summary>
        private readonly ConcurrentDictionary<string, string> _localLockTokens;

        /// <summary>
        /// Resource lock key.
        /// </summary>
        private readonly string _lockKey;

        /// <summary>
        /// Lock ownership token.
        /// </summary>
        private readonly string _lockValue;

        /// <summary>
        /// Gets the token value associated with this local lock handle.
        /// </summary>
        internal string TokenValue => _lockValue;

        /// <summary>
        /// Flag tracking whether disposal has occurred.
        /// </summary>
        private int _isDisposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="LocalLockToken"/> class.
        /// </summary>
        /// <param name="semaphore">The semaphore to release.</param>
        /// <param name="localLockTokens">The local tokens dictionary.</param>
        /// <param name="lockKey">The lock key.</param>
        /// <param name="lockValue">The lock value token.</param>
        public LocalLockToken(
            SemaphoreSlim semaphore,
            ConcurrentDictionary<string, string> localLockTokens,
            string lockKey,
            string lockValue)
        {
            _semaphore = semaphore;
            _localLockTokens = localLockTokens;
            _lockKey = lockKey;
            _lockValue = lockValue;
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            // WHAT: Releases the local semaphore after verifying ownership in constant time.
            // WHY:  Allows the next local waiting thread to acquire the lock and guarantees ownership validity.
            // HOW:  Validates ownership with FixedTimeEquals, removes key from local dictionary, and releases semaphore.
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                if (_localLockTokens.TryGetValue(_lockKey, out string? currentToken))
                {
                    byte[] expected = Encoding.UTF8.GetBytes(currentToken);
                    byte[] actual = Encoding.UTF8.GetBytes(_lockValue);
                    if (CryptographicOperations.FixedTimeEquals(expected, actual))
                    {
                        _localLockTokens.TryRemove(_lockKey, out _);
                        _semaphore.Release();
                    }
                }
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Explicitly releases an acquired lock by verifying token ownership before removal.
    /// </summary>
    /// <param name="resource">The logical resource name.</param>
    /// <param name="lockToken">The unique ownership token supplied when acquiring the lock.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the lock was owned and successfully released; otherwise, <c>false</c>.</returns>
    public async ValueTask<bool> ReleaseLockAsync(string resource, string lockToken, CancellationToken ct = default)
    {
        // WHAT: Releases a lock explicitly by validating ownership.
        // WHY:  Allows callers to release locks via token comparison without holding an IAsyncDisposable instance.
        // HOW:  Executes atomic Lua script on Valkey; if in-memory, verifies with CryptographicOperations.FixedTimeEquals.
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockToken);

        string lockKey = LockKeyPrefix + resource;
        IDatabase? db = _connectionFactory.GetDatabase();
        if (db != null)
        {
            try
            {
                RedisResult result = await db.ScriptEvaluateAsync(
                    ReleaseLuaScript,
                    new RedisKey[] { lockKey },
                    new RedisValue[] { lockToken }).ConfigureAwait(false);

                return (int)result == 1;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to execute atomic Lua release for '{LockKey}'.", lockKey);
            }
        }

        if (_localLockTokens.TryGetValue(lockKey, out string? currentToken))
        {
            byte[] expected = Encoding.UTF8.GetBytes(currentToken);
            byte[] actual = Encoding.UTF8.GetBytes(lockToken);
            if (CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                if (_localLockTokens.TryRemove(lockKey, out _))
                {
                    if (_localLocks.TryGetValue(lockKey, out SemaphoreSlim? sem))
                    {
                        sem.Release();
                    }
                    return true;
                }
            }
        }

        return false;
    }
}
