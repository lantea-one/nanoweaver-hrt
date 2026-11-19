namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 015: Establishes database tables and indexes for patient-specific wet-lab nanocarrier
/// fabrication lots and 5-gate pharmaceutical quality control measurements across SQLite, PostgreSQL,
/// DuckDB, and MySQL/MariaDB engines.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The relational schema tables (<c>weaver_fabrication_lots</c> and
/// <c>weaver_fabrication_qc_checks</c>) storing wet-lab batch specifications, Opentrons protocol SHA-256 digests,
/// 5-gate QC measurements, and NIST FIPS 204 ML-DSA-65 Certificate of Analysis signature payloads.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Provides durable, multi-engine chain-of-custody tracking from adaptive
/// semi-annual depot formulations to physically fabricated, cryptographically certified patient lots.
/// </para>
/// <para>
/// <strong>How it works:</strong> Emits dialect-specific DDL for PostgreSQL, DuckDB, MySQL/MariaDB, and SQLite
/// with foreign key linkage to <c>weaver_depot_formulations</c> and indexes on patient, depot, and lot identifiers.
/// </para>
/// </remarks>
public sealed class Migration015_FabricationLotLedger : DatabaseMigrationBase
{
    /// <inheritdoc/>
    public override long Version => 15;

    /// <inheritdoc/>
    public override string Name => "015_FabricationLotLedger";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits dialect-specific DDL creating weaver_fabrication_lots and weaver_fabrication_qc_checks.
        // WHY:  Guarantees deterministic schema evolution across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB.
        // HOW:  Selects VARCHAR/DOUBLE PRECISION on PostgreSQL and DuckDB, DOUBLE on MySQL/MariaDB, and TEXT/REAL on SQLite.
        ArgumentNullException.ThrowIfNull(dialect);

        if (dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_fabrication_lots (
    lot_id                       VARCHAR(64)      PRIMARY KEY,
    depot_id                     VARCHAR(64)      NOT NULL REFERENCES weaver_depot_formulations(depot_id) ON DELETE CASCADE,
    calibration_snapshot_id      VARCHAR(64)      NULL,
    patient_id                   VARCHAR(128)     NOT NULL,
    regimen                      VARCHAR(32)      NOT NULL,
    polymer_matrix_type          VARCHAR(64)      NOT NULL,
    target_mass_mg               DOUBLE PRECISION NOT NULL,
    target_erosion_rate_percent  DOUBLE PRECISION NOT NULL,
    cpg_methylation_required     INTEGER          NOT NULL DEFAULT 1,
    opentrons_protocol_sha256    VARCHAR(64)      NOT NULL,
    lot_status                   VARCHAR(32)      NOT NULL,
    certificate_payload_sha256   VARCHAR(64)      NULL,
    certificate_signature_base64 TEXT             NULL,
    certificate_signer_key_id    VARCHAR(128)     NULL,
    created_at_utc               VARCHAR(64)      NOT NULL,
    released_at_utc              VARCHAR(64)      NULL
);

CREATE INDEX IF NOT EXISTS idx_fab_lot_patient_id ON weaver_fabrication_lots (patient_id);
CREATE INDEX IF NOT EXISTS idx_fab_lot_depot_id ON weaver_fabrication_lots (depot_id);

CREATE TABLE IF NOT EXISTS weaver_fabrication_qc_checks (
    check_id        VARCHAR(64)      PRIMARY KEY,
    lot_id          VARCHAR(64)      NOT NULL REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE,
    patient_id      VARCHAR(128)     NOT NULL,
    gate_type       VARCHAR(48)      NOT NULL,
    measured_value  DOUBLE PRECISION NOT NULL,
    secondary_value DOUBLE PRECISION NULL,
    threshold_value DOUBLE PRECISION NOT NULL,
    passed          INTEGER          NOT NULL,
    recorded_at_utc VARCHAR(64)      NOT NULL,
    notes_encrypted TEXT             NULL
);

CREATE INDEX IF NOT EXISTS idx_fab_qc_lot_id ON weaver_fabrication_qc_checks (lot_id);
CREATE INDEX IF NOT EXISTS idx_fab_qc_patient_id ON weaver_fabrication_qc_checks (patient_id);
";
        }

        if (dialect.StartsWith("duckdb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_fabrication_lots (
    lot_id                       VARCHAR(64)      PRIMARY KEY,
    depot_id                     VARCHAR(64)      NOT NULL REFERENCES weaver_depot_formulations(depot_id),
    calibration_snapshot_id      VARCHAR(64)      NULL,
    patient_id                   VARCHAR(128)     NOT NULL,
    regimen                      VARCHAR(32)      NOT NULL,
    polymer_matrix_type          VARCHAR(64)      NOT NULL,
    target_mass_mg               DOUBLE PRECISION NOT NULL,
    target_erosion_rate_percent  DOUBLE PRECISION NOT NULL,
    cpg_methylation_required     INTEGER          NOT NULL DEFAULT 1,
    opentrons_protocol_sha256    VARCHAR(64)      NOT NULL,
    lot_status                   VARCHAR(32)      NOT NULL,
    certificate_payload_sha256   VARCHAR(64)      NULL,
    certificate_signature_base64 TEXT             NULL,
    certificate_signer_key_id    VARCHAR(128)     NULL,
    created_at_utc               VARCHAR(64)      NOT NULL,
    released_at_utc              VARCHAR(64)      NULL
);

CREATE INDEX IF NOT EXISTS idx_fab_lot_patient_id ON weaver_fabrication_lots (patient_id);
CREATE INDEX IF NOT EXISTS idx_fab_lot_depot_id ON weaver_fabrication_lots (depot_id);

CREATE TABLE IF NOT EXISTS weaver_fabrication_qc_checks (
    check_id        VARCHAR(64)      PRIMARY KEY,
    lot_id          VARCHAR(64)      NOT NULL REFERENCES weaver_fabrication_lots(lot_id),
    patient_id      VARCHAR(128)     NOT NULL,
    gate_type       VARCHAR(48)      NOT NULL,
    measured_value  DOUBLE PRECISION NOT NULL,
    secondary_value DOUBLE PRECISION NULL,
    threshold_value DOUBLE PRECISION NOT NULL,
    passed          INTEGER          NOT NULL,
    recorded_at_utc VARCHAR(64)      NOT NULL,
    notes_encrypted TEXT             NULL
);

CREATE INDEX IF NOT EXISTS idx_fab_qc_lot_id ON weaver_fabrication_qc_checks (lot_id);
CREATE INDEX IF NOT EXISTS idx_fab_qc_patient_id ON weaver_fabrication_qc_checks (patient_id);
";
        }

