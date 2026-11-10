namespace NanoWeaverHrt.Desktop;

/// <summary>
/// Configuration options for the headless background monitoring daemon.
/// </summary>
/// <remarks>
/// // WHAT: Configuration record controlling background polling cadence and operational parameters.
/// // WHY:  Issue #160 (P157): Replaces hardcoded delay intervals with user-configurable polling cadence.
/// // HOW:  Defines immutable properties with validated defaults.
/// </remarks>
/// <param name="PollingIntervalSeconds">Telemetry and health polling interval in seconds (default is 15 seconds).</param>
public sealed record DesktopDaemonConfig(int PollingIntervalSeconds = 15);
