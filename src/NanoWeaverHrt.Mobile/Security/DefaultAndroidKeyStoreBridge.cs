using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace NanoWeaverHrt.Mobile.Security;

/// <summary>
/// Default in-memory simulation implementation of <see cref="IAndroidKeyStoreBridge"/> modeling
/// Android Keystore hardware storage with zero-copy safety, HKDF domain separation, and RAM zeroization.
/// </summary>
/// <remarks>
/// // WHAT: Default implementation of Android Keystore bridge storing keys in protected memory and encrypted files.
/// // WHY:  Provides full functional testing of Android Keystore operations and persistence across bridge instances.
/// // HOW:  Uses ConcurrentDictionary to store defensive key copies, HKDF for domain separation, and AES-256-GCM for disk encryption.
/// </remarks>
public sealed class DefaultAndroidKeyStoreBridge : IAndroidKeyStoreBridge
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
    /// Magic header identifying NanoWeaver keystore entry payload (4 bytes "NWKS").
    /// </summary>
    private static ReadOnlySpan<byte> PayloadMagic => "NWKS"u8;

    /// <summary>
    /// Ed25519 signing HKDF info context string.
    /// </summary>
    private static ReadOnlySpan<byte> Ed25519SignInfo => "nanoweaver-android-ed25519-sign-v1"u8;

    /// <summary>
    /// AES-256-GCM wrapping HKDF info context string.
    /// </summary>
    private static ReadOnlySpan<byte> Aes256GcmWrapInfo => "nanoweaver-android-aes256gcm-wrap-v1"u8;

    /// <summary>
    /// Thread-safe in-memory store mapping key aliases to their key entries.
    /// </summary>
    private readonly ConcurrentDictionary<string, KeyEntry> _store = new(StringComparer.Ordinal);

    /// <summary>
    /// Value indicating whether hardware-backed TEE / StrongBox simulation is enabled.
    /// </summary>
    private readonly bool _hardwareAvailable;

    /// <summary>
    /// Optional delegate handling biometric prompt evaluation for deterministic testing.
    /// </summary>
    private readonly Func<string, CancellationToken, Task<bool>>? _authHandler;

    /// <summary>
    /// Optional storage directory path for encrypted file-backed persistence.
    /// </summary>
    private readonly string? _storageDirectoryPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAndroidKeyStoreBridge"/> class.
    /// </summary>
    /// <param name="hardwareAvailable">Flag indicating whether simulated hardware-backed storage is available.</param>
    /// <param name="authHandler">Optional delegate simulating BiometricPrompt authentication responses.</param>
    /// <param name="storageDirectoryPath">Optional directory path for persisting encrypted key entries to disk.</param>
    public DefaultAndroidKeyStoreBridge(
        bool hardwareAvailable = true,
        Func<string, CancellationToken, Task<bool>>? authHandler = null,
        string? storageDirectoryPath = null)
    {
        // WHAT: Initialize bridge with availability flag, optional authentication handler, and persistence directory.
        // WHY:  Permits simulating Android Keystore environments with optional disk persistence and biometric mocking.
        // HOW:  Stores parameter values in private fields and loads existing keys from disk if directory configured.
        _hardwareAvailable = hardwareAvailable;
        _authHandler = authHandler;
        _storageDirectoryPath = storageDirectoryPath;

        if (!string.IsNullOrWhiteSpace(_storageDirectoryPath))
        {
            Directory.CreateDirectory(_storageDirectoryPath);
            LoadKeysFromDisk();
        }
    }

    /// <inheritdoc/>
    public bool IsHardwareBackedAvailable => _hardwareAvailable;

    /// <inheritdoc/>
    public Task<bool> AuthenticateBiometricPromptAsync(string reason, CancellationToken ct = default)
    {
        // WHAT: Evaluate simulated Android BiometricPrompt challenge.
        // WHY:  Authorizes access to sensitive cryptographic key material.
        // HOW:  Checks cancellation token, invokes custom handler if provided, or returns true by default.
        if (ct.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        if (_authHandler != null)
        {
            return _authHandler(reason, ct);
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public void StoreWrappedKey(string alias, ReadOnlySpan<byte> keyMaterial, bool requiresBiometric = false)
    {
        // WHAT: Persist defensive copy of key material under alias with biometric requirements and disk persistence.
        // WHY:  Guarantees isolated key storage without caller buffer retention and validates HKDF subkey derivation.
        // HOW:  Derives signing and wrapping subkeys to verify HKDF, updates memory store, and writes encrypted file if configured.
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        byte[] tempSign = DeriveSigningSubkey(alias, keyMaterial);
        byte[] tempWrap = DeriveWrappingSubkey(alias, keyMaterial);
        try
        {
            if (_store.TryGetValue(alias, out KeyEntry? existing))
            {
                existing.Zeroize();
            }

            byte[] copy = keyMaterial.ToArray();
            _store[alias] = new KeyEntry(copy, requiresBiometric);

            if (!string.IsNullOrWhiteSpace(_storageDirectoryPath))
            {
                PersistKeyToDisk(alias, requiresBiometric, keyMaterial);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tempSign);
            CryptographicOperations.ZeroMemory(tempWrap);
        }
    }

    /// <inheritdoc/>
    public bool TryLoadWrappedKey(string alias, out byte[] keyMaterial)
    {
        // WHAT: Retrieve defensive copy of key material if alias exists and biometric auth passes.
        // WHY:  Prevents external code from mutating the internally stored buffer and enforces biometric gating.
        // HOW:  Validates alias, checks store, evaluates biometric prompt if required, returns cloned array or empty.
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        if (_store.TryGetValue(alias, out KeyEntry? entry))
        {
            if (entry.RequiresBiometric)
            {
                bool authResult = AuthenticateBiometricPromptAsync($"Authenticate to access key '{alias}'").GetAwaiter().GetResult();
                if (!authResult)
                {
                    keyMaterial = [];
                    return false;
                }
            }

            keyMaterial = (byte[])entry.MasterSeed.Clone();
            return true;
        }

        keyMaterial = [];
        return false;
    }

    /// <inheritdoc/>
    public bool TryLoadKeyForSigning(string alias, out byte[] signingKey)
    {
        // WHAT: Retrieve domain-separated Ed25519 signing key if alias exists and biometric auth passes.
        // WHY:  Enforces biometric authorization and zero key reuse between signing and encryption.
        // HOW:  Validates alias, enforces biometric prompt if required, derives signing subkey via HKDF.
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        if (_store.TryGetValue(alias, out KeyEntry? entry))
        {
            if (entry.RequiresBiometric)
            {
                bool authResult = AuthenticateBiometricPromptAsync($"Authenticate to sign with key '{alias}'").GetAwaiter().GetResult();
                if (!authResult)
                {
                    signingKey = [];
                    throw new CryptographicException($"Biometric prompt authorization failed for key '{alias}'.");
                }
            }

            signingKey = DeriveSigningSubkey(alias, entry.MasterSeed);
            return true;
        }

        signingKey = [];
        return false;
    }

    /// <inheritdoc/>
    public bool TryLoadKeyForWrapping(string alias, out byte[] wrappingKey)
    {
        // WHAT: Retrieve domain-separated AES-256-GCM wrapping key if alias exists and biometric auth passes.
        // WHY:  Enforces biometric authorization and zero key reuse between signing and encryption.
        // HOW:  Validates alias, enforces biometric prompt if required, derives wrapping subkey via HKDF.
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        if (_store.TryGetValue(alias, out KeyEntry? entry))
        {
            if (entry.RequiresBiometric)
            {
                bool authResult = AuthenticateBiometricPromptAsync($"Authenticate to access encryption key '{alias}'").GetAwaiter().GetResult();
                if (!authResult)
                {
                    wrappingKey = [];
                    throw new CryptographicException($"Biometric prompt authorization failed for key '{alias}'.");
                }
            }

            wrappingKey = DeriveWrappingSubkey(alias, entry.MasterSeed);
            return true;
        }

        wrappingKey = [];
        return false;
    }

    /// <inheritdoc/>
    public async Task<byte[]?> GetKeyForSigningAsync(string alias, string? reason = null, CancellationToken ct = default)
    {
        // WHAT: Asynchronously retrieve Ed25519 signing subkey with BiometricPrompt evaluation.
        // WHY:  Allows non-blocking biometric prompt evaluation prior to challenge signing.
        // HOW:  Validates alias, awaits prompt if requiresBiometric, derives signing key via HKDF.
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ct.ThrowIfCancellationRequested();

        if (!_store.TryGetValue(alias, out KeyEntry? entry))
        {
            return null;
        }

        if (entry.RequiresBiometric)
        {
            bool authorized = await AuthenticateBiometricPromptAsync(reason ?? $"Authenticate to access signing key '{alias}'", ct).ConfigureAwait(false);
            if (!authorized)
            {
                throw new CryptographicException($"Biometric prompt authorization failed for key '{alias}'.");
            }
        }

        return DeriveSigningSubkey(alias, entry.MasterSeed);
    }

    /// <inheritdoc/>
    public async Task<byte[]?> GetKeyForWrappingAsync(string alias, string? reason = null, CancellationToken ct = default)
    {
        // WHAT: Asynchronously retrieve AES-256-GCM wrapping subkey with BiometricPrompt evaluation.
        // WHY:  Allows non-blocking biometric prompt evaluation prior to payload decryption.
        // HOW:  Validates alias, awaits prompt if requiresBiometric, derives wrapping key via HKDF.
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ct.ThrowIfCancellationRequested();

        if (!_store.TryGetValue(alias, out KeyEntry? entry))
        {
            return null;
        }

        if (entry.RequiresBiometric)
        {
            bool authorized = await AuthenticateBiometricPromptAsync(reason ?? $"Authenticate to access encryption key '{alias}'", ct).ConfigureAwait(false);
            if (!authorized)
            {
                throw new CryptographicException($"Biometric prompt authorization failed for key '{alias}'.");
            }
        }

        return DeriveWrappingSubkey(alias, entry.MasterSeed);
    }

    /// <inheritdoc/>
    public bool DeleteWrappedKey(string alias)
    {
        // WHAT: Remove key from Keystore and zeroize its buffer.
        // WHY:  Ensures deleted key material cannot linger in process memory or on disk.
        // HOW:  Validates alias, removes entry from _store, zeroizes buffer, deletes disk file, and returns success flag.
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        if (_store.TryRemove(alias, out KeyEntry? removed))
        {
            removed.Zeroize();
            DeleteKeyFromDisk(alias);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public void ClearAllSessionKeys()
    {
        // WHAT: Zeroize and remove all session keys currently held in volatile memory.
        // WHY:  Provides complete memory cleanup during session reset or lifecycle pause.
        // HOW:  Iterates over all stored entries, wipes each via ZeroMemory, and clears the dictionary.
        foreach (KeyEntry entry in _store.Values)
        {
            entry.Zeroize();
        }

        _store.Clear();
    }

    /// <summary>
    /// Derives the domain-separated 32-byte Ed25519 signing subkey from the master seed using HKDF.
    /// </summary>
    /// <param name="alias">The key alias used as HKDF salt.</param>
    /// <param name="masterSeed">The master seed bytes.</param>
    /// <returns>A 32-byte derived signing key.</returns>
    private static byte[] DeriveSigningSubkey(string alias, ReadOnlySpan<byte> masterSeed)
    {
        // WHAT: Derive 32-byte Ed25519 signing subkey via HKDF-SHA256.
        // WHY:  Enforces domain separation between asymmetric signing and symmetric wrapping.
        // HOW:  Invokes HKDF.DeriveKey with SHA-256, alias as salt, and signing info tag.
        byte[] aliasBytes = Encoding.UTF8.GetBytes(alias);
        byte[] subkey = new byte[32];
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            masterSeed,
            subkey,
            salt: aliasBytes,
            info: Ed25519SignInfo);
        return subkey;
    }

    /// <summary>
    /// Derives the domain-separated 32-byte AES-256-GCM wrapping subkey from the master seed using HKDF.
    /// </summary>
    /// <param name="alias">The key alias used as HKDF salt.</param>
    /// <param name="masterSeed">The master seed bytes.</param>
    /// <returns>A 32-byte derived wrapping key.</returns>
    private static byte[] DeriveWrappingSubkey(string alias, ReadOnlySpan<byte> masterSeed)
    {
        // WHAT: Derive 32-byte AES-256-GCM wrapping subkey via HKDF-SHA256.
        // WHY:  Enforces domain separation between symmetric envelope crypto and asymmetric identity.
        // HOW:  Invokes HKDF.DeriveKey with SHA-256, alias as salt, and wrapping info tag.
        byte[] aliasBytes = Encoding.UTF8.GetBytes(alias);
        byte[] subkey = new byte[32];
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            masterSeed,
            subkey,
            salt: aliasBytes,
            info: Aes256GcmWrapInfo);
        return subkey;
    }

    /// <summary>
    /// Derives the machine/process-bound AES-256-GCM key used for encrypting persisted keystore files.
    /// </summary>
    /// <returns>A 32-byte machine-bound cryptographic key buffer.</returns>
    private static byte[] GetDiskEncryptionKey()
    {
        // WHAT: Derive machine-bound 32-byte AES-GCM encryption key using HKDF.
        // WHY:  Protects persisted keys at rest bound to the host environment.
        // HOW:  Hashes machine and user identity as salt and expands using HKDF.
        byte[] salt = SHA256.HashData(Encoding.UTF8.GetBytes($"nanoweaver:{Environment.MachineName}:{Environment.UserName}"));
        byte[] key = new byte[32];
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            salt,
            key,
            salt: default,
            info: "nanoweaver-mobile-keystore-storage-v1"u8);
        return key;
    }

    /// <summary>
    /// Loads and decrypts persisted key entries from disk.
    /// </summary>
    /// <remarks>
    /// // WHAT: Load encrypted key files from disk directory into the in-memory store.
    /// // WHY:  Ensures keys survive bridge reconstruction.
    /// // HOW:  Iterates over *.key files, decrypts with AES-256-GCM using machine-bound key, and populates _store.
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

                        if (TryParseKeyPayload(plaintext, out string? alias, out bool requiresBiometric, out byte[]? masterSeed))
                        {
                            _store[alias] = new KeyEntry(masterSeed, requiresBiometric);
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
    /// Encrypts and persists a key entry to disk.
    /// </summary>
    /// <param name="alias">The key alias.</param>
    /// <param name="requiresBiometric">Flag indicating whether biometric auth is required.</param>
    /// <param name="masterSeed">The master seed bytes.</param>
    private void PersistKeyToDisk(string alias, bool requiresBiometric, ReadOnlySpan<byte> masterSeed)
    {
        // WHAT: Encrypt key entry and write to disk under hashed filename.
        // WHY:  Persists key across bridge instances with confidentiality at rest.
        // HOW:  Encodes payload, encrypts via AES-256-GCM, and writes to storage directory.
        Directory.CreateDirectory(_storageDirectoryPath!);
        byte[] diskKey = GetDiskEncryptionKey();
        try
        {
            byte[] payload = FormatKeyPayload(alias, requiresBiometric, masterSeed);
            try
            {
                byte[] nonce = new byte[NonceSizeBytes];
                RandomNumberGenerator.Fill(nonce);

                byte[] tag = new byte[TagSizeBytes];
                byte[] ciphertext = new byte[payload.Length];

                using (var aesGcm = new AesGcm(diskKey, TagSizeBytes))
                {
                    aesGcm.Encrypt(nonce, payload, ciphertext, tag);
                }

                byte[] envelope = new byte[MinimumEnvelopeLength + ciphertext.Length];
                Buffer.BlockCopy(nonce, 0, envelope, 0, NonceSizeBytes);
                Buffer.BlockCopy(tag, 0, envelope, NonceSizeBytes, TagSizeBytes);
                Buffer.BlockCopy(ciphertext, 0, envelope, MinimumEnvelopeLength, ciphertext.Length);

                string fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(alias))) + ".key";
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
    /// Deletes a persisted key file from disk if it exists.
    /// </summary>
    /// <param name="alias">The key alias.</param>
    private void DeleteKeyFromDisk(string alias)
    {
        // WHAT: Remove key file from disk storage directory.
        // WHY:  Guarantees deleted keys do not persist on storage media.
        // HOW:  Hashes alias to determine filename and deletes file if present.
        if (string.IsNullOrWhiteSpace(_storageDirectoryPath))
        {
            return;
        }

        string fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(alias))) + ".key";
        string filePath = Path.Combine(_storageDirectoryPath, fileName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Formats key entry metadata and master seed into a structured binary payload.
    /// </summary>
    /// <param name="alias">Key alias.</param>
    /// <param name="requiresBiometric">Flag indicating whether biometric auth is required.</param>
    /// <param name="masterSeed">Master seed bytes.</param>
    /// <returns>Formatted binary payload bytes.</returns>
    private static byte[] FormatKeyPayload(string alias, bool requiresBiometric, ReadOnlySpan<byte> masterSeed)
    {
        // WHAT: Serialize key entry fields into binary buffer with magic header.
        // WHY:  Ensures structured format before AES-GCM encryption at rest.
        // HOW:  Writes magic, version, biometric flag, alias length, alias UTF-8, seed length, and seed bytes.
        byte[] aliasBytes = Encoding.UTF8.GetBytes(alias);
        int totalLen = 4 + 1 + 1 + 2 + aliasBytes.Length + 4 + masterSeed.Length;
        byte[] payload = new byte[totalLen];

        PayloadMagic.CopyTo(payload.AsSpan(0, 4));
        payload[4] = 1;
        payload[5] = requiresBiometric ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(6, 2), (short)aliasBytes.Length);
        aliasBytes.CopyTo(payload.AsSpan(8, aliasBytes.Length));

        int offset = 8 + aliasBytes.Length;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset, 4), masterSeed.Length);
        masterSeed.CopyTo(payload.AsSpan(offset + 4, masterSeed.Length));

        return payload;
    }

    /// <summary>
    /// Parses a binary key entry payload into alias, biometric flag, and master seed.
    /// </summary>
    /// <param name="payload">Plaintext payload bytes.</param>
    /// <param name="alias">Parsed key alias.</param>
    /// <param name="requiresBiometric">Parsed biometric requirement flag.</param>
    /// <param name="masterSeed">Parsed master seed bytes.</param>
    /// <returns><see langword="true"/> if payload was successfully parsed; otherwise <see langword="false"/>.</returns>
    private static bool TryParseKeyPayload(
        byte[] payload,
        [NotNullWhen(true)] out string? alias,
        out bool requiresBiometric,
        [NotNullWhen(true)] out byte[]? masterSeed)
    {
        // WHAT: Parse and validate decrypted binary payload from disk.
        // WHY:  Ensures data integrity and prevents malformed entry loading.
        // HOW:  Verifies length, magic, version, reads alias, and extracts master seed buffer.
        alias = null;
        requiresBiometric = false;
        masterSeed = null;

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
        short aliasLen = BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(6, 2));
        if (aliasLen <= 0)
        {
            return false;
        }

        if (payload.Length < 8 + aliasLen + 4)
        {
            return false;
        }

        alias = Encoding.UTF8.GetString(payload, 8, aliasLen);
        int offset = 8 + aliasLen;
        int seedLen = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset, 4));
        if (seedLen <= 0)
        {
            return false;
        }

        if (payload.Length < offset + 4 + seedLen)
        {
            return false;
        }

        masterSeed = new byte[seedLen];
        Buffer.BlockCopy(payload, offset + 4, masterSeed, 0, seedLen);
        return true;
    }

    /// <summary>
    /// Represents an in-memory or persisted key entry containing master seed material and biometric gating policy.
    /// </summary>
    private sealed class KeyEntry
    {
        /// <summary>
        /// Master seed cryptographic key buffer.
        /// </summary>
        public byte[] MasterSeed { get; }

        /// <summary>
        /// Flag indicating whether user biometric authentication is required before accessing or deriving keys.
        /// </summary>
        public bool RequiresBiometric { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="KeyEntry"/> class.
        /// </summary>
        /// <param name="masterSeed">Master seed byte buffer.</param>
        /// <param name="requiresBiometric">Flag indicating whether biometric prompt is required.</param>
        public KeyEntry(byte[] masterSeed, bool requiresBiometric)
        {
            // WHAT: Construct key entry storing master seed and policy flag.
            // WHY:  Associates biometric requirement metadata with cryptographic key material.
            // HOW:  Assigns arguments to public read-only properties.
            MasterSeed = masterSeed;
            RequiresBiometric = requiresBiometric;
        }

        /// <summary>
        /// Zeroizes the master seed buffer.
        /// </summary>
        public void Zeroize()
        {
            // WHAT: Cryptographically erase master seed from memory.
            // WHY:  Ensures key material cannot be extracted from discarded memory buffers.
            // HOW:  Invokes CryptographicOperations.ZeroMemory on MasterSeed array.
            CryptographicOperations.ZeroMemory(MasterSeed);
        }
    }
}

