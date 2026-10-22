using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NanoWeaverHrt.Abstractions;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Database.Migrations;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Models;
using Npgsql;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Dual-engine PostgreSQL and SQLite repository for patient-associated digitized DNA blueprints,
/// imported sequence files/datasets, and analytical simulation/endocrine results.
/// </summary>
public sealed class PatientDnaRepository : IPatientDnaRepository, IDisposable
{
    /// <summary>
    /// Thread synchronization lock for coordinating database mutations and connection acquisitions.
    /// </summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Path to local SQLite database file used as fallback or sovereign node storage.
    /// </summary>
    private readonly string _sqlitePath;

    /// <summary>
    /// PostgreSQL connection string.
    /// </summary>
    private readonly string _postgresConnString;

    /// <summary>
    /// Configuration repository used to discover active backend name.
    /// </summary>
    private readonly IConfigurationRepository _configRepo;

    /// <summary>
    /// Migration runner ensuring database schema versioning.
    /// </summary>
    private readonly DatabaseMigrationRunner _migrationRunner;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatientDnaRepository"/> class.
    /// </summary>
    /// <param name="configuration">Host application configuration.</param>
    /// <param name="configRepo">Authoritative configuration repository.</param>
    /// <param name="migrationRunner">Optional explicit database migration runner.</param>
    public PatientDnaRepository(
        IConfiguration configuration,
        IConfigurationRepository configRepo,
        DatabaseMigrationRunner? migrationRunner = null)
    {
        // WHAT: Initializes database paths, PostgreSQL connection credentials, and migration runners.
        // WHY:  Prepares the repository to interact transparently with either PostgreSQL or SQLite.
        // HOW:  Reads configuration settings with safe directory verification and defaults.
        ArgumentNullException.ThrowIfNull(configuration);
        _configRepo = configRepo ?? throw new ArgumentNullException(nameof(configRepo));
        _migrationRunner = migrationRunner ?? new DatabaseMigrationRunner();

        string? envPostgres = configuration["POSTGRES_CONNECTION_STRING"] ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
        _postgresConnString = envPostgres ?? "Host=localhost;Port=5432;Database=weaver_hrt;Username=weaver;Password=weaver_secret_change_me;Timeout=3";

        string incomingDir = configuration["TRAJECTORY_INCOMING_DIR"] ?? Path.Combine(Directory.GetCurrentDirectory(), "simulations", "shared-data");
        Directory.CreateDirectory(incomingDir);
        _sqlitePath = Path.Combine(incomingDir, "weaver_config.db");
    }

