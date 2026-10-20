using System.Text.Json.Serialization;

namespace WeaverLammpsHrt.TrajectoryParser.Compliance;

/// <summary>
/// Represents the output of a HIPAA Safe Harbor (§ 164.514) and GDPR Article 9 pseudonymization process.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A de-identified subject profile stripped of Protected Health Information (PHI).
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Guarantees that raw patient names, medical record numbers, and direct identifiers
/// are never persisted or exposed to simulation pipelines or external peers.
/// </para>
/// <para>
/// <strong>How it works:</strong> Derives an irreversible cryptographic pseudonym (`sub_&lt;sha384_digest&gt;`),
/// strips prohibited direct identifiers, and enforces GINA consent and purpose limitation flags.
/// </para>
/// </remarks>
public sealed record DeIdentificationResult
{
    /// <summary>
    /// Gets the pseudonymized subject identifier (e.g. "sub_3f8a9c...").
    /// </summary>
    [JsonPropertyName("pseudonymSubjectId")]
    public string PseudonymSubjectId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the cleaned, sanitized genomic sequence or SNP payload free of patient identity metadata.
    /// </summary>
    [JsonPropertyName("sanitizedData")]
    public string SanitizedData { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether explicit subject consent was confirmed for GDPR Article 9 special category processing.
    /// </summary>
    [JsonPropertyName("gdprConsentConfirmed")]
    public bool GdprConsentConfirmed { get; init; }

    /// <summary>
    /// Gets a value indicating whether GINA non-discrimination and purpose limitation safeguards were verified.
    /// </summary>
    [JsonPropertyName("ginaSafeguardVerified")]
    public bool GinaSafeguardVerified { get; init; }

    /// <summary>
    /// Gets the list of stripped direct identifier names detected in the input payload.
    /// </summary>
    [JsonPropertyName("strippedIdentifiers")]
    public IReadOnlyList<string> StrippedIdentifiers { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the UTC timestamp when the de-identification operation occurred.
    /// </summary>
    [JsonPropertyName("deIdentifiedAtUtc")]
    public DateTimeOffset DeIdentifiedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
