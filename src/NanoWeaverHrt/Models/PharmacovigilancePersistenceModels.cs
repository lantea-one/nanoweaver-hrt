using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Common Terminology Criteria for Adverse Events (CTCAE v5.0) clinical severity grading scale
/// for post-implantation reactions following nanocarrier HRT depot administration.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AdverseEventSeverityGrade
{
    /// <summary>
    /// Grade 1 Mild: asymptomatic or mild localized symptoms; clinical observation indicated, no intervention required.
    /// </summary>
    Grade1Mild = 1,

    /// <summary>
    /// Grade 2 Moderate: minimal, local, or noninvasive intervention indicated; monitoring escalation triggered.
    /// </summary>
    Grade2Moderate = 2,

    /// <summary>
    /// Grade 3 Severe: medically significant reaction requiring intervention or hospitalization; triggers automatic lot quarantine.
    /// </summary>
    Grade3Severe = 3,

    /// <summary>
    /// Grade 4 Life-Threatening: urgent clinical intervention or anaphylaxis/cytokine storm management required; triggers automatic lot quarantine and recall recommendation.
    /// </summary>
    Grade4LifeThreatening = 4
}

/// <summary>
/// Persistent database record capturing a post-implantation clinical adverse event linked to a
/// physically fabricated nanocarrier HRT batch.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A row stored in <c>weaver_lot_adverse_events</c> capturing CTCAE v5.0 grade,
/// symptom category, post-implantation onset latency, and AES-256-GCM encrypted clinical notes.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 41 (P215): Provides durable post-market clinical pharmacovigilance
/// traceability across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB persistence engines.
/// </para>
/// <para>
/// <strong>How it works:</strong> Inserted chronologically via <see cref="NanoWeaverHrt.Storage.IClinicalPharmacovigilanceRepository"/>
/// with atomic GDPR Article 17 erasure cascade linkage through <c>patient_id</c> and <c>lot_id</c>.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "LotAdverseEventRecord", Description = "Persisted CTCAE v5.0 post-implantation adverse event record for a fabrication lot.")]
public sealed record LotAdverseEventRecord : IExamplesProvider<LotAdverseEventRecord>
{
    /// <summary>
    /// Gets or initializes the unique identifier of this clinical adverse event record.
    /// </summary>
    [Description("Unique identifier of this adverse event record.")]
    [JsonPropertyName("eventId")]
    public string EventId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the associated wet-lab fabrication lot identifier.
    /// </summary>
    [Description("Associated wet-lab fabrication lot identifier.")]
    [JsonPropertyName("lotId")]
    public string LotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the patient identifier owning the associated fabrication lot.
    /// </summary>
    [Description("Patient identifier owning the associated fabrication lot.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the CTCAE v5.0 adverse event severity classification.
    /// </summary>
    [Description("CTCAE v5.0 clinical severity grade (Grade1Mild through Grade4LifeThreatening).")]
    [JsonPropertyName("severityGrade")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AdverseEventSeverityGrade SeverityGrade { get; init; } = AdverseEventSeverityGrade.Grade1Mild;

    /// <summary>
    /// Gets or initializes the standardized symptom category descriptor (e.g. InjectionSiteErythema, CARPA, Anaphylaxis).
    /// </summary>
    [Description("Clinical symptom category descriptor (1-128 characters).")]
    [JsonPropertyName("symptomCategory")]
    public string SymptomCategory { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the elapsed hours between depot implantation and clinical symptom onset.
    /// </summary>
    [Description("Elapsed hours post-implantation when symptoms emerged.")]
    [JsonPropertyName("onsetHoursPostImplant")]
    public double OnsetHoursPostImplant { get; init; }

    /// <summary>
    /// Gets or initializes optional AES-256-GCM encrypted clinician narrative notes stored at rest.
    /// </summary>
    [Description("Optional AES-256-GCM encrypted clinical notes stored at rest.")]
    [JsonPropertyName("clinicalNotesEncrypted")]
    public string? ClinicalNotesEncrypted { get; init; }

    /// <summary>
    /// Gets or initializes the authenticated clinician or administrator username who logged this adverse event.
    /// </summary>
    [Description("Authenticated clinician or operator username who reported this event.")]
    [JsonPropertyName("reportedBy")]
    public string ReportedBy { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the microsecond-normalized UTC timestamp when this adverse event was recorded.
    /// </summary>
    [Description("UTC timestamp when this adverse event was recorded.")]
    [JsonPropertyName("recordedAtUtc")]
    public DateTimeOffset RecordedAtUtc { get; init; }

    /// <inheritdoc/>
    public LotAdverseEventRecord GetExamples() => new()
    {
        EventId = "aevt_20261123_001",
        LotId = "lot_20261123_001",
        PatientId = "patient_alice",
        SeverityGrade = AdverseEventSeverityGrade.Grade1Mild,
        SymptomCategory = "InjectionSiteErythema",
        OnsetHoursPostImplant = 4.5,
        ClinicalNotesEncrypted = null,
        ReportedBy = "dr_lin",
        RecordedAtUtc = DateTimeOffset.Parse("2026-11-23T14:30:00.000000Z", CultureInfo.InvariantCulture)
    };
}

/// <summary>
/// Persistent database record capturing a post-implantation complement, innate TLR9 cytokine, and humoral
/// immunogenicity laboratory panel for a nanocarrier HRT batch.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A row stored in <c>weaver_lot_immunogenicity_assays</c> tracking CH50 complement
/// consumption, anaphylatoxins (C3a, C5a), pro-inflammatory cytokines (IL-6, TNF-α), anti-PEG IgM, anti-dsDNA IgG,
/// and TLR9 activation index.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 41 (P215): Persists quantitative CARPA and TLR9 innate immune biomarkers
/// used to trigger automated fail-closed lot quarantine before severe systemic toxicity develops.
/// </para>
/// <para>
/// <strong>How it works:</strong> Inserted chronologically via <see cref="NanoWeaverHrt.Storage.IClinicalPharmacovigilanceRepository"/>
/// with atomic GDPR Article 17 erasure cascade linkage through <c>patient_id</c> and <c>lot_id</c>.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "LotImmunogenicityAssayRecord", Description = "Persisted post-implantation CARPA complement, cytokine, and anti-DNA immunogenicity assay panel.")]
public sealed record LotImmunogenicityAssayRecord : IExamplesProvider<LotImmunogenicityAssayRecord>
{
    /// <summary>
    /// Gets or initializes the unique identifier of this immunogenicity assay record.
    /// </summary>
    [Description("Unique identifier of this immunogenicity assay panel.")]
    [JsonPropertyName("assayId")]
    public string AssayId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the associated wet-lab fabrication lot identifier.
    /// </summary>
    [Description("Associated wet-lab fabrication lot identifier.")]
    [JsonPropertyName("lotId")]
    public string LotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the patient identifier owning the associated fabrication lot.
    /// </summary>
    [Description("Patient identifier owning the associated fabrication lot.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes total hemolytic complement activity (CH50) expressed as percentage of baseline (normal ~70–130%).
    /// </summary>
    [Description("Total hemolytic complement activity (CH50 %) indicating complement consumption when depressed.")]
    [JsonPropertyName("ch50Percent")]
    public double Ch50Percent { get; init; }

    /// <summary>
    /// Gets or initializes complement anaphylatoxin C3a concentration in ng/mL.
    /// </summary>
    [Description("Complement anaphylatoxin C3a concentration in ng/mL.")]
    [JsonPropertyName("c3aNgMl")]
    public double C3aNgMl { get; init; }

    /// <summary>
    /// Gets or initializes complement anaphylatoxin C5a concentration in ng/mL.
    /// </summary>
    [Description("Complement anaphylatoxin C5a concentration in ng/mL.")]
    [JsonPropertyName("c5aNgMl")]
    public double C5aNgMl { get; init; }

    /// <summary>
    /// Gets or initializes serum Interleukin-6 (IL-6) concentration in pg/mL.
    /// </summary>
    [Description("Serum IL-6 pro-inflammatory cytokine concentration in pg/mL.")]
    [JsonPropertyName("il6PgMl")]
    public double Il6PgMl { get; init; }

    /// <summary>
    /// Gets or initializes serum Tumor Necrosis Factor-alpha (TNF-α) concentration in pg/mL.
    /// </summary>
    [Description("Serum TNF-alpha cytokine concentration in pg/mL.")]
    [JsonPropertyName("tnfaPgMl")]
    public double TnfaPgMl { get; init; }

    /// <summary>
    /// Gets or initializes anti-PEG IgM reciprocal titer (accelerates CARPA upon repeat administration).
    /// </summary>
    [Description("Anti-PEG IgM reciprocal antibody titer.")]
    [JsonPropertyName("antiPegIgmTiter")]
    public double AntiPegIgmTiter { get; init; }

    /// <summary>
    /// Gets or initializes anti-double-stranded DNA IgG autoantibody concentration in IU/mL.
    /// </summary>
    [Description("Anti-dsDNA IgG antibody concentration in IU/mL.")]
    [JsonPropertyName("antiDsDnaIgGIuMl")]
    public double AntiDsDnaIgGIuMl { get; init; }

    /// <summary>
    /// Gets or initializes fold-change TLR9 innate signaling activation index relative to unstimulated baseline (1.0 = baseline).
    /// </summary>
    [Description("Fold-change TLR9 activation index relative to baseline (1.0 = nominal).")]
    [JsonPropertyName("tlr9ActivationIndex")]
    public double Tlr9ActivationIndex { get; init; }

    /// <summary>
    /// Gets or initializes the microsecond-normalized UTC timestamp when this immunogenicity panel was recorded.
    /// </summary>
    [Description("UTC timestamp when this immunogenicity assay was recorded.")]
    [JsonPropertyName("recordedAtUtc")]
    public DateTimeOffset RecordedAtUtc { get; init; }

    /// <inheritdoc/>
    public LotImmunogenicityAssayRecord GetExamples() => new()
    {
        AssayId = "immu_20261123_001",
        LotId = "lot_20261123_001",
        PatientId = "patient_alice",
        Ch50Percent = 96.0,
        C3aNgMl = 110.0,
        C5aNgMl = 5.5,
        Il6PgMl = 3.2,
        TnfaPgMl = 4.1,
        AntiPegIgmTiter = 20.0,
        AntiDsDnaIgGIuMl = 8.0,
        Tlr9ActivationIndex = 1.1,
        RecordedAtUtc = DateTimeOffset.Parse("2026-11-23T15:00:00.000000Z", CultureInfo.InvariantCulture)
    };
}
