using System.Threading.Channels;

namespace NanoWeaverHrt.Events;

/// <summary>
/// Configuration options controlling the capacity, concurrency, and backpressure behavior
/// of the in-process <see cref="ChannelEventBus"/>.
/// </summary>
public sealed class EventBusOptions
{
    /// <summary>
    /// Gets or sets the default bounded channel capacity for event queues when an explicit capacity is not specified.
    /// Default is 10,000 items.
    /// </summary>
    public int DefaultCapacity { get; set; } = 10_000;

    /// <summary>
    /// Gets or sets the default backpressure full mode when a channel reaches capacity.
    /// Default is <see cref="BoundedChannelFullMode.DropOldest"/> to prevent parser stalling during high-rate streaming.
    /// </summary>
    public BoundedChannelFullMode DefaultFullMode { get; set; } = BoundedChannelFullMode.DropOldest;

    /// <summary>
    /// Gets or sets whether channel readers and writers should optimize for single-writer concurrency.
    /// Default is false to support multiple concurrent threads and tasks.
    /// </summary>
    public bool SingleWriter { get; set; } = false;

    /// <summary>
    /// Gets or sets whether channel readers should optimize for single-reader concurrency.
    /// Default is false to allow multiple parallel consumer tasks.
    /// </summary>
    public bool SingleReader { get; set; } = false;
}
