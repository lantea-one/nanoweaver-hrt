using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using HrtLab.TrajectoryParser.Abstractions;
using HrtLab.TrajectoryParser.Models;

namespace HrtLab.TrajectoryParser.Pipelines;

/// <summary>
/// High-performance zero-allocation binary parser for the DCD (CHARMM/NAMD/LAMMPS) trajectory file format.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is the DCD format:</strong> DCD is a standard binary format for storing molecular dynamics trajectories. 
/// Because it stores coordinates as raw 32-bit floating-point numbers in Fortran-unformatted binary blocks rather 
/// than ASCII characters, it is significantly more compact and faster to parse than plain-text formats like XYZ.
/// </para>
/// <para>
/// <strong>Binary Frame Structure:</strong>
/// In DCD files, coordinates for each frame are stored in three sequential contiguous blocks:
/// <list type="bullet">
/// <item><description>Block 1: All X coordinates for atoms 1..N (N * 4 bytes)</description></item>
/// <item><description>Block 2: All Y coordinates for atoms 1..N (N * 4 bytes)</description></item>
/// <item><description>Block 3: All Z coordinates for atoms 1..N (N * 4 bytes)</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class DcdPipeReader : ITrajectoryParser
{
    /// <inheritdoc/>
    public string FormatName => "DCD";

    /// <summary>
    /// Magic 4-character signature marking a valid DCD trajectory ("CORD").
    /// </summary>
    private static readonly byte[] DcdSignature = "CORD"u8.ToArray();

    /// <inheritdoc/>
    public async Task ParseAsync(
        PipeReader reader,
        FrameParsedCallback onFrameAvailable,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(onFrameAvailable);

        long frameIndex = 0;
        int numberOfAtoms = -1;
        bool headerParsed = false;
        bool hasUnitCell = false;

        float[] rentedX = Array.Empty<float>();
        float[] rentedY = Array.Empty<float>();
        float[] rentedZ = Array.Empty<float>();
        AtomCoordinate[] atomBuffer = Array.Empty<AtomCoordinate>();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ReadResult result = await reader.ReadAsync(cancellationToken);
                ReadOnlySequence<byte> buffer = result.Buffer;

                // -------------------------------------------------------------
                // STAGE 1: Parse DCD Binary Header
                // -------------------------------------------------------------
                if (!headerParsed)
                {
                    // DCD Header is typically at least 100-200 bytes. Wait until we have enough bytes.
                    if (buffer.Length < 104)
                    {
                        reader.AdvanceTo(buffer.Start, buffer.End);
                        if (result.IsCompleted) break;
                        continue;
                    }

                    // WHAT: Verifies the DCD "CORD" signature and extracts atom count and unit cell flag.
                    // WHY:  Confirms the file is a valid DCD stream before proceeding.
                    // HOW:  Reads Fortran unformatted record sizes and header integer flags.
                    SequenceReader<byte> seqReader = new(buffer);
                    if (!TryParseHeader(ref seqReader, out numberOfAtoms, out hasUnitCell))
                    {
                        // Needs more data or invalid header
                        reader.AdvanceTo(buffer.Start, buffer.End);
                        if (result.IsCompleted) break;
                        continue;
                    }

                    buffer = buffer.Slice(seqReader.Position);
                    headerParsed = true;

                    // Pre-allocate rented coordinate buffers
                    rentedX = ArrayPool<float>.Shared.Rent(numberOfAtoms);
                    rentedY = ArrayPool<float>.Shared.Rent(numberOfAtoms);
                    rentedZ = ArrayPool<float>.Shared.Rent(numberOfAtoms);
                    atomBuffer = ArrayPool<AtomCoordinate>.Shared.Rent(numberOfAtoms);
                }

                // -------------------------------------------------------------
                // STAGE 2: Parse Consecutive Binary Coordinate Frames
                // -------------------------------------------------------------
                while (numberOfAtoms > 0)
                {
                    // Calculate required bytes for a complete frame:
                    // Optional unit cell: 4-byte Fortran marker + 48 bytes (6 doubles) + 4-byte marker = 56 bytes
                    // X Block: 4 marker + (N * 4) + 4 marker = 8 + (N * 4)
                    // Y Block: 8 + (N * 4)
                    // Z Block: 8 + (N * 4)
                    int framePayloadBytes = (hasUnitCell ? 56 : 0) + (3 * (8 + (numberOfAtoms * 4)));

                    if (buffer.Length < framePayloadBytes)
                    {
                        // Buffer does not yet contain a complete binary frame
                        break;
                    }

                    SequenceReader<byte> frameSeq = new(buffer.Slice(0, framePayloadBytes));

                    // Skip unit cell block if present (contains periodic box dimensions)
                    if (hasUnitCell)
                    {
                        frameSeq.Advance(56);
                    }

                    // WHAT: Reads contiguous floats for X, Y, and Z blocks.
                    // WHY:  DCD stores coordinates grouped by dimension rather than interleaved (x1,x2.. y1,y2.. z1,z2..).
                    // HOW:  Reads raw bytes directly into rented float arrays using MemoryMarshal.
                    ReadCoordinateBlock(ref frameSeq, rentedX.AsSpan(0, numberOfAtoms));
                    ReadCoordinateBlock(ref frameSeq, rentedY.AsSpan(0, numberOfAtoms));
                    ReadCoordinateBlock(ref frameSeq, rentedZ.AsSpan(0, numberOfAtoms));

                    // Interleave X, Y, Z floats into AtomCoordinate structs without heap allocations
                    for (int i = 0; i < numberOfAtoms; i++)
                    {
                        atomBuffer[i] = new AtomCoordinate(rentedX[i], rentedY[i], rentedZ[i]);
                    }

                    // Emit parsed frame to downstream pipeline
                    await onFrameAvailable(frameIndex++, atomBuffer.AsSpan(0, numberOfAtoms));

                    // Advance buffer past this completed frame
                    buffer = buffer.Slice(framePayloadBytes);
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted) break;
            }
        }
        finally
        {
            // Return all rented buffers to pools
            if (rentedX.Length > 0) ArrayPool<float>.Shared.Return(rentedX);
            if (rentedY.Length > 0) ArrayPool<float>.Shared.Return(rentedY);
            if (rentedZ.Length > 0) ArrayPool<float>.Shared.Return(rentedZ);
            if (atomBuffer.Length > 0) ArrayPool<AtomCoordinate>.Shared.Return(atomBuffer);
        }
    }

    /// <summary>
    /// Reads and validates the binary header of a DCD trajectory stream.
    /// </summary>
    /// <param name="reader">The sequence reader positioned at the start of the stream.</param>
    /// <param name="atomCount">The extracted number of atoms in the trajectory.</param>
    /// <param name="hasUnitCell">True if the trajectory includes periodic box dimensions in each frame.</param>
    /// <returns>True if the header was successfully parsed; otherwise false.</returns>
    private static bool TryParseHeader(ref SequenceReader<byte> reader, out int atomCount, out bool hasUnitCell)
    {
        atomCount = 0;
        hasUnitCell = false;

        // Fortran record leading 4-byte integer marker
        if (!reader.TryReadLittleEndian(out int recordLength) || recordLength < 84)
        {
            return false;
        }

        Span<byte> magic = stackalloc byte[4];
        if (!reader.TryCopyTo(magic)) return false;
        reader.Advance(4);

        if (!magic.SequenceEqual(DcdSignature))
        {
            return false;
        }

        // Advance through DCD header integer array (20 ints * 4 bytes = 80 bytes)
        Span<byte> headerInts = stackalloc byte[76];
        if (!reader.TryCopyTo(headerInts)) return false;
        reader.Advance(76);

        // Header int index 10 (offset 40) is 1 if unit cell data is present
        int unitCellFlag = BinaryPrimitives.ReadInt32LittleEndian(headerInts.Slice(36, 4));
        hasUnitCell = (unitCellFlag == 1);

        // Trailing Fortran record marker
        if (!reader.TryReadLittleEndian(out int trailingRecord) || trailingRecord != recordLength)
        {
            return false;
        }

        // Title record
        if (!reader.TryReadLittleEndian(out int titleLength)) return false;
        reader.Advance(titleLength);
        if (!reader.TryReadLittleEndian(out int _)) return false;

        // Atom count record
        if (!reader.TryReadLittleEndian(out int natomRecordLength) || natomRecordLength != 4) return false;
        if (!reader.TryReadLittleEndian(out atomCount) || atomCount <= 0) return false;
        if (!reader.TryReadLittleEndian(out int _)) return false;

        return true;
    }

    /// <summary>
    /// Reads a single Fortran-unformatted block of contiguous 32-bit floats into the target coordinate span.
    /// </summary>
    /// <param name="reader">The sequence reader positioned at the Fortran record marker.</param>
    /// <param name="destination">The destination span to populate with floats.</param>
    private static void ReadCoordinateBlock(ref SequenceReader<byte> reader, Span<float> destination)
    {
        // Read leading Fortran block size marker (4 bytes)
        int blockMarker;
        reader.TryReadLittleEndian(out blockMarker);

        // WHAT: Reads raw float bytes into destination span.
        // WHY:  Avoids intermediate memory copies.
        // HOW:  Reads byte slice and converts little-endian integers to floats in-place.
        for (int i = 0; i < destination.Length; i++)
        {
            reader.TryReadLittleEndian(out int rawBits);
            destination[i] = BitConverter.Int32BitsToSingle(rawBits);
        }

        // Read trailing Fortran block size marker (4 bytes)
        reader.TryReadLittleEndian(out blockMarker);
    }
}
