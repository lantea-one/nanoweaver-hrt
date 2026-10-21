using System.IO.Pipelines;
using System.Text;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Pipelines;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Unit tests verifying coordinate accuracy and frame parsing behavior of the <see cref="XyzPipeReader"/>.
/// </summary>
public sealed class XyzParserTests
{
    /// <summary>
    /// Sample 2-frame XYZ trajectory formatted with standard comment headers.
    /// </summary>
    private const string SampleXyzTrajectory =
        "2\n" +
        "Frame 0 Lattice=\"50.0 0.0 0.0 0.0 50.0 0.0 0.0 0.0 50.0\"\n" +
        "P 10.50 20.25 30.75\n" +
        "C 11.00 21.00 31.00\n" +
        "2\n" +
        "Frame 1\n" +
        "P 10.80 20.40 30.90\n" +
        "C 11.20 21.15 31.10\n";

    /// <summary>
    /// Verifies that the <see cref="XyzPipeReader"/> correctly extracts all coordinates across multiple frames.
    /// </summary>
    [Fact]
    public async Task ParseAsync_ShouldExtractAccurateCoordinates_AcrossFrames()
    {
        // WHAT: Streams a 2-frame XYZ test string into a System.IO.Pipelines PipeReader.
        // WHY:  Validates that zero-allocation line slicing correctly distinguishes frames and atom coordinates.
        // HOW:  Encodes the test string as UTF-8 bytes and feeds it directly into the pipeline reader.
        byte[] bytes = Encoding.UTF8.GetBytes(SampleXyzTrajectory);
        using MemoryStream stream = new(bytes);
        PipeReader pipeReader = PipeReader.Create(stream);

        XyzPipeReader parser = new();
        List<(long frameIndex, AtomCoordinate[] coordinates)> collectedFrames = new();

        await parser.ParseAsync(pipeReader, (frameIndex, coordinates) =>
        {
            // Copy coordinates to local array for assertion comparison
            collectedFrames.Add((frameIndex, coordinates.ToArray()));
            return ValueTask.CompletedTask;
        });

        // Assert 2 frames parsed
        Assert.Equal(2, collectedFrames.Count);

        // Frame 0 validation
        Assert.Equal(0, collectedFrames[0].frameIndex);
        Assert.Equal(2, collectedFrames[0].coordinates.Length);
        Assert.Equal(10.50f, collectedFrames[0].coordinates[0].X);
        Assert.Equal(20.25f, collectedFrames[0].coordinates[0].Y);
        Assert.Equal(30.75f, collectedFrames[0].coordinates[0].Z);

        // Frame 1 validation
        Assert.Equal(1, collectedFrames[1].frameIndex);
        Assert.Equal(2, collectedFrames[1].coordinates.Length);
        Assert.Equal(10.80f, collectedFrames[1].coordinates[0].X);
        Assert.Equal(20.40f, collectedFrames[1].coordinates[0].Y);
        Assert.Equal(30.90f, collectedFrames[1].coordinates[0].Z);
    }

    /// <summary>
    /// Verifies parsing of a 3-atom 2-frame trajectory.
    /// </summary>
    [Fact]
    public async Task ParseAsync_ThreeAtomsPerFrame_ShouldParseBothFrames()
    {
        string sample =
            "3\n" +
            "Frame 0 DNA origami aptamer\n" +
            "C 10.0 20.0 30.0\n" +
            "N 15.0 25.0 35.0\n" +
            "P 18.0 28.0 38.0\n" +
            "3\n" +
            "Frame 1 DNA origami aptamer\n" +
            "C 10.5 20.5 30.5\n" +
            "N 15.5 25.5 35.5\n" +
            "P 18.5 28.5 38.5\n";

        byte[] bytes = Encoding.UTF8.GetBytes(sample);
        using MemoryStream stream = new(bytes);
        PipeReader pipeReader = PipeReader.Create(stream);

        XyzPipeReader parser = new();
        int frames = 0;
        await parser.ParseAsync(pipeReader, (idx, coords) =>
        {
            frames++;
            return ValueTask.CompletedTask;
        });

        Assert.Equal(2, frames);
    }
}
