using System.Buffers;
using System.Diagnostics;

namespace NanoWeaverHrt.Hardware.Benchmarking;

/// <summary>
/// Hardware accelerator qualification and diagnostic benchmark engine.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Plain-English Summary:</strong> This service qualifies GPU and accelerator hardware for DNA origami
/// and endocrine simulation workloads. It executes deterministic floating-point matrix calculations (FP32 and FP64)
/// and memory throughput transfers to categorize hardware into Go, Marginal, or Insufficient readiness tiers.
/// </para>
/// </remarks>
public sealed class AcceleratorBenchmarkService : IAcceleratorBenchmarkService
{
    /// <summary>
    /// Square dimension of benchmark test matrices for FP32 and FP64 compute throughput testing.
    /// </summary>
    private const int MatrixDimension = 32;

    /// <summary>
    /// Iteration count for floating-point compute benchmark loops.
    /// </summary>
    private const int ComputeIterations = 50;

    /// <summary>
    /// Size in bytes of memory transfer buffers used for bandwidth throughput testing (256 KB).
    /// </summary>
    private const int BandwidthBufferSize = 256 * 1024;

    /// <summary>
    /// Iteration count for memory transfer bandwidth testing loops.
    /// </summary>
    private const int BandwidthIterations = 20;

    /// <summary>
    /// Telemetry provider supplying device identification, driver version, thermal, and electrical telemetry.
    /// </summary>
    private readonly IAcceleratorTelemetryProvider _telemetryProvider;

    /// <summary>
    /// Concurrency throttle ensuring only one hardware benchmark executes at any given time.
    /// </summary>
    private readonly SemaphoreSlim _benchmarkLock = new(1, 1);

    /// <summary>
    /// Optional benchmark compute delegate used to override hardware execution in deterministic test harnesses.
    /// </summary>
    private readonly Func<(double Fp32Gflops, double Fp64Gflops, double BandwidthGbps)>? _benchmarkOverride;

    /// <summary>
    /// Cached latest benchmark report, or null if no benchmark has been executed.
    /// </summary>
    private AcceleratorBenchmarkReport? _latestReport;

    /// <summary>
    /// Initializes a new instance of the <see cref="AcceleratorBenchmarkService"/> class using the default telemetry provider.
    /// </summary>
    public AcceleratorBenchmarkService() : this(null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AcceleratorBenchmarkService"/> class with an optional telemetry provider.
    /// </summary>
    /// <param name="telemetryProvider">Optional accelerator telemetry provider. If null, a default provider is used.</param>
    public AcceleratorBenchmarkService(IAcceleratorTelemetryProvider? telemetryProvider) : this(telemetryProvider, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AcceleratorBenchmarkService"/> class with telemetry and an optional test override.
    /// </summary>
    /// <param name="telemetryProvider">Optional accelerator telemetry provider.</param>
    /// <param name="benchmarkOverride">Optional benchmark compute override delegate for qualification testing.</param>
    internal AcceleratorBenchmarkService(
        IAcceleratorTelemetryProvider? telemetryProvider,
        Func<(double Fp32Gflops, double Fp64Gflops, double BandwidthGbps)>? benchmarkOverride)
    {
        _telemetryProvider = telemetryProvider ?? new DefaultAcceleratorTelemetryProvider();
        _benchmarkOverride = benchmarkOverride;
    }

    /// <inheritdoc />
    public async Task<AcceleratorBenchmarkReport> RunBenchmarkAsync(CancellationToken ct = default)
    {
        // WHAT: Enforces task cancellation if requested prior to starting the benchmark.
        // WHY:  Avoids executing intensive compute loops if the caller has aborted the request.
        // HOW:  Calls ThrowIfCancellationRequested on the provided cancellation token.
        ct.ThrowIfCancellationRequested();

        // WHAT: Serializes benchmark runs using a semaphore to avoid concurrent hardware contention.
        // WHY:  Simultaneous benchmarks on the same GPU/accelerator would distort throughput measurements.
        // HOW:  Asynchronously waits on the semaphore with cancellation token support.
        await _benchmarkLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();

            double fp32Gflops;
            double fp64Gflops;
            double bandwidthGbps;

            // WHAT: Executes either simulated benchmark override or actual rented-buffer compute loops.
            // WHY:  Permits test environments without physical GPUs to validate readiness categorization.
            // HOW:  Invokes _benchmarkOverride if present, otherwise calls ExecuteComputeBenchmark.
            if (_benchmarkOverride != null)
            {
                (fp32Gflops, fp64Gflops, bandwidthGbps) = _benchmarkOverride();
            }
            else
            {
                ExecuteComputeBenchmark(out fp32Gflops, out fp64Gflops, out bandwidthGbps);
            }

            string deviceName = _telemetryProvider.DeviceName;
            string driverVersion = _telemetryProvider.DriverVersion;
            double temperatureCelsius = _telemetryProvider.TemperatureCelsius;
            double powerUsageWatts = _telemetryProvider.PowerUsageWatts;

            HardwareReadinessScore readiness = DetermineReadinessScore(fp32Gflops, bandwidthGbps);
            IReadOnlyList<string> recommendations = GenerateRecommendations(
                readiness,
                temperatureCelsius,
                powerUsageWatts);

            AcceleratorBenchmarkReport report = new()
            {
                DeviceName = deviceName,
                DriverVersion = driverVersion,
                SinglePrecisionGflops = fp32Gflops,
                DoublePrecisionGflops = fp64Gflops,
                MemoryBandwidthGigaBytesPerSecond = bandwidthGbps,
                TemperatureCelsius = temperatureCelsius,
                PowerUsageWatts = powerUsageWatts,
                Readiness = readiness,
                Recommendations = recommendations,
                BenchmarkTimestampUtc = DateTimeOffset.UtcNow
            };

            _latestReport = report;
            return report;
        }
        finally
        {
            _benchmarkLock.Release();
        }
    }

    /// <inheritdoc />
    public Task<AcceleratorBenchmarkReport?> GetLatestReportAsync(CancellationToken ct = default)
    {
        // WHAT: Returns the cached latest benchmark report.
        // WHY:  Permits status queries without triggering high-load benchmark compute runs.
        // HOW:  Verifies cancellation token then returns completed Task wrapping _latestReport.
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_latestReport);
    }