    /// <summary>
    /// Creates an open database connection appropriate for the active configuration backend.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A tuple containing the open connection and dialect string ("postgresql" or "sqlite").</returns>
    private async Task<(DbConnection Conn, string Dialect)> OpenConnectionAsync(CancellationToken ct)
    {
        // WHAT: Discovers the active storage engine from IConfigurationRepository and opens a database connection.
        // WHY:  Allows seamless execution across clustered PostgreSQL and standalone SQLite nodes.
        // HOW:  Attempts PostgreSQL if active backend indicates pg-duckdb; falls back to local SQLite on failure.
        bool isPostgres = _configRepo.ActiveBackendName.StartsWith("PostgreSQL", StringComparison.OrdinalIgnoreCase);
        if (isPostgres)
        {
            try
            {
                NpgsqlConnection pgConn = new(_postgresConnString);
                await pgConn.OpenAsync(ct);
                await _migrationRunner.ApplyPendingMigrationsAsync(pgConn, "postgresql", ct);
                return (pgConn, "postgresql");
            }
            catch
            {
                // Fall back to SQLite if PostgreSQL fails
            }
        }

        SqliteConnection sqliteConn = new($"Data Source={_sqlitePath}");
        await sqliteConn.OpenAsync(ct);
        await _migrationRunner.ApplyPendingMigrationsAsync(sqliteConn, "sqlite", ct);
        return (sqliteConn, "sqlite");
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PatientDigitalDnaRecord>> GetDnaRecordsAsync(string username, CancellationToken ct = default)
    {
        // WHAT: Queries weaver_patient_dna_records table for all designs assigned to a patient.
        // WHY:  Enables patients and researchers to inspect fabricated and planned DNA nanocarrier blueprints.
        // HOW:  Executes SELECT parameterized query ordered by created_at_utc descending.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        await _lock.WaitAsync(ct);
        try
        {
            var (conn, _) = await OpenConnectionAsync(ct);
            await using (conn)
            {
                await using DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, username, title, regimen, target_tissue, active_ligand,
                           nanocarrier_geometry, target_aptamer_kd_nm, scaffold_fasta,
                           gc_percentage, estimated_mw_kda, staples_json, annealing_protocol_json,
                           provenance_sha256, created_at_utc, notes
                    FROM weaver_patient_dna_records
                    WHERE username = @u
                    ORDER BY created_at_utc DESC;";

                DbParameter p = cmd.CreateParameter();
                p.ParameterName = "@u";
                p.Value = username;
                cmd.Parameters.Add(p);

                List<PatientDigitalDnaRecord> list = [];
                await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    list.Add(MapDnaRecordFromReader(reader));
                }

                return list;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<PatientDigitalDnaRecord?> GetDnaRecordByIdAsync(string id, CancellationToken ct = default)
    {
        // WHAT: Retrieves a specific digitized DNA blueprint by its unique identifier.
        // WHY:  Allows retrieval and verification of individual nanocarrier blueprints.
        // HOW:  Executes SELECT parameterized query by id.
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await _lock.WaitAsync(ct);
        try
        {
            var (conn, _) = await OpenConnectionAsync(ct);
            await using (conn)
            {
                await using DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, username, title, regimen, target_tissue, active_ligand,
                           nanocarrier_geometry, target_aptamer_kd_nm, scaffold_fasta,
                           gc_percentage, estimated_mw_kda, staples_json, annealing_protocol_json,
                           provenance_sha256, created_at_utc, notes
                    FROM weaver_patient_dna_records
                    WHERE id = @id;";

                DbParameter p = cmd.CreateParameter();
                p.ParameterName = "@id";
                p.Value = id;
                cmd.Parameters.Add(p);

                await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    return MapDnaRecordFromReader(reader);
                }

                return null;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task UpsertDnaRecordAsync(PatientDigitalDnaRecord record, CancellationToken ct = default)
    {
        // WHAT: Inserts or updates a digitized DNA blueprint record for a patient.
        // WHY:  Persists nanocarrier designs, staple routings, and annealing schedules.
        // HOW:  Serializes staples and protocols to JSON and executes an idempotent upsert statement.
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Username);

        await _lock.WaitAsync(ct);
        try
        {
            var (conn, dialect) = await OpenConnectionAsync(ct);
            await using (conn)
            {
                string staplesJson = JsonSerializer.Serialize(record.Staples ?? []);
                string annealingJson = JsonSerializer.Serialize(record.AnnealingProtocol ?? []);

                await using DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO weaver_patient_dna_records (
                        id, username, title, regimen, target_tissue, active_ligand,
                        nanocarrier_geometry, target_aptamer_kd_nm, scaffold_fasta,
                        gc_percentage, estimated_mw_kda, staples_json, annealing_protocol_json,
                        provenance_sha256, created_at_utc, notes
                    ) VALUES (
                        @id, @u, @tit, @reg, @tt, @al,
                        @ng, @kd, @fa,
                        @gc, @mw, @stap, @ann,
                        @sha, @cat, @not
                    )
                    ON CONFLICT(id) DO UPDATE SET
                        username = @u,
                        title = @tit,
                        regimen = @reg,
                        target_tissue = @tt,
                        active_ligand = @al,
                        nanocarrier_geometry = @ng,
                        target_aptamer_kd_nm = @kd,
                        scaffold_fasta = @fa,
                        gc_percentage = @gc,
                        estimated_mw_kda = @mw,
                        staples_json = @stap,
                        annealing_protocol_json = @ann,
                        provenance_sha256 = @sha,
                        notes = @not;";

                AddParam(cmd, "@id", record.Id);
                AddParam(cmd, "@u", record.Username);
                AddParam(cmd, "@tit", record.Title);
                AddParam(cmd, "@reg", record.Regimen.ToString());
                AddParam(cmd, "@tt", record.TargetTissue);
                AddParam(cmd, "@al", record.ActiveLigand);
                AddParam(cmd, "@ng", record.NanocarrierGeometry);
                AddParam(cmd, "@kd", record.TargetAptamerKdNm);
                AddParam(cmd, "@fa", record.ScaffoldFasta);
                AddParam(cmd, "@gc", record.GcPercentage);
                AddParam(cmd, "@mw", record.EstimatedMwKDa);
                AddParam(cmd, "@stap", staplesJson);
                AddParam(cmd, "@ann", annealingJson);
                AddParam(cmd, "@sha", record.ProvenanceSha256);
                AddParam(cmd, "@cat", dialect == "postgresql" ? (object)record.CreatedAtUtc.UtcDateTime : record.CreatedAtUtc.ToString("O"));
                AddParam(cmd, "@not", (object?)record.Notes ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteDnaRecordAsync(string id, CancellationToken ct = default)
    {
        // WHAT: Deletes a digitized DNA blueprint record by id.
        // WHY:  Allows researchers or patients to prune superseded or invalid blueprints.
        // HOW:  Executes DELETE parameterized query and checks rows affected.
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await _lock.WaitAsync(ct);
        try
        {
            var (conn, _) = await OpenConnectionAsync(ct);
            await using (conn)
            {
                await using DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM weaver_patient_dna_records WHERE id = @id;";
                AddParam(cmd, "@id", id);
                int rows = await cmd.ExecuteNonQueryAsync(ct);
                return rows > 0;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PatientDnaImportRecord>> GetImportsAsync(string username, CancellationToken ct = default)
    {
        // WHAT: Queries all imported DNA sequences and datasets associated with a patient.
        // WHY:  Supplies imported file assets to the DNA management interface and analysis pipeline.
        // HOW:  Executes SELECT parameterized query ordered by imported_at_utc descending.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        await _lock.WaitAsync(ct);
        try
        {
            var (conn, _) = await OpenConnectionAsync(ct);
            await using (conn)
            {
                await using DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, username, file_name, import_type, sequence_or_data,
                           size_bytes, sha256_checksum, imported_by, status,
                           metadata_json, imported_at_utc
                    FROM weaver_patient_dna_imports
                    WHERE username = @u
                    ORDER BY imported_at_utc DESC;";

                AddParam(cmd, "@u", username);

                List<PatientDnaImportRecord> list = [];
                await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    list.Add(MapImportRecordFromReader(reader));
                }

                return list;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<PatientDnaImportRecord?> GetImportByIdAsync(string id, CancellationToken ct = default)
    {
        // WHAT: Retrieves a specific import record by its unique identifier.
        // WHY:  Enables inspecting imported sequence data and verification metadata.
        // HOW:  Executes SELECT parameterized query by id.
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await _lock.WaitAsync(ct);
        try
        {
            var (conn, _) = await OpenConnectionAsync(ct);
            await using (conn)
            {
                await using DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, username, file_name, import_type, sequence_or_data,
                           size_bytes, sha256_checksum, imported_by, status,
                           metadata_json, imported_at_utc
                    FROM weaver_patient_dna_imports
                    WHERE id = @id;";

                AddParam(cmd, "@id", id);

                await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    return MapImportRecordFromReader(reader);
                }

                return null;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task UpsertImportAsync(PatientDnaImportRecord importRecord, CancellationToken ct = default)
    {
        // WHAT: Inserts or updates an imported sequence or dataset record for a patient.
        // WHY:  Maintains traceability of imported external genetic and molecular files.
        // HOW:  Executes an idempotent upsert statement mapping metadata and SHA-256 checksums.
        ArgumentNullException.ThrowIfNull(importRecord);
        ArgumentException.ThrowIfNullOrWhiteSpace(importRecord.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(importRecord.Username);

        await _lock.WaitAsync(ct);
        try
        {
            var (conn, dialect) = await OpenConnectionAsync(ct);
            await using (conn)
            {
                await using DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO weaver_patient_dna_imports (
                        id, username, file_name, import_type, sequence_or_data,
                        size_bytes, sha256_checksum, imported_by, status,
                        metadata_json, imported_at_utc
                    ) VALUES (
                        @id, @u, @fn, @it, @sod,
                        @sb, @sha, @ib, @st,
                        @mj, @iat
                    )
                    ON CONFLICT(id) DO UPDATE SET
                        username = @u,
                        file_name = @fn,
                        import_type = @it,
                        sequence_or_data = @sod,
                        size_bytes = @sb,
                        sha256_checksum = @sha,
                        imported_by = @ib,
                        status = @st,
                        metadata_json = @mj,
                        imported_at_utc = @iat;";

                AddParam(cmd, "@id", importRecord.Id);
                AddParam(cmd, "@u", importRecord.Username);
                AddParam(cmd, "@fn", importRecord.FileName);
                AddParam(cmd, "@it", importRecord.ImportType);
                AddParam(cmd, "@sod", importRecord.SequenceOrData);
                AddParam(cmd, "@sb", importRecord.SizeBytes);
                AddParam(cmd, "@sha", importRecord.Sha256Checksum);
                AddParam(cmd, "@ib", importRecord.ImportedBy);
                AddParam(cmd, "@st", importRecord.Status);
                AddParam(cmd, "@mj", importRecord.MetadataJson ?? "{}");
                AddParam(cmd, "@iat", dialect == "postgresql" ? (object)importRecord.ImportedAtUtc.UtcDateTime : importRecord.ImportedAtUtc.ToString("O"));

                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteImportAsync(string id, CancellationToken ct = default)
    {
        // WHAT: Deletes an imported sequence or dataset record by id.
        // WHY:  Allows cleaning up obsolete or incorrect imports.
        // HOW:  Executes DELETE parameterized query and checks rows affected.
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await _lock.WaitAsync(ct);
        try
        {
            var (conn, _) = await OpenConnectionAsync(ct);
            await using (conn)
            {
                await using DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM weaver_patient_dna_imports WHERE id = @id;";
                AddParam(cmd, "@id", id);
                int rows = await cmd.ExecuteNonQueryAsync(ct);
                return rows > 0;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PatientResultRecord>> GetResultsAsync(string username, CancellationToken ct = default)
    {
        // WHAT: Queries all simulation, endocrine, and pharmacogenomics results for a patient.
        // WHY:  Allows clinicians and patients to review personalized biophysical and kinetic findings.
        // HOW:  Executes SELECT parameterized query ordered by created_at_utc descending.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        await _lock.WaitAsync(ct);
        try
        {
            var (conn, _) = await OpenConnectionAsync(ct);
            await using (conn)
            {
                await using DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, username, dna_record_id, import_id, result_type,
                           title, status, metrics_json, summary, created_at_utc
                    FROM weaver_patient_results
                    WHERE username = @u
                    ORDER BY created_at_utc DESC;";

                AddParam(cmd, "@u", username);

                List<PatientResultRecord> list = [];
                await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    list.Add(MapResultRecordFromReader(reader));
                }

                return list;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<PatientResultRecord?> GetResultByIdAsync(string id, CancellationToken ct = default)
    {
        // WHAT: Retrieves a specific simulation or clinical result record by id.
        // WHY:  Enables detail rendering of a specific evaluation or simulation outcome.
        // HOW:  Executes SELECT parameterized query by id.
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await _lock.WaitAsync(ct);
        try
        {
            var (conn, _) = await OpenConnectionAsync(ct);
            await using (conn)
            {
                await using DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, username, dna_record_id, import_id, result_type,
                           title, status, metrics_json, summary, created_at_utc
                    FROM weaver_patient_results
                    WHERE id = @id;";

                AddParam(cmd, "@id", id);

                await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    return MapResultRecordFromReader(reader);
                }

                return null;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task UpsertResultAsync(PatientResultRecord result, CancellationToken ct = default)
    {
        // WHAT: Inserts or updates a simulation or clinical result record for a patient.
        // WHY:  Persists computed affinities, risks, kinetic models, and simulation yields.
        // HOW:  Executes an idempotent upsert statement mapping foreign references and metrics JSON.
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.Username);

        await _lock.WaitAsync(ct);
        try
        {
            var (conn, dialect) = await OpenConnectionAsync(ct);
            await using (conn)
            {
                await using DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO weaver_patient_results (
                        id, username, dna_record_id, import_id, result_type,
                        title, status, metrics_json, summary, created_at_utc
                    ) VALUES (
                        @id, @u, @dna, @imp, @rt,
                        @tit, @st, @mj, @sum, @cat
                    )
                    ON CONFLICT(id) DO UPDATE SET
                        username = @u,
                        dna_record_id = @dna,
                        import_id = @imp,
                        result_type = @rt,
                        title = @tit,
                        status = @st,
                        metrics_json = @mj,
                        summary = @sum,
                        created_at_utc = @cat;";

                AddParam(cmd, "@id", result.Id);
                AddParam(cmd, "@u", result.Username);
                AddParam(cmd, "@dna", (object?)result.DnaRecordId ?? DBNull.Value);
                AddParam(cmd, "@imp", (object?)result.ImportId ?? DBNull.Value);
                AddParam(cmd, "@rt", result.ResultType);
                AddParam(cmd, "@tit", result.Title);
                AddParam(cmd, "@st", result.Status);
                AddParam(cmd, "@mj", result.MetricsJson ?? "{}");
                AddParam(cmd, "@sum", result.Summary);
                AddParam(cmd, "@cat", dialect == "postgresql" ? (object)result.CreatedAtUtc.UtcDateTime : result.CreatedAtUtc.ToString("O"));

                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteResultAsync(string id, CancellationToken ct = default)
    {
        // WHAT: Deletes a simulation or clinical result record by id.
        // WHY:  Allows pruning superseded result snapshots.
        // HOW:  Executes DELETE parameterized query and checks rows affected.
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await _lock.WaitAsync(ct);
        try
        {
            var (conn, _) = await OpenConnectionAsync(ct);
            await using (conn)
            {
                await using DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM weaver_patient_results WHERE id = @id;";
                AddParam(cmd, "@id", id);
                int rows = await cmd.ExecuteNonQueryAsync(ct);
                return rows > 0;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Helper to attach a parameter to a database command.
    /// </summary>
    private static void AddParam(DbCommand cmd, string name, object value)
    {
        DbParameter param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value;
        cmd.Parameters.Add(param);
    }

    /// <summary>
    /// Maps a database reader row to a strongly-typed <see cref="PatientDigitalDnaRecord"/>.
    /// </summary>
    internal static PatientDigitalDnaRecord MapDnaRecordFromReader(DbDataReader reader)
    {
        string id = reader.GetString(0);
        string username = reader.GetString(1);
        string title = reader.GetString(2);
        string regimenStr = reader.GetString(3);
        string targetTissue = reader.GetString(4);
        string activeLigand = reader.GetString(5);
        string nanocarrierGeometry = reader.GetString(6);
        double targetKd = reader.GetDouble(7);
        string scaffoldFasta = reader.GetString(8);
        double gcPercentage = reader.GetDouble(9);
        double estimatedMwKDa = reader.GetDouble(10);
        string staplesJson = reader.GetString(11);
        string annealingJson = reader.GetString(12);
        string sha256 = reader.GetString(13);
        DateTimeOffset createdAt = ParseDateTimeOffset(reader, 14);
        string? notes = reader.IsDBNull(15) ? null : reader.GetString(15);

        Enum.TryParse(regimenStr, ignoreCase: true, out TherapeuticRegimen regimen);

        IReadOnlyList<StapleOligoRecord> staples = [];
        try
        {
            staples = JsonSerializer.Deserialize<List<StapleOligoRecord>>(staplesJson) ?? [];
        }
        catch
        {
            // Fallback for corrupted json
        }

        IReadOnlyList<AnnealingRampStep> protocol = [];
        try
        {
            protocol = JsonSerializer.Deserialize<List<AnnealingRampStep>>(annealingJson) ?? [];
        }
        catch
        {
            // Fallback for corrupted json
        }

        return new PatientDigitalDnaRecord
        {
            Id = id,
            Username = username,
            Title = title,
            Regimen = regimen,
            TargetTissue = targetTissue,
            ActiveLigand = activeLigand,
            NanocarrierGeometry = nanocarrierGeometry,
            TargetAptamerKdNm = targetKd,
            ScaffoldFasta = scaffoldFasta,
            GcPercentage = gcPercentage,
            EstimatedMwKDa = estimatedMwKDa,
            Staples = staples,
            AnnealingProtocol = protocol,
            ProvenanceSha256 = sha256,
            CreatedAtUtc = createdAt,
            Notes = notes
        };
    }

    /// <summary>
    /// Maps a database reader row to a strongly-typed <see cref="PatientDnaImportRecord"/>.
    /// </summary>
    internal static PatientDnaImportRecord MapImportRecordFromReader(DbDataReader reader)
    {
        string id = reader.GetString(0);
        string username = reader.GetString(1);
        string fileName = reader.GetString(2);
        string importType = reader.GetString(3);
        string sequenceOrData = reader.GetString(4);
        long sizeBytes = reader.GetInt64(5);
        string sha256 = reader.GetString(6);
        string importedBy = reader.GetString(7);
        string status = reader.GetString(8);
        string metadataJson = reader.GetString(9);
        DateTimeOffset importedAt = ParseDateTimeOffset(reader, 10);

        return new PatientDnaImportRecord
        {
            Id = id,
            Username = username,
            FileName = fileName,
            ImportType = importType,
            SequenceOrData = sequenceOrData,
            SizeBytes = sizeBytes,
            Sha256Checksum = sha256,
            ImportedBy = importedBy,
            Status = status,
            MetadataJson = metadataJson,
            ImportedAtUtc = importedAt
        };
    }

    /// <summary>
    /// Maps a database reader row to a strongly-typed <see cref="PatientResultRecord"/>.
    /// </summary>
    internal static PatientResultRecord MapResultRecordFromReader(DbDataReader reader)
    {
        string id = reader.GetString(0);
        string username = reader.GetString(1);
        string? dnaRecordId = reader.IsDBNull(2) ? null : reader.GetString(2);
        string? importId = reader.IsDBNull(3) ? null : reader.GetString(3);
        string resultType = reader.GetString(4);
        string title = reader.GetString(5);
        string status = reader.GetString(6);
        string metricsJson = reader.GetString(7);
        string summary = reader.GetString(8);
        DateTimeOffset createdAt = ParseDateTimeOffset(reader, 9);

        return new PatientResultRecord
        {
            Id = id,
            Username = username,
            DnaRecordId = dnaRecordId,
            ImportId = importId,
            ResultType = resultType,
            Title = title,
            Status = status,
            MetricsJson = metricsJson,
            Summary = summary,
            CreatedAtUtc = createdAt
        };
    }

    /// <summary>
    /// Parses a database column value into a <see cref="DateTimeOffset"/> handling both native timestamps and ISO 8601 strings.
    /// </summary>
    /// <param name="reader">The active database data reader.</param>
    /// <param name="index">Zero-based column ordinal.</param>
    /// <returns>The parsed datetime offset value.</returns>
    internal static DateTimeOffset ParseDateTimeOffset(DbDataReader reader, int index)
    {
        // WHAT: Normalizes temporal timestamps across heterogeneous database engines.
        // WHY:  PostgreSQL returns native DateTime objects, while SQLite returns ISO 8601 text strings.
        // HOW:  Inspects GetFieldType or fallback to GetString, parsing ISO 8601 strings into DateTimeOffset.
        if (reader.GetFieldType(index) == typeof(DateTime))
        {
            return new DateTimeOffset(reader.GetDateTime(index), TimeSpan.Zero);
        }

        return DateTimeOffset.Parse(reader.GetString(index), CultureInfo.InvariantCulture);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _lock.Dispose();
    }
}
