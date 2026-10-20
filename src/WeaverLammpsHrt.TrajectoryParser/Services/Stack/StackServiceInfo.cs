using System.ComponentModel;
using Swashbuckle.AspNetCore.Annotations;

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
[SwaggerSchema(Title = "StackServiceInfo", Description = "Diagnostic telemetry and operational health for a container or component in the sovereign stack.")]
public sealed record StackServiceInfo
{
    /// <summary>
    /// Unique service identifier (e.g., 'simulation-engine', 'pg-duckdb', 'trajectory-parser', 'peer-mesh').
    /// </summary>
    [Description("Unique service identifier.")]
    public required string ServiceId { get; init; }

    /// <summary>
    /// Display name of the service for human interface representation.
    /// </summary>
    [Description("Display name of the service.")]
    public required string DisplayName { get; init; }

    /// <summary>
    /// Architectural role and primary responsibilities of this service.
    /// </summary>
    [Description("Architectural role and primary responsibilities.")]
    public required string RoleDescription { get; init; }

    /// <summary>
    /// Primary network address, URI, or volume path used to access this service.
    /// </summary>
    [Description("Primary network address, URI, or volume path.")]
    public required string EndpointOrLocation { get; init; }

    /// <summary>
    /// Current operational health state of the service.
    /// </summary>
    [Description("Current operational health state of the service.")]
    public required ServiceHealthState Health { get; init; }

    /// <summary>
    /// Human-readable operational status message (e.g., 'Online (Intel OpenCL iGPU Ready)').
    /// </summary>
    [Description("Human-readable operational status message.")]
    public required string StatusMessage { get; init; }

    /// <summary>
    /// Hardware acceleration backend or engine technology powering this service.
    /// </summary>
    [Description("Hardware acceleration backend or engine technology.")]
    public required string TechnologyStack { get; init; }

    /// <summary>
    /// Collection of key-value metrics or telemetry points specific to this service.
    /// </summary>
    [Description("Collection of key-value metrics or telemetry points.")]
    public Dictionary<string, string> Metrics { get; init; } = [];

    /// <summary>
    /// Timestamp when this service status was last sampled.
    /// </summary>
    [Description("Timestamp when this service status was last sampled.")]
    public DateTime LastCheckedUtc { get; init; } = DateTime.UtcNow;
}
