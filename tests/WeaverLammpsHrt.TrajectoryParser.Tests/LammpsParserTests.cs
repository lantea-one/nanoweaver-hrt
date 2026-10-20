using System.IO.Pipelines;
using System.Text;
using WeaverLammpsHrt.TrajectoryParser.Models;
using WeaverLammpsHrt.TrajectoryParser.Pipelines;
using Xunit;

namespace WeaverLammpsHrt.TrajectoryParser.Tests;

/// <summary>
/// Unit test suite verifying the zero-allocation LAMMPS custom dump pipeline reader.
/// </summary>
public sealed class LammpsParserTests
{
    /// <summary>
    /// Sample multi-frame LAMMPS text dump used for parser validation.
    /// </summary>
    private const string SampleLammpsDump =
@"ITEM: TIMESTEP
0
ITEM: NUMBER OF ATOMS
3
ITEM: BOX BOUNDS pp pp pp
0.0000000000000000e+00 5.0000000000000000e+01
0.0000000000000000e+00 5.0000000000000000e+01
0.0000000000000000e+00 5.0000000000000000e+01
ITEM: ATOMS id type x y z
1 1 10.000 20.000 30.000
2 1 15.000 25.000 35.000
3 2 18.000 28.000 38.000
ITEM: TIMESTEP
1000
ITEM: NUMBER OF ATOMS
3
ITEM: BOX BOUNDS pp pp pp
0.0000000000000000e+00 5.0000000000000000e+01
0.0000000000000000e+00 5.0000000000000000e+01
0.0000000000000000e+00 5.0000000000000000e+01
ITEM: ATOMS id type x y z
1 1 10.500 20.500 30.500
2 1 15.500 25.500 35.500
3 2 18.500 28.500 38.500
";

    /// <summary>
    /// Verifies that <see cref="LammpsCustomPipeReader.ParseAsync"/> parses multiple frames,
    /// skips metadata, and extracts accurate Cartesian coordinates.
    /// </summary>
    [Fact]
    public async Task ParseAsync_ShouldExtractCoordinatesFromLammpsDump()
    {
        // WHAT: Tests streaming pipeline reader on simulated LAMMPS dump output.
        // WHY:  Verifies section state transitions (TIMESTEP, NUMBER OF ATOMS, BOX BOUNDS, ATOMS).
        // HOW:  Feeds byte memory stream into PipeReader and asserts extracted coordinates.
        LammpsCustomPipeReader parser = new();
        byte[] bytes = Encoding.UTF8.GetBytes(SampleLammpsDump);
        using MemoryStream stream = new(bytes);
        PipeReader reader = PipeReader.Create(stream);

        List<AtomCoordinate[]> parsedFrames = [];

        await parser.ParseAsync(reader, (frameIndex, coords) =>
        {
            parsedFrames.Add(coords.ToArray());
            return ValueTask.CompletedTask;
        });

        Assert.Equal(2, parsedFrames.Count);

        // Frame 0 Assertions
        Assert.Equal(3, parsedFrames[0].Length);
        Assert.Equal(10.0f, parsedFrames[0][0].X, precision: 2);
        Assert.Equal(20.0f, parsedFrames[0][0].Y, precision: 2);
        Assert.Equal(30.0f, parsedFrames[0][0].Z, precision: 2);

        // Frame 1 Assertions
        Assert.Equal(3, parsedFrames[1].Length);
        Assert.Equal(10.5f, parsedFrames[1][0].X, precision: 2);
        Assert.Equal(20.5f, parsedFrames[1][0].Y, precision: 2);
        Assert.Equal(30.5f, parsedFrames[1][0].Z, precision: 2);
    }
}
