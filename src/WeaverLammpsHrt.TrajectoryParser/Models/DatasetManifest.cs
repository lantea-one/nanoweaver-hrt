using System.Text.Json.Serialization;

namespace WeaverLammpsHrt.TrajectoryParser.Models;

/// <summary>
/// Cryptographic provenance manifest for exported or imported molecular simulation datasets.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A machine-readable certificate that records the cryptographic signature, 
/// origin timestamp, file size, and contents of a simulation trajectory bundle.
/// </para>
/// <para>
/// <strong>Why this design:</strong> In decentralized and open-source science, peer laboratories must be able to 
/// verify that simulation datasets have not been corrupted in transit, truncated, or tampered with.
/// </para>
/// </remarks>
public sealed record DatasetManifest
{
    /// <summary>
    /// The unique name or identifier of the dataset (e.g., "dna-origami-tile-step500k").
    /// </summary>
    [JsonPropertyName("dataset")]
    public required string DatasetName { get; init; }

    /// <summary>
    /// The ISO 8601 UTC timestamp recording when this archive was generated.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public required string TimestampUtc { get; init; }

    /// <summary>
    /// The filename of the compressed archive (e.g., "dna-origami-tile.tar.gz").
    /// </summary>
    [JsonPropertyName("archive")]
    public required string ArchiveFileName { get; init; }

    /// <summary>
    /// The cryptographic SHA-256 hash (in lowercase hexadecimal) computed across the entire archive.
    /// </summary>
    [JsonPropertyName("sha256")]
    public required string Sha256Checksum { get; init; }

    /// <summary>
    /// The total size of the archive in bytes.
    /// </summary>
    [JsonPropertyName("size_bytes")]
    public required long SizeInBytes { get; init; }

    /// <summary>
    /// Optional list of trajectory and configuration files packaged inside this archive.
    /// </summary>
    [JsonPropertyName("included_files")]
    public string[] IncludedFiles { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Represents metadata describing a trajectory file discovered in the simulation storage volume.
/// </summary>
public sealed record TrajectoryFileItem
{
    /// <summary>
    /// The file name including its extension (e.g., "folding_run.xyz" or "traj.dcd").
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    [JsonPropertyName("size_bytes")]
    public required long SizeBytes { get; init; }

    /// <summary>
    /// Human-readable file size (e.g., "14.2 MB", "1.8 GB").
    /// </summary>
    [JsonPropertyName("size_formatted")]
    public required string SizeFormatted { get; init; }

    /// <summary>
    /// Last modified timestamp in ISO 8601 UTC.
    /// </summary>
    [JsonPropertyName("last_modified")]
    public required string LastModifiedUtc { get; init; }

    /// <summary>
    /// Detected trajectory format (e.g., "XYZ", "DCD", "oxDNA").
    /// </summary>
    [JsonPropertyName("format")]
    public required string Format { get; init; }
}
