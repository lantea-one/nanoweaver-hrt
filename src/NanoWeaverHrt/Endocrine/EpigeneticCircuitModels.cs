using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Endocrine;

/// <summary>
/// Clinical safety, genome-wide off-target specificity, and bistable dynamical-systems thresholds
/// governing fail-closed approval of permanent epigenetic steroidogenic circuits.
/// </summary>
public static class EpigeneticSafetyThresholds
{
    /// <summary>
    /// Minimum genome-wide Cutting Frequency Determination (CFD) specificity score (%) required for every guide RNA.
    /// </summary>
    public const double MinimumCfdSpecificityScore = 98.0;

    /// <summary>
    /// Minimum normalized Euclidean phase-plane separation between target attractor and saddle separatrix.
    /// </summary>
    public const double MinimumSeparatrixMargin = 0.25;

    /// <summary>
    /// Minimum predicted steady-state time-in-therapeutic-range percentage (%) required for blueprint approval.
    /// </summary>
    public const double MinimumPredictedTtrPercent = 85.0;

    /// <summary>
    /// Minimum cisgender female therapeutic 17β-estradiol serum concentration (pmol/L) for MtF regimens.
    /// </summary>
    public const double MtFMinEstradiolPmolL = 350.0;

    /// <summary>
    /// Maximum cisgender female therapeutic 17β-estradiol serum concentration (pmol/L) for MtF regimens.
    /// </summary>
    public const double MtFMaxEstradiolPmolL = 750.0;

    /// <summary>
    /// Maximum suppressed serum testosterone concentration (nmol/L) required for MtF regimens.
    /// </summary>
    public const double MtFMaxTestosteroneNmolL = 1.8;

    /// <summary>
    /// Minimum cisgender male therapeutic total testosterone serum concentration (nmol/L) for FtM regimens.
    /// </summary>
    public const double FtMMinTestosteroneNmolL = 12.0;

    /// <summary>
    /// Maximum cisgender male therapeutic total testosterone serum concentration (nmol/L) for FtM regimens.
    /// </summary>
    public const double FtMMaxTestosteroneNmolL = 28.0;

    /// <summary>
    /// Maximum suppressed serum 17β-estradiol concentration (pmol/L) required for FtM regimens.
    /// </summary>
    public const double FtMMaxEstradiolPmolL = 150.0;

    /// <summary>
    /// Hill cooperativity exponent governing mutual chromatin-loop transcriptional repression.
    /// </summary>
    public const double HillCooperativityN = 4.0;

    /// <summary>
    /// Half-saturation repression constant (<c>K</c>) on the normalized steroidogenic axis.
    /// </summary>
    public const double HillHalfSaturationK = 0.5;

    /// <summary>
    /// Maximum permitted character count for clinician design or reviewer notes.
    /// </summary>
    public const int MaxNotesLength = 2000;

    /// <summary>
    /// Maximum number of multiplexed single-guide RNAs permitted in a single epigenetic circuit blueprint.
    /// </summary>
    public const int MaxGuidesPerCircuit = 16;
}

/// <summary>
/// Two-dimensional coordinate in the mutual-repression epigenetic phase plane plotting normalized
/// estrogenic (<c>u</c>) versus androgenic (<c>v</c>) chromatin locus activity.
/// </summary>
[SwaggerSchema(Title = "PhasePlanePoint", Description = "Point (u, v) in the bistable steroidogenic chromatin phase plane.")]
public sealed record PhasePlanePoint
{
    /// <summary>
    /// Gets or initializes normalized estrogenic steroidogenic module activity (<c>u</c>).
    /// </summary>
    [Description("Normalized estrogenic steroidogenic chromatin activity (u).")]
    [JsonPropertyName("estrogenAxisNormalized")]
    public double EstrogenAxisNormalized { get; init; }

    /// <summary>
    /// Gets or initializes normalized androgenic steroidogenic module activity (<c>v</c>).
    /// </summary>
    [Description("Normalized androgenic steroidogenic chromatin activity (v).")]
    [JsonPropertyName("androgenAxisNormalized")]
    public double AndrogenAxisNormalized { get; init; }
}

