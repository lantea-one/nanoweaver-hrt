using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Fabrication;

/// <summary>
/// Clinical cold-chain stability classification derived from cumulative Arrhenius thermal degradation,
/// dsDNA origami cooperative melting thresholds, and 365-day nominal shelf-life limits.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ThermalStabilityStatus
{
    /// <summary>
    /// Lot remains within optimal frozen cold-chain parameters (at least 90% potency and below 25% thermal budget used).
    /// </summary>
    Optimal = 0,

    /// <summary>
    /// Lot remains safe for implantation (at least 90% potency), but elevated temperature exposure or thermal budget usage warrants clinical notice.
    /// </summary>
    AdvisoryExcursion = 1,

    /// <summary>
    /// Lot has dropped below 90% active potency or exceeded its 365-day nominal shelf life; unsafe for clinical implantation.
    /// </summary>
    ExpiredOrCompromised = 2,

    /// <summary>
    /// Lot experienced a catastrophic dsDNA origami melting excursion above 42.0°C; permanently unsafe for implantation.
    /// </summary>
    CatastrophicDenaturation = 3
}

/// <summary>
/// Physical and biochemical constants governing first-order Arrhenius thermal degradation and dsDNA origami
/// melting-cliff kinetics for PLGA-PEG-PLGA encapsulated nanocarrier HRT depots.
/// </summary>
public static class ArrheniusColdChainConstants
{
    /// <summary>
    /// Universal ideal gas constant R in J/(mol·K) (2019 SI exact definition).
    /// </summary>
    public const double UniversalGasConstantJPerMolK = 8.314462618;

    /// <summary>
    /// Offset converting degrees Celsius to Kelvin (0°C = 273.15 K).
    /// </summary>
    public const double AbsoluteZeroOffsetKelvin = 273.15;

    /// <summary>
    /// Reference clinical freezer storage temperature in degrees Celsius (-20.0°C).
    /// </summary>
    public const double ReferenceStorageCelsius = -20.0;

    /// <summary>
    /// Reference clinical freezer storage temperature in Kelvin (253.15 K).
    /// </summary>
    public const double ReferenceStorageKelvin = 253.15;

    /// <summary>
    /// Standard clinical refrigerator temperature in degrees Celsius (+4.0°C) used for short-term shelf-life projections.
    /// </summary>
    public const double RefrigeratedReferenceCelsius = 4.0;

    /// <summary>
    /// Nominal shelf-life duration in days at reference -20°C storage before reaching 90% potency (365 days).
    /// </summary>
    public const double NominalShelfLifeDays = 365.0;

    /// <summary>
    /// Minimum required remaining active potency percentage permitted for clinical implantation (90.0%).
    /// </summary>
    public const double MinimumSafePotencyPercent = 90.0;

    /// <summary>
    /// Activation energy Ea in J/mol modeling dsDNA origami staple dissociation and PLGA-PEG-PLGA hydrogel hydrolysis (72 kJ/mol).
    /// </summary>
    public const double ActivationEnergyJPerMol = 72_000.0;

    /// <summary>
    /// Reference first-order thermal degradation rate constant k_ref (h^-1) calibrated to 90% potency at 365 days at -20°C.
    /// </summary>
    public static readonly double ReferenceRateConstantPerHour = -Math.Log(0.90) / (NominalShelfLifeDays * 24.0);

    /// <summary>
    /// Maximum allowable cumulative dimensionless decay exponent (-ln(0.90)) corresponding to 100% thermal budget utilization.
    /// </summary>
    public static readonly double MaxAllowableDecayExponent = -Math.Log(0.90);

    /// <summary>
    /// Lower threshold in degrees Celsius for cooperative dsDNA origami pre-melting transition acceleration (37.0°C).
    /// </summary>
    public const double CriticalMeltingWarningCelsius = 37.0;

    /// <summary>
    /// Accelerated degradation multiplier applied during pre-melting excursions between 37.0°C and 42.0°C inclusive.
    /// </summary>
    public const double TransitionMeltingPenaltyMultiplier = 10.0;

    /// <summary>
    /// Threshold in degrees Celsius above which dsDNA origami scaffold denaturation becomes catastrophic and irreversible (42.0°C).
    /// </summary>
    public const double CatastrophicDenaturationCelsius = 42.0;

    /// <summary>
    /// Catastrophic denaturation degradation multiplier applied strictly above 42.0°C.
    /// </summary>
    public const double CatastrophicDenaturationMultiplier = 100.0;

