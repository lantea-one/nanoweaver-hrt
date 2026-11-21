namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 019: Establishes the cold-chain thermal excursion log (<c>weaver_lot_thermal_excursions</c>)
/// and tamper-evident chain-of-custody handoff ledger (<c>weaver_lot_custody_handoffs</c>) across
/// SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB database engines.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Relational schema evolution persisting temperature-time cold-chain sensor
/// observations and SHA-256 hash-chained physical custody handoffs linked to <c>weaver_fabrication_lots</c>.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 40 (P210): Enables Arrhenius potency tracking, shelf-life gating,
/// and 21 CFR Part 11 chain-of-custody provenance across all four supported relational persistence engines.
/// </para>
/// <para>
/// <strong>How it works:</strong> Emits dialect-specific DDL for PostgreSQL, DuckDB (omitting unsupported
/// <c>ON DELETE CASCADE</c>), MySQL/MariaDB (table-level constraints and non-IF-NOT-EXISTS indexes), and SQLite.
/// </para>
/// </remarks>
public sealed class Migration019_ColdChainStabilityAndCustody : DatabaseMigrationBase
{
    /// <inheritdoc/>
    public override long Version => 19;

    /// <inheritdoc/>
    public override string Name => "019_ColdChainStabilityAndCustody";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits dialect-specific DDL creating weaver_lot_thermal_excursions, weaver_lot_custody_handoffs, and 4 query indexes.
        // WHY:  Supports deterministic Arrhenius thermal logs and hash-chained custody ledgers across all 4 SQL backends.
        // HOW:  Branches on dialect prefix for PostgreSQL, DuckDB, MySQL/MariaDB, and SQLite default.
        ArgumentNullException.ThrowIfNull(dialect);

