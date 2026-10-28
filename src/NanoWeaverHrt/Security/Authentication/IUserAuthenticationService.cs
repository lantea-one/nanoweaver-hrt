namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Authoritative authentication and user identity management service enforcing private-key authentication,
/// role-based authorization, access gating, and mandatory first-launch system setup.
/// </summary>
public interface IUserAuthenticationService
{
    /// <summary>
    /// Evaluates whether the system is in an uninitialized state requiring the first-launch setup wizard.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if no active Administrator user exists; otherwise, <c>false</c>.</returns>
    Task<bool> IsSetupRequiredAsync(CancellationToken ct = default);

    /// <summary>
    /// Completes the first-launch setup wizard by creating the initial Administrator account.
    /// </summary>
    /// <param name="adminUsername">The chosen administrator username.</param>
    /// <param name="publicKeyBase64">The public key DER bytes in Base64 encoding.</param>
    /// <param name="algorithm">The asymmetric signature algorithm.</param>
    /// <param name="accessGate">The access gate boundary.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The newly created Administrator account record.</returns>
    Task<UserAccount> CompleteInitialSetupAsync(
        string adminUsername,
        string publicKeyBase64,
        KeyAlgorithm algorithm,
        AccessGate accessGate = AccessGate.ApiWeb,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a high-security asymmetric keypair (Ed25519 or ML-DSA-65) with optional password encryption.
    /// </summary>
    /// <param name="username">The username to bind to this keypair.</param>
    /// <param name="role">The assigned authorization role.</param>
    /// <param name="gate">The assigned access gating boundary.</param>
    /// <param name="algorithm">The chosen cryptographic signature algorithm.</param>
    /// <param name="password">Optional password to encrypt the private key PEM.</param>
    /// <returns>A generated key bundle containing public and private PEM strings.</returns>
    GeneratedKeyBundle GenerateSecureKeyBundle(
        string username,
        UserRole role,
        AccessGate gate,
        KeyAlgorithm algorithm,
        string? password = null);

    /// <summary>
    /// Issues a time-bounded cryptographic challenge nonce to initiate client authentication.
    /// </summary>
    /// <param name="username">The username requesting authentication.</param>
    /// <returns>A new <see cref="AuthChallenge"/>.</returns>
    AuthChallenge CreateChallenge(string username);

    /// <summary>
    /// Verifies a signed cryptographic challenge and mints a time-bounded authenticated bearer token.
    /// </summary>
    /// <param name="username">The username presenting authentication.</param>
    /// <param name="challengeId">The challenge session identifier.</param>
    /// <param name="signatureBase64">The Base64-encoded signature of the challenge nonce.</param>
    /// <param name="isWebRequest">True if authenticating for interactive Web UI session; false for API token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Authentication verdict, error message (if any), bearer token, and user record.</returns>
    Task<(bool Success, string? ErrorMessage, string? Token, UserAccount? User)> VerifyChallengeAndIssueTokenAsync(
        string username,
        string challengeId,
        string signatureBase64,
        bool isWebRequest = false,
        CancellationToken ct = default);

    /// <summary>
    /// Validates an authenticated bearer token and extracts authorized principal claims.
    /// </summary>
    /// <param name="token">The bearer token string.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Validation verdict, user account record, and error description if invalid.</returns>
    Task<(bool Valid, UserAccount? User, string? ErrorReason)> ValidateTokenAsync(
        string token,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves all registered user accounts (Administrator access only).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Read-only collection of user accounts.</returns>
    Task<IReadOnlyList<UserAccount>> ListUsersAsync(CancellationToken ct = default);

    /// <summary>
    /// Registers a new user account with a pre-existing public key.
    /// </summary>
    /// <param name="username">The username to register.</param>
    /// <param name="role">The authorization role.</param>
    /// <param name="gate">The access gate.</param>
    /// <param name="publicKeyBase64">The Base64-encoded public key bytes.</param>
    /// <param name="algorithm">The signature algorithm.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created user account.</returns>
    Task<UserAccount> CreateUserAsync(
        string username,
        UserRole role,
        AccessGate gate,
        string publicKeyBase64,
        KeyAlgorithm algorithm,
        CancellationToken ct = default);

    /// <summary>
    /// Registers a new user account only when the username is not already in use.
    /// </summary>
    /// <param name="username">The username to register.</param>
    /// <param name="role">The authorization role.</param>
    /// <param name="gate">The access gate.</param>
    /// <param name="publicKeyBase64">The Base64-encoded public key bytes.</param>
    /// <param name="algorithm">The signature algorithm.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created user account, or null when the username was already taken (nothing is changed).</returns>
    Task<UserAccount?> TryCreateUserAsync(
        string username,
        UserRole role,
        AccessGate gate,
        string publicKeyBase64,
        KeyAlgorithm algorithm,
        CancellationToken ct = default);

    /// <summary>
    /// Mints a signed bearer token for an authenticated user account.
    /// </summary>
    /// <param name="user">The user account for whom the token is minted.</param>
    /// <returns>A signed, time-bounded bearer token.</returns>
    string GenerateBearerToken(UserAccount user);

    /// <summary>
    /// Revokes and deletes a user account.
    /// </summary>
    /// <param name="username">The username of the account to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if successfully deleted; otherwise, <c>false</c>.</returns>
    Task<bool> DeleteUserAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Removes all expired authentication challenges from the active challenge cache.
    /// </summary>
    /// <param name="nowUtc">The reference UTC timestamp against which challenge expiration is evaluated.</param>
    /// <returns>The number of expired challenges removed.</returns>
    int PruneExpiredChallenges(DateTimeOffset nowUtc);
}
