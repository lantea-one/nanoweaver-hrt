using System.Text.Json.Serialization;

namespace NanoWeaverHrt.Client.Api;

/// <summary>
/// Summary metadata describing an in-silico simulation campaign available on a node.
/// </summary>
/// <param name="CampaignId">Unique campaign identifier (e.g., CAMPAIGN_SERUM_STABILITY).</param>
/// <param name="Name">Human-readable title of the campaign.</param>
/// <param name="SimulationEngine">Underlying engine (oxDNA, LAMMPS, .NET PBPK).</param>
/// <param name="Description">Biophysical objective description.</param>
/// <param name="PhysicsGoal">Hypothesis and parameter goals.</param>
/// <param name="IsActive">True if this campaign is currently actively running on the node.</param>
public sealed record SimulationCampaignSummary(
    [property: JsonPropertyName("campaignId")] string CampaignId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("simulationEngine")] string SimulationEngine,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("physicsGoal")] string PhysicsGoal,
    [property: JsonPropertyName("isActive")] bool IsActive = false);
