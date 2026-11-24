using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Fabrication;

/// <summary>
/// Aggregate clinical pharmacovigilance safety posture derived from post-implantation CTCAE v5.0 adverse events
/// and quantitative CARPA/TLR9 immunogenicity biomarker assays.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PharmacovigilancePosture
{
    /// <summary>
    /// No adverse events or elevated CARPA/TLR9 immunogenicity markers recorded; batch remains clinically nominal.
    /// </summary>
    Nominal = 0,

    /// <summary>
    /// Mild/moderate (CTCAE Grade 1–2) adverse event or sub-critical complement/cytokine elevation detected; clinical monitoring indicated.
    /// </summary>
    Monitor = 1,

    /// <summary>
    /// Severe/life-threatening event (CTCAE Grade 3–4), CARPA complement storm, or TLR9/humoral hyperactivation detected; lot quarantined fail-closed.
    /// </summary>
    CarpaQuarantine = 2
}

/// <summary>
/// Normative clinical toxicology and immunogenicity thresholds governing post-implantation surveillance
/// and automated fail-closed quarantine escalation for DNA origami HRT depots.
/// </summary>
public static class PharmacovigilanceThresholds
{
    /// <summary>
    /// Lower CH50 hemolytic complement threshold (%) below which sub-critical complement consumption monitoring activates (60.0%).
    /// </summary>
    public const double Ch50MonitorLowPercent = 60.0;

    /// <summary>
    /// Critical CH50 hemolytic complement threshold (%) at or below which severe CARPA complement depletion quarantine triggers (40.0%).
    /// </summary>
    public const double Ch50CarpaCriticalLowPercent = 40.0;

    /// <summary>
    /// Minimum valid physical measurement bound for CH50 hemolytic complement percentage (0.0%).
    /// </summary>
    public const double MinCh50Percent = 0.0;

    /// <summary>
    /// Maximum valid physical measurement bound for CH50 hemolytic complement percentage (200.0%).
    /// </summary>
    public const double MaxCh50Percent = 200.0;

    /// <summary>
    /// Complement C3a concentration threshold (ng/mL) triggering clinical monitoring (250.0 ng/mL).
    /// </summary>
    public const double C3aMonitorNgMl = 250.0;

    /// <summary>
    /// Complement C3a concentration threshold (ng/mL) triggering automatic CARPA lot quarantine (500.0 ng/mL).
    /// </summary>
    public const double C3aCarpaCriticalNgMl = 500.0;

    /// <summary>
    /// Maximum valid assay measurement bound for C3a anaphylatoxin concentration (50,000.0 ng/mL).
    /// </summary>
    public const double MaxC3aNgMl = 50_000.0;

    /// <summary>
    /// Complement C5a concentration threshold (ng/mL) triggering clinical monitoring (15.0 ng/mL).
    /// </summary>
    public const double C5aMonitorNgMl = 15.0;

    /// <summary>
    /// Complement C5a concentration threshold (ng/mL) triggering automatic CARPA lot quarantine (40.0 ng/mL).
    /// </summary>
    public const double C5aCarpaCriticalNgMl = 40.0;

    /// <summary>
    /// Maximum valid assay measurement bound for C5a anaphylatoxin concentration (10,000.0 ng/mL).
    /// </summary>
    public const double MaxC5aNgMl = 10_000.0;

    /// <summary>
    /// Serum IL-6 concentration threshold (pg/mL) triggering innate cytokine monitoring (20.0 pg/mL).
    /// </summary>
    public const double Il6MonitorPgMl = 20.0;

    /// <summary>
    /// Serum IL-6 concentration threshold (pg/mL) triggering automatic cytokine-storm quarantine (100.0 pg/mL).
    /// </summary>
    public const double Il6CytokineStormCriticalPgMl = 100.0;

    /// <summary>
    /// Maximum valid assay measurement bound for serum IL-6 concentration (100,000.0 pg/mL).
    /// </summary>
    public const double MaxIl6PgMl = 100_000.0;

    /// <summary>
    /// Serum TNF-alpha concentration threshold (pg/mL) triggering innate cytokine monitoring (15.0 pg/mL).
    /// </summary>
    public const double TnfaMonitorPgMl = 15.0;

