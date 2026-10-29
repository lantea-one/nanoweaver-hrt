using System.Net;
using System.Text;
using System.Text.Json;
using Moq;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Security;
using NanoWeaverHrt.Client.Storage;
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
                if (path.EndsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    throw kvp.Value;
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
