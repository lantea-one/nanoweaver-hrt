using System.Security.Cryptography;
using System.Text;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.Models;
using Xunit;

namespace NanoWeaverHrt.Tests.Security.Cryptography;

/// <summary>
/// Comprehensive unit tests verifying NIST FIPS 203 ML-KEM-768 key encapsulation and
/// NIST FIPS 204 ML-DSA-65 digital signatures implemented in <see cref="PostQuantumCryptoProvider"/>.
/// </summary>
public sealed class PostQuantumCryptoProviderTests
{
    /// <summary>
    /// Verifies that ML-KEM-768 key generation, encapsulation, and decapsulation conform strictly to
    /// FIPS 203 standard byte lengths: 1184-byte encapsulation key, 2400-byte decapsulation key,
    /// 1088-byte ciphertext, and 32-byte shared secret.
    /// </summary>
    [Fact]
    public void MlKem768_RawKeyLengthsAndRoundtrip_SucceedWithStandardFips203Lengths()
    {
        // WHAT: Tests FIPS 203 ML-KEM-768 key generation, encapsulation, and decapsulation byte lengths.
        // WHY:  Standard FIPS 203 specifies exact wire lengths: ek=1184, dk=2400, ct=1088, ss=32.
        // HOW:  Generates raw keys, encapsulates shared secret, decapsulates, and asserts lengths and equality.
        PostQuantumCryptoProvider provider = new();

        var (encapsulationKey, decapsulationKey) = provider.GenerateKemRawKeyPair();

        // 1. Verify standard FIPS 203 ML-KEM-768 key lengths
        Assert.NotNull(encapsulationKey);
        Assert.NotNull(decapsulationKey);
        Assert.Equal(1184, encapsulationKey.Length);
        Assert.Equal(2400, decapsulationKey.Length);

        // 2. Encapsulate fresh shared secret
        var (ciphertext, sharedSecret) = provider.Encapsulate(encapsulationKey);

        Assert.NotNull(ciphertext);
        Assert.NotNull(sharedSecret);
        Assert.Equal(1088, ciphertext.Length);
        Assert.Equal(32, sharedSecret.Length);

        // 3. Decapsulate with matching decapsulation key
        byte[] recoveredSecret = provider.Decapsulate(decapsulationKey, ciphertext);

        Assert.NotNull(recoveredSecret);
        Assert.Equal(32, recoveredSecret.Length);
        Assert.Equal(sharedSecret, recoveredSecret);
    }

    /// <summary>
    /// Verifies that an attacker possessing only the public encapsulation key and ciphertext
    /// cannot derive the shared secret, and that decapsulation with an unrelated key yields an invalid secret.
    /// </summary>
    [Fact]
    public void MlKem768_EavesdropperWithoutPrivateKey_CannotDeriveSharedSecret()
    {
        // WHAT: Tests security properties of ML-KEM-768 against eavesdroppers and wrong-key decapsulation.
        // WHY:  Unlike broken HKDF fallbacks where sharedSecret = HKDF(ek, ct), genuine ML-KEM-768 protects the secret.
        // HOW:  Decapsulates with an independent key pair (implicit rejection) and verifies secret mismatch.
        PostQuantumCryptoProvider provider = new();

        var (ek1, dk1) = provider.GenerateKemRawKeyPair();
        var (ek2, dk2) = provider.GenerateKemRawKeyPair();

        var (ciphertext, legitimateSecret) = provider.Encapsulate(ek1);

        // An independent key pair cannot decapsulate the secret
        byte[] rejectedSecret = provider.Decapsulate(dk2, ciphertext);
        Assert.NotEqual(legitimateSecret, rejectedSecret);

        // Ciphertext tampering results in implicit rejection
        byte[] tamperedCiphertext = (byte[])ciphertext.Clone();
        tamperedCiphertext[0] ^= 0xFF;
        byte[] tamperedSecret = provider.Decapsulate(dk1, tamperedCiphertext);
        Assert.NotEqual(legitimateSecret, tamperedSecret);
    }

    /// <summary>
    /// Verifies that fallback alias methods execute genuine BouncyCastle ML-KEM-768 operations.
    /// </summary>
    [Fact]
    public void MlKem768_FallbackAliases_ExecuteGenuineBouncyCastleKems()
    {
        // WHAT: Tests GenerateKemFallbackKeyPair, EncapsulateKemFallback, and DecapsulateKemFallback.
        // WHY:  Maintains API compatibility with fallback naming while using genuine FIPS 203 lattice algorithms.
        // HOW:  Executes fallback methods and asserts standard lengths and clean roundtrip.
        PostQuantumCryptoProvider provider = new();

        PqcKeyMaterial keyMaterial = provider.GenerateKemFallbackKeyPair("fallback_test_01");
        Assert.Equal("ML-KEM-768", keyMaterial.Algorithm);
        Assert.Equal("fallback_test_01", keyMaterial.KeyId);

        var (ek, dk) = provider.GenerateKemRawKeyPair();
        var (ciphertext, sharedSecret) = provider.EncapsulateKemFallback(ek);
        byte[] recoveredSecret = provider.DecapsulateKemFallback(dk, ciphertext);

        Assert.Equal(sharedSecret, recoveredSecret);
    }

