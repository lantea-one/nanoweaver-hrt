using System.Data.Common;
using System.Globalization;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Multi-engine relational repository persisting epigenetic steroidogenic toggle circuits and guide RNAs
/// across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Concrete implementation of <see cref="IEpigeneticCircuitRepository"/>
/// backed by <see cref="IWeaverDbConnectionFactory"/>.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 42 (P220): Provides dialect-neutral transactional persistence and
/// GDPR Article 17 cascading patient erasure across all four supported SQL backends.
/// </para>
/// <para>
/// <strong>How it works:</strong> Wraps every session in explicit <c>{ await using ... }</c> blocks, uses ordinal
/// reader extraction without LINQ allocations, and deletes child rows prior to parent rows for DuckDB compatibility.
/// </para>
/// </remarks>
public sealed class EpigeneticCircuitRepository : IEpigeneticCircuitRepository
{
    /// <summary>
    /// Column projection list shared across circuit SELECT queries to guarantee ordinal alignment.
    /// </summary>
    private const string CircuitSelectColumns =
        "circuit_id, patient_id, regimen, target_tissue, effector_platform, circuit_status, " +
        "predicted_ttr_percent, separatrix_margin, min_offtarget_specificity_score, notes_encrypted, " +
        "created_at_utc, approved_at_utc, approval_payload_sha256, approval_signature_base64, approval_signer_key_id";

