using Microsoft.Extensions.Hosting;

namespace NanoWeaverHrt.Bridges;

/// <summary>
/// Service contract for bridging local in-memory event bus events with a distributed Valkey cluster.
/// Enables multi-node pub/sub broadcasting and federated cluster synchronization.
/// </summary>
public interface IValkeyEventBridge : IHostedService, IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Gets a value indicating whether the bridge is actively broadcasting to and receiving from Valkey.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Gets the count of events forwarded outbound from the local bus to Valkey.
    /// </summary>
    long OutboundEventsCount { get; }

    /// <summary>
    /// Gets the count of events received inbound from the Valkey cluster and republished locally.
    /// </summary>
    long InboundEventsCount { get; }
}
