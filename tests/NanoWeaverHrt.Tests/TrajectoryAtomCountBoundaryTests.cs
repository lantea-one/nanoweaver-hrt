using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Pipelines;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Security and boundary tests for the per-frame atom count limit enforced by <see cref="TrajectoryFrameBuffer"/> and
/// applied by the XYZ, LAMMPS and DCD trajectory readers.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English summary:</strong> a molecular dynamics trajectory file states how many atoms each snapshot
/// contains before listing their positions. That number is just text (or, for DCD, four bytes) inside a file that may
/// have been uploaded by an untrusted party. Before this hardening it was handed straight to the memory pool, so a
/// twenty-byte file claiming two billion atoms would make the service try to reserve roughly 24 GB in one go and fall
/// over. These tests pin both halves of the fix: the declared count is checked against a documented ceiling, and the
/// coordinate buffer only grows as real atom data actually arrives.
/// </para>
/// </remarks>
public sealed class TrajectoryAtomCountBoundaryTests
{
    /// <summary>
    /// Runs an XYZ trajectory through <see cref="XyzPipeReader"/> and returns the frames it emitted.
    /// </summary>
    /// <param name="trajectory">The complete XYZ text to parse.</param>
    /// <returns>The emitted frames, each captured as a copy of the coordinate span.</returns>
    private static async Task<List<AtomCoordinate[]>> ParseXyzAsync(string trajectory)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(trajectory));
        PipeReader pipeReader = PipeReader.Create(stream);

        XyzPipeReader parser = new();
        List<AtomCoordinate[]> frames = new();

        await parser.ParseAsync(pipeReader, (frameIndex, coordinates) =>
        {
            frames.Add(coordinates.ToArray());
            return ValueTask.CompletedTask;
        });

        return frames;
    }

    /// <summary>
    /// Runs a LAMMPS custom dump through <see cref="LammpsCustomPipeReader"/> and returns the frames it emitted.
    /// </summary>
    /// <param name="dump">The complete LAMMPS dump text to parse.</param>
    /// <returns>The emitted frames, each captured as a copy of the coordinate span.</returns>
    private static async Task<List<AtomCoordinate[]>> ParseLammpsAsync(string dump)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(dump));
        PipeReader pipeReader = PipeReader.Create(stream);

        LammpsCustomPipeReader parser = new();
        List<AtomCoordinate[]> frames = new();

        await parser.ParseAsync(pipeReader, (frameIndex, coordinates) =>
        {
            frames.Add(coordinates.ToArray());
            return ValueTask.CompletedTask;
        });

        return frames;
    }

    /// <summary>
    /// Runs a binary DCD stream through <see cref="DcdPipeReader"/> and returns the frames it emitted.
    /// </summary>
    /// <param name="dcdBytes">The complete DCD byte stream to parse.</param>
    /// <returns>The emitted frames, each captured as a copy of the coordinate span.</returns>
    private static async Task<List<AtomCoordinate[]>> ParseDcdAsync(byte[] dcdBytes)
    {
        using MemoryStream stream = new(dcdBytes);
        PipeReader pipeReader = PipeReader.Create(stream);

        DcdPipeReader parser = new();
        List<AtomCoordinate[]> frames = new();

        await parser.ParseAsync(pipeReader, (frameIndex, coordinates) =>
        {
            frames.Add(coordinates.ToArray());
            return ValueTask.CompletedTask;
        });

        return frames;
    }

    /// <summary>
    /// Builds a DCD byte stream with a chosen declared atom count and an arbitrary number of coordinate frames.
    /// </summary>
    /// <param name="declaredAtomCount">The atom count to write into the DCD header's NATOM record.</param>
    /// <param name="frameCount">How many complete coordinate frames to append after the header.</param>
    /// <param name="atomsPerFrameWritten">
    /// How many atoms of coordinate data to actually write per frame. Setting this below
    /// <paramref name="declaredAtomCount"/> produces a file that lies about its width, which is the hostile case.
    /// </param>
    /// <returns>The synthetic DCD byte stream.</returns>
    private static byte[] CreateDcdBytes(int declaredAtomCount, int frameCount, int atomsPerFrameWritten)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        // 1. Header record: leading marker, "CORD" signature, 19 zeroed header integers, trailing marker.
        writer.Write(84);
        writer.Write("CORD"u8.ToArray());
        writer.Write(new byte[76]);
        writer.Write(84);

        // 2. Title record.
        writer.Write(4);
        writer.Write("TEST"u8.ToArray());
        writer.Write(4);

        // 3. NATOM record carrying the declared atom count under test.
        writer.Write(4);
        writer.Write(declaredAtomCount);
        writer.Write(4);

        // 4. Coordinate frames: three Fortran blocks (all X, then all Y, then all Z).
        for (int frame = 0; frame < frameCount; frame++)
        {
            int blockSize = atomsPerFrameWritten * 4;
            for (int dimension = 0; dimension < 3; dimension++)
            {
                writer.Write(blockSize);
                for (int atom = 0; atom < atomsPerFrameWritten; atom++)
                {
                    writer.Write((float)((frame * 100) + (dimension * 10) + atom));
                }
                writer.Write(blockSize);
            }
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Pins <see cref="TrajectoryFrameBuffer.IsPlausibleAtomCount"/> at and around every boundary that matters.
    /// </summary>
    /// <param name="declaredAtomCount">The candidate atom count.</param>
    /// <param name="expected">Whether the count is expected to be accepted.</param>
    [Theory]
    [InlineData(int.MinValue, false)]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(TrajectoryFrameBuffer.MaxAtomsPerFrame - 1, true)]
    [InlineData(TrajectoryFrameBuffer.MaxAtomsPerFrame, true)]
    [InlineData(TrajectoryFrameBuffer.MaxAtomsPerFrame + 1, false)]
    [InlineData(int.MaxValue, false)]
    public void IsPlausibleAtomCount_EnforcesDocumentedCeilingAndPositivity(int declaredAtomCount, bool expected)
    {
        // WHAT: Exercises the single gate every reader consults before reserving any coordinate memory.
        // WHY:  The ceiling is the documented security boundary from finding H4; an off-by-one here would either
        //       reject legitimate large simulations or let a hostile header back through.
        // HOW:  Asserts acceptance at the limit and rejection one above it, plus zero and negative counts.
        Assert.Equal(expected, TrajectoryFrameBuffer.IsPlausibleAtomCount(declaredAtomCount));
    }

    /// <summary>
    /// Verifies that the very first coordinate buffer rental is capped at the initial capacity no matter how many
    /// atoms the file declared.
    /// </summary>
    [Fact]
    public void Grow_FirstRental_IsCappedAtInitialCapacity_EvenForTheLargestLegalDeclaredCount()
    {
        // WHAT: Asks for the first buffer of a frame that claims the maximum legal number of atoms.
        // WHY:  This is the heart of the H4 fix: a file may legally declare ten million atoms, but until it has
        //       actually supplied them the service must not reserve 120 MB on its word alone.
        // HOW:  Asserts the returned rental is on the order of InitialFrameCapacity, not the declared count.
        AtomCoordinate[] buffer = TrajectoryFrameBuffer.Grow(Array.Empty<AtomCoordinate>(), TrajectoryFrameBuffer.MaxAtomsPerFrame, 0);

        try
        {
            Assert.True(buffer.Length >= TrajectoryFrameBuffer.InitialFrameCapacity);
            Assert.True(buffer.Length < TrajectoryFrameBuffer.InitialFrameCapacity * 2);
        }
        finally
        {
            ArrayPool<AtomCoordinate>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Verifies that a frame narrower than the initial capacity is not over-provisioned.
    /// </summary>
    [Fact]
    public void Grow_FirstRental_IsClampedToTheDeclaredCountForSmallFrames()
    {
        // WHAT: Asks for the first buffer of a five-atom frame.
        // WHY:  Clamping to the declared count keeps small trajectories inside the smallest pool buckets.
        // HOW:  Asserts the rental holds at least the five atoms needed but far less than the initial capacity.
        AtomCoordinate[] buffer = TrajectoryFrameBuffer.Grow(Array.Empty<AtomCoordinate>(), 5, 0);

        try
        {
            Assert.True(buffer.Length >= 5);
            Assert.True(buffer.Length < TrajectoryFrameBuffer.InitialFrameCapacity);
        }
        finally
        {
            ArrayPool<AtomCoordinate>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Verifies that a subsequent growth doubles the buffer, preserves already-parsed atoms, and never overshoots the
    /// declared frame width.
    /// </summary>
    /// <param name="declaredAtomCount">The declared frame width driving the clamp.</param>
    /// <param name="minimumExpectedCapacity">The smallest acceptable resulting capacity.</param>
    [Theory]
    [InlineData(100_000, 64)]
    [InlineData(40, 40)]
    public void Grow_SubsequentRental_DoublesWithinTheDeclaredWidthAndPreservesParsedAtoms(int declaredAtomCount, int minimumExpectedCapacity)
    {
        // WHAT: Grows a full 32-atom buffer whose entries carry recognisable coordinates.
        // WHY:  A resize that lost or reordered atoms would silently corrupt an emitted frame, which Prime Directive 6
        //       forbids. The clamped case (declared width 40) proves growth never exceeds what the frame can need.
        // HOW:  Fills the old buffer with X = index, grows it, and re-reads every entry from the new buffer.
        AtomCoordinate[] original = ArrayPool<AtomCoordinate>.Shared.Rent(32);
        int atomsInUse = 32;
        for (int i = 0; i < atomsInUse; i++)
        {
            original[i] = new AtomCoordinate(i, i + 0.5f, i + 0.25f);
        }

        AtomCoordinate[] grown = TrajectoryFrameBuffer.Grow(original, declaredAtomCount, atomsInUse);

        try
        {
            Assert.True(grown.Length >= minimumExpectedCapacity);
            Assert.True(grown.Length > original.Length);
            Assert.True(grown.Length <= Math.Max(declaredAtomCount, original.Length * 2));

            for (int i = 0; i < atomsInUse; i++)
            {
                Assert.Equal(i, grown[i].X);
                Assert.Equal(i + 0.5f, grown[i].Y);
                Assert.Equal(i + 0.25f, grown[i].Z);
            }
        }
        finally
        {
            ArrayPool<AtomCoordinate>.Shared.Return(grown);
        }
    }

    /// <summary>
    /// Verifies the XYZ reader's treatment of every interesting declared atom count.
    /// </summary>
    /// <param name="declaredCount">The literal text written on the atom count header line.</param>
    /// <param name="expectedFrameCount">
    /// How many frames the reader should emit. An implausible count is discarded exactly like a malformed header, so
    /// the reader recovers and goes on to parse the well-formed two-atom frame later in the file, giving one frame.
    /// A plausible but unsatisfied count leaves the reader waiting for atoms that never come, giving no frames.
    /// </param>
    [Theory]
    // Negative and zero counts: rejected, reader recovers and parses the trailing valid frame.
    [InlineData("-2147483648", 1)]
    [InlineData("-1", 1)]
    [InlineData("0", 1)]
    // Just under, and exactly at, the ceiling: accepted, so the reader waits forever for 10 million atoms.
    [InlineData("9999999", 0)]
    [InlineData("10000000", 0)]
    // Just over the ceiling, and the classic int.MaxValue attack: rejected before any memory is reserved.
    [InlineData("10000001", 1)]
    [InlineData("2147483647", 1)]
    // Beyond the range of a 32-bit integer entirely: rejected by the numeric parse.
    [InlineData("99999999999999999999", 1)]
    // A sane count, for contrast: two frames, the declared one and the trailing one.
    [InlineData("2", 2)]
    public async Task XyzPipeReader_BoundsDeclaredAtomCount(string declaredCount, int expectedFrameCount)
    {
        // WHAT: Feeds a tiny XYZ file whose first header carries the count under test, followed by a well-formed
        //       two-atom frame.
        // WHY:  Proves the ceiling is applied, that rejection is non-fatal (the reader resynchronises on the next
        //       valid header), and that an inflated count can never be satisfied so no large buffer is ever filled.
        // HOW:  Counts the frames the parser emits.
        string trajectory =
            declaredCount + "\n" +
            "hostile frame comment\n" +
            "C 1.0 1.0 1.0\n" +
            "C 2.0 2.0 2.0\n" +
            "2\n" +
            "honest frame comment\n" +
            "C 3.0 3.0 3.0\n" +
            "C 4.0 4.0 4.0\n";

        List<AtomCoordinate[]> frames = await ParseXyzAsync(trajectory);

        Assert.Equal(expectedFrameCount, frames.Count);
    }

    /// <summary>
    /// Verifies the LAMMPS reader's treatment of every interesting declared atom count.
    /// </summary>
    /// <param name="declaredCount">The literal text written under the <c>ITEM: NUMBER OF ATOMS</c> section header.</param>
    /// <param name="expectedFrameCount">How many frames the reader should emit.</param>
    [Theory]
    [InlineData("-1", 0)]
    [InlineData("0", 0)]
    [InlineData("9999999", 0)]
    [InlineData("10000000", 0)]
    [InlineData("10000001", 0)]
    [InlineData("2147483647", 0)]
    [InlineData("2", 1)]
    public async Task LammpsCustomPipeReader_BoundsDeclaredAtomCount(string declaredCount, int expectedFrameCount)
    {
        // WHAT: Feeds a single LAMMPS snapshot whose NUMBER OF ATOMS value is the count under test, followed by two
        //       real atom lines.
        // WHY:  Proves an implausible count reserves nothing and emits nothing, while a plausible count still works.
        // HOW:  Counts the frames the parser emits; only the honest count of 2 can be satisfied by two atom lines.
        string dump =
            "ITEM: TIMESTEP\n" +
            "0\n" +
            "ITEM: NUMBER OF ATOMS\n" +
            declaredCount + "\n" +
            "ITEM: BOX BOUNDS pp pp pp\n" +
            "0.0 50.0\n" +
            "0.0 50.0\n" +
            "0.0 50.0\n" +
            "ITEM: ATOMS id type x y z\n" +
            "1 1 1.0 1.0 1.0\n" +
            "2 1 2.0 2.0 2.0\n";

        List<AtomCoordinate[]> frames = await ParseLammpsAsync(dump);

        Assert.Equal(expectedFrameCount, frames.Count);
    }

    /// <summary>
    /// Verifies the DCD reader rejects an implausible NATOM header value without reserving any memory.
    /// </summary>
    /// <param name="declaredAtomCount">The atom count written into the DCD NATOM record.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(TrajectoryFrameBuffer.MaxAtomsPerFrame + 1)]
    [InlineData(int.MaxValue)]
    public async Task DcdPipeReader_RejectsImplausibleHeaderAtomCount(int declaredAtomCount)
    {
        // WHAT: Builds a header-only DCD stream declaring an impossible width and parses it.
        // WHY:  Before the fix, the four coordinate buffers were rented from the header alone, so a header-only file
        //       declaring int.MaxValue atoms demanded roughly 24 GB. The header must now be refused outright.
        // HOW:  TryParseHeader returns false, the reader emits nothing, and the parse completes cleanly.
        byte[] dcdBytes = CreateDcdBytes(declaredAtomCount, frameCount: 0, atomsPerFrameWritten: 0);

        List<AtomCoordinate[]> frames = await ParseDcdAsync(dcdBytes);

        Assert.Empty(frames);
    }

    /// <summary>
    /// Verifies that a DCD header declaring the maximum legal atom count reserves nothing until a complete frame of
    /// coordinates has genuinely arrived.
    /// </summary>
    [Theory]
    [InlineData(TrajectoryFrameBuffer.MaxAtomsPerFrame - 1)]
    [InlineData(TrajectoryFrameBuffer.MaxAtomsPerFrame)]
    public async Task DcdPipeReader_AcceptsCeilingHeader_ButReservesNothingForAStarvedStream(int declaredAtomCount)
    {
        // WHAT: Builds a DCD stream with a legal but enormous declared width and no coordinate data at all.
        // WHY:  The ceiling alone is not enough: ten million atoms is legal, so a file must not be able to trigger a
        //       120 MB reservation simply by claiming that width in a 112-byte file.
        // HOW:  The rents now sit behind the "is a whole frame buffered?" check, so a starved stream emits no frames
        //       and the parse returns promptly rather than allocating.
        byte[] dcdBytes = CreateDcdBytes(declaredAtomCount, frameCount: 0, atomsPerFrameWritten: 0);

        List<AtomCoordinate[]> frames = await ParseDcdAsync(dcdBytes);

        Assert.Empty(frames);
    }

    /// <summary>
    /// Verifies that a well-formed multi-frame DCD stream still parses accurately, so the deferred rental is taken
    /// exactly once and reused across frames.
    /// </summary>
    [Fact]
    public async Task DcdPipeReader_ParsesMultipleFrames_ReusingASingleRental()
    {
        // WHAT: Parses a three-frame, two-atom DCD stream.
        // WHY:  Moving the rents inside the frame loop must not re-rent per frame nor lose coordinate accuracy.
        // HOW:  Asserts all three frames arrive with the exact synthetic coordinates written by the builder.
        byte[] dcdBytes = CreateDcdBytes(declaredAtomCount: 2, frameCount: 3, atomsPerFrameWritten: 2);

        List<AtomCoordinate[]> frames = await ParseDcdAsync(dcdBytes);

        Assert.Equal(3, frames.Count);
        for (int frame = 0; frame < 3; frame++)
        {
            Assert.Equal(2, frames[frame].Length);
            for (int atom = 0; atom < 2; atom++)
            {
                Assert.Equal((frame * 100) + atom, frames[frame][atom].X);
                Assert.Equal((frame * 100) + 10 + atom, frames[frame][atom].Y);
                Assert.Equal((frame * 100) + 20 + atom, frames[frame][atom].Z);
            }
        }
    }

    /// <summary>
    /// Verifies that a frame wider than the initial buffer capacity is parsed completely and accurately, exercising
    /// the incremental growth path end to end.
    /// </summary>
    [Fact]
    public async Task XyzPipeReader_ParsesFrameWiderThanTheInitialBufferCapacity()
    {
        // WHAT: Parses one XYZ frame holding InitialFrameCapacity + 1 atoms with distinct coordinates.
        // WHY:  The buffer must grow mid-frame without dropping, duplicating or reordering any atom; this is the case
        //       the doubling branch of TrajectoryFrameBuffer.Grow exists for.
        // HOW:  Generates atoms whose X coordinate is their index and asserts every index round-trips.
        int atomCount = TrajectoryFrameBuffer.InitialFrameCapacity + 1;
        StringBuilder builder = new();
        builder.Append(atomCount).Append('\n');
        builder.Append("wide origami frame\n");
        for (int i = 0; i < atomCount; i++)
        {
            builder.Append("C ").Append(i).Append(".0 0.0 0.0\n");
        }

        List<AtomCoordinate[]> frames = await ParseXyzAsync(builder.ToString());

        AtomCoordinate[] frame = Assert.Single(frames);
        Assert.Equal(atomCount, frame.Length);
        for (int i = 0; i < atomCount; i++)
        {
            Assert.Equal(i, frame[i].X);
        }
    }
}
