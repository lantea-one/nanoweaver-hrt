using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using WeaverLammpsHrt.TrajectoryParser.Endocrine;
using WeaverLammpsHrt.TrajectoryParser.Swagger;

namespace WeaverLammpsHrt.TrajectoryParser.Simulations.Models;

/// <summary>
/// Execution request for Campaign 4: Quantitative Systems Pharmacology (QSP) / PBPK Whole-Body Endocrine Simulation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> This simulation tracks the nanocarrier and hormone levels across the entire
/// human body over 30, 60, or 90 days. It uses the patient's genetic profile (like clotting or clearance genes) to test
/// whether a subcutaneous depot dose can maintain cis-typical hormone levels without spikes or crashes.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "PbpkSimulationRequest", Description = "Request payload for whole-body PBPK endocrine simulation.")]
public sealed record PbpkSimulationRequest : IExamplesProvider<PbpkSimulationRequest>
{
    /// <summary>
    /// Identifier of the research subject or patient.
    /// </summary>
    [Description("Identifier of the research subject or patient.")]
    [JsonPropertyName("patientId")]
    public string PatientId { get; set; } = "SUBJECT-001";

    /// <summary>
    /// Target therapeutic regimen (MaleToFemale or FemaleToMale).
    /// </summary>
    [Description("Target therapeutic regimen (MaleToFemale or FemaleToMale).")]
    [JsonPropertyName("regimen")]
    public TherapeuticRegimen Regimen { get; set; } = TherapeuticRegimen.MaleToFemale;

    /// <summary>
    /// Administered subcutaneous nanocarrier depot dose in milligrams (default 25.0 mg).
    /// </summary>
    [Description("Administered subcutaneous nanocarrier depot dose in milligrams.")]
    [JsonPropertyName("depotDoseMg")]
    public double DepotDoseMg { get; set; } = 25.0;

    /// <summary>
    /// Duration of whole-body simulation in days (30, 60, or 90 days).
    /// </summary>
    [Description("Duration of whole-body simulation in days (30, 60, or 90 days).")]
    [JsonPropertyName("simulationDurationDays")]
    public int SimulationDurationDays { get; set; } = 90;

    /// <summary>
    /// Targeted redosing interval in days (e.g. 180 days for semi-annual administration).
    /// </summary>
    [Description("Targeted redosing interval in days.")]
    [JsonPropertyName("deliveryIntervalDays")]
    public int DeliveryIntervalDays { get; set; } = 180;

    /// <summary>
    /// Optional list of patient genetic marker IDs (e.g. F5_LEIDEN, AR_CAG_LONG) to incorporate into kinetic scaling.
    /// </summary>
    [Description("Optional list of patient genetic marker IDs to incorporate.")]
    [JsonPropertyName("genomicMarkerIds")]
    public IReadOnlyList<string> GenomicMarkerIds { get; set; } = [];

    /// <summary>
    /// Generates a representative example of <see cref="PbpkSimulationRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated example instance.</returns>
    public PbpkSimulationRequest GetExamples() => new()
    {
        PatientId = "SUBJECT-001",
        Regimen = TherapeuticRegimen.MaleToFemale,
        DepotDoseMg = 25.0,
        SimulationDurationDays = 90,
        DeliveryIntervalDays = 180,
        GenomicMarkerIds = ["F5_LEIDEN", "COMT_VAL158MET"]
    };
}

/// <summary>
/// Simulated daily endocrine and pharmacokinetics data point in the PBPK model.
/// </summary>
[SwaggerSchema(Title = "PbpkTrajectoryPoint", Description = "Daily endocrine and pharmacokinetics data point.")]
public sealed record PbpkTrajectoryPoint
{
    /// <summary>
    /// Elapsed trial simulation day (1 to N).
    /// </summary>
    [Description("Elapsed trial simulation day.")]
    [JsonPropertyName("day")]
    public required int Day { get; init; }

    /// <summary>
    /// Circulating 17β-estradiol concentration in pmol/L.
    /// </summary>
    [Description("Circulating 17β-estradiol concentration in pmol/L.")]
    [JsonPropertyName("estradiolPmolL")]
    public required double EstradiolPmolL { get; init; }

