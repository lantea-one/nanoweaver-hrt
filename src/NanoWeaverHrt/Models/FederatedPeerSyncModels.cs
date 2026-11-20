using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using NanoWeaverHrt.Swagger;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Models;

/// <summary>
/// Execution status of the most recent automated or on-demand federated peer revocation pull-sync attempt.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FederatedPeerSyncStatus
{
    /// <summary>
    /// The peer endpoint subscription has been configured but has not completed a pull-sync attempt yet.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The most recent pull-sync verified the peer ML-DSA-65 signature and updated the federated recall ledger cleanly.
    /// </summary>
    Succeeded = 1,

    /// <summary>
    /// The most recent pull-sync failed SSRF, network transport, pinned-key, or ML-DSA-65 verification checks.
    /// </summary>
    Failed = 2
}

/// <summary>
/// Persistent database record describing a subscribed federated peer laboratory endpoint
/// for automated ML-DSA-65 revocation feed pull-synchronization.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> The cluster-level federation record stored in
/// <c>weaver_federated_peer_sync_endpoints</c> binding a peer laboratory URL to a pinned active ML-DSA-65 key
/// and recording sync telemetry.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Milestone 39 (P205): Eliminates manual JSON copy-paste for cross-node recall
/// propagation while enforcing fail-closed key pinning and SSRF transport hygiene.
/// </para>
/// <para>
/// <strong>How it works:</strong> Persisted across SQLite, PostgreSQL, DuckDB, and MySQL/MariaDB via
/// <see cref="NanoWeaverHrt.Storage.IFederatedPeerSyncRepository"/> without storing any patient PHI.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "FederatedPeerSyncEndpointRecord", Description = "Configured federated peer laboratory endpoint for automated ML-DSA-65 revocation pull-sync.")]
public sealed record FederatedPeerSyncEndpointRecord : IExamplesProvider<FederatedPeerSyncEndpointRecord>
{
    /// <summary>
    /// Gets or initializes the stable ASCII identifier of this federated peer subscription.
    /// </summary>
    [Description("Stable ASCII identifier of this federated peer subscription (1-64 characters).")]
    [JsonPropertyName("peerId")]
    public string PeerId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the human-readable laboratory display name.
    /// </summary>
    [Description("Human-readable laboratory display name.")]
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the normalized HTTPS (or explicitly opted-in loopback HTTP) base URL of the peer node.
    /// </summary>
    [Description("Normalized base URL of the peer NanoWeaverHrt node (no query string, fragment, or credentials).")]
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the pinned active ML-DSA-65 signing key identifier expected from this peer's feed.
    /// </summary>
    [Description("Pinned active ML-DSA-65 key identifier expected on signed feeds from this peer.")]
    [JsonPropertyName("pinnedKeyId")]
    public string PinnedKeyId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes a value indicating whether scheduled background pull-sync is enabled for this peer.
    /// </summary>
    [Description("Whether automated scheduled pull-sync is enabled for this peer.")]
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets or initializes the polling interval in minutes between automated background sync attempts (1-10080).
    /// </summary>
    [Description("Polling interval in minutes between automated background sync attempts (1 to 10080).")]
    [JsonPropertyName("syncIntervalMinutes")]
    public int SyncIntervalMinutes { get; init; } = 15;

    /// <summary>
    /// Gets or initializes a value indicating whether explicit loopback HTTP endpoints are permitted for local lab testing.
    /// </summary>
    [Description("Explicit opt-in permitting loopback HTTP endpoints (127.0.0.1 / ::1 / localhost) for local laboratory testing.")]
    [JsonPropertyName("allowLoopbackHttp")]
    public bool AllowLoopbackHttp { get; init; }

    /// <summary>
    /// Gets or initializes the UTC timestamp of the most recent pull-sync attempt, or null if never attempted.
    /// </summary>
    [Description("UTC timestamp of the most recent pull-sync attempt, or null if never attempted.")]
    [JsonPropertyName("lastAttemptUtc")]
    public DateTimeOffset? LastAttemptUtc { get; init; }

    /// <summary>
    /// Gets or initializes the UTC timestamp of the most recent successful pull-sync, or null if none succeeded.
    /// </summary>
    [Description("UTC timestamp of the most recent successful pull-sync, or null if none succeeded.")]
    [JsonPropertyName("lastSuccessUtc")]
    public DateTimeOffset? LastSuccessUtc { get; init; }

