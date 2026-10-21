using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Request payload for initializing the sovereign platform with an initial Administrator.
/// </summary>
public sealed record InitialSetupRequest : IExamplesProvider<InitialSetupRequest>
{
    /// <summary>
    /// Gets the chosen administrator username.
    /// </summary>
    [Description("Master administrator username.")]
    [JsonPropertyName("adminUsername")]
    public string AdminUsername { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Base64-encoded public key DER bytes.
    /// </summary>
    [Description("Base64-encoded public key bytes.")]
    [JsonPropertyName("publicKeyBase64")]
    public string PublicKeyBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets the asymmetric signature algorithm.
    /// </summary>
    [Description("Signature algorithm (Ed25519 or MlDsa65).")]
    [JsonPropertyName("algorithm")]
    public KeyAlgorithm Algorithm { get; init; } = KeyAlgorithm.Ed25519;

    /// <summary>
    /// Gets the access gate boundary.
    /// </summary>
    [Description("Access gating boundary (Api, Web, ApiWeb).")]
    [JsonPropertyName("accessGate")]
    public AccessGate AccessGate { get; init; } = AccessGate.ApiWeb;

    /// <summary>
    /// Generates a representative example instance of <see cref="InitialSetupRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample initial setup request.</returns>
    public InitialSetupRequest GetExamples() => new()
    {
        AdminUsername = "administrator",
        PublicKeyBase64 = "MCowBQYDK2VwAyEA1234567890abcdef1234567890abcdef1234567890=",
        Algorithm = KeyAlgorithm.Ed25519,
        AccessGate = AccessGate.ApiWeb
    };
}

/// <summary>
/// Request payload for generating a high-security keypair.
/// </summary>
public sealed record GenerateKeyRequest : IExamplesProvider<GenerateKeyRequest>
{
    /// <summary>
    /// Gets the username for whom to generate the keypair.
    /// </summary>
    [Description("Username for keypair ownership.")]
    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Gets the authorization role to assign.
    /// </summary>
    [Description("Authorization role.")]
    [JsonPropertyName("role")]
    public UserRole Role { get; init; } = UserRole.Researcher;

    /// <summary>
    /// Gets the access gating boundary.
    /// </summary>
    [Description("Access gating boundary.")]
    [JsonPropertyName("accessGate")]
    public AccessGate AccessGate { get; init; } = AccessGate.ApiWeb;

    /// <summary>
    /// Gets the cryptographic algorithm.
    /// </summary>
    [Description("Cryptographic algorithm (Ed25519 or MlDsa65).")]
    [JsonPropertyName("algorithm")]
    public KeyAlgorithm Algorithm { get; init; } = KeyAlgorithm.Ed25519;

