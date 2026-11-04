using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace NanoWeaverHrt.Mobile.Security;

/// <summary>
/// Default in-memory simulation implementation of <see cref="IAppleSecureEnclaveBridge"/> modeling
/// Apple Secure Enclave hardware keychain storage with zero-copy safety and RAM zeroization.
/// </summary>
/// <remarks>
/// // WHAT: Default implementation of Apple Secure Enclave bridge storing secrets in protected memory.
/// // WHY:  Provides full functional testing of Secure Enclave operations on non-Apple test harnesses.
/// // HOW:  Uses ConcurrentDictionary to store defensive secret copies and zeroizes memory on deletion or clearing.
/// </remarks>
public sealed class DefaultAppleSecureEnclaveBridge : IAppleSecureEnclaveBridge
{
    /// <summary>
    /// Thread-safe in-memory store mapping keychain tags to their secret byte buffers.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte[]> _keychain = new(StringComparer.Ordinal);

    /// <summary>
    /// Value indicating whether hardware-backed Secure Enclave simulation is enabled.
    /// </summary>
    private readonly bool _hardwareAvailable;

    /// <summary>
    /// Optional delegate handling LocalAuthentication access control policy evaluation for deterministic testing.
    /// </summary>
    private readonly Func<string, CancellationToken, Task<bool>>? _policyHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAppleSecureEnclaveBridge"/> class.
    /// </summary>
    /// <param name="hardwareAvailable">Flag indicating whether simulated Secure Enclave hardware is available.</param>
    /// <param name="policyHandler">Optional delegate simulating LAContext policy evaluation responses.</param>
    public DefaultAppleSecureEnclaveBridge(
        bool hardwareAvailable = true,
        Func<string, CancellationToken, Task<bool>>? policyHandler = null)
    {
        // WHAT: Initialize bridge with availability flag and optional policy evaluation handler.
        // WHY:  Permits simulating both healthy and hardware-unavailable Apple Secure Enclave environments.
        // HOW:  Stores parameter values in private fields.
        _hardwareAvailable = hardwareAvailable;
        _policyHandler = policyHandler;
    }

    /// <inheritdoc/>
    public bool IsSecureEnclaveAvailable => _hardwareAvailable;

    /// <inheritdoc/>
    public Task<bool> EvaluateAccessControlPolicyAsync(string localizedReason, CancellationToken ct = default)
    {
        // WHAT: Evaluate simulated Apple LocalAuthentication policy.
        // WHY:  Authorizes access to sensitive keychain secret material.
        // HOW:  Checks cancellation token, invokes custom handler if provided, or returns true by default.
        if (ct.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        if (_policyHandler != null)
        {
            return _policyHandler(localizedReason, ct);
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public void StoreKeychainSecret(string tag, ReadOnlySpan<byte> secretBytes)
    {
        // WHAT: Persist defensive copy of secret material under tag.
        // WHY:  Guarantees isolated keychain secret storage without caller buffer retention.
        // HOW:  Validates tag, wipes any existing buffer, allocates clone, and registers in dictionary.
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        if (_keychain.TryGetValue(tag, out byte[]? existing))
        {
            CryptographicOperations.ZeroMemory(existing);
        }

        byte[] copy = secretBytes.ToArray();
        _keychain[tag] = copy;
    }

    /// <inheritdoc/>
    public bool TryReadKeychainSecret(string tag, out byte[] secretBytes)
    {
        // WHAT: Retrieve defensive copy of secret material if tag exists.
        // WHY:  Prevents external code from mutating the internally stored buffer.
        // HOW:  Validates tag, checks keychain, returns cloned array on match, or empty array on miss.
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        if (_keychain.TryGetValue(tag, out byte[]? stored))
        {
            secretBytes = (byte[])stored.Clone();
            return true;
        }

        secretBytes = Array.Empty<byte>();
        return false;
    }

    /// <inheritdoc/>
    public bool DeleteKeychainSecret(string tag)
    {
        // WHAT: Remove secret from Secure Enclave keychain and zeroize its buffer.
        // WHY:  Ensures deleted secret material cannot linger in process memory.
        // HOW:  Validates tag, removes entry, zeroizes removed array via ZeroMemory, and returns success flag.
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        if (_keychain.TryRemove(tag, out byte[]? removed))
        {
            CryptographicOperations.ZeroMemory(removed);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public void ClearAllSessionSecrets()
    {
        // WHAT: Zeroize and remove all secrets currently held in the bridge.
        // WHY:  Provides complete memory cleanup during session reset or lifecycle pause.
        // HOW:  Iterates over all stored arrays, wipes each via ZeroMemory, and clears the dictionary.
        foreach (byte[] secret in _keychain.Values)
        {
            CryptographicOperations.ZeroMemory(secret);
        }

        _keychain.Clear();
    }
}
