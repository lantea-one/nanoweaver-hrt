using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using WeaverLammpsHrt.TrajectoryParser.Swagger;

namespace WeaverLammpsHrt.TrajectoryParser.Models;

/// <summary>
/// High-level system operational health status returned to unauthenticated clients and container orchestrators.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A lightweight, unauthenticated health status descriptor suitable for Docker,
/// Kubernetes, and external monitoring probes.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Allows container runtime healthchecks to verify node responsiveness without
/// exposing sensitive internal architecture, memory layout, user details, or cryptographic state.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "SystemHealthOverview", Description = "High-level system health status returned to unauthenticated clients and container healthcheck probes.")]
public record SystemHealthOverview : IExamplesProvider<SystemHealthOverview>
{
    /// <summary>
    /// Gets the overall operational status indicator (e.g. "Healthy", "Degraded", "Unhealthy").
    /// </summary>
    [Description("Overall operational status of the service node.")]
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// Gets the UTC timestamp when the health evaluation was performed.
    /// </summary>
    [Description("UTC timestamp when the health evaluation was performed.")]
    [JsonPropertyName("timestampUtc")]
    public required DateTimeOffset TimestampUtc { get; init; }

    /// <summary>
    /// Gets the application semantic version string.
    /// </summary>
    [Description("Application semantic version string.")]
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>
    /// Gets the human-readable formatted system uptime.
    /// </summary>
    [Description("Human-readable formatted system uptime.")]
    [JsonPropertyName("uptime")]
    public required string Uptime { get; init; }

    /// <summary>
    /// Gets the cumulative uptime in total seconds.
    /// </summary>
    [Description("Cumulative uptime in total seconds.")]
    [JsonPropertyName("uptimeSeconds")]
    public required double UptimeSeconds { get; init; }

    /// <summary>
    /// Gets a value indicating whether first-launch setup is pending.
    /// </summary>
    [Description("Indicates whether first-launch administrator setup is required.")]
    [JsonPropertyName("setupRequired")]
    public required bool SetupRequired { get; init; }

    /// <summary>
    /// Gets a value indicating whether this health payload was evaluated under an authenticated session.
    /// </summary>
    [Description("Indicates whether this response includes authenticated granular subsystem diagnostics.")]
    [JsonPropertyName("authenticatedSession")]
    public required bool AuthenticatedSession { get; init; }

    /// <summary>
    /// Gets an informative diagnostic description or guidance message.
    /// </summary>
    [Description("Informative diagnostic description or guidance message.")]
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>
    /// Generates an illustrative example instance of <see cref="SystemHealthOverview"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated health overview instance.</returns>
    public SystemHealthOverview GetExamples() => new()
    {
        Status = "Healthy",
        TimestampUtc = DateTimeOffset.UtcNow,
        Version = "1.0.0",
        Uptime = "0d 4h 12m 30s",
        UptimeSeconds = 15150.0,
        SetupRequired = false,
        AuthenticatedSession = false,
        Description = "High-level system health status. Authenticate with a valid session token for granular diagnostic subsystem metrics."
    };
}

/// <summary>
/// Granular diagnostic system health report returned when an authenticated session is present.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A comprehensive diagnostic report presenting the operational status of every subsystem,
/// including database migrations, event channels, caching, memory performance, physical storage, and post-quantum compliance.
/// </para>
/// <para>
/// <strong>Why it exists:</strong> Enables authenticated administrators and researchers to inspect internal platform health
/// while maintaining strict zero-disclosure boundaries against unauthenticated external callers.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "GranularSystemHealth", Description = "Granular multi-subsystem diagnostic health report returned to authenticated callers.")]
public sealed record GranularSystemHealth : SystemHealthOverview, IExamplesProvider<GranularSystemHealth>
{
    /// <summary>
    /// Gets identity metadata for the active authenticated user session.
    /// </summary>
    [Description("Identity and permission metadata for the caller session.")]
    [JsonPropertyName("user")]
    public required HealthUserInfo User { get; init; }

