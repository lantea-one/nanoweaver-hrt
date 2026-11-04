using System.Security.Cryptography;
using System.Text;
using Moq;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Gui.Core.Services;
using NanoWeaverHrt.Mobile.Platform;
using NanoWeaverHrt.Mobile.Security;
using NanoWeaverHrt.Models;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Xunit;

namespace NanoWeaverHrt.Tests.Mobile;

/// <summary>
/// Disposable dummy biometric security provider for testing vault lifecycle disposal propagation.
/// </summary>
public sealed class DisposableTestBiometricProvider : IBiometricSecurityProvider, IDisposable
{
    /// <summary>
    /// Gets a value indicating whether this provider instance was disposed.
    /// </summary>
    public bool WasDisposed { get; private set; }

    /// <inheritdoc/>
    public Task<BiometricAvailabilityResult> CheckBiometricAvailabilityAsync(CancellationToken ct = default)
    {
        // WHAT: Return mock available status.
        // WHY:  Fulfills interface requirements.
        // HOW:  Returns completed task with Available status.
        return Task.FromResult(new BiometricAvailabilityResult(true, BiometricType.Fingerprint, BiometricStatus.Available, "OK"));
    }

    /// <inheritdoc/>
    public Task<BiometricAuthResult> AuthenticateAndDecryptAsync(BiometricAuthRequest request, CancellationToken ct = default)
    {
        // WHAT: Return mock successful auth result.
        // WHY:  Fulfills interface requirements.
        // HOW:  Returns completed task with success.
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(new BiometricAuthResult(true, [1, 2, 3], null, BiometricStatus.Available));
    }

    /// <inheritdoc/>
    public Task<HardwareKeyGenerationResult> GenerateHardwareBoundKeyAsync(string keyAlias, CancellationToken ct = default)
    {
        // WHAT: Return mock key generation result.
        // WHY:  Fulfills interface requirements.
        // HOW:  Returns completed task with success.
        ArgumentException.ThrowIfNullOrWhiteSpace(keyAlias);
        return Task.FromResult(new HardwareKeyGenerationResult(true, keyAlias, "mock-key", null));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // WHAT: Mark provider as disposed.
        // WHY:  Allows tests to assert disposal propagation from UnifiedMobileBiometricVault.
        // HOW:  Sets WasDisposed to true.
        WasDisposed = true;
    }
}

/// <summary>
/// Comprehensive unit tests verifying native mobile biometric authentication,
/// hardware-backed key vaults, and lifecycle zeroization across Android and Apple platforms.
/// </summary>
public sealed class MobileBiometricSecurityTests
{
    /// <summary>
    /// Verifies that <see cref="AndroidBiometricSecurityProvider"/> correctly probes availability across all status conditions.
    /// </summary>
    [Fact]
    public async Task AndroidBiometricSecurityProvider_CheckBiometricAvailability_AllStatuses()
    {
        // WHAT: Verify Android biometric availability probing across all operational statuses.
        // WHY:  Guarantees prompt UI responds accurately to enrollment, lockout, and hardware failure.
        // HOW:  Tests Available, NotEnrolled, HardwareUnavailable, Lockout, UserCancelled, Failed, and default.
        static bool AndroidPlatform(string p) => p.Equals("Android", StringComparison.OrdinalIgnoreCase);

        // 1. Available
        var provider = new AndroidBiometricSecurityProvider(AndroidPlatform, () => BiometricStatus.Available);
        var result = await provider.CheckBiometricAvailabilityAsync();
        Assert.True(result.IsAvailable);
        Assert.Equal(BiometricType.Fingerprint, result.SupportedType);
        Assert.Equal(BiometricStatus.Available, result.Status);

        // 2. NotEnrolled
        provider = new AndroidBiometricSecurityProvider(AndroidPlatform, () => BiometricStatus.NotEnrolled);
        result = await provider.CheckBiometricAvailabilityAsync();
        Assert.False(result.IsAvailable);
        Assert.Equal(BiometricStatus.NotEnrolled, result.Status);

        // 3. HardwareUnavailable
        provider = new AndroidBiometricSecurityProvider(AndroidPlatform, () => BiometricStatus.HardwareUnavailable);
        result = await provider.CheckBiometricAvailabilityAsync();
        Assert.False(result.IsAvailable);
        Assert.Equal(BiometricType.None, result.SupportedType);
        Assert.Equal(BiometricStatus.HardwareUnavailable, result.Status);

        // 4. Lockout
        provider = new AndroidBiometricSecurityProvider(AndroidPlatform, () => BiometricStatus.Lockout);
        result = await provider.CheckBiometricAvailabilityAsync();
        Assert.False(result.IsAvailable);
        Assert.Equal(BiometricStatus.Lockout, result.Status);

        // 5. UserCancelled
        provider = new AndroidBiometricSecurityProvider(AndroidPlatform, () => BiometricStatus.UserCancelled);
        result = await provider.CheckBiometricAvailabilityAsync();
        Assert.False(result.IsAvailable);
        Assert.Equal(BiometricStatus.UserCancelled, result.Status);

        // 6. Failed
        provider = new AndroidBiometricSecurityProvider(AndroidPlatform, () => BiometricStatus.Failed);
        result = await provider.CheckBiometricAvailabilityAsync();
        Assert.False(result.IsAvailable);
        Assert.Equal(BiometricType.None, result.SupportedType);
        Assert.Equal(BiometricStatus.Failed, result.Status);

        // 7. Unknown / default branch
        provider = new AndroidBiometricSecurityProvider(AndroidPlatform, () => (BiometricStatus)999);
        result = await provider.CheckBiometricAvailabilityAsync();
        Assert.False(result.IsAvailable);
        Assert.Equal((BiometricStatus)999, result.Status);

        // 8. Default statusProbe (null) defaults to Available
        provider = new AndroidBiometricSecurityProvider(AndroidPlatform);
        result = await provider.CheckBiometricAvailabilityAsync();
        Assert.True(result.IsAvailable);

        // 9. Cancellation requested
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        result = await provider.CheckBiometricAvailabilityAsync(cts.Token);
        Assert.False(result.IsAvailable);
        Assert.Equal(BiometricStatus.UserCancelled, result.Status);

        // 10. Non-Android platform
        provider = new AndroidBiometricSecurityProvider(_ => false);
        result = await provider.CheckBiometricAvailabilityAsync();
        Assert.False(result.IsAvailable);
        Assert.Equal(BiometricStatus.HardwareUnavailable, result.Status);
    }

    /// <summary>
    /// Verifies platform predicate branches in <see cref="AndroidBiometricSecurityProvider"/>.
    /// </summary>
    [Fact]
    public async Task AndroidBiometricSecurityProvider_PlatformPredicate_Branches()
    {
        // WHAT: Test uppercase and lowercase Android platform predicate evaluation and null predicate.
        // WHY:  Guarantees complete branch coverage on platform detection logic.
        // HOW:  Tests Android, ANDROID (when Android is false), and null predicate.
        // Test "ANDROID" branch
        var provider = new AndroidBiometricSecurityProvider(p => p == "ANDROID");
        var result = await provider.CheckBiometricAvailabilityAsync();
        Assert.True(result.IsAvailable);

        // Test null predicate (on host OS - Linux, which is not Android)
        var nullProvider = new AndroidBiometricSecurityProvider(null);
        var nullResult = await nullProvider.CheckBiometricAvailabilityAsync();
        Assert.False(nullResult.IsAvailable);
    }

