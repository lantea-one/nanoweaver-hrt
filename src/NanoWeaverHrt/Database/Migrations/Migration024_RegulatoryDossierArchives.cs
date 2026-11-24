namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 024: Establishes encrypted FDA IND / EMA IMPD regulatory dossier archive persistence
/// (<c>weaver_regulatory_dossiers</c>) across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB database engines.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Relational schema evolution persisting NIST FIPS 204 ML-DSA-65 signed,
/// AES-256-GCM at-rest encrypted IND / IMPD clinical trial submission bundles and readiness scores.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 45 (P235): Provides durable, tamper-evident archival and
/// GDPR Article 17 patient erasure support for compiled clinical trial submission packages across all 4 SQL backends.
/// </para>
/// <para>
/// <strong>How it works:</strong> Emits dialect-specific DDL for PostgreSQL, DuckDB, MySQL/MariaDB (<c>LONGTEXT</c>
/// for encrypted JSON bundles, <c>TINYINT(1)</c> for readiness flags, and non-IF-NOT-EXISTS indexes), and SQLite.
/// </para>
/// </remarks>
public sealed class Migration024_RegulatoryDossierArchives : DatabaseMigrationBase
{
    /// <inheritdoc/>
    public override long Version => 24;

    /// <inheritdoc/>
    public override string Name => "024_RegulatoryDossierArchives";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits dialect-specific DDL creating weaver_regulatory_dossiers and 3 query indexes.
        // WHY:  Persists large encrypted IND/IMPD dossier archives deterministically across all 4 supported SQL engines.
        // HOW:  Branches on dialect prefix for PostgreSQL, DuckDB, MySQL/MariaDB, and SQLite default.
        ArgumentNullException.ThrowIfNull(dialect);

        if (dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_regulatory_dossiers (
    dossier_id            VARCHAR(64)      PRIMARY KEY,
    patient_id            VARCHAR(128)     NOT NULL,
    lot_id                VARCHAR(64)      NULL,
    submission_framework  VARCHAR(32)      NOT NULL,
    payload_sha256        VARCHAR(64)      NOT NULL,
    signature_base64      TEXT             NOT NULL,
    signer_key_id         VARCHAR(128)     NOT NULL,
    bundle_json_encrypted TEXT             NOT NULL,
    completeness_score    DOUBLE PRECISION NOT NULL,
    is_submission_ready   BOOLEAN          NOT NULL,
    compiled_by           VARCHAR(128)     NOT NULL,
    compiled_at_utc       TIMESTAMPTZ      NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_weaver_dossiers_patient ON weaver_regulatory_dossiers (patient_id, compiled_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_dossiers_lot ON weaver_regulatory_dossiers (lot_id);
CREATE INDEX IF NOT EXISTS idx_weaver_dossiers_framework ON weaver_regulatory_dossiers (submission_framework, compiled_at_utc);
";
        }

        if (dialect.StartsWith("duckdb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_regulatory_dossiers (
    dossier_id            VARCHAR(64)      PRIMARY KEY,
    patient_id            VARCHAR(128)     NOT NULL,
    lot_id                VARCHAR(64)      NULL,
    submission_framework  VARCHAR(32)      NOT NULL,
    payload_sha256        VARCHAR(64)      NOT NULL,
    signature_base64      TEXT             NOT NULL,
    signer_key_id         VARCHAR(128)     NOT NULL,
    bundle_json_encrypted TEXT             NOT NULL,
    completeness_score    DOUBLE PRECISION NOT NULL,
    is_submission_ready   BOOLEAN          NOT NULL,
    compiled_by           VARCHAR(128)     NOT NULL,
    compiled_at_utc       TIMESTAMP        NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_weaver_dossiers_patient ON weaver_regulatory_dossiers (patient_id, compiled_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_dossiers_lot ON weaver_regulatory_dossiers (lot_id);
CREATE INDEX IF NOT EXISTS idx_weaver_dossiers_framework ON weaver_regulatory_dossiers (submission_framework, compiled_at_utc);
";
        }

        if (dialect.StartsWith("mysql", StringComparison.OrdinalIgnoreCase) || dialect.StartsWith("mariadb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_regulatory_dossiers (
    dossier_id            VARCHAR(64)  NOT NULL PRIMARY KEY,
    patient_id            VARCHAR(128) NOT NULL,
    lot_id                VARCHAR(64)  NULL,
    submission_framework  VARCHAR(32)  NOT NULL,
    payload_sha256        VARCHAR(64)  NOT NULL,
    signature_base64      LONGTEXT     NOT NULL,
    signer_key_id         VARCHAR(128) NOT NULL,
    bundle_json_encrypted LONGTEXT     NOT NULL,
    completeness_score    DOUBLE       NOT NULL,
    is_submission_ready   TINYINT(1)   NOT NULL,
    compiled_by           VARCHAR(128) NOT NULL,
    compiled_at_utc       DATETIME(6)  NOT NULL
);

CREATE INDEX idx_weaver_dossiers_patient ON weaver_regulatory_dossiers (patient_id, compiled_at_utc);
CREATE INDEX idx_weaver_dossiers_lot ON weaver_regulatory_dossiers (lot_id);
CREATE INDEX idx_weaver_dossiers_framework ON weaver_regulatory_dossiers (submission_framework, compiled_at_utc);
";
        }

        return @"
CREATE TABLE IF NOT EXISTS weaver_regulatory_dossiers (
    dossier_id            TEXT PRIMARY KEY,
    patient_id            TEXT NOT NULL,
    lot_id                TEXT NULL,
    submission_framework  TEXT NOT NULL,
    payload_sha256        TEXT NOT NULL,
    signature_base64      TEXT NOT NULL,
    signer_key_id         TEXT NOT NULL,
    bundle_json_encrypted TEXT NOT NULL,
    completeness_score    REAL NOT NULL,
    is_submission_ready   INTEGER NOT NULL,
    compiled_by           TEXT NOT NULL,
    compiled_at_utc       TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_weaver_dossiers_patient ON weaver_regulatory_dossiers (patient_id, compiled_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_dossiers_lot ON weaver_regulatory_dossiers (lot_id);
CREATE INDEX IF NOT EXISTS idx_weaver_dossiers_framework ON weaver_regulatory_dossiers (submission_framework, compiled_at_utc);
";
    }
}
