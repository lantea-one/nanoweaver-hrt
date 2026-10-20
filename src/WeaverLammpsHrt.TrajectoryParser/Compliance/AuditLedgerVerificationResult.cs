using System.Text.Json.Serialization;

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
public sealed record AuditLedgerVerificationResult
{
    /// <summary>
    /// Gets a value indicating whether every record in the ledger passed hash-chain and digital signature verification.
    /// </summary>
    [JsonPropertyName("isValid")]
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets the total number of audit records verified in the ledger.
    /// </summary>
    [JsonPropertyName("totalRecordsVerified")]
    public int TotalRecordsVerified { get; init; }

    /// <summary>
    /// Gets the sequence number of the first invalid record detected, or null if all records are valid.
    /// </summary>
    [JsonPropertyName("firstViolationSequence")]
    public long? FirstViolationSequence { get; init; }

    /// <summary>
    /// Gets a plain-English message describing the verification status or failure reason.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp when the audit verification was executed.
    /// </summary>
    [JsonPropertyName("verifiedAtUtc")]
    public DateTimeOffset VerifiedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
