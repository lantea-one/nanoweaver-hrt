using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Kems;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using WeaverLammpsHrt.TrajectoryParser.Security.Models;

namespace WeaverLammpsHrt.TrajectoryParser.Security.Cryptography;

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

        // WHAT: Parse recipient ML-KEM public key from Base64 DER bytes.
        // WHY:  Enables key encapsulation to the recipient's public lattice identity.
        // HOW:  Decodes Base64 and constructs MLKemPublicKeyParameters via PublicKeyFactory.
        byte[] pubBytes = Convert.FromBase64String(recipientPublicKeyBase64);
        var recipientPubKey = (MLKemPublicKeyParameters)PublicKeyFactory.CreateKey(pubBytes);

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

        // WHAT: Parse recipient ML-KEM private key from Base64 DER bytes.
        // WHY:  Enables decapsulation of the symmetric shared secret.
        // HOW:  Decodes Base64 and constructs MLKemPrivateKeyParameters via PrivateKeyFactory.
        byte[] privBytes = Convert.FromBase64String(recipientPrivateKeyBase64);
        var recipientPrivKey = (MLKemPrivateKeyParameters)PrivateKeyFactory.CreateKey(privBytes);

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
}
