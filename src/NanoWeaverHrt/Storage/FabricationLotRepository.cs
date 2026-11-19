using System.Data.Common;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Multi-engine database repository for patient-specific wet-lab nanocarrier fabrication lots
/// and 5-gate pharmaceutical quality control measurements.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The relational persistence implementation of <see cref="IFabricationLotRepository"/>
/// built on <see cref="IWeaverDbConnectionFactory"/> for SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Provides deterministic, dialect-neutral storage for physical wet-lab batch records,
/// encrypted technician notes, and ML-DSA-65 release certificates with GDPR Article 17 patient erasure cascade support.
/// </para>
/// <para>
/// <strong>How it works:</strong> Uses <see cref="ISqlDialect.BuildUpsertSql"/>, parameterized commands via
/// <see cref="WeaverDbParameters"/>, explicit <c>await using</c> block scopes, and zero-allocation ordinal reader mapping.
/// </para>
/// </remarks>
public sealed class FabricationLotRepository : IFabricationLotRepository
{
    /// <summary>
    /// Column names for the <c>weaver_fabrication_lots</c> table.
    /// </summary>
    private static readonly string[] LotColumns =
    [
        "lot_id", "depot_id", "calibration_snapshot_id", "patient_id",
        "regimen", "polymer_matrix_type", "target_mass_mg", "target_erosion_rate_percent",
        "cpg_methylation_required", "opentrons_protocol_sha256", "lot_status",
        "certificate_payload_sha256", "certificate_signature_base64", "certificate_signer_key_id",
        "created_at_utc", "released_at_utc"
    ];

    /// <summary>
    /// Primary key conflict column for fabrication lot upserts.
    /// </summary>
    private static readonly string[] LotConflictColumns = ["lot_id"];

    /// <summary>
    /// Update column list for fabrication lot collision updates.
    /// </summary>
    private static readonly string[] LotUpdateColumns =
    [
        "depot_id", "calibration_snapshot_id", "patient_id",
        "regimen", "polymer_matrix_type", "target_mass_mg", "target_erosion_rate_percent",
        "cpg_methylation_required", "opentrons_protocol_sha256", "lot_status",
        "certificate_payload_sha256", "certificate_signature_base64", "certificate_signer_key_id",
        "created_at_utc", "released_at_utc"
    ];

    /// <summary>
    /// Parameter names corresponding to <see cref="LotColumns"/>.
    /// </summary>
    private static readonly string[] LotParameterNames =
    [
        "lid", "did", "csid", "pid",
        "reg", "poly", "mass", "eros",
        "cpg", "osha", "stat",
        "csha", "csig", "ckid",
        "cat", "rat"
    ];

    /// <summary>
    /// Column names for the <c>weaver_fabrication_qc_checks</c> table.
    /// </summary>
    private static readonly string[] QcColumns =
    [
        "check_id", "lot_id", "patient_id", "gate_type",
        "measured_value", "secondary_value", "threshold_value",
        "passed", "recorded_at_utc", "notes_encrypted"
    ];

    /// <summary>
    /// Primary key conflict column for QC check upserts.
    /// </summary>
    private static readonly string[] QcConflictColumns = ["check_id"];

    /// <summary>
    /// Update column list for QC check collision updates.
    /// </summary>
    private static readonly string[] QcUpdateColumns =
    [
        "lot_id", "patient_id", "gate_type",
        "measured_value", "secondary_value", "threshold_value",
        "passed", "recorded_at_utc", "notes_encrypted"
    ];

    /// <summary>
    /// Parameter names corresponding to <see cref="QcColumns"/>.
    /// </summary>
    private static readonly string[] QcParameterNames =
    [
        "cid", "lid", "pid", "gate",
        "mval", "sval", "tval",
        "pass", "rat", "notes"
    ];

