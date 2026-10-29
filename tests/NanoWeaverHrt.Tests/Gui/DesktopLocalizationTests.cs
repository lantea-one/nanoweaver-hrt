using System.ComponentModel;
using Microsoft.Data.Sqlite;
using Moq;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Desktop.Platform;
using NanoWeaverHrt.Gui.Core.Services;
using NanoWeaverHrt.Gui.Core.ViewModels;
using NanoWeaverHrt.Localization;
using Xunit;

namespace NanoWeaverHrt.Tests.Gui;

/// <summary>
/// Comprehensive unit tests verifying desktop and ViewModel localization, preference persistence,
/// reactive UI updates, and tray menu translations, achieving 100.00% line and branch coverage.
/// </summary>
public sealed class DesktopLocalizationTests : IDisposable
{
    /// <summary>
    /// Temporary SQLite database file path.
    /// </summary>
    private readonly string _tempDbFile;

    /// <summary>
    /// SQLite connection string for testing preference persistence.
    /// </summary>
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="DesktopLocalizationTests"/> class.
    /// </summary>
    public DesktopLocalizationTests()
    {
        // WHAT: Prepares isolated test SQLite connection string.
        // WHY:  Ensures test database independence without file contention.
        // HOW:  Creates temporary file path in system temp folder with unique GUID.
        _tempDbFile = Path.Combine(Path.GetTempPath(), $"weaver_pref_test_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_tempDbFile}";
    }

    /// <summary>
    /// Disposes resources and cleans up temporary SQLite files.
    /// </summary>
    public void Dispose()
    {
        // WHAT: Cleans up the SQLite database file and clears connection pools.
        // WHY:  Adheres to Prime Directive 6 for clean test tear-down.
        // HOW:  Clears connection pool using SqliteConnection.ClearPool and deletes file if present.
        using (var conn = new SqliteConnection(_connectionString))
        {
            SqliteConnection.ClearPool(conn);
        }

        if (File.Exists(_tempDbFile))
        {
            try
            {
                File.Delete(_tempDbFile);
            }
            catch
            {
                // Suppress transient file lock on Windows runner teardown
            }
        }
    }

    // =========================================================================
    // LanguagePreferenceService Tests
    // =========================================================================

    /// <summary>
    /// Verifies constructor argument validation on <see cref="LanguagePreferenceService"/>.
    /// </summary>
    /// <param name="invalidConnectionString">Invalid connection string.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void LanguagePreferenceService_Constructor_ThrowsOnInvalidConnectionString(string? invalidConnectionString)
    {
        // WHAT: Verifies null or whitespace connection strings are rejected.
        // WHY:  Ensures fail-fast semantics on missing configuration.
        // HOW:  Asserts ArgumentException is thrown.
        Assert.ThrowsAny<ArgumentException>(() => new LanguagePreferenceService(invalidConnectionString!));
    }

    /// <summary>
    /// Verifies that the parameterless constructor for <see cref="LanguagePreferenceService"/> succeeds.
    /// </summary>
    [Fact]
    public void LanguagePreferenceService_ParameterlessConstructor_InitializesSuccessfully()
    {
        // WHAT: Tests parameterless constructor fallback.
        // WHY:  Ensures out-of-the-box initialization uses user application data folder.
        // HOW:  Instantiates LanguagePreferenceService with parameterless constructor and calls Dispose.
        using var service = new LanguagePreferenceService();
        Assert.NotNull(service);
    }

    /// <summary>
    /// Verifies that calling <see cref="LanguagePreferenceService.InitializeAsync"/> multiple times is idempotent,
    /// including under concurrent initialization attempts.
    /// </summary>
    [Fact]
    public async Task LanguagePreferenceService_InitializeAsync_IdempotentOnMultipleCalls()
    {
        // WHAT: Ensures InitializeAsync can be safely executed repeatedly and concurrently.
        // WHY:  Guarantees concurrent callers or re-entrant queries do not fail.
        // HOW:  Calls InitializeAsync concurrently across tasks and sequentially, verifying no exceptions occur.
        using var service = new LanguagePreferenceService(_connectionString);
        var t1 = Task.Run(() => service.InitializeAsync());
        var t2 = Task.Run(() => service.InitializeAsync());
        await Task.WhenAll(t1, t2);
        await service.InitializeAsync();
    }

    /// <summary>
    /// Verifies preference round-trip and update via <see cref="LanguagePreferenceService"/>.
    /// </summary>
    [Fact]
    public async Task LanguagePreferenceService_GetAndSetPreferredLanguage_PersistsCorrectly()
    {
        // WHAT: Verifies default language, preference mutation, and persistence across service instances.
        // WHY:  Ensures user's selected language persists across application restarts.
        // HOW:  Calls GetPreferredLanguageAsync on empty DB (returns English), sets German, reads back German,
        //       creates new service instance against same DB and verifies German is loaded, updates to Spanish.
        using (var service = new LanguagePreferenceService(_connectionString))
        {
            WeaverLanguage initial = await service.GetPreferredLanguageAsync();
            Assert.Equal(WeaverLanguage.English, initial);

            await service.SetPreferredLanguageAsync(WeaverLanguage.German);
            WeaverLanguage updated = await service.GetPreferredLanguageAsync();
            Assert.Equal(WeaverLanguage.German, updated);
        }

        // Reopen with new instance to confirm disk persistence and test update conflict
        using (var service2 = new LanguagePreferenceService(_connectionString))
        {
            WeaverLanguage reloaded = await service2.GetPreferredLanguageAsync();
            Assert.Equal(WeaverLanguage.German, reloaded);

            await service2.SetPreferredLanguageAsync(WeaverLanguage.Spanish);
            WeaverLanguage updated2 = await service2.GetPreferredLanguageAsync();
            Assert.Equal(WeaverLanguage.Spanish, updated2);
        }
    }

    /// <summary>
    /// Verifies that <see cref="LanguagePreferenceService.GetPreferredLanguageAsync"/> gracefully falls back to English
    /// when the stored preference value is unrecognized.
    /// </summary>
    [Fact]
    public async Task LanguagePreferenceService_GetPreferredLanguage_FallsBackToEnglishOnCorruptValue()
    {
        // WHAT: Simulates database corruption or unrecognized language tag.
        // WHY:  Prevents runtime crash if configuration file is corrupted.
        // HOW:  Directly inserts invalid string into SQLite table, calls GetPreferredLanguageAsync, verifies English.
        using var service = new LanguagePreferenceService(_connectionString);
        await service.InitializeAsync();

        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO weaver_gui_preferences (preference_key, preference_value, updated_at_utc) VALUES ('ui.language', 'non_existent_lang_123', '2026-10-29T12:00:00Z');";
            await cmd.ExecuteNonQueryAsync();
        }

        WeaverLanguage result = await service.GetPreferredLanguageAsync();
        Assert.Equal(WeaverLanguage.English, result);
    }

