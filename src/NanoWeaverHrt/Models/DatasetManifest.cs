using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using NanoWeaverHrt.Swagger;
using NanoWeaverHrt.Endocrine;
using NanoWeaverHrt.Genomics;

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

/// <summary>
/// Server response payload following cryptographic manifest verification and dataset archive import.
/// </summary>
[SwaggerSchema(Title = "DatasetImportResponse", Description = "Server response payload following cryptographic manifest verification and dataset archive import.")]
public sealed record DatasetImportResponse : IExamplesProvider<DatasetImportResponse>
{
    /// <summary>
    /// Informational message describing import verification and extraction status.
    /// </summary>
    [Description("Informational message describing import verification and extraction status.")]
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Cryptographic provenance manifest extracted from the imported archive.
    /// </summary>
    [Description("Cryptographic provenance manifest extracted from the imported archive.")]
    [JsonPropertyName("manifest")]
    public DatasetManifest? Manifest { get; init; }

    /// <summary>
    /// Cryptographic provenance classification (signed or unsigned).
    /// </summary>
    [Description("Cryptographic provenance classification (signed or unsigned).")]
    [JsonPropertyName("provenance")]
    public string Provenance { get; init; } = string.Empty;

    /// <summary>
    /// Signer public key identifier when the imported manifest was digitally signed; otherwise null.
    /// </summary>
    [Description("Signer public key identifier when the imported manifest was digitally signed; otherwise null.")]
    [JsonPropertyName("signerKeyId")]
    public string? SignerKeyId { get; init; }

    /// <summary>
    /// Generates an illustrative example instance of <see cref="DatasetImportResponse"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated dataset import response example.</returns>
    public DatasetImportResponse GetExamples() => new()
    {
        Message = "Dataset successfully verified and imported.",
        Manifest = new()
        {
            DatasetName = "dna-origami-tile-500k",
            TimestampUtc = "2026-10-20T18:00:00Z",
            ArchiveFileName = "dna-origami-tile-500k.tar.gz",
            Sha256Checksum = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            SizeInBytes = 14850200,
            IncludedFiles = ["trajectory.xyz", "simulation.in", "manifest.json"]
        },
        Provenance = "signed",
        SignerKeyId = "ed25519_pk_0123456789abcdef0123456789abcdef"
    };
}

/// <summary>
/// Result metadata for an envelope-encrypted dataset archive export.
/// </summary>
[SwaggerSchema(Title = "ExportEncryptedDatasetResult", Description = "Result metadata for an envelope-encrypted dataset archive export.")]
public sealed record ExportEncryptedDatasetResult : IExamplesProvider<ExportEncryptedDatasetResult>
{
    /// <summary>
    /// File system path where the encrypted dataset bundle was saved.
    /// </summary>
    [Description("File system path where the encrypted dataset bundle was saved.")]
    [JsonPropertyName("bundle_path")]
    public string BundlePath { get; init; } = string.Empty;

    /// <summary>
    /// Cryptographic algorithm suite used for envelope encryption and manifest signing.
    /// </summary>
    [Description("Cryptographic algorithm suite used for envelope encryption and manifest signing.")]
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; init; } = string.Empty;

    /// <summary>
    /// Subject identifier whose post-quantum public key was used for key encapsulation.
    /// </summary>
    [Description("Subject identifier whose post-quantum public key was used for key encapsulation.")]
    [JsonPropertyName("subject_id")]
    public string SubjectId { get; init; } = string.Empty;

    /// <summary>
    /// Informational message describing export completion.
    /// </summary>
    [Description("Informational message describing export completion.")]
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Generates an illustrative example instance of <see cref="ExportEncryptedDatasetResult"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated encrypted export result example.</returns>
    public ExportEncryptedDatasetResult GetExamples() => new()
    {
        BundlePath = "/simulations/exports/dna-origami-tile-step500k.enc.tar.gz",
        Algorithm = "ML-KEM-768+AES-256-GCM",
        SubjectId = "sub_patient_42",
        Message = "Encrypted dataset bundle packaged and signed with ML-DSA-65."
    };
}

