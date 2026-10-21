namespace WeaverLammpsHrt.TrajectoryParser.Events.Models;

/// <summary>
/// Event emitted when a batch of molecular trajectory atomic coordinates has been ingested and parsed.
/// </summary>
/// <param name="TrajectoryName">The filename or identifier of the parsed trajectory stream.</param>
/// <param name="FrameIndex">The zero-based frame sequence index within the trajectory file.</param>
/// <param name="AtomCount">The total number of atom coordinates processed in this frame.</param>
/// <param name="ElapsedMs">The elapsed parser execution duration in milliseconds.</param>
public sealed record TrajectoryFrameBatchEvent(
    string TrajectoryName,
    long FrameIndex,
    int AtomCount,
    double ElapsedMs) : IWeaverEvent
{
    /// <inheritdoc/>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <inheritdoc/>
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <inheritdoc/>
    public string EventName => "TrajectoryFrameBatch";
}