    /// <summary>
    /// Gets detailed health metrics across all platform subsystems.
    /// </summary>
    [Description("Detailed health metrics across all platform subsystems.")]
    [JsonPropertyName("subsystems")]
    public required HealthSubsystemsInfo Subsystems { get; init; }

    /// <summary>
    /// Generates an illustrative example instance of <see cref="GranularSystemHealth"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated granular health report example.</returns>
    public new GranularSystemHealth GetExamples() => new()
    {
        Status = "Healthy",
        TimestampUtc = DateTimeOffset.UtcNow,
        Version = "1.0.0",
        Uptime = "0d 4h 12m 30s",
        UptimeSeconds = 15150.0,
        SetupRequired = false,
        AuthenticatedSession = true,
        Description = "Granular subsystem health diagnostics.",
        User = new HealthUserInfo
        {
            Username = "admin",
            Role = "Administrator",
            AccessGate = "ApiWeb"
        },
        Subsystems = new HealthSubsystemsInfo
        {
            Database = new DatabaseHealthInfo
            {
                Status = "Healthy",
                Provider = "PostgreSQL",
                CanConnect = true,
                AppliedMigrationsCount = 2
            },
            EventBus = new EventBusHealthInfo
            {
                Status = "Healthy",
                IsInternalBusActive = true,
                TotalSubscribers = 6,
                ActiveSubscribers = new Dictionary<string, int>
                {
                    { "TrajectoryFrameBatchEvent", 2 },
                    { "SimulationProgressEvent", 1 },
                    { "UserSessionActivityEvent", 1 },
                    { "AuditLogDispatchedEvent", 1 },
                    { "SystemTelemetryUpdatedEvent", 1 }
                },
                IsValkeyBridgeConnected = true,
                ValkeyEndpoint = "valkey:6379,password=[REDACTED]"
            },
            Caching = new CachingHealthInfo
            {
                Status = "Healthy",
                IsDistributed = true,
                Mode = "Distributed (Valkey)"
            },
            Memory = new MemoryHealthInfo
            {
                Status = "Healthy",
                HeapMemoryMb = 48.5,
                WorkingSetMb = 120.0,
                ServerGc = true,
                Gen0Collections = 24,
                Gen1Collections = 2,
                Gen2Collections = 0
            },
            Storage = new StorageHealthInfo
            {
                Status = "Healthy",
                IncomingDirectory = "/workspace/data",
                IncomingWritable = true,
                ExportDirectory = "/workspace/exports",
                ExportWritable = true,
                ParquetDirectory = "/workspace/parquet",
                ParquetWritable = true
            },
            Simulations = new SimulationHealthInfo
            {
                Status = "Healthy",
                AcceleratorBackend = "Intel Arc A770 (Level Zero / SYCL)",
                TotalFramesParsed = 450000,
                AvailableCampaignsCount = 4
            },
            Compliance = new ComplianceHealthInfo
            {
                Status = "Healthy",
                AuditLedgerHealthy = true,
                PqcProvider = "ML-KEM-768 + ML-DSA-65 (NIST FIPS 203/204)"
            }
        }
    };
}

/// <summary>
/// Authenticated session user summary for granular health reports.
/// </summary>
[SwaggerSchema(Title = "HealthUserInfo", Description = "Authenticated user identity and role gating metadata.")]
public sealed record HealthUserInfo
{
    /// <summary>
    /// Gets the authenticated username.
    /// </summary>
    [Description("Authenticated username.")]
    [JsonPropertyName("username")]
    public required string Username { get; init; }

    /// <summary>
    /// Gets the assigned user role.
    /// </summary>
    [Description("Assigned security role.")]
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>
    /// Gets the assigned access boundary gate.
    /// </summary>
    [Description("Assigned transport access gate (Api, Web, or ApiWeb).")]
    [JsonPropertyName("accessGate")]
    public required string AccessGate { get; init; }
}

