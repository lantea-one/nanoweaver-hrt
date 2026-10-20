using System.Text.Json.Serialization;

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
public sealed record PqcKeyMaterial
{
    /// <summary>
    /// Gets the unique identifier for this key pair.
    /// </summary>
    [JsonPropertyName("keyId")]
    public string KeyId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the formal cryptographic algorithm identifier (e.g. "ML-KEM-768" or "ML-DSA-65").
    /// </summary>
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Base64-encoded public key bytes.
    /// </summary>
    [JsonPropertyName("publicKey")]
    public string PublicKeyBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Base64-encoded private key bytes, or null if this instance represents a public-only key.
    /// </summary>
    [JsonPropertyName("privateKey")]
    public string? PrivateKeyBase64 { get; init; }

    /// <summary>
    /// Gets the UTC timestamp when this key pair was generated.
    /// </summary>
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
