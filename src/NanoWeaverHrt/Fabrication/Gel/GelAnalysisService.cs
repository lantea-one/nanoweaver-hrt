using System.Buffers;
using Microsoft.Extensions.Logging;

namespace NanoWeaverHrt.Fabrication.Gel;

/// <summary>
/// Provides zero-allocation agarose gel electrophoresis image segmentation, lane vertical profiling,
/// band peak detection, DNA ladder log-linear mobility calibration, and nanocarrier folding quality densitometry.
/// </summary>
public sealed class GelAnalysisService : IGelAnalysisService
{
    /// <summary>
    /// Logger instance recording analytical densitometry events and diagnostic telemetry.
    /// </summary>
    private readonly ILogger<GelAnalysisService> _logger;

    /// <summary>
    /// Known base pair sizes for the standard 1 kb DNA ladder sorted from slowest (top) to fastest (bottom).
    /// </summary>
    private static readonly double[] KnownOneKbDnaLadderBands =
    [
        10000.0, 8000.0, 6000.0, 5000.0, 4000.0, 3000.0, 2000.0, 1500.0, 1000.0, 500.0
    ];

    /// <summary>
    /// Known base pair sizes for the M13mp18 single-stranded scaffold reference.
    /// </summary>
    private static readonly double[] KnownM13mp18Bands =
    [
        7249.0, 5000.0, 3000.0, 2000.0, 1000.0, 500.0
    ];

    /// <summary>
    /// Known base pair sizes for arbitrary custom DNA ladders.
    /// </summary>
    private static readonly double[] KnownCustomLadderBands =
    [
        10000.0, 5000.0, 2000.0, 1000.0, 500.0
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="GelAnalysisService"/> class.
    /// </summary>
    /// <param name="logger">The application logging facility.</param>
    public GelAnalysisService(ILogger<GelAnalysisService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Analyzes an agarose gel electrophoresis image request using zero-allocation densitometry buffers.
    /// </summary>
    /// <param name="request">The gel analysis parameters including raw pixel data, dimensions, and ladder metadata.</param>
    /// <returns>A comprehensive analysis result containing lane metrics, band sizes, monomer yields, and quality verdicts.</returns>
    public GelAnalysisResult AnalyzeGel(GelAnalysisRequest request)
    {
        // WHAT: Performs high-throughput densitometry and nanocarrier quality assessment on raw gel pixels.
        // WHY:  Wet-lab DNA origami assembly quality must be validated by measuring folded monomer yield and staple smear.
        // HOW:  Segments lanes via column projection, extracts background-subtracted vertical profiles, detects peaks, and calibrates against ladder.
        ArgumentNullException.ThrowIfNull(request);

        if (request.Width <= 0 || request.Width > 8192)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Width), "Width must be between 1 and 8192.");
        }

