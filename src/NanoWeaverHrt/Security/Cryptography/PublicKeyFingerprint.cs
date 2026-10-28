using System.Security.Cryptography;

namespace NanoWeaverHrt.Security.Cryptography;

/// <summary>
/// Computes canonical public-key fingerprints used as signer and peer key identifiers across the platform.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A pure, stateless cryptographic helper that turns an X.509
/// <c>SubjectPublicKeyInfo</c> (SPKI) DER byte sequence into its 64-character lowercase hexadecimal SHA-256 digest.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Every node signing key (ML-DSA-65 for FDA 21 CFR Part 11 audit ledgers and
/// Ed25519 for dataset export manifests) and every trusted peer key in the registry is identified by the SHA-256
/// digest of its DER-encoded public key. Centralizing that calculation in one method guarantees that audit
/// verifiers, dataset verifiers, and peer key lookups always derive identical identifiers.
/// </para>
/// <para>
/// <strong>How it works:</strong> Hashes the input span with <see cref="SHA256.HashData(ReadOnlySpan{byte})"/> and
/// formats the 32-byte digest with <see cref="Convert.ToHexStringLower(ReadOnlySpan{byte})"/>.
/// </para>
/// </remarks>
public static class PublicKeyFingerprint
{
    /// <summary>
    /// Computes the 64-character lowercase hexadecimal SHA-256 fingerprint of a DER-encoded
    /// <c>SubjectPublicKeyInfo</c> public key.
    /// </summary>
    /// <param name="subjectPublicKeyInfoDer">The DER-encoded <c>SubjectPublicKeyInfo</c> bytes.</param>
    /// <returns>A 64-character lowercase hexadecimal string (<c>0-9a-f</c>).</returns>
    public static string ComputeKeyId(ReadOnlySpan<byte> subjectPublicKeyInfoDer)
    {
        // WHAT: Hash the SubjectPublicKeyInfo DER bytes with SHA-256 and encode as lowercase hexadecimal.
        // WHY:  Produces a collision-resistant, deterministic 64-character key identifier shared by ML-DSA-65 and Ed25519 keys.
        // HOW:  Allocates a 32-byte stack buffer for SHA256.HashData and formats it via Convert.ToHexStringLower.
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(subjectPublicKeyInfoDer, digest);
        return Convert.ToHexStringLower(digest);
    }
}