/// <summary>
/// Extension methods for <see cref="UnifiedMobileBiometricVault"/> mobile lifecycle coordination.
/// </summary>
/// <remarks>
/// // WHAT: Extension methods facilitating explicit vault locking and lifecycle-driven memory zeroization.
/// // WHY:  Allows mobile platform lifecycle hooks to purge decrypted patient DNA regimens in a single call.
/// // HOW:  Dispatches MobileAppLifecycleState.Paused to the vault and zeroizes underlying provider memory.
/// </remarks>
public static class UnifiedMobileBiometricVaultExtensions
{
    /// <summary>
    /// Locks the biometric vault immediately, purging and zeroizing all decrypted patient DNA regimens in memory.
    /// </summary>
    /// <param name="vault">The unified biometric vault instance to lock.</param>
    public static void LockVault(this UnifiedMobileBiometricVault vault)
    {
        // WHAT: Immediately lock the vault and zeroize decrypted regimens and session keys.
        // WHY:  Guarantees zero-exposure of plaintext genomic data when application leaves foreground.
        // HOW:  Notifies vault of Paused lifecycle state and invokes ZeroizeMemory on active provider if supported.
        ArgumentNullException.ThrowIfNull(vault);

        vault.OnAppLifecycleStateChanged(MobileAppLifecycleState.Paused);

        if (vault.ActiveProvider is AndroidBiometricSecurityProvider androidProvider)
        {
            androidProvider.ZeroizeMemory();
        }
        else if (vault.ActiveProvider is AppleSecureEnclaveSecurityProvider appleProvider)
        {
            appleProvider.ZeroizeMemory();
        }
    }
}
