using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Encapsulates a complete patient-specific digital DNA record, including targeted nanocarrier geometry,
/// nucleotide sequences, staple routing tables, and thermal annealing synthesis parameters.
/// </summary>
public sealed record PatientDigitalDnaPackage : IExamplesProvider<PatientDigitalDnaPackage>
{
    /// <summary>
    /// Gets the patient or subject identifier.
    /// </summary>
    [Description("Pseudonymized patient or subject identifier.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the therapeutic endocrine regimen (MtF or FtM).
    /// </summary>
    [Description("Target therapeutic endocrine regimen (MaleToFemale or FemaleToMale).")]
    [JsonPropertyName("regimen")]
    public TherapeuticRegimen Regimen { get; init; } = TherapeuticRegimen.MaleToFemale;

    /// <summary>
    /// Gets the target anatomical tissue for nanocarrier tropism.
    /// </summary>
    [Description("Target tissue for aptamer delivery (e.g., Anterior Pituitary Gonadotrope).")]
    [JsonPropertyName("targetTissue")]
    public string TargetTissue { get; init; } = string.Empty;

    /// <summary>
    /// Gets the active therapeutic steroid ligand payload.
    /// </summary>
    [Description("Target steroid hormone molecule (17β-Estradiol or Testosterone).")]
    [JsonPropertyName("activeLigand")]
    public string ActiveLigand { get; init; } = string.Empty;

    /// <summary>
    /// Gets the 3D nanocarrier folded architecture (e.g., Hexagonal Nanotube, Hollow Cuboid, Tetrahedral Cage).
    /// </summary>
    [Description("Target 3D DNA origami nanostructure architecture.")]
    [JsonPropertyName("nanocarrierGeometry")]
    public string NanocarrierGeometry { get; init; } = "Hexagonal Nanotube (6HB)";

    /// <summary>
    /// Gets the targeted aptamer receptor dissociation constant in nanomolar.
    /// </summary>
    [Description("Aptamer binding pocket target Kd in nanomolar (nM).")]
    [JsonPropertyName("targetAptamerKdNm")]
    public double TargetAptamerKdNm { get; init; } = 3.5;

    /// <summary>
    /// Gets the single-stranded viral scaffold sequence in FASTA format.
    /// </summary>
    [Description("Bacteriophage M13mp18 single-stranded scaffold sequence in FASTA format.")]
    [JsonPropertyName("scaffoldFasta")]
    public string ScaffoldFasta { get; init; } = string.Empty;

    /// <summary>
    /// Gets the GC content percentage of the custom design.
    /// </summary>
    [Description("Guanine-Cytosine base composition percentage.")]
    [JsonPropertyName("gcPercentage")]
    public double GcPercentage { get; init; } = 42.5;

    /// <summary>
    /// Gets the estimated molecular weight in kilodaltons.
    /// </summary>
    [Description("Estimated molecular weight in kilodaltons (kDa).")]
    [JsonPropertyName("estimatedMwKDa")]
    public double EstimatedMwKDa { get; init; } = 2400.0;

    /// <summary>
    /// Gets the collection of synthetic staple oligonucleotides.
    /// </summary>
    [Description("Staple oligonucleotides with 5' to 3' sequences, crossover positions, and functions.")]
    [JsonPropertyName("staples")]
    public IReadOnlyList<StapleOligoRecord> Staples { get; init; } = [];

    /// <summary>
    /// Gets the stoichiometric thermal annealing ramp schedule.
    /// </summary>
    [Description("6-stage thermal annealing temperature cooling protocol (90°C to 4°C).")]
    [JsonPropertyName("annealingProtocol")]
    public IReadOnlyList<AnnealingRampStep> AnnealingProtocol { get; init; } = [];

    /// <summary>
    /// Gets the deterministic SHA-256 cryptographic provenance manifest checksum.
    /// </summary>
    [Description("Cryptographic SHA-256 provenance checksum of this digital DNA package.")]
    [JsonPropertyName("provenanceSha256")]
    public string ProvenanceSha256 { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp when this digital DNA package was generated.
    /// </summary>
    [Description("UTC generation timestamp.")]
    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Generates a representative example instance of <see cref="PatientDigitalDnaPackage"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample patient digital DNA package record.</returns>
    public PatientDigitalDnaPackage GetExamples() => new()
    {
        PatientId = "PATIENT-SOVEREIGN-01",
        Regimen = TherapeuticRegimen.MaleToFemale,
        TargetTissue = "Anterior Pituitary Gonadotrope / Estrogen Receptor α/β",
        ActiveLigand = "17β-Estradiol (E2)",
        NanocarrierGeometry = "Hexagonal Nanotube (6HB)",
        TargetAptamerKdNm = 3.5,
        ScaffoldFasta = ">M13mp18_scaffold\nAAGCTC...",
        GcPercentage = 42.5,
        EstimatedMwKDa = 2400.0,
        Staples = [],
        AnnealingProtocol = [],
        ProvenanceSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
        GeneratedAtUtc = DateTimeOffset.UtcNow
    };
}
