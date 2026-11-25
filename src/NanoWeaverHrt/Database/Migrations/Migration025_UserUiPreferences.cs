namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 025: Establishes per-user Web UI display mode persistence (<c>weaver_user_preferences</c>)
/// across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB database engines.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Relational schema table keyed by normalized username storing each account's
/// preferred display density (<c>Simple</c> or <c>Advanced</c>).
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 47 (P245): Persists individual users' progressive disclosure preferences
/// server-side across browser sessions and devices while falling back to node defaults for new users.
/// </para>
/// <para>
/// <strong>How it works:</strong> Emits dialect-specific DDL for PostgreSQL (<c>TIMESTAMPTZ</c>), DuckDB (<c>TIMESTAMP</c>),
/// MySQL/MariaDB (<c>DATETIME(6)</c>), and SQLite (<c>TEXT</c>).
/// </para>
/// </remarks>
public sealed class Migration025_UserUiPreferences : DatabaseMigrationBase
{
    /// <inheritdoc/>
    public override long Version => 25;

    /// <inheritdoc/>
    public override string Name => "025_UserUiPreferences";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits dialect-specific DDL creating weaver_user_preferences.
        // WHY:  Provides durable per-user Simple/Advanced UI preference persistence across all 4 supported SQL backends.
        // HOW:  Selects appropriate string and timestamp column types based on dialect prefix, defaulting to SQLite.
        ArgumentNullException.ThrowIfNull(dialect);

        if (dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_user_preferences (
    username        VARCHAR(128) PRIMARY KEY,
    display_mode    VARCHAR(32)  NOT NULL,
    updated_at_utc  TIMESTAMPTZ  NOT NULL
);
";
        }

        if (dialect.StartsWith("duckdb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_user_preferences (
    username        VARCHAR(128) PRIMARY KEY,
    display_mode    VARCHAR(32)  NOT NULL,
    updated_at_utc  TIMESTAMP    NOT NULL
);
";
        }

        if (dialect.StartsWith("mysql", StringComparison.OrdinalIgnoreCase) || dialect.StartsWith("mariadb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_user_preferences (
    username        VARCHAR(128) NOT NULL PRIMARY KEY,
    display_mode    VARCHAR(32)  NOT NULL,
    updated_at_utc  DATETIME(6)  NOT NULL
);
";
        }

        return @"
CREATE TABLE IF NOT EXISTS weaver_user_preferences (
    username        TEXT PRIMARY KEY,
    display_mode    TEXT NOT NULL,
    updated_at_utc  TEXT NOT NULL
);
";
    }
}
