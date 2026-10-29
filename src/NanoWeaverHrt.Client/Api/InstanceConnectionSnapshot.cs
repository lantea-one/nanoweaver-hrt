namespace NanoWeaverHrt.Client.Api;

/// <summary>
/// Represents a point-in-time telemetry and status snapshot for a single managed instance.
/// </summary>
/// <param name="InstanceId">Unique identifier of the managed instance.</param>
/// <param name="DisplayName">Human-readable name of the instance.</param>
/// <param name="State">Current connection state.</param>
/// <param name="CpuUtilizationPercent">Node CPU utilization percentage (0.0 to 100.0).</param>
/// <param name="GpuUtilizationPercent">Node GPU utilization percentage (0.0 to 100.0).</param>
/// <param name="GpuTemperatureCelsius">GPU core temperature in degrees Celsius.</param>
/// <param name="ActiveSimulationCount">Number of running oxDNA or LAMMPS campaigns.</param>
/// <param name="ParsedFramesPerSecond">Zero-allocation trajectory parser throughput in frames/s.</param>
/// <param name="LastUpdatedUtc">UTC timestamp when this snapshot was captured.</param>
/// <param name="LastErrorTitle">RFC 7807 problem title or safe error summary if faulted; otherwise null.</param>
public sealed record InstanceConnectionSnapshot(
    string InstanceId,
    string DisplayName,
    InstanceConnectionState State,
    double CpuUtilizationPercent,
    double GpuUtilizationPercent,
    double GpuTemperatureCelsius,
    int ActiveSimulationCount,
    double ParsedFramesPerSecond,
    DateTimeOffset LastUpdatedUtc,
    string? LastErrorTitle);
