using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Moq;
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
/// Unit test suite proving that idle session expiration is enforced on the server (Prime Directive 12):
/// a bearer token presented after its idle window is refused, genuine activity inside the window extends it,
/// and merely presenting the token again - which is all a page reload does - extends nothing.
/// </summary>
public sealed class SessionIdleEnforcementTests : IDisposable
{
    /// <summary>
    /// Deterministic, manually advanced clock injected into the session activity tracker.
    /// </summary>
    /// <remarks>
    /// Prime Directive 6 forbids wall-clock waits in tests; the idle window is therefore crossed by
    /// moving this clock rather than by sleeping for fifteen minutes.
    /// </remarks>
    private sealed class ManualTimeProvider : TimeProvider
    {
        /// <summary>
        /// Gets or sets the instant that <see cref="GetUtcNow"/> reports.
        /// </summary>
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

        /// <summary>
        /// Advances the clock by the supplied duration.
        /// </summary>
        /// <param name="delta">The amount of time to move forward.</param>
        public void Advance(TimeSpan delta) => UtcNow += delta;

        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    /// <summary>
    /// Temporary directory isolating the SQLite user database for this test class.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// User account persistence repository backing the real authentication service.
    /// </summary>
    private readonly UserRepository _userRepository;

    /// <summary>
    /// Real authentication service used for the end-to-end idle expiry proofs.
    /// </summary>
    private readonly UserAuthenticationService _authService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionIdleEnforcementTests"/> class.
    /// </summary>
    public SessionIdleEnforcementTests()
    {
        // WHAT: Builds an isolated SQLite-backed authentication stack in a private temp directory.
        // WHY:  Tests must never bind to the live simulations/shared-data directory or to an
        //       ambient PostgreSQL instance, or their outcome depends on the machine they run on.
        // HOW:  Points TRAJECTORY_INCOMING_DIR at a per-class temp directory and aims the Postgres
        //       connection string at a deliberately unreachable port so SQLite is always chosen.
        _tempDir = Path.Combine(Path.GetTempPath(), "weaver_idle_tests_" + Guid.NewGuid().ToString("N"));
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

        // WHAT: Provisions this test instance's own bearer-token signing key inside its temp directory.
        // WHY:  The signing key is no longer derived from a constant published in the source; the service now
        //       takes it from INodeSecretStore, and deriving the directory from TRAJECTORY_INCOMING_DIR keeps
        //       the generated key files isolated per test class.
        // HOW:  NodeSecretStore resolves '<temp>/node-keys' from the configuration built above.
        NodeSecretStore secretStore = new(configuration);

        PostQuantumCryptoProvider pqcProvider = new();
        ProfileDataProtector protector = new(secretStore);
        SubjectKeyRepository subjectKeyRepo = new(WeaverTestDb.CreateFactory(configuration));
        SubjectErasureJournal journal = new(secretStore);
        using SubjectKeyVault subjectKeyVault = new(subjectKeyRepo, protector, pqcProvider, journal);
        subjectKeyVault.InitializeAsync().GetAwaiter().GetResult();
        KeyManagementService keyManagement = new(pqcProvider, subjectKeyVault, secretStore);
        ComplianceAuditService auditService = new(keyManagement, pqcProvider);

        _userRepository = new UserRepository(WeaverTestDb.CreateFactory(configuration, new DatabaseMigrationRunner()));
        _userRepository.InitializeAsync().GetAwaiter().GetResult();

        _authService = new UserAuthenticationService(_userRepository, pqcProvider, auditService, secretStore);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
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
    /// Builds a syntactically valid bearer token payload carrying the supplied issuance time.
    /// </summary>
    /// <param name="username">Username to seal into the claims.</param>
    /// <param name="issuedAt">Issuance timestamp sealed into the claims.</param>
    /// <returns>A token in <c>payload.signature</c> form; the signature is not meaningful to the tracker.</returns>
    private static string BuildToken(string username, DateTimeOffset issuedAt)
    {
        AuthTokenClaims claims = new()
        {
            Username = username,
            Role = UserRole.Administrator,
            AccessGate = AccessGate.ApiWeb,
            IssuedAtUnixSeconds = issuedAt.ToUnixTimeSeconds(),
            ExpiresAtUnixSeconds = issuedAt.AddHours(8).ToUnixTimeSeconds()
        };

        string payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(claims))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        return payload + ".c2lnbmF0dXJl";
    }