/// <summary>
/// Generated simulation input deck script configured for the specified campaign.
/// </summary>
[SwaggerSchema(Title = "SimulationInputDeckResult", Description = "Generated simulation input deck script configured for the specified campaign.")]
public sealed record SimulationInputDeckResult : IExamplesProvider<SimulationInputDeckResult>
{
    /// <summary>
    /// Identifier of the simulation campaign.
    /// </summary>
    [Description("Identifier of the simulation campaign.")]
    [JsonPropertyName("campaign_id")]
    public string CampaignId { get; init; } = string.Empty;

    /// <summary>
    /// Complete simulation input deck script content.
    /// </summary>
    [Description("Complete simulation input deck script content.")]
    [JsonPropertyName("input_deck")]
    public string InputDeck { get; init; } = string.Empty;

    /// <summary>
    /// Generates an illustrative example instance of <see cref="SimulationInputDeckResult"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated simulation input deck result example.</returns>
    public SimulationInputDeckResult GetExamples() => new()
    {
        CampaignId = "CAMPAIGN_SERUM_STABILITY",
        InputDeck = "# oxDNA 2.4 Simulation Input Deck\nsteps = 1000000\ntemp = 37C"
    };
}

/// <summary>
/// Zero-allocation trajectory parser performance and frame metrics.
/// </summary>
[SwaggerSchema(Title = "TrajectoryParserProcessResult", Description = "Zero-allocation trajectory parser performance and frame metrics.")]
public sealed record TrajectoryParserProcessResult : IExamplesProvider<TrajectoryParserProcessResult>
{
    /// <summary>
    /// Name of the processed trajectory file.
    /// </summary>
    [Description("Name of the processed trajectory file.")]
    [JsonPropertyName("fileName")]
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// Detected or specified trajectory file format.
    /// </summary>
    [Description("Detected or specified trajectory file format.")]
    [JsonPropertyName("format")]
    public string Format { get; init; } = string.Empty;

    /// <summary>
    /// Total coordinate frames parsed from the trajectory stream.
    /// </summary>
    [Description("Total coordinate frames parsed from the trajectory stream.")]
    [JsonPropertyName("framesParsed")]
    public long FramesParsed { get; init; }

    /// <summary>
    /// Total cumulative atomic coordinates evaluated across all parsed frames.
    /// </summary>
    [Description("Total cumulative atomic coordinates evaluated across all parsed frames.")]
    [JsonPropertyName("totalAtomsProcessed")]
    public long TotalAtomsProcessed { get; init; }

    /// <summary>
    /// Total elapsed time in milliseconds for stream processing.
    /// </summary>
    [Description("Total elapsed time in milliseconds for stream processing.")]
    [JsonPropertyName("elapsedMilliseconds")]
    public long ElapsedMilliseconds { get; init; }

    /// <summary>
    /// Calculated parsing throughput in frames per second.
    /// </summary>
    [Description("Calculated parsing throughput in frames per second.")]
    [JsonPropertyName("framesPerSecond")]
    public double FramesPerSecond { get; init; }

    /// <summary>
    /// Generates an illustrative example instance of <see cref="TrajectoryParserProcessResult"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated trajectory parser result example.</returns>
    public TrajectoryParserProcessResult GetExamples() => new()
    {
        FileName = "nanocarrier-production.xyz",
        Format = "XYZ",
        FramesParsed = 10000,
        TotalAtomsProcessed = 8400000,
        ElapsedMilliseconds = 142,
        FramesPerSecond = 70422.5
    };
}

/// <summary>
/// Result metadata for columnar Apache Parquet coordinate serialization.
/// </summary>
[SwaggerSchema(Title = "TrajectoryParquetExportResult", Description = "Result metadata for columnar Apache Parquet coordinate serialization.")]
public sealed record TrajectoryParquetExportResult : IExamplesProvider<TrajectoryParquetExportResult>
{
    /// <summary>
    /// Source trajectory file name.
    /// </summary>
    [Description("Source trajectory file name.")]
    [JsonPropertyName("source_file")]
    public string SourceFile { get; init; } = string.Empty;

    /// <summary>
    /// Output Apache Parquet columnar file name.
    /// </summary>
    [Description("Output Apache Parquet columnar file name.")]
    [JsonPropertyName("parquet_file")]
    public string ParquetFile { get; init; } = string.Empty;

