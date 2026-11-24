namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 020: Establishes post-implantation clinical adverse event tracking (<c>weaver_lot_adverse_events</c>)
/// and CARPA/TLR9 immunogenicity assay persistence (<c>weaver_lot_immunogenicity_assays</c>) across
/// SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB database engines.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Relational schema evolution persisting CTCAE v5.0 adverse events and quantitative
/// complement/cytokine/anti-DNA biomarker panels linked to <c>weaver_fabrication_lots</c>.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 41 (P215): Enables post-implantation safety surveillance and automated
/// fail-closed lot quarantine escalation across all four supported relational persistence engines.
/// </para>
/// <para>
/// <strong>How it works:</strong> Emits dialect-specific DDL for PostgreSQL, DuckDB (omitting unsupported
/// <c>ON DELETE CASCADE</c>), MySQL/MariaDB (table-level constraints and non-IF-NOT-EXISTS indexes), and SQLite.
/// </para>
/// </remarks>
public sealed class Migration020_ClinicalPharmacovigilance : DatabaseMigrationBase
{
    /// <inheritdoc/>
    public override long Version => 20;

    /// <inheritdoc/>
    public override string Name => "020_ClinicalPharmacovigilance";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits dialect-specific DDL creating weaver_lot_adverse_events, weaver_lot_immunogenicity_assays, and 4 query indexes.
        // WHY:  Supports deterministic CTCAE v5.0 adverse event and CARPA/TLR9 assay persistence across all 4 SQL backends.
        // HOW:  Branches on dialect prefix for PostgreSQL, DuckDB, MySQL/MariaDB, and SQLite default.
        ArgumentNullException.ThrowIfNull(dialect);

