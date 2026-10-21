namespace NanoWeaverHrt.Events.Models;

/// <summary>
/// Event emitted when an in-silico simulation campaign (oxDNA, LAMMPS, or PBPK) advances in execution or changes state.
/// </summary>
/// <param name="CampaignId">The unique identifier of the active simulation campaign.</param>
/// <param name="CampaignName">The human-readable title or descriptor of the campaign.</param>
/// <param name="Step">The current step index completed in the simulation loop.</param>
/// <param name="TotalSteps">The total planned steps required for full convergence.</param>
/// <param name="Status">The operational status description (e.g. Initializing, Running, Completed, Failed).</param>
/// <param name="Details">Optional status detail or diagnostic message.</param>
public sealed record SimulationProgressEvent(
    string CampaignId,
    string CampaignName,
    long Step,
    long TotalSteps,
    string Status,
    string? Details = null) : IWeaverEvent
{
    /// <inheritdoc/>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <inheritdoc/>
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <inheritdoc/>
    public string EventName => "SimulationProgress";
}
