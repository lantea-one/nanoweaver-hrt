namespace NanoWeaverHrt.Database.Access;

/// <summary>
/// Describes engine-specific SQL dialect rules: boolean literals, timestamp mapping,
/// parameter placeholders, and standard upsert / insert-ignore statements.
/// </summary>
public interface ISqlDialect
{
    /// <summary>Gets the dialect identifier the migration runner expects: "postgresql", "sqlite", "duckdb", or "mysql".</summary>
    string Name { get; }

    /// <summary>Gets the backend this dialect belongs to.</summary>
    WeaverDatabaseBackend Backend { get; }

    /// <summary>Gets the SQL text for boolean true: "TRUE" on PostgreSQL/DuckDB, "1" on SQLite/MySQL.</summary>
    string TrueLiteral { get; }

    /// <summary>Gets the SQL text for boolean false: "FALSE" on PostgreSQL/DuckDB, "0" on SQLite/MySQL.</summary>
    string FalseLiteral { get; }

    /// <summary>Converts a boolean into the parameter value the driver for this engine expects.</summary>
    /// <param name="value">The boolean to convert.</param>
    /// <returns>A native <see cref="bool"/> for PostgreSQL/DuckDB, or integer 0/1 for SQLite/MySQL.</returns>
    object BooleanValue(bool value);

    /// <summary>Converts a timestamp into the parameter value the driver for this engine expects.</summary>
    /// <param name="value">The timestamp to convert.</param>
    /// <returns>A UTC <see cref="DateTime"/> for PostgreSQL/DuckDB/MySQL, or an ISO-8601 round-trip string for SQLite.</returns>
    object TimestampValue(DateTimeOffset value);

    /// <summary>Converts an optional timestamp, mapping null to a database NULL.</summary>
    /// <param name="value">The timestamp to convert, or null.</param>
    /// <returns><see cref="DBNull.Value"/> when <paramref name="value"/> is null; otherwise the same as <see cref="TimestampValue"/>.</returns>
    object TimestampValueOrNull(DateTimeOffset? value);

    /// <summary>
    /// Formats a parameter placeholder name appropriate for the database engine.
    /// Returns <c>$name</c> for DuckDB, and <c>@name</c> for SQLite, PostgreSQL, and MySQL.
    /// Leading '@' or '$' characters are stripped from <paramref name="name"/> before prefixing.
    /// </summary>
    /// <param name="name">The parameter identifier (with or without marker prefix).</param>
    /// <returns>The dialect-specific parameter marker string.</returns>
    string Parameter(string name);

    /// <summary>
    /// Builds an engine-specific upsert (INSERT ... ON CONFLICT / ON DUPLICATE KEY UPDATE) SQL statement.
    /// </summary>
    /// <param name="tableName">The target database table name.</param>
    /// <param name="columns">The list of column names being inserted.</param>
    /// <param name="conflictColumns">The unique constraint or primary key columns checked for conflict.</param>
    /// <param name="updateColumns">The list of column names updated upon collision.</param>
    /// <param name="parameterNames">Optional custom parameter placeholder names corresponding to <paramref name="columns"/>. When null, column names are used.</param>
    /// <returns>A fully-formed, engine-compliant upsert SQL statement.</returns>
    string BuildUpsertSql(
        string tableName,
        IReadOnlyList<string> columns,
        IReadOnlyList<string> conflictColumns,
        IReadOnlyList<string> updateColumns,
        IReadOnlyList<string>? parameterNames = null);

    /// <summary>
    /// Builds an engine-specific insert-ignore (INSERT IGNORE / INSERT ... ON CONFLICT DO NOTHING) SQL statement.
    /// </summary>
    /// <param name="tableName">The target database table name.</param>
    /// <param name="columns">The list of column names being inserted.</param>
    /// <param name="conflictColumns">The unique constraint or primary key columns checked for conflict.</param>
    /// <param name="parameterNames">Optional custom parameter placeholder names corresponding to <paramref name="columns"/>. When null, column names are used.</param>
    /// <returns>A fully-formed, engine-compliant insert-ignore SQL statement.</returns>
    string BuildInsertIgnoreSql(
        string tableName,
        IReadOnlyList<string> columns,
        IReadOnlyList<string> conflictColumns,
        IReadOnlyList<string>? parameterNames = null);
}
