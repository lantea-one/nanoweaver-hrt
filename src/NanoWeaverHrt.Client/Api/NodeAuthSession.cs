namespace NanoWeaverHrt.Client.Api;

/// <summary>
/// Represents an authenticated asymmetric PKI session with a nanoweaver-hrt node.
/// </summary>
/// <param name="Token">Signed Bearer JWT used for authenticated HTTP requests.</param>
/// <param name="TokenType">Authorization scheme type (typically "Bearer").</param>
/// <param name="ExpiresInSeconds">Lifetime of the token in seconds.</param>
/// <param name="IssuedAtUtc">UTC timestamp when the token was issued.</param>
/// <param name="ExpiresAtUtc">UTC timestamp when the token expires.</param>
/// <param name="Username">Authenticated username identity.</param>
/// <param name="Role">Authorized role on the instance (Administrator, Researcher, Patient).</param>
public sealed record NodeAuthSession(
    string Token,
    string TokenType,
    int ExpiresInSeconds,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Username,
    string Role)
{
    /// <summary>
    /// Checks whether the token has expired relative to the specified timestamp.
    /// </summary>
    /// <param name="nowUtc">The timestamp against which to compare expiration.</param>
    /// <returns>True if expired; otherwise false.</returns>
    public bool IsExpired(DateTimeOffset nowUtc) => nowUtc >= ExpiresAtUtc;

    /// <summary>
    /// Calculates the time remaining before session expiration.
    /// </summary>
    /// <param name="nowUtc">The current UTC timestamp.</param>
    /// <returns>A non-negative TimeSpan representing remaining valid duration.</returns>
    public TimeSpan TimeRemaining(DateTimeOffset nowUtc) =>
        ExpiresAtUtc > nowUtc ? ExpiresAtUtc - nowUtc : TimeSpan.Zero;
}
