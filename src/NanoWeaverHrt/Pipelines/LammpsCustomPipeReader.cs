using System.Buffers;
using System.Buffers.Text;
using System.IO.Pipelines;
using NanoWeaverHrt.Abstractions;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Pipelines.Simd;

namespace NanoWeaverHrt.Pipelines;

/// <summary>
/// High-throughput zero-allocation pipeline reader for LAMMPS custom text trajectory dump files (.lammpstrj / .dump).
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is the LAMMPS Dump Format:</strong> LAMMPS (Large-scale Atomic/Molecular Massively Parallel Simulator)
/// outputs molecular trajectories in formatted snapshot blocks:
/// <list type="bullet">
/// <item><description><c>ITEM: TIMESTEP</c> followed by the simulation timestep integer.</description></item>
/// <item><description><c>ITEM: NUMBER OF ATOMS</c> followed by the atom count for that frame.</description></item>
/// <item><description><c>ITEM: BOX BOUNDS</c> followed by 3 lines of simulation cell boundaries (xlo xhi, ylo yhi, zlo zhi).</description></item>
/// <item><description><c>ITEM: ATOMS id type x y z ...</c> followed by N lines of atomic properties and Cartesian coordinates.</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Biomedical Context:</strong> LAMMPS is utilized to simulate synthetic gene regulatory toggles, 
/// DNA origami nanocarrier folding kinetics, and receptor-ligand docking over nanosecond time scales.
/// </para>
/// </remarks>
public sealed class LammpsCustomPipeReader : ITrajectoryParser
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
    /// UTF-8 prefix marking the start of a LAMMPS snapshot section ("ITEM: ").
    /// </summary>
    private static readonly byte[] ItemPrefix = "ITEM: "u8.ToArray();

    /// <summary>
    /// UTF-8 tag marking atom count section ("NUMBER OF ATOMS").
    /// </summary>
    private static readonly byte[] NumberOfAtomsTag = "NUMBER OF ATOMS"u8.ToArray();

    /// <summary>
    /// UTF-8 tag marking the coordinate table section ("ATOMS").
    /// </summary>
    private static readonly byte[] AtomsTag = "ATOMS"u8.ToArray();

    /// <summary>
    /// UTF-8 tag marking simulation box boundary lines ("BOX BOUNDS").
    /// </summary>
    private static readonly byte[] BoxBoundsTag = "BOX BOUNDS"u8.ToArray();

    /// <summary>
    /// UTF-8 Byte Order Mark (BOM) preamble (0xEF, 0xBB, 0xBF).
    /// </summary>
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <inheritdoc/>
    public string FormatName => "LAMMPS_CUSTOM";

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
        int currentAtomIndex = 0;
        bool inAtomsSection = false;
        bool expectAtomCountLine = false;
        int boxBoundsLinesToSkip = 0;

        AtomCoordinate[] atomBuffer = Array.Empty<AtomCoordinate>();
        byte[] lineByteBuffer = ArrayPool<byte>.Shared.Rent(TrajectoryLineBuffer.InitialLineCapacity);

        try
        {
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
                    // WHY:  A LAMMPS dump row legitimately carries many columns (id type x y z vx vy vz fx fy fz q
                    //       ...), so lines past 512 bytes are routine rather than exotic. The Math.Min clamp this
                    //       replaced cut them short and parsed the remainder as if complete, which could yield a
                    //       wrong coordinate with nothing logged and nothing thrown (finding L5).
                    // HOW:  TrajectoryLineBuffer.TryCopyLine grows the ArrayPool rental to fit, or returns false for
                    //       a line beyond the documented ceiling.
                    if (!TrajectoryLineBuffer.TryCopyLine(lineSequence, ref lineByteBuffer, out int lineLength))
                    {
                        // WHAT: Abandons the snapshot currently being accumulated and resynchronises.
                        // WHY:  Skipping just the corrupt line would leave a gap that the next snapshot's atom rows
                        //       would quietly fill, emitting a frame blended from two timesteps and presenting it as
                        //       complete. Discarding the partial snapshot makes that impossible, and matches how this
                        //       reader already treats a malformed count: non-fatal, state reset, keep scanning.
                        // HOW:  Clears every section flag so only a fresh "ITEM:" header can start the next snapshot.
                        inAtomsSection = false;
                        expectAtomCountLine = false;
                        boxBoundsLinesToSkip = 0;
                        expectedAtomCount = -1;
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

                    // ---------------------------------------------------------
                    // 1. Process Section Headers ("ITEM: ...")
                    // ---------------------------------------------------------
                    if (lineSpan.StartsWith(ItemPrefix))
                    {
                        ReadOnlySpan<byte> headerRemainder = lineSpan[ItemPrefix.Length..];

                        if (headerRemainder.StartsWith(NumberOfAtomsTag))
                        {
                            expectAtomCountLine = true;
                            inAtomsSection = false;
                            continue;
                        }

                        if (headerRemainder.StartsWith(BoxBoundsTag))
                        {
                            boxBoundsLinesToSkip = 3;
                            inAtomsSection = false;
                            continue;
                        }

                        if (headerRemainder.StartsWith(AtomsTag))
                        {
                            inAtomsSection = true;
                            currentAtomIndex = 0;
                            continue;
                        }

                        inAtomsSection = false;
                        continue;
                    }

                    // ---------------------------------------------------------
                    // 2. Read Atom Count Value
                    // ---------------------------------------------------------
                    if (expectAtomCountLine)
                    {
                        // WHAT: Reads the integer that follows "ITEM: NUMBER OF ATOMS" and bounds it before any
                        //       memory is reserved.
                        // WHY:  The value comes from an untrusted dump file, so it must never size an allocation by
                        //       itself (AGENTS.md Prime Directive 1 / finding H4). Zero, negative and over-ceiling
                        //       counts are all treated as a malformed section, exactly as a non-numeric line was.
                        // HOW:  Utf8Parser.TryParse decodes in-place and IsPlausibleAtomCount gates the result. The
                        //       coordinate buffer is not rented here; it grows on demand as real atom lines arrive.
                        expectAtomCountLine = false;
                        if (Utf8Parser.TryParse(lineSpan, out int count, out _) && TrajectoryFrameBuffer.IsPlausibleAtomCount(count))
                        {
                            expectedAtomCount = count;
                        }
                        continue;
                    }

                    // ---------------------------------------------------------
                    // 3. Skip Box Bounds Lines (3 lines)
                    // ---------------------------------------------------------
                    if (boxBoundsLinesToSkip > 0)
                    {
                        boxBoundsLinesToSkip--;
                        continue;
                    }

                    // ---------------------------------------------------------
                    // 4. Parse Atom Coordinate Lines via SIMD
                    // ---------------------------------------------------------
                    if (inAtomsSection && expectedAtomCount > 0)
                    {
                        // WHAT: Parses atomic coordinates from lines formatted as "id type x y z [vx vy vz ...]"
                        // WHY:  Zero string allocations using SimdCoordinateParser.
                        // HOW:  Skips the first two integer columns (id, type) then parses floating-point X, Y, Z.
                        if (TryParseLammpsAtomLine(lineSpan, out AtomCoordinate coordinate))
                        {
                            // WHAT: Expands the rented coordinate buffer only once the atoms to fill it have arrived.
                            // WHY:  Keeps reserved memory proportional to the coordinates the dump really contains
                            //       rather than to the count its header declared (finding H4).
                            // HOW:  TrajectoryFrameBuffer.Grow rents the larger buffer, copies the live prefix and
                            //       returns the old rental, leaving one live rental for the finally block to release.
                            if (currentAtomIndex == atomBuffer.Length)
                            {
                                atomBuffer = TrajectoryFrameBuffer.Grow(atomBuffer, expectedAtomCount, currentAtomIndex);
                            }

                            atomBuffer[currentAtomIndex++] = coordinate;

                            if (currentAtomIndex == expectedAtomCount)
                            {
                                await onFrameAvailable(frameIndex++, atomBuffer.AsSpan(0, expectedAtomCount));
                                inAtomsSection = false;
                                expectedAtomCount = -1;
                                currentAtomIndex = 0;
                            }
                        }
                    }
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lineByteBuffer);
            if (atomBuffer.Length > 0)
            {
                ArrayPool<AtomCoordinate>.Shared.Return(atomBuffer);
            }
        }
    }

    /// <summary>
    /// Parses a single LAMMPS custom dump atom line formatted as "id type x y z" into an <see cref="AtomCoordinate"/>.
    /// </summary>
    /// <param name="span">The UTF-8 line byte span.</param>
    /// <param name="coordinate">The parsed 3D coordinate on success.</param>
    /// <returns>True if spatial coordinates were successfully extracted; otherwise false.</returns>
    internal static bool TryParseLammpsAtomLine(ReadOnlySpan<byte> span, out AtomCoordinate coordinate)
    {
        coordinate = default;
        ReadOnlySpan<byte> remaining = SimdCoordinateParser.TrimStartWhitespaceSimd(span);

        // Skip Column 1: Atom ID
        int sep1 = SimdCoordinateParser.FindFirstWhitespaceSimd(remaining);
        if (sep1 == -1) return false;
        remaining = SimdCoordinateParser.TrimStartWhitespaceSimd(remaining[sep1..]);

        // Skip Column 2: Atom Type
        int sep2 = SimdCoordinateParser.FindFirstWhitespaceSimd(remaining);
        if (sep2 == -1) return false;
        remaining = SimdCoordinateParser.TrimStartWhitespaceSimd(remaining[sep2..]);

        // Parse Column 3: X coordinate via SIMD
        if (!SimdCoordinateParser.TryParseFloatSimd(remaining, out float x, out int bytesX))
        {
            return false;
        }
        remaining = SimdCoordinateParser.TrimStartWhitespaceSimd(remaining[bytesX..]);

        // Parse Column 4: Y coordinate via SIMD
        if (!SimdCoordinateParser.TryParseFloatSimd(remaining, out float y, out int bytesY))
        {
            return false;
        }
        remaining = SimdCoordinateParser.TrimStartWhitespaceSimd(remaining[bytesY..]);

        // Parse Column 5: Z coordinate via SIMD
        if (!SimdCoordinateParser.TryParseFloatSimd(remaining, out float z, out _))
        {
            return false;
        }

        coordinate = new AtomCoordinate(x, y, z);
        return true;
    }

    /// <summary>
    /// Slices a line delimiter from the byte buffer without allocating heap memory.
    /// </summary>
    /// <param name="buffer">The sequence of unconsumed pipeline bytes.</param>
    /// <param name="line">The extracted line slice.</param>
    /// <returns>True if a newline was located; otherwise false.</returns>
    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
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
}
