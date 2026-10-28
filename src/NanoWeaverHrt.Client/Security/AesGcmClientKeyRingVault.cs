using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace NanoWeaverHrt.Client.Security;

/// <summary>
/// Production implementation of <see cref="IClientKeyRingVault"/> providing client-side key generation,
/// AES-256-GCM envelope sealing with PBKDF2-SHA256 derivation, and challenge signing for Ed25519 and ML-DSA-65.
/// </summary>
public sealed class AesGcmClientKeyRingVault : IClientKeyRingVault
{
    /// <summary>
    /// Standard salt size in bytes for PBKDF2-SHA256 key derivation.
    /// </summary>
    private const int SaltSizeBytes = 16;

    /// <summary>
    /// Standard initialization vector / nonce size in bytes for AES-256-GCM (96 bits).
    /// </summary>
    private const int NonceSizeBytes = 12;

    /// <summary>
    /// Standard authentication tag size in bytes for AES-256-GCM (128 bits).
    /// </summary>
    private const int TagSizeBytes = 16;

    /// <summary>
    /// Minimum header overhead size in bytes for the sovereign envelope ([salt 16][nonce 12][tag 16]).
    /// </summary>
    private const int EnvelopeHeaderLength = SaltSizeBytes + NonceSizeBytes + TagSizeBytes;

    /// <summary>
    /// Number of PBKDF2-SHA256 iterations required for master key derivation.
    /// </summary>
    private const int Pbkdf2Iterations = 100_000;

    /// <summary>
    /// Length of the derived AES-256 symmetric key in bytes (256 bits).
    /// </summary>
    private const int DerivedKeySizeBytes = 32;

    /// <summary>
    /// Thread-safe cryptographic pseudo-random number generator for BouncyCastle key generators.
    /// </summary>
    private readonly SecureRandom _secureRandom = new();

