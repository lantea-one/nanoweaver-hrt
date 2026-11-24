using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Endocrine;

/// <summary>
/// Clinical safety, biophysical handoff, and mitotic inheritance constants governing multi-year durability forecasts.
/// </summary>
public static class MultiYearHomeostasisThresholds
{
    /// <summary>
    /// Minimum permitted forecast horizon in years.
    /// </summary>
    public const int MinHorizonYears = 1;

    /// <summary>
    /// Maximum permitted forecast horizon in years.
    /// </summary>
    public const int MaxHorizonYears = 10;

    /// <summary>
    /// Minimum permitted per-division DNMT1 maintenance methylation fidelity.
    /// </summary>
    public const double MinMitoticFidelityRate = 0.90;

    /// <summary>
    /// Maximum permitted per-division DNMT1 maintenance methylation fidelity.
    /// </summary>
    public const double MaxMitoticFidelityRate = 0.9999;

    /// <summary>
    /// Minimum projected multi-year Time in Target Range percentage required to verify permanent homeostasis.
    /// </summary>
    public const double RequiredTenYearTtrPercent = 95.0;

    /// <summary>
    /// Minimum retained methylated progenitor fraction required at horizon end without washout drift.
    /// </summary>
    public const double MinimumProgenitorFractionFloor = 0.55;

    /// <summary>
    /// First day of the coupled hydrogel-to-epigenetic handoff surveillance window.
    /// </summary>
    public const int HandoffStartDay = 90;

    /// <summary>
    /// Final day of the coupled hydrogel-to-epigenetic handoff surveillance window.
    /// </summary>
    public const int HandoffEndDay = 180;
}

/// <summary>
/// Discrete trajectory sample point capturing dual-source exogenous hydrogel vs. endogenous epigenetic
/// steroidogenesis, serum hormone concentrations, and chromatin memory retention.
/// </summary>
[SwaggerSchema(Title = "MultiYearTrajectoryPoint", Description = "Trajectory sample along a 1-to-10-year coupled hydrogel-to-epigenetic endocrine forecast.")]
public sealed record MultiYearTrajectoryPoint
{
    /// <summary>
    /// Gets or initializes the elapsed simulation day (0 through HorizonYears * 365).
    /// </summary>
    [Description("Elapsed simulation day since initial depot implantation.")]
    [JsonPropertyName("day")]
    public int Day { get; init; }

    /// <summary>
    /// Gets or initializes elapsed simulation time in fractional years (<c>day / 365.0</c>).
    /// </summary>
    [Description("Elapsed simulation time in years (Day / 365.0).")]
    [JsonPropertyName("yearFraction")]
    public double YearFraction { get; init; }

    /// <summary>
    /// Gets or initializes circulating serum 17β-estradiol concentration in pmol/L.
    /// </summary>
    [Description("Circulating serum 17β-estradiol concentration in pmol/L.")]
    [JsonPropertyName("estradiolPmolL")]
    public double EstradiolPmolL { get; init; }

    /// <summary>
    /// Gets or initializes circulating serum total testosterone concentration in nmol/L.
    /// </summary>
    [Description("Circulating serum total testosterone concentration in nmol/L.")]
    [JsonPropertyName("testosteroneNmolL")]
    public double TestosteroneNmolL { get; init; }

    /// <summary>
    /// Gets or initializes circulating luteinizing hormone (LH) concentration in IU/L.
    /// </summary>
    [Description("Circulating serum luteinizing hormone (LH) concentration in IU/L.")]
    [JsonPropertyName("lhIuL")]
    public double LhIuL { get; init; }

    /// <summary>
    /// Gets or initializes circulating follicle-stimulating hormone (FSH) concentration in IU/L.
    /// </summary>
    [Description("Circulating serum follicle-stimulating hormone (FSH) concentration in IU/L.")]
    [JsonPropertyName("fshIuL")]
    public double FshIuL { get; init; }

    /// <summary>
    /// Gets or initializes the normalized fraction of hormone output supplied by the exogenous polymer depot (0.0 to 1.0).
    /// </summary>
    [Description("Fraction of hormone drive contributed by exogenous hydrogel depot elution (0.0 to 1.0).")]
    [JsonPropertyName("hydrogelContributionFraction")]
    public double HydrogelContributionFraction { get; init; }

    /// <summary>
    /// Gets or initializes the normalized fraction of hormone output supplied by endogenous epigenetically reprogrammed tissue (0.0 to 1.0).
    /// </summary>
    [Description("Fraction of hormone drive contributed by endogenous epigenetically reprogrammed tissue (0.0 to 1.0).")]
    [JsonPropertyName("endogenousContributionFraction")]
    public double EndogenousContributionFraction { get; init; }

