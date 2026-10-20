using System.Buffers;
using System.Buffers.Text;
using System.IO.Pipelines;
using HrtLab.TrajectoryParser.Abstractions;
using HrtLab.TrajectoryParser.Models;

namespace HrtLab.TrajectoryParser.Pipelines;

/// <summary>
/// High-throughput, zero-allocation parser for the plain-text XYZ molecular trajectory file format.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is the XYZ format:</strong> XYZ is one of the most widely used scientific formats for recording 
/// atomic coordinates over time. Each simulation frame consists of:
/// <list type="number">
/// <item><description>Line 1: An integer <c>N</c> specifying the number of atoms in this frame.</description></item>
/// <item><description>Line 2: A comment line (often describing timestep or simulation box dimensions).</description></item>
/// <item><description>Lines 3 through N+2: Each line contains an atom identifier followed by X, Y, Z coordinates.</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Zero-Allocation Architecture:</strong> Standard parsers split lines into strings, causing millions of 
/// string allocations per second. This implementation slices raw UTF-8 byte sequences directly and parses 
/// numbers using <see cref="Utf8Parser"/>, yielding zero heap allocations in steady-state parsing.
/// </para>
/// </remarks>
public sealed class XyzPipeReader : ITrajectoryParser
{
    /// <summary>
    /// ASCII byte code representing the newline character ('\n').
    /// </summary>
    private static readonly byte NewLineByte = (byte)'\n';

    /// <summary>
    /// ASCII byte code representing the carriage return character ('\r').
    /// </summary>
    private static readonly byte CarriageReturnByte = (byte)'\r';

    /// <summary>
    /// ASCII byte code representing the space character (' ').
    /// </summary>
    private static readonly byte SpaceByte = (byte)' ';

    /// <summary>
    /// ASCII byte code representing the horizontal tab character ('\t').
    /// </summary>
    private static readonly byte TabByte = (byte)'\t';

    /// <inheritdoc/>
    public string FormatName => "XYZ";