    /// <summary>
    /// Database connection factory supplying open sessions and dialect helpers.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="FabricationLotRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">The database connection factory.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> is <see langword="null"/>.</exception>
    public FabricationLotRepository(IWeaverDbConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc/>
    public async Task<FabricationLotRecord> UpsertLotAsync(
        FabricationLotRecord lot,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Inserts or updates a wet-lab fabrication lot record in weaver_fabrication_lots.
        // WHY:  Persists newly minted adaptive batches as well as subsequent QC-pending, released, or quarantined transitions.
        // HOW:  Assigns a deterministic prefix lot ID when blank, builds dialect upsert SQL, and binds all typed parameters.
        ArgumentNullException.ThrowIfNull(lot);
        ArgumentException.ThrowIfNullOrWhiteSpace(lot.DepotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(lot.PatientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(lot.OpentronsProtocolSha256);

        string lotId = string.IsNullOrWhiteSpace(lot.LotId)
            ? $"lot_{Guid.NewGuid():N}"
            : lot.LotId;

        FabricationLotRecord normalized = lot with { LotId = lotId };

        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            string sql = session.Dialect.BuildUpsertSql(
                "weaver_fabrication_lots",
                LotColumns,
                LotConflictColumns,
                LotUpdateColumns,
                LotParameterNames);

            await using DbCommand cmd = session.CreateCommand(sql);
            WeaverDbParameters.Add(cmd, "lid", normalized.LotId);
            WeaverDbParameters.Add(cmd, "did", normalized.DepotId);
            WeaverDbParameters.AddNullable(cmd, "csid", normalized.CalibrationSnapshotId);
            WeaverDbParameters.Add(cmd, "pid", normalized.PatientId);
            WeaverDbParameters.Add(cmd, "reg", normalized.Regimen.ToString());
            WeaverDbParameters.Add(cmd, "poly", normalized.PolymerMatrixType);
            WeaverDbParameters.Add(cmd, "mass", normalized.TargetMassMg);
            WeaverDbParameters.Add(cmd, "eros", normalized.TargetErosionRatePercent);
            WeaverDbParameters.Add(cmd, "cpg", normalized.CpGMethylationRequired ? 1 : 0);
            WeaverDbParameters.Add(cmd, "osha", normalized.OpentronsProtocolSha256);
            WeaverDbParameters.Add(cmd, "stat", normalized.LotStatus.ToString());
            WeaverDbParameters.AddNullable(cmd, "csha", normalized.CertificatePayloadSha256);
            WeaverDbParameters.AddNullable(cmd, "csig", normalized.CertificateSignatureBase64);
            WeaverDbParameters.AddNullable(cmd, "ckid", normalized.CertificateSignerKeyId);
            WeaverDbParameters.Add(cmd, "cat", normalized.CreatedAtUtc.ToString("o"));
            WeaverDbParameters.AddNullable(cmd, "rat", normalized.ReleasedAtUtc?.ToString("o"));

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return normalized;
    }

    /// <inheritdoc/>
    public async Task<FabricationLotRecord?> GetLotByIdAsync(
        string lotId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Queries a single fabrication lot by its primary key.
        // WHY:  Supports QC recording, release evaluation, and Certificate of Analysis lookup endpoints.
        // HOW:  Executes a parameterized SELECT filtered by lot_id and maps the single row via MapLot.
        ArgumentException.ThrowIfNullOrWhiteSpace(lotId);

        FabricationLotRecord? lot = null;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT lot_id, depot_id, calibration_snapshot_id, patient_id, regimen, polymer_matrix_type, target_mass_mg, target_erosion_rate_percent, cpg_methylation_required, opentrons_protocol_sha256, lot_status, certificate_payload_sha256, certificate_signature_base64, certificate_signer_key_id, created_at_utc, released_at_utc FROM weaver_fabrication_lots WHERE lot_id = {session.Dialect.Parameter("lid")}");

            WeaverDbParameters.Add(cmd, "lid", lotId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                lot = MapLot(reader);
            }
        }

        return lot;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FabricationLotRecord>> GetLotsByPatientIdAsync(
        string patientId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Queries all wet-lab fabrication lots associated with a specific patient ordered newest first.
        // WHY:  Supplies the patient's historical and active batch ledger to APIs, desktop/mobile apps, and Depot Studio.
        // HOW:  Executes a parameterized SELECT filtered by patient_id ordered by created_at_utc DESC, lot_id DESC.
        ArgumentException.ThrowIfNullOrWhiteSpace(patientId);

        List<FabricationLotRecord> lots = [];
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT lot_id, depot_id, calibration_snapshot_id, patient_id, regimen, polymer_matrix_type, target_mass_mg, target_erosion_rate_percent, cpg_methylation_required, opentrons_protocol_sha256, lot_status, certificate_payload_sha256, certificate_signature_base64, certificate_signer_key_id, created_at_utc, released_at_utc FROM weaver_fabrication_lots WHERE patient_id = {session.Dialect.Parameter("pid")} ORDER BY created_at_utc DESC, lot_id DESC");

            WeaverDbParameters.Add(cmd, "pid", patientId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                lots.Add(MapLot(reader));
            }
        }

        return lots;
    }

    /// <inheritdoc/>
    public async Task<FabricationQcCheckRecord> RecordQcCheckAsync(
        FabricationQcCheckRecord check,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Persists a single pharmaceutical wet-lab QC measurement linked to a fabrication lot.
        // WHY:  Maintains an auditable laboratory log for all 5 release verification gates.
        // HOW:  Generates a prefixed check ID if blank, builds dialect upsert SQL, and executes parameterized command.
        ArgumentNullException.ThrowIfNull(check);
        ArgumentException.ThrowIfNullOrWhiteSpace(check.LotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(check.PatientId);

        string checkId = string.IsNullOrWhiteSpace(check.CheckId)
            ? $"qc_{Guid.NewGuid():N}"
            : check.CheckId;

        FabricationQcCheckRecord normalized = check with { CheckId = checkId };

        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            string sql = session.Dialect.BuildUpsertSql(
                "weaver_fabrication_qc_checks",
                QcColumns,
                QcConflictColumns,
                QcUpdateColumns,
                QcParameterNames);

            await using DbCommand cmd = session.CreateCommand(sql);
            WeaverDbParameters.Add(cmd, "cid", normalized.CheckId);
            WeaverDbParameters.Add(cmd, "lid", normalized.LotId);
            WeaverDbParameters.Add(cmd, "pid", normalized.PatientId);
            WeaverDbParameters.Add(cmd, "gate", normalized.GateType.ToString());
            WeaverDbParameters.Add(cmd, "mval", normalized.MeasuredValue);
            WeaverDbParameters.AddNullable(cmd, "sval", normalized.SecondaryValue);
            WeaverDbParameters.Add(cmd, "tval", normalized.ThresholdValue);
            WeaverDbParameters.Add(cmd, "pass", normalized.Passed ? 1 : 0);
            WeaverDbParameters.Add(cmd, "rat", normalized.RecordedAtUtc.ToString("o"));
            WeaverDbParameters.AddNullable(cmd, "notes", normalized.NotesEncrypted);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return normalized;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FabricationQcCheckRecord>> GetQcChecksByLotAsync(
        string lotId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Queries all wet-lab QC check measurements for a fabrication lot in chronological order.
        // WHY:  Allows the release engine to inspect the latest verified assay per required gate deterministically.
        // HOW:  Executes a parameterized SELECT filtered by lot_id ordered by recorded_at_utc ASC, check_id ASC.
        ArgumentException.ThrowIfNullOrWhiteSpace(lotId);

        List<FabricationQcCheckRecord> checks = [];
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT check_id, lot_id, patient_id, gate_type, measured_value, secondary_value, threshold_value, passed, recorded_at_utc, notes_encrypted FROM weaver_fabrication_qc_checks WHERE lot_id = {session.Dialect.Parameter("lid")} ORDER BY recorded_at_utc ASC, check_id ASC");

            WeaverDbParameters.Add(cmd, "lid", lotId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                checks.Add(MapQcCheck(reader));
            }
        }

        return checks;
    }

    /// <inheritdoc/>
    public async Task<int> DeleteByPatientIdAsync(
        string patientId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Deletes all wet-lab QC checks and fabrication lots belonging to a patient inside a transaction.
        // WHY:  Supports GDPR Article 17 right-to-erasure cascades across physical batch manufacturing records.
        // HOW:  Deletes child QC check rows first, then parent lot rows within a single atomic DbTransaction.
        ArgumentException.ThrowIfNullOrWhiteSpace(patientId);

        int deletedTotal = 0;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbTransaction tx = await session.Connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (DbCommand deleteChecks = session.CreateCommand(
                $"DELETE FROM weaver_fabrication_qc_checks WHERE patient_id = {session.Dialect.Parameter("pid")}"))
            {
                deleteChecks.Transaction = tx;
                WeaverDbParameters.Add(deleteChecks, "pid", patientId);
                deletedTotal += await deleteChecks.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (DbCommand deleteLots = session.CreateCommand(
                $"DELETE FROM weaver_fabrication_lots WHERE patient_id = {session.Dialect.Parameter("pid")}"))
            {
                deleteLots.Transaction = tx;
                WeaverDbParameters.Add(deleteLots, "pid", patientId);
                deletedTotal += await deleteLots.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return deletedTotal;
    }

    /// <summary>
    /// Maps the current row of a <see cref="DbDataReader"/> to a <see cref="FabricationLotRecord"/>.
    /// </summary>
    /// <param name="reader">Active database reader positioned on a fabrication lot row.</param>
    /// <returns>A populated <see cref="FabricationLotRecord"/> instance.</returns>
    private static FabricationLotRecord MapLot(DbDataReader reader)
    {
        // WHAT: Extracts column ordinals from weaver_fabrication_lots into a strongly typed record.
        // WHY:  Eliminates column-name hash lookups and safely handles nullable certificate metadata and enum parsing.
        // HOW:  Reads columns sequentially by ordinal index and applies safe fallbacks on unrecognized enum values.
        string regimenText = reader.GetString(4);
        TherapeuticRegimen regimen = Enum.TryParse(regimenText, ignoreCase: true, out TherapeuticRegimen parsedRegimen)
            ? parsedRegimen
            : TherapeuticRegimen.MaleToFemale;

        string statusText = reader.GetString(10);
        FabricationLotStatus status = Enum.TryParse(statusText, ignoreCase: true, out FabricationLotStatus parsedStatus)
            ? parsedStatus
            : FabricationLotStatus.Draft;

        return new FabricationLotRecord
        {
            LotId = reader.GetString(0),
            DepotId = reader.GetString(1),
            CalibrationSnapshotId = reader.IsDBNull(2) ? null : reader.GetString(2),
            PatientId = reader.GetString(3),
            Regimen = regimen,
            PolymerMatrixType = reader.GetString(5),
            TargetMassMg = reader.GetDouble(6),
            TargetErosionRatePercent = reader.GetDouble(7),
            CpGMethylationRequired = reader.GetInt32(8) != 0,
            OpentronsProtocolSha256 = reader.GetString(9),
            LotStatus = status,
            CertificatePayloadSha256 = reader.IsDBNull(11) ? null : reader.GetString(11),
            CertificateSignatureBase64 = reader.IsDBNull(12) ? null : reader.GetString(12),
            CertificateSignerKeyId = reader.IsDBNull(13) ? null : reader.GetString(13),
            CreatedAtUtc = WeaverDbValues.ReadTimestamp(reader, 14),
            ReleasedAtUtc = reader.IsDBNull(15) ? null : WeaverDbValues.ReadTimestamp(reader, 15)
        };
    }

    /// <summary>
    /// Maps the current row of a <see cref="DbDataReader"/> to a <see cref="FabricationQcCheckRecord"/>.
    /// </summary>
    /// <param name="reader">Active database reader positioned on a wet-lab QC check row.</param>
    /// <returns>A populated <see cref="FabricationQcCheckRecord"/> instance.</returns>
    private static FabricationQcCheckRecord MapQcCheck(DbDataReader reader)
    {
        // WHAT: Extracts column ordinals from weaver_fabrication_qc_checks into a strongly typed record.
        // WHY:  Centralizes QC check hydration and handles nullable secondary measurements and encrypted notes cleanly.
        // HOW:  Reads columns by ordinal index and parses GateType with fallback to GelFoldingYield.
        string gateText = reader.GetString(3);
        FabricationQcGateType gateType = Enum.TryParse(gateText, ignoreCase: true, out FabricationQcGateType parsedGate)
            ? parsedGate
            : FabricationQcGateType.GelFoldingYield;

        return new FabricationQcCheckRecord
        {
            CheckId = reader.GetString(0),
            LotId = reader.GetString(1),
            PatientId = reader.GetString(2),
            GateType = gateType,
            MeasuredValue = reader.GetDouble(4),
            SecondaryValue = reader.IsDBNull(5) ? null : reader.GetDouble(5),
            ThresholdValue = reader.GetDouble(6),
            Passed = reader.GetInt32(7) != 0,
            RecordedAtUtc = WeaverDbValues.ReadTimestamp(reader, 8),
            NotesEncrypted = reader.IsDBNull(9) ? null : reader.GetString(9)
        };
    }
}
