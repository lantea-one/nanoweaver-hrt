using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Federation;

/// <summary>
/// Request payload for building a P2P federation manifest for a trajectory file.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A strongly-typed request containing target trajectory filename, peer node ID, and signing key material.
/// </para>
/// <para>
/// <strong>Why this design:</strong> Enables verifiable P2P trajectory distribution across sovereign research nodes.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "FederationBuildManifestRequest", Description = "Request payload for building a P2P federation manifest.")]
public sealed record FederationBuildManifestRequest : IExamplesProvider<FederationBuildManifestRequest>
{
    /// <summary>
    /// Target trajectory filename within shared data storage.
    /// </summary>
    [Description("Target trajectory filename within shared data storage.")]
    [JsonPropertyName("trajectoryFileName")]
    public string TrajectoryFileName { get; init; } = string.Empty;

    /// <summary>
    /// Identifier of the publishing peer node.
    /// </summary>
    [Description("Identifier of the publishing peer node.")]
    [JsonPropertyName("nodeId")]
    public string NodeId { get; init; } = string.Empty;

    /// <summary>
    /// Base64-encoded private key for manifest signature generation.
    /// </summary>
    [Description("Base64-encoded private key for manifest signature generation.")]
    [JsonPropertyName("privateKeyBase64")]
    public string PrivateKeyBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Cryptographic signature algorithm (default Ed25519).
    /// </summary>
    [Description("Cryptographic signature algorithm (default Ed25519).")]
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; init; } = "Ed25519";

    /// <summary>
    /// Generates a representative example instance of <see cref="FederationBuildManifestRequest"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated request example.</returns>
    public FederationBuildManifestRequest GetExamples() => new()
    {
        TrajectoryFileName = "dna_nanotube_sample.xyz",
        NodeId = "node-alpha-01",
        PrivateKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        Algorithm = "Ed25519"
    };
}
