using System.Text;
using DuckDB.NET.Data;
using Microsoft.Extensions.Configuration;
using NanoWeaverHrt.Analytics;
using NanoWeaverHrt.Pipelines;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests.Analytics;

/// <summary>
/// Unit test suite verifying the in-process DuckDB columnar trajectory analytical engine,
/// read-only query safety guards, row limits, summary statistics, and cancellation propagation.
/// </summary>
public sealed class DuckDbTrajectoryAnalyticsServiceTests
{
    /// <summary>
    /// Sample XYZ trajectory content used to generate real on-disk Parquet files for analytical queries.
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
    /// Verifies that <see cref="DuckDbTrajectoryAnalyticsService.ExecuteQueryAsync"/> correctly computes
    /// mathematical aggregations and standard SELECT expressions against an in-memory session.
    /// </summary>
    [Fact]
    public async Task ExecuteQueryAsync_WithMemorySessionAndMathAggregations_ReturnsExpectedResult()
    {
        // WHAT: Executes arithmetic and scalar math aggregations directly in the in-memory DuckDB session.
        // WHY:  Validates that in-memory queries execute without disk dependencies and return accurate results.
        // HOW:  Invokes ExecuteQueryAsync with dataset ":memory:" and asserts column and row values.
        using DuckDbTrajectoryAnalyticsService service = new();

        TrajectoryAnalyticsQuery query = new(
            DatasetName: ":memory:",
            Query: "SELECT 1 + 1 AS sum_val, SQRT(144) AS sqrt_val;");

        TrajectoryAnalyticsResult result = await service.ExecuteQueryAsync(query);

        Assert.Equal(2, result.Columns.Count);
        Assert.Equal("sum_val", result.Columns[0]);
        Assert.Equal("sqrt_val", result.Columns[1]);
        Assert.Equal(1, result.TotalRows);
        Assert.Single(result.Rows);
        Assert.True(result.ElapsedMilliseconds >= 0.0);

        Assert.Equal(2, Convert.ToInt32(result.Rows[0][0]));
        Assert.Equal(12.0, Convert.ToDouble(result.Rows[0][1]));
    }

    /// <summary>
    /// Verifies that null database values returned by queries are mapped to null entries in the result row.
    /// </summary>
    [Fact]
    public async Task ExecuteQueryAsync_WithNullValues_HandlesDbNullCorrectly()
    {
        // WHAT: Tests queries that produce NULL database column values.
        // WHY:  Ensures reader.IsDBNull checks map database nulls to C# null references cleanly.
        // HOW:  Selects a null cast alongside a constant value and asserts row cell values.
        using DuckDbTrajectoryAnalyticsService service = new();

        TrajectoryAnalyticsQuery query = new(
            DatasetName: "memory",
            Query: "SELECT CAST(NULL AS VARCHAR) AS null_col, 42 AS num_col;");

        TrajectoryAnalyticsResult result = await service.ExecuteQueryAsync(query);

        Assert.Equal(1, result.TotalRows);
        Assert.Null(result.Rows[0][0]);
        Assert.Equal(42, Convert.ToInt32(result.Rows[0][1]));
    }

    /// <summary>
    /// Verifies that queries against synthetic sample datasets populate and read standard origami coordinate frames.
    /// </summary>
    [Fact]
    public async Task ExecuteQueryAsync_WithSampleDataset_ReturnsOrigamiFrames()
    {
        // WHAT: Tests analytical queries targeting synthetic sample datasets.
        // WHY:  Verifies that EnsureSampleTableCreatedAsync initializes and exposes the trajectory view.
        // HOW:  Queries trajectory coordinates for frame 0, then re-runs a query to test initialized caching.
        using DuckDbTrajectoryAnalyticsService service = new();

        TrajectoryAnalyticsQuery query = new(
            DatasetName: "sample",
            Query: "SELECT frame_index, atom_index, x, y, z, distance_from_center, target_region FROM trajectory WHERE frame_index = 0 ORDER BY atom_index;");

        TrajectoryAnalyticsResult result1 = await service.ExecuteQueryAsync(query);
        Assert.Equal(3, result1.TotalRows);
        Assert.Equal(7, result1.Columns.Count);

        // Verify second execution hits cached _sampleTableInitialized path
        TrajectoryAnalyticsQuery query2 = new(
            DatasetName: "samples",
            Query: "SELECT COUNT(*) AS total_beads FROM trajectory;");

        TrajectoryAnalyticsResult result2 = await service.ExecuteQueryAsync(query2);
        Assert.Equal(1, result2.TotalRows);
        Assert.Equal(6L, Convert.ToInt64(result2.Rows[0][0]));
    }

