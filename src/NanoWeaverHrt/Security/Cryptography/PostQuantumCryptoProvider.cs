using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Kems;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using NanoWeaverHrt.Security.Models;

namespace NanoWeaverHrt.Security.Cryptography;

/// <summary>
/// Production implementation of <see cref="IPostQuantumCryptoProvider"/> leveraging NIST FIPS 203 (ML-KEM-768),
/// NIST FIPS 204 (ML-DSA-65), and authenticated AES-256-GCM symmetric encryption.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A post-quantum cryptographic engine for encrypting and signing sensitive health data at rest.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Classical RSA and ECC public-key schemes can be broken by Shor's quantum algorithm.
/// This provider uses Module-Lattice-Based Key Encapsulation (ML-KEM) and Signatures (ML-DSA) approved by NIST in 2024.
/// </para>
/// <para>
/// <strong>How it works:</strong>
/// 1. For at-rest encryption: Generates an ephemeral 256-bit symmetric key, encapsulates it via ML-KEM-768 to recipient public key,
///    and encrypts data using hardware-accelerated AES-256-GCM with a 96-bit unique nonce and 128-bit authentication tag.
/// 2. For digital signatures: Computes lattice-based ML-DSA-65 signatures guaranteeing non-repudiation for audit trails and export manifests.
/// </para>
/// </remarks>
public sealed class PostQuantumCryptoProvider : IPostQuantumCryptoProvider
{
    /// <summary>
    /// Thread-safe cryptographic pseudo-random number generator for lattice operations.
    /// </summary>
    private readonly SecureRandom _secureRandom = new();

    /// <summary>
    /// Standard tag size in bytes for AES-256-GCM authentication.
    /// </summary>
    private const int AesGcmTagSizeBytes = 16;

    /// <summary>
    /// Standard nonce size in bytes for AES-256-GCM (96 bits).
    /// </summary>
    private const int AesGcmNonceSizeBytes = 12;

