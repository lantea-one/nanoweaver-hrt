using System.Text;
using NanoWeaverHrt.Pipelines;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Unit test suite verifying Apache Parquet serialization and DuckDB querying.
/// </summary>
public sealed class ParquetWriterTests
{
    /// <summary>
    /// Sample XYZ format content for serialization testing.
    /// </summary>
    private const string SampleXyz =
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

    /// <summary>
    /// Verifies that <see cref="DuckDbParquetWriter.ConvertTrajectoryToParquetAsync"/> generates a valid Parquet file
    /// and that <see cref="DuckDbParquetWriter.QueryRecordCountAsync"/> returns the correct total row count.
    /// </summary>
    [Fact]
    public async Task ConvertTrajectoryToParquetAsync_ShouldCreateParquetAndQueryRowCount()
    {
        // WHAT: Tests end-to-end conversion from raw trajectory stream to columnar Apache Parquet.
        // WHY:  Ensures DuckDB in-memory staging table, appenders, and Parquet COPY execute reliably.
        // HOW:  Writes a temporary XYZ file, invokes writer, asserts file creation and row count.
        string tempDir = Path.Combine(Path.GetTempPath(), $"weaver_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        string sourceXyz = Path.Combine(tempDir, "sample.xyz");
        string destParquet = Path.Combine(tempDir, "output.parquet");

        try
        {
            await File.WriteAllTextAsync(sourceXyz, SampleXyz, Encoding.UTF8);

            DuckDbParquetWriter writer = new();
            XyzPipeReader parser = new();

            long recordsSerialized = await writer.ConvertTrajectoryToParquetAsync(sourceXyz, destParquet, parser);

            // 2 frames * 3 atoms = 6 total atomic coordinate records
            Assert.Equal(6, recordsSerialized);
            Assert.True(File.Exists(destParquet));

            FileInfo fileInfo = new(destParquet);
            Assert.True(fileInfo.Length > 0);

            long queriedCount = await writer.QueryRecordCountAsync(destParquet);
            Assert.Equal(6, queriedCount);

            // Test ExportToParquetAsync and QueryParquetAsync delegation
            string exportPath = Path.Combine(tempDir, "exported.parquet");
            long exportedCount = await writer.ExportToParquetAsync(sourceXyz, exportPath, parser);
            Assert.Equal(6, exportedCount);
            long queriedExportCount = await writer.QueryParquetAsync(exportPath);
            Assert.Equal(6, queriedExportCount);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that <see cref="DuckDbParquetWriter"/> rejects path traversal segments in file paths.
    /// </summary>
    [Fact]
    public async Task ParquetWriter_RejectsPathTraversal()
    {
        // WHAT: Tests path traversal validation in DuckDbParquetWriter methods.
        // WHY:  Prevents directory traversal attacks ('..') in output and query paths.
        // HOW:  Passes traversal strings to ConvertTrajectoryToParquetAsync, ExportToParquetAsync, and QueryParquetAsync.
        DuckDbParquetWriter writer = new();
        XyzPipeReader parser = new();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            writer.ConvertTrajectoryToParquetAsync("in.xyz", "../out.parquet", parser));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            writer.ExportToParquetAsync("in.xyz", "../out.parquet", parser));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            writer.QueryRecordCountAsync("../test.parquet"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            writer.QueryParquetAsync("../test.parquet"));
    }
}