        if (dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_lot_adverse_events (
    event_id                 VARCHAR(64)      PRIMARY KEY,
    lot_id                   VARCHAR(64)      NOT NULL REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE,
    patient_id               VARCHAR(128)     NOT NULL,
    severity_grade           VARCHAR(48)      NOT NULL,
    symptom_category         VARCHAR(128)     NOT NULL,
    onset_hours_post_implant DOUBLE PRECISION NOT NULL,
    clinical_notes_encrypted TEXT             NULL,
    reported_by              VARCHAR(128)     NOT NULL,
    recorded_at_utc          TIMESTAMPTZ      NOT NULL
);

CREATE TABLE IF NOT EXISTS weaver_lot_immunogenicity_assays (
    assay_id                VARCHAR(64)      PRIMARY KEY,
    lot_id                  VARCHAR(64)      NOT NULL REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE,
    patient_id              VARCHAR(128)     NOT NULL,
    ch50_percent            DOUBLE PRECISION NOT NULL,
    c3a_ng_ml               DOUBLE PRECISION NOT NULL,
    c5a_ng_ml               DOUBLE PRECISION NOT NULL,
    il6_pg_ml               DOUBLE PRECISION NOT NULL,
    tnfa_pg_ml              DOUBLE PRECISION NOT NULL,
    anti_peg_igm_titer      DOUBLE PRECISION NOT NULL,
    anti_dsdna_igg_iu_ml    DOUBLE PRECISION NOT NULL,
    tlr9_activation_index   DOUBLE PRECISION NOT NULL,
    recorded_at_utc         TIMESTAMPTZ      NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_weaver_adverse_events_lot ON weaver_lot_adverse_events (lot_id, recorded_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_adverse_events_patient ON weaver_lot_adverse_events (patient_id);
CREATE INDEX IF NOT EXISTS idx_weaver_immunogenicity_lot ON weaver_lot_immunogenicity_assays (lot_id, recorded_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_immunogenicity_patient ON weaver_lot_immunogenicity_assays (patient_id);
";
        }

        if (dialect.StartsWith("duckdb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_lot_adverse_events (
    event_id                 VARCHAR(64)      PRIMARY KEY,
    lot_id                   VARCHAR(64)      NOT NULL REFERENCES weaver_fabrication_lots(lot_id),
    patient_id               VARCHAR(128)     NOT NULL,
    severity_grade           VARCHAR(48)      NOT NULL,
    symptom_category         VARCHAR(128)     NOT NULL,
    onset_hours_post_implant DOUBLE PRECISION NOT NULL,
    clinical_notes_encrypted TEXT             NULL,
    reported_by              VARCHAR(128)     NOT NULL,
    recorded_at_utc          TIMESTAMP        NOT NULL
);

CREATE TABLE IF NOT EXISTS weaver_lot_immunogenicity_assays (
    assay_id                VARCHAR(64)      PRIMARY KEY,
    lot_id                  VARCHAR(64)      NOT NULL REFERENCES weaver_fabrication_lots(lot_id),
    patient_id              VARCHAR(128)     NOT NULL,
    ch50_percent            DOUBLE PRECISION NOT NULL,
    c3a_ng_ml               DOUBLE PRECISION NOT NULL,
    c5a_ng_ml               DOUBLE PRECISION NOT NULL,
    il6_pg_ml               DOUBLE PRECISION NOT NULL,
    tnfa_pg_ml              DOUBLE PRECISION NOT NULL,
    anti_peg_igm_titer      DOUBLE PRECISION NOT NULL,
    anti_dsdna_igg_iu_ml    DOUBLE PRECISION NOT NULL,
    tlr9_activation_index   DOUBLE PRECISION NOT NULL,
    recorded_at_utc         TIMESTAMP        NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_weaver_adverse_events_lot ON weaver_lot_adverse_events (lot_id, recorded_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_adverse_events_patient ON weaver_lot_adverse_events (patient_id);
CREATE INDEX IF NOT EXISTS idx_weaver_immunogenicity_lot ON weaver_lot_immunogenicity_assays (lot_id, recorded_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_immunogenicity_patient ON weaver_lot_immunogenicity_assays (patient_id);
";
        }

        if (dialect.StartsWith("mysql", StringComparison.OrdinalIgnoreCase) || dialect.StartsWith("mariadb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_lot_adverse_events (
    event_id                 VARCHAR(64)  NOT NULL PRIMARY KEY,
    lot_id                   VARCHAR(64)  NOT NULL,
    patient_id               VARCHAR(128) NOT NULL,
    severity_grade           VARCHAR(48)  NOT NULL,
    symptom_category         VARCHAR(128) NOT NULL,
    onset_hours_post_implant DOUBLE       NOT NULL,
    clinical_notes_encrypted TEXT         NULL,
    reported_by              VARCHAR(128) NOT NULL,
    recorded_at_utc          DATETIME(6)  NOT NULL,
    CONSTRAINT fk_weaver_adverse_lot FOREIGN KEY (lot_id) REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS weaver_lot_immunogenicity_assays (
    assay_id                VARCHAR(64)  NOT NULL PRIMARY KEY,
    lot_id                  VARCHAR(64)  NOT NULL,
    patient_id              VARCHAR(128) NOT NULL,
    ch50_percent            DOUBLE       NOT NULL,
    c3a_ng_ml               DOUBLE       NOT NULL,
    c5a_ng_ml               DOUBLE       NOT NULL,
    il6_pg_ml               DOUBLE       NOT NULL,
    tnfa_pg_ml              DOUBLE       NOT NULL,
    anti_peg_igm_titer      DOUBLE       NOT NULL,
    anti_dsdna_igg_iu_ml    DOUBLE       NOT NULL,
    tlr9_activation_index   DOUBLE       NOT NULL,
    recorded_at_utc         DATETIME(6)  NOT NULL,
    CONSTRAINT fk_weaver_immuno_lot FOREIGN KEY (lot_id) REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE
);

CREATE INDEX idx_weaver_adverse_events_lot ON weaver_lot_adverse_events (lot_id, recorded_at_utc);
CREATE INDEX idx_weaver_adverse_events_patient ON weaver_lot_adverse_events (patient_id);
CREATE INDEX idx_weaver_immunogenicity_lot ON weaver_lot_immunogenicity_assays (lot_id, recorded_at_utc);
CREATE INDEX idx_weaver_immunogenicity_patient ON weaver_lot_immunogenicity_assays (patient_id);
";
        }

        return @"
CREATE TABLE IF NOT EXISTS weaver_lot_adverse_events (
    event_id                 TEXT PRIMARY KEY,
    lot_id                   TEXT NOT NULL REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE,
    patient_id               TEXT NOT NULL,
    severity_grade           TEXT NOT NULL,
    symptom_category         TEXT NOT NULL,
    onset_hours_post_implant REAL NOT NULL,
    clinical_notes_encrypted TEXT NULL,
    reported_by              TEXT NOT NULL,
    recorded_at_utc          TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS weaver_lot_immunogenicity_assays (
    assay_id                TEXT PRIMARY KEY,
    lot_id                  TEXT NOT NULL REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE,
    patient_id              TEXT NOT NULL,
    ch50_percent            REAL NOT NULL,
    c3a_ng_ml               REAL NOT NULL,
    c5a_ng_ml               REAL NOT NULL,
    il6_pg_ml               REAL NOT NULL,
    tnfa_pg_ml              REAL NOT NULL,
    anti_peg_igm_titer      REAL NOT NULL,
    anti_dsdna_igg_iu_ml    REAL NOT NULL,
    tlr9_activation_index   REAL NOT NULL,
    recorded_at_utc         TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_weaver_adverse_events_lot ON weaver_lot_adverse_events (lot_id, recorded_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_adverse_events_patient ON weaver_lot_adverse_events (patient_id);
CREATE INDEX IF NOT EXISTS idx_weaver_immunogenicity_lot ON weaver_lot_immunogenicity_assays (lot_id, recorded_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_immunogenicity_patient ON weaver_lot_immunogenicity_assays (patient_id);
";
    }
}
