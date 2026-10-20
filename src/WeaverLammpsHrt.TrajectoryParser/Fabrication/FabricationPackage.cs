using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using WeaverLammpsHrt.TrajectoryParser.Endocrine;

namespace WeaverLammpsHrt.TrajectoryParser.Fabrication;

/// <summary>
/// Represents a single synthetic oligonucleotide staple strand designed to fold the long scaffold
/// into the programmed DNA origami nanocarrier geometry.
/// </summary>
[SwaggerSchema(Title = "StapleOligoRecord", Description = "Synthetic oligonucleotide staple strand with sequence, melting temperature, and GC content.")]
public sealed record StapleOligoRecord
{
    /// <summary>
    /// Systematic identifier for the staple oligo (e.g., 'ST-001', 'ST-002').
    /// </summary>
    [Description("Systematic identifier for the staple oligo.")]
    [JsonPropertyName("staple_id")]
    public required string StapleId { get; init; }

    /// <summary>
    /// 5' to 3' nucleotide sequence of the synthetic DNA oligo.
    /// </summary>
    [Description("5' to 3' nucleotide sequence.")]
    [JsonPropertyName("sequence")]
    public required string Sequence { get; init; }

    /// <summary>
    /// Length of the oligonucleotide in nucleotides (bases).
    /// </summary>
    [Description("Length of the oligonucleotide in bases.")]
    [JsonPropertyName("length")]
    public required int Length { get; init; }

    /// <summary>
    /// Theoretical melting temperature (Tm) in degrees Celsius calculated for physiological salt conditions.
    /// </summary>
    [Description("Theoretical melting temperature (Tm) in degrees Celsius.")]
    [JsonPropertyName("melting_temperature_celsius")]
    public required double MeltingTemperatureCelsius { get; init; }

    /// <summary>
    /// Guanine-Cytosine content percentage (% GC).
    /// </summary>
    [Description("Guanine-Cytosine content percentage (% GC).")]
    [JsonPropertyName("gc_percentage")]
    public required double GcPercentage { get; init; }

    /// <summary>
    /// Recommended stock concentration in micromolar (µM) for stoichiometric assembly.
    /// </summary>
    [Description("Recommended stock concentration in micromolar (µM).")]
    [JsonPropertyName("stock_concentration_micromolar")]
    public required double StockConcentrationMicromolar { get; init; }

    /// <summary>
    /// Spatial lattice domain or functional role (e.g., 'Core Helix 1-3 Crossover', 'Aptamer Loop Staple').
    /// </summary>
    [Description("Spatial lattice domain or functional role.")]
    [JsonPropertyName("functional_role")]
    public required string FunctionalRole { get; init; }
}

/// <summary>
/// Defines a single temperature stage in the thermal annealing protocol required to fold DNA origami.
/// </summary>
[SwaggerSchema(Title = "AnnealingRampStep", Description = "Thermal annealing ramp stage with target temperature, hold duration, and ramp rate.")]
public sealed record AnnealingRampStep
{
    /// <summary>
    /// Sequence order index of this thermal step.
    /// </summary>
    [Description("Sequence order index of this thermal step.")]
    [JsonPropertyName("step_index")]
    public required int StepIndex { get; init; }

    /// <summary>
    /// Target temperature in degrees Celsius.
    /// </summary>
    [Description("Target temperature in degrees Celsius.")]
    [JsonPropertyName("target_temperature_celsius")]
    public required double TargetTemperatureCelsius { get; init; }

    /// <summary>
    /// Incubation / hold duration in minutes at this temperature.
    /// </summary>
    [Description("Incubation hold duration in minutes.")]
    [JsonPropertyName("hold_duration_minutes")]
    public required int HoldDurationMinutes { get; init; }

    /// <summary>
    /// Ramp rate in degrees Celsius per minute used to transition to the next step.
    /// </summary>
    [Description("Ramp rate in degrees Celsius per minute.")]
    [JsonPropertyName("ramp_rate_celsius_per_minute")]
    public required double RampRateCelsiusPerMinute { get; init; }

    /// <summary>
    /// Plain-English explanation of what physical molecular behavior is occurring at this thermal stage.
    /// </summary>
    [Description("Plain-English explanation of biophysical folding behavior occurring at this stage.")]
    [JsonPropertyName("biophysical_purpose")]
    public required string BiophysicalPurpose { get; init; }
}

/// <summary>
/// Represents a laboratory reagent or equipment item needed for DNA origami folding,
/// detailing both professional laboratory and DIY / HomeLab alternatives.
/// </summary>
[SwaggerSchema(Title = "FabricationMaterialItem", Description = "Laboratory reagent or equipment item with standard and DIY alternatives.")]
public sealed record FabricationMaterialItem
{
    /// <summary>
    /// Common chemical or material name.
    /// </summary>
    [Description("Common chemical or material name.")]
    [JsonPropertyName("material_name")]
    public required string MaterialName { get; init; }

    /// <summary>
    /// Recommended commercial or research supplier source (e.g., NEB, IDT, Sigma-Aldrich).
    /// </summary>
    [Description("Recommended research supplier source.")]
    [JsonPropertyName("standard_source")]
    public required string StandardSource { get; init; }

