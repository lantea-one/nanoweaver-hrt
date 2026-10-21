using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NanoWeaverHrt.Bridges;
using NanoWeaverHrt.Caching;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Events;
using NanoWeaverHrt.Events.Models;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Simulations;
using NanoWeaverHrt.Simulations.Models;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Comprehensive integration and unit test suite verifying the multi-purpose health check endpoints
/// (/api/system/health, /healthz, /health), model contracts, authentication gating, and granular diagnostics.
/// </summary>
public sealed class SystemHealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>
    /// Test server web application factory.
    /// </summary>
    private readonly WebApplicationFactory<Program> _factory;

    /// <summary>
    /// Authenticated HTTP client connected to in-memory test server.
    /// </summary>
    private readonly HttpClient _authClient;

    /// <summary>
    /// Unauthenticated HTTP client connected to in-memory test server.
    /// </summary>
    private readonly HttpClient _anonymousClient;

    /// <summary>
    /// Valid Bearer token extracted for test administrator.
    /// </summary>
    private readonly string _adminToken;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemHealthEndpointTests"/> class.
    /// </summary>
    /// <param name="factory">The test web application factory fixture.</param>
    public SystemHealthEndpointTests(WebApplicationFactory<Program> factory)
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

        _anonymousClient = _factory.CreateClient();
        _authClient = _factory.CreateClient();
        _adminToken = TestFixturesHelper.EnsureTestAdminAndGetTokenAsync(_factory).GetAwaiter().GetResult();
        _authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
    }

    /// <summary>
    /// Verifies that <see cref="SystemHealthOverview"/> correctly instantiates properties and generates valid OpenAPI examples.
    /// </summary>
    [Fact]
    public void SystemHealthOverview_ModelPropertiesAndExamples_BehaveAccurately()
    {
        // WHAT: Tests SystemHealthOverview property assignment and GetExamples method.
        // WHY:  Guarantees 100% line and branch coverage on high-level health models.
        // HOW:  Instantiates model directly and verifies GetExamples returned schema values.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SystemHealthOverview overview = new()
        {
            Status = "Healthy",
            TimestampUtc = now,
            Version = "1.0.0",
            Uptime = "1d 2h 3m 4s",
            UptimeSeconds = 93784.0,
            SetupRequired = false,
            AuthenticatedSession = false,
            Description = "Custom description"
        };

        Assert.Equal("Healthy", overview.Status);
        Assert.Equal(now, overview.TimestampUtc);
        Assert.Equal("1.0.0", overview.Version);
        Assert.Equal("1d 2h 3m 4s", overview.Uptime);
        Assert.Equal(93784.0, overview.UptimeSeconds);
        Assert.False(overview.SetupRequired);
        Assert.False(overview.AuthenticatedSession);
        Assert.Equal("Custom description", overview.Description);

        SystemHealthOverview example = overview.GetExamples();
        Assert.NotNull(example);
        Assert.Equal("Healthy", example.Status);
        Assert.False(example.AuthenticatedSession);
        Assert.False(example.SetupRequired);
    }

    /// <summary>
    /// Verifies that <see cref="GranularSystemHealth"/> and all child subsystem records correctly instantiate and generate OpenAPI examples.
    /// </summary>
    [Fact]
    public void GranularSystemHealth_ModelPropertiesAndExamples_BehaveAccurately()
    {
        // WHAT: Tests GranularSystemHealth and every subsystem diagnostic child record.
        // WHY:  Guarantees 100% line and branch coverage on granular health telemetry models.
        // HOW:  Instantiates records with explicit property values and verifies GetExamples().
        HealthUserInfo user = new()
        {
            Username = "researcher1",
            Role = "Researcher",
            AccessGate = "Api"
        };
        Assert.Equal("researcher1", user.Username);
        Assert.Equal("Researcher", user.Role);
        Assert.Equal("Api", user.AccessGate);

        DatabaseHealthInfo db = new()
        {
            Status = "Healthy",
            Provider = "SQLite",
            CanConnect = true,
            AppliedMigrationsCount = 2
        };
        Assert.Equal("Healthy", db.Status);
        Assert.Equal("SQLite", db.Provider);
        Assert.True(db.CanConnect);
        Assert.Equal(2, db.AppliedMigrationsCount);

        EventBusHealthInfo bus = new()
        {
            Status = "Healthy",
            IsInternalBusActive = true,
            TotalSubscribers = 5,
            ActiveSubscribers = new Dictionary<string, int> { ["TestEvent"] = 5 },
            IsValkeyBridgeConnected = false,
            ValkeyEndpoint = "none"
        };
        Assert.Equal("Healthy", bus.Status);
        Assert.True(bus.IsInternalBusActive);
        Assert.Equal(5, bus.TotalSubscribers);
        Assert.False(bus.IsValkeyBridgeConnected);
        Assert.Equal("none", bus.ValkeyEndpoint);

        CachingHealthInfo cache = new()
        {
            Status = "Healthy",
            IsDistributed = false,
            Mode = "In-Memory Fallback"
        };
        Assert.Equal("Healthy", cache.Status);
        Assert.False(cache.IsDistributed);
        Assert.Equal("In-Memory Fallback", cache.Mode);

        MemoryHealthInfo memory = new()
        {
            Status = "Healthy",
            HeapMemoryMb = 32.1,
            WorkingSetMb = 64.2,
            ServerGc = true,
            Gen0Collections = 10,
            Gen1Collections = 1,
            Gen2Collections = 0
        };
        Assert.Equal("Healthy", memory.Status);
        Assert.Equal(32.1, memory.HeapMemoryMb);
        Assert.Equal(64.2, memory.WorkingSetMb);
        Assert.True(memory.ServerGc);
        Assert.Equal(10, memory.Gen0Collections);
        Assert.Equal(1, memory.Gen1Collections);
        Assert.Equal(0, memory.Gen2Collections);

        StorageHealthInfo storage = new()
        {
            Status = "Healthy",
            IncomingDirectory = "/data",
            IncomingWritable = true,
            ExportDirectory = "/exports",
            ExportWritable = true,
            ParquetDirectory = "/parquet",
            ParquetWritable = true
        };
        Assert.Equal("Healthy", storage.Status);
        Assert.Equal("/data", storage.IncomingDirectory);
        Assert.True(storage.IncomingWritable);
        Assert.Equal("/exports", storage.ExportDirectory);
        Assert.True(storage.ExportWritable);
        Assert.Equal("/parquet", storage.ParquetDirectory);
        Assert.True(storage.ParquetWritable);

        SimulationHealthInfo sim = new()
        {
            Status = "Healthy",
            AcceleratorBackend = "CPU",
            TotalFramesParsed = 1000,
            AvailableCampaignsCount = 4
        };
        Assert.Equal("Healthy", sim.Status);
        Assert.Equal("CPU", sim.AcceleratorBackend);
        Assert.Equal(1000, sim.TotalFramesParsed);
        Assert.Equal(4, sim.AvailableCampaignsCount);

        ComplianceHealthInfo compliance = new()
        {
            Status = "Healthy",
            AuditLedgerHealthy = true,
            PqcProvider = "ML-KEM-768"
        };
        Assert.Equal("Healthy", compliance.Status);
        Assert.True(compliance.AuditLedgerHealthy);
        Assert.Equal("ML-KEM-768", compliance.PqcProvider);

        HealthSubsystemsInfo subsystems = new()
        {
            Database = db,
            EventBus = bus,
            Caching = cache,
            Memory = memory,
            Storage = storage,
            Simulations = sim,
            Compliance = compliance
        };

        GranularSystemHealth granular = new()
        {
            Status = "Healthy",
            TimestampUtc = DateTimeOffset.UtcNow,
            Version = "1.0.0",
            Uptime = "0d 1h 0m 0s",
            UptimeSeconds = 3600.0,
            SetupRequired = false,
            AuthenticatedSession = true,
            Description = "Granular diagnostic telemetry active.",
            User = user,
            Subsystems = subsystems
        };

        Assert.True(granular.AuthenticatedSession);
        Assert.Equal("researcher1", granular.User.Username);
        Assert.Equal("SQLite", granular.Subsystems.Database.Provider);

        GranularSystemHealth example = granular.GetExamples();
        Assert.NotNull(example);
        Assert.True(example.AuthenticatedSession);
        Assert.NotNull(example.User);
        Assert.NotNull(example.Subsystems);
        Assert.NotNull(example.Subsystems.Database);
        Assert.NotNull(example.Subsystems.EventBus);
        Assert.NotNull(example.Subsystems.Caching);
        Assert.NotNull(example.Subsystems.Memory);
        Assert.NotNull(example.Subsystems.Storage);
        Assert.NotNull(example.Subsystems.Simulations);
        Assert.NotNull(example.Subsystems.Compliance);
    }

    /// <summary>
    /// Verifies that GET /api/system/health unauthenticated returns HTTP 200 OK with <see cref="SystemHealthOverview"/>.
    /// </summary>
    [Fact]
    public async Task GetSystemHealth_Unauthenticated_ReturnsHighLevelOverview()
    {
        // WHAT: Invokes /api/system/health without an Authorization header.
        // WHY:  Confirms the endpoint is publicly accessible and returns high-level overview.
        // HOW:  Sends GET request using anonymous HttpClient and validates SystemHealthOverview.
        HttpResponseMessage response = await _anonymousClient.GetAsync("/api/system/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        SystemHealthOverview? result = await response.Content.ReadFromJsonAsync<SystemHealthOverview>();
        Assert.NotNull(result);
        Assert.Equal("Healthy", result.Status);
        Assert.False(result.AuthenticatedSession);
        Assert.False(result.SetupRequired);
        Assert.Equal("1.0.0", result.Version);
        Assert.False(string.IsNullOrWhiteSpace(result.Uptime));
        Assert.True(result.UptimeSeconds >= 0);
        Assert.Contains("Authenticate with a valid session token", result.Description);
    }

    /// <summary>
    /// Verifies that GET /healthz unauthenticated returns HTTP 200 OK for Docker container healthchecks.
    /// </summary>
    [Fact]
    public async Task GetHealthz_Unauthenticated_ReturnsHighLevelOverview()
    {
        // WHAT: Invokes /healthz alias without authentication.
        // WHY:  Verifies Docker and Kubernetes container runtime probes succeed unhindered.
        // HOW:  Sends GET request and verifies SystemHealthOverview payload.
        HttpResponseMessage response = await _anonymousClient.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        SystemHealthOverview? result = await response.Content.ReadFromJsonAsync<SystemHealthOverview>();
        Assert.NotNull(result);
        Assert.Equal("Healthy", result.Status);
        Assert.False(result.AuthenticatedSession);
    }

    /// <summary>
    /// Verifies that GET /health unauthenticated returns HTTP 200 OK.
    /// </summary>
    [Fact]
    public async Task GetHealth_Unauthenticated_ReturnsHighLevelOverview()
    {
        // WHAT: Invokes /health alias without authentication.
        // WHY:  Verifies universal health endpoint alias functions identically.
        // HOW:  Sends GET request and validates status code and model.
        HttpResponseMessage response = await _anonymousClient.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        SystemHealthOverview? result = await response.Content.ReadFromJsonAsync<SystemHealthOverview>();
        Assert.NotNull(result);
        Assert.Equal("Healthy", result.Status);
        Assert.False(result.AuthenticatedSession);
    }

    /// <summary>
    /// Verifies that GET /api/system/health with a valid Bearer token returns HTTP 200 OK with <see cref="GranularSystemHealth"/>.
    /// </summary>
    [Fact]
    public async Task GetSystemHealth_AuthenticatedBearer_ReturnsGranularSubsystemDiagnostics()
    {
        // WHAT: Invokes /api/system/health with a valid Bearer token.
        // WHY:  Confirms authenticated callers receive granular multi-subsystem diagnostic telemetry.
        // HOW:  Uses authenticated HttpClient and validates GranularSystemHealth and child subsystems.
        HttpResponseMessage response = await _authClient.GetAsync("/api/system/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GranularSystemHealth? result = await response.Content.ReadFromJsonAsync<GranularSystemHealth>();
        Assert.NotNull(result);
        Assert.Equal("Healthy", result.Status);
        Assert.True(result.AuthenticatedSession);
        Assert.NotNull(result.User);
        Assert.Equal("testadmin", result.User.Username);
        Assert.Equal("Administrator", result.User.Role);
        Assert.Equal("ApiWeb", result.User.AccessGate);

        Assert.NotNull(result.Subsystems);
        Assert.NotNull(result.Subsystems.Database);
        Assert.Equal("Healthy", result.Subsystems.Database.Status);
        Assert.True(result.Subsystems.Database.CanConnect);
        Assert.True(result.Subsystems.Database.AppliedMigrationsCount >= 0);

        Assert.NotNull(result.Subsystems.EventBus);
        Assert.Equal("Healthy", result.Subsystems.EventBus.Status);
        Assert.True(result.Subsystems.EventBus.IsInternalBusActive);
        Assert.NotNull(result.Subsystems.EventBus.ActiveSubscribers);

        Assert.NotNull(result.Subsystems.Caching);
        Assert.Equal("Healthy", result.Subsystems.Caching.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Subsystems.Caching.Mode));

        Assert.NotNull(result.Subsystems.Memory);
        Assert.Equal("Healthy", result.Subsystems.Memory.Status);
        Assert.True(result.Subsystems.Memory.HeapMemoryMb >= 0);
        Assert.True(result.Subsystems.Memory.WorkingSetMb >= 0);

        Assert.NotNull(result.Subsystems.Storage);
        Assert.Equal("Healthy", result.Subsystems.Storage.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Subsystems.Storage.IncomingDirectory));

        Assert.NotNull(result.Subsystems.Simulations);
        Assert.Equal("Healthy", result.Subsystems.Simulations.Status);
        Assert.Equal(4, result.Subsystems.Simulations.AvailableCampaignsCount);

        Assert.NotNull(result.Subsystems.Compliance);
        Assert.Equal("Healthy", result.Subsystems.Compliance.Status);
        Assert.True(result.Subsystems.Compliance.AuditLedgerHealthy);
    }

    /// <summary>
    /// Verifies that GET /healthz with a valid Bearer token returns <see cref="GranularSystemHealth"/>.
    /// </summary>
    [Fact]
    public async Task GetHealthz_AuthenticatedBearer_ReturnsGranularSubsystemDiagnostics()
    {
        // WHAT: Invokes /healthz with a valid Bearer token.
        // WHY:  Verifies alias endpoints also detect authenticated sessions and return granular diagnostics.
        // HOW:  Sends GET request with auth header and checks AuthenticatedSession property.
        HttpResponseMessage response = await _authClient.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GranularSystemHealth? result = await response.Content.ReadFromJsonAsync<GranularSystemHealth>();
        Assert.NotNull(result);
        Assert.True(result.AuthenticatedSession);
        Assert.Equal("testadmin", result.User.Username);
    }

    /// <summary>
    /// Verifies that GET /api/system/health with a weaver_session cookie returns <see cref="GranularSystemHealth"/>.
    /// </summary>
    [Fact]
    public async Task GetSystemHealth_CookieAuthenticated_ReturnsGranularSubsystemDiagnostics()
    {
        // WHAT: Invokes /api/system/health using a weaver_session cookie instead of an Authorization header.
        // WHY:  Verifies browser-based sessions automatically receive granular diagnostics.
        // HOW:  Sends GET request with Cookie header set to weaver_session={_adminToken}.
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/system/health");
        request.Headers.Add("Cookie", $"weaver_session={_adminToken}");

        HttpResponseMessage response = await _anonymousClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GranularSystemHealth? result = await response.Content.ReadFromJsonAsync<GranularSystemHealth>();
        Assert.NotNull(result);
        Assert.True(result.AuthenticatedSession);
        Assert.Equal("testadmin", result.User.Username);
    }

    /// <summary>
    /// Verifies that GET /api/system/health with an invalid Bearer token gracefully falls back to <see cref="SystemHealthOverview"/>.
    /// </summary>
    [Fact]
    public async Task GetSystemHealth_InvalidToken_FallsBackToHighLevelOverview()
    {
        // WHAT: Invokes /api/system/health with an invalid or expired token.
        // WHY:  Ensures that invalid tokens on public health endpoints do not return 401, but fall back to high-level overview.
        // HOW:  Passes invalid Bearer token and verifies HTTP 200 OK with AuthenticatedSession == false.
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/system/health");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "completely_invalid_or_expired_token");

        HttpResponseMessage response = await _anonymousClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        SystemHealthOverview? result = await response.Content.ReadFromJsonAsync<SystemHealthOverview>();
        Assert.NotNull(result);
        Assert.False(result.AuthenticatedSession);
        Assert.Equal("Healthy", result.Status);
    }

    /// <summary>
    /// Verifies that GET /api/system/health with a non-Bearer auth header falls back to <see cref="SystemHealthOverview"/>.
    /// </summary>
    [Fact]
    public async Task GetSystemHealth_NonBearerAuthHeader_FallsBackToHighLevelOverview()
    {
        // WHAT: Invokes /api/system/health with a Basic auth header.
        // WHY:  Covers branch where Authorization header is present but does not start with 'Bearer '.
        // HOW:  Passes Basic header and validates high-level overview fallback.
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/system/health");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", "dXNlcjpwYXNz");

        HttpResponseMessage response = await _anonymousClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        SystemHealthOverview? result = await response.Content.ReadFromJsonAsync<SystemHealthOverview>();
        Assert.NotNull(result);
        Assert.False(result.AuthenticatedSession);
    }

    /// <summary>
    /// Verifies that GET /api/system/health with an empty or whitespace cookie falls back to <see cref="SystemHealthOverview"/>.
    /// </summary>
    [Fact]
    public async Task GetSystemHealth_WhitespaceCookie_FallsBackToHighLevelOverview()
    {
        // WHAT: Invokes /api/system/health with a whitespace-only cookie value.
        // WHY:  Covers branch where cookie token is null or whitespace.
        // HOW:  Passes cookie 'weaver_session=  ' and asserts high-level overview.
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/system/health");
        request.Headers.Add("Cookie", "weaver_session=   ");

        HttpResponseMessage response = await _anonymousClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        SystemHealthOverview? result = await response.Content.ReadFromJsonAsync<SystemHealthOverview>();
        Assert.NotNull(result);
        Assert.False(result.AuthenticatedSession);
    }

    /// <summary>
    /// Verifies that GET /api/system/health respects XML format content negotiation.
    /// </summary>
    [Fact]
    public async Task GetSystemHealth_AcceptXml_ReturnsXmlFormattedHealth()
    {
        // WHAT: Requests /api/system/health with Accept: application/xml.
        // WHY:  Verifies multi-format content negotiation transpiles health payloads into valid XML.
        // HOW:  Sets Accept header and asserts application/xml response.
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/system/health");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));

        HttpResponseMessage response = await _anonymousClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.Contains("application/xml", response.Content.Headers.ContentType.MediaType);

        string xml = await response.Content.ReadAsStringAsync();
        Assert.Contains("<Response>", xml);
        Assert.Contains("<status>Healthy</status>", xml);
    }

    /// <summary>
    /// Verifies that GET /api/system/health respects YAML format content negotiation.
    /// </summary>
    [Fact]
    public async Task GetSystemHealth_AcceptYaml_ReturnsYamlFormattedHealth()
    {
        // WHAT: Requests /api/system/health with Accept: application/x-yaml.
        // WHY:  Verifies multi-format content negotiation transpiles health payloads into hyphenated YAML.
        // HOW:  Sets Accept header and asserts application/x-yaml response.
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/system/health");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-yaml"));

        HttpResponseMessage response = await _anonymousClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.Contains("application/x-yaml", response.Content.Headers.ContentType.MediaType);

        string yaml = await response.Content.ReadAsStringAsync();
        Assert.Contains("status: Healthy", yaml);
    }

    /// <summary>
    /// Verifies that <see cref="Program.HandleSystemHealthAsync"/> correctly indicates setup requirements when setup is pending.
    /// </summary>
    [Fact]
    public async Task HandleSystemHealthAsync_SetupRequired_ReturnsSetupRequiredDescription()
    {
        // WHAT: Directly invokes HandleSystemHealthAsync when IsSetupRequiredAsync returns true.
        // WHY:  Ensures full branch coverage for unauthenticated requests during first-launch setup pending state.
        // HOW:  Mocks IUserAuthenticationService to return true for IsSetupRequiredAsync.
        DefaultHttpContext context = new();
        Mock<IUserAuthenticationService> mockAuth = new();
        mockAuth.Setup(a => a.IsSetupRequiredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Mock<IConfigurationRepository> mockConfigRepo = new();
        mockConfigRepo.Setup(c => c.ActiveBackendName).Returns("SQLite");

        DatabaseMigrationRunner migrationRunner = new();
        Mock<IInternalEventBus> mockBus = new();
        Mock<IValkeyConnectionFactory> mockValkey = new();
        mockValkey.Setup(v => v.IsConnected).Returns(false);
        mockValkey.Setup(v => v.SanitizedEndpoint).Returns("none");

        Mock<IValkeyCacheService> mockCache = new();
        mockCache.Setup(c => c.IsDistributed).Returns(false);

        IConfiguration config = new ConfigurationBuilder().Build();
        Mock<ISimulationCampaignService> mockSim = new();
        mockSim.Setup(s => s.GetCampaignCatalog()).Returns(new List<SimulationCampaignInfo>());

        Mock<IComplianceAuditService> mockCompliance = new();
        mockCompliance.Setup(c => c.VerifyLedgerIntegrity()).Returns(new AuditLedgerVerificationResult { IsValid = true, TotalRecordsVerified = 0 });

        IResult result = await Program.HandleSystemHealthAsync(
            context,
            mockAuth.Object,
            mockConfigRepo.Object,
            migrationRunner,
            mockBus.Object,
            mockValkey.Object,
            mockCache.Object,
            config,
            mockSim.Object,
            mockCompliance.Object,
            CancellationToken.None);

        Assert.NotNull(result);
        // Extract SystemHealthOverview from Microsoft.AspNetCore.Http.HttpResults.Ok<SystemHealthOverview>
        var okResult = result as Microsoft.AspNetCore.Http.HttpResults.Ok<SystemHealthOverview>;
        Assert.NotNull(okResult);
        Assert.NotNull(okResult.Value);
        Assert.True(okResult.Value.SetupRequired);
        Assert.False(okResult.Value.AuthenticatedSession);
        Assert.Contains("Master Administrator initialization required", okResult.Value.Description);
    }

    /// <summary>
    /// Verifies that <see cref="Program.HandleSystemHealthAsync"/> picks up authenticated user identity from scoped <see cref="UserSessionState"/>.
    /// </summary>
    [Fact]
    public async Task HandleSystemHealthAsync_UserSessionState_ReturnsGranularWhenScopedSessionActive()
    {
        // WHAT: Directly invokes HandleSystemHealthAsync with CurrentUser omitted from Items but present in scoped UserSessionState.
        // WHY:  Ensures full branch coverage for scoped Blazor interactive session state resolution.
        // HOW:  Configures ServiceProvider with authenticated UserSessionState and invokes method.
        ServiceCollection services = new();
        UserSessionState sessionState = new();
        UserAccount testUser = new()
        {
            Username = "blazor_user",
            Role = UserRole.Researcher,
            AccessGate = AccessGate.Web
        };
        sessionState.SetUser(testUser);
        services.AddSingleton(sessionState);
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        DefaultHttpContext context = new()
        {
            RequestServices = serviceProvider
        };

        Mock<IUserAuthenticationService> mockAuth = new();
        mockAuth.Setup(a => a.IsSetupRequiredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        Mock<IConfigurationRepository> mockConfigRepo = new();
        mockConfigRepo.Setup(c => c.ActiveBackendName).Returns("PostgreSQL");

        DatabaseMigrationRunner migrationRunner = new();
        Mock<IInternalEventBus> mockBus = new();
        Mock<IValkeyConnectionFactory> mockValkey = new();
        mockValkey.Setup(v => v.IsConnected).Returns(true);
        mockValkey.Setup(v => v.SanitizedEndpoint).Returns("valkey:6379");

        Mock<IValkeyCacheService> mockCache = new();
        mockCache.Setup(c => c.IsDistributed).Returns(true);

        IConfiguration config = new ConfigurationBuilder().Build();
        Mock<ISimulationCampaignService> mockSim = new();
        mockSim.Setup(s => s.GetCampaignCatalog()).Returns(new List<SimulationCampaignInfo>());

        Mock<IComplianceAuditService> mockCompliance = new();
        mockCompliance.Setup(c => c.VerifyLedgerIntegrity()).Returns(new AuditLedgerVerificationResult { IsValid = false, TotalRecordsVerified = 0 });

        IResult result = await Program.HandleSystemHealthAsync(
            context,
            mockAuth.Object,
            mockConfigRepo.Object,
            migrationRunner,
            mockBus.Object,
            mockValkey.Object,
            mockCache.Object,
            config,
            mockSim.Object,
            mockCompliance.Object,
            CancellationToken.None);

        Assert.NotNull(result);
        var okResult = result as Microsoft.AspNetCore.Http.HttpResults.Ok<GranularSystemHealth>;
        Assert.NotNull(okResult);
        Assert.NotNull(okResult.Value);
        Assert.True(okResult.Value.AuthenticatedSession);
        Assert.Equal("blazor_user", okResult.Value.User.Username);
        Assert.Equal("Researcher", okResult.Value.User.Role);
        Assert.Equal("Distributed (Valkey)", okResult.Value.Subsystems.Caching.Mode);
        Assert.False(okResult.Value.Subsystems.Compliance.AuditLedgerHealthy);
    }
}