    /// <summary>
    /// Lower validation bound for physical cold-chain temperature readings in degrees Celsius (liquid nitrogen, -196.0°C).
    /// </summary>
    public const double MinValidTemperatureCelsius = -196.0;

    /// <summary>
    /// Upper validation bound for physical temperature readings in degrees Celsius (autoclave steam, +121.0°C).
    /// </summary>
    public const double MaxValidTemperatureCelsius = 121.0;

    /// <summary>
    /// Upper validation bound for a single thermal excursion exposure duration in hours (10 years = 87,600 h).
    /// </summary>
    public const double MaxSingleDurationHours = 87_600.0;

    /// <summary>
    /// Temperature threshold in degrees Celsius above standard refrigeration triggering an advisory excursion notice (+8.0°C).
    /// </summary>
    public const double AdvisoryTemperatureCelsius = 8.0;

    /// <summary>
    /// Cumulative thermal budget percentage threshold triggering an advisory notice (25.0%).
    /// </summary>
    public const double AdvisoryBudgetPercent = 25.0;

    /// <summary>
    /// Genesis previous-entry SHA-256 hex string (64 lowercase zeros) anchored on the first custody handoff of every lot.
    /// </summary>
    public const string GenesisCustodyPrevSha256 = "0000000000000000000000000000000000000000000000000000000000000000";
}

/// <summary>
/// Verification verdict summarizing SHA-256 hash-chain integrity across a fabrication lot's custody ledger.
/// </summary>
public sealed record CustodyChainVerificationResult
{
    /// <summary>
    /// Gets or initializes a value indicating whether every entry in the custody chain verified cleanly from genesis to tail.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets or initializes the diagnostic reason when <see cref="IsValid"/> is false; null when valid.
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// Gets or initializes the total number of custody handoff records evaluated.
    /// </summary>
    public int EvaluatedCount { get; init; }
}

