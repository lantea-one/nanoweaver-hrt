using System.Text.Json.Serialization;
using WeaverLammpsHrt.TrajectoryParser.Endocrine;

namespace WeaverLammpsHrt.TrajectoryParser.Genomics;

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
public sealed record PatientGenomicProfile
{
    /// <summary>
    /// Anonymized identifier for the patient or research subject.
    /// </summary>
    [JsonPropertyName("patient_id")]
    public required string PatientId { get; init; }

    /// <summary>
    /// The active therapeutic regimen (MaleToFemale or FemaleToMale) being calibrated.
    /// </summary>
    [JsonPropertyName("regimen")]
    public required TherapeuticRegimen Regimen { get; init; }

    /// <summary>
    /// Collection of verified genetic markers present in the patient's genome.
    /// </summary>
    [JsonPropertyName("observed_markers")]
    public required IReadOnlyList<GeneticMarker> ObservedMarkers { get; init; }

    /// <summary>
    /// Active call-out alerts generated from the detected markers in the context of the active regimen.
    /// </summary>
    [JsonPropertyName("alerts")]
    public required IReadOnlyList<GeneticAlert> Alerts { get; init; }

    /// <summary>
    /// Indicates whether one or more Critical severity alerts are active.
    /// </summary>
    [JsonPropertyName("has_critical_alerts")]
    public bool HasCriticalAlerts => Alerts.Any(a => a.Severity == ClinicalSeverity.Critical);

    /// <summary>
    /// Aggregate relative thrombosis risk index (1.0 = baseline population risk; &gt; 2.0 = elevated risk).
    /// </summary>
    [JsonPropertyName("thrombosis_risk_index")]
    public required double ThrombosisRiskIndex { get; init; }

    /// <summary>
    /// Aggregate clearance rate multiplier combining hepatic Phase I and Phase II metabolism variants (1.0 = baseline).
    /// </summary>
    [JsonPropertyName("overall_clearance_multiplier")]
    public required double OverallClearanceMultiplier { get; init; }

    /// <summary>
    /// Indicates whether enzymatic CpG methylation is mandatory on the DNA origami chassis to prevent TLR9 recognition.
    /// </summary>
    [JsonPropertyName("cpg_methylation_mandatory")]
    public required bool CpGMethylationMandatory { get; init; }

    /// <summary>
    /// Safe maximum serum 17β-estradiol concentration (pmol/L) derived from the patient's hemostatic genetic profile.
    /// </summary>
    [JsonPropertyName("recommended_max_estradiol_pmol_l")]
    public double? RecommendedMaxEstradiolPmolL { get; init; }

    /// <summary>
    /// Recommended minimum serum testosterone concentration (nmol/L) required to overcome receptor resistance.
    /// </summary>
    [JsonPropertyName("recommended_min_testosterone_nmol_l")]
    public double? RecommendedMinTestosteroneNmolL { get; init; }

    /// <summary>
    /// Recommended binding affinity Kd offset in nanomolar (nM) to tune aptamer dissociation kinetics.
    /// </summary>
    [JsonPropertyName("recommended_kd_offset_nm")]
    public double RecommendedKdOffsetNanoMolar { get; init; }

    /// <summary>
    /// Timestamp in ISO 8601 UTC when this genomic evaluation was performed.
    /// </summary>
    [JsonPropertyName("calculated_at_utc")]
    public required string CalculatedAtUtc { get; init; }
}

/// <summary>
/// Pre-configured clinical genetic test panel or patient preset for rapid demonstration and validation.
/// </summary>
public sealed record GeneticMarkerPreset
{
    /// <summary>Unique preset identifier.</summary>
    [JsonPropertyName("preset_id")]
    public required string PresetId { get; init; }

    /// <summary>User-friendly clinical preset title.</summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>Biomedical context and clinical profile description.</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>Relevant therapeutic regimen.</summary>
    [JsonPropertyName("regimen")]
    public required TherapeuticRegimen Regimen { get; init; }

    /// <summary>List of marker identifiers included in this preset.</summary>
    [JsonPropertyName("marker_ids")]
    public required IReadOnlyList<string> MarkerIds { get; init; }
}
