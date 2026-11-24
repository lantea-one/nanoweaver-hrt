namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 023: Establishes paired toehold strand-displacement antidote persistence
/// (<c>weaver_lot_reversal_antidotes</c>) and emergency clinical neutralization event tracking
/// (<c>weaver_lot_neutralization_events</c>) across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Relational schema evolution persisting ML-DSA-65 signed ssDNA reversal antidotes
/// paired with primary fabrication lots and immutable 24-hour neutralization intervention records.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 44 (P230): Enables auditable emergency kill-switch traceability and
/// fail-closed paired-antidote enforcement across all four supported SQL database backends.
/// </para>
/// <para>
/// <strong>How it works:</strong> Emits dialect-specific DDL for PostgreSQL, DuckDB (omitting unsupported
/// <c>ON DELETE CASCADE</c>), MySQL/MariaDB (named foreign-key constraints and non-IF-NOT-EXISTS indexes), and SQLite.
/// </para>
/// </remarks>
public sealed class Migration023_ReversalAntidoteProtocols : DatabaseMigrationBase
{
    /// <inheritdoc/>
    public override long Version => 23;

    /// <inheritdoc/>
    public override string Name => "023_ReversalAntidoteProtocols";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits dialect-specific DDL creating weaver_lot_reversal_antidotes, weaver_lot_neutralization_events, and 4 indexes.
        // WHY:  Supports deterministic antidote pairing and emergency neutralization storage across all 4 SQL engines.
        // HOW:  Branches on dialect prefix for PostgreSQL, DuckDB, MySQL/MariaDB, and SQLite default.
        ArgumentNullException.ThrowIfNull(dialect);

