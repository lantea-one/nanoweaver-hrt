using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using WeaverLammpsHrt.TrajectoryParser.Swagger;

namespace WeaverLammpsHrt.TrajectoryParser.Simulations.Models;

/// <summary>
/// Execution request for Campaign 1: oxDNA Physiological Serum &amp; Low-Magnesium Stability Simulation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> This request specifies the biological temperature, salt, and magnesium
/// levels of human serum so the oxDNA engine can test if the nanocarrier will hold together or melt.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "SerumStabilityRequest", Description = "Request payload for oxDNA physiological serum stability simulation.")]
public sealed record SerumStabilityRequest : IExamplesProvider<SerumStabilityRequest>
{
    /// <summary>
    /// Identifier or name of the master scaffold sequence (e.g., M13mp18).
    /// </summary>
    [Description("Identifier or name of the master scaffold sequence.")]
    [JsonPropertyName("scaffoldName")]
    public string ScaffoldName { get; set; } = "Bacteriophage M13mp18 (7,249 nt)";

    /// <summary>
    /// Simulated core body temperature in degrees Celsius (default 37.0°C).
    /// </summary>
    [Description("Simulated core body temperature in degrees Celsius (default 37.0°C).")]
    [JsonPropertyName("temperatureCelsius")]
    public double TemperatureCelsius { get; set; } = 37.0;

    /// <summary>
    /// Physiological monovalent sodium chloride concentration in molar (default 0.15 M / 150 mM).
    /// </summary>
    [Description("Physiological monovalent sodium chloride concentration in molar (default 0.15 M / 150 mM).")]
    [JsonPropertyName("monovalentSaltMolar")]
    public double MonovalentSaltMolar { get; set; } = 0.15;

    /// <summary>
    /// Divalent magnesium concentration in millimolar (human blood serum is 0.8 mM; in-vitro folding buffer is 12.5 mM).
    /// </summary>
    [Description("Divalent magnesium concentration in millimolar (human blood serum is 0.8 mM; folding buffer is 12.5 mM).")]
    [JsonPropertyName("divalentMagnesiumMilliMolar")]
    public double DivalentMagnesiumMilliMolar { get; set; } = 0.8;

    /// <summary>
    /// Total coarse-grained simulation integration steps to execute.
    /// </summary>
    [Description("Total coarse-grained simulation integration steps to execute.")]
    [JsonPropertyName("simulationSteps")]
    public long SimulationSteps { get; set; } = 10000000;

    /// <summary>
    /// Number of inter-helix staple crossover junctions to evaluate for mechanical strain.
    /// </summary>
    [Description("Number of inter-helix staple crossover junctions to evaluate.")]
    [JsonPropertyName("stapleCrossoverCount")]
    public int StapleCrossoverCount { get; set; } = 180;

    /// <summary>
    /// Indicates whether enzymatic CpG methylation (M.SssI) has been applied to shield against TLR9 endosomal recognition.
    /// </summary>
    [Description("Indicates whether enzymatic CpG methylation (M.SssI) has been applied.")]
    [JsonPropertyName("cpGMethylationApplied")]
    public bool CpGMethylationApplied { get; set; }

    /// <summary>
    /// Indicates whether poly-L-lysine-polyethylene glycol (PLL-PEG) passivation coating has been applied.
    /// </summary>
    [Description("Indicates whether PLL-PEG passivation coating has been applied.")]
    [JsonPropertyName("pllPegCoatingApplied")]
    public bool PllPegCoatingApplied { get; set; }

    /// <summary>
    /// Generates a representative example of <see cref="SerumStabilityRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated example instance.</returns>
    public SerumStabilityRequest GetExamples() => new()
    {
        ScaffoldName = "Bacteriophage M13mp18 (7,249 nt)",
        TemperatureCelsius = 37.0,
        MonovalentSaltMolar = 0.15,
        DivalentMagnesiumMilliMolar = 0.8,
        SimulationSteps = 10000000,
        StapleCrossoverCount = 180,
        CpGMethylationApplied = true,
        PllPegCoatingApplied = true
    };
}

/// <summary>
/// Specific structural detail for an individual inter-helix staple crossover evaluated in Campaign 1.
/// </summary>
[SwaggerSchema(Title = "CrossoverFrayingDetail", Description = "Structural detail for an individual inter-helix staple crossover.")]
public sealed record CrossoverFrayingDetail
{
    /// <summary>
    /// Index of the staple crossover along the scaffold lattice (0-indexed).
    /// </summary>
    [Description("Index of the staple crossover along the scaffold lattice.")]
    [JsonPropertyName("crossoverIndex")]
    public required int CrossoverIndex { get; init; }

    /// <summary>
    /// Local GC-content percentage of the duplex arms flanking the crossover.
    /// </summary>
    [Description("Local GC-content percentage flanking the crossover.")]
    [JsonPropertyName("localGcPercent")]
    public required double LocalGcPercent { get; init; }

    /// <summary>
    /// Simulated Root Mean Square Fluctuation (RMSF) in nanometers.
    /// </summary>
    [Description("Simulated Root Mean Square Fluctuation (RMSF) in nanometers.")]
    [JsonPropertyName("rmsfNm")]
    public required double RmsfNm { get; init; }

    /// <summary>
    /// Probability (0.0 to 1.0) of spontaneous base-pair fraying under tested conditions.
    /// </summary>
    [Description("Probability (0.0 to 1.0) of spontaneous base-pair fraying.")]
    [JsonPropertyName("frayingProbability")]
    public required double FrayingProbability { get; init; }

