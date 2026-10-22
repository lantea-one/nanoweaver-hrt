using System.Buffers;
using System.Buffers.Text;
using System.IO.Pipelines;
using NanoWeaverHrt.Abstractions;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Pipelines.Simd;

namespace NanoWeaverHrt.Pipelines;

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
    /// UTF-8 Byte Order Mark (BOM) preamble (0xEF, 0xBB, 0xBF).
    /// </summary>
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

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
        byte[] lineByteBuffer = ArrayPool<byte>.Shared.Rent(TrajectoryLineBuffer.InitialLineCapacity);

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

                    // WHAT: Copies the whole line into the rented byte buffer, growing it when the line is wider.
                    // WHY:  Avoids stackalloc Span across async/await yield boundaries, and - unlike the Math.Min
                    //       clamp this replaced - can never hand a truncated line to the coordinate parser. A line
                    //       cut short mid-number used to parse as a different, wrong coordinate with no error at all
                    //       (finding L5).
                    // HOW:  TrajectoryLineBuffer.TryCopyLine grows the ArrayPool rental to fit, or returns false for
                    //       a line beyond the documented ceiling.
                    if (!TrajectoryLineBuffer.TryCopyLine(lineSequence, ref lineByteBuffer, out int lineLength))
                    {
                        // WHAT: Abandons the frame currently being accumulated and resynchronises.
                        // WHY:  Merely skipping the corrupt line would leave a hole that the next frame's coordinate
                        //       lines would quietly fill, emitting one frame stitched together from two different
                        //       simulation timesteps - a wrong result presented as a complete one. Discarding the
                        //       partial frame makes that impossible, and matches how these readers already treat a
                        //       malformed header: non-fatal, state reset, keep scanning.
                        // HOW:  Resets to STATE 1 so the next atom-count header starts a clean frame.
                        expectedAtomCount = -1;
                        commentLineRead = false;
                        currentAtomIndex = 0;
                        continue;
                    }

                    int effectiveLength = lineLength;
                    if (effectiveLength > 0 && lineByteBuffer[effectiveLength - 1] == CarriageReturnByte)
                    {
                        effectiveLength--;
                    }

                    if (effectiveLength == 0)
                    {
                        continue;
                    }

                    ReadOnlySpan<byte> lineSpan = lineByteBuffer.AsSpan(0, effectiveLength);
                    if (lineSpan.StartsWith(Utf8Bom))
                    {
                        lineSpan = lineSpan[Utf8Bom.Length..];
                    }

                    // -------------------------------------------------------------
                    // STATE 1: Reading Atom Count Header Line
                    // -------------------------------------------------------------
                    if (expectedAtomCount == -1)
                    {
                        // WHAT: Parses the integer count of atoms in this snapshot directly from UTF-8 bytes and
                        //       checks it against the documented per-frame ceiling before any memory is reserved.
                        // WHY:  This count comes from an untrusted file, so it must never size an allocation on its
                        //       own (AGENTS.md Prime Directive 1 / finding H4). A count that is zero, negative or
                        //       above TrajectoryFrameBuffer.MaxAtomsPerFrame is treated exactly like a non-numeric
                        //       header line: the reader stays in STATE 1 and waits for a plausible count.
                        // HOW:  Utf8Parser.TryParse decodes the numeric value in-place; IsPlausibleAtomCount gates it.
                        //       No buffer is rented here at all - the coordinate buffer grows on demand in STATE 3 as
                        //       real atoms arrive, so a tiny hostile file cannot induce a large rental.
                        if (Utf8Parser.TryParse(lineSpan, out int count, out _) && TrajectoryFrameBuffer.IsPlausibleAtomCount(count))
                        {
                            expectedAtomCount = count;
                            commentLineRead = false;
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
                    bool isAtomLine = ParseAtomLine(lineSpan, out AtomCoordinate coordinate);
                    if (isAtomLine)
                    {
                        // WHAT: Expands the rented coordinate buffer only once the atoms to fill it have arrived.
                        // WHY:  Keeps the reserved memory proportional to the coordinates the file really contains
                        //       rather than to the width its header declared (finding H4).
                        // HOW:  TrajectoryFrameBuffer.Grow rents the larger buffer, copies the live prefix and returns
                        //       the previous rental, leaving exactly one live rental for the finally block to release.
                        if (currentAtomIndex == atomBuffer.Length)
                        {
                            atomBuffer = TrajectoryFrameBuffer.Grow(atomBuffer, expectedAtomCount, currentAtomIndex);
                        }

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
    /// Parses a single XYZ coordinate line formatted as "Identifier X Y Z" into an <see cref="AtomCoordinate"/>
    /// using SIMD-accelerated whitespace scanning and float decoding.
    /// </summary>
    /// <param name="span">The UTF-8 encoded line byte span.</param>
    /// <param name="coordinate">The parsed 3D spatial coordinate on success.</param>
    /// <returns>True if all three spatial coordinates (X, Y, Z) were successfully parsed; otherwise false.</returns>
    internal static bool ParseAtomLine(ReadOnlySpan<byte> span, out AtomCoordinate coordinate)
    {
        // WHAT: Vectorized coordinate line parser delegation.
        // WHY:  Standardizes SIMD conversion routines across all pipeline readers.
        // HOW:  Delegates directly to SimdCoordinateParser.TryParseCoordinateLineSimd.
        return SimdCoordinateParser.TryParseCoordinateLineSimd(span, out coordinate);
    }
}
