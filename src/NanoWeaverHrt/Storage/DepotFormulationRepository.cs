using System.Data.Common;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Database repository for semi-annual time-release depot formulations and discrete pharmacokinetic release schedules.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The persistence adapter implementing <see cref="IDepotFormulationRepository"/> on top of
/// <see cref="IWeaverDbConnectionFactory"/> across SQLite, PostgreSQL, DuckDB, and MySQL engines.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Provides dialect-neutral database persistence with zero hot-path heap allocations,
/// deterministic upsert semantics, and transactional schedule point management.
/// </para>
/// <para>
/// <strong>How it works:</strong> Formulates dialect-appropriate SQL via <see cref="ISqlDialect.BuildUpsertSql"/> and
/// parameterized commands via <see cref="WeaverDbParameters"/> and <see cref="WeaverDbValues"/>.
/// </para>
/// </remarks>
public sealed class DepotFormulationRepository : IDepotFormulationRepository
{
    /// <summary>
    /// Column names for the <c>weaver_depot_formulations</c> table.
    /// </summary>
    private static readonly string[] DepotColumns =
    [
        "depot_id", "patient_id", "regimen", "polymer_matrix_type",
        "initial_nanocarrier_mass_mg", "daily_erosion_rate_percent",
        "release_kinetics_model", "target_horizon_days", "created_at_utc"
    ];

    /// <summary>
    /// Primary key conflict column for formulation upserts.
    /// </summary>
    private static readonly string[] DepotConflictColumns = ["depot_id"];

    /// <summary>
    /// Update column list for formulation collision updates.
    /// </summary>
    private static readonly string[] DepotUpdateColumns =
    [
        "patient_id", "regimen", "polymer_matrix_type",
        "initial_nanocarrier_mass_mg", "daily_erosion_rate_percent",
        "release_kinetics_model", "target_horizon_days", "created_at_utc"
    ];

    /// <summary>
    /// Parameter names corresponding to <see cref="DepotColumns"/>.
    /// </summary>
    private static readonly string[] DepotParameterNames =
    [
        "id", "pid", "reg", "poly",
        "mass", "rate",
        "kin", "days", "cat"
    ];