    /// <summary>
    /// Accessible DIY, community lab, or bio-hacker alternative.
    /// </summary>
    [Description("Accessible DIY, community lab, or bio-hacker alternative.")]
    [JsonPropertyName("diy_alternative")]
    public required string DiyAlternative { get; init; }

    /// <summary>
    /// Required quantity or concentration.
    /// </summary>
    [Description("Required quantity or concentration.")]
    [JsonPropertyName("quantity")]
    public required string Quantity { get; init; }

    /// <summary>
    /// Functional biophysical purpose in the assembly reaction.
    /// </summary>
    [Description("Functional biophysical purpose in the assembly reaction.")]
    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }
}

/// <summary>
/// Defines a single pipetting component in the stoichiometric folding reaction recipe.
/// </summary>
[SwaggerSchema(Title = "ReactionPipettingComponent", Description = "Single pipetting component in the stoichiometric folding reaction recipe.")]
public sealed record ReactionPipettingComponent
{
    /// <summary>
    /// Recommended sequential pipetting order (1-indexed).
    /// </summary>
    [Description("Recommended sequential pipetting order.")]
    [JsonPropertyName("addition_order")]
    public required int AdditionOrder { get; init; }

    /// <summary>
    /// Name of the reaction component (e.g., Nuclease-Free ddH2O, Scaffold, Staples).
    /// </summary>
    [Description("Name of the reaction component.")]
    [JsonPropertyName("component_name")]
    public required string ComponentName { get; init; }

    /// <summary>
    /// Stock concentration before addition.
    /// </summary>
    [Description("Stock concentration before addition.")]
    [JsonPropertyName("stock_concentration")]
    public required string StockConcentration { get; init; }

    /// <summary>
    /// Volume to pipette for a standard 50 microliter folding reaction.
    /// </summary>
    [Description("Volume to pipette for a standard 50 µL folding reaction.")]
    [JsonPropertyName("volume_microliters")]
    public required double VolumeMicroliters { get; init; }

    /// <summary>
    /// Final concentration in the completed 50 microliter reaction.
    /// </summary>
    [Description("Final concentration in the completed reaction.")]
    [JsonPropertyName("final_concentration")]
    public required string FinalConcentration { get; init; }

    /// <summary>
    /// Essential handling tip or note for this component.
    /// </summary>
    [Description("Essential handling tip or note for this component.")]
    [JsonPropertyName("handling_tip")]
    public required string HandlingTip { get; init; }
}

/// <summary>
/// Actionable step in the DIY / HomeLab benchtop fabrication protocol.
/// </summary>
[SwaggerSchema(Title = "FabricationStepInstruction", Description = "Actionable protocol step in the DIY / HomeLab benchtop fabrication protocol.")]
public sealed record FabricationStepInstruction
{
    /// <summary>
    /// Sequential step number (1 to 7).
    /// </summary>
    [Description("Sequential step number (1 to 7).")]
    [JsonPropertyName("step_number")]
    public required int StepNumber { get; init; }

    /// <summary>
    /// Title of the protocol phase.
    /// </summary>
    [Description("Title of the protocol phase.")]
    [JsonPropertyName("phase_title")]
    public required string PhaseTitle { get; init; }

    /// <summary>
    /// Plain-English, step-by-step practical instructions written for DIYers and beginners.
    /// </summary>
    [Description("Plain-English, step-by-step practical instructions.")]
    [JsonPropertyName("detailed_instructions")]
    public required string DetailedInstructions { get; init; }

    /// <summary>
    /// Practical advice, cautionary notes, or DIY equipment substitutions for homelab environments.
    /// </summary>
    [Description("Practical advice and equipment substitutions for homelabs.")]
    [JsonPropertyName("diy_homelab_note")]
    public required string DiyHomeLabNote { get; init; }
}

/// <summary>
/// Comprehensive DIY / HomeLab fabrication guide included in every synthesized result manifest.
/// </summary>
[SwaggerSchema(Title = "DiyFabricationGuide", Description = "Comprehensive DIY / HomeLab fabrication guide with pipetting recipes, steps, and QC.")]
public sealed record DiyFabricationGuide
{
    /// <summary>
    /// Checklist of reagents and equipment with DIY substitutes.
    /// </summary>
    [Description("Checklist of reagents and equipment with DIY substitutes.")]
    [JsonPropertyName("materials")]
    public required IReadOnlyList<FabricationMaterialItem> Materials { get; init; }

    /// <summary>
    /// Exact pipetting table for a 50 microliter benchtop folding reaction.
    /// </summary>
    [Description("Exact pipetting table for a 50 µL benchtop folding reaction.")]
    [JsonPropertyName("pipetting_table")]
    public required IReadOnlyList<ReactionPipettingComponent> PipettingTable { get; init; }

    /// <summary>
    /// Step-by-step physical bench protocol (from reconstitution to storage).
    /// </summary>
    [Description("Step-by-step physical bench protocol.")]
    [JsonPropertyName("steps")]
    public required IReadOnlyList<FabricationStepInstruction> Steps { get; init; }

