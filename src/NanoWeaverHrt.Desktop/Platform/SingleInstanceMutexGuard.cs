namespace NanoWeaverHrt.Desktop.Platform;

/// <summary>
/// Manages a cross-process single-instance mutex to ensure only one instance of the
/// desktop platform head runs concurrently per desktop user session.
/// </summary>
/// <remarks>
/// // WHAT: Inter-process mutex guard enforcing single-instance execution invariants.
/// // WHY:  Prevents multiple background polling daemons or tray instances from contending on ports/vaults.
/// // HOW:  Creates a named system mutex and inspects the createdNew ownership flag upon acquisition.
/// </remarks>
public sealed class SingleInstanceMutexGuard : IDisposable
{
    /// <summary>
    /// Default global mutex name identifying the sovereign NanoWeaver HRT desktop instance.
    /// </summary>
    public const string DefaultMutexName = "Global\\one.lantea.nanoweaver.hrt.singleinstance";

    /// <summary>
    /// Thread-safe registry of mutex names currently held within this process.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> ActiveProcessMutexes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Underlying operating system synchronization mutex handle.
    /// </summary>
    private readonly Mutex? _mutex;

    /// <summary>
    /// Indicates whether this instance successfully obtained mutex ownership.
    /// </summary>
    private bool _hasHandle;

    /// <summary>
    /// Indicates whether the guard instance has already been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleInstanceMutexGuard"/> class.
    /// </summary>
    /// <param name="mutexName">System-wide mutex name identifying the single-instance domain.</param>
    public SingleInstanceMutexGuard(string mutexName = DefaultMutexName)
        : this(mutexName, null)
    {
    }

    /// <summary>
    /// Internal constructor supporting injected mutex factory for deterministic fault testing.
    /// </summary>
    /// <param name="mutexName">System-wide mutex name identifying the single-instance domain.</param>
    /// <param name="mutexFactory">Optional mutex creation factory delegate.</param>
    internal SingleInstanceMutexGuard(string mutexName, Func<string, (Mutex? Mutex, bool CreatedNew)>? mutexFactory)
    {
        // WHAT: Attempt to create and acquire the single-instance synchronization mutex.
        // WHY:  Determines whether this process is the primary instance or a redundant secondary invocation.
        // HOW:  Constructs Mutex, verifies within-process uniqueness, and checks OS mutex ownership/abandonment.
        ArgumentNullException.ThrowIfNull(mutexName);
        MutexName = mutexName;

        try
        {
            if (mutexFactory != null)
            {
                var (customMutex, created) = mutexFactory(mutexName);
                _mutex = customMutex;
                _hasHandle = created;
                IsPrimaryInstance = created;
                if (created)
                {
                    ActiveProcessMutexes.TryAdd(mutexName, 1);
                }
            }
            else
            {
                if (!ActiveProcessMutexes.TryAdd(mutexName, 1))
                {
                    // WHAT: Mutex is already held by another active guard instance in this process.
                    // WHY:  Prevent recursive within-thread reentrancy from granting duplicate primary status.
                    // HOW:  Yield immediate non-primary status without querying OS mutex.
                    _hasHandle = false;
                    IsPrimaryInstance = false;
                    return;
                }

                _mutex = new Mutex(initiallyOwned: false, mutexName, out _);
                bool acquired = _mutex.WaitOne(0);
                _hasHandle = acquired;
                IsPrimaryInstance = acquired;
                if (!acquired)
                {
                    ActiveProcessMutexes.TryRemove(mutexName, out _);
                }
            }
        }
        catch (AbandonedMutexException)
        {
            _hasHandle = true;
            IsPrimaryInstance = true;
            ActiveProcessMutexes.TryAdd(mutexName, 1);
        }
        catch (Exception)
        {
            _hasHandle = false;
            IsPrimaryInstance = false;
            ActiveProcessMutexes.TryRemove(mutexName, out _);
        }
    }

    /// <summary>
    /// Gets the system-wide mutex name used by this guard.
    /// </summary>
    public string MutexName { get; }

    /// <summary>
    /// Gets a value indicating whether this process is the first/primary instance holding the lock.
    /// </summary>
    public bool IsPrimaryInstance { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        // WHAT: Release mutex ownership and dispose kernel resources.
        // WHY:  Allows subsequent process invocations to successfully acquire the primary instance lock.
        // HOW:  Guards against double disposal, calls ReleaseMutex if owned, and disposes the Mutex instance.
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hasHandle)
        {
            ActiveProcessMutexes.TryRemove(MutexName, out _);
        }

        if (_hasHandle && _mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Mutex was not owned or already released by OS.
            }
        }

        _mutex?.Dispose();
        _hasHandle = false;
    }
}
