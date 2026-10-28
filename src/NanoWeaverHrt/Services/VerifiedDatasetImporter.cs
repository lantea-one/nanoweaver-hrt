using NanoWeaverHrt.Abstractions;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Signing;
using NanoWeaverHrt.Storage;

namespace NanoWeaverHrt.Services;

/// <summary>
/// Enforces cryptographic provenance and strict-mode trust policy on incoming dataset manifests before delegating
/// archive integrity verification and extraction to <see cref="IDatasetManager.ImportDatasetAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The single enforcement gate for <c>POST /api/datasets/import</c> that authenticates
/// who produced a dataset bundle before reading a single byte of the uploaded archive stream.
/// </para>
/// <para>
/// <strong>Why it exists (and the strict-mode-off downgrade property):</strong> With strict mode off (the default,
/// <c>Peer:RequireSignedDatasets = false</c>), signing tells you who sent a bundle, and any bundle carrying a
/// forged signature or a signature from an untrusted key is rejected. However, while strict mode is off, signing
/// does not protect you from an uploader who simply omits the signature fields altogether: an unsigned bundle is
/// still accepted so existing unsigned bundles and the quickstart workflow continue to work out of the box. Turning
/// on <c>Peer:RequireSignedDatasets</c> closes that downgrade path so only bundles signed by this node or an active
/// peer key in <c>weaver_peer_keys</c> are accepted.
/// </para>
/// <para>
/// <strong>How it works:</strong> Classifies the manifest signature state first, checks strict mode for unsigned
/// manifests or resolves the signer's public key (from this node's own <see cref="INodeDatasetSigner"/> or an active
/// row in <see cref="IPeerKeyRegistry"/>) and verifies the cryptographic signature in full, and only then invokes
/// <see cref="IDatasetManager.ImportDatasetAsync"/> to verify the archive SHA-256 hash and byte size.
/// </para>
/// </remarks>
public sealed class VerifiedDatasetImporter
{
    /// <summary>
    /// Configuration catalog key controlling whether unsigned dataset manifests are rejected (<c>"Peer:RequireSignedDatasets"</c>).
    /// </summary>
    public const string StrictModeConfigurationKey = "Peer:RequireSignedDatasets";

    /// <summary>
    /// Fixed RFC 7807 problem detail returned when an uploaded archive's SHA-256 digest or byte size does not match its manifest.
    /// </summary>
    public const string IntegrityMismatchDetail = "Dataset import rejected: the archive does not match the checksum or size recorded in its manifest.";

    /// <summary>
    /// Fixed RFC 7807 problem detail returned when a manifest signature block is malformed or fails cryptographic verification.
    /// </summary>
    public const string SignatureInvalidDetail = "Dataset import rejected: the manifest signature is malformed or does not verify.";

    /// <summary>
    /// Fixed RFC 7807 problem detail returned when a manifest is signed by a key that is neither this node's own key nor an active peer key.
    /// </summary>
    public const string SignerNotTrustedDetail = "Dataset import rejected: the manifest is signed by a key this node does not trust.";

    /// <summary>
    /// Fixed RFC 7807 problem detail returned when an unsigned manifest is uploaded while <c>Peer:RequireSignedDatasets</c> is enabled.
    /// </summary>
    public const string UnsignedRejectedDetail = "Dataset import rejected: this node accepts only signed datasets from trusted peers.";

    /// <summary>
    /// Underlying integrity-checking dataset manager.
    /// </summary>
    private readonly IDatasetManager _datasetManager;

    /// <summary>
    /// Trusted peer public key registry.
    /// </summary>
    private readonly IPeerKeyRegistry _peerKeys;

    /// <summary>
    /// This node's own persistent Ed25519 dataset manifest signer.
    /// </summary>
    private readonly INodeDatasetSigner _localSigner;

    /// <summary>
    /// Cryptographic verifier for Ed25519 and ML-DSA-65 dataset manifest signatures.
    /// </summary>
    private readonly DatasetManifestSignatureVerifier _signatureVerifier;

