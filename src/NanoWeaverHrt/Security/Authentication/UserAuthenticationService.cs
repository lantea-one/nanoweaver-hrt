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
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Models;

namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Production implementation of <see cref="IUserAuthenticationService"/> providing asymmetric private-key authentication,
/// challenge-response token exchanges, role-based access control, and first-launch setup gating.
/// </summary>
public sealed class UserAuthenticationService : IUserAuthenticationService
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
    private readonly byte[] _tokenHmacKey;

    /// <summary>
    /// Thread-safe in-memory cache of pending authentication challenges.
    /// </summary>
    private readonly ConcurrentDictionary<string, AuthChallenge> _activeChallenges = new();

    /// <summary>
    /// Lifetime duration for authentication challenge nonces before expiration.
    /// </summary>
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromSeconds(60);

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
    /// <param name="keyManagement">Key management service for node identity.</param>
    public UserAuthenticationService(
        IUserRepository userRepository,
        IPostQuantumCryptoProvider pqcProvider,
        IComplianceAuditService auditService,
        IKeyManagementService keyManagement)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _pqcProvider = pqcProvider ?? throw new ArgumentNullException(nameof(pqcProvider));
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));

        ArgumentNullException.ThrowIfNull(keyManagement);

        // Derive deterministic, high-entropy 256-bit token signing key from node signing key identity
        string nodeKeyId = keyManagement.GetNodeSigningKey().KeyId;
        _tokenHmacKey = SHA256.HashData(Encoding.UTF8.GetBytes($"{nodeKeyId}:WEAVER_BEARER_TOKEN_SECRET_V1"));
    }

    /// <summary>
    /// Registers an explicit authentication challenge for testing expiration workflows.
    /// </summary>
    /// <param name="challenge">The challenge instance to register.</param>
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
            Username = adminUsername.Trim(),
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
            Username = username.Trim(),
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

        // Clean expired challenges
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (var kvp in _activeChallenges)
        {
            if (kvp.Value.ExpiresAtUtc <= now)
            {
                _activeChallenges.TryRemove(kvp.Key, out _);
            }
        }

        byte[] nonce = new byte[32];
        RandomNumberGenerator.Fill(nonce);

        string challengeId = Guid.NewGuid().ToString("N");
        AuthChallenge challenge = new()
        {
            ChallengeId = challengeId,
            Username = username.Trim(),
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

        if (!string.Equals(challenge.Username, username.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Challenge username mismatch.", null, null);
        }

        UserAccount? user = await _userRepository.GetUserAsync(username.Trim(), ct);
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

        UserAccount? user = await _userRepository.GetUserAsync(username.Trim(), ct);
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

        string[] parts = token.Split('.');
        if (parts.Length != 2)
        {
            return (false, null, "Malformed bearer token format.");
        }

        string payloadBase64 = parts[0];
        string signatureBase64 = parts[1];

        // Verify HMAC-SHA256 signature
        byte[] payloadBytes = Convert.FromBase64String(Base64UrlDecode(payloadBase64));
        byte[] expectedSig = HMACSHA256.HashData(_tokenHmacKey, payloadBytes);
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
            Username = username.Trim(),
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
    public async Task<bool> DeleteUserAsync(string username, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        IReadOnlyList<UserAccount> allUsers = await _userRepository.ListUsersAsync(ct);
        UserAccount? target = allUsers.FirstOrDefault(u => string.Equals(u.Username, username.Trim(), StringComparison.OrdinalIgnoreCase));

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

        bool deleted = await _userRepository.DeleteUserAsync(username.Trim(), ct);
        if (deleted)
        {
            _auditService.LogAction(
                "USER_MANAGEMENT",
                "UserDeleted",
                $"user:{username.Trim()}",
                $"{{\"role\":\"{target.Role}\"}}");
        }

        return deleted;
    }

    /// <summary>
    /// Verifies cryptographic signature for a user using either Ed25519 or ML-DSA-65.
    /// </summary>
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
        byte[] sigBytes = HMACSHA256.HashData(_tokenHmacKey, payloadBytes);

        return $"{Base64UrlEncode(Convert.ToBase64String(payloadBytes))}.{Base64UrlEncode(Convert.ToBase64String(sigBytes))}";
    }

    /// <summary>
    /// Mints a signed bearer token for an authenticated user.
    /// </summary>
    private string MintBearerToken(UserAccount user) => GenerateBearerToken(user);

    /// <summary>
    /// Encrypts raw private key DER bytes using PBKDF2 (SHA-256) and AES-256-GCM.
    /// </summary>
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
    private static string Base64UrlEncode(string base64) =>
        base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>
    /// Base64Url decodes into standard Base64 text.
    /// </summary>
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
}
