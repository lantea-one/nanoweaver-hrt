using System.Data.Common;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Multi-engine database repository for longitudinal depot serum hormone assay observations
/// and adaptive Bayesian calibration snapshots.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The relational persistence implementation of <see cref="IDepotAssayRepository"/>
/// built on <see cref="IWeaverDbConnectionFactory"/> for SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Provides deterministic, dialect-neutral storage for clinical serum assay timepoints
/// and calibration snapshots while supporting GDPR Article 17 patient-level erasure cascades.
/// </para>
/// <para>
/// <strong>How it works:</strong> Uses <see cref="ISqlDialect.BuildUpsertSql"/>, parameterized commands via
/// <see cref="WeaverDbParameters"/>, explicit <c>await using</c> block scopes, and zero-allocation reader mapping.
/// </para>
/// </remarks>
public sealed class DepotAssayRepository : IDepotAssayRepository
{
    /// <summary>
    /// Column names for the <c>weaver_depot_assay_observations</c> table.
    /// </summary>
    private static readonly string[] ObservationColumns =
    [
        "observation_id", "depot_id", "patient_id", "day_since_implant",
        "observed_e2_pmol_l", "observed_t_nmol_l", "observed_lh_iu_l",
        "observed_fsh_iu_l", "observed_shbg_nmol_l", "observed_hematocrit_pct",
        "recorded_at_utc", "notes_encrypted"
    ];

    /// <summary>
    /// Primary key conflict column for assay observation upserts.
    /// </summary>
    private static readonly string[] ObservationConflictColumns = ["observation_id"];

    /// <summary>
    /// Update column list for assay observation collision updates.
    /// </summary>
    private static readonly string[] ObservationUpdateColumns =
    [
        "depot_id", "patient_id", "day_since_implant",
        "observed_e2_pmol_l", "observed_t_nmol_l", "observed_lh_iu_l",
        "observed_fsh_iu_l", "observed_shbg_nmol_l", "observed_hematocrit_pct",
        "recorded_at_utc", "notes_encrypted"
    ];

    /// <summary>
    /// Parameter names corresponding to <see cref="ObservationColumns"/>.
    /// </summary>
    private static readonly string[] ObservationParameterNames =
    [
        "oid", "did", "pid", "day",
        "e2", "t", "lh",
        "fsh", "shbg", "hct",
        "rat", "notes"
    ];

    /// <summary>
    /// Column names for the <c>weaver_depot_calibration_snapshots</c> table.
    /// </summary>
    private static readonly string[] SnapshotColumns =
    [
        "snapshot_id", "depot_id", "patient_id", "inferred_clearance_multiplier",
        "inferred_erosion_rate_percent", "adjusted_redose_day", "recommended_next_mass_mg",
        "safety_status", "residual_rmse", "computed_at_utc"
    ];

    /// <summary>
    /// Primary key conflict column for calibration snapshot upserts.
    /// </summary>
    private static readonly string[] SnapshotConflictColumns = ["snapshot_id"];

    /// <summary>
    /// Update column list for calibration snapshot collision updates.
    /// </summary>
    private static readonly string[] SnapshotUpdateColumns =
    [
        "depot_id", "patient_id", "inferred_clearance_multiplier",
        "inferred_erosion_rate_percent", "adjusted_redose_day", "recommended_next_mass_mg",
        "safety_status", "residual_rmse", "computed_at_utc"
    ];

    /// <summary>
    /// Parameter names corresponding to <see cref="SnapshotColumns"/>.
    /// </summary>
    private static readonly string[] SnapshotParameterNames =
    [
        "sid", "did", "pid", "cl",
        "er", "rday", "mass",
        "stat", "rmse", "cat"
    ];

