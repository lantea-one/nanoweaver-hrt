namespace NanoWeaverHrt.Database.Access;

/// <summary>
/// Describes the few places where SQLite and PostgreSQL genuinely differ for the SQL this platform writes:
/// the boolean literal, and how booleans and timestamps are handed to the driver as parameter values.
/// </summary>
/// <remarks>
/// This interface is deliberately capped at seven members. It never builds SQL statements; every query
/// stays written out in full in its repository. If an eighth member seems necessary the seam is drifting
/// toward a micro-ORM and the design must be revisited instead.
/// </remarks>
public interface ISqlDialect
{
    /// <summary>Gets the dialect identifier the migration runner expects: exactly "postgresql" or "sqlite".</summary>
    string Name { get; }

    /// <summary>Gets the backend this dialect belongs to.</summary>
    WeaverDatabaseBackend Backend { get; }

    /// <summary>Gets the SQL text for boolean true: "TRUE" on PostgreSQL, "1" on SQLite.</summary>
    string TrueLiteral { get; }

    /// <summary>Gets the SQL text for boolean false: "FALSE" on PostgreSQL, "0" on SQLite.</summary>
    string FalseLiteral { get; }

    /// <summary>Converts a boolean into the parameter value the driver for this engine expects.</summary>
    /// <param name="value">The boolean to convert.</param>
    /// <returns>A native <see cref="bool"/> for PostgreSQL, or integer 0/1 for SQLite.</returns>
    object BooleanValue(bool value);

    /// <summary>Converts a timestamp into the parameter value the driver for this engine expects.</summary>
    /// <param name="value">The timestamp to convert.</param>
    /// <returns>A UTC <see cref="DateTime"/> for PostgreSQL, or an ISO-8601 round-trip string for SQLite.</returns>
    object TimestampValue(DateTimeOffset value);

    /// <summary>Converts an optional timestamp, mapping null to a database NULL.</summary>
    /// <param name="value">The timestamp to convert, or null.</param>
    /// <returns><see cref="DBNull.Value"/> when <paramref name="value"/> is null; otherwise the same as <see cref="TimestampValue"/>.</returns>
    object TimestampValueOrNull(DateTimeOffset? value);
}