/// <summary>
/// Subsystem diagnostic health container grouping core architecture modules.
/// </summary>
[SwaggerSchema(Title = "HealthSubsystemsInfo", Description = "Container grouping health metrics across all platform architectural subsystems.")]
public sealed record HealthSubsystemsInfo
{
    /// <summary>
    /// Gets diagnostic health metrics for the database subsystem.
    /// </summary>
    [Description("Database connectivity and migration status.")]
    [JsonPropertyName("database")]
    public required DatabaseHealthInfo Database { get; init; }

    /// <summary>
    /// Gets diagnostic health metrics for the internal event bus and cluster pub/sub bridge.
    /// </summary>
    [Description("Internal event bus and Valkey bridge status.")]
    [JsonPropertyName("eventBus")]
    public required EventBusHealthInfo EventBus { get; init; }

    /// <summary>
    /// Gets diagnostic health metrics for the caching layer.
    /// </summary>
    [Description("Caching layer distribution and operational mode.")]
    [JsonPropertyName("caching")]
    public required CachingHealthInfo Caching { get; init; }

    /// <summary>
    /// Gets diagnostic performance metrics for system memory and Server GC collections.
    /// </summary>
    [Description("Memory consumption and GC collection telemetry.")]
    [JsonPropertyName("memory")]
    public required MemoryHealthInfo Memory { get; init; }

    /// <summary>
    /// Gets diagnostic health metrics for shared physical filesystem storage directories.
    /// </summary>
    [Description("Filesystem storage mount accessibility.")]
    [JsonPropertyName("storage")]
    public required StorageHealthInfo Storage { get; init; }

    /// <summary>
    /// Gets diagnostic health metrics for the simulation computation engine.
    /// </summary>
    [Description("Simulation accelerator backend and parsing status.")]
    [JsonPropertyName("simulations")]
    public required SimulationHealthInfo Simulations { get; init; }

    /// <summary>
    /// Gets diagnostic health metrics for post-quantum regulatory compliance and audit ledgers.
    /// </summary>
    [Description("Regulatory compliance and audit trail integrity status.")]
    [JsonPropertyName("compliance")]
    public required ComplianceHealthInfo Compliance { get; init; }
}

/// <summary>
/// Database subsystem connectivity and migration status.
/// </summary>
[SwaggerSchema(Title = "DatabaseHealthInfo", Description = "Database health and schema migration versioning.")]
public sealed record DatabaseHealthInfo
{
    /// <summary>
    /// Gets the operational status of the database subsystem.
    /// </summary>
    [Description("Operational health status.")]
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// Gets the active database engine dialect (e.g. "PostgreSQL", "SQLite").
    /// </summary>
    [Description("Active database engine dialect.")]
    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    /// <summary>
    /// Gets a value indicating whether database queries succeed.
    /// </summary>
    [Description("Indicates whether database connectivity is verified.")]
    [JsonPropertyName("canConnect")]
    public required bool CanConnect { get; init; }

    /// <summary>
    /// Gets the total number of applied schema migrations.
    /// </summary>
    [Description("Total number of deterministic schema migrations applied.")]
    [JsonPropertyName("appliedMigrationsCount")]
    public required int AppliedMigrationsCount { get; init; }
}

/// <summary>
/// Event bus and Valkey cluster pub/sub bridge health diagnostics.
/// </summary>
[SwaggerSchema(Title = "EventBusHealthInfo", Description = "Event bus and distributed cluster pub/sub bridge health.")]
public sealed record EventBusHealthInfo
{
    /// <summary>
    /// Gets the operational status of the event bus subsystem.
    /// </summary>
    [Description("Operational health status.")]
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// Gets a value indicating whether the in-memory channel event bus is active.
    /// </summary>
    [Description("Indicates whether in-memory channels are active.")]
    [JsonPropertyName("isInternalBusActive")]
    public required bool IsInternalBusActive { get; init; }

