using System.Data.Common;
using System.Text.Json;
using NanoWeaverHrt.Abstractions;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Dual-engine PostgreSQL and SQLite repository for patient-associated digitized DNA blueprints,
/// imported sequence files/datasets, and analytical simulation/endocrine results.
/// </summary>
/// <remarks>
/// Methods that produce a value assign it to a local and return it after the try/finally instead of returning
/// from inside the try. Returning from inside a block that holds `await using` locals makes the compiler emit
/// one never-executed closing-brace line per async state machine, which the un-excluded coverage ratchet counts.
/// </remarks>
public sealed class PatientDnaRepository : IPatientDnaRepository, IDisposable
{
    /// <summary>
    /// Thread synchronization lock for coordinating database mutations and connection acquisitions.
    /// </summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Connection seam that decides which database engine each call talks to.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Protector used to seal and unseal sensitive patient DNA regimen and notes columns.
    /// </summary>
    private readonly ProfileDataProtector _protector;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatientDnaRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">Factory that opens connections to the selected backend.</param>
    /// <param name="protector">Protector used to seal and unseal sensitive patient DNA regimen and notes columns.</param>
    public PatientDnaRepository(IWeaverDbConnectionFactory connectionFactory, ProfileDataProtector protector)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PatientDigitalDnaRecord>> GetDnaRecordsAsync(string username, CancellationToken ct = default)
    {
        // WHAT: Queries weaver_patient_dna_records table for all designs assigned to a patient.
        // WHY:  Enables patients and researchers to inspect fabricated and planned DNA nanocarrier blueprints.
        // HOW:  Executes SELECT parameterized query ordered by created_at_utc descending.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        await _lock.WaitAsync(ct);
        IReadOnlyList<PatientDigitalDnaRecord> result = [];
        try
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
            await using DbCommand cmd = session.Connection.CreateCommand();
            cmd.CommandText = @"
                SELECT id, username, title, regimen, target_tissue, active_ligand,
                       nanocarrier_geometry, target_aptamer_kd_nm, scaffold_fasta,
                       gc_percentage, estimated_mw_kda, staples_json, annealing_protocol_json,
                       provenance_sha256, created_at_utc, notes
                FROM weaver_patient_dna_records
                WHERE username = @u
                ORDER BY created_at_utc DESC;";

            WeaverDbParameters.Add(cmd, "@u", username);

            List<PatientDigitalDnaRecord> list = [];
            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(MapDnaRecordFromReader(reader));
            }

