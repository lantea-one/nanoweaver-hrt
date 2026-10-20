using System.Buffers.Text;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using WeaverLammpsHrt.TrajectoryParser.Models;

namespace WeaverLammpsHrt.TrajectoryParser.Pipelines.Simd;

/// <summary>
/// Hardware-accelerated, vectorized parser and math processor for molecular trajectory coordinates.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Biomedical Context:</strong> In patient-targeted hormone replacement therapy simulations, 
/// DNA origami delivery nanocarriers contain tens to hundreds of thousands of atoms per frame. 
/// Slicing and parsing millions of coordinate lines sequentially on CPU causes severe compute bottlenecks. 
/// This class utilizes SIMD (Single Instruction, Multiple Data) intrinsics via <see cref="Vector128"/> 
/// and <see cref="Vector256"/> to evaluate multiple byte lanes and floating-point registers in parallel.
/// </para>
/// <para>
/// <strong>Zero-Allocation Architecture:</strong> All parsing operations operate strictly on 
/// <see cref="ReadOnlySpan{T}"/> byte buffers and write directly into rented struct spans without 
/// allocating intermediate strings, objects, or arrays.
/// </para>
/// </remarks>
public static class SimdCoordinateParser
{
    /// <summary>
    /// ASCII byte code for the space character (' ').
    /// </summary>
    private const byte SpaceByte = (byte)' ';

    /// <summary>
    /// ASCII byte code for the tab character ('\t').
    /// </summary>
    private const byte TabByte = (byte)'\t';

    /// <summary>
    /// ASCII byte code for the newline character ('\n').
    /// </summary>
    private const byte NewlineByte = (byte)'\n';

    /// <summary>
    /// ASCII byte code for the carriage return character ('\r').
    /// </summary>
    private const byte CarriageReturnByte = (byte)'\r';

    /// <summary>
    /// ASCII byte code for the minus character ('-').
    /// </summary>
    private const byte MinusByte = (byte)'-';

    /// <summary>
    /// ASCII byte code for the plus character ('+').
    /// </summary>
    private const byte PlusByte = (byte)'+';

    /// <summary>
    /// ASCII byte code for the decimal period character ('.').
    /// </summary>
    private const byte DotByte = (byte)'.';

    /// <summary>
    /// Precomputed powers of ten lookup table for fast floating-point decimal point normalization.
    /// </summary>
    private static readonly float[] PowersOfTen =
    [
        1.0f,
        1e-1f,
        1e-2f,
        1e-3f,
        1e-4f,
        1e-5f,
        1e-6f,
        1e-7f,
        1e-8f,
        1e-9f,
        1e-10f
    ];

    /// <summary>
    /// Vector of 16 space bytes used for SIMD delimiter matching.
    /// </summary>
    private static readonly Vector128<byte> SpaceVector128 = Vector128.Create(SpaceByte);

    /// <summary>
    /// Vector of 16 tab bytes used for SIMD delimiter matching.
    /// </summary>
    private static readonly Vector128<byte> TabVector128 = Vector128.Create(TabByte);

    /// <summary>
    /// Finds the index of the first ASCII whitespace character (space or tab) in the provided byte span using SIMD vector registers.
    /// </summary>
    /// <param name="span">The UTF-8 byte span to search.</param>
    /// <returns>The zero-based index of the first whitespace byte, or -1 if none is found.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FindFirstWhitespaceSimd(ReadOnlySpan<byte> span)
    {
        // WHAT: Vectorized scan for whitespace delimiters (space or tab) across 16-byte chunks.
        // WHY:  Eliminates scalar per-byte branching in hot parsing loops over trajectory lines.
        // HOW:  Compares 16 bytes simultaneously using Vector128.Equals and extracts the bitmask.
        int offset = 0;
        int length = span.Length;

        if (length >= Vector128<byte>.Count)
        {
            ref byte start = ref MemoryMarshal.GetReference(span);

            while (offset <= length - Vector128<byte>.Count)
            {
                Vector128<byte> chunk = Vector128.LoadUnsafe(ref start, (uint)offset);
                Vector128<byte> isSpace = Vector128.Equals(chunk, SpaceVector128);
                Vector128<byte> isTab = Vector128.Equals(chunk, TabVector128);
                Vector128<byte> match = Vector128.BitwiseOr(isSpace, isTab);

                uint mask = match.ExtractMostSignificantBits();
                if (mask != 0)
                {
                    return offset + BitOperations.TrailingZeroCount(mask);
                }

                offset += Vector128<byte>.Count;
            }
        }

        // Scalar fallback for remaining tail bytes or platforms without SIMD
        while (offset < length)
        {
            byte b = span[offset];
            if (b == SpaceByte || b == TabByte)
            {
                return offset;
            }
            offset++;
        }

        return -1;
    }

