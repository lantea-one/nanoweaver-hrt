using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace NanoWeaverHrt.Caching;

/// <summary>
/// Hybrid distributed caching service utilizing Valkey when connected, with automatic, transparent
/// fallback to a high-speed in-memory store when operating in standalone offline mode.
/// </summary>
public sealed class ValkeyCacheService : IValkeyCacheService
{
    /// <summary>
    /// Key prefix applied to all cache keys to avoid collision with other cluster services.
    /// </summary>
    private const string KeyPrefix = "weaver:cache:";

    /// <summary>
    /// Connection factory providing access to the underlying Valkey multiplexer and database.
    /// </summary>
    private readonly IValkeyConnectionFactory _connectionFactory;

    /// <summary>
    /// Thread-safe in-memory cache used as a fallback when Valkey is offline or unconfigured.
    /// </summary>
    private readonly ConcurrentDictionary<string, CacheEntry> _inMemoryCache = new();

    /// <summary>
    /// Logger instance used to record caching anomalies without propagating transient faults.
    /// </summary>
    private readonly ILogger<ValkeyCacheService> _logger;

    /// <inheritdoc/>
    public bool IsDistributed => _connectionFactory.IsConnected;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValkeyCacheService"/> class.
    /// </summary>
    /// <param name="connectionFactory">The Valkey connection factory.</param>
    /// <param name="logger">Optional logger instance.</param>
    public ValkeyCacheService(IValkeyConnectionFactory connectionFactory, ILogger<ValkeyCacheService>? logger = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? NullLogger<ValkeyCacheService>.Instance;
    }

    /// <inheritdoc/>
    public async ValueTask SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        // WHAT: Stores the item in either distributed Valkey or local fallback memory.
        // WHY:  Provides low-latency access to session heartbeats, auth challenges, and temporary state.
        // HOW:  Checks IsDistributed; serializes value to JSON and sets with expiration window.
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        TimeSpan effectiveTtl = ttl ?? TimeSpan.FromMinutes(30);
        string fullKey = KeyPrefix + key;

        IDatabase? db = _connectionFactory.GetDatabase();
        if (db != null)
        {
            try
            {
                byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(value);
                await db.StringSetAsync(fullKey, jsonBytes, (TimeSpan?)effectiveTtl, When.Always).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write key '{Key}' to Valkey. Writing to local in-memory fallback.", key);
            }
        }

        // Local in-memory fallback
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.Add(effectiveTtl);
        _inMemoryCache[fullKey] = new CacheEntry(value, expiresAt);
    }

    /// <inheritdoc/>
    public async ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        // WHAT: Retrieves and deserializes the cached item, checking expiration.
        // WHY:  Shields databases and disk I/O from redundant lookups.
        // HOW:  Queries Valkey first; if unavailable, checks local concurrent dictionary and expiry time.
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        string fullKey = KeyPrefix + key;

        IDatabase? db = _connectionFactory.GetDatabase();
        if (db != null)
        {
            try
            {
                RedisValue result = await db.StringGetAsync(fullKey).ConfigureAwait(false);
                if (result.HasValue && !result.IsNullOrEmpty)
                {
                    byte[] bytes = (byte[])result!;
                    return JsonSerializer.Deserialize<T>(bytes);
                }
                return default;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read key '{Key}' from Valkey. Falling back to local memory.", key);
            }
        }

        // Local in-memory fallback
        if (_inMemoryCache.TryGetValue(fullKey, out CacheEntry? entry))
        {
            if (DateTimeOffset.UtcNow <= entry.ExpiresAtUtc)
            {
                if (entry.Value is T typedValue)
                {
                    return typedValue;
                }

                if (entry.Value != null)
                {
                    // Convert through JSON if types differ
                    byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(entry.Value);
                    return JsonSerializer.Deserialize<T>(bytes);
                }
            }
            else
            {
                _inMemoryCache.TryRemove(fullKey, out _);
            }
        }

        return default;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> RemoveAsync(string key, CancellationToken ct = default)
    {
        // WHAT: Evicts the key from both Valkey and local in-memory storage.
        // WHY:  Ensures invalidation takes effect across both tiers.
        // HOW:  Invokes KeyDeleteAsync on Valkey and TryRemove on local dictionary.
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        string fullKey = KeyPrefix + key;
        bool removed = false;

        IDatabase? db = _connectionFactory.GetDatabase();
        if (db != null)
        {
            try
            {
                removed = await db.KeyDeleteAsync(fullKey).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete key '{Key}' from Valkey.", key);
            }
        }

        bool inMemRemoved = _inMemoryCache.TryRemove(fullKey, out _);
        return removed || inMemRemoved;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        // WHAT: Checks existence and validity of the key.
        // WHY:  Determines whether cache warming or database retrieval is necessary.
        // HOW:  Queries Valkey KeyExistsAsync or checks unexpired presence in local dictionary.
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        string fullKey = KeyPrefix + key;

        IDatabase? db = _connectionFactory.GetDatabase();
        if (db != null)
        {
            try
            {
                return await db.KeyExistsAsync(fullKey).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check key existence '{Key}' in Valkey.", key);
            }
        }

        if (_inMemoryCache.TryGetValue(fullKey, out CacheEntry? entry))
        {
            if (DateTimeOffset.UtcNow <= entry.ExpiresAtUtc)
            {
                return true;
            }

            _inMemoryCache.TryRemove(fullKey, out _);
        }

        return false;
    }

    /// <summary>
    /// Represents a cached item with an expiration timestamp for in-memory storage.
    /// </summary>
    private sealed class CacheEntry
    {
        /// <summary>
        /// Gets the stored value object.
        /// </summary>
        public object? Value { get; }

        /// <summary>
        /// Gets the UTC expiration threshold when this item becomes invalid.
        /// </summary>
        public DateTimeOffset ExpiresAtUtc { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CacheEntry"/> class.
        /// </summary>
        /// <param name="value">The value to store.</param>
        /// <param name="expiresAtUtc">The expiration timestamp.</param>
        public CacheEntry(object? value, DateTimeOffset expiresAtUtc)
        {
            Value = value;
            ExpiresAtUtc = expiresAtUtc;
        }
    }
}