    /// <summary>
    /// Creates an <see cref="IHttpContextAccessor"/> reporting a request for the supplied path.
    /// </summary>
    /// <param name="path">Request path to report, or null to report no ambient HTTP request at all.</param>
    /// <returns>A configured accessor stub.</returns>
    private static IHttpContextAccessor BuildAccessor(string? path)
    {
        Mock<IHttpContextAccessor> accessor = new();
        if (path == null)
        {
            accessor.SetupGet(a => a.HttpContext).Returns((HttpContext?)null);
        }
        else
        {
            DefaultHttpContext context = new();
            context.Request.Path = path;
            accessor.SetupGet(a => a.HttpContext).Returns(context);
        }

        return accessor.Object;
    }

    /// <summary>
    /// Verifies that the tracker rejects a positive-duration-violating configuration and exposes its defaults.
    /// </summary>
    [Fact]
    public void Tracker_Construction_ValidatesIdleWindowAndDefaultsToFifteenMinutes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServerSessionActivityTracker(null, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServerSessionActivityTracker(null, TimeSpan.FromMinutes(-5)));

        ServerSessionActivityTracker systemClockTracker = new();
        Assert.Equal(TimeSpan.FromMinutes(15), systemClockTracker.IdleTimeout);
        Assert.Equal(0, systemClockTracker.TrackedSessionCount);

        // A token minted "now" against the real system clock must be live immediately.
        Assert.True(systemClockTracker.IsSessionActive(BuildToken("admin", DateTimeOffset.UtcNow)));
        Assert.Equal(1, systemClockTracker.TrackedSessionCount);
    }

    /// <summary>
    /// Verifies that every tracker entry point rejects a missing or blank token.
    /// </summary>
    [Fact]
    public void Tracker_BlankToken_IsRejected()
    {
        ServerSessionActivityTracker tracker = new(new ManualTimeProvider());

        Assert.Throws<ArgumentException>(() => tracker.IsSessionActive("   "));
        Assert.Throws<ArgumentException>(() => tracker.RecordActivity(string.Empty));
        Assert.Throws<ArgumentNullException>(() => tracker.Revoke(null!)); // Justification: asserting the guard rejects null.
    }

    /// <summary>
    /// Verifies that a session dies exactly once its idle window elapses and can never be revived afterwards.
    /// </summary>
    [Fact]
    public void Tracker_IdleWindowElapses_SessionDiesAndStaysDead()
    {
        ManualTimeProvider clock = new();
        ServerSessionActivityTracker tracker = new(clock, TimeSpan.FromMinutes(15));
        string token = BuildToken("admin", clock.UtcNow);

        Assert.True(tracker.IsSessionActive(token));

        // Inside the window: still live, and repeated presentation does not extend anything.
        clock.Advance(TimeSpan.FromMinutes(14));
        Assert.True(tracker.IsSessionActive(token));

        // Crossing the window kills it, and the tombstone keeps it dead.
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.False(tracker.IsSessionActive(token));
        Assert.False(tracker.IsSessionActive(token));
        Assert.False(tracker.RecordActivity(token));
        Assert.False(tracker.Revoke(token));
    }

    /// <summary>
    /// Verifies that recorded activity inside the window extends the session, and only that.
    /// </summary>
    [Fact]
    public void Tracker_RecordedActivity_ExtendsTheIdleWindow()
    {
        ManualTimeProvider clock = new();
        ServerSessionActivityTracker tracker = new(clock, TimeSpan.FromMinutes(15));
        string token = BuildToken("researcher", clock.UtcNow);

        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.True(tracker.RecordActivity(token));

        // 20 minutes after issuance, but only 10 minutes after the heartbeat: still live.
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.True(tracker.IsSessionActive(token));

        // 16 minutes after that heartbeat with nothing recorded in between: dead.
        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.False(tracker.IsSessionActive(token));
    }

