namespace NanoWeaverHrt.Client.Api;

/// <summary>
/// Represents a state-change alert emitted by the multi-instance coordinator.
/// </summary>
/// <param name="AlertId">Unique identifier for the alert event.</param>
/// <param name="InstanceId">Identifier of the source instance.</param>
/// <param name="InstanceDisplayName">Display name of the source instance.</param>
/// <param name="Severity">Alert severity level.</param>
/// <param name="Title">Short notification title.</param>
/// <param name="Message">Plain-English notification description.</param>
/// <param name="OccurredAtUtc">UTC timestamp of the transition.</param>
public sealed record InstanceAlertEvent(
    string AlertId,
    string InstanceId,
    string InstanceDisplayName,
    InstanceAlertSeverity Severity,
    string Title,
    string Message,
    DateTimeOffset OccurredAtUtc);
