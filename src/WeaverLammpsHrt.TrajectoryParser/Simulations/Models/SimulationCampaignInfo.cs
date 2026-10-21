using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using WeaverLammpsHrt.TrajectoryParser.Swagger;

namespace WeaverLammpsHrt.TrajectoryParser.Simulations.Models;

/// <summary>
/// Metadata describing an in-silico simulation campaign designed to de-risk physical translation
/// of DNA origami nanocarriers prior to wet-lab synthesis or animal trials.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> A simulation campaign is a computational experiment run on a GPU
/// or CPU cluster (using oxDNA or LAMMPS) that tests whether a nanoscale design will withstand human blood,
/// open its gates properly, and hold hormone cargo without leaking.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "SimulationCampaignInfo", Description = "Metadata describing an in-silico simulation campaign.")]
public sealed record SimulationCampaignInfo : IExamplesProvider<SimulationCampaignInfo>
{
    /// <summary>
    /// Unique identifier for the simulation campaign (e.g., CAMPAIGN_SERUM_STABILITY).
    /// </summary>
    [Description("Unique identifier for the simulation campaign.")]
    [JsonPropertyName("campaignId")]
    public required string CampaignId { get; init; }

    /// <summary>
    /// Human-readable title of the campaign.
    /// </summary>
    [Description("Human-readable title of the campaign.")]
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// The primary computational engine executing this campaign (e.g., oxDNA, LAMMPS, .NET QSP/PBPK).
    /// </summary>
    [Description("Primary computational engine executing this campaign.")]
    [JsonPropertyName("simulationEngine")]
    public required string SimulationEngine { get; init; }

    /// <summary>
    /// Plain-English description of the scientific challenge and computational approach.
    /// </summary>
    [Description("Plain-English description of the scientific challenge and computational approach.")]
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>
    /// The specific physical or biomedical hypothesis being tested and validated.
    /// </summary>
    [Description("The specific physical or biomedical hypothesis being tested and validated.")]
    [JsonPropertyName("physicsGoal")]
    public required string PhysicsGoal { get; init; }

    /// <summary>
    /// Key biophysical parameters evaluated during this campaign.
    /// </summary>
    [Description("Key biophysical parameters evaluated during this campaign.")]
    [JsonPropertyName("keyParameters")]
    public required IReadOnlyList<string> KeyParameters { get; init; }

    /// <summary>
    /// Current operational status of the simulation campaign (e.g., Ready, Validated, In-Silico).
    /// </summary>
    [Description("Current operational status of the simulation campaign.")]
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// Filename of the baseline input deck template located in simulations/configs/.
    /// </summary>
    [Description("Filename of the baseline input deck template located in simulations/configs/.")]
    [JsonPropertyName("configFileTemplate")]
    public required string ConfigFileTemplate { get; init; }

    /// <summary>
    /// Generates a representative canonical example of <see cref="SimulationCampaignInfo"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated example instance.</returns>
    public SimulationCampaignInfo GetExamples() => new()
    {
        CampaignId = "CAMPAIGN_SERUM_STABILITY",
        Name = "Campaign 1: oxDNA Physiological Serum & Salt Stress",
        SimulationEngine = "oxDNA (OpenCL / SYCL)",
        Description = "Models structural fluctuations (RMSF) of DNA origami nanostructures at 37°C in human serum salt conditions (150 mM NaCl, 0.8 mM Mg2+).",
        PhysicsGoal = "Verify that staple crossovers do not fray or melt under physiological ionic stress before ordering oligos.",
        KeyParameters = ["RMSF (< 1.2 nm)", "Melting Point Shift", "Crossover Fraying Count", "0.8 mM Mg2+ Tolerance"],
        Status = "Ready",
        ConfigFileTemplate = "oxdna_serum_stability.in"
    };
}