    /// <summary>
    /// Generates a new ML-KEM-768 key pair for key encapsulation and envelope encryption.
    /// </summary>
    /// <param name="keyId">The unique identifier to assign to this key pair.</param>
    /// <returns>A new <see cref="PqcKeyMaterial"/> containing public and private lattice key parameters.</returns>
    public PqcKeyMaterial GenerateKemKeyPair(string keyId)
    {
        // WHAT: Generate an ML-KEM-768 key pair.
        // WHY:  Provides 128-bit quantum security level (NIST Security Category 3) for key encapsulation.
        // HOW:  Uses BouncyCastle MLKemKeyPairGenerator initialized with MLKemParameters.ml_kem_768.
        MLKemParameters kemParams = MLKemParameters.ml_kem_768;
        MLKemKeyPairGenerator keyGen = new();
        keyGen.Init(new MLKemKeyGenerationParameters(_secureRandom, kemParams));
        var keyPair = keyGen.GenerateKeyPair();

        byte[] pubDer = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(keyPair.Public).GetDerEncoded();
        byte[] privDer = PrivateKeyInfoFactory.CreatePrivateKeyInfo(keyPair.Private).GetDerEncoded();

        return new PqcKeyMaterial
        {
            KeyId = keyId,
            Algorithm = "ML-KEM-768",
            PublicKeyBase64 = Convert.ToBase64String(pubDer),
            PrivateKeyBase64 = Convert.ToBase64String(privDer),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Generates a new ML-DSA-65 key pair for tamper-evident digital signatures.
    /// </summary>
    /// <param name="keyId">The unique identifier to assign to this signing key pair.</param>
    /// <returns>A new <see cref="PqcKeyMaterial"/> containing public and private signing parameters.</returns>
    public PqcKeyMaterial GenerateSigningKeyPair(string keyId)
    {
        // WHAT: Generate an ML-DSA-65 key pair.
        // WHY:  Provides NIST Security Category 3 post-quantum digital signatures for FDA 21 CFR Part 11 logs.
        // HOW:  Uses BouncyCastle MLDsaKeyPairGenerator initialized with MLDsaParameters.ml_dsa_65.
        MLDsaParameters dsaParams = MLDsaParameters.ml_dsa_65;
        MLDsaKeyPairGenerator keyGen = new();
        keyGen.Init(new MLDsaKeyGenerationParameters(_secureRandom, dsaParams));
        var keyPair = keyGen.GenerateKeyPair();

        byte[] pubDer = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(keyPair.Public).GetDerEncoded();
        byte[] privDer = PrivateKeyInfoFactory.CreatePrivateKeyInfo(keyPair.Private).GetDerEncoded();

        return new PqcKeyMaterial
        {
            KeyId = keyId,
            Algorithm = "ML-DSA-65",
            PublicKeyBase64 = Convert.ToBase64String(pubDer),
            PrivateKeyBase64 = Convert.ToBase64String(privDer),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Encrypts raw plaintext bytes into an authenticated <see cref="EncryptedEnvelope"/> using ML-KEM-768 and AES-256-GCM.
    /// </summary>
    /// <param name="plaintext">The raw unencrypted data span.</param>
    /// <param name="recipientPublicKeyBase64">The recipient's Base64-encoded ML-KEM-768 public key.</param>
    /// <param name="keyId">The identifier of the recipient's key.</param>
    /// <returns>An authenticated <see cref="EncryptedEnvelope"/> container.</returns>
    public EncryptedEnvelope EncryptEnvelope(ReadOnlySpan<byte> plaintext, string recipientPublicKeyBase64, string keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientPublicKeyBase64);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        // WHAT: Parse recipient ML-KEM public key from Base64 DER bytes or raw FIPS 203 encoding.
        // WHY:  Enables key encapsulation to the recipient's public lattice identity.
        // HOW:  Decodes Base64 and parses via ParseKemPublicKey supporting raw 1184-byte and SPKI encodings.
        byte[] pubBytes = Convert.FromBase64String(recipientPublicKeyBase64);
        MLKemPublicKeyParameters recipientPubKey = ParseKemPublicKey(pubBytes);

        // WHAT: Encapsulate a fresh 256-bit shared secret key.
        // WHY:  Produces a quantum-secure symmetric key known only to sender and private key holder.
        // HOW:  MLKemEncapsulator encapsulates a random shared secret into an encapsulation buffer.
        MLKemEncapsulator encapsulator = new(recipientPubKey.Parameters);
        encapsulator.Init(recipientPubKey);

        byte[] encapsulation = new byte[encapsulator.EncapsulationLength];
        byte[] sharedSecret = new byte[encapsulator.SecretLength];
        encapsulator.Encapsulate(encapsulation, sharedSecret);

        try
        {
            // WHAT: Encrypt bulk plaintext using authenticated AES-256-GCM.
            // WHY:  AES-256 offers 128-bit quantum security against Grover's search algorithm with hardware SIMD speed.
            // HOW:  Uses 96-bit cryptographically random nonce and computes 128-bit authentication tag.
            byte[] nonce = new byte[AesGcmNonceSizeBytes];
            RandomNumberGenerator.Fill(nonce);

            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[AesGcmTagSizeBytes];
            byte[] associatedData = Encoding.UTF8.GetBytes($"{keyId}:ML-KEM-768+AES-256-GCM");

            using (AesGcm aesGcm = new(sharedSecret, AesGcmTagSizeBytes))
            {
                aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            }

            return new EncryptedEnvelope
            {
                Algorithm = "ML-KEM-768+AES-256-GCM",
                KeyId = keyId,
                EncapsulatedKeyBase64 = Convert.ToBase64String(encapsulation),
                CiphertextBase64 = Convert.ToBase64String(ciphertext),
                NonceBase64 = Convert.ToBase64String(nonce),
                TagBase64 = Convert.ToBase64String(tag),
                SealedAtUtc = DateTimeOffset.UtcNow
            };
        }
        finally
        {
            // WHAT: Zero out sensitive shared secret bytes from memory.
            // WHY:  Prevent ephemeral symmetric key leakage in heap dumps or crash reports.
            // HOW:  CryptographicOperations.ZeroMemory explicitly zeroes the buffer.
            CryptographicOperations.ZeroMemory(sharedSecret);
        }
    }

    /// <summary>
    /// Decrypts an authenticated <see cref="EncryptedEnvelope"/> using ML-KEM-768 decapsulation and AES-256-GCM.
    /// </summary>
    /// <param name="envelope">The encrypted envelope containing the encapsulated key, ciphertext, nonce, and tag.</param>
    /// <param name="recipientPrivateKeyBase64">The recipient's Base64-encoded ML-KEM-768 private key.</param>
    /// <returns>The decrypted plaintext byte array.</returns>
    public byte[] DecryptEnvelope(EncryptedEnvelope envelope, string recipientPrivateKeyBase64)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientPrivateKeyBase64);

        // WHAT: Parse recipient ML-KEM private key from Base64 DER bytes or raw FIPS 203 encoding.
        // WHY:  Enables decapsulation of the symmetric shared secret.
        // HOW:  Decodes Base64 and parses via ParseKemPrivateKey supporting raw 2400-byte and PKCS#8 encodings.
        byte[] privBytes = Convert.FromBase64String(recipientPrivateKeyBase64);
        MLKemPrivateKeyParameters recipientPrivKey = ParseKemPrivateKey(privBytes);

        // WHAT: Decapsulate the 256-bit shared secret key.
        // WHY:  Recovers the exact symmetric key used during envelope encryption.
        // HOW:  MLKemDecapsulator consumes the encapsulation ciphertext to reconstruct the shared secret.
        MLKemDecapsulator decapsulator = new(recipientPrivKey.Parameters);
        decapsulator.Init(recipientPrivKey);

        byte[] encapsulation = Convert.FromBase64String(envelope.EncapsulatedKeyBase64);
        byte[] sharedSecret = new byte[decapsulator.SecretLength];
        decapsulator.Decapsulate(encapsulation, sharedSecret);

        try
        {
            // WHAT: Authenticate and decrypt the ciphertext payload using AES-256-GCM.
            // WHY:  Verifies that ciphertext and associated data have not been tampered with.
            // HOW:  Uses AesGcm.Decrypt with the decapsulated secret, nonce, tag, and associated data.
            byte[] nonce = Convert.FromBase64String(envelope.NonceBase64);
            byte[] ciphertext = Convert.FromBase64String(envelope.CiphertextBase64);
            byte[] tag = Convert.FromBase64String(envelope.TagBase64);
            byte[] associatedData = Encoding.UTF8.GetBytes($"{envelope.KeyId}:ML-KEM-768+AES-256-GCM");

            byte[] plaintext = new byte[ciphertext.Length];
            using (AesGcm aesGcm = new(sharedSecret, AesGcmTagSizeBytes))
            {
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            }

            return plaintext;
        }
        finally
        {
            // WHAT: Zero out sensitive shared secret bytes from memory.
            // WHY:  Prevent ephemeral symmetric key leakage.
            // HOW:  CryptographicOperations.ZeroMemory explicitly zeroes the buffer.
            CryptographicOperations.ZeroMemory(sharedSecret);
        }
    }

    /// <summary>
    /// Signs raw data using NIST FIPS 204 ML-DSA-65 post-quantum digital signatures.
    /// </summary>
    /// <param name="data">The raw data span to sign.</param>
    /// <param name="signerPrivateKeyBase64">The signer's Base64-encoded ML-DSA-65 private key.</param>
    /// <param name="signerKeyId">The identifier of the signer's key.</param>
    /// <returns>A tamper-evident <see cref="PqcSignature"/> record.</returns>
    public PqcSignature SignData(ReadOnlySpan<byte> data, string signerPrivateKeyBase64, string signerKeyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signerPrivateKeyBase64);
        ArgumentException.ThrowIfNullOrWhiteSpace(signerKeyId);

        // WHAT: Parse signer ML-DSA private key.
        // WHY:  Produces a cryptographically unforgeable post-quantum signature.
        // HOW:  Uses PrivateKeyFactory to construct MLDsaPrivateKeyParameters.
        byte[] privBytes = Convert.FromBase64String(signerPrivateKeyBase64);
        var signerPrivKey = (MLDsaPrivateKeyParameters)PrivateKeyFactory.CreateKey(privBytes);

        // WHAT: Compute deterministic ML-DSA signature over input data span.
        // WHY:  Guarantees non-repudiation and audit log tamper detection.
        // HOW:  MLDsaSigner updates buffer and generates raw signature bytes.
        MLDsaSigner signer = new(signerPrivKey.Parameters, deterministic: true);
        signer.Init(true, signerPrivKey);
        signer.BlockUpdate(data);
        byte[] signature = signer.GenerateSignature();

        return new PqcSignature
        {
            Algorithm = "ML-DSA-65",
            SignerKeyId = signerKeyId,
            SignatureBase64 = Convert.ToBase64String(signature),
            SignedAtUtc = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Verifies an ML-DSA-65 post-quantum digital signature against raw data.
    /// </summary>
    /// <param name="data">The raw data span that was signed.</param>
    /// <param name="signatureBase64">The Base64-encoded digital signature bytes.</param>
    /// <param name="signerPublicKeyBase64">The signer's Base64-encoded ML-DSA-65 public key.</param>
    /// <returns><c>true</c> if the signature is cryptographically valid and matches the data; otherwise, <c>false</c>.</returns>
    public bool VerifySignature(ReadOnlySpan<byte> data, string signatureBase64, string signerPublicKeyBase64)
    {
        if (string.IsNullOrWhiteSpace(signatureBase64) || string.IsNullOrWhiteSpace(signerPublicKeyBase64))
        {
            return false;
        }

        try
        {
            // WHAT: Parse signer ML-DSA public key.
            // WHY:  Verifies authenticity without access to private signing key.
            // HOW:  Uses PublicKeyFactory to construct MLDsaPublicKeyParameters.
            byte[] pubBytes = Convert.FromBase64String(signerPublicKeyBase64);
            var signerPubKey = (MLDsaPublicKeyParameters)PublicKeyFactory.CreateKey(pubBytes);

            byte[] signature = Convert.FromBase64String(signatureBase64);

            MLDsaSigner verifier = new(signerPubKey.Parameters, deterministic: true);
            verifier.Init(false, signerPubKey);
            verifier.BlockUpdate(data);

            return verifier.VerifySignature(signature);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Deterministically expands a 32-byte FIPS 204 seed into an ML-DSA-65 signing key pair.
    /// </summary>
    /// <param name="seed">The 32-byte FIPS 204 internal key-generation seed (ξ).</param>
    /// <returns>
    /// A <see cref="PqcKeyMaterial"/> with <c>KeyId</c> set to <see cref="PublicKeyFingerprint.ComputeKeyId"/> of the
    /// public key's SubjectPublicKeyInfo DER, <c>Algorithm</c> set to <c>"ML-DSA-65"</c>, and <c>CreatedAtUtc</c> set
    /// to the UTC time when the key pair was derived.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="seed"/> is not exactly 32 bytes long.</exception>
    public PqcKeyMaterial DeriveSigningKeyPairFromSeed(ReadOnlySpan<byte> seed)
    {
        if (seed.Length != 32)
        {
            throw new ArgumentException("The ML-DSA-65 seed must be exactly 32 bytes.", nameof(seed));
        }

        // WHAT: Expand the 32-byte FIPS 204 seed into a deterministic ML-DSA-65 key pair and zero the seed copy.
        // WHY:  Allows the node's persistent 32-byte signing key file to reconstruct the exact same ML-DSA-65 key pair across restarts.
        // HOW:  Copies the seed span to a temporary array for BouncyCastle MLDsaPrivateKeyParameters.FromSeed, encodes SPKI and PKCS#8 DER, and zeroes the copy in finally.
        byte[] seedCopy = seed.ToArray();
        try
        {
            MLDsaPrivateKeyParameters privateKey = MLDsaPrivateKeyParameters.FromSeed(MLDsaParameters.ml_dsa_65, seedCopy);
            MLDsaPublicKeyParameters publicKey = privateKey.GetPublicKey();

            byte[] pubDer = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(publicKey).GetDerEncoded();
            byte[] privDer = PrivateKeyInfoFactory.CreatePrivateKeyInfo(privateKey).GetDerEncoded();

            return new PqcKeyMaterial
            {
                KeyId = PublicKeyFingerprint.ComputeKeyId(pubDer),
                Algorithm = "ML-DSA-65",
                PublicKeyBase64 = Convert.ToBase64String(pubDer),
                PrivateKeyBase64 = Convert.ToBase64String(privDer),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(seedCopy);
        }
    }

    /// <summary>
    /// Generates a raw FIPS 203 ML-KEM-768 key pair producing standard byte lengths.
    /// </summary>
    /// <returns>A tuple of raw 1184-byte encapsulation key and 2400-byte decapsulation key.</returns>
    public (byte[] EncapsulationKey, byte[] DecapsulationKey) GenerateKemRawKeyPair()
    {
        // WHAT: Generates an ML-KEM-768 key pair and extracts raw FIPS 203 wire encodings.
        // WHY:  Standard FIPS 203 defines 1184-byte encapsulation key and 2400-byte decapsulation key.
        // HOW:  Uses BouncyCastle MLKemKeyPairGenerator with MLKemParameters.ml_kem_768 and calls GetEncoded().
        MLKemKeyPairGenerator keyGen = new();
        keyGen.Init(new MLKemKeyGenerationParameters(_secureRandom, MLKemParameters.ml_kem_768));
        var keyPair = keyGen.GenerateKeyPair();

        var pubKey = (MLKemPublicKeyParameters)keyPair.Public;
        var privKey = (MLKemPrivateKeyParameters)keyPair.Private;

        return (pubKey.GetEncoded(), privKey.GetEncoded());
    }

    /// <summary>
    /// Encapsulates a fresh shared secret against an ML-KEM-768 encapsulation key.
    /// </summary>
    /// <param name="encapsulationKey">The recipient's encapsulation key bytes (raw 1184 bytes or SPKI DER).</param>
    /// <returns>A tuple containing the 1088-byte ciphertext and 32-byte shared secret.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="encapsulationKey"/> is null.</exception>
    public (byte[] Ciphertext, byte[] SharedSecret) Encapsulate(byte[] encapsulationKey)
    {
        ArgumentNullException.ThrowIfNull(encapsulationKey);

        // WHAT: Encapsulates a fresh 256-bit shared secret using FIPS 203 ML-KEM-768.
        // WHY:  Produces standard 1088-byte ciphertext and 32-byte shared secret without leaking secret to eavesdroppers.
        // HOW:  Constructs MLKemPublicKeyParameters, initializes MLKemEncapsulator, and extracts encapsulation and secret.
        MLKemPublicKeyParameters pubKey = ParseKemPublicKey(encapsulationKey);
        MLKemEncapsulator encapsulator = new(pubKey.Parameters);
        encapsulator.Init(pubKey);

        byte[] ciphertext = new byte[encapsulator.EncapsulationLength];
        byte[] sharedSecret = new byte[encapsulator.SecretLength];
        encapsulator.Encapsulate(ciphertext, sharedSecret);

        return (ciphertext, sharedSecret);
    }

    /// <summary>
    /// Decapsulates an ML-KEM-768 ciphertext using an ML-KEM-768 decapsulation key.
    /// </summary>
    /// <param name="decapsulationKey">The decapsulation key bytes (raw 2400 bytes or PKCS#8 DER).</param>
    /// <param name="ciphertext">The 1088-byte encapsulation ciphertext.</param>
    /// <returns>The recovered 32-byte shared secret (or an implicitly rejected pseudo-random secret if ciphertext or key is invalid).</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="decapsulationKey"/> or <paramref name="ciphertext"/> is null.</exception>
    public byte[] Decapsulate(byte[] decapsulationKey, byte[] ciphertext)
    {
        ArgumentNullException.ThrowIfNull(decapsulationKey);
        ArgumentNullException.ThrowIfNull(ciphertext);

        // WHAT: Recovers shared secret from ML-KEM-768 ciphertext using decapsulation key.
        // WHY:  Standard FIPS 203 decapsulation provides implicit rejection on invalid/tampered ciphertext.
        // HOW:  Constructs MLKemPrivateKeyParameters, initializes MLKemDecapsulator, and decapsulates ciphertext.
        MLKemPrivateKeyParameters privKey = ParseKemPrivateKey(decapsulationKey);
        MLKemDecapsulator decapsulator = new(privKey.Parameters);
        decapsulator.Init(privKey);

        byte[] sharedSecret = new byte[decapsulator.SecretLength];
        decapsulator.Decapsulate(ciphertext, sharedSecret);

        return sharedSecret;
    }

    /// <summary>
    /// Fallback alias generating an ML-KEM-768 key pair.
    /// </summary>
    /// <param name="keyId">The unique key identifier.</param>
    /// <returns>A new <see cref="PqcKeyMaterial"/> record.</returns>
    public PqcKeyMaterial GenerateKemFallbackKeyPair(string keyId) => GenerateKemKeyPair(keyId);

    /// <summary>
    /// Fallback alias encapsulating a shared secret against an ML-KEM-768 encapsulation key.
    /// </summary>
    /// <param name="encapsulationKey">The recipient's encapsulation key bytes.</param>
    /// <returns>A tuple containing the ciphertext and shared secret.</returns>
    public (byte[] Ciphertext, byte[] SharedSecret) EncapsulateKemFallback(byte[] encapsulationKey) => Encapsulate(encapsulationKey);

    /// <summary>
    /// Fallback alias decapsulating an ML-KEM-768 ciphertext.
    /// </summary>
    /// <param name="decapsulationKey">The decapsulation key bytes.</param>
    /// <param name="ciphertext">The encapsulation ciphertext.</param>
    /// <returns>The recovered shared secret.</returns>
    public byte[] DecapsulateKemFallback(byte[] decapsulationKey, byte[] ciphertext) => Decapsulate(decapsulationKey, ciphertext);

    /// <summary>
    /// Parses an ML-KEM-768 public key from either raw 1184-byte encoding or SubjectPublicKeyInfo ASN.1 DER.
    /// </summary>
    /// <param name="keyBytes">The raw or SPKI-encoded public key bytes.</param>
    /// <returns>An initialized <see cref="MLKemPublicKeyParameters"/> instance.</returns>
    private static MLKemPublicKeyParameters ParseKemPublicKey(byte[] keyBytes)
    {
        // WHAT: Parses ML-KEM-768 public key supporting both raw FIPS 203 wire encoding and X.509 SPKI DER.
        // WHY:  Allows wire formats (1184 bytes) and standard ASN.1 certificates/envelopes to be consumed transparently.
        // HOW:  Checks byte length; if 1184 bytes, uses MLKemPublicKeyParameters.FromEncoding, otherwise PublicKeyFactory.
        if (keyBytes.Length == 1184)
        {
            return MLKemPublicKeyParameters.FromEncoding(MLKemParameters.ml_kem_768, keyBytes);
        }

        return (MLKemPublicKeyParameters)PublicKeyFactory.CreateKey(keyBytes);
    }

    /// <summary>
    /// Parses an ML-KEM-768 private key from either raw 2400-byte encoding or PKCS#8 ASN.1 DER.
    /// </summary>
    /// <param name="keyBytes">The raw or PKCS#8-encoded private key bytes.</param>
    /// <returns>An initialized <see cref="MLKemPrivateKeyParameters"/> instance.</returns>
    private static MLKemPrivateKeyParameters ParseKemPrivateKey(byte[] keyBytes)
    {
        // WHAT: Parses ML-KEM-768 private key supporting both raw FIPS 203 wire encoding and PKCS#8 DER.
        // WHY:  Allows wire formats (2400 bytes) and PKCS#8 persisted key envelopes to be consumed transparently.
        // HOW:  Checks byte length; if 2400 bytes, uses MLKemPrivateKeyParameters.FromEncoding, otherwise PrivateKeyFactory.
        if (keyBytes.Length == 2400)
        {
            return MLKemPrivateKeyParameters.FromEncoding(MLKemParameters.ml_kem_768, keyBytes);
        }

        return (MLKemPrivateKeyParameters)PrivateKeyFactory.CreateKey(keyBytes);
    }
}
