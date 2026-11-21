using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NanoWeaverHrt.Analytics;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Fabrication.Gel;
using NanoWeaverHrt.Genomics;
using NanoWeaverHrt.Hardware.Benchmarking;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Access;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Integration test suite verifying HTTP endpoints exposed by the minimal API service in <see cref="Program"/>.
/// </summary>
public sealed class EndpointIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    /// <summary>
    /// Root temp directory created by this instance's constructor; deleted in <see cref="Dispose"/>.
    /// </summary>
    private readonly string _testRoot;

    /// <summary>
    /// Deletes the temporary directories this test instance created.
    /// </summary>
    /// <remarks>
    /// xUnit builds one instance per test method, so without this every test would leave a directory behind.
    /// <see cref="IOException"/> and <see cref="UnauthorizedAccessException"/> are swallowed because a file
    /// still held open by a disposing host is realistic and cleanup must never fail a test. The shared
    /// <c>WebApplicationFactory</c> is owned by xUnit's class fixture and is deliberately not disposed here.
    /// </remarks>
    public void Dispose()
    {
        // WHAT: Removes this instance's temporary directories.
        // WHY:  Prevents leaking weaver_* directories under the OS temp path on every test.
        // HOW:  Guards with Directory.Exists, deletes recursively, and ignores IO/permission failures.
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup: a file still held open must not fail the test run.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup: a locked or read-only entry must not fail the test run.
        }
    }

    /// <summary>
    /// Test server web application factory.
    /// </summary>
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>
    /// Reusable HTTP client connected to in-memory test server.
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// Absolute path to this instance's isolated export directory, which is also the directory bound to
    /// the test host's TRAJECTORY_EXPORT_DIR setting.
    /// </summary>
    /// <remarks>
    /// Tests that stage an export file on disk and then read it back through an HTTP endpoint must write
    /// into this exact directory, otherwise the host would be looking somewhere else entirely.
    /// </remarks>
    private readonly string _exportDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="EndpointIntegrationTests"/> class.
    /// </summary>
    /// <param name="factory">The test web application factory fixture.</param>
    public EndpointIntegrationTests(WebApplicationFactory<Program> factory)
    {
        // WHAT: Allocates a private, writable data root for this test class instance.
        // WHY:  SystemConfigurationStore places weaver_config.db inside TRAJECTORY_INCOMING_DIR, so binding
        //       that to the live simulations/shared-data made this class read the operator's real user
        //       database and depend on an Administrator account it did not create.
        // HOW:   Derives a GUID-suffixed directory under the OS temp path and seeds trajectory fixtures there.
        string testRoot = Path.Combine(Path.GetTempPath(), "weaver_endpoint_" + Guid.NewGuid().ToString("N"));
        _testRoot = testRoot;
        string dataDir = Path.Combine(testRoot, "shared-data");
        string exportDir = Path.Combine(testRoot, "exports");
        string parquetDir = Path.Combine(testRoot, "parquet");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(exportDir);
        Directory.CreateDirectory(parquetDir);
        _exportDir = exportDir;

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
        string exportDir = _exportDir;

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
        string exportDir = _exportDir;
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

    /// <summary>
    /// Verifies that POST /api/trajectories/analytics/query returns HTTP 200 OK with tabular data for a valid sample dataset query.
    /// </summary>
    [Fact]
    public async Task QueryTrajectoryAnalytics_WithValidSampleDataset_ReturnsOkWithData()
    {
        // WHAT: Posts an analytical query against the sample dataset.
        // WHY:  Validates end-to-end execution of the columnar SQL minimal API endpoint.
        // HOW:  Sends a POST request with TrajectoryAnalyticsApiRequest, validates 200 OK, columns, and rows.
        TrajectoryAnalyticsApiRequest req = new(
            DatasetName: "sample",
            Query: "SELECT target_region, COUNT(*) AS count_val FROM trajectory GROUP BY target_region ORDER BY count_val DESC;",
            MaxRows: 50);

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/trajectories/analytics/query", req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TrajectoryAnalyticsApiResponse? result = await response.Content.ReadFromJsonAsync<TrajectoryAnalyticsApiResponse>();
        Assert.NotNull(result);
        Assert.Equal("sample", result.DatasetName);
        Assert.Equal(2, result.Columns.Count);
        Assert.Equal("target_region", result.Columns[0]);
        Assert.Equal("count_val", result.Columns[1]);
        Assert.True(result.TotalRows > 0);
        Assert.True(result.ElapsedMilliseconds >= 0.0);
    }

    /// <summary>
    /// Verifies that POST /api/trajectories/analytics/query returns HTTP 200 OK for an ephemeral in-memory calculation.
    /// </summary>
    [Fact]
    public async Task QueryTrajectoryAnalytics_WithMemorySession_ReturnsOk()
    {
        // WHAT: Posts a scalar mathematical calculation against the ephemeral in-memory session.
        // WHY:  Ensures that :memory: queries succeed without disk file dependencies.
        // HOW:  Sends a POST request specifying :memory: and an arithmetic query, asserting the computed value.
        TrajectoryAnalyticsApiRequest req = new(
            DatasetName: ":memory:",
            Query: "SELECT 21 * 2 AS answer;",
            MaxRows: null);

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/trajectories/analytics/query", req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TrajectoryAnalyticsApiResponse? result = await response.Content.ReadFromJsonAsync<TrajectoryAnalyticsApiResponse>();
        Assert.NotNull(result);
        Assert.Single(result.Columns);
        Assert.Equal("answer", result.Columns[0]);
        Assert.Single(result.Rows);
        JsonElement elem = (JsonElement)result.Rows[0][0]!;
        Assert.Equal(42, elem.GetInt32());
    }

    /// <summary>
    /// Verifies that POST /api/trajectories/analytics/query returns HTTP 400 Bad Request when the query is empty.
    /// </summary>
    [Fact]
    public async Task QueryTrajectoryAnalytics_WithEmptyQuery_ReturnsBadRequest()
    {
        // WHAT: Sends a query request with whitespace-only SQL statement.
        // WHY:  Enforces input validation guarding against blank or missing analytical queries.
        // HOW:  Sends a POST request with whitespace query and asserts 400 Bad Request.
        TrajectoryAnalyticsApiRequest req = new(
            DatasetName: "sample",
            Query: "   ",
            MaxRows: 100);

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/trajectories/analytics/query", req);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Verifies that POST /api/trajectories/analytics/query returns HTTP 400 Bad Request when the dataset name is empty.
    /// </summary>
    [Fact]
    public async Task QueryTrajectoryAnalytics_WithEmptyDatasetName_ReturnsBadRequest()
    {
        // WHAT: Sends a query request with whitespace-only dataset name.
        // WHY:  Enforces input validation requiring an identifiable target dataset.
        // HOW:  Sends a POST request with empty dataset name and asserts 400 Bad Request.
        TrajectoryAnalyticsApiRequest req = new(
            DatasetName: "",
            Query: "SELECT 1;",
            MaxRows: 100);

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/trajectories/analytics/query", req);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Verifies that POST /api/trajectories/analytics/query returns HTTP 400 Bad Request when mutation SQL is submitted.
    /// </summary>
    [Fact]
    public async Task QueryTrajectoryAnalytics_WithProhibitedMutationQuery_ReturnsBadRequest()
    {
        // WHAT: Submits a query attempting a DROP TABLE mutation command.
        // WHY:  Validates that read-only query guard rejections are mapped to 400 Bad Request Problem Details.
        // HOW:  Sends a POST request with DROP TABLE statement and asserts 400 Bad Request.
        TrajectoryAnalyticsApiRequest req = new(
            DatasetName: "sample",
            Query: "DROP TABLE trajectory;",
            MaxRows: 100);

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/trajectories/analytics/query", req);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Verifies that POST /api/trajectories/analytics/query returns HTTP 404 Not Found when the requested dataset does not exist.
    /// </summary>
    [Fact]
    public async Task QueryTrajectoryAnalytics_WithMissingDataset_ReturnsNotFound()
    {
        // WHAT: Queries a non-existent dataset name on disk.
        // WHY:  Validates that FileNotFoundException is caught and mapped to 404 Not Found Problem Details.
        // HOW:  Sends a POST request specifying an unresolvable parquet filename and asserts 404 Not Found.
        TrajectoryAnalyticsApiRequest req = new(
            DatasetName: "nonexistent_dataset_test_404",
            Query: "SELECT * FROM trajectory;",
            MaxRows: 100);

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/trajectories/analytics/query", req);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Verifies that POST /api/trajectories/analytics/query handles row limiting and non-positive MaxRows gracefully.
    /// </summary>
    [Fact]
    public async Task QueryTrajectoryAnalytics_WithRowLimiting_EnforcesRequestedLimit()
    {
        // WHAT: Posts queries with specific row count bounds and non-positive row counts.
        // WHY:  Validates row limit clamping logic inside the analytical endpoint.
        // HOW:  Sends POST requests with MaxRows = 2 and MaxRows = -1, asserting proper row returns.
        TrajectoryAnalyticsApiRequest reqLimited = new(
            DatasetName: "sample",
            Query: "SELECT * FROM trajectory;",
            MaxRows: 2);

        HttpResponseMessage responseLimited = await _client.PostAsJsonAsync("/api/trajectories/analytics/query", reqLimited);
        Assert.Equal(HttpStatusCode.OK, responseLimited.StatusCode);
        TrajectoryAnalyticsApiResponse? resultLimited = await responseLimited.Content.ReadFromJsonAsync<TrajectoryAnalyticsApiResponse>();
        Assert.NotNull(resultLimited);
        Assert.Equal(2, resultLimited.TotalRows);

        TrajectoryAnalyticsApiRequest reqZero = new(
            DatasetName: "sample",
            Query: "SELECT * FROM trajectory;",
            MaxRows: -1);

        HttpResponseMessage responseZero = await _client.PostAsJsonAsync("/api/trajectories/analytics/query", reqZero);
        Assert.Equal(HttpStatusCode.OK, responseZero.StatusCode);
        TrajectoryAnalyticsApiResponse? resultZero = await responseZero.Content.ReadFromJsonAsync<TrajectoryAnalyticsApiResponse>();
        Assert.NotNull(resultZero);
        Assert.True(resultZero.TotalRows > 2);
    }

    /// <summary>
    /// Provisions a test user with the specified role and generates a signed bearer token.
    /// </summary>
    /// <param name="username">Unique username for the test user.</param>
    /// <param name="role">Role to assign to the user.</param>
    /// <returns>A signed Bearer token granting the specified role permissions.</returns>
    private async Task<string> ProvisionUserAndGetTokenAsync(string username, UserRole role)
    {
        // WHAT: Seeds a test user account in the test database and mints an authentication token.
        // WHY:  Enables testing role gating against specific permission levels (Patient, Researcher, Administrator).
        // HOW:  Uses IUserAuthenticationService to complete initial setup if needed, creates user, and returns bearer token.
        using IServiceScope scope = _factory.Services.CreateScope();
        IUserAuthenticationService auth = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
        await TestFixturesHelper.EnsureTestAdminAndGetTokenAsync(_factory);
        IReadOnlyList<UserAccount> users = await auth.ListUsersAsync();
        UserAccount? existing = users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return auth.GenerateBearerToken(existing);
        }

        GeneratedKeyBundle bundle = TestChallengeHelper.CreateTestKeyBundle(username, role, AccessGate.ApiWeb, KeyAlgorithm.Ed25519);
        UserAccount created = await auth.CreateUserAsync(username, role, AccessGate.ApiWeb, bundle.PublicKeyBase64, KeyAlgorithm.Ed25519);
        return auth.GenerateBearerToken(created);
    }

    /// <summary>
    /// Creates an HTTP client configured with an optional bearer token header.
    /// </summary>
    /// <param name="token">Optional Bearer token string to attach.</param>
    /// <returns>A configured <see cref="HttpClient"/>.</returns>
    private HttpClient CreateClientWithBearer(string? token)
    {
        // WHAT: Instantiates an HttpClient with the provided bearer token or none.
        // WHY:  Allows discrete authentication state isolation for positive and negative tests.
        // HOW:  Creates client from factory and sets Authorization header if token is provided.
        HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        if (!string.IsNullOrEmpty(token))
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    /// <summary>
    /// Verifies that POST /api/system/benchmark/run executes a hardware qualification benchmark and returns 200 OK with report.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAcceleratorBenchmark_Endpoint_ExecutesAndReturnsReport()
    {
        // WHAT: Posts to /api/system/benchmark/run with Administrator authentication.
        // WHY:  Validates that operators and researchers can execute hardware qualification benchmarks via REST API.
        // HOW:  Sends POST request, asserts 200 OK, and validates the populated AcceleratorBenchmarkReport payload.
        using HttpResponseMessage response = await _client.PostAsync("/api/system/benchmark/run", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        AcceleratorBenchmarkReport? report = await response.Content.ReadFromJsonAsync<AcceleratorBenchmarkReport>();
        Assert.NotNull(report);
        Assert.False(string.IsNullOrWhiteSpace(report.DeviceName));
        Assert.False(string.IsNullOrWhiteSpace(report.DriverVersion));
        Assert.True(report.SinglePrecisionGflops >= 0.0);
        Assert.True(report.DoublePrecisionGflops >= 0.0);
        Assert.True(report.MemoryBandwidthGigaBytesPerSecond >= 0.0);
        Assert.True(Enum.IsDefined(report.Readiness));
        Assert.NotNull(report.Recommendations);
    }

    /// <summary>
    /// Verifies that GET /api/system/benchmark/latest returns 404 when no benchmark is cached, and 200 OK once executed.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetLatestAcceleratorBenchmark_Endpoint_ReturnsReportOrNotFound()
    {
        // WHAT: Queries /api/system/benchmark/latest across both uninitialized and populated states.
        // WHY:  Ensures the endpoint returns RFC 7807 404 Problem Details when no report exists, and 200 OK with report after execution.
        // HOW:  Creates an isolated host to assert 404 on clean state, then triggers run and asserts 200 OK on retrieval.
        string isolatedRoot = Path.Combine(Path.GetTempPath(), "weaver_bench_iso_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(isolatedRoot);
            TestFixturesHelper.EnsureTestFixtures(isolatedRoot);

            using WebApplicationFactory<Program> isolatedFactory = _factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("TRAJECTORY_INCOMING_DIR", isolatedRoot);
                builder.UseSetting("TRAJECTORY_EXPORT_DIR", Path.Combine(isolatedRoot, "exports"));
                builder.UseSetting("TRAJECTORY_PARQUET_DIR", Path.Combine(isolatedRoot, "parquet"));
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IAcceleratorBenchmarkService, AcceleratorBenchmarkService>();
                });
            });

            string token = await TestFixturesHelper.EnsureTestAdminAndGetTokenAsync(isolatedFactory);
            using HttpClient isolatedClient = isolatedFactory.CreateClient();
            isolatedClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            // Phase 1: Clean state -> 404 Not Found
            using HttpResponseMessage notFoundResponse = await isolatedClient.GetAsync("/api/system/benchmark/latest");
            Assert.Equal(HttpStatusCode.NotFound, notFoundResponse.StatusCode);
            Assert.Equal("application/problem+json", notFoundResponse.Content.Headers.ContentType?.MediaType);
            string notFoundBody = await notFoundResponse.Content.ReadAsStringAsync();
            Assert.Contains("No accelerator benchmark report is currently available.", notFoundBody);

            // Phase 2: Run benchmark -> 200 OK
            using HttpResponseMessage runResponse = await isolatedClient.PostAsync("/api/system/benchmark/run", null);
            Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);

            // Phase 3: Populated state -> 200 OK
            using HttpResponseMessage okResponse = await isolatedClient.GetAsync("/api/system/benchmark/latest");
            Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);
            AcceleratorBenchmarkReport? report = await okResponse.Content.ReadFromJsonAsync<AcceleratorBenchmarkReport>();
            Assert.NotNull(report);
            Assert.False(string.IsNullOrWhiteSpace(report.DeviceName));
        }
        finally
        {
            try
            {
                if (Directory.Exists(isolatedRoot))
                {
                    Directory.Delete(isolatedRoot, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    /// <summary>
    /// Verifies that unauthenticated requests to benchmark endpoints are rejected with 401 Unauthorized.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task AcceleratorBenchmark_UnauthenticatedRequest_ReturnsUnauthorized()
    {
        // WHAT: Posts and gets benchmark endpoints without Bearer token.
        // WHY:  Prime Directive 12 strictly requires PKI authentication for infrastructure endpoints.
        // HOW:  Uses anonymous client, asserts 401 Unauthorized on both POST and GET.
        using HttpClient anonymousClient = CreateClientWithBearer(null);

        using HttpResponseMessage runResp = await anonymousClient.PostAsync("/api/system/benchmark/run", null);
        Assert.Equal(HttpStatusCode.Unauthorized, runResp.StatusCode);

        using HttpResponseMessage latestResp = await anonymousClient.GetAsync("/api/system/benchmark/latest");
        Assert.Equal(HttpStatusCode.Unauthorized, latestResp.StatusCode);
    }

    /// <summary>
    /// Verifies that authenticated Patient accounts are rejected with 403 Forbidden from benchmark endpoints.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task AcceleratorBenchmark_PatientRole_ReturnsForbidden()
    {
        // WHAT: Sends benchmark requests using a Patient bearer token.
        // WHY:  Prime Directive 12 restricts infrastructure benchmarks to Administrator and Researcher roles.
        // HOW:  Provisions a Patient token, sends POST and GET requests, asserts 403 Forbidden.
        string patientToken = await ProvisionUserAndGetTokenAsync("bench_patient_user", UserRole.Patient);
        using HttpClient patientClient = CreateClientWithBearer(patientToken);

        using HttpResponseMessage runResp = await patientClient.PostAsync("/api/system/benchmark/run", null);
        Assert.Equal(HttpStatusCode.Forbidden, runResp.StatusCode);

        using HttpResponseMessage latestResp = await patientClient.GetAsync("/api/system/benchmark/latest");
        Assert.Equal(HttpStatusCode.Forbidden, latestResp.StatusCode);
    }

    /// <summary>
    /// Verifies that authenticated Researcher accounts are authorized to execute and read benchmark reports.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task AcceleratorBenchmark_ResearcherRole_IsAuthorized()
    {
        // WHAT: Posts to /api/system/benchmark/run with Researcher bearer token.
        // WHY:  Prime Directive 12 permits Researchers to execute qualification benchmarks to ensure simulation readiness.
        // HOW:  Provisions a Researcher token, sends POST and GET, asserting successful 200 OK responses.
        string researcherToken = await ProvisionUserAndGetTokenAsync("bench_researcher_user", UserRole.Researcher);
        using HttpClient researcherClient = CreateClientWithBearer(researcherToken);

        using HttpResponseMessage runResp = await researcherClient.PostAsync("/api/system/benchmark/run", null);
        Assert.Equal(HttpStatusCode.OK, runResp.StatusCode);

        using HttpResponseMessage latestResp = await researcherClient.GetAsync("/api/system/benchmark/latest");
        Assert.Equal(HttpStatusCode.OK, latestResp.StatusCode);
    }

    /// <summary>
    /// Verifies that exceptions thrown by the accelerator benchmark service are sanitized into 500 Internal Server Error Problem Details.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task AcceleratorBenchmark_FaultingService_ReturnsInternalServerError()
    {
        // WHAT: Injects a faulting IAcceleratorBenchmarkService and requests run and latest endpoints.
        // WHY:  Directive 14 mandates RFC 7807 SecureProblemDetails for internal server faults with zero information disclosure.
        // HOW:  Configures factory with mock throwing InvalidOperationException, asserts 500 status and problem+json content type.
        Mock<IAcceleratorBenchmarkService> faultingMock = new();
        faultingMock
            .Setup(b => b.RunBenchmarkAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Hardware accelerator unit fault: OpenCL compute context failed."));
        faultingMock
            .Setup(b => b.GetLatestReportAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Hardware accelerator registry query fault."));

        using WebApplicationFactory<Program> faultFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IAcceleratorBenchmarkService>(faultingMock.Object);
            });
        });

        string token = await TestFixturesHelper.EnsureTestAdminAndGetTokenAsync(faultFactory);
        using HttpClient faultClient = faultFactory.CreateClient();
        faultClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage runResp = await faultClient.PostAsync("/api/system/benchmark/run", null);
        Assert.Equal(HttpStatusCode.InternalServerError, runResp.StatusCode);
        Assert.Equal("application/problem+json", runResp.Content.Headers.ContentType?.MediaType);

        using HttpResponseMessage latestResp = await faultClient.GetAsync("/api/system/benchmark/latest");
        Assert.Equal(HttpStatusCode.InternalServerError, latestResp.StatusCode);
        Assert.Equal("application/problem+json", latestResp.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// Verifies POST /api/endocrine/closed-loop/simulate handles valid requests and rejects invalid input ranges with RFC 7807 problem details.
    /// </summary>
    [Fact]
    public async Task SimulateClosedLoopEndocrine_ShouldHandleValidAndInvalidRequests()
    {
        // WHAT: Tests the POST /api/endocrine/closed-loop/simulate endpoint.
        // WHY:  Verifies API contract, numerical simulation execution, and input boundary validation.
        // HOW:  Posts valid and invalid ClosedLoopSimulationRequest payloads and asserts status codes and response models.

        // 1. Valid MtF request returns 200 OK with trajectory points and suppression
        ClosedLoopSimulationRequest validReq = new()
        {
            InitialEstradiolPmolL = 120.0,
            InitialTestosteroneNmolL = 18.0,
            InitialLhIuL = 6.5,
            InitialFshIuL = 5.0,
            TargetEstradiolPmolL = 550.0,
            TargetTestosteroneNmolL = 1.2,
            TargetState = ToggleState.EstrogenicDominant,
            TotalHours = 48,
            StepHours = 2.0,
            ClearanceMultiplier = 1.0
        };

        HttpResponseMessage okResp = await _client.PostAsJsonAsync("/api/endocrine/closed-loop/simulate", validReq);
        Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);
        ClosedLoopSimulationResult? result = await okResp.Content.ReadFromJsonAsync<ClosedLoopSimulationResult>();
        Assert.NotNull(result);
        Assert.NotEmpty(result.Points);
        Assert.True(result.FinalLhIuL < 1.0);
        Assert.True(result.FinalFshIuL < 1.5);

        // 2. Null request body returns 400 Bad Request
        using StringContent nullContent = new("null", Encoding.UTF8, "application/json");
        HttpResponseMessage nullResp = await _client.PostAsync("/api/endocrine/closed-loop/simulate", nullContent);
        Assert.Equal(HttpStatusCode.BadRequest, nullResp.StatusCode);

        // 3. Invalid TotalHours (< 1 or > 720) returns 400 Bad Request
        HttpResponseMessage lowHoursResp = await _client.PostAsJsonAsync("/api/endocrine/closed-loop/simulate", new ClosedLoopSimulationRequest { TotalHours = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, lowHoursResp.StatusCode);
        HttpResponseMessage highHoursResp = await _client.PostAsJsonAsync("/api/endocrine/closed-loop/simulate", new ClosedLoopSimulationRequest { TotalHours = 721 });
        Assert.Equal(HttpStatusCode.BadRequest, highHoursResp.StatusCode);

        // 4. Invalid StepHours (< 0.1, > 24.0, or > TotalHours) returns 400 Bad Request
        HttpResponseMessage lowStepResp = await _client.PostAsJsonAsync("/api/endocrine/closed-loop/simulate", new ClosedLoopSimulationRequest { StepHours = 0.05 });
        Assert.Equal(HttpStatusCode.BadRequest, lowStepResp.StatusCode);
        HttpResponseMessage highStepResp = await _client.PostAsJsonAsync("/api/endocrine/closed-loop/simulate", new ClosedLoopSimulationRequest { StepHours = 25.0 });
        Assert.Equal(HttpStatusCode.BadRequest, highStepResp.StatusCode);
        HttpResponseMessage overStepResp = await _client.PostAsJsonAsync("/api/endocrine/closed-loop/simulate", new ClosedLoopSimulationRequest { TotalHours = 2, StepHours = 5.0 });
        Assert.Equal(HttpStatusCode.BadRequest, overStepResp.StatusCode);

        // 5. Invalid ClearanceMultiplier (< 0.1 or > 5.0) returns 400 Bad Request
        HttpResponseMessage lowClearanceResp = await _client.PostAsJsonAsync("/api/endocrine/closed-loop/simulate", new ClosedLoopSimulationRequest { ClearanceMultiplier = 0.05 });
        Assert.Equal(HttpStatusCode.BadRequest, lowClearanceResp.StatusCode);
        HttpResponseMessage highClearanceResp = await _client.PostAsJsonAsync("/api/endocrine/closed-loop/simulate", new ClosedLoopSimulationRequest { ClearanceMultiplier = 5.5 });
        Assert.Equal(HttpStatusCode.BadRequest, highClearanceResp.StatusCode);

        // 6. Negative hormone concentration returns 400 Bad Request
        HttpResponseMessage negativeResp = await _client.PostAsJsonAsync("/api/endocrine/closed-loop/simulate", new ClosedLoopSimulationRequest { InitialEstradiolPmolL = -10.0 });
        Assert.Equal(HttpStatusCode.BadRequest, negativeResp.StatusCode);
    }

    /// <summary>
    /// Verifies the <c>/api/fabrication/gel/analyze</c> endpoint: handles valid requests with 200 OK
    /// and rejects invalid dimensions, empty payloads, and out-of-range thresholds with 400 Bad Request.
    /// </summary>
    /// <returns>A task representing the asynchronous test execution.</returns>
    [Fact]
    public async Task PostFabricationGelAnalyze_ValidationAndExecutionPaths()
    {
        // 1. Valid request returns 200 OK with GelAnalysisResult
        byte[] testImage = new byte[80 * 40];
        Array.Fill(testImage, (byte)20);
        GelAnalysisRequest validReq = new()
        {
            ImageBytes = testImage,
            Width = 80,
            Height = 40,
            ExpectedLaneCount = 4,
            LadderType = GelLadderType.OneKbDnaLadder,
            LadderLaneNumber = 1,
            BackgroundThreshold = 0.15
        };

        HttpResponseMessage okResp = await _client.PostAsJsonAsync("/api/fabrication/gel/analyze", validReq);
        Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);
        GelAnalysisResult? result = await okResp.Content.ReadFromJsonAsync<GelAnalysisResult>();
        Assert.NotNull(result);
        Assert.Equal(4, result.DetectedLaneCount);
        Assert.Equal(80, result.Width);
        Assert.Equal(40, result.Height);

        // 2. Null request body returns 400 Bad Request
        using StringContent nullContent = new("null", Encoding.UTF8, "application/json");
        HttpResponseMessage nullResp = await _client.PostAsync("/api/fabrication/gel/analyze", nullContent);
        Assert.Equal(HttpStatusCode.BadRequest, nullResp.StatusCode);

        // 3. Width <= 0 or Height <= 0 returns 400 Bad Request
        HttpResponseMessage zeroWidthResp = await _client.PostAsJsonAsync("/api/fabrication/gel/analyze", new GelAnalysisRequest
        {
            ImageBytes = testImage,
            Width = 0,
            Height = 40
        });
        Assert.Equal(HttpStatusCode.BadRequest, zeroWidthResp.StatusCode);

        HttpResponseMessage zeroHeightResp = await _client.PostAsJsonAsync("/api/fabrication/gel/analyze", new GelAnalysisRequest
        {
            ImageBytes = testImage,
            Width = 80,
            Height = 0
        });
        Assert.Equal(HttpStatusCode.BadRequest, zeroHeightResp.StatusCode);

        // 4. Empty ImageBytes returns 400 Bad Request
        HttpResponseMessage emptyBytesResp = await _client.PostAsJsonAsync("/api/fabrication/gel/analyze", new GelAnalysisRequest
        {
            ImageBytes = [],
            Width = 80,
            Height = 40
        });
        Assert.Equal(HttpStatusCode.BadRequest, emptyBytesResp.StatusCode);

        // 5. ExpectedLaneCount <= 0 or > 24 returns 400 Bad Request
        HttpResponseMessage zeroLanesResp = await _client.PostAsJsonAsync("/api/fabrication/gel/analyze", new GelAnalysisRequest
        {
            ImageBytes = testImage,
            Width = 80,
            Height = 40,
            ExpectedLaneCount = 0
        });
        Assert.Equal(HttpStatusCode.BadRequest, zeroLanesResp.StatusCode);

        HttpResponseMessage highLanesResp = await _client.PostAsJsonAsync("/api/fabrication/gel/analyze", new GelAnalysisRequest
        {
            ImageBytes = testImage,
            Width = 80,
            Height = 40,
            ExpectedLaneCount = 25
        });
        Assert.Equal(HttpStatusCode.BadRequest, highLanesResp.StatusCode);

        // 6. BackgroundThreshold < 0 or > 1 returns 400 Bad Request
        HttpResponseMessage lowBgResp = await _client.PostAsJsonAsync("/api/fabrication/gel/analyze", new GelAnalysisRequest
        {
            ImageBytes = testImage,
            Width = 80,
            Height = 40,
            BackgroundThreshold = -0.05
        });
        Assert.Equal(HttpStatusCode.BadRequest, lowBgResp.StatusCode);

        HttpResponseMessage highBgResp = await _client.PostAsJsonAsync("/api/fabrication/gel/analyze", new GelAnalysisRequest
        {
            ImageBytes = testImage,
            Width = 80,
            Height = 40,
            BackgroundThreshold = 1.05
        });
        Assert.Equal(HttpStatusCode.BadRequest, highBgResp.StatusCode);

        // 7. LadderLaneNumber <= 0 or > ExpectedLaneCount returns 400 Bad Request
        HttpResponseMessage zeroLadderResp = await _client.PostAsJsonAsync("/api/fabrication/gel/analyze", new GelAnalysisRequest
        {
            ImageBytes = testImage,
            Width = 80,
            Height = 40,
            ExpectedLaneCount = 4,
            LadderLaneNumber = 0
        });
        Assert.Equal(HttpStatusCode.BadRequest, zeroLadderResp.StatusCode);

        HttpResponseMessage highLadderResp = await _client.PostAsJsonAsync("/api/fabrication/gel/analyze", new GelAnalysisRequest
        {
            ImageBytes = testImage,
            Width = 80,
            Height = 40,
            ExpectedLaneCount = 4,
            LadderLaneNumber = 5
        });
        Assert.Equal(HttpStatusCode.BadRequest, highLadderResp.StatusCode);

        // 8. ImageBytes length smaller than Width * Height returns 400 Bad Request
        HttpResponseMessage shortBufferResp = await _client.PostAsJsonAsync("/api/fabrication/gel/analyze", new GelAnalysisRequest
        {
            ImageBytes = new byte[100],
            Width = 80,
            Height = 40
        });
        Assert.Equal(HttpStatusCode.BadRequest, shortBufferResp.StatusCode);
    }

    /// <summary>
    /// Verifies that POST /api/datasets/import returns 400 Bad Request when archive or manifest is missing.
    /// </summary>
    [Fact]
    public async Task ImportDataset_NullArchiveOrManifest_Returns400BadRequest()
    {
        // WHAT: Tests POST /api/datasets/import when only one of the two multipart form files is provided.
        // WHY:  Ensures line 747 in Program.cs handles archive == null || manifest == null branches.
        // HOW:  Posts multipart form with only archive, then only manifest, asserting 400 Bad Request.
        string token = await ProvisionUserAndGetTokenAsync("admin-import-tester", UserRole.Administrator);
        using HttpClient client = CreateClientWithBearer(token);

        // 1. Only archive file provided (manifest is null)
        using (MultipartFormDataContent form1 = new())
        {
            form1.Add(new ByteArrayContent("TEST_ARCHIVE"u8.ToArray()), "archive", "test.tar.gz");
            HttpResponseMessage resp1 = await client.PostAsync("/api/datasets/import", form1);
            Assert.Equal(HttpStatusCode.BadRequest, resp1.StatusCode);
        }

        // 2. Only manifest file provided (archive is null)
        using (MultipartFormDataContent form2 = new())
        {
            form2.Add(new ByteArrayContent("{}"u8.ToArray()), "manifest", "manifest.json");
            HttpResponseMessage resp2 = await client.PostAsync("/api/datasets/import", form2);
            Assert.Equal(HttpStatusCode.BadRequest, resp2.StatusCode);
        }
    }

    /// <summary>
    /// Verifies that POST /api/setup/initialize returns 400 Bad Request with RFC 7807 problem details when setup has already completed.
    /// </summary>
    [Fact]
    public async Task InitializeSetup_WhenAlreadyCompleted_Returns400ProblemDetails()
    {
        // WHAT: Dispatches POST /api/setup/initialize on an already-configured instance.
        // WHY:  Exercises lines 1763-1772 in Program.cs where setup conflict InvalidOperationException is caught.
        // HOW:  Sends InitialSetupRequest; expects 400 Bad Request with RFC 7807 problem details.
        HttpClient client = _factory.CreateClient();
        var setupReq = new InitialSetupRequest
        {
            AdminUsername = "admin-late",
            AccessGate = AccessGate.ApiWeb,
            PublicKeyBase64 = Convert.ToBase64String(new byte[32]),
            Algorithm = KeyAlgorithm.Ed25519
        };

        HttpResponseMessage resp = await client.PostAsJsonAsync("/api/setup/initialize", setupReq);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("System has already completed initial Administrator setup.", body);
    }

    /// <summary>
    /// Verifies that non-admin patient accounts attempting cross-subject access on User Profiles, Addresses, and Contacts receive 403 Forbidden.
    /// </summary>
    [Fact]
    public async Task UserProfilesAndContacts_ForbiddenCrossAccess_Returns403()
    {
        // WHAT: Tests authorization gating on /api/users/{username}/* routes.
        // WHY:  Exercises lines 2035, 2075, 2119, 2154, 2202, 2244, 2279, 2327 403 Forbidden branches.
        // HOW:  Provisions patient-charlie and patient-dana; uses charlie's client to access dana's resources.
        string tokenCharlie = await ProvisionUserAndGetTokenAsync("patient-charlie", UserRole.Patient);
        using HttpClient clientCharlie = CreateClientWithBearer(tokenCharlie);

        // 1. GET /api/users/patient-dana/profile
        HttpResponseMessage getProfResp = await clientCharlie.GetAsync("/api/users/patient-dana/profile");
        Assert.Equal(HttpStatusCode.Forbidden, getProfResp.StatusCode);

        // 2. PUT /api/users/patient-dana/profile
        HttpResponseMessage putProfResp = await clientCharlie.PutAsJsonAsync("/api/users/patient-dana/profile", new UserProfile { Username = "patient-dana" });
        Assert.Equal(HttpStatusCode.Forbidden, putProfResp.StatusCode);

        // 3. GET /api/users/patient-dana/addresses
        HttpResponseMessage getAddrResp = await clientCharlie.GetAsync("/api/users/patient-dana/addresses");
        Assert.Equal(HttpStatusCode.Forbidden, getAddrResp.StatusCode);

        // 4. POST /api/users/patient-dana/addresses
        HttpResponseMessage postAddrResp = await clientCharlie.PostAsJsonAsync("/api/users/patient-dana/addresses", new PatientAddress { AddressLine1 = "123 St" });
        Assert.Equal(HttpStatusCode.Forbidden, postAddrResp.StatusCode);

        // 5. DELETE /api/users/patient-dana/addresses/addr-1
        HttpResponseMessage delAddrResp = await clientCharlie.DeleteAsync("/api/users/patient-dana/addresses/addr-1");
        Assert.Equal(HttpStatusCode.Forbidden, delAddrResp.StatusCode);

        // 6. GET /api/users/patient-dana/contacts
        HttpResponseMessage getContResp = await clientCharlie.GetAsync("/api/users/patient-dana/contacts");
        Assert.Equal(HttpStatusCode.Forbidden, getContResp.StatusCode);

        // 7. POST /api/users/patient-dana/contacts
        HttpResponseMessage postContResp = await clientCharlie.PostAsJsonAsync("/api/users/patient-dana/contacts", new PatientContact { ContactName = "Dana Emergency", PhoneNumber = "555-1234" });
        Assert.Equal(HttpStatusCode.Forbidden, postContResp.StatusCode);

        // 8. DELETE /api/users/patient-dana/contacts/cont-1
        HttpResponseMessage delContResp = await clientCharlie.DeleteAsync("/api/users/patient-dana/contacts/cont-1");
        Assert.Equal(HttpStatusCode.Forbidden, delContResp.StatusCode);
    }

    /// <summary>
    /// Verifies that unauthorized patient callers querying other patients' DNA, imports, and results receive 403 or 404 access rejections.
    /// </summary>
    [Fact]
    public async Task PatientDnaImportsAndResults_ForbiddenCrossAccess_Returns403Or404()
    {
        // WHAT: Tests access gating on /api/patients/{username}/* routes.
        // WHY:  Exercises 403 Forbidden branches across Patient DNA, Patient Imports, and Patient Results endpoints.
        // HOW:  Provisions patient-eve and patient-frank; attempts cross-patient access with eve's token.
        string tokenEve = await ProvisionUserAndGetTokenAsync("patient-eve", UserRole.Patient);
        using HttpClient clientEve = CreateClientWithBearer(tokenEve);

        // 1. GET /api/patients/patient-frank/dna
        HttpResponseMessage getDnaResp = await clientEve.GetAsync("/api/patients/patient-frank/dna");
        Assert.True(getDnaResp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);

        // 2. GET /api/patients/patient-frank/dna/dna-1
        HttpResponseMessage getDnaIdResp = await clientEve.GetAsync("/api/patients/patient-frank/dna/dna-1");
        Assert.True(getDnaIdResp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);

        // 3. POST /api/patients/patient-frank/dna
        HttpResponseMessage postDnaResp = await clientEve.PostAsJsonAsync("/api/patients/patient-frank/dna", new CreatePatientDnaRequest { Title = "Hexagonal Nanotube" });
        Assert.True(postDnaResp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);

        // 4. GET /api/patients/patient-frank/imports
        HttpResponseMessage getImpResp = await clientEve.GetAsync("/api/patients/patient-frank/imports");
        Assert.True(getImpResp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);

        // 5. GET /api/patients/patient-frank/imports/imp-1
        HttpResponseMessage getImpIdResp = await clientEve.GetAsync("/api/patients/patient-frank/imports/imp-1");
        Assert.True(getImpIdResp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);

        // 6. POST /api/patients/patient-frank/imports
        HttpResponseMessage postImpResp = await clientEve.PostAsJsonAsync("/api/patients/patient-frank/imports", new CreatePatientImportRequest { FileName = "frank.fasta", SequenceOrData = "ATCG" });
        Assert.True(postImpResp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);

        // 7. GET /api/patients/patient-frank/results
        HttpResponseMessage getResResp = await clientEve.GetAsync("/api/patients/patient-frank/results");
        Assert.True(getResResp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);

        // 8. GET /api/patients/patient-frank/results/res-1
        HttpResponseMessage getResIdResp = await clientEve.GetAsync("/api/patients/patient-frank/results/res-1");
        Assert.True(getResIdResp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);

        // 9. POST /api/patients/patient-frank/results
        HttpResponseMessage postResResp = await clientEve.PostAsJsonAsync("/api/patients/patient-frank/results", new CreatePatientResultRequest { Title = "Kinetics", ResultType = "EndocrineKinetics", Summary = "Test" });
        Assert.True(postResResp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies that string enum literals (e.g. 'FemaleToMale', 'AndrogenicDominant', 'Flex', 'M13mp18Scaffold')
    /// deserialize with HTTP 200 OK across Genomics, Closed-Loop, Opentrons, Gel, and PBPK endpoints,
    /// maintaining backwards-compatible parity with integer enum serialization.
    /// </summary>
    [Fact]
    public async Task Endpoints_StringAndIntegerEnumDeserializationParity_ReturnsOk()
    {
        // WHAT: Tests string and integer enum deserialization across clinical and synthetic biology endpoints.
        // WHY:  Fixes senior biomedical engineering audit finding (Issue #174) ensuring seamless client DX.
        // HOW:  Sends raw JSON string payloads and integer payloads to all 5 endpoints and asserts HttpStatusCode.OK.

        // 1. Genomics Analysis: Regimen as string "FemaleToMale" and integer 1
        string jsonGenomicsString = "{\"patientId\":\"PATIENT-ENUM-01\",\"markerIds\":[\"F5_LEIDEN\"],\"regimen\":\"FemaleToMale\"}";
        using StringContent contentGenomicsString = new(jsonGenomicsString, Encoding.UTF8, "application/json");
        HttpResponseMessage respGenomicsString = await _client.PostAsync("/api/genomics/analyze", contentGenomicsString);
        Assert.Equal(HttpStatusCode.OK, respGenomicsString.StatusCode);

        string jsonGenomicsInt = "{\"patientId\":\"PATIENT-ENUM-01\",\"markerIds\":[\"F5_LEIDEN\"],\"regimen\":1}";
        using StringContent contentGenomicsInt = new(jsonGenomicsInt, Encoding.UTF8, "application/json");
        HttpResponseMessage respGenomicsInt = await _client.PostAsync("/api/genomics/analyze", contentGenomicsInt);
        Assert.Equal(HttpStatusCode.OK, respGenomicsInt.StatusCode);

        // 2. Closed-Loop Simulation: TargetState as string "AndrogenicDominant"
        string jsonClosedLoop = "{\"targetState\":\"AndrogenicDominant\",\"totalHours\":24,\"stepHours\":1.0}";
        using StringContent contentClosedLoop = new(jsonClosedLoop, Encoding.UTF8, "application/json");
        HttpResponseMessage respClosedLoop = await _client.PostAsync("/api/endocrine/closed-loop/simulate", contentClosedLoop);
        Assert.Equal(HttpStatusCode.OK, respClosedLoop.StatusCode);

        // 3. Opentrons Protocol Script: Regimen as string "FemaleToMale", RobotModel "Flex", PipetteModel "Flex_1Channel_50", SourcePlateType "Nest384WellPcr"
        string jsonOpentrons = "{\"sequenceOrSampleId\":\"CAGE-6HB-E2-001\",\"regimen\":\"FemaleToMale\",\"geometry\":\"Nanotube\",\"options\":{\"robotModel\":\"Flex\",\"pipetteModel\":\"Flex_1Channel_50\",\"sourcePlateType\":\"Nest384WellPcr\"}}";
        using StringContent contentOpentrons = new(jsonOpentrons, Encoding.UTF8, "application/json");
        HttpResponseMessage respOpentrons = await _client.PostAsync("/api/fabrication/opentrons/generate", contentOpentrons);
        Assert.Equal(HttpStatusCode.OK, respOpentrons.StatusCode);

        // 4. Gel Electrophoresis Analysis: LadderType as string "M13mp18Scaffold"
        byte[] gelBytes = new byte[200];
        string base64Gel = Convert.ToBase64String(gelBytes);
        string jsonGel = $"{{\"imageBytes\":\"{base64Gel}\",\"width\":20,\"height\":10,\"expectedLaneCount\":2,\"ladderType\":\"M13mp18Scaffold\",\"ladderLaneNumber\":1,\"backgroundThreshold\":0.15}}";
        using StringContent contentGel = new(jsonGel, Encoding.UTF8, "application/json");
        HttpResponseMessage respGel = await _client.PostAsync("/api/fabrication/gel/analyze", contentGel);
        Assert.Equal(HttpStatusCode.OK, respGel.StatusCode);

        // 5. PBPK Simulation: Regimen as string "FemaleToMale"
        string jsonPbpk = "{\"patientId\":\"SUBJECT-ENUM-01\",\"regimen\":\"FemaleToMale\",\"depotDoseMg\":30.0,\"simulationDurationDays\":30,\"deliveryIntervalDays\":90,\"genomicMarkerIds\":[]}";
        using StringContent contentPbpk = new(jsonPbpk, Encoding.UTF8, "application/json");
        HttpResponseMessage respPbpk = await _client.PostAsync("/api/simulations/run/pbpk-endocrine", contentPbpk);
        Assert.Equal(HttpStatusCode.OK, respPbpk.StatusCode);
    }

    /// <summary>
    /// Verifies that POST /api/depots/simulate-release simulates 180-day depot kinetics, supports string and integer enum deserialization,
    /// enforces role gating, and rejects out-of-range parameters with 400 Bad Request.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task SimulateDepotRelease_ValidAndInvalidRequests()
    {
        // WHAT: Tests the POST /api/depots/simulate-release endpoint.
        // WHY:  Verifies API contract, numerical simulation execution, input boundary validation, enum deserialization parity, and role gating.
        // HOW:  Executes positive simulations, tests string and integer enums, tests parameter validation branches, and validates role gating.

        // 1. Positive MtF simulation with default/null baseline concentrations
        SimulateDepotReleaseRequest validMtf = new()
        {
            Regimen = TherapeuticRegimen.MaleToFemale,
            MatrixType = PolymerMatrixType.PlgaPegPlga,
            KineticsModel = ReleaseKineticsModel.KorsmeyerPeppas,
            InitialNanocarrierMassMg = 16.0,
            HorizonDays = 180,
            DailyErosionRatePercent = 0.55,
            ClearanceMultiplier = 1.0,
            TargetDailyDoseMg = 0.08
        };
        HttpResponseMessage okMtf = await _client.PostAsJsonAsync("/api/depots/simulate-release", validMtf);
        Assert.Equal(HttpStatusCode.OK, okMtf.StatusCode);
        TimeReleaseSimulationResult? resultMtf = await okMtf.Content.ReadFromJsonAsync<TimeReleaseSimulationResult>();
        Assert.NotNull(resultMtf);
        Assert.Equal(181, resultMtf.Points.Count);
        Assert.True(resultMtf.BurstSuppressed);
        Assert.True(resultMtf.LhSuppressionAchieved);
        Assert.True(resultMtf.FshSuppressionAchieved);

        // 2. Positive FtM simulation with custom baseline concentrations
        SimulateDepotReleaseRequest validFtm = new()
        {
            Regimen = TherapeuticRegimen.FemaleToMale,
            MatrixType = PolymerMatrixType.PlgaPegPlga,
            KineticsModel = ReleaseKineticsModel.KorsmeyerPeppas,
            InitialNanocarrierMassMg = 1050.0,
            HorizonDays = 180,
            DailyErosionRatePercent = 0.55,
            ClearanceMultiplier = 1.0,
            TargetDailyDoseMg = 6.0,
            InitialEstradiolPmolL = 500.0,
            InitialTestosteroneNmolL = 2.0,
            InitialLhIuL = 6.0,
            InitialFshIuL = 4.5
        };
        HttpResponseMessage okFtm = await _client.PostAsJsonAsync("/api/depots/simulate-release", validFtm);
        Assert.Equal(HttpStatusCode.OK, okFtm.StatusCode);
        TimeReleaseSimulationResult? resultFtm = await okFtm.Content.ReadFromJsonAsync<TimeReleaseSimulationResult>();
        Assert.NotNull(resultFtm);
        Assert.Equal(181, resultFtm.Points.Count);
        Assert.True(resultFtm.BurstSuppressed);

        // 3. Positive Custom regimen simulation
        SimulateDepotReleaseRequest validCustom = new()
        {
            Regimen = TherapeuticRegimen.Custom,
            MatrixType = PolymerMatrixType.AlginateChitosan,
            KineticsModel = ReleaseKineticsModel.Higuchi,
            InitialNanocarrierMassMg = 100.0,
            HorizonDays = 90,
            DailyErosionRatePercent = 0.40,
            ClearanceMultiplier = 1.1,
            InitialEstradiolPmolL = 200.0,
            InitialTestosteroneNmolL = 5.0,
            InitialLhIuL = 5.0,
            InitialFshIuL = 4.0
        };
        HttpResponseMessage okCustom = await _client.PostAsJsonAsync("/api/depots/simulate-release", validCustom);
        Assert.Equal(HttpStatusCode.OK, okCustom.StatusCode);

        // 4. Raw JSON string enum literals deserialization parity (Issue #174)
        string jsonStringEnums = "{\"regimen\":\"MaleToFemale\",\"matrixType\":\"PlgaPegPlga\",\"kineticsModel\":\"KorsmeyerPeppas\",\"initialNanocarrierMassMg\":16.0,\"horizonDays\":180,\"dailyErosionRatePercent\":0.55,\"clearanceMultiplier\":1.0}";
        using StringContent contentStringEnums = new(jsonStringEnums, Encoding.UTF8, "application/json");
        HttpResponseMessage okStringEnums = await _client.PostAsync("/api/depots/simulate-release", contentStringEnums);
        Assert.Equal(HttpStatusCode.OK, okStringEnums.StatusCode);

        // 5. Raw JSON integer enum literals deserialization parity (Issue #174)
        string jsonIntEnums = "{\"regimen\":0,\"matrixType\":0,\"kineticsModel\":0,\"initialNanocarrierMassMg\":16.0,\"horizonDays\":180,\"dailyErosionRatePercent\":0.55,\"clearanceMultiplier\":1.0}";
        using StringContent contentIntEnums = new(jsonIntEnums, Encoding.UTF8, "application/json");
        HttpResponseMessage okIntEnums = await _client.PostAsync("/api/depots/simulate-release", contentIntEnums);
        Assert.Equal(HttpStatusCode.OK, okIntEnums.StatusCode);

        // 6. Validation: Null request body -> 400 Bad Request
        using StringContent nullContent = new("null", Encoding.UTF8, "application/json");
        HttpResponseMessage nullResp = await _client.PostAsync("/api/depots/simulate-release", nullContent);
        Assert.Equal(HttpStatusCode.BadRequest, nullResp.StatusCode);

        // 7. Validation: Non-positive mass -> 400 Bad Request
        HttpResponseMessage zeroMassResp = await _client.PostAsJsonAsync("/api/depots/simulate-release", validMtf with { InitialNanocarrierMassMg = 0.0 });
        Assert.Equal(HttpStatusCode.BadRequest, zeroMassResp.StatusCode);
        HttpResponseMessage negMassResp = await _client.PostAsJsonAsync("/api/depots/simulate-release", validMtf with { InitialNanocarrierMassMg = -5.0 });
        Assert.Equal(HttpStatusCode.BadRequest, negMassResp.StatusCode);

        // 8. Validation: Invalid horizon (< 1 or > 365) -> 400 Bad Request
        HttpResponseMessage zeroDaysResp = await _client.PostAsJsonAsync("/api/depots/simulate-release", validMtf with { HorizonDays = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, zeroDaysResp.StatusCode);
        HttpResponseMessage highDaysResp = await _client.PostAsJsonAsync("/api/depots/simulate-release", validMtf with { HorizonDays = 366 });
        Assert.Equal(HttpStatusCode.BadRequest, highDaysResp.StatusCode);

        // 9. Validation: Invalid erosion (<= 0 or > 100) -> 400 Bad Request
        HttpResponseMessage zeroErosionResp = await _client.PostAsJsonAsync("/api/depots/simulate-release", validMtf with { DailyErosionRatePercent = 0.0 });
        Assert.Equal(HttpStatusCode.BadRequest, zeroErosionResp.StatusCode);
        HttpResponseMessage highErosionResp = await _client.PostAsJsonAsync("/api/depots/simulate-release", validMtf with { DailyErosionRatePercent = 101.0 });
        Assert.Equal(HttpStatusCode.BadRequest, highErosionResp.StatusCode);

        // 10. Validation: Invalid clearance (< 0.1 or > 5.0) -> 400 Bad Request
        HttpResponseMessage lowClearanceResp = await _client.PostAsJsonAsync("/api/depots/simulate-release", validMtf with { ClearanceMultiplier = 0.05 });
        Assert.Equal(HttpStatusCode.BadRequest, lowClearanceResp.StatusCode);
        HttpResponseMessage highClearanceResp = await _client.PostAsJsonAsync("/api/depots/simulate-release", validMtf with { ClearanceMultiplier = 5.5 });
        Assert.Equal(HttpStatusCode.BadRequest, highClearanceResp.StatusCode);

        // 11. Validation: Negative initial concentrations -> 400 Bad Request
        HttpResponseMessage negE2Resp = await _client.PostAsJsonAsync("/api/depots/simulate-release", validMtf with { InitialEstradiolPmolL = -1.0 });
        Assert.Equal(HttpStatusCode.BadRequest, negE2Resp.StatusCode);
        HttpResponseMessage negTResp = await _client.PostAsJsonAsync("/api/depots/simulate-release", validMtf with { InitialTestosteroneNmolL = -1.0 });
        Assert.Equal(HttpStatusCode.BadRequest, negTResp.StatusCode);
        HttpResponseMessage negLhResp = await _client.PostAsJsonAsync("/api/depots/simulate-release", validMtf with { InitialLhIuL = -0.5 });
        Assert.Equal(HttpStatusCode.BadRequest, negLhResp.StatusCode);
        HttpResponseMessage negFshResp = await _client.PostAsJsonAsync("/api/depots/simulate-release", validMtf with { InitialFshIuL = -0.5 });
        Assert.Equal(HttpStatusCode.BadRequest, negFshResp.StatusCode);

        // 12. Role Gating: Unauthenticated call -> 401 Unauthorized
        using HttpClient anonClient = CreateClientWithBearer(null);
        HttpResponseMessage anonResp = await anonClient.PostAsJsonAsync("/api/depots/simulate-release", validMtf);
        Assert.Equal(HttpStatusCode.Unauthorized, anonResp.StatusCode);

        // 13. Role Gating: Patient role call -> 403 Forbidden
        string patientToken = await ProvisionUserAndGetTokenAsync("depot_patient_sim", UserRole.Patient);
        using HttpClient patientClient = CreateClientWithBearer(patientToken);
        HttpResponseMessage patientResp = await patientClient.PostAsJsonAsync("/api/depots/simulate-release", validMtf);
        Assert.Equal(HttpStatusCode.Forbidden, patientResp.StatusCode);

        // 14. Role Gating: Researcher role call -> 200 OK
        string researcherToken = await ProvisionUserAndGetTokenAsync("depot_researcher_sim", UserRole.Researcher);
        using HttpClient researcherClient = CreateClientWithBearer(researcherToken);
        HttpResponseMessage researcherResp = await researcherClient.PostAsJsonAsync("/api/depots/simulate-release", validMtf);
        Assert.Equal(HttpStatusCode.OK, researcherResp.StatusCode);
    }

    /// <summary>
    /// Verifies POST /api/depots/formulations and GET /api/depots/formulations/{patientId} lifecycle, input validation,
    /// schedule persistence, and tiered role gating (Administrator, Researcher with subject grant, Patient self).
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DepotFormulations_CreateAndGet_RoleGatingAndValidation()
    {
        // WHAT: Tests formulation creation and cohort retrieval across authorization tiers.
        // WHY:  Upholds Prime Directive 12 role gating and clinical persistence integrity across SQLite/Postgres.
        // HOW:  Exercises Administrator creation, Researcher with/without grant, Patient self/cross-patient access, and input validation.

        string patientAlice = "depot_patient_alice";
        string patientBob = "depot_patient_bob";
        string researcherLead = "depot_res_lead";

        string tokenAlice = await ProvisionUserAndGetTokenAsync(patientAlice, UserRole.Patient);
        string tokenBob = await ProvisionUserAndGetTokenAsync(patientBob, UserRole.Patient);
        string tokenResearcher = await ProvisionUserAndGetTokenAsync(researcherLead, UserRole.Researcher);

        using HttpClient clientAlice = CreateClientWithBearer(tokenAlice);
        using HttpClient clientBob = CreateClientWithBearer(tokenBob);
        using HttpClient clientResearcher = CreateClientWithBearer(tokenResearcher);
        using HttpClient clientAnon = CreateClientWithBearer(null);

        // 1. Validation on POST /api/depots/formulations: Null body -> 400 Bad Request
        using StringContent nullContent = new("null", Encoding.UTF8, "application/json");
        HttpResponseMessage nullResp = await _client.PostAsync("/api/depots/formulations", nullContent);
        Assert.Equal(HttpStatusCode.BadRequest, nullResp.StatusCode);

        // 2. Validation: Empty or whitespace PatientId -> 400 Bad Request
        HttpResponseMessage emptyPatientResp = await _client.PostAsJsonAsync("/api/depots/formulations", new CreateDepotFormulationRequest { PatientId = "", InitialNanocarrierMassMg = 100.0, DailyErosionRatePercent = 0.5 });
        Assert.Equal(HttpStatusCode.BadRequest, emptyPatientResp.StatusCode);
        HttpResponseMessage wsPatientResp = await _client.PostAsJsonAsync("/api/depots/formulations", new CreateDepotFormulationRequest { PatientId = "   ", InitialNanocarrierMassMg = 100.0, DailyErosionRatePercent = 0.5 });
        Assert.Equal(HttpStatusCode.BadRequest, wsPatientResp.StatusCode);

        // 3. Validation: Invalid PatientId format -> 400 Bad Request
        HttpResponseMessage invalidPatientResp = await _client.PostAsJsonAsync("/api/depots/formulations", new CreateDepotFormulationRequest { PatientId = "invalid/patient/id", InitialNanocarrierMassMg = 100.0, DailyErosionRatePercent = 0.5 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidPatientResp.StatusCode);

        // 4. Validation: Non-positive mass -> 400 Bad Request
        HttpResponseMessage zeroMassResp = await _client.PostAsJsonAsync("/api/depots/formulations", new CreateDepotFormulationRequest { PatientId = patientAlice, InitialNanocarrierMassMg = 0.0, DailyErosionRatePercent = 0.5 });
        Assert.Equal(HttpStatusCode.BadRequest, zeroMassResp.StatusCode);

        // 5. Validation: Invalid erosion (<= 0 or > 100) -> 400 Bad Request
        HttpResponseMessage zeroErosionResp = await _client.PostAsJsonAsync("/api/depots/formulations", new CreateDepotFormulationRequest { PatientId = patientAlice, InitialNanocarrierMassMg = 100.0, DailyErosionRatePercent = 0.0 });
        Assert.Equal(HttpStatusCode.BadRequest, zeroErosionResp.StatusCode);
        HttpResponseMessage highErosionResp = await _client.PostAsJsonAsync("/api/depots/formulations", new CreateDepotFormulationRequest { PatientId = patientAlice, InitialNanocarrierMassMg = 100.0, DailyErosionRatePercent = 100.5 });
        Assert.Equal(HttpStatusCode.BadRequest, highErosionResp.StatusCode);

        // 6. Validation: Invalid horizon (< 1 or > 365) -> 400 Bad Request
        HttpResponseMessage lowHorizonResp = await _client.PostAsJsonAsync("/api/depots/formulations", new CreateDepotFormulationRequest { PatientId = patientAlice, InitialNanocarrierMassMg = 100.0, DailyErosionRatePercent = 0.5, TargetHorizonDays = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, lowHorizonResp.StatusCode);
        HttpResponseMessage highHorizonResp = await _client.PostAsJsonAsync("/api/depots/formulations", new CreateDepotFormulationRequest { PatientId = patientAlice, InitialNanocarrierMassMg = 100.0, DailyErosionRatePercent = 0.5, TargetHorizonDays = 366 });
        Assert.Equal(HttpStatusCode.BadRequest, highHorizonResp.StatusCode);

        // 7. Role Gating: Unauthenticated caller -> 401 Unauthorized
        CreateDepotFormulationRequest validAliceReq = new()
        {
            PatientId = patientAlice,
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 150.0,
            DailyErosionRatePercent = 0.55,
            ReleaseKineticsModel = "Korsmeyer-Peppas",
            TargetHorizonDays = 180,
            InitialSchedule =
            [
                new DepotSchedulePoint { ScheduleId = "", TimepointDay = 1, CumulativeReleaseFraction = 0.01, InstantaneousRateMgDay = 0.8, RemainingReserveMg = 149.2 },
                new DepotSchedulePoint { ScheduleId = "explicit_sched_1", TimepointDay = 30, CumulativeReleaseFraction = 0.16, InstantaneousRateMgDay = 0.8, RemainingReserveMg = 126.0 }
            ]
        };
        HttpResponseMessage unauthCreateResp = await clientAnon.PostAsJsonAsync("/api/depots/formulations", validAliceReq);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthCreateResp.StatusCode);

        HttpResponseMessage unauthGetResp = await clientAnon.GetAsync($"/api/depots/formulations/{patientAlice}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthGetResp.StatusCode);

        // 8. Role Gating: Patient caller trying to create formulation -> 403 Forbidden
        HttpResponseMessage patientCreateResp = await clientAlice.PostAsJsonAsync("/api/depots/formulations", validAliceReq);
        Assert.Equal(HttpStatusCode.Forbidden, patientCreateResp.StatusCode);

        // 9. Role Gating: Researcher WITHOUT subject grant trying to create formulation -> 403 Forbidden
        HttpResponseMessage resNoGrantCreateResp = await clientResearcher.PostAsJsonAsync("/api/depots/formulations", validAliceReq);
        Assert.Equal(HttpStatusCode.Forbidden, resNoGrantCreateResp.StatusCode);

        // 10. Role Gating: Researcher WITHOUT grant trying to read formulation -> 403 Forbidden
        HttpResponseMessage resNoGrantGetResp = await clientResearcher.GetAsync($"/api/depots/formulations/{patientAlice}");
        Assert.Equal(HttpStatusCode.Forbidden, resNoGrantGetResp.StatusCode);

        // 11. Positive path: Administrator creates formulation with schedule -> 201 Created
        HttpResponseMessage adminCreateResp = await _client.PostAsJsonAsync("/api/depots/formulations", validAliceReq);
        Assert.Equal(HttpStatusCode.Created, adminCreateResp.StatusCode);
        Assert.NotNull(adminCreateResp.Headers.Location);
        DepotFormulationResponse? createdAliceResp = await adminCreateResp.Content.ReadFromJsonAsync<DepotFormulationResponse>();
        Assert.NotNull(createdAliceResp);
        Assert.Equal(patientAlice, createdAliceResp.Formulation.PatientId);
        Assert.Equal(2, createdAliceResp.Schedule.Count);
        Assert.False(string.IsNullOrWhiteSpace(createdAliceResp.Schedule[0].ScheduleId));
        Assert.Equal("explicit_sched_1", createdAliceResp.Schedule[1].ScheduleId);

        // 12. Positive path: Administrator creates formulation with null schedule and alias fallbacks -> 201 Created
        CreateDepotFormulationRequest fallbackReq = new()
        {
            PatientId = patientAlice,
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "",
            MatrixType = "CrosslinkedHydrogel",
            InitialNanocarrierMassMg = 80.0,
            DailyErosionRatePercent = 0.45,
            ReleaseKineticsModel = "",
            KineticsModel = "CoupledAnomalousFickian",
            TargetHorizonDays = 90,
            InitialSchedule = null
        };
        HttpResponseMessage fallbackCreateResp = await _client.PostAsJsonAsync("/api/depots/formulations", fallbackReq);
        Assert.Equal(HttpStatusCode.Created, fallbackCreateResp.StatusCode);
        DepotFormulationResponse? createdFallbackResp = await fallbackCreateResp.Content.ReadFromJsonAsync<DepotFormulationResponse>();
        Assert.NotNull(createdFallbackResp);
        Assert.Equal("CrosslinkedHydrogel", createdFallbackResp.Formulation.PolymerMatrixType);
        Assert.Equal("CoupledAnomalousFickian", createdFallbackResp.Formulation.ReleaseKineticsModel);
        Assert.Empty(createdFallbackResp.Schedule);

        // 13. Positive path: Administrator reads formulations for patientAlice -> 200 OK (2 items)
        HttpResponseMessage adminGetResp = await _client.GetAsync($"/api/depots/formulations/{patientAlice}");
        Assert.Equal(HttpStatusCode.OK, adminGetResp.StatusCode);
        List<DepotFormulationResponse>? aliceDepots = await adminGetResp.Content.ReadFromJsonAsync<List<DepotFormulationResponse>>();
        Assert.NotNull(aliceDepots);
        Assert.Equal(2, aliceDepots.Count);

        // 14. Positive path: Patient reads own formulations (Self-Access) -> 200 OK
        HttpResponseMessage selfGetResp = await clientAlice.GetAsync($"/api/depots/formulations/{patientAlice}");
        Assert.Equal(HttpStatusCode.OK, selfGetResp.StatusCode);
        List<DepotFormulationResponse>? selfDepots = await selfGetResp.Content.ReadFromJsonAsync<List<DepotFormulationResponse>>();
        Assert.NotNull(selfDepots);
        Assert.Equal(2, selfDepots.Count);

        // 15. Cross-Patient Access Denied: Patient Bob tries to read Patient Alice's formulations -> 403 Forbidden
        HttpResponseMessage crossGetResp = await clientBob.GetAsync($"/api/depots/formulations/{patientAlice}");
        Assert.Equal(HttpStatusCode.Forbidden, crossGetResp.StatusCode);

        // 16. Grant researcher subject access on patientBob, then verify Researcher access
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ISubjectAccessService accessService = scope.ServiceProvider.GetRequiredService<ISubjectAccessService>();
            await accessService.GrantSubjectAccessAsync("admin", researcherLead, patientBob, SubjectAccessLevel.FullReadAccess, "Test grant");
        }

        CreateDepotFormulationRequest bobReq = new()
        {
            PatientId = patientBob,
            Regimen = TherapeuticRegimen.FemaleToMale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 950.0,
            DailyErosionRatePercent = 0.50,
            ReleaseKineticsModel = "Korsmeyer-Peppas",
            TargetHorizonDays = 180,
            InitialSchedule = []
        };

        // Researcher WITH subject grant creates formulation for patientBob -> 201 Created
        HttpResponseMessage resGrantedCreateResp = await clientResearcher.PostAsJsonAsync("/api/depots/formulations", bobReq);
        Assert.Equal(HttpStatusCode.Created, resGrantedCreateResp.StatusCode);

        // Researcher WITH subject grant reads formulations for patientBob -> 200 OK
        HttpResponseMessage resGrantedGetResp = await clientResearcher.GetAsync($"/api/depots/formulations/{patientBob}");
        Assert.Equal(HttpStatusCode.OK, resGrantedGetResp.StatusCode);
        List<DepotFormulationResponse>? bobDepots = await resGrantedGetResp.Content.ReadFromJsonAsync<List<DepotFormulationResponse>>();
        Assert.NotNull(bobDepots);
        Assert.Single(bobDepots);

        // 17. GET formulations for a patient with zero formulations -> 200 OK (empty list)
        string patientEmpty = "depot_patient_empty";
        string tokenEmpty = await ProvisionUserAndGetTokenAsync(patientEmpty, UserRole.Patient);
        using HttpClient clientEmpty = CreateClientWithBearer(tokenEmpty);
        HttpResponseMessage emptyGetResp = await clientEmpty.GetAsync($"/api/depots/formulations/{patientEmpty}");
        Assert.Equal(HttpStatusCode.OK, emptyGetResp.StatusCode);
        List<DepotFormulationResponse>? emptyList = await emptyGetResp.Content.ReadFromJsonAsync<List<DepotFormulationResponse>>();
        Assert.NotNull(emptyList);
        Assert.Empty(emptyList);

        // 18. GET formulations with invalid identifier format -> 400 Bad Request
        HttpResponseMessage invalidGetResp = await _client.GetAsync("/api/depots/formulations/bad*id");
        Assert.Equal(HttpStatusCode.BadRequest, invalidGetResp.StatusCode);

        // 19. POST /api/depots/simulate-release forwards DailyErosionRatePercent
        HttpResponseMessage slowSimResp = await _client.PostAsJsonAsync("/api/depots/simulate-release", new SimulateDepotReleaseRequest
        {
            InitialNanocarrierMassMg = 16.0,
            HorizonDays = 180,
            DailyErosionRatePercent = 0.30
        });
        Assert.Equal(HttpStatusCode.OK, slowSimResp.StatusCode);
        TimeReleaseSimulationResult? slowSimResult = await slowSimResp.Content.ReadFromJsonAsync<TimeReleaseSimulationResult>();
        Assert.NotNull(slowSimResult);

        HttpResponseMessage fastSimResp = await _client.PostAsJsonAsync("/api/depots/simulate-release", new SimulateDepotReleaseRequest
        {
            InitialNanocarrierMassMg = 16.0,
            HorizonDays = 180,
            DailyErosionRatePercent = 0.90
        });
        Assert.Equal(HttpStatusCode.OK, fastSimResp.StatusCode);
        TimeReleaseSimulationResult? fastSimResult = await fastSimResp.Content.ReadFromJsonAsync<TimeReleaseSimulationResult>();
        Assert.NotNull(fastSimResult);
        Assert.True(fastSimResult.AverageDailyReleaseMg > slowSimResult.AverageDailyReleaseMg);
    }

    /// <summary>
    /// Verifies Milestone 34 longitudinal depot serum assay ingestion, PHI encryption/decryption,
    /// adaptive Bayesian calibration, role and subject-access gating, and 21 CFR Part 11 audit trails.
    /// </summary>
    [Fact]
    public async Task DepotCalibrationEndpoints_EnforceValidationGatingEncryptionCalibrationAndAudit()
    {
        // WHAT: End-to-end integration test for POST/GET /api/depots/{depotId}/assays, POST /calibrate, and GET /calibrations/latest.
        // WHY:  Validates Phase 3 API contracts, PHI encryption at rest, role/cohort isolation, RFC 7807 errors, and audit logging.
        // HOW:  Provisions Admin, Researcher, and Patients; seeds depots; exercises every status code (200, 201, 400, 401, 403, 404).

        // 1. Verify API DTO defaults, GetExamples(), and DepotCalibrationMapper helpers
        RecordDepotAssayRequest reqDef = new();
        Assert.Equal(30, reqDef.DaySinceImplant);
        Assert.False(string.IsNullOrWhiteSpace(reqDef.GetExamples().ClinicalNotes));

        DepotAssayObservationResponse obsDef = new();
        Assert.Equal(string.Empty, obsDef.ObservationId);
        Assert.False(string.IsNullOrWhiteSpace(obsDef.GetExamples().ObservationId));

        DepotCalibrationResponse calDef = new();
        Assert.Equal(180, calDef.AdjustedRedoseDay);
        Assert.False(string.IsNullOrWhiteSpace(calDef.GetExamples().SnapshotId));

        Assert.Throws<ArgumentNullException>(() => DepotCalibrationMapper.ToBaselineParameters(null!));
        Assert.Throws<ArgumentNullException>(() => DepotCalibrationMapper.ToDomainObservation(null!));
        Assert.Throws<ArgumentNullException>(() => DepotCalibrationMapper.ToApiResponse(null!, null!));

        using (IServiceScope mapperScope = _factory.Services.CreateScope())
        {
            ProfileDataProtector protector = mapperScope.ServiceProvider.GetRequiredService<ProfileDataProtector>();
            Assert.Throws<ArgumentNullException>(() => DepotCalibrationMapper.ToApiResponse(new DepotAssayObservationRecord(), null!));
            Assert.Equal(PolymerMatrixType.PlgaPegPlga, DepotCalibrationMapper.ParsePolymerMatrix(null));
            Assert.Equal(PolymerMatrixType.PlgaPegPlga, DepotCalibrationMapper.ParsePolymerMatrix("unknown-matrix"));
            Assert.Equal(PolymerMatrixType.AlginateChitosan, DepotCalibrationMapper.ParsePolymerMatrix("Alginate-Chitosan"));
            Assert.Equal(ReleaseKineticsModel.KorsmeyerPeppas, DepotCalibrationMapper.ParseReleaseKinetics(null));
            Assert.Equal(ReleaseKineticsModel.KorsmeyerPeppas, DepotCalibrationMapper.ParseReleaseKinetics("unknown-model"));
            Assert.Equal(ReleaseKineticsModel.CoupledAnomalousFickian, DepotCalibrationMapper.ParseReleaseKinetics("Coupled-Fickian"));
            Assert.Equal(ReleaseKineticsModel.Higuchi, DepotCalibrationMapper.ParseReleaseKinetics("Higuchi"));

            TimeReleaseDepotParameters ftmParams = DepotCalibrationMapper.ToBaselineParameters(new DepotFormulationRecord
            {
                Regimen = TherapeuticRegimen.FemaleToMale,
                InitialNanocarrierMassMg = 1000.0,
                DailyErosionRatePercent = 0.55,
                TargetHorizonDays = 180
            });
            Assert.Equal(20.0, ftmParams.TargetTestosteroneNmolL);
            Assert.NotNull(DepotCalibrationMapper.ToApiResponse(new DepotAssayObservationRecord(), protector));
        }

        // 2. Provision isolated identities
        string patientA = "cal_patient_a";
        string patientB = "cal_patient_b";
        string researcher = "cal_researcher";
        string tokenA = await ProvisionUserAndGetTokenAsync(patientA, UserRole.Patient);
        string tokenB = await ProvisionUserAndGetTokenAsync(patientB, UserRole.Patient);
        string tokenRes = await ProvisionUserAndGetTokenAsync(researcher, UserRole.Researcher);

        using HttpClient clientAnon = _factory.CreateClient();
        using HttpClient clientA = CreateClientWithBearer(tokenA);
        using HttpClient clientB = CreateClientWithBearer(tokenB);
        using HttpClient clientRes = CreateClientWithBearer(tokenRes);

        // 3. Seed two depots for patientA (one with assays, one empty)
        HttpResponseMessage createDepotResp = await _client.PostAsJsonAsync("/api/depots/formulations", new CreateDepotFormulationRequest
        {
            PatientId = patientA,
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 18.0,
            DailyErosionRatePercent = 0.55,
            ReleaseKineticsModel = "Korsmeyer-Peppas",
            TargetHorizonDays = 180
        });
        Assert.Equal(HttpStatusCode.Created, createDepotResp.StatusCode);
        DepotFormulationResponse? depotA = await createDepotResp.Content.ReadFromJsonAsync<DepotFormulationResponse>();
        Assert.NotNull(depotA);
        string depotId = depotA.Formulation.DepotId;

        HttpResponseMessage createEmptyDepotResp = await _client.PostAsJsonAsync("/api/depots/formulations", new CreateDepotFormulationRequest
        {
            PatientId = patientA,
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 18.0,
            DailyErosionRatePercent = 0.55,
            ReleaseKineticsModel = "Korsmeyer-Peppas",
            TargetHorizonDays = 180
        });
        DepotFormulationResponse? emptyDepot = await createEmptyDepotResp.Content.ReadFromJsonAsync<DepotFormulationResponse>();
        Assert.NotNull(emptyDepot);
        string emptyDepotId = emptyDepot.Formulation.DepotId;

        RecordDepotAssayRequest validAssay = new()
        {
            DaySinceImplant = 45,
            ObservedE2PmolL = 510.0,
            ObservedTNmolL = 1.1,
            ObservedLhIuL = 0.6,
            ObservedFshIuL = 0.9,
            ObservedShbgNmolL = 76.0,
            ObservedHematocritPct = 41.2,
            ClinicalNotes = "Fasting venous draw at clinic."
        };

        // 4. Validation failures on POST /api/depots/{depotId}/assays -> 400 Bad Request
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/depots/%20/assays", validAssay)).StatusCode);
        using (StringContent nullBody = new("null", System.Text.Encoding.UTF8, "application/json"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync($"/api/depots/{depotId}/assays", nullBody)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"/api/depots/{depotId}/assays", validAssay with { DaySinceImplant = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"/api/depots/{depotId}/assays", validAssay with { DaySinceImplant = 366 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"/api/depots/{depotId}/assays", validAssay with { ObservedE2PmolL = -1.0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"/api/depots/{depotId}/assays", validAssay with { ObservedTNmolL = -1.0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"/api/depots/{depotId}/assays", validAssay with { ObservedLhIuL = -1.0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"/api/depots/{depotId}/assays", validAssay with { ObservedFshIuL = -1.0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"/api/depots/{depotId}/assays", validAssay with { ObservedShbgNmolL = -1.0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"/api/depots/{depotId}/assays", validAssay with { ObservedHematocritPct = -1.0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"/api/depots/{depotId}/assays", validAssay with { ObservedHematocritPct = 101.0 })).StatusCode);

        // 5. Auth, role, subject access, and 404 gating on POST/GET assays, POST calibrate, GET latest
        Assert.Equal(HttpStatusCode.Unauthorized, (await clientAnon.PostAsJsonAsync($"/api/depots/{depotId}/assays", validAssay)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await clientAnon.GetAsync($"/api/depots/{depotId}/assays")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await clientAnon.PostAsync($"/api/depots/{depotId}/calibrate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await clientAnon.GetAsync($"/api/depots/{depotId}/calibrations/latest")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await clientA.PostAsJsonAsync($"/api/depots/{depotId}/assays", validAssay)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientRes.PostAsJsonAsync($"/api/depots/{depotId}/assays", validAssay)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsJsonAsync("/api/depots/depot_nonexistent/assays", validAssay)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/depots/%20/assays")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/depots/depot_nonexistent/assays")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientB.GetAsync($"/api/depots/{depotId}/assays")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientRes.GetAsync($"/api/depots/{depotId}/assays")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/depots/%20/calibrate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientA.PostAsync($"/api/depots/{depotId}/calibrate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync("/api/depots/depot_nonexistent/calibrate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientRes.PostAsync($"/api/depots/{depotId}/calibrate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync($"/api/depots/{emptyDepotId}/calibrate", null)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/depots/%20/calibrations/latest")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/depots/depot_nonexistent/calibrations/latest")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientB.GetAsync($"/api/depots/{depotId}/calibrations/latest")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientRes.GetAsync($"/api/depots/{depotId}/calibrations/latest")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/depots/{emptyDepotId}/calibrations/latest")).StatusCode);

        // 6. Positive Admin record assay with encrypted notes -> 201 Created
        HttpResponseMessage postAssay1 = await _client.PostAsJsonAsync($"/api/depots/{depotId}/assays", validAssay);
        Assert.Equal(HttpStatusCode.Created, postAssay1.StatusCode);
        DepotAssayObservationResponse? obs1 = await postAssay1.Content.ReadFromJsonAsync<DepotAssayObservationResponse>();
        Assert.NotNull(obs1);
        Assert.Equal(45, obs1.DaySinceImplant);
        Assert.Equal("Fasting venous draw at clinic.", obs1.ClinicalNotes);

        // Verify clinical notes are encrypted at rest in repository
        using (IServiceScope verifyScope = _factory.Services.CreateScope())
        {
            IDepotAssayRepository assayRepo = verifyScope.ServiceProvider.GetRequiredService<IDepotAssayRepository>();
            IReadOnlyList<DepotAssayObservationRecord> rawRecords = await assayRepo.GetObservationsByDepotAsync(depotId);
            Assert.Single(rawRecords);
            Assert.NotNull(rawRecords[0].NotesEncrypted);
            Assert.DoesNotContain("Fasting venous draw", rawRecords[0].NotesEncrypted, StringComparison.Ordinal);
        }

        // 7. Grant Researcher access to patientA, record second assay without notes, and run calibration
        using (IServiceScope grantScope = _factory.Services.CreateScope())
        {
            ISubjectAccessService accessService = grantScope.ServiceProvider.GetRequiredService<ISubjectAccessService>();
            await accessService.GrantSubjectAccessAsync("admin", researcher, patientA, SubjectAccessLevel.FullReadAccess, "Calibration cohort");
        }

        HttpResponseMessage postAssay2 = await clientRes.PostAsJsonAsync($"/api/depots/{depotId}/assays", new RecordDepotAssayRequest
        {
            DaySinceImplant = 90,
            ObservedE2PmolL = 475.0,
            ObservedTNmolL = 1.0,
            ObservedLhIuL = 0.5,
            ObservedFshIuL = 0.8,
            ObservedShbgNmolL = 80.0,
            ObservedHematocritPct = 40.8,
            ClinicalNotes = null,
            RecordedAtUtc = DateTimeOffset.UtcNow
        });
        Assert.Equal(HttpStatusCode.Created, postAssay2.StatusCode);
        DepotAssayObservationResponse? obs2 = await postAssay2.Content.ReadFromJsonAsync<DepotAssayObservationResponse>();
        Assert.NotNull(obs2);
        Assert.Null(obs2.ClinicalNotes);

        // 8. GET /api/depots/{depotId}/assays as Admin, Patient self, and granted Researcher -> 200 OK
        foreach (HttpClient authorizedClient in new[] { _client, clientA, clientRes })
        {
            HttpResponseMessage getAssaysResp = await authorizedClient.GetAsync($"/api/depots/{depotId}/assays");
            Assert.Equal(HttpStatusCode.OK, getAssaysResp.StatusCode);
            List<DepotAssayObservationResponse>? list = await getAssaysResp.Content.ReadFromJsonAsync<List<DepotAssayObservationResponse>>();
            Assert.NotNull(list);
            Assert.Equal(2, list.Count);
            Assert.Equal(45, list[0].DaySinceImplant);
            Assert.Equal(90, list[1].DaySinceImplant);
        }

        // 9. POST /api/depots/{depotId}/calibrate as granted Researcher -> 200 OK
        HttpResponseMessage calResp = await clientRes.PostAsync($"/api/depots/{depotId}/calibrate", null);
        Assert.Equal(HttpStatusCode.OK, calResp.StatusCode);
        DepotCalibrationResponse? calibrated = await calResp.Content.ReadFromJsonAsync<DepotCalibrationResponse>();
        Assert.NotNull(calibrated);
        Assert.False(string.IsNullOrWhiteSpace(calibrated.SnapshotId));
        Assert.Equal(depotId, calibrated.DepotId);
        Assert.NotEmpty(calibrated.RecalibratedTrajectory.Points);

        // 10. GET /api/depots/{depotId}/calibrations/latest as Admin, Patient self, and granted Researcher -> 200 OK
        foreach (HttpClient authorizedClient in new[] { _client, clientA, clientRes })
        {
            HttpResponseMessage latestResp = await authorizedClient.GetAsync($"/api/depots/{depotId}/calibrations/latest");
            Assert.Equal(HttpStatusCode.OK, latestResp.StatusCode);
            DepotCalibrationResponse? latest = await latestResp.Content.ReadFromJsonAsync<DepotCalibrationResponse>();
            Assert.NotNull(latest);
            Assert.Equal(calibrated.SnapshotId, latest.SnapshotId);
            Assert.NotEmpty(latest.RecalibratedTrajectory.Points);
        }

        // 11. Verify 21 CFR Part 11 audit ledger recorded both DEPOT_ASSAY_RECORD and DEPOT_CALIBRATION_RUN
        using (IServiceScope auditScope = _factory.Services.CreateScope())
        {
            IComplianceAuditService auditService = auditScope.ServiceProvider.GetRequiredService<IComplianceAuditService>();
            IReadOnlyList<AuditRecord> entries = auditService.GetAuditRecords(200);
            Assert.Contains(entries, e => e.Action == "DEPOT_ASSAY_RECORD" && e.Resource == $"depot:{depotId}");
            Assert.Contains(entries, e => e.Action == "DEPOT_CALIBRATION_RUN" && e.Resource == $"depot:{depotId}");
        }
    }

    /// <summary>
    /// Verifies end-to-end Milestone 35 wet-lab batch fabrication, 5-gate pharmaceutical QC logging,
    /// fail-closed quarantine, NIST FIPS 204 ML-DSA-65 Certificate of Analysis signing, live verification,
    /// RBAC gating, PHI note encryption, and 21 CFR Part 11 audit trails.
    /// </summary>
    [Fact]
    public async Task FabricationLotEndpoints_AdaptiveMintingQcReleaseAndCertificateVerification_EnforceRbacAndPqc()
    {
        // WHAT: Exercises all 5 Milestone 35 REST endpoints plus API DTO examples and mapper guards.
        // WHY:  Milestone 35 (P187): Guarantees 100% line and branch coverage across endpoints, RBAC, and PQC verification.
        // HOW:  Seeds a depot and calibration snapshot, verifies 400/401/403/404 paths, tests quarantine then full
        //       5-gate ML-DSA-65 release, and confirms live certificate verification for Admin, Researcher, and Patient self.
        // 1. Verify API DTO defaults, OpenAPI examples, and mapper null guards
        Assert.True(new CreateFabricationLotRequest().UseLatestCalibration);
        Assert.False(string.IsNullOrWhiteSpace(new CreateFabricationLotRequest().GetExamples().DepotId));
        Assert.Equal(FabricationQcGateType.GelFoldingYield, new RecordFabricationQcRequest().GateType);
        Assert.Equal(92.4, new RecordFabricationQcRequest().GetExamples().MeasuredValue);
        Assert.Equal(string.Empty, new FabricationQcCheckResponse().CheckId);
        Assert.True(new FabricationQcCheckResponse().GetExamples().Passed);
        Assert.Equal(FabricationLotStatus.Draft, new FabricationLotResponse().LotStatus);
        Assert.NotEmpty(new FabricationLotResponse().GetExamples().QcChecks);
        Assert.False(new FabricationLotReleaseResponse().CertificateVerified);
        Assert.True(new FabricationLotReleaseResponse().GetExamples().CertificateVerified);
        Assert.False(new FabricationLotCertificateResponse().SignatureVerified);
        Assert.True(new FabricationLotCertificateResponse().GetExamples().SignatureVerified);

        Assert.Throws<ArgumentNullException>(() => FabricationLotMapper.ToQcResponse(null!, null!));
        Assert.Throws<ArgumentNullException>(() => FabricationLotMapper.ToLotResponse(null!, [], null!));

        using (IServiceScope mapperScope = _factory.Services.CreateScope())
        {
            ProfileDataProtector protector = mapperScope.ServiceProvider.GetRequiredService<ProfileDataProtector>();
            Assert.Throws<ArgumentNullException>(() => FabricationLotMapper.ToQcResponse(new FabricationQcCheckRecord(), null!));
            Assert.Throws<ArgumentNullException>(() => FabricationLotMapper.ToLotResponse(new FabricationLotRecord(), null!, protector));
            Assert.Throws<ArgumentNullException>(() => FabricationLotMapper.ToLotResponse(new FabricationLotRecord(), [], null!));
        }

        // 2. Provision isolated identities
        string patientA = "fab_patient_a";
        string patientB = "fab_patient_b";
        string researcher = "fab_researcher";
        string tokenA = await ProvisionUserAndGetTokenAsync(patientA, UserRole.Patient);
        string tokenB = await ProvisionUserAndGetTokenAsync(patientB, UserRole.Patient);
        string tokenRes = await ProvisionUserAndGetTokenAsync(researcher, UserRole.Researcher);

        using HttpClient clientAnon = _factory.CreateClient();
        using HttpClient clientA = CreateClientWithBearer(tokenA);
        using HttpClient clientB = CreateClientWithBearer(tokenB);
        using HttpClient clientRes = CreateClientWithBearer(tokenRes);

        // 3. Seed parent depot formulation and a longitudinal calibration snapshot for patientA
        HttpResponseMessage depotResp = await _client.PostAsJsonAsync("/api/depots/formulations", new CreateDepotFormulationRequest
        {
            PatientId = patientA,
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 18.0,
            DailyErosionRatePercent = 0.55,
            ReleaseKineticsModel = "Korsmeyer-Peppas",
            TargetHorizonDays = 180
        });
        Assert.Equal(HttpStatusCode.Created, depotResp.StatusCode);
        DepotFormulationResponse? depotA = await depotResp.Content.ReadFromJsonAsync<DepotFormulationResponse>();
        Assert.NotNull(depotA);
        string depotId = depotA.Formulation.DepotId;

        HttpResponseMessage assayResp = await _client.PostAsJsonAsync($"/api/depots/{depotId}/assays", new RecordDepotAssayRequest
        {
            DaySinceImplant = 45,
            ObservedE2PmolL = 490.0,
            ObservedTNmolL = 1.2,
            ObservedLhIuL = 0.7,
            ObservedFshIuL = 0.9,
            ObservedShbgNmolL = 75.0,
            ObservedHematocritPct = 41.0
        });
        Assert.Equal(HttpStatusCode.Created, assayResp.StatusCode);
        HttpResponseMessage calResp = await _client.PostAsync($"/api/depots/{depotId}/calibrate", null);
        Assert.Equal(HttpStatusCode.OK, calResp.StatusCode);
        DepotCalibrationResponse? calData = await calResp.Content.ReadFromJsonAsync<DepotCalibrationResponse>();
        Assert.NotNull(calData);

        CreateFabricationLotRequest validCreateReq = new()
        {
            DepotId = depotId,
            UseLatestCalibration = true,
            CpGMethylationRequired = true
        };

        // 4. Validation & auth checks on POST /api/fabrication/lots
        Assert.Equal(HttpStatusCode.Unauthorized, (await clientAnon.PostAsJsonAsync("/api/fabrication/lots", validCreateReq)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/fabrication/lots", new CreateFabricationLotRequest { DepotId = " " })).StatusCode);
        using (StringContent nullBody = new("null", System.Text.Encoding.UTF8, "application/json"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/fabrication/lots", nullBody)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await clientA.PostAsJsonAsync("/api/fabrication/lots", validCreateReq)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsJsonAsync("/api/fabrication/lots", validCreateReq with { DepotId = "depot_missing" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientRes.PostAsJsonAsync("/api/fabrication/lots", validCreateReq)).StatusCode);

        // 5. Mint adaptive lot as Admin (inherits Milestone 34 calibration snapshot) -> 201 Created
        HttpResponseMessage createLotResp = await _client.PostAsJsonAsync("/api/fabrication/lots", validCreateReq);
        Assert.Equal(HttpStatusCode.Created, createLotResp.StatusCode);
        FabricationLotResponse? createdLot = await createLotResp.Content.ReadFromJsonAsync<FabricationLotResponse>();
        Assert.NotNull(createdLot);
        Assert.Equal(calData.SnapshotId, createdLot.CalibrationSnapshotId);
        Assert.Equal(calData.RecommendedNextMassMg, createdLot.TargetMassMg);
        Assert.Equal(FabricationLotStatus.Draft, createdLot.LotStatus);
        Assert.Equal(64, createdLot.OpentronsProtocolSha256.Length);
        string lotId = createdLot.LotId;

        // 6. Mint a second baseline lot without calibration and with custom Opentrons SHA-256
        HttpResponseMessage customLotResp = await _client.PostAsJsonAsync("/api/fabrication/lots", new CreateFabricationLotRequest
        {
            DepotId = depotId,
            UseLatestCalibration = false,
            CpGMethylationRequired = false,
            OpentronsProtocolSha256 = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789"
        });
        Assert.Equal(HttpStatusCode.Created, customLotResp.StatusCode);
        FabricationLotResponse? customLot = await customLotResp.Content.ReadFromJsonAsync<FabricationLotResponse>();
        Assert.NotNull(customLot);
        Assert.Null(customLot.CalibrationSnapshotId);
        Assert.Equal(18.0, customLot.TargetMassMg);
        Assert.Equal("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", customLot.OpentronsProtocolSha256);

        // 7. Validation, auth, RBAC, and 404 checks across GET lots, POST qc, POST release, and GET certificate
        Assert.Equal(HttpStatusCode.Unauthorized, (await clientAnon.GetAsync($"/api/fabrication/lots/{patientA}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/fabrication/lots/%20")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientB.GetAsync($"/api/fabrication/lots/{patientA}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientRes.GetAsync($"/api/fabrication/lots/{patientA}")).StatusCode);

        RecordFabricationQcRequest sampleQcReq = new()
        {
            GateType = FabricationQcGateType.GelFoldingYield,
            MeasuredValue = 92.5,
            TechnicianNotes = "2% agarose gel clean monomeric band."
        };
        Assert.Equal(HttpStatusCode.Unauthorized, (await clientAnon.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/qc", sampleQcReq)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/fabrication/lots/%20/qc", sampleQcReq)).StatusCode);
        using (StringContent nullQcBody = new("null", System.Text.Encoding.UTF8, "application/json"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync($"/api/fabrication/lots/{lotId}/qc", nullQcBody)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/qc", sampleQcReq with { GateType = (FabricationQcGateType)99 })).StatusCode);
        using (StringContent nanQcBody = new("{\"gateType\":\"GelFoldingYield\",\"measuredValue\":\"NaN\"}", System.Text.Encoding.UTF8, "application/json"))
        {
            HttpResponseMessage nanResp = await _client.PostAsync($"/api/fabrication/lots/{lotId}/qc", nanQcBody);
            Assert.Equal(HttpStatusCode.BadRequest, nanResp.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await clientA.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/qc", sampleQcReq)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsJsonAsync("/api/fabrication/lots/lot_missing/qc", sampleQcReq)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientRes.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/qc", sampleQcReq)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await clientAnon.PostAsync($"/api/fabrication/lots/{lotId}/release", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/fabrication/lots/%20/release", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientA.PostAsync($"/api/fabrication/lots/{lotId}/release", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync("/api/fabrication/lots/lot_missing/release", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientRes.PostAsync($"/api/fabrication/lots/{lotId}/release", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await clientAnon.GetAsync($"/api/fabrication/lots/{lotId}/certificate")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/fabrication/lots/%20/certificate")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/fabrication/lots/lot_missing/certificate")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientB.GetAsync($"/api/fabrication/lots/{lotId}/certificate")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientRes.GetAsync($"/api/fabrication/lots/{lotId}/certificate")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/fabrication/lots/{lotId}/certificate")).StatusCode);

        // 8. Grant Researcher access to patientA
        using (IServiceScope grantScope = _factory.Services.CreateScope())
        {
            ISubjectAccessService accessService = grantScope.ServiceProvider.GetRequiredService<ISubjectAccessService>();
            await accessService.GrantSubjectAccessAsync("admin", researcher, patientA, SubjectAccessLevel.FullReadAccess, "Wet-lab cohort");
        }

        // 9. Record Gate 1 as Admin (with encrypted technician notes) -> transitions Draft to QcPending
        HttpResponseMessage qc1Resp = await _client.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/qc", sampleQcReq);
        Assert.Equal(HttpStatusCode.Created, qc1Resp.StatusCode);
        FabricationQcCheckResponse? qc1 = await qc1Resp.Content.ReadFromJsonAsync<FabricationQcCheckResponse>();
        Assert.NotNull(qc1);
        Assert.True(qc1.Passed);
        Assert.Equal("2% agarose gel clean monomeric band.", qc1.TechnicianNotes);

        using (IServiceScope verifyScope = _factory.Services.CreateScope())
        {
            IFabricationLotRepository lotRepo = verifyScope.ServiceProvider.GetRequiredService<IFabricationLotRepository>();
            IReadOnlyList<FabricationQcCheckRecord> rawChecks = await lotRepo.GetQcChecksByLotAsync(lotId);
            Assert.Single(rawChecks);
            Assert.NotNull(rawChecks[0].NotesEncrypted);
            Assert.DoesNotContain("SYBR", rawChecks[0].NotesEncrypted, StringComparison.Ordinal);
            Assert.DoesNotContain("monomeric", rawChecks[0].NotesEncrypted, StringComparison.Ordinal);
        }

        // 10. Attempt premature release as granted Researcher -> Quarantined (4 missing gates)
        HttpResponseMessage quarantineResp = await clientRes.PostAsync($"/api/fabrication/lots/{lotId}/release", null);
        Assert.Equal(HttpStatusCode.OK, quarantineResp.StatusCode);
        FabricationLotReleaseResponse? quarantined = await quarantineResp.Content.ReadFromJsonAsync<FabricationLotReleaseResponse>();
        Assert.NotNull(quarantined);
        Assert.Equal(FabricationLotStatus.Quarantined, quarantined.Evaluation.ResultStatus);
        Assert.False(quarantined.CertificateVerified);
        Assert.Null(quarantined.Evaluation.Certificate);

        // 11. Record remaining 4 passing gates as granted Researcher (without technician notes)
        RecordFabricationQcRequest[] remainingGates =
        [
            new() { GateType = FabricationQcGateType.EndotoxinSterility, MeasuredValue = 0.14, RecordedAtUtc = DateTimeOffset.UtcNow },
            new() { GateType = FabricationQcGateType.HydrodynamicSizePdi, MeasuredValue = 58.0, SecondaryValue = 0.11 },
            new() { GateType = FabricationQcGateType.PayloadEncapsulation, MeasuredValue = 89.5, SecondaryValue = 1.2 },
            new() { GateType = FabricationQcGateType.CpGMethylation, MeasuredValue = 97.8 }
        ];

        foreach (RecordFabricationQcRequest gateReq in remainingGates)
        {
            HttpResponseMessage gateResp = await clientRes.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/qc", gateReq);
            Assert.Equal(HttpStatusCode.Created, gateResp.StatusCode);
        }

        // 12. Execute release as granted Researcher -> Released + Verified ML-DSA-65 Certificate of Analysis
        HttpResponseMessage releaseResp = await clientRes.PostAsync($"/api/fabrication/lots/{lotId}/release", null);
        Assert.Equal(HttpStatusCode.OK, releaseResp.StatusCode);
        FabricationLotReleaseResponse? released = await releaseResp.Content.ReadFromJsonAsync<FabricationLotReleaseResponse>();
        Assert.NotNull(released);
        Assert.Equal(FabricationLotStatus.Released, released.Evaluation.ResultStatus);
        Assert.True(released.CertificateVerified);
        Assert.NotNull(released.Evaluation.Certificate);
        Assert.Equal("ML-DSA-65", released.Evaluation.Certificate.SignatureAlgorithm);

        // 13. Verify GET lots and GET certificate succeed for Admin, Patient self, and granted Researcher
        foreach (HttpClient authorizedClient in new[] { _client, clientA, clientRes })
        {
            HttpResponseMessage listResp = await authorizedClient.GetAsync($"/api/fabrication/lots/{patientA}");
            Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
            List<FabricationLotResponse>? lots = await listResp.Content.ReadFromJsonAsync<List<FabricationLotResponse>>();
            Assert.NotNull(lots);
            Assert.Equal(2, lots.Count);

            HttpResponseMessage certResp = await authorizedClient.GetAsync($"/api/fabrication/lots/{lotId}/certificate");
            Assert.Equal(HttpStatusCode.OK, certResp.StatusCode);
            FabricationLotCertificateResponse? certDto = await certResp.Content.ReadFromJsonAsync<FabricationLotCertificateResponse>();
            Assert.NotNull(certDto);
            Assert.True(certDto.SignatureVerified);
            Assert.Equal(released.Evaluation.Certificate.PayloadSha256, certDto.Certificate.PayloadSha256);
        }

        // 14. Verify granted Researcher can mint a lot when no calibration exists on a fresh depot
        HttpResponseMessage freshDepotResp = await _client.PostAsJsonAsync("/api/depots/formulations", new CreateDepotFormulationRequest
        {
            PatientId = patientA,
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 16.5,
            DailyErosionRatePercent = 0.50,
            ReleaseKineticsModel = "Korsmeyer-Peppas",
            TargetHorizonDays = 180
        });
        DepotFormulationResponse? freshDepot = await freshDepotResp.Content.ReadFromJsonAsync<DepotFormulationResponse>();
        Assert.NotNull(freshDepot);
        HttpResponseMessage resCreateResp = await clientRes.PostAsJsonAsync("/api/fabrication/lots", new CreateFabricationLotRequest
        {
            DepotId = freshDepot.Formulation.DepotId,
            UseLatestCalibration = true,
            CpGMethylationRequired = true
        });
        Assert.Equal(HttpStatusCode.Created, resCreateResp.StatusCode);
        FabricationLotResponse? resLot = await resCreateResp.Content.ReadFromJsonAsync<FabricationLotResponse>();
        Assert.NotNull(resLot);
        Assert.Null(resLot.CalibrationSnapshotId);
        Assert.Equal(16.5, resLot.TargetMassMg);

        // 15. Verify 21 CFR Part 11 audit ledger recorded all four Milestone 35 event types
        using (IServiceScope auditScope = _factory.Services.CreateScope())
        {
            IComplianceAuditService auditService = auditScope.ServiceProvider.GetRequiredService<IComplianceAuditService>();
            IReadOnlyList<AuditRecord> entries = auditService.GetAuditRecords(300);
            Assert.Contains(entries, e => e.Action == "FABRICATION_LOT_CREATE" && e.Resource == $"fabrication-lot:{lotId}");
            Assert.Contains(entries, e => e.Action == "FABRICATION_QC_RECORD" && e.Resource == $"fabrication-lot:{lotId}");
            Assert.Contains(entries, e => e.Action == "FABRICATION_LOT_QUARANTINE" && e.Resource == $"fabrication-lot:{lotId}");
            Assert.Contains(entries, e => e.Action == "FABRICATION_LOT_RELEASE" && e.Resource == $"fabrication-lot:{lotId}");
        }

        // 16. Milestone 36: Verify DTO examples, portable CoA download, peer/local verification, and permanent revocation
        Assert.Equal(string.Empty, new RevokeFabricationLotRequest().Reason);
        Assert.False(string.IsNullOrWhiteSpace(new RevokeFabricationLotRequest().GetExamples().Reason));
        Assert.Equal(FabricationLotStatus.Revoked, new FabricationLotRevocationResponse().GetExamples().Lot.LotStatus);
        Assert.NotNull(new FabricationLotRevocationResponse().Revocation);

        // Download CoA JSON bundle validation, auth, RBAC, 404, and 200 OK across Admin, Patient self, and granted Researcher
        Assert.Equal(HttpStatusCode.Unauthorized, (await clientAnon.GetAsync($"/api/fabrication/lots/{lotId}/certificate/download")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/fabrication/lots/%20/certificate/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/fabrication/lots/lot_missing/certificate/download")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientB.GetAsync($"/api/fabrication/lots/{lotId}/certificate/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/fabrication/lots/{customLot.LotId}/certificate/download")).StatusCode);

        string ungrantedResearcher = "fab_res_ungranted";
        string tokenUngranted = await ProvisionUserAndGetTokenAsync(ungrantedResearcher, UserRole.Researcher);
        using HttpClient clientUngranted = CreateClientWithBearer(tokenUngranted);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientUngranted.GetAsync($"/api/fabrication/lots/{lotId}/certificate/download")).StatusCode);

        FabricationLotCertificate? downloadedCert = null;
        foreach (HttpClient authorizedClient in new[] { _client, clientA, clientRes })
        {
            HttpResponseMessage dlResp = await authorizedClient.GetAsync($"/api/fabrication/lots/{lotId}/certificate/download");
            Assert.Equal(HttpStatusCode.OK, dlResp.StatusCode);
            downloadedCert = await dlResp.Content.ReadFromJsonAsync<FabricationLotCertificate>();
            Assert.NotNull(downloadedCert);
            Assert.Equal(lotId, downloadedCert.LotId);
        }

        // Verify certificate endpoint validation, RBAC, local trust, and peer key matching loop
        Assert.Equal(HttpStatusCode.Unauthorized, (await clientAnon.PostAsJsonAsync("/api/fabrication/certificates/verify", downloadedCert)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/fabrication/certificates/verify", new FabricationLotCertificate { LotId = " " })).StatusCode);
        using (StringContent nullCertBody = new("null", Encoding.UTF8, "application/json"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/fabrication/certificates/verify", nullCertBody)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await clientA.PostAsJsonAsync("/api/fabrication/certificates/verify", downloadedCert)).StatusCode);

        // Seed two peer keys (one non-matching, one matching a synthetic peer key) to exercise both loop branches
        using (IServiceScope peerScope = _factory.Services.CreateScope())
        {
            IPeerKeyRegistry peerReg = peerScope.ServiceProvider.GetRequiredService<IPeerKeyRegistry>();
            await peerReg.TryRegisterAsync(new PeerKeyRecord
            {
                KeyId = "1111111111111111111111111111111111111111111111111111111111111111",
                Label = "Unmatched Peer Node",
                Algorithm = KeyAlgorithm.MlDsa65,
                PublicKeyBase64 = "DUMMY_PUB_1",
                RegisteredBy = "admin",
                RegisteredAtUtc = DateTimeOffset.UtcNow
            });
            await peerReg.TryRegisterAsync(new PeerKeyRecord
            {
                KeyId = "2222222222222222222222222222222222222222222222222222222222222222",
                Label = "Matched Peer Node",
                Algorithm = KeyAlgorithm.MlDsa65,
                PublicKeyBase64 = "DUMMY_PUB_2",
                RegisteredBy = "admin",
                RegisteredAtUtc = DateTimeOffset.UtcNow
            });
        }

        HttpResponseMessage verifyLocalResp = await clientRes.PostAsJsonAsync("/api/fabrication/certificates/verify", downloadedCert);
        Assert.Equal(HttpStatusCode.OK, verifyLocalResp.StatusCode);
        FabricationCertificateVerificationReport? localReport = await verifyLocalResp.Content.ReadFromJsonAsync<FabricationCertificateVerificationReport>();
        Assert.NotNull(localReport);
        Assert.True(localReport.IsTrusted);
        Assert.Equal(FabricationCertificateTrustSource.LocalNode, localReport.TrustSource);

        // Exercise matched peer loop branch
        HttpResponseMessage verifyPeerMatchResp = await _client.PostAsJsonAsync(
            "/api/fabrication/certificates/verify",
            downloadedCert! with { SignerKeyId = "2222222222222222222222222222222222222222222222222222222222222222" });
        Assert.Equal(HttpStatusCode.OK, verifyPeerMatchResp.StatusCode);

        // Revoke lot validation, RBAC (Admin only), 404, and 200 OK
        RevokeFabricationLotRequest revokeReq = new() { Reason = "Cold-chain excursion during transit." };
        Assert.Equal(HttpStatusCode.Unauthorized, (await clientAnon.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/revoke", revokeReq)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/fabrication/lots/%20/revoke", revokeReq)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/revoke", new RevokeFabricationLotRequest { Reason = " " })).StatusCode);
        using (StringContent nullRevBody = new("null", Encoding.UTF8, "application/json"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync($"/api/fabrication/lots/{lotId}/revoke", nullRevBody)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await clientRes.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/revoke", revokeReq)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientA.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/revoke", revokeReq)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsJsonAsync("/api/fabrication/lots/lot_missing/revoke", revokeReq)).StatusCode);

        HttpResponseMessage revokeResp = await _client.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/revoke", revokeReq);
        Assert.Equal(HttpStatusCode.OK, revokeResp.StatusCode);
        FabricationLotRevocationResponse? revokedDto = await revokeResp.Content.ReadFromJsonAsync<FabricationLotRevocationResponse>();
        Assert.NotNull(revokedDto);
        Assert.Equal(FabricationLotStatus.Revoked, revokedDto.Lot.LotStatus);
        Assert.Equal("Cold-chain excursion during transit.", revokedDto.Revocation.RevocationReason);

        // Re-verifying the previously downloaded CoA now reports IsRevoked = true and IsTrusted = false; download returns 404
        HttpResponseMessage verifyAfterRevoke = await _client.PostAsJsonAsync("/api/fabrication/certificates/verify", downloadedCert);
        Assert.Equal(HttpStatusCode.OK, verifyAfterRevoke.StatusCode);
        FabricationCertificateVerificationReport? revokedReport = await verifyAfterRevoke.Content.ReadFromJsonAsync<FabricationCertificateVerificationReport>();
        Assert.NotNull(revokedReport);
        Assert.True(revokedReport.IsRevoked);
        Assert.False(revokedReport.IsTrusted);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/fabrication/lots/{lotId}/certificate/download")).StatusCode);

        using (IServiceScope auditScope2 = _factory.Services.CreateScope())
        {
            IComplianceAuditService auditService = auditScope2.ServiceProvider.GetRequiredService<IComplianceAuditService>();
            IReadOnlyList<AuditRecord> entries = auditService.GetAuditRecords(400);
            Assert.Contains(entries, e => e.Action == "FABRICATION_LOT_REVOKE" && e.Resource == $"fabrication-lot:{lotId}");
            Assert.Contains(entries, e => e.Action == "FABRICATION_CERT_VERIFY" && e.Resource == $"fabrication-lot:{lotId}");
        }

        // -----------------------------------------------------------------
        // Milestone 38: Federated Peer Lot Revocation Sync & Recall Gossip Ledger
        // -----------------------------------------------------------------
        // 1. Export signed ML-DSA-65 revocation feed: 401 anon, 403 patient, 200 researcher/admin
        Assert.Equal(HttpStatusCode.Unauthorized, (await clientAnon.GetAsync("/api/fabrication/revocations/feed")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientA.GetAsync("/api/fabrication/revocations/feed")).StatusCode);

        HttpResponseMessage feedResp = await clientRes.GetAsync("/api/fabrication/revocations/feed");
        Assert.Equal(HttpStatusCode.OK, feedResp.StatusCode);
        SignedLotRevocationFeed? exportedFeed = await feedResp.Content.ReadFromJsonAsync<SignedLotRevocationFeed>();
        Assert.NotNull(exportedFeed);
        Assert.Contains(exportedFeed.Entries, e => e.LotId == lotId);

        // 2. Mint & release a second lot (NOT locally revoked) to verify federated recall enforcement
        HttpResponseMessage mintLotBResp = await _client.PostAsJsonAsync("/api/fabrication/lots", new CreateFabricationLotRequest
        {
            DepotId = depotId,
            UseLatestCalibration = false,
            CpGMethylationRequired = false
        });
        Assert.Equal(HttpStatusCode.Created, mintLotBResp.StatusCode);
        string lotBId = (await mintLotBResp.Content.ReadFromJsonAsync<FabricationLotResponse>())!.LotId;

        await _client.PostAsJsonAsync($"/api/fabrication/lots/{lotBId}/qc", new RecordFabricationQcRequest { GateType = FabricationQcGateType.GelFoldingYield, MeasuredValue = 93.0 });
        await _client.PostAsJsonAsync($"/api/fabrication/lots/{lotBId}/qc", new RecordFabricationQcRequest { GateType = FabricationQcGateType.EndotoxinSterility, MeasuredValue = 0.11 });
        await _client.PostAsJsonAsync($"/api/fabrication/lots/{lotBId}/qc", new RecordFabricationQcRequest { GateType = FabricationQcGateType.HydrodynamicSizePdi, MeasuredValue = 58.0, SecondaryValue = 0.11 });
        await _client.PostAsJsonAsync($"/api/fabrication/lots/{lotBId}/qc", new RecordFabricationQcRequest { GateType = FabricationQcGateType.PayloadEncapsulation, MeasuredValue = 90.0, SecondaryValue = 1.0 });
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/api/fabrication/lots/{lotBId}/release", null)).StatusCode);

        FabricationLotCertificate certLotB = (await (await _client.GetAsync($"/api/fabrication/lots/{lotBId}/certificate/download")).Content.ReadFromJsonAsync<FabricationLotCertificate>())!;

        // 3. Import revocation feed guards: 401 anon, 400 null body, 403 patient, 422 tampered/untrusted
        Assert.Equal(HttpStatusCode.Unauthorized, (await clientAnon.PostAsJsonAsync("/api/fabrication/revocations/import", exportedFeed)).StatusCode);
        using (StringContent nullFeedBody = new("null", Encoding.UTF8, "application/json"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/fabrication/revocations/import", nullFeedBody)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await clientA.PostAsJsonAsync("/api/fabrication/revocations/import", exportedFeed)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.PostAsJsonAsync("/api/fabrication/revocations/import", exportedFeed with { PayloadSha256 = "0000" })).StatusCode);

        // Match peer key loop branch on untrusted peer
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.PostAsJsonAsync("/api/fabrication/revocations/import", exportedFeed with { SignerKeyId = "2222222222222222222222222222222222222222222222222222222222222222" })).StatusCode);

        // 4. Sign a feed containing lotB + an intentionally blank entry (to cover skip branch) and import via Researcher
        SignedLotRevocationFeed feedForLotB;
        using (IServiceScope signScope = _factory.Services.CreateScope())
        {
            IFabricationLotService lotSvc = signScope.ServiceProvider.GetRequiredService<IFabricationLotService>();
            feedForLotB = lotSvc.BuildSignedRevocationFeed(
            [
                new FabricationLotRevocationRecord
                {
                    LotId = " ",
                    PatientId = "p",
                    RevokedBy = "admin",
                    RevocationReason = "skip blank lotId"
                },
                new FabricationLotRevocationRecord
                {
                    LotId = "skip_blank_actor",
                    PatientId = "p",
                    RevokedBy = " ",
                    RevocationReason = "skip blank actor"
                },
                new FabricationLotRevocationRecord
                {
                    LotId = "skip_blank_reason",
                    PatientId = "p",
                    RevokedBy = "admin",
                    RevocationReason = " "
                },
                new FabricationLotRevocationRecord
                {
                    LotId = lotBId,
                    PatientId = "p",
                    PayloadSha256 = certLotB.PayloadSha256,
                    RevokedBy = "peer_director",
                    RevocationReason = "Upstream federated courier recall"
                }
            ]);
        }

        HttpResponseMessage importResp = await clientRes.PostAsJsonAsync("/api/fabrication/revocations/import", feedForLotB);
        Assert.Equal(HttpStatusCode.OK, importResp.StatusCode);
        ImportRevocationFeedResponse? importDto = await importResp.Content.ReadFromJsonAsync<ImportRevocationFeedResponse>();
        Assert.NotNull(importDto);
        Assert.Equal(1, importDto.ImportedCount);
        Assert.Equal(FabricationCertificateTrustSource.LocalNode, importDto.TrustSource);

        // 5. List federated revocations: 401 anon, 403 patient, 200 researcher/admin
        Assert.Equal(HttpStatusCode.Unauthorized, (await clientAnon.GetAsync("/api/fabrication/revocations/federated")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientA.GetAsync("/api/fabrication/revocations/federated")).StatusCode);
        HttpResponseMessage fedListResp = await clientRes.GetAsync("/api/fabrication/revocations/federated");
        Assert.Equal(HttpStatusCode.OK, fedListResp.StatusCode);
        List<FederatedLotRevocationRecord>? fedRecalls = await fedListResp.Content.ReadFromJsonAsync<List<FederatedLotRevocationRecord>>();
        Assert.NotNull(fedRecalls);
        Assert.Contains(fedRecalls, r => r.LotId == lotBId);

        // 6. Verifying lotB's valid CoA now returns IsRevoked = true, IsTrusted = false via federated recall ledger!
        HttpResponseMessage verifyLotBResp = await _client.PostAsJsonAsync("/api/fabrication/certificates/verify", certLotB);
        Assert.Equal(HttpStatusCode.OK, verifyLotBResp.StatusCode);
        FabricationCertificateVerificationReport? lotBReport = await verifyLotBResp.Content.ReadFromJsonAsync<FabricationCertificateVerificationReport>();
        Assert.NotNull(lotBReport);
        Assert.True(lotBReport.SignatureValid);
        Assert.True(lotBReport.IsRevoked);
        Assert.False(lotBReport.IsTrusted);
        Assert.Equal("Upstream federated courier recall", lotBReport.RevocationReason);

        using (IServiceScope auditScope3 = _factory.Services.CreateScope())
        {
            IComplianceAuditService auditService = auditScope3.ServiceProvider.GetRequiredService<IComplianceAuditService>();
            IReadOnlyList<AuditRecord> entries = auditService.GetAuditRecords(400);
            Assert.Contains(entries, e => e.Action == "FABRICATION_REVOCATION_FEED_IMPORT");
        }
    }

    /// <summary>
    /// End-to-end integration test for Milestone 39 (P207): verifies RBAC, RFC 7807 validation, SSRF rejection,
    /// live loopback ML-DSA-65 pull-sync ingestion, 422 failure mapping, sweep aggregation, deletion, and audit trails
    /// across all 5 <c>/api/federation/peers/revocation-sync*</c> Minimal API endpoints.
    /// </summary>
    [Fact]
    public async Task FederatedPeerRevocationSyncEndpoints_FullLifecycleAndRbac_WorkEndToEnd()
    {
        string tokenRes = await ProvisionUserAndGetTokenAsync("m39_researcher", UserRole.Researcher);
        string tokenPat = await ProvisionUserAndGetTokenAsync("m39_patient", UserRole.Patient);

        using HttpClient clientAnon = _factory.CreateClient();
        HttpClient clientAdmin = _client;
        using HttpClient clientRes = CreateClientWithBearer(tokenRes);
        using HttpClient clientPat = CreateClientWithBearer(tokenPat);

        // 1. Auth & Role gates: 401 anon, 403 patient on all 5, 403 researcher on POST upsert & DELETE
        Assert.Equal(HttpStatusCode.Unauthorized, (await clientAnon.GetAsync("/api/federation/peers/revocation-sync")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientPat.GetAsync("/api/federation/peers/revocation-sync")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientPat.PostAsJsonAsync("/api/federation/peers/revocation-sync", new UpsertFederatedPeerSyncEndpointRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientRes.PostAsJsonAsync("/api/federation/peers/revocation-sync", new UpsertFederatedPeerSyncEndpointRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientRes.DeleteAsync("/api/federation/peers/revocation-sync/peer-1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientPat.PostAsync("/api/federation/peers/revocation-sync/peer-1/run", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientPat.PostAsync("/api/federation/peers/revocation-sync/run-all", null)).StatusCode);

        // 2. Null body & invalid payload -> 400; whitespace peerId -> 400; missing peerId -> 404
        using StringContent nullBody = new("null", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await clientAdmin.PostAsync("/api/federation/peers/revocation-sync", nullBody)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await clientAdmin.PostAsJsonAsync("/api/federation/peers/revocation-sync", new UpsertFederatedPeerSyncEndpointRequest
        {
            PeerId = "ssrf-peer",
            DisplayName = "SSRF Blocked",
            BaseUrl = "http://169.254.169.254",
            PinnedKeyId = "missing",
            SyncIntervalMinutes = 15,
            AllowLoopbackHttp = false
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await clientAdmin.DeleteAsync("/api/federation/peers/revocation-sync/%20")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientAdmin.DeleteAsync("/api/federation/peers/revocation-sync/unknown-peer")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await clientRes.PostAsync("/api/federation/peers/revocation-sync/%20/run", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientRes.PostAsync("/api/federation/peers/revocation-sync/unknown-peer/run", null)).StatusCode);

        // 3. Spin up an isolated ML-DSA-65 peer keystore and loopback TCP HTTP server serving a real signed revocation feed
        string peerKeyDir = Path.Combine(Path.GetTempPath(), $"nw_m39_peer_{Guid.NewGuid():N}");
        Directory.CreateDirectory(peerKeyDir);
        using SubjectKeyTestHost peerHost = new(peerKeyDir);
        FabricationLotService peerLotService = new(peerHost.KeyManagement, peerHost.CryptoProvider);
        SignedLotRevocationFeed signedFeed = peerLotService.BuildSignedRevocationFeed(
            [
                new FabricationLotRevocationRecord
                {
                    LotId = "LOT-M39-PULL-001",
                    PatientId = "remote-pat",
                    PayloadSha256 = new string('9', 64),
                    RevokedBy = "peer_director",
                    RevocationReason = "Automated pull-sync cold-chain excursion recall",
                    RevokedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
                }
            ]);

        using (IServiceScope regScope = _factory.Services.CreateScope())
        {
            IPeerKeyRegistry peerKeyRegistry = regScope.ServiceProvider.GetRequiredService<IPeerKeyRegistry>();
            await peerKeyRegistry.TryRegisterAsync(new PeerKeyRecord
            {
                KeyId = signedFeed.SignerKeyId,
                Label = "Loopback Peer Lab",
                Algorithm = KeyAlgorithm.MlDsa65,
                PublicKeyBase64 = signedFeed.SignerPublicKeyBase64,
                RegisteredBy = "admin",
                RegisteredAtUtc = DateTimeOffset.UtcNow
            });
        }

        string feedJson = JsonSerializer.Serialize(signedFeed);
        int responseMode = 0; // 0 = 200 OK signed feed, 1 = 503 failure
        using System.Net.Sockets.TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using CancellationTokenSource serverCts = new();
        Task serverTask = Task.Run(async () =>
        {
            while (!serverCts.IsCancellationRequested)
            {
                try
                {
                    using System.Net.Sockets.TcpClient tcp = await listener.AcceptTcpClientAsync(serverCts.Token);
                    await using System.Net.Sockets.NetworkStream netStream = tcp.GetStream();
                    byte[] reqBuf = new byte[2048];
                    _ = await netStream.ReadAsync(reqBuf, serverCts.Token);
                    byte[] bodyBytes = responseMode == 0
                        ? System.Text.Encoding.UTF8.GetBytes(feedJson)
                        : System.Text.Encoding.UTF8.GetBytes("{\"error\":\"down\"}");
                    string statusLine = responseMode == 0 ? "HTTP/1.1 200 OK" : "HTTP/1.1 503 Service Unavailable";
                    string header = $"{statusLine}\r\nContent-Type: application/json\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
                    byte[] headerBytes = System.Text.Encoding.ASCII.GetBytes(header);
                    await netStream.WriteAsync(headerBytes, serverCts.Token);
                    await netStream.WriteAsync(bodyBytes, serverCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });

        try
        {
            // 4. Administrator upserts loopback subscription -> 200 OK
            HttpResponseMessage upsertResp = await clientAdmin.PostAsJsonAsync("/api/federation/peers/revocation-sync", new UpsertFederatedPeerSyncEndpointRequest
            {
                PeerId = "peer-loopback-01",
                DisplayName = "Loopback Sovereign Lab",
                BaseUrl = $"http://127.0.0.1:{port}",
                PinnedKeyId = signedFeed.SignerKeyId,
                Enabled = true,
                SyncIntervalMinutes = 15,
                AllowLoopbackHttp = true
            });
            Assert.Equal(HttpStatusCode.OK, upsertResp.StatusCode);
            FederatedPeerSyncEndpointRecord? saved = await upsertResp.Content.ReadFromJsonAsync<FederatedPeerSyncEndpointRecord>();
            Assert.NotNull(saved);
            Assert.Equal("peer-loopback-01", saved.PeerId);

            // 5. Researcher lists endpoints -> 200 OK
            HttpResponseMessage listResp = await clientRes.GetAsync("/api/federation/peers/revocation-sync");
            Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
            List<FederatedPeerSyncEndpointRecord>? listed = await listResp.Content.ReadFromJsonAsync<List<FederatedPeerSyncEndpointRecord>>();
            Assert.NotNull(listed);
            Assert.Contains(listed, e => e.PeerId == "peer-loopback-01");

            // 6. Researcher triggers manual /run -> 200 OK, imports 1 new recall tombstone
            HttpResponseMessage runResp = await clientRes.PostAsync("/api/federation/peers/revocation-sync/peer-loopback-01/run", null);
            Assert.Equal(HttpStatusCode.OK, runResp.StatusCode);
            FederatedPeerSyncRunResult? runResult = await runResp.Content.ReadFromJsonAsync<FederatedPeerSyncRunResult>();
            Assert.NotNull(runResult);
            Assert.True(runResult.Succeeded);
            Assert.Equal(1, runResult.ImportedCount);

            // 7. Researcher triggers /run-all -> 200 OK (idempotent 0 new recalls)
            HttpResponseMessage sweepResp = await clientRes.PostAsync("/api/federation/peers/revocation-sync/run-all", null);
            Assert.Equal(HttpStatusCode.OK, sweepResp.StatusCode);
            FederatedPeerSyncSweepResult? sweepResult = await sweepResp.Content.ReadFromJsonAsync<FederatedPeerSyncSweepResult>();
            Assert.NotNull(sweepResult);
            Assert.True(sweepResult.PeersSucceeded >= 1);

            // 8. Upstream returns 503 -> /run returns 422 Unprocessable Entity
            responseMode = 1;
            HttpResponseMessage failRunResp = await clientRes.PostAsync("/api/federation/peers/revocation-sync/peer-loopback-01/run", null);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, failRunResp.StatusCode);

            // 9. Administrator deletes subscription -> 204 No Content
            Assert.Equal(HttpStatusCode.NoContent, (await clientAdmin.DeleteAsync("/api/federation/peers/revocation-sync/peer-loopback-01")).StatusCode);

            using (IServiceScope auditScope = _factory.Services.CreateScope())
            {
                IComplianceAuditService audit = auditScope.ServiceProvider.GetRequiredService<IComplianceAuditService>();
                IReadOnlyList<AuditRecord> entries = audit.GetAuditRecords(500);
                Assert.Contains(entries, e => e.Action == "FABRICATION_PEER_SUBSCRIPTION_UPSERT");
                Assert.Contains(entries, e => e.Action == "FABRICATION_PEER_SYNC_SUCCESS");
                Assert.Contains(entries, e => e.Action == "FABRICATION_PEER_SYNC_FAILURE");
                Assert.Contains(entries, e => e.Action == "FABRICATION_PEER_SUBSCRIPTION_DELETE");
            }
        }
        finally
        {
            serverCts.Cancel();
            listener.Stop();
            await serverTask;
            if (Directory.Exists(peerKeyDir))
            {
                try
                {
                    Directory.Delete(peerKeyDir, recursive: true);
                }
                catch
                {
                    // Best-effort temp cleanup.
                }
            }
        }
    }

    /// <summary>
    /// End-to-end integration test for Milestone 40 (P212): verifies all 4 cold-chain stability and custody ledger
    /// Minimal API routes, role/subject-access gating, fail-closed <c>PatientAdministration</c> blocking (HTTP 422),
    /// fail-closed <c>POST /api/fabrication/certificates/verify</c> cold-chain override, and 21 CFR Part 11 audit trails.
    /// </summary>
    [Fact]
    public async Task ColdChainStabilityAndCustodyEndpoints_FullLifecycleRbacAndFailClosedGates_WorkEndToEnd()
    {
        string patSelf = "m40_patient_self";
        string patOther = "m40_patient_other";
        string researcher = "m40_researcher";

        string tokSelf = await ProvisionUserAndGetTokenAsync(patSelf, UserRole.Patient);
        string tokOther = await ProvisionUserAndGetTokenAsync(patOther, UserRole.Patient);
        string tokRes = await ProvisionUserAndGetTokenAsync(researcher, UserRole.Researcher);

        using HttpClient clientAnon = _factory.CreateClient();
        using HttpClient clientSelf = CreateClientWithBearer(tokSelf);
        using HttpClient clientOther = CreateClientWithBearer(tokOther);
        using HttpClient clientRes = CreateClientWithBearer(tokRes);

        // 1. Seed depot formulation, mint lot, pass all 5 QC gates, and release with ML-DSA-65 CoA
        HttpResponseMessage depResp = await _client.PostAsJsonAsync("/api/depots/formulations", new CreateDepotFormulationRequest
        {
            PatientId = patSelf,
            Regimen = TherapeuticRegimen.MaleToFemale,
            PolymerMatrixType = "PLGA-PEG-PLGA",
            InitialNanocarrierMassMg = 16.0,
            DailyErosionRatePercent = 0.55,
            ReleaseKineticsModel = "Korsmeyer-Peppas",
            TargetHorizonDays = 180
        });
        Assert.Equal(HttpStatusCode.Created, depResp.StatusCode);
        DepotFormulationResponse? dep = await depResp.Content.ReadFromJsonAsync<DepotFormulationResponse>();
        Assert.NotNull(dep);

        HttpResponseMessage lotResp = await _client.PostAsJsonAsync("/api/fabrication/lots", new CreateFabricationLotRequest
        {
            DepotId = dep.Formulation.DepotId,
            UseLatestCalibration = false,
            CpGMethylationRequired = true,
            OpentronsProtocolSha256 = new string('7', 64)
        });
        Assert.Equal(HttpStatusCode.Created, lotResp.StatusCode);
        FabricationLotResponse? lot = await lotResp.Content.ReadFromJsonAsync<FabricationLotResponse>();
        Assert.NotNull(lot);
        string lotId = lot.LotId;

        await _client.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/qc", new RecordFabricationQcRequest { GateType = FabricationQcGateType.GelFoldingYield, MeasuredValue = 93.0 });
        await _client.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/qc", new RecordFabricationQcRequest { GateType = FabricationQcGateType.EndotoxinSterility, MeasuredValue = 0.10 });
        await _client.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/qc", new RecordFabricationQcRequest { GateType = FabricationQcGateType.HydrodynamicSizePdi, MeasuredValue = 60.0, SecondaryValue = 0.12 });
        await _client.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/qc", new RecordFabricationQcRequest { GateType = FabricationQcGateType.PayloadEncapsulation, MeasuredValue = 89.0, SecondaryValue = 1.0 });
        await _client.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/qc", new RecordFabricationQcRequest { GateType = FabricationQcGateType.CpGMethylation, MeasuredValue = 98.0 });
        HttpResponseMessage relResp = await _client.PostAsync($"/api/fabrication/lots/{lotId}/release", null);
        Assert.Equal(HttpStatusCode.OK, relResp.StatusCode);

        HttpResponseMessage certResp = await _client.GetAsync($"/api/fabrication/lots/{lotId}/certificate");
        Assert.Equal(HttpStatusCode.OK, certResp.StatusCode);
        FabricationLotCertificateResponse? certDto = await certResp.Content.ReadFromJsonAsync<FabricationLotCertificateResponse>();
        Assert.NotNull(certDto?.Certificate);

        // 2. Auth, 400, 403, and 404 checks across all 4 endpoints
        RecordThermalExcursionRequest validExc = new()
        {
            TemperatureCelsius = 4.0,
            DurationHours = 2.0,
            StorageTier = ThermalStorageTier.Refrigerated2To8,
            SensorId = "PROBE-M40-01",
            Notes = "Routine cold-box transfer"
        };
        RecordCustodyHandoffRequest validHandoff = new()
        {
            Stage = CustodyStage.WetLabRelease,
            FromActor = "wetlab_admin",
            ToActor = "courier_01",
            FacilityCode = "ZRH-LAB-01",
            TemperatureCelsius = -19.5
        };

        Assert.Equal(HttpStatusCode.Unauthorized, (await clientAnon.GetAsync($"/api/fabrication/lots/{lotId}/stability")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/fabrication/lots/%20/stability")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/fabrication/lots/%20/custody-handoffs")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/fabrication/lots/%20/thermal-excursions", validExc)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/fabrication/lots/%20/custody-handoffs", validHandoff)).StatusCode);
        using (StringContent nullBody = new("null", Encoding.UTF8, "application/json"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync($"/api/fabrication/lots/{lotId}/thermal-excursions", nullBody)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync($"/api/fabrication/lots/{lotId}/custody-handoffs", nullBody)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/fabrication/lots/missing_lot_404/stability")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/fabrication/lots/missing_lot_404/custody-handoffs")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsJsonAsync("/api/fabrication/lots/missing_lot_404/thermal-excursions", validExc)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsJsonAsync("/api/fabrication/lots/missing_lot_404/custody-handoffs", validHandoff)).StatusCode);

        // Patient self blocked on mutations, allowed on reads; other patient & ungranted researcher blocked on all
        Assert.Equal(HttpStatusCode.Forbidden, (await clientSelf.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/thermal-excursions", validExc)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientSelf.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/custody-handoffs", validHandoff)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientOther.GetAsync($"/api/fabrication/lots/{lotId}/stability")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientOther.GetAsync($"/api/fabrication/lots/{lotId}/custody-handoffs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientRes.GetAsync($"/api/fabrication/lots/{lotId}/stability")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientRes.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/thermal-excursions", validExc)).StatusCode);

        // Validation ArgumentException -> 400 Bad Request
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/thermal-excursions", validExc with { DurationHours = -5.0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/custody-handoffs", validHandoff with { FromActor = "" })).StatusCode);

        // Grant Researcher access to patSelf
        using (IServiceScope grantScope = _factory.Services.CreateScope())
        {
            ISubjectAccessService accessSvc = grantScope.ServiceProvider.GetRequiredService<ISubjectAccessService>();
            await accessSvc.GrantSubjectAccessAsync("admin", researcher, patSelf, SubjectAccessLevel.FullReadAccess, "M40 clinical monitoring");
        }

        // 3. Researcher logs optimal thermal excursion (201) and custody handoff (201); Patient self reads stability & custody ledger (200)
        HttpResponseMessage excResp = await clientRes.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/thermal-excursions", validExc);
        Assert.Equal(HttpStatusCode.Created, excResp.StatusCode);
        ThermalExcursionResponse? excDto = await excResp.Content.ReadFromJsonAsync<ThermalExcursionResponse>();
        Assert.NotNull(excDto);
        Assert.True(excDto.StabilityAfterExcursion.IsSafeForImplantation);

        HttpResponseMessage hndResp = await clientRes.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/custody-handoffs", validHandoff);
        Assert.Equal(HttpStatusCode.Created, hndResp.StatusCode);

        HttpResponseMessage selfStabResp = await clientSelf.GetAsync($"/api/fabrication/lots/{lotId}/stability");
        Assert.Equal(HttpStatusCode.OK, selfStabResp.StatusCode);
        LotStabilityReport? selfStab = await selfStabResp.Content.ReadFromJsonAsync<LotStabilityReport>();
        Assert.NotNull(selfStab);
        Assert.True(selfStab.IsSafeForImplantation);

        HttpResponseMessage selfLedResp = await clientSelf.GetAsync($"/api/fabrication/lots/{lotId}/custody-handoffs");
        Assert.Equal(HttpStatusCode.OK, selfLedResp.StatusCode);
        CustodyHandoffLedgerResponse? selfLedger = await selfLedResp.Content.ReadFromJsonAsync<CustodyHandoffLedgerResponse>();
        Assert.NotNull(selfLedger);
        Assert.True(selfLedger.CustodyChainValid);
        Assert.Single(selfLedger.Handoffs);

        // 4. Verify CoA passes while cold chain is safe
        HttpResponseMessage verifySafeResp = await _client.PostAsJsonAsync("/api/fabrication/certificates/verify", certDto.Certificate);
        Assert.Equal(HttpStatusCode.OK, verifySafeResp.StatusCode);
        FabricationCertificateVerificationReport? verifySafe = await verifySafeResp.Content.ReadFromJsonAsync<FabricationCertificateVerificationReport>();
        Assert.True(verifySafe!.IsTrusted);
        Assert.True(verifySafe.IsSafeForImplantation);

        // 5. Log catastrophic 48°C excursion -> CoA verify flips fail-closed to IsTrusted = false AND PatientAdministration returns 422!
        HttpResponseMessage catResp = await _client.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/thermal-excursions", new RecordThermalExcursionRequest
        {
            TemperatureCelsius = 48.0,
            DurationHours = 1.0,
            StorageTier = ThermalStorageTier.ThermalExcursion,
            SensorId = "PROBE-M40-MELT"
        });
        Assert.Equal(HttpStatusCode.Created, catResp.StatusCode);

        HttpResponseMessage verifyCompromisedResp = await _client.PostAsJsonAsync("/api/fabrication/certificates/verify", certDto.Certificate);
        Assert.Equal(HttpStatusCode.OK, verifyCompromisedResp.StatusCode);
        FabricationCertificateVerificationReport? verifyCompromised = await verifyCompromisedResp.Content.ReadFromJsonAsync<FabricationCertificateVerificationReport>();
        Assert.True(verifyCompromised!.SignatureValid);
        Assert.False(verifyCompromised.IsTrusted);
        Assert.Equal(ThermalStabilityStatus.CatastrophicDenaturation, verifyCompromised.ColdChainStatus);
        Assert.False(verifyCompromised.IsSafeForImplantation);

        HttpResponseMessage blockedAdminResp = await clientRes.PostAsJsonAsync($"/api/fabrication/lots/{lotId}/custody-handoffs", new RecordCustodyHandoffRequest
        {
            Stage = CustodyStage.PatientAdministration,
            FromActor = "clinic_rn",
            ToActor = patSelf,
            FacilityCode = "ZRH-CLINIC-01",
            TemperatureCelsius = 4.0
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blockedAdminResp.StatusCode);

        using (IServiceScope auditScope = _factory.Services.CreateScope())
        {
            IComplianceAuditService audit = auditScope.ServiceProvider.GetRequiredService<IComplianceAuditService>();
            IReadOnlyList<AuditRecord> entries = audit.GetAuditRecords(500);
            Assert.Contains(entries, e => e.Action == "FABRICATION_THERMAL_LOG_RECORD");
            Assert.Contains(entries, e => e.Action == "FABRICATION_CUSTODY_HANDOFF_RECORD");
        }
    }
}
