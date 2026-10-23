using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Secrets;
using NanoWeaverHrt.Storage;
using Xunit;
using Xunit.Abstractions;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// User, DNA-record, import and result behaviour on BOTH real engines: embedded SQLite always, and a REAL
/// PostgreSQL server when WEAVER_TEST_POSTGRES supplies one. Nothing here mocks a database driver.
/// </summary>
[Collection(PostgresTestEnvironment.CollectionName)]
public sealed class PostgresBackedRepositoryTests : IDisposable
{
    /// <summary>
    /// Connection string pointing at a port nothing listens on, used to prove a dead PostgreSQL is an error.
    /// </summary>
    private const string UnreachablePostgres = "Host=127.0.0.1;Port=54397;Database=nonexistent;Timeout=1";

    /// <summary>
    /// Shared PostgreSQL fixture.
    /// </summary>
    private readonly PostgresTestEnvironment _postgres;

    /// <summary>
    /// Test output sink used to report why a test did not run.
    /// </summary>
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Per-test directory under the system temp path; removed on dispose.
    /// </summary>
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "weaver_pgrepo_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresBackedRepositoryTests"/> class.
    /// </summary>
    /// <param name="postgres">Shared PostgreSQL fixture.</param>
    /// <param name="output">Test output sink.</param>
    public PostgresBackedRepositoryTests(PostgresTestEnvironment postgres, ITestOutputHelper output)
    {
        _postgres = postgres;
        _output = output;
        Directory.CreateDirectory(_tempDir);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    /// <summary>
    /// Builds a factory bound to this test's temp directory.
    /// </summary>
    /// <param name="postgresConnectionString">The PostgreSQL connection string to configure; empty selects SQLite.</param>
    /// <param name="logger">Logger that captures the factory's output, or null to discard it.</param>
    /// <returns>A real connection factory.</returns>
    private WeaverDbConnectionFactory CreateFactory(string postgresConnectionString, ILogger<WeaverDbConnectionFactory>? logger = null)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["POSTGRES_CONNECTION_STRING"] = postgresConnectionString
            })
            .Build();
        return WeaverTestDb.CreateFactory(configuration, logger: logger);
    }

    /// <summary>
    /// Creates an account value for the scenarios.
    /// </summary>
    /// <param name="username">Account name.</param>
    /// <param name="role">Account role.</param>
    /// <param name="isActive">Whether the account is active.</param>
    /// <returns>The account.</returns>
    private static UserAccount Account(string username, UserRole role, bool isActive = true) => new()
    {
        Username = username,
        Role = role,
        AccessGate = AccessGate.ApiWeb,
        Algorithm = KeyAlgorithm.Ed25519,
        PublicKeyBase64 = "MCowBQYDK2VwAyEAkey" + username,
        IsActive = isActive
    };

    /// <summary>
    /// Runs the user-account scenario against any engine.
    /// </summary>
    /// <param name="factory">A factory bound to the engine under test.</param>
    /// <returns>A task that completes once every assertion has run.</returns>
    private static async Task RunUserScenarioAsync(WeaverDbConnectionFactory factory)
    {
        // WHAT: Exercises every UserRepository method once on a real engine.
        // WHY:  The PostgreSQL twins of these methods were unreachable by any test; one scenario now covers both engines.
        // HOW:  Initialize, create, duplicate-create, administrator check, read, list, login update, delete.
        using UserRepository repo = new(factory);
        await repo.InitializeAsync();
        Assert.False(await repo.HasAnyAdministratorAsync());

        await repo.CreateUserAsync(Account("inactive-admin", UserRole.Administrator, isActive: false));
        Assert.False(await repo.HasAnyAdministratorAsync());
        await repo.CreateUserAsync(Account("researcher", UserRole.Researcher));
        Assert.False(await repo.HasAnyAdministratorAsync());

        Assert.True(await repo.TryCreateUserAsync(Account("admin", UserRole.Administrator)));
        Assert.False(await repo.TryCreateUserAsync(Account("admin", UserRole.Patient)));
        Assert.True(await repo.HasAnyAdministratorAsync());

        await repo.CreateUserAsync(Account("researcher", UserRole.Patient));
        UserAccount? read = await repo.GetUserAsync("researcher");
        Assert.NotNull(read);
        Assert.Equal(UserRole.Patient, read.Role);
        Assert.True(read.IsActive);
        Assert.Null(read.LastLoginUtc);
        Assert.Null(await repo.GetUserAsync("nobody"));

        DateTimeOffset login = new(2026, 10, 20, 12, 30, 0, TimeSpan.Zero);
        await repo.UpdateLastLoginAsync("researcher", login);
        Assert.Equal(login, (await repo.GetUserAsync("researcher"))!.LastLoginUtc); // GetUserAsync just returned the row, so non-null.

        IReadOnlyList<UserAccount> users = await repo.ListUsersAsync();
        Assert.Equal(["admin", "inactive-admin", "researcher"], users.Select(u => u.Username).ToArray());

        Assert.True(await repo.DeleteUserAsync("researcher"));
        Assert.False(await repo.DeleteUserAsync("researcher"));

        await using WeaverDbSession session = await factory.OpenAsync();
        await using DbCommand versions = session.CreateCommand("SELECT COUNT(*) FROM weaver_schema_migrations WHERE version <= 6");
        Assert.Equal(6L, Convert.ToInt64(await versions.ExecuteScalarAsync()));
    }

    /// <summary>
    /// Runs the DNA-record, import and result scenario against any engine.
    /// </summary>
    /// <param name="factory">A factory bound to the engine under test.</param>
    /// <returns>A task that completes once every assertion has run.</returns>
    private static async Task RunPatientDataScenarioAsync(WeaverDbConnectionFactory factory)
    {
        // WHAT: Round-trips one DNA record, one import and one result, then deletes them.
        // WHY:  PatientDnaRepository's twelve methods share one connection path that used to fall back silently.
        // HOW:  Upserts twice (insert then update), reads back by id and by owner, then deletes each row.
        using UserRepository users = new(factory);
        await users.CreateUserAsync(Account("dana", UserRole.Patient));
        using PatientDnaRepository repo = new(factory);

        PatientDigitalDnaRecord dna = new() { Id = "dna1", Username = "dana", Title = "Tube", TargetAptamerKdNm = 3.5, Notes = "n" };
        await repo.UpsertDnaRecordAsync(dna);
        await repo.UpsertDnaRecordAsync(dna with { Title = "Tube2", Notes = null });
        Assert.Equal("Tube2", (await repo.GetDnaRecordByIdAsync("dna1"))!.Title); // Row was just upserted, so non-null.
        Assert.Single(await repo.GetDnaRecordsAsync("dana"));

        PatientDnaImportRecord import = new() { Id = "imp1", Username = "dana", FileName = "a.fasta", SequenceOrData = "ACGT", SizeBytes = 4 };
        await repo.UpsertImportAsync(import);
        await repo.UpsertImportAsync(import with { Status = "Done" });
        Assert.Equal("Done", (await repo.GetImportByIdAsync("imp1"))!.Status); // Row was just upserted, so non-null.
        Assert.Single(await repo.GetImportsAsync("dana"));

        PatientResultRecord result = new() { Id = "res1", Username = "dana", DnaRecordId = "dna1", ImportId = "imp1", Title = "Kinetics", Summary = "s" };
        await repo.UpsertResultAsync(result);
        await repo.UpsertResultAsync(result with { Title = "Kinetics2", DnaRecordId = null, ImportId = null });
        PatientResultRecord? readResult = await repo.GetResultByIdAsync("res1");
        Assert.Equal("Kinetics2", readResult!.Title); // Row was just upserted, so non-null.
        Assert.Null(readResult.DnaRecordId);
        Assert.Single(await repo.GetResultsAsync("dana"));

        Assert.True(await repo.DeleteResultAsync("res1"));
        Assert.True(await repo.DeleteImportAsync("imp1"));
        Assert.True(await repo.DeleteDnaRecordAsync("dna1"));
        Assert.False(await repo.DeleteDnaRecordAsync("dna1"));
    }

    /// <summary>
    /// Verifies the user scenario on SQLite.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task Sqlite_UserRepository_Scenario()
    {
        await RunUserScenarioAsync(CreateFactory(string.Empty));
    }

    /// <summary>
    /// Verifies the patient data scenario on SQLite.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task Sqlite_PatientDnaRepository_Scenario()
    {
        await RunPatientDataScenarioAsync(CreateFactory(string.Empty));
    }

    /// <summary>
    /// Verifies a deleted SQLite file is re-created by the next repository call.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task Sqlite_InitializeAsync_RecreatesDeletedDatabaseFile()
    {
        // WHAT: Deletes the database file mid-test and calls InitializeAsync again.
        // WHY:  The old short-circuit existed so a vanished file is rebuilt rather than trusted.
        // HOW:  The factory migrates on every open, so a second InitializeAsync restores the file and the schema.
        using UserRepository repo = new(CreateFactory(string.Empty));
        await repo.InitializeAsync();
        string dbPath = Path.Combine(_tempDir, "weaver_config.db");
        Assert.True(File.Exists(dbPath));

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(dbPath);
        await repo.InitializeAsync();

        Assert.True(File.Exists(dbPath));
        Assert.False(await repo.HasAnyAdministratorAsync());
    }

    /// <summary>
    /// Verifies the constructor rejects a missing connection factory.
    /// </summary>
    [Fact]
    public void UserRepository_NullConnectionFactory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new UserRepository(null!));
    }

    /// <summary>
    /// Verifies the user scenario on real PostgreSQL.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task PostgreSql_UserRepository_Scenario()
    {
        if (!_postgres.IsAvailable)
        {
            // WHAT: Leaves the test body unexecuted when no PostgreSQL server was supplied.
            // WHY:  Directive 6 forbids depending on ambient network state; a set-but-dead variable fails elsewhere.
            // HOW:  Reports the reason through ITestOutputHelper and returns before touching the network.
            _output.WriteLine("SKIPPED: WEAVER_TEST_POSTGRES is not set.");
            return;
        }

        await RunUserScenarioAsync(CreateFactory(await _postgres.CreateIsolatedDatabaseAsync()));
        Assert.False(File.Exists(Path.Combine(_tempDir, "weaver_config.db")));
    }

    /// <summary>
    /// Verifies the patient data scenario on real PostgreSQL.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task PostgreSql_PatientDnaRepository_Scenario()
    {
        if (!_postgres.IsAvailable)
        {
            // WHAT: Leaves the test body unexecuted when no PostgreSQL server was supplied.
            // WHY:  Directive 6 forbids depending on ambient network state; a set-but-dead variable fails elsewhere.
            // HOW:  Reports the reason through ITestOutputHelper and returns before touching the network.
            _output.WriteLine("SKIPPED: WEAVER_TEST_POSTGRES is not set.");
            return;
        }

        await RunPatientDataScenarioAsync(CreateFactory(await _postgres.CreateIsolatedDatabaseAsync()));
        Assert.False(File.Exists(Path.Combine(_tempDir, "weaver_config.db")));
    }

    /// <summary>
    /// Returns early, with a visible reason, when no PostgreSQL server was supplied.
    /// </summary>
    /// <returns><c>true</c> when the caller must stop because PostgreSQL is unavailable.</returns>
    private bool SkipWithoutPostgres()
    {
        if (_postgres.IsAvailable)
        {
            return false;
        }

        // WHAT: Leaves the test body unexecuted when no PostgreSQL server was supplied.
        // WHY:  Directive 6 forbids depending on ambient network state; a set-but-dead variable fails elsewhere.
        // HOW:  Reports the reason through ITestOutputHelper and tells the caller to return.
        _output.WriteLine("SKIPPED: WEAVER_TEST_POSTGRES is not set.");
        return true;
    }

    /// <summary>
    /// Runs the configuration and share-key scenario against any engine.
    /// </summary>
    /// <param name="factory">A factory bound to the engine under test.</param>
    /// <returns>A task that completes once every assertion has run.</returns>
    private static async Task RunConfigurationScenarioAsync(WeaverDbConnectionFactory factory)
    {
        // WHAT: Exercises every SystemConfigurationStore method that touches the database.
        // WHY:  The nine dialect twins in the store were reachable only on SQLite; one scenario now covers both engines.
        // HOW:  Override upsert twice, read, revert; create, list, consume twice (single use), revoke, expire.
        using SystemConfigurationStore store = new(factory);
        await store.InitializeAsync();

        await store.SetDatabaseOverrideAsync("Simulation:DefaultTimeStep", "0.005", "first");
        await store.SetDatabaseOverrideAsync("Simulation:DefaultTimeStep", "0.007", "second");
        Assert.Equal("0.007", await store.GetValueAsync("Simulation:DefaultTimeStep"));
        IReadOnlyList<ConfigurationItem> all = await store.GetAllConfigurationsAsync();
        Assert.Contains(all, i => i.Key == "Simulation:DefaultTimeStep" && i.Value == "0.007" && i.Source == ConfigurationSource.DatabaseOverride);
        await store.RevertToDefaultAsync("Simulation:DefaultTimeStep");
        Assert.Equal("0.002", await store.GetValueAsync("Simulation:DefaultTimeStep"));

        ShareKeyRecord key = await store.CreateShareKeyAsync("Peer", TimeSpan.FromHours(1), "ReadOnlyTelemetry");
        IReadOnlyList<ShareKeyRecord> listed = await store.ListShareKeysAsync();
        ShareKeyRecord stored = Assert.Single(listed);
        Assert.Equal(key.TokenId, stored.TokenId);
        Assert.False(stored.IsConsumed);
        Assert.Null(stored.ConsumedAtUtc);
        Assert.Equal("ReadOnlyTelemetry", stored.Permissions);
        Assert.True(Math.Abs((stored.ExpiresAtUtc - key.ExpiresAtUtc).TotalSeconds) < 1);

        ShareKeyConsumeResult first = await store.ConsumeShareKeyAsync(key.KeyToken, "10.0.0.5");
        Assert.True(first.Success);
        ShareKeyConsumeResult second = await store.ConsumeShareKeyAsync(key.KeyToken, "10.0.0.6");
        Assert.False(second.Success);
        Assert.Contains("already consumed", second.Message);
        Assert.Contains("10.0.0.5", second.Message);
        Assert.False((await store.ConsumeShareKeyAsync("nanoweaver_hrt_missing", "x")).Success);

        ShareKeyRecord consumed = Assert.Single(await store.ListShareKeysAsync());
        Assert.True(consumed.IsConsumed);
        Assert.NotNull(consumed.ConsumedAtUtc);
        Assert.Equal("10.0.0.5", consumed.RemotePeerInfo);

        ShareKeyRecord expired = await store.CreateShareKeyAsync("Old", TimeSpan.FromSeconds(-10));
        Assert.Contains("expired", (await store.ConsumeShareKeyAsync(expired.KeyToken, "x")).Message);

        ShareKeyRecord revokable = await store.CreateShareKeyAsync("Revoke", TimeSpan.FromHours(1));
        Assert.True(await store.RevokeShareKeyAsync(revokable.TokenId));
        Assert.False(await store.RevokeShareKeyAsync("no-such-token"));
        Assert.Contains("already consumed", (await store.ConsumeShareKeyAsync(revokable.KeyToken, "x")).Message);

        // Versions 1-6 must be recorded by the shared runner on this engine.
        await using WeaverDbSession session = await factory.OpenAsync();
        await using DbCommand versions = session.CreateCommand("SELECT COUNT(*) FROM weaver_schema_migrations WHERE version <= 6");
        Assert.Equal(6L, Convert.ToInt64(await versions.ExecuteScalarAsync()));
    }

    /// <summary>
    /// Verifies the configuration and share-key scenario on SQLite, including the factory's display name.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task Sqlite_SystemConfigurationStore_Scenario()
    {
        WeaverDbConnectionFactory factory = CreateFactory(string.Empty);
        Assert.Equal("Local SQLite Fallback", factory.ActiveBackendDisplayName);
        using SystemConfigurationStore store = new(factory);
        Assert.Equal("Local SQLite Fallback", store.ActiveBackendName);
        await RunConfigurationScenarioAsync(factory);
        Assert.Equal(WeaverDatabaseBackend.Sqlite, factory.ActiveBackend);
    }

    /// <summary>
    /// Verifies the configuration and share-key scenario on real PostgreSQL.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task PostgreSql_SystemConfigurationStore_Scenario()
    {
        if (SkipWithoutPostgres())
        {
            return;
        }

        WeaverDbConnectionFactory factory = CreateFactory(await _postgres.CreateIsolatedDatabaseAsync());
        using SystemConfigurationStore store = new(factory);
        await store.InitializeAsync();
        Assert.Equal("PostgreSQL (pg-duckdb)", store.ActiveBackendName);
        Assert.Equal(WeaverDatabaseBackend.PostgreSql, factory.ActiveBackend);
        await RunConfigurationScenarioAsync(factory);
        Assert.False(File.Exists(Path.Combine(_tempDir, "weaver_config.db")));
    }

    /// <summary>
    /// Makes every UPDATE of weaver_share_keys affect zero rows, as if another caller had already won the race.
    /// </summary>
    /// <param name="factory">A factory bound to the engine under test.</param>
    /// <param name="blockingStatement">The engine-specific DDL that silently discards UPDATEs on the table.</param>
    /// <returns>A task that completes once a key is created and a redemption has been attempted and refused.</returns>
    private static async Task AssertConcurrentConsumptionIsRefusedAsync(WeaverDbConnectionFactory factory, string blockingStatement)
    {
        // WHAT: Simulates losing the single-use race deterministically on a real engine.
        // WHY:  The "rows == 0" branch is what actually stops a replay when two redemptions interleave; without a
        //       real interleaving it can only be reached by making the database itself ignore the UPDATE.
        // HOW:  Creates a key, installs a trigger (SQLite) or rule (PostgreSQL) that discards UPDATEs, then redeems.
        using SystemConfigurationStore store = new(factory);
        ShareKeyRecord key = await store.CreateShareKeyAsync("Race", TimeSpan.FromHours(1));
        await using (WeaverDbSession session = await factory.OpenAsync())
        {
            await using DbCommand block = session.CreateCommand(blockingStatement);
            await block.ExecuteNonQueryAsync();
        }

        ShareKeyConsumeResult result = await store.ConsumeShareKeyAsync(key.KeyToken, "10.0.0.9");

        Assert.False(result.Success);
        Assert.Equal("Concurrent consumption detected.", result.Message);
        Assert.False(Assert.Single(await store.ListShareKeysAsync()).IsConsumed);
    }

    /// <summary>
    /// Verifies a lost redemption race is refused on SQLite.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task Sqlite_ConsumeShareKey_LostRace_IsRefused()
    {
        await AssertConcurrentConsumptionIsRefusedAsync(
            CreateFactory(string.Empty),
            "CREATE TRIGGER ignore_share_key_updates BEFORE UPDATE ON weaver_share_keys BEGIN SELECT RAISE(IGNORE); END;");
    }

    /// <summary>
    /// Verifies a lost redemption race is refused on real PostgreSQL.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task PostgreSql_ConsumeShareKey_LostRace_IsRefused()
    {
        if (SkipWithoutPostgres())
        {
            return;
        }

        await AssertConcurrentConsumptionIsRefusedAsync(
            CreateFactory(await _postgres.CreateIsolatedDatabaseAsync()),
            "CREATE RULE ignore_share_key_updates AS ON UPDATE TO weaver_share_keys DO INSTEAD NOTHING;");
    }

    /// <summary>
    /// Verifies a deleted SQLite file is re-created by the next store call.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task Sqlite_StoreInitializeAsync_RecreatesDeletedDatabaseFile()
    {
        // WHAT: Deletes weaver_config.db between two store calls.
        // WHY:  The resolution is cached, but a vanished file must still be rebuilt rather than trusted.
        // HOW:  The second call finds the cached SQLite decision with no file and re-applies the migrations.
        using SystemConfigurationStore store = new(CreateFactory(string.Empty));
        await store.SetDatabaseOverrideAsync("Simulation:DefaultTimeStep", "0.009");
        string dbPath = Path.Combine(_tempDir, "weaver_config.db");
        Assert.True(File.Exists(dbPath));

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(dbPath);
        await store.InitializeAsync();

        Assert.True(File.Exists(dbPath));
        Assert.Equal("0.002", await store.GetValueAsync("Simulation:DefaultTimeStep"));
    }

    /// <summary>
    /// Verifies an unreachable configured PostgreSQL makes every call throw, logs one safe warning, and never touches SQLite.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task UnreachableConfiguredPostgres_FailsEveryCall_WarnsExactlyOnce_WithoutLeakingDetails()
    {
        // WHAT: Points the factory at a dead port and drives several repository calls through it.
        // WHY:  Integrity beats availability: no call may be served from a local file, the failure must be loud,
        //       the warning must appear once, and it must never leak connection details.
        // HOW:  A capturing logger records every entry; the test counts warnings and scans their text.
        CapturingLogger<WeaverDbConnectionFactory> logger = new();
        string secret = "Host=127.0.0.1;Port=54397;Database=hunter2db;Username=u;Password=hunter2pw;Timeout=1";
        WeaverDbConnectionFactory factory = CreateFactory(secret, logger);
        using UserRepository users = new(factory);
        using SystemConfigurationStore store = new(factory);

        await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => users.InitializeAsync());
        await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => store.InitializeAsync());
        await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => users.HasAnyAdministratorAsync());
        await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => store.SetDatabaseOverrideAsync("Simulation:DefaultTimeStep", "0.004"));
        WeaverDatabaseUnavailableException again = await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => factory.EnsureBackendResolvedAsync());
        Assert.Equal(WeaverDatabaseBackend.PostgreSql, again.Backend);

        string warning = Assert.Single(logger.Warnings);
        foreach (string forbidden in new[] { "127.0.0.1", "54397", "hunter2", "Host=", "Password", _tempDir, "weaver_config.db" })
        {
            Assert.DoesNotContain(forbidden, warning, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("could not be reached", warning);
        Assert.DoesNotContain("Falling back", warning);
        Assert.Equal("PostgreSQL (pg-duckdb) - unreachable", factory.ActiveBackendDisplayName);
        Assert.StartsWith("PostgreSQL", store.ActiveBackendName);
        Assert.False(File.Exists(Path.Combine(_tempDir, "weaver_config.db")));
    }

    /// <summary>
    /// Verifies concurrent first callers still produce a single warning and all fail.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task UnreachableConfiguredPostgres_ConcurrentResolution_WarnsOnce()
    {
        CapturingLogger<WeaverDbConnectionFactory> logger = new();
        WeaverDbConnectionFactory factory = CreateFactory(UnreachablePostgres, logger);

        Task[] calls = Enumerable.Range(0, 8).Select(_ => Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => factory.EnsureBackendResolvedAsync())).ToArray();
        await Task.WhenAll(calls);

        Assert.Single(logger.Warnings);
        Assert.False(File.Exists(Path.Combine(_tempDir, "weaver_config.db")));
    }

    /// <summary>
    /// Verifies a selected-but-unreachable PostgreSQL makes UserRepository throw and creates no SQLite file.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task UserRepository_PostgresSelectedButUnreachable_ThrowsAndCreatesNoSqliteFile()
    {
        // WHAT: Proves UserRepository has no silent PostgreSQL to SQLite fallback.
        // WHY:  The former bare catch blocks hid a PostgreSQL that had never been reachable.
        // HOW:  Selects PostgreSQL at a dead port, calls two methods, expects the unavailable exception, and checks no file exists.
        using UserRepository repo = new(CreateFactory(UnreachablePostgres));

        await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => repo.InitializeAsync());
        await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => repo.GetUserAsync("anyone"));
        Assert.False(File.Exists(Path.Combine(_tempDir, "weaver_config.db")));
    }

    /// <summary>
    /// Verifies a selected-but-unreachable PostgreSQL makes PatientDnaRepository throw and creates no SQLite file.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task PatientDnaRepository_PostgresSelectedButUnreachable_ThrowsAndCreatesNoSqliteFile()
    {
        // WHAT: Proves there is no silent PostgreSQL to SQLite fallback.
        // WHY:  The old bare catch let a dead PostgreSQL split data into a local file while the gate stayed green.
        // HOW:  Selects PostgreSQL at a dead port, calls a read, expects WeaverDatabaseUnavailableException, and
        //       asserts weaver_config.db was never created.
        using PatientDnaRepository repo = new(CreateFactory(UnreachablePostgres));

        await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => repo.GetDnaRecordsAsync("some_user"));
        Assert.False(File.Exists(Path.Combine(_tempDir, "weaver_config.db")));
    }

    /// <summary>
    /// Verifies a selected-but-unreachable PostgreSQL is an error for UserProfileRepository and never a SQLite write.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task UserProfileRepository_PostgresUnreachable_ThrowsInsteadOfWritingToSqlite()
    {
        // WHAT: Selects PostgreSQL against a port nothing listens on and calls the repository.
        // WHY:  The former per-call catch redirected such failures to the local SQLite file, splitting the
        //       data across two databases. The failure must propagate and nothing may be written locally.
        // HOW:  Asserts WeaverDatabaseUnavailableException, a message free of connection details, and that
        //       no weaver_config.db was created in the isolated temp directory.
        IConfiguration keys = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["WEAVER_NODE_KEYS_DIR"] = Path.Combine(_tempDir, "node-keys") })
            .Build();
        using UserProfileRepository repo = new(CreateFactory(UnreachablePostgres), new ProfileDataProtector(new NodeSecretStore(keys)));

        WeaverDatabaseUnavailableException ex = await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => repo.GetProfileAsync("anyone"));
        Assert.Equal(WeaverDatabaseBackend.PostgreSql, ex.Backend);
        Assert.DoesNotContain("Host=", ex.Message);
        Assert.DoesNotContain("Password", ex.Message);
        Assert.DoesNotContain(_tempDir, ex.Message);
        Assert.False(File.Exists(Path.Combine(_tempDir, "weaver_config.db")));
    }

    /// <summary>
    /// Verifies that configuring no PostgreSQL at all is silent: SQLite-only is the intended setup.
    /// </summary>
    /// <param name="connectionString">An empty or whitespace connection string.</param>
    /// <returns>A task that completes when the test finishes.</returns>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NoPostgresConfigured_LogsNoWarning(string connectionString)
    {
        CapturingLogger<WeaverDbConnectionFactory> logger = new();
        WeaverDbConnectionFactory factory = CreateFactory(connectionString, logger);
        using UserRepository users = new(factory);

        await users.InitializeAsync();
        await users.GetUserAsync("nobody");

        Assert.Empty(logger.Warnings);
        Assert.Equal("Local SQLite Fallback", factory.ActiveBackendDisplayName);
    }

    /// <summary>
    /// Verifies a cancelled token is not disguised as a PostgreSQL outage.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task EnsureBackendResolved_Cancelled_ThrowsAndLogsNothing()
    {
        CapturingLogger<WeaverDbConnectionFactory> logger = new();
        WeaverDbConnectionFactory factory = CreateFactory(UnreachablePostgres, logger);
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.EnsureBackendResolvedAsync(cancelled.Token));

        Assert.Empty(logger.Warnings);
    }

    /// <summary>
    /// Verifies cancellation requested during the PostgreSQL probe propagates instead of being logged as an outage.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task EnsureBackendResolved_CancelledDuringProbe_ThrowsAndLogsNothing()
    {
        // WHAT: Points the probe at a local socket that accepts connections but never answers.
        // WHY:  A shutdown request during startup must stop startup, not be reported as "PostgreSQL unreachable".
        // HOW:  A loopback listener owned by this test never speaks the protocol, so the open cannot succeed;
        //       the token is cancelled while it waits, and the one-second connect timeout then ends the open.
        //       Whatever order those two events land in, the cancelled token is observed and rethrown.
        using System.Net.Sockets.TcpListener silent = new(System.Net.IPAddress.Loopback, 0);
        silent.Start();
        int port = ((System.Net.IPEndPoint)silent.LocalEndpoint).Port;
        CapturingLogger<WeaverDbConnectionFactory> logger = new();
        WeaverDbConnectionFactory factory = CreateFactory($"Host=127.0.0.1;Port={port};Database=x;Timeout=1;SSL Mode=Disable", logger);
        using CancellationTokenSource cts = new();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.EnsureBackendResolvedAsync(cts.Token));

        Assert.Empty(logger.Warnings);
    }

    /// <summary>
    /// Verifies the unreachable-then-fixed ordering: a failed probe is final for the process.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task UnavailablePostgresAfterSelection_UserRepository_ThrowsAndCreatesNoSqliteFile()
    {
        if (SkipWithoutPostgres())
        {
            return;
        }

        (WeaverDbConnectionFactory factory, _) = await _postgres.CreateUnavailableAfterResolutionAsync(_tempDir);
        using UserRepository repo = new(factory);

        WeaverDatabaseUnavailableException ex = await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => repo.GetUserAsync("anyone"));
        Assert.Equal(WeaverDatabaseBackend.PostgreSql, ex.Backend);
        await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => repo.InitializeAsync());
        Assert.False(File.Exists(Path.Combine(_tempDir, "weaver_config.db")));
    }

    /// <summary>
    /// Verifies a PostgreSQL lost after selection makes PatientDnaRepository throw and creates no SQLite file.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task UnavailablePostgresAfterSelection_PatientDnaRepository_ThrowsAndCreatesNoSqliteFile()
    {
        if (SkipWithoutPostgres())
        {
            return;
        }

        (WeaverDbConnectionFactory factory, _) = await _postgres.CreateUnavailableAfterResolutionAsync(_tempDir);
        using PatientDnaRepository repo = new(factory);

        await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => repo.GetDnaRecordsAsync("some_user"));
        Assert.False(File.Exists(Path.Combine(_tempDir, "weaver_config.db")));
    }

    /// <summary>
    /// Verifies a PostgreSQL lost after selection makes SystemConfigurationStore throw and creates no SQLite file.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task UnavailablePostgresAfterSelection_SystemConfigurationStore_ThrowsAndCreatesNoSqliteFile()
    {
        if (SkipWithoutPostgres())
        {
            return;
        }

        (WeaverDbConnectionFactory factory, _) = await _postgres.CreateUnavailableAfterResolutionAsync(_tempDir);
        using SystemConfigurationStore store = new(factory);

        await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => store.ListShareKeysAsync());
        await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => store.SetDatabaseOverrideAsync("k", "v"));
        Assert.False(File.Exists(Path.Combine(_tempDir, "weaver_config.db")));
    }
}

/// <summary>
/// A logger that records formatted warnings so tests can count them and scan their text.
/// </summary>
/// <typeparam name="T">The category type the logger is created for.</typeparam>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    /// <summary>
    /// Gets the formatted text of every warning-level entry, in order.
    /// </summary>
    public List<string> Warnings { get; } = [];

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc/>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning)
        {
            Warnings.Add(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
        }
    }
}
