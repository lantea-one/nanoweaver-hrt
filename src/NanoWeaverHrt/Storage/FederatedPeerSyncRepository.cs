using System.Data.Common;
using NanoWeaverHrt.Database.Access;
using NanoWeaverHrt.Models;

namespace NanoWeaverHrt.Storage;

/// <summary>
/// Multi-engine relational repository persisting automated federated peer revocation pull-sync subscriptions
/// in <c>weaver_federated_peer_sync_endpoints</c> across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> Concrete implementation of <see cref="IFederatedPeerSyncRepository"/>
/// using parameterized dialect-neutral SQL via <see cref="IWeaverDbConnectionFactory"/>.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 39 (P205): Preserves cluster-level peer sync state and audit
/// telemetry across restarts without coupling to patient-scoped GDPR erasure repositories.
/// </para>
/// <para>
/// <strong>How it works:</strong> Uses explicit <c>{ await using ... }</c> block scopes, ordinal column mapping,
/// dialect-aware boolean/timestamp coercion, and conflict upserts that preserve prior sync telemetry on config edits.
/// </para>
/// </remarks>
public sealed class FederatedPeerSyncRepository : IFederatedPeerSyncRepository
{
    /// <summary>
    /// Ordered column list for <c>weaver_federated_peer_sync_endpoints</c> insert statements.
    /// </summary>
    private static readonly string[] EndpointColumns =
    [
        "peer_id", "display_name", "base_url", "pinned_key_id",
        "enabled", "sync_interval_minutes", "allow_loopback_http",
        "last_attempt_utc", "last_success_utc", "last_status", "last_error",
        "last_imported_count", "total_imported_count", "created_at_utc", "updated_at_utc"
    ];

    /// <summary>
    /// Primary key conflict column list for endpoint upserts.
    /// </summary>
    private static readonly string[] EndpointConflictColumns = ["peer_id"];

    /// <summary>
    /// Configuration-only columns updated on upsert conflict so creation timestamp and sync telemetry are preserved.
    /// </summary>
    private static readonly string[] EndpointUpdateColumns =
    [
        "display_name", "base_url", "pinned_key_id",
        "enabled", "sync_interval_minutes", "allow_loopback_http", "updated_at_utc"
    ];

    /// <summary>
    /// Parameter names corresponding positionally to <see cref="EndpointColumns"/>.
    /// </summary>
    private static readonly string[] EndpointParameterNames =
    [
        "pid", "dname", "burl", "pkid",
        "en", "sint", "alh",
        "lat", "lsuc", "lst", "lerr",
        "lic", "tic", "cat", "uat"
    ];

    /// <summary>
    /// Shared SELECT column projection for reading endpoint rows by ordinal position.
    /// </summary>
    private const string SelectColumnsSql =
        "peer_id, display_name, base_url, pinned_key_id, enabled, sync_interval_minutes, allow_loopback_http, " +
        "last_attempt_utc, last_success_utc, last_status, last_error, last_imported_count, total_imported_count, " +
        "created_at_utc, updated_at_utc";

