namespace NanoWeaverHrt.Database.Access;

/// <summary>
/// DuckDB implementation of <see cref="ISqlDialect"/>. DuckDB is an in-process columnar SQL engine
/// with native boolean and timestamp support, so values travel as native .NET <see cref="bool"/> and UTC <see cref="DateTime"/>.
/// </summary>
public sealed class DuckDbDialect : ISqlDialect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DuckDbDialect"/> class; private so only the singleton exists.
    /// </summary>
    private DuckDbDialect()
    {
    }

    /// <summary>Gets the stateless singleton instance.</summary>
    public static ISqlDialect Instance { get; } = new DuckDbDialect();

    /// <inheritdoc/>
    public string Name => "duckdb";

    /// <inheritdoc/>
    public WeaverDatabaseBackend Backend => WeaverDatabaseBackend.DuckDb;

    /// <inheritdoc/>
    public string TrueLiteral => "TRUE";

    /// <inheritdoc/>
    public string FalseLiteral => "FALSE";

    /// <inheritdoc/>
    public object BooleanValue(bool value) => value;

    /// <inheritdoc/>
    public object TimestampValue(DateTimeOffset value) => value.UtcDateTime;

    /// <inheritdoc/>
    public object TimestampValueOrNull(DateTimeOffset? value)
    {
        // WHAT: Maps an optional timestamp to either a UTC DateTime or database NULL.
        // WHY:  DuckDB accepts DBNull for nullable TIMESTAMP columns.
        // HOW:  Checks HasValue and coerces via TimestampValue or returns DBNull.Value.
        return value.HasValue ? TimestampValue(value.Value) : DBNull.Value;
    }

    /// <inheritdoc/>
    public string Parameter(string name)
    {
        // WHAT: Normalizes parameter identifier to $name for DuckDB queries.
        // WHY:  DuckDB SQL statements bind parameters using the '$' prefix.
        // HOW:  Trims leading '@' and '$' then prepends '$'.
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return $"${name.TrimStart('@', '$')}";
    }

    /// <inheritdoc/>
    public string BuildUpsertSql(
        string tableName,
        IReadOnlyList<string> columns,
        IReadOnlyList<string> conflictColumns,
        IReadOnlyList<string> updateColumns,
        IReadOnlyList<string>? parameterNames = null)
    {
        // WHAT: Builds DuckDB ON CONFLICT DO UPDATE upsert statement.
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
        // WHAT: Builds DuckDB ON CONFLICT DO NOTHING statement.
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
