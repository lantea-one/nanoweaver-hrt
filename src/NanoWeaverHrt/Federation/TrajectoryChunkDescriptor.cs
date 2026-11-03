namespace NanoWeaverHrt.Federation;

/// <summary>
/// Describes a discrete binary chunk of a distributed trajectory dataset, including its byte offset, length, and cryptographic integrity digest.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A descriptor for a 1 MiB (or custom-sized) chunk slice of a molecular dynamics trajectory file.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Enables fine-grained swarm synchronization, parallel chunk distribution, and incremental integrity verification across peer laboratory nodes.
/// </para>
/// <para>
/// <strong>How it works:</strong> Carries the 0-based chunk index, the byte offset and length in the source file, and the lowercase hexadecimal SHA-256 digest of the chunk payload.
/// </para>
/// </remarks>
public sealed record TrajectoryChunkDescriptor
{
    /// <summary>
    /// Gets the zero-based index of this chunk within the trajectory dataset sequence.
    /// </summary>
    public required int ChunkIndex { get; init; }

    /// <summary>
    /// Gets the byte offset where this chunk begins in the source trajectory file.
    /// </summary>
    public required long OffsetBytes { get; init; }

    /// <summary>
    /// Gets the length of this chunk in bytes.
    /// </summary>
    public required int LengthBytes { get; init; }

    /// <summary>
    /// Gets the lowercase 64-character hexadecimal SHA-256 digest of this chunk's payload.
    /// </summary>
    public required string Sha256Checksum { get; init; }
}