    /// <summary>
    /// Database connection factory providing dialect-scoped sessions.
    /// </summary>
    private readonly IWeaverDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederatedPeerSyncRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">The multi-engine database connection factory.</param>
    public FederatedPeerSyncRepository(IWeaverDbConnectionFactory connectionFactory)
    {
        // WHAT: Stores the database connection factory after null validation.
        // WHY:  All repository operations open short-lived dialect-aware sessions from this factory.
        // HOW:  Guards against null injection before assigning the readonly field.
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc/>
    public async Task<FederatedPeerSyncEndpointRecord> UpsertEndpointAsync(
        FederatedPeerSyncEndpointRecord endpoint,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Inserts a new peer sync subscription or updates only configuration columns on conflict.
        // WHY:  Editing a peer's URL, label, or interval must never wipe historical sync timestamps or recall totals.
        // HOW:  Executes dialect-generated upsert SQL then re-reads the persisted row for exact state fidelity.
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint.PeerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint.BaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint.PinnedKeyId);
        ArgumentOutOfRangeException.ThrowIfLessThan(endpoint.SyncIntervalMinutes, 1);

        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            string sql = session.Dialect.BuildUpsertSql(
                "weaver_federated_peer_sync_endpoints",
                EndpointColumns,
                EndpointConflictColumns,
                EndpointUpdateColumns,
                EndpointParameterNames);

            await using DbCommand cmd = session.CreateCommand(sql);
            WeaverDbParameters.Add(cmd, "pid", endpoint.PeerId);
            WeaverDbParameters.Add(cmd, "dname", endpoint.DisplayName);
            WeaverDbParameters.Add(cmd, "burl", endpoint.BaseUrl);
            WeaverDbParameters.Add(cmd, "pkid", endpoint.PinnedKeyId);
            WeaverDbParameters.Add(cmd, "en", session.Dialect.BooleanValue(endpoint.Enabled));
            WeaverDbParameters.Add(cmd, "sint", endpoint.SyncIntervalMinutes);
            WeaverDbParameters.Add(cmd, "alh", session.Dialect.BooleanValue(endpoint.AllowLoopbackHttp));
            WeaverDbParameters.Add(cmd, "lat", session.Dialect.TimestampValueOrNull(endpoint.LastAttemptUtc));
            WeaverDbParameters.Add(cmd, "lsuc", session.Dialect.TimestampValueOrNull(endpoint.LastSuccessUtc));
            WeaverDbParameters.Add(cmd, "lst", endpoint.LastStatus.ToString());
            WeaverDbParameters.AddNullable(cmd, "lerr", endpoint.LastError);
            WeaverDbParameters.Add(cmd, "lic", endpoint.LastImportedCount);
            WeaverDbParameters.Add(cmd, "tic", endpoint.TotalImportedCount);
            WeaverDbParameters.Add(cmd, "cat", session.Dialect.TimestampValue(endpoint.CreatedAtUtc));
            WeaverDbParameters.Add(cmd, "uat", session.Dialect.TimestampValue(endpoint.UpdatedAtUtc));

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        FederatedPeerSyncEndpointRecord? persisted = await GetEndpointAsync(endpoint.PeerId, cancellationToken).ConfigureAwait(false);
        // The row was just upserted in the same database immediately above, so it cannot be null.
        return persisted!;
    }

