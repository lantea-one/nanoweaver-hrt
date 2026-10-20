using System.Text.Json.Serialization;

namespace WeaverLammpsHrt.TrajectoryParser.Security.Models;

/// <summary>
/// Represents an authenticated, post-quantum envelope-encrypted payload.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A hybrid post-quantum encrypted container wrapping sensitive health or trajectory records at rest.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Combines NIST FIPS 203 (ML-KEM-768) key encapsulation with high-speed symmetric AES-256-GCM encryption
/// to achieve Grover-resistant quantum security without large asymmetric encryption overhead on bulk data.
/// </para>
/// <para>
/// <strong>How it works:</strong> Stores the encapsulated shared secret ciphertext, AES-GCM ciphertext, 96-bit initialization vector (nonce),
/// and 128-bit authentication tag.
/// </para>
/// </remarks>
public sealed record EncryptedEnvelope
{
    /// <summary>
    /// Gets the envelope encryption algorithm suite (default: "ML-KEM-768+AES-256-GCM").
    /// </summary>
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; init; } = "ML-KEM-768+AES-256-GCM";

    /// <summary>
    /// Gets the identifier of the key or subject used to encrypt this envelope.
    /// </summary>
    [JsonPropertyName("keyId")]
    public string KeyId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Base64-encoded ML-KEM encapsulation ciphertext containing the wrapped 256-bit symmetric key.
    /// </summary>
    [JsonPropertyName("encapsulatedKey")]
    public string EncapsulatedKeyBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Base64-encoded AES-256-GCM encrypted payload ciphertext.
    /// </summary>
    [JsonPropertyName("ciphertext")]
    public string CiphertextBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Base64-encoded 96-bit initialization vector / nonce used for AES-GCM.
    /// </summary>
    [JsonPropertyName("nonce")]
    public string NonceBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Base64-encoded 128-bit authentication tag validating ciphertext integrity.
    /// </summary>
    [JsonPropertyName("tag")]
    public string TagBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp when this envelope was sealed.
    /// </summary>
    [JsonPropertyName("sealedAtUtc")]
    public DateTimeOffset SealedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