    /// <summary>
    /// True if RMSF exceeds safe threshold (> 1.2 nm) or fraying probability exceeds 0.25.
    /// </summary>
    [Description("True if RMSF exceeds safe threshold or fraying probability exceeds 0.25.")]
    [JsonPropertyName("isCompromised")]
    public required bool IsCompromised { get; init; }
}

/// <summary>
/// Outcome of Campaign 1: oxDNA Physiological Serum &amp; Low-Magnesium Stability Simulation.
/// </summary>
[SwaggerSchema(Title = "SerumStabilityAnalysisResult", Description = "Outcome of oxDNA physiological serum stability simulation.")]
public sealed record SerumStabilityAnalysisResult : IExamplesProvider<SerumStabilityAnalysisResult>
{
    /// <summary>
    /// Name of scaffold simulated.
    /// </summary>
    [Description("Name of scaffold simulated.")]
    [JsonPropertyName("scaffoldName")]
    public required string ScaffoldName { get; init; }

    /// <summary>
    /// Simulated temperature in Celsius.
    /// </summary>
    [Description("Simulated temperature in Celsius.")]
    [JsonPropertyName("temperatureCelsius")]
    public required double TemperatureCelsius { get; init; }

    /// <summary>
    /// Simulated magnesium concentration in mM.
    /// </summary>
    [Description("Simulated magnesium concentration in mM.")]
    [JsonPropertyName("divalentMagnesiumMilliMolar")]
    public required double DivalentMagnesiumMilliMolar { get; init; }

    /// <summary>
    /// Global average Root Mean Square Fluctuation (RMSF) in nanometers across all lattice atoms.
    /// </summary>
    [Description("Global average RMSF in nanometers across all lattice atoms.")]
    [JsonPropertyName("averageRmsfNm")]
    public required double AverageRmsfNm { get; init; }

    /// <summary>
    /// Maximum RMSF recorded among all evaluated crossover junctions.
    /// </summary>
    [Description("Maximum RMSF recorded among all evaluated crossover junctions.")]
    [JsonPropertyName("maxCrossoverRmsfNm")]
    public required double MaxCrossoverRmsfNm { get; init; }

    /// <summary>
    /// Calculated Debye-Hückel electrostatic screening length in nanometers.
    /// </summary>
    [Description("Calculated Debye-Hückel electrostatic screening length in nanometers.")]
    [JsonPropertyName("debyeScreeningLengthNm")]
    public required double DebyeScreeningLengthNm { get; init; }

    /// <summary>
    /// Global structural integrity score (0.0 = completely denatured; 1.0 = pristine rigid fold).
    /// </summary>
    [Description("Global structural integrity score (0.0 to 1.0).")]
    [JsonPropertyName("structuralIntegrityScore")]
    public required double StructuralIntegrityScore { get; init; }

    /// <summary>
    /// Number of crossover junctions compromised by low magnesium charge repulsion.
    /// </summary>
    [Description("Number of crossover junctions compromised by low magnesium charge repulsion.")]
    [JsonPropertyName("compromisedCrossoverCount")]
    public required int CompromisedCrossoverCount { get; init; }

    /// <summary>
    /// Total number of crossover junctions analyzed.
    /// </summary>
    [Description("Total number of crossover junctions analyzed.")]
    [JsonPropertyName("totalCrossoverCount")]
    public required int TotalCrossoverCount { get; init; }

    /// <summary>
    /// Clinical verdict and stability determination (e.g. Structurally Stable vs Vulnerable).
    /// </summary>
    [Description("Clinical verdict and stability determination.")]
    [JsonPropertyName("verdict")]
    public required string Verdict { get; init; }

    /// <summary>
    /// Specific wet-lab stabilization recommendations to mitigate detected weaknesses.
    /// </summary>
    [Description("Specific wet-lab stabilization recommendations to mitigate detected weaknesses.")]
    [JsonPropertyName("recommendedStabilization")]
    public required string RecommendedStabilization { get; init; }

    /// <summary>
    /// List of specifically vulnerable crossovers flagged for redesign or crosslinking.
    /// </summary>
    [Description("List of specifically vulnerable crossovers flagged for redesign or crosslinking.")]
    [JsonPropertyName("vulnerableCrossovers")]
    public required IReadOnlyList<CrossoverFrayingDetail> VulnerableCrossovers { get; init; }

    /// <summary>
    /// Continuous RMSF profile across lattice regions for visualization.
    /// </summary>
    [Description("Continuous RMSF profile across lattice regions for visualization.")]
    [JsonPropertyName("rmsfProfilePoints")]
    public required IReadOnlyList<double> RmsfProfilePoints { get; init; }

    /// <summary>
    /// Generates a representative example of <see cref="SerumStabilityAnalysisResult"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated example instance.</returns>
    public SerumStabilityAnalysisResult GetExamples() => new()
    {
        ScaffoldName = "Bacteriophage M13mp18 (7,249 nt)",
        TemperatureCelsius = 37.0,
        DivalentMagnesiumMilliMolar = 0.8,
        AverageRmsfNm = 0.82,
        MaxCrossoverRmsfNm = 1.15,
        DebyeScreeningLengthNm = 0.78,
        StructuralIntegrityScore = 0.94,
        CompromisedCrossoverCount = 0,
        TotalCrossoverCount = 180,
        Verdict = "Structurally Stable Under Physiological Ionic Stress (PLL-PEG Shield Active)",
        RecommendedStabilization = "Chassis is stabilized by PEGylation and CpG methylation. Proceed to Campaign 2.",
        VulnerableCrossovers = [],
        RmsfProfilePoints = [0.45, 0.52, 0.61, 0.78, 0.82, 0.74, 0.65, 0.58]
    };
}