    /// <summary>
    /// System configuration store queried on each unsigned import for <see cref="StrictModeConfigurationKey"/>.
    /// </summary>
    private readonly IConfigurationRepository _configuration;

    /// <summary>
    /// Structured diagnostic logger.
    /// </summary>
    private readonly ILogger<VerifiedDatasetImporter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="VerifiedDatasetImporter"/> class.
    /// </summary>
    /// <param name="datasetManager">Underlying dataset manager that checks archive SHA-256 and byte size and extracts files.</param>
    /// <param name="peerKeys">Repository of trusted peer dataset-signing public keys.</param>
    /// <param name="localSigner">This node's own dataset manifest signer (implicitly trusted when its signature verifies).</param>
    /// <param name="signatureVerifier">Cryptographic signature verifier for dataset manifests.</param>
    /// <param name="configuration">System configuration repository supplying <see cref="StrictModeConfigurationKey"/>.</param>
    /// <param name="logger">Logger for rejection warnings.</param>
    public VerifiedDatasetImporter(
        IDatasetManager datasetManager,
        IPeerKeyRegistry peerKeys,
        INodeDatasetSigner localSigner,
        DatasetManifestSignatureVerifier signatureVerifier,
        IConfigurationRepository configuration,
        ILogger<VerifiedDatasetImporter> logger)
    {
        ArgumentNullException.ThrowIfNull(datasetManager);
        ArgumentNullException.ThrowIfNull(peerKeys);
        ArgumentNullException.ThrowIfNull(localSigner);
        ArgumentNullException.ThrowIfNull(signatureVerifier);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _datasetManager = datasetManager;
        _peerKeys = peerKeys;
        _localSigner = localSigner;
        _signatureVerifier = signatureVerifier;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Evaluates manifest provenance and trust policy before delegating archive integrity verification and extraction.
    /// </summary>
    /// <param name="archiveStream">Readable stream containing the uploaded <c>.tar.gz</c> archive.</param>
    /// <param name="manifest">Parsed dataset manifest accompanying the archive.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="DatasetImportResult"/> describing whether the dataset was imported or why it was rejected.</returns>
    public async Task<DatasetImportResult> ImportAsync(
        Stream archiveStream,
        DatasetManifest manifest,
        CancellationToken ct = default)
    {
        // WHAT: Enforces the fixed 6-step provenance and integrity decision order before touching archiveStream.
        // WHY:  Rejects malformed signatures, untrusted signers, and policy-disallowed unsigned manifests before buffering or hashing any archive bytes.
        // HOW:  Inspects DatasetManifestSignature.GetState, evaluates strict mode or verifies the signature against local/active peer keys, then calls ImportDatasetAsync.
        DatasetManifestSignatureState state = DatasetManifestSignature.GetState(manifest);
        if (state == DatasetManifestSignatureState.Malformed)
        {
            _logger.LogWarning(
                "Dataset import rejected ({Outcome}): manifest signature block is malformed.",
                DatasetImportOutcome.SignatureInvalid);
            return new DatasetImportResult(DatasetImportOutcome.SignatureInvalid, null);
        }

        if (state == DatasetManifestSignatureState.Unsigned)
        {
            string rawStrict = await _configuration.GetValueAsync(StrictModeConfigurationKey, "false").ConfigureAwait(false);
            bool? strict = TryParseStrictMode(rawStrict);
            if (strict is null)
            {
                _logger.LogWarning(
                    "Configuration key {ConfigurationKey} holds an unparseable boolean value; failing closed and requiring signed datasets.",
                    StrictModeConfigurationKey);
            }

            if (strict != false)
            {
                _logger.LogWarning(
                    "Dataset import rejected ({Outcome}): unsigned manifest refused under strict dataset signing policy.",
                    DatasetImportOutcome.UnsignedRejectedByPolicy);
                return new DatasetImportResult(DatasetImportOutcome.UnsignedRejectedByPolicy, null);
            }
        }
        else
        {
            // WHY `!`: DatasetManifestSignature.GetState(manifest) == Signed guarantees SignerKeyId is a non-null 64-character lowercase hex string.
            string keyId = manifest.SignerKeyId!;
            KeyAlgorithm trustedAlgorithm;
            string trustedPublicKeyBase64;

            if (string.Equals(keyId, _localSigner.KeyId, StringComparison.Ordinal))
            {
                trustedAlgorithm = _localSigner.Algorithm;
                trustedPublicKeyBase64 = _localSigner.PublicKeyBase64;
            }
            else
            {
                PeerKeyRecord? peer = await _peerKeys.FindActiveAsync(keyId, ct).ConfigureAwait(false);
                if (peer is null)
                {
                    _logger.LogWarning(
                        "Dataset import rejected ({Outcome}): signer key {SignerKeyId} is not trusted.",
                        DatasetImportOutcome.SignerNotTrusted,
                        keyId);
                    return new DatasetImportResult(DatasetImportOutcome.SignerNotTrusted, null);
                }

                trustedAlgorithm = peer.Algorithm;
                trustedPublicKeyBase64 = peer.PublicKeyBase64;
            }

            if (!_signatureVerifier.Verify(manifest, trustedAlgorithm, trustedPublicKeyBase64))
            {
                _logger.LogWarning(
                    "Dataset import rejected ({Outcome}): signature verification failed for signer key {SignerKeyId}.",
                    DatasetImportOutcome.SignatureInvalid,
                    keyId);
                return new DatasetImportResult(DatasetImportOutcome.SignatureInvalid, null);
            }
        }

        bool imported = await _datasetManager.ImportDatasetAsync(archiveStream, manifest, ct).ConfigureAwait(false);
        if (!imported)
        {
            _logger.LogWarning(
                "Dataset import rejected ({Outcome}): archive checksum or size mismatch.",
                DatasetImportOutcome.IntegrityMismatch);
            return new DatasetImportResult(DatasetImportOutcome.IntegrityMismatch, null);
        }

        return state == DatasetManifestSignatureState.Signed
            ? new DatasetImportResult(DatasetImportOutcome.ImportedSigned, manifest.SignerKeyId)
            : new DatasetImportResult(DatasetImportOutcome.ImportedUnsigned, null);
    }

    /// <summary>
    /// Maps a rejection <see cref="DatasetImportOutcome"/> to its HTTP status code and fixed RFC 7807 detail sentence.
    /// </summary>
    /// <param name="outcome">The non-success import outcome to describe.</param>
    /// <returns>A tuple of the HTTP status code (<c>400</c> or <c>403</c>) and the constant detail message.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="outcome"/> is <see cref="DatasetImportOutcome.ImportedUnsigned"/> or <see cref="DatasetImportOutcome.ImportedSigned"/>.</exception>
    public static (int StatusCode, string Detail) DescribeRejection(DatasetImportOutcome outcome) =>
        outcome switch
        {
            DatasetImportOutcome.IntegrityMismatch => (StatusCodes.Status400BadRequest, IntegrityMismatchDetail),
            DatasetImportOutcome.SignatureInvalid => (StatusCodes.Status400BadRequest, SignatureInvalidDetail),
            DatasetImportOutcome.SignerNotTrusted => (StatusCodes.Status403Forbidden, SignerNotTrustedDetail),
            DatasetImportOutcome.UnsignedRejectedByPolicy => (StatusCodes.Status403Forbidden, UnsignedRejectedDetail),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Outcome does not represent a rejected dataset import.")
        };

    /// <summary>
    /// Parses the raw configuration string for <see cref="StrictModeConfigurationKey"/>.
    /// </summary>
    /// <param name="raw">The raw string returned by the configuration store.</param>
    /// <returns><c>true</c> or <c>false</c> when <paramref name="raw"/> is a valid boolean string; <c>null</c> when it cannot be parsed (caller fails closed).</returns>
    internal static bool? TryParseStrictMode(string? raw) =>
        bool.TryParse(raw?.Trim(), out bool parsed) ? parsed : null;
}
