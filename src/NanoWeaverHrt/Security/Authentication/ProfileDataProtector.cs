using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Cryptographic envelope protector for sensitive patient profile assets (such as avatar pictures and PHI fields),
/// applying authenticated AES-256-GCM encryption at rest.
/// </summary>
public sealed class ProfileDataProtector
{
    /// <summary>
    /// AES-256 key size in bytes.
    /// </summary>
    private const int KeySizeBytes = 32;

    /// <summary>
    /// GCM nonce/IV size in bytes.
    /// </summary>
    private const int NonceSizeBytes = 12;

    /// <summary>
    /// GCM authentication tag size in bytes.
    /// </summary>
    private const int TagSizeBytes = 16;

    /// <summary>
    /// Encrypted data URI prefix.
    /// </summary>
    private const string EncryptedPrefix = "data:application/encrypted;base64,";

    /// <summary>
    /// Cryptographic key material derived from node pepper.
    /// </summary>
    private readonly byte[] _encryptionKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileDataProtector"/> class.
    /// </summary>
    /// <param name="configuration">Application configuration containing security pepper settings.</param>
    public ProfileDataProtector(IConfiguration configuration)
    {
        // WHAT: Derives a 256-bit symmetric key from system configuration or fallback secret.
        // WHY:  Ensures data-at-rest encryption for sensitive visual and biometric health assets.
        // HOW:  Hashes the configured secret pepper using SHA-256 to produce an exact 32-byte key.
        string pepper = configuration["Security:ProfilePepper"]
            ?? configuration["SECURITY_PEPPER"]
            ?? "nanoweaver_hrt_default_profile_protection_master_key_v1";

        _encryptionKey = SHA256.HashData(Encoding.UTF8.GetBytes(pepper));
    }

    /// <summary>
    /// Encrypts a raw data-URI or plaintext string into an authenticated AES-256-GCM encrypted envelope.
    /// </summary>
    /// <param name="plainDataUri">The raw data-URI or image string to protect.</param>
    /// <returns>An encrypted data-URI string.</returns>
    public string EncryptDataUri(string? plainDataUri)
    {
        // WHAT: Encrypts the provided string using AES-256-GCM and formats as an encrypted data URI.
        // WHY:  Guarantees that avatar pictures and identifiers are encrypted before being written to disk/database.
        // HOW:  Generates random 96-bit nonce, encrypts plaintext bytes with AesGcm, and combines into Base64 envelope.
        if (string.IsNullOrWhiteSpace(plainDataUri))
        {
            return string.Empty;
        }

        if (plainDataUri.StartsWith(EncryptedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return plainDataUri;
        }

        byte[] plainBytes = Encoding.UTF8.GetBytes(plainDataUri);
        byte[] nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);

        byte[] cipherBytes = new byte[plainBytes.Length];
        byte[] tag = new byte[TagSizeBytes];

        using (AesGcm aes = new(_encryptionKey, TagSizeBytes))
        {
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }

        // Combine: Nonce + Tag + Ciphertext
        byte[] payload = new byte[NonceSizeBytes + TagSizeBytes + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSizeBytes);
        Buffer.BlockCopy(tag, 0, payload, NonceSizeBytes, TagSizeBytes);
        Buffer.BlockCopy(cipherBytes, 0, payload, NonceSizeBytes + TagSizeBytes, cipherBytes.Length);

        return $"{EncryptedPrefix}{Convert.ToBase64String(payload)}";
    }

    /// <summary>
    /// Decrypts an authenticated AES-256-GCM encrypted data-URI envelope back into its raw string.
    /// </summary>
    /// <param name="encryptedDataUri">The encrypted data-URI string.</param>
    /// <returns>The decrypted plaintext string, or the input if not encrypted or invalid.</returns>
    public string DecryptDataUri(string? encryptedDataUri)
    {
        // WHAT: Decrypts an encrypted data URI back into the original plaintext data URI.
        // WHY:  Allows authorized clients with verified session claims to render decrypted images.
        // HOW:  Extracts nonce, tag, and ciphertext from Base64 envelope and decrypts using AesGcm.
        if (string.IsNullOrWhiteSpace(encryptedDataUri))
        {
            return string.Empty;
        }

        if (!encryptedDataUri.StartsWith(EncryptedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return encryptedDataUri;
        }

        try
        {
            string base64Payload = encryptedDataUri[EncryptedPrefix.Length..];
            byte[] payload = Convert.FromBase64String(base64Payload);

            if (payload.Length < NonceSizeBytes + TagSizeBytes)
            {
                return encryptedDataUri;
            }

            byte[] nonce = new byte[NonceSizeBytes];
            byte[] tag = new byte[TagSizeBytes];
            int cipherLen = payload.Length - NonceSizeBytes - TagSizeBytes;
            byte[] cipherBytes = new byte[cipherLen];
            byte[] plainBytes = new byte[cipherLen];

            Buffer.BlockCopy(payload, 0, nonce, 0, NonceSizeBytes);
            Buffer.BlockCopy(payload, NonceSizeBytes, tag, 0, TagSizeBytes);
            Buffer.BlockCopy(payload, NonceSizeBytes + TagSizeBytes, cipherBytes, 0, cipherLen);

            using (AesGcm aes = new(_encryptionKey, TagSizeBytes))
            {
                aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
            }

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Tampered or invalid ciphertext
            return encryptedDataUri;
        }
    }
}
