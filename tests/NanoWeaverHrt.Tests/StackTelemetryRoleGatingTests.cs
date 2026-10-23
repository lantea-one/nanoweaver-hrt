using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Services.Stack;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Integration test suite verifying that the stack service telemetry routes are gated by role.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is tested:</strong> <c>GET /api/services</c> reports whether PostgreSQL, Valkey, oxDNA and
/// LAMMPS are reachable and how quickly they answer, and <c>POST /api/services/ping/{serviceId}</c> probes one
/// of them on demand.
/// </para>
/// <para>
/// <strong>Why it matters:</strong> that is infrastructure diagnostics. Prime Directive 12 gives cluster
/// oversight to Administrators and computational work to Researchers, while a Patient's mandate is their own
/// results and encrypted DNA payloads. A Patient token used to be able to map and probe the node's backing
/// services, which is both reconnaissance and a remote probe primitive.
/// </para>
/// </remarks>
public sealed class StackTelemetryRoleGatingTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    /// <summary>
    /// Temporary data directory isolating this class's databases, fixtures and node key files.
    /// </summary>
    private readonly string _tempDataDir;

    /// <summary>
    /// Isolated WebApplicationFactory instance.
    /// </summary>
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>
    /// Unauthenticated HTTP client used for setup and for the anonymous probe.
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="StackTelemetryRoleGatingTests"/> class.
    /// </summary>
    /// <param name="baseFactory">Base application factory fixture.</param>
    public StackTelemetryRoleGatingTests(WebApplicationFactory<Program> baseFactory)
    {
        _tempDataDir = Path.Combine(Path.GetTempPath(), "weaver_stack_gating_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDataDir);
        TestFixturesHelper.EnsureTestFixtures(_tempDataDir);

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("TRAJECTORY_INCOMING_DIR", _tempDataDir);
            builder.UseSetting("TRAJECTORY_EXPORT_DIR", Path.Combine(_tempDataDir, "exports"));
            builder.UseSetting("TRAJECTORY_PARQUET_DIR", Path.Combine(_tempDataDir, "parquet"));
            builder.UseSetting("POSTGRES_CONNECTION_STRING", string.Empty);
        });

        _client = _factory.CreateClient();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        if (Directory.Exists(_tempDataDir))
        {
            try
            {
                Directory.Delete(_tempDataDir, recursive: true);
            }
            catch (IOException)
            {
                // Ignored in test cleanup.
            }
        }
    }

    /// <summary>
    /// Provisions the master Administrator and returns its Bearer token.
    /// </summary>
    /// <returns>A signed Administrator token.</returns>
    private async Task<string> InitializeAdministratorAsync()
    {
        GenerateKeyRequest keyRequest = new()
        {
            Username = "stack_admin",
            Role = UserRole.Administrator,
            AccessGate = AccessGate.ApiWeb,
            Algorithm = KeyAlgorithm.Ed25519
        };

        HttpResponseMessage keyResponse = await _client.PostAsJsonAsync("/api/setup/generate-keypair", keyRequest);
        Assert.Equal(HttpStatusCode.OK, keyResponse.StatusCode);
        GeneratedKeyBundle? bundle = await keyResponse.Content.ReadFromJsonAsync<GeneratedKeyBundle>();
        Assert.NotNull(bundle);

        InitialSetupRequest setupRequest = new()
        {
            AdminUsername = "stack_admin",
            PublicKeyBase64 = bundle.PublicKeyBase64,
            Algorithm = KeyAlgorithm.Ed25519,
            AccessGate = AccessGate.ApiWeb
        };

        HttpResponseMessage setupResponse = await _client.PostAsJsonAsync("/api/setup/initialize", setupRequest);
        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
        AuthTokenResponse? auth = await setupResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(auth);
        return auth.Token;
    }

    /// <summary>
    /// Provisions an account with the requested role and signs it in.
    /// </summary>
    /// <param name="adminToken">Administrator token authorizing the account creation.</param>
    /// <param name="username">Account name to create.</param>
    /// <param name="role">Role to assign.</param>
    /// <returns>A signed Bearer token for the new account.</returns>
    private async Task<string> ProvisionAndSignInAsync(string adminToken, string username, UserRole role)
    {
        using HttpClient adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        GenerateKeyRequest keyRequest = new()
        {
            Username = username,
            Role = role,
            AccessGate = AccessGate.ApiWeb,
            Algorithm = KeyAlgorithm.Ed25519
        };

        HttpResponseMessage keyResponse = await adminClient.PostAsJsonAsync("/api/users/generate-keypair", keyRequest);
        Assert.Equal(HttpStatusCode.OK, keyResponse.StatusCode);
        GeneratedKeyBundle? bundle = await keyResponse.Content.ReadFromJsonAsync<GeneratedKeyBundle>();
        Assert.NotNull(bundle);

        KeyLoginRequest loginRequest = new()
        {
            Username = username,
            PrivateKeyPem = bundle.PrivateKeyPem
        };

        HttpResponseMessage loginResponse = await _client.PostAsJsonAsync("/api/auth/login-key", loginRequest);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        AuthTokenResponse? auth = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(auth);
        return auth.Token;
    }

    /// <summary>
    /// Creates a client carrying the supplied Bearer token.
    /// </summary>
    /// <param name="token">Bearer token to attach to every request.</param>
    /// <returns>An authenticated HTTP client.</returns>
    private HttpClient CreateTokenClient(string token)
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Verifies the stack telemetry gate for every role and for an anonymous caller.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    /// <remarks>
    /// Two declarative route-table entries match '/api/services': the research table (any verb) and the
    /// administrator mutation table (state-changing verbs). The resolver answers with the stricter of the two,
    /// so reading telemetry is Administrator or Researcher while the on-demand probe stays Administrator-only.
    /// </remarks>
    [Fact]
    public async Task StackServiceTelemetry_IsReadableByAdministratorAndResearcherOnly()
    {
        string adminToken = await InitializeAdministratorAsync();
        string researcherToken = await ProvisionAndSignInAsync(adminToken, "stack_researcher", UserRole.Researcher);
        string patientToken = await ProvisionAndSignInAsync(adminToken, "stack_patient", UserRole.Patient);

        // 1. Administrator: full cluster oversight.
        using HttpClient adminClient = CreateTokenClient(adminToken);
        HttpResponseMessage adminList = await adminClient.GetAsync("/api/services");
        Assert.Equal(HttpStatusCode.OK, adminList.StatusCode);
        StackServiceInfo[]? services = await adminList.Content.ReadFromJsonAsync<StackServiceInfo[]>();
        Assert.NotNull(services);
        Assert.NotEmpty(services);

        HttpResponseMessage adminPing = await adminClient.PostAsync("/api/services/ping/pg-duckdb", null);
        Assert.Equal(HttpStatusCode.OK, adminPing.StatusCode);

        // 2. Researcher: runs the simulations these services carry, so may read the telemetry — but the
        //    on-demand probe verb remains reserved for the Administrator, which is the stricter of the two
        //    route-table entries that match '/api/services'.
        using HttpClient researcherClient = CreateTokenClient(researcherToken);
        HttpResponseMessage researcherList = await researcherClient.GetAsync("/api/services");
        Assert.Equal(HttpStatusCode.OK, researcherList.StatusCode);

        HttpResponseMessage researcherPing = await researcherClient.PostAsync("/api/services/ping/simulation-engine", null);
        Assert.Equal(HttpStatusCode.Forbidden, researcherPing.StatusCode);

        // 3. Patient: no infrastructure mandate at all, for either verb.
        using HttpClient patientClient = CreateTokenClient(patientToken);
        HttpResponseMessage patientList = await patientClient.GetAsync("/api/services");
        Assert.Equal(HttpStatusCode.Forbidden, patientList.StatusCode);
        string patientBody = await patientList.Content.ReadAsStringAsync();
        Assert.Contains("urn:weaver:error:forbidden", patientBody);
        Assert.DoesNotContain("Host=", patientBody);
        Assert.DoesNotContain("/home/", patientBody);

        HttpResponseMessage patientPing = await patientClient.PostAsync("/api/services/ping/pg-duckdb", null);
        Assert.Equal(HttpStatusCode.Forbidden, patientPing.StatusCode);

        // 4. Anonymous callers are still refused before any role check happens.
        // NOTE: a fresh client is required here, because the sign-in calls above left a weaver_session cookie
        //       in the shared client's cookie container, which would make this request authenticated.
        using HttpClient anonymousClient = _factory.CreateClient();
        HttpResponseMessage anonymous = await anonymousClient.GetAsync("/api/services");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // 5. A Patient keeps the access their role is for, which proves the gate is targeted and not blanket.
        HttpResponseMessage patientDatasets = await patientClient.GetAsync("/api/datasets");
        Assert.Equal(HttpStatusCode.OK, patientDatasets.StatusCode);
    }

    /// <summary>
    /// Verifies that a token forged with the historic published signing key is now rejected with a clean 401.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task TokenForgedWithHistoricPublishedKey_IsRejectedWithUnauthorized()
    {
        // WHAT: Replays the pre-fix attack: build Administrator claims and sign them with the key anybody
        //       could recompute from this repository's source code.
        // WHY:  This is the defect being closed. It must now fail, and it must fail as a plain 401 carrying no
        //       stack trace, no path, no SQL and no key material (Prime Directive 14).
        // HOW:  Constructs the token exactly the way the service does, using the historic HMAC key.
        await InitializeAdministratorAsync();

        byte[] historicKey = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("node_signing_master:WEAVER_BEARER_TOKEN_SECRET_V1"));

        AuthTokenClaims claims = new()
        {
            Username = "stack_admin",
            Role = UserRole.Administrator,
            AccessGate = AccessGate.ApiWeb,
            IssuedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ExpiresAtUnixSeconds = DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeSeconds()
        };

        byte[] payloadBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(claims);
        byte[] signature = System.Security.Cryptography.HMACSHA256.HashData(historicKey, payloadBytes);
        string forgedToken = Base64Url(payloadBytes) + "." + Base64Url(signature);

        using HttpClient forgedClient = CreateTokenClient(forgedToken);
        HttpResponseMessage response = await forgedClient.GetAsync("/api/datasets");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Token signature verification failed.", body);
        Assert.DoesNotContain("   at ", body);
        Assert.DoesNotContain("/home/", body);
        Assert.DoesNotContain("SELECT", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Encodes bytes using the URL-safe, unpadded Base64 alphabet the token format uses.
    /// </summary>
    /// <param name="value">Bytes to encode.</param>
    /// <returns>The URL-safe Base64 text.</returns>
    private static string Base64Url(byte[] value)
    {
        return Convert.ToBase64String(value).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
