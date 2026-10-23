using System.Data.Common;
using NanoWeaverHrt.Database.Access;

namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Orchestrates deterministic, versioned database schema migrations across PostgreSQL and SQLite database engines.
/// </summary>
public sealed class DatabaseMigrationRunner : IDisposable
{
    /// <summary>
    /// Thread synchronization lock for running migrations.
    /// </summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Ordered list of available database migrations.
    /// </summary>
    private readonly IReadOnlyList<IDatabaseMigration> _migrations;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseMigrationRunner"/> class with registered migrations.
    /// </summary>
    /// <param name="migrations">Optional explicit migrations collection; defaults to built-in migrations if omitted.</param>
    public DatabaseMigrationRunner(IEnumerable<IDatabaseMigration>? migrations = null)
    {
        // WHAT: Discovers and orders all registered migration instances by version.
        // WHY:  Guarantees migrations are applied in strict ascending numerical order.
        // HOW:  Sorts by Version property, defaulting to Migration001 and Migration002 if empty or omitted.
        var list = migrations?.ToList();
        if (list != null && list.Count > 0)
        {
            _migrations = list.OrderBy(m => m.Version).ToList();
        }
        else
        {
            _migrations = new List<IDatabaseMigration>
            {
                new Migration001_InitialSchema(),
                new Migration002_UserAuditAndIndexes(),
                new Migration003_UserProfileAddressesAndContacts(),
                new Migration004_PatientDnaImportsAndResults()
            }.OrderBy(m => m.Version).ToList();
        }
    }

    /// <summary>
    /// Gets all registered migrations available to this runner.
    /// </summary>
    public IReadOnlyList<IDatabaseMigration> RegisteredMigrations => _migrations;

