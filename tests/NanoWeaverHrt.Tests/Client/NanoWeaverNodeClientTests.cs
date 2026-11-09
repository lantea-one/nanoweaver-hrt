using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Moq;
using NanoWeaverHrt;
using NanoWeaverHrt.Analytics;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Security;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Fabrication.Gel;
using NanoWeaverHrt.Fabrication.Opentrons;
using NanoWeaverHrt.Federation;
using NanoWeaverHrt.Genomics;
using NanoWeaverHrt.Hardware.Benchmarking;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Access;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Services.Stack;
using NanoWeaverHrt.Simulations.Models;
using Xunit;

namespace NanoWeaverHrt.Tests.Client;

/// <summary>
/// Comprehensive unit tests verifying <see cref="NanoWeaverNodeClient"/>, <see cref="NodeApiException"/>,
/// and associated API client DTOs, covering challenge-response authentication, telemetry polling,
/// simulation triggering, error parsing, and base URI security gating.
/// </summary>
public sealed class NanoWeaverNodeClientTests : IDisposable
{
    /// <summary>
    /// Stub key ring vault for challenge signing.
    /// </summary>
    private readonly StubKeyRingVault _vault = new();

    /// <summary>
    /// In-memory test HTTP message handler.
    /// </summary>
    private readonly StubHttpMessageHandler _httpHandler = new();

    /// <summary>
    /// HTTP client instance configured with the in-memory test handler.
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Instance of <see cref="NanoWeaverNodeClient"/> under test.
    /// </summary>
    private readonly NanoWeaverNodeClient _client;

    /// <summary>
    /// Standard valid localhost instance profile for testing.
    /// </summary>
    private readonly InstanceProfile _localProfile = new(
        InstanceId: "local-node",
        DisplayName: "Local Node",
        BaseUri: "http://127.0.0.1:5000",
        IsLocalInstance: true,
        Username: "researcher-01",
        Role: "Researcher",
        KeyAlgorithm: "Ed25519",
        PublicKeyPem: "-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEA1234567890abcdef1234567890abcdef1234567890=\n-----END PUBLIC KEY-----",
        EncryptedPrivateKeyEnvelopeBase64: "envelope_base64_data",
        NotificationsEnabled: true,
        PollingIntervalSeconds: 10,
        AddedAtUtc: DateTimeOffset.UtcNow,
        LastConnectedAtUtc: null);

    /// <summary>
    /// Standard valid remote HTTPS instance profile for testing.
    /// </summary>
    private readonly InstanceProfile _remoteProfile = new(
        InstanceId: "remote-node",
        DisplayName: "Remote Node",
        BaseUri: "https://cluster.lantea.one:5000",
        IsLocalInstance: false,
        Username: "admin-01",
        Role: "Administrator",
        KeyAlgorithm: "ML-DSA-65",
        PublicKeyPem: "-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEA1234567890abcdef1234567890abcdef1234567890=\n-----END PUBLIC KEY-----",
        EncryptedPrivateKeyEnvelopeBase64: "envelope_base64_data",
        NotificationsEnabled: true,
        PollingIntervalSeconds: 15,
        AddedAtUtc: DateTimeOffset.UtcNow,
        LastConnectedAtUtc: null);

    /// <summary>
    /// Initializes a new instance of the <see cref="NanoWeaverNodeClientTests"/> class.
    /// </summary>
    public NanoWeaverNodeClientTests()
    {
        // WHAT: Sets up HttpClient with StubHttpMessageHandler and instantiates client.
        // WHY:  Permits deterministic in-memory HTTP mocking without real socket listeners.
        // HOW:  Passes in-memory handler to HttpClient constructor and initializes NanoWeaverNodeClient.
        _httpClient = new HttpClient(_httpHandler);
        _client = new NanoWeaverNodeClient(_httpClient, _vault);
    }

    /// <summary>
    /// Disposes test resources.
    /// </summary>
    public void Dispose()
    {
        // WHAT: Disposes the HttpClient and test handler.
        // WHY:  Frees in-memory HTTP resources after test execution.
        // HOW:  Invokes Dispose on _httpClient and _httpHandler.
        _httpClient.Dispose();
        _httpHandler.Dispose();
    }

    /// <summary>
    /// Verifies constructor argument validation for null parameters.
    /// </summary>
    [Fact]
    public void Constructor_NullArguments_ThrowsArgumentNullException()
    {
        // WHAT: Tests null guard clauses on NanoWeaverNodeClient constructor.
        // WHY:  Fail-fast principle requires non-null dependencies.
        // HOW:  Asserts ArgumentNullException is thrown when either parameter is null.
        Assert.Throws<ArgumentNullException>(() => new NanoWeaverNodeClient(null!, _vault));
        Assert.Throws<ArgumentNullException>(() => new NanoWeaverNodeClient(_httpClient, null!));
    }