/// <summary>
/// Computed Hill-toggle nullclines, separatrix boundary, and fixed points defining whether an
/// epigenetic circuit creates two stable permanent hormone attractors.
/// </summary>
[SwaggerSchema(Title = "EpigeneticNullclineDiagram", Description = "Nullclines, separatrix boundary, attractors, and saddle point for the epigenetic toggle switch.")]
public sealed record EpigeneticNullclineDiagram
{
    /// <summary>
    /// Gets or initializes sampled points along the estrogenic steady-state nullcline (<c>du/dt = 0</c>).
    /// </summary>
    [Description("Estrogenic chromatin nullcline curve (du/dt = 0).")]
    [JsonPropertyName("estrogenNullcline")]
    public IReadOnlyList<PhasePlanePoint> EstrogenNullcline { get; init; } = [];

    /// <summary>
    /// Gets or initializes sampled points along the androgenic steady-state nullcline (<c>dv/dt = 0</c>).
    /// </summary>
    [Description("Androgenic chromatin nullcline curve (dv/dt = 0).")]
    [JsonPropertyName("androgenNullcline")]
    public IReadOnlyList<PhasePlanePoint> AndrogenNullcline { get; init; } = [];

    /// <summary>
    /// Gets or initializes sampled points along the linearized saddle separatrix dividing the two attractor basins.
    /// </summary>
    [Description("Separatrix boundary passing through the unstable saddle point.")]
    [JsonPropertyName("separatrixCurve")]
    public IReadOnlyList<PhasePlanePoint> SeparatrixCurve { get; init; } = [];

    /// <summary>
    /// Gets or initializes the steady-state attractor matching the prescribed clinical HRT regimen.
    /// </summary>
    [Description("Target clinical hormonal steady-state attractor.")]
    [JsonPropertyName("targetAttractor")]
    public PhasePlanePoint TargetAttractor { get; init; } = new();

    /// <summary>
    /// Gets or initializes the opposing chromatin attractor (or identical point when monostable).
    /// </summary>
    [Description("Alternate endogenous steady-state attractor.")]
    [JsonPropertyName("alternateAttractor")]
    public PhasePlanePoint AlternateAttractor { get; init; } = new();

    /// <summary>
    /// Gets or initializes the unstable saddle fixed point separating both basins (or unique root when monostable).
    /// </summary>
    [Description("Unstable saddle fixed point along the separatrix.")]
    [JsonPropertyName("saddlePoint")]
    public PhasePlanePoint SaddlePoint { get; init; } = new();

    /// <summary>
    /// Gets or initializes a value indicating whether three fixed points exist (true bistable switch).
    /// </summary>
    [Description("True when 3 distinct fixed points form a genuine bistable epigenetic switch.")]
    [JsonPropertyName("isBistable")]
    public bool IsBistable { get; init; }

    /// <summary>
    /// Gets or initializes the Euclidean distance separating the target attractor from the unstable saddle point.
    /// </summary>
    [Description("Normalized separatrix margin between target attractor and saddle point.")]
    [JsonPropertyName("separatrixMargin")]
    public double SeparatrixMargin { get; init; }
}

