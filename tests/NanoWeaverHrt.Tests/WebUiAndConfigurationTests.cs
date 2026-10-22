using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Services.Stack;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Comprehensive test suite verifying the Blazor web UI backend, database configuration precedence
/// ("Whatever is in the database wins, stack environment variables do nothing if configuration exists in the database"),
/// single-use collaborative share key lifecycles, and DNA origami fabrication synthesis.
/// </summary>
public sealed class WebUiAndConfigurationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
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

        try
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, recursive: true);
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
        // WHAT: Allocates a private, writable data/export/parquet root for this test class instance.
        // WHY:  SystemConfigurationStore places weaver_config.db inside TRAJECTORY_INCOMING_DIR. Pointing
        //       that at the repository's live simulations/shared-data made the suite read and write the
        //       operator's real configuration and user database, which both leaked production identities
        //       into assertions and failed outright whenever that file was owned by another user.
        // HOW:   Derives a GUID-suffixed directory under the OS temp path and seeds trajectory fixtures there.
        string testRoot = Path.Combine(Path.GetTempPath(), "weaver_ui_" + Guid.NewGuid().ToString("N"));
        _testRoot = testRoot;
        string dataDir = Path.Combine(testRoot, "shared-data");
        string exportDir = Path.Combine(testRoot, "exports");
        string parquetDir = Path.Combine(testRoot, "parquet");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(exportDir);
        Directory.CreateDirectory(parquetDir);

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
        TestFixturesHelper.ConfigureAuthenticatedClient(_client, _factory);
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

        // Verify DIY / HomeLab fabrication guide
        Assert.NotNull(package.DiyFabricationGuide);
        Assert.NotEmpty(package.DiyFabricationGuide.Materials);
        Assert.NotEmpty(package.DiyFabricationGuide.PipettingTable);
        Assert.Equal(7, package.DiyFabricationGuide.Steps.Count); // All 7 bench steps
        Assert.NotEmpty(package.DiyFabricationGuide.QualityControlGuide);
        Assert.NotEmpty(package.DiyFabricationGuide.SafetyAndHandlingNotes);

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
        string configsDir = Path.Combine(_testTempDir, "sim_configs");
        Directory.CreateDirectory(configsDir);
        await File.WriteAllTextAsync(Path.Combine(configsDir, "test.in"), "units real\n");

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SIMULATION_CONFIGS_DIR"] = configsDir,
                ["TRAJECTORY_INCOMING_DIR"] = _testTempDir,
                ["POSTGRES_CONNECTION_STRING"] = "Host=nonexistent_test_host;Timeout=1"
            })
            .Build();

        using SystemConfigurationStore store = new(config);
        await store.InitializeAsync();

        // WHAT: Supplies a probe client whose every request fails.
        // WHY:  Without an injected client StackServiceManager builds a real HttpClient, and its
        //       simulation probe falls back from the unresolvable "simulation-node" host to
        //       http://localhost:8080. The Docker stack publishes the simulation daemon on exactly
        //       that host port, so whenever the stack happened to be running this test observed a
        //       live daemon and read "Online & Connected" instead of the offline "Local Mode Active"
        //       path it is asserting. That made the result depend on ambient machine state.
        // HOW:   Forces both the primary and localhost-fallback probes to throw so the offline
        //        branch is exercised deterministically, per the zero-flakiness standard.
        using HttpClient offlineProbeClient = new(new TestMockHttpMessageHandler(
            _ => throw new HttpRequestException("simulation probe unavailable in unit test")));
        StackServiceManager mgr = new(store, config, offlineProbeClient);
        IReadOnlyList<StackServiceInfo> services = await mgr.GetStackServicesAsync();

        Assert.Equal(4, services.Count);
        Assert.Contains(services, s => s.ServiceId == "simulation-engine");
        Assert.Contains(services, s => s.ServiceId == "pg-duckdb");
        Assert.Contains(services, s => s.ServiceId == "trajectory-parser");
        Assert.Contains(services, s => s.ServiceId == "peer-mesh");

        StackServiceInfo? parser = await mgr.GetServiceByIdAsync("trajectory-parser");
        Assert.NotNull(parser);
        Assert.Equal(ServiceHealthState.Healthy, parser.Health);

        // Test discovered configs
        IReadOnlyList<string> discovered = mgr.GetDiscoveredSimulationConfigs();
        Assert.Contains("test.in", discovered);

        // Test SIMD descriptions
        Assert.Equal("AVX-512 (512-bit SIMD)", StackServiceManager.GetSimdCapabilityDescription(true, false, false));
        Assert.Equal("AVX2 (256-bit SIMD)", StackServiceManager.GetSimdCapabilityDescription(false, true, false));
        Assert.Equal("SSE4.2 (128-bit SIMD)", StackServiceManager.GetSimdCapabilityDescription(false, false, true));
        Assert.Equal("Software Fallback", StackServiceManager.GetSimdCapabilityDescription(false, false, false));

        // Test GC mode descriptions
        Assert.Equal("Enabled (Server GC)", StackServiceManager.GetGcModeDescription(true));
        Assert.Equal("Workstation", StackServiceManager.GetGcModeDescription(false));

        // Test with mock postgres repository to cover isPostgres == true
        MockPostgresConfigRepo mockPgRepo = new();
        StackServiceManager pgMgr = new(mockPgRepo, config);
        StackServiceInfo pgInfo = await pgMgr.PingServiceAsync("pg-duckdb");
        Assert.Equal(ServiceHealthState.Healthy, pgInfo.Health);
        Assert.Contains("PostgreSQL", pgInfo.DisplayName);

        // Test simulation ping when offline with configs discovered (Local Mode branch)
        StackServiceInfo simInfo = await mgr.PingServiceAsync("simulation-engine");
        Assert.Contains("Local Mode Active", simInfo.StatusMessage);

        // Test empty configs directory branch
        IConfiguration emptyConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SIMULATION_CONFIGS_DIR"] = Path.Combine(_testTempDir, "non_existent_configs_dir")
            })
            .Build();
        StackServiceManager emptyMgr = new(store, emptyConfig);
        Assert.Empty(emptyMgr.GetDiscoveredSimulationConfigs());
    }

    /// <summary>
    /// Verifies all probing branches of StackServiceManager for the simulation node (daemon online, corrupted JSON, localhost fallback, failure).
    /// </summary>
    [Fact]
    public async Task StackServiceManager_SimulationNode_AllProbingBranches()
    {
        // WHAT: Exercises all branches of InspectSimulationNodeAsync with various HTTP responses and failures.
        // WHY:  Guarantees 100% line and branch coverage across simulation engine telemetry checks.
        // HOW:  Uses custom TestMockHttpMessageHandler to simulate online daemon, corrupted JSON, fallback, and errors.
        string configsDir = Path.Combine(_testTempDir, "sim_probe_configs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configsDir);
        await File.WriteAllTextAsync(Path.Combine(configsDir, "deck.in"), "units real\n");

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SIMULATION_CONFIGS_DIR"] = configsDir,
                ["TRAJECTORY_INCOMING_DIR"] = _testTempDir,
                ["POSTGRES_CONNECTION_STRING"] = "Host=nonexistent_test_host;Timeout=1"
            })
            .Build();

        using SystemConfigurationStore store = new(config);
        await store.InitializeAsync();

        IConfiguration emptyConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SIMULATION_CONFIGS_DIR"] = Path.Combine(_testTempDir, "empty_configs_" + Guid.NewGuid().ToString("N"))
            })
            .Build();

        // 1. Online simulation daemon with valid accelerator property
        using HttpClient clientOnline = new(new TestMockHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"Healthy\",\"accelerator\":\"Intel Arc A770 (Level Zero)\"}", Encoding.UTF8, "application/json")
            }));
        StackServiceManager mgrOnline = new(store, config, clientOnline);
        StackServiceInfo infoOnline = await mgrOnline.PingServiceAsync("simulation-engine");
        Assert.Equal(ServiceHealthState.Healthy, infoOnline.Health);
        Assert.Contains("Online & Connected (Intel Arc A770 (Level Zero))", infoOnline.StatusMessage);
        Assert.Equal("Active (HTTP/1.1 REST API)", infoOnline.Metrics["Simulation Daemon"]);

        // 2. Online simulation daemon with corrupted JSON payload (falls back to default accelerator)
        using HttpClient clientCorrupt = new(new TestMockHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{malformed_json", Encoding.UTF8, "application/json")
            }));
        StackServiceManager mgrCorrupt = new(store, config, clientCorrupt);
        StackServiceInfo infoCorrupt = await mgrCorrupt.PingServiceAsync("simulation-engine");
        Assert.Equal(ServiceHealthState.Healthy, infoCorrupt.Health);
        Assert.Contains("Online & Connected", infoCorrupt.StatusMessage);

        // 3. simulation-node throws exception, but localhost fallback succeeds
        using HttpClient clientFallback = new(new TestMockHttpMessageHandler(req =>
        {
            if (req.RequestUri != null && req.RequestUri.Host.Equals("simulation-node", StringComparison.OrdinalIgnoreCase))
            {
                throw new HttpRequestException("simulation-node host resolution failed");
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"Healthy\",\"accelerator\":\"CUDA Fallback\"}", Encoding.UTF8, "application/json")
            };
        }));
        StackServiceManager mgrFallback = new(store, config, clientFallback);
        StackServiceInfo infoFallback = await mgrFallback.PingServiceAsync("simulation-engine");
        Assert.Equal(ServiceHealthState.Healthy, infoFallback.Health);
        Assert.Contains("localhost", infoFallback.EndpointOrLocation);
        Assert.Contains("Online & Connected (CUDA Fallback)", infoFallback.StatusMessage);

        // 4. Primary endpoint returns non-success (500) and localhost fallback throws exception; configs == 0 -> Offline
        using HttpClient clientFail = new(new TestMockHttpMessageHandler(req =>
        {
            if (req.RequestUri != null && req.RequestUri.Host.Equals("simulation-node", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }
            throw new HttpRequestException("Localhost fallback failed");
        }));
        StackServiceManager mgrFail = new(store, emptyConfig, clientFail);
        StackServiceInfo infoFail = await mgrFail.PingServiceAsync("simulation-engine");
        Assert.Equal(ServiceHealthState.Offline, infoFail.Health);
        Assert.Contains("Offline / Standby", infoFail.StatusMessage);

        // 5. Localhost fallback returns non-success (503); configs == 0 -> Offline
        using HttpClient clientFallbackFail = new(new TestMockHttpMessageHandler(req =>
        {
            if (req.RequestUri != null && req.RequestUri.Host.Equals("simulation-node", StringComparison.OrdinalIgnoreCase))
            {
                throw new HttpRequestException("simulation-node unreachable");
            }
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }));
        StackServiceManager mgrFallbackFail = new(store, emptyConfig, clientFallbackFail);
        StackServiceInfo infoFallbackFail = await mgrFallbackFail.PingServiceAsync("simulation-engine");
        Assert.Equal(ServiceHealthState.Offline, infoFallbackFail.Health);
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

    /// <summary>
    /// Verifies the native JavaScript charting and visualization engine executes with 100%
    /// line and branch test coverage enforced via the Node.js native test runner.
    /// </summary>
    [Fact]
    public void JavaScriptVisualizer_FullTestSuite_Achieves100PercentLineAndBranchCoverage()
    {
        // WHAT: Executes the native JavaScript visualization test suite.
        // WHY:  Enforces identical build and coverage constraints (100% line and branch coverage) across JS and .NET code.
        // HOW:  Runs 'node --test --experimental-test-coverage' with coverage checks on weaver-visualizer.test.mjs.
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "NanoWeaverHrt.slnx")))
        {
            current = Directory.GetParent(current)?.FullName;
        }
        string repoRoot = current ?? Directory.GetCurrentDirectory();
        string testFile = Path.Combine(repoRoot, "tests", "weaver-visualizer.test.mjs");

        Assert.True(File.Exists(testFile), $"Test file not found: {testFile}");

        // WHAT: Detects Node.js major version to conditionally pass branch/line flags.
        // WHY:  --test-coverage-branches was introduced in Node.js v22.8.0; Node v20 runners reject this flag.
        // HOW:  Inspects 'node --version' output and tailors CLI arguments accordingly.
        string nodeArgs = "--test --experimental-test-coverage --test-coverage-branches=100 --test-coverage-lines=100 --test-coverage-functions=100 tests/weaver-visualizer.test.mjs";
        try
        {
            ProcessStartInfo verPsi = new("node", "--version") { RedirectStandardOutput = true, UseShellExecute = false };
            using Process? verProc = Process.Start(verPsi);
            if (verProc != null)
            {
                string verStr = verProc.StandardOutput.ReadToEnd().Trim().TrimStart('v');
                verProc.WaitForExit();
                if (int.TryParse(verStr.Split('.')[0], out int maj) && maj < 22)
                {
                    nodeArgs = "--test --experimental-test-coverage tests/weaver-visualizer.test.mjs";
                }
            }
        }
        catch
        {
            // Fallback to default args
        }

        ProcessStartInfo psi = new()
        {
            FileName = "node",
            Arguments = nodeArgs,
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using Process proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to spawn node process");
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        Assert.True(proc.ExitCode == 0, $"JavaScript tests failed with exit code {proc.ExitCode}.\nStdout:\n{stdout}\nStderr:\n{stderr}");
        Assert.Contains("pass 9", stdout);
        if (nodeArgs.Contains("--test-coverage-branches"))
        {
            Assert.Contains("100.00 |   100.00 |  100.00", stdout);
        }
    }

    /// <summary>
    /// Verifies that simultaneous concurrent redemption attempts on the same single-use share key
    /// result in exactly one successful redemption and all other attempts fail.
    /// </summary>
    [Fact]
    public async Task SingleUseShareKey_ConcurrentRedemption_GuaranteesExactlyOneWinner()
    {
        // WHAT: Spawns 10 concurrent redemption requests attempting to consume the same key.
        // WHY:  Guarantees race-condition safety and atomic single-use database semantics.
        // HOW:  Uses Task.WhenAll with parallel ConsumeShareKeyAsync calls.
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _testTempDir,
                ["POSTGRES_CONNECTION_STRING"] = "Host=nonexistent_test_host;Timeout=1"
            })
            .Build();

        using SystemConfigurationStore store = new(config);
        await store.InitializeAsync();

        ShareKeyRecord key = await store.CreateShareKeyAsync("Concurrent Race Key", TimeSpan.FromMinutes(30), "FullCollaborator");

        const int concurrencyLevel = 10;
        Task<ShareKeyConsumeResult>[] tasks = new Task<ShareKeyConsumeResult>[concurrencyLevel];
        for (int i = 0; i < concurrencyLevel; i++)
        {
            int peerId = i + 1;
            tasks[i] = Task.Run(() => store.ConsumeShareKeyAsync(key.KeyToken, $"10.0.0.{peerId}"));
        }

        ShareKeyConsumeResult[] results = await Task.WhenAll(tasks);
        int successCount = results.Count(r => r.Success);
        int failureCount = results.Count(r => !r.Success);

        Assert.Equal(1, successCount);
        Assert.Equal(concurrencyLevel - 1, failureCount);
    }

    /// <summary>
    /// Verifies 100% mathematical accuracy of oligonucleotide melting temperature calculations
    /// across Wallace short-oligo rules (&lt; 14 nt) and Marmur empirical formulas (&gt;= 14 nt).
    /// </summary>
    [Fact]
    public void FabricationService_CalculatesAccurateMeltingTemperatures_ShortAndLong()
    {
        // WHAT: Tests Wallace rule for short oligos, Marmur rule for long oligos, and empty sequences.
        // WHY:  Biochemically accurate Tm is essential for DNA origami thermal annealing ramp design.
        // HOW:  Asserts exact expected Tm for Wallace formula, Marmur formula, and RNA Uracil bases.
        FabricationService fab = new();

        // 1. Zero bases
        Assert.Equal(0.0, FabricationService.CalculateMeltingTemperature(0, 0, 0, 0));
        Assert.Equal(0.0, fab.CalculateSequenceMeltingTemperature(""));

        // 2. Wallace rule: length < 14. Tm = 2*(A+T) + 4*(G+C)
        // a=2, t=2, g=2, c=2 => (2+2)*2 + (2+2)*4 = 8 + 16 = 24.0°C
        Assert.Equal(24.0, FabricationService.CalculateMeltingTemperature(2, 2, 2, 2));
        Assert.Equal(24.0, fab.CalculateSequenceMeltingTemperature("AATTGGCC"));

        // 3. Marmur rule: length >= 14. Tm = 64.9 + 41.0 * (G + C - 16.4) / total
        // a=10, t=10, g=15, c=15 => total = 50, (g+c) = 30.
        // Tm = 64.9 + 41.0 * (30 - 16.4) / 50 = 64.9 + 41.0 * 13.6 / 50 = 64.9 + 11.152 = 76.052 => rounded to 76.1°C
        Assert.Equal(76.1, FabricationService.CalculateMeltingTemperature(10, 10, 15, 15));

        // 4. RNA containing Uracil: 'U' treated identically to 'T'
        (int rnaA, int rnaT, int rnaG, int rnaC, double rnaGc, double rnaMw) = fab.AnalyzeSequenceComposition("AAUUGGCC");
        Assert.Equal(2, rnaA);
        Assert.Equal(2, rnaT);
        Assert.Equal(2, rnaG);
        Assert.Equal(2, rnaC);
        Assert.Equal(50.0, rnaGc);
        Assert.Equal(2.64, rnaMw);
        Assert.Equal(24.0, fab.CalculateSequenceMeltingTemperature("AAUUGGCC"));

        // 5. Empty sequence composition
        (int emptyA, int emptyT, int emptyG, int emptyC, double emptyGc, double emptyMw) = fab.AnalyzeSequenceComposition("");
        Assert.Equal(0, emptyA);
        Assert.Equal(0, emptyT);
        Assert.Equal(0, emptyG);
        Assert.Equal(0, emptyC);
        Assert.Equal(0.0, emptyGc);
        Assert.Equal(0.0, emptyMw);

        // 6. Fabrication package with custom sequence and FemaleToMale regimen
        FabricationPackage ftmPkg = fab.GenerateFabricationPackage(
            "CUSTOM_SEQUENCE_ATGC",
            TherapeuticRegimen.FemaleToMale,
            "Spherical Nanocage (3D Octahedron)",
            6.5);

        Assert.Equal(TherapeuticRegimen.FemaleToMale, ftmPkg.Regimen);
        Assert.Equal("Steroidogenic Receptors (Pituitary/Adrenal)", ftmPkg.TargetTissue);
        Assert.Equal(22.5, ftmPkg.ExpectedHydrodynamicRadiusNm);
        Assert.NotEmpty(ftmPkg.StapleOligos);

        // 7. Fabrication package with custom sequence and MaleToFemale regimen
        FabricationPackage mtfCustom = fab.GenerateFabricationPackage(
            "CUSTOM_SEQUENCE_ATGC",
            TherapeuticRegimen.MaleToFemale,
            "Hexagonal 6-Helix Bundle Nanotube (6HB)",
            4.0);

        Assert.Equal(TherapeuticRegimen.MaleToFemale, mtfCustom.Regimen);
        Assert.Equal("Anterior Pituitary Gonadotrope (GnRH-R)", mtfCustom.TargetTissue);
        Assert.Equal(14.8, mtfCustom.ExpectedHydrodynamicRadiusNm);
        Assert.NotEmpty(mtfCustom.StapleOligos);
    }

    /// <summary>
    /// Verifies edge cases of the configuration and share key store including non-existent keys,
    /// manual revocations, and key listings.
    /// </summary>
    [Fact]
    public async Task ConfigurationStore_EdgeCasesAndRevocations()
    {
        // WHAT: Tests non-existent key lookup, token consumption errors, and revocation returns.
        // WHY:  Guarantees robust error handling and 100% branch reliability.
        // HOW:  Invokes ConsumeShareKeyAsync and RevokeShareKeyAsync with unknown IDs.
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _testTempDir,
                ["POSTGRES_CONNECTION_STRING"] = "Host=nonexistent_test_host;Timeout=1"
            })
            .Build();

        using SystemConfigurationStore store = new(config);
        await store.InitializeAsync();

        // 1. Consume unknown key
        ShareKeyConsumeResult unknownConsume = await store.ConsumeShareKeyAsync("nanoweaver_hrt_does_not_exist", "127.0.0.1");
        Assert.False(unknownConsume.Success);
        Assert.Contains("not recognized", unknownConsume.Message, StringComparison.OrdinalIgnoreCase);

        // 2. Revoke unknown key
        bool revokedNonExistent = await store.RevokeShareKeyAsync("fake_token_id_9999");
        Assert.False(revokedNonExistent);

        // 3. List keys
        ShareKeyRecord created = await store.CreateShareKeyAsync("Listing Test", TimeSpan.FromHours(1), "FullCollaborator");
        IReadOnlyList<ShareKeyRecord> allKeys = await store.ListShareKeysAsync();
        Assert.Contains(allKeys, k => k.TokenId == created.TokenId);
    }

    /// <summary>
    /// Verifies SystemConfigurationStore constructor falls back to default paths when configuration is empty.
    /// </summary>
    [Fact]
    public void ConfigurationStore_Constructor_DefaultPathsWhenConfigEmpty()
    {
        // WHAT: Instantiates SystemConfigurationStore with an empty configuration.
        // WHY:  Covers the null coalescing fallback branches for incoming directory and postgres connection string.
        // HOW:  Passes empty IConfiguration and asserts non-null instance.
        IConfiguration emptyConfig = new ConfigurationBuilder().Build();
        using SystemConfigurationStore store = new(emptyConfig);
        Assert.NotNull(store);
    }
}

