using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Federation;

/// <summary>
/// Response payload returned after verifying a P2P federation manifest.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A verification result summary indicating validity, trajectory ID, chunk count, and total size.
/// </para>
/// <para>
/// <strong>Why this design:</strong> Provides receiving peers with cryptographic assurance and structural metrics before downloading chunks.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "FederationVerifyManifestResponse", Description = "Response payload returned after verifying a P2P federation manifest.")]
public sealed record FederationVerifyManifestResponse : IExamplesProvider<FederationVerifyManifestResponse>
{
    /// <summary>
    /// True if the manifest signature and SHA-256 integrity checks pass; otherwise false.
    /// </summary>
    [Description("True if the manifest signature and SHA-256 integrity checks pass; otherwise false.")]
    [JsonPropertyName("isValid")]
    public bool IsValid { get; init; }

    /// <summary>
    /// Unique trajectory identifier referenced in the manifest.
    /// </summary>
    [Description("Unique trajectory identifier referenced in the manifest.")]
    [JsonPropertyName("trajectoryId")]
    public string TrajectoryId { get; init; } = string.Empty;

    /// <summary>
    /// Total number of verified chunks comprising the trajectory archive.
    /// </summary>
    [Description("Total number of verified chunks comprising the trajectory archive.")]
    [JsonPropertyName("chunkCount")]
    public int ChunkCount { get; init; }

    /// <summary>
    /// Total uncompressed or compressed size of the dataset in bytes.
    /// </summary>
    [Description("Total size of the dataset in bytes.")]
    [JsonPropertyName("totalSizeBytes")]
    public long TotalSizeBytes { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="FederationVerifyManifestResponse"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated response example.</returns>
    public FederationVerifyManifestResponse GetExamples() => new()
    {
        IsValid = true,
        TrajectoryId = "traj-a1b2c3d4",
        ChunkCount = 4,
        TotalSizeBytes = 1048576
    };
}
