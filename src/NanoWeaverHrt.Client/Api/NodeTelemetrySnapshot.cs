using System.Text.Json.Serialization;

namespace NanoWeaverHrt.Client.Api;

/// <summary>
/// Telemetry metrics returned by a nanoweaver-hrt node via /api/system/telemetry.
/// </summary>
/// <param name="NodeName">Hostname or container identifier of the node.</param>
/// <param name="AcceleratorBackend">Hardware accelerator backend string.</param>
/// <param name="FramesParsed">Cumulative trajectory frames parsed since node boot.</param>
/// <param name="FramesPerSecond">Current trajectory parsing throughput in frames/s.</param>
/// <param name="HeapMemoryMb">Managed GC heap memory in megabytes.</param>
/// <param name="WorkingSetMb">Process working set memory in megabytes.</param>
/// <param name="GcGen0Collections">Generation 0 GC collections.</param>
/// <param name="GcGen1Collections">Generation 1 GC collections.</param>
/// <param name="GcGen2Collections">Generation 2 (Full GC) collections.</param>
/// <param name="Uptime">Node uptime string.</param>
/// <param name="CpuUtilizationPercent">Estimated or reported CPU utilization (0.0 to 100.0).</param>
/// <param name="GpuUtilizationPercent">Estimated or reported GPU utilization (0.0 to 100.0).</param>
/// <param name="GpuTemperatureCelsius">GPU temperature in degrees Celsius.</param>
/// <param name="ActiveSimulationCount">Count of actively running simulation campaigns.</param>
public sealed record NodeTelemetrySnapshot(
    [property: JsonPropertyName("nodeName")] string NodeName,
    [property: JsonPropertyName("acceleratorBackend")] string AcceleratorBackend,
    [property: JsonPropertyName("framesParsed")] long FramesParsed,
    [property: JsonPropertyName("framesPerSecond")] double FramesPerSecond,
    [property: JsonPropertyName("heapMemoryMb")] double HeapMemoryMb,
    [property: JsonPropertyName("workingSetMb")] double WorkingSetMb,
    [property: JsonPropertyName("gcGen0Collections")] int GcGen0Collections,
    [property: JsonPropertyName("gcGen1Collections")] int GcGen1Collections,
    [property: JsonPropertyName("gcGen2Collections")] int GcGen2Collections,
    [property: JsonPropertyName("uptime")] string Uptime,
    [property: JsonPropertyName("cpuUtilizationPercent")] double CpuUtilizationPercent = 0.0,
    [property: JsonPropertyName("gpuUtilizationPercent")] double GpuUtilizationPercent = 0.0,
    [property: JsonPropertyName("gpuTemperatureCelsius")] double GpuTemperatureCelsius = 0.0,
    [property: JsonPropertyName("activeSimulationCount")] int ActiveSimulationCount = 0);