/// <summary>
/// Mock repository returning PostgreSQL as the active backend to test PostgreSQL-specific stack inspection paths.
/// </summary>
public sealed class MockPostgresConfigRepo : IConfigurationRepository
{
    /// <inheritdoc/>
    public string ActiveBackendName => "PostgreSQL (pg-duckdb)";

    /// <inheritdoc/>
    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<IReadOnlyList<ConfigurationItem>> GetAllConfigurationsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ConfigurationItem>>([]);

    /// <inheritdoc/>
    public Task<string> GetValueAsync(string key, string defaultValue = "", CancellationToken ct = default)
    {
        if (key == "Storage:PostgresConnectionString") return Task.FromResult("Host=localhost;Database=weaver");
        return Task.FromResult(defaultValue);
    }

    /// <inheritdoc/>
    public Task SetDatabaseOverrideAsync(string key, string value, string description = "", CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task RevertToDefaultAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<ShareKeyRecord> CreateShareKeyAsync(string label, TimeSpan lifetime, string permissions = "FullCollaborator", CancellationToken ct = default) => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<ShareKeyConsumeResult> ConsumeShareKeyAsync(string keyToken, string remotePeerInfo, CancellationToken ct = default) => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<IReadOnlyList<ShareKeyRecord>> ListShareKeysAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ShareKeyRecord>>([]);

    /// <inheritdoc/>
    public Task<bool> RevokeShareKeyAsync(string tokenId, CancellationToken ct = default) => Task.FromResult(false);
}

/// <summary>
/// Test mock HTTP message handler supporting customized delegate responses for probe checks.
/// </summary>
public sealed class TestMockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestMockHttpMessageHandler"/> class.
    /// </summary>
    /// <param name="responder">The delegate producing mock HTTP responses.</param>
    public TestMockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_responder(request));
    }
}
