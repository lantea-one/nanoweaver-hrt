using NanoWeaverHrt.Security.Models;

namespace NanoWeaverHrt.Security.Cryptography;

/// <summary>
/// Service contract providing post-quantum key generation, key encapsulation (ML-KEM-768),
/// symmetric bulk encryption (AES-256-GCM), and digital signatures (ML-DSA-65).
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The primary cryptographic engine implementing NIST Post-Quantum Cryptography standards.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> To secure sensitive genomic, endocrine, and simulation data at rest against future quantum decryption.
/// </para>
/// <para>
/// <strong>How it works:</strong> Combines NIST FIPS 203 (ML-KEM-768) lattice-based key encapsulation with authenticated AES-256-GCM
/// and NIST FIPS 204 (ML-DSA-65) digital signatures.
/// </para>
/// </remarks>
public interface IPostQuantumCryptoProvider
{
    /// <summary>
    /// Generates a new ML-KEM-768 key pair for key encapsulation and envelope encryption.
    /// </summary>
    /// <param name="keyId">The unique identifier to assign to this key pair.</param>
    /// <returns>A new <see cref="PqcKeyMaterial"/> containing public and private lattice key parameters.</returns>
    PqcKeyMaterial GenerateKemKeyPair(string keyId);

    /// <summary>
    /// Generates a new ML-DSA-65 key pair for tamper-evident digital signatures.
    /// </summary>
    /// <param name="keyId">The unique identifier to assign to this signing key pair.</param>
    /// <returns>A new <see cref="PqcKeyMaterial"/> containing public and private signing parameters.</returns>
    PqcKeyMaterial GenerateSigningKeyPair(string keyId);

    /// <summary>
    /// Encrypts raw plaintext bytes into an authenticated <see cref="EncryptedEnvelope"/> using ML-KEM-768 and AES-256-GCM.
    /// </summary>
    /// <param name="plaintext">The raw unencrypted data span.</param>
    /// <param name="recipientPublicKeyBase64">The recipient's Base64-encoded ML-KEM-768 public key.</param>
    /// <param name="keyId">The identifier of the recipient's key.</param>
    /// <returns>An authenticated <see cref="EncryptedEnvelope"/> container.</returns>
    EncryptedEnvelope EncryptEnvelope(ReadOnlySpan<byte> plaintext, string recipientPublicKeyBase64, string keyId);

    /// <summary>
    /// Decrypts an authenticated <see cref="EncryptedEnvelope"/> using ML-KEM-768 decapsulation and AES-256-GCM.
    /// </summary>
    /// <param name="envelope">The encrypted envelope containing the encapsulated key, ciphertext, nonce, and tag.</param>
    /// <param name="recipientPrivateKeyBase64">The recipient's Base64-encoded ML-KEM-768 private key.</param>
    /// <returns>The decrypted plaintext byte array.</returns>
    byte[] DecryptEnvelope(EncryptedEnvelope envelope, string recipientPrivateKeyBase64);

    /// <summary>
    /// Signs raw data using NIST FIPS 204 ML-DSA-65 post-quantum digital signatures.
    /// </summary>
    /// <param name="data">The raw data span to sign.</param>
    /// <param name="signerPrivateKeyBase64">The signer's Base64-encoded ML-DSA-65 private key.</param>
    /// <param name="signerKeyId">The identifier of the signer's key.</param>
    /// <returns>A tamper-evident <see cref="PqcSignature"/> record.</returns>
    PqcSignature SignData(ReadOnlySpan<byte> data, string signerPrivateKeyBase64, string signerKeyId);

    /// <summary>
    /// Verifies an ML-DSA-65 post-quantum digital signature against raw data.
    /// </summary>
    /// <param name="data">The raw data span that was signed.</param>
    /// <param name="signatureBase64">The Base64-encoded digital signature bytes.</param>
    /// <param name="signerPublicKeyBase64">The signer's Base64-encoded ML-DSA-65 public key.</param>
    /// <returns><c>true</c> if the signature is cryptographically valid and matches the data; otherwise, <c>false</c>.</returns>
    bool VerifySignature(ReadOnlySpan<byte> data, string signatureBase64, string signerPublicKeyBase64);
}
