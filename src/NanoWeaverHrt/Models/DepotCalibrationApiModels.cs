using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Request payload for recording a longitudinal clinical serum hormone assay observation against a depot formulation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Submitted when a patient's blood test results arrive after depot
/// implantation. Optional clinical notes are encrypted at rest before database storage.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "RecordDepotAssayRequest", Description = "Request payload for recording a longitudinal serum hormone assay observation against a depot formulation.")]
public sealed record RecordDepotAssayRequest : IExamplesProvider<RecordDepotAssayRequest>
{
    /// <summary>
    /// Gets or initializes the elapsed number of days since depot implantation (1 to 365).
    /// </summary>
    [Description("Elapsed days since depot implantation (1 to 365).")]
    [JsonPropertyName("daySinceImplant")]
    public int DaySinceImplant { get; init; } = 30;

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
    [Description("Measured blood hematocrit percentage (0 to 100%).")]
    [JsonPropertyName("observedHematocritPct")]
    public double ObservedHematocritPct { get; init; }

    /// <summary>
    /// Gets or initializes optional plaintext clinical notes encrypted at rest using the node PHI master key.
    /// </summary>
    [Description("Optional clinical notes (encrypted at rest using AES-256-GCM before storage).")]
    [JsonPropertyName("clinicalNotes")]
    public string? ClinicalNotes { get; init; }

    /// <summary>
    /// Gets or initializes an optional UTC timestamp for when the blood draw was recorded.
    /// </summary>
    [Description("Optional UTC timestamp when the serum assay was recorded; defaults to current UTC time.")]
    [JsonPropertyName("recordedAtUtc")]
    public DateTimeOffset? RecordedAtUtc { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="RecordDepotAssayRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated depot assay recording request.</returns>
    public RecordDepotAssayRequest GetExamples() => new()
    {
        DaySinceImplant = 60,
        ObservedE2PmolL = 540.0,
        ObservedTNmolL = 1.1,
        ObservedLhIuL = 0.6,
        ObservedFshIuL = 0.9,
        ObservedShbgNmolL = 78.0,
        ObservedHematocritPct = 41.5,
        ClinicalNotes = "Fasting morning venipuncture; no injection-site erythema.",
        RecordedAtUtc = DateTimeOffset.Parse("2026-11-19T12:00:00Z", CultureInfo.InvariantCulture)
    };
}

/// <summary>
/// Response payload representing a recorded longitudinal serum hormone assay observation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Returned when listing or recording blood hormone assays for a depot,
/// with clinical notes decrypted only for authorized callers.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "DepotAssayObservationResponse", Description = "Response payload for a recorded longitudinal serum hormone assay observation.")]
public sealed record DepotAssayObservationResponse : IExamplesProvider<DepotAssayObservationResponse>
{
    /// <summary>
    /// Gets or initializes the unique identifier of this assay observation.
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
    /// Gets or initializes the measured blood hematocrit percentage.
    /// </summary>
    [Description("Measured blood hematocrit percentage (%).")]
    [JsonPropertyName("observedHematocritPct")]
    public double ObservedHematocritPct { get; init; }

