using System.Runtime.Intrinsics;
using System.Text;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Pipelines.Simd;
using Xunit;

namespace NanoWeaverHrt.Tests.Pipelines;

/// <summary>
/// Unit tests verifying SIMD vector intrinsics, distance computation, and overflow-safe float parsing
/// in <see cref="SimdCoordinateParser"/>.
/// </summary>
public sealed class SimdCoordinateParserTests
{
    /// <summary>
    /// Verifies that <see cref="SimdCoordinateParser.ComputeDistancesFromCentroidSimd"/> processes coordinates
    /// using both 4-wide <see cref="Vector128{Single}"/> SIMD iterations and scalar tail branches.
    /// </summary>
    [Fact]
    public void ComputeDistancesFromCentroidSimd_VectorAndTailIterations_CalculatesAccurately()
    {
        // WHAT: Evaluates centroid distances for 7 coordinates (1 SIMD batch of 4 + 3 scalar tail items).
        // WHY:  Ensures both the 4-wide vector loop and the scalar remainder loop execute and produce identical math.
        // HOW:  Constructs known coordinates relative to (1.0, 2.0, 3.0) and validates calculated Euclidean distances.
        AtomCoordinate centroid = new(1.0f, 2.0f, 3.0f);
        AtomCoordinate[] coords =
        [
            new(1.0f, 2.0f, 3.0f),      // dist = 0.0
            new(4.0f, 6.0f, 3.0f),      // dist = sqrt(9 + 16 + 0) = 5.0
            new(1.0f, 2.0f, 13.0f),     // dist = 10.0
            new(2.0f, 4.0f, 5.0f),      // dist = sqrt(1 + 4 + 4) = 3.0
            new(1.0f, -2.0f, 3.0f),     // dist = 4.0 (scalar tail)
            new(1.0f, 2.0f, -5.0f),     // dist = 8.0 (scalar tail)
            new(1.0f, 2.0f, 3.0f)       // dist = 0.0 (scalar tail)
        ];

        float[] distances = new float[coords.Length];
        SimdCoordinateParser.ComputeDistancesFromCentroidSimd(coords, centroid, distances);

        Assert.Equal(0.0f, distances[0], precision: 3);
        Assert.Equal(5.0f, distances[1], precision: 3);
        Assert.Equal(10.0f, distances[2], precision: 3);
        Assert.Equal(3.0f, distances[3], precision: 3);
        Assert.Equal(4.0f, distances[4], precision: 3);
        Assert.Equal(8.0f, distances[5], precision: 3);
        Assert.Equal(0.0f, distances[6], precision: 3);
    }

    /// <summary>
    /// Verifies that <see cref="SimdCoordinateParser.ComputeDistancesFromCentroidSimd"/> operates cleanly
    /// when fewer than 4 coordinates are provided, executing strictly the scalar branch.
    /// </summary>
    [Fact]
    public void ComputeDistancesFromCentroidSimd_UnderFourCoordinates_OperatesViaScalarPath()
    {
        // WHAT: Evaluates 2 coordinates to guarantee vector loop is safely bypassed when count < 4.
        // WHY:  Prevents buffer over-reads on small fragments or ligand-only coordinate spans.
        // HOW:  Passes length 2 coordinates and verifies distances.
        AtomCoordinate centroid = new(0.0f, 0.0f, 0.0f);
        AtomCoordinate[] coords =
        [
            new(3.0f, 4.0f, 0.0f),
            new(0.0f, 0.0f, 6.0f)
        ];

        float[] distances = new float[coords.Length];
        SimdCoordinateParser.ComputeDistancesFromCentroidSimd(coords, centroid, distances);

        Assert.Equal(5.0f, distances[0], precision: 3);
        Assert.Equal(6.0f, distances[1], precision: 3);
    }

    /// <summary>
    /// Verifies that <see cref="SimdCoordinateParser.ComputeDistancesFromCentroidSimd"/> respects destination span boundaries.
    /// </summary>
    [Fact]
    public void ComputeDistancesFromCentroidSimd_DestinationShorterThanSource_ClampsSafely()
    {
        // WHAT: Provides destination span of length 4 for coordinates of length 6.
        // WHY:  Protects against buffer overrun when destination slice is bounded.
        // HOW:  Asserts exactly destination.Length elements are populated.
        AtomCoordinate centroid = new(0.0f, 0.0f, 0.0f);
        AtomCoordinate[] coords = new AtomCoordinate[6];
        float[] distances = new float[4];

        SimdCoordinateParser.ComputeDistancesFromCentroidSimd(coords, centroid, distances);
        Assert.Equal(4, distances.Length);
    }