        if (dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_lot_reversal_antidotes (
    antidote_id              VARCHAR(64)      PRIMARY KEY,
    primary_lot_id           VARCHAR(64)      NOT NULL REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE,
    patient_id               VARCHAR(128)     NOT NULL,
    antidote_sequence_5to3   VARCHAR(256)     NOT NULL,
    toehold_length_nt        INTEGER          NOT NULL,
    melting_temp_celsius     DOUBLE PRECISION NOT NULL,
    binding_delta_g_kcal_mol DOUBLE PRECISION NOT NULL,
    paired_payload_sha256    VARCHAR(64)      NOT NULL,
    paired_signature_base64  TEXT             NOT NULL,
    signer_key_id            VARCHAR(128)     NOT NULL,
    status                   VARCHAR(48)      NOT NULL,
    created_at_utc           TIMESTAMPTZ      NOT NULL
);

CREATE TABLE IF NOT EXISTS weaver_lot_neutralization_events (
    neutralization_id         VARCHAR(64)      PRIMARY KEY,
    antidote_id               VARCHAR(64)      NOT NULL REFERENCES weaver_lot_reversal_antidotes(antidote_id) ON DELETE CASCADE,
    primary_lot_id            VARCHAR(64)      NOT NULL REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE,
    patient_id                VARCHAR(128)     NOT NULL,
    dosage_mg                 DOUBLE PRECISION NOT NULL,
    clinical_reason           TEXT             NOT NULL,
    estimated_clearance_hours DOUBLE PRECISION NOT NULL,
    executed_by               VARCHAR(128)     NOT NULL,
    administered_at_utc       TIMESTAMPTZ      NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_weaver_antidotes_lot ON weaver_lot_reversal_antidotes (primary_lot_id, created_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_antidotes_patient ON weaver_lot_reversal_antidotes (patient_id);
CREATE INDEX IF NOT EXISTS idx_weaver_neutralizations_lot ON weaver_lot_neutralization_events (primary_lot_id, administered_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_neutralizations_patient ON weaver_lot_neutralization_events (patient_id);
";
        }

        if (dialect.StartsWith("duckdb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_lot_reversal_antidotes (
    antidote_id              VARCHAR(64)      PRIMARY KEY,
    primary_lot_id           VARCHAR(64)      NOT NULL REFERENCES weaver_fabrication_lots(lot_id),
    patient_id               VARCHAR(128)     NOT NULL,
    antidote_sequence_5to3   VARCHAR(256)     NOT NULL,
    toehold_length_nt        INTEGER          NOT NULL,
    melting_temp_celsius     DOUBLE PRECISION NOT NULL,
    binding_delta_g_kcal_mol DOUBLE PRECISION NOT NULL,
    paired_payload_sha256    VARCHAR(64)      NOT NULL,
    paired_signature_base64  TEXT             NOT NULL,
    signer_key_id            VARCHAR(128)     NOT NULL,
    status                   VARCHAR(48)      NOT NULL,
    created_at_utc           TIMESTAMP        NOT NULL
);

CREATE TABLE IF NOT EXISTS weaver_lot_neutralization_events (
    neutralization_id         VARCHAR(64)      PRIMARY KEY,
    antidote_id               VARCHAR(64)      NOT NULL REFERENCES weaver_lot_reversal_antidotes(antidote_id),
    primary_lot_id            VARCHAR(64)      NOT NULL REFERENCES weaver_fabrication_lots(lot_id),
    patient_id                VARCHAR(128)     NOT NULL,
    dosage_mg                 DOUBLE PRECISION NOT NULL,
    clinical_reason           TEXT             NOT NULL,
    estimated_clearance_hours DOUBLE PRECISION NOT NULL,
    executed_by               VARCHAR(128)     NOT NULL,
    administered_at_utc       TIMESTAMP        NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_weaver_antidotes_lot ON weaver_lot_reversal_antidotes (primary_lot_id, created_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_antidotes_patient ON weaver_lot_reversal_antidotes (patient_id);
CREATE INDEX IF NOT EXISTS idx_weaver_neutralizations_lot ON weaver_lot_neutralization_events (primary_lot_id, administered_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_neutralizations_patient ON weaver_lot_neutralization_events (patient_id);
";
        }

        if (dialect.StartsWith("mysql", StringComparison.OrdinalIgnoreCase) || dialect.StartsWith("mariadb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_lot_reversal_antidotes (
    antidote_id              VARCHAR(64)  NOT NULL PRIMARY KEY,
    primary_lot_id           VARCHAR(64)  NOT NULL,
    patient_id               VARCHAR(128) NOT NULL,
    antidote_sequence_5to3   VARCHAR(256) NOT NULL,
    toehold_length_nt        INT          NOT NULL,
    melting_temp_celsius     DOUBLE       NOT NULL,
    binding_delta_g_kcal_mol DOUBLE       NOT NULL,
    paired_payload_sha256    VARCHAR(64)  NOT NULL,
    paired_signature_base64  TEXT         NOT NULL,
    signer_key_id            VARCHAR(128) NOT NULL,
    status                   VARCHAR(48)  NOT NULL,
    created_at_utc           DATETIME(6)  NOT NULL,
    CONSTRAINT fk_weaver_antidotes_lot FOREIGN KEY (primary_lot_id) REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS weaver_lot_neutralization_events (
    neutralization_id         VARCHAR(64)  NOT NULL PRIMARY KEY,
    antidote_id               VARCHAR(64)  NOT NULL,
    primary_lot_id            VARCHAR(64)  NOT NULL,
    patient_id                VARCHAR(128) NOT NULL,
    dosage_mg                 DOUBLE       NOT NULL,
    clinical_reason           TEXT         NOT NULL,
    estimated_clearance_hours DOUBLE       NOT NULL,
    executed_by               VARCHAR(128) NOT NULL,
    administered_at_utc       DATETIME(6)  NOT NULL,
    CONSTRAINT fk_weaver_neutralizations_antidote FOREIGN KEY (antidote_id) REFERENCES weaver_lot_reversal_antidotes(antidote_id) ON DELETE CASCADE,
    CONSTRAINT fk_weaver_neutralizations_lot FOREIGN KEY (primary_lot_id) REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE
);

CREATE INDEX idx_weaver_antidotes_lot ON weaver_lot_reversal_antidotes (primary_lot_id, created_at_utc);
CREATE INDEX idx_weaver_antidotes_patient ON weaver_lot_reversal_antidotes (patient_id);
CREATE INDEX idx_weaver_neutralizations_lot ON weaver_lot_neutralization_events (primary_lot_id, administered_at_utc);
CREATE INDEX idx_weaver_neutralizations_patient ON weaver_lot_neutralization_events (patient_id);
";
        }

        return @"
CREATE TABLE IF NOT EXISTS weaver_lot_reversal_antidotes (
    antidote_id              TEXT PRIMARY KEY,
    primary_lot_id           TEXT NOT NULL REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE,
    patient_id               TEXT NOT NULL,
    antidote_sequence_5to3   TEXT NOT NULL,
    toehold_length_nt        INTEGER NOT NULL,
    melting_temp_celsius     REAL NOT NULL,
    binding_delta_g_kcal_mol REAL NOT NULL,
    paired_payload_sha256    TEXT NOT NULL,
    paired_signature_base64  TEXT NOT NULL,
    signer_key_id            TEXT NOT NULL,
    status                   TEXT NOT NULL,
    created_at_utc           TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS weaver_lot_neutralization_events (
    neutralization_id         TEXT PRIMARY KEY,
    antidote_id               TEXT NOT NULL REFERENCES weaver_lot_reversal_antidotes(antidote_id) ON DELETE CASCADE,
    primary_lot_id            TEXT NOT NULL REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE,
    patient_id                TEXT NOT NULL,
    dosage_mg                 REAL NOT NULL,
    clinical_reason           TEXT NOT NULL,
    estimated_clearance_hours REAL NOT NULL,
    executed_by               TEXT NOT NULL,
    administered_at_utc       TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_weaver_antidotes_lot ON weaver_lot_reversal_antidotes (primary_lot_id, created_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_antidotes_patient ON weaver_lot_reversal_antidotes (patient_id);
CREATE INDEX IF NOT EXISTS idx_weaver_neutralizations_lot ON weaver_lot_neutralization_events (primary_lot_id, administered_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_neutralizations_patient ON weaver_lot_neutralization_events (patient_id);
";
    }
}