        if (dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_lot_thermal_excursions (
    excursion_id        VARCHAR(64)      PRIMARY KEY,
    lot_id              VARCHAR(64)      NOT NULL REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE,
    patient_id          VARCHAR(128)     NOT NULL,
    recorded_at_utc     TIMESTAMPTZ      NOT NULL,
    temperature_celsius DOUBLE PRECISION NOT NULL,
    duration_hours      DOUBLE PRECISION NOT NULL,
    storage_tier        VARCHAR(48)      NOT NULL,
    sensor_id           VARCHAR(128)     NOT NULL,
    notes_encrypted     TEXT             NULL,
    recorded_by         VARCHAR(128)     NOT NULL
);

CREATE TABLE IF NOT EXISTS weaver_lot_custody_handoffs (
    handoff_id                 VARCHAR(64)      PRIMARY KEY,
    lot_id                     VARCHAR(64)      NOT NULL REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE,
    patient_id                 VARCHAR(128)     NOT NULL,
    stage                      VARCHAR(48)      NOT NULL,
    from_actor                 VARCHAR(128)     NOT NULL,
    to_actor                   VARCHAR(128)     NOT NULL,
    facility_code              VARCHAR(64)      NOT NULL,
    temperature_celsius        DOUBLE PRECISION NOT NULL,
    potency_percent_at_handoff DOUBLE PRECISION NOT NULL,
    chain_prev_sha256          VARCHAR(64)      NOT NULL,
    entry_sha256               VARCHAR(64)      NOT NULL,
    recorded_at_utc            TIMESTAMPTZ      NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_weaver_thermal_excursions_lot ON weaver_lot_thermal_excursions (lot_id, recorded_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_thermal_excursions_patient ON weaver_lot_thermal_excursions (patient_id);
CREATE INDEX IF NOT EXISTS idx_weaver_custody_handoffs_lot ON weaver_lot_custody_handoffs (lot_id, recorded_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_custody_handoffs_patient ON weaver_lot_custody_handoffs (patient_id);
";
        }

        if (dialect.StartsWith("duckdb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_lot_thermal_excursions (
    excursion_id        VARCHAR(64)      PRIMARY KEY,
    lot_id              VARCHAR(64)      NOT NULL REFERENCES weaver_fabrication_lots(lot_id),
    patient_id          VARCHAR(128)     NOT NULL,
    recorded_at_utc     TIMESTAMP        NOT NULL,
    temperature_celsius DOUBLE PRECISION NOT NULL,
    duration_hours      DOUBLE PRECISION NOT NULL,
    storage_tier        VARCHAR(48)      NOT NULL,
    sensor_id           VARCHAR(128)     NOT NULL,
    notes_encrypted     TEXT             NULL,
    recorded_by         VARCHAR(128)     NOT NULL
);

CREATE TABLE IF NOT EXISTS weaver_lot_custody_handoffs (
    handoff_id                 VARCHAR(64)      PRIMARY KEY,
    lot_id                     VARCHAR(64)      NOT NULL REFERENCES weaver_fabrication_lots(lot_id),
    patient_id                 VARCHAR(128)     NOT NULL,
    stage                      VARCHAR(48)      NOT NULL,
    from_actor                 VARCHAR(128)     NOT NULL,
    to_actor                   VARCHAR(128)     NOT NULL,
    facility_code              VARCHAR(64)      NOT NULL,
    temperature_celsius        DOUBLE PRECISION NOT NULL,
    potency_percent_at_handoff DOUBLE PRECISION NOT NULL,
    chain_prev_sha256          VARCHAR(64)      NOT NULL,
    entry_sha256               VARCHAR(64)      NOT NULL,
    recorded_at_utc            TIMESTAMP        NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_weaver_thermal_excursions_lot ON weaver_lot_thermal_excursions (lot_id, recorded_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_thermal_excursions_patient ON weaver_lot_thermal_excursions (patient_id);
CREATE INDEX IF NOT EXISTS idx_weaver_custody_handoffs_lot ON weaver_lot_custody_handoffs (lot_id, recorded_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_custody_handoffs_patient ON weaver_lot_custody_handoffs (patient_id);
";
        }

        if (dialect.StartsWith("mysql", StringComparison.OrdinalIgnoreCase) || dialect.StartsWith("mariadb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_lot_thermal_excursions (
    excursion_id        VARCHAR(64)  NOT NULL PRIMARY KEY,
    lot_id              VARCHAR(64)  NOT NULL,
    patient_id          VARCHAR(128) NOT NULL,
    recorded_at_utc     DATETIME(6)  NOT NULL,
    temperature_celsius DOUBLE       NOT NULL,
    duration_hours      DOUBLE       NOT NULL,
    storage_tier        VARCHAR(48)  NOT NULL,
    sensor_id           VARCHAR(128) NOT NULL,
    notes_encrypted     TEXT         NULL,
    recorded_by         VARCHAR(128) NOT NULL,
    CONSTRAINT fk_weaver_thermal_lot FOREIGN KEY (lot_id) REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS weaver_lot_custody_handoffs (
    handoff_id                 VARCHAR(64)  NOT NULL PRIMARY KEY,
    lot_id                     VARCHAR(64)  NOT NULL,
    patient_id                 VARCHAR(128) NOT NULL,
    stage                      VARCHAR(48)  NOT NULL,
    from_actor                 VARCHAR(128) NOT NULL,
    to_actor                   VARCHAR(128) NOT NULL,
    facility_code              VARCHAR(64)  NOT NULL,
    temperature_celsius        DOUBLE       NOT NULL,
    potency_percent_at_handoff DOUBLE       NOT NULL,
    chain_prev_sha256          VARCHAR(64)  NOT NULL,
    entry_sha256               VARCHAR(64)  NOT NULL,
    recorded_at_utc            DATETIME(6)  NOT NULL,
    CONSTRAINT fk_weaver_custody_lot FOREIGN KEY (lot_id) REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE
);

CREATE INDEX idx_weaver_thermal_excursions_lot ON weaver_lot_thermal_excursions (lot_id, recorded_at_utc);
CREATE INDEX idx_weaver_thermal_excursions_patient ON weaver_lot_thermal_excursions (patient_id);
CREATE INDEX idx_weaver_custody_handoffs_lot ON weaver_lot_custody_handoffs (lot_id, recorded_at_utc);
CREATE INDEX idx_weaver_custody_handoffs_patient ON weaver_lot_custody_handoffs (patient_id);
";
        }

        return @"
CREATE TABLE IF NOT EXISTS weaver_lot_thermal_excursions (
    excursion_id        TEXT PRIMARY KEY,
    lot_id              TEXT NOT NULL REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE,
    patient_id          TEXT NOT NULL,
    recorded_at_utc     TEXT NOT NULL,
    temperature_celsius REAL NOT NULL,
    duration_hours      REAL NOT NULL,
    storage_tier        TEXT NOT NULL,
    sensor_id           TEXT NOT NULL,
    notes_encrypted     TEXT NULL,
    recorded_by         TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS weaver_lot_custody_handoffs (
    handoff_id                 TEXT PRIMARY KEY,
    lot_id                     TEXT NOT NULL REFERENCES weaver_fabrication_lots(lot_id) ON DELETE CASCADE,
    patient_id                 TEXT NOT NULL,
    stage                      TEXT NOT NULL,
    from_actor                 TEXT NOT NULL,
    to_actor                   TEXT NOT NULL,
    facility_code              TEXT NOT NULL,
    temperature_celsius        REAL NOT NULL,
    potency_percent_at_handoff REAL NOT NULL,
    chain_prev_sha256          TEXT NOT NULL,
    entry_sha256               TEXT NOT NULL,
    recorded_at_utc            TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_weaver_thermal_excursions_lot ON weaver_lot_thermal_excursions (lot_id, recorded_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_thermal_excursions_patient ON weaver_lot_thermal_excursions (patient_id);
CREATE INDEX IF NOT EXISTS idx_weaver_custody_handoffs_lot ON weaver_lot_custody_handoffs (lot_id, recorded_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_custody_handoffs_patient ON weaver_lot_custody_handoffs (patient_id);
";
    }
}
