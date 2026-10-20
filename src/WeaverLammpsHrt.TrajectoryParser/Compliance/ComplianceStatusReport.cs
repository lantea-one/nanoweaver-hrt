using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using WeaverLammpsHrt.TrajectoryParser.Swagger;

namespace WeaverLammpsHrt.TrajectoryParser.Compliance;

/// <summary>
/// Provides a comprehensive regulatory compliance posture assessment covering HIPAA, GDPR, GINA, and FDA 21 CFR Part 11.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> An auditable health data compliance scorecard and technical security posture report.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> To give researchers, institutional review boards (IRBs), and external regulatory auditors
/// instant mathematical proof of data protection at rest, post-quantum readiness, and audit integrity.
/// </para>
/// <para>
/// <strong>How it works:</strong> Inspects active cryptographic suites, queries key management states, evaluates
/// audit ledger hash-chain integrity, and verifies privacy enforcement flags.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "ComplianceStatusReport", Description = "Comprehensive regulatory compliance posture assessment covering HIPAA, GDPR, GINA, and FDA 21 CFR Part 11.")]
public sealed record ComplianceStatusReport : IExamplesProvider<ComplianceStatusReport>
{
    /// <summary>
    /// Gets the primary Post-Quantum Encryption at Rest algorithm suite (e.g. "ML-KEM-768 + AES-256-GCM (NIST FIPS 203)").
    /// </summary>
    [Description("Post-quantum encryption at rest algorithm suite (e.g. ML-KEM-768 + AES-256-GCM).")]
    [JsonPropertyName("encryptionAtRestAlgorithm")]
    public string EncryptionAtRestAlgorithm { get; init; } = "ML-KEM-768 + AES-256-GCM (NIST FIPS 203)";

    /// <summary>
    /// Gets the digital signature algorithm suite for immutable audit logs (e.g. "ML-DSA-65 (NIST FIPS 204)").
    /// </summary>
    [Description("Digital signature algorithm suite for immutable audit logs (e.g. ML-DSA-65).")]
    [JsonPropertyName("auditSignatureAlgorithm")]
    public string AuditSignatureAlgorithm { get; init; } = "ML-DSA-65 (NIST FIPS 204)";

    /// <summary>
    /// Gets a value indicating whether encryption at rest is strictly active across all databases, profiles, and export bundles.
    /// </summary>
    [Description("Indicates whether post-quantum encryption at rest is enforced across all datastores.")]
    [JsonPropertyName("isEncryptionAtRestActive")]
    public bool IsEncryptionAtRestActive { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the FDA 21 CFR Part 11 audit ledger has verified unbroken hash-chain integrity.
    /// </summary>
    [Description("Indicates whether the FDA 21 CFR Part 11 audit ledger has verified unbroken hash-chain integrity.")]
    [JsonPropertyName("isAuditLedgerIntact")]
    public bool IsAuditLedgerIntact { get; init; }

    /// <summary>
    /// Gets the total number of committed, signed audit records in the ledger.
    /// </summary>
    [Description("Total number of committed, signed audit records in the ledger.")]
    [JsonPropertyName("auditRecordCount")]
    public int AuditRecordCount { get; init; }

    /// <summary>
    /// Gets a value indicating whether HIPAA Safe Harbor (§ 164.514) de-identification and ePHI field-level encryption are enforced.
    /// </summary>
    [Description("Indicates whether HIPAA Safe Harbor de-identification and field encryption are enforced.")]
    [JsonPropertyName("hipaaSafeHarborEnforced")]
    public bool HipaaSafeHarborEnforced { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether GDPR Article 9 special category consent and Article 17 crypto-shredding are active.
    /// </summary>
    [Description("Indicates whether GDPR Article 9 special category consent and Article 17 crypto-shredding are active.")]
    [JsonPropertyName("gdprArticle9And17Active")]
    public bool GdprArticle9And17Active { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether GINA genetic non-discrimination and purpose limitation safeguards are verified.
    /// </summary>
    [Description("Indicates whether GINA genetic non-discrimination and purpose limitation safeguards are verified.")]
    [JsonPropertyName("ginaNonDiscriminationVerified")]
    public bool GinaNonDiscriminationVerified { get; init; } = true;

    /// <summary>
    /// Gets the total number of registered subject key profiles.
    /// </summary>
    [Description("Total number of registered subject key profiles.")]
    [JsonPropertyName("totalSubjectsRegistered")]
    public int TotalSubjectsRegistered { get; init; }

    /// <summary>
    /// Gets the count of subjects who have executed GDPR Article 17 Crypto-Shredding.
    /// </summary>
    [Description("Count of subjects who have executed GDPR Article 17 cryptographic erasure.")]
    [JsonPropertyName("cryptoShreddedSubjectCount")]
    public int CryptoShreddedSubjectCount { get; init; }

    /// <summary>
    /// Gets the overall compliance verification status: "Compliant", "Degraded", or "Non-Compliant".
    /// </summary>
    [Description("Overall regulatory posture: Compliant, Degraded, or Non-Compliant.")]
    [JsonPropertyName("overallStatus")]
    public string OverallStatus { get; init; } = "Compliant";

    /// <summary>
    /// Gets the UTC timestamp when this compliance report was generated.
    /// </summary>
    [Description("UTC timestamp when this compliance report was generated.")]
    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Generates a representative example instance of <see cref="ComplianceStatusReport"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated compliance status report example.</returns>
    public ComplianceStatusReport GetExamples() => new()
    {
        EncryptionAtRestAlgorithm = "ML-KEM-768 + AES-256-GCM (NIST FIPS 203)",
        AuditSignatureAlgorithm = "ML-DSA-65 (NIST FIPS 204)",
        IsEncryptionAtRestActive = true,
        IsAuditLedgerIntact = true,
        AuditRecordCount = 142,
        HipaaSafeHarborEnforced = true,
        GdprArticle9And17Active = true,
        GinaNonDiscriminationVerified = true,
        TotalSubjectsRegistered = 18,
        CryptoShreddedSubjectCount = 2,
        OverallStatus = "Compliant",
        GeneratedAtUtc = new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero)
    };
}
