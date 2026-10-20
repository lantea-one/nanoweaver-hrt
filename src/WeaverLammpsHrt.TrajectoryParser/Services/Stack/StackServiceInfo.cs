namespace WeaverLammpsHrt.TrajectoryParser.Services.Stack;

/// <summary>
/// Status health indicator for a service in the sovereign stack.
/// </summary>
public enum ServiceHealthState
{
    /// <summary>
    /// Service is actively running, responsive, and ready for work.
    /// </summary>
    Healthy,

    /// <summary>
    /// Service is operational via local fallback mode (e.g., SQLite instead of full Docker Postgres).
    /// </summary>
    DegradedOrFallback,

    /// <summary>
    /// Service endpoint is unreachable or container is offline.
    /// </summary>
    Offline,

    /// <summary>
    /// Service is currently executing a long-running computational workload or simulation.
    /// </summary>
    Busy
}

/// <summary>
/// Diagnostic telemetry and operational metadata for a component in the sovereign nanotech stack.
/// </summary>
public sealed record StackServiceInfo
{
    /// <summary>
    /// Unique service identifier (e.g., 'simulation-engine', 'pg-duckdb', 'trajectory-parser', 'peer-mesh').
    /// </summary>
    public required string ServiceId { get; init; }

    /// <summary>
    /// Display name of the service for human interface representation.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Architectural role and primary responsibilities of this service.
    /// </summary>
    public required string RoleDescription { get; init; }

    /// <summary>
    /// Primary network address, URI, or volume path used to access this service.
    /// </summary>
    public required string EndpointOrLocation { get; init; }

    /// <summary>
    /// Current operational health state of the service.
    /// </summary>
    public required ServiceHealthState Health { get; init; }

    /// <summary>
    /// Human-readable operational status message (e.g., 'Online (Intel OpenCL iGPU Ready)').
    /// </summary>
    public required string StatusMessage { get; init; }

    /// <summary>
    /// Hardware acceleration backend or engine technology powering this service.
    /// </summary>
    public required string TechnologyStack { get; init; }

    /// <summary>
    /// Collection of key-value metrics or telemetry points specific to this service.
    /// </summary>
    public Dictionary<string, string> Metrics { get; init; } = [];

    /// <summary>
    /// Timestamp when this service status was last sampled.
    /// </summary>
    public DateTime LastCheckedUtc { get; init; } = DateTime.UtcNow;
}
