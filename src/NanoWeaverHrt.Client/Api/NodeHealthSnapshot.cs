using System.Text.Json.Serialization;

namespace NanoWeaverHrt.Client.Api;

/// <summary>
/// High-level operational health snapshot returned by a node via /healthz or /api/system/health.
/// </summary>
/// <param name="Status">High-level status string (e.g., Healthy, Degraded, Unhealthy).</param>
/// <param name="NodeName">Node hostname or container identifier.</param>
/// <param name="IsDatabaseConnected">True if the backing database is reachable.</param>
/// <param name="IsStorageHealthy">True if the trajectory storage directory is accessible.</param>
/// <param name="TimestampUtc">Timestamp when health was evaluated.</param>
public sealed record NodeHealthSnapshot(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("nodeName")] string NodeName,
    [property: JsonPropertyName("isDatabaseConnected")] bool IsDatabaseConnected,
    [property: JsonPropertyName("isStorageHealthy")] bool IsStorageHealthy,
    [property: JsonPropertyName("timestampUtc")] DateTimeOffset TimestampUtc);
