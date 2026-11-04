namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 012: Establishes persistent storage for GUI and localization preferences across SQLite, PostgreSQL, DuckDB, and MySQL engines.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The database schema table (<c>weaver_gui_preferences</c>) storing persistent
/// user interface preferences such as active language, theme, and locale settings.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Unifies preference storage across all four supported database backends
/// (SQLite, PostgreSQL, DuckDB, and MySQL / MariaDB) with consistent column names and constraints.
/// </para>
/// <para>
/// <strong>How it works:</strong> Creates <c>weaver_gui_preferences</c> with <c>preference_key</c>,
/// <c>preference_value</c>, and <c>updated_at_utc</c> using dialect-appropriate data types.
/// </para>
/// </remarks>
public sealed class Migration012_GuiPreferences : DatabaseMigrationBase
{
    /// <inheritdoc/>
    public override long Version => 12;

    /// <inheritdoc/>
    public override string Name => "012_GuiPreferences";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits dialect-specific DDL creating the weaver_gui_preferences table.
        // WHY:  Provides deterministic schema migration across SQLite, PostgreSQL, DuckDB, and MySQL backends.
        // HOW:  Uses VARCHAR and TEXT column types tailored to each database dialect.
        ArgumentNullException.ThrowIfNull(dialect);

        if (dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_gui_preferences (
    preference_key   VARCHAR(128) PRIMARY KEY,
    preference_value TEXT         NOT NULL,
    updated_at_utc   VARCHAR(64)  NOT NULL
);";
        }

        if (dialect.StartsWith("duckdb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_gui_preferences (
    preference_key   VARCHAR(128) PRIMARY KEY,
    preference_value TEXT         NOT NULL,
    updated_at_utc   VARCHAR(64)  NOT NULL
);";
        }

        if (dialect.StartsWith("mysql", StringComparison.OrdinalIgnoreCase) || dialect.StartsWith("mariadb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_gui_preferences (
    preference_key   VARCHAR(128) PRIMARY KEY,
    preference_value TEXT         NOT NULL,
    updated_at_utc   VARCHAR(64)  NOT NULL
);";
        }

        return @"
CREATE TABLE IF NOT EXISTS weaver_gui_preferences (
    preference_key   TEXT PRIMARY KEY,
    preference_value TEXT NOT NULL,
    updated_at_utc   TEXT NOT NULL
);";
    }
}