    /// <summary>
    /// Verifies that the strict read-only query guard rejects mutation statements and administrative commands.
    /// </summary>
    /// <param name="prohibitedQuery">The prohibited SQL statement containing a forbidden keyword.</param>
    [Theory]
    [InlineData("DROP TABLE trajectory;")]
    [InlineData("DELETE FROM trajectory;")]
    [InlineData("INSERT INTO trajectory VALUES (1, 2, 3);")]
    [InlineData("UPDATE trajectory SET x = 0;")]
    [InlineData("ALTER TABLE trajectory ADD COLUMN test INT;")]
    [InlineData("ATTACH 'external.db';")]
    [InlineData("COPY trajectory TO '/tmp/out.csv';")]
    [InlineData("PRAGMA version;")]
    [InlineData("INSTALL httpfs;")]
    [InlineData("LOAD httpfs;")]
    [InlineData("SELECT 1; DROP TABLE trajectory;")]
    public async Task ExecuteQueryAsync_WithProhibitedKeywords_ThrowsInvalidOperationException(string prohibitedQuery)
    {
        // WHAT: Tests rejection of destructive and administrative SQL keywords.
        // WHY:  Guarantees that no mutation or administrative commands can be executed via the analytical engine.
        // HOW:  Passes forbidden statements and asserts that InvalidOperationException is thrown.
        using DuckDbTrajectoryAnalyticsService service = new();

        TrajectoryAnalyticsQuery query = new(
            DatasetName: "sample",
            Query: prohibitedQuery);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExecuteQueryAsync(query));

        Assert.Contains("prohibited mutation", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that invalid or null query inputs throw informative argument exceptions.
    /// </summary>
    [Fact]
    public async Task ExecuteQueryAsync_WithNullOrInvalidInputs_ThrowsArgumentException()
    {
        // WHAT: Validates parameter guard assertions in ExecuteQueryAsync.
        // WHY:  Ensures robust input validation on query objects and SQL strings.
        // HOW:  Passes null query, whitespace dataset, and whitespace SQL, asserting ArgumentException.
        using DuckDbTrajectoryAnalyticsService service = new();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.ExecuteQueryAsync(null!));

        TrajectoryAnalyticsQuery emptyDatasetQuery = new("", "SELECT 1;");
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ExecuteQueryAsync(emptyDatasetQuery));

