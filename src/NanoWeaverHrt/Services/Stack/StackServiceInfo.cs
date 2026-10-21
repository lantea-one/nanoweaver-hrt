using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using NanoWeaverHrt.Swagger;

namespace NanoWeaverHrt.Services.Stack;

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
public sealed record StackServiceInfo : IExamplesProvider<StackServiceInfo>
{
    /// <summary>
    /// Unique service identifier (e.g., 'simulation-engine', 'pg-duckdb', 'trajectory-parser', 'peer-mesh').
    /// </summary>
    [Description("Unique service identifier.")]
    [JsonPropertyName("serviceId")]
    public required string ServiceId { get; init; }

    /// <summary>
    /// Display name of the service for human interface representation.
    /// </summary>
    [Description("Display name of the service.")]
    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    /// <summary>
    /// Architectural role and primary responsibilities of this service.
    /// </summary>
    [Description("Architectural role and primary responsibilities.")]
    [JsonPropertyName("roleDescription")]
    public required string RoleDescription { get; init; }

    /// <summary>
    /// Primary network address, URI, or volume path used to access this service.
    /// </summary>
    [Description("Primary network address, URI, or volume path.")]
    [JsonPropertyName("endpointOrLocation")]
    public required string EndpointOrLocation { get; init; }

    /// <summary>
    /// Current operational health state of the service.
    /// </summary>
    [Description("Current operational health state of the service.")]
    [JsonPropertyName("health")]
    public required ServiceHealthState Health { get; init; }

    /// <summary>
    /// Human-readable operational status message (e.g., 'Online (Intel OpenCL iGPU Ready)').
    /// </summary>
    [Description("Human-readable operational status message.")]
    [JsonPropertyName("statusMessage")]
    public required string StatusMessage { get; init; }

    /// <summary>
    /// Hardware acceleration backend or engine technology powering this service.
    /// </summary>
    [Description("Hardware acceleration backend or engine technology.")]
    [JsonPropertyName("technologyStack")]
    public required string TechnologyStack { get; init; }

    /// <summary>
    /// Collection of key-value metrics or telemetry points specific to this service.
    /// </summary>
    [Description("Collection of key-value metrics or telemetry points.")]
    [JsonPropertyName("metrics")]
    public Dictionary<string, string> Metrics { get; init; } = [];

    /// <summary>
    /// Timestamp when this service status was last sampled.
    /// </summary>
    [Description("Timestamp when this service status was last sampled.")]
    [JsonPropertyName("lastCheckedUtc")]
    public DateTime LastCheckedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Generates a representative example instance of <see cref="StackServiceInfo"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated stack service info example.</returns>
    public StackServiceInfo GetExamples() => new()
    {
        ServiceId = "trajectory-parser",
        DisplayName = ".NET 10 System.IO.Pipelines Parser & Web UI",
        RoleDescription = "High-throughput zero-allocation trajectory ingestion and sovereign Blazor Web UI",
        EndpointOrLocation = "http://127.0.0.1:5000",
        Health = ServiceHealthState.Healthy,
        StatusMessage = "Operational - Intel Arc A770 Discrete GPU Active",
        TechnologyStack = "ASP.NET Core / C# 13 / System.IO.Pipelines / SYCL",
        Metrics = new Dictionary<string, string>
        {
            ["FPS"] = "59.8",
            ["Memory"] = "84.5 MB"
        },
        LastCheckedUtc = new DateTime(2026, 10, 20, 18, 0, 0, DateTimeKind.Utc)
    };
}