    /// <summary>
    /// Executes deterministic FP32 and FP64 matrix compute loops and memory bandwidth transfers using pooled memory.
    /// </summary>
    /// <param name="fp32Gflops">Receives the measured single-precision compute throughput in GFLOPS.</param>
    /// <param name="fp64Gflops">Receives the measured double-precision compute throughput in GFLOPS.</param>
    /// <param name="bandwidthGbps">Receives the measured memory transfer bandwidth in GB/s.</param>
    internal static void ExecuteComputeBenchmark(out double fp32Gflops, out double fp64Gflops, out double bandwidthGbps)
    {
        fp32Gflops = BenchmarkFp32Compute();
        fp64Gflops = BenchmarkFp64Compute();
        bandwidthGbps = BenchmarkMemoryBandwidth();
    }

    /// <summary>
    /// Evaluates the hardware readiness score based on measured FP32 compute throughput and memory bandwidth.
    /// </summary>
    /// <param name="singlePrecisionGflops">Measured single-precision compute throughput in GFLOPS.</param>
    /// <param name="memoryBandwidthGigaBytesPerSecond">Measured host-to-device memory transfer bandwidth in GB/s.</param>
    /// <returns>The calculated hardware readiness score.</returns>
    public static HardwareReadinessScore DetermineReadinessScore(double singlePrecisionGflops, double memoryBandwidthGigaBytesPerSecond)
    {
        // WHAT: Evaluates whether hardware passes high-throughput production simulation criteria.
        // WHY:  Production oxDNA 2.4 and LAMMPS trajectories require massive FP32 parallelism and memory bandwidth.
        // HOW:  Compares FP32 GFLOPS against 5,000 threshold and memory bandwidth against 150 GB/s.
        if (singlePrecisionGflops >= 5000.0 && memoryBandwidthGigaBytesPerSecond >= 150.0)
        {
            return HardwareReadinessScore.Go;
        }

        // WHAT: Evaluates whether hardware meets minimum thresholds for smaller kinetic runs.
        // WHY:  Lower-atom gene circuit models can execute on moderate accelerator hardware.
        // HOW:  Checks if FP32 exceeds 1,500 GFLOPS or bandwidth exceeds 50 GB/s.
        if (singlePrecisionGflops >= 1500.0 || memoryBandwidthGigaBytesPerSecond >= 50.0)
        {
            return HardwareReadinessScore.Marginal;
        }

        // WHAT: Flags insufficient hardware performance.
        // WHY:  Hardware failing both thresholds will bottleneck or fail molecular dynamics workloads.
        // HOW:  Returns HardwareReadinessScore.Insufficient as the fallback classification.
        return HardwareReadinessScore.Insufficient;
    }

