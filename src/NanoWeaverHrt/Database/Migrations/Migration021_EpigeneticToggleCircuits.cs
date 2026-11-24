namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 021: Establishes epigenetic steroidogenic toggle circuit persistence (<c>weaver_epigenetic_circuits</c>)
/// and guide-RNA target locus specificity records (<c>weaver_epigenetic_guide_rnas</c>) across
/// SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB database engines.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Relational schema evolution storing patient-scoped non-nuclease dCas9 steroidogenic
/// toggle circuit designs, separatrix stability metrics, ML-DSA-65 approval metadata, and individual sgRNA targets.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 42 (P220): Enables durable storage, independent off-target auditing, and
/// GDPR Article 17 cascading patient erasure across all four relational database backends.
/// </para>
/// <para>
/// <strong>How it works:</strong> Emits dialect-specific DDL for PostgreSQL, DuckDB (omitting unsupported
/// <c>ON DELETE CASCADE</c>), MySQL/MariaDB (named foreign key constraint and non-IF-NOT-EXISTS indexes), and SQLite.
/// </para>
/// </remarks>
public sealed class Migration021_EpigeneticToggleCircuits : DatabaseMigrationBase
{
    /// <inheritdoc/>
    public override long Version => 21;

    /// <inheritdoc/>
    public override string Name => "021_EpigeneticToggleCircuits";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits dialect-specific DDL creating weaver_epigenetic_circuits, weaver_epigenetic_guide_rnas, and 3 indexes.
        // WHY:  Supports deterministic epigenetic toggle circuit and guide-RNA persistence across all 4 SQL backends.
        // HOW:  Branches on dialect prefix for PostgreSQL, DuckDB, MySQL/MariaDB, and SQLite default.
        ArgumentNullException.ThrowIfNull(dialect);

