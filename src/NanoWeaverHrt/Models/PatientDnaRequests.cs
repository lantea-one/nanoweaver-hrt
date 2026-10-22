using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Request payload for synthesizing or registering a digitized DNA nanocarrier blueprint for a patient.
/// </summary>
public sealed record CreatePatientDnaRequest : IExamplesProvider<CreatePatientDnaRequest>
{
    /// <summary>
    /// Gets or initializes optional clinical title for the blueprint.
    /// </summary>
    [Description("Clinical or laboratory title for the blueprint.")]
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>
    /// Gets or initializes the target therapeutic endocrine regimen.
    /// </summary>
    [Description("Target therapeutic endocrine regimen (MaleToFemale or FemaleToMale).")]
    [JsonPropertyName("regimen")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TherapeuticRegimen? Regimen { get; init; }

    /// <summary>
    /// Gets or initializes the target anatomical tissue for nanocarrier tropism.
    /// </summary>
    [Description("Target tissue for aptamer delivery (e.g. Anterior Pituitary Gonadotrope).")]
    [JsonPropertyName("targetTissue")]
    public string? TargetTissue { get; init; }

    /// <summary>
    /// Gets or initializes the active therapeutic steroid ligand payload.
    /// </summary>
    [Description("Target steroid hormone molecule (17β-Estradiol or Testosterone).")]
    [JsonPropertyName("activeLigand")]
    public string? ActiveLigand { get; init; }

    /// <summary>
    /// Gets or initializes the 3D folded nanocarrier geometry architecture.
    /// </summary>
    [Description("Target 3D DNA origami nanostructure architecture (e.g. Hexagonal Nanotube (6HB)).")]
    [JsonPropertyName("nanocarrierGeometry")]
    public string? NanocarrierGeometry { get; init; }

    /// <summary>
    /// Gets or initializes the target aptamer receptor dissociation constant in nanomolar.
    /// </summary>
    [Description("Aptamer binding pocket target Kd in nanomolar (nM).")]
    [JsonPropertyName("targetAptamerKdNm")]
    public double? TargetAptamerKdNm { get; init; }

    /// <summary>
    /// Gets or initializes optional custom single-stranded scaffold sequence in FASTA format.
    /// </summary>
    [Description("Optional custom scaffold sequence in FASTA format. If omitted, default M13mp18 scaffold is used.")]
    [JsonPropertyName("scaffoldFasta")]
    public string? ScaffoldFasta { get; init; }

    /// <summary>
    /// Gets or initializes optional clinical notes or operator instructions.
    /// </summary>
    [Description("Optional clinical notes or operator instructions.")]
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="CreatePatientDnaRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated DNA blueprint request.</returns>
    public CreatePatientDnaRequest GetExamples() => new()
    {
        Title = "Hexagonal Nanotube (6HB) - 17β-Estradiol Delivery",
        Regimen = TherapeuticRegimen.MaleToFemale,
        TargetTissue = "Anterior Pituitary Gonadotrope / Estrogen Receptor α/β",
        ActiveLigand = "17β-Estradiol (E2)",
        NanocarrierGeometry = "Hexagonal Nanotube (6HB)",
        TargetAptamerKdNm = 3.5,
        ScaffoldFasta = ">M13mp18_scaffold\nAAGCTC...",
        Notes = "Baseline synthesis design"
    };
}

/// <summary>
/// Request payload for importing DNA sequence, genomic variant, or dataset archive data associated with a patient.
/// </summary>
public sealed record CreatePatientImportRequest : IExamplesProvider<CreatePatientImportRequest>
{
    /// <summary>
    /// Gets or initializes the original file name of the imported asset.
    /// </summary>
    [Description("Original file name of the imported asset.")]
    [JsonPropertyName("fileName")]
    public required string FileName { get; init; }

    /// <summary>
    /// Gets or initializes the categorization type of the import (e.g. FASTA, GenomicPanel, TrajectoryArchive, AptamerData).
    /// </summary>
    [Description("Categorization type of the import (e.g. FASTA, GenomicPanel, TrajectoryArchive, AptamerData).")]
    [JsonPropertyName("importType")]
    public string ImportType { get; init; } = "FASTA";

