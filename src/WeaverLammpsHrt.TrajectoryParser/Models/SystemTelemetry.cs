using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;
using WeaverLammpsHrt.TrajectoryParser.Swagger;

namespace WeaverLammpsHrt.TrajectoryParser.Models;

/// <summary>
/// Telemetry metrics capturing hardware accelerator status, memory performance, and GC pressure.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is this:</strong> A live snapshot of system health, reporting how effectively our 
/// zero-allocation pipeline avoids garbage collection while processing trajectories.
/// </para>
/// <para>
/// <strong>Why this design:</strong> If GC collections occur during real-time streaming, latency spikes 
/// can cause packet drops or GPU kernel stalls. Exposing GC Gen 0/1/2 counters allows engineers 
/// to verify that the zero-allocation invariant is being maintained.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "SystemTelemetry", Description = "Live snapshot of system health, hardware acceleration, memory footprint, and GC collections.")]
public sealed record SystemTelemetry : IExamplesProvider<SystemTelemetry>
{
    /// <summary>
    /// The node hostname (e.g., local machine name or container ID).
    /// </summary>
    [Description("The node hostname or container identifier.")]
    [JsonPropertyName("nodeName")]
    public required string NodeName { get; init; }

    /// <summary>
    /// Active hardware accelerator backend detected in the environment.
    /// </summary>
    [Description("Active hardware accelerator backend detected (e.g. Intel Arc, NVIDIA CUDA, CPU fallback).")]
    [JsonPropertyName("acceleratorBackend")]
    public required string AcceleratorBackend { get; init; }

    /// <summary>
    /// Cumulative number of trajectory frames parsed since node startup.
    /// </summary>
    [Description("Cumulative number of trajectory frames parsed since node startup.")]
    [JsonPropertyName("framesParsed")]
    public required long FramesParsed { get; init; }

    /// <summary>
    /// Current frames processed per second.
    /// </summary>
    [Description("Current frames processed per second.")]
    [JsonPropertyName("framesPerSecond")]
    public required double FramesPerSecond { get; init; }

    /// <summary>
    /// Heap memory currently allocated by the .NET runtime in megabytes.
    /// </summary>
    [Description("Heap memory currently allocated by the .NET runtime in megabytes.")]
    [JsonPropertyName("heapMemoryMb")]
    public required double HeapMemoryMb { get; init; }

    /// <summary>
    /// Total process working set memory in megabytes.
    /// </summary>
    [Description("Total process working set memory in megabytes.")]
    [JsonPropertyName("workingSetMb")]
    public required double WorkingSetMb { get; init; }

    /// <summary>
    /// Total Garbage Collector Generation 0 collections since startup.
    /// </summary>
    [Description("Total Garbage Collector Generation 0 collections since startup.")]
    [JsonPropertyName("gcGen0Collections")]
    public required int GcGen0Collections { get; init; }

    /// <summary>
    /// Total Garbage Collector Generation 1 collections since startup.
    /// </summary>
    [Description("Total Garbage Collector Generation 1 collections since startup.")]
    [JsonPropertyName("gcGen1Collections")]
    public required int GcGen1Collections { get; init; }

    /// <summary>
    /// Total Garbage Collector Generation 2 (Full GC) collections since startup.
    /// </summary>
    [Description("Total Garbage Collector Generation 2 (Full GC) collections since startup.")]
    [JsonPropertyName("gcGen2Collections")]
    public required int GcGen2Collections { get; init; }

    /// <summary>
    /// Service uptime in human-readable duration format.
    /// </summary>
    [Description("Service uptime in human-readable duration format.")]
    [JsonPropertyName("uptime")]
    public required string Uptime { get; init; }

    /// <summary>
    /// Generates a representative example instance of <see cref="SystemTelemetry"/> for OpenAPI documentation.
    /// </summary>
    /// <returns>A populated telemetry snapshot example.</returns>
    public SystemTelemetry GetExamples() => new()
    {
        NodeName = "weaver-arc-node-01",
        AcceleratorBackend = "Intel Arc A770 Discrete GPU (OpenCL / SYCL)",
        FramesParsed = 125000,
        FramesPerSecond = 59.8,
        HeapMemoryMb = 84.5,
        WorkingSetMb = 142.1,
        GcGen0Collections = 0,
        GcGen1Collections = 0,
        GcGen2Collections = 0,
        Uptime = "04:15:32"
    };
}