    /// <summary>
    /// Gets or initializes the status of the most recent pull-sync attempt.
    /// </summary>
    [Description("Status of the most recent pull-sync attempt (Pending, Succeeded, or Failed).")]
    [JsonPropertyName("lastStatus")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FederatedPeerSyncStatus LastStatus { get; init; } = FederatedPeerSyncStatus.Pending;

    /// <summary>
    /// Gets or initializes the sanitized diagnostic message from the most recent failed attempt, or null when healthy.
    /// </summary>
    [Description("Sanitized error summary from the most recent failed pull-sync attempt, or null when healthy.")]
    [JsonPropertyName("lastError")]
    public string? LastError { get; init; }

    /// <summary>
    /// Gets or initializes the count of newly discovered recall tombstones imported during the most recent attempt.
    /// </summary>
    [Description("Count of newly discovered recall tombstones imported during the most recent attempt.")]
    [JsonPropertyName("lastImportedCount")]
    public int LastImportedCount { get; init; }

    /// <summary>
    /// Gets or initializes the cumulative count of newly discovered recall tombstones imported from this peer.
    /// </summary>
    [Description("Cumulative count of newly discovered recall tombstones imported from this peer.")]
    [JsonPropertyName("totalImportedCount")]
    public int TotalImportedCount { get; init; }

    /// <summary>
    /// Gets or initializes the UTC timestamp when this peer subscription was created.
    /// </summary>
    [Description("UTC timestamp when this peer subscription was first created.")]
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>
    /// Gets or initializes the UTC timestamp when this peer subscription configuration or telemetry was last updated.
    /// </summary>
    [Description("UTC timestamp when this peer subscription was last updated.")]
    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset UpdatedAtUtc { get; init; }

    /// <inheritdoc/>
    public FederatedPeerSyncEndpointRecord GetExamples() => new()
    {
        PeerId = "clinic-zurich-01",
        DisplayName = "Zurich Sovereign Origami Lab",
        BaseUrl = "https://lab-zurich.lantea.one",
        PinnedKeyId = "peer-mldsa65-zurich-2026",
        Enabled = true,
        SyncIntervalMinutes = 15,
        AllowLoopbackHttp = false,
        LastAttemptUtc = DateTimeOffset.Parse("2026-11-19T12:00:00Z", CultureInfo.InvariantCulture),
        LastSuccessUtc = DateTimeOffset.Parse("2026-11-19T12:00:00Z", CultureInfo.InvariantCulture),
        LastStatus = FederatedPeerSyncStatus.Succeeded,
        LastError = null,
        LastImportedCount = 1,
        TotalImportedCount = 4,
        CreatedAtUtc = DateTimeOffset.Parse("2026-11-19T08:00:00Z", CultureInfo.InvariantCulture),
        UpdatedAtUtc = DateTimeOffset.Parse("2026-11-19T12:00:00Z", CultureInfo.InvariantCulture)
    };
}

/// <summary>
/// Request payload to create or update an automated federated peer revocation pull-sync subscription.
/// </summary>
[SwaggerSchema(Title = "UpsertFederatedPeerSyncEndpointRequest", Description = "Configuration payload to create or update an automated peer revocation pull-sync subscription.")]
public sealed record UpsertFederatedPeerSyncEndpointRequest : IExamplesProvider<UpsertFederatedPeerSyncEndpointRequest>
{
    /// <summary>
    /// Gets or initializes the stable ASCII peer identifier (1-64 characters: letters, digits, '.', '_', '-').
    /// </summary>
    [Description("Stable ASCII peer identifier (1-64 characters: A-Z, a-z, 0-9, '.', '_', '-').")]
    [JsonPropertyName("peerId")]
    public string PeerId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the human-readable laboratory display name.
    /// </summary>
    [Description("Human-readable laboratory display name.")]
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the peer node base URL (HTTPS required unless <see cref="AllowLoopbackHttp"/> is true on loopback).
    /// </summary>
    [Description("Peer node base URL (HTTPS required unless AllowLoopbackHttp is explicitly enabled for loopback).")]
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the pinned active ML-DSA-65 key identifier registered in the local peer key registry.
    /// </summary>
    [Description("Pinned active ML-DSA-65 key identifier registered in the local peer key registry.")]
    [JsonPropertyName("pinnedKeyId")]
    public string PinnedKeyId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes a value indicating whether scheduled background pull-sync is enabled.
    /// </summary>
    [Description("Whether scheduled background pull-sync is enabled for this peer.")]
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets or initializes the polling interval in minutes (1 to 10080).
    /// </summary>
    [Description("Polling interval in minutes (1 to 10080, default 15).")]
    [JsonPropertyName("syncIntervalMinutes")]
    public int SyncIntervalMinutes { get; init; } = 15;