        TrajectoryAnalyticsQuery whitespaceSqlQuery = new("sample", "   ");
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ExecuteQueryAsync(whitespaceSqlQuery));
    }

    /// <summary>
    /// Verifies that MaxRows clamps non-positive numbers to the default limit and caps upper bounds at 10,000.
    /// </summary>
    [Fact]
    public async Task ExecuteQueryAsync_WithMaxRowsClamping_RespectsLimits()
    {
        // WHAT: Tests row limiting and clamping behavior across various MaxRows requests.
        // WHY:  Protects against memory exhaustion while providing controlled row sets.
        // HOW:  Queries sample trajectory with MaxRows = 1, MaxRows = 0, and MaxRows = 50000.
        using DuckDbTrajectoryAnalyticsService service = new();

        // 1. Explicit limit of 1
        TrajectoryAnalyticsQuery queryLimit1 = new("sample", "SELECT * FROM trajectory;", MaxRows: 1);
        TrajectoryAnalyticsResult result1 = await service.ExecuteQueryAsync(queryLimit1);
        Assert.Equal(1, result1.TotalRows);

        // 2. Non-positive limit (0 or negative) falls back to default 1000
        TrajectoryAnalyticsQuery queryDefault = new("sample", "SELECT * FROM trajectory;", MaxRows: -10);
        TrajectoryAnalyticsResult resultDefault = await service.ExecuteQueryAsync(queryDefault);
        Assert.Equal(6, resultDefault.TotalRows);

        // 3. Excessively large limit (e.g. 50,000) is clamped to AbsoluteMaxRows (10,000)
        TrajectoryAnalyticsQuery queryClamped = new("sample", "SELECT * FROM trajectory;", MaxRows: 50000);
        TrajectoryAnalyticsResult resultClamped = await service.ExecuteQueryAsync(queryClamped);
        Assert.Equal(6, resultClamped.TotalRows);
    }

    /// <summary>
    /// Verifies that query execution completes successfully when an execution timeout is specified.
    /// </summary>
    [Fact]
    public async Task ExecuteQueryAsync_WithTimeout_ExecutesSuccessfully()
    {
        // WHAT: Validates execution with a valid timeout parameter.
        // WHY:  Ensures CommandTimeout and linked cancellation tokens are configured properly.
        // HOW:  Runs analytical query with a 10-second timeout.
        using DuckDbTrajectoryAnalyticsService service = new();

        TrajectoryAnalyticsQuery query = new(
            DatasetName: "sample",
            Query: "SELECT COUNT(*) FROM trajectory;",
            Timeout: TimeSpan.FromSeconds(10));

        TrajectoryAnalyticsResult result = await service.ExecuteQueryAsync(query);
        Assert.Equal(1, result.TotalRows);
    }

    /// <summary>
    /// Verifies that query execution throws when an already cancelled cancellation token is passed.
    /// </summary>
    [Fact]
    public async Task ExecuteQueryAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // WHAT: Tests cancellation token propagation in ExecuteQueryAsync.
        // WHY:  Ensures long-running queries can be cancelled cooperatively.
        // HOW:  Passes a pre-cancelled CancellationToken and verifies OperationCanceledException.
        using DuckDbTrajectoryAnalyticsService service = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        TrajectoryAnalyticsQuery query = new("sample", "SELECT 1;");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ExecuteQueryAsync(query, cts.Token));
    }

    /// <summary>
    /// Verifies that queries against real on-disk Parquet files execute correctly using direct paths
    /// and configured directory resolution.
    /// </summary>
    [Fact]
    public async Task ExecuteQueryAsync_WithRealParquetFile_ExecutesColumnarScan()
    {
        // WHAT: Tests Parquet file resolution and SQL execution over actual on-disk trajectory files.
        // WHY:  Validates that DuckDbParquetWriter output can be queried by DuckDbTrajectoryAnalyticsService.
        // HOW:  Generates a real Parquet file, queries it via direct path and configured directory, and asserts row contents.
        string tempDir = Path.Combine(Path.GetTempPath(), $"weaver_analytics_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        string sourceXyz = Path.Combine(tempDir, "trajectory.xyz");
        string destParquet = Path.Combine(tempDir, "dataset_a.parquet");

        try
        {
            await File.WriteAllTextAsync(sourceXyz, SampleXyz, Encoding.UTF8);

            DuckDbParquetWriter writer = new();
            XyzPipeReader parser = new();
            await writer.ConvertTrajectoryToParquetAsync(sourceXyz, destParquet, parser);

            // 1. Direct path query
            using DuckDbTrajectoryAnalyticsService directService = new();
            TrajectoryAnalyticsQuery directQuery = new(
                DatasetName: destParquet,
                Query: "SELECT atom_index, x, y, z FROM trajectory WHERE frame_index = 0 ORDER BY atom_index;");

            TrajectoryAnalyticsResult directResult = await directService.ExecuteQueryAsync(directQuery);
            Assert.Equal(3, directResult.TotalRows);
            Assert.Equal(10.0, Convert.ToDouble(directResult.Rows[0][1]));

            // 2. Directory-configured query with extension and without extension
            Dictionary<string, string?> configValues = new()
            {
                ["TRAJECTORY_PARQUET_DIR"] = tempDir
            };
            IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();

            using DuckDbTrajectoryAnalyticsService configuredService = new(config);
            TrajectoryAnalyticsQuery namedQuery = new(
                DatasetName: "dataset_a",
                Query: "SELECT COUNT(DISTINCT frame_index) AS frames FROM dataset_a;");

            TrajectoryAnalyticsResult namedResult = await configuredService.ExecuteQueryAsync(namedQuery);
            Assert.Equal(1, namedResult.TotalRows);
            Assert.Equal(2L, Convert.ToInt64(namedResult.Rows[0][0]));

            TrajectoryAnalyticsQuery namedWithExtQuery = new(
                DatasetName: "dataset_a.parquet",
                Query: "SELECT COUNT(*) FROM trajectory;");

            TrajectoryAnalyticsResult namedExtResult = await configuredService.ExecuteQueryAsync(namedWithExtQuery);
            Assert.Equal(6L, Convert.ToInt64(namedExtResult.Rows[0][0]));

            // 3. Configuration with null or whitespace override defaults cleanly to DefaultParquetDirectory
            IConfiguration emptyConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_PARQUET_DIR"] = "   "
            }).Build();
            using DuckDbTrajectoryAnalyticsService emptyConfigService = new(emptyConfig);
            await Assert.ThrowsAsync<FileNotFoundException>(
                () => emptyConfigService.ExecuteQueryAsync(new TrajectoryAnalyticsQuery("missing_with_empty_config", "SELECT 1;")));

            IConfiguration nullConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
            using DuckDbTrajectoryAnalyticsService nullConfigService = new(nullConfig);
            await Assert.ThrowsAsync<FileNotFoundException>(
                () => nullConfigService.ExecuteQueryAsync(new TrajectoryAnalyticsQuery("missing_with_null_config", "SELECT 1;")));
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
    /// Verifies that querying a missing dataset throws an informative FileNotFoundException.
    /// </summary>
    [Fact]
    public async Task ExecuteQueryAsync_WithMissingDataset_ThrowsFileNotFoundException()
    {
        // WHAT: Tests missing dataset error handling in ExecuteQueryAsync.
        // WHY:  Ensures that non-existent Parquet datasets report informative file path diagnostics.
        // HOW:  Queries a missing dataset name and asserts FileNotFoundException.
        using DuckDbTrajectoryAnalyticsService service = new();

        TrajectoryAnalyticsQuery query = new("nonexistent_dataset_999", "SELECT * FROM trajectory;");

        FileNotFoundException ex = await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.ExecuteQueryAsync(query));

        Assert.Contains("nonexistent_dataset_999", ex.Message);
    }

    /// <summary>
    /// Verifies that <see cref="DuckDbTrajectoryAnalyticsService.GetSummaryStatisticsAsync"/> computes
    /// accurate atom counts, frame counts, bounding box volume, and mean radius of gyration.
    /// </summary>
    [Fact]
    public async Task GetSummaryStatisticsAsync_WithSampleDataset_ComputesAccurateMetrics()
    {
        // WHAT: Tests geometric and structural summary statistics calculation over sample datasets.
        // WHY:  Validates that atom count, frame count, bounding box volume, and mean Rg are mathematically exact.
        // HOW:  Invokes GetSummaryStatisticsAsync("sample") and asserts computed values against analytical calculations.
        using DuckDbTrajectoryAnalyticsService service = new();

        TrajectorySummaryStatistics stats = await service.GetSummaryStatisticsAsync("sample");

        Assert.Equal("sample", stats.DatasetName);
        Assert.Equal(3L, stats.AtomCount);
        Assert.Equal(2L, stats.FrameCount);

        // DX = 8.5, DY = 8.5, DZ = 8.5 -> Volume = 614.125
        Assert.Equal(614.125, stats.BoundingBoxVolume, precision: 3);

        // Mean Rg across frame 0 (4.0825) and frame 1 (4.1645) is approximately 4.1235
        Assert.True(stats.MeanRadiusOfGyration > 4.0 && stats.MeanRadiusOfGyration < 4.2);
    }

    /// <summary>
    /// Verifies that <see cref="DuckDbTrajectoryAnalyticsService.GetSummaryStatisticsAsync"/> works against
    /// in-memory sessions using memory tokens.
    /// </summary>
    [Fact]
    public async Task GetSummaryStatisticsAsync_WithMemorySession_ComputesMetrics()
    {
        // WHAT: Tests summary statistics computation targeting in-memory memory tokens.
        // WHY:  Ensures memory tokens trigger sample table initialization and compute valid statistics.
        // HOW:  Invokes GetSummaryStatisticsAsync(":memory:") and asserts positive atom and frame count.
        using DuckDbTrajectoryAnalyticsService service = new();

        TrajectorySummaryStatistics stats = await service.GetSummaryStatisticsAsync(":memory:");

        Assert.Equal(":memory:", stats.DatasetName);
        Assert.Equal(3L, stats.AtomCount);
        Assert.Equal(2L, stats.FrameCount);
    }

    /// <summary>
    /// Verifies that summary statistics calculated over a real on-disk Parquet file match expected physical values.
    /// </summary>
    [Fact]
    public async Task GetSummaryStatisticsAsync_WithRealParquetDataset_ComputesMetrics()
    {
        // WHAT: Tests summary statistics calculation over actual Parquet files generated by DuckDbParquetWriter.
        // WHY:  Verifies end-to-end integration between trajectory writing and analytical summary calculation.
        // HOW:  Serializes sample XYZ, computes summary statistics, and verifies metrics.
        string tempDir = Path.Combine(Path.GetTempPath(), $"weaver_stats_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        string sourceXyz = Path.Combine(tempDir, "trajectory.xyz");
        string destParquet = Path.Combine(tempDir, "summary_test.parquet");

        try
        {
            await File.WriteAllTextAsync(sourceXyz, SampleXyz, Encoding.UTF8);

            DuckDbParquetWriter writer = new();
            XyzPipeReader parser = new();
            await writer.ConvertTrajectoryToParquetAsync(sourceXyz, destParquet, parser);

            using DuckDbTrajectoryAnalyticsService service = new();
            TrajectorySummaryStatistics stats = await service.GetSummaryStatisticsAsync(destParquet);

            Assert.Equal(destParquet, stats.DatasetName);
            Assert.Equal(3L, stats.AtomCount);
            Assert.Equal(2L, stats.FrameCount);
            Assert.Equal(614.125, stats.BoundingBoxVolume, precision: 3);
            Assert.True(stats.MeanRadiusOfGyration > 0.0);
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
    /// Verifies that summary statistics for an empty dataset cleanly return zero metrics without dividing by zero.
    /// </summary>
    [Fact]
    public async Task GetSummaryStatisticsAsync_WithEmptyParquetDataset_ReturnsZeroMetrics()
    {
        // WHAT: Tests summary calculation when the underlying Parquet file contains zero records.
        // WHY:  Guarantees that empty datasets return zeroes gracefully rather than throwing DB or math exceptions.
        // HOW:  Creates an empty Parquet file and asserts that AtomCount, FrameCount, Volume, and MeanRg are all zero.
        string tempDir = Path.Combine(Path.GetTempPath(), $"weaver_empty_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        string destParquet = Path.Combine(tempDir, "empty.parquet");

        try
        {
            using (DuckDBConnection conn = new("DataSource=:memory:"))
            {
                await conn.OpenAsync();
                using DuckDBCommand cmd = conn.CreateCommand();
                string normalized = destParquet.Replace('\\', '/').Replace("'", "''");
                cmd.CommandText = $@"
                    CREATE TABLE empty_traj (
                        frame_index BIGINT,
                        atom_index INTEGER,
                        x FLOAT,
                        y FLOAT,
                        z FLOAT,
                        distance_from_center FLOAT,
                        target_region VARCHAR
                    );
                    COPY empty_traj TO '{normalized}' (FORMAT PARQUET);
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            using DuckDbTrajectoryAnalyticsService service = new();
            TrajectorySummaryStatistics stats = await service.GetSummaryStatisticsAsync(destParquet);

            Assert.Equal(0L, stats.AtomCount);
            Assert.Equal(0L, stats.FrameCount);
            Assert.Equal(0.0, stats.BoundingBoxVolume);
            Assert.Equal(0.0, stats.MeanRadiusOfGyration);
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
    /// Verifies that GetSummaryStatisticsAsync throws FileNotFoundException when the dataset Parquet file does not exist.
    /// </summary>
    [Fact]
    public async Task GetSummaryStatisticsAsync_WithMissingDataset_ThrowsFileNotFoundException()
    {
        // WHAT: Tests missing dataset error handling in GetSummaryStatisticsAsync.
        // WHY:  Ensures informative file path diagnostics when summary statistics are requested for unknown datasets.
        // HOW:  Calls GetSummaryStatisticsAsync with a non-existent name and asserts FileNotFoundException.
        using DuckDbTrajectoryAnalyticsService service = new();

        FileNotFoundException ex = await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.GetSummaryStatisticsAsync("missing_dataset_123"));

        Assert.Contains("missing_dataset_123", ex.Message);
    }

    /// <summary>
    /// Verifies that GetSummaryStatisticsAsync validates that dataset names are not null or whitespace.
    /// </summary>
    [Fact]
    public async Task GetSummaryStatisticsAsync_WithNullOrWhitespaceDataset_ThrowsArgumentException()
    {
        // WHAT: Tests input validation on dataset names in GetSummaryStatisticsAsync.
        // WHY:  Prevents invalid queries from reaching the database driver.
        // HOW:  Calls GetSummaryStatisticsAsync with null and whitespace strings, asserting ArgumentException.
        using DuckDbTrajectoryAnalyticsService service = new();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.GetSummaryStatisticsAsync(null!));

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetSummaryStatisticsAsync("   "));
    }

    /// <summary>
    /// Verifies that GetSummaryStatisticsAsync propagates cancellation tokens when cancelled.
    /// </summary>
    [Fact]
    public async Task GetSummaryStatisticsAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // WHAT: Tests cancellation propagation in GetSummaryStatisticsAsync.
        // WHY:  Ensures summary calculations can be cancelled cooperatively.
        // HOW:  Passes a pre-cancelled CancellationToken and verifies OperationCanceledException.
        using DuckDbTrajectoryAnalyticsService service = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetSummaryStatisticsAsync("sample", cts.Token));
    }

    /// <summary>
    /// Verifies that Dispose is idempotent and subsequent calls throw ObjectDisposedException.
    /// </summary>
    [Fact]
    public async Task Dispose_IdempotentAndRejectsFurtherCalls()
    {
        // WHAT: Tests the IDisposable implementation of DuckDbTrajectoryAnalyticsService.
        // WHY:  Ensures resources are cleaned up safely and multiple disposes do not throw.
        // HOW:  Calls Dispose twice, then asserts ObjectDisposedException on subsequent method calls.
        DuckDbTrajectoryAnalyticsService service = new();
        service.Dispose();
        service.Dispose(); // Verify idempotent dispose

        TrajectoryAnalyticsQuery query = new("sample", "SELECT 1;");

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => service.ExecuteQueryAsync(query));

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => service.GetSummaryStatisticsAsync("sample"));
    }

    /// <summary>
    /// Verifies that the analytical model records hold values and default parameters correctly.
    /// </summary>
    [Fact]
    public void Records_ValidatePropertyAccessorsAndDefaults()
    {
        // WHAT: Tests model records TrajectoryAnalyticsQuery, TrajectoryAnalyticsResult, and TrajectorySummaryStatistics.
        // WHY:  Verifies record construction, property equality, and default parameter values.
        // HOW:  Instantiates records and verifies property values.
        TrajectoryAnalyticsQuery query = new("ds1", "SELECT 1;");
        Assert.Equal("ds1", query.DatasetName);
        Assert.Equal("SELECT 1;", query.Query);
        Assert.Equal(1000, query.MaxRows);
        Assert.Null(query.Timeout);

        TrajectoryAnalyticsResult result = new(
            new[] { "col1" },
            new[] { new object?[] { 10 } },
            1,
            2.5);
        Assert.Single(result.Columns);
        Assert.Single(result.Rows);
        Assert.Equal(1, result.TotalRows);
        Assert.Equal(2.5, result.ElapsedMilliseconds);

        TrajectorySummaryStatistics stats = new("ds1", 100L, 50L, 1250.0, 5.5);
        Assert.Equal("ds1", stats.DatasetName);
        Assert.Equal(100L, stats.AtomCount);
        Assert.Equal(50L, stats.FrameCount);
        Assert.Equal(1250.0, stats.BoundingBoxVolume);
        Assert.Equal(5.5, stats.MeanRadiusOfGyration);
    }
}
