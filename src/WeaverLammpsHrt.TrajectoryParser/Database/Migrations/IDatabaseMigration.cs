using System.Data.Common;

namespace WeaverLammpsHrt.TrajectoryParser.Database.Migrations;

/// <summary>
/// Defines a contract for versioned, deterministic database schema migrations supporting PostgreSQL and SQLite.
/// </summary>
public interface IDatabaseMigration
{
    /// <summary>
    /// Gets the unique ascending sequence version number of the migration.
    /// </summary>
    long Version { get; }

    /// <summary>
    /// Gets the human-readable identifier and description of the migration.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Generates the raw DDL statement matching the target database dialect.
    /// </summary>
    /// <param name="dialect">Database engine dialect ("postgresql" or "sqlite").</param>
    /// <returns>DDL script string to execute.</returns>
    string GetSql(string dialect);

    /// <summary>
    /// Executes the migration within an optional active database transaction.
    /// </summary>
    /// <param name="connection">Open database connection.</param>
    /// <param name="transaction">Active transaction, or null if unmanaged.</param>
    /// <param name="dialect">Database dialect identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Asynchronous task representing migration completion.</returns>
    Task UpAsync(DbConnection connection, DbTransaction? transaction, string dialect, CancellationToken ct);
}
