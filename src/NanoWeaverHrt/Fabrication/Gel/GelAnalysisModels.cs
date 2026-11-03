using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Fabrication.Gel;

/// <summary>
/// Supported DNA ladder standards for agarose gel electrophoresis densitometry and base pair size calibration.
/// </summary>
public enum GelLadderType
{
    /// <summary>
    /// Standard 1 kb DNA ladder with known reference bands from 500 bp to 10,000 bp.
    /// </summary>
    OneKbDnaLadder,

    /// <summary>
    /// Single-stranded M13mp18 bacteriophage scaffold reference (7,249 nt) and associated multimeric forms.
    /// </summary>
    M13mp18Scaffold,

    /// <summary>
    /// User-defined or custom reference DNA ladder with arbitrary molecular size markers.
    /// </summary>
    CustomLadder
}

/// <summary>
/// Represents a single detected electrophoretic band within an agarose gel lane.
/// </summary>
[SwaggerSchema(Title = "GelBand", Description = "A single detected electrophoretic nucleic acid band within an agarose gel lane.")]
public sealed record GelBand
{
    /// <summary>
    /// Zero-based sequential band index within the lane, ordered from top (slowest migration) to bottom (fastest migration).
    /// </summary>
    [Description("Zero-based sequential band index within the lane.")]
    [JsonPropertyName("bandIndex")]
    public int BandIndex { get; init; }

    /// <summary>
    /// Absolute migration distance from the top of the gel lane in pixels.
    /// </summary>
    [Description("Absolute migration distance from the top of the gel lane in pixels.")]
    [JsonPropertyName("migrationDistancePixels")]
    public double MigrationDistancePixels { get; init; }

    /// <summary>
    /// Relative electrophoretic migration value (Rf = distance / lane height), bounded between 0.0 and 1.0.
    /// </summary>
    [Description("Relative electrophoretic migration value (Rf = distance / lane height), bounded between 0.0 and 1.0.")]
    [JsonPropertyName("relativeMigrationRf")]
    public double RelativeMigrationRf { get; init; }

    /// <summary>
    /// Estimated molecular size in base pairs (bp), determined via log-linear ladder calibration.
    /// </summary>
    [Description("Estimated molecular size in base pairs (bp), determined via log-linear ladder calibration.")]
    [JsonPropertyName("estimatedBasePairs")]
    public double EstimatedBasePairs { get; init; }

    /// <summary>
    /// Background-subtracted integrated pixel intensity density under the band peak.
    /// </summary>
    [Description("Background-subtracted integrated pixel intensity density under the band peak.")]
    [JsonPropertyName("intensityIntegratedDensity")]
    public double IntensityIntegratedDensity { get; init; }

    /// <summary>
    /// Yield percentage of this individual band relative to total lane intensity (0.0% to 100.0%).
    /// </summary>
    [Description("Yield percentage of this individual band relative to total lane intensity.")]
    [JsonPropertyName("relativeYieldPercentage")]
    public double RelativeYieldPercentage { get; init; }
}

/// <summary>
/// Quantitative densitometry and quality assessment for a single agarose gel lane.
/// </summary>
[SwaggerSchema(Title = "GelLaneAnalysis", Description = "Quantitative densitometry and quality assessment for a single agarose gel lane.")]
public sealed record GelLaneAnalysis
{
    /// <summary>
    /// 1-based sequential lane number across the gel slab from left to right.
    /// </summary>
    [Description("1-based sequential lane number across the gel slab.")]
    [JsonPropertyName("laneNumber")]
    public int LaneNumber { get; init; }

    /// <summary>
    /// Human-readable lane label describing sample content (e.g. 'Lane 1 (Ladder)' or 'Lane 2 (Folded Nanotube)').
    /// </summary>
    [Description("Human-readable lane label describing sample content.")]
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Collection of electrophoretic bands detected within this lane.
    /// </summary>
    [Description("Collection of electrophoretic bands detected within this lane.")]
    [JsonPropertyName("bands")]
    public IReadOnlyList<GelBand> Bands { get; init; } = [];

    /// <summary>
    /// Percentage of total lane fluorescence intensity concentrated in the primary folded monomer band.
    /// </summary>
    [Description("Percentage of total lane fluorescence intensity concentrated in the primary folded monomer band.")]
    [JsonPropertyName("monomerBandYieldPercentage")]
    public double MonomerBandYieldPercentage { get; init; }

