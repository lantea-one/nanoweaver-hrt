namespace NanoWeaverHrt.Database.Access;

/// <summary>
/// SQLite implementation of <see cref="ISqlDialect"/>. SQLite has no native boolean or timestamp column
/// type, so booleans travel as 0/1 integers and timestamps as ISO-8601 text.
/// </summary>
public sealed class SqliteDialect : ISqlDialect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteDialect"/> class; private so only the singleton exists.
    /// </summary>
    private SqliteDialect()
    {
    }

    /// <summary>Gets the stateless singleton instance.</summary>
    public static ISqlDialect Instance { get; } = new SqliteDialect();

    /// <inheritdoc/>
    public string Name => "sqlite";

    /// <inheritdoc/>
    public WeaverDatabaseBackend Backend => WeaverDatabaseBackend.Sqlite;

    /// <inheritdoc/>
    public string TrueLiteral => "1";

    /// <inheritdoc/>
    public string FalseLiteral => "0";

    /// <inheritdoc/>
    public object BooleanValue(bool value) => value ? 1 : 0;

    /// <inheritdoc/>
    public object TimestampValue(DateTimeOffset value) => value.ToString("o");

    /// <inheritdoc/>
    public object TimestampValueOrNull(DateTimeOffset? value) => value.HasValue ? TimestampValue(value.Value) : DBNull.Value;

    /// <inheritdoc/>
    public string Parameter(string name)
    {
        // WHAT: Normalizes parameter identifier to @name for SQLite queries.
        // WHY:  SQLite expects parameters prefixed with '@'.
        // HOW:  Trims leading '@' and '$' then prepends '@'.
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return $"@{name.TrimStart('@', '$')}";
    }

    /// <inheritdoc/>
    public string BuildUpsertSql(
        string tableName,
        IReadOnlyList<string> columns,
        IReadOnlyList<string> conflictColumns,
        IReadOnlyList<string> updateColumns,
        IReadOnlyList<string>? parameterNames = null)
    {
        // WHAT: Builds SQLite ON CONFLICT DO UPDATE upsert statement.
        // WHY:  Standardizes upsert statements without dialect divergence.
        // HOW:  Formats column, value, conflict, and EXCLUDED assignment clauses.
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(conflictColumns);
        ArgumentNullException.ThrowIfNull(updateColumns);

        IReadOnlyList<string> valueParams = parameterNames ?? columns;
        string cols = string.Join(", ", columns);
        string vals = string.Join(", ", valueParams.Select(Parameter));
        string conflicts = string.Join(", ", conflictColumns);
        string updates = string.Join(", ", updateColumns.Select(c => $"{c} = EXCLUDED.{c}"));

        return $"INSERT INTO {tableName} ({cols}) VALUES ({vals}) ON CONFLICT ({conflicts}) DO UPDATE SET {updates}";
    }

    /// <inheritdoc/>
    public string BuildInsertIgnoreSql(
        string tableName,
        IReadOnlyList<string> columns,
        IReadOnlyList<string> conflictColumns,
        IReadOnlyList<string>? parameterNames = null)
    {
        // WHAT: Builds SQLite ON CONFLICT DO NOTHING statement.
        // WHY:  Ignores primary key / unique constraint collisions idempotently.
        // HOW:  Formats column, value, and ON CONFLICT DO NOTHING clauses.
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(conflictColumns);

        IReadOnlyList<string> valueParams = parameterNames ?? columns;
        string cols = string.Join(", ", columns);
        string vals = string.Join(", ", valueParams.Select(Parameter));
        string conflicts = string.Join(", ", conflictColumns);

        return $"INSERT INTO {tableName} ({cols}) VALUES ({vals}) ON CONFLICT ({conflicts}) DO NOTHING";
    }
}