    /// <summary>
    /// Database connection factory supplying open sessions and dialect helpers.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="DepotAssayRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">The database connection factory.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> is <see langword="null"/>.</exception>
    public DepotAssayRepository(IWeaverDbConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc/>
    public async Task<DepotAssayObservationRecord> RecordObservationAsync(
        DepotAssayObservationRecord observation,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Inserts or updates a longitudinal serum hormone assay observation linked to a depot.
        // WHY:  Persists clinical bloodwork measurements for closed-loop Bayesian pharmacokinetic calibration.
        // HOW:  Generates an observation identifier if blank, builds dialect upsert SQL, and executes parameterized command.
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.DepotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.PatientId);

        string observationId = string.IsNullOrWhiteSpace(observation.ObservationId)
            ? $"assay_{Guid.NewGuid():N}"
            : observation.ObservationId;

        DepotAssayObservationRecord normalized = observation with { ObservationId = observationId };

        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            string sql = session.Dialect.BuildUpsertSql(
                "weaver_depot_assay_observations",
                ObservationColumns,
                ObservationConflictColumns,
                ObservationUpdateColumns,
                ObservationParameterNames);

            await using DbCommand cmd = session.CreateCommand(sql);
            WeaverDbParameters.Add(cmd, "oid", normalized.ObservationId);
            WeaverDbParameters.Add(cmd, "did", normalized.DepotId);
            WeaverDbParameters.Add(cmd, "pid", normalized.PatientId);
            WeaverDbParameters.Add(cmd, "day", normalized.DaySinceImplant);
            WeaverDbParameters.Add(cmd, "e2", normalized.ObservedE2PmolL);
            WeaverDbParameters.Add(cmd, "t", normalized.ObservedTNmolL);
            WeaverDbParameters.Add(cmd, "lh", normalized.ObservedLhIuL);
            WeaverDbParameters.Add(cmd, "fsh", normalized.ObservedFshIuL);
            WeaverDbParameters.Add(cmd, "shbg", normalized.ObservedShbgNmolL);
            WeaverDbParameters.Add(cmd, "hct", normalized.ObservedHematocritPct);
            WeaverDbParameters.Add(cmd, "rat", normalized.RecordedAtUtc.ToString("o"));
            WeaverDbParameters.AddNullable(cmd, "notes", normalized.NotesEncrypted);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return normalized;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DepotAssayObservationRecord>> GetObservationsByDepotAsync(
        string depotId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Queries all serum hormone assay observations for a depot formulation in chronological order.
        // WHY:  Supplies ordered longitudinal data points to the Bayesian calibration engine and clinical UI.
        // HOW:  Executes a parameterized SELECT filtered by depot_id ordered by day_since_implant ASC, recorded_at_utc ASC.
        ArgumentException.ThrowIfNullOrWhiteSpace(depotId);

        List<DepotAssayObservationRecord> observations = [];
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT observation_id, depot_id, patient_id, day_since_implant, observed_e2_pmol_l, observed_t_nmol_l, observed_lh_iu_l, observed_fsh_iu_l, observed_shbg_nmol_l, observed_hematocrit_pct, recorded_at_utc, notes_encrypted FROM weaver_depot_assay_observations WHERE depot_id = {session.Dialect.Parameter("did")} ORDER BY day_since_implant ASC, recorded_at_utc ASC");

            WeaverDbParameters.Add(cmd, "did", depotId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                observations.Add(MapObservation(reader));
            }
        }

        return observations;
    }

    /// <inheritdoc/>
    public async Task<DepotCalibrationSnapshotRecord> SaveCalibrationSnapshotAsync(
        DepotCalibrationSnapshotRecord snapshot,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Persists an adaptive Bayesian depot calibration snapshot.
        // WHY:  Maintains an auditable clinical record of inferred clearance, erosion, and re-dosing recommendations.
        // HOW:  Generates a snapshot identifier if blank, builds dialect upsert SQL, and executes parameterized command.
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.DepotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.PatientId);

        string snapshotId = string.IsNullOrWhiteSpace(snapshot.SnapshotId)
            ? $"calib_{Guid.NewGuid():N}"
            : snapshot.SnapshotId;

        DepotCalibrationSnapshotRecord normalized = snapshot with { SnapshotId = snapshotId };

        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            string sql = session.Dialect.BuildUpsertSql(
                "weaver_depot_calibration_snapshots",
                SnapshotColumns,
                SnapshotConflictColumns,
                SnapshotUpdateColumns,
                SnapshotParameterNames);

