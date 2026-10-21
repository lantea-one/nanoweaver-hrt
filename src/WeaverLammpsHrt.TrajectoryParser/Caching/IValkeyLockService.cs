namespace WeaverLammpsHrt.TrajectoryParser.Caching;

/// <summary>
/// Contract providing distributed mutual exclusion locking across cluster nodes or local processes.
/// Protects single-use share keys, initial administrator setup, and concurrent simulation orchestrations.
/// </summary>
public interface IValkeyLockService
{
    /// <summary>
    /// Attempts to acquire an exclusive lock on the designated resource within the specified timeout.
    /// </summary>
    /// <param name="resource">The logical name of the resource to lock (e.g. "share_key:nanoweaver_hrt_123" or "setup_lock").</param>
    /// <param name="expiry">The automatic TTL after which the lock expires if not explicitly released.</param>
    /// <param name="timeout">The maximum duration to wait for lock acquisition before returning null.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>An <see cref="IAsyncDisposable"/> token representing lock ownership, or null if acquisition timed out.</returns>
    ValueTask<IAsyncDisposable?> AcquireLockAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan timeout,
        CancellationToken ct = default);
}