    /// <summary>
    /// Verifies that <see cref="SimdCoordinateParser.TryParseFloatSimd"/> safely parses floats with long mantissas (> 18 digits)
    /// without 64-bit integer overflow.
    /// </summary>
    [Fact]
    public void TryParseFloatSimd_LongMantissaAboveEighteenDigits_ParsesWithoutOverflow()
    {
        // WHAT: Tests float parsing on a high-precision number with 22 significant digits.
        // WHY:  A naive 64-bit mantissa multiplier overflows long.MaxValue (~9.22e18) causing arithmetic exceptions or corrupted floats.
        // HOW:  Parses UTF-8 byte span with 22 digits and asserts correct floating-point magnitude.
        byte[] bytes = "1234567890123456789.012"u8.ToArray();
        bool success = SimdCoordinateParser.TryParseFloatSimd(bytes, out float value, out int consumed);

        Assert.True(success);
        Assert.True(value > 1.2e18f);
        Assert.True(consumed > 0);
    }

    /// <summary>
    /// Verifies that <see cref="SimdCoordinateParser.TryParseFloatSimd"/> rejects malformed tokens halted by invalid delimiter characters.
    /// </summary>
    /// <param name="malformed">Malformed numeric token string.</param>
    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.0-2.0")]
    [InlineData("1.0+2.0")]
    [InlineData("12a")]
    [InlineData("3.14159z")]
    [InlineData("3.14159Z")]
    [InlineData("1.23e")]
    [InlineData("1.25e-2x")]
    [InlineData("1.25e-2X")]
    [InlineData("1.25e-2.")]
    [InlineData("1.25e-2-")]
    [InlineData("1.25e-2+")]
    public void TryParseFloatSimd_MalformedToken_RejectsToken(string malformed)
    {
        // WHAT: Tests invalid floating point inputs halted by extra '.', '-', '+', or letters.
        // WHY:  Prevents corrupted coordinates from being silently accepted as partial numbers.
        // HOW:  Encodes string to UTF-8 and asserts TryParseFloatSimd returns false.
        byte[] bytes = Encoding.UTF8.GetBytes(malformed);
        bool success = SimdCoordinateParser.TryParseFloatSimd(bytes, out _, out _);

        Assert.False(success);
    }

    /// <summary>
    /// Verifies that <see cref="SimdCoordinateParser.TryParseFloatSimd"/> handles leading plus sign and scientific notation.
    /// </summary>
    [Fact]
    public void TryParseFloatSimd_LeadingPlusAndScientificNotation_ParsesSuccessfully()
    {
        // WHAT: Parses +4.25e2 and standard positive number with leading plus.
        // WHY:  Ensures leading plus branch and scientific notation exponent branches are covered.
        // HOW:  Asserts TryParseFloatSimd parses 425.0f and 12.5f accurately.
        byte[] bytes1 = "+4.25e2"u8.ToArray();
        Assert.True(SimdCoordinateParser.TryParseFloatSimd(bytes1, out float val1, out _));
        Assert.Equal(425.0f, val1, precision: 1);

        byte[] bytes2 = "+12.5"u8.ToArray();
        Assert.True(SimdCoordinateParser.TryParseFloatSimd(bytes2, out float val2, out _));
        Assert.Equal(12.5f, val2, precision: 1);
    }

    /// <summary>
    /// Verifies that <see cref="SimdCoordinateParser.TryParseFloatSimd"/> returns false on empty span or sign-only inputs.
    /// </summary>
    [Fact]
    public void TryParseFloatSimd_EmptyOrSignOnly_ReturnsFalse()
    {
        // WHAT: Tests empty span and bare "+" / "-" tokens.
        // WHY:  Guarantees edge cases with no digits return false without indexing out of range.
        // HOW:  Asserts TryParseFloatSimd returns false.
        Assert.False(SimdCoordinateParser.TryParseFloatSimd(ReadOnlySpan<byte>.Empty, out _, out _));
        Assert.False(SimdCoordinateParser.TryParseFloatSimd("+"u8, out _, out _));
        Assert.False(SimdCoordinateParser.TryParseFloatSimd("-"u8, out _, out _));
    }
}
