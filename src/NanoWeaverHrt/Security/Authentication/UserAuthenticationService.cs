using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Security.Secrets;

namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Production implementation of <see cref="IUserAuthenticationService"/> providing asymmetric private-key authentication,
/// challenge-response token exchanges, role-based access control, and first-launch setup gating.
/// </summary>
public sealed class UserAuthenticationService : IUserAuthenticationService, IDisposable
{
    /// <summary>
    /// Master user account persistence repository.
    /// </summary>
    private readonly IUserRepository _userRepository;

    /// <summary>
    /// Post-quantum cryptographic provider for ML-DSA-65 operations.
    /// </summary>
    private readonly IPostQuantumCryptoProvider _pqcProvider;

    /// <summary>
    /// Compliance audit service for FDA 21 CFR Part 11 immutable audit records.
    /// </summary>
    private readonly IComplianceAuditService _auditService;

    /// <summary>
    /// Cryptographic random number generator for challenge nonces.
    /// </summary>
    private readonly SecureRandom _secureRandom = new();

    /// <summary>
    /// Secret key bytes used to seal and verify tamper-evident bearer tokens.
    /// </summary>
    /// <remarks>
    /// Provisioned by <see cref="INodeSecretStore"/>: 256 random bits that exist only on this node and only in
    /// its owner-only key file. Held as <see cref="ReadOnlyMemory{T}"/> because this class has <c>async</c>
    /// methods, which cannot hold a <c>ReadOnlySpan&lt;byte&gt;</c> in a field.
    /// </remarks>
    private readonly ReadOnlyMemory<byte> _tokenHmacKey;

    /// <summary>
    /// Thread-safe in-memory cache of pending authentication challenges.
    /// </summary>
    private readonly ConcurrentDictionary<string, AuthChallenge> _activeChallenges = new();

    /// <summary>
    /// Lifetime duration for authentication challenge nonces before expiration (120 seconds).
    /// </summary>
    public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Default token validity period (8 hours).
    /// </summary>
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(8);

