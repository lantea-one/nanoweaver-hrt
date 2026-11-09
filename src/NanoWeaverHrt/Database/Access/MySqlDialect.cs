namespace NanoWeaverHrt.Database.Access;

/// <summary>
/// MySQL and MariaDB implementation of <see cref="ISqlDialect"/>. MySQL has no native boolean
/// column type (booleans are aliases for TINYINT(1)), so booleans travel as 0/1 integers,
/// while timestamps travel as UTC <see cref="DateTime"/>.
/// </summary>
public sealed class MySqlDialect : ISqlDialect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlDialect"/> class; private so only the singleton exists.
    /// </summary>
    private MySqlDialect()
    {
    }

    /// <summary>Gets the stateless singleton instance.</summary>
    public static ISqlDialect Instance { get; } = new MySqlDialect();

    /// <inheritdoc/>
    public string Name => "mysql";

    /// <inheritdoc/>
    public WeaverDatabaseBackend Backend => WeaverDatabaseBackend.MySql;

    /// <inheritdoc/>
    public string TrueLiteral => "1";

    /// <inheritdoc/>
    public string FalseLiteral => "0";

    /// <inheritdoc/>
    public object BooleanValue(bool value)
    {
        // WHAT: Coerces a boolean flag into an integer representation (1 or 0).
        // WHY:  MySQL uses TINYINT(1) for boolean storage without a native boolean type.
        // HOW:  Returns 1 for true, 0 for false.
        return value ? 1 : 0;
    }

    /// <inheritdoc/>
    public object TimestampValue(DateTimeOffset value) => value.UtcDateTime;

    /// <inheritdoc/>
    public object TimestampValueOrNull(DateTimeOffset? value)
    {
        // WHAT: Maps an optional timestamp to either a UTC DateTime or database NULL.
        // WHY:  MySQL accepts DBNull for nullable DATETIME columns.
        // HOW:  Checks HasValue and coerces via TimestampValue or returns DBNull.Value.
        return value.HasValue ? TimestampValue(value.Value) : DBNull.Value;
    }

    /// <inheritdoc/>
    public string Parameter(string name)
    {
        // WHAT: Normalizes parameter identifier to @name for MySQL queries.
        // WHY:  MySqlConnector parameters are prefixed with '@'.
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
        // WHAT: Builds MySQL ON DUPLICATE KEY UPDATE upsert statement.
        // WHY:  MySQL does not support standard SQL ON CONFLICT syntax.
        // HOW:  Formats column, value, and ON DUPLICATE KEY UPDATE column = VALUES(column) clauses.
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(conflictColumns);
        ArgumentNullException.ThrowIfNull(updateColumns);

        IReadOnlyList<string> valueParams = parameterNames ?? columns;
        string cols = string.Join(", ", columns);
        string vals = string.Join(", ", valueParams.Select(Parameter));
        string updates = string.Join(", ", updateColumns.Select(c => $"{c} = VALUES({c})"));

        return $"INSERT INTO {tableName} ({cols}) VALUES ({vals}) ON DUPLICATE KEY UPDATE {updates}";
    }

    /// <inheritdoc/>
    public string BuildInsertIgnoreSql(
        string tableName,
        IReadOnlyList<string> columns,
        IReadOnlyList<string> conflictColumns,
        IReadOnlyList<string>? parameterNames = null)
    {
        // WHAT: Builds MySQL INSERT IGNORE statement.
        // WHY:  MySQL uses INSERT IGNORE rather than ON CONFLICT DO NOTHING.
        // HOW:  Formats column and value placeholders prefixed with INSERT IGNORE INTO.
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(conflictColumns);

        IReadOnlyList<string> valueParams = parameterNames ?? columns;
        string cols = string.Join(", ", columns);
        string vals = string.Join(", ", valueParams.Select(Parameter));

        return $"INSERT IGNORE INTO {tableName} ({cols}) VALUES ({vals})";
    }
}
