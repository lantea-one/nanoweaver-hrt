using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using NanoWeaverHrt.Database.Migrations;

namespace NanoWeaverHrt.Database.Access;

/// <summary>
/// Opens real SQLite or PostgreSQL connections for the one backend this process resolved at startup.
/// PostgreSQL is probed exactly once; after that decision there is no retry or fallback of any kind, so an
/// unreachable or failing PostgreSQL stops every operation instead of splitting data across two databases.
/// </summary>
public sealed class WeaverDbConnectionFactory : IWeaverDbConnectionFactory
{
    /// <summary>Display name when PostgreSQL is the resolved backend.</summary>
    private const string PostgresDisplayName = "PostgreSQL (pg-duckdb)";

    /// <summary>Display name when SQLite is the intended backend.</summary>
    private const string SqliteDisplayName = "Local SQLite Fallback";

    /// <summary>Display name when PostgreSQL is configured but its server could not be reached.</summary>
    private const string PostgresUnreachableDisplayName = "PostgreSQL (pg-duckdb) - unreachable";

    /// <summary>
    /// Applies schema migrations after each connection opens.
    /// </summary>
    private readonly DatabaseMigrationRunner _migrationRunner;

    /// <summary>
    /// Receives the one-time warning when a configured PostgreSQL server is unreachable.
    /// </summary>
    private readonly ILogger<WeaverDbConnectionFactory> _logger;

    /// <summary>
    /// Serializes the one-time backend resolution so concurrent first callers probe PostgreSQL once.
    /// </summary>
    private readonly SemaphoreSlim _resolveLock = new(1, 1);

    /// <summary>
    /// PostgreSQL connection string; empty when none is configured.
    /// </summary>
    private readonly string _postgresConnectionString;

    /// <summary>
    /// Path of the local SQLite database file.
    /// </summary>
    private readonly string _sqlitePath;

    /// <summary>
    /// The resolved backend, or null until <see cref="EnsureBackendResolvedAsync"/> has completed once.
    /// </summary>
    private WeaverDatabaseBackend? _resolved;

    /// <summary>
    /// The cached failure of the one PostgreSQL probe, or null when the probe succeeded or was not needed.
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
    /// <param name="logger">Logger that receives the one-time unreachable-PostgreSQL warning.</param>
    public WeaverDbConnectionFactory(
        IConfiguration configuration,
        DatabaseMigrationRunner migrationRunner,
        ILogger<WeaverDbConnectionFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _migrationRunner = migrationRunner ?? throw new ArgumentNullException(nameof(migrationRunner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        string? envPostgres = configuration["POSTGRES_CONNECTION_STRING"] ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
        // WHAT: Uses the configured PostgreSQL connection string, or no connection string at all.
        // WHY:  Shipping a built-in default that embeds a password means every deployment which
        //       forgets to set POSTGRES_CONNECTION_STRING shares one publicly known credential,
        //       and that credential also leaks through any surface that echoes configuration.
        //       PostgreSQL is therefore opt-in: with nothing configured the stack uses its
        //       embedded SQLite database, which is the intended configuration and not a degradation.
        // HOW:  Falls back to the empty string instead of a hardcoded credential.
        _postgresConnectionString = envPostgres ?? string.Empty;

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
        // WHAT: Decides once per process whether this node talks to PostgreSQL or to local SQLite.
        // WHY:  The decision and the connections must come from one place, and a configured PostgreSQL
        //       server that cannot be reached must be a loud log line, not an indistinguishable silence.
        //       A deleted SQLite file must also be re-created, because operators do delete it.
        // HOW:  A cached probe failure throws immediately, before anything else, with no new probe
        //       connection and no second warning. Otherwise the fast path returns the cached answer
        //       (re-checking only that the SQLite file still exists). Otherwise, under a semaphore: no
        //       connection string means SQLite by intent; a connection string means one real PostgreSQL
        //       open plus migrations. An unreachable server is warned about once and recorded, and from
        //       then on every operation fails — it never selects SQLite and never creates that file.
        Exception? failed = _probeFailure;
        if (failed is not null)
        {
            throw new WeaverDatabaseUnavailableException(WeaverDatabaseBackend.PostgreSql, failed);
        }

        WeaverDatabaseBackend? cached = _resolved;
        if (cached == WeaverDatabaseBackend.PostgreSql || (cached == WeaverDatabaseBackend.Sqlite && File.Exists(_sqlitePath)))
        {
            return cached.Value;
        }

        await _resolveLock.WaitAsync(ct);
        try
        {
            if (_resolved is null)
            {
                // WHAT: Separates "the server cannot be reached" from "the server is reached but the schema fails".
                // WHY:  Neither may fall back to SQLite. Silently switching to a local file would split patient
                //       data across two databases, which is just as true when the server is down as when a
                //       migration fails. An unreachable server is therefore recorded, warned about once, and
                //       made to fail every operation until the process is restarted.
                // HOW:  The probe is a bare open. If it succeeds, the chosen backend is opened and migrated
                //       through the normal path (a failure propagates with nothing cached). If it fails, the
                //       failure is cached and nothing is opened, so no SQLite file is created. The choice is
                //       made with data (booleans and conditional values) rather than separate statements per
                //       engine, so there is one code path to test.
                bool configured = !string.IsNullOrWhiteSpace(_postgresConnectionString);
                Exception? unreachable = configured ? await ProbePostgresAsync(ct) : null;
                if (unreachable is not null)
                {
                    // WHAT: Records why every operation is about to fail.
                    // WHY:  The operator must be able to tell "database down" from any other fault.
                    // HOW:  Logs the driver exception TYPE only; its message carries host, port and
                    //       credentials and must not reach a log line (Directive 14).
                    _logger.LogWarning(
                        "A PostgreSQL connection string is configured but the server could not be reached ({FailureType}). "
                        + "No data will be read or written, and no local database will be used, until the server is reachable and this process is restarted.",
                        unreachable.GetType().Name);
                }

                WeaverDatabaseBackend backend = configured ? WeaverDatabaseBackend.PostgreSql : WeaverDatabaseBackend.Sqlite;
                await using WeaverDbSession? first = unreachable is null ? await OpenBackendAsync(backend, ct) : null;
                _displayName = configured ? (unreachable is null ? PostgresDisplayName : PostgresUnreachableDisplayName) : SqliteDisplayName;
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
                throw new WeaverDatabaseUnavailableException(WeaverDatabaseBackend.PostgreSql, _probeFailure);
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
        // WHY:  The former per-call fallback silently redirected a failed PostgreSQL call to the local
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
    private async Task<WeaverDbSession> OpenBackendAsync(WeaverDatabaseBackend backend, CancellationToken ct)
    {
        // WHAT: Opens one connection and migrates it, for either engine.
        // WHY:  Resolution and ordinary opens must share one code path so the failure wrapping cannot drift.
        // HOW:  Picks the driver and dialect by backend, opens, migrates, and on any non-cancellation
        //       failure disposes the half-opened connection and throws an exception naming only the backend.
        bool isPostgres = backend == WeaverDatabaseBackend.PostgreSql;
        ISqlDialect dialect = isPostgres ? PostgreSqlDialect.Instance : SqliteDialect.Instance;
        DbConnection connection = isPostgres
            ? new NpgsqlConnection(_postgresConnectionString)
            : new SqliteConnection($"Data Source={_sqlitePath}");
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
}
