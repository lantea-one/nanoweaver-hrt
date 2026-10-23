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
}
