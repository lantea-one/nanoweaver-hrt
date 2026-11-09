using System.Text;
using NanoWeaverHrt.Models;
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

    /// <summary>
    /// Verifies that <see cref="DuckDbParquetWriter.WriteFrameBatchAsync"/> serializes coordinate batches to Parquet,
    /// overwrites existing files, and supports empty coordinate slices.
    /// </summary>
    [Fact]
    public async Task WriteFrameBatchAsync_SerializesBatchAndHandlesEmptyFrame()
    {
        // WHAT: Tests single-frame/batch Parquet writing via DuckDbParquetWriter.
        // WHY:  Validates zero-allocation batch exports with SIMD distance calculation.
        // HOW:  Invokes WriteFrameBatchAsync with 5 coordinates, queries count, then tests with empty slice and overwriting.
        string tempDir = Path.Combine(Path.GetTempPath(), $"weaver_test_batch_{Guid.NewGuid():N}");
        string destParquet = Path.Combine(tempDir, "nested", "batch_output.parquet");

        try
        {
            DuckDbParquetWriter writer = new();
            AtomCoordinate[] coords =
            [
                new(0.0f, 10.0f, 0.0f),
                new(1.5f, 10.2f, 0.5f),
                new(2.2f, 11.0f, 1.2f),
                new(3.1f, 10.8f, 2.0f),
                new(4.0f, 12.0f, 3.0f)
            ];

            long records = await writer.WriteFrameBatchAsync(destParquet, 0, coords);
            Assert.Equal(5, records);
            Assert.True(File.Exists(destParquet));

            long queried = await writer.QueryRecordCountAsync(destParquet);
            Assert.Equal(5, queried);

            // Test overwriting existing file with empty coordinates slice
            long emptyRecords = await writer.WriteFrameBatchAsync(destParquet, 1, ReadOnlyMemory<AtomCoordinate>.Empty);
            Assert.Equal(0, emptyRecords);
            long queriedEmpty = await writer.QueryRecordCountAsync(destParquet);
            Assert.Equal(0, queriedEmpty);

            // Test writing with bare filename (no directory segment)
            string bareFileName = $"weaver_bare_{Guid.NewGuid():N}.parquet";
            try
            {
                long bareRecords = await writer.WriteFrameBatchAsync(bareFileName, 0, coords);
                Assert.Equal(5, bareRecords);
            }
            finally
            {
                if (File.Exists(bareFileName))
                {
                    File.Delete(bareFileName);
                }
            }

            // Test validation branches
            await Assert.ThrowsAsync<ArgumentNullException>(() => writer.WriteFrameBatchAsync(null!, 0, coords));
            await Assert.ThrowsAsync<ArgumentException>(() => writer.WriteFrameBatchAsync("   ", 0, coords));
            await Assert.ThrowsAsync<ArgumentException>(() => writer.WriteFrameBatchAsync("../traversal.parquet", 0, coords));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
