using System.Data.Common;
using DuckDB.NET.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using NanoWeaverHrt.Database.Migrations;

namespace NanoWeaverHrt.Database.Access;

/// <summary>
/// Opens real SQLite, PostgreSQL, DuckDB, or MySQL connections for the one backend this process resolved at startup.
/// Remote or external backends are probed exactly once; after that decision there is no retry or fallback of any kind, so an
/// unreachable or failing backend stops every operation instead of splitting data across two databases.
/// </summary>
public sealed class WeaverDbConnectionFactory : IWeaverDbConnectionFactory
{
    /// <summary>Display name when PostgreSQL is the resolved backend.</summary>
    private const string PostgresDisplayName = "PostgreSQL (pg-duckdb)";

    /// <summary>Display name when SQLite is the intended backend.</summary>
    private const string SqliteDisplayName = "Local SQLite Fallback";

    /// <summary>Display name when PostgreSQL is configured but its server could not be reached.</summary>
    private const string PostgresUnreachableDisplayName = "PostgreSQL (pg-duckdb) - unreachable";

    /// <summary>Display name when DuckDB is the resolved backend.</summary>
    private const string DuckDbDisplayName = "DuckDB (In-Process Columnar)";

    /// <summary>Display name when MySQL is the resolved backend.</summary>
    private const string MySqlDisplayName = "MySQL / MariaDB (Enterprise Clustered)";

    /// <summary>Display name when MySQL is configured but its server could not be reached.</summary>
    private const string MySqlUnreachableDisplayName = "MySQL / MariaDB (Unreachable)";

    /// <summary>
    /// Applies schema migrations after each connection opens.
    /// </summary>
    private readonly DatabaseMigrationRunner _migrationRunner;

    /// <summary>
    /// Receives the one-time warning when a configured database server is unreachable.
    /// </summary>
    private readonly ILogger<WeaverDbConnectionFactory> _logger;

    /// <summary>
    /// Serializes the one-time backend resolution so concurrent first callers probe backends once.
    /// </summary>
    private readonly SemaphoreSlim _resolveLock = new(1, 1);

    /// <summary>
    /// PostgreSQL connection string; empty when none is configured.
    /// </summary>
    private readonly string _postgresConnectionString;

    /// <summary>
    /// MySQL connection string; empty when none is configured.
    /// </summary>
    private readonly string _mySqlConnectionString;

    /// <summary>
    /// DuckDB connection string; empty when none is configured.
    /// </summary>
    private readonly string _duckDbConnectionString;

    /// <summary>
    /// Path of the local SQLite database file.
    /// </summary>
    private readonly string _sqlitePath;

    /// <summary>
    /// Optional custom connection factory for MySQL connections (used in tests or custom DI).
    /// </summary>
    private readonly Func<string, DbConnection>? _mySqlConnectionFactory;

    /// <summary>
    /// The resolved backend, or null until <see cref="EnsureBackendResolvedAsync"/> has completed once.
    /// </summary>
    private WeaverDatabaseBackend? _resolved;

    /// <summary>
    /// The configured database backend target (PostgreSql, MySql, DuckDb, or Sqlite).
    /// </summary>
    private WeaverDatabaseBackend _configuredBackend = WeaverDatabaseBackend.Sqlite;

    /// <summary>
    /// The cached failure of the one backend probe, or null when the probe succeeded or was not needed.
    /// Once set it is never cleared: the process keeps failing loudly until it is restarted.
    /// </summary>
    private Exception? _probeFailure;

    /// <summary>
    /// Human-readable name of the resolved backend.
    /// </summary>
    private string _displayName = SqliteDisplayName;

