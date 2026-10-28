namespace NanoWeaverHrt.Client.Storage;

/// <summary>
/// Provides CRUD persistence for local and remote nanoweaver-hrt instance profiles.
/// </summary>
public interface IInstanceProfileRepository
{
    /// <summary>
    /// Initializes the local SQLite schema if it does not already exist.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Lists all configured instance profiles ordered by display name.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Read-only list of saved instance profiles.</returns>
    Task<IReadOnlyList<InstanceProfile>> ListProfilesAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves a single instance profile by its unique identifier.
    /// </summary>
    /// <param name="instanceId">The unique instance identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matching profile, or null if not found.</returns>
    Task<InstanceProfile?> GetProfileAsync(string instanceId, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates an instance connection profile.
    /// </summary>
    /// <param name="profile">The profile to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpsertProfileAsync(InstanceProfile profile, CancellationToken ct = default);

    /// <summary>
    /// Updates the last connected timestamp for an existing instance profile.
    /// </summary>
    /// <param name="instanceId">The unique instance identifier.</param>
    /// <param name="connectedAtUtc">The UTC connection timestamp.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if a row was updated; otherwise false.</returns>
    Task<bool> UpdateLastConnectedAsync(string instanceId, DateTimeOffset connectedAtUtc, CancellationToken ct = default);

    /// <summary>
    /// Deletes an instance profile by its unique identifier.
    /// </summary>
    /// <param name="instanceId">The unique instance identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the profile existed and was removed; otherwise false.</returns>
    Task<bool> DeleteProfileAsync(string instanceId, CancellationToken ct = default);
}
