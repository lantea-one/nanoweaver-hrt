using System.Security.Cryptography;
using NanoWeaverHrt.Mobile.Security;
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
}