    /// <summary>
    /// Gets or initializes the UTC timestamp when the assay observation was recorded.
    /// </summary>
    [Description("UTC timestamp when the assay observation was recorded.")]
    [JsonPropertyName("recordedAtUtc")]
    public DateTimeOffset RecordedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or initializes optional decrypted clinical notes for authorized clinical or patient viewers.
    /// </summary>
    [Description("Optional decrypted clinical notes.")]
    [JsonPropertyName("clinicalNotes")]
    public string? ClinicalNotes { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="DepotAssayObservationResponse"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated depot assay observation response.</returns>
    public DepotAssayObservationResponse GetExamples() => new()
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
        ClinicalNotes = "Fasting morning venipuncture; no injection-site erythema."
    };
}

/// <summary>
/// Response payload containing a persisted adaptive Bayesian calibration snapshot, evaluated clinical
/// safety alerts, and the recalibrated 180-day pharmacokinetic trajectory.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Returned by <c>POST /api/depots/{depotId}/calibrate</c> and
/// <c>GET /api/depots/{depotId}/calibrations/latest</c> with complete adaptive re-dosing guidance and curves.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "DepotCalibrationResponse", Description = "Response payload for longitudinal depot calibration and adaptive re-dosing recommendations.")]
public sealed record DepotCalibrationResponse : IExamplesProvider<DepotCalibrationResponse>
{
    /// <summary>
    /// Gets or initializes the unique calibration snapshot identifier.
    /// </summary>
    [Description("Unique identifier of the persisted calibration snapshot.")]
    [JsonPropertyName("snapshotId")]
    public string SnapshotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the parent depot formulation identifier.
    /// </summary>
    [Description("Parent depot formulation identifier.")]
    [JsonPropertyName("depotId")]
    public string DepotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the patient identifier associated with this depot.
    /// </summary>
    [Description("Patient identifier associated with this calibration.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the inferred metabolic clearance multiplier.
    /// </summary>
    [Description("Inferred in-vivo metabolic clearance rate multiplier.")]
    [JsonPropertyName("inferredClearanceMultiplier")]
    public double InferredClearanceMultiplier { get; init; } = 1.0;

    /// <summary>
    /// Gets or initializes the inferred daily polymer erosion rate in percent per day.
    /// </summary>
    [Description("Inferred in-vivo daily hydrogel erosion rate in percent per day.")]
    [JsonPropertyName("inferredDailyErosionRatePercent")]
    public double InferredDailyErosionRatePercent { get; init; } = 0.55;

    /// <summary>
    /// Gets or initializes the normalized root-mean-square residual error.
    /// </summary>
    [Description("Normalized RMSE between observed assays and recalibrated trajectory predictions.")]
    [JsonPropertyName("residualRmse")]
    public double ResidualRmse { get; init; }

    /// <summary>
    /// Gets or initializes the adjusted clinical re-dosing day.
    /// </summary>
    [Description("Adjusted clinical re-dosing day based on the recalibrated pharmacokinetic curve.")]
    [JsonPropertyName("adjustedRedoseDay")]
    public int AdjustedRedoseDay { get; init; } = 180;

    /// <summary>
    /// Gets or initializes the recommended next-cycle payload mass in milligrams.
    /// </summary>
    [Description("Recommended initial nanocarrier payload mass in milligrams for the next 180-day depot cycle.")]
    [JsonPropertyName("recommendedNextMassMg")]
    public double RecommendedNextMassMg { get; init; } = 18.0;

    /// <summary>
    /// Gets or initializes the recommended next-cycle daily erosion rate percentage.
    /// </summary>
    [Description("Recommended target daily hydrogel erosion rate in percent per day for the next cycle.")]
    [JsonPropertyName("recommendedNextErosionRatePercent")]
    public double RecommendedNextErosionRatePercent { get; init; } = 0.55;

    /// <summary>
    /// Gets or initializes the highest clinical safety severity classification.
    /// </summary>
    [Description("Overall clinical safety status across all evaluated guardrails.")]
    [JsonPropertyName("overallSafetyStatus")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DepotSafetyStatus OverallSafetyStatus { get; init; } = DepotSafetyStatus.Optimal;

    /// <summary>
    /// Gets or initializes the active clinical safety alerts evaluated against recorded assays.
    /// </summary>
    [Description("Active clinical safety alerts triggered by recorded serum assays.")]
    [JsonPropertyName("alerts")]
    public IReadOnlyList<DepotSafetyAlert> Alerts { get; init; } = [];

    /// <summary>
    /// Gets or initializes the UTC timestamp when the calibration snapshot was computed.
    /// </summary>
    [Description("UTC timestamp when this calibration snapshot was computed.")]
    [JsonPropertyName("computedAtUtc")]
    public DateTimeOffset ComputedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or initializes the recalibrated daily pharmacokinetic trajectory.
    /// </summary>
    [Description("Recalibrated daily pharmacokinetic and coupled endocrine simulation trajectory.")]
    [JsonPropertyName("recalibratedTrajectory")]
    public TimeReleaseSimulationResult RecalibratedTrajectory { get; init; } = new();

    /// <summary>
    /// Generates a representative example instance of <see cref="DepotCalibrationResponse"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated depot calibration response.</returns>
    public DepotCalibrationResponse GetExamples() => new()
    {
        SnapshotId = "calib_sample_001",
        DepotId = "depot_sample_001",
        PatientId = "patient_alice",
        InferredClearanceMultiplier = 1.08,
        InferredDailyErosionRatePercent = 0.58,
        ResidualRmse = 0.042,
        AdjustedRedoseDay = 164,
        RecommendedNextMassMg = 19.4,
        RecommendedNextErosionRatePercent = 0.522,
        OverallSafetyStatus = DepotSafetyStatus.Optimal,
        Alerts = [],
        ComputedAtUtc = DateTimeOffset.Parse("2026-11-19T12:05:00Z", CultureInfo.InvariantCulture),
        RecalibratedTrajectory = new TimeReleaseSimulationResult().GetExamples()
    };
}

/// <summary>
/// Pure static mapping helpers bridging persisted depot records, PHI encryption, domain calibration requests,
/// and REST API response contracts.
/// </summary>
public static class DepotCalibrationMapper
{
    /// <summary>
    /// Converts a persisted <see cref="DepotFormulationRecord"/> into baseline <see cref="TimeReleaseDepotParameters"/>
    /// suitable for forward simulation and Bayesian longitudinal calibration.
    /// </summary>
    /// <param name="formulation">The persisted depot formulation record.</param>
    /// <returns>Baseline <see cref="TimeReleaseDepotParameters"/> populated from the formulation.</returns>
    public static TimeReleaseDepotParameters ToBaselineParameters(DepotFormulationRecord formulation)
    {
        // WHAT: Maps persisted formulation strings and scalar properties into typed TimeReleaseDepotParameters.
        // WHY:  Bridges relational string columns (e.g. 'PLGA-PEG-PLGA', 'Korsmeyer-Peppas') with strongly typed enums.
        // HOW:  Strips hyphens/spaces for enum parsing with biophysical defaults and regimen-appropriate initial hormones.
        ArgumentNullException.ThrowIfNull(formulation);

        bool isFtm = formulation.Regimen == TherapeuticRegimen.FemaleToMale;
        return new TimeReleaseDepotParameters
        {
            Regimen = formulation.Regimen,
            MatrixType = ParsePolymerMatrix(formulation.PolymerMatrixType),
            KineticsModel = ParseReleaseKinetics(formulation.ReleaseKineticsModel),
            InitialPayloadMassMg = formulation.InitialNanocarrierMassMg,
            TargetHorizonDays = formulation.TargetHorizonDays,
            ClearanceMultiplier = 1.0,
            DailyErosionRatePercent = formulation.DailyErosionRatePercent,
            InitialEstradiolPmolL = isFtm ? 550.0 : 120.0,
            InitialTestosteroneNmolL = isFtm ? 1.5 : 18.0,
            InitialLhIuL = 6.5,
            InitialFshIuL = 5.0,
            TargetEstradiolPmolL = isFtm ? 100.0 : 550.0,
            TargetTestosteroneNmolL = isFtm ? 20.0 : 1.2
        };
    }

    /// <summary>
    /// Converts a persisted <see cref="DepotAssayObservationRecord"/> into a domain <see cref="DepotAssayObservation"/>.
    /// </summary>
    /// <param name="record">The persisted assay observation record.</param>
    /// <returns>A domain <see cref="DepotAssayObservation"/> instance.</returns>
    public static DepotAssayObservation ToDomainObservation(DepotAssayObservationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new DepotAssayObservation
        {
            DaySinceImplant = record.DaySinceImplant,
            ObservedE2PmolL = record.ObservedE2PmolL,
            ObservedTNmolL = record.ObservedTNmolL,
            ObservedLhIuL = record.ObservedLhIuL,
            ObservedFshIuL = record.ObservedFshIuL,
            ObservedShbgNmolL = record.ObservedShbgNmolL,
            ObservedHematocritPct = record.ObservedHematocritPct,
            RecordedAtUtc = record.RecordedAtUtc
        };
    }

    /// <summary>
    /// Maps a persisted <see cref="DepotAssayObservationRecord"/> to an API <see cref="DepotAssayObservationResponse"/>,
    /// decrypting any stored PHI clinical notes via <see cref="ProfileDataProtector"/>.
    /// </summary>
    /// <param name="record">The persisted assay observation record.</param>
    /// <param name="protector">The node PHI envelope protector.</param>
    /// <returns>An API-ready <see cref="DepotAssayObservationResponse"/>.</returns>
    public static DepotAssayObservationResponse ToApiResponse(
        DepotAssayObservationRecord record,
        ProfileDataProtector protector)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(protector);

        string? decryptedNotes = string.IsNullOrWhiteSpace(record.NotesEncrypted)
            ? null
            : protector.DecryptDataUri(record.NotesEncrypted);

        return new DepotAssayObservationResponse
        {
            ObservationId = record.ObservationId,
            DepotId = record.DepotId,
            PatientId = record.PatientId,
            DaySinceImplant = record.DaySinceImplant,
            ObservedE2PmolL = record.ObservedE2PmolL,
            ObservedTNmolL = record.ObservedTNmolL,
            ObservedLhIuL = record.ObservedLhIuL,
            ObservedFshIuL = record.ObservedFshIuL,
            ObservedShbgNmolL = record.ObservedShbgNmolL,
            ObservedHematocritPct = record.ObservedHematocritPct,
            RecordedAtUtc = record.RecordedAtUtc,
            ClinicalNotes = decryptedNotes
        };
    }

    /// <summary>
    /// Parses a polymer matrix descriptor string into a <see cref="PolymerMatrixType"/> enum value.
    /// </summary>
    /// <param name="rawMatrix">The raw polymer matrix string (e.g. <c>PLGA-PEG-PLGA</c> or <c>AlginateChitosan</c>).</param>
    /// <returns>The parsed <see cref="PolymerMatrixType"/>, defaulting to <see cref="PolymerMatrixType.PlgaPegPlga"/>.</returns>
    public static PolymerMatrixType ParsePolymerMatrix(string? rawMatrix)
    {
        if (string.IsNullOrWhiteSpace(rawMatrix))
        {
            return PolymerMatrixType.PlgaPegPlga;
        }

        string normalized = rawMatrix.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        return Enum.TryParse(normalized, ignoreCase: true, out PolymerMatrixType parsed)
            ? parsed
            : PolymerMatrixType.PlgaPegPlga;
    }

    /// <summary>
    /// Parses a release kinetics descriptor string into a <see cref="ReleaseKineticsModel"/> enum value.
    /// </summary>
    /// <param name="rawModel">The raw kinetics model string (e.g. <c>Korsmeyer-Peppas</c> or <c>Coupled-Fickian</c>).</param>
    /// <returns>The parsed <see cref="ReleaseKineticsModel"/>, defaulting to <see cref="ReleaseKineticsModel.KorsmeyerPeppas"/>.</returns>
    public static ReleaseKineticsModel ParseReleaseKinetics(string? rawModel)
    {
        if (string.IsNullOrWhiteSpace(rawModel))
        {
            return ReleaseKineticsModel.KorsmeyerPeppas;
        }

        string normalized = rawModel.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        if (normalized.Equals("CoupledFickian", StringComparison.OrdinalIgnoreCase))
        {
            return ReleaseKineticsModel.CoupledAnomalousFickian;
        }

        return Enum.TryParse(normalized, ignoreCase: true, out ReleaseKineticsModel parsed)
            ? parsed
            : ReleaseKineticsModel.KorsmeyerPeppas;
    }
}
