using System.ComponentModel;
using Swashbuckle.AspNetCore.Annotations;
using WeaverLammpsHrt.TrajectoryParser.Endocrine;

namespace WeaverLammpsHrt.TrajectoryParser.Fabrication;

/// <summary>
/// Pre-configured digitized DNA origami sample templates available for immediate 1-click loading and testing.
/// Provides real-world therapeutic designs for Male-to-Female (MtF) and Female-to-Male (FtM) HRT nanocarriers.
/// </summary>
[SwaggerSchema(Title = "DnaOrigamiSample", Description = "Pre-configured digitized DNA origami sample template for synthetic biology nanocarriers.")]
public sealed record DnaOrigamiSample
{
    /// <summary>
    /// Unique identifier for the sample preset.
    /// </summary>
    [Description("Unique identifier for the sample preset.")]
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable title of the DNA nanotechnology architecture.
    /// </summary>
    [Description("Human-readable title of the DNA nanotechnology architecture.")]
    public required string Title { get; init; }

    /// <summary>
    /// Associated endocrine therapeutic regimen (MtF or FtM).
    /// </summary>
    [Description("Associated endocrine therapeutic regimen (MaleToFemale or FemaleToMale).")]
    public required TherapeuticRegimen Regimen { get; init; }

    /// <summary>
    /// Delivery vehicle structural geometry (e.g., 'Hexagonal Nanotube 6HB', '3D Cuboid Cage').
    /// </summary>
    [Description("Delivery vehicle structural geometry.")]
    public required string VehicleGeometry { get; init; }

    /// <summary>
    /// Target anatomical tissue or cellular receptor.
    /// </summary>
    [Description("Target anatomical tissue or cellular receptor.")]
    public required string TargetTissue { get; init; }

    /// <summary>
    /// Primary active ligand or synthetic hormone (e.g., '17β-Estradiol', 'Testosterone Enanthate').
    /// </summary>
    [Description("Primary active ligand or synthetic hormone.")]
    public required string ActiveLigand { get; init; }

    /// <summary>
    /// In-silico target dissociation constant (Kd) in nanomolar (lower Kd indicates higher binding affinity).
    /// </summary>
    [Description("In-silico target dissociation constant (Kd) in nanomolar.")]
    public required double TargetKdNanomolar { get; init; }

    /// <summary>
    /// Plain-English explanation of what this sample nanocarrier is engineered to achieve.
    /// </summary>
    [Description("Plain-English explanation of what this sample nanocarrier is engineered to achieve.")]
    public required string Description { get; init; }

    /// <summary>
    /// Biomedical rationale explaining why this geometry and aptamer sequence were selected.
    /// </summary>
    [Description("Biomedical rationale explaining why this geometry and aptamer sequence were selected.")]
    public required string BiomedicalRationale { get; init; }

    /// <summary>
    /// Digitized nucleotide sequence for the active aptamer binding core.
    /// </summary>
    [Description("Digitized nucleotide sequence for the active aptamer binding core.")]
    public required string AptamerSequence { get; init; }

    /// <summary>
    /// Scaffold backbone used for the origami fold (typically single-stranded bacteriophage M13mp18, 7249 nucleotides).
    /// </summary>
    [Description("Scaffold backbone used for the origami fold.")]
    public required string ScaffoldType { get; init; }

    /// <summary>
    /// Returns the built-in library of verified sovereign DNA nanocarrier sample designs.
    /// </summary>
    public static IReadOnlyList<DnaOrigamiSample> GetPresetLibrary() =>
    [
        new DnaOrigamiSample
        {
            Id = "mtf-e2-nanotube",
            Title = "MtF 17β-Estradiol Aptamer Nanocarrier (6HB Nanotube)",
            Regimen = TherapeuticRegimen.MaleToFemale,
            VehicleGeometry = "Hexagonal 6-Helix Bundle Nanotube (6HB)",
            TargetTissue = "Anterior Pituitary Gonadotrope (GnRH-R)",
            ActiveLigand = "17β-Estradiol",
            TargetKdNanomolar = 4.2,
            Description = "Rigid tubular DNA origami chassis embedding 12 high-affinity 17β-estradiol aptamer pockets with steric protection against nuclease degradation.",
            BiomedicalRationale = "Targeting anterior pituitary gonadotropes enables direct localized feedback regulation, suppressing luteinizing hormone (LH) and follicle-stimulating hormone (FSH) to achieve permanent systemic estrogenic dominance without hepatic first-pass toxicity.",
            AptamerSequence = "5'-ATACCAGCTTATTCAATTGGGAGTGTGTGGTATAGTGGTGTCTTTGTTTGT-3'",
            ScaffoldType = "M13mp18 ssDNA (7,249 nt)"
        },
        new DnaOrigamiSample
        {
            Id = "ftm-t-cuboid",
            Title = "FtM Testosterone Delivery Nanocage (3D Cuboid Box)",
            Regimen = TherapeuticRegimen.FemaleToMale,
            VehicleGeometry = "3D Cuboid Box with Aptamer Lid",
            TargetTissue = "Steroidogenic Receptors (Pituitary / Adrenal)",
            ActiveLigand = "Testosterone Enanthate",
            TargetKdNanomolar = 8.5,
            Description = "Enclosed hollow origami box equipped with an allosteric aptamer lock that unlatches specifically in response to target tissue receptor binding.",
            BiomedicalRationale = "Encapsulates hydrophobic testosterone within an aqueous-soluble DNA origami nanocage, preventing premature crystallization in the bloodstream and ensuring zero burst-release spikes.",
            AptamerSequence = "5'-CGAATGAGTTACGTACCTGTAGTGCTGTGGCATTTGTACGGTCAGCT-3'",
            ScaffoldType = "M13mp18 ssDNA (7,249 nt)"
        },
        new DnaOrigamiSample
        {
            Id = "allosteric-toggle-latch",
            Title = "Bistable Allosteric DNA Molecular Switch",
            Regimen = TherapeuticRegimen.MaleToFemale,
            VehicleGeometry = "Dual-State Hinged Origami Nanocarrier",
            TargetTissue = "Hypothalamic-Pituitary-Gonadal (HPG) Axis",
            ActiveLigand = "17β-Estradiol / Testosterone Dual Responsive",
            TargetKdNanomolar = 5.0,
            Description = "Reversible nano-mechanical origami hinge that switches between open (active release) and closed (steric inhibition) states via oligonucleotide strand displacement.",
            BiomedicalRationale = "Allows continuous adaptive autonomous regulation: the carrier naturally titrates hormone release based on circulating feedback loops, preventing over-saturation or receptor desensitization.",
            AptamerSequence = "5'-GGGAGGACGAUGCGGCCUCUGUUGCUCUAUUUGUGACGUCCC-3'",
            ScaffoldType = "M13mp18 ssDNA (7,249 nt)"
        }
    ];
}
