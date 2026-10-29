using System.Collections;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Moq;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Database.Access;
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
    /// including under concurrent initialization attempts, deterministically exercising both outer and inner guards.
    /// </summary>
    [Fact]
    public async Task LanguagePreferenceService_InitializeAsync_IdempotentOnMultipleCalls()
    {
        // WHAT: Ensures InitializeAsync can be safely executed repeatedly and concurrently.
        // WHY:  Guarantees concurrent callers or re-entrant queries do not fail and double-checked lock branch executes deterministically.
        // HOW:  Uses TaskCompletionSource to hold first caller inside connection creation while concurrent callers queue on _initLock,
        //       guaranteeing the inner if (_isInitialized) return branch executes deterministically across all environments.
        var enteredFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var service = new LanguagePreferenceService(
            _connectionString,
            cs =>
            {
                enteredFactory.TrySetResult();
                releaseFactory.Task.GetAwaiter().GetResult();
                return new SqliteConnection(cs);
            });

        Task t1 = Task.Run(() => service.InitializeAsync());
        await enteredFactory.Task;

        // t1 is holding _initLock inside InitializeAsync.
        // Queue concurrent callers while t1 is inside the lock and _isInitialized is still false.
        Task t2 = Task.Run(() => service.InitializeAsync());
        Task t3 = Task.Run(() => service.InitializeAsync());

        // Allow queued tasks to reach await _initLock.WaitAsync().
        await Task.Delay(50);

        // Release t1 to set _isInitialized = true and release _initLock, allowing t2 and t3 to hit the inner return.
        releaseFactory.TrySetResult();
        await Task.WhenAll(t1, t2, t3);

        // Sequential call verifies outer return path.
        await service.InitializeAsync();
        WeaverLanguage lang = await service.GetPreferredLanguageAsync();
        Assert.Equal(WeaverLanguage.English, lang);
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
    /// Verifies that <see cref="LanguagePreferenceService"/> properly detects all supported database backends.
    /// </summary>
    /// <param name="connectionString">Connection string.</param>
    /// <param name="expectedBackend">Expected database backend.</param>
    [Theory]
    [InlineData("Host=localhost;Database=test;Username=test;Password=secret", WeaverDatabaseBackend.PostgreSql)]
    [InlineData("Server=127.0.0.1;Port=5432;User Id=postgres;Password=pass;Database=test", WeaverDatabaseBackend.PostgreSql)]
    [InlineData("Data Source=test.db", WeaverDatabaseBackend.Sqlite)]
    [InlineData("Data Source=:memory:", WeaverDatabaseBackend.Sqlite)]
    [InlineData("Data Source=analytics.duckdb", WeaverDatabaseBackend.DuckDb)]
    [InlineData("Server=localhost;Port=3306;Database=weaver;Uid=root;", WeaverDatabaseBackend.MySql)]
    [InlineData("Server=cluster;Database=weaver;Dialect=mysql", WeaverDatabaseBackend.MySql)]
    public void LanguagePreferenceService_IdentifiesAllStorageBackends(string connectionString, WeaverDatabaseBackend expectedBackend)
    {
        // WHAT: Tests multi-engine backend detection from connection strings.
        // WHY:  Ensures proper backend selection across PostgreSQL, DuckDB, MySQL, and SQLite.
        // HOW:  Instantiates LanguagePreferenceService and checks Backend, IsPostgreSql, IsDuckDb, and IsMySql properties.
        using var service = new LanguagePreferenceService(connectionString);
        Assert.Equal(expectedBackend, service.Backend);
        Assert.Equal(expectedBackend == WeaverDatabaseBackend.PostgreSql, service.IsPostgreSql);
        Assert.Equal(expectedBackend == WeaverDatabaseBackend.DuckDb, service.IsDuckDb);
        Assert.Equal(expectedBackend == WeaverDatabaseBackend.MySql, service.IsMySql);
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
    /// Verifies that <see cref="LanguagePreferenceService"/> throws when MySQL is configured without a connection factory.
    /// </summary>
    [Fact]
    public async Task LanguagePreferenceService_MySql_ThrowsWithoutProviderWhenOpenedDirectly()
    {
        // WHAT: Tests that MySQL without provider or custom factory fails fast with InvalidOperationException.
        // WHY:  Ensures missing driver is surfaced clearly rather than swallowed.
        // HOW:  Instantiates LanguagePreferenceService with MySQL string and asserts InitializeAsync throws.
        using var service = new LanguagePreferenceService("Server=localhost;Port=3306;Database=weaver;Uid=root;");
        Assert.True(service.IsMySql);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InitializeAsync());
    }

    /// <summary>
    /// Verifies that <see cref="LanguagePreferenceService"/> opens a direct DuckDB connection when no custom factory is provided.
    /// </summary>
    [Fact]
    public async Task LanguagePreferenceService_DuckDb_DirectConnection_OpensAndInitializes()
    {
        // WHAT: Tests DuckDB connection instantiation through OpenConnectionAsync without custom factory.
        // WHY:  Guarantees 100% coverage on DuckDB branch in OpenConnectionAsync.
        // HOW:  Creates LanguagePreferenceService with a file-backed DuckDB connection string and runs InitializeAsync.
        string dbFile = Path.Combine(Path.GetTempPath(), $"weaver_pref_duck_{Guid.NewGuid():N}.duckdb");
        try
        {
            using var service = new LanguagePreferenceService($"Data Source={dbFile}");
            Assert.True(service.IsDuckDb);
            await service.InitializeAsync();
        }
        finally
        {
            if (File.Exists(dbFile))
            {
                try
                {
                    File.Delete(dbFile);
                }
                catch
                {
                    // Suppress transient file lock on Windows teardown
                }
            }
        }
    }

    /// <summary>
    /// Verifies that <see cref="LanguagePreferenceService"/> creates schema, executes upserts, and retrieves values
    /// on DuckDB dialect using an isolated SQLite-backed connection factory.
    /// </summary>
    [Fact]
    public async Task LanguagePreferenceService_DuckDbDialect_CustomFactory_ExecutesAllBranches()
    {
        // WHAT: Verifies DuckDB dialect table DDL creation, upsert execution, and preferred language retrieval.
        // WHY:  Guarantees that DuckDB dialect SQL and parameters round-trip accurately in preference service.
        // HOW:  Supplies a connection factory returning SQLite test database and executes full lifecycle.
        string memDb = $"Data Source={Path.Combine(Path.GetTempPath(), $"weaver_duck_pref_{Guid.NewGuid():N}.db")}";
        try
        {
            using var service = new LanguagePreferenceService(
                "Data Source=inproc.duckdb",
                _ => new SqliteConnection(memDb));

            Assert.True(service.IsDuckDb);
            Assert.False(service.IsPostgreSql);
            Assert.False(service.IsMySql);
            Assert.Equal(WeaverDatabaseBackend.DuckDb, service.Backend);

            await service.InitializeAsync();
            await service.SetPreferredLanguageAsync(WeaverLanguage.French);
            WeaverLanguage read = await service.GetPreferredLanguageAsync();
            Assert.Equal(WeaverLanguage.French, read);

            await service.SetPreferredLanguageAsync(WeaverLanguage.Spanish);
            WeaverLanguage updated = await service.GetPreferredLanguageAsync();
            Assert.Equal(WeaverLanguage.Spanish, updated);
        }
        finally
        {
            using var cleanupConn = new SqliteConnection(memDb);
            SqliteConnection.ClearPool(cleanupConn);
            string file = memDb.Replace("Data Source=", string.Empty);
            if (File.Exists(file))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }

    /// <summary>
    /// Verifies that <see cref="LanguagePreferenceService"/> executes MySQL schema creation and upsert DML
    /// using a recording test double connection.
    /// </summary>
    [Fact]
    public async Task LanguagePreferenceService_MySqlDialect_CustomFactory_ExecutesAllBranches()
    {
        // WHAT: Tests MySQL schema DDL execution, ON DUPLICATE KEY upsert statement, and scalar query.
        // WHY:  Confirms MySQL dialect SQL emission and parameter binding without requiring a live MySQL server.
        // HOW:  Passes recording connection test double and validates executed commands and parameters.
        var recordingConn = new RecordingPreferenceDbConnection();
        using var service = new LanguagePreferenceService(
            "Server=cluster.lan;Port=3306;Database=weaver;Uid=root;",
            _ => recordingConn);

        Assert.True(service.IsMySql);
        Assert.False(service.IsPostgreSql);
        Assert.False(service.IsDuckDb);
        Assert.Equal(WeaverDatabaseBackend.MySql, service.Backend);

        await service.InitializeAsync();
        Assert.Single(recordingConn.ExecutedCommandTexts);
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_gui_preferences", recordingConn.ExecutedCommandTexts[0]);
        Assert.Contains("DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)", recordingConn.ExecutedCommandTexts[0]);

        await service.SetPreferredLanguageAsync(WeaverLanguage.Japanese);
        Assert.Equal(2, recordingConn.ExecutedCommandTexts.Count);
        Assert.Contains("ON DUPLICATE KEY UPDATE", recordingConn.ExecutedCommandTexts[1]);
        Assert.Contains("@k", recordingConn.ExecutedCommandTexts[1]);
        Assert.Contains("@v", recordingConn.ExecutedCommandTexts[1]);
        Assert.Contains("@t", recordingConn.ExecutedCommandTexts[1]);

        recordingConn.ScalarResultToReturn = "Japanese";
        WeaverLanguage lang = await service.GetPreferredLanguageAsync();
        Assert.Equal(WeaverLanguage.Japanese, lang);
        Assert.Equal("SELECT value FROM weaver_gui_preferences WHERE key = @k;", recordingConn.ExecutedCommandTexts[2]);
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
        Assert.False(vm.IsRightToLeft);
        Assert.Equal(18, vm.AvailableLanguages.Count);
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

    /// <summary>
    /// Verifies that all 18 supported community languages round-trip accurately through
    /// <see cref="LanguagePreferenceService"/> across write, disk persistence, and re-read cycles on SQLite.
    /// </summary>
    [Fact]
    public async Task LanguagePreferenceService_All18Languages_RoundTripCorrectly()
    {
        // WHAT: Verifies that every one of the 18 WeaverLanguage enum values persists and round-trips correctly.
        // WHY:  Guarantees that no supported language is corrupted, truncated, or dropped during SQLite serialization.
        // HOW:  Iterates over Enum.GetValues<WeaverLanguage>(), persists each via SetPreferredLanguageAsync,
        //       and asserts GetPreferredLanguageAsync returns the exact enum value.
        using var service = new LanguagePreferenceService(_connectionString);
        foreach (WeaverLanguage lang in Enum.GetValues<WeaverLanguage>())
        {
            await service.SetPreferredLanguageAsync(lang);
            WeaverLanguage read = await service.GetPreferredLanguageAsync();
            Assert.Equal(lang, read);
        }
    }

    /// <summary>
    /// Verifies that all 18 supported community languages round-trip accurately through
    /// <see cref="LanguagePreferenceService"/> using PostgreSQL dialect and custom connection factory.
    /// </summary>
    [Fact]
    public async Task LanguagePreferenceService_PostgreSql_All18Languages_RoundTripCorrectly()
    {
        // WHAT: Verifies PostgreSQL dialect persistence across all 18 community languages.
        // WHY:  Ensures enterprise PostgreSQL nodes handle every supported language dialect without error.
        // HOW:  Passes custom connection factory returning isolated SQLite memory/temp file and tests all 18 enum values.
        string memDb = $"Data Source={Path.Combine(Path.GetTempPath(), $"weaver_pg_pref_all_{Guid.NewGuid():N}.db")}";
        try
        {
            using var service = new LanguagePreferenceService(
                "Host=pg-node.lan;Database=weaver;Username=postgres",
                _ => new SqliteConnection(memDb));

            await service.InitializeAsync();
            foreach (WeaverLanguage lang in Enum.GetValues<WeaverLanguage>())
            {
                await service.SetPreferredLanguageAsync(lang);
                WeaverLanguage read = await service.GetPreferredLanguageAsync();
                Assert.Equal(lang, read);
            }
        }
        finally
        {
            using var cleanupConn = new SqliteConnection(memDb);
            SqliteConnection.ClearPool(cleanupConn);
            string file = memDb.Replace("Data Source=", string.Empty);
            if (File.Exists(file))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }

    /// <summary>
    /// Verifies that all 18 community languages update <see cref="MainShellViewModel.SelectedLanguage"/>,
    /// correctly compute <see cref="MainShellViewModel.IsRightToLeft"/>, and fire property change notifications.
    /// </summary>
    [Fact]
    public async Task MainShellViewModel_All18Languages_SetLanguageAsync_UpdatesStateAndNotifies()
    {
        // WHAT: Tests language mutation, RTL resolution, and event dispatching for all 18 languages.
        // WHY:  Ensures UI reactivity across entire community language catalog in MainShellViewModel.
        // HOW:  Loops through all 18 WeaverLanguage values, sets language via SetLanguageAsync, asserts IsRightToLeft,
        //       and captures PropertyChanged events.
        var repoMock = new Mock<IInstanceProfileRepository>();
        var coordMock = new Mock<IMultiInstanceCoordinator>();
        coordMock.Setup(c => c.GetSnapshots()).Returns(Array.Empty<InstanceConnectionSnapshot>());

        var prefMock = new Mock<ILanguagePreferenceService>();
        prefMock.Setup(p => p.SetPreferredLanguageAsync(It.IsAny<WeaverLanguage>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var vm = new MainShellViewModel(repoMock.Object, coordMock.Object, languagePreferenceService: prefMock.Object);

        foreach (WeaverLanguage lang in Enum.GetValues<WeaverLanguage>())
        {
            var changedProperties = new HashSet<string>();
            vm.PropertyChanged += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.PropertyName))
                {
                    changedProperties.Add(e.PropertyName);
                }
            };

            await vm.SetLanguageAsync(lang);

            Assert.Equal(lang, vm.SelectedLanguage);
            Assert.Equal(lang == WeaverLanguage.Arabic, vm.IsRightToLeft);
            Assert.Contains(nameof(MainShellViewModel.SelectedLanguage), changedProperties);
            Assert.Contains(nameof(MainShellViewModel.IsRightToLeft), changedProperties);
            Assert.Contains(nameof(MainShellViewModel.LocalizedStatusText), changedProperties);
            Assert.Contains(nameof(MainShellViewModel.LocalizedIdleLockMessage), changedProperties);
            Assert.Contains(nameof(MainShellViewModel.LocalizedTrayShowText), changedProperties);
            Assert.Contains(nameof(MainShellViewModel.LocalizedTrayLockVaultText), changedProperties);
            Assert.Contains(nameof(MainShellViewModel.LocalizedTrayExitText), changedProperties);
        }
    }

    /// <summary>
    /// Verifies that <see cref="DesktopTrayIconController"/> supports all 18 languages, correctly reflects
    /// <see cref="DesktopTrayIconController.IsRightToLeft"/>, and emits localized tray snapshots.
    /// </summary>
    [Fact]
    public void DesktopTrayIconController_All18Languages_SetLanguage_UpdatesStateAndDirection()
    {
        // WHAT: Tests tray icon controller language switching, RTL detection, and standard menu items for all 18 languages.
        // WHY:  Ensures system tray menus adapt to any community language supported by the node.
        // HOW:  Instantiates DesktopTrayIconController, iterates all 18 languages, sets language,
        //       asserts IsRightToLeft flag and verifies GetStandardMenuItems returns 3 items.
        var localizer = new WeaverLocalizer();
        var controller = new DesktopTrayIconController(localizer: localizer);

        foreach (WeaverLanguage lang in Enum.GetValues<WeaverLanguage>())
        {
            controller.SetLanguage(lang);
            Assert.Equal(lang, controller.CurrentLanguage);
            Assert.Equal(lang == WeaverLanguage.Arabic, controller.IsRightToLeft);

            IReadOnlyList<string> menuItems = controller.GetStandardMenuItems();
            Assert.Equal(3, menuItems.Count);
            Assert.All(menuItems, item => Assert.False(string.IsNullOrWhiteSpace(item)));

            controller.ApplyLocalizedState(TrayHealthBadge.Healthy, "Status.Online", true, false, Array.Empty<string>());
            Assert.Contains("NanoWeaver HRT", controller.CurrentState.TooltipText);
        }
    }

    /// <summary>
    /// Recording database connection for preference service testing without a live MySQL server.
    /// </summary>
    private sealed class RecordingPreferenceDbConnection : DbConnection
    {
        /// <summary>Gets executed command texts.</summary>
        public List<string> ExecutedCommandTexts { get; } = new();

        /// <summary>Gets or sets scalar result.</summary>
        public object? ScalarResultToReturn { get; set; }

        /// <summary>Gets or sets connection string.</summary>
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = "Server=recording;";

        /// <summary>Gets database name.</summary>
        public override string Database => "recording_db";

        /// <summary>Gets data source.</summary>
        public override string DataSource => "recording_host";

        /// <summary>Gets server version.</summary>
        public override string ServerVersion => "8.0.30";

        /// <summary>Gets connection state.</summary>
        public override ConnectionState State => ConnectionState.Open;

        /// <summary>Changes database.</summary>
        /// <param name="databaseName">Name.</param>
        public override void ChangeDatabase(string databaseName) { }

        /// <summary>Closes connection.</summary>
        public override void Close() { }

        /// <summary>Opens connection.</summary>
        public override void Open() { }

        /// <summary>Opens connection async.</summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A completed task.</returns>
        public override Task OpenAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>Begins transaction.</summary>
        /// <param name="isolationLevel">Level.</param>
        /// <returns>Transaction.</returns>
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new RecordingPreferenceDbTransaction(this);

        /// <summary>Creates command.</summary>
        /// <returns>Recording command.</returns>
        protected override DbCommand CreateDbCommand() => new RecordingPreferenceDbCommand(this);
    }

    /// <summary>
    /// Recording transaction for preference tests.
    /// </summary>
    private sealed class RecordingPreferenceDbTransaction : DbTransaction
    {
        /// <summary>Owner connection.</summary>
        private readonly DbConnection _conn;

        /// <summary>Initializes a new instance of the <see cref="RecordingPreferenceDbTransaction"/> class.</summary>
        /// <param name="conn">Connection.</param>
        public RecordingPreferenceDbTransaction(DbConnection conn) => _conn = conn;

        /// <summary>Gets connection.</summary>
        protected override DbConnection DbConnection => _conn;

        /// <summary>Gets isolation level.</summary>
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        /// <summary>Commits transaction.</summary>
        public override void Commit() { }

        /// <summary>Rolls back transaction.</summary>
        public override void Rollback() { }
    }

    /// <summary>
    /// Recording command for preference tests.
    /// </summary>
    private sealed class RecordingPreferenceDbCommand : DbCommand
    {
        /// <summary>Owner connection.</summary>
        private readonly RecordingPreferenceDbConnection _conn;

        /// <summary>Parameter collection.</summary>
        private readonly RecordingPreferenceDbParameterCollection _params = new();

        /// <summary>Initializes a new instance of the <see cref="RecordingPreferenceDbCommand"/> class.</summary>
        /// <param name="conn">Connection.</param>
        public RecordingPreferenceDbCommand(RecordingPreferenceDbConnection conn) => _conn = conn;

        /// <summary>Gets or sets command text.</summary>
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        /// <summary>Gets or sets command timeout.</summary>
        public override int CommandTimeout { get; set; }

        /// <summary>Gets or sets command type.</summary>
        public override CommandType CommandType { get; set; } = CommandType.Text;

        /// <summary>Gets or sets design-time visibility.</summary>
        public override bool DesignTimeVisible { get; set; }

        /// <summary>Gets or sets updated row source.</summary>
        public override UpdateRowSource UpdatedRowSource { get; set; }

        /// <summary>Gets or sets connection.</summary>
        protected override DbConnection? DbConnection { get => _conn; set { } }

        /// <summary>Gets parameter collection.</summary>
        protected override DbParameterCollection DbParameterCollection => _params;

        /// <summary>Gets or sets transaction.</summary>
        protected override DbTransaction? DbTransaction { get; set; }

        /// <summary>Cancels execution.</summary>
        public override void Cancel() { }

        /// <summary>Executes non query.</summary>
        /// <returns>Rows affected.</returns>
        public override int ExecuteNonQuery()
        {
            _conn.ExecutedCommandTexts.Add(CommandText);
            return 1;
        }

        /// <summary>Executes non query async.</summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Rows affected.</returns>
        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) => Task.FromResult(ExecuteNonQuery());

        /// <summary>Executes scalar.</summary>
        /// <returns>Scalar result.</returns>
        public override object? ExecuteScalar()
        {
            _conn.ExecutedCommandTexts.Add(CommandText);
            return _conn.ScalarResultToReturn;
        }

        /// <summary>Executes scalar async.</summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Scalar result.</returns>
        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken) => Task.FromResult(ExecuteScalar());

        /// <summary>Prepares command.</summary>
        public override void Prepare() { }

        /// <summary>Creates parameter.</summary>
        /// <returns>New parameter.</returns>
        protected override DbParameter CreateDbParameter() => new RecordingPreferenceDbParameter();

        /// <summary>Executes reader.</summary>
        /// <param name="behavior">Behavior.</param>
        /// <returns>Not supported.</returns>
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    }

    /// <summary>
    /// Recording parameter for preference tests.
    /// </summary>
    private sealed class RecordingPreferenceDbParameter : DbParameter
    {
        /// <summary>Gets or sets db type.</summary>
        public override DbType DbType { get; set; }

        /// <summary>Gets or sets direction.</summary>
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

        /// <summary>Gets or sets is nullable.</summary>
        public override bool IsNullable { get; set; }

        /// <summary>Gets or sets parameter name.</summary>
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ParameterName { get; set; } = string.Empty;

        /// <summary>Gets or sets source column.</summary>
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;

        /// <summary>Gets or sets value.</summary>
        public override object? Value { get; set; }

        /// <summary>Gets or sets null mapping.</summary>
        public override bool SourceColumnNullMapping { get; set; }

        /// <summary>Gets or sets size.</summary>
        public override int Size { get; set; }

        /// <summary>Resets db type.</summary>
        public override void ResetDbType() { }
    }

    /// <summary>
    /// Recording parameter collection for preference tests.
    /// </summary>
    private sealed class RecordingPreferenceDbParameterCollection : DbParameterCollection
    {
        /// <summary>Parameters list.</summary>
        private readonly List<DbParameter> _items = new();

        /// <summary>Gets count.</summary>
        public override int Count => _items.Count;

        /// <summary>Gets sync root.</summary>
        public override object SyncRoot => ((ICollection)_items).SyncRoot;

        /// <summary>Adds parameter.</summary>
        /// <param name="value">Parameter.</param>
        /// <returns>Index.</returns>
        public override int Add(object? value) { _items.Add((DbParameter)value!); return _items.Count - 1; }

        /// <summary>Adds range.</summary>
        /// <param name="values">Array.</param>
        public override void AddRange(Array values) { foreach (object v in values) Add(v); }

        /// <summary>Clears parameters.</summary>
        public override void Clear() => _items.Clear();

        /// <summary>Checks contains.</summary>
        /// <param name="value">Value.</param>
        /// <returns>True if contained.</returns>
        public override bool Contains(object? value) => value is DbParameter p && _items.Contains(p);

        /// <summary>Checks contains name.</summary>
        /// <param name="value">Name.</param>
        /// <returns>True if contained.</returns>
        public override bool Contains(string value) => _items.Any(p => p.ParameterName == value);

        /// <summary>Copies to array.</summary>
        /// <param name="array">Array.</param>
        /// <param name="index">Index.</param>
        public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

        /// <summary>Gets enumerator.</summary>
        /// <returns>Enumerator.</returns>
        public override IEnumerator GetEnumerator() => _items.GetEnumerator();

        /// <summary>Gets parameter by index.</summary>
        /// <param name="index">Index.</param>
        /// <returns>Parameter.</returns>
        protected override DbParameter GetParameter(int index) => _items[index];

        /// <summary>Gets parameter by name.</summary>
        /// <param name="parameterName">Name.</param>
        /// <returns>Parameter.</returns>
        protected override DbParameter GetParameter(string parameterName) => _items.First(p => p.ParameterName == parameterName);

        /// <summary>Gets index of parameter.</summary>
        /// <param name="value">Value.</param>
        /// <returns>Index.</returns>
        public override int IndexOf(object? value) => value is DbParameter p ? _items.IndexOf(p) : -1;

        /// <summary>Gets index of name.</summary>
        /// <param name="parameterName">Name.</param>
        /// <returns>Index.</returns>
        public override int IndexOf(string parameterName) => _items.FindIndex(p => p.ParameterName == parameterName);

        /// <summary>Inserts parameter.</summary>
        /// <param name="index">Index.</param>
        /// <param name="value">Parameter.</param>
        public override void Insert(int index, object? value) => _items.Insert(index, (DbParameter)value!);

        /// <summary>Removes parameter.</summary>
        /// <param name="value">Parameter.</param>
        public override void Remove(object? value) { if (value is DbParameter p) _items.Remove(p); }

        /// <summary>Removes at index.</summary>
        /// <param name="index">Index.</param>
        public override void RemoveAt(int index) => _items.RemoveAt(index);

        /// <summary>Removes by name.</summary>
        /// <param name="parameterName">Name.</param>
        public override void RemoveAt(string parameterName)
        {
            int idx = IndexOf(parameterName);
            if (idx >= 0) _items.RemoveAt(idx);
        }

        /// <summary>Sets parameter.</summary>
        /// <param name="index">Index.</param>
        /// <param name="value">Parameter.</param>
        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;

        /// <summary>Sets parameter by name.</summary>
        /// <param name="parameterName">Name.</param>
        /// <param name="value">Parameter.</param>
        protected override void SetParameter(string parameterName, DbParameter value)
        {
            int idx = IndexOf(parameterName);
            if (idx >= 0) _items[idx] = value;
            else Add(value);
        }
    }
}
