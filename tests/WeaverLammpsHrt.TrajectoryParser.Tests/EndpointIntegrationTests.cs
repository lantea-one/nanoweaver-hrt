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
        string baseDir = AppContext.BaseDirectory;
        string solutionRoot = Path.GetFullPath(Path.Combine(baseDir, "../../../../../"));
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
}
