using System.Text.Json.Serialization;

namespace WeaverLammpsHrt.TrajectoryParser.Genomics;

/// <summary>
/// An actionable clinical call-out alert generated when a patient's genetic profile contains 
/// variants that influence HRT outcomes, nanocarrier stability, or thrombosis risk.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> Call-out alerts are prominent visual warnings shown in the UI.
/// They explain exactly what genetic predisposition was found, why it matters, and how the biophysical 
/// parameters of the DNA nanocarrier or hormone dosage must be adjusted to keep the patient safe.
/// </para>
/// </remarks>
public sealed record GeneticAlert
{
    /// <summary>
    /// Unique identifier for this alert instance.
    /// </summary>
    [JsonPropertyName("alert_id")]
    public required string AlertId { get; init; }

    /// <summary>
    /// HGNC gene symbol associated with the alert.
    /// </summary>
    [JsonPropertyName("gene_symbol")]
    public required string GeneSymbol { get; init; }

    /// <summary>
    /// Specific variant or mutation name.
    /// </summary>
    [JsonPropertyName("variant_name")]
    public required string VariantName { get; init; }

    /// <summary>
    /// Urgency and severity tier of the call-out (Critical, Warning, Advisory, Optimal).
    /// </summary>
    [JsonPropertyName("severity")]
    public required ClinicalSeverity Severity { get; init; }

    /// <summary>
    /// Physiological domain category of the alert.
    /// </summary>
    [JsonPropertyName("category")]
    public required GeneticCategory Category { get; init; }

    /// <summary>
    /// Short summary title displayed on the call-out banner.
    /// </summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>
    /// Primary high-contrast call-out message highlighting the genetic finding.
    /// </summary>
    [JsonPropertyName("call_out_message")]
    public required string CallOutMessage { get; init; }

    /// <summary>
    /// In-depth clinical advice regarding endocrine physiology and systemic safety.
    /// </summary>
    [JsonPropertyName("clinical_guidance")]
    public required string ClinicalGuidance { get; init; }

    /// <summary>
    /// Specific instructions for adjusting DNA origami chassis design, staple routing, or CpG methylation.
    /// </summary>
    [JsonPropertyName("scaffold_guidance")]
    public string? ScaffoldGuidance { get; init; }

    /// <summary>
    /// Specific instructions for altering delivery kinetics, plateau half-life, or aptamer Kd.
    /// </summary>
    [JsonPropertyName("kinetic_guidance")]
    public string? KineticGuidance { get; init; }
}
