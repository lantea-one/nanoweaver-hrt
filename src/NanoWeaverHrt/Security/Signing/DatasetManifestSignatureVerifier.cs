using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;

namespace NanoWeaverHrt.Security.Signing;

/// <summary>
/// Verifies dataset manifest digital signatures (Ed25519 and ML-DSA-65) against a caller-supplied trusted public key.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A stateless cryptographic verifier for signed <see cref="DatasetManifest"/> instances.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Allows importing nodes to verify that a dataset manifest was signed by a trusted
/// peer key over the exact canonical byte representation of its fields before accepting the archive.
/// </para>
/// <para>
/// <strong>How it works:</strong> Checks structural well-formedness via <see cref="DatasetManifestSignature.GetState"/>,
/// confirms that the manifest's algorithm and key identifier match the supplied trusted public key, reconstructs the
/// canonical signing bytes via <see cref="DatasetManifestCanonicalizer.GetSigningBytes"/>, and verifies the signature
/// using BouncyCastle <see cref="Ed25519Signer"/> or <see cref="IPostQuantumCryptoProvider.VerifySignature"/>.
/// </para>
/// </remarks>
public sealed class DatasetManifestSignatureVerifier
{
    /// <summary>
    /// Post-quantum cryptographic provider used for ML-DSA-65 signature verification.
    /// </summary>
    private readonly IPostQuantumCryptoProvider _pqcProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatasetManifestSignatureVerifier"/> class.
    /// </summary>
    /// <param name="pqcProvider">The post-quantum cryptographic provider.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pqcProvider"/> is <c>null</c>.</exception>
    public DatasetManifestSignatureVerifier(IPostQuantumCryptoProvider pqcProvider)
    {
        ArgumentNullException.ThrowIfNull(pqcProvider);
        _pqcProvider = pqcProvider;
    }

    /// <summary>
    /// Verifies <paramref name="manifest"/> against a trusted algorithm and Base64-encoded SubjectPublicKeyInfo DER public key.
    /// Never throws for a non-null manifest; any structural, encoding, key-parsing, or cryptographic error returns <c>false</c>.
    /// </summary>
    /// <param name="manifest">The dataset manifest to verify.</param>
    /// <param name="trustedAlgorithm">The algorithm registered for the trusted key.</param>
    /// <param name="trustedPublicKeyBase64">The Base64-encoded SubjectPublicKeyInfo DER bytes of the trusted public key.</param>
    /// <returns><c>true</c> if the manifest is well-formed, matches the trusted key, and has a valid signature; otherwise, <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="manifest"/> is <c>null</c>.</exception>
    public bool Verify(DatasetManifest manifest, KeyAlgorithm trustedAlgorithm, string trustedPublicKeyBase64)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        // WHAT: Validate manifest signature state, algorithm match, key fingerprint match, and cryptographic signature.
        // WHY:  Prevents algorithm-substitution attacks, key-id mismatches, and malformed inputs from throwing or passing verification.
        // HOW:  Checks GetState and TryParseAlgorithm, decodes SPKI DER to compare ComputeKeyId, encodes canonical bytes, and dispatches to Ed25519 or ML-DSA-65.
        if (DatasetManifestSignature.GetState(manifest) != DatasetManifestSignatureState.Signed)
        {
            return false;
        }

        if (!DatasetManifestSignature.TryParseAlgorithm(manifest.SignatureAlgorithm, out KeyAlgorithm manifestAlgorithm) ||
            manifestAlgorithm != trustedAlgorithm)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(trustedPublicKeyBase64))
        {
            return false;
        }

        try
        {
            byte[] spkiDer = Convert.FromBase64String(trustedPublicKeyBase64);
            string computedKeyId = DatasetManifestSignature.ComputeKeyId(spkiDer);
            if (!string.Equals(manifest.SignerKeyId, computedKeyId, StringComparison.Ordinal))
            {
                return false;
            }

            byte[] canonicalBytes = DatasetManifestCanonicalizer.GetSigningBytes(manifest);

            if (trustedAlgorithm == KeyAlgorithm.Ed25519)
            {
                AsymmetricKeyParameter parsedKey = PublicKeyFactory.CreateKey(spkiDer);
                if (parsedKey is not Ed25519PublicKeyParameters ed25519PublicKey)
                {
                    return false;
                }

                byte[] signatureBytes = Convert.FromBase64String(manifest.SignatureBase64!); // Justification: GetState == Signed guarantees SignatureBase64 is non-null.
                Ed25519Signer verifier = new();
                verifier.Init(forSigning: false, ed25519PublicKey);
                verifier.BlockUpdate(canonicalBytes, 0, canonicalBytes.Length);
                return verifier.VerifySignature(signatureBytes);
            }

            return _pqcProvider.VerifySignature(
                canonicalBytes,
                manifest.SignatureBase64!, // Justification: GetState == Signed guarantees SignatureBase64 is non-null.
                trustedPublicKeyBase64);
        }
        catch
        {
            return false;
        }
    }
}
