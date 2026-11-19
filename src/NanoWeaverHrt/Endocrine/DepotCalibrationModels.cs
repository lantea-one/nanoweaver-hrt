using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Endocrine;

/// <summary>
/// Clinical safety classification assigned after evaluating longitudinal patient serum hormone assays
/// against therapeutic endocrine guardrails.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Summarizes whether a patient's measured blood hormone levels are
/// safely on target (<see cref="Optimal"/>), drifting slightly below target (<see cref="AdvisoryDrift"/>),
/// escaping pituitary suppression or approaching clinical upper limits (<see cref="WarningEscape"/>),
/// or exceeding an immediate clinical safety ceiling such as severe erythrocytosis or thrombotic estradiol
/// exposure (<see cref="CriticalSafety"/>).
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DepotSafetyStatus
{
    /// <summary>
    /// All measured hormone concentrations and safety biomarkers are within physiological therapeutic bounds.
    /// </summary>
    Optimal,

    /// <summary>
    /// Primary hormone concentration has drifted below the therapeutic trough threshold; earlier re-dosing or
    /// next-cycle mass adjustment is advised.
    /// </summary>
    AdvisoryDrift,

    /// <summary>
    /// Pituitary gonadotropin escape (elevated LH/FSH) or upper-bound warning biomarker elevation detected.
    /// </summary>
    WarningEscape,

    /// <summary>
    /// Critical safety ceiling breached (e.g. FtM hematocrit &gt;= 54.0% or MtF estradiol &gt;= 1500.0 pmol/L),
    /// requiring immediate clinical review and dose reduction.
    /// </summary>
    CriticalSafety
}

/// <summary>
/// Represents a single longitudinal clinical serum assay measurement taken after depot implantation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Captures blood-draw hormone measurements (estradiol, testosterone,
/// LH, FSH, SHBG, and hematocrit) recorded on a specific day following subcutaneous depot placement.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "DepotAssayObservation", Description = "A single longitudinal serum hormone assay observation taken after depot implantation.")]
public sealed record DepotAssayObservation : IExamplesProvider<DepotAssayObservation>
{
    /// <summary>
    /// Gets or initializes the elapsed number of days since depot implantation (1 to 365).
    /// </summary>
    [Description("Elapsed days since depot implantation (1 to 365).")]
    [JsonPropertyName("daySinceImplant")]
    public int DaySinceImplant { get; init; }

    /// <summary>
    /// Gets or initializes the measured serum 17β-estradiol concentration in pmol/L.
    /// </summary>
    [Description("Measured serum 17β-estradiol concentration in pmol/L.")]
    [JsonPropertyName("observedE2PmolL")]
    public double ObservedE2PmolL { get; init; }

    /// <summary>
    /// Gets or initializes the measured serum total testosterone concentration in nmol/L.
    /// </summary>
    [Description("Measured serum total testosterone concentration in nmol/L.")]
    [JsonPropertyName("observedTNmolL")]
    public double ObservedTNmolL { get; init; }

    /// <summary>
    /// Gets or initializes the measured circulating luteinizing hormone (LH) concentration in IU/L.
    /// </summary>
    [Description("Measured circulating luteinizing hormone (LH) concentration in IU/L.")]
    [JsonPropertyName("observedLhIuL")]
    public double ObservedLhIuL { get; init; }

    /// <summary>
    /// Gets or initializes the measured circulating follicle-stimulating hormone (FSH) concentration in IU/L.
    /// </summary>
    [Description("Measured circulating follicle-stimulating hormone (FSH) concentration in IU/L.")]
    [JsonPropertyName("observedFshIuL")]
    public double ObservedFshIuL { get; init; }

    /// <summary>
    /// Gets or initializes the measured serum sex hormone-binding globulin (SHBG) concentration in nmol/L.
    /// </summary>
    [Description("Measured serum sex hormone-binding globulin (SHBG) concentration in nmol/L.")]
    [JsonPropertyName("observedShbgNmolL")]
    public double ObservedShbgNmolL { get; init; }

    /// <summary>
    /// Gets or initializes the measured blood hematocrit percentage (0 to 100%).
    /// </summary>
    [Description("Measured blood hematocrit percentage (%).")]
    [JsonPropertyName("observedHematocritPct")]
    public double ObservedHematocritPct { get; init; }

