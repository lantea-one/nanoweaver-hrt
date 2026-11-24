namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 022: Establishes multi-year coupled hydrogel-to-epigenetic endocrine durability persistence
/// (<c>weaver_multiyear_homeostasis_runs</c> and <c>weaver_multiyear_homeostasis_checkpoints</c>)
/// across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB database engines.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Relational schema evolution storing 1–10 year mitotic cell-turnover durability
/// simulation headers and annual biomarker/methylation checkpoints linked to semi-annual depot formulations.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 43 (P225): Enables durable longitudinal homeostasis tracking across all
/// four supported SQL backends with patient-scoped GDPR Article 17 cascading cleanup.
/// </para>
/// <para>
/// <strong>How it works:</strong> Emits dialect-specific DDL for PostgreSQL, DuckDB (omitting unsupported
/// <c>ON DELETE CASCADE</c>), MySQL/MariaDB (named foreign key constraint and non-IF-NOT-EXISTS indexes), and SQLite.
/// </para>
/// </remarks>
public sealed class Migration022_MultiYearHomeostasisForecasts : DatabaseMigrationBase
{
    /// <inheritdoc/>
    public override long Version => 22;

    /// <inheritdoc/>
    public override string Name => "022_MultiYearHomeostasisForecasts";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits dialect-specific DDL creating weaver_multiyear_homeostasis_runs, checkpoints, and 4 indexes.
        // WHY:  Supports deterministic multi-year durability persistence across PostgreSQL, DuckDB, MySQL/MariaDB, and SQLite.
        // HOW:  Branches on dialect prefix for PostgreSQL, DuckDB, MySQL/MariaDB, defaulting to SQLite.
        ArgumentNullException.ThrowIfNull(dialect);