    /// <summary>
    /// Gets or initializes a value indicating whether loopback HTTP is explicitly allowed for local laboratory testing.
    /// </summary>
    [Description("Explicit opt-in allowing loopback HTTP endpoints (127.0.0.1 / ::1 / localhost) for local lab testing.")]
    [JsonPropertyName("allowLoopbackHttp")]
    public bool AllowLoopbackHttp { get; init; }

    /// <inheritdoc/>
    public UpsertFederatedPeerSyncEndpointRequest GetExamples() => new()
    {
        PeerId = "clinic-zurich-01",
        DisplayName = "Zurich Sovereign Origami Lab",
        BaseUrl = "https://lab-zurich.lantea.one",
        PinnedKeyId = "peer-mldsa65-zurich-2026",
        Enabled = true,
        SyncIntervalMinutes = 15,
        AllowLoopbackHttp = false
    };
}

/// <summary>
/// Result of executing a single SSRF-hardened, ML-DSA-65 verified revocation pull-sync against one peer node.
/// </summary>
[SwaggerSchema(Title = "FederatedPeerSyncRunResult", Description = "Outcome of a single federated peer revocation pull-sync execution.")]
public sealed record FederatedPeerSyncRunResult : IExamplesProvider<FederatedPeerSyncRunResult>
{
    /// <summary>
    /// Gets or initializes the peer identifier targeted by this sync run.
    /// </summary>
    [Description("Peer identifier targeted by this sync run.")]
    [JsonPropertyName("peerId")]
    public string PeerId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes a value indicating whether the requested peer subscription exists in the database.
    /// </summary>
    [Description("True when the target peer subscription exists; false when unknown.")]
    [JsonPropertyName("endpointFound")]
    public bool EndpointFound { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether the pull-sync succeeded end-to-end.
    /// </summary>
    [Description("True when SSRF validation, HTTP fetch, pinned-key match, and ML-DSA-65 verification all succeeded.")]
    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; init; }

    /// <summary>
    /// Gets or initializes the number of newly discovered recall tombstones added during this run.
    /// </summary>
    [Description("Number of newly discovered recall tombstones added during this run.")]
    [JsonPropertyName("importedCount")]
    public int ImportedCount { get; init; }

    /// <summary>
    /// Gets or initializes the total number of revocation entries present in the verified upstream feed.
    /// </summary>
    [Description("Total number of revocation entries contained in the verified upstream feed.")]
    [JsonPropertyName("feedEntryCount")]
    public int FeedEntryCount { get; init; }

    /// <summary>
    /// Gets or initializes the sanitized human-readable outcome summary.
    /// </summary>
    [Description("Sanitized human-readable outcome summary.")]
    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the UTC timestamp when this pull-sync run completed.
    /// </summary>
    [Description("UTC timestamp when this pull-sync run completed.")]
    [JsonPropertyName("completedAtUtc")]
    public DateTimeOffset CompletedAtUtc { get; init; }

    /// <summary>
    /// Gets or initializes the updated peer subscription record after persisting telemetry, or null if not found.
    /// </summary>
    [Description("Updated peer subscription record reflecting the latest sync telemetry, or null if not found.")]
    [JsonPropertyName("endpoint")]
    public FederatedPeerSyncEndpointRecord? Endpoint { get; init; }