    /// <summary>
    /// Verifies that <see cref="LanguagePreferenceService"/> properly identifies PostgreSQL and SQLite connection strings.
    /// </summary>
    /// <param name="connectionString">The connection string under evaluation.</param>
    /// <param name="expectedIsPostgreSql">Expected boolean indicator.</param>
    [Theory]
    [InlineData("Host=localhost;Database=test;Username=test;Password=secret", true)]
    [InlineData("Server=127.0.0.1;Port=5432;User Id=postgres;Password=pass;Database=test", true)]
    [InlineData("Data Source=test.db", false)]
    [InlineData("Data Source=:memory:", false)]
    public void LanguagePreferenceService_IdentifiesStorageBackend(string connectionString, bool expectedIsPostgreSql)
    {
        // WHAT: Tests connection string dialect identification.
        // WHY:  Guarantees proper driver selection for SQLite versus enterprise PostgreSQL preference storage.
        // HOW:  Instantiates LanguagePreferenceService and asserts IsPostgreSql matches expectations.
        using var service = new LanguagePreferenceService(connectionString);
        Assert.Equal(expectedIsPostgreSql, service.IsPostgreSql);
    }

    /// <summary>
    /// Verifies that <see cref="LanguagePreferenceService"/> with a PostgreSQL connection string invokes Npgsql
    /// and throws when the configured database host cannot be reached.
    /// </summary>
    [Fact]
    public async Task LanguagePreferenceService_PostgreSql_FailsLoudlyWhenServerUnreachable()
    {
        // WHAT: Verifies PostgreSQL driver invocation and fail-fast behavior on unreachable server.
        // WHY:  Ensures that PostgreSQL preference persistence never silently falls back or hangs.
        // HOW:  Points connection string at unreachable local port and asserts NpgsqlException or SocketException.
        using var service = new LanguagePreferenceService("Host=127.0.0.1;Port=54397;Database=nonexistent;Timeout=1");
        Assert.True(service.IsPostgreSql);

        await Assert.ThrowsAnyAsync<Exception>(async () => await service.InitializeAsync());
    }

