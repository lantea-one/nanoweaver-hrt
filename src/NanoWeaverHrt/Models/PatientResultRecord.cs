using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Fabrication;
using NanoWeaverHrt.Genomics;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Persistent record representing computational simulation, biophysical endocrine kinetics, or pharmacogenomic analysis
/// results linked to a patient and optionally to a specific digitized DNA blueprint or imported sequence.
/// </summary>
public sealed record PatientResultRecord : IExamplesProvider<PatientResultRecord>
{
    /// <summary>
    /// Gets or initializes the unique identifier for this result record.
    /// </summary>
    [Description("Unique identifier for this result record.")]
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the username of the patient associated with this result.
    /// </summary>
    [Description("Username of the patient associated with this result.")]
    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes an optional foreign reference to an associated <see cref="PatientDigitalDnaRecord"/>.
    /// </summary>
    [Description("Optional reference to an associated digitized DNA blueprint.")]
    [JsonPropertyName("dnaRecordId")]
    public string? DnaRecordId { get; init; }

    /// <summary>
    /// Gets or initializes an optional foreign reference to an associated <see cref="PatientDnaImportRecord"/>.
    /// </summary>
    [Description("Optional reference to an associated DNA import record.")]
    [JsonPropertyName("importId")]
    public string? ImportId { get; init; }

    /// <summary>
    /// Gets or initializes the categorization type of the result (e.g. EndocrineKinetics, Pharmacogenomics, AptamerAffinity, SimulationTrajectory).
    /// </summary>
    [Description("Result category type (e.g. EndocrineKinetics, Pharmacogenomics, AptamerAffinity, SimulationTrajectory).")]
    [JsonPropertyName("resultType")]
    public string ResultType { get; init; } = "EndocrineKinetics";

    /// <summary>
    /// Gets or initializes the clinical or computational title of this result.
    /// </summary>
    [Description("Clinical or computational title of this result.")]
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the clinical outcome status (e.g. Optimal, Caution, ElevatedRisk, Complete, Verified).
    /// </summary>
    [Description("Outcome status (e.g. Optimal, Caution, ElevatedRisk, Complete, Verified).")]
    [JsonPropertyName("status")]
    public string Status { get; init; } = "Complete";

    /// <summary>
    /// Gets or initializes serialized JSON key-value quantitative biophysical or biochemical metrics.
    /// </summary>
    [Description("Serialized JSON key-value quantitative metrics.")]
    [JsonPropertyName("metricsJson")]
    public string MetricsJson { get; init; } = "{}";