/// <summary>
/// Comprehensive biophysical, off-target safety, phase-plane, and cryptographic ML-DSA-65 evaluation report
/// for a patient-specific epigenetic steroidogenic toggle circuit.
/// </summary>
[SwaggerSchema(Title = "EpigeneticCircuitEvaluationReport", Description = "Complete off-target, bistable phase-plane, endocrine setpoint, and ML-DSA-65 approval report.")]
public sealed record EpigeneticCircuitEvaluationReport : IExamplesProvider<EpigeneticCircuitEvaluationReport>
{
    /// <summary>
    /// Gets or initializes the unique circuit blueprint identifier.
    /// </summary>
    [Description("Unique identifier of the epigenetic circuit blueprint.")]
    [JsonPropertyName("circuitId")]
    public string CircuitId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the owning patient identifier.
    /// </summary>
    [Description("Patient identifier owning this blueprint.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the target therapeutic endocrine regimen.
    /// </summary>
    [Description("Prescribed endocrine therapeutic regimen.")]
    [JsonPropertyName("regimen")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TherapeuticRegimen Regimen { get; init; } = TherapeuticRegimen.MaleToFemale;

    /// <summary>
    /// Gets or initializes the targeted anatomical tissue for delivery.
    /// </summary>
    [Description("Anatomical delivery tissue target.")]
    [JsonPropertyName("targetTissue")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TargetTissueType TargetTissue { get; init; } = TargetTissueType.GonadalTarget;

    /// <summary>
    /// Gets or initializes the dCas9 epigenetic effector platform.
    /// </summary>
    [Description("Epigenetic dCas9 chromatin-modifying platform.")]
    [JsonPropertyName("effectorPlatform")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EpigeneticEffectorPlatform EffectorPlatform { get; init; } = EpigeneticEffectorPlatform.CrisprOffOn;

    /// <summary>
    /// Gets or initializes the current lifecycle and safety status of this circuit.
    /// </summary>
    [Description("Current circuit status (Draft, Evaluated, Blocked, Approved).")]
    [JsonPropertyName("circuitStatus")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EpigeneticCircuitStatus CircuitStatus { get; init; } = EpigeneticCircuitStatus.Evaluated;

    /// <summary>
    /// Gets or initializes the predicted steady-state serum 17β-estradiol concentration (pmol/L).
    /// </summary>
    [Description("Predicted steady-state serum 17β-estradiol concentration in pmol/L.")]
    [JsonPropertyName("predictedEstradiolPmolL")]
    public double PredictedEstradiolPmolL { get; init; }

    /// <summary>
    /// Gets or initializes the predicted steady-state serum total testosterone concentration (nmol/L).
    /// </summary>
    [Description("Predicted steady-state serum total testosterone concentration in nmol/L.")]
    [JsonPropertyName("predictedTestosteroneNmolL")]
    public double PredictedTestosteroneNmolL { get; init; }

    /// <summary>
    /// Gets or initializes the predicted percentage of time serum hormones remain in the therapeutic range.
    /// </summary>
    [Description("Predicted therapeutic time-in-range percentage (0 to 100%).")]
    [JsonPropertyName("predictedTtrPercent")]
    public double PredictedTtrPercent { get; init; }

    /// <summary>
    /// Gets or initializes the minimum genome-wide CFD off-target specificity percentage across all assigned sgRNAs.
    /// </summary>
    [Description("Minimum genome-wide CFD specificity score across assigned guide RNAs.")]
    [JsonPropertyName("minOffTargetSpecificityScore")]
    public double MinOffTargetSpecificityScore { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether every guide RNA meets the 98.0% CFD specificity floor.
    /// </summary>
    [Description("True when all guides satisfy CFD >= 98.0%.")]
    [JsonPropertyName("passesOffTargetGate")]
    public bool PassesOffTargetGate { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether the Hill circuit exhibits three distinct fixed points.
    /// </summary>
    [Description("True when the Hill toggle differential system is strictly bistable.")]
    [JsonPropertyName("isBistable")]
    public bool IsBistable { get; init; }

    /// <summary>
    /// Gets or initializes the normalized separatrix margin separating the target attractor from the saddle boundary.
    /// </summary>
    [Description("Normalized Euclidean separatrix stability margin.")]
    [JsonPropertyName("separatrixMargin")]
    public double SeparatrixMargin { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether bistability and separatrix margin satisfy the >= 0.25 threshold.
    /// </summary>
    [Description("True when the circuit is bistable with separatrix margin >= 0.25.")]
    [JsonPropertyName("passesBistabilityGate")]
    public bool PassesBistabilityGate { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether steady-state E2/T and TTR satisfy clinical targets.
    /// </summary>
    [Description("True when predicted hormones and TTR satisfy target regimen bounds.")]
    [JsonPropertyName("passesEndocrineSetpointGate")]
    public bool PassesEndocrineSetpointGate { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether all safety gates passed and permanent synthesis can be approved.
    /// </summary>
    [Description("True only when off-target, bistability, and endocrine setpoint gates all pass.")]
    [JsonPropertyName("isSafeForPermanentSynthesis")]
    public bool IsSafeForPermanentSynthesis { get; init; }

    /// <summary>
    /// Gets or initializes human-readable diagnostic reasons explaining why any safety gate failed.
    /// </summary>
    [Description("Diagnostic failure messages when one or more safety gates fail.")]
    [JsonPropertyName("failureReasons")]
    public IReadOnlyList<string> FailureReasons { get; init; } = [];

    /// <summary>
    /// Gets or initializes the ordered list of target single-guide RNAs composing this circuit.
    /// </summary>
    [Description("Ordered list of single-guide RNA loci included in this circuit.")]
    [JsonPropertyName("guides")]
    public IReadOnlyList<EpigeneticGuideRnaRecord> Guides { get; init; } = [];

    /// <summary>
    /// Gets or initializes the complete phase-plane nullcline and separatrix diagram.
    /// </summary>
    [Description("Phase-plane nullcline and separatrix geometry for visualization.")]
    [JsonPropertyName("phasePlane")]
    public EpigeneticNullclineDiagram PhasePlane { get; init; } = new();

    /// <summary>
    /// Gets or initializes optional clinician design notes decrypted for authorized viewers.
    /// </summary>
    [Description("Optional decrypted clinician notes.")]
    [JsonPropertyName("decryptedNotes")]
    public string? DecryptedNotes { get; init; }

    /// <summary>
    /// Gets or initializes the canonical lowercase SHA-256 hex digest signed on permanent synthesis approval.
    /// </summary>
    [Description("Canonical SHA-256 hex digest of the approved circuit payload.")]
    [JsonPropertyName("approvalPayloadSha256")]
    public string? ApprovalPayloadSha256 { get; init; }

    /// <summary>
    /// Gets or initializes the Base64-encoded NIST FIPS 204 ML-DSA-65 approval signature when approved.
    /// </summary>
    [Description("Base64 ML-DSA-65 digital signature authorizing permanent synthesis.")]
    [JsonPropertyName("approvalSignatureBase64")]
    public string? ApprovalSignatureBase64 { get; init; }

    /// <summary>
    /// Gets or initializes the key identifier of the node ML-DSA-65 signing key.
    /// </summary>
    [Description("Identifier of the node ML-DSA-65 signing key.")]
    [JsonPropertyName("approvalSignerKeyId")]
    public string? ApprovalSignerKeyId { get; init; }

    /// <summary>
    /// Gets or initializes the Base64-encoded ML-DSA-65 public key for offline verification.
    /// </summary>
    [Description("Base64-encoded ML-DSA-65 public key corresponding to ApprovalSignerKeyId.")]
    [JsonPropertyName("approvalSignerPublicKeyBase64")]
    public string? ApprovalSignerPublicKeyBase64 { get; init; }

    /// <summary>
    /// Gets or initializes the cryptographic signature algorithm (<c>ML-DSA-65</c>) when approved.
    /// </summary>
    [Description("Post-quantum cryptographic signature algorithm identifier (ML-DSA-65).")]
    [JsonPropertyName("signatureAlgorithm")]
    public string? SignatureAlgorithm { get; init; }

    /// <summary>
    /// Gets or initializes the UTC timestamp when this circuit was designed.
    /// </summary>
    [Description("UTC timestamp when the circuit design was created.")]
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>
    /// Gets or initializes the UTC timestamp when ML-DSA-65 permanent synthesis approval was granted, or <see langword="null"/>.
    /// </summary>
    [Description("UTC timestamp when permanent synthesis approval was granted, or null.")]
    [JsonPropertyName("approvedAtUtc")]
    public DateTimeOffset? ApprovedAtUtc { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="EpigeneticCircuitEvaluationReport"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated epigenetic circuit evaluation report example.</returns>
    public EpigeneticCircuitEvaluationReport GetExamples() => new()
    {
        CircuitId = "epicirc_20261123_001",
        PatientId = "pat_001",
        Regimen = TherapeuticRegimen.MaleToFemale,
        TargetTissue = TargetTissueType.GonadalTarget,
        EffectorPlatform = EpigeneticEffectorPlatform.CrisprOffOn,
        CircuitStatus = EpigeneticCircuitStatus.Evaluated,
        PredictedEstradiolPmolL = 562.4,
        PredictedTestosteroneNmolL = 0.91,
        PredictedTtrPercent = 96.2,
        MinOffTargetSpecificityScore = 98.8,
        PassesOffTargetGate = true,
        IsBistable = true,
        SeparatrixMargin = 0.551,
        PassesBistabilityGate = true,
        PassesEndocrineSetpointGate = true,
        IsSafeForPermanentSynthesis = true,
        FailureReasons = [],
        Guides = [new EpigeneticGuideRnaRecord().GetExamples()],
        PhasePlane = new EpigeneticNullclineDiagram
        {
            TargetAttractor = new PhasePlanePoint { EstrogenAxisNormalized = 1.14, AndrogenAxisNormalized = 0.034 },
            AlternateAttractor = new PhasePlanePoint { EstrogenAxisNormalized = 0.082, AndrogenAxisNormalized = 0.949 },
            SaddlePoint = new PhasePlanePoint { EstrogenAxisNormalized = 0.461, AndrogenAxisNormalized = 0.551 },
            IsBistable = true,
            SeparatrixMargin = 0.551
        },
        CreatedAtUtc = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero)
    };
}
