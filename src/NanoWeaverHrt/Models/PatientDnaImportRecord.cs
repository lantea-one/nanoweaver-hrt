using System.ComponentModel;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Persistent record representing imported DNA sequence data, variant panels, or trajectory datasets
/// associated with a specific patient in the sovereign HRT platform.
/// </summary>
public sealed record PatientDnaImportRecord : IExamplesProvider<PatientDnaImportRecord>
{
    /// <summary>
    /// Gets or initializes the unique identifier for this import record.
    /// </summary>
    [Description("Unique identifier for this import record.")]
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the username of the patient associated with this import.
    /// </summary>
    [Description("Username of the patient associated with this import.")]
    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the original file name of the imported asset.
    /// </summary>
    [Description("Original file name of the imported asset.")]
    [JsonPropertyName("fileName")]
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the categorization type of the import (e.g. FASTA, GenomicPanel, TrajectoryArchive, AptamerData).
    /// </summary>
    [Description("Categorization type of the import (e.g. FASTA, GenomicPanel, TrajectoryArchive, AptamerData).")]
    [JsonPropertyName("importType")]
    public string ImportType { get; init; } = "FASTA";

    /// <summary>
    /// Gets or initializes the raw nucleotide sequence text, JSON variant manifest, or metadata payload.
    /// </summary>
    [Description("Raw nucleotide sequence text, JSON variant manifest, or metadata payload.")]
    [JsonPropertyName("sequenceOrData")]
    public string SequenceOrData { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the file size of the imported asset in bytes.
    /// </summary>
    [Description("File size in bytes.")]
    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }

    /// <summary>
    /// Gets or initializes the cryptographic SHA-256 hash of the imported payload.
    /// </summary>
    [Description("Cryptographic SHA-256 hash of the imported payload.")]
    [JsonPropertyName("sha256Checksum")]
    public string Sha256Checksum { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the username or system service that performed the import.
    /// </summary>
    [Description("Username or system service that performed the import.")]
    [JsonPropertyName("importedBy")]
    public string ImportedBy { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the processing status of the import (e.g. Processed, Verified, Pending).
    /// </summary>
    [Description("Processing status of the import (e.g. Processed, Verified, Pending).")]
    [JsonPropertyName("status")]
    public string Status { get; init; } = "Processed";

    /// <summary>
    /// Gets or initializes optional serialized JSON metadata accompanying the import.
    /// </summary>
    [Description("Serialized JSON metadata accompanying the import.")]
    [JsonPropertyName("metadataJson")]
    public string MetadataJson { get; init; } = "{}";

    /// <summary>
    /// Gets or initializes the UTC timestamp when the import occurred.
    /// </summary>
    [Description("UTC import timestamp.")]
    [JsonPropertyName("importedAtUtc")]
    public DateTimeOffset ImportedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Generates a representative example instance of <see cref="PatientDnaImportRecord"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A sample populated patient DNA import record.</returns>
    public PatientDnaImportRecord GetExamples() => new()
    {
        Id = "imp_example_01",
        Username = "patient_alice",
        FileName = "m13mp18_custom_scaffold.fasta",
        ImportType = "FASTA",
        SequenceOrData = ">CustomScaffold\nAAGCTCAGAG...",
        SizeBytes = 7249,
        Sha256Checksum = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
        ImportedBy = "researcher_bob",
        Status = "Processed",
        MetadataJson = "{\"cpgCount\":12,\"gcPercent\":42.5}",
        ImportedAtUtc = DateTimeOffset.UtcNow
    };
}
