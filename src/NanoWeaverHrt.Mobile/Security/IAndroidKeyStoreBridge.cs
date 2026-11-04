namespace NanoWeaverHrt.Mobile.Security;

/// <summary>
/// Defines the bridge interface abstracting Android Keystore hardware-backed cryptographic operations and biometric authentication.
/// </summary>
/// <remarks>
/// // WHAT: Interface bridging Android Keystore TEE / StrongBox hardware storage and BiometricPrompt evaluation.
/// // WHY:  Decouples the Android platform head from the mobile security provider for deterministic zero-hardware testing.
/// // HOW:  Exposes properties for hardware availability, biometric prompt authentication, and key persistence primitives.
/// </remarks>
public interface IAndroidKeyStoreBridge
{
    /// <summary>
    /// Gets a value indicating whether hardware-backed key storage (Android TEE or StrongBox) is available on the current device.
    /// </summary>
    bool IsHardwareBackedAvailable { get; }

    /// <summary>
    /// Authenticates the user using Android BiometricPrompt.
    /// </summary>
    /// <param name="reason">Localized explanation displayed to the user explaining why biometric authorization is requested.</param>
    /// <param name="ct">Cancellation token for cancelling the biometric prompt operation.</param>
    /// <returns>A task representing the asynchronous operation that yields <see langword="true"/> if biometric authentication succeeded; otherwise <see langword="false"/>.</returns>
    Task<bool> AuthenticateBiometricPromptAsync(string reason, CancellationToken ct = default);

    /// <summary>
    /// Stores wrapped or raw cryptographic key material inside the Android Keystore.
    /// </summary>
    /// <param name="alias">Unique key alias identifying the key inside the Android Keystore.</param>
    /// <param name="keyMaterial">Byte span containing the raw or wrapped key material to persist.</param>
    void StoreWrappedKey(string alias, ReadOnlySpan<byte> keyMaterial);

    /// <summary>
    /// Attempts to load wrapped or raw cryptographic key material from the Android Keystore.
    /// </summary>
    /// <param name="alias">Unique key alias identifying the key inside the Android Keystore.</param>
    /// <param name="keyMaterial">When this method returns, contains defensive copy of key material if found; otherwise an empty array.</param>
    /// <returns><see langword="true"/> if the key alias was located in the Keystore; otherwise <see langword="false"/>.</returns>
    bool TryLoadWrappedKey(string alias, out byte[] keyMaterial);

    /// <summary>
    /// Deletes a wrapped key from the Android Keystore and zeroizes its buffer.
    /// </summary>
    /// <param name="alias">Unique key alias to delete.</param>
    /// <returns><see langword="true"/> if the key existed and was deleted; otherwise <see langword="false"/>.</returns>
    bool DeleteWrappedKey(string alias);

    /// <summary>
    /// Purges and zeroizes all session keys stored in the Android Keystore bridge.
    /// </summary>
    void ClearAllSessionKeys();
}
