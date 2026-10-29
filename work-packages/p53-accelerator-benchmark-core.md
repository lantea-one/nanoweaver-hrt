# Work Package P53: Hardware Accelerator Benchmark Engine Core

## 1. Context & Objective
- **Problem Statement**:
  Simulations of synthetic hormone gene circuits and DNA origami nanostructures depend heavily on GPU/accelerator performance (oxDNA 2.4 and LAMMPS with OpenCL/SYCL/CUDA/ROCm). Before launching long-running production simulations, operators need rigorous, automated hardware qualification measuring FP32 compute throughput (GFLOPS), FP64 double-precision throughput, and host-to-device memory bandwidth (GB/s) with physical Go / Marginal / Insufficient readiness scoring.
- **Goal & Scope**:
  1. Define domain models in `NanoWeaverHrt.Hardware.Benchmarking`:
     - `AcceleratorBenchmarkReport`:
       - `string DeviceName`
       - `string DriverVersion`
       - `double SinglePrecisionGflops` (FP32 matrix GEMM throughput)
       - `double DoublePrecisionGflops` (FP64 throughput)
       - `double MemoryBandwidthGigaBytesPerSecond` (host-to-device & device-to-host bandwidth)
       - `double TemperatureCelsius`
       - `double PowerUsageWatts`
       - `HardwareReadinessScore Readiness` (`Go`, `Marginal`, `Insufficient`)
       - `IReadOnlyList<string> Recommendations`
       - `DateTimeOffset BenchmarkTimestampUtc`
     - `HardwareReadinessScore` enum: `Go = 0`, `Marginal = 1`, `Insufficient = 2`
  2. Implement `IAcceleratorBenchmarkService`:
     - `Task<AcceleratorBenchmarkReport> RunBenchmarkAsync(CancellationToken ct = default);`
     - `Task<AcceleratorBenchmarkReport?> GetLatestReportAsync(CancellationToken ct = default);`
  3. Implement `AcceleratorBenchmarkService : IAcceleratorBenchmarkService`:
     - Hardware-isolated benchmark execution using managed SIMD vectors / OpenCL probes / direct telemetry.
     - Deterministic benchmark synthetic matrices (cache-warmed, zero garbage generation).
     - Readiness score calculation:
       - `Go`: FP32 > 5,000 GFLOPS and Bandwidth > 150 GB/s (ready for complex oxDNA nanostructures).
       - `Marginal`: FP32 > 1,500 GFLOPS or Bandwidth > 50 GB/s (sufficient for low-atom kinetic runs).
       - `Insufficient`: FP32 < 1,500 GFLOPS and Bandwidth < 50 GB/s.
  4. Author unit tests covering all calculation paths, thresholds, and edge cases with 100.00% Line & Branch coverage.
- **Target Role / Model**: `coder`
- **Transport Boundary**: Hardware Diagnostic Engine

---

## 2. Technical Contracts & Invariants
- **Namespace**: `NanoWeaverHrt.Hardware.Benchmarking`
- **Invariants**:
  - Blittable memory buffers rented from `ArrayPool<T>.Shared` and returned in `finally` blocks.
  - 100% XML docblocks on all types, methods, fields, and properties.
  - 100.00% Line & Branch test coverage.
  - Zero LaTeX single-dollar delimiters in comments or docstrings.
