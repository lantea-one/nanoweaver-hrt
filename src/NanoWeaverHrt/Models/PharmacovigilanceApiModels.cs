using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Request payload for recording a post-implantation CTCAE v5.0 adverse event against a wet-lab fabrication lot.
/// </summary>
[SwaggerSchema(Title = "RecordAdverseEventRequest", Description = "CTCAE v5.0 post-implantation adverse event log request for a fabrication lot.")]
public sealed record RecordAdverseEventRequest : IExamplesProvider<RecordAdverseEventRequest>
{
    /// <summary>
    /// Gets or initializes the CTCAE v5.0 clinical severity classification (Grade 1–4).
    /// </summary>
    [Description("CTCAE v5.0 adverse event severity grade (Grade1Mild through Grade4LifeThreatening).")]
    [JsonPropertyName("severityGrade")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AdverseEventSeverityGrade SeverityGrade { get; init; } = AdverseEventSeverityGrade.Grade1Mild;

    /// <summary>
    /// Gets or initializes the standardized symptom category descriptor (1–128 characters).
    /// </summary>
    [Description("Clinical symptom category descriptor (1-128 characters).")]
    [JsonPropertyName("symptomCategory")]
    public string SymptomCategory { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes elapsed hours between implantation and symptom onset (0 to 87,600 hours).
    /// </summary>
    [Description("Elapsed hours post-implantation when symptoms emerged (0 to 87,600 hours).")]
    [JsonPropertyName("onsetHoursPostImplant")]
    public double OnsetHoursPostImplant { get; init; }

    /// <summary>
    /// Gets or initializes optional clinical notes encrypted at rest via AES-256-GCM (max 2,000 characters).
    /// </summary>
    [Description("Optional clinician notes encrypted at rest with AES-256-GCM (max 2,000 characters).")]
    [JsonPropertyName("clinicalNotes")]
    public string? ClinicalNotes { get; init; }

    /// <summary>
    /// Gets or initializes an optional deterministic UTC observation timestamp (defaults to server UTC time).
    /// </summary>
    [Description("Optional UTC observation timestamp; defaults to server UTC time.")]
    [JsonPropertyName("recordedAtUtc")]
    public DateTimeOffset? RecordedAtUtc { get; init; }

    /// <inheritdoc/>
    public RecordAdverseEventRequest GetExamples() => new()
    {
        SeverityGrade = AdverseEventSeverityGrade.Grade1Mild,
        SymptomCategory = "InjectionSiteErythema",
        OnsetHoursPostImplant = 4.5,
        ClinicalNotes = "Localized redness at subcutaneous implant site; vital signs normal.",
        RecordedAtUtc = DateTimeOffset.Parse("2026-11-23T14:30:00Z", CultureInfo.InvariantCulture)
    };
}

/// <summary>
/// Response payload returned after persisting an adverse event and evaluating pharmacovigilance quarantine posture.
/// </summary>
[SwaggerSchema(Title = "AdverseEventResponse", Description = "Persisted CTCAE v5.0 adverse event with recalculated lot pharmacovigilance posture.")]
public sealed record AdverseEventResponse : IExamplesProvider<AdverseEventResponse>
{
    /// <summary>
    /// Gets or initializes the persisted decrypted adverse event summary.
    /// </summary>
    [Description("Persisted post-implantation adverse event summary.")]
    [JsonPropertyName("event")]
    public LotAdverseEventSummary Event { get; init; } = new();

    /// <summary>
    /// Gets or initializes the recalculated lot pharmacovigilance surveillance report immediately after persistence.
    /// </summary>
    [Description("Recalculated lot pharmacovigilance surveillance and quarantine report.")]
    [JsonPropertyName("reportAfterEvent")]
    public LotPharmacovigilanceReport ReportAfterEvent { get; init; } = new();

    /// <inheritdoc/>
    public AdverseEventResponse GetExamples() => new()
    {
        Event = new LotAdverseEventSummary().GetExamples(),
        ReportAfterEvent = new LotPharmacovigilanceReport().GetExamples()
    };
}

/// <summary>
/// Request payload for recording a post-implantation CARPA complement, cytokine, and humoral immunogenicity panel.
/// </summary>
[SwaggerSchema(Title = "RecordImmunogenicityAssayRequest", Description = "CARPA complement, TLR9 cytokine, and anti-DNA humoral immunogenicity panel log request.")]
public sealed record RecordImmunogenicityAssayRequest : IExamplesProvider<RecordImmunogenicityAssayRequest>
{
    /// <summary>
    /// Gets or initializes total hemolytic complement activity CH50 (%) (0–200%).
    /// </summary>
    [Description("Total hemolytic complement activity CH50 % (0 to 200).")]
    [JsonPropertyName("ch50Percent")]
    public double Ch50Percent { get; init; } = 95.0;

    /// <summary>
    /// Gets or initializes complement anaphylatoxin C3a concentration in ng/mL (0–50,000 ng/mL).
    /// </summary>
    [Description("Complement C3a concentration in ng/mL (0 to 50,000).")]
    [JsonPropertyName("c3aNgMl")]
    public double C3aNgMl { get; init; } = 100.0;

    /// <summary>
    /// Gets or initializes complement anaphylatoxin C5a concentration in ng/mL (0–10,000 ng/mL).
    /// </summary>
    [Description("Complement C5a concentration in ng/mL (0 to 10,000).")]
    [JsonPropertyName("c5aNgMl")]
    public double C5aNgMl { get; init; } = 5.0;

    /// <summary>
    /// Gets or initializes serum IL-6 cytokine concentration in pg/mL (0–100,000 pg/mL).
    /// </summary>
    [Description("Serum IL-6 concentration in pg/mL (0 to 100,000).")]
    [JsonPropertyName("il6PgMl")]
    public double Il6PgMl { get; init; } = 3.0;

    /// <summary>
    /// Gets or initializes serum TNF-alpha cytokine concentration in pg/mL (0–50,000 pg/mL).
    /// </summary>
    [Description("Serum TNF-alpha concentration in pg/mL (0 to 50,000).")]
    [JsonPropertyName("tnfaPgMl")]
    public double TnfaPgMl { get; init; } = 4.0;

    /// <summary>
    /// Gets or initializes anti-PEG IgM reciprocal antibody titer (0–100,000).
    /// </summary>
    [Description("Anti-PEG IgM reciprocal titer (0 to 100,000).")]
    [JsonPropertyName("antiPegIgmTiter")]
    public double AntiPegIgmTiter { get; init; } = 20.0;

    /// <summary>
    /// Gets or initializes anti-dsDNA IgG antibody concentration in IU/mL (0–10,000 IU/mL).
    /// </summary>
    [Description("Anti-dsDNA IgG concentration in IU/mL (0 to 10,000).")]
    [JsonPropertyName("antiDsDnaIgGIuMl")]
    public double AntiDsDnaIgGIuMl { get; init; } = 8.0;

    /// <summary>
    /// Gets or initializes fold-change TLR9 innate signaling activation index (0–100).
    /// </summary>
    [Description("Fold-change TLR9 activation index relative to baseline (0 to 100).")]
    [JsonPropertyName("tlr9ActivationIndex")]
    public double Tlr9ActivationIndex { get; init; } = 1.0;

    /// <summary>
    /// Gets or initializes an optional deterministic UTC assay timestamp (defaults to server UTC time).
    /// </summary>
    [Description("Optional UTC timestamp of the laboratory assay; defaults to server UTC time.")]
    [JsonPropertyName("recordedAtUtc")]
    public DateTimeOffset? RecordedAtUtc { get; init; }

    /// <inheritdoc/>
    public RecordImmunogenicityAssayRequest GetExamples() => new()
    {
        Ch50Percent = 96.0,
        C3aNgMl = 110.0,
        C5aNgMl = 5.5,
        Il6PgMl = 3.2,
        TnfaPgMl = 4.1,
        AntiPegIgmTiter = 20.0,
        AntiDsDnaIgGIuMl = 8.0,
        Tlr9ActivationIndex = 1.1,
        RecordedAtUtc = DateTimeOffset.Parse("2026-11-23T15:00:00Z", CultureInfo.InvariantCulture)
    };
}

/// <summary>
/// Response payload returned after persisting an immunogenicity panel and evaluating quarantine escalation.
/// </summary>
[SwaggerSchema(Title = "ImmunogenicityAssayResponse", Description = "Persisted CARPA/TLR9 immunogenicity panel with recalculated pharmacovigilance report.")]
public sealed record ImmunogenicityAssayResponse : IExamplesProvider<ImmunogenicityAssayResponse>
{
    /// <summary>
    /// Gets or initializes the persisted quantitative immunogenicity assay record.
    /// </summary>
    [Description("Persisted immunogenicity assay panel record.")]
    [JsonPropertyName("assay")]
    public LotImmunogenicityAssayRecord Assay { get; init; } = new();

    /// <summary>
    /// Gets or initializes the recalculated lot pharmacovigilance surveillance report immediately after persistence.
    /// </summary>
    [Description("Recalculated lot pharmacovigilance surveillance and quarantine report.")]
    [JsonPropertyName("reportAfterAssay")]
    public LotPharmacovigilanceReport ReportAfterAssay { get; init; } = new();

    /// <inheritdoc/>
    public ImmunogenicityAssayResponse GetExamples() => new()
    {
        Assay = new LotImmunogenicityAssayRecord().GetExamples(),
        ReportAfterAssay = new LotPharmacovigilanceReport().GetExamples()
    };
}
