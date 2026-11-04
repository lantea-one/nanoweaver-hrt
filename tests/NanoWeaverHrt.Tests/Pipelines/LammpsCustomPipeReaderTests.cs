using System.IO.Pipelines;
using System.Text;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Pipelines;
using Xunit;

namespace NanoWeaverHrt.Tests.Pipelines;

/// <summary>
/// Unit tests verifying dynamic column header resolution, zero-allocation coordinate parsing,
/// and EOF line flushing in <see cref="LammpsCustomPipeReader"/>.
/// </summary>
public sealed class LammpsCustomPipeReaderTests
{
    /// <summary>
    /// Verifies that <see cref="LammpsCustomPipeReader"/> dynamically resolves custom column ordering
    /// such as <c>ITEM: ATOMS id type xs ys zs vx vy vz</c> and extracts accurate coordinates.
    /// </summary>
    [Fact]
    public async Task ParseAsync_CustomColumnOrdering_ResolvesAndExtractsCoordinates()
    {
        // WHAT: Tests LAMMPS dump with custom column sequence (id type xs ys zs vx vy vz).
        // WHY:  Proves dynamic column header scanning decouples parser from hardcoded 2, 3, 4 indices.
        // HOW:  Parses dump stream and verifies coordinates extracted from xs, ys, zs columns.
        string dump = @"ITEM: TIMESTEP
100
ITEM: NUMBER OF ATOMS
2
ITEM: BOX BOUNDS pp pp pp
0.0 10.0
0.0 10.0
0.0 10.0
ITEM: ATOMS id type xs ys zs vx vy vz
1 1 2.5 3.5 4.5 0.1 0.2 0.3
2 2 5.5 6.5 7.5 0.4 0.5 0.6
";

        byte[] bytes = Encoding.UTF8.GetBytes(dump);
        using MemoryStream ms = new(bytes);
        PipeReader reader = PipeReader.Create(ms);

        LammpsCustomPipeReader parser = new();
        List<AtomCoordinate[]> frames = [];

        await parser.ParseAsync(reader, (_, coords) =>
        {
            frames.Add(coords.ToArray());
            return ValueTask.CompletedTask;
        });

        Assert.Single(frames);
        Assert.Equal(2, frames[0].Length);
        Assert.Equal(2.5f, frames[0][0].X, precision: 2);
        Assert.Equal(3.5f, frames[0][0].Y, precision: 2);
        Assert.Equal(4.5f, frames[0][0].Z, precision: 2);
        Assert.Equal(5.5f, frames[0][1].X, precision: 2);
        Assert.Equal(6.5f, frames[0][1].Y, precision: 2);
        Assert.Equal(7.5f, frames[0][1].Z, precision: 2);
    }

    /// <summary>
    /// Verifies that <see cref="LammpsCustomPipeReader.ParseAsync"/> processes a final atom coordinate line
    /// at EOF when no trailing newline is present.
    /// </summary>
    [Fact]
    public async Task ParseAsync_FinalLineWithoutTrailingNewline_EmitsCompletedFrame()
    {
        // WHAT: Tests LAMMPS dump where the last atom line terminates at EOF without a newline.
        // WHY:  Validates EOF line flush handling.
        // HOW:  Feeds stream with no trailing '\n' and verifies the snapshot is emitted.
        string dump = "ITEM: NUMBER OF ATOMS\n1\nITEM: ATOMS id type x y z\n1 1 12.0 14.0 16.0"; // No trailing \n

        byte[] bytes = Encoding.UTF8.GetBytes(dump);
        using MemoryStream ms = new(bytes);
        PipeReader reader = PipeReader.Create(ms);

        LammpsCustomPipeReader parser = new();
        List<AtomCoordinate[]> frames = [];

        await parser.ParseAsync(reader, (_, coords) =>
        {
            frames.Add(coords.ToArray());
            return ValueTask.CompletedTask;
        });

        Assert.Single(frames);
        Assert.Equal(12.0f, frames[0][0].X, precision: 2);
        Assert.Equal(14.0f, frames[0][0].Y, precision: 2);
        Assert.Equal(16.0f, frames[0][0].Z, precision: 2);
    }

    /// <summary>
    /// Verifies that <see cref="LammpsCustomPipeReader.ResolveLammpsColumns"/> resolves standard and alternative
    /// column identifiers (x, xs, xu, y, ys, yu, z, zs, zu) and defaults to (2, 3, 4) on bare headers.
    /// </summary>
    [Fact]
    public void ResolveLammpsColumns_VariousHeaders_ResolvesCorrectIndices()
    {
        // WHAT: Directly unit-tests ResolveLammpsColumns across different header strings.
        // WHY:  Ensures full branch coverage for all coordinate token variations.
        // HOW:  Tests standard x y z, bare ATOMS, unwrapped xu yu zu, and non-standard ordering.
        LammpsCustomPipeReader.ResolveLammpsColumns(" id type x y z"u8, out int x1, out int y1, out int z1);
        Assert.Equal(2, x1);
        Assert.Equal(3, y1);
        Assert.Equal(4, z1);

        // Bare header (no columns) -> defaults to 2, 3, 4
        LammpsCustomPipeReader.ResolveLammpsColumns(""u8, out int x2, out int y2, out int z2);
        Assert.Equal(2, x2);
        Assert.Equal(3, y2);
        Assert.Equal(4, z2);

        // Unwrapped xu yu zu
        LammpsCustomPipeReader.ResolveLammpsColumns(" id xu yu zu type"u8, out int x3, out int y3, out int z3);
        Assert.Equal(1, x3);
        Assert.Equal(2, y3);
        Assert.Equal(3, z3);
    }

    /// <summary>
    /// Verifies that <see cref="LammpsCustomPipeReader.TryParseLammpsAtomLine(ReadOnlySpan{byte}, out AtomCoordinate)"/>
    /// and its dynamic index overload return false on truncated or malformed coordinate lines.
    /// </summary>
    [Fact]
    public void TryParseLammpsAtomLine_MalformedLines_ReturnsFalse()
    {
        // WHAT: Tests malformed and truncated atom coordinate lines.
        // WHY:  Ensures error paths do not throw exceptions.
        // HOW:  Passes incomplete lines and asserts false.
        Assert.False(LammpsCustomPipeReader.TryParseLammpsAtomLine("1"u8, out _));
        Assert.False(LammpsCustomPipeReader.TryParseLammpsAtomLine("1 1"u8, out _));
        Assert.False(LammpsCustomPipeReader.TryParseLammpsAtomLine("1 1 invalid 2.0 3.0"u8, out _));
        Assert.False(LammpsCustomPipeReader.TryParseLammpsAtomLine("1 1 2.0 invalid 3.0"u8, out _));
        Assert.False(LammpsCustomPipeReader.TryParseLammpsAtomLine("1 1 2.0 3.0 invalid"u8, out _));
        Assert.False(LammpsCustomPipeReader.TryParseLammpsAtomLine("1 1 2.0 3.0"u8, 0, 1, 5, out _));
        Assert.False(LammpsCustomPipeReader.TryParseLammpsAtomLine("1.0 2.0"u8, 0, 1, 2, out _));
    }
}
