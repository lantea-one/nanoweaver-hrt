using StackExchange.Redis;

namespace WeaverLammpsHrt.TrajectoryParser.Caching;

/// <summary>
/// Connection manager contract providing multiplexed connectivity to an external Valkey
/// (or Redis-compatible) high-throughput in-memory datastore.
/// </summary>
public interface IValkeyConnectionFactory : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Gets a value indicating whether a live multiplexed connection to Valkey is active and healthy.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets the sanitized endpoint address or host descriptor (with sensitive credentials redacted).
    /// </summary>
    string SanitizedEndpoint { get; }

    /// <summary>
    /// Retrieves the active <see cref="IConnectionMultiplexer"/> instance, or null if unconfigured or offline.
    /// </summary>
    /// <returns>The multiplexer instance, or null.</returns>
    IConnectionMultiplexer? GetConnection();

    /// <summary>
    /// Retrieves a Valkey database client instance for key-value, hash, and stream operations.
    /// </summary>
    /// <param name="db">The logical database index to target (default is -1 for default database).</param>
    /// <returns>An <see cref="IDatabase"/> instance, or null if Valkey is offline.</returns>
    IDatabase? GetDatabase(int db = -1);

    /// <summary>
    /// Retrieves a Valkey pub/sub subscriber instance for real-time channel broadcasting and streaming.
    /// </summary>
    /// <returns>An <see cref="ISubscriber"/> instance, or null if Valkey is offline.</returns>
    ISubscriber? GetSubscriber();
}