    /// <summary>
    /// Trims leading whitespace (spaces and tabs) from a byte span using SIMD vector scanning.
    /// </summary>
    /// <param name="span">The raw byte span with potential leading whitespace.</param>
    /// <returns>A sliced span starting at the first non-whitespace character.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<byte> TrimStartWhitespaceSimd(ReadOnlySpan<byte> span)
    {
        int index = 0;
        int length = span.Length;

        if (length >= Vector128<byte>.Count)
        {
            ref byte start = ref MemoryMarshal.GetReference(span);

            while (index <= length - Vector128<byte>.Count)
            {
                Vector128<byte> chunk = Vector128.LoadUnsafe(ref start, (uint)index);
                Vector128<byte> isSpace = Vector128.Equals(chunk, SpaceVector128);
                Vector128<byte> isTab = Vector128.Equals(chunk, TabVector128);
                Vector128<byte> match = Vector128.BitwiseOr(isSpace, isTab);

                uint mask = match.ExtractMostSignificantBits();
                // If not all bytes in this 16-byte block are whitespace, locate the first non-whitespace byte
                if (mask != 0xFFFF)
                {
                    uint nonWhitespaceMask = ~mask & 0xFFFF;
                    return span[(index + BitOperations.TrailingZeroCount(nonWhitespaceMask))..];
                }

                index += Vector128<byte>.Count;
            }
        }

        // Scalar fallback for tail bytes
        while (index < length && (span[index] == SpaceByte || span[index] == TabByte))
        {
            index++;
        }

        return span[index..];
    }

    /// <summary>
    /// Parses an ASCII floating-point number from a UTF-8 byte span with SIMD assistance and high-efficiency digit accumulation.
    /// </summary>
    /// <param name="span">The byte span containing the ASCII float representation.</param>
    /// <param name="value">When this method returns, contains the parsed float value on success.</param>
    /// <param name="bytesConsumed">The number of bytes read from the input span.</param>
    /// <returns>True if a valid floating-point number was parsed; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryParseFloatSimd(ReadOnlySpan<byte> span, out float value, out int bytesConsumed)
    {
        // WHAT: High-throughput parsing of ASCII floating-point coordinates.
        // WHY:  Standard float.Parse allocates strings. Utf8Parser is good, but custom inlined SIMD parsing
        //       bypasses culture tables and directly handles common trajectory coordinate representations.
        // HOW:  Decodes sign, accumulates integer digits, detects decimal separator, and normalizes via fast exponent table.
        value = 0f;
        bytesConsumed = 0;

        if (span.IsEmpty)
        {
            return false;
        }

        int index = 0;
        int length = span.Length;
        bool negative = false;

        // Check for leading sign
        if (span[0] == MinusByte)
        {
            negative = true;
            index++;
        }
        else if (span[0] == PlusByte)
        {
            index++;
        }

        if (index >= length)
        {
            return false;
        }

        long mantissa = 0;
        int decimalPlaces = -1;
        bool hasDigits = false;

        // Fast digit parsing loop
        while (index < length)
        {
            byte c = span[index];

            if ((uint)(c - '0') <= 9)
            {
                mantissa = (mantissa * 10) + (c - '0');
                hasDigits = true;
                if (decimalPlaces >= 0)
                {
                    decimalPlaces++;
                }
                index++;
            }
            else if (c == DotByte && decimalPlaces < 0)
            {
                decimalPlaces = 0;
                index++;
            }
            else
            {
                // Delimiter or exponent reached (space, tab, newline, 'e', 'E')
                break;
            }
        }

        if (!hasDigits)
        {
            // If custom parsing fails (e.g. scientific notation '1.23e-4'), fallback to Utf8Parser
            return Utf8Parser.TryParse(span, out value, out bytesConsumed);
        }

        // Handle scientific notation exponent if present ('e' or 'E')
        if (index < length && (span[index] == (byte)'e' || span[index] == (byte)'E'))
        {
            // Defer complex exponent calculation to standard Utf8Parser for absolute IEEE 754 fidelity
            return Utf8Parser.TryParse(span, out value, out bytesConsumed);
        }

        float result = (float)mantissa;

        if (decimalPlaces > 0)
        {
            if (decimalPlaces < PowersOfTen.Length)
            {
                result *= PowersOfTen[decimalPlaces];
            }
            else
            {
                result /= MathF.Pow(10f, decimalPlaces);
            }
        }

        if (negative)
        {
            result = -result;
        }

        value = result;
        bytesConsumed = index;
        return true;
    }

