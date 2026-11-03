namespace NanoWeaverHrt.Federation;

/// <summary>
/// Cryptographic manifest describing a partitioned molecular trajectory dataset and its digital provenance signature.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A signed manifest detailing all chunk descriptors and cryptographic checksums for a trajectory dataset.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Allows peer nodes in the trajectory federation swarm to verify dataset provenance, detect tampering,
/// and schedule multi-source chunk downloads deterministically.
/// </para>
/// <para>
/// <strong>How it works:</strong> Carries chunk metadata along with the signing node's identifier, signature algorithm, Base64 signature,
/// and UTC timestamp.
/// </para>
/// </remarks>
public sealed record TrajectoryFederationManifest
{
    /// <summary>
    /// Gets the name of the trajectory dataset.
    /// </summary>
    public required string DatasetName { get; init; }

    /// <summary>
    /// Gets the total size of the trajectory dataset in bytes.
    /// </summary>
    public required long TotalSizeBytes { get; init; }

    /// <summary>
    /// Gets the nominal size of each chunk in bytes.
    /// </summary>
    public required int ChunkSizeBytes { get; init; }

    /// <summary>
    /// Gets the total number of chunks comprising the dataset.
    /// </summary>
    public required int TotalChunks { get; init; }

    /// <summary>
    /// Gets the ordered collection of chunk descriptors for this dataset.
    /// </summary>
    public required IReadOnlyList<TrajectoryChunkDescriptor> Chunks { get; init; }

    /// <summary>
    /// Gets the 64-character lowercase hexadecimal SHA-256 fingerprint of the signing node's public key.
    /// </summary>
    public required string SigningNodeId { get; init; }

    /// <summary>
    /// Gets the cryptographic digital signature algorithm used to sign this manifest (e.g., "Ed25519").
    /// </summary>
    public required string SignatureAlgorithm { get; init; }

    /// <summary>
    /// Gets the Base64-encoded digital signature computed over the canonical representation of this manifest.
    /// </summary>
    public required string SignatureBase64 { get; init; }

    /// <summary>
    /// Gets the UTC timestamp when the manifest was generated and signed.
    /// </summary>
    public required DateTimeOffset ManifestTimestampUtc { get; init; }
}
