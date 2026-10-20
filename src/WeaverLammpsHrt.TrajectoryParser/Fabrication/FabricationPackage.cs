using System.Text.Json.Serialization;
using WeaverLammpsHrt.TrajectoryParser.Endocrine;

namespace WeaverLammpsHrt.TrajectoryParser.Fabrication;

/// <summary>
/// Represents a single synthetic oligonucleotide staple strand designed to fold the long scaffold
/// into the programmed DNA origami nanocarrier geometry.
/// </summary>
public sealed record StapleOligoRecord
{
    /// <summary>
    /// Systematic identifier for the staple oligo (e.g., 'ST-001', 'ST-002').
    /// </summary>
    [JsonPropertyName("staple_id")]
    public required string StapleId { get; init; }

    /// <summary>
    /// 5' to 3' nucleotide sequence of the synthetic DNA oligo.
    /// </summary>
    [JsonPropertyName("sequence")]
    public required string Sequence { get; init; }

    /// <summary>
    /// Length of the oligonucleotide in nucleotides (bases).
    /// </summary>
    [JsonPropertyName("length")]
    public required int Length { get; init; }

    /// <summary>
    /// Theoretical melting temperature (Tm) in degrees Celsius calculated for physiological salt conditions.
    /// </summary>
    [JsonPropertyName("melting_temperature_celsius")]
    public required double MeltingTemperatureCelsius { get; init; }

    /// <summary>
    /// Guanine-Cytosine content percentage (% GC).
    /// </summary>
    [JsonPropertyName("gc_percentage")]
    public required double GcPercentage { get; init; }

    /// <summary>
    /// Recommended stock concentration in micromolar (µM) for stoichiometric assembly.
    /// </summary>
    [JsonPropertyName("stock_concentration_micromolar")]
    public required double StockConcentrationMicromolar { get; init; }

    /// <summary>
    /// Spatial lattice domain or functional role (e.g., 'Core Helix 1-3 Crossover', 'Aptamer Loop Staple').
    /// </summary>
    [JsonPropertyName("functional_role")]
    public required string FunctionalRole { get; init; }
}

/// <summary>
/// Defines a single temperature stage in the thermal annealing protocol required to fold DNA origami.
/// </summary>
public sealed record AnnealingRampStep
{
    /// <summary>
    /// Sequence order index of this thermal step.
    /// </summary>
    [JsonPropertyName("step_index")]
    public required int StepIndex { get; init; }

    /// <summary>
    /// Target temperature in degrees Celsius.
    /// </summary>
    [JsonPropertyName("target_temperature_celsius")]
    public required double TargetTemperatureCelsius { get; init; }

    /// <summary>
    /// Incubation / hold duration in minutes at this temperature.
    /// </summary>
    [JsonPropertyName("hold_duration_minutes")]
    public required int HoldDurationMinutes { get; init; }

    /// <summary>
    /// Ramp rate in degrees Celsius per minute used to transition to the next step.
    /// </summary>
    [JsonPropertyName("ramp_rate_celsius_per_minute")]
    public required double RampRateCelsiusPerMinute { get; init; }

    /// <summary>
    /// Plain-English explanation of what physical molecular behavior is occurring at this thermal stage.
    /// </summary>
    [JsonPropertyName("biophysical_purpose")]
    public required string BiophysicalPurpose { get; init; }
}

/// <summary>
/// Comprehensive, actionable bench fabrication package containing all oligonucleotide sequences,
/// stoichiometric mixing ratios, cryogenic thermal annealing ramps, and quality control criteria.
/// </summary>
public sealed record FabricationPackage
{
    /// <summary>
    /// Unique bundle identifier for the generated fabrication specification.
    /// </summary>
    [JsonPropertyName("package_id")]
    public required string PackageId { get; init; }

    /// <summary>
    /// Associated therapeutic regimen (Male-to-Female or Female-to-Male).
    /// </summary>
    [JsonPropertyName("regimen")]
    public required TherapeuticRegimen Regimen { get; init; }

    /// <summary>
    /// Name and length of the single-stranded scaffold DNA backbone (e.g., 'M13mp18 (7,249 nt)').
    /// </summary>
    [JsonPropertyName("scaffold_backbone")]
    public required string ScaffoldBackbone { get; init; }

    /// <summary>
    /// Structural nanocarrier geometry (e.g., '6-Helix Bundle Hexagonal Nanotube').
    /// </summary>
    [JsonPropertyName("delivery_geometry")]
    public required string DeliveryGeometry { get; init; }

    /// <summary>
    /// Target anatomical tissue or cellular receptor.
    /// </summary>
    [JsonPropertyName("target_tissue")]
    public required string TargetTissue { get; init; }

    /// <summary>
    /// List of all designed oligonucleotide staple strands required for synthesis.
    /// </summary>
    [JsonPropertyName("staple_oligos")]
    public required IReadOnlyList<StapleOligoRecord> StapleOligos { get; init; }

    /// <summary>
    /// Thermal annealing protocol schedule for programmable folding.
    /// </summary>
    [JsonPropertyName("annealing_schedule")]
    public required IReadOnlyList<AnnealingRampStep> AnnealingSchedule { get; init; }

    /// <summary>
    /// Recommended stoichiometric ratio of staples to scaffold (typically 10:1 to 20:1 excess).
    /// </summary>
    [JsonPropertyName("stoichiometric_ratio")]
    public required string StoichiometricRatio { get; init; }

    /// <summary>
    /// Folding buffer formulation (e.g., 1x TAE + 12.5 mM MgCl2).
    /// </summary>
    [JsonPropertyName("folding_buffer_recipe")]
    public required string FoldingBufferRecipe { get; init; }

    /// <summary>
    /// Expected hydrodynamic radius (in nanometers) validated by Dynamic Light Scattering (DLS) or Cryo-EM.
    /// </summary>
    [JsonPropertyName("expected_hydrodynamic_radius_nm")]
    public required double ExpectedHydrodynamicRadiusNm { get; init; }

    /// <summary>
    /// Cryptographic SHA-256 fingerprint guaranteeing provenance and immutability of the design.
    /// </summary>
    [JsonPropertyName("sha256_manifest")]
    public required string Sha256Manifest { get; init; }

    /// <summary>
    /// Timestamp when this fabrication package was synthesized by the system.
    /// </summary>
    [JsonPropertyName("created_at_utc")]
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}
