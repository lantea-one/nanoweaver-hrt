using System.Buffers;
using System.IO.Pipelines;
using DuckDB.NET.Data;
using NanoWeaverHrt.Abstractions;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Pipelines.Simd;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Columnar Apache Parquet serializer leveraging DuckDB's in-process analytical engine and zero-allocation pipelines.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Architecture Overview:</strong> Streams raw trajectory files using <see cref="PipeReader"/>,
/// computes centroid distances using hardware-accelerated SIMD instructions via <see cref="SimdCoordinateParser"/>,
/// appends coordinate tuples into an in-memory DuckDB staging table using native unmanaged appenders, and exports
/// directly to Apache Parquet with Zstandard (ZSTD) compression.
/// </para>
/// <para>
/// <strong>Biomedical Context:</strong> Enables the sovereign node to convert high-frequency molecular simulation
/// trajectories into Parquet files that can be queried instantaneously by <c>pg_duckdb</c> in PostgreSQL.
/// </para>
/// </remarks>
public sealed class DuckDbParquetWriter : IParquetTrajectoryWriter
{
    /// <summary>
    /// Tag identifying scaffold DNA origami beads.
    /// </summary>
    private const string ScaffoldRegionTag = "scaffold";

    /// <summary>
    /// Tag identifying aptamer binding pocket beads.
    /// </summary>
    private const string AptamerRegionTag = "aptamer_pocket";

    /// <summary>
    /// Tag identifying hormone ligand molecules (Estrogen/Testosterone).
    /// </summary>
    private const string LigandRegionTag = "hormone_payload";

    /// <inheritdoc/>
    public async Task<long> ConvertTrajectoryToParquetAsync(
        string sourceTrajectoryPath,
        string destinationParquetPath,
        ITrajectoryParser parser,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceTrajectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationParquetPath);
        ArgumentNullException.ThrowIfNull(parser);

        if (destinationParquetPath.Contains(".."))
        {
            throw new ArgumentException("Path traversal segments ('..') are prohibited in Parquet output path.", nameof(destinationParquetPath));
        }

        if (!File.Exists(sourceTrajectoryPath))
        {
            throw new FileNotFoundException("Source trajectory file does not exist.", sourceTrajectoryPath);
        }

        string? destinationDir = Path.GetDirectoryName(destinationParquetPath);
        if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        // Delete previous export if present to ensure clean atomic Parquet generation
        if (File.Exists(destinationParquetPath))
        {
            File.Delete(destinationParquetPath);
        }

        long totalRecordsSerialized = 0;

        // WHAT: Initializes an in-memory DuckDB session.
        // WHY:  DuckDB provides native, zero-dependency C-level Parquet encoding with dictionary compression.
        // HOW:  Opens an in-memory connection and prepares a staging table.
        // NOTE: The CREATE TABLE below is NOT schema evolution and is deliberately outside the
        //       IDatabaseMigration pipeline required by AGENTS.md Prime Directive 13. It builds a
        //       throwaway staging table inside a private ":memory:" DuckDB session that exists only
        //       for the duration of this one export and is discarded when the connection closes.
        //       Nothing persists, so there is no schema version to record and no migration to run.
        using DuckDBConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync(cancellationToken);