    /// <inheritdoc/>
    public async Task ParseAsync(
        PipeReader reader,
        FrameParsedCallback onFrameAvailable,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(onFrameAvailable);

        long frameIndex = 0;
        int expectedAtomCount = -1;
        bool commentLineRead = false;
        int currentAtomIndex = 0;
        AtomCoordinate[] atomBuffer = Array.Empty<AtomCoordinate>();
        byte[] lineByteBuffer = ArrayPool<byte>.Shared.Rent(512);

        try
        {
            // WHAT: Continues reading chunks of raw bytes from the pipeline until cancelled or EOF.
            // WHY:  Pipelines handle I/O backpressure automatically and avoid allocating Large Object Heap buffers.
            // HOW:  Awaits ReadAsync, processes all complete lines in the buffer, and advances the reader cursor.
            while (!cancellationToken.IsCancellationRequested)
            {
                ReadResult result = await reader.ReadAsync(cancellationToken);
                ReadOnlySequence<byte> buffer = result.Buffer;

                while (TryReadLine(ref buffer, out ReadOnlySequence<byte> lineSequence))
                {
                    if (lineSequence.IsEmpty)
                    {
                        continue;
                    }

                    // WHAT: Copies line bytes into rented byte buffer and trims carriage return if needed.
                    // WHY:  Avoids stackalloc Span across async/await yield boundaries.
                    // HOW:  Uses ArrayPool-rented array and slices via ReadOnlySpan without heap allocations.
                    int lineLength = (int)Math.Min(lineSequence.Length, lineByteBuffer.Length);
                    lineSequence.Slice(0, lineLength).CopyTo(lineByteBuffer);

                    int effectiveLength = lineLength;
                    if (effectiveLength > 0 && lineByteBuffer[effectiveLength - 1] == CarriageReturnByte)
                    {
                        effectiveLength--;
                    }

                    if (effectiveLength == 0)
                    {
                        continue;
                    }

                    // -------------------------------------------------------------
                    // STATE 1: Reading Atom Count Header Line
                    // -------------------------------------------------------------
                    if (expectedAtomCount == -1)
                    {
                        // WHAT: Parses the integer count of atoms in this snapshot directly from UTF-8 bytes.
                        // WHY:  Avoids converting the count to a C# string before calling int.Parse.
                        // HOW:  Utf8Parser.TryParse decodes the numeric value in-place.
                        if (Utf8Parser.TryParse(lineByteBuffer.AsSpan(0, effectiveLength), out int count, out _) && count > 0)
                        {
                            expectedAtomCount = count;
                            commentLineRead = false;

                            // Ensure our rented coordinate buffer is large enough for this frame
                            if (atomBuffer.Length < expectedAtomCount)
                            {
                                if (atomBuffer.Length > 0)
                                {
                                    ArrayPool<AtomCoordinate>.Shared.Return(atomBuffer);
                                }
                                atomBuffer = ArrayPool<AtomCoordinate>.Shared.Rent(expectedAtomCount);
                            }
                            currentAtomIndex = 0;
                        }
                        continue;
                    }

                    // -------------------------------------------------------------
                    // STATE 2: Reading Comment / Metadata Line
                    // -------------------------------------------------------------
                    if (!commentLineRead && expectedAtomCount > 0)
                    {
                        // WHAT: Skips or inspects the single comment line immediately following the atom count.
                        // WHY:  Standard XYZ format mandates exactly one comment/metadata line per frame before coordinates.
                        // HOW:  Marks commentLineRead as true and advances immediately to atom coordinate lines.
                        commentLineRead = true;
                        continue;
                    }

                    // -------------------------------------------------------------
                    // STATE 3: Reading Atom Coordinate Lines (Symbol X Y Z)
                    // -------------------------------------------------------------
                    bool isAtomLine = ParseAtomLine(lineByteBuffer.AsSpan(0, effectiveLength), out AtomCoordinate coordinate);
                    if (isAtomLine)
                    {
                        atomBuffer[currentAtomIndex++] = coordinate;

                        // WHAT: Emits the completed frame once all expected atoms for this step are accumulated.
                        // WHY:  Allows downstream streaming analysis without copying entire frame arrays to the heap.
                        // HOW:  Passes a ReadOnlySpan slice of the rented buffer to the callback delegate.
                        if (currentAtomIndex == expectedAtomCount)
                        {
                            await onFrameAvailable(frameIndex++, atomBuffer.AsSpan(0, expectedAtomCount));

                            // Reset state for the subsequent frame in the trajectory stream
                            expectedAtomCount = -1;
                            commentLineRead = false;
                            currentAtomIndex = 0;
                        }
                    }
                }

                // Advance the reader cursor to notify the pipeline of consumed bytes
                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        finally
        {
            // Return rented buffers to prevent memory leakage
            ArrayPool<byte>.Shared.Return(lineByteBuffer);
            if (atomBuffer.Length > 0)
            {
                ArrayPool<AtomCoordinate>.Shared.Return(atomBuffer);
            }
        }
    }

    /// <summary>
    /// Searches for a newline delimiter in the byte sequence and extracts a complete line slice without copying.
    /// </summary>
    /// <param name="buffer">The sequence of unconsumed pipeline bytes, advanced past the delimiter on success.</param>
    /// <param name="line">The extracted line slice excluding the newline delimiter.</param>
    /// <returns>True if a newline delimiter was found; otherwise false.</returns>
    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        // WHAT: Locates the first occurrence of '\n' in the multi-segment byte buffer.
        // WHY:  Trajectories are processed streamingly; data arrives in arbitrary chunk sizes.
        // HOW:  Uses SequencePosition and ReadOnlySequence.PositionOf for zero-copy slicing.
        SequencePosition? position = buffer.PositionOf(NewLineByte);
        if (position == null)
        {
            line = default;
            return false;
        }

        line = buffer.Slice(0, position.Value);
        buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
        return true;
    }

    /// <summary>
    /// Trims leading ASCII space and tab characters from a byte span without allocations.
    /// </summary>
    /// <param name="span">The input byte span.</param>
    /// <returns>A slice of <paramref name="span"/> with leading whitespace removed.</returns>
    private static ReadOnlySpan<byte> TrimStartWhitespace(ReadOnlySpan<byte> span)
    {
        int index = 0;
        while (index < span.Length && (span[index] == SpaceByte || span[index] == TabByte))
        {
            index++;
        }
        return span[index..];
    }

    /// <summary>
    /// Parses a single XYZ coordinate line formatted as "Identifier X Y Z" into an <see cref="AtomCoordinate"/>.
    /// </summary>
    /// <param name="span">The UTF-8 encoded line byte span.</param>
    /// <param name="coordinate">The parsed 3D spatial coordinate on success.</param>
    /// <returns>True if all three spatial coordinates (X, Y, Z) were successfully parsed; otherwise false.</returns>
    private static bool ParseAtomLine(ReadOnlySpan<byte> span, out AtomCoordinate coordinate)
    {
        coordinate = default;
        ReadOnlySpan<byte> remaining = TrimStartWhitespace(span);

        // WHAT: Skips the leading atom symbol or bead identifier (e.g., "C", "N", "P", "1").
        // WHY:  We only need the Cartesian floats for spatial analysis and bounding computations.
        // HOW:  Finds the first whitespace delimiter (space or tab) and slices past it.
        int separatorIndex = remaining.IndexOfAny(SpaceByte, TabByte);
        if (separatorIndex == -1)
        {
            return false;
        }

        remaining = TrimStartWhitespace(remaining[separatorIndex..]);

        // WHAT: Parses the X coordinate float directly from UTF-8 bytes.
        // WHY:  Zero string allocations.
        // HOW:  Utf8Parser.TryParse directly writes out float value and consumed byte count.
        if (!Utf8Parser.TryParse(remaining, out float x, out int bytesReadX))
        {
            return false;
        }
        remaining = TrimStartWhitespace(remaining[bytesReadX..]);

        // Parse Y coordinate
        if (!Utf8Parser.TryParse(remaining, out float y, out int bytesReadY))
        {
            return false;
        }
        remaining = TrimStartWhitespace(remaining[bytesReadY..]);

        // Parse Z coordinate
        if (!Utf8Parser.TryParse(remaining, out float z, out _))
        {
            return false;
        }

        coordinate = new AtomCoordinate(x, y, z);
        return true;
    }
}
