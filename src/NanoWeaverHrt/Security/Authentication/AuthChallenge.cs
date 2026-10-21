using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Represents an active cryptographic challenge nonce issued to a client during the authentication handshake.
/// </summary>
public sealed record AuthChallenge : IExamplesProvider<AuthChallenge>
{
    /// <summary>
    /// Gets the unique identifier for this challenge session.
    /// </summary>
    [Description("Unique challenge session identifier.")]
    [JsonPropertyName("challengeId")]
    public string ChallengeId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the username for whom this challenge was generated.
    /// </summary>
    [Description("Username for whom the challenge was minted.")]
    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Base64-encoded cryptographically random nonce to be signed by the client's private key.
    /// </summary>
    [Description("Base64-encoded 256-bit cryptographic challenge nonce.")]
    [JsonPropertyName("nonceBase64")]
    public string NonceBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC expiration timestamp beyond which this challenge is invalid.
    /// </summary>
    [Description("UTC timestamp when this challenge expires.")]
    [JsonPropertyName("expiresAtUtc")]
    public DateTimeOffset ExpiresAtUtc { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="AuthChallenge"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample authentication challenge record.</returns>
    public AuthChallenge GetExamples() => new()
    {
        ChallengeId = "chal_9876543210abcdef",
        Username = "researcher-01",
        NonceBase64 = "YWJjZGVmZ2hpamtsbW5vcHFyc3R1dnd4eXoxMjM0NTY=",
        ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(60)
    };
}
