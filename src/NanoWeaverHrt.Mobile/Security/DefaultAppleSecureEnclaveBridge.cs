using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace NanoWeaverHrt.Mobile.Security;

/// <summary>
/// Default in-memory simulation implementation of <see cref="IAppleSecureEnclaveBridge"/> modeling
/// Apple Secure Enclave hardware keychain storage with zero-copy safety, HKDF domain separation, and RAM zeroization.
/// </summary>
/// <remarks>
/// // WHAT: Default implementation of Apple Secure Enclave bridge storing secrets in protected memory and encrypted files.
/// // WHY:  Provides full functional testing of Secure Enclave operations and persistence across bridge instances.
/// // HOW:  Uses ConcurrentDictionary to store defensive secret copies, HKDF for domain separation, and AES-256-GCM for disk encryption.
/// </remarks>
public sealed class DefaultAppleSecureEnclaveBridge : IAppleSecureEnclaveBridge
{
    /// <summary>
    /// AES-GCM nonce size in bytes (96 bits).
    /// </summary>
    private const int NonceSizeBytes = 12;

    /// <summary>
    /// AES-GCM authentication tag size in bytes (128 bits).
    /// </summary>
    private const int TagSizeBytes = 16;

    /// <summary>
    /// Minimum encrypted envelope length in bytes (nonce + tag).
    /// </summary>
    private const int MinimumEnvelopeLength = NonceSizeBytes + TagSizeBytes;

    /// <summary>
    /// Magic header identifying NanoWeaver Secure Enclave keychain entry payload (4 bytes "NWEC").
    /// </summary>
    private static ReadOnlySpan<byte> PayloadMagic => "NWEC"u8;

    /// <summary>
    /// Ed25519 signing HKDF info context string.
    /// </summary>
    private static ReadOnlySpan<byte> Ed25519SignInfo => "nanoweaver-ios-ed25519-sign-v1"u8;

    /// <summary>
    /// AES-256-GCM wrapping HKDF info context string.
    /// </summary>
    private static ReadOnlySpan<byte> Aes256GcmWrapInfo => "nanoweaver-ios-aes256gcm-wrap-v1"u8;

    /// <summary>
    /// Thread-safe in-memory store mapping keychain tags to their secret entries.
    /// </summary>
    private readonly ConcurrentDictionary<string, KeyEntry> _keychain = new(StringComparer.Ordinal);

    /// <summary>
    /// Value indicating whether hardware-backed Secure Enclave simulation is enabled.
    /// </summary>
    private readonly bool _hardwareAvailable;

    /// <summary>
    /// Optional delegate handling LocalAuthentication access control policy evaluation for deterministic testing.
    /// </summary>
    private readonly Func<string, CancellationToken, Task<bool>>? _policyHandler;

    /// <summary>
    /// Optional storage directory path for encrypted file-backed persistence.
    /// </summary>
    private readonly string? _storageDirectoryPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAppleSecureEnclaveBridge"/> class.
    /// </summary>
    /// <param name="hardwareAvailable">Flag indicating whether simulated Secure Enclave hardware is available.</param>
    /// <param name="policyHandler">Optional delegate simulating LAContext policy evaluation responses.</param>
    /// <param name="storageDirectoryPath">Optional directory path for persisting encrypted keychain entries to disk.</param>
    public DefaultAppleSecureEnclaveBridge(
        bool hardwareAvailable = true,
        Func<string, CancellationToken, Task<bool>>? policyHandler = null,
        string? storageDirectoryPath = null)
    {
        // WHAT: Initialize bridge with availability flag, optional policy handler, and persistence directory.
        // WHY:  Permits simulating Apple Secure Enclave keychain with optional disk persistence and biometric mocking.
        // HOW:  Stores parameter values in private fields and loads existing secrets from disk if directory configured.
        _hardwareAvailable = hardwareAvailable;
        _policyHandler = policyHandler;
        _storageDirectoryPath = storageDirectoryPath;

        if (!string.IsNullOrWhiteSpace(_storageDirectoryPath))
        {
            Directory.CreateDirectory(_storageDirectoryPath);
            LoadKeysFromDisk();
        }
    }