    /// <summary>
    /// Quality control procedures accessible to non-institutional researchers (e.g. Agarose Gel Electrophoresis with Mg2+).
    /// </summary>
    [Description("Quality control procedures (e.g. Agarose Gel Electrophoresis).")]
    [JsonPropertyName("quality_control_guide")]
    public required string QualityControlGuide { get; init; }

    /// <summary>
    /// Critical safety, sterility, and disposal guidelines for home and community lab environments.
    /// </summary>
    [Description("Critical safety, sterility, and disposal guidelines.")]
    [JsonPropertyName("safety_and_handling_notes")]
    public required string SafetyAndHandlingNotes { get; init; }
}

/// <summary>
/// Comprehensive, actionable bench fabrication package containing all oligonucleotide sequences,
/// stoichiometric mixing ratios, cryogenic thermal annealing ramps, and quality control criteria.
/// </summary>
[SwaggerSchema(Title = "FabricationPackage", Description = "Comprehensive fabrication package with staple oligos, thermal ramps, buffer recipes, and cryptographic manifest.")]
public sealed record FabricationPackage
{
    /// <summary>
    /// Unique bundle identifier for the generated fabrication specification.
    /// </summary>
    [Description("Unique bundle identifier for the generated fabrication specification.")]
    [JsonPropertyName("package_id")]
    public required string PackageId { get; init; }

    /// <summary>
    /// Associated therapeutic regimen (Male-to-Female or Female-to-Male).
    /// </summary>
    [Description("Associated therapeutic regimen (MaleToFemale or FemaleToMale).")]
    [JsonPropertyName("regimen")]
    public required TherapeuticRegimen Regimen { get; init; }

    /// <summary>
    /// Name and length of the single-stranded scaffold DNA backbone (e.g., 'M13mp18 (7,249 nt)').
    /// </summary>
    [Description("Name and length of the single-stranded scaffold DNA backbone.")]
    [JsonPropertyName("scaffold_backbone")]
    public required string ScaffoldBackbone { get; init; }

    /// <summary>
    /// Structural nanocarrier geometry (e.g., '6-Helix Bundle Hexagonal Nanotube').
    /// </summary>
    [Description("Structural nanocarrier geometry.")]
    [JsonPropertyName("delivery_geometry")]
    public required string DeliveryGeometry { get; init; }

    /// <summary>
    /// Target anatomical tissue or cellular receptor.
    /// </summary>
    [Description("Target anatomical tissue or cellular receptor.")]
    [JsonPropertyName("target_tissue")]
    public required string TargetTissue { get; init; }

    /// <summary>
    /// List of all designed oligonucleotide staple strands required for synthesis.
    /// </summary>
    [Description("List of designed oligonucleotide staple strands required for synthesis.")]
    [JsonPropertyName("staple_oligos")]
    public required IReadOnlyList<StapleOligoRecord> StapleOligos { get; init; }

    /// <summary>
    /// Thermal annealing protocol schedule for programmable folding.
    /// </summary>
    [Description("Thermal annealing protocol schedule for programmable folding.")]
    [JsonPropertyName("annealing_schedule")]
    public required IReadOnlyList<AnnealingRampStep> AnnealingSchedule { get; init; }

    /// <summary>
    /// Recommended stoichiometric ratio of staples to scaffold (typically 10:1 to 20:1 excess).
    /// </summary>
    [Description("Recommended stoichiometric ratio of staples to scaffold.")]
    [JsonPropertyName("stoichiometric_ratio")]
    public required string StoichiometricRatio { get; init; }

    /// <summary>
    /// Folding buffer formulation (e.g., 1x TAE + 12.5 mM MgCl2).
    /// </summary>
    [Description("Folding buffer formulation.")]
    [JsonPropertyName("folding_buffer_recipe")]
    public required string FoldingBufferRecipe { get; init; }

    /// <summary>
    /// Expected hydrodynamic radius (in nanometers) validated by Dynamic Light Scattering (DLS) or Cryo-EM.
    /// </summary>
    [Description("Expected hydrodynamic radius in nanometers.")]
    [JsonPropertyName("expected_hydrodynamic_radius_nm")]
    public required double ExpectedHydrodynamicRadiusNm { get; init; }

    /// <summary>
    /// Cryptographic SHA-256 fingerprint guaranteeing provenance and immutability of the design.
    /// </summary>
    [Description("Cryptographic SHA-256 fingerprint guaranteeing provenance.")]
    [JsonPropertyName("sha256_manifest")]
    public required string Sha256Manifest { get; init; }

    /// <summary>
    /// Comprehensive DIY and HomeLab bench fabrication instructions ('how-to') tailored for community researchers.
    /// </summary>
    [Description("Comprehensive DIY and HomeLab bench fabrication instructions.")]
    [JsonPropertyName("diy_fabrication_guide")]
    public required DiyFabricationGuide DiyFabricationGuide { get; init; }

    /// <summary>
    /// Timestamp when this fabrication package was synthesized by the system.
    /// </summary>
    [Description("Timestamp when this fabrication package was synthesized.")]
    [JsonPropertyName("created_at_utc")]
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}
