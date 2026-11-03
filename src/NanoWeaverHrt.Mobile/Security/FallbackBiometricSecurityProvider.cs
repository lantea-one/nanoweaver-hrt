namespace NanoWeaverHrt.Mobile.Security;

/// <summary>
/// Fallback biometric security provider used on platforms lacking native biometric hardware support.
/// </summary>
/// <remarks>
/// // WHAT: Safe fallback implementation of <see cref="IBiometricSecurityProvider"/>.
/// // WHY:  Prevents null reference exceptions on non-mobile desktop or server platforms while reporting hardware unavailability.
/// // HOW:  Returns <see cref="BiometricStatus.HardwareUnavailable"/> for all operations while supporting cancellation checks.
/// </remarks>
public sealed class FallbackBiometricSecurityProvider : IBiometricSecurityProvider
{
    /// <inheritdoc/>
    public Task<BiometricAvailabilityResult> CheckBiometricAvailabilityAsync(CancellationToken ct = default)
    {
        // WHAT: Return hardware unavailable availability result.
        // WHY:  Informs callers that host platform does not possess mobile biometric hardware.
        // HOW:  Checks cancellation; returns user cancelled or hardware unavailable result.
        if (ct.IsCancellationRequested)
        {
            return Task.FromResult(new BiometricAvailabilityResult(
                IsAvailable: false,
                SupportedType: BiometricType.None,
                Status: BiometricStatus.UserCancelled,
                StatusMessage: "Biometric availability check was cancelled."));
        }

        return Task.FromResult(new BiometricAvailabilityResult(
            IsAvailable: false,
            SupportedType: BiometricType.None,
            Status: BiometricStatus.HardwareUnavailable,
            StatusMessage: "Native biometric hardware is not supported on this platform."));
    }

    /// <inheritdoc/>
    public Task<BiometricAuthResult> AuthenticateAndDecryptAsync(BiometricAuthRequest request, CancellationToken ct = default)
    {
        // WHAT: Reject authentication attempt on unsupported host platform.
        // WHY:  Vault decryption cannot be executed without biometric hardware enclave support.
        // HOW:  Validates request arguments, checks cancellation, and returns failed auth result.
        ArgumentNullException.ThrowIfNull(request);

        if (ct.IsCancellationRequested)
        {
            return Task.FromResult(new BiometricAuthResult(
                Success: false,
                DecryptedPayload: null,
                ErrorMessage: "Biometric authentication was cancelled.",
                Status: BiometricStatus.UserCancelled));
        }

        return Task.FromResult(new BiometricAuthResult(
            Success: false,
            DecryptedPayload: null,
            ErrorMessage: "Biometric authentication is not supported on this platform.",
            Status: BiometricStatus.HardwareUnavailable));
    }

    /// <inheritdoc/>
    public Task<HardwareKeyGenerationResult> GenerateHardwareBoundKeyAsync(string keyAlias, CancellationToken ct = default)
    {
        // WHAT: Reject hardware-bound key generation on unsupported platform.
        // WHY:  Hardware key generation requires a physical TEE or Secure Enclave.
        // HOW:  Validates key alias, checks cancellation, and returns failure result.
        ArgumentException.ThrowIfNullOrWhiteSpace(keyAlias);

        if (ct.IsCancellationRequested)
        {
            return Task.FromResult(new HardwareKeyGenerationResult(
                Success: false,
                KeyAlias: keyAlias,
                PublicKeyBase64: string.Empty,
                ErrorMessage: "Key generation was cancelled."));
        }

        return Task.FromResult(new HardwareKeyGenerationResult(
            Success: false,
            KeyAlias: keyAlias,
            PublicKeyBase64: string.Empty,
            ErrorMessage: "Hardware-bound key generation is not supported on this platform."));
    }
}
