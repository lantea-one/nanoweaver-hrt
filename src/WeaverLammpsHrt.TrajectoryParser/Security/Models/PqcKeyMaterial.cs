using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using WeaverLammpsHrt.TrajectoryParser.Swagger;

namespace WeaverLammpsHrt.TrajectoryParser.Security.Models;

/// <summary>
/// Represents serialized public and private cryptographic key material for post-quantum algorithms.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A strongly-typed container holding NIST post-quantum key pairs (e.g. ML-KEM-768 or ML-DSA-65).
/// </para>
/// <para>
/// <strong>Why it exists:</strong> To decouple raw lattice parameter objects from storage serialization and network exchange formats.
/// </para>
/// <para>
/// <strong>How it works:</strong> Stores algorithm names, key identifiers, and Base64-encoded binary key spans.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "PqcKeyMaterial", Description = "Post-quantum cryptographic key material containing public and optional private keys (e.g. ML-KEM-768 or ML-DSA-65).")]
public sealed record PqcKeyMaterial : IExamplesProvider<PqcKeyMaterial>
{
    /// <summary>
    /// Gets the unique identifier for this key pair.
    /// </summary>
    [Description("Unique key identifier or fingerprint.")]
    [JsonPropertyName("keyId")]
    public string KeyId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the formal cryptographic algorithm identifier (e.g. "ML-KEM-768" or "ML-DSA-65").
    /// </summary>
    [Description("Post-quantum cryptographic algorithm identifier (e.g. ML-KEM-768, ML-DSA-65).")]
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Base64-encoded public key bytes.
    /// </summary>
    [Description("Base64-encoded public key bytes.")]
    [JsonPropertyName("publicKey")]
    public string PublicKeyBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Base64-encoded private key bytes, or null if this instance represents a public-only key.
    /// </summary>
    [Description("Base64-encoded private key bytes, or null if this is a public-only key container.")]
    [JsonPropertyName("privateKey")]
    public string? PrivateKeyBase64 { get; init; }

    /// <summary>
    /// Gets the UTC timestamp when this key pair was generated.
    /// </summary>
    [Description("UTC timestamp when this key pair was generated.")]
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Generates a representative example instance of <see cref="PqcKeyMaterial"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated post-quantum key material example.</returns>
    public PqcKeyMaterial GetExamples() => new()
    {
        KeyId = "sub_pqc_key_4a91f",
        Algorithm = "ML-KEM-768",
        PublicKeyBase64 = "k8V...publicKey1184bytes...==",
        PrivateKeyBase64 = null,
        CreatedAtUtc = new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero)
    };
}
