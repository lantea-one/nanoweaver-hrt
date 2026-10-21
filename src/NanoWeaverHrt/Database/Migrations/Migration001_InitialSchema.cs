namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 001: Establishes base tables for system configuration, cryptographic share keys, and sovereign user accounts.
/// </summary>
public sealed class Migration001_InitialSchema : DatabaseMigrationBase
{
    /// <inheritdoc/>
    public override long Version => 1;

    /// <inheritdoc/>
    public override string Name => "001_InitialSchema";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits table creation DDL matching PostgreSQL or SQLite dialects.
        // WHY:  Bootstraps required schema with correct data types for each relational database engine.
        // HOW:  Returns dialect-tailored SQL scripts with idempotent CREATE TABLE IF NOT EXISTS clauses.
        if (dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return @"
                CREATE TABLE IF NOT EXISTS weaver_configuration (
                    key VARCHAR(128) PRIMARY KEY,
                    value TEXT NOT NULL,
                    description TEXT,
                    updated_at_utc TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC')
                );

                CREATE TABLE IF NOT EXISTS weaver_share_keys (
                    token_id VARCHAR(64) PRIMARY KEY,
                    key_token VARCHAR(128) NOT NULL UNIQUE,
                    label VARCHAR(256) NOT NULL,
                    created_at_utc TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
                    expires_at_utc TIMESTAMP NOT NULL,
                    is_consumed BOOLEAN NOT NULL DEFAULT FALSE,
                    consumed_at_utc TIMESTAMP,
                    remote_peer_info TEXT,
                    permissions VARCHAR(64) NOT NULL
                );

                CREATE TABLE IF NOT EXISTS weaver_users (
                    username VARCHAR(128) PRIMARY KEY,
                    role VARCHAR(64) NOT NULL,
                    access_gate VARCHAR(64) NOT NULL,
                    key_algorithm VARCHAR(64) NOT NULL,
                    public_key TEXT NOT NULL,
                    created_at_utc TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
                    last_login_utc TIMESTAMP,
                    is_active BOOLEAN NOT NULL DEFAULT TRUE
                );
            ";
        }

        return @"
            CREATE TABLE IF NOT EXISTS weaver_configuration (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                description TEXT,
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS weaver_share_keys (
                token_id TEXT PRIMARY KEY,
                key_token TEXT NOT NULL UNIQUE,
                label TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                expires_at_utc TEXT NOT NULL,
                is_consumed INTEGER NOT NULL DEFAULT 0,
                consumed_at_utc TEXT,
                remote_peer_info TEXT,
                permissions TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS weaver_users (
                username TEXT PRIMARY KEY,
                role TEXT NOT NULL,
                access_gate TEXT NOT NULL,
                key_algorithm TEXT NOT NULL,
                public_key TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                last_login_utc TEXT,
                is_active INTEGER NOT NULL DEFAULT 1
            );
        ";
    }
}
