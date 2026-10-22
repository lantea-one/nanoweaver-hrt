using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Pipelines;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Accuracy tests for trajectory lines wider than the readers' initial line buffer, covering audit finding L5:
/// a long coordinate line must never be silently truncated into a different, wrong coordinate.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English summary.</strong> Each line of a text trajectory file names an atom and gives its three
/// coordinates. The readers copy one line at a time into a small reusable buffer before reading the numbers out of
/// it. That buffer used to be a fixed 512 bytes and the copy was clamped to its size, so a longer line was quietly
/// cut short and whatever was left got parsed as though it were the whole line. If the cut fell inside the last
/// number, <c>3.75</c> silently became <c>3.7</c> — a wrong atomic position, reported as valid.
/// </para>
/// <para>
/// Long lines are not exotic. A LAMMPS dump row carrying velocities, forces and charge alongside the position runs
/// well past 512 bytes, and so does any file written with wide fixed-width columns. Every test below therefore
/// asserts either an exactly correct parse or a clean refusal — never a wrong number.
/// </para>
/// </remarks>
public sealed class TrajectoryLineLengthBoundaryTests
{
    /// <summary>
    /// The X coordinate every synthetic wide line encodes.
    /// </summary>
    private const float ExpectedX = 1.5f;

    /// <summary>
    /// The Y coordinate every synthetic wide line encodes.
    /// </summary>
    private const float ExpectedY = 2.5f;

    /// <summary>
    /// The Z coordinate every synthetic wide line encodes.
    /// </summary>
    /// <remarks>
    /// Chosen so that losing the final byte changes the value: a line truncated by one character parses this as
    /// <c>3.7</c>, which is why the assertions below would have failed before the fix rather than passing by luck.
    /// </remarks>
    private const float ExpectedZ = 3.75f;

    /// <summary>
    /// The coordinate columns appended to the end of every synthetic wide line.
    /// </summary>
    private const string CoordinateColumns = "1.5 2.5 3.75";

    /// <summary>
    /// Builds an XYZ coordinate line of an exact byte length by padding between the atom symbol and the coordinates.
    /// </summary>
    /// <param name="totalLineBytes">The exact length the line must have, excluding its newline delimiter.</param>
    /// <returns>A single XYZ coordinate line encoding the expected coordinates.</returns>
    private static string BuildWideXyzLine(int totalLineBytes)
    {
        // "C" + padding + "1.5 2.5 3.75" => 1 + padding + 12 bytes.
        int padding = totalLineBytes - 1 - CoordinateColumns.Length;
        return "C" + new string(' ', padding) + CoordinateColumns;
    }

    /// <summary>
    /// Builds a LAMMPS atom line of an exact byte length by padding between the id/type columns and the coordinates.
    /// </summary>
    /// <param name="totalLineBytes">The exact length the line must have, excluding its newline delimiter.</param>
    /// <returns>A single LAMMPS atom row encoding the expected coordinates.</returns>
    private static string BuildWideLammpsLine(int totalLineBytes)
    {
        // "1 1" + padding + "1.5 2.5 3.75" => 3 + padding + 12 bytes.
        int padding = totalLineBytes - 3 - CoordinateColumns.Length;
        return "1 1" + new string(' ', padding) + CoordinateColumns;
    }

    /// <summary>
    /// Wraps a LAMMPS atom row in the surrounding snapshot section headers.
    /// </summary>
    /// <param name="atomRows">The atom rows to place under the <c>ITEM: ATOMS</c> header.</param>
    /// <param name="declaredAtomCount">The value to write under <c>ITEM: NUMBER OF ATOMS</c>.</param>
    /// <returns>A complete LAMMPS snapshot.</returns>
    private static string BuildLammpsSnapshot(string atomRows, int declaredAtomCount)
    {
        return "ITEM: TIMESTEP\n0\n" +
               "ITEM: NUMBER OF ATOMS\n" + declaredAtomCount + "\n" +
               "ITEM: BOX BOUNDS pp pp pp\n0.0 50.0\n0.0 50.0\n0.0 50.0\n" +
               "ITEM: ATOMS id type x y z\n" + atomRows;
    }

