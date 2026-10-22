namespace NanoWeaverHrt.Database.Migrations;

/// <summary>
/// Migration 004: Establishes schema tables and indexes for patient digitized DNA blueprint records,
/// imported DNA sequence/variant datasets, and personalized biophysical/simulation results.
/// </summary>
public sealed class Migration004_PatientDnaImportsAndResults : DatabaseMigrationBase
{
    /// <inheritdoc/>
    public override long Version => 4;

    /// <inheritdoc/>
    public override string Name => "004_PatientDnaImportsAndResults";

    /// <inheritdoc/>
    public override string GetSql(string dialect)
    {
        // WHAT: Emits DDL for patient DNA blueprints, sequence imports, and clinical/simulation results.
        // WHY:  Associates synthetic biology nanocarrier designs and analytical results directly with patient profiles.
        // HOW:  Tailors types and foreign keys for PostgreSQL vs SQLite with idempotent CREATE TABLE and CREATE INDEX statements.
        ArgumentNullException.ThrowIfNull(dialect);

        if (dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return @"
                CREATE TABLE IF NOT EXISTS weaver_patient_dna_records (
                    id VARCHAR(64) PRIMARY KEY,
                    username VARCHAR(128) NOT NULL REFERENCES weaver_users(username) ON DELETE CASCADE,
                    title VARCHAR(256) NOT NULL,
                    regimen VARCHAR(64) NOT NULL,
                    target_tissue VARCHAR(256) NOT NULL,
                    active_ligand VARCHAR(128) NOT NULL,
                    nanocarrier_geometry VARCHAR(128) NOT NULL,
                    target_aptamer_kd_nm DOUBLE PRECISION NOT NULL DEFAULT 3.5,
                    scaffold_fasta TEXT NOT NULL,
                    gc_percentage DOUBLE PRECISION NOT NULL DEFAULT 0.0,
                    estimated_mw_kda DOUBLE PRECISION NOT NULL DEFAULT 0.0,
                    staples_json TEXT NOT NULL DEFAULT '[]',
                    annealing_protocol_json TEXT NOT NULL DEFAULT '[]',
                    provenance_sha256 VARCHAR(64) NOT NULL,
                    created_at_utc TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
                    notes TEXT
                );

                CREATE TABLE IF NOT EXISTS weaver_patient_dna_imports (
                    id VARCHAR(64) PRIMARY KEY,
                    username VARCHAR(128) NOT NULL REFERENCES weaver_users(username) ON DELETE CASCADE,
                    file_name VARCHAR(256) NOT NULL,
                    import_type VARCHAR(64) NOT NULL,
                    sequence_or_data TEXT NOT NULL,
                    size_bytes BIGINT NOT NULL DEFAULT 0,
                    sha256_checksum VARCHAR(64) NOT NULL,
                    imported_by VARCHAR(128) NOT NULL,
                    status VARCHAR(64) NOT NULL DEFAULT 'Processed',
                    metadata_json TEXT NOT NULL DEFAULT '{}',
                    imported_at_utc TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC')
                );

                CREATE TABLE IF NOT EXISTS weaver_patient_results (
                    id VARCHAR(64) PRIMARY KEY,
                    username VARCHAR(128) NOT NULL REFERENCES weaver_users(username) ON DELETE CASCADE,
                    dna_record_id VARCHAR(64) REFERENCES weaver_patient_dna_records(id) ON DELETE SET NULL,
                    import_id VARCHAR(64) REFERENCES weaver_patient_dna_imports(id) ON DELETE SET NULL,
                    result_type VARCHAR(64) NOT NULL,
                    title VARCHAR(256) NOT NULL,
                    status VARCHAR(64) NOT NULL DEFAULT 'Complete',
                    metrics_json TEXT NOT NULL DEFAULT '{}',
                    summary TEXT NOT NULL,
                    created_at_utc TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC')
                );

                CREATE INDEX IF NOT EXISTS ix_weaver_patient_dna_records_username ON weaver_patient_dna_records(username);
                CREATE INDEX IF NOT EXISTS ix_weaver_patient_dna_records_created ON weaver_patient_dna_records(created_at_utc);

                CREATE INDEX IF NOT EXISTS ix_weaver_patient_dna_imports_username ON weaver_patient_dna_imports(username);
                CREATE INDEX IF NOT EXISTS ix_weaver_patient_dna_imports_imported ON weaver_patient_dna_imports(imported_at_utc);

                CREATE INDEX IF NOT EXISTS ix_weaver_patient_results_username ON weaver_patient_results(username);
                CREATE INDEX IF NOT EXISTS ix_weaver_patient_results_dna_record_id ON weaver_patient_results(dna_record_id);
                CREATE INDEX IF NOT EXISTS ix_weaver_patient_results_created ON weaver_patient_results(created_at_utc);
            ";
        }

        return @"
            CREATE TABLE IF NOT EXISTS weaver_patient_dna_records (
                id TEXT PRIMARY KEY,
                username TEXT NOT NULL REFERENCES weaver_users(username) ON DELETE CASCADE,
                title TEXT NOT NULL,
                regimen TEXT NOT NULL,
                target_tissue TEXT NOT NULL,
                active_ligand TEXT NOT NULL,
                nanocarrier_geometry TEXT NOT NULL,
                target_aptamer_kd_nm REAL NOT NULL DEFAULT 3.5,
                scaffold_fasta TEXT NOT NULL,
                gc_percentage REAL NOT NULL DEFAULT 0.0,
                estimated_mw_kda REAL NOT NULL DEFAULT 0.0,
                staples_json TEXT NOT NULL DEFAULT '[]',
                annealing_protocol_json TEXT NOT NULL DEFAULT '[]',
                provenance_sha256 TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                notes TEXT
            );

            CREATE TABLE IF NOT EXISTS weaver_patient_dna_imports (
                id TEXT PRIMARY KEY,
                username TEXT NOT NULL REFERENCES weaver_users(username) ON DELETE CASCADE,
                file_name TEXT NOT NULL,
                import_type TEXT NOT NULL,
                sequence_or_data TEXT NOT NULL,
                size_bytes INTEGER NOT NULL DEFAULT 0,
                sha256_checksum TEXT NOT NULL,
                imported_by TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'Processed',
                metadata_json TEXT NOT NULL DEFAULT '{}',
                imported_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS weaver_patient_results (
                id TEXT PRIMARY KEY,
                username TEXT NOT NULL REFERENCES weaver_users(username) ON DELETE CASCADE,
                dna_record_id TEXT REFERENCES weaver_patient_dna_records(id) ON DELETE SET NULL,
                import_id TEXT REFERENCES weaver_patient_dna_imports(id) ON DELETE SET NULL,
                result_type TEXT NOT NULL,
                title TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'Complete',
                metrics_json TEXT NOT NULL DEFAULT '{}',
                summary TEXT NOT NULL,
                created_at_utc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_weaver_patient_dna_records_username ON weaver_patient_dna_records(username);
            CREATE INDEX IF NOT EXISTS ix_weaver_patient_dna_records_created ON weaver_patient_dna_records(created_at_utc);

            CREATE INDEX IF NOT EXISTS ix_weaver_patient_dna_imports_username ON weaver_patient_dna_imports(username);
            CREATE INDEX IF NOT EXISTS ix_weaver_patient_dna_imports_imported ON weaver_patient_dna_imports(imported_at_utc);

            CREATE INDEX IF NOT EXISTS ix_weaver_patient_results_username ON weaver_patient_results(username);
            CREATE INDEX IF NOT EXISTS ix_weaver_patient_results_dna_record_id ON weaver_patient_results(dna_record_id);
            CREATE INDEX IF NOT EXISTS ix_weaver_patient_results_created ON weaver_patient_results(created_at_utc);
        ";
    }
}
