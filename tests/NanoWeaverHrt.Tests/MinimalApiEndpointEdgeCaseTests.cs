using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NanoWeaverHrt.Security.Authentication;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Integration tests targeting defensive edge-case branches, role-gating checks,
/// and input validation errors in <see cref="Program"/> minimal-API endpoints.
/// </summary>
public sealed class MinimalApiEndpointEdgeCaseTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    /// <summary>
    /// Temporary filesystem directory for isolated test fixtures and databases.
    /// </summary>
    private readonly string _tempDataDir;

    /// <summary>
    /// Isolated web application factory instance.
    /// </summary>
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>
    /// HTTP client communicating with the isolated test application.
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// Administrator JWT bearer token string.
    /// </summary>
    private string _adminToken = string.Empty;

    /// <summary>
    /// Non-administrator researcher JWT bearer token string.
    /// </summary>
    private string _researcherToken = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="MinimalApiEndpointEdgeCaseTests"/> class.
    /// </summary>
    /// <param name="baseFactory">Base application factory fixture.</param>
    public MinimalApiEndpointEdgeCaseTests(WebApplicationFactory<Program> baseFactory)
    {
        // WHAT: Initializes temporary workspace directories and WebApplicationFactory host.
        // WHY:  Provides isolated SQLite databases and storage folders for endpoint verification.
        // HOW:  Creates temporary directory, sets configuration overrides, and mints HTTP client.
        _tempDataDir = Path.Combine(Path.GetTempPath(), "weaver_edge_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDataDir);
        TestFixturesHelper.EnsureTestFixtures(_tempDataDir);

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("TRAJECTORY_INCOMING_DIR", _tempDataDir);
            builder.UseSetting("TRAJECTORY_EXPORT_DIR", Path.Combine(_tempDataDir, "exports"));
            builder.UseSetting("TRAJECTORY_PARQUET_DIR", Path.Combine(_tempDataDir, "parquet"));
            builder.UseSetting("POSTGRES_CONNECTION_STRING", string.Empty);
        });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    /// <summary>
    /// Ensures that the test database has been provisioned with administrator and researcher identities.
    /// </summary>
    /// <returns>An asynchronous task representing the setup operation.</returns>
    private async Task EnsureUsersProvisionedAsync()
    {
        // WHAT: Bootstraps master administrator and secondary researcher identities.
        // WHY:  Enables testing role gating and authorized API interactions.
        // HOW:  Uses IUserAuthenticationService to complete setup and create researcher account.
        if (!string.IsNullOrEmpty(_adminToken))
        {
            return;
        }

        using IServiceScope scope = _factory.Services.CreateScope();
        IUserAuthenticationService authService = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();

        if (await authService.IsSetupRequiredAsync())
        {
            GeneratedKeyBundle adminBundle = authService.GenerateSecureKeyBundle("admin", UserRole.Administrator, AccessGate.ApiWeb, KeyAlgorithm.Ed25519, null);
            await authService.CompleteInitialSetupAsync("admin", adminBundle.PublicKeyBase64, KeyAlgorithm.Ed25519, AccessGate.ApiWeb);
            _adminToken = await TestChallengeHelper.ExchangeChallengeTokenAsync(_client, "admin", adminBundle.PrivateKeyPem);
        }

        GeneratedKeyBundle researcherBundle = authService.GenerateSecureKeyBundle("researcher-bob", UserRole.Researcher, AccessGate.ApiWeb, KeyAlgorithm.Ed25519, null);
        await authService.CreateUserAsync("researcher-bob", UserRole.Researcher, AccessGate.ApiWeb, researcherBundle.PublicKeyBase64, KeyAlgorithm.Ed25519);
        _researcherToken = await TestChallengeHelper.ExchangeChallengeTokenAsync(_client, "researcher-bob", researcherBundle.PrivateKeyPem);
    }

    /// <summary>
    /// Verifies setup middleware redirect when visiting non-setup UI endpoints prior to platform initialization.
    /// </summary>
    [Fact]
    public async Task SetupMiddleware_UninitializedNode_RedirectsNonSetupUiRequests()
    {
        // WHAT: Tests setup redirect middleware for UI visitors on an uninitialized node.
        // WHY:  Platform operations are barred until the master Administrator account exists.
        // HOW:  Creates a pristine factory with a fresh temp directory and sends GET / and GET /setup.
        string freshDir = Path.Combine(Path.GetTempPath(), "weaver_uninit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(freshDir);
        TestFixturesHelper.EnsureTestFixtures(freshDir);

        try
        {
            using var uninitFactory = _factory.WithWebHostBuilder(b =>
            {
                b.UseSetting("TRAJECTORY_INCOMING_DIR", freshDir);
                b.UseSetting("TRAJECTORY_EXPORT_DIR", Path.Combine(freshDir, "exports"));
                b.UseSetting("TRAJECTORY_PARQUET_DIR", Path.Combine(freshDir, "parquet"));
                b.UseSetting("POSTGRES_CONNECTION_STRING", string.Empty);
            });
            using HttpClient uninitClient = uninitFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // GET / should redirect to /setup (line 414)
            HttpResponseMessage rootResp = await uninitClient.GetAsync("/");
            Assert.Equal(HttpStatusCode.Redirect, rootResp.StatusCode);
            Assert.Equal("/setup", rootResp.Headers.Location?.OriginalString);

            // GET /setup should pass through (line 418)
            HttpResponseMessage setupResp = await uninitClient.GetAsync("/setup");
            Assert.Equal(HttpStatusCode.OK, setupResp.StatusCode);

            // POST /api/setup/initialize with empty payload -> 400 (line 1670)
            using (HttpRequestMessage initReq = new(HttpMethod.Post, "/api/setup/initialize"))
            {
                initReq.Content = JsonContent.Create(new { AdminUsername = "", PublicKeyBase64 = "" });
                HttpResponseMessage initResp = await uninitClient.SendAsync(initReq);
                Assert.Equal(HttpStatusCode.BadRequest, initResp.StatusCode);
            }

            // POST /api/setup/generate-keypair with empty payload -> 400 (line 1721)
            using (HttpRequestMessage genReq = new(HttpMethod.Post, "/api/setup/generate-keypair"))
            {
                genReq.Content = JsonContent.Create(new { Username = "" });
                HttpResponseMessage genResp = await uninitClient.SendAsync(genReq);
                Assert.Equal(HttpStatusCode.BadRequest, genResp.StatusCode);
            }
        }
        finally
        {
            try { Directory.Delete(freshDir, true); } catch { }
        }
    }

    /// <summary>
    /// Verifies that non-API requests on an initialized node pass through the authentication middleware cleanly.
    /// </summary>
    [Fact]
    public async Task AuthenticationMiddleware_NonApiRequest_PassesThroughCleanly()
    {
        // WHAT: Tests public UI routing on an initialized node.
        // WHY:  Unauthenticated visitors must be allowed to load the Blazor shell and /login page.
        // HOW:  Sends GET /login and verifies 200 OK without requiring a bearer token.
        await EnsureUsersProvisionedAsync();

        HttpResponseMessage resp = await _client.GetAsync("/login");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    /// <summary>
    /// Verifies that POST /api/datasets/import without multipart archive and manifest files returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task DatasetImport_MissingFormFiles_ReturnsBadRequest()
    {
        // WHAT: Tests missing archive and manifest guard on dataset import.
        // WHY:  Dataset import requires both .tar.gz and .json manifest files.
        // HOW:  POSTs empty MultipartFormDataContent to /api/datasets/import.
        await EnsureUsersProvisionedAsync();

        using HttpRequestMessage req = new(HttpMethod.Post, "/api/datasets/import");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        req.Content = new MultipartFormDataContent();

        HttpResponseMessage resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    /// <summary>
    /// Verifies that POST /api/setup/initialize rejects empty usernames and handles double-initialization.
    /// </summary>
    [Fact]
    public async Task SetupInitialize_ValidationAndConflictBranches()
    {
        // WHAT: Tests input validation and re-initialization conflict on /api/setup/initialize.
        // WHY:  Master administrator setup must enforce valid credentials and lock out after initial run.
        // HOW:  POSTs empty payload (asserts 400), then attempts re-initialization after setup is complete (asserts 400).
        await EnsureUsersProvisionedAsync();

        // 1. Empty username or key -> 400 (line 1670)
        using (HttpRequestMessage emptyReq = new(HttpMethod.Post, "/api/setup/initialize"))
        {
            emptyReq.Content = JsonContent.Create(new { AdminUsername = "", PublicKeyBase64 = "" });
            HttpResponseMessage emptyResp = await _client.SendAsync(emptyReq);
            Assert.Equal(HttpStatusCode.BadRequest, emptyResp.StatusCode);
        }

        // 2. Already initialized -> 400 conflict (line 1698)
        using (HttpRequestMessage conflictReq = new(HttpMethod.Post, "/api/setup/initialize"))
        {
            conflictReq.Content = JsonContent.Create(new
            {
                AdminUsername = "second-admin",
                PublicKeyBase64 = "MCowBQYDK2VwAyEA1234567890abcdef1234567890abcdef1234567890=="
            });
            HttpResponseMessage conflictResp = await _client.SendAsync(conflictReq);
            Assert.Equal(HttpStatusCode.BadRequest, conflictResp.StatusCode);
        }
    }

    /// <summary>
    /// Verifies that POST /api/setup/generate-keypair with an empty username returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task SetupGenerateKey_EmptyUsername_ReturnsBadRequest()
    {
        // WHAT: Tests username validation on /api/setup/generate-keypair.
        // WHY:  Key generation requires a designated identity handle.
        // HOW:  POSTs empty username payload and verifies 400 Bad Request.
        using HttpRequestMessage req = new(HttpMethod.Post, "/api/setup/generate-keypair");
        req.Content = JsonContent.Create(new { Username = "" });

        HttpResponseMessage resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    /// <summary>
    /// Verifies that GET /api/auth/me returns 401 Unauthorized when requested without a valid token.
    /// </summary>
    [Fact]
    public async Task GetCurrentUser_WithoutToken_ReturnsUnauthorized()
    {
        // WHAT: Tests unauthenticated rejection on /api/auth/me.
        // WHY:  Current user identity cannot be inspected without an authenticated token.
        // HOW:  Ensures node is initialized, sends GET /api/auth/me using a client without session state, and asserts 401.
        await EnsureUsersProvisionedAsync();

        using HttpClient unauthClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        HttpResponseMessage resp = await unauthClient.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    /// <summary>
    /// Verifies that POST /api/users/generate-keypair rejects empty usernames and POST /api/users rejects duplicate registrations.
    /// </summary>
    [Fact]
    public async Task UserGenerateKey_ValidationAndDuplicateBranches()
    {
        // WHAT: Tests validation and duplicate user handling on user provisioning endpoints.
        // WHY:  Admin provisioning must validate username inputs and prevent duplicate handle collisions.
        // HOW:  POSTs empty username to keypair generator (asserts 400), then POSTs duplicate username to create endpoint (asserts 409).
        await EnsureUsersProvisionedAsync();

        // 1. Empty username -> 400 (line 1950)
        using (HttpRequestMessage emptyReq = new(HttpMethod.Post, "/api/users/generate-keypair"))
        {
            emptyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
            emptyReq.Content = JsonContent.Create(new { Username = "" });
            HttpResponseMessage emptyResp = await _client.SendAsync(emptyReq);
            Assert.Equal(HttpStatusCode.BadRequest, emptyResp.StatusCode);
        }

        // 2. Duplicate username on POST /api/users -> 409 Conflict (line 1910)
        using (HttpRequestMessage dupReq = new(HttpMethod.Post, "/api/users"))
        {
            dupReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
            dupReq.Content = JsonContent.Create(new
            {
                Username = "admin",
                PublicKeyBase64 = "MCowBQYDK2VwAyEA1234567890abcdef1234567890abcdef1234567890=="
            });
            HttpResponseMessage dupResp = await _client.SendAsync(dupReq);
            Assert.Equal(HttpStatusCode.Conflict, dupResp.StatusCode);
        }
    }

    /// <summary>
    /// Verifies that DELETE /api/users/{username} rejects deleting the last administrator and nonexistent users.
    /// </summary>
    [Fact]
    public async Task DeleteUser_EdgeCases_ReturnsBadRequest()
    {
        // WHAT: Tests error handling when attempting to delete nonexistent users or the last active administrator.
        // WHY:  Endpoints must return actionable error details and safeguard against admin lockout.
        // HOW:  Deletes nonexistent user (asserts 400) and deletes sole active administrator (asserts 400).
        await EnsureUsersProvisionedAsync();

        // 1. Nonexistent user -> 400 (line 2010)
        using (HttpRequestMessage nonExReq = new(HttpMethod.Delete, "/api/users/nonexistent-user-1234"))
        {
            nonExReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
            HttpResponseMessage nonExResp = await _client.SendAsync(nonExReq);
            Assert.Equal(HttpStatusCode.BadRequest, nonExResp.StatusCode);
        }

        // 2. Sole active administrator -> 400 (line 2019)
        using (HttpRequestMessage lastAdminReq = new(HttpMethod.Delete, "/api/users/admin"))
        {
            lastAdminReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
            HttpResponseMessage lastAdminResp = await _client.SendAsync(lastAdminReq);
            Assert.Equal(HttpStatusCode.BadRequest, lastAdminResp.StatusCode);
        }
    }

    /// <summary>
    /// Verifies role gating and nonexistent user handling across demographic profile, address, contact,
    /// and patient DNA sub-resource endpoints.
    /// </summary>
    [Fact]
    public async Task UserSubResources_RoleGatingAndNonexistentUserChecks()
    {
        // WHAT: Tests authorization and account existence guards on user sub-resource endpoints.
        // WHY:  Ensures non-admin users cannot access peers' records and nonexistent accounts return 404.
        // HOW:  Iterates endpoints with unauthorized caller (asserts 403/404) and non-existent account (asserts 404).
        await EnsureUsersProvisionedAsync();

        string nonExistentUser = "nonexistent-user-9999";

        async Task TestUserResourcePair(HttpMethod method, string pathSuffix, HttpContent? content = null)
        {
            // 1. Non-admin targeting admin -> Forbidden (403 or 404 per security policy)
            using HttpRequestMessage forbiddenReq = new(method, $"/api/users/admin/{pathSuffix}");
            forbiddenReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _researcherToken);
            if (content != null)
            {
                forbiddenReq.Content = content;
            }

            HttpResponseMessage forbiddenResp = await _client.SendAsync(forbiddenReq);
            Assert.True(
                forbiddenResp.StatusCode == HttpStatusCode.Forbidden || forbiddenResp.StatusCode == HttpStatusCode.NotFound,
                $"Expected Forbidden/NotFound for non-admin on /api/users/admin/{pathSuffix}, but got {forbiddenResp.StatusCode}");

            // 2. Admin targeting non-existent user -> NotFound (404)
            using HttpRequestMessage notFoundReq = new(method, $"/api/users/{nonExistentUser}/{pathSuffix}");
            notFoundReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
            if (content != null)
            {
                notFoundReq.Content = content;
            }

            HttpResponseMessage notFoundResp = await _client.SendAsync(notFoundReq);
            Assert.Equal(HttpStatusCode.NotFound, notFoundResp.StatusCode);
        }

        async Task TestPatientResourcePair(HttpMethod method, string pathSuffix, HttpContent? content = null)
        {
            // 1. Non-admin targeting admin -> NotFound (404 per SubjectRecordNotFoundDetail)
            using HttpRequestMessage forbiddenReq = new(method, $"/api/patients/admin/{pathSuffix}");
            forbiddenReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _researcherToken);
            if (content != null)
            {
                forbiddenReq.Content = content;
            }

            HttpResponseMessage forbiddenResp = await _client.SendAsync(forbiddenReq);
            Assert.Equal(HttpStatusCode.NotFound, forbiddenResp.StatusCode);

            // 2. Admin targeting non-existent user -> NotFound (404)
            using HttpRequestMessage notFoundReq = new(method, $"/api/patients/{nonExistentUser}/{pathSuffix}");
            notFoundReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
            if (content != null)
            {
                notFoundReq.Content = content;
            }

            HttpResponseMessage notFoundResp = await _client.SendAsync(notFoundReq);
            Assert.Equal(HttpStatusCode.NotFound, notFoundResp.StatusCode);
        }

        // Demographic Profile
        await TestUserResourcePair(HttpMethod.Get, "profile");
        await TestUserResourcePair(HttpMethod.Put, "profile", JsonContent.Create(new { PreferredName = "Test" }));

        // Addresses
        await TestUserResourcePair(HttpMethod.Get, "addresses");
        await TestUserResourcePair(HttpMethod.Post, "addresses", JsonContent.Create(new { StreetAddress = "123 Main St", City = "City", PostalCode = "12345", Country = "USA" }));
        await TestUserResourcePair(HttpMethod.Delete, "addresses/dummy-id");

        // Outreach Contacts
        await TestUserResourcePair(HttpMethod.Get, "contacts");
        await TestUserResourcePair(HttpMethod.Post, "contacts", JsonContent.Create(new { ContactName = "Jane Doe", PhoneNumber = "+1-555-0100" }));
        await TestUserResourcePair(HttpMethod.Delete, "contacts/dummy-id");

        // Patient DNA Blueprints
        await TestPatientResourcePair(HttpMethod.Get, "dna");
        await TestPatientResourcePair(HttpMethod.Post, "dna", JsonContent.Create(new { BlueprintName = "Test Blueprint" }));
        await TestPatientResourcePair(HttpMethod.Get, "dna/rec-dummy");
        await TestPatientResourcePair(HttpMethod.Delete, "dna/rec-dummy");

        // Patient DNA Imports
        await TestPatientResourcePair(HttpMethod.Get, "imports");
        await TestPatientResourcePair(HttpMethod.Post, "imports", JsonContent.Create(new { FileName = "genome.fasta", SequenceOrData = "ATCG" }));
        await TestPatientResourcePair(HttpMethod.Get, "imports/rec-dummy");
        await TestPatientResourcePair(HttpMethod.Delete, "imports/rec-dummy");

        // Patient DNA Results
        await TestPatientResourcePair(HttpMethod.Get, "results");
        await TestPatientResourcePair(HttpMethod.Post, "results", JsonContent.Create(new { Title = "Result", ResultType = "Test", Summary = "Summary" }));
        await TestPatientResourcePair(HttpMethod.Get, "results/res-dummy");
        await TestPatientResourcePair(HttpMethod.Delete, "results/res-dummy");

        // Direct Patient DNA Endpoints (/api/patient/dna)
        using (HttpRequestMessage patGetForbidden = new(HttpMethod.Get, "/api/patient/dna?patientId=admin"))
        {
            patGetForbidden.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _researcherToken);
            HttpResponseMessage resp = await _client.SendAsync(patGetForbidden);
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        using (HttpRequestMessage patGetInvalidId = new(HttpMethod.Get, "/api/patient/dna?patientId=invalid!id@#$"))
        {
            patGetInvalidId.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
            HttpResponseMessage resp = await _client.SendAsync(patGetInvalidId);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        // Sub-resource Item Not Found on Existing Account (admin)
        async Task AssertAdminSubResourceNotFound(HttpMethod method, string path)
        {
            using HttpRequestMessage req = new(method, path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
            HttpResponseMessage resp = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        await AssertAdminSubResourceNotFound(HttpMethod.Delete, "/api/users/admin/addresses/missing-addr-id");
        await AssertAdminSubResourceNotFound(HttpMethod.Delete, "/api/users/admin/contacts/missing-contact-id");
        await AssertAdminSubResourceNotFound(HttpMethod.Get, "/api/patients/admin/dna/missing-dna-id");
        await AssertAdminSubResourceNotFound(HttpMethod.Delete, "/api/patients/admin/dna/missing-dna-id");
        await AssertAdminSubResourceNotFound(HttpMethod.Get, "/api/patients/admin/imports/missing-import-id");
        await AssertAdminSubResourceNotFound(HttpMethod.Delete, "/api/patients/admin/imports/missing-import-id");
        await AssertAdminSubResourceNotFound(HttpMethod.Get, "/api/patients/admin/results/missing-result-id");
        await AssertAdminSubResourceNotFound(HttpMethod.Delete, "/api/patients/admin/results/missing-result-id");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // WHAT: Disposes HTTP client, factory, and cleans up temporary test database folder.
        // WHY:  Ensures no orphaned files remain on disk after test suite completion.
        // HOW:  Disposes resources and recursively deletes temporary directory.
        _client.Dispose();
        _factory.Dispose();
        try
        {
            if (Directory.Exists(_tempDataDir))
            {
                Directory.Delete(_tempDataDir, true);
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
    }
}
