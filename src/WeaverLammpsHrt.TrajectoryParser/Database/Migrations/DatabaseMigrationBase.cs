using System.Data.Common;

namespace WeaverLammpsHrt.TrajectoryParser.Database.Migrations;

/// <summary>
/// Abstract base class for SQL schema migrations executing dialect-specific DDL scripts.
/// </summary>
public abstract class DatabaseMigrationBase : IDatabaseMigration
{
    /// <inheritdoc/>
    public abstract long Version { get; }

    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public abstract string GetSql(string dialect);

    /// <inheritdoc/>
    public virtual async Task UpAsync(DbConnection connection, DbTransaction? transaction, string dialect, CancellationToken ct)
    {
        // WHAT: Executes the generated dialect-specific SQL statement within the active connection/transaction.
        // WHY:  Guarantees consistent schema evolution across both PostgreSQL and SQLite backends.
        // HOW:  Splits multi-statement DDL scripts by semicolon and executes each statement sequentially.
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(dialect);

        string sql = GetSql(dialect);
        if (string.IsNullOrWhiteSpace(sql))
        {
            return;
        }

        string[] statements = sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string statement in statements)
        {
            if (string.IsNullOrWhiteSpace(statement))
            {
                continue;
            }

            using DbCommand cmd = connection.CreateCommand();
            cmd.CommandText = statement;
            if (transaction != null)
            {
                cmd.Transaction = transaction;
            }

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
