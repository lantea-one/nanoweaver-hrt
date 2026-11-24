using System.Data.Common;
using System.Globalization;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Multi-engine relational repository persisting paired toehold strand-displacement antidotes and emergency
/// clinical depot neutralization events across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Concrete implementation of <see cref="IReversalAntidoteRepository"/> backed
/// by <see cref="IWeaverDbConnectionFactory"/>.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 44 (P230): Guarantees consistent, dialect-agnostic persistence and
/// atomic GDPR Article 17 erasure cascades across all four supported relational storage engines.
/// </para>
/// <para>
/// <strong>How it works:</strong> Uses explicit <c>{ await using ... }</c> block scopes, positional column mapping
/// without LINQ allocations, and child-before-parent transaction deletes matching DuckDB foreign-key rules.
/// </para>
/// </remarks>
public sealed class ReversalAntidoteRepository : IReversalAntidoteRepository
{
    /// <summary>
    /// Shared comma-separated column projection for <c>weaver_lot_reversal_antidotes</c>.
    /// </summary>
    private const string AntidoteSelectColumns =
        "antidote_id, primary_lot_id, patient_id, antidote_sequence_5to3, toehold_length_nt, " +
        "melting_temp_celsius, binding_delta_g_kcal_mol, paired_payload_sha256, paired_signature_base64, " +
        "signer_key_id, status, created_at_utc";

    /// <summary>
    /// Shared comma-separated column projection for <c>weaver_lot_neutralization_events</c>.
    /// </summary>
    private const string NeutralizationSelectColumns =
        "neutralization_id, antidote_id, primary_lot_id, patient_id, dosage_mg, " +
        "clinical_reason, estimated_clearance_hours, executed_by, administered_at_utc";