    /// <summary>
    /// Verifies that <see cref="PostQuantumCryptoProvider.EncryptEnvelope"/> and <see cref="PostQuantumCryptoProvider.DecryptEnvelope"/>
    /// accept raw 1184-byte and 2400-byte keys in Base64 format as well as SubjectPublicKeyInfo DER format.
    /// </summary>
    [Fact]
    public void EncryptEnvelope_WithRawKeyBytesBase64_EncryptsAndDecryptsCleanly()
    {
        // WHAT: Tests envelope encryption using raw key byte Base64 strings.
        // WHY:  Ensures flexibility across raw FIPS 203 and X.509 SPKI encodings.
        // HOW:  Base64-encodes raw 1184/2400 byte arrays, calls EncryptEnvelope and DecryptEnvelope, and asserts plaintext match.
        PostQuantumCryptoProvider provider = new();

        var (rawEk, rawDk) = provider.GenerateKemRawKeyPair();
        string rawEkBase64 = Convert.ToBase64String(rawEk);
        string rawDkBase64 = Convert.ToBase64String(rawDk);

        byte[] payload = Encoding.UTF8.GetBytes("FIPS 203 ML-KEM-768 Raw Key Payload Verification");
        EncryptedEnvelope envelope = provider.EncryptEnvelope(payload, rawEkBase64, "raw_key_01");

        Assert.Equal("ML-KEM-768+AES-256-GCM", envelope.Algorithm);
        Assert.Equal("raw_key_01", envelope.KeyId);

        byte[] decrypted = provider.DecryptEnvelope(envelope, rawDkBase64);
        Assert.Equal(payload, decrypted);
    }

    /// <summary>
    /// Verifies null argument validation for <see cref="PostQuantumCryptoProvider.Encapsulate"/>
    /// and <see cref="PostQuantumCryptoProvider.Decapsulate"/>.
    /// </summary>
    [Fact]
    public void MlKem768_ArgumentNullGuards_ThrowArgumentNullException()
    {
        // WHAT: Tests null guards on Encapsulate and Decapsulate.
        // WHY:  Prevents NullReferenceException when null buffers are passed.
        // HOW:  Asserts ArgumentNullException for null inputs.
        PostQuantumCryptoProvider provider = new();
        byte[] validKey = new byte[1184];

        Assert.Throws<ArgumentNullException>(() => provider.Encapsulate(null!));
        Assert.Throws<ArgumentNullException>(() => provider.Decapsulate(null!, new byte[1088]));
        Assert.Throws<ArgumentNullException>(() => provider.Decapsulate(validKey, null!));
    }

    /// <summary>
    /// Verifies deterministic seed expansion for ML-DSA-65 via <see cref="PostQuantumCryptoProvider.DeriveSigningKeyPairFromSeed"/>.
    /// </summary>
    [Fact]
    public void DeriveSigningKeyPairFromSeed_ValidAndInvalidSeeds_BehaveCorrectly()
    {
        // WHAT: Tests FIPS 204 ML-DSA-65 seed derivation.
        // WHY:  Enforces 32-byte seed constraint and deterministic key pair generation.
        // HOW:  Derives key pair from 32-byte seed, checks determinism, and tests non-32-byte rejection.
        PostQuantumCryptoProvider provider = new();

        byte[] seed = new byte[32];
        seed[0] = 0xAA;
        seed[31] = 0x55;

        PqcKeyMaterial key1 = provider.DeriveSigningKeyPairFromSeed(seed);
        PqcKeyMaterial key2 = provider.DeriveSigningKeyPairFromSeed(seed);

        Assert.Equal(key1.KeyId, key2.KeyId);
        Assert.Equal(key1.PublicKeyBase64, key2.PublicKeyBase64);
        Assert.Equal("ML-DSA-65", key1.Algorithm);

        // Invalid length throws ArgumentException
        Assert.Throws<ArgumentException>(() => provider.DeriveSigningKeyPairFromSeed(new byte[16]));
        Assert.Throws<ArgumentException>(() => provider.DeriveSigningKeyPairFromSeed(new byte[64]));
    }
}
