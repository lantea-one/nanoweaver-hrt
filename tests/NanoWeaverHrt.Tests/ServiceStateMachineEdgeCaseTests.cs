using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NanoWeaverHrt.Bridges;
using NanoWeaverHrt.Caching;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Database;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Events;
using NanoWeaverHrt.Events.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Security.Secrets;
using NanoWeaverHrt.Services.Stack;
using StackExchange.Redis;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Unit tests targeting defensive edge-case branches and asynchronous state machine
/// execution paths across core platform services and caching layers.
/// </summary>
public sealed class ServiceStateMachineEdgeCaseTests : IDisposable
{
    /// <summary>
    /// Temporary directory holding SQLite test databases and local artifacts.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Real SQLite database connection factory for repository execution.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// User repository backed by isolated SQLite database.
    /// </summary>
    private readonly UserRepository _userRepository;

    /// <summary>
    /// Compliance audit service mock.
    /// </summary>
    private readonly Mock<IComplianceAuditService> _auditServiceMock;

    /// <summary>
    /// Deterministic 256-bit HMAC signing key for bearer token generation.
    /// </summary>
    private readonly byte[] _tokenHmacKey;

    /// <summary>
    /// Node secret store providing test cryptographic master keys.
    /// </summary>
    private readonly Mock<INodeSecretStore> _secretStoreMock;

    /// <summary>
    /// Post-quantum cryptographic provider.
    /// </summary>
    private readonly PostQuantumCryptoProvider _pqcProvider;

    /// <summary>
    /// Configuration repository backed by isolated SQLite database.
    /// </summary>
    private readonly SystemConfigurationStore _configRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceStateMachineEdgeCaseTests"/> class.
    /// </summary>
    public ServiceStateMachineEdgeCaseTests()
    {
        // WHAT: Initializes temporary filesystem workspace, real SQLite schema, and mocked secret store.
        // WHY:  Ensures all tests execute against real SQLite storage without mocking ADO.NET (Directive 1).
        // HOW:  Creates temporary directory, sets configuration overrides, initializes WeaverTestDb, and sets up dependencies.
        _tempDir = Path.Combine(Path.GetTempPath(), "weaver_sm_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        TestFixturesHelper.EnsureTestFixtures(_tempDir);

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["TRAJECTORY_EXPORT_DIR"] = Path.Combine(_tempDir, "exports"),
                ["TRAJECTORY_PARQUET_DIR"] = Path.Combine(_tempDir, "parquet"),
                ["POSTGRES_CONNECTION_STRING"] = string.Empty
            })
            .Build();

        _connectionFactory = WeaverTestDb.CreateFactory(config, new DatabaseMigrationRunner());
        _userRepository = new UserRepository(_connectionFactory);
        _auditServiceMock = new Mock<IComplianceAuditService>();

        _tokenHmacKey = new byte[32];
        RandomNumberGenerator.Fill(_tokenHmacKey);

        _secretStoreMock = new Mock<INodeSecretStore>();
        _secretStoreMock.Setup(s => s.TokenSigningKey).Returns(_tokenHmacKey);
        _secretStoreMock.Setup(s => s.PhiMasterKey).Returns(new byte[32]);
        _secretStoreMock.Setup(s => s.NodeKeysDirectory).Returns(_tempDir);

