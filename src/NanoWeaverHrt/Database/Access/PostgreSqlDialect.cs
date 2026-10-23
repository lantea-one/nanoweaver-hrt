namespace NanoWeaverHrt.Database.Access;

/// <summary>
/// PostgreSQL implementation of <see cref="ISqlDialect"/>. PostgreSQL has native boolean and timestamp
/// column types, so values travel as native .NET <see cref="bool"/> and UTC <see cref="DateTime"/>.
/// </summary>
public sealed class PostgreSqlDialect : ISqlDialect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlDialect"/> class; private so only the singleton exists.
    /// </summary>
    private PostgreSqlDialect()
    {
    }

    /// <summary>Gets the stateless singleton instance.</summary>
    public static ISqlDialect Instance { get; } = new PostgreSqlDialect();

    /// <inheritdoc/>
    public string Name => "postgresql";

    /// <inheritdoc/>
    public WeaverDatabaseBackend Backend => WeaverDatabaseBackend.PostgreSql;

    /// <inheritdoc/>
    public string TrueLiteral => "TRUE";

    /// <inheritdoc/>
    public string FalseLiteral => "FALSE";

    /// <inheritdoc/>
    public object BooleanValue(bool value) => value;

    /// <inheritdoc/>
    public object TimestampValue(DateTimeOffset value) => value.UtcDateTime;

    /// <inheritdoc/>
    public object TimestampValueOrNull(DateTimeOffset? value) => value.HasValue ? TimestampValue(value.Value) : DBNull.Value;
}
