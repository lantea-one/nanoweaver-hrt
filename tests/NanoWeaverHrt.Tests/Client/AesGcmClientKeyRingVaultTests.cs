using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using NanoWeaverHrt.Client.Security;
using NanoWeaverHrt.Security.Authentication;
using Xunit;

namespace NanoWeaverHrt.Tests.Client;

/// <summary>
/// Comprehensive unit tests verifying zero-trust asymmetric key generation, AES-256-GCM envelope sealing,
/// PBKDF2-SHA256 key derivation, PEM import/export, and challenge signing for Ed25519 and ML-DSA-65.
/// </summary>
public sealed class AesGcmClientKeyRingVaultTests
{
    /// <summary>
    /// Vault instance under test.
    /// </summary>
    private readonly AesGcmClientKeyRingVault _vault = new();

    /// <summary>
    /// Verifies that key bundle generation produces valid Ed25519 credentials and verifiable signatures.
    /// </summary>
    /// <returns>A task representing test completion.</returns>
    [Fact]
    public void GenerateKeyBundle_Ed25519_GeneratesValidBundleAndSignsChallenge()
    {
        // WHAT: Generates an Ed25519 key bundle sealed with passphrase and signs a challenge nonce.
        // WHY:  Verifies end-to-end client-side Ed25519 workflow without server interaction.
        // HOW:  Calls GenerateKeyBundle, verifies fingerprint, signs 32-byte nonce, and verifies with Ed25519Signer.
        const string passphrase = "master-client-passphrase-2026!";
        ClientKeyBundle bundle = _vault.GenerateKeyBundle("Ed25519", passphrase);

        Assert.Equal("Ed25519", bundle.KeyAlgorithm);
        Assert.Contains("-----BEGIN PUBLIC KEY-----", bundle.PublicKeyPem);
        Assert.Contains("-----END PUBLIC KEY-----", bundle.PublicKeyPem);
        Assert.False(string.IsNullOrWhiteSpace(bundle.EncryptedPrivateKeyEnvelopeBase64));
        Assert.Equal(64, bundle.PublicKeyFingerprint.Length);

        // Verify SHA-256 fingerprint calculation matches public key DER
        byte[] pubDer = ExtractDerFromPem(bundle.PublicKeyPem);
        string expectedFingerprint = Convert.ToHexStringLower(SHA256.HashData(pubDer));
        Assert.Equal(expectedFingerprint, bundle.PublicKeyFingerprint);

        // Sign challenge nonce
        byte[] challengeNonce = new byte[32];
        RandomNumberGenerator.Fill(challengeNonce);

        string signatureBase64 = _vault.SignChallenge(
            "Ed25519",
            bundle.EncryptedPrivateKeyEnvelopeBase64,
            passphrase,
            challengeNonce);

        Assert.False(string.IsNullOrWhiteSpace(signatureBase64));

        // Verify cryptographic signature against public key
        bool verified = VerifyEd25519Signature(pubDer, challengeNonce, signatureBase64);
        Assert.True(verified);
    }

    /// <summary>
    /// Verifies that key bundle generation produces valid ML-DSA-65 credentials and verifiable signatures.
    /// </summary>
    /// <returns>A task representing test completion.</returns>
    [Fact]
    public void GenerateKeyBundle_MlDsa65_GeneratesValidBundleAndSignsChallenge()
    {
        // WHAT: Generates a post-quantum ML-DSA-65 key bundle and signs a challenge nonce.
        // WHY:  Verifies NIST FIPS 204 post-quantum client signing capability.
        // HOW:  Calls GenerateKeyBundle with "ML-DSA-65", signs nonce, and verifies with MLDsaSigner.
        const string passphrase = "pqc-quantum-safe-passphrase-42";
        ClientKeyBundle bundle = _vault.GenerateKeyBundle("ML-DSA-65", passphrase);

        Assert.Equal("ML-DSA-65", bundle.KeyAlgorithm);
        Assert.Contains("-----BEGIN PUBLIC KEY-----", bundle.PublicKeyPem);
        Assert.Contains("-----END PUBLIC KEY-----", bundle.PublicKeyPem);
        Assert.False(string.IsNullOrWhiteSpace(bundle.EncryptedPrivateKeyEnvelopeBase64));
        Assert.Equal(64, bundle.PublicKeyFingerprint.Length);

        byte[] pubDer = ExtractDerFromPem(bundle.PublicKeyPem);
        string expectedFingerprint = Convert.ToHexStringLower(SHA256.HashData(pubDer));
        Assert.Equal(expectedFingerprint, bundle.PublicKeyFingerprint);

        byte[] challengeNonce = new byte[32];
        RandomNumberGenerator.Fill(challengeNonce);

        string signatureBase64 = _vault.SignChallenge(
            "ml-dsa-65",
            bundle.EncryptedPrivateKeyEnvelopeBase64,
            passphrase,
            challengeNonce);

        Assert.False(string.IsNullOrWhiteSpace(signatureBase64));

        bool verified = VerifyMlDsa65Signature(pubDer, challengeNonce, signatureBase64);
        Assert.True(verified);
    }