    /// <summary>
    /// Verifies BaseUri validation enforcing HTTPS for remote endpoints and HTTP for loopback.
    /// </summary>
    /// <param name="baseUri">The URI string under test.</param>
    /// <param name="shouldSucceed">True if URI should be accepted; false if rejected.</param>
    [Theory]
    [InlineData("http://127.0.0.1:5000", true)]
    [InlineData("http://localhost:5000", true)]
    [InlineData("https://node.lantea.one", true)]
    [InlineData("http://node.lantea.one:5000", false)]
    [InlineData("http://192.168.1.50:5000", false)]
    [InlineData("ftp://node.lantea.one", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(":::invalid", false)]
    public async Task BaseUri_SecurityValidation_EnforcesHttpsOnRemote(string baseUri, bool shouldSucceed)
    {
        // WHAT: Tests Prime Directive 9: remote non-loopback endpoints must use HTTPS.
        // WHY:  Prevent plaintext credential transmission across untrusted networks.
        // HOW:  Calls PollSnapshotAsync with different URIs and verifies ArgumentException behavior.
        InstanceProfile profile = _localProfile with { BaseUri = baseUri };

        if (shouldSucceed)
        {
            _httpHandler.SetResponse("/api/system/telemetry", HttpStatusCode.Unauthorized, "{}");
            InstanceConnectionSnapshot snapshot = await _client.PollSnapshotAsync(profile, "dummy_token");
            Assert.Equal(InstanceConnectionState.Unauthorized, snapshot.State);
        }
        else
        {
            await Assert.ThrowsAsync<ArgumentException>(() => _client.PollSnapshotAsync(profile, "dummy_token"));
        }
    }

    /// <summary>
    /// Verifies argument validation in AuthenticateAsync.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_InvalidArguments_ThrowsExpectedExceptions()
    {
        // WHAT: Tests argument validation in AuthenticateAsync.
        // WHY:  Ensures invalid profiles or missing parameters are rejected immediately.
        // HOW:  Asserts ArgumentNullException or ArgumentException for invalid profile states.
        ReadOnlyMemory<char> passphrase = "password123".AsMemory();

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.AuthenticateAsync(null!, passphrase));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.AuthenticateAsync(_localProfile with { Username = "" }, passphrase));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.AuthenticateAsync(_localProfile with { EncryptedPrivateKeyEnvelopeBase64 = "" }, passphrase));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.AuthenticateAsync(_localProfile with { KeyAlgorithm = "" }, passphrase));
    }

    /// <summary>
    /// Verifies complete successful challenge-response authentication handshake.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_ValidCredentials_ReturnsAuthenticatedSession()
    {
        // WHAT: Tests end-to-end asymmetric challenge-response authentication.
        // WHY:  Validates that /api/auth/challenge and /api/auth/token are executed correctly.
        // HOW:  Stubs challenge issuance, configures vault mock to sign nonce, stubs token issuance, asserts session properties.
        ReadOnlyMemory<char> passphrase = "correct_passphrase".AsMemory();
        byte[] expectedNonce = new byte[32];
        expectedNonce[0] = 0x42;
        string nonceBase64 = Convert.ToBase64String(expectedNonce);

        _httpHandler.SetResponse(
            "/api/auth/challenge",
            HttpStatusCode.OK,
            $$"""{"challengeId": "chal_123", "nonceBase64": "{{nonceBase64}}"}""");

        _vault.SignatureToReturn = "signature_base64_result";

        _httpHandler.SetResponse(
            "/api/auth/token",
            HttpStatusCode.OK,
            """
            {
                "token": "jwt_token_sample",
                "tokenType": "Bearer",
                "expiresInSeconds": 3600,
                "user": {
                    "username": "researcher-01",
                    "role": "Researcher"
                }
            }
            """);

        NodeAuthSession session = await _client.AuthenticateAsync(_localProfile, passphrase);

        Assert.Equal("Ed25519", _vault.LastAlgorithm);
        Assert.Equal(_localProfile.EncryptedPrivateKeyEnvelopeBase64, _vault.LastEnvelope);
        Assert.Equal("correct_passphrase", _vault.LastPassphrase);
        Assert.Equal(expectedNonce, _vault.LastNonce);

        Assert.Equal("jwt_token_sample", session.Token);
        Assert.Equal("Bearer", session.TokenType);
        Assert.Equal(3600, session.ExpiresInSeconds);
        Assert.Equal("researcher-01", session.Username);
        Assert.Equal("Researcher", session.Role);
        Assert.True(session.IssuedAtUtc <= DateTimeOffset.UtcNow);
        Assert.True(session.ExpiresAtUtc > session.IssuedAtUtc);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Assert.False(session.IsExpired(now));
        Assert.True(session.TimeRemaining(now) > TimeSpan.Zero);
        Assert.True(session.IsExpired(now.AddHours(2)));
        Assert.Equal(TimeSpan.Zero, session.TimeRemaining(now.AddHours(2)));
    }

    /// <summary>
    /// Verifies authentication with numeric role element and fallback to profile metadata when user is null.
    /// </summary>
    [Theory]
    [InlineData(0, "Administrator")]
    [InlineData(1, "Researcher")]
    [InlineData(2, "Patient")]
    [InlineData(99, "99")]
    public async Task AuthenticateAsync_NumericRoleInToken_ResolvesCorrectRoleString(int roleCode, string expectedRole)
    {
        // WHAT: Tests role parsing when the API returns an integer enum value.
        // WHY:  Supports nodes serializing enum values without string converters.
        // HOW:  Returns JSON with integer role and verifies resolved Role string in session.
        ReadOnlyMemory<char> passphrase = "password".AsMemory();
        byte[] nonce = new byte[32];

        _httpHandler.SetResponse(
            "/api/auth/challenge",
            HttpStatusCode.OK,
            $$"""{"challengeId": "chal_int", "nonceBase64": "{{Convert.ToBase64String(nonce)}}"}""");

        _vault.SignatureToReturn = "sig";

        _httpHandler.SetResponse(
            "/api/auth/token",
            HttpStatusCode.OK,
            $$"""
            {
                "token": "token_val",
                "tokenType": "Bearer",
                "expiresInSeconds": 0,
                "user": {
                    "username": "custom_user",
                    "role": {{roleCode}}
                }
            }
            """);

        NodeAuthSession session = await _client.AuthenticateAsync(_localProfile, passphrase);
        Assert.Equal(expectedRole, session.Role);
        Assert.Equal("custom_user", session.Username);
        Assert.Equal(28800, session.ExpiresInSeconds); // default fallback
    }

    /// <summary>
    /// Verifies authentication fallback when user object is null in token response.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_NullUserInTokenResponse_FallsBackToProfileDefaults()
    {
        // WHAT: Tests fallback to profile username and role when token response has null user.
        // WHY:  Guarantees robust session creation even with minimal token responses.
        // HOW:  Returns JSON without user property and asserts session matches profile values.
        ReadOnlyMemory<char> passphrase = "password".AsMemory();
        byte[] nonce = new byte[32];

        _httpHandler.SetResponse(
            "/api/auth/challenge",
            HttpStatusCode.OK,
            $$"""{"challengeId": "chal_null_user", "nonceBase64": "{{Convert.ToBase64String(nonce)}}"}""");

        _vault.SignatureToReturn = "sig";

        _httpHandler.SetResponse(
            "/api/auth/token",
            HttpStatusCode.OK,
            """
            {
                "token": "token_val",
                "tokenType": null,
                "expiresInSeconds": 1800
            }
            """);

        NodeAuthSession session = await _client.AuthenticateAsync(_localProfile, passphrase);
        Assert.Equal(_localProfile.Username, session.Username);
        Assert.Equal(_localProfile.Role, session.Role);
        Assert.Equal("Bearer", session.TokenType);
    }

    /// <summary>
    /// Verifies authentication fallback when role JSON element is boolean or large number that cannot be parsed as Int32.
    /// </summary>
    /// <param name="rawRoleJson">Raw JSON representation of role property.</param>
    [Theory]
    [InlineData("true")]
    [InlineData("1e35")]
    public async Task AuthenticateAsync_NonStringOrInvalidNumericRole_ResolvesToNullAndFallsBack(string rawRoleJson)
    {
        // WHAT: Tests fallback when role element is a boolean or non-Int32 number.
        // WHY:  Ensures switch default branch (_ => null) in Role property is fully exercised.
        // HOW:  Returns JSON with boolean or floating point role and verifies session.Role defaults to profile.Role.
        ReadOnlyMemory<char> passphrase = "password".AsMemory();
        byte[] nonce = new byte[32];

        _httpHandler.SetResponse(
            "/api/auth/challenge",
            HttpStatusCode.OK,
            $$"""{"challengeId": "chal_bool", "nonceBase64": "{{Convert.ToBase64String(nonce)}}"}""");

        _vault.SignatureToReturn = "sig";

        _httpHandler.SetResponse(
            "/api/auth/token",
            HttpStatusCode.OK,
            $$"""
            {
                "token": "token_val",
                "tokenType": "Bearer",
                "expiresInSeconds": 1800,
                "user": {
                    "username": "user1",
                    "role": {{rawRoleJson}}
                }
            }
            """);

        NodeAuthSession session = await _client.AuthenticateAsync(_localProfile, passphrase);
        Assert.Equal(_localProfile.Role, session.Role);
    }

    /// <summary>
    /// Verifies authentication failure when challenge endpoint returns non-2xx status code.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_ChallengeFailure_ThrowsNodeApiException()
    {
        // WHAT: Tests error handling when /api/auth/challenge fails.
        // WHY:  NodeApiException must be thrown with sanitized RFC 7807 problem details.
        // HOW:  Configures 429 response and asserts NodeApiException is thrown with matching status.
        _httpHandler.SetResponse(
            "/api/auth/challenge",
            HttpStatusCode.TooManyRequests,
            """{"title": "Too Many Challenges", "status": 429, "traceId": "tr_123"}""");

        NodeApiException ex = await Assert.ThrowsAsync<NodeApiException>(
            () => _client.AuthenticateAsync(_localProfile, "pass".AsMemory()));

        Assert.Equal(429, ex.StatusCode);
        Assert.Equal("Too Many Challenges", ex.Title);
        Assert.Equal("tr_123", ex.TraceId);
    }

    /// <summary>
    /// Verifies authentication failure when challenge response payload is malformed.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_MalformedChallengePayload_ThrowsNodeApiException()
    {
        // WHAT: Tests error handling when challenge JSON is empty or missing required fields.
        // WHY:  Malformed response cannot be signed; client must abort.
        // HOW:  Returns empty JSON object and asserts NodeApiException.
        _httpHandler.SetResponse("/api/auth/challenge", HttpStatusCode.OK, "{}");

        await Assert.ThrowsAsync<NodeApiException>(
            () => _client.AuthenticateAsync(_localProfile, "pass".AsMemory()));
    }

    /// <summary>
    /// Verifies authentication failure when token exchange endpoint returns 401 Unauthorized.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_TokenExchangeFailure_ThrowsNodeApiException()
    {
        // WHAT: Tests error handling when /api/auth/token rejects the signature.
        // WHY:  Ensures signature verification failures are wrapped in sanitized NodeApiException.
        // HOW:  Returns 401 with RFC 7807 payload and asserts NodeApiException properties.
        _httpHandler.SetResponse(
            "/api/auth/challenge",
            HttpStatusCode.OK,
            $$"""{"challengeId": "chal_1", "nonceBase64": "{{Convert.ToBase64String(new byte[32])}}"}""");

        _vault.SignatureToReturn = "sig";

        _httpHandler.SetResponse(
            "/api/auth/token",
            HttpStatusCode.Unauthorized,
            """{"title": "Invalid Signature", "status": 401, "detail": "Cryptographic signature mismatch."}""");

        NodeApiException ex = await Assert.ThrowsAsync<NodeApiException>(
            () => _client.AuthenticateAsync(_localProfile, "pass".AsMemory()));

        Assert.Equal(401, ex.StatusCode);
        Assert.Equal("Invalid Signature", ex.Title);
        Assert.Equal("Cryptographic signature mismatch.", ex.Detail);
    }

    /// <summary>
    /// Verifies authentication failure when token response payload is empty or missing token.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_MalformedTokenPayload_ThrowsNodeApiException()
    {
        // WHAT: Tests error handling when token response contains no token string.
        // WHY:  Prevents returning an unauthenticated session.
        // HOW:  Returns empty JSON and asserts NodeApiException.
        _httpHandler.SetResponse(
            "/api/auth/challenge",
            HttpStatusCode.OK,
            $$"""{"challengeId": "chal_1", "nonceBase64": "{{Convert.ToBase64String(new byte[32])}}"}""");

        _vault.SignatureToReturn = "sig";

        _httpHandler.SetResponse("/api/auth/token", HttpStatusCode.OK, "{}");

        await Assert.ThrowsAsync<NodeApiException>(
            () => _client.AuthenticateAsync(_localProfile, "pass".AsMemory()));
    }

    /// <summary>
    /// Verifies PollSnapshotAsync input argument validation.
    /// </summary>
    [Fact]
    public async Task PollSnapshotAsync_InvalidArguments_ThrowsExpectedExceptions()
    {
        // WHAT: Tests input validation on PollSnapshotAsync.
        // WHY:  Guarantees profile and bearerToken are non-null and non-whitespace.
        // HOW:  Asserts ArgumentNullException or ArgumentException.
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.PollSnapshotAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.PollSnapshotAsync(_localProfile, ""));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.PollSnapshotAsync(_localProfile, "   "));
    }

    /// <summary>
    /// Verifies successful telemetry polling returning Online connection state.
    /// </summary>
    [Fact]
    public async Task PollSnapshotAsync_SuccessfulTelemetry_ReturnsOnlineSnapshot()
    {
        // WHAT: Tests normal telemetry polling under nominal operating conditions.
        // WHY:  Constructs accurate snapshot with live FPS, GPU metrics, and simulation counts.
        // HOW:  Stubs /api/system/telemetry response and verifies InstanceConnectionSnapshot.
        _httpHandler.SetResponse(
            "/api/system/telemetry",
            HttpStatusCode.OK,
            """
            {
                "nodeName": "node-alpha",
                "acceleratorBackend": "Intel Arc A770",
                "framesParsed": 500000,
                "framesPerSecond": 120.5,
                "heapMemoryMb": 45.2,
                "workingSetMb": 98.4,
                "gcGen0Collections": 1,
                "gcGen1Collections": 0,
                "gcGen2Collections": 0,
                "uptime": "01:23:45",
                "cpuUtilizationPercent": 25.5,
                "gpuUtilizationPercent": 75.0,
                "gpuTemperatureCelsius": 68.2,
                "activeSimulationCount": 2
            }
            """);

        InstanceConnectionSnapshot snapshot = await _client.PollSnapshotAsync(_localProfile, "valid_token");

        Assert.Equal(_localProfile.InstanceId, snapshot.InstanceId);
        Assert.Equal(_localProfile.DisplayName, snapshot.DisplayName);
        Assert.Equal(InstanceConnectionState.Online, snapshot.State);
        Assert.Equal(25.5, snapshot.CpuUtilizationPercent);
        Assert.Equal(75.0, snapshot.GpuUtilizationPercent);
        Assert.Equal(68.2, snapshot.GpuTemperatureCelsius);
        Assert.Equal(2, snapshot.ActiveSimulationCount);
        Assert.Equal(120.5, snapshot.ParsedFramesPerSecond);
        Assert.True(snapshot.LastUpdatedUtc <= DateTimeOffset.UtcNow);
        Assert.Null(snapshot.LastErrorTitle);
    }

    /// <summary>
    /// Verifies telemetry polling when GPU core temperature exceeds 85.0°C thermal threshold.
    /// </summary>
    [Fact]
    public async Task PollSnapshotAsync_ElevatedGpuTemperature_ReturnsDegradedState()
    {
        // WHAT: Tests thermal threshold boundary (>= 85.0°C).
        // WHY:  Node must transition to Degraded state to warn user of thermal throttling risk.
        // HOW:  Stubs telemetry with 86.5°C and verifies State is Degraded with descriptive error title.
        _httpHandler.SetResponse(
            "/api/system/telemetry",
            HttpStatusCode.OK,
            """
            {
                "nodeName": "node-hot",
                "acceleratorBackend": "Intel Arc A770",
                "framesParsed": 1000,
                "framesPerSecond": 45.0,
                "heapMemoryMb": 50.0,
                "workingSetMb": 100.0,
                "gcGen0Collections": 0,
                "gcGen1Collections": 0,
                "gcGen2Collections": 0,
                "uptime": "00:10:00",
                "cpuUtilizationPercent": 10.0,
                "gpuUtilizationPercent": 99.0,
                "gpuTemperatureCelsius": 86.5,
                "activeSimulationCount": 1
            }
            """);

        InstanceConnectionSnapshot snapshot = await _client.PollSnapshotAsync(_localProfile, "token");

        Assert.Equal(InstanceConnectionState.Degraded, snapshot.State);
        Assert.Equal("Elevated GPU temperature (thermal throttling risk)", snapshot.LastErrorTitle);
        Assert.Equal(86.5, snapshot.GpuTemperatureCelsius);
    }

    /// <summary>
    /// Verifies telemetry polling when the server returns 401 Unauthorized or 403 Forbidden.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task PollSnapshotAsync_UnauthorizedStatus_ReturnsUnauthorizedState(HttpStatusCode statusCode)
    {
        // WHAT: Tests polling response when authentication credentials expire or are rejected.
        // WHY:  Instance must report Unauthorized state to trigger token re-authentication.
        // HOW:  Configures 401/403 status and asserts InstanceConnectionState.Unauthorized.
        _httpHandler.SetResponse("/api/system/telemetry", statusCode, "Unauthorized");

        InstanceConnectionSnapshot snapshot = await _client.PollSnapshotAsync(_localProfile, "expired_token");

        Assert.Equal(InstanceConnectionState.Unauthorized, snapshot.State);
        Assert.Contains("Unauthorized", snapshot.LastErrorTitle);
    }

    /// <summary>
    /// Verifies telemetry polling when server returns 500 Internal Server Error.
    /// </summary>
    [Fact]
    public async Task PollSnapshotAsync_ServerError_ReturnsDegradedStateWithProblemTitle()
    {
        // WHAT: Tests polling when node encounters an internal server error.
        // WHY:  Degraded state allows UI to display the error title without dropping instance offline.
        // HOW:  Returns 500 with RFC 7807 problem details and asserts Degraded state.
        _httpHandler.SetResponse(
            "/api/system/telemetry",
            HttpStatusCode.InternalServerError,
            """{"title": "OpenCL Driver Fault", "status": 500, "traceId": "tr_opencl_1"}""");

        InstanceConnectionSnapshot snapshot = await _client.PollSnapshotAsync(_localProfile, "valid_token");

        Assert.Equal(InstanceConnectionState.Degraded, snapshot.State);
        Assert.Equal("OpenCL Driver Fault", snapshot.LastErrorTitle);
    }

    /// <summary>
    /// Verifies telemetry polling when server returns empty JSON content.
    /// </summary>
    [Fact]
    public async Task PollSnapshotAsync_EmptyTelemetryBody_ReturnsDegradedState()
    {
        // WHAT: Tests response when node returns 200 OK with empty content.
        // WHY:  Protects against null deserialization results.
        // HOW:  Returns whitespace content and asserts Degraded state.
        _httpHandler.SetResponse("/api/system/telemetry", HttpStatusCode.OK, "null");

        InstanceConnectionSnapshot snapshot = await _client.PollSnapshotAsync(_localProfile, "valid_token");

        Assert.Equal(InstanceConnectionState.Degraded, snapshot.State);
        Assert.Equal("Empty or malformed telemetry payload received.", snapshot.LastErrorTitle);
    }

    /// <summary>
    /// Verifies telemetry polling when a transport-level network exception occurs.
    /// </summary>
    [Fact]
    public async Task PollSnapshotAsync_NetworkException_ReturnsOfflineState()
    {
        // WHAT: Tests network unreachable / connection refused fault handling.
        // WHY:  Node must transition to Offline state.
        // HOW:  Configures handler to throw HttpRequestException and asserts Offline snapshot.
        _httpHandler.SetException("/api/system/telemetry", new HttpRequestException("Connection refused."));

        InstanceConnectionSnapshot snapshot = await _client.PollSnapshotAsync(_localProfile, "token");

        Assert.Equal(InstanceConnectionState.Offline, snapshot.State);
        Assert.Equal("Connection refused.", snapshot.LastErrorTitle);
    }

    /// <summary>
    /// Verifies telemetry polling when connection times out.
    /// </summary>
    [Fact]
    public async Task PollSnapshotAsync_TimeoutException_ReturnsOfflineState()
    {
        // WHAT: Tests timeout fault handling.
        // WHY:  Unresponsive instances must transition to Offline state.
        // HOW:  Configures handler to throw TaskCanceledException and asserts Offline snapshot.
        _httpHandler.SetException("/api/system/telemetry", new TaskCanceledException());

        InstanceConnectionSnapshot snapshot = await _client.PollSnapshotAsync(_localProfile, "token");

        Assert.Equal(InstanceConnectionState.Offline, snapshot.State);
        Assert.Equal("Connection timed out.", snapshot.LastErrorTitle);
    }

    /// <summary>
    /// Verifies StartSimulationCampaignAsync input validation.
    /// </summary>
    [Fact]
    public async Task StartSimulationCampaignAsync_InvalidArguments_ThrowsExpectedExceptions()
    {
        // WHAT: Tests argument validation in StartSimulationCampaignAsync.
        // WHY:  Guarantees unsupported campaign types and null inputs are rejected.
        // HOW:  Asserts ArgumentNullException or ArgumentException.
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _client.StartSimulationCampaignAsync(null!, "token", "serum-stability"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => _client.StartSimulationCampaignAsync(_localProfile, "", "serum-stability"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => _client.StartSimulationCampaignAsync(_localProfile, "token", ""));
        await Assert.ThrowsAsync<ArgumentException>(
            () => _client.StartSimulationCampaignAsync(_localProfile, "token", "unsupported-simulation-type"));
    }

    /// <summary>
    /// Verifies triggering all four supported simulation campaigns.
    /// </summary>
    /// <param name="simulationType">The campaign slug under test.</param>
    [Theory]
    [InlineData("serum-stability")]
    [InlineData("aptamer-gating")]
    [InlineData("payload-leakage")]
    [InlineData("pbpk-endocrine")]
    public async Task StartSimulationCampaignAsync_SupportedTypes_SendsPostAndReturnsTrue(string simulationType)
    {
        // WHAT: Tests starting each supported simulation campaign.
        // WHY:  Verifies correct route formatting and successful execution.
        // HOW:  Configures 200 OK response on route and asserts method returns true.
        _httpHandler.SetResponse($"/api/simulations/run/{simulationType}", HttpStatusCode.OK, "{}");

        bool result = await _client.StartSimulationCampaignAsync(_localProfile, "valid_token", simulationType);
        Assert.True(result);
    }

    /// <summary>
    /// Verifies StartSimulationCampaignAsync error handling when server returns 500 error.
    /// </summary>
    [Fact]
    public async Task StartSimulationCampaignAsync_ServerError_ThrowsNodeApiException()
    {
        // WHAT: Tests error handling when triggering a campaign fails on the node.
        // WHY:  Must throw sanitized NodeApiException.
        // HOW:  Stubs 500 response and asserts NodeApiException.
        _httpHandler.SetResponse(
            "/api/simulations/run/serum-stability",
            HttpStatusCode.InternalServerError,
            """{"title": "Simulation Engine Failure", "status": 500, "traceId": "sim_err_1"}""");

        NodeApiException ex = await Assert.ThrowsAsync<NodeApiException>(
            () => _client.StartSimulationCampaignAsync(_localProfile, "valid_token", "serum-stability"));

        Assert.Equal(500, ex.StatusCode);
        Assert.Equal("Simulation Engine Failure", ex.Title);
        Assert.Equal("sim_err_1", ex.TraceId);
    }

    /// <summary>
    /// Verifies GetTelemetryAsync retrieves and deserializes full telemetry metrics.
    /// </summary>
    [Fact]
    public async Task GetTelemetryAsync_NominalResponse_ReturnsTelemetrySnapshot()
    {
        // WHAT: Tests strongly-typed GetTelemetryAsync query method.
        // WHY:  Enables granular inspection of memory and hardware accelerator telemetry.
        // HOW:  Stubs /api/system/telemetry response and verifies deserialized properties.
        _httpHandler.SetResponse(
            "/api/system/telemetry",
            HttpStatusCode.OK,
            """
            {
                "nodeName": "node-beta",
                "acceleratorBackend": "NVIDIA CUDA (sm_86)",
                "framesParsed": 250000,
                "framesPerSecond": 85.0,
                "heapMemoryMb": 64.0,
                "workingSetMb": 128.0,
                "gcGen0Collections": 2,
                "gcGen1Collections": 1,
                "gcGen2Collections": 0,
                "uptime": "02:00:00"
            }
            """);

        NodeTelemetrySnapshot telemetry = await _client.GetTelemetryAsync(_localProfile, "token");

        Assert.Equal("node-beta", telemetry.NodeName);
        Assert.Equal("NVIDIA CUDA (sm_86)", telemetry.AcceleratorBackend);
        Assert.Equal(250000, telemetry.FramesParsed);
        Assert.Equal(85.0, telemetry.FramesPerSecond);
        Assert.Equal(64.0, telemetry.HeapMemoryMb);
        Assert.Equal(128.0, telemetry.WorkingSetMb);
        Assert.Equal(2, telemetry.GcGen0Collections);
        Assert.Equal(1, telemetry.GcGen1Collections);
        Assert.Equal(0, telemetry.GcGen2Collections);
        Assert.Equal("02:00:00", telemetry.Uptime);
        Assert.Equal(0.0, telemetry.CpuUtilizationPercent);
        Assert.Equal(0.0, telemetry.GpuUtilizationPercent);
        Assert.Equal(0.0, telemetry.GpuTemperatureCelsius);
        Assert.Equal(0, telemetry.ActiveSimulationCount);
    }

    /// <summary>
    /// Verifies GetTelemetryAsync error paths.
    /// </summary>
    [Fact]
    public async Task GetTelemetryAsync_Errors_ThrowsExpectedExceptions()
    {
        // WHAT: Tests validation and error handling on GetTelemetryAsync.
        // WHY:  Guarantees robust rejection of invalid arguments and server failures.
        // HOW:  Asserts ArgumentNullException, ArgumentException, and NodeApiException.
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetTelemetryAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetTelemetryAsync(_localProfile, ""));

        _httpHandler.SetResponse("/api/system/telemetry", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetTelemetryAsync(_localProfile, "token"));

        _httpHandler.SetResponse("/api/system/telemetry", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetTelemetryAsync(_localProfile, "token"));
    }

    /// <summary>
    /// Verifies GetCampaignsAsync retrieves and deserializes the campaign catalog.
    /// </summary>
    [Fact]
    public async Task GetCampaignsAsync_NominalResponse_ReturnsCampaignCatalog()
    {
        // WHAT: Tests GetCampaignsAsync query method.
        // WHY:  Enables UI to populate available simulation campaigns.
        // HOW:  Stubs /api/simulations/campaigns and verifies deserialized list.
        _httpHandler.SetResponse(
            "/api/simulations/campaigns",
            HttpStatusCode.OK,
            """
            [
                {
                    "campaignId": "CAMPAIGN_SERUM_STABILITY",
                    "name": "Serum Stability",
                    "simulationEngine": "oxDNA",
                    "description": "Tests physiological serum stability.",
                    "physicsGoal": "Validate crossover stability under 0.8 mM Mg2+."
                }
            ]
            """);

        IReadOnlyList<SimulationCampaignSummary> campaigns = await _client.GetCampaignsAsync(_localProfile, "token");

        Assert.Single(campaigns);
        Assert.Equal("CAMPAIGN_SERUM_STABILITY", campaigns[0].CampaignId);
        Assert.Equal("Serum Stability", campaigns[0].Name);
        Assert.Equal("oxDNA", campaigns[0].SimulationEngine);
        Assert.Equal("Tests physiological serum stability.", campaigns[0].Description);
        Assert.Equal("Validate crossover stability under 0.8 mM Mg2+.", campaigns[0].PhysicsGoal);
        Assert.False(campaigns[0].IsActive);
    }

    /// <summary>
    /// Verifies GetCampaignsAsync error handling and empty fallback.
    /// </summary>
    [Fact]
    public async Task GetCampaignsAsync_EmptyAndErrorResponses_HandledCleanly()
    {
        // WHAT: Tests error handling and empty catalog response.
        // WHY:  Guarantees safe array fallback when null body returned.
        // HOW:  Stubs null and 500 error; verifies empty array and NodeApiException.
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetCampaignsAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetCampaignsAsync(_localProfile, ""));

        _httpHandler.SetResponse("/api/simulations/campaigns", HttpStatusCode.OK, "null");
        IReadOnlyList<SimulationCampaignSummary> empty = await _client.GetCampaignsAsync(_localProfile, "token");
        Assert.Empty(empty);

        _httpHandler.SetResponse("/api/simulations/campaigns", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetCampaignsAsync(_localProfile, "token"));
    }

    /// <summary>
    /// Verifies GetEndocrineProfileAsync retrieves and deserializes active hormone setpoints.
    /// </summary>
    [Fact]
    public async Task GetEndocrineProfileAsync_NominalResponse_ReturnsProfile()
    {
        // WHAT: Tests GetEndocrineProfileAsync query method.
        // WHY:  Provides target steroid hormone concentration setpoints.
        // HOW:  Stubs /api/endocrine/profile and verifies deserialized EndocrineProfileSummary.
        _httpHandler.SetResponse(
            "/api/endocrine/profile",
            HttpStatusCode.OK,
            """
            {
                "dominance": "Estrogenic",
                "targetEstradiolPicoMolar": 734.2,
                "targetTestosteroneNanoMolar": 1.2,
                "targetProgesteroneNanoMolar": 15.0,
                "lastUpdatedUtc": "2026-10-28T12:00:00Z"
            }
            """);

        EndocrineProfileSummary profile = await _client.GetEndocrineProfileAsync(_localProfile, "token");

        Assert.Equal("Estrogenic", profile.Dominance);
        Assert.Equal(734.2, profile.TargetEstradiolPicoMolar);
        Assert.Equal(1.2, profile.TargetTestosteroneNanoMolar);
        Assert.Equal(15.0, profile.TargetProgesteroneNanoMolar);
    }

    /// <summary>
    /// Verifies GetEndocrineProfileAsync error handling.
    /// </summary>
    [Fact]
    public async Task GetEndocrineProfileAsync_Errors_ThrowsExpectedExceptions()
    {
        // WHAT: Tests validation and error handling on GetEndocrineProfileAsync.
        // WHY:  Rejects invalid arguments and server errors.
        // HOW:  Asserts ArgumentNullException, ArgumentException, and NodeApiException.
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetEndocrineProfileAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetEndocrineProfileAsync(_localProfile, ""));

        _httpHandler.SetResponse("/api/endocrine/profile", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetEndocrineProfileAsync(_localProfile, "token"));

        _httpHandler.SetResponse("/api/endocrine/profile", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetEndocrineProfileAsync(_localProfile, "token"));
    }

    /// <summary>
    /// Verifies SimulateClosedLoopEndocrineAsync nominal simulation and error handling.
    /// </summary>
    [Fact]
    public async Task SimulateClosedLoopEndocrineAsync_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests SimulateClosedLoopEndocrineAsync for successful dispatch and edge errors.
        // WHY:  Ensures numerical simulation parameters are validated and results are parsed.
        // HOW:  Stubs /api/endocrine/closed-loop/simulate, tests happy path and invalid inputs.
        var req = new ClosedLoopSimulationRequest { TotalHours = 24, StepHours = 1.0 };
        _httpHandler.SetResponse(
            "/api/endocrine/closed-loop/simulate",
            HttpStatusCode.OK,
            """
            {
                "points": [],
                "finalEstradiolPmolL": 550.0,
                "finalTestosteroneNmolL": 1.2,
                "finalLhIuL": 0.5,
                "finalFshIuL": 0.8,
                "steadyStateReached": true,
                "timeToSuppressionHours": 12.5
            }
            """);

        ClosedLoopSimulationResult res = await _client.SimulateClosedLoopEndocrineAsync(_localProfile, "token", req);
        Assert.True(res.SteadyStateReached);
        Assert.Equal(12.5, res.TimeToSuppressionHours);

        // Validation & Errors
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.SimulateClosedLoopEndocrineAsync(null!, "token", req));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.SimulateClosedLoopEndocrineAsync(_localProfile, "", req));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.SimulateClosedLoopEndocrineAsync(_localProfile, "token", null!));

        _httpHandler.SetResponse("/api/endocrine/closed-loop/simulate", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.SimulateClosedLoopEndocrineAsync(_localProfile, "token", req));

        _httpHandler.SetResponse("/api/endocrine/closed-loop/simulate", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.SimulateClosedLoopEndocrineAsync(_localProfile, "token", req));
    }

    /// <summary>
    /// Verifies DiscoverFederationPeersAsync nominal list retrieval and error handling.
    /// </summary>
    [Fact]
    public async Task DiscoverFederationPeersAsync_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests DiscoverFederationPeersAsync for peer list querying and error handling.
        // WHY:  Enables decentralized swarm topology inspection.
        // HOW:  Stubs /api/federation/peers with peers, empty, null, and server error.
        _httpHandler.SetResponse(
            "/api/federation/peers",
            HttpStatusCode.OK,
            """
            [
                {
                    "nodeId": "peer-01",
                    "endpointUri": "http://10.0.0.1:5000",
                    "publicKeyBase64": "pubKeyBase64Data",
                    "algorithm": "Ed25519",
                    "latencyMilliseconds": 14.2,
                    "lastSeenUtc": "2026-10-28T12:00:00Z"
                }
            ]
            """);

        IReadOnlyList<FederationPeerNode> peers = await _client.DiscoverFederationPeersAsync(_localProfile, "token");
        Assert.Single(peers);
        Assert.Equal("peer-01", peers[0].NodeId);

        // Null response returns empty list
        _httpHandler.SetResponse("/api/federation/peers", HttpStatusCode.OK, "null");
        IReadOnlyList<FederationPeerNode> emptyPeers = await _client.DiscoverFederationPeersAsync(_localProfile, "token");
        Assert.Empty(emptyPeers);

        // Errors
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.DiscoverFederationPeersAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DiscoverFederationPeersAsync(_localProfile, ""));

        _httpHandler.SetResponse("/api/federation/peers", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.DiscoverFederationPeersAsync(_localProfile, "token"));
    }

    /// <summary>
    /// Verifies BuildFederationManifestAsync nominal execution and error handling.
    /// </summary>
    [Fact]
    public async Task BuildFederationManifestAsync_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests BuildFederationManifestAsync for trajectory chunk manifest synthesis.
        // WHY:  Ensures trajectory files can be chunked and signed for P2P distribution.
        // HOW:  Stubs /api/federation/manifests/build and tests validation and errors.
        var req = new FederationBuildManifestRequest { TrajectoryFileName = "run1.xyz" };
        _httpHandler.SetResponse(
            "/api/federation/manifests/build",
            HttpStatusCode.OK,
            """
            {
                "datasetName": "run1.xyz",
                "totalSizeBytes": 2048,
                "chunkSizeBytes": 1024,
                "totalChunks": 2,
                "chunks": [],
                "signingNodeId": "node-1",
                "signatureAlgorithm": "Ed25519",
                "signatureBase64": "sig123",
                "manifestTimestampUtc": "2026-10-28T12:00:00Z"
            }
            """);

        TrajectoryFederationManifest manifest = await _client.BuildFederationManifestAsync(_localProfile, "token", req);
        Assert.Equal("run1.xyz", manifest.DatasetName);
        Assert.Equal(2, manifest.TotalChunks);

        // Validation & Errors
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.BuildFederationManifestAsync(null!, "token", req));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.BuildFederationManifestAsync(_localProfile, "", req));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.BuildFederationManifestAsync(_localProfile, "token", null!));

        _httpHandler.SetResponse("/api/federation/manifests/build", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.BuildFederationManifestAsync(_localProfile, "token", req));

        _httpHandler.SetResponse("/api/federation/manifests/build", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.BuildFederationManifestAsync(_localProfile, "token", req));
    }

    /// <summary>
    /// Verifies VerifyFederationManifestAsync nominal execution and error handling.
    /// </summary>
    [Fact]
    public async Task VerifyFederationManifestAsync_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests VerifyFederationManifestAsync for cryptographic manifest validation.
        // WHY:  Guarantees integrity of received swarm trajectory manifests.
        // HOW:  Stubs /api/federation/manifests/verify and verifies response and errors.
        var manifest = new TrajectoryFederationManifest
        {
            DatasetName = "traj.xyz",
            TotalSizeBytes = 1024,
            ChunkSizeBytes = 1024,
            TotalChunks = 1,
            Chunks = [],
            SigningNodeId = "node-1",
            SignatureAlgorithm = "Ed25519",
            SignatureBase64 = "sig",
            ManifestTimestampUtc = DateTimeOffset.UtcNow
        };

        _httpHandler.SetResponse(
            "/api/federation/manifests/verify",
            HttpStatusCode.OK,
            """
            {
                "isValid": true,
                "trajectoryId": "traj.xyz",
                "chunkCount": 1,
                "totalSizeBytes": 1024
            }
            """);

        FederationVerifyManifestResponse res = await _client.VerifyFederationManifestAsync(_localProfile, "token", manifest);
        Assert.True(res.IsValid);
        Assert.Equal("traj.xyz", res.TrajectoryId);

        // Validation & Errors
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.VerifyFederationManifestAsync(null!, "token", manifest));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.VerifyFederationManifestAsync(_localProfile, "", manifest));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.VerifyFederationManifestAsync(_localProfile, "token", null!));

        _httpHandler.SetResponse("/api/federation/manifests/verify", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.VerifyFederationManifestAsync(_localProfile, "token", manifest));

        _httpHandler.SetResponse("/api/federation/manifests/verify", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.VerifyFederationManifestAsync(_localProfile, "token", manifest));
    }

    /// <summary>
    /// Verifies AnalyzeGelAsync nominal processing and error handling.
    /// </summary>
    [Fact]
    public async Task AnalyzeGelAsync_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests AnalyzeGelAsync for gel image processing and quality metrics.
        // WHY:  Validates automated DNA origami folding yield analysis via REST.
        // HOW:  Stubs /api/fabrication/gel/analyze and verifies deserialization and validation.
        var req = new GelAnalysisRequest
        {
            ImageBytes = [1, 2, 3, 4],
            Width = 2,
            Height = 2,
            ExpectedLaneCount = 1,
            BackgroundThreshold = 0.1,
            LadderLaneNumber = 1
        };

        _httpHandler.SetResponse(
            "/api/fabrication/gel/analyze",
            HttpStatusCode.OK,
            """
            {
                "width": 2,
                "height": 2,
                "detectedLaneCount": 1,
                "overallFoldingSuccessRate": 95.0,
                "lanes": []
            }
            """);

        GelAnalysisResult res = await _client.AnalyzeGelAsync(_localProfile, "token", req);
        Assert.Equal(1, res.DetectedLaneCount);
        Assert.Equal(95.0, res.OverallFoldingSuccessRate);

        // Validation & Errors
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.AnalyzeGelAsync(null!, "token", req));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.AnalyzeGelAsync(_localProfile, "", req));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.AnalyzeGelAsync(_localProfile, "token", null!));

        _httpHandler.SetResponse("/api/fabrication/gel/analyze", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.AnalyzeGelAsync(_localProfile, "token", req));

        _httpHandler.SetResponse("/api/fabrication/gel/analyze", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.AnalyzeGelAsync(_localProfile, "token", req));
    }

    /// <summary>
    /// Verifies RunBenchmarkAsync nominal execution and error handling.
    /// </summary>
    [Fact]
    public async Task RunBenchmarkAsync_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests RunBenchmarkAsync for on-demand hardware accelerator qualification.
        // WHY:  Ensures compute benchmarks can be initiated from client API.
        // HOW:  Stubs /api/system/benchmark/run and verifies report deserialization.
        _httpHandler.SetResponse(
            "/api/system/benchmark/run",
            HttpStatusCode.OK,
            """
            {
                "deviceName": "Intel Arc A770",
                "driverVersion": "31.0",
                "singlePrecisionGflops": 19500.0,
                "doublePrecisionGflops": 1200.0,
                "memoryBandwidthGigaBytesPerSecond": 512.0,
                "temperatureCelsius": 55.0,
                "powerUsageWatts": 225.0,
                "readiness": "Go",
                "benchmarkedAtUtc": "2026-10-28T12:00:00Z",
                "benchmarkTimestampUtc": "2026-10-28T12:00:00Z",
                "recommendations": []
            }
            """);

        AcceleratorBenchmarkReport report = await _client.RunBenchmarkAsync(_localProfile, "token");
        Assert.Equal("Intel Arc A770", report.DeviceName);
        Assert.Equal(19500.0, report.SinglePrecisionGflops);

        // Validation & Errors
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.RunBenchmarkAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.RunBenchmarkAsync(_localProfile, ""));

        _httpHandler.SetResponse("/api/system/benchmark/run", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.RunBenchmarkAsync(_localProfile, "token"));

        _httpHandler.SetResponse("/api/system/benchmark/run", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.RunBenchmarkAsync(_localProfile, "token"));
    }

    /// <summary>
    /// Verifies GetLatestBenchmarkAsync nominal retrieval, 404 null return, and error handling.
    /// </summary>
    [Fact]
    public async Task GetLatestBenchmarkAsync_NominalNotFoundAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests GetLatestBenchmarkAsync for cached report querying and 404 handling.
        // WHY:  Allows clients to inspect hardware readiness without triggering compute tests.
        // HOW:  Stubs /api/system/benchmark/latest with 200, 404, and 500 status codes.
        _httpHandler.SetResponse(
            "/api/system/benchmark/latest",
            HttpStatusCode.OK,
            """
            {
                "deviceName": "NVIDIA RTX 4090",
                "driverVersion": "535.0",
                "singlePrecisionGflops": 82500.0,
                "doublePrecisionGflops": 2500.0,
                "memoryBandwidthGigaBytesPerSecond": 1008.0,
                "temperatureCelsius": 62.0,
                "powerUsageWatts": 450.0,
                "readiness": "Go",
                "benchmarkedAtUtc": "2026-10-28T12:00:00Z",
                "benchmarkTimestampUtc": "2026-10-28T12:00:00Z",
                "recommendations": []
            }
            """);

        AcceleratorBenchmarkReport? report = await _client.GetLatestBenchmarkAsync(_localProfile, "token");
        Assert.NotNull(report);
        Assert.Equal("NVIDIA RTX 4090", report.DeviceName);

        // 404 returns null cleanly
        _httpHandler.SetResponse("/api/system/benchmark/latest", HttpStatusCode.NotFound, "{}");
        AcceleratorBenchmarkReport? absentReport = await _client.GetLatestBenchmarkAsync(_localProfile, "token");
        Assert.Null(absentReport);

        // Validation & Errors
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetLatestBenchmarkAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetLatestBenchmarkAsync(_localProfile, ""));

        _httpHandler.SetResponse("/api/system/benchmark/latest", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetLatestBenchmarkAsync(_localProfile, "token"));
    }

    /// <summary>
    /// Verifies GenerateOpentronsProtocolAsync nominal execution and error handling.
    /// </summary>
    [Fact]
    public async Task GenerateOpentronsProtocolAsync_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests GenerateOpentronsProtocolAsync for OT-2/Flex script generation.
        // WHY:  Automates wet-lab pipetting script synthesis from client.
        // HOW:  Stubs /api/fabrication/opentrons/generate and asserts returned script.
        var req = new GenerateOpentronsScriptRequest
        {
            SequenceOrSampleId = "sample-1",
            Regimen = TherapeuticRegimen.MaleToFemale
        };

        _httpHandler.SetResponse(
            "/api/fabrication/opentrons/generate",
            HttpStatusCode.OK,
            """
            {
                "pythonScript": "# metadata\nfrom opentrons import protocol_api\n",
                "scriptSha256": "sha123",
                "transferCount": 8,
                "sourcePlatesRequired": 1,
                "tipRacksRequired": 1,
                "hasThermocyclerRamp": true,
                "estimatedRunDurationMinutes": 45.0
            }
            """);

        OpentronsProtocolResult res = await _client.GenerateOpentronsProtocolAsync(_localProfile, "token", req);
        Assert.Contains("protocol_api", res.PythonScript);
        Assert.Equal(8, res.TransferCount);

        // Validation & Errors
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GenerateOpentronsProtocolAsync(null!, "token", req));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GenerateOpentronsProtocolAsync(_localProfile, "", req));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GenerateOpentronsProtocolAsync(_localProfile, "token", null!));

        _httpHandler.SetResponse("/api/fabrication/opentrons/generate", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GenerateOpentronsProtocolAsync(_localProfile, "token", req));

        _httpHandler.SetResponse("/api/fabrication/opentrons/generate", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GenerateOpentronsProtocolAsync(_localProfile, "token", req));
    }

    /// <summary>
    /// Verifies GetPatientDnaRegimensAsync nominal retrieval and error handling.
    /// </summary>
    [Fact]
    public async Task GetPatientDnaRegimensAsync_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests GetPatientDnaRegimensAsync for downloading complete patient Digital DNA packages.
        // WHY:  Supplies scaffold sequences, staples, and provenance to client devices.
        // HOW:  Stubs /api/patient/dna with and without query params and verifies package.
        _httpHandler.SetResponse(
            "/api/patient/dna",
            HttpStatusCode.OK,
            """
            {
                "patientId": "patient-01",
                "regimen": "MaleToFemale",
                "scaffoldFasta": "ACGTACGT",
                "provenanceSha256": "manifest_hash_123"
            }
            """);

        PatientDigitalDnaPackage pkg1 = await _client.GetPatientDnaRegimensAsync(_localProfile, "token");
        Assert.Equal("patient-01", pkg1.PatientId);
        Assert.Equal("ACGTACGT", pkg1.ScaffoldFasta);

        _httpHandler.SetResponse(
            "/api/patient/dna",
            HttpStatusCode.OK,
            """
            {
                "patientId": "patient-custom",
                "regimen": "FemaleToMale",
                "scaffoldFasta": "TGCATGCA",
                "provenanceSha256": "manifest_hash_456"
            }
            """);

        PatientDigitalDnaPackage pkg2 = await _client.GetPatientDnaRegimensAsync(_localProfile, "token", "patient-custom");
        Assert.Equal("patient-custom", pkg2.PatientId);
        Assert.Equal("TGCATGCA", pkg2.ScaffoldFasta);

        // Validation & Errors
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetPatientDnaRegimensAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetPatientDnaRegimensAsync(_localProfile, ""));

        _httpHandler.SetResponse("/api/patient/dna", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetPatientDnaRegimensAsync(_localProfile, "token"));

        _httpHandler.SetResponse("/api/patient/dna", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetPatientDnaRegimensAsync(_localProfile, "token"));
    }

    /// <summary>
    /// Verifies GetPatientDigitalDnaAsync nominal record retrieval and error handling.
    /// </summary>
    [Fact]
    public async Task GetPatientDigitalDnaAsync_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests GetPatientDigitalDnaAsync for patient DNA record collection querying.
        // WHY:  Allows reviewing digitized DNA nanocarrier blueprints in client UI.
        // HOW:  Stubs /api/patients/{username}/dna, asserts parsed records, null response handling, and errors.
        _httpHandler.SetResponse(
            "/api/patients/patient-01/dna",
            HttpStatusCode.OK,
            """
            [
                {
                    "id": "rec-01",
                    "username": "patient-01",
                    "title": "Hexagonal Nanotube Carrier"
                }
            ]
            """);

        IReadOnlyList<PatientDigitalDnaRecord> records = await _client.GetPatientDigitalDnaAsync(_localProfile, "token", "patient-01");
        Assert.Single(records);
        Assert.Equal("rec-01", records[0].Id);

        // Null response returns empty list
        _httpHandler.SetResponse("/api/patients/patient-01/dna", HttpStatusCode.OK, "null");
        IReadOnlyList<PatientDigitalDnaRecord> emptyRecords = await _client.GetPatientDigitalDnaAsync(_localProfile, "token", "patient-01");
        Assert.Empty(emptyRecords);

        // Validation & Errors
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetPatientDigitalDnaAsync(null!, "token", "patient-01"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetPatientDigitalDnaAsync(_localProfile, "", "patient-01"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetPatientDigitalDnaAsync(_localProfile, "token", ""));

        _httpHandler.SetResponse("/api/patients/patient-01/dna", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetPatientDigitalDnaAsync(_localProfile, "token", "patient-01"));
    }

    /// <summary>
    /// Verifies ListTrajectoriesAsync nominal retrieval, empty response, and error paths.
    /// </summary>
    [Fact]
    public async Task ListTrajectoriesAsync_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests ListTrajectoriesAsync endpoint communication against /api/datasets.
        // WHY:  Ensures trajectory file discovery works over REST.
        // HOW:  Mocks /api/datasets, tests populated list, empty list, validation, and HTTP 500 error.
        _httpHandler.SetResponse(
            "/api/datasets",
            HttpStatusCode.OK,
            """
            [
                {
                    "name": "dna_run_01.xyz",
                    "sizeBytes": 1024,
                    "sizeFormatted": "1.0 KB",
                    "lastModified": "2026-10-20T18:00:00Z",
                    "format": "xyz"
                }
            ]
            """);

        IReadOnlyList<TrajectoryFileItem> items = await _client.ListTrajectoriesAsync(_localProfile, "token");
        Assert.Single(items);
        Assert.Equal("dna_run_01.xyz", items[0].Name);

        _httpHandler.SetResponse("/api/datasets", HttpStatusCode.OK, "null");
        IReadOnlyList<TrajectoryFileItem> empty = await _client.ListTrajectoriesAsync(_localProfile, "token");
        Assert.Empty(empty);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ListTrajectoriesAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ListTrajectoriesAsync(_localProfile, ""));

        _httpHandler.SetResponse("/api/datasets", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ListTrajectoriesAsync(_localProfile, "token"));
    }

    /// <summary>
    /// Verifies ExportDatasetAsync nominal packaging, null response handling, and error paths.
    /// </summary>
    [Fact]
    public async Task ExportDatasetAsync_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests ExportDatasetAsync endpoint communication against /api/datasets/export.
        // WHY:  Ensures trajectory export bundles with manifests are correctly dispatched.
        // HOW:  Mocks /api/datasets/export, asserts manifest properties, null response exception, and errors.
        _httpHandler.SetResponse(
            "/api/datasets/export",
            HttpStatusCode.OK,
            """
            {
                "dataset": "dataset_01",
                "timestamp": "2026-10-20T18:00:00Z",
                "archive": "dataset_01.tar.gz",
                "sha256": "abc123sha",
                "sizeBytes": 2048,
                "includedFiles": []
            }
            """);

        ExportRequest req = new() { DatasetName = "dataset_01" };
        DatasetManifest manifest = await _client.ExportDatasetAsync(_localProfile, "token", req);
        Assert.Equal("dataset_01", manifest.DatasetName);
        Assert.Equal("abc123sha", manifest.Sha256Checksum);

        _httpHandler.SetResponse("/api/datasets/export", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ExportDatasetAsync(_localProfile, "token", req));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ExportDatasetAsync(null!, "token", req));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ExportDatasetAsync(_localProfile, "", req));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ExportDatasetAsync(_localProfile, "token", null!));

        _httpHandler.SetResponse("/api/datasets/export", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ExportDatasetAsync(_localProfile, "token", req));
    }

    /// <summary>
    /// Verifies GetAllConfigurationsAsync nominal retrieval, empty response, and error paths.
    /// </summary>
    [Fact]
    public async Task GetAllConfigurationsAsync_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests GetAllConfigurationsAsync endpoint communication against /api/configuration.
        // WHY:  Ensures configuration auditing operates cleanly over REST.
        // HOW:  Mocks /api/configuration, tests populated list, empty list, validation, and error.
        _httpHandler.SetResponse(
            "/api/configuration",
            HttpStatusCode.OK,
            """
            [
                {
                    "key": "Simulation:Backend",
                    "value": "OpenCL",
                    "defaultValue": "OpenCL",
                    "description": "Accelerator engine",
                    "source": "DefaultFallback",
                    "category": "Simulation"
                }
            ]
            """);

        IReadOnlyList<ConfigurationItem> configs = await _client.GetAllConfigurationsAsync(_localProfile, "token");
        Assert.Single(configs);
        Assert.Equal("Simulation:Backend", configs[0].Key);

        _httpHandler.SetResponse("/api/configuration", HttpStatusCode.OK, "null");
        IReadOnlyList<ConfigurationItem> empty = await _client.GetAllConfigurationsAsync(_localProfile, "token");
        Assert.Empty(empty);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetAllConfigurationsAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetAllConfigurationsAsync(_localProfile, ""));

        _httpHandler.SetResponse("/api/configuration", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetAllConfigurationsAsync(_localProfile, "token"));
    }

    /// <summary>
    /// Verifies SetConfigurationOverrideAsync and DeleteConfigurationOverrideAsync nominal and error paths.
    /// </summary>
    [Fact]
    public async Task ConfigurationOverrides_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests SetConfigurationOverrideAsync and DeleteConfigurationOverrideAsync operations.
        // WHY:  Allows persisting and reverting runtime configuration overrides.
        // HOW:  Mocks /api/configuration/override, asserts success and error responses.
        _httpHandler.SetResponse("/api/configuration/override", HttpStatusCode.OK, "{}");
        ConfigOverrideRequest req = new() { Key = "Simulation:MaxFrames", Value = "5000" };
        await _client.SetConfigurationOverrideAsync(_localProfile, "token", req);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.SetConfigurationOverrideAsync(null!, "token", req));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.SetConfigurationOverrideAsync(_localProfile, "", req));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.SetConfigurationOverrideAsync(_localProfile, "token", null!));

        _httpHandler.SetResponse("/api/configuration/override", HttpStatusCode.BadRequest, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.SetConfigurationOverrideAsync(_localProfile, "token", req));

        // Delete override
        _httpHandler.SetResponse("/api/configuration/override/Simulation%3AMaxFrames", HttpStatusCode.OK, "{}");
        await _client.DeleteConfigurationOverrideAsync(_localProfile, "token", "Simulation:MaxFrames");

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.DeleteConfigurationOverrideAsync(null!, "token", "key"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeleteConfigurationOverrideAsync(_localProfile, "", "key"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeleteConfigurationOverrideAsync(_localProfile, "token", ""));

        _httpHandler.SetResponse("/api/configuration/override/Simulation%3AMaxFrames", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.DeleteConfigurationOverrideAsync(_localProfile, "token", "Simulation:MaxFrames"));
    }

    /// <summary>
    /// Verifies ListSubjectGrantsAsync nominal query with parameters, empty response, and error paths.
    /// </summary>
    [Fact]
    public async Task ListSubjectGrantsAsync_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests ListSubjectGrantsAsync endpoint communication against /api/subject-access/grants.
        // WHY:  Ensures subject grant auditing operates cleanly with query parameter filtering.
        // HOW:  Mocks /api/subject-access/grants with and without filters, asserts results and exceptions.
        _httpHandler.SetResponse(
            "/api/subject-access/grants",
            HttpStatusCode.OK,
            """
            [
                {
                    "researcherUsername": "res-01",
                    "subjectUsername": "pt-01",
                    "accessLevel": 2,
                    "grantedBy": "admin",
                    "grantedAtUtc": "2026-11-04T00:00:00Z"
                }
            ]
            """);

        IReadOnlyList<SubjectGrant> grants = await _client.ListSubjectGrantsAsync(_localProfile, "token", "res-01", "pt-01");
        Assert.Single(grants);
        Assert.Equal("res-01", grants[0].ResearcherUsername);

        _httpHandler.SetResponse("/api/subject-access/grants", HttpStatusCode.OK, "null");
        IReadOnlyList<SubjectGrant> empty = await _client.ListSubjectGrantsAsync(_localProfile, "token");
        Assert.Empty(empty);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ListSubjectGrantsAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ListSubjectGrantsAsync(_localProfile, ""));

        _httpHandler.SetResponse("/api/subject-access/grants", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ListSubjectGrantsAsync(_localProfile, "token"));
    }

    /// <summary>
    /// Verifies GetComplianceStatusAsync nominal response, null deserialization fallback, and error handling.
    /// </summary>
    [Fact]
    public async Task GetComplianceStatusAsync_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests GetComplianceStatusAsync endpoint communication against /api/compliance/status.
        // WHY:  Ensures regulatory compliance scorecard retrieval operates cleanly.
        // HOW:  Mocks /api/compliance/status with JSON, asserts populated report and exception handling.
        _httpHandler.SetResponse(
            "/api/compliance/status",
            HttpStatusCode.OK,
            """
            {
                "encryptionAtRestAlgorithm": "ML-KEM-768 + AES-256-GCM (NIST FIPS 203)",
                "auditSignatureAlgorithm": "ML-DSA-65 (NIST FIPS 204)",
                "isEncryptionAtRestActive": true,
                "isAuditLedgerIntact": true,
                "auditRecordCount": 42
            }
            """);

        ComplianceStatusReport report = await _client.GetComplianceStatusAsync(_localProfile, "token");
        Assert.NotNull(report);
        Assert.Equal("ML-KEM-768 + AES-256-GCM (NIST FIPS 203)", report.EncryptionAtRestAlgorithm);
        Assert.True(report.IsEncryptionAtRestActive);
        Assert.True(report.IsAuditLedgerIntact);
        Assert.Equal(42, report.AuditRecordCount);

        _httpHandler.SetResponse("/api/compliance/status", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetComplianceStatusAsync(_localProfile, "token"));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetComplianceStatusAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetComplianceStatusAsync(_localProfile, ""));

        _httpHandler.SetResponse("/api/compliance/status", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetComplianceStatusAsync(_localProfile, "token"));
    }

    /// <summary>
    /// Verifies GetAuditLedgerAsync nominal response with and without limit parameter, empty response, and error paths.
    /// </summary>
    [Fact]
    public async Task GetAuditLedgerAsync_NominalWithLimitAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests GetAuditLedgerAsync endpoint communication against /api/compliance/audit-ledger.
        // WHY:  Ensures FDA 21 CFR Part 11 audit trail querying operates cleanly with optional limit query param.
        // HOW:  Mocks /api/compliance/audit-ledger with JSON, asserts returned records and exception handling.
        _httpHandler.SetResponse(
            "/api/compliance/audit-ledger",
            HttpStatusCode.OK,
            """
            [
                {
                    "sequenceNumber": 1,
                    "previousHash": "0000000000000000000000000000000000000000000000000000000000000000",
                    "currentHash": "abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234",
                    "operatorId": "admin",
                    "action": "UserLogin",
                    "resource": "/api/auth/token"
                }
            ]
            """);

        IReadOnlyList<AuditRecord> recordsWithLimit = await _client.GetAuditLedgerAsync(_localProfile, "token", 10);
        Assert.Single(recordsWithLimit);
        Assert.Equal(1, recordsWithLimit[0].SequenceNumber);
        Assert.Equal("admin", recordsWithLimit[0].OperatorId);

        IReadOnlyList<AuditRecord> recordsWithoutLimit = await _client.GetAuditLedgerAsync(_localProfile, "token");
        Assert.Single(recordsWithoutLimit);

        _httpHandler.SetResponse("/api/compliance/audit-ledger", HttpStatusCode.OK, "null");
        IReadOnlyList<AuditRecord> empty = await _client.GetAuditLedgerAsync(_localProfile, "token");
        Assert.Empty(empty);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetAuditLedgerAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetAuditLedgerAsync(_localProfile, ""));

        _httpHandler.SetResponse("/api/compliance/audit-ledger", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetAuditLedgerAsync(_localProfile, "token"));
    }

    /// <summary>
    /// Verifies AnalyzeGenomicsAsync nominal response, null deserialization fallback, and error handling.
    /// </summary>
    [Fact]
    public async Task AnalyzeGenomicsAsync_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests AnalyzeGenomicsAsync endpoint communication against /api/genomics/analyze.
        // WHY:  Ensures patient genetic SNP screening and kinetic modifier computation operates cleanly.
        // HOW:  Mocks /api/genomics/analyze with JSON, asserts populated profile and exception handling.
        _httpHandler.SetResponse(
            "/api/genomics/analyze",
            HttpStatusCode.OK,
            """
            {
                "patientId": "pt-42",
                "regimen": "FemaleToMale",
                "observedMarkers": [],
                "alerts": [],
                "hasCriticalAlerts": false,
                "thrombosisRiskIndex": 1.0,
                "overallClearanceMultiplier": 1.0,
                "cpGMethylationMandatory": false,
                "calculatedAtUtc": "2026-11-04T00:00:00Z"
            }
            """);

        GenomicAnalysisRequest req = new()
        {
            PatientId = "pt-42",
            MarkerIds = ["F5-LEIDEN"],
            Regimen = TherapeuticRegimen.FemaleToMale
        };

        PatientGenomicProfile profile = await _client.AnalyzeGenomicsAsync(_localProfile, "token", req);
        Assert.NotNull(profile);
        Assert.Equal("pt-42", profile.PatientId);
        Assert.Equal(TherapeuticRegimen.FemaleToMale, profile.Regimen);

        _httpHandler.SetResponse("/api/genomics/analyze", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.AnalyzeGenomicsAsync(_localProfile, "token", req));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.AnalyzeGenomicsAsync(null!, "token", req));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.AnalyzeGenomicsAsync(_localProfile, "", req));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.AnalyzeGenomicsAsync(_localProfile, "token", null!));

        _httpHandler.SetResponse("/api/genomics/analyze", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.AnalyzeGenomicsAsync(_localProfile, "token", req));
    }

    /// <summary>
    /// Verifies VerifyAuditLedgerAsync nominal response, null deserialization fallback, and error handling.
    /// </summary>
    [Fact]
    public async Task VerifyAuditLedgerAsync_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests VerifyAuditLedgerAsync endpoint communication against /api/compliance/verify-ledger.
        // WHY:  Ensures cryptographic ledger integrity verification operates cleanly.
        // HOW:  Mocks /api/compliance/verify-ledger with JSON, asserts populated result and exception handling.
        _httpHandler.SetResponse(
            "/api/compliance/verify-ledger",
            HttpStatusCode.OK,
            """
            {
                "isValid": true,
                "totalRecordsVerified": 10,
                "firstViolationSequence": null,
                "message": "Chain valid",
                "verifiedAtUtc": "2026-11-04T00:00:00Z"
            }
            """);

        AuditLedgerVerificationResult result = await _client.VerifyAuditLedgerAsync(_localProfile, "token");
        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Equal(10, result.TotalRecordsVerified);
        Assert.Equal("Chain valid", result.Message);

        _httpHandler.SetResponse("/api/compliance/verify-ledger", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.VerifyAuditLedgerAsync(_localProfile, "token"));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.VerifyAuditLedgerAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.VerifyAuditLedgerAsync(_localProfile, ""));

        _httpHandler.SetResponse("/api/compliance/verify-ledger", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.VerifyAuditLedgerAsync(_localProfile, "token"));
    }

    /// <summary>
    /// Verifies AnalyzeGenomicSequenceAsync nominal response, null deserialization fallback, and error handling.
    /// </summary>
    [Fact]
    public async Task AnalyzeGenomicSequenceAsync_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests AnalyzeGenomicSequenceAsync endpoint communication against /api/genomics/analyze-sequence.
        // WHY:  Ensures raw FASTA sequence scanning operates cleanly with full error handling.
        // HOW:  Mocks /api/genomics/analyze-sequence with JSON, asserts returned genomic profile and parameter validations.
        _httpHandler.SetResponse(
            "/api/genomics/analyze-sequence",
            HttpStatusCode.OK,
            """
            {
                "patientId": "pt-seq-42",
                "regimen": "FemaleToMale",
                "observedMarkers": [],
                "alerts": [],
                "thrombosisRiskIndex": 1.0,
                "overallClearanceMultiplier": 1.0,
                "cpGMethylationMandatory": false,
                "calculatedAtUtc": "2026-11-04T00:00:00Z"
            }
            """);

        SequenceAnalysisRequest req = new()
        {
            PatientId = "pt-seq-42",
            Sequence = "ACGTACGT",
            Regimen = TherapeuticRegimen.FemaleToMale
        };

        PatientGenomicProfile profile = await _client.AnalyzeGenomicSequenceAsync(_localProfile, "token", req);
        Assert.NotNull(profile);
        Assert.Equal("pt-seq-42", profile.PatientId);
        Assert.Equal(TherapeuticRegimen.FemaleToMale, profile.Regimen);

        _httpHandler.SetResponse("/api/genomics/analyze-sequence", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.AnalyzeGenomicSequenceAsync(_localProfile, "token", req));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.AnalyzeGenomicSequenceAsync(null!, "token", req));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.AnalyzeGenomicSequenceAsync(_localProfile, "", req));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.AnalyzeGenomicSequenceAsync(_localProfile, "token", null!));

        _httpHandler.SetResponse("/api/genomics/analyze-sequence", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.AnalyzeGenomicSequenceAsync(_localProfile, "token", req));
    }

    /// <summary>
    /// Verifies QueryTrajectoryAnalyticsAsync nominal response, null deserialization fallback, and error handling.
    /// </summary>
    [Fact]
    public async Task QueryTrajectoryAnalyticsAsync_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests QueryTrajectoryAnalyticsAsync endpoint communication against /api/trajectories/analytics/query.
        // WHY:  Ensures analytical query execution against DuckDB returns structured tabular results.
        // HOW:  Mocks /api/trajectories/analytics/query with JSON, asserts row/column counts and parameter validation.
        _httpHandler.SetResponse(
            "/api/trajectories/analytics/query",
            HttpStatusCode.OK,
            """
            {
                "datasetName": "test-dataset",
                "columns": ["id", "val"],
                "rows": [["1", "alpha"], ["2", "beta"]],
                "totalRows": 2,
                "elapsedMilliseconds": 14.2
            }
            """);

        TrajectoryAnalyticsApiRequest req = new("test-dataset", "SELECT * FROM atoms");

        TrajectoryAnalyticsApiResponse response = await _client.QueryTrajectoryAnalyticsAsync(_localProfile, "token", req);
        Assert.NotNull(response);
        Assert.Equal("test-dataset", response.DatasetName);
        Assert.Equal(2, response.TotalRows);
        Assert.Equal(2, response.Columns.Count);
        Assert.Equal(2, response.Rows.Count);

        _httpHandler.SetResponse("/api/trajectories/analytics/query", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.QueryTrajectoryAnalyticsAsync(_localProfile, "token", req));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.QueryTrajectoryAnalyticsAsync(null!, "token", req));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.QueryTrajectoryAnalyticsAsync(_localProfile, "", req));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.QueryTrajectoryAnalyticsAsync(_localProfile, "token", null!));

        _httpHandler.SetResponse("/api/trajectories/analytics/query", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.QueryTrajectoryAnalyticsAsync(_localProfile, "token", req));
    }

    /// <summary>
    /// Verifies PeerKeys list, register, and revoke nominal responses, null fallback, and error paths.
    /// </summary>
    [Fact]
    public async Task PeerKeys_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests ListPeerKeysAsync, RegisterPeerKeyAsync, and RevokePeerKeyAsync endpoint communications against /api/peer-keys.
        // WHY:  Guarantees cryptographic peer laboratory identity registration and revocation functions cleanly.
        // HOW:  Mocks GET, POST, and POST /revoke routes with JSON, asserting records and parameter validation.
        _httpHandler.SetResponse(
            "/api/peer-keys",
            HttpStatusCode.OK,
            """
            {
                "localNode": {
                    "keyId": "k-local",
                    "algorithm": "Ed25519",
                    "publicKeyBase64": "pubKeyNode=="
                },
                "peers": [
                    {
                        "keyId": "k-peer-1",
                        "label": "Lab 1",
                        "algorithm": "Ed25519",
                        "publicKeyBase64": "pubKeyPeer==",
                        "registeredBy": "admin",
                        "registeredAtUtc": "2026-11-04T00:00:00Z",
                        "revokedBy": null,
                        "revokedAtUtc": null
                    }
                ]
            }
            """);

        PeerKeyListResponse list = await _client.ListPeerKeysAsync(_localProfile, "token");
        Assert.NotNull(list);
        Assert.Equal(KeyAlgorithm.Ed25519, list.LocalNode.Algorithm);
        Assert.Single(list.Peers);
        Assert.Equal("k-peer-1", list.Peers[0].KeyId);

        _httpHandler.SetResponse("/api/peer-keys", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ListPeerKeysAsync(_localProfile, "token"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ListPeerKeysAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ListPeerKeysAsync(_localProfile, ""));

        _httpHandler.SetResponse("/api/peer-keys", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ListPeerKeysAsync(_localProfile, "token"));

        // Register
        RegisterPeerKeyRequest regReq = new()
        {
            Label = "Lab 1",
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = "pubKeyPeer=="
        };

        _httpHandler.SetResponse(
            "/api/peer-keys",
            HttpStatusCode.OK,
            """
            {
                "keyId": "k-peer-1",
                "label": "Lab 1",
                "algorithm": "Ed25519",
                "publicKeyBase64": "pubKeyPeer==",
                "registeredBy": "admin",
                "registeredAtUtc": "2026-11-04T00:00:00Z",
                "revokedBy": null,
                "revokedAtUtc": null
            }
            """);

        PeerKeyRecord record = await _client.RegisterPeerKeyAsync(_localProfile, "token", regReq);
        Assert.NotNull(record);
        Assert.Equal("k-peer-1", record.KeyId);
        Assert.True(record.IsActive);

        _httpHandler.SetResponse("/api/peer-keys", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.RegisterPeerKeyAsync(_localProfile, "token", regReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.RegisterPeerKeyAsync(null!, "token", regReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.RegisterPeerKeyAsync(_localProfile, "", regReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.RegisterPeerKeyAsync(_localProfile, "token", null!));

        _httpHandler.SetResponse("/api/peer-keys", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.RegisterPeerKeyAsync(_localProfile, "token", regReq));

        // Revoke
        _httpHandler.SetResponse(
            "/api/peer-keys/k-peer-1/revoke",
            HttpStatusCode.OK,
            """{"message": "Peer key revoked."}""");

        await _client.RevokePeerKeyAsync(_localProfile, "token", "k-peer-1");

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.RevokePeerKeyAsync(null!, "token", "k-peer-1"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.RevokePeerKeyAsync(_localProfile, "", "k-peer-1"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.RevokePeerKeyAsync(_localProfile, "token", ""));

        _httpHandler.SetResponse("/api/peer-keys/k-peer-1/revoke", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.RevokePeerKeyAsync(_localProfile, "token", "k-peer-1"));
    }

    /// <summary>
    /// Verifies CreateSubjectGrantAsync, RevokeSubjectGrantAsync, ListSubjectGroupsAsync, CreateSubjectGroupAsync, DeleteSubjectGroupAsync, and ListShareKeysAsync.
    /// </summary>
    [Fact]
    public async Task SubjectAccessAndShareKeys_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests subject access grant creation/revocation, subject cohort group CRUD, and share key listing.
        // WHY:  Verifies REST endpoints for patient access oversight and single-use peering invitations.
        // HOW:  Mocks endpoints for success, null response, argument errors, and HTTP 500 error propagation.
        // 1. CreateSubjectGrantAsync
        GrantSubjectAccessRequest grantReq = new()
        {
            ResearcherUsername = "dr_watson",
            SubjectUsername = "pt1",
            AccessLevel = SubjectAccessLevel.GenomicAccess,
            Notes = "Clinical trial grant"
        };

        _httpHandler.SetResponse(
            "/api/subject-access/grants",
            HttpStatusCode.Created,
            """
            {
                "id": "grant-1",
                "researcherUsername": "dr_watson",
                "subjectUsername": "pt1",
                "accessLevel": 2,
                "grantedBy": "admin",
                "grantedAtUtc": "2026-11-04T00:00:00Z",
                "notes": "Clinical trial grant"
            }
            """);

        SubjectGrant createdGrant = await _client.CreateSubjectGrantAsync(_localProfile, "token", grantReq);
        Assert.NotNull(createdGrant);
        Assert.Equal("dr_watson", createdGrant.ResearcherUsername);
        Assert.Equal("pt1", createdGrant.SubjectUsername);

        _httpHandler.SetResponse("/api/subject-access/grants", HttpStatusCode.Created, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.CreateSubjectGrantAsync(_localProfile, "token", grantReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.CreateSubjectGrantAsync(null!, "token", grantReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.CreateSubjectGrantAsync(_localProfile, "", grantReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.CreateSubjectGrantAsync(_localProfile, "token", null!));

        _httpHandler.SetResponse("/api/subject-access/grants", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.CreateSubjectGrantAsync(_localProfile, "token", grantReq));

        // 2. RevokeSubjectGrantAsync
        _httpHandler.SetResponse(
            "/api/subject-access/grants/dr_watson/pt1",
            HttpStatusCode.OK,
            """{"message": "Grant revoked."}""");

        await _client.RevokeSubjectGrantAsync(_localProfile, "token", "dr_watson", "pt1");

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.RevokeSubjectGrantAsync(null!, "token", "dr_watson", "pt1"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.RevokeSubjectGrantAsync(_localProfile, "", "dr_watson", "pt1"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.RevokeSubjectGrantAsync(_localProfile, "token", "", "pt1"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.RevokeSubjectGrantAsync(_localProfile, "token", "dr_watson", ""));

        _httpHandler.SetResponse("/api/subject-access/grants/dr_watson/pt1", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.RevokeSubjectGrantAsync(_localProfile, "token", "dr_watson", "pt1"));

        // 3. ListSubjectGroupsAsync
        _httpHandler.SetResponse(
            "/api/subject-groups",
            HttpStatusCode.OK,
            """
            [
                {
                    "id": "grp-1",
                    "name": "Trial Cohort Alpha",
                    "description": "Alpha description",
                    "createdBy": "dr_watson",
                    "createdAtUtc": "2026-11-04T00:00:00Z",
                    "updatedAtUtc": "2026-11-04T00:00:00Z",
                    "memberCount": 2
                }
            ]
            """);

        IReadOnlyList<SubjectGroup> groups = await _client.ListSubjectGroupsAsync(_localProfile, "token");
        Assert.Single(groups);
        Assert.Equal("grp-1", groups[0].Id);

        _httpHandler.SetResponse("/api/subject-groups", HttpStatusCode.OK, "null");
        IReadOnlyList<SubjectGroup> nullGroups = await _client.ListSubjectGroupsAsync(_localProfile, "token");
        Assert.Empty(nullGroups);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ListSubjectGroupsAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ListSubjectGroupsAsync(_localProfile, ""));

        _httpHandler.SetResponse("/api/subject-groups", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ListSubjectGroupsAsync(_localProfile, "token"));

        // 4. CreateSubjectGroupAsync
        CreateSubjectGroupRequest grpReq = new()
        {
            Name = "Trial Cohort Alpha",
            Description = "Alpha description",
            SubjectUsernames = ["pt1", "pt2"]
        };

        _httpHandler.SetResponse(
            "/api/subject-groups",
            HttpStatusCode.Created,
            """
            {
                "id": "grp-1",
                "name": "Trial Cohort Alpha",
                "description": "Alpha description",
                "createdBy": "dr_watson",
                "createdAtUtc": "2026-11-04T00:00:00Z",
                "updatedAtUtc": "2026-11-04T00:00:00Z",
                "memberCount": 2
            }
            """);

        SubjectGroup createdGroup = await _client.CreateSubjectGroupAsync(_localProfile, "token", grpReq);
        Assert.NotNull(createdGroup);
        Assert.Equal("grp-1", createdGroup.Id);

        _httpHandler.SetResponse("/api/subject-groups", HttpStatusCode.Created, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.CreateSubjectGroupAsync(_localProfile, "token", grpReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.CreateSubjectGroupAsync(null!, "token", grpReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.CreateSubjectGroupAsync(_localProfile, "", grpReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.CreateSubjectGroupAsync(_localProfile, "token", null!));

        _httpHandler.SetResponse("/api/subject-groups", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.CreateSubjectGroupAsync(_localProfile, "token", grpReq));

        // 5. DeleteSubjectGroupAsync
        _httpHandler.SetResponse(
            "/api/subject-groups/grp-1",
            HttpStatusCode.OK,
            """{"message": "Group deleted."}""");

        await _client.DeleteSubjectGroupAsync(_localProfile, "token", "grp-1");

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.DeleteSubjectGroupAsync(null!, "token", "grp-1"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeleteSubjectGroupAsync(_localProfile, "", "grp-1"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeleteSubjectGroupAsync(_localProfile, "token", ""));

        _httpHandler.SetResponse("/api/subject-groups/grp-1", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.DeleteSubjectGroupAsync(_localProfile, "token", "grp-1"));

        // 6. ListShareKeysAsync
        _httpHandler.SetResponse(
            "/api/share-keys",
            HttpStatusCode.OK,
            """
            [
                {
                    "tokenId": "tok-1",
                    "keyToken": "nanoweaver_hrt_tok1",
                    "label": "Partner Lab Tokyo",
                    "createdAtUtc": "2026-11-04T00:00:00Z",
                    "expiresAtUtc": "2026-11-05T00:00:00Z",
                    "isConsumed": false,
                    "consumedAtUtc": null,
                    "remotePeerInfo": null,
                    "permissions": "FullCollaborator"
                }
            ]
            """);

        IReadOnlyList<ShareKeyRecord> shareKeys = await _client.ListShareKeysAsync(_localProfile, "token");
        Assert.Single(shareKeys);
        Assert.Equal("tok-1", shareKeys[0].TokenId);

        _httpHandler.SetResponse("/api/share-keys", HttpStatusCode.OK, "null");
        IReadOnlyList<ShareKeyRecord> nullKeys = await _client.ListShareKeysAsync(_localProfile, "token");
        Assert.Empty(nullKeys);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ListShareKeysAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ListShareKeysAsync(_localProfile, ""));

        _httpHandler.SetResponse("/api/share-keys", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ListShareKeysAsync(_localProfile, "token"));
    }

    /// <summary>
    /// Verifies ListGroupMembersAsync, AddGroupMemberAsync, RemoveGroupMemberAsync, ListGroupGrantsAsync, CreateGroupGrantAsync, RevokeGroupGrantAsync, CreateShareKeyAsync, and RedeemShareKeyAsync.
    /// </summary>
    [Fact]
    public async Task CohortMembershipAndShareKeyRedemption_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests cohort membership management, group grant authorization, share key creation, and unauthenticated peer key redemption.
        // WHY:  Verifies complete REST coverage for clinical trials, group access tiers, and peer federation.
        // HOW:  Mocks endpoints for 200/201, null payload responses, input guard clauses, and HTTP 500 error handling.
        // 1. ListGroupMembersAsync
        _httpHandler.SetResponse(
            "/api/subject-groups/grp-1/members",
            HttpStatusCode.OK,
            """["patient_alice", "patient_bob"]""");

        IReadOnlyList<string> members = await _client.ListGroupMembersAsync(_localProfile, "token", "grp-1");
        Assert.Equal(2, members.Count);
        Assert.Contains("patient_alice", members);

        _httpHandler.SetResponse("/api/subject-groups/grp-1/members", HttpStatusCode.OK, "null");
        IReadOnlyList<string> nullMembers = await _client.ListGroupMembersAsync(_localProfile, "token", "grp-1");
        Assert.Empty(nullMembers);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ListGroupMembersAsync(null!, "token", "grp-1"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ListGroupMembersAsync(_localProfile, "", "grp-1"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ListGroupMembersAsync(_localProfile, "token", ""));

        _httpHandler.SetResponse("/api/subject-groups/grp-1/members", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ListGroupMembersAsync(_localProfile, "token", "grp-1"));

        // 2. AddGroupMemberAsync
        AddGroupMemberRequest addMemberReq = new()
        {
            SubjectUsername = "patient_carol"
        };

        _httpHandler.SetResponse(
            "/api/subject-groups/grp-1/members",
            HttpStatusCode.Created,
            """{"message": "Enrolled"}""");

        await _client.AddGroupMemberAsync(_localProfile, "token", "grp-1", addMemberReq);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.AddGroupMemberAsync(null!, "token", "grp-1", addMemberReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.AddGroupMemberAsync(_localProfile, "", "grp-1", addMemberReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.AddGroupMemberAsync(_localProfile, "token", "", addMemberReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.AddGroupMemberAsync(_localProfile, "token", "grp-1", null!));

        _httpHandler.SetResponse("/api/subject-groups/grp-1/members", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.AddGroupMemberAsync(_localProfile, "token", "grp-1", addMemberReq));

        // 3. RemoveGroupMemberAsync
        _httpHandler.SetResponse(
            "/api/subject-groups/grp-1/members/patient_carol",
            HttpStatusCode.OK,
            """{"message": "Removed"}""");

        await _client.RemoveGroupMemberAsync(_localProfile, "token", "grp-1", "patient_carol");

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.RemoveGroupMemberAsync(null!, "token", "grp-1", "patient_carol"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.RemoveGroupMemberAsync(_localProfile, "", "grp-1", "patient_carol"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.RemoveGroupMemberAsync(_localProfile, "token", "", "patient_carol"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.RemoveGroupMemberAsync(_localProfile, "token", "grp-1", ""));

        _httpHandler.SetResponse("/api/subject-groups/grp-1/members/patient_carol", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.RemoveGroupMemberAsync(_localProfile, "token", "grp-1", "patient_carol"));

        // 4. ListGroupGrantsAsync
        _httpHandler.SetResponse(
            "/api/subject-groups/grp-1/grants",
            HttpStatusCode.OK,
            """
            [
                {
                    "id": "g-1",
                    "groupId": "grp-1",
                    "researcherUsername": "dr_watson",
                    "accessLevel": 2,
                    "grantedBy": "admin",
                    "grantedAtUtc": "2026-11-04T00:00:00Z"
                }
            ]
            """);

        IReadOnlyList<SubjectGroupGrant> groupGrants = await _client.ListGroupGrantsAsync(_localProfile, "token", "grp-1");
        Assert.Single(groupGrants);
        Assert.Equal("g-1", groupGrants[0].Id);

        _httpHandler.SetResponse("/api/subject-groups/grp-1/grants", HttpStatusCode.OK, "null");
        IReadOnlyList<SubjectGroupGrant> nullGrants = await _client.ListGroupGrantsAsync(_localProfile, "token", "grp-1");
        Assert.Empty(nullGrants);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ListGroupGrantsAsync(null!, "token", "grp-1"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ListGroupGrantsAsync(_localProfile, "", "grp-1"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ListGroupGrantsAsync(_localProfile, "token", ""));

        _httpHandler.SetResponse("/api/subject-groups/grp-1/grants", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ListGroupGrantsAsync(_localProfile, "token", "grp-1"));

        // 5. CreateGroupGrantAsync
        GrantGroupAccessRequest groupGrantReq = new()
        {
            ResearcherUsername = "dr_watson",
            AccessLevel = SubjectAccessLevel.FullReadAccess,
            Notes = "Clinical study grant"
        };

        _httpHandler.SetResponse(
            "/api/subject-groups/grp-1/grants",
            HttpStatusCode.Created,
            """
            {
                "id": "g-1",
                "groupId": "grp-1",
                "researcherUsername": "dr_watson",
                "accessLevel": 3,
                "grantedBy": "admin",
                "grantedAtUtc": "2026-11-04T00:00:00Z",
                "notes": "Clinical study grant"
            }
            """);

        SubjectGroupGrant createdGroupGrant = await _client.CreateGroupGrantAsync(_localProfile, "token", "grp-1", groupGrantReq);
        Assert.NotNull(createdGroupGrant);
        Assert.Equal(SubjectAccessLevel.FullReadAccess, createdGroupGrant.AccessLevel);

        _httpHandler.SetResponse("/api/subject-groups/grp-1/grants", HttpStatusCode.Created, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.CreateGroupGrantAsync(_localProfile, "token", "grp-1", groupGrantReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.CreateGroupGrantAsync(null!, "token", "grp-1", groupGrantReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.CreateGroupGrantAsync(_localProfile, "", "grp-1", groupGrantReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.CreateGroupGrantAsync(_localProfile, "token", "", groupGrantReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.CreateGroupGrantAsync(_localProfile, "token", "grp-1", null!));

        _httpHandler.SetResponse("/api/subject-groups/grp-1/grants", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.CreateGroupGrantAsync(_localProfile, "token", "grp-1", groupGrantReq));

        // 6. RevokeGroupGrantAsync
        _httpHandler.SetResponse(
            "/api/subject-groups/grp-1/grants/dr_watson",
            HttpStatusCode.OK,
            """{"message": "Grant revoked"}""");

        await _client.RevokeGroupGrantAsync(_localProfile, "token", "grp-1", "dr_watson");

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.RevokeGroupGrantAsync(null!, "token", "grp-1", "dr_watson"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.RevokeGroupGrantAsync(_localProfile, "", "grp-1", "dr_watson"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.RevokeGroupGrantAsync(_localProfile, "token", "", "dr_watson"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.RevokeGroupGrantAsync(_localProfile, "token", "grp-1", ""));

        _httpHandler.SetResponse("/api/subject-groups/grp-1/grants/dr_watson", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.RevokeGroupGrantAsync(_localProfile, "token", "grp-1", "dr_watson"));

        // 7. CreateShareKeyAsync
        CreateShareKeyRequest createKeyReq = new()
        {
            Label = "Partner Lab Tokyo",
            LifetimeHours = 24,
            Permissions = "FullCollaborator"
        };

        _httpHandler.SetResponse(
            "/api/share-keys/create",
            HttpStatusCode.OK,
            """
            {
                "tokenId": "tok-99",
                "keyToken": "nanoweaver_hrt_tok99",
                "label": "Partner Lab Tokyo",
                "createdAtUtc": "2026-11-04T00:00:00Z",
                "expiresAtUtc": "2026-11-05T00:00:00Z",
                "isConsumed": false,
                "consumedAtUtc": null,
                "remotePeerInfo": null,
                "permissions": "FullCollaborator"
            }
            """);

        ShareKeyRecord createdShareKey = await _client.CreateShareKeyAsync(_localProfile, "token", createKeyReq);
        Assert.NotNull(createdShareKey);
        Assert.Equal("tok-99", createdShareKey.TokenId);

        _httpHandler.SetResponse("/api/share-keys/create", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.CreateShareKeyAsync(_localProfile, "token", createKeyReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.CreateShareKeyAsync(null!, "token", createKeyReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.CreateShareKeyAsync(_localProfile, "", createKeyReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.CreateShareKeyAsync(_localProfile, "token", null!));

        _httpHandler.SetResponse("/api/share-keys/create", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.CreateShareKeyAsync(_localProfile, "token", createKeyReq));

        // 8. RedeemShareKeyAsync (unauthenticated)
        RedeemShareKeyRequest redeemReq = new()
        {
            Token = "nanoweaver_hrt_tok99"
        };

        _httpHandler.SetResponse(
            "/api/share-keys/redeem",
            HttpStatusCode.OK,
            """
            {
                "success": true,
                "message": "Token redeemed.",
                "record": null,
                "peerSessionToken": "jwt_token_redeemed"
            }
            """);

        ShareKeyConsumeResult redeemResult = await _client.RedeemShareKeyAsync(_localProfile, redeemReq);
        Assert.NotNull(redeemResult);
        Assert.True(redeemResult.Success);
        Assert.Equal("jwt_token_redeemed", redeemResult.PeerSessionToken);

        _httpHandler.SetResponse("/api/share-keys/redeem", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.RedeemShareKeyAsync(_localProfile, redeemReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.RedeemShareKeyAsync(null!, redeemReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.RedeemShareKeyAsync(_localProfile, null!));

        _httpHandler.SetResponse("/api/share-keys/redeem", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.RedeemShareKeyAsync(_localProfile, redeemReq));
    }

    /// <summary>
    /// Verifies extended client SDK endpoints for subject access, fabrication, stack telemetry, and hormone kinetics.
    /// </summary>
    [Fact]
    public async Task ExtendedClientSdkEndpoints_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests extended INanoWeaverNodeClient methods across nominal, null fallback, parameter guard, and error branches.
        // WHY:  Issue #124-#126 (P121-P123): Ensures 100% test coverage across all newly exposed REST client operations.
        // HOW:  Configures StubHttpMessageHandler routes with representative JSON and asserts responses and exceptions.

        // 1. GetSubjectGroupAsync
        _httpHandler.SetResponse(
            "/api/subject-groups/grp-alpha",
            HttpStatusCode.OK,
            """
            {
                "id": "grp-alpha",
                "name": "Alpha Cohort",
                "description": "Primary study cohort",
                "createdBy": "admin",
                "createdAtUtc": "2026-11-04T00:00:00Z"
            }
            """);

        SubjectGroup grp = await _client.GetSubjectGroupAsync(_localProfile, "token", "grp-alpha");
        Assert.NotNull(grp);
        Assert.Equal("grp-alpha", grp.Id);
        Assert.Equal("Alpha Cohort", grp.Name);

        _httpHandler.SetResponse("/api/subject-groups/grp-alpha", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.GetSubjectGroupAsync(_localProfile, "token", "grp-alpha"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetSubjectGroupAsync(null!, "token", "grp-alpha"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetSubjectGroupAsync(_localProfile, "", "grp-alpha"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetSubjectGroupAsync(_localProfile, "token", ""));

        _httpHandler.SetResponse("/api/subject-groups/grp-alpha", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetSubjectGroupAsync(_localProfile, "token", "grp-alpha"));

        // 2. UpdateSubjectGroupAsync
        UpdateSubjectGroupRequest updateGrpReq = new()
        {
            Name = "Alpha Cohort Renamed",
            Description = "Updated study cohort"
        };

        _httpHandler.SetResponse(
            "/api/subject-groups/grp-alpha",
            HttpStatusCode.OK,
            """
            {
                "id": "grp-alpha",
                "name": "Alpha Cohort Renamed",
                "description": "Updated study cohort",
                "createdBy": "admin",
                "createdAtUtc": "2026-11-04T00:00:00Z"
            }
            """);

        SubjectGroup updatedGrp = await _client.UpdateSubjectGroupAsync(_localProfile, "token", "grp-alpha", updateGrpReq);
        Assert.NotNull(updatedGrp);
        Assert.Equal("Alpha Cohort Renamed", updatedGrp.Name);

        _httpHandler.SetResponse("/api/subject-groups/grp-alpha", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.UpdateSubjectGroupAsync(_localProfile, "token", "grp-alpha", updateGrpReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.UpdateSubjectGroupAsync(null!, "token", "grp-alpha", updateGrpReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.UpdateSubjectGroupAsync(_localProfile, "", "grp-alpha", updateGrpReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.UpdateSubjectGroupAsync(_localProfile, "token", "", updateGrpReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.UpdateSubjectGroupAsync(_localProfile, "token", "grp-alpha", null!));

        _httpHandler.SetResponse("/api/subject-groups/grp-alpha", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.UpdateSubjectGroupAsync(_localProfile, "token", "grp-alpha", updateGrpReq));

        // 3. ListMySubjectsAsync
        _httpHandler.SetResponse(
            "/api/subject-access/my-subjects",
            HttpStatusCode.OK,
            """
            [
                {
                    "subjectUsername": "patient_01",
                    "effectiveLevel": "FullReadAccess"
                }
            ]
            """);

        IReadOnlyList<AccessibleSubjectItem> mySubjects = await _client.ListMySubjectsAsync(_localProfile, "token");
        Assert.Single(mySubjects);
        Assert.Equal("patient_01", mySubjects[0].SubjectUsername);
        Assert.Equal(SubjectAccessLevel.FullReadAccess, mySubjects[0].EffectiveLevel);

        _httpHandler.SetResponse("/api/subject-access/my-subjects", HttpStatusCode.OK, "null");
        IReadOnlyList<AccessibleSubjectItem> emptyMySubjects = await _client.ListMySubjectsAsync(_localProfile, "token");
        Assert.Empty(emptyMySubjects);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ListMySubjectsAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ListMySubjectsAsync(_localProfile, ""));

        _httpHandler.SetResponse("/api/subject-access/my-subjects", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ListMySubjectsAsync(_localProfile, "token"));

        // 4. GetFabricationPresetsAsync
        DnaOrigamiSample sampleSample = new FabricationService().GetPresetSamples()[0];
        _httpHandler.SetResponse(
            "/api/fabrication/presets",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new[] { sampleSample }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        IReadOnlyList<DnaOrigamiSample> presets = await _client.GetFabricationPresetsAsync(_localProfile, "token");
        Assert.Single(presets);
        Assert.Equal(sampleSample.Id, presets[0].Id);

        _httpHandler.SetResponse("/api/fabrication/presets", HttpStatusCode.OK, "null");
        IReadOnlyList<DnaOrigamiSample> emptyPresets = await _client.GetFabricationPresetsAsync(_localProfile, "token");
        Assert.Empty(emptyPresets);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetFabricationPresetsAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetFabricationPresetsAsync(_localProfile, ""));

        _httpHandler.SetResponse("/api/fabrication/presets", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetFabricationPresetsAsync(_localProfile, "token"));

        // 5. GenerateFabricationProtocolAsync
        FabricationGenerateRequest fabReq = new()
        {
            SequenceOrSampleId = "6hb-nanotube",
            Regimen = TherapeuticRegimen.MaleToFemale,
            Geometry = "Hexagonal 6-Helix Bundle Nanotube (6HB)",
            TargetKd = 4.2
        };

        FabricationService fabService = new();
        FabricationPackage samplePkg = fabService.GenerateFabricationPackage(fabService.GetPresetSamples()[0].Id, TherapeuticRegimen.MaleToFemale, "Hexagonal 6-Helix Bundle Nanotube (6HB)", 4.2);
        _httpHandler.SetResponse(
            "/api/fabrication/generate",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(samplePkg, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        FabricationPackage fabPkg = await _client.GenerateFabricationProtocolAsync(_localProfile, "token", fabReq);
        Assert.NotNull(fabPkg);
        Assert.Equal(samplePkg.PackageId, fabPkg.PackageId);

        _httpHandler.SetResponse("/api/fabrication/generate", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.GenerateFabricationProtocolAsync(_localProfile, "token", fabReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GenerateFabricationProtocolAsync(null!, "token", fabReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GenerateFabricationProtocolAsync(_localProfile, "", fabReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GenerateFabricationProtocolAsync(_localProfile, "token", null!));

        _httpHandler.SetResponse("/api/fabrication/generate", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GenerateFabricationProtocolAsync(_localProfile, "token", fabReq));

        // 6. GetGenomicPresetsAsync
        PatientGenomicProfile sampleGenProfile = new PharmacogenomicsService().AnalyzeProfile("preset_thrombophilia", Array.Empty<string>(), TherapeuticRegimen.MaleToFemale);
        _httpHandler.SetResponse(
            "/api/genomics/presets",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new[] { sampleGenProfile }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        IReadOnlyList<PatientGenomicProfile> genPresets = await _client.GetGenomicPresetsAsync(_localProfile, "token");
        Assert.Single(genPresets);
        Assert.Equal("preset_thrombophilia", genPresets[0].PatientId);

        _httpHandler.SetResponse("/api/genomics/presets", HttpStatusCode.OK, "null");
        IReadOnlyList<PatientGenomicProfile> emptyGenPresets = await _client.GetGenomicPresetsAsync(_localProfile, "token");
        Assert.Empty(emptyGenPresets);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetGenomicPresetsAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetGenomicPresetsAsync(_localProfile, ""));

        _httpHandler.SetResponse("/api/genomics/presets", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetGenomicPresetsAsync(_localProfile, "token"));

        // 7. GetGenomicMarkersAsync
        GeneticMarker sampleMarker = GeneticMarker.CreateExample();
        _httpHandler.SetResponse(
            "/api/genomics/markers",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new[] { sampleMarker }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        IReadOnlyList<GeneticMarker> markers = await _client.GetGenomicMarkersAsync(_localProfile, "token");
        Assert.Single(markers);
        Assert.Equal(sampleMarker.MarkerId, markers[0].MarkerId);

        _httpHandler.SetResponse("/api/genomics/markers", HttpStatusCode.OK, "null");
        IReadOnlyList<GeneticMarker> emptyMarkers = await _client.GetGenomicMarkersAsync(_localProfile, "token");
        Assert.Empty(emptyMarkers);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetGenomicMarkersAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetGenomicMarkersAsync(_localProfile, ""));

        _httpHandler.SetResponse("/api/genomics/markers", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetGenomicMarkersAsync(_localProfile, "token"));

        // 8. GetStackServicesAsync
        StackServiceInfo sampleSvc = new()
        {
            ServiceId = "valkey-cache",
            DisplayName = "Valkey In-Memory Cache",
            RoleDescription = "Cache",
            EndpointOrLocation = "valkey://127.0.0.1:6379",
            Health = ServiceHealthState.Healthy,
            StatusMessage = "Operational",
            TechnologyStack = "Valkey"
        };
        _httpHandler.SetResponse(
            "/api/services",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new[] { sampleSvc }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        IReadOnlyList<StackServiceInfo> svcs = await _client.GetStackServicesAsync(_localProfile, "token");
        Assert.Single(svcs);
        Assert.Equal(sampleSvc.ServiceId, svcs[0].ServiceId);

        _httpHandler.SetResponse("/api/services", HttpStatusCode.OK, "null");
        IReadOnlyList<StackServiceInfo> emptySvcs = await _client.GetStackServicesAsync(_localProfile, "token");
        Assert.Empty(emptySvcs);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetStackServicesAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetStackServicesAsync(_localProfile, ""));

        _httpHandler.SetResponse("/api/services", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetStackServicesAsync(_localProfile, "token"));

        // 9. PingStackServiceAsync
        _httpHandler.SetResponse(
            $"/api/services/ping/{sampleSvc.ServiceId}",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(sampleSvc, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        StackServiceInfo pingResult = await _client.PingStackServiceAsync(_localProfile, "token", sampleSvc.ServiceId);
        Assert.NotNull(pingResult);
        Assert.Equal(sampleSvc.ServiceId, pingResult.ServiceId);

        _httpHandler.SetResponse($"/api/services/ping/{sampleSvc.ServiceId}", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.PingStackServiceAsync(_localProfile, "token", sampleSvc.ServiceId));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.PingStackServiceAsync(null!, "token", sampleSvc.ServiceId));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.PingStackServiceAsync(_localProfile, "", sampleSvc.ServiceId));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.PingStackServiceAsync(_localProfile, "token", ""));

        _httpHandler.SetResponse($"/api/services/ping/{sampleSvc.ServiceId}", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.PingStackServiceAsync(_localProfile, "token", sampleSvc.ServiceId));

        // 10. GetEndocrineKineticsAsync
        EndocrineStateEngine stateEngine = new();
        IReadOnlyList<HormoneCurvePoint> sampleKinetics = stateEngine.SimulateKinetics();
        _httpHandler.SetResponse(
            "/api/endocrine/kinetics",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(sampleKinetics, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        IReadOnlyList<HormoneCurvePoint> kinetics = await _client.GetEndocrineKineticsAsync(_localProfile, "token");
        Assert.NotEmpty(kinetics);
        Assert.Equal(sampleKinetics[0].Hour, kinetics[0].Hour);
        Assert.Equal(sampleKinetics[0].EstradiolPmolL, kinetics[0].EstradiolPmolL);

        _httpHandler.SetResponse("/api/endocrine/kinetics", HttpStatusCode.OK, "null");
        IReadOnlyList<HormoneCurvePoint> emptyKinetics = await _client.GetEndocrineKineticsAsync(_localProfile, "token");
        Assert.Empty(emptyKinetics);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetEndocrineKineticsAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetEndocrineKineticsAsync(_localProfile, ""));

        _httpHandler.SetResponse("/api/endocrine/kinetics", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetEndocrineKineticsAsync(_localProfile, "token"));

        // 11. ToggleEndocrineSwitchAsync
        ToggleSwitchRequest toggleReq = new() { TargetState = ToggleState.EstrogenicDominant };
        EndocrineTargetProfile sampleTargetProf = stateEngine.GetActiveProfile();

        _httpHandler.SetResponse(
            "/api/endocrine/toggle",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(sampleTargetProf, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        EndocrineTargetProfile targetProf = await _client.ToggleEndocrineSwitchAsync(_localProfile, "token", toggleReq);
        Assert.NotNull(targetProf);
        Assert.Equal(sampleTargetProf.TargetState, targetProf.TargetState);
        Assert.Equal(sampleTargetProf.TargetEstradiolPmolL, targetProf.TargetEstradiolPmolL);

        _httpHandler.SetResponse("/api/endocrine/toggle", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.ToggleEndocrineSwitchAsync(_localProfile, "token", toggleReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ToggleEndocrineSwitchAsync(null!, "token", toggleReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ToggleEndocrineSwitchAsync(_localProfile, "", toggleReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ToggleEndocrineSwitchAsync(_localProfile, "token", null!));

        _httpHandler.SetResponse("/api/endocrine/toggle", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ToggleEndocrineSwitchAsync(_localProfile, "token", toggleReq));
    }

    /// <summary>
    /// Verifies nominal responses and fault conditions for User Management, Profile, Address, and Contact client SDK endpoints.
    /// </summary>
    /// <returns>A task representing asynchronous test completion.</returns>
    [Fact]
    public async Task UserManagementAndPatientDirectoryEndpoints_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Test nominal and fault execution branches for user accounts, demographic profiles, addresses, and contacts.
        // WHY:  Issue #129 (P126): Ensures client SDK provides comprehensive coverage for administrative and self-service directory routes.
        // HOW:  Mocks HTTP responses with JSON payloads, tests success, null, argument validation, and HTTP error responses.
        UserAccount sampleUser = new UserAccount().GetExamples();
        CreateUserRequest sampleCreateReq = new CreateUserRequest().GetExamples();
        UserProfile sampleProfile = new UserProfile().GetExamples();
        PatientAddress sampleAddress = new PatientAddress().GetExamples();
        PatientContact sampleContact = new PatientContact().GetExamples();

        // 1. ListUsersAsync
        _httpHandler.SetResponse(
            "/api/users",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new[] { sampleUser }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        IReadOnlyList<UserAccount> users = await _client.ListUsersAsync(_localProfile, "token");
        Assert.Single(users);
        Assert.Equal(sampleUser.Username, users[0].Username);

        _httpHandler.SetResponse("/api/users", HttpStatusCode.OK, "null");
        IReadOnlyList<UserAccount> emptyUsers = await _client.ListUsersAsync(_localProfile, "token");
        Assert.Empty(emptyUsers);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ListUsersAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ListUsersAsync(_localProfile, ""));
        _httpHandler.SetResponse("/api/users", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ListUsersAsync(_localProfile, "token"));

        // 2. CreateUserAsync
        _httpHandler.SetResponse(
            "/api/users",
            HttpStatusCode.Created,
            JsonSerializer.Serialize(sampleUser, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        UserAccount createdUser = await _client.CreateUserAsync(_localProfile, "token", sampleCreateReq);
        Assert.Equal(sampleUser.Username, createdUser.Username);

        _httpHandler.SetResponse("/api/users", HttpStatusCode.Created, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.CreateUserAsync(_localProfile, "token", sampleCreateReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.CreateUserAsync(null!, "token", sampleCreateReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.CreateUserAsync(_localProfile, "", sampleCreateReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.CreateUserAsync(_localProfile, "token", null!));
        _httpHandler.SetResponse("/api/users", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.CreateUserAsync(_localProfile, "token", sampleCreateReq));

        // 3. DeleteUserAsync
        _httpHandler.SetResponse($"/api/users/{sampleUser.Username}", HttpStatusCode.OK, "{}");
        await _client.DeleteUserAsync(_localProfile, "token", sampleUser.Username);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.DeleteUserAsync(null!, "token", sampleUser.Username));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeleteUserAsync(_localProfile, "", sampleUser.Username));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeleteUserAsync(_localProfile, "token", ""));
        _httpHandler.SetResponse($"/api/users/{sampleUser.Username}", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.DeleteUserAsync(_localProfile, "token", sampleUser.Username));

        // 4. GetUserProfileAsync
        _httpHandler.SetResponse(
            $"/api/users/{sampleUser.Username}/profile",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(sampleProfile, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        UserProfile profile = await _client.GetUserProfileAsync(_localProfile, "token", sampleUser.Username);
        Assert.Equal(sampleProfile.Username, profile.Username);

        _httpHandler.SetResponse($"/api/users/{sampleUser.Username}/profile", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.GetUserProfileAsync(_localProfile, "token", sampleUser.Username));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetUserProfileAsync(null!, "token", sampleUser.Username));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetUserProfileAsync(_localProfile, "", sampleUser.Username));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetUserProfileAsync(_localProfile, "token", ""));
        _httpHandler.SetResponse($"/api/users/{sampleUser.Username}/profile", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetUserProfileAsync(_localProfile, "token", sampleUser.Username));

        // 5. UpdateUserProfileAsync
        _httpHandler.SetResponse(
            $"/api/users/{sampleUser.Username}/profile",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(sampleProfile, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        UserProfile updatedProfile = await _client.UpdateUserProfileAsync(_localProfile, "token", sampleUser.Username, sampleProfile);
        Assert.Equal(sampleProfile.Username, updatedProfile.Username);

        _httpHandler.SetResponse($"/api/users/{sampleUser.Username}/profile", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.UpdateUserProfileAsync(_localProfile, "token", sampleUser.Username, sampleProfile));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.UpdateUserProfileAsync(null!, "token", sampleUser.Username, sampleProfile));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.UpdateUserProfileAsync(_localProfile, "", sampleUser.Username, sampleProfile));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.UpdateUserProfileAsync(_localProfile, "token", "", sampleProfile));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.UpdateUserProfileAsync(_localProfile, "token", sampleUser.Username, null!));
        _httpHandler.SetResponse($"/api/users/{sampleUser.Username}/profile", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.UpdateUserProfileAsync(_localProfile, "token", sampleUser.Username, sampleProfile));

        // 6. ListUserAddressesAsync
        _httpHandler.SetResponse(
            $"/api/users/{sampleUser.Username}/addresses",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new[] { sampleAddress }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        IReadOnlyList<PatientAddress> addresses = await _client.ListUserAddressesAsync(_localProfile, "token", sampleUser.Username);
        Assert.Single(addresses);
        Assert.Equal(sampleAddress.Id, addresses[0].Id);

        _httpHandler.SetResponse($"/api/users/{sampleUser.Username}/addresses", HttpStatusCode.OK, "null");
        IReadOnlyList<PatientAddress> emptyAddresses = await _client.ListUserAddressesAsync(_localProfile, "token", sampleUser.Username);
        Assert.Empty(emptyAddresses);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ListUserAddressesAsync(null!, "token", sampleUser.Username));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ListUserAddressesAsync(_localProfile, "", sampleUser.Username));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ListUserAddressesAsync(_localProfile, "token", ""));
        _httpHandler.SetResponse($"/api/users/{sampleUser.Username}/addresses", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ListUserAddressesAsync(_localProfile, "token", sampleUser.Username));

        // 7. AddUserAddressAsync
        _httpHandler.SetResponse(
            $"/api/users/{sampleUser.Username}/addresses",
            HttpStatusCode.Created,
            JsonSerializer.Serialize(sampleAddress, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        PatientAddress createdAddress = await _client.AddUserAddressAsync(_localProfile, "token", sampleUser.Username, sampleAddress);
        Assert.Equal(sampleAddress.Id, createdAddress.Id);

        _httpHandler.SetResponse($"/api/users/{sampleUser.Username}/addresses", HttpStatusCode.Created, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.AddUserAddressAsync(_localProfile, "token", sampleUser.Username, sampleAddress));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.AddUserAddressAsync(null!, "token", sampleUser.Username, sampleAddress));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.AddUserAddressAsync(_localProfile, "", sampleUser.Username, sampleAddress));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.AddUserAddressAsync(_localProfile, "token", "", sampleAddress));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.AddUserAddressAsync(_localProfile, "token", sampleUser.Username, null!));
        _httpHandler.SetResponse($"/api/users/{sampleUser.Username}/addresses", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.AddUserAddressAsync(_localProfile, "token", sampleUser.Username, sampleAddress));

        // 8. DeleteUserAddressAsync
        _httpHandler.SetResponse($"/api/users/{sampleUser.Username}/addresses/{sampleAddress.Id}", HttpStatusCode.OK, "{}");
        await _client.DeleteUserAddressAsync(_localProfile, "token", sampleUser.Username, sampleAddress.Id);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.DeleteUserAddressAsync(null!, "token", sampleUser.Username, sampleAddress.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeleteUserAddressAsync(_localProfile, "", sampleUser.Username, sampleAddress.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeleteUserAddressAsync(_localProfile, "token", "", sampleAddress.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeleteUserAddressAsync(_localProfile, "token", sampleUser.Username, ""));
        _httpHandler.SetResponse($"/api/users/{sampleUser.Username}/addresses/{sampleAddress.Id}", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.DeleteUserAddressAsync(_localProfile, "token", sampleUser.Username, sampleAddress.Id));

        // 9. ListUserContactsAsync
        _httpHandler.SetResponse(
            $"/api/users/{sampleUser.Username}/contacts",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new[] { sampleContact }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        IReadOnlyList<PatientContact> contacts = await _client.ListUserContactsAsync(_localProfile, "token", sampleUser.Username);
        Assert.Single(contacts);
        Assert.Equal(sampleContact.Id, contacts[0].Id);

        _httpHandler.SetResponse($"/api/users/{sampleUser.Username}/contacts", HttpStatusCode.OK, "null");
        IReadOnlyList<PatientContact> emptyContacts = await _client.ListUserContactsAsync(_localProfile, "token", sampleUser.Username);
        Assert.Empty(emptyContacts);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ListUserContactsAsync(null!, "token", sampleUser.Username));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ListUserContactsAsync(_localProfile, "", sampleUser.Username));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ListUserContactsAsync(_localProfile, "token", ""));
        _httpHandler.SetResponse($"/api/users/{sampleUser.Username}/contacts", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ListUserContactsAsync(_localProfile, "token", sampleUser.Username));

        // 10. AddUserContactAsync
        _httpHandler.SetResponse(
            $"/api/users/{sampleUser.Username}/contacts",
            HttpStatusCode.Created,
            JsonSerializer.Serialize(sampleContact, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        PatientContact createdContact = await _client.AddUserContactAsync(_localProfile, "token", sampleUser.Username, sampleContact);
        Assert.Equal(sampleContact.Id, createdContact.Id);

        _httpHandler.SetResponse($"/api/users/{sampleUser.Username}/contacts", HttpStatusCode.Created, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.AddUserContactAsync(_localProfile, "token", sampleUser.Username, sampleContact));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.AddUserContactAsync(null!, "token", sampleUser.Username, sampleContact));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.AddUserContactAsync(_localProfile, "", sampleUser.Username, sampleContact));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.AddUserContactAsync(_localProfile, "token", "", sampleContact));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.AddUserContactAsync(_localProfile, "token", sampleUser.Username, null!));
        _httpHandler.SetResponse($"/api/users/{sampleUser.Username}/contacts", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.AddUserContactAsync(_localProfile, "token", sampleUser.Username, sampleContact));

        // 11. DeleteUserContactAsync
        _httpHandler.SetResponse($"/api/users/{sampleUser.Username}/contacts/{sampleContact.Id}", HttpStatusCode.OK, "{}");
        await _client.DeleteUserContactAsync(_localProfile, "token", sampleUser.Username, sampleContact.Id);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.DeleteUserContactAsync(null!, "token", sampleUser.Username, sampleContact.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeleteUserContactAsync(_localProfile, "", sampleUser.Username, sampleContact.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeleteUserContactAsync(_localProfile, "token", "", sampleContact.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeleteUserContactAsync(_localProfile, "token", sampleUser.Username, ""));
        _httpHandler.SetResponse($"/api/users/{sampleUser.Username}/contacts/{sampleContact.Id}", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.DeleteUserContactAsync(_localProfile, "token", sampleUser.Username, sampleContact.Id));
    }

    /// <summary>
    /// Verifies nominal responses and fault conditions for Patient Digital DNA, Import Archives, and Clinical Results client SDK endpoints.
    /// </summary>
    /// <returns>A task representing asynchronous test completion.</returns>
    [Fact]
    public async Task PatientClinicalDataEndpoints_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests nominal and fault execution branches for digitized DNA blueprints, import archives, and clinical assay results.
        // WHY:  Issue #130 (P127): Guarantees client REST SDK provides full endpoint coverage for patient clinical records.
        // HOW:  Mocks HTTP responses with JSON payloads, tests success, null handling, argument validation, and HTTP error responses.
        PatientDigitalDnaRecord sampleDna = new PatientDigitalDnaRecord().GetExamples();
        PatientDnaImportRecord sampleImport = new PatientDnaImportRecord().GetExamples();
        CreatePatientImportRequest sampleImportReq = new()
        {
            FileName = "patient_scaffold_variant.fasta",
            SequenceOrData = ">sample_sequence\nATGCGATCGATCGATCGATCGATCGATCGATC"
        };
        PatientResultRecord sampleResult = new PatientResultRecord().GetExamples();
        CreatePatientResultRequest sampleResultReq = new()
        {
            ResultType = "EndocrineKinetics",
            Title = "Endocrine Steady-State Target Kinetics",
            Summary = "Target setpoints calibrated for Anterior Pituitary Gonadotrope."
        };
        string username = "patient1";

        // 1. GetPatientDigitalDnaRecordAsync
        _httpHandler.SetResponse(
            $"/api/patients/{username}/dna/{sampleDna.Id}",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(sampleDna, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        PatientDigitalDnaRecord fetchedDna = await _client.GetPatientDigitalDnaRecordAsync(_localProfile, "token", username, sampleDna.Id);
        Assert.Equal(sampleDna.Id, fetchedDna.Id);

        _httpHandler.SetResponse($"/api/patients/{username}/dna/{sampleDna.Id}", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.GetPatientDigitalDnaRecordAsync(_localProfile, "token", username, sampleDna.Id));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetPatientDigitalDnaRecordAsync(null!, "token", username, sampleDna.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetPatientDigitalDnaRecordAsync(_localProfile, "", username, sampleDna.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetPatientDigitalDnaRecordAsync(_localProfile, "token", "", sampleDna.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetPatientDigitalDnaRecordAsync(_localProfile, "token", username, ""));
        _httpHandler.SetResponse($"/api/patients/{username}/dna/{sampleDna.Id}", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetPatientDigitalDnaRecordAsync(_localProfile, "token", username, sampleDna.Id));

        // 2. DeletePatientDigitalDnaAsync
        _httpHandler.SetResponse($"/api/patients/{username}/dna/{sampleDna.Id}", HttpStatusCode.OK, "{}");
        await _client.DeletePatientDigitalDnaAsync(_localProfile, "token", username, sampleDna.Id);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.DeletePatientDigitalDnaAsync(null!, "token", username, sampleDna.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeletePatientDigitalDnaAsync(_localProfile, "", username, sampleDna.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeletePatientDigitalDnaAsync(_localProfile, "token", "", sampleDna.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeletePatientDigitalDnaAsync(_localProfile, "token", username, ""));
        _httpHandler.SetResponse($"/api/patients/{username}/dna/{sampleDna.Id}", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.DeletePatientDigitalDnaAsync(_localProfile, "token", username, sampleDna.Id));

        // 3. ListPatientImportsAsync
        _httpHandler.SetResponse(
            $"/api/patients/{username}/imports",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new[] { sampleImport }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        IReadOnlyList<PatientDnaImportRecord> imports = await _client.ListPatientImportsAsync(_localProfile, "token", username);
        Assert.Single(imports);
        Assert.Equal(sampleImport.Id, imports[0].Id);

        _httpHandler.SetResponse($"/api/patients/{username}/imports", HttpStatusCode.OK, "null");
        IReadOnlyList<PatientDnaImportRecord> emptyImports = await _client.ListPatientImportsAsync(_localProfile, "token", username);
        Assert.Empty(emptyImports);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ListPatientImportsAsync(null!, "token", username));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ListPatientImportsAsync(_localProfile, "", username));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ListPatientImportsAsync(_localProfile, "token", ""));
        _httpHandler.SetResponse($"/api/patients/{username}/imports", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ListPatientImportsAsync(_localProfile, "token", username));

        // 4. GetPatientImportAsync
        _httpHandler.SetResponse(
            $"/api/patients/{username}/imports/{sampleImport.Id}",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(sampleImport, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        PatientDnaImportRecord fetchedImport = await _client.GetPatientImportAsync(_localProfile, "token", username, sampleImport.Id);
        Assert.Equal(sampleImport.Id, fetchedImport.Id);

        _httpHandler.SetResponse($"/api/patients/{username}/imports/{sampleImport.Id}", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.GetPatientImportAsync(_localProfile, "token", username, sampleImport.Id));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetPatientImportAsync(null!, "token", username, sampleImport.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetPatientImportAsync(_localProfile, "", username, sampleImport.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetPatientImportAsync(_localProfile, "token", "", sampleImport.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetPatientImportAsync(_localProfile, "token", username, ""));
        _httpHandler.SetResponse($"/api/patients/{username}/imports/{sampleImport.Id}", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetPatientImportAsync(_localProfile, "token", username, sampleImport.Id));

        // 5. CreatePatientImportAsync
        _httpHandler.SetResponse(
            $"/api/patients/{username}/imports",
            HttpStatusCode.Created,
            JsonSerializer.Serialize(sampleImport, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        PatientDnaImportRecord createdImport = await _client.CreatePatientImportAsync(_localProfile, "token", username, sampleImportReq);
        Assert.Equal(sampleImport.Id, createdImport.Id);

        _httpHandler.SetResponse($"/api/patients/{username}/imports", HttpStatusCode.Created, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.CreatePatientImportAsync(_localProfile, "token", username, sampleImportReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.CreatePatientImportAsync(null!, "token", username, sampleImportReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.CreatePatientImportAsync(_localProfile, "", username, sampleImportReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.CreatePatientImportAsync(_localProfile, "token", "", sampleImportReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.CreatePatientImportAsync(_localProfile, "token", username, null!));
        _httpHandler.SetResponse($"/api/patients/{username}/imports", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.CreatePatientImportAsync(_localProfile, "token", username, sampleImportReq));

        // 6. DeletePatientImportAsync
        _httpHandler.SetResponse($"/api/patients/{username}/imports/{sampleImport.Id}", HttpStatusCode.OK, "{}");
        await _client.DeletePatientImportAsync(_localProfile, "token", username, sampleImport.Id);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.DeletePatientImportAsync(null!, "token", username, sampleImport.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeletePatientImportAsync(_localProfile, "", username, sampleImport.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeletePatientImportAsync(_localProfile, "token", "", sampleImport.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeletePatientImportAsync(_localProfile, "token", username, ""));
        _httpHandler.SetResponse($"/api/patients/{username}/imports/{sampleImport.Id}", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.DeletePatientImportAsync(_localProfile, "token", username, sampleImport.Id));

        // 7. ListPatientClinicalResultsAsync (both with and without resultType)
        _httpHandler.SetResponse(
            $"/api/patients/{username}/results",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new[] { sampleResult }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        IReadOnlyList<PatientResultRecord> resultsNoFilter = await _client.ListPatientClinicalResultsAsync(_localProfile, "token", username);
        Assert.Single(resultsNoFilter);

        _httpHandler.SetResponse(
            $"/api/patients/{username}/results?resultType=oxdna",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new[] { sampleResult }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        IReadOnlyList<PatientResultRecord> resultsFiltered = await _client.ListPatientClinicalResultsAsync(_localProfile, "token", username, "oxdna");
        Assert.Single(resultsFiltered);

        _httpHandler.SetResponse($"/api/patients/{username}/results", HttpStatusCode.OK, "null");
        IReadOnlyList<PatientResultRecord> emptyResults = await _client.ListPatientClinicalResultsAsync(_localProfile, "token", username);
        Assert.Empty(emptyResults);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ListPatientClinicalResultsAsync(null!, "token", username));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ListPatientClinicalResultsAsync(_localProfile, "", username));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ListPatientClinicalResultsAsync(_localProfile, "token", ""));
        _httpHandler.SetResponse($"/api/patients/{username}/results", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ListPatientClinicalResultsAsync(_localProfile, "token", username));

        // 8. GetPatientClinicalResultAsync
        _httpHandler.SetResponse(
            $"/api/patients/{username}/results/{sampleResult.Id}",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(sampleResult, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        PatientResultRecord fetchedResult = await _client.GetPatientClinicalResultAsync(_localProfile, "token", username, sampleResult.Id);
        Assert.Equal(sampleResult.Id, fetchedResult.Id);

        _httpHandler.SetResponse($"/api/patients/{username}/results/{sampleResult.Id}", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.GetPatientClinicalResultAsync(_localProfile, "token", username, sampleResult.Id));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetPatientClinicalResultAsync(null!, "token", username, sampleResult.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetPatientClinicalResultAsync(_localProfile, "", username, sampleResult.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetPatientClinicalResultAsync(_localProfile, "token", "", sampleResult.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetPatientClinicalResultAsync(_localProfile, "token", username, ""));
        _httpHandler.SetResponse($"/api/patients/{username}/results/{sampleResult.Id}", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetPatientClinicalResultAsync(_localProfile, "token", username, sampleResult.Id));

        // 9. CreatePatientClinicalResultAsync
        _httpHandler.SetResponse(
            $"/api/patients/{username}/results",
            HttpStatusCode.Created,
            JsonSerializer.Serialize(sampleResult, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        PatientResultRecord createdResult = await _client.CreatePatientClinicalResultAsync(_localProfile, "token", username, sampleResultReq);
        Assert.Equal(sampleResult.Id, createdResult.Id);

        _httpHandler.SetResponse($"/api/patients/{username}/results", HttpStatusCode.Created, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.CreatePatientClinicalResultAsync(_localProfile, "token", username, sampleResultReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.CreatePatientClinicalResultAsync(null!, "token", username, sampleResultReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.CreatePatientClinicalResultAsync(_localProfile, "", username, sampleResultReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.CreatePatientClinicalResultAsync(_localProfile, "token", "", sampleResultReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.CreatePatientClinicalResultAsync(_localProfile, "token", username, null!));
        _httpHandler.SetResponse($"/api/patients/{username}/results", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.CreatePatientClinicalResultAsync(_localProfile, "token", username, sampleResultReq));

        // 10. DeletePatientClinicalResultAsync
        _httpHandler.SetResponse($"/api/patients/{username}/results/{sampleResult.Id}", HttpStatusCode.OK, "{}");
        await _client.DeletePatientClinicalResultAsync(_localProfile, "token", username, sampleResult.Id);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.DeletePatientClinicalResultAsync(null!, "token", username, sampleResult.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeletePatientClinicalResultAsync(_localProfile, "", username, sampleResult.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeletePatientClinicalResultAsync(_localProfile, "token", "", sampleResult.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeletePatientClinicalResultAsync(_localProfile, "token", username, ""));
        _httpHandler.SetResponse($"/api/patients/{username}/results/{sampleResult.Id}", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.DeletePatientClinicalResultAsync(_localProfile, "token", username, sampleResult.Id));
    }

    /// <summary>
    /// Verifies nominal responses and fault conditions for Identity Introspection, Session Invalidation, and Setup Status client SDK endpoints.
    /// </summary>
    /// <returns>A task representing asynchronous test completion.</returns>
    [Fact]
    public async Task IdentityAndSetupEndpoints_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests nominal and error execution paths for GetCurrentUserAsync, LogoutAsync, and GetSetupStatusAsync.
        // WHY:  Issue #131 (P128): Ensures client REST SDK properly supports auth identity inspection and setup gating.
        // HOW:  Mocks HTTP responses with serialized models, validates argument guards, null handling, and error response mapping.
        UserAccount sampleUser = new UserAccount().GetExamples();
        SetupStatusResponse sampleStatus = new SetupStatusResponse().GetExamples();

        // 1. GetCurrentUserAsync
        _httpHandler.SetResponse(
            "/api/auth/me",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(sampleUser, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        UserAccount user = await _client.GetCurrentUserAsync(_localProfile, "token");
        Assert.Equal(sampleUser.Username, user.Username);

        _httpHandler.SetResponse("/api/auth/me", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.GetCurrentUserAsync(_localProfile, "token"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetCurrentUserAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetCurrentUserAsync(_localProfile, ""));
        _httpHandler.SetResponse("/api/auth/me", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetCurrentUserAsync(_localProfile, "token"));

        // 2. LogoutAsync
        _httpHandler.SetResponse("/api/auth/logout", HttpStatusCode.OK, "{}");
        await _client.LogoutAsync(_localProfile, "token");

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.LogoutAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.LogoutAsync(_localProfile, ""));
        _httpHandler.SetResponse("/api/auth/logout", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.LogoutAsync(_localProfile, "token"));

        // 3. GetSetupStatusAsync
        _httpHandler.SetResponse(
            "/api/setup/status",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(sampleStatus, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        SetupStatusResponse status = await _client.GetSetupStatusAsync(_localProfile);
        Assert.Equal(sampleStatus.SetupRequired, status.SetupRequired);

        _httpHandler.SetResponse("/api/setup/status", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.GetSetupStatusAsync(_localProfile));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetSetupStatusAsync(null!));
        _httpHandler.SetResponse("/api/setup/status", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetSetupStatusAsync(_localProfile));
    }

    /// <summary>
    /// Verifies nominal responses and fault conditions for Compliance Cryptographic Shredding and Record Encryption client SDK endpoints.
    /// </summary>
    /// <returns>A task representing asynchronous test completion.</returns>
    [Fact]
    public async Task ComplianceAndCryptoShredEndpoints_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests nominal and error execution paths for CryptoShredUserAsync, DeIdentifyDatasetAsync, EncryptRecordAsync, and DecryptRecordAsync.
        // WHY:  Issue #132 (P129): Ensures post-quantum encryption at rest and GDPR erasure endpoints are covered by client SDK.
        // HOW:  Mocks HTTP responses with serialized models, validates argument guards, null handling, and error response mapping.
        CryptoShredRequest shredReq = new CryptoShredRequest().GetExamples();
        DeIdentifyRequest deIdReq = new DeIdentifyRequest().GetExamples();
        DeIdentificationResult deIdRes = new DeIdentificationResult().GetExamples();
        EncryptRecordRequest encReq = new EncryptRecordRequest().GetExamples();
        EncryptedEnvelope sampleEnv = new EncryptedEnvelope().GetExamples();
        DecryptRecordRequest decReq = new DecryptRecordRequest().GetExamples();

        // 1. CryptoShredUserAsync
        _httpHandler.SetResponse("/api/compliance/crypto-shred", HttpStatusCode.OK, "{}");
        await _client.CryptoShredUserAsync(_localProfile, "token", shredReq);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.CryptoShredUserAsync(null!, "token", shredReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.CryptoShredUserAsync(_localProfile, "", shredReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.CryptoShredUserAsync(_localProfile, "token", null!));
        _httpHandler.SetResponse("/api/compliance/crypto-shred", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.CryptoShredUserAsync(_localProfile, "token", shredReq));

        // 2. DeIdentifyDatasetAsync
        _httpHandler.SetResponse(
            "/api/compliance/de-identify",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(deIdRes, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        DeIdentificationResult deIdentified = await _client.DeIdentifyDatasetAsync(_localProfile, "token", deIdReq);
        Assert.Equal(deIdRes.PseudonymSubjectId, deIdentified.PseudonymSubjectId);

        _httpHandler.SetResponse("/api/compliance/de-identify", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.DeIdentifyDatasetAsync(_localProfile, "token", deIdReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.DeIdentifyDatasetAsync(null!, "token", deIdReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeIdentifyDatasetAsync(_localProfile, "", deIdReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.DeIdentifyDatasetAsync(_localProfile, "token", null!));
        _httpHandler.SetResponse("/api/compliance/de-identify", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.DeIdentifyDatasetAsync(_localProfile, "token", deIdReq));

        // 3. EncryptRecordAsync
        _httpHandler.SetResponse(
            "/api/compliance/encrypt-record",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(sampleEnv, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        EncryptedEnvelope envelope = await _client.EncryptRecordAsync(_localProfile, "token", encReq);
        Assert.Equal(sampleEnv.KeyId, envelope.KeyId);

        _httpHandler.SetResponse("/api/compliance/encrypt-record", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.EncryptRecordAsync(_localProfile, "token", encReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.EncryptRecordAsync(null!, "token", encReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.EncryptRecordAsync(_localProfile, "", encReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.EncryptRecordAsync(_localProfile, "token", null!));
        _httpHandler.SetResponse("/api/compliance/encrypt-record", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.EncryptRecordAsync(_localProfile, "token", encReq));

        // 4. DecryptRecordAsync
        _httpHandler.SetResponse(
            "/api/compliance/decrypt-record",
            HttpStatusCode.OK,
            "{\"testProp\":\"testVal\"}");

        JsonElement decrypted = await _client.DecryptRecordAsync(_localProfile, "token", decReq);
        Assert.Equal("testVal", decrypted.GetProperty("testProp").GetString());

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.DecryptRecordAsync(null!, "token", decReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DecryptRecordAsync(_localProfile, "", decReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.DecryptRecordAsync(_localProfile, "token", null!));
        _httpHandler.SetResponse("/api/compliance/decrypt-record", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.DecryptRecordAsync(_localProfile, "token", decReq));
    }

    /// <summary>
    /// Verifies nominal responses and fault conditions for dataset archive lifecycle endpoints.
    /// </summary>
    /// <returns>A task representing asynchronous test completion.</returns>
    [Fact]
    public async Task DatasetArchiveEndpoints_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests nominal and error execution paths for ImportDatasetArchiveAsync, ExportEncryptedDatasetAsync,
        //       ListExportedDatasetBundlesAsync, DeleteExportedDatasetBundleAsync, DownloadDatasetArchiveAsync, and DownloadTrajectoryAsync.
        // WHY:  Issue #134 (P131): Guarantees 100% test coverage across dataset bundle lifecycle operations.
        // HOW:  Mocks HTTP responses with serialized models, validates argument guards, null handling, and error response mapping.
        DatasetImportResponse sampleImportRes = new DatasetImportResponse().GetExamples();
        ExportEncryptedDatasetResult sampleExportRes = new ExportEncryptedDatasetResult().GetExamples();
        ExportEncryptedDatasetRequest exportReq = new() { TrajectoryFileName = "traj.xyz", SubjectId = "sub_42" };
        List<ExportFileItem> sampleExportList = [new ExportFileItem { Name = "a", SizeBytes = 1, SizeFormatted = "1B", LastModifiedUtc = "now", Category = "Archive" }.GetExamples()];
        byte[] dummyArchive = [0x1f, 0x8b, 0x08, 0x00];
        byte[] dummyManifest = Encoding.UTF8.GetBytes("{}");
        byte[] dummyFileBytes = [0x01, 0x02, 0x03, 0x04];

        // 1. ImportDatasetArchiveAsync
        _httpHandler.SetResponse(
            "/api/datasets/import",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(sampleImportRes, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        DatasetImportResponse importRes = await _client.ImportDatasetArchiveAsync(
            _localProfile, "token", dummyArchive, "archive.tar.gz", dummyManifest, "manifest.json");
        Assert.Equal(sampleImportRes.Message, importRes.Message);
        Assert.Equal(sampleImportRes.Provenance, importRes.Provenance);

        _httpHandler.SetResponse("/api/datasets/import", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.ImportDatasetArchiveAsync(_localProfile, "token", dummyArchive, "a.tar.gz", dummyManifest, "m.json"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ImportDatasetArchiveAsync(null!, "token", dummyArchive, "a.tar.gz", dummyManifest, "m.json"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ImportDatasetArchiveAsync(_localProfile, "", dummyArchive, "a.tar.gz", dummyManifest, "m.json"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ImportDatasetArchiveAsync(_localProfile, "token", null!, "a.tar.gz", dummyManifest, "m.json"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ImportDatasetArchiveAsync(_localProfile, "token", dummyArchive, "", dummyManifest, "m.json"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ImportDatasetArchiveAsync(_localProfile, "token", dummyArchive, "a.tar.gz", null!, "m.json"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ImportDatasetArchiveAsync(_localProfile, "token", dummyArchive, "a.tar.gz", dummyManifest, ""));
        _httpHandler.SetResponse("/api/datasets/import", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ImportDatasetArchiveAsync(_localProfile, "token", dummyArchive, "a.tar.gz", dummyManifest, "m.json"));

        // 2. ExportEncryptedDatasetAsync
        _httpHandler.SetResponse(
            "/api/datasets/export-encrypted",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(sampleExportRes, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        ExportEncryptedDatasetResult exportRes = await _client.ExportEncryptedDatasetAsync(_localProfile, "token", exportReq);
        Assert.Equal(sampleExportRes.BundlePath, exportRes.BundlePath);
        Assert.Equal(sampleExportRes.Algorithm, exportRes.Algorithm);

        _httpHandler.SetResponse("/api/datasets/export-encrypted", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.ExportEncryptedDatasetAsync(_localProfile, "token", exportReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ExportEncryptedDatasetAsync(null!, "token", exportReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ExportEncryptedDatasetAsync(_localProfile, "", exportReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ExportEncryptedDatasetAsync(_localProfile, "token", null!));
        _httpHandler.SetResponse("/api/datasets/export-encrypted", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ExportEncryptedDatasetAsync(_localProfile, "token", exportReq));

        // 3. ListExportedDatasetBundlesAsync
        _httpHandler.SetResponse(
            "/api/datasets/exports",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(sampleExportList, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        IReadOnlyList<ExportFileItem> exports = await _client.ListExportedDatasetBundlesAsync(_localProfile, "token");
        Assert.Single(exports);
        Assert.Equal(sampleExportList[0].Name, exports[0].Name);

        _httpHandler.SetResponse("/api/datasets/exports", HttpStatusCode.OK, "null");
        IReadOnlyList<ExportFileItem> emptyExports = await _client.ListExportedDatasetBundlesAsync(_localProfile, "token");
        Assert.Empty(emptyExports);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ListExportedDatasetBundlesAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ListExportedDatasetBundlesAsync(_localProfile, ""));
        _httpHandler.SetResponse("/api/datasets/exports", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ListExportedDatasetBundlesAsync(_localProfile, "token"));

        // 4. DeleteExportedDatasetBundleAsync
        _httpHandler.SetResponse("/api/datasets/exports/bundle.tar.gz", HttpStatusCode.OK, "{}");
        await _client.DeleteExportedDatasetBundleAsync(_localProfile, "token", "bundle.tar.gz");

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.DeleteExportedDatasetBundleAsync(null!, "token", "bundle.tar.gz"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeleteExportedDatasetBundleAsync(_localProfile, "", "bundle.tar.gz"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DeleteExportedDatasetBundleAsync(_localProfile, "token", ""));
        _httpHandler.SetResponse("/api/datasets/exports/bundle.tar.gz", HttpStatusCode.NotFound, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.DeleteExportedDatasetBundleAsync(_localProfile, "token", "bundle.tar.gz"));

        // 5. DownloadDatasetArchiveAsync
        _httpHandler.SetByteResponse("/api/datasets/download/bundle.tar.gz", HttpStatusCode.OK, dummyFileBytes);
        byte[] downloadedArchive = await _client.DownloadDatasetArchiveAsync(_localProfile, "token", "bundle.tar.gz");
        Assert.Equal(dummyFileBytes, downloadedArchive);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.DownloadDatasetArchiveAsync(null!, "token", "bundle.tar.gz"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DownloadDatasetArchiveAsync(_localProfile, "", "bundle.tar.gz"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DownloadDatasetArchiveAsync(_localProfile, "token", ""));
        _httpHandler.SetResponse("/api/datasets/download/bundle.tar.gz", HttpStatusCode.NotFound, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.DownloadDatasetArchiveAsync(_localProfile, "token", "bundle.tar.gz"));

        // 6. DownloadTrajectoryAsync
        _httpHandler.SetByteResponse("/api/trajectories/download/traj.xyz", HttpStatusCode.OK, dummyFileBytes);
        byte[] downloadedTraj = await _client.DownloadTrajectoryAsync(_localProfile, "token", "traj.xyz");
        Assert.Equal(dummyFileBytes, downloadedTraj);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.DownloadTrajectoryAsync(null!, "token", "traj.xyz"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DownloadTrajectoryAsync(_localProfile, "", "traj.xyz"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DownloadTrajectoryAsync(_localProfile, "token", ""));
        _httpHandler.SetResponse("/api/trajectories/download/traj.xyz", HttpStatusCode.NotFound, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.DownloadTrajectoryAsync(_localProfile, "token", "traj.xyz"));
    }

    /// <summary>
    /// Verifies nominal responses and fault conditions for setup bootstrap and system telemetry endpoints.
    /// </summary>
    /// <returns>A task representing asynchronous test completion.</returns>
    [Fact]
    public async Task SetupAndTelemetryEndpoints_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests nominal and error execution paths for InitializeSetupAdminAsync, GetSystemHealthAsync, and GetEventBusMetricsAsync.
        // WHY:  Issue #135 (P132): Ensures first-launch setup bootstrapping and event bus telemetry are covered by client SDK.
        // HOW:  Mocks HTTP responses with serialized models, validates argument guards, null handling, and error response mapping.
        InitialSetupRequest setupReq = new InitialSetupRequest().GetExamples();
        AuthTokenResponse sampleAuthToken = new AuthTokenResponse().GetExamples();
        SystemHealthOverview sampleHealth = new()
        {
            Status = "Healthy",
            TimestampUtc = DateTimeOffset.UtcNow,
            Version = "1.0.0",
            Uptime = "0d 4h 12m 30s",
            UptimeSeconds = 15150.0,
            SetupRequired = false,
            AuthenticatedSession = false,
            Description = "High-level system health status."
        };
        EventBusTelemetry sampleBus = new EventBusTelemetry
        {
            IsInternalBusActive = true,
            IsValkeyConnected = true,
            IsDistributedCache = true,
            ValkeyEndpoint = "endpoint",
            OutboundEventsCount = 0,
            InboundEventsCount = 0,
            ActiveSubscribers = []
        }.GetExamples();

        // 1. InitializeSetupAdminAsync
        _httpHandler.SetResponse(
            "/api/setup/initialize",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(sampleAuthToken, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        AuthTokenResponse setupRes = await _client.InitializeSetupAdminAsync(_localProfile, setupReq);
        Assert.Equal(sampleAuthToken.Token, setupRes.Token);
        Assert.Equal(sampleAuthToken.TokenType, setupRes.TokenType);

        _httpHandler.SetResponse("/api/setup/initialize", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.InitializeSetupAdminAsync(_localProfile, setupReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.InitializeSetupAdminAsync(null!, setupReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.InitializeSetupAdminAsync(_localProfile, null!));
        _httpHandler.SetResponse("/api/setup/initialize", HttpStatusCode.BadRequest, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.InitializeSetupAdminAsync(_localProfile, setupReq));

        // 2. GetSystemHealthAsync (both unauthenticated and authenticated)
        _httpHandler.SetResponse(
            "/api/system/health",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(sampleHealth, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        SystemHealthOverview health1 = await _client.GetSystemHealthAsync(_localProfile);
        Assert.Equal(sampleHealth.Status, health1.Status);
        Assert.Equal(sampleHealth.Version, health1.Version);

        SystemHealthOverview health2 = await _client.GetSystemHealthAsync(_localProfile, "bearer_token");
        Assert.Equal(sampleHealth.Status, health2.Status);

        _httpHandler.SetResponse("/api/system/health", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.GetSystemHealthAsync(_localProfile));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetSystemHealthAsync(null!));
        _httpHandler.SetResponse("/api/system/health", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetSystemHealthAsync(_localProfile));

        // 3. GetEventBusMetricsAsync
        _httpHandler.SetResponse(
            "/api/system/event-bus",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(sampleBus, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        EventBusTelemetry busRes = await _client.GetEventBusMetricsAsync(_localProfile, "token");
        Assert.Equal(sampleBus.IsInternalBusActive, busRes.IsInternalBusActive);
        Assert.Equal(sampleBus.ValkeyEndpoint, busRes.ValkeyEndpoint);

        _httpHandler.SetResponse("/api/system/event-bus", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.GetEventBusMetricsAsync(_localProfile, "token"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetEventBusMetricsAsync(null!, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetEventBusMetricsAsync(_localProfile, ""));
        _httpHandler.SetResponse("/api/system/event-bus", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetEventBusMetricsAsync(_localProfile, "token"));
    }

    /// <summary>
    /// Verifies nominal responses and fault conditions for trajectory parser, parquet export, and endocrine affinity endpoints.
    /// </summary>
    /// <returns>A task representing asynchronous test completion.</returns>
    [Fact]
    public async Task ScientificParserAndEndocrineAnalysisEndpoints_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests nominal and error execution paths for ProcessTrajectoryCoordinatesAsync, ExportCoordinatesParquetAsync,
        //       AnalyzeTrajectoryEndocrineAsync, and GetSimulationCampaignConfigAsync.
        // WHY:  Issue #136 (P133): Guarantees coverage across high-throughput parser, Parquet serialization, and endocrine analysis routes.
        // HOW:  Mocks HTTP responses with serialized models, validates argument guards, null handling, and error response mapping.
        TrajectoryParserProcessResult sampleParser = new TrajectoryParserProcessResult().GetExamples();
        TrajectoryParquetExportResult sampleParquet = new TrajectoryParquetExportResult().GetExamples();
        AptamerTrajectoryMetrics sampleMetrics = new AptamerTrajectoryMetrics
        {
            TrajectoryFileName = "traj.xyz",
            FramesAnalyzed = 100,
            AverageContactsPerFrame = 2.0,
            BindingOccupancyRatio = 0.9,
            StabilityVerdict = "Stable"
        }.GetExamples();
        SimulationInputDeckResult sampleDeck = new SimulationInputDeckResult().GetExamples();

        // 1. ProcessTrajectoryCoordinatesAsync
        _httpHandler.SetResponse(
            "/api/parser/process/traj.xyz",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(sampleParser, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        TrajectoryParserProcessResult parserRes = await _client.ProcessTrajectoryCoordinatesAsync(_localProfile, "token", "traj.xyz");
        Assert.Equal(sampleParser.FileName, parserRes.FileName);
        Assert.Equal(sampleParser.FramesParsed, parserRes.FramesParsed);

        _httpHandler.SetResponse("/api/parser/process/traj.xyz", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.ProcessTrajectoryCoordinatesAsync(_localProfile, "token", "traj.xyz"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ProcessTrajectoryCoordinatesAsync(null!, "token", "traj.xyz"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ProcessTrajectoryCoordinatesAsync(_localProfile, "", "traj.xyz"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ProcessTrajectoryCoordinatesAsync(_localProfile, "token", ""));
        _httpHandler.SetResponse("/api/parser/process/traj.xyz", HttpStatusCode.NotFound, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ProcessTrajectoryCoordinatesAsync(_localProfile, "token", "traj.xyz"));

        // 2. ExportCoordinatesParquetAsync
        _httpHandler.SetResponse(
            "/api/parser/export-parquet/traj.xyz",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(sampleParquet, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        TrajectoryParquetExportResult parquetRes = await _client.ExportCoordinatesParquetAsync(_localProfile, "token", "traj.xyz");
        Assert.Equal(sampleParquet.ParquetFile, parquetRes.ParquetFile);
        Assert.Equal(sampleParquet.TotalRecordsSerialized, parquetRes.TotalRecordsSerialized);

        _httpHandler.SetResponse("/api/parser/export-parquet/traj.xyz", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.ExportCoordinatesParquetAsync(_localProfile, "token", "traj.xyz"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ExportCoordinatesParquetAsync(null!, "token", "traj.xyz"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ExportCoordinatesParquetAsync(_localProfile, "", "traj.xyz"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ExportCoordinatesParquetAsync(_localProfile, "token", ""));
        _httpHandler.SetResponse("/api/parser/export-parquet/traj.xyz", HttpStatusCode.NotFound, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ExportCoordinatesParquetAsync(_localProfile, "token", "traj.xyz"));

        // 3. AnalyzeTrajectoryEndocrineAsync
        _httpHandler.SetResponse(
            "/api/endocrine/analyze/traj.xyz",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(sampleMetrics, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        AptamerTrajectoryMetrics metricsRes = await _client.AnalyzeTrajectoryEndocrineAsync(_localProfile, "token", "traj.xyz");
        Assert.Equal(sampleMetrics.TrajectoryFileName, metricsRes.TrajectoryFileName);
        Assert.Equal(sampleMetrics.StabilityVerdict, metricsRes.StabilityVerdict);

        _httpHandler.SetResponse("/api/endocrine/analyze/traj.xyz", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.AnalyzeTrajectoryEndocrineAsync(_localProfile, "token", "traj.xyz"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.AnalyzeTrajectoryEndocrineAsync(null!, "token", "traj.xyz"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.AnalyzeTrajectoryEndocrineAsync(_localProfile, "", "traj.xyz"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.AnalyzeTrajectoryEndocrineAsync(_localProfile, "token", ""));
        _httpHandler.SetResponse("/api/endocrine/analyze/traj.xyz", HttpStatusCode.NotFound, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.AnalyzeTrajectoryEndocrineAsync(_localProfile, "token", "traj.xyz"));

        // 4. GetSimulationCampaignConfigAsync
        _httpHandler.SetResponse(
            "/api/simulations/configs/CAMPAIGN_SERUM_STABILITY",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(sampleDeck, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        SimulationInputDeckResult deckRes = await _client.GetSimulationCampaignConfigAsync(_localProfile, "token", "CAMPAIGN_SERUM_STABILITY");
        Assert.Equal(sampleDeck.CampaignId, deckRes.CampaignId);
        Assert.Equal(sampleDeck.InputDeck, deckRes.InputDeck);

        _httpHandler.SetResponse("/api/simulations/configs/CAMPAIGN_SERUM_STABILITY", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.GetSimulationCampaignConfigAsync(_localProfile, "token", "CAMPAIGN_SERUM_STABILITY"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetSimulationCampaignConfigAsync(null!, "token", "CAMPAIGN_SERUM_STABILITY"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetSimulationCampaignConfigAsync(_localProfile, "", "CAMPAIGN_SERUM_STABILITY"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetSimulationCampaignConfigAsync(_localProfile, "token", ""));
        _httpHandler.SetResponse("/api/simulations/configs/CAMPAIGN_SERUM_STABILITY", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetSimulationCampaignConfigAsync(_localProfile, "token", "CAMPAIGN_SERUM_STABILITY"));
    }

    /// <summary>
    /// Verifies nominal responses and fault conditions for precision pharmacogenomic adjustments, Opentrons downloads, and federation chunks.
    /// </summary>
    /// <returns>A task representing asynchronous test completion.</returns>
    [Fact]
    public async Task PharmacogenomicAndFabricationEndpoints_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests nominal and error execution paths for ApplyGenomicAdjustmentsAsync, DownloadOpentronsScriptAsync, and GetFederationChunkAsync.
        // WHY:  Issue #137 (P134): Guarantees coverage across precision genomics, automation robot protocols, and P2P swarm chunks.
        // HOW:  Mocks HTTP responses with serialized models, validates argument guards, null handling, and error response mapping.
        GenomicAnalysisRequest genomicReq = new GenomicAnalysisRequest().GetExamples();
        GenomicAdjustmentResult sampleGenomicRes = new GenomicAdjustmentResult().GetExamples();
        GenerateOpentronsScriptRequest opentronsReq = new GenerateOpentronsScriptRequest { SequenceOrSampleId = "s1" }.GetExamples();
        byte[] dummyScriptBytes = Encoding.UTF8.GetBytes("# Opentrons protocol script");
        byte[] dummyChunkBytes = [0xaa, 0xbb, 0xcc, 0xdd];

        // 1. ApplyGenomicAdjustmentsAsync
        _httpHandler.SetResponse(
            "/api/genomics/apply-adjustments",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(sampleGenomicRes, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        GenomicAdjustmentResult genomicRes = await _client.ApplyGenomicAdjustmentsAsync(_localProfile, "token", genomicReq);
        Assert.NotNull(genomicRes.GenomicProfile);
        Assert.NotNull(genomicRes.AdjustedEndocrineProfile);

        _httpHandler.SetResponse("/api/genomics/apply-adjustments", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.ApplyGenomicAdjustmentsAsync(_localProfile, "token", genomicReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ApplyGenomicAdjustmentsAsync(null!, "token", genomicReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ApplyGenomicAdjustmentsAsync(_localProfile, "", genomicReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ApplyGenomicAdjustmentsAsync(_localProfile, "token", null!));
        _httpHandler.SetResponse("/api/genomics/apply-adjustments", HttpStatusCode.BadRequest, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ApplyGenomicAdjustmentsAsync(_localProfile, "token", genomicReq));

        // 2. DownloadOpentronsScriptAsync
        _httpHandler.SetByteResponse("/api/fabrication/opentrons/download", HttpStatusCode.OK, dummyScriptBytes, "text/x-python");
        byte[] script = await _client.DownloadOpentronsScriptAsync(_localProfile, "token", opentronsReq);
        Assert.Equal(dummyScriptBytes, script);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.DownloadOpentronsScriptAsync(null!, "token", opentronsReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.DownloadOpentronsScriptAsync(_localProfile, "", opentronsReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.DownloadOpentronsScriptAsync(_localProfile, "token", null!));
        _httpHandler.SetResponse("/api/fabrication/opentrons/download", HttpStatusCode.BadRequest, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.DownloadOpentronsScriptAsync(_localProfile, "token", opentronsReq));

        // 3. GetFederationChunkAsync (without index and with index)
        _httpHandler.SetByteResponse("/api/federation/chunks/traj.xyz", HttpStatusCode.OK, dummyChunkBytes);
        byte[] chunk1 = await _client.GetFederationChunkAsync(_localProfile, "token", "traj.xyz");
        Assert.Equal(dummyChunkBytes, chunk1);

        byte[] chunk2 = await _client.GetFederationChunkAsync(_localProfile, "token", "traj.xyz", chunkIndex: 3);
        Assert.Equal(dummyChunkBytes, chunk2);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetFederationChunkAsync(null!, "token", "traj.xyz"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetFederationChunkAsync(_localProfile, "", "traj.xyz"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetFederationChunkAsync(_localProfile, "token", ""));
        _httpHandler.SetResponse("/api/federation/chunks/traj.xyz", HttpStatusCode.NotFound, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetFederationChunkAsync(_localProfile, "token", "traj.xyz"));
    }

    /// <summary>
    /// Verifies NodeApiException constructors, properties, and sensitive credential masking.
    /// </summary>
    [Fact]
    public void NodeApiException_ConstructorsAndSanitization_MasksSensitiveCredentials()
    {
        // WHAT: Tests NodeApiException constructor overloads, Message formatting, and credential sanitization.
        // WHY:  Prime Directive 14 prohibits leaking Bearer tokens, private keys, or passphrases in exception text.
        // HOW:  Instantiates exception with sensitive text and verifies redacted output in properties.
        NodeApiException ex1 = new("Simple error message");
        Assert.Equal(0, ex1.StatusCode);
        Assert.Equal("Simple error message", ex1.Title);
        Assert.Null(ex1.Detail);

        Exception inner = new InvalidOperationException("Inner root cause");
        NodeApiException ex2 = new("Wrapped error", inner);
        Assert.Same(inner, ex2.InnerException);

        NodeApiException ex3 = new(
            401,
            "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.invalid token signature",
            "Error with Bearer secret_jwt_token_here embedded",
            "trace_id_1",
            "/api/auth/token");

        Assert.Contains("Bearer [REDACTED]", ex3.Title);
        Assert.Contains("Bearer [REDACTED]", ex3.Detail!);
        Assert.DoesNotContain("secret_jwt_token_here", ex3.Detail!);
        Assert.Equal(401, ex3.StatusCode);
        Assert.Equal("trace_id_1", ex3.TraceId);
        Assert.Equal("/api/auth/token", ex3.Instance);

        NodeApiException ex4 = new(500, "Corrupted -----BEGIN PRIVATE KEY----- material");
        Assert.Equal("[REDACTED_KEY_MATERIAL]", ex4.Title);

        // Test empty/null strings in Sanitize
        NodeApiException exEmpty = new(400, "", "");
        Assert.Equal(string.Empty, exEmpty.Title);
        Assert.Equal(string.Empty, exEmpty.Detail);

        // Test Bearer at end of string with no trailing space
        NodeApiException exTrailing = new(401, "Error: Bearer token_at_the_end");
        Assert.Equal("Error: Bearer [REDACTED]", exTrailing.Title);

        // Test Message format with and without trace ID
        NodeApiException exNoTrace = new(500, "General Error", null, null);
        Assert.Equal("General Error (Status: 500)", exNoTrace.Message);

        NodeApiException exWithTrace = new(500, "General Error", null, "trace_xyz");
        Assert.Equal("General Error (Status: 500, TraceId: trace_xyz)", exWithTrace.Message);
    }

    /// <summary>
    /// Verifies NodeApiException.CreateAsync with various response bodies.
    /// </summary>
    [Fact]
    public async Task NodeApiException_CreateAsync_ParsesRfc7807AndFallbacks()
    {
        // WHAT: Tests CreateAsync with RFC 7807 JSON, empty body, and non-JSON HTML body.
        // WHY:  Standardizes exception creation across all HTTP response conditions.
        // HOW:  Calls CreateAsync with various HttpResponseMessage instances and asserts properties.
        await Assert.ThrowsAsync<ArgumentNullException>(() => NodeApiException.CreateAsync(null!));

        // RFC 7807 problem details
        using HttpResponseMessage resp1 = new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"title": "Validation Failed", "status": 400, "detail": "Field is required", "traceId": "t1", "instance": "/api/users"}""",
                Encoding.UTF8,
                "application/problem+json")
        };
        NodeApiException ex1 = await NodeApiException.CreateAsync(resp1);
        Assert.Equal(400, ex1.StatusCode);
        Assert.Equal("Validation Failed", ex1.Title);
        Assert.Equal("Field is required", ex1.Detail);
        Assert.Equal("t1", ex1.TraceId);
        Assert.Equal("/api/users", ex1.Instance);

        // Empty body
        using HttpResponseMessage resp2 = new(HttpStatusCode.NotFound);
        NodeApiException ex2 = await NodeApiException.CreateAsync(resp2);
        Assert.Equal(404, ex2.StatusCode);
        Assert.Equal("HTTP 404 Not Found", ex2.Title);
        Assert.Null(ex2.Detail);

        // Non-JSON HTML body (e.g., 502 Bad Gateway)
        using HttpResponseMessage resp3 = new(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html><body>502 Bad Gateway</body></html>", Encoding.UTF8, "text/html")
        };
        NodeApiException ex3 = await NodeApiException.CreateAsync(resp3);
        Assert.Equal(502, ex3.StatusCode);
        Assert.Contains("502", ex3.Title);
        Assert.Contains("502 Bad Gateway", ex3.Detail);
    }

    /// <summary>
    /// Verifies DTO record property initialization and structural equality.
    /// </summary>
    [Fact]
    public void ClientDtoRecords_PropertyInitialization_MaintainsInvariants()
    {
        // WHAT: Tests instantiation and property retention on strongly-typed client DTO records.
        // WHY:  Guarantees 100% test coverage across all client API contracts.
        // HOW:  Instantiates records and verifies property values.
        DateTimeOffset now = DateTimeOffset.UtcNow;

        NodeHealthSnapshot health = new("Healthy", "node-1", true, true, now);
        Assert.Equal("Healthy", health.Status);
        Assert.Equal("node-1", health.NodeName);
        Assert.True(health.IsDatabaseConnected);
        Assert.True(health.IsStorageHealthy);
        Assert.Equal(now, health.TimestampUtc);

        InstanceAlertEvent alert = new("a1", "i1", "Node 1", InstanceAlertSeverity.Critical, "Offline", "Error", now);
        Assert.Equal("a1", alert.AlertId);
        Assert.Equal("i1", alert.InstanceId);
        Assert.Equal("Node 1", alert.InstanceDisplayName);
        Assert.Equal(InstanceAlertSeverity.Critical, alert.Severity);
        Assert.Equal("Offline", alert.Title);
        Assert.Equal("Error", alert.Message);
        Assert.Equal(now, alert.OccurredAtUtc);
        Assert.NotNull(alert.ToString());
        Assert.Equal(alert, alert with { });

        InstanceConnectionSnapshot snap = new("i1", "Node 1", InstanceConnectionState.Connecting, 10.0, 20.0, 30.0, 1, 100.0, now, "error");
        Assert.Equal("i1", snap.InstanceId);
        Assert.Equal("Node 1", snap.DisplayName);
        Assert.Equal(InstanceConnectionState.Connecting, snap.State);
        Assert.Equal(10.0, snap.CpuUtilizationPercent);
        Assert.Equal(20.0, snap.GpuUtilizationPercent);
        Assert.Equal(30.0, snap.GpuTemperatureCelsius);
        Assert.Equal(1, snap.ActiveSimulationCount);
        Assert.Equal(100.0, snap.ParsedFramesPerSecond);
        Assert.Equal(now, snap.LastUpdatedUtc);
        Assert.Equal("error", snap.LastErrorTitle);
        Assert.NotNull(snap.ToString());
        Assert.Equal(snap, snap with { });

        NodeAuthSession auth = new("token", "Bearer", 1200, now, now.AddSeconds(1200), "user", "Admin");
        Assert.Equal("token", auth.Token);
        Assert.Equal("Bearer", auth.TokenType);
        Assert.Equal(1200, auth.ExpiresInSeconds);
        Assert.Equal(now, auth.IssuedAtUtc);
        Assert.Equal(now.AddSeconds(1200), auth.ExpiresAtUtc);
        Assert.Equal("user", auth.Username);
        Assert.Equal("Admin", auth.Role);
        Assert.NotNull(auth.ToString());
        Assert.Equal(auth, auth with { });

        NodeTelemetrySnapshot tele = new("node", "Intel", 100, 50.0, 30.0, 60.0, 1, 2, 3, "1d", 10.0, 20.0, 70.0, 4);
        Assert.Equal("node", tele.NodeName);
        Assert.Equal("Intel", tele.AcceleratorBackend);
        Assert.Equal(100, tele.FramesParsed);
        Assert.Equal(50.0, tele.FramesPerSecond);
        Assert.Equal(30.0, tele.HeapMemoryMb);
        Assert.Equal(60.0, tele.WorkingSetMb);
        Assert.Equal(1, tele.GcGen0Collections);
        Assert.Equal(2, tele.GcGen1Collections);
        Assert.Equal(3, tele.GcGen2Collections);
        Assert.Equal("1d", tele.Uptime);
        Assert.Equal(10.0, tele.CpuUtilizationPercent);
        Assert.Equal(20.0, tele.GpuUtilizationPercent);
        Assert.Equal(70.0, tele.GpuTemperatureCelsius);
        Assert.Equal(4, tele.ActiveSimulationCount);
        Assert.NotNull(tele.ToString());
        Assert.Equal(tele, tele with { });

        SimulationCampaignSummary camp = new("c1", "Camp 1", "oxDNA", "desc", "goal", true);
        Assert.Equal("c1", camp.CampaignId);
        Assert.Equal("Camp 1", camp.Name);
        Assert.Equal("oxDNA", camp.SimulationEngine);
        Assert.Equal("desc", camp.Description);
        Assert.Equal("goal", camp.PhysicsGoal);
        Assert.True(camp.IsActive);
        Assert.NotNull(camp.ToString());
        Assert.Equal(camp, camp with { });

        EndocrineProfileSummary endo = new("Estrogenic", 700.0, 1.0, 10.0, now);
        Assert.Equal("Estrogenic", endo.Dominance);
        Assert.Equal(700.0, endo.TargetEstradiolPicoMolar);
        Assert.Equal(1.0, endo.TargetTestosteroneNanoMolar);
        Assert.Equal(10.0, endo.TargetProgesteroneNanoMolar);
        Assert.Equal(now, endo.LastUpdatedUtc);
        Assert.NotNull(endo.ToString());
        Assert.Equal(endo, endo with { });

        DatasetImportResponse importResp = new DatasetImportResponse().GetExamples();
        Assert.Equal("Dataset successfully verified and imported.", importResp.Message);
        Assert.Equal("signed", importResp.Provenance);
        Assert.NotNull(importResp.Manifest);
        Assert.NotNull(importResp.SignerKeyId);
        Assert.NotNull(importResp.ToString());
        Assert.Equal(importResp, importResp with { });

        ExportEncryptedDatasetResult exportResult = new ExportEncryptedDatasetResult().GetExamples();
        Assert.Contains("enc.tar.gz", exportResult.BundlePath);
        Assert.Equal("ML-KEM-768+AES-256-GCM", exportResult.Algorithm);
        Assert.Equal("sub_patient_42", exportResult.SubjectId);
        Assert.NotNull(exportResult.Message);
        Assert.NotNull(exportResult.ToString());
        Assert.Equal(exportResult, exportResult with { });

        SimulationInputDeckResult inputDeckResult = new SimulationInputDeckResult().GetExamples();
        Assert.Equal("CAMPAIGN_SERUM_STABILITY", inputDeckResult.CampaignId);
        Assert.Contains("oxDNA", inputDeckResult.InputDeck);
        Assert.NotNull(inputDeckResult.ToString());
        Assert.Equal(inputDeckResult, inputDeckResult with { });

        TrajectoryParserProcessResult parserResult = new TrajectoryParserProcessResult().GetExamples();
        Assert.Equal("nanocarrier-production.xyz", parserResult.FileName);
        Assert.Equal("XYZ", parserResult.Format);
        Assert.Equal(10000, parserResult.FramesParsed);
        Assert.Equal(8400000, parserResult.TotalAtomsProcessed);
        Assert.Equal(142, parserResult.ElapsedMilliseconds);
        Assert.Equal(70422.5, parserResult.FramesPerSecond);
        Assert.NotNull(parserResult.ToString());
        Assert.Equal(parserResult, parserResult with { });

        TrajectoryParquetExportResult parquetResult = new TrajectoryParquetExportResult().GetExamples();
        Assert.Equal("nanocarrier-production.xyz", parquetResult.SourceFile);
        Assert.Equal("nanocarrier-production.parquet", parquetResult.ParquetFile);
        Assert.Equal("XYZ", parquetResult.Format);
        Assert.Equal(8400000, parquetResult.TotalRecordsSerialized);
        Assert.Equal(12582912, parquetResult.FileSizeBytes);
        Assert.Equal(310, parquetResult.ElapsedMilliseconds);
        Assert.NotNull(parquetResult.ToString());
        Assert.Equal(parquetResult, parquetResult with { });

        GenomicAdjustmentResult genomicResult = new GenomicAdjustmentResult().GetExamples();
        Assert.NotNull(genomicResult.GenomicProfile);
        Assert.NotNull(genomicResult.AdjustedEndocrineProfile);
        Assert.NotNull(genomicResult.ToString());
        Assert.Equal(genomicResult, genomicResult with { });
    }

    /// <summary>
    /// Verifies RunSerumStabilityAsync, RunAptamerGatingAsync, RunPayloadLeakageAsync, and RunPbpkSimulationAsync nominal and error paths.
    /// </summary>
    [Fact]
    public async Task SimulationCampaignEndpoints_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests simulation execution endpoints: serum stability, aptamer gating, payload leakage, and PBPK endocrine.
        // WHY:  Guarantees 100% test coverage and error validation across all simulation campaign REST calls.
        // HOW:  Stubs endpoints with example DTOs, asserts deserialization, tests HTTP errors, empty bodies, and argument guards.

        // 1. RunSerumStabilityAsync
        SerumStabilityAnalysisResult serumExample = new SerumStabilityAnalysisResult
        {
            ScaffoldName = "M13mp18",
            TemperatureCelsius = 37.0,
            DivalentMagnesiumMilliMolar = 0.8,
            AverageRmsfNm = 0.8,
            MaxCrossoverRmsfNm = 1.1,
            DebyeScreeningLengthNm = 0.7,
            StructuralIntegrityScore = 0.9,
            CompromisedCrossoverCount = 0,
            TotalCrossoverCount = 180,
            Verdict = "OK",
            RecommendedStabilization = "None",
            VulnerableCrossovers = [],
            RmsfProfilePoints = [0.5]
        }.GetExamples();
        _httpHandler.SetResponse("/api/simulations/run/serum-stability", HttpStatusCode.OK, JsonSerializer.Serialize(serumExample));
        SerumStabilityRequest serumReq = new SerumStabilityRequest().GetExamples();
        SerumStabilityAnalysisResult serumRes = await _client.RunSerumStabilityAsync(_localProfile, "test_token", serumReq);
        Assert.NotNull(serumRes);
        Assert.Equal(serumExample.StructuralIntegrityScore, serumRes.StructuralIntegrityScore);

        _httpHandler.SetResponse("/api/simulations/run/serum-stability", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.RunSerumStabilityAsync(_localProfile, "test_token", serumReq));
        _httpHandler.SetResponse("/api/simulations/run/serum-stability", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.RunSerumStabilityAsync(_localProfile, "test_token", serumReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.RunSerumStabilityAsync(null!, "test_token", serumReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.RunSerumStabilityAsync(_localProfile, "", serumReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.RunSerumStabilityAsync(_localProfile, "test_token", null!));

        // 2. RunAptamerGatingAsync
        AptamerGatingAnalysisResult aptamerExample = new AptamerGatingAnalysisResult
        {
            TargetReceptor = "R",
            TargetGonadotropin = "LH",
            UnboundOpeningFreeEnergyKcalMol = 9.0,
            BoundOpeningFreeEnergyKcalMol = -2.0,
            AllostericSwitchDeltaKcalMol = -11.0,
            GnRhReceptorContactRatio = 0.85,
            EstimatedKdNanoMolar = 4.0,
            GatingVerdict = "OK",
            TropismVerdict = "OK",
            PmfCurve = []
        }.GetExamples();
        _httpHandler.SetResponse("/api/simulations/run/aptamer-gating", HttpStatusCode.OK, JsonSerializer.Serialize(aptamerExample));
        AptamerGatingRequest aptamerReq = new AptamerGatingRequest().GetExamples();
        AptamerGatingAnalysisResult aptamerRes = await _client.RunAptamerGatingAsync(_localProfile, "test_token", aptamerReq);
        Assert.NotNull(aptamerRes);
        Assert.Equal(aptamerExample.GnRhReceptorContactRatio, aptamerRes.GnRhReceptorContactRatio);

        _httpHandler.SetResponse("/api/simulations/run/aptamer-gating", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.RunAptamerGatingAsync(_localProfile, "test_token", aptamerReq));
        _httpHandler.SetResponse("/api/simulations/run/aptamer-gating", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.RunAptamerGatingAsync(_localProfile, "test_token", aptamerReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.RunAptamerGatingAsync(null!, "test_token", aptamerReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.RunAptamerGatingAsync(_localProfile, "", aptamerReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.RunAptamerGatingAsync(_localProfile, "test_token", null!));

        // 3. RunPayloadLeakageAsync
        PayloadLeakageAnalysisResult leakageExample = new PayloadLeakageAnalysisResult
        {
            ChassisGeometry = "Nanotube",
            HormoneMolecule = "E2",
            CavityFunctionalization = "Cyclodextrin",
            RetentionPercentageClosed = 98.6,
            PassiveLeakagePercentage = 1.4,
            ReleaseRatePerMinuteOpen = 8.4,
            EncapsulationCapacityMolecules = 48,
            MaxDailyDeliveryNanomoles = 1.25,
            LeakageVerdict = "OK",
            RetentionCurve = []
        }.GetExamples();
        _httpHandler.SetResponse("/api/simulations/run/payload-leakage", HttpStatusCode.OK, JsonSerializer.Serialize(leakageExample));
        PayloadLeakageRequest leakageReq = new PayloadLeakageRequest().GetExamples();
        PayloadLeakageAnalysisResult leakageRes = await _client.RunPayloadLeakageAsync(_localProfile, "test_token", leakageReq);
        Assert.NotNull(leakageRes);
        Assert.Equal(leakageExample.RetentionPercentageClosed, leakageRes.RetentionPercentageClosed);

        _httpHandler.SetResponse("/api/simulations/run/payload-leakage", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.RunPayloadLeakageAsync(_localProfile, "test_token", leakageReq));
        _httpHandler.SetResponse("/api/simulations/run/payload-leakage", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.RunPayloadLeakageAsync(_localProfile, "test_token", leakageReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.RunPayloadLeakageAsync(null!, "test_token", leakageReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.RunPayloadLeakageAsync(_localProfile, "", leakageReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.RunPayloadLeakageAsync(_localProfile, "test_token", null!));

        // 4. RunPbpkSimulationAsync
        PbpkEndocrineSimulationResult pbpkExample = new PbpkEndocrineSimulationResult
        {
            PatientId = "P1",
            Regimen = TherapeuticRegimen.MaleToFemale,
            SimulationDurationDays = 90,
            DepotDoseMg = 25.0,
            AverageSteadyStateEstradiolPmolL = 500.0,
            AverageSteadyStateTestosteroneNmolL = 1.0,
            AverageLhIuL = 2.0,
            TimeInTargetRangePercent = 95.0,
            TargetSuppressionAchieved = true,
            ThromboticSurgeDetected = false,
            RecommendedDosingIntervalDays = 180,
            ClinicalFeasibilityVerdict = "OK",
            OrganBiodistributionSummary = "Summary",
            DailyKinetics = []
        }.GetExamples();
        _httpHandler.SetResponse("/api/simulations/run/pbpk-endocrine", HttpStatusCode.OK, JsonSerializer.Serialize(pbpkExample));
        PbpkSimulationRequest pbpkReq = new PbpkSimulationRequest().GetExamples();
        PbpkEndocrineSimulationResult pbpkRes = await _client.RunPbpkSimulationAsync(_localProfile, "test_token", pbpkReq);
        Assert.NotNull(pbpkRes);
        Assert.Equal(pbpkExample.AverageSteadyStateEstradiolPmolL, pbpkRes.AverageSteadyStateEstradiolPmolL);

        _httpHandler.SetResponse("/api/simulations/run/pbpk-endocrine", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.RunPbpkSimulationAsync(_localProfile, "test_token", pbpkReq));
        _httpHandler.SetResponse("/api/simulations/run/pbpk-endocrine", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.RunPbpkSimulationAsync(_localProfile, "test_token", pbpkReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.RunPbpkSimulationAsync(null!, "test_token", pbpkReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.RunPbpkSimulationAsync(_localProfile, "", pbpkReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.RunPbpkSimulationAsync(_localProfile, "test_token", null!));
    }

    /// <summary>
    /// Verifies ExchangeWebTokenAsync and GetHealthzAsync nominal and error paths.
    /// </summary>
    [Fact]
    public async Task WebTokenAndHealthzEndpoints_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests web challenge-token exchange (/api/auth/web-token) and cluster liveness health (/healthz).
        // WHY:  Guarantees 100% test coverage and error validation across session and liveness routes.
        // HOW:  Stubs endpoints with example DTOs, asserts deserialization, tests HTTP errors, empty bodies, and argument guards.

        // 1. ExchangeWebTokenAsync
        AuthTokenResponse tokenExample = new AuthTokenResponse().GetExamples();
        _httpHandler.SetResponse("/api/auth/web-token", HttpStatusCode.OK, JsonSerializer.Serialize(tokenExample));
        TokenExchangeRequest exchangeReq = new TokenExchangeRequest().GetExamples();
        AuthTokenResponse tokenRes = await _client.ExchangeWebTokenAsync(_localProfile, exchangeReq);
        Assert.NotNull(tokenRes);
        Assert.Equal(tokenExample.Token, tokenRes.Token);

        _httpHandler.SetResponse("/api/auth/web-token", HttpStatusCode.Unauthorized, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.ExchangeWebTokenAsync(_localProfile, exchangeReq));
        _httpHandler.SetResponse("/api/auth/web-token", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.ExchangeWebTokenAsync(_localProfile, exchangeReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ExchangeWebTokenAsync(null!, exchangeReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ExchangeWebTokenAsync(_localProfile, null!));

        // 2. GetHealthzAsync
        SystemHealthOverview healthExample = new()
        {
            Status = "Healthy",
            TimestampUtc = DateTimeOffset.UtcNow,
            Version = "1.0.0",
            Uptime = "0d 4h 12m 30s",
            UptimeSeconds = 15150.0,
            SetupRequired = false,
            AuthenticatedSession = false,
            Description = "High-level system health status."
        };
        _httpHandler.SetResponse("/healthz", HttpStatusCode.OK, JsonSerializer.Serialize(healthExample));
        SystemHealthOverview healthRes = await _client.GetHealthzAsync(_localProfile);
        Assert.NotNull(healthRes);
        Assert.Equal(healthExample.Status, healthRes.Status);

        _httpHandler.SetResponse("/healthz", HttpStatusCode.ServiceUnavailable, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetHealthzAsync(_localProfile));
        _httpHandler.SetResponse("/healthz", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.GetHealthzAsync(_localProfile));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetHealthzAsync(null!));
    }

    /// <summary>
    /// Verifies CreatePatientDnaAsync, GetHealthAsync, GetGranularSystemHealthAsync, and filtered GetPatientDnaRegimensAsync nominal and error paths.
    /// </summary>
    /// <returns>A task representing asynchronous test completion.</returns>
    [Fact]
    public async Task PatientDnaAndHealthEndpoints_NominalAndErrors_BehavesExpectedly()
    {
        // WHAT: Tests CreatePatientDnaAsync, GetHealthAsync, GetGranularSystemHealthAsync, and filtered GetPatientDnaRegimensAsync.
        // WHY:  Issue #152 ([P149]): Guarantees 100% code coverage and fault resilience across patient DNA synthesis and health probes.
        // HOW:  Mocks responses, exercises nominal serialization, validates HTTP errors, null payloads, and argument exceptions.

        // 1. CreatePatientDnaAsync
        PatientDigitalDnaRecord dnaRecord = new()
        {
            Id = "dna_test_01",
            Username = "patient-01",
            Title = "Hexagonal Nanotube (6HB)",
            Regimen = TherapeuticRegimen.MaleToFemale,
            TargetTissue = "Pituitary",
            ActiveLigand = "GnRH",
            NanocarrierGeometry = "Hexagonal Nanotube (6HB)",
            TargetAptamerKdNm = 3.5,
            ScaffoldFasta = "ACGTACGT",
            GcPercentage = 50.0,
            EstimatedMwKDa = 120.5,
            Staples = [],
            AnnealingProtocol = [],
            ProvenanceSha256 = "sha256_mock_hash",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        _httpHandler.SetResponse("/api/patients/patient-01/dna", HttpStatusCode.Created, JsonSerializer.Serialize(dnaRecord));
        CreatePatientDnaRequest createDnaReq = new()
        {
            Title = "Hexagonal Nanotube (6HB)",
            Regimen = TherapeuticRegimen.MaleToFemale,
            NanocarrierGeometry = "Hexagonal Nanotube (6HB)",
            TargetAptamerKdNm = 3.5
        };

        PatientDigitalDnaRecord createdDnaRes = await _client.CreatePatientDnaAsync(_localProfile, "valid_token", "patient-01", createDnaReq);
        Assert.NotNull(createdDnaRes);
        Assert.Equal("dna_test_01", createdDnaRes.Id);
        Assert.Equal("patient-01", createdDnaRes.Username);

        _httpHandler.SetResponse("/api/patients/patient-01/dna", HttpStatusCode.BadRequest, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.CreatePatientDnaAsync(_localProfile, "valid_token", "patient-01", createDnaReq));
        _httpHandler.SetResponse("/api/patients/patient-01/dna", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.CreatePatientDnaAsync(_localProfile, "valid_token", "patient-01", createDnaReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.CreatePatientDnaAsync(null!, "valid_token", "patient-01", createDnaReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.CreatePatientDnaAsync(_localProfile, "", "patient-01", createDnaReq));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.CreatePatientDnaAsync(_localProfile, "valid_token", "", createDnaReq));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.CreatePatientDnaAsync(_localProfile, "valid_token", "patient-01", null!));

        // 2. GetHealthAsync
        SystemHealthOverview healthOverview = new()
        {
            Status = "Healthy",
            TimestampUtc = DateTimeOffset.UtcNow,
            Version = "1.0.0",
            Uptime = "1d 2h 3m 4s",
            UptimeSeconds = 93784.0,
            SetupRequired = false,
            AuthenticatedSession = false,
            Description = "Standard operational health overview."
        };
        _httpHandler.SetResponse("/health", HttpStatusCode.OK, JsonSerializer.Serialize(healthOverview));
        SystemHealthOverview healthRes = await _client.GetHealthAsync(_localProfile);
        Assert.NotNull(healthRes);
        Assert.Equal("Healthy", healthRes.Status);

        _httpHandler.SetResponse("/health", HttpStatusCode.ServiceUnavailable, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetHealthAsync(_localProfile));
        _httpHandler.SetResponse("/health", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.GetHealthAsync(_localProfile));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetHealthAsync(null!));

        // 3. GetGranularSystemHealthAsync
        GranularSystemHealth granularExample = ((GranularSystemHealth)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GranularSystemHealth))).GetExamples();
        _httpHandler.SetResponse("/api/system/health", HttpStatusCode.OK, JsonSerializer.Serialize(granularExample));
        GranularSystemHealth granularRes = await _client.GetGranularSystemHealthAsync(_localProfile, "valid_token");
        Assert.NotNull(granularRes);
        Assert.Equal(granularExample.Status, granularRes.Status);
        Assert.Equal(granularExample.User.Username, granularRes.User.Username);

        _httpHandler.SetResponse("/api/system/health", HttpStatusCode.Unauthorized, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetGranularSystemHealthAsync(_localProfile, "valid_token"));
        _httpHandler.SetResponse("/api/system/health", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.GetGranularSystemHealthAsync(_localProfile, "valid_token"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetGranularSystemHealthAsync(null!, "valid_token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetGranularSystemHealthAsync(_localProfile, ""));

        // 4. Filtered GetPatientDnaRegimensAsync
        PatientDigitalDnaPackage dnaPkg = new()
        {
            PatientId = "patient-01",
            Regimen = TherapeuticRegimen.FemaleToMale,
            TargetTissue = "Testes",
            ActiveLigand = "Testosterone",
            NanocarrierGeometry = "6HB",
            TargetAptamerKdNm = 2.5,
            ScaffoldFasta = "GGCCAATT",
            GcPercentage = 50.0,
            EstimatedMwKDa = 80.0,
            Staples = [],
            AnnealingProtocol = [],
            ProvenanceSha256 = "sha256_package_hash",
            GeneratedAtUtc = DateTime.UtcNow
        };
        _httpHandler.SetResponse("/api/patient/dna", HttpStatusCode.OK, JsonSerializer.Serialize(dnaPkg));

        // Nominal with full filters
        PatientDigitalDnaPackage filteredPkg = await _client.GetPatientDnaRegimensAsync(
            _localProfile,
            "valid_token",
            "patient-01",
            TherapeuticRegimen.FemaleToMale,
            "6HB",
            2.5);
        Assert.NotNull(filteredPkg);
        Assert.Equal("patient-01", filteredPkg.PatientId);
        Assert.Equal(TherapeuticRegimen.FemaleToMale, filteredPkg.Regimen);

        // Nominal with null filters (empty query string path)
        PatientDigitalDnaPackage emptyFilterPkg = await _client.GetPatientDnaRegimensAsync(
            _localProfile,
            "valid_token",
            null,
            null,
            null,
            null);
        Assert.NotNull(emptyFilterPkg);
        Assert.Equal("patient-01", emptyFilterPkg.PatientId);

        _httpHandler.SetResponse("/api/patient/dna", HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<NodeApiException>(() => _client.GetPatientDnaRegimensAsync(_localProfile, "valid_token", "patient-01", TherapeuticRegimen.FemaleToMale, "6HB", 2.5));
        _httpHandler.SetResponse("/api/patient/dna", HttpStatusCode.OK, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.GetPatientDnaRegimensAsync(_localProfile, "valid_token", "patient-01", TherapeuticRegimen.FemaleToMale, "6HB", 2.5));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.GetPatientDnaRegimensAsync(null!, "valid_token", "patient-01", TherapeuticRegimen.FemaleToMale, "6HB", 2.5));
        await Assert.ThrowsAsync<ArgumentException>(() => _client.GetPatientDnaRegimensAsync(_localProfile, "", "patient-01", TherapeuticRegimen.FemaleToMale, "6HB", 2.5));
    }

    /// <summary>
    /// In-memory HTTP message handler stub for deterministic test execution.
    /// </summary>
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        /// <summary>
        /// Dictionary mapping path suffixes to HTTP response factories.
        /// </summary>
        private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new();

        /// <summary>
        /// Dictionary mapping path suffixes to exceptions to throw.
        /// </summary>
        private readonly Dictionary<string, Exception> _exceptions = new();

        /// <summary>
        /// Configures a stubbed response for a specific URI path suffix.
        /// </summary>
        /// <param name="path">The URI path suffix to match.</param>
        /// <param name="statusCode">The HTTP status code to return.</param>
        /// <param name="content">The response body content string.</param>
        public void SetResponse(string path, HttpStatusCode statusCode, string content)
        {
            // WHAT: Stubs a response for a route.
            // WHY:  Allows tests to define expected HTTP responses per endpoint.
            // HOW:  Registers a response generator lambda in the routes dictionary.
            _exceptions.Remove(path);
            _routes[path] = _ => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
        }

        /// <summary>
        /// Configures a stubbed binary byte response for a specific URI path suffix.
        /// </summary>
        /// <param name="path">The URI path suffix to match.</param>
        /// <param name="statusCode">The HTTP status code to return.</param>
        /// <param name="bytes">The response byte array.</param>
        /// <param name="mediaType">The MIME media type.</param>
        public void SetByteResponse(string path, HttpStatusCode statusCode, byte[] bytes, string mediaType = "application/octet-stream")
        {
            // WHAT: Stubs a binary byte response for a route.
            // WHY:  Allows tests to return arbitrary raw byte payloads.
            // HOW:  Registers a response generator returning ByteArrayContent.
            _exceptions.Remove(path);
            _routes[path] = _ => new HttpResponseMessage(statusCode)
            {
                Content = new ByteArrayContent(bytes)
                {
                    Headers = { ContentType = new MediaTypeHeaderValue(mediaType) }
                }
            };
        }

        /// <summary>
        /// Configures an exception to throw when a specific URI path suffix is requested.
        /// </summary>
        /// <param name="path">The URI path suffix to match.</param>
        /// <param name="exception">The exception to throw.</param>
        public void SetException(string path, Exception exception)
        {
            // WHAT: Configures an exception for a route.
            // WHY:  Allows tests to simulate socket timeouts or connection drops.
            // HOW:  Registers the exception in the exceptions dictionary.
            _routes.Remove(path);
            _exceptions[path] = exception;
        }

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // WHAT: Dispatches in-memory HTTP responses based on request URI path.
            // WHY:  Executes simulated HTTP calls synchronously without network sockets.
            // HOW:  Finds matching route by path ending and returns generated response or throws configured exception.
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;

            foreach (var kvp in _exceptions)
            {
                if (path.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    throw kvp.Value;
                }
            }

            foreach (var kvp in _exceptions)
            {
                if (path.EndsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    throw kvp.Value;
                }
            }

            foreach (var kvp in _routes)
            {
                if (path.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(kvp.Value(request));
                }
            }

            foreach (var kvp in _routes)
            {
                if (path.EndsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(kvp.Value(request));
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Route not found: {path}")
            });
        }
    }

    /// <summary>
    /// In-memory test stub for IClientKeyRingVault supporting ReadOnlySpan parameters.
    /// </summary>
    private sealed class StubKeyRingVault : IClientKeyRingVault
    {
        /// <summary>
        /// Expected signature to return from SignChallenge.
        /// </summary>
        public string SignatureToReturn { get; set; } = "default_signature";

        /// <summary>
        /// Last algorithm passed to SignChallenge.
        /// </summary>
        public string? LastAlgorithm { get; private set; }

        /// <summary>
        /// Last envelope passed to SignChallenge.
        /// </summary>
        public string? LastEnvelope { get; private set; }

        /// <summary>
        /// Last passphrase passed to SignChallenge.
        /// </summary>
        public string? LastPassphrase { get; private set; }

        /// <summary>
        /// Last nonce passed to SignChallenge.
        /// </summary>
        public byte[]? LastNonce { get; private set; }

        /// <summary>
        /// Optional exception to throw from SignChallenge.
        /// </summary>
        public Exception? ExceptionToThrow { get; set; }

        /// <inheritdoc/>
        public string SignChallenge(
            string keyAlgorithm,
            string encryptedEnvelopeBase64,
            ReadOnlySpan<char> passphrase,
            ReadOnlySpan<byte> challengeNonce)
        {
            // WHAT: Stubs challenge signing.
            // WHY:  Captures arguments and returns configured signature or throws.
            // HOW:  Stores properties and returns SignatureToReturn.
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            LastAlgorithm = keyAlgorithm;
            LastEnvelope = encryptedEnvelopeBase64;
            LastPassphrase = passphrase.ToString();
            LastNonce = challengeNonce.ToArray();
            return SignatureToReturn;
        }

        /// <inheritdoc/>
        public ClientKeyBundle GenerateKeyBundle(string keyAlgorithm, ReadOnlySpan<char> passphrase)
        {
            // WHAT: Stubs key bundle generation.
            // WHY:  Satisfies interface requirement.
            // HOW:  Returns mock bundle.
            return new ClientKeyBundle(keyAlgorithm, "pub", "enc", "fingerprint_123");
        }

        /// <inheritdoc/>
        public string SealPrivateKeyPem(string privateKeyPem, ReadOnlySpan<char> passphrase)
        {
            // WHAT: Stubs private key envelope sealing.
            // WHY:  Satisfies interface requirement.
            // HOW:  Returns dummy envelope string.
            return "sealed_env";
        }
    }
}
