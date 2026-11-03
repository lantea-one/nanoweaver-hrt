namespace NanoWeaverHrt.Mobile.Security;

/// <summary>
/// Defines the contract for OS-native biometric authentication and hardware-bound secure enclave key operations.
/// </summary>
/// <remarks>
/// // WHAT: Interface abstracting mobile hardware biometrics and cryptographic enclave vaults.
/// // WHY:  Allows unified patient regimen decryption across Android BiometricPrompt and Apple Secure Enclave.
/// // HOW:  Provides asynchronous methods for capability probing, interactive authentication with decryption, and key generation.
/// </remarks>
public interface IBiometricSecurityProvider
{
    /// <summary>
    /// Checks biometric hardware availability and credential enrollment on the host mobile platform.
    /// </summary>
    /// <param name="ct">Cancellation token for cancelling the availability check operation.</param>
    /// <returns>A task representing the asynchronous operation that yields a <see cref="BiometricAvailabilityResult"/>.</returns>
    Task<BiometricAvailabilityResult> CheckBiometricAvailabilityAsync(CancellationToken ct = default);

    /// <summary>
    /// Presents an OS-native biometric authentication prompt and decrypts the encrypted vault payload upon success.
    /// </summary>
    /// <param name="request">Authentication request parameters including prompt strings, key alias, and ciphertext payload.</param>
    /// <param name="ct">Cancellation token for cancelling the interactive authentication operation.</param>
    /// <returns>A task representing the asynchronous operation that yields a <see cref="BiometricAuthResult"/>.</returns>
    Task<BiometricAuthResult> AuthenticateAndDecryptAsync(BiometricAuthRequest request, CancellationToken ct = default);

    /// <summary>
    /// Generates and persists a hardware-bound cryptographic key pair in the secure hardware module.
    /// </summary>
    /// <param name="keyAlias">Unique alias to identify the generated hardware key.</param>
    /// <param name="ct">Cancellation token for cancelling the key generation operation.</param>
    /// <returns>A task representing the asynchronous operation that yields a <see cref="HardwareKeyGenerationResult"/>.</returns>
    Task<HardwareKeyGenerationResult> GenerateHardwareBoundKeyAsync(string keyAlias, CancellationToken ct = default);
}
