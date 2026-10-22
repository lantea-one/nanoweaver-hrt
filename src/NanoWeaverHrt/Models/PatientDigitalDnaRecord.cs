using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Persistent digitized DNA nanocarrier blueprint record associated with a patient, capturing targeted geometry,
/// sequence composition, synthetic staple routing, and synthesis ramp parameters.
/// </summary>
public sealed record PatientDigitalDnaRecord : IExamplesProvider<PatientDigitalDnaRecord>
{
    /// <summary>
    /// Gets or initializes the unique identifier for this digitized DNA record.
    /// </summary>
    [Description("Unique identifier for this digitized DNA record.")]
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the username of the patient associated with this DNA blueprint.
    /// </summary>
    [Description("Username of the patient associated with this DNA blueprint.")]
    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the clinical or laboratory title for this DNA package design.
    /// </summary>
    [Description("Clinical or laboratory design title.")]
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the targeted therapeutic endocrine regimen.
    /// </summary>
    [Description("Target therapeutic endocrine regimen (MaleToFemale or FemaleToMale).")]
    [JsonPropertyName("regimen")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TherapeuticRegimen Regimen { get; init; } = TherapeuticRegimen.MaleToFemale;

    /// <summary>
    /// Gets or initializes the target anatomical tissue for nanocarrier tropism.
    /// </summary>
    [Description("Target tissue for aptamer delivery (e.g. Anterior Pituitary Gonadotrope).")]
    [JsonPropertyName("targetTissue")]
    public string TargetTissue { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the active therapeutic steroid ligand payload.
    /// </summary>
    [Description("Target steroid hormone molecule (17β-Estradiol or Testosterone).")]
    [JsonPropertyName("activeLigand")]
    public string ActiveLigand { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the 3D folded nanocarrier geometry architecture.
    /// </summary>
    [Description("Target 3D DNA origami nanostructure architecture (e.g. Hexagonal Nanotube (6HB)).")]
    [JsonPropertyName("nanocarrierGeometry")]
    public string NanocarrierGeometry { get; init; } = "Hexagonal Nanotube (6HB)";

    /// <summary>
    /// Gets or initializes the target aptamer receptor dissociation constant in nanomolar.
    /// </summary>
    [Description("Aptamer binding pocket target Kd in nanomolar (nM).")]
    [JsonPropertyName("targetAptamerKdNm")]
    public double TargetAptamerKdNm { get; init; } = 3.5;

    /// <summary>
    /// Gets or initializes the single-stranded scaffold sequence in FASTA format.
    /// </summary>
    [Description("Bacteriophage single-stranded scaffold sequence in FASTA format.")]
    [JsonPropertyName("scaffoldFasta")]
    public string ScaffoldFasta { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the GC content base percentage.
    /// </summary>
    [Description("Guanine-Cytosine base composition percentage.")]
    [JsonPropertyName("gcPercentage")]
    public double GcPercentage { get; init; } = 42.5;

    /// <summary>
    /// Gets or initializes the estimated folded molecular weight in kilodaltons.
    /// </summary>
    [Description("Estimated molecular weight in kilodaltons (kDa).")]
    [JsonPropertyName("estimatedMwKDa")]
    public double EstimatedMwKDa { get; init; } = 2400.0;

    /// <summary>
    /// Gets or initializes the ensemble of synthetic staple oligonucleotides.
    /// </summary>
    [Description("Synthetic staple oligonucleotides with sequences, lengths, and melting temperatures.")]
    [JsonPropertyName("staples")]
    public IReadOnlyList<StapleOligoRecord> Staples { get; init; } = [];

    /// <summary>
    /// Gets or initializes the stoichiometric thermal annealing ramp schedule.
    /// </summary>
    [Description("Multi-stage thermal annealing temperature cooling protocol.")]
    [JsonPropertyName("annealingProtocol")]
    public IReadOnlyList<AnnealingRampStep> AnnealingProtocol { get; init; } = [];

    /// <summary>
    /// Gets or initializes the SHA-256 cryptographic provenance manifest checksum.
    /// </summary>
    [Description("Cryptographic SHA-256 provenance checksum of this DNA record.")]
    [JsonPropertyName("provenanceSha256")]
    public string ProvenanceSha256 { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the UTC creation timestamp.
    /// </summary>
    [Description("UTC creation timestamp.")]
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or initializes optional clinical notes or operator instructions.
    /// </summary>
    [Description("Optional clinical notes or operator instructions.")]
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    /// <summary>
    /// Converts this database record into a transportable <see cref="PatientDigitalDnaPackage"/>.
    /// </summary>
    /// <returns>A populated digital DNA package instance.</returns>
    public PatientDigitalDnaPackage ToPackage()
    {
        // WHAT: Converts database persistent entity into the patient-facing digital DNA export package.
        // WHY:  Standardizes serialization and download formats for client web browsers and laboratory robotics.
        // HOW:  Maps corresponding fields and preserves cryptographic SHA-256 manifest.
        return new PatientDigitalDnaPackage
        {
            PatientId = Username,
            Regimen = Regimen,
            TargetTissue = TargetTissue,
            ActiveLigand = ActiveLigand,
            NanocarrierGeometry = NanocarrierGeometry,
            TargetAptamerKdNm = TargetAptamerKdNm,
            ScaffoldFasta = ScaffoldFasta,
            GcPercentage = GcPercentage,
            EstimatedMwKDa = EstimatedMwKDa,
            Staples = Staples,
            AnnealingProtocol = AnnealingProtocol,
            ProvenanceSha256 = ProvenanceSha256,
            GeneratedAtUtc = CreatedAtUtc
        };
    }

    /// <summary>
    /// Constructs a persistent <see cref="PatientDigitalDnaRecord"/> from a <see cref="PatientDigitalDnaPackage"/>.
    /// </summary>
    /// <param name="package">The source digital DNA package.</param>
    /// <param name="id">Optional unique record identifier; generated if null or empty.</param>
    /// <param name="title">Optional design title.</param>
    /// <param name="notes">Optional clinical notes.</param>
    /// <returns>A new persistent DNA record instance.</returns>
    public static PatientDigitalDnaRecord FromPackage(PatientDigitalDnaPackage package, string? id = null, string? title = null, string? notes = null)
    {
        // WHAT: Factory constructor converting an ephemeral digital DNA package into a patient-associated database entity.
        // WHY:  Persists synthesized origami structures to the patient's medical dossier for long-term tracking.
        // HOW:  Maps package properties, generates a unique ID, and assigns default title if absent.
        ArgumentNullException.ThrowIfNull(package);

        return new PatientDigitalDnaRecord
        {
            Id = !string.IsNullOrWhiteSpace(id) ? id : "dna_" + Guid.NewGuid().ToString("N"),
            Username = package.PatientId,
            Title = !string.IsNullOrWhiteSpace(title) ? title : $"{package.NanocarrierGeometry} - {package.ActiveLigand}",
            Regimen = package.Regimen,
            TargetTissue = package.TargetTissue,
            ActiveLigand = package.ActiveLigand,
            NanocarrierGeometry = package.NanocarrierGeometry,
            TargetAptamerKdNm = package.TargetAptamerKdNm,
            ScaffoldFasta = package.ScaffoldFasta,
            GcPercentage = package.GcPercentage,
            EstimatedMwKDa = package.EstimatedMwKDa,
            Staples = package.Staples,
            AnnealingProtocol = package.AnnealingProtocol,
            ProvenanceSha256 = package.ProvenanceSha256,
            CreatedAtUtc = package.GeneratedAtUtc,
            Notes = notes
        };
    }

    /// <summary>
    /// Generates a representative example instance of <see cref="PatientDigitalDnaRecord"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated patient digitized DNA record.</returns>
    public PatientDigitalDnaRecord GetExamples() => new()
    {
        Id = "dna_example_01",
        Username = "patient_alice",
        Title = "Hexagonal Nanotube (6HB) - 17β-Estradiol Delivery",
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
        CreatedAtUtc = DateTimeOffset.UtcNow,
        Notes = "Baseline sovereign synthesis candidate"
    };
}
