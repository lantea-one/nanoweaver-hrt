using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WeaverLammpsHrt.TrajectoryParser.Configuration;
using WeaverLammpsHrt.TrajectoryParser.Endocrine;
using WeaverLammpsHrt.TrajectoryParser.Fabrication;
using WeaverLammpsHrt.TrajectoryParser.Services.Stack;
using Xunit;

namespace WeaverLammpsHrt.TrajectoryParser.Tests;

/// <summary>
/// Comprehensive test suite verifying the Blazor web UI backend, database configuration precedence
/// ("Whatever is in the database wins, stack environment variables do nothing if configuration exists in the database"),
/// single-use collaborative share key lifecycles, and DNA origami fabrication synthesis.
/// </summary>
public sealed class WebUiAndConfigurationTests : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>
    /// Web application factory fixture.
    /// </summary>
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>
    /// HTTP client connected to the in-memory test host.
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// Path to a temporary test directory for isolated configuration tests.
    /// </summary>
    private readonly string _testTempDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebUiAndConfigurationTests"/> class.
    /// </summary>
    /// <param name="factory">Test web application factory fixture.</param>
    public WebUiAndConfigurationTests(WebApplicationFactory<Program> factory)
    {
        string baseDir = AppContext.BaseDirectory;
        string solutionRoot = Path.GetFullPath(Path.Combine(baseDir, "../../../../../"));
        string dataDir = Path.Combine(solutionRoot, "simulations", "shared-data");
        string exportDir = Path.Combine(solutionRoot, "simulations", "exports");
        string parquetDir = Path.Combine(solutionRoot, "simulations", "parquet");

        TestFixturesHelper.EnsureTestFixtures(dataDir);

        _testTempDir = Path.Combine(Path.GetTempPath(), "weaver_ui_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testTempDir);

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("TRAJECTORY_INCOMING_DIR", dataDir);
            builder.UseSetting("TRAJECTORY_EXPORT_DIR", exportDir);
            builder.UseSetting("TRAJECTORY_PARQUET_DIR", parquetDir);
        });

        _client = _factory.CreateClient();
    }

    /// <summary>
    /// Verifies that configuration precedence strictly enforces: Database Override > Environment Variable > Default Fallback.
    /// </summary>
    [Fact]
    public async Task ConfigurationStore_EnforcesDatabaseWinsOverEnvironmentVariable()
    {
        // WHAT: Creates an isolated SystemConfigurationStore with an environment variable set.
        // WHY:  Proves the rule: "Whatever is in the database wins, stack environment variables do nothing if configuration exists in the database."
        // HOW:  1. Verify environment value is active. 2. Write database override. 3. Confirm database value wins. 4. Revert.
        string testKey = "Simulation:EndpointUrl";
        string envVal = "http://cluster-env.internal:9999";
        string dbOverrideVal = "http://database-wins.internal:1234";

        Environment.SetEnvironmentVariable("SIMULATION_ENDPOINT_URL", envVal);

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _testTempDir,
                ["POSTGRES_CONNECTION_STRING"] = "Host=nonexistent_test_host;Timeout=1"
            })
            .Build();

        using SystemConfigurationStore store = new(config);
        await store.InitializeAsync();

        try
        {
            // Step 1: Without database override, the environment variable should be active
            string initialVal = await store.GetValueAsync(testKey);
            Assert.Equal(envVal, initialVal);

            // Step 2: Persist database override
            await store.SetDatabaseOverrideAsync(testKey, dbOverrideVal, "Test Override");

            // Step 3: Database override MUST win!
            string activeVal = await store.GetValueAsync(testKey);
            Assert.Equal(dbOverrideVal, activeVal);

            IReadOnlyList<ConfigurationItem> allConfigs = await store.GetAllConfigurationsAsync();
            ConfigurationItem? item = allConfigs.FirstOrDefault(c => c.Key == testKey);
            Assert.NotNull(item);
            Assert.Equal(ConfigurationSource.DatabaseOverride, item.Source);
            Assert.Equal(dbOverrideVal, item.Value);

            // Step 4: Reverting override restores environment variable precedence
            await store.RevertToDefaultAsync(testKey);
            string restoredVal = await store.GetValueAsync(testKey);
            Assert.Equal(envVal, restoredVal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SIMULATION_ENDPOINT_URL", null);
        }
    }

    /// <summary>
    /// Verifies the full single-use share key lifecycle: generation, single-use consumption,
    /// rejection of reuse, and rejection of expired tokens.
    /// </summary>
    [Fact]
    public async Task SingleUseShareKey_CompleteLifecycle_GuaranteesSingleUseConsumption()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _testTempDir,
                ["POSTGRES_CONNECTION_STRING"] = "Host=nonexistent_test_host;Timeout=1"
            })
            .Build();

        using SystemConfigurationStore store = new(config);
        await store.InitializeAsync();

        // 1. Generate single-use key
        ShareKeyRecord key = await store.CreateShareKeyAsync("Remote Laptop Test", TimeSpan.FromHours(2), "FullCollaborator");
        Assert.NotNull(key.KeyToken);
        Assert.StartsWith("nanoweaver_hrt_", key.KeyToken);
        Assert.False(key.IsConsumed);
        Assert.True(key.IsActive);

        // 2. Consume key for the first time -> MUST SUCCEED
        ShareKeyConsumeResult firstConsume = await store.ConsumeShareKeyAsync(key.KeyToken, "192.168.1.100");
        Assert.True(firstConsume.Success);
        Assert.NotNull(firstConsume.PeerSessionToken);
        Assert.StartsWith("peer_sess_", firstConsume.PeerSessionToken);
        Assert.True(firstConsume.Record?.IsConsumed);

        // 3. Attempt to consume the SAME key again -> MUST BE REJECTED (Single-Use Rule!)
        ShareKeyConsumeResult secondConsume = await store.ConsumeShareKeyAsync(key.KeyToken, "192.168.1.200");
        Assert.False(secondConsume.Success);
        Assert.Contains("already consumed", secondConsume.Message, StringComparison.OrdinalIgnoreCase);

        // 4. Test expired token rejection
        ShareKeyRecord expiredKey = await store.CreateShareKeyAsync("Expired Test", TimeSpan.FromSeconds(-10), "ReadOnlyTelemetry");
        ShareKeyConsumeResult expiredConsume = await store.ConsumeShareKeyAsync(expiredKey.KeyToken, "192.168.1.50");
        Assert.False(expiredConsume.Success);
        Assert.Contains("expired", expiredConsume.Message, StringComparison.OrdinalIgnoreCase);

        // 5. Test manual revocation
        ShareKeyRecord revokableKey = await store.CreateShareKeyAsync("To Revoke", TimeSpan.FromHours(1), "FullCollaborator");
        bool revoked = await store.RevokeShareKeyAsync(revokableKey.TokenId);
        Assert.True(revoked);
        ShareKeyConsumeResult revokedConsume = await store.ConsumeShareKeyAsync(revokableKey.KeyToken, "192.168.1.50");
        Assert.False(revokedConsume.Success);
    }

    /// <summary>
    /// Verifies the DNA origami fabrication service generates complete staple manifests and protocols.
    /// </summary>
    [Fact]
    public async Task FabricationService_GeneratesAccuratePackageAndSha256Manifest()
    {
        FabricationService fab = new();

        // Verify presets
        IReadOnlyList<DnaOrigamiSample> samples = fab.GetPresetSamples();
        Assert.NotEmpty(samples);
        DnaOrigamiSample mtf = Assert.Single(samples, s => s.Id == "mtf-e2-nanotube");
        Assert.Equal(TherapeuticRegimen.MaleToFemale, mtf.Regimen);

        // Analyze sequence composition
        string testSeq = "ATCGATCGATCG";
        (int a, int t, int g, int c, double gc, double mw) = fab.AnalyzeSequenceComposition(testSeq);
        Assert.Equal(3, a);
        Assert.Equal(3, t);
        Assert.Equal(3, g);
        Assert.Equal(3, c);
        Assert.Equal(50.0, gc);
        Assert.True(mw > 0);

        // Generate full fabrication package
        FabricationPackage package = fab.GenerateFabricationPackage(
            "mtf-e2-nanotube",
            TherapeuticRegimen.MaleToFemale,
            "Hexagonal 6-Helix Bundle Nanotube (6HB)",
            4.2);

        Assert.NotNull(package.PackageId);
        Assert.NotEmpty(package.StapleOligos);
        Assert.NotEmpty(package.AnnealingSchedule);
        Assert.Equal(6, package.AnnealingSchedule.Count); // All 6 thermal stages
        Assert.NotNull(package.Sha256Manifest);

        // Export to disk and verify SHA-256
        (string path, string sha256) = await fab.ExportFabricationManifestJsonAsync(package, _testTempDir);
        Assert.True(File.Exists(path));
        Assert.NotEmpty(sha256);
    }

    /// <summary>
    /// Verifies the stack service manager inspects simulation, database, parser, and peer services.
    /// </summary>
    [Fact]
    public async Task StackServiceManager_InspectsAllStackComponents()
    {
        IConfiguration config = new ConfigurationBuilder().Build();
        using SystemConfigurationStore store = new(config);
        await store.InitializeAsync();

        StackServiceManager mgr = new(store, config);
        IReadOnlyList<StackServiceInfo> services = await mgr.GetStackServicesAsync();

        Assert.Equal(4, services.Count);
        Assert.Contains(services, s => s.ServiceId == "simulation-engine");
        Assert.Contains(services, s => s.ServiceId == "pg-duckdb");
        Assert.Contains(services, s => s.ServiceId == "trajectory-parser");
        Assert.Contains(services, s => s.ServiceId == "peer-mesh");

        StackServiceInfo? parser = await mgr.GetServiceByIdAsync("trajectory-parser");
        Assert.NotNull(parser);
        Assert.Equal(ServiceHealthState.Healthy, parser.Health);
    }

    /// <summary>
    /// Verifies REST API configuration endpoints: GET, POST override, and DELETE revert.
    /// </summary>
    [Fact]
    public async Task RestApi_ConfigurationEndpoints_WorkCorrectly()
    {
        // 1. GET /api/configuration
        HttpResponseMessage getResp = await _client.GetAsync("/api/configuration");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        ConfigurationItem[]? configs = await getResp.Content.ReadFromJsonAsync<ConfigurationItem[]>();
        Assert.NotNull(configs);
        Assert.NotEmpty(configs);

        // 2. POST /api/configuration/override
        ConfigOverrideRequest overrideReq = new()
        {
            Key = "Simulation:AcceleratorBackend",
            Value = "Arc A770 (Discrete GPU)"
        };
        HttpResponseMessage postResp = await _client.PostAsJsonAsync("/api/configuration/override", overrideReq);
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);

        // 3. DELETE /api/configuration/override/{key}
        HttpResponseMessage delResp = await _client.DeleteAsync($"/api/configuration/override/{Uri.EscapeDataString(overrideReq.Key)}");
        Assert.Equal(HttpStatusCode.OK, delResp.StatusCode);
    }

    /// <summary>
    /// Verifies REST API single-use share key endpoints: create and redeem.
    /// </summary>
    [Fact]
    public async Task RestApi_ShareKeyEndpoints_CreateAndRedeem()
    {
        // 1. POST /api/share-keys/create
        CreateShareKeyRequest createReq = new()
        {
            Label = "API Integration Test Peer",
            LifetimeHours = 4,
            Permissions = "FullCollaborator"
        };
        HttpResponseMessage createResp = await _client.PostAsJsonAsync("/api/share-keys/create", createReq);
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode);
        ShareKeyRecord? record = await createResp.Content.ReadFromJsonAsync<ShareKeyRecord>();
        Assert.NotNull(record);
        Assert.False(record.IsConsumed);

        // 2. POST /api/share-keys/redeem -> Success
        RedeemShareKeyRequest redeemReq = new() { Token = record.KeyToken };
        HttpResponseMessage redeemResp = await _client.PostAsJsonAsync("/api/share-keys/redeem", redeemReq);
        Assert.Equal(HttpStatusCode.OK, redeemResp.StatusCode);
        ShareKeyConsumeResult? result = await redeemResp.Content.ReadFromJsonAsync<ShareKeyConsumeResult>();
        Assert.NotNull(result);
        Assert.True(result.Success);

        // 3. POST /api/share-keys/redeem second time -> Rejection (400 Bad Request)
        HttpResponseMessage secondResp = await _client.PostAsJsonAsync("/api/share-keys/redeem", redeemReq);
        Assert.Equal(HttpStatusCode.BadRequest, secondResp.StatusCode);
    }

    /// <summary>
    /// Verifies the extended expiration windows ('1m', '1y', 'never') correctly configure
    /// lifetime durations and that 'never' sets IsNeverExpires.
    /// </summary>
    /// <param name="window">The named expiration window code.</param>
    /// <param name="expectedDays">Expected total lifetime in days.</param>
    [Theory]
    [InlineData("1m", 30)]
    [InlineData("1y", 365)]
    [InlineData("never", 36525)]
    public async Task RestApi_ShareKeyEndpoints_ExtendedExpirationWindows_FunctionCorrectly(string window, int expectedDays)
    {
        // WHAT: Test extended expiration options ('1m', '1y', 'never') through the REST API.
        // WHY: Remote collaboration sessions may need long-term persistent or permanent peering credentials.
        // HOW: Send a CreateShareKeyRequest with the expiration_window field set, verify calculated timestamps and redeemability.
        CreateShareKeyRequest createReq = new()
        {
            Label = $"Collaborator {window}",
            ExpirationWindow = window,
            Permissions = "FullCollaborator"
        };
        HttpResponseMessage createResp = await _client.PostAsJsonAsync("/api/share-keys/create", createReq);
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode);
        ShareKeyRecord? record = await createResp.Content.ReadFromJsonAsync<ShareKeyRecord>();
        Assert.NotNull(record);
        Assert.False(record.IsConsumed);
        Assert.True(record.IsActive);

        if (window == "never")
        {
            Assert.True(record.IsNeverExpires);
            Assert.True(record.ExpiresAtUtc > DateTime.UtcNow.AddYears(90));
        }
        else
        {
            Assert.False(record.IsNeverExpires);
            TimeSpan duration = record.ExpiresAtUtc - record.CreatedAtUtc;
            Assert.InRange(duration.TotalDays, expectedDays - 1, expectedDays + 1);
        }

        // Test redemption works for these extended tokens
        RedeemShareKeyRequest redeemReq = new() { Token = record.KeyToken };
        HttpResponseMessage redeemResp = await _client.PostAsJsonAsync("/api/share-keys/redeem", redeemReq);
        Assert.Equal(HttpStatusCode.OK, redeemResp.StatusCode);
        ShareKeyConsumeResult? result = await redeemResp.Content.ReadFromJsonAsync<ShareKeyConsumeResult>();
        Assert.NotNull(result);
        Assert.True(result.Success);
    }

    /// <summary>
    /// Verifies REST API fabrication endpoints.
    /// </summary>
    [Fact]
    public async Task RestApi_FabricationEndpoints_GeneratePackage()
    {
        // 1. GET /api/fabrication/presets
        HttpResponseMessage presetsResp = await _client.GetAsync("/api/fabrication/presets");
        Assert.Equal(HttpStatusCode.OK, presetsResp.StatusCode);

        // 2. POST /api/fabrication/generate
        FabricationGenerateRequest genReq = new()
        {
            SequenceOrSampleId = "ftm-t-cuboid",
            Regimen = TherapeuticRegimen.FemaleToMale,
            Geometry = "3D Cuboid Box with Aptamer Lid",
            TargetKd = 8.5
        };
        HttpResponseMessage genResp = await _client.PostAsJsonAsync("/api/fabrication/generate", genReq);
        Assert.Equal(HttpStatusCode.OK, genResp.StatusCode);
        FabricationPackage? pkg = await genResp.Content.ReadFromJsonAsync<FabricationPackage>();
        Assert.NotNull(pkg);
        Assert.Equal(TherapeuticRegimen.FemaleToMale, pkg.Regimen);
        Assert.NotEmpty(pkg.StapleOligos);
    }
}