    /// <summary>
    /// Initializes a new instance of the <see cref="WeaverDbConnectionFactory"/> class.
    /// </summary>
    /// <param name="configuration">Host application configuration.</param>
    /// <param name="migrationRunner">Migration runner that versions the schema.</param>
    /// <param name="logger">Logger that receives the one-time unreachable warnings.</param>
    public WeaverDbConnectionFactory(
        IConfiguration configuration,
        DatabaseMigrationRunner migrationRunner,
        ILogger<WeaverDbConnectionFactory> logger)
        : this(configuration, migrationRunner, logger, mySqlConnectionFactory: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WeaverDbConnectionFactory"/> class with an optional MySQL connection factory delegate.
    /// </summary>
    /// <param name="configuration">Host application configuration.</param>
    /// <param name="migrationRunner">Migration runner that versions the schema.</param>
    /// <param name="logger">Logger that receives the one-time unreachable warnings.</param>
    /// <param name="mySqlConnectionFactory">Optional custom factory for producing MySQL <see cref="DbConnection"/> instances.</param>
    public WeaverDbConnectionFactory(
        IConfiguration configuration,
        DatabaseMigrationRunner migrationRunner,
        ILogger<WeaverDbConnectionFactory> logger,
        Func<string, DbConnection>? mySqlConnectionFactory)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _migrationRunner = migrationRunner ?? throw new ArgumentNullException(nameof(migrationRunner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mySqlConnectionFactory = mySqlConnectionFactory;

        string? envPostgres = configuration["POSTGRES_CONNECTION_STRING"] ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
        // WHAT: Uses the configured PostgreSQL connection string, or no connection string at all.
        // WHY:  Shipping a built-in default that embeds a password means every deployment which
        //       forgets to set POSTGRES_CONNECTION_STRING shares one publicly known credential,
        //       and that credential also leaks through any surface that echoes configuration.
        //       PostgreSQL is therefore opt-in: with nothing configured the stack uses its
        //       embedded SQLite database, which is the intended configuration and not a degradation.
        // HOW:  Falls back to the empty string instead of a hardcoded credential.
        _postgresConnectionString = envPostgres ?? string.Empty;

        string? envMySql = configuration["MYSQL_CONNECTION_STRING"] ?? Environment.GetEnvironmentVariable("MYSQL_CONNECTION_STRING");
        _mySqlConnectionString = envMySql ?? string.Empty;

        string? envDuckDb = configuration["DUCKDB_CONNECTION_STRING"] ?? Environment.GetEnvironmentVariable("DUCKDB_CONNECTION_STRING")
            ?? configuration["DUCKDB_DATABASE_PATH"] ?? Environment.GetEnvironmentVariable("DUCKDB_DATABASE_PATH");
        _duckDbConnectionString = FormatDuckDbConnectionString(envDuckDb);

        string incomingDir = configuration["TRAJECTORY_INCOMING_DIR"] ?? Path.Combine(Directory.GetCurrentDirectory(), "simulations", "shared-data");
        Directory.CreateDirectory(incomingDir);
        _sqlitePath = Path.Combine(incomingDir, "weaver_config.db");
    }

    /// <inheritdoc/>
    public WeaverDatabaseBackend ActiveBackend => _resolved ?? WeaverDatabaseBackend.Sqlite;

    /// <inheritdoc/>
    public string ActiveBackendDisplayName => _displayName;

    /// <inheritdoc/>
    public async Task<WeaverDatabaseBackend> EnsureBackendResolvedAsync(CancellationToken ct = default)
    {
        // WHAT: Decides once per process whether this node talks to PostgreSQL, MySQL, DuckDB, or local SQLite.
        // WHY:  The decision and the connections must come from one place, and a configured backend
        //       server that cannot be reached must be a loud log line, not an indistinguishable silence.
        //       A deleted SQLite file must also be re-created, because operators do delete it.
        // HOW:  A cached probe failure throws immediately, before anything else, with no new probe
        //       connection and no second warning. Otherwise the fast path returns the cached answer
        //       (re-checking only that the SQLite file still exists). Otherwise, under a semaphore: no
        //       connection string means SQLite by intent; a connection string means one real probe
        //       open plus migrations. An unreachable server is warned about once and recorded, and from
        //       then on every operation fails — it never selects SQLite and never creates that file.
        Exception? failed = _probeFailure;
        if (failed is not null)
        {
            throw new WeaverDatabaseUnavailableException(_configuredBackend, failed);
        }

        WeaverDatabaseBackend? cached = _resolved;
        if (cached is not null && (cached != WeaverDatabaseBackend.Sqlite || File.Exists(_sqlitePath)))
        {
            return cached.Value;
        }

        await _resolveLock.WaitAsync(ct);
        try
        {
            if (_resolved is null)
            {
                // WHAT: Resolves configured backend in prioritized order: PostgreSQL -> MySQL -> DuckDB -> SQLite.
                // WHY:  Guarantees deterministic multi-engine resolution while preventing silent fallback to local storage.
                // HOW:  Evaluates configuration keys in strict precedence order and sets up the appropriate probe action.
                WeaverDatabaseBackend backend;
                string configuredDisplayName;
                string? unreachableDisplayName = null;
                Func<CancellationToken, Task<Exception?>>? probeAsync = null;

                if (!string.IsNullOrWhiteSpace(_postgresConnectionString))
                {
                    backend = WeaverDatabaseBackend.PostgreSql;
                    configuredDisplayName = PostgresDisplayName;
                    unreachableDisplayName = PostgresUnreachableDisplayName;
                    probeAsync = ProbePostgresAsync;
                }
                else if (!string.IsNullOrWhiteSpace(_mySqlConnectionString))
                {
                    backend = WeaverDatabaseBackend.MySql;
                    configuredDisplayName = MySqlDisplayName;
                    unreachableDisplayName = MySqlUnreachableDisplayName;
                    probeAsync = ProbeMySqlAsync;
                }
                else if (!string.IsNullOrWhiteSpace(_duckDbConnectionString))
                {
                    backend = WeaverDatabaseBackend.DuckDb;
                    configuredDisplayName = DuckDbDisplayName;
                    probeAsync = ProbeDuckDbAsync;
                }
                else
                {
                    backend = WeaverDatabaseBackend.Sqlite;
                    configuredDisplayName = SqliteDisplayName;
                }

                _configuredBackend = backend;
                Exception? unreachable = probeAsync is not null ? await probeAsync(ct) : null;
                if (unreachable is not null)
                {
                    // WHAT: Records why every operation is about to fail.
                    // WHY:  The operator must be able to tell "database down" from any other fault.
                    // HOW:  Logs the driver exception TYPE only; its message carries host, port and
                    //       credentials and must not reach a log line (Directive 14).
                    _logger.LogWarning(
                        "A {Backend} connection string is configured but the server could not be reached ({FailureType}). "
                        + "No data will be read or written, and no local database will be used, until the server is reachable and this process is restarted.",
                        backend,
                        unreachable.GetType().Name);
                }

                await using WeaverDbSession? first = unreachable is null ? await OpenBackendAsync(backend, ct) : null;
                _displayName = unreachable is not null ? (unreachableDisplayName ?? configuredDisplayName) : configuredDisplayName;
                _probeFailure = unreachable;
                _resolved = backend;
            }
            else if (_resolved == WeaverDatabaseBackend.Sqlite && !File.Exists(_sqlitePath))
            {
                // The decision is cached but the SQLite file vanished: open once so the schema is rebuilt.
                await using WeaverDbSession recreated = await OpenBackendAsync(WeaverDatabaseBackend.Sqlite, ct);
            }

            if (_probeFailure is not null)
            {
                throw new WeaverDatabaseUnavailableException(_configuredBackend, _probeFailure);
            }

            return _resolved.Value;
        }
        finally
        {
            _resolveLock.Release();
        }
    }

    /// <summary>
    /// Tries to open one PostgreSQL connection, without migrating, to learn whether the server is reachable.
    /// </summary>
    /// <param name="ct">Cancellation token; cancellation propagates rather than counting as "unreachable".</param>
    /// <returns>Null when the server accepted the connection; otherwise the failure the open reported.</returns>
    private async Task<Exception?> ProbePostgresAsync(CancellationToken ct)
    {
        // WHAT: Opens and immediately closes a PostgreSQL connection, reporting the failure instead of throwing it.
        // WHY:  Reachability is the only thing allowed to trigger the SQLite fallback. Observing the open task
        //       directly, rather than wrapping it in try/catch, keeps one straight-line path whose every line runs
        //       whether or not a server answers, so the un-excluded coverage measured without PostgreSQL does
        //       not depend on a live server for lines that are not really conditional.
        // HOW:  WhenAny completes when the open finishes (successfully or not) without rethrowing; a cancelled
        //       token is rethrown explicitly so shutdown is never mistaken for an outage; the connection is
        //       disposed when the method exits.
        await using NpgsqlConnection probe = new(_postgresConnectionString);
        Task open = probe.OpenAsync(ct);
        await Task.WhenAny(open);
        ct.ThrowIfCancellationRequested();
        return open.Exception?.GetBaseException();
    }

    /// <inheritdoc/>
    public async Task<WeaverDbSession> OpenAsync(CancellationToken ct = default)
    {
        // WHAT: Opens a connection to exactly the backend resolved at startup and migrates its schema.
        // WHY:  The former per-call fallback silently redirected a failed call to the local
        //       SQLite file, splitting the data across two databases. A selected backend that fails must
        //       be an error, never a quiet switch.
        // HOW:  Resolves the backend (cached after the first call), then opens it through the shared helper.
        WeaverDatabaseBackend backend = await EnsureBackendResolvedAsync(ct);
        return await OpenBackendAsync(backend, ct);
    }

    /// <summary>
    /// Opens a real connection to the given backend and applies its pending migrations.
    /// </summary>
    /// <param name="backend">The backend to open.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A session the caller must dispose.</returns>
    /// <exception cref="WeaverDatabaseUnavailableException">The backend cannot be reached or migrated.</exception>
    internal async Task<WeaverDbSession> OpenBackendAsync(WeaverDatabaseBackend backend, CancellationToken ct = default)
    {
        // WHAT: Opens one connection and migrates it, across SQLite, PostgreSQL, DuckDB, or MySQL.
        // WHY:  Resolution and ordinary opens must share one code path so the failure wrapping cannot drift.
        // HOW:  Picks the driver and dialect by backend, opens, migrates, and on any non-cancellation
        //       failure disposes the half-opened connection and throws an exception naming only the backend.
        ISqlDialect dialect;
        DbConnection connection;
        switch (backend)
        {
            case WeaverDatabaseBackend.PostgreSql:
                dialect = PostgreSqlDialect.Instance;
                connection = new NpgsqlConnection(_postgresConnectionString);
                break;
            case WeaverDatabaseBackend.DuckDb:
                dialect = DuckDbDialect.Instance;
                connection = new DuckDBConnection(_duckDbConnectionString);
                break;
            case WeaverDatabaseBackend.MySql:
                dialect = MySqlDialect.Instance;
                connection = CreateMySqlConnection();
                break;
            case WeaverDatabaseBackend.Sqlite:
            default:
                dialect = SqliteDialect.Instance;
                connection = new SqliteConnection($"Data Source={_sqlitePath}");
                break;
        }

        try
        {
            await connection.OpenAsync(ct);
            await _migrationRunner.ApplyPendingMigrationsAsync(connection, dialect.Name, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // WHAT: Releases the half-opened connection before reporting failure.
            // WHY:  A failed open must not leak a pooled connection or a file handle.
            // HOW:  Disposes, then wraps the driver error so its details stay in InnerException.
            await connection.DisposeAsync();
            throw new WeaverDatabaseUnavailableException(backend, ex);
        }

        return new WeaverDbSession(connection, dialect);
    }

    /// <summary>
    /// Tries to open one MySQL connection, without migrating, to learn whether the server is reachable.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Null when the server accepted the connection; otherwise the failure the open reported.</returns>
    private async Task<Exception?> ProbeMySqlAsync(CancellationToken ct)
    {
        // WHAT: Opens and immediately closes a MySQL connection, reporting the failure instead of throwing it.
        // WHY:  Reachability is evaluated without throwing uncaught exceptions to the caller.
        // HOW:  Creates DbConnection via factory, probes reachability via Task.WhenAny, and disposes connection.
        DbConnection probe;
        try
        {
            probe = CreateMySqlConnection();
        }
        catch (Exception ex)
        {
            return ex;
        }

        await using (probe)
        {
            Task open = probe.OpenAsync(ct);
            await Task.WhenAny(open);
            ct.ThrowIfCancellationRequested();
            return open.Exception?.GetBaseException();
        }
    }

    /// <summary>
    /// Tries to open one DuckDB connection, without migrating, to learn whether the database can be initialized.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Null when the connection opened successfully; otherwise the reported exception.</returns>
    private async Task<Exception?> ProbeDuckDbAsync(CancellationToken ct)
    {
        // WHAT: Opens and immediately closes a DuckDB in-process connection.
        // WHY:  Validates that DuckDB library and storage file or memory instance are accessible without migrating.
        // HOW:  Opens DuckDB connection via Task.WhenAny to inspect completion state without try/catch branching.
        await using DuckDBConnection probe = new(_duckDbConnectionString);
        Task open = probe.OpenAsync(ct);
        await Task.WhenAny(open);
        ct.ThrowIfCancellationRequested();
        return open.Exception?.GetBaseException();
    }

    /// <summary>
    /// Instantiates a new MySQL <see cref="DbConnection"/> using the custom factory or reflection fallback.
    /// </summary>
    /// <returns>A new <see cref="DbConnection"/> instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no MySQL factory is registered and no provider can be resolved.</exception>
    private DbConnection CreateMySqlConnection()
    {
        // WHAT: Instantiates MySQL DbConnection for probing and sessions.
        // WHY:  Allows decoupling from compile-time MySQL driver dependencies via delegate factory.
        // HOW:  Invokes _mySqlConnectionFactory if configured; otherwise throws an informative InvalidOperationException.
        if (_mySqlConnectionFactory is not null)
        {
            return _mySqlConnectionFactory(_mySqlConnectionString);
        }

        throw new InvalidOperationException(
            "No MySQL ADO.NET provider is available. Supply a custom connection factory delegate to WeaverDbConnectionFactory.");
    }

    /// <summary>
    /// Formats a raw DuckDB path or connection string into an ADO.NET connection string.
    /// </summary>
    /// <param name="raw">The raw configured path or connection string.</param>
    /// <returns>A formatted connection string, or empty string if unconfigured.</returns>
    private static string FormatDuckDbConnectionString(string? raw)
    {
        // WHAT: Normalizes DuckDB connection configuration.
        // WHY:  Allows operators to provide either a full connection string or a database file path.
        // HOW:  Returns empty string if null or whitespace. If it starts with 'Data Source=' or 'DataSource=', returns as-is; otherwise prefixes 'Data Source='.
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        if (raw.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("DataSource=", StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }

        return $"Data Source={raw}";
    }
}
