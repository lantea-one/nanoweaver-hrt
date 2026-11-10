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
    /// <param name="requiresBiometric">Flag indicating whether access to this key or derived keys requires biometric prompt authorization.</param>
    void StoreWrappedKey(string alias, ReadOnlySpan<byte> keyMaterial, bool requiresBiometric = false);

    /// <summary>
    /// Attempts to load wrapped or raw cryptographic key material from the Android Keystore.
    /// </summary>
    /// <param name="alias">Unique key alias identifying the key inside the Android Keystore.</param>
    /// <param name="keyMaterial">When this method returns, contains defensive copy of key material if found; otherwise an empty array.</param>
    /// <returns><see langword="true"/> if the key alias was located in the Keystore; otherwise <see langword="false"/>.</returns>
    bool TryLoadWrappedKey(string alias, out byte[] keyMaterial);

    /// <summary>
    /// Attempts to load the domain-separated Ed25519 signing key derived from the master seed via HKDF.
    /// </summary>
    /// <param name="alias">Unique key alias identifying the key inside the Android Keystore.</param>
    /// <param name="signingKey">When this method returns, contains defensive copy of the derived 32-byte signing key if found and authorized; otherwise an empty array.</param>
    /// <returns><see langword="true"/> if the key was located and authorized; otherwise <see langword="false"/>.</returns>
    bool TryLoadKeyForSigning(string alias, out byte[] signingKey);

    /// <summary>
    /// Attempts to load the domain-separated AES-256-GCM wrapping key derived from the master seed via HKDF.
    /// </summary>
    /// <param name="alias">Unique key alias identifying the key inside the Android Keystore.</param>
    /// <param name="wrappingKey">When this method returns, contains defensive copy of the derived 32-byte wrapping key if found and authorized; otherwise an empty array.</param>
    /// <returns><see langword="true"/> if the key was located and authorized; otherwise <see langword="false"/>.</returns>
    bool TryLoadKeyForWrapping(string alias, out byte[] wrappingKey);

    /// <summary>
    /// Asynchronously retrieves the domain-separated Ed25519 signing key derived from the master seed, evaluating BiometricPrompt if required.
    /// </summary>
    /// <param name="alias">Unique key alias identifying the key.</param>
    /// <param name="reason">Prompt explanation displayed if biometric authentication is enforced.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task yielding the derived 32-byte signing key if successful and authorized; otherwise <see langword="null"/>.</returns>
    Task<byte[]?> GetKeyForSigningAsync(string alias, string? reason = null, CancellationToken ct = default);

    /// <summary>
    /// Asynchronously retrieves the domain-separated AES-256-GCM wrapping key derived from the master seed, evaluating BiometricPrompt if required.
    /// </summary>
    /// <param name="alias">Unique key alias identifying the key.</param>
    /// <param name="reason">Prompt explanation displayed if biometric authentication is enforced.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task yielding the derived 32-byte wrapping key if successful and authorized; otherwise <see langword="null"/>.</returns>
    Task<byte[]?> GetKeyForWrappingAsync(string alias, string? reason = null, CancellationToken ct = default);

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
