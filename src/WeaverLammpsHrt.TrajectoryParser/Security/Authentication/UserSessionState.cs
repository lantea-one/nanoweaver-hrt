namespace WeaverLammpsHrt.TrajectoryParser.Security.Authentication;

/// <summary>
/// Scoped session state container tracking the authenticated user identity within Blazor Interactive Server circuits.
/// </summary>
public sealed class UserSessionState
{
    /// <summary>
    /// Event triggered when authentication state changes.
    /// </summary>
    public event Action? OnChange;

    /// <summary>
    /// Gets the currently authenticated user account, or null if unauthenticated.
    /// </summary>
    public UserAccount? CurrentUser { get; private set; }

    /// <summary>
    /// Gets a value indicating whether an active authenticated user session exists.
    /// </summary>
    public bool IsAuthenticated => CurrentUser != null;

    /// <summary>
    /// Gets a value indicating whether the active user possesses the Administrator role.
    /// </summary>
    public bool IsAdmin => CurrentUser?.Role == UserRole.Administrator;

    /// <summary>
    /// Gets a value indicating whether the active user possesses the Researcher role.
    /// </summary>
    public bool IsResearcher => CurrentUser?.Role == UserRole.Researcher;

    /// <summary>
    /// Gets a value indicating whether the active user possesses the Patient role.
    /// </summary>
    public bool IsPatient => CurrentUser?.Role == UserRole.Patient;

    /// <summary>
    /// Sets the active authenticated user and notifies state change listeners.
    /// </summary>
    /// <param name="user">The authenticated user account.</param>
    public void SetUser(UserAccount? user)
    {
        CurrentUser = user;
        NotifyStateChanged();
    }

    /// <summary>
    /// Signs out the current user session and clears authentication state.
    /// </summary>
    public void SignOut()
    {
        CurrentUser = null;
        NotifyStateChanged();
    }

    /// <summary>
    /// Dispatches state change notifications to registered Blazor subscribers.
    /// </summary>
    private void NotifyStateChanged() => OnChange?.Invoke();
}
