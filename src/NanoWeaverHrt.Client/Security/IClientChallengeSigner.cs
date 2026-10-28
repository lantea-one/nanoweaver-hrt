namespace NanoWeaverHrt.Client.Security;

/// <summary>
/// Defines client-side signing of authentication challenge nonces.
/// </summary>
public interface IClientChallengeSigner
{
    /// <summary>
    /// Unseals an AES-256-GCM private key envelope and signs a raw server challenge nonce.
    /// </summary>
    /// <param name="keyAlgorithm">Algorithm name ("Ed25519" or "ML-DSA-65").</param>
    /// <param name="encryptedEnvelopeBase64">Base64-encoded sealed private key envelope.</param>
    /// <param name="passphrase">Passphrase used to unseal the envelope.</param>
    /// <param name="challengeNonce">Raw challenge bytes issued by POST /api/auth/challenge.</param>
    /// <returns>Base64-encoded cryptographic signature.</returns>
    string SignChallenge(
        string keyAlgorithm,
        string encryptedEnvelopeBase64,
        ReadOnlySpan<char> passphrase,
        ReadOnlySpan<byte> challengeNonce);
}