    /// <inheritdoc/>
    public async Task<FederatedPeerSyncEndpointRecord?> GetEndpointAsync(
        string peerId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Queries a single peer revocation pull-sync endpoint subscription by peer_id.
        // WHY:  Supports targeted manual sync runs, configuration edits, and existence verification.
        // HOW:  Executes a parameterized SELECT inside an explicit block scope and maps by ordinal position.
        ArgumentException.ThrowIfNullOrWhiteSpace(peerId);

        FederatedPeerSyncEndpointRecord? result = null;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"SELECT {SelectColumnsSql} FROM weaver_federated_peer_sync_endpoints WHERE peer_id = {session.Dialect.Parameter("pid")}");
            WeaverDbParameters.Add(cmd, "pid", peerId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result = MapEndpoint(reader);
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FederatedPeerSyncEndpointRecord>> ListEndpointsAsync(
        bool enabledOnly = false,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Lists all configured (or only enabled) peer revocation pull-sync subscriptions.
        // WHY:  Drives both the sovereign UI subscription list and the background scheduled sync sweep.
        // HOW:  Optionally applies a dialect-coerced enabled predicate and orders deterministically by peer_id.
        List<FederatedPeerSyncEndpointRecord> results = [];
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            string sql = enabledOnly
                ? $"SELECT {SelectColumnsSql} FROM weaver_federated_peer_sync_endpoints WHERE enabled = {session.Dialect.Parameter("en")} ORDER BY peer_id ASC"
                : $"SELECT {SelectColumnsSql} FROM weaver_federated_peer_sync_endpoints ORDER BY peer_id ASC";

            await using DbCommand cmd = session.CreateCommand(sql);
            if (enabledOnly)
            {
                WeaverDbParameters.Add(cmd, "en", session.Dialect.BooleanValue(true));
            }

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(MapEndpoint(reader));
            }
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteEndpointAsync(
        string peerId,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Deletes a peer revocation pull-sync subscription row by peer_id.
        // WHY:  Allows administrators to decommission peer laboratory sync subscriptions cleanly.
        // HOW:  Executes a parameterized DELETE and returns true iff at least one row was affected.
        ArgumentException.ThrowIfNullOrWhiteSpace(peerId);

        int affected;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = session.CreateCommand(
                $"DELETE FROM weaver_federated_peer_sync_endpoints WHERE peer_id = {session.Dialect.Parameter("pid")}");
            WeaverDbParameters.Add(cmd, "pid", peerId);

            affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return affected > 0;
    }

    /// <inheritdoc/>
    public async Task<FederatedPeerSyncEndpointRecord?> RecordSyncOutcomeAsync(
        string peerId,
        DateTimeOffset attemptedAtUtc,
        DateTimeOffset? succeededAtUtc,
        FederatedPeerSyncStatus status,
        string? errorMessage,
        int importedCount,
        CancellationToken cancellationToken = default)
    {
        // WHAT: Persists sync attempt telemetry back onto the peer endpoint row.
        // WHY:  Tracks health, preserves last_success_utc across transient failures, and accumulates imported recalls.
        // HOW:  Conditionally includes last_success_utc in the parameterized UPDATE and re-reads the updated row.
        ArgumentException.ThrowIfNullOrWhiteSpace(peerId);
        ArgumentOutOfRangeException.ThrowIfNegative(importedCount);

        int affected;
        {
            await using WeaverDbSession session = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            string successClause = succeededAtUtc.HasValue
                ? $"last_success_utc = {session.Dialect.Parameter("lsuc")}, "
                : string.Empty;

            string sql =
                $"UPDATE weaver_federated_peer_sync_endpoints SET " +
                $"last_attempt_utc = {session.Dialect.Parameter("lat")}, " +
                successClause +
                $"last_status = {session.Dialect.Parameter("lst")}, " +
                $"last_error = {session.Dialect.Parameter("lerr")}, " +
                $"last_imported_count = {session.Dialect.Parameter("lic")}, " +
                $"total_imported_count = total_imported_count + {session.Dialect.Parameter("inc")}, " +
                $"updated_at_utc = {session.Dialect.Parameter("uat")} " +
                $"WHERE peer_id = {session.Dialect.Parameter("pid")}";

            await using DbCommand cmd = session.CreateCommand(sql);
            WeaverDbParameters.Add(cmd, "lat", session.Dialect.TimestampValue(attemptedAtUtc));
            if (succeededAtUtc.HasValue)
            {
                WeaverDbParameters.Add(cmd, "lsuc", session.Dialect.TimestampValue(succeededAtUtc.Value));
            }

            WeaverDbParameters.Add(cmd, "lst", status.ToString());
            WeaverDbParameters.AddNullable(cmd, "lerr", errorMessage);
            WeaverDbParameters.Add(cmd, "lic", importedCount);
            WeaverDbParameters.Add(cmd, "inc", importedCount);
            WeaverDbParameters.Add(cmd, "uat", session.Dialect.TimestampValue(attemptedAtUtc));
            WeaverDbParameters.Add(cmd, "pid", peerId);

            affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (affected == 0)
        {
            return null;
        }

        return await GetEndpointAsync(peerId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Maps the current row of a <see cref="DbDataReader"/> positioned on <see cref="SelectColumnsSql"/>
    /// into a <see cref="FederatedPeerSyncEndpointRecord"/>.
    /// </summary>
    /// <param name="reader">The active database data reader positioned on a valid row.</param>
    /// <returns>The mapped <see cref="FederatedPeerSyncEndpointRecord"/>.</returns>
    private static FederatedPeerSyncEndpointRecord MapEndpoint(DbDataReader reader)
    {
        // WHAT: Reads all 15 columns by ordinal index with dialect-neutral boolean and timestamp decoding.
        // WHY:  Avoids string-name lookup overhead and handles SQLite/PostgreSQL/DuckDB/MySQL type differences uniformly.
        // HOW:  Uses WeaverDbValues helpers and Enum.TryParse with Pending fallback for unknown status strings.
        string rawStatus = reader.GetString(9);
        FederatedPeerSyncStatus status = Enum.TryParse(rawStatus, ignoreCase: true, out FederatedPeerSyncStatus parsed)
            ? parsed
            : FederatedPeerSyncStatus.Pending;

        return new FederatedPeerSyncEndpointRecord
        {
            PeerId = reader.GetString(0),
            DisplayName = reader.GetString(1),
            BaseUrl = reader.GetString(2),
            PinnedKeyId = reader.GetString(3),
            Enabled = WeaverDbValues.ReadBoolean(reader, 4),
            SyncIntervalMinutes = Convert.ToInt32(reader.GetValue(5), System.Globalization.CultureInfo.InvariantCulture),
            AllowLoopbackHttp = WeaverDbValues.ReadBoolean(reader, 6),
            LastAttemptUtc = WeaverDbValues.ReadNullableTimestamp(reader, 7),
            LastSuccessUtc = WeaverDbValues.ReadNullableTimestamp(reader, 8),
            LastStatus = status,
            LastError = reader.IsDBNull(10) ? null : reader.GetString(10),
            LastImportedCount = Convert.ToInt32(reader.GetValue(11), System.Globalization.CultureInfo.InvariantCulture),
            TotalImportedCount = Convert.ToInt32(reader.GetValue(12), System.Globalization.CultureInfo.InvariantCulture),
            CreatedAtUtc = WeaverDbValues.ReadTimestamp(reader, 13),
            UpdatedAtUtc = WeaverDbValues.ReadTimestamp(reader, 14)
        };
    }
}