    /// <summary>
    /// Verifies argument validation in <see cref="IClientKeyRingVault.GenerateKeyBundle"/>.
    /// </summary>
    /// <param name="invalidAlgorithm">The unsupported or invalid algorithm identifier.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GenerateKeyBundle_InvalidAlgorithm_ThrowsArgumentException(string? invalidAlgorithm)
    {
        // WHAT: Asserts null or empty algorithm throws ArgumentException.
        // WHY:  Guarantees fail-fast on malformed parameters.
        // HOW:  Invokes GenerateKeyBundle with invalid algorithm.
        Assert.ThrowsAny<ArgumentException>(() => _vault.GenerateKeyBundle(invalidAlgorithm!, "passphrase"));
    }

    /// <summary>
    /// Verifies that unsupported key algorithms throw <see cref="NotSupportedException"/>.
    /// </summary>
    /// <param name="unsupportedAlgorithm">An unsupported algorithm name.</param>
    [Theory]
    [InlineData("RSA-4096")]
    [InlineData("ECDSA-P256")]
    [InlineData("Falcon-512")]
    public void GenerateKeyBundle_UnsupportedAlgorithm_ThrowsNotSupportedException(string unsupportedAlgorithm)
    {
        // WHAT: Asserts unsupported algorithms throw NotSupportedException.
        // WHY:  Platform strictly enforces Ed25519 and ML-DSA-65.
        // HOW:  Invokes GenerateKeyBundle with non-standard algorithm strings.
        Assert.Throws<NotSupportedException>(() => _vault.GenerateKeyBundle(unsupportedAlgorithm, "passphrase"));
    }

    /// <summary>
    /// Verifies that empty or whitespace passphrases throw <see cref="ArgumentException"/>.
    /// </summary>
    /// <param name="invalidPassphrase">The invalid passphrase string.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GenerateKeyBundle_EmptyOrWhitespacePassphrase_ThrowsArgumentException(string invalidPassphrase)
    {
        // WHAT: Asserts empty or whitespace passphrase throws ArgumentException.
        // WHY:  Zero-trust sealing prohibits empty encryption keys.
        // HOW:  Invokes GenerateKeyBundle with blank passphrase.
        Assert.Throws<ArgumentException>(() => _vault.GenerateKeyBundle("Ed25519", invalidPassphrase));
    }

    /// <summary>
    /// Verifies sealing existing unencrypted private key PEM and signing challenges.
    /// </summary>
    [Fact]
    public void SealPrivateKeyPem_ValidEd25519Pem_SealsEnvelopeAndSignsChallenge()
    {
        // WHAT: Generates an unencrypted Ed25519 keypair, formats PEM, seals via SealPrivateKeyPem, and signs challenge.
        // WHY:  Verifies import of raw unencrypted PEM keys into sealed envelopes.
        // HOW:  Generates Ed25519 pair, formats PEM, seals with passphrase, signs challenge, and verifies.
        const string passphrase = "seal-existing-key-passphrase";
        GeneratedKeyBundle bundle = TestChallengeHelper.CreateTestKeyBundle("test_ed", algorithm: KeyAlgorithm.Ed25519);

        string sealedEnvelopeBase64 = _vault.SealPrivateKeyPem(bundle.PrivateKeyPem, passphrase);
        Assert.False(string.IsNullOrWhiteSpace(sealedEnvelopeBase64));

        byte[] nonce = new byte[32];
        RandomNumberGenerator.Fill(nonce);

        string signatureBase64 = _vault.SignChallenge("Ed25519", sealedEnvelopeBase64, passphrase, nonce);
        Assert.False(string.IsNullOrWhiteSpace(signatureBase64));

        byte[] pubDer = ExtractDerFromPem(bundle.PublicKeyPem);
        bool verified = VerifyEd25519Signature(pubDer, nonce, signatureBase64);
        Assert.True(verified);
    }

