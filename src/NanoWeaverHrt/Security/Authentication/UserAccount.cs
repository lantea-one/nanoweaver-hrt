using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Represents a registered user identity authorized to access sovereign API or Web resources.
/// </summary>
public sealed record UserAccount : IExamplesProvider<UserAccount>
{
    /// <summary>
    /// Gets the unique username identifying this principal.
    /// </summary>
    [Description("Unique username identifying the account principal.")]
    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Gets the authorization role assigned to this user.
    /// </summary>
    [Description("Authorization role governing permissions (Administrator, Researcher, Patient).")]
    [JsonPropertyName("role")]
    public UserRole Role { get; init; } = UserRole.Researcher;

    /// <summary>
    /// Gets the access gating constraint for this account.
    /// </summary>
    [Description("Access gating boundary (Api, Web, ApiWeb).")]
    [JsonPropertyName("accessGate")]
    public AccessGate AccessGate { get; init; } = AccessGate.ApiWeb;

    /// <summary>
    /// Gets the asymmetric signature algorithm paired with the user's registered public key.
    /// </summary>
    [Description("Cryptographic signature algorithm (Ed25519 or MlDsa65).")]
    [JsonPropertyName("algorithm")]
    public KeyAlgorithm Algorithm { get; init; } = KeyAlgorithm.Ed25519;

    /// <summary>
    /// Gets the Base64-encoded public key bytes used for cryptographic signature verification.
    /// </summary>
    [Description("Base64-encoded public key DER bytes.")]
    [JsonPropertyName("publicKeyBase64")]
    public string PublicKeyBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp when this account was created.
    /// </summary>
    [Description("UTC timestamp marking user creation.")]
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the UTC timestamp of the most recent successful authentication, or null if never authenticated.
    /// </summary>
    [Description("UTC timestamp of the most recent successful login.")]
    [JsonPropertyName("lastLoginUtc")]
    public DateTimeOffset? LastLoginUtc { get; init; }

    /// <summary>
    /// Gets a value indicating whether this user account is active and permitted to authenticate.
    /// </summary>
    [Description("Indicates whether the account is currently active.")]
    [JsonPropertyName("isActive")]
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Generates a representative example instance of <see cref="UserAccount"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample user account record.</returns>
    public UserAccount GetExamples() => new()
    {
        Username = "researcher-01",
        Role = UserRole.Researcher,
        AccessGate = AccessGate.ApiWeb,
        Algorithm = KeyAlgorithm.Ed25519,
        PublicKeyBase64 = "MCowBQYDK2VwAyEA1234567890abcdef1234567890abcdef1234567890=",
        CreatedAtUtc = DateTimeOffset.UtcNow,
        LastLoginUtc = DateTimeOffset.UtcNow,
        IsActive = true
    };
}
