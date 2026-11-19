namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 016: Establishes the persistent lot certificate revocation ledger (<c>weaver_fabrication_lot_revocations</c>)
/// across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB database engines.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The relational schema table tracking permanently revoked wet-lab fabrication lot
/// certificates, including the revoking administrator, plain-English clinical recall reason, payload SHA-256 digest,
/// and UTC timestamp.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Guarantees fail-closed post-release recall enforcement across local and federated
/// cross-node Certificate of Analysis verifications without mutating historical signed audit logs.
/// </para>
/// <para>
/// <strong>How it works:</strong> Emits dialect-specific DDL for PostgreSQL, DuckDB, MySQL/MariaDB, and SQLite
/// with foreign key linkage to <c>weaver_fabrication_lots</c> and an index on <c>patient_id</c> for GDPR erasure.
/// </para>
/// </remarks>
public sealed class Migration016_FabricationLotRevocation : DatabaseMigrationBase
{
    /// <inheritdoc/>
    public override long Version => 16;

    /// <inheritdoc/>
    public override string Name => "016_FabricationLotRevocation";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits dialect-specific DDL creating weaver_fabrication_lot_revocations and its patient index.
        // WHY:  Supports permanent certificate recall tracking across all four relational database backends.
        // HOW:  Selects engine-appropriate VARCHAR/TEXT types and foreign-key cascade clauses by dialect prefix.
        ArgumentNullException.ThrowIfNull(dialect);

        if (dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_fabrication_lot_revocations (
    lot_id            VARCHAR(64)  PRIMARY KEY REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE,
    patient_id        VARCHAR(128) NOT NULL,
    payload_sha256    VARCHAR(64)  NULL,
    revoked_by        VARCHAR(128) NOT NULL,
    revocation_reason TEXT         NOT NULL,
    revoked_at_utc    VARCHAR(64)  NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_fab_rev_patient_id ON weaver_fabrication_lot_revocations (patient_id);
";
        }

        if (dialect.StartsWith("duckdb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_fabrication_lot_revocations (
    lot_id            VARCHAR(64)  PRIMARY KEY REFERENCES weaver_fabrication_lots(lot_id),
    patient_id        VARCHAR(128) NOT NULL,
    payload_sha256    VARCHAR(64)  NULL,
    revoked_by        VARCHAR(128) NOT NULL,
    revocation_reason TEXT         NOT NULL,
    revoked_at_utc    VARCHAR(64)  NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_fab_rev_patient_id ON weaver_fabrication_lot_revocations (patient_id);
";
        }

        if (dialect.StartsWith("mysql", StringComparison.OrdinalIgnoreCase) || dialect.StartsWith("mariadb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_fabrication_lot_revocations (
    lot_id            VARCHAR(64)  PRIMARY KEY,
    patient_id        VARCHAR(128) NOT NULL,
    payload_sha256    VARCHAR(64)  NULL,
    revoked_by        VARCHAR(128) NOT NULL,
    revocation_reason TEXT         NOT NULL,
    revoked_at_utc    VARCHAR(64)  NOT NULL,
    FOREIGN KEY (lot_id) REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE
);

CREATE INDEX idx_fab_rev_patient_id ON weaver_fabrication_lot_revocations (patient_id);
";
        }

        return @"
CREATE TABLE IF NOT EXISTS weaver_fabrication_lot_revocations (
    lot_id            TEXT PRIMARY KEY REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE,
    patient_id        TEXT NOT NULL,
    payload_sha256    TEXT NULL,
    revoked_by        TEXT NOT NULL,
    revocation_reason TEXT NOT NULL,
    revoked_at_utc    TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_fab_rev_patient_id ON weaver_fabrication_lot_revocations (patient_id);
";
    }
}