    /// <summary>
    /// Verifies sealing existing unencrypted ML-DSA-65 private key PEM and signing challenges.
    /// </summary>
    [Fact]
    public void SealPrivateKeyPem_ValidMlDsa65Pem_SealsEnvelopeAndSignsChallenge()
    {
        // WHAT: Generates an unencrypted ML-DSA-65 keypair, formats PEM, seals via SealPrivateKeyPem, and signs challenge.
        // WHY:  Verifies import of post-quantum ML-DSA-65 PEM keys into sealed envelopes.
        // HOW:  Generates ML-DSA-65 pair, formats PEM, seals with passphrase, signs challenge, and verifies.
        const string passphrase = "seal-pqc-key-passphrase-99";
        GeneratedKeyBundle bundle = TestChallengeHelper.CreateTestKeyBundle("test_pqc", algorithm: KeyAlgorithm.MlDsa65);

        string sealedEnvelopeBase64 = _vault.SealPrivateKeyPem(bundle.PrivateKeyPem, passphrase);
        Assert.False(string.IsNullOrWhiteSpace(sealedEnvelopeBase64));

        byte[] nonce = new byte[32];
        RandomNumberGenerator.Fill(nonce);

        string signatureBase64 = _vault.SignChallenge("ML-DSA-65", sealedEnvelopeBase64, passphrase, nonce);
        Assert.False(string.IsNullOrWhiteSpace(signatureBase64));

        byte[] pubDer = ExtractDerFromPem(bundle.PublicKeyPem);
        bool verified = VerifyMlDsa65Signature(pubDer, nonce, signatureBase64);
        Assert.True(verified);
    }

    /// <summary>
    /// Verifies argument validation in <see cref="IClientKeyRingVault.SealPrivateKeyPem"/>.
    /// </summary>
    /// <param name="invalidPem">The invalid PEM string.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SealPrivateKeyPem_InvalidPem_ThrowsArgumentException(string? invalidPem)
    {
        // WHAT: Asserts null or empty PEM string throws ArgumentException.
        // WHY:  Prevents sealing missing key material.
        // HOW:  Calls SealPrivateKeyPem with invalid input.
        Assert.ThrowsAny<ArgumentException>(() => _vault.SealPrivateKeyPem(invalidPem!, "passphrase"));
    }

    /// <summary>
    /// Verifies passphrase validation in <see cref="IClientKeyRingVault.SealPrivateKeyPem"/>.
    /// </summary>
    /// <param name="invalidPassphrase">The invalid passphrase string.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SealPrivateKeyPem_InvalidPassphrase_ThrowsArgumentException(string invalidPassphrase)
    {
        // WHAT: Asserts empty or whitespace passphrase throws ArgumentException.
        // WHY:  Guarantees non-empty encryption keys.
        // HOW:  Calls SealPrivateKeyPem with blank passphrase.
        Assert.Throws<ArgumentException>(() => _vault.SealPrivateKeyPem("-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----", invalidPassphrase));
    }

    /// <summary>
    /// Verifies input argument validation on <see cref="IClientChallengeSigner.SignChallenge"/>.
    /// </summary>
    [Fact]
    public void SignChallenge_InvalidArguments_ThrowsExpectedExceptions()
    {
        // WHAT: Tests argument validation across all parameters in SignChallenge.
        // WHY:  Guarantees robust exception behavior.
        // HOW:  Tests null/unsupported algorithms, empty envelopes, blank passphrases, and empty nonces.
        byte[] validNonce = new byte[32];
        const string validEnvelope = "AAAAAAAA";

        Assert.ThrowsAny<ArgumentException>(() => _vault.SignChallenge(" ", validEnvelope, "pass", validNonce));
        Assert.Throws<NotSupportedException>(() => _vault.SignChallenge("RSA-2048", validEnvelope, "pass", validNonce));
        Assert.ThrowsAny<ArgumentException>(() => _vault.SignChallenge("Ed25519", " ", "pass", validNonce));
        Assert.Throws<ArgumentException>(() => _vault.SignChallenge("Ed25519", validEnvelope, " ", validNonce));
        Assert.Throws<ArgumentException>(() => _vault.SignChallenge("Ed25519", validEnvelope, "pass", ReadOnlySpan<byte>.Empty));
    }

    /// <summary>
    /// Verifies that signing with an incorrect passphrase fails with <see cref="CryptographicException"/>.
    /// </summary>
    [Fact]
    public void SignChallenge_IncorrectPassphrase_ThrowsCryptographicException()
    {
        // WHAT: Attempts challenge signing using the wrong unsealing passphrase.
        // WHY:  AES-256-GCM authentication must fail securely on authentication tag mismatch.
        // HOW:  Generates bundle with passphrase A, attempts signing with passphrase B, asserts CryptographicException.
        ClientKeyBundle bundle = _vault.GenerateKeyBundle("Ed25519", "correct-passphrase-alpha");
        byte[] nonce = new byte[32];

        Assert.ThrowsAny<CryptographicException>(() => _vault.SignChallenge(
            "Ed25519",
            bundle.EncryptedPrivateKeyEnvelopeBase64,
            "wrong-passphrase-beta",
            nonce));
    }

