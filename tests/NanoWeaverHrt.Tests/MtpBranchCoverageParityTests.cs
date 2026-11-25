using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NanoWeaverHrt.Analytics;
using NanoWeaverHrt.Bridges;
using NanoWeaverHrt.Caching;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Compliance;
using NanoWeaverHrt.Components.Layout;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Database;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Desktop;
using NanoWeaverHrt.Desktop.Platform;
using NanoWeaverHrt.Desktop.Shell;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Events;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Fabrication.Gel;
using NanoWeaverHrt.Federation;
using NanoWeaverHrt.Formatting;
using NanoWeaverHrt.Genomics;
using NanoWeaverHrt.Gui.Core.Services;
using NanoWeaverHrt.Gui.Core.ViewModels;
using NanoWeaverHrt.Mobile.Platform;
using NanoWeaverHrt.Mobile.Security;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Pipelines;
using NanoWeaverHrt.Pipelines.Simd;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Security.Signing;
using NanoWeaverHrt.Services.Stack;
using NanoWeaverHrt.Simulations;
using NanoWeaverHrt.Simulations.Models;
using NanoWeaverHrt.Storage;
using StackExchange.Redis;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Targeted unit tests covering every branch exposed by Coverlet.MTP 10.1.1 Release-mode
/// instrumentation across synchronous classes and compiler-generated async state machines.
/// </summary>
public sealed class MtpBranchCoverageParityTests : IDisposable
{
    /// <summary>
    /// Isolated temporary root directory created for file-backed and SQLite tests in this instance.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="MtpBranchCoverageParityTests"/> class.
    /// </summary>
    public MtpBranchCoverageParityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "weaver_mtp_parity_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// Releases pooled SQLite handles and removes temporary test directories created during execution.
    /// </summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup when native SQLite/DuckDB handles linger transiently.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup when directory permissions restrict immediate removal.
            }
        }
    }

    /// <summary>
    /// Verifies <see cref="MultiInstanceCoordinator.PollAllConnectedAsync"/> transition alerts with both
    /// non-null and null <see cref="InstanceConnectionSnapshot.LastErrorTitle"/> values.
    /// </summary>
    [Fact]
    public async Task MultiInstanceCoordinator_PollAllConnectedAsync_CoversErrorTitleNullAndNonNull()
    {
        // WHAT: Exercises Online -> Offline diff alerts in PollAllConnectedAsync with null and non-null LastErrorTitle.
        // WHY:  Coverlet 10.1.1 instruments the null-coalescing branch inside async state machines.
        // HOW:  Connects one instance cleanly, then polls Offline snapshots with null and explicit error titles.
        InstanceProfile profile = new(
            "inst-1",
            "Primary Node",
            "http://127.0.0.1:5000",
            true,
            "admin",
            "Administrator",
            "Ed25519",
            "PUB",
            "ENV",
            true,
            15,
            DateTimeOffset.UtcNow,
            null);

        Mock<IInstanceProfileRepository> repoMock = new();
        repoMock.Setup(r => r.GetProfileAsync("inst-1", It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        repoMock.Setup(r => r.UpdateLastConnectedAsync("inst-1", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        int pollStep = 0;
        Mock<INanoWeaverNodeClient> clientMock = new();
        clientMock.Setup(c => c.AuthenticateAsync(profile, It.IsAny<ReadOnlyMemory<char>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeAuthSession("tok-1", "Bearer", 3600, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "admin", "Administrator"));
        clientMock.Setup(c => c.PollSnapshotAsync(profile, "tok-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                pollStep++;
                return pollStep switch
                {
                    1 => new InstanceConnectionSnapshot("inst-1", "Primary Node", InstanceConnectionState.Online, 10, 20, 45, 0, 100, DateTimeOffset.UtcNow, null),
                    2 => new InstanceConnectionSnapshot("inst-1", "Primary Node", InstanceConnectionState.Offline, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow, "Timeout"),
                    3 => new InstanceConnectionSnapshot("inst-1", "Primary Node", InstanceConnectionState.Online, 10, 20, 45, 0, 100, DateTimeOffset.UtcNow, null),
                    _ => new InstanceConnectionSnapshot("inst-1", "Primary Node", InstanceConnectionState.Offline, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow, null)
                };
            });

        using MultiInstanceCoordinator coordinator = new(repoMock.Object, clientMock.Object);
        await coordinator.ConnectInstanceAsync("inst-1", "pass".AsMemory());

        IReadOnlyList<InstanceAlertEvent> alert1 = await coordinator.PollAllConnectedAsync();
        Assert.Single(alert1);
        Assert.Contains("Timeout", alert1[0].Message, StringComparison.Ordinal);

        IReadOnlyList<InstanceAlertEvent> backOnline = await coordinator.PollAllConnectedAsync();
        Assert.Single(backOnline);
        Assert.Equal(InstanceAlertSeverity.Info, backOnline[0].Severity);

        IReadOnlyList<InstanceAlertEvent> alert2 = await coordinator.PollAllConnectedAsync();
        Assert.Single(alert2);
        Assert.Contains("Connection lost.", alert2[0].Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies <see cref="NodeApiException.CreateAsync"/> handles null <see cref="HttpResponseMessage.ReasonPhrase"/> values.
    /// </summary>
    [Fact]
    public async Task NodeApiException_CreateAsync_HandlesNullReasonPhrase()
    {
        // WHAT: Calls NodeApiException.CreateAsync on an HTTP response whose ReasonPhrase is null.
        // WHY:  Covers the response.ReasonPhrase ?? "Error" fallback branch inside the async factory.
        // HOW:  Constructs HttpResponseMessage with non-standard status code 599 and explicit null ReasonPhrase.
        using HttpResponseMessage response = new((HttpStatusCode)599)
        {
            ReasonPhrase = null,
            Content = new StringContent(string.Empty)
        };

        NodeApiException ex = await NodeApiException.CreateAsync(response);
        Assert.Equal(599, ex.StatusCode);
        Assert.Equal("HTTP 599 Error", ex.Title);
    }

    /// <summary>
    /// Verifies <see cref="LanguagePreferenceService"/> MySQL/MariaDB backend detection across every clause.
    /// </summary>
    [Fact]
    public void LanguagePreferenceService_BackendDetection_CoversAllMySqlIndicators()
    {
        // WHAT: Tests MySQL detection tokens ("mysql", "mariadb", "Port=3306", "Uid=") plus fallback.
        // WHY:  Coverlet 10.1.1 instruments each individual short-circuit OR clause on ResolveBackend.
        // HOW:  Constructs instances with connection strings isolating each clause individually.
        using LanguagePreferenceService mysql = new("Server=db;Database=nw;Protocol=mysql");
        using LanguagePreferenceService mariadb = new("Server=mariadb.internal;Database=nw");
        using LanguagePreferenceService port3306 = new("Server=db;Port=3306;Database=nw");
        using LanguagePreferenceService uid = new("Server=db;Uid=weaver;Pwd=secret;Database=nw");
        using LanguagePreferenceService sqlite = new("Data Source=:memory:");

        Assert.Equal(WeaverDatabaseBackend.MySql, mysql.Backend);
        Assert.Equal(WeaverDatabaseBackend.MySql, mariadb.Backend);
        Assert.Equal(WeaverDatabaseBackend.MySql, port3306.Backend);
        Assert.Equal(WeaverDatabaseBackend.MySql, uid.Backend);
        Assert.Equal(WeaverDatabaseBackend.Sqlite, sqlite.Backend);
    }

    /// <summary>
    /// Verifies <see cref="DesktopProgram"/> default stream writers, default mutex name, non-disposable coordinator,
    /// and local app-data directory resolution fallback sequentially without mutex races.
    /// </summary>
    [Fact]
    public async Task DesktopProgram_DefaultWritersMutexAndAppDataResolution_Covered()
    {
        // WHAT: Exercises Console.Out/Error fallbacks, DefaultMutexName on GUI/Daemon paths, non-IDisposable coordinator, and ResolveAppDataBaseDirectory.
        // WHY:  Covers async state machine null-coalescing and safe cast branches inside DesktopProgram.
        // HOW:  Runs null-writer and null-mutex invocations sequentially with pre-cancelled CTS and tests ResolveAppDataBaseDirectory directly.
        Assert.Equal(Path.GetTempPath(), DesktopProgram.ResolveAppDataBaseDirectory(null));
        Assert.Equal(Path.GetTempPath(), DesktopProgram.ResolveAppDataBaseDirectory("   "));
        Assert.Equal(_tempDir, DesktopProgram.ResolveAppDataBaseDirectory(_tempDir));

        int versionExit = await DesktopProgram.RunAsync(["--version"]);
        Assert.Equal(0, versionExit);

        Mock<IDesktopUiHost> uiHostMock = new();
        uiHostMock.Setup(h => h.RunUiAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        int guiExit = await DesktopProgram.RunAsync(["--gui"], new StringWriter(), new StringWriter(), uiHost: uiHostMock.Object, mutexName: null);
        Assert.Equal(0, guiExit);

        Mock<IBackgroundInstanceMonitor> monitorMock = new();
        monitorMock.Setup(m => m.ExecuteMonitoringTickAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        Mock<IMultiInstanceCoordinator> nonDisposableCoord = new();

        int daemonExit = await DesktopProgram.RunAsync(
            ["--headless-daemon"],
            new StringWriter(),
            new StringWriter(),
            mutexName: null,
            daemonMonitor: monitorMock.Object,
            daemonCoordinator: nonDisposableCoord.Object);
        Assert.Equal(0, daemonExit);
    }

    /// <summary>
    /// Verifies ViewModel severity escalation on lonely multi-year forecasts and null provider fallbacks across
    /// <see cref="DepotAndLotWorkspaceViewModel"/>, <see cref="FederationAndDatasetWorkspaceViewModel"/>, and
    /// <see cref="NodeAdministrationViewModel"/>.
    /// </summary>
    [Fact]
    public async Task ViewModels_NullProvidersAndMultiYearForecastSeverity_Covered()
    {
        // WHAT: Covers BuildSummary Optimal severity via LatestMultiYearForecast alone, and null provider/token fallbacks across ViewModels.
        // WHY:  Covers multi-condition OR and null-conditional provider invocation branches.
        // HOW:  Constructs ViewModels with null providers/tokens and explicit overrides, verifying exception and success paths.
        InstanceProfile profile = new("n1", "Node 1", "http://127.0.0.1:5000", true, "admin", "Administrator", "Ed25519", "P", "E", true, 15, DateTimeOffset.UtcNow, null);
        Mock<INanoWeaverNodeClient> clientMock = new();

        DepotAndLotWorkspaceViewModel depotVm = new(clientMock.Object, () => profile, () => "jwt")
        {
            SelectedDepotId = "depot-1"
        };
        Assert.Equal(ClinicalWorkspaceSeverity.Idle, depotVm.BuildSummary().Severity);

        MultiYearHomeostasisForecastReport report = new MultiYearHomeostasisForecastReport().GetExamples() with
        {
            DepotId = "depot-1",
            MitoticDriftAlert = false,
            ProjectedTenYearTtrPercent = 98.5
        };
        clientMock.Setup(c => c.GetLatestMultiYearHomeostasisForecastAsync(profile, "jwt", "depot-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);
        await depotVm.LoadLatestMultiYearHomeostasisForecastAsync();
        Assert.Equal(ClinicalWorkspaceSeverity.Optimal, depotVm.BuildSummary().Severity);

        FederationAndDatasetWorkspaceViewModel fedNoProvider = new(clientMock.Object, profileProvider: null, tokenProvider: null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fedNoProvider.ListShareKeysAsync());
        fedNoProvider.ActiveProfile = profile;
        fedNoProvider.ActiveBearerToken = "jwt-fed";
        clientMock.Setup(c => c.ListShareKeysAsync(profile, "jwt-fed", It.IsAny<CancellationToken>())).ReturnsAsync([]);
        IReadOnlyList<ShareKeyRecord> keys = await fedNoProvider.ListShareKeysAsync();
        Assert.Empty(keys);

        NodeAdministrationViewModel adminNullProviders = new(clientMock.Object, profileProvider: null, tokenProvider: null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => adminNullProviders.GetSystemHealthAsync());
        adminNullProviders.ActiveProfile = profile;
        SystemHealthOverview health = new()
        {
            Status = "Healthy",
            TimestampUtc = DateTimeOffset.UtcNow,
            Version = "1.0.0",
            Uptime = "1s",
            UptimeSeconds = 1.0,
            SetupRequired = false,
            AuthenticatedSession = true,
            Description = "ok"
        };
        clientMock.Setup(c => c.GetSystemHealthAsync(profile, null, It.IsAny<CancellationToken>())).ReturnsAsync(health);
        Assert.Same(health, await adminNullProviders.GetSystemHealthAsync());

        adminNullProviders.ActiveBearerToken = "explicit-token";
        clientMock.Setup(c => c.GetSystemHealthAsync(profile, "explicit-token", It.IsAny<CancellationToken>())).ReturnsAsync(health);
        Assert.Same(health, await adminNullProviders.GetSystemHealthAsync());

        clientMock.Setup(c => c.GetDepotFormulationsAsync(profile, "jwt", "p-lot-only", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        clientMock.Setup(c => c.GetFabricationLotsAsync(profile, "jwt", "p-lot-only", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new FabricationLotResponse().GetExamples() with { PatientId = "p-lot-only", LotStatus = FabricationLotStatus.Released }]);
        DepotWorkspaceSummary lotOnlySummary = await depotVm.LoadPatientWorkspaceAsync("p-lot-only");
        Assert.Equal("p-lot-only", depotVm.ActivePatientId);
        Assert.NotNull(lotOnlySummary);

        Mock<IInstanceProfileRepository> nullRepo = new();
        nullRepo.Setup(r => r.ListProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<InstanceProfile>)null!);
        Mock<IMultiInstanceCoordinator> nullCoord = new();
        nullCoord.Setup(c => c.GetSnapshots())
            .Returns((IReadOnlyList<InstanceConnectionSnapshot>)null!);
        using MainShellViewModel nullShellVm = new(nullRepo.Object, nullCoord.Object, clientMock.Object);
        await nullShellVm.InitializeAsync();
        Assert.Empty(nullShellVm.Profiles);
        Assert.Empty(nullShellVm.Snapshots);
    }

    /// <summary>
    /// Verifies mobile lifecycle hosts and disk-backed keystore bridges across null-attachment, null-vault,
    /// and missing-file delete scenarios.
    /// </summary>
    [Fact]
    public async Task MobileLifecycleAndKeystore_NullAttachmentsAndMissingDiskKey_Covered()
    {
        // WHAT: Exercises null vault/shell lifecycle transitions and DeleteKeyFromDisk when the file is absent on disk.
        // WHY:  Covers null-conditional operators and File.Exists(filePath) false branches across mobile abstractions.
        // HOW:  Invokes MainActivity/AppDelegate unattached, RunMobileLifecycleAsync with BiometricVault=null, and deletes after removing .key files.
        MainActivity unattachedActivity = new();
        unattachedActivity.OnPause();
        unattachedActivity.OnDestroy();

        using VaultUnlockViewModel vaultVm = new();
        unattachedActivity.AttachVaultViewModel(vaultVm);
        unattachedActivity.OnDestroy();
        Assert.True(unattachedActivity.IsDestroyed);

        AppDelegate unattachedDelegate = new();
        unattachedDelegate.DidEnterBackground();
        Assert.True(unattachedDelegate.IsBackgrounded);

        UnifiedMobileBiometricVault? originalVault = MobileEntryPoint.BiometricVault;
        try
        {
            MobileEntryPoint.BiometricVault = null;
            using CancellationTokenSource preCancelled = new();
            preCancelled.Cancel();
            Assert.Equal(0, await MobileEntryPoint.RunMobileLifecycleAsync("Android", new MobileNotificationSink("android"), preCancelled.Token));

            using CancellationTokenSource immediateCancel = new();
            immediateCancel.CancelAfter(15);
            Assert.Equal(0, await MobileEntryPoint.RunMobileLifecycleAsync("iOS", new MobileNotificationSink("ios"), immediateCancel.Token));
        }
        finally
        {
            MobileEntryPoint.BiometricVault = originalVault;
        }

        string androidDir = Path.Combine(_tempDir, "android-ks");
        DefaultAndroidKeyStoreBridge androidBridge = new(true, null, androidDir);
        androidBridge.StoreWrappedKey("missing-file-alias", new byte[32]);
        foreach (string keyFile in Directory.GetFiles(androidDir, "*.key"))
        {
            File.Delete(keyFile);
        }
        Assert.True(androidBridge.DeleteWrappedKey("missing-file-alias"));

        string appleDir = Path.Combine(_tempDir, "apple-se");
        DefaultAppleSecureEnclaveBridge appleBridge = new(true, null, appleDir);
        appleBridge.StoreKeychainSecret("missing-file-tag", new byte[32]);
        foreach (string keyFile in Directory.GetFiles(appleDir, "*.key"))
        {
            File.Delete(keyFile);
        }
        Assert.True(appleBridge.DeleteKeychainSecret("missing-file-tag"));
    }

    /// <summary>
    /// Verifies Valkey cache, connection factory, distributed lock, and event bridge edge branches.
    /// </summary>
    [Fact]
    public async Task ValkeySubsystems_NonDisposableFactoryLoggerAndTokenMismatch_Covered()
    {
        // WHAT: Covers non-disposable factory disposal, WeaverConfig fallback, null/explicit logger, LocalLockToken mismatch/double-dispose, and pre-prefixed channel publish.
        // WHY:  Covers synchronous and async branches introduced across caching and pub/sub bridge classes.
        // HOW:  Exercises ValkeyCacheService, ValkeyConnectionFactory, ValkeyLockService, and ValkeyEventBridge in isolation.
        Mock<IValkeyConnectionFactory> nonDisposableFactory = new();
        ValkeyCacheService cacheService = new(nonDisposableFactory.Object);
        cacheService.Dispose();

        IConfiguration weaverConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WeaverConfig:ValkeyConnectionString"] = "bad host string ::::"
            })
            .Build();
        using ValkeyConnectionFactory cfgFactory = new(weaverConfig);
        Assert.Equal("bad host string ::::", cfgFactory.SanitizedEndpoint);

        using ValkeyConnectionFactory withExplicitLogger = new(null, "custom-ep", NullLogger<ValkeyConnectionFactory>.Instance);
        Assert.Equal("custom-ep", withExplicitLogger.SanitizedEndpoint);

        Mock<IConnectionMultiplexer> muxMock = new();
        Mock<ISubscriber> subMock = new();
        muxMock.Setup(m => m.IsConnected).Returns(true);
        muxMock.Setup(m => m.GetSubscriber(It.IsAny<object?>())).Returns(subMock.Object);
        ValkeyConnectionFactory connectedFactory = new(muxMock.Object, "connected");

        ChannelEventBus bus = new();
        ValkeyEventBridge bridge = new(bus, connectedFactory);
        await bridge.PublishAsync("weaver:events:custom-already-prefixed", "{}");
        Assert.Equal(1, bridge.OutboundEventsCount);
        subMock.Verify(s => s.PublishAsync(It.Is<RedisChannel>(c => c.ToString() == "weaver:events:custom-already-prefixed"), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()), Times.Once);

        ValkeyLockService lockService = new(new ValkeyConnectionFactory(null, "local"));
        IAsyncDisposable? token1 = await lockService.AcquireLockAsync("lock-parity-res", TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(50));
        Assert.NotNull(token1);
        ValkeyLockService.LocalLockToken localToken1 = Assert.IsType<ValkeyLockService.LocalLockToken>(token1);
        Assert.True(await lockService.ReleaseLockAsync("lock-parity-res", localToken1.TokenValue));

        await using IAsyncDisposable? token2 = await lockService.AcquireLockAsync("lock-parity-res", TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(50));
        Assert.NotNull(token2);
        // Disposing stale token1 while lock-parity-res is held by token2 hits FixedTimeEquals == false without releasing token2's semaphore.
        await localToken1.DisposeAsync();
        await localToken1.DisposeAsync();
    }

    /// <summary>
    /// Verifies <see cref="DuckDbTrajectoryAnalyticsService"/> identifier scanning when a read-only SQL query
    /// terminates immediately with an identifier token, plus <see cref="WeaverDbConnectionFactory"/> DuckDB configuration resolution
    /// and <see cref="SystemConfigurationStore"/> null timestamp handling.
    /// </summary>
    [Fact]
    public async Task AnalyticsAndPersistence_IdentifierEofDuckDbPathAndNullUpdatedTimestamp_Covered()
    {
        // WHAT: Tests trailing identifier tokenization in DuckDB analytics, DUCKDB_DATABASE_PATH lookup, and null updated_at_utc rows in weaver_system_config.
        // WHY:  Exercises loop termination without trailing punctuation and SQLite null-timestamp reader mappings.
        // HOW:  Executes valid SELECT ending on identifier, constructs WeaverDbConnectionFactory with DUCKDB_DATABASE_PATH, and queries seeded config row.
        using DuckDbTrajectoryAnalyticsService analytics = new();
        TrajectoryAnalyticsResult queryRes = await analytics.ExecuteQueryAsync(new TrajectoryAnalyticsQuery("sample", "SELECT 1 AS a_1"));
        Assert.Single(queryRes.Rows);

        string dbIncoming = Path.Combine(_tempDir, "factory-db");
        Directory.CreateDirectory(dbIncoming);
        IConfiguration duckConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = dbIncoming,
                ["DUCKDB_DATABASE_PATH"] = Path.Combine(dbIncoming, "analytics.duckdb")
            })
            .Build();
        WeaverDbConnectionFactory duckFactory = new(duckConfig, new DatabaseMigrationRunner(), NullLogger<WeaverDbConnectionFactory>.Instance);
        Assert.Equal(WeaverDatabaseBackend.DuckDb, await duckFactory.EnsureBackendResolvedAsync());

        string cfgIncoming = Path.Combine(_tempDir, "cfg-db");
        Directory.CreateDirectory(cfgIncoming);
        IConfiguration sqliteConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = cfgIncoming
            })
            .Build();
        WeaverDbConnectionFactory sqliteFactory = new(sqliteConfig, new DatabaseMigrationRunner(), NullLogger<WeaverDbConnectionFactory>.Instance);
        await sqliteFactory.EnsureBackendResolvedAsync();

        await using (WeaverDbSession session = await sqliteFactory.OpenAsync())
        {
            await using System.Data.Common.DbCommand cmd = session.CreateCommand(
                "INSERT OR REPLACE INTO weaver_configuration (key, value, updated_at_utc) VALUES ('Simulation:EndpointUrl', 'http://override:8080', '2026-11-24T00:00:00Z');");
            await cmd.ExecuteNonQueryAsync();
        }

        using SystemConfigurationStore configStore = new(sqliteFactory);
        IReadOnlyList<ConfigurationItem> items = await configStore.GetAllConfigurationsAsync();
        ConfigurationItem overridden = Assert.Single(items, i => i.Key == "Simulation:EndpointUrl");
        Assert.Equal("http://override:8080", overridden.Value);
        Assert.NotNull(overridden.LastUpdatedUtc);

        IConfiguration emptyCfg = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
        IConfiguration primaryCfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["P_KEY"] = "cfg-primary" }).Build();
        IConfiguration secondaryCfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["S_KEY"] = "cfg-secondary" }).Build();

        Assert.Equal("cfg-primary", WeaverDbConnectionFactory.ResolveSettingWithFallback(primaryCfg, _ => "env-any", "P_KEY", "S_KEY"));
        Assert.Equal("env-primary", WeaverDbConnectionFactory.ResolveSettingWithFallback(emptyCfg, k => k == "P_KEY" ? "env-primary" : null, "P_KEY", "S_KEY"));
        Assert.Equal("cfg-secondary", WeaverDbConnectionFactory.ResolveSettingWithFallback(secondaryCfg, _ => null, "P_KEY", "S_KEY"));
        Assert.Equal("env-secondary", WeaverDbConnectionFactory.ResolveSettingWithFallback(emptyCfg, k => k == "S_KEY" ? "env-secondary" : null, "P_KEY", "S_KEY"));
        Assert.Null(WeaverDbConnectionFactory.ResolveSettingWithFallback(emptyCfg, _ => null, "P_KEY", "S_KEY"));
    }

    /// <summary>
    /// Verifies <see cref="ComplianceAuditService.DeIdentifySubject"/> across repeated header subject occurrences,
    /// trailing whitespace tags, all delimiter types, and trailing spaces.
    /// </summary>
    [Fact]
    public void ComplianceAuditService_DeIdentifySubject_CoversHeaderRepeatWhitespaceAndDelimiters()
    {
        // WHAT: Exercises repeated subject match in headers, trailing-whitespace tags, and all delimiter terminators in FindTagValueEnd.
        // WHY:  Covers every boundary condition in ScanAndExtractHeaderIdentifiers, SanitizeNonHeaderLine, and FindTagValueEnd.
        // HOW:  Constructs a multi-line genomic + clinical notes payload triggering every delimiter and whitespace path.
        using SubjectKeyTestHost host = SubjectKeyTestHost.CreateLoaded(Path.Combine(_tempDir, "audit-host"));
        ComplianceAuditService service = new(host.KeyManagement, host.CryptoProvider);

        string payload = string.Join('\n',
            ">patient_alpha_01|mrn:   111,dob:1990-01-01;phone:555-0100|email:\"a@b.c\"}hospital:   ",
            ">patient_alpha_01;mrn:222|mrn:   ",
            "Notes: mrn:   111,name:Alice\"dob:1990-01-01}phone:555-0100;ssn:123-45-6789|email:a@b.c   mrn:333   ",
            "Trailing empty tag: hospital:   ");

        DeIdentificationResult res = service.DeIdentifySubject("patient_alpha_01", payload, true, true);
        Assert.Contains("patient_alpha_01", res.StrippedIdentifiers);
        Assert.Contains("111", res.StrippedIdentifiers);
        Assert.Contains("333", res.StrippedIdentifiers);
    }

    /// <summary>
    /// Verifies <see cref="MainLayout.Dispose"/> handles uninjected null collaborators cleanly.
    /// </summary>
    [Fact]
    public void MainLayout_Dispose_WithUninjectedCollaborators_DoesNotThrow()
    {
        // WHAT: Invokes Dispose on a newly instantiated MainLayout before Blazor DI populates properties.
        // WHY:  Covers SessionState != null and Navigation != null false branches in MainLayout.Dispose.
        // HOW:  Constructs new MainLayout() directly and invokes Dispose().
        using MainLayout layout = new();
        Assert.NotNull(layout);
    }

    /// <summary>
    /// Verifies <see cref="AptamerAffinityAnalyzer"/>, <see cref="EpigeneticCircuitService"/>, <see cref="ColdChainStabilityService"/>,
    /// <see cref="FabricationService"/>, <see cref="ReversalAntidoteService"/>, and <see cref="GelAnalysisService"/> boundary conditions.
    /// </summary>
    [Fact]
    public async Task BiomedicalAndFabricationServices_EdgeGuardsAndPeaks_Covered()
    {
        // WHAT: Exercises out-of-range indices, null patient ID, non-finite/out-of-bounds temperatures, CRLF FASTA, empty antidote fields, and synthetic gel peaks.
        // WHY:  Covers defensive input validation and numerical loop boundary conditions across fabrication/endocrine services.
        // HOW:  Invokes each domain service method with boundary inputs directly.
        AtomCoordinate[] frame = [new AtomCoordinate(0f, 0f, 0f), new AtomCoordinate(1f, 0f, 0f)];
        int contacts = AptamerAffinityAnalyzer.CountBindingContacts(frame, [-1, 0, 999], 0, 5.0f);
        Assert.Equal(1, contacts);

        using SubjectKeyTestHost host = SubjectKeyTestHost.CreateLoaded(Path.Combine(_tempDir, "bio-host"));
        ComplianceAuditService audit = new(host.KeyManagement, host.CryptoProvider);

        Mock<IEpigeneticCircuitRepository> epiRepo = new();
        EpigeneticCircuitService epiService = new(epiRepo.Object, host.Protector, host.KeyManagement, host.CryptoProvider, audit);
        await Assert.ThrowsAsync<ArgumentException>(() => epiService.DesignCircuitAsync(new DesignEpigeneticCircuitRequest { PatientId = null! }, "op"));

        ColdChainStabilityService coldChain = new(Mock.Of<IFabricationLotRepository>(), Mock.Of<IColdChainCustodyRepository>(), host.Protector, audit);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => coldChain.RecordThermalExcursionAsync("lot-1", new RecordThermalExcursionRequest { TemperatureCelsius = double.NaN }, "op"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => coldChain.RecordThermalExcursionAsync("lot-1", new RecordThermalExcursionRequest { TemperatureCelsius = -300.0 }, "op"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => coldChain.RecordThermalExcursionAsync("lot-1", new RecordThermalExcursionRequest { TemperatureCelsius = 300.0 }, "op"));

        FabricationService fab = new();
        var comp = fab.AnalyzeSequenceComposition(">header\r\nATGC\r\n");
        Assert.Equal(4, comp.A + comp.T + comp.G + comp.C);

        ReversalAntidoteService antidoteSvc = new(
            Mock.Of<IFabricationLotRepository>(),
            Mock.Of<IReversalAntidoteRepository>(),
            host.KeyManagement,
            host.CryptoProvider,
            audit);
        ReversalAntidoteRecord baseAntidote = new ReversalAntidoteRecord().GetExamples();
        Assert.False(antidoteSvc.VerifyAntidotePairingSignature(baseAntidote with { PairedPayloadSha256 = "" }));
        Assert.False(antidoteSvc.VerifyAntidotePairingSignature(baseAntidote with { PairedSignatureBase64 = "" }));
        Assert.False(antidoteSvc.VerifyAntidotePairingSignature(baseAntidote with { SignerKeyId = "" }));

        GelAnalysisService gelSvc = new(NullLogger<GelAnalysisService>.Instance);
        Assert.Throws<ArgumentException>(() => gelSvc.AnalyzeGel(new GelAnalysisRequest { Width = 4, Height = 4, ImageBytes = null!, ExpectedLaneCount = 1, LadderLaneNumber = 1 }));
        Assert.Throws<ArgumentException>(() => gelSvc.AnalyzeGel(new GelAnalysisRequest { Width = 4, Height = 4, ImageBytes = [], ExpectedLaneCount = 1, LadderLaneNumber = 1 }));

        // Cover left == 0 boundary, ascending valley (left - 1 > left), zero valley (== 0.0), flat plateau peak (val == rowProfile[y + 1]), and right == height - 1 boundary.
        IReadOnlyList<GelAnalysisService.DetectedPeak> p1 = GelAnalysisService.DetectPeaksInProfile([8.0, 20.0, 10.0, 15.0, 15.0, 8.0], 6, 1);
        Assert.NotEmpty(p1);
        IReadOnlyList<GelAnalysisService.DetectedPeak> p2 = GelAnalysisService.DetectPeaksInProfile([0.0, 0.0, 20.0, 20.0, 10.0, 0.0], 6, 1);
        Assert.NotEmpty(p2);
        Assert.Empty(GelAnalysisService.DetectPeaksInProfile([20.0, 10.0, 5.0], 3, 1));
    }

    /// <summary>
    /// Verifies <see cref="FederatedPeerSyncService"/> peer ID character validation, <see cref="TrajectoryFederationService"/>
    /// resolution paths, <see cref="ContentFormatSerializer"/> single-character camelCase normalization, and
    /// <see cref="FormatNegotiationMiddleware"/> null response content-type passthrough.
    /// </summary>
    [Fact]
    public async Task FederationAndFormatting_PeerIdsPathsAndMiddleware_Covered()
    {
        // WHAT: Exercises >'z' peer ID validation, incoming/parquet path branches, single-char YAML keys, and null ContentType passthrough.
        // WHY:  Covers boundary character predicates, ternary resolution paths, and null-coalescing branches.
        // HOW:  Invokes UpsertSubscriptionAsync, ReadChunkAsync, NormalizeYamlObjectKeys, and FormatNegotiationMiddleware.
        using SubjectKeyTestHost host = SubjectKeyTestHost.CreateLoaded(Path.Combine(_tempDir, "fed-host"));
        ComplianceAuditService audit = new(host.KeyManagement, host.CryptoProvider);

        Mock<IHttpClientFactory> httpFactoryMock = new();
        FederatedPeerSyncService syncService = new(
            Mock.Of<IFederatedPeerSyncRepository>(),
            Mock.Of<IFabricationLotRepository>(),
            Mock.Of<IFabricationLotService>(),
            Mock.Of<IPeerKeyRegistry>(),
            audit,
            httpFactoryMock.Object,
            Mock.Of<IFederatedDnsResolver>());

        UpsertFederatedPeerSyncEndpointRequest validPeer = new UpsertFederatedPeerSyncEndpointRequest().GetExamples() with
        {
            PeerId = "peer_A.0-9z",
            BaseUrl = "http://invalid-url"
        };
        // Reaches URL validation after passing IsValidPeerId, covering every valid ASCII class in line 804.
        await Assert.ThrowsAsync<ArgumentException>(() => syncService.UpsertSubscriptionAsync(validPeer, "op"));
        // Rejects char > 'z' ('{'), covering false branch of (c <= 'z').
        await Assert.ThrowsAsync<ArgumentException>(() => syncService.UpsertSubscriptionAsync(validPeer with { PeerId = "bad{peer" }, "op"));

        string incomingDir = Path.Combine(_tempDir, "fed-incoming");
        string parquetDir = Path.Combine(_tempDir, "fed-parquet");
        Directory.CreateDirectory(incomingDir);
        Directory.CreateDirectory(parquetDir);
        await File.WriteAllBytesAsync(Path.Combine(parquetDir, "only_parquet.dcd"), [1, 2, 3, 4]);

        Mock<IConfigurationRepository> cfgRepoMock = new();
        cfgRepoMock.Setup(c => c.GetValueAsync("Storage:TrajectoryIncomingDir", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(incomingDir);
        cfgRepoMock.Setup(c => c.GetValueAsync("Storage:TrajectoryParquetDir", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(parquetDir);

        TrajectoryFederationService fedSvc = new(
            cfgRepoMock.Object,
            Mock.Of<IPeerKeyRegistry>(),
            host.SecretStore,
            NullLogger<TrajectoryFederationService>.Instance,
            host.CryptoProvider);

        byte[] foundParquet = await fedSvc.ReadChunkAsync("only_parquet.dcd", 0);
        Assert.Equal([1, 2, 3, 4], foundParquet);
        await Assert.ThrowsAsync<FileNotFoundException>(() => fedSvc.ReadChunkAsync("missing.parquet", 0));
        await Assert.ThrowsAsync<FileNotFoundException>(() => fedSvc.ReadChunkAsync("missing.xyz", 0));

        Dictionary<string, object?> singleCharKeyGraph = new()
        {
            ["a_b"] = "val"
        };
        object? normalized = ContentFormatSerializer.NormalizeYamlObjectKeys(singleCharKeyGraph);
        Dictionary<string, object?> normalizedDict = Assert.IsType<Dictionary<string, object?>>(normalized);
        Assert.True(normalizedDict.ContainsKey("aB"));

        DefaultHttpContext httpCtx = new();
        httpCtx.Request.Headers.Accept = "application/xml";
        using MemoryStream outStream = new();
        httpCtx.Response.Body = outStream;
        FormatNegotiationMiddleware middleware = new(async ctx =>
        {
            ctx.Response.ContentType = null;
            await ctx.Response.WriteAsync("plain");
        });
        await middleware.InvokeAsync(httpCtx);
        outStream.Seek(0, SeekOrigin.Begin);
        using StreamReader rdr = new(outStream);
        Assert.Equal("plain", await rdr.ReadToEndAsync());
    }

    /// <summary>
    /// Verifies zero-allocation trajectory parsers (<see cref="DcdPipeReader"/>, <see cref="LammpsCustomPipeReader"/>,
    /// <see cref="SimdCoordinateParser"/>) across malformed unit cells, partial columns, and ASCII bounds <c>&gt; 'z'</c>.
    /// </summary>
    [Fact]
    public async Task TrajectoryParsers_DcdUnitCellLammpsAndSimdBounds_Covered()
    {
        // WHAT: Tests corrupted DCD unit-cell leading/trailing markers, partial LAMMPS column headers, and SimdCoordinateParser > 'z' trailing/halting bytes.
        // WHY:  Covers branch paths in DcdPipeReader, LammpsCustomPipeReader, and SimdCoordinateParser.
        // HOW:  Streams synthetic DCD payloads with mismatched unit cell markers and invokes ResolveLammpsColumns and TryParseFloatSimd directly.
        static byte[] BuildDcdWithUnitCell(int ucLead, int ucTrail)
        {
            using MemoryStream ms = new();
            using BinaryWriter w = new(ms);
            w.Write(84);
            w.Write("CORD"u8);
            Span<byte> hdr = stackalloc byte[76];
            BinaryPrimitives.WriteInt32LittleEndian(hdr.Slice(36, 4), 1); // hasUnitCell = 1
            w.Write(hdr);
            w.Write(84);

            w.Write(4);
            w.Write("TEST"u8);
            w.Write(4);

            w.Write(4);
            w.Write(1); // atomCount = 1
            w.Write(4);

            w.Write(ucLead);
            w.Write(new byte[48]);
            w.Write(ucTrail);

            w.Write(4);
            w.Write(1.0f);
            w.Write(4);
            w.Write(4);
            w.Write(2.0f);
            w.Write(4);
            w.Write(4);
            w.Write(3.0f);
            w.Write(4);
            return ms.ToArray();
        }

        DcdPipeReader dcdReader = new();
        int framesEmitted = 0;
        await using (MemoryStream s1 = new(BuildDcdWithUnitCell(99, 48)))
        {
            await dcdReader.ParseAsync(PipeReader.Create(s1), (idx, span) => { framesEmitted++; return ValueTask.CompletedTask; });
        }
        await using (MemoryStream s2 = new(BuildDcdWithUnitCell(48, 99)))
        {
            await dcdReader.ParseAsync(PipeReader.Create(s2), (idx, span) => { framesEmitted++; return ValueTask.CompletedTask; });
        }
        await using (MemoryStream s3 = new(BuildDcdWithUnitCell(48, 48)))
        {
            await dcdReader.ParseAsync(PipeReader.Create(s3), (idx, span) => { framesEmitted++; return ValueTask.CompletedTask; });
        }
        Assert.Equal(1, framesEmitted);

        LammpsCustomPipeReader.ResolveLammpsColumns("id type"u8, out int cx, out int cy, out int cz);
        Assert.Equal((2, 3, 4), (cx, cy, cz));
        LammpsCustomPipeReader.ResolveLammpsColumns("x"u8, out cx, out cy, out cz);
        Assert.Equal((2, 3, 4), (cx, cy, cz));
        LammpsCustomPipeReader.ResolveLammpsColumns("x y"u8, out cx, out cy, out cz);
        Assert.Equal((2, 3, 4), (cx, cy, cz));
        LammpsCustomPipeReader.ResolveLammpsColumns("x z"u8, out cx, out cy, out cz);
        Assert.Equal((2, 3, 4), (cx, cy, cz));

        Assert.False(SimdCoordinateParser.TryParseFloatSimd("1e2."u8, out _, out _));
        Assert.False(SimdCoordinateParser.TryParseFloatSimd("1e2-"u8, out _, out _));
        Assert.False(SimdCoordinateParser.TryParseFloatSimd("1e2+"u8, out _, out _));
        Assert.False(SimdCoordinateParser.TryParseFloatSimd("1e2a"u8, out _, out _));
        Assert.False(SimdCoordinateParser.TryParseFloatSimd("1e2A"u8, out _, out _));
        Assert.True(SimdCoordinateParser.TryParseFloatSimd("1e2{"u8, out float sciCurly, out _));
        Assert.Equal(100f, sciCurly);

        Assert.False(SimdCoordinateParser.TryParseFloatSimd("1.2."u8, out _, out _));
        Assert.False(SimdCoordinateParser.TryParseFloatSimd("1.2-"u8, out _, out _));
        Assert.False(SimdCoordinateParser.TryParseFloatSimd("1.2+"u8, out _, out _));
        Assert.False(SimdCoordinateParser.TryParseFloatSimd("1.2a"u8, out _, out _));
        Assert.False(SimdCoordinateParser.TryParseFloatSimd("1.2A"u8, out _, out _));
        Assert.True(SimdCoordinateParser.TryParseFloatSimd("1.2{"u8, out float normCurly, out _));
        Assert.Equal(1.2f, normCurly, precision: 3);
    }

    /// <summary>
    /// Verifies authentication, manifest signature verification, profile/DNA repository fallbacks, stack service aliases,
    /// and campaign simulation boundary branches.
    /// </summary>
    [Fact]
    public async Task SecurityStorageStackAndCampaigns_BoundaryBranches_Covered()
    {
        // WHAT: Covers whitespace session cookie, empty Base64 bearer segments, unrecognized regimen enum in DB, manifest algorithm mismatch, null DNA collection fields, stack service aliases, and campaign boundaries.
        // WHY:  Closes all remaining genuine uncovered branches across Auth, Storage, Signing, StackServiceManager, and SimulationCampaignService.
        // HOW:  Executes direct repository/service calls over an isolated SQLite SubjectKeyTestHost and controlled mocks.
        DefaultHttpContext wsCookieCtx = new();
        wsCookieCtx.Request.Headers.Append("Cookie", "weaver_session=%20");
        Mock<IServerSessionActivityTracker> trackerMock = new();
        Program.RevokeActiveRequestSession(wsCookieCtx, trackerMock.Object);
        trackerMock.Verify(t => t.Revoke(It.IsAny<string>()), Times.Never);

        using SubjectKeyTestHost host = SubjectKeyTestHost.CreateLoaded(Path.Combine(_tempDir, "sec-host"));
        ComplianceAuditService audit = new(host.KeyManagement, host.CryptoProvider);
        using UserRepository userRepo = new(host.ConnectionFactory);
        await userRepo.InitializeAsync();
        using UserAuthenticationService authSvc = new(userRepo, host.CryptoProvider, audit, host.SecretStore);
        var (okEmptySeg, _, _) = await authSvc.ValidateTokenAsync(".sig");
        Assert.False(okEmptySeg);

        DatasetManifestSignatureVerifier verifier = new(host.CryptoProvider);
        using SubjectKeyTestHost dsHost = SubjectKeyTestHost.CreateLoaded(Path.Combine(_tempDir, "ds-sign-host"));
        NodeDatasetSigner edSigner = new(dsHost.SecretStore);
        DatasetManifest unsignedManifest = new()
        {
            DatasetName = "ds-parity",
            TimestampUtc = "2026-11-24T00:00:00Z",
            ArchiveFileName = "ds-parity.tar.gz",
            Sha256Checksum = new string('a', 64),
            SizeInBytes = 100
        };
        DatasetManifest signedEd = edSigner.Sign(unsignedManifest);
        Assert.False(verifier.Verify(signedEd, KeyAlgorithm.MlDsa65, edSigner.PublicKeyBase64));
        Assert.False(verifier.Verify(signedEd with { SignatureAlgorithm = "UnknownAlg" }, KeyAlgorithm.Ed25519, edSigner.PublicKeyBase64));

        await userRepo.CreateUserAsync(new UserAccount
        {
            Username = "patient-bad-regimen",
            Role = UserRole.Patient,
            AccessGate = AccessGate.ApiWeb,
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = edSigner.PublicKeyBase64,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            IsActive = true
        });

        using UserProfileRepository profileRepo = new(host.ConnectionFactory, host.Protector);
        await profileRepo.UpsertProfileAsync(new UserProfile
        {
            Username = "patient-bad-regimen",
            FirstName = null!,
            LastName = null!,
            Pronouns = null!,
            LanguagePreference = "   ",
            Regimen = TherapeuticRegimen.MaleToFemale,
            GenderAssignedAtBirth = GenderAssignedAtBirth.Male,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await using (WeaverDbSession session = await host.ConnectionFactory.OpenAsync())
        {
            await using System.Data.Common.DbCommand cmd = session.CreateCommand(
                "UPDATE weaver_user_profiles SET regimen = @reg WHERE username = 'patient-bad-regimen';");
            WeaverDbParameters.Add(cmd, "reg", host.Protector.EncryptRequiredField("NotAValidRegimen"));
            await cmd.ExecuteNonQueryAsync();
        }

        UserProfile? loadedProfile = await profileRepo.GetProfileAsync("patient-bad-regimen");
        Assert.NotNull(loadedProfile);
        Assert.Null(loadedProfile.Regimen);
        Assert.Equal("en-US", loadedProfile.LanguagePreference);

        using PatientDnaRepository dnaRepo = new(host.ConnectionFactory, host.Protector);
        await dnaRepo.UpsertDnaRecordAsync(new PatientDigitalDnaRecord().GetExamples() with
        {
            Id = "dna-null-lists",
            Username = "patient-bad-regimen",
            Staples = null!,
            AnnealingProtocol = null!
        });
        await dnaRepo.UpsertImportAsync(new PatientDnaImportRecord().GetExamples() with
        {
            Id = "imp-null-meta",
            Username = "patient-bad-regimen",
            MetadataJson = null!
        });
        await dnaRepo.UpsertResultAsync(new PatientResultRecord().GetExamples() with
        {
            Id = "res-null-metrics",
            Username = "patient-bad-regimen",
            DnaRecordId = null,
            ImportId = null,
            MetricsJson = null!
        });

        IConfiguration emptyHostCfg = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
        using SystemConfigurationStore stackCfgStore = new(host.ConnectionFactory);
        StackServiceManager stackMgr = new(stackCfgStore, emptyHostCfg);

        Assert.NotNull(await stackMgr.GetServiceByIdAsync("simulation-engine"));
        Assert.NotNull(await stackMgr.GetServiceByIdAsync("pg-duckdb"));
        Assert.NotNull(await stackMgr.GetServiceByIdAsync("database"));
        Assert.NotNull(await stackMgr.GetServiceByIdAsync("trajectory-parser"));
        Assert.NotNull(await stackMgr.GetServiceByIdAsync("parser"));
        Assert.NotNull(await stackMgr.GetServiceByIdAsync("peer-mesh"));
        Assert.NotNull(await stackMgr.GetServiceByIdAsync("peers"));
        Assert.Null(await stackMgr.GetServiceByIdAsync("unknown-service-id"));

        Assert.Equal("db.local", StackServiceManager.SanitizeEndpoint("Host=db.local;MalformedSegmentNoEquals"));
        Assert.Equal("Foo=1;Password=***", StackServiceManager.SanitizeEndpoint("Foo=1;;Password=secret;"));

        Mock<IPharmacogenomicsService> genMock = new();
        genMock.Setup(g => g.AnalyzeProfile(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<TherapeuticRegimen>()))
            .Returns(new PatientGenomicProfile
            {
                PatientId = "p1",
                Regimen = TherapeuticRegimen.MaleToFemale,
                ObservedMarkers = [],
                Alerts = [],
                ThrombosisRiskIndex = 1.0,
                OverallClearanceMultiplier = 1.0,
                CpGMethylationMandatory = false,
                RecommendedMaxEstradiolPmolL = 500.0,
                CalculatedAtUtc = "2026-11-24T00:00:00Z"
            });
        SimulationCampaignService campaignSvc = new(genMock.Object);

        SerumStabilityAnalysisResult zeroSteps = campaignSvc.SimulateSerumStability(new SerumStabilityRequest { SimulationSteps = 0 });
        Assert.True(zeroSteps.AverageRmsfNm > 0);

        AptamerGatingAnalysisResult lowerGc = campaignSvc.SimulateAptamerGating(new AptamerGatingRequest { AptamerSequence = "atgcATGC" });
        Assert.NotNull(lowerGc);

        PayloadLeakageAnalysisResult testoCyclo = campaignSvc.SimulatePayloadLeakage(new PayloadLeakageRequest
        {
            HormoneMolecule = "Testosterone",
            CavityFunctionalization = "Cyclodextrin"
        });
        Assert.Equal(8.4, testoCyclo.ReleaseRatePerMinuteOpen);

        PbpkEndocrineSimulationResult cappedNotExceeded = campaignSvc.SimulatePbpkEndocrine(new PbpkSimulationRequest
        {
            PatientId = "p1",
            Regimen = TherapeuticRegimen.MaleToFemale,
            DepotDoseMg = 10.0,
            GenomicMarkerIds = ["CUSTOM_CAP"],
            SimulationDurationDays = 30
        });
        Assert.False(cappedNotExceeded.ThromboticSurgeDetected);

        PbpkEndocrineSimulationResult highDoseFtm = campaignSvc.SimulatePbpkEndocrine(new PbpkSimulationRequest
        {
            PatientId = "p1",
            Regimen = TherapeuticRegimen.FemaleToMale,
            DepotDoseMg = 100.0,
            SimulationDurationDays = 30
        });
        Assert.True(highDoseFtm.AverageSteadyStateTestosteroneNmolL > 26.0);

        PbpkEndocrineSimulationResult lowDoseFtm = campaignSvc.SimulatePbpkEndocrine(new PbpkSimulationRequest
        {
            PatientId = "p1",
            Regimen = TherapeuticRegimen.FemaleToMale,
            DepotDoseMg = 5.0,
            SimulationDurationDays = 30
        });
        Assert.True(lowDoseFtm.AverageSteadyStateTestosteroneNmolL < 14.0);
    }
}