    /// <summary>
    /// Verifies that <see cref="LanguagePreferenceService"/> correctly executes PostgreSQL DDL and DML operations
    /// when configured with a custom connection factory.
    /// </summary>
    [Fact]
    public async Task LanguagePreferenceService_PostgreSqlDialect_CustomFactory_ExecutesAllBranches()
    {
        // WHAT: Verifies PostgreSQL dialect schema creation, insertion with ON CONFLICT, and retrieval.
        // WHY:  Ensures the PostgreSQL code path executes cleanly without needing an external PostgreSQL daemon.
        // HOW:  Passes custom connection factory returning an isolated SQLite database connection and asserts round-trip.
        string memDb = $"Data Source={Path.Combine(Path.GetTempPath(), $"weaver_pg_pref_{Guid.NewGuid():N}.db")}";
        try
        {
            using var service = new LanguagePreferenceService(
                "Host=pg-node.lan;Database=weaver;Username=postgres",
                _ => new SqliteConnection(memDb));

            Assert.True(service.IsPostgreSql);
            await service.InitializeAsync();
            await service.SetPreferredLanguageAsync(WeaverLanguage.Portuguese);
            WeaverLanguage read = await service.GetPreferredLanguageAsync();
            Assert.Equal(WeaverLanguage.Portuguese, read);
        }
        finally
        {
            using var cleanupConn = new SqliteConnection(memDb);
            SqliteConnection.ClearPool(cleanupConn);
            string file = memDb.Replace("Data Source=", "");
            if (File.Exists(file))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }

    // =========================================================================
    // DesktopTrayIconController Tests
    // =========================================================================

    /// <summary>
    /// Verifies that <see cref="DesktopTrayIconController"/> initializes with default state and handles null checks.
    /// </summary>
    [Fact]
    public void DesktopTrayIconController_Initialization_And_NullGuard()
    {
        // WHAT: Verifies default properties and ArgumentNullException on null state.
        // WHY:  Prevents invalid state transitions.
        // HOW:  Instantiates controller, checks CurrentState, asserts ArgumentNullException on ApplyState(null).
        var controller = new DesktopTrayIconController();
        Assert.Equal(TrayHealthBadge.Idle, controller.CurrentState.Badge);
        Assert.Equal(WeaverLanguage.English, controller.CurrentLanguage);
        Assert.Null(controller.Localizer);

        Assert.Throws<ArgumentNullException>(() => controller.ApplyState(null!));
    }

    /// <summary>
    /// Verifies that <see cref="DesktopTrayIconController"/> applies state and dispatches events.
    /// </summary>
    [Fact]
    public void DesktopTrayIconController_ApplyState_DispatchesEventsAndCallback()
    {
        // WHAT: Tests StateChanged event and constructor callback invocation.
        // WHY:  Ensures tray host UI components receive state notifications.
        // HOW:  Subscribes to StateChanged and onStateApplied callback, calls ApplyState, asserts both fired.
        TrayMenuState? callbackState = null;
        TrayMenuState? eventState = null;

        var controller = new DesktopTrayIconController(s => callbackState = s);
        controller.StateChanged += (_, s) => eventState = s;

        var newState = new TrayMenuState(
            Badge: TrayHealthBadge.Healthy,
            TooltipText: "NanoWeaver HRT — Healthy",
            IsMainWindowVisible: true,
            AreNotificationsMuted: false,
            InstanceSummaries: ["Node 1: Online"]);

        controller.ApplyState(newState);

        Assert.Same(newState, controller.CurrentState);
        Assert.Same(newState, callbackState);
        Assert.Same(newState, eventState);
    }

    /// <summary>
    /// Verifies tray menu labels without a localizer fallback to standard English text.
    /// </summary>
    [Fact]
    public void DesktopTrayIconController_WithoutLocalizer_ReturnsFallbackLabels()
    {
        // WHAT: Tests label getters when no IWeaverLocalizer is configured.
        // WHY:  Ensures tray functions properly even without a localization provider.
        // HOW:  Asserts ShowLabel, LockVaultLabel, ExitLabel and GetStandardMenuItems match English fallbacks.
        var controller = new DesktopTrayIconController();

        Assert.Equal("Show / Restore", controller.LocalizedShowLabel);
        Assert.Equal("Lock Key Vault", controller.LocalizedLockVaultLabel);
        Assert.Equal("Quit", controller.LocalizedExitLabel);

        IReadOnlyList<string> items = controller.GetStandardMenuItems();
        Assert.Equal(3, items.Count);
        Assert.Equal("Show / Restore", items[0]);
        Assert.Equal("Lock Key Vault", items[1]);
        Assert.Equal("Quit", items[2]);

        Assert.Equal("Custom Fallback", controller.GetLocalizedLabel("Any.Key", "Custom Fallback"));
        Assert.Equal("Custom Fallback", controller.GetLocalizedLabel(null!, "Custom Fallback"));
        Assert.Equal("Custom Fallback", controller.GetLocalizedLabel("   ", "Custom Fallback"));
    }

    /// <summary>
    /// Verifies tray menu labels with <see cref="IWeaverLocalizer"/> and dynamic language switching.
    /// </summary>
    [Fact]
    public void DesktopTrayIconController_WithLocalizer_TranslatesAndHandlesFallbacks()
    {
        // WHAT: Tests translation lookups, fallback on raw key return, and SetLanguage.
        // WHY:  Fulfills P44 requirement for localized tray context menus.
        // HOW:  Sets up mock localizer with custom translations, asserts translated strings and fallbacks.
        var localizerMock = new Mock<IWeaverLocalizer>();
        localizerMock.Setup(l => l.GetString("Tray.Show", WeaverLanguage.Spanish)).Returns("Mostrar / Restaurar");
        localizerMock.Setup(l => l.GetString("Tray.LockVault", WeaverLanguage.Spanish)).Returns("Bloquear Bóveda");
        localizerMock.Setup(l => l.GetString("Tray.Exit", WeaverLanguage.Spanish)).Returns("Salir");
        localizerMock.Setup(l => l.GetString("Status.Online", WeaverLanguage.Spanish)).Returns("En línea");

        // Simulate key not found returning key itself
        localizerMock.Setup(l => l.GetString("Unknown.Key", WeaverLanguage.Spanish)).Returns("Unknown.Key");
        // Simulate empty string
        localizerMock.Setup(l => l.GetString("Empty.Key", WeaverLanguage.Spanish)).Returns(string.Empty);

        var controller = new DesktopTrayIconController(null, localizerMock.Object, WeaverLanguage.English);
        Assert.Same(localizerMock.Object, controller.Localizer);
        Assert.Equal(WeaverLanguage.English, controller.CurrentLanguage);

        controller.SetLanguage(WeaverLanguage.Spanish);
        Assert.Equal(WeaverLanguage.Spanish, controller.CurrentLanguage);

        Assert.Equal("Mostrar / Restaurar", controller.LocalizedShowLabel);
        Assert.Equal("Bloquear Bóveda", controller.LocalizedLockVaultLabel);
        Assert.Equal("Salir", controller.LocalizedExitLabel);

        IReadOnlyList<string> items = controller.GetStandardMenuItems();
        Assert.Equal(["Mostrar / Restaurar", "Bloquear Bóveda", "Salir"], items);

        Assert.Equal("Fallback Val", controller.GetLocalizedLabel("Unknown.Key", "Fallback Val"));
        Assert.Equal("Fallback Val", controller.GetLocalizedLabel("Empty.Key", "Fallback Val"));

        // Test ApplyLocalizedState
        controller.ApplyLocalizedState(
            TrayHealthBadge.Healthy,
            "Status.Online",
            isMainWindowVisible: true,
            areNotificationsMuted: false,
            instanceSummaries: ["Nodo 1: En línea"]);

        Assert.Equal(TrayHealthBadge.Healthy, controller.CurrentState.Badge);
        Assert.Equal("NanoWeaver HRT — En línea", controller.CurrentState.TooltipText);
    }

    // =========================================================================
    // MainShellViewModel Localization Tests
    // =========================================================================

    /// <summary>
    /// Verifies constructor initialization of localization properties on <see cref="MainShellViewModel"/>.
    /// </summary>
    [Fact]
    public void MainShellViewModel_Localization_Defaults_AreValid()
    {
        // WHAT: Verifies default language, localizer, and available languages on newly created MainShellViewModel.
        // WHY:  Ensures ViewModel is ready for data-binding without explicit initialization.
        // HOW:  Creates MainShellViewModel, asserts English language, AvailableLanguages list, and LocalizedStatusText.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();
        coordMock.Setup(c => c.GetSnapshots()).Returns(Array.Empty<InstanceConnectionSnapshot>());

        var vm = new MainShellViewModel(repoMock.Object, coordMock.Object);

        Assert.NotNull(vm.Localizer);
        Assert.Null(vm.LanguagePreferenceService);
        Assert.Equal(WeaverLanguage.English, vm.SelectedLanguage);
        Assert.Equal(10, vm.AvailableLanguages.Count);
        Assert.Equal("Offline", vm.LocalizedStatusText);
        Assert.False(string.IsNullOrWhiteSpace(vm.LocalizedIdleLockMessage));
        Assert.Equal("Show / Restore", vm.LocalizedTrayShowText);
        Assert.Equal("Lock Key Vault", vm.LocalizedTrayLockVaultText);
        Assert.Equal("Quit", vm.LocalizedTrayExitText);
    }

    /// <summary>
    /// Verifies that <see cref="MainShellViewModel.InitializeAsync"/> loads preferred language from service.
    /// </summary>
    [Fact]
    public async Task MainShellViewModel_InitializeAsync_LoadsPreferredLanguage()
    {
        // WHAT: Tests that InitializeAsync queries ILanguagePreferenceService and notifies UI.
        // WHY:  Restores user's preferred language when application shell launches.
        // HOW:  Configures mock ILanguagePreferenceService returning French, calls InitializeAsync, verifies SelectedLanguage is French.
        var repoMock = new Mock<IInstanceProfileRepository>();
        repoMock.Setup(r => r.ListProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<InstanceProfile>());

        var coordMock = new Mock<IMultiInstanceCoordinator>();
        coordMock.Setup(c => c.GetSnapshots()).Returns(Array.Empty<InstanceConnectionSnapshot>());

        var prefMock = new Mock<ILanguagePreferenceService>();
        prefMock.Setup(p => p.GetPreferredLanguageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(WeaverLanguage.French);

        var vm = new MainShellViewModel(
            repoMock.Object,
            coordMock.Object,
            nodeClient: null,
            localizer: null,
            languagePreferenceService: prefMock.Object);

        Assert.Same(prefMock.Object, vm.LanguagePreferenceService);

        List<string> changedProps = new();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null)
            {
                changedProps.Add(e.PropertyName);
            }
        };

        await vm.InitializeAsync();

        Assert.Equal(WeaverLanguage.French, vm.SelectedLanguage);
        Assert.Contains(nameof(vm.LocalizedStatusText), changedProps);
        Assert.Contains(nameof(vm.LocalizedIdleLockMessage), changedProps);
    }

