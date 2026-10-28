using System.Security.Cryptography;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Security.Secrets;

namespace NanoWeaverHrt.Security.KeyManagement;

/// <summary>
/// Production implementation of <see cref="IKeyManagementService"/> managing the node's persistent ML-DSA-65 signing key,
/// durable subject-specific key encapsulation keys, and GDPR Article 17 crypto-shredding.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The key coordinator that loads the node's persistent post-quantum signing identity
/// from disk and forwards per-subject encryption key operations to <see cref="SubjectKeyVault"/>.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Every FDA 21 CFR Part 11 audit ledger record and every encrypted dataset export
/// manifest is signed by the node's ML-DSA-65 key. Before this persistence design landed, the node generated a fresh
/// signing key in memory on every start and discarded it on shutdown, so no signature made before that change can ever
/// be verified by anyone because the public half of those ephemeral keys was never saved either. Storing the key seed on
/// disk ensures that the node reconstructs the exact same key pair and 64-character SHA-256 public-key fingerprint across
/// restarts while keeping the private key encapsulated inside this service.
/// </para>
/// <para>
/// <strong>How it works (and operator warning):</strong> A 32-byte FIPS 204 seed is a compact random value from which the
/// mathematical key-generation algorithm deterministically expands the full ML-DSA-65 public and private lattice matrices.
/// On first start, <see cref="NodeSecretStore.LoadOrCreateKeyFile"/> writes 32 random bytes to
/// <c>node_signing_mldsa65.key</c> with owner-only (<c>0600</c>) permissions; on every subsequent start, the same 32 bytes
/// are read back, expanded into the key pair, and immediately zeroed from memory. <strong>Operator warning:</strong>
/// deleting <c>node_signing_mldsa65.key</c> is destructive—the next process start will treat its absence as a first launch
/// and mint a different key, causing all signatures made under the deleted key to classify as <c>UnknownSigningKey</c>.
/// </para>
/// </remarks>
public sealed class KeyManagementService : IKeyManagementService
{
    /// <summary>
    /// Post-quantum cryptographic provider for key derivation and digital signatures.
    /// </summary>
    private readonly IPostQuantumCryptoProvider _cryptoProvider;

    /// <summary>
    /// Durable vault storing and sealing per-subject ML-KEM-768 keys and erasure tombstones.
    /// </summary>
    private readonly SubjectKeyVault _subjectKeys;

    /// <summary>
    /// The public-only descriptor of the node's authoritative ML-DSA-65 signing key (<c>PrivateKeyBase64</c> is always <c>null</c>).
    /// </summary>
    private readonly PqcKeyMaterial _nodeSigningKey;

    /// <summary>
    /// Base64-encoded PKCS#8 DER private key for the node's ML-DSA-65 signing key, never exposed outside this class.
    /// </summary>
    private readonly string _nodeSigningPrivateKeyBase64;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyManagementService"/> class by loading or creating the
    /// persistent 32-byte ML-DSA-65 signing seed in <see cref="INodeSecretStore.NodeKeysDirectory"/>.
    /// </summary>
    /// <param name="cryptoProvider">The post-quantum cryptographic algorithm provider.</param>
    /// <param name="subjectKeys">The durable subject key vault.</param>
    /// <param name="secretStore">The node secret store providing the hardened key directory path.</param>
    /// <exception cref="ArgumentNullException">Any parameter is <c>null</c>.</exception>
    /// <exception cref="NodeSecretProvisioningException">The signing key seed file cannot be loaded or provisioned.</exception>
    public KeyManagementService(
        IPostQuantumCryptoProvider cryptoProvider,
        SubjectKeyVault subjectKeys,
        INodeSecretStore secretStore)
    {
        ArgumentNullException.ThrowIfNull(cryptoProvider);
        ArgumentNullException.ThrowIfNull(subjectKeys);
        ArgumentNullException.ThrowIfNull(secretStore);

        _cryptoProvider = cryptoProvider;
        _subjectKeys = subjectKeys;

        // WHAT: Load or atomically provision the 32-byte FIPS 204 seed file, derive the ML-DSA-65 key pair, and zero the seed.
        // WHY:  Ensures the node's audit and manifest signing identity survives process restarts while never exposing private key material.
        // HOW:  Calls NodeSecretStore.LoadOrCreateKeyFile, expands via DeriveSigningKeyPairFromSeed, strips PrivateKeyBase64 from the public descriptor, and zeroes the seed in finally.
        (byte[] seed, NodeSecretOrigin origin) = NodeSecretStore.LoadOrCreateKeyFile(
            secretStore.NodeKeysDirectory,
            NodeSigningKeyFileName);

        try
        {
            PqcKeyMaterial full = _cryptoProvider.DeriveSigningKeyPairFromSeed(seed);
            _nodeSigningPrivateKeyBase64 = full.PrivateKeyBase64!; // Justification: DeriveSigningKeyPairFromSeed always populates PrivateKeyBase64 with the encoded PKCS#8 DER.
            _nodeSigningKey = full with { PrivateKeyBase64 = null };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(seed);
        }

        NodeSigningKeyOrigin = origin;
    }

    /// <inheritdoc/>
    public PqcKeyMaterial GetNodeSigningKey() => _nodeSigningKey;

    /// <inheritdoc/>
    public Task<SubjectKeyProfile> GetOrCreateSubjectKeyAsync(string subjectId, CancellationToken ct = default) =>
        _subjectKeys.GetOrCreateAsync(subjectId, ct);

    /// <inheritdoc/>
    public SubjectKeyProfile? GetSubjectKey(string subjectId) =>
        _subjectKeys.Find(subjectId);

    /// <inheritdoc/>
    public Task<SubjectErasureOutcome> CryptoShredSubjectKeyAsync(string subjectId, CancellationToken ct = default) =>
        _subjectKeys.EraseAsync(subjectId, ct);

    /// <inheritdoc/>
    public IReadOnlyList<SubjectKeyProfile> GetAllSubjectProfiles() =>
        _subjectKeys.ListWithoutPrivateKeys();

    /// <inheritdoc/>
    public string GetSubjectPrivateKeyForDecryption(string subjectId) =>
        _subjectKeys.GetPrivateKeyForDecryption(subjectId);

    /// <inheritdoc/>
    public NodeSecretOrigin NodeSigningKeyOrigin { get; }

    /// <inheritdoc/>
    public PqcSignature SignWithNodeSigningKey(ReadOnlySpan<byte> data) =>
        _cryptoProvider.SignData(data, _nodeSigningPrivateKeyBase64, _nodeSigningKey.KeyId);

    /// <summary>
    /// File name of the persisted 32-byte ML-DSA-65 seed inside <see cref="INodeSecretStore.NodeKeysDirectory"/>.
    /// </summary>
    internal const string NodeSigningKeyFileName = "node_signing_mldsa65.key";
}