    /// <summary>
    /// Multi-engine database connection factory supplying dialect-configured sessions.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReversalAntidoteRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">Multi-engine database connection factory.</param>
    public ReversalAntidoteRepository(IWeaverDbConnectionFactory connectionFactory)
    {
        // WHAT: Validates and stores the multi-engine database connection factory.
        // WHY:  Prevents silent null dereferences during antidote pairing or emergency neutralization writes.
        // HOW:  Guards connectionFactory with ArgumentNullException.ThrowIfNull before field assignment.
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc/>
    public async Task SaveAntidoteAsync(
        ReversalAntidoteRecord antidote,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Atomically inserts a paired reversal antidote record into weaver_lot_reversal_antidotes.
        // WHY:  Replaces any existing record with the same antidote_id safely across SQLite, PostgreSQL, DuckDB, and MySQL.
        // HOW:  Validates required fields and executes a DELETE + INSERT pair inside an atomic DbTransaction.
        ArgumentNullException.ThrowIfNull(antidote);
        ArgumentException.ThrowIfNullOrWhiteSpace(antidote.AntidoteId);
        ArgumentException.ThrowIfNullOrWhiteSpace(antidote.PrimaryLotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(antidote.PatientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(antidote.AntidoteSequence5To3);
        ArgumentException.ThrowIfNullOrWhiteSpace(antidote.PairedPayloadSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(antidote.PairedSignatureBase64);
        ArgumentException.ThrowIfNullOrWhiteSpace(antidote.SignerKeyId);

        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbTransaction tx = await session.Connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (DbCommand delCmd = session.CreateCommand(
                $"DELETE FROM weaver_lot_reversal_antidotes WHERE antidote_id = {session.Dialect.Parameter("aid")}"))
            {
                delCmd.Transaction = tx;
                WeaverDbParameters.Add(delCmd, "aid", antidote.AntidoteId);
                await delCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            string insertSql =
                $"INSERT INTO weaver_lot_reversal_antidotes ({AntidoteSelectColumns}) VALUES (" +
                $"{session.Dialect.Parameter("aid")}, {session.Dialect.Parameter("lid")}, {session.Dialect.Parameter("pid")}, " +
                $"{session.Dialect.Parameter("seq")}, {session.Dialect.Parameter("tnt")}, {session.Dialect.Parameter("tm")}, " +
                $"{session.Dialect.Parameter("dg")}, {session.Dialect.Parameter("sha")}, {session.Dialect.Parameter("sig")}, " +
                $"{session.Dialect.Parameter("kid")}, {session.Dialect.Parameter("sts")}, {session.Dialect.Parameter("cat")})";

            await using (DbCommand insCmd = session.CreateCommand(insertSql))
            {
                insCmd.Transaction = tx;
                WeaverDbParameters.Add(insCmd, "aid", antidote.AntidoteId);
                WeaverDbParameters.Add(insCmd, "lid", antidote.PrimaryLotId);
                WeaverDbParameters.Add(insCmd, "pid", antidote.PatientId);
                WeaverDbParameters.Add(insCmd, "seq", antidote.AntidoteSequence5To3);
                WeaverDbParameters.Add(insCmd, "tnt", antidote.ToeholdLengthNt);
                WeaverDbParameters.Add(insCmd, "tm", antidote.MeltingTempCelsius);
                WeaverDbParameters.Add(insCmd, "dg", antidote.BindingDeltaGKcalMol);
                WeaverDbParameters.Add(insCmd, "sha", antidote.PairedPayloadSha256);
                WeaverDbParameters.Add(insCmd, "sig", antidote.PairedSignatureBase64);
                WeaverDbParameters.Add(insCmd, "kid", antidote.SignerKeyId);
                WeaverDbParameters.Add(insCmd, "sts", antidote.Status.ToString());
                WeaverDbParameters.Add(insCmd, "cat", session.Dialect.TimestampValue(antidote.CreatedAtUtc));
                await insCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<ReversalAntidoteRecord?> GetLatestAntidoteByLotAsync(
        string primaryLotId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Fetches the newest reversal antidote paired with primaryLotId across all 4 SQL backends.
        // WHY:  Enforces standby availability checks and hydrates clinical depot displays deterministically.
        // HOW:  Executes parameterized SELECT ORDER BY created_at_utc DESC, antidote_id DESC LIMIT 1 inside block scope.
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryLotId);

        ReversalAntidoteRecord? record = null;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT {AntidoteSelectColumns} FROM weaver_lot_reversal_antidotes " +
                $"WHERE primary_lot_id = {session.Dialect.Parameter("lid")} " +
                "ORDER BY created_at_utc DESC, antidote_id DESC LIMIT 1");
            WeaverDbParameters.Add(cmd, "lid", primaryLotId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                record = ReadAntidote(reader);
            }
        }

        return record;
    }

    /// <inheritdoc/>
    public async Task<ReversalAntidoteRecord?> GetAntidoteByIdAsync(
        string antidoteId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Retrieves a single reversal antidote record matching antidoteId.
        // WHY:  Supports direct identifier lookups and status transition verification.
        // HOW:  Executes parameterized primary-key SELECT and maps ordinals via ReadAntidote.
        ArgumentException.ThrowIfNullOrWhiteSpace(antidoteId);

        ReversalAntidoteRecord? record = null;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT {AntidoteSelectColumns} FROM weaver_lot_reversal_antidotes " +
                $"WHERE antidote_id = {session.Dialect.Parameter("aid")}");
            WeaverDbParameters.Add(cmd, "aid", antidoteId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                record = ReadAntidote(reader);
            }
        }

        return record;
    }

    /// <inheritdoc/>
    public async Task<bool> UpdateAntidoteStatusAsync(
        string antidoteId,
        ReversalAntidoteStatus status,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Updates the lifecycle status column of a paired reversal antidote record.
        // WHY:  Marks an antidote ActiveStandby -> Neutralized during emergency administration or Revoked if superseded.
        // HOW:  Executes parameterized UPDATE and returns whether at least one row changed.
        ArgumentException.ThrowIfNullOrWhiteSpace(antidoteId);

        bool updated = false;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"UPDATE weaver_lot_reversal_antidotes SET status = {session.Dialect.Parameter("sts")} " +
                $"WHERE antidote_id = {session.Dialect.Parameter("aid")}");
            WeaverDbParameters.Add(cmd, "sts", status.ToString());
            WeaverDbParameters.Add(cmd, "aid", antidoteId);

            int rowsAffected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            updated = rowsAffected > 0;
        }

        return updated;
    }