        if (request.Height <= 0 || request.Height > 8192)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Height), "Height must be between 1 and 8192.");
        }

        if (request.ImageBytes == null || request.ImageBytes.Length == 0)
        {
            throw new ArgumentException("ImageBytes cannot be null or empty.", nameof(request));
        }

        if (request.ExpectedLaneCount <= 0 || request.ExpectedLaneCount > 24)
        {
            throw new ArgumentOutOfRangeException(nameof(request.ExpectedLaneCount), "ExpectedLaneCount must be between 1 and 24.");
        }

        if (request.BackgroundThreshold < 0.0 || request.BackgroundThreshold > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.BackgroundThreshold), "BackgroundThreshold must be between 0.0 and 1.0.");
        }

        if (request.LadderLaneNumber <= 0 || request.LadderLaneNumber > request.ExpectedLaneCount)
        {
            throw new ArgumentOutOfRangeException(nameof(request.LadderLaneNumber), "LadderLaneNumber must be between 1 and ExpectedLaneCount.");
        }

        if (request.ImageBytes.Length < request.Width * request.Height)
        {
            throw new ArgumentException("ImageBytes buffer length is smaller than the required Width * Height dimensions.", nameof(request));
        }

        int width = request.Width;
        int height = request.Height;
        int laneCount = request.ExpectedLaneCount;
        int bytesPerPixel = ResolveBytesPerPixel(request.ImageBytes.Length, width, height);

        ReadOnlySpan<byte> pixelSpan = request.ImageBytes.AsSpan(0, width * height * bytesPerPixel);

        // Segment lane horizontal boundaries
        int[] laneBoundaries = SegmentLanes(pixelSpan, width, height, laneCount, bytesPerPixel);

        // Pre-extract ladder calibration points from the designated ladder lane
        IReadOnlyList<LadderCalibrationPoint> calibration = ExtractLadderCalibration(
            pixelSpan,
            width,
            height,
            laneBoundaries,
            request.LadderLaneNumber,
            request.LadderType,
            request.BackgroundThreshold,
            bytesPerPixel);

        // Process all individual lanes
        List<GelLaneAnalysis> lanes = new(laneCount);
        for (int laneIdx = 0; laneIdx < laneCount; laneIdx++)
        {
            int laneNum = laneIdx + 1;
            bool isLadder = laneNum == request.LadderLaneNumber;
            string label = isLadder
                ? $"Lane {laneNum} (Ladder: {request.LadderType})"
                : $"Lane {laneNum} (Experimental)";

            int xStart = laneBoundaries[laneIdx];
            int xEnd = laneBoundaries[laneIdx + 1];

            GelLaneAnalysis laneAnalysis = AnalyzeSingleLane(
                pixelSpan,
                width,
                height,
                xStart,
                xEnd,
                laneNum,
                label,
                isLadder,
                request.BackgroundThreshold,
                calibration,
                bytesPerPixel);

            lanes.Add(laneAnalysis);
        }

        // Compute overall folding success rate across non-ladder experimental lanes
        // WHAT: Computes mean monomer band yield across experimental non-ladder lanes without LINQ allocations.
        // WHY:  Eliminates heap-allocated closures and List<T> instances in high-throughput densitometry pipelines.
        // HOW:  Accumulates yield sum and non-ladder count using a single sequential loop, falling back to all lanes.
        double nonLadderSum = 0.0;
        int nonLadderCount = 0;
        double allLanesSum = 0.0;
        for (int i = 0; i < lanes.Count; i++)
        {
            double yield = lanes[i].MonomerBandYieldPercentage;
            allLanesSum += yield;
            if (lanes[i].LaneNumber != request.LadderLaneNumber)
            {
                nonLadderSum += yield;
                nonLadderCount++;
            }
        }

        double overallFoldingSuccessRate = nonLadderCount > 0
            ? Math.Round(nonLadderSum / nonLadderCount, 1)
            : Math.Round(allLanesSum / lanes.Count, 1);

        string notes = GenerateDiagnosticNotes(lanes, request.LadderLaneNumber, overallFoldingSuccessRate);

        _logger.LogDebug(
            "Gel analysis complete: {Lanes} lanes processed, overall folding success: {Rate}%",
            lanes.Count,
            overallFoldingSuccessRate);

        return new GelAnalysisResult
        {
            Width = width,
            Height = height,
            DetectedLaneCount = lanes.Count,
            Lanes = lanes,
            OverallFoldingSuccessRate = overallFoldingSuccessRate,
            QualityAssessmentNotes = notes,
            AnalyzedAtUtc = DateTimeOffset.UtcNow
        };
    }

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
    public async Task<GelAnalysisResult> AnalyzeGelAsync(
        Stream imageStream,
        int width,
        int height,
        GelLadderType ladderType = GelLadderType.OneKbDnaLadder,
        int expectedLanes = 8,
        CancellationToken ct = default)
    {
        // WHAT: Asynchronously ingests raw pixel streams into memory buffers and delegates to AnalyzeGel.
        // WHY:  Allows non-blocking I/O when processing large 16-bit or multi-megabyte gel images uploaded by clients.
        // HOW:  Reads the entire stream into a rented byte buffer or MemoryStream respecting the CancellationToken.
        ArgumentNullException.ThrowIfNull(imageStream);

        if (width <= 0 || width > 8192)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be between 1 and 8192.");
        }

        if (height <= 0 || height > 8192)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be between 1 and 8192.");
        }

        if (expectedLanes <= 0 || expectedLanes > 24)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedLanes), "ExpectedLanes must be between 1 and 24.");
        }

        ct.ThrowIfCancellationRequested();

        long maxAllowedBytes = (long)width * height * 4;
        if (imageStream.CanSeek && imageStream.Length > maxAllowedBytes)
        {
            throw new ArgumentException($"ImageStream length ({imageStream.Length} bytes) exceeds maximum supported buffer size ({maxAllowedBytes} bytes) for specified dimensions.", nameof(imageStream));
        }

        byte[] imageBytes;
        {
            await using MemoryStream ms = new();
            byte[] copyBuffer = ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                long totalBytesRead = 0;
                int read;
                while ((read = await imageStream.ReadAsync(copyBuffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
                {
                    totalBytesRead += read;
                    if (totalBytesRead > maxAllowedBytes)
                    {
                        throw new ArgumentException($"Image stream exceeded maximum allowed byte size of {maxAllowedBytes} bytes for specified dimensions.", nameof(imageStream));
                    }

                    await ms.WriteAsync(copyBuffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(copyBuffer);
            }

            imageBytes = ms.ToArray();
        }

        GelAnalysisRequest request = new()
        {
            ImageBytes = imageBytes,
            Width = width,
            Height = height,
            ExpectedLaneCount = expectedLanes,
            LadderType = ladderType,
            LadderLaneNumber = 1
        };

        return AnalyzeGel(request);
    }

    /// <summary>
    /// Resolves the bytes per pixel encoding (1 for 8-bit grayscale, 3 for 24-bit RGB, 4 for 32-bit RGBA).
    /// </summary>
    /// <param name="bufferLength">Total byte length of the supplied image buffer.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <returns>Integer count of bytes per pixel.</returns>
    private static int ResolveBytesPerPixel(int bufferLength, int width, int height)
    {
        // WHAT: Determines pixel byte width based on buffer capacity.
        // WHY:  Accepts both raw grayscale byte arrays and raw RGB/RGBA frame buffers without throwing.
        // HOW:  Checks whether buffer is at least 4x or 3x the nominal pixel count, defaulting to 1 byte/pixel.
        int pixelCount = width * height;
        if (bufferLength >= pixelCount * 4)
        {
            return 4;
        }

        if (bufferLength >= pixelCount * 3)
        {
            return 3;
        }

        return 1;
    }

    /// <summary>
    /// Reads a scalar intensity value for a single pixel (0.0 to 255.0).
    /// </summary>
    /// <param name="bytes">Read-only pixel memory span.</param>
    /// <param name="x">Horizontal coordinate.</param>
    /// <param name="y">Vertical coordinate.</param>
    /// <param name="width">Total image width in pixels.</param>
    /// <param name="bpp">Bytes per pixel.</param>
    /// <returns>Extracted scalar intensity value (0.0 to 255.0).</returns>
    private static double ReadPixelIntensity(ReadOnlySpan<byte> bytes, int x, int y, int width, int bpp)
    {
        // WHAT: Extracts grayscale luminance from pixel bytes.
        // WHY:  Standardizes multi-channel RGB/RGBA and single-channel grayscale into a single densitometry value.
        // HOW:  Applies ITU-R BT.601 luminance weighting (0.299 R + 0.587 G + 0.114 B) for multi-channel bytes.
        int offset = (y * width + x) * bpp;
        if (bpp == 1)
        {
            return bytes[offset];
        }

        double r = bytes[offset];
        double g = bytes[offset + 1];
        double b = bytes[offset + 2];
        return (0.299 * r) + (0.587 * g) + (0.114 * b);
    }

    /// <summary>
    /// Computes column vertical projections to detect lane boundaries (valleys in horizontal intensity profile).
    /// </summary>
    /// <param name="bytes">Pixel byte memory span.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="laneCount">Expected number of lanes.</param>
    /// <param name="bpp">Bytes per pixel.</param>
    /// <returns>An array of lane boundaries of size laneCount + 1, where lane i spans from boundaries[i] to boundaries[i+1].</returns>
    private static int[] SegmentLanes(ReadOnlySpan<byte> bytes, int width, int height, int laneCount, int bpp)
    {
        // WHAT: Computes vertical column intensity sums to identify inter-lane valleys.
        // WHY:  Agarose gel lanes are separated by dark, unstained spaces (valleys) between migration tracks.
        // HOW:  Rents a double array from ArrayPool<double>.Shared, sums columns, and locates local minima around nominal boundaries.
        int[] boundaries = new int[laneCount + 1];
        boundaries[0] = 0;
        boundaries[laneCount] = width;

        if (laneCount == 1)
        {
            return boundaries;
        }

        double[] colProfile = ArrayPool<double>.Shared.Rent(width);
        try
        {
            Array.Clear(colProfile, 0, width);

            for (int x = 0; x < width; x++)
            {
                double sum = 0.0;
                for (int y = 0; y < height; y++)
                {
                    sum += ReadPixelIntensity(bytes, x, y, width, bpp);
                }
                colProfile[x] = sum / height;
            }

            double nominalLaneWidth = (double)width / laneCount;
            int searchRadius = Math.Max(1, (int)(nominalLaneWidth * 0.25));

            int prevBound = 0;
            for (int k = 1; k < laneCount; k++)
            {
                int nominalBound = (int)Math.Round(k * nominalLaneWidth);
                int minSearch = Math.Max(prevBound + 1, nominalBound - searchRadius);
                int maxSearch = Math.Min(width - (laneCount - k) - 1, nominalBound + searchRadius);

                int bestX = nominalBound;
                double minIntensity = double.MaxValue;

                for (int x = minSearch; x <= maxSearch; x++)
                {
                    if (colProfile[x] < minIntensity)
                    {
                        minIntensity = colProfile[x];
                        bestX = x;
                    }
                }

                bestX = Math.Clamp(bestX, prevBound + 1, width - (laneCount - k));
                boundaries[k] = bestX;
                prevBound = bestX;
            }
        }
        finally
        {
            ArrayPool<double>.Shared.Return(colProfile);
        }

        return boundaries;
    }

    /// <summary>
    /// Extracts ladder calibration points from the designated reference ladder lane.
    /// </summary>
    /// <param name="bytes">Pixel byte memory span.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="boundaries">Lane column boundaries array.</param>
    /// <param name="ladderLaneNum">1-based lane number for the ladder.</param>
    /// <param name="ladderType">Ladder type standard.</param>
    /// <param name="bgThreshold">Fractional background threshold.</param>
    /// <param name="bpp">Bytes per pixel.</param>
    /// <returns>A list of ladder calibration points mapping relative migration Rf to log10(bp).</returns>
    private static IReadOnlyList<LadderCalibrationPoint> ExtractLadderCalibration(
        ReadOnlySpan<byte> bytes,
        int width,
        int height,
        int[] boundaries,
        int ladderLaneNum,
        GelLadderType ladderType,
        double bgThreshold,
        int bpp)
    {
        // WHAT: Establishes the mathematical relationship between migration distance Rf and fragment base pairs.
        // WHY:  Agarose gel electrophoresis obeys the classic Southern equation: log10(bp) is inversely proportional to Rf.
        // HOW:  Detects peaks in the designated ladder lane and assigns reference molecular weights, or uses synthetic reference bands.
        double[] referenceSizes = ladderType switch
        {
            GelLadderType.M13mp18Scaffold => KnownM13mp18Bands,
            GelLadderType.CustomLadder => KnownCustomLadderBands,
            _ => KnownOneKbDnaLadderBands
        };

        int ladderIdx = ladderLaneNum - 1;
        int xStart = boundaries[ladderIdx];
        int xEnd = boundaries[ladderIdx + 1];

        // Extract peaks from the ladder lane
        IReadOnlyList<DetectedPeak> peaks = ExtractPeaksFromLane(
            bytes,
            width,
            height,
            xStart,
            xEnd,
            bgThreshold,
            bpp);

        List<LadderCalibrationPoint> points = new(referenceSizes.Length);

        if (peaks.Count >= 2)
        {
            // Map detected peaks to known reference ladder sizes in sequential order from top to bottom
            int mapCount = Math.Min(peaks.Count, referenceSizes.Length);
            for (int i = 0; i < mapCount; i++)
            {
                double bp = referenceSizes[i];
                points.Add(new LadderCalibrationPoint(peaks[i].Rf, Math.Log10(bp), bp));
            }
        }
        else
        {
            // Fallback: construct theoretical reference positions linearly distributed across typical Rf range 0.10 to 0.85
            int n = referenceSizes.Length;
            for (int i = 0; i < n; i++)
            {
                double rf = 0.10 + (i * (0.85 - 0.10) / (n - 1));
                double bp = referenceSizes[i];
                points.Add(new LadderCalibrationPoint(rf, Math.Log10(bp), bp));
            }
        }

        // Sort calibration points by Rf ascending
        points.Sort((a, b) => a.Rf.CompareTo(b.Rf));
        return points;
    }

    /// <summary>
    /// Analyzes a single lane: extracts background-subtracted vertical profile, detects peaks, and computes metrics.
    /// </summary>
    /// <param name="bytes">Pixel byte memory span.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="xStart">Start column index (inclusive).</param>
    /// <param name="xEnd">End column index (exclusive).</param>
    /// <param name="laneNum">1-based lane number.</param>
    /// <param name="label">Lane label.</param>
    /// <param name="isLadder">True if this is the ladder lane.</param>
    /// <param name="bgThreshold">Background threshold fraction.</param>
    /// <param name="calibration">Ladder calibration points for bp estimation.</param>
    /// <param name="bpp">Bytes per pixel.</param>
    /// <returns>A populated <see cref="GelLaneAnalysis"/> instance.</returns>
    private static GelLaneAnalysis AnalyzeSingleLane(
        ReadOnlySpan<byte> bytes,
        int width,
        int height,
        int xStart,
        int xEnd,
        int laneNum,
        string label,
        bool isLadder,
        double bgThreshold,
        IReadOnlyList<LadderCalibrationPoint> calibration,
        int bpp)
    {
        // WHAT: Performs densitometric quantification and quality classification on a single lane slice.
        // WHY:  Calculates primary monomer yield, unannealed staple smear, and concatemeric multimer fractions.
        // HOW:  Extracts background-subtracted profile with ArrayPool<double>.Shared, integrates peak density, and evaluates criteria.
        int laneWidth = Math.Max(1, xEnd - xStart);
        double[] rowProfile = ArrayPool<double>.Shared.Rent(height);
        try
        {
            Array.Clear(rowProfile, 0, height);

            // Step 1: Compute vertical profile
            for (int y = 0; y < height; y++)
            {
                double rowSum = 0.0;
                for (int x = xStart; x < xEnd; x++)
                {
                    rowSum += ReadPixelIntensity(bytes, x, y, width, bpp);
                }
                rowProfile[y] = rowSum / laneWidth;
            }

            // Step 2: Background subtraction
            double bgBaseline = bgThreshold * 255.0;
            double minObserved = double.MaxValue;
            for (int y = 0; y < height; y++)
            {
                if (rowProfile[y] < minObserved)
                {
                    minObserved = rowProfile[y];
                }
            }

            double effectiveBg = Math.Max(bgBaseline, minObserved);
            double totalLaneIntensity = 0.0;
            for (int y = 0; y < height; y++)
            {
                rowProfile[y] = Math.Max(0.0, rowProfile[y] - effectiveBg);
                totalLaneIntensity += rowProfile[y] * laneWidth;
            }

            // Step 3: Detect local intensity peaks (bands)
            IReadOnlyList<DetectedPeak> detectedPeaks = DetectPeaksInProfile(rowProfile, height, laneWidth);

            List<GelBand> bands = new(detectedPeaks.Count);
            for (int b = 0; b < detectedPeaks.Count; b++)
            {
                DetectedPeak peak = detectedPeaks[b];
                double estBp = isLadder && b < calibration.Count
                    ? calibration[b].BasePairs
                    : EstimateBasePairs(peak.Rf, calibration);

                double yieldPct = Math.Round((peak.IntegratedDensity / totalLaneIntensity) * 100.0, 1);

                bands.Add(new GelBand
                {
                    BandIndex = b,
                    MigrationDistancePixels = peak.Y,
                    RelativeMigrationRf = Math.Round(peak.Rf, 3),
                    EstimatedBasePairs = Math.Round(estBp, 0),
                    IntensityIntegratedDensity = Math.Round(peak.IntegratedDensity, 1),
                    RelativeYieldPercentage = yieldPct
                });
            }

            // Step 4: Calculate monomer yield percentage
            // The primary monomer band is the dominant band with highest integrated density
            // WHAT: Identifies the primary monomer band by finding the peak with maximum integrated density.
            // WHY:  Avoids LINQ OrderByDescending heap allocation of enumerator and sorted buffer.
            // HOW:  Scans bands linearly tracking the maximum IntensityIntegratedDensity.
            GelBand? monomerBand = null;
            if (bands.Count > 0)
            {
                monomerBand = bands[0];
                for (int i = 1; i < bands.Count; i++)
                {
                    if (bands[i].IntensityIntegratedDensity > monomerBand.IntensityIntegratedDensity)
                    {
                        monomerBand = bands[i];
                    }
                }
            }

            double monomerYieldPct = monomerBand != null
                ? Math.Round((monomerBand.IntensityIntegratedDensity / totalLaneIntensity) * 100.0, 1)
                : 0.0;

            // Step 5: Calculate unannealed staple smear percentage (Rf > 0.8)
            double stapleIntensity = 0.0;
            for (int y = 0; y < height; y++)
            {
                double rowRf = (double)y / Math.Max(1, height - 1);
                if (rowRf > 0.8)
                {
                    stapleIntensity += rowProfile[y] * laneWidth;
                }
            }

            double stapleSmearPct = totalLaneIntensity > 0.0
                ? Math.Round((stapleIntensity / totalLaneIntensity) * 100.0, 1)
                : 0.0;

            // Step 6: Calculate dimer/multimer percentage (signal migrating above monomer band)
            double multimerIntensity = 0.0;
            if (monomerBand != null)
            {
                // Bands migrating higher up (lower Rf) than the monomer band
                double thresholdRf = monomerBand.RelativeMigrationRf - 0.03;
                foreach (GelBand b in bands)
                {
                    if (b.RelativeMigrationRf < thresholdRf)
                    {
                        multimerIntensity += b.IntensityIntegratedDensity;
                    }
                }
            }

            double dimerMultimerPct = monomerBand != null
                ? Math.Round((multimerIntensity / totalLaneIntensity) * 100.0, 1)
                : 0.0;

            // Step 7: Flag folding quality acceptable
            // Flags true when MonomerBandYieldPercentage >= 65.0 and UnannealedStapleSmearPercentage <= 25.0
            bool foldingQualityAcceptable = !isLadder && monomerYieldPct >= 65.0 && stapleSmearPct <= 25.0;

            return new GelLaneAnalysis
            {
                LaneNumber = laneNum,
                Label = label,
                Bands = bands,
                MonomerBandYieldPercentage = monomerYieldPct,
                UnannealedStapleSmearPercentage = stapleSmearPct,
                DimerMultimerPercentage = dimerMultimerPct,
                TotalIntensity = Math.Round(totalLaneIntensity, 1),
                FoldingQualityAcceptable = foldingQualityAcceptable
            };
        }
        finally
        {
            ArrayPool<double>.Shared.Return(rowProfile);
        }
    }

    /// <summary>
    /// Extracts local intensity peaks from a lane slice without allocating persistent buffers.
    /// </summary>
    /// <param name="bytes">Pixel byte memory span.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="xStart">Start column index.</param>
    /// <param name="xEnd">End column index.</param>
    /// <param name="bgThreshold">Background threshold fraction.</param>
    /// <param name="bpp">Bytes per pixel.</param>
    /// <returns>A list of detected peak coordinates and integrated densities.</returns>
    private static IReadOnlyList<DetectedPeak> ExtractPeaksFromLane(
        ReadOnlySpan<byte> bytes,
        int width,
        int height,
        int xStart,
        int xEnd,
        double bgThreshold,
        int bpp)
    {
        // WHAT: Extracts peak positions and densities directly from an image lane slice.
        // WHY:  Used during ladder calibration extraction before lane analysis objects are built.
        // HOW:  Constructs vertical profile using rented ArrayPool buffer, subtracts background, and locates maxima.
        int laneWidth = Math.Max(1, xEnd - xStart);
        double[] rowProfile = ArrayPool<double>.Shared.Rent(height);
        try
        {
            Array.Clear(rowProfile, 0, height);

            for (int y = 0; y < height; y++)
            {
                double sum = 0.0;
                for (int x = xStart; x < xEnd; x++)
                {
                    sum += ReadPixelIntensity(bytes, x, y, width, bpp);
                }
                rowProfile[y] = sum / laneWidth;
            }

            double bgBaseline = bgThreshold * 255.0;
            double minObserved = double.MaxValue;
            for (int y = 0; y < height; y++)
            {
                if (rowProfile[y] < minObserved)
                {
                    minObserved = rowProfile[y];
                }
            }

            double effectiveBg = Math.Max(bgBaseline, minObserved);
            for (int y = 0; y < height; y++)
            {
                rowProfile[y] = Math.Max(0.0, rowProfile[y] - effectiveBg);
            }

            return DetectPeaksInProfile(rowProfile, height, laneWidth);
        }
        finally
        {
            ArrayPool<double>.Shared.Return(rowProfile);
        }
    }

    /// <summary>
    /// Detects peak local maxima in a background-subtracted vertical profile array.
    /// </summary>
    /// <param name="rowProfile">Array containing background-subtracted vertical intensities.</param>
    /// <param name="height">Height of the profile.</param>
    /// <param name="laneWidth">Width of the lane in pixels for area integration.</param>
    /// <returns>A list of detected local peak structures.</returns>
    internal static IReadOnlyList<DetectedPeak> DetectPeaksInProfile(double[] rowProfile, int height, int laneWidth)
    {
        // WHAT: Locates local intensity maxima with prominence filtering.
        // WHY:  Isolates distinct fluorescent DNA bands from baseline noise and smears.
        // HOW:  Finds local maxima where rowProfile[y] > rowProfile[y-1] and rowProfile[y] >= rowProfile[y+1], integrating area.
        List<DetectedPeak> peaks = [];
        if (height < 3)
        {
            return peaks;
        }

        // Measure maximum signal to filter sub-threshold noise
        double maxSignal = 0.0;
        for (int y = 0; y < height; y++)
        {
            if (rowProfile[y] > maxSignal)
            {
                maxSignal = rowProfile[y];
            }
        }

        if (maxSignal < 1.0)
        {
            return peaks;
        }

        double minPeakHeight = Math.Max(2.0, maxSignal * 0.03);

        for (int y = 1; y < height - 1; y++)
        {
            double val = rowProfile[y];
            if (val < minPeakHeight || val <= rowProfile[y - 1] || val < rowProfile[y + 1])
            {
                continue;
            }

            // Find left valley boundary
            int left = y;
            while (left > 0 && rowProfile[left - 1] <= rowProfile[left] && rowProfile[left - 1] > 0.0)
            {
                left--;
            }

            // Find right valley boundary
            int right = y;
            while (right < height - 1 && rowProfile[right + 1] <= rowProfile[right] && rowProfile[right + 1] > 0.0)
            {
                right++;
            }

            // Calculate integrated density within the peak envelope
            double integratedDensity = 0.0;
            for (int k = left; k <= right; k++)
            {
                integratedDensity += rowProfile[k] * laneWidth;
            }

            double rf = (double)y / Math.Max(1, height - 1);
            peaks.Add(new DetectedPeak(y, rf, integratedDensity));

            // Skip forward past this peak to avoid duplicate detection on plateaus
            y = right;
        }

        return peaks;
    }

    /// <summary>
    /// Estimates fragment base pairs from relative electrophoretic mobility (Rf) via log-linear interpolation.
    /// </summary>
    /// <param name="rf">The relative mobility Rf value (0.0 to 1.0).</param>
    /// <param name="calibration">Calibration points connecting Rf to log10(bp).</param>
    /// <returns>Estimated fragment size in base pairs (bp).</returns>
    internal static double EstimateBasePairs(double rf, IReadOnlyList<LadderCalibrationPoint> calibration)
    {
        // WHAT: Performs log-linear interpolation against reference DNA ladder mobility points.
        // WHY:  Standard densitometric standard: electrophoretic migration is proportional to negative log molecular weight.
        // HOW:  Locates bounding calibration points and linearly interpolates log10(bp), transforming back via 10^(log_bp).
        if (calibration.Count == 0)
        {
            return 1000.0;
        }

        if (calibration.Count == 1)
        {
            return calibration[0].BasePairs;
        }

        int segmentIdx = 0;
        while (segmentIdx < calibration.Count - 2 && rf > calibration[segmentIdx + 1].Rf)
        {
            segmentIdx++;
        }

        LadderCalibrationPoint p0 = calibration[segmentIdx];
        LadderCalibrationPoint p1 = calibration[segmentIdx + 1];
        double span = p1.Rf - p0.Rf;
        if (span <= 0.0)
        {
            return p0.BasePairs;
        }

        double t = (rf - p0.Rf) / span;
        double logBp = p0.LogBp + (t * (p1.LogBp - p0.LogBp));
        return Math.Clamp(Math.Pow(10.0, logBp), 50.0, 50000.0);
    }

    /// <summary>
    /// Synthesizes human-readable quality control diagnostic assessment notes.
    /// </summary>
    /// <param name="lanes">All analyzed lanes.</param>
    /// <param name="ladderLaneNum">Ladder lane number.</param>
    /// <param name="overallSuccessRate">Overall folding success percentage.</param>
    /// <returns>Formatted assessment notes string.</returns>
    private static string GenerateDiagnosticNotes(IReadOnlyList<GelLaneAnalysis> lanes, int ladderLaneNum, double overallSuccessRate)
    {
        // WHAT: Formulates actionable plain-English feedback on gel assembly quality without LINQ allocations.
        // WHY:  Assists researchers and wet-lab technicians in diagnosing folding failures or confirming therapeutic readiness.
        // HOW:  Counts experimental lanes and acceptable folding lanes via a single sequential loop.
        int total = 0;
        int passed = 0;
        for (int i = 0; i < lanes.Count; i++)
        {
            GelLaneAnalysis lane = lanes[i];
            if (lane.LaneNumber != ladderLaneNum)
            {
                total++;
                if (lane.FoldingQualityAcceptable)
                {
                    passed++;
                }
            }
        }

        if (total == 0)
        {
            return "Reference ladder analysis complete. Zero experimental nanocarrier lanes present on the gel.";
        }

        if (passed == total)
        {
            return $"All {total} experimental lanes exhibit acceptable folding (monomer yield >= 65.0%, unannealed staple smear <= 25.0%). Mean monomer yield: {overallSuccessRate:F1}%. Nanocarrier chassis ready for therapeutic encapsulation.";
        }

        if (passed > 0)
        {
            return $"{passed}/{total} experimental lanes met acceptable folding criteria. Mean monomer yield: {overallSuccessRate:F1}%. Elevated unannealed staple smear or concatemer multimer formation detected in {total - passed} lane(s). Verify staple molar excess ratio and magnesium concentration.";
        }

        return $"Suboptimal folding quality across all {total} experimental lanes. Mean monomer yield ({overallSuccessRate:F1}%) fell below the 65.0% threshold. Recommended corrective action: optimize Mg²⁺ concentration (11–12.5 mM) and reduce thermal cooling ramp rate (1.0°C/min or slower).";
    }

    /// <summary>
    /// Internal representation of a detected intensity peak.
    /// </summary>
    /// <param name="Y">Vertical pixel coordinate.</param>
    /// <param name="Rf">Relative migration value.</param>
    /// <param name="IntegratedDensity">Background-subtracted integrated intensity density.</param>
    internal sealed record DetectedPeak(double Y, double Rf, double IntegratedDensity);

    /// <summary>
    /// Internal reference ladder calibration point connecting migration distance to base pair size.
    /// </summary>
    /// <param name="Rf">Relative migration value.</param>
    /// <param name="LogBp">Logarithm base 10 of base pair size.</param>
    /// <param name="BasePairs">Base pair size.</param>
    internal sealed record LadderCalibrationPoint(double Rf, double LogBp, double BasePairs);
}
