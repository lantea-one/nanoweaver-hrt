using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WeaverLammpsHrt.TrajectoryParser.Endocrine;
using WeaverLammpsHrt.TrajectoryParser.Models;
using Xunit;

namespace WeaverLammpsHrt.TrajectoryParser.Tests;

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
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "dotnet-weaver-lammps-hrt.sln")))
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
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "dotnet-weaver-lammps-hrt.sln")))
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
}