    /// <summary>
    /// Verifies that an explicit revocation kills a session immediately, well inside its idle window.
    /// </summary>
    [Fact]
    public void Tracker_Revoke_KillsLiveSessionImmediately()
    {
        ManualTimeProvider clock = new();
        ServerSessionActivityTracker tracker = new(clock, TimeSpan.FromMinutes(15));
        string token = BuildToken("admin", clock.UtcNow);

        Assert.True(tracker.IsSessionActive(token));
        Assert.True(tracker.Revoke(token));
        Assert.False(tracker.Revoke(token));
        Assert.False(tracker.IsSessionActive(token));
        Assert.False(tracker.RecordActivity(token));
    }

    /// <summary>
    /// Verifies that a token this node has never seen is judged from its own issuance claim, so a harvested
    /// token cannot buy a fresh idle window by being presented to a restarted process.
    /// </summary>
    [Fact]
    public void Tracker_UnseenToken_IsJudgedFromItsIssuanceClaim()
    {
        ManualTimeProvider clock = new();
        ServerSessionActivityTracker tracker = new(clock, TimeSpan.FromMinutes(15));

        string freshToken = BuildToken("admin", clock.UtcNow - TimeSpan.FromMinutes(5));
        string staleToken = BuildToken("admin", clock.UtcNow - TimeSpan.FromHours(3));

        Assert.True(tracker.IsSessionActive(freshToken));
        Assert.False(tracker.IsSessionActive(staleToken));
    }

    /// <summary>
    /// Verifies that tokens whose payload carries no readable issuance claim fall back to the current time,
    /// across every Base64Url padding length.
    /// </summary>
    [Fact]
    public void Tracker_UnreadableOrVariableLengthPayloads_AreHandledDeterministically()
    {
        ManualTimeProvider clock = new();
        ServerSessionActivityTracker tracker = new(clock, TimeSpan.FromMinutes(15));

        // No separator at all, non-Base64 payload, and a payload deserializing to null all fall back to "now".
        Assert.True(tracker.IsSessionActive("opaque-token-without-a-separator"));
        Assert.True(tracker.IsSessionActive(".leading-separator"));
        Assert.True(tracker.IsSessionActive("not*valid*base64.signature"));

        string nullPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes("null")).TrimEnd('=');
        Assert.True(tracker.IsSessionActive(nullPayload + ".signature"));

        // Real payloads exercise every Base64Url padding remainder (0, 2 and 3 characters).
        HashSet<int> observedRemainders = [];
        for (int nameLength = 1; nameLength <= 12; nameLength++)
        {
            string token = BuildToken(new string('u', nameLength), clock.UtcNow);
            observedRemainders.Add(token[..token.IndexOf('.')].Length % 4);
            Assert.True(tracker.IsSessionActive(token));
        }

