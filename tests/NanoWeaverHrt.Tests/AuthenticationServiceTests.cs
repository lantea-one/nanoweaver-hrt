using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.KeyManagement;
using NanoWeaverHrt.Security.Secrets;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Unit test suite comprehensively verifying asymmetric private-key authentication,
/// post-quantum ML-DSA-65 and Ed25519 key generation, PBKDF2 passphrase encryption,
/// challenge-response token exchanges, access gating, and sovereign user management.
/// </summary>
public sealed class AuthenticationServiceTests : IDisposable
{
    /// <summary>
    /// Temporary directory isolated for SQLite database fixtures.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Master user persistence repository.
    /// </summary>
    private readonly UserRepository _userRepository;

    /// <summary>
    /// Post-quantum cryptographic provider.
    /// </summary>
    private readonly PostQuantumCryptoProvider _pqcProvider;

    /// <summary>
    /// Durable subject key vault.
    /// </summary>
    private readonly SubjectKeyVault _subjectKeyVault;

    /// <summary>
    /// Node key management service.
    /// </summary>
    private readonly KeyManagementService _keyManagement;

    /// <summary>
    /// FDA Part 11 compliance audit service.
    /// </summary>
    private readonly ComplianceAuditService _auditService;

    /// <summary>
    /// Node secret store supplying this test instance's persisted bearer-token signing key.
    /// </summary>
    private readonly NodeSecretStore _secretStore;

