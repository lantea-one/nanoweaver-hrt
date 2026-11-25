namespace NanoWeaverHrt.Ui;

/// <summary>
/// Repository contract managing per-user Web UI display mode preferences in <c>weaver_user_preferences</c>.
/// </summary>
public interface IUserUiPreferenceRepository
{
    /// <summary>
    /// Retrieves the saved UI display preference record for the specified username, or <see langword="null"/> if unset.
    /// </summary>
    /// <param name="username">The account username whose preference is queried.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stored <see cref="UserUiPreferenceRecord"/>, or <see langword="null"/> when no personal override exists.</returns>
    Task<UserUiPreferenceRecord?> GetByUsernameAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a user's preferred UI display mode across all supported database backends.
    /// </summary>
    /// <param name="username">The account username owning the preference.</param>
    /// <param name="displayMode">The selected UI display mode (<see cref="UiDisplayMode.Simple"/> or <see cref="UiDisplayMode.Advanced"/>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted <see cref="UserUiPreferenceRecord"/>.</returns>
    Task<UserUiPreferenceRecord> UpsertAsync(string username, UiDisplayMode displayMode, CancellationToken ct = default);

    /// <summary>
    /// Deletes any saved personal display preference for the specified username so the account reverts to node defaults.
    /// </summary>
    /// <param name="username">The account username whose personal preference is removed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> when a saved preference row existed and was deleted; otherwise <see langword="false"/>.</returns>
    Task<bool> DeleteByUsernameAsync(string username, CancellationToken ct = default);
}
