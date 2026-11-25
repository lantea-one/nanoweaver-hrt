using NanoWeaverHrt.Security.Authentication;

namespace NanoWeaverHrt.Ui;

/// <summary>
/// Application service managing effective per-user Web UI display preferences, audit logging,
/// and role-tailored Task Hub / grouped navigation catalogs.
/// </summary>
public interface IUiPreferenceService
{
    /// <summary>
    /// Reads the administrator-configured node default display mode (<c>Ui:DefaultDisplayMode</c>),
    /// defaulting to <see cref="UiDisplayMode.Simple"/> when unset or invalid.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The node-wide default <see cref="UiDisplayMode"/>.</returns>
    Task<UiDisplayMode> GetNodeDefaultDisplayModeAsync(CancellationToken ct = default);

    /// <summary>
    /// Resolves the effective UI display mode for the specified user by merging any personal database override
    /// with the node-wide default configuration.
    /// </summary>
    /// <param name="username">The authenticated user's username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="EffectiveUiPreference"/> describing the active mode and override status.</returns>
    Task<EffectiveUiPreference> GetEffectivePreferenceAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Saves the user's chosen UI display mode, logs an audit event, and returns the updated effective preference.
    /// </summary>
    /// <param name="username">The authenticated user's username.</param>
    /// <param name="displayMode">The selected display mode (<see cref="UiDisplayMode.Simple"/> or <see cref="UiDisplayMode.Advanced"/>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The newly persisted <see cref="EffectiveUiPreference"/>.</returns>
    Task<EffectiveUiPreference> SetUserDisplayModeAsync(string username, UiDisplayMode displayMode, CancellationToken ct = default);

    /// <summary>
    /// Removes the user's personal UI display override so their interface follows the node default again.
    /// </summary>
    /// <param name="username">The authenticated user's username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved <see cref="EffectiveUiPreference"/> inheriting the node default.</returns>
    Task<EffectiveUiPreference> ResetUserDisplayModeAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Returns the ordered list of Task Hub action cards visible for a given role and display mode.
    /// </summary>
    /// <param name="role">The signed-in user's authorization role.</param>
    /// <param name="displayMode">The active <see cref="UiDisplayMode"/> filtering primary vs. advanced shortcuts.</param>
    /// <returns>Role-filtered task launch card descriptors.</returns>
    IReadOnlyList<UiTaskCardDescriptor> GetTaskCardsForRole(UserRole role, UiDisplayMode displayMode);

    /// <summary>
    /// Returns grouped navigation dropdown menus filtered by the signed-in user's role for Simple Mode.
    /// </summary>
    /// <param name="role">The signed-in user's authorization role.</param>
    /// <returns>Role-appropriate grouped navigation menus.</returns>
    IReadOnlyList<UiNavGroupDescriptor> GetNavigationGroupsForRole(UserRole role);
}
