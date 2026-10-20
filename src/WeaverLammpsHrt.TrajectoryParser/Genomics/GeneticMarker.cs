using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using WeaverLammpsHrt.TrajectoryParser.Endocrine;

namespace WeaverLammpsHrt.TrajectoryParser.Genomics;

/// <summary>
/// Represents a clinically characterized genetic marker or single nucleotide polymorphism (SNP) 
/// that modulates patient response to hormone replacement therapy or DNA origami delivery.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> A genetic marker is a known variation in human DNA (like a typo in an instruction manual).
/// For example, the Factor V Leiden mutation alters a blood-clotting protein so it cannot be switched off normally, making 
/// estrogen therapy riskier unless the nanocarrier is calibrated for smooth, continuous micro-release.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "GeneticMarker", Description = "Clinically characterized genetic marker or single nucleotide polymorphism (SNP) modulating HRT response.")]
public sealed record GeneticMarker
{
    /// <summary>
    /// Unique catalog identifier for this marker (e.g. 'F5_LEIDEN', 'AR_CAG_LONG').
    /// </summary>
    [Description("Unique catalog identifier for this marker.")]
    [JsonPropertyName("marker_id")]
    public required string MarkerId { get; init; }

    /// <summary>
    /// Official HUGO Gene Nomenclature Committee (HGNC) gene symbol (e.g. 'F5', 'AR', 'COMT', 'TLR9').
    /// </summary>
    [Description("Official HGNC gene symbol.")]
    [JsonPropertyName("gene_symbol")]
    public required string GeneSymbol { get; init; }

    /// <summary>
    /// Common clinical variant name, dbSNP rsID, and amino acid change.
    /// </summary>
    [Description("Common clinical variant name, dbSNP rsID, and amino acid change.")]
    [JsonPropertyName("variant_name")]
    public required string VariantName { get; init; }

    /// <summary>
    /// Physiological domain category affected by this variant.
    /// </summary>
    [Description("Physiological domain category affected by this variant.")]
    [JsonPropertyName("category")]
    public required GeneticCategory Category { get; init; }

    /// <summary>
    /// Clinical severity classification governing alert urgency.
    /// </summary>
    [Description("Clinical severity classification governing alert urgency.")]
    [JsonPropertyName("severity")]
    public required ClinicalSeverity Severity { get; init; }

    /// <summary>
    /// Observed patient genotype or repeat length (e.g. 'G/A Heterozygous', 'CAG Repeats > 26', 'Met/Met').
    /// </summary>
    [Description("Observed patient genotype or repeat length.")]
    [JsonPropertyName("observed_genotype")]
    public required string ObservedGenotype { get; init; }

    /// <summary>
    /// Detailed physiological and pharmacological explanation of the variant's impact.
    /// </summary>
    [Description("Detailed physiological and pharmacological explanation of the variant's impact.")]
    [JsonPropertyName("clinical_description")]
    public required string ClinicalDescription { get; init; }

    /// <summary>
    /// Actionable clinical directive detailing necessary setpoint, aptamer, or fabrication adjustments.
    /// </summary>
    [Description("Actionable clinical directive detailing necessary setpoint, aptamer, or fabrication adjustments.")]
    [JsonPropertyName("recommended_action")]
    public required string RecommendedAction { get; init; }

    /// <summary>
    /// The specific therapeutic regimen affected (MaleToFemale, FemaleToMale, or null if applicable to all).
    /// </summary>
    [Description("The specific therapeutic regimen affected.")]
    [JsonPropertyName("associated_regimen")]
    public TherapeuticRegimen? AssociatedRegimen { get; init; }

    /// <summary>
    /// Clearance rate multiplier modifying systemic hormone half-life (1.0 = baseline normal; &gt; 1.0 = rapid clearance; &lt; 1.0 = impaired clearance).
    /// </summary>
    [Description("Clearance rate multiplier modifying systemic hormone half-life.")]
    [JsonPropertyName("clearance_rate_multiplier")]
    public double ClearanceRateMultiplier { get; init; } = 1.0;

    /// <summary>
    /// Binding affinity offset in nanomolar (nM) to tune aptamer gating for altered receptor density or co-activator dynamics.
    /// </summary>
    [Description("Binding affinity offset in nM to tune aptamer gating.")]
    [JsonPropertyName("target_kd_offset_nm")]
    public double TargetKdOffsetNanoMolar { get; init; } = 0.0;

    /// <summary>
    /// Maximum recommended serum 17β-estradiol concentration in pmol/L to prevent thrombotic complications.
    /// </summary>
    [Description("Maximum recommended serum 17β-estradiol concentration in pmol/L.")]
    [JsonPropertyName("max_safe_estradiol_pmol_l")]
    public double? MaxSafeEstradiolPmolL { get; init; }

    /// <summary>
    /// Minimum recommended serum testosterone concentration in nmol/L to overcome receptor resistance.
    /// </summary>
    [Description("Minimum recommended serum testosterone concentration in nmol/L.")]
    [JsonPropertyName("min_recommended_testosterone_nmol_l")]
    public double? MinRecommendedTestosteroneNmolL { get; init; }

    /// <summary>
    /// Venous thromboembolism relative risk multiplier (1.0 = population baseline).
    /// </summary>
    [Description("Venous thromboembolism relative risk multiplier.")]
    [JsonPropertyName("thrombosis_risk_multiplier")]
    public double ThrombosisRiskMultiplier { get; init; } = 1.0;

    /// <summary>
    /// Indicates whether enzymatic CpG methylation is mandatory on the DNA scaffold to bypass Toll-like receptor 9.
    /// </summary>
    [Description("Indicates whether enzymatic CpG methylation is mandatory on the DNA scaffold.")]
    [JsonPropertyName("requires_cpg_methylation")]
    public bool RequiresCpGMethylation { get; init; } = false;
}