    /// <summary>
    /// Database connection factory supplying dialect-aware database sessions.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="EpigeneticCircuitRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">Multi-engine database connection factory.</param>
    public EpigeneticCircuitRepository(IWeaverDbConnectionFactory connectionFactory)
    {
        // WHAT: Stores the validated database connection factory.
        // WHY:  Every epigenetic circuit query opens a short-lived session bound to the configured SQL engine.
        // HOW:  Guards against null injection before assigning the readonly field.
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc/>
    public async Task SaveCircuitAsync(
        EpigeneticCircuitRecord circuit,
        IReadOnlyList<EpigeneticGuideRnaRecord> guides,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Atomically persists an epigenetic circuit and all associated guide-RNA loci inside a single transaction.
        // WHY:  Milestone 42 (P220): Ensures circuit headers and sgRNA lists stay consistent across all 4 SQL backends.
        // HOW:  Validates inputs, deletes any existing child/parent rows for CircuitId, inserts circuit and guides, and commits.
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(guides);
        ArgumentException.ThrowIfNullOrWhiteSpace(circuit.CircuitId);
        ArgumentException.ThrowIfNullOrWhiteSpace(circuit.PatientId);

        for (int i = 0; i < guides.Count; i++)
        {
            EpigeneticGuideRnaRecord guide = guides[i];
            ArgumentNullException.ThrowIfNull(guide);
            ArgumentException.ThrowIfNullOrWhiteSpace(guide.GuideId);
            ArgumentException.ThrowIfNullOrWhiteSpace(guide.TargetGeneSymbol);
        }

        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbTransaction tx = await session.Connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (DbCommand delGuides = session.CreateCommand(
                $"DELETE FROM weaver_epigenetic_guide_rnas WHERE circuit_id = {session.Dialect.Parameter("cid")}"))
            {
                delGuides.Transaction = tx;
                WeaverDbParameters.Add(delGuides, "cid", circuit.CircuitId);
                await delGuides.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (DbCommand delCircuit = session.CreateCommand(
                $"DELETE FROM weaver_epigenetic_circuits WHERE circuit_id = {session.Dialect.Parameter("cid")}"))
            {
                delCircuit.Transaction = tx;
                WeaverDbParameters.Add(delCircuit, "cid", circuit.CircuitId);
                await delCircuit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            string insertCircuitSql =
                $"INSERT INTO weaver_epigenetic_circuits ({CircuitSelectColumns}) VALUES (" +
                $"{session.Dialect.Parameter("cid")}, {session.Dialect.Parameter("pid")}, {session.Dialect.Parameter("reg")}, " +
                $"{session.Dialect.Parameter("tis")}, {session.Dialect.Parameter("eff")}, {session.Dialect.Parameter("sts")}, " +
                $"{session.Dialect.Parameter("ttr")}, {session.Dialect.Parameter("sep")}, {session.Dialect.Parameter("cfd")}, " +
                $"{session.Dialect.Parameter("notes")}, {session.Dialect.Parameter("cat")}, {session.Dialect.Parameter("aat")}, " +
                $"{session.Dialect.Parameter("asha")}, {session.Dialect.Parameter("asig")}, {session.Dialect.Parameter("akid")})";

            await using (DbCommand insCircuit = session.CreateCommand(insertCircuitSql))
            {
                insCircuit.Transaction = tx;
                WeaverDbParameters.Add(insCircuit, "cid", circuit.CircuitId);
                WeaverDbParameters.Add(insCircuit, "pid", circuit.PatientId);
                WeaverDbParameters.Add(insCircuit, "reg", circuit.Regimen.ToString());
                WeaverDbParameters.Add(insCircuit, "tis", circuit.TargetTissue.ToString());
                WeaverDbParameters.Add(insCircuit, "eff", circuit.EffectorPlatform.ToString());
                WeaverDbParameters.Add(insCircuit, "sts", circuit.CircuitStatus.ToString());
                WeaverDbParameters.Add(insCircuit, "ttr", circuit.PredictedTtrPercent);
                WeaverDbParameters.Add(insCircuit, "sep", circuit.SeparatrixMargin);
                WeaverDbParameters.Add(insCircuit, "cfd", circuit.MinOffTargetSpecificityScore);
                WeaverDbParameters.AddNullable(insCircuit, "notes", circuit.NotesEncrypted);
                WeaverDbParameters.Add(insCircuit, "cat", session.Dialect.TimestampValue(circuit.CreatedAtUtc));
                object? approvedVal = circuit.ApprovedAtUtc.HasValue
                    ? session.Dialect.TimestampValue(circuit.ApprovedAtUtc.Value)
                    : null;
                WeaverDbParameters.AddNullable(insCircuit, "aat", approvedVal);
                WeaverDbParameters.AddNullable(insCircuit, "asha", circuit.ApprovalPayloadSha256);
                WeaverDbParameters.AddNullable(insCircuit, "asig", circuit.ApprovalSignatureBase64);
                WeaverDbParameters.AddNullable(insCircuit, "akid", circuit.ApprovalSignerKeyId);
                await insCircuit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            string insertGuideSql =
                "INSERT INTO weaver_epigenetic_guide_rnas " +
                "(guide_id, circuit_id, patient_id, target_gene_symbol, modulation_mode, protospacer_sequence, pam_sequence, cfd_specificity_score, predicted_fold_change) " +
                $"VALUES ({session.Dialect.Parameter("gid")}, {session.Dialect.Parameter("cid")}, {session.Dialect.Parameter("pid")}, " +
                $"{session.Dialect.Parameter("sym")}, {session.Dialect.Parameter("mod")}, {session.Dialect.Parameter("pro")}, " +
                $"{session.Dialect.Parameter("pam")}, {session.Dialect.Parameter("cfd")}, {session.Dialect.Parameter("fc")})";

            for (int i = 0; i < guides.Count; i++)
            {
                EpigeneticGuideRnaRecord guide = guides[i];
                await using DbCommand insGuide = session.CreateCommand(insertGuideSql);
                insGuide.Transaction = tx;
                WeaverDbParameters.Add(insGuide, "gid", guide.GuideId);
                WeaverDbParameters.Add(insGuide, "cid", circuit.CircuitId);
                WeaverDbParameters.Add(insGuide, "pid", circuit.PatientId);
                WeaverDbParameters.Add(insGuide, "sym", guide.TargetGeneSymbol);
                WeaverDbParameters.Add(insGuide, "mod", guide.ModulationMode.ToString());
                WeaverDbParameters.Add(insGuide, "pro", guide.ProtospacerSequence);
                WeaverDbParameters.Add(insGuide, "pam", guide.PamSequence);
                WeaverDbParameters.Add(insGuide, "cfd", guide.CfdSpecificityScore);
                WeaverDbParameters.Add(insGuide, "fc", guide.PredictedFoldChange);
                await insGuide.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<EpigeneticCircuitRecord?> GetCircuitByIdAsync(
        string circuitId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Loads a single epigenetic circuit row by primary key.
        // WHY:  Evaluate, approve, and authorization routes require direct circuit retrieval.
        // HOW:  Executes a parameterized SELECT inside a block scope and maps via ReadCircuit.
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitId);

        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT {CircuitSelectColumns} FROM weaver_epigenetic_circuits WHERE circuit_id = {session.Dialect.Parameter("cid")}");
            WeaverDbParameters.Add(cmd, "cid", circuitId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return ReadCircuit(reader);
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<EpigeneticCircuitRecord>> ListCircuitsByPatientAsync(
        string patientId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Queries all epigenetic circuits belonging to a patient ordered chronologically ascending.
        // WHY:  Supports deterministic patient timeline rendering and multi-circuit comparisons.
        // HOW:  Executes a parameterized SELECT ordered by created_at_utc ASC, circuit_id ASC without LINQ.
        ArgumentException.ThrowIfNullOrWhiteSpace(patientId);

        List<EpigeneticCircuitRecord> results = [];
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT {CircuitSelectColumns} FROM weaver_epigenetic_circuits " +
                $"WHERE patient_id = {session.Dialect.Parameter("pid")} ORDER BY created_at_utc ASC, circuit_id ASC");
            WeaverDbParameters.Add(cmd, "pid", patientId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(ReadCircuit(reader));
            }
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<EpigeneticGuideRnaRecord>> ListGuidesByCircuitAsync(
        string circuitId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Queries all single-guide RNA records linked to an epigenetic circuit ordered by guide_id ASC.
        // WHY:  Deterministic guide ordering ensures reproducible biophysical and ML-DSA-65 canonical hashing.
        // HOW:  Executes a parameterized SELECT ordered by guide_id ASC and extracts ordinals without LINQ.
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitId);

        List<EpigeneticGuideRnaRecord> results = [];
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                "SELECT guide_id, circuit_id, patient_id, target_gene_symbol, modulation_mode, protospacer_sequence, pam_sequence, cfd_specificity_score, predicted_fold_change " +
                $"FROM weaver_epigenetic_guide_rnas WHERE circuit_id = {session.Dialect.Parameter("cid")} ORDER BY guide_id ASC");
            WeaverDbParameters.Add(cmd, "cid", circuitId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new EpigeneticGuideRnaRecord
                {
                    GuideId = reader.GetString(0),
                    CircuitId = reader.GetString(1),
                    PatientId = reader.GetString(2),
                    TargetGeneSymbol = reader.GetString(3),
                    ModulationMode = ParseEnumOrDefault(reader.GetString(4), GuideRnaModulationMode.Activate),
                    ProtospacerSequence = reader.GetString(5),
                    PamSequence = reader.GetString(6),
                    CfdSpecificityScore = Convert.ToDouble(reader.GetValue(7), CultureInfo.InvariantCulture),
                    PredictedFoldChange = Convert.ToDouble(reader.GetValue(8), CultureInfo.InvariantCulture)
                });
            }
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<bool> UpdateCircuitEvaluationAsync(
        string circuitId,
        EpigeneticCircuitStatus status,
        double predictedTtrPercent,
        double separatrixMargin,
        double minOffTargetSpecificityScore,
        DateTimeOffset? approvedAtUtc,
        string? approvalPayloadSha256,
        string? approvalSignatureBase64,
        string? approvalSignerKeyId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Persists updated evaluation metrics, status, and optional ML-DSA-65 signature columns for an existing circuit.
        // WHY:  Records deterministic pass/fail evaluation outcomes and fail-closed Blocked transitions.
        // HOW:  Executes a parameterized UPDATE statement matching circuit_id and returns true when a row changed.
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitId);

        int updated = 0;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            string sql =
                "UPDATE weaver_epigenetic_circuits SET " +
                $"circuit_status = {session.Dialect.Parameter("sts")}, " +
                $"predicted_ttr_percent = {session.Dialect.Parameter("ttr")}, " +
                $"separatrix_margin = {session.Dialect.Parameter("sep")}, " +
                $"min_offtarget_specificity_score = {session.Dialect.Parameter("cfd")}, " +
                $"approved_at_utc = {session.Dialect.Parameter("aat")}, " +
                $"approval_payload_sha256 = {session.Dialect.Parameter("asha")}, " +
                $"approval_signature_base64 = {session.Dialect.Parameter("asig")}, " +
                $"approval_signer_key_id = {session.Dialect.Parameter("akid")} " +
                $"WHERE circuit_id = {session.Dialect.Parameter("cid")}";

            await using DbCommand cmd = session.CreateCommand(sql);
            WeaverDbParameters.Add(cmd, "sts", status.ToString());
            WeaverDbParameters.Add(cmd, "ttr", predictedTtrPercent);
            WeaverDbParameters.Add(cmd, "sep", separatrixMargin);
            WeaverDbParameters.Add(cmd, "cfd", minOffTargetSpecificityScore);
            object? approvedVal = approvedAtUtc.HasValue
                ? session.Dialect.TimestampValue(approvedAtUtc.Value)
                : null;
            WeaverDbParameters.AddNullable(cmd, "aat", approvedVal);
            WeaverDbParameters.AddNullable(cmd, "asha", approvalPayloadSha256);
            WeaverDbParameters.AddNullable(cmd, "asig", approvalSignatureBase64);
            WeaverDbParameters.AddNullable(cmd, "akid", approvalSignerKeyId);
            WeaverDbParameters.Add(cmd, "cid", circuitId);

            updated = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return updated > 0;
    }

    /// <inheritdoc/>
    public async Task<int> DeleteByPatientIdAsync(
        string patientId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Deletes all epigenetic guide-RNA loci and circuit blueprints belonging to a patient inside a transaction.
        // WHY:  Fulfils GDPR Article 17 right-to-erasure across all 4 engines, including DuckDB without ON DELETE CASCADE.
        // HOW:  Deletes child rows from weaver_epigenetic_guide_rnas first, then parent rows from weaver_epigenetic_circuits.
        ArgumentException.ThrowIfNullOrWhiteSpace(patientId);

        int deletedTotal = 0;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbTransaction tx = await session.Connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (DbCommand delGuides = session.CreateCommand(
                $"DELETE FROM weaver_epigenetic_guide_rnas WHERE patient_id = {session.Dialect.Parameter("pid")}"))
            {
                delGuides.Transaction = tx;
                WeaverDbParameters.Add(delGuides, "pid", patientId);
                deletedTotal += await delGuides.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (DbCommand delCircuits = session.CreateCommand(
                $"DELETE FROM weaver_epigenetic_circuits WHERE patient_id = {session.Dialect.Parameter("pid")}"))
            {
                delCircuits.Transaction = tx;
                WeaverDbParameters.Add(delCircuits, "pid", patientId);
                deletedTotal += await delCircuits.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return deletedTotal;
    }

    /// <summary>
    /// Materializes an <see cref="EpigeneticCircuitRecord"/> from the current row of an open data reader.
    /// </summary>
    /// <param name="reader">Positioned data reader matching <see cref="CircuitSelectColumns"/>.</param>
    /// <returns>The populated circuit record.</returns>
    private static EpigeneticCircuitRecord ReadCircuit(DbDataReader reader)
    {
        // WHAT: Maps an aligned SQL result row into an immutable EpigeneticCircuitRecord instance.
        // WHY:  Avoids duplicating 15-column ordinal parsing across GetCircuitByIdAsync and ListCircuitsByPatientAsync.
        // HOW:  Reads columns by ordinal index and applies safe enum and nullable timestamp coercion.
        DateTimeOffset? approvedAtUtc = reader.IsDBNull(11) ? null : WeaverDbValues.ReadTimestamp(reader, 11);
        return new EpigeneticCircuitRecord
        {
            CircuitId = reader.GetString(0),
            PatientId = reader.GetString(1),
            Regimen = ParseEnumOrDefault(reader.GetString(2), TherapeuticRegimen.MaleToFemale),
            TargetTissue = ParseEnumOrDefault(reader.GetString(3), TargetTissueType.GonadalTarget),
            EffectorPlatform = ParseEnumOrDefault(reader.GetString(4), EpigeneticEffectorPlatform.CrisprOffOn),
            CircuitStatus = ParseEnumOrDefault(reader.GetString(5), EpigeneticCircuitStatus.Draft),
            PredictedTtrPercent = Convert.ToDouble(reader.GetValue(6), CultureInfo.InvariantCulture),
            SeparatrixMargin = Convert.ToDouble(reader.GetValue(7), CultureInfo.InvariantCulture),
            MinOffTargetSpecificityScore = Convert.ToDouble(reader.GetValue(8), CultureInfo.InvariantCulture),
            NotesEncrypted = reader.IsDBNull(9) ? null : reader.GetString(9),
            CreatedAtUtc = WeaverDbValues.ReadTimestamp(reader, 10),
            ApprovedAtUtc = approvedAtUtc,
            ApprovalPayloadSha256 = reader.IsDBNull(12) ? null : reader.GetString(12),
            ApprovalSignatureBase64 = reader.IsDBNull(13) ? null : reader.GetString(13),
            ApprovalSignerKeyId = reader.IsDBNull(14) ? null : reader.GetString(14)
        };
    }

    /// <summary>
    /// Parses a persisted string enum column with a safe fallback when unrecognized tokens appear.
    /// </summary>
    /// <typeparam name="TEnum">Target enumeration type.</typeparam>
    /// <param name="raw">Raw string column value.</param>
    /// <param name="fallback">Default enumeration value returned if parsing fails.</param>
    /// <returns>Parsed enumeration member or <paramref name="fallback"/>.</returns>
    private static TEnum ParseEnumOrDefault<TEnum>(string raw, TEnum fallback)
        where TEnum : struct, Enum
    {
        // WHAT: Case-insensitive enum parser with deterministic fallback.
        // WHY:  Prevents reader deserialization exceptions when legacy or corrupted enum labels exist in storage.
        // HOW:  Attempts Enum.TryParse and returns the parsed value on match or fallback otherwise.
        return Enum.TryParse(raw, ignoreCase: true, out TEnum parsed) ? parsed : fallback;
    }
}
