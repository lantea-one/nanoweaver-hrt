using NanoWeaverHrt.Client.Security;
using NanoWeaverHrt.Client.Storage;

namespace NanoWeaverHrt.Client.Api;

/// <summary>
/// Sovereign API client managing an authenticated session with zeroizable in-memory credential storage.
/// </summary>
/// <remarks>
/// // WHAT: Authenticated REST node client wrapping an underlying <see cref="INanoWeaverNodeClient"/> with zeroizable session caching.
/// // WHY:  Fulfills Issue #84 (P81): Replaces persistent plaintext string tokens with zeroizable byte buffers erased on logout or timeout.
/// // HOW:  Caches active session bearer tokens in <see cref="ZeroizableSecretBuffer"/>, provides <see cref="ClearSessionToken"/>, and unrolls tokens on demand.
/// </remarks>
public sealed class WeaverNodeApiClient : INanoWeaverNodeClient, IDisposable
{
    /// <summary>
    /// Underlying REST node client executing network requests and cryptographic challenges.
    /// </summary>
    private readonly INanoWeaverNodeClient _innerClient;

    /// <summary>
    /// Zeroizable secret buffer holding the active session bearer token.
    /// </summary>
    private ZeroizableSecretBuffer? _sessionTokenBuffer;

    /// <summary>
    /// Synchronization object protecting access to the session token buffer.
    /// </summary>
    private readonly object _syncLock = new();