/// <summary>
/// Comprehensive Arrhenius thermal stability, shelf-life projection, and custody-chain integrity report
/// for a physically fabricated nanocarrier HRT lot.
/// </summary>
[SwaggerSchema(Title = "LotStabilityReport", Description = "Arrhenius cold-chain potency, remaining shelf-life, and custody hash-chain verification report.")]
public sealed record LotStabilityReport : IExamplesProvider<LotStabilityReport>
{
    /// <summary>
    /// Gets or initializes the fabrication lot identifier evaluated by this report.
    /// </summary>
    [Description("Wet-lab fabrication lot identifier.")]
    [JsonPropertyName("lotId")]
    public string LotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the patient identifier owning this fabrication lot.
    /// </summary>
    [Description("Patient identifier owning this fabrication lot.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the UTC timestamp at which Arrhenius kinetics were evaluated.
    /// </summary>
    [Description("UTC timestamp at which thermal stability was evaluated.")]
    [JsonPropertyName("evaluatedAtUtc")]
    public DateTimeOffset EvaluatedAtUtc { get; init; }

    /// <summary>
    /// Gets or initializes the nominal 365-day expiration UTC timestamp measured from lot release (or creation if unreleased).
    /// </summary>
    [Description("Nominal 365-day expiration UTC timestamp.")]
    [JsonPropertyName("nominalExpiryAtUtc")]
    public DateTimeOffset NominalExpiryAtUtc { get; init; }

    /// <summary>
    /// Gets or initializes the computed remaining active nanocarrier potency percentage (0.0 to 100.0).
    /// </summary>
    [Description("Remaining active nanocarrier potency percentage (0.0 to 100.0).")]
    [JsonPropertyName("remainingPotencyPercent")]
    public double RemainingPotencyPercent { get; init; }

    /// <summary>
    /// Gets or initializes the cumulative percentage of the total 10%-loss thermal budget consumed (100% = 90% potency).
    /// </summary>
    [Description("Cumulative percentage of total allowable thermal budget consumed (100% corresponds to 90% potency).")]
    [JsonPropertyName("cumulativeThermalBudgetUsedPercent")]
    public double CumulativeThermalBudgetUsedPercent { get; init; }

    /// <summary>
    /// Gets or initializes the projected remaining safe shelf life in days if stored continuously at -20°C.
    /// </summary>
    [Description("Projected remaining safe shelf life in days if maintained at -20°C.")]
    [JsonPropertyName("estimatedRemainingShelfLifeDaysAtMinus20C")]
    public double EstimatedRemainingShelfLifeDaysAtMinus20C { get; init; }

    /// <summary>
    /// Gets or initializes the projected remaining safe shelf life in days if stored continuously at +4°C.
    /// </summary>
    [Description("Projected remaining safe shelf life in days if maintained at +4°C.")]
    [JsonPropertyName("estimatedRemainingShelfLifeDaysAt4C")]
    public double EstimatedRemainingShelfLifeDaysAt4C { get; init; }

    /// <summary>
    /// Gets or initializes the overall cold-chain stability classification.
    /// </summary>
    [Description("Overall thermal stability status classification.")]
    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ThermalStabilityStatus Status { get; init; } = ThermalStabilityStatus.Optimal;

    /// <summary>
    /// Gets or initializes a value indicating whether any logged excursion exceeded the 42.0°C irreversible denaturation cliff.
    /// </summary>
    [Description("True when any excursion exceeded 42.0°C (irreversible dsDNA origami melting).")]
    [JsonPropertyName("hasCatastrophicExcursion")]
    public bool HasCatastrophicExcursion { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether evaluation time has exceeded the 365-day nominal expiration date.
    /// </summary>
    [Description("True when the lot has passed its nominal 365-day expiration date.")]
    [JsonPropertyName("isPastNominalExpiry")]
    public bool IsPastNominalExpiry { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether the SHA-256 chain-of-custody ledger verified cleanly.
    /// </summary>
    [Description("True when all recorded custody handoffs verify cleanly from genesis to tail.")]
    [JsonPropertyName("custodyChainValid")]
    public bool CustodyChainValid { get; init; } = true;

    /// <summary>
    /// Gets or initializes the diagnostic reason when <see cref="CustodyChainValid"/> is false; otherwise null.
    /// </summary>
    [Description("Diagnostic reason if custody hash-chain verification failed; otherwise null.")]
    [JsonPropertyName("custodyChainFailureReason")]
    public string? CustodyChainFailureReason { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether this lot passes cold-chain safety criteria for clinical implantation
    /// (at least 90% potency, no catastrophic denaturation, not expired, and valid custody hash chain).
    /// </summary>
    [Description("True only when remaining potency >= 90%, no catastrophic melting occurred, not expired, and custody chain is valid.")]
    [JsonPropertyName("isSafeForImplantation")]
    public bool IsSafeForImplantation { get; init; }

    /// <summary>
    /// Gets or initializes the number of thermal excursion records included in this stability calculation.
    /// </summary>
    [Description("Count of thermal excursion log entries evaluated.")]
    [JsonPropertyName("excursionCount")]
    public int ExcursionCount { get; init; }

    /// <summary>
    /// Gets or initializes the number of custody handoff records evaluated for hash-chain integrity.
    /// </summary>
    [Description("Count of custody handoff records evaluated.")]
    [JsonPropertyName("custodyHandoffCount")]
    public int CustodyHandoffCount { get; init; }

    /// <summary>
    /// Gets or initializes a plain-English clinical summary of this lot's thermal stability and custody status.
    /// </summary>
    [Description("Plain-English clinical summary of cold-chain stability and custody status.")]
    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    /// <inheritdoc/>
    public LotStabilityReport GetExamples() => new()
    {
        LotId = "lot_20261119_001",
        PatientId = "patient_alice",
        EvaluatedAtUtc = DateTimeOffset.Parse("2026-11-19T16:00:00.000000Z", CultureInfo.InvariantCulture),
        NominalExpiryAtUtc = DateTimeOffset.Parse("2027-11-19T12:00:00.000000Z", CultureInfo.InvariantCulture),
        RemainingPotencyPercent = 99.64,
        CumulativeThermalBudgetUsedPercent = 3.42,
        EstimatedRemainingShelfLifeDaysAtMinus20C = 352.5,
        EstimatedRemainingShelfLifeDaysAt4C = 17.9,
        Status = ThermalStabilityStatus.Optimal,
        HasCatastrophicExcursion = false,
        IsPastNominalExpiry = false,
        CustodyChainValid = true,
        CustodyChainFailureReason = null,
        IsSafeForImplantation = true,
        ExcursionCount = 1,
        CustodyHandoffCount = 2,
        Summary = "Optimal cold-chain stability: 99.64% potency remaining (3.42% thermal budget used), custody chain verified."
    };
}