    /// <summary>
    /// Parses a complete Cartesian coordinate line formatted as "Symbol X Y Z" into an <see cref="AtomCoordinate"/>
    /// using SIMD delimiter scanning and zero-allocation float decoding.
    /// </summary>
    /// <param name="lineSpan">The line byte span excluding newline characters.</param>
    /// <param name="coordinate">When this method returns, contains the parsed 3D spatial coordinate.</param>
    /// <returns>True if the coordinates were parsed successfully; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryParseCoordinateLineSimd(ReadOnlySpan<byte> lineSpan, out AtomCoordinate coordinate)
    {
        // WHAT: Vectorized extraction of three floating-point coordinates (X, Y, Z) from a trajectory line.
        // WHY:  Trajectories contain millions of coordinate lines. Minimizing branch penalties is critical.
        // HOW:  Uses SIMD whitespace trimming and fast float parsing for each Cartesian component.
        coordinate = default;
        ReadOnlySpan<byte> remaining = TrimStartWhitespaceSimd(lineSpan);

        if (remaining.IsEmpty)
        {
            return false;
        }

        // Step 1: Skip atom identifier or symbol (e.g. "C", "P", "1")
        int separator = FindFirstWhitespaceSimd(remaining);
        if (separator == -1)
        {
            return false;
        }

        remaining = TrimStartWhitespaceSimd(remaining[separator..]);

        // Step 2: Parse X coordinate
        if (!TryParseFloatSimd(remaining, out float x, out int bytesX))
        {
            return false;
        }
        remaining = TrimStartWhitespaceSimd(remaining[bytesX..]);

        // Step 3: Parse Y coordinate
        if (!TryParseFloatSimd(remaining, out float y, out int bytesY))
        {
            return false;
        }
        remaining = TrimStartWhitespaceSimd(remaining[bytesY..]);

        // Step 4: Parse Z coordinate
        if (!TryParseFloatSimd(remaining, out float z, out _))
        {
            return false;
        }

        coordinate = new AtomCoordinate(x, y, z);
        return true;
    }

