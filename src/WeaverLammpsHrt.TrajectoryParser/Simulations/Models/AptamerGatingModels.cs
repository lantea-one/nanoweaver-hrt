using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using WeaverLammpsHrt.TrajectoryParser.Swagger;

namespace WeaverLammpsHrt.TrajectoryParser.Simulations.Models;

/// <summary>
/// Execution request for Campaign 2: LAMMPS Aptamer Gating Free-Energy &amp; Pituitary Receptor Tropism.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> This simulation calculates the physical energy needed to pop open the
/// nanocarrier's latch. It confirms that the door stays locked when hormone levels are fine, and swings open when
/// LH/FSH signals arrive, while testing if the nanocarrier latches onto pituitary cells.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "AptamerGatingRequest", Description = "Request payload for LAMMPS aptamer gating and receptor tropism simulation.")]
public sealed record AptamerGatingRequest : IExamplesProvider<AptamerGatingRequest>
{
    /// <summary>
    /// Target cellular receptor targeted for nanocarrier organ homing.
    /// </summary>
    [Description("Target cellular receptor targeted for nanocarrier organ homing.")]
    [JsonPropertyName("targetReceptor")]
    public string TargetReceptor { get; init; } = "Anterior Pituitary Gonadotrope (GnRH-R)";

    /// <summary>
    /// Stimulus gonadotropin hormone triggering allosteric gate opening.
    /// </summary>
    [Description("Stimulus gonadotropin hormone triggering allosteric gate opening.")]
    [JsonPropertyName("targetGonadotropin")]
    public string TargetGonadotropin { get; init; } = "Luteinizing Hormone (LH)";

    /// <summary>
    /// Custom sequence of the aptamer latch oligonucleotide.
    /// </summary>
    [Description("Custom sequence of the aptamer latch oligonucleotide.")]
    [JsonPropertyName("aptamerSequence")]
    public string AptamerSequence { get; init; } = "AGCTCAGGTACCGATCGTTTACCGGATACCTTTCGATCGGAT";

    /// <summary>
    /// Number of umbrella sampling simulation windows along reaction coordinate (default 24).
    /// </summary>
    [Description("Number of umbrella sampling simulation windows along reaction coordinate.")]
    [JsonPropertyName("umbrellaSamplingWindows")]
    public int UmbrellaSamplingWindows { get; init; } = 24;

    /// <summary>
    /// Maximum extension distance in nanometers along pulling reaction coordinate.
    /// </summary>
    [Description("Maximum extension distance in nanometers along pulling reaction coordinate.")]
    [JsonPropertyName("reactionCoordinateMaxNm")]
    public double ReactionCoordinateMaxNm { get; init; } = 3.0;

    /// <summary>
    /// Generates a representative example of <see cref="AptamerGatingRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated example instance.</returns>
    public AptamerGatingRequest GetExamples() => new()
    {
        TargetReceptor = "Anterior Pituitary Gonadotrope (GnRH-R)",
        TargetGonadotropin = "Luteinizing Hormone (LH)",
        AptamerSequence = "AGCTCAGGTACCGATCGTTTACCGGATACCTTTCGATCGGAT",
        UmbrellaSamplingWindows = 24,
        ReactionCoordinateMaxNm = 3.0
    };
}

/// <summary>
/// A single point along the Potential of Mean Force (PMF) free-energy curve.
/// </summary>
[SwaggerSchema(Title = "FreeEnergyPmfPoint", Description = "A single point along the Potential of Mean Force free-energy curve.")]
public sealed record FreeEnergyPmfPoint
{
    /// <summary>
    /// Separation distance along reaction coordinate in nanometers.
    /// </summary>
    [Description("Separation distance along reaction coordinate in nanometers.")]
    [JsonPropertyName("reactionCoordinateNm")]
    public required double ReactionCoordinateNm { get; init; }

    /// <summary>
    /// Free energy in kcal/mol when no stimulus hormone is bound (unbound state).
    /// </summary>
    [Description("Free energy in kcal/mol when no stimulus hormone is bound.")]
    [JsonPropertyName("unboundPmfKcalMol")]
    public required double UnboundPmfKcalMol { get; init; }

    /// <summary>
    /// Free energy in kcal/mol when stimulus gonadotropin is actively bound (bound state).
    /// </summary>
    [Description("Free energy in kcal/mol when stimulus gonadotropin is actively bound.")]
    [JsonPropertyName("boundPmfKcalMol")]
    public required double BoundPmfKcalMol { get; init; }
}