        Assert.Contains(0, observedRemainders);
        Assert.Contains(2, observedRemainders);
        Assert.Contains(3, observedRemainders);
    }

    /// <summary>
    /// Verifies that the registry discards records older than the retention window while keeping recent ones.
    /// </summary>
    [Fact]
    public void Tracker_Housekeeping_DiscardsRecordsOlderThanRetention()
    {
        ManualTimeProvider clock = new();
        ServerSessionActivityTracker tracker = new(clock, TimeSpan.FromMinutes(15));

        tracker.IsSessionActive(BuildToken("admin", clock.UtcNow));
        Assert.Equal(1, tracker.TrackedSessionCount);

        // Within the prune interval nothing is swept, even for a second session.
        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.IsSessionActive(BuildToken("researcher", clock.UtcNow));
        Assert.Equal(2, tracker.TrackedSessionCount);

        // Past the 12-hour retention window both stale records are swept, leaving only the new session.
        clock.Advance(TimeSpan.FromHours(13));
        string patientToken = BuildToken("patient", clock.UtcNow);
        tracker.IsSessionActive(patientToken);
        Assert.Equal(1, tracker.TrackedSessionCount);

        // A sweep that finds only a recent record keeps it.
        clock.Advance(TimeSpan.FromMinutes(2));
        tracker.IsSessionActive(patientToken);
        Assert.Equal(1, tracker.TrackedSessionCount);
    }

    /// <summary>
    /// Verifies that the decorator validates its collaborators.
    /// </summary>
    [Fact]
    public void Decorator_Construction_RejectsMissingCollaborators()
    {
        Mock<IUserAuthenticationService> inner = new();
        Mock<IServerSessionActivityTracker> tracker = new();

        Assert.Throws<ArgumentNullException>(() => new SessionIdleEnforcingAuthenticationService(null!, tracker.Object)); // Justification: asserting the guard rejects null.
        Assert.Throws<ArgumentNullException>(() => new SessionIdleEnforcingAuthenticationService(inner.Object, null!)); // Justification: asserting the guard rejects null.
    }

    /// <summary>
    /// Verifies that the decorator passes an inner rejection straight through without consulting the registry.
    /// </summary>
    [Fact]
    public async Task Decorator_InnerRejection_IsReturnedUntouched()
    {
        Mock<IUserAuthenticationService> inner = new();
        Mock<IServerSessionActivityTracker> tracker = new();

        inner.Setup(s => s.ValidateTokenAsync("bad", It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, null, "Token signature verification failed."));

        inner.Setup(s => s.ValidateTokenAsync("valid-but-no-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, null));

        SessionIdleEnforcingAuthenticationService service = new(inner.Object, tracker.Object);

        var rejected = await service.ValidateTokenAsync("bad");
        Assert.False(rejected.Valid);
        Assert.Equal("Token signature verification failed.", rejected.ErrorReason);

        var userless = await service.ValidateTokenAsync("valid-but-no-user");
        Assert.True(userless.Valid);
        Assert.Null(userless.User);

        tracker.Verify(t => t.IsSessionActive(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// Verifies that an idle-expired session is refused with an informative, non-disclosing reason.
    /// </summary>
    [Fact]
    public async Task Decorator_IdleExpiredSession_IsRefused()
    {
        Mock<IUserAuthenticationService> inner = new();
        Mock<IServerSessionActivityTracker> tracker = new();

        UserAccount admin = new() { Username = "admin", Role = UserRole.Administrator };
        inner.Setup(s => s.ValidateTokenAsync("token", It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, admin, null));
        tracker.Setup(t => t.IsSessionActive("token")).Returns(false);
        tracker.SetupGet(t => t.IdleTimeout).Returns(TimeSpan.FromMinutes(15));

        SessionIdleEnforcingAuthenticationService service = new(inner.Object, tracker.Object);

        var result = await service.ValidateTokenAsync("token");
        Assert.False(result.Valid);
        Assert.Null(result.User);
        Assert.Equal("Session expired after 15 minutes of inactivity. Please sign in again.", result.ErrorReason);
        tracker.Verify(t => t.RecordActivity(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// Verifies that a REST API call counts as activity while a bare token presentation does not.
    /// </summary>
    [Fact]
    public async Task Decorator_OnlyApiRequestsCountAsActivity()
    {
        UserAccount admin = new() { Username = "admin", Role = UserRole.Administrator };

        foreach ((string? path, int expectedHeartbeats) in new (string?, int)[]
        {
            (null, 0),            // Blazor circuit restoring a session after a page reload
            ("/trajectories", 0), // Interactive Web UI page load
            ("/api/datasets", 1)  // Genuine REST API traffic from the token holder
        })
        {
            Mock<IUserAuthenticationService> inner = new();
            Mock<IServerSessionActivityTracker> tracker = new();

            inner.Setup(s => s.ValidateTokenAsync("token", It.IsAny<CancellationToken>()))
                .ReturnsAsync((true, admin, null));
            tracker.Setup(t => t.IsSessionActive("token")).Returns(true);

            SessionIdleEnforcingAuthenticationService service = new(inner.Object, tracker.Object, BuildAccessor(path));

            var result = await service.ValidateTokenAsync("token");
            Assert.True(result.Valid);
            Assert.Same(admin, result.User);
            tracker.Verify(t => t.RecordActivity("token"), Times.Exactly(expectedHeartbeats));
        }
    }

    /// <summary>
    /// Verifies that every member the decorator does not police is delegated verbatim to the inner service.
    /// </summary>
    [Fact]
    public async Task Decorator_DelegatesEveryOtherMemberToInnerService()
    {
        Mock<IUserAuthenticationService> inner = new(MockBehavior.Strict);
        Mock<IServerSessionActivityTracker> tracker = new();

        UserAccount account = new() { Username = "admin", Role = UserRole.Administrator };
        AuthChallenge challenge = new() { ChallengeId = "cid", Username = "admin", NonceBase64 = "bm9uY2U=" };
        GeneratedKeyBundle bundle = new() { Username = "admin" };
        IReadOnlyList<UserAccount> accounts = [account];

        inner.Setup(s => s.IsSetupRequiredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        inner.Setup(s => s.CompleteInitialSetupAsync("admin", "pub", KeyAlgorithm.Ed25519, AccessGate.ApiWeb, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        inner.Setup(s => s.GenerateSecureKeyBundle("admin", UserRole.Administrator, AccessGate.ApiWeb, KeyAlgorithm.Ed25519, "pass"))
            .Returns(bundle);
        inner.Setup(s => s.CreateChallenge("admin")).Returns(challenge);
        inner.Setup(s => s.VerifyChallengeAndIssueTokenAsync("admin", "cid", "sig", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, "token", account));
        inner.Setup(s => s.AuthenticateWithPrivateKeyAsync("admin", "pem", "pass", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, "token", account));
        inner.Setup(s => s.ListUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(accounts);
        inner.Setup(s => s.CreateUserAsync("admin", UserRole.Administrator, AccessGate.ApiWeb, "pub", KeyAlgorithm.Ed25519, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        inner.Setup(s => s.TryCreateUserAsync("admin", UserRole.Administrator, AccessGate.ApiWeb, "pub", KeyAlgorithm.Ed25519, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        inner.Setup(s => s.GenerateBearerToken(account)).Returns("minted-token");
        inner.Setup(s => s.DeleteUserAsync("admin", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        inner.Setup(s => s.PruneExpiredChallenges(now)).Returns(3);

        SessionIdleEnforcingAuthenticationService service = new(inner.Object, tracker.Object);

        Assert.True(await service.IsSetupRequiredAsync());
        Assert.Same(account, await service.CompleteInitialSetupAsync("admin", "pub", KeyAlgorithm.Ed25519));
        Assert.Same(bundle, service.GenerateSecureKeyBundle("admin", UserRole.Administrator, AccessGate.ApiWeb, KeyAlgorithm.Ed25519, "pass"));
        Assert.Same(challenge, service.CreateChallenge("admin"));
        Assert.Equal("token", (await service.VerifyChallengeAndIssueTokenAsync("admin", "cid", "sig", true)).Token);
        Assert.Equal("token", (await service.AuthenticateWithPrivateKeyAsync("admin", "pem", "pass")).Token);
        Assert.Same(accounts, await service.ListUsersAsync());
        Assert.Same(account, await service.CreateUserAsync("admin", UserRole.Administrator, AccessGate.ApiWeb, "pub", KeyAlgorithm.Ed25519));
        Assert.Same(account, await service.TryCreateUserAsync("admin", UserRole.Administrator, AccessGate.ApiWeb, "pub", KeyAlgorithm.Ed25519));
        Assert.Equal("minted-token", service.GenerateBearerToken(account));
        Assert.True(await service.DeleteUserAsync("admin"));
        Assert.Equal(3, service.PruneExpiredChallenges(now));

        inner.VerifyAll();
    }

    /// <summary>
    /// End-to-end proof of the C7 fix against the real authentication service: a genuine, cryptographically
    /// valid, non-expired bearer token is refused once its idle window has elapsed, a heartbeat inside the
    /// window keeps it alive, and presenting it again (a page reload) buys it nothing.
    /// </summary>
    [Fact]
    public async Task EndToEnd_RealTokenIsRefusedAfterIdleWindowAndReloadNeverExtendsIt()
    {
        UserAccount admin = await _authService.CompleteInitialSetupAsync(
            "idle_admin",
            "MCowBQYDK2VwAyEA1111111111111111111111111111111111111111111=",
            KeyAlgorithm.Ed25519,
            AccessGate.ApiWeb);

        // 1. A token presented after the idle window is refused, although its absolute 8-hour expiry
        //    has not been reached and the inner service still considers it perfectly valid.
        ManualTimeProvider clock = new() { UtcNow = DateTimeOffset.UtcNow };
        ServerSessionActivityTracker tracker = new(clock, TimeSpan.FromMinutes(15));
        SessionIdleEnforcingAuthenticationService guarded = new(_authService, tracker);

        string token = _authService.GenerateBearerToken(admin);
        Assert.True((await guarded.ValidateTokenAsync(token)).Valid);

        clock.Advance(TimeSpan.FromMinutes(16));
        var afterIdle = await guarded.ValidateTokenAsync(token);
        Assert.False(afterIdle.Valid);
        Assert.Null(afterIdle.User);
        Assert.Contains("inactivity", afterIdle.ErrorReason);

        // The inner service, left to itself, would still have accepted it - that is precisely the defect.
        Assert.True((await _authService.ValidateTokenAsync(token)).Valid);

        // 2. A DOM heartbeat inside the window extends the session.
        ManualTimeProvider activeClock = new() { UtcNow = DateTimeOffset.UtcNow };
        ServerSessionActivityTracker activeTracker = new(activeClock, TimeSpan.FromMinutes(15));
        SessionIdleEnforcingAuthenticationService activeGuarded = new(_authService, activeTracker);
        string activeToken = _authService.GenerateBearerToken(admin);

        Assert.True((await activeGuarded.ValidateTokenAsync(activeToken)).Valid);
        activeClock.Advance(TimeSpan.FromMinutes(14));
        Assert.True(activeTracker.RecordActivity(activeToken));
        activeClock.Advance(TimeSpan.FromMinutes(14));
        Assert.True((await activeGuarded.ValidateTokenAsync(activeToken)).Valid);

        // 3. Reloading the page only re-presents the token, so it does not extend anything: twenty-eight
        //    minutes of reloads with no recorded activity still end in a refusal.
        ManualTimeProvider reloadClock = new() { UtcNow = DateTimeOffset.UtcNow };
        ServerSessionActivityTracker reloadTracker = new(reloadClock, TimeSpan.FromMinutes(15));
        SessionIdleEnforcingAuthenticationService reloadGuarded = new(_authService, reloadTracker);
        string reloadToken = _authService.GenerateBearerToken(admin);

        Assert.True((await reloadGuarded.ValidateTokenAsync(reloadToken)).Valid);
        reloadClock.Advance(TimeSpan.FromMinutes(14));
        Assert.True((await reloadGuarded.ValidateTokenAsync(reloadToken)).Valid);
        reloadClock.Advance(TimeSpan.FromMinutes(14));
        Assert.False((await reloadGuarded.ValidateTokenAsync(reloadToken)).Valid);

        // 4. Signing out revokes the credential itself, so a copy taken beforehand is worthless.
        ManualTimeProvider signOutClock = new() { UtcNow = DateTimeOffset.UtcNow };
        ServerSessionActivityTracker signOutTracker = new(signOutClock, TimeSpan.FromMinutes(15));
        SessionIdleEnforcingAuthenticationService signOutGuarded = new(_authService, signOutTracker);
        string signOutToken = _authService.GenerateBearerToken(admin);

        Assert.True((await signOutGuarded.ValidateTokenAsync(signOutToken)).Valid);
        Assert.True(signOutTracker.Revoke(signOutToken));
        Assert.False((await signOutGuarded.ValidateTokenAsync(signOutToken)).Valid);
    }
}