    /// <summary>
    /// Gets or initializes the raw sequence text, JSON variant manifest, or metadata payload.
    /// </summary>
    [Description("Raw sequence text, JSON variant manifest, or metadata payload.")]
    [JsonPropertyName("sequenceOrData")]
    public required string SequenceOrData { get; init; }

    /// <summary>
    /// Gets or initializes optional processing status (defaults to 'Processed').
    /// </summary>
    [Description("Processing status (e.g. Processed, Verified, Pending).")]
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>
    /// Gets or initializes optional serialized JSON metadata.
    /// </summary>
    [Description("Optional serialized JSON metadata.")]
    [JsonPropertyName("metadataJson")]
    public string? MetadataJson { get; init; }

    /// <summary>
    /// Gets or initializes a flag indicating whether to run automated pharmacogenomic / sequence analysis on the import.
    /// </summary>
    [Description("Whether to automatically execute sequence analysis and generate patient results upon import.")]
    [JsonPropertyName("autoAnalyze")]
    public bool AutoAnalyze { get; init; } = true;

    /// <summary>
    /// Generates a representative example instance of <see cref="CreatePatientImportRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated import request.</returns>
    public CreatePatientImportRequest GetExamples() => new()
    {
        FileName = "patient_scaffold_variant.fasta",
        ImportType = "FASTA",
        SequenceOrData = ">VariantScaffold\nAAGCTCAGAG...",
        Status = "Processed",
        MetadataJson = "{\"origin\":\"SequencingCore\"}",
        AutoAnalyze = true
    };
}

/// <summary>
/// Request payload for creating a patient simulation, endocrine, or pharmacogenomic result record.
/// </summary>
public sealed record CreatePatientResultRequest : IExamplesProvider<CreatePatientResultRequest>
{
    /// <summary>
    /// Gets or initializes an optional foreign reference to an associated <see cref="PatientDigitalDnaRecord"/>.
    /// </summary>
    [Description("Optional reference to an associated digitized DNA blueprint.")]
    [JsonPropertyName("dnaRecordId")]
    public string? DnaRecordId { get; init; }

    /// <summary>
    /// Gets or initializes an optional foreign reference to an associated <see cref="PatientDnaImportRecord"/>.
    /// </summary>
    [Description("Optional reference to an associated DNA import record.")]
    [JsonPropertyName("importId")]
    public string? ImportId { get; init; }

    /// <summary>
    /// Gets or initializes the categorization type of the result (e.g. EndocrineKinetics, Pharmacogenomics, AptamerAffinity, SimulationTrajectory).
    /// </summary>
    [Description("Result category type (e.g. EndocrineKinetics, Pharmacogenomics, AptamerAffinity, SimulationTrajectory).")]
    [JsonPropertyName("resultType")]
    public required string ResultType { get; init; }

    /// <summary>
    /// Gets or initializes the clinical or computational title of this result.
    /// </summary>
    [Description("Clinical or computational title of this result.")]
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>
    /// Gets or initializes the clinical outcome status (e.g. Optimal, Caution, ElevatedRisk, Complete, Verified).
    /// </summary>
    [Description("Outcome status (e.g. Optimal, Caution, ElevatedRisk, Complete, Verified).")]
    [JsonPropertyName("status")]
    public string Status { get; init; } = "Complete";

    /// <summary>
    /// Gets or initializes serialized JSON key-value quantitative biophysical or biochemical metrics.
    /// </summary>
    [Description("Serialized JSON key-value quantitative metrics.")]
    [JsonPropertyName("metricsJson")]
    public string? MetricsJson { get; init; }

    /// <summary>
    /// Gets or initializes a human-readable, plain-English summary of the clinical or biophysical findings.
    /// </summary>
    [Description("Plain-English summary of clinical or biophysical findings.")]
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="CreatePatientResultRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated result creation request.</returns>
    public CreatePatientResultRequest GetExamples() => new()
    {
        DnaRecordId = "dna_example_01",
        ImportId = null,
        ResultType = "EndocrineKinetics",
        Title = "Endocrine Steady-State Target Kinetics",
        Status = "Optimal",
        MetricsJson = "{\"TargetE2PmolL\":\"650.0\",\"ReceptorKdNm\":\"3.50\"}",
        Summary = "Target setpoints calibrated for Anterior Pituitary Gonadotrope: E2 target 650.0 pmol/L with Kd 3.50 nM."
    };
}
