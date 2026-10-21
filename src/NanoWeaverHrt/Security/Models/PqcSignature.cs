using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Security.Models;

/// <summary>
/// Represents a post-quantum digital signature providing tamper-evident authenticity and non-repudiation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A signature container produced by NIST FIPS 204 (ML-DSA-65).
/// </para>
/// <para>
/// <strong>Why it exists:</strong> To guarantee FDA 21 CFR Part 11 compliance and cryptographic manifest provenance
/// against both classical and quantum computing adversaries.
/// </para>
/// <para>
/// <strong>How it works:</strong> Stores the Base64 signature bytes, the signer's public key identifier, and generation timestamp.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "PqcSignature", Description = "Post-quantum digital signature container providing tamper-evident authenticity and non-repudiation (NIST FIPS 204 ML-DSA-65).")]
public sealed record PqcSignature : IExamplesProvider<PqcSignature>
{
    /// <summary>
    /// Gets the signature algorithm (default: "ML-DSA-65").
    /// </summary>
    [Description("Signature algorithm (e.g. ML-DSA-65).")]
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; init; } = "ML-DSA-65";

    /// <summary>
    /// Gets the key identifier of the authorized signing authority.
    /// </summary>
    [Description("Key identifier of the authorized signing authority.")]
    [JsonPropertyName("signerKeyId")]
    public string SignerKeyId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Base64-encoded digital signature bytes.
    /// </summary>
    [Description("Base64-encoded digital signature bytes.")]
    [JsonPropertyName("signature")]
    public string SignatureBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp when this signature was generated.
    /// </summary>
    [Description("UTC timestamp when this signature was generated.")]
    [JsonPropertyName("signedAtUtc")]
    public DateTimeOffset SignedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Generates a representative example instance of <see cref="PqcSignature"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated post-quantum digital signature example.</returns>
    public PqcSignature GetExamples() => new()
    {
        Algorithm = "ML-DSA-65",
        SignerKeyId = "weaver_node_master_authority",
        SignatureBase64 = "u92L...ml_dsa_65_signature_3309_bytes...==",
        SignedAtUtc = new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero)
    };
}
