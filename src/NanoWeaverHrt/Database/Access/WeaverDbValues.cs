using System.Data.Common;

namespace NanoWeaverHrt.Database.Access;

/// <summary>
/// Reader-side coercions for column types that differ between engines: PostgreSQL returns native
/// booleans and timestamps, SQLite returns integers and text.
/// </summary>
public static class WeaverDbValues
{
    /// <summary>
    /// Reads a boolean column from either engine.
    /// </summary>
    /// <param name="reader">A reader positioned on a row.</param>
    /// <param name="ordinal">The zero-based column index.</param>
    /// <returns>The boolean value of the column.</returns>
    public static bool ReadBoolean(DbDataReader reader, int ordinal) =>
        reader.GetFieldType(ordinal) == typeof(bool) ? reader.GetBoolean(ordinal) : reader.GetInt64(ordinal) != 0;

    /// <summary>
    /// Reads a timestamp column from either engine.
    /// </summary>
    /// <param name="reader">A reader positioned on a row.</param>
    /// <param name="ordinal">The zero-based column index.</param>
    /// <returns>The column value as a timestamp anchored to UTC.</returns>
    public static DateTimeOffset ReadTimestamp(DbDataReader reader, int ordinal) =>
        reader.GetFieldType(ordinal) == typeof(DateTime)
            ? new DateTimeOffset(reader.GetDateTime(ordinal), TimeSpan.Zero)
            : DateTimeOffset.Parse(reader.GetString(ordinal));

    /// <summary>
    /// Reads a timestamp column that may be NULL.
    /// </summary>
    /// <param name="reader">A reader positioned on a row.</param>
    /// <param name="ordinal">The zero-based column index.</param>
    /// <returns>The timestamp, or null when the column is NULL.</returns>
    public static DateTimeOffset? ReadNullableTimestamp(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadTimestamp(reader, ordinal);
}