    /// <summary>
    /// Verifies hardware key generation in <see cref="AndroidBiometricSecurityProvider"/>.
    /// </summary>
    [Fact]
    public async Task AndroidBiometricSecurityProvider_GenerateHardwareBoundKey_ValidationsAndSuccess()
    {
        // WHAT: Test key generation successes, cancellations, and input validations.
        // WHY:  Ensures Android Keystore keys are correctly created and invalid inputs are rejected.
        // HOW:  Tests empty/null aliases, cancellation token, non-Android platform, and successful key creation.
        static bool AndroidPlatform(string p) => p.Equals("Android", StringComparison.OrdinalIgnoreCase);
        var provider = new AndroidBiometricSecurityProvider(AndroidPlatform);

        // Null, empty, whitespace alias validation
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.GenerateHardwareBoundKeyAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GenerateHardwareBoundKeyAsync(string.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GenerateHardwareBoundKeyAsync("   "));

        // Cancellation
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelResult = await provider.GenerateHardwareBoundKeyAsync("key-1", cts.Token);
        Assert.False(cancelResult.Success);
        Assert.Contains("cancelled", cancelResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Non-Android platform failure
        var nonAndroidProvider = new AndroidBiometricSecurityProvider(_ => false);
        var nonAndroidResult = await nonAndroidProvider.GenerateHardwareBoundKeyAsync("key-1");
        Assert.False(nonAndroidResult.Success);
        Assert.Contains("unavailable", nonAndroidResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Success
        var successResult = await provider.GenerateHardwareBoundKeyAsync("dna-key-android");
        Assert.True(successResult.Success);
        Assert.Equal("dna-key-android", successResult.KeyAlias);
        Assert.False(string.IsNullOrWhiteSpace(successResult.PublicKeyBase64));
        Assert.Null(successResult.ErrorMessage);
    }

    /// <summary>
    /// Verifies payload encryption and interactive authentication with decryption in <see cref="AndroidBiometricSecurityProvider"/>.
    /// </summary>
    [Fact]
    public async Task AndroidBiometricSecurityProvider_EncryptAndDecrypt_FlowsAndFailures()
    {
        // WHAT: Test end-to-end encryption and decryption on Android provider.
        // WHY:  Guarantees cryptographic confidentiality and handles error conditions.
        // HOW:  Tests null validations, non-existent key, corrupted payload, short payload, cancellation, and success.
        static bool AndroidPlatform(string p) => p.Equals("Android", StringComparison.OrdinalIgnoreCase);
        var provider = new AndroidBiometricSecurityProvider(AndroidPlatform);

        // Key generation
        await provider.GenerateHardwareBoundKeyAsync("patient-key-01");

        // Encrypt payload argument validation
        Assert.Throws<ArgumentNullException>(() => provider.EncryptPayload(null!, [1, 2]));
        Assert.Throws<ArgumentException>(() => provider.EncryptPayload("", [1, 2]));
        Assert.Throws<ArgumentException>(() => provider.EncryptPayload("   ", [1, 2]));
        Assert.Throws<InvalidOperationException>(() => provider.EncryptPayload("missing-key", [1, 2]));

        // Successful encryption
        byte[] plaintext = "GAATTC-DNA-SEQUENCE-RECEPTOR"u8.ToArray();
        byte[] encryptedEnvelope = provider.EncryptPayload("patient-key-01", plaintext);
        Assert.NotNull(encryptedEnvelope);
        Assert.True(encryptedEnvelope.Length >= 28 + plaintext.Length);

        // Decryption argument validation
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.AuthenticateAndDecryptAsync(null!));
        var reqNullAlias = new BiometricAuthRequest("Auth", "Sub", "Desc", null!, encryptedEnvelope);
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.AuthenticateAndDecryptAsync(reqNullAlias));
        var reqEmptyAlias = new BiometricAuthRequest("Auth", "Sub", "Desc", "", encryptedEnvelope);
        await Assert.ThrowsAsync<ArgumentException>(() => provider.AuthenticateAndDecryptAsync(reqEmptyAlias));
        var reqNullPayload = new BiometricAuthRequest("Auth", "Sub", "Desc", "patient-key-01", null!);
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.AuthenticateAndDecryptAsync(reqNullPayload));

        // Cancellation check
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var req = new BiometricAuthRequest("Unlock", "Sub", "Desc", "patient-key-01", encryptedEnvelope);
        var cancelResult = await provider.AuthenticateAndDecryptAsync(req, cts.Token);
        Assert.False(cancelResult.Success);
        Assert.Equal(BiometricStatus.UserCancelled, cancelResult.Status);

        // Biometric availability failure during auth
        var notEnrolledProvider = new AndroidBiometricSecurityProvider(AndroidPlatform, () => BiometricStatus.NotEnrolled);
        var notEnrolledResult = await notEnrolledProvider.AuthenticateAndDecryptAsync(req);
        Assert.False(notEnrolledResult.Success);
        Assert.Equal(BiometricStatus.NotEnrolled, notEnrolledResult.Status);

