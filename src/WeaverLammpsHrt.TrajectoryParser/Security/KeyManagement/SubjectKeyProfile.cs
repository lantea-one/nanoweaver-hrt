using System.Text.Json.Serialization;
using WeaverLammpsHrt.TrajectoryParser.Security.Models;

namespace WeaverLammpsHrt.TrajectoryParser.Security.KeyManagement;

/// <summary>
/// Represents the cryptographic key profile for an individual research subject or patient.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A subject-specific key container holding post-quantum key material and erasure status.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Enables per-subject envelope encryption and GDPR Article 17 "Right to Erasure"
/// through cryptographic key destruction (Crypto-Shredding).
/// </para>
/// <para>
/// <strong>How it works:</strong> Stores the subject's ML-KEM-768 key material and tracks whether the key has been shredded.
/// </para>
/// </remarks>
public sealed record SubjectKeyProfile
{
    /// <summary>
    /// Gets the pseudonymized subject identifier (e.g. "sub_a1b2c3d4...").
    /// </summary>
    [JsonPropertyName("subjectId")]
    public string SubjectId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the post-quantum ML-KEM key material assigned to this subject.
    /// </summary>
    [JsonPropertyName("keyMaterial")]
    public PqcKeyMaterial KeyMaterial { get; init; } = new();

    /// <summary>
    /// Gets a value indicating whether this subject's private keys have been permanently destroyed (Crypto-Shredded).
    /// </summary>
    [JsonPropertyName("isShredded")]
    public bool IsShredded { get; init; }

    /// <summary>
    /// Gets the timestamp when this subject was crypto-shredded, or null if active.
    /// </summary>
    [JsonPropertyName("shreddedAtUtc")]
    public DateTimeOffset? ShreddedAtUtc { get; init; }

    /// <summary>
    /// Gets the timestamp when this subject profile was created.
    /// </summary>
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
