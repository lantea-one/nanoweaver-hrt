using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Federation;

/// <summary>
/// Domain service orchestrating SSRF-hardened, ML-DSA-65 pinned automated revocation feed pull-sync
/// against subscribed federated peer NanoWeaverHrt laboratories.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The application-layer orchestrator bridging
/// <see cref="NanoWeaverHrt.Storage.IFederatedPeerSyncRepository"/>,
/// <see cref="NanoWeaverHrt.Storage.IPeerKeyRegistry"/>,
/// <see cref="NanoWeaverHrt.Fabrication.IFabricationLotService"/>, and outbound HTTP transport.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 39 (P206): Automates cross-node propagation of permanent
/// wet-lab recalls while enforcing fail-closed SSRF defense, pinned post-quantum signing keys, and 21 CFR Part 11 auditing.
/// </para>
/// <para>
/// <strong>How it works:</strong> Validates endpoint URLs against scheme and IP-range restrictions, fetches
/// bounded 512 KiB JSON feeds without following redirects, verifies pinned ML-DSA-65 signatures, upserts newly
/// discovered recall tombstones, and records outcome telemetry and audit trail entries.
/// </para>
/// </remarks>
public interface IFederatedPeerSyncService
{
    /// <summary>
    /// Validates a peer endpoint base URL against HTTPS, credential, query/fragment, and DNS/IP SSRF invariants.
    /// </summary>
    /// <param name="baseUrl">The candidate peer node base URL.</param>
    /// <param name="allowLoopbackHttp">Explicit opt-in allowing loopback-only endpoints (including HTTP) for local laboratory testing.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>A deterministic <see cref="PeerEndpointValidationResult"/> with normalized base URL or sanitized rejection reason.</returns>
    Task<PeerEndpointValidationResult> ValidatePeerEndpointUrlAsync(
        string baseUrl,
        bool allowLoopbackHttp,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates and persists a new or updated federated peer revocation pull-sync subscription,
    /// requiring an SSRF-safe base URL and an active pinned ML-DSA-65 key in <c>weaver_peer_keys</c>.
    /// </summary>
    /// <param name="request">The peer subscription configuration payload.</param>
    /// <param name="operatorId">The authenticated administrator username performing the change.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The persisted <see cref="FederatedPeerSyncEndpointRecord"/>.</returns>
    Task<FederatedPeerSyncEndpointRecord> UpsertSubscriptionAsync(
        UpsertFederatedPeerSyncEndpointRequest request,
        string operatorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists persisted peer revocation pull-sync subscriptions.
    /// </summary>
    /// <param name="enabledOnly">When true, returns only enabled subscriptions.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>Ordered list of <see cref="FederatedPeerSyncEndpointRecord"/> entries.</returns>
    Task<IReadOnlyList<FederatedPeerSyncEndpointRecord>> ListSubscriptionsAsync(
        bool enabledOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a configured peer revocation pull-sync subscription and writes an audit event when removed.
    /// </summary>
    /// <param name="peerId">The stable peer subscription identifier to delete.</param>
    /// <param name="operatorId">The authenticated administrator username performing the deletion.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>True when the subscription existed and was deleted; otherwise false.</returns>
    Task<bool> DeleteSubscriptionAsync(
        string peerId,
        string operatorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an SSRF-hardened, pinned ML-DSA-65 pull-sync against a single subscribed peer node.
    /// </summary>
    /// <param name="peerId">The stable peer subscription identifier to pull from.</param>
    /// <param name="operatorId">The operator username or system worker identity initiating the pull.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>A <see cref="FederatedPeerSyncRunResult"/> capturing outcome, imported count, and updated telemetry.</returns>
    Task<FederatedPeerSyncRunResult> SyncPeerAsync(
        string peerId,
        string operatorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes pull-sync across all enabled peer subscriptions (optionally restricted to subscriptions whose polling interval has elapsed).
    /// </summary>
    /// <param name="operatorId">The operator username or system worker identity initiating the sweep.</param>
    /// <param name="dueOnly">When true, skips peers whose <c>SyncIntervalMinutes</c> have not elapsed since <c>LastAttemptUtc</c>.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>An aggregate <see cref="FederatedPeerSyncSweepResult"/> across all attempted peers.</returns>
    Task<FederatedPeerSyncSweepResult> SyncAllEnabledPeersAsync(
        string operatorId,
        bool dueOnly = false,
        CancellationToken cancellationToken = default);
}