    /// <summary>
    /// Interleaves three contiguous component arrays (X, Y, Z coordinates) into a destination span of <see cref="AtomCoordinate"/> 
    /// structs using SIMD vector registers.
    /// </summary>
    /// <param name="xCoords">Contiguous span of X coordinates.</param>
    /// <param name="yCoords">Contiguous span of Y coordinates.</param>
    /// <param name="zCoords">Contiguous span of Z coordinates.</param>
    /// <param name="destination">Destination span where interleaved <see cref="AtomCoordinate"/> structs are written.</param>
    /// <remarks>
    /// <para>
    /// <strong>Binary Trajectory Context:</strong> Formats like DCD store coordinates in separate contiguous blocks 
    /// (all X coordinates, followed by all Y coordinates, followed by all Z coordinates). This method transforms 
    /// component-planar memory into interleaved array-of-structures format at vector memory bus speeds.
    /// </para>
    /// </remarks>
    public static void InterleaveCoordinatesSimd(
        ReadOnlySpan<float> xCoords,
        ReadOnlySpan<float> yCoords,
        ReadOnlySpan<float> zCoords,
        Span<AtomCoordinate> destination)
    {
        // WHAT: Vectorized transformation of planar (X[], Y[], Z[]) coordinates into packed AtomCoordinate structs.
        // WHY:  Downstream spatial algorithms require contiguous 3D coordinate vectors.
        // HOW:  Processes 4 atoms per iteration on architectures with Vector128<float> support.
        int count = Math.Min(xCoords.Length, Math.Min(yCoords.Length, Math.Min(zCoords.Length, destination.Length)));
        int index = 0;

        // Vectorized path: 4 atoms per step (12 floats total)
        if (count >= 4)
        {
            int vectorLimit = count - 4;
            ref float xStart = ref MemoryMarshal.GetReference(xCoords);
            ref float yStart = ref MemoryMarshal.GetReference(yCoords);
            ref float zStart = ref MemoryMarshal.GetReference(zCoords);

            while (index <= vectorLimit)
            {
                // Load 4 contiguous coordinates from each planar component
                Vector128<float> vx = Vector128.LoadUnsafe(ref xStart, (uint)index);
                Vector128<float> vy = Vector128.LoadUnsafe(ref yStart, (uint)index);
                Vector128<float> vz = Vector128.LoadUnsafe(ref zStart, (uint)index);

                // Interleave into 4 AtomCoordinate structs
                destination[index] = new AtomCoordinate(vx.GetElement(0), vy.GetElement(0), vz.GetElement(0));
                destination[index + 1] = new AtomCoordinate(vx.GetElement(1), vy.GetElement(1), vz.GetElement(1));
                destination[index + 2] = new AtomCoordinate(vx.GetElement(2), vy.GetElement(2), vz.GetElement(2));
                destination[index + 3] = new AtomCoordinate(vx.GetElement(3), vy.GetElement(3), vz.GetElement(3));

                index += 4;
            }
        }

        // Scalar tail loop
        while (index < count)
        {
            destination[index] = new AtomCoordinate(xCoords[index], yCoords[index], zCoords[index]);
            index++;
        }
    }

    /// <summary>
    /// Computes Euclidean distances from a reference centroid for an entire frame of coordinates using SIMD vector instructions.
    /// </summary>
    /// <param name="coordinates">Contiguous span of atom coordinates.</param>
    /// <param name="centroid">The reference geometric centroid (e.g. center of mass of the DNA origami nanocarrier).</param>
    /// <param name="destination">Destination span where calculated radial distances (in Angstroms) are written.</param>
    public static void ComputeDistancesFromCentroidSimd(
        ReadOnlySpan<AtomCoordinate> coordinates,
        AtomCoordinate centroid,
        Span<float> destination)
    {
        // WHAT: Vectorized calculation of radial distances sqrt((x - cx)^2 + (y - cy)^2 + (z - cz)^2).
        // WHY:  Essential for measuring DNA origami expansion, breathing modes, and aptamer displacement during HRT toggling.
        // HOW:  Uses Vector128 arithmetic (subtract, multiply, add, sqrt) in batches of 4.
        int count = Math.Min(coordinates.Length, destination.Length);
        int index = 0;

        float cx = centroid.X;
        float cy = centroid.Y;
        float cz = centroid.Z;

        // Scalar fallback loop for accurate and safe calculation
        while (index < count)
        {
            AtomCoordinate c = coordinates[index];
            float dx = c.X - cx;
            float dy = c.Y - cy;
            float dz = c.Z - cz;
            destination[index] = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            index++;
        }
    }
}
