using System.Buffers;
using System.IO.Pipelines;
using DuckDB.NET.Data;
using WeaverLammpsHrt.TrajectoryParser.Abstractions;
using WeaverLammpsHrt.TrajectoryParser.Models;
using WeaverLammpsHrt.TrajectoryParser.Pipelines.Simd;

namespace WeaverLammpsHrt.TrajectoryParser.Storage;

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
            // Normalize path separators to forward slashes for DuckDB SQL syntax compatibility
            string normalizedPath = destinationParquetPath.Replace('\\', '/');
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

        if (!File.Exists(parquetFilePath))
        {
            throw new FileNotFoundException("Target Parquet file does not exist.", parquetFilePath);
        }

        using DuckDBConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync(cancellationToken);

        using DuckDBCommand queryCmd = connection.CreateCommand();
        string normalizedPath = parquetFilePath.Replace('\\', '/');
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
}
