namespace NanoWeaverHrt.Database.Access;

/// <summary>
/// The single place that decides which database engine a connection targets.
/// </summary>
public interface IWeaverDbConnectionFactory
{
    /// <summary>
    /// Gets the backend the next <see cref="OpenAsync"/> will target. A pure read of the decision made at startup.
    /// </summary>
    WeaverDatabaseBackend ActiveBackend { get; }

    /// <summary>
    /// Opens a connection to the active backend and applies pending migrations for its dialect.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A session the caller must dispose.</returns>
    /// <exception cref="WeaverDatabaseUnavailableException">The active backend cannot be reached or migrated.</exception>
    Task<WeaverDbSession> OpenAsync(CancellationToken ct = default);
}
