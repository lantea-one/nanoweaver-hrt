using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;

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
public sealed record SystemTelemetry
{
    /// <summary>
    /// The node hostname (e.g., local machine name or container ID).
    /// </summary>
    [Description("The node hostname or container identifier.")]
    [JsonPropertyName("node_name")]
    public required string NodeName { get; init; }

    /// <summary>
    /// Active hardware accelerator backend detected in the environment.
    /// </summary>
    [Description("Active hardware accelerator backend detected (e.g. Intel Arc, NVIDIA CUDA, CPU fallback).")]
    [JsonPropertyName("accelerator_backend")]
    public required string AcceleratorBackend { get; init; }

    /// <summary>
    /// Cumulative number of trajectory frames parsed since node startup.
    /// </summary>
    [Description("Cumulative number of trajectory frames parsed since node startup.")]
    [JsonPropertyName("frames_parsed")]
    public required long FramesParsed { get; init; }

    /// <summary>
    /// Current frames processed per second.
    /// </summary>
    [Description("Current frames processed per second.")]
    [JsonPropertyName("frames_per_second")]
    public required double FramesPerSecond { get; init; }

    /// <summary>
    /// Heap memory currently allocated by the .NET runtime in megabytes.
    /// </summary>
    [Description("Heap memory currently allocated by the .NET runtime in megabytes.")]
    [JsonPropertyName("heap_memory_mb")]
    public required double HeapMemoryMb { get; init; }

    /// <summary>
    /// Total process working set memory in megabytes.
    /// </summary>
    [Description("Total process working set memory in megabytes.")]
    [JsonPropertyName("working_set_mb")]
    public required double WorkingSetMb { get; init; }

    /// <summary>
    /// Total Garbage Collector Generation 0 collections since startup.
    /// </summary>
    [Description("Total Garbage Collector Generation 0 collections since startup.")]
    [JsonPropertyName("gc_gen0_collections")]
    public required int GcGen0Collections { get; init; }

    /// <summary>
    /// Total Garbage Collector Generation 1 collections since startup.
    /// </summary>
    [Description("Total Garbage Collector Generation 1 collections since startup.")]
    [JsonPropertyName("gc_gen1_collections")]
    public required int GcGen1Collections { get; init; }

    /// <summary>
    /// Total Garbage Collector Generation 2 (Full GC) collections since startup.
    /// </summary>
    [Description("Total Garbage Collector Generation 2 (Full GC) collections since startup.")]
    [JsonPropertyName("gc_gen2_collections")]
    public required int GcGen2Collections { get; init; }

    /// <summary>
    /// Service uptime in human-readable duration format.
    /// </summary>
    [Description("Service uptime in human-readable duration format.")]
    [JsonPropertyName("uptime")]
    public required string Uptime { get; init; }
}
