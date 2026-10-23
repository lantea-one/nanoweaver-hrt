using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.ExceptionHandling;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Security test suite proving the REST API role gates actually match the mapped routes (Prime Directive 12),
/// that credential-bearing configuration values and the authoritative identity database can never leave the
/// node through an API response, and that every rejection is an RFC 7807 SecureProblemDetails body with no
/// filesystem path, SQL fragment, connection string, PEM envelope, or stack trace in it (Prime Directive 14).
/// </summary>
public sealed class ApiRoleGatingAndDisclosureTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    /// <summary>
    /// Custom HTTP response feature simulating a response whose headers have already been flushed.
    /// </summary>
    private sealed class FlushedResponseFeature : Microsoft.AspNetCore.Http.Features.IHttpResponseFeature
    {
        /// <summary>Gets or sets the response status code.</summary>
        public int StatusCode { get; set; } = 200;

        /// <summary>Gets or sets the response reason phrase.</summary>
        public string? ReasonPhrase { get; set; }

        /// <summary>Gets or sets the response headers.</summary>
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        /// <summary>Gets or sets the response body stream.</summary>
        public Stream Body { get; set; } = new MemoryStream();

        /// <summary>Gets a value indicating that the response has already started.</summary>
        public bool HasStarted => true;

        /// <summary>Registers a callback invoked before the response starts. Not used by these tests.</summary>
        /// <param name="callback">Callback delegate.</param>
        /// <param name="state">Callback state.</param>
        public void OnStarting(Func<object, Task> callback, object state)
        {
        }

        /// <summary>Registers a callback invoked after the response completes. Not used by these tests.</summary>
        /// <param name="callback">Callback delegate.</param>
        /// <param name="state">Callback state.</param>
        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }
    }

    /// <summary>
    /// Temporary directory isolated from the live simulation volume for SQLite and trajectory fixtures.
    /// </summary>
    private readonly string _tempDataDir;

    /// <summary>
    /// Isolated web application factory bound to the temporary data directory.
    /// </summary>
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiRoleGatingAndDisclosureTests"/> class.
    /// </summary>
    /// <param name="baseFactory">Base application factory fixture.</param>
    public ApiRoleGatingAndDisclosureTests(WebApplicationFactory<Program> baseFactory)
    {
        ArgumentNullException.ThrowIfNull(baseFactory);

        _tempDataDir = Path.Combine(Path.GetTempPath(), "weaver_role_gate_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDataDir);
        TestFixturesHelper.EnsureTestFixtures(_tempDataDir);

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("TRAJECTORY_INCOMING_DIR", _tempDataDir);
            builder.UseSetting("TRAJECTORY_EXPORT_DIR", Path.Combine(_tempDataDir, "exports"));
            builder.UseSetting("TRAJECTORY_PARQUET_DIR", Path.Combine(_tempDataDir, "parquet"));
            builder.UseSetting("POSTGRES_CONNECTION_STRING", string.Empty);
        });
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _factory.Dispose();
        if (Directory.Exists(_tempDataDir))
        {
            try
            {
                Directory.Delete(_tempDataDir, recursive: true);
            }
            catch (IOException)
            {
                // Ignored in test cleanup: the SQLite file may still be held by the disposing host.
            }
        }
    }

    /// <summary>
    /// Provisions one Administrator, one Researcher, and one Patient account and mints a bearer token for each.
    /// </summary>
    /// <returns>A tuple of signed bearer tokens for the Administrator, Researcher, and Patient identities.</returns>
    /// <remarks>
    /// Provisioning goes through the authentication service rather than HTTP so that the role boundary under
    /// test is never also the thing that provisions the fixture. The helper is idempotent, so every test in
    /// the class may call it.
    /// </remarks>
    private async Task<(string Administrator, string Researcher, string Patient)> ProvisionRoleTokensAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IUserAuthenticationService auth = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();

        if (await auth.IsSetupRequiredAsync())
        {
            GeneratedKeyBundle adminBundle = auth.GenerateSecureKeyBundle("gate_admin", UserRole.Administrator, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
            await auth.CompleteInitialSetupAsync("gate_admin", adminBundle.PublicKeyBase64, KeyAlgorithm.Ed25519, AccessGate.ApiWeb);

            GeneratedKeyBundle researcherBundle = auth.GenerateSecureKeyBundle("gate_researcher", UserRole.Researcher, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
            await auth.CreateUserAsync("gate_researcher", UserRole.Researcher, AccessGate.ApiWeb, researcherBundle.PublicKeyBase64, KeyAlgorithm.Ed25519);

            GeneratedKeyBundle patientBundle = auth.GenerateSecureKeyBundle("gate_patient", UserRole.Patient, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
            await auth.CreateUserAsync("gate_patient", UserRole.Patient, AccessGate.ApiWeb, patientBundle.PublicKeyBase64, KeyAlgorithm.Ed25519);
        }

        IReadOnlyList<UserAccount> users = await auth.ListUsersAsync();
        string TokenFor(string username) => auth.GenerateBearerToken(
            users.First(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase)));

        return (TokenFor("gate_admin"), TokenFor("gate_researcher"), TokenFor("gate_patient"));
    }

    /// <summary>
    /// Creates an HTTP client that sends the supplied bearer token on every request.
    /// </summary>
    /// <param name="token">Signed bearer token to attach.</param>
    /// <returns>A configured <see cref="HttpClient"/> the caller must dispose.</returns>
    private HttpClient CreateClientWithToken(string token)
    {
        HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// C4: Verifies that every real configuration route rejects non-Administrator identities and that the
    /// Administrator still reaches them.
    /// </summary>
    [Fact]
    public async Task ConfigurationRoutes_RejectEveryNonAdministratorRole()
    {
        // WHAT: Calls GET /api/configuration, POST /api/configuration/override and DELETE
        //       /api/configuration/override/{key} as Researcher and Patient, then as Administrator.
        // WHY:  The historical gate tested the partial segment '/api/config', which never matched
        //       '/api/configuration', so any role could rewrite the authoritative database overrides.
        // HOW:  Issues each verb with each token and asserts 403 for non-Administrators, success for Administrator.
        var (adminToken, researcherToken, patientToken) = await ProvisionRoleTokensAsync();

        (HttpMethod Method, string Route)[] configurationRoutes =
        [
            (HttpMethod.Get, "/api/configuration"),
            (HttpMethod.Post, "/api/configuration/override"),
            (HttpMethod.Delete, "/api/configuration/override/Storage%3APostgresConnectionString")
        ];

        foreach (string deniedToken in new[] { researcherToken, patientToken })
        {
            using HttpClient deniedClient = CreateClientWithToken(deniedToken);
            foreach ((HttpMethod method, string route) in configurationRoutes)
            {
                using HttpRequestMessage request = new(method, route);
                if (method == HttpMethod.Post)
                {
                    request.Content = JsonContent.Create(new ConfigOverrideRequest { Key = "Storage:PostgresConnectionString", Value = "Host=attacker.example;Password=pwned" });
                }

                HttpResponseMessage response = await deniedClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

                string body = await response.Content.ReadAsStringAsync();
                Assert.Contains("urn:weaver:error:forbidden", body);
                Assert.Contains("Administrator role required", body);
            }
        }

        // The override the denied callers attempted must not exist: the database still reports no override.
        using HttpClient adminClient = CreateClientWithToken(adminToken);
        HttpResponseMessage adminList = await adminClient.GetAsync("/api/configuration");
        Assert.Equal(HttpStatusCode.OK, adminList.StatusCode);

        ConfigurationItem[]? items = await adminList.Content.ReadFromJsonAsync<ConfigurationItem[]>();
        Assert.NotNull(items);
        ConfigurationItem connectionItem = items.First(i => i.Key == "Storage:PostgresConnectionString");
        Assert.NotEqual(ConfigurationSource.DatabaseOverride, connectionItem.Source);

        HttpResponseMessage adminOverride = await adminClient.PostAsJsonAsync("/api/configuration/override", new ConfigOverrideRequest { Key = "Simulation:DefaultTimeStep", Value = "0.003" });
        Assert.Equal(HttpStatusCode.OK, adminOverride.StatusCode);

        HttpResponseMessage adminRevert = await adminClient.DeleteAsync("/api/configuration/override/Simulation%3ADefaultTimeStep");
        Assert.Equal(HttpStatusCode.OK, adminRevert.StatusCode);
    }

    /// <summary>
    /// C2: Verifies that the configuration listing never discloses the PostgreSQL connection string or its password.
    /// </summary>
    [Fact]
    public async Task ConfigurationListing_NeverDisclosesConnectionStringOrPassword()
    {
        // WHAT: Reads GET /api/configuration as the Administrator and scans the raw response body.
        // WHY:  Prime Directive 14 forbids exposing connection strings or credentials to API clients at any
        //       role, so redaction — not role gating alone — has to be what protects the value.
        // HOW:  Asserts the configured password, host pair, and default placeholder are all absent and that
        //       the key itself is still listed with a redacted value.
        var (adminToken, _, _) = await ProvisionRoleTokensAsync();
        using HttpClient adminClient = CreateClientWithToken(adminToken);

        HttpResponseMessage response = await adminClient.GetAsync("/api/configuration");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("role_gate_probe_password", body);
        Assert.DoesNotContain("weaver_secret_change_me", body);
        Assert.DoesNotContain("Password=", body);
        Assert.DoesNotContain("Host=127.0.0.1", body);

        ConfigurationItem[]? items = await response.Content.ReadFromJsonAsync<ConfigurationItem[]>();
        Assert.NotNull(items);

        ConfigurationItem connectionItem = items.First(i => i.Key == "Storage:PostgresConnectionString");
        Assert.Equal("[REDACTED]", connectionItem.Value);
        Assert.Equal("[REDACTED]", connectionItem.DefaultValue);

        // Non-secret settings are untouched, so the endpoint stays useful for configuration management.
        ConfigurationItem endpointItem = items.First(i => i.Key == "Simulation:EndpointUrl");
        Assert.NotEqual("[REDACTED]", endpointItem.Value);

        // Echoing an override back must not re-publish the credential the listing redacts.
        HttpResponseMessage echo = await adminClient.PostAsJsonAsync("/api/configuration/override", new ConfigOverrideRequest { Key = "Storage:PostgresConnectionString", Value = "Host=db.internal;Password=echo_probe_password" });
        Assert.Equal(HttpStatusCode.OK, echo.StatusCode);

        string echoBody = await echo.Content.ReadAsStringAsync();
        Assert.DoesNotContain("echo_probe_password", echoBody);
        Assert.Contains("[REDACTED]", echoBody);

        HttpResponseMessage revert = await adminClient.DeleteAsync("/api/configuration/override/Storage%3APostgresConnectionString");
        Assert.Equal(HttpStatusCode.OK, revert.StatusCode);
    }

    /// <summary>
    /// C5: Verifies that the trajectory download route never serves the authoritative identity database and
    /// honours the Prime Directive 12 role boundaries.
    /// </summary>
    [Fact]
    public async Task TrajectoryDownload_NeverServesIdentityDatabaseAndHonoursRoleBoundaries()
    {
        // WHAT: Requests 'weaver_config.db' and a real trajectory as Administrator, Researcher, and Patient.
        // WHY:  The route streamed any file in the shared volume, and that volume holds the SQLite database
        //       with every username, role, public key, patient profile, address, contact, and DNA record.
        // HOW:  Confirms the database file genuinely exists on disk, then asserts the Administrator and
        //       Researcher cannot obtain it, the Researcher can obtain trajectory coordinates, and the
        //       Patient is refused the route entirely (patients read their payload from /api/patient/dna).
        var (adminToken, researcherToken, patientToken) = await ProvisionRoleTokensAsync();

        string databasePath = Path.Combine(_tempDataDir, "weaver_config.db");
        Assert.True(File.Exists(databasePath), "The identity database fixture must exist for this probe to be meaningful.");

        using HttpClient adminClient = CreateClientWithToken(adminToken);
        using HttpClient researcherClient = CreateClientWithToken(researcherToken);
        using HttpClient patientClient = CreateClientWithToken(patientToken);

        foreach (string databaseArtifact in new[] { "weaver_config.db", "weaver_config.db-wal", "weaver_config.db-shm" })
        {
            HttpResponseMessage adminAttempt = await adminClient.GetAsync($"/api/trajectories/download/{databaseArtifact}");
            Assert.Equal(HttpStatusCode.NotFound, adminAttempt.StatusCode);
            Assert.DoesNotContain("SQLite", await adminAttempt.Content.ReadAsStringAsync());

            HttpResponseMessage researcherAttempt = await researcherClient.GetAsync($"/api/trajectories/download/{databaseArtifact}");
            Assert.Equal(HttpStatusCode.NotFound, researcherAttempt.StatusCode);
        }

        // Researcher keeps legitimate trajectory streaming.
        HttpResponseMessage researcherTrajectory = await researcherClient.GetAsync("/api/trajectories/download/dna_nanotube_sample.xyz");
        Assert.Equal(HttpStatusCode.OK, researcherTrajectory.StatusCode);
        Assert.Contains("Timestep", await researcherTrajectory.Content.ReadAsStringAsync());

        // Administrator keeps full trajectory access.
        HttpResponseMessage adminTrajectory = await adminClient.GetAsync("/api/trajectories/download/dna_nanotube_sample.xyz");
        Assert.Equal(HttpStatusCode.OK, adminTrajectory.StatusCode);

        // Patient may not read the shared research volume at all.
        HttpResponseMessage patientTrajectory = await patientClient.GetAsync("/api/trajectories/download/dna_nanotube_sample.xyz");
        Assert.Equal(HttpStatusCode.Forbidden, patientTrajectory.StatusCode);

        HttpResponseMessage patientDatabase = await patientClient.GetAsync("/api/trajectories/download/weaver_config.db");
        Assert.Equal(HttpStatusCode.Forbidden, patientDatabase.StatusCode);

        // The export download route is likewise restricted to packaged artifacts.
        string strayExportPath = Path.Combine(_tempDataDir, "exports", "stray_identity_copy.db");
        Directory.CreateDirectory(Path.GetDirectoryName(strayExportPath)!);
        await File.WriteAllTextAsync(strayExportPath, "SQLite format 3 stand-in payload");

        HttpResponseMessage strayExport = await adminClient.GetAsync("/api/datasets/download/stray_identity_copy.db");
        Assert.Equal(HttpStatusCode.NotFound, strayExport.StatusCode);

        string permittedExportPath = Path.Combine(_tempDataDir, "exports", "role_gate_probe.manifest.json");
        await File.WriteAllTextAsync(permittedExportPath, "{\"dataset\":\"role_gate_probe\"}");

        HttpResponseMessage permittedExport = await adminClient.GetAsync("/api/datasets/download/role_gate_probe.manifest.json");
        Assert.Equal(HttpStatusCode.OK, permittedExport.StatusCode);
        Assert.Contains("role_gate_probe", await permittedExport.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// H2: Verifies that every endpoint with real-world consequence now carries the role gate its action implies.
    /// </summary>
    [Fact]
    public async Task ConsequentialEndpoints_EnforceTheDirectiveTwelveRoleMatrix()
    {
        // WHAT: Replays the previously ungated consequential endpoints with a Patient and a Researcher token.
        // WHY:  A Patient token could mint peering keys, crypto-shred another subject's decryption key, decrypt
        //       stored PHI, delete export bundles, and drive unbounded parse jobs.
        // HOW:  Asserts 403 for each role that Prime Directive 12 does not grant the capability, and confirms
        //       the Researcher still reaches the simulation, trajectory, and endocrine capabilities it owns.
        var (adminToken, researcherToken, patientToken) = await ProvisionRoleTokensAsync();

        (HttpMethod Method, string Route)[] administratorOnlyEndpoints =
        [
            (HttpMethod.Get, "/api/share-keys"),
            (HttpMethod.Post, "/api/share-keys/create"),
            (HttpMethod.Post, "/api/compliance/de-identify"),
            (HttpMethod.Post, "/api/compliance/crypto-shred"),
            (HttpMethod.Post, "/api/compliance/encrypt-record"),
            (HttpMethod.Post, "/api/compliance/decrypt-record"),
            (HttpMethod.Post, "/api/services/ping/postgres")
        ];

        (HttpMethod Method, string Route)[] researchEndpoints =
        [
            (HttpMethod.Delete, "/api/datasets/exports/role_gate_probe.tar.gz"),
            (HttpMethod.Post, "/api/parser/export-parquet/dna_nanotube_sample.xyz"),
            (HttpMethod.Post, "/api/endocrine/analyze/dna_nanotube_sample.xyz"),
            (HttpMethod.Post, "/api/endocrine/toggle"),
            (HttpMethod.Post, "/api/datasets/export-encrypted"),
            (HttpMethod.Get, "/api/trajectories/download/dna_nanotube_sample.xyz"),
            (HttpMethod.Get, "/api/datasets/download/role_gate_probe.tar.gz"),
            (HttpMethod.Get, "/api/simulations/campaigns")
        ];

        using HttpClient patientClient = CreateClientWithToken(patientToken);
        using HttpClient researcherClient = CreateClientWithToken(researcherToken);

        foreach ((HttpMethod method, string route) in administratorOnlyEndpoints)
        {
            using HttpRequestMessage patientRequest = new(method, route);
            HttpResponseMessage patientResponse = await patientClient.SendAsync(patientRequest);
            Assert.Equal(HttpStatusCode.Forbidden, patientResponse.StatusCode);

            using HttpRequestMessage researcherRequest = new(method, route);
            HttpResponseMessage researcherResponse = await researcherClient.SendAsync(researcherRequest);
            Assert.Equal(HttpStatusCode.Forbidden, researcherResponse.StatusCode);
        }

        foreach ((HttpMethod method, string route) in researchEndpoints)
        {
            using HttpRequestMessage patientRequest = new(method, route);
            HttpResponseMessage patientResponse = await patientClient.SendAsync(patientRequest);
            Assert.Equal(HttpStatusCode.Forbidden, patientResponse.StatusCode);

            string body = await patientResponse.Content.ReadAsStringAsync();
            Assert.Contains("urn:weaver:error:forbidden", body);
        }

        // Researcher capabilities granted by Prime Directive 12 remain intact.
        HttpResponseMessage researcherCampaigns = await researcherClient.GetAsync("/api/simulations/campaigns");
        Assert.Equal(HttpStatusCode.OK, researcherCampaigns.StatusCode);

        HttpResponseMessage researcherToggle = await researcherClient.PostAsJsonAsync("/api/endocrine/toggle", new ToggleSwitchRequest
        {
            TargetState = ToggleState.EstrogenicDominant,
            Regimen = TherapeuticRegimen.MaleToFemale
        });
        Assert.Equal(HttpStatusCode.OK, researcherToggle.StatusCode);

        HttpResponseMessage researcherAnalyze = await researcherClient.PostAsync("/api/endocrine/analyze/dna_nanotube_sample.xyz", null);
        Assert.Equal(HttpStatusCode.OK, researcherAnalyze.StatusCode);

        // Administrator capabilities remain intact on the Administrator-only surface.
        using HttpClient adminClient = CreateClientWithToken(adminToken);
        HttpResponseMessage adminShareKeys = await adminClient.GetAsync("/api/share-keys");
        Assert.Equal(HttpStatusCode.OK, adminShareKeys.StatusCode);

        HttpResponseMessage adminPing = await adminClient.PostAsync("/api/services/ping/postgres", null);
        Assert.Equal(HttpStatusCode.OK, adminPing.StatusCode);
    }

    /// <summary>
    /// M1: Verifies that the health probes survive a malformed session cookie and that /api routes answer 401.
    /// </summary>
    [Fact]
    public async Task HealthProbes_SurviveMalformedSessionCookieWithoutDisclosure()
    {
        // WHAT: Sends 'Cookie: weaver_session=a.b' to /healthz, /health, /api/system/health, and a malformed
        //       bearer token to a protected /api route.
        // WHY:  The token decoder throws FormatException on non-Base64 segments, which surfaced as an unhandled
        //       500 from a liveness probe and, in Development, as a full stack trace — the disclosure Prime
        //       Directive 14 forbids, on a route that bypassed the exception middleware by construction.
        // HOW:  Asserts the probes still answer 200 with the public overview and that /api answers 401 with
        //       problem details containing no stack frame, source path, or exception type name.
        await ProvisionRoleTokensAsync();

        using HttpClient probeClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        foreach (string probeRoute in new[] { "/healthz", "/health", "/api/system/health" })
        {
            using HttpRequestMessage probeRequest = new(HttpMethod.Get, probeRoute);
            probeRequest.Headers.Add("Cookie", "weaver_session=a.b");
            HttpResponseMessage probeResponse = await probeClient.SendAsync(probeRequest);

            Assert.Equal(HttpStatusCode.OK, probeResponse.StatusCode);
            string probeBody = await probeResponse.Content.ReadAsStringAsync();
            Assert.Contains("\"status\": \"Healthy\"", probeBody);
            Assert.Contains("\"authenticatedSession\": false", probeBody);
            Assert.DoesNotContain("FormatException", probeBody);
            Assert.DoesNotContain("   at ", probeBody);
        }

        using HttpRequestMessage apiRequest = new(HttpMethod.Get, "/api/datasets");
        apiRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "a.b");
        HttpResponseMessage apiResponse = await probeClient.SendAsync(apiRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, apiResponse.StatusCode);
        Assert.Equal("application/problem+json", apiResponse.Content.Headers.ContentType?.MediaType);

        string apiBody = await apiResponse.Content.ReadAsStringAsync();
        Assert.Contains("urn:weaver:error:unauthorized", apiBody);
        Assert.Contains("Malformed bearer token encoding.", apiBody);
        Assert.DoesNotContain("FormatException", apiBody);
        Assert.DoesNotContain("   at ", apiBody);

        // The same probe under ASPNETCORE_ENVIRONMENT=Development, where the framework would otherwise
        // render its developer exception page (complete with source paths) for an escaping exception.
        string developmentDir = Path.Combine(Path.GetTempPath(), "weaver_role_gate_dev_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(developmentDir);
        TestFixturesHelper.EnsureTestFixtures(developmentDir);

        try
        {
            using WebApplicationFactory<Program> developmentFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Development");
                builder.UseSetting("TRAJECTORY_INCOMING_DIR", developmentDir);
                builder.UseSetting("TRAJECTORY_EXPORT_DIR", Path.Combine(developmentDir, "exports"));
                builder.UseSetting("TRAJECTORY_PARQUET_DIR", Path.Combine(developmentDir, "parquet"));
                builder.UseSetting("POSTGRES_CONNECTION_STRING", string.Empty);
            });

            using HttpClient developmentClient = developmentFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

            // Confirms the probe really runs under the environment that enables the developer exception page.
            Assert.Equal("Development", developmentFactory.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>().EnvironmentName);

            using HttpRequestMessage developmentProbe = new(HttpMethod.Get, "/healthz");
            developmentProbe.Headers.Add("Cookie", "weaver_session=a.b");
            HttpResponseMessage developmentResponse = await developmentClient.SendAsync(developmentProbe);

            Assert.Equal(HttpStatusCode.OK, developmentResponse.StatusCode);
            string developmentBody = await developmentResponse.Content.ReadAsStringAsync();
            Assert.Contains("\"status\": \"Healthy\"", developmentBody);
            Assert.DoesNotContain("FormatException", developmentBody);
            Assert.DoesNotContain("Stack", developmentBody);
        }
        finally
        {
            if (Directory.Exists(developmentDir))
            {
                try
                {
                    Directory.Delete(developmentDir, recursive: true);
                }
                catch (IOException)
                {
                    // Ignored in test cleanup.
                }
            }
        }
    }

    /// <summary>
    /// M2: Verifies that authentication, access gating, and setup rejections are RFC 7807 SecureProblemDetails.
    /// </summary>
    [Fact]
    public async Task AuthenticationAndSetupRejections_UseSecureProblemDetails()
    {
        // WHAT: Triggers the missing-token 401, the already-initialized setup 400, and the auth/setup endpoint
        //       validation rejections, then checks every field of the RFC 7807 body.
        // WHY:  Directive 14 mandates the single SecureProblemDetails schema; hand-rolled '{ error }' objects
        //       gave API clients a second, incompatible error contract.
        // HOW:  Deserializes each rejection into SecureProblemDetails and asserts the type, title, status,
        //       instance, and trace identifier are all populated.
        await ProvisionRoleTokensAsync();
        using HttpClient anonymousClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        HttpResponseMessage missingToken = await anonymousClient.GetAsync("/api/datasets");
        Assert.Equal(HttpStatusCode.Unauthorized, missingToken.StatusCode);
        Assert.Equal("application/problem+json", missingToken.Content.Headers.ContentType?.MediaType);

        SecureProblemDetails? problem = await missingToken.Content.ReadFromJsonAsync<SecureProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("urn:weaver:error:unauthorized", problem.Type);
        Assert.Equal("Unauthorized", problem.Title);
        Assert.Equal(401, problem.Status);
        Assert.Equal("/api/datasets", problem.Instance);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
        Assert.Contains("Authentication required", problem.Detail);

        HttpResponseMessage setupAgain = await anonymousClient.PostAsJsonAsync("/api/setup/initialize", new InitialSetupRequest
        {
            AdminUsername = "late_admin",
            PublicKeyBase64 = "AAAA",
            Algorithm = KeyAlgorithm.Ed25519,
            AccessGate = AccessGate.ApiWeb
        });
        Assert.Equal(HttpStatusCode.BadRequest, setupAgain.StatusCode);

        SecureProblemDetails? setupProblem = await setupAgain.Content.ReadFromJsonAsync<SecureProblemDetails>();
        Assert.NotNull(setupProblem);
        Assert.Equal("urn:weaver:error:bad-request", setupProblem.Type);
        Assert.Equal(400, setupProblem.Status);
        Assert.Contains("already completed", setupProblem.Detail);

        HttpResponseMessage emptyChallenge = await anonymousClient.PostAsJsonAsync("/api/auth/challenge", new ChallengeRequest { Username = "" });
        Assert.Equal(HttpStatusCode.BadRequest, emptyChallenge.StatusCode);
        Assert.Equal("application/problem+json", emptyChallenge.Content.Headers.ContentType?.MediaType);

        SecureProblemDetails? challengeProblem = await emptyChallenge.Content.ReadFromJsonAsync<SecureProblemDetails>();
        Assert.NotNull(challengeProblem);
        Assert.Equal("Username is required.", challengeProblem.Detail);

        HttpResponseMessage emptyExchange = await anonymousClient.PostAsJsonAsync("/api/auth/token", new TokenExchangeRequest { Username = "", ChallengeId = "", SignatureBase64 = "" });
        Assert.Equal(HttpStatusCode.BadRequest, emptyExchange.StatusCode);
        Assert.Equal("application/problem+json", emptyExchange.Content.Headers.ContentType?.MediaType);

        HttpResponseMessage unknownExchange = await anonymousClient.PostAsJsonAsync("/api/auth/token", new TokenExchangeRequest
        {
            Username = "ghost_user",
            ChallengeId = Guid.NewGuid().ToString(),
            SignatureBase64 = "AAAA"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, unknownExchange.StatusCode);

        SecureProblemDetails? exchangeProblem = await unknownExchange.Content.ReadFromJsonAsync<SecureProblemDetails>();
        Assert.NotNull(exchangeProblem);
        Assert.Equal(401, exchangeProblem.Status);

        HttpResponseMessage emptyLogin = await anonymousClient.PostAsJsonAsync("/api/auth/login-key", new KeyLoginRequest { Username = "", PrivateKeyPem = "" });
        Assert.Equal(HttpStatusCode.BadRequest, emptyLogin.StatusCode);
        Assert.Equal("application/problem+json", emptyLogin.Content.Headers.ContentType?.MediaType);

        HttpResponseMessage unknownLogin = await anonymousClient.PostAsJsonAsync("/api/auth/login-key", new KeyLoginRequest { Username = "ghost_user", PrivateKeyPem = "-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----" });
        Assert.Equal(HttpStatusCode.Unauthorized, unknownLogin.StatusCode);

        SecureProblemDetails? loginProblem = await unknownLogin.Content.ReadFromJsonAsync<SecureProblemDetails>();
        Assert.NotNull(loginProblem);
        Assert.Equal(401, loginProblem.Status);

        HttpResponseMessage keypairRejection = await anonymousClient.PostAsJsonAsync("/api/setup/generate-keypair", new GenerateKeyRequest { Username = "" });
        Assert.Equal(HttpStatusCode.BadRequest, keypairRejection.StatusCode);
    }

    /// <summary>
    /// M2: Verifies the first-launch lockout answers 503 problem details carrying the setupRequired extension member.
    /// </summary>
    [Fact]
    public async Task FirstLaunchLockout_UsesProblemDetailsWithSetupRequiredExtension()
    {
        // WHAT: Calls a protected API route on a node that has never been initialized.
        // WHY:  The lockout is the single rejection that carries an RFC 7807 extension member, so it must stay
        //       machine-readable for the setup wizard while still using the SecureProblemDetails schema.
        // HOW:  Spins up a factory over an empty data directory and inspects the 503 body.
        string lockoutDir = Path.Combine(Path.GetTempPath(), "weaver_role_gate_lockout_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(lockoutDir);

        try
        {
            using WebApplicationFactory<Program> lockoutFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("TRAJECTORY_INCOMING_DIR", lockoutDir);
                builder.UseSetting("TRAJECTORY_EXPORT_DIR", Path.Combine(lockoutDir, "exports"));
                builder.UseSetting("TRAJECTORY_PARQUET_DIR", Path.Combine(lockoutDir, "parquet"));
                builder.UseSetting("POSTGRES_CONNECTION_STRING", string.Empty);
            });

            using HttpClient lockoutClient = lockoutFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            HttpResponseMessage lockout = await lockoutClient.GetAsync("/api/datasets");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, lockout.StatusCode);
            Assert.Equal("application/problem+json", lockout.Content.Headers.ContentType?.MediaType);

            string body = await lockout.Content.ReadAsStringAsync();
            Assert.Contains("\"setupRequired\": true", body);

            using JsonDocument document = JsonDocument.Parse(body);
            Assert.Equal("urn:weaver:error:service-unavailable", document.RootElement.GetProperty("type").GetString());
            Assert.Equal("Service Unavailable", document.RootElement.GetProperty("title").GetString());
            Assert.Equal(503, document.RootElement.GetProperty("status").GetInt32());
            Assert.True(document.RootElement.GetProperty("setupRequired").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(lockoutDir))
            {
                try
                {
                    Directory.Delete(lockoutDir, recursive: true);
                }
                catch (IOException)
                {
                    // Ignored in test cleanup.
                }
            }
        }
    }

    /// <summary>
    /// C4 and H2: Verifies the pure route-requirement resolver maps every gated route and leaves open routes open.
    /// </summary>
    /// <param name="method">HTTP verb under test.</param>
    /// <param name="path">Request path under test.</param>
    /// <param name="expected">Expected resolved requirement.</param>
    [Theory]
    [InlineData("GET", "/api/configuration", Program.RouteRoleRequirement.AdministratorOnly)]
    [InlineData("POST", "/api/configuration/override", Program.RouteRoleRequirement.AdministratorOnly)]
    [InlineData("DELETE", "/api/configuration/override/Storage:PostgresConnectionString", Program.RouteRoleRequirement.AdministratorOnly)]
    [InlineData("GET", "/api/share-keys", Program.RouteRoleRequirement.AdministratorOnly)]
    [InlineData("POST", "/api/share-keys/create", Program.RouteRoleRequirement.AdministratorOnly)]
    [InlineData("POST", "/api/share-keys/redeem", Program.RouteRoleRequirement.None)]
    [InlineData("POST", "/api/compliance/de-identify", Program.RouteRoleRequirement.AdministratorOnly)]
    [InlineData("POST", "/api/compliance/crypto-shred", Program.RouteRoleRequirement.AdministratorOnly)]
    [InlineData("POST", "/api/compliance/encrypt-record", Program.RouteRoleRequirement.AdministratorOnly)]
    [InlineData("POST", "/api/compliance/decrypt-record", Program.RouteRoleRequirement.AdministratorOnly)]
    [InlineData("GET", "/api/compliance/verify-ledger", Program.RouteRoleRequirement.AdministratorOrResearcher)]
    [InlineData("GET", "/api/compliance/status", Program.RouteRoleRequirement.AdministratorOnly)]
    [InlineData("GET", "/api/compliance/audit-ledger", Program.RouteRoleRequirement.AdministratorOnly)]
    [InlineData("POST", "/api/services/ping/postgres", Program.RouteRoleRequirement.AdministratorOnly)]
    // Reading the stack telemetry is now gated too: it reports which backing services are reachable and how
    // fast they answer, which is infrastructure diagnostics rather than anything a Patient needs. The probe
    // verb above stays Administrator-only, because the resolver returns the stricter of the two matches.
    [InlineData("GET", "/api/services", Program.RouteRoleRequirement.AdministratorOrResearcher)]
    [InlineData("POST", "/api/parser/process/sample.xyz", Program.RouteRoleRequirement.AdministratorOrResearcher)]
    [InlineData("POST", "/api/parser/export-parquet/sample.xyz", Program.RouteRoleRequirement.AdministratorOrResearcher)]
    [InlineData("POST", "/api/simulations/run/serum-stability", Program.RouteRoleRequirement.AdministratorOrResearcher)]
    [InlineData("GET", "/api/simulations/campaigns", Program.RouteRoleRequirement.AdministratorOrResearcher)]
    [InlineData("POST", "/api/fabrication/generate", Program.RouteRoleRequirement.AdministratorOrResearcher)]
    [InlineData("POST", "/api/endocrine/analyze/sample.xyz", Program.RouteRoleRequirement.AdministratorOrResearcher)]
    [InlineData("POST", "/api/endocrine/toggle", Program.RouteRoleRequirement.AdministratorOrResearcher)]
    [InlineData("GET", "/api/endocrine/profile", Program.RouteRoleRequirement.None)]
    [InlineData("POST", "/api/datasets/export", Program.RouteRoleRequirement.AdministratorOrResearcher)]
    [InlineData("POST", "/api/datasets/import", Program.RouteRoleRequirement.AdministratorOrResearcher)]
    [InlineData("POST", "/api/datasets/export-encrypted", Program.RouteRoleRequirement.AdministratorOrResearcher)]
    [InlineData("GET", "/api/datasets/download/bundle.tar.gz", Program.RouteRoleRequirement.AdministratorOrResearcher)]
    [InlineData("GET", "/api/trajectories/download/sample.xyz", Program.RouteRoleRequirement.AdministratorOrResearcher)]
    [InlineData("DELETE", "/api/datasets/exports/bundle.tar.gz", Program.RouteRoleRequirement.AdministratorOrResearcher)]
    [InlineData("GET", "/api/datasets/exports", Program.RouteRoleRequirement.AdministratorOrResearcher)]
    [InlineData("GET", "/api/datasets", Program.RouteRoleRequirement.None)]
    [InlineData("POST", "/api/auth/logout", Program.RouteRoleRequirement.None)]
    [InlineData("GET", "/api/configurations-report", Program.RouteRoleRequirement.None)]
    public void ResolveRouteRoleRequirement_MatchesTheMappedRoutes(string method, string path, Program.RouteRoleRequirement expected)
    {
        // WHAT: Exercises the declarative route table against the real mapped routes.
        // WHY:  '/api/config' never matched '/api/configuration' and no route ever began '/api/stack', so the
        //       previous gates were unreachable code; a pure resolver can be proven exhaustively instead.
        // HOW:  Resolves each verb and path pair and compares against the expected requirement.
        Assert.Equal(expected, Program.ResolveRouteRoleRequirement(method, path));
    }

    /// <summary>
    /// Verifies that the resolver rejects a null HTTP verb rather than silently treating it as a mutation.
    /// </summary>
    [Fact]
    public void ResolveRouteRoleRequirement_NullMethod_Throws()
    {
        // WHAT: Passes a null verb to the resolver.
        // WHY:  A null verb must never be interpreted as a permissive default.
        // HOW:  Asserts ArgumentNullException is raised.
        Assert.Throws<ArgumentNullException>(() => Program.ResolveRouteRoleRequirement(null!, "/api/configuration"));
    }

    /// <summary>
    /// Verifies the role matrix applied to each resolved route requirement.
    /// </summary>
    /// <param name="role">Role carried by the authenticated identity.</param>
    /// <param name="requirement">Resolved route requirement.</param>
    /// <param name="expected">Whether the role is expected to pass the gate.</param>
    [Theory]
    [InlineData(UserRole.Administrator, Program.RouteRoleRequirement.AdministratorOnly, true)]
    [InlineData(UserRole.Researcher, Program.RouteRoleRequirement.AdministratorOnly, false)]
    [InlineData(UserRole.Patient, Program.RouteRoleRequirement.AdministratorOnly, false)]
    [InlineData(UserRole.Administrator, Program.RouteRoleRequirement.AdministratorOrResearcher, true)]
    [InlineData(UserRole.Researcher, Program.RouteRoleRequirement.AdministratorOrResearcher, true)]
    [InlineData(UserRole.Patient, Program.RouteRoleRequirement.AdministratorOrResearcher, false)]
    [InlineData(UserRole.Administrator, Program.RouteRoleRequirement.None, true)]
    [InlineData(UserRole.Researcher, Program.RouteRoleRequirement.None, true)]
    [InlineData(UserRole.Patient, Program.RouteRoleRequirement.None, true)]
    public void IsRoleAuthorizedForRequirement_AppliesTheDirectiveTwelveMatrix(UserRole role, Program.RouteRoleRequirement requirement, bool expected)
    {
        // WHAT: Evaluates each role against each requirement level.
        // WHY:  Prime Directive 12 defines exactly three roles, and each gate level must admit exactly the
        //       roles the directive names.
        // HOW:  Asserts the authorization decision for all nine combinations.
        Assert.Equal(expected, Program.IsRoleAuthorizedForRequirement(role, requirement));
    }

    /// <summary>
    /// C2: Verifies secret-bearing configuration key classification and value redaction.
    /// </summary>
    [Fact]
    public void ConfigurationRedaction_ClassifiesSecretKeysAndRewritesValues()
    {
        // WHAT: Classifies secret and non-secret keys, then redacts a listing containing both.
        // WHY:  Prime Directive 14 forbids publishing connection strings or credentials to API clients.
        // HOW:  Calls IsSecretConfigurationKey and RedactSecretConfigurationValues directly.
        Assert.True(Program.IsSecretConfigurationKey("Storage:PostgresConnectionString"));
        Assert.True(Program.IsSecretConfigurationKey("Security:ProfilePepper"));
        Assert.True(Program.IsSecretConfigurationKey("Peer:SharedApiKey"));
        Assert.False(Program.IsSecretConfigurationKey("Simulation:EndpointUrl"));
        Assert.Throws<ArgumentNullException>(() => Program.IsSecretConfigurationKey(null!));

        ConfigurationItem secretItem = new()
        {
            Key = "Storage:PostgresConnectionString",
            Value = "Host=db.internal;Password=live_secret",
            DefaultValue = "Host=localhost;Password=default_secret",
            Description = "Connection string for the PostgreSQL storage node.",
            Source = ConfigurationSource.EnvironmentVariable,
            Category = "Storage & Database"
        };

        ConfigurationItem publicItem = new()
        {
            Key = "Simulation:EndpointUrl",
            Value = "http://simulation-node:8080",
            DefaultValue = "http://simulation-node:8080",
            Description = "Network URL of the simulation engine.",
            Source = ConfigurationSource.DefaultFallback,
            Category = "Simulation Engine"
        };

        IReadOnlyList<ConfigurationItem> redacted = Program.RedactSecretConfigurationValues([secretItem, publicItem]);

        Assert.Equal("[REDACTED]", redacted[0].Value);
        Assert.Equal("[REDACTED]", redacted[0].DefaultValue);
        Assert.Equal("Connection string for the PostgreSQL storage node.", redacted[0].Description);
        Assert.Equal("http://simulation-node:8080", redacted[1].Value);
        Assert.Throws<ArgumentNullException>(() => Program.RedactSecretConfigurationValues(null!));
    }

    /// <summary>
    /// C5: Verifies the download extension allow-list accepts trajectory artifacts and refuses database files.
    /// </summary>
    [Fact]
    public void DownloadAllowList_AcceptsTrajectoriesAndRefusesDatabaseFiles()
    {
        // WHAT: Tests the allow-list helper against trajectory, export, and database file names.
        // WHY:  The authoritative identity and PHI database lives in the same directory the trajectory
        //       download route serves, so only an allow-list can keep it unreachable.
        // HOW:  Calls IsDownloadableFileName with representative names and guard-clause arguments.
        string[] trajectoryExtensions = [".xyz", ".dcd", ".dump"];

        Assert.True(Program.IsDownloadableFileName("dna_nanotube_sample.xyz", trajectoryExtensions));
        Assert.True(Program.IsDownloadableFileName("DNA_SAMPLE.DCD", trajectoryExtensions));
        Assert.False(Program.IsDownloadableFileName("weaver_config.db", trajectoryExtensions));
        Assert.False(Program.IsDownloadableFileName("weaver_config.db-wal", trajectoryExtensions));
        Assert.False(Program.IsDownloadableFileName("passwd", trajectoryExtensions));
        Assert.Throws<ArgumentNullException>(() => Program.IsDownloadableFileName(null!, trajectoryExtensions));
        Assert.Throws<ArgumentNullException>(() => Program.IsDownloadableFileName("sample.xyz", null!));
    }

    /// <summary>
    /// M3: Verifies that no 4xx problem detail can carry a filesystem path, SQL fragment, connection string, or PEM key.
    /// </summary>
    [Fact]
    public void ExceptionSanitization_RedactsSqlFragmentsAndUncPathsOn4xx()
    {
        // WHAT: Sanitizes a hostile 400-mapped exception message containing a Unix path, a UNC share, a full
        //       connection string, a SQL statement, and a PEM private key envelope.
        // WHY:  Prime Directive 14 names filesystem paths, credentials, PEM material, and internal SQL
        //       fragments; the SQL and UNC classes previously passed through every 4xx response verbatim.
        // HOW:  Calls SanitizeDetail at status 400 and asserts none of the sensitive substrings survive.
        string hostileMessage =
            "Query failed: SELECT public_key_base64, role FROM weaver_users WHERE username = 'admin'; " +
            "store /home/fabigail/simulations/shared-data/weaver_config.db on \\\\fileserver\\weaver-data\\patients " +
            "using Host=db.internal;Port=5432;Database=weaver_hrt;Username=weaver;Password=SuperSecretDbPassword123! " +
            "signed by -----BEGIN PRIVATE KEY-----\nMC4CAQAwBQYDK2VwBCIEIKey\n-----END PRIVATE KEY-----";

        string sanitized = SecureExceptionSanitizer.SanitizeDetail(hostileMessage, 400, "trace-m3");

        Assert.DoesNotContain("weaver_users", sanitized);
        Assert.DoesNotContain("SELECT", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/home/fabigail", sanitized);
        Assert.DoesNotContain("fileserver", sanitized);
        Assert.DoesNotContain("weaver-data", sanitized);
        Assert.DoesNotContain("SuperSecretDbPassword123!", sanitized);
        Assert.DoesNotContain("weaver_hrt", sanitized);
        Assert.DoesNotContain("BEGIN PRIVATE KEY", sanitized);
        Assert.DoesNotContain("MC4CAQAwBQYDK2Vw", sanitized);
        Assert.Contains("[REDACTED_SQL]", sanitized);
        Assert.Contains("[REDACTED_PATH]", sanitized);

        // A PEM envelope on its own is replaced by the key marker rather than swallowed by a credential pair.
        string pemOnly = SecureExceptionSanitizer.SanitizeDetail(
            "Rejected key: -----BEGIN PRIVATE KEY-----\nMC4CAQAwBQYDK2VwBCIEIKey\n-----END PRIVATE KEY-----",
            400,
            "trace-pem");
        Assert.Contains("[REDACTED_KEY]", pemOnly);
        Assert.DoesNotContain("MC4CAQAwBQYDK2Vw", pemOnly);

        // Additional SQL statement shapes are each redacted, and ordinary prose is left intact.
        string[] sqlShapes =
        [
            "INSERT INTO weaver_schema_migrations (version) VALUES (5)",
            "INSERT OR REPLACE INTO weaver_configuration (config_key) VALUES ('x')",
            "UPDATE weaver_users SET role = 0 WHERE username = 'victim'",
            "DELETE FROM weaver_patient_dna WHERE id = 7",
            "CREATE TABLE weaver_sessions (id TEXT)",
            "CREATE UNIQUE INDEX idx_weaver_users_username ON weaver_users (username)",
            "ALTER TABLE weaver_users ADD COLUMN pepper TEXT",
            "DROP TABLE weaver_users",
            "TRUNCATE TABLE weaver_audit_ledger",
            "ON CONFLICT (config_key) DO NOTHING"
        ];

        foreach (string sqlShape in sqlShapes)
        {
            string sanitizedShape = SecureExceptionSanitizer.SanitizeDetail($"Database rejected statement: {sqlShape};", 400, "trace-sql");
            Assert.Contains("[REDACTED_SQL]", sanitizedShape);
            Assert.DoesNotContain("weaver_", sanitizedShape);
        }

        string prose = SecureExceptionSanitizer.SanitizeDetail("Select a regimen before you update the dosing schedule.", 400, "trace-prose");
        Assert.Equal("Select a regimen before you update the dosing schedule.", prose);
    }

    /// <summary>
    /// M1: Verifies that a non-API request can never surface a stack trace, even in Development.
    /// </summary>
    [Fact]
    public async Task SecureExceptionHandlerMiddleware_NonApiFailure_ReturnsOpaqueCorrelationNotice()
    {
        // WHAT: Drives the middleware with a throwing Web UI (non-/api) request, once with a fresh response
        //       and once with a response whose headers have already been flushed.
        // WHY:  ASP.NET Core registers its Development developer exception page ahead of this middleware, so an
        //       escaping exception would be rendered as a full stack trace with source paths.
        // HOW:  Asserts an opaque 500 correlation notice is written, and that a flushed response is left alone.
        NullLogger<SecureExceptionHandlerMiddleware> logger = NullLogger<SecureExceptionHandlerMiddleware>.Instance;

        SecureExceptionHandlerMiddleware middleware = new(_ =>
            throw new InvalidOperationException("Razor render failed reading /home/fabigail/simulations/shared-data/weaver_config.db"), logger);

        DefaultHttpContext pageContext = new();
        pageContext.Request.Path = "/dashboard";
        pageContext.TraceIdentifier = "TRACE-NONAPI";
        using MemoryStream pageBody = new();
        pageContext.Response.Body = pageBody;

        await middleware.InvokeAsync(pageContext);

        Assert.Equal(StatusCodes.Status500InternalServerError, pageContext.Response.StatusCode);
        Assert.Equal("text/plain; charset=utf-8", pageContext.Response.ContentType);

        pageBody.Seek(0, SeekOrigin.Begin);
        using StreamReader pageReader = new(pageBody);
        string pageText = await pageReader.ReadToEndAsync();

        Assert.Equal("An internal error occurred during request processing. Correlation ID: TRACE-NONAPI.", pageText);
        Assert.DoesNotContain("/home/fabigail", pageText);
        Assert.DoesNotContain("InvalidOperationException", pageText);
        Assert.DoesNotContain("   at ", pageText);

        DefaultHttpContext flushedContext = new();
        flushedContext.Request.Path = "/dashboard";
        flushedContext.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(new FlushedResponseFeature());

        // Must complete without throwing and without attempting to rewrite a started response.
        await middleware.InvokeAsync(flushedContext);
        Assert.Equal(200, flushedContext.Response.StatusCode);
    }

    /// <summary>
    /// Provisions a second Patient account so cross-subject record access can be probed, and returns its token.
    /// </summary>
    /// <returns>A signed bearer token for the second Patient identity.</returns>
    private async Task<string> ProvisionSecondPatientTokenAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IUserAuthenticationService auth = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();

        IReadOnlyList<UserAccount> existing = await auth.ListUsersAsync();
        UserAccount? second = existing.FirstOrDefault(u => string.Equals(u.Username, "gate_patient_two", StringComparison.OrdinalIgnoreCase));
        if (second == null)
        {
            GeneratedKeyBundle bundle = auth.GenerateSecureKeyBundle("gate_patient_two", UserRole.Patient, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
            second = await auth.CreateUserAsync("gate_patient_two", UserRole.Patient, AccessGate.ApiWeb, bundle.PublicKeyBase64, KeyAlgorithm.Ed25519);
        }

        return auth.GenerateBearerToken(second);
    }

    /// <summary>
    /// H1: Verifies that patient DNA, import, and result records are reachable only by their own subject
    /// or an Administrator, and that a cross-subject request is indistinguishable from a nonexistent subject.
    /// </summary>
    [Fact]
    public async Task PatientRecordRoutes_AreScopedToTheSubjectOrAdministrator()
    {
        // WHAT: Drives the per-subject record routes with the subject itself, a second Patient, a Researcher,
        //       and an Administrator, and compares the cross-subject rejection with the nonexistent-subject one.
        // WHY:  Prime Directive 12 grants the Researcher role simulation, trajectory, endocrine and dataset
        //       work — not blanket read/write over every patient's DNA, imports and results — and scopes the
        //       Patient role to its own personalized records.
        // HOW:  Asserts self and Administrator access succeed, every other combination answers a masked 404,
        //       and confirms through the Administrator that a blocked write never reached the database.
        var (adminToken, researcherToken, patientToken) = await ProvisionRoleTokensAsync();
        string otherPatientToken = await ProvisionSecondPatientTokenAsync();

        using HttpClient adminClient = CreateClientWithToken(adminToken);
        using HttpClient researcherClient = CreateClientWithToken(researcherToken);
        using HttpClient patientClient = CreateClientWithToken(patientToken);
        using HttpClient otherPatientClient = CreateClientWithToken(otherPatientToken);

        string[] recordCollections = ["dna", "imports", "results"];

        // 1. The subject reaches its own records.
        foreach (string collection in recordCollections)
        {
            HttpResponseMessage ownRecords = await patientClient.GetAsync($"/api/patients/gate_patient/{collection}");
            Assert.Equal(HttpStatusCode.OK, ownRecords.StatusCode);
        }

        // 2. A second Patient is refused, and the refusal is masked as 404.
        foreach (string collection in recordCollections)
        {
            HttpResponseMessage crossSubject = await otherPatientClient.GetAsync($"/api/patients/gate_patient/{collection}");
            Assert.Equal(HttpStatusCode.NotFound, crossSubject.StatusCode);
        }

        // 3. The Researcher role no longer spans subjects on the identity/PHI surface, for reads or writes.
        foreach (string collection in recordCollections)
        {
            HttpResponseMessage researcherRead = await researcherClient.GetAsync($"/api/patients/gate_patient/{collection}");
            Assert.Equal(HttpStatusCode.NotFound, researcherRead.StatusCode);
        }

        HttpResponseMessage researcherWrite = await researcherClient.PostAsJsonAsync("/api/patients/gate_patient/dna", new CreatePatientDnaRequest
        {
            Title = "Researcher authored blueprint",
            Regimen = TherapeuticRegimen.MaleToFemale,
            NanocarrierGeometry = "Hexagonal Nanotube (6HB)"
        });
        Assert.Equal(HttpStatusCode.NotFound, researcherWrite.StatusCode);

        HttpResponseMessage researcherDelete = await researcherClient.DeleteAsync("/api/patients/gate_patient/dna/any_id");
        Assert.Equal(HttpStatusCode.NotFound, researcherDelete.StatusCode);

        // The blocked write must not have reached the database: the subject still sees no records.
        HttpResponseMessage ownDnaAfterBlockedWrite = await patientClient.GetAsync("/api/patients/gate_patient/dna");
        Assert.Equal(HttpStatusCode.OK, ownDnaAfterBlockedWrite.StatusCode);
        PatientDigitalDnaRecord[]? ownDna = await ownDnaAfterBlockedWrite.Content.ReadFromJsonAsync<PatientDigitalDnaRecord[]>();
        Assert.NotNull(ownDna);
        Assert.Empty(ownDna);

        // 4. A Researcher still reaches its own records, which is data-subject access rather than a cohort view.
        HttpResponseMessage researcherOwnRecords = await researcherClient.GetAsync("/api/patients/gate_researcher/dna");
        Assert.Equal(HttpStatusCode.OK, researcherOwnRecords.StatusCode);

        // 5. The Administrator spans subjects, as Prime Directive 12 intends.
        foreach (string collection in recordCollections)
        {
            HttpResponseMessage adminRead = await adminClient.GetAsync($"/api/patients/gate_patient/{collection}");
            Assert.Equal(HttpStatusCode.OK, adminRead.StatusCode);
        }

        // 6. Another subject's records and a nonexistent subject's records answer identically, so a Patient
        //    cannot enumerate the other subjects registered on the node.
        HttpResponseMessage existingSubject = await otherPatientClient.GetAsync("/api/patients/gate_patient/dna");
        HttpResponseMessage unknownSubject = await otherPatientClient.GetAsync("/api/patients/no_such_subject_at_all/dna");

        Assert.Equal(unknownSubject.StatusCode, existingSubject.StatusCode);
        Assert.Equal(unknownSubject.Content.Headers.ContentType?.MediaType, existingSubject.Content.Headers.ContentType?.MediaType);

        SecureProblemDetails? existingProblem = await existingSubject.Content.ReadFromJsonAsync<SecureProblemDetails>();
        SecureProblemDetails? unknownProblem = await unknownSubject.Content.ReadFromJsonAsync<SecureProblemDetails>();
        Assert.NotNull(existingProblem);
        Assert.NotNull(unknownProblem);
        Assert.Equal(unknownProblem.Type, existingProblem.Type);
        Assert.Equal(unknownProblem.Title, existingProblem.Title);
        Assert.Equal(unknownProblem.Status, existingProblem.Status);
        Assert.Equal(unknownProblem.Detail, existingProblem.Detail);
        Assert.DoesNotContain("gate_patient", existingProblem.Detail);

        // 7. The self-service identity surface keeps a plain 403: it carries no subject-existence signal,
        //    because a nonexistent subject is refused identically.
        HttpResponseMessage crossProfile = await otherPatientClient.GetAsync("/api/users/gate_patient/profile");
        HttpResponseMessage unknownProfile = await otherPatientClient.GetAsync("/api/users/no_such_subject_at_all/profile");
        Assert.Equal(HttpStatusCode.Forbidden, crossProfile.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unknownProfile.StatusCode);

        // Self-service access still passes the gate: the handler is reached and reports that this freshly
        // provisioned account simply has no profile row yet, which is a different answer from a refusal.
        HttpResponseMessage ownProfile = await otherPatientClient.GetAsync("/api/users/gate_patient_two/profile");
        Assert.NotEqual(HttpStatusCode.Forbidden, ownProfile.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ownProfile.StatusCode);
        Assert.Contains("Profile for user", await ownProfile.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// H1: Verifies the pure subject-record access resolver across every role, subject, and sub-resource.
    /// </summary>
    /// <param name="subPath">Route remainder below '/api/users' or '/api/patients'.</param>
    /// <param name="callerUsername">Username carried by the validated session.</param>
    /// <param name="callerRole">Role carried by the validated session.</param>
    /// <param name="expected">Expected access decision.</param>
    [Theory]
    [InlineData("/alice/dna", "alice", UserRole.Patient, Program.SubjectRecordAccessDecision.Allowed)]
    [InlineData("/alice/dna/7", "bob", UserRole.Patient, Program.SubjectRecordAccessDecision.MaskedAsNotFound)]
    [InlineData("/alice/imports", "bob", UserRole.Researcher, Program.SubjectRecordAccessDecision.MaskedAsNotFound)]
    [InlineData("/alice/results", "alice", UserRole.Researcher, Program.SubjectRecordAccessDecision.Allowed)]
    [InlineData("/alice/results/9", "ALICE", UserRole.Patient, Program.SubjectRecordAccessDecision.Allowed)]
    [InlineData("/alice/dna", "root", UserRole.Administrator, Program.SubjectRecordAccessDecision.Allowed)]
    [InlineData("/alice/profile", "alice", UserRole.Patient, Program.SubjectRecordAccessDecision.Allowed)]
    [InlineData("/alice/profile", "bob", UserRole.Patient, Program.SubjectRecordAccessDecision.Forbidden)]
    [InlineData("/alice/addresses/1", "bob", UserRole.Researcher, Program.SubjectRecordAccessDecision.Forbidden)]
    [InlineData("/alice/contacts", "alice", UserRole.Patient, Program.SubjectRecordAccessDecision.Allowed)]
    [InlineData("/alice/unknown-resource", "alice", UserRole.Patient, Program.SubjectRecordAccessDecision.Forbidden)]
    [InlineData("/alice", "alice", UserRole.Patient, Program.SubjectRecordAccessDecision.Forbidden)]
    [InlineData("", "alice", UserRole.Researcher, Program.SubjectRecordAccessDecision.Forbidden)]
    [InlineData(null, "alice", UserRole.Patient, Program.SubjectRecordAccessDecision.Forbidden)]
    [InlineData(null, "root", UserRole.Administrator, Program.SubjectRecordAccessDecision.Allowed)]
    public void ResolveSubjectRecordAccess_ScopesRecordsToTheirSubject(string? subPath, string callerUsername, UserRole callerRole, Program.SubjectRecordAccessDecision expected)
    {
        // WHAT: Exercises every role, subject-match, and sub-resource combination of the resolver.
        // WHY:  The decision is the only thing standing between a Patient token and another patient's PHI.
        // HOW:  Compares the resolved decision against the Prime Directive 12 expectation.
        Assert.Equal(expected, Program.ResolveSubjectRecordAccess(subPath, callerUsername, callerRole));
    }

    /// <summary>
    /// Verifies that the subject-record resolver refuses a null caller identity rather than defaulting open.
    /// </summary>
    [Fact]
    public void ResolveSubjectRecordAccess_NullCallerUsername_Throws()
    {
        // WHAT: Passes a null caller username to the resolver.
        // WHY:  A missing session identity must never be treated as matching the route's subject.
        // HOW:  Asserts ArgumentNullException is raised.
        Assert.Throws<ArgumentNullException>(() => Program.ResolveSubjectRecordAccess("/alice/dna", null!, UserRole.Patient));
    }

    /// <summary>
    /// Residual 4: Verifies that no health response discloses a local filesystem path, at any role.
    /// </summary>
    [Fact]
    public async Task HealthEndpoints_NeverDiscloseLocalFilesystemPaths()
    {
        // WHAT: Reads every health alias anonymously and as Administrator, Researcher, and Patient, then walks
        //       every string in the JSON response looking for an absolute path.
        // WHY:  Prime Directive 14 names local filesystem paths as something that must never reach an API
        //       client and grants no role an exemption; the granular report used to publish three mount paths.
        // HOW:  Asserts no JSON string value starts with a path separator or a drive letter, and that none
        //       contains the configured data directory, while the storage descriptors remain informative.
        var (adminToken, researcherToken, patientToken) = await ProvisionRoleTokensAsync();

        using HttpClient anonymousClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using HttpClient adminClient = CreateClientWithToken(adminToken);
        using HttpClient researcherClient = CreateClientWithToken(researcherToken);
        using HttpClient patientClient = CreateClientWithToken(patientToken);

        HttpClient[] clients = [anonymousClient, adminClient, researcherClient, patientClient];
        string[] healthRoutes = ["/api/system/health", "/healthz", "/health"];

        foreach (HttpClient client in clients)
        {
            foreach (string route in healthRoutes)
            {
                HttpResponseMessage response = await client.GetAsync(route);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain(_tempDataDir, body);
                Assert.DoesNotContain("/simulations/", body);

                using JsonDocument document = JsonDocument.Parse(body);
                AssertNoAbsolutePathValues(document.RootElement);
            }
        }

        // The granular report stays useful: it names the setting that configures each directory.
        HttpResponseMessage granular = await adminClient.GetAsync("/api/system/health");
        string granularBody = await granular.Content.ReadAsStringAsync();
        Assert.Contains("Configured by TRAJECTORY_INCOMING_DIR; absolute path withheld.", granularBody);
        Assert.Contains("Configured by TRAJECTORY_EXPORT_DIR; absolute path withheld.", granularBody);
        Assert.Contains("Configured by TRAJECTORY_PARQUET_DIR; absolute path withheld.", granularBody);
        Assert.Contains("\"incomingWritable\": true", granularBody);
    }

    /// <summary>
    /// Asserts recursively that no string value anywhere in a JSON element looks like an absolute path.
    /// </summary>
    /// <param name="element">JSON element to inspect.</param>
    private static void AssertNoAbsolutePathValues(JsonElement element)
    {
        // WHAT: Walks the JSON tree and inspects every string leaf.
        // WHY:  A path can hide in any field of a nested health report, so spot-checking known fields is weaker
        //       than asserting the invariant over the whole document.
        // HOW:  Recurses through objects and arrays; for each string, rejects a leading path separator or a
        //       Windows drive-letter prefix.
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    AssertNoAbsolutePathValues(property.Value);
                }

                break;

            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    AssertNoAbsolutePathValues(item);
                }

                break;

            case JsonValueKind.String:
                string value = element.GetString() ?? string.Empty;
                Assert.False(value.StartsWith('/') || value.StartsWith('\\'), $"Health response disclosed an absolute path: '{value}'");
                Assert.False(value.Length > 2 && value[1] == ':' && (value[2] == '\\' || value[2] == '/'), $"Health response disclosed an absolute path: '{value}'");
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Residual 2: Verifies that the IsSecret catalog flag is the single authority behind API and UI redaction.
    /// </summary>
    [Fact]
    public async Task SecretConfigurationFlag_IsTheSingleAuthorityForApiAndUi()
    {
        // WHAT: Classifies keys through the store and through Program, then reads the API listing.
        // WHY:  The API was redacting from its own marker list while the Web UI rendered the live connection
        //       string from the repository, so the credential had simply moved surfaces.
        // HOW:  Asserts both entry points agree, that the catalog flag reaches the serialized item, and that
        //       redaction triggers on either the flag or the key name.
        Assert.True(SystemConfigurationStore.IsSecretConfigurationKey("Storage:PostgresConnectionString"));
        Assert.True(SystemConfigurationStore.IsSecretConfigurationKey("Security:ProfilePepper"));
        Assert.False(SystemConfigurationStore.IsSecretConfigurationKey("Simulation:EndpointUrl"));
        Assert.Throws<ArgumentNullException>(() => SystemConfigurationStore.IsSecretConfigurationKey(null!));

        // Program delegates to the store rather than keeping a second list.
        Assert.True(Program.IsSecretConfigurationKey("Storage:PostgresConnectionString"));
        Assert.False(Program.IsSecretConfigurationKey("Simulation:AcceleratorBackend"));

        var (adminToken, _, _) = await ProvisionRoleTokensAsync();
        using HttpClient adminClient = CreateClientWithToken(adminToken);

        ConfigurationItem[]? items = await adminClient.GetFromJsonAsync<ConfigurationItem[]>("/api/configuration");
        Assert.NotNull(items);

        ConfigurationItem connectionItem = items.First(i => i.Key == "Storage:PostgresConnectionString");
        Assert.True(connectionItem.IsSecret);
        Assert.Equal("[REDACTED]", connectionItem.Value);

        ConfigurationItem endpointItem = items.First(i => i.Key == "Simulation:EndpointUrl");
        Assert.False(endpointItem.IsSecret);

        // Redaction triggers on the item flag even when the key name looks innocuous...
        ConfigurationItem flaggedOnly = new()
        {
            Key = "Peer:CollaborativeWorkerEndpoint",
            Value = "https://peer.internal/token/abc",
            DefaultValue = "http://localhost:5000",
            Description = "Flagged by the catalog.",
            Source = ConfigurationSource.DatabaseOverride,
            Category = "Peer Collaboration",
            IsSecret = true
        };

        // ...and on the key name even when the flag was never set.
        ConfigurationItem nameOnly = new()
        {
            Key = "Storage:PostgresConnectionString",
            Value = "Host=db.internal;Password=unflagged_secret",
            DefaultValue = "Host=localhost;Password=default",
            Description = "Not flagged by the constructing caller.",
            Source = ConfigurationSource.EnvironmentVariable,
            Category = "Storage & Database",
            IsSecret = false
        };

        IReadOnlyList<ConfigurationItem> redacted = Program.RedactSecretConfigurationValues([flaggedOnly, nameOnly]);
        Assert.Equal("[REDACTED]", redacted[0].Value);
        Assert.Equal("[REDACTED]", redacted[1].Value);
        Assert.True(redacted[1].IsSecret);
        Assert.DoesNotContain("unflagged_secret", redacted[1].Value);
    }

    /// <summary>
    /// Residual 3: Verifies the compliance read surface and the export listing honour the role matrix.
    /// </summary>
    [Fact]
    public async Task ComplianceReadsAndExportListing_EnforceTheRoleMatrix()
    {
        // WHAT: Reads the compliance status, audit ledger, ledger verification, and export listing as each role.
        // WHY:  An FDA 21 CFR Part 11 audit ledger readable by every Patient token is a plain disclosure, and
        //       the export listing belongs with the dataset export/import grant.
        // HOW:  Asserts Administrator-only for status and the ledger, Administrator or Researcher for the
        //       integrity check and the export listing, and 403 for the Patient role throughout.
        var (adminToken, researcherToken, patientToken) = await ProvisionRoleTokensAsync();

        using HttpClient adminClient = CreateClientWithToken(adminToken);
        using HttpClient researcherClient = CreateClientWithToken(researcherToken);
        using HttpClient patientClient = CreateClientWithToken(patientToken);

        string[] administratorOnlyReads = ["/api/compliance/status", "/api/compliance/audit-ledger?limit=5"];
        foreach (string route in administratorOnlyReads)
        {
            Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await researcherClient.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await patientClient.GetAsync(route)).StatusCode);
        }

        string[] researchReads = ["/api/compliance/verify-ledger", "/api/datasets/exports"];
        foreach (string route in researchReads)
        {
            Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await researcherClient.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await patientClient.GetAsync(route)).StatusCode);
        }
    }

    /// <summary>
    /// Residual 4: Verifies the storage descriptor helper names the setting instead of the path.
    /// </summary>
    [Fact]
    public void DescribeStorageLocation_NamesTheSettingNotThePath()
    {
        // WHAT: Formats a descriptor for a configured storage directory.
        // WHY:  Health telemetry must answer "is storage reachable" without publishing the mount layout.
        // HOW:  Asserts the sentence names the variable, contains no separator-prefixed path, and rejects null.
        string descriptor = Program.DescribeStorageLocation("TRAJECTORY_INCOMING_DIR");

        Assert.Equal("Configured by TRAJECTORY_INCOMING_DIR; absolute path withheld.", descriptor);
        Assert.DoesNotContain("/", descriptor);
        Assert.Throws<ArgumentNullException>(() => Program.DescribeStorageLocation(null!));
    }
}
