using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// In-memory, thread-safe implementation of <see cref="IServerSessionActivityTracker"/> holding the
/// authoritative idle clock for every issued bearer token.
/// </summary>
/// <remarks>
/// Sessions are keyed by the SHA-256 digest of the bearer token rather than by the token itself, so a memory
/// dump of this node never yields a usable credential. Expired sessions are kept as tombstones for longer than
/// the absolute token lifetime: forgetting a session would otherwise re-admit the very token that just expired.
/// </remarks>
public sealed class ServerSessionActivityTracker : IServerSessionActivityTracker
{
    /// <summary>
    /// Mutable per-session record holding the authoritative last-activity timestamp and revocation tombstone.
    /// </summary>
    /// <remarks>
    /// Field writes race benignly across concurrent requests: the timestamp is last-writer-wins (both writers
    /// are recording activity that really happened) and <see cref="IsRevoked"/> is only ever set to
    /// <c>true</c>, so no interleaving can un-revoke a dead session.
    /// </remarks>
    private sealed class TrackedSession
    {
        /// <summary>
        /// UTC timestamp of the most recent recorded activity, seeded from the token's issuance time.
        /// </summary>
        public DateTimeOffset LastActivityUtc;

        /// <summary>
        /// Indicates whether the session has been permanently revoked (idle-expired or signed out).
        /// </summary>
        public bool IsRevoked;
    }

    /// <summary>
    /// Live and tombstoned sessions, keyed by the hexadecimal SHA-256 digest of the bearer token.
    /// </summary>
    private readonly ConcurrentDictionary<string, TrackedSession> _sessions = new(StringComparer.Ordinal);

    /// <summary>
    /// Clock abstraction supplying the current UTC time.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Inactivity duration after which a tracked session expires.
    /// </summary>
    private readonly TimeSpan _idleTimeout;

    /// <summary>
    /// UTC timestamp of the most recent housekeeping sweep.
    /// </summary>
    private DateTimeOffset _lastPruneUtc;

    /// <summary>
    /// Minimum interval between housekeeping sweeps of the session registry.
    /// </summary>
    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Age after which a session record (including a tombstone) is discarded.
    /// </summary>
    /// <remarks>
    /// Deliberately longer than the 8-hour absolute bearer token lifetime, so a token can never outlive the
    /// tombstone that rejects it.
    /// </remarks>
    private static readonly TimeSpan SessionRetention = TimeSpan.FromHours(12);

