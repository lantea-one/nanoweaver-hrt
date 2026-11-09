using System.IO.Pipelines;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Pipelines;
using Xunit;

namespace NanoWeaverHrt.Tests.Pipelines;

/// <summary>
/// Unit tests verifying binary framing validation, Fortran marker verification, SIMD coordinate interleaving,
/// and unit cell handling in <see cref="DcdPipeReader"/>.
/// </summary>
public sealed class DcdPipeReaderTests
{
    /// <summary>
    /// Verifies that <see cref="DcdPipeReader.ParseAsync"/> parses a trajectory containing unit cell data
    /// and 5 atoms (exercising 4-wide SIMD coordinate interleaving and scalar tail interleaving).
    /// </summary>
    [Fact]
    public async Task ParseAsync_WithUnitCellAndFiveAtoms_ParsesViaSimdInterleaving()
    {
        // WHAT: Synthesizes a valid DCD stream with unitCellFlag = 1 (56-byte unit cell block) and 5 atoms.
        // WHY:  Verifies unit cell skipping and InterleaveCoordinatesSimd vector + scalar tail execution.
        // HOW:  Encodes header with unit cell flag, unit cell block, and 5-atom coordinate records.
        float[] x = [1f, 2f, 3f, 4f, 5f];
        float[] y = [10f, 20f, 30f, 40f, 50f];
        float[] z = [100f, 200f, 300f, 400f, 500f];

        byte[] dcdBytes = CreateSyntheticDcdBytes(
            atomCount: 5,
            xCoords: x,
            yCoords: y,
            zCoords: z,
            hasUnitCell: true);

        using MemoryStream ms = new(dcdBytes);
        PipeReader reader = PipeReader.Create(ms);

        DcdPipeReader parser = new();
        List<AtomCoordinate[]> frames = [];

        await parser.ParseAsync(reader, (_, coords) =>
        {
            frames.Add(coords.ToArray());
            return ValueTask.CompletedTask;
        });

        Assert.Single(frames);
        Assert.Equal(5, frames[0].Length);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(x[i], frames[0][i].X);
            Assert.Equal(y[i], frames[0][i].Y);
            Assert.Equal(z[i], frames[0][i].Z);
        }
    }

    /// <summary>
    /// Verifies that <see cref="DcdPipeReader.ParseAsync"/> rejects a stream with a corrupted leading coordinate block marker.
    /// </summary>
    [Fact]
    public async Task ParseAsync_CorruptedLeadingBlockMarker_RejectsFrame()
    {
        // WHAT: Corrupts the leading block marker integer of the X coordinate block.
        // WHY:  Ensures ReadCoordinateBlock validates leadingMarker == destination.Length * 4 and aborts gracefully.
        // HOW:  Flips bytes of the leading marker and verifies no frames are emitted.
        byte[] dcdBytes = CreateSyntheticDcdBytes(
            atomCount: 2,
            xCoords: [1f, 2f],
            yCoords: [3f, 4f],
            zCoords: [5f, 6f],
            hasUnitCell: false);

        // Header is 84 + 8 + 4 + 4 + 8 + 4 + 4 + 8 = ~120 bytes; corrupt coordinate block marker
        // Find first coordinate block size (8 bytes = 2 atoms * 4 bytes)
        int blockMarkerOffset = dcdBytes.Length - (3 * (4 + 8 + 4));
        dcdBytes[blockMarkerOffset] = 0xFF; // Corrupt marker size

        using MemoryStream ms = new(dcdBytes);
        PipeReader reader = PipeReader.Create(ms);

        DcdPipeReader parser = new();
        List<AtomCoordinate[]> frames = [];

        await parser.ParseAsync(reader, (_, coords) =>
        {
            frames.Add(coords.ToArray());
            return ValueTask.CompletedTask;
        });

        Assert.Empty(frames);
    }

    /// <summary>
    /// Verifies that <see cref="DcdPipeReader.ParseAsync"/> rejects a stream with a mismatched trailing block marker.
    /// </summary>
    [Fact]
    public async Task ParseAsync_MismatchedTrailingBlockMarker_RejectsFrame()
    {
        // WHAT: Modifies trailing Fortran block marker so it does not match leading marker.
        // WHY:  Ensures Fortran unformatted framing integrity is enforced.
        // HOW:  Corrupts trailing marker of the X block and verifies frame is discarded.
        byte[] dcdBytes = CreateSyntheticDcdBytes(
            atomCount: 2,
            xCoords: [1f, 2f],
            yCoords: [3f, 4f],
            zCoords: [5f, 6f],
            hasUnitCell: false);

        int trailingMarkerOffset = dcdBytes.Length - (3 * (4 + 8 + 4)) + 4 + 8;
        dcdBytes[trailingMarkerOffset] = 0xAA;

        using MemoryStream ms = new(dcdBytes);
        PipeReader reader = PipeReader.Create(ms);

        DcdPipeReader parser = new();
        List<AtomCoordinate[]> frames = [];

        await parser.ParseAsync(reader, (_, coords) =>
        {
            frames.Add(coords.ToArray());
            return ValueTask.CompletedTask;
        });

        Assert.Empty(frames);
    }

    /// <summary>
    /// Verifies that <see cref="DcdPipeReader.ParseAsync"/> handles truncated coordinate block and corrupt title marker.
    /// </summary>
    [Fact]
    public async Task ParseAsync_TruncatedBlockAndCorruptTitle_HandlesGracefully()
    {
        // WHAT: Tests truncated coordinate block (leading marker declares 8 bytes, but only 2 bytes exist)
        //       and corrupted trailing title marker.
        // WHY:  Ensures full branch coverage in ReadCoordinateBlock and TryParseHeader.
        // HOW:  Constructs truncated and corrupted streams and asserts zero frames emitted.
        byte[] dcdBytes = CreateSyntheticDcdBytes(
            atomCount: 2,
            xCoords: [1f, 2f],
            yCoords: [3f, 4f],
            zCoords: [5f, 6f],
            hasUnitCell: false);

        // Truncate stream midway through the first coordinate block (cuts off inside loop)
        byte[] truncatedBytes = dcdBytes[..(dcdBytes.Length - 6)];
        using MemoryStream msTrunc = new(truncatedBytes);
        PipeReader readerTrunc = PipeReader.Create(msTrunc);
        DcdPipeReader parser = new();
        List<AtomCoordinate[]> frames = [];
        await parser.ParseAsync(readerTrunc, (_, coords) =>
        {
            frames.Add(coords.ToArray());
            return ValueTask.CompletedTask;
        });
        Assert.Empty(frames);

        // Corrupt trailing title marker (offset: 84 bytes header payload + 4 bytes leading marker + 4 bytes title len + 4 bytes title payload = offset 96)
        byte[] corruptTitleBytes = (byte[])dcdBytes.Clone();
        corruptTitleBytes[96] = 0x7F; // Mismatch trailing title marker
        using MemoryStream msTitle = new(corruptTitleBytes);
        PipeReader readerTitle = PipeReader.Create(msTitle);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            parser.ParseAsync(readerTitle, (_, coords) =>
            {
                frames.Add(coords.ToArray());
                return ValueTask.CompletedTask;
            }));
    }

    /// <summary>
    /// Verifies that <see cref="DcdPipeReader.ParseAsync"/> aborts parsing when the trailing coordinate block marker mismatches.
    /// </summary>
    [Fact]
    public async Task ParseAsync_CorruptTrailingBlockMarker_AbortsParsing()
    {
        // WHAT: Corrupts the trailing Fortran record marker for coordinate blocks.
        // WHY:  Ensures data integrity and covers the trailing block marker validation branch.
        // HOW:  Flips bits in the trailing coordinate record marker and asserts zero frames emitted.
        byte[] bytes = CreateSyntheticDcdBytes(2, [1f, 2f], [3f, 4f], [5f, 6f], hasUnitCell: false);
        // Header: 84 + 8 = 92
        // Title: 4 + 4 + 4 = 12 -> total 104
        // NATOM: 4 + 4 + 4 = 12 -> total 116
        // Coordinate block X: leading marker (4) + 2 floats (8) = 12 bytes -> trailing marker at 116 + 12 = 128
        bytes[128] ^= 0xFF;

        using MemoryStream ms = new(bytes);
        PipeReader reader = PipeReader.Create(ms);
        DcdPipeReader parser = new();
        List<AtomCoordinate[]> frames = [];

        await parser.ParseAsync(reader, (_, coords) =>
        {
            frames.Add(coords.ToArray());
            return ValueTask.CompletedTask;
        });

        Assert.Empty(frames);
    }

    /// <summary>
    /// Helper constructing synthetic binary DCD bytes.
    /// </summary>
    /// <param name="atomCount">Number of atoms.</param>
    /// <param name="xCoords">X coordinate array.</param>
    /// <param name="yCoords">Y coordinate array.</param>
    /// <param name="zCoords">Z coordinate array.</param>
    /// <param name="hasUnitCell">Whether to include unit cell data.</param>
    /// <returns>Synthesized binary DCD stream bytes.</returns>
    private static byte[] CreateSyntheticDcdBytes(
        int atomCount,
        float[] xCoords,
        float[] yCoords,
        float[] zCoords,
        bool hasUnitCell)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        // 1. Header Record (84 bytes payload)
        writer.Write((int)84);                   // Leading Fortran record marker
        writer.Write("CORD"u8.ToArray());        // Signature
        byte[] dummyHeaderInts = new byte[76];
        if (hasUnitCell)
        {
            dummyHeaderInts[36] = 1; // unitCellFlag = 1 at offset 40 (36 in 76-byte slice)
        }
        writer.Write(dummyHeaderInts);
        writer.Write((int)84);                   // Trailing record marker

        // 2. Title Record
        writer.Write((int)4);                    // Title length
        writer.Write("TEST"u8.ToArray());        // Title content
        writer.Write((int)4);                    // Trailing title marker

        // 3. NATOM Record
        writer.Write((int)4);                    // Leading marker
        writer.Write(atomCount);                 // NATOM
        writer.Write((int)4);                    // Trailing marker

        // 4. Unit Cell Block (56 bytes: leading marker 48 + 6 doubles * 8 + trailing marker 48)
        if (hasUnitCell)
        {
            writer.Write((int)48);
            for (int i = 0; i < 6; i++) writer.Write((double)50.0);
            writer.Write((int)48);
        }

        // 5. Coordinates: X, Y, Z
        int blockSize = atomCount * 4;
        writer.Write(blockSize);
        for (int i = 0; i < atomCount; i++) writer.Write(xCoords[i]);
        writer.Write(blockSize);

        writer.Write(blockSize);
        for (int i = 0; i < atomCount; i++) writer.Write(yCoords[i]);
        writer.Write(blockSize);

        writer.Write(blockSize);
        for (int i = 0; i < atomCount; i++) writer.Write(zCoords[i]);
        writer.Write(blockSize);

        return ms.ToArray();
    }
}
