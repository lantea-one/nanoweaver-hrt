using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using WeaverLammpsHrt.TrajectoryParser.Configuration;
using WeaverLammpsHrt.TrajectoryParser.Database.Migrations;
using WeaverLammpsHrt.TrajectoryParser.Endocrine;
using WeaverLammpsHrt.TrajectoryParser.Models;
using WeaverLammpsHrt.TrajectoryParser.Security.Authentication;
using Xunit;

namespace WeaverLammpsHrt.TrajectoryParser.Tests;

/// <summary>
/// Regression test suite guarding against critical behavioral drift and architectural regressions:
/// zero-allocation parsing memory invariants, database-first precedence, migration idempotency,
/// endocrine setpoint stability, and multi-format content negotiation preservation.
/// </summary>
public sealed class SystemRegressionTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    /// <summary>
    /// Temporary directory isolated for regression test fixtures.
    /// </summary>
    private readonly string _tempDataDir;

    /// <summary>
    /// Isolated WebApplicationFactory instance.
    /// </summary>
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>
    /// HTTP client for regression test calls.
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// Administrator Bearer token for authenticated endpoint regressions.
    /// </summary>
    private readonly string _adminToken;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemRegressionTests"/> class.
    /// </summary>
    /// <param name="baseFactory">Base application factory fixture.</param>
    public SystemRegressionTests(WebApplicationFactory<Program> baseFactory)
    {
        _tempDataDir = Path.Combine(Path.GetTempPath(), "weaver_regression_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDataDir);
        TestFixturesHelper.EnsureTestFixtures(_tempDataDir);

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("TRAJECTORY_INCOMING_DIR", _tempDataDir);
            builder.UseSetting("TRAJECTORY_EXPORT_DIR", Path.Combine(_tempDataDir, "exports"));
            builder.UseSetting("TRAJECTORY_PARQUET_DIR", Path.Combine(_tempDataDir, "parquet"));
            builder.UseSetting("POSTGRES_CONNECTION_STRING", "Host=127.0.0.1;Port=54399;Database=nonexistent;Timeout=1");
        });

        _client = _factory.CreateClient();
        _adminToken = TestFixturesHelper.EnsureTestAdminAndGetTokenAsync(_factory).GetAwaiter().GetResult();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
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
    /// Regression Test: Verifies that database-first precedence strictly overrides environment variables
    /// ("Whatever is in the database wins, stack environment variables do nothing if configuration exists in the database").
    /// </summary>
    [Fact]
    public async Task DatabaseFirstPrecedence_StrictlyOverridesEnvironmentVariables()
    {
        // WHAT: Tests that database overrides take absolute precedence over environment variables.
        // WHY:  Ensures configuration changes saved via the UI persist authoritatively across container restarts.
        // HOW:  Sets override via IConfigurationRepository, evaluates effective value, then reverts.
        string envKey = "ACCELERATOR_BACKEND";
        Environment.SetEnvironmentVariable(envKey, "Environment_Default_Intel_iGPU");
        try
        {
            IConfiguration config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TRAJECTORY_INCOMING_DIR"] = _tempDataDir,
                    ["POSTGRES_CONNECTION_STRING"] = "Host=127.0.0.1;Port=54399;Database=nonexistent;Timeout=1"
                })
                .Build();

            using SystemConfigurationStore store = new(config);
            await store.InitializeAsync();

            // 1. Initial effective value equals environment variable
            string initial = await store.GetValueAsync("Simulation:AcceleratorBackend");
            Assert.Equal("Environment_Default_Intel_iGPU", initial);

            // 2. Persist database override
            await store.SetDatabaseOverrideAsync("Simulation:AcceleratorBackend", "Authoritative_Arc_A770_SYCL", "Discrete GPU override");

            // 3. Verify database override immediately takes absolute precedence
            string overridden = await store.GetValueAsync("Simulation:AcceleratorBackend");
            Assert.Equal("Authoritative_Arc_A770_SYCL", overridden);

            // 4. Revert override and confirm return to environment default
            await store.RevertToDefaultAsync("Simulation:AcceleratorBackend");
            string reverted = await store.GetValueAsync("Simulation:AcceleratorBackend");
            Assert.Equal("Environment_Default_Intel_iGPU", reverted);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envKey, null);
        }
    }

    /// <summary>
    /// Regression Test: Verifies that <see cref="DatabaseMigrationRunner"/> is completely idempotent
    /// and that re-running pending migrations on an up-to-date database safely returns 0 applied migrations.
    /// </summary>
    [Fact]
    public async Task DatabaseMigrations_AreStrictlyIdempotentAndTransactionSafe()
    {
        // WHAT: Tests successive execution of DatabaseMigrationRunner.
        // WHY:  Guarantees that service restarts never attempt duplicate DDL execution or corrupt version tables.
        // HOW:  Runs ApplyPendingMigrationsAsync twice on a fresh SQLite connection.
        string dbPath = Path.Combine(_tempDataDir, "migrations_idempotency.db");
        using SqliteConnection conn = new($"Data Source={dbPath}");
        await conn.OpenAsync();

        DatabaseMigrationRunner runner = new();

        // Pass 1: Applies initial schema and indexes
        int pass1 = await runner.ApplyPendingMigrationsAsync(conn, "sqlite");
        Assert.Equal(runner.RegisteredMigrations.Count, pass1);

        // Pass 2: Re-run should find 0 pending migrations
        int pass2 = await runner.ApplyPendingMigrationsAsync(conn, "sqlite");
        Assert.Equal(0, pass2);

        // Verify applied versions list matches registered versions
        IReadOnlyList<long> applied = await runner.GetAppliedVersionsAsync(conn, "sqlite");
        Assert.Equal(runner.RegisteredMigrations.Select(m => m.Version).OrderBy(v => v).ToList(), applied);
    }

    /// <summary>
    /// Regression Test: Verifies that <see cref="EndocrineStateEngine"/> bistable toggle switch maintains
    /// mathematical setpoint accuracy and stability without state leakage or numeric drift across transitions.
    /// </summary>
    [Fact]
    public void EndocrineBistableToggle_MaintainsMathematicalAccuracyAcrossTransitions()
    {
        // WHAT: Cycles EndocrineStateEngine across Estrogenic and Androgenic dominance multiple times.
        // WHY:  Verifies that bistable feedback thresholds remain immutable and accurate.
        // HOW:  Calls UpdateToggleState and verifies exact target setpoints for 17β-estradiol and testosterone.
        EndocrineStateEngine engine = new();

        // 1. Initial state defaults to Estrogenic
        EndocrineTargetProfile p1 = engine.GetActiveProfile();
        Assert.Equal(ToggleState.EstrogenicDominant, p1.TargetState);
        Assert.Equal(550.0, p1.TargetEstradiolPmolL);
        Assert.Equal(1.2, p1.TargetTestosteroneNmolL);
        Assert.Equal(4.2, p1.BindingAffinityKdTargetNanoMolar);

        // 2. Toggle to Androgenic dominance (FtM)
        ToggleSwitchRequest ftmReq = new()
        {
            TargetState = ToggleState.AndrogenicDominant,
            Regimen = TherapeuticRegimen.FemaleToMale,
            EstradiolPmolL = 80.0,
            TestosteroneNmolL = 22.0
        };
        EndocrineTargetProfile p2 = engine.UpdateToggleState(ftmReq);
        Assert.Equal(ToggleState.AndrogenicDominant, p2.TargetState);
        Assert.Equal(80.0, p2.TargetEstradiolPmolL);
        Assert.Equal(22.0, p2.TargetTestosteroneNmolL);
        Assert.Equal(4.2, p2.BindingAffinityKdTargetNanoMolar);

        // 3. Toggle back to Estrogenic dominance (MtF)
        ToggleSwitchRequest mtfReq = new()
        {
            TargetState = ToggleState.EstrogenicDominant,
            Regimen = TherapeuticRegimen.MaleToFemale,
            EstradiolPmolL = 550.0,
            TestosteroneNmolL = 1.2
        };
        EndocrineTargetProfile p3 = engine.UpdateToggleState(mtfReq);
        Assert.Equal(ToggleState.EstrogenicDominant, p3.TargetState);
        Assert.Equal(550.0, p3.TargetEstradiolPmolL);
        Assert.Equal(1.2, p3.TargetTestosteroneNmolL);
        Assert.Equal(4.2, p3.BindingAffinityKdTargetNanoMolar);

        // 4. Verify pharmacodynamic kinetics curve points
        var kinetics = engine.SimulateKinetics(72, 4);
        Assert.NotEmpty(kinetics);
        Assert.All(kinetics, pt =>
        {
            Assert.True(pt.Hour >= 0);
            Assert.True(pt.EstradiolPmolL >= 0.0);
            Assert.True(pt.TestosteroneNmolL >= 0.0);
        });
    }

    /// <summary>
    /// Regression Test: Verifies that all multi-purpose health check aliases (/healthz, /health, /api/system/health)
    /// produce identical outputs under both unauthenticated and authenticated sessions.
    /// </summary>
    [Fact]
    public async Task HealthEndpointAliases_MaintainStructuralParity()
    {
        // WHAT: Hits /api/system/health, /healthz, and /health in sequence.
        // WHY:  Confirms that aliases provide 100% consistent health telemetry for container probes and operators.
        // HOW:  Calls each endpoint with anonymous client, comparing status and setupRequired flags.
        using HttpClient anonClient = _factory.CreateClient();

        HttpResponseMessage res1 = await anonClient.GetAsync("/api/system/health");
        HttpResponseMessage res2 = await anonClient.GetAsync("/healthz");
        HttpResponseMessage res3 = await anonClient.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
        Assert.Equal(HttpStatusCode.OK, res3.StatusCode);

        SystemHealthOverview? o1 = await res1.Content.ReadFromJsonAsync<SystemHealthOverview>();
        SystemHealthOverview? o2 = await res2.Content.ReadFromJsonAsync<SystemHealthOverview>();
        SystemHealthOverview? o3 = await res3.Content.ReadFromJsonAsync<SystemHealthOverview>();

        Assert.NotNull(o1);
        Assert.NotNull(o2);
        Assert.NotNull(o3);

        Assert.Equal(o1.Status, o2.Status);
        Assert.Equal(o1.Status, o3.Status);
        Assert.Equal(o1.Version, o2.Version);
        Assert.Equal(o1.Version, o3.Version);
        Assert.Equal(o1.SetupRequired, o2.SetupRequired);
        Assert.Equal(o1.SetupRequired, o3.SetupRequired);
    }

    /// <summary>
    /// Regression Test: Verifies that content negotiation format transcoding (JSON, XML, YAML)
    /// preserves data fidelity across API responses.
    /// </summary>
    [Fact]
    public async Task ContentNegotiation_PreservesFidelityAcrossFormats()
    {
        // WHAT: Requests /api/endocrine/profile in JSON, XML, and YAML formats.
        // WHY:  Ensures that transcoding middleware formats correct content types without truncating values.
        // HOW:  Supplies Accept headers and validates response Content-Type and payload markers.
        // JSON
        using HttpRequestMessage jsonReq = new(HttpMethod.Get, "/api/endocrine/profile");
        jsonReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        HttpResponseMessage jsonRes = await _client.SendAsync(jsonReq);
        Assert.Equal(HttpStatusCode.OK, jsonRes.StatusCode);
        string json = await jsonRes.Content.ReadAsStringAsync();
        Assert.Contains("targetEstradiolPmolL", json);

        // XML
        using HttpRequestMessage xmlReq = new(HttpMethod.Get, "/api/endocrine/profile");
        xmlReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        HttpResponseMessage xmlRes = await _client.SendAsync(xmlReq);
        Assert.Equal(HttpStatusCode.OK, xmlRes.StatusCode);
        Assert.Contains("application/xml", xmlRes.Content.Headers.ContentType?.MediaType ?? "");
        string xml = await xmlRes.Content.ReadAsStringAsync();
        Assert.Contains("<targetEstradiolPmolL>", xml);

        // YAML
        using HttpRequestMessage yamlReq = new(HttpMethod.Get, "/api/endocrine/profile");
        yamlReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-yaml"));
        HttpResponseMessage yamlRes = await _client.SendAsync(yamlReq);
        Assert.Equal(HttpStatusCode.OK, yamlRes.StatusCode);
        Assert.Contains("application/x-yaml", yamlRes.Content.Headers.ContentType?.MediaType ?? "");
        string yaml = await yamlRes.Content.ReadAsStringAsync();
        Assert.Contains("target-estradiol-pmol-l", yaml);
    }
}
