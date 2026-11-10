using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Record representing a semi-annual time-release depot formulation engineered for sustained nanocarrier elution.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A clinical depot specification encapsulating polymer matrix composition, initial payload mass,
/// daily erosion degradation rates, and biophysical release kinetics over a multi-month therapeutic horizon.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Enables long-acting, semi-annual (180-day) subcutaneous depot administration of hormone replacement
/// therapy without requiring frequent patient self-injections or clinical visits.
/// </para>
/// <para>
/// <strong>How it works:</strong> Configures nanocarrier erosion kinetics (e.g. Korsmeyer-Peppas or coupled Fickian diffusion)
/// inside biocompatible polymer matrices such as PLGA-PEG-PLGA hydrogels to deliver steady-state hormone kinetics.
/// </para>
/// </remarks>
public sealed record DepotFormulationRecord : IExamplesProvider<DepotFormulationRecord>
{
    /// <summary>
    /// Gets or initializes the unique identifier of the depot formulation.
    /// </summary>
    [Description("Unique identifier for this depot formulation.")]
    [JsonPropertyName("depotId")]
    public string DepotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the patient identifier associated with this depot.
    /// </summary>
    [Description("Patient identifier associated with this depot formulation.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the target therapeutic endocrine regimen.
    /// </summary>
    [Description("Target therapeutic endocrine regimen (MaleToFemale, FemaleToMale, or Custom).")]
    [JsonPropertyName("regimen")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TherapeuticRegimen Regimen { get; init; } = TherapeuticRegimen.MaleToFemale;

    /// <summary>
    /// Gets or initializes the polymer matrix carrier type.
    /// </summary>
    [Description("Polymer matrix composition (e.g. PLGA-PEG-PLGA, Alginate-Chitosan, Cyclodextrin-Scaffold).")]
    [JsonPropertyName("polymerMatrixType")]
    public string PolymerMatrixType { get; init; } = "PLGA-PEG-PLGA";

    /// <summary>
    /// Gets or initializes the initial loaded nanocarrier payload mass in milligrams.
    /// </summary>
    [Description("Initial nanocarrier payload mass in milligrams (mg).")]
    [JsonPropertyName("initialNanocarrierMassMg")]
    public double InitialNanocarrierMassMg { get; init; }

    /// <summary>
    /// Gets or initializes the nominal daily matrix erosion degradation rate in percentage.
    /// </summary>
    [Description("Nominal daily polymer matrix erosion degradation rate in percent (%/day).")]
    [JsonPropertyName("dailyErosionRatePercent")]
    public double DailyErosionRatePercent { get; init; }

    /// <summary>
    /// Gets or initializes the mathematical release kinetics model.
    /// </summary>
    [Description("Mathematical release kinetics model (e.g. Korsmeyer-Peppas, Higuchi, Coupled-Fickian).")]
    [JsonPropertyName("releaseKineticsModel")]
    public string ReleaseKineticsModel { get; init; } = "Korsmeyer-Peppas";

    /// <summary>
    /// Gets or initializes the targeted therapeutic time horizon in days.
    /// </summary>
    [Description("Targeted therapeutic time horizon in days (default 180 days for semi-annual administration).")]
    [JsonPropertyName("targetHorizonDays")]
    public int TargetHorizonDays { get; init; } = 180;

    /// <summary>
    /// Gets or initializes the timestamp when the formulation was generated or recorded in UTC.
    /// </summary>
    [Description("Timestamp when the depot formulation was created in UTC.")]
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Generates a representative example instance of <see cref="DepotFormulationRecord"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated depot formulation record.</returns>
    public DepotFormulationRecord GetExamples() => new()
    {
        DepotId = "depot_sample_001",
        PatientId = "patient_alice",
        Regimen = TherapeuticRegimen.MaleToFemale,
        PolymerMatrixType = "PLGA-PEG-PLGA",
        InitialNanocarrierMassMg = 150.0,
        DailyErosionRatePercent = 0.55,
        ReleaseKineticsModel = "Korsmeyer-Peppas",
        TargetHorizonDays = 180,
        CreatedAtUtc = DateTimeOffset.Parse("2026-11-10T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture)
    };
}

/// <summary>
/// Record representing an individual discrete timepoint along a simulated or measured depot release trajectory.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A discrete point in time capturing the cumulative payload fraction eluted,
/// instantaneous delivery rate, and remaining depot payload reserve.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Allows clinicians and pharmacokinetic models to evaluate hormone release profiles
/// day-by-day and ensure delivery remains strictly within therapeutic physiological target windows.
/// </para>
/// <para>
/// <strong>How it works:</strong> Stores cumulative fraction, daily rate in mg/day, and remaining payload mass
/// indexed against formulation depot ID and simulation elapsed day.
/// </para>
/// </remarks>
public sealed record DepotSchedulePoint : IExamplesProvider<DepotSchedulePoint>
{
    /// <summary>
    /// Gets or initializes the unique identifier of this schedule timepoint.
    /// </summary>
    [Description("Unique schedule point identifier.")]
    [JsonPropertyName("scheduleId")]
    public string ScheduleId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the depot formulation identifier to which this point belongs.
    /// </summary>
    [Description("Parent depot formulation identifier.")]
    [JsonPropertyName("depotId")]
    public string DepotId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the simulation elapsed timepoint in days.
    /// </summary>
    [Description("Elapsed timepoint day since depot implantation (e.g. 0 to 180).")]
    [JsonPropertyName("timepointDay")]
    public int TimepointDay { get; init; }

    /// <summary>
    /// Gets or initializes the cumulative released fraction of the total payload (0.0 to 1.0).
    /// </summary>
    [Description("Cumulative fraction of nanocarrier payload released (0.0 to 1.0).")]
    [JsonPropertyName("cumulativeReleaseFraction")]
    public double CumulativeReleaseFraction { get; init; }

    /// <summary>
    /// Gets or initializes the instantaneous delivery release rate in milligrams per day.
    /// </summary>
    [Description("Instantaneous nanocarrier elution rate in milligrams per day (mg/day).")]
    [JsonPropertyName("instantaneousRateMgDay")]
    public double InstantaneousRateMgDay { get; init; }

    /// <summary>
    /// Gets or initializes the remaining unreleased depot payload reserve in milligrams.
    /// </summary>
    [Description("Remaining nanocarrier payload mass remaining in the depot matrix in milligrams (mg).")]
    [JsonPropertyName("remainingReserveMg")]
    public double RemainingReserveMg { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="DepotSchedulePoint"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated depot release schedule point.</returns>
    public DepotSchedulePoint GetExamples() => new()
    {
        ScheduleId = "sched_pt_030",
        DepotId = "depot_sample_001",
        TimepointDay = 30,
        CumulativeReleaseFraction = 0.165,
        InstantaneousRateMgDay = 0.825,
        RemainingReserveMg = 125.25
    };
}

/// <summary>
/// Request payload for creating and persisting a semi-annual time-release depot formulation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A clinical request contract defining patient ID, endocrine regimen,
/// polymer carrier matrix, initial nanocarrier mass, daily erosion rate, and optional initial schedule points.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Allows clinical researchers and administrators to record bespoke depot formulations
/// for long-acting hormone delivery.
/// </para>
/// <para>
/// <strong>How it works:</strong> Deserialized from HTTP POST /api/depots/formulations and persisted via IDepotFormulationRepository.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "CreateDepotFormulationRequest", Description = "Request payload for creating and persisting a semi-annual time-release depot formulation.")]
public sealed record CreateDepotFormulationRequest : IExamplesProvider<CreateDepotFormulationRequest>
{
    /// <summary>
    /// Gets or initializes the target patient identifier.
    /// </summary>
    [Description("Target patient identifier.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the target therapeutic endocrine regimen.
    /// </summary>
    [Description("Target therapeutic endocrine regimen (MaleToFemale, FemaleToMale, or Custom).")]
    [JsonPropertyName("regimen")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TherapeuticRegimen Regimen { get; init; } = TherapeuticRegimen.MaleToFemale;

    /// <summary>
    /// Gets or initializes the polymer matrix carrier type.
    /// </summary>
    [Description("Polymer matrix composition (e.g. PLGA-PEG-PLGA, Alginate-Chitosan, Cyclodextrin-Scaffold).")]
    [JsonPropertyName("polymerMatrixType")]
    public string PolymerMatrixType { get; init; } = "PLGA-PEG-PLGA";

    /// <summary>
    /// Gets or initializes an optional alias for polymer matrix carrier type.
    /// </summary>
    [Description("Optional alias for polymer matrix carrier type.")]
    [JsonPropertyName("matrixType")]
    public string? MatrixType { get; init; }

    /// <summary>
    /// Gets or initializes the initial loaded nanocarrier payload mass in milligrams.
    /// </summary>
    [Description("Initial nanocarrier payload mass in milligrams (mg).")]
    [JsonPropertyName("initialNanocarrierMassMg")]
    public double InitialNanocarrierMassMg { get; init; }

    /// <summary>
    /// Gets or initializes the nominal daily matrix erosion degradation rate in percentage.
    /// </summary>
    [Description("Nominal daily polymer matrix erosion degradation rate in percent (%/day).")]
    [JsonPropertyName("dailyErosionRatePercent")]
    public double DailyErosionRatePercent { get; init; }

    /// <summary>
    /// Gets or initializes the mathematical release kinetics model.
    /// </summary>
    [Description("Mathematical release kinetics model (e.g. Korsmeyer-Peppas, Higuchi, Coupled-Fickian).")]
    [JsonPropertyName("releaseKineticsModel")]
    public string ReleaseKineticsModel { get; init; } = "Korsmeyer-Peppas";

    /// <summary>
    /// Gets or initializes an optional alias for mathematical release kinetics model.
    /// </summary>
    [Description("Optional alias for mathematical release kinetics model.")]
    [JsonPropertyName("kineticsModel")]
    public string? KineticsModel { get; init; }

    /// <summary>
    /// Gets or initializes the targeted therapeutic time horizon in days.
    /// </summary>
    [Description("Targeted therapeutic time horizon in days (default 180 days).")]
    [JsonPropertyName("targetHorizonDays")]
    public int TargetHorizonDays { get; init; } = 180;

    /// <summary>
    /// Gets or initializes the optional discrete initial release schedule timepoints.
    /// </summary>
    [Description("Optional discrete initial release schedule timepoints.")]
    [JsonPropertyName("initialSchedule")]
    public IReadOnlyList<DepotSchedulePoint>? InitialSchedule { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="CreateDepotFormulationRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated depot formulation creation request.</returns>
    public CreateDepotFormulationRequest GetExamples() => new()
    {
        PatientId = "patient_alice",
        Regimen = TherapeuticRegimen.MaleToFemale,
        PolymerMatrixType = "PLGA-PEG-PLGA",
        InitialNanocarrierMassMg = 150.0,
        DailyErosionRatePercent = 0.55,
        ReleaseKineticsModel = "Korsmeyer-Peppas",
        TargetHorizonDays = 180,
        InitialSchedule = [new DepotSchedulePoint().GetExamples()]
    };
}

/// <summary>
/// Response payload containing a depot formulation record and its associated release schedule timepoints.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A clinical response DTO encapsulating the persisted depot formulation record and its discrete release schedule.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Provides a unified representation of depot specifications alongside daily elution trajectory points.
/// </para>
/// <para>
/// <strong>How it works:</strong> Serialized to JSON when querying or creating depot formulations.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "DepotFormulationResponse", Description = "Response payload containing a depot formulation record and its associated release schedule timepoints.")]
public sealed record DepotFormulationResponse : IExamplesProvider<DepotFormulationResponse>
{
    /// <summary>
    /// Gets or initializes the semi-annual depot formulation record.
    /// </summary>
    [Description("The semi-annual depot formulation record.")]
    [JsonPropertyName("formulation")]
    public DepotFormulationRecord Formulation { get; init; } = new();

    /// <summary>
    /// Gets or initializes the discrete release schedule timepoints associated with the depot formulation.
    /// </summary>
    [Description("The discrete release schedule timepoints associated with the depot formulation.")]
    [JsonPropertyName("schedule")]
    public IReadOnlyList<DepotSchedulePoint> Schedule { get; init; } = [];

    /// <summary>
    /// Generates a representative example instance of <see cref="DepotFormulationResponse"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated depot formulation response.</returns>
    public DepotFormulationResponse GetExamples() => new()
    {
        Formulation = new DepotFormulationRecord().GetExamples(),
        Schedule = [new DepotSchedulePoint().GetExamples()]
    };
}

/// <summary>
/// Request payload for simulating a multi-phase semi-annual time-release depot trajectory.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A simulation request contract specifying polymer matrix composition, release kinetics model,
/// initial payload mass, erosion rate, target daily dose, and baseline endocrine hormone levels.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Allows researchers to predict multi-phase (burst suppression, steady-state, terminal taper)
/// hormone release and pituitary gonadotropin suppression before physical formulation fabrication.
/// </para>
/// <para>
/// <strong>How it works:</strong> Deserialized from HTTP POST /api/depots/simulate-release and executed via ITimeReleaseDepotService.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "SimulateDepotReleaseRequest", Description = "Request payload for simulating a multi-phase semi-annual time-release depot trajectory.")]
public sealed record SimulateDepotReleaseRequest : IExamplesProvider<SimulateDepotReleaseRequest>
{
    /// <summary>
    /// Gets or initializes the target therapeutic endocrine regimen.
    /// </summary>
    [Description("Target therapeutic endocrine regimen (MaleToFemale, FemaleToMale, or Custom).")]
    [JsonPropertyName("regimen")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TherapeuticRegimen Regimen { get; init; } = TherapeuticRegimen.MaleToFemale;

    /// <summary>
    /// Gets or initializes the biocompatible polymer matrix composition.
    /// </summary>
    [Description("Biocompatible polymer matrix composition.")]
    [JsonPropertyName("matrixType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PolymerMatrixType MatrixType { get; init; } = PolymerMatrixType.PlgaPegPlga;

    /// <summary>
    /// Gets or initializes the mathematical release kinetics model.
    /// </summary>
    [Description("Mathematical release kinetics model.")]
    [JsonPropertyName("kineticsModel")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ReleaseKineticsModel KineticsModel { get; init; } = ReleaseKineticsModel.KorsmeyerPeppas;

    /// <summary>
    /// Gets or initializes the initial loaded nanocarrier payload mass in milligrams.
    /// </summary>
    [Description("Initial nanocarrier payload mass loaded into the depot in milligrams (mg).")]
    [JsonPropertyName("initialNanocarrierMassMg")]
    public double InitialNanocarrierMassMg { get; init; } = 18.0;

    /// <summary>
    /// Gets or initializes the nominal daily matrix erosion degradation rate in percentage.
    /// </summary>
    [Description("Nominal daily polymer matrix erosion degradation rate in percent (%/day).")]
    [JsonPropertyName("dailyErosionRatePercent")]
    public double DailyErosionRatePercent { get; init; } = 0.55;

    /// <summary>
    /// Gets or initializes the targeted therapeutic time horizon in days.
    /// </summary>
    [Description("Targeted therapeutic time horizon in days (default 180 days).")]
    [JsonPropertyName("horizonDays")]
    public int HorizonDays { get; init; } = 180;

    /// <summary>
    /// Gets or initializes the patient-specific metabolic clearance rate multiplier.
    /// </summary>
    [Description("Patient-specific metabolic clearance rate multiplier (range 0.1 to 5.0).")]
    [JsonPropertyName("clearanceMultiplier")]
    public double ClearanceMultiplier { get; init; } = 1.0;

    /// <summary>
    /// Gets or initializes the target daily maintenance dose in milligrams per day.
    /// </summary>
    [Description("Target daily maintenance dose in milligrams per day (mg/day).")]
    [JsonPropertyName("targetDailyDoseMg")]
    public double TargetDailyDoseMg { get; init; } = 0.10;

    /// <summary>
    /// Gets or initializes the optional initial baseline serum 17β-estradiol concentration in pmol/L.
    /// </summary>
    [Description("Optional initial baseline serum 17β-estradiol concentration in pmol/L.")]
    [JsonPropertyName("initialEstradiolPmolL")]
    public double? InitialEstradiolPmolL { get; init; }

    /// <summary>
    /// Gets or initializes the optional initial baseline serum total testosterone concentration in nmol/L.
    /// </summary>
    [Description("Optional initial baseline serum total testosterone concentration in nmol/L.")]
    [JsonPropertyName("initialTestosteroneNmolL")]
    public double? InitialTestosteroneNmolL { get; init; }

    /// <summary>
    /// Gets or initializes the optional initial circulating luteinizing hormone (LH) concentration in IU/L.
    /// </summary>
    [Description("Optional initial circulating luteinizing hormone (LH) concentration in IU/L.")]
    [JsonPropertyName("initialLhIuL")]
    public double? InitialLhIuL { get; init; }

    /// <summary>
    /// Gets or initializes the optional initial circulating follicle-stimulating hormone (FSH) concentration in IU/L.
    /// </summary>
    [Description("Optional initial circulating follicle-stimulating hormone (FSH) concentration in IU/L.")]
    [JsonPropertyName("initialFshIuL")]
    public double? InitialFshIuL { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="SimulateDepotReleaseRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated depot simulation request.</returns>
    public SimulateDepotReleaseRequest GetExamples() => new();
}
