using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;

namespace NanoWeaverHrt.Security.Signing;

/// <summary>
/// Classification of a dataset manifest's signature block, evaluated before any public-key lookup.
/// </summary>
public enum DatasetManifestSignatureState
{
    /// <summary>
    /// <see cref="DatasetManifest.SignatureAlgorithm"/>, <see cref="DatasetManifest.SignerKeyId"/>,
    /// and <see cref="DatasetManifest.SignatureBase64"/> are all <c>null</c>.
    /// </summary>
    Unsigned,

    /// <summary>
    /// All three signature fields are present and well-formed, and <see cref="DatasetManifest.ManifestVersion"/> is <c>2</c>.
    /// </summary>
    Signed,

    /// <summary>
    /// The signature block is only partially populated, carries empty or whitespace fields, names an unknown algorithm,
    /// has a key identifier that is not 64 lowercase hexadecimal characters, or has a version other than <c>2</c>.
    /// </summary>
    Malformed
}

/// <summary>
/// Constants, structural classification, algorithm parsing, and public-key fingerprint calculation for signed dataset manifests.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The protocol metadata helper for v2 dataset manifest digital signatures.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Ensures that exporters and importers agree on the exact domain-separation tag,
/// schema version, algorithm names (<c>Ed25519</c> and <c>MlDsa65</c>), and 64-character SHA-256 key identifier format.
/// </para>
/// <para>
/// <strong>How it works:</strong> Inspects a deserialized <see cref="DatasetManifest"/> without allocating or throwing,
/// classifying it into <see cref="DatasetManifestSignatureState"/> before any cryptographic verification runs.
/// </para>
/// </remarks>
public static class DatasetManifestSignature
{
    /// <summary>
    /// ASCII domain-separation tag written at the start of every canonical dataset manifest byte sequence.
    /// </summary>
    public const string DomainTag = "NWHRT-DATASET-MANIFEST";

    /// <summary>
    /// Schema version number required on every signed dataset manifest.
    /// </summary>
    public const int SignedManifestVersion = 2;

    /// <summary>
    /// Canonical manifest string for the Ed25519 signature algorithm.
    /// </summary>
    public const string Ed25519AlgorithmName = "Ed25519";

    /// <summary>
    /// Canonical manifest string for the NIST FIPS 204 ML-DSA-65 signature algorithm.
    /// </summary>
    public const string MlDsa65AlgorithmName = "MlDsa65";

    /// <summary>
    /// Classifies a manifest's signature block as <see cref="DatasetManifestSignatureState.Unsigned"/>,
    /// <see cref="DatasetManifestSignatureState.Signed"/>, or <see cref="DatasetManifestSignatureState.Malformed"/>.
    /// </summary>
    /// <param name="manifest">The dataset manifest to inspect.</param>
    /// <returns>The classified <see cref="DatasetManifestSignatureState"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="manifest"/> is <c>null</c>.</exception>
    public static DatasetManifestSignatureState GetState(DatasetManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        // WHAT: Classify the manifest's signature block before any key lookup or cryptographic verification.
        // WHY:  Separates legacy unsigned manifests from structurally malformed blocks and well-formed signed manifests.
        // HOW:  Checks whether all three signature properties are null (Unsigned), then validates version 2, algorithm name, 64-char hex key id, and non-whitespace signature.
        if (manifest.SignatureAlgorithm is null &&
            manifest.SignerKeyId is null &&
            manifest.SignatureBase64 is null)
        {
            return DatasetManifestSignatureState.Unsigned;
        }

        if (string.IsNullOrWhiteSpace(manifest.SignatureAlgorithm) ||
            string.IsNullOrWhiteSpace(manifest.SignerKeyId) ||
            string.IsNullOrWhiteSpace(manifest.SignatureBase64))
        {
            return DatasetManifestSignatureState.Malformed;
        }

        if (manifest.ManifestVersion != SignedManifestVersion)
        {
            return DatasetManifestSignatureState.Malformed;
        }

        if (!TryParseAlgorithm(manifest.SignatureAlgorithm, out _))
        {
            return DatasetManifestSignatureState.Malformed;
        }

        if (!IsLowerHexKeyId(manifest.SignerKeyId))
        {
            return DatasetManifestSignatureState.Malformed;
        }

        return DatasetManifestSignatureState.Signed;
    }

    /// <summary>
    /// Maps a manifest signature algorithm string ordinally to its <see cref="KeyAlgorithm"/> enum value.
    /// </summary>
    /// <param name="name">The algorithm string from the manifest.</param>
    /// <param name="algorithm">When this method returns <c>true</c>, contains the parsed <see cref="KeyAlgorithm"/>.</param>
    /// <returns><c>true</c> if <paramref name="name"/> is ordinally <c>"Ed25519"</c> or <c>"MlDsa65"</c>; otherwise, <c>false</c>.</returns>
    public static bool TryParseAlgorithm(string? name, out KeyAlgorithm algorithm)
    {
        if (string.Equals(name, Ed25519AlgorithmName, StringComparison.Ordinal))
        {
            algorithm = KeyAlgorithm.Ed25519;
            return true;
        }

        if (string.Equals(name, MlDsa65AlgorithmName, StringComparison.Ordinal))
        {
            algorithm = KeyAlgorithm.MlDsa65;
            return true;
        }

        algorithm = default;
        return false;
    }

    /// <summary>
    /// Computes the 64-character lowercase hexadecimal SHA-256 fingerprint of a DER-encoded
    /// <c>SubjectPublicKeyInfo</c> public key.
    /// </summary>
    /// <param name="subjectPublicKeyInfoDer">The DER-encoded <c>SubjectPublicKeyInfo</c> bytes.</param>
    /// <returns>A 64-character lowercase hexadecimal string (<c>0-9a-f</c>).</returns>
    public static string ComputeKeyId(ReadOnlySpan<byte> subjectPublicKeyInfoDer) =>
        PublicKeyFingerprint.ComputeKeyId(subjectPublicKeyInfoDer);

    /// <summary>
    /// Checks whether a string consists of exactly 64 lowercase hexadecimal characters (<c>0-9</c> or <c>a-f</c>).
    /// </summary>
    /// <param name="value">Candidate key identifier string.</param>
    /// <returns><c>true</c> if valid; otherwise, <c>false</c>.</returns>
    private static bool IsLowerHexKeyId(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool isDigit = c >= '0' && c <= '9';
            bool isLowerHex = c >= 'a' && c <= 'f';
            if (!isDigit && !isLowerHex)
            {
                return false;
            }
        }

        return true;
    }
}
