using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.X509;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Secrets;

namespace NanoWeaverHrt.Security.Signing;

/// <summary>
/// Production implementation of <see cref="INodeDatasetSigner"/> backed by a persistent 32-byte Ed25519 seed file
/// (<c>dataset_signing_ed25519.key</c>) inside <see cref="INodeSecretStore.NodeKeysDirectory"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The per-node Ed25519 dataset manifest signer.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Provides a dedicated asymmetric signing identity for exported simulation dataset
/// bundles, completely isolated from the symmetric bearer-token and PHI master keys and from the ML-DSA-65 audit
/// ledger key.
/// </para>
/// <para>
/// <strong>How it works:</strong> Uses <see cref="NodeSecretStore.LoadOrCreateKeyFile"/> to provision or load the
/// 32-byte RFC 8032 secret seed with owner-only (<c>0600</c>) permissions, derives the X.509
/// <c>SubjectPublicKeyInfo</c> DER public key and its SHA-256 fingerprint, and signs canonical manifest bytes on demand.
/// </para>
/// </remarks>
public sealed class NodeDatasetSigner : INodeDatasetSigner
{
    /// <summary>
    /// File name of the persisted 32-byte Ed25519 dataset signing seed inside <see cref="INodeSecretStore.NodeKeysDirectory"/>.
    /// </summary>
    internal const string KeyFileName = "dataset_signing_ed25519.key";

    /// <summary>
    /// The 32-byte RFC 8032 Ed25519 private seed, never exposed outside this instance.
    /// </summary>
    private readonly byte[] _privateSeed;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeDatasetSigner"/> class by loading or creating
    /// <c>dataset_signing_ed25519.key</c> in <see cref="INodeSecretStore.NodeKeysDirectory"/>.
    /// </summary>
    /// <param name="secretStore">The node secret store providing the hardened key directory path.</param>
    /// <exception cref="ArgumentNullException"><paramref name="secretStore"/> is <c>null</c>.</exception>
    /// <exception cref="NodeSecretProvisioningException">The key file cannot be loaded or provisioned.</exception>
    public NodeDatasetSigner(INodeSecretStore secretStore)
    {
        ArgumentNullException.ThrowIfNull(secretStore);

        // WHAT: Load or atomically provision the 32-byte Ed25519 seed file and derive the public SPKI identity.
        // WHY:  Ensures the node's dataset signing identity persists across restarts without touching token or PHI keys.
        // HOW:  Calls NodeSecretStore.LoadOrCreateKeyFile, derives Ed25519PublicKeyParameters, encodes SPKI DER, and hashes it for KeyId.
        (byte[] seed, NodeSecretOrigin origin) = NodeSecretStore.LoadOrCreateKeyFile(
            secretStore.NodeKeysDirectory,
            KeyFileName);

        _privateSeed = seed;
        KeyOrigin = origin;
        Algorithm = KeyAlgorithm.Ed25519;

        Ed25519PrivateKeyParameters privateKey = new(_privateSeed, 0);
        Ed25519PublicKeyParameters publicKey = privateKey.GeneratePublicKey();
        byte[] spkiDer = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(publicKey).GetDerEncoded();

        PublicKeyBase64 = Convert.ToBase64String(spkiDer);
        KeyId = DatasetManifestSignature.ComputeKeyId(spkiDer);
    }

    /// <inheritdoc/>
    public string KeyId { get; }

    /// <inheritdoc/>
    public KeyAlgorithm Algorithm { get; }

    /// <inheritdoc/>
    public string PublicKeyBase64 { get; }

    /// <inheritdoc/>
    public NodeSecretOrigin KeyOrigin { get; }

    /// <inheritdoc/>
    public DatasetManifest Sign(DatasetManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        // WHAT: Populate the v2 signature metadata fields, encode the canonical signing bytes, and attach the Ed25519 signature.
        // WHY:  ManifestVersion, SignatureAlgorithm, and SignerKeyId are part of the canonical signed payload and must be set before signing.
        // HOW:  Creates a copy via with-expression, computes GetSigningBytes, signs via SignCanonicalBytes, and sets SignatureBase64.
        DatasetManifest metadataStamped = manifest with
        {
            ManifestVersion = DatasetManifestSignature.SignedManifestVersion,
            SignatureAlgorithm = DatasetManifestSignature.Ed25519AlgorithmName,
            SignerKeyId = KeyId,
            SignatureBase64 = null
        };

        byte[] canonicalBytes = DatasetManifestCanonicalizer.GetSigningBytes(metadataStamped);
        byte[] signatureBytes = SignCanonicalBytes(canonicalBytes);

        return metadataStamped with
        {
            SignatureBase64 = Convert.ToBase64String(signatureBytes)
        };
    }

    /// <summary>
    /// Computes the raw 64-byte deterministic RFC 8032 Ed25519 signature over <paramref name="data"/>.
    /// </summary>
    /// <param name="data">The byte span to sign.</param>
    /// <returns>The 64-byte Ed25519 signature.</returns>
    internal byte[] SignCanonicalBytes(ReadOnlySpan<byte> data)
    {
        Ed25519PrivateKeyParameters privateKey = new(_privateSeed, 0);
        Ed25519Signer signer = new();
        signer.Init(forSigning: true, privateKey);
        signer.BlockUpdate(data);
        return signer.GenerateSignature();
    }
}
