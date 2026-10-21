using System.Text;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Pipelines.Simd;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Unit test suite verifying the hardware-accelerated SIMD coordinate parser routines.
/// </summary>
public sealed class SimdParserTests
{
    /// <summary>
    /// Verifies that <see cref="SimdCoordinateParser.FindFirstWhitespaceSimd"/> accurately locates spaces and tabs.
    /// </summary>
    [Fact]
    public void FindFirstWhitespaceSimd_ShouldLocateWhitespaceIndices()
    {
        // WHAT: Tests whitespace detection in byte spans.
        // WHY:  Ensures SIMD vector scanning locates delimiter boundaries accurately.
        // HOW:  Passes spans with delimiters at various offsets.
        byte[] data1 = "AtomName 12.345 67.890"u8.ToArray();
        int index1 = SimdCoordinateParser.FindFirstWhitespaceSimd(data1);
        Assert.Equal(8, index1);

        byte[] data2 = "NoWhitespaceHereAtAll"u8.ToArray();
        int index2 = SimdCoordinateParser.FindFirstWhitespaceSimd(data2);
        Assert.Equal(-1, index2);

        byte[] data3 = "LongIdentifierNameWithPadding\t12.0"u8.ToArray();
        int index3 = SimdCoordinateParser.FindFirstWhitespaceSimd(data3);
        Assert.Equal(29, index3);
    }

    /// <summary>
    /// Verifies that <see cref="SimdCoordinateParser.TrimStartWhitespaceSimd"/> removes leading spaces and tabs.
    /// </summary>
    [Fact]
    public void TrimStartWhitespaceSimd_ShouldStripLeadingWhitespace()
    {
        byte[] input = "   \t  -15.420"u8.ToArray();
        ReadOnlySpan<byte> trimmed = SimdCoordinateParser.TrimStartWhitespaceSimd(input);
        string result = Encoding.UTF8.GetString(trimmed);
        Assert.Equal("-15.420", result);
    }

    /// <summary>
    /// Verifies that <see cref="SimdCoordinateParser.TryParseFloatSimd"/> accurately parses positive, negative, and decimal floats.
    /// </summary>
    [Theory]
    [InlineData("0.0", 0.0f)]
    [InlineData("12.345", 12.345f)]
    [InlineData("-89.1234", -89.1234f)]
    [InlineData("105.5", 105.5f)]
    [InlineData("-0.005", -0.005f)]
    [InlineData("1.25e-2", 0.0125f)]
    public void TryParseFloatSimd_ShouldParseAccurateFloatValues(string text, float expected)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        bool success = SimdCoordinateParser.TryParseFloatSimd(bytes, out float value, out _);

        Assert.True(success);
        Assert.Equal(expected, value, precision: 3);
    }

    /// <summary>
    /// Verifies that <see cref="SimdCoordinateParser.TryParseCoordinateLineSimd"/> extracts 3D Cartesian coordinates.
    /// </summary>
    [Fact]
    public void TryParseCoordinateLineSimd_ShouldExtractXyzValues()
    {
        byte[] line = "C    -12.500   24.125   -3.750"u8.ToArray();
        bool success = SimdCoordinateParser.TryParseCoordinateLineSimd(line, out AtomCoordinate coord);

        Assert.True(success);
        Assert.Equal(-12.500f, coord.X, precision: 3);
        Assert.Equal(24.125f, coord.Y, precision: 3);
        Assert.Equal(-3.750f, coord.Z, precision: 3);
    }

    /// <summary>
    /// Verifies that <see cref="SimdCoordinateParser.InterleaveCoordinatesSimd"/> packs component arrays into struct spans.
    /// </summary>
    [Fact]
    public void InterleaveCoordinatesSimd_ShouldCorrectlyInterleaveComponents()
    {
        float[] x = [1f, 2f, 3f, 4f, 5f];
        float[] y = [10f, 20f, 30f, 40f, 50f];
        float[] z = [100f, 200f, 300f, 400f, 500f];

        AtomCoordinate[] destination = new AtomCoordinate[5];
        SimdCoordinateParser.InterleaveCoordinatesSimd(x, y, z, destination);

        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(x[i], destination[i].X);
            Assert.Equal(y[i], destination[i].Y);
            Assert.Equal(z[i], destination[i].Z);
        }
    }

    /// <summary>
    /// Verifies that <see cref="SimdCoordinateParser.ComputeDistancesFromCentroidSimd"/> calculates Euclidean distance from origin.
    /// </summary>
    [Fact]
    public void ComputeDistancesFromCentroidSimd_ShouldCalculateRadialDistances()
    {
        AtomCoordinate[] coords =
        [
            new AtomCoordinate(3f, 4f, 0f),      // dist = 5 from (0,0,0)
            new AtomCoordinate(0f, 0f, 10f),     // dist = 10 from (0,0,0)
            new AtomCoordinate(1f, 2f, 2f)       // dist = 3 from (0,0,0)
        ];

        AtomCoordinate centroid = new(0f, 0f, 0f);
        float[] distances = new float[coords.Length];

        SimdCoordinateParser.ComputeDistancesFromCentroidSimd(coords, centroid, distances);

        Assert.Equal(5f, distances[0], precision: 3);
        Assert.Equal(10f, distances[1], precision: 3);
        Assert.Equal(3f, distances[2], precision: 3);
    }
}
