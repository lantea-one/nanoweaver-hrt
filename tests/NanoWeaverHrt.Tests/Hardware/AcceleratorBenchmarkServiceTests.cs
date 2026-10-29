using Moq;
using NanoWeaverHrt.Hardware.Benchmarking;
using Xunit;

namespace NanoWeaverHrt.Tests.Hardware;

/// <summary>
/// Unit test suite for the hardware accelerator qualification and diagnostic benchmark engine.
/// </summary>
public sealed class AcceleratorBenchmarkServiceTests : IDisposable
{
    /// <summary>
    /// Preserved original value of ACCELERATOR_DEVICE_NAME before test execution.
    /// </summary>
    private readonly string? _originalDeviceName;

    /// <summary>
    /// Preserved original value of ACCELERATOR_BACKEND before test execution.
    /// </summary>
    private readonly string? _originalBackend;

    /// <summary>
    /// Preserved original value of ACCELERATOR_DRIVER_VERSION before test execution.
    /// </summary>
    private readonly string? _originalDriverVersion;

    /// <summary>
    /// Preserved original value of ACCELERATOR_TEMPERATURE_C before test execution.
    /// </summary>
    private readonly string? _originalTemperature;

    /// <summary>
    /// Preserved original value of ACCELERATOR_POWER_WATTS before test execution.
    /// </summary>
    private readonly string? _originalPower;

    /// <summary>
    /// Initializes a new instance of the <see cref="AcceleratorBenchmarkServiceTests"/> class, snapshotting environment variables.
    /// </summary>
    public AcceleratorBenchmarkServiceTests()
    {
        _originalDeviceName = Environment.GetEnvironmentVariable("ACCELERATOR_DEVICE_NAME");
        _originalBackend = Environment.GetEnvironmentVariable("ACCELERATOR_BACKEND");
        _originalDriverVersion = Environment.GetEnvironmentVariable("ACCELERATOR_DRIVER_VERSION");
        _originalTemperature = Environment.GetEnvironmentVariable("ACCELERATOR_TEMPERATURE_C");
        _originalPower = Environment.GetEnvironmentVariable("ACCELERATOR_POWER_WATTS");
    }

