using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Integration test suite validating the complete public API authentication, authorization,
/// access gating boundaries, first-launch setup wizard lockout, and user management endpoints.
/// </summary>
public sealed class AuthEndpointsIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    /// <summary>
    /// Temporary directory isolated for SQLite database fixtures.
    /// </summary>
    private readonly string _tempDataDir;

    /// <summary>
    /// Isolated WebApplicationFactory instance targeting isolated database directory.
    /// </summary>
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>
    /// Unauthenticated HTTP client for initial lockout and setup verification.
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthEndpointsIntegrationTests"/> class.
    /// </summary>
    /// <param name="baseFactory">Base application factory fixture.</param>
    public AuthEndpointsIntegrationTests(WebApplicationFactory<Program> baseFactory)
    {
        _tempDataDir = Path.Combine(Path.GetTempPath(), "weaver_auth_endpoint_tests_" + Guid.NewGuid().ToString("N"));
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
    /// Verifies the complete end-to-end authentication lifecycle:
    /// 1. Uninitialized node returns 503 Service Unavailable on protected endpoints.
    /// 2. First-launch setup wizard provisions master Administrator.
    /// 3. Duplicate setup calls are rejected with 400 Bad Request.
    /// 4. Challenge-response authentication exchanges signed nonce for Bearer token.
    /// 5. Direct private key login issues token and sets session cookie.
    /// 6. Access gating and role permissions strictly govern endpoint access.
    /// 7. Sole administrator cannot be deleted.
    /// 8. Patient can generate and download Digital DNA package.
    /// </summary>
    [Fact]
    public async Task CompleteAuthenticationAndRoleGatingLifecycle_Succeeds()
    {
        // ---------------------------------------------------------------------
        // STEP 1: Verify First-Launch 503 Lockout
        // ---------------------------------------------------------------------
        // WHAT: Call protected API endpoints on uninitialized node.
        // WHY:  Proves the system strictly prevents functioning until the master Administrator is established.
        // HOW:  GET /api/datasets and POST /api/parser/process on clean test directory.
        HttpResponseMessage lockoutResponse1 = await _client.GetAsync("/api/datasets");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lockoutResponse1.StatusCode);
        string lockoutBody1 = await lockoutResponse1.Content.ReadAsStringAsync();
        Assert.Contains("\"setupRequired\": true", lockoutBody1);

        HttpResponseMessage lockoutResponse2 = await _client.PostAsync("/api/parser/process/dna_nanotube_sample.xyz", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lockoutResponse2.StatusCode);

        // Setup status should report required
        HttpResponseMessage statusRes = await _client.GetAsync("/api/setup/status");
        Assert.Equal(HttpStatusCode.OK, statusRes.StatusCode);
        SetupStatusResponse? status = await statusRes.Content.ReadFromJsonAsync<SetupStatusResponse>();
        Assert.NotNull(status);
        Assert.True(status.SetupRequired);

        // ---------------------------------------------------------------------
        // STEP 2: Generate Setup Keypair & Initialize Master Administrator
        // ---------------------------------------------------------------------
        // WHAT: Calls /api/setup/generate-keypair and /api/setup/initialize.
        // WHY:  Bootstraps the node's authoritative cryptographic identity.
        // HOW:  Generates an Ed25519 keypair and invokes CompleteInitialSetupAsync via REST API.
        GenerateKeyRequest keyGenReq = new()
        {
            Username = "super_admin",
            Role = UserRole.Administrator,
            AccessGate = AccessGate.ApiWeb,
            Algorithm = KeyAlgorithm.Ed25519
        };

        HttpResponseMessage keyGenRes = await _client.PostAsJsonAsync("/api/setup/generate-keypair", keyGenReq);
        Assert.Equal(HttpStatusCode.OK, keyGenRes.StatusCode);
        GeneratedKeyBundle? adminBundle = await keyGenRes.Content.ReadFromJsonAsync<GeneratedKeyBundle>();
        Assert.NotNull(adminBundle);
        Assert.Equal("super_admin", adminBundle.Username);
        Assert.Equal(KeyAlgorithm.Ed25519, adminBundle.Algorithm);

        InitialSetupRequest initReq = new()
        {
            AdminUsername = "super_admin",
            PublicKeyBase64 = adminBundle.PublicKeyBase64,
            Algorithm = KeyAlgorithm.Ed25519,
            AccessGate = AccessGate.ApiWeb
        };

        HttpResponseMessage initRes = await _client.PostAsJsonAsync("/api/setup/initialize", initReq);
        Assert.Equal(HttpStatusCode.OK, initRes.StatusCode);
        AuthTokenResponse? adminAuth = await initRes.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(adminAuth);
        Assert.False(string.IsNullOrEmpty(adminAuth.Token));
        Assert.Equal("super_admin", adminAuth.User?.Username);
        Assert.Equal(UserRole.Administrator, adminAuth.User?.Role);

        // Verify setup status is now false
        HttpResponseMessage postStatusRes = await _client.GetAsync("/api/setup/status");
        SetupStatusResponse? postStatus = await postStatusRes.Content.ReadFromJsonAsync<SetupStatusResponse>();
        Assert.NotNull(postStatus);
        Assert.False(postStatus.SetupRequired);

        // Re-initialization must now be rejected with 400 Bad Request
        HttpResponseMessage duplicateInit = await _client.PostAsJsonAsync("/api/setup/initialize", initReq);
        Assert.Equal(HttpStatusCode.BadRequest, duplicateInit.StatusCode);

        // ---------------------------------------------------------------------
        // STEP 3: Verify Unauthenticated Requests to Protected Endpoints Return 401
        // ---------------------------------------------------------------------
        HttpResponseMessage unauthDatasets = await _client.GetAsync("/api/datasets");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthDatasets.StatusCode);

        // ---------------------------------------------------------------------
        // STEP 4: Authenticate Master Admin using Bearer Token
        // ---------------------------------------------------------------------
        using HttpClient adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminAuth.Token);

        HttpResponseMessage authedDatasets = await adminClient.GetAsync("/api/datasets");
        Assert.Equal(HttpStatusCode.OK, authedDatasets.StatusCode);

        HttpResponseMessage meRes = await adminClient.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meRes.StatusCode);
        UserAccount? me = await meRes.Content.ReadFromJsonAsync<UserAccount>();
        Assert.NotNull(me);
        Assert.Equal("super_admin", me.Username);
        Assert.Equal(UserRole.Administrator, me.Role);

        // ---------------------------------------------------------------------
        // STEP 5: Challenge-Response Authentication Handshake
        // ---------------------------------------------------------------------
        // WHAT: Tests /api/auth/challenge and /api/auth/token.
        // WHY:  Proves client can authenticate by signing challenge nonce.
        // HOW:  Calls challenge, signs nonce with Ed25519 private key, and exchanges for token.
        ChallengeRequest chalReq = new() { Username = "super_admin" };
        HttpResponseMessage chalRes = await _client.PostAsJsonAsync("/api/auth/challenge", chalReq);
        Assert.Equal(HttpStatusCode.OK, chalRes.StatusCode);
        AuthChallenge? challenge = await chalRes.Content.ReadFromJsonAsync<AuthChallenge>();
        Assert.NotNull(challenge);

        byte[] nonce = Convert.FromBase64String(challenge.NonceBase64);
        string signature = SignEd25519(adminBundle.PrivateKeyPem, nonce);

        TokenExchangeRequest tokenReq = new()
        {
            Username = "super_admin",
            ChallengeId = challenge.ChallengeId,
            SignatureBase64 = signature
        };

        HttpResponseMessage tokenExchangeRes = await _client.PostAsJsonAsync("/api/auth/token", tokenReq);
        Assert.Equal(HttpStatusCode.OK, tokenExchangeRes.StatusCode);
        AuthTokenResponse? exchangedToken = await tokenExchangeRes.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(exchangedToken);
        Assert.False(string.IsNullOrEmpty(exchangedToken.Token));

        // ---------------------------------------------------------------------
        // STEP 6: Direct Private Key Login & Session Cookie
        // ---------------------------------------------------------------------
        KeyLoginRequest keyLoginReq = new()
        {
            Username = "super_admin",
            PrivateKeyPem = adminBundle.PrivateKeyPem
        };

        HttpResponseMessage keyLoginRes = await _client.PostAsJsonAsync("/api/auth/login-key", keyLoginReq);
        Assert.Equal(HttpStatusCode.OK, keyLoginRes.StatusCode);
        Assert.True(keyLoginRes.Headers.Contains("Set-Cookie"));

        // Logout clears cookie
        HttpResponseMessage logoutRes = await _client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.OK, logoutRes.StatusCode);

        // ---------------------------------------------------------------------
        // STEP 7: User Management - Administrator Provisions Team Members
        // ---------------------------------------------------------------------
        // 1. Provision Researcher User
        GenerateKeyRequest resKeyReq = new()
        {
            Username = "researcher_lead",
            Role = UserRole.Researcher,
            AccessGate = AccessGate.ApiWeb,
            Algorithm = KeyAlgorithm.Ed25519
        };

        HttpResponseMessage resGenRes = await adminClient.PostAsJsonAsync("/api/users/generate-keypair", resKeyReq);
        Assert.Equal(HttpStatusCode.OK, resGenRes.StatusCode);
        GeneratedKeyBundle? resBundle = await resGenRes.Content.ReadFromJsonAsync<GeneratedKeyBundle>();
        Assert.NotNull(resBundle);

        // 2. Provision Patient User
        GenerateKeyRequest patKeyReq = new()
        {
            Username = "patient_007",
            Role = UserRole.Patient,
            AccessGate = AccessGate.ApiWeb,
            Algorithm = KeyAlgorithm.Ed25519
        };

        HttpResponseMessage patGenRes = await adminClient.PostAsJsonAsync("/api/users/generate-keypair", patKeyReq);
        Assert.Equal(HttpStatusCode.OK, patGenRes.StatusCode);
        GeneratedKeyBundle? patBundle = await patGenRes.Content.ReadFromJsonAsync<GeneratedKeyBundle>();
        Assert.NotNull(patBundle);

        // 3. Provision Web-only User
        GenerateKeyRequest webKeyReq = new()
        {
            Username = "web_only_patient",
            Role = UserRole.Patient,
            AccessGate = AccessGate.Web,
            Algorithm = KeyAlgorithm.Ed25519
        };

        HttpResponseMessage webGenRes = await adminClient.PostAsJsonAsync("/api/users/generate-keypair", webKeyReq);
        Assert.Equal(HttpStatusCode.OK, webGenRes.StatusCode);
        GeneratedKeyBundle? webBundle = await webGenRes.Content.ReadFromJsonAsync<GeneratedKeyBundle>();
        Assert.NotNull(webBundle);

        // List Users (Admin only)
        HttpResponseMessage listRes = await adminClient.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        UserAccount[]? users = await listRes.Content.ReadFromJsonAsync<UserAccount[]>();
        Assert.NotNull(users);
        Assert.True(users.Length >= 4);

        // ---------------------------------------------------------------------
        // STEP 8: Access Gating Restrictions (Web-only blocked from API)
        // ---------------------------------------------------------------------
        // Login as Web-only user to obtain token
        KeyLoginRequest webLoginReq = new()
        {
            Username = "web_only_patient",
            PrivateKeyPem = webBundle.PrivateKeyPem
        };

        // Note: /api/auth/login-key grants a browser session cookie, so it is the web transport (isWebRequest=true)
        // and ACCEPTS Web-only users. The API transport (/api/auth/token) is what refuses them.
        HttpResponseMessage webApiLoginRes = await _client.PostAsJsonAsync("/api/auth/login-key", webLoginReq);
        Assert.Equal(HttpStatusCode.OK, webApiLoginRes.StatusCode);

        // ---------------------------------------------------------------------
        // STEP 9: Role Gating Restrictions
        // ---------------------------------------------------------------------
        // Login as Researcher
        KeyLoginRequest resLogin = new() { Username = "researcher_lead", PrivateKeyPem = resBundle.PrivateKeyPem };
        HttpResponseMessage resLoginRes = await _client.PostAsJsonAsync("/api/auth/login-key", resLogin);
        AuthTokenResponse? resAuth = await resLoginRes.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(resAuth);

        using HttpClient researcherClient = _factory.CreateClient();
        researcherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", resAuth.Token);

        // Researcher CAN view datasets and execute parser
        HttpResponseMessage resDatasets = await researcherClient.GetAsync("/api/datasets");
        Assert.Equal(HttpStatusCode.OK, resDatasets.StatusCode);

        HttpResponseMessage resParser = await researcherClient.PostAsync("/api/parser/process/dna_nanotube_sample.xyz", null);
        Assert.Equal(HttpStatusCode.OK, resParser.StatusCode);

        // Researcher CANNOT access user management (Admin only)
        HttpResponseMessage resUsers = await researcherClient.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Forbidden, resUsers.StatusCode);

        // Login as Patient
        KeyLoginRequest patLogin = new() { Username = "patient_007", PrivateKeyPem = patBundle.PrivateKeyPem };
        HttpResponseMessage patLoginRes = await _client.PostAsJsonAsync("/api/auth/login-key", patLogin);
        AuthTokenResponse? patAuth = await patLoginRes.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(patAuth);

        using HttpClient patientClient = _factory.CreateClient();
        patientClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", patAuth.Token);

        // Patient CAN view datasets
        HttpResponseMessage patDatasets = await patientClient.GetAsync("/api/datasets");
        Assert.Equal(HttpStatusCode.OK, patDatasets.StatusCode);

        // Patient CANNOT access user management
        HttpResponseMessage patUsers = await patientClient.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Forbidden, patUsers.StatusCode);

        // Patient CANNOT trigger parser execution
        HttpResponseMessage patParser = await patientClient.PostAsync("/api/parser/process/dna_nanotube_sample.xyz", null);
        Assert.Equal(HttpStatusCode.Forbidden, patParser.StatusCode);

        // ---------------------------------------------------------------------
        // STEP 10: Patient Digital DNA Generation & Download
        // ---------------------------------------------------------------------
        // WHAT: Calls GET /api/patient/dna with omitted, self, invalid, cross-subject, and administrator scoping.
        // WHY:  Allows Patients to retrieve their personalized DNA origami nanocarrier sequences while preventing
        //       unauthorized subject enumeration or malformed identifier injection.
        // HOW:  Queries with omitted ID (defaults to self), valid self ID, invalid IDs, cross-subject (404), and Admin access.
        // 10a. Omitted patientId defaults to currentUser.Username
        HttpResponseMessage defaultDnaRes = await patientClient.GetAsync("/api/patient/dna");
        Assert.Equal(HttpStatusCode.OK, defaultDnaRes.StatusCode);
        PatientDigitalDnaPackage? defaultPackage = await defaultDnaRes.Content.ReadFromJsonAsync<PatientDigitalDnaPackage>();
        Assert.NotNull(defaultPackage);
        Assert.Equal("patient_007", defaultPackage.PatientId);

        // 10b. Explicit self patientId with custom parameters
        HttpResponseMessage dnaRes = await patientClient.GetAsync("/api/patient/dna?patientId=patient_007&regimen=MaleToFemale&geometry=Hexagonal%20Nanotube%20(6HB)&targetKd=3.5");
        Assert.Equal(HttpStatusCode.OK, dnaRes.StatusCode);
        PatientDigitalDnaPackage? dnaPackage = await dnaRes.Content.ReadFromJsonAsync<PatientDigitalDnaPackage>();
        Assert.NotNull(dnaPackage);
        Assert.Equal("patient_007", dnaPackage.PatientId);
        Assert.Equal(TherapeuticRegimen.MaleToFemale, dnaPackage.Regimen);
        Assert.NotEmpty(dnaPackage.Staples);
        Assert.NotEmpty(dnaPackage.AnnealingProtocol);
        Assert.False(string.IsNullOrEmpty(dnaPackage.ProvenanceSha256));

        // 10c. Invalid patientId formats return 400 Bad Request
        HttpResponseMessage badIdRes = await patientClient.GetAsync("/api/patient/dna?patientId=bad/id!");
        Assert.Equal(HttpStatusCode.BadRequest, badIdRes.StatusCode);
        Assert.Equal("application/problem+json", badIdRes.Content.Headers.ContentType?.MediaType);

        HttpResponseMessage longIdRes = await patientClient.GetAsync($"/api/patient/dna?patientId={new string('a', 65)}");
        Assert.Equal(HttpStatusCode.BadRequest, longIdRes.StatusCode);
        Assert.Equal("application/problem+json", longIdRes.Content.Headers.ContentType?.MediaType);

        // 10d. Non-Admin (Patient or Researcher) requesting someone else returns 404 Not Found problem details
        HttpResponseMessage patientSomeoneElse = await patientClient.GetAsync("/api/patient/dna?patientId=someone_else");
        Assert.Equal(HttpStatusCode.NotFound, patientSomeoneElse.StatusCode);
        Assert.Equal("application/problem+json", patientSomeoneElse.Content.Headers.ContentType?.MediaType);
        string notFoundBody = await patientSomeoneElse.Content.ReadAsStringAsync();
        Assert.Contains("The requested patient record was not found.", notFoundBody, StringComparison.Ordinal);

        HttpResponseMessage researcherSomeoneElse = await researcherClient.GetAsync("/api/patient/dna?patientId=someone_else");
        Assert.Equal(HttpStatusCode.NotFound, researcherSomeoneElse.StatusCode);
        Assert.Equal("application/problem+json", researcherSomeoneElse.Content.Headers.ContentType?.MediaType);

        // 10e. Administrator requesting another user's patientId succeeds with 200 OK
        HttpResponseMessage adminDnaRes = await adminClient.GetAsync("/api/patient/dna?patientId=patient_007");
        Assert.Equal(HttpStatusCode.OK, adminDnaRes.StatusCode);
        PatientDigitalDnaPackage? adminDnaPkg = await adminDnaRes.Content.ReadFromJsonAsync<PatientDigitalDnaPackage>();
        Assert.NotNull(adminDnaPkg);
        Assert.Equal("patient_007", adminDnaPkg.PatientId);

        // ---------------------------------------------------------------------
        // STEP 11: Sole Administrator Protection
        // ---------------------------------------------------------------------
        // Admin attempts to delete themselves (only active admin)
        HttpResponseMessage delSoleAdmin = await adminClient.DeleteAsync("/api/users/super_admin");
        Assert.Equal(HttpStatusCode.BadRequest, delSoleAdmin.StatusCode);

        // Admin can delete a researcher
        HttpResponseMessage delResearcher = await adminClient.DeleteAsync("/api/users/researcher_lead");
        Assert.Equal(HttpStatusCode.OK, delResearcher.StatusCode);
    }

    /// <summary>
    /// Helper signing raw bytes using Ed25519 private key PEM.
    /// </summary>
    private static string SignEd25519(string privateKeyPem, byte[] data)
    {
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
}