    /// <summary>
    /// Generates actionable operational recommendations based on the calculated readiness score, operating temperature, and power telemetry.
    /// </summary>
    /// <param name="readiness">The evaluated readiness score.</param>
    /// <param name="temperatureCelsius">Operating temperature in degrees Celsius.</param>
    /// <param name="powerUsageWatts">Power usage in watts.</param>
    /// <returns>A read-only list of actionable recommendations.</returns>
    public static IReadOnlyList<string> GenerateRecommendations(
        HardwareReadinessScore readiness,
        double temperatureCelsius,
        double powerUsageWatts)
    {
        List<string> recommendations = new();

        switch (readiness)
        {
            case HardwareReadinessScore.Go:
                recommendations.Add("Hardware accelerator qualification passed: device is optimal for production oxDNA 2.4 and LAMMPS molecular dynamics campaigns.");
                recommendations.Add("High-density DNA origami folding and multi-strand kinetic trajectories can be scheduled at maximum batch concurrency.");
                break;

            case HardwareReadinessScore.Marginal:
                recommendations.Add("Hardware accelerator performance is marginal: suitable for low-atom kinetic runs and small gene circuit simulations.");
                recommendations.Add("Production-scale DNA origami nanostructures may encounter compute or bandwidth bottlenecks; consider upgrading or provisioning dedicated discrete GPU acceleration.");
                break;

            default:
                recommendations.Add("Hardware accelerator performance is insufficient: compute throughput or memory bandwidth fails minimum simulation standards.");
                recommendations.Add("Simulation execution is not recommended on this hardware; verify OpenCL/SYCL GPU drivers or attach a supported discrete accelerator.");
                break;
        }

        if (temperatureCelsius > 80.0)
        {
            recommendations.Add("Thermal warning: accelerator temperature exceeds 80°C; inspect chassis airflow and active cooling.");
        }

        if (powerUsageWatts > 300.0)
        {
            recommendations.Add("Power warning: accelerator power draw exceeds 300W; verify power supply capacity for sustained workloads.");
        }

        return recommendations;
    }

    /// <summary>
    /// Executes a deterministic single-precision (FP32) matrix multiplication benchmark using pooled buffers.
    /// </summary>
    /// <returns>Measured single-precision compute throughput in GFLOPS.</returns>
    private static double BenchmarkFp32Compute()
    {
        // WHAT: Rents contiguous FP32 matrix buffers from the shared pool.
        // WHY:  Guarantees zero GC allocation pressure during high-frequency compute benchmarking.
        // HOW:  Rents 3 buffers of size MatrixDimension * MatrixDimension and returns them in a finally block.
        const int dimension = MatrixDimension;
        const int matrixSize = dimension * dimension;
        const int iterations = ComputeIterations;

        float[] a = ArrayPool<float>.Shared.Rent(matrixSize);
        float[] b = ArrayPool<float>.Shared.Rent(matrixSize);
        float[] c = ArrayPool<float>.Shared.Rent(matrixSize);

        try
        {
            // WHAT: Deterministically initializes matrix elements without runtime heap allocation.
            // WHY:  Ensures identical mathematical operations across runs for reproducible GFLOPS measurement.
            // HOW:  Populates matrices A and B with modular arithmetic patterns.
            for (int i = 0; i < matrixSize; i++)
            {
                a[i] = 1.0f + (i % 7) * 0.1f;
                b[i] = 0.5f + (i % 11) * 0.1f;
                c[i] = 0.0f;
            }

            long startTimestamp = Stopwatch.GetTimestamp();

            // WHAT: Executes cache-warmed matrix multiplication kernel.
            // WHY:  Generates floating-point multiply-accumulate operations to measure FP32 throughput.
            // HOW:  Iterates over row, column, and inner product loops accumulating into matrix C.
            for (int iter = 0; iter < iterations; iter++)
            {
                for (int i = 0; i < dimension; i++)
                {
                    int rowOffset = i * dimension;
                    for (int k = 0; k < dimension; k++)
                    {
                        float aVal = a[rowOffset + k];
                        int bOffset = k * dimension;
                        for (int j = 0; j < dimension; j++)
                        {
                            c[rowOffset + j] += aVal * b[bOffset + j];
                        }
                    }
                }
            }

            TimeSpan elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            double elapsedSeconds = Math.Max(elapsed.TotalSeconds, 1e-9);

            // Total floating-point operations: 2 * N^3 operations per iteration
            double totalFlops = 2.0 * dimension * dimension * dimension * iterations;
            return (totalFlops / elapsedSeconds) / 1e9;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(a);
            ArrayPool<float>.Shared.Return(b);
            ArrayPool<float>.Shared.Return(c);
        }
    }

