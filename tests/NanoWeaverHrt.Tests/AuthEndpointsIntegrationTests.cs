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
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        // STEP 6: Direct Private Key Login & Session Cookie (via Challenge Handshake)
        // ---------------------------------------------------------------------
        HttpResponseMessage keyLoginRes = await TestChallengeHelper.LoginViaChallengeAsync(
            _client,
            "super_admin",
            adminBundle.PrivateKeyPem,
            useWebTokenEndpoint: true);
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
        // Login as Web-only user to obtain token via challenge-response
        HttpResponseMessage webApiLoginRes = await TestChallengeHelper.LoginViaChallengeAsync(
            _client,
            "web_only_patient",
            webBundle.PrivateKeyPem,
            useWebTokenEndpoint: true);
        Assert.Equal(HttpStatusCode.OK, webApiLoginRes.StatusCode);

        // ---------------------------------------------------------------------
        // STEP 9: Role Gating Restrictions
        // ---------------------------------------------------------------------
        // Login as Researcher
        HttpResponseMessage resLoginRes = await TestChallengeHelper.LoginViaChallengeAsync(
            _client,
            "researcher_lead",
            resBundle.PrivateKeyPem,
            useWebTokenEndpoint: true);
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
        HttpResponseMessage patLoginRes = await TestChallengeHelper.LoginViaChallengeAsync(
            _client,
            "patient_007",
            patBundle.PrivateKeyPem,
            useWebTokenEndpoint: true);
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

    /// <summary>
    /// Helper signing raw bytes using ML-DSA-65 private key PEM.
    /// </summary>
    private static string SignMlDsa65(string privateKeyPem, byte[] data)
    {
        string clean = privateKeyPem
            .Replace("-----BEGIN PRIVATE KEY-----", "")
            .Replace("-----END PRIVATE KEY-----", "")
            .Replace("\r", "")
            .Replace("\n", "")
            .Trim();

        PostQuantumCryptoProvider pqc = new();
        PqcSignature sig = pqc.SignData(data, clean, "test_signer");
        return sig.SignatureBase64;
    }

    /// <summary>
    /// Verifies that POST /api/auth/web-token accepts Web and ApiWeb accounts across both Ed25519 and ML-DSA-65,
    /// sets the HttpOnly weaver_session cookie with SameSite=Strict, and records isWeb=true in the audit ledger.
    /// </summary>
    [Fact]
    public async Task WebToken_AllowsWebAndApiWebAccounts_AndSetsHttpOnlyCookie()
    {
        // WHAT: Tests web-transport token exchange for Web and ApiWeb accounts with Ed25519 and ML-DSA-65.
        // WHY:  Proves browser UI login establishes an authenticated session without leaking private keys.
        // HOW:  Issues challenge, signs nonce, calls POST /api/auth/web-token, checks cookie and audit log.
        using IServiceScope scope = _factory.Services.CreateScope();
        IUserAuthenticationService auth = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
        IComplianceAuditService audit = scope.ServiceProvider.GetRequiredService<IComplianceAuditService>();

        if (await auth.IsSetupRequiredAsync())
        {
            GeneratedKeyBundle adminBundle = auth.GenerateSecureKeyBundle("web_admin", UserRole.Administrator, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
            await auth.CompleteInitialSetupAsync("web_admin", adminBundle.PublicKeyBase64, KeyAlgorithm.Ed25519, AccessGate.ApiWeb);
        }

        var testAccounts = new[]
        {
            ("web_ed25519_user", AccessGate.Web, KeyAlgorithm.Ed25519),
            ("web_mldsa_user", AccessGate.Web, KeyAlgorithm.MlDsa65),
            ("apiweb_ed25519_user", AccessGate.ApiWeb, KeyAlgorithm.Ed25519)
        };

        foreach (var (username, gate, algorithm) in testAccounts)
        {
            GeneratedKeyBundle bundle = auth.GenerateSecureKeyBundle(username, UserRole.Researcher, gate, algorithm);
            await auth.CreateUserAsync(username, UserRole.Researcher, gate, bundle.PublicKeyBase64, algorithm);

            // Step 1: Request challenge
            ChallengeRequest chalReq = new() { Username = username };
            HttpResponseMessage chalRes = await _client.PostAsJsonAsync("/api/auth/challenge", chalReq);
            Assert.Equal(HttpStatusCode.OK, chalRes.StatusCode);
            AuthChallenge? challenge = await chalRes.Content.ReadFromJsonAsync<AuthChallenge>();
            Assert.NotNull(challenge);

            // Step 2: Sign raw 32-byte nonce
            byte[] nonce = Convert.FromBase64String(challenge.NonceBase64);
            string signature = algorithm == KeyAlgorithm.Ed25519
                ? SignEd25519(bundle.PrivateKeyPem, nonce)
                : SignMlDsa65(bundle.PrivateKeyPem, nonce);

            // Step 3: Exchange for web token
            TokenExchangeRequest tokenReq = new()
            {
                Username = username,
                ChallengeId = challenge.ChallengeId,
                SignatureBase64 = signature
            };

            HttpResponseMessage webTokenRes = await _client.PostAsJsonAsync("/api/auth/web-token", tokenReq);
            Assert.Equal(HttpStatusCode.OK, webTokenRes.StatusCode);

            AuthTokenResponse? tokenResponse = await webTokenRes.Content.ReadFromJsonAsync<AuthTokenResponse>();
            Assert.NotNull(tokenResponse);
            Assert.False(string.IsNullOrEmpty(tokenResponse.Token));
            Assert.Equal(username, tokenResponse.User?.Username);

            // Verify Set-Cookie header
            Assert.True(webTokenRes.Headers.TryGetValues("Set-Cookie", out var cookieHeaders));
            string setCookie = string.Join(";", cookieHeaders);
            Assert.Contains("weaver_session=", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);

            // Verify audit ledger record
            IReadOnlyList<AuditRecord> records = audit.GetAuditRecords(50);
            Assert.Contains(records, r =>
                r.Action == "UserAuthenticated" &&
                r.Resource == $"user:{username}" &&
                r.DetailsJson.Contains("\"isWeb\":true", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Verifies that POST /api/auth/web-token rejects accounts gated strictly for API transport
    /// with 401 Unauthorized and does not emit a Set-Cookie header.
    /// </summary>
    [Fact]
    public async Task WebToken_RejectsApiOnlyAccount_With401AndNoCookie()
    {
        // WHAT: Attempts to authenticate an Api-only account on the web-token endpoint.
        // WHY:  Enforces transport boundary gating (P2 Invariant) so API keys cannot open browser sessions.
        // HOW:  Creates AccessGate.Api user, signs challenge, posts to /api/auth/web-token, asserts 401 and no cookie.
        using IServiceScope scope = _factory.Services.CreateScope();
        IUserAuthenticationService auth = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();

        if (await auth.IsSetupRequiredAsync())
        {
            GeneratedKeyBundle adminBundle = auth.GenerateSecureKeyBundle("gate_admin2", UserRole.Administrator, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
            await auth.CompleteInitialSetupAsync("gate_admin2", adminBundle.PublicKeyBase64, KeyAlgorithm.Ed25519, AccessGate.ApiWeb);
        }

        string apiUser = "api_only_user";
        GeneratedKeyBundle bundle = auth.GenerateSecureKeyBundle(apiUser, UserRole.Researcher, AccessGate.Api, KeyAlgorithm.Ed25519);
        await auth.CreateUserAsync(apiUser, UserRole.Researcher, AccessGate.Api, bundle.PublicKeyBase64, KeyAlgorithm.Ed25519);

        ChallengeRequest chalReq = new() { Username = apiUser };
        HttpResponseMessage chalRes = await _client.PostAsJsonAsync("/api/auth/challenge", chalReq);
        Assert.Equal(HttpStatusCode.OK, chalRes.StatusCode);
        AuthChallenge? challenge = await chalRes.Content.ReadFromJsonAsync<AuthChallenge>();
        Assert.NotNull(challenge);

        byte[] nonce = Convert.FromBase64String(challenge.NonceBase64);
        string signature = SignEd25519(bundle.PrivateKeyPem, nonce);

        TokenExchangeRequest tokenReq = new()
        {
            Username = apiUser,
            ChallengeId = challenge.ChallengeId,
            SignatureBase64 = signature
        };

        HttpResponseMessage webTokenRes = await _client.PostAsJsonAsync("/api/auth/web-token", tokenReq);
        Assert.Equal(HttpStatusCode.Unauthorized, webTokenRes.StatusCode);
        Assert.False(webTokenRes.Headers.Contains("Set-Cookie"));
    }

    /// <summary>
    /// Verifies same-origin enforcement on POST /api/auth/web-token across allowed local/reverse-proxy
    /// origins, cross-origin rejections, invalid URIs, Sec-Fetch-Site rules, and missing payload fields.
    /// </summary>
    [Fact]
    public async Task WebToken_ValidatesSameOriginAndConfiguredPublicOrigin()
    {
        // WHAT: Tests origin filtering, Sec-Fetch-Site header inspection, and payload validation.
        // WHY:  Protects the session-cookie-setting web-token endpoint against CSRF and cross-site fixation.
        // HOW:  Executes POST /api/auth/web-token with varied Origin, Sec-Fetch-Site headers, and payload structures.
        using IServiceScope scope = _factory.Services.CreateScope();
        IUserAuthenticationService auth = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
        IConfigurationRepository configRepo = scope.ServiceProvider.GetRequiredService<IConfigurationRepository>();

        if (await auth.IsSetupRequiredAsync())
        {
            GeneratedKeyBundle adminBundle = auth.GenerateSecureKeyBundle("origin_admin", UserRole.Administrator, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
            await auth.CompleteInitialSetupAsync("origin_admin", adminBundle.PublicKeyBase64, KeyAlgorithm.Ed25519, AccessGate.ApiWeb);
        }

        string username = "origin_test_user";
        GeneratedKeyBundle bundle = auth.GenerateSecureKeyBundle(username, UserRole.Researcher, AccessGate.Web, KeyAlgorithm.Ed25519);
        await auth.CreateUserAsync(username, UserRole.Researcher, AccessGate.Web, bundle.PublicKeyBase64, KeyAlgorithm.Ed25519);

        async Task<TokenExchangeRequest> CreateValidRequestAsync()
        {
            ChallengeRequest chalReq = new() { Username = username };
            HttpResponseMessage chalRes = await _client.PostAsJsonAsync("/api/auth/challenge", chalReq);
            AuthChallenge challenge = (await chalRes.Content.ReadFromJsonAsync<AuthChallenge>())!;
            byte[] nonce = Convert.FromBase64String(challenge.NonceBase64);
            string signature = SignEd25519(bundle.PrivateKeyPem, nonce);
            return new TokenExchangeRequest
            {
                Username = username,
                ChallengeId = challenge.ChallengeId,
                SignatureBase64 = signature
            };
        }

        // 1. Missing Origin header -> 200 OK
        {
            using HttpRequestMessage req = new(HttpMethod.Post, "/api/auth/web-token")
            {
                Content = JsonContent.Create(await CreateValidRequestAsync())
            };
            HttpResponseMessage res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }

        // 2. Origin matching Host (http://localhost) -> 200 OK
        {
            using HttpRequestMessage req = new(HttpMethod.Post, "/api/auth/web-token")
            {
                Content = JsonContent.Create(await CreateValidRequestAsync())
            };
            req.Headers.Add("Origin", "http://localhost");
            HttpResponseMessage res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }

        // 3. Origin matching Peer:CollaborativeWorkerEndpoint -> 200 OK
        {
            await configRepo.SetDatabaseOverrideAsync("Peer:CollaborativeWorkerEndpoint", "https://hrt.lantea.one");
            using HttpRequestMessage req = new(HttpMethod.Post, "/api/auth/web-token")
            {
                Content = JsonContent.Create(await CreateValidRequestAsync())
            };
            req.Headers.Add("Origin", "https://hrt.lantea.one");
            HttpResponseMessage res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }

        // 4. Origin matching DOMAIN environment variable -> 200 OK
        string? oldDomain = Environment.GetEnvironmentVariable("DOMAIN");
        try
        {
            Environment.SetEnvironmentVariable("DOMAIN", "proxy.lantea.one");
            using HttpRequestMessage req = new(HttpMethod.Post, "/api/auth/web-token")
            {
                Content = JsonContent.Create(await CreateValidRequestAsync())
            };
            req.Headers.Add("Origin", "https://proxy.lantea.one");
            HttpResponseMessage res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOMAIN", oldDomain);
        }

        // 5. Cross-origin request from evil.example.com -> 403 Forbidden
        {
            using HttpRequestMessage req = new(HttpMethod.Post, "/api/auth/web-token")
            {
                Content = JsonContent.Create(await CreateValidRequestAsync())
            };
            req.Headers.Add("Origin", "https://evil.example.com");
            HttpResponseMessage res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }

        // 6. Malformed Origin header -> 403 Forbidden
        {
            using HttpRequestMessage req = new(HttpMethod.Post, "/api/auth/web-token")
            {
                Content = JsonContent.Create(await CreateValidRequestAsync())
            };
            req.Headers.TryAddWithoutValidation("Origin", "://bad-uri");
            HttpResponseMessage res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }

        // 7. Sec-Fetch-Site: cross-site -> 403 Forbidden
        {
            using HttpRequestMessage req = new(HttpMethod.Post, "/api/auth/web-token")
            {
                Content = JsonContent.Create(await CreateValidRequestAsync())
            };
            req.Headers.Add("Sec-Fetch-Site", "cross-site");
            HttpResponseMessage res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }

        // 8. Missing request body fields -> 400 BadRequest
        {
            TokenExchangeRequest emptyUser = new() { Username = "", ChallengeId = "c1", SignatureBase64 = "s1" };
            HttpResponseMessage res = await _client.PostAsJsonAsync("/api/auth/web-token", emptyUser);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        {
            TokenExchangeRequest emptyChal = new() { Username = "user", ChallengeId = "", SignatureBase64 = "s1" };
            HttpResponseMessage res = await _client.PostAsJsonAsync("/api/auth/web-token", emptyChal);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        {
            TokenExchangeRequest emptySig = new() { Username = "user", ChallengeId = "c1", SignatureBase64 = "" };
            HttpResponseMessage res = await _client.PostAsJsonAsync("/api/auth/web-token", emptySig);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }
    }

    /// <summary>
    /// Verifies that POST /api/auth/challenge returns HTTP 429 TooManyRequests with problem details
    /// when the in-memory active challenge capacity is reached.
    /// </summary>
    [Fact]
    public async Task CreateChallengeEndpoint_WhenCapReached_Returns429TooManyRequests()
    {
        // WHAT: Validates 429 throttling on POST /api/auth/challenge when store reaches capacity.
        // WHY:  Protects unauthenticated challenge endpoint from memory exhaustion (Audit L9).
        // HOW:  Temporarily lowers MaxActiveChallenges to 1, creates 1st challenge (200), then 2nd (429).
        UserAuthenticationService authService = _factory.Services.GetRequiredService<UserAuthenticationService>();
        if (await authService.IsSetupRequiredAsync())
        {
            GeneratedKeyBundle adminBundle = authService.GenerateSecureKeyBundle("cap_admin", UserRole.Administrator, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
            await authService.CompleteInitialSetupAsync("cap_admin", adminBundle.PublicKeyBase64, KeyAlgorithm.Ed25519, AccessGate.ApiWeb);
        }

        int originalCap = authService.MaxActiveChallenges;

        try
        {
            authService.MaxActiveChallenges = 1;
            authService.PruneExpiredChallenges(DateTimeOffset.UtcNow.AddHours(1));

            // First challenge succeeds
            ChallengeRequest req1 = new() { Username = "throttle_test_user" };
            HttpResponseMessage res1 = await _client.PostAsJsonAsync("/api/auth/challenge", req1);
            Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

            // Second challenge triggers capacity overflow -> 429 Too Many Requests
            ChallengeRequest req2 = new() { Username = "throttle_test_user" };
            HttpResponseMessage res2 = await _client.PostAsJsonAsync("/api/auth/challenge", req2);
            Assert.Equal((HttpStatusCode)429, res2.StatusCode);
            Assert.Equal("application/problem+json", res2.Content.Headers.ContentType?.MediaType);
            string body = await res2.Content.ReadAsStringAsync();
            Assert.Contains("Too many pending authentication challenges", body, StringComparison.Ordinal);
        }
        finally
        {
            authService.MaxActiveChallenges = originalCap;
        }
    }

    /// <summary>
    /// Verifies that the removed /api/auth/login-key endpoint returns 401 Unauthorized rather than accepting a private key.
    /// </summary>
    /// <returns>An asynchronous task tracking the assertion.</returns>
    [Fact]
    public async Task RemovedLoginKeyEndpoint_Returns401Unauthorized()
    {
        // WHAT: Attempts calling legacy /api/auth/login-key with credentials.
        // WHY:  Proves the private-key oracle endpoint was completely eliminated from the unauthenticated allow-list (Audit C8).
        // HOW:  Ensures node is initialized, sends POST to /api/auth/login-key, and asserts HTTP 401 Unauthorized.
        UserAuthenticationService authService = _factory.Services.GetRequiredService<UserAuthenticationService>();
        if (await authService.IsSetupRequiredAsync())
        {
            GeneratedKeyBundle adminBundle = authService.GenerateSecureKeyBundle("admin_probe", UserRole.Administrator, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
            await authService.CompleteInitialSetupAsync("admin_probe", adminBundle.PublicKeyBase64, KeyAlgorithm.Ed25519, AccessGate.ApiWeb);
        }

        using HttpRequestMessage request = new(HttpMethod.Post, "/api/auth/login-key")
        {
            Content = JsonContent.Create(new { username = "admin", privateKeyPem = "-----BEGIN PRIVATE KEY-----" })
        };

        HttpResponseMessage response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
