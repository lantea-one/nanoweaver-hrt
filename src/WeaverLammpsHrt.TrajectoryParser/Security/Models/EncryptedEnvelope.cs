using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using WeaverLammpsHrt.TrajectoryParser.Swagger;

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
[SwaggerSchema(Title = "EncryptedEnvelope", Description = "Authenticated post-quantum envelope-encrypted payload combining NIST ML-KEM-768 and AES-256-GCM.")]
public sealed record EncryptedEnvelope : IExamplesProvider<EncryptedEnvelope>
{
    /// <summary>
    /// Gets the envelope encryption algorithm suite (default: "ML-KEM-768+AES-256-GCM").
    /// </summary>
    [Description("Envelope encryption algorithm suite (e.g. ML-KEM-768+AES-256-GCM).")]
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; init; } = "ML-KEM-768+AES-256-GCM";

    /// <summary>
    /// Gets the identifier of the key or subject used to encrypt this envelope.
    /// </summary>
    [Description("Identifier of the subject key or authority used to seal this envelope.")]
    [JsonPropertyName("keyId")]
    public string KeyId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Base64-encoded ML-KEM encapsulation ciphertext containing the wrapped 256-bit symmetric key.
    /// </summary>
    [Description("Base64-encoded ML-KEM-768 encapsulation ciphertext wrapping the symmetric key.")]
    [JsonPropertyName("encapsulatedKey")]
    public string EncapsulatedKeyBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Base64-encoded AES-256-GCM encrypted payload ciphertext.
    /// </summary>
    [Description("Base64-encoded AES-256-GCM encrypted payload ciphertext.")]
    [JsonPropertyName("ciphertext")]
    public string CiphertextBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Base64-encoded 96-bit initialization vector / nonce used for AES-GCM.
    /// </summary>
    [Description("Base64-encoded 96-bit initialization vector/nonce used for AES-GCM.")]
    [JsonPropertyName("nonce")]
    public string NonceBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Base64-encoded 128-bit authentication tag validating ciphertext integrity.
    /// </summary>
    [Description("Base64-encoded 128-bit authentication tag validating ciphertext integrity.")]
    [JsonPropertyName("tag")]
    public string TagBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp when this envelope was sealed.
    /// </summary>
    [Description("UTC timestamp when this envelope was sealed.")]
    [JsonPropertyName("sealedAtUtc")]
    public DateTimeOffset SealedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Generates a representative example instance of <see cref="EncryptedEnvelope"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated post-quantum encrypted envelope example.</returns>
    public EncryptedEnvelope GetExamples() => new()
    {
        Algorithm = "ML-KEM-768+AES-256-GCM",
        KeyId = "sub_pqc_key_4a91f",
        EncapsulatedKeyBase64 = "g37p1v9K...example...==",
        CiphertextBase64 = "xK92mZ...sealed_record...==",
        NonceBase64 = "MDEyMzQ1Njc4OWFi",
        TagBase64 = "QUJDREVGR0hJSktMTU5PUA==",
        SealedAtUtc = new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero)
    };
}
