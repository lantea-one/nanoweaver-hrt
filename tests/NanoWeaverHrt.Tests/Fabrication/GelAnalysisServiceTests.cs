using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NanoWeaverHrt.Fabrication.Gel;
using Xunit;

namespace NanoWeaverHrt.Tests.Fabrication;

/// <summary>
/// Unit tests verifying agarose gel electrophoresis lane segmentation, band peak detection,
/// ladder base-pair calibration, and nanocarrier folding quality densitometry in <see cref="GelAnalysisService"/>.
/// </summary>
public sealed class GelAnalysisServiceTests
{
    /// <summary>
    /// SUT instance configured with a null logger.
    /// </summary>
    private readonly GelAnalysisService _service = new(NullLogger<GelAnalysisService>.Instance);

    /// <summary>
    /// Verifies constructor guards against a null logger instance.
    /// </summary>
    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new GelAnalysisService(null!));
    }

    /// <summary>
    /// Verifies that invalid or out-of-range request parameters trigger appropriate argument exceptions.
    /// </summary>
    [Fact]
    public void AnalyzeGel_InvalidArguments_ThrowsAppropriateExceptions()
    {
        // Null request
        Assert.Throws<ArgumentNullException>(() => _service.AnalyzeGel(null!));

        // Width <= 0
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.AnalyzeGel(new GelAnalysisRequest
        {
            ImageBytes = [1, 2, 3, 4],
            Width = 0,
            Height = 2
        }));

        // Height <= 0
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.AnalyzeGel(new GelAnalysisRequest
        {
            ImageBytes = [1, 2, 3, 4],
            Width = 2,
            Height = 0
        }));

        // ImageBytes null or empty
        Assert.Throws<ArgumentException>(() => _service.AnalyzeGel(new GelAnalysisRequest
        {
            ImageBytes = [],
            Width = 2,
            Height = 2
        }));

        // ExpectedLaneCount <= 0 or > 24
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.AnalyzeGel(new GelAnalysisRequest
        {
            ImageBytes = new byte[100],
            Width = 10,
            Height = 10,
            ExpectedLaneCount = 0
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.AnalyzeGel(new GelAnalysisRequest
        {
            ImageBytes = new byte[100],
            Width = 10,
            Height = 10,
            ExpectedLaneCount = 25
        }));

        // BackgroundThreshold < 0 or > 1
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.AnalyzeGel(new GelAnalysisRequest
        {
            ImageBytes = new byte[100],
            Width = 10,
            Height = 10,
            BackgroundThreshold = -0.1
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.AnalyzeGel(new GelAnalysisRequest
        {
            ImageBytes = new byte[100],
            Width = 10,
            Height = 10,
            BackgroundThreshold = 1.1
        }));

        // LadderLaneNumber <= 0 or > ExpectedLaneCount
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.AnalyzeGel(new GelAnalysisRequest
        {
            ImageBytes = new byte[100],
            Width = 10,
            Height = 10,
            LadderLaneNumber = 0
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.AnalyzeGel(new GelAnalysisRequest
        {
            ImageBytes = new byte[100],
            Width = 10,
            Height = 10,
            ExpectedLaneCount = 4,
            LadderLaneNumber = 5
        }));

        // ImageBytes smaller than Width * Height
        Assert.Throws<ArgumentException>(() => _service.AnalyzeGel(new GelAnalysisRequest
        {
            ImageBytes = new byte[10],
            Width = 10,
            Height = 10
        }));
    }

    /// <summary>
    /// Verifies that <see cref="GelAnalysisService.AnalyzeGelAsync"/> validates parameters and supports cancellation.
    /// </summary>
    /// <returns>A task representing the asynchronous test execution.</returns>
    [Fact]
    public async Task AnalyzeGelAsync_ValidationAndCancellation_BehavesCorrectly()
    {
        // Null stream
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.AnalyzeGelAsync(null!, 10, 10));

        using MemoryStream stream = new(new byte[100]);

        // Invalid width
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _service.AnalyzeGelAsync(stream, 0, 10));

        // Invalid height
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _service.AnalyzeGelAsync(stream, 10, 0));

        // Invalid expected lanes
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _service.AnalyzeGelAsync(stream, 10, 10, expectedLanes: 0));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _service.AnalyzeGelAsync(stream, 10, 10, expectedLanes: 25));

        // Canceled cancellation token
        using CancellationTokenSource cts = new();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _service.AnalyzeGelAsync(stream, 10, 10, ct: cts.Token));

        // Successful stream analysis
        byte[] testGel = CreateSyntheticGel(160, 100, 8, cleanMonomer: true);
        using MemoryStream validStream = new(testGel);
        GelAnalysisResult result = await _service.AnalyzeGelAsync(validStream, 160, 100, GelLadderType.OneKbDnaLadder, 8);
        Assert.NotNull(result);
        Assert.Equal(8, result.DetectedLaneCount);
    }

    /// <summary>
    /// Verifies end-to-end densitometry of an 8-lane agarose gel with high-yield folded monomer lanes.
    /// </summary>
    [Fact]
    public void AnalyzeGel_HighYieldCleanMonomerGel_PassesFoldingQualityThresholds()
    {
        int width = 160;
        int height = 100;
        int lanes = 8;
        byte[] gelBytes = CreateSyntheticGel(width, height, lanes, cleanMonomer: true);

        GelAnalysisRequest request = new()
        {
            ImageBytes = gelBytes,
            Width = width,
            Height = height,
            ExpectedLaneCount = lanes,
            LadderType = GelLadderType.OneKbDnaLadder,
            LadderLaneNumber = 1,
            BackgroundThreshold = 0.10
        };

        GelAnalysisResult result = _service.AnalyzeGel(request);

        Assert.NotNull(result);
        Assert.Equal(width, result.Width);
        Assert.Equal(height, result.Height);
        Assert.Equal(lanes, result.DetectedLaneCount);
        Assert.Equal(lanes, result.Lanes.Count);

        // Verify ladder lane
        GelLaneAnalysis ladderLane = result.Lanes[0];
        Assert.Equal(1, ladderLane.LaneNumber);
        Assert.Contains("Ladder", ladderLane.Label);
        Assert.False(ladderLane.FoldingQualityAcceptable);
        Assert.NotEmpty(ladderLane.Bands);

        // Verify experimental lanes (lanes 2 to 8)
        for (int i = 1; i < lanes; i++)
        {
            GelLaneAnalysis sampleLane = result.Lanes[i];
            Assert.Equal(i + 1, sampleLane.LaneNumber);
            Assert.Contains("Experimental", sampleLane.Label);
            Assert.True(sampleLane.MonomerBandYieldPercentage >= 65.0, $"Lane {i + 1} monomer yield was {sampleLane.MonomerBandYieldPercentage}");
            Assert.True(sampleLane.UnannealedStapleSmearPercentage <= 25.0, $"Lane {i + 1} staple smear was {sampleLane.UnannealedStapleSmearPercentage}");
            Assert.True(sampleLane.FoldingQualityAcceptable);
            Assert.NotEmpty(sampleLane.Bands);
        }

        Assert.True(result.OverallFoldingSuccessRate >= 65.0);
        Assert.Contains("acceptable folding", result.QualityAssessmentNotes);
    }

    /// <summary>
    /// Verifies that suboptimal experimental lanes with heavy unannealed staple smears are flagged as unacceptable.
    /// </summary>
    [Fact]
    public void AnalyzeGel_SuboptimalDirtySmearGel_FlagsQualityFailures()
    {
        int width = 160;
        int height = 100;
        int lanes = 8;
        byte[] gelBytes = CreateSyntheticGel(width, height, lanes, cleanMonomer: false);

        GelAnalysisRequest request = new()
        {
            ImageBytes = gelBytes,
            Width = width,
            Height = height,
            ExpectedLaneCount = lanes,
            LadderType = GelLadderType.OneKbDnaLadder,
            LadderLaneNumber = 1,
            BackgroundThreshold = 0.10
        };

        GelAnalysisResult result = _service.AnalyzeGel(request);

        Assert.NotNull(result);
        for (int i = 1; i < lanes; i++)
        {
            GelLaneAnalysis lane = result.Lanes[i];
            Assert.False(lane.FoldingQualityAcceptable);
            Assert.True(lane.UnannealedStapleSmearPercentage > 25.0 || lane.MonomerBandYieldPercentage < 65.0);
        }

        Assert.Contains("Suboptimal folding", result.QualityAssessmentNotes);
    }

    /// <summary>
    /// Verifies that multi-channel 24-bit RGB and 32-bit RGBA pixel buffers are supported cleanly.
    /// </summary>
    [Fact]
    public void AnalyzeGel_RgbAndRgbaBuffers_ProcessedAccurately()
    {
        int width = 80;
        int height = 60;
        int lanes = 4;
        byte[] grayBytes = CreateSyntheticGel(width, height, lanes, cleanMonomer: true);

        // Convert to 24-bit RGB (3 bytes/pixel)
        byte[] rgbBytes = new byte[width * height * 3];
        for (int i = 0; i < grayBytes.Length; i++)
        {
            rgbBytes[i * 3] = grayBytes[i];
            rgbBytes[(i * 3) + 1] = grayBytes[i];
            rgbBytes[(i * 3) + 2] = grayBytes[i];
        }

        GelAnalysisResult rgbResult = _service.AnalyzeGel(new GelAnalysisRequest
        {
            ImageBytes = rgbBytes,
            Width = width,
            Height = height,
            ExpectedLaneCount = lanes
        });
        Assert.Equal(lanes, rgbResult.DetectedLaneCount);

        // Convert to 32-bit RGBA (4 bytes/pixel)
        byte[] rgbaBytes = new byte[width * height * 4];
        for (int i = 0; i < grayBytes.Length; i++)
        {
            rgbaBytes[i * 4] = grayBytes[i];
            rgbaBytes[(i * 4) + 1] = grayBytes[i];
            rgbaBytes[(i * 4) + 2] = grayBytes[i];
            rgbaBytes[(i * 4) + 3] = 255;
        }

        GelAnalysisResult rgbaResult = _service.AnalyzeGel(new GelAnalysisRequest
        {
            ImageBytes = rgbaBytes,
            Width = width,
            Height = height,
            ExpectedLaneCount = lanes
        });
        Assert.Equal(lanes, rgbaResult.DetectedLaneCount);
    }

    /// <summary>
    /// Verifies handling of edge cases including all-black images, all-white images, and single-lane gels.
    /// </summary>
    [Fact]
    public void AnalyzeGel_EdgeCases_HandledDeterministically()
    {
        // 1. All-black image (zero intensity everywhere)
        byte[] black = new byte[100 * 50];
        GelAnalysisResult blackResult = _service.AnalyzeGel(new GelAnalysisRequest
        {
            ImageBytes = black,
            Width = 100,
            Height = 50,
            ExpectedLaneCount = 4
        });
        Assert.Equal(4, blackResult.DetectedLaneCount);
        Assert.Equal(0.0, blackResult.OverallFoldingSuccessRate);
        Assert.All(blackResult.Lanes, l => Assert.Empty(l.Bands));

        // 2. All-white image (uniform intensity everywhere)
        byte[] white = new byte[100 * 50];
        Array.Fill(white, (byte)255);
        GelAnalysisResult whiteResult = _service.AnalyzeGel(new GelAnalysisRequest
        {
            ImageBytes = white,
            Width = 100,
            Height = 50,
            ExpectedLaneCount = 4
        });
        Assert.Equal(4, whiteResult.DetectedLaneCount);

        // 3. Single lane gel (lane count = 1, ladder only)
        byte[] singleLaneGel = new byte[50 * 50];
        GelAnalysisResult singleResult = _service.AnalyzeGel(new GelAnalysisRequest
        {
            ImageBytes = singleLaneGel,
            Width = 50,
            Height = 50,
            ExpectedLaneCount = 1,
            LadderLaneNumber = 1
        });
        Assert.Equal(1, singleResult.DetectedLaneCount);
        Assert.Contains("Reference ladder analysis complete", singleResult.QualityAssessmentNotes);

        // 4. M13mp18 scaffold ladder and Custom ladder types
        byte[] m13Gel = CreateSyntheticGel(100, 60, 4, cleanMonomer: true);
        GelAnalysisResult m13Result = _service.AnalyzeGel(new GelAnalysisRequest
        {
            ImageBytes = m13Gel,
            Width = 100,
            Height = 60,
            ExpectedLaneCount = 4,
            LadderType = GelLadderType.M13mp18Scaffold
        });
        Assert.Equal(4, m13Result.DetectedLaneCount);

        GelAnalysisResult customResult = _service.AnalyzeGel(new GelAnalysisRequest
        {
            ImageBytes = m13Gel,
            Width = 100,
            Height = 60,
            ExpectedLaneCount = 4,
            LadderType = GelLadderType.CustomLadder
        });
        Assert.Equal(4, customResult.DetectedLaneCount);

        // 5. Short height (< 3 pixels)
        byte[] tinyGel = new byte[10 * 2];
        GelAnalysisResult tinyResult = _service.AnalyzeGel(new GelAnalysisRequest
        {
            ImageBytes = tinyGel,
            Width = 10,
            Height = 2,
            ExpectedLaneCount = 2
        });
        Assert.Equal(2, tinyResult.DetectedLaneCount);
    }

    /// <summary>
    /// Verifies that partial quality pass states generate mixed-lane diagnostic guidance notes.
    /// </summary>
    [Fact]
    public void AnalyzeGel_MixedQualityLanes_GeneratesMixedDiagnosticNotes()
    {
        int width = 120;
        int height = 80;
        int lanes = 3;
        byte[] gelBytes = new byte[width * height];
        Array.Fill(gelBytes, (byte)20);

        int laneWidth = width / lanes;

        // Lane 1: Ladder
        for (int y = 15; y <= 65; y += 12)
        {
            for (int x = 2; x < laneWidth - 2; x++)
            {
                gelBytes[(y * width) + x] = 200;
            }
        }

        // Lane 2: Good monomer lane (y = 35)
        int x2Start = laneWidth + 2;
        int x2End = (2 * laneWidth) - 2;
        for (int x = x2Start; x < x2End; x++)
        {
            gelBytes[(35 * width) + x] = 220;
        }

        // Lane 3: Heavy smear lane (y = 70)
        int x3Start = (2 * laneWidth) + 2;
        int x3End = (3 * laneWidth) - 2;
        for (int x = x3Start; x < x3End; x++)
        {
            gelBytes[(70 * width) + x] = 220;
        }

        GelAnalysisResult result = _service.AnalyzeGel(new GelAnalysisRequest
        {
            ImageBytes = gelBytes,
            Width = width,
            Height = height,
            ExpectedLaneCount = lanes,
            BackgroundThreshold = 0.10
        });

        Assert.Equal(3, result.DetectedLaneCount);
        Assert.True(result.Lanes[1].FoldingQualityAcceptable);
        Assert.False(result.Lanes[2].FoldingQualityAcceptable);
        Assert.Contains("met acceptable folding criteria", result.QualityAssessmentNotes);
        Assert.Contains("Elevated unannealed staple smear", result.QualityAssessmentNotes);
    }

    /// <summary>
    /// Verifies that <see cref="GelAnalysisRequest.GetExamples"/> and <see cref="GelAnalysisResult.GetExamples"/> produce non-null examples.
    /// </summary>
    [Fact]
    public void Models_GetExamples_ReturnPopulatedInstances()
    {
        GelAnalysisRequest req = new GelAnalysisRequest().GetExamples();
        Assert.NotNull(req);
        Assert.Equal(160, req.Width);
        Assert.Equal(100, req.Height);
        Assert.Equal(8, req.ExpectedLaneCount);

        GelAnalysisResult res = new GelAnalysisResult().GetExamples();
        Assert.NotNull(res);
        Assert.Equal(160, res.Width);
        Assert.Equal(8, res.DetectedLaneCount);
        Assert.NotEmpty(res.Lanes);
    }

    /// <summary>
    /// Verifies all boundary conditions and interpolation paths of <see cref="GelAnalysisService.EstimateBasePairs"/>.
    /// </summary>
    [Fact]
    public void EstimateBasePairs_AllCalibrationBranches_InterpolateAccurately()
    {
        // 1. Empty calibration points returns fallback 1000.0
        double emptyResult = GelAnalysisService.EstimateBasePairs(0.5, []);
        Assert.Equal(1000.0, emptyResult);

        // 2. Single calibration point returns that point's base pairs
        var singlePt = new GelAnalysisService.LadderCalibrationPoint(0.5, Math.Log10(5000.0), 5000.0);
        double singleResult = GelAnalysisService.EstimateBasePairs(0.2, [singlePt]);
        Assert.Equal(5000.0, singleResult);

        // 3. Multi-point calibration list
        var cal = new List<GelAnalysisService.LadderCalibrationPoint>
        {
            new(0.2, Math.Log10(10000.0), 10000.0),
            new(0.5, Math.Log10(4000.0), 4000.0),
            new(0.8, Math.Log10(1000.0), 1000.0)
        };

        // Clamp / extrapolate above top of ladder (rf <= 0.2)
        double topResult = GelAnalysisService.EstimateBasePairs(0.1, cal);
        Assert.True(topResult >= 10000.0);

        // Clamp / extrapolate below bottom of ladder (rf >= 0.8)
        double bottomResult = GelAnalysisService.EstimateBasePairs(0.9, cal);
        Assert.True(bottomResult <= 1000.0);

        // Interpolate between middle points (0.35 is halfway between 0.2 and 0.5)
        double midResult = GelAnalysisService.EstimateBasePairs(0.35, cal);
        Assert.True(midResult < 10000.0 && midResult > 4000.0);

        // Interpolate with zero span between duplicate points
        var dupCal = new List<GelAnalysisService.LadderCalibrationPoint>
        {
            new(0.3, Math.Log10(5000.0), 5000.0),
            new(0.3, Math.Log10(5000.0), 5000.0)
        };
        double dupResult = GelAnalysisService.EstimateBasePairs(0.3, dupCal);
        Assert.Equal(5000.0, dupResult);

        // 4-point calibration advancing to middle segment
        var fourPtCal = new List<GelAnalysisService.LadderCalibrationPoint>
        {
            new(0.1, Math.Log10(10000.0), 10000.0),
            new(0.3, Math.Log10(7000.0), 7000.0),
            new(0.6, Math.Log10(3000.0), 3000.0),
            new(0.9, Math.Log10(1000.0), 1000.0)
        };
        double midSegmentResult = GelAnalysisService.EstimateBasePairs(0.5, fourPtCal);
        Assert.True(midSegmentResult < 7000.0 && midSegmentResult > 3000.0);

        // Fallback when rf falls outside loop index
        var endCal = new List<GelAnalysisService.LadderCalibrationPoint>
        {
            new(0.2, Math.Log10(8000.0), 8000.0),
            new(0.6, Math.Log10(2000.0), 2000.0)
        };
        double endResult = GelAnalysisService.EstimateBasePairs(0.7, endCal);
        Assert.True(endResult <= 2000.0);
    }

    /// <summary>
    /// Verifies <see cref="GelAnalysisService.DetectPeaksInProfile"/> on short heights, flat signals, and threshold skips.
    /// </summary>
    [Fact]
    public void DetectPeaksInProfile_EdgeCasesAndProminence_HandledCleanly()
    {
        // 1. Height < 3 returns empty peaks
        var shortResult = GelAnalysisService.DetectPeaksInProfile([10.0, 20.0], 2, 10);
        Assert.Empty(shortResult);

        // 2. Max signal < 1.0 returns empty peaks
        var flatResult = GelAnalysisService.DetectPeaksInProfile([0.2, 0.5, 0.2], 3, 10);
        Assert.Empty(flatResult);

        // 3. Sub-threshold or non-maximum noise
        double[] noisy = [0.0, 50.0, 10.0, 10.0, 100.0, 20.0, 0.0];
        var noisyResult = GelAnalysisService.DetectPeaksInProfile(noisy, noisy.Length, 10);
        Assert.NotEmpty(noisyResult);
    }

    /// <summary>
    /// Constructs a simulated 8-bit grayscale agarose gel image containing a reference ladder and nanocarrier samples.
    /// </summary>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="lanes">Total lane count.</param>
    /// <param name="cleanMonomer">True to generate high-yield clean monomer bands; false to generate heavy staple smears.</param>
    /// <returns>Populated grayscale pixel byte array.</returns>
    private static byte[] CreateSyntheticGel(int width, int height, int lanes, bool cleanMonomer)
    {
        // WHAT: Creates a simulated gel with fluorescent DNA bands on dark background.
        // WHY:  Allows deterministic testing of peak detection, Rf migration, and yield percentages.
        // HOW:  Fills background at 25, draws 10 ladder bands in lane 1, and draws monomer bands or smears in samples.
        byte[] pixels = new byte[width * height];
        Array.Fill(pixels, (byte)25);

        int laneWidth = width / lanes;

        // Lane 1: Reference ladder bands
        int[] ladderRows =
        [
            (int)(height * 0.12),
            (int)(height * 0.20),
            (int)(height * 0.28),
            (int)(height * 0.36),
            (int)(height * 0.44),
            (int)(height * 0.52),
            (int)(height * 0.60),
            (int)(height * 0.68),
            (int)(height * 0.76),
            (int)(height * 0.84)
        ];

        foreach (int row in ladderRows)
        {
            if (row > 0 && row < height - 1)
            {
                for (int x = 2; x < laneWidth - 2; x++)
                {
                    pixels[(row * width) + x] = 210;
                    if (row + 1 < height)
                    {
                        pixels[((row + 1) * width) + x] = 160;
                    }
                }
            }
        }

        int monomerRow = Math.Clamp((int)(height * 0.45), 2, height - 3);
        int smearRow = Math.Clamp((int)(height * 0.88), monomerRow + 2, height - 2);

        // Experimental lanes: 2 to N
        for (int l = 1; l < lanes; l++)
        {
            int xStart = (l * laneWidth) + 3;
            int xEnd = ((l + 1) * laneWidth) - 3;

            if (cleanMonomer)
            {
                // Add a small dimer/multimer band in lane 2 at lower Rf (higher up)
                if (l == 1)
                {
                    int dimerRow = Math.Clamp((int)(height * 0.25), 2, monomerRow - 2);
                    for (int x = xStart; x < xEnd; x++)
                    {
                        pixels[(dimerRow * width) + x] = 80;
                    }
                }

                // High-yield sharp folded monomer band (intensity 230)
                for (int y = monomerRow - 1; y <= monomerRow + 1; y++)
                {
                    byte intensity = y == monomerRow ? (byte)230 : (byte)150;
                    for (int x = xStart; x < xEnd; x++)
                    {
                        pixels[(y * width) + x] = intensity;
                    }
                }

                // Minor staple smear (intensity 50, < 15% lane total)
                for (int x = xStart; x < xEnd; x++)
                {
                    pixels[(smearRow * width) + x] = 50;
                }
            }
            else
            {
                // Suboptimal: weak monomer (intensity 60) and heavy staple smear (intensity 220)
                for (int x = xStart; x < xEnd; x++)
                {
                    pixels[(monomerRow * width) + x] = 60;
                }

                for (int y = smearRow - 1; y <= smearRow + 1; y++)
                {
                    byte intensity = y == smearRow ? (byte)220 : (byte)180;
                    for (int x = xStart; x < xEnd; x++)
                    {
                        pixels[(y * width) + x] = intensity;
                    }
                }
            }
        }

        return pixels;
    }
}
