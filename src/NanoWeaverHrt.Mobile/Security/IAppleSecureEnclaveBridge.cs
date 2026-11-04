namespace NanoWeaverHrt.Mobile.Security;

/// <summary>
/// Defines the bridge interface abstracting Apple Secure Enclave hardware-backed cryptographic operations and LocalAuthentication access control.
/// </summary>
/// <remarks>
/// // WHAT: Interface bridging Apple Secure Enclave processor and LocalAuthentication policies.
/// // WHY:  Decouples the Apple platform head from the mobile security provider for deterministic zero-hardware testing.
/// // HOW:  Exposes properties for hardware availability, access control policy evaluation, and keychain secret persistence primitives.
/// </remarks>
public interface IAppleSecureEnclaveBridge
{
    /// <summary>
    /// Gets a value indicating whether Apple Secure Enclave hardware is available on the current device.
    /// </summary>
    bool IsSecureEnclaveAvailable { get; }

    /// <summary>
    /// Evaluates an Apple LocalAuthentication access control policy (Touch ID / Face ID / Passcode).
    /// </summary>
    /// <param name="localizedReason">Localized string explaining why biometric authorization is requested.</param>
    /// <param name="ct">Cancellation token for cancelling the policy evaluation.</param>
    /// <returns>A task representing the asynchronous operation that yields <see langword="true"/> if policy evaluation succeeded; otherwise <see langword="false"/>.</returns>
    Task<bool> EvaluateAccessControlPolicyAsync(string localizedReason, CancellationToken ct = default);

    /// <summary>
    /// Stores secret key material inside the Apple Secure Enclave keychain.
    /// </summary>
    /// <param name="tag">Unique tag identifying the secret in the keychain.</param>
    /// <param name="secretBytes">Byte span containing the raw secret material.</param>
    void StoreKeychainSecret(string tag, ReadOnlySpan<byte> secretBytes);

    /// <summary>
    /// Attempts to read secret key material from the Apple Secure Enclave keychain.
    /// </summary>
    /// <param name="tag">Unique tag identifying the secret in the keychain.</param>
    /// <param name="secretBytes">When this method returns, contains defensive copy of secret bytes if found; otherwise an empty array.</param>
    /// <returns><see langword="true"/> if the keychain tag was located; otherwise <see langword="false"/>.</returns>
    bool TryReadKeychainSecret(string tag, out byte[] secretBytes);

    /// <summary>
    /// Deletes a secret from the Apple Secure Enclave keychain and zeroizes its memory buffer.
    /// </summary>
    /// <param name="tag">Unique keychain tag to delete.</param>
    /// <returns><see langword="true"/> if the secret existed and was deleted; otherwise <see langword="false"/>.</returns>
    bool DeleteKeychainSecret(string tag);

    /// <summary>
    /// Purges and zeroizes all session secrets stored in the Apple Secure Enclave keychain.
    /// </summary>
    void ClearAllSessionSecrets();
}