    /// <summary>
    /// Initializes a new instance of the <see cref="UserAuthenticationService"/> class.
    /// </summary>
    /// <param name="userRepository">User persistence repository.</param>
    /// <param name="pqcProvider">Post-quantum crypto provider.</param>
    /// <param name="auditService">Compliance audit ledger service.</param>
    /// <param name="secretStore">Provider of this node's persisted bearer-token signing key.</param>
    public UserAuthenticationService(
        IUserRepository userRepository,
        IPostQuantumCryptoProvider pqcProvider,
        IComplianceAuditService auditService,
        INodeSecretStore secretStore)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _pqcProvider = pqcProvider ?? throw new ArgumentNullException(nameof(pqcProvider));
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));

        ArgumentNullException.ThrowIfNull(secretStore);

        // WHAT: Adopts the node's persisted 256-bit token signing key.
        // WHY:  The previous derivation hashed the literal key identifier "node_signing_master" together with
        //       a constant suffix published in this repository, so the HMAC key was identical on every
        //       deployment on earth and anyone could mint a valid token for any username and any role.
        // HOW:  INodeSecretStore hands over key material that RandomNumberGenerator produced on first launch
        //       and that persists in an owner-only file, so tokens also survive a service restart.
        _tokenHmacKey = secretStore.TokenSigningKey;
        _pruneTimer = new Timer(OnPruneTimerTick, null, DefaultPruneInterval, DefaultPruneInterval);
    }

    /// <summary>
    /// Registers an explicit authentication challenge for testing expiration workflows.
    /// </summary>
    /// <param name="challenge">The challenge instance to register.</param>
    /// <remarks>
    /// Test-only surface reachable exclusively via <c>InternalsVisibleTo("NanoWeaverHrt.Tests")</c> so unit tests
    /// can deterministically inject pre-expired challenge nonces without <c>Thread.Sleep</c> (Directive 6).
    /// Never called by production code.
    /// </remarks>
    internal void RegisterChallengeForTesting(AuthChallenge challenge)
    {
        _activeChallenges[challenge.ChallengeId] = challenge;
    }

    /// <inheritdoc/>
    public async Task<bool> IsSetupRequiredAsync(CancellationToken ct = default)
    {
        // WHAT: Checks if any active Administrator account exists in the database.
        // WHY:  Enforces the rule that first-launch setup wizard is mandatory until an Administrator is established.
        // HOW:  Queries repository HasAnyAdministratorAsync.
        bool hasAdmin = await _userRepository.HasAnyAdministratorAsync(ct);
        return !hasAdmin;
    }

    /// <inheritdoc/>
    public async Task<UserAccount> CompleteInitialSetupAsync(
        string adminUsername,
        string publicKeyBase64,
        KeyAlgorithm algorithm,
        AccessGate accessGate = AccessGate.ApiWeb,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adminUsername);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyBase64);

        if (!await IsSetupRequiredAsync(ct))
        {
            throw new InvalidOperationException("First-launch setup is already complete. Multiple initial setup calls are forbidden.");
        }

        UserAccount adminAccount = new()
        {
            Username = NormalizeUsername(adminUsername),
            Role = UserRole.Administrator,
            AccessGate = accessGate,
            Algorithm = algorithm,
            PublicKeyBase64 = publicKeyBase64.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            IsActive = true
        };

        await _userRepository.CreateUserAsync(adminAccount, ct);

        _auditService.LogAction(
            "AUTHENTICATION",
            "InitialAdminSetupCompleted",
            $"user:{adminAccount.Username}",
            $"{{\"role\":\"Administrator\",\"algorithm\":\"{algorithm}\",\"gate\":\"{accessGate}\"}}");

        return adminAccount;
    }

    /// <inheritdoc/>
    public GeneratedKeyBundle GenerateSecureKeyBundle(
        string username,
        UserRole role,
        AccessGate gate,
        KeyAlgorithm algorithm,
        string? password = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        byte[] pubDer;
        byte[] privDer;

        // WHAT: Generates high-security asymmetric keypair according to selected algorithm.
        // WHY:  Provides cryptographically unforgeable identity for user authentication.
        // HOW:  Uses BouncyCastle Ed25519KeyPairGenerator or MLDsaKeyPairGenerator.
        if (algorithm == KeyAlgorithm.Ed25519)
        {
            Ed25519KeyPairGenerator keyGen = new();
            keyGen.Init(new Ed25519KeyGenerationParameters(_secureRandom));
            AsymmetricCipherKeyPair keyPair = keyGen.GenerateKeyPair();

            pubDer = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(keyPair.Public).GetDerEncoded();
            privDer = PrivateKeyInfoFactory.CreatePrivateKeyInfo(keyPair.Private).GetDerEncoded();
        }
        else
        {
            MLDsaKeyPairGenerator keyGen = new();
            keyGen.Init(new MLDsaKeyGenerationParameters(_secureRandom, MLDsaParameters.ml_dsa_65));
            AsymmetricCipherKeyPair keyPair = keyGen.GenerateKeyPair();

            pubDer = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(keyPair.Public).GetDerEncoded();
            privDer = PrivateKeyInfoFactory.CreatePrivateKeyInfo(keyPair.Private).GetDerEncoded();
        }

        string pubBase64 = Convert.ToBase64String(pubDer);
        string pubPem = FormatPem("PUBLIC KEY", pubDer);
        string privPem;
        bool isEncrypted = false;

        if (!string.IsNullOrWhiteSpace(password))
        {
            byte[] encryptedPriv = EncryptPrivateKeyBytes(privDer, password);
            privPem = FormatPem("ENCRYPTED PRIVATE KEY", encryptedPriv);
            isEncrypted = true;
        }
        else
        {
            privPem = FormatPem("PRIVATE KEY", privDer);
        }

        return new GeneratedKeyBundle
        {
            Username = NormalizeUsername(username),
            Role = role,
            AccessGate = gate,
            Algorithm = algorithm,
            PublicKeyBase64 = pubBase64,
            PublicKeyPem = pubPem,
            PrivateKeyPem = privPem,
            IsPasswordProtected = isEncrypted
        };
    }

    /// <inheritdoc/>
    public AuthChallenge CreateChallenge(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        // WHAT: Sweeps expired challenges and enforces the hard upper bound on active challenges before allocating a nonce.
        // WHY:  /api/auth/challenge is unauthenticated; capping _activeChallenges stops an attacker from exhausting heap memory.
        // HOW:  Prunes expired entries first, then throws ChallengeStoreCapacityExceededException if still at or above MaxActiveChallenges.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        PruneExpiredChallenges(now);

        if (_activeChallenges.Count >= MaxActiveChallenges)
        {
            throw new ChallengeStoreCapacityExceededException(MaxActiveChallenges);
        }

        byte[] nonce = new byte[32];
        RandomNumberGenerator.Fill(nonce);

        string challengeId = Guid.NewGuid().ToString("N");
        AuthChallenge challenge = new()
        {
            ChallengeId = challengeId,
            Username = NormalizeUsername(username),
            NonceBase64 = Convert.ToBase64String(nonce),
            ExpiresAtUtc = now.Add(ChallengeLifetime)
        };

        _activeChallenges[challengeId] = challenge;
        return challenge;
    }

    /// <inheritdoc/>
    public async Task<(bool Success, string? ErrorMessage, string? Token, UserAccount? User)> VerifyChallengeAndIssueTokenAsync(
        string username,
        string challengeId,
        string signatureBase64,
        bool isWebRequest = false,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(challengeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(signatureBase64);

        if (await IsSetupRequiredAsync(ct))
        {
            return (false, "First-launch setup required. Please complete initial setup at /setup.", null, null);
        }

        if (!_activeChallenges.TryRemove(challengeId, out AuthChallenge? challenge) || challenge == null)
        {
            return (false, "Invalid or expired authentication challenge.", null, null);
        }

        if (challenge.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            return (false, "Authentication challenge has expired.", null, null);
        }

        string normalizedUsername = NormalizeUsername(username);
        if (!string.Equals(challenge.Username, normalizedUsername, StringComparison.Ordinal))
        {
            return (false, "Challenge username mismatch.", null, null);
        }

        UserAccount? user = await _userRepository.GetUserAsync(normalizedUsername, ct);
        if (user == null || !user.IsActive)
        {
            return (false, "User account not found or deactivated.", null, null);
        }

        // Validate access gating boundaries
        if (isWebRequest && user.AccessGate == AccessGate.Api)
        {
            return (false, "Access restricted: User account is gated for API access only.", null, null);
        }

        if (!isWebRequest && user.AccessGate == AccessGate.Web)
        {
            return (false, "Access restricted: User account is gated for Web UI access only.", null, null);
        }

        // Verify cryptographic signature against registered public key
        byte[] nonceBytes = Convert.FromBase64String(challenge.NonceBase64);
        bool isValidSig = VerifySignatureInternal(user, nonceBytes, signatureBase64);

        if (!isValidSig)
        {
            _auditService.LogAction(
                "AUTHENTICATION",
                "AuthenticationFailed",
                $"user:{user.Username}",
                $"{{\"reason\":\"Invalid cryptographic signature\",\"algorithm\":\"{user.Algorithm}\"}}");

            return (false, "Cryptographic signature verification failed.", null, null);
        }

        // Mint authenticated bearer token
        string token = MintBearerToken(user);
        DateTimeOffset loginTime = DateTimeOffset.UtcNow;
        await _userRepository.UpdateLastLoginAsync(user.Username, loginTime, ct);

        _auditService.LogAction(
            "AUTHENTICATION",
            "UserAuthenticated",
            $"user:{user.Username}",
            $"{{\"role\":\"{user.Role}\",\"gate\":\"{user.AccessGate}\",\"isWeb\":{isWebRequest.ToString().ToLowerInvariant()}}}");

        return (true, null, token, user with { LastLoginUtc = loginTime });
    }

    /// <inheritdoc/>
    public async Task<(bool Success, string? ErrorMessage, string? Token, UserAccount? User)> AuthenticateWithPrivateKeyAsync(
        string username,
        string privateKeyPem,
        string? password = null,
        bool isWebRequest = true,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);

        UserAccount? user = await _userRepository.GetUserAsync(NormalizeUsername(username), ct);
        if (user == null || !user.IsActive)
        {
            return (false, "User account not found or deactivated.", null, null);
        }

        // Parse and decrypt private key
        byte[]? privateKeyDer = ExtractPrivateKeyDerFromPem(privateKeyPem, password);
        if (privateKeyDer == null)
        {
            return (false, "Failed to parse or decrypt private key. Check passphrase or format.", null, null);
        }

        // Sign an ephemeral challenge and verify against user's registered public key
        byte[] ephemeralNonce = new byte[32];
        RandomNumberGenerator.Fill(ephemeralNonce);

        string sigBase64;
        try
        {
            sigBase64 = SignEphemeralNonce(user.Algorithm, ephemeralNonce, privateKeyDer);
        }
        catch
        {
            return (false, "Cryptographic signature generation with private key failed.", null, null);
        }

        // Validate access gating
        if (isWebRequest && user.AccessGate == AccessGate.Api)
        {
            return (false, "Access restricted: User account is gated for API access only.", null, null);
        }

        if (!isWebRequest && user.AccessGate == AccessGate.Web)
        {
            return (false, "Access restricted: User account is gated for Web UI access only.", null, null);
        }

        bool isValid = VerifySignatureInternal(user, ephemeralNonce, sigBase64);
        if (!isValid)
        {
            return (false, "Private key does not match the registered public key for this account.", null, null);
        }

        string token = MintBearerToken(user);
        DateTimeOffset loginTime = DateTimeOffset.UtcNow;
        await _userRepository.UpdateLastLoginAsync(user.Username, loginTime, ct);

        return (true, null, token, user with { LastLoginUtc = loginTime });
    }

    /// <inheritdoc/>
    public async Task<(bool Valid, UserAccount? User, string? ErrorReason)> ValidateTokenAsync(
        string token,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, null, "Bearer token is missing or empty.");
        }

        // WHAT: Splits the bearer token around its single '.' delimiter using ReadOnlySpan<char>.
        // WHY:  ValidateTokenAsync runs on every authenticated HTTP request; string.Split('.') allocates a string[] on the hot path.
        // HOW:  Checks IndexOf('.') and LastIndexOf('.') to ensure exactly one separator exists before slicing payload and signature.
        ReadOnlySpan<char> tokenSpan = token.AsSpan();
        int dotIndex = tokenSpan.IndexOf('.');
        if (dotIndex < 0 || dotIndex != tokenSpan.LastIndexOf('.'))
        {
            return (false, null, "Malformed bearer token format.");
        }

        string payloadBase64 = tokenSpan[..dotIndex].ToString();
        string signatureBase64 = tokenSpan[(dotIndex + 1)..].ToString();

        // Verify HMAC-SHA256 signature
        byte[] payloadBytes = Convert.FromBase64String(Base64UrlDecode(payloadBase64));
        byte[] expectedSig = HMACSHA256.HashData(_tokenHmacKey.Span, payloadBytes);
        byte[] actualSig = Convert.FromBase64String(Base64UrlDecode(signatureBase64));

        if (!CryptographicOperations.FixedTimeEquals(expectedSig, actualSig))
        {
            return (false, null, "Token signature verification failed.");
        }

        AuthTokenClaims? claims;
        try
        {
            claims = JsonSerializer.Deserialize<AuthTokenClaims>(payloadBytes);
        }
        catch
        {
            return (false, null, "Failed to deserialize token claims.");
        }

        if (claims == null)
        {
            return (false, null, "Token payload contains null claims.");
        }

        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (claims.ExpiresAtUnixSeconds <= nowUnix)
        {
            return (false, null, "Token has expired.");
        }

        UserAccount? user = await _userRepository.GetUserAsync(claims.Username, ct);
        if (user == null || !user.IsActive)
        {
            return (false, null, "User account no longer exists or is inactive.");
        }

        return (true, user, null);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<UserAccount>> ListUsersAsync(CancellationToken ct = default)
    {
        return _userRepository.ListUsersAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<UserAccount> CreateUserAsync(
        string username,
        UserRole role,
        AccessGate gate,
        string publicKeyBase64,
        KeyAlgorithm algorithm,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyBase64);

        UserAccount user = new()
        {
            Username = NormalizeUsername(username),
            Role = role,
            AccessGate = gate,
            Algorithm = algorithm,
            PublicKeyBase64 = publicKeyBase64.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            IsActive = true
        };

        await _userRepository.CreateUserAsync(user, ct);

        _auditService.LogAction(
            "USER_MANAGEMENT",
            "UserCreated",
            $"user:{user.Username}",
            $"{{\"role\":\"{role}\",\"gate\":\"{gate}\",\"algorithm\":\"{algorithm}\"}}");

        return user;
    }

    /// <inheritdoc/>
    public async Task<UserAccount?> TryCreateUserAsync(
        string username,
        UserRole role,
        AccessGate gate,
        string publicKeyBase64,
        KeyAlgorithm algorithm,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyBase64);

        UserAccount user = new()
        {
            Username = NormalizeUsername(username),
            Role = role,
            AccessGate = gate,
            Algorithm = algorithm,
            PublicKeyBase64 = publicKeyBase64.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            IsActive = true
        };

        // WHAT: Inserts the account only if the name is free.
        // WHY:  A refused duplicate must neither alter the existing account nor appear in the ledger as a creation.
        // HOW:  The repository's insert-only path returns false on conflict; we audit solely on success.
        if (!await _userRepository.TryCreateUserAsync(user, ct))
        {
            return null;
        }

        _auditService.LogAction(
            "USER_MANAGEMENT",
            "UserCreated",
            $"user:{user.Username}",
            $"{{\"role\":\"{role}\",\"gate\":\"{gate}\",\"algorithm\":\"{algorithm}\"}}");

        return user;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteUserAsync(string username, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        string normalizedUsername = NormalizeUsername(username);
        IReadOnlyList<UserAccount> allUsers = await _userRepository.ListUsersAsync(ct);
        UserAccount? target = allUsers.FirstOrDefault(u => string.Equals(u.Username, normalizedUsername, StringComparison.Ordinal));

        if (target == null)
        {
            return false;
        }

        // Prevent deleting the sole remaining active Administrator
        if (target.Role == UserRole.Administrator)
        {
            int adminCount = allUsers.Count(u => u.Role == UserRole.Administrator && u.IsActive);
            if (adminCount <= 1)
            {
                throw new InvalidOperationException("Cannot delete the last remaining active Administrator account.");
            }
        }

        bool deleted = await _userRepository.DeleteUserAsync(target.Username, ct);
        if (deleted)
        {
            _auditService.LogAction(
                "USER_MANAGEMENT",
                "UserDeleted",
                $"user:{target.Username}",
                $"{{\"role\":\"{target.Role}\"}}");
        }

        return deleted;
    }

    /// <summary>
    /// Verifies cryptographic signature for a user using either Ed25519 or ML-DSA-65.
    /// </summary>
    /// <param name="user">The account whose stored public key is used for verification.</param>
    /// <param name="data">The exact bytes that were supposed to have been signed, normally a challenge nonce.</param>
    /// <param name="signatureBase64">The Base64-encoded signature presented by the caller.</param>
    /// <returns><c>true</c> when the signature was produced by the holder of the matching private key; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// In plain English: the account record stores only the public half of the user's key pair, which can check
    /// a signature but cannot create one. If this method returns <c>true</c>, the caller must have had the
    /// private half, which is the whole basis of password-free sign-in here. Any malformed key, malformed
    /// signature or algorithm mismatch is treated as a plain verification failure rather than an error, so a
    /// hostile caller learns nothing from the difference.
    /// </remarks>
    private bool VerifySignatureInternal(UserAccount user, byte[] data, string signatureBase64)
    {
        try
        {
            if (user.Algorithm == KeyAlgorithm.Ed25519)
            {
                byte[] pubBytes = Convert.FromBase64String(user.PublicKeyBase64);
                var pubKey = (Ed25519PublicKeyParameters)PublicKeyFactory.CreateKey(pubBytes);
                Ed25519Signer signer = new();
                signer.Init(false, pubKey);
                signer.BlockUpdate(data, 0, data.Length);
                byte[] sigBytes = Convert.FromBase64String(signatureBase64);
                return signer.VerifySignature(sigBytes);
            }
            else
            {
                return _pqcProvider.VerifySignature(data, signatureBase64, user.PublicKeyBase64);
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Signs an ephemeral nonce with a private key DER payload.
    /// </summary>
    /// <param name="algorithm">Which signature scheme the key belongs to, Ed25519 or ML-DSA-65.</param>
    /// <param name="data">The short-lived random bytes to sign.</param>
    /// <param name="privateKeyDer">The private key in raw DER form, already decrypted.</param>
    /// <returns>The Base64-encoded signature over <paramref name="data"/>.</returns>
    /// <remarks>
    /// In plain English: this is the server doing, on the user's behalf, what a browser would normally do with
    /// the user's own key file during direct private-key sign-in. "Ephemeral" means the signed value is random
    /// and used exactly once, so the resulting signature cannot be replayed later.
    /// </remarks>
    private string SignEphemeralNonce(KeyAlgorithm algorithm, byte[] data, byte[] privateKeyDer)
    {
        if (algorithm == KeyAlgorithm.Ed25519)
        {
            var privKey = (Ed25519PrivateKeyParameters)PrivateKeyFactory.CreateKey(privateKeyDer);
            Ed25519Signer signer = new();
            signer.Init(true, privKey);
            signer.BlockUpdate(data, 0, data.Length);
            byte[] sig = signer.GenerateSignature();
            return Convert.ToBase64String(sig);
        }
        else
        {
            string privBase64 = Convert.ToBase64String(privateKeyDer);
            PqcSignature sig = _pqcProvider.SignData(data, privBase64, "ephemeral");
            return sig.SignatureBase64;
        }
    }

    /// <inheritdoc/>
    public string GenerateBearerToken(UserAccount user)
    {
        ArgumentNullException.ThrowIfNull(user);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        AuthTokenClaims claims = new()
        {
            Username = user.Username,
            Role = user.Role,
            AccessGate = user.AccessGate,
            IssuedAtUnixSeconds = now.ToUnixTimeSeconds(),
            ExpiresAtUnixSeconds = now.Add(TokenLifetime).ToUnixTimeSeconds()
        };

        byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(claims);
        byte[] sigBytes = HMACSHA256.HashData(_tokenHmacKey.Span, payloadBytes);

        return $"{Base64UrlEncode(Convert.ToBase64String(payloadBytes))}.{Base64UrlEncode(Convert.ToBase64String(sigBytes))}";
    }

    /// <summary>
    /// Mints a signed bearer token for an authenticated user.
    /// </summary>
    /// <param name="user">The account that has just proven its identity.</param>
    /// <returns>A tamper-evident bearer token carrying the account's username, role and access gate.</returns>
    /// <remarks>
    /// A thin internal alias for <c>GenerateBearerToken</c>, kept so the authentication flows read as
    /// "mint a token" at the point of use. The token is sealed with this node's own 256-bit signing key, so a
    /// token minted on another node, or forged with a key taken from the public source history, fails
    /// verification and is answered with HTTP 401.
    /// </remarks>
    private string MintBearerToken(UserAccount user) => GenerateBearerToken(user);

    /// <summary>
    /// Encrypts raw private key DER bytes using PBKDF2 (SHA-256) and AES-256-GCM.
    /// </summary>
    /// <param name="privateKeyDer">The private key bytes to protect.</param>
    /// <param name="password">The passphrase chosen by the user who will store the key file.</param>
    /// <returns>A self-contained envelope laid out as salt (16 bytes), nonce (12), tag (16), ciphertext.</returns>
    /// <remarks>
    /// In plain English: a passphrase on its own is a poor encryption key, so it is stretched into a strong
    /// 256-bit key by hashing it 100,000 times together with 16 random bytes (the "salt"). The salt is stored
    /// alongside the result, which is why two identical passphrases never produce identical files. The
    /// authentication tag lets the decrypting side notice if even one byte was altered. The derived key is
    /// wiped from memory before returning.
    /// </remarks>
    internal static byte[] EncryptPrivateKeyBytes(byte[] privateKeyDer, string password)
    {
        byte[] salt = new byte[16];
        byte[] nonce = new byte[12];
        RandomNumberGenerator.Fill(salt);
        RandomNumberGenerator.Fill(nonce);

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations: 100000,
            HashAlgorithmName.SHA256,
            outputLength: 32);

        byte[] ciphertext = new byte[privateKeyDer.Length];
        byte[] tag = new byte[16];

        using (AesGcm aesGcm = new(key, 16))
        {
            aesGcm.Encrypt(nonce, privateKeyDer, ciphertext, tag, associatedData: salt);
        }

        // Structure: [Salt: 16][Nonce: 12][Tag: 16][Ciphertext: N]
        byte[] output = new byte[salt.Length + nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(salt, 0, output, 0, 16);
        Buffer.BlockCopy(nonce, 0, output, 16, 12);
        Buffer.BlockCopy(tag, 0, output, 28, 16);
        Buffer.BlockCopy(ciphertext, 0, output, 44, ciphertext.Length);

        CryptographicOperations.ZeroMemory(key);
        return output;
    }

    /// <summary>
    /// Decrypts private key bytes from encrypted envelope structure.
    /// </summary>
    /// <param name="encryptedBytes">The envelope produced by <c>EncryptPrivateKeyBytes</c>.</param>
    /// <param name="password">The passphrase that was used to protect it.</param>
    /// <returns>The original private key bytes, or <c>null</c> when the envelope is too short, corrupt, or the passphrase is wrong.</returns>
    /// <remarks>
    /// In plain English: the salt and nonce are read back out of the front of the envelope, the passphrase is
    /// stretched the same way it was during encryption, and the authentication tag is checked. A wrong
    /// passphrase and a tampered file are deliberately indistinguishable from the outside: both simply return
    /// <c>null</c>, with no detail that would help someone guess. The derived key is always wiped.
    /// </remarks>
    internal static byte[]? DecryptPrivateKeyBytes(byte[] encryptedBytes, string password)
    {
        if (encryptedBytes.Length < 44) return null;

        byte[] salt = new byte[16];
        byte[] nonce = new byte[12];
        byte[] tag = new byte[16];
        int cipherLen = encryptedBytes.Length - 44;
        byte[] ciphertext = new byte[cipherLen];

        Buffer.BlockCopy(encryptedBytes, 0, salt, 0, 16);
        Buffer.BlockCopy(encryptedBytes, 16, nonce, 0, 12);
        Buffer.BlockCopy(encryptedBytes, 28, tag, 0, 16);
        Buffer.BlockCopy(encryptedBytes, 44, ciphertext, 0, cipherLen);

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations: 100000,
            HashAlgorithmName.SHA256,
            outputLength: 32);

        byte[] plaintext = new byte[cipherLen];
        try
        {
            using AesGcm aesGcm = new(key, 16);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData: salt);
            return plaintext;
        }
        catch
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// Extracts DER private key bytes from standard plain or encrypted PEM text.
    /// </summary>
    /// <param name="pem">The PEM text pasted or uploaded by the user.</param>
    /// <param name="password">The passphrase, required only when the PEM is an encrypted private key.</param>
    /// <returns>The raw private key bytes, or <c>null</c> when the text is not usable key material.</returns>
    /// <remarks>
    /// In plain English: PEM is just key bytes rendered as Base64 text between two
    /// <c>-----BEGIN ...-----</c> and <c>-----END ...-----</c> marker lines, which makes a key safe to paste
    /// into a text box. This strips the marker lines, joins the Base64 back together and converts it to bytes.
    /// If the header says the key is encrypted, the passphrase is then required and the bytes are passed on to
    /// be decrypted. Anything unreadable yields <c>null</c> rather than an exception, because the input comes
    /// straight from a sign-in form.
    /// </remarks>
    internal static byte[]? ExtractPrivateKeyDerFromPem(string pem, string? password)
    {
        string clean = pem.Trim();
        bool isEncrypted = clean.Contains("ENCRYPTED PRIVATE KEY", StringComparison.OrdinalIgnoreCase);

        // Strip PEM boundary lines
        string[] lines = clean.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        StringBuilder base64Sb = new();
        foreach (string line in lines)
        {
            string t = line.Trim();
            if (t.StartsWith("-----") && t.EndsWith("-----")) continue;
            base64Sb.Append(t);
        }

        byte[] derBytes;
        try
        {
            derBytes = Convert.FromBase64String(base64Sb.ToString());
        }
        catch
        {
            return null;
        }

        if (!isEncrypted)
        {
            return derBytes;
        }

        if (password == null || password.Length == 0)
        {
            return null;
        }

        return DecryptPrivateKeyBytes(derBytes, password);
    }

    /// <summary>
    /// Formats DER bytes into standard PEM text.
    /// </summary>
    /// <param name="type">The label that goes in the marker lines, for example <c>PUBLIC KEY</c> or <c>ENCRYPTED PRIVATE KEY</c>.</param>
    /// <param name="derBytes">The raw key bytes to render.</param>
    /// <returns>PEM text with marker lines and Base64 wrapped at 64 characters per line.</returns>
    /// <remarks>
    /// In plain English: the inverse of the extraction above. Key bytes are turned into Base64, broken into
    /// 64-character lines (the convention every tool expects) and wrapped in the two marker lines, producing a
    /// file the user can save, print or paste anywhere.
    /// </remarks>
    internal static string FormatPem(string type, byte[] derBytes)
    {
        string base64 = Convert.ToBase64String(derBytes);
        StringBuilder sb = new();
        sb.AppendLine($"-----BEGIN {type}-----");
        for (int i = 0; i < base64.Length; i += 64)
        {
            int len = Math.Min(64, base64.Length - i);
            sb.AppendLine(base64.Substring(i, len));
        }
        sb.AppendLine($"-----END {type}-----");
        return sb.ToString();
    }

    /// <summary>
    /// Base64Url encodes standard Base64 text.
    /// </summary>
    /// <param name="base64">Standard Base64 text.</param>
    /// <returns>The same data in the URL-safe Base64 alphabet, without trailing padding.</returns>
    /// <remarks>
    /// In plain English: ordinary Base64 uses <c>+</c>, <c>/</c> and trailing <c>=</c>, all of which have
    /// special meaning inside a URL or an HTTP header. Swapping them for <c>-</c> and <c>_</c> and dropping the
    /// padding makes the token safe to put in a header, a query string or a cookie without further escaping.
    /// </remarks>
    private static string Base64UrlEncode(string base64) =>
        base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>
    /// Base64Url decodes into standard Base64 text.
    /// </summary>
    /// <param name="base64Url">URL-safe Base64 text, with or without padding.</param>
    /// <returns>Standard Base64 text that the runtime's decoder accepts.</returns>
    /// <remarks>
    /// In plain English: the exact reverse of the encoding above. The two substituted characters are put back
    /// and the dropped <c>=</c> padding is restored, because Base64 is only decodable in whole four-character
    /// groups.
    /// </remarks>
    private static string Base64UrlDecode(string base64Url)
    {
        string s = base64Url.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return s;
    }

    /// <summary>
    /// Normalizes a username by trimming whitespace and converting to lowercase invariant characters.
    /// </summary>
    /// <param name="username">The raw username string.</param>
    /// <returns>The normalized lowercase username.</returns>
    private static string NormalizeUsername(string username) => username.Trim().ToLowerInvariant();

    /// <summary>
    /// Default maximum number of concurrent unconsumed authentication challenges permitted in memory.
    /// </summary>
    public const int DefaultMaxActiveChallenges = 10_000;

    /// <summary>
    /// Default interval between background sweeps of expired authentication challenges.
    /// </summary>
    private static readonly TimeSpan DefaultPruneInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Periodic timer that sweeps expired authentication challenges even when no new challenges arrive.
    /// </summary>
    private readonly Timer _pruneTimer;

    /// <summary>
    /// Gets or sets the maximum number of active authentication challenges allowed before new requests are throttled.
    /// </summary>
    /// <remarks>
    /// Exposed as <c>internal</c> so unit and integration tests can exercise the capacity limit without allocating
    /// 10,000 challenge records.
    /// </remarks>
    internal int MaxActiveChallenges { get; set; } = DefaultMaxActiveChallenges;

    /// <summary>
    /// Timer callback that sweeps expired authentication challenges using the current UTC clock.
    /// </summary>
    /// <param name="state">Unused timer state object.</param>
    internal void OnPruneTimerTick(object? state)
    {
        PruneExpiredChallenges(DateTimeOffset.UtcNow);
    }

    /// <inheritdoc/>
    public int PruneExpiredChallenges(DateTimeOffset nowUtc)
    {
        // WHAT: Removes every challenge from _activeChallenges whose ExpiresAtUtc is at or before nowUtc.
        // WHY:  Prevents unbounded memory growth from abandoned authentication challenges (Audit L9).
        // HOW:  Iterates the lock-free ConcurrentDictionary enumerator and atomically removes expired keys.
        int removed = 0;
        foreach (KeyValuePair<string, AuthChallenge> kvp in _activeChallenges)
        {
            if (kvp.Value.ExpiresAtUtc <= nowUtc && _activeChallenges.TryRemove(kvp.Key, out _))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// Disposes the background challenge pruning timer.
    /// </summary>
    public void Dispose()
    {
        _pruneTimer.Dispose();
    }
}
