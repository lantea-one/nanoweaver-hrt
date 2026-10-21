namespace NanoWeaverHrt.Events.Models;

/// <summary>
/// Event emitted when an authenticated user performs an action, signs in, heartbeats, or automatically expires due to inactivity.
/// </summary>
/// <param name="Username">The unique identifier of the user account.</param>
/// <param name="Role">The assigned role of the user (Administrator, Researcher, or Patient).</param>
/// <param name="Action">The activity action performed (e.g. SignIn, Heartbeat, SignOut, IdleTimeout).</param>
/// <param name="AccessGate">The transport boundary where the activity occurred (Api, Web, or ApiWeb).</param>
public sealed record UserSessionActivityEvent(
    string Username,
    string Role,
    string Action,
    string AccessGate) : IWeaverEvent
{
    /// <inheritdoc/>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <inheritdoc/>
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <inheritdoc/>
    public string EventName => "UserSessionActivity";
}
