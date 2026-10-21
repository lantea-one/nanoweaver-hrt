using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Genomics;

/// <summary>
/// Comprehensive pharmacogenomic profile for a specific patient, aggregating detected genetic markers,
/// active call-out alerts, and computed biophysical kinetic modifiers.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> This profile represents the genetic passport of the patient.
/// It summarizes all detected gene variants and computes overall multiplier adjustments so the DNA 
/// nanocarrier's release kinetics and target setpoints are safely customized for their body.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "PatientGenomicProfile", Description = "Comprehensive pharmacogenomic profile aggregating detected genetic markers, alerts, and kinetic modifiers.")]
public sealed record PatientGenomicProfile : IExamplesProvider<PatientGenomicProfile>
{
    /// <summary>
    /// Anonymized identifier for the patient or research subject.
    /// </summary>
    [Description("Anonymized identifier for the patient or research subject.")]
    [JsonPropertyName("patientId")]
    public required string PatientId { get; init; }

    /// <summary>
    /// The active therapeutic regimen (MaleToFemale or FemaleToMale) being calibrated.
    /// </summary>
    [Description("The active therapeutic regimen (MaleToFemale or FemaleToMale).")]
    [JsonPropertyName("regimen")]
    public required TherapeuticRegimen Regimen { get; init; }

    /// <summary>
    /// Collection of verified genetic markers present in the patient's genome.
    /// </summary>
    [Description("Collection of verified genetic markers present in the patient's genome.")]
    [JsonPropertyName("observedMarkers")]
    public required IReadOnlyList<GeneticMarker> ObservedMarkers { get; init; }

    /// <summary>
    /// Active call-out alerts generated from the detected markers in the context of the active regimen.
    /// </summary>
    [Description("Active call-out alerts generated from the detected markers.")]
    [JsonPropertyName("alerts")]
    public required IReadOnlyList<GeneticAlert> Alerts { get; init; }

    /// <summary>
    /// Indicates whether one or more Critical severity alerts are active.
    /// </summary>
    [Description("Indicates whether one or more Critical severity alerts are active.")]
    [JsonPropertyName("hasCriticalAlerts")]
    public bool HasCriticalAlerts => Alerts.Any(a => a.Severity == ClinicalSeverity.Critical);

    /// <summary>
    /// Aggregate relative thrombosis risk index (1.0 = baseline population risk; &gt; 2.0 = elevated risk).
    /// </summary>
    [Description("Aggregate relative thrombosis risk index (1.0 = population baseline).")]
    [JsonPropertyName("thrombosisRiskIndex")]
    public required double ThrombosisRiskIndex { get; init; }

    /// <summary>
    /// Aggregate clearance rate multiplier combining hepatic Phase I and Phase II metabolism variants (1.0 = baseline).
    /// </summary>
    [Description("Aggregate clearance rate multiplier combining Phase I and Phase II metabolism.")]
    [JsonPropertyName("overallClearanceMultiplier")]
    public required double OverallClearanceMultiplier { get; init; }

    /// <summary>
    /// Indicates whether enzymatic CpG methylation is mandatory on the DNA origami chassis to prevent TLR9 recognition.
    /// </summary>
    [Description("Indicates whether enzymatic CpG methylation is mandatory on the DNA origami chassis.")]
    [JsonPropertyName("cpgMethylationMandatory")]
    public required bool CpGMethylationMandatory { get; init; }

    /// <summary>
    /// Safe maximum serum 17β-estradiol concentration (pmol/L) derived from the patient's hemostatic genetic profile.
    /// </summary>
    [Description("Safe maximum serum 17β-estradiol concentration (pmol/L).")]
    [JsonPropertyName("recommendedMaxEstradiolPmolL")]
    public double? RecommendedMaxEstradiolPmolL { get; init; }

    /// <summary>
    /// Recommended minimum serum testosterone concentration (nmol/L) required to overcome receptor resistance.
    /// </summary>
    [Description("Recommended minimum serum testosterone concentration (nmol/L).")]
    [JsonPropertyName("recommendedMinTestosteroneNmolL")]
    public double? RecommendedMinTestosteroneNmolL { get; init; }

    /// <summary>
    /// Recommended binding affinity Kd offset in nanomolar (nM) to tune aptamer dissociation kinetics.
    /// </summary>
    [Description("Recommended binding affinity Kd offset in nM.")]
    [JsonPropertyName("recommendedKdOffsetNm")]
    public double RecommendedKdOffsetNanoMolar { get; init; }

    /// <summary>
    /// Timestamp in ISO 8601 UTC when this genomic evaluation was performed.
    /// </summary>
    [Description("Timestamp in ISO 8601 UTC when this genomic evaluation was performed.")]
    [JsonPropertyName("calculatedAtUtc")]
    public required string CalculatedAtUtc { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="PatientGenomicProfile"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated patient genomic profile example.</returns>
    public PatientGenomicProfile GetExamples() => new()
    {
        PatientId = "sub_patient_42c8e",
        Regimen = TherapeuticRegimen.MaleToFemale,
        ObservedMarkers = [GeneticMarker.CreateExample()],
        Alerts = [GeneticAlert.CreateExample()],
        ThrombosisRiskIndex = 4.5,
        OverallClearanceMultiplier = 1.0,
        CpGMethylationMandatory = false,
        RecommendedMaxEstradiolPmolL = 450.0,
        RecommendedMinTestosteroneNmolL = null,
        RecommendedKdOffsetNanoMolar = 0.0,
        CalculatedAtUtc = "2026-10-20T18:00:00Z"
    };
}

/// <summary>
/// Pre-configured clinical genetic test panel or patient preset for rapid demonstration and validation.
/// </summary>
[SwaggerSchema(Title = "GeneticMarkerPreset", Description = "Pre-configured clinical genetic test panel for rapid demonstration.")]
public sealed record GeneticMarkerPreset : IExamplesProvider<GeneticMarkerPreset>
{
    /// <summary>Unique preset identifier.</summary>
    [Description("Unique preset identifier.")]
    [JsonPropertyName("presetId")]
    public required string PresetId { get; init; }

    /// <summary>User-friendly clinical preset title.</summary>
    [Description("User-friendly clinical preset title.")]
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>Biomedical context and clinical profile description.</summary>
    [Description("Biomedical context and clinical profile description.")]
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>Relevant therapeutic regimen.</summary>
    [Description("Relevant therapeutic regimen.")]
    [JsonPropertyName("regimen")]
    public required TherapeuticRegimen Regimen { get; init; }

    /// <summary>List of marker identifiers included in this preset.</summary>
    [Description("List of marker identifiers included in this preset.")]
    [JsonPropertyName("markerIds")]
    public required IReadOnlyList<string> MarkerIds { get; init; }

    /// <summary>
    /// Generates a default populated instance of <see cref="GeneticMarkerPreset"/> for examples.
    /// </summary>
    /// <returns>A populated genetic marker preset example.</returns>
    public static GeneticMarkerPreset CreateExample() => new()
    {
        PresetId = "preset-mtf-thrombosis",
        Title = "MtF High Thrombosis Risk Panel (Factor V Leiden + Prothrombin)",
        Description = "Simulates a patient carrying hemostatic clotting variants requiring safe estradiol titration.",
        Regimen = TherapeuticRegimen.MaleToFemale,
        MarkerIds = ["F5_LEIDEN", "F2_PROTHROMBIN"]
    };

    /// <summary>
    /// Generates a representative example instance of <see cref="GeneticMarkerPreset"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated genetic marker preset example.</returns>
    public GeneticMarkerPreset GetExamples() => CreateExample();
}