    /// <summary>
    /// Default inactivity window mandated by Prime Directive 12.
    /// </summary>
    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerSessionActivityTracker"/> class.
    /// </summary>
    /// <param name="timeProvider">Clock abstraction; defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="idleTimeout">Inactivity window; defaults to 15 minutes.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="idleTimeout"/> is not positive.</exception>
    public ServerSessionActivityTracker(TimeProvider? timeProvider = null, TimeSpan? idleTimeout = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _idleTimeout = idleTimeout ?? DefaultIdleTimeout;

        if (_idleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleTimeout), "Idle timeout must be a positive duration.");
        }

        _lastPruneUtc = _timeProvider.GetUtcNow();
    }

    /// <inheritdoc/>
    public TimeSpan IdleTimeout => _idleTimeout;

    /// <inheritdoc/>
    public int TrackedSessionCount => _sessions.Count;

    /// <inheritdoc/>
    public bool IsSessionActive(string sessionToken)
    {
        // WHAT: Read-only liveness check of the session behind a presented bearer token.
        // WHY:  Presenting a token proves possession, not presence - a page reload, a second
        //       tab or a stolen copy all present the same token, so this must never extend
        //       the idle window (that is exactly how the reload bypass used to work).
        // HOW:  Resolves (or lazily seeds) the session record, then evaluates liveness.
        DateTimeOffset now = ResolveNow(sessionToken);
        return IsLive(ResolveSession(sessionToken, now), now);
    }

    /// <inheritdoc/>
    public bool RecordActivity(string sessionToken)
    {
        // WHAT: Extends the idle window of a live session.
        // WHY:  A researcher actively driving the UI or the API must not be signed out mid-workflow.
        // HOW:  Evaluates liveness first so a dead session is never resurrected, then stamps the clock.
        DateTimeOffset now = ResolveNow(sessionToken);
        TrackedSession session = ResolveSession(sessionToken, now);

        if (!IsLive(session, now))
        {
            return false;
        }

        session.LastActivityUtc = now;
        return true;
    }

    /// <inheritdoc/>
    public bool Revoke(string sessionToken)
    {
        // WHAT: Permanently tombstones a session.
        // WHY:  Sign-out and idle sign-out must kill the credential itself, not just the browser's
        //       copy of it, so a token captured beforehand is worthless afterwards.
        // HOW:  Evaluates liveness (which tombstones an already-elapsed session), then sets the flag.
        DateTimeOffset now = ResolveNow(sessionToken);
        TrackedSession session = ResolveSession(sessionToken, now);

        if (!IsLive(session, now))
        {
            return false;
        }

        session.IsRevoked = true;
        return true;
    }

    /// <summary>
    /// Validates the supplied token, samples the clock, and runs registry housekeeping.
    /// </summary>
    /// <param name="sessionToken">The bearer token supplied by the caller.</param>
    /// <returns>The current UTC timestamp.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sessionToken"/> is null, empty, or whitespace.</exception>
    private DateTimeOffset ResolveNow(string sessionToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        PruneStaleSessions(now);
        return now;
    }

    /// <summary>
    /// Evaluates whether a session is still inside its idle window, tombstoning it when it is not.
    /// </summary>
    /// <param name="session">The tracked session record.</param>
    /// <param name="now">The current UTC timestamp.</param>
    /// <returns><c>true</c> while the session is live; otherwise, <c>false</c>.</returns>
    private bool IsLive(TrackedSession session, DateTimeOffset now)
    {
        // WHAT: The single place where "is this session still alive?" is decided.
        // WHY:  Liveness must be identical for presentation, heartbeat and revocation, and the
        //       transition to dead must be recorded rather than recomputed - a forgotten session
        //       would otherwise be re-seeded as brand new on the next presentation.
        // HOW:  Honours an existing tombstone, otherwise compares elapsed inactivity against the
        //       configured window and writes the tombstone on the first crossing.
        if (session.IsRevoked)
        {
            return false;
        }

        if (now - session.LastActivityUtc > _idleTimeout)
        {
            session.IsRevoked = true;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Retrieves the tracked session for a bearer token, seeding a new record on first sight.
    /// </summary>
    /// <param name="sessionToken">The bearer token identifying the session.</param>
    /// <param name="now">The current UTC timestamp.</param>
    /// <returns>The tracked session record.</returns>
    /// <remarks>
    /// A first-sight record is seeded from the token's own issuance claim, never from the current time.
    /// That makes the registry fail closed: a token that was minted hours ago but has never been seen by
    /// this node (for example after a process restart, or a copy harvested from a browser) is already
    /// outside its idle window and is rejected instead of being handed a fresh 15 minutes.
    /// </remarks>
    private TrackedSession ResolveSession(string sessionToken, DateTimeOffset now)
    {
        string key = ComputeSessionKey(sessionToken);
        return _sessions.GetOrAdd(key, _ => new TrackedSession
        {
            LastActivityUtc = ResolveSessionStartUtc(sessionToken, now)
        });
    }

    /// <summary>
    /// Computes the registry key for a bearer token.
    /// </summary>
    /// <param name="sessionToken">The bearer token identifying the session.</param>
    /// <returns>The uppercase hexadecimal SHA-256 digest of the token.</returns>
    private static string ComputeSessionKey(string sessionToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionToken)));

    /// <summary>
    /// Determines the instant from which a newly tracked session's idle clock runs.
    /// </summary>
    /// <param name="sessionToken">The bearer token identifying the session.</param>
    /// <param name="now">The current UTC timestamp, used when the token carries no readable issuance claim.</param>
    /// <returns>The token's issuance timestamp, or <paramref name="now"/> when it cannot be read.</returns>
    private static DateTimeOffset ResolveSessionStartUtc(string sessionToken, DateTimeOffset now)
    {
        // WHAT: Reads the "iat" (issued-at) claim out of the token payload.
        // WHY:  The issuance time is the only trustworthy starting point for a session this node has
        //       never seen; using the current time instead would reward an attacker for presenting a
        //       harvested token to a freshly restarted process.
        // HOW:  Splits off the payload segment ahead of the '.' separator, Base64Url-decodes it, and
        //       deserializes the sealed claims. Any unreadable payload falls back to the current time,
        //       which only ever happens for tokens that fail signature verification anyway.
        int separatorIndex = sessionToken.IndexOf('.');
        if (separatorIndex <= 0)
        {
            return now;
        }

        try
        {
            byte[] payloadBytes = Convert.FromBase64String(Base64UrlDecode(sessionToken[..separatorIndex]));
            AuthTokenClaims? claims = JsonSerializer.Deserialize<AuthTokenClaims>(payloadBytes);
            if (claims == null)
            {
                return now;
            }

            return DateTimeOffset.FromUnixTimeSeconds(claims.IssuedAtUnixSeconds);
        }
        catch
        {
            // WHY: Malformed Base64, malformed JSON and out-of-range epoch seconds are all simply
            //      "no readable issuance claim"; the caller must not see a cryptic parser exception.
            return now;
        }
    }

    /// <summary>
    /// Converts Base64Url text into standard Base64 text.
    /// </summary>
    /// <param name="base64Url">The Base64Url-encoded segment.</param>
    /// <returns>Standard Base64 text with padding restored.</returns>
    private static string Base64UrlDecode(string base64Url)
    {
        string standard = base64Url.Replace('-', '+').Replace('_', '/');
        return (standard.Length % 4) switch
        {
            2 => standard + "==",
            3 => standard + "=",
            _ => standard
        };
    }

    /// <summary>
    /// Discards session records that are older than the retention window.
    /// </summary>
    /// <param name="now">The current UTC timestamp.</param>
    private void PruneStaleSessions(DateTimeOffset now)
    {
        // WHAT: Rate-limited housekeeping sweep of the session registry.
        // WHY:  Tombstones must outlive the tokens they reject, but they must not accumulate forever
        //       on a long-running node; the sweep is throttled so a burst of requests cannot turn it
        //       into an O(sessions) cost per request.
        // HOW:  Runs at most once per PruneInterval and removes every record whose last activity is
        //       older than SessionRetention (well beyond the absolute token lifetime).
        if (now - _lastPruneUtc < PruneInterval)
        {
            return;
        }

        _lastPruneUtc = now;

        foreach (KeyValuePair<string, TrackedSession> entry in _sessions)
        {
            if (now - entry.Value.LastActivityUtc > SessionRetention)
            {
                _sessions.TryRemove(entry.Key, out _);
            }
        }
    }
}
