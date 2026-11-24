using System.Data.Common;
using System.Globalization;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Multi-engine relational repository persisting post-implantation adverse events and CARPA/TLR9 immunogenicity
/// assay panels across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Concrete implementation of <see cref="IClinicalPharmacovigilanceRepository"/>
/// backed by <see cref="IWeaverDbConnectionFactory"/>.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 41 (P215): Persists CTCAE v5.0 adverse events and quantitative
/// immunogenicity biomarkers deterministically across all four relational engines.
/// </para>
/// <para>
/// <strong>How it works:</strong> Uses explicit <c>{ await using ... }</c> block scopes, dialect-aware timestamp
/// coercion, ordinal reader extraction, and deterministic chronological sorting (<c>recorded_at_utc ASC, id ASC</c>).
/// </para>
/// </remarks>
public sealed class ClinicalPharmacovigilanceRepository : IClinicalPharmacovigilanceRepository
{
    /// <summary>
    /// Database connection factory supplying dialect-aware database sessions.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClinicalPharmacovigilanceRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">Multi-engine database connection factory.</param>
    public ClinicalPharmacovigilanceRepository(IWeaverDbConnectionFactory connectionFactory)
    {
        // WHAT: Stores the validated database connection factory.
        // WHY:  Every pharmacovigilance query opens a short-lived session bound to the configured SQL engine.
        // HOW:  Guards against null injection before assigning the readonly field.
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc/>
    public async Task AddAdverseEventAsync(
        LotAdverseEventRecord record,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Inserts a new post-implantation adverse event into weaver_lot_adverse_events.
        // WHY:  Milestone 41 (P215): Persists CTCAE v5.0 safety telemetry linked to a fabrication lot and patient.
        // HOW:  Validates required fields, binds dialect-neutral parameters, and executes inside an explicit block scope.
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.EventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.LotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.PatientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.SymptomCategory);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.ReportedBy);

        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            string sql =
                "INSERT INTO weaver_lot_adverse_events " +
                "(event_id, lot_id, patient_id, severity_grade, symptom_category, onset_hours_post_implant, clinical_notes_encrypted, reported_by, recorded_at_utc) " +
                $"VALUES ({session.Dialect.Parameter("eid")}, {session.Dialect.Parameter("lid")}, {session.Dialect.Parameter("pid")}, " +
                $"{session.Dialect.Parameter("sev")}, {session.Dialect.Parameter("sym")}, {session.Dialect.Parameter("ons")}, " +
                $"{session.Dialect.Parameter("notes")}, {session.Dialect.Parameter("rby")}, {session.Dialect.Parameter("rat")})";

            await using DbCommand cmd = session.CreateCommand(sql);
            WeaverDbParameters.Add(cmd, "eid", record.EventId);
            WeaverDbParameters.Add(cmd, "lid", record.LotId);
            WeaverDbParameters.Add(cmd, "pid", record.PatientId);
            WeaverDbParameters.Add(cmd, "sev", record.SeverityGrade.ToString());
            WeaverDbParameters.Add(cmd, "sym", record.SymptomCategory);
            WeaverDbParameters.Add(cmd, "ons", record.OnsetHoursPostImplant);
            WeaverDbParameters.AddNullable(cmd, "notes", record.ClinicalNotesEncrypted);
            WeaverDbParameters.Add(cmd, "rby", record.ReportedBy);
            WeaverDbParameters.Add(cmd, "rat", session.Dialect.TimestampValue(record.RecordedAtUtc));

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LotAdverseEventRecord>> ListAdverseEventsByLotAsync(
        string lotId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Queries all clinical adverse events for a fabrication lot ordered chronologically ascending.
        // WHY:  Pharmacovigilance posture classification and audit timelines require deterministic chronological ordering.
        // HOW:  Executes a parameterized SELECT and maps each row by ordinal index without LINQ allocations.
        ArgumentException.ThrowIfNullOrWhiteSpace(lotId);

        List<LotAdverseEventRecord> results = [];
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                "SELECT event_id, lot_id, patient_id, severity_grade, symptom_category, onset_hours_post_implant, clinical_notes_encrypted, reported_by, recorded_at_utc " +
                $"FROM weaver_lot_adverse_events WHERE lot_id = {session.Dialect.Parameter("lid")} ORDER BY recorded_at_utc ASC, event_id ASC");
            WeaverDbParameters.Add(cmd, "lid", lotId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string rawSeverity = reader.GetString(3);
                AdverseEventSeverityGrade severityGrade = Enum.TryParse(rawSeverity, ignoreCase: true, out AdverseEventSeverityGrade parsedGrade)
                    ? parsedGrade
                    : AdverseEventSeverityGrade.Grade1Mild;

                results.Add(new LotAdverseEventRecord
                {
                    EventId = reader.GetString(0),
                    LotId = reader.GetString(1),
                    PatientId = reader.GetString(2),
                    SeverityGrade = severityGrade,
                    SymptomCategory = reader.GetString(4),
                    OnsetHoursPostImplant = Convert.ToDouble(reader.GetValue(5), CultureInfo.InvariantCulture),
                    ClinicalNotesEncrypted = reader.IsDBNull(6) ? null : reader.GetString(6),
                    ReportedBy = reader.GetString(7),
                    RecordedAtUtc = WeaverDbValues.ReadTimestamp(reader, 8)
                });
            }
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task AddImmunogenicityAssayAsync(
        LotImmunogenicityAssayRecord record,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Inserts a new quantitative CARPA/TLR9 immunogenicity assay panel into weaver_lot_immunogenicity_assays.
        // WHY:  Milestone 41 (P215): Persists complement consumption, cytokine surge, and antibody titers for quarantine detection.
        // HOW:  Validates required identifiers, binds dialect-neutral parameters, and executes inside an explicit block scope.
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.AssayId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.LotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.PatientId);

        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            string sql =
                "INSERT INTO weaver_lot_immunogenicity_assays " +
                "(assay_id, lot_id, patient_id, ch50_percent, c3a_ng_ml, c5a_ng_ml, il6_pg_ml, tnfa_pg_ml, anti_peg_igm_titer, anti_dsdna_igg_iu_ml, tlr9_activation_index, recorded_at_utc) " +
                $"VALUES ({session.Dialect.Parameter("aid")}, {session.Dialect.Parameter("lid")}, {session.Dialect.Parameter("pid")}, " +
                $"{session.Dialect.Parameter("ch50")}, {session.Dialect.Parameter("c3a")}, {session.Dialect.Parameter("c5a")}, " +
                $"{session.Dialect.Parameter("il6")}, {session.Dialect.Parameter("tnfa")}, {session.Dialect.Parameter("apeg")}, " +
                $"{session.Dialect.Parameter("adsdna")}, {session.Dialect.Parameter("tlr9")}, {session.Dialect.Parameter("rat")})";