    /// <summary>
    /// Verifies that truncated or corrupted envelopes throw <see cref="CryptographicException"/>.
    /// </summary>
    [Fact]
    public void SignChallenge_TruncatedOrCorruptedEnvelope_ThrowsCryptographicException()
    {
        // WHAT: Passes truncated (< 44 bytes) or tampered envelope bytes to SignChallenge.
        // WHY:  Guarantees detection of envelope truncation or tampering.
        // HOW:  Tests envelope shorter than 44 bytes, and tests flipped ciphertext byte causing GCM tag mismatch.
        byte[] nonce = new byte[32];

        // Envelope shorter than minimum 44 bytes header overhead
        byte[] shortEnvelope = new byte[20];
        string shortB64 = Convert.ToBase64String(shortEnvelope);
        Assert.Throws<CryptographicException>(() => _vault.SignChallenge("Ed25519", shortB64, "passphrase", nonce));

        // Tampered ciphertext byte
        ClientKeyBundle bundle = _vault.GenerateKeyBundle("Ed25519", "test-passphrase");
        byte[] rawEnvelope = Convert.FromBase64String(bundle.EncryptedPrivateKeyEnvelopeBase64);
        rawEnvelope[^1] ^= 0xFF; // Corrupt ciphertext byte
        string tamperedB64 = Convert.ToBase64String(rawEnvelope);

        Assert.ThrowsAny<CryptographicException>(() => _vault.SignChallenge("Ed25519", tamperedB64, "test-passphrase", nonce));
    }

    /// <summary>
    /// Helper extracting raw DER bytes from an RFC 7468 PEM string.
    /// </summary>
    /// <param name="pem">The PEM formatted string.</param>
    /// <returns>Decoded byte array.</returns>
    private static byte[] ExtractDerFromPem(string pem)
    {
        // WHAT: Extracts DER bytes by stripping delimiters and base64-decoding.
        // WHY:  Helper for test signature verification.
        // HOW:  Enumerates lines via span, discards boundary markers, and decodes Base64.
        StringBuilder sb = new();
        foreach (ReadOnlySpan<char> line in pem.AsSpan().EnumerateLines())
        {
            ReadOnlySpan<char> trimmed = line.Trim();
            if (trimmed.IsEmpty || (trimmed.StartsWith("-----") && trimmed.EndsWith("-----")))
            {
                continue;
            }

            sb.Append(trimmed);
        }

        return Convert.FromBase64String(sb.ToString());
    }

    /// <summary>
    /// Helper verifying an Ed25519 signature using BouncyCastle.
    /// </summary>
    /// <param name="pubDer">The public key SPKI DER bytes.</param>
    /// <param name="data">The signed data bytes.</param>
    /// <param name="signatureBase64">The Base64-encoded signature.</param>
    /// <returns>True if signature matches; otherwise false.</returns>
    private static bool VerifyEd25519Signature(byte[] pubDer, byte[] data, string signatureBase64)
    {
        // WHAT: Verifies an Ed25519 signature.
        // WHY:  Confirms the signature produced by AesGcmClientKeyRingVault is mathematically valid.
        // HOW:  Initializes Ed25519Signer in verification mode with public key parameters.
        var pubKey = (Ed25519PublicKeyParameters)PublicKeyFactory.CreateKey(pubDer);
        Ed25519Signer verifier = new();
        verifier.Init(false, pubKey);
        verifier.BlockUpdate(data);
        byte[] sigBytes = Convert.FromBase64String(signatureBase64);
        return verifier.VerifySignature(sigBytes);
    }

    /// <summary>
    /// Helper verifying an ML-DSA-65 signature using BouncyCastle.
    /// </summary>
    /// <param name="pubDer">The public key SPKI DER bytes.</param>
    /// <param name="data">The signed data bytes.</param>
    /// <param name="signatureBase64">The Base64-encoded signature.</param>
    /// <returns>True if signature matches; otherwise false.</returns>
    private static bool VerifyMlDsa65Signature(byte[] pubDer, byte[] data, string signatureBase64)
    {
        // WHAT: Verifies an ML-DSA-65 signature.
        // WHY:  Confirms the post-quantum signature produced by AesGcmClientKeyRingVault is mathematically valid.
        // HOW:  Initializes MLDsaSigner in verification mode with public key parameters.
        var pubKey = (MLDsaPublicKeyParameters)PublicKeyFactory.CreateKey(pubDer);
        MLDsaSigner verifier = new(pubKey.Parameters, deterministic: true);
        verifier.Init(false, pubKey);
        verifier.BlockUpdate(data);
        byte[] sigBytes = Convert.FromBase64String(signatureBase64);
        return verifier.VerifySignature(sigBytes);
    }
}
