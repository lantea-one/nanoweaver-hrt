namespace NanoWeaverHrt.Configuration;

/// <summary>
/// Defines database persistence operations for system configuration overrides and single-use
/// collaborative sharing keys. Enforces the strict rule: "Whatever is in the database wins,
/// stack environment variables do nothing if configuration exists in the database."
/// </summary>
public interface IConfigurationRepository
{
    /// <summary>
    /// Gets the human-readable name of the active database backend (e.g., 'PostgreSQL (pg-duckdb)' or 'Local SQLite Fallback').
    /// </summary>
    string ActiveBackendName { get; }

    /// <summary>
    /// Initializes database tables and schemas if they do not already exist.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the initialization operation.</returns>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves all configuration settings, evaluating database overrides against environment variables and defaults.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only collection of all defined configuration items.</returns>
    Task<IReadOnlyList<ConfigurationItem>> GetAllConfigurationsAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves the effective value for a specific configuration key, prioritizing database overrides first.
    /// </summary>
    /// <param name="key">The configuration key name.</param>
    /// <param name="defaultValue">The fallback value if neither database nor environment defines one.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The effective configuration value.</returns>
    Task<string> GetValueAsync(string key, string defaultValue = "", CancellationToken ct = default);

    /// <summary>
    /// Writes or updates a configuration setting in the database, establishing it as the authoritative override.
    /// </summary>
    /// <param name="key">The configuration key name.</param>
    /// <param name="value">The new value to persist.</param>
    /// <param name="description">Optional description of the setting.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the database write operation.</returns>
    Task SetDatabaseOverrideAsync(string key, string value, string description = "", CancellationToken ct = default);

    /// <summary>
    /// Removes a configuration override from the database, allowing the system to revert to environment variables or defaults.
    /// </summary>
    /// <param name="key">The configuration key name to revert.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the deletion operation.</returns>
    Task RevertToDefaultAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Generates a cryptographically secure, single-use share key and persists it to the database.
    /// </summary>
    /// <param name="label">Descriptive label identifying the intended collaborator or node.</param>
    /// <param name="lifetime">Duration before the invitation expires.</param>
    /// <param name="permissions">Access level granted upon redemption.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The newly created share key record.</returns>
    Task<ShareKeyRecord> CreateShareKeyAsync(string label, TimeSpan lifetime, string permissions = "FullCollaborator", CancellationToken ct = default);

    /// <summary>
    /// Validates and atomically consumes a single-use share key, establishing an authenticated peer connection.
    /// </summary>
    /// <param name="keyToken">The secret token provided by the external connecting instance.</param>
    /// <param name="remotePeerInfo">Network address or client metadata of the connecting instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The redemption outcome including session credentials if successful.</returns>
    Task<ShareKeyConsumeResult> ConsumeShareKeyAsync(string keyToken, string remotePeerInfo, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all generated share keys, including active, consumed, and expired keys.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of share key records.</returns>
    Task<IReadOnlyList<ShareKeyRecord>> ListShareKeysAsync(CancellationToken ct = default);

    /// <summary>
    /// Manually revokes an active share key before its natural expiration.
    /// </summary>
    /// <param name="tokenId">Unique identifier of the token to revoke.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the token was found and revoked, false otherwise.</returns>
    Task<bool> RevokeShareKeyAsync(string tokenId, CancellationToken ct = default);
}
