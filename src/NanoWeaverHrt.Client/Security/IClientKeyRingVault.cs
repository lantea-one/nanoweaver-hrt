namespace NanoWeaverHrt.Client.Security;

/// <summary>
/// Provides zero-trust client-side keypair generation, envelope sealing, and challenge signing.
/// </summary>
public interface IClientKeyRingVault : IClientChallengeSigner
{
    /// <summary>
    /// Generates a new Ed25519 or ML-DSA-65 keypair in memory and seals the private key with AES-256-GCM.
    /// </summary>
    /// <param name="keyAlgorithm">Algorithm name ("Ed25519" or "ML-DSA-65").</param>
    /// <param name="passphrase">User passphrase used to derive the 256-bit wrapping key via PBKDF2-SHA256.</param>
    /// <returns>The generated public key PEM and sealed private key envelope.</returns>
    ClientKeyBundle GenerateKeyBundle(string keyAlgorithm, ReadOnlySpan<char> passphrase);

    /// <summary>
    /// Seals an existing PEM-encoded PKCS#8 private key into an AES-256-GCM envelope.
    /// </summary>
    /// <param name="privateKeyPem">Unencrypted PEM-encoded PKCS#8 private key.</param>
    /// <param name="passphrase">Passphrase for PBKDF2-SHA256 key derivation.</param>
    /// <returns>Base64-encoded envelope ([salt 16][nonce 12][tag 16][ciphertext]).</returns>
    string SealPrivateKeyPem(string privateKeyPem, ReadOnlySpan<char> passphrase);
}
