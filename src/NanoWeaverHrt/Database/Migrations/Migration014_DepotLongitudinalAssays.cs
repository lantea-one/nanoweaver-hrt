namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 014: Establishes database tables and indexes for longitudinal depot serum hormone assay observations
/// and adaptive Bayesian calibration snapshots across SQLite, PostgreSQL, DuckDB, and MySQL engines.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The relational schema tables (<c>weaver_depot_assay_observations</c> and
/// <c>weaver_depot_calibration_snapshots</c>) persisting patient lab assay timepoints and inferred in-vivo
/// pharmacokinetic calibration outputs linked to active semi-annual depot formulations.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Enables longitudinal closed-loop recalibration of 180-day subcutaneous nanocarrier
/// depots as real-world serum estradiol, testosterone, LH, FSH, SHBG, and hematocrit lab measurements arrive.
/// </para>
/// <para>
/// <strong>How it works:</strong> Emits dialect-specific DDL for PostgreSQL, DuckDB, MySQL/MariaDB, and SQLite with
/// foreign key links to <c>weaver_depot_formulations</c> and performance indexes on patient and depot identifiers.
/// </para>
/// </remarks>
public sealed class Migration014_DepotLongitudinalAssays : DatabaseMigrationBase
{
    /// <inheritdoc/>
    public override long Version => 14;

    /// <inheritdoc/>
    public override string Name => "014_DepotLongitudinalAssays";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits dialect-specific DDL creating weaver_depot_assay_observations and weaver_depot_calibration_snapshots.
        // WHY:  Ensures deterministic multi-engine persistence across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB.
        // HOW:  Selects VARCHAR/DOUBLE PRECISION on PostgreSQL and DuckDB, DOUBLE on MySQL/MariaDB, and TEXT/REAL on SQLite.
        ArgumentNullException.ThrowIfNull(dialect);