    /// <inheritdoc/>
    public FederatedPeerSyncRunResult GetExamples() => new()
    {
        PeerId = "clinic-zurich-01",
        EndpointFound = true,
        Succeeded = true,
        ImportedCount = 1,
        FeedEntryCount = 2,
        Summary = "Synchronized 1 new recall tombstone (2 total feed entries) from pinned peer key 'peer-mldsa65-zurich-2026'.",
        CompletedAtUtc = DateTimeOffset.Parse("2026-11-19T12:00:00Z", CultureInfo.InvariantCulture),
        Endpoint = new FederatedPeerSyncEndpointRecord().GetExamples()
    };
}

/// <summary>
/// Aggregate outcome of sweeping all enabled (or due) federated peer revocation subscriptions.
/// </summary>
[SwaggerSchema(Title = "FederatedPeerSyncSweepResult", Description = "Aggregate outcome of sweeping enabled federated peer revocation subscriptions.")]
public sealed record FederatedPeerSyncSweepResult : IExamplesProvider<FederatedPeerSyncSweepResult>
{
    /// <summary>
    /// Gets or initializes the number of peer endpoints attempted during this sweep.
    /// </summary>
    [Description("Number of peer endpoints attempted during this sweep.")]
    [JsonPropertyName("peersAttempted")]
    public int PeersAttempted { get; init; }

    /// <summary>
    /// Gets or initializes the number of peer endpoints that succeeded during this sweep.
    /// </summary>
    [Description("Number of peer endpoints that succeeded during this sweep.")]
    [JsonPropertyName("peersSucceeded")]
    public int PeersSucceeded { get; init; }

    /// <summary>
    /// Gets or initializes the number of peer endpoints that failed during this sweep.
    /// </summary>
    [Description("Number of peer endpoints that failed during this sweep.")]
    [JsonPropertyName("peersFailed")]
    public int PeersFailed { get; init; }

    /// <summary>
    /// Gets or initializes the total count of newly discovered recall tombstones imported across all peers.
    /// </summary>
    [Description("Total count of newly discovered recall tombstones imported across all peers in this sweep.")]
    [JsonPropertyName("totalNewRevocationsImported")]
    public int TotalNewRevocationsImported { get; init; }

    /// <summary>
    /// Gets or initializes the UTC timestamp when this sweep completed.
    /// </summary>
    [Description("UTC timestamp when this sweep completed.")]
    [JsonPropertyName("completedAtUtc")]
    public DateTimeOffset CompletedAtUtc { get; init; }

    /// <summary>
    /// Gets or initializes the per-peer execution outcomes recorded during this sweep.
    /// </summary>
    [Description("Ordered list of per-peer execution outcomes recorded during this sweep.")]
    [JsonPropertyName("results")]
    public IReadOnlyList<FederatedPeerSyncRunResult> Results { get; init; } = [];

    /// <inheritdoc/>
    public FederatedPeerSyncSweepResult GetExamples() => new()
    {
        PeersAttempted = 1,
        PeersSucceeded = 1,
        PeersFailed = 0,
        TotalNewRevocationsImported = 1,
        CompletedAtUtc = DateTimeOffset.Parse("2026-11-19T12:00:00Z", CultureInfo.InvariantCulture),
        Results = [new FederatedPeerSyncRunResult().GetExamples()]
    };
}

/// <summary>
/// Deterministic result of validating a federated peer endpoint URL against SSRF and transport policy.
/// </summary>
[SwaggerSchema(Title = "PeerEndpointValidationResult", Description = "Deterministic SSRF and transport validation outcome for a peer endpoint base URL.")]
public sealed record PeerEndpointValidationResult : IExamplesProvider<PeerEndpointValidationResult>
{
    /// <summary>
    /// Gets or initializes a value indicating whether the peer base URL satisfies all SSRF and scheme invariants.
    /// </summary>
    [Description("True when the URL satisfies all SSRF, scheme, and DNS invariants; otherwise false.")]
    [JsonPropertyName("isValid")]
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets or initializes the normalized canonical base URL without trailing slash when valid, or null when invalid.
    /// </summary>
    [Description("Canonical base URL without trailing slash when valid; null when rejected.")]
    [JsonPropertyName("normalizedBaseUrl")]
    public string? NormalizedBaseUrl { get; init; }

    /// <summary>
    /// Gets or initializes the sanitized rejection reason when invalid, or null when valid.
    /// </summary>
    [Description("Sanitized rejection reason when invalid; null when valid.")]
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <inheritdoc/>
    public PeerEndpointValidationResult GetExamples() => new()
    {
        IsValid = true,
        NormalizedBaseUrl = "https://lab-zurich.lantea.one",
        Error = null
    };
}
