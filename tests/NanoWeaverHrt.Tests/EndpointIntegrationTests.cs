using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Genomics;
using NanoWeaverHrt.Models;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Integration test suite verifying HTTP endpoints exposed by the minimal API service in <see cref="Program"/>.
/// </summary>
public sealed class EndpointIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>
    /// Test server web application factory.
    /// </summary>
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>
    /// Reusable HTTP client connected to in-memory test server.
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="EndpointIntegrationTests"/> class.
    /// </summary>
    /// <param name="factory">The test web application factory fixture.</param>
    public EndpointIntegrationTests(WebApplicationFactory<Program> factory)
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "NanoWeaverHrt.slnx")))
        {
            current = Directory.GetParent(current)?.FullName;
        }
        string solutionRoot = current ?? Directory.GetCurrentDirectory();
        string dataDir = Path.Combine(solutionRoot, "simulations", "shared-data");
        string exportDir = Path.Combine(solutionRoot, "simulations", "exports");
        string parquetDir = Path.Combine(solutionRoot, "simulations", "parquet");

        TestFixturesHelper.EnsureTestFixtures(dataDir);

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("TRAJECTORY_INCOMING_DIR", dataDir);
            builder.UseSetting("TRAJECTORY_EXPORT_DIR", exportDir);
            builder.UseSetting("TRAJECTORY_PARQUET_DIR", parquetDir);
        });

        _client = _factory.CreateClient();
        TestFixturesHelper.ConfigureAuthenticatedClient(_client, _factory);
    }

    /// <summary>
    /// Verifies that GET /api/datasets returns HTTP 200 OK with a list of discovered files.
    /// </summary>
    [Fact]
    public async Task GetDatasets_ShouldReturnOk()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/datasets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        TrajectoryFileItem[]? items = await response.Content.ReadFromJsonAsync<TrajectoryFileItem[]>();
        Assert.NotNull(items);
    }

    /// <summary>
    /// Verifies that POST /api/datasets/export validates request inputs and creates bundles.
    /// </summary>
    [Fact]
    public async Task ExportDataset_ValidationAndSuccessPaths()
    {
        // Path 1: Empty dataset name -> 400 Bad Request
        ExportRequest badRequest = new() { DatasetName = "" };
        HttpResponseMessage badResponse = await _client.PostAsJsonAsync("/api/datasets/export", badRequest);
        Assert.Equal(HttpStatusCode.BadRequest, badResponse.StatusCode);

        // Path 2: Valid request -> 200 OK
        ExportRequest validRequest = new() { DatasetName = "IntegrationTestDataset", Files = ["dna_nanotube_sample.xyz"] };
        HttpResponseMessage okResponse = await _client.PostAsJsonAsync("/api/datasets/export", validRequest);
        Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);

        DatasetManifest? manifest = await okResponse.Content.ReadFromJsonAsync<DatasetManifest>();
        Assert.NotNull(manifest);
        Assert.Equal("IntegrationTestDataset", manifest.DatasetName);
    }

    /// <summary>
    /// Verifies GET /api/datasets/download/{fileName} handles missing and existing files.
    /// </summary>
    [Fact]
    public async Task DownloadExport_ShouldHandleNotFound()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/datasets/download/nonexistent_bundle.tar.gz");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Verifies that GET /api/trajectories/download/{fileName} handles existing trajectories and missing files.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadTrajectory_ExistingAndNotFound()
    {
        // WHAT: Tests the raw trajectory download endpoint with both an existing and non-existent file.
        // WHY:  Ensures operators and downstream tools can fetch coordinate data over HTTP without errors.
        // HOW:  Sends GET requests to /api/trajectories/download/{fileName} and asserts 200 OK and 404 NotFound.
        HttpResponseMessage okResp = await _client.GetAsync("/api/trajectories/download/dna_nanotube_sample.xyz");
        Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);
        Assert.Equal("application/octet-stream", okResp.Content.Headers.ContentType?.MediaType);

        HttpResponseMessage notFoundResp = await _client.GetAsync("/api/trajectories/download/nonexistent_sample.xyz");
        Assert.Equal(HttpStatusCode.NotFound, notFoundResp.StatusCode);
    }

    /// <summary>
    /// Verifies that GET /api/datasets/exports and DELETE /api/datasets/exports/{fileName} manage export bundles cleanly.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ListExports_And_DeleteExport_Endpoints()
    {
        // WHAT: Tests listing and deleting exported simulation bundles via REST endpoints.
        // WHY:  Ensures full CRUD lifecycle management of export packages over HTTP.
        // HOW:  Creates a temporary export file, verifies its presence in GET /api/datasets/exports,
        //       deletes it via DELETE /api/datasets/exports/{fileName}, and asserts 404 on subsequent deletion.
        string solutionRoot = TestFixturesHelper.GetSolutionRoot();
        string exportDir = Path.Combine(solutionRoot, "simulations", "exports");
        Directory.CreateDirectory(exportDir);

        string testFileName = $"integration_export_{Guid.NewGuid():N}.tar.gz";
        string fullPath = Path.Combine(exportDir, testFileName);
        await File.WriteAllTextAsync(fullPath, "temporary integration archive data");

        try
        {
            HttpResponseMessage listResp = await _client.GetAsync("/api/datasets/exports");
            Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
            ExportFileItem[]? items = await listResp.Content.ReadFromJsonAsync<ExportFileItem[]>();
            Assert.NotNull(items);
            Assert.Contains(items, item => item.Name == testFileName);

            HttpResponseMessage deleteResp = await _client.DeleteAsync($"/api/datasets/exports/{testFileName}");
            Assert.Equal(HttpStatusCode.OK, deleteResp.StatusCode);

            HttpResponseMessage deleteAgainResp = await _client.DeleteAsync($"/api/datasets/exports/{testFileName}");
            Assert.Equal(HttpStatusCode.NotFound, deleteAgainResp.StatusCode);
        }
        finally
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
    }

    /// <summary>
    /// Verifies that POST /api/datasets/import rejects missing files and bad data.
    /// </summary>
    [Fact]
    public async Task ImportDataset_ShouldRejectMissingFiles()
    {
        using MultipartFormDataContent emptyContent = new();
        HttpResponseMessage response = await _client.PostAsync("/api/datasets/import", emptyContent);

        // Missing form files triggers bad request
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Verifies GET /api/system/telemetry returns runtime metrics.
    /// </summary>
    [Fact]
    public async Task GetSystemTelemetry_ShouldReturnValidMetrics()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/system/telemetry");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SystemTelemetry? telemetry = await response.Content.ReadFromJsonAsync<SystemTelemetry>();
        Assert.NotNull(telemetry);
        Assert.False(string.IsNullOrWhiteSpace(telemetry.NodeName));
        Assert.True(telemetry.WorkingSetMb > 0);
    }

    /// <summary>
    /// Verifies that standard OWASP security headers (X-Content-Type-Options, X-Frame-Options, Referrer-Policy, Permissions-Policy)
    /// are attached to HTTP responses for defense-in-depth hardening.
    /// </summary>
    [Fact]
    public async Task ResponseHeaders_ShouldContainSecurityHardeningHeaders()
    {
        // WHAT: Dispatches a request to an API endpoint and inspects HTTP response headers.
        // WHY:  Ensures that application-level defense-in-depth security headers are injected on all responses.
        // HOW:  Asserts presence of X-Content-Type-Options, X-Frame-Options, Referrer-Policy, and Permissions-Policy.
        HttpResponseMessage response = await _client.GetAsync("/api/system/telemetry");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").FirstOrDefault());

        Assert.True(response.Headers.Contains("X-Frame-Options"));
        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").FirstOrDefault());

        Assert.True(response.Headers.Contains("Referrer-Policy"));
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").FirstOrDefault());

        Assert.True(response.Headers.Contains("Permissions-Policy"));
    }

    /// <summary>
    /// Verifies GET /api/endocrine/profile, POST /api/endocrine/toggle, and GET /api/endocrine/kinetics.
    /// </summary>
    [Fact]
    public async Task EndocrineEndpoints_ShouldProvideProfileAndHandleToggle()
    {
        // 1. Get Profile
        HttpResponseMessage profileResponse = await _client.GetAsync("/api/endocrine/profile");
        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
        EndocrineTargetProfile? profile = await profileResponse.Content.ReadFromJsonAsync<EndocrineTargetProfile>();
        Assert.NotNull(profile);

        // 2. Toggle State
        ToggleSwitchRequest toggleRequest = new()
        {
            TargetState = ToggleState.AndrogenicDominant,
            Regimen = TherapeuticRegimen.FemaleToMale
        };
        HttpResponseMessage toggleResponse = await _client.PostAsJsonAsync("/api/endocrine/toggle", toggleRequest);
        Assert.Equal(HttpStatusCode.OK, toggleResponse.StatusCode);
        EndocrineTargetProfile? updated = await toggleResponse.Content.ReadFromJsonAsync<EndocrineTargetProfile>();
        Assert.NotNull(updated);
        Assert.Equal(ToggleState.AndrogenicDominant, updated.TargetState);

        // 3. Get Kinetics
        HttpResponseMessage kineticsResponse = await _client.GetAsync("/api/endocrine/kinetics");
        Assert.Equal(HttpStatusCode.OK, kineticsResponse.StatusCode);
        HormoneCurvePoint[]? kinetics = await kineticsResponse.Content.ReadFromJsonAsync<HormoneCurvePoint[]>();
        Assert.NotNull(kinetics);
        Assert.Equal(19, kinetics.Length); // 0 to 72 hours with 4-hour step = 19 points
    }

    /// <summary>
    /// Verifies /api/parser/process/{fileName} handles missing and valid trajectory formats.
    /// </summary>
    [Fact]
    public async Task ProcessTrajectory_ShouldHandleMissingFilesAndFormats()
    {
        // 1. Missing file -> 404 Not Found
        HttpResponseMessage notFoundResponse = await _client.PostAsync("/api/parser/process/missing_sample.xyz", null);
        Assert.Equal(HttpStatusCode.NotFound, notFoundResponse.StatusCode);

        // 2. Valid sample file -> 200 OK
        HttpResponseMessage okResponse = await _client.PostAsync("/api/parser/process/dna_nanotube_sample.xyz", null);
        Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);
    }

    /// <summary>
    /// Verifies /api/parser/export-parquet/{fileName} converts trajectory files into Parquet.
    /// </summary>
    [Fact]
    public async Task ExportParquet_ShouldHandleMissingAndValidFiles()
    {
        // 1. Missing file -> 404 Not Found
        HttpResponseMessage notFound = await _client.PostAsync("/api/parser/export-parquet/missing.xyz", null);
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);

        // 2. Existing sample file -> 200 OK
        HttpResponseMessage ok = await _client.PostAsync("/api/parser/export-parquet/dna_nanotube_sample.xyz", null);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    /// <summary>
    /// Verifies /api/endocrine/analyze/{fileName} handles missing and valid trajectory streams.
    /// </summary>
    [Fact]
    public async Task AnalyzeAptamerAffinity_ShouldHandleMissingAndValidFiles()
    {
        // 1. Missing file -> 404 Not Found
        HttpResponseMessage notFound = await _client.PostAsync("/api/endocrine/analyze/missing.xyz", null);
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);

        // 2. Existing sample file -> 200 OK
        HttpResponseMessage ok = await _client.PostAsync("/api/endocrine/analyze/dna_nanotube_sample.xyz", null);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        AptamerTrajectoryMetrics? metrics = await ok.Content.ReadFromJsonAsync<AptamerTrajectoryMetrics>();
        Assert.NotNull(metrics);
        Assert.True(metrics.FramesAnalyzed > 0);
    }

    /// <summary>
    /// Verifies GET /api/datasets/download/{fileName} successfully streams existing exported files.
    /// </summary>
    [Fact]
    public async Task DownloadExport_ShouldReturnFile_WhenExists()
    {
        // WHAT: Creates a test file in the export directory and verifies successful download.
        // WHY:  Ensures researchers can download cryptographically signed bundles over HTTP.
        // HOW:  Writes a temporary export file and requests it via the download endpoint.
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "NanoWeaverHrt.slnx")))
        {
            current = Directory.GetParent(current)?.FullName;
        }
        string solutionRoot = current ?? Directory.GetCurrentDirectory();
        string exportDir = Path.Combine(solutionRoot, "simulations", "exports");
        Directory.CreateDirectory(exportDir);
        string testFile = Path.Combine(exportDir, "test_download.tar.gz");
        await File.WriteAllTextAsync(testFile, "MOCK_TAR_GZ_CONTENT");

        try
        {
            HttpResponseMessage response = await _client.GetAsync("/api/datasets/download/test_download.tar.gz");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.NotEmpty(bytes);
        }
        finally
        {
            if (File.Exists(testFile))
            {
                File.Delete(testFile);
            }
        }
    }

    /// <summary>
    /// Verifies POST /api/datasets/import handles corrupted manifests and hash mismatches.
    /// </summary>
    [Fact]
    public async Task ImportDataset_ShouldHandleCorruptedManifestAndMismatch()
    {
        // 1. Corrupted Manifest JSON
        using (MultipartFormDataContent form1 = new())
        {
            form1.Add(new ByteArrayContent("TEST_ARCHIVE"u8.ToArray()), "archive", "test.tar.gz");
            form1.Add(new ByteArrayContent("NOT_VALID_JSON"u8.ToArray()), "manifest", "manifest.json");

            HttpResponseMessage resp1 = await _client.PostAsync("/api/datasets/import", form1);
            Assert.Equal(HttpStatusCode.BadRequest, resp1.StatusCode);
        }

        // 2. Hash Mismatch Manifest
        using (MultipartFormDataContent form2 = new())
        {
            DatasetManifest mismatchManifest = new()
            {
                DatasetName = "MismatchTest",
                ArchiveFileName = "test.tar.gz",
                Sha256Checksum = "0000000000000000000000000000000000000000000000000000000000000000",
                TimestampUtc = DateTime.UtcNow.ToString("o"),
                SizeInBytes = 12L,
                IncludedFiles = ["test.xyz"]
            };
            string json = JsonSerializer.Serialize(mismatchManifest);

            form2.Add(new ByteArrayContent("TEST_ARCHIVE"u8.ToArray()), "archive", "test.tar.gz");
            form2.Add(new StringContent(json, Encoding.UTF8, "application/json"), "manifest", "manifest.json");

            HttpResponseMessage resp2 = await _client.PostAsync("/api/datasets/import", form2);
            Assert.Equal(HttpStatusCode.BadRequest, resp2.StatusCode);
        }
    }

    /// <summary>
    /// Verifies configuration and share key validation endpoints.
    /// </summary>
    [Fact]
    public async Task ConfigAndShareKey_ValidationEndpoints()
    {
        // 1. POST /api/configuration/override with empty key -> 400 Bad Request
        ConfigOverrideRequest emptyKeyReq = new() { Key = "", Value = "test" };
        HttpResponseMessage badConfigResp = await _client.PostAsJsonAsync("/api/configuration/override", emptyKeyReq);
        Assert.Equal(HttpStatusCode.BadRequest, badConfigResp.StatusCode);

        // 2. POST /api/share-keys/redeem with invalid token -> 400 Bad Request
        RedeemShareKeyRequest invalidTokReq = new() { Token = "nanoweaver_hrt_nonexistent_token_12345" };
        HttpResponseMessage badRedeemResp = await _client.PostAsJsonAsync("/api/share-keys/redeem", invalidTokReq);
        Assert.Equal(HttpStatusCode.BadRequest, badRedeemResp.StatusCode);

        // 3. GET /api/share-keys -> 200 OK
        HttpResponseMessage listResp = await _client.GetAsync("/api/share-keys");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
    }

    /// <summary>
    /// Verifies service ping diagnostic endpoints.
    /// </summary>
    [Fact]
    public async Task ServiceEndpoints_ShouldReturnStatusAndHandlePing()
    {
        // 1. GET /api/services -> 200 OK
        HttpResponseMessage svcsResp = await _client.GetAsync("/api/services");
        Assert.Equal(HttpStatusCode.OK, svcsResp.StatusCode);

        // 2. POST /api/services/ping/{serviceId}
        string[] services = ["simulation-engine", "pg-duckdb", "trajectory-parser", "peer-mesh", "unrecognized"];
        foreach (string id in services)
        {
            HttpResponseMessage pingResp = await _client.PostAsync($"/api/services/ping/{id}", null);
            Assert.Equal(HttpStatusCode.OK, pingResp.StatusCode);
        }
    }

    /// <summary>
    /// Verifies <see cref="Program.ParseExpirationWindow"/> covers all duration strings and fallbacks.
    /// </summary>
    [Fact]
    public void Program_ParseExpirationWindow_AllBranches()
    {
        Assert.Equal(TimeSpan.FromHours(24), Program.ParseExpirationWindow(null));
        Assert.Equal(TimeSpan.FromHours(10), Program.ParseExpirationWindow(null, 10));
        Assert.Equal(TimeSpan.FromHours(24), Program.ParseExpirationWindow("", -5));
        Assert.Equal(TimeSpan.FromHours(1), Program.ParseExpirationWindow("1h"));
        Assert.Equal(TimeSpan.FromHours(4), Program.ParseExpirationWindow("4h"));
        Assert.Equal(TimeSpan.FromHours(24), Program.ParseExpirationWindow("24h"));
        Assert.Equal(TimeSpan.FromDays(7), Program.ParseExpirationWindow("7d"));
        Assert.Equal(TimeSpan.FromDays(30), Program.ParseExpirationWindow("1m"));
        Assert.Equal(TimeSpan.FromDays(30), Program.ParseExpirationWindow("30d"));
        Assert.Equal(TimeSpan.FromDays(365), Program.ParseExpirationWindow("1y"));
        Assert.Equal(TimeSpan.FromDays(365), Program.ParseExpirationWindow("365d"));
        Assert.Equal(TimeSpan.FromDays(36525), Program.ParseExpirationWindow("never"));
        Assert.Equal(TimeSpan.FromDays(36525), Program.ParseExpirationWindow("infinite"));
        Assert.Equal(TimeSpan.FromHours(48), Program.ParseExpirationWindow("48"));
        Assert.Equal(TimeSpan.FromHours(12), Program.ParseExpirationWindow("invalid", 12));
        Assert.Equal(TimeSpan.FromHours(24), Program.ParseExpirationWindow("invalid", -1));
    }

    /// <summary>
    /// Verifies that GET /api/genomics/markers returns HTTP 200 OK with the curated genetic catalog.
    /// </summary>
    [Fact]
    public async Task GetGenomicsMarkers_ShouldReturnOkWithCatalog()
    {
        // WHAT: Invokes GET /api/genomics/markers endpoint.
        // WHY:  Ensures the frontend can fetch the active marker catalog for patient profiling.
        // HOW:  Asserts 200 OK and non-empty JSON array of genetic markers.
        HttpResponseMessage response = await _client.GetAsync("/api/genomics/markers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        List<GeneticMarker>? markers = await response.Content.ReadFromJsonAsync<List<GeneticMarker>>();
        Assert.NotNull(markers);
        Assert.Equal(8, markers.Count);
    }

    /// <summary>
    /// Verifies that GET /api/genomics/presets returns HTTP 200 OK with clinical test panels.
    /// </summary>
    [Fact]
    public async Task GetGenomicsPresets_ShouldReturnOkWithPresets()
    {
        // WHAT: Invokes GET /api/genomics/presets endpoint.
        // WHY:  Ensures the frontend can load clinical demonstration test panels.
        // HOW:  Asserts 200 OK and list containing 4 presets.
        HttpResponseMessage response = await _client.GetAsync("/api/genomics/presets");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        List<GeneticMarkerPreset>? presets = await response.Content.ReadFromJsonAsync<List<GeneticMarkerPreset>>();
        Assert.NotNull(presets);
        Assert.Equal(4, presets.Count);
    }

    /// <summary>
    /// Verifies that POST /api/genomics/analyze evaluates patient markers and returns alerts.
    /// </summary>
    [Fact]
    public async Task PostGenomicsAnalyze_ShouldReturnOkWithProfile()
    {
        // WHAT: Invokes POST /api/genomics/analyze with specific markers and with null fallbacks.
        // WHY:  Validates minimal API request binding and response serialization.
        // HOW:  Asserts 200 OK and evaluated patient genomic profile.
        GenomicAnalysisRequest req = new()
        {
            PatientId = "TEST-API-001",
            MarkerIds = ["F5_LEIDEN"],
            Regimen = TherapeuticRegimen.MaleToFemale
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/genomics/analyze", req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PatientGenomicProfile? profile = await response.Content.ReadFromJsonAsync<PatientGenomicProfile>();
        Assert.NotNull(profile);
        Assert.Equal("TEST-API-001", profile.PatientId);
        Assert.Single(profile.ObservedMarkers);

        // Also test null fallback branch
        GenomicAnalysisRequest fallbackReq = new()
        {
            PatientId = null,
            MarkerIds = null,
            Regimen = TherapeuticRegimen.MaleToFemale
        };
        HttpResponseMessage fallbackResp = await _client.PostAsJsonAsync("/api/genomics/analyze", fallbackReq);
        Assert.Equal(HttpStatusCode.OK, fallbackResp.StatusCode);
        PatientGenomicProfile? fallbackProfile = await fallbackResp.Content.ReadFromJsonAsync<PatientGenomicProfile>();
        Assert.NotNull(fallbackProfile);
        Assert.Equal("ANON-PATIENT-001", fallbackProfile.PatientId);
    }

    /// <summary>
    /// Verifies that POST /api/genomics/analyze-sequence scans raw DNA sequence for immunogenicity and repeat motifs.
    /// </summary>
    [Fact]
    public async Task PostGenomicsAnalyzeSequence_ShouldReturnOkWithSequenceProfile()
    {
        // WHAT: Invokes POST /api/genomics/analyze-sequence with nucleotide string.
        // WHY:  Ensures sequence-level screening operates properly over HTTP.
        // HOW:  Sends high CpG sequence and asserts TLR9 detection.
        SequenceAnalysisRequest req = new()
        {
            PatientId = "TEST-SEQ-API",
            Sequence = "CGCGCGCGCGCGCGCGCGCG",
            Regimen = TherapeuticRegimen.MaleToFemale
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/genomics/analyze-sequence", req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PatientGenomicProfile? profile = await response.Content.ReadFromJsonAsync<PatientGenomicProfile>();
        Assert.NotNull(profile);
        Assert.True(profile.CpGMethylationMandatory);

        // Test null fallback
        SequenceAnalysisRequest fallbackReq = new()
        {
            PatientId = null,
            Sequence = null,
            Regimen = TherapeuticRegimen.MaleToFemale
        };
        HttpResponseMessage fallbackResp = await _client.PostAsJsonAsync("/api/genomics/analyze-sequence", fallbackReq);
        Assert.Equal(HttpStatusCode.OK, fallbackResp.StatusCode);
    }

    /// <summary>
    /// Verifies that POST /api/genomics/apply-adjustments updates active endocrine profile setpoints.
    /// </summary>
    [Fact]
    public async Task PostGenomicsApplyAdjustments_ShouldReturnOkWithAdjustedProfile()
    {
        // WHAT: Invokes POST /api/genomics/apply-adjustments.
        // WHY:  Validates that patient genetic limits are applied to the active runtime state engine.
        // HOW:  Asserts 200 OK and response containing adjusted endocrine profile.
        GenomicAnalysisRequest req = new()
        {
            PatientId = "TEST-ADJUST-API",
            MarkerIds = ["F5_LEIDEN"],
            Regimen = TherapeuticRegimen.MaleToFemale
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/genomics/apply-adjustments", req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Test fallback defaults with null fields
        GenomicAnalysisRequest fallbackReq = new()
        {
            PatientId = null,
            MarkerIds = null,
            Regimen = TherapeuticRegimen.FemaleToMale
        };
        HttpResponseMessage fallbackResp = await _client.PostAsJsonAsync("/api/genomics/apply-adjustments", fallbackReq);
        Assert.Equal(HttpStatusCode.OK, fallbackResp.StatusCode);
    }

    /// <summary>
    /// Verifies model record instantiation and with-expressions for request payloads.
    /// </summary>
    [Fact]
    public void GenomicRequestModels_RecordPropertiesAndWith()
    {
        // WHAT: Tests GenomicAnalysisRequest and SequenceAnalysisRequest records.
        // WHY:  Guarantees 100% line and branch coverage on request records.
        // HOW:  Initializes records and exercises with-mutations.
        GenomicAnalysisRequest req = new()
        {
            PatientId = "P1",
            MarkerIds = ["F5_LEIDEN"],
            Regimen = TherapeuticRegimen.MaleToFemale
        };
        GenomicAnalysisRequest mutated = req with { PatientId = "P2" };
        Assert.Equal("P2", mutated.PatientId);
        Assert.Single(mutated.MarkerIds!);

        SequenceAnalysisRequest seqReq = new()
        {
            PatientId = "S1",
            Sequence = "ATCG",
            Regimen = TherapeuticRegimen.FemaleToMale
        };
        SequenceAnalysisRequest seqMutated = seqReq with { Sequence = "GGCC" };
        Assert.Equal("GGCC", seqMutated.Sequence);
        Assert.Equal(TherapeuticRegimen.FemaleToMale, seqMutated.Regimen);
    }

    /// <summary>
    /// Verifies that the Swagger JSON specification and interactive UI pages return HTTP 200 OK with expected content.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task SwaggerAndReDoc_Endpoints_ShouldReturnOkAndValidContent()
    {
        // WHAT: Tests GET /swagger/v1/swagger.json, /swagger/index.html, and /documentation/index.html.
        // WHY:  Ensures the OpenAPI specification, Swagger UI, and ReDoc documentation are successfully hosted.
        // HOW:  Issues HTTP GET requests to each endpoint and asserts 200 OK and matching markup/JSON signatures.

        // Path 1: Swagger JSON specification
        HttpResponseMessage specResponse = await _client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, specResponse.StatusCode);
        string specJson = await specResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"openapi\": \"3.0", specJson);
        Assert.Contains("/api/datasets", specJson);
        Assert.Contains("EncryptedEnvelope", specJson);

        // Path 2: Swagger UI HTML page
        HttpResponseMessage swaggerUiResponse = await _client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, swaggerUiResponse.StatusCode);
        string swaggerUiHtml = await swaggerUiResponse.Content.ReadAsStringAsync();
        Assert.Contains("swagger-ui", swaggerUiHtml);

        // Path 3: ReDoc HTML documentation page
        HttpResponseMessage redocResponse = await _client.GetAsync("/documentation/index.html");
        Assert.Equal(HttpStatusCode.OK, redocResponse.StatusCode);
        string redocHtml = await redocResponse.Content.ReadAsStringAsync();
        Assert.Contains("redoc", redocHtml);
    }

    /// <summary>
    /// Verifies that /swagger and /documentation redirect to their respective index.html files.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task SwaggerAndReDoc_Redirects_ShouldRedirectToIndex()
    {
        // WHAT: Tests redirect endpoints GET /swagger and GET /documentation without following redirects.
        // WHY:  Guarantees that users navigating to clean URLs receive 302 redirects to index.html.
        // HOW:  Creates an HttpClient with AllowAutoRedirect disabled and validates status code and Location header.
        HttpClient redirectClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Path 1: /swagger -> swagger/index.html
        HttpResponseMessage swaggerRedirect = await redirectClient.GetAsync("/swagger");
        Assert.True(swaggerRedirect.StatusCode == HttpStatusCode.MovedPermanently || swaggerRedirect.StatusCode == HttpStatusCode.Redirect);
        Assert.EndsWith("swagger/index.html", swaggerRedirect.Headers.Location?.OriginalString);

        // Path 2: /documentation -> documentation/index.html
        HttpResponseMessage docRedirect = await redirectClient.GetAsync("/documentation");
        Assert.True(docRedirect.StatusCode == HttpStatusCode.MovedPermanently || docRedirect.StatusCode == HttpStatusCode.Redirect);
        Assert.EndsWith("documentation/index.html", docRedirect.Headers.Location?.OriginalString);
    }

    /// <summary>
    /// Verifies that GET /api/system/telemetry returns valid lower camelCase JSON keys.
    /// </summary>
    [Fact]
    public async Task GetSystemTelemetry_ShouldReturnCamelCaseJson()
    {
        // WHAT: Tests raw JSON response from GET /api/system/telemetry.
        // WHY:  Enforces lower camelCase JSON standard requested by user.
        // HOW:  Issues GET request, reads raw JSON text, and asserts presence of camelCase keys.
        HttpResponseMessage response = await _client.GetAsync("/api/system/telemetry");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"nodeName\":", json);
        Assert.Contains("\"acceleratorBackend\":", json);
        Assert.Contains("\"framesParsed\":", json);
        Assert.DoesNotContain("\"node_name\":", json);
        Assert.DoesNotContain("\"frames_parsed\":", json);
    }

    /// <summary>
    /// Verifies that GET /api/system/telemetry returns XML when requested via Accept header.
    /// </summary>
    [Fact]
    public async Task GetSystemTelemetry_WithXmlAccept_ShouldReturnXml()
    {
        // WHAT: Tests content negotiation for application/xml on telemetry endpoint.
        // WHY:  Validates XML transcoding in FormatNegotiationMiddleware.
        // HOW:  Issues GET with Accept: application/xml header and asserts XML payload.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/system/telemetry");
        request.Headers.Accept.ParseAdd("application/xml");

        HttpResponseMessage response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml; charset=utf-8", response.Content.Headers.ContentType?.ToString());

        string xml = await response.Content.ReadAsStringAsync();
        Assert.Contains("<nodeName>", xml);
        Assert.Contains("</nodeName>", xml);
    }

    /// <summary>
    /// Verifies that GET /api/system/telemetry returns YAML with lower snake-case with hyphens when requested via Accept header.
    /// </summary>
    [Fact]
    public async Task GetSystemTelemetry_WithYamlAccept_ShouldReturnYamlWithHyphens()
    {
        // WHAT: Tests content negotiation for application/x-yaml on telemetry endpoint.
        // WHY:  Enforces YAML kebab-case with hyphens standard.
        // HOW:  Issues GET with Accept: application/x-yaml header and asserts hyphenated keys.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/system/telemetry");
        request.Headers.Accept.ParseAdd("application/x-yaml");

        HttpResponseMessage response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-yaml; charset=utf-8", response.Content.Headers.ContentType?.ToString());

        string yaml = await response.Content.ReadAsStringAsync();
        Assert.Contains("node-name:", yaml);
        Assert.Contains("accelerator-backend:", yaml);
        Assert.Contains("frames-parsed:", yaml);
        Assert.DoesNotContain("node_name:", yaml);
    }

    /// <summary>
    /// Verifies that ?format= query parameters override the response format.
    /// </summary>
    [Fact]
    public async Task GetSystemTelemetry_WithFormatQuery_ShouldReturnRequestedFormat()
    {
        // WHAT: Tests format override via query string (?format=xml and ?format=yaml).
        // WHY:  Allows browser navigation or curl queries without custom Accept headers.
        // HOW:  Requests telemetry with ?format=xml and ?format=yaml, asserts corresponding Content-Type and payload.
        HttpResponseMessage xmlResp = await _client.GetAsync("/api/system/telemetry?format=xml");
        Assert.Equal(HttpStatusCode.OK, xmlResp.StatusCode);
        Assert.Contains("application/xml", xmlResp.Content.Headers.ContentType?.ToString());

        HttpResponseMessage yamlResp = await _client.GetAsync("/api/system/telemetry?format=yaml");
        Assert.Equal(HttpStatusCode.OK, yamlResp.StatusCode);
        Assert.Contains("application/x-yaml", yamlResp.Content.Headers.ContentType?.ToString());
    }

    /// <summary>
    /// Verifies that POST /api/datasets/export accepts XML payloads and transcodes them seamlessly.
    /// </summary>
    [Fact]
    public async Task ExportDataset_WithXmlContentType_ShouldSucceed()
    {
        // WHAT: Posts an XML payload to /api/datasets/export.
        // WHY:  Validates inbound XML request transcoding in FormatNegotiationMiddleware.
        // HOW:  Constructs StringContent with application/xml, sends POST, verifies 200 OK and manifest.
        string xmlPayload = "<ExportRequest><datasetName>XmlTranscodeTest</datasetName><files><item>dna_nanotube_sample.xyz</item></files></ExportRequest>";
        using var content = new StringContent(xmlPayload, Encoding.UTF8, "application/xml");

        HttpResponseMessage response = await _client.PostAsync("/api/datasets/export", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        DatasetManifest? manifest = await response.Content.ReadFromJsonAsync<DatasetManifest>();
        Assert.NotNull(manifest);
        Assert.Equal("XmlTranscodeTest", manifest.DatasetName);
    }

    /// <summary>
    /// Verifies that POST /api/datasets/export accepts YAML payloads with hyphenated keys and transcodes them seamlessly.
    /// </summary>
    [Fact]
    public async Task ExportDataset_WithYamlContentType_ShouldSucceed()
    {
        // WHAT: Posts a YAML payload to /api/datasets/export.
        // WHY:  Validates inbound YAML request transcoding with kebab-case in FormatNegotiationMiddleware.
        // HOW:  Constructs StringContent with application/x-yaml, sends POST, verifies 200 OK and manifest.
        string yamlPayload = """
        dataset-name: YamlTranscodeTest
        files:
          - dna_nanotube_sample.xyz
        """;
        using var content = new StringContent(yamlPayload, Encoding.UTF8, "application/x-yaml");

        HttpResponseMessage response = await _client.PostAsync("/api/datasets/export", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        DatasetManifest? manifest = await response.Content.ReadFromJsonAsync<DatasetManifest>();
        Assert.NotNull(manifest);
        Assert.Equal("YamlTranscodeTest", manifest.DatasetName);
    }

    /// <summary>
    /// Verifies that /swagger/v1/swagger.json contains multi-format media types and examples for JSON, XML, and YAML.
    /// </summary>
    [Fact]
    public async Task SwaggerSpec_ShouldContainMultiFormatExamples()
    {
        // WHAT: Verifies OpenAPI 3.0 schema generation contains JSON, XML, and YAML content types and examples.
        // WHY:  Ensures Swagger and ReDoc documentation interfaces display multi-format payloads.
        // HOW:  Fetches /swagger/v1/swagger.json and inspects contents for media types and examples.
        HttpResponseMessage response = await _client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string specJson = await response.Content.ReadAsStringAsync();
        Assert.Contains("application/json", specJson);
        Assert.Contains("application/xml", specJson);
        Assert.Contains("application/x-yaml", specJson);
        Assert.Contains("example", specJson);
    }
}