    /// <inheritdoc/>
    public ClientKeyBundle GenerateKeyBundle(string keyAlgorithm, ReadOnlySpan<char> passphrase)
    {
        // WHAT: Generates an asymmetric keypair in memory, formats public key PEM and fingerprint, and seals private key.
        // WHY:  Allows client applications to create Ed25519 or post-quantum ML-DSA-65 credentials with zero server dependency.
        // HOW:  Uses BouncyCastle key generators, derives SPKI/PKCS#8 DER, seals private key with AES-256-GCM, and zeroes raw private DER.
        string canonicalAlgorithm = NormalizeKeyAlgorithm(keyAlgorithm);
        ValidatePassphrase(passphrase);

        byte[] pubDer;
        byte[] privDer;

        if (canonicalAlgorithm == "Ed25519")
        {
            Ed25519KeyPairGenerator generator = new();
            generator.Init(new Ed25519KeyGenerationParameters(_secureRandom));
            AsymmetricCipherKeyPair pair = generator.GenerateKeyPair();
            pubDer = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pair.Public).GetDerEncoded();
            privDer = PrivateKeyInfoFactory.CreatePrivateKeyInfo(pair.Private).GetDerEncoded();
        }
        else
        {
            MLDsaKeyPairGenerator generator = new();
            generator.Init(new MLDsaKeyGenerationParameters(_secureRandom, MLDsaParameters.ml_dsa_65));
            AsymmetricCipherKeyPair pair = generator.GenerateKeyPair();
            pubDer = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pair.Public).GetDerEncoded();
            privDer = PrivateKeyInfoFactory.CreatePrivateKeyInfo(pair.Private).GetDerEncoded();
        }

        try
        {
            string pubPem = FormatPem("PUBLIC KEY", pubDer);
            string fingerprint = ComputeSha256Fingerprint(pubDer);
            byte[] sealedEnvelope = SealBytes(privDer, passphrase);
            string envelopeBase64 = Convert.ToBase64String(sealedEnvelope);

            return new ClientKeyBundle(
                canonicalAlgorithm,
                pubPem,
                envelopeBase64,
                fingerprint);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privDer);
        }
    }

    /// <inheritdoc/>
    public string SealPrivateKeyPem(string privateKeyPem, ReadOnlySpan<char> passphrase)
    {
        // WHAT: Converts a PEM-encoded unencrypted PKCS#8 private key into a sealed AES-256-GCM envelope.
        // WHY:  Enables importing existing unencrypted private keys into sovereign encrypted storage.
        // HOW:  Extracts PKCS#8 DER from PEM framing, seals via SealBytes, and zeroes raw DER in a finally block.
        if (string.IsNullOrWhiteSpace(privateKeyPem))
        {
            throw new ArgumentException("Private key PEM cannot be empty or whitespace.", nameof(privateKeyPem));
        }

        ValidatePassphrase(passphrase);

        byte[] privDer = ExtractDerFromPem(privateKeyPem);
        try
        {
            byte[] sealedEnvelope = SealBytes(privDer, passphrase);
            return Convert.ToBase64String(sealedEnvelope);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privDer);
        }
    }

    /// <inheritdoc/>
    public string SignChallenge(
        string keyAlgorithm,
        string encryptedEnvelopeBase64,
        ReadOnlySpan<char> passphrase,
        ReadOnlySpan<byte> challengeNonce)
    {
        // WHAT: Unseals an AES-256-GCM private key envelope and signs raw challenge nonce bytes.
        // WHY:  Executes client-side authentication challenge-response handshake without exposing private keys.
        // HOW:  Decrypts sealed envelope using PBKDF2-derived key, signs nonce with BouncyCastle signer, and zeroes DER in finally.
        string canonicalAlgorithm = NormalizeKeyAlgorithm(keyAlgorithm);

        if (string.IsNullOrWhiteSpace(encryptedEnvelopeBase64))
        {
            throw new ArgumentException("Encrypted envelope Base64 cannot be empty or whitespace.", nameof(encryptedEnvelopeBase64));
        }

        ValidatePassphrase(passphrase);

        if (challengeNonce.IsEmpty)
        {
            throw new ArgumentException("Challenge nonce cannot be empty.", nameof(challengeNonce));
        }

        byte[] envelopeBytes = Convert.FromBase64String(encryptedEnvelopeBase64);
        byte[] privDer = UnsealBytes(envelopeBytes, passphrase);

        try
        {
            if (canonicalAlgorithm == "Ed25519")
            {
                var privKey = (Ed25519PrivateKeyParameters)PrivateKeyFactory.CreateKey(privDer);
                Ed25519Signer signer = new();
                signer.Init(true, privKey);
                signer.BlockUpdate(challengeNonce);
                byte[] signature = signer.GenerateSignature();
                return Convert.ToBase64String(signature);
            }
            else
            {
                var privKey = (MLDsaPrivateKeyParameters)PrivateKeyFactory.CreateKey(privDer);
                MLDsaSigner signer = new(privKey.Parameters, deterministic: true);
                signer.Init(true, privKey);
                signer.BlockUpdate(challengeNonce);
                byte[] signature = signer.GenerateSignature();
                return Convert.ToBase64String(signature);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privDer);
        }
    }

    /// <summary>
    /// Derives a 256-bit symmetric encryption key from a passphrase and salt using PBKDF2-SHA256.
    /// </summary>
    /// <param name="passphrase">User passphrase.</param>
    /// <param name="salt">16-byte random salt.</param>
    /// <param name="destinationKey">32-byte destination buffer for the derived key.</param>
    private static void DeriveWrappingKey(ReadOnlySpan<char> passphrase, ReadOnlySpan<byte> salt, Span<byte> destinationKey)
    {
        // WHAT: Derives a 256-bit AES key from the passphrase and salt using PBKDF2-SHA256 with 100,000 iterations.
        // WHY:  Standard sovereign key derivation matching client and server encryption layouts.
        // HOW:  Invokes Rfc2898DeriveBytes.Pbkdf2 writing directly into the stack-allocated destination span.
        Rfc2898DeriveBytes.Pbkdf2(
            passphrase,
            salt,
            destinationKey,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256);
    }

    /// <summary>
    /// Encrypts raw plaintext bytes into an authenticated AES-256-GCM envelope ([salt 16][nonce 12][tag 16][ciphertext]).
    /// </summary>
    /// <param name="plaintext">The raw unencrypted data span.</param>
    /// <param name="passphrase">The user passphrase for key derivation.</param>
    /// <returns>Concatenated envelope byte array.</returns>
    private static byte[] SealBytes(ReadOnlySpan<byte> plaintext, ReadOnlySpan<char> passphrase)
    {
        // WHAT: Encrypts raw bytes using AES-256-GCM with a PBKDF2-SHA256 derived key and produces sovereign envelope.
        // WHY:  Secures private key material at rest on desktop and mobile platforms.
        // HOW:  Generates random salt and nonce, derives 256-bit key on stack, encrypts with salt as associatedData, zeroes key.
        byte[] salt = new byte[SaltSizeBytes];
        byte[] nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(salt);
        RandomNumberGenerator.Fill(nonce);

        Span<byte> wrappingKey = stackalloc byte[DerivedKeySizeBytes];
        try
        {
            DeriveWrappingKey(passphrase, salt, wrappingKey);

            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[TagSizeBytes];

            using (AesGcm aesGcm = new(wrappingKey, TagSizeBytes))
            {
                aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData: salt);
            }

            byte[] envelope = new byte[EnvelopeHeaderLength + ciphertext.Length];
            Buffer.BlockCopy(salt, 0, envelope, 0, SaltSizeBytes);
            Buffer.BlockCopy(nonce, 0, envelope, SaltSizeBytes, NonceSizeBytes);
            Buffer.BlockCopy(tag, 0, envelope, SaltSizeBytes + NonceSizeBytes, TagSizeBytes);
            Buffer.BlockCopy(ciphertext, 0, envelope, EnvelopeHeaderLength, ciphertext.Length);

            return envelope;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    /// <summary>
    /// Decrypts an authenticated AES-256-GCM envelope ([salt 16][nonce 12][tag 16][ciphertext]) into raw plaintext bytes.
    /// </summary>
    /// <param name="envelopeBytes">The full envelope byte array.</param>
    /// <param name="passphrase">The user passphrase for key derivation.</param>
    /// <returns>Decrypted raw plaintext byte array.</returns>
    private static byte[] UnsealBytes(byte[] envelopeBytes, ReadOnlySpan<char> passphrase)
    {
        // WHAT: Authenticates and decrypts an AES-256-GCM envelope into raw plaintext bytes.
        // WHY:  Restores PKCS#8 private key DER bytes for challenge signing.
        // HOW:  Validates envelope length, splits salt/nonce/tag/ciphertext, derives key, decrypts via AesGcm, zeroes key.
        ArgumentNullException.ThrowIfNull(envelopeBytes);
        if (envelopeBytes.Length < EnvelopeHeaderLength)
        {
            throw new CryptographicException("Encrypted envelope is truncated or invalid (< 44 bytes).");
        }

        ReadOnlySpan<byte> salt = envelopeBytes.AsSpan(0, SaltSizeBytes);
        ReadOnlySpan<byte> nonce = envelopeBytes.AsSpan(SaltSizeBytes, NonceSizeBytes);
        ReadOnlySpan<byte> tag = envelopeBytes.AsSpan(SaltSizeBytes + NonceSizeBytes, TagSizeBytes);
        ReadOnlySpan<byte> ciphertext = envelopeBytes.AsSpan(EnvelopeHeaderLength);

        Span<byte> wrappingKey = stackalloc byte[DerivedKeySizeBytes];
        try
        {
            DeriveWrappingKey(passphrase, salt, wrappingKey);

            byte[] plaintext = new byte[ciphertext.Length];
            using (AesGcm aesGcm = new(wrappingKey, TagSizeBytes))
            {
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData: salt);
            }

            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    /// <summary>
    /// Normalizes and validates the specified key algorithm identifier.
    /// </summary>
    /// <param name="keyAlgorithm">The algorithm string to validate.</param>
    /// <returns>Canonical algorithm name ("Ed25519" or "ML-DSA-65").</returns>
    private static string NormalizeKeyAlgorithm(string keyAlgorithm)
    {
        // WHAT: Validates and normalizes algorithm strings to canonical casing.
        // WHY:  Guarantees consistent dispatch for Ed25519 and ML-DSA-65.
        // HOW:  Checks string against known algorithms case-insensitively and returns canonical string.
        if (string.IsNullOrWhiteSpace(keyAlgorithm))
        {
            throw new ArgumentException("Key algorithm cannot be empty or whitespace.", nameof(keyAlgorithm));
        }

        if (keyAlgorithm.Equals("Ed25519", StringComparison.OrdinalIgnoreCase))
        {
            return "Ed25519";
        }

        if (keyAlgorithm.Equals("ML-DSA-65", StringComparison.OrdinalIgnoreCase))
        {
            return "ML-DSA-65";
        }

        throw new NotSupportedException($"Unsupported key algorithm: '{keyAlgorithm}'. Supported algorithms are 'Ed25519' and 'ML-DSA-65'.");
    }

    /// <summary>
    /// Validates that a passphrase is non-empty and contains non-whitespace characters.
    /// </summary>
    /// <param name="passphrase">The passphrase span to inspect.</param>
    private static void ValidatePassphrase(ReadOnlySpan<char> passphrase)
    {
        // WHAT: Asserts passphrase contains valid characters.
        // WHY:  Zero-trust sealing prohibits empty or whitespace-only passphrases.
        // HOW:  Checks IsEmpty and IsWhiteSpace on the span.
        if (passphrase.IsEmpty || passphrase.IsWhiteSpace())
        {
            throw new ArgumentException("Passphrase cannot be empty or whitespace.", nameof(passphrase));
        }
    }

    /// <summary>
    /// Computes the 64-character lowercase hexadecimal SHA-256 fingerprint of a public key DER byte sequence.
    /// </summary>
    /// <param name="data">The SubjectPublicKeyInfo DER bytes.</param>
    /// <returns>A 64-character lowercase hexadecimal string.</returns>
    private static string ComputeSha256Fingerprint(ReadOnlySpan<byte> data)
    {
        // WHAT: Computes SHA-256 digest of input bytes and formats as lowercase hex string.
        // WHY:  Standard public key fingerprint calculation across the platform.
        // HOW:  Uses SHA256.HashData into stack-allocated digest buffer and formats via Convert.ToHexStringLower.
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(data, digest);
        return Convert.ToHexStringLower(digest);
    }

    /// <summary>
    /// Formats DER bytes into standard RFC 7468 PEM text with 64-character line wraps.
    /// </summary>
    /// <param name="type">The label for header and footer (e.g., "PUBLIC KEY").</param>
    /// <param name="derBytes">Raw DER bytes to encode.</param>
    /// <returns>Formatted PEM string terminated by a newline.</returns>
    private static string FormatPem(string type, ReadOnlySpan<byte> derBytes)
    {
        // WHAT: Encodes DER bytes to Base64 and wraps in RFC 7468 PEM framing.
        // WHY:  Standard cryptographic text format for public and private keys.
        // HOW:  Converts to Base64 string and appends 64-character chunks between BEGIN and END delimiters.
        string b64 = Convert.ToBase64String(derBytes);
        StringBuilder sb = new();
        sb.Append("-----BEGIN ").Append(type).Append("-----\n");

        for (int i = 0; i < b64.Length; i += 64)
        {
            int length = Math.Min(64, b64.Length - i);
            sb.Append(b64.AsSpan(i, length)).Append('\n');
        }

        sb.Append("-----END ").Append(type).Append("-----\n");
        return sb.ToString();
    }

    /// <summary>
    /// Extracts raw DER bytes from an unencrypted or encrypted PEM string by stripping boundary delimiters.
    /// </summary>
    /// <param name="pem">The PEM-formatted string.</param>
    /// <returns>Decoded DER byte array.</returns>
    private static byte[] ExtractDerFromPem(string pem)
    {
        // WHAT: Strips RFC 7468 PEM boundary markers and base64-decodes the payload.
        // WHY:  Reconstructs raw PKCS#8 DER bytes from textual PEM representations.
        // HOW:  Enumerates lines via ReadOnlySpan<char>, discards delimiter headers/footers, and converts combined Base64.
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
}
