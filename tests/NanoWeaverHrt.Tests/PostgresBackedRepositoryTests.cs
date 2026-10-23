using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Moq;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
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
    /// <param name="backendName">The backend name the configuration repository reports.</param>
    /// <param name="postgresConnectionString">The PostgreSQL connection string to configure.</param>
    /// <returns>A real connection factory.</returns>
    private WeaverDbConnectionFactory CreateFactory(string backendName, string postgresConnectionString)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["POSTGRES_CONNECTION_STRING"] = postgresConnectionString
            })
            .Build();
        Mock<IConfigurationRepository> configRepo = new();
        configRepo.SetupGet(c => c.ActiveBackendName).Returns(backendName);
        return new WeaverDbConnectionFactory(configuration, configRepo.Object, new DatabaseMigrationRunner());
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
        await RunUserScenarioAsync(CreateFactory("Local SQLite Fallback", string.Empty));
    }

    /// <summary>
    /// Verifies the patient data scenario on SQLite.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task Sqlite_PatientDnaRepository_Scenario()
    {
        await RunPatientDataScenarioAsync(CreateFactory("Local SQLite Fallback", string.Empty));
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
        using UserRepository repo = new(CreateFactory("Local SQLite Fallback", string.Empty));
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
    /// Verifies a selected-but-unreachable PostgreSQL makes UserRepository throw and creates no SQLite file.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task UserRepository_PostgresSelectedButUnreachable_ThrowsAndCreatesNoSqliteFile()
    {
        // WHAT: Proves UserRepository has no silent PostgreSQL to SQLite fallback.
        // WHY:  The former bare catch blocks hid a PostgreSQL that had never been reachable.
        // HOW:  Selects PostgreSQL at a dead port, calls two methods, expects the unavailable exception, and checks no file exists.
        using UserRepository repo = new(CreateFactory("PostgreSQL (pg-duckdb)", UnreachablePostgres));

        await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => repo.InitializeAsync());
        await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => repo.GetUserAsync("anyone"));
        Assert.False(File.Exists(Path.Combine(_tempDir, "weaver_config.db")));
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

        await RunUserScenarioAsync(CreateFactory("PostgreSQL (pg-duckdb)", await _postgres.CreateIsolatedDatabaseAsync()));
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

        await RunPatientDataScenarioAsync(CreateFactory("PostgreSQL (pg-duckdb)", await _postgres.CreateIsolatedDatabaseAsync()));
        Assert.False(File.Exists(Path.Combine(_tempDir, "weaver_config.db")));
    }
}