/// <summary>
/// Outcome of Campaign 2: LAMMPS Aptamer Gating Free-Energy &amp; Receptor Tropism Simulation.
/// </summary>
[SwaggerSchema(Title = "AptamerGatingAnalysisResult", Description = "Outcome of LAMMPS aptamer gating and receptor tropism simulation.")]
public sealed record AptamerGatingAnalysisResult : IExamplesProvider<AptamerGatingAnalysisResult>
{
    /// <summary>
    /// Cellular receptor targeted for tropism.
    /// </summary>
    [Description("Cellular receptor targeted for tropism.")]
    [JsonPropertyName("targetReceptor")]
    public required string TargetReceptor { get; init; }

    /// <summary>
    /// Stimulus hormone triggering allosteric opening.
    /// </summary>
    [Description("Stimulus hormone triggering allosteric opening.")]
    [JsonPropertyName("targetGonadotropin")]
    public required string TargetGonadotropin { get; init; }

    /// <summary>
    /// Free energy barrier required to open latch without stimulus (target &gt;= +8.5 kcal/mol).
    /// </summary>
    [Description("Free energy barrier required to open latch without stimulus.")]
    [JsonPropertyName("unboundOpeningFreeEnergyKcalMol")]
    public required double UnboundOpeningFreeEnergyKcalMol { get; init; }

    /// <summary>
    /// Free energy barrier to open latch when gonadotropin is bound (target &lt;= 0.0 kcal/mol).
    /// </summary>
    [Description("Free energy barrier to open latch when gonadotropin is bound.")]
    [JsonPropertyName("boundOpeningFreeEnergyKcalMol")]
    public required double BoundOpeningFreeEnergyKcalMol { get; init; }

    /// <summary>
    /// Allosteric switching delta (Bound minus Unbound free energy).
    /// </summary>
    [Description("Allosteric switching delta.")]
    [JsonPropertyName("allostericSwitchDeltaKcalMol")]
    public required double AllostericSwitchDeltaKcalMol { get; init; }

    /// <summary>
    /// Fraction of targeted receptor interface residues in active contact (&lt; 5.0 Angstroms).
    /// </summary>
    [Description("Fraction of targeted receptor interface residues in active contact.")]
    [JsonPropertyName("gnRhReceptorContactRatio")]
    public required double GnRhReceptorContactRatio { get; init; }

    /// <summary>
    /// Estimated thermodynamic dissociation constant (Kd) in nanomolar against target tissue.
    /// </summary>
    [Description("Estimated thermodynamic dissociation constant (Kd) in nanomolar.")]
    [JsonPropertyName("estimatedKdNanoMolar")]
    public required double EstimatedKdNanoMolar { get; init; }

    /// <summary>
    /// Latch gating mechanism determination verdict.
    /// </summary>
    [Description("Latch gating mechanism determination verdict.")]
    [JsonPropertyName("gatingVerdict")]
    public required string GatingVerdict { get; init; }

    /// <summary>
    /// Receptor tropism determination verdict.
    /// </summary>
    [Description("Receptor tropism determination verdict.")]
    [JsonPropertyName("tropismVerdict")]
    public required string TropismVerdict { get; init; }

    /// <summary>
    /// Complete Potential of Mean Force (PMF) energy profile curve.
    /// </summary>
    [Description("Complete Potential of Mean Force energy profile curve.")]
    [JsonPropertyName("pmfCurve")]
    public required IReadOnlyList<FreeEnergyPmfPoint> PmfCurve { get; init; }

    /// <summary>
    /// Generates a representative example of <see cref="AptamerGatingAnalysisResult"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated example instance.</returns>
    public AptamerGatingAnalysisResult GetExamples() => new()
    {
        TargetReceptor = "Anterior Pituitary Gonadotrope (GnRH-R)",
        TargetGonadotropin = "Luteinizing Hormone (LH)",
        UnboundOpeningFreeEnergyKcalMol = 9.2,
        BoundOpeningFreeEnergyKcalMol = -2.1,
        AllostericSwitchDeltaKcalMol = -11.3,
        GnRhReceptorContactRatio = 0.88,
        EstimatedKdNanoMolar = 4.1,
        GatingVerdict = "Allosterically Responsive & Securely Latched",
        TropismVerdict = "High-Affinity Gonadotrope Tropism Verified",
        PmfCurve = [
            new() { ReactionCoordinateNm = 0.5, UnboundPmfKcalMol = 0.0, BoundPmfKcalMol = 0.0 },
            new() { ReactionCoordinateNm = 1.0, UnboundPmfKcalMol = 2.4, BoundPmfKcalMol = -0.5 },
            new() { ReactionCoordinateNm = 1.5, UnboundPmfKcalMol = 5.6, BoundPmfKcalMol = -1.2 },
            new() { ReactionCoordinateNm = 2.0, UnboundPmfKcalMol = 7.9, BoundPmfKcalMol = -1.8 },
            new() { ReactionCoordinateNm = 2.5, UnboundPmfKcalMol = 9.2, BoundPmfKcalMol = -2.1 }
        ]
    };
}
