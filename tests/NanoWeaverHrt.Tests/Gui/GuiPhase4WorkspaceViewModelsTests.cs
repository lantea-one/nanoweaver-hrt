using Moq;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Federation;
using NanoWeaverHrt.Gui.Core.ViewModels;
using NanoWeaverHrt.Hardware.Benchmarking;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Access;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Services.Stack;
using Xunit;

namespace NanoWeaverHrt.Tests.Gui;

/// <summary>
/// Unit tests for <see cref="NodeAdministrationViewModel"/>, <see cref="PatientClinicalWorkspaceViewModel"/>,
/// and <see cref="FederationAndDatasetWorkspaceViewModel"/>.
/// </summary>
public sealed class GuiPhase4WorkspaceViewModelsTests
{
    /// <summary>
    /// Creates a dummy instance profile for testing purposes.
    /// </summary>
    /// <param name="id">Instance identifier.</param>
    /// <returns>A populated <see cref="InstanceProfile"/> record.</returns>
    private static InstanceProfile CreateProfile(string id = "inst-001") =>
        new(id, "Test Node", "http://127.0.0.1:5000", true, "admin", "Administrator", "Ed25519", "PUBKEY", "ENVELOPE", true, 15, DateTimeOffset.UtcNow, null);

    /// <summary>
    /// Creates a sample benchmark report for testing.
    /// </summary>
    /// <returns>A populated <see cref="AcceleratorBenchmarkReport"/>.</returns>
    private static AcceleratorBenchmarkReport CreateBenchmarkReport() => new()
    {
        DeviceName = "GPU",
        DriverVersion = "1.0",
        SinglePrecisionGflops = 1000.0,
        DoublePrecisionGflops = 100.0,
        MemoryBandwidthGigaBytesPerSecond = 100.0,
        TemperatureCelsius = 45.0,
        PowerUsageWatts = 150.0,
        Readiness = HardwareReadinessScore.Go,
        BenchmarkTimestampUtc = DateTimeOffset.UtcNow,
        Recommendations = []
    };

    /// <summary>
    /// Creates a sample configuration item for testing.
    /// </summary>
    /// <returns>A populated <see cref="ConfigurationItem"/>.</returns>
    private static ConfigurationItem CreateConfigItem() => new()
    {
        Key = "k",
        Value = "v",
        DefaultValue = "d",
        Description = "desc",
        Source = ConfigurationSource.DefaultFallback,
        Category = "General"
    };

    /// <summary>
    /// Creates a sample stack service info for testing.
    /// </summary>
    /// <returns>A populated <see cref="StackServiceInfo"/>.</returns>
    private static StackServiceInfo CreateStackService() => new()
    {
        ServiceId = "svc1",
        DisplayName = "Service 1",
        RoleDescription = "Role",
        EndpointOrLocation = "http://127.0.0.1:5000",
        Health = ServiceHealthState.Healthy,
        StatusMessage = "OK",
        TechnologyStack = "C#"
    };

    /// <summary>
    /// Creates a sample system health overview for testing.
    /// </summary>
    /// <returns>A populated <see cref="SystemHealthOverview"/>.</returns>
    private static SystemHealthOverview CreateHealthOverview() => new()
    {
        Status = "Healthy",
        TimestampUtc = DateTimeOffset.UtcNow,
        Version = "1.0.0",
        Uptime = "0d 4h 12m 30s",
        UptimeSeconds = 15150.0,
        SetupRequired = false,
        AuthenticatedSession = true,
        Description = "All subsystems operational"
    };

    /// <summary>
    /// Creates a sample granular system health for testing.
    /// </summary>
    /// <returns>A populated <see cref="GranularSystemHealth"/>.</returns>
    private static GranularSystemHealth CreateGranularHealth() => new()
    {
        Status = "Healthy",
        TimestampUtc = DateTimeOffset.UtcNow,
        Version = "1.0.0",
        Uptime = "0d 4h 12m 30s",
        UptimeSeconds = 15150.0,
        SetupRequired = false,
        AuthenticatedSession = true,
        Description = "All subsystems operational",
        User = new HealthUserInfo { Username = "admin", Role = "Administrator", AccessGate = "ApiWeb" },
        Subsystems = new HealthSubsystemsInfo
        {
            Database = new DatabaseHealthInfo { Status = "Healthy", Provider = "PostgreSQL", CanConnect = true, AppliedMigrationsCount = 1 },
            EventBus = new EventBusHealthInfo { Status = "Healthy", IsInternalBusActive = true, TotalSubscribers = 1, ActiveSubscribers = [], IsValkeyBridgeConnected = true, ValkeyEndpoint = "valkey:6379" },
            Caching = new CachingHealthInfo { Status = "Healthy", IsDistributed = true, Mode = "Valkey" },
            Memory = new MemoryHealthInfo { Status = "Healthy", HeapMemoryMb = 50.0, WorkingSetMb = 100.0, ServerGc = true, Gen0Collections = 1, Gen1Collections = 0, Gen2Collections = 0 },
            Storage = new StorageHealthInfo { Status = "Healthy", IncomingDirectory = "/tmp", IncomingWritable = true, ExportDirectory = "/tmp", ExportWritable = true, ParquetDirectory = "/tmp", ParquetWritable = true },
            Simulations = new SimulationHealthInfo { Status = "Healthy", AcceleratorBackend = "Level Zero", TotalFramesParsed = 100, AvailableCampaignsCount = 1 },
            Compliance = new ComplianceHealthInfo { Status = "Healthy", AuditLedgerHealthy = true, PqcProvider = "ML-DSA-65" }
        }
    };

    /// <summary>
    /// Creates a sample event bus telemetry for testing.
    /// </summary>
    /// <returns>A populated <see cref="EventBusTelemetry"/>.</returns>
    private static EventBusTelemetry CreateEventBusTelemetry() => new()
    {
        IsInternalBusActive = true,
        IsValkeyConnected = true,
        IsDistributedCache = true,
        ValkeyEndpoint = "valkey:6379",
        OutboundEventsCount = 10,
        InboundEventsCount = 5,
        ActiveSubscribers = []
    };

    /// <summary>
    /// Creates a sample share key record for testing.
    /// </summary>
    /// <returns>A populated <see cref="ShareKeyRecord"/>.</returns>
    private static ShareKeyRecord CreateShareKey() => new()
    {
        TokenId = "tok_1",
        KeyToken = "nanoweaver_tok_1",
        Label = "Test Key",
        CreatedAtUtc = DateTime.UtcNow,
        ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
        Permissions = "FullCollaborator"
    };

    /// <summary>
    /// Creates a sample share key consume result for testing.
    /// </summary>
    /// <returns>A populated <see cref="ShareKeyConsumeResult"/>.</returns>
    private static ShareKeyConsumeResult CreateShareKeyConsumeResult() => new()
    {
        Success = true,
        Message = "Success"
    };

    /// <summary>
    /// Creates a sample peer key record for testing.
    /// </summary>
    /// <returns>A populated <see cref="PeerKeyRecord"/>.</returns>
    private static PeerKeyRecord CreatePeerKey() => new()
    {
        KeyId = "peer_1",
        Label = "Peer 1",
        Algorithm = KeyAlgorithm.Ed25519,
        PublicKeyBase64 = "pk",
        RegisteredBy = "admin",
        RegisteredAtUtc = DateTimeOffset.UtcNow
    };

    /// <summary>
    /// Creates a sample peer key list response for testing.
    /// </summary>
    /// <returns>A populated <see cref="PeerKeyListResponse"/>.</returns>
    private static PeerKeyListResponse CreatePeerKeyListResponse() => new()
    {
        LocalNode = new LocalDatasetSigningKey("local_key", KeyAlgorithm.Ed25519, "pk"),
        Peers = [CreatePeerKey()]
    };

    /// <summary>
    /// Creates a sample dataset manifest for testing.
    /// </summary>
    /// <returns>A populated <see cref="DatasetManifest"/>.</returns>
    private static DatasetManifest CreateDatasetManifest() => new()
    {
        DatasetName = "ds1",
        TimestampUtc = "2026-11-09T00:00:00Z",
        ArchiveFileName = "ds1.tar.gz",
        Sha256Checksum = "abc",
        SizeInBytes = 1024
    };

    /// <summary>
    /// Creates a sample patient import request for testing.
    /// </summary>
    /// <returns>A populated <see cref="CreatePatientImportRequest"/>.</returns>
    private static CreatePatientImportRequest CreateImportReq() => new()
    {
        FileName = "dna.fa",
        SequenceOrData = "ATCG"
    };

    /// <summary>
    /// Creates a sample patient clinical result request for testing.
    /// </summary>
    /// <returns>A populated <see cref="CreatePatientResultRequest"/>.</returns>
    private static CreatePatientResultRequest CreateResultReq() => new()
    {
        ResultType = "EndocrineKinetics",
        Title = "Title",
        Summary = "Summary"
    };

    // =========================================================================
    // NodeAdministrationViewModel Tests
    // =========================================================================

    /// <summary>
    /// Verifies constructor guard against null client.
    /// </summary>
    [Fact]
    public void NodeAdministrationViewModel_Constructor_NullClient_Throws()
    {
        // WHAT: Tests null client validation in NodeAdministrationViewModel.
        // WHY:  Guarantees fail-fast on missing dependency.
        // HOW:  Passes null client and asserts ArgumentNullException.
        Assert.Throws<ArgumentNullException>(() => new NodeAdministrationViewModel(null!));
    }

