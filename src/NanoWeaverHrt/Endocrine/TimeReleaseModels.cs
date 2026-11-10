using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Endocrine;

/// <summary>
/// Biocompatible polymeric matrix materials engineered for sustained subcutaneous nanocarrier encapsulation and release.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Defines the biomaterial scaffold surrounding the DNA origami nanocarriers.
/// Each matrix type has different physical pore sizes, degradation pathways, and water-swelling rates that dictate
/// how rapidly hormones elute into surrounding tissue over months.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PolymerMatrixType
{
    /// <summary>
    /// Poly(lactic-co-glycolic acid)-poly(ethylene glycol)-poly(lactic-co-glycolic acid) triblock copolymer hydrogel.
    /// Forms an in situ depot upon subcutaneous injection at body temperature (37°C) with hydrolytic polyester degradation.
    /// </summary>
    PlgaPegPlga,

    /// <summary>
    /// Ionically crosslinked polyelectrolyte complex of anionic sodium alginate and cationic chitosan oligosaccharides.
    /// Exhibits controlled swelling relaxation and matrix dissolution in physiological saline.
    /// </summary>
    AlginateChitosan,

    /// <summary>
    /// Porous polymeric scaffold functionalized with supramolecular cyclodextrin cavities.
    /// Provides hydrophobic inclusion complexation for steady, guest-displacement zero-order hormone release.
    /// </summary>
    CyclodextrinScaffold,

    /// <summary>
    /// Covalently crosslinked biocompatible polyethylene glycol/methacrylate hydrogel network.
    /// Provides uniform mesh pore diffusion and steady anomalous solute transport.
    /// </summary>
    CrosslinkedHydrogel
}

/// <summary>
/// Mathematical biophysical release kinetics models governing payload elution from depot matrices.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Specifies the mathematical equation used to predict how drug molecules
/// escape from the polymer depot into the bloodstream over time (e.g. diffusion through pores vs. matrix erosion).
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReleaseKineticsModel
{
    /// <summary>
    /// Power-law anomalous transport model (Mt / Minf = k * t^n).
    /// Captures coupled Fickian diffusion and polymer chain relaxation when 0.45 &lt; n &lt; 0.89.
    /// </summary>
    KorsmeyerPeppas,

    /// <summary>
    /// Classical square-root-of-time planar diffusion model (Mt / Minf = k * t^0.5).
    /// Models pure Fickian concentration gradient diffusion from non-eroding matrix depots.
    /// </summary>
    Higuchi,

    /// <summary>
    /// Dual-mechanism model combining early Fickian boundary layer diffusion with continuous polymer matrix hydrolytic erosion.
    /// </summary>
    CoupledAnomalousFickian,

    /// <summary>
    /// Pure zero-order hydrolytic surface erosion kinetics delivering constant daily micro-aliquots (Mt / Minf = k0 * t).
    /// </summary>
    ZeroOrderErosion
}