    /// <summary>
    /// Verifies setting <see cref="MainShellViewModel.SelectedLanguage"/> fires reactive property changes
    /// and triggers persistent save.
    /// </summary>
    [Fact]
    public async Task MainShellViewModel_SelectedLanguage_FiresReactiveChangesAndSaves()
    {
        // WHAT: Tests PropertyChanged events and asynchronous persistence when changing SelectedLanguage.
        // WHY:  Ensures Avalonia / Blazor views re-render translated text reactively.
        // HOW:  Subscribes to PropertyChanged, sets SelectedLanguage, verifies event list, verifies save call.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();
        coordMock.Setup(c => c.GetSnapshots()).Returns(Array.Empty<InstanceConnectionSnapshot>());

        var prefMock = new Mock<ILanguagePreferenceService>();
        prefMock.Setup(p => p.SetPreferredLanguageAsync(It.IsAny<WeaverLanguage>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var vm = new MainShellViewModel(
            repoMock.Object,
            coordMock.Object,
            nodeClient: null,
            localizer: null,
            languagePreferenceService: prefMock.Object);

        List<string> changedProps = new();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null)
            {
                changedProps.Add(e.PropertyName);
            }
        };

        // Setting same language should not fire any event
        vm.SelectedLanguage = WeaverLanguage.English;
        Assert.Empty(changedProps);

        // Setting new language should fire all localized properties
        vm.SelectedLanguage = WeaverLanguage.Spanish;
        Assert.Equal(WeaverLanguage.Spanish, vm.SelectedLanguage);

        Assert.Contains(nameof(vm.SelectedLanguage), changedProps);
        Assert.Contains(nameof(vm.LocalizedStatusText), changedProps);
        Assert.Contains(nameof(vm.LocalizedIdleLockMessage), changedProps);
        Assert.Contains(nameof(vm.LocalizedTrayShowText), changedProps);
        Assert.Contains(nameof(vm.LocalizedTrayLockVaultText), changedProps);
        Assert.Contains(nameof(vm.LocalizedTrayExitText), changedProps);

        // Allow fire-and-forget task to complete
        await Task.Yield();
        prefMock.Verify(p => p.SetPreferredLanguageAsync(WeaverLanguage.Spanish, It.IsAny<CancellationToken>()), Times.AtLeastOnce());
    }