    /// <summary>
    /// Gets or initializes the fraction of progenitors actively preserving the reprogrammed epigenetic mark (0.0 to 1.0).
    /// </summary>
    [Description("Weighted quiescent + proliferating methylated progenitor cell fraction (0.0 to 1.0).")]
    [JsonPropertyName("methylatedProgenitorFraction")]
    public double MethylatedProgenitorFraction { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether therapeutic hormones remain inside physiological target bounds on this day.
    /// </summary>
    [Description("True when systemic E2/T levels fall inside the regimen clinical target range.")]
    [JsonPropertyName("inTargetRange")]
    public bool InTargetRange { get; init; }
}

/// <summary>
/// Comprehensive longitudinal systems-biology forecast evaluating 1-to-10-year coupled hydrogel handoff
/// and mitotic cell-turnover durability for a persistent depot formulation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Complete API and clinical workspace report aggregating multi-year TTR,
/// Day 90–180 handoff continuity, chromatin washout advisory detection, annual checkpoints, and downsampled SVG curves.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 43 (P226): Enables clinicians, API clients, and GUI screens to verify
/// durable lifetime hormone independence and surface early mitotic drift warnings before sub-therapeutic escape occurs.
/// </para>
/// <para>
/// <strong>How it works:</strong> Generated by <see cref="IMultiYearHomeostasisService"/> via deterministic daily RK4
/// integration and persisted atomically to <see cref="NanoWeaverHrt.Storage.IMultiYearHomeostasisRepository"/>.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "MultiYearHomeostasisForecastReport", Description = "Complete 1-to-10-year coupled hydrogel-to-epigenetic durability forecast and mitotic drift report.")]
public sealed record MultiYearHomeostasisForecastReport : IExamplesProvider<MultiYearHomeostasisForecastReport>
{
    /// <summary>
    /// Gets or initializes the unique identifier for this multi-year forecast run.
    /// </summary>
    [Description("Unique identifier of this multi-year durability forecast run.")]
    [JsonPropertyName("runId")]
    public string RunId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the parent semi-annual depot formulation identifier.
    /// </summary>
    [Description("Semi-annual polymer depot formulation identifier.")]
    [JsonPropertyName("depotId")]
    public string DepotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the optional linked epigenetic toggle circuit identifier.
    /// </summary>
    [Description("Optional linked epigenetic toggle circuit identifier.")]
    [JsonPropertyName("circuitId")]
    public string? CircuitId { get; init; }

