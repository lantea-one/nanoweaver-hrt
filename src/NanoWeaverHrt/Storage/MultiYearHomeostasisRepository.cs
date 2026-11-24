using System.Data.Common;
using System.Globalization;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Multi-engine relational repository persisting multi-year permanent endocrine homeostasis forecast runs
/// and annual mitotic durability checkpoints across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Concrete implementation of <see cref="IMultiYearHomeostasisRepository"/>
/// backed by <see cref="IWeaverDbConnectionFactory"/>.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 43 (P225): Guarantees atomic persistence, deterministic newest-first
/// depot queries, and DuckDB-compatible GDPR Article 17 child-before-parent erasure across all 4 SQL backends.
/// </para>
/// <para>
/// <strong>How it works:</strong> Declares return values outside <c>{ await using ... }</c> session blocks,
/// parses columns via fixed ordinals without LINQ allocations, and writes child rows after parent rows inside transactions.
/// </para>
/// </remarks>
public sealed class MultiYearHomeostasisRepository : IMultiYearHomeostasisRepository
{
    /// <summary>
    /// Shared projection column list for <c>weaver_multiyear_homeostasis_runs</c> queries guaranteeing ordinal alignment.
    /// </summary>
    private const string RunSelectColumns =
        "run_id, depot_id, circuit_id, patient_id, regimen, horizon_years, " +
        "mitotic_fidelity_rate, transduced_progenitor_fraction, projected_ten_year_ttr_pct, " +
        "durability_status, created_at_utc";

    /// <summary>
    /// Shared projection column list for <c>weaver_multiyear_homeostasis_checkpoints</c> queries guaranteeing ordinal alignment.
    /// </summary>
    private const string CheckpointSelectColumns =
        "checkpoint_id, run_id, patient_id, year_index, estradiol_pmol_l, testosterone_nmol_l, " +
        "lh_iu_l, fsh_iu_l, methylated_progenitor_fraction, hydrogel_contribution_fraction";