    /// <summary>
    /// Percentage of total lane fluorescence intensity residing in the high-mobility staple smear region (Rf &gt; 0.8).
    /// </summary>
    [Description("Percentage of total lane fluorescence intensity residing in the high-mobility staple smear region (Rf > 0.8).")]
    [JsonPropertyName("unannealedStapleSmearPercentage")]
    public double UnannealedStapleSmearPercentage { get; init; }

    /// <summary>
    /// Percentage of total lane fluorescence intensity attributed to slow-migrating concatemers, dimers, or aggregates.
    /// </summary>
    [Description("Percentage of total lane fluorescence intensity attributed to slow-migrating concatemers, dimers, or aggregates.")]
    [JsonPropertyName("dimerMultimerPercentage")]
    public double DimerMultimerPercentage { get; init; }

    /// <summary>
    /// Total integrated fluorescence intensity across the full length of the lane after background subtraction.
    /// </summary>
    [Description("Total integrated fluorescence intensity across the full length of the lane.")]
    [JsonPropertyName("totalIntensity")]
    public double TotalIntensity { get; init; }

    /// <summary>
    /// Indicates whether the lane passes quality thresholds (MonomerBandYield &gt;= 65.0% and UnannealedStapleSmear &lt;= 25.0%).
    /// </summary>
    [Description("Indicates whether the lane passes nanocarrier folding quality thresholds.")]
    [JsonPropertyName("foldingQualityAcceptable")]
    public bool FoldingQualityAcceptable { get; init; }
}

/// <summary>
/// Request payload for automated agarose gel electrophoresis image segmentation and band densitometry.
/// </summary>
[SwaggerSchema(Title = "GelAnalysisRequest", Description = "Request payload for automated agarose gel electrophoresis densitometry.")]
public sealed record GelAnalysisRequest : IExamplesProvider<GelAnalysisRequest>
{
    /// <summary>
    /// Raw 8-bit grayscale pixel byte array (row-major order) or raw pixel buffer.
    /// </summary>
    [Description("Raw 8-bit grayscale pixel byte array in row-major order.")]
    [JsonPropertyName("imageBytes")]
    public byte[] ImageBytes { get; init; } = [];

    /// <summary>
    /// Horizontal width of the gel image in pixels.
    /// </summary>
    [Description("Horizontal width of the gel image in pixels.")]
    [JsonPropertyName("width")]
    public int Width { get; init; }

    /// <summary>
    /// Vertical height of the gel image in pixels.
    /// </summary>
    [Description("Vertical height of the gel image in pixels.")]
    [JsonPropertyName("height")]
    public int Height { get; init; }

    /// <summary>
    /// Expected number of sample wells/lanes across the gel (default: 8, allowable range: 1 to 24).
    /// </summary>
    [Description("Expected number of sample wells/lanes across the gel (1 to 24).")]
    [JsonPropertyName("expectedLaneCount")]
    public int ExpectedLaneCount { get; init; } = 8;

    /// <summary>
    /// DNA molecular weight ladder type used for base pair calibration (default: OneKbDnaLadder).
    /// </summary>
    [Description("DNA molecular weight ladder type used for base pair calibration.")]
    [JsonPropertyName("ladderType")]
    public GelLadderType LadderType { get; init; } = GelLadderType.OneKbDnaLadder;

    /// <summary>
    /// 1-based index of the lane containing the reference molecular weight ladder (default: 1).
    /// </summary>
    [Description("1-based index of the lane containing the reference molecular weight ladder.")]
    [JsonPropertyName("ladderLaneNumber")]
    public int LadderLaneNumber { get; init; } = 1;

    /// <summary>
    /// Fractional background subtraction threshold (0.0 to 1.0, default: 0.15).
    /// </summary>
    [Description("Fractional background subtraction threshold (0.0 to 1.0).")]
    [JsonPropertyName("backgroundThreshold")]
    public double BackgroundThreshold { get; init; } = 0.15;

    /// <summary>
    /// Generates a representative example instance of <see cref="GelAnalysisRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated gel analysis request example.</returns>
    public GelAnalysisRequest GetExamples() => new()
    {
        ImageBytes = new byte[160 * 100],
        Width = 160,
        Height = 100,
        ExpectedLaneCount = 8,
        LadderType = GelLadderType.OneKbDnaLadder,
        LadderLaneNumber = 1,
        BackgroundThreshold = 0.15
    };
}

