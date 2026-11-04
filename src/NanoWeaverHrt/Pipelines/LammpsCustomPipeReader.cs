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
        int colX = 2;
        int colY = 3;
        int colZ = 4;

        AtomCoordinate[] atomBuffer = Array.Empty<AtomCoordinate>();
        byte[] lineByteBuffer = ArrayPool<byte>.Shared.Rent(TrajectoryLineBuffer.InitialLineCapacity);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ReadResult result = await reader.ReadAsync(cancellationToken);
                ReadOnlySequence<byte> buffer = result.Buffer;

                while (true)
                {
                    ReadOnlySequence<byte> lineSequence;
                    if (TryReadLine(ref buffer, out lineSequence))
                    {
                        // Successfully read a newline-terminated line
                    }
                    else if (result.IsCompleted && !buffer.IsEmpty)
                    {
                        // WHAT: Flushes and parses the final trailing line at EOF when a trailing newline is absent.
                        // WHY:  Simulation trajectories frequently end on a final coordinate row without a trailing '\n'.
                        // HOW:  Takes the remaining buffer as the line sequence and advances the buffer slice to its end.
                        lineSequence = buffer;
                        buffer = buffer.Slice(buffer.End);
                    }
                    else
                    {
                        break;
                    }

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
                            ResolveLammpsColumns(headerRemainder[AtomsTag.Length..], out colX, out colY, out colZ);
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
                        if (TryParseLammpsAtomLine(lineSpan, colX, colY, colZ, out AtomCoordinate coordinate))
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
    /// Parses a single LAMMPS custom dump atom line formatted as "id type x y z" into an <see cref="AtomCoordinate"/>
    /// using standard column positions (2, 3, 4).
    /// </summary>
    /// <param name="span">The UTF-8 line byte span.</param>
    /// <param name="coordinate">The parsed 3D coordinate on success.</param>
    /// <returns>True if spatial coordinates were successfully extracted; otherwise false.</returns>
    internal static bool TryParseLammpsAtomLine(ReadOnlySpan<byte> span, out AtomCoordinate coordinate)
        => TryParseLammpsAtomLine(span, 2, 3, 4, out coordinate);

    /// <summary>
    /// Parses a single LAMMPS custom dump atom line extracting coordinates from dynamically resolved column indices.
    /// </summary>
    /// <param name="span">The UTF-8 line byte span.</param>
    /// <param name="colX">0-based column index containing the X coordinate (or xs/xu).</param>
    /// <param name="colY">0-based column index containing the Y coordinate (or ys/yu).</param>
    /// <param name="colZ">0-based column index containing the Z coordinate (or zs/zu).</param>
    /// <param name="coordinate">The parsed 3D coordinate on success.</param>
    /// <returns>True if spatial coordinates were successfully extracted; otherwise false.</returns>
    internal static bool TryParseLammpsAtomLine(ReadOnlySpan<byte> span, int colX, int colY, int colZ, out AtomCoordinate coordinate)
    {
        // WHAT: Zero-allocation extraction of 3D coordinates from dynamic LAMMPS column offsets.
        // WHY:  Allows reading custom dump files with varying column ordering (e.g. id type xs ys zs vx vy vz).
        // HOW:  Iterates across whitespace-delimited columns, parsing floats at matching column indices and skipping others.
        coordinate = default;
        ReadOnlySpan<byte> remaining = SimdCoordinateParser.TrimStartWhitespaceSimd(span);
        int currentCol = 0;
        float x = 0f, y = 0f, z = 0f;
        int foundCount = 0;
        int maxCol = Math.Max(colX, Math.Max(colY, colZ));

        while (!remaining.IsEmpty && currentCol <= maxCol)
        {
            if (currentCol == colX)
            {
                if (!SimdCoordinateParser.TryParseFloatSimd(remaining, out x, out int bytesConsumed)) return false;
                remaining = SimdCoordinateParser.TrimStartWhitespaceSimd(remaining[bytesConsumed..]);
                foundCount++;
            }
            else if (currentCol == colY)
            {
                if (!SimdCoordinateParser.TryParseFloatSimd(remaining, out y, out int bytesConsumed)) return false;
                remaining = SimdCoordinateParser.TrimStartWhitespaceSimd(remaining[bytesConsumed..]);
                foundCount++;
            }
            else if (currentCol == colZ)
            {
                if (!SimdCoordinateParser.TryParseFloatSimd(remaining, out z, out int bytesConsumed)) return false;
                remaining = SimdCoordinateParser.TrimStartWhitespaceSimd(remaining[bytesConsumed..]);
                foundCount++;
            }
            else
            {
                int sep = SimdCoordinateParser.FindFirstWhitespaceSimd(remaining);
                if (sep == -1)
                {
                    return false;
                }
                remaining = SimdCoordinateParser.TrimStartWhitespaceSimd(remaining[sep..]);
            }
            currentCol++;
        }

        if (foundCount == 3)
        {
            coordinate = new AtomCoordinate(x, y, z);
            return true;
        }

        return false;
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

    /// <summary>
    /// Parses column name identifiers following "ITEM: ATOMS" to dynamically identify 0-based column indices
    /// for X, Y, and Z Cartesian coordinates.
    /// </summary>
    /// <param name="columnsSpan">The remainder of the "ITEM: ATOMS" header line after the "ATOMS" keyword.</param>
    /// <param name="colX">The resolved 0-based column index for the X coordinate.</param>
    /// <param name="colY">The resolved 0-based column index for the Y coordinate.</param>
    /// <param name="colZ">The resolved 0-based column index for the Z coordinate.</param>
    internal static void ResolveLammpsColumns(ReadOnlySpan<byte> columnsSpan, out int colX, out int colY, out int colZ)
    {
        // WHAT: Dynamically resolves column positions for x/xs/xu, y/ys/yu, and z/zs/zu from the LAMMPS ATOMS header.
        // WHY:  Eliminates hardcoded (2, 3, 4) column assumptions for diverse MD dump specifications.
        // HOW:  Scans tokens delimited by whitespace, matching coordinate names, and falls back to (2, 3, 4).
        colX = 2;
        colY = 3;
        colZ = 4;

        ReadOnlySpan<byte> remaining = SimdCoordinateParser.TrimStartWhitespaceSimd(columnsSpan);
        if (remaining.IsEmpty)
        {
            return;
        }

        int currentColumn = 0;
        int foundX = -1;
        int foundY = -1;
        int foundZ = -1;

        while (!remaining.IsEmpty)
        {
            int sep = SimdCoordinateParser.FindFirstWhitespaceSimd(remaining);
            ReadOnlySpan<byte> token = sep == -1 ? remaining : remaining[..sep];
            remaining = sep == -1 ? default : SimdCoordinateParser.TrimStartWhitespaceSimd(remaining[sep..]);

            if (token.SequenceEqual("x"u8) || token.SequenceEqual("xs"u8) || token.SequenceEqual("xu"u8))
            {
                foundX = currentColumn;
            }
            else if (token.SequenceEqual("y"u8) || token.SequenceEqual("ys"u8) || token.SequenceEqual("yu"u8))
            {
                foundY = currentColumn;
            }
            else if (token.SequenceEqual("z"u8) || token.SequenceEqual("zs"u8) || token.SequenceEqual("zu"u8))
            {
                foundZ = currentColumn;
            }

            currentColumn++;
        }

        if (foundX >= 0 && foundY >= 0 && foundZ >= 0)
        {
            colX = foundX;
            colY = foundY;
            colZ = foundZ;
        }
    }
}
