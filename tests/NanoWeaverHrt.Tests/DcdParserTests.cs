using System.Buffers.Binary;
using System.IO.Pipelines;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Pipelines;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Unit tests verifying binary stream decoding and coordinate accuracy for the <see cref="DcdPipeReader"/>.
/// </summary>
public sealed class DcdParserTests
{
    /// <summary>
    /// Verifies that a valid binary DCD stream containing one frame of two atoms is parsed accurately.
    /// </summary>
    [Fact]
    public async Task ParseAsync_ShouldParseBinaryFrameCoordinatesAccurately()
    {
        // WHAT: Synthesizes a valid in-memory binary DCD stream with 2 atoms.
        // WHY:  Permits unit testing of binary Fortran record parsing without external file dependencies.
        // HOW:  Encodes standard DCD header ("CORD", atom count 2) and coordinate blocks for X, Y, and Z.
        byte[] dcdBytes = CreateSyntheticDcdBytes(
            atomCount: 2,
            xCoords: new float[] { 1.5f, 4.5f },
            yCoords: new float[] { 2.5f, 5.5f },
            zCoords: new float[] { 3.5f, 6.5f }
        );

        using MemoryStream stream = new(dcdBytes);
        PipeReader reader = PipeReader.Create(stream);

        DcdPipeReader parser = new();
        List<(long frameIndex, AtomCoordinate[] coordinates)> collectedFrames = new();

        await parser.ParseAsync(reader, (frameIndex, coords) =>
        {
            collectedFrames.Add((frameIndex, coords.ToArray()));
            return ValueTask.CompletedTask;
        });

        // Assert 1 frame parsed
        Assert.Single(collectedFrames);
        Assert.Equal(0, collectedFrames[0].frameIndex);
        Assert.Equal(2, collectedFrames[0].coordinates.Length);

        // Atom 0: (1.5, 2.5, 3.5)
        Assert.Equal(1.5f, collectedFrames[0].coordinates[0].X);
        Assert.Equal(2.5f, collectedFrames[0].coordinates[0].Y);
        Assert.Equal(3.5f, collectedFrames[0].coordinates[0].Z);

        // Atom 1: (4.5, 5.5, 6.5)
        Assert.Equal(4.5f, collectedFrames[0].coordinates[1].X);
        Assert.Equal(5.5f, collectedFrames[0].coordinates[1].Y);
        Assert.Equal(6.5f, collectedFrames[0].coordinates[1].Z);
    }

    /// <summary>
    /// Generates a minimal binary DCD stream according to the CHARMM/LAMMPS specification.
    /// </summary>
    /// <param name="atomCount">Number of atoms in each frame.</param>
    /// <param name="xCoords">Array of X coordinates.</param>
    /// <param name="yCoords">Array of Y coordinates.</param>
    /// <param name="zCoords">Array of Z coordinates.</param>
    /// <returns>Raw byte array representing the binary DCD trajectory.</returns>
    private static byte[] CreateSyntheticDcdBytes(int atomCount, float[] xCoords, float[] yCoords, float[] zCoords)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        // 1. Header Record (84 bytes payload)
        writer.Write((int)84);                   // Leading Fortran record marker
        writer.Write("CORD"u8.ToArray());        // Signature
        byte[] dummyHeaderInts = new byte[76];   // 19 ints (all zero, unit cell flag = 0)
        writer.Write(dummyHeaderInts);
        writer.Write((int)84);                   // Trailing record marker

        // 2. Title Record
        writer.Write((int)4);                    // Title length
        writer.Write("TEST"u8.ToArray());        // Title content
        writer.Write((int)4);                    // Trailing title marker

        // 3. Number of Atoms Record
        writer.Write((int)4);                    // Leading marker
        writer.Write(atomCount);                 // NATOM
        writer.Write((int)4);                    // Trailing marker

        // 4. Coordinate Frame: X block
        int coordBlockSize = atomCount * 4;
        writer.Write(coordBlockSize);
        for (int i = 0; i < atomCount; i++) writer.Write(xCoords[i]);
        writer.Write(coordBlockSize);

        // Coordinate Frame: Y block
        writer.Write(coordBlockSize);
        for (int i = 0; i < atomCount; i++) writer.Write(yCoords[i]);
        writer.Write(coordBlockSize);

        // Coordinate Frame: Z block
        writer.Write(coordBlockSize);
        for (int i = 0; i < atomCount; i++) writer.Write(zCoords[i]);
        writer.Write(coordBlockSize);

        return ms.ToArray();
    }
}