/// <summary>
/// Complete outcome of an automated agarose gel electrophoresis densitometry analysis run.
/// </summary>
[SwaggerSchema(Title = "GelAnalysisResult", Description = "Complete outcome of an automated agarose gel electrophoresis densitometry analysis run.")]
public sealed record GelAnalysisResult : IExamplesProvider<GelAnalysisResult>
{
    /// <summary>
    /// Horizontal width of the analyzed gel image in pixels.
    /// </summary>
    [Description("Horizontal width of the analyzed gel image in pixels.")]
    [JsonPropertyName("width")]
    public int Width { get; init; }

    /// <summary>
    /// Vertical height of the analyzed gel image in pixels.
    /// </summary>
    [Description("Vertical height of the analyzed gel image in pixels.")]
    [JsonPropertyName("height")]
    public int Height { get; init; }

    /// <summary>
    /// Number of segmented electrophoretic lanes detected across the image slab.
    /// </summary>
    [Description("Number of segmented electrophoretic lanes detected across the image slab.")]
    [JsonPropertyName("detectedLaneCount")]
    public int DetectedLaneCount { get; init; }

    /// <summary>
    /// Analytical densitometry results for each segmented lane.
    /// </summary>
    [Description("Analytical densitometry results for each segmented lane.")]
    [JsonPropertyName("lanes")]
    public IReadOnlyList<GelLaneAnalysis> Lanes { get; init; } = [];

    /// <summary>
    /// Mean monomer yield percentage across all non-ladder experimental lanes (0.0% to 100.0%).
    /// </summary>
    [Description("Mean monomer yield percentage across all non-ladder experimental lanes.")]
    [JsonPropertyName("overallFoldingSuccessRate")]
    public double OverallFoldingSuccessRate { get; init; }

    /// <summary>
    /// Human-readable biophysical interpretation and quality control diagnostic notes.
    /// </summary>
    [Description("Human-readable biophysical interpretation and quality control diagnostic notes.")]
    [JsonPropertyName("qualityAssessmentNotes")]
    public string QualityAssessmentNotes { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp recording when this analysis was executed.
    /// </summary>
    [Description("UTC timestamp recording when this analysis was executed.")]
    [JsonPropertyName("analyzedAtUtc")]
    public DateTimeOffset AnalyzedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Generates a representative example instance of <see cref="GelAnalysisResult"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated gel analysis result example.</returns>
    public GelAnalysisResult GetExamples() => new()
    {
        Width = 160,
        Height = 100,
        DetectedLaneCount = 8,
        Lanes =
        [
            new GelLaneAnalysis
            {
                LaneNumber = 1,
                Label = "Lane 1 (Ladder: OneKbDnaLadder)",
                Bands =
                [
                    new GelBand { BandIndex = 0, MigrationDistancePixels = 15.0, RelativeMigrationRf = 0.15, EstimatedBasePairs = 10000.0, IntensityIntegratedDensity = 1200.0, RelativeYieldPercentage = 15.0 }
                ],
                MonomerBandYieldPercentage = 15.0,
                UnannealedStapleSmearPercentage = 5.0,
                DimerMultimerPercentage = 0.0,
                TotalIntensity = 8000.0,
                FoldingQualityAcceptable = false
            },
            new GelLaneAnalysis
            {
                LaneNumber = 2,
                Label = "Lane 2 (Experimental)",
                Bands =
                [
                    new GelBand { BandIndex = 0, MigrationDistancePixels = 45.0, RelativeMigrationRf = 0.45, EstimatedBasePairs = 7249.0, IntensityIntegratedDensity = 6800.0, RelativeYieldPercentage = 78.5 }
                ],
                MonomerBandYieldPercentage = 78.5,
                UnannealedStapleSmearPercentage = 12.0,
                DimerMultimerPercentage = 4.2,
                TotalIntensity = 8660.0,
                FoldingQualityAcceptable = true
            }
        ],
        OverallFoldingSuccessRate = 78.5,
        QualityAssessmentNotes = "All 7 experimental lanes exhibit acceptable folding (monomer yield >= 65%, staple smear <= 25%). Overall yield: 78.5%.",
        AnalyzedAtUtc = DateTimeOffset.UtcNow
    };
}
