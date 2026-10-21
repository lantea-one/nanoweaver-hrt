using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Encapsulates newly generated cryptographic asymmetric key material ready for user export and storage.
/// </summary>
public sealed record GeneratedKeyBundle : IExamplesProvider<GeneratedKeyBundle>
{
    /// <summary>
    /// Gets the username for whom this keypair was generated.
    /// </summary>
    [Description("Username identifying the key owner.")]
    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Gets the authorization role assigned to this key identity.
    /// </summary>
    [Description("Authorization role assigned to the key identity.")]
    [JsonPropertyName("role")]
    public UserRole Role { get; init; } = UserRole.Researcher;

    /// <summary>
    /// Gets the access gating constraint for this key identity.
    /// </summary>
    [Description("Access gating boundary (Api, Web, ApiWeb).")]
    [JsonPropertyName("accessGate")]
    public AccessGate AccessGate { get; init; } = AccessGate.ApiWeb;

    /// <summary>
    /// Gets the cryptographic algorithm of the keypair.
    /// </summary>
    [Description("Cryptographic signature algorithm (Ed25519 or MlDsa65).")]
    [JsonPropertyName("algorithm")]
    public KeyAlgorithm Algorithm { get; init; } = KeyAlgorithm.Ed25519;

    /// <summary>
    /// Gets the Base64-encoded public key bytes registered in the system.
    /// </summary>
    [Description("Base64-encoded public key DER bytes.")]
    [JsonPropertyName("publicKeyBase64")]
    public string PublicKeyBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets the PEM-formatted public key string.
    /// </summary>
    [Description("Standard PEM-encoded public key string.")]
    [JsonPropertyName("publicKeyPem")]
    public string PublicKeyPem { get; init; } = string.Empty;

    /// <summary>
    /// Gets the PEM-formatted private key string (plain or password-encrypted).
    /// </summary>
    [Description("Standard PEM-encoded private key string (plain or password-encrypted).")]
    [JsonPropertyName("privateKeyPem")]
    public string PrivateKeyPem { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the private key is encrypted with a user-supplied password.
    /// </summary>
    [Description("Indicates whether the private key PEM is encrypted with a passphrase.")]
    [JsonPropertyName("isPasswordProtected")]
    public bool IsPasswordProtected { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="GeneratedKeyBundle"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample generated key bundle record.</returns>
    public GeneratedKeyBundle GetExamples() => new()
    {
        Username = "researcher-01",
        Role = UserRole.Researcher,
        AccessGate = AccessGate.ApiWeb,
        Algorithm = KeyAlgorithm.Ed25519,
        PublicKeyBase64 = "MCowBQYDK2VwAyEA1234567890abcdef1234567890abcdef1234567890=",
        PublicKeyPem = "-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEA1234567890abcdef1234567890abcdef1234567890=\n-----END PUBLIC KEY-----\n",
        PrivateKeyPem = "-----BEGIN PRIVATE KEY-----\nMC4CAQAwBQYDK2VwBCIEIP...SAMPLE...=\n-----END PRIVATE KEY-----\n",
        IsPasswordProtected = false
    };
}
