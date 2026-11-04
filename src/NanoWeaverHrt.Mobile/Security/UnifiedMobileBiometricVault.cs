using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using NanoWeaverHrt.Client.Api;
using NanoWeaverHrt.Client.Storage;
using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Mobile.Security;

/// <summary>
/// Unified biometric vault coordinator for mobile devices managing OS-native biometric providers
/// and secure in-memory caching of decrypted patient DNA regimens with automated lifecycle zeroization.
/// </summary>
/// <remarks>
/// // WHAT: Central mobile biometric coordinator and in-memory cryptographic patient DNA vault.
/// // WHY:  Guarantees that sensitive decrypted genomic regimens are wiped from RAM whenever the app leaves the active foreground state.
/// // HOW:  Resolves OS-native security provider (Android Keystore vs Apple Secure Enclave), caches plaintext regimens, and zeroizes memory on pause/stop.
/// </remarks>
public sealed class UnifiedMobileBiometricVault : IDisposable
{
    /// <summary>
    /// Synchronized lock guarding thread-safe mutations to the in-memory regimen cache.
    /// </summary>
    private readonly object _syncRoot = new();

    /// <summary>
    /// Thread-safe in-memory cache mapping patient identifiers to their active plaintext decrypted DNA regimens.
    /// </summary>
    private readonly Dictionary<string, byte[]> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Active OS-native biometric security provider coordinated by this vault instance.
    /// </summary>
    private readonly IBiometricSecurityProvider _activeProvider;

    /// <summary>
    /// Flag indicating whether this vault instance has been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnifiedMobileBiometricVault"/> class.
    /// </summary>
    /// <param name="activeProvider">Optional explicit biometric security provider; if omitted, automatically resolved based on the host OS.</param>
    /// <param name="isPlatform">Optional platform resolution predicate for cross-platform unit testing.</param>
    public UnifiedMobileBiometricVault(
        IBiometricSecurityProvider? activeProvider = null,
        Func<string, bool>? isPlatform = null)
    {
        // WHAT: Resolve and configure active mobile biometric security provider.
        // WHY:  Guarantees correct hardware crypto module (Android Keystore vs Apple Secure Enclave vs Fallback) is loaded.
        // HOW:  Uses injected provider if specified; otherwise detects platform via predicate or RuntimeInformation.
        _activeProvider = activeProvider ?? ResolveDefaultProvider(isPlatform);
    }

    /// <summary>
    /// Gets the active biometric security provider utilized by this vault instance.
    /// </summary>
    public IBiometricSecurityProvider ActiveProvider => _activeProvider;

