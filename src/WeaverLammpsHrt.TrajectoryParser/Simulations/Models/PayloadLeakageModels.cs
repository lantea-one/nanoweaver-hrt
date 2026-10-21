using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using WeaverLammpsHrt.TrajectoryParser.Swagger;

namespace WeaverLammpsHrt.TrajectoryParser.Simulations.Models;

/// <summary>
/// Execution request for Campaign 3: Steroid Payload Encapsulation &amp; Micro-Leakage Dynamics.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> This simulation packs steroid hormones into the nanocarrier's
/// interior hollow chamber and measures whether the molecules slip out while closed, and how fast they escape
/// once the latch is opened.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "PayloadLeakageRequest", Description = "Request payload for steroid encapsulation and leakage dynamics simulation.")]
public sealed record PayloadLeakageRequest : IExamplesProvider<PayloadLeakageRequest>
{
    /// <summary>
    /// Geometry of the nanocarrier cavity being loaded (e.g. Hexagonal Nanotube, Icosahedral Nanocage).
    /// </summary>
    [Description("Geometry of the nanocarrier cavity being loaded.")]
    [JsonPropertyName("chassisGeometry")]
    public string ChassisGeometry { get; set; } = "Hexagonal Nanotube (6-HB)";

    /// <summary>
    /// Specific steroid hormone molecule to simulate (e.g. 17β-Estradiol or Testosterone Enanthate).
    /// </summary>
    [Description("Specific steroid hormone molecule to simulate.")]
    [JsonPropertyName("hormoneMolecule")]
    public string HormoneMolecule { get; set; } = "17β-Estradiol";

    /// <summary>
    /// Number of hormone molecules encapsulated in the cavity (default 50).
    /// </summary>
    [Description("Number of hormone molecules encapsulated in the cavity.")]
    [JsonPropertyName("loadedMoleculeCount")]
    public int LoadedMoleculeCount { get; set; } = 50;

    /// <summary>
    /// Duration of molecular dynamics trajectory to simulate in nanoseconds.
    /// </summary>
    [Description("Duration of molecular dynamics trajectory to simulate in nanoseconds.")]
    [JsonPropertyName("simulationDurationNs")]
    public double SimulationDurationNs { get; set; } = 100.0;

    /// <summary>
    /// Internal chemical passivation mode (Bare DNA, Cyclodextrin Anchored, Cholesterol Tethered).
    /// </summary>
    [Description("Internal chemical passivation mode.")]
    [JsonPropertyName("cavityFunctionalization")]
    public string CavityFunctionalization { get; set; } = "Cyclodextrin Anchored";

    /// <summary>
    /// Generates a representative example of <see cref="PayloadLeakageRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated example instance.</returns>
    public PayloadLeakageRequest GetExamples() => new()
    {
        ChassisGeometry = "Hexagonal Nanotube (6-HB)",
        HormoneMolecule = "17β-Estradiol",
        LoadedMoleculeCount = 50,
        SimulationDurationNs = 100.0,
        CavityFunctionalization = "Cyclodextrin Anchored"
    };
}

/// <summary>
/// Time point tracking steroid retention in closed vs open gate states.
/// </summary>
[SwaggerSchema(Title = "PayloadRetentionTimePoint", Description = "Time point tracking steroid retention in closed vs open gate states.")]
public sealed record PayloadRetentionTimePoint
{
    /// <summary>
    /// Simulation time in nanoseconds.
    /// </summary>
    [Description("Simulation time in nanoseconds.")]
    [JsonPropertyName("timeNs")]
    public required double TimeNs { get; init; }

    /// <summary>
    /// Percentage of payload remaining inside cavity when latch is closed.
    /// </summary>
    [Description("Percentage of payload remaining inside cavity when latch is closed.")]
    [JsonPropertyName("closedLatchRetentionPercent")]
    public required double ClosedLatchRetentionPercent { get; init; }

    /// <summary>
    /// Percentage of payload remaining inside cavity when latch is open.
    /// </summary>
    [Description("Percentage of payload remaining inside cavity when latch is open.")]
    [JsonPropertyName("openLatchRetentionPercent")]
    public required double OpenLatchRetentionPercent { get; init; }
}