    /// <summary>
    /// Gets the cumulative subscriber count across all event types.
    /// </summary>
    [Description("Cumulative active subscriber count.")]
    [JsonPropertyName("totalSubscribers")]
    public required int TotalSubscribers { get; init; }

    /// <summary>
    /// Gets subscriber counts categorized by event contract name.
    /// </summary>
    [Description("Subscriber listener counts by event type.")]
    [JsonPropertyName("activeSubscribers")]
    public required Dictionary<string, int> ActiveSubscribers { get; init; }

    /// <summary>
    /// Gets a value indicating whether the Valkey cluster bridge is connected.
    /// </summary>
    [Description("Indicates whether the Valkey pub/sub bridge is connected.")]
    [JsonPropertyName("isValkeyBridgeConnected")]
    public required bool IsValkeyBridgeConnected { get; init; }

    /// <summary>
    /// Gets the sanitized endpoint descriptor of the active Valkey cluster.
    /// </summary>
    [Description("Sanitized endpoint descriptor with credentials redacted.")]
    [JsonPropertyName("valkeyEndpoint")]
    public required string ValkeyEndpoint { get; init; }
}

/// <summary>
/// Caching layer operational mode and state.
/// </summary>
[SwaggerSchema(Title = "CachingHealthInfo", Description = "Caching subsystem distribution and state.")]
public sealed record CachingHealthInfo
{
    /// <summary>
    /// Gets the operational status of the caching subsystem.
    /// </summary>
    [Description("Operational health status.")]
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// Gets a value indicating whether distributed caching is active.
    /// </summary>
    [Description("Indicates whether caching is distributed across Valkey nodes.")]
    [JsonPropertyName("isDistributed")]
    public required bool IsDistributed { get; init; }

    /// <summary>
    /// Gets a descriptive name of the caching execution mode.
    /// </summary>
    [Description("Descriptive name of caching mode.")]
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }
}

/// <summary>
/// Memory consumption and Server GC collection statistics.
/// </summary>
[SwaggerSchema(Title = "MemoryHealthInfo", Description = "Process memory metrics and GC generation collection counts.")]
public sealed record MemoryHealthInfo
{
    /// <summary>
    /// Gets the operational status of memory management.
    /// </summary>
    [Description("Operational health status.")]
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// Gets the managed heap memory in megabytes.
    /// </summary>
    [Description("Managed heap memory in megabytes.")]
    [JsonPropertyName("heapMemoryMb")]
    public required double HeapMemoryMb { get; init; }

    /// <summary>
    /// Gets the process working set memory in megabytes.
    /// </summary>
    [Description("Process physical working set memory in megabytes.")]
    [JsonPropertyName("workingSetMb")]
    public required double WorkingSetMb { get; init; }

    /// <summary>
    /// Gets a value indicating whether Server GC mode is enabled.
    /// </summary>
    [Description("Indicates whether Server GC mode is active.")]
    [JsonPropertyName("serverGc")]
    public required bool ServerGc { get; init; }

    /// <summary>
    /// Gets the cumulative count of Generation 0 garbage collections.
    /// </summary>
    [Description("Generation 0 GC collections.")]
    [JsonPropertyName("gen0Collections")]
    public required int Gen0Collections { get; init; }

    /// <summary>
    /// Gets the cumulative count of Generation 1 garbage collections.
    /// </summary>
    [Description("Generation 1 GC collections.")]
    [JsonPropertyName("gen1Collections")]
    public required int Gen1Collections { get; init; }

    /// <summary>
    /// Gets the cumulative count of Generation 2 full garbage collections.
    /// </summary>
    [Description("Generation 2 GC collections (must remain 0 during hot trajectory streaming).")]
    [JsonPropertyName("gen2Collections")]
    public required int Gen2Collections { get; init; }
}