        if (dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_depot_assay_observations (
    observation_id          VARCHAR(64)      PRIMARY KEY,
    depot_id                VARCHAR(64)      NOT NULL REFERENCES weaver_depot_formulations(depot_id) ON DELETE CASCADE,
    patient_id              VARCHAR(128)     NOT NULL,
    day_since_implant       INTEGER          NOT NULL,
    observed_e2_pmol_l      DOUBLE PRECISION NOT NULL,
    observed_t_nmol_l       DOUBLE PRECISION NOT NULL,
    observed_lh_iu_l        DOUBLE PRECISION NOT NULL,
    observed_fsh_iu_l       DOUBLE PRECISION NOT NULL,
    observed_shbg_nmol_l    DOUBLE PRECISION NOT NULL,
    observed_hematocrit_pct DOUBLE PRECISION NOT NULL,
    recorded_at_utc         VARCHAR(64)      NOT NULL,
    notes_encrypted         TEXT             NULL
);

CREATE INDEX IF NOT EXISTS idx_depot_assay_patient_id ON weaver_depot_assay_observations (patient_id);
CREATE INDEX IF NOT EXISTS idx_depot_assay_depot_day ON weaver_depot_assay_observations (depot_id, day_since_implant);

CREATE TABLE IF NOT EXISTS weaver_depot_calibration_snapshots (
    snapshot_id                   VARCHAR(64)      PRIMARY KEY,
    depot_id                      VARCHAR(64)      NOT NULL REFERENCES weaver_depot_formulations(depot_id) ON DELETE CASCADE,
    patient_id                    VARCHAR(128)     NOT NULL,
    inferred_clearance_multiplier DOUBLE PRECISION NOT NULL,
    inferred_erosion_rate_percent DOUBLE PRECISION NOT NULL,
    adjusted_redose_day           INTEGER          NOT NULL,
    recommended_next_mass_mg      DOUBLE PRECISION NOT NULL,
    safety_status                 VARCHAR(32)      NOT NULL,
    residual_rmse                 DOUBLE PRECISION NOT NULL,
    computed_at_utc               VARCHAR(64)      NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_depot_calib_depot_id ON weaver_depot_calibration_snapshots (depot_id);
CREATE INDEX IF NOT EXISTS idx_depot_calib_patient_id ON weaver_depot_calibration_snapshots (patient_id);
";
        }

        if (dialect.StartsWith("duckdb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_depot_assay_observations (
    observation_id          VARCHAR(64)      PRIMARY KEY,
    depot_id                VARCHAR(64)      NOT NULL REFERENCES weaver_depot_formulations(depot_id),
    patient_id              VARCHAR(128)     NOT NULL,
    day_since_implant       INTEGER          NOT NULL,
    observed_e2_pmol_l      DOUBLE PRECISION NOT NULL,
    observed_t_nmol_l       DOUBLE PRECISION NOT NULL,
    observed_lh_iu_l        DOUBLE PRECISION NOT NULL,
    observed_fsh_iu_l       DOUBLE PRECISION NOT NULL,
    observed_shbg_nmol_l    DOUBLE PRECISION NOT NULL,
    observed_hematocrit_pct DOUBLE PRECISION NOT NULL,
    recorded_at_utc         VARCHAR(64)      NOT NULL,
    notes_encrypted         TEXT             NULL
);

CREATE INDEX IF NOT EXISTS idx_depot_assay_patient_id ON weaver_depot_assay_observations (patient_id);
CREATE INDEX IF NOT EXISTS idx_depot_assay_depot_day ON weaver_depot_assay_observations (depot_id, day_since_implant);

CREATE TABLE IF NOT EXISTS weaver_depot_calibration_snapshots (
    snapshot_id                   VARCHAR(64)      PRIMARY KEY,
    depot_id                      VARCHAR(64)      NOT NULL REFERENCES weaver_depot_formulations(depot_id),
    patient_id                    VARCHAR(128)     NOT NULL,
    inferred_clearance_multiplier DOUBLE PRECISION NOT NULL,
    inferred_erosion_rate_percent DOUBLE PRECISION NOT NULL,
    adjusted_redose_day           INTEGER          NOT NULL,
    recommended_next_mass_mg      DOUBLE PRECISION NOT NULL,
    safety_status                 VARCHAR(32)      NOT NULL,
    residual_rmse                 DOUBLE PRECISION NOT NULL,
    computed_at_utc               VARCHAR(64)      NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_depot_calib_depot_id ON weaver_depot_calibration_snapshots (depot_id);
CREATE INDEX IF NOT EXISTS idx_depot_calib_patient_id ON weaver_depot_calibration_snapshots (patient_id);
";
        }

        if (dialect.StartsWith("mysql", StringComparison.OrdinalIgnoreCase) || dialect.StartsWith("mariadb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_depot_assay_observations (
    observation_id          VARCHAR(64)  PRIMARY KEY,
    depot_id                VARCHAR(64)  NOT NULL,
    patient_id              VARCHAR(128) NOT NULL,
    day_since_implant       INTEGER      NOT NULL,
    observed_e2_pmol_l      DOUBLE       NOT NULL,
    observed_t_nmol_l       DOUBLE       NOT NULL,
    observed_lh_iu_l        DOUBLE       NOT NULL,
    observed_fsh_iu_l       DOUBLE       NOT NULL,
    observed_shbg_nmol_l    DOUBLE       NOT NULL,
    observed_hematocrit_pct DOUBLE       NOT NULL,
    recorded_at_utc         VARCHAR(64)  NOT NULL,
    notes_encrypted         TEXT         NULL,
    FOREIGN KEY (depot_id) REFERENCES weaver_depot_formulations(depot_id) ON DELETE CASCADE
);

CREATE INDEX idx_depot_assay_patient_id ON weaver_depot_assay_observations (patient_id);
CREATE INDEX idx_depot_assay_depot_day ON weaver_depot_assay_observations (depot_id, day_since_implant);

CREATE TABLE IF NOT EXISTS weaver_depot_calibration_snapshots (
    snapshot_id                   VARCHAR(64)  PRIMARY KEY,
    depot_id                      VARCHAR(64)  NOT NULL,
    patient_id                    VARCHAR(128) NOT NULL,
    inferred_clearance_multiplier DOUBLE       NOT NULL,
    inferred_erosion_rate_percent DOUBLE       NOT NULL,
    adjusted_redose_day           INTEGER      NOT NULL,
    recommended_next_mass_mg      DOUBLE       NOT NULL,
    safety_status                 VARCHAR(32)  NOT NULL,
    residual_rmse                 DOUBLE       NOT NULL,
    computed_at_utc               VARCHAR(64)  NOT NULL,
    FOREIGN KEY (depot_id) REFERENCES weaver_depot_formulations(depot_id) ON DELETE CASCADE
);

CREATE INDEX idx_depot_calib_depot_id ON weaver_depot_calibration_snapshots (depot_id);
CREATE INDEX idx_depot_calib_patient_id ON weaver_depot_calibration_snapshots (patient_id);
";
        }

        return @"
CREATE TABLE IF NOT EXISTS weaver_depot_assay_observations (
    observation_id          TEXT    PRIMARY KEY,
    depot_id                TEXT    NOT NULL REFERENCES weaver_depot_formulations(depot_id) ON DELETE CASCADE,
    patient_id              TEXT    NOT NULL,
    day_since_implant       INTEGER NOT NULL,
    observed_e2_pmol_l      REAL    NOT NULL,
    observed_t_nmol_l       REAL    NOT NULL,
    observed_lh_iu_l        REAL    NOT NULL,
    observed_fsh_iu_l       REAL    NOT NULL,
    observed_shbg_nmol_l    REAL    NOT NULL,
    observed_hematocrit_pct REAL    NOT NULL,
    recorded_at_utc         TEXT    NOT NULL,
    notes_encrypted         TEXT    NULL
);

CREATE INDEX IF NOT EXISTS idx_depot_assay_patient_id ON weaver_depot_assay_observations (patient_id);
CREATE INDEX IF NOT EXISTS idx_depot_assay_depot_day ON weaver_depot_assay_observations (depot_id, day_since_implant);

CREATE TABLE IF NOT EXISTS weaver_depot_calibration_snapshots (
    snapshot_id                   TEXT    PRIMARY KEY,
    depot_id                      TEXT    NOT NULL REFERENCES weaver_depot_formulations(depot_id) ON DELETE CASCADE,
    patient_id                    TEXT    NOT NULL,
    inferred_clearance_multiplier REAL    NOT NULL,
    inferred_erosion_rate_percent REAL    NOT NULL,
    adjusted_redose_day           INTEGER NOT NULL,
    recommended_next_mass_mg      REAL    NOT NULL,
    safety_status                 TEXT    NOT NULL,
    residual_rmse                 REAL    NOT NULL,
    computed_at_utc               TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_depot_calib_depot_id ON weaver_depot_calibration_snapshots (depot_id);
CREATE INDEX IF NOT EXISTS idx_depot_calib_patient_id ON weaver_depot_calibration_snapshots (patient_id);
";
    }
}