    /// <summary>
    /// Gets or initializes the owning patient identifier.
    /// </summary>
    [Description("Patient identifier owning this forecast.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the target therapeutic endocrine regimen.
    /// </summary>
    [Description("Target therapeutic endocrine regimen.")]
    [JsonPropertyName("regimen")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TherapeuticRegimen Regimen { get; init; } = TherapeuticRegimen.MaleToFemale;

    /// <summary>
    /// Gets or initializes the simulated horizon in years (1 to 10).
    /// </summary>
    [Description("Simulated longitudinal horizon in years.")]
    [JsonPropertyName("horizonYears")]
    public int HorizonYears { get; init; } = 10;

    /// <summary>
    /// Gets or initializes the total simulated discrete daily time steps (<c>HorizonYears * 365</c>).
    /// </summary>
    [Description("Total simulated discrete days (HorizonYears * 365).")]
    [JsonPropertyName("simulatedDays")]
    public int SimulatedDays { get; init; } = 3650;

    /// <summary>
    /// Gets or initializes the simulated DNMT1 maintenance methylation fidelity per mitotic division.
    /// </summary>
    [Description("DNMT1 maintenance methylation fidelity per division (0.90 to 0.9999).")]
    [JsonPropertyName("mitoticFidelityRate")]
    public double MitoticFidelityRate { get; init; } = 0.9985;

    /// <summary>
    /// Gets or initializes the initial fraction of target progenitor cells transduced.
    /// </summary>
    [Description("Initial fraction of target progenitor cells epigenetically reprogrammed (0.10 to 1.0).")]
    [JsonPropertyName("transducedProgenitorFraction")]
    public double TransducedProgenitorFraction { get; init; } = 0.85;

    /// <summary>
    /// Gets or initializes the overall Time in Target Range percentage across the entire multi-year horizon.
    /// </summary>
    [Description("Projected multi-year Time in Target Range percentage (benchmark >= 95.0%).")]
    [JsonPropertyName("projectedTenYearTtrPercent")]
    public double ProjectedTenYearTtrPercent { get; init; }

    /// <summary>
    /// Gets or initializes the Time in Target Range percentage inside the critical Day 90–180 handoff window.
    /// </summary>
    [Description("Time in Target Range percentage during the Day 90-180 hydrogel-to-epigenetic handoff window.")]
    [JsonPropertyName("handoffWindowTtrPercent")]
    public double HandoffWindowTtrPercent { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether systemic hormone levels dropped below target during Day 90–180 handoff.
    /// </summary>
    [Description("True if a sub-therapeutic trough drop occurred during the Day 90-180 exogenous-to-endogenous handoff.")]
    [JsonPropertyName("handoffTroughDropDetected")]
    public bool HandoffTroughDropDetected { get; init; }

    /// <summary>
    /// Gets or initializes the final retained methylated progenitor fraction at the end of the simulation horizon.
    /// </summary>
    [Description("Retained methylated progenitor fraction at the end of the horizon (0.0 to 1.0).")]
    [JsonPropertyName("finalMethylatedProgenitorFraction")]
    public double FinalMethylatedProgenitorFraction { get; init; }

    /// <summary>
    /// Gets or initializes the first fractional year at which progenitor memory or hormone output drifted below threshold, or <see langword="null"/> if conserved.
    /// </summary>
    [Description("First fractional year where epigenetic washout or sub-therapeutic drift began, or null when conserved.")]
    [JsonPropertyName("washoutOnsetYear")]
    public double? WashoutOnsetYear { get; init; }

    /// <summary>
    /// Gets or initializes the overall multi-year durability verdict.
    /// </summary>
    [Description("Multi-year durability verdict (PermanentHomeostasisVerified or MitoticDriftAdvisory).")]
    [JsonPropertyName("durabilityStatus")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MultiYearDurabilityStatus DurabilityStatus { get; init; } = MultiYearDurabilityStatus.PermanentHomeostasisVerified;

    /// <summary>
    /// Gets or initializes a value indicating whether mitotic drift or handoff failure triggered a clinical advisory alert.
    /// </summary>
    [Description("True when multi-year TTR < 95%, progenitor pool drifts below floor, or handoff trough drop occurs.")]
    [JsonPropertyName("mitoticDriftAlert")]
    public bool MitoticDriftAlert { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether a booster epigenetic LNP dose or promoter reinforcement is recommended.
    /// </summary>
    [Description("True when booster dosing or CRISPRoff/on promoter reinforcement is clinically advised.")]
    [JsonPropertyName("requiresBoosterDose")]
    public bool RequiresBoosterDose { get; init; }

    /// <summary>
    /// Gets or initializes an actionable clinical guidance message summarizing durability findings and recommended action.
    /// </summary>
    [Description("Actionable clinical summary and booster/reinforcement recommendation.")]
    [JsonPropertyName("recommendedIntervention")]
    public string RecommendedIntervention { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the persisted annual checkpoint rows from Year 0 through HorizonYears.
    /// </summary>
    [Description("Year-by-year endocrine and epigenetic memory checkpoints from Year 0 through HorizonYears.")]
    [JsonPropertyName("annualCheckpoints")]
    public IReadOnlyList<MultiYearHomeostasisCheckpointRecord> AnnualCheckpoints { get; init; } = [];

    /// <summary>
    /// Gets or initializes downsampled trajectory points across Day 0–180 handoff and multi-year maintenance.
    /// </summary>
    [Description("Representative trajectory samples covering Day 0-180 handoff and multi-year steady state.")]
    [JsonPropertyName("trajectorySamples")]
    public IReadOnlyList<MultiYearTrajectoryPoint> TrajectorySamples { get; init; } = [];

    /// <summary>
    /// Gets or initializes the UTC timestamp when this forecast run was generated.
    /// </summary>
    [Description("UTC timestamp when the multi-year forecast was generated.")]
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="MultiYearHomeostasisForecastReport"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated multi-year homeostasis forecast report example.</returns>
    public MultiYearHomeostasisForecastReport GetExamples() => new()
    {
        RunId = "myh_20261124_001",
        DepotId = "depot_sample_001",
        CircuitId = "epicirc_20261123_001",
        PatientId = "pat_001",
        Regimen = TherapeuticRegimen.MaleToFemale,
        HorizonYears = 10,
        SimulatedDays = 3650,
        MitoticFidelityRate = 0.9985,
        TransducedProgenitorFraction = 0.85,
        ProjectedTenYearTtrPercent = 99.6,
        HandoffWindowTtrPercent = 100.0,
        HandoffTroughDropDetected = false,
        FinalMethylatedProgenitorFraction = 0.826,
        WashoutOnsetYear = null,
        DurabilityStatus = MultiYearDurabilityStatus.PermanentHomeostasisVerified,
        MitoticDriftAlert = false,
        RequiresBoosterDose = false,
        RecommendedIntervention = "Permanent endogenous homeostasis conserved across 10 years without booster dosing.",
        AnnualCheckpoints = [new MultiYearHomeostasisCheckpointRecord().GetExamples()],
        TrajectorySamples =
        [
            new MultiYearTrajectoryPoint
            {
                Day = 120,
                YearFraction = 0.33,
                EstradiolPmolL = 550.0,
                TestosteroneNmolL = 1.05,
                LhIuL = 0.42,
                FshIuL = 0.78,
                HydrogelContributionFraction = 0.42,
                EndogenousContributionFraction = 0.58,
                MethylatedProgenitorFraction = 0.849,
                InTargetRange = true
            }
        ],
        CreatedAtUtc = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero)
    };
}
