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
        byte[] lineByteBuffer = ArrayPool<byte>.Shared.Rent(512);

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
                        expectAtomCountLine = false;
                        if (Utf8Parser.TryParse(lineSpan, out int count, out _) && count > 0)
                        {
                            expectedAtomCount = count;
                            if (atomBuffer.Length < expectedAtomCount)
                            {
                                if (atomBuffer.Length > 0)
                                {
                                    ArrayPool<AtomCoordinate>.Shared.Return(atomBuffer);
                                }
                                atomBuffer = ArrayPool<AtomCoordinate>.Shared.Rent(expectedAtomCount);
                            }
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
