using NanoWeaverHrt.Security.Authentication;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace NanoWeaverHrt.Security.Signing;

/// <summary>
/// Pure, zero-side-effect validators and normalizers for peer key labels and SubjectPublicKeyInfo public keys.
/// </summary>
public static class PeerKeyValidation
{
    /// <summary>
    /// Maximum allowed length in characters for a normalized peer key label.
    /// </summary>
    public const int MaxLabelLength = 128;

    /// <summary>
    /// Maximum allowed length in characters for a submitted Base64 public key string.
    /// </summary>
    public const int MaxPublicKeyBase64Length = 8192;

    /// <summary>
    /// Trims and validates a human-readable peer key label.
    /// </summary>
    /// <param name="label">The raw label string supplied by the caller.</param>
    /// <param name="normalized">On success, the trimmed label; on failure, <see cref="string.Empty"/>.</param>
    /// <returns><c>true</c> if the trimmed label is 1 to 128 characters long and contains no control characters; otherwise <c>false</c>.</returns>
    public static bool TryNormalizeLabel(string? label, out string normalized)
    {
        // WHAT: Trims leading/trailing whitespace and rejects empty, overlong, or control-character labels.
        // WHY:  Prevents log/UI control-character injection and bounds the label to the VARCHAR(128) schema column.
        // HOW:  Checks null, trims, validates length in [1, 128], and scans characters with char.IsControl.
        normalized = string.Empty;
        if (label is null)
        {
            return false;
        }

        string trimmed = label.Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxLabelLength)
        {
            return false;
        }

        for (int i = 0; i < trimmed.Length; i++)
        {
            if (char.IsControl(trimmed[i]))
            {
                return false;
            }
        }

        normalized = trimmed;
        return true;
    }

    /// <summary>
    /// Validates and canonicalizes a Base64-encoded SubjectPublicKeyInfo DER public key against its declared algorithm.
    /// </summary>
    /// <param name="algorithm">The declared signature algorithm (<see cref="KeyAlgorithm.Ed25519"/> or <see cref="KeyAlgorithm.MlDsa65"/>).</param>
    /// <param name="publicKeyBase64">The submitted Base64-encoded SubjectPublicKeyInfo DER public key.</param>
    /// <param name="canonicalBase64">On success, the canonical Base64 re-encoding of the decoded DER bytes; on failure, <see cref="string.Empty"/>.</param>
    /// <param name="keyId">On success, the 64-character lowercase hex SHA-256 fingerprint of the DER bytes; on failure, <see cref="string.Empty"/>.</param>
    /// <returns><c>true</c> if the input is a valid SubjectPublicKeyInfo DER public key matching <paramref name="algorithm"/>; otherwise <c>false</c>.</returns>
    public static bool TryNormalizePublicKey(
        KeyAlgorithm algorithm,
        string? publicKeyBase64,
        out string canonicalBase64,
        out string keyId)
    {
        // WHAT: Parses a Base64 SubjectPublicKeyInfo DER public key, verifies its algorithm and parameter set, and computes its canonical encoding and key ID.
        // WHY:  Rejects PEM armor, PKCS#8 private keys, non-SPKI byte sequences, and mismatched algorithms (including non-ML-DSA-65 parameter sets) before storage.
        // HOW:  Decodes Base64, parses via BouncyCastle PublicKeyFactory.CreateKey, checks Ed25519PublicKeyParameters or MLDsaPublicKeyParameters (ml_dsa_65), and re-encodes DER.
        canonicalBase64 = string.Empty;
        keyId = string.Empty;

        if (string.IsNullOrWhiteSpace(publicKeyBase64)
            || publicKeyBase64.Length > MaxPublicKeyBase64Length
            || publicKeyBase64.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            return false;
        }

        byte[] der;
        AsymmetricKeyParameter parsedKey;
        try
        {
            der = Convert.FromBase64String(publicKeyBase64);
            parsedKey = PublicKeyFactory.CreateKey(der);
        }
        catch
        {
            return false;
        }

        bool matchesAlgorithm = algorithm == KeyAlgorithm.Ed25519
            ? parsedKey is Ed25519PublicKeyParameters
            : parsedKey is MLDsaPublicKeyParameters mldsaKey
                && mldsaKey.Parameters == MLDsaParameters.ml_dsa_65;

        if (!matchesAlgorithm)
        {
            return false;
        }

        canonicalBase64 = Convert.ToBase64String(der);
        keyId = DatasetManifestSignature.ComputeKeyId(der);
        return true;
    }
}
