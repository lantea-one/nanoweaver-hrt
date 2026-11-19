using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Persistent database record representing a single longitudinal patient serum hormone assay observation
/// linked to an active semi-annual subcutaneous depot formulation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The relational storage record stored in <c>weaver_depot_assay_observations</c>
/// capturing lab-measured estradiol, testosterone, LH, FSH, SHBG, hematocrit, and encrypted clinical notes.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Provides auditable, durable longitudinal clinical biomarker history across
/// SQLite, PostgreSQL, DuckDB, and MySQL backends for closed-loop depot recalibration.
/// </para>
/// <para>
/// <strong>How it works:</strong> Persisted and queried via <see cref="NanoWeaverHrt.Storage.IDepotAssayRepository"/>
/// with PHI-sensitive notes encrypted at rest using <see cref="NanoWeaverHrt.Security.Authentication.ProfileDataProtector"/>.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "DepotAssayObservationRecord", Description = "Persisted longitudinal patient serum hormone assay observation linked to a depot formulation.")]
public sealed record DepotAssayObservationRecord : IExamplesProvider<DepotAssayObservationRecord>
{
    /// <summary>
    /// Gets or initializes the unique identifier of this serum assay observation.
    /// </summary>
    [Description("Unique identifier of this serum assay observation.")]
    [JsonPropertyName("observationId")]
    public string ObservationId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the parent depot formulation identifier.
    /// </summary>
    [Description("Parent depot formulation identifier.")]
    [JsonPropertyName("depotId")]
    public string DepotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the patient identifier associated with this assay.
    /// </summary>
    [Description("Patient identifier associated with this assay.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the elapsed number of days since depot implantation.
    /// </summary>
    [Description("Elapsed days since depot implantation.")]
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
    /// Gets or initializes the measured serum luteinizing hormone (LH) concentration in IU/L.
    /// </summary>
    [Description("Measured serum luteinizing hormone (LH) concentration in IU/L.")]
    [JsonPropertyName("observedLhIuL")]
    public double ObservedLhIuL { get; init; }

    /// <summary>
    /// Gets or initializes the measured serum follicle-stimulating hormone (FSH) concentration in IU/L.
    /// </summary>
    [Description("Measured serum follicle-stimulating hormone (FSH) concentration in IU/L.")]
    [JsonPropertyName("observedFshIuL")]
    public double ObservedFshIuL { get; init; }

    /// <summary>
    /// Gets or initializes the measured serum sex hormone-binding globulin (SHBG) concentration in nmol/L.
    /// </summary>
    [Description("Measured serum sex hormone-binding globulin (SHBG) concentration in nmol/L.")]
    [JsonPropertyName("observedShbgNmolL")]
    public double ObservedShbgNmolL { get; init; }

    /// <summary>
    /// Gets or initializes the measured blood hematocrit percentage.
    /// </summary>
    [Description("Measured blood hematocrit percentage (%).")]
    [JsonPropertyName("observedHematocritPct")]
    public double ObservedHematocritPct { get; init; }

    /// <summary>
    /// Gets or initializes the UTC timestamp when the observation was recorded.
    /// </summary>
    [Description("UTC timestamp when the assay observation was recorded.")]
    [JsonPropertyName("recordedAtUtc")]
    public DateTimeOffset RecordedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or initializes optional PHI-encrypted clinical notes stored at rest in the database.
    /// </summary>
    [Description("Optional PHI-encrypted clinical notes stored at rest.")]
    [JsonPropertyName("notesEncrypted")]
    public string? NotesEncrypted { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="DepotAssayObservationRecord"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated depot assay observation record.</returns>
    public DepotAssayObservationRecord GetExamples() => new()
    {
        ObservationId = "assay_sample_001",
        DepotId = "depot_sample_001",
        PatientId = "patient_alice",
        DaySinceImplant = 60,
        ObservedE2PmolL = 540.0,
        ObservedTNmolL = 1.1,
        ObservedLhIuL = 0.6,
        ObservedFshIuL = 0.9,
        ObservedShbgNmolL = 78.0,
        ObservedHematocritPct = 41.5,
        RecordedAtUtc = DateTimeOffset.Parse("2026-11-19T12:00:00Z", CultureInfo.InvariantCulture),
        NotesEncrypted = null
    };
}

/// <summary>
/// Persistent database record representing a computed longitudinal Bayesian calibration snapshot
/// for an active subcutaneous time-release depot.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The relational storage record stored in <c>weaver_depot_calibration_snapshots</c>
/// capturing inferred metabolic clearance, hydrogel erosion rate, adjusted re-dosing day, recommended next-cycle mass,
/// safety classification, and fit residual RMSE.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Retains an auditable snapshot of every adaptive depot recalibration so clinicians
/// and patients can track kinetic drift across consecutive lab visits without recomputing from scratch.
/// </para>
/// <para>
/// <strong>How it works:</strong> Persisted and queried via <see cref="NanoWeaverHrt.Storage.IDepotAssayRepository"/>.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "DepotCalibrationSnapshotRecord", Description = "Persisted Bayesian longitudinal calibration snapshot for a depot formulation.")]
public sealed record DepotCalibrationSnapshotRecord : IExamplesProvider<DepotCalibrationSnapshotRecord>
{
    /// <summary>
    /// Gets or initializes the unique identifier of this calibration snapshot.
    /// </summary>
    [Description("Unique identifier of this calibration snapshot.")]
    [JsonPropertyName("snapshotId")]
    public string SnapshotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the parent depot formulation identifier.
    /// </summary>
    [Description("Parent depot formulation identifier.")]
    [JsonPropertyName("depotId")]
    public string DepotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the patient identifier associated with this calibration snapshot.
    /// </summary>
    [Description("Patient identifier associated with this calibration snapshot.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the inferred patient-specific metabolic clearance multiplier.
    /// </summary>
    [Description("Inferred in-vivo metabolic clearance rate multiplier.")]
    [JsonPropertyName("inferredClearanceMultiplier")]
    public double InferredClearanceMultiplier { get; init; } = 1.0;

    /// <summary>
    /// Gets or initializes the inferred daily polymer erosion rate percentage per day.
    /// </summary>
    [Description("Inferred in-vivo daily hydrogel erosion rate in percent per day.")]
    [JsonPropertyName("inferredDailyErosionRatePercent")]
    public double InferredDailyErosionRatePercent { get; init; } = 0.55;

    /// <summary>
    /// Gets or initializes the adjusted re-dosing day derived from the recalibrated trajectory.
    /// </summary>
    [Description("Adjusted clinical re-dosing day derived from the recalibrated trajectory.")]
    [JsonPropertyName("adjustedRedoseDay")]
    public int AdjustedRedoseDay { get; init; } = 180;

    /// <summary>
    /// Gets or initializes the recommended initial payload mass in milligrams for the next 180-day depot cycle.
    /// </summary>
    [Description("Recommended initial nanocarrier payload mass in milligrams for the next 180-day cycle.")]
    [JsonPropertyName("recommendedNextMassMg")]
    public double RecommendedNextMassMg { get; init; } = 18.0;

    /// <summary>
    /// Gets or initializes the clinical safety status assigned at calibration time.
    /// </summary>
    [Description("Clinical safety status assigned at calibration time.")]
    [JsonPropertyName("safetyStatus")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DepotSafetyStatus SafetyStatus { get; init; } = DepotSafetyStatus.Optimal;

    /// <summary>
    /// Gets or initializes the normalized root-mean-square residual error of the calibrated fit.
    /// </summary>
    [Description("Normalized root-mean-square residual error of the calibrated pharmacokinetic fit.")]
    [JsonPropertyName("residualRmse")]
    public double ResidualRmse { get; init; }

    /// <summary>
    /// Gets or initializes the UTC timestamp when the calibration snapshot was computed.
    /// </summary>
    [Description("UTC timestamp when the calibration snapshot was computed.")]
    [JsonPropertyName("computedAtUtc")]
    public DateTimeOffset ComputedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Generates a representative example instance of <see cref="DepotCalibrationSnapshotRecord"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated depot calibration snapshot record.</returns>
    public DepotCalibrationSnapshotRecord GetExamples() => new()
    {
        SnapshotId = "calib_sample_001",
        DepotId = "depot_sample_001",
        PatientId = "patient_alice",
        InferredClearanceMultiplier = 1.08,
        InferredDailyErosionRatePercent = 0.58,
        AdjustedRedoseDay = 164,
        RecommendedNextMassMg = 19.4,
        SafetyStatus = DepotSafetyStatus.Optimal,
        ResidualRmse = 0.042,
        ComputedAtUtc = DateTimeOffset.Parse("2026-11-19T12:05:00Z", CultureInfo.InvariantCulture)
    };
}
