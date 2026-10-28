using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Cryptographic provenance manifest for exported or imported molecular simulation datasets.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A machine-readable provenance certificate that records the archive SHA-256 digest,
/// dataset identifier, archive file name, byte size, origin UTC timestamp, packaged file list, and optional
/// detached digital signature metadata for a simulation trajectory bundle.
/// </para>
/// <para>
/// <strong>Why this design:</strong> In decentralized and open-source science, peer laboratories must be able to
/// verify both that simulation datasets were not corrupted in transit (via the SHA-256 archive digest) and that the
/// manifest itself was cryptographically signed by a trusted peer node over a deterministic length-prefixed binary
/// encoding of every manifest field except the signature value itself.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "DatasetManifest", Description = "Cryptographic provenance manifest recording SHA-256 hashes, timestamps, and files within an archive.")]
public sealed record DatasetManifest : IExamplesProvider<DatasetManifest>
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
    [JsonPropertyName("sizeBytes")]
    public required long SizeInBytes { get; init; }

    /// <summary>
    /// Optional list of trajectory and configuration files packaged inside this archive.
    /// </summary>
    [Description("List of trajectory and configuration files packaged inside this archive.")]
    [JsonPropertyName("includedFiles")]
    public string[] IncludedFiles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Schema version of the manifest (<c>null</c> for legacy unsigned v1 manifests; <c>2</c> for signed manifests).
    /// </summary>
    [Description("Schema version of the manifest (null for legacy unsigned v1 manifests; 2 for signed manifests).")]
    [JsonPropertyName("manifestVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ManifestVersion { get; init; }

    /// <summary>
    /// Digital signature algorithm identifier (<c>"Ed25519"</c> or <c>"MlDsa65"</c>) when the manifest is signed.
    /// </summary>
    [Description("Digital signature algorithm identifier ('Ed25519' or 'MlDsa65') when the manifest is signed.")]
    [JsonPropertyName("signatureAlgorithm")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SignatureAlgorithm { get; init; }

    /// <summary>
    /// Lowercase 64-character hexadecimal SHA-256 fingerprint of the signer's SubjectPublicKeyInfo DER public key.
    /// </summary>
    [Description("Lowercase 64-character hexadecimal SHA-256 fingerprint of the signer's SubjectPublicKeyInfo DER public key.")]
    [JsonPropertyName("signerKeyId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SignerKeyId { get; init; }

    /// <summary>
    /// Standard padded Base64 encoding of the raw digital signature over the canonical manifest bytes.
    /// </summary>
    [Description("Standard padded Base64 encoding of the raw digital signature over the canonical manifest bytes.")]
    [JsonPropertyName("signature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SignatureBase64 { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="DatasetManifest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated dataset manifest example.</returns>
    public DatasetManifest GetExamples() => new()
    {
        DatasetName = "dna-origami-tile-500k",
        TimestampUtc = "2026-10-20T18:00:00Z",
        ArchiveFileName = "dna-origami-tile-500k.tar.gz",
        Sha256Checksum = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
        SizeInBytes = 14850200,
        IncludedFiles = ["trajectory.xyz", "simulation.in", "manifest.json"]
    };
}

/// <summary>
/// Represents metadata describing a trajectory file discovered in the simulation storage volume.
/// </summary>
[SwaggerSchema(Title = "TrajectoryFileItem", Description = "Metadata describing an oxDNA, LAMMPS, or CHARMM trajectory file found on disk.")]
public sealed record TrajectoryFileItem : IExamplesProvider<TrajectoryFileItem>
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
    [JsonPropertyName("sizeBytes")]
    public required long SizeBytes { get; init; }

    /// <summary>
    /// Human-readable file size (e.g., "14.2 MB", "1.8 GB").
    /// </summary>
    [Description("Human-readable file size (e.g., '14.2 MB').")]
    [JsonPropertyName("sizeFormatted")]
    public required string SizeFormatted { get; init; }

    /// <summary>
    /// Last modified timestamp in ISO 8601 UTC.
    /// </summary>
    [Description("Last modified timestamp in ISO 8601 UTC.")]
    [JsonPropertyName("lastModified")]
    public required string LastModifiedUtc { get; init; }

    /// <summary>
    /// Detected trajectory format (e.g., "XYZ", "DCD", "oxDNA").
    /// </summary>
    [Description("Detected trajectory format (e.g., 'XYZ', 'DCD', 'LAMMPS Custom').")]
    [JsonPropertyName("format")]
    public required string Format { get; init; }

    /// <summary>
    /// Relative web API URL for downloading the raw trajectory file.
    /// </summary>
    [Description("Relative web API URL for downloading the raw trajectory file.")]
    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl => $"/api/trajectories/download/{Uri.EscapeDataString(Name)}";

    /// <summary>
    /// Generates a representative example instance of <see cref="TrajectoryFileItem"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated trajectory file metadata example.</returns>
    public TrajectoryFileItem GetExamples() => new()
    {
        Name = "dna_nanotube_folding.xyz",
        SizeBytes = 15728640,
        SizeFormatted = "15.0 MB",
        LastModifiedUtc = "2026-10-20T18:30:00Z",
        Format = "XYZ"
    };
}

/// <summary>
/// Metadata describing an exported dataset bundle, manifest, or cryptographic signature stored on disk.
/// </summary>
[SwaggerSchema(Title = "ExportFileItem", Description = "Metadata describing an exported simulation bundle archive or cryptographic manifest.")]
public sealed record ExportFileItem : IExamplesProvider<ExportFileItem>
{
    /// <summary>
    /// The filename of the export artifact.
    /// </summary>
    [Description("The filename of the export artifact.")]
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Total file size in bytes.
    /// </summary>
    [Description("Total file size in bytes.")]
    [JsonPropertyName("sizeBytes")]
    public required long SizeBytes { get; init; }

    /// <summary>
    /// Formatted human-readable file size (e.g., '4.2 MB', '120 KB').
    /// </summary>
    [Description("Formatted human-readable file size.")]
    [JsonPropertyName("sizeFormatted")]
    public required string SizeFormatted { get; init; }

    /// <summary>
    /// Last modified timestamp in ISO 8601 UTC.
    /// </summary>
    [Description("Last modified timestamp in ISO 8601 UTC.")]
    [JsonPropertyName("lastModified")]
    public required string LastModifiedUtc { get; init; }

    /// <summary>
    /// Artifact categorization (e.g., 'Archive', 'Manifest', 'PQC Signature', 'Analytical Parquet').
    /// </summary>
    [Description("Artifact categorization (Archive, Manifest, PQC Signature, or Parquet).")]
    [JsonPropertyName("category")]
    public required string Category { get; init; }

    /// <summary>
    /// Relative web download endpoint URL.
    /// </summary>
    [Description("Relative web download endpoint URL.")]
    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl => $"/api/datasets/download/{Uri.EscapeDataString(Name)}";

    /// <summary>
    /// Generates a representative example instance of <see cref="ExportFileItem"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated export file metadata example.</returns>
    public ExportFileItem GetExamples() => new()
    {
        Name = "dna-origami-nanocarrier-001.tar.gz",
        SizeBytes = 4398120,
        SizeFormatted = "4.2 MB",
        LastModifiedUtc = "2026-10-21T18:00:00Z",
        Category = "Archive"
    };
}
