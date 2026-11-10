namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 013: Establishes database tables and indexes for semi-annual time-release depot formulations
/// and discrete elution release schedules across SQLite, PostgreSQL, DuckDB, and MySQL engines.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The relational database schema tables (<c>weaver_depot_formulations</c> and
/// <c>weaver_depot_release_schedules</c>) storing sustained nanocarrier time-release formulations, polymer matrix
/// degradation parameters, and daily pharmacokinetic release trajectories.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Unifies persistent storage for semi-annual (180-day) hormone replacement therapy
/// formulations across all supported database backends, supporting point lookups, patient cohort filtering,
/// and discrete schedule tracking.
/// </para>
/// <para>
/// <strong>How it works:</strong> Applies relational tables with primary keys, cascade deletions on depot removal,
/// foreign key constraints, and performance indexes on <c>patient_id</c> and <c>depot_id</c>.
/// </para>
/// </remarks>
public sealed class Migration013_TimeReleaseDepotFormulations : DatabaseMigrationBase
{
    /// <inheritdoc/>
    public override long Version => 13;

    /// <inheritdoc/>
    public override string Name => "013_TimeReleaseDepotFormulations";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits dialect-specific DDL creating weaver_depot_formulations and weaver_depot_release_schedules.
        // WHY:  Provides deterministic schema migration across SQLite, PostgreSQL, DuckDB, and MySQL engines.
        // HOW:  Uses VARCHAR and DOUBLE PRECISION on PostgreSQL/DuckDB; DOUBLE on MySQL; TEXT and REAL on SQLite.
        ArgumentNullException.ThrowIfNull(dialect);

        if (dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_depot_formulations (
    depot_id                   VARCHAR(64)      PRIMARY KEY,
    patient_id                 VARCHAR(128)     NOT NULL,
    regimen                    VARCHAR(32)      NOT NULL,
    polymer_matrix_type        VARCHAR(64)      NOT NULL,
    initial_nanocarrier_mass_mg DOUBLE PRECISION NOT NULL,
    daily_erosion_rate_percent  DOUBLE PRECISION NOT NULL,
    release_kinetics_model     VARCHAR(64)      NOT NULL,
    target_horizon_days        INTEGER          NOT NULL DEFAULT 180,
    created_at_utc             VARCHAR(64)      NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_depot_patient_id ON weaver_depot_formulations (patient_id);

CREATE TABLE IF NOT EXISTS weaver_depot_release_schedules (
    schedule_id                 VARCHAR(64)      PRIMARY KEY,
    depot_id                    VARCHAR(64)      NOT NULL REFERENCES weaver_depot_formulations(depot_id) ON DELETE CASCADE,
    timepoint_day               INTEGER          NOT NULL,
    cumulative_release_fraction DOUBLE PRECISION NOT NULL,
    instantaneous_rate_mg_day   DOUBLE PRECISION NOT NULL,
    remaining_reserve_mg        DOUBLE PRECISION NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_schedule_depot_id ON weaver_depot_release_schedules (depot_id);
";
        }

        if (dialect.StartsWith("duckdb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_depot_formulations (
    depot_id                   VARCHAR(64)      PRIMARY KEY,
    patient_id                 VARCHAR(128)     NOT NULL,
    regimen                    VARCHAR(32)      NOT NULL,
    polymer_matrix_type        VARCHAR(64)      NOT NULL,
    initial_nanocarrier_mass_mg DOUBLE PRECISION NOT NULL,
    daily_erosion_rate_percent  DOUBLE PRECISION NOT NULL,
    release_kinetics_model     VARCHAR(64)      NOT NULL,
    target_horizon_days        INTEGER          NOT NULL DEFAULT 180,
    created_at_utc             VARCHAR(64)      NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_depot_patient_id ON weaver_depot_formulations (patient_id);

CREATE TABLE IF NOT EXISTS weaver_depot_release_schedules (
    schedule_id                 VARCHAR(64)      PRIMARY KEY,
    depot_id                    VARCHAR(64)      NOT NULL REFERENCES weaver_depot_formulations(depot_id),
    timepoint_day               INTEGER          NOT NULL,
    cumulative_release_fraction DOUBLE PRECISION NOT NULL,
    instantaneous_rate_mg_day   DOUBLE PRECISION NOT NULL,
    remaining_reserve_mg        DOUBLE PRECISION NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_schedule_depot_id ON weaver_depot_release_schedules (depot_id);
";
        }

        if (dialect.StartsWith("mysql", StringComparison.OrdinalIgnoreCase) || dialect.StartsWith("mariadb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_depot_formulations (
    depot_id                   VARCHAR(64)  PRIMARY KEY,
    patient_id                 VARCHAR(128) NOT NULL,
    regimen                    VARCHAR(32)  NOT NULL,
    polymer_matrix_type        VARCHAR(64)  NOT NULL,
    initial_nanocarrier_mass_mg DOUBLE       NOT NULL,
    daily_erosion_rate_percent  DOUBLE       NOT NULL,
    release_kinetics_model     VARCHAR(64)  NOT NULL,
    target_horizon_days        INTEGER      NOT NULL DEFAULT 180,
    created_at_utc             VARCHAR(64)  NOT NULL
);

CREATE INDEX idx_depot_patient_id ON weaver_depot_formulations (patient_id);

CREATE TABLE IF NOT EXISTS weaver_depot_release_schedules (
    schedule_id                 VARCHAR(64) PRIMARY KEY,
    depot_id                    VARCHAR(64) NOT NULL,
    timepoint_day               INTEGER     NOT NULL,
    cumulative_release_fraction DOUBLE      NOT NULL,
    instantaneous_rate_mg_day   DOUBLE      NOT NULL,
    remaining_reserve_mg        DOUBLE      NOT NULL,
    FOREIGN KEY (depot_id) REFERENCES weaver_depot_formulations(depot_id) ON DELETE CASCADE
);

CREATE INDEX idx_schedule_depot_id ON weaver_depot_release_schedules (depot_id);
";
        }

        return @"
CREATE TABLE IF NOT EXISTS weaver_depot_formulations (
    depot_id                   TEXT PRIMARY KEY,
    patient_id                 TEXT NOT NULL,
    regimen                    TEXT NOT NULL,
    polymer_matrix_type        TEXT NOT NULL,
    initial_nanocarrier_mass_mg REAL NOT NULL,
    daily_erosion_rate_percent  REAL NOT NULL,
    release_kinetics_model     TEXT NOT NULL,
    target_horizon_days        INTEGER NOT NULL DEFAULT 180,
    created_at_utc             TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_depot_patient_id ON weaver_depot_formulations (patient_id);

CREATE TABLE IF NOT EXISTS weaver_depot_release_schedules (
    schedule_id                 TEXT PRIMARY KEY,
    depot_id                    TEXT NOT NULL REFERENCES weaver_depot_formulations(depot_id) ON DELETE CASCADE,
    timepoint_day               INTEGER NOT NULL,
    cumulative_release_fraction REAL NOT NULL,
    instantaneous_rate_mg_day   REAL NOT NULL,
    remaining_reserve_mg        REAL NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_schedule_depot_id ON weaver_depot_release_schedules (depot_id);
";
    }
}
