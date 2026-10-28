using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Secrets;

namespace NanoWeaverHrt.Security.Signing;

/// <summary>
/// Exposes the public identity of this node's persistent Ed25519 dataset manifest signing key and signs exported manifests.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The node-local signing contract for dataset provenance manifests.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Allows this node to sign exported dataset manifests with a persistent Ed25519 key
/// and publish the corresponding public key and 64-character SHA-256 fingerprint to peer laboratories without ever
/// exposing the private seed outside the signer implementation.
/// </para>
/// <para>
/// <strong>How it works:</strong> Populates the version, algorithm, and key-identifier metadata on a
/// <see cref="DatasetManifest"/>, computes the canonical signing bytes over those fields, and attaches the Base64
/// Ed25519 signature.
/// </para>
/// </remarks>
public interface INodeDatasetSigner
{
    /// <summary>
    /// Gets the 64-character lowercase hexadecimal SHA-256 fingerprint of this node's SubjectPublicKeyInfo DER public key.
    /// </summary>
    string KeyId { get; }

    /// <summary>
    /// Gets the signature algorithm of this node's dataset signing key (always <see cref="KeyAlgorithm.Ed25519"/>).
    /// </summary>
    KeyAlgorithm Algorithm { get; }

    /// <summary>
    /// Gets the Base64-encoded SubjectPublicKeyInfo DER representation of this node's Ed25519 dataset signing public key.
    /// </summary>
    string PublicKeyBase64 { get; }

    /// <summary>
    /// Gets the provenance of the signing key file for this process start (<see cref="NodeSecretOrigin.GeneratedFile"/>
    /// on first launch or <see cref="NodeSecretOrigin.ExistingFile"/> on subsequent starts).
    /// </summary>
    NodeSecretOrigin KeyOrigin { get; }

    /// <summary>
    /// Returns a signed copy of <paramref name="manifest"/> carrying <c>ManifestVersion = 2</c>,
    /// <c>SignatureAlgorithm = "Ed25519"</c>, <c>SignerKeyId = KeyId</c>, and <c>SignatureBase64</c>.
    /// </summary>
    /// <param name="manifest">The dataset manifest to sign.</param>
    /// <returns>A new <see cref="DatasetManifest"/> instance with all signature fields populated.</returns>
    DatasetManifest Sign(DatasetManifest manifest);
}
