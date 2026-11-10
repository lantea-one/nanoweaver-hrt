using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace NanoWeaverHrt.Mobile.Security;

/// <summary>
/// Apple platform implementation of <see cref="IBiometricSecurityProvider"/> modeling
/// iOS/macOS <c>LocalAuthentication</c> (<c>LAContext</c>) and Apple Secure Enclave hardware keychain integration.
/// </summary>
/// <remarks>
/// // WHAT: Apple Secure Enclave and LocalAuthentication biometric security provider.
/// // WHY:  Guarantees cryptographic patient DNA regimen protection on Apple iOS/iPadOS/macOS devices.
/// // HOW:  Simulates Apple Secure Enclave key generation, HKDF key separation, Touch ID / Face ID evaluation, and AES-256-GCM crypto.
/// </remarks>
public sealed class AppleSecureEnclaveSecurityProvider : IBiometricSecurityProvider, IDisposable
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
    private static ReadOnlySpan<byte> Ed25519SignInfo => "nanoweaver-ios-ed25519-sign-v1"u8;

    /// <summary>
    /// AES-256-GCM wrapping HKDF info context string.
    /// </summary>
    private static ReadOnlySpan<byte> Aes256GcmWrapInfo => "nanoweaver-ios-aes256gcm-wrap-v1"u8;

    /// <summary>
    /// In-memory simulated Apple Secure Enclave keychain holding master secrets for hardware-bound keys.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte[]> _keyStore = new(StringComparer.Ordinal);

    /// <summary>
    /// Apple Secure Enclave bridge handling hardware keychain persistence and LocalAuthentication policy evaluation.
    /// </summary>
    private readonly IAppleSecureEnclaveBridge _enclaveBridge;

    /// <summary>
    /// Optional delegate evaluating whether the host platform matches Apple iOS / macOS / Darwin.
    /// </summary>
    private readonly Func<string, bool>? _isPlatform;

    /// <summary>
    /// Optional delegate querying Apple LocalAuthentication status for unit test simulation.
    /// </summary>
    private readonly Func<BiometricStatus>? _statusProbe;

    /// <summary>
    /// Configured biometric modality supported on this simulated Apple hardware head.
    /// </summary>
    private readonly BiometricType _biometricType;

    /// <summary>
    /// Optional delegate dynamically resolving the active biometric modality for unit test simulation.
    /// </summary>
    private readonly Func<BiometricType>? _typeProbe;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppleSecureEnclaveSecurityProvider"/> class.
    /// </summary>
    /// <param name="isPlatform">Optional platform resolution predicate for cross-platform unit testing.</param>
    /// <param name="statusProbe">Optional status probe delegate simulating LAContext responses.</param>
    /// <param name="biometricType">Biometric modality supported on this Apple device (defaults to AppleFaceId).</param>
    /// <param name="typeProbe">Optional dynamic type probe delegate returning AppleFaceId or AppleTouchId.</param>
    /// <param name="enclaveBridge">Optional Apple Secure Enclave bridge abstraction (defaults to <see cref="DefaultAppleSecureEnclaveBridge"/>).</param>
    /// <param name="storageDirectoryPath">Optional storage directory path for encrypted file-backed persistence.</param>
    public AppleSecureEnclaveSecurityProvider(
        Func<string, bool>? isPlatform = null,
        Func<BiometricStatus>? statusProbe = null,
        BiometricType biometricType = BiometricType.AppleFaceId,
        Func<BiometricType>? typeProbe = null,
        IAppleSecureEnclaveBridge? enclaveBridge = null,
        string? storageDirectoryPath = null)
    {
        // WHAT: Initialize Apple Secure Enclave provider with test hooks, bridge, and persistence path.
        // WHY:  Allows deterministic testing of LAContext Touch ID / Face ID states without Apple hardware.
        // HOW:  Stores injected platform predicate, status probe, static modality, dynamic type probe, and bridge.
        _isPlatform = isPlatform;
        _statusProbe = statusProbe;
        _biometricType = biometricType;
        _typeProbe = typeProbe;
        _enclaveBridge = enclaveBridge ?? new DefaultAppleSecureEnclaveBridge(storageDirectoryPath: storageDirectoryPath);
    }

    /// <summary>
    /// Gets the Apple Secure Enclave bridge used by this provider.
    /// </summary>
    public IAppleSecureEnclaveBridge EnclaveBridge => _enclaveBridge;

    /// <inheritdoc/>
    public Task<BiometricAvailabilityResult> CheckBiometricAvailabilityAsync(CancellationToken ct = default)
    {
        // WHAT: Evaluate Apple LocalAuthentication hardware availability.
        // WHY:  Determines whether Touch ID or Face ID can be evaluated for vault decryption.
        // HOW:  Delegates to synchronous core logic and wraps in completed task.
        return Task.FromResult(CheckBiometricAvailabilityCore(ct));
    }

    /// <inheritdoc/>
    public Task<BiometricAuthResult> AuthenticateAndDecryptAsync(BiometricAuthRequest request, CancellationToken ct = default)
    {
        // WHAT: Present simulated LAContext prompt and decrypt the vault payload.
        // WHY:  Guarantees cryptographic patient DNA regimen protection on Apple platforms.
        // HOW:  Validates inputs, evaluates prompt status, decrypts payload via AES-256-GCM, and returns result.
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.KeyAlias);
        ArgumentNullException.ThrowIfNull(request.EncryptedVaultPayload);

        return Task.FromResult(AuthenticateAndDecryptCore(request, ct));
    }

    /// <inheritdoc/>
    public Task<HardwareKeyGenerationResult> GenerateHardwareBoundKeyAsync(string keyAlias, CancellationToken ct = default)
    {
        // WHAT: Generate a hardware-bound key inside Apple Secure Enclave without biometric requirement.
        // WHY:  Provides a secure key alias bound to enclave keychain.
        // HOW:  Validates alias and delegates to core generation logic with requiresBiometric false.
        ArgumentException.ThrowIfNullOrWhiteSpace(keyAlias);
        return Task.FromResult(GenerateHardwareBoundKeyCore(keyAlias, requiresBiometric: false, ct));
    }

    /// <summary>
    /// Generates a hardware-bound key inside Apple Secure Enclave with an optional biometric requirement.
    /// </summary>
    /// <param name="keyAlias">Unique alias identifying the key within the hardware module.</param>
    /// <param name="requiresBiometric">Flag indicating whether access to this key requires biometric authorization.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task yielding the populated <see cref="HardwareKeyGenerationResult"/>.</returns>
    public Task<HardwareKeyGenerationResult> GenerateHardwareBoundKeyAsync(string keyAlias, bool requiresBiometric, CancellationToken ct = default)
    {
        // WHAT: Generate a hardware-bound key inside Apple Secure Enclave with explicit biometric requirement.
        // WHY:  Allows gating key access behind LocalAuthentication biometric policy.
        // HOW:  Validates alias and delegates to core generation logic.
        ArgumentException.ThrowIfNullOrWhiteSpace(keyAlias);
        return Task.FromResult(GenerateHardwareBoundKeyCore(keyAlias, requiresBiometric, ct));
    }

    /// <summary>
    /// Generates a genuine RFC 8032 Ed25519 key pair inside the Apple Secure Enclave bridge and returns the raw 32-byte public key.
    /// </summary>
    /// <param name="keyAlias">Unique alias identifying the key inside the Apple Secure Enclave keychain.</param>
    /// <param name="ct">Cancellation token for cancelling the key generation operation.</param>
    /// <returns>A task yielding the genuine 32-byte RFC 8032 Ed25519 public key bytes.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="keyAlias"/> is null or whitespace.</exception>
    /// <exception cref="PlatformNotSupportedException">Thrown when the current platform is not Apple.</exception>
    public Task<byte[]> GenerateHardwareKeyAsync(string keyAlias, CancellationToken ct = default)
    {
        // WHAT: Generate genuine Ed25519 keypair without biometric prompt requirement.
        // WHY:  Provides hardware-bound asymmetric identity for node and patient federation.
        // HOW:  Delegates to overload with requiresBiometric false.
        return GenerateHardwareKeyAsync(keyAlias, requiresBiometric: false, ct);
    }

    /// <summary>
    /// Generates a genuine RFC 8032 Ed25519 key pair inside the Apple Secure Enclave bridge with an optional biometric requirement.
    /// </summary>
    /// <param name="keyAlias">Unique alias identifying the key inside the Apple Secure Enclave keychain.</param>
    /// <param name="requiresBiometric">Flag indicating whether key usage requires biometric authorization.</param>
    /// <param name="ct">Cancellation token for cancelling the key generation operation.</param>
    /// <returns>A task yielding the genuine 32-byte RFC 8032 Ed25519 public key bytes.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="keyAlias"/> is null or whitespace.</exception>
    /// <exception cref="PlatformNotSupportedException">Thrown when the current platform is not Apple.</exception>
    public Task<byte[]> GenerateHardwareKeyAsync(string keyAlias, bool requiresBiometric, CancellationToken ct = default)
    {
        // WHAT: Generate genuine RFC 8032 Ed25519 keypair and persist master secret in Secure Enclave bridge.
        // WHY:  Provides hardware-bound asymmetric identity with HKDF domain separation.
        // HOW:  Generates random master secret, derives Ed25519 signing subkey via HKDF, computes public key, zeroizes buffers.
        ArgumentException.ThrowIfNullOrWhiteSpace(keyAlias);
        ct.ThrowIfCancellationRequested();

        if (!IsPlatformApple())
        {
            throw new PlatformNotSupportedException("Apple Secure Enclave is unavailable on this platform.");
        }

        byte[] masterSecret = new byte[32];
        RandomNumberGenerator.Fill(masterSecret);

        try
        {
            _enclaveBridge.StoreKeychainSecret(keyAlias, masterSecret, requiresBiometric);
            _keyStore[keyAlias] = (byte[])masterSecret.Clone();

            byte[] signingKey = DeriveSigningKey(keyAlias, masterSecret);

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
            CryptographicOperations.ZeroMemory(masterSecret);
        }
    }

    /// <summary>
    /// Signs a challenge byte array using the Ed25519 private key stored under the specified alias in the Apple Secure Enclave bridge.
    /// </summary>
    /// <param name="keyAlias">Unique alias of the hardware key in Apple Secure Enclave keychain.</param>
    /// <param name="challengeBytes">Challenge bytes to sign.</param>
    /// <param name="ct">Cancellation token for cancelling the signing operation.</param>
    /// <returns>A task yielding the genuine 64-byte RFC 8032 Ed25519 signature.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="keyAlias"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="challengeBytes"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the key alias does not exist in Apple Secure Enclave keychain.</exception>
    /// <exception cref="CryptographicException">Thrown when LocalAuthentication policy evaluation fails.</exception>
    public async Task<byte[]> SignChallengeAsync(string keyAlias, byte[] challengeBytes, CancellationToken ct = default)
    {
        // WHAT: Produce RFC 8032 Ed25519 signature over challenge using domain-separated Secure Enclave signing key.
        // WHY:  Allows iOS/macOS mobile clients to prove private-key possession during node challenge-response authentication.
        // HOW:  Loads derived signing key, initializes Ed25519Signer, generates 64-byte signature, and zeroizes private key memory.
        ArgumentException.ThrowIfNullOrWhiteSpace(keyAlias);
        ArgumentNullException.ThrowIfNull(challengeBytes);
        ct.ThrowIfCancellationRequested();

        byte[] signingKey;
        byte[]? bridgeSigningKey = await _enclaveBridge.GetKeyForSigningAsync(keyAlias, "Authenticate to sign cryptographic challenge", ct).ConfigureAwait(false);
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
            throw new InvalidOperationException($"Hardware key '{keyAlias}' does not exist in Apple Secure Enclave keychain.");
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
    /// Signs a challenge memory slice using the Ed25519 private key stored under the specified alias in the Apple Secure Enclave bridge.
    /// </summary>
    /// <param name="keyAlias">Unique alias of the hardware key in Apple Secure Enclave keychain.</param>
    /// <param name="challengeBytes">Challenge memory slice to sign.</param>
    /// <param name="ct">Cancellation token for cancelling the signing operation.</param>
    /// <returns>A task yielding the genuine 64-byte RFC 8032 Ed25519 signature.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="keyAlias"/> is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the key alias does not exist in Apple Secure Enclave keychain.</exception>
    public Task<byte[]> SignChallengeAsync(string keyAlias, ReadOnlyMemory<byte> challengeBytes, CancellationToken ct = default)
    {
        // WHAT: Overload accepting ReadOnlyMemory<byte> for challenge signing.
        // WHY:  Simplifies caller integration with modern memory slices.
        // HOW:  Converts memory slice to array and delegates to primary signature method.
        return SignChallengeAsync(keyAlias, challengeBytes.ToArray(), ct);
    }

    /// <summary>
    /// Encrypts a plaintext payload using the specified Secure Enclave hardware-bound key using AES-256-GCM.
    /// </summary>
    /// <param name="keyAlias">Unique alias of the hardware key in the Secure Enclave keychain.</param>
    /// <param name="plaintext">Plaintext payload bytes to encrypt.</param>
    /// <returns>Encrypted envelope bytes containing nonce, tag, and ciphertext.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="keyAlias"/> is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the key alias does not exist in the keychain.</exception>
    /// <exception cref="CryptographicException">Thrown when LocalAuthentication policy evaluation fails.</exception>
    public byte[] EncryptPayload(string keyAlias, ReadOnlySpan<byte> plaintext)
    {
        // WHAT: Encrypt plaintext payload with AES-256-GCM using domain-separated wrapping key.
        // WHY:  Provides symmetric vault encryption aligned with Apple Secure Enclave capabilities.
        // HOW:  Loads derived wrapping key defensively, generates 12-byte random nonce, encrypts plaintext, appends authentication tag, and formats envelope.
        ArgumentException.ThrowIfNullOrWhiteSpace(keyAlias);

        byte[] wrappingKey;
        if (_enclaveBridge.TryReadKeyForWrapping(keyAlias, out byte[] bridgeKey))
        {
            wrappingKey = bridgeKey;
        }
        else if (_keyStore.TryGetValue(keyAlias, out byte[]? storeBytes))
        {
            wrappingKey = DeriveWrappingKey(keyAlias, storeBytes);
        }
        else
        {
            throw new InvalidOperationException($"Hardware key '{keyAlias}' does not exist in Apple Secure Enclave keychain.");
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
    /// Purges and zeroizes all cached and session secrets in memory and the Secure Enclave bridge.
    /// </summary>
    public void ZeroizeMemory()
    {
        // WHAT: Zeroize and clear all keys in provider memory and Secure Enclave bridge.
        // WHY:  Ensures no cryptographic key material remains in memory when locked or disposed.
        // HOW:  Clears bridge session secrets and zeroizes and clears the in-memory dictionary.
        _enclaveBridge.ClearAllSessionSecrets();
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
    /// Evaluates whether the current host or simulated platform is an Apple operating system.
    /// </summary>
    /// <returns><see langword="true"/> if iOS/macOS/Darwin; otherwise <see langword="false"/>.</returns>
    private bool IsPlatformApple()
    {
        // WHAT: Check if current runtime environment is Apple iOS, macOS, or Darwin.
        // WHY:  Prevents executing Apple LocalAuthentication operations on foreign operating systems.
        // HOW:  Evaluates injected platform predicate or queries RuntimeInformation.
        Func<string, bool> checker = _isPlatform ?? (p => RuntimeInformation.IsOSPlatform(OSPlatform.Create(p)) || (p == "OSX" && RuntimeInformation.IsOSPlatform(OSPlatform.OSX)));
        return checker("iOS") || checker("Apple") || checker("Darwin") || checker("OSX");
    }

    /// <summary>
    /// Resolves the active biometric modality for this Apple device head.
    /// </summary>
    /// <returns>Resolved <see cref="BiometricType"/>.</returns>
    private BiometricType ResolveBiometricType()
    {
        // WHAT: Determine whether Face ID or Touch ID is modeled.
        // WHY:  Apple devices support either Touch ID or Face ID depending on hardware architecture.
        // HOW:  Queries injected type probe delegate or falls back to configured static type.
        return _typeProbe != null ? _typeProbe() : _biometricType;
    }

    /// <summary>
    /// Synchronously checks Apple biometric hardware availability.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Populated <see cref="BiometricAvailabilityResult"/>.</returns>
    private BiometricAvailabilityResult CheckBiometricAvailabilityCore(CancellationToken ct)
    {
        // WHAT: Core diagnostic logic probing Apple LocalAuthentication status.
        // WHY:  Centralizes availability logic for both capability checks and pre-flight evaluation.
        // HOW:  Checks cancellation, validates Apple platform, queries status probe, and maps result.
        if (ct.IsCancellationRequested)
        {
            return new BiometricAvailabilityResult(
                IsAvailable: false,
                SupportedType: BiometricType.None,
                Status: BiometricStatus.UserCancelled,
                StatusMessage: "Biometric availability check was cancelled.");
        }

        if (!IsPlatformApple())
        {
            return new BiometricAvailabilityResult(
                IsAvailable: false,
                SupportedType: BiometricType.None,
                Status: BiometricStatus.HardwareUnavailable,
                StatusMessage: "Apple Secure Enclave is not supported on this platform.");
        }

        BiometricType activeType = ResolveBiometricType();
        BiometricStatus probedStatus = _statusProbe != null ? _statusProbe() : BiometricStatus.Available;

        return probedStatus switch
        {
            BiometricStatus.Available => new BiometricAvailabilityResult(
                IsAvailable: true,
                SupportedType: activeType,
                Status: BiometricStatus.Available,
                StatusMessage: $"Apple LocalAuthentication ({activeType}) is ready and enrolled."),

            BiometricStatus.NotEnrolled => new BiometricAvailabilityResult(
                IsAvailable: false,
                SupportedType: activeType,
                Status: BiometricStatus.NotEnrolled,
                StatusMessage: "No biometric identities enrolled in Apple Secure Enclave."),

            BiometricStatus.HardwareUnavailable => new BiometricAvailabilityResult(
                IsAvailable: false,
                SupportedType: BiometricType.None,
                Status: BiometricStatus.HardwareUnavailable,
                StatusMessage: "Apple Secure Enclave hardware is unavailable."),

            BiometricStatus.Lockout => new BiometricAvailabilityResult(
                IsAvailable: false,
                SupportedType: activeType,
                Status: BiometricStatus.Lockout,
                StatusMessage: "Biometric authentication is locked out on iOS."),

            BiometricStatus.UserCancelled => new BiometricAvailabilityResult(
                IsAvailable: false,
                SupportedType: activeType,
                Status: BiometricStatus.UserCancelled,
                StatusMessage: "Biometric evaluation was cancelled by user."),

            BiometricStatus.Failed => new BiometricAvailabilityResult(
                IsAvailable: false,
                SupportedType: BiometricType.None,
                Status: BiometricStatus.Failed,
                StatusMessage: "LocalAuthentication evaluation failed."),

            _ => new BiometricAvailabilityResult(
                IsAvailable: false,
                SupportedType: BiometricType.None,
                Status: probedStatus,
                StatusMessage: $"Apple biometric status: {probedStatus}")
        };
    }

    /// <summary>
    /// Synchronously performs Apple biometric authentication and payload decryption.
    /// </summary>
    /// <param name="request">Authentication request parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Populated <see cref="BiometricAuthResult"/>.</returns>
    private BiometricAuthResult AuthenticateAndDecryptCore(BiometricAuthRequest request, CancellationToken ct)
    {
        // WHAT: Core decryption logic verifying Apple biometric prompt and Secure Enclave key.
        // WHY:  Guarantees robust decryption of encrypted patient DNA payloads.
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
            if (_enclaveBridge.TryReadKeyForWrapping(request.KeyAlias, out byte[] bridgeKey))
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
                    ErrorMessage: $"Hardware key '{request.KeyAlias}' not found in Apple Secure Enclave keychain.",
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
    /// Synchronously generates a hardware-bound key inside the simulated Apple Secure Enclave keychain.
    /// </summary>
    /// <param name="keyAlias">Target key alias.</param>
    /// <param name="requiresBiometric">Flag indicating whether biometric auth is required.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Populated <see cref="HardwareKeyGenerationResult"/>.</returns>
    private HardwareKeyGenerationResult GenerateHardwareBoundKeyCore(string keyAlias, bool requiresBiometric, CancellationToken ct)
    {
        // WHAT: Core key generation routine for Apple Secure Enclave.
        // WHY:  Simulates hardware key generation backed by Apple Secure Enclave processor with HKDF subkey derivation.
        // HOW:  Checks cancellation, confirms Apple platform, generates random master secret, derives Ed25519 signing key, and stores in bridge.
        if (ct.IsCancellationRequested)
        {
            return new HardwareKeyGenerationResult(
                Success: false,
                KeyAlias: keyAlias,
                PublicKeyBase64: string.Empty,
                ErrorMessage: "Key generation was cancelled.");
        }

        if (!IsPlatformApple())
        {
            return new HardwareKeyGenerationResult(
                Success: false,
                KeyAlias: keyAlias,
                PublicKeyBase64: string.Empty,
                ErrorMessage: "Apple Secure Enclave is unavailable on this platform.");
        }

        byte[] masterSecret = new byte[32];
        RandomNumberGenerator.Fill(masterSecret);

        try
        {
            _enclaveBridge.StoreKeychainSecret(keyAlias, masterSecret, requiresBiometric);
            _keyStore[keyAlias] = (byte[])masterSecret.Clone();

            byte[] signingKey = DeriveSigningKey(keyAlias, masterSecret);

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
            CryptographicOperations.ZeroMemory(masterSecret);
        }
    }

    /// <summary>
    /// Derives the 32-byte Ed25519 signing key from the master secret using HKDF.
    /// </summary>
    /// <param name="keyAlias">Key alias used as HKDF salt.</param>
    /// <param name="masterSecret">Master secret bytes.</param>
    /// <returns>A 32-byte derived signing key.</returns>
    private static byte[] DeriveSigningKey(string keyAlias, ReadOnlySpan<byte> masterSecret)
    {
        // WHAT: Derive domain-separated Ed25519 signing key via HKDF.
        // WHY:  Ensures signing key is cryptographically separated from wrapping key.
        // HOW:  Uses HKDF with SHA256, alias salt, and Apple signing info string.
        byte[] aliasBytes = Encoding.UTF8.GetBytes(keyAlias);
        byte[] subkey = new byte[32];
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            masterSecret,
            subkey,
            salt: aliasBytes,
            info: Ed25519SignInfo);
        return subkey;
    }

    /// <summary>
    /// Derives the 32-byte AES-256-GCM wrapping key from the master secret using HKDF.
    /// </summary>
    /// <param name="keyAlias">Key alias used as HKDF salt.</param>
    /// <param name="masterSecret">Master secret bytes.</param>
    /// <returns>A 32-byte derived wrapping key.</returns>
    private static byte[] DeriveWrappingKey(string keyAlias, ReadOnlySpan<byte> masterSecret)
    {
        // WHAT: Derive domain-separated AES-256-GCM wrapping key via HKDF.
        // WHY:  Ensures wrapping key is cryptographically separated from signing key.
        // HOW:  Uses HKDF with SHA256, alias salt, and Apple wrapping info string.
        byte[] aliasBytes = Encoding.UTF8.GetBytes(keyAlias);
        byte[] subkey = new byte[32];
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            masterSecret,
            subkey,
            salt: aliasBytes,
            info: Aes256GcmWrapInfo);
        return subkey;
    }
}