    /// <summary>
    /// Ensures that the schema migrations tracking table exists in the target database.
    /// </summary>
    /// <param name="conn">Open database connection.</param>
    /// <param name="dialect">Database engine dialect ("postgresql" or "sqlite").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the tracking table is guaranteed to exist.</returns>
    public async Task EnsureTrackingTableAsync(DbConnection conn, string dialect, CancellationToken ct = default)
    {
        // WHAT: Creates weaver_schema_migrations tracking table if it does not already exist.
        // WHY:  Persists migration execution history to prevent re-execution of previously applied DDL.
        // HOW:  Executes CREATE TABLE IF NOT EXISTS with dialect-appropriate types.
        ArgumentNullException.ThrowIfNull(conn);
        ArgumentNullException.ThrowIfNull(dialect);

        string sql = dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase)
            ? @"CREATE TABLE IF NOT EXISTS weaver_schema_migrations (
                   version BIGINT PRIMARY KEY,
                   name VARCHAR(255) NOT NULL,
                   applied_at_utc TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
               );"
            : @"CREATE TABLE IF NOT EXISTS weaver_schema_migrations (
                   version INTEGER PRIMARY KEY,
                   name TEXT NOT NULL,
                   applied_at_utc TEXT NOT NULL
               );";

        using DbCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Retrieves the list of all migration version numbers that have been successfully applied.
    /// </summary>
    /// <param name="conn">Open database connection.</param>
    /// <param name="dialect">Database dialect identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Read-only list of applied version numbers sorted in ascending order.</returns>
    public async Task<IReadOnlyList<long>> GetAppliedVersionsAsync(DbConnection conn, string dialect, CancellationToken ct = default)
    {
        // WHAT: Queries weaver_schema_migrations for all recorded version IDs.
        // WHY:  Determines which migrations have already completed.
        // HOW:  Ensures table existence then reads all version records via DbDataReader.
        ArgumentNullException.ThrowIfNull(conn);
        ArgumentNullException.ThrowIfNull(dialect);

        await EnsureTrackingTableAsync(conn, dialect, ct);

        List<long> applied = new();
        using DbCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM weaver_schema_migrations ORDER BY version ASC";

        using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            applied.Add(reader.GetInt64(0));
        }

        return applied;
    }

    /// <summary>
    /// Determines which registered migrations have not yet been applied to the target database.
    /// </summary>
    /// <param name="conn">Open database connection.</param>
    /// <param name="dialect">Database dialect identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Read-only list of pending migration instances.</returns>
    public async Task<IReadOnlyList<IDatabaseMigration>> GetPendingMigrationsAsync(DbConnection conn, string dialect, CancellationToken ct = default)
    {
        // WHAT: Calculates difference between registered migrations and applied versions.
        // WHY:  Identifies pending DDL operations needed to reach desired database state.
        // HOW:  Filters _migrations against applied set.
        IReadOnlyList<long> applied = await GetAppliedVersionsAsync(conn, dialect, ct);
        HashSet<long> appliedSet = new(applied);

        return _migrations.Where(m => !appliedSet.Contains(m.Version)).ToList();
    }

    /// <summary>
    /// Applies all pending migrations in ascending version sequence inside transactions.
    /// </summary>
    /// <param name="conn">Open database connection.</param>
    /// <param name="dialect">Database dialect identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Count of migrations successfully applied during this execution.</returns>
    public async Task<int> ApplyPendingMigrationsAsync(DbConnection conn, string dialect, CancellationToken ct = default)
    {
        // WHAT: Executes unapplied migrations sequentially within isolated transactions.
        // WHY:  Ensures atomic, crash-resilient schema evolution without corrupting database state.
        // HOW:  Iterates pending migrations, runs UpAsync, records record in tracking table, and commits.
        ArgumentNullException.ThrowIfNull(conn);
        ArgumentNullException.ThrowIfNull(dialect);

        await _lock.WaitAsync(ct);
        try
        {
            IReadOnlyList<IDatabaseMigration> pending = await GetPendingMigrationsAsync(conn, dialect, ct);
            if (pending.Count == 0)
            {
                return 0;
            }

            int appliedCount = 0;
            foreach (IDatabaseMigration migration in pending)
            {
                using DbTransaction tx = await conn.BeginTransactionAsync(ct);
                try
                {
                    await migration.UpAsync(conn, tx, dialect, ct);

                    using DbCommand recordCmd = conn.CreateCommand();
                    recordCmd.Transaction = tx;
                    bool isPostgres = dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase);
                    recordCmd.CommandText = isPostgres
                        ? "INSERT INTO weaver_schema_migrations (version, name, applied_at_utc) VALUES (@v, @n, @t) ON CONFLICT (version) DO NOTHING"
                        : "INSERT OR REPLACE INTO weaver_schema_migrations (version, name, applied_at_utc) VALUES (@v, @n, @t)";

                    DbParameter pVersion = recordCmd.CreateParameter();
                    pVersion.ParameterName = "@v";
                    pVersion.Value = migration.Version;
                    recordCmd.Parameters.Add(pVersion);

                    DbParameter pName = recordCmd.CreateParameter();
                    pName.ParameterName = "@n";
                    pName.Value = migration.Name;
                    recordCmd.Parameters.Add(pName);

                    DbParameter pTime = recordCmd.CreateParameter();
                    pTime.ParameterName = "@t";
                    // WHAT: Binds the applied-at timestamp in the form each engine's column expects.
                    // WHY:  PostgreSQL declares applied_at_utc as TIMESTAMPTZ and rejects text for it (42804),
                    //       which made every migration fail on PostgreSQL; SQLite declares TEXT and its existing
                    //       rows hold ISO-8601 round-trip strings, so it must keep receiving that format.
                    // HOW:  Asks the engine's dialect to coerce the timestamp instead of formatting it here.
                    pTime.Value = (isPostgres ? PostgreSqlDialect.Instance : SqliteDialect.Instance).TimestampValue(DateTimeOffset.UtcNow);
                    recordCmd.Parameters.Add(pTime);

                    await recordCmd.ExecuteNonQueryAsync(ct);
                    await tx.CommitAsync(ct);

                    appliedCount++;
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }

            return appliedCount;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _lock.Dispose();
    }
}