    /// <summary>
    /// Gets the number of decrypted patient DNA regimens currently held in volatile memory.
    /// </summary>
    public int CachedCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _cache.Count;
            }
        }
    }

    /// <summary>
    /// Caches a decrypted patient DNA regimen payload in volatile memory.
    /// </summary>
    /// <param name="patientId">Unique patient identifier.</param>
    /// <param name="plaintextPayload">Plaintext payload bytes containing decrypted regimen sequences.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="patientId"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="plaintextPayload"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this vault has already been disposed.</exception>
    public void CacheDecryptedRegimen(string patientId, byte[] plaintextPayload)
    {
        // WHAT: Store decrypted patient DNA regimen in volatile memory with defensive copying.
        // WHY:  Provides fast access during foreground sessions while retaining ownership of buffer zeroization.
        // HOW:  Validates inputs, wipes any existing buffer for this patient, copies bytes, and registers in dictionary.
        ArgumentException.ThrowIfNullOrWhiteSpace(patientId);
        ArgumentNullException.ThrowIfNull(plaintextPayload);

        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_cache.TryGetValue(patientId, out byte[]? existingBuffer))
            {
                CryptographicOperations.ZeroMemory(existingBuffer);
            }

            byte[] copy = new byte[plaintextPayload.Length];
            Buffer.BlockCopy(plaintextPayload, 0, copy, 0, plaintextPayload.Length);
            _cache[patientId] = copy;
        }
    }

    /// <summary>
    /// Attempts to retrieve a decrypted patient DNA regimen from volatile memory.
    /// </summary>
    /// <param name="patientId">Unique patient identifier.</param>
    /// <param name="plaintextPayload">Output parameter receiving a defensive copy of the plaintext payload, or <see langword="null"/> if not cached.</param>
    /// <returns><see langword="true"/> if the regimen was found in memory; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="patientId"/> is null or whitespace.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this vault has already been disposed.</exception>
    public bool TryGetDecryptedRegimen(string patientId, out byte[]? plaintextPayload)
    {
        // WHAT: Retrieve cached decrypted patient regimen.
        // WHY:  Avoids redundant biometric prompts while application remains continuously active in the foreground.
        // HOW:  Validates inputs, checks disposed state, copies cached buffer defensively, and returns retrieval status.
        ArgumentException.ThrowIfNullOrWhiteSpace(patientId);

        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_cache.TryGetValue(patientId, out byte[]? cachedBuffer))
            {
                byte[] copy = new byte[cachedBuffer.Length];
                Buffer.BlockCopy(cachedBuffer, 0, copy, 0, cachedBuffer.Length);
                plaintextPayload = copy;
                return true;
            }

            plaintextPayload = null;
            return false;
        }
    }

    /// <summary>
    /// Downloads and securely caches a patient DNA digital package in volatile memory using an authenticated client.
    /// </summary>
    /// <param name="client">Authenticated REST API client.</param>
    /// <param name="profile">Instance connection profile.</param>
    /// <param name="bearerToken">Active Bearer token.</param>
    /// <param name="patientId">Target patient identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The downloaded patient digital DNA package.</returns>
    public async Task<PatientDigitalDnaPackage> DownloadAndCachePatientPackageAsync(
        INanoWeaverNodeClient client,
        InstanceProfile profile,
        string bearerToken,
        string patientId,
        CancellationToken ct = default)
    {
        // WHAT: Downloads patient digital DNA package and caches its scaffold bytes in volatile memory.
        // WHY:  Permits mobile clients to load genomic regimens into the zeroizable biometric vault on demand.
        // HOW:  Awaits client.GetPatientDnaRegimensAsync, serializes payload to UTF-8 bytes, and stores via CacheDecryptedRegimen.
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(patientId);

        PatientDigitalDnaPackage package = await client.GetPatientDnaRegimensAsync(profile, bearerToken, patientId, ct).ConfigureAwait(false);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(package.ScaffoldFasta);
        CacheDecryptedRegimen(patientId, payloadBytes);
        return package;
    }

    /// <summary>
    /// Dispatches mobile application lifecycle state changes to purge and zeroize volatile memory when backgrounded or paused.
    /// </summary>
    /// <param name="state">Host mobile application lifecycle transition state.</param>
    public void OnAppLifecycleStateChanged(MobileAppLifecycleState state)
    {
        // WHAT: React to OS mobile lifecycle state changes.
        // WHY:  Eliminates plaintext genomic exposure in memory dumps or background snapshots.
        // HOW:  Checks lifecycle state; if paused, backgrounded, stopped, or terminated, immediately zeroizes memory buffers.
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            if (state is MobileAppLifecycleState.Paused
                or MobileAppLifecycleState.Backgrounded
                or MobileAppLifecycleState.Stopped
                or MobileAppLifecycleState.Terminated)
            {
                PurgeAndZeroizeInternal();
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // WHAT: Dispose vault and securely zeroize all cached buffers.
        // WHY:  Ensures no plaintext DNA sequences persist in memory after vault teardown.
        // HOW:  Acquires lock, sets disposed flag, wipes and clears all buffers, and disposes provider if disposable.
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            PurgeAndZeroizeInternal();

            if (_activeProvider is IDisposable disposableProvider)
            {
                disposableProvider.Dispose();
            }
        }
    }

    /// <summary>
    /// Test helper retrieving a direct reference to the internal cached buffer to verify memory zeroization.
    /// </summary>
    /// <param name="patientId">Unique patient identifier.</param>
    /// <returns>Direct byte array reference, or <see langword="null"/> if not found.</returns>
    internal byte[]? GetDirectBufferRefForTesting(string patientId)
    {
        // WHAT: Retrieve direct array reference for testing assertions.
        // WHY:  Permits unit tests to prove CryptographicOperations.ZeroMemory actually zeroed the exact memory slice.
        // HOW:  Looks up dictionary without cloning array.
        lock (_syncRoot)
        {
            return _cache.TryGetValue(patientId, out byte[]? buffer) ? buffer : null;
        }
    }

    /// <summary>
    /// Internal routine zeroizing and removing all cached payload buffers.
    /// </summary>
    private void PurgeAndZeroizeInternal()
    {
        // WHAT: Cryptographically zeroize each cached array and clear dictionary.
        // WHY:  Prevents plaintext cryptographic material from remaining in RAM.
        // HOW:  Calls CryptographicOperations.ZeroMemory on each buffer and clears _cache.
        foreach (byte[] buffer in _cache.Values)
        {
            CryptographicOperations.ZeroMemory(buffer);
        }

        _cache.Clear();
    }

    /// <summary>
    /// Detects host operating system and instantiates the appropriate default biometric security provider.
    /// </summary>
    /// <param name="isPlatform">Optional platform resolution predicate for testing.</param>
    /// <returns>Platform-native or fallback <see cref="IBiometricSecurityProvider"/> instance.</returns>
    private static IBiometricSecurityProvider ResolveDefaultProvider(Func<string, bool>? isPlatform)
    {
        // WHAT: Resolve default biometric security provider according to host OS.
        // WHY:  Dynamically selects Android Keystore, Apple Secure Enclave, or Fallback provider.
        // HOW:  Probes injected predicate or queries RuntimeInformation.
        Func<string, bool> checker = isPlatform ?? (p => RuntimeInformation.IsOSPlatform(OSPlatform.Create(p)) || (p == "OSX" && RuntimeInformation.IsOSPlatform(OSPlatform.OSX)));

        if (checker("Android") || checker("ANDROID"))
        {
            return new AndroidBiometricSecurityProvider(isPlatform);
        }

        if (checker("iOS") || checker("Apple") || checker("Darwin") || checker("OSX"))
        {
            return new AppleSecureEnclaveSecurityProvider(isPlatform);
        }

        return new FallbackBiometricSecurityProvider();
    }
}
