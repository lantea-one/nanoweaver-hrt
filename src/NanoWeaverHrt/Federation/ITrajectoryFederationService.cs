namespace NanoWeaverHrt.Federation;

/// <summary>
/// Defines the peer-to-peer trajectory federation and sovereign peer synchronization service contract.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The central service contract for trajectory dataset chunking, manifest signing, integrity verification, and swarm peer discovery.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Enables sovereign laboratories to distribute multi-gigabyte molecular trajectory files across a decentralized P2P swarm with cryptographic provenance and chunk-level integrity checks.
/// </para>
/// <para>
/// <strong>How it works:</strong> Partitions files into deterministic chunks, computes SHA-256 digests, signs manifests via asymmetric cryptography, validates chunk checksums in constant time, and tracks active peers.
/// </para>
/// </remarks>
public interface ITrajectoryFederationService
{
    /// <summary>
    /// Builds and digitally signs a trajectory federation manifest for a specified local trajectory dataset file.
    /// </summary>
    /// <param name="trajectoryFileName">The filename or path of the trajectory dataset to partition and manifest.</param>
    /// <param name="chunkSizeBytes">The size of each chunk slice in bytes; defaults to 1,048,576 bytes (1 MiB).</param>
    /// <param name="ct">Cancellation token for aborting the operation.</param>
    /// <returns>A signed <see cref="TrajectoryFederationManifest"/> describing all chunk slices and cryptographic signatures.</returns>
    Task<TrajectoryFederationManifest> BuildManifestAsync(string trajectoryFileName, int chunkSizeBytes = 1048576, CancellationToken ct = default);

    /// <summary>
    /// Verifies the cryptographic signature and integrity of a trajectory federation manifest against trusted node and peer keys.
    /// </summary>
    /// <param name="manifest">The federation manifest to verify.</param>
    /// <param name="ct">Cancellation token for aborting the operation.</param>
    /// <returns><c>true</c> if the manifest's signature is authentic and originates from this node or a trusted active peer; otherwise, <c>false</c>.</returns>
    Task<bool> VerifyManifestAsync(TrajectoryFederationManifest manifest, CancellationToken ct = default);

    /// <summary>
    /// Reads a discrete chunk slice of a trajectory file from disk.
    /// </summary>
    /// <param name="trajectoryFileName">The filename or path of the trajectory dataset.</param>
    /// <param name="chunkIndex">The zero-based chunk index to read.</param>
    /// <param name="chunkSizeBytes">The size of each chunk slice in bytes; defaults to 1,048,576 bytes (1 MiB).</param>
    /// <param name="ct">Cancellation token for aborting the operation.</param>
    /// <returns>A byte array containing the chunk's binary payload.</returns>
    Task<byte[]> ReadChunkAsync(string trajectoryFileName, int chunkIndex, int chunkSizeBytes = 1048576, CancellationToken ct = default);

    /// <summary>
    /// Validates the binary payload of a chunk against an expected SHA-256 checksum in constant time.
    /// </summary>
    /// <param name="chunkBytes">The raw binary payload of the chunk.</param>
    /// <param name="expectedSha256">The expected lowercase hexadecimal SHA-256 checksum.</param>
    /// <param name="ct">Cancellation token for aborting the operation.</param>
    /// <returns><c>true</c> if the payload matches the expected checksum in constant time; otherwise, <c>false</c>.</returns>
    Task<bool> ValidateChunkAsync(ReadOnlyMemory<byte> chunkBytes, string expectedSha256, CancellationToken ct = default);

    /// <summary>
    /// Discovers active, trusted peer nodes participating in the trajectory federation swarm.
    /// </summary>
    /// <param name="ct">Cancellation token for aborting the operation.</param>
    /// <returns>A read-only collection of active <see cref="FederationPeerNode"/> instances.</returns>
    Task<IReadOnlyList<FederationPeerNode>> DiscoverPeersAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads a slice of a trajectory file directly into caller-provided memory with zero heap allocations.
    /// </summary>
    /// <param name="trajectoryFileName">The simple file name of the trajectory dataset to read.</param>
    /// <param name="offsetBytes">The non-negative byte offset within the file to begin reading from.</param>
    /// <param name="destination">The destination memory buffer where trajectory bytes will be written.</param>
    /// <param name="ct">Cancellation token for aborting the operation.</param>
    /// <returns>The total number of bytes read into <paramref name="destination"/>.</returns>
    Task<int> ReadChunkIntoAsync(string trajectoryFileName, long offsetBytes, Memory<byte> destination, CancellationToken ct = default);
}