    /// <summary>
    /// Gets or initializes the UTC timestamp when the assay was recorded.
    /// </summary>
    [Description("UTC timestamp when the serum assay was recorded.")]
    [JsonPropertyName("recordedAtUtc")]
    public DateTimeOffset RecordedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Generates a representative example instance of <see cref="DepotAssayObservation"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated serum assay observation.</returns>
    public DepotAssayObservation GetExamples() => new()
    {
        DaySinceImplant = 60,
        ObservedE2PmolL = 540.0,
        ObservedTNmolL = 1.1,
        ObservedLhIuL = 0.6,
        ObservedFshIuL = 0.9,
        ObservedShbgNmolL = 78.0,
        ObservedHematocritPct = 41.5,
        RecordedAtUtc = DateTimeOffset.Parse("2026-11-19T12:00:00Z", CultureInfo.InvariantCulture)
    };
}

/// <summary>
/// Clinical safety alert raised when a longitudinal serum assay breaches a physiological guardrail threshold.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Flags specific hormone or hematology findings (for example,
/// rising hematocrit on testosterone therapy or supraphysiological estradiol) with the measured value and threshold.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "DepotSafetyAlert", Description = "Clinical safety guardrail alert triggered during longitudinal assay evaluation.")]
public sealed record DepotSafetyAlert : IExamplesProvider<DepotSafetyAlert>
{
    /// <summary>
    /// Gets or initializes the machine-readable clinical alert code (e.g. <c>HPG_LH_ESCAPE</c>, <c>FTM_HEMATOCRIT_CRITICAL</c>).
    /// </summary>
    [Description("Machine-readable clinical guardrail alert code.")]
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the severity tier of this safety alert.
    /// </summary>
    [Description("Severity classification of this safety alert.")]
    [JsonPropertyName("severity")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DepotSafetyStatus Severity { get; init; } = DepotSafetyStatus.Optimal;

    /// <summary>
    /// Gets or initializes a plain-English explanation of the clinical finding and recommended action.
    /// </summary>
    [Description("Plain-English clinical description of the safety alert.")]
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the observed laboratory biomarker value that triggered the alert.
    /// </summary>
    [Description("Observed biomarker value that triggered the alert.")]
    [JsonPropertyName("measuredValue")]
    public double MeasuredValue { get; init; }

    /// <summary>
    /// Gets or initializes the clinical guardrail threshold value that was crossed.
    /// </summary>
    [Description("Clinical guardrail threshold value crossed.")]
    [JsonPropertyName("thresholdValue")]
    public double ThresholdValue { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="DepotSafetyAlert"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated depot safety alert.</returns>
    public DepotSafetyAlert GetExamples() => new()
    {
        Code = "MTF_E2_SUBTHERAPEUTIC_TROUGH",
        Severity = DepotSafetyStatus.AdvisoryDrift,
        Message = "Measured estradiol (320.0 pmol/L) fell below the 350.0 pmol/L therapeutic trough floor.",
        MeasuredValue = 320.0,
        ThresholdValue = 350.0
    };
}

/// <summary>
/// Input request for Bayesian weighted least-squares calibration of an active subcutaneous depot formulation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Bundles the depot's initial theoretical design parameters with one
/// or more real-world blood hormone measurements so the calibration engine can infer the patient's true in-vivo
/// clearance and polymer erosion kinetics.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "DepotCalibrationRequest", Description = "Input request for longitudinal Bayesian calibration of an active depot formulation.")]
public sealed record DepotCalibrationRequest : IExamplesProvider<DepotCalibrationRequest>
{
    /// <summary>
    /// Gets or initializes the unique identifier of the depot being calibrated.
    /// </summary>
    [Description("Unique identifier of the depot formulation being calibrated.")]
    [JsonPropertyName("depotId")]
    public string DepotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the patient identifier associated with this depot.
    /// </summary>
    [Description("Patient identifier associated with this depot.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the baseline theoretical depot parameters prior to assay assimilation.
    /// </summary>
    [Description("Baseline theoretical depot simulation parameters prior to assay assimilation.")]
    [JsonPropertyName("baselineParameters")]
    public TimeReleaseDepotParameters BaselineParameters { get; init; } = new();

    /// <summary>
    /// Gets or initializes the chronological collection of longitudinal serum assay observations.
    /// </summary>
    [Description("Collection of longitudinal serum assay observations.")]
    [JsonPropertyName("observations")]
    public IReadOnlyList<DepotAssayObservation> Observations { get; init; } = [];

    /// <summary>
    /// Generates a representative example instance of <see cref="DepotCalibrationRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated depot calibration request.</returns>
    public DepotCalibrationRequest GetExamples() => new()
    {
        DepotId = "depot_sample_001",
        PatientId = "patient_alice",
        BaselineParameters = new TimeReleaseDepotParameters().GetExamples(),
        Observations = [new DepotAssayObservation().GetExamples()]
    };
}

/// <summary>
/// Comprehensive output from longitudinal Bayesian depot calibration and adaptive re-dosing analysis.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Reports the patient's inferred metabolic clearance multiplier and
/// in-vivo hydrogel erosion rate, goodness-of-fit RMSE, adjusted re-dosing day, recommended payload mass for
/// the next 180-day cycle, active clinical safety alerts, and the updated daily hormone trajectory.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "DepotCalibrationResult", Description = "Result of longitudinal Bayesian depot calibration and adaptive re-dosing calculation.")]
public sealed record DepotCalibrationResult : IExamplesProvider<DepotCalibrationResult>
{
    /// <summary>
    /// Gets or initializes the calibrated depot formulation identifier.
    /// </summary>
    [Description("Unique identifier of the calibrated depot formulation.")]
    [JsonPropertyName("depotId")]
    public string DepotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the inferred patient-specific metabolic clearance multiplier (0.40 to 2.50).
    /// </summary>
    [Description("Inferred in-vivo metabolic clearance rate multiplier (range 0.40 to 2.50).")]
    [JsonPropertyName("inferredClearanceMultiplier")]
    public double InferredClearanceMultiplier { get; init; } = 1.0;

    /// <summary>
    /// Gets or initializes the inferred daily polymer hydrogel erosion rate in percent per day (0.20 to 2.00).
    /// </summary>
    [Description("Inferred in-vivo daily polymer erosion rate in percent per day (range 0.20 to 2.00).")]
    [JsonPropertyName("inferredDailyErosionRatePercent")]
    public double InferredDailyErosionRatePercent { get; init; } = 0.55;

    /// <summary>
    /// Gets or initializes the normalized root-mean-square residual error between observed and recalibrated hormone values.
    /// </summary>
    [Description("Root-mean-square error between measured assay concentrations and recalibrated model predictions.")]
    [JsonPropertyName("residualRmse")]
    public double ResidualRmse { get; init; }

    /// <summary>
    /// Gets or initializes the adjusted re-dosing day when reserve drops below 5% or primary hormone drops below floor.
    /// </summary>
    [Description("Adjusted clinical re-dosing day based on the recalibrated pharmacokinetic trajectory.")]
    [JsonPropertyName("adjustedRedoseDay")]
    public int AdjustedRedoseDay { get; init; } = 180;

    /// <summary>
    /// Gets or initializes the recommended initial nanocarrier payload mass in milligrams for the next 180-day depot cycle.
    /// </summary>
    [Description("Recommended initial nanocarrier payload mass in milligrams (mg) for the next 180-day cycle.")]
    [JsonPropertyName("recommendedNextMassMg")]
    public double RecommendedNextMassMg { get; init; } = 18.0;

    /// <summary>
    /// Gets or initializes the recommended target daily erosion rate percentage for the next 180-day depot fabrication.
    /// </summary>
    [Description("Recommended target daily hydrogel erosion rate in percent per day for the next cycle.")]
    [JsonPropertyName("recommendedNextErosionRatePercent")]
    public double RecommendedNextErosionRatePercent { get; init; } = 0.55;

    /// <summary>
    /// Gets or initializes the highest severity clinical safety classification across all evaluated guardrails.
    /// </summary>
    [Description("Overall clinical safety status across all evaluated longitudinal assay guardrails.")]
    [JsonPropertyName("overallSafetyStatus")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DepotSafetyStatus OverallSafetyStatus { get; init; } = DepotSafetyStatus.Optimal;

    /// <summary>
    /// Gets or initializes the list of clinical safety alerts triggered by the observed serum assays.
    /// </summary>
    [Description("Clinical safety guardrail alerts triggered during assay evaluation.")]
    [JsonPropertyName("alerts")]
    public IReadOnlyList<DepotSafetyAlert> Alerts { get; init; } = [];

    /// <summary>
    /// Gets or initializes the full recalibrated daily pharmacokinetic and endocrine simulation trajectory.
    /// </summary>
    [Description("Recalibrated daily pharmacokinetic and coupled endocrine simulation trajectory.")]
    [JsonPropertyName("recalibratedTrajectory")]
    public TimeReleaseSimulationResult RecalibratedTrajectory { get; init; } = new();

    /// <summary>
    /// Generates a representative example instance of <see cref="DepotCalibrationResult"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated depot calibration result.</returns>
    public DepotCalibrationResult GetExamples() => new()
    {
        DepotId = "depot_sample_001",
        InferredClearanceMultiplier = 1.08,
        InferredDailyErosionRatePercent = 0.58,
        ResidualRmse = 0.042,
        AdjustedRedoseDay = 164,
        RecommendedNextMassMg = 19.4,
        RecommendedNextErosionRatePercent = 0.55,
        OverallSafetyStatus = DepotSafetyStatus.Optimal,
        Alerts = [],
        RecalibratedTrajectory = new TimeReleaseSimulationResult().GetExamples()
    };
}
