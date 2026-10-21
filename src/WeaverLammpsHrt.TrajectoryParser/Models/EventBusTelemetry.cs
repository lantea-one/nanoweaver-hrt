using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using WeaverLammpsHrt.TrajectoryParser.Swagger;

namespace WeaverLammpsHrt.TrajectoryParser.Models;

/// <summary>
/// Operational telemetry metrics for the in-memory <see cref="Events.IInternalEventBus"/>
/// and the distributed Valkey cluster bridge.
/// </summary>
[SwaggerSchema(Title = "EventBusTelemetry", Description = "Real-time metrics for in-memory event channels and distributed Valkey bridge.")]
public sealed record EventBusTelemetry : IExamplesProvider<EventBusTelemetry>
{
    /// <summary>
    /// Gets a value indicating whether the internal in-memory channel event bus is active.
    /// </summary>
    [Description("Indicates whether the internal in-memory channel event bus is active.")]
    [JsonPropertyName("isInternalBusActive")]
    public required bool IsInternalBusActive { get; init; }

    /// <summary>
    /// Gets a value indicating whether a live multiplexed connection to Valkey is healthy.
    /// </summary>
    [Description("Indicates whether a live multiplexed connection to Valkey is healthy.")]
    [JsonPropertyName("isValkeyConnected")]
    public required bool IsValkeyConnected { get; init; }

    /// <summary>
    /// Gets a value indicating whether the cache layer is distributed (Valkey) or local in-memory fallback.
    /// </summary>
    [Description("Indicates whether caching is distributed across Valkey (true) or operating in local fallback (false).")]
    [JsonPropertyName("isDistributedCache")]
    public required bool IsDistributedCache { get; init; }

    /// <summary>
    /// Gets the sanitized endpoint address of the Valkey cluster.
    /// </summary>
    [Description("Sanitized endpoint descriptor of the active Valkey cluster.")]
    [JsonPropertyName("valkeyEndpoint")]
    public required string ValkeyEndpoint { get; init; }

    /// <summary>
    /// Gets the cumulative count of events dispatched outbound from this node to Valkey.
    /// </summary>
    [Description("Cumulative count of events dispatched outbound from this node to Valkey.")]
    [JsonPropertyName("outboundEventsCount")]
    public required long OutboundEventsCount { get; init; }

    /// <summary>
    /// Gets the cumulative count of events received inbound from the Valkey cluster.
    /// </summary>
    [Description("Cumulative count of events received inbound from the Valkey cluster.")]
    [JsonPropertyName("inboundEventsCount")]
    public required long InboundEventsCount { get; init; }

    /// <summary>
    /// Gets a dictionary mapping event type names to their active subscriber counts.
    /// </summary>
    [Description("Active subscriber listener counts categorized by event type.")]
    [JsonPropertyName("activeSubscribers")]
    public required Dictionary<string, int> ActiveSubscribers { get; init; }

    /// <summary>
    /// Generates an illustrative example instance of <see cref="EventBusTelemetry"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated telemetry snapshot example.</returns>
    public EventBusTelemetry GetExamples() => new()
    {
        IsInternalBusActive = true,
        IsValkeyConnected = true,
        IsDistributedCache = true,
        ValkeyEndpoint = "valkey-node:6379 (authenticated)",
        OutboundEventsCount = 1420,
        InboundEventsCount = 890,
        ActiveSubscribers = new Dictionary<string, int>
        {
            { "TrajectoryFrameBatchEvent", 2 },
            { "SimulationProgressEvent", 1 },
            { "UserSessionActivityEvent", 1 },
            { "AuditLogDispatchedEvent", 1 },
            { "SystemTelemetryUpdatedEvent", 1 }
        }
    };
}