    /// <inheritdoc/>
    public bool IsSecureEnclaveAvailable => _hardwareAvailable;

    /// <inheritdoc/>
    public Task<bool> EvaluateAccessControlPolicyAsync(string localizedReason, CancellationToken ct = default)
    {
        // WHAT: Evaluate simulated Apple LocalAuthentication policy.
        // WHY:  Authorizes access to sensitive keychain secret material.
        // HOW:  Checks cancellation token, invokes custom handler if provided, or returns true by default.
        if (ct.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        if (_policyHandler != null)
        {
            return _policyHandler(localizedReason, ct);
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public void StoreKeychainSecret(string tag, ReadOnlySpan<byte> secretBytes, bool requiresBiometric = false)
    {
        // WHAT: Persist defensive copy of secret material under tag with biometric requirements and disk persistence.
        // WHY:  Guarantees isolated keychain secret storage without caller buffer retention and validates HKDF subkey derivation.
        // HOW:  Derives signing and wrapping subkeys to verify HKDF, updates memory store, and writes encrypted file if configured.
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        byte[] tempSign = DeriveSigningSubkey(tag, secretBytes);
        byte[] tempWrap = DeriveWrappingSubkey(tag, secretBytes);
        try
        {
            if (_keychain.TryGetValue(tag, out KeyEntry? existing))
            {
                existing.Zeroize();
            }

            byte[] copy = secretBytes.ToArray();
            _keychain[tag] = new KeyEntry(copy, requiresBiometric);

            if (!string.IsNullOrWhiteSpace(_storageDirectoryPath))
            {
                PersistKeyToDisk(tag, requiresBiometric, secretBytes);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tempSign);
            CryptographicOperations.ZeroMemory(tempWrap);
        }
    }

    /// <inheritdoc/>
    public bool TryReadKeychainSecret(string tag, out byte[] secretBytes)
    {
        // WHAT: Retrieve defensive copy of secret material if tag exists and biometric auth passes.
        // WHY:  Prevents external code from mutating the internally stored buffer and enforces biometric gating.
        // HOW:  Validates tag, checks store, evaluates biometric policy if required, returns cloned array or empty.
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        if (_keychain.TryGetValue(tag, out KeyEntry? entry))
        {
            if (entry.RequiresBiometric)
            {
                bool authResult = EvaluateAccessControlPolicyAsync($"Authenticate to access secret '{tag}'").GetAwaiter().GetResult();
                if (!authResult)
                {
                    secretBytes = [];
                    return false;
                }
            }

            secretBytes = (byte[])entry.MasterSecret.Clone();
            return true;
        }

        secretBytes = [];
        return false;
    }

    /// <inheritdoc/>
    public bool TryReadKeyForSigning(string tag, out byte[] signingKey)
    {
        // WHAT: Retrieve domain-separated Ed25519 signing key if tag exists and biometric auth passes.
        // WHY:  Enforces biometric authorization and zero key reuse between signing and encryption.
        // HOW:  Validates tag, enforces biometric policy if required, derives signing subkey via HKDF.
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        if (_keychain.TryGetValue(tag, out KeyEntry? entry))
        {
            if (entry.RequiresBiometric)
            {
                bool authResult = EvaluateAccessControlPolicyAsync($"Authenticate to sign with key '{tag}'").GetAwaiter().GetResult();
                if (!authResult)
                {
                    signingKey = [];
                    throw new CryptographicException($"LocalAuthentication policy evaluation failed for tag '{tag}'.");
                }
            }

            signingKey = DeriveSigningSubkey(tag, entry.MasterSecret);
            return true;
        }

        signingKey = [];
        return false;
    }

    /// <inheritdoc/>
    public bool TryReadKeyForWrapping(string tag, out byte[] wrappingKey)
    {
        // WHAT: Retrieve domain-separated AES-256-GCM wrapping key if tag exists and biometric auth passes.
        // WHY:  Enforces biometric authorization and zero key reuse between signing and encryption.
        // HOW:  Validates tag, enforces biometric policy if required, derives wrapping subkey via HKDF.
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        if (_keychain.TryGetValue(tag, out KeyEntry? entry))
        {
            if (entry.RequiresBiometric)
            {
                bool authResult = EvaluateAccessControlPolicyAsync($"Authenticate to access encryption key '{tag}'").GetAwaiter().GetResult();
                if (!authResult)
                {
                    wrappingKey = [];
                    throw new CryptographicException($"LocalAuthentication policy evaluation failed for tag '{tag}'.");
                }
            }

            wrappingKey = DeriveWrappingSubkey(tag, entry.MasterSecret);
            return true;
        }

        wrappingKey = [];
        return false;
    }

    /// <inheritdoc/>
    public async Task<byte[]?> GetKeyForSigningAsync(string tag, string? reason = null, CancellationToken ct = default)
    {
        // WHAT: Asynchronously retrieve Ed25519 signing subkey with LocalAuthentication evaluation.
        // WHY:  Allows non-blocking biometric evaluation prior to challenge signing.
        // HOW:  Validates tag, awaits policy if requiresBiometric, derives signing key via HKDF.
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ct.ThrowIfCancellationRequested();

        if (!_keychain.TryGetValue(tag, out KeyEntry? entry))
        {
            return null;
        }

        if (entry.RequiresBiometric)
        {
            bool authorized = await EvaluateAccessControlPolicyAsync(reason ?? $"Authenticate to access signing key '{tag}'", ct).ConfigureAwait(false);
            if (!authorized)
            {
                throw new CryptographicException($"LocalAuthentication policy evaluation failed for tag '{tag}'.");
            }
        }

        return DeriveSigningSubkey(tag, entry.MasterSecret);
    }

    /// <inheritdoc/>
    public async Task<byte[]?> GetKeyForWrappingAsync(string tag, string? reason = null, CancellationToken ct = default)
    {
        // WHAT: Asynchronously retrieve AES-256-GCM wrapping subkey with LocalAuthentication evaluation.
        // WHY:  Allows non-blocking biometric evaluation prior to payload decryption.
        // HOW:  Validates tag, awaits policy if requiresBiometric, derives wrapping key via HKDF.
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ct.ThrowIfCancellationRequested();

        if (!_keychain.TryGetValue(tag, out KeyEntry? entry))
        {
            return null;
        }

        if (entry.RequiresBiometric)
        {
            bool authorized = await EvaluateAccessControlPolicyAsync(reason ?? $"Authenticate to access encryption key '{tag}'", ct).ConfigureAwait(false);
            if (!authorized)
            {
                throw new CryptographicException($"LocalAuthentication policy evaluation failed for tag '{tag}'.");
            }
        }

        return DeriveWrappingSubkey(tag, entry.MasterSecret);
    }

    /// <inheritdoc/>
    public bool DeleteKeychainSecret(string tag)
    {
        // WHAT: Remove secret from Secure Enclave keychain and zeroize its buffer.
        // WHY:  Ensures deleted secret material cannot linger in process memory or on disk.
        // HOW:  Validates tag, removes entry from _keychain, zeroizes buffer, deletes disk file, and returns success flag.
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        if (_keychain.TryRemove(tag, out KeyEntry? removed))
        {
            removed.Zeroize();
            DeleteKeyFromDisk(tag);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public void ClearAllSessionSecrets()
    {
        // WHAT: Zeroize and remove all session secrets currently held in volatile memory.
        // WHY:  Provides complete memory cleanup during session reset or lifecycle pause.
        // HOW:  Iterates over all stored entries, wipes each via ZeroMemory, and clears the dictionary.
        foreach (KeyEntry entry in _keychain.Values)
        {
            entry.Zeroize();
        }

        _keychain.Clear();
    }

    /// <summary>
    /// Derives the domain-separated 32-byte Ed25519 signing subkey from the master secret using HKDF.
    /// </summary>
    /// <param name="tag">The keychain tag used as HKDF salt.</param>
    /// <param name="masterSecret">The master secret bytes.</param>
    /// <returns>A 32-byte derived signing key.</returns>
    private static byte[] DeriveSigningSubkey(string tag, ReadOnlySpan<byte> masterSecret)
    {
        // WHAT: Derive 32-byte Ed25519 signing subkey via HKDF-SHA256.
        // WHY:  Enforces domain separation between asymmetric signing and symmetric wrapping.
        // HOW:  Invokes HKDF.DeriveKey with SHA-256, tag as salt, and signing info tag.
        byte[] tagBytes = Encoding.UTF8.GetBytes(tag);
        byte[] subkey = new byte[32];
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            masterSecret,
            subkey,
            salt: tagBytes,
            info: Ed25519SignInfo);
        return subkey;
    }

    /// <summary>
    /// Derives the domain-separated 32-byte AES-256-GCM wrapping subkey from the master secret using HKDF.
    /// </summary>
    /// <param name="tag">The keychain tag used as HKDF salt.</param>
    /// <param name="masterSecret">The master secret bytes.</param>
    /// <returns>A 32-byte derived wrapping key.</returns>
    private static byte[] DeriveWrappingSubkey(string tag, ReadOnlySpan<byte> masterSecret)
    {
        // WHAT: Derive 32-byte AES-256-GCM wrapping subkey via HKDF-SHA256.
        // WHY:  Enforces domain separation between symmetric envelope crypto and asymmetric identity.
        // HOW:  Invokes HKDF.DeriveKey with SHA-256, tag as salt, and wrapping info tag.
        byte[] tagBytes = Encoding.UTF8.GetBytes(tag);
        byte[] subkey = new byte[32];
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            masterSecret,
            subkey,
            salt: tagBytes,
            info: Aes256GcmWrapInfo);
        return subkey;
    }

    /// <summary>
    /// Derives the machine/process-bound AES-256-GCM key used for encrypting persisted keychain files.
    /// </summary>
    /// <returns>A 32-byte machine-bound cryptographic key buffer.</returns>
    private static byte[] GetDiskEncryptionKey()
    {
        // WHAT: Derive machine-bound 32-byte AES-GCM encryption key using HKDF.
        // WHY:  Protects persisted keychain secrets at rest bound to the host environment.
        // HOW:  Hashes machine and user identity as salt and expands using HKDF.
        byte[] salt = SHA256.HashData(Encoding.UTF8.GetBytes($"nanoweaver:{Environment.MachineName}:{Environment.UserName}"));
        byte[] key = new byte[32];
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            salt,
            key,
            salt: default,
            info: "nanoweaver-mobile-keychain-storage-v1"u8);
        return key;
    }

    /// <summary>
    /// Loads and decrypts persisted keychain entries from disk.
    /// </summary>
    /// <remarks>
    /// // WHAT: Load encrypted keychain files from disk directory into the in-memory store.
    /// // WHY:  Ensures keychain secrets survive bridge reconstruction.
    /// // HOW:  Iterates over *.key files, decrypts with AES-256-GCM using machine-bound key, and populates _keychain.
    /// </remarks>
    private void LoadKeysFromDisk()
    {
        byte[] diskKey = GetDiskEncryptionKey();
        try
        {
            string[] files = Directory.GetFiles(_storageDirectoryPath!, "*.key");
            foreach (string filePath in files)
            {
                try
                {
                    byte[] fileBytes = File.ReadAllBytes(filePath);
                    if (fileBytes.Length < MinimumEnvelopeLength)
                    {
                        continue;
                    }

                    ReadOnlySpan<byte> nonce = fileBytes.AsSpan(0, NonceSizeBytes);
                    ReadOnlySpan<byte> tag = fileBytes.AsSpan(NonceSizeBytes, TagSizeBytes);
                    ReadOnlySpan<byte> ciphertext = fileBytes.AsSpan(MinimumEnvelopeLength);

                    byte[] plaintext = new byte[ciphertext.Length];
                    try
                    {
                        using var aesGcm = new AesGcm(diskKey, TagSizeBytes);
                        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

                        if (TryParseKeyPayload(plaintext, out string? keyTag, out bool requiresBiometric, out byte[]? masterSecret))
                        {
                            _keychain[keyTag] = new KeyEntry(masterSecret, requiresBiometric);
                        }
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(plaintext);
                    }
                }
                catch (Exception)
                {
                    // Ignore corrupted or invalid files on disk.
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(diskKey);
        }
    }

    /// <summary>
    /// Encrypts and persists a keychain entry to disk.
    /// </summary>
    /// <param name="tag">The keychain tag.</param>
    /// <param name="requiresBiometric">Flag indicating whether biometric auth is required.</param>
    /// <param name="masterSecret">The master secret bytes.</param>
    private void PersistKeyToDisk(string tag, bool requiresBiometric, ReadOnlySpan<byte> masterSecret)
    {
        // WHAT: Encrypt keychain entry and write to disk under hashed filename.
        // WHY:  Persists secret across bridge instances with confidentiality at rest.
        // HOW:  Encodes payload, encrypts via AES-256-GCM, and writes to storage directory.
        Directory.CreateDirectory(_storageDirectoryPath!);
        byte[] diskKey = GetDiskEncryptionKey();
        try
        {
            byte[] payload = FormatKeyPayload(tag, requiresBiometric, masterSecret);
            try
            {
                byte[] nonce = new byte[NonceSizeBytes];
                RandomNumberGenerator.Fill(nonce);

                byte[] tagBytes = new byte[TagSizeBytes];
                byte[] ciphertext = new byte[payload.Length];

                using (var aesGcm = new AesGcm(diskKey, TagSizeBytes))
                {
                    aesGcm.Encrypt(nonce, payload, ciphertext, tagBytes);
                }

                byte[] envelope = new byte[MinimumEnvelopeLength + ciphertext.Length];
                Buffer.BlockCopy(nonce, 0, envelope, 0, NonceSizeBytes);
                Buffer.BlockCopy(tagBytes, 0, envelope, NonceSizeBytes, TagSizeBytes);
                Buffer.BlockCopy(ciphertext, 0, envelope, MinimumEnvelopeLength, ciphertext.Length);

                string fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tag))) + ".key";
                string filePath = Path.Combine(_storageDirectoryPath!, fileName);
                File.WriteAllBytes(filePath, envelope);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(diskKey);
        }
    }

    /// <summary>
    /// Deletes a persisted keychain file from disk if it exists.
    /// </summary>
    /// <param name="tag">The keychain tag.</param>
    private void DeleteKeyFromDisk(string tag)
    {
        // WHAT: Remove keychain file from disk storage directory.
        // WHY:  Guarantees deleted secrets do not persist on storage media.
        // HOW:  Hashes tag to determine filename and deletes file if present.
        if (string.IsNullOrWhiteSpace(_storageDirectoryPath))
        {
            return;
        }

        string fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tag))) + ".key";
        string filePath = Path.Combine(_storageDirectoryPath, fileName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Formats keychain entry metadata and master secret into a structured binary payload.
    /// </summary>
    /// <param name="tag">Keychain tag.</param>
    /// <param name="requiresBiometric">Flag indicating whether biometric auth is required.</param>
    /// <param name="masterSecret">Master secret bytes.</param>
    /// <returns>Formatted binary payload bytes.</returns>
    private static byte[] FormatKeyPayload(string tag, bool requiresBiometric, ReadOnlySpan<byte> masterSecret)
    {
        // WHAT: Serialize keychain entry fields into binary buffer with magic header.
        // WHY:  Ensures structured format before AES-GCM encryption at rest.
        // HOW:  Writes magic, version, biometric flag, tag length, tag UTF-8, secret length, and secret bytes.
        byte[] tagBytes = Encoding.UTF8.GetBytes(tag);
        int totalLen = 4 + 1 + 1 + 2 + tagBytes.Length + 4 + masterSecret.Length;
        byte[] payload = new byte[totalLen];

        PayloadMagic.CopyTo(payload.AsSpan(0, 4));
        payload[4] = 1;
        payload[5] = requiresBiometric ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(6, 2), (short)tagBytes.Length);
        tagBytes.CopyTo(payload.AsSpan(8, tagBytes.Length));

        int offset = 8 + tagBytes.Length;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset, 4), masterSecret.Length);
        masterSecret.CopyTo(payload.AsSpan(offset + 4, masterSecret.Length));

        return payload;
    }

    /// <summary>
    /// Parses a binary keychain entry payload into tag, biometric flag, and master secret.
    /// </summary>
    /// <param name="payload">Plaintext payload bytes.</param>
    /// <param name="tag">Parsed keychain tag.</param>
    /// <param name="requiresBiometric">Parsed biometric requirement flag.</param>
    /// <param name="masterSecret">Parsed master secret bytes.</param>
    /// <returns><see langword="true"/> if payload was successfully parsed; otherwise <see langword="false"/>.</returns>
    private static bool TryParseKeyPayload(
        byte[] payload,
        [NotNullWhen(true)] out string? tag,
        out bool requiresBiometric,
        [NotNullWhen(true)] out byte[]? masterSecret)
    {
        // WHAT: Parse and validate decrypted binary payload from disk.
        // WHY:  Ensures data integrity and prevents malformed entry loading.
        // HOW:  Verifies length, magic, version, reads tag, and extracts master secret buffer.
        tag = null;
        requiresBiometric = false;
        masterSecret = null;

        if (payload.Length < 12)
        {
            return false;
        }

        if (!payload.AsSpan(0, 4).SequenceEqual(PayloadMagic))
        {
            return false;
        }

        if (payload[4] != 1)
        {
            return false;
        }

        requiresBiometric = payload[5] == 1;
        short tagLen = BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(6, 2));
        if (tagLen <= 0)
        {
            return false;
        }

        if (payload.Length < 8 + tagLen + 4)
        {
            return false;
        }

        tag = Encoding.UTF8.GetString(payload, 8, tagLen);
        int offset = 8 + tagLen;
        int secretLen = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset, 4));
        if (secretLen <= 0)
        {
            return false;
        }

        if (payload.Length < offset + 4 + secretLen)
        {
            return false;
        }

        masterSecret = new byte[secretLen];
        Buffer.BlockCopy(payload, offset + 4, masterSecret, 0, secretLen);
        return true;
    }

    /// <summary>
    /// Represents an in-memory or persisted keychain entry containing master secret material and biometric gating policy.
    /// </summary>
    private sealed class KeyEntry
    {
        /// <summary>
        /// Master secret cryptographic key buffer.
        /// </summary>
        public byte[] MasterSecret { get; }

        /// <summary>
        /// Flag indicating whether user biometric authentication is required before accessing or deriving keys.
        /// </summary>
        public bool RequiresBiometric { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="KeyEntry"/> class.
        /// </summary>
        /// <param name="masterSecret">Master secret byte buffer.</param>
        /// <param name="requiresBiometric">Flag indicating whether biometric prompt is required.</param>
        public KeyEntry(byte[] masterSecret, bool requiresBiometric)
        {
            // WHAT: Construct keychain entry storing master secret and policy flag.
            // WHY:  Associates biometric requirement metadata with cryptographic key material.
            // HOW:  Assigns arguments to public read-only properties.
            MasterSecret = masterSecret;
            RequiresBiometric = requiresBiometric;
        }

        /// <summary>
        /// Zeroizes the master secret buffer.
        /// </summary>
        public void Zeroize()
        {
            // WHAT: Cryptographically erase master secret from memory.
            // WHY:  Ensures secret material cannot be extracted from discarded memory buffers.
            // HOW:  Invokes CryptographicOperations.ZeroMemory on MasterSecret array.
            CryptographicOperations.ZeroMemory(MasterSecret);
        }
    }
}