            await using DbCommand cmd = session.CreateCommand(sql);
            WeaverDbParameters.Add(cmd, "aid", record.AssayId);
            WeaverDbParameters.Add(cmd, "lid", record.LotId);
            WeaverDbParameters.Add(cmd, "pid", record.PatientId);
            WeaverDbParameters.Add(cmd, "ch50", record.Ch50Percent);
            WeaverDbParameters.Add(cmd, "c3a", record.C3aNgMl);
            WeaverDbParameters.Add(cmd, "c5a", record.C5aNgMl);
            WeaverDbParameters.Add(cmd, "il6", record.Il6PgMl);
            WeaverDbParameters.Add(cmd, "tnfa", record.TnfaPgMl);
            WeaverDbParameters.Add(cmd, "apeg", record.AntiPegIgmTiter);
            WeaverDbParameters.Add(cmd, "adsdna", record.AntiDsDnaIgGIuMl);
            WeaverDbParameters.Add(cmd, "tlr9", record.Tlr9ActivationIndex);
            WeaverDbParameters.Add(cmd, "rat", session.Dialect.TimestampValue(record.RecordedAtUtc));

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LotImmunogenicityAssayRecord>> ListImmunogenicityAssaysByLotAsync(
        string lotId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Queries all immunogenicity assay panels for a fabrication lot ordered chronologically ascending.
        // WHY:  Longitudinal biomarker trend inspection and fail-closed quarantine gating depend on deterministic ordering.
        // HOW:  Executes a parameterized SELECT and maps each row by ordinal index without LINQ allocations.
        ArgumentException.ThrowIfNullOrWhiteSpace(lotId);

        List<LotImmunogenicityAssayRecord> results = [];
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                "SELECT assay_id, lot_id, patient_id, ch50_percent, c3a_ng_ml, c5a_ng_ml, il6_pg_ml, tnfa_pg_ml, anti_peg_igm_titer, anti_dsdna_igg_iu_ml, tlr9_activation_index, recorded_at_utc " +
                $"FROM weaver_lot_immunogenicity_assays WHERE lot_id = {session.Dialect.Parameter("lid")} ORDER BY recorded_at_utc ASC, assay_id ASC");
            WeaverDbParameters.Add(cmd, "lid", lotId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new LotImmunogenicityAssayRecord
                {
                    AssayId = reader.GetString(0),
                    LotId = reader.GetString(1),
                    PatientId = reader.GetString(2),
                    Ch50Percent = Convert.ToDouble(reader.GetValue(3), CultureInfo.InvariantCulture),
                    C3aNgMl = Convert.ToDouble(reader.GetValue(4), CultureInfo.InvariantCulture),
                    C5aNgMl = Convert.ToDouble(reader.GetValue(5), CultureInfo.InvariantCulture),
                    Il6PgMl = Convert.ToDouble(reader.GetValue(6), CultureInfo.InvariantCulture),
                    TnfaPgMl = Convert.ToDouble(reader.GetValue(7), CultureInfo.InvariantCulture),
                    AntiPegIgmTiter = Convert.ToDouble(reader.GetValue(8), CultureInfo.InvariantCulture),
                    AntiDsDnaIgGIuMl = Convert.ToDouble(reader.GetValue(9), CultureInfo.InvariantCulture),
                    Tlr9ActivationIndex = Convert.ToDouble(reader.GetValue(10), CultureInfo.InvariantCulture),
                    RecordedAtUtc = WeaverDbValues.ReadTimestamp(reader, 11)
                });
            }
        }

        return results;
    }
}
