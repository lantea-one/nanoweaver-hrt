using System.ComponentModel;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;

namespace NanoWeaverHrt.Hardware.Benchmarking;

/// <summary>
/// Comprehensive qualification and diagnostic benchmark report for a hardware accelerator.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> A benchmark report documents the measured floating-point compute throughput
/// (single-precision FP32 and double-precision FP64 in GFLOPS), memory transfer bandwidth (GB/s), operating temperature,
/// power draw, and overall simulation readiness score of a GPU or accelerator.
/// </para>
/// </remarks>
[SwaggerSchema(Title = "AcceleratorBenchmarkReport", Description = "Benchmark and qualification report for a hardware accelerator.")]
public sealed record AcceleratorBenchmarkReport
{
    /// <summary>
    /// The detected hardware device name (e.g., Intel Arc A770, NVIDIA RTX 4090).
    /// </summary>
    [Description("Detected accelerator hardware device model and manufacturer.")]
    [JsonPropertyName("deviceName")]
    public required string DeviceName { get; init; }

    /// <summary>
    /// The installed accelerator driver or runtime version string.
    /// </summary>
    [Description("Installed accelerator driver or runtime version string.")]
    [JsonPropertyName("driverVersion")]
    public required string DriverVersion { get; init; }

    /// <summary>
    /// Measured single-precision (FP32) floating-point compute throughput in GFLOPS.
    /// </summary>
    [Description("Measured single-precision (FP32) floating-point compute throughput in GFLOPS.")]
    [JsonPropertyName("singlePrecisionGflops")]
    public required double SinglePrecisionGflops { get; init; }

    /// <summary>
    /// Measured double-precision (FP64) floating-point compute throughput in GFLOPS.
    /// </summary>
    [Description("Measured double-precision (FP64) floating-point compute throughput in GFLOPS.")]
    [JsonPropertyName("doublePrecisionGflops")]
    public required double DoublePrecisionGflops { get; init; }

    /// <summary>
    /// Measured memory transfer bandwidth in gigabytes per second (GB/s).
    /// </summary>
    [Description("Measured memory transfer bandwidth in gigabytes per second (GB/s).")]
    [JsonPropertyName("memoryBandwidthGigaBytesPerSecond")]
    public required double MemoryBandwidthGigaBytesPerSecond { get; init; }

    /// <summary>
    /// Current operating temperature of the hardware accelerator in degrees Celsius.
    /// </summary>
    [Description("Current operating temperature of the hardware accelerator in degrees Celsius.")]
    [JsonPropertyName("temperatureCelsius")]
    public required double TemperatureCelsius { get; init; }

    /// <summary>
    /// Current power consumption of the hardware accelerator in watts.
    /// </summary>
    [Description("Current power consumption of the hardware accelerator in watts.")]
    [JsonPropertyName("powerUsageWatts")]
    public required double PowerUsageWatts { get; init; }

    /// <summary>
    /// Overall hardware readiness score for production molecular simulation campaigns.
    /// </summary>
    [Description("Overall hardware readiness score (Go, Marginal, Insufficient).")]
    [JsonPropertyName("readiness")]
    public required HardwareReadinessScore Readiness { get; init; }

    /// <summary>
    /// Actionable operational recommendations and configuration guidance based on benchmark results.
    /// </summary>
    [Description("Actionable operational recommendations and configuration guidance.")]
    [JsonPropertyName("recommendations")]
    public required IReadOnlyList<string> Recommendations { get; init; }

    /// <summary>
    /// Coordinated Universal Time (UTC) timestamp when this benchmark was completed.
    /// </summary>
    [Description("UTC timestamp when the benchmark execution was completed.")]
    [JsonPropertyName("benchmarkTimestampUtc")]
    public required DateTimeOffset BenchmarkTimestampUtc { get; init; }
}