    /// <summary>
    /// Detected or specified trajectory format.
    /// </summary>
    [Description("Detected or specified trajectory format.")]
    [JsonPropertyName("format")]
    public string Format { get; init; } = string.Empty;

    /// <summary>
    /// Total coordinate records serialized into columnar Parquet batches.
    /// </summary>
    [Description("Total coordinate records serialized into columnar Parquet batches.")]
    [JsonPropertyName("total_records_serialized")]
    public long TotalRecordsSerialized { get; init; }

    /// <summary>
    /// File size in bytes of the generated Parquet file on disk.
    /// </summary>
    [Description("File size in bytes of the generated Parquet file on disk.")]
    [JsonPropertyName("file_size_bytes")]
    public long FileSizeBytes { get; init; }

    /// <summary>
    /// Elapsed time in milliseconds for Parquet serialization.
    /// </summary>
    [Description("Elapsed time in milliseconds for Parquet serialization.")]
    [JsonPropertyName("elapsed_milliseconds")]
    public long ElapsedMilliseconds { get; init; }

    /// <summary>
    /// Generates an illustrative example instance of <see cref="TrajectoryParquetExportResult"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated parquet export result example.</returns>
    public TrajectoryParquetExportResult GetExamples() => new()
    {
        SourceFile = "nanocarrier-production.xyz",
        ParquetFile = "nanocarrier-production.parquet",
        Format = "XYZ",
        TotalRecordsSerialized = 8400000,
        FileSizeBytes = 12582912,
        ElapsedMilliseconds = 310
    };
}

/// <summary>
/// Result payload of applying patient pharmacogenomic adjustments to the endocrine setpoint profile.
/// </summary>
[SwaggerSchema(Title = "GenomicAdjustmentResult", Description = "Result payload of applying patient pharmacogenomic adjustments to the endocrine setpoint profile.")]
public sealed record GenomicAdjustmentResult : IExamplesProvider<GenomicAdjustmentResult>
{
    /// <summary>
    /// Evaluated patient pharmacogenomic profile.
    /// </summary>
    [Description("Evaluated patient pharmacogenomic profile.")]
    [JsonPropertyName("genomic_profile")]
    public PatientGenomicProfile? GenomicProfile { get; init; }

    /// <summary>
    /// Adjusted synthetic endocrine target setpoint profile.
    /// </summary>
    [Description("Adjusted synthetic endocrine target setpoint profile.")]
    [JsonPropertyName("adjusted_endocrine_profile")]
    public EndocrineTargetProfile? AdjustedEndocrineProfile { get; init; }

    /// <summary>
    /// Generates an illustrative example instance of <see cref="GenomicAdjustmentResult"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated genomic adjustment result example.</returns>
    public GenomicAdjustmentResult GetExamples() => new()
    {
        GenomicProfile = new()
        {
            PatientId = "sub_patient_42c8e",
            Regimen = TherapeuticRegimen.MaleToFemale,
            ObservedMarkers = [GeneticMarker.CreateExample()],
            Alerts = [GeneticAlert.CreateExample()],
            ThrombosisRiskIndex = 4.5,
            OverallClearanceMultiplier = 1.0,
            CpGMethylationMandatory = false,
            RecommendedMaxEstradiolPmolL = 450.0,
            RecommendedMinTestosteroneNmolL = null,
            RecommendedKdOffsetNanoMolar = 0.0,
            CalculatedAtUtc = "2026-10-20T18:00:00Z"
        },
        AdjustedEndocrineProfile = new()
        {
            PatientId = "sub_patient_8849c",
            Regimen = TherapeuticRegimen.MaleToFemale,
            TargetState = ToggleState.EstrogenicDominant,
            TargetTissue = TargetTissueType.AnteriorPituitaryGonadotrope,
            TargetEstradiolPmolL = 550.0,
            TargetTestosteroneNmolL = 0.95,
            TargetProgesteroneNmolL = 2.4,
            BindingAffinityKdTargetNanoMolar = 2.5,
            DeliveryVehicle = NanocarrierDeliveryVehicle.Default,
            UpdatedUtc = "2026-10-20T18:00:00Z"
        }
    };
}