    /// <summary>
    /// Verifies property change notifications and equality suppression in <see cref="NodeAdministrationViewModel"/>.
    /// </summary>
    [Fact]
    public void NodeAdministrationViewModel_Properties_FirePropertyChanged()
    {
        // WHAT: Tests all observable properties of NodeAdministrationViewModel.
        // WHY:  Ensures data binding invariants are satisfied.
        // HOW:  Mutates each property, checks notification list, and sets same value to verify suppression.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var vm = new NodeAdministrationViewModel(clientMock.Object);

        var fired = new List<string>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName!);

        var profile = CreateProfile();
        vm.ActiveProfile = profile;
        Assert.Same(profile, vm.ActiveProfile);
        Assert.Contains(nameof(vm.ActiveProfile), fired);
        fired.Clear();
        vm.ActiveProfile = profile;
        Assert.Empty(fired);

        vm.ActiveBearerToken = "token-admin";
        Assert.Equal("token-admin", vm.ActiveBearerToken);
        Assert.Contains(nameof(vm.ActiveBearerToken), fired);
        fired.Clear();
        vm.ActiveBearerToken = "token-admin";
        Assert.Empty(fired);

        Assert.False(vm.IsBusy);
        Assert.Null(vm.ErrorMessage);
        Assert.Null(vm.LastSetupStatus);
        Assert.Null(vm.Users);
        Assert.Null(vm.CurrentUser);
        Assert.Null(vm.Configurations);
        Assert.Null(vm.StackServices);
        Assert.Null(vm.LastBenchmarkReport);
        Assert.Null(vm.LastGranularHealth);
        Assert.Null(vm.LastSystemHealth);
        Assert.Null(vm.LastEventBusMetrics);
    }

    /// <summary>
    /// Verifies session resolution failures when profile or token is missing in <see cref="NodeAdministrationViewModel"/>.
    /// </summary>
    [Fact]
    public async Task NodeAdministrationViewModel_SessionResolution_ThrowsWhenMissing()
    {
        // WHAT: Tests missing profile and missing token branches in NodeAdministrationViewModel.
        // WHY:  Guarantees operations cannot be performed without necessary credentials.
        // HOW:  Calls methods on VMs without profile or token and asserts InvalidOperationException.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();

        // No profile
        var noProfileVm = new NodeAdministrationViewModel(clientMock.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => noProfileVm.GetSetupStatusAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => noProfileVm.ListUsersAsync());

        // Has profile but no token
        var noTokenVm = new NodeAdministrationViewModel(clientMock.Object, () => profile, () => null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => noTokenVm.ListUsersAsync());

        // Whitespace token
        var wsTokenVm = new NodeAdministrationViewModel(clientMock.Object, () => profile, () => "   ");
        await Assert.ThrowsAsync<InvalidOperationException>(() => wsTokenVm.ListUsersAsync());

        // Has profile but null token provider callback
        var nullProviderVm = new NodeAdministrationViewModel(clientMock.Object, () => profile);
        await Assert.ThrowsAsync<InvalidOperationException>(() => nullProviderVm.ListUsersAsync());

        // Explicit ActiveBearerToken takes precedence over token provider
        clientMock.Setup(c => c.ListUsersAsync(profile, "explicit-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UserAccount>());
        nullProviderVm.ActiveBearerToken = "explicit-token";
        await nullProviderVm.ListUsersAsync();
        Assert.NotNull(nullProviderVm.Users);
    }

    /// <summary>
    /// Verifies setup methods in <see cref="NodeAdministrationViewModel"/>.
    /// </summary>
    [Fact]
    public async Task NodeAdministrationViewModel_SetupMethods_AllPaths()
    {
        // WHAT: Tests GetSetupStatusAsync and InitializeSetupAdminAsync happy and fault paths.
        // WHY:  Covers setup bootstrapping logic in the administration ViewModel.
        // HOW:  Mocks client responses, asserts property updates, and verifies exception handling.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();
        var vm = new NodeAdministrationViewModel(clientMock.Object, () => profile);

        // GetSetupStatusAsync success
        var setupStatus = new SetupStatusResponse { SetupRequired = true };
        clientMock.Setup(c => c.GetSetupStatusAsync(profile, It.IsAny<CancellationToken>()))
            .ReturnsAsync(setupStatus);

        var statusResult = await vm.GetSetupStatusAsync();
        Assert.Same(setupStatus, statusResult);
        Assert.Same(setupStatus, vm.LastSetupStatus);
        Assert.False(vm.IsBusy);
        Assert.Null(vm.ErrorMessage);

        // GetSetupStatusAsync fault
        clientMock.Setup(c => c.GetSetupStatusAsync(profile, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Setup status check failed"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => vm.GetSetupStatusAsync());
        Assert.Equal("Setup status check failed", vm.ErrorMessage);
        Assert.False(vm.IsBusy);

        // InitializeSetupAdminAsync null guard
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.InitializeSetupAdminAsync(null!));

        // InitializeSetupAdminAsync success
        var setupReq = new InitialSetupRequest { AdminUsername = "root" };
        var authResp = new AuthTokenResponse { Token = "jwt-root" };
        clientMock.Setup(c => c.InitializeSetupAdminAsync(profile, setupReq, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authResp);

        var authResult = await vm.InitializeSetupAdminAsync(setupReq);
        Assert.Same(authResp, authResult);
        Assert.False(vm.IsBusy);

        // InitializeSetupAdminAsync fault
        clientMock.Setup(c => c.InitializeSetupAdminAsync(profile, setupReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Initialization failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.InitializeSetupAdminAsync(setupReq));
        Assert.Equal("Initialization failed", vm.ErrorMessage);
    }

    /// <summary>
    /// Verifies user management methods in <see cref="NodeAdministrationViewModel"/>.
    /// </summary>
    [Fact]
    public async Task NodeAdministrationViewModel_UserMethods_AllPaths()
    {
        // WHAT: Tests ListUsersAsync, CreateUserAsync, DeleteUserAsync, CryptoShredUserAsync, GetCurrentUserAsync, LogoutAsync.
        // WHY:  Covers all user lifecycle branches in the administration ViewModel.
        // HOW:  Validates null/empty checks, mock responses, and exception paths.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();
        var vm = new NodeAdministrationViewModel(clientMock.Object, () => profile, () => "token-users");

        // ListUsersAsync
        IReadOnlyList<UserAccount> usersList = [new UserAccount().GetExamples()];
        clientMock.Setup(c => c.ListUsersAsync(profile, "token-users", It.IsAny<CancellationToken>()))
            .ReturnsAsync(usersList);

        var usersResult = await vm.ListUsersAsync();
        Assert.Same(usersList, usersResult);
        Assert.Same(usersList, vm.Users);

        clientMock.Setup(c => c.ListUsersAsync(profile, "token-users", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Users fetch failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ListUsersAsync());
        Assert.Equal("Users fetch failed", vm.ErrorMessage);

        // CreateUserAsync
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.CreateUserAsync(null!));
        var createReq = new CreateUserRequest { Username = "alice" };
        var createdUser = new UserAccount().GetExamples();
        clientMock.Setup(c => c.CreateUserAsync(profile, "token-users", createReq, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdUser);

        var userCreated = await vm.CreateUserAsync(createReq);
        Assert.Same(createdUser, userCreated);

        clientMock.Setup(c => c.CreateUserAsync(profile, "token-users", createReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Creation failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CreateUserAsync(createReq));

        // DeleteUserAsync
        await Assert.ThrowsAsync<ArgumentException>(() => vm.DeleteUserAsync(string.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.DeleteUserAsync("   "));

        clientMock.Setup(c => c.DeleteUserAsync(profile, "token-users", "alice", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await vm.DeleteUserAsync("alice");

        clientMock.Setup(c => c.DeleteUserAsync(profile, "token-users", "alice", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Deletion failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeleteUserAsync("alice"));

        // CryptoShredUserAsync
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.CryptoShredUserAsync(null!));
        var shredReq = new CryptoShredRequest { SubjectId = "alice" };
        clientMock.Setup(c => c.CryptoShredUserAsync(profile, "token-users", shredReq, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await vm.CryptoShredUserAsync(shredReq);

        clientMock.Setup(c => c.CryptoShredUserAsync(profile, "token-users", shredReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Shred failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CryptoShredUserAsync(shredReq));

        // GetCurrentUserAsync
        clientMock.Setup(c => c.GetCurrentUserAsync(profile, "token-users", It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdUser);
        var meResult = await vm.GetCurrentUserAsync();
        Assert.Same(createdUser, meResult);
        Assert.Same(createdUser, vm.CurrentUser);

        clientMock.Setup(c => c.GetCurrentUserAsync(profile, "token-users", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Me failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.GetCurrentUserAsync());

        // LogoutAsync
        clientMock.Setup(c => c.LogoutAsync(profile, "token-users", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await vm.LogoutAsync();
        Assert.Null(vm.CurrentUser);

        clientMock.Setup(c => c.LogoutAsync(profile, "token-users", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Logout failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.LogoutAsync());
    }

    /// <summary>
    /// Verifies configuration and stack services methods in <see cref="NodeAdministrationViewModel"/>.
    /// </summary>
    [Fact]
    public async Task NodeAdministrationViewModel_ConfigAndServices_AllPaths()
    {
        // WHAT: Tests GetAllConfigurationsAsync, SetConfigurationOverrideAsync, DeleteConfigurationOverrideAsync, GetStackServicesAsync, PingStackServiceAsync.
        // WHY:  Covers configuration and container service oversight methods.
        // HOW:  Validates null/empty checks, mock responses, and exception paths.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();
        var vm = new NodeAdministrationViewModel(clientMock.Object, () => profile, () => "token-cfg");

        // GetAllConfigurationsAsync
        IReadOnlyList<ConfigurationItem> configs = [CreateConfigItem()];
        clientMock.Setup(c => c.GetAllConfigurationsAsync(profile, "token-cfg", It.IsAny<CancellationToken>()))
            .ReturnsAsync(configs);

        var cfgResult = await vm.GetAllConfigurationsAsync();
        Assert.Same(configs, cfgResult);
        Assert.Same(configs, vm.Configurations);

        clientMock.Setup(c => c.GetAllConfigurationsAsync(profile, "token-cfg", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Cfg fetch failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.GetAllConfigurationsAsync());

        // SetConfigurationOverrideAsync
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.SetConfigurationOverrideAsync(null!));
        var overrideReq = new ConfigOverrideRequest { Key = "k", Value = "v" };
        clientMock.Setup(c => c.SetConfigurationOverrideAsync(profile, "token-cfg", overrideReq, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await vm.SetConfigurationOverrideAsync(overrideReq);

        clientMock.Setup(c => c.SetConfigurationOverrideAsync(profile, "token-cfg", overrideReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Override failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SetConfigurationOverrideAsync(overrideReq));

        // DeleteConfigurationOverrideAsync
        await Assert.ThrowsAsync<ArgumentException>(() => vm.DeleteConfigurationOverrideAsync(string.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.DeleteConfigurationOverrideAsync("   "));
        clientMock.Setup(c => c.DeleteConfigurationOverrideAsync(profile, "token-cfg", "k", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await vm.DeleteConfigurationOverrideAsync("k");

        clientMock.Setup(c => c.DeleteConfigurationOverrideAsync(profile, "token-cfg", "k", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Delete override failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeleteConfigurationOverrideAsync("k"));

        // GetStackServicesAsync
        IReadOnlyList<StackServiceInfo> services = [CreateStackService()];
        clientMock.Setup(c => c.GetStackServicesAsync(profile, "token-cfg", It.IsAny<CancellationToken>()))
            .ReturnsAsync(services);

        var svcResult = await vm.GetStackServicesAsync();
        Assert.Same(services, svcResult);
        Assert.Same(services, vm.StackServices);

        clientMock.Setup(c => c.GetStackServicesAsync(profile, "token-cfg", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Services fetch failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.GetStackServicesAsync());

        // PingStackServiceAsync
        await Assert.ThrowsAsync<ArgumentException>(() => vm.PingStackServiceAsync(string.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.PingStackServiceAsync("   "));
        clientMock.Setup(c => c.PingStackServiceAsync(profile, "token-cfg", "svc1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(services[0]);

        var pingResult = await vm.PingStackServiceAsync("svc1");
        Assert.Same(services[0], pingResult);

        clientMock.Setup(c => c.PingStackServiceAsync(profile, "token-cfg", "svc1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Ping failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.PingStackServiceAsync("svc1"));
    }

    /// <summary>
    /// Verifies benchmarking, system health, and event bus methods in <see cref="NodeAdministrationViewModel"/>.
    /// </summary>
    [Fact]
    public async Task NodeAdministrationViewModel_BenchmarkingAndHealth_AllPaths()
    {
        // WHAT: Tests GetLatestBenchmarkAsync, RunBenchmarkAsync, GetGranularSystemHealthAsync, GetSystemHealthAsync, GetHealthzAsync, GetHealthAsync, GetEventBusMetricsAsync.
        // WHY:  Covers diagnostic and performance measurement methods.
        // HOW:  Validates mock returns, state updates, and exception reporting across all methods.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();
        var vm = new NodeAdministrationViewModel(clientMock.Object, () => profile, () => "token-bench");

        // Benchmarks
        var benchReport = CreateBenchmarkReport();
        clientMock.Setup(c => c.GetLatestBenchmarkAsync(profile, "token-bench", It.IsAny<CancellationToken>()))
            .ReturnsAsync(benchReport);
        var latestBench = await vm.GetLatestBenchmarkAsync();
        Assert.Same(benchReport, latestBench);
        Assert.Same(benchReport, vm.LastBenchmarkReport);

        clientMock.Setup(c => c.GetLatestBenchmarkAsync(profile, "token-bench", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Latest benchmark failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.GetLatestBenchmarkAsync());

        clientMock.Setup(c => c.RunBenchmarkAsync(profile, "token-bench", It.IsAny<CancellationToken>()))
            .ReturnsAsync(benchReport);
        var runBench = await vm.RunBenchmarkAsync();
        Assert.Same(benchReport, runBench);

        clientMock.Setup(c => c.RunBenchmarkAsync(profile, "token-bench", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Run benchmark failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RunBenchmarkAsync());

        // Granular health
        var granularHealth = CreateGranularHealth();
        clientMock.Setup(c => c.GetGranularSystemHealthAsync(profile, "token-bench", It.IsAny<CancellationToken>()))
            .ReturnsAsync(granularHealth);
        var granResult = await vm.GetGranularSystemHealthAsync();
        Assert.Same(granularHealth, granResult);
        Assert.Same(granularHealth, vm.LastGranularHealth);

        clientMock.Setup(c => c.GetGranularSystemHealthAsync(profile, "token-bench", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Granular health failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.GetGranularSystemHealthAsync());

        // System health overview
        var healthOverview = CreateHealthOverview();
        clientMock.Setup(c => c.GetSystemHealthAsync(profile, "token-bench", It.IsAny<CancellationToken>()))
            .ReturnsAsync(healthOverview);
        var sysHealth = await vm.GetSystemHealthAsync();
        Assert.Same(healthOverview, sysHealth);
        Assert.Same(healthOverview, vm.LastSystemHealth);

        clientMock.Setup(c => c.GetSystemHealthAsync(profile, "token-bench", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("System health failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.GetSystemHealthAsync());

        // Healthz
        clientMock.Setup(c => c.GetHealthzAsync(profile, It.IsAny<CancellationToken>()))
            .ReturnsAsync(healthOverview);
        var healthzResult = await vm.GetHealthzAsync();
        Assert.Same(healthOverview, healthzResult);

        clientMock.Setup(c => c.GetHealthzAsync(profile, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Healthz failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.GetHealthzAsync());

        // Health
        clientMock.Setup(c => c.GetHealthAsync(profile, It.IsAny<CancellationToken>()))
            .ReturnsAsync(healthOverview);
        var healthResult = await vm.GetHealthAsync();
        Assert.Same(healthOverview, healthResult);

        clientMock.Setup(c => c.GetHealthAsync(profile, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Health failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.GetHealthAsync());

        // Event bus metrics
        var eventBus = CreateEventBusTelemetry();
        clientMock.Setup(c => c.GetEventBusMetricsAsync(profile, "token-bench", It.IsAny<CancellationToken>()))
            .ReturnsAsync(eventBus);
        var busResult = await vm.GetEventBusMetricsAsync();
        Assert.Same(eventBus, busResult);
        Assert.Same(eventBus, vm.LastEventBusMetrics);

        clientMock.Setup(c => c.GetEventBusMetricsAsync(profile, "token-bench", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Event bus failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.GetEventBusMetricsAsync());
    }

    // =========================================================================
    // PatientClinicalWorkspaceViewModel Tests
    // =========================================================================

    /// <summary>
    /// Verifies constructor guard against null client.
    /// </summary>
    [Fact]
    public void PatientClinicalWorkspaceViewModel_Constructor_NullClient_Throws()
    {
        // WHAT: Tests null client validation in PatientClinicalWorkspaceViewModel.
        // WHY:  Guarantees fail-fast on missing dependency.
        // HOW:  Passes null client and asserts ArgumentNullException.
        Assert.Throws<ArgumentNullException>(() => new PatientClinicalWorkspaceViewModel(null!));
    }

    /// <summary>
    /// Verifies property change notifications and equality suppression in <see cref="PatientClinicalWorkspaceViewModel"/>.
    /// </summary>
    [Fact]
    public void PatientClinicalWorkspaceViewModel_Properties_FirePropertyChanged()
    {
        // WHAT: Tests observable properties of PatientClinicalWorkspaceViewModel.
        // WHY:  Ensures data binding invariants are satisfied.
        // HOW:  Mutates each property, checks notification list, and sets same value to verify suppression.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var vm = new PatientClinicalWorkspaceViewModel(clientMock.Object);

        var fired = new List<string>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName!);

        var profile = CreateProfile();
        vm.ActiveProfile = profile;
        Assert.Same(profile, vm.ActiveProfile);
        Assert.Contains(nameof(vm.ActiveProfile), fired);
        fired.Clear();
        vm.ActiveProfile = profile;
        Assert.Empty(fired);

        vm.ActiveBearerToken = "token-pat";
        Assert.Equal("token-pat", vm.ActiveBearerToken);
        Assert.Contains(nameof(vm.ActiveBearerToken), fired);
        fired.Clear();
        vm.ActiveBearerToken = "token-pat";
        Assert.Empty(fired);

        Assert.False(vm.IsBusy);
        Assert.Null(vm.ErrorMessage);
        Assert.Null(vm.ActiveUserProfile);
        Assert.Null(vm.UserAddresses);
        Assert.Null(vm.UserContacts);
        Assert.Null(vm.LastDnaPackage);
        Assert.Null(vm.PatientDnaRecords);
        Assert.Null(vm.PatientImports);
        Assert.Null(vm.PatientClinicalResults);
        Assert.Null(vm.SubjectGrants);
        Assert.Null(vm.SubjectGroups);
        Assert.Null(vm.AccessibleSubjects);
    }

    /// <summary>
    /// Verifies session resolution failures when profile or token is missing in <see cref="PatientClinicalWorkspaceViewModel"/>.
    /// </summary>
    [Fact]
    public async Task PatientClinicalWorkspaceViewModel_SessionResolution_ThrowsWhenMissing()
    {
        // WHAT: Tests missing profile and missing token branches in PatientClinicalWorkspaceViewModel.
        // WHY:  Guarantees clinical operations require complete session credentials.
        // HOW:  Calls methods on VMs without profile or token and asserts InvalidOperationException.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();

        var noProfileVm = new PatientClinicalWorkspaceViewModel(clientMock.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => noProfileVm.GetUserProfileAsync("alice"));

        var noTokenVm = new PatientClinicalWorkspaceViewModel(clientMock.Object, () => profile, () => null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => noTokenVm.GetUserProfileAsync("alice"));

        var wsTokenVm = new PatientClinicalWorkspaceViewModel(clientMock.Object, () => profile, () => "  ");
        await Assert.ThrowsAsync<InvalidOperationException>(() => wsTokenVm.GetUserProfileAsync("alice"));

        // Has profile but null token provider callback
        var nullProviderVm = new PatientClinicalWorkspaceViewModel(clientMock.Object, () => profile);
        await Assert.ThrowsAsync<InvalidOperationException>(() => nullProviderVm.GetUserProfileAsync("alice"));

        // Explicit ActiveBearerToken takes precedence over token provider
        clientMock.Setup(c => c.GetUserProfileAsync(profile, "explicit-token", "alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserProfile().GetExamples());
        nullProviderVm.ActiveBearerToken = "explicit-token";
        await nullProviderVm.GetUserProfileAsync("alice");
        Assert.NotNull(nullProviderVm.ActiveUserProfile);
    }

    /// <summary>
    /// Verifies demographic and contact methods in <see cref="PatientClinicalWorkspaceViewModel"/>.
    /// </summary>
    [Fact]
    public async Task PatientClinicalWorkspaceViewModel_DemographicAndContacts_AllPaths()
    {
        // WHAT: Tests GetUserProfileAsync, UpdateUserProfileAsync, ListUserAddressesAsync, AddUserAddressAsync, DeleteUserAddressAsync, ListUserContactsAsync, AddUserContactAsync, DeleteUserContactAsync.
        // WHY:  Covers all demographic and contact management branches.
        // HOW:  Validates string guards, null guards, mock responses, and exception paths.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();
        var vm = new PatientClinicalWorkspaceViewModel(clientMock.Object, () => profile, () => "token-demog");

        // Profile
        await Assert.ThrowsAsync<ArgumentException>(() => vm.GetUserProfileAsync(string.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.GetUserProfileAsync("  "));
        var userProfile = new UserProfile().GetExamples();
        clientMock.Setup(c => c.GetUserProfileAsync(profile, "token-demog", "alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(userProfile);
        var profResult = await vm.GetUserProfileAsync("alice");
        Assert.Same(userProfile, profResult);
        Assert.Same(userProfile, vm.ActiveUserProfile);

        clientMock.Setup(c => c.GetUserProfileAsync(profile, "token-demog", "alice", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Profile error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.GetUserProfileAsync("alice"));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.UpdateUserProfileAsync(string.Empty, userProfile));
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.UpdateUserProfileAsync("alice", null!));
        clientMock.Setup(c => c.UpdateUserProfileAsync(profile, "token-demog", "alice", userProfile, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userProfile);
        var updatedProf = await vm.UpdateUserProfileAsync("alice", userProfile);
        Assert.Same(userProfile, updatedProf);

        clientMock.Setup(c => c.UpdateUserProfileAsync(profile, "token-demog", "alice", userProfile, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Update profile error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.UpdateUserProfileAsync("alice", userProfile));

        // Addresses
        await Assert.ThrowsAsync<ArgumentException>(() => vm.ListUserAddressesAsync(string.Empty));
        IReadOnlyList<PatientAddress> addresses = [new PatientAddress().GetExamples()];
        clientMock.Setup(c => c.ListUserAddressesAsync(profile, "token-demog", "alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(addresses);
        var addrResult = await vm.ListUserAddressesAsync("alice");
        Assert.Same(addresses, addrResult);
        Assert.Same(addresses, vm.UserAddresses);

        clientMock.Setup(c => c.ListUserAddressesAsync(profile, "token-demog", "alice", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("List addresses error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ListUserAddressesAsync("alice"));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.AddUserAddressAsync(string.Empty, addresses[0]));
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.AddUserAddressAsync("alice", null!));
        clientMock.Setup(c => c.AddUserAddressAsync(profile, "token-demog", "alice", addresses[0], It.IsAny<CancellationToken>()))
            .ReturnsAsync(addresses[0]);
        var addedAddr = await vm.AddUserAddressAsync("alice", addresses[0]);
        Assert.Same(addresses[0], addedAddr);

        clientMock.Setup(c => c.AddUserAddressAsync(profile, "token-demog", "alice", addresses[0], It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Add address error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddUserAddressAsync("alice", addresses[0]));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.DeleteUserAddressAsync(string.Empty, "id1"));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.DeleteUserAddressAsync("alice", string.Empty));
        clientMock.Setup(c => c.DeleteUserAddressAsync(profile, "token-demog", "alice", "id1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await vm.DeleteUserAddressAsync("alice", "id1");

        clientMock.Setup(c => c.DeleteUserAddressAsync(profile, "token-demog", "alice", "id1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Delete address error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeleteUserAddressAsync("alice", "id1"));

        // Contacts
        await Assert.ThrowsAsync<ArgumentException>(() => vm.ListUserContactsAsync(string.Empty));
        IReadOnlyList<PatientContact> contacts = [new PatientContact().GetExamples()];
        clientMock.Setup(c => c.ListUserContactsAsync(profile, "token-demog", "alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(contacts);
        var contactsResult = await vm.ListUserContactsAsync("alice");
        Assert.Same(contacts, contactsResult);
        Assert.Same(contacts, vm.UserContacts);

        clientMock.Setup(c => c.ListUserContactsAsync(profile, "token-demog", "alice", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("List contacts error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ListUserContactsAsync("alice"));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.AddUserContactAsync(string.Empty, contacts[0]));
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.AddUserContactAsync("alice", null!));
        clientMock.Setup(c => c.AddUserContactAsync(profile, "token-demog", "alice", contacts[0], It.IsAny<CancellationToken>()))
            .ReturnsAsync(contacts[0]);
        var addedContact = await vm.AddUserContactAsync("alice", contacts[0]);
        Assert.Same(contacts[0], addedContact);

        clientMock.Setup(c => c.AddUserContactAsync(profile, "token-demog", "alice", contacts[0], It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Add contact error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddUserContactAsync("alice", contacts[0]));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.DeleteUserContactAsync(string.Empty, "cid1"));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.DeleteUserContactAsync("alice", string.Empty));
        clientMock.Setup(c => c.DeleteUserContactAsync(profile, "token-demog", "alice", "cid1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await vm.DeleteUserContactAsync("alice", "cid1");

        clientMock.Setup(c => c.DeleteUserContactAsync(profile, "token-demog", "alice", "cid1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Delete contact error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeleteUserContactAsync("alice", "cid1"));
    }

    /// <summary>
    /// Verifies Digital DNA, imports, and clinical result methods in <see cref="PatientClinicalWorkspaceViewModel"/>.
    /// </summary>
    [Fact]
    public async Task PatientClinicalWorkspaceViewModel_DnaImportsAndResults_AllPaths()
    {
        // WHAT: Tests GetPatientDnaRegimensAsync, GetPatientDigitalDnaAsync, GetPatientDigitalDnaRecordAsync, CreatePatientDnaAsync, DeletePatientDigitalDnaAsync,
        //       ListPatientImportsAsync, GetPatientImportAsync, CreatePatientImportAsync, DeletePatientImportAsync,
        //       ListPatientClinicalResultsAsync, GetPatientClinicalResultAsync, CreatePatientClinicalResultAsync, DeletePatientClinicalResultAsync.
        // WHY:  Covers all digital DNA and clinical laboratory result branches.
        // HOW:  Validates string guards, null guards, mock responses, and exception paths.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();
        var vm = new PatientClinicalWorkspaceViewModel(clientMock.Object, () => profile, () => "token-dna-res");

        // Regimens overloads
        var dnaPkg = new PatientDigitalDnaPackage().GetExamples();
        clientMock.Setup(c => c.GetPatientDnaRegimensAsync(profile, "token-dna-res", "alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dnaPkg);
        var pkg1 = await vm.GetPatientDnaRegimensAsync("alice");
        Assert.Same(dnaPkg, pkg1);
        Assert.Same(dnaPkg, vm.LastDnaPackage);

        clientMock.Setup(c => c.GetPatientDnaRegimensAsync(profile, "token-dna-res", "alice", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dna reg error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.GetPatientDnaRegimensAsync("alice"));

        clientMock.Setup(c => c.GetPatientDnaRegimensAsync(profile, "token-dna-res", "alice", TherapeuticRegimen.MaleToFemale, "Nanotube", 4.0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dnaPkg);
        var pkg2 = await vm.GetPatientDnaRegimensAsync("alice", TherapeuticRegimen.MaleToFemale, "Nanotube", 4.0);
        Assert.Same(dnaPkg, pkg2);

        clientMock.Setup(c => c.GetPatientDnaRegimensAsync(profile, "token-dna-res", "alice", TherapeuticRegimen.MaleToFemale, "Nanotube", 4.0, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Filtered dna error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.GetPatientDnaRegimensAsync("alice", TherapeuticRegimen.MaleToFemale, "Nanotube", 4.0));

        // Digital DNA
        await Assert.ThrowsAsync<ArgumentException>(() => vm.GetPatientDigitalDnaAsync(string.Empty));
        IReadOnlyList<PatientDigitalDnaRecord> dnaList = [new PatientDigitalDnaRecord().GetExamples()];
        clientMock.Setup(c => c.GetPatientDigitalDnaAsync(profile, "token-dna-res", "alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dnaList);
        var dnaRecords = await vm.GetPatientDigitalDnaAsync("alice");
        Assert.Same(dnaList, dnaRecords);
        Assert.Same(dnaList, vm.PatientDnaRecords);

        clientMock.Setup(c => c.GetPatientDigitalDnaAsync(profile, "token-dna-res", "alice", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Get dna records error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.GetPatientDigitalDnaAsync("alice"));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.GetPatientDigitalDnaRecordAsync(string.Empty, "d1"));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.GetPatientDigitalDnaRecordAsync("alice", string.Empty));
        clientMock.Setup(c => c.GetPatientDigitalDnaRecordAsync(profile, "token-dna-res", "alice", "d1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dnaList[0]);
        var singleDna = await vm.GetPatientDigitalDnaRecordAsync("alice", "d1");
        Assert.Same(dnaList[0], singleDna);

        clientMock.Setup(c => c.GetPatientDigitalDnaRecordAsync(profile, "token-dna-res", "alice", "d1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Single dna error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.GetPatientDigitalDnaRecordAsync("alice", "d1"));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.CreatePatientDnaAsync(string.Empty, new CreatePatientDnaRequest()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.CreatePatientDnaAsync("alice", null!));
        var createDnaReq = new CreatePatientDnaRequest();
        clientMock.Setup(c => c.CreatePatientDnaAsync(profile, "token-dna-res", "alice", createDnaReq, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dnaList[0]);
        var createdDna = await vm.CreatePatientDnaAsync("alice", createDnaReq);
        Assert.Same(dnaList[0], createdDna);

        clientMock.Setup(c => c.CreatePatientDnaAsync(profile, "token-dna-res", "alice", createDnaReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Create dna error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CreatePatientDnaAsync("alice", createDnaReq));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.DeletePatientDigitalDnaAsync(string.Empty, "d1"));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.DeletePatientDigitalDnaAsync("alice", string.Empty));
        clientMock.Setup(c => c.DeletePatientDigitalDnaAsync(profile, "token-dna-res", "alice", "d1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await vm.DeletePatientDigitalDnaAsync("alice", "d1");

        clientMock.Setup(c => c.DeletePatientDigitalDnaAsync(profile, "token-dna-res", "alice", "d1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Delete dna error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeletePatientDigitalDnaAsync("alice", "d1"));

        // Imports
        await Assert.ThrowsAsync<ArgumentException>(() => vm.ListPatientImportsAsync(string.Empty));
        IReadOnlyList<PatientDnaImportRecord> imports = [new PatientDnaImportRecord().GetExamples()];
        clientMock.Setup(c => c.ListPatientImportsAsync(profile, "token-dna-res", "alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(imports);
        var impList = await vm.ListPatientImportsAsync("alice");
        Assert.Same(imports, impList);
        Assert.Same(imports, vm.PatientImports);

        clientMock.Setup(c => c.ListPatientImportsAsync(profile, "token-dna-res", "alice", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("List imports error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ListPatientImportsAsync("alice"));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.GetPatientImportAsync(string.Empty, "imp1"));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.GetPatientImportAsync("alice", string.Empty));
        clientMock.Setup(c => c.GetPatientImportAsync(profile, "token-dna-res", "alice", "imp1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(imports[0]);
        var singleImp = await vm.GetPatientImportAsync("alice", "imp1");
        Assert.Same(imports[0], singleImp);

        clientMock.Setup(c => c.GetPatientImportAsync(profile, "token-dna-res", "alice", "imp1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Single imp error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.GetPatientImportAsync("alice", "imp1"));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.CreatePatientImportAsync(string.Empty, CreateImportReq()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.CreatePatientImportAsync("alice", null!));
        var createImpReq = CreateImportReq();
        clientMock.Setup(c => c.CreatePatientImportAsync(profile, "token-dna-res", "alice", createImpReq, It.IsAny<CancellationToken>()))
            .ReturnsAsync(imports[0]);
        var createdImp = await vm.CreatePatientImportAsync("alice", createImpReq);
        Assert.Same(imports[0], createdImp);

        clientMock.Setup(c => c.CreatePatientImportAsync(profile, "token-dna-res", "alice", createImpReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Create imp error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CreatePatientImportAsync("alice", createImpReq));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.DeletePatientImportAsync(string.Empty, "imp1"));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.DeletePatientImportAsync("alice", string.Empty));
        clientMock.Setup(c => c.DeletePatientImportAsync(profile, "token-dna-res", "alice", "imp1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await vm.DeletePatientImportAsync("alice", "imp1");

        clientMock.Setup(c => c.DeletePatientImportAsync(profile, "token-dna-res", "alice", "imp1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Delete imp error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeletePatientImportAsync("alice", "imp1"));

        // Results
        await Assert.ThrowsAsync<ArgumentException>(() => vm.ListPatientClinicalResultsAsync(string.Empty));
        IReadOnlyList<PatientResultRecord> results = [new PatientResultRecord().GetExamples()];
        clientMock.Setup(c => c.ListPatientClinicalResultsAsync(profile, "token-dna-res", "alice", "Endocrine", It.IsAny<CancellationToken>()))
            .ReturnsAsync(results);
        var resList = await vm.ListPatientClinicalResultsAsync("alice", "Endocrine");
        Assert.Same(results, resList);
        Assert.Same(results, vm.PatientClinicalResults);

        clientMock.Setup(c => c.ListPatientClinicalResultsAsync(profile, "token-dna-res", "alice", "Endocrine", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("List results error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ListPatientClinicalResultsAsync("alice", "Endocrine"));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.GetPatientClinicalResultAsync(string.Empty, "r1"));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.GetPatientClinicalResultAsync("alice", string.Empty));
        clientMock.Setup(c => c.GetPatientClinicalResultAsync(profile, "token-dna-res", "alice", "r1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(results[0]);
        var singleRes = await vm.GetPatientClinicalResultAsync("alice", "r1");
        Assert.Same(results[0], singleRes);

        clientMock.Setup(c => c.GetPatientClinicalResultAsync(profile, "token-dna-res", "alice", "r1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Single res error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.GetPatientClinicalResultAsync("alice", "r1"));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.CreatePatientClinicalResultAsync(string.Empty, CreateResultReq()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.CreatePatientClinicalResultAsync("alice", null!));
        var createResReq = CreateResultReq();
        clientMock.Setup(c => c.CreatePatientClinicalResultAsync(profile, "token-dna-res", "alice", createResReq, It.IsAny<CancellationToken>()))
            .ReturnsAsync(results[0]);
        var createdRes = await vm.CreatePatientClinicalResultAsync("alice", createResReq);
        Assert.Same(results[0], createdRes);

        clientMock.Setup(c => c.CreatePatientClinicalResultAsync(profile, "token-dna-res", "alice", createResReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Create res error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CreatePatientClinicalResultAsync("alice", createResReq));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.DeletePatientClinicalResultAsync(string.Empty, "r1"));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.DeletePatientClinicalResultAsync("alice", string.Empty));
        clientMock.Setup(c => c.DeletePatientClinicalResultAsync(profile, "token-dna-res", "alice", "r1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await vm.DeletePatientClinicalResultAsync("alice", "r1");

        clientMock.Setup(c => c.DeletePatientClinicalResultAsync(profile, "token-dna-res", "alice", "r1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Delete res error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeletePatientClinicalResultAsync("alice", "r1"));
    }

    /// <summary>
    /// Verifies subject grants, my-subjects, and cohort groups methods in <see cref="PatientClinicalWorkspaceViewModel"/>.
    /// </summary>
    [Fact]
    public async Task PatientClinicalWorkspaceViewModel_SubjectsAndCohortGroups_AllPaths()
    {
        // WHAT: Tests ListSubjectGrantsAsync, CreateSubjectGrantAsync, RevokeSubjectGrantAsync, ListMySubjectsAsync,
        //       ListSubjectGroupsAsync, CreateSubjectGroupAsync, GetSubjectGroupAsync, UpdateSubjectGroupAsync, DeleteSubjectGroupAsync,
        //       ListGroupMembersAsync, AddGroupMemberAsync, RemoveGroupMemberAsync, ListGroupGrantsAsync, CreateGroupGrantAsync, RevokeGroupGrantAsync.
        // WHY:  Covers all subject access gating and cohort management branches.
        // HOW:  Validates string guards, null guards, mock responses, and exception paths.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();
        var vm = new PatientClinicalWorkspaceViewModel(clientMock.Object, () => profile, () => "token-sub-grp");

        // SubjectGrants
        IReadOnlyList<SubjectGrant> grants = [new SubjectGrant { Id = "g1", ResearcherUsername = "r1", SubjectUsername = "s1", AccessLevel = SubjectAccessLevel.GenomicAccess, GrantedBy = "admin", GrantedAtUtc = DateTimeOffset.UtcNow }];
        clientMock.Setup(c => c.ListSubjectGrantsAsync(profile, "token-sub-grp", "r1", "s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(grants);
        var grantsResult = await vm.ListSubjectGrantsAsync("r1", "s1");
        Assert.Same(grants, grantsResult);
        Assert.Same(grants, vm.SubjectGrants);

        clientMock.Setup(c => c.ListSubjectGrantsAsync(profile, "token-sub-grp", "r1", "s1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Grants fetch failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ListSubjectGrantsAsync("r1", "s1"));

        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.CreateSubjectGrantAsync(null!));
        var createGrantReq = new GrantSubjectAccessRequest { ResearcherUsername = "r1", SubjectUsername = "s1" };
        clientMock.Setup(c => c.CreateSubjectGrantAsync(profile, "token-sub-grp", createGrantReq, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grants[0]);
        var createdGrant = await vm.CreateSubjectGrantAsync(createGrantReq);
        Assert.Same(grants[0], createdGrant);

        clientMock.Setup(c => c.CreateSubjectGrantAsync(profile, "token-sub-grp", createGrantReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Create grant failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CreateSubjectGrantAsync(createGrantReq));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.RevokeSubjectGrantAsync(string.Empty, "s1"));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.RevokeSubjectGrantAsync("r1", string.Empty));
        clientMock.Setup(c => c.RevokeSubjectGrantAsync(profile, "token-sub-grp", "r1", "s1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await vm.RevokeSubjectGrantAsync("r1", "s1");

        clientMock.Setup(c => c.RevokeSubjectGrantAsync(profile, "token-sub-grp", "r1", "s1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Revoke grant failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RevokeSubjectGrantAsync("r1", "s1"));

        // My subjects
        IReadOnlyList<AccessibleSubjectItem> mySubjects = [new AccessibleSubjectItem { SubjectUsername = "s1", EffectiveLevel = SubjectAccessLevel.FullReadAccess }];
        clientMock.Setup(c => c.ListMySubjectsAsync(profile, "token-sub-grp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(mySubjects);
        var mySubs = await vm.ListMySubjectsAsync();
        Assert.Same(mySubjects, mySubs);
        Assert.Same(mySubjects, vm.AccessibleSubjects);

        clientMock.Setup(c => c.ListMySubjectsAsync(profile, "token-sub-grp", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("My subjects failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ListMySubjectsAsync());

        // Groups
        IReadOnlyList<SubjectGroup> groups = [new SubjectGroup { Id = "grp1", Name = "Cohort", CreatedBy = "admin", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow }];
        clientMock.Setup(c => c.ListSubjectGroupsAsync(profile, "token-sub-grp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(groups);
        var grpList = await vm.ListSubjectGroupsAsync();
        Assert.Same(groups, grpList);
        Assert.Same(groups, vm.SubjectGroups);

        clientMock.Setup(c => c.ListSubjectGroupsAsync(profile, "token-sub-grp", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("List groups failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ListSubjectGroupsAsync());

        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.CreateSubjectGroupAsync(null!));
        var createGrpReq = new CreateSubjectGroupRequest { Name = "Cohort" };
        clientMock.Setup(c => c.CreateSubjectGroupAsync(profile, "token-sub-grp", createGrpReq, It.IsAny<CancellationToken>()))
            .ReturnsAsync(groups[0]);
        var createdGrp = await vm.CreateSubjectGroupAsync(createGrpReq);
        Assert.Same(groups[0], createdGrp);

        clientMock.Setup(c => c.CreateSubjectGroupAsync(profile, "token-sub-grp", createGrpReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Create group failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CreateSubjectGroupAsync(createGrpReq));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.GetSubjectGroupAsync(string.Empty));
        clientMock.Setup(c => c.GetSubjectGroupAsync(profile, "token-sub-grp", "g1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(groups[0]);
        var singleGrp = await vm.GetSubjectGroupAsync("g1");
        Assert.Same(groups[0], singleGrp);

        clientMock.Setup(c => c.GetSubjectGroupAsync(profile, "token-sub-grp", "g1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Get group failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.GetSubjectGroupAsync("g1"));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.UpdateSubjectGroupAsync(string.Empty, new UpdateSubjectGroupRequest()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.UpdateSubjectGroupAsync("g1", null!));
        var updateGrpReq = new UpdateSubjectGroupRequest { Name = "Updated" };
        clientMock.Setup(c => c.UpdateSubjectGroupAsync(profile, "token-sub-grp", "g1", updateGrpReq, It.IsAny<CancellationToken>()))
            .ReturnsAsync(groups[0]);
        var updatedGrp = await vm.UpdateSubjectGroupAsync("g1", updateGrpReq);
        Assert.Same(groups[0], updatedGrp);

        clientMock.Setup(c => c.UpdateSubjectGroupAsync(profile, "token-sub-grp", "g1", updateGrpReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Update group failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.UpdateSubjectGroupAsync("g1", updateGrpReq));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.DeleteSubjectGroupAsync(string.Empty));
        clientMock.Setup(c => c.DeleteSubjectGroupAsync(profile, "token-sub-grp", "g1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await vm.DeleteSubjectGroupAsync("g1");

        clientMock.Setup(c => c.DeleteSubjectGroupAsync(profile, "token-sub-grp", "g1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Delete group failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeleteSubjectGroupAsync("g1"));

        // Group members
        await Assert.ThrowsAsync<ArgumentException>(() => vm.ListGroupMembersAsync(string.Empty));
        IReadOnlyList<string> members = ["userA", "userB"];
        clientMock.Setup(c => c.ListGroupMembersAsync(profile, "token-sub-grp", "g1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(members);
        var memList = await vm.ListGroupMembersAsync("g1");
        Assert.Same(members, memList);

        clientMock.Setup(c => c.ListGroupMembersAsync(profile, "token-sub-grp", "g1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("List members failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ListGroupMembersAsync("g1"));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.AddGroupMemberAsync(string.Empty, new AddGroupMemberRequest { SubjectUsername = "userA" }));
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.AddGroupMemberAsync("g1", null!));
        var addMemReq = new AddGroupMemberRequest { SubjectUsername = "userA" };
        clientMock.Setup(c => c.AddGroupMemberAsync(profile, "token-sub-grp", "g1", addMemReq, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await vm.AddGroupMemberAsync("g1", addMemReq);

        clientMock.Setup(c => c.AddGroupMemberAsync(profile, "token-sub-grp", "g1", addMemReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Add member failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddGroupMemberAsync("g1", addMemReq));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.RemoveGroupMemberAsync(string.Empty, "userA"));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.RemoveGroupMemberAsync("g1", string.Empty));
        clientMock.Setup(c => c.RemoveGroupMemberAsync(profile, "token-sub-grp", "g1", "userA", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await vm.RemoveGroupMemberAsync("g1", "userA");

        clientMock.Setup(c => c.RemoveGroupMemberAsync(profile, "token-sub-grp", "g1", "userA", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Remove member failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RemoveGroupMemberAsync("g1", "userA"));

        // Group grants
        await Assert.ThrowsAsync<ArgumentException>(() => vm.ListGroupGrantsAsync(string.Empty));
        IReadOnlyList<SubjectGroupGrant> groupGrants = [new SubjectGroupGrant { Id = "g1", GroupId = "grp1", ResearcherUsername = "r1", AccessLevel = SubjectAccessLevel.FullReadAccess, GrantedBy = "admin", GrantedAtUtc = DateTimeOffset.UtcNow }];
        clientMock.Setup(c => c.ListGroupGrantsAsync(profile, "token-sub-grp", "g1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(groupGrants);
        var grpGrantsResult = await vm.ListGroupGrantsAsync("g1");
        Assert.Same(groupGrants, grpGrantsResult);

        clientMock.Setup(c => c.ListGroupGrantsAsync(profile, "token-sub-grp", "g1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("List group grants failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ListGroupGrantsAsync("g1"));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.CreateGroupGrantAsync(string.Empty, new GrantGroupAccessRequest()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.CreateGroupGrantAsync("g1", null!));
        var createGrpGrantReq = new GrantGroupAccessRequest { ResearcherUsername = "r1" };
        clientMock.Setup(c => c.CreateGroupGrantAsync(profile, "token-sub-grp", "g1", createGrpGrantReq, It.IsAny<CancellationToken>()))
            .ReturnsAsync(groupGrants[0]);
        var createdGrpGrant = await vm.CreateGroupGrantAsync("g1", createGrpGrantReq);
        Assert.Same(groupGrants[0], createdGrpGrant);

        clientMock.Setup(c => c.CreateGroupGrantAsync(profile, "token-sub-grp", "g1", createGrpGrantReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Create group grant failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CreateGroupGrantAsync("g1", createGrpGrantReq));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.RevokeGroupGrantAsync(string.Empty, "r1"));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.RevokeGroupGrantAsync("g1", string.Empty));
        clientMock.Setup(c => c.RevokeGroupGrantAsync(profile, "token-sub-grp", "g1", "r1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await vm.RevokeGroupGrantAsync("g1", "r1");

        clientMock.Setup(c => c.RevokeGroupGrantAsync(profile, "token-sub-grp", "g1", "r1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Revoke group grant failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RevokeGroupGrantAsync("g1", "r1"));
    }

    // =========================================================================
    // FederationAndDatasetWorkspaceViewModel Tests
    // =========================================================================

    /// <summary>
    /// Verifies constructor guard against null client.
    /// </summary>
    [Fact]
    public void FederationAndDatasetWorkspaceViewModel_Constructor_NullClient_Throws()
    {
        // WHAT: Tests null client validation in FederationAndDatasetWorkspaceViewModel.
        // WHY:  Guarantees fail-fast on missing dependency.
        // HOW:  Passes null client and asserts ArgumentNullException.
        Assert.Throws<ArgumentNullException>(() => new FederationAndDatasetWorkspaceViewModel(null!));
    }

    /// <summary>
    /// Verifies property change notifications and equality suppression in <see cref="FederationAndDatasetWorkspaceViewModel"/>.
    /// </summary>
    [Fact]
    public void FederationAndDatasetWorkspaceViewModel_Properties_FirePropertyChanged()
    {
        // WHAT: Tests observable properties of FederationAndDatasetWorkspaceViewModel.
        // WHY:  Ensures data binding invariants are satisfied.
        // HOW:  Mutates each property, checks notification list, and sets same value to verify suppression.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var vm = new FederationAndDatasetWorkspaceViewModel(clientMock.Object);

        var fired = new List<string>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName!);

        var profile = CreateProfile();
        vm.ActiveProfile = profile;
        Assert.Same(profile, vm.ActiveProfile);
        Assert.Contains(nameof(vm.ActiveProfile), fired);
        fired.Clear();
        vm.ActiveProfile = profile;
        Assert.Empty(fired);

        vm.ActiveBearerToken = "token-fed";
        Assert.Equal("token-fed", vm.ActiveBearerToken);
        Assert.Contains(nameof(vm.ActiveBearerToken), fired);
        fired.Clear();
        vm.ActiveBearerToken = "token-fed";
        Assert.Empty(fired);

        Assert.False(vm.IsBusy);
        Assert.Null(vm.ErrorMessage);
        Assert.Null(vm.ShareKeys);
        Assert.Null(vm.PeerKeys);
        Assert.Null(vm.ExportedBundles);
        Assert.Null(vm.Trajectories);
        Assert.Null(vm.FederationPeers);
        Assert.Null(vm.LastBuiltManifest);
        Assert.Null(vm.LastVerifyResponse);
    }

    /// <summary>
    /// Verifies session resolution failures when profile or token is missing in <see cref="FederationAndDatasetWorkspaceViewModel"/>.
    /// </summary>
    [Fact]
    public async Task FederationAndDatasetWorkspaceViewModel_SessionResolution_ThrowsWhenMissing()
    {
        // WHAT: Tests missing profile and missing token branches in FederationAndDatasetWorkspaceViewModel.
        // WHY:  Guarantees federation operations fail fast when credentials are absent.
        // HOW:  Calls methods on VMs without profile or token and asserts InvalidOperationException.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();

        var noProfileVm = new FederationAndDatasetWorkspaceViewModel(clientMock.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => noProfileVm.ListShareKeysAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => noProfileVm.RedeemShareKeyAsync(new RedeemShareKeyRequest()));

        var noTokenVm = new FederationAndDatasetWorkspaceViewModel(clientMock.Object, () => profile, () => null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => noTokenVm.ListShareKeysAsync());

        var wsTokenVm = new FederationAndDatasetWorkspaceViewModel(clientMock.Object, () => profile, () => "   ");
        await Assert.ThrowsAsync<InvalidOperationException>(() => wsTokenVm.ListShareKeysAsync());

        // Has profile but null token provider callback
        var nullProviderVm = new FederationAndDatasetWorkspaceViewModel(clientMock.Object, () => profile);
        await Assert.ThrowsAsync<InvalidOperationException>(() => nullProviderVm.ListShareKeysAsync());

        // Explicit ActiveBearerToken takes precedence over token provider
        clientMock.Setup(c => c.ListShareKeysAsync(profile, "explicit-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ShareKeyRecord>());
        nullProviderVm.ActiveBearerToken = "explicit-token";
        await nullProviderVm.ListShareKeysAsync();
        Assert.NotNull(nullProviderVm.ShareKeys);
    }

    /// <summary>
    /// Verifies share keys and peer key registry methods in <see cref="FederationAndDatasetWorkspaceViewModel"/>.
    /// </summary>
    [Fact]
    public async Task FederationAndDatasetWorkspaceViewModel_ShareKeysAndPeerKeys_AllPaths()
    {
        // WHAT: Tests ListShareKeysAsync, CreateShareKeyAsync, RedeemShareKeyAsync, ListPeerKeysAsync, RegisterPeerKeyAsync, RevokePeerKeyAsync.
        // WHY:  Covers all share key and peer identity branches.
        // HOW:  Validates string guards, null guards, mock responses, and exception paths.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();
        var vm = new FederationAndDatasetWorkspaceViewModel(clientMock.Object, () => profile, () => "token-share-peer");

        // Share keys
        IReadOnlyList<ShareKeyRecord> shareKeys = [CreateShareKey()];
        clientMock.Setup(c => c.ListShareKeysAsync(profile, "token-share-peer", It.IsAny<CancellationToken>()))
            .ReturnsAsync(shareKeys);
        var keysList = await vm.ListShareKeysAsync();
        Assert.Same(shareKeys, keysList);
        Assert.Same(shareKeys, vm.ShareKeys);

        clientMock.Setup(c => c.ListShareKeysAsync(profile, "token-share-peer", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("List share keys error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ListShareKeysAsync());

        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.CreateShareKeyAsync(null!));
        var createKeyReq = new CreateShareKeyRequest();
        clientMock.Setup(c => c.CreateShareKeyAsync(profile, "token-share-peer", createKeyReq, It.IsAny<CancellationToken>()))
            .ReturnsAsync(shareKeys[0]);
        var createdKey = await vm.CreateShareKeyAsync(createKeyReq);
        Assert.Same(shareKeys[0], createdKey);

        clientMock.Setup(c => c.CreateShareKeyAsync(profile, "token-share-peer", createKeyReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Create share key error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CreateShareKeyAsync(createKeyReq));

        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.RedeemShareKeyAsync(null!));
        var redeemReq = new RedeemShareKeyRequest();
        var consumeRes = CreateShareKeyConsumeResult();
        clientMock.Setup(c => c.RedeemShareKeyAsync(profile, redeemReq, It.IsAny<CancellationToken>()))
            .ReturnsAsync(consumeRes);
        var redeemResult = await vm.RedeemShareKeyAsync(redeemReq);
        Assert.Same(consumeRes, redeemResult);

        clientMock.Setup(c => c.RedeemShareKeyAsync(profile, redeemReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redeem share key error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RedeemShareKeyAsync(redeemReq));

        // Peer keys
        var peerKeysResp = CreatePeerKeyListResponse();
        clientMock.Setup(c => c.ListPeerKeysAsync(profile, "token-share-peer", It.IsAny<CancellationToken>()))
            .ReturnsAsync(peerKeysResp);
        var peerKeysResult = await vm.ListPeerKeysAsync();
        Assert.Same(peerKeysResp, peerKeysResult);
        Assert.Same(peerKeysResp, vm.PeerKeys);

        clientMock.Setup(c => c.ListPeerKeysAsync(profile, "token-share-peer", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("List peer keys error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ListPeerKeysAsync());

        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.RegisterPeerKeyAsync(null!));
        var regKeyReq = new RegisterPeerKeyRequest();
        var peerKeyRec = CreatePeerKey();
        clientMock.Setup(c => c.RegisterPeerKeyAsync(profile, "token-share-peer", regKeyReq, It.IsAny<CancellationToken>()))
            .ReturnsAsync(peerKeyRec);
        var regKeyResult = await vm.RegisterPeerKeyAsync(regKeyReq);
        Assert.Same(peerKeyRec, regKeyResult);

        clientMock.Setup(c => c.RegisterPeerKeyAsync(profile, "token-share-peer", regKeyReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Register peer key error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RegisterPeerKeyAsync(regKeyReq));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.RevokePeerKeyAsync(string.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.RevokePeerKeyAsync("   "));
        clientMock.Setup(c => c.RevokePeerKeyAsync(profile, "token-share-peer", "pk1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await vm.RevokePeerKeyAsync("pk1");

        clientMock.Setup(c => c.RevokePeerKeyAsync(profile, "token-share-peer", "pk1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Revoke peer key error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RevokePeerKeyAsync("pk1"));
    }

    /// <summary>
    /// Verifies dataset transfer and trajectory federation methods in <see cref="FederationAndDatasetWorkspaceViewModel"/>.
    /// </summary>
    [Fact]
    public async Task FederationAndDatasetWorkspaceViewModel_DatasetAndFederation_AllPaths()
    {
        // WHAT: Tests ListExportedDatasetBundlesAsync, ExportDatasetAsync, ExportEncryptedDatasetAsync, ImportDatasetArchiveAsync,
        //       DownloadDatasetArchiveAsync, DeleteExportedDatasetBundleAsync, DiscoverFederationPeersAsync, BuildFederationManifestAsync,
        //       VerifyFederationManifestAsync, GetFederationChunkAsync, ListTrajectoriesAsync, DownloadTrajectoryAsync.
        // WHY:  Covers all dataset package export, import, and swarm trajectory distribution branches.
        // HOW:  Validates string guards, null guards, mock responses, and exception paths.
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = CreateProfile();
        var vm = new FederationAndDatasetWorkspaceViewModel(clientMock.Object, () => profile, () => "token-data-fed");

        // Exported bundles
        IReadOnlyList<ExportFileItem> exportItems = [new ExportFileItem { Name = "a", SizeBytes = 1, SizeFormatted = "1B", LastModifiedUtc = "now", Category = "Archive" }.GetExamples()];
        clientMock.Setup(c => c.ListExportedDatasetBundlesAsync(profile, "token-data-fed", It.IsAny<CancellationToken>()))
            .ReturnsAsync(exportItems);
        var exportsList = await vm.ListExportedDatasetBundlesAsync();
        Assert.Same(exportItems, exportsList);
        Assert.Same(exportItems, vm.ExportedBundles);

        clientMock.Setup(c => c.ListExportedDatasetBundlesAsync(profile, "token-data-fed", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("List exports error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ListExportedDatasetBundlesAsync());

        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.ExportDatasetAsync(null!));
        var exportReq = new ExportRequest();
        var datasetManifest = CreateDatasetManifest();
        clientMock.Setup(c => c.ExportDatasetAsync(profile, "token-data-fed", exportReq, It.IsAny<CancellationToken>()))
            .ReturnsAsync(datasetManifest);
        var exported = await vm.ExportDatasetAsync(exportReq);
        Assert.Same(datasetManifest, exported);

        clientMock.Setup(c => c.ExportDatasetAsync(profile, "token-data-fed", exportReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Export dataset error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ExportDatasetAsync(exportReq));

        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.ExportEncryptedDatasetAsync(null!));
        var encReq = new ExportEncryptedDatasetRequest { TrajectoryFileName = "traj.xyz", SubjectId = "alice" };
        var encRes = new ExportEncryptedDatasetResult().GetExamples();
        clientMock.Setup(c => c.ExportEncryptedDatasetAsync(profile, "token-data-fed", encReq, It.IsAny<CancellationToken>()))
            .ReturnsAsync(encRes);
        var encResult = await vm.ExportEncryptedDatasetAsync(encReq);
        Assert.Same(encRes, encResult);

        clientMock.Setup(c => c.ExportEncryptedDatasetAsync(profile, "token-data-fed", encReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Export enc error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ExportEncryptedDatasetAsync(encReq));

        // Import
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.ImportDatasetArchiveAsync(null!, "arch.tar.gz", [1], "man.json"));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.ImportDatasetArchiveAsync([1], string.Empty, [1], "man.json"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.ImportDatasetArchiveAsync([1], "arch.tar.gz", null!, "man.json"));
        await Assert.ThrowsAsync<ArgumentException>(() => vm.ImportDatasetArchiveAsync([1], "arch.tar.gz", [1], string.Empty));

        var importRes = new DatasetImportResponse().GetExamples();
        clientMock.Setup(c => c.ImportDatasetArchiveAsync(profile, "token-data-fed", It.IsAny<byte[]>(), "arch.tar.gz", It.IsAny<byte[]>(), "man.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(importRes);
        var imported = await vm.ImportDatasetArchiveAsync([1], "arch.tar.gz", [2], "man.json");
        Assert.Same(importRes, imported);

        clientMock.Setup(c => c.ImportDatasetArchiveAsync(profile, "token-data-fed", It.IsAny<byte[]>(), "arch.tar.gz", It.IsAny<byte[]>(), "man.json", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Import error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ImportDatasetArchiveAsync([1], "arch.tar.gz", [2], "man.json"));

        // Download / delete export
        await Assert.ThrowsAsync<ArgumentException>(() => vm.DownloadDatasetArchiveAsync(string.Empty));
        byte[] downloadBytes = [1, 2, 3];
        clientMock.Setup(c => c.DownloadDatasetArchiveAsync(profile, "token-data-fed", "bundle.tar.gz", It.IsAny<CancellationToken>()))
            .ReturnsAsync(downloadBytes);
        var downloaded = await vm.DownloadDatasetArchiveAsync("bundle.tar.gz");
        Assert.Same(downloadBytes, downloaded);

        clientMock.Setup(c => c.DownloadDatasetArchiveAsync(profile, "token-data-fed", "bundle.tar.gz", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Download archive error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DownloadDatasetArchiveAsync("bundle.tar.gz"));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.DeleteExportedDatasetBundleAsync(string.Empty));
        clientMock.Setup(c => c.DeleteExportedDatasetBundleAsync(profile, "token-data-fed", "bundle.tar.gz", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await vm.DeleteExportedDatasetBundleAsync("bundle.tar.gz");

        clientMock.Setup(c => c.DeleteExportedDatasetBundleAsync(profile, "token-data-fed", "bundle.tar.gz", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Delete archive error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeleteExportedDatasetBundleAsync("bundle.tar.gz"));

        // Trajectory Federation
        IReadOnlyList<FederationPeerNode> peers = [new FederationPeerNode
        {
            NodeId = "n1",
            EndpointUri = "http://localhost:5001",
            PublicKeyBase64 = "pk",
            Algorithm = KeyAlgorithm.Ed25519,
            LatencyMilliseconds = 10.0,
            LastSeenUtc = DateTimeOffset.UtcNow
        }];
        clientMock.Setup(c => c.DiscoverFederationPeersAsync(profile, "token-data-fed", It.IsAny<CancellationToken>()))
            .ReturnsAsync(peers);
        var discoveredPeers = await vm.DiscoverFederationPeersAsync();
        Assert.Same(peers, discoveredPeers);
        Assert.Same(peers, vm.FederationPeers);

        clientMock.Setup(c => c.DiscoverFederationPeersAsync(profile, "token-data-fed", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Discover peers error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DiscoverFederationPeersAsync());

        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.BuildFederationManifestAsync(null!));
        var buildManifestReq = new FederationBuildManifestRequest { TrajectoryFileName = "traj.xyz" };
        var fedManifest = new TrajectoryFederationManifest
        {
            DatasetName = "traj.xyz",
            TotalSizeBytes = 100,
            ChunkSizeBytes = 10,
            TotalChunks = 10,
            Chunks = [],
            SigningNodeId = "n1",
            SignatureAlgorithm = "Ed25519",
            SignatureBase64 = "sig",
            ManifestTimestampUtc = DateTimeOffset.UtcNow
        };
        clientMock.Setup(c => c.BuildFederationManifestAsync(profile, "token-data-fed", buildManifestReq, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fedManifest);
        var builtManifest = await vm.BuildFederationManifestAsync(buildManifestReq);
        Assert.Same(fedManifest, builtManifest);
        Assert.Same(fedManifest, vm.LastBuiltManifest);

        clientMock.Setup(c => c.BuildFederationManifestAsync(profile, "token-data-fed", buildManifestReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Build manifest error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.BuildFederationManifestAsync(buildManifestReq));

        await Assert.ThrowsAsync<ArgumentNullException>(() => vm.VerifyFederationManifestAsync(null!));
        var verifyRes = new FederationVerifyManifestResponse().GetExamples();
        clientMock.Setup(c => c.VerifyFederationManifestAsync(profile, "token-data-fed", fedManifest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(verifyRes);
        var verified = await vm.VerifyFederationManifestAsync(fedManifest);
        Assert.Same(verifyRes, verified);
        Assert.Same(verifyRes, vm.LastVerifyResponse);

        clientMock.Setup(c => c.VerifyFederationManifestAsync(profile, "token-data-fed", fedManifest, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Verify manifest error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.VerifyFederationManifestAsync(fedManifest));

        await Assert.ThrowsAsync<ArgumentException>(() => vm.GetFederationChunkAsync(string.Empty));
        byte[] chunkBytes = [10, 20];
        clientMock.Setup(c => c.GetFederationChunkAsync(profile, "token-data-fed", "traj.xyz", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunkBytes);
        var chunk = await vm.GetFederationChunkAsync("traj.xyz", 1);
        Assert.Same(chunkBytes, chunk);

        clientMock.Setup(c => c.GetFederationChunkAsync(profile, "token-data-fed", "traj.xyz", 1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Get chunk error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.GetFederationChunkAsync("traj.xyz", 1));

        IReadOnlyList<TrajectoryFileItem> trajectories = [new TrajectoryFileItem
        {
            Name = "dna.xyz",
            SizeBytes = 100,
            SizeFormatted = "100 B",
            LastModifiedUtc = "2026-10-20T18:00:00Z",
            Format = "XYZ"
        }];
        clientMock.Setup(c => c.ListTrajectoriesAsync(profile, "token-data-fed", It.IsAny<CancellationToken>()))
            .ReturnsAsync(trajectories);
        var trajs = await vm.ListTrajectoriesAsync();
        Assert.Same(trajectories, trajs);
        Assert.Same(trajectories, vm.Trajectories);

        clientMock.Setup(c => c.ListTrajectoriesAsync(profile, "token-data-fed", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("List trajectories error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ListTrajectoriesAsync());

        await Assert.ThrowsAsync<ArgumentException>(() => vm.DownloadTrajectoryAsync(string.Empty));
        byte[] trajBytes = [4, 5, 6];
        clientMock.Setup(c => c.DownloadTrajectoryAsync(profile, "token-data-fed", "traj.xyz", It.IsAny<CancellationToken>()))
            .ReturnsAsync(trajBytes);
        var downloadedTraj = await vm.DownloadTrajectoryAsync("traj.xyz");
        Assert.Same(trajBytes, downloadedTraj);

        clientMock.Setup(c => c.DownloadTrajectoryAsync(profile, "token-data-fed", "traj.xyz", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Download trajectory error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DownloadTrajectoryAsync("traj.xyz"));
    }

    /// <summary>
    /// Verifies integration of the 3 new workspaces into <see cref="MainShellViewModel"/>.
    /// </summary>
    [Fact]
    public void MainShellViewModel_Phase4Workspaces_IntegrationAndInjection()
    {
        // WHAT: Tests default construction and custom injection of Phase 4 ViewModels in MainShellViewModel.
        // WHY:  Ensures MainShellViewModel exposes NodeAdministration, PatientClinicalWorkspace, and FederationAndDatasetWorkspace.
        // HOW:  Instantiates MainShellViewModel with defaults and custom instances, asserts properties.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();
        var nodeMock = new Mock<INanoWeaverNodeClient>();

        using var defaultShell = new MainShellViewModel(repoMock.Object, coordMock.Object, nodeMock.Object);
        Assert.NotNull(defaultShell.NodeAdministration);
        Assert.NotNull(defaultShell.PatientClinicalWorkspace);
        Assert.NotNull(defaultShell.FederationAndDatasetWorkspace);

        var customAdmin = new NodeAdministrationViewModel(nodeMock.Object);
        var customPatient = new PatientClinicalWorkspaceViewModel(nodeMock.Object);
        var customFed = new FederationAndDatasetWorkspaceViewModel(nodeMock.Object);

        using var customShell = new MainShellViewModel(
            repoMock.Object,
            coordMock.Object,
            nodeMock.Object,
            nodeAdministration: customAdmin,
            patientClinicalWorkspace: customPatient,
            federationAndDatasetWorkspace: customFed);

        Assert.Same(customAdmin, customShell.NodeAdministration);
        Assert.Same(customPatient, customShell.PatientClinicalWorkspace);
        Assert.Same(customFed, customShell.FederationAndDatasetWorkspace);
    }
}