    /// <summary>
    /// Serum TNF-alpha concentration threshold (pg/mL) triggering automatic cytokine-storm quarantine (50.0 pg/mL).
    /// </summary>
    public const double TnfaCytokineStormCriticalPgMl = 50.0;

    /// <summary>
    /// Maximum valid assay measurement bound for serum TNF-alpha concentration (50,000.0 pg/mL).
    /// </summary>
    public const double MaxTnfaPgMl = 50_000.0;

    /// <summary>
    /// Anti-PEG IgM reciprocal titer triggering CARPA sensitivity monitoring (80.0).
    /// </summary>
    public const double AntiPegIgmMonitorTiter = 80.0;

    /// <summary>
    /// Anti-PEG IgM reciprocal titer triggering automatic CARPA lot quarantine (320.0).
    /// </summary>
    public const double AntiPegIgmCriticalTiter = 320.0;

    /// <summary>
    /// Maximum valid assay measurement bound for anti-PEG IgM reciprocal titer (100,000.0).
    /// </summary>
    public const double MaxAntiPegIgmTiter = 100_000.0;

    /// <summary>
    /// Anti-dsDNA IgG autoantibody concentration (IU/mL) triggering humoral immunogenicity monitoring (30.0 IU/mL).
    /// </summary>
    public const double AntiDsDnaIgGMonitorIuMl = 30.0;

    /// <summary>
    /// Anti-dsDNA IgG autoantibody concentration (IU/mL) triggering automatic humoral immunogenicity quarantine (100.0 IU/mL).
    /// </summary>
    public const double AntiDsDnaIgGCriticalIuMl = 100.0;

    /// <summary>
    /// Maximum valid assay measurement bound for anti-dsDNA IgG concentration (10,000.0 IU/mL).
    /// </summary>
    public const double MaxAntiDsDnaIgGIuMl = 10_000.0;

    /// <summary>
    /// Fold-change TLR9 innate signaling activation index triggering monitoring (2.0).
    /// </summary>
    public const double Tlr9MonitorIndex = 2.0;

    /// <summary>
    /// Fold-change TLR9 innate signaling activation index triggering automatic quarantine (4.0).
    /// </summary>
    public const double Tlr9CriticalIndex = 4.0;

    /// <summary>
    /// Maximum valid fold-change TLR9 activation index bound (100.0).
    /// </summary>
    public const double MaxTlr9ActivationIndex = 100.0;

    /// <summary>
    /// Maximum allowed post-implantation symptom onset latency in hours (10 years = 87,600 h).
    /// </summary>
    public const double MaxOnsetHoursPostImplant = 87_600.0;

    /// <summary>
    /// Maximum allowed character length for symptom category descriptors.
    /// </summary>
    public const int MaxSymptomCategoryLength = 128;

    /// <summary>
    /// Maximum allowed plaintext character length for encrypted clinical adverse event notes.
    /// </summary>
    public const int MaxClinicalNotesLength = 2_000;
}