            await using DbCommand cmd = session.CreateCommand(sql);
            WeaverDbParameters.Add(cmd, "sid", normalized.SnapshotId);
            WeaverDbParameters.Add(cmd, "did", normalized.DepotId);
            WeaverDbParameters.Add(cmd, "pid", normalized.PatientId);
            WeaverDbParameters.Add(cmd, "cl", normalized.InferredClearanceMultiplier);
            WeaverDbParameters.Add(cmd, "er", normalized.InferredDailyErosionRatePercent);
            WeaverDbParameters.Add(cmd, "rday", normalized.AdjustedRedoseDay);
            WeaverDbParameters.Add(cmd, "mass", normalized.RecommendedNextMassMg);
            WeaverDbParameters.Add(cmd, "stat", normalized.SafetyStatus.ToString());
            WeaverDbParameters.Add(cmd, "rmse", normalized.ResidualRmse);
            WeaverDbParameters.Add(cmd, "cat", normalized.ComputedAtUtc.ToString("o"));

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return normalized;
    }

    /// <inheritdoc/>
    public async Task<DepotCalibrationSnapshotRecord?> GetLatestCalibrationSnapshotAsync(
        string depotId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Retrieves the most recently computed calibration snapshot for a depot formulation.
        // WHY:  Surfaces current adaptive re-dosing guidance without re-running parameter optimization on every page load.
        // HOW:  Executes a parameterized SELECT ordered by computed_at_utc DESC, snapshot_id DESC with LIMIT 1.
        ArgumentException.ThrowIfNullOrWhiteSpace(depotId);

        DepotCalibrationSnapshotRecord? latest = null;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT snapshot_id, depot_id, patient_id, inferred_clearance_multiplier, inferred_erosion_rate_percent, adjusted_redose_day, recommended_next_mass_mg, safety_status, residual_rmse, computed_at_utc FROM weaver_depot_calibration_snapshots WHERE depot_id = {session.Dialect.Parameter("did")} ORDER BY computed_at_utc DESC, snapshot_id DESC LIMIT 1");

            WeaverDbParameters.Add(cmd, "did", depotId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                latest = MapSnapshot(reader);
            }
        }

        return latest;
    }

    /// <inheritdoc/>
    public async Task<int> DeleteByPatientIdAsync(
        string patientId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Deletes all assay observations and calibration snapshots belonging to the specified patient.
        // WHY:  Supports GDPR Article 17 right-to-erasure cascades across longitudinal endocrine records.
        // HOW:  Executes two parameterized DELETE statements inside an atomic database transaction and sums affected rows.
        ArgumentException.ThrowIfNullOrWhiteSpace(patientId);

        int deletedTotal = 0;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbTransaction tx = await session.Connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (DbCommand deleteSnapshots = session.CreateCommand(
                $"DELETE FROM weaver_depot_calibration_snapshots WHERE patient_id = {session.Dialect.Parameter("pid")}"))
            {
                deleteSnapshots.Transaction = tx;
                WeaverDbParameters.Add(deleteSnapshots, "pid", patientId);
                deletedTotal += await deleteSnapshots.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (DbCommand deleteObservations = session.CreateCommand(
                $"DELETE FROM weaver_depot_assay_observations WHERE patient_id = {session.Dialect.Parameter("pid")}"))
            {
                deleteObservations.Transaction = tx;
                WeaverDbParameters.Add(deleteObservations, "pid", patientId);
                deletedTotal += await deleteObservations.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return deletedTotal;
    }

    /// <summary>
    /// Maps the current row of a <see cref="DbDataReader"/> to a <see cref="DepotAssayObservationRecord"/>.
    /// </summary>
    /// <param name="reader">Active database reader positioned on an observation row.</param>
    /// <returns>A populated <see cref="DepotAssayObservationRecord"/> instance.</returns>
    private static DepotAssayObservationRecord MapObservation(DbDataReader reader)
    {
        // WHAT: Extracts column ordinals from weaver_depot_assay_observations into a strongly typed record.
        // WHY:  Avoids string-lookup overhead and cleanly handles nullable encrypted clinical notes.
        // HOW:  Reads columns sequentially by ordinal index and parses recorded_at_utc via WeaverDbValues.
        string? notesEncrypted = reader.IsDBNull(11) ? null : reader.GetString(11);

        return new DepotAssayObservationRecord
        {
            ObservationId = reader.GetString(0),
            DepotId = reader.GetString(1),
            PatientId = reader.GetString(2),
            DaySinceImplant = reader.GetInt32(3),
            ObservedE2PmolL = reader.GetDouble(4),
            ObservedTNmolL = reader.GetDouble(5),
            ObservedLhIuL = reader.GetDouble(6),
            ObservedFshIuL = reader.GetDouble(7),
            ObservedShbgNmolL = reader.GetDouble(8),
            ObservedHematocritPct = reader.GetDouble(9),
            RecordedAtUtc = WeaverDbValues.ReadTimestamp(reader, 10),
            NotesEncrypted = notesEncrypted
        };
    }

    /// <summary>
    /// Maps the current row of a <see cref="DbDataReader"/> to a <see cref="DepotCalibrationSnapshotRecord"/>.
    /// </summary>
    /// <param name="reader">Active database reader positioned on a calibration snapshot row.</param>
    /// <returns>A populated <see cref="DepotCalibrationSnapshotRecord"/> instance.</returns>
    private static DepotCalibrationSnapshotRecord MapSnapshot(DbDataReader reader)
    {
        // WHAT: Extracts column ordinals from weaver_depot_calibration_snapshots into a strongly typed record.
        // WHY:  Centralizes snapshot hydration and safely falls back when encountering unrecognized enum strings.
        // HOW:  Reads columns by ordinal index, parses DepotSafetyStatus with fallback to Optimal, and parses computed_at_utc.
        string statusText = reader.GetString(7);
        DepotSafetyStatus status = Enum.TryParse(statusText, ignoreCase: true, out DepotSafetyStatus parsed)
            ? parsed
            : DepotSafetyStatus.Optimal;

        return new DepotCalibrationSnapshotRecord
        {
            SnapshotId = reader.GetString(0),
            DepotId = reader.GetString(1),
            PatientId = reader.GetString(2),
            InferredClearanceMultiplier = reader.GetDouble(3),
            InferredDailyErosionRatePercent = reader.GetDouble(4),
            AdjustedRedoseDay = reader.GetInt32(5),
            RecommendedNextMassMg = reader.GetDouble(6),
            SafetyStatus = status,
            ResidualRmse = reader.GetDouble(8),
            ComputedAtUtc = WeaverDbValues.ReadTimestamp(reader, 9)
        };
    }
}
