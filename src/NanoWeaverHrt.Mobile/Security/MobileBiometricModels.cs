namespace NanoWeaverHrt.Mobile.Security;

/// <summary>
/// Specifies the hardware biometric sensing modality supported or utilized by the mobile device.
/// </summary>
public enum BiometricType
{
    /// <summary>
    /// No biometric authentication hardware is available.
    /// </summary>
    None = 0,

    /// <summary>
    /// Physical fingerprint scanner (Android FingerprintManager or generic sensor).
    /// </summary>
    Fingerprint = 1,

    /// <summary>
    /// Generic facial recognition sensor.
    /// </summary>
    Face = 2,

    /// <summary>
    /// Iris scanning optical sensor.
    /// </summary>
    Iris = 3,

    /// <summary>
    /// Apple Touch ID capacitive or optical fingerprint sensor.
    /// </summary>
    AppleTouchId = 4,

    /// <summary>
    /// Apple Face ID structured-light 3D facial recognition sensor.
    /// </summary>
    AppleFaceId = 5
}

/// <summary>
/// Represents the operational or enrollment status of the biometric subsystem.
/// </summary>
public enum BiometricStatus
{
    /// <summary>
    /// Biometric hardware is operational and user biometric identities are enrolled.
    /// </summary>
    Available = 0,

    /// <summary>
    /// Biometric hardware is present, but no biometric credentials have been enrolled in device settings.
    /// </summary>
    NotEnrolled = 1,

    /// <summary>
    /// Biometric hardware is absent, disabled, or not supported by the underlying operating system.
    /// </summary>
    HardwareUnavailable = 2,

    /// <summary>
    /// Biometric authentication is locked out due to excessive failed attempts or device PIN requirements.
    /// </summary>
    Lockout = 3,

    /// <summary>
    /// The biometric authentication prompt was cancelled by the user or host application.
    /// </summary>
    UserCancelled = 4,

    /// <summary>
    /// Biometric sensor evaluation failed or encountered an unrecoverable hardware error.
    /// </summary>
    Failed = 5
}

/// <summary>
/// Represents host operating system mobile application lifecycle state transitions.
/// </summary>
public enum MobileAppLifecycleState
{
    /// <summary>
    /// Application has resumed execution from background or paused state.
    /// </summary>
    Resumed = 0,

    /// <summary>
    /// Application is actively running in the foreground and receiving user interactions.
    /// </summary>
    Active = 1,

    /// <summary>
    /// Application has lost foreground focus or is partially obscured by system UI.
    /// </summary>
    Paused = 2,

    /// <summary>
    /// Application is running in the background and no longer visible to the operator.
    /// </summary>
    Backgrounded = 3,

    /// <summary>
    /// Application execution has been suspended or stopped by the host mobile operating system.
    /// </summary>
    Stopped = 4,

    /// <summary>
    /// Application process is undergoing graceful or forced process termination.
    /// </summary>
    Terminated = 5
}

/// <summary>
/// Sealed diagnostic record representing biometric hardware availability and enrollment status.
/// </summary>
/// <param name="IsAvailable">Indicates whether biometric authentication is ready and enrolled on the device.</param>
/// <param name="SupportedType">The specific biometric sensor modality supported by the host device.</param>
/// <param name="Status">Detailed availability status enumeration.</param>
/// <param name="StatusMessage">Human-readable diagnostic or error status message.</param>
public sealed record BiometricAvailabilityResult(
    bool IsAvailable,
    BiometricType SupportedType,
    BiometricStatus Status,
    string StatusMessage);

/// <summary>
/// Sealed record encapsulating an authentication and payload decryption request for the biometric provider.
/// </summary>
/// <param name="Title">Title displayed on the OS-native biometric authentication prompt.</param>
/// <param name="Subtitle">Subtitle displayed on the OS-native biometric prompt.</param>
/// <param name="Description">Detailed description of the cryptographic vault payload being unlocked.</param>
/// <param name="KeyAlias">Unique alias of the hardware-bound key inside the Secure Enclave or Keystore.</param>
/// <param name="EncryptedVaultPayload">Ciphertext payload containing encrypted patient DNA regimen data.</param>
public sealed record BiometricAuthRequest(
    string Title,
    string Subtitle,
    string Description,
    string KeyAlias,
    byte[] EncryptedVaultPayload);

/// <summary>
/// Sealed record encapsulating the result of a biometric authentication and decryption attempt.
/// </summary>
/// <param name="Success">Indicates whether biometric authentication and payload decryption succeeded.</param>
/// <param name="DecryptedPayload">Plaintext payload bytes if successful; otherwise <see langword="null"/>.</param>
/// <param name="ErrorMessage">Descriptive error message if authentication or decryption failed; otherwise <see langword="null"/>.</param>
/// <param name="Status">Biometric operational status returned by the OS authentication subsystem.</param>
public sealed record BiometricAuthResult(
    bool Success,
    byte[]? DecryptedPayload,
    string? ErrorMessage,
    BiometricStatus Status);

/// <summary>
/// Sealed record encapsulating the result of a hardware-bound cryptographic key generation operation.
/// </summary>
/// <param name="Success">Indicates whether hardware key generation succeeded.</param>
/// <param name="KeyAlias">Unique alias identifying the key within the hardware module.</param>
/// <param name="PublicKeyBase64">Base-64 encoded public key representation of the hardware-bound key.</param>
/// <param name="ErrorMessage">Descriptive error message if key generation failed; otherwise <see langword="null"/>.</param>
public sealed record HardwareKeyGenerationResult(
    bool Success,
    string KeyAlias,
    string PublicKeyBase64,
    string? ErrorMessage);
