# Hardware Accelerator Benchmarking & Simulation Qualification

> **Automated FP32/FP64 Throughput and Memory Bandwidth Qualification for Molecular Dynamics Engines**

Running in-silico synthetic biology simulations (such as coarse-grained oxDNA 2.4 calculations and all-atom LAMMPS runs) places immense demands on hardware compute pipelines and host-device memory buses. An underpowered or thermally throttled GPU can cause simulations to silently slow down, stall, or corrupt trajectory steps.

NanoWeaverHrt provides an automated, non-destructive **Hardware Accelerator Benchmark Suite** that qualifies hardware simulation readiness before long-running production campaigns begin.

---

## 1. Measured Performance Metrics

The benchmark suite measures three foundational physical metrics using cache-warmed SIMD and compute vector loops:

| Metric | Physical Significance | Simulation Impact |
| :--- | :--- | :--- |
| **FP32 Compute Throughput (GFLOPS)** | Single-precision floating-point matrix operations | Drives oxDNA base-pairing, stacking potentials, and Lennard-Jones pair evaluations |
| **FP64 Compute Throughput (GFLOPS)** | Double-precision floating-point calculations | Essential for high-precision symplectic integration and long-timestep thermostat stability |
| **Memory Bandwidth (GB/s)** | Host-to-device and device-to-host memory bus transfer speed | Determines how quickly atom coordinate frames and velocity buffers are streamed to the parser |
| **Thermal & Power Telemetry** | Core temperature (°C) and power draw (Watts) | Detects thermal throttling, cooling failures, and power delivery deficits |

---

## 2. Hardware Readiness Scoring Matrix

The engine aggregates benchmark metrics into a deterministic three-tier readiness score:

```
                      +-------------------------------------------+
                      |       Run Hardware Qualification          |
                      +---------------------+---------------------+
                                            |
                         +------------------+------------------+
                         |                                     |
                         v                                     v
                 FP32 >= 5,000 GFLOPS                 FP32 >= 1,500 GFLOPS
               AND Bandwidth >= 150 GB/s             OR Bandwidth >= 50 GB/s
                         |                                     |
                         v                                     v
                   [ SCORE: GO ]                       [ SCORE: MARGINAL ]
             Full production campaigns           Small-scale kinetic runs
             (384-well arrays, origami)         (single duplexes, coarse tests)
                         |                                     |
                         +------------------+------------------+
                                            |
                                            v (otherwise)
                                  [ SCORE: INSUFFICIENT ]
                                  CPU / Integrated fallback
```

### Readiness Levels

#### 1. Go (Green Badge)
- **Threshold**: FP32 ≥ 5,000 GFLOPS and Memory Bandwidth ≥ 150 GB/s.
- **Suitability**: Full-scale production campaigns, multi-thousand nucleotide DNA origami nanostructures, continuous real-time trajectory streaming.
- **Example Hardware**: Intel Arc A770 (16 GB), NVIDIA RTX 3080 / 4070 / 4080 / 4090, AMD Radeon RX 6800 / 7800 XT / 7900 XTX, Apple M-series Max/Ultra.

#### 2. Marginal (Amber Badge)
- **Threshold**: FP32 ≥ 1,500 GFLOPS or Memory Bandwidth ≥ 50 GB/s.
- **Suitability**: Small-scale simulations (short oligonucleotides, simple aptamer binding, 2-strand kinetic probes).
- **Recommendations**: Reduce simulation box sizes, increase snapshot save intervals, and disable real-time 3D visual rendering during runs.
- **Example Hardware**: Intel Iris Xe / Arc A380, NVIDIA GTX 1660 / RTX 3050, AMD Radeon 6600, Apple M1/M2/M3 base chips.

#### 3. Insufficient (Red Badge)
- **Threshold**: Below marginal requirements.
- **Suitability**: Simulation tasks should fall back to CPU multi-threading or offload to a remote sovereign compute peer.
- **Recommendations**: Connect an external accelerator, or peer with an institutional NanoWeaverHrt node via the P2P dataset bridge.

---

## 3. Triggering Benchmarks via REST API

### Trigger On-Demand Benchmark
```http
POST /api/system/benchmark/run HTTP/1.1
Authorization: Bearer <token>
```
**Response (200 OK)**:
```json
{
  "deviceName": "Intel Arc A770 Graphics",
  "driverVersion": "24.1.0-opencl",
  "singlePrecisionGflops": 18500.5,
  "doublePrecisionGflops": 2300.25,
  "memoryBandwidthGigaBytesPerSecond": 512.75,
  "temperatureCelsius": 58.5,
  "powerUsageWatts": 195.0,
  "readiness": 0,
  "recommendations": [
    "Hardware surpasses production simulation campaign requirements."
  ],
  "benchmarkTimestampUtc": "2026-11-03T16:00:00Z"
}
```

### Inspect Latest Cached Report
```http
GET /api/system/benchmark/latest HTTP/1.1
Authorization: Bearer <token>
```

---

## 4. Web UI Diagnostics Integration

The benchmark suite is directly integrated into the Sovereign Stack Services dashboard (`/services`):
- **Live Readiness Badges**: Instant visual feedback displaying `Go`, `Marginal`, or `Insufficient`.
- **GFLOPS Throughput Meters**: High-contrast meters displaying FP32 and FP64 computational performance.
- **One-Click Qualification**: An interactive "Run Hardware Qualification" button with real-time progress indicators.
- **Automated Recommendations**: Tailored guidance displayed when marginal or insufficient performance is detected.
