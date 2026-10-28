using System.Text.Json.Serialization;

namespace NanoWeaverHrt.Compliance;

/// <summary>
/// Classifies the outcome of FDA 21 CFR Part 11 audit ledger verification, separating structural tampering
/// from unverifiable legacy or foreign signing keys and failed cryptographic signature checks.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuditLedgerViolationKind
{
    /// <summary>
    /// Every audit record in the ledger passed sequence, hash-chain, payload digest, and ML-DSA-65 signature verification.
    /// </summary>
    None,

    /// <summary>
    /// An audit record's sequence number did not match its zero-based index in the ledger.
    /// </summary>
    SequenceDiscontinuity,

    /// <summary>
    /// An audit record's <c>PreviousHash</c> did not match the preceding record's <c>CurrentHash</c>.
    /// </summary>
    BrokenHashLink,

    /// <summary>
    /// Recomputing an audit record's SHA-384 digest over its fields did not match its stored <c>CurrentHash</c>.
    /// </summary>
    TamperedPayload,

    /// <summary>
    /// The record carries a signature from the pre-persistence ephemeral key identifier
    /// (<see cref="ComplianceAuditService.LegacyEphemeralSigningKeyId"/>), whose public key was never stored, so the
    /// signature can never be verified. This is not evidence of tampering.
    /// </summary>
    LegacyEphemeralSigningKey,

    /// <summary>
    /// The record carries a <c>SignerKeyId</c> that is not this node's current audit signing key, so its signature
    /// cannot be verified here.
    /// </summary>
    UnknownSigningKey,

    /// <summary>
    /// The record names this node's current audit signing key and failed ML-DSA-65 signature verification against that key.
    /// </summary>
    InvalidSignature
}
