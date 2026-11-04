using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace NanoWeaverHrt.Mobile.Security;

/// <summary>
/// Default in-memory simulation implementation of <see cref="IAndroidKeyStoreBridge"/> modeling
/// Android Keystore hardware storage with zero-copy safety and RAM zeroization.
/// </summary>
/// <remarks>
/// // WHAT: Default implementation of Android Keystore bridge storing keys in protected memory.
/// // WHY:  Provides full functional testing of Android Keystore operations on non-Android test harnesses.
/// // HOW:  Uses ConcurrentDictionary to store defensive key copies and zeroizes memory on deletion or clearing.
/// </remarks>
public sealed class DefaultAndroidKeyStoreBridge : IAndroidKeyStoreBridge
{
    /// <summary>
    /// Thread-safe in-memory store mapping key aliases to their wrapped or raw byte buffers.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte[]> _store = new(StringComparer.Ordinal);

    /// <summary>
    /// Value indicating whether hardware-backed TEE / StrongBox simulation is enabled.
    /// </summary>
    private readonly bool _hardwareAvailable;

    /// <summary>
    /// Optional delegate handling biometric prompt evaluation for deterministic testing.
    /// </summary>
    private readonly Func<string, CancellationToken, Task<bool>>? _authHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAndroidKeyStoreBridge"/> class.
    /// </summary>
    /// <param name="hardwareAvailable">Flag indicating whether simulated hardware-backed storage is available.</param>
    /// <param name="authHandler">Optional delegate simulating BiometricPrompt authentication responses.</param>
    public DefaultAndroidKeyStoreBridge(
        bool hardwareAvailable = true,
        Func<string, CancellationToken, Task<bool>>? authHandler = null)
    {
        // WHAT: Initialize bridge with availability flag and optional authentication handler.
        // WHY:  Permits simulating both healthy and hardware-failing Android Keystore environments.
        // HOW:  Stores parameter values in private fields.
        _hardwareAvailable = hardwareAvailable;
        _authHandler = authHandler;
    }

    /// <inheritdoc/>
    public bool IsHardwareBackedAvailable => _hardwareAvailable;

    /// <inheritdoc/>
    public Task<bool> AuthenticateBiometricPromptAsync(string reason, CancellationToken ct = default)
    {
        // WHAT: Evaluate simulated Android BiometricPrompt challenge.
        // WHY:  Authorizes access to sensitive cryptographic key material.
        // HOW:  Checks cancellation token, invokes custom handler if provided, or returns true by default.
        if (ct.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        if (_authHandler != null)
        {
            return _authHandler(reason, ct);
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public void StoreWrappedKey(string alias, ReadOnlySpan<byte> keyMaterial)
    {
        // WHAT: Persist defensive copy of key material under alias.
        // WHY:  Guarantees isolated key storage without caller buffer retention.
        // HOW:  Validates alias, wipes any existing buffer, allocates clone, and registers in dictionary.
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        if (_store.TryGetValue(alias, out byte[]? existing))
        {
            CryptographicOperations.ZeroMemory(existing);
        }

        byte[] copy = keyMaterial.ToArray();
        _store[alias] = copy;
    }

    /// <inheritdoc/>
    public bool TryLoadWrappedKey(string alias, out byte[] keyMaterial)
    {
        // WHAT: Retrieve defensive copy of key material if alias exists.
        // WHY:  Prevents external code from mutating the internally stored buffer.
        // HOW:  Validates alias, checks store, returns cloned array on match, or empty array on miss.
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        if (_store.TryGetValue(alias, out byte[]? stored))
        {
            keyMaterial = (byte[])stored.Clone();
            return true;
        }

        keyMaterial = Array.Empty<byte>();
        return false;
    }

    /// <inheritdoc/>
    public bool DeleteWrappedKey(string alias)
    {
        // WHAT: Remove key from Keystore and zeroize its buffer.
        // WHY:  Ensures deleted key material cannot linger in process memory.
        // HOW:  Validates alias, removes entry, zeroizes removed array via ZeroMemory, and returns success flag.
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        if (_store.TryRemove(alias, out byte[]? removed))
        {
            CryptographicOperations.ZeroMemory(removed);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public void ClearAllSessionKeys()
    {
        // WHAT: Zeroize and remove all keys currently held in the bridge.
        // WHY:  Provides complete memory cleanup during session reset or lifecycle pause.
        // HOW:  Iterates over all stored arrays, wipes each via ZeroMemory, and clears the dictionary.
        foreach (byte[] key in _store.Values)
        {
            CryptographicOperations.ZeroMemory(key);
        }

        _store.Clear();
    }
}

/// <summary>
/// Extension methods for <see cref="UnifiedMobileBiometricVault"/> mobile lifecycle coordination.
/// </summary>
/// <remarks>
/// // WHAT: Extension methods facilitating explicit vault locking and lifecycle-driven memory zeroization.
/// // WHY:  Allows mobile platform lifecycle hooks to purge decrypted patient DNA regimens in a single call.
/// // HOW:  Dispatches MobileAppLifecycleState.Paused to the vault and zeroizes underlying provider memory.
/// </remarks>
public static class UnifiedMobileBiometricVaultExtensions
{
    /// <summary>
    /// Locks the biometric vault immediately, purging and zeroizing all decrypted patient DNA regimens in memory.
    /// </summary>
    /// <param name="vault">The unified biometric vault instance to lock.</param>
    public static void LockVault(this UnifiedMobileBiometricVault vault)
    {
        // WHAT: Immediately lock the vault and zeroize decrypted regimens and session keys.
        // WHY:  Guarantees zero-exposure of plaintext genomic data when application leaves foreground.
        // HOW:  Notifies vault of Paused lifecycle state and invokes ZeroizeMemory on active provider if supported.
        ArgumentNullException.ThrowIfNull(vault);

        vault.OnAppLifecycleStateChanged(MobileAppLifecycleState.Paused);

        if (vault.ActiveProvider is AndroidBiometricSecurityProvider androidProvider)
        {
            androidProvider.ZeroizeMemory();
        }
        else if (vault.ActiveProvider is AppleSecureEnclaveSecurityProvider appleProvider)
        {
            appleProvider.ZeroizeMemory();
        }
    }
}
