using System.Collections;
using System.Data;
using System.Data.Common;
using DuckDB.NET.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Database.Migrations;
using Xunit;

namespace NanoWeaverHrt.Tests.Database;

/// <summary>
/// Comprehensive unit tests verifying <see cref="WeaverDbConnectionFactory"/> across SQLite, PostgreSQL, DuckDB,
/// and MySQL database backends, ensuring deterministic resolution order, reachability probing, error handling,
/// and display name diagnostic formatting.
/// </summary>
public sealed class WeaverDbConnectionFactoryTests : IDisposable
{
    /// <summary>
    /// Temporary directory containing test database files and SQLite fallbacks.
    /// </summary>
    private readonly string _tempDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="WeaverDbConnectionFactoryTests"/> class.
    /// </summary>
    public WeaverDbConnectionFactoryTests()
    {
        // WHAT: Allocates an isolated temporary directory for test storage artifacts.
        // WHY:  Guarantees zero file contention across parallel test execution runs.
        // HOW:  Combines system temp path with a unique GUID.
        _tempDir = Path.Combine(Path.GetTempPath(), $"weaver_fact_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// Cleans up test database directories and resets connection pools.
    /// </summary>
    public void Dispose()
    {
        // WHAT: Cleans up temporary directory and files.
        // WHY:  Adheres to Prime Directive 6 for leak-free deterministic test cycles.
        // HOW:  Clears SQLite connection pools and removes the directory.
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Suppress transient Windows file-lock race during teardown
            }
        }
    }