        using (DuckDBCommand createTableCmd = connection.CreateCommand())
        {
            createTableCmd.CommandText = @"
                CREATE TABLE staging_trajectory (
                    frame_index BIGINT,
                    atom_index INTEGER,
                    x FLOAT,
                    y FLOAT,
                    z FLOAT,
                    distance_from_center FLOAT,
                    target_region VARCHAR
                );";
            await createTableCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Open DuckDB native appender for maximum streaming throughput
        using (DuckDBAppender appender = connection.CreateAppender("staging_trajectory"))
        {
            // Open streaming pipeline over source trajectory file
            await using FileStream fileStream = new(
                sourceTrajectoryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 65536,
                useAsync: true);

            PipeReader pipeReader = PipeReader.Create(fileStream);

            // Stream frames through parser and append directly to DuckDB
            await parser.ParseAsync(pipeReader, (frameIndex, coordinates) =>
            {
                if (coordinates.IsEmpty)
                {
                    return ValueTask.CompletedTask;
                }

                int atomCount = coordinates.Length;
                float[] rentedDistances = ArrayPool<float>.Shared.Rent(atomCount);

                try
                {
                    // WHAT: Computes geometric centroid and radial distances using SIMD intrinsics.
                    // WHY:  Calculates nanocarrier expansion and ligand displacement metrics without heap overhead.
                    // HOW:  Averages X, Y, Z and invokes SimdCoordinateParser.ComputeDistancesFromCentroidSimd.
                    AtomCoordinate centroid = CalculateCentroid(coordinates);
                    SimdCoordinateParser.ComputeDistancesFromCentroidSimd(
                        coordinates,
                        centroid,
                        rentedDistances.AsSpan(0, atomCount));

                    for (int i = 0; i < atomCount; i++)
                    {
                        AtomCoordinate coord = coordinates[i];
                        float dist = rentedDistances[i];
                        string region = ClassifyAtomRegion(i, atomCount);

                        IDuckDBAppenderRow row = appender.CreateRow();
                        row.AppendValue(frameIndex);
                        row.AppendValue(i);
                        row.AppendValue(coord.X);
                        row.AppendValue(coord.Y);
                        row.AppendValue(coord.Z);
                        row.AppendValue(dist);
                        row.AppendValue(region);
                        row.EndRow();

                        totalRecordsSerialized++;
                    }
                }
                finally
                {
                    ArrayPool<float>.Shared.Return(rentedDistances);
                }

                return ValueTask.CompletedTask;
            }, cancellationToken);
        }

        // WHAT: Flushes all staging rows to the columnar Apache Parquet file on disk.
        // WHY:  Applies Snappy or ZSTD compression and dictionary encoding to minimize storage footprint.
        // HOW:  Executes DuckDB COPY command.
        using (DuckDBCommand copyCommand = connection.CreateCommand())
        {
            // Normalize path separators to forward slashes and escape single quotes for DuckDB SQL syntax compatibility
            string normalizedPath = destinationParquetPath.Replace('\\', '/').Replace("'", "''");
            copyCommand.CommandText = $"COPY staging_trajectory TO '{normalizedPath}' (FORMAT PARQUET, COMPRESSION ZSTD);";
            await copyCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        return totalRecordsSerialized;
    }

    /// <inheritdoc/>
    public async Task<long> QueryRecordCountAsync(
        string parquetFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parquetFilePath);

        if (parquetFilePath.Contains(".."))
        {
            throw new ArgumentException("Path traversal segments ('..') are prohibited in Parquet file path.", nameof(parquetFilePath));
        }

        if (!File.Exists(parquetFilePath))
        {
            throw new FileNotFoundException("Target Parquet file does not exist.", parquetFilePath);
        }

        using DuckDBConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync(cancellationToken);

        using DuckDBCommand queryCmd = connection.CreateCommand();
        string normalizedPath = parquetFilePath.Replace('\\', '/').Replace("'", "''");
        queryCmd.CommandText = $"SELECT COUNT(*) FROM read_parquet('{normalizedPath}');";

        object? result = await queryCmd.ExecuteScalarAsync(cancellationToken);
        return result is long count ? count : Convert.ToInt64(result);
    }

    /// <summary>
    /// Computes the arithmetic centroid of a coordinate frame slice without allocating memory.
    /// </summary>
    /// <param name="coordinates">The slice of Cartesian atom coordinates.</param>
    /// <returns>An <see cref="AtomCoordinate"/> representing the frame centroid.</returns>
    internal static AtomCoordinate CalculateCentroid(ReadOnlySpan<AtomCoordinate> coordinates)
    {
        // WHAT: Calculates the geometric center of mass of the atomic structure.
        // WHY:  Serves as the reference origin for computing radial bead distances.
        // HOW:  Accumulates coordinate sums and divides by total atom count.
        if (coordinates.IsEmpty)
        {
            return default;
        }

        double sumX = 0.0;
        double sumY = 0.0;
        double sumZ = 0.0;

        for (int i = 0; i < coordinates.Length; i++)
        {
            AtomCoordinate c = coordinates[i];
            sumX += c.X;
            sumY += c.Y;
            sumZ += c.Z;
        }

        float inv = (float)(1.0 / coordinates.Length);
        return new AtomCoordinate((float)sumX * inv, (float)sumY * inv, (float)sumZ * inv);
    }

    /// <summary>
    /// Classifies an atom or bead index into functional nanocarrier regions.
    /// </summary>
    /// <param name="atomIndex">The sequential atom index within the frame.</param>
    /// <param name="totalAtoms">The total atom count in the simulation frame.</param>
    /// <returns>A string tag describing the functional biomedical domain.</returns>
    private static string ClassifyAtomRegion(int atomIndex, int totalAtoms)
    {
        // WHAT: Assigns biomedical classification tags based on bead sequence.
        // WHY:  Allows columnar queries in pg_duckdb to filter specifically on ligand vs aptamer beads.
        // HOW:  In standard model conventions, pocket beads are the initial indices and ligand is the terminal bead.
        if (totalAtoms >= 5 && atomIndex == totalAtoms - 1)
        {
            return LigandRegionTag;
        }

        if (totalAtoms >= 5 && atomIndex < 4)
        {
            return AptamerRegionTag;
        }

        return ScaffoldRegionTag;
    }

