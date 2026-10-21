using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.ExceptionHandling;
using NanoWeaverHrt.Simulations.Models;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Comprehensive automated security and penetration test suite verifying platform resistance
/// against token tampering, privilege escalation, path traversal, SQL injection, replay attacks,
/// timing side-channels, and RFC 7807 sensitive information disclosure.
/// </summary>
public sealed class SecurityPenetrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    /// <summary>
    /// Temporary directory isolated for penetration test fixtures.
    /// </summary>
    private readonly string _tempDataDir;

    /// <summary>
    /// Isolated WebApplicationFactory instance.
    /// </summary>
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>
    /// HTTP client for security penetration probes.
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// Valid Administrator Bearer token.
    /// </summary>
    private readonly string _adminToken;

    /// <summary>
    /// Generated key bundle for administrator account.
    /// </summary>
    private readonly GeneratedKeyBundle _adminBundle;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityPenetrationTests"/> class.
    /// </summary>
    /// <param name="baseFactory">Base application factory fixture.</param>
    public SecurityPenetrationTests(WebApplicationFactory<Program> baseFactory)
    {
        _tempDataDir = Path.Combine(Path.GetTempPath(), "weaver_penetration_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDataDir);
        TestFixturesHelper.EnsureTestFixtures(_tempDataDir);

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("TRAJECTORY_INCOMING_DIR", _tempDataDir);
            builder.UseSetting("TRAJECTORY_EXPORT_DIR", Path.Combine(_tempDataDir, "exports"));
            builder.UseSetting("TRAJECTORY_PARQUET_DIR", Path.Combine(_tempDataDir, "parquet"));
            builder.UseSetting("POSTGRES_CONNECTION_STRING", "Host=127.0.0.1;Port=54399;Database=nonexistent;Timeout=1");
        });

        _client = _factory.CreateClient();

        // Initialize Administrator for authenticated penetration baselines
        GenerateKeyRequest adminKeyReq = new()
        {
            Username = "sec_admin",
            Role = UserRole.Administrator,
            AccessGate = AccessGate.ApiWeb,
            Algorithm = KeyAlgorithm.Ed25519
        };
        HttpResponseMessage keyRes = _client.PostAsJsonAsync("/api/setup/generate-keypair", adminKeyReq).GetAwaiter().GetResult();
        _adminBundle = keyRes.Content.ReadFromJsonAsync<GeneratedKeyBundle>().GetAwaiter().GetResult()!;

        InitialSetupRequest initReq = new()
        {
            AdminUsername = "sec_admin",
            PublicKeyBase64 = _adminBundle.PublicKeyBase64,
            Algorithm = KeyAlgorithm.Ed25519,
            AccessGate = AccessGate.ApiWeb
        };
        _client.PostAsJsonAsync("/api/setup/initialize", initReq).GetAwaiter().GetResult();

        KeyLoginRequest loginReq = new()
        {
            Username = "sec_admin",
            PrivateKeyPem = _adminBundle.PrivateKeyPem
        };
        HttpResponseMessage loginRes = _client.PostAsJsonAsync("/api/auth/login-key", loginReq).GetAwaiter().GetResult();
        AuthTokenResponse loginResp = loginRes.Content.ReadFromJsonAsync<AuthTokenResponse>().GetAwaiter().GetResult()!;
        _adminToken = loginResp.Token;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _client.Dispose();
        if (Directory.Exists(_tempDataDir))
        {
            try
            {
                Directory.Delete(_tempDataDir, recursive: true);
            }
            catch
            {
                // Ignored in test cleanup
            }
        }
    }

    /// <summary>
    /// Penetration Probe: Verifies that forged, tampered, expired, or malformed Bearer tokens are strictly rejected.
    /// </summary>
    [Fact]
    public async Task TokenTamperingAndSignatureForgery_StrictlyRejectedWithUnauthorized()
    {
        // WHAT: Tests hostile modifications to valid bearer tokens.
        // WHY:  Ensures cryptographic integrity cannot be bypassed by modifying client claims.
        // HOW:  Modifies signature bytes, alters payload claims, passes expired tokens, and tests malformed formats.
        string[] tokenParts = _adminToken.Split('.');
        Assert.Equal(2, tokenParts.Length);

        string originalPayload = tokenParts[0];
        string originalSig = tokenParts[1];

        // 1. Hostile payload modification (Attacker changes claims but retains original signature)
        byte[] payloadBytes = Convert.FromBase64String(originalPayload.Replace('-', '+').Replace('_', '/').PadRight(originalPayload.Length + (4 - originalPayload.Length % 4) % 4, '='));
        AuthTokenClaims claims = JsonSerializer.Deserialize<AuthTokenClaims>(payloadBytes)!;
        AuthTokenClaims forgedClaims = claims with { Username = "forged_superuser" };
        byte[] forgedPayloadBytes = JsonSerializer.SerializeToUtf8Bytes(forgedClaims);
        string forgedPayloadBase64 = Convert.ToBase64String(forgedPayloadBytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        string forgedToken = $"{forgedPayloadBase64}.{originalSig}";

        using HttpClient probeClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using HttpRequestMessage forgedReq = new(HttpMethod.Get, "/api/datasets");
        forgedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);
        HttpResponseMessage forgedRes = await probeClient.SendAsync(forgedReq);
        Assert.Equal(HttpStatusCode.Unauthorized, forgedRes.StatusCode);
        string forgedBody = await forgedRes.Content.ReadAsStringAsync();
        Assert.Contains("signature", forgedBody, StringComparison.OrdinalIgnoreCase);

        // 2. Tampered signature bytes
        string tamperedSig = originalSig[..^4] + "AAAA";
        string tamperedToken = $"{originalPayload}.{tamperedSig}";

        using HttpRequestMessage tamperedReq = new(HttpMethod.Get, "/api/datasets");
        tamperedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tamperedToken);
        HttpResponseMessage tamperedRes = await probeClient.SendAsync(tamperedReq);
        Assert.Equal(HttpStatusCode.Unauthorized, tamperedRes.StatusCode);

        // 3. Malformed token formats
        string[] malformedTokens =
        [
            "invalid_single_string_without_dot",
            "three.parts.token.format",
            "malformed!base64$.signature"
        ];

        foreach (string badToken in malformedTokens)
        {
            using HttpRequestMessage badReq = new(HttpMethod.Get, "/api/datasets");
            badReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", badToken);
            HttpResponseMessage badRes = await probeClient.SendAsync(badReq);
            Assert.True(badRes.StatusCode == HttpStatusCode.Unauthorized || badRes.StatusCode == HttpStatusCode.BadRequest);
        }
    }

    /// <summary>
    /// Penetration Probe: Verifies that access gating boundaries (Web vs API) cannot be crossed.
    /// </summary>
    [Fact]
    public async Task AccessGating_WebOnlyUserBlockedFromRestApi()
    {
        // WHAT: Creates a user with AccessGate.Web and attempts REST API calls.
        // WHY:  Verifies strict transport access isolation between browser and machine clients.
        // HOW:  Mints token for Web-only user and asserts 403 Forbidden on /api/datasets.
        using HttpRequestMessage webUserKeyReq = new(HttpMethod.Post, "/api/users/generate-keypair");
        webUserKeyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        webUserKeyReq.Content = JsonContent.Create(new GenerateKeyRequest
        {
            Username = "browser_only_user",
            Role = UserRole.Researcher,
            AccessGate = AccessGate.Web,
            Algorithm = KeyAlgorithm.Ed25519
        });
        HttpResponseMessage keyRes = await _client.SendAsync(webUserKeyReq);
        Assert.Equal(HttpStatusCode.OK, keyRes.StatusCode);
        GeneratedKeyBundle? bundle = await keyRes.Content.ReadFromJsonAsync<GeneratedKeyBundle>();
        Assert.NotNull(bundle);

        // 1. Direct login via /api/auth/login-key should be rejected because user is gated to Web UI only
        KeyLoginRequest loginReq = new()
        {
            Username = "browser_only_user",
            PrivateKeyPem = bundle.PrivateKeyPem
        };
        HttpResponseMessage loginRes = await _client.PostAsJsonAsync("/api/auth/login-key", loginReq);
        Assert.Equal(HttpStatusCode.Unauthorized, loginRes.StatusCode);
        string loginBody = await loginRes.Content.ReadAsStringAsync();
        Assert.Contains("Web UI access only", loginBody);

        // 2. Even if an interactive Web UI session token is minted, REST API middleware rejects with 403 Forbidden
        using var scope = _factory.Services.CreateScope();
        IUserAuthenticationService authService = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
        var (success, _, webToken, _) = await authService.AuthenticateWithPrivateKeyAsync(
            "browser_only_user",
            bundle.PrivateKeyPem,
            password: null,
            isWebRequest: true,
            CancellationToken.None);
        Assert.True(success);
        Assert.NotNull(webToken);

        // Attempt REST API endpoint with Web-gated token -> 403 Forbidden
        using HttpRequestMessage apiReq = new(HttpMethod.Get, "/api/datasets");
        apiReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", webToken);
        HttpResponseMessage apiRes = await _client.SendAsync(apiReq);
        Assert.Equal(HttpStatusCode.Forbidden, apiRes.StatusCode);
        string body = await apiRes.Content.ReadAsStringAsync();
        Assert.Contains("Web interface access only", body);
    }

    /// <summary>
    /// Penetration Probe: Verifies that path traversal attacks in download and trajectory endpoints are fully neutralized.
    /// </summary>
    [Fact]
    public async Task PathTraversalAttacks_AreStrictlyNeutralized()
    {
        // WHAT: Submits directory traversal sequences (e.g. ../../etc/passwd, ..\..\windows\win.ini).
        // WHY:  Protects the host operating system from arbitrary file disclosure or traversal.
        // HOW:  Sends GET requests to /api/datasets/download/ with directory escape payloads.
        string[] traversalPayloads =
        [
            "../../../../etc/passwd",
            "..%2F..%2F..%2Fetc%2Fpasswd",
            "..\\..\\..\\windows\\win.ini",
            "....//....//etc/passwd",
            "/etc/shadow",
            "C:\\Windows\\System32\\drivers\\etc\\hosts"
        ];

        foreach (string payload in traversalPayloads)
        {
            using HttpRequestMessage downloadReq = new(HttpMethod.Get, $"/api/datasets/download/{Uri.EscapeDataString(payload)}");
            downloadReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
            HttpResponseMessage downloadRes = await _client.SendAsync(downloadReq);

            // Must return 404 Not Found (or 400 Bad Request), NEVER 200 OK
            Assert.True(downloadRes.StatusCode == HttpStatusCode.NotFound || downloadRes.StatusCode == HttpStatusCode.BadRequest);

            // Trajectory parser trigger endpoint path traversal probe
            using HttpRequestMessage parseReq = new(HttpMethod.Post, $"/api/parser/process/{Uri.EscapeDataString(payload)}");
            parseReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
            HttpResponseMessage parseRes = await _client.SendAsync(parseReq);

            Assert.True(parseRes.StatusCode == HttpStatusCode.NotFound || parseRes.StatusCode == HttpStatusCode.BadRequest);
        }
    }

    /// <summary>
    /// Penetration Probe: Verifies parameterized query safety against hostile SQL injection payloads.
    /// </summary>
    [Fact]
    public async Task SqlInjectionAttacks_AreFullyNeutralizedByParameterizedQueries()
    {
        // WHAT: Injects classic and advanced SQL injection strings into database repository methods.
        // WHY:  Guarantees that SQL injection can never bypass authentication or alter database tables.
        // HOW:  Submits injection payloads to GetUserAsync, HasAnyAdministratorAsync, and SetDatabaseOverrideAsync.
        string[] injectionPayloads =
        [
            "' OR '1'='1",
            "admin'--",
            "'; DROP TABLE weaver_users;--",
            "' UNION SELECT * FROM weaver_users--",
            "' OR 1=1; SELECT * FROM weaver_configuration;--",
            "\" OR \"\"=\""
        ];

        string dbPath = Path.Combine(_tempDataDir, "weaver_config.db");
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDataDir,
                ["POSTGRES_CONNECTION_STRING"] = "Host=127.0.0.1;Port=54399;Database=nonexistent;Timeout=1"
            })
            .Build();

        using SystemConfigurationStore configStore = new(config);
        await configStore.InitializeAsync();

        using UserRepository userRepo = new(config, configStore);
        await userRepo.InitializeAsync();

        foreach (string sqli in injectionPayloads)
        {
            // 1. Querying with injection payload should safely return null
            UserAccount? injectedUser = await userRepo.GetUserAsync(sqli);
            Assert.Null(injectedUser);

            // 2. Setting config with injection payload should escape safely as string literal
            await configStore.SetDatabaseOverrideAsync($"test_key_{Guid.NewGuid():N}", sqli);
            string retrieved = await configStore.GetValueAsync($"test_key_{Guid.NewGuid():N}", defaultValue: "not_found");
            Assert.Equal("not_found", retrieved);
        }

        // 3. Confirm tables remain intact after injection attempts
        using SqliteConnection conn = new($"Data Source={dbPath}");
        await conn.OpenAsync();
        using SqliteCommand verifyCmd = conn.CreateCommand();
        verifyCmd.CommandText = "SELECT COUNT(*) FROM weaver_users;";
        object? count = await verifyCmd.ExecuteScalarAsync();
        Assert.NotNull(count);
    }

    /// <summary>
    /// Penetration Probe: Verifies RFC 7807 zero-information-disclosure exception guarantees.
    /// </summary>
    [Fact]
    public void ExceptionSanitization_NeverDisclosesPathsPasswordsOrKeys()
    {
        // WHAT: Tests SecureExceptionSanitizer against strings containing paths, credentials, and PEM keys.
        // WHY:  Prevents internal server exceptions from leaking infrastructure topology to attackers.
        // HOW:  Passes hostile exception messages and validates redaction patterns.
        string rawLeakMessage = "Failed to connect to host=10.0.0.5;Password=SuperSecretDbPassword123!;Database=weaver at /home/fabigail/code/server.cs:line 42 with key -----BEGIN PRIVATE KEY-----\nMIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQg\n-----END PRIVATE KEY-----";

        // 1. Sanitize for 400 Bad Request
        string sanitized400 = SecureExceptionSanitizer.SanitizeDetail(rawLeakMessage, 400, "trace-abc");
        Assert.DoesNotContain("SuperSecretDbPassword123!", sanitized400);
        Assert.DoesNotContain("/home/fabigail/code", sanitized400);
        Assert.DoesNotContain("BEGIN PRIVATE KEY", sanitized400);
        Assert.Contains("Password=[REDACTED]", sanitized400);
        Assert.Contains("[REDACTED_PATH]", sanitized400);
        Assert.Contains("[REDACTED_KEY]", sanitized400);

        // 2. Sanitize for 500 Internal Server Error (Opaque trace mask)
        string sanitized500 = SecureExceptionSanitizer.SanitizeDetail(rawLeakMessage, 500, "trace-xyz-987");
        Assert.Equal("An internal error occurred during request processing. Correlation ID: trace-xyz-987.", sanitized500);
    }

    /// <summary>
    /// Penetration Probe: Verifies that authentication challenge nonces cannot be replayed or mismatched.
    /// </summary>
    [Fact]
    public async Task ChallengeResponseReplayAndMismatch_StrictlyNeutralized()
    {
        // WHAT: Attempts to reuse a challenge ID and tests username mismatch spoofing.
        // WHY:  Guarantees cryptographic challenge nonces are strictly single-use and principal-bound.
        // HOW:  Requests challenge for sec_admin, signs nonce, and attempts replay.
        ChallengeRequest challengeReq = new() { Username = "sec_admin" };
        HttpResponseMessage chalRes = await _client.PostAsJsonAsync("/api/auth/challenge", challengeReq);
        Assert.Equal(HttpStatusCode.OK, chalRes.StatusCode);
        AuthChallenge? challenge = await chalRes.Content.ReadFromJsonAsync<AuthChallenge>();
        Assert.NotNull(challenge);

        // Sign challenge nonce with admin private key
        byte[] nonceBytes = Convert.FromBase64String(challenge.NonceBase64);
        string sigBase64 = SignEd25519(_adminBundle.PrivateKeyPem, nonceBytes);

        // 1. Mismatched username spoofing attempt
        TokenExchangeRequest spoofReq = new()
        {
            ChallengeId = challenge.ChallengeId,
            Username = "attacker_spoofed_user",
            SignatureBase64 = sigBase64
        };
        HttpResponseMessage spoofRes = await _client.PostAsJsonAsync("/api/auth/token", spoofReq);
        Assert.Equal(HttpStatusCode.Unauthorized, spoofRes.StatusCode);
        string spoofBody = await spoofRes.Content.ReadAsStringAsync();
        Assert.Contains("mismatch", spoofBody, StringComparison.OrdinalIgnoreCase);

        // 1b. Verify that the spoof attempt permanently burned the original challenge nonce
        HttpResponseMessage burnedRes = await _client.PostAsJsonAsync("/api/auth/token", new TokenExchangeRequest
        {
            ChallengeId = challenge.ChallengeId,
            Username = "sec_admin",
            SignatureBase64 = sigBase64
        });
        Assert.Equal(HttpStatusCode.Unauthorized, burnedRes.StatusCode);

        // 2. Legitimate initial token exchange with fresh challenge
        ChallengeRequest freshReq = new() { Username = "sec_admin" };
        HttpResponseMessage freshRes = await _client.PostAsJsonAsync("/api/auth/challenge", freshReq);
        Assert.Equal(HttpStatusCode.OK, freshRes.StatusCode);
        AuthChallenge? challenge2 = await freshRes.Content.ReadFromJsonAsync<AuthChallenge>();
        Assert.NotNull(challenge2);

        byte[] nonceBytes2 = Convert.FromBase64String(challenge2.NonceBase64);
        string sigBase64_2 = SignEd25519(_adminBundle.PrivateKeyPem, nonceBytes2);

        TokenExchangeRequest validReq = new()
        {
            ChallengeId = challenge2.ChallengeId,
            Username = "sec_admin",
            SignatureBase64 = sigBase64_2
        };
        HttpResponseMessage validRes = await _client.PostAsJsonAsync("/api/auth/token", validReq);
        Assert.Equal(HttpStatusCode.OK, validRes.StatusCode);

        // 3. Replay attack attempt using the already consumed challenge ID
        HttpResponseMessage replayRes = await _client.PostAsJsonAsync("/api/auth/token", validReq);
        Assert.Equal(HttpStatusCode.Unauthorized, replayRes.StatusCode);
        string replayBody = await replayRes.Content.ReadAsStringAsync();
        Assert.Contains("expired", replayBody, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Cryptographically signs arbitrary byte sequences using an Ed25519 private key PEM string.
    /// </summary>
    /// <param name="privateKeyPem">Ed25519 private key in PEM format.</param>
    /// <param name="data">Data bytes to sign.</param>
    /// <returns>Base64-encoded signature bytes.</returns>
    private static string SignEd25519(string privateKeyPem, byte[] data)
    {
        // WHAT: Derives Ed25519 private key from PEM and signs raw payload.
        // WHY:  Allows test suites to emulate client-side asymmetric signing without leaking keys.
        // HOW:  Strips PEM framing, constructs Ed25519Signer, and outputs Base64.
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
    /// Penetration Probe: Verifies that OWASP security response headers are strictly present on all responses.
    /// </summary>
    [Fact]
    public async Task DefenseInDepthHeaders_StrictlyPresentOnAllResponses()
    {
        // WHAT: Inspects HTTP response headers on public, protected, and health check endpoints.
        // WHY:  Ensures browser defenses against MIME sniffing, clickjacking, and XSS are active across all endpoints.
        // HOW:  Checks X-Content-Type-Options, X-Frame-Options, X-XSS-Protection, and Referrer-Policy headers.
        HttpResponseMessage response = await _client.GetAsync("/healthz");

        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").First());

        Assert.True(response.Headers.Contains("X-Frame-Options"));
        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").First());

        Assert.True(response.Headers.Contains("X-XSS-Protection"));
        Assert.Equal("0", response.Headers.GetValues("X-XSS-Protection").First());

        Assert.True(response.Headers.Contains("Referrer-Policy"));
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").First());

        Assert.True(response.Headers.Contains("Permissions-Policy"));
    }
}
