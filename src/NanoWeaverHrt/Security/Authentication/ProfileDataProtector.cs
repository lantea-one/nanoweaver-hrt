using System.Security.Cryptography;
using System.Text;
using NanoWeaverHrt.Security.Secrets;

namespace NanoWeaverHrt.Security.Authentication;

/// <summary>
/// Cryptographic envelope protector for sensitive patient profile assets (such as avatar pictures and PHI fields),
/// applying authenticated AES-256-GCM encryption at rest.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A small two-way door for patient data. Values go in as plain text and come out
/// as an opaque <c>data:application/encrypted;base64,…</c> string that is useless without this node's key.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Avatars, preferred names and clinical notes are protected health information.
/// They must be unreadable to anyone who obtains a copy of the database file or a backup.
/// </para>
/// <para>
/// <strong>How it works:</strong> AES-256-GCM with a fresh random 96-bit nonce per value and a 128-bit
/// authentication tag, keyed by the node's PHI master key from <see cref="INodeSecretStore"/>.
/// </para>
/// <para>
/// <strong>Upgrade path:</strong> deployments created before the node key existed encrypted their data with the
/// SHA-256 of a constant that was published in this repository. That historic key is retained here as a
/// <strong>decrypt-only</strong> fallback so that no patient data is lost on upgrade, and every successful
/// legacy decryption is counted in <see cref="LegacyDecryptionCount"/> so the host can re-encrypt the affected
/// rows under the new key at startup. Nothing encrypts with the legacy key, ever.
/// </para>
/// </remarks>
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
    /// Error text used when an envelope claims to be protected but is structurally unusable.
    /// </summary>
    /// <remarks>
    /// Deliberately free of file paths, key bytes, SQL and connection strings, so that it stays safe even if it
    /// ever reached an API client through the RFC 7807 handler (Prime Directive 14).
    /// </remarks>
    private const string MalformedEnvelopeMessage = "Protected profile field envelope is malformed.";

    /// <summary>
    /// Error text used when an envelope cannot be authenticated with either the active or the legacy key.
    /// </summary>
    private const string UndecryptableMessage =
        "Protected profile field could not be decrypted with the active or legacy node key.";

    /// <summary>
    /// Hex encoding of the historic PHI key: the SHA-256 digest of the profile-protection pepper that earlier
    /// releases hard-coded in this public repository.
    /// </summary>
    /// <remarks>
    /// The digest is embedded rather than the original pepper text so that the historic secret is not reprinted
    /// in the source tree. It exists for one purpose only: decrypting data written before this node generated
    /// its own key, so that upgrading loses no patient record.
    /// </remarks>
    private const string LegacyPhiMasterKeyHex = "FEA47E93A869FF0D5965AB61A3E7476FE15632CE387DBFBC49CB84A3E45A2FE6";

    /// <summary>
    /// Decrypt-only key material for data written before per-node key provisioning existed.
    /// </summary>
    private static readonly byte[] LegacyPhiMasterKey = Convert.FromHexString(LegacyPhiMasterKeyHex);

    /// <summary>
    /// AES-256-GCM key material provisioned for this node, used for all encryption and for decryption first.
    /// </summary>
    private readonly ReadOnlyMemory<byte> _encryptionKey;

    /// <summary>
    /// Running count of values that could only be decrypted with the legacy key.
    /// </summary>
    private long _legacyDecryptionCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileDataProtector"/> class.
    /// </summary>
    /// <param name="secretStore">Provider of this node's persisted 256-bit PHI master key.</param>
    /// <exception cref="ArgumentNullException"><paramref name="secretStore"/> is <c>null</c>.</exception>
    public ProfileDataProtector(INodeSecretStore secretStore)
    {
        // WHAT: Adopts the node's persisted 256-bit PHI master key.
        // WHY:  The previous implementation fell back to the SHA-256 of a literal published in this
        //       repository, so every default deployment protected patient avatars and PHI with a key that
        //       every reader of the source code already knew.
        // HOW:  INodeSecretStore provisions a random key on first launch into an owner-only file that lives
        //       outside the database it protects.
        ArgumentNullException.ThrowIfNull(secretStore);
        _encryptionKey = secretStore.PhiMasterKey;
    }

    /// <summary>
    /// Gets the number of values decrypted with the legacy key since this instance was created.
    /// </summary>
    /// <value>Zero on a deployment that has no pre-upgrade data.</value>
    /// <remarks>
    /// The host samples this counter around a read to decide whether a stored row still carries pre-upgrade
    /// ciphertext and must be written back under the current key.
    /// </remarks>
    public long LegacyDecryptionCount => Interlocked.Read(ref _legacyDecryptionCount);

    /// <summary>
    /// Encrypts a raw data-URI or plaintext string into an authenticated AES-256-GCM encrypted envelope.
    /// </summary>
    /// <param name="plainDataUri">The raw data-URI or image string to protect.</param>
    /// <returns>An encrypted data-URI string, or an empty string when there was nothing to protect.</returns>
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

        return EncryptCore(plainDataUri);
    }

    /// <summary>
    /// Decrypts an authenticated AES-256-GCM encrypted data-URI envelope back into its raw string.
    /// </summary>
    /// <param name="encryptedDataUri">The encrypted data-URI string, a plaintext value, or <c>null</c>.</param>
    /// <returns>
    /// The decrypted plaintext for a protected value, an empty string for a blank input, or the input itself
    /// when it is ordinary plaintext that was never protected.
    /// </returns>
    /// <exception cref="CryptographicException">
    /// The value carries the protected-envelope prefix but is not valid Base64, is shorter than a nonce plus a
    /// tag, or fails authentication under both the active and the legacy node key.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Earlier releases swallowed every failure here and returned the ciphertext unchanged, which handed the
    /// caller an opaque <c>data:application/encrypted;base64,…</c> string dressed up as plaintext: a silent
    /// failure that is indistinguishable from a value that was never encrypted. Genuine failures now throw, so
    /// a wrong key, a truncated column or a tampered row is visible instead of being rendered as garbage.
    /// Ordinary plaintext never throws, because it does not carry the prefix.
    /// </para>
    /// </remarks>
    public string DecryptDataUri(string? encryptedDataUri)
    {
        // WHAT: Decrypts an encrypted data URI back into the original plaintext data URI.
        // WHY:  Allows authorized clients with verified session claims to render decrypted images.
        // HOW:  Extracts nonce, tag, and ciphertext from the Base64 envelope, tries the node key, then the
        //       decrypt-only legacy key, and otherwise reports a cryptographic failure.
        if (string.IsNullOrWhiteSpace(encryptedDataUri))
        {
            return string.Empty;
        }

        if (!encryptedDataUri.StartsWith(EncryptedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return encryptedDataUri;
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(encryptedDataUri[EncryptedPrefix.Length..]);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException(MalformedEnvelopeMessage, ex);
        }

        if (payload.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new CryptographicException(MalformedEnvelopeMessage);
        }

        ReadOnlySpan<byte> nonce = payload.AsSpan(0, NonceSizeBytes);
        ReadOnlySpan<byte> tag = payload.AsSpan(NonceSizeBytes, TagSizeBytes);
        ReadOnlySpan<byte> cipherBytes = payload.AsSpan(NonceSizeBytes + TagSizeBytes);
        byte[] plainBytes = new byte[cipherBytes.Length];

        if (TryDecrypt(_encryptionKey.Span, nonce, cipherBytes, tag, plainBytes))
        {
            return Encoding.UTF8.GetString(plainBytes);
        }

        // WHAT: Second attempt with the historic published key.
        // WHY:  Data written before this node generated its own key must remain readable, otherwise upgrading
        //       the service would silently destroy patient avatars and PHI.
        // HOW:  Decrypt with the legacy key and record the event so the host can re-encrypt the row.
        if (TryDecrypt(LegacyPhiMasterKey, nonce, cipherBytes, tag, plainBytes))
        {
            Interlocked.Increment(ref _legacyDecryptionCount);
            return Encoding.UTF8.GetString(plainBytes);
        }

        throw new CryptographicException(UndecryptableMessage);
    }

    /// <summary>
    /// Always encrypts a text value into the <c>data:application/encrypted;base64,…</c> envelope.
    /// </summary>
    /// <param name="plainText">The non-empty text to protect.</param>
    /// <returns>The authenticated AES-256-GCM envelope.</returns>
    /// <remarks>
    /// Unlike <see cref="EncryptDataUri"/> this never passes a value through because it merely looks like an
    /// envelope; a patient who types such a string into a free-text field must get it encrypted, not stored raw.
    /// </remarks>
    private string EncryptCore(string plainText)
    {
        // WHAT: Seals one string with a fresh nonce under the node PHI key.
        // WHY:  Every write path, picture or PII field, must produce the same authenticated envelope.
        // HOW:  Random 96-bit nonce, AES-GCM encrypt, then Base64 of nonce + tag + ciphertext.
        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        byte[] nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);

        byte[] cipherBytes = new byte[plainBytes.Length];
        byte[] tag = new byte[TagSizeBytes];

        using (AesGcm aes = new(_encryptionKey.Span, TagSizeBytes))
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
    /// Encrypts one protected-health-information text field for storage.
    /// </summary>
    /// <param name="plainText">The field value, or <c>null</c> when the field is absent.</param>
    /// <returns><c>null</c> for <c>null</c>, the empty string for the empty string, otherwise the envelope.</returns>
    /// <remarks>
    /// Nothing is hidden by encrypting an empty value, so absence and emptiness stay visible and
    /// <c>NOT NULL</c> columns keep working. Whitespace-only text is real data and is encrypted unchanged.
    /// </remarks>
    public string? EncryptField(string? plainText)
    {
        // WHAT: Null/empty-preserving field encryption.
        // WHY:  Optional columns must remain NULL when absent, and an empty string leaks nothing.
        // HOW:  Short-circuits null and empty, otherwise delegates to the always-encrypt core.
        if (string.IsNullOrEmpty(plainText))
        {
            return plainText;
        }

        return EncryptCore(plainText);
    }

    /// <summary>
    /// Decrypts one protected-health-information text field read from storage.
    /// </summary>
    /// <param name="storedValue">The stored column value, or <c>null</c> when the column is NULL.</param>
    /// <returns>The plaintext, or <c>null</c>/empty when the stored value was.</returns>
    /// <exception cref="CryptographicException">
    /// The value is not an envelope, is malformed, was forged, or was written under a different key. The message
    /// never contains the stored value.
    /// </exception>
    /// <remarks>
    /// A non-empty value that is not an envelope is refused instead of returned: after migration 005 no plaintext
    /// can legitimately be present, so one that is must never be rendered as if it were trustworthy.
    /// </remarks>
    public string? DecryptField(string? storedValue)
    {
        // WHAT: Strict counterpart of EncryptField.
        // WHY:  Silently returning an unencrypted value would hide a bypassed or tampered row.
        // HOW:  Passes through null/empty and hands everything else to the strict envelope reader.
        if (string.IsNullOrEmpty(storedValue))
        {
            return storedValue;
        }

        return DecryptEnvelope(storedValue);
    }

    /// <summary>
    /// Encrypts a field that is never <c>null</c> (a <c>NOT NULL</c> column).
    /// </summary>
    /// <param name="plainText">The field value; may be empty.</param>
    /// <returns>The empty string for empty input, otherwise the envelope.</returns>
    public string EncryptRequiredField(string plainText)
    {
        // WHAT: Non-nullable flavour of EncryptField.
        // WHY:  NOT NULL columns and non-nullable model properties need a string result, not string?.
        // HOW:  Empty stays empty (nothing to hide); anything else is sealed.
        return plainText.Length == 0 ? string.Empty : EncryptCore(plainText);
    }

    /// <summary>
    /// Decrypts a field read from a <c>NOT NULL</c> column.
    /// </summary>
    /// <param name="storedValue">The stored column value; may be empty.</param>
    /// <returns>The plaintext, or the empty string for an empty stored value.</returns>
    /// <exception cref="CryptographicException">The value is not a valid envelope for this node.</exception>
    public string DecryptRequiredField(string storedValue)
    {
        // WHAT: Non-nullable flavour of DecryptField.
        // WHY:  Mirrors EncryptRequiredField so the model's non-nullable properties stay non-nullable.
        // HOW:  Empty stays empty; anything else goes through the strict envelope reader.
        return storedValue.Length == 0 ? string.Empty : DecryptEnvelope(storedValue);
    }

    /// <summary>
    /// Strictly decrypts one non-empty stored value.
    /// </summary>
    /// <param name="storedValue">A non-empty stored column value.</param>
    /// <returns>The plaintext.</returns>
    /// <exception cref="CryptographicException">The value is not an envelope, or cannot be decrypted.</exception>
    private string DecryptEnvelope(string storedValue)
    {
        // WHAT: Rejects plaintext, then decrypts.
        // WHY:  After migration 005 a non-envelope value means the row bypassed encryption or was tampered with.
        // HOW:  Uses DecryptDataUri for the decrypt so the legacy-key fallback and LegacyDecryptionCount behave
        //       exactly as they do for the picture column.
        if (!IsEncrypted(storedValue))
        {
            throw new CryptographicException(UnprotectedFieldMessage);
        }

        return DecryptDataUri(storedValue);
    }

    /// <summary>
    /// Determines whether a stored value already carries the encrypted envelope prefix.
    /// </summary>
    /// <param name="value">The stored column value.</param>
    /// <returns><c>true</c> when the value starts with the envelope prefix.</returns>
    /// <remarks>The migration uses this to stay idempotent: an envelope is never encrypted a second time.</remarks>
    public static bool IsEncrypted(string? value) =>
        value is not null && value.StartsWith(EncryptedPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Error message used when a PHI column holds a non-empty value that is not an encrypted envelope.
    /// </summary>
    private const string UnprotectedFieldMessage = "Protected profile field is not stored in encrypted form.";

    /// <summary>
    /// Attempts an AES-256-GCM open operation with the supplied key.
    /// </summary>
    /// <param name="key">The 32-byte key to try.</param>
    /// <param name="nonce">The 12-byte nonce taken from the envelope.</param>
    /// <param name="cipherBytes">The ciphertext taken from the envelope.</param>
    /// <param name="tag">The 16-byte authentication tag taken from the envelope.</param>
    /// <param name="plainBytes">Destination buffer, exactly as long as <paramref name="cipherBytes"/>.</param>
    /// <returns><c>true</c> when the tag authenticated and <paramref name="plainBytes"/> holds the plaintext; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// A failed tag check is an expected, non-exceptional outcome here: it is how a value encrypted under the
    /// legacy key is told apart from a value encrypted under the current key. The authenticated mode makes this
    /// trial decryption safe, because a wrong key cannot produce a plaintext that passes authentication.
    /// </remarks>
    private static bool TryDecrypt(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> cipherBytes,
        ReadOnlySpan<byte> tag,
        Span<byte> plainBytes)
    {
        try
        {
            using AesGcm aes = new(key, TagSizeBytes);
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
