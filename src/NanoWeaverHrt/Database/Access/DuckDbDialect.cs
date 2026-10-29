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
}
