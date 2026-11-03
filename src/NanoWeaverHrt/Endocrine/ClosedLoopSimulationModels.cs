using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Endocrine;

/// <summary>
/// Simulation request for dynamic closed-loop endocrine feedback and pituitary gonadotropin suppression.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Specifies baseline hormone levels, target therapeutic setpoints,
/// and temporal integration parameters for modeling how aptamer-gated DNA origami nanocarriers regulate
/// circulating 17β-estradiol, testosterone, and pituitary gonadotropins (LH and FSH) across the HPG axis.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "ClosedLoopSimulationRequest", Description = "Request payload for closed-loop endocrine simulation.")]
public sealed record ClosedLoopSimulationRequest : IExamplesProvider<ClosedLoopSimulationRequest>
{
    /// <summary>
    /// Initial baseline serum 17β-estradiol concentration in pmol/L (e.g. 120.0 for MtF, 600.0 for FtM).
    /// </summary>
    [Description("Initial baseline serum 17β-estradiol concentration in pmol/L.")]
    [JsonPropertyName("initialEstradiolPmolL")]
    public double InitialEstradiolPmolL { get; init; } = 120.0;

    /// <summary>
    /// Initial baseline serum total testosterone concentration in nmol/L (e.g. 18.0 for MtF, 1.5 for FtM).
    /// </summary>
    [Description("Initial baseline serum total testosterone concentration in nmol/L.")]
    [JsonPropertyName("initialTestosteroneNmolL")]
    public double InitialTestosteroneNmolL { get; init; } = 18.0;

    /// <summary>
    /// Initial circulating luteinizing hormone (LH) concentration in IU/L (reference baseline ~6.5 IU/L).
    /// </summary>
    [Description("Initial circulating luteinizing hormone (LH) concentration in IU/L.")]
    [JsonPropertyName("initialLhIuL")]
    public double InitialLhIuL { get; init; } = 6.5;

    /// <summary>
    /// Initial circulating follicle-stimulating hormone (FSH) concentration in IU/L (reference baseline ~5.0 IU/L).
    /// </summary>
    [Description("Initial circulating follicle-stimulating hormone (FSH) concentration in IU/L.")]
    [JsonPropertyName("initialFshIuL")]
    public double InitialFshIuL { get; init; } = 5.0;

    /// <summary>
    /// Target 17β-estradiol setpoint concentration in pmol/L (e.g. 550.0 pmol/L for MtF).
    /// </summary>
    [Description("Target 17β-estradiol setpoint concentration in pmol/L.")]
    [JsonPropertyName("targetEstradiolPmolL")]
    public double TargetEstradiolPmolL { get; init; } = 550.0;

    /// <summary>
    /// Target total testosterone setpoint concentration in nmol/L (e.g. 1.2 nmol/L for MtF).
    /// </summary>
    [Description("Target total testosterone setpoint concentration in nmol/L.")]
    [JsonPropertyName("targetTestosteroneNmolL")]
    public double TargetTestosteroneNmolL { get; init; } = 1.2;

    /// <summary>
    /// Target operational toggle state (EstrogenicDominant or AndrogenicDominant).
    /// </summary>
    [Description("Target operational toggle state.")]
    [JsonPropertyName("targetState")]
    public ToggleState TargetState { get; init; } = ToggleState.EstrogenicDominant;

    /// <summary>
    /// Total duration of the simulation in hours (range 1 to 720 hours, default 72).
    /// </summary>
    [Description("Total duration of the simulation in hours (range 1 to 720).")]
    [JsonPropertyName("totalHours")]
    public int TotalHours { get; init; } = 72;

    /// <summary>
    /// Temporal sampling step size in hours (range 0.1 to 24.0 hours, default 1.0).
    /// </summary>
    [Description("Temporal sampling step size in hours (range 0.1 to 24.0).")]
    [JsonPropertyName("stepHours")]
    public double StepHours { get; init; } = 1.0;

    /// <summary>
    /// Patient-specific metabolic clearance rate multiplier (range 0.1 to 5.0, default 1.0).
    /// </summary>
    [Description("Patient-specific metabolic clearance rate multiplier (range 0.1 to 5.0).")]
    [JsonPropertyName("clearanceMultiplier")]
    public double ClearanceMultiplier { get; init; } = 1.0;

    /// <summary>
    /// Generates a representative example instance of <see cref="ClosedLoopSimulationRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated request example.</returns>
    public ClosedLoopSimulationRequest GetExamples() => new();
}

/// <summary>
/// Represents a discrete temporal sample along the closed-loop endocrine simulation trajectory.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Captures circulating hormone concentrations, pituitary gonadotropins,
/// and aptamer-gated nanocarrier release rate at a specific hour during the simulation.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "ClosedLoopPoint", Description = "Temporal sample point in a closed-loop endocrine simulation trajectory.")]
public sealed record ClosedLoopPoint
{
    /// <summary>
    /// Elapsed simulation time in hours.
    /// </summary>
    [Description("Elapsed simulation time in hours.")]
    [JsonPropertyName("hour")]
    public double Hour { get; init; }

