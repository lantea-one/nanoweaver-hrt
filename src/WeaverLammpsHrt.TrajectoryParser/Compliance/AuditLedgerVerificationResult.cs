using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using WeaverLammpsHrt.TrajectoryParser.Swagger;

namespace WeaverLammpsHrt.TrajectoryParser.Compliance;

/// <summary>
/// Represents the result of a cryptographic verification audit across the complete FDA 21 CFR Part 11 audit ledger.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The verification report validating hash-chain integrity and post-quantum digital signatures.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> To mathematically prove that no audit records have been modified, deleted, inserted, or reordered.
/// </para>
/// <para>
/// <strong>How it works:</strong> Traverses the entire audit log sequence, verifying SHA-384 hash links and ML-DSA-65 signatures.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "AuditLedgerVerificationResult", Description = "Cryptographic verification result covering hash-chain integrity and post-quantum digital signatures across the FDA 21 CFR Part 11 audit ledger.")]
public sealed record AuditLedgerVerificationResult : IExamplesProvider<AuditLedgerVerificationResult>
{
    /// <summary>
    /// Gets a value indicating whether every record in the ledger passed hash-chain and digital signature verification.
    /// </summary>
    [Description("Indicates whether every record in the ledger passed hash-chain and digital signature verification.")]
    [JsonPropertyName("isValid")]
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets the total number of audit records verified in the ledger.
    /// </summary>
    [Description("Total number of audit records verified in the ledger.")]
    [JsonPropertyName("totalRecordsVerified")]
    public int TotalRecordsVerified { get; init; }

    /// <summary>
    /// Gets the sequence number of the first invalid record detected, or null if all records are valid.
    /// </summary>
    [Description("Sequence number of the first detected tampering violation or signature mismatch, or null if all records are valid.")]
    [JsonPropertyName("firstViolationSequence")]
    public long? FirstViolationSequence { get; init; }

    /// <summary>
    /// Gets a plain-English message describing the verification status or failure reason.
    /// </summary>
    [Description("Plain-English description of verification outcome.")]
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp when the audit verification was executed.
    /// </summary>
    [Description("UTC timestamp when the ledger verification completed.")]
    [JsonPropertyName("verifiedAtUtc")]
    public DateTimeOffset VerifiedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Generates a representative example instance of <see cref="AuditLedgerVerificationResult"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated audit ledger verification report example.</returns>
    public AuditLedgerVerificationResult GetExamples() => new()
    {
        IsValid = true,
        TotalRecordsVerified = 142,
        FirstViolationSequence = null,
        Message = "Audit ledger cryptographically verified: 142 records intact, zero hash-chain or signature violations detected.",
        VerifiedAtUtc = new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero)
    };
}
