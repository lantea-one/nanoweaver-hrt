namespace NanoWeaverHrt.Services;

/// <summary>
/// Classifies the provenance and integrity outcome of a dataset import operation.
/// </summary>
public enum DatasetImportOutcome
{
    /// <summary>
    /// The manifest was unsigned, strict mode was off, and the archive passed integrity verification and was extracted (HTTP 200).
    /// </summary>
    ImportedUnsigned,

    /// <summary>
    /// The manifest carried a valid signature from a trusted key and the archive passed integrity verification and was extracted (HTTP 200).
    /// </summary>
    ImportedSigned,

    /// <summary>
    /// The archive's SHA-256 digest or byte length did not match the values recorded in the manifest (HTTP 400).
    /// </summary>
    IntegrityMismatch,

    /// <summary>
    /// The manifest signature block was malformed, or the cryptographic signature did not verify over the canonical manifest bytes (HTTP 400).
    /// </summary>
    SignatureInvalid,

    /// <summary>
    /// The manifest had a well-formed signature block whose key identifier is neither this node's own key nor an active peer key in <c>weaver_peer_keys</c> (HTTP 403).
    /// </summary>
    SignerNotTrusted,

    /// <summary>
    /// The manifest was unsigned while <c>Peer:RequireSignedDatasets</c> was enabled (or set to an unparseable value, which fails closed) (HTTP 403).
    /// </summary>
    UnsignedRejectedByPolicy
}

/// <summary>
/// Result of evaluating dataset provenance and archive integrity in <see cref="VerifiedDatasetImporter"/>.
/// </summary>
/// <param name="Outcome">The terminal import outcome.</param>
/// <param name="SignerKeyId">The 64-character lowercase hex key identifier of the verified signer when <paramref name="Outcome"/> is <see cref="DatasetImportOutcome.ImportedSigned"/>; otherwise <c>null</c>.</param>
public sealed record DatasetImportResult(DatasetImportOutcome Outcome, string? SignerKeyId);