    /// <summary>
    /// Circulating 17β-estradiol concentration in pmol/L.
    /// </summary>
    [Description("Circulating 17β-estradiol concentration in pmol/L.")]
    [JsonPropertyName("estradiolPmolL")]
    public double EstradiolPmolL { get; init; }

    /// <summary>
    /// Circulating total testosterone concentration in nmol/L.
    /// </summary>
    [Description("Circulating total testosterone concentration in nmol/L.")]
    [JsonPropertyName("testosteroneNmolL")]
    public double TestosteroneNmolL { get; init; }

    /// <summary>
    /// Circulating luteinizing hormone (LH) concentration in IU/L.
    /// </summary>
    [Description("Circulating luteinizing hormone (LH) concentration in IU/L.")]
    [JsonPropertyName("lhIuL")]
    public double LhIuL { get; init; }

    /// <summary>
    /// Circulating follicle-stimulating hormone (FSH) concentration in IU/L.
    /// </summary>
    [Description("Circulating follicle-stimulating hormone (FSH) concentration in IU/L.")]
    [JsonPropertyName("fshIuL")]
    public double FshIuL { get; init; }

    /// <summary>
    /// Instantaneous feedback-controlled nanocarrier payload release rate (pmol/(L·h) or nmol/(L·h)).
    /// </summary>
    [Description("Instantaneous feedback-controlled nanocarrier payload release rate.")]
    [JsonPropertyName("carrierReleaseRate")]
    public double CarrierReleaseRate { get; init; }
}

/// <summary>
/// Outcome of a closed-loop endocrine simulation run.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Summarizes the trajectory of hormone and gonadotropin levels over time,
/// including final concentrations, whether steady state was reached, and the time required to suppress pituitary gonadotropins.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "ClosedLoopSimulationResult", Description = "Outcome of a closed-loop endocrine simulation run.")]
public sealed record ClosedLoopSimulationResult : IExamplesProvider<ClosedLoopSimulationResult>
{
    /// <summary>
    /// Chronological sequence of simulated trajectory data points.
    /// </summary>
    [Description("Chronological sequence of simulated trajectory data points.")]
    [JsonPropertyName("points")]
    public IReadOnlyList<ClosedLoopPoint> Points { get; init; } = [];

    /// <summary>
    /// Final 17β-estradiol concentration in pmol/L at the conclusion of the simulation.
    /// </summary>
    [Description("Final 17β-estradiol concentration in pmol/L.")]
    [JsonPropertyName("finalEstradiolPmolL")]
    public double FinalEstradiolPmolL { get; init; }

    /// <summary>
    /// Final total testosterone concentration in nmol/L at the conclusion of the simulation.
    /// </summary>
    [Description("Final total testosterone concentration in nmol/L.")]
    [JsonPropertyName("finalTestosteroneNmolL")]
    public double FinalTestosteroneNmolL { get; init; }

    /// <summary>
    /// Final luteinizing hormone (LH) concentration in IU/L at the conclusion of the simulation.
    /// </summary>
    [Description("Final luteinizing hormone (LH) concentration in IU/L.")]
    [JsonPropertyName("finalLhIuL")]
    public double FinalLhIuL { get; init; }

    /// <summary>
    /// Final follicle-stimulating hormone (FSH) concentration in IU/L at the conclusion of the simulation.
    /// </summary>
    [Description("Final follicle-stimulating hormone (FSH) concentration in IU/L.")]
    [JsonPropertyName("finalFshIuL")]
    public double FinalFshIuL { get; init; }

    /// <summary>
    /// Indicates whether homeostatic steady state was reached during the simulated horizon.
    /// </summary>
    [Description("Indicates whether homeostatic steady state was reached during the simulated horizon.")]
    [JsonPropertyName("steadyStateReached")]
    public bool SteadyStateReached { get; init; }

    /// <summary>
    /// Time in hours at which gonadotropin suppression (LH &lt; 1.0 IU/L or FSH &lt; 1.5 IU/L) was first achieved, or -1 if not suppressed.
    /// </summary>
    [Description("Time in hours at which gonadotropin suppression was first achieved, or -1 if not suppressed.")]
    [JsonPropertyName("timeToSuppressionHours")]
    public double TimeToSuppressionHours { get; init; } = -1.0;

    /// <summary>
    /// Generates a representative example instance of <see cref="ClosedLoopSimulationResult"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated simulation result example.</returns>
    public ClosedLoopSimulationResult GetExamples() => new()
    {
        Points =
        [
            new ClosedLoopPoint { Hour = 0.0, EstradiolPmolL = 120.0, TestosteroneNmolL = 18.0, LhIuL = 6.5, FshIuL = 5.0, CarrierReleaseRate = 559.0 },
            new ClosedLoopPoint { Hour = 72.0, EstradiolPmolL = 514.57, TestosteroneNmolL = 8.41, LhIuL = 0.42, FshIuL = 0.85, CarrierReleaseRate = 36.12 }
        ],
        FinalEstradiolPmolL = 514.57,
        FinalTestosteroneNmolL = 8.41,
        FinalLhIuL = 0.42,
        FinalFshIuL = 0.85,
        SteadyStateReached = true,
        TimeToSuppressionHours = 5.0
    };
}