/// <summary>
/// Request parameters for semi-annual (180-day) time-release depot simulation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Specifies formulation settings including the polymer matrix type,
/// loaded hormone mass, therapeutic regimen, and pharmacogenomic clearance scaling for a 180-day depot simulation.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "TimeReleaseDepotParameters", Description = "Request parameters for semi-annual (180-day) time-release depot simulation.")]
public sealed record TimeReleaseDepotParameters : IExamplesProvider<TimeReleaseDepotParameters>
{
    /// <summary>
    /// Target therapeutic endocrine regimen (MaleToFemale, FemaleToMale, or Custom).
    /// </summary>
    [Description("Target therapeutic endocrine regimen (MaleToFemale, FemaleToMale, or Custom).")]
    [JsonPropertyName("regimen")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TherapeuticRegimen Regimen { get; init; } = TherapeuticRegimen.MaleToFemale;

    /// <summary>
    /// Biocompatible polymer matrix composition.
    /// </summary>
    [Description("Biocompatible polymer matrix composition.")]
    [JsonPropertyName("matrixType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PolymerMatrixType MatrixType { get; init; } = PolymerMatrixType.PlgaPegPlga;

    /// <summary>
    /// Mathematical release kinetics model.
    /// </summary>
    [Description("Mathematical release kinetics model.")]
    [JsonPropertyName("kineticsModel")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ReleaseKineticsModel KineticsModel { get; init; } = ReleaseKineticsModel.KorsmeyerPeppas;

    /// <summary>
    /// Initial nanocarrier payload mass loaded into the depot in milligrams (mg).
    /// </summary>
    [Description("Initial nanocarrier payload mass loaded into the depot in milligrams (mg).")]
    [JsonPropertyName("initialPayloadMassMg")]
    public double InitialPayloadMassMg { get; init; } = 18.0;

    /// <summary>
    /// Targeted therapeutic time horizon in days (default 180 days).
    /// </summary>
    [Description("Targeted therapeutic time horizon in days (default 180 days).")]
    [JsonPropertyName("targetHorizonDays")]
    public int TargetHorizonDays { get; init; } = 180;

    /// <summary>
    /// Patient-specific metabolic clearance rate multiplier (range 0.1 to 5.0).
    /// </summary>
    [Description("Patient-specific metabolic clearance rate multiplier (range 0.1 to 5.0).")]
    [JsonPropertyName("clearanceMultiplier")]
    public double ClearanceMultiplier { get; init; } = 1.0;

    /// <summary>
    /// Initial baseline serum 17β-estradiol concentration in pmol/L.
    /// </summary>
    [Description("Initial baseline serum 17β-estradiol concentration in pmol/L.")]
    [JsonPropertyName("initialEstradiolPmolL")]
    public double InitialEstradiolPmolL { get; init; } = 120.0;

    /// <summary>
    /// Initial baseline serum total testosterone concentration in nmol/L.
    /// </summary>
    [Description("Initial baseline serum total testosterone concentration in nmol/L.")]
    [JsonPropertyName("initialTestosteroneNmolL")]
    public double InitialTestosteroneNmolL { get; init; } = 18.0;

    /// <summary>
    /// Initial circulating luteinizing hormone (LH) concentration in IU/L.
    /// </summary>
    [Description("Initial circulating luteinizing hormone (LH) concentration in IU/L.")]
    [JsonPropertyName("initialLhIuL")]
    public double InitialLhIuL { get; init; } = 6.5;

    /// <summary>
    /// Initial circulating follicle-stimulating hormone (FSH) concentration in IU/L.
    /// </summary>
    [Description("Initial circulating follicle-stimulating hormone (FSH) concentration in IU/L.")]
    [JsonPropertyName("initialFshIuL")]
    public double InitialFshIuL { get; init; } = 5.0;

    /// <summary>
    /// Target 17β-estradiol setpoint concentration in pmol/L.
    /// </summary>
    [Description("Target 17β-estradiol setpoint concentration in pmol/L.")]
    [JsonPropertyName("targetEstradiolPmolL")]
    public double TargetEstradiolPmolL { get; init; } = 550.0;

    /// <summary>
    /// Target total testosterone setpoint concentration in nmol/L.
    /// </summary>
    [Description("Target total testosterone setpoint concentration in nmol/L.")]
    [JsonPropertyName("targetTestosteroneNmolL")]
    public double TargetTestosteroneNmolL { get; init; } = 1.2;

    /// <summary>
    /// Generates a representative example instance of <see cref="TimeReleaseDepotParameters"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated example parameters record.</returns>
    public TimeReleaseDepotParameters GetExamples() => new();
}

/// <summary>
/// Discrete daily trajectory sample point from a time-release depot simulation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Records drug elution fraction, daily delivered milligrams, remaining payload,
/// serum hormone concentrations, and clinical target flags for a single 24-hour day.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "TimeReleaseDailyPoint", Description = "Discrete daily trajectory sample point from a time-release depot simulation.")]
public sealed record TimeReleaseDailyPoint
{
    /// <summary>
    /// Elapsed simulation timepoint in days (0 to 180).
    /// </summary>
    [Description("Elapsed simulation timepoint in days (0 to 180).")]
    [JsonPropertyName("day")]
    public int Day { get; init; }

    /// <summary>
    /// Cumulative fraction of initial nanocarrier payload released (0.0 to 1.0).
    /// </summary>
    [Description("Cumulative fraction of initial nanocarrier payload released (0.0 to 1.0).")]
    [JsonPropertyName("cumulativeFraction")]
    public double CumulativeFraction { get; init; }

    /// <summary>
    /// Instantaneous daily payload released on this day in milligrams (mg/day).
    /// </summary>
    [Description("Instantaneous daily payload released on this day in milligrams (mg/day).")]
    [JsonPropertyName("dailyReleaseMg")]
    public double DailyReleaseMg { get; init; }

    /// <summary>
    /// Remaining unreleased payload mass in depot matrix in milligrams (mg).
    /// </summary>
    [Description("Remaining unreleased payload mass in depot matrix in milligrams (mg).")]
    [JsonPropertyName("remainingReserveMg")]
    public double RemainingReserveMg { get; init; }

    /// <summary>
    /// Circulating serum 17β-estradiol concentration in pmol/L.
    /// </summary>
    [Description("Circulating serum 17β-estradiol concentration in pmol/L.")]
    [JsonPropertyName("estradiolPmolL")]
    public double EstradiolPmolL { get; init; }

    /// <summary>
    /// Circulating serum total testosterone concentration in nmol/L.
    /// </summary>
    [Description("Circulating serum total testosterone concentration in nmol/L.")]
    [JsonPropertyName("testosteroneNmolL")]
    public double TestosteroneNmolL { get; init; }

    /// <summary>
    /// Circulating serum luteinizing hormone (LH) concentration in IU/L.
    /// </summary>
    [Description("Circulating serum luteinizing hormone (LH) concentration in IU/L.")]
    [JsonPropertyName("lhIuL")]
    public double LhIuL { get; init; }

    /// <summary>
    /// Circulating serum follicle-stimulating hormone (FSH) concentration in IU/L.
    /// </summary>
    [Description("Circulating serum follicle-stimulating hormone (FSH) concentration in IU/L.")]
    [JsonPropertyName("fshIuL")]
    public double FshIuL { get; init; }

    /// <summary>
    /// Indicates whether the primary therapeutic hormone is within clinical target range.
    /// </summary>
    [Description("Indicates whether the primary therapeutic hormone is within clinical target range.")]
    [JsonPropertyName("inTargetRange")]
    public bool InTargetRange { get; init; }

    /// <summary>
    /// Release phase label (Phase I: Burst Suppression, Phase II: Steady-State Plateau, or Phase III: Terminal Taper).
    /// </summary>
    [Description("Release phase label (Phase I: Burst Suppression, Phase II: Steady-State Plateau, or Phase III: Terminal Taper).")]
    [JsonPropertyName("phase")]
    public string Phase { get; init; } = string.Empty;

    /// <summary>
    /// Indicates whether payload reserve has depleted to trigger a clinical re-dosing alert.
    /// </summary>
    [Description("Indicates whether payload reserve has depleted to trigger a clinical re-dosing alert.")]
    [JsonPropertyName("reDosingAlert")]
    public bool ReDosingAlert { get; init; }
}

/// <summary>
/// Comprehensive simulation result for a 180-day semi-annual time-release depot.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Summarizes the 180-day simulation outcome, verifying whether the initial burst
/// was suppressed (&lt;= 5.0%), daily steady-state micro-dosing was sustained, time in target range (TTR &gt;= 95.0%) was achieved,
/// and pituitary gonadotropins (LH/FSH) remained suppressed.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "TimeReleaseSimulationResult", Description = "Comprehensive simulation result for a 180-day semi-annual time-release depot.")]
public sealed record TimeReleaseSimulationResult : IExamplesProvider<TimeReleaseSimulationResult>
{
    /// <summary>
    /// Chronological daily trajectory points across the simulated horizon.
    /// </summary>
    [Description("Chronological daily trajectory points across the simulated horizon.")]
    [JsonPropertyName("points")]
    public IReadOnlyList<TimeReleaseDailyPoint> Points { get; init; } = [];

    /// <summary>
    /// Cumulative payload fraction released at Day 7 (strictly &lt;= 0.05).
    /// </summary>
    [Description("Cumulative payload fraction released at Day 7 (strictly <= 0.05).")]
    [JsonPropertyName("day7BurstFraction")]
    public double Day7BurstFraction { get; init; }

    /// <summary>
    /// Indicates whether Phase I burst release was successfully suppressed (&lt;= 5.0%).
    /// </summary>
    [Description("Indicates whether Phase I burst release was successfully suppressed (<= 5.0%).")]
    [JsonPropertyName("burstSuppressed")]
    public bool BurstSuppressed { get; init; }

    /// <summary>
    /// Average daily payload release during Phase II steady-state plateau in mg/day.
    /// </summary>
    [Description("Average daily payload release during Phase II steady-state plateau in mg/day.")]
    [JsonPropertyName("averageDailyReleaseMg")]
    public double AverageDailyReleaseMg { get; init; }

    /// <summary>
    /// Percentage of days where target hormone was within the clinical physiological range (target &gt;= 95.0%).
    /// </summary>
    [Description("Percentage of days where target hormone was within the clinical physiological range (target >= 95.0%).")]
    [JsonPropertyName("timeInTargetRangePercent")]
    public double TimeInTargetRangePercent { get; init; }

    /// <summary>
    /// Remaining unreleased fraction of payload mass at the end of the simulation horizon.
    /// </summary>
    [Description("Remaining unreleased fraction of payload mass at the end of the simulation horizon.")]
    [JsonPropertyName("terminalReserveFraction")]
    public double TerminalReserveFraction { get; init; }

    /// <summary>
    /// First simulation day on which the re-dosing alert was triggered, or -1 if never triggered.
    /// </summary>
    [Description("First simulation day on which the re-dosing alert was triggered, or -1 if never triggered.")]
    [JsonPropertyName("reDosingAlertDay")]
    public int ReDosingAlertDay { get; init; } = -1;

    /// <summary>
    /// Indicates whether steady-state luteinizing hormone (LH) was suppressed below 1.0 IU/L.
    /// </summary>
    [Description("Indicates whether steady-state luteinizing hormone (LH) was suppressed below 1.0 IU/L.")]
    [JsonPropertyName("lhSuppressionAchieved")]
    public bool LhSuppressionAchieved { get; init; }

    /// <summary>
    /// Indicates whether steady-state follicle-stimulating hormone (FSH) was suppressed below 1.5 IU/L.
    /// </summary>
    [Description("Indicates whether steady-state follicle-stimulating hormone (FSH) was suppressed below 1.5 IU/L.")]
    [JsonPropertyName("fshSuppressionAchieved")]
    public bool FshSuppressionAchieved { get; init; }

    /// <summary>
    /// Final circulating 17β-estradiol concentration in pmol/L at the end of the simulation.
    /// </summary>
    [Description("Final circulating 17β-estradiol concentration in pmol/L at the end of the simulation.")]
    [JsonPropertyName("finalEstradiolPmolL")]
    public double FinalEstradiolPmolL { get; init; }

    /// <summary>
    /// Final circulating total testosterone concentration in nmol/L at the end of the simulation.
    /// </summary>
    [Description("Final circulating total testosterone concentration in nmol/L.")]
    [JsonPropertyName("finalTestosteroneNmolL")]
    public double FinalTestosteroneNmolL { get; init; }

    /// <summary>
    /// Final circulating luteinizing hormone (LH) concentration in IU/L at the end of the simulation.
    /// </summary>
    [Description("Final circulating luteinizing hormone (LH) concentration in IU/L.")]
    [JsonPropertyName("finalLhIuL")]
    public double FinalLhIuL { get; init; }

    /// <summary>
    /// Final circulating follicle-stimulating hormone (FSH) concentration in IU/L at the end of the simulation.
    /// </summary>
    [Description("Final circulating follicle-stimulating hormone (FSH) concentration in IU/L.")]
    [JsonPropertyName("finalFshIuL")]
    public double FinalFshIuL { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="TimeReleaseSimulationResult"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated example simulation result record.</returns>
    public TimeReleaseSimulationResult GetExamples() => new()
    {
        Points =
        [
            new TimeReleaseDailyPoint
            {
                Day = 0,
                CumulativeFraction = 0.0,
                DailyReleaseMg = 0.0,
                RemainingReserveMg = 18.0,
                EstradiolPmolL = 120.0,
                TestosteroneNmolL = 18.0,
                LhIuL = 6.5,
                FshIuL = 5.0,
                InTargetRange = false,
                Phase = "Phase I: Burst Suppression",
                ReDosingAlert = false
            },
            new TimeReleaseDailyPoint
            {
                Day = 180,
                CumulativeFraction = 0.952,
                DailyReleaseMg = 0.021,
                RemainingReserveMg = 0.86,
                EstradiolPmolL = 485.2,
                TestosteroneNmolL = 1.15,
                LhIuL = 0.72,
                FshIuL = 1.12,
                InTargetRange = true,
                Phase = "Phase III: Terminal Taper",
                ReDosingAlert = true
            }
        ],
        Day7BurstFraction = 0.038,
        BurstSuppressed = true,
        AverageDailyReleaseMg = 0.082,
        TimeInTargetRangePercent = 96.1,
        TerminalReserveFraction = 0.048,
        ReDosingAlertDay = 151,
        LhSuppressionAchieved = true,
        FshSuppressionAchieved = true,
        FinalEstradiolPmolL = 485.2,
        FinalTestosteroneNmolL = 1.15,
        FinalLhIuL = 0.72,
        FinalFshIuL = 1.12
    };
}