        // Missing key in keystore
        var missingKeyReq = new BiometricAuthRequest("Unlock", "Sub", "Desc", "nonexistent-key", encryptedEnvelope);
        var missingKeyResult = await provider.AuthenticateAndDecryptAsync(missingKeyReq);
        Assert.False(missingKeyResult.Success);
        Assert.Contains("not found", missingKeyResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Payload length invalid (< 28 bytes)
        var shortReq = new BiometricAuthRequest("Unlock", "Sub", "Desc", "patient-key-01", [1, 2, 3]);
        var shortResult = await provider.AuthenticateAndDecryptAsync(shortReq);
        Assert.False(shortResult.Success);
        Assert.Contains("invalid", shortResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Payload corrupted (tampered ciphertext / tag)
        byte[] corruptedEnvelope = (byte[])encryptedEnvelope.Clone();
        corruptedEnvelope[^1] ^= 0xFF; // Flip bit in ciphertext
        var corruptedReq = new BiometricAuthRequest("Unlock", "Sub", "Desc", "patient-key-01", corruptedEnvelope);
        var corruptedResult = await provider.AuthenticateAndDecryptAsync(corruptedReq);
        Assert.False(corruptedResult.Success);
        Assert.Contains("Decryption failed", corruptedResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Successful authentication and decryption
        var successResult = await provider.AuthenticateAndDecryptAsync(req);
        Assert.True(successResult.Success);
        Assert.Equal(plaintext, successResult.DecryptedPayload);
        Assert.Null(successResult.ErrorMessage);
        Assert.Equal(BiometricStatus.Available, successResult.Status);
    }

    /// <summary>
    /// Verifies availability probing in <see cref="AppleSecureEnclaveSecurityProvider"/> across all status conditions and modalities.
    /// </summary>
    [Fact]
    public async Task AppleSecureEnclaveSecurityProvider_CheckBiometricAvailability_AllStatuses()
    {
        // WHAT: Verify Apple LocalAuthentication availability across Face ID, Touch ID, and error statuses.
        // WHY:  Guarantees proper UI prompting on iOS/iPadOS/macOS hardware.
        // HOW:  Tests Face ID, Touch ID, NotEnrolled, HardwareUnavailable, Lockout, UserCancelled, Failed, default, dynamic type probe.
        static bool ApplePlatform(string p) => p.Equals("iOS", StringComparison.OrdinalIgnoreCase);

        // 1. Face ID Available (default)
        var provider = new AppleSecureEnclaveSecurityProvider(ApplePlatform, () => BiometricStatus.Available);
        var result = await provider.CheckBiometricAvailabilityAsync();
        Assert.True(result.IsAvailable);
        Assert.Equal(BiometricType.AppleFaceId, result.SupportedType);
        Assert.Equal(BiometricStatus.Available, result.Status);

        // 2. Touch ID Available (constructor specified)
        provider = new AppleSecureEnclaveSecurityProvider(ApplePlatform, () => BiometricStatus.Available, BiometricType.AppleTouchId);
        result = await provider.CheckBiometricAvailabilityAsync();
        Assert.True(result.IsAvailable);
        Assert.Equal(BiometricType.AppleTouchId, result.SupportedType);

        // 3. Dynamic type probe delegate
        provider = new AppleSecureEnclaveSecurityProvider(ApplePlatform, () => BiometricStatus.Available, typeProbe: () => BiometricType.AppleTouchId);
        result = await provider.CheckBiometricAvailabilityAsync();
        Assert.Equal(BiometricType.AppleTouchId, result.SupportedType);

        // 4. NotEnrolled
        provider = new AppleSecureEnclaveSecurityProvider(ApplePlatform, () => BiometricStatus.NotEnrolled);
        result = await provider.CheckBiometricAvailabilityAsync();
        Assert.False(result.IsAvailable);
        Assert.Equal(BiometricStatus.NotEnrolled, result.Status);

        // 5. HardwareUnavailable
        provider = new AppleSecureEnclaveSecurityProvider(ApplePlatform, () => BiometricStatus.HardwareUnavailable);
        result = await provider.CheckBiometricAvailabilityAsync();
        Assert.False(result.IsAvailable);
        Assert.Equal(BiometricType.None, result.SupportedType);
        Assert.Equal(BiometricStatus.HardwareUnavailable, result.Status);

        // 6. Lockout
        provider = new AppleSecureEnclaveSecurityProvider(ApplePlatform, () => BiometricStatus.Lockout);
        result = await provider.CheckBiometricAvailabilityAsync();
        Assert.False(result.IsAvailable);
        Assert.Equal(BiometricStatus.Lockout, result.Status);

        // 7. UserCancelled
        provider = new AppleSecureEnclaveSecurityProvider(ApplePlatform, () => BiometricStatus.UserCancelled);
        result = await provider.CheckBiometricAvailabilityAsync();
        Assert.False(result.IsAvailable);
        Assert.Equal(BiometricStatus.UserCancelled, result.Status);

        // 8. Failed
        provider = new AppleSecureEnclaveSecurityProvider(ApplePlatform, () => BiometricStatus.Failed);
        result = await provider.CheckBiometricAvailabilityAsync();
        Assert.False(result.IsAvailable);
        Assert.Equal(BiometricType.None, result.SupportedType);
        Assert.Equal(BiometricStatus.Failed, result.Status);

        // 9. Unknown status
        provider = new AppleSecureEnclaveSecurityProvider(ApplePlatform, () => (BiometricStatus)888);
        result = await provider.CheckBiometricAvailabilityAsync();
        Assert.False(result.IsAvailable);
        Assert.Equal((BiometricStatus)888, result.Status);

        // 10. Default status probe (null) defaults to Available
        provider = new AppleSecureEnclaveSecurityProvider(ApplePlatform);
        result = await provider.CheckBiometricAvailabilityAsync();
        Assert.True(result.IsAvailable);

        // 11. Cancellation requested
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        result = await provider.CheckBiometricAvailabilityAsync(cts.Token);
        Assert.False(result.IsAvailable);
        Assert.Equal(BiometricStatus.UserCancelled, result.Status);

        // 12. Non-Apple platform
        provider = new AppleSecureEnclaveSecurityProvider(_ => false);
        result = await provider.CheckBiometricAvailabilityAsync();
        Assert.False(result.IsAvailable);
        Assert.Equal(BiometricStatus.HardwareUnavailable, result.Status);
    }

    /// <summary>
    /// Verifies platform predicate evaluation across Apple platform strings in <see cref="AppleSecureEnclaveSecurityProvider"/>.
    /// </summary>
    [Fact]
    public async Task AppleSecureEnclaveSecurityProvider_PlatformPredicate_Branches()
    {
        // WHAT: Test iOS, Apple, Darwin, OSX, and null platform predicates.
        // WHY:  Ensures full branch coverage of Apple platform aliases.
        // HOW:  Tests each string alias in isolation and verifies null predicate fallback.
        // "iOS"
        var p1 = new AppleSecureEnclaveSecurityProvider(p => p == "iOS");
        Assert.True((await p1.CheckBiometricAvailabilityAsync()).IsAvailable);

        // "Apple" (when iOS is false)
        var p2 = new AppleSecureEnclaveSecurityProvider(p => p == "Apple");
        Assert.True((await p2.CheckBiometricAvailabilityAsync()).IsAvailable);

        // "Darwin" (when earlier false)
        var p3 = new AppleSecureEnclaveSecurityProvider(p => p == "Darwin");
        Assert.True((await p3.CheckBiometricAvailabilityAsync()).IsAvailable);

        // "OSX" (when earlier false)
        var p4 = new AppleSecureEnclaveSecurityProvider(p => p == "OSX");
        Assert.True((await p4.CheckBiometricAvailabilityAsync()).IsAvailable);

        // null predicate on Linux host
        var pNull = new AppleSecureEnclaveSecurityProvider(null);
        Assert.False((await pNull.CheckBiometricAvailabilityAsync()).IsAvailable);
    }

    /// <summary>
    /// Verifies hardware key generation in <see cref="AppleSecureEnclaveSecurityProvider"/>.
    /// </summary>
    [Fact]
    public async Task AppleSecureEnclaveSecurityProvider_GenerateHardwareBoundKey_ValidationsAndSuccess()
    {
        // WHAT: Test Apple Secure Enclave key generation validation and execution.
        // WHY:  Ensures enclave keypairs are created safely.
        // HOW:  Tests empty/null aliases, cancellation, non-Apple platform, and successful key creation.
        static bool ApplePlatform(string p) => p.Equals("iOS", StringComparison.OrdinalIgnoreCase);
        var provider = new AppleSecureEnclaveSecurityProvider(ApplePlatform);

        // Null, empty, whitespace alias validation
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.GenerateHardwareBoundKeyAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GenerateHardwareBoundKeyAsync(string.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GenerateHardwareBoundKeyAsync("   "));

        // Cancellation
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelResult = await provider.GenerateHardwareBoundKeyAsync("apple-key", cts.Token);
        Assert.False(cancelResult.Success);
        Assert.Contains("cancelled", cancelResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Non-Apple platform failure
        var nonAppleProvider = new AppleSecureEnclaveSecurityProvider(_ => false);
        var nonAppleResult = await nonAppleProvider.GenerateHardwareBoundKeyAsync("apple-key");
        Assert.False(nonAppleResult.Success);
        Assert.Contains("unavailable", nonAppleResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Success
        var successResult = await provider.GenerateHardwareBoundKeyAsync("apple-dna-enclave-key");
        Assert.True(successResult.Success);
        Assert.Equal("apple-dna-enclave-key", successResult.KeyAlias);
        Assert.False(string.IsNullOrWhiteSpace(successResult.PublicKeyBase64));
        Assert.Null(successResult.ErrorMessage);
    }

    /// <summary>
    /// Verifies payload encryption and interactive authentication with decryption in <see cref="AppleSecureEnclaveSecurityProvider"/>.
    /// </summary>
    [Fact]
    public async Task AppleSecureEnclaveSecurityProvider_EncryptAndDecrypt_FlowsAndFailures()
    {
        // WHAT: Test end-to-end encryption and decryption on Apple provider.
        // WHY:  Guarantees cryptographic confidentiality and handles error conditions.
        // HOW:  Tests null validations, non-existent key, corrupted payload, short payload, cancellation, and success.
        static bool ApplePlatform(string p) => p.Equals("iOS", StringComparison.OrdinalIgnoreCase);
        var provider = new AppleSecureEnclaveSecurityProvider(ApplePlatform);

        await provider.GenerateHardwareBoundKeyAsync("apple-patient-key");

        // Encrypt payload argument validation
        Assert.Throws<ArgumentNullException>(() => provider.EncryptPayload(null!, [1, 2]));
        Assert.Throws<ArgumentException>(() => provider.EncryptPayload("", [1, 2]));
        Assert.Throws<ArgumentException>(() => provider.EncryptPayload("   ", [1, 2]));
        Assert.Throws<InvalidOperationException>(() => provider.EncryptPayload("missing-key", [1, 2]));

        // Successful encryption
        byte[] plaintext = "TGCAAT-ENCLAVE-REGIMEN-SECRET"u8.ToArray();
        byte[] encryptedEnvelope = provider.EncryptPayload("apple-patient-key", plaintext);
        Assert.NotNull(encryptedEnvelope);

        // Decryption argument validation
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.AuthenticateAndDecryptAsync(null!));
        var reqNullAlias = new BiometricAuthRequest("Auth", "Sub", "Desc", null!, encryptedEnvelope);
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.AuthenticateAndDecryptAsync(reqNullAlias));
        var reqEmptyAlias = new BiometricAuthRequest("Auth", "Sub", "Desc", "", encryptedEnvelope);
        await Assert.ThrowsAsync<ArgumentException>(() => provider.AuthenticateAndDecryptAsync(reqEmptyAlias));
        var reqNullPayload = new BiometricAuthRequest("Auth", "Sub", "Desc", "apple-patient-key", null!);
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.AuthenticateAndDecryptAsync(reqNullPayload));

        // Cancellation check
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var req = new BiometricAuthRequest("Unlock", "Sub", "Desc", "apple-patient-key", encryptedEnvelope);
        var cancelResult = await provider.AuthenticateAndDecryptAsync(req, cts.Token);
        Assert.False(cancelResult.Success);
        Assert.Equal(BiometricStatus.UserCancelled, cancelResult.Status);

        // Biometric availability failure during auth
        var notEnrolledProvider = new AppleSecureEnclaveSecurityProvider(ApplePlatform, () => BiometricStatus.NotEnrolled);
        var notEnrolledResult = await notEnrolledProvider.AuthenticateAndDecryptAsync(req);
        Assert.False(notEnrolledResult.Success);
        Assert.Equal(BiometricStatus.NotEnrolled, notEnrolledResult.Status);

        // Missing key in keychain
        var missingKeyReq = new BiometricAuthRequest("Unlock", "Sub", "Desc", "nonexistent-key", encryptedEnvelope);
        var missingKeyResult = await provider.AuthenticateAndDecryptAsync(missingKeyReq);
        Assert.False(missingKeyResult.Success);
        Assert.Contains("not found", missingKeyResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Payload length invalid (< 28 bytes)
        var shortReq = new BiometricAuthRequest("Unlock", "Sub", "Desc", "apple-patient-key", [1, 2, 3]);
        var shortResult = await provider.AuthenticateAndDecryptAsync(shortReq);
        Assert.False(shortResult.Success);
        Assert.Contains("invalid", shortResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Payload corrupted (tampered ciphertext)
        byte[] corruptedEnvelope = (byte[])encryptedEnvelope.Clone();
        corruptedEnvelope[^1] ^= 0xAA;
        var corruptedReq = new BiometricAuthRequest("Unlock", "Sub", "Desc", "apple-patient-key", corruptedEnvelope);
        var corruptedResult = await provider.AuthenticateAndDecryptAsync(corruptedReq);
        Assert.False(corruptedResult.Success);
        Assert.Contains("Decryption failed", corruptedResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Successful authentication and decryption
        var successResult = await provider.AuthenticateAndDecryptAsync(req);
        Assert.True(successResult.Success);
        Assert.Equal(plaintext, successResult.DecryptedPayload);
        Assert.Null(successResult.ErrorMessage);
        Assert.Equal(BiometricStatus.Available, successResult.Status);
    }

    /// <summary>
    /// Verifies that <see cref="FallbackBiometricSecurityProvider"/> safely reports hardware unavailability.
    /// </summary>
    [Fact]
    public async Task FallbackBiometricSecurityProvider_ReportsHardwareUnavailable_AndHandlesCancellation()
    {
        // WHAT: Test fallback provider operations and argument validation.
        // WHY:  Ensures safe behavior on unsupported platforms without throwing unhandled exceptions.
        // HOW:  Tests availability, auth, keygen, cancellation on each, and null validations.
        var provider = new FallbackBiometricSecurityProvider();

        // Check availability
        var avail = await provider.CheckBiometricAvailabilityAsync();
        Assert.False(avail.IsAvailable);
        Assert.Equal(BiometricStatus.HardwareUnavailable, avail.Status);

        // Check availability cancellation
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelAvail = await provider.CheckBiometricAvailabilityAsync(cts.Token);
        Assert.False(cancelAvail.IsAvailable);
        Assert.Equal(BiometricStatus.UserCancelled, cancelAvail.Status);

        // Authenticate & Decrypt
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.AuthenticateAndDecryptAsync(null!));
        var req = new BiometricAuthRequest("Title", "Sub", "Desc", "key", [1, 2, 3]);
        var authResult = await provider.AuthenticateAndDecryptAsync(req);
        Assert.False(authResult.Success);
        Assert.Equal(BiometricStatus.HardwareUnavailable, authResult.Status);

        var cancelAuthResult = await provider.AuthenticateAndDecryptAsync(req, cts.Token);
        Assert.False(cancelAuthResult.Success);
        Assert.Equal(BiometricStatus.UserCancelled, cancelAuthResult.Status);

        // Generate key
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.GenerateHardwareBoundKeyAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GenerateHardwareBoundKeyAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GenerateHardwareBoundKeyAsync("   "));

        var keyResult = await provider.GenerateHardwareBoundKeyAsync("test-key");
        Assert.False(keyResult.Success);
        Assert.Contains("not supported", keyResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var cancelKeyResult = await provider.GenerateHardwareBoundKeyAsync("test-key", cts.Token);
        Assert.False(cancelKeyResult.Success);
        Assert.Contains("cancelled", cancelKeyResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies platform resolution and active provider coordination in <see cref="UnifiedMobileBiometricVault"/>.
    /// </summary>
    [Fact]
    public void UnifiedMobileBiometricVault_PlatformResolution_SelectsCorrectProvider()
    {
        // WHAT: Test platform detection and provider instantiation in vault constructor.
        // WHY:  Guarantees correct provider selection for Android, iOS, and foreign platforms.
        // HOW:  Tests injected platform predicates for Android, ANDROID, iOS, Apple, Darwin, OSX, unknown, and null.
        using var androidVault = new UnifiedMobileBiometricVault(isPlatform: p => p == "Android");
        Assert.IsType<AndroidBiometricSecurityProvider>(androidVault.ActiveProvider);

        using var androidUpperVault = new UnifiedMobileBiometricVault(isPlatform: p => p == "ANDROID");
        Assert.IsType<AndroidBiometricSecurityProvider>(androidUpperVault.ActiveProvider);

        using var iosVault = new UnifiedMobileBiometricVault(isPlatform: p => p == "iOS");
        Assert.IsType<AppleSecureEnclaveSecurityProvider>(iosVault.ActiveProvider);

        using var appleVault = new UnifiedMobileBiometricVault(isPlatform: p => p == "Apple");
        Assert.IsType<AppleSecureEnclaveSecurityProvider>(appleVault.ActiveProvider);

        using var darwinVault = new UnifiedMobileBiometricVault(isPlatform: p => p == "Darwin");
        Assert.IsType<AppleSecureEnclaveSecurityProvider>(darwinVault.ActiveProvider);

        using var osxVault = new UnifiedMobileBiometricVault(isPlatform: p => p == "OSX");
        Assert.IsType<AppleSecureEnclaveSecurityProvider>(osxVault.ActiveProvider);

        using var fallbackVault = new UnifiedMobileBiometricVault(isPlatform: _ => false);
        Assert.IsType<FallbackBiometricSecurityProvider>(fallbackVault.ActiveProvider);

        using var nullPlatformVault = new UnifiedMobileBiometricVault(null, null);
        Assert.NotNull(nullPlatformVault.ActiveProvider);

        var customProvider = new FallbackBiometricSecurityProvider();
        using var customVault = new UnifiedMobileBiometricVault(customProvider);
        Assert.Same(customProvider, customVault.ActiveProvider);
    }

    /// <summary>
    /// Verifies in-memory regimen caching, defensive cloning, and retrieval in <see cref="UnifiedMobileBiometricVault"/>.
    /// </summary>
    [Fact]
    public void UnifiedMobileBiometricVault_CachingAndRetrieval_DefensiveCopies()
    {
        // WHAT: Verify payload caching, overwrite zeroization, and defensive copying.
        // WHY:  Prevents external mutations from corrupting cached data and tests cache hits/misses.
        // HOW:  Caches payload, mutates original, verifies cache integrity, overwrites, and retrieves.
        using var vault = new UnifiedMobileBiometricVault(new FallbackBiometricSecurityProvider());
        Assert.Equal(0, vault.CachedCount);

        byte[] payload1 = [0xAA, 0xBB, 0xCC, 0xDD];
        vault.CacheDecryptedRegimen("patient-alpha", payload1);
        Assert.Equal(1, vault.CachedCount);

        // Mutating payload1 must not mutate cached data
        payload1[0] = 0x00;
        bool found = vault.TryGetDecryptedRegimen("patient-alpha", out byte[]? retrieved);
        Assert.True(found);
        Assert.NotNull(retrieved);
        Assert.Equal([0xAA, 0xBB, 0xCC, 0xDD], retrieved);

        // Mutating retrieved array must not mutate cached data
        retrieved[0] = 0xFF;
        vault.TryGetDecryptedRegimen("patient-alpha", out byte[]? retrieved2);
        Assert.Equal([0xAA, 0xBB, 0xCC, 0xDD], retrieved2);

        // Overwrite existing patient regimen
        byte[] directRefBefore = vault.GetDirectBufferRefForTesting("patient-alpha")!;
        byte[] payload2 = [0x11, 0x22, 0x33];
        vault.CacheDecryptedRegimen("patient-alpha", payload2);
        Assert.Equal(1, vault.CachedCount);
        // Previous buffer must have been zeroized
        Assert.All(directRefBefore, b => Assert.Equal(0, b));

        vault.TryGetDecryptedRegimen("patient-alpha", out byte[]? retrievedOverwritten);
        Assert.Equal(payload2, retrievedOverwritten);

        // Cache miss
        bool missingFound = vault.TryGetDecryptedRegimen("patient-beta", out byte[]? missingPayload);
        Assert.False(missingFound);
        Assert.Null(missingPayload);

        // GetDirectBufferRefForTesting miss
        Assert.Null(vault.GetDirectBufferRefForTesting("nonexistent"));
    }

    /// <summary>
    /// Verifies input validation and exceptions for invalid parameters in <see cref="UnifiedMobileBiometricVault"/>.
    /// </summary>
    [Fact]
    public void UnifiedMobileBiometricVault_InvalidParameters_Throws()
    {
        // WHAT: Test null, empty, and whitespace input validation.
        // WHY:  Ensures strict defensive input checking across public APIs.
        // HOW:  Validates CacheDecryptedRegimen and TryGetDecryptedRegimen argument exceptions.
        using var vault = new UnifiedMobileBiometricVault(new FallbackBiometricSecurityProvider());

        // CacheDecryptedRegimen validations
        Assert.Throws<ArgumentNullException>(() => vault.CacheDecryptedRegimen(null!, [1, 2]));
        Assert.Throws<ArgumentException>(() => vault.CacheDecryptedRegimen("", [1, 2]));
        Assert.Throws<ArgumentException>(() => vault.CacheDecryptedRegimen("   ", [1, 2]));
        Assert.Throws<ArgumentNullException>(() => vault.CacheDecryptedRegimen("patient-1", null!));

        // TryGetDecryptedRegimen validations
        Assert.Throws<ArgumentNullException>(() => vault.TryGetDecryptedRegimen(null!, out _));
        Assert.Throws<ArgumentException>(() => vault.TryGetDecryptedRegimen("", out _));
        Assert.Throws<ArgumentException>(() => vault.TryGetDecryptedRegimen("   ", out _));
    }

    /// <summary>
    /// Verifies mobile application lifecycle state transitions and zeroization in <see cref="UnifiedMobileBiometricVault"/>.
    /// </summary>
    [Fact]
    public void UnifiedMobileBiometricVault_LifecycleTransitions_ZeroizeMemory()
    {
        // WHAT: Verify volatile memory zeroization across Paused, Backgrounded, Stopped, and Terminated states.
        // WHY:  Guarantees genomic regimens are immediately purged from RAM when application loses foreground focus.
        // HOW:  Caches payload, transitions through Active/Resumed (preserved), then background states (zeroized).
        using var vault = new UnifiedMobileBiometricVault(new FallbackBiometricSecurityProvider());

        vault.CacheDecryptedRegimen("p1", [0x10, 0x20, 0x30]);
        vault.OnAppLifecycleStateChanged(MobileAppLifecycleState.Active);
        Assert.Equal(1, vault.CachedCount);

        vault.OnAppLifecycleStateChanged(MobileAppLifecycleState.Resumed);
        Assert.Equal(1, vault.CachedCount);

        // Test Paused
        byte[] directRef = vault.GetDirectBufferRefForTesting("p1")!;
        vault.OnAppLifecycleStateChanged(MobileAppLifecycleState.Paused);
        Assert.Equal(0, vault.CachedCount);
        Assert.All(directRef, b => Assert.Equal(0, b));

        // Test Backgrounded
        vault.CacheDecryptedRegimen("p2", [0x40, 0x50]);
        byte[] directRef2 = vault.GetDirectBufferRefForTesting("p2")!;
        vault.OnAppLifecycleStateChanged(MobileAppLifecycleState.Backgrounded);
        Assert.Equal(0, vault.CachedCount);
        Assert.All(directRef2, b => Assert.Equal(0, b));

        // Test Stopped
        vault.CacheDecryptedRegimen("p3", [0x60, 0x70]);
        byte[] directRef3 = vault.GetDirectBufferRefForTesting("p3")!;
        vault.OnAppLifecycleStateChanged(MobileAppLifecycleState.Stopped);
        Assert.Equal(0, vault.CachedCount);
        Assert.All(directRef3, b => Assert.Equal(0, b));

        // Test Terminated
        vault.CacheDecryptedRegimen("p4", [0x80, 0x90]);
        byte[] directRef4 = vault.GetDirectBufferRefForTesting("p4")!;
        vault.OnAppLifecycleStateChanged(MobileAppLifecycleState.Terminated);
        Assert.Equal(0, vault.CachedCount);
        Assert.All(directRef4, b => Assert.Equal(0, b));
    }

    /// <summary>
    /// Verifies Dispose semantics, memory zeroization, provider disposal, and post-dispose guard checks in <see cref="UnifiedMobileBiometricVault"/>.
    /// </summary>
    [Fact]
    public void UnifiedMobileBiometricVault_Dispose_ZeroizesMemoryAndGuardsOperations()
    {
        // WHAT: Test Dispose idempotency, memory zeroization, provider disposal, and ObjectDisposedException.
        // WHY:  Guarantees cryptographic memory wipe on disposal and prevents post-disposal usage.
        // HOW:  Caches payload with disposable provider, disposes vault, asserts zeroization and exceptions.
        var disposableProvider = new DisposableTestBiometricProvider();
        var vault = new UnifiedMobileBiometricVault(disposableProvider);

        vault.CacheDecryptedRegimen("pt-dispose", [0xDE, 0xAD, 0xBE, 0xEF]);
        byte[] directRef = vault.GetDirectBufferRefForTesting("pt-dispose")!;
        Assert.Equal(1, vault.CachedCount);

        vault.Dispose();
        Assert.Equal(0, vault.CachedCount);
        Assert.All(directRef, b => Assert.Equal(0, b));
        Assert.True(disposableProvider.WasDisposed);

        // Multiple calls to Dispose are idempotent
        vault.Dispose();

        // Lifecycle calls on disposed vault are safe no-ops
        vault.OnAppLifecycleStateChanged(MobileAppLifecycleState.Paused);

        // Operations throw ObjectDisposedException
        Assert.Throws<ObjectDisposedException>(() => vault.CacheDecryptedRegimen("p1", [1, 2]));
        Assert.Throws<ObjectDisposedException>(() => vault.TryGetDecryptedRegimen("p1", out _));
    }

    /// <summary>
    /// Verifies all enumeration members and model records for complete coverage.
    /// </summary>
    [Fact]
    public void MobileBiometricModels_RecordProperties_AndEnums()
    {
        // WHAT: Exercise record constructors, properties, and enum members.
        // WHY:  Ensures full test coverage of records and enums.
        // HOW:  Instantiates records, tests properties, copy-with expressions, and enum member accesses.
        // Enums
        Assert.Equal(0, (int)BiometricType.None);
        Assert.Equal(1, (int)BiometricType.Fingerprint);
        Assert.Equal(2, (int)BiometricType.Face);
        Assert.Equal(3, (int)BiometricType.Iris);
        Assert.Equal(4, (int)BiometricType.AppleTouchId);
        Assert.Equal(5, (int)BiometricType.AppleFaceId);

        Assert.Equal(0, (int)BiometricStatus.Available);
        Assert.Equal(1, (int)BiometricStatus.NotEnrolled);
        Assert.Equal(2, (int)BiometricStatus.HardwareUnavailable);
        Assert.Equal(3, (int)BiometricStatus.Lockout);
        Assert.Equal(4, (int)BiometricStatus.UserCancelled);
        Assert.Equal(5, (int)BiometricStatus.Failed);

        Assert.Equal(0, (int)MobileAppLifecycleState.Resumed);
        Assert.Equal(1, (int)MobileAppLifecycleState.Active);
        Assert.Equal(2, (int)MobileAppLifecycleState.Paused);
        Assert.Equal(3, (int)MobileAppLifecycleState.Backgrounded);
        Assert.Equal(4, (int)MobileAppLifecycleState.Stopped);
        Assert.Equal(5, (int)MobileAppLifecycleState.Terminated);

        // BiometricAvailabilityResult
        var avail = new BiometricAvailabilityResult(true, BiometricType.Fingerprint, BiometricStatus.Available, "Ready");
        Assert.True(avail.IsAvailable);
        Assert.Equal(BiometricType.Fingerprint, avail.SupportedType);
        Assert.Equal(BiometricStatus.Available, avail.Status);
        Assert.Equal("Ready", avail.StatusMessage);
        var availClone = avail with { StatusMessage = "Updated" };
        Assert.Equal("Updated", availClone.StatusMessage);
        Assert.NotEqual(avail, availClone);

        // BiometricAuthRequest
        byte[] payload = [1, 2, 3];
        var req = new BiometricAuthRequest("Title", "Sub", "Desc", "key-alias", payload);
        Assert.Equal("Title", req.Title);
        Assert.Equal("Sub", req.Subtitle);
        Assert.Equal("Desc", req.Description);
        Assert.Equal("key-alias", req.KeyAlias);
        Assert.Equal(payload, req.EncryptedVaultPayload);
        var reqClone = req with { Title = "New Title" };
        Assert.Equal("New Title", reqClone.Title);

        // BiometricAuthResult
        var authRes = new BiometricAuthResult(true, payload, null, BiometricStatus.Available);
        Assert.True(authRes.Success);
        Assert.Equal(payload, authRes.DecryptedPayload);
        Assert.Null(authRes.ErrorMessage);
        Assert.Equal(BiometricStatus.Available, authRes.Status);
        var authClone = authRes with { Success = false, ErrorMessage = "Error" };
        Assert.False(authClone.Success);
        Assert.Equal("Error", authClone.ErrorMessage);

        // HardwareKeyGenerationResult
        var keyRes = new HardwareKeyGenerationResult(true, "alias-1", "pub-b64", null);
        Assert.True(keyRes.Success);
        Assert.Equal("alias-1", keyRes.KeyAlias);
        Assert.Equal("pub-b64", keyRes.PublicKeyBase64);
        Assert.Null(keyRes.ErrorMessage);
        var keyClone = keyRes with { Success = false, ErrorMessage = "Fail" };
        Assert.False(keyClone.Success);
        Assert.Equal("Fail", keyClone.ErrorMessage);
    }

    /// <summary>
    /// Verifies that <see cref="DefaultAndroidKeyStoreBridge"/> correctly handles key storage, retrieval, deletion, and biometric authentication.
    /// </summary>
    [Fact]
    public async Task DefaultAndroidKeyStoreBridge_LifecycleAndStorage_Branches()
    {
        // WHAT: Test DefaultAndroidKeyStoreBridge CRUD, zeroization, and biometric authentication prompt handling.
        // WHY:  Guarantees robust in-memory Android Keystore simulation for test environments.
        // HOW:  Tests store, overwrite, load, delete, clear, invalid parameters, and cancellation branches.
        var bridge = new DefaultAndroidKeyStoreBridge();
        Assert.True(bridge.IsHardwareBackedAvailable);

        var unavailableBridge = new DefaultAndroidKeyStoreBridge(hardwareAvailable: false);
        Assert.False(unavailableBridge.IsHardwareBackedAvailable);

        // Biometric prompt authentication default and cancellation
        Assert.True(await bridge.AuthenticateBiometricPromptAsync("Reason"));
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            Assert.False(await bridge.AuthenticateBiometricPromptAsync("Reason", cts.Token));
        }

        // Custom auth handler
        var customBridge = new DefaultAndroidKeyStoreBridge(
            hardwareAvailable: true,
            authHandler: (reason, ct) => Task.FromResult(reason == "allowed"));
        Assert.True(await customBridge.AuthenticateBiometricPromptAsync("allowed"));
        Assert.False(await customBridge.AuthenticateBiometricPromptAsync("denied"));

        // Store validation
        Assert.Throws<ArgumentNullException>(() => bridge.StoreWrappedKey(null!, [1, 2, 3]));
        Assert.Throws<ArgumentException>(() => bridge.StoreWrappedKey("", [1, 2, 3]));
        Assert.Throws<ArgumentException>(() => bridge.StoreWrappedKey("   ", [1, 2, 3]));

        // Store and overwrite
        bridge.StoreWrappedKey("key-1", [1, 2, 3, 4]);
        bridge.StoreWrappedKey("key-1", [5, 6, 7, 8]); // Overwrites and zeroizes existing

        // Load validation and defensive copy
        Assert.Throws<ArgumentNullException>(() => bridge.TryLoadWrappedKey(null!, out _));
        Assert.Throws<ArgumentException>(() => bridge.TryLoadWrappedKey("", out _));
        Assert.Throws<ArgumentException>(() => bridge.TryLoadWrappedKey("   ", out _));

        Assert.True(bridge.TryLoadWrappedKey("key-1", out byte[] loaded));
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, loaded);
        loaded[0] = 99; // Mutate local buffer
        Assert.True(bridge.TryLoadWrappedKey("key-1", out byte[] loadedAgain));
        Assert.Equal((byte)5, loadedAgain[0]); // Internal buffer remains unmodified

        Assert.False(bridge.TryLoadWrappedKey("nonexistent", out byte[] missing));
        Assert.Empty(missing);

        // Delete validation and execution
        Assert.Throws<ArgumentNullException>(() => bridge.DeleteWrappedKey(null!));
        Assert.Throws<ArgumentException>(() => bridge.DeleteWrappedKey(""));
        Assert.Throws<ArgumentException>(() => bridge.DeleteWrappedKey("   "));

        Assert.True(bridge.DeleteWrappedKey("key-1"));
        Assert.False(bridge.DeleteWrappedKey("key-1")); // Already deleted

        // ClearAllSessionKeys
        bridge.StoreWrappedKey("key-a", [10, 20]);
        bridge.StoreWrappedKey("key-b", [30, 40]);
        bridge.ClearAllSessionKeys();
        Assert.False(bridge.TryLoadWrappedKey("key-a", out _));
        Assert.False(bridge.TryLoadWrappedKey("key-b", out _));
    }

    /// <summary>
    /// Verifies that <see cref="DefaultAppleSecureEnclaveBridge"/> correctly handles secret storage, retrieval, deletion, and policy evaluation.
    /// </summary>
    [Fact]
    public async Task DefaultAppleSecureEnclaveBridge_LifecycleAndStorage_Branches()
    {
        // WHAT: Test DefaultAppleSecureEnclaveBridge CRUD, zeroization, and LocalAuthentication access control policy handling.
        // WHY:  Guarantees robust in-memory Apple Secure Enclave simulation for test environments.
        // HOW:  Tests store, overwrite, load, delete, clear, invalid parameters, and cancellation branches.
        var bridge = new DefaultAppleSecureEnclaveBridge();
        Assert.True(bridge.IsSecureEnclaveAvailable);

        var unavailableBridge = new DefaultAppleSecureEnclaveBridge(hardwareAvailable: false);
        Assert.False(unavailableBridge.IsSecureEnclaveAvailable);

        // Policy evaluation default and cancellation
        Assert.True(await bridge.EvaluateAccessControlPolicyAsync("Reason"));
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            Assert.False(await bridge.EvaluateAccessControlPolicyAsync("Reason", cts.Token));
        }

        // Custom policy handler
        var customBridge = new DefaultAppleSecureEnclaveBridge(
            hardwareAvailable: true,
            policyHandler: (reason, ct) => Task.FromResult(reason == "allowed"));
        Assert.True(await customBridge.EvaluateAccessControlPolicyAsync("allowed"));
        Assert.False(await customBridge.EvaluateAccessControlPolicyAsync("denied"));

        // Store validation
        Assert.Throws<ArgumentNullException>(() => bridge.StoreKeychainSecret(null!, [1, 2, 3]));
        Assert.Throws<ArgumentException>(() => bridge.StoreKeychainSecret("", [1, 2, 3]));
        Assert.Throws<ArgumentException>(() => bridge.StoreKeychainSecret("   ", [1, 2, 3]));

        // Store and overwrite
        bridge.StoreKeychainSecret("tag-1", [1, 2, 3, 4]);
        bridge.StoreKeychainSecret("tag-1", [5, 6, 7, 8]); // Overwrites and zeroizes existing

        // Read validation and defensive copy
        Assert.Throws<ArgumentNullException>(() => bridge.TryReadKeychainSecret(null!, out _));
        Assert.Throws<ArgumentException>(() => bridge.TryReadKeychainSecret("", out _));
        Assert.Throws<ArgumentException>(() => bridge.TryReadKeychainSecret("   ", out _));

        Assert.True(bridge.TryReadKeychainSecret("tag-1", out byte[] loaded));
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, loaded);
        loaded[0] = 99; // Mutate local buffer
        Assert.True(bridge.TryReadKeychainSecret("tag-1", out byte[] loadedAgain));
        Assert.Equal((byte)5, loadedAgain[0]); // Internal buffer remains unmodified

        Assert.False(bridge.TryReadKeychainSecret("nonexistent", out byte[] missing));
        Assert.Empty(missing);

        // Delete validation and execution
        Assert.Throws<ArgumentNullException>(() => bridge.DeleteKeychainSecret(null!));
        Assert.Throws<ArgumentException>(() => bridge.DeleteKeychainSecret(""));
        Assert.Throws<ArgumentException>(() => bridge.DeleteKeychainSecret("   "));

        Assert.True(bridge.DeleteKeychainSecret("tag-1"));
        Assert.False(bridge.DeleteKeychainSecret("tag-1")); // Already deleted

        // ClearAllSessionSecrets
        bridge.StoreKeychainSecret("tag-a", [10, 20]);
        bridge.StoreKeychainSecret("tag-b", [30, 40]);
        bridge.ClearAllSessionSecrets();
        Assert.False(bridge.TryReadKeychainSecret("tag-a", out _));
        Assert.False(bridge.TryReadKeychainSecret("tag-b", out _));
    }

    /// <summary>
    /// Verifies genuine BouncyCastle RFC 8032 Ed25519 key generation and signature verification in <see cref="AndroidBiometricSecurityProvider"/>.
    /// </summary>
    [Fact]
    public async Task AndroidBiometricSecurityProvider_Ed25519_BridgeSigningAndZeroization()
    {
        // WHAT: Test genuine Ed25519 key generation, challenge signing, signature verification, and memory zeroization on Android provider.
        // WHY:  Guarantees cryptographic patient identity security and full branch coverage.
        // HOW:  Validates inputs, generates key, signs challenge, verifies via Ed25519Signer, tests fallbacks and zeroization.
        static bool AndroidPlatform(string p) => p.Equals("Android", StringComparison.OrdinalIgnoreCase);
        var provider = new AndroidBiometricSecurityProvider(AndroidPlatform);
        Assert.NotNull(provider.KeyStoreBridge);

        // Input validation for GenerateHardwareKeyAsync
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.GenerateHardwareKeyAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GenerateHardwareKeyAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GenerateHardwareKeyAsync("   "));

        // Cancellation on GenerateHardwareKeyAsync
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => provider.GenerateHardwareKeyAsync("alias-cancel", cts.Token));
        }

        // Platform not supported on GenerateHardwareKeyAsync
        var nonAndroidProvider = new AndroidBiometricSecurityProvider(_ => false);
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => nonAndroidProvider.GenerateHardwareKeyAsync("alias-fail"));

        // Successful Ed25519 key generation
        byte[] publicKey = await provider.GenerateHardwareKeyAsync("ed25519-android-key");
        Assert.Equal(32, publicKey.Length);

        // Input validation for SignChallengeAsync
        byte[] challenge = "PATIENT-SESSION-CHALLENGE-2026"u8.ToArray();
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.SignChallengeAsync(null!, challenge));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.SignChallengeAsync("", challenge));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.SignChallengeAsync("   ", challenge));
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.SignChallengeAsync("ed25519-android-key", (byte[])null!));

        // Cancellation on SignChallengeAsync
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => provider.SignChallengeAsync("ed25519-android-key", challenge, cts.Token));
        }

        // Missing key on SignChallengeAsync
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SignChallengeAsync("missing-alias", challenge));

        // Genuine signature generation and RFC 8032 verification
        byte[] signature = await provider.SignChallengeAsync("ed25519-android-key", challenge);
        Assert.Equal(64, signature.Length);

        var verifier = new Ed25519Signer();
        var pubParams = new Ed25519PublicKeyParameters(publicKey, 0);
        verifier.Init(false, pubParams);
        verifier.BlockUpdate(challenge, 0, challenge.Length);
        Assert.True(verifier.VerifySignature(signature));

        // Tampered challenge verification fails
        byte[] tamperedChallenge = "TAMPERED-CHALLENGE-2026"u8.ToArray();
        verifier.Reset();
        verifier.BlockUpdate(tamperedChallenge, 0, tamperedChallenge.Length);
        Assert.False(verifier.VerifySignature(signature));

        // ReadOnlyMemory<byte> overload
        byte[] memorySig = await provider.SignChallengeAsync("ed25519-android-key", new ReadOnlyMemory<byte>(challenge));
        Assert.Equal(64, memorySig.Length);
        verifier.Reset();
        verifier.BlockUpdate(challenge, 0, challenge.Length);
        Assert.True(verifier.VerifySignature(memorySig));

        // Fallback branch where key is in _keyStore but not in bridge
        var mockBridge = new DefaultAndroidKeyStoreBridge();
        var fallbackProvider = new AndroidBiometricSecurityProvider(AndroidPlatform, keyStoreBridge: mockBridge);
        await fallbackProvider.GenerateHardwareBoundKeyAsync("fallback-key");
        mockBridge.ClearAllSessionKeys(); // Delete from bridge, keeping in fallbackProvider._keyStore

        byte[] plaintext = "REGIMEN-SECRET"u8.ToArray();
        byte[] envelope = fallbackProvider.EncryptPayload("fallback-key", plaintext);
        Assert.NotNull(envelope);

        var authReq = new BiometricAuthRequest("Auth", "Sub", "Desc", "fallback-key", envelope);
        var authRes = await fallbackProvider.AuthenticateAndDecryptAsync(authReq);
        Assert.True(authRes.Success);
        Assert.Equal(plaintext, authRes.DecryptedPayload);

        byte[] fallbackSig = await fallbackProvider.SignChallengeAsync("fallback-key", challenge);
        Assert.Equal(64, fallbackSig.Length);

        // AuthenticateAndDecrypt with key present but payload length < 28 bytes (exercising ZeroMemory on keyBytes)
        var shortReq = new BiometricAuthRequest("Auth", "Sub", "Desc", "fallback-key", [1, 2, 3]);
        var shortRes = await fallbackProvider.AuthenticateAndDecryptAsync(shortReq);
        Assert.False(shortRes.Success);
        Assert.Contains("invalid", shortRes.ErrorMessage);

        // ZeroizeMemory and Dispose
        provider.ZeroizeMemory();
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SignChallengeAsync("ed25519-android-key", challenge));
        provider.Dispose();
    }

    /// <summary>
    /// Verifies genuine BouncyCastle RFC 8032 Ed25519 key generation and signature verification in <see cref="AppleSecureEnclaveSecurityProvider"/>.
    /// </summary>
    [Fact]
    public async Task AppleSecureEnclaveSecurityProvider_Ed25519_BridgeSigningAndZeroization()
    {
        // WHAT: Test genuine Ed25519 key generation, challenge signing, signature verification, and memory zeroization on Apple provider.
        // WHY:  Guarantees cryptographic patient identity security and full branch coverage on Apple platforms.
        // HOW:  Validates inputs, generates key, signs challenge, verifies via Ed25519Signer, tests fallbacks and zeroization.
        static bool ApplePlatform(string p) => p.Equals("iOS", StringComparison.OrdinalIgnoreCase);
        var provider = new AppleSecureEnclaveSecurityProvider(ApplePlatform);
        Assert.NotNull(provider.EnclaveBridge);

        // Input validation for GenerateHardwareKeyAsync
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.GenerateHardwareKeyAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GenerateHardwareKeyAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GenerateHardwareKeyAsync("   "));

        // Cancellation on GenerateHardwareKeyAsync
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => provider.GenerateHardwareKeyAsync("alias-cancel", cts.Token));
        }

        // Platform not supported on GenerateHardwareKeyAsync
        var nonAppleProvider = new AppleSecureEnclaveSecurityProvider(_ => false);
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => nonAppleProvider.GenerateHardwareKeyAsync("alias-fail"));

        // Successful Ed25519 key generation
        byte[] publicKey = await provider.GenerateHardwareKeyAsync("ed25519-apple-key");
        Assert.Equal(32, publicKey.Length);

        // Input validation for SignChallengeAsync
        byte[] challenge = "APPLE-PATIENT-CHALLENGE-2026"u8.ToArray();
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.SignChallengeAsync(null!, challenge));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.SignChallengeAsync("", challenge));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.SignChallengeAsync("   ", challenge));
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.SignChallengeAsync("ed25519-apple-key", (byte[])null!));

        // Cancellation on SignChallengeAsync
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => provider.SignChallengeAsync("ed25519-apple-key", challenge, cts.Token));
        }

        // Missing key on SignChallengeAsync
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SignChallengeAsync("missing-alias", challenge));

        // Genuine signature generation and RFC 8032 verification
        byte[] signature = await provider.SignChallengeAsync("ed25519-apple-key", challenge);
        Assert.Equal(64, signature.Length);

        var verifier = new Ed25519Signer();
        var pubParams = new Ed25519PublicKeyParameters(publicKey, 0);
        verifier.Init(false, pubParams);
        verifier.BlockUpdate(challenge, 0, challenge.Length);
        Assert.True(verifier.VerifySignature(signature));

        // Tampered challenge verification fails
        byte[] tamperedChallenge = "TAMPERED-APPLE-CHALLENGE-2026"u8.ToArray();
        verifier.Reset();
        verifier.BlockUpdate(tamperedChallenge, 0, tamperedChallenge.Length);
        Assert.False(verifier.VerifySignature(signature));

        // ReadOnlyMemory<byte> overload
        byte[] memorySig = await provider.SignChallengeAsync("ed25519-apple-key", new ReadOnlyMemory<byte>(challenge));
        Assert.Equal(64, memorySig.Length);
        verifier.Reset();
        verifier.BlockUpdate(challenge, 0, challenge.Length);
        Assert.True(verifier.VerifySignature(memorySig));

        // Fallback branch where key is in _keyStore but not in bridge
        var mockBridge = new DefaultAppleSecureEnclaveBridge();
        var fallbackProvider = new AppleSecureEnclaveSecurityProvider(ApplePlatform, enclaveBridge: mockBridge);
        await fallbackProvider.GenerateHardwareBoundKeyAsync("fallback-key");
        mockBridge.ClearAllSessionSecrets(); // Delete from bridge, keeping in fallbackProvider._keyStore

        byte[] plaintext = "REGIMEN-SECRET"u8.ToArray();
        byte[] envelope = fallbackProvider.EncryptPayload("fallback-key", plaintext);
        Assert.NotNull(envelope);

        var authReq = new BiometricAuthRequest("Auth", "Sub", "Desc", "fallback-key", envelope);
        var authRes = await fallbackProvider.AuthenticateAndDecryptAsync(authReq);
        Assert.True(authRes.Success);
        Assert.Equal(plaintext, authRes.DecryptedPayload);

        byte[] fallbackSig = await fallbackProvider.SignChallengeAsync("fallback-key", challenge);
        Assert.Equal(64, fallbackSig.Length);

        // AuthenticateAndDecrypt with key present but payload length < 28 bytes (exercising ZeroMemory on keyBytes)
        var shortReq = new BiometricAuthRequest("Auth", "Sub", "Desc", "fallback-key", [1, 2, 3]);
        var shortRes = await fallbackProvider.AuthenticateAndDecryptAsync(shortReq);
        Assert.False(shortRes.Success);
        Assert.Contains("invalid", shortRes.ErrorMessage);

        // ZeroizeMemory and Dispose
        provider.ZeroizeMemory();
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SignChallengeAsync("ed25519-apple-key", challenge));
        provider.Dispose();
    }

    /// <summary>
    /// Verifies <see cref="MobileEntryPoint"/> biometric vault property, lifecycle zeroization hooks, and vault lock extension method.
    /// </summary>
    [Fact]
    public async Task MobileEntryPoint_BiometricVault_LifecycleAndZeroization_AllBranches()
    {
        // WHAT: Test MobileEntryPoint static biometric vault property and lifecycle zeroization hooks.
        // WHY:  Guarantees patient DNA regimens are purged from RAM across OnPause, OnStop, and backgrounding.
        // HOW:  Caches regimen, triggers lifecycle hooks, verifies zeroization, tests null vault safety, and checks extension method.
        // Extension method null check
        Assert.Throws<ArgumentNullException>(() => UnifiedMobileBiometricVaultExtensions.LockVault(null!));

        // LockVault with Android provider
        static bool AndroidPlatform(string p) => p.Equals("Android", StringComparison.OrdinalIgnoreCase);
        var androidVault = new UnifiedMobileBiometricVault(new AndroidBiometricSecurityProvider(AndroidPlatform));
        androidVault.CacheDecryptedRegimen("patient-1", [1, 2, 3, 4]);
        Assert.Equal(1, androidVault.CachedCount);
        androidVault.LockVault();
        Assert.Equal(0, androidVault.CachedCount);

        // LockVault with Apple provider
        static bool ApplePlatform(string p) => p.Equals("iOS", StringComparison.OrdinalIgnoreCase);
        var appleVault = new UnifiedMobileBiometricVault(new AppleSecureEnclaveSecurityProvider(ApplePlatform));
        appleVault.CacheDecryptedRegimen("patient-2", [5, 6, 7, 8]);
        Assert.Equal(1, appleVault.CachedCount);
        appleVault.LockVault();
        Assert.Equal(0, appleVault.CachedCount);

        // LockVault with other / fallback provider (exercises the final 'else' branch in LockVault)
        var otherVault = new UnifiedMobileBiometricVault(new DisposableTestBiometricProvider());
        otherVault.CacheDecryptedRegimen("patient-3", [9, 10]);
        Assert.Equal(1, otherVault.CachedCount);
        otherVault.LockVault();
        Assert.Equal(0, otherVault.CachedCount);

        // MobileEntryPoint BiometricVault getter and setter
        var originalVault = MobileEntryPoint.BiometricVault;
        try
        {
            var testVault = new UnifiedMobileBiometricVault(new AndroidBiometricSecurityProvider(AndroidPlatform));
            MobileEntryPoint.BiometricVault = testVault;
            Assert.Same(testVault, MobileEntryPoint.BiometricVault);

            // OnPause purges memory
            testVault.CacheDecryptedRegimen("patient-android", [11, 12, 13]);
            Assert.Equal(1, testVault.CachedCount);
            MobileEntryPoint.OnPause();
            Assert.Equal(0, testVault.CachedCount);

            // OnStop purges memory
            testVault.CacheDecryptedRegimen("patient-android", [14, 15, 16]);
            Assert.Equal(1, testVault.CachedCount);
            MobileEntryPoint.OnStop();
            Assert.Equal(0, testVault.CachedCount);

            // OnBackground purges memory
            testVault.CacheDecryptedRegimen("patient-android", [17, 18, 19]);
            Assert.Equal(1, testVault.CachedCount);
            MobileEntryPoint.OnBackground();
            Assert.Equal(0, testVault.CachedCount);

            // Safe execution when BiometricVault is null
            MobileEntryPoint.BiometricVault = null;
            Assert.Null(MobileEntryPoint.BiometricVault);
            MobileEntryPoint.OnPause();
            MobileEntryPoint.OnStop();
            MobileEntryPoint.OnBackground();

            // RunMobileLifecycleAsync pre-cancelled token triggers LockVault
            MobileEntryPoint.BiometricVault = testVault;
            testVault.CacheDecryptedRegimen("patient-lifecycle", [20, 21]);
            Assert.Equal(1, testVault.CachedCount);
            using var preCts = new CancellationTokenSource();
            preCts.Cancel();
            var sink = new MobileNotificationSink("android");
            int preExit = await MobileEntryPoint.RunMobileLifecycleAsync("android", sink, preCts.Token);
            Assert.Equal(0, preExit);
            Assert.Equal(0, testVault.CachedCount);

            // RunMobileLifecycleAsync active delay cancellation triggers LockVault in catch block
            testVault.CacheDecryptedRegimen("patient-lifecycle-live", [22, 23]);
            Assert.Equal(1, testVault.CachedCount);
            using var liveCts = new CancellationTokenSource();
            var lifecycleTask = MobileEntryPoint.RunMobileLifecycleAsync("android", sink, liveCts.Token);
            liveCts.Cancel();
            int liveExit = await lifecycleTask;
            Assert.Equal(0, liveExit);
            Assert.Equal(0, testVault.CachedCount);
        }
        finally
        {
            MobileEntryPoint.BiometricVault = originalVault;
        }
    }

    /// <summary>
    /// Verifies DownloadAndCachePatientPackageAsync downloads patient DNA blueprints and caches them in volatile memory.
    /// </summary>
    [Fact]
    public async Task UnifiedMobileBiometricVault_DownloadAndCachePatientPackageAsync_DownloadsAndCachesInRAM()
    {
        // WHAT: Tests DownloadAndCachePatientPackageAsync integration with INanoWeaverNodeClient.
        // WHY:  Guarantees genomic blueprints are loaded into the zeroizable biometric vault on mobile clients.
        // HOW:  Mocks GetPatientDnaRegimensAsync, executes download, verifies package metadata and decrypted buffer.
        using var vault = new UnifiedMobileBiometricVault();
        var clientMock = new Mock<INanoWeaverNodeClient>();
        var profile = new InstanceProfile(
            "node-1",
            "Node 1",
            "http://127.0.0.1:5000",
            true,
            "patient-1",
            "Patient",
            "Ed25519",
            "pub",
            "env",
            false,
            10,
            DateTimeOffset.UtcNow,
            null);

        var expectedPackage = new PatientDigitalDnaPackage
        {
            PatientId = "patient-1",
            ScaffoldFasta = "ATCG-SCAFFOLD-12345",
            NanocarrierGeometry = "Hexagonal Nanotube",
            ProvenanceSha256 = "sha256-provenance-digest"
        };

        clientMock.Setup(c => c.GetPatientDnaRegimensAsync(profile, "token-xyz", "patient-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedPackage);

        // Nominal download and caching
        PatientDigitalDnaPackage downloaded = await vault.DownloadAndCachePatientPackageAsync(
            clientMock.Object,
            profile,
            "token-xyz",
            "patient-1");

        Assert.Equal("patient-1", downloaded.PatientId);
        Assert.Equal(1, vault.CachedCount);
        Assert.True(vault.TryGetDecryptedRegimen("patient-1", out byte[]? cachedPayload));
        Assert.NotNull(cachedPayload);
        Assert.Equal("ATCG-SCAFFOLD-12345", Encoding.UTF8.GetString(cachedPayload));

        // Argument validation guards
        await Assert.ThrowsAsync<ArgumentNullException>(() => vault.DownloadAndCachePatientPackageAsync(null!, profile, "token", "patient-1"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => vault.DownloadAndCachePatientPackageAsync(clientMock.Object, null!, "token", "patient-1"));
        await Assert.ThrowsAsync<ArgumentException>(() => vault.DownloadAndCachePatientPackageAsync(clientMock.Object, profile, "", "patient-1"));
        await Assert.ThrowsAsync<ArgumentException>(() => vault.DownloadAndCachePatientPackageAsync(clientMock.Object, profile, "token", ""));
    }
}