    /// <summary>
    /// Runs XYZ text through <see cref="XyzPipeReader"/> and returns the frames it emitted.
    /// </summary>
    /// <param name="trajectory">The complete XYZ text to parse.</param>
    /// <returns>The emitted frames, each captured as a copy of the coordinate span.</returns>
    private static async Task<List<AtomCoordinate[]>> ParseXyzAsync(string trajectory)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(trajectory));
        XyzPipeReader parser = new();
        List<AtomCoordinate[]> frames = new();

        await parser.ParseAsync(PipeReader.Create(stream), (frameIndex, coordinates) =>
        {
            frames.Add(coordinates.ToArray());
            return ValueTask.CompletedTask;
        });

        return frames;
    }

    /// <summary>
    /// Runs LAMMPS dump text through <see cref="LammpsCustomPipeReader"/> and returns the frames it emitted.
    /// </summary>
    /// <param name="dump">The complete LAMMPS dump text to parse.</param>
    /// <returns>The emitted frames, each captured as a copy of the coordinate span.</returns>
    private static async Task<List<AtomCoordinate[]>> ParseLammpsAsync(string dump)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(dump));
        LammpsCustomPipeReader parser = new();
        List<AtomCoordinate[]> frames = new();

        await parser.ParseAsync(PipeReader.Create(stream), (frameIndex, coordinates) =>
        {
            frames.Add(coordinates.ToArray());
            return ValueTask.CompletedTask;
        });

        return frames;
    }

    /// <summary>
    /// Asserts a frame holds exactly one atom carrying the expected coordinates, to full float precision.
    /// </summary>
    /// <param name="frame">The emitted frame to inspect.</param>
    private static void AssertExactCoordinates(AtomCoordinate[] frame)
    {
        AtomCoordinate atom = Assert.Single(frame);
        Assert.Equal(ExpectedX, atom.X);
        Assert.Equal(ExpectedY, atom.Y);
        Assert.Equal(ExpectedZ, atom.Z);
    }

    /// <summary>
    /// Verifies that the XYZ reader parses a wide coordinate line exactly, at and either side of the former
    /// 512-byte truncation threshold and well beyond it.
    /// </summary>
    /// <param name="lineBytes">Exact length of the coordinate line under test, excluding its newline.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Theory]
    // Just under, exactly at, and just over the old fixed 512-byte buffer. 513 is the regression case: before the fix
    // the final '5' of "3.75" was dropped and Z parsed as 3.7.
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(513)]
    // Comfortably past the old threshold, including lengths that force several buffer growths.
    [InlineData(1024)]
    [InlineData(4096)]
    // Exactly the documented ceiling, which must still be accepted, and which spans many pipeline segments.
    [InlineData(TrajectoryLineBuffer.MaxLineBytes)]
    public async Task XyzPipeReader_ParsesWideLineExactly_AcrossTheLengthBoundary(int lineBytes)
    {
        // WHAT: Parses a one-atom XYZ frame whose coordinate line is exactly lineBytes long.
        // WHY:  Prime Directive 6 demands 100% deterministic accuracy, and these coordinates drive hormone dosing
        //       and fabrication decisions, so a quietly truncated number is the most dangerous outcome available.
        // HOW:  Pads between the atom symbol and the coordinates to hit the exact length, then asserts all three
        //       coordinates to full float precision.
        string trajectory = "1\nwide frame\n" + BuildWideXyzLine(lineBytes) + "\n";

        List<AtomCoordinate[]> frames = await ParseXyzAsync(trajectory);

        AssertExactCoordinates(Assert.Single(frames));
    }

    /// <summary>
    /// Verifies that the LAMMPS reader parses a wide atom row exactly, at and either side of the former 512-byte
    /// truncation threshold and well beyond it.
    /// </summary>
    /// <param name="lineBytes">Exact length of the atom row under test, excluding its newline.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Theory]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(513)]
    [InlineData(1024)]
    [InlineData(4096)]
    [InlineData(TrajectoryLineBuffer.MaxLineBytes)]
    public async Task LammpsCustomPipeReader_ParsesWideRowExactly_AcrossTheLengthBoundary(int lineBytes)
    {
        // WHAT: Parses a one-atom LAMMPS snapshot whose atom row is exactly lineBytes long.
        // WHY:  A dump row with velocities, forces and charge alongside the position routinely exceeds 512 bytes, so
        //       this is the common case rather than an edge case.
        // HOW:  Pads between the id/type columns and the coordinates, then asserts full float precision.
        string dump = BuildLammpsSnapshot(BuildWideLammpsLine(lineBytes) + "\n", declaredAtomCount: 1);

        List<AtomCoordinate[]> frames = await ParseLammpsAsync(dump);

        AssertExactCoordinates(Assert.Single(frames));
    }

    /// <summary>
    /// Verifies that an XYZ line beyond the documented ceiling is refused, that the frame it belonged to is discarded
    /// rather than left half-filled, and that the reader resynchronises on the next well-formed frame.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task XyzPipeReader_DiscardsFrameContainingAnOverLongLine_ThenResynchronises()
    {
        // WHAT: Parses a trajectory whose first frame declares two atoms, supplies one good atom and then an
        //       absurdly long line, followed by a second, entirely well-formed frame.
        // WHY:  Skipping only the offending line would leave the first frame one atom short, and the second frame's
        //       coordinate lines would quietly fill that gap - emitting a single frame stitched from two different
        //       timesteps and presenting it as complete. That is exactly the silently-wrong-number class of defect
        //       this work is closing, so the partial frame must be abandoned instead.
        // HOW:  Asserts exactly one frame is emitted, that it holds the second frame's coordinates, and that the
        //       discarded atom (X = 9.0) appears nowhere.
        string trajectory =
            "2\nfirst frame\n" +
            "C 9.0 9.0 9.0\n" +
            BuildWideXyzLine(TrajectoryLineBuffer.MaxLineBytes + 1) + "\n" +
            "1\nsecond frame\n" +
            BuildWideXyzLine(600) + "\n";

        List<AtomCoordinate[]> frames = await ParseXyzAsync(trajectory);

        AtomCoordinate[] frame = Assert.Single(frames);
        AssertExactCoordinates(frame);
        Assert.DoesNotContain(frame, atom => atom.X == 9.0f);
    }

    /// <summary>
    /// Verifies that a LAMMPS atom row beyond the documented ceiling is refused, that the snapshot it belonged to is
    /// discarded, and that the reader resynchronises on the next snapshot.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task LammpsCustomPipeReader_DiscardsSnapshotContainingAnOverLongRow_ThenResynchronises()
    {
        // WHAT: Parses a dump whose first snapshot declares two atoms, supplies one good row and then an absurdly
        //       long row, followed by a second, entirely well-formed snapshot.
        // WHY:  Same reasoning as the XYZ case: a gap left in a partially filled snapshot would be backfilled by the
        //       next snapshot's rows, blending two timesteps into one apparently complete frame.
        // HOW:  Asserts exactly one frame is emitted and that it carries only the second snapshot's coordinates.
        string dump =
            BuildLammpsSnapshot(
                "1 1 9.0 9.0 9.0\n" + BuildWideLammpsLine(TrajectoryLineBuffer.MaxLineBytes + 1) + "\n",
                declaredAtomCount: 2) +
            BuildLammpsSnapshot(BuildWideLammpsLine(600) + "\n", declaredAtomCount: 1);

        List<AtomCoordinate[]> frames = await ParseLammpsAsync(dump);

        AtomCoordinate[] frame = Assert.Single(frames);
        AssertExactCoordinates(frame);
        Assert.DoesNotContain(frame, atom => atom.X == 9.0f);
    }

    /// <summary>
    /// Verifies that a trajectory consisting solely of an over-long line yields no frames at all rather than a
    /// partial or fabricated one.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task XyzPipeReader_EmitsNothingWhenEveryCoordinateLineIsOverLong()
    {
        // WHAT: Parses a single-atom frame whose only coordinate line exceeds the ceiling.
        // WHY:  The honest outcome for an unparseable file is no data, never approximate data.
        // HOW:  Asserts no frame is emitted and the parse completes without throwing.
        string trajectory = "1\nframe\n" + BuildWideXyzLine(TrajectoryLineBuffer.MaxLineBytes + 1) + "\n";

        List<AtomCoordinate[]> frames = await ParseXyzAsync(trajectory);

        Assert.Empty(frames);
    }

    /// <summary>
    /// Pins the line buffer helper's behaviour directly at the ceiling boundary.
    /// </summary>
    /// <param name="lineBytes">Length of the synthetic line.</param>
    /// <param name="expectedAccepted">Whether the line is expected to be copied rather than refused.</param>
    [Theory]
    [InlineData(1, true)]
    [InlineData(TrajectoryLineBuffer.InitialLineCapacity, true)]
    [InlineData(TrajectoryLineBuffer.InitialLineCapacity + 1, true)]
    [InlineData(TrajectoryLineBuffer.MaxLineBytes, true)]
    [InlineData(TrajectoryLineBuffer.MaxLineBytes + 1, false)]
    public void TryCopyLine_CopiesWholeLineUpToTheCeilingAndRefusesBeyondIt(int lineBytes, bool expectedAccepted)
    {
        // WHAT: Calls the helper directly with lines straddling both the initial capacity and the hard ceiling.
        // WHY:  The all-or-nothing contract is the guarantee the readers depend on; a partial copy is the wrong
        //       answer this helper exists to make impossible.
        // HOW:  Asserts the reported length, that the buffer really holds every byte of the line, and that a refused
        //       line reports a length of zero.
        byte[] lineBytesArray = new byte[lineBytes];
        for (int i = 0; i < lineBytes; i++)
        {
            lineBytesArray[i] = (byte)('A' + (i % 26));
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(TrajectoryLineBuffer.InitialLineCapacity);

        try
        {
            bool accepted = TrajectoryLineBuffer.TryCopyLine(new ReadOnlySequence<byte>(lineBytesArray), ref buffer, out int copiedLength);

            Assert.Equal(expectedAccepted, accepted);

            if (!expectedAccepted)
            {
                Assert.Equal(0, copiedLength);
                return;
            }

            Assert.Equal(lineBytes, copiedLength);
            Assert.True(buffer.Length >= lineBytes);
            Assert.True(lineBytesArray.AsSpan().SequenceEqual(buffer.AsSpan(0, copiedLength)));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