        _pqcProvider = new PostQuantumCryptoProvider();
        _configRepository = new SystemConfigurationStore(_connectionFactory);
    }

    /// <summary>
    /// Encodes arbitrary raw bytes into standard Base64Url format without padding.
    /// </summary>
    /// <param name="data">The raw bytes to encode.</param>
    /// <returns>A Base64Url encoded string.</returns>
    private static string Base64UrlEncode(byte[] data)
    {
        // WHAT: Encodes binary bytes to URL-safe Base64 without '=' padding.
        // WHY:  Standard token framing format required by UserAuthenticationService.
        // HOW:  Replaces '+' with '-', '/' with '_', and trims padding.
        return Convert.ToBase64String(data)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    /// <summary>
    /// Constructs an HMAC-SHA256 signed tamper-evident bearer token with arbitrary payload text.
    /// </summary>
    /// <param name="payloadJson">The raw JSON string payload.</param>
    /// <returns>A validly signed bearer token string.</returns>
    private string CreateSignedToken(string payloadJson)
    {
        // WHAT: Signs arbitrary JSON bytes using the test suite's 256-bit token HMAC key.
        // WHY:  Allows constructing malformed, null, or expired claims payloads to test parser validation.
        // HOW:  Encodes payload, computes HMACSHA256 digest, and formats as payload.signature.
        byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
        byte[] sig = HMACSHA256.HashData(_tokenHmacKey, payloadBytes);
        return $"{Base64UrlEncode(payloadBytes)}.{Base64UrlEncode(sig)}";
    }

    /// <summary>
    /// Verifies that <see cref="UserAuthenticationService.ValidateTokenAsync"/> rejects tokens with malformed JSON,
    /// null claims, expired timestamps, and nonexistent or inactive accounts.
    /// </summary>
    [Fact]
    public async Task UserAuthenticationService_ValidateTokenAsync_HandlesAllInvalidClaimPaths()
    {
        // WHAT: Tests error handling paths in token claim deserialization, expiry checks, and user existence verification.
        // WHY:  Ensures all validation branches in ValidateTokenAsync return structured rejection details.
        // HOW:  Injects handcrafted HMAC-signed tokens exercising lines 376, 381, 387, and 393.
        using UserAuthenticationService auth = new(_userRepository, _pqcProvider, _auditServiceMock.Object, _secretStoreMock.Object);

        // 1. Malformed JSON payload -> Deserialization failure (line 376)
        string malformedToken = CreateSignedToken("{ invalid_json_here : 123");
        var (malOk, malUser, malErr) = await auth.ValidateTokenAsync(malformedToken);
        Assert.False(malOk);
        Assert.Null(malUser);
        Assert.Equal("Failed to deserialize token claims.", malErr);

        // 2. JSON literal "null" payload -> Null claims (line 381)
        string nullClaimsToken = CreateSignedToken("null");
        var (nullOk, nullUser, nullErr) = await auth.ValidateTokenAsync(nullClaimsToken);
        Assert.False(nullOk);
        Assert.Null(nullUser);
        Assert.Equal("Token payload contains null claims.", nullErr);

        // 3. Expired claims -> Expiration rejection (line 387)
        AuthTokenClaims expiredClaims = new()
        {
            Username = "bob",
            Role = UserRole.Researcher,
            AccessGate = AccessGate.ApiWeb,
            IssuedAtUnixSeconds = 100,
            ExpiresAtUnixSeconds = 200
        };
        string expiredToken = CreateSignedToken(JsonSerializer.Serialize(expiredClaims));
        var (expOk, expUser, expErr) = await auth.ValidateTokenAsync(expiredToken);
        Assert.False(expOk);
        Assert.Null(expUser);
        Assert.Equal("Token has expired.", expErr);

        // 4. Valid token payload targeting a nonexistent or deleted user (line 393)
        AuthTokenClaims nonExistentClaims = new()
        {
            Username = "ghost_user",
            Role = UserRole.Researcher,
            AccessGate = AccessGate.ApiWeb,
            IssuedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ExpiresAtUnixSeconds = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds()
        };
        string nonExistentToken = CreateSignedToken(JsonSerializer.Serialize(nonExistentClaims));
        var (nonOk, nonUser, nonErr) = await auth.ValidateTokenAsync(nonExistentToken);
        Assert.False(nonOk);
        Assert.Null(nonUser);
        Assert.Equal("User account no longer exists or is inactive.", nonErr);
    }

    /// <summary>
    /// Verifies that <see cref="UserAuthenticationService.VerifyChallengeAndIssueTokenAsync"/> rejects
    /// attempts prior to setup completion, with expired challenges, and for deactivated accounts.
    /// </summary>
    [Fact]
    public async Task UserAuthenticationService_VerifyChallenge_HandlesSetupExpiredAndDeactivatedBranches()
    {
        // WHAT: Tests challenge verification guards for uninitialized platform, expired nonces, and deactivated users.
        // WHY:  Barres authentication attempts when invariants are violated.
        // HOW:  Calls VerifyChallengeAndIssueTokenAsync across setup required (line 271), expired nonce (line 281),
        //       and deactivated user (line 293).
        using UserAuthenticationService auth = new(_userRepository, _pqcProvider, _auditServiceMock.Object, _secretStoreMock.Object);

        // 1. Setup required branch (line 271)
        var (setupOk, setupErr, _, _) = await auth.VerifyChallengeAndIssueTokenAsync("admin", "chal-1", "sig-1");
        Assert.False(setupOk);
        Assert.Equal("First-launch setup required. Please complete initial setup at /setup.", setupErr);

        // Complete setup
        GeneratedKeyBundle adminBundle = TestChallengeHelper.CreateTestKeyBundle("admin", UserRole.Administrator, AccessGate.ApiWeb, KeyAlgorithm.Ed25519, null);
        await auth.CompleteInitialSetupAsync("admin", adminBundle.PublicKeyBase64, KeyAlgorithm.Ed25519, AccessGate.ApiWeb);

        // 2. Expired challenge branch (line 281)
        AuthChallenge expiredChallenge = new()
        {
            ChallengeId = "expired-chal-id",
            Username = "admin",
            NonceBase64 = Convert.ToBase64String(new byte[32]),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        auth.RegisterChallengeForTesting(expiredChallenge);

        var (expOk, expErr, _, _) = await auth.VerifyChallengeAndIssueTokenAsync("admin", "expired-chal-id", "dummy-sig");
        Assert.False(expOk);
        Assert.Equal("Authentication challenge has expired.", expErr);

        // 3. Deactivated user branch (line 293)
        GeneratedKeyBundle userBundle = TestChallengeHelper.CreateTestKeyBundle("inactive-user", UserRole.Researcher, AccessGate.ApiWeb, KeyAlgorithm.Ed25519, null);
        UserAccount deactivatedAccount = await auth.CreateUserAsync("inactive-user", UserRole.Researcher, AccessGate.ApiWeb, userBundle.PublicKeyBase64, KeyAlgorithm.Ed25519);
        deactivatedAccount = deactivatedAccount with { IsActive = false };
        await _userRepository.CreateUserAsync(deactivatedAccount);

        AuthChallenge activeChallenge = new()
        {
            ChallengeId = "active-chal-deactivated",
            Username = "inactive-user",
            NonceBase64 = Convert.ToBase64String(new byte[32]),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5)
        };
        auth.RegisterChallengeForTesting(activeChallenge);

        var (deactOk, deactErr, _, _) = await auth.VerifyChallengeAndIssueTokenAsync("inactive-user", "active-chal-deactivated", "dummy-sig");
        Assert.False(deactOk);
        Assert.Equal("User account not found or deactivated.", deactErr);
    }

    /// <summary>
    /// Verifies that <see cref="StackServiceManager.GetServiceByIdAsync"/> routes to simulation, storage,
    /// peer, and default null fallback branches, and validates <see cref="StackServiceManager.ExtractAcceleratorAsync"/>.
    /// </summary>
    [Fact]
    public async Task StackServiceManager_GetServiceById_AndAcceleratorParsing_EdgeCases()
    {
        // WHAT: Tests service lookup switch branches and hardware accelerator fallback on empty string.
        // WHY:  Ensures full coverage of telemetry service enumeration and JSON attribute extraction.
        // HOW:  Calls GetServiceByIdAsync with various identifiers and invokes ExtractAcceleratorAsync with empty JSON attribute.
        IConfiguration hostConfig = new ConfigurationBuilder().Build();
        StackServiceManager manager = new(_configRepository, hostConfig);

        // 1. Simulation node lookup (line 75)
        StackServiceInfo? sim = await manager.GetServiceByIdAsync("simulation");
        Assert.NotNull(sim);
        Assert.Equal("simulation-engine", sim.ServiceId);

        // 2. Storage node lookup (line 76)
        StackServiceInfo? storage = await manager.GetServiceByIdAsync("storage");
        Assert.NotNull(storage);
        Assert.Equal("pg-duckdb", storage.ServiceId);

        // 3. Peer mesh lookup (line 78)
        StackServiceInfo? peer = await manager.GetServiceByIdAsync("peer");
        Assert.NotNull(peer);
        Assert.Equal("peer-mesh", peer.ServiceId);

        // 4. Unknown service lookup fallback (line 79)
        StackServiceInfo? unknown = await manager.GetServiceByIdAsync("unknown-service-id");
        Assert.Null(unknown);

        // 5. ExtractAcceleratorAsync with whitespace/empty accelerator attribute (line 247)
        using HttpResponseMessage emptyAccResp = new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{\"accelerator\": \"   \"}", Encoding.UTF8, "application/json")
        };
        string fallbackResult = await StackServiceManager.ExtractAcceleratorAsync(emptyAccResp, "Intel Arc A770 (OpenCL)", CancellationToken.None);
        Assert.Equal("Intel Arc A770 (OpenCL)", fallbackResult);
    }

    /// <summary>
    /// Verifies that <see cref="ChannelEventBus.PublishAsync{TEvent}"/> handles cancellation rethrow,
    /// closed subscriber channel unregistration, and full stream cancellation cleanly.
    /// </summary>
    [Fact]
    public async Task ChannelEventBus_PublishAsync_HandlesCancellationAndClosedChannelBranches()
    {
        // WHAT: Tests subscriber cancellation rethrow and channel stream error/cancellation handling in PublishAsync.
        // WHY:  Ensures robust dispatching when streams terminate or callers cancel delivery.
        // HOW:  Triggers OperationCanceledException in handler (lines 80-83), closed stream ChannelClosedException (lines 114-118),
        //       and full channel cancellation (lines 119-122).
        ChannelEventBus bus = new();

        // 1. Subscriber handler throws OperationCanceledException when caller requests cancellation (lines 80-83)
        ChannelEventBus bus1 = new();
        using CancellationTokenSource cts = new();

        bus1.Subscribe<string>((evt, ct) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(ct);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await bus1.PublishAsync("test-cancel", cts.Token);
        });

        // 2. Closed channel stream throws ChannelClosedException -> unregisters cleanly (lines 114-118)
        ChannelEventBus bus2 = new();
        Channel<string> closedChannel = Channel.CreateBounded<string>(1);
        closedChannel.Writer.Complete();
        bus2.RegisterCompletedStreamForTesting(closedChannel);

        // Publishing to closed stream should unregister without throwing
        await bus2.PublishAsync("test-closed-stream");

        // 3. Full channel stream write with cancelled token -> OperationCanceledException (lines 119-122)
        ChannelEventBus bus3 = new();
        BoundedChannelOptions fullOptions = new(1)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        Channel<string> fullChannel = Channel.CreateBounded<string>(fullOptions);
        Assert.True(fullChannel.Writer.TryWrite("initial-item"));

        bus3.RegisterCompletedStreamForTesting(fullChannel);

        using CancellationTokenSource fullCts = new();
        ValueTask publishTask = bus3.PublishAsync("overflow-item", fullCts.Token);
        fullCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await publishTask;
        });

        // 4. Full channel stream write completes successfully when consumer drains buffer (line 113)
        ChannelEventBus bus4 = new();
        BoundedChannelOptions waitOptions = new(1)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        Channel<string> waitChannel = Channel.CreateBounded<string>(waitOptions);
        Assert.True(waitChannel.Writer.TryWrite("initial-wait"));

        bus4.RegisterCompletedStreamForTesting(waitChannel);

        using CancellationTokenSource waitCts = new(TimeSpan.FromSeconds(5));
        _ = Task.Run(async () =>
        {
            await Task.Delay(25);
            await waitChannel.Reader.ReadAsync(waitCts.Token);
        });

        await bus4.PublishAsync("second-wait-item", waitCts.Token);
    }

    /// <summary>
    /// Verifies in-memory fallback conversion and expired entry cleanup in <see cref="ValkeyCacheService"/>.
    /// </summary>
    [Fact]
    public async Task ValkeyCacheService_GetAsync_HandlesJsonFallbackAndExpiryCleanup()
    {
        // WHAT: Tests in-memory type conversion and expired entry eviction in ValkeyCacheService.
        // WHY:  Validates cache fallback logic when Valkey daemon is disconnected.
        // HOW:  Stores non-primitive object and retrieves as JsonDocument (lines 124-125), and stores expired entry (line 130).
        Mock<IValkeyConnectionFactory> mockFactory = new();
        mockFactory.Setup(f => f.GetDatabase(It.IsAny<int>())).Returns((IDatabase?)null);

        ValkeyCacheService cache = new(mockFactory.Object, NullLogger<ValkeyCacheService>.Instance);

        // 1. Type mismatch conversion through JSON (lines 124-125)
        await cache.SetAsync("user_obj", new { Role = "Admin", Gate = "ApiWeb" }, TimeSpan.FromMinutes(5));
        JsonDocument? doc = await cache.GetAsync<JsonDocument>("user_obj");
        Assert.NotNull(doc);
        Assert.Equal("Admin", doc.RootElement.GetProperty("Role").GetString());

        // 2. Expired entry cleanup (lines 128-131)
        await cache.SetAsync("stale_item", "old_val", TimeSpan.FromSeconds(-5));
        string? expiredVal = await cache.GetAsync<string>("stale_item");
        Assert.Null(expiredVal);
    }

    /// <summary>
    /// Verifies distributed lock acquisition delay, double-disposal, and Lua script error handling in <see cref="ValkeyLockService"/>.
    /// </summary>
    [Fact]
    public async Task ValkeyLockService_HandlesAcquireDelay_DoubleDispose_AndScriptException()
    {
        // WHAT: Tests distributed lock polling delay and DisposeAsync exception resilience.
        // WHY:  Ensures distributed lock acquisition retries within timeout and disposal never crashes.
        // HOW:  Simulates initial lock collision resolved on retry (line 95), double dispose (line 170),
        //       and script evaluate fault (lines 180-183).
        Mock<IValkeyConnectionFactory> mockFactory = new();
        Mock<IDatabase> mockDb = new();
        mockFactory.Setup(f => f.GetDatabase(It.IsAny<int>())).Returns(mockDb.Object);

        // Sequence: first attempt fails (collision), second attempt succeeds
        int callCount = 0;
        mockDb.Setup(d => d.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            When.NotExists))
            .Returns(() =>
            {
                callCount++;
                return Task.FromResult(callCount > 1);
            });

        mockDb.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(1));

        ValkeyLockService lockService = new(mockFactory.Object, NullLogger<ValkeyLockService>.Instance);

        // 1. Lock acquired after retry delay (line 95)
        IAsyncDisposable? lockToken = await lockService.AcquireLockAsync("test_resource", TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(150));
        Assert.NotNull(lockToken);
        Assert.True(callCount >= 2);

        // 2. Double-disposal of distributed lock token (line 170)
        await lockToken.DisposeAsync();
        await lockToken.DisposeAsync(); // Second disposal is a no-op

        // 3. Script evaluate fault on disposal (lines 180-183)
        mockDb.Setup(d => d.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            When.NotExists))
            .ReturnsAsync(true);

        mockDb.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisException("Simulated Lua script failure"));

        IAsyncDisposable? errorToken = await lockService.AcquireLockAsync("fault_resource", TimeSpan.FromSeconds(5), TimeSpan.Zero);
        Assert.NotNull(errorToken);

        // Dispose should catch exception cleanly and log warning without rethrowing
        await errorToken.DisposeAsync();
    }

    /// <summary>
    /// Verifies inbound cluster listener registration, outbound failure resilience, and message parsing
    /// edge cases in <see cref="ValkeyEventBridge"/>.
    /// </summary>
    [Fact]
    public async Task ValkeyEventBridge_HandlesInboundListeners_OutboundFailures_AndMessageEdges()
    {
        // WHAT: Tests ValkeyEventBridge pub/sub registration, outbound serialization errors, and inbound parsing edge cases.
        // WHY:  Validates cluster messaging resilience against socket disconnects and malformed cluster payloads.
        // HOW:  Tests registration failure catch (lines 239-242), outbound publish failure catch (lines 196-199),
        //       and ProcessInboundMessageAsync edge cases (lines 258, 270, 276, 282-285).
        Mock<IValkeyConnectionFactory> mockFactory = new();
        Mock<ISubscriber> mockSubscriber = new();

        mockFactory.Setup(f => f.IsConnected).Returns(true);
        mockFactory.Setup(f => f.GetSubscriber()).Returns(mockSubscriber.Object);

        ChannelEventBus bus = new();

        // 1. Subscription registration fault handling (lines 239-242)
        mockSubscriber.Setup(s => s.SubscribeAsync(
            It.IsAny<RedisChannel>(),
            It.IsAny<Action<RedisChannel, RedisValue>>(),
            It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisException("Subscription fault"));

        await using (ValkeyEventBridge faultBridge = new(bus, mockFactory.Object, NullLogger<ValkeyEventBridge>.Instance))
        {
            await faultBridge.RegisterInboundClusterListenersAsync();
            // Verify execution caught exception without throwing
        }

        // 2. Successful subscription registration and listener invocation (lines 220-237)
        Action<RedisChannel, RedisValue>? simCallback = null;
        Action<RedisChannel, RedisValue>? sessionCallback = null;
        Action<RedisChannel, RedisValue>? auditCallback = null;
        Action<RedisChannel, RedisValue>? telemetryCallback = null;

        mockSubscriber.Setup(s => s.SubscribeAsync(
            RedisChannel.Literal("weaver:events:SimulationProgress"),
            It.IsAny<Action<RedisChannel, RedisValue>>(),
            It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>((ch, cb, _) => simCallback = cb)
            .Returns(Task.CompletedTask);

        mockSubscriber.Setup(s => s.SubscribeAsync(
            RedisChannel.Literal("weaver:events:UserSessionActivity"),
            It.IsAny<Action<RedisChannel, RedisValue>>(),
            It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>((ch, cb, _) => sessionCallback = cb)
            .Returns(Task.CompletedTask);

        mockSubscriber.Setup(s => s.SubscribeAsync(
            RedisChannel.Literal("weaver:events:AuditLogDispatched"),
            It.IsAny<Action<RedisChannel, RedisValue>>(),
            It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>((ch, cb, _) => auditCallback = cb)
            .Returns(Task.CompletedTask);

        mockSubscriber.Setup(s => s.SubscribeAsync(
            RedisChannel.Literal("weaver:events:SystemTelemetryUpdated"),
            It.IsAny<Action<RedisChannel, RedisValue>>(),
            It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>((ch, cb, _) => telemetryCallback = cb)
            .Returns(Task.CompletedTask);

        await using (ValkeyEventBridge activeBridge = new(bus, mockFactory.Object, NullLogger<ValkeyEventBridge>.Instance))
        {
            activeBridge.RegisterLocalOutboundForwarders();
            await activeBridge.RegisterInboundClusterListenersAsync();

            Assert.NotNull(simCallback);
            Assert.NotNull(sessionCallback);
            Assert.NotNull(auditCallback);
            Assert.NotNull(telemetryCallback);

            // Invoke each callback with a valid serialized payload
            SimulationProgressEvent simEvt = new("sim-1", "oxDNA Nanocarrier", 50, 100, "Running", "Converging");
            simCallback(RedisChannel.Literal("weaver:events:SimulationProgress"), JsonSerializer.Serialize(simEvt));

            UserSessionActivityEvent sessionEvt = new("alice", "Researcher", "Heartbeat", "Web");
            sessionCallback(RedisChannel.Literal("weaver:events:UserSessionActivity"), JsonSerializer.Serialize(sessionEvt));

            AuditLogDispatchedEvent auditEvt = new(1L, "user:alice", "Login", "session", "sha384-digest");
            auditCallback(RedisChannel.Literal("weaver:events:AuditLogDispatched"), JsonSerializer.Serialize(auditEvt));

            SystemTelemetryUpdatedEvent telemetryEvt = new("node-primary", 5000, 1048576, true, "AVX-512");
            telemetryCallback(RedisChannel.Literal("weaver:events:SystemTelemetryUpdated"), JsonSerializer.Serialize(telemetryEvt));

            // 3. Outbound publication success (lines 190-195) and failure catch (lines 196-199)
            mockSubscriber.Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
                .ReturnsAsync(1L);

            await bus.PublishAsync(new SimulationProgressEvent("sim-norm", "Normal Campaign", 5, 100, "Running", null));

            mockSubscriber.Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisException("Publish socket closed"));

            // Dispatching local event should trigger ForwardOutboundAsync and catch exception cleanly
            await bus.PublishAsync(new SimulationProgressEvent("sim-2", "Test Campaign", 10, 100, "Running", null));

            // 4. Inbound ProcessInboundMessageAsync edge cases
            // 4a. Empty or null message -> return (line 258)
            await activeBridge.ProcessInboundMessageAsync<SimulationProgressEvent>(RedisValue.EmptyString);
            await activeBridge.ProcessInboundMessageAsync<SimulationProgressEvent>(RedisValue.Null);

            // 4b. JSON literal "null" -> parsed == null return (line 270)
            await activeBridge.ProcessInboundMessageAsync<SimulationProgressEvent>("null");

            // 4c. Echo suppression -> locally produced event skipped (line 276)
            SimulationProgressEvent localEvt = new("sim-local", "Local Campaign", 20, 100, "Running", null);
            activeBridge.RecordLocalEventForTesting(localEvt.EventId);
            await activeBridge.ProcessInboundMessageAsync<SimulationProgressEvent>(JsonSerializer.Serialize(localEvt));

            // 4d. Corrupted JSON payload -> catch exception and log warning (lines 282-285)
            await activeBridge.ProcessInboundMessageAsync<SimulationProgressEvent>("{ corrupt_json :");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // WHAT: Cleans up temporary test directory and database files.
        // WHY:  Leaves no orphaned artifacts on the filesystem.
        // HOW:  Recursively deletes _tempDir.
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Ignore directory deletion failures
        }
    }
}
