using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Compliance;

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
[SwaggerSchema(Title = "DeIdentificationResult", Description = "Output of HIPAA Safe Harbor (§ 164.514) and GDPR Article 9 pseudonymization process.")]
public sealed record DeIdentificationResult : IExamplesProvider<DeIdentificationResult>
{
    /// <summary>
    /// Gets the pseudonymized subject identifier (e.g. "sub_3f8a9c...").
    /// </summary>
    [Description("Cryptographically derived pseudonymized subject identifier (e.g. sub_3f8a9c...).")]
    [JsonPropertyName("pseudonymSubjectId")]
    public string PseudonymSubjectId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the cleaned, sanitized genomic sequence or SNP payload free of patient identity metadata.
    /// </summary>
    [Description("Sanitized genomic or analytical data stripped of direct patient identifiers.")]
    [JsonPropertyName("sanitizedData")]
    public string SanitizedData { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether explicit subject consent was confirmed for GDPR Article 9 special category processing.
    /// </summary>
    [Description("Indicates whether explicit subject consent was confirmed for GDPR Article 9 processing.")]
    [JsonPropertyName("gdprConsentConfirmed")]
    public bool GdprConsentConfirmed { get; init; }

    /// <summary>
    /// Gets a value indicating whether GINA non-discrimination and purpose limitation safeguards were verified.
    /// </summary>
    [Description("Indicates whether GINA non-discrimination and purpose limitation safeguards were verified.")]
    [JsonPropertyName("ginaSafeguardVerified")]
    public bool GinaSafeguardVerified { get; init; }

    /// <summary>
    /// Gets the list of stripped direct identifier names detected in the input payload.
    /// </summary>
    [Description("Collection of prohibited direct identifiers detected and removed from the payload.")]
    [JsonPropertyName("strippedIdentifiers")]
    public IReadOnlyList<string> StrippedIdentifiers { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the UTC timestamp when the de-identification operation occurred.
    /// </summary>
    [Description("UTC timestamp when the de-identification operation was executed.")]
    [JsonPropertyName("deIdentifiedAtUtc")]
    public DateTimeOffset DeIdentifiedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Generates a representative example instance of <see cref="DeIdentificationResult"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated de-identification result example.</returns>
    public DeIdentificationResult GetExamples() => new()
    {
        PseudonymSubjectId = "sub_9e4a1b5c2d7f8e3a0b",
        SanitizedData = "F5_LEIDEN,CYP2C19_POOR,TLR9_HYPORESPONSIVE",
        GdprConsentConfirmed = true,
        GinaSafeguardVerified = true,
        StrippedIdentifiers = ["Name", "MRN", "DateOfBirth", "PostalCode"],
        DeIdentifiedAtUtc = new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero)
    };
}