    /// <summary>
    /// Gets or initializes a human-readable, plain-English summary of the clinical or biophysical findings.
    /// </summary>
    [Description("Plain-English summary of clinical or biophysical findings.")]
    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the UTC timestamp when the result was generated.
    /// </summary>
    [Description("UTC timestamp when the result was generated.")]
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Deserializes the <see cref="MetricsJson"/> into a read-only dictionary of string key-value pairs.
    /// </summary>
    /// <returns>A dictionary of metric names and formatted values.</returns>
    public IReadOnlyDictionary<string, string> GetMetrics()
    {
        // WHAT: Parses the serialized JSON metrics blob into an accessible key-value map.
        // WHY:  Enables UI components and clinical reports to render metric badges without repetitive JSON deserialization.
        // HOW:  Uses System.Text.Json.JsonSerializer with fallback to an empty dictionary on parse failures.
        if (string.IsNullOrWhiteSpace(MetricsJson))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(MetricsJson)
                ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Factory helper that produces a <see cref="PatientResultRecord"/> from a <see cref="PatientGenomicProfile"/>.
    /// </summary>
    /// <param name="profile">The evaluated genomic profile.</param>
    /// <param name="importId">Optional ID of the originating import record.</param>
    /// <returns>A populated patient result record.</returns>
    public static PatientResultRecord FromGenomicProfile(PatientGenomicProfile profile, string? importId = null)
    {
        // WHAT: Transforms a pharmacogenomics profile evaluation into a patient-associated persistent result record.
        // WHY:  Retains historical genomic risk scores and active clinical alerts in the patient record.
        // HOW:  Serializes thrombosis risk, clearance multipliers, and alerts into metrics JSON and status badge.
        ArgumentNullException.ThrowIfNull(profile);

        string status = profile.HasCriticalAlerts ? "ElevatedRisk" : (profile.Alerts.Count > 0 ? "Caution" : "Optimal");
        var metrics = new Dictionary<string, string>
        {
            ["ThrombosisRiskIndex"] = profile.ThrombosisRiskIndex.ToString("F2"),
            ["ClearanceMultiplier"] = profile.OverallClearanceMultiplier.ToString("F2"),
            ["CpGMethylationMandatory"] = profile.CpGMethylationMandatory ? "True" : "False",
            ["MaxSafeE2PmolL"] = profile.RecommendedMaxEstradiolPmolL?.ToString("F1") ?? "N/A",
            ["AlertCount"] = profile.Alerts.Count.ToString(),
            ["ObservedMarkersCount"] = profile.ObservedMarkers.Count.ToString()
        };

        string summary = profile.Alerts.Count > 0
            ? $"Identified {profile.Alerts.Count} clinical pharmacogenomic alert(s). Thrombosis risk index: {profile.ThrombosisRiskIndex:F2}x baseline; clearance multiplier: {profile.OverallClearanceMultiplier:F2}x."
            : $"Genomic screen optimal with zero adverse alerts. Baseline clearance multiplier: {profile.OverallClearanceMultiplier:F2}x.";

        return new PatientResultRecord
        {
            Id = "res_" + Guid.NewGuid().ToString("N"),
            Username = profile.PatientId,
            DnaRecordId = null,
            ImportId = importId,
            ResultType = "Pharmacogenomics",
            Title = $"Pharmacogenomic Profile ({profile.Regimen})",
            Status = status,
            MetricsJson = JsonSerializer.Serialize(metrics),
            Summary = summary,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Factory helper that produces a <see cref="PatientResultRecord"/> from an <see cref="EndocrineTargetProfile"/>.
    /// </summary>
    /// <param name="profile">The target endocrine profile.</param>
    /// <param name="patientUsername">The target patient username.</param>
    /// <param name="dnaRecordId">Optional ID of the associated digitized DNA record.</param>
    /// <returns>A populated patient result record.</returns>
    public static PatientResultRecord FromEndocrineProfile(EndocrineTargetProfile profile, string patientUsername, string? dnaRecordId = null)
    {
        // WHAT: Creates a persistent result record from an endocrine steady-state target evaluation.
        // WHY:  Tracks hormone concentration targets and receptor dissociation constants for patient review.
        // HOW:  Extracts target concentrations, suppression half-lives, and binding affinity Kd values.
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(patientUsername);

        var metrics = new Dictionary<string, string>
        {
            ["TargetE2PmolL"] = profile.TargetEstradiolPmolL.ToString("F1"),
            ["TargetTestosteroneNmolL"] = profile.TargetTestosteroneNmolL.ToString("F1"),
            ["ReceptorKdNm"] = profile.BindingAffinityKdTargetNanoMolar.ToString("F2"),
            ["TargetTissue"] = profile.TargetTissue.ToString()
        };

        return new PatientResultRecord
        {
            Id = "res_" + Guid.NewGuid().ToString("N"),
            Username = patientUsername,
            DnaRecordId = dnaRecordId,
            ImportId = null,
            ResultType = "EndocrineKinetics",
            Title = $"Target Endocrine Setpoints ({profile.Regimen})",
            Status = "Optimal",
            MetricsJson = JsonSerializer.Serialize(metrics),
            Summary = $"Target setpoints calibrated for {profile.TargetTissue}: E2 target {profile.TargetEstradiolPmolL:F1} pmol/L, T target {profile.TargetTestosteroneNmolL:F1} nmol/L with Kd {profile.BindingAffinityKdTargetNanoMolar:F2} nM.",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Factory helper that produces a <see cref="PatientResultRecord"/> from a <see cref="FabricationPackage"/>.
    /// </summary>
    /// <param name="package">The fabrication package.</param>
    /// <param name="patientUsername">The target patient username.</param>
    /// <param name="dnaRecordId">Optional ID of the associated digitized DNA record.</param>
    /// <returns>A populated patient result record.</returns>
    public static PatientResultRecord FromFabricationPackage(FabricationPackage package, string patientUsername, string? dnaRecordId = null)
    {
        // WHAT: Constructs a result record summarizing synthesis yield and thermodynamic parameters from a fabrication package.
        // WHY:  Maintains fabrication quality metrics alongside the patient's digitized DNA package.
        // HOW:  Extracts staple count, ramp stages, buffer conditions, and SHA-256 provenance manifest.
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(patientUsername);

        var metrics = new Dictionary<string, string>
        {
            ["StapleCount"] = package.StapleOligos.Count.ToString(),
            ["AnnealingStages"] = package.AnnealingSchedule.Count.ToString(),
            ["BufferRecipe"] = package.FoldingBufferRecipe,
            ["ExpectedRadiusNm"] = package.ExpectedHydrodynamicRadiusNm.ToString("F1"),
            ["ProvenanceSha256"] = package.Sha256Manifest
        };

        return new PatientResultRecord
        {
            Id = "res_" + Guid.NewGuid().ToString("N"),
            Username = patientUsername,
            DnaRecordId = dnaRecordId,
            ImportId = null,
            ResultType = "SynthesisFabrication",
            Title = $"Origami Fabrication Blueprint ({package.TargetTissue})",
            Status = "Complete",
            MetricsJson = JsonSerializer.Serialize(metrics),
            Summary = $"Generated {package.StapleOligos.Count} synthetic staple strands across {package.AnnealingSchedule.Count} annealing ramp stages with {package.FoldingBufferRecipe} assembly buffer.",
            CreatedAtUtc = new DateTimeOffset(package.CreatedAtUtc, TimeSpan.Zero)
        };
    }

    /// <summary>
    /// Generates a representative example instance of <see cref="PatientResultRecord"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated patient result record.</returns>
    public PatientResultRecord GetExamples() => new()
    {
        Id = "res_example_01",
        Username = "patient_alice",
        DnaRecordId = "dna_example_01",
        ImportId = "imp_example_01",
        ResultType = "EndocrineKinetics",
        Title = "Endocrine Steady-State Target Kinetics",
        Status = "Optimal",
        MetricsJson = "{\"TargetE2PmolL\":\"650.0\",\"ReceptorKdNm\":\"3.50\"}",
        Summary = "Target setpoints calibrated for Anterior Pituitary Gonadotrope: E2 target 650.0 pmol/L with Kd 3.50 nM.",
        CreatedAtUtc = DateTimeOffset.UtcNow
    };
}