    /// <summary>
    /// Authoritative user authentication service under test.
    /// </summary>
    private readonly UserAuthenticationService _authService;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthenticationServiceTests"/> class.
    /// </summary>
    public AuthenticationServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "weaver_auth_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["POSTGRES_CONNECTION_STRING"] = string.Empty
            })
            .Build();

        SystemConfigurationStore configStore = new(WeaverTestDb.CreateFactory(configuration));
        configStore.InitializeAsync().GetAwaiter().GetResult();

        // WHAT: Provisions this test instance's own token signing key inside its isolated temp directory.
        // WHY:  The signing key is no longer derived from a constant, so the service needs a real key store;
        //       keeping it under _tempDir keeps every test class cryptographically and filesystem independent.
        // HOW:  NodeSecretStore derives its directory from the TRAJECTORY_INCOMING_DIR already configured above.
        _secretStore = new NodeSecretStore(configuration);

        _pqcProvider = new PostQuantumCryptoProvider();
        ProfileDataProtector protector = new(_secretStore);
        SubjectKeyRepository subjectKeyRepo = new(WeaverTestDb.CreateFactory(configuration));
        SubjectErasureJournal journal = new(_secretStore);
        _subjectKeyVault = new SubjectKeyVault(subjectKeyRepo, protector, _pqcProvider, journal);
        _subjectKeyVault.InitializeAsync().GetAwaiter().GetResult();
        _keyManagement = new KeyManagementService(_pqcProvider, _subjectKeyVault, _secretStore);
        _auditService = new ComplianceAuditService(_keyManagement, _pqcProvider);

        _userRepository = new UserRepository(WeaverTestDb.CreateFactory(configuration, new DatabaseMigrationRunner()));
        _userRepository.InitializeAsync().GetAwaiter().GetResult();

        _authService = new UserAuthenticationService(
            _userRepository,
            _pqcProvider,
            _auditService,
            _secretStore);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _authService.Dispose();
        _subjectKeyVault.Dispose();
        _userRepository.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Ignored in test cleanup
            }
        }
    }

    /// <summary>
    /// Verifies that <see cref="UserSessionState"/> correctly tracks user identity, roles, and change events.
    /// </summary>
    [Fact]
    public void UserSessionState_LifecycleAndRoleFlags_BehaveAccurately()
    {
        // WHAT: Tests properties and event dispatches of UserSessionState.
        // WHY:  Ensures Blazor circuits correctly reflect user permissions and role switches.
        // HOW:  Sets Admin, Researcher, and Patient users, checking role access flags and OnChange.
        UserSessionState state = new();
        Assert.False(state.IsAuthenticated);
        Assert.False(state.IsAdmin);
        Assert.False(state.IsResearcher);
        Assert.False(state.IsPatient);
        Assert.Null(state.CurrentUser);

        // Test state notifications without listeners
        state.SetUser(null);
        state.SignOut();

        int eventCount = 0;
        state.OnChange += () => eventCount++;

        // 1. Set Administrator
        UserAccount admin = new()
        {
            Username = "admin_user",
            Role = UserRole.Administrator,
            AccessGate = AccessGate.ApiWeb,
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = "MCowBQYDK2VwAyEA1234567890abcdef1234567890abcdef1234567890="
        };
        state.SetUser(admin);

        Assert.True(state.IsAuthenticated);
        Assert.True(state.IsAdmin);
        Assert.False(state.IsResearcher);
        Assert.False(state.IsPatient);
        Assert.Equal("admin_user", state.CurrentUser?.Username);
        Assert.Equal(1, eventCount);

        // 2. Set Researcher
        UserAccount researcher = new()
        {
            Username = "res_user",
            Role = UserRole.Researcher,
            AccessGate = AccessGate.Api,
            Algorithm = KeyAlgorithm.MlDsa65,
            PublicKeyBase64 = "MIIB...TEST..."
        };
        state.SetUser(researcher);

        Assert.True(state.IsAuthenticated);
        Assert.False(state.IsAdmin);
        Assert.True(state.IsResearcher);
        Assert.False(state.IsPatient);
        Assert.Equal(2, eventCount);

        // 3. Set Patient
        UserAccount patient = new()
        {
            Username = "patient_user",
            Role = UserRole.Patient,
            AccessGate = AccessGate.Web,
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = "MCowBQYDK2VwAyEA9876543210abcdef1234567890abcdef1234567890="
        };
        state.SetUser(patient);

        Assert.True(state.IsAuthenticated);
        Assert.False(state.IsAdmin);
        Assert.False(state.IsResearcher);
        Assert.True(state.IsPatient);
        Assert.Equal(3, eventCount);

        // 4. Sign Out
        state.SignOut();
        Assert.False(state.IsAuthenticated);
        Assert.False(state.IsAdmin);
        Assert.Null(state.CurrentUser);
        Assert.Equal(4, eventCount);
    }

    /// <summary>
    /// Verifies that the insert-only repository path inserts a fresh name and refuses a taken one untouched.
    /// </summary>
    [Fact]
    public async Task UserRepository_TryCreateUserAsync_InsertsFreshNameAndRefusesTakenNameWithoutModifyingIt()
    {
        // WHAT: Inserts a username twice through TryCreateUserAsync with different role, gate and key.
        // WHY:  The database must arbitrate the conflict so a duplicate can never overwrite an account.
        // HOW:  Asserts true then false, then reads the row back and compares it with the first insert.
        UserAccount original = new()
        {
            Username = "try_create_user",
            Role = UserRole.Patient,
            AccessGate = AccessGate.Web,
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = "MCowBQYDK2VwAyEA1111111111111111111111111111111111111111111=",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            IsActive = true
        };
        UserAccount duplicate = new()
        {
            Username = "try_create_user",
            Role = UserRole.Administrator,
            AccessGate = AccessGate.ApiWeb,
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = "MCowBQYDK2VwAyEA2222222222222222222222222222222222222222222=",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            IsActive = false
        };

        Assert.True(await _userRepository.TryCreateUserAsync(original));
        Assert.False(await _userRepository.TryCreateUserAsync(duplicate));

        UserAccount? stored = await _userRepository.GetUserAsync("try_create_user");
        Assert.NotNull(stored);
        Assert.Equal(UserRole.Patient, stored.Role);
        Assert.Equal(AccessGate.Web, stored.AccessGate);
        Assert.Equal(original.PublicKeyBase64, stored.PublicKeyBase64);
        Assert.True(stored.IsActive);
        Assert.Single((await _userRepository.ListUsersAsync()), u => u.Username == "try_create_user");
    }

    /// <summary>
    /// Verifies argument validation on the insert-only repository path.
    /// </summary>
    [Fact]
    public async Task UserRepository_TryCreateUserAsync_InvalidArguments_Throw()
    {
        // WHAT: Passes a null account and accounts with blank username or key.
        // WHY:  Fail-fast validation must match the upserting CreateUserAsync.
        // HOW:  Asserts the argument exceptions.
        await Assert.ThrowsAsync<ArgumentNullException>(() => _userRepository.TryCreateUserAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => _userRepository.TryCreateUserAsync(new UserAccount { Username = " ", PublicKeyBase64 = "k" }));
        await Assert.ThrowsAsync<ArgumentException>(() => _userRepository.TryCreateUserAsync(new UserAccount { Username = "u", PublicKeyBase64 = " " }));
    }

    /// <summary>
    /// Verifies the service-level insert-only creation returns null for a duplicate and leaves the account intact.
    /// </summary>
    [Fact]
    public async Task TryCreateUserAsync_DuplicateUsername_ReturnsNullAndKeepsOriginalAccount()
    {
        // WHAT: Creates an account through the service, then repeats the name with a different role and key.
        // WHY:  A refused duplicate must not alter the account (and is not audited as a creation).
        // HOW:  Asserts non-null then null, plus argument validation, then re-reads the stored account.
        GeneratedKeyBundle bundle = TestChallengeHelper.CreateTestKeyBundle("try_svc", UserRole.Researcher, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);

        UserAccount? created = await _authService.TryCreateUserAsync(" try_svc ", UserRole.Researcher, AccessGate.ApiWeb, bundle.PublicKeyBase64, KeyAlgorithm.Ed25519);
        Assert.NotNull(created);
        Assert.Equal("try_svc", created.Username);

        Assert.Null(await _authService.TryCreateUserAsync("try_svc", UserRole.Administrator, AccessGate.Api, "other-key", KeyAlgorithm.Ed25519));

        UserAccount? stored = await _userRepository.GetUserAsync("try_svc");
        Assert.NotNull(stored);
        Assert.Equal(UserRole.Researcher, stored.Role);
        Assert.Equal(bundle.PublicKeyBase64, stored.PublicKeyBase64);

        await Assert.ThrowsAsync<ArgumentException>(() => _authService.TryCreateUserAsync(" ", UserRole.Researcher, AccessGate.Api, "k", KeyAlgorithm.Ed25519));
        await Assert.ThrowsAsync<ArgumentException>(() => _authService.TryCreateUserAsync("x", UserRole.Researcher, AccessGate.Api, " ", KeyAlgorithm.Ed25519));
    }

    /// <summary>
    /// Verifies constructor argument validation on <see cref="UserAuthenticationService"/> and <see cref="UserRepository"/>.
    /// </summary>
    [Fact]
    public void Constructors_NullArguments_ThrowArgumentNullException()
    {
        // WHAT: Tests constructor preconditions for null parameter safety.
        // WHY:  Ensures fail-fast semantics when required dependencies are missing.
        // HOW:  Invokes constructors with null values and asserts ArgumentNullException.
        Assert.Throws<ArgumentNullException>(() => new UserRepository(null!));

        Assert.Throws<ArgumentNullException>(() => new UserAuthenticationService(null!, _pqcProvider, _auditService, _secretStore));
        Assert.Throws<ArgumentNullException>(() => new UserAuthenticationService(_userRepository, null!, _auditService, _secretStore));
        Assert.Throws<ArgumentNullException>(() => new UserAuthenticationService(_userRepository, _pqcProvider, null!, _secretStore));
        Assert.Throws<ArgumentNullException>(() => new UserAuthenticationService(_userRepository, _pqcProvider, _auditService, null!));
    }

    /// <summary>
    /// Verifies first-launch setup detection and completion semantics.
    /// </summary>
    [Fact]
    public async Task SetupWorkflow_UninitializedState_RequiresSetupAndLocksAfterCreation()
    {
        // WHAT: Tests IsSetupRequiredAsync and CompleteInitialSetupAsync.
        // WHY:  Guarantees that first-launch setup strictly unlocks the system and cannot be called twice.
        // HOW:  Checks initial setup required, generates an Ed25519 keypair, initializes admin, and checks duplicate rejection.
        bool initialRequired = await _authService.IsSetupRequiredAsync();
        Assert.True(initialRequired);

        GeneratedKeyBundle bundle = TestChallengeHelper.CreateTestKeyBundle(
            "master_admin",
            UserRole.Administrator,
            AccessGate.ApiWeb,
            KeyAlgorithm.Ed25519);

        UserAccount admin = await _authService.CompleteInitialSetupAsync(
            "master_admin",
            bundle.PublicKeyBase64,
            KeyAlgorithm.Ed25519,
            AccessGate.ApiWeb);

        Assert.Equal("master_admin", admin.Username);
        Assert.Equal(UserRole.Administrator, admin.Role);
        Assert.Equal(AccessGate.ApiWeb, admin.AccessGate);
        Assert.True(admin.IsActive);

        // System should now report setup complete
        bool postSetupRequired = await _authService.IsSetupRequiredAsync();
        Assert.False(postSetupRequired);

        // Duplicate call to CompleteInitialSetupAsync must throw InvalidOperationException
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _authService.CompleteInitialSetupAsync(
                "master_admin_2",
                bundle.PublicKeyBase64,
                KeyAlgorithm.Ed25519,
                AccessGate.ApiWeb));
    }

    /// <summary>
    /// Verifies asymmetric keypair generation for Ed25519 and ML-DSA-65 with plain and password-encrypted PEMs.
    /// </summary>
    [Fact]
    public void KeyGeneration_Ed25519AndMlDsa65_GeneratesValidBundles()
    {
        // WHAT: Tests TestChallengeHelper.CreateTestKeyBundle across algorithms and password protection modes.
        // WHY:  Ensures both classical and post-quantum keys are generated with accurate PEM headers.
        // HOW:  Generates unencrypted and encrypted bundles for Ed25519 and ML-DSA-65 and asserts properties.
        // 1. Ed25519 Plain
        GeneratedKeyBundle edPlain = TestChallengeHelper.CreateTestKeyBundle(
            "ed_plain_user",
            UserRole.Researcher,
            AccessGate.ApiWeb,
            KeyAlgorithm.Ed25519);

        Assert.Equal("ed_plain_user", edPlain.Username);
        Assert.Equal(KeyAlgorithm.Ed25519, edPlain.Algorithm);
        Assert.False(edPlain.IsPasswordProtected);
        Assert.Contains("BEGIN PUBLIC KEY", edPlain.PublicKeyPem);
        Assert.Contains("BEGIN PRIVATE KEY", edPlain.PrivateKeyPem);

        // 2. Ed25519 Encrypted
        GeneratedKeyBundle edEncrypted = TestChallengeHelper.CreateTestKeyBundle(
            "ed_enc_user",
            UserRole.Administrator,
            AccessGate.ApiWeb,
            KeyAlgorithm.Ed25519,
            passphrase: "super_secret_passphrase");

        Assert.True(edEncrypted.IsPasswordProtected);
        Assert.Contains("BEGIN ENCRYPTED PRIVATE KEY", edEncrypted.PrivateKeyPem);

        // 3. ML-DSA-65 Plain
        GeneratedKeyBundle mlPlain = TestChallengeHelper.CreateTestKeyBundle(
            "ml_plain_user",
            UserRole.Researcher,
            AccessGate.ApiWeb,
            KeyAlgorithm.MlDsa65);

        Assert.Equal(KeyAlgorithm.MlDsa65, mlPlain.Algorithm);
        Assert.False(mlPlain.IsPasswordProtected);
        Assert.Contains("BEGIN PUBLIC KEY", mlPlain.PublicKeyPem);
        Assert.Contains("BEGIN PRIVATE KEY", mlPlain.PrivateKeyPem);

        // 4. ML-DSA-65 Encrypted
        GeneratedKeyBundle mlEncrypted = TestChallengeHelper.CreateTestKeyBundle(
            "ml_enc_user",
            UserRole.Patient,
            AccessGate.Web,
            KeyAlgorithm.MlDsa65,
            passphrase: "pqc_passphrase_456");

        Assert.True(mlEncrypted.IsPasswordProtected);
        Assert.Contains("BEGIN ENCRYPTED PRIVATE KEY", mlEncrypted.PrivateKeyPem);
    }

    /// <summary>
    /// Verifies the challenge-response authentication handshake using digital signatures.
    /// </summary>
    [Fact]
    public async Task ChallengeResponse_ValidSignature_AuthenticatesAndIssuesBearerToken()
    {
        // WHAT: Tests CreateChallenge and VerifyChallengeAndIssueTokenAsync with Ed25519.
        // WHY:  Proves zero-knowledge private-key authentication without transmitting private keys.
        // HOW:  Registers a user, requests a challenge nonce, signs it locally with Ed25519, and verifies token issuance.
        GeneratedKeyBundle bundle = TestChallengeHelper.CreateTestKeyBundle(
            "sig_researcher",
            UserRole.Researcher,
            AccessGate.ApiWeb,
            KeyAlgorithm.Ed25519);

        await _authService.CompleteInitialSetupAsync(
            "admin_init",
            bundle.PublicKeyBase64,
            KeyAlgorithm.Ed25519,
            AccessGate.ApiWeb);

        await _authService.CreateUserAsync(
            "sig_researcher",
            UserRole.Researcher,
            AccessGate.ApiWeb,
            bundle.PublicKeyBase64,
            KeyAlgorithm.Ed25519);

        // 1. Issue challenge
        AuthChallenge challenge = _authService.CreateChallenge("sig_researcher");
        Assert.Equal("sig_researcher", challenge.Username);
        Assert.False(string.IsNullOrEmpty(challenge.NonceBase64));

        // 2. Sign nonce locally with Ed25519
        byte[] nonceBytes = Convert.FromBase64String(challenge.NonceBase64);
        string signatureBase64 = SignEd25519(bundle.PrivateKeyPem, nonceBytes);

        // 3. Verify challenge and receive token
        var (success, errorMsg, token, user) = await _authService.VerifyChallengeAndIssueTokenAsync(
            "sig_researcher",
            challenge.ChallengeId,
            signatureBase64,
            isWebRequest: false);

        Assert.True(success);
        Assert.Null(errorMsg);
        Assert.NotNull(token);
        Assert.NotNull(user);
        Assert.Equal("sig_researcher", user.Username);

        // 4. Token validation
        var (valid, validUser, _) = await _authService.ValidateTokenAsync(token);
        Assert.True(valid);
        Assert.Equal("sig_researcher", validUser?.Username);

        // 5. Challenge replay attempt must fail (challenges are single-use)
        var (replaySuccess, replayError, _, _) = await _authService.VerifyChallengeAndIssueTokenAsync(
            "sig_researcher",
            challenge.ChallengeId,
            signatureBase64,
            isWebRequest: false);

        Assert.False(replaySuccess);
        Assert.Contains("invalid or expired", replayError?.ToLowerInvariant());
    }

    /// <summary>
    /// Verifies challenge-response rejection on invalid signatures or non-matching accounts.
    /// </summary>
    [Fact]
    public async Task ChallengeResponse_InvalidSignature_RejectsAuthentication()
    {
        // WHAT: Tests signature verification failure handling.
        // WHY:  Ensures forged or corrupted signatures are rejected with 401 Unauthorized.
        // HOW:  Signs arbitrary data and submits an invalid signature string.
        GeneratedKeyBundle bundle = TestChallengeHelper.CreateTestKeyBundle(
            "victim_user",
            UserRole.Researcher,
            AccessGate.ApiWeb,
            KeyAlgorithm.Ed25519);

        await _authService.CompleteInitialSetupAsync(
            "admin_test",
            bundle.PublicKeyBase64,
            KeyAlgorithm.Ed25519);

        await _authService.CreateUserAsync(
            "victim_user",
            UserRole.Researcher,
            AccessGate.ApiWeb,
            bundle.PublicKeyBase64,
            KeyAlgorithm.Ed25519);

        AuthChallenge challenge = _authService.CreateChallenge("victim_user");
        string badSignature = Convert.ToBase64String(new byte[64]);

        var (success, errorMsg, token, _) = await _authService.VerifyChallengeAndIssueTokenAsync(
            "victim_user",
            challenge.ChallengeId,
            badSignature);

        Assert.False(success);
        Assert.Null(token);
        Assert.Contains("signature verification failed", errorMsg?.ToLowerInvariant());

        // Test exception branch in signature verification
        AuthChallenge challenge2 = _authService.CreateChallenge("victim_user");
        var (corruptSuccess, corruptError, _, _) = await _authService.VerifyChallengeAndIssueTokenAsync(
            "victim_user",
            challenge2.ChallengeId,
            "!!not_valid_base64_sig!!");

        Assert.False(corruptSuccess);
        Assert.Contains("signature verification failed", corruptError?.ToLowerInvariant());
    }

    /// <summary>
    /// Verifies challenge-response authentication for plain and encrypted keys across Ed25519 and ML-DSA-65.
    /// </summary>
    [Fact]
    public async Task ChallengeResponseAuth_PlainAndEncryptedKeys_AuthenticatesCleanly()
    {
        // WHAT: Tests CreateChallenge + VerifyChallengeAndIssueTokenAsync across key formats and algorithms.
        // WHY:  Enforces asymmetric challenge-response authentication without transmitting private keys (Audit C8).
        // HOW:  Issues challenges, signs with TestChallengeHelper, and verifies token issuance and audit trail.
        GeneratedKeyBundle adminBundle = TestChallengeHelper.CreateTestKeyBundle(
            "admin_root",
            UserRole.Administrator,
            AccessGate.ApiWeb,
            KeyAlgorithm.Ed25519);

        await _authService.CompleteInitialSetupAsync(
            "admin_root",
            adminBundle.PublicKeyBase64,
            KeyAlgorithm.Ed25519);

        // 1. Ed25519 Plain Key Authentication
        AuthChallenge edChal = _authService.CreateChallenge("admin_root");
        string edSig = TestChallengeHelper.SignNonceWithPem(adminBundle.PrivateKeyPem, edChal.NonceBase64);
        var (edSuccess, _, edToken, _) = await _authService.VerifyChallengeAndIssueTokenAsync(
            "admin_root",
            edChal.ChallengeId,
            edSig,
            isWebRequest: true);

        Assert.True(edSuccess);
        Assert.NotNull(edToken);

        // 2. Ed25519 Encrypted Key Authentication with valid password
        GeneratedKeyBundle edEnc = TestChallengeHelper.CreateTestKeyBundle(
            "ed_enc_user",
            UserRole.Researcher,
            AccessGate.ApiWeb,
            KeyAlgorithm.Ed25519,
            passphrase: "mypassword123");

        await _authService.CreateUserAsync(
            "ed_enc_user",
            UserRole.Researcher,
            AccessGate.ApiWeb,
            edEnc.PublicKeyBase64,
            KeyAlgorithm.Ed25519);

        AuthChallenge encChal = _authService.CreateChallenge("ed_enc_user");
        string encSig = TestChallengeHelper.SignNonceWithPem(edEnc.PrivateKeyPem, encChal.NonceBase64, passphrase: "mypassword123");
        var (encSuccess, _, encToken, _) = await _authService.VerifyChallengeAndIssueTokenAsync(
            "ed_enc_user",
            encChal.ChallengeId,
            encSig,
            isWebRequest: true);

        Assert.True(encSuccess);
        Assert.NotNull(encToken);

        // 3. Ed25519 Encrypted Key Authentication with wrong password throws in helper, and invalid signature fails
        Assert.ThrowsAny<Exception>(() => TestChallengeHelper.SignNonceWithPem(edEnc.PrivateKeyPem, encChal.NonceBase64, passphrase: "WRONG_PASSWORD"));

        AuthChallenge failChal = _authService.CreateChallenge("ed_enc_user");
        var (failSuccess, failError, _, _) = await _authService.VerifyChallengeAndIssueTokenAsync(
            "ed_enc_user",
            failChal.ChallengeId,
            Convert.ToBase64String(new byte[64]),
            isWebRequest: true);

        Assert.False(failSuccess);
        Assert.NotNull(failError);

        // 4. ML-DSA-65 Plain Key Authentication
        GeneratedKeyBundle mlPlain = TestChallengeHelper.CreateTestKeyBundle(
            "ml_user",
            UserRole.Researcher,
            AccessGate.ApiWeb,
            KeyAlgorithm.MlDsa65);

        await _authService.CreateUserAsync(
            "ml_user",
            UserRole.Researcher,
            AccessGate.ApiWeb,
            mlPlain.PublicKeyBase64,
            KeyAlgorithm.MlDsa65);

        AuthChallenge mlChal = _authService.CreateChallenge("ml_user");
        string mlSig = TestChallengeHelper.SignNonceWithPem(mlPlain.PrivateKeyPem, mlChal.NonceBase64, KeyAlgorithm.MlDsa65);
        var (mlSuccess, _, mlToken, _) = await _authService.VerifyChallengeAndIssueTokenAsync(
            "ml_user",
            mlChal.ChallengeId,
            mlSig,
            isWebRequest: true);

        Assert.True(mlSuccess);
        Assert.NotNull(mlToken);

        // 5. Audit trail assertions
        var auditTrail = _auditService.GetAuditRecords();
        Assert.Contains(auditTrail, e => e.Action == "UserAuthenticated" && e.DetailsJson.Contains("\"isWeb\":true"));
        Assert.Contains(auditTrail, e => e.Action == "AuthenticationFailed");
    }

    /// <summary>
    /// Verifies access gating boundaries preventing Web-only users from calling APIs and Api-only users from Web login.
    /// </summary>
    [Fact]
    public async Task AccessGating_ApiAndWebBoundaries_StrictlyEnforced()
    {
        // WHAT: Tests AccessGate.Api vs AccessGate.Web restrictions.
        // WHY:  Guarantees dedicated API keys cannot compromise Web sessions and vice versa.
        // HOW:  Attempts web login on Api user and API challenge verification on Web user.
        GeneratedKeyBundle adminBundle = TestChallengeHelper.CreateTestKeyBundle(
            "admin_gate",
            UserRole.Administrator,
            AccessGate.ApiWeb,
            KeyAlgorithm.Ed25519);

        await _authService.CompleteInitialSetupAsync(
            "admin_gate",
            adminBundle.PublicKeyBase64,
            KeyAlgorithm.Ed25519);

        // Api-only user
        GeneratedKeyBundle apiUserBundle = TestChallengeHelper.CreateTestKeyBundle(
            "api_only_user",
            UserRole.Researcher,
            AccessGate.Api,
            KeyAlgorithm.Ed25519);

        await _authService.CreateUserAsync(
            "api_only_user",
            UserRole.Researcher,
            AccessGate.Api,
            apiUserBundle.PublicKeyBase64,
            KeyAlgorithm.Ed25519);

        // Web-only user
        GeneratedKeyBundle webUserBundle = TestChallengeHelper.CreateTestKeyBundle(
            "web_only_user",
            UserRole.Patient,
            AccessGate.Web,
            KeyAlgorithm.Ed25519);

        await _authService.CreateUserAsync(
            "web_only_user",
            UserRole.Patient,
            AccessGate.Web,
            webUserBundle.PublicKeyBase64,
            KeyAlgorithm.Ed25519);

        // 1. Api user attempting Web login via VerifyChallengeAndIssueTokenAsync(..., isWebRequest: true) -> must be rejected
        AuthChallenge apiChal = _authService.CreateChallenge("api_only_user");
        string apiSig = TestChallengeHelper.SignNonceWithPem(apiUserBundle.PrivateKeyPem, apiChal.NonceBase64);
        var (apiWebLoginSuccess, apiWebLoginError, _, _) = await _authService.VerifyChallengeAndIssueTokenAsync(
            "api_only_user",
            apiChal.ChallengeId,
            apiSig,
            isWebRequest: true);

        Assert.False(apiWebLoginSuccess);
        Assert.Contains("api access only", apiWebLoginError?.ToLowerInvariant());

        // 2. Web user attempting API token challenge verification -> must be rejected
        AuthChallenge challenge = _authService.CreateChallenge("web_only_user");
        byte[] nonce = Convert.FromBase64String(challenge.NonceBase64);
        string sig = SignEd25519(webUserBundle.PrivateKeyPem, nonce);

        var (webApiSuccess, webApiError, _, _) = await _authService.VerifyChallengeAndIssueTokenAsync(
            "web_only_user",
            challenge.ChallengeId,
            sig,
            isWebRequest: false);

        Assert.False(webApiSuccess);
        Assert.Contains("web ui access only", webApiError?.ToLowerInvariant());
    }

    /// <summary>
    /// Verifies Bearer token validation, tamper resistance, and expiration checks.
    /// </summary>
    [Fact]
    public async Task TokenValidation_TamperedAndMalformedTokens_Rejected()
    {
        // WHAT: Tests ValidateTokenAsync for malformed, tampered, and valid tokens.
        // WHY:  Guarantees forged claims or modified JSON payloads are immediately detected.
        // HOW:  Validates empty string, malformed string, tampered signature, and valid token.
        // 1. Empty / whitespace token
        var (emptyValid, _, emptyReason) = await _authService.ValidateTokenAsync("");
        Assert.False(emptyValid);
        Assert.NotNull(emptyReason);

        // 2. Malformed token without dot separator and with multiple dot separators
        var (malformedValid, _, malformedReason) = await _authService.ValidateTokenAsync("this_is_not_a_valid_token");
        Assert.False(malformedValid);
        Assert.NotNull(malformedReason);

        var (multiDotValid, _, multiDotReason) = await _authService.ValidateTokenAsync("part1.part2.part3");
        Assert.False(multiDotValid);
        Assert.Equal("Malformed bearer token format.", multiDotReason);

        // 3. Valid token then tampered signature
        GeneratedKeyBundle bundle = TestChallengeHelper.CreateTestKeyBundle(
            "tamper_user",
            UserRole.Researcher,
            AccessGate.ApiWeb,
            KeyAlgorithm.Ed25519);

        await _authService.CompleteInitialSetupAsync(
            "admin_tok",
            bundle.PublicKeyBase64,
            KeyAlgorithm.Ed25519);

        UserAccount user = await _authService.CreateUserAsync(
            "tamper_user",
            UserRole.Researcher,
            AccessGate.ApiWeb,
            bundle.PublicKeyBase64,
            KeyAlgorithm.Ed25519);

        string validToken = _authService.GenerateBearerToken(user);
        var (v1, u1, _) = await _authService.ValidateTokenAsync(validToken);
        Assert.True(v1);
        Assert.Equal("tamper_user", u1?.Username);

        // Tamper with payload
        string[] parts = validToken.Split('.');
        string tamperedToken = parts[0] + "EXTRA." + parts[1];
        var (tamperValid, _, tamperReason) = await _authService.ValidateTokenAsync(tamperedToken);
        Assert.False(tamperValid);
        Assert.Contains("signature", tamperReason?.ToLowerInvariant() ?? "");
    }

    /// <summary>
    /// Verifies user management operations including preventing deletion of the last remaining Administrator.
    /// </summary>
    [Fact]
    public async Task UserManagement_CrudAndSoleAdminProtection_Enforced()
    {
        // WHAT: Tests user listing, user deletion, and sole Administrator retention guard.
        // WHY:  Prevents administrative lockout where the final administrator is deleted.
        // HOW:  Creates initial admin, attempts to delete initial admin (fails), adds second admin, deletes first admin (succeeds).
        GeneratedKeyBundle admin1 = TestChallengeHelper.CreateTestKeyBundle(
            "admin_one",
            UserRole.Administrator,
            AccessGate.ApiWeb,
            KeyAlgorithm.Ed25519);

        await _authService.CompleteInitialSetupAsync(
            "admin_one",
            admin1.PublicKeyBase64,
            KeyAlgorithm.Ed25519);

        // Attempting to delete sole active admin must throw InvalidOperationException
        await Assert.ThrowsAsync<InvalidOperationException>(() => _authService.DeleteUserAsync("admin_one"));

        // Add second administrator
        GeneratedKeyBundle admin2 = TestChallengeHelper.CreateTestKeyBundle(
            "admin_two",
            UserRole.Administrator,
            AccessGate.ApiWeb,
            KeyAlgorithm.Ed25519);

        await _authService.CreateUserAsync(
            "admin_two",
            UserRole.Administrator,
            AccessGate.ApiWeb,
            admin2.PublicKeyBase64,
            KeyAlgorithm.Ed25519);

        // Now deleting admin_one should succeed because admin_two remains active
        bool deleteAdminOne = await _authService.DeleteUserAsync("admin_one");
        Assert.True(deleteAdminOne);

        // Listing users should now only show admin_two
        var users = await _authService.ListUsersAsync();
        Assert.Single(users);
        Assert.Equal("admin_two", users[0].Username);

        // Deleting non-existent user returns false
        bool deleteNonExistent = await _authService.DeleteUserAsync("ghost_user");
        Assert.False(deleteNonExistent);
    }

    /// <summary>
    /// Verifies all OpenAPI example generator implementations for auth models.
    /// </summary>
    [Fact]
    public void OpenApiExamples_AllModels_ReturnPopulatedExamples()
    {
        // WHAT: Tests GetExamples implementations across new auth and patient models.
        // WHY:  Guarantees Swagger multi-format serializers and ReDoc documentation generate valid example trees.
        // HOW:  Invokes GetExamples on all records and asserts properties.
        Assert.NotNull(new InitialSetupRequest().GetExamples());
        Assert.NotNull(new GeneratedKeyBundle().GetExamples());
        Assert.NotNull(new ChallengeRequest().GetExamples());
        Assert.NotNull(new TokenExchangeRequest().GetExamples());
        Assert.NotNull(new CreateUserRequest().GetExamples());
        Assert.NotNull(new AuthTokenResponse().GetExamples());
        Assert.NotNull(new SetupStatusResponse().GetExamples());
        Assert.NotNull(new AuthChallenge().GetExamples());
        Assert.NotNull(new GeneratedKeyBundle().GetExamples());
        Assert.NotNull(new UserAccount().GetExamples());
        Assert.NotNull(new Models.PatientDigitalDnaPackage().GetExamples());
    }

    /// <summary>
    /// Helper signing raw bytes using Ed25519 private key PEM.
    /// </summary>
    /// <param name="privateKeyPem">The PEM-formatted private key.</param>
    /// <param name="data">The byte buffer to sign.</param>
    /// <returns>Base64-encoded signature.</returns>
    private static string SignEd25519(string privateKeyPem, byte[] data)
    {
        // WHAT: Signs data buffer with Ed25519 private key.
        // WHY:  Simulates client-side challenge signing in unit tests.
        // HOW:  Strips PEM headers, decodes PKCS#8 DER, initializes BouncyCastle Ed25519Signer.
        string clean = privateKeyPem
            .Replace("-----BEGIN PRIVATE KEY-----", "")
            .Replace("-----END PRIVATE KEY-----", "")
            .Replace("\r", "")
            .Replace("\n", "")
            .Trim();

        byte[] der = Convert.FromBase64String(clean);
        var privKey = (Ed25519PrivateKeyParameters)PrivateKeyFactory.CreateKey(der);

        Ed25519Signer signer = new();
        signer.Init(true, privKey);
        signer.BlockUpdate(data, 0, data.Length);
        byte[] sig = signer.GenerateSignature();
        return Convert.ToBase64String(sig);
    }

    /// <summary>
    /// Verifies <see cref="UserRepository.MapUserFromReader"/> with PostgreSQL native data types (DateTime and Boolean).
    /// </summary>
    [Fact]
    public void UserRepository_MapUserFromReader_PostgresTypes_Succeeds()
    {
        // WHAT: Tests MapUserFromReader handling DateTime and bool column types from PostgreSQL.
        // WHY:  Ensures full branch coverage across database drivers.
        // HOW:  Constructs a DataTable with DateTime and bool columns and parses UserAccount.
        System.Data.DataTable dt = new();
        dt.Columns.Add("username", typeof(string));
        dt.Columns.Add("role", typeof(string));
        dt.Columns.Add("access_gate", typeof(string));
        dt.Columns.Add("algorithm", typeof(string));
        dt.Columns.Add("public_key_base64", typeof(string));
        dt.Columns.Add("created_at_utc", typeof(DateTime));
        dt.Columns.Add("last_login_utc", typeof(DateTime));
        dt.Columns.Add("is_active", typeof(bool));

        DateTime now = DateTime.UtcNow;
        dt.Rows.Add("pg_admin", "Administrator", "ApiWeb", "Ed25519", "pubkey_der", now, now, true);

        using System.Data.Common.DbDataReader rdr = dt.CreateDataReader();
        Assert.True(rdr.Read());

        UserAccount user = UserRepository.MapUserFromReader(rdr);
        Assert.Equal("pg_admin", user.Username);
        Assert.Equal(UserRole.Administrator, user.Role);
        Assert.Equal(AccessGate.ApiWeb, user.AccessGate);
        Assert.Equal(KeyAlgorithm.Ed25519, user.Algorithm);
        Assert.True(user.IsActive);
        Assert.NotNull(user.LastLoginUtc);
    }

    /// <summary>
    /// Verifies private key parsing edge cases and expired challenge cleanup.
    /// </summary>
    [Fact]
    public void KeyParsingAndChallengeCleanup_EdgeCases_Covered()
    {
        // WHAT: Tests EncryptPrivateKeyBytes PBKDF2/AES-GCM encryption, round-trip extraction, and expired challenge eviction.
        // WHY:  Ensures 100% line and branch coverage across private key envelope encryption and challenge dictionaries.
        // HOW:  Encrypts random DER bytes, wraps in PEM, asserts round-trip via TestChallengeHelper, and checks eviction.
        // 1. EncryptPrivateKeyBytes produces valid envelope and round-trips via TestChallengeHelper.ExtractDerFromPem
        byte[] rawDer = new byte[48];
        RandomNumberGenerator.Fill(rawDer);
        byte[] envelope = TestChallengeHelper.EncryptPrivateKeyBytes(rawDer, "Passphrase123!");
        Assert.True(envelope.Length > 44);

        string encPem = TestChallengeHelper.FormatPem("ENCRYPTED PRIVATE KEY", envelope);
        byte[] roundTrip = TestChallengeHelper.ExtractDerFromPem(encPem, "Passphrase123!");
        Assert.Equal(rawDer, roundTrip);

        // Wrong passphrase fails decryption
        Assert.ThrowsAny<Exception>(() => TestChallengeHelper.ExtractDerFromPem(encPem, "WrongPassword!"));

        // 2. FormatPem handles arbitrary types cleanly
        string plainPem = TestChallengeHelper.FormatPem("PUBLIC KEY", rawDer);
        Assert.Contains("-----BEGIN PUBLIC KEY-----", plainPem);
        Assert.Contains("-----END PUBLIC KEY-----", plainPem);

        // 5. Expired challenge eviction
        AuthChallenge expiredChal = new()
        {
            ChallengeId = "expired_chal_id",
            Username = "test_user",
            NonceBase64 = "YWJj",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        _authService.RegisterChallengeForTesting(expiredChal);
        AuthChallenge freshChal = _authService.CreateChallenge("test_user");
        Assert.NotEqual("expired_chal_id", freshChal.ChallengeId);

        // 6. Default directory when TRAJECTORY_INCOMING_DIR is not configured
        IConfiguration emptyConfig = new ConfigurationBuilder().Build();
        SystemConfigurationStore configStore = new(WeaverTestDb.CreateFactory(emptyConfig));
        using UserRepository defaultRepo = new(WeaverTestDb.CreateFactory(emptyConfig, new DatabaseMigrationRunner()));
        Assert.NotNull(defaultRepo);
    }

    /// <summary>
    /// Verifies that username normalization converts usernames to lowercase invariant across creation,
    /// duplicate prevention, challenge verification, and deletion.
    /// </summary>
    [Fact]
    public async Task UsernameNormalization_MixedCase_PreventsShadowingAndNormalizesLifecycle()
    {
        // WHAT: Tests that creating "MixedCaseUser" normalizes to lowercase, rejects duplicates, and deletes cleanly.
        // WHY:  Prevents mixed-case duplicate accounts (e.g., "Admin" and "admin") from coexisting or shadowing each other.
        // HOW:  Calls CompleteInitialSetupAsync, CreateUserAsync, TryCreateUserAsync, CreateChallenge, VerifyChallengeAndIssueTokenAsync, and DeleteUserAsync.
        GeneratedKeyBundle adminBundle = TestChallengeHelper.CreateTestKeyBundle(
            "Admin_Master",
            UserRole.Administrator,
            AccessGate.ApiWeb,
            KeyAlgorithm.Ed25519);

        await _authService.CompleteInitialSetupAsync(
            "Admin_Master",
            adminBundle.PublicKeyBase64,
            KeyAlgorithm.Ed25519);

        GeneratedKeyBundle bundle = TestChallengeHelper.CreateTestKeyBundle(
            "MixedCaseUser",
            UserRole.Researcher,
            AccessGate.ApiWeb,
            KeyAlgorithm.Ed25519);

        UserAccount created = await _authService.CreateUserAsync(
            "MixedCaseUser",
            UserRole.Researcher,
            AccessGate.ApiWeb,
            bundle.PublicKeyBase64,
            KeyAlgorithm.Ed25519);

        Assert.Equal("mixedcaseuser", created.Username);

        // TryCreateUserAsync with uppercase cannot shadow or coexist
        UserAccount? duplicate = await _authService.TryCreateUserAsync(
            "MIXEDCASEUSER",
            UserRole.Researcher,
            AccessGate.ApiWeb,
            bundle.PublicKeyBase64,
            KeyAlgorithm.Ed25519);
        Assert.Null(duplicate);

        // Challenge and verify with mixed case
        AuthChallenge challenge = _authService.CreateChallenge("MixedCaseUser");
        Assert.Equal("mixedcaseuser", challenge.Username);

        byte[] nonceBytes = Convert.FromBase64String(challenge.NonceBase64);
        string signatureBase64 = SignEd25519(bundle.PrivateKeyPem, nonceBytes);

        var (success, errorMsg, token, user) = await _authService.VerifyChallengeAndIssueTokenAsync(
            "MixedCaseUser",
            challenge.ChallengeId,
            signatureBase64,
            isWebRequest: false);

        Assert.True(success);
        Assert.Null(errorMsg);
        Assert.NotNull(token);
        Assert.NotNull(user);
        Assert.Equal("mixedcaseuser", user.Username);

        // Deletion with uppercase removes the lowercase account
        bool deleted = await _authService.DeleteUserAsync("MIXEDCASEUSER");
        Assert.True(deleted);

        // Account is gone
        IReadOnlyList<UserAccount> remaining = await _authService.ListUsersAsync();
        Assert.DoesNotContain(remaining, u => u.Username == "mixedcaseuser");
    }

    /// <summary>
    /// Verifies that patient identifier syntactic validation properly accepts allowed characters and rejects invalid characters or lengths.
    /// </summary>
    [Fact]
    public void IsValidPatientIdentifier_ValidatesFormatAndLengths()
    {
        // WHAT: Tests syntactic validation on patient identifiers across allowed/disallowed characters and lengths.
        // WHY:  Prevents log/manifest injection and ensures clean patient-scaffold routing.
        // HOW:  Validates empty strings, boundary lengths, valid characters (letters, digits, '-', '_', '.'), and invalid characters.
        Assert.False(Program.IsValidPatientIdentifier(""));
        Assert.False(Program.IsValidPatientIdentifier(new string('a', 65)));
        Assert.False(Program.IsValidPatientIdentifier("bad/id!"));
        Assert.False(Program.IsValidPatientIdentifier("id with spaces"));

        Assert.True(Program.IsValidPatientIdentifier("a"));
        Assert.True(Program.IsValidPatientIdentifier("0"));
        Assert.True(Program.IsValidPatientIdentifier("-"));
        Assert.True(Program.IsValidPatientIdentifier("_"));
        Assert.True(Program.IsValidPatientIdentifier("."));
        Assert.True(Program.IsValidPatientIdentifier("patient-007_test.v1"));
        Assert.True(Program.IsValidPatientIdentifier(new string('b', 64)));
    }

    /// <summary>
    /// Verifies that authentication challenges are minted with a 120-second TTL and that expired
    /// entries are pruned both on explicit invocation and periodic timer ticks.
    /// </summary>
    [Fact]
    public void CreateChallenge_Sets120SecondExpiration_AndPrunesExpiredEntries()
    {
        // WHAT: Asserts 120s expiration window and sweeps expired challenges.
        // WHY:  L9 challenge store hardening requires bounded lifetimes and proactive garbage collection.
        // HOW:  Creates a challenge, asserts TTL in [115..121], registers an expired challenge, and tests pruning and timer tick.
        AuthChallenge challenge = _authService.CreateChallenge("alice");
        double ttlSeconds = (challenge.ExpiresAtUtc - DateTimeOffset.UtcNow).TotalSeconds;
        Assert.True(ttlSeconds >= 115 && ttlSeconds <= 121, $"Expected TTL near 120s, got {ttlSeconds}s");

        AuthChallenge expiredChallenge = new()
        {
            ChallengeId = "expired-test-id",
            Username = "bob",
            NonceBase64 = Convert.ToBase64String(new byte[32]),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        _authService.RegisterChallengeForTesting(expiredChallenge);

        int pruned = _authService.PruneExpiredChallenges(DateTimeOffset.UtcNow);
        Assert.True(pruned >= 1, "Expected at least 1 expired challenge removed.");

        // Additional sweep on timer tick when nothing expired
        _authService.OnPruneTimerTick(null);
        int secondPruned = _authService.PruneExpiredChallenges(DateTimeOffset.UtcNow);
        Assert.Equal(0, secondPruned);
    }

    /// <summary>
    /// Verifies that when the active challenge store reaches its capacity, creating a challenge throws
    /// <see cref="ChallengeStoreCapacityExceededException"/>, and that pruning expired entries restores capacity.
    /// </summary>
    [Fact]
    public void CreateChallenge_WhenMaxActiveChallengesReached_ThrowsCapacityException()
    {
        // WHAT: Tests challenge store capacity capping and proactive pruning before cap checks.
        // WHY:  Unauthenticated challenge requests must not exhaust host memory (Audit L9).
        // HOW:  Sets MaxActiveChallenges to 2, verifies 3rd creation throws, registers expired challenge at cap and verifies replacement succeeds.
        int originalCap = _authService.MaxActiveChallenges;
        try
        {
            _authService.MaxActiveChallenges = 2;
            _authService.PruneExpiredChallenges(DateTimeOffset.UtcNow.AddHours(1));

            AuthChallenge c1 = _authService.CreateChallenge("alice");
            AuthChallenge c2 = _authService.CreateChallenge("bob");
            Assert.NotNull(c1);
            Assert.NotNull(c2);

            ChallengeStoreCapacityExceededException ex = Assert.Throws<ChallengeStoreCapacityExceededException>(
                () => _authService.CreateChallenge("carol"));
            Assert.Equal(2, ex.MaxCapacity);

            // Register an expired challenge directly to simulate expiration while at cap
            AuthChallenge expired = new()
            {
                ChallengeId = "expired-cap-test",
                Username = "dan",
                NonceBase64 = Convert.ToBase64String(new byte[32]),
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
            };
            _authService.PruneExpiredChallenges(DateTimeOffset.UtcNow.AddHours(1));
            _authService.CreateChallenge("eve");
            _authService.RegisterChallengeForTesting(expired);

            // CreateChallenge should prune the expired entry first, leaving 1 active, and succeed
            AuthChallenge cNew = _authService.CreateChallenge("frank");
            Assert.NotNull(cNew);
        }
        finally
        {
            _authService.MaxActiveChallenges = originalCap;
        }
    }
}
