namespace NanoWeaverHrt.Caching;

/// <summary>
/// Distributed and in-memory hybrid caching contract supporting serialization, time-to-live (TTL) expiration,
/// and automatic fallback for session heartbeats, authentication challenges, and telemetry metrics.
/// </summary>
public interface IValkeyCacheService
{
    /// <summary>
    /// Gets a value indicating whether the cache is currently backed by a distributed Valkey cluster (true)
    /// or operating in sovereign local in-memory fallback mode (false).
    /// </summary>
    bool IsDistributed { get; }

    /// <summary>
    /// Sets a value in the cache with an optional time-to-live expiration window.
    /// </summary>
    /// <typeparam name="T">The type of the item to store.</typeparam>
    /// <param name="key">The unique cache key.</param>
    /// <param name="value">The value instance to serialize and store.</param>
    /// <param name="ttl">Optional expiration lifespan; if null, defaults to 30 minutes.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a value from the cache, or default/null if expired or absent.
    /// </summary>
    /// <typeparam name="T">The expected type of the cached item.</typeparam>
    /// <param name="key">The unique cache key.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The deserialized value instance, or null if missing or expired.</returns>
    ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default);

    /// <summary>
    /// Removes a value from the cache.
    /// </summary>
    /// <param name="key">The unique cache key to remove.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>True if the key was found and removed; otherwise, false.</returns>
    ValueTask<bool> RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Determines whether the specified key exists and has not expired in the cache.
    /// </summary>
    /// <param name="key">The unique cache key to inspect.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>True if the key exists and is valid; otherwise, false.</returns>
    ValueTask<bool> ExistsAsync(string key, CancellationToken ct = default);
}