    /// <summary>
    /// Database connection factory providing dialect-aware database sessions.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiYearHomeostasisRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">Multi-engine database connection factory.</param>
    public MultiYearHomeostasisRepository(IWeaverDbConnectionFactory connectionFactory)
    {
        // WHAT: Stores the validated database connection factory.
        // WHY:  Every multi-year homeostasis query opens a session bound to the configured SQL dialect.
        // HOW:  Guards against null injection before assigning the readonly field.
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc/>
    public async Task SaveForecastRunAsync(
        MultiYearHomeostasisRunRecord run,
        IReadOnlyList<MultiYearHomeostasisCheckpointRecord> checkpoints,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Atomically persists a multi-year homeostasis run header and all annual checkpoints in a transaction.
        // WHY:  Milestone 43 (P225): Guarantees partial forecast checkpoints cannot orphan or diverge from run metadata.
        // HOW:  Validates inputs, deletes any existing child/parent rows matching RunId, inserts run and checkpoints, and commits.
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(checkpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(run.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(run.DepotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(run.PatientId);

        for (int i = 0; i < checkpoints.Count; i++)
        {
            MultiYearHomeostasisCheckpointRecord checkpoint = checkpoints[i];
            ArgumentNullException.ThrowIfNull(checkpoint);
            ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint.CheckpointId);
        }

        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbTransaction tx = await session.Connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (DbCommand delCheckpoints = session.CreateCommand(
                $"DELETE FROM weaver_multiyear_homeostasis_checkpoints WHERE run_id = {session.Dialect.Parameter("rid")}"))
            {
                delCheckpoints.Transaction = tx;
                WeaverDbParameters.Add(delCheckpoints, "rid", run.RunId);
                await delCheckpoints.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (DbCommand delRun = session.CreateCommand(
                $"DELETE FROM weaver_multiyear_homeostasis_runs WHERE run_id = {session.Dialect.Parameter("rid")}"))
            {
                delRun.Transaction = tx;
                WeaverDbParameters.Add(delRun, "rid", run.RunId);
                await delRun.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            string insertRunSql =
                $"INSERT INTO weaver_multiyear_homeostasis_runs ({RunSelectColumns}) VALUES (" +
                $"{session.Dialect.Parameter("rid")}, {session.Dialect.Parameter("did")}, {session.Dialect.Parameter("cid")}, " +
                $"{session.Dialect.Parameter("pid")}, {session.Dialect.Parameter("reg")}, {session.Dialect.Parameter("hor")}, " +
                $"{session.Dialect.Parameter("fid")}, {session.Dialect.Parameter("tpf")}, {session.Dialect.Parameter("ttr")}, " +
                $"{session.Dialect.Parameter("sts")}, {session.Dialect.Parameter("cat")})";

            await using (DbCommand insRun = session.CreateCommand(insertRunSql))
            {
                insRun.Transaction = tx;
                WeaverDbParameters.Add(insRun, "rid", run.RunId);
                WeaverDbParameters.Add(insRun, "did", run.DepotId);
                WeaverDbParameters.AddNullable(insRun, "cid", run.CircuitId);
                WeaverDbParameters.Add(insRun, "pid", run.PatientId);
                WeaverDbParameters.Add(insRun, "reg", run.Regimen.ToString());
                WeaverDbParameters.Add(insRun, "hor", run.HorizonYears);
                WeaverDbParameters.Add(insRun, "fid", run.MitoticFidelityRate);
                WeaverDbParameters.Add(insRun, "tpf", run.TransducedProgenitorFraction);
                WeaverDbParameters.Add(insRun, "ttr", run.ProjectedTenYearTtrPercent);
                WeaverDbParameters.Add(insRun, "sts", run.DurabilityStatus.ToString());
                WeaverDbParameters.Add(insRun, "cat", session.Dialect.TimestampValue(run.CreatedAtUtc));
                await insRun.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            string insertCheckpointSql =
                $"INSERT INTO weaver_multiyear_homeostasis_checkpoints ({CheckpointSelectColumns}) VALUES (" +
                $"{session.Dialect.Parameter("chid")}, {session.Dialect.Parameter("rid")}, {session.Dialect.Parameter("pid")}, " +
                $"{session.Dialect.Parameter("yr")}, {session.Dialect.Parameter("e2")}, {session.Dialect.Parameter("t")}, " +
                $"{session.Dialect.Parameter("lh")}, {session.Dialect.Parameter("fsh")}, {session.Dialect.Parameter("meth")}, " +
                $"{session.Dialect.Parameter("hyd")})";

            for (int i = 0; i < checkpoints.Count; i++)
            {
                MultiYearHomeostasisCheckpointRecord cp = checkpoints[i];
                await using DbCommand insCp = session.CreateCommand(insertCheckpointSql);
                insCp.Transaction = tx;
                WeaverDbParameters.Add(insCp, "chid", cp.CheckpointId);
                WeaverDbParameters.Add(insCp, "rid", run.RunId);
                WeaverDbParameters.Add(insCp, "pid", run.PatientId);
                WeaverDbParameters.Add(insCp, "yr", cp.YearIndex);
                WeaverDbParameters.Add(insCp, "e2", cp.EstradiolPmolL);
                WeaverDbParameters.Add(insCp, "t", cp.TestosteroneNmolL);
                WeaverDbParameters.Add(insCp, "lh", cp.LhIuL);
                WeaverDbParameters.Add(insCp, "fsh", cp.FshIuL);
                WeaverDbParameters.Add(insCp, "meth", cp.MethylatedProgenitorFraction);
                WeaverDbParameters.Add(insCp, "hyd", cp.HydrogelContributionFraction);
                await insCp.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<MultiYearHomeostasisRunRecord?> GetRunByIdAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Queries a single multi-year homeostasis forecast run header by run_id.
        // WHY:  Supports targeted audit lookups and run-specific checkpoint resolution.
        // HOW:  Executes a parameterized SELECT inside an explicit session block and returns after block exit.
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        MultiYearHomeostasisRunRecord? result = null;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT {RunSelectColumns} FROM weaver_multiyear_homeostasis_runs " +
                $"WHERE run_id = {session.Dialect.Parameter("rid")}");
            WeaverDbParameters.Add(cmd, "rid", runId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result = ReadRun(reader);
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MultiYearHomeostasisRunRecord>> ListRunsByDepotAsync(
        string depotId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Lists all multi-year forecast runs linked to a depot formulation in newest-first order.
        // WHY:  Powers GET /api/depots/{depotId}/multiyear-forecasts timeline history views.
        // HOW:  Executes a parameterized SELECT ordered by created_at_utc DESC, run_id DESC.
        ArgumentException.ThrowIfNullOrWhiteSpace(depotId);

        List<MultiYearHomeostasisRunRecord> runs = [];
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT {RunSelectColumns} FROM weaver_multiyear_homeostasis_runs " +
                $"WHERE depot_id = {session.Dialect.Parameter("did")} " +
                "ORDER BY created_at_utc DESC, run_id DESC");
            WeaverDbParameters.Add(cmd, "did", depotId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                runs.Add(ReadRun(reader));
            }
        }

        return runs;
    }

    /// <inheritdoc/>
    public async Task<MultiYearHomeostasisRunRecord?> GetLatestRunByDepotAsync(
        string depotId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Queries the single newest multi-year homeostasis forecast run for a depot formulation.
        // WHY:  Powers GET /api/depots/{depotId}/multiyear-forecasts/latest without loading older runs.
        // HOW:  Executes a parameterized SELECT ordered newest-first with LIMIT 1 across all 4 SQL dialects.
        ArgumentException.ThrowIfNullOrWhiteSpace(depotId);

        MultiYearHomeostasisRunRecord? latest = null;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT {RunSelectColumns} FROM weaver_multiyear_homeostasis_runs " +
                $"WHERE depot_id = {session.Dialect.Parameter("did")} " +
                "ORDER BY created_at_utc DESC, run_id DESC LIMIT 1");
            WeaverDbParameters.Add(cmd, "did", depotId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                latest = ReadRun(reader);
            }
        }

        return latest;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MultiYearHomeostasisCheckpointRecord>> GetCheckpointsByRunAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Retrieves all annual checkpoints belonging to a multi-year forecast run ordered chronologically.
        // WHY:  Reconstructs yearly biomarker tables and SVG charts for historical forecast reviews.
        // HOW:  Queries weaver_multiyear_homeostasis_checkpoints ordered by year_index ASC.
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        List<MultiYearHomeostasisCheckpointRecord> checkpoints = [];
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT {CheckpointSelectColumns} FROM weaver_multiyear_homeostasis_checkpoints " +
                $"WHERE run_id = {session.Dialect.Parameter("rid")} ORDER BY year_index ASC");
            WeaverDbParameters.Add(cmd, "rid", runId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                checkpoints.Add(ReadCheckpoint(reader));
            }
        }

        return checkpoints;
    }

    /// <inheritdoc/>
    public async Task<int> DeleteByPatientIdAsync(
        string patientId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Atomically deletes all patient-scoped checkpoints and forecast runs for GDPR Article 17 erasure.
        // WHY:  Ensures child checkpoint rows are deleted before parent runs across SQLite, PostgreSQL, MySQL, and DuckDB.
        // HOW:  Deletes child rows from weaver_multiyear_homeostasis_checkpoints first, then runs, inside a transaction.
        ArgumentException.ThrowIfNullOrWhiteSpace(patientId);

        int deletedTotal = 0;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbTransaction tx = await session.Connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (DbCommand delCheckpoints = session.CreateCommand(
                $"DELETE FROM weaver_multiyear_homeostasis_checkpoints WHERE patient_id = {session.Dialect.Parameter("pid")}"))
            {
                delCheckpoints.Transaction = tx;
                WeaverDbParameters.Add(delCheckpoints, "pid", patientId);
                deletedTotal += await delCheckpoints.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (DbCommand delRuns = session.CreateCommand(
                $"DELETE FROM weaver_multiyear_homeostasis_runs WHERE patient_id = {session.Dialect.Parameter("pid")}"))
            {
                delRuns.Transaction = tx;
                WeaverDbParameters.Add(delRuns, "pid", patientId);
                deletedTotal += await delRuns.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return deletedTotal;
    }

    /// <summary>
    /// Materializes a <see cref="MultiYearHomeostasisRunRecord"/> from the current row of an open reader.
    /// </summary>
    /// <param name="reader">Positioned data reader matching <see cref="RunSelectColumns"/>.</param>
    /// <returns>The populated run header record.</returns>
    private static MultiYearHomeostasisRunRecord ReadRun(DbDataReader reader)
    {
        // WHAT: Maps an aligned SQL row into an immutable MultiYearHomeostasisRunRecord instance.
        // WHY:  Centralizes 11-column ordinal mapping across GetRunByIdAsync, ListRunsByDepotAsync, and GetLatestRunByDepotAsync.
        // HOW:  Reads columns by ordinal index and applies safe enum and timestamp helpers.
        return new MultiYearHomeostasisRunRecord
        {
            RunId = reader.GetString(0),
            DepotId = reader.GetString(1),
            CircuitId = reader.IsDBNull(2) ? null : reader.GetString(2),
            PatientId = reader.GetString(3),
            Regimen = ParseEnumOrDefault(reader.GetString(4), TherapeuticRegimen.MaleToFemale),
            HorizonYears = Convert.ToInt32(reader.GetValue(5), CultureInfo.InvariantCulture),
            MitoticFidelityRate = Convert.ToDouble(reader.GetValue(6), CultureInfo.InvariantCulture),
            TransducedProgenitorFraction = Convert.ToDouble(reader.GetValue(7), CultureInfo.InvariantCulture),
            ProjectedTenYearTtrPercent = Convert.ToDouble(reader.GetValue(8), CultureInfo.InvariantCulture),
            DurabilityStatus = ParseEnumOrDefault(reader.GetString(9), MultiYearDurabilityStatus.PermanentHomeostasisVerified),
            CreatedAtUtc = WeaverDbValues.ReadTimestamp(reader, 10)
        };
    }

    /// <summary>
    /// Materializes a <see cref="MultiYearHomeostasisCheckpointRecord"/> from the current row of an open reader.
    /// </summary>
    /// <param name="reader">Positioned data reader matching <see cref="CheckpointSelectColumns"/>.</param>
    /// <returns>The populated annual checkpoint record.</returns>
    private static MultiYearHomeostasisCheckpointRecord ReadCheckpoint(DbDataReader reader)
    {
        // WHAT: Maps an aligned SQL row into an immutable MultiYearHomeostasisCheckpointRecord instance.
        // WHY:  Avoids duplicating 10-column ordinal extraction when loading annual checkpoint trajectories.
        // HOW:  Reads columns sequentially via fixed ordinals and invariant numeric conversions.
        return new MultiYearHomeostasisCheckpointRecord
        {
            CheckpointId = reader.GetString(0),
            RunId = reader.GetString(1),
            PatientId = reader.GetString(2),
            YearIndex = Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
            EstradiolPmolL = Convert.ToDouble(reader.GetValue(4), CultureInfo.InvariantCulture),
            TestosteroneNmolL = Convert.ToDouble(reader.GetValue(5), CultureInfo.InvariantCulture),
            LhIuL = Convert.ToDouble(reader.GetValue(6), CultureInfo.InvariantCulture),
            FshIuL = Convert.ToDouble(reader.GetValue(7), CultureInfo.InvariantCulture),
            MethylatedProgenitorFraction = Convert.ToDouble(reader.GetValue(8), CultureInfo.InvariantCulture),
            HydrogelContributionFraction = Convert.ToDouble(reader.GetValue(9), CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Parses a persisted string enum column with a safe fallback when unrecognized values appear.
    /// </summary>
    /// <typeparam name="TEnum">Target enumeration type.</typeparam>
    /// <param name="raw">Raw database string column value.</param>
    /// <param name="fallback">Default enumeration member returned if parsing fails.</param>
    /// <returns>Parsed enumeration value or <paramref name="fallback"/>.</returns>
    private static TEnum ParseEnumOrDefault<TEnum>(string raw, TEnum fallback)
        where TEnum : struct, Enum
    {
        // WHAT: Case-insensitive enum parser with deterministic fallback.
        // WHY:  Prevents reader exceptions if legacy or external records contain unrecognized string labels.
        // HOW:  Invokes Enum.TryParse and falls back cleanly on mismatch.
        return Enum.TryParse(raw, ignoreCase: true, out TEnum parsed) ? parsed : fallback;
    }
}
