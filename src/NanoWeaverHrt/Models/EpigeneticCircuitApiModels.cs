using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Input specification describing an individual single-guide RNA locus included when designing a
/// patient-specific epigenetic steroidogenic toggle circuit.
/// </summary>
[SwaggerSchema(Title = "GuideRnaSpecInput", Description = "Single-guide RNA target locus, protospacer sequence, CFD specificity, and predicted fold-change.")]
public sealed record GuideRnaSpecInput
{
    /// <summary>
    /// Gets or initializes the HGNC steroidogenic gene symbol targeted by this guide RNA.
    /// </summary>
    [Description("Target HGNC gene symbol (e.g., CYP19A1, HSD17B1, SRD5A2, HSD17B3, CYP17A1).")]
    [JsonPropertyName("targetGeneSymbol")]
    public string TargetGeneSymbol { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes whether this locus recruits transcriptional activation or silencing domains.
    /// </summary>
    [Description("Modulation direction (Activate or Repress).")]
    [JsonPropertyName("modulationMode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GuideRnaModulationMode ModulationMode { get; init; } = GuideRnaModulationMode.Activate;

    /// <summary>
    /// Gets or initializes the targeting DNA protospacer sequence (18 to 24 bases, A/C/G/T).
    /// </summary>
    [Description("DNA protospacer sequence (18 to 24 bases of A, C, G, T).")]
    [JsonPropertyName("protospacerSequence")]
    public string ProtospacerSequence { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the protospacer adjacent motif (PAM) sequence (2 to 6 characters).
    /// </summary>
    [Description("PAM sequence (2 to 6 characters, e.g., AGG or TGG).")]
    [JsonPropertyName("pamSequence")]
    public string PamSequence { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the genome-wide CFD specificity score percentage (0 to 100%).
    /// </summary>
    [Description("Genome-wide CFD off-target specificity percentage (0 to 100%).")]
    [JsonPropertyName("cfdSpecificityScore")]
    public double CfdSpecificityScore { get; init; }

    /// <summary>
    /// Gets or initializes the predicted transcriptional expression fold change (&gt;1 activate, &lt;1 repress).
    /// </summary>
    [Description("Predicted transcriptional fold-change multiplier (0.01 to 50.0).")]
    [JsonPropertyName("predictedFoldChange")]
    public double PredictedFoldChange { get; init; }
}

/// <summary>
/// Request payload for designing and evaluating a patient-specific epigenetic steroidogenic toggle circuit.
/// </summary>
[SwaggerSchema(Title = "DesignEpigeneticCircuitRequest", Description = "Request payload creating and evaluating an epigenetic steroidogenic toggle circuit blueprint.")]
public sealed record DesignEpigeneticCircuitRequest : IExamplesProvider<DesignEpigeneticCircuitRequest>
{
    /// <summary>
    /// Gets or initializes the anonymized patient identifier for whom the circuit is designed.
    /// </summary>
    [Description("Patient identifier owning this epigenetic circuit.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the clinical endocrine objective (<c>MaleToFemale</c>, <c>FemaleToMale</c>, or <c>Custom</c>).
    /// </summary>
    [Description("Therapeutic endocrine regimen objective.")]
    [JsonPropertyName("regimen")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TherapeuticRegimen Regimen { get; init; } = TherapeuticRegimen.MaleToFemale;

    /// <summary>
    /// Gets or initializes the anatomical target tissue receiving the nanocarrier payload.
    /// </summary>
    [Description("Target steroidogenic or peripheral anatomical tissue.")]
    [JsonPropertyName("targetTissue")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TargetTissueType TargetTissue { get; init; } = TargetTissueType.GonadalTarget;

    /// <summary>
    /// Gets or initializes the non-nuclease dCas9 epigenetic effector platform.
    /// </summary>
    [Description("Epigenetic dCas9 effector platform.")]
    [JsonPropertyName("effectorPlatform")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EpigeneticEffectorPlatform EffectorPlatform { get; init; } = EpigeneticEffectorPlatform.CrisprOffOn;

    /// <summary>
    /// Gets or initializes optional custom guide RNAs; if omitted or empty, validated regimen defaults are synthesized.
    /// </summary>
    [Description("Optional custom single-guide RNA specifications; defaults to regimen-specific canonical guides when omitted.")]
    [JsonPropertyName("guides")]
    public IReadOnlyList<GuideRnaSpecInput>? Guides { get; init; }

    /// <summary>
    /// Gets or initializes optional clinician design notes encrypted at rest via AES-256-GCM.
    /// </summary>
    [Description("Optional clinician design notes encrypted via AES-256-GCM before storage.")]
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="DesignEpigeneticCircuitRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated epigenetic circuit design request example.</returns>
    public DesignEpigeneticCircuitRequest GetExamples() => new()
    {
        PatientId = "pat_001",
        Regimen = TherapeuticRegimen.MaleToFemale,
        TargetTissue = TargetTissueType.GonadalTarget,
        EffectorPlatform = EpigeneticEffectorPlatform.CrisprOffOn,
        Guides = null,
        Notes = "CYP19A1 + HSD17B1 activation coupled with SRD5A2 + HSD17B3 silencing."
    };
}

/// <summary>
/// Request payload configuring dynamic phase-plane evaluation under patient-specific clearance and chromatin stability factors.
/// </summary>
[SwaggerSchema(Title = "EvaluateEpigeneticCircuitRequest", Description = "Parameters controlling phase-plane bistability and steady-state evaluation for an existing circuit.")]
public sealed record EvaluateEpigeneticCircuitRequest : IExamplesProvider<EvaluateEpigeneticCircuitRequest>
{
    /// <summary>
    /// Gets or initializes the patient metabolic hormone clearance multiplier (0.25 to 4.0; default 1.0).
    /// </summary>
    [Description("Patient hepatic/renal steroid clearance multiplier (0.25 to 4.0).")]
    [JsonPropertyName("clearanceMultiplier")]
    public double ClearanceMultiplier { get; init; } = 1.0;

    /// <summary>
    /// Gets or initializes the epigenetic histone/CpG heritability stability scaling factor (0.2 to 2.0; default 1.0).
    /// </summary>
    [Description("Epigenetic chromatin persistence stability factor (0.2 to 2.0).")]
    [JsonPropertyName("epigeneticStabilityFactor")]
    public double EpigeneticStabilityFactor { get; init; } = 1.0;

    /// <summary>
    /// Generates a representative example instance of <see cref="EvaluateEpigeneticCircuitRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated circuit evaluation request example.</returns>
    public EvaluateEpigeneticCircuitRequest GetExamples() => new()
    {
        ClearanceMultiplier = 1.0,
        EpigeneticStabilityFactor = 1.0
    };
}

/// <summary>
/// Request payload attempting fail-closed NIST FIPS 204 ML-DSA-65 permanent synthesis sign-off for a circuit.
/// </summary>
[SwaggerSchema(Title = "ApproveEpigeneticCircuitRequest", Description = "Request payload triggering fail-closed ML-DSA-65 synthesis approval for a safe bistable circuit.")]
public sealed record ApproveEpigeneticCircuitRequest : IExamplesProvider<ApproveEpigeneticCircuitRequest>
{
    /// <summary>
    /// Gets or initializes optional reviewer authorization remarks recorded in the 21 CFR Part 11 audit ledger.
    /// </summary>
    [Description("Optional reviewer sign-off comment written to the audit ledger.")]
    [JsonPropertyName("reviewerNotes")]
    public string? ReviewerNotes { get; init; }

    /// <summary>
    /// Gets or initializes the patient metabolic hormone clearance multiplier checked during live re-evaluation (0.25 to 4.0).
    /// </summary>
    [Description("Patient steroid clearance multiplier verified during pre-approval re-evaluation.")]
    [JsonPropertyName("clearanceMultiplier")]
    public double ClearanceMultiplier { get; init; } = 1.0;

    /// <summary>
    /// Gets or initializes the chromatin stability factor checked during live re-evaluation (0.2 to 2.0).
    /// </summary>
    [Description("Epigenetic persistence stability factor verified during pre-approval re-evaluation.")]
    [JsonPropertyName("epigeneticStabilityFactor")]
    public double EpigeneticStabilityFactor { get; init; } = 1.0;

    /// <summary>
    /// Generates a representative example instance of <see cref="ApproveEpigeneticCircuitRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated circuit approval request example.</returns>
    public ApproveEpigeneticCircuitRequest GetExamples() => new()
    {
        ReviewerNotes = "CFD specificity and bistable separatrix margin verified for clinical synthesis.",
        ClearanceMultiplier = 1.0,
        EpigeneticStabilityFactor = 1.0
    };
}