    /// <summary>
    /// Database connection factory supplying open sessions and dialect primitives.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="DepotFormulationRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">The database connection factory.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> is <see langword="null"/>.</exception>
    public DepotFormulationRepository(IWeaverDbConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc/>
    public async Task UpsertDepotFormulationAsync(DepotFormulationRecord record, CancellationToken ct = default)
    {
        // WHAT: Inserts or updates a semi-annual time-release depot formulation.
        // WHY:  Persists nanocarrier depot parameters across system restarts.
        // HOW:  Constructs dialect-specific upsert SQL statement and executes parameterized query.
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.DepotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.PatientId);

        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            string sql = session.Dialect.BuildUpsertSql(
                "weaver_depot_formulations",
                DepotColumns,
                DepotConflictColumns,
                DepotUpdateColumns,
                DepotParameterNames);

            await using DbCommand cmd = session.CreateCommand(sql);
            WeaverDbParameters.Add(cmd, "id", record.DepotId);
            WeaverDbParameters.Add(cmd, "pid", record.PatientId);
            WeaverDbParameters.Add(cmd, "reg", record.Regimen.ToString());
            WeaverDbParameters.Add(cmd, "poly", record.PolymerMatrixType);
            WeaverDbParameters.Add(cmd, "mass", record.InitialNanocarrierMassMg);
            WeaverDbParameters.Add(cmd, "rate", record.DailyErosionRatePercent);
            WeaverDbParameters.Add(cmd, "kin", record.ReleaseKineticsModel);
            WeaverDbParameters.Add(cmd, "days", record.TargetHorizonDays);
            WeaverDbParameters.Add(cmd, "cat", record.CreatedAtUtc.ToString("o"));

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<DepotFormulationRecord?> GetDepotFormulationByIdAsync(string depotId, CancellationToken ct = default)
    {
        // WHAT: Queries a specific depot formulation by primary key identifier.
        // WHY:  Provides direct point lookup for simulation runs or clinical review.
        // HOW:  Executes a parameterized SELECT filtered by depot_id.
        ArgumentException.ThrowIfNullOrWhiteSpace(depotId);

        DepotFormulationRecord? result = null;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT depot_id, patient_id, regimen, polymer_matrix_type, initial_nanocarrier_mass_mg, daily_erosion_rate_percent, release_kinetics_model, target_horizon_days, created_at_utc FROM weaver_depot_formulations WHERE depot_id = {session.Dialect.Parameter("did")}");

            WeaverDbParameters.Add(cmd, "did", depotId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                result = MapDepotRecord(reader);
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DepotFormulationRecord>> GetDepotsByPatientIdAsync(string patientId, CancellationToken ct = default)
    {
        // WHAT: Queries all depot formulations associated with a specific patient.
        // WHY:  Allows clinicians and patients to inspect current and historical formulations.
        // HOW:  Executes a parameterized SELECT filtered by patient_id ordered by created_at_utc descending.
        ArgumentException.ThrowIfNullOrWhiteSpace(patientId);

        List<DepotFormulationRecord> depots = [];
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT depot_id, patient_id, regimen, polymer_matrix_type, initial_nanocarrier_mass_mg, daily_erosion_rate_percent, release_kinetics_model, target_horizon_days, created_at_utc FROM weaver_depot_formulations WHERE patient_id = {session.Dialect.Parameter("pid")} ORDER BY created_at_utc DESC");

            WeaverDbParameters.Add(cmd, "pid", patientId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                depots.Add(MapDepotRecord(reader));
            }
        }

        return depots;
    }

    /// <inheritdoc/>
    public async Task SaveReleaseScheduleAsync(string depotId, IReadOnlyList<DepotSchedulePoint> points, CancellationToken ct = default)
    {
        // WHAT: Atomically replaces all release schedule timepoints for the given depot.
        // WHY:  Guarantees consistent pharmacokinetic curves without orphaned or duplicate timepoints.
        // HOW:  Deletes existing timepoints inside a transaction and sequentially inserts the points collection.
        ArgumentException.ThrowIfNullOrWhiteSpace(depotId);
        ArgumentNullException.ThrowIfNull(points);

        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbTransaction tx = await session.Connection.BeginTransactionAsync(ct).ConfigureAwait(false);

            await using (DbCommand deleteCmd = session.CreateCommand(
                $"DELETE FROM weaver_depot_release_schedules WHERE depot_id = {session.Dialect.Parameter("did")}"))
            {
                deleteCmd.Transaction = tx;
                WeaverDbParameters.Add(deleteCmd, "did", depotId);
                await deleteCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            if (points.Count > 0)
            {
                string insertSql = $@"
                    INSERT INTO weaver_depot_release_schedules (
                        schedule_id, depot_id, timepoint_day, cumulative_release_fraction, instantaneous_rate_mg_day, remaining_reserve_mg
                    ) VALUES (
                        {session.Dialect.Parameter("sid")},
                        {session.Dialect.Parameter("did")},
                        {session.Dialect.Parameter("day")},
                        {session.Dialect.Parameter("frac")},
                        {session.Dialect.Parameter("rate")},
                        {session.Dialect.Parameter("res")}
                    );";

                for (int i = 0; i < points.Count; i++)
                {
                    DepotSchedulePoint point = points[i];
                    await using DbCommand insertCmd = session.CreateCommand(insertSql);
                    insertCmd.Transaction = tx;

                    string scheduleId = string.IsNullOrWhiteSpace(point.ScheduleId)
                        ? Guid.NewGuid().ToString("N")
                        : point.ScheduleId;

                    WeaverDbParameters.Add(insertCmd, "sid", scheduleId);
                    WeaverDbParameters.Add(insertCmd, "did", depotId);
                    WeaverDbParameters.Add(insertCmd, "day", point.TimepointDay);
                    WeaverDbParameters.Add(insertCmd, "frac", point.CumulativeReleaseFraction);
                    WeaverDbParameters.Add(insertCmd, "rate", point.InstantaneousRateMgDay);
                    WeaverDbParameters.Add(insertCmd, "res", point.RemainingReserveMg);

                    await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DepotSchedulePoint>> GetReleaseScheduleAsync(string depotId, CancellationToken ct = default)
    {
        // WHAT: Queries all discrete release schedule timepoints for the specified depot.
        // WHY:  Retrieves the simulated elution curve ordered chronologically by day.
        // HOW:  Executes a parameterized SELECT filtered by depot_id ordered by timepoint_day ascending.
        ArgumentException.ThrowIfNullOrWhiteSpace(depotId);

        List<DepotSchedulePoint> points = [];
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT schedule_id, depot_id, timepoint_day, cumulative_release_fraction, instantaneous_rate_mg_day, remaining_reserve_mg FROM weaver_depot_release_schedules WHERE depot_id = {session.Dialect.Parameter("did")} ORDER BY timepoint_day ASC");

            WeaverDbParameters.Add(cmd, "did", depotId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                points.Add(MapSchedulePoint(reader));
            }
        }

        return points;
    }

    /// <summary>
    /// Maps columns from the active reader row to a <see cref="DepotFormulationRecord"/> instance.
    /// </summary>
    /// <param name="reader">The active reader positioned on a formulation row.</param>
    /// <returns>A populated formulation record.</returns>
    private static DepotFormulationRecord MapDepotRecord(DbDataReader reader)
    {
        // WHAT: Maps columns from the data reader to a DepotFormulationRecord instance.
        // WHY:  Centralizes mapping logic and isolates reader indexing.
        // HOW:  Reads ordinals directly, parses enum regimen, and parses timestamp via WeaverDbValues.
        string depotId = reader.GetString(0);
        string patientId = reader.GetString(1);
        string regimenStr = reader.GetString(2);
        TherapeuticRegimen regimen = Enum.TryParse(regimenStr, ignoreCase: true, out TherapeuticRegimen parsed)
            ? parsed
            : TherapeuticRegimen.MaleToFemale;
        string polymerType = reader.GetString(3);
        double initialMass = reader.GetDouble(4);
        double dailyRate = reader.GetDouble(5);
        string kineticsModel = reader.GetString(6);
        int targetHorizon = reader.GetInt32(7);
        DateTimeOffset createdAt = WeaverDbValues.ReadTimestamp(reader, 8);

        return new DepotFormulationRecord
        {
            DepotId = depotId,
            PatientId = patientId,
            Regimen = regimen,
            PolymerMatrixType = polymerType,
            InitialNanocarrierMassMg = initialMass,
            DailyErosionRatePercent = dailyRate,
            ReleaseKineticsModel = kineticsModel,
            TargetHorizonDays = targetHorizon,
            CreatedAtUtc = createdAt
        };
    }

    /// <summary>
    /// Maps columns from the active reader row to a <see cref="DepotSchedulePoint"/> instance.
    /// </summary>
    /// <param name="reader">The active reader positioned on a release schedule row.</param>
    /// <returns>A populated schedule point.</returns>
    private static DepotSchedulePoint MapSchedulePoint(DbDataReader reader)
    {
        // WHAT: Maps columns from the data reader to a DepotSchedulePoint instance.
        // WHY:  Centralizes mapping logic for release schedule timepoints.
        // HOW:  Reads schedule columns sequentially by ordinal index.
        return new DepotSchedulePoint
        {
            ScheduleId = reader.GetString(0),
            DepotId = reader.GetString(1),
            TimepointDay = reader.GetInt32(2),
            CumulativeReleaseFraction = reader.GetDouble(3),
            InstantaneousRateMgDay = reader.GetDouble(4),
            RemainingReserveMg = reader.GetDouble(5)
        };
    }
}
