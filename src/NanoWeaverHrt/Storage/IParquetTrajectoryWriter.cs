using NanoWeaverHrt.Abstractions;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Defines a contract for streaming molecular simulation coordinates and serializing them into columnar Apache Parquet files.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is Apache Parquet:</strong> Parquet is an open-source, columnar storage format optimized for analytical 
/// queries. Unlike row-based formats (like .xyz text or relational tables), Parquet stores each coordinate component 
/// (X, Y, Z, distance) in separate columnar chunks with dictionary encoding and compression (e.g. ZSTD or Snappy).
/// </para>
/// <para>
/// <strong>Biomedical Analytics Context:</strong> When analyzing hormone toggling kinetics across millions of timesteps, 
/// researchers often query specific subsets of data (e.g., "Find all frames where the ligand is within 5.0 Angstroms 
/// of the aptamer binding pocket"). Reading columnar Parquet via <c>pg_duckdb</c> skips unneeded columns and reads 
/// data orders of magnitude faster than parsing text files.
/// </para>
/// </remarks>
public interface IParquetTrajectoryWriter
{
    /// <summary>
    /// Streams a molecular trajectory from disk, processes coordinates via SIMD vectorization, and writes 
    /// a compressed Apache Parquet file using DuckDB.
    /// </summary>
    /// <param name="sourceTrajectoryPath">The absolute or relative file path to the raw trajectory (.xyz, .dcd, .lammpstrj).</param>
    /// <param name="destinationParquetPath">The target file path where the .parquet file will be created.</param>
    /// <param name="parser">The streaming trajectory parser implementation to extract frames.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, returning the total number of atomic records serialized.</returns>
    Task<long> ConvertTrajectoryToParquetAsync(
        string sourceTrajectoryPath,
        string destinationParquetPath,
        ITrajectoryParser parser,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries the total number of atom coordinate records stored inside a Parquet file using DuckDB's vectorized reader.
    /// </summary>
    /// <param name="parquetFilePath">The path to the .parquet file on disk.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>The total row count of the Parquet file.</returns>
    Task<long> QueryRecordCountAsync(
        string parquetFilePath,
        CancellationToken cancellationToken = default);
}
