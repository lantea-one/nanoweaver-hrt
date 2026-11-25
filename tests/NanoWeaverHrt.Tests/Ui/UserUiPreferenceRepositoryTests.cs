using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Ui;
using Xunit;

namespace NanoWeaverHrt.Tests.Ui;

/// <summary>
/// Unit tests verifying <see cref="Migration025_UserUiPreferences"/>, <see cref="UserUiPreferenceRepository"/>,
/// user deletion preference cascading, <c>Ui:DefaultDisplayMode</c> catalog configuration, and display DTO examples.
/// </summary>
public sealed class UserUiPreferenceRepositoryTests : IDisposable
{
    /// <summary>
    /// Isolated temporary directory holding the SQLite test database.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Database connection factory bound to the isolated SQLite test instance.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserUiPreferenceRepositoryTests"/> class.
    /// </summary>
    public UserUiPreferenceRepositoryTests()
    {
        // WHAT: Allocates an isolated SQLite database with all 25 schema migrations applied.
        // WHY:  Prevents SQLite handle pooling collisions across concurrent repository unit tests.
        // HOW:  Creates a unique temporary directory and instantiates WeaverTestDb.CreateFactory.
        _tempDir = Path.Combine(Path.GetTempPath(), $"nw_uipref_repo_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["POSTGRES_CONNECTION_STRING"] = string.Empty
            })
            .Build();
        _factory = WeaverTestDb.CreateFactory(config);
    }

    /// <summary>
    /// Verifies <see cref="Migration025_UserUiPreferences"/> emits valid DDL across all 4 dialects and rejects null input.
    /// </summary>
    [Fact]
    public void Migration025_GetSql_SupportsAllFourDialectsAndGuardsNull()
    {
        // WHAT: Validates Migration 025 version, name, null argument guard, and DDL across PostgreSQL, DuckDB, MySQL, MariaDB, and SQLite.
        // WHY:  Milestone 47 (P245): weaver_user_preferences must deploy cleanly across all 4 supported SQL engines.
        // HOW:  Calls GetSql for each supported dialect and inspects expected table and column definitions.
        Migration025_UserUiPreferences migration = new();
        Assert.Equal(25, migration.Version);
        Assert.Equal("025_UserUiPreferences", migration.Name);
        Assert.Throws<ArgumentNullException>(() => migration.GetSql(null!));

        string pg = migration.GetSql("postgresql");
        Assert.Contains("weaver_user_preferences", pg, StringComparison.Ordinal);
        Assert.Contains("TIMESTAMPTZ", pg, StringComparison.Ordinal);

        string duck = migration.GetSql("duckdb");
        Assert.Contains("weaver_user_preferences", duck, StringComparison.Ordinal);
        Assert.Contains("TIMESTAMP", duck, StringComparison.Ordinal);

        string mysql = migration.GetSql("mysql");
        Assert.Contains("weaver_user_preferences", mysql, StringComparison.Ordinal);
        Assert.Contains("DATETIME(6)", mysql, StringComparison.Ordinal);

        string mariadb = migration.GetSql("mariadb");
        Assert.Contains("DATETIME(6)", mariadb, StringComparison.Ordinal);

        string sqlite = migration.GetSql("sqlite");
        Assert.Contains("CREATE TABLE IF NOT EXISTS weaver_user_preferences", sqlite, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies argument validation, case-insensitive upsert/read, fallback parsing, and delete operations on
    /// <see cref="UserUiPreferenceRepository"/>.
    /// </summary>
    [Fact]
    public async Task Repository_CrudNormalizationAndInvalidValueFallback_WorkDeterministically()
    {
        // WHAT: Exercises GetByUsernameAsync, UpsertAsync, DeleteByUsernameAsync, parameter validation, and unknown mode fallback.
        // WHY:  Achieves 100% branch coverage on UserUiPreferenceRepository while testing real SQLite persistence.
        // HOW:  Verifies null guards, inserts Advanced, overwrites to Simple, tests invalid stored token fallback, and deletes row.
        Assert.Throws<ArgumentNullException>(() => new UserUiPreferenceRepository(null!));

        using UserUiPreferenceRepository repo = new(_factory);

        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.GetByUsernameAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.GetByUsernameAsync("   "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.UpsertAsync(null!, UiDisplayMode.Simple));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.UpsertAsync(" ", UiDisplayMode.Simple));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repo.UpsertAsync("alice", (UiDisplayMode)999));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.DeleteByUsernameAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.DeleteByUsernameAsync(""));

        Assert.Null(await repo.GetByUsernameAsync("missing_user"));
        Assert.False(await repo.DeleteByUsernameAsync("missing_user"));

        UserUiPreferenceRecord savedAdvanced = await repo.UpsertAsync("  Alice_Lead  ", UiDisplayMode.Advanced);
        Assert.Equal("alice_lead", savedAdvanced.Username);
        Assert.Equal(UiDisplayMode.Advanced, savedAdvanced.DisplayMode);

        UserUiPreferenceRecord? fetchedAdvanced = await repo.GetByUsernameAsync("ALICE_LEAD");
        Assert.NotNull(fetchedAdvanced);
        Assert.Equal("alice_lead", fetchedAdvanced.Username);
        Assert.Equal(UiDisplayMode.Advanced, fetchedAdvanced.DisplayMode);

        UserUiPreferenceRecord savedSimple = await repo.UpsertAsync("alice_lead", UiDisplayMode.Simple);
        Assert.Equal(UiDisplayMode.Simple, savedSimple.DisplayMode);

        // Verify defensive fallback to Simple when DB holds an unrecognized mode token or out-of-range numeric enum string
        {
            await using WeaverDbSession session = await _factory.OpenAsync();
            await using DbCommand corruptCmd = session.CreateCommand(
                "UPDATE weaver_user_preferences SET display_mode = 'CorruptedToken' WHERE username = 'alice_lead';");
            await corruptCmd.ExecuteNonQueryAsync();
        }

        UserUiPreferenceRecord? fallbackParsed = await repo.GetByUsernameAsync("alice_lead");
        Assert.NotNull(fallbackParsed);
        Assert.Equal(UiDisplayMode.Simple, fallbackParsed.DisplayMode);

        {
            await using WeaverDbSession session = await _factory.OpenAsync();
            await using DbCommand numericCmd = session.CreateCommand(
                "UPDATE weaver_user_preferences SET display_mode = '42' WHERE username = 'alice_lead';");
            await numericCmd.ExecuteNonQueryAsync();
        }

        UserUiPreferenceRecord? undefinedNumericParsed = await repo.GetByUsernameAsync("alice_lead");
        Assert.NotNull(undefinedNumericParsed);
        Assert.Equal(UiDisplayMode.Simple, undefinedNumericParsed.DisplayMode);

        Assert.True(await repo.DeleteByUsernameAsync("  ALICE_LEAD "));
        Assert.Null(await repo.GetByUsernameAsync("alice_lead"));
        Assert.False(await repo.DeleteByUsernameAsync("alice_lead"));
    }

    /// <summary>
    /// Verifies deleting a user via <see cref="UserRepository.DeleteUserAsync"/> cascades to remove any saved
    /// preference row in <c>weaver_user_preferences</c>, and verifies <c>Ui:DefaultDisplayMode</c> catalog presence.
    /// </summary>
    [Fact]
    public async Task UserDeletion_RemovesPreferenceRow_AndConfigCatalogContainsUiDefaultDisplayMode()
    {
        // WHAT: Tests user deletion cascade onto weaver_user_preferences plus Ui:DefaultDisplayMode config default.
        // WHY:  Prevents orphaned preference rows across accounts and verifies admin-configurable node defaults.
        // HOW:  Creates user + UI preference, calls UserRepository.DeleteUserAsync, asserts row deleted, and reads config catalog.
        using UserRepository userRepo = new(_factory);
        using UserUiPreferenceRepository prefRepo = new(_factory);
        using SystemConfigurationStore configStore = new(_factory);

        await userRepo.InitializeAsync();
        bool created = await userRepo.TryCreateUserAsync(new UserAccount
        {
            Username = "CascadeUser",
            Role = UserRole.Researcher,
            AccessGate = AccessGate.ApiWeb,
            Algorithm = KeyAlgorithm.Ed25519,
            PublicKeyBase64 = "MCowBQYDK2VwAyEA9876543210abcdef9876543210abcdef9876543210==",
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        });
        Assert.True(created);

        _ = await prefRepo.UpsertAsync("CascadeUser", UiDisplayMode.Advanced);
        Assert.NotNull(await prefRepo.GetByUsernameAsync("cascadeuser"));

        Assert.True(await userRepo.DeleteUserAsync("CascadeUser"));
        Assert.Null(await prefRepo.GetByUsernameAsync("cascadeuser"));

        IReadOnlyList<ConfigurationItem> configs = await configStore.GetAllConfigurationsAsync();
        ConfigurationItem uiDefault = Assert.Single(configs, c => c.Key == UiPreferenceService.DefaultDisplayModeConfigKey);
        Assert.Equal("Simple", uiDefault.DefaultValue);
        Assert.Equal("Simple", uiDefault.Value);

        EffectiveUiPreference exampleEffective = new EffectiveUiPreference().GetExamples();
        Assert.Equal(UiDisplayMode.Simple, exampleEffective.DisplayMode);
        UpdateUiPreferenceRequest exampleReq = new UpdateUiPreferenceRequest().GetExamples();
        Assert.Equal(UiDisplayMode.Advanced, exampleReq.DisplayMode);
    }

    /// <summary>
    /// Disposes SQLite connection pools and deletes the isolated temporary directory.
    /// </summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
