namespace NanoWeaverHrt.Events.Models;

/// <summary>
/// Common marker interface for all strongly-typed domain, simulation, and security events
/// dispatched through the sovereign event pipeline.
/// </summary>
public interface IWeaverEvent
{
    /// <summary>
    /// Gets the unique cryptographic identifier for the event instance.
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    /// Gets the UTC timestamp when the event was produced.
    /// </summary>
    DateTimeOffset TimestampUtc { get; }

    /// <summary>
    /// Gets the logical classification name of the event type.
    /// </summary>
    string EventName { get; }
}