/// <summary>
/// Outcome of Campaign 3: Steroid Payload Encapsulation &amp; Micro-Leakage Dynamics.
/// </summary>
[SwaggerSchema(Title = "PayloadLeakageAnalysisResult", Description = "Outcome of steroid payload encapsulation and leakage simulation.")]
public sealed record PayloadLeakageAnalysisResult : IExamplesProvider<PayloadLeakageAnalysisResult>
{
    /// <summary>
    /// Nanocarrier geometry simulated.
    /// </summary>
    [Description("Nanocarrier geometry simulated.")]
    [JsonPropertyName("chassisGeometry")]
    public required string ChassisGeometry { get; init; }

    /// <summary>
    /// Hormone molecule simulated.
    /// </summary>
    [Description("Hormone molecule simulated.")]
    [JsonPropertyName("hormoneMolecule")]
    public required string HormoneMolecule { get; init; }

    /// <summary>
    /// Internal chemical cavity functionalization applied.
    /// </summary>
    [Description("Internal chemical cavity functionalization applied.")]
    [JsonPropertyName("cavityFunctionalization")]
    public required string CavityFunctionalization { get; init; }

    /// <summary>
    /// Percentage of cargo retained inside cavity over 100 ns in closed state (target &gt; 98%).
    /// </summary>
    [Description("Percentage of cargo retained inside cavity over 100 ns in closed state.")]
    [JsonPropertyName("retentionPercentageClosed")]
    public required double RetentionPercentageClosed { get; init; }

    /// <summary>
    /// Unintended passive micro-leakage percentage in closed state.
    /// </summary>
    [Description("Unintended passive micro-leakage percentage in closed state.")]
    [JsonPropertyName("passiveLeakagePercentage")]
    public required double PassiveLeakagePercentage { get; init; }

    /// <summary>
    /// Active release rate (%/min) when aptamer gate is triggered open.
    /// </summary>
    [Description("Active release rate (%/min) when aptamer gate is triggered open.")]
    [JsonPropertyName("releaseRatePerMinuteOpen")]
    public required double ReleaseRatePerMinuteOpen { get; init; }

    /// <summary>
    /// Effective stable encapsulation capacity in discrete hormone molecules.
    /// </summary>
    [Description("Effective stable encapsulation capacity in discrete hormone molecules.")]
    [JsonPropertyName("encapsulationCapacityMolecules")]
    public required int EncapsulationCapacityMolecules { get; init; }

    /// <summary>
    /// Maximum daily physiological hormone turnover supported in nanomoles.
    /// </summary>
    [Description("Maximum daily physiological hormone turnover supported in nanomoles.")]
    [JsonPropertyName("maxDailyDeliveryNanomoles")]
    public required double MaxDailyDeliveryNanomoles { get; init; }

    /// <summary>
    /// Cargo containment determination verdict.
    /// </summary>
    [Description("Cargo containment determination verdict.")]
    [JsonPropertyName("leakageVerdict")]
    public required string LeakageVerdict { get; init; }

    /// <summary>
    /// Time-course retention trajectory curves.
    /// </summary>
    [Description("Time-course retention trajectory curves.")]
    [JsonPropertyName("retentionCurve")]
    public required IReadOnlyList<PayloadRetentionTimePoint> RetentionCurve { get; init; }

    /// <summary>
    /// Generates a representative example of <see cref="PayloadLeakageAnalysisResult"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated example instance.</returns>
    public PayloadLeakageAnalysisResult GetExamples() => new()
    {
        ChassisGeometry = "Hexagonal Nanotube (6-HB)",
        HormoneMolecule = "17β-Estradiol",
        CavityFunctionalization = "Cyclodextrin Anchored",
        RetentionPercentageClosed = 98.6,
        PassiveLeakagePercentage = 1.4,
        ReleaseRatePerMinuteOpen = 8.4,
        EncapsulationCapacityMolecules = 48,
        MaxDailyDeliveryNanomoles = 1.25,
        LeakageVerdict = "Sterically Contained (< 2% Passive Leakage)",
        RetentionCurve = [
            new() { TimeNs = 0.0, ClosedLatchRetentionPercent = 100.0, OpenLatchRetentionPercent = 100.0 },
            new() { TimeNs = 20.0, ClosedLatchRetentionPercent = 99.5, OpenLatchRetentionPercent = 65.0 },
            new() { TimeNs = 50.0, ClosedLatchRetentionPercent = 99.0, OpenLatchRetentionPercent = 32.0 },
            new() { TimeNs = 100.0, ClosedLatchRetentionPercent = 98.6, OpenLatchRetentionPercent = 8.0 }
        ]
    };
}