    /// <summary>
    /// Restores environment variables to their pre-test state.
    /// </summary>
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ACCELERATOR_DEVICE_NAME", _originalDeviceName);
        Environment.SetEnvironmentVariable("ACCELERATOR_BACKEND", _originalBackend);
        Environment.SetEnvironmentVariable("ACCELERATOR_DRIVER_VERSION", _originalDriverVersion);
        Environment.SetEnvironmentVariable("ACCELERATOR_TEMPERATURE_C", _originalTemperature);
        Environment.SetEnvironmentVariable("ACCELERATOR_POWER_WATTS", _originalPower);
    }

    /// <summary>
    /// Verifies that <see cref="HardwareReadinessScore"/> enum values correspond to defined integer codes.
    /// </summary>
    [Fact]
    public void HardwareReadinessScore_EnumValues_MatchExpectedValues()
    {
        Assert.Equal(0, (int)HardwareReadinessScore.Go);
        Assert.Equal(1, (int)HardwareReadinessScore.Marginal);
        Assert.Equal(2, (int)HardwareReadinessScore.Insufficient);
    }

    /// <summary>
    /// Verifies that <see cref="AcceleratorBenchmarkReport"/> records store and expose all properties accurately.
    /// </summary>
    [Fact]
    public void AcceleratorBenchmarkReport_Properties_Roundtrip()
    {
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        List<string> recommendations = new() { "Optimal configuration" };

        AcceleratorBenchmarkReport report = new()
        {
            DeviceName = "Test GPU",
            DriverVersion = "1.0.0",
            SinglePrecisionGflops = 6000.0,
            DoublePrecisionGflops = 1200.0,
            MemoryBandwidthGigaBytesPerSecond = 200.0,
            TemperatureCelsius = 52.0,
            PowerUsageWatts = 120.0,
            Readiness = HardwareReadinessScore.Go,
            Recommendations = recommendations,
            BenchmarkTimestampUtc = timestamp
        };

        Assert.Equal("Test GPU", report.DeviceName);
        Assert.Equal("1.0.0", report.DriverVersion);
        Assert.Equal(6000.0, report.SinglePrecisionGflops);
        Assert.Equal(1200.0, report.DoublePrecisionGflops);
        Assert.Equal(200.0, report.MemoryBandwidthGigaBytesPerSecond);
        Assert.Equal(52.0, report.TemperatureCelsius);
        Assert.Equal(120.0, report.PowerUsageWatts);
        Assert.Equal(HardwareReadinessScore.Go, report.Readiness);
        Assert.Same(recommendations, report.Recommendations);
        Assert.Equal(timestamp, report.BenchmarkTimestampUtc);
    }

    /// <summary>
    /// Verifies that <see cref="DefaultAcceleratorTelemetryProvider"/> parses environment variables when present.
    /// </summary>
    [Fact]
    public void DefaultAcceleratorTelemetryProvider_ReadsEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("ACCELERATOR_DEVICE_NAME", "NVIDIA RTX 4090");
        Environment.SetEnvironmentVariable("ACCELERATOR_DRIVER_VERSION", "535.129.03");
        Environment.SetEnvironmentVariable("ACCELERATOR_TEMPERATURE_C", "58.4");
        Environment.SetEnvironmentVariable("ACCELERATOR_POWER_WATTS", "285.0");

        DefaultAcceleratorTelemetryProvider provider = new();

        Assert.Equal("NVIDIA RTX 4090", provider.DeviceName);
        Assert.Equal("535.129.03", provider.DriverVersion);
        Assert.Equal(58.4, provider.TemperatureCelsius);
        Assert.Equal(285.0, provider.PowerUsageWatts);
    }

    /// <summary>
    /// Verifies that <see cref="DefaultAcceleratorTelemetryProvider"/> falls back to default values when environment is cleared.
    /// </summary>
    [Fact]
    public void DefaultAcceleratorTelemetryProvider_FallsBackToDefaults_WhenEnvironmentEmpty()
    {
        Environment.SetEnvironmentVariable("ACCELERATOR_DEVICE_NAME", null);
        Environment.SetEnvironmentVariable("ACCELERATOR_BACKEND", null);
        Environment.SetEnvironmentVariable("ACCELERATOR_DRIVER_VERSION", null);
        Environment.SetEnvironmentVariable("ACCELERATOR_TEMPERATURE_C", null);
        Environment.SetEnvironmentVariable("ACCELERATOR_POWER_WATTS", null);

        DefaultAcceleratorTelemetryProvider provider = new();

        Assert.Equal("Intel Arc A770 Discrete GPU (OpenCL / SYCL)", provider.DeviceName);
        Assert.Equal("31.0.101.4952", provider.DriverVersion);
        Assert.Equal(48.5, provider.TemperatureCelsius);
        Assert.Equal(75.0, provider.PowerUsageWatts);
    }

    /// <summary>
    /// Verifies that <see cref="DefaultAcceleratorTelemetryProvider"/> falls back to defaults when numeric parsing fails.
    /// </summary>
    [Fact]
    public void DefaultAcceleratorTelemetryProvider_HandlesInvalidNumericEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("ACCELERATOR_TEMPERATURE_C", "not_a_number");
        Environment.SetEnvironmentVariable("ACCELERATOR_POWER_WATTS", "invalid_watts");

        DefaultAcceleratorTelemetryProvider provider = new();

        Assert.Equal(48.5, provider.TemperatureCelsius);
        Assert.Equal(75.0, provider.PowerUsageWatts);
    }

    /// <summary>
    /// Verifies that <see cref="DefaultAcceleratorTelemetryProvider"/> checks ACCELERATOR_BACKEND when ACCELERATOR_DEVICE_NAME is unset.
    /// </summary>
    [Fact]
    public void DefaultAcceleratorTelemetryProvider_DeviceName_FallsBackToBackendEnvVar()
    {
        Environment.SetEnvironmentVariable("ACCELERATOR_DEVICE_NAME", null);
        Environment.SetEnvironmentVariable("ACCELERATOR_BACKEND", "AMD Radeon RX 7900 XTX (ROCm)");

        DefaultAcceleratorTelemetryProvider provider = new();

        Assert.Equal("AMD Radeon RX 7900 XTX (ROCm)", provider.DeviceName);
    }

    /// <summary>
    /// Verifies that <see cref="AcceleratorBenchmarkService"/> constructors initialize dependencies properly.
    /// </summary>
    [Fact]
    public void AcceleratorBenchmarkService_Constructors_InitializeCorrectly()
    {
        AcceleratorBenchmarkService defaultService = new();
        Assert.NotNull(defaultService);

        Mock<IAcceleratorTelemetryProvider> mockProvider = new();
        AcceleratorBenchmarkService injectedService = new(mockProvider.Object);
        Assert.NotNull(injectedService);

        AcceleratorBenchmarkService nullService = new(null);
        Assert.NotNull(nullService);
    }

    /// <summary>
    /// Verifies that real hardware compute loops execute using rented ArrayPool buffers and return valid reports.
    /// </summary>
    [Fact]
    public async Task RunBenchmarkAsync_ExecutesRealHardwareCompute_UsingArrayPool()
    {
        AcceleratorBenchmarkService service = new();

        AcceleratorBenchmarkReport report = await service.RunBenchmarkAsync(CancellationToken.None);

        Assert.NotNull(report);
        Assert.False(string.IsNullOrWhiteSpace(report.DeviceName));
        Assert.False(string.IsNullOrWhiteSpace(report.DriverVersion));
        Assert.True(report.SinglePrecisionGflops > 0.0);
        Assert.True(report.DoublePrecisionGflops > 0.0);
        Assert.True(report.MemoryBandwidthGigaBytesPerSecond > 0.0);
        Assert.True(report.TemperatureCelsius > 0.0);
        Assert.True(report.PowerUsageWatts > 0.0);
        Assert.NotEmpty(report.Recommendations);
        Assert.True(report.BenchmarkTimestampUtc <= DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Verifies that <see cref="AcceleratorBenchmarkService.RunBenchmarkAsync"/> throws when cancellation token is pre-canceled.
    /// </summary>
    [Fact]
    public async Task RunBenchmarkAsync_ThrowsOperationCanceledException_WhenTokenAlreadyCanceled()
    {
        AcceleratorBenchmarkService service = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.RunBenchmarkAsync(cts.Token));
    }

    /// <summary>
    /// Verifies that readiness score Go is evaluated when both FP32 and bandwidth meet or exceed thresholds.
    /// </summary>
    [Fact]
    public async Task RunBenchmarkAsync_EvaluatesGoReadiness_WhenThresholdsMet()
    {
        Mock<IAcceleratorTelemetryProvider> mockTelemetry = new();
        mockTelemetry.SetupGet(p => p.DeviceName).Returns("Intel Arc A770 Discrete GPU");
        mockTelemetry.SetupGet(p => p.DriverVersion).Returns("31.0.101.4952");
        mockTelemetry.SetupGet(p => p.TemperatureCelsius).Returns(50.0);
        mockTelemetry.SetupGet(p => p.PowerUsageWatts).Returns(150.0);

        AcceleratorBenchmarkService service = new(
            mockTelemetry.Object,
            () => (Fp32Gflops: 5200.0, Fp64Gflops: 1300.0, BandwidthGbps: 180.0));

        AcceleratorBenchmarkReport report = await service.RunBenchmarkAsync();

        Assert.Equal(HardwareReadinessScore.Go, report.Readiness);
        Assert.Contains(report.Recommendations, r => r.Contains("optimal for production"));
    }

    /// <summary>
    /// Verifies that readiness score Marginal is evaluated when only FP32 meets the marginal threshold.
    /// </summary>
    [Fact]
    public async Task RunBenchmarkAsync_EvaluatesMarginalReadiness_ViaGflopsThreshold()
    {
        Mock<IAcceleratorTelemetryProvider> mockTelemetry = new();
        mockTelemetry.SetupGet(p => p.DeviceName).Returns("Mid-tier GPU");
        mockTelemetry.SetupGet(p => p.DriverVersion).Returns("1.0.0");
        mockTelemetry.SetupGet(p => p.TemperatureCelsius).Returns(60.0);
        mockTelemetry.SetupGet(p => p.PowerUsageWatts).Returns(100.0);

        AcceleratorBenchmarkService service = new(
            mockTelemetry.Object,
            () => (Fp32Gflops: 2500.0, Fp64Gflops: 600.0, BandwidthGbps: 30.0));

        AcceleratorBenchmarkReport report = await service.RunBenchmarkAsync();

        Assert.Equal(HardwareReadinessScore.Marginal, report.Readiness);
        Assert.Contains(report.Recommendations, r => r.Contains("marginal: suitable for low-atom"));
    }

    /// <summary>
    /// Verifies that readiness score Marginal is evaluated when only bandwidth meets the marginal threshold.
    /// </summary>
    [Fact]
    public async Task RunBenchmarkAsync_EvaluatesMarginalReadiness_ViaBandwidthThreshold()
    {
        Mock<IAcceleratorTelemetryProvider> mockTelemetry = new();
        mockTelemetry.SetupGet(p => p.DeviceName).Returns("High-Bandwidth Accelerator");
        mockTelemetry.SetupGet(p => p.DriverVersion).Returns("1.0.0");
        mockTelemetry.SetupGet(p => p.TemperatureCelsius).Returns(60.0);
        mockTelemetry.SetupGet(p => p.PowerUsageWatts).Returns(100.0);

        AcceleratorBenchmarkService service = new(
            mockTelemetry.Object,
            () => (Fp32Gflops: 1000.0, Fp64Gflops: 250.0, BandwidthGbps: 80.0));

        AcceleratorBenchmarkReport report = await service.RunBenchmarkAsync();

        Assert.Equal(HardwareReadinessScore.Marginal, report.Readiness);
    }

    /// <summary>
    /// Verifies that readiness score Marginal is evaluated when both metrics exceed marginal threshold but do not meet Go.
    /// </summary>
    [Fact]
    public async Task RunBenchmarkAsync_EvaluatesMarginalReadiness_WhenBothMarginal()
    {
        Mock<IAcceleratorTelemetryProvider> mockTelemetry = new();
        mockTelemetry.SetupGet(p => p.DeviceName).Returns("Balanced Marginal Device");
        mockTelemetry.SetupGet(p => p.DriverVersion).Returns("1.0.0");
        mockTelemetry.SetupGet(p => p.TemperatureCelsius).Returns(60.0);
        mockTelemetry.SetupGet(p => p.PowerUsageWatts).Returns(100.0);

        AcceleratorBenchmarkService service = new(
            mockTelemetry.Object,
            () => (Fp32Gflops: 2000.0, Fp64Gflops: 500.0, BandwidthGbps: 80.0));

        AcceleratorBenchmarkReport report = await service.RunBenchmarkAsync();

        Assert.Equal(HardwareReadinessScore.Marginal, report.Readiness);
    }

    /// <summary>
    /// Verifies that readiness score Insufficient is evaluated when both compute and bandwidth fall below marginal limits.
    /// </summary>
    [Fact]
    public async Task RunBenchmarkAsync_EvaluatesInsufficientReadiness_WhenBelowBothThresholds()
    {
        Mock<IAcceleratorTelemetryProvider> mockTelemetry = new();
        mockTelemetry.SetupGet(p => p.DeviceName).Returns("Legacy iGPU");
        mockTelemetry.SetupGet(p => p.DriverVersion).Returns("0.1.0");
        mockTelemetry.SetupGet(p => p.TemperatureCelsius).Returns(45.0);
        mockTelemetry.SetupGet(p => p.PowerUsageWatts).Returns(35.0);

        AcceleratorBenchmarkService service = new(
            mockTelemetry.Object,
            () => (Fp32Gflops: 400.0, Fp64Gflops: 100.0, BandwidthGbps: 20.0));

        AcceleratorBenchmarkReport report = await service.RunBenchmarkAsync();

        Assert.Equal(HardwareReadinessScore.Insufficient, report.Readiness);
        Assert.Contains(report.Recommendations, r => r.Contains("insufficient: compute throughput"));
    }

    /// <summary>
    /// Verifies all boundary conditions of <see cref="AcceleratorBenchmarkService.DetermineReadinessScore"/>.
    /// </summary>
    [Fact]
    public void DetermineReadinessScore_ValidatesAllBoundaryConditions()
    {
        // Go boundary: exactly at thresholds
        Assert.Equal(HardwareReadinessScore.Go, AcceleratorBenchmarkService.DetermineReadinessScore(5000.0, 150.0));
        Assert.Equal(HardwareReadinessScore.Go, AcceleratorBenchmarkService.DetermineReadinessScore(6000.0, 200.0));

        // Marginal boundary: FP32 passes Go, but bandwidth is between Marginal and Go
        Assert.Equal(HardwareReadinessScore.Marginal, AcceleratorBenchmarkService.DetermineReadinessScore(5000.0, 149.9));

        // Marginal boundary: bandwidth passes Go, but FP32 is between Marginal and Go
        Assert.Equal(HardwareReadinessScore.Marginal, AcceleratorBenchmarkService.DetermineReadinessScore(4999.9, 150.0));

        // Marginal boundary: exactly at 1500 FP32, bandwidth below 50
        Assert.Equal(HardwareReadinessScore.Marginal, AcceleratorBenchmarkService.DetermineReadinessScore(1500.0, 49.9));

        // Marginal boundary: exactly at 50 GB/s bandwidth, FP32 below 1500
        Assert.Equal(HardwareReadinessScore.Marginal, AcceleratorBenchmarkService.DetermineReadinessScore(1499.9, 50.0));

        // Insufficient: just below both thresholds
        Assert.Equal(HardwareReadinessScore.Insufficient, AcceleratorBenchmarkService.DetermineReadinessScore(1499.9, 49.9));
        Assert.Equal(HardwareReadinessScore.Insufficient, AcceleratorBenchmarkService.DetermineReadinessScore(0.0, 0.0));
    }

    /// <summary>
    /// Verifies recommendation texts generated for Go, Marginal, and Insufficient hardware tiers.
    /// </summary>
    [Fact]
    public void GenerateRecommendations_GeneratesCorrectRecommendations_ForAllReadinessScores()
    {
        IReadOnlyList<string> goRecs = AcceleratorBenchmarkService.GenerateRecommendations(
            HardwareReadinessScore.Go, 45.0, 100.0);
        Assert.Contains(goRecs, r => r.Contains("optimal for production"));

        IReadOnlyList<string> marginalRecs = AcceleratorBenchmarkService.GenerateRecommendations(
            HardwareReadinessScore.Marginal, 45.0, 100.0);
        Assert.Contains(marginalRecs, r => r.Contains("marginal: suitable for low-atom"));

        IReadOnlyList<string> insufficientRecs = AcceleratorBenchmarkService.GenerateRecommendations(
            HardwareReadinessScore.Insufficient, 45.0, 100.0);
        Assert.Contains(insufficientRecs, r => r.Contains("insufficient: compute throughput"));
    }

    /// <summary>
    /// Verifies that thermal and power warnings are appended when safe operational limits are exceeded.
    /// </summary>
    [Fact]
    public void GenerateRecommendations_IncludesThermalAndPowerWarnings_WhenThresholdsExceeded()
    {
        // Temperature > 80°C and Power > 300W
        IReadOnlyList<string> hotAndPowerHeavy = AcceleratorBenchmarkService.GenerateRecommendations(
            HardwareReadinessScore.Go, 85.5, 350.0);
        Assert.Contains(hotAndPowerHeavy, r => r.Contains("Thermal warning"));
        Assert.Contains(hotAndPowerHeavy, r => r.Contains("Power warning"));

        // Normal temperature (<= 80°C) and normal power (<= 300W)
        IReadOnlyList<string> coolAndNominal = AcceleratorBenchmarkService.GenerateRecommendations(
            HardwareReadinessScore.Go, 75.0, 250.0);
        Assert.DoesNotContain(coolAndNominal, r => r.Contains("Thermal warning"));
        Assert.DoesNotContain(coolAndNominal, r => r.Contains("Power warning"));
    }

    /// <summary>
    /// Verifies that <see cref="AcceleratorBenchmarkService.GetLatestReportAsync"/> returns null before any benchmark runs,
    /// and returns the cached report once a benchmark finishes.
    /// </summary>
    [Fact]
    public async Task GetLatestReportAsync_ReturnsNull_BeforeRun_AndCachedReport_AfterRun()
    {
        AcceleratorBenchmarkService service = new(
            null,
            () => (Fp32Gflops: 6000.0, Fp64Gflops: 1500.0, BandwidthGbps: 200.0));

        AcceleratorBenchmarkReport? initial = await service.GetLatestReportAsync();
        Assert.Null(initial);

        AcceleratorBenchmarkReport ranReport = await service.RunBenchmarkAsync();
        AcceleratorBenchmarkReport? cached = await service.GetLatestReportAsync();

        Assert.NotNull(cached);
        Assert.Same(ranReport, cached);
    }

    /// <summary>
    /// Verifies that <see cref="AcceleratorBenchmarkService.GetLatestReportAsync"/> respects cancellation tokens.
    /// </summary>
    [Fact]
    public async Task GetLatestReportAsync_ThrowsOperationCanceledException_WhenCanceled()
    {
        AcceleratorBenchmarkService service = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetLatestReportAsync(cts.Token));
    }

    /// <summary>
    /// Verifies that internal static <see cref="AcceleratorBenchmarkService.ExecuteComputeBenchmark"/> produces positive throughput metrics.
    /// </summary>
    [Fact]
    public void ExecuteComputeBenchmark_ProducesPositiveThroughputMetrics()
    {
        AcceleratorBenchmarkService.ExecuteComputeBenchmark(
            out double fp32Gflops,
            out double fp64Gflops,
            out double bandwidthGbps);

        Assert.True(fp32Gflops > 0.0);
        Assert.True(fp64Gflops > 0.0);
        Assert.True(bandwidthGbps > 0.0);
    }
}
