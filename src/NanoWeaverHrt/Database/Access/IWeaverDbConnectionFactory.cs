namespace NanoWeaverHrt.Database.Access;

/// <summary>
/// The single place that decides which database engine a connection targets.
/// </summary>
public interface IWeaverDbConnectionFactory
{
    /// <summary>
    /// Gets the backend the next <see cref="OpenAsync"/> will target. A pure read of the decision made by
    /// <see cref="EnsureBackendResolvedAsync"/>; SQLite until that decision has been made.
    /// </summary>
    WeaverDatabaseBackend ActiveBackend { get; }

    /// <summary>
    /// Gets the human-readable name of the resolved backend: "PostgreSQL (pg-duckdb)", "Local SQLite Fallback",
    /// or "Local SQLite Fallback (PostgreSQL configured but unreachable)".
    /// </summary>
    string ActiveBackendDisplayName { get; }

    /// <summary>
    /// Resolves the backend once per process and applies that backend's pending migrations.
    /// Idempotent and safe to call concurrently; later calls return the cached decision.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved backend.</returns>
    /// <remarks>
    /// With no PostgreSQL connection string configured, SQLite is the intended backend and nothing is logged.
    /// With one configured but unreachable, a single warning is logged and SQLite is used for this process.
    /// If the SQLite file has been deleted since the last call, its schema is re-created.
    /// </remarks>
    Task<WeaverDatabaseBackend> EnsureBackendResolvedAsync(CancellationToken ct = default);

    /// <summary>
    /// Opens a connection to the active backend and applies pending migrations for its dialect.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A session the caller must dispose.</returns>
    /// <exception cref="WeaverDatabaseUnavailableException">The active backend cannot be reached or migrated.</exception>
    Task<WeaverDbSession> OpenAsync(CancellationToken ct = default);
}
