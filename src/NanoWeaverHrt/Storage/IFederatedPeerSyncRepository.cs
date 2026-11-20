using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Persistence contract for automated federated peer revocation pull-sync endpoint subscriptions
/// stored in <c>weaver_federated_peer_sync_endpoints</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The dialect-neutral data-access interface managing cluster-level peer sync
/// configurations and execution telemetry.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 39 (P205): Isolates non-PHI federation endpoint state from
/// patient-scoped <see cref="IFabricationLotRepository"/> GDPR erasure cascades.
/// </para>
/// <para>
/// <strong>How it works:</strong> Implemented by <see cref="FederatedPeerSyncRepository"/> over
/// <see cref="NanoWeaverHrt.Database.Access.IWeaverDbConnectionFactory"/> across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB.
/// </para>
/// </remarks>
public interface IFederatedPeerSyncRepository
{
    /// <summary>
    /// Creates a new peer revocation pull-sync endpoint subscription or updates an existing subscription's
    /// configuration columns while preserving its historical creation timestamp and sync telemetry.
    /// </summary>
    /// <param name="endpoint">The validated peer endpoint subscription record to persist.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The persisted <see cref="FederatedPeerSyncEndpointRecord"/> reflecting current database state.</returns>
    Task<FederatedPeerSyncEndpointRecord> UpsertEndpointAsync(
        FederatedPeerSyncEndpointRecord endpoint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single peer revocation pull-sync endpoint subscription by its identifier.
    /// </summary>
    /// <param name="peerId">The unique peer subscription identifier.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The matching <see cref="FederatedPeerSyncEndpointRecord"/>, or null when not found.</returns>
    Task<FederatedPeerSyncEndpointRecord?> GetEndpointAsync(
        string peerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists persisted peer revocation pull-sync endpoint subscriptions ordered deterministically by peer identifier.
    /// </summary>
    /// <param name="enabledOnly">When true, returns only enabled subscriptions; otherwise returns all subscriptions.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>Ordered list of persisted <see cref="FederatedPeerSyncEndpointRecord"/> entries.</returns>
    Task<IReadOnlyList<FederatedPeerSyncEndpointRecord>> ListEndpointsAsync(
        bool enabledOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a peer revocation pull-sync endpoint subscription by its identifier.
    /// </summary>
    /// <param name="peerId">The unique peer subscription identifier to delete.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>True if a row was deleted; false if no subscription matched <paramref name="peerId"/>.</returns>
    Task<bool> DeleteEndpointAsync(
        string peerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the execution outcome of a pull-sync attempt, preserving the previous <c>last_success_utc</c>
    /// on failure and incrementing <c>total_imported_count</c> atomically.
    /// </summary>
    /// <param name="peerId">The unique peer subscription identifier.</param>
    /// <param name="attemptedAtUtc">The UTC timestamp when the sync attempt executed.</param>
    /// <param name="succeededAtUtc">The UTC timestamp when the sync succeeded, or null on failure.</param>
    /// <param name="status">The resulting <see cref="FederatedPeerSyncStatus"/>.</param>
    /// <param name="errorMessage">The sanitized failure diagnostic message, or null on success.</param>
    /// <param name="importedCount">The non-negative number of newly discovered recall tombstones imported during this run.</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
    /// <returns>The updated <see cref="FederatedPeerSyncEndpointRecord"/>, or null when the peer subscription does not exist.</returns>
    Task<FederatedPeerSyncEndpointRecord?> RecordSyncOutcomeAsync(
        string peerId,
        DateTimeOffset attemptedAtUtc,
        DateTimeOffset? succeededAtUtc,
        FederatedPeerSyncStatus status,
        string? errorMessage,
        int importedCount,
        CancellationToken cancellationToken = default);
}
