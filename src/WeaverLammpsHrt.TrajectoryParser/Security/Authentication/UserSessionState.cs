using WeaverLammpsHrt.TrajectoryParser.Caching;
using WeaverLammpsHrt.TrajectoryParser.Events;
using WeaverLammpsHrt.TrajectoryParser.Events.Models;

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
    /// Optional event bus for publishing session lifecycle events.
    /// </summary>
    private readonly IInternalEventBus? _eventBus;

    /// <summary>
    /// Optional cache service for recording session heartbeats and active tokens.
    /// </summary>
    private readonly IValkeyCacheService? _cacheService;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserSessionState"/> class.
    /// </summary>
    /// <param name="eventBus">Optional internal event bus.</param>
    /// <param name="cacheService">Optional distributed or in-memory cache service.</param>
    public UserSessionState(IInternalEventBus? eventBus = null, IValkeyCacheService? cacheService = null)
    {
        _eventBus = eventBus;
        _cacheService = cacheService;
    }

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
    /// Gets the UTC timestamp of the most recent detected user interaction or activity.
    /// </summary>
    public DateTimeOffset LastActivityUtc { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the idle inactivity duration before the session automatically expires.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Records user interaction and refreshes the activity timestamp.
    /// </summary>
    public void RecordActivity()
    {
        // WHAT: Refreshes the last active timestamp to current UTC time.
        // WHY:  Resets idle tracking so active users remain signed in during research workflows.
        // HOW:  Assigns DateTimeOffset.UtcNow to the tracked property and records in cache.
        LastActivityUtc = DateTimeOffset.UtcNow;

        if (CurrentUser != null && _cacheService != null)
        {
            _ = _cacheService.SetAsync($"heartbeat:{CurrentUser.Username}", LastActivityUtc.ToString("O"), IdleTimeout);
        }
    }

    /// <summary>
    /// Determines whether the session has exceeded the specified or default idle timeout duration.
    /// </summary>
    /// <param name="customTimeout">Optional explicit timeout threshold to test.</param>
    /// <returns>True if an authenticated session has timed out; otherwise, false.</returns>
    public bool IsExpired(TimeSpan? customTimeout = null)
    {
        // WHAT: Compares elapsed inactivity against the configured idle threshold.
        // WHY:  Provides a deterministic check for browser session expiry.
        // HOW:  Calculates the delta between UtcNow and LastActivityUtc.
        if (!IsAuthenticated)
        {
            return false;
        }

        TimeSpan threshold = IdleTimeout;
        if (customTimeout.HasValue)
        {
            threshold = customTimeout.Value;
        }

        return (DateTimeOffset.UtcNow - LastActivityUtc) > threshold;
    }

    /// <summary>
    /// Checks if the session has expired due to idle inactivity, and automatically signs out if expired.
    /// </summary>
    /// <param name="customTimeout">Optional explicit timeout threshold to test.</param>
    /// <returns>True if the user was signed out due to inactivity; otherwise, false.</returns>
    public bool CheckIdleTimeout(TimeSpan? customTimeout = null)
    {
        // WHAT: Enforces automatic sign-out when idle duration exceeds the configured threshold.
        // WHY:  Protects sensitive research, patient genomic payloads, and admin privileges on abandoned terminals.
        // HOW:  Checks IsExpired; if true, emits IdleTimeout event, signs out the user, and returns true.
        if (IsExpired(customTimeout))
        {
            if (CurrentUser != null && _eventBus != null)
            {
                _ = _eventBus.PublishAsync(new UserSessionActivityEvent(
                    CurrentUser.Username,
                    CurrentUser.Role.ToString(),
                    "IdleTimeout",
                    CurrentUser.AccessGate.ToString()));
            }

            SignOut();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Sets the active authenticated user and notifies state change listeners.
    /// </summary>
    /// <param name="user">The authenticated user account.</param>
    public void SetUser(UserAccount? user)
    {
        CurrentUser = user;
        LastActivityUtc = DateTimeOffset.UtcNow;

        if (user != null)
        {
            if (_eventBus != null)
            {
                _ = _eventBus.PublishAsync(new UserSessionActivityEvent(
                    user.Username,
                    user.Role.ToString(),
                    "SignIn",
                    user.AccessGate.ToString()));
            }

            if (_cacheService != null)
            {
                _ = _cacheService.SetAsync($"session:{user.Username}", user.Role.ToString(), IdleTimeout);
            }
        }

        NotifyStateChanged();
    }

    /// <summary>
    /// Signs out the current user session and clears authentication state.
    /// </summary>
    public void SignOut()
    {
        UserAccount? prevUser = CurrentUser;
        CurrentUser = null;

        if (prevUser != null)
        {
            if (_eventBus != null)
            {
                _ = _eventBus.PublishAsync(new UserSessionActivityEvent(
                    prevUser.Username,
                    prevUser.Role.ToString(),
                    "SignOut",
                    prevUser.AccessGate.ToString()));
            }

            if (_cacheService != null)
            {
                _ = _cacheService.RemoveAsync($"session:{prevUser.Username}");
            }
        }

        NotifyStateChanged();
    }

    /// <summary>
    /// Dispatches state change notifications to registered Blazor subscribers.
    /// </summary>
    private void NotifyStateChanged() => OnChange?.Invoke();
}