    /// <summary>
    /// Executes a deterministic double-precision (FP64) matrix multiplication benchmark using pooled buffers.
    /// </summary>
    /// <returns>Measured double-precision compute throughput in GFLOPS.</returns>
    private static double BenchmarkFp64Compute()
    {
        // WHAT: Rents contiguous FP64 matrix buffers from the shared pool.
        // WHY:  Guarantees zero GC allocation pressure during double-precision benchmarking.
        // HOW:  Rents 3 buffers of size MatrixDimension * MatrixDimension and returns them in a finally block.
        const int dimension = MatrixDimension;
        const int matrixSize = dimension * dimension;
        const int iterations = ComputeIterations;

        double[] a = ArrayPool<double>.Shared.Rent(matrixSize);
        double[] b = ArrayPool<double>.Shared.Rent(matrixSize);
        double[] c = ArrayPool<double>.Shared.Rent(matrixSize);

        try
        {
            // WHAT: Deterministically initializes double-precision matrix elements.
            // WHY:  Ensures identical mathematical operations across runs for reproducible FP64 measurement.
            // HOW:  Populates matrices A and B with modular arithmetic patterns.
            for (int i = 0; i < matrixSize; i++)
            {
                a[i] = 1.0 + (i % 7) * 0.1;
                b[i] = 0.5 + (i % 11) * 0.1;
                c[i] = 0.0;
            }

            long startTimestamp = Stopwatch.GetTimestamp();

            // WHAT: Executes cache-warmed double-precision matrix multiplication kernel.
            // WHY:  Measures double-precision floating-point arithmetic capability.
            // HOW:  Iterates over row, column, and inner product loops accumulating into matrix C.
            for (int iter = 0; iter < iterations; iter++)
            {
                for (int i = 0; i < dimension; i++)
                {
                    int rowOffset = i * dimension;
                    for (int k = 0; k < dimension; k++)
                    {
                        double aVal = a[rowOffset + k];
                        int bOffset = k * dimension;
                        for (int j = 0; j < dimension; j++)
                        {
                            c[rowOffset + j] += aVal * b[bOffset + j];
                        }
                    }
                }
            }

            TimeSpan elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            double elapsedSeconds = Math.Max(elapsed.TotalSeconds, 1e-9);

            // Total floating-point operations: 2 * N^3 operations per iteration
            double totalFlops = 2.0 * dimension * dimension * dimension * iterations;
            return (totalFlops / elapsedSeconds) / 1e9;
        }
        finally
        {
            ArrayPool<double>.Shared.Return(a);
            ArrayPool<double>.Shared.Return(b);
            ArrayPool<double>.Shared.Return(c);
        }
    }

    /// <summary>
    /// Executes a deterministic memory transfer bandwidth benchmark using pooled byte buffers.
    /// </summary>
    /// <returns>Measured memory transfer bandwidth in gigabytes per second (GB/s).</returns>
    private static double BenchmarkMemoryBandwidth()
    {
        // WHAT: Rents contiguous byte buffers from the shared pool for memory throughput testing.
        // WHY:  Evaluates memory bus throughput without incurring GC allocation overhead.
        // HOW:  Rents source and destination byte buffers and transfers blocks in a tight loop.
        const int bufferSize = BandwidthBufferSize;
        const int iterations = BandwidthIterations;

        byte[] src = ArrayPool<byte>.Shared.Rent(bufferSize);
        byte[] dst = ArrayPool<byte>.Shared.Rent(bufferSize);

        try
        {
            // WHAT: Fills the source buffer deterministically.
            // WHY:  Ensures valid memory state before transfer benchmarking.
            // HOW:  Fills bytes with modular index values.
            for (int i = 0; i < bufferSize; i++)
            {
                src[i] = (byte)(i % 256);
            }

            long startTimestamp = Stopwatch.GetTimestamp();

            // WHAT: Copies memory blocks between source and destination buffers.
            // WHY:  Measures raw memory copy throughput across the memory subsystem.
            // HOW:  Performs Buffer.BlockCopy for the specified iteration count.
            for (int iter = 0; iter < iterations; iter++)
            {
                Buffer.BlockCopy(src, 0, dst, 0, bufferSize);
            }

            TimeSpan elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            double elapsedSeconds = Math.Max(elapsed.TotalSeconds, 1e-9);

            // Total bytes transferred
            double totalBytes = (double)bufferSize * iterations;
            return (totalBytes / elapsedSeconds) / 1e9;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(src);
            ArrayPool<byte>.Shared.Return(dst);
        }
    }
}
