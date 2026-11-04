using System.IO.Pipelines;
using System.Text;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Pipelines;
using Xunit;

namespace NanoWeaverHrt.Tests.Pipelines;

/// <summary>
/// Unit tests verifying streaming trajectory parsing, framing resilience, and EOF trailing line handling
/// in <see cref="XyzPipeReader"/>.
/// </summary>
public sealed class XyzPipeReaderTests
{
    /// <summary>
    /// Verifies that <see cref="XyzPipeReader.ParseAsync"/> processes a final coordinate line at EOF
    /// even when the file lacks a trailing newline character.
    /// </summary>
    [Fact]
    public async Task ParseAsync_FinalLineWithoutTrailingNewline_EmitsCompletedFrame()
    {
        // WHAT: Tests an XYZ stream where the final atom line ends immediately at EOF without '\n'.
        // WHY:  Simulation packages often write trajectory snapshots without a final newline.
        // HOW:  Feeds stream with no trailing newline and asserts the frame with both atoms is emitted.
        string xyzContent = "2\nSnapshot Comment\nC 1.0 2.0 3.0\nN 4.0 5.0 6.0"; // No trailing \n
        byte[] bytes = Encoding.UTF8.GetBytes(xyzContent);
        using MemoryStream ms = new(bytes);
        PipeReader reader = PipeReader.Create(ms);

        XyzPipeReader parser = new();
        List<AtomCoordinate[]> frames = [];

        await parser.ParseAsync(reader, (_, coords) =>
        {
            frames.Add(coords.ToArray());
            return ValueTask.CompletedTask;
        });

        Assert.Single(frames);
        Assert.Equal(2, frames[0].Length);
        Assert.Equal(1.0f, frames[0][0].X, precision: 2);
        Assert.Equal(2.0f, frames[0][0].Y, precision: 2);
        Assert.Equal(3.0f, frames[0][0].Z, precision: 2);
        Assert.Equal(4.0f, frames[0][1].X, precision: 2);
        Assert.Equal(5.0f, frames[0][1].Y, precision: 2);
        Assert.Equal(6.0f, frames[0][1].Z, precision: 2);
    }

    /// <summary>
    /// Verifies that <see cref="XyzPipeReader.ParseAsync"/> parses multiple consecutive frames with CRLF line endings.
    /// </summary>
    [Fact]
    public async Task ParseAsync_MultipleFramesWithCrLf_ParsesAllFramesAccurately()
    {
        // WHAT: Tests multi-frame XYZ trajectory with Windows CRLF newlines.
        // WHY:  Verifies frame boundary reset and carriage return stripping.
        // HOW:  Streams two frames with CRLF and asserts 2 frames parsed.
        string xyzContent = "1\r\nFrame 0\r\nO 10.0 20.0 30.0\r\n1\r\nFrame 1\r\nH 11.0 21.0 31.0\r\n";
        byte[] bytes = Encoding.UTF8.GetBytes(xyzContent);
        using MemoryStream ms = new(bytes);
        PipeReader reader = PipeReader.Create(ms);

        XyzPipeReader parser = new();
        List<AtomCoordinate[]> frames = [];

        await parser.ParseAsync(reader, (_, coords) =>
        {
            frames.Add(coords.ToArray());
            return ValueTask.CompletedTask;
        });

        Assert.Equal(2, frames.Count);
        Assert.Equal(10.0f, frames[0][0].X, precision: 2);
        Assert.Equal(11.0f, frames[1][0].X, precision: 2);
    }
}
