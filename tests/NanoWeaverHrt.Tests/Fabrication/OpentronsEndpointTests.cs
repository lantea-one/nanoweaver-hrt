using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication.Opentrons;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.ExceptionHandling;
using NanoWeaverHrt.Security.Models;
using Xunit;

namespace NanoWeaverHrt.Tests.Fabrication;

/// <summary>
/// Integration and unit test suite for the Opentrons liquid handler REST API endpoints
/// (<c>/api/fabrication/opentrons/generate</c> and <c>/api/fabrication/opentrons/download</c>),
/// validating protocol generation, file streaming, MIME types, input validation, and role gating.
/// </summary>
public sealed class OpentronsEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    /// <summary>
    /// Temporary directory isolated from the live simulation volume for SQLite and test fixtures.
    /// </summary>
    private readonly string _tempDataDir;

    /// <summary>
    /// Isolated web application factory bound to the temporary data directory.
    /// </summary>
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>
    /// Administrator Bearer authentication token.
    /// </summary>
    private string? _adminToken;

    /// <summary>
    /// Researcher Bearer authentication token.
    /// </summary>
    private string? _researcherToken;

    /// <summary>
    /// Patient Bearer authentication token.
    /// </summary>
    private string? _patientToken;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpentronsEndpointTests"/> class.
    /// </summary>
    /// <param name="baseFactory">Base application factory fixture.</param>
    public OpentronsEndpointTests(WebApplicationFactory<Program> baseFactory)
    {
        // WHAT: Initializes isolated test directory and binds WebApplicationFactory configuration.
        // WHY:  Prevents concurrency conflicts with other test classes running against SQLite or trajectory paths.
        // HOW:  Creates a unique temporary folder and overrides environment paths in WebApplicationFactory.
        ArgumentNullException.ThrowIfNull(baseFactory);

        _tempDataDir = Path.Combine(Path.GetTempPath(), "weaver_opentrons_test_" + Guid.NewGuid().ToString("N"));
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
        // WHAT: Tears down isolated test directory and disposes application factory instance.
        // WHY:  Frees host resources and removes temporary test artifacts from disk.
        // HOW:  Disposes the factory and deletes _tempDataDir recursively.
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
    /// Ensures that test user accounts (Administrator, Researcher, Patient) are provisioned and mints tokens.
    /// </summary>
    /// <returns>A task representing the asynchronous setup operation.</returns>
    private async Task EnsureTokensProvisionedAsync()
    {
        // WHAT: Provisions administrator, researcher, and patient test accounts.
        // WHY:  Guarantees signed Bearer tokens are available for testing role gating.
        // HOW:  Checks setup state on UserAuthenticationService, initializes admin if needed, and creates test accounts.
        if (_adminToken != null && _researcherToken != null && _patientToken != null)
        {
            return;
        }

        using IServiceScope scope = _factory.Services.CreateScope();
        IUserAuthenticationService authService = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();

        string adminUsername = "admin_ot_" + Guid.NewGuid().ToString("N")[..8];
        string resUsername = "res_ot_" + Guid.NewGuid().ToString("N")[..8];
        string patUsername = "pat_ot_" + Guid.NewGuid().ToString("N")[..8];

        if (await authService.IsSetupRequiredAsync())
        {
            GeneratedKeyBundle adminBundle = TestChallengeHelper.CreateTestKeyBundle(adminUsername, UserRole.Administrator, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
            UserAccount admin = await authService.CompleteInitialSetupAsync(adminUsername, adminBundle.PublicKeyBase64, KeyAlgorithm.Ed25519, AccessGate.ApiWeb);
            _adminToken = authService.GenerateBearerToken(admin);
        }
        else
        {
            IReadOnlyList<UserAccount> users = await authService.ListUsersAsync();
            UserAccount? existingAdmin = users.FirstOrDefault(u => u.Role == UserRole.Administrator);
            if (existingAdmin != null)
            {
                _adminToken = authService.GenerateBearerToken(existingAdmin);
            }
            else
            {
                GeneratedKeyBundle adminBundle = TestChallengeHelper.CreateTestKeyBundle(adminUsername, UserRole.Administrator, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
                UserAccount admin = await authService.CreateUserAsync(adminUsername, UserRole.Administrator, AccessGate.ApiWeb, adminBundle.PublicKeyBase64, KeyAlgorithm.Ed25519);
                _adminToken = authService.GenerateBearerToken(admin);
            }
        }

        GeneratedKeyBundle resBundle = TestChallengeHelper.CreateTestKeyBundle(resUsername, UserRole.Researcher, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
        UserAccount res = await authService.CreateUserAsync(resUsername, UserRole.Researcher, AccessGate.ApiWeb, resBundle.PublicKeyBase64, KeyAlgorithm.Ed25519);
        _researcherToken = authService.GenerateBearerToken(res);

        GeneratedKeyBundle patBundle = TestChallengeHelper.CreateTestKeyBundle(patUsername, UserRole.Patient, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
        UserAccount pat = await authService.CreateUserAsync(patUsername, UserRole.Patient, AccessGate.ApiWeb, patBundle.PublicKeyBase64, KeyAlgorithm.Ed25519);
        _patientToken = authService.GenerateBearerToken(pat);
    }

    /// <summary>
    /// Creates an HTTP client configured with an optional Bearer authentication token.
    /// </summary>
    /// <param name="token">Optional Bearer token string.</param>
    /// <returns>A configured <see cref="HttpClient"/> instance.</returns>
    private HttpClient CreateClient(string? token)
    {
        // WHAT: Constructs an HttpClient with optional Bearer authorization header.
        // WHY:  Allows making requests across authenticated and unauthenticated roles.
        // HOW:  Creates client from _factory and sets Authorization header if token is non-empty.
        HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        if (!string.IsNullOrEmpty(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    /// <summary>
    /// Verifies that POST /api/fabrication/opentrons/generate generates valid Python protocol scripts and metadata.
    /// </summary>
    [Fact]
    public async Task GenerateProtocol_ValidRequest_ReturnsOkWithScriptAndMetadata()
    {
        // WHAT: Calls /api/fabrication/opentrons/generate with a valid preset sample ID.
        // WHY:  Validates that researchers can synthesize complete Opentrons scripts and deck layouts.
        // HOW:  Sends POST request as Researcher and asserts 200 OK with valid OpentronsProtocolResult fields.
        await EnsureTokensProvisionedAsync();
        using HttpClient client = CreateClient(_researcherToken);

        GenerateOpentronsScriptRequest request = new()
        {
            SequenceOrSampleId = "CAGE-6HB-E2-001",
            Regimen = TherapeuticRegimen.MaleToFemale,
            Geometry = "Hexagonal 6-Helix Bundle Nanotube (6HB)",
            TargetKd = 4.2,
            Options = new OpentronsProtocolOptions
            {
                RobotModel = OpentronsRobotModel.OT2,
                PipetteModel = OpentronsPipetteModel.P20_Single_Gen2,
                SourcePlateType = OpentronsPlateType.Nest96WellPcr,
                UseTemperatureModule = true,
                UseThermocyclerModule = false,
                TotalReactionVolumeMicroliters = 50.0,
                ScaffoldConcentrationNanomolar = 10.0,
                StapleMolarExcessRatio = 10.0
            }
        };

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/fabrication/opentrons/generate", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        OpentronsProtocolResult? result = await response.Content.ReadFromJsonAsync<OpentronsProtocolResult>();
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.PythonScript));
        Assert.Contains("metadata = {", result.PythonScript, StringComparison.Ordinal);
        Assert.Contains("def run(protocol", result.PythonScript, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(result.ScriptSha256));
        Assert.Equal(64, result.ScriptSha256.Length);
        Assert.True(result.TransferCount > 0);
        Assert.True(result.SourcePlatesRequired > 0);
        Assert.True(result.TipRacksRequired > 0);
        Assert.False(result.HasThermocyclerRamp);
        Assert.True(result.EstimatedRunDurationMinutes > 0);
    }

    /// <summary>
    /// Verifies that POST /api/fabrication/opentrons/download streams a Python script attachment with correct MIME headers.
    /// </summary>
    [Fact]
    public async Task DownloadProtocol_ValidRequest_ReturnsFileAttachmentWithCorrectMimeType()
    {
        // WHAT: Calls /api/fabrication/opentrons/download with valid parameters.
        // WHY:  Validates that researchers can download standalone .py protocol files directly for liquid handlers.
        // HOW:  Sends POST request, asserts 200 OK, Content-Type "text/x-python", Content-Disposition filename, and script body.
        await EnsureTokensProvisionedAsync();
        using HttpClient client = CreateClient(_researcherToken);

        GenerateOpentronsScriptRequest request = new()
        {
            SequenceOrSampleId = "CAGE-6HB-E2-001",
            Regimen = TherapeuticRegimen.FemaleToMale,
            Geometry = "Cuboid",
            TargetKd = 5.0,
            Options = new OpentronsProtocolOptions
            {
                RobotModel = OpentronsRobotModel.Flex,
                PipetteModel = OpentronsPipetteModel.Flex_1Channel_50,
                SourcePlateType = OpentronsPlateType.Nest96WellPcr
            }
        };

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/fabrication/opentrons/download", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/x-python", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.NotNull(response.Content.Headers.ContentDisposition);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition.DispositionType);
        Assert.Equal("nanoweaver_origami_opentrons.py", response.Content.Headers.ContentDisposition.FileName);

        string script = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(script));
        Assert.Contains("metadata = {", script, StringComparison.Ordinal);
        Assert.Contains("protocol.load_instrument", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that POST /api/fabrication/opentrons/generate handles default options, null options, and zero TargetKd.
    /// </summary>
    [Fact]
    public async Task GenerateAndDownload_DefaultsAndFallbackBranches_HandledCorrectly()
    {
        // WHAT: Tests endpoints when Geometry is whitespace, TargetKd is 0, and Options is null or default.
        // WHY:  Ensures full branch coverage for fallback values (Geometry="Nanotube", TargetKd=4.8, Options=new()).
        // HOW:  Sends requests with empty geometry, TargetKd 0, and null options, verifying 200 OK.
        await EnsureTokensProvisionedAsync();
        using HttpClient client = CreateClient(_adminToken);

        string jsonPayload = """
        {
            "sequenceOrSampleId": "CAGE-6HB-E2-001",
            "regimen": 0,
            "geometry": "",
            "targetKd": 0.0,
            "options": null
        }
        """;

        using StringContent generateContent = new(jsonPayload, System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage generateRes = await client.PostAsync("/api/fabrication/opentrons/generate", generateContent);
        Assert.Equal(HttpStatusCode.OK, generateRes.StatusCode);
        OpentronsProtocolResult? genResult = await generateRes.Content.ReadFromJsonAsync<OpentronsProtocolResult>();
        Assert.NotNull(genResult);
        Assert.False(string.IsNullOrWhiteSpace(genResult.PythonScript));

        using StringContent downloadContent = new(jsonPayload, System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage downloadRes = await client.PostAsync("/api/fabrication/opentrons/download", downloadContent);
        Assert.Equal(HttpStatusCode.OK, downloadRes.StatusCode);
        string script = await downloadRes.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(script));
    }

    /// <summary>
    /// Verifies that POST /api/fabrication/opentrons/generate and download reject missing or empty sequences with RFC 7807 problem details.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Endpoints_EmptySequence_ReturnsBadRequestProblemDetails(string emptySequence)
    {
        // WHAT: Tests validation rejection when SequenceOrSampleId is empty or whitespace.
        // WHY:  A protocol cannot be synthesized without a sequence target or sample identifier.
        // HOW:  Asserts 400 Bad Request, application/problem+json, and RFC 7807 problem details.
        await EnsureTokensProvisionedAsync();
        using HttpClient client = CreateClient(_researcherToken);

        GenerateOpentronsScriptRequest request = new()
        {
            SequenceOrSampleId = emptySequence
        };

        using HttpResponseMessage generateRes = await client.PostAsJsonAsync("/api/fabrication/opentrons/generate", request);
        Assert.Equal(HttpStatusCode.BadRequest, generateRes.StatusCode);
        Assert.Equal("application/problem+json", generateRes.Content.Headers.ContentType?.MediaType);
        SecureProblemDetails? genProblem = await generateRes.Content.ReadFromJsonAsync<SecureProblemDetails>();
        Assert.NotNull(genProblem);
        Assert.Equal(400, genProblem.Status);
        Assert.Contains("SequenceOrSampleId cannot be empty.", genProblem.Detail, StringComparison.Ordinal);

        using HttpResponseMessage downloadRes = await client.PostAsJsonAsync("/api/fabrication/opentrons/download", request);
        Assert.Equal(HttpStatusCode.BadRequest, downloadRes.StatusCode);
        Assert.Equal("application/problem+json", downloadRes.Content.Headers.ContentType?.MediaType);
        SecureProblemDetails? dlProblem = await downloadRes.Content.ReadFromJsonAsync<SecureProblemDetails>();
        Assert.NotNull(dlProblem);
        Assert.Equal(400, dlProblem.Status);
        Assert.Contains("SequenceOrSampleId cannot be empty.", dlProblem.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that invalid protocol options (non-positive volume, concentration, or ratio) are rejected with RFC 7807 details.
    /// </summary>
    [Fact]
    public async Task Endpoints_InvalidBiophysicalOptions_ReturnsBadRequestProblemDetails()
    {
        // WHAT: Tests validation rules for non-positive volume, scaffold concentration, staple excess ratio, and negative TargetKd.
        // WHY:  Non-positive physical parameters break stoichiometric pipetting and deck allocation math.
        // HOW:  Calls endpoints with invalid parameters and verifies 400 Bad Request with specific problem details.
        await EnsureTokensProvisionedAsync();
        using HttpClient client = CreateClient(_researcherToken);

        // 1. Non-positive total reaction volume
        GenerateOpentronsScriptRequest zeroVolumeReq = new()
        {
            SequenceOrSampleId = "CAGE-6HB-E2-001",
            Options = new OpentronsProtocolOptions { TotalReactionVolumeMicroliters = 0 }
        };
        using HttpResponseMessage zeroVolRes = await client.PostAsJsonAsync("/api/fabrication/opentrons/generate", zeroVolumeReq);
        Assert.Equal(HttpStatusCode.BadRequest, zeroVolRes.StatusCode);
        SecureProblemDetails? zeroVolProblem = await zeroVolRes.Content.ReadFromJsonAsync<SecureProblemDetails>();
        Assert.NotNull(zeroVolProblem);
        Assert.Contains("TotalReactionVolumeMicroliters must be greater than zero.", zeroVolProblem.Detail, StringComparison.Ordinal);

        // 2. Non-positive scaffold concentration
        GenerateOpentronsScriptRequest negConcReq = new()
        {
            SequenceOrSampleId = "CAGE-6HB-E2-001",
            Options = new OpentronsProtocolOptions { ScaffoldConcentrationNanomolar = -1.0 }
        };
        using HttpResponseMessage negConcRes = await client.PostAsJsonAsync("/api/fabrication/opentrons/generate", negConcReq);
        Assert.Equal(HttpStatusCode.BadRequest, negConcRes.StatusCode);
        SecureProblemDetails? negConcProblem = await negConcRes.Content.ReadFromJsonAsync<SecureProblemDetails>();
        Assert.NotNull(negConcProblem);
        Assert.Contains("ScaffoldConcentrationNanomolar must be greater than zero.", negConcProblem.Detail, StringComparison.Ordinal);

        // 3. Non-positive staple molar excess ratio
        GenerateOpentronsScriptRequest zeroRatioReq = new()
        {
            SequenceOrSampleId = "CAGE-6HB-E2-001",
            Options = new OpentronsProtocolOptions { StapleMolarExcessRatio = 0 }
        };
        using HttpResponseMessage zeroRatioRes = await client.PostAsJsonAsync("/api/fabrication/opentrons/generate", zeroRatioReq);
        Assert.Equal(HttpStatusCode.BadRequest, zeroRatioRes.StatusCode);
        SecureProblemDetails? zeroRatioProblem = await zeroRatioRes.Content.ReadFromJsonAsync<SecureProblemDetails>();
        Assert.NotNull(zeroRatioProblem);
        Assert.Contains("StapleMolarExcessRatio must be greater than zero.", zeroRatioProblem.Detail, StringComparison.Ordinal);

        // 4. Negative TargetKd
        GenerateOpentronsScriptRequest negKdReq = new()
        {
            SequenceOrSampleId = "CAGE-6HB-E2-001",
            TargetKd = -2.5
        };
        using HttpResponseMessage negKdRes = await client.PostAsJsonAsync("/api/fabrication/opentrons/generate", negKdReq);
        Assert.Equal(HttpStatusCode.BadRequest, negKdRes.StatusCode);
        SecureProblemDetails? negKdProblem = await negKdRes.Content.ReadFromJsonAsync<SecureProblemDetails>();
        Assert.NotNull(negKdProblem);
        Assert.Contains("TargetKd cannot be negative.", negKdProblem.Detail, StringComparison.Ordinal);

        // 5. Download endpoint also rejects invalid parameters
        using HttpResponseMessage dlNegKdRes = await client.PostAsJsonAsync("/api/fabrication/opentrons/download", negKdReq);
        Assert.Equal(HttpStatusCode.BadRequest, dlNegKdRes.StatusCode);
        SecureProblemDetails? dlNegKdProblem = await dlNegKdRes.Content.ReadFromJsonAsync<SecureProblemDetails>();
        Assert.NotNull(dlNegKdProblem);
        Assert.Contains("TargetKd cannot be negative.", dlNegKdProblem.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that unauthenticated callers are rejected with 401 Unauthorized across Opentrons endpoints.
    /// </summary>
    [Fact]
    public async Task Endpoints_UnauthenticatedRequest_ReturnsUnauthorized()
    {
        // WHAT: Tests unauthenticated access to /api/fabrication/opentrons/generate and download.
        // WHY:  Prime Directive 12 requires all REST endpoints to enforce asymmetric PKI challenge authentication.
        // HOW:  Ensures setup is completed, sends POST requests without Bearer token and asserts 401 Unauthorized.
        await EnsureTokensProvisionedAsync();
        using HttpClient anonymousClient = CreateClient(null);

        GenerateOpentronsScriptRequest request = new()
        {
            SequenceOrSampleId = "CAGE-6HB-E2-001"
        };

        using HttpResponseMessage generateRes = await anonymousClient.PostAsJsonAsync("/api/fabrication/opentrons/generate", request);
        Assert.Equal(HttpStatusCode.Unauthorized, generateRes.StatusCode);

        using HttpResponseMessage downloadRes = await anonymousClient.PostAsJsonAsync("/api/fabrication/opentrons/download", request);
        Assert.Equal(HttpStatusCode.Unauthorized, downloadRes.StatusCode);
    }

    /// <summary>
    /// Verifies that unauthorized Patient accounts are rejected with 403 Forbidden across Opentrons endpoints.
    /// </summary>
    [Fact]
    public async Task Endpoints_PatientRole_ReturnsForbidden()
    {
        // WHAT: Tests access by an authenticated Patient account to Opentrons endpoints.
        // WHY:  Prime Directive 12 gates wet-lab automation and liquid handler script generation to Researcher and Administrator roles.
        // HOW:  Sends POST requests with Patient token and asserts 403 Forbidden with RFC 7807 problem details.
        await EnsureTokensProvisionedAsync();
        using HttpClient patientClient = CreateClient(_patientToken);

        GenerateOpentronsScriptRequest request = new()
        {
            SequenceOrSampleId = "CAGE-6HB-E2-001"
        };

        using HttpResponseMessage generateRes = await patientClient.PostAsJsonAsync("/api/fabrication/opentrons/generate", request);
        Assert.Equal(HttpStatusCode.Forbidden, generateRes.StatusCode);

        using HttpResponseMessage downloadRes = await patientClient.PostAsJsonAsync("/api/fabrication/opentrons/download", request);
        Assert.Equal(HttpStatusCode.Forbidden, downloadRes.StatusCode);
    }

    /// <summary>
    /// Verifies that Administrator accounts are permitted to access both Opentrons endpoints.
    /// </summary>
    [Fact]
    public async Task Endpoints_AdministratorRole_ReturnsOk()
    {
        // WHAT: Tests access by an authenticated Administrator account.
        // WHY:  Administrator role possesses full cluster oversight and execution permissions.
        // HOW:  Sends POST requests with Administrator token and asserts 200 OK.
        await EnsureTokensProvisionedAsync();
        using HttpClient adminClient = CreateClient(_adminToken);

        GenerateOpentronsScriptRequest request = new()
        {
            SequenceOrSampleId = "CAGE-6HB-E2-001"
        };

        using HttpResponseMessage generateRes = await adminClient.PostAsJsonAsync("/api/fabrication/opentrons/generate", request);
        Assert.Equal(HttpStatusCode.OK, generateRes.StatusCode);

        using HttpResponseMessage downloadRes = await adminClient.PostAsJsonAsync("/api/fabrication/opentrons/download", request);
        Assert.Equal(HttpStatusCode.OK, downloadRes.StatusCode);
    }

    /// <summary>
    /// Verifies that <see cref="Program.ValidateOpentronsRequest"/> correctly identifies all valid and invalid parameter combinations.
    /// </summary>
    [Fact]
    public void ValidateOpentronsRequest_DirectUnitTests_CoverAllBranches()
    {
        // WHAT: Directly invokes Program.ValidateOpentronsRequest across all branch conditions.
        // WHY:  Ensures 100% line and branch coverage on internal validation routines.
        // HOW:  Tests valid request (returns null) and each invalid parameter branch (returns SecureProblemResult).
        DefaultHttpContext context = new();

        // 1. Valid request returns null
        GenerateOpentronsScriptRequest validReq = new()
        {
            SequenceOrSampleId = "CAGE-6HB-E2-001",
            TargetKd = 4.2,
            Options = new OpentronsProtocolOptions
            {
                TotalReactionVolumeMicroliters = 50.0,
                ScaffoldConcentrationNanomolar = 10.0,
                StapleMolarExcessRatio = 10.0
            }
        };
        Assert.Null(Program.ValidateOpentronsRequest(validReq, context));

        // 2. Empty sequence returns 400
        GenerateOpentronsScriptRequest emptySeqReq = new() { SequenceOrSampleId = "" };
        Assert.NotNull(Program.ValidateOpentronsRequest(emptySeqReq, context));

        // 3. Zero volume returns 400
        GenerateOpentronsScriptRequest zeroVolReq = new()
        {
            SequenceOrSampleId = "CAGE-6HB-E2-001",
            Options = new OpentronsProtocolOptions { TotalReactionVolumeMicroliters = 0.0 }
        };
        Assert.NotNull(Program.ValidateOpentronsRequest(zeroVolReq, context));

        // 4. Zero scaffold concentration returns 400
        GenerateOpentronsScriptRequest zeroScaffReq = new()
        {
            SequenceOrSampleId = "CAGE-6HB-E2-001",
            Options = new OpentronsProtocolOptions { ScaffoldConcentrationNanomolar = 0.0 }
        };
        Assert.NotNull(Program.ValidateOpentronsRequest(zeroScaffReq, context));

        // 5. Zero staple molar excess ratio returns 400
        GenerateOpentronsScriptRequest zeroRatioReq = new()
        {
            SequenceOrSampleId = "CAGE-6HB-E2-001",
            Options = new OpentronsProtocolOptions { StapleMolarExcessRatio = 0.0 }
        };
        Assert.NotNull(Program.ValidateOpentronsRequest(zeroRatioReq, context));

        // 6. Negative TargetKd returns 400
        GenerateOpentronsScriptRequest negKdReq = new()
        {
            SequenceOrSampleId = "CAGE-6HB-E2-001",
            TargetKd = -1.0
        };
        Assert.NotNull(Program.ValidateOpentronsRequest(negKdReq, context));
    }

    /// <summary>
    /// Verifies that <see cref="GenerateOpentronsScriptRequest.GetExamples"/> generates valid sample DTO instances.
    /// </summary>
    [Fact]
    public void GenerateOpentronsScriptRequest_GetExamples_ReturnsValidPopulatedModel()
    {
        // WHAT: Verifies the IExamplesProvider implementation on GenerateOpentronsScriptRequest.
        // WHY:  Guarantees OpenAPI documentation filters receive populated, valid example models.
        // HOW:  Invokes GetExamples and asserts properties are populated with non-null defaults.
        GenerateOpentronsScriptRequest request = new()
        {
            SequenceOrSampleId = "TEST-SEQ"
        };

        GenerateOpentronsScriptRequest example = request.GetExamples();
        Assert.NotNull(example);
        Assert.Equal("CAGE-6HB-E2-001", example.SequenceOrSampleId);
        Assert.Equal(TherapeuticRegimen.MaleToFemale, example.Regimen);
        Assert.Equal("Nanotube", example.Geometry);
        Assert.Equal(4.8, example.TargetKd);
        Assert.NotNull(example.Options);
    }
}