        if (dialect.StartsWith("mysql", StringComparison.OrdinalIgnoreCase) || dialect.StartsWith("mariadb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_fabrication_lots (
    lot_id                       VARCHAR(64)  PRIMARY KEY,
    depot_id                     VARCHAR(64)  NOT NULL,
    calibration_snapshot_id      VARCHAR(64)  NULL,
    patient_id                   VARCHAR(128) NOT NULL,
    regimen                      VARCHAR(32)  NOT NULL,
    polymer_matrix_type          VARCHAR(64)  NOT NULL,
    target_mass_mg               DOUBLE       NOT NULL,
    target_erosion_rate_percent  DOUBLE       NOT NULL,
    cpg_methylation_required     INTEGER      NOT NULL DEFAULT 1,
    opentrons_protocol_sha256    VARCHAR(64)  NOT NULL,
    lot_status                   VARCHAR(32)  NOT NULL,
    certificate_payload_sha256   VARCHAR(64)  NULL,
    certificate_signature_base64 TEXT         NULL,
    certificate_signer_key_id    VARCHAR(128) NULL,
    created_at_utc               VARCHAR(64)  NOT NULL,
    released_at_utc              VARCHAR(64)  NULL,
    FOREIGN KEY (depot_id) REFERENCES weaver_depot_formulations(depot_id) ON DELETE CASCADE
);

CREATE INDEX idx_fab_lot_patient_id ON weaver_fabrication_lots (patient_id);
CREATE INDEX idx_fab_lot_depot_id ON weaver_fabrication_lots (depot_id);

CREATE TABLE IF NOT EXISTS weaver_fabrication_qc_checks (
    check_id        VARCHAR(64)  PRIMARY KEY,
    lot_id          VARCHAR(64)  NOT NULL,
    patient_id      VARCHAR(128) NOT NULL,
    gate_type       VARCHAR(48)  NOT NULL,
    measured_value  DOUBLE       NOT NULL,
    secondary_value DOUBLE       NULL,
    threshold_value DOUBLE       NOT NULL,
    passed          INTEGER      NOT NULL,
    recorded_at_utc VARCHAR(64)  NOT NULL,
    notes_encrypted TEXT         NULL,
    FOREIGN KEY (lot_id) REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE
);

CREATE INDEX idx_fab_qc_lot_id ON weaver_fabrication_qc_checks (lot_id);
CREATE INDEX idx_fab_qc_patient_id ON weaver_fabrication_qc_checks (patient_id);
";
        }

        return @"
CREATE TABLE IF NOT EXISTS weaver_fabrication_lots (
    lot_id                       TEXT    PRIMARY KEY,
    depot_id                     TEXT    NOT NULL REFERENCES weaver_depot_formulations(depot_id) ON DELETE CASCADE,
    calibration_snapshot_id      TEXT    NULL,
    patient_id                   TEXT    NOT NULL,
    regimen                      TEXT    NOT NULL,
    polymer_matrix_type          TEXT    NOT NULL,
    target_mass_mg               REAL    NOT NULL,
    target_erosion_rate_percent  REAL    NOT NULL,
    cpg_methylation_required     INTEGER NOT NULL DEFAULT 1,
    opentrons_protocol_sha256    TEXT    NOT NULL,
    lot_status                   TEXT    NOT NULL,
    certificate_payload_sha256   TEXT    NULL,
    certificate_signature_base64 TEXT    NULL,
    certificate_signer_key_id    TEXT    NULL,
    created_at_utc               TEXT    NOT NULL,
    released_at_utc              TEXT    NULL
);

CREATE INDEX IF NOT EXISTS idx_fab_lot_patient_id ON weaver_fabrication_lots (patient_id);
CREATE INDEX IF NOT EXISTS idx_fab_lot_depot_id ON weaver_fabrication_lots (depot_id);

CREATE TABLE IF NOT EXISTS weaver_fabrication_qc_checks (
    check_id        TEXT    PRIMARY KEY,
    lot_id          TEXT    NOT NULL REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE,
    patient_id      TEXT    NOT NULL,
    gate_type       TEXT    NOT NULL,
    measured_value  REAL    NOT NULL,
    secondary_value REAL    NULL,
    threshold_value REAL    NOT NULL,
    passed          INTEGER NOT NULL,
    recorded_at_utc TEXT    NOT NULL,
    notes_encrypted TEXT    NULL
);

CREATE INDEX IF NOT EXISTS idx_fab_qc_lot_id ON weaver_fabrication_qc_checks (lot_id);
CREATE INDEX IF NOT EXISTS idx_fab_qc_patient_id ON weaver_fabrication_qc_checks (patient_id);
";
    }
}
