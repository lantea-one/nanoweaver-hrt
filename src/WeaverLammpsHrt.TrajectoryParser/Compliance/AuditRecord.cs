using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using WeaverLammpsHrt.TrajectoryParser.Security.Models;
using WeaverLammpsHrt.TrajectoryParser.Swagger;

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
[SwaggerSchema(Title = "AuditRecord", Description = "Immutable, tamper-evident audit log entry compliant with FDA 21 CFR Part 11 and HIPAA Security Rule.")]
public sealed record AuditRecord : IExamplesProvider<AuditRecord>
{
    /// <summary>
    /// Gets the monotonically increasing sequence number of this audit record.
    /// </summary>
    [Description("Monotonically increasing sequence number.")]
    [JsonPropertyName("sequenceNumber")]
    public long SequenceNumber { get; init; }

    /// <summary>
    /// Gets the precise UTC timestamp when this audit record was committed.
    /// </summary>
    [Description("Precise UTC timestamp when the record was committed.")]
    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the SHA-384 hash of the previous audit record, or 64 zeros for the genesis record.
    /// </summary>
    [Description("SHA-384 hash of the preceding record for blockchain-style hash chaining.")]
    [JsonPropertyName("previousHash")]
    public string PreviousHash { get; init; } = string.Empty;

    /// <summary>
    /// Gets the SHA-384 digest of this record's contents (sequence, timestamp, previousHash, operator, action, resource, details).
    /// </summary>
    [Description("SHA-384 hash digest of this record's contents.")]
    [JsonPropertyName("currentHash")]
    public string CurrentHash { get; init; } = string.Empty;

    /// <summary>
    /// Gets the pseudonymized operator or service identity that executed the audited action.
    /// </summary>
    [Description("Pseudonymized operator or service identity.")]
    [JsonPropertyName("operatorId")]
    public string OperatorId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the functional action type (e.g. "IngestScaffold", "ScreenGenomics", "RecalibrateKinetics", "CryptoShredSubject").
    /// </summary>
    [Description("Functional action type executed.")]
    [JsonPropertyName("action")]
    public string Action { get; init; } = string.Empty;

    /// <summary>
    /// Gets the resource or entity affected by this action.
    /// </summary>
    [Description("Resource or entity affected by this action.")]
    [JsonPropertyName("resource")]
    public string Resource { get; init; } = string.Empty;

    /// <summary>
    /// Gets serialized JSON details containing action parameters, compliance flags, or metric deltas.
    /// </summary>
    [Description("Serialized JSON details containing action parameters or metadata.")]
    [JsonPropertyName("detailsJson")]
    public string DetailsJson { get; init; } = "{}";

    /// <summary>
    /// Gets the NIST FIPS 204 ML-DSA-65 post-quantum digital signature authenticating this record.
    /// </summary>
    [Description("NIST FIPS 204 ML-DSA-65 post-quantum digital signature.")]
    [JsonPropertyName("signature")]
    public PqcSignature Signature { get; init; } = new();

    /// <summary>
    /// Generates a representative example instance of <see cref="AuditRecord"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated immutable audit record example.</returns>
    public AuditRecord GetExamples() => new()
    {
        SequenceNumber = 142,
        TimestampUtc = new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero),
        PreviousHash = "a1b2c3d4e5f60718293a4b5c6d7e8f9a0b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e",
        CurrentHash = "f6e5d4c3b2a10987654321fedcba0987654321abcdef0123456789abcdef0123",
        OperatorId = "sub_operator_compliance_officer_01",
        Action = "ScreenGenomics",
        Resource = "PatientGenomicProfile:sub_patient_8849c",
        DetailsJson = "{\"markersDetected\":[\"F5_LEIDEN\"],\"thrombosisMultiplier\":4.5}",
        Signature = new PqcSignature().GetExamples()
    };
}
