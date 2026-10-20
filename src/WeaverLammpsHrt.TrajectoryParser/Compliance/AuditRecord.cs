using System.Text.Json.Serialization;
using WeaverLammpsHrt.TrajectoryParser.Security.Models;

namespace WeaverLammpsHrt.TrajectoryParser.Compliance;

/// <summary>
/// Represents an immutable, tamper-evident audit log entry compliant with FDA 21 CFR Part 11 and HIPAA Security Rule.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A cryptographically chained audit ledger record.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Mandated by FDA 21 CFR Part 11 (§ 11.10(e)) and HIPAA (§ 164.312(b)) to create
/// an independent, computer-generated, time-stamped audit trail that records user actions.
/// </para>
/// <para>
/// <strong>How it works:</strong> Each record includes the SHA-384 hash of the preceding record (hash chaining)
/// and is digitally signed by the node authority using NIST FIPS 204 (ML-DSA-65).
/// </para>
/// </remarks>
public sealed record AuditRecord
{
    /// <summary>
    /// Gets the monotonically increasing sequence number of this audit record.
    /// </summary>
    [JsonPropertyName("sequenceNumber")]
    public long SequenceNumber { get; init; }

    /// <summary>
    /// Gets the precise UTC timestamp when this audit record was committed.
    /// </summary>
    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the SHA-384 hash of the previous audit record, or 64 zeros for the genesis record.
    /// </summary>
    [JsonPropertyName("previousHash")]
    public string PreviousHash { get; init; } = string.Empty;

    /// <summary>
    /// Gets the SHA-384 digest of this record's contents (sequence, timestamp, previousHash, operator, action, resource, details).
    /// </summary>
    [JsonPropertyName("currentHash")]
    public string CurrentHash { get; init; } = string.Empty;

    /// <summary>
    /// Gets the pseudonymized operator or service identity that executed the audited action.
    /// </summary>
    [JsonPropertyName("operatorId")]
    public string OperatorId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the functional action type (e.g. "IngestScaffold", "ScreenGenomics", "RecalibrateKinetics", "CryptoShredSubject").
    /// </summary>
    [JsonPropertyName("action")]
    public string Action { get; init; } = string.Empty;

    /// <summary>
    /// Gets the resource or entity affected by this action.
    /// </summary>
    [JsonPropertyName("resource")]
    public string Resource { get; init; } = string.Empty;

    /// <summary>
    /// Gets serialized JSON details containing action parameters, compliance flags, or metric deltas.
    /// </summary>
    [JsonPropertyName("detailsJson")]
    public string DetailsJson { get; init; } = "{}";

    /// <summary>
    /// Gets the NIST FIPS 204 ML-DSA-65 post-quantum digital signature authenticating this record.
    /// </summary>
    [JsonPropertyName("signature")]
    public PqcSignature Signature { get; init; } = new();
}
