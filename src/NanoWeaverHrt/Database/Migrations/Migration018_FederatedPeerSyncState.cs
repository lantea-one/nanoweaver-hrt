namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 018: Establishes the automated federated peer revocation pull-sync endpoint registry
/// (<c>weaver_federated_peer_sync_endpoints</c>) across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The cluster-level relational table storing subscribed federated peer laboratory
/// base URLs, pinned ML-DSA-65 key identifiers, polling cadence, loopback opt-in policy, and sync telemetry.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 39 (P205): Allows sovereign clinics to automate periodic pull-sync of
/// signed ML-DSA-65 lot revocation feeds across all four supported relational database backends without storing PHI.
/// </para>
/// <para>
/// <strong>How it works:</strong> Emits dialect-specific DDL for PostgreSQL, DuckDB, MySQL/MariaDB, and SQLite
/// with secondary indexes on <c>(enabled, last_attempt_utc)</c> and <c>(pinned_key_id)</c>.
/// </para>
/// </remarks>
public sealed class Migration018_FederatedPeerSyncState : DatabaseMigrationBase
{
    /// <inheritdoc/>
    public override long Version => 18;

    /// <inheritdoc/>
    public override string Name => "018_FederatedPeerSyncState";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits dialect-specific DDL creating weaver_federated_peer_sync_endpoints and its query indexes.
        // WHY:  Persists automated peer revocation pull-sync subscriptions and telemetry across all 4 SQL engines.
        // HOW:  Selects engine-appropriate boolean, timestamp, and index syntax by dialect prefix.
        ArgumentNullException.ThrowIfNull(dialect);

        if (dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_federated_peer_sync_endpoints (
    peer_id               VARCHAR(64)  PRIMARY KEY,
    display_name          VARCHAR(160) NOT NULL,
    base_url              VARCHAR(512) NOT NULL,
    pinned_key_id         VARCHAR(128) NOT NULL,
    enabled               BOOLEAN      NOT NULL DEFAULT TRUE,
    sync_interval_minutes INTEGER      NOT NULL DEFAULT 15,
    allow_loopback_http   BOOLEAN      NOT NULL DEFAULT FALSE,
    last_attempt_utc      TIMESTAMPTZ  NULL,
    last_success_utc      TIMESTAMPTZ  NULL,
    last_status           VARCHAR(32)  NOT NULL,
    last_error            TEXT         NULL,
    last_imported_count   INTEGER      NOT NULL DEFAULT 0,
    total_imported_count  INTEGER      NOT NULL DEFAULT 0,
    created_at_utc        TIMESTAMPTZ  NOT NULL,
    updated_at_utc        TIMESTAMPTZ  NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_fed_sync_enabled ON weaver_federated_peer_sync_endpoints (enabled, last_attempt_utc);
CREATE INDEX IF NOT EXISTS idx_fed_sync_pinned_key ON weaver_federated_peer_sync_endpoints (pinned_key_id);
";
        }

        if (dialect.StartsWith("duckdb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_federated_peer_sync_endpoints (
    peer_id               VARCHAR(64)  PRIMARY KEY,
    display_name          VARCHAR(160) NOT NULL,
    base_url              VARCHAR(512) NOT NULL,
    pinned_key_id         VARCHAR(128) NOT NULL,
    enabled               BOOLEAN      NOT NULL DEFAULT TRUE,
    sync_interval_minutes INTEGER      NOT NULL DEFAULT 15,
    allow_loopback_http   BOOLEAN      NOT NULL DEFAULT FALSE,
    last_attempt_utc      TIMESTAMP    NULL,
    last_success_utc      TIMESTAMP    NULL,
    last_status           VARCHAR(32)  NOT NULL,
    last_error            TEXT         NULL,
    last_imported_count   INTEGER      NOT NULL DEFAULT 0,
    total_imported_count  INTEGER      NOT NULL DEFAULT 0,
    created_at_utc        TIMESTAMP    NOT NULL,
    updated_at_utc        TIMESTAMP    NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_fed_sync_enabled ON weaver_federated_peer_sync_endpoints (enabled, last_attempt_utc);
CREATE INDEX IF NOT EXISTS idx_fed_sync_pinned_key ON weaver_federated_peer_sync_endpoints (pinned_key_id);
";
        }

        if (dialect.StartsWith("mysql", StringComparison.OrdinalIgnoreCase) || dialect.StartsWith("mariadb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_federated_peer_sync_endpoints (
    peer_id               VARCHAR(64)  PRIMARY KEY,
    display_name          VARCHAR(160) NOT NULL,
    base_url              VARCHAR(512) NOT NULL,
    pinned_key_id         VARCHAR(128) NOT NULL,
    enabled               TINYINT(1)   NOT NULL DEFAULT 1,
    sync_interval_minutes INTEGER      NOT NULL DEFAULT 15,
    allow_loopback_http   TINYINT(1)   NOT NULL DEFAULT 0,
    last_attempt_utc      DATETIME(6)  NULL,
    last_success_utc      DATETIME(6)  NULL,
    last_status           VARCHAR(32)  NOT NULL,
    last_error            TEXT         NULL,
    last_imported_count   INTEGER      NOT NULL DEFAULT 0,
    total_imported_count  INTEGER      NOT NULL DEFAULT 0,
    created_at_utc        DATETIME(6)  NOT NULL,
    updated_at_utc        DATETIME(6)  NOT NULL
);

CREATE INDEX idx_fed_sync_enabled ON weaver_federated_peer_sync_endpoints (enabled, last_attempt_utc);
CREATE INDEX idx_fed_sync_pinned_key ON weaver_federated_peer_sync_endpoints (pinned_key_id);
";
        }

        return @"
CREATE TABLE IF NOT EXISTS weaver_federated_peer_sync_endpoints (
    peer_id               TEXT    PRIMARY KEY,
    display_name          TEXT    NOT NULL,
    base_url              TEXT    NOT NULL,
    pinned_key_id         TEXT    NOT NULL,
    enabled               INTEGER NOT NULL DEFAULT 1,
    sync_interval_minutes INTEGER NOT NULL DEFAULT 15,
    allow_loopback_http   INTEGER NOT NULL DEFAULT 0,
    last_attempt_utc      TEXT    NULL,
    last_success_utc      TEXT    NULL,
    last_status           TEXT    NOT NULL,
    last_error            TEXT    NULL,
    last_imported_count   INTEGER NOT NULL DEFAULT 0,
    total_imported_count  INTEGER NOT NULL DEFAULT 0,
    created_at_utc        TEXT    NOT NULL,
    updated_at_utc        TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_fed_sync_enabled ON weaver_federated_peer_sync_endpoints (enabled, last_attempt_utc);
CREATE INDEX IF NOT EXISTS idx_fed_sync_pinned_key ON weaver_federated_peer_sync_endpoints (pinned_key_id);
";
    }
}