    /// <summary>
    /// Circulating total testosterone concentration in nmol/L.
    /// </summary>
    [Description("Circulating total testosterone concentration in nmol/L.")]
    [JsonPropertyName("testosteroneNmolL")]
    public required double TestosteroneNmolL { get; init; }

    /// <summary>
    /// Circulating pituitary Luteinizing Hormone (LH) in IU/L.
    /// </summary>
    [Description("Circulating pituitary Luteinizing Hormone (LH) in IU/L.")]
    [JsonPropertyName("gonadotropinLhIuL")]
    public required double GonadotropinLhIuL { get; init; }

    /// <summary>
    /// Remaining percentage of unreleased nanocarrier formulation in the subcutaneous depot.
    /// </summary>
    [Description("Remaining percentage of unreleased nanocarrier in subcutaneous depot.")]
    [JsonPropertyName("depotRemainingPercent")]
    public required double DepotRemainingPercent { get; init; }

    /// <summary>
    /// Active nanocarrier concentration in central plasma compartment in nanomolar.
    /// </summary>
    [Description("Active nanocarrier concentration in central plasma compartment in nanomolar.")]
    [JsonPropertyName("centralSerumConcentrationNm")]
    public required double CentralSerumConcentrationNm { get; init; }
}

/// <summary>
/// Outcome of Campaign 4: Quantitative Systems Pharmacology (QSP) / PBPK Whole-Body Endocrine Simulation.
/// </summary>
[SwaggerSchema(Title = "PbpkEndocrineSimulationResult", Description = "Outcome of whole-body PBPK endocrine simulation.")]
public sealed record PbpkEndocrineSimulationResult : IExamplesProvider<PbpkEndocrineSimulationResult>
{
    /// <summary>
    /// Patient identifier.
    /// </summary>
    [Description("Patient identifier.")]
    [JsonPropertyName("patientId")]
    public required string PatientId { get; init; }

    /// <summary>
    /// Regimen simulated.
    /// </summary>
    [Description("Regimen simulated.")]
    [JsonPropertyName("regimen")]
    public required TherapeuticRegimen Regimen { get; init; }

    /// <summary>
    /// Total duration simulated in days.
    /// </summary>
    [Description("Total duration simulated in days.")]
    [JsonPropertyName("simulationDurationDays")]
    public required int SimulationDurationDays { get; init; }

    /// <summary>
    /// Depot dose administered in milligrams.
    /// </summary>
    [Description("Depot dose administered in milligrams.")]
    [JsonPropertyName("depotDoseMg")]
    public required double DepotDoseMg { get; init; }

    /// <summary>
    /// Average steady-state 17β-estradiol concentration in pmol/L.
    /// </summary>
    [Description("Average steady-state 17β-estradiol concentration in pmol/L.")]
    [JsonPropertyName("averageSteadyStateEstradiolPmolL")]
    public required double AverageSteadyStateEstradiolPmolL { get; init; }

    /// <summary>
    /// Average steady-state total testosterone concentration in nmol/L.
    /// </summary>
    [Description("Average steady-state total testosterone concentration in nmol/L.")]
    [JsonPropertyName("averageSteadyStateTestosteroneNmolL")]
    public required double AverageSteadyStateTestosteroneNmolL { get; init; }

    /// <summary>
    /// Average pituitary Luteinizing Hormone (LH) in IU/L.
    /// </summary>
    [Description("Average pituitary Luteinizing Hormone (LH) in IU/L.")]
    [JsonPropertyName("averageLhIuL")]
    public required double AverageLhIuL { get; init; }

    /// <summary>
    /// Percentage of trial duration where hormone concentrations stayed within clinical target bounds.
    /// </summary>
    [Description("Percentage of trial duration where hormone concentrations stayed within clinical target bounds.")]
    [JsonPropertyName("timeInTargetRangePercent")]
    public required double TimeInTargetRangePercent { get; init; }

