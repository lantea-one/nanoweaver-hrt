namespace WeaverLammpsHrt.TrajectoryParser.Events.Models;

/// <summary>
/// Event emitted when system telemetry metrics (CPU load, memory allocations, parsed frames) are sampled.
/// </summary>
/// <param name="NodeName">The unique hostname or node identifier within the sovereign cluster.</param>
/// <param name="TotalFramesParsed">The cumulative number of atomic trajectory frames parsed since node inception.</param>
/// <param name="AllocatedMemoryBytes">Current total managed memory allocated in bytes.</param>
/// <param name="ServerGcEnabled">Indicates whether high-throughput Server Garbage Collection mode is active.</param>
/// <param name="SimdEngine">The detected SIMD vector instruction set utilized by the coordinate parser.</param>
public sealed record SystemTelemetryUpdatedEvent(
    string NodeName,
    long TotalFramesParsed,
    long AllocatedMemoryBytes,
    bool ServerGcEnabled,
    string SimdEngine) : IWeaverEvent
{
    /// <inheritdoc/>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <inheritdoc/>
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <inheritdoc/>
    public string EventName => "SystemTelemetryUpdated";
}
