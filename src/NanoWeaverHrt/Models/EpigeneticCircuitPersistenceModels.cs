using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Epigenetic effector architecture used to reprogram endogenous steroidogenic chromatin without
/// introducing double-strand DNA breaks.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EpigeneticEffectorPlatform
{
    /// <summary>
    /// Modular catalytically dead Cas9 fusions coupling DNMT3A/3L targeted CpG methylation (silencing)
    /// and TET1 hydroxylase demethylation (reactivation).
    /// </summary>
    DCas9Dnmt3aTet1 = 0,

    /// <summary>
    /// Hit-and-run CRISPRoff/CRISPRon fusion combining DNMT3A-DNMT3L-KRAB heritable silencing with
    /// TET1-VP64 reactivation domains for mitotically stable epigenetic memory.
    /// </summary>
    CrisprOffOn = 1,

    /// <summary>
    /// Transient dCas9-KRAB / dCas9-VP64 chromatin steric-blocking complex with shorter persistence half-life.
    /// </summary>
    DCas9KrabVp64 = 2
}

/// <summary>
/// Clinical safety and cryptographic release lifecycle status for an epigenetic steroidogenic toggle circuit.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EpigeneticCircuitStatus
{
    /// <summary>
    /// Newly designed circuit awaiting initial biophysical and off-target evaluation.
    /// </summary>
    Draft = 0,

    /// <summary>
    /// Circuit has been analyzed for off-target CFD specificity, bistability, and endocrine setpoints.
    /// </summary>
    Evaluated = 1,

    /// <summary>
    /// Circuit failed fail-closed safety gating (CFD &lt; 98.0%, monostable phase plane, or out-of-range steady state).
    /// </summary>
    Blocked = 2,

    /// <summary>
    /// All gates passed and the permanent synthesis blueprint is signed with the node's NIST FIPS 204 ML-DSA-65 key.
    /// </summary>
    Approved = 3
}

/// <summary>
/// Transcriptional modulation direction assigned to a single-guide RNA in an epigenetic toggle circuit.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GuideRnaModulationMode
{
    /// <summary>
    /// Demethylates and opens promoter chromatin (e.g., dCas9-TET1) to upregulate steroidogenic transcription.
    /// </summary>
    Activate = 0,

    /// <summary>
    /// Deposits heritable H3K9me3 and CpG methylation marks (e.g., dCas9-DNMT3A / CRISPRoff) to silence transcription.
    /// </summary>
    Repress = 1
}