    /// <summary>
    /// Indicates whether this client instance has been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WeaverNodeApiClient"/> class wrapping the specified node client.
    /// </summary>
    /// <param name="innerClient">The underlying REST node client.</param>
    public WeaverNodeApiClient(INanoWeaverNodeClient innerClient)
    {
        // WHAT: Assigns underlying client dependency.
        // WHY:  Permits composition and test double injection.
        // HOW:  Validates non-null parameter and assigns internal field.
        _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WeaverNodeApiClient"/> class using an HTTP client and key ring vault.
    /// </summary>
    /// <param name="httpClient">HTTP client used for REST communications.</param>
    /// <param name="keyRingVault">Key ring vault used for cryptographic envelope unsealing.</param>
    public WeaverNodeApiClient(HttpClient httpClient, IClientKeyRingVault keyRingVault)
        : this(new NanoWeaverNodeClient(httpClient, keyRingVault))
    {
    }

    /// <summary>
    /// Gets a cloned snapshot of the current session token buffer, or <c>null</c> if unauthenticated.
    /// </summary>
    public ZeroizableSecretBuffer? SessionTokenBuffer
    {
        get
        {
            lock (_syncLock)
            {
                if (_sessionTokenBuffer == null || _sessionTokenBuffer.IsDisposed)
                {
                    return null;
                }

                return _sessionTokenBuffer.Clone();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether this client holds an active, un-disposed session token.
    /// </summary>
    public bool HasActiveSession
    {
        get
        {
            lock (_syncLock)
            {
                return _sessionTokenBuffer != null && !_sessionTokenBuffer.IsDisposed && _sessionTokenBuffer.Length > 0;
            }
        }
    }

    /// <summary>
    /// Sets or updates the active session bearer token using a zeroizable secret buffer.
    /// </summary>
    /// <param name="tokenBuffer">The secret buffer containing the bearer token.</param>
    public void SetSessionToken(ZeroizableSecretBuffer tokenBuffer)
    {
        // WHAT: Assigns a zeroizable token buffer to this client, clearing any previously cached token.
        // WHY:  Configures client authentication without retaining plaintext string references on the heap.
        // HOW:  Locks state, disposes old buffer if present, and clones the incoming buffer.
        ArgumentNullException.ThrowIfNull(tokenBuffer);

        lock (_syncLock)
        {
            if (_sessionTokenBuffer != null && !_sessionTokenBuffer.IsDisposed)
            {
                _sessionTokenBuffer.Dispose();
            }

            _sessionTokenBuffer = tokenBuffer.Clone();
        }
    }

    /// <summary>
    /// Sets or updates the active session bearer token using a string secret.
    /// </summary>
    /// <param name="token">Bearer token string to wrap and store.</param>
    public void SetSessionToken(string token)
    {
        // WHAT: Ingests a string bearer token into an internal ZeroizableSecretBuffer.
        // WHY:  Facilitates interoperability with string-based auth responses while enabling zeroization.
        // HOW:  Wraps token with ZeroizableSecretBuffer.FromUtf8 and stores it.
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        using var buffer = ZeroizableSecretBuffer.FromUtf8(token);
        SetSessionToken(buffer);
    }

    /// <summary>
    /// Cryptographically zeroizes and clears the active session token from memory.
    /// </summary>
    public void ClearSessionToken()
    {
        // WHAT: Wipes cached session token bytes and resets the session buffer.
        // WHY:  Fulfills Issue #84 (P81) zero-knowledge credentials hygiene on disconnect or idle timeout.
        // HOW:  Disposes _sessionTokenBuffer and sets field to null under lock.
        lock (_syncLock)
        {
            if (_sessionTokenBuffer != null)
            {
                _sessionTokenBuffer.Dispose();
                _sessionTokenBuffer = null;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<NodeAuthSession> AuthenticateAsync(
        InstanceProfile profile,
        ReadOnlyMemory<char> passphrase,
        CancellationToken ct = default)
    {
        // WHAT: Performs PKI challenge-response handshake and stores the resulting session token in zeroizable memory.
        // WHY:  Ensures single-handshake authentication and zeroizable token lifetime tracking.
        // HOW:  Delegates to _innerClient.AuthenticateAsync, converts token to ZeroizableSecretBuffer, and returns session.
        ThrowIfDisposed();
        NodeAuthSession session = await _innerClient.AuthenticateAsync(profile, passphrase, ct).ConfigureAwait(false);

        using var buffer = ZeroizableSecretBuffer.FromUtf8(session.Token);
        SetSessionToken(buffer);

        return session;
    }

    /// <inheritdoc/>
    public Task<InstanceConnectionSnapshot> PollSnapshotAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Polls target node telemetry snapshot using provided or cached session token.
        // WHY:  Permits polling without callers having to hold or pass raw bearer tokens.
        // HOW:  Resolves effective token, delegates to _innerClient.PollSnapshotAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.PollSnapshotAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<bool> StartSimulationCampaignAsync(
        InstanceProfile profile,
        string bearerToken,
        string simulationType,
        CancellationToken ct = default)
    {
        // WHAT: Triggers simulation campaign on node using provided or cached session token.
        // WHY:  Simplifies campaign dispatch from GUI ViewModels.
        // HOW:  Resolves effective token, delegates to _innerClient.StartSimulationCampaignAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.StartSimulationCampaignAsync(profile, effectiveToken, simulationType, ct);
    }

    /// <inheritdoc/>
    public Task<NodeTelemetrySnapshot> GetTelemetryAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Queries raw telemetry metrics from node using provided or cached session token.
        // WHY:  Provides hardware and GC telemetry metrics to client dashboard.
        // HOW:  Resolves effective token, delegates to _innerClient.GetTelemetryAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetTelemetryAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<SimulationCampaignSummary>> GetCampaignsAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Fetches catalog of supported simulation campaigns.
        // WHY:  Enumerates campaign configurations available on the connected node.
        // HOW:  Resolves effective token, delegates to _innerClient.GetCampaignsAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetCampaignsAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public Task<EndocrineProfileSummary> GetEndocrineProfileAsync(
        InstanceProfile profile,
        string bearerToken,
        CancellationToken ct = default)
    {
        // WHAT: Fetches active endocrine profile from connected node.
        // WHY:  Enables display of hormone levels and target feedback loops.
        // HOW:  Resolves effective token, delegates to _innerClient.GetEndocrineProfileAsync.
        ThrowIfDisposed();
        string effectiveToken = ResolveBearerToken(bearerToken);
        return _innerClient.GetEndocrineProfileAsync(profile, effectiveToken, ct);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // WHAT: Zeroizes cached token and disposes wrapped client if disposable.
        // WHY:  Guarantees credential wiping upon service shutdown.
        // HOW:  Calls ClearSessionToken, disposes inner client if IDisposable, and marks disposed.
        lock (_syncLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_sessionTokenBuffer != null)
            {
                _sessionTokenBuffer.Dispose();
                _sessionTokenBuffer = null;
            }

            if (_innerClient is IDisposable disposableInner)
            {
                disposableInner.Dispose();
            }
        }
    }

    /// <summary>
    /// Resolves the effective bearer token to use for a request.
    /// </summary>
    /// <param name="explicitToken">Explicit token passed to method.</param>
    /// <returns>Non-empty bearer token string.</returns>
    private string ResolveBearerToken(string explicitToken)
    {
        // WHAT: Resolves token from explicit argument or cached session buffer.
        // WHY:  Allows caller to pass explicit token or rely on internal session cache.
        // HOW:  Inspects explicitToken; if non-empty, returns it; otherwise decodes _sessionTokenBuffer.
        if (!string.IsNullOrWhiteSpace(explicitToken))
        {
            return explicitToken;
        }

        lock (_syncLock)
        {
            if (_sessionTokenBuffer != null && !_sessionTokenBuffer.IsDisposed)
            {
                string token = _sessionTokenBuffer.ToUnprotectedString();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    return token;
                }
            }
        }

        throw new InvalidOperationException("No bearer token provided and no active session token is cached.");
    }

    /// <summary>
    /// Throws <see cref="ObjectDisposedException"/> if this client instance has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        // WHAT: Asserts non-disposed state before processing operations.
        // WHY:  Guarantees fail-fast semantics when client is accessed after disposal.
        // HOW:  Checks boolean flag under lock and throws ObjectDisposedException if true.
        lock (_syncLock)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WeaverNodeApiClient));
            }
        }
    }
}
