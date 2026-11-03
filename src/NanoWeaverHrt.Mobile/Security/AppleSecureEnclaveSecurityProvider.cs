using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace NanoWeaverHrt.Mobile.Security;

/// <summary>
/// Apple platform implementation of <see cref="IBiometricSecurityProvider"/> modeling
/// iOS/macOS <c>LocalAuthentication</c> (<c>LAContext</c>) and Apple Secure Enclave hardware keychain integration.
/// </summary>
/// <remarks>
/// // WHAT: Apple Secure Enclave and LocalAuthentication biometric security provider.
/// // WHY:  Guarantees cryptographic patient DNA regimen protection on Apple iOS/iPadOS/macOS devices.
/// // HOW:  Simulates Apple Secure Enclave hardware key generation, Touch ID / Face ID evaluation, and AES-256-GCM envelope crypto.
/// </remarks>
public sealed class AppleSecureEnclaveSecurityProvider : IBiometricSecurityProvider
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
    /// In-memory simulated Apple Secure Enclave keychain holding 256-bit hardware-bound symmetric keys.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte[]> _keyStore = new(StringComparer.Ordinal);

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
    public AppleSecureEnclaveSecurityProvider(
        Func<string, bool>? isPlatform = null,
        Func<BiometricStatus>? statusProbe = null,
        BiometricType biometricType = BiometricType.AppleFaceId,
        Func<BiometricType>? typeProbe = null)
    {
        // WHAT: Initialize Apple Secure Enclave provider with test hooks.
        // WHY:  Allows deterministic testing of LAContext Touch ID / Face ID states without Apple hardware.
        // HOW:  Stores injected platform predicate, status probe, static modality, and dynamic type probe.
        _isPlatform = isPlatform;
        _statusProbe = statusProbe;
        _biometricType = biometricType;
        _typeProbe = typeProbe;
    }

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
        // WHAT: Generate a hardware-bound key inside Apple Secure Enclave keychain.
        // WHY:  Provides a secure key alias bound to device biometric authentication.
        // HOW:  Validates alias, checks platform, generates 256-bit key in simulated keychain, and returns public key digest.
        ArgumentException.ThrowIfNullOrWhiteSpace(keyAlias);

        return Task.FromResult(GenerateHardwareBoundKeyCore(keyAlias, ct));
    }

    /// <summary>
    /// Encrypts a plaintext payload using the specified Secure Enclave hardware-bound key using AES-256-GCM.
    /// </summary>
    /// <param name="keyAlias">Unique alias of the hardware key in the Secure Enclave keychain.</param>
    /// <param name="plaintext">Plaintext payload bytes to encrypt.</param>
    /// <returns>Encrypted envelope bytes containing nonce, tag, and ciphertext.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="keyAlias"/> is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the key alias does not exist in the keychain.</exception>
    public byte[] EncryptPayload(string keyAlias, ReadOnlySpan<byte> plaintext)
    {
        // WHAT: Encrypt plaintext payload with AES-256-GCM using Secure Enclave key.
        // WHY:  Provides symmetric vault encryption aligned with Apple Secure Enclave capabilities.
        // HOW:  Generates 12-byte random nonce, encrypts plaintext, appends authentication tag, and formats envelope.
        ArgumentException.ThrowIfNullOrWhiteSpace(keyAlias);

        if (!_keyStore.TryGetValue(keyAlias, out byte[]? keyBytes))
        {
            throw new InvalidOperationException($"Hardware key '{keyAlias}' does not exist in Apple Secure Enclave keychain.");
        }

        byte[] nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);

        byte[] tag = new byte[TagSizeBytes];
        byte[] ciphertext = new byte[plaintext.Length];

        using (var aesGcm = new AesGcm(keyBytes, TagSizeBytes))
        {
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        byte[] envelope = new byte[NonceSizeBytes + TagSizeBytes + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, envelope, 0, NonceSizeBytes);
        Buffer.BlockCopy(tag, 0, envelope, NonceSizeBytes, TagSizeBytes);
        Buffer.BlockCopy(ciphertext, 0, envelope, MinimumEnvelopeLength, ciphertext.Length);

        return envelope;
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
        // HOW:  Checks cancellation, verifies availability, looks up Secure Enclave key, and decrypts via AES-GCM.
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

        if (!_keyStore.TryGetValue(request.KeyAlias, out byte[]? keyBytes))
        {
            return new BiometricAuthResult(
                Success: false,
                DecryptedPayload: null,
                ErrorMessage: $"Hardware key '{request.KeyAlias}' not found in Apple Secure Enclave keychain.",
                Status: BiometricStatus.Failed);
        }

        if (request.EncryptedVaultPayload.Length < MinimumEnvelopeLength)
        {
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
            using var aesGcm = new AesGcm(keyBytes, TagSizeBytes);
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
    }

    /// <summary>
    /// Synchronously generates a hardware-bound key inside the simulated Apple Secure Enclave keychain.
    /// </summary>
    /// <param name="keyAlias">Target key alias.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Populated <see cref="HardwareKeyGenerationResult"/>.</returns>
    private HardwareKeyGenerationResult GenerateHardwareBoundKeyCore(string keyAlias, CancellationToken ct)
    {
        // WHAT: Core key generation routine for Apple Secure Enclave.
        // WHY:  Simulates hardware key generation backed by Apple T2/T3/M-series/A-series Secure Enclave processor.
        // HOW:  Checks cancellation, confirms Apple platform, generates 256-bit key, and calculates public digest.
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

        byte[] keyBytes = new byte[32];
        RandomNumberGenerator.Fill(keyBytes);
        _keyStore[keyAlias] = keyBytes;

        byte[] publicKeyDigest = SHA256.HashData(keyBytes);
        string publicKeyBase64 = Convert.ToBase64String(publicKeyDigest);

        return new HardwareKeyGenerationResult(
            Success: true,
            KeyAlias: keyAlias,
            PublicKeyBase64: publicKeyBase64,
            ErrorMessage: null);
    }
}