    /// <summary>
    /// True if opposite-sex hormone was successfully suppressed to target (MtF T &lt; 1.8 nmol/L, or FtM E2 &lt; 150 pmol/L).
    /// </summary>
    [Description("True if opposite-sex hormone was successfully suppressed to target.")]
    [JsonPropertyName("targetSuppressionAchieved")]
    public required bool TargetSuppressionAchieved { get; init; }

    /// <summary>
    /// True if peak transient levels exceeded safety threshold (e.g. thrombotic surge).
    /// </summary>
    [Description("True if peak transient levels exceeded safety threshold.")]
    [JsonPropertyName("thromboticSurgeDetected")]
    public required bool ThromboticSurgeDetected { get; init; }

    /// <summary>
    /// Empirically determined recommended subcutaneous redosing interval in days.
    /// </summary>
    [Description("Empirically determined recommended subcutaneous redosing interval in days.")]
    [JsonPropertyName("recommendedDosingIntervalDays")]
    public required int RecommendedDosingIntervalDays { get; init; }

    /// <summary>
    /// Clinical translation feasibility determination.
    /// </summary>
    [Description("Clinical translation feasibility determination.")]
    [JsonPropertyName("clinicalFeasibilityVerdict")]
    public required string ClinicalFeasibilityVerdict { get; init; }

    /// <summary>
    /// Summary of organ biodistribution across compartments (Blood, Pituitary, Liver, Spleen/RES, Depot).
    /// </summary>
    [Description("Summary of organ biodistribution across compartments.")]
    [JsonPropertyName("organBiodistributionSummary")]
    public required string OrganBiodistributionSummary { get; init; }

    /// <summary>
    /// Daily multi-trace kinetics data points over the full simulation horizon.
    /// </summary>
    [Description("Daily multi-trace kinetics data points.")]
    [JsonPropertyName("dailyKinetics")]
    public required IReadOnlyList<PbpkTrajectoryPoint> DailyKinetics { get; init; }

    /// <summary>
    /// Generates a representative example of <see cref="PbpkEndocrineSimulationResult"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated example instance.</returns>
    public PbpkEndocrineSimulationResult GetExamples() => new()
    {
        PatientId = "SUBJECT-001",
        Regimen = TherapeuticRegimen.MaleToFemale,
        SimulationDurationDays = 90,
        DepotDoseMg = 25.0,
        AverageSteadyStateEstradiolPmolL = 542.5,
        AverageSteadyStateTestosteroneNmolL = 1.15,
        AverageLhIuL = 2.1,
        TimeInTargetRangePercent = 97.8,
        TargetSuppressionAchieved = true,
        ThromboticSurgeDetected = false,
        RecommendedDosingIntervalDays = 180,
        ClinicalFeasibilityVerdict = "Feasible as Semi-Annual Subcutaneous Depot (25 mg every 180 days)",
        OrganBiodistributionSummary = "Depot: 52% (Day 90) | Central Plasma: 18% | Pituitary Gonadotropes: 14% | Hepatic Clearance: 11% | Spleen/RES: 5%",
        DailyKinetics = [
            new() { Day = 1, EstradiolPmolL = 220.0, TestosteroneNmolL = 14.5, GonadotropinLhIuL = 6.2, DepotRemainingPercent = 99.5, CentralSerumConcentrationNm = 0.4 },
            new() { Day = 30, EstradiolPmolL = 545.0, TestosteroneNmolL = 1.3, GonadotropinLhIuL = 2.1, DepotRemainingPercent = 84.0, CentralSerumConcentrationNm = 2.1 },
            new() { Day = 60, EstradiolPmolL = 550.0, TestosteroneNmolL = 1.1, GonadotropinLhIuL = 2.0, DepotRemainingPercent = 68.0, CentralSerumConcentrationNm = 2.0 },
            new() { Day = 90, EstradiolPmolL = 540.0, TestosteroneNmolL = 1.1, GonadotropinLhIuL = 2.1, DepotRemainingPercent = 52.0, CentralSerumConcentrationNm = 1.9 }
        ]
    };
}
