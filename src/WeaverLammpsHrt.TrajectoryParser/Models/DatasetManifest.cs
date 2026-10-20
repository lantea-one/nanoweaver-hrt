using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;

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
[SwaggerSchema(Title = "DatasetManifest", Description = "Cryptographic provenance manifest recording SHA-256 hashes, timestamps, and files within an archive.")]
public sealed record DatasetManifest
{
    /// <summary>
    /// The unique name or identifier of the dataset (e.g., "dna-origami-tile-step500k").
    /// </summary>
    [Description("The unique name or identifier of the dataset.")]
    [JsonPropertyName("dataset")]
    public required string DatasetName { get; init; }

    /// <summary>
    /// The ISO 8601 UTC timestamp recording when this archive was generated.
    /// </summary>
    [Description("The ISO 8601 UTC timestamp recording when this archive was generated.")]
    [JsonPropertyName("timestamp")]
    public required string TimestampUtc { get; init; }

    /// <summary>
    /// The filename of the compressed archive (e.g., "dna-origami-tile.tar.gz").
    /// </summary>
    [Description("The filename of the compressed archive.")]
    [JsonPropertyName("archive")]
    public required string ArchiveFileName { get; init; }

    /// <summary>
    /// The cryptographic SHA-256 hash (in lowercase hexadecimal) computed across the entire archive.
    /// </summary>
    [Description("The cryptographic SHA-256 hash (in lowercase hexadecimal) computed across the entire archive.")]
    [JsonPropertyName("sha256")]
    public required string Sha256Checksum { get; init; }

    /// <summary>
    /// The total size of the archive in bytes.
    /// </summary>
    [Description("The total size of the archive in bytes.")]
    [JsonPropertyName("size_bytes")]
    public required long SizeInBytes { get; init; }

    /// <summary>
    /// Optional list of trajectory and configuration files packaged inside this archive.
    /// </summary>
    [Description("List of trajectory and configuration files packaged inside this archive.")]
    [JsonPropertyName("included_files")]
    public string[] IncludedFiles { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Represents metadata describing a trajectory file discovered in the simulation storage volume.
/// </summary>
[SwaggerSchema(Title = "TrajectoryFileItem", Description = "Metadata describing an oxDNA, LAMMPS, or CHARMM trajectory file found on disk.")]
public sealed record TrajectoryFileItem
{
    /// <summary>
    /// The file name including its extension (e.g., "folding_run.xyz" or "traj.dcd").
    /// </summary>
    [Description("The file name including its extension.")]
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    [Description("File size in bytes.")]
    [JsonPropertyName("size_bytes")]
    public required long SizeBytes { get; init; }

    /// <summary>
    /// Human-readable file size (e.g., "14.2 MB", "1.8 GB").
    /// </summary>
    [Description("Human-readable file size (e.g., '14.2 MB').")]
    [JsonPropertyName("size_formatted")]
    public required string SizeFormatted { get; init; }

    /// <summary>
    /// Last modified timestamp in ISO 8601 UTC.
    /// </summary>
    [Description("Last modified timestamp in ISO 8601 UTC.")]
    [JsonPropertyName("last_modified")]
    public required string LastModifiedUtc { get; init; }

    /// <summary>
    /// Detected trajectory format (e.g., "XYZ", "DCD", "oxDNA").
    /// </summary>
    [Description("Detected trajectory format (e.g., 'XYZ', 'DCD', 'LAMMPS Custom').")]
    [JsonPropertyName("format")]
    public required string Format { get; init; }
}