        if (dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_multiyear_homeostasis_runs (
    run_id                         VARCHAR(64)      PRIMARY KEY,
    depot_id                       VARCHAR(64)      NOT NULL,
    circuit_id                     VARCHAR(64)      NULL,
    patient_id                     VARCHAR(128)     NOT NULL,
    regimen                        VARCHAR(48)      NOT NULL,
    horizon_years                  INTEGER          NOT NULL,
    mitotic_fidelity_rate          DOUBLE PRECISION NOT NULL,
    transduced_progenitor_fraction DOUBLE PRECISION NOT NULL,
    projected_ten_year_ttr_pct     DOUBLE PRECISION NOT NULL,
    durability_status              VARCHAR(48)      NOT NULL,
    created_at_utc                 TIMESTAMPTZ      NOT NULL
);

CREATE TABLE IF NOT EXISTS weaver_multiyear_homeostasis_checkpoints (
    checkpoint_id                  VARCHAR(64)      PRIMARY KEY,
    run_id                         VARCHAR(64)      NOT NULL REFERENCES weaver_multiyear_homeostasis_runs(run_id) ON DELETE CASCADE,
    patient_id                     VARCHAR(128)     NOT NULL,
    year_index                     INTEGER          NOT NULL,
    estradiol_pmol_l               DOUBLE PRECISION NOT NULL,
    testosterone_nmol_l            DOUBLE PRECISION NOT NULL,
    lh_iu_l                        DOUBLE PRECISION NOT NULL,
    fsh_iu_l                       DOUBLE PRECISION NOT NULL,
    methylated_progenitor_fraction DOUBLE PRECISION NOT NULL,
    hydrogel_contribution_fraction DOUBLE PRECISION NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_weaver_multiyear_runs_depot ON weaver_multiyear_homeostasis_runs (depot_id, created_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_multiyear_runs_patient ON weaver_multiyear_homeostasis_runs (patient_id, created_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_multiyear_checkpoints_run ON weaver_multiyear_homeostasis_checkpoints (run_id, year_index);
CREATE INDEX IF NOT EXISTS idx_weaver_multiyear_checkpoints_patient ON weaver_multiyear_homeostasis_checkpoints (patient_id);
";
        }

        if (dialect.StartsWith("duckdb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_multiyear_homeostasis_runs (
    run_id                         VARCHAR(64)      PRIMARY KEY,
    depot_id                       VARCHAR(64)      NOT NULL,
    circuit_id                     VARCHAR(64)      NULL,
    patient_id                     VARCHAR(128)     NOT NULL,
    regimen                        VARCHAR(48)      NOT NULL,
    horizon_years                  INTEGER          NOT NULL,
    mitotic_fidelity_rate          DOUBLE PRECISION NOT NULL,
    transduced_progenitor_fraction DOUBLE PRECISION NOT NULL,
    projected_ten_year_ttr_pct     DOUBLE PRECISION NOT NULL,
    durability_status              VARCHAR(48)      NOT NULL,
    created_at_utc                 TIMESTAMP        NOT NULL
);

CREATE TABLE IF NOT EXISTS weaver_multiyear_homeostasis_checkpoints (
    checkpoint_id                  VARCHAR(64)      PRIMARY KEY,
    run_id                         VARCHAR(64)      NOT NULL REFERENCES weaver_multiyear_homeostasis_runs(run_id),
    patient_id                     VARCHAR(128)     NOT NULL,
    year_index                     INTEGER          NOT NULL,
    estradiol_pmol_l               DOUBLE PRECISION NOT NULL,
    testosterone_nmol_l            DOUBLE PRECISION NOT NULL,
    lh_iu_l                        DOUBLE PRECISION NOT NULL,
    fsh_iu_l                       DOUBLE PRECISION NOT NULL,
    methylated_progenitor_fraction DOUBLE PRECISION NOT NULL,
    hydrogel_contribution_fraction DOUBLE PRECISION NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_weaver_multiyear_runs_depot ON weaver_multiyear_homeostasis_runs (depot_id, created_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_multiyear_runs_patient ON weaver_multiyear_homeostasis_runs (patient_id, created_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_multiyear_checkpoints_run ON weaver_multiyear_homeostasis_checkpoints (run_id, year_index);
CREATE INDEX IF NOT EXISTS idx_weaver_multiyear_checkpoints_patient ON weaver_multiyear_homeostasis_checkpoints (patient_id);
";
        }

        if (dialect.StartsWith("mysql", StringComparison.OrdinalIgnoreCase) || dialect.StartsWith("mariadb", StringComparison.OrdinalIgnoreCase))
        {
            return @"
CREATE TABLE IF NOT EXISTS weaver_multiyear_homeostasis_runs (
    run_id                         VARCHAR(64)  NOT NULL PRIMARY KEY,
    depot_id                       VARCHAR(64)  NOT NULL,
    circuit_id                     VARCHAR(64)  NULL,
    patient_id                     VARCHAR(128) NOT NULL,
    regimen                        VARCHAR(48)  NOT NULL,
    horizon_years                  INT          NOT NULL,
    mitotic_fidelity_rate          DOUBLE       NOT NULL,
    transduced_progenitor_fraction DOUBLE       NOT NULL,
    projected_ten_year_ttr_pct     DOUBLE       NOT NULL,
    durability_status              VARCHAR(48)  NOT NULL,
    created_at_utc                 DATETIME(6)  NOT NULL
);

CREATE TABLE IF NOT EXISTS weaver_multiyear_homeostasis_checkpoints (
    checkpoint_id                  VARCHAR(64)  NOT NULL PRIMARY KEY,
    run_id                         VARCHAR(64)  NOT NULL,
    patient_id                     VARCHAR(128) NOT NULL,
    year_index                     INT          NOT NULL,
    estradiol_pmol_l               DOUBLE       NOT NULL,
    testosterone_nmol_l            DOUBLE       NOT NULL,
    lh_iu_l                        DOUBLE       NOT NULL,
    fsh_iu_l                       DOUBLE       NOT NULL,
    methylated_progenitor_fraction DOUBLE       NOT NULL,
    hydrogel_contribution_fraction DOUBLE       NOT NULL,
    CONSTRAINT fk_weaver_multiyear_checkpoints_run FOREIGN KEY (run_id) REFERENCES weaver_multiyear_homeostasis_runs(run_id) ON DELETE CASCADE
);

CREATE INDEX idx_weaver_multiyear_runs_depot ON weaver_multiyear_homeostasis_runs (depot_id, created_at_utc);
CREATE INDEX idx_weaver_multiyear_runs_patient ON weaver_multiyear_homeostasis_runs (patient_id, created_at_utc);
CREATE INDEX idx_weaver_multiyear_checkpoints_run ON weaver_multiyear_homeostasis_checkpoints (run_id, year_index);
CREATE INDEX idx_weaver_multiyear_checkpoints_patient ON weaver_multiyear_homeostasis_checkpoints (patient_id);
";
        }

        return @"
CREATE TABLE IF NOT EXISTS weaver_multiyear_homeostasis_runs (
    run_id                         TEXT PRIMARY KEY,
    depot_id                       TEXT NOT NULL,
    circuit_id                     TEXT NULL,
    patient_id                     TEXT NOT NULL,
    regimen                        TEXT NOT NULL,
    horizon_years                  INTEGER NOT NULL,
    mitotic_fidelity_rate          REAL NOT NULL,
    transduced_progenitor_fraction REAL NOT NULL,
    projected_ten_year_ttr_pct     REAL NOT NULL,
    durability_status              TEXT NOT NULL,
    created_at_utc                 TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS weaver_multiyear_homeostasis_checkpoints (
    checkpoint_id                  TEXT PRIMARY KEY,
    run_id                         TEXT NOT NULL REFERENCES weaver_multiyear_homeostasis_runs(run_id) ON DELETE CASCADE,
    patient_id                     TEXT NOT NULL,
    year_index                     INTEGER NOT NULL,
    estradiol_pmol_l               REAL NOT NULL,
    testosterone_nmol_l            REAL NOT NULL,
    lh_iu_l                        REAL NOT NULL,
    fsh_iu_l                       REAL NOT NULL,
    methylated_progenitor_fraction REAL NOT NULL,
    hydrogel_contribution_fraction REAL NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_weaver_multiyear_runs_depot ON weaver_multiyear_homeostasis_runs (depot_id, created_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_multiyear_runs_patient ON weaver_multiyear_homeostasis_runs (patient_id, created_at_utc);
CREATE INDEX IF NOT EXISTS idx_weaver_multiyear_checkpoints_run ON weaver_multiyear_homeostasis_checkpoints (run_id, year_index);
CREATE INDEX IF NOT EXISTS idx_weaver_multiyear_checkpoints_patient ON weaver_multiyear_homeostasis_checkpoints (patient_id);
";
    }
}