/// <summary>
/// Persistent database record representing a patient-specific epigenetic steroidogenic toggle circuit
/// and its fail-closed ML-DSA-65 synthesis approval metadata.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Durable record stored in <c>weaver_epigenetic_circuits</c> capturing target regimen,
/// anatomical delivery tissue, dCas9 effector architecture, bistable separatrix metrics, and ML-DSA-65 signature fields.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 42 (P220): Persists non-nuclease chromatin reprogramming designs across
/// SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB with full GDPR Article 17 erasure support.
/// </para>
/// <para>
/// <strong>How it works:</strong> Managed by <see cref="NanoWeaverHrt.Storage.IEpigeneticCircuitRepository"/> alongside
/// child guide-RNA loci stored in <c>weaver_epigenetic_guide_rnas</c>.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "EpigeneticCircuitRecord", Description = "Persisted epigenetic steroidogenic reprogramming circuit and synthesis approval record.")]
public sealed record EpigeneticCircuitRecord : IExamplesProvider<EpigeneticCircuitRecord>
{
    /// <summary>
    /// Gets or initializes the unique identifier for this epigenetic circuit design.
    /// </summary>
    [Description("Unique identifier of the epigenetic circuit blueprint.")]
    [JsonPropertyName("circuitId")]
    public string CircuitId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the anonymized patient identifier owning this circuit blueprint.
    /// </summary>
    [Description("Patient identifier associated with this epigenetic circuit.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the endocrine therapeutic objective (<c>MaleToFemale</c>, <c>FemaleToMale</c>, or <c>Custom</c>).
    /// </summary>
    [Description("Endocrine therapeutic regimen objective.")]
    [JsonPropertyName("regimen")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TherapeuticRegimen Regimen { get; init; } = TherapeuticRegimen.MaleToFemale;

    /// <summary>
    /// Gets or initializes the anatomical target tissue receiving the nanocarrier epigenetic payload.
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
    /// Gets or initializes the fail-closed evaluation and ML-DSA-65 approval status.
    /// </summary>
    [Description("Lifecycle and safety approval status (Draft, Evaluated, Blocked, Approved).")]
    [JsonPropertyName("circuitStatus")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EpigeneticCircuitStatus CircuitStatus { get; init; } = EpigeneticCircuitStatus.Draft;

    /// <summary>
    /// Gets or initializes the predicted therapeutic time-in-range percentage (0 to 100%).
    /// </summary>
    [Description("Predicted steady-state time-in-therapeutic-range percentage.")]
    [JsonPropertyName("predictedTtrPercent")]
    public double PredictedTtrPercent { get; init; }

    /// <summary>
    /// Gets or initializes the normalized phase-plane distance between target attractor and bistable separatrix saddle.
    /// </summary>
    [Description("Normalized bistable separatrix stability margin.")]
    [JsonPropertyName("separatrixMargin")]
    public double SeparatrixMargin { get; init; }

    /// <summary>
    /// Gets or initializes the lowest genome-wide CFD off-target specificity score across all guide RNAs (0 to 100%).
    /// </summary>
    [Description("Minimum genome-wide CFD off-target specificity percentage across all guides.")]
    [JsonPropertyName("minOffTargetSpecificityScore")]
    public double MinOffTargetSpecificityScore { get; init; }

    /// <summary>
    /// Gets or initializes optional AES-256-GCM encrypted clinician design notes stored at rest.
    /// </summary>
    [Description("Optional AES-256-GCM encrypted design notes stored at rest.")]
    [JsonPropertyName("notesEncrypted")]
    public string? NotesEncrypted { get; init; }

    /// <summary>
    /// Gets or initializes the microsecond-normalized UTC timestamp when this circuit was created.
    /// </summary>
    [Description("UTC timestamp when this circuit design was created.")]
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>
    /// Gets or initializes the UTC timestamp when ML-DSA-65 synthesis approval was granted, or <see langword="null"/> if not approved.
    /// </summary>
    [Description("UTC timestamp when permanent synthesis approval was signed, or null.")]
    [JsonPropertyName("approvedAtUtc")]
    public DateTimeOffset? ApprovedAtUtc { get; init; }

    /// <summary>
    /// Gets or initializes the canonical lowercase SHA-256 hex digest covered by the ML-DSA-65 approval signature.
    /// </summary>
    [Description("Canonical SHA-256 hex digest of the signed circuit approval payload.")]
    [JsonPropertyName("approvalPayloadSha256")]
    public string? ApprovalPayloadSha256 { get; init; }

    /// <summary>
    /// Gets or initializes the Base64-encoded NIST FIPS 204 ML-DSA-65 approval signature.
    /// </summary>
    [Description("Base64-encoded ML-DSA-65 digital signature authorizing permanent synthesis.")]
    [JsonPropertyName("approvalSignatureBase64")]
    public string? ApprovalSignatureBase64 { get; init; }

    /// <summary>
    /// Gets or initializes the node ML-DSA-65 signing key identifier used during blueprint approval.
    /// </summary>
    [Description("Identifier of the node ML-DSA-65 key that signed the approval.")]
    [JsonPropertyName("approvalSignerKeyId")]
    public string? ApprovalSignerKeyId { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="EpigeneticCircuitRecord"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated epigenetic circuit persistence record example.</returns>
    public EpigeneticCircuitRecord GetExamples() => new()
    {
        CircuitId = "epicirc_20261123_001",
        PatientId = "pat_001",
        Regimen = TherapeuticRegimen.MaleToFemale,
        TargetTissue = TargetTissueType.GonadalTarget,
        EffectorPlatform = EpigeneticEffectorPlatform.CrisprOffOn,
        CircuitStatus = EpigeneticCircuitStatus.Evaluated,
        PredictedTtrPercent = 96.4,
        SeparatrixMargin = 0.48,
        MinOffTargetSpecificityScore = 98.8,
        NotesEncrypted = null,
        CreatedAtUtc = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero),
        ApprovedAtUtc = null
    };
}

/// <summary>
/// Persistent database record capturing an individual single-guide RNA (sgRNA) locus and specificity score
/// belonging to an epigenetic steroidogenic toggle circuit.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Row stored in <c>weaver_epigenetic_guide_rnas</c> pairing a target steroidogenic gene
/// (<c>CYP19A1</c>, <c>HSD17B1</c>, <c>SRD5A2</c>, <c>HSD17B3</c>, <c>CYP17A1</c>) with its 20 nt protospacer and CFD score.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 42 (P220): Stores sequence-level guide specifications so offline verification
/// and fail-closed CFD specificity gating audit every chromatin locus independently.
/// </para>
/// <para>
/// <strong>How it works:</strong> Persisted atomically alongside its parent <see cref="EpigeneticCircuitRecord"/> and removed
/// via cascading foreign key or explicit patient-scoped delete transaction.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "EpigeneticGuideRnaRecord", Description = "Persisted single-guide RNA specification within an epigenetic toggle circuit.")]
public sealed record EpigeneticGuideRnaRecord : IExamplesProvider<EpigeneticGuideRnaRecord>
{
    /// <summary>
    /// Gets or initializes the unique identifier for this guide-RNA locus entry.
    /// </summary>
    [Description("Unique identifier of this guide-RNA entry.")]
    [JsonPropertyName("guideId")]
    public string GuideId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the parent epigenetic circuit identifier.
    /// </summary>
    [Description("Identifier of the parent epigenetic circuit.")]
    [JsonPropertyName("circuitId")]
    public string CircuitId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the owning patient identifier for atomic GDPR erasure indexing.
    /// </summary>
    [Description("Owning patient identifier for GDPR Article 17 erasure cascade indexing.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the HGNC steroidogenic gene symbol targeted by this sgRNA.
    /// </summary>
    [Description("HGNC steroidogenic target gene symbol (e.g., CYP19A1, HSD17B3, SRD5A2).")]
    [JsonPropertyName("targetGeneSymbol")]
    public string TargetGeneSymbol { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes whether this guide RNA recruits an activator or repressor effector.
    /// </summary>
    [Description("Transcriptional modulation direction (Activate or Repress).")]
    [JsonPropertyName("modulationMode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GuideRnaModulationMode ModulationMode { get; init; } = GuideRnaModulationMode.Activate;

    /// <summary>
    /// Gets or initializes the DNA protospacer targeting sequence (18 to 24 bases, uppercase A/C/G/T).
    /// </summary>
    [Description("DNA targeting protospacer sequence (18-24 bases).")]
    [JsonPropertyName("protospacerSequence")]
    public string ProtospacerSequence { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the protospacer adjacent motif sequence (e.g., AGG, TGG, NGG).
    /// </summary>
    [Description("Protospacer adjacent motif (PAM) sequence.")]
    [JsonPropertyName("pamSequence")]
    public string PamSequence { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the Cutting Frequency Determination (CFD) specificity score (0 to 100%).
    /// </summary>
    [Description("Genome-wide CFD specificity score percentage (0 to 100%).")]
    [JsonPropertyName("cfdSpecificityScore")]
    public double CfdSpecificityScore { get; init; }

    /// <summary>
    /// Gets or initializes the predicted steady-state transcriptional fold change (&gt;1 for activation, &lt;1 for repression).
    /// </summary>
    [Description("Predicted transcriptional expression fold-change multiplier.")]
    [JsonPropertyName("predictedFoldChange")]
    public double PredictedFoldChange { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="EpigeneticGuideRnaRecord"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated epigenetic guide-RNA persistence record example.</returns>
    public EpigeneticGuideRnaRecord GetExamples() => new()
    {
        GuideId = "g_20261123_001",
        CircuitId = "epicirc_20261123_001",
        PatientId = "pat_001",
        TargetGeneSymbol = "CYP19A1",
        ModulationMode = GuideRnaModulationMode.Activate,
        ProtospacerSequence = "GCTAGTCAGCTGATCGATCG",
        PamSequence = "AGG",
        CfdSpecificityScore = 99.4,
        PredictedFoldChange = 4.2
    };
}