    /// <inheritdoc/>
    public async Task AddNeutralizationEventAsync(
        LotNeutralizationEventRecord neutralizationEvent,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Inserts an emergency clinical depot neutralization event into weaver_lot_neutralization_events.
        // WHY:  Maintains immutable 21 CFR Part 11 traceability for administered reversal interventions.
        // HOW:  Validates required fields and executes a dialect-parameterized INSERT inside an explicit block scope.
        ArgumentNullException.ThrowIfNull(neutralizationEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(neutralizationEvent.NeutralizationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(neutralizationEvent.AntidoteId);
        ArgumentException.ThrowIfNullOrWhiteSpace(neutralizationEvent.PrimaryLotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(neutralizationEvent.PatientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(neutralizationEvent.ClinicalReason);
        ArgumentException.ThrowIfNullOrWhiteSpace(neutralizationEvent.ExecutedBy);

        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            string sql =
                $"INSERT INTO weaver_lot_neutralization_events ({NeutralizationSelectColumns}) VALUES (" +
                $"{session.Dialect.Parameter("nid")}, {session.Dialect.Parameter("aid")}, {session.Dialect.Parameter("lid")}, " +
                $"{session.Dialect.Parameter("pid")}, {session.Dialect.Parameter("dmg")}, {session.Dialect.Parameter("rsn")}, " +
                $"{session.Dialect.Parameter("hrs")}, {session.Dialect.Parameter("eby")}, {session.Dialect.Parameter("aat")})";

            await using DbCommand cmd = session.CreateCommand(sql);
            WeaverDbParameters.Add(cmd, "nid", neutralizationEvent.NeutralizationId);
            WeaverDbParameters.Add(cmd, "aid", neutralizationEvent.AntidoteId);
            WeaverDbParameters.Add(cmd, "lid", neutralizationEvent.PrimaryLotId);
            WeaverDbParameters.Add(cmd, "pid", neutralizationEvent.PatientId);
            WeaverDbParameters.Add(cmd, "dmg", neutralizationEvent.DosageMg);
            WeaverDbParameters.Add(cmd, "rsn", neutralizationEvent.ClinicalReason);
            WeaverDbParameters.Add(cmd, "hrs", neutralizationEvent.EstimatedClearanceHours);
            WeaverDbParameters.Add(cmd, "eby", neutralizationEvent.ExecutedBy);
            WeaverDbParameters.Add(cmd, "aat", session.Dialect.TimestampValue(neutralizationEvent.AdministeredAtUtc));

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LotNeutralizationEventRecord>> ListNeutralizationEventsByLotAsync(
        string primaryLotId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Queries all neutralization events associated with a fabrication lot in chronological order.
        // WHY:  Displays administration history cleanly on web DepotStudio and native ViewModels without LINQ allocations.
        // HOW:  Executes parameterized SELECT ORDER BY administered_at_utc ASC, neutralization_id ASC and maps rows sequentially.
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryLotId);

        List<LotNeutralizationEventRecord> events = [];
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT {NeutralizationSelectColumns} FROM weaver_lot_neutralization_events " +
                $"WHERE primary_lot_id = {session.Dialect.Parameter("lid")} " +
                "ORDER BY administered_at_utc ASC, neutralization_id ASC");
            WeaverDbParameters.Add(cmd, "lid", primaryLotId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                events.Add(ReadNeutralizationEvent(reader));
            }
        }

        return events;
    }

    /// <inheritdoc/>
    public async Task<int> DeleteByPatientIdAsync(
        string patientId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Atomically deletes patient-owned neutralization events and paired antidotes for GDPR Article 17 erasure.
        // WHY:  Deletes child neutralization events before parent antidotes so FK constraints succeed across all 4 engines.
        // HOW:  Executes both parameterized DELETE statements inside one DbTransaction and commits atomically.
        ArgumentException.ThrowIfNullOrWhiteSpace(patientId);

        int deletedTotal = 0;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbTransaction tx = await session.Connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (DbCommand delEvents = session.CreateCommand(
                $"DELETE FROM weaver_lot_neutralization_events WHERE patient_id = {session.Dialect.Parameter("pid")}"))
            {
                delEvents.Transaction = tx;
                WeaverDbParameters.Add(delEvents, "pid", patientId);
                deletedTotal += await delEvents.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (DbCommand delAntidotes = session.CreateCommand(
                $"DELETE FROM weaver_lot_reversal_antidotes WHERE patient_id = {session.Dialect.Parameter("pid")}"))
            {
                delAntidotes.Transaction = tx;
                WeaverDbParameters.Add(delAntidotes, "pid", patientId);
                deletedTotal += await delAntidotes.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return deletedTotal;
    }

    /// <summary>
    /// Materializes a <see cref="ReversalAntidoteRecord"/> from the current row of an open data reader.
    /// </summary>
    /// <param name="reader">Positioned data reader matching <see cref="AntidoteSelectColumns"/>.</param>
    /// <returns>The populated reversal antidote record.</returns>
    private static ReversalAntidoteRecord ReadAntidote(DbDataReader reader)
    {
        // WHAT: Maps an aligned 12-column SQL row into a ReversalAntidoteRecord instance.
        // WHY:  Centralizes ordinal parsing and enum fallback handling between GetLatestAntidoteByLotAsync and GetAntidoteByIdAsync.
        // HOW:  Reads columns by fixed ordinal index using InvariantCulture numeric conversions and safe enum parsing.
        return new ReversalAntidoteRecord
        {
            AntidoteId = reader.GetString(0),
            PrimaryLotId = reader.GetString(1),
            PatientId = reader.GetString(2),
            AntidoteSequence5To3 = reader.GetString(3),
            ToeholdLengthNt = Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture),
            MeltingTempCelsius = Convert.ToDouble(reader.GetValue(5), CultureInfo.InvariantCulture),
            BindingDeltaGKcalMol = Convert.ToDouble(reader.GetValue(6), CultureInfo.InvariantCulture),
            PairedPayloadSha256 = reader.GetString(7),
            PairedSignatureBase64 = reader.GetString(8),
            SignerKeyId = reader.GetString(9),
            Status = ParseStatusOrDefault(reader.GetString(10)),
            CreatedAtUtc = WeaverDbValues.ReadTimestamp(reader, 11)
        };
    }

    /// <summary>
    /// Materializes a <see cref="LotNeutralizationEventRecord"/> from the current row of an open data reader.
    /// </summary>
    /// <param name="reader">Positioned data reader matching <see cref="NeutralizationSelectColumns"/>.</param>
    /// <returns>The populated neutralization event record.</returns>
    private static LotNeutralizationEventRecord ReadNeutralizationEvent(DbDataReader reader)
    {
        // WHAT: Maps an aligned 9-column SQL row into a LotNeutralizationEventRecord instance.
        // WHY:  Eliminates dynamic column lookups during chronological event listing.
        // HOW:  Extracts scalar fields sequentially and coerces numeric and timestamp columns deterministically.
        return new LotNeutralizationEventRecord
        {
            NeutralizationId = reader.GetString(0),
            AntidoteId = reader.GetString(1),
            PrimaryLotId = reader.GetString(2),
            PatientId = reader.GetString(3),
            DosageMg = Convert.ToDouble(reader.GetValue(4), CultureInfo.InvariantCulture),
            ClinicalReason = reader.GetString(5),
            EstimatedClearanceHours = Convert.ToDouble(reader.GetValue(6), CultureInfo.InvariantCulture),
            ExecutedBy = reader.GetString(7),
            AdministeredAtUtc = WeaverDbValues.ReadTimestamp(reader, 8)
        };
    }

    /// <summary>
    /// Parses a persisted string antidote status label with a safe standby fallback on unrecognized values.
    /// </summary>
    /// <param name="raw">Raw database string value from the <c>status</c> column.</param>
    /// <returns>Parsed <see cref="ReversalAntidoteStatus"/> enumeration value or <see cref="ReversalAntidoteStatus.ActiveStandby"/>.</returns>
    private static ReversalAntidoteStatus ParseStatusOrDefault(string raw)
    {
        // WHAT: Parses stored antidote status strings without throwing on unknown tokens.
        // WHY:  Maintains reader resilience across mixed schema histories.
        // HOW:  Delegates to Enum.TryParse with case-insensitive matching and falls back to ActiveStandby.
        return Enum.TryParse(raw, ignoreCase: true, out ReversalAntidoteStatus parsed)
            ? parsed
            : ReversalAntidoteStatus.ActiveStandby;
    }
}