    /// <summary>
    /// Exports trajectory simulation data to an Apache Parquet file, validating against path traversal and ensuring invariant float formatting.
    /// </summary>
    /// <param name="sourceTrajectoryPath">The path to the raw source trajectory file.</param>
    /// <param name="outputFilePath">The target Apache Parquet file path.</param>
    /// <param name="parser">The trajectory parser extracting coordinate frames.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total atomic records serialized.</returns>
    public Task<long> ExportToParquetAsync(
        string sourceTrajectoryPath,
        string outputFilePath,
        ITrajectoryParser parser,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Delegates to ConvertTrajectoryToParquetAsync with strict path traversal checking.
        // WHY:  Provides a standardized export API validating outputFilePath against traversal attacks.
        // HOW:  Validates outputFilePath does not contain ".." and passes to ConvertTrajectoryToParquetAsync.
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFilePath);
        if (outputFilePath.Contains(".."))
        {
            throw new ArgumentException("Path traversal segments ('..') are prohibited in Parquet output path.", nameof(outputFilePath));
        }

        return ConvertTrajectoryToParquetAsync(sourceTrajectoryPath, outputFilePath, parser, cancellationToken);
    }

    /// <summary>
    /// Queries the total record count from a Parquet file, validating against path traversal.
    /// </summary>
    /// <param name="parquetFilePath">Path to the Parquet file on disk.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total number of records stored in the Parquet file.</returns>
    public Task<long> QueryParquetAsync(
        string parquetFilePath,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Queries total record count from the specified Parquet file.
        // WHY:  Ensures internal Parquet query operations validate paths and sandboxing.
        // HOW:  Validates path traversal segments and delegates to QueryRecordCountAsync.
        ArgumentException.ThrowIfNullOrWhiteSpace(parquetFilePath);
        if (parquetFilePath.Contains(".."))
        {
            throw new ArgumentException("Path traversal segments ('..') are prohibited in Parquet file path.", nameof(parquetFilePath));
        }

        return QueryRecordCountAsync(parquetFilePath, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<long> WriteFrameBatchAsync(
        string destinationParquetPath,
        long frameIndex,
        ReadOnlyMemory<AtomCoordinate> coordinates,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Serializes a single frame or batch of atomic coordinates directly to Apache Parquet via in-memory DuckDB.
        // WHY:  Enables zero-allocation batch exports without streaming full trajectory files through the pipeline.
        // HOW:  Populates an in-memory DuckDB table with coordinates, applies SIMD distance metrics, and copies to Parquet with ZSTD.
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationParquetPath);

        if (destinationParquetPath.Contains(".."))
        {
            throw new ArgumentException("Path traversal segments ('..') are prohibited in Parquet output path.", nameof(destinationParquetPath));
        }

        string? destinationDir = Path.GetDirectoryName(destinationParquetPath);
        if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        if (File.Exists(destinationParquetPath))
        {
            File.Delete(destinationParquetPath);
        }

        long totalRecordsSerialized = 0;
        using DuckDBConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync(cancellationToken);

        using (DuckDBCommand createTableCmd = connection.CreateCommand())
        {
            createTableCmd.CommandText = @"
                CREATE TABLE staging_trajectory (
                    frame_index BIGINT,
                    atom_index INTEGER,
                    x FLOAT,
                    y FLOAT,
                    z FLOAT,
                    distance_from_center FLOAT,
                    target_region VARCHAR
                );";
            await createTableCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        int atomCount = coordinates.Length;
        if (atomCount > 0)
        {
            using DuckDBAppender appender = connection.CreateAppender("staging_trajectory");
            float[] rentedDistances = ArrayPool<float>.Shared.Rent(atomCount);
            try
            {
                AtomCoordinate centroid = CalculateCentroid(coordinates.Span);
                SimdCoordinateParser.ComputeDistancesFromCentroidSimd(
                    coordinates.Span,
                    centroid,
                    rentedDistances.AsSpan(0, atomCount));

                for (int i = 0; i < atomCount; i++)
                {
                    AtomCoordinate coord = coordinates.Span[i];
                    float dist = rentedDistances[i];
                    string region = ClassifyAtomRegion(i, atomCount);

                    IDuckDBAppenderRow row = appender.CreateRow();
                    row.AppendValue(frameIndex);
                    row.AppendValue(i);
                    row.AppendValue(coord.X);
                    row.AppendValue(coord.Y);
                    row.AppendValue(coord.Z);
                    row.AppendValue(dist);
                    row.AppendValue(region);
                    row.EndRow();

                    totalRecordsSerialized++;
                }
            }
            finally
            {
                ArrayPool<float>.Shared.Return(rentedDistances);
            }
        }

        using (DuckDBCommand copyCommand = connection.CreateCommand())
        {
            string normalizedPath = destinationParquetPath.Replace('\\', '/').Replace("'", "''");
            copyCommand.CommandText = $"COPY staging_trajectory TO '{normalizedPath}' (FORMAT PARQUET, COMPRESSION ZSTD);";
            await copyCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        return totalRecordsSerialized;
    }
}