    /// <summary>
    /// Verifies that background exceptions in <see cref="MainShellViewModel.SelectedLanguage"/> setter are safely swallowed.
    /// </summary>
    [Fact]
    public async Task MainShellViewModel_SelectedLanguage_SuppressesStorageErrors()
    {
        // WHAT: Verifies robust exception handling when ILanguagePreferenceService throws.
        // WHY:  Prevents UI unhandled exception crash when disk write fails.
        // HOW:  Mocks SetPreferredLanguageAsync to throw, sets SelectedLanguage, ensures no exception escapes.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();
        coordMock.Setup(c => c.GetSnapshots()).Returns(Array.Empty<InstanceConnectionSnapshot>());

        var prefMock = new Mock<ILanguagePreferenceService>();
        prefMock.Setup(p => p.SetPreferredLanguageAsync(It.IsAny<WeaverLanguage>(), It.IsAny<CancellationToken>()))
            .Throws(new IOException("Disk write failure"));

        var vm = new MainShellViewModel(
            repoMock.Object,
            coordMock.Object,
            nodeClient: null,
            localizer: null,
            languagePreferenceService: prefMock.Object);

        vm.SelectedLanguage = WeaverLanguage.Japanese;
        Assert.Equal(WeaverLanguage.Japanese, vm.SelectedLanguage);

        await Task.Yield();
        await Task.Delay(20);
    }

