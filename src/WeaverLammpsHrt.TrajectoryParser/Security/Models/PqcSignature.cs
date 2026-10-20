using System.Text.Json.Serialization;

namespace WeaverLammpsHrt.TrajectoryParser.Security.Models;

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
public sealed record PqcSignature
{
    /// <summary>
    /// Gets the signature algorithm (default: "ML-DSA-65").
    /// </summary>
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; init; } = "ML-DSA-65";

    /// <summary>
    /// Gets the key identifier of the authorized signing authority.
    /// </summary>
    [JsonPropertyName("signerKeyId")]
    public string SignerKeyId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Base64-encoded digital signature bytes.
    /// </summary>
    [JsonPropertyName("signature")]
    public string SignatureBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp when this signature was generated.
    /// </summary>
    [JsonPropertyName("signedAtUtc")]
    public DateTimeOffset SignedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
