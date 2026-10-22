namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Authoritative server-side registry of per-session last-activity timestamps enforcing idle expiration
/// of issued bearer tokens independently of any client-side state.
/// </summary>
/// <remarks>
/// Prime Directive 12 requires browser sessions to sign out after idle inactivity and requires that expiry
/// to be "validated server-side". Browser storage cannot be trusted with that decision: clearing it, copying
/// the token to another machine, or simply reloading the page would otherwise hand the session straight back.
/// This tracker therefore keeps the only authoritative idle clock. A session's clock starts when its token was
/// issued, advances only when genuine activity is recorded, and once it elapses the session is tombstoned so the
/// same token can never be revived.
/// </remarks>
public interface IServerSessionActivityTracker
{
    /// <summary>
    /// Gets the inactivity duration after which a tracked session is considered expired.
    /// </summary>
    TimeSpan IdleTimeout { get; }

    /// <summary>
    /// Gets the number of sessions (live and tombstoned) currently held in the registry.
    /// </summary>
    int TrackedSessionCount { get; }

    /// <summary>
    /// Determines whether the session behind a bearer token is still within its idle window,
    /// tombstoning it when the window has elapsed.
    /// </summary>
    /// <param name="sessionToken">The bearer token presented by the caller.</param>
    /// <returns><c>true</c> while the session is live; <c>false</c> once it is idle-expired or revoked.</returns>
    /// <remarks>
    /// Deliberately does <b>not</b> extend the idle window: merely presenting a token - which is all a page
    /// reload does - must never buy the session more time. Only <see cref="RecordActivity"/> does that.
    /// </remarks>
    bool IsSessionActive(string sessionToken);

    /// <summary>
    /// Records genuine user or client activity against a session, extending its idle window.
    /// </summary>
    /// <param name="sessionToken">The bearer token identifying the session.</param>
    /// <returns><c>true</c> if the window was extended; <c>false</c> if the session was already idle-expired or revoked.</returns>
    /// <remarks>
    /// An expired or revoked session is never resurrected, so a browser that keeps firing DOM heartbeats after
    /// the idle window closed gains nothing.
    /// </remarks>
    bool RecordActivity(string sessionToken);

    /// <summary>
    /// Immediately and permanently revokes a session, regardless of its remaining idle window.
    /// </summary>
    /// <param name="sessionToken">The bearer token identifying the session.</param>
    /// <returns><c>true</c> if a live session was revoked; <c>false</c> if it was already revoked or expired.</returns>
    bool Revoke(string sessionToken);
}
