namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 017: Establishes the federated peer lot revocation ledger (<c>weaver_federated_lot_revocations</c>)
/// across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB database engines.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The relational schema table persisting cryptographically verified ML-DSA-65
/// lot recall tombstones imported from federated peer laboratories.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Enables receiving sovereign clinics to enforce upstream lot recalls fail-closed
/// during cross-node Certificate of Analysis verification even when the recalled lot was never fabricated locally.
/// </para>
/// <para>
/// <strong>How it works:</strong> Emits dialect-specific DDL for PostgreSQL, DuckDB, MySQL/MariaDB, and SQLite
/// storing lot ID, issuer signing key ID, peer laboratory label, recall reason, revoking actor, feed payload hash,
/// and ML-DSA-65 feed signature with indexes on <c>issuer_key_id</c> and <c>imported_at_utc</c>.
/// </para>
/// </remarks>
public sealed class Migration017_FederatedRevocationFeed : DatabaseMigrationBase
{
    /// <inheritdoc/>
    public override long Version => 17;

    /// <inheritdoc/>
    public override string Name => "017_FederatedRevocationFeed";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits dialect-specific DDL creating weaver_federated_lot_revocations and its query indexes.
        // WHY:  Persists verified upstream peer recall feeds across all four relational database backends.
        // HOW:  Selects engine-appropriate VARCHAR/TEXT column types and index syntax by dialect prefix.
        ArgumentNullException.ThrowIfNull(dialect);

        if (dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_federated_lot_revocations (
    lot_id                  VARCHAR(64)  PRIMARY KEY,
    payload_sha256          VARCHAR(64)  NULL,
    issuer_key_id           VARCHAR(128) NOT NULL,
    issuer_peer_label       VARCHAR(160) NULL,
    revoked_by              VARCHAR(128) NOT NULL,
    revocation_reason       TEXT         NOT NULL,
    revoked_at_utc          VARCHAR(64)  NOT NULL,
    feed_payload_sha256     VARCHAR(64)  NOT NULL,
    feed_signature_base64   TEXT         NOT NULL,
    imported_at_utc         VARCHAR(64)  NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_fed_rev_issuer_key ON weaver_federated_lot_revocations (issuer_key_id);
CREATE INDEX IF NOT EXISTS idx_fed_rev_imported_at ON weaver_federated_lot_revocations (imported_at_utc);
";
        }

        if (dialect.StartsWith("duckdb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_federated_lot_revocations (
    lot_id                  VARCHAR(64)  PRIMARY KEY,
    payload_sha256          VARCHAR(64)  NULL,
    issuer_key_id           VARCHAR(128) NOT NULL,
    issuer_peer_label       VARCHAR(160) NULL,
    revoked_by              VARCHAR(128) NOT NULL,
    revocation_reason       TEXT         NOT NULL,
    revoked_at_utc          VARCHAR(64)  NOT NULL,
    feed_payload_sha256     VARCHAR(64)  NOT NULL,
    feed_signature_base64   TEXT         NOT NULL,
    imported_at_utc         VARCHAR(64)  NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_fed_rev_issuer_key ON weaver_federated_lot_revocations (issuer_key_id);
CREATE INDEX IF NOT EXISTS idx_fed_rev_imported_at ON weaver_federated_lot_revocations (imported_at_utc);
";
        }

        if (dialect.StartsWith("mysql", StringComparison.OrdinalIgnoreCase) || dialect.StartsWith("mariadb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_federated_lot_revocations (
    lot_id                  VARCHAR(64)  PRIMARY KEY,
    payload_sha256          VARCHAR(64)  NULL,
    issuer_key_id           VARCHAR(128) NOT NULL,
    issuer_peer_label       VARCHAR(160) NULL,
    revoked_by              VARCHAR(128) NOT NULL,
    revocation_reason       TEXT         NOT NULL,
    revoked_at_utc          VARCHAR(64)  NOT NULL,
    feed_payload_sha256     VARCHAR(64)  NOT NULL,
    feed_signature_base64   TEXT         NOT NULL,
    imported_at_utc         VARCHAR(64)  NOT NULL
);

CREATE INDEX idx_fed_rev_issuer_key ON weaver_federated_lot_revocations (issuer_key_id);
CREATE INDEX idx_fed_rev_imported_at ON weaver_federated_lot_revocations (imported_at_utc);
";
        }

        return @"
CREATE TABLE IF NOT EXISTS weaver_federated_lot_revocations (
    lot_id                  TEXT PRIMARY KEY,
    payload_sha256          TEXT NULL,
    issuer_key_id           TEXT NOT NULL,
    issuer_peer_label       TEXT NULL,
    revoked_by              TEXT NOT NULL,
    revocation_reason       TEXT NOT NULL,
    revoked_at_utc          TEXT NOT NULL,
    feed_payload_sha256     TEXT NOT NULL,
    feed_signature_base64   TEXT NOT NULL,
    imported_at_utc         TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_fed_rev_issuer_key ON weaver_federated_lot_revocations (issuer_key_id);
CREATE INDEX IF NOT EXISTS idx_fed_rev_imported_at ON weaver_federated_lot_revocations (imported_at_utc);
";
    }
}