    /// <summary>
    /// Gets the optional password to encrypt the private key PEM.
    /// </summary>
    [Description("Optional passphrase for private key encryption.")]
    [JsonPropertyName("password")]
    public string? Password { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="GenerateKeyRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample key generation request.</returns>
    public GenerateKeyRequest GetExamples() => new()
    {
        Username = "researcher-01",
        Role = UserRole.Researcher,
        AccessGate = AccessGate.ApiWeb,
        Algorithm = KeyAlgorithm.Ed25519,
        Password = "optional-key-passphrase"
    };
}

/// <summary>
/// Request payload to issue an asymmetric authentication challenge for a username.
/// </summary>
public sealed record ChallengeRequest : IExamplesProvider<ChallengeRequest>
{
    /// <summary>
    /// Gets the authenticating account username.
    /// </summary>
    [Description("Authenticating account username.")]
    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Generates a representative example instance of <see cref="ChallengeRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample challenge issuance request.</returns>
    public ChallengeRequest GetExamples() => new()
    {
        Username = "researcher-01"
    };
}

/// <summary>
/// Request payload for exchanging a signed challenge nonce for an authenticated bearer token.
/// </summary>
public sealed record TokenExchangeRequest : IExamplesProvider<TokenExchangeRequest>
{
    /// <summary>
    /// Gets the authenticating username.
    /// </summary>
    [Description("Authenticating account username.")]
    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Gets the challenge identifier returned by /api/auth/challenge.
    /// </summary>
    [Description("Challenge session identifier.")]
    [JsonPropertyName("challengeId")]
    public string ChallengeId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Base64-encoded signature of the challenge nonce.
    /// </summary>
    [Description("Base64-encoded digital signature.")]
    [JsonPropertyName("signatureBase64")]
    public string SignatureBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Generates a representative example instance of <see cref="TokenExchangeRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample token exchange request.</returns>
    public TokenExchangeRequest GetExamples() => new()
    {
        Username = "researcher-01",
        ChallengeId = "chal_9876543210abcdef",
        SignatureBase64 = "c2lnbmF0dXJlX2J5dGVzX2Jhc2U2NA=="
    };
}

/// <summary>
/// Request payload for direct private-key authentication.
/// </summary>
public sealed record KeyLoginRequest : IExamplesProvider<KeyLoginRequest>
{
    /// <summary>
    /// Gets the username attempting authentication.
    /// </summary>
    [Description("Authenticating account username.")]
    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Gets the user's private key PEM text.
    /// </summary>
    [Description("Private key PEM text (plain or encrypted).")]
    [JsonPropertyName("privateKeyPem")]
    public string PrivateKeyPem { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional password if the private key is encrypted.
    /// </summary>
    [Description("Optional passphrase if private key is encrypted.")]
    [JsonPropertyName("password")]
    public string? Password { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="KeyLoginRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample private-key login request.</returns>
    public KeyLoginRequest GetExamples() => new()
    {
        Username = "researcher-01",
        PrivateKeyPem = "-----BEGIN PRIVATE KEY-----\nMC4CAQAwBQYDK2VwBCIEIP...SAMPLE...=\n-----END PRIVATE KEY-----\n",
        Password = null
    };
}

/// <summary>
/// Request payload for registering a new user account with an existing public key.
/// </summary>
public sealed record CreateUserRequest : IExamplesProvider<CreateUserRequest>
{
    /// <summary>
    /// Gets the username to create.
    /// </summary>
    [Description("New account username.")]
    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Gets the authorization role to grant.
    /// </summary>
    [Description("Granted authorization role.")]
    [JsonPropertyName("role")]
    public UserRole Role { get; init; } = UserRole.Researcher;

    /// <summary>
    /// Gets the access gating boundary.
    /// </summary>
    [Description("Access gating boundary.")]
    [JsonPropertyName("accessGate")]
    public AccessGate AccessGate { get; init; } = AccessGate.ApiWeb;

    /// <summary>
    /// Gets the cryptographic algorithm.
    /// </summary>
    [Description("Cryptographic algorithm.")]
    [JsonPropertyName("algorithm")]
    public KeyAlgorithm Algorithm { get; init; } = KeyAlgorithm.Ed25519;

    /// <summary>
    /// Gets the Base64-encoded public key bytes.
    /// </summary>
    [Description("Base64-encoded public key bytes.")]
    [JsonPropertyName("publicKeyBase64")]
    public string PublicKeyBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Generates a representative example instance of <see cref="CreateUserRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample user creation request.</returns>
    public CreateUserRequest GetExamples() => new()
    {
        Username = "patient-402",
        Role = UserRole.Patient,
        AccessGate = AccessGate.Web,
        Algorithm = KeyAlgorithm.Ed25519,
        PublicKeyBase64 = "MCowBQYDK2VwAyEA1234567890abcdef1234567890abcdef1234567890="
    };
}

/// <summary>
/// Response payload containing an authenticated Bearer token and user account summary.
/// </summary>
public sealed record AuthTokenResponse : IExamplesProvider<AuthTokenResponse>
{
    /// <summary>
    /// Gets the authenticated Bearer token string.
    /// </summary>
    [Description("Authenticated Bearer token.")]
    [JsonPropertyName("token")]
    public string Token { get; init; } = string.Empty;

    /// <summary>
    /// Gets the token type ("Bearer").
    /// </summary>
    [Description("Token authorization scheme.")]
    [JsonPropertyName("tokenType")]
    public string TokenType { get; init; } = "Bearer";

    /// <summary>
    /// Gets the token expiration in seconds.
    /// </summary>
    [Description("Lifetime of the issued token in seconds.")]
    [JsonPropertyName("expiresInSeconds")]
    public int ExpiresInSeconds { get; init; } = 28800;

    /// <summary>
    /// Gets the authenticated user account details.
    /// </summary>
    [Description("Authenticated user account.")]
    [JsonPropertyName("user")]
    public UserAccount? User { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="AuthTokenResponse"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample token response.</returns>
    public AuthTokenResponse GetExamples() => new()
    {
        Token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.sample_payload.sample_signature",
        TokenType = "Bearer",
        ExpiresInSeconds = 28800,
        User = new UserAccount
        {
            Username = "researcher-01",
            Role = UserRole.Researcher,
            AccessGate = AccessGate.ApiWeb,
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = "MCowBQYDK2VwAyEA1234567890abcdef1234567890abcdef1234567890=",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LastLoginUtc = DateTimeOffset.UtcNow,
            IsActive = true
        }
    };
}

/// <summary>
/// Status response indicating whether the first-launch setup wizard is required.
/// </summary>
public sealed record SetupStatusResponse : IExamplesProvider<SetupStatusResponse>
{
    /// <summary>
    /// Gets a value indicating whether initial setup is required.
    /// </summary>
    [Description("True if the system requires first-launch administrator initialization; otherwise false.")]
    [JsonPropertyName("setupRequired")]
    public bool SetupRequired { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="SetupStatusResponse"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample setup status response.</returns>
    public SetupStatusResponse GetExamples() => new()
    {
        SetupRequired = false
    };
}
