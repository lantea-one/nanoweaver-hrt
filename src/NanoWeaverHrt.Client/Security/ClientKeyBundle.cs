namespace NanoWeaverHrt.Client.Security;

/// <summary>
/// Represents a newly generated client-side asymmetric keypair and sealed private key envelope.
/// </summary>
/// <param name="KeyAlgorithm">Canonical algorithm identifier ("Ed25519" or "ML-DSA-65").</param>
/// <param name="PublicKeyPem">PEM-encoded public key for server registration.</param>
/// <param name="EncryptedPrivateKeyEnvelopeBase64">Base64-encoded AES-256-GCM encrypted PKCS#8 private key DER.</param>
/// <param name="PublicKeyFingerprint">SHA-256 hex fingerprint of the public key DER.</param>
public sealed record ClientKeyBundle(
    string KeyAlgorithm,
    string PublicKeyPem,
    string EncryptedPrivateKeyEnvelopeBase64,
    string PublicKeyFingerprint);
