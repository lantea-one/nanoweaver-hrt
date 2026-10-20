using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using WeaverLammpsHrt.TrajectoryParser.Endocrine;
using WeaverLammpsHrt.TrajectoryParser.Swagger;

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
public sealed record GeneticMarker : IExamplesProvider<GeneticMarker>
{
    /// <summary>
    /// Unique catalog identifier for this marker (e.g. 'F5_LEIDEN', 'AR_CAG_LONG').
    /// </summary>
    [Description("Unique catalog identifier for this marker.")]
    [JsonPropertyName("markerId")]
    public required string MarkerId { get; init; }

    /// <summary>
    /// Official HUGO Gene Nomenclature Committee (HGNC) gene symbol (e.g. 'F5', 'AR', 'COMT', 'TLR9').
    /// </summary>
    [Description("Official HGNC gene symbol.")]
    [JsonPropertyName("geneSymbol")]
    public required string GeneSymbol { get; init; }

    /// <summary>
    /// Common clinical variant name, dbSNP rsID, and amino acid change.
    /// </summary>
    [Description("Common clinical variant name, dbSNP rsID, and amino acid change.")]
    [JsonPropertyName("variantName")]
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
    [JsonPropertyName("observedGenotype")]
    public required string ObservedGenotype { get; init; }

    /// <summary>
    /// Detailed physiological and pharmacological explanation of the variant's impact.
    /// </summary>
    [Description("Detailed physiological and pharmacological explanation of the variant's impact.")]
    [JsonPropertyName("clinicalDescription")]
    public required string ClinicalDescription { get; init; }

    /// <summary>
    /// Actionable clinical directive detailing necessary setpoint, aptamer, or fabrication adjustments.
    /// </summary>
    [Description("Actionable clinical directive detailing necessary setpoint, aptamer, or fabrication adjustments.")]
    [JsonPropertyName("recommendedAction")]
    public required string RecommendedAction { get; init; }

    /// <summary>
    /// The specific therapeutic regimen affected (MaleToFemale, FemaleToMale, or null if applicable to all).
    /// </summary>
    [Description("The specific therapeutic regimen affected.")]
    [JsonPropertyName("associatedRegimen")]
    public TherapeuticRegimen? AssociatedRegimen { get; init; }

    /// <summary>
    /// Clearance rate multiplier modifying systemic hormone half-life (1.0 = baseline normal; &gt; 1.0 = rapid clearance; &lt; 1.0 = impaired clearance).
    /// </summary>
    [Description("Clearance rate multiplier modifying systemic hormone half-life.")]
    [JsonPropertyName("clearanceRateMultiplier")]
    public double ClearanceRateMultiplier { get; init; } = 1.0;

    /// <summary>
    /// Binding affinity offset in nanomolar (nM) to tune aptamer gating for altered receptor density or co-activator dynamics.
    /// </summary>
    [Description("Binding affinity offset in nM to tune aptamer gating.")]
    [JsonPropertyName("targetKdOffsetNm")]
    public double TargetKdOffsetNanoMolar { get; init; } = 0.0;

    /// <summary>
    /// Maximum recommended serum 17β-estradiol concentration in pmol/L to prevent thrombotic complications.
    /// </summary>
    [Description("Maximum recommended serum 17β-estradiol concentration in pmol/L.")]
    [JsonPropertyName("maxSafeEstradiolPmolL")]
    public double? MaxSafeEstradiolPmolL { get; init; }

    /// <summary>
    /// Minimum recommended serum testosterone concentration in nmol/L to overcome receptor resistance.
    /// </summary>
    [Description("Minimum recommended serum testosterone concentration in nmol/L.")]
    [JsonPropertyName("minRecommendedTestosteroneNmolL")]
    public double? MinRecommendedTestosteroneNmolL { get; init; }

    /// <summary>
    /// Venous thromboembolism relative risk multiplier (1.0 = population baseline).
    /// </summary>
    [Description("Venous thromboembolism relative risk multiplier.")]
    [JsonPropertyName("thrombosisRiskMultiplier")]
    public double ThrombosisRiskMultiplier { get; init; } = 1.0;

    /// <summary>
    /// Indicates whether enzymatic CpG methylation is mandatory on the DNA scaffold to bypass Toll-like receptor 9.
    /// </summary>
    [Description("Indicates whether enzymatic CpG methylation is mandatory on the DNA scaffold.")]
    [JsonPropertyName("requiresCpgMethylation")]
    public bool RequiresCpGMethylation { get; init; } = false;

    /// <summary>
    /// Generates a default populated instance of <see cref="GeneticMarker"/> for examples.
    /// </summary>
    /// <returns>A populated genetic marker example.</returns>
    public static GeneticMarker CreateExample() => new()
    {
        MarkerId = "F5_LEIDEN",
        GeneSymbol = "F5",
        VariantName = "Factor V Leiden (c.1691G>A / p.Arg506Gln)",
        Category = GeneticCategory.ThromboticRisk,
        Severity = ClinicalSeverity.Critical,
        ObservedGenotype = "G/A Heterozygous",
        ClinicalDescription = "Resistance to activated protein C (APC) leading to hypercoagulability under exogenous estrogen exposure.",
        RecommendedAction = "Cap 17β-estradiol target setpoint at 450 pmol/L. Utilize slow-release nanocarrier.",
        AssociatedRegimen = TherapeuticRegimen.MaleToFemale,
        ClearanceRateMultiplier = 1.0,
        TargetKdOffsetNanoMolar = 0.0,
        MaxSafeEstradiolPmolL = 450.0,
        MinRecommendedTestosteroneNmolL = null,
        ThrombosisRiskMultiplier = 4.5,
        RequiresCpGMethylation = false
    };

    /// <summary>
    /// Generates a representative example instance of <see cref="GeneticMarker"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated genetic marker example.</returns>
    public GeneticMarker GetExamples() => CreateExample();
}