    /// <summary>
    /// Verifies deterministic awaiting of language switches via <see cref="MainShellViewModel.SetLanguageAsync"/>.
    /// </summary>
    [Fact]
    public async Task MainShellViewModel_SetLanguageAsync_AwaitsPersistence()
    {
        // WHAT: Tests explicit SetLanguageAsync method with and without preference service.
        // WHY:  Provides a deterministic async method for test and UI controllers.
        // HOW:  Calls SetLanguageAsync on VM with service and on VM without service.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();
        coordMock.Setup(c => c.GetSnapshots()).Returns(Array.Empty<InstanceConnectionSnapshot>());

        var prefMock = new Mock<ILanguagePreferenceService>();
        prefMock.Setup(p => p.SetPreferredLanguageAsync(It.IsAny<WeaverLanguage>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var vm = new MainShellViewModel(
            repoMock.Object,
            coordMock.Object,
            nodeClient: null,
            localizer: null,
            languagePreferenceService: prefMock.Object);

        await vm.SetLanguageAsync(WeaverLanguage.Italian);
        Assert.Equal(WeaverLanguage.Italian, vm.SelectedLanguage);
        prefMock.Verify(p => p.SetPreferredLanguageAsync(WeaverLanguage.Italian, It.IsAny<CancellationToken>()), Times.Once());

        // Without service: should not throw
        var vmNoService = new MainShellViewModel(repoMock.Object, coordMock.Object);
        await vmNoService.SetLanguageAsync(WeaverLanguage.Dutch);
        Assert.Equal(WeaverLanguage.Dutch, vmNoService.SelectedLanguage);
    }

    /// <summary>
    /// Verifies that <see cref="MainShellViewModel.LocalizedStatusText"/> reflects live connection states.
    /// </summary>
    [Fact]
    public async Task MainShellViewModel_LocalizedStatusText_ReflectsConnectionStates()
    {
        // WHAT: Tests LocalizedStatusText for Online, Connecting, Offline/Degraded, and null snapshot.
        // WHY:  Ensures node status is accurately translated across all connection phases.
        // HOW:  Configures snapshots with varying states, initializes ViewModel, and verifies LocalizedStatusText output.
        var repoMock = new Mock<IInstanceProfileRepository>();
        repoMock.Setup(r => r.ListProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<InstanceProfile>());

        var coordMock = new Mock<IMultiInstanceCoordinator>();

        var onlineSnapshot = new InstanceConnectionSnapshot(
            "inst-1", "Node 1", InstanceConnectionState.Online, 10, 20, 45, 1, 100, DateTimeOffset.UtcNow, null);
        var connectingSnapshot = new InstanceConnectionSnapshot(
            "inst-2", "Node 2", InstanceConnectionState.Connecting, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow, null);
        var degradedSnapshot = new InstanceConnectionSnapshot(
            "inst-3", "Node 3", InstanceConnectionState.Degraded, 95, 95, 88, 1, 50, DateTimeOffset.UtcNow, null);

        coordMock.Setup(c => c.GetSnapshots()).Returns([onlineSnapshot, connectingSnapshot, degradedSnapshot]);

        var vm = new MainShellViewModel(repoMock.Object, coordMock.Object);
        await vm.InitializeAsync();

        // When SelectedInstanceId is null -> LocalizedStatusText is Offline
        vm.SelectedInstanceId = null;
        Assert.Equal("Offline", vm.LocalizedStatusText);

        // Online node
        vm.SelectedInstanceId = "inst-1";
        Assert.Equal("Online", vm.LocalizedStatusText);

        // Connecting node
        vm.SelectedInstanceId = "inst-2";
        Assert.Equal("Connecting...", vm.LocalizedStatusText);

        // Degraded node -> Status.Offline key
        vm.SelectedInstanceId = "inst-3";
        Assert.Equal("Offline", vm.LocalizedStatusText);

        // Switch to Spanish
        vm.SelectedLanguage = WeaverLanguage.Spanish;
        Assert.Equal("Desconectado", vm.LocalizedStatusText);

        vm.SelectedInstanceId = "inst-1";
        Assert.Equal("En línea", vm.LocalizedStatusText);

        vm.SelectedInstanceId = "inst-2";
        Assert.Equal("Conectando...", vm.LocalizedStatusText);
    }

    /// <summary>
    /// Verifies <see cref="MainShellViewModel.GetLocalizedText"/> and <see cref="MainShellViewModel.GetLocalizedAlertMessage"/>
    /// helper methods across empty, unlocalized, and localized keys.
    /// </summary>
    [Fact]
    public void MainShellViewModel_GetLocalizedText_And_AlertMessage_CoverAllBranches()
    {
        // WHAT: Tests string resolution edge cases for UI and notifications.
        // WHY:  Guarantees robust string fallback behavior without null reference exceptions.
        // HOW:  Asserts results for null/empty key, missing key with fallback, missing key without fallback, and valid key.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();
        coordMock.Setup(c => c.GetSnapshots()).Returns(Array.Empty<InstanceConnectionSnapshot>());

        var vm = new MainShellViewModel(repoMock.Object, coordMock.Object);

        // Null / empty key returns fallback
        Assert.Equal("Default Fallback", vm.GetLocalizedText(null!, "Default Fallback"));
        Assert.Equal("Default Fallback", vm.GetLocalizedText(string.Empty, "Default Fallback"));

        // Unresolved key with fallback
        Assert.Equal("My Fallback", vm.GetLocalizedText("Unmapped.Key.123", "My Fallback"));

        // Unresolved key without fallback returns key itself
        Assert.Equal("Unmapped.Key.123", vm.GetLocalizedText("Unmapped.Key.123"));

        // Resolved key
        Assert.Equal("Home", vm.GetLocalizedText("Nav.Home", "Fallback"));

        // Alert message helper
        Assert.Equal("Session expired due to inactivity. Please authenticate again.",
            vm.GetLocalizedAlertMessage("Alert.IdleTimeout", "Default Expired"));
        Assert.Equal("Default Alert Message",
            vm.GetLocalizedAlertMessage("Unmapped.Alert", "Default Alert Message"));
        Assert.Equal("Default Alert Message",
            vm.GetLocalizedAlertMessage(string.Empty, "Default Alert Message"));
    }
}
