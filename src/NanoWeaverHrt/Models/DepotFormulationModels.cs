using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Swagger;

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