/// <summary>
/// Authorized clinical summary view of a persisted <see cref="LotAdverseEventRecord"/> with optional notes decrypted.
/// </summary>
[SwaggerSchema(Title = "LotAdverseEventSummary", Description = "Decrypted post-implantation CTCAE v5.0 adverse event summary.")]
public sealed record LotAdverseEventSummary : IExamplesProvider<LotAdverseEventSummary>
{
    /// <summary>
    /// Gets or initializes the unique identifier of this clinical adverse event.
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
    /// Gets or initializes the patient identifier associated with this fabrication lot.
    /// </summary>
    [Description("Patient identifier owning the associated fabrication lot.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the CTCAE v5.0 severity grade.
    /// </summary>
    [Description("CTCAE v5.0 severity grade.")]
    [JsonPropertyName("severityGrade")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AdverseEventSeverityGrade SeverityGrade { get; init; } = AdverseEventSeverityGrade.Grade1Mild;

    /// <summary>
    /// Gets or initializes the standardized symptom category descriptor.
    /// </summary>
    [Description("Clinical symptom category descriptor.")]
    [JsonPropertyName("symptomCategory")]
    public string SymptomCategory { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the elapsed hours between implantation and symptom onset.
    /// </summary>
    [Description("Elapsed hours post-implantation when symptoms emerged.")]
    [JsonPropertyName("onsetHoursPostImplant")]
    public double OnsetHoursPostImplant { get; init; }

    /// <summary>
    /// Gets or initializes the decrypted optional clinical notes (null when omitted).
    /// </summary>
    [Description("Optional decrypted clinical narrative notes.")]
    [JsonPropertyName("clinicalNotes")]
    public string? ClinicalNotes { get; init; }

    /// <summary>
    /// Gets or initializes the authenticated username who reported this event.
    /// </summary>
    [Description("Authenticated clinician or operator username who reported this event.")]
    [JsonPropertyName("reportedBy")]
    public string ReportedBy { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the microsecond-normalized UTC timestamp when the adverse event was logged.
    /// </summary>
    [Description("UTC timestamp when this adverse event was logged.")]
    [JsonPropertyName("recordedAtUtc")]
    public DateTimeOffset RecordedAtUtc { get; init; }

    /// <inheritdoc/>
    public LotAdverseEventSummary GetExamples() => new()
    {
        EventId = "aevt_20261123_001",
        LotId = "lot_20261123_001",
        PatientId = "patient_alice",
        SeverityGrade = AdverseEventSeverityGrade.Grade1Mild,
        SymptomCategory = "InjectionSiteErythema",
        OnsetHoursPostImplant = 4.5,
        ClinicalNotes = "Mild localized erythema resolved within 12 hours without intervention.",
        ReportedBy = "dr_lin",
        RecordedAtUtc = DateTimeOffset.Parse("2026-11-23T14:30:00.000000Z", CultureInfo.InvariantCulture)
    };
}

/// <summary>
/// Comprehensive post-implantation pharmacovigilance, CARPA complement surveillance, TLR9 cytokine storm,
/// and automated quarantine escalation verdict for a wet-lab fabrication lot.
/// </summary>
[SwaggerSchema(Title = "LotPharmacovigilanceReport", Description = "Post-implantation CTCAE v5.0 and CARPA/TLR9 pharmacovigilance surveillance report.")]
public sealed record LotPharmacovigilanceReport : IExamplesProvider<LotPharmacovigilanceReport>
{
    /// <summary>
    /// Gets or initializes the evaluated wet-lab fabrication lot identifier.
    /// </summary>
    [Description("Wet-lab fabrication lot identifier.")]
    [JsonPropertyName("lotId")]
    public string LotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the patient identifier owning the fabrication lot.
    /// </summary>
    [Description("Patient identifier owning this fabrication lot.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the current lifecycle status of the fabrication lot after pharmacovigilance evaluation.
    /// </summary>
    [Description("Current lifecycle status of the fabrication lot.")]
    [JsonPropertyName("lotStatus")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FabricationLotStatus LotStatus { get; init; } = FabricationLotStatus.Released;

    /// <summary>
    /// Gets or initializes the UTC timestamp when this pharmacovigilance evaluation completed.
    /// </summary>
    [Description("UTC timestamp when pharmacovigilance evaluation completed.")]
    [JsonPropertyName("evaluatedAtUtc")]
    public DateTimeOffset EvaluatedAtUtc { get; init; }

    /// <summary>
    /// Gets or initializes the aggregate pharmacovigilance safety posture (<c>Nominal</c>, <c>Monitor</c>, or <c>CarpaQuarantine</c>).
    /// </summary>
    [Description("Aggregate pharmacovigilance posture classification.")]
    [JsonPropertyName("posture")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PharmacovigilancePosture Posture { get; init; } = PharmacovigilancePosture.Nominal;

    /// <summary>
    /// Gets or initializes the highest CTCAE v5.0 adverse event grade recorded on this lot, or null when none exist.
    /// </summary>
    [Description("Highest CTCAE v5.0 grade recorded for this lot, or null if no events exist.")]
    [JsonPropertyName("highestSeverityGrade")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AdverseEventSeverityGrade? HighestSeverityGrade { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether fail-closed pharmacovigilance quarantine was triggered.
    /// </summary>
    [Description("True when severe CTCAE Grade >= 3, CARPA, TLR9 cytokine storm, or humoral escalation triggered lot quarantine.")]
    [JsonPropertyName("isQuarantineTriggered")]
    public bool IsQuarantineTriggered { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether an emergency clinical lot recall is recommended.
    /// </summary>
    [Description("True when severe pharmacovigilance or CARPA signals warrant emergency lot recall.")]
    [JsonPropertyName("emergencyRecallRecommended")]
    public bool EmergencyRecallRecommended { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether critical CARPA complement activation markers were exceeded.
    /// </summary>
    [Description("True when CH50, C3a, C5a, or anti-PEG IgM crossed critical CARPA thresholds.")]
    [JsonPropertyName("carpaSignalDetected")]
    public bool CarpaSignalDetected { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether critical TLR9 innate signaling or cytokine storm markers were exceeded.
    /// </summary>
    [Description("True when IL-6, TNF-alpha, or TLR9 activation index crossed critical thresholds.")]
    [JsonPropertyName("tlr9CytokineStormDetected")]
    public bool Tlr9CytokineStormDetected { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether critical humoral anti-PEG IgM or anti-dsDNA IgG autoantibodies were detected.
    /// </summary>
    [Description("True when anti-PEG IgM or anti-dsDNA IgG crossed critical humoral immunogenicity thresholds.")]
    [JsonPropertyName("humoralImmunogenicityDetected")]
    public bool HumoralImmunogenicityDetected { get; init; }

    /// <summary>
    /// Gets or initializes the total count of recorded adverse events for this lot.
    /// </summary>
    [Description("Total count of recorded CTCAE v5.0 adverse events for this lot.")]
    [JsonPropertyName("adverseEventCount")]
    public int AdverseEventCount { get; init; }

    /// <summary>
    /// Gets or initializes the total count of recorded immunogenicity assay panels for this lot.
    /// </summary>
    [Description("Total count of recorded immunogenicity assay panels for this lot.")]
    [JsonPropertyName("immunogenicityAssayCount")]
    public int ImmunogenicityAssayCount { get; init; }

    /// <summary>
    /// Gets or initializes the ordered list of distinct clinical/biomarker signal descriptions detected.
    /// </summary>
    [Description("Ordered list of triggered pharmacovigilance and immunogenicity diagnostic signals.")]
    [JsonPropertyName("triggeredSignals")]
    public IReadOnlyList<string> TriggeredSignals { get; init; } = [];

    /// <summary>
    /// Gets or initializes a plain-English diagnostic summary explaining the pharmacovigilance posture.
    /// </summary>
    [Description("Plain-English summary of post-implantation safety posture and quarantine status.")]
    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the chronological list of decrypted adverse event summaries for this lot.
    /// </summary>
    [Description("Chronologically ordered adverse event summaries for this lot.")]
    [JsonPropertyName("adverseEvents")]
    public IReadOnlyList<LotAdverseEventSummary> AdverseEvents { get; init; } = [];

    /// <summary>
    /// Gets or initializes the chronological list of quantitative immunogenicity assay panels for this lot.
    /// </summary>
    [Description("Chronologically ordered immunogenicity assay panels for this lot.")]
    [JsonPropertyName("immunogenicityAssays")]
    public IReadOnlyList<LotImmunogenicityAssayRecord> ImmunogenicityAssays { get; init; } = [];

    /// <inheritdoc/>
    public LotPharmacovigilanceReport GetExamples() => new()
    {
        LotId = "lot_20261123_001",
        PatientId = "patient_alice",
        LotStatus = FabricationLotStatus.Released,
        EvaluatedAtUtc = DateTimeOffset.Parse("2026-11-23T16:00:00.000000Z", CultureInfo.InvariantCulture),
        Posture = PharmacovigilancePosture.Nominal,
        HighestSeverityGrade = null,
        IsQuarantineTriggered = false,
        EmergencyRecallRecommended = false,
        CarpaSignalDetected = false,
        Tlr9CytokineStormDetected = false,
        HumoralImmunogenicityDetected = false,
        AdverseEventCount = 0,
        ImmunogenicityAssayCount = 1,
        TriggeredSignals = [],
        Summary = "Nominal post-implantation pharmacovigilance: 0 adverse event(s), 1 assay(s), no CARPA or TLR9 signals detected.",
        AdverseEvents = [],
        ImmunogenicityAssays = [new LotImmunogenicityAssayRecord().GetExamples()]
    };
}
