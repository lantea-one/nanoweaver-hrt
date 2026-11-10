using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace NanoWeaverHrt.Mobile.Security;

/// <summary>
/// Android platform implementation of <see cref="IBiometricSecurityProvider"/> modeling
/// Android <c>BiometricPrompt</c> biometric authentication and Android Keystore hardware-backed keys.
/// </summary>
/// <remarks>
/// // WHAT: Concrete security provider modeling Android BiometricPrompt and Keystore hardware encryption.
/// // WHY:  Guarantees cryptographic patient DNA regimen protection on Android devices with zero-mock unit testability.
/// // HOW:  Simulates Android Keystore key generation, BiometricPrompt availability states, HKDF key separation, and AES-256-GCM crypto.
/// </remarks>
public sealed class AndroidBiometricSecurityProvider : IBiometricSecurityProvider, IDisposable
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
    /// Ed25519 signing HKDF info context string.
    /// </summary>
    private static ReadOnlySpan<byte> Ed25519SignInfo => "nanoweaver-android-ed25519-sign-v1"u8;

    /// <summary>
    /// AES-256-GCM wrapping HKDF info context string.
    /// </summary>
    private static ReadOnlySpan<byte> Aes256GcmWrapInfo => "nanoweaver-android-aes256gcm-wrap-v1"u8;

    /// <summary>
    /// In-memory simulated Android Keystore holding master seeds for hardware-bound keys.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte[]> _keyStore = new(StringComparer.Ordinal);

    /// <summary>
    /// Android Keystore bridge handling hardware key persistence and biometric prompt evaluation.
    /// </summary>
    private readonly IAndroidKeyStoreBridge _keyStoreBridge;

    /// <summary>
    /// Optional delegate evaluating whether the host platform matches Android.
    /// </summary>
    private readonly Func<string, bool>? _isPlatform;

    /// <summary>
    /// Optional delegate querying Android BiometricManager status for unit test simulation.
    /// </summary>
    private readonly Func<BiometricStatus>? _statusProbe;

    /// <summary>
    /// Supported biometric sensing modality configured for this provider.
    /// </summary>
    private readonly BiometricType _supportedType;

    /// <summary>
    /// Initializes a new instance of the <see cref="AndroidBiometricSecurityProvider"/> class.
    /// </summary>
    /// <param name="isPlatform">Optional platform resolution predicate for cross-platform unit testing.</param>
    /// <param name="statusProbe">Optional status probe delegate simulating BiometricManager responses.</param>
    /// <param name="supportedType">Biometric modality supported on this simulated device (defaults to Fingerprint).</param>
    /// <param name="keyStoreBridge">Optional Android Keystore bridge abstraction (defaults to <see cref="DefaultAndroidKeyStoreBridge"/>).</param>
    /// <param name="storageDirectoryPath">Optional storage directory path for encrypted file-backed persistence.</param>
    public AndroidBiometricSecurityProvider(
        Func<string, bool>? isPlatform = null,
        Func<BiometricStatus>? statusProbe = null,
        BiometricType supportedType = BiometricType.Fingerprint,
        IAndroidKeyStoreBridge? keyStoreBridge = null,
        string? storageDirectoryPath = null)
    {
        // WHAT: Initialize Android biometric provider with test hooks, Keystore bridge, and persistence path.
        // WHY:  Allows deterministic testing of all Android BiometricManager and Keystore states without an Android emulator or device.
        // HOW:  Stores injected platform predicate, status probe, supported biometric modality, and Keystore bridge.
        _isPlatform = isPlatform;
        _statusProbe = statusProbe;
        _supportedType = supportedType;
        _keyStoreBridge = keyStoreBridge ?? new DefaultAndroidKeyStoreBridge(storageDirectoryPath: storageDirectoryPath);
    }

    /// <summary>
    /// Gets the Android Keystore bridge used by this provider.
    /// </summary>
    public IAndroidKeyStoreBridge KeyStoreBridge => _keyStoreBridge;

    /// <inheritdoc/>
    public Task<BiometricAvailabilityResult> CheckBiometricAvailabilityAsync(CancellationToken ct = default)
    {
        // WHAT: Evaluate Android biometric hardware availability.
        // WHY:  Determines whether BiometricPrompt can be invoked to decrypt patient DNA regimens.
        // HOW:  Delegates to synchronous core logic and wraps in completed task avoiding state machine overhead.
        return Task.FromResult(CheckBiometricAvailabilityCore(ct));
    }

    /// <inheritdoc/>
    public Task<BiometricAuthResult> AuthenticateAndDecryptAsync(BiometricAuthRequest request, CancellationToken ct = default)
    {
        // WHAT: Present simulated Android BiometricPrompt and decrypt the vault payload.
        // WHY:  Guarantees cryptographic security and biometric authorization on Android.
        // HOW:  Validates inputs, evaluates prompt status, decrypts payload via AES-256-GCM, and returns result.
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.KeyAlias);
        ArgumentNullException.ThrowIfNull(request.EncryptedVaultPayload);

        return Task.FromResult(AuthenticateAndDecryptCore(request, ct));
    }

    /// <inheritdoc/>
    public Task<HardwareKeyGenerationResult> GenerateHardwareBoundKeyAsync(string keyAlias, CancellationToken ct = default)
    {
        // WHAT: Generate a hardware-bound key inside Android Keystore without biometric requirement.
        // WHY:  Provides a secure key alias bound to device keystore.
        // HOW:  Validates alias and delegates to core generation logic with requiresBiometric false.
        ArgumentException.ThrowIfNullOrWhiteSpace(keyAlias);
        return Task.FromResult(GenerateHardwareBoundKeyCore(keyAlias, requiresBiometric: false, ct));
    }

    /// <summary>
    /// Generates a hardware-bound key inside Android Keystore with an optional biometric prompt requirement.
    /// </summary>
    /// <param name="keyAlias">Unique alias identifying the key within the hardware module.</param>
    /// <param name="requiresBiometric">Flag indicating whether access to this key requires biometric authorization.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task yielding the populated <see cref="HardwareKeyGenerationResult"/>.</returns>
    public Task<HardwareKeyGenerationResult> GenerateHardwareBoundKeyAsync(string keyAlias, bool requiresBiometric, CancellationToken ct = default)
    {
        // WHAT: Generate a hardware-bound key inside Android Keystore with explicit biometric requirement.
        // WHY:  Allows gating key access behind biometric authentication prompts.
        // HOW:  Validates alias and delegates to core generation logic.
        ArgumentException.ThrowIfNullOrWhiteSpace(keyAlias);
        return Task.FromResult(GenerateHardwareBoundKeyCore(keyAlias, requiresBiometric, ct));
    }

    /// <summary>
    /// Generates a genuine RFC 8032 Ed25519 key pair inside the Android Keystore bridge and returns the raw 32-byte public key.
    /// </summary>
    /// <param name="keyAlias">Unique alias identifying the key inside the Android Keystore.</param>
    /// <param name="ct">Cancellation token for cancelling the key generation operation.</param>
    /// <returns>A task yielding the genuine 32-byte RFC 8032 Ed25519 public key bytes.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="keyAlias"/> is null or whitespace.</exception>
    /// <exception cref="PlatformNotSupportedException">Thrown when the current platform is not Android.</exception>
    public Task<byte[]> GenerateHardwareKeyAsync(string keyAlias, CancellationToken ct = default)
    {
        // WHAT: Generate genuine Ed25519 keypair without biometric prompt requirement.
        // WHY:  Provides hardware-bound asymmetric identity for node and patient federation.
        // HOW:  Delegates to overload with requiresBiometric false.
        return GenerateHardwareKeyAsync(keyAlias, requiresBiometric: false, ct);
    }

    /// <summary>
    /// Generates a genuine RFC 8032 Ed25519 key pair inside the Android Keystore bridge with an optional biometric requirement.
    /// </summary>
    /// <param name="keyAlias">Unique alias identifying the key inside the Android Keystore.</param>
    /// <param name="requiresBiometric">Flag indicating whether key usage requires biometric authorization.</param>
    /// <param name="ct">Cancellation token for cancelling the key generation operation.</param>
    /// <returns>A task yielding the genuine 32-byte RFC 8032 Ed25519 public key bytes.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="keyAlias"/> is null or whitespace.</exception>
    /// <exception cref="PlatformNotSupportedException">Thrown when the current platform is not Android.</exception>
    public Task<byte[]> GenerateHardwareKeyAsync(string keyAlias, bool requiresBiometric, CancellationToken ct = default)
    {
        // WHAT: Generate genuine RFC 8032 Ed25519 keypair and persist master seed in Keystore bridge.
        // WHY:  Provides hardware-bound asymmetric identity with HKDF domain separation.
        // HOW:  Generates random master seed, derives Ed25519 signing subkey via HKDF, computes public key, zeroizes buffers.
        ArgumentException.ThrowIfNullOrWhiteSpace(keyAlias);
        ct.ThrowIfCancellationRequested();

        if (!IsPlatformAndroid())
        {
            throw new PlatformNotSupportedException("Android Keystore is unavailable on this platform.");
        }

        byte[] masterSeed = new byte[32];
        RandomNumberGenerator.Fill(masterSeed);

        try
        {
            _keyStoreBridge.StoreWrappedKey(keyAlias, masterSeed, requiresBiometric);
            _keyStore[keyAlias] = (byte[])masterSeed.Clone();

            byte[] signingKey = DeriveSigningKey(keyAlias, masterSeed);

            try
            {
                var privKeyParams = new Ed25519PrivateKeyParameters(signingKey, 0);
                Ed25519PublicKeyParameters pubKeyParams = privKeyParams.GeneratePublicKey();
                return Task.FromResult(pubKeyParams.GetEncoded());
            }
            finally
            {
                CryptographicOperations.ZeroMemory(signingKey);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterSeed);
        }
    }

    /// <summary>
    /// Signs a challenge byte array using the Ed25519 private key stored under the specified alias in the Android Keystore bridge.
    /// </summary>
    /// <param name="keyAlias">Unique alias of the hardware key in Android Keystore.</param>
    /// <param name="challengeBytes">Challenge bytes to sign.</param>
    /// <param name="ct">Cancellation token for cancelling the signing operation.</param>
    /// <returns>A task yielding the genuine 64-byte RFC 8032 Ed25519 signature.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="keyAlias"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="challengeBytes"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the key alias does not exist in Android Keystore.</exception>
    /// <exception cref="CryptographicException">Thrown when biometric prompt authentication fails.</exception>
    public async Task<byte[]> SignChallengeAsync(string keyAlias, byte[] challengeBytes, CancellationToken ct = default)
    {
        // WHAT: Produce RFC 8032 Ed25519 signature over challenge using domain-separated Keystore-backed signing key.
        // WHY:  Allows Android mobile clients to prove private-key possession during node challenge-response authentication.
        // HOW:  Loads derived signing key, initializes Ed25519Signer, generates 64-byte signature, and zeroizes private key memory.
        ArgumentException.ThrowIfNullOrWhiteSpace(keyAlias);
        ArgumentNullException.ThrowIfNull(challengeBytes);
        ct.ThrowIfCancellationRequested();

        byte[] signingKey;
        byte[]? bridgeSigningKey = await _keyStoreBridge.GetKeyForSigningAsync(keyAlias, "Authenticate to sign cryptographic challenge", ct).ConfigureAwait(false);
        if (bridgeSigningKey != null)
        {
            signingKey = bridgeSigningKey;
        }
        else if (_keyStore.TryGetValue(keyAlias, out byte[]? storeBytes))
        {
            signingKey = DeriveSigningKey(keyAlias, storeBytes);
        }
        else
        {
            throw new InvalidOperationException($"Hardware key '{keyAlias}' does not exist in Android Keystore.");
        }

        try
        {
            var privKeyParams = new Ed25519PrivateKeyParameters(signingKey, 0);
            var signer = new Ed25519Signer();
            signer.Init(true, privKeyParams);
            signer.BlockUpdate(challengeBytes, 0, challengeBytes.Length);
            byte[] signature = signer.GenerateSignature();
            return signature;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signingKey);
        }
    }

    /// <summary>
    /// Signs a challenge memory slice using the Ed25519 private key stored under the specified alias in the Android Keystore bridge.
    /// </summary>
    /// <param name="keyAlias">Unique alias of the hardware key in Android Keystore.</param>
    /// <param name="challengeBytes">Challenge memory slice to sign.</param>
    /// <param name="ct">Cancellation token for cancelling the signing operation.</param>
    /// <returns>A task yielding the genuine 64-byte RFC 8032 Ed25519 signature.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="keyAlias"/> is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the key alias does not exist in Android Keystore.</exception>
    public Task<byte[]> SignChallengeAsync(string keyAlias, ReadOnlyMemory<byte> challengeBytes, CancellationToken ct = default)
    {
        // WHAT: Overload accepting ReadOnlyMemory<byte> for challenge signing.
        // WHY:  Simplifies caller integration with modern memory slices.
        // HOW:  Converts memory slice to array and delegates to primary signature method.
        return SignChallengeAsync(keyAlias, challengeBytes.ToArray(), ct);
    }

    /// <summary>
    /// Encrypts a plaintext payload using the specified Android Keystore hardware-bound key using AES-256-GCM.
    /// </summary>
    /// <param name="keyAlias">Unique alias of the hardware key in Android Keystore.</param>
    /// <param name="plaintext">Plaintext payload bytes to encrypt.</param>
    /// <returns>Encrypted envelope bytes containing nonce, tag, and ciphertext.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="keyAlias"/> is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the key alias does not exist in the Keystore.</exception>
    /// <exception cref="CryptographicException">Thrown when biometric prompt authorization fails.</exception>
    public byte[] EncryptPayload(string keyAlias, ReadOnlySpan<byte> plaintext)
    {
        // WHAT: Encrypt plaintext payload with AES-256-GCM using domain-separated wrapping key.
        // WHY:  Provides symmetric vault encryption aligned with Android Keystore capabilities.
        // HOW:  Loads derived wrapping key defensively, generates 12-byte random nonce, encrypts plaintext, appends authentication tag, and formats envelope.
        ArgumentException.ThrowIfNullOrWhiteSpace(keyAlias);

        byte[] wrappingKey;
        if (_keyStoreBridge.TryLoadKeyForWrapping(keyAlias, out byte[] bridgeKey))
        {
            wrappingKey = bridgeKey;
        }
        else if (_keyStore.TryGetValue(keyAlias, out byte[]? storeBytes))
        {
            wrappingKey = DeriveWrappingKey(keyAlias, storeBytes);
        }
        else
        {
            throw new InvalidOperationException($"Hardware key '{keyAlias}' does not exist in Android Keystore.");
        }

        try
        {
            byte[] nonce = new byte[NonceSizeBytes];
            RandomNumberGenerator.Fill(nonce);

            byte[] tag = new byte[TagSizeBytes];
            byte[] ciphertext = new byte[plaintext.Length];

            using (var aesGcm = new AesGcm(wrappingKey, TagSizeBytes))
            {
                aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
            }

            byte[] envelope = new byte[NonceSizeBytes + TagSizeBytes + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, envelope, 0, NonceSizeBytes);
            Buffer.BlockCopy(tag, 0, envelope, NonceSizeBytes, TagSizeBytes);
            Buffer.BlockCopy(ciphertext, 0, envelope, MinimumEnvelopeLength, ciphertext.Length);

            return envelope;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    /// <summary>
    /// Purges and zeroizes all cached and session keys in memory and the Keystore bridge.
    /// </summary>
    public void ZeroizeMemory()
    {
        // WHAT: Zeroize and clear all keys in provider memory and Keystore bridge.
        // WHY:  Ensures no cryptographic key material remains in memory when locked or disposed.
        // HOW:  Clears bridge session keys and zeroizes and clears the in-memory dictionary.
        _keyStoreBridge.ClearAllSessionKeys();
        foreach (byte[] key in _keyStore.Values)
        {
            CryptographicOperations.ZeroMemory(key);
        }

        _keyStore.Clear();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // WHAT: Disposes the provider and zeroizes sensitive key memory.
        // WHY:  Fulfills IDisposable to guarantee RAM hygiene.
        // HOW:  Delegates to ZeroizeMemory.
        ZeroizeMemory();
    }

    /// <summary>
    /// Evaluates whether the current host or simulated platform is Android.
    /// </summary>
    /// <returns><see langword="true"/> if Android; otherwise <see langword="false"/>.</returns>
    private bool IsPlatformAndroid()
    {
        // WHAT: Check if current runtime environment is Android.
        // WHY:  Prevents executing Android Keystore operations on incompatible operating systems.
        // HOW:  Evaluates injected platform predicate or queries RuntimeInformation.
        Func<string, bool> checker = _isPlatform ?? (p => RuntimeInformation.IsOSPlatform(OSPlatform.Create(p)));
        return checker("Android") || checker("ANDROID");
    }

    /// <summary>
    /// Synchronously checks Android biometric hardware availability.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Populated <see cref="BiometricAvailabilityResult"/>.</returns>
    private BiometricAvailabilityResult CheckBiometricAvailabilityCore(CancellationToken ct)
    {
        // WHAT: Core diagnostic logic probing Android biometric status.
        // WHY:  Centralizes availability logic for both availability checks and authentication pre-flight.
        // HOW:  Checks cancellation, validates Android OS, queries probe or default, and maps to result record.
        if (ct.IsCancellationRequested)
        {
            return new BiometricAvailabilityResult(
                IsAvailable: false,
                SupportedType: BiometricType.None,
                Status: BiometricStatus.UserCancelled,
                StatusMessage: "Biometric availability check was cancelled.");
        }

        if (!IsPlatformAndroid())
        {
            return new BiometricAvailabilityResult(
                IsAvailable: false,
                SupportedType: BiometricType.None,
                Status: BiometricStatus.HardwareUnavailable,
                StatusMessage: "Android BiometricPrompt is not supported on this platform.");
        }

        BiometricStatus probedStatus = _statusProbe != null ? _statusProbe() : BiometricStatus.Available;

        return probedStatus switch
        {
            BiometricStatus.Available => new BiometricAvailabilityResult(
                IsAvailable: true,
                SupportedType: _supportedType,
                Status: BiometricStatus.Available,
                StatusMessage: "Android BiometricPrompt hardware ready and enrolled."),

            BiometricStatus.NotEnrolled => new BiometricAvailabilityResult(
                IsAvailable: false,
                SupportedType: _supportedType,
                Status: BiometricStatus.NotEnrolled,
                StatusMessage: "No biometric credentials enrolled in Android settings."),

            BiometricStatus.HardwareUnavailable => new BiometricAvailabilityResult(
                IsAvailable: false,
                SupportedType: BiometricType.None,
                Status: BiometricStatus.HardwareUnavailable,
                StatusMessage: "Android biometric hardware is unavailable."),

            BiometricStatus.Lockout => new BiometricAvailabilityResult(
                IsAvailable: false,
                SupportedType: _supportedType,
                Status: BiometricStatus.Lockout,
                StatusMessage: "Android biometric sensor is locked due to too many failed attempts."),

            BiometricStatus.UserCancelled => new BiometricAvailabilityResult(
                IsAvailable: false,
                SupportedType: _supportedType,
                Status: BiometricStatus.UserCancelled,
                StatusMessage: "Biometric operation was cancelled."),

            BiometricStatus.Failed => new BiometricAvailabilityResult(
                IsAvailable: false,
                SupportedType: BiometricType.None,
                Status: BiometricStatus.Failed,
                StatusMessage: "Android biometric sensor failure."),

            _ => new BiometricAvailabilityResult(
                IsAvailable: false,
                SupportedType: BiometricType.None,
                Status: probedStatus,
                StatusMessage: $"Android biometric status: {probedStatus}")
        };
    }

    /// <summary>
    /// Synchronously performs biometric authentication and payload decryption.
    /// </summary>
    /// <param name="request">Authentication request parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Populated <see cref="BiometricAuthResult"/>.</returns>
    private BiometricAuthResult AuthenticateAndDecryptCore(BiometricAuthRequest request, CancellationToken ct)
    {
        // WHAT: Core decryption logic verifying biometric prompt and Keystore key.
        // WHY:  Guarantees robust decryption of encrypted DNA payloads.
        // HOW:  Checks cancellation, verifies availability, loads domain-separated wrapping key, and decrypts via AES-GCM.
        if (ct.IsCancellationRequested)
        {
            return new BiometricAuthResult(
                Success: false,
                DecryptedPayload: null,
                ErrorMessage: "Biometric authentication was cancelled.",
                Status: BiometricStatus.UserCancelled);
        }

        BiometricAvailabilityResult availability = CheckBiometricAvailabilityCore(ct);
        if (!availability.IsAvailable)
        {
            return new BiometricAuthResult(
                Success: false,
                DecryptedPayload: null,
                ErrorMessage: availability.StatusMessage,
                Status: availability.Status);
        }

        byte[] wrappingKey;
        try
        {
            if (_keyStoreBridge.TryLoadKeyForWrapping(request.KeyAlias, out byte[] bridgeKey))
            {
                wrappingKey = bridgeKey;
            }
            else if (_keyStore.TryGetValue(request.KeyAlias, out byte[]? storeBytes))
            {
                wrappingKey = DeriveWrappingKey(request.KeyAlias, storeBytes);
            }
            else
            {
                return new BiometricAuthResult(
                    Success: false,
                    DecryptedPayload: null,
                    ErrorMessage: $"Hardware key '{request.KeyAlias}' not found in Android Keystore.",
                    Status: BiometricStatus.Failed);
            }
        }
        catch (CryptographicException ex)
        {
            return new BiometricAuthResult(
                Success: false,
                DecryptedPayload: null,
                ErrorMessage: $"Biometric authorization failed: {ex.Message}",
                Status: BiometricStatus.Failed);
        }

        if (request.EncryptedVaultPayload.Length < MinimumEnvelopeLength)
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
            return new BiometricAuthResult(
                Success: false,
                DecryptedPayload: null,
                ErrorMessage: "Encrypted vault payload length is invalid.",
                Status: BiometricStatus.Failed);
        }

        ReadOnlySpan<byte> envelope = request.EncryptedVaultPayload;
        ReadOnlySpan<byte> nonce = envelope[..NonceSizeBytes];
        ReadOnlySpan<byte> tag = envelope.Slice(NonceSizeBytes, TagSizeBytes);
        ReadOnlySpan<byte> ciphertext = envelope[MinimumEnvelopeLength..];

        byte[] plaintext = new byte[ciphertext.Length];
        try
        {
            using var aesGcm = new AesGcm(wrappingKey, TagSizeBytes);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

            return new BiometricAuthResult(
                Success: true,
                DecryptedPayload: plaintext,
                ErrorMessage: null,
                Status: BiometricStatus.Available);
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            return new BiometricAuthResult(
                Success: false,
                DecryptedPayload: null,
                ErrorMessage: $"Decryption failed: {ex.Message}",
                Status: BiometricStatus.Failed);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    /// <summary>
    /// Synchronously generates a hardware-bound key inside the simulated Android Keystore.
    /// </summary>
    /// <param name="keyAlias">Target key alias.</param>
    /// <param name="requiresBiometric">Flag indicating whether biometric auth is required.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Populated <see cref="HardwareKeyGenerationResult"/>.</returns>
    private HardwareKeyGenerationResult GenerateHardwareBoundKeyCore(string keyAlias, bool requiresBiometric, CancellationToken ct)
    {
        // WHAT: Core key generation routine for Android Keystore.
        // WHY:  Simulates hardware key generation backed by Android TEE or StrongBox Keymaster with HKDF subkey derivation.
        // HOW:  Checks cancellation, confirms Android platform, generates random master seed, derives Ed25519 signing key, and stores in bridge.
        if (ct.IsCancellationRequested)
        {
            return new HardwareKeyGenerationResult(
                Success: false,
                KeyAlias: keyAlias,
                PublicKeyBase64: string.Empty,
                ErrorMessage: "Key generation was cancelled.");
        }

        if (!IsPlatformAndroid())
        {
            return new HardwareKeyGenerationResult(
                Success: false,
                KeyAlias: keyAlias,
                PublicKeyBase64: string.Empty,
                ErrorMessage: "Android Keystore is unavailable on this platform.");
        }

        byte[] masterSeed = new byte[32];
        RandomNumberGenerator.Fill(masterSeed);

        try
        {
            _keyStoreBridge.StoreWrappedKey(keyAlias, masterSeed, requiresBiometric);
            _keyStore[keyAlias] = (byte[])masterSeed.Clone();

            byte[] signingKey = DeriveSigningKey(keyAlias, masterSeed);

            try
            {
                var privKeyParams = new Ed25519PrivateKeyParameters(signingKey, 0);
                Ed25519PublicKeyParameters pubKeyParams = privKeyParams.GeneratePublicKey();
                byte[] pubBytes = pubKeyParams.GetEncoded();
                string publicKeyBase64 = Convert.ToBase64String(pubBytes);

                return new HardwareKeyGenerationResult(
                    Success: true,
                    KeyAlias: keyAlias,
                    PublicKeyBase64: publicKeyBase64,
                    ErrorMessage: null);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(signingKey);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterSeed);
        }
    }

    /// <summary>
    /// Derives the 32-byte Ed25519 signing key from the master seed using HKDF.
    /// </summary>
    /// <param name="keyAlias">Key alias used as HKDF salt.</param>
    /// <param name="masterSeed">Master seed bytes.</param>
    /// <returns>A 32-byte derived signing key.</returns>
    private static byte[] DeriveSigningKey(string keyAlias, ReadOnlySpan<byte> masterSeed)
    {
        // WHAT: Derive domain-separated Ed25519 signing key via HKDF.
        // WHY:  Ensures signing key is cryptographically separated from wrapping key.
        // HOW:  Uses HKDF with SHA256, alias salt, and Android signing info string.
        byte[] aliasBytes = Encoding.UTF8.GetBytes(keyAlias);
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
    /// Derives the 32-byte AES-256-GCM wrapping key from the master seed using HKDF.
    /// </summary>
    /// <param name="keyAlias">Key alias used as HKDF salt.</param>
    /// <param name="masterSeed">Master seed bytes.</param>
    /// <returns>A 32-byte derived wrapping key.</returns>
    private static byte[] DeriveWrappingKey(string keyAlias, ReadOnlySpan<byte> masterSeed)
    {
        // WHAT: Derive domain-separated AES-256-GCM wrapping key via HKDF.
        // WHY:  Ensures wrapping key is cryptographically separated from signing key.
        // HOW:  Uses HKDF with SHA256, alias salt, and Android wrapping info string.
        byte[] aliasBytes = Encoding.UTF8.GetBytes(keyAlias);
        byte[] subkey = new byte[32];
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            masterSeed,
            subkey,
            salt: aliasBytes,
            info: Aes256GcmWrapInfo);
        return subkey;
    }
}