            result = list;
        }
        finally
        {
            _lock.Release();
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<PatientDigitalDnaRecord?> GetDnaRecordByIdAsync(string id, CancellationToken ct = default)
    {
        // WHAT: Retrieves a specific digitized DNA blueprint by its unique identifier.
        // WHY:  Allows retrieval and verification of individual nanocarrier blueprints.
        // HOW:  Executes SELECT parameterized query by id.
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await _lock.WaitAsync(ct);
        PatientDigitalDnaRecord? result = null;
        try
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
            await using DbCommand cmd = session.Connection.CreateCommand();
            cmd.CommandText = @"
                SELECT id, username, title, regimen, target_tissue, active_ligand,
                       nanocarrier_geometry, target_aptamer_kd_nm, scaffold_fasta,
                       gc_percentage, estimated_mw_kda, staples_json, annealing_protocol_json,
                       provenance_sha256, created_at_utc, notes
                FROM weaver_patient_dna_records
                WHERE id = @id;";

            WeaverDbParameters.Add(cmd, "@id", id);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                result = MapDnaRecordFromReader(reader);
            }
        }
        finally
        {
            _lock.Release();
        }

        return result;
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
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
            string staplesJson = JsonSerializer.Serialize(record.Staples ?? []);
            string annealingJson = JsonSerializer.Serialize(record.AnnealingProtocol ?? []);

            // WHY !: record.Regimen.ToString() is a non-null enum string, so EncryptField returns a non-null envelope string.
            string encryptedRegimen = _protector.EncryptField(record.Regimen.ToString())!;
            string? encryptedNotes = string.IsNullOrEmpty(record.Notes) ? record.Notes : _protector.EncryptField(record.Notes);

            await using DbCommand cmd = session.Connection.CreateCommand();
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

            WeaverDbParameters.Add(cmd, "@id", record.Id);
            WeaverDbParameters.Add(cmd, "@u", record.Username);
            WeaverDbParameters.Add(cmd, "@tit", record.Title);
            WeaverDbParameters.Add(cmd, "@reg", encryptedRegimen);
            WeaverDbParameters.Add(cmd, "@tt", record.TargetTissue);
            WeaverDbParameters.Add(cmd, "@al", record.ActiveLigand);
            WeaverDbParameters.Add(cmd, "@ng", record.NanocarrierGeometry);
            WeaverDbParameters.Add(cmd, "@kd", record.TargetAptamerKdNm);
            WeaverDbParameters.Add(cmd, "@fa", record.ScaffoldFasta);
            WeaverDbParameters.Add(cmd, "@gc", record.GcPercentage);
            WeaverDbParameters.Add(cmd, "@mw", record.EstimatedMwKDa);
            WeaverDbParameters.Add(cmd, "@stap", staplesJson);
            WeaverDbParameters.Add(cmd, "@ann", annealingJson);
            WeaverDbParameters.Add(cmd, "@sha", record.ProvenanceSha256);
            WeaverDbParameters.Add(cmd, "@cat", session.Dialect.TimestampValue(record.CreatedAtUtc));
            WeaverDbParameters.AddNullable(cmd, "@not", encryptedNotes);

            await cmd.ExecuteNonQueryAsync(ct);
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
        bool result = false;
        try
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
            await using DbCommand cmd = session.Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM weaver_patient_dna_records WHERE id = @id;";
            WeaverDbParameters.Add(cmd, "@id", id);
            int rows = await cmd.ExecuteNonQueryAsync(ct);
            result = rows > 0;
        }
        finally
        {
            _lock.Release();
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PatientDnaImportRecord>> GetImportsAsync(string username, CancellationToken ct = default)
    {
        // WHAT: Queries all imported DNA sequences and datasets associated with a patient.
        // WHY:  Supplies imported file assets to the DNA management interface and analysis pipeline.
        // HOW:  Executes SELECT parameterized query ordered by imported_at_utc descending.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        await _lock.WaitAsync(ct);
        IReadOnlyList<PatientDnaImportRecord> result = [];
        try
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
            await using DbCommand cmd = session.Connection.CreateCommand();
            cmd.CommandText = @"
                SELECT id, username, file_name, import_type, sequence_or_data,
                       size_bytes, sha256_checksum, imported_by, status,
                       metadata_json, imported_at_utc
                FROM weaver_patient_dna_imports
                WHERE username = @u
                ORDER BY imported_at_utc DESC;";

            WeaverDbParameters.Add(cmd, "@u", username);

            List<PatientDnaImportRecord> list = [];
            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(MapImportRecordFromReader(reader));
            }

            result = list;
        }
        finally
        {
            _lock.Release();
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<PatientDnaImportRecord?> GetImportByIdAsync(string id, CancellationToken ct = default)
    {
        // WHAT: Retrieves a specific import record by its unique identifier.
        // WHY:  Enables inspecting imported sequence data and verification metadata.
        // HOW:  Executes SELECT parameterized query by id.
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await _lock.WaitAsync(ct);
        PatientDnaImportRecord? result = null;
        try
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
            await using DbCommand cmd = session.Connection.CreateCommand();
            cmd.CommandText = @"
                SELECT id, username, file_name, import_type, sequence_or_data,
                       size_bytes, sha256_checksum, imported_by, status,
                       metadata_json, imported_at_utc
                FROM weaver_patient_dna_imports
                WHERE id = @id;";

            WeaverDbParameters.Add(cmd, "@id", id);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                result = MapImportRecordFromReader(reader);
            }
        }
        finally
        {
            _lock.Release();
        }

        return result;
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
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
            await using DbCommand cmd = session.Connection.CreateCommand();
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

            WeaverDbParameters.Add(cmd, "@id", importRecord.Id);
            WeaverDbParameters.Add(cmd, "@u", importRecord.Username);
            WeaverDbParameters.Add(cmd, "@fn", importRecord.FileName);
            WeaverDbParameters.Add(cmd, "@it", importRecord.ImportType);
            WeaverDbParameters.Add(cmd, "@sod", importRecord.SequenceOrData);
            WeaverDbParameters.Add(cmd, "@sb", importRecord.SizeBytes);
            WeaverDbParameters.Add(cmd, "@sha", importRecord.Sha256Checksum);
            WeaverDbParameters.Add(cmd, "@ib", importRecord.ImportedBy);
            WeaverDbParameters.Add(cmd, "@st", importRecord.Status);
            WeaverDbParameters.Add(cmd, "@mj", importRecord.MetadataJson ?? "{}");
            WeaverDbParameters.Add(cmd, "@iat", session.Dialect.TimestampValue(importRecord.ImportedAtUtc));

            await cmd.ExecuteNonQueryAsync(ct);
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
        bool result = false;
        try
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
            await using DbCommand cmd = session.Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM weaver_patient_dna_imports WHERE id = @id;";
            WeaverDbParameters.Add(cmd, "@id", id);
            int rows = await cmd.ExecuteNonQueryAsync(ct);
            result = rows > 0;
        }
        finally
        {
            _lock.Release();
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PatientResultRecord>> GetResultsAsync(string username, CancellationToken ct = default)
    {
        // WHAT: Queries all simulation, endocrine, and pharmacogenomics results for a patient.
        // WHY:  Allows clinicians and patients to review personalized biophysical and kinetic findings.
        // HOW:  Executes SELECT parameterized query ordered by created_at_utc descending.
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        await _lock.WaitAsync(ct);
        IReadOnlyList<PatientResultRecord> result = [];
        try
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
            await using DbCommand cmd = session.Connection.CreateCommand();
            cmd.CommandText = @"
                SELECT id, username, dna_record_id, import_id, result_type,
                       title, status, metrics_json, summary, created_at_utc
                FROM weaver_patient_results
                WHERE username = @u
                ORDER BY created_at_utc DESC;";

            WeaverDbParameters.Add(cmd, "@u", username);

            List<PatientResultRecord> list = [];
            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(MapResultRecordFromReader(reader));
            }

            result = list;
        }
        finally
        {
            _lock.Release();
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<PatientResultRecord?> GetResultByIdAsync(string id, CancellationToken ct = default)
    {
        // WHAT: Retrieves a specific simulation or clinical result record by id.
        // WHY:  Enables detail rendering of a specific evaluation or simulation outcome.
        // HOW:  Executes SELECT parameterized query by id.
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await _lock.WaitAsync(ct);
        PatientResultRecord? result = null;
        try
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
            await using DbCommand cmd = session.Connection.CreateCommand();
            cmd.CommandText = @"
                SELECT id, username, dna_record_id, import_id, result_type,
                       title, status, metrics_json, summary, created_at_utc
                FROM weaver_patient_results
                WHERE id = @id;";

            WeaverDbParameters.Add(cmd, "@id", id);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                result = MapResultRecordFromReader(reader);
            }
        }
        finally
        {
            _lock.Release();
        }

        return result;
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
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
            await using DbCommand cmd = session.Connection.CreateCommand();
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

            WeaverDbParameters.Add(cmd, "@id", result.Id);
            WeaverDbParameters.Add(cmd, "@u", result.Username);
            WeaverDbParameters.AddNullable(cmd, "@dna", result.DnaRecordId);
            WeaverDbParameters.AddNullable(cmd, "@imp", result.ImportId);
            WeaverDbParameters.Add(cmd, "@rt", result.ResultType);
            WeaverDbParameters.Add(cmd, "@tit", result.Title);
            WeaverDbParameters.Add(cmd, "@st", result.Status);
            WeaverDbParameters.Add(cmd, "@mj", result.MetricsJson ?? "{}");
            WeaverDbParameters.Add(cmd, "@sum", result.Summary);
            WeaverDbParameters.Add(cmd, "@cat", session.Dialect.TimestampValue(result.CreatedAtUtc));

            await cmd.ExecuteNonQueryAsync(ct);
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
        bool result = false;
        try
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);
            await using DbCommand cmd = session.Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM weaver_patient_results WHERE id = @id;";
            WeaverDbParameters.Add(cmd, "@id", id);
            int rows = await cmd.ExecuteNonQueryAsync(ct);
            result = rows > 0;
        }
        finally
        {
            _lock.Release();
        }

        return result;
    }

    /// <summary>
    /// Maps a database reader row to a strongly-typed <see cref="PatientDigitalDnaRecord"/>.
    /// </summary>
    /// <param name="reader">A reader positioned on the digital DNA row to read.</param>
    /// <returns>The stored nanocarrier design described by the current row.</returns>
    private PatientDigitalDnaRecord MapDnaRecordFromReader(DbDataReader reader)
    {
        string id = reader.GetString(0);
        string username = reader.GetString(1);
        string title = reader.GetString(2);
        string rawRegimen = _protector.DecryptDataUri(reader.GetString(3));
        Enum.TryParse(rawRegimen, ignoreCase: true, out TherapeuticRegimen regimen);
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
        DateTimeOffset createdAt = WeaverDbValues.ReadTimestamp(reader, 14);
        string? rawNotes = reader.IsDBNull(15) ? null : reader.GetString(15);
        string? notes = rawNotes is null ? null : _protector.DecryptDataUri(rawNotes);

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
    /// <param name="reader">A reader positioned on the import row to read.</param>
    /// <returns>The record of an uploaded sequence or dataset described by the current row.</returns>
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
        DateTimeOffset importedAt = WeaverDbValues.ReadTimestamp(reader, 10);

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
    /// <param name="reader">A reader positioned on the result row to read.</param>
    /// <returns>The simulation or analysis result described by the current row.</returns>
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
        DateTimeOffset createdAt = WeaverDbValues.ReadTimestamp(reader, 9);

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

    /// <inheritdoc/>
    public void Dispose()
    {
        _lock.Dispose();
    }
}
