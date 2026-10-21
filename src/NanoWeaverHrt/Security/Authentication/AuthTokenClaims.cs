using System.ComponentModel;
using System.Text.Json.Serialization;

namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Encapsulates authorization claims cryptographically sealed inside an authenticated bearer token.
/// </summary>
public sealed record AuthTokenClaims
{
    /// <summary>
    /// Gets the authenticated username.
    /// </summary>
    [Description("Authenticated account username.")]
    [JsonPropertyName("sub")]
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Gets the assigned authorization role.
    /// </summary>
    [Description("Granted authorization role.")]
    [JsonPropertyName("role")]
    public UserRole Role { get; init; } = UserRole.Researcher;

    /// <summary>
    /// Gets the assigned access gate.
    /// </summary>
    [Description("Granted access gating boundary.")]
    [JsonPropertyName("gate")]
    public AccessGate AccessGate { get; init; } = AccessGate.ApiWeb;

    /// <summary>
    /// Gets the UTC timestamp when the token was issued.
    /// </summary>
    [Description("UTC issuance timestamp.")]
    [JsonPropertyName("iat")]
    public long IssuedAtUnixSeconds { get; init; }

    /// <summary>
    /// Gets the UTC expiration timestamp in Unix epoch seconds.
    /// </summary>
    [Description("UTC expiration timestamp in Unix epoch seconds.")]
    [JsonPropertyName("exp")]
    public long ExpiresAtUnixSeconds { get; init; }
}
