using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Abstractions;

/// <summary>
/// Service contract managing decentralized dataset discovery, packaging, cryptographic verification, and export/import.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The central coordinator for sharing molecular simulation datasets across sovereign nodes.
/// </para>
/// <para>
/// <strong>Why this design:</strong> Ensures that all dataset exports carry immutable SHA-256 signatures, 
/// allowing decentralized peer laboratories to exchange DNA origami simulation results with cryptographic certainty.
/// </para>
/// </remarks>
public interface IDatasetManager
{
    /// <summary>
    /// Lists all molecular trajectory and configuration files currently present in the shared simulation volume.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to abort the file scan.</param>
    /// <returns>A collection of metadata descriptors for each discovered trajectory file.</returns>
    Task<IReadOnlyList<TrajectoryFileItem>> ListTrajectoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Packages specified simulation files into a compressed .tar.gz archive and produces a signed manifest.
    /// </summary>
    /// <param name="datasetName">A unique identifier name for the dataset bundle.</param>
    /// <param name="fileNames">Names of the files to include. If empty or null, all trajectory files are included.</param>
    /// <param name="cancellationToken">Cancellation token to abort archive generation.</param>
    /// <returns>The cryptographic provenance manifest describing the newly generated archive.</returns>
    Task<DatasetManifest> ExportDatasetAsync(string datasetName, IEnumerable<string>? fileNames = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the SHA-256 hash of an uploaded archive against its manifest and unpacks it into the active data volume.
    /// </summary>
    /// <param name="archiveStream">The readable stream containing the .tar.gz archive bytes.</param>
    /// <param name="manifest">The cryptographic provenance manifest accompanying the archive.</param>
    /// <param name="cancellationToken">Cancellation token to abort the import.</param>
    /// <returns>True if the SHA-256 checksum matched and files were successfully extracted; otherwise false.</returns>
    Task<bool> ImportDatasetAsync(Stream archiveStream, DatasetManifest manifest, CancellationToken cancellationToken = default);
}