        if (dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_epigenetic_circuits (
    circuit_id                     VARCHAR(64)      PRIMARY KEY,
    patient_id                     VARCHAR(128)     NOT NULL,
    regimen                        VARCHAR(48)      NOT NULL,
    target_tissue                  VARCHAR(64)      NOT NULL,
    effector_platform              VARCHAR(64)      NOT NULL,
    circuit_status                 VARCHAR(32)      NOT NULL,
    predicted_ttr_percent          DOUBLE PRECISION NOT NULL,
    separatrix_margin              DOUBLE PRECISION NOT NULL,
    min_offtarget_specificity_score DOUBLE PRECISION NOT NULL,
    notes_encrypted                TEXT             NULL,
    created_at_utc                 TIMESTAMPTZ      NOT NULL,
    approved_at_utc                TIMESTAMPTZ      NULL,
    approval_payload_sha256        VARCHAR(128)     NULL,
    approval_signature_base64      TEXT             NULL,
    approval_signer_key_id         VARCHAR(128)     NULL
);

CREATE TABLE IF NOT EXISTS weaver_epigenetic_guide_rnas (
    guide_id               VARCHAR(64)      PRIMARY KEY,
    circuit_id             VARCHAR(64)      NOT NULL REFERENCES weaver_epigenetic_circuits(circuit_id) ON DELETE CASCADE,
    patient_id             VARCHAR(128)     NOT NULL,
    target_gene_symbol     VARCHAR(64)      NOT NULL,
    modulation_mode        VARCHAR(32)      NOT NULL,
    protospacer_sequence   VARCHAR(64)      NOT NULL,
    pam_sequence           VARCHAR(16)      NOT NULL,
    cfd_specificity_score  DOUBLE PRECISION NOT NULL,
    predicted_fold_change  DOUBLE PRECISION NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_weaver_epigenetic_circuits_patient ON weaver_epigenetic_circuits (patient_id, created_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_epigenetic_guides_circuit ON weaver_epigenetic_guide_rnas (circuit_id, guide_id);
CREATE INDEX IF NOT EXISTS idx_weaver_epigenetic_guides_patient ON weaver_epigenetic_guide_rnas (patient_id);
";
        }

        if (dialect.StartsWith("duckdb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_epigenetic_circuits (
    circuit_id                     VARCHAR(64)      PRIMARY KEY,
    patient_id                     VARCHAR(128)     NOT NULL,
    regimen                        VARCHAR(48)      NOT NULL,
    target_tissue                  VARCHAR(64)      NOT NULL,
    effector_platform              VARCHAR(64)      NOT NULL,
    circuit_status                 VARCHAR(32)      NOT NULL,
    predicted_ttr_percent          DOUBLE PRECISION NOT NULL,
    separatrix_margin              DOUBLE PRECISION NOT NULL,
    min_offtarget_specificity_score DOUBLE PRECISION NOT NULL,
    notes_encrypted                TEXT             NULL,
    created_at_utc                 TIMESTAMP        NOT NULL,
    approved_at_utc                TIMESTAMP        NULL,
    approval_payload_sha256        VARCHAR(128)     NULL,
    approval_signature_base64      TEXT             NULL,
    approval_signer_key_id         VARCHAR(128)     NULL
);

CREATE TABLE IF NOT EXISTS weaver_epigenetic_guide_rnas (
    guide_id               VARCHAR(64)      PRIMARY KEY,
    circuit_id             VARCHAR(64)      NOT NULL REFERENCES weaver_epigenetic_circuits(circuit_id),
    patient_id             VARCHAR(128)     NOT NULL,
    target_gene_symbol     VARCHAR(64)      NOT NULL,
    modulation_mode        VARCHAR(32)      NOT NULL,
    protospacer_sequence   VARCHAR(64)      NOT NULL,
    pam_sequence           VARCHAR(16)      NOT NULL,
    cfd_specificity_score  DOUBLE PRECISION NOT NULL,
    predicted_fold_change  DOUBLE PRECISION NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_weaver_epigenetic_circuits_patient ON weaver_epigenetic_circuits (patient_id, created_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_epigenetic_guides_circuit ON weaver_epigenetic_guide_rnas (circuit_id, guide_id);
CREATE INDEX IF NOT EXISTS idx_weaver_epigenetic_guides_patient ON weaver_epigenetic_guide_rnas (patient_id);
";
        }

        if (dialect.StartsWith("mysql", StringComparison.OrdinalIgnoreCase) || dialect.StartsWith("mariadb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_epigenetic_circuits (
    circuit_id                     VARCHAR(64)  NOT NULL PRIMARY KEY,
    patient_id                     VARCHAR(128) NOT NULL,
    regimen                        VARCHAR(48)  NOT NULL,
    target_tissue                  VARCHAR(64)  NOT NULL,
    effector_platform              VARCHAR(64)  NOT NULL,
    circuit_status                 VARCHAR(32)  NOT NULL,
    predicted_ttr_percent          DOUBLE       NOT NULL,
    separatrix_margin              DOUBLE       NOT NULL,
    min_offtarget_specificity_score DOUBLE      NOT NULL,
    notes_encrypted                TEXT         NULL,
    created_at_utc                 DATETIME(6)  NOT NULL,
    approved_at_utc                DATETIME(6)  NULL,
    approval_payload_sha256        VARCHAR(128) NULL,
    approval_signature_base64      TEXT         NULL,
    approval_signer_key_id         VARCHAR(128) NULL
);

CREATE TABLE IF NOT EXISTS weaver_epigenetic_guide_rnas (
    guide_id               VARCHAR(64)  NOT NULL PRIMARY KEY,
    circuit_id             VARCHAR(64)  NOT NULL,
    patient_id             VARCHAR(128) NOT NULL,
    target_gene_symbol     VARCHAR(64)  NOT NULL,
    modulation_mode        VARCHAR(32)  NOT NULL,
    protospacer_sequence   VARCHAR(64)  NOT NULL,
    pam_sequence           VARCHAR(16)  NOT NULL,
    cfd_specificity_score  DOUBLE       NOT NULL,
    predicted_fold_change  DOUBLE       NOT NULL,
    CONSTRAINT fk_weaver_epigenetic_guides_circuit FOREIGN KEY (circuit_id) REFERENCES weaver_epigenetic_circuits(circuit_id) ON DELETE CASCADE
);

CREATE INDEX idx_weaver_epigenetic_circuits_patient ON weaver_epigenetic_circuits (patient_id, created_at_utc);
CREATE INDEX idx_weaver_epigenetic_guides_circuit ON weaver_epigenetic_guide_rnas (circuit_id, guide_id);
CREATE INDEX idx_weaver_epigenetic_guides_patient ON weaver_epigenetic_guide_rnas (patient_id);
";
        }

        return @"
CREATE TABLE IF NOT EXISTS weaver_epigenetic_circuits (
    circuit_id                     TEXT PRIMARY KEY,
    patient_id                     TEXT NOT NULL,
    regimen                        TEXT NOT NULL,
    target_tissue                  TEXT NOT NULL,
    effector_platform              TEXT NOT NULL,
    circuit_status                 TEXT NOT NULL,
    predicted_ttr_percent          REAL NOT NULL,
    separatrix_margin              REAL NOT NULL,
    min_offtarget_specificity_score REAL NOT NULL,
    notes_encrypted                TEXT NULL,
    created_at_utc                 TEXT NOT NULL,
    approved_at_utc                TEXT NULL,
    approval_payload_sha256        TEXT NULL,
    approval_signature_base64      TEXT NULL,
    approval_signer_key_id         TEXT NULL
);

CREATE TABLE IF NOT EXISTS weaver_epigenetic_guide_rnas (
    guide_id               TEXT PRIMARY KEY,
    circuit_id             TEXT NOT NULL REFERENCES weaver_epigenetic_circuits(circuit_id) ON DELETE CASCADE,
    patient_id             TEXT NOT NULL,
    target_gene_symbol     TEXT NOT NULL,
    modulation_mode        TEXT NOT NULL,
    protospacer_sequence   TEXT NOT NULL,
    pam_sequence           TEXT NOT NULL,
    cfd_specificity_score  REAL NOT NULL,
    predicted_fold_change  REAL NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_weaver_epigenetic_circuits_patient ON weaver_epigenetic_circuits (patient_id, created_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_epigenetic_guides_circuit ON weaver_epigenetic_guide_rnas (circuit_id, guide_id);
CREATE INDEX IF NOT EXISTS idx_weaver_epigenetic_guides_patient ON weaver_epigenetic_guide_rnas (patient_id);
";
    }
}