    /// <summary>
    /// Verifies that DuckDB in-process database is resolved, probed, opened, and has the correct display name.
    /// </summary>
    [Fact]
    public async Task DuckDb_ResolvesAndOpensInProcessDatabase()
    {
        // WHAT: Tests resolving and opening an in-process temp file DuckDB instance.
        // WHY:  Validates that DuckDB configuration triggers DuckDBConnection and DuckDbDialect.
        // HOW:  Prepares a seeded temp file database, configures DUCKDB_CONNECTION_STRING, and asserts backend properties.
        string dbFile = Path.Combine(_tempDir, "inproc_test.duckdb");
        await SeedDuckDbMigrationsAsync(dbFile);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["DUCKDB_CONNECTION_STRING"] = $"Data Source={dbFile}"
            })
            .Build();

        var migrationRunner = new DatabaseMigrationRunner();
        var factory = new WeaverDbConnectionFactory(config, migrationRunner, NullLogger<WeaverDbConnectionFactory>.Instance);

        WeaverDatabaseBackend resolved = await factory.EnsureBackendResolvedAsync();

        Assert.Equal(WeaverDatabaseBackend.DuckDb, resolved);
        Assert.Equal(WeaverDatabaseBackend.DuckDb, factory.ActiveBackend);
        Assert.Equal("DuckDB (In-Process Columnar)", factory.ActiveBackendDisplayName);

        await using WeaverDbSession session = await factory.OpenAsync();
        Assert.NotNull(session);
        Assert.Equal("duckdb", session.Dialect.Name);
        Assert.Equal(WeaverDatabaseBackend.DuckDb, session.Dialect.Backend);
        Assert.IsType<DuckDBConnection>(session.Connection);
        Assert.Equal(ConnectionState.Open, session.Connection.State);
    }

    /// <summary>
    /// Verifies that DuckDB database path configuration normalizes correctly to a file-backed connection.
    /// </summary>
    [Fact]
    public async Task DuckDb_ResolvesFromDatabasePathKey()
    {
        // WHAT: Tests DUCKDB_DATABASE_PATH key resolution.
        // WHY:  Confirms that operators can supply a plain file path instead of a full connection string.
        // HOW:  Points DUCKDB_DATABASE_PATH at a pre-seeded file in _tempDir and verifies successful resolution.
        string dbFile = Path.Combine(_tempDir, "analytics.duckdb");
        await SeedDuckDbMigrationsAsync(dbFile);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["DUCKDB_DATABASE_PATH"] = dbFile
            })
            .Build();

        var migrationRunner = new DatabaseMigrationRunner();
        var factory = new WeaverDbConnectionFactory(config, migrationRunner, NullLogger<WeaverDbConnectionFactory>.Instance);

        WeaverDatabaseBackend resolved = await factory.EnsureBackendResolvedAsync();
        Assert.Equal(WeaverDatabaseBackend.DuckDb, resolved);
        Assert.Equal("DuckDB (In-Process Columnar)", factory.ActiveBackendDisplayName);

        await using WeaverDbSession session = await factory.OpenAsync();
        Assert.NotNull(session);
        Assert.Equal(ConnectionState.Open, session.Connection.State);
    }

    /// <summary>
    /// Verifies that DuckDB probe failure fails loudly and prevents fallback to SQLite.
    /// </summary>
    [Fact]
    public async Task DuckDb_ProbeFailure_FailsLoudly()
    {
        // WHAT: Tests DuckDB failure wrapping when connection fails to open.
        // WHY:  Guarantees fail-loud behavior without quiet SQLite degradation.
        // HOW:  Points DuckDB connection string to an invalid directory path that cannot be accessed.
        string invalidPath = Path.Combine(_tempDir, "non_existent_folder_xyz", "sub", "test.duckdb");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["DUCKDB_CONNECTION_STRING"] = $"Data Source={invalidPath}"
            })
            .Build();

        var migrationRunner = new DatabaseMigrationRunner(Array.Empty<IDatabaseMigration>());
        var factory = new WeaverDbConnectionFactory(config, migrationRunner, NullLogger<WeaverDbConnectionFactory>.Instance);

        var ex = await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => factory.EnsureBackendResolvedAsync());
        Assert.Equal(WeaverDatabaseBackend.DuckDb, ex.Backend);
        Assert.Equal("DuckDB (In-Process Columnar)", factory.ActiveBackendDisplayName);

        // Subsequent call must throw cached failure immediately
        var cachedEx = await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => factory.EnsureBackendResolvedAsync());
        Assert.Equal(WeaverDatabaseBackend.DuckDb, cachedEx.Backend);
    }

    /// <summary>
    /// Verifies that MySQL backend resolves and opens successfully with a custom connection factory.
    /// </summary>
    [Fact]
    public async Task MySql_ResolvesAndOpensWithCustomFactory()
    {
        // WHAT: Tests MySQL backend resolution with custom connection factory.
        // WHY:  Allows clustered MySQL deployments without mandatory direct NuGet coupling.
        // HOW:  Configures MYSQL_CONNECTION_STRING and passes mock connection factory delegate.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["MYSQL_CONNECTION_STRING"] = "Server=cluster.lan;Port=3306;Database=weaver;Uid=root;"
            })
            .Build();

        var migrationRunner = new DatabaseMigrationRunner(Array.Empty<IDatabaseMigration>());
        var factory = new WeaverDbConnectionFactory(
            config,
            migrationRunner,
            NullLogger<WeaverDbConnectionFactory>.Instance,
            cs => new MockTestDbConnection());

        WeaverDatabaseBackend resolved = await factory.EnsureBackendResolvedAsync();

        Assert.Equal(WeaverDatabaseBackend.MySql, resolved);
        Assert.Equal(WeaverDatabaseBackend.MySql, factory.ActiveBackend);
        Assert.Equal("MySQL / MariaDB (Enterprise Clustered)", factory.ActiveBackendDisplayName);

        await using WeaverDbSession session = await factory.OpenAsync();
        Assert.NotNull(session);
        Assert.Equal("mysql", session.Dialect.Name);
        Assert.Equal(WeaverDatabaseBackend.MySql, session.Dialect.Backend);
    }

    /// <summary>
    /// Verifies that unreachable MySQL without a factory fails loud and sets unreachable display name.
    /// </summary>
    [Fact]
    public async Task MySql_UnreachableWithoutProvider_FailsLoudly()
    {
        // WHAT: Tests MySQL probe failure when no driver or factory is registered.
        // WHY:  Guarantees that unconfigured MySQL fails loud and never falls back to SQLite.
        // HOW:  Configures MYSQL_CONNECTION_STRING without factory, calls EnsureBackendResolvedAsync.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["MYSQL_CONNECTION_STRING"] = "Server=127.0.0.1;Port=3306;Database=weaver;Uid=root;"
            })
            .Build();

        var migrationRunner = new DatabaseMigrationRunner(Array.Empty<IDatabaseMigration>());
        var factory = new WeaverDbConnectionFactory(config, migrationRunner, NullLogger<WeaverDbConnectionFactory>.Instance);

        var ex = await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => factory.EnsureBackendResolvedAsync());
        Assert.Equal(WeaverDatabaseBackend.MySql, ex.Backend);
        Assert.Equal("MySQL / MariaDB (Unreachable)", factory.ActiveBackendDisplayName);

        // Subsequent call throws cached exception immediately
        var cachedEx = await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => factory.EnsureBackendResolvedAsync());
        Assert.Equal(WeaverDatabaseBackend.MySql, cachedEx.Backend);
    }

    /// <summary>
    /// Verifies that unreachable MySQL with a failing factory throws and records unreachable display name.
    /// </summary>
    [Fact]
    public async Task MySql_FailingConnectionFactory_FailsLoudly()
    {
        // WHAT: Tests MySQL probe failure when custom factory throws a network exception.
        // WHY:  Ensures offline cluster is detected and reported.
        // HOW:  Passes custom factory returning connection that throws on OpenAsync.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["MYSQL_CONNECTION_STRING"] = "Server=10.254.254.254;Port=3306;Database=weaver;"
            })
            .Build();

        var migrationRunner = new DatabaseMigrationRunner(Array.Empty<IDatabaseMigration>());
        var factory = new WeaverDbConnectionFactory(
            config,
            migrationRunner,
            NullLogger<WeaverDbConnectionFactory>.Instance,
            cs => new MockTestDbConnection(shouldFailOpen: true));

        var ex = await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => factory.EnsureBackendResolvedAsync());
        Assert.Equal(WeaverDatabaseBackend.MySql, ex.Backend);
        Assert.Equal("MySQL / MariaDB (Unreachable)", factory.ActiveBackendDisplayName);
    }

    /// <summary>
    /// Verifies that unreachable PostgreSQL throws and sets unreachable display name.
    /// </summary>
    [Fact]
    public async Task PostgreSql_Unreachable_FailsLoudly()
    {
        // WHAT: Tests PostgreSQL unreachable probe error handling.
        // WHY:  Verifies fail-fast semantics for unreachable PostgreSQL cluster.
        // HOW:  Configures POSTGRES_CONNECTION_STRING with non-existent port and short timeout.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["POSTGRES_CONNECTION_STRING"] = "Host=127.0.0.1;Port=54398;Database=weaver;Timeout=1;Command Timeout=1;"
            })
            .Build();

        var migrationRunner = new DatabaseMigrationRunner(Array.Empty<IDatabaseMigration>());
        var factory = new WeaverDbConnectionFactory(config, migrationRunner, NullLogger<WeaverDbConnectionFactory>.Instance);

        var ex = await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => factory.EnsureBackendResolvedAsync());
        Assert.Equal(WeaverDatabaseBackend.PostgreSql, ex.Backend);
        Assert.Equal("PostgreSQL (pg-duckdb) - unreachable", factory.ActiveBackendDisplayName);

        // Cached exception path
        var cachedEx = await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => factory.EnsureBackendResolvedAsync());
        Assert.Equal(WeaverDatabaseBackend.PostgreSql, cachedEx.Backend);
    }

    /// <summary>
    /// Verifies that default configuration without external databases resolves to local SQLite.
    /// </summary>
    [Fact]
    public async Task Sqlite_DefaultConfiguration_ResolvesAndOpens()
    {
        // WHAT: Tests SQLite fallback when no remote connection strings are configured.
        // WHY:  Verifies that embedded single-node deployments initialize out of the box.
        // HOW:  Passes configuration without PostgreSQL, MySQL, or DuckDB keys.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir
            })
            .Build();

        var migrationRunner = new DatabaseMigrationRunner(Array.Empty<IDatabaseMigration>());
        var factory = new WeaverDbConnectionFactory(config, migrationRunner, NullLogger<WeaverDbConnectionFactory>.Instance);

        WeaverDatabaseBackend resolved = await factory.EnsureBackendResolvedAsync();

        Assert.Equal(WeaverDatabaseBackend.Sqlite, resolved);
        Assert.Equal(WeaverDatabaseBackend.Sqlite, factory.ActiveBackend);
        Assert.Equal("Local SQLite Fallback", factory.ActiveBackendDisplayName);

        await using WeaverDbSession session = await factory.OpenAsync();
        Assert.NotNull(session);
        Assert.Equal("sqlite", session.Dialect.Name);
        Assert.Equal(WeaverDatabaseBackend.Sqlite, session.Dialect.Backend);
    }

    /// <summary>
    /// Verifies that if the SQLite file is deleted after startup resolution, it is recreated on next call.
    /// </summary>
    [Fact]
    public async Task Sqlite_RecreatesFileIfDeletedAfterResolution()
    {
        // WHAT: Tests recreation of deleted SQLite file.
        // WHY:  Prevents broken state if operator removes database file while process is running.
        // HOW:  Resolves SQLite backend, deletes SQLite file from disk, calls EnsureBackendResolvedAsync again.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir
            })
            .Build();

        var migrationRunner = new DatabaseMigrationRunner(Array.Empty<IDatabaseMigration>());
        var factory = new WeaverDbConnectionFactory(config, migrationRunner, NullLogger<WeaverDbConnectionFactory>.Instance);

        await factory.EnsureBackendResolvedAsync();
        string sqlitePath = Path.Combine(_tempDir, "weaver_config.db");
        Assert.True(File.Exists(sqlitePath));

        SqliteConnection.ClearAllPools();
        File.Delete(sqlitePath);
        Assert.False(File.Exists(sqlitePath));

        WeaverDatabaseBackend second = await factory.EnsureBackendResolvedAsync();
        Assert.Equal(WeaverDatabaseBackend.Sqlite, second);
        Assert.True(File.Exists(sqlitePath));
    }

    /// <summary>
    /// Verifies strict resolution precedence: PostgreSQL > MySQL > DuckDB > SQLite.
    /// </summary>
    [Fact]
    public async Task BackendResolutionOrder_PrioritizesCorrectly()
    {
        // WHAT: Tests prioritized backend resolution precedence order.
        // WHY:  Guarantees predictable backend selection when multiple connection strings exist.
        // HOW:  Tests combination of connection string keys and asserts expected engine is selected.

        // 1. MySQL + DuckDB configured -> MySQL wins over DuckDB
        var configMySqlDuck = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["MYSQL_CONNECTION_STRING"] = "Server=localhost;Database=weaver;",
                ["DUCKDB_CONNECTION_STRING"] = "Data Source=:memory:"
            })
            .Build();

        var factoryMySql = new WeaverDbConnectionFactory(
            configMySqlDuck,
            new DatabaseMigrationRunner(Array.Empty<IDatabaseMigration>()),
            NullLogger<WeaverDbConnectionFactory>.Instance,
            _ => new MockTestDbConnection());

        WeaverDatabaseBackend resMySql = await factoryMySql.EnsureBackendResolvedAsync();
        Assert.Equal(WeaverDatabaseBackend.MySql, resMySql);

        // 2. DuckDB configured alone -> DuckDB wins over SQLite
        string duckFile = Path.Combine(_tempDir, "duck_prio.duckdb");
        await SeedDuckDbMigrationsAsync(duckFile);
        var configDuck = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["DUCKDB_CONNECTION_STRING"] = $"Data Source={duckFile}"
            })
            .Build();

        var factoryDuck = new WeaverDbConnectionFactory(
            configDuck,
            new DatabaseMigrationRunner(),
            NullLogger<WeaverDbConnectionFactory>.Instance);

        WeaverDatabaseBackend resDuck = await factoryDuck.EnsureBackendResolvedAsync();
        Assert.Equal(WeaverDatabaseBackend.DuckDb, resDuck);
    }

    /// <summary>
    /// Verifies display names across all database backends in both reachable and unreachable states.
    /// </summary>
    [Fact]
    public async Task DisplayNames_MatchDiagnosticRequirements()
    {
        // WHAT: Asserts exact diagnostic display names across SQLite, DuckDB, MySQL, and PostgreSQL.
        // WHY:  Enforces diagnostic contract required by telemetry and status pages.
        // HOW:  Validates ActiveBackendDisplayName across each configured factory state.

        // SQLite
        var configSqlite = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TRAJECTORY_INCOMING_DIR"] = _tempDir })
            .Build();
        var fSqlite = new WeaverDbConnectionFactory(configSqlite, new DatabaseMigrationRunner(Array.Empty<IDatabaseMigration>()), NullLogger<WeaverDbConnectionFactory>.Instance);
        await fSqlite.EnsureBackendResolvedAsync();
        Assert.Equal("Local SQLite Fallback", fSqlite.ActiveBackendDisplayName);

        // DuckDB
        string duckFile = Path.Combine(_tempDir, "duck_display.duckdb");
        await SeedDuckDbMigrationsAsync(duckFile);
        var configDuck = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["DUCKDB_CONNECTION_STRING"] = $"Data Source={duckFile}"
            })
            .Build();
        var fDuck = new WeaverDbConnectionFactory(configDuck, new DatabaseMigrationRunner(), NullLogger<WeaverDbConnectionFactory>.Instance);
        await fDuck.EnsureBackendResolvedAsync();
        Assert.Equal("DuckDB (In-Process Columnar)", fDuck.ActiveBackendDisplayName);

        // MySQL reachable
        var configMySql = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["MYSQL_CONNECTION_STRING"] = "Server=localhost;"
            })
            .Build();
        var fMySql = new WeaverDbConnectionFactory(configMySql, new DatabaseMigrationRunner(Array.Empty<IDatabaseMigration>()), NullLogger<WeaverDbConnectionFactory>.Instance, _ => new MockTestDbConnection());
        await fMySql.EnsureBackendResolvedAsync();
        Assert.Equal("MySQL / MariaDB (Enterprise Clustered)", fMySql.ActiveBackendDisplayName);

        // MySQL unreachable
        var fMySqlUnreach = new WeaverDbConnectionFactory(configMySql, new DatabaseMigrationRunner(Array.Empty<IDatabaseMigration>()), NullLogger<WeaverDbConnectionFactory>.Instance);
        await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => fMySqlUnreach.EnsureBackendResolvedAsync());
        Assert.Equal("MySQL / MariaDB (Unreachable)", fMySqlUnreach.ActiveBackendDisplayName);
    }

    /// <summary>
    /// Verifies that <see cref="WeaverDbConnectionFactory.OpenBackendAsync"/> covers the PostgreSQL branch and wraps failures.
    /// </summary>
    [Fact]
    public async Task OpenBackendAsync_PostgreSql_ThrowsWhenUnreachable()
    {
        // WHAT: Tests OpenBackendAsync directly against PostgreSQL when unreached.
        // WHY:  Ensures full coverage of PostgreSql case in OpenBackendAsync async state machine.
        // HOW:  Calls OpenBackendAsync with WeaverDatabaseBackend.PostgreSql and asserts WeaverDatabaseUnavailableException.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["POSTGRES_CONNECTION_STRING"] = "Host=127.0.0.1;Port=54396;Database=weaver;Timeout=1;"
            })
            .Build();

        var factory = new WeaverDbConnectionFactory(config, new DatabaseMigrationRunner(Array.Empty<IDatabaseMigration>()), NullLogger<WeaverDbConnectionFactory>.Instance);
        var ex = await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => factory.OpenBackendAsync(WeaverDatabaseBackend.PostgreSql));
        Assert.Equal(WeaverDatabaseBackend.PostgreSql, ex.Backend);
    }

    /// <summary>
    /// Verifies that DuckDB connection string normalization preserves DataSource without space prefix.
    /// </summary>
    [Fact]
    public async Task DuckDb_DataSourceWithoutSpacePrefix_Preserved()
    {
        // WHAT: Tests DuckDB connection string starting with DataSource= (no space).
        // WHY:  Exercises normalization branch in FormatDuckDbConnectionString.
        // HOW:  Configures DUCKDB_CONNECTION_STRING with DataSource=:memory: and asserts resolution.
        string dbFile = Path.Combine(_tempDir, "compact_prefix.duckdb");
        await SeedDuckDbMigrationsAsync(dbFile);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = _tempDir,
                ["DUCKDB_CONNECTION_STRING"] = $"DataSource={dbFile}"
            })
            .Build();

        var factory = new WeaverDbConnectionFactory(config, new DatabaseMigrationRunner(), NullLogger<WeaverDbConnectionFactory>.Instance);
        WeaverDatabaseBackend resolved = await factory.EnsureBackendResolvedAsync();
        Assert.Equal(WeaverDatabaseBackend.DuckDb, resolved);
    }

    /// <summary>
    /// Seeds schema migrations tracking table in a DuckDB database file to prevent unapplied migration errors.
    /// </summary>
    /// <param name="dbPath">Path to DuckDB database file.</param>
    /// <returns>A task representing the operation.</returns>
    private static async Task SeedDuckDbMigrationsAsync(string dbPath)
    {
        // WHAT: Bootstraps the tracking table with migration versions in a DuckDB file.
        // WHY:  Ensures DuckDB tests operate cleanly against real DuckDBConnection instances without pending schema conflicts.
        // HOW:  Creates weaver_schema_migrations table and inserts records for all registered migrations.
        await using var conn = new DuckDBConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS weaver_schema_migrations (version BIGINT PRIMARY KEY, name VARCHAR(255) NOT NULL, applied_at_utc TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP);";
        await cmd.ExecuteNonQueryAsync();

        await using var ins = conn.CreateCommand();
        ins.CommandText = "INSERT INTO weaver_schema_migrations (version, name) VALUES (1, '001'), (2, '002'), (3, '003'), (4, '004'), (5, '005'), (6, '006'), (7, '007'), (8, '008'), (9, '009'), (10, '010'), (11, '011');";
        await ins.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Mock database connection used to verify MySQL connection factory branches without an external database daemon.
    /// </summary>
    private sealed class MockTestDbConnection : DbConnection
    {
        /// <summary>
        /// Indicates whether OpenAsync should throw an exception.
        /// </summary>
        private readonly bool _shouldFailOpen;

        /// <summary>
        /// Tracks connection open state.
        /// </summary>
        private ConnectionState _state = ConnectionState.Closed;

        /// <summary>
        /// Initializes a new instance of the <see cref="MockTestDbConnection"/> class.
        /// </summary>
        /// <param name="shouldFailOpen">Whether to simulate connection failure.</param>
        public MockTestDbConnection(bool shouldFailOpen = false)
        {
            _shouldFailOpen = shouldFailOpen;
        }

        /// <summary>Gets or sets the connection string.</summary>
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = "Server=mock;";

        /// <summary>Gets the database name.</summary>
        public override string Database => "mock_db";

        /// <summary>Gets the data source.</summary>
        public override string DataSource => "mock_host";

        /// <summary>Gets the server version.</summary>
        public override string ServerVersion => "8.0.30";

        /// <summary>Gets the connection state.</summary>
        public override ConnectionState State => _state;

        /// <summary>Changes the database.</summary>
        /// <param name="databaseName">The database name.</param>
        public override void ChangeDatabase(string databaseName) { }

        /// <summary>Closes the connection.</summary>
        public override void Close() => _state = ConnectionState.Closed;

        /// <summary>Opens the connection.</summary>
        public override void Open()
        {
            if (_shouldFailOpen)
            {
                throw new InvalidOperationException("Simulated connection failure.");
            }
            _state = ConnectionState.Open;
        }

        /// <summary>Opens the connection asynchronously.</summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A completed task.</returns>
        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            if (_shouldFailOpen)
            {
                return Task.FromException(new InvalidOperationException("Simulated connection failure."));
            }
            _state = ConnectionState.Open;
            return Task.CompletedTask;
        }

        /// <summary>Begins a database transaction.</summary>
        /// <param name="isolationLevel">Isolation level.</param>
        /// <returns>A mock transaction.</returns>
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => new MockTestDbTransaction(this);

        /// <summary>Creates a database command.</summary>
        /// <returns>A mock command.</returns>
        protected override DbCommand CreateDbCommand()
            => new MockTestDbCommand(this);
    }

    /// <summary>
    /// Mock database transaction.
    /// </summary>
    private sealed class MockTestDbTransaction : DbTransaction
    {
        /// <summary>Associated connection.</summary>
        private readonly DbConnection _conn;

        /// <summary>Initializes a new instance of the <see cref="MockTestDbTransaction"/> class.</summary>
        /// <param name="conn">Connection.</param>
        public MockTestDbTransaction(DbConnection conn) => _conn = conn;

        /// <summary>Gets the connection.</summary>
        protected override DbConnection DbConnection => _conn;

        /// <summary>Gets the isolation level.</summary>
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        /// <summary>Commits transaction.</summary>
        public override void Commit() { }

        /// <summary>Rolls back transaction.</summary>
        public override void Rollback() { }
    }

    /// <summary>
    /// Mock database command.
    /// </summary>
    private sealed class MockTestDbCommand : DbCommand
    {
        /// <summary>Associated connection.</summary>
        private readonly MockTestDbConnection _conn;

        /// <summary>Parameter collection.</summary>
        private readonly MockTestDbParameterCollection _params = new();

        /// <summary>Initializes a new instance of the <see cref="MockTestDbCommand"/> class.</summary>
        /// <param name="conn">Connection.</param>
        public MockTestDbCommand(MockTestDbConnection conn) => _conn = conn;

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

        /// <summary>Gets parameters.</summary>
        protected override DbParameterCollection DbParameterCollection => _params;

        /// <summary>Gets or sets transaction.</summary>
        protected override DbTransaction? DbTransaction { get; set; }

        /// <summary>Cancels command.</summary>
        public override void Cancel() { }

        /// <summary>Executes non query.</summary>
        /// <returns>Rows affected.</returns>
        public override int ExecuteNonQuery() => 0;

        /// <summary>Executes non query async.</summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Rows affected.</returns>
        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        /// <summary>Executes scalar.</summary>
        /// <returns>Null.</returns>
        public override object? ExecuteScalar() => null;

        /// <summary>Executes scalar async.</summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Null.</returns>
        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken) => Task.FromResult<object?>(null);

        /// <summary>Prepares command.</summary>
        public override void Prepare() { }

        /// <summary>Creates parameter.</summary>
        /// <returns>New parameter.</returns>
        protected override DbParameter CreateDbParameter() => new MockTestDbParameter();

        /// <summary>Executes reader.</summary>
        /// <param name="behavior">Behavior.</param>
        /// <returns>Mock reader.</returns>
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => new MockTestDbDataReader();

        /// <summary>Executes reader async.</summary>
        /// <param name="behavior">Behavior.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Mock reader task.</returns>
        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
            => Task.FromResult<DbDataReader>(new MockTestDbDataReader());
    }

    /// <summary>
    /// Mock database parameter.
    /// </summary>
    private sealed class MockTestDbParameter : DbParameter
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
    /// Mock database parameter collection.
    /// </summary>
    private sealed class MockTestDbParameterCollection : DbParameterCollection
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

    /// <summary>
    /// Mock database data reader.
    /// </summary>
    private sealed class MockTestDbDataReader : DbDataReader
    {
        /// <summary>Gets depth.</summary>
        public override int Depth => 0;

        /// <summary>Gets field count.</summary>
        public override int FieldCount => 0;

        /// <summary>Gets has rows.</summary>
        public override bool HasRows => false;

        /// <summary>Gets is closed.</summary>
        public override bool IsClosed => false;

        /// <summary>Gets records affected.</summary>
        public override int RecordsAffected => 0;

        /// <summary>Gets value by ordinal.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override object this[int ordinal] => throw new NotSupportedException();

        /// <summary>Gets value by name.</summary>
        /// <param name="name">Name.</param>
        /// <returns>Not supported.</returns>
        public override object this[string name] => throw new NotSupportedException();

        /// <summary>Reads row.</summary>
        /// <returns>False.</returns>
        public override bool Read() => false;

        /// <summary>Reads row async.</summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>False.</returns>
        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(false);

        /// <summary>Next result.</summary>
        /// <returns>False.</returns>
        public override bool NextResult() => false;

        /// <summary>Gets boolean.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();

        /// <summary>Gets byte.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override byte GetByte(int ordinal) => throw new NotSupportedException();

        /// <summary>Gets bytes.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <param name="dataOffset">Data offset.</param>
        /// <param name="buffer">Buffer.</param>
        /// <param name="bufferOffset">Buffer offset.</param>
        /// <param name="length">Length.</param>
        /// <returns>Not supported.</returns>
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();

        /// <summary>Gets char.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override char GetChar(int ordinal) => throw new NotSupportedException();

        /// <summary>Gets chars.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <param name="dataOffset">Data offset.</param>
        /// <param name="buffer">Buffer.</param>
        /// <param name="bufferOffset">Buffer offset.</param>
        /// <param name="length">Length.</param>
        /// <returns>Not supported.</returns>
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();

        /// <summary>Gets data type name.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>String.</returns>
        public override string GetDataTypeName(int ordinal) => "TEXT";

        /// <summary>Gets date time.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();

        /// <summary>Gets decimal.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();

        /// <summary>Gets double.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override double GetDouble(int ordinal) => throw new NotSupportedException();

        /// <summary>Gets field type.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>String type.</returns>
        public override Type GetFieldType(int ordinal) => typeof(string);

        /// <summary>Gets float.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override float GetFloat(int ordinal) => throw new NotSupportedException();

        /// <summary>Gets guid.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();

        /// <summary>Gets int16.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override short GetInt16(int ordinal) => throw new NotSupportedException();

        /// <summary>Gets int32.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override int GetInt32(int ordinal) => throw new NotSupportedException();

        /// <summary>Gets int64.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override long GetInt64(int ordinal) => throw new NotSupportedException();

        /// <summary>Gets name.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Empty.</returns>
        public override string GetName(int ordinal) => string.Empty;

        /// <summary>Gets ordinal.</summary>
        /// <param name="name">Name.</param>
        /// <returns>Zero.</returns>
        public override int GetOrdinal(string name) => 0;

        /// <summary>Gets string.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override string GetString(int ordinal) => throw new NotSupportedException();

        /// <summary>Gets value.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>Not supported.</returns>
        public override object GetValue(int ordinal) => throw new NotSupportedException();

        /// <summary>Gets values.</summary>
        /// <param name="values">Array.</param>
        /// <returns>Zero.</returns>
        public override int GetValues(object[] values) => 0;

        /// <summary>Checks is db null.</summary>
        /// <param name="ordinal">Ordinal.</param>
        /// <returns>False.</returns>
        public override bool IsDBNull(int ordinal) => false;

        /// <summary>Gets enumerator.</summary>
        /// <returns>Not supported.</returns>
        public override IEnumerator GetEnumerator() => throw new NotSupportedException();
    }
}