/// <summary>
/// Storage mount accessibility and write-permission diagnostics.
/// </summary>
[SwaggerSchema(Title = "StorageHealthInfo", Description = "Shared storage directories accessibility.")]
public sealed record StorageHealthInfo
{
    /// <summary>
    /// Gets the operational status of storage directories.
    /// </summary>
    [Description("Operational health status.")]
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// Gets the incoming trajectory storage directory path.
    /// </summary>
    [Description("Incoming trajectory directory path.")]
    [JsonPropertyName("incomingDirectory")]
    public required string IncomingDirectory { get; init; }

    /// <summary>
    /// Gets a value indicating whether the incoming directory exists and is accessible.
    /// </summary>
    [Description("Indicates whether incoming directory is accessible.")]
    [JsonPropertyName("incomingWritable")]
    public required bool IncomingWritable { get; init; }

    /// <summary>
    /// Gets the dataset exports storage directory path.
    /// </summary>
    [Description("Dataset export directory path.")]
    [JsonPropertyName("exportDirectory")]
    public required string ExportDirectory { get; init; }

    /// <summary>
    /// Gets a value indicating whether the export directory exists and is accessible.
    /// </summary>
    [Description("Indicates whether export directory is accessible.")]
    [JsonPropertyName("exportWritable")]
    public required bool ExportWritable { get; init; }

    /// <summary>
    /// Gets the columnar Parquet storage directory path.
    /// </summary>
    [Description("Parquet columnar directory path.")]
    [JsonPropertyName("parquetDirectory")]
    public required string ParquetDirectory { get; init; }

    /// <summary>
    /// Gets a value indicating whether the parquet directory exists and is accessible.
    /// </summary>
    [Description("Indicates whether parquet directory is accessible.")]
    [JsonPropertyName("parquetWritable")]
    public required bool ParquetWritable { get; init; }
}

/// <summary>
/// Simulation engine and accelerator backend health diagnostics.
/// </summary>
[SwaggerSchema(Title = "SimulationHealthInfo", Description = "Molecular simulation engine and accelerator backend diagnostics.")]
public sealed record SimulationHealthInfo
{
    /// <summary>
    /// Gets the operational status of the simulation subsystem.
    /// </summary>
    [Description("Operational health status.")]
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// Gets the detected hardware accelerator backend (e.g. Intel Arc, NVIDIA CUDA, CPU).
    /// </summary>
    [Description("Hardware accelerator backend.")]
    [JsonPropertyName("acceleratorBackend")]
    public required string AcceleratorBackend { get; init; }

    /// <summary>
    /// Gets the cumulative count of trajectory frames parsed.
    /// </summary>
    [Description("Cumulative trajectory frames parsed.")]
    [JsonPropertyName("totalFramesParsed")]
    public required long TotalFramesParsed { get; init; }

    /// <summary>
    /// Gets the total count of registered in-silico simulation campaigns in the catalog.
    /// </summary>
    [Description("Available in-silico simulation campaigns count.")]
    [JsonPropertyName("availableCampaignsCount")]
    public required int AvailableCampaignsCount { get; init; }
}

/// <summary>
/// Regulatory compliance and cryptographic audit ledger health diagnostics.
/// </summary>
[SwaggerSchema(Title = "ComplianceHealthInfo", Description = "Audit trail integrity and post-quantum cryptographic status.")]
public sealed record ComplianceHealthInfo
{
    /// <summary>
    /// Gets the operational status of the compliance subsystem.
    /// </summary>
    [Description("Operational health status.")]
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// Gets a value indicating whether the audit ledger sequence is verified and tamper-free.
    /// </summary>
    [Description("Indicates whether audit ledger sequence is verified and tamper-free.")]
    [JsonPropertyName("auditLedgerHealthy")]
    public required bool AuditLedgerHealthy { get; init; }

    /// <summary>
    /// Gets the active post-quantum cryptographic algorithms descriptor.
    /// </summary>
    [Description("Active post-quantum cryptographic algorithms descriptor.")]
    [JsonPropertyName("pqcProvider")]
    public required string PqcProvider { get; init; }
}
