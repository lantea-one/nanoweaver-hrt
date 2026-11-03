namespace NanoWeaverHrt.Fabrication.Gel;

/// <summary>
/// Service providing high-throughput automated agarose gel electrophoresis densitometry, lane segmentation,
/// band peak detection, and DNA origami nanocarrier folding quality assessment.
/// </summary>
public interface IGelAnalysisService
{
    /// <summary>
    /// Analyzes an agarose gel electrophoresis image request using zero-allocation densitometry buffers.
    /// </summary>
    /// <param name="request">The gel analysis parameters including raw pixel data, dimensions, and ladder metadata.</param>
    /// <returns>A comprehensive analysis result containing lane metrics, band sizes, monomer yields, and quality verdicts.</returns>
    GelAnalysisResult AnalyzeGel(GelAnalysisRequest request);

    /// <summary>
    /// Asynchronously streams and analyzes an agarose gel electrophoresis image from an input stream.
    /// </summary>
    /// <param name="imageStream">Input stream providing the raw grayscale or encoded pixel image data.</param>
    /// <param name="width">Horizontal width of the gel image in pixels.</param>
    /// <param name="height">Vertical height of the gel image in pixels.</param>
    /// <param name="ladderType">Molecular weight reference ladder type (default: OneKbDnaLadder).</param>
    /// <param name="expectedLanes">Expected number of lanes across the gel slab (default: 8).</param>
    /// <param name="ct">Cancellation token for aborting the asynchronous read operation.</param>
    /// <returns>A task producing the complete gel analysis result upon completion.</returns>
    Task<GelAnalysisResult> AnalyzeGelAsync(
        Stream imageStream,
        int width,
        int height,
        GelLadderType ladderType = GelLadderType.OneKbDnaLadder,
        int expectedLanes = 8,
        CancellationToken ct = default);
}
