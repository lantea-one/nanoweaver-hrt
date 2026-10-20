# REST API Reference & Test Harness

> **Programmatic Minimal API Catalog, JSON Payloads, and Automated Test Execution**

The **Weaver-LAMMPS-HRT** platform exposes a high-throughput, low-overhead REST API built on ASP.NET Core Minimal APIs. All endpoints return standard JSON payloads and support zero-allocation streaming where applicable.

---

## 1. System Health & Telemetry Endpoints

### `GET /healthz`
Lightweight health probe for container orchestrators (Kubernetes, Docker Compose).

* **Method**: `GET`
* **Response Status**: `200 OK`
* **Response Payload**:
  ```json
  {
    "status": "Healthy",
    "timestamp": "2026-10-20T16:15:00.123Z",
    "version": "1.0.0"
  }
  ```

---

### `GET /api/system/status`
Comprehensive microservice dependency status and connectivity pings.

* **Method**: `GET`
* **Response Status**: `200 OK`
* **Response Payload**:
  ```json
  {
    "service": "WeaverLammpsHrt.TrajectoryParser",
    "uptime_seconds": 18450.4,
    "database": {
      "provider": "PostgreSQL",
      "connected": true,
      "latency_ms": 1.2
    },
    "simulation_engine": {
      "oxdna_present": true,
      "lammps_present": true,
      "shared_volume_writable": true
    },
    "simd_acceleration": {
      "detected": "AVX-512",
      "vector_width_bits": 512,
      "hardware_supported": true
    }
  }
  ```

---

### `GET /api/system/telemetry`
Real-time memory, CPU, and Garbage Collector telemetry.

* **Method**: `GET`
* **Response Status**: `200 OK`
* **Response Payload**:
  ```json
  {
    "timestamp": "2026-10-20T16:15:30.000Z",
    "cpu_usage_percent": 14.8,
    "memory_allocated_bytes": 48234496,
    "gc_gen0_collections": 42,
    "gc_gen1_collections": 3,
    "gc_gen2_collections": 0,
    "thread_pool_worker_threads_active": 4,
    "thread_pool_worker_threads_available": 32763
  }
  ```

---

## 2. Configuration & Peering Endpoints

### `GET /api/config`
Retrieves authoritative system configuration from the database (or bootstrap defaults).

* **Method**: `GET`
* **Response Status**: `200 OK`
* **Response Payload**:
  ```json
  {
    "pipeReaderHighWatermark": 4194304,
    "pipeReaderLowWatermark": 1048576,
    "simdVectorizationMode": "Auto",
    "sharedDataPath": "/simulations/shared-data",
    "parquetCompressionCodec": "Zstandard",
    "maxConcurrentStreamCount": 8,
    "peerFederationEnabled": true
  }
  ```

---

### `PUT /api/config`
Updates system configuration in the database. Adheres to the **Database Precedence Law** (overwrites environment variable defaults permanently).

* **Method**: `PUT`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "pipeReaderHighWatermark": 8388608,
    "simdVectorizationMode": "AVX512",
    "parquetCompressionCodec": "Zstandard"
  }
  ```
* **Response Status**: `200 OK`

---

### `POST /api/peers/generate-key`
Issues a new single-use cryptographic share key.

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "expirationWindow": "24h",
    "label": "Lab B Collaboration Key"
  }
  ```
  *Allowed `expirationWindow` values*: `"1h"`, `"4h"`, `"24h"`, `"7d"`, `"1m"`, `"1y"`, `"Never"`.
* **Response Status**: `201 Created`
* **Response Payload**:
  ```json
  {
    "key": "nanoweaver_hrt_9e8b7c6d5e4f3a2b1c0d9e8b7c6d5e4f3a2b1c0d9e8b7c6d5e4f3a2b1c0d9e8b",
    "label": "Lab B Collaboration Key",
    "created_at": "2026-10-20T16:20:00Z",
    "expires_at": "2026-10-21T16:20:00Z",
    "is_consumed": false
  }
  ```

---

### `POST /api/peers/connect`
Redeems a single-use share key to establish an authenticated federated link with a remote node.

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "remoteUrl": "https://lab-a.hrt-lab.local:5000",
    "shareKey": "nanoweaver_hrt_9e8b7c6d5e4f3a2b1c0d9e8b7c6d5e4f3a2b1c0d9e8b7c6d5e4f3a2b1c0d9e8b"
  }
  ```
* **Response Status**:
  * `200 OK`: Key redeemed atomically; peer link active.
  * `409 Conflict`: Key already consumed or expired.
  * `401 Unauthorized`: Invalid key token.

---

## 3. Trajectory & Dataset Endpoints

### `GET /api/trajectories`
Enumerates available trajectory files in the shared storage mount.

* **Method**: `GET`
* **Response Status**: `200 OK`
* **Response Payload**:
  ```json
  [
    {
      "fileName": "nanotube_folding.xyz",
      "format": "ExtendedXYZ",
      "sizeBytes": 104857600,
      "lastModified": "2026-10-20T15:00:00Z",
      "isParquetConverted": true
    }
  ]
  ```

---

### `POST /api/trajectories/stream`
Triggers high-throughput zero-allocation pipeline streaming of a trajectory file.

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "fileName": "nanotube_folding.xyz",
    "format": "XYZ",
    "convertToParquet": true,
    "compression": "Zstandard"
  }
  ```
* **Response Status**: `202 Accepted`

---

### `GET /api/datasets/export`
Packages trajectories, configs, and signed manifests into a deterministic `.tar.gz` bundle.

* **Method**: `GET`
* **Query Parameters**:
  * `datasetId`: Unique dataset identifier.
* **Response Headers**:
  * `Content-Type: application/gzip`
  * `Content-Disposition: attachment; filename="dataset_export.tar.gz"`

---

### `POST /api/datasets/import`
Streams an uploaded dataset bundle, unpacks files in memory, and verifies all constituent files against the SHA-256 cryptographic manifest.

* **Method**: `POST`
* **Request Headers**: `Content-Type: multipart/form-data`
* **Form Field**: `file` (the `.tar.gz` bundle).
* **Response Status**:
  * `200 OK`: Manifest verified; dataset staged.
  * `422 Unprocessable Entity`: SHA-256 checksum mismatch or corrupted archive.

---

## 4. Endocrine Simulation Endpoints

### `POST /api/endocrine/simulate`
Executes closed-loop pharmacodynamics simulation across a 72-hour timeline.

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "targetProtocol": "FeminizingMtF",
    "durationHours": 72,
    "estradiolTargetPmolL": 550.0,
    "testosteroneTargetNmolL": 1.5,
    "perturbations": [
      {
        "hour": 24,
        "type": "ExogenousBolus",
        "magnitude": 250.0
      }
    ]
  }
  ```
* **Response Status**: `200 OK`
* **Response Payload**:
  ```json
  {
    "protocol": "FeminizingMtF",
    "timePointsHours": [0, 1, 2, "...", 72],
    "estradiolCurve": [120.0, 180.5, "...", 548.9],
    "testosteroneCurve": [18.2, 14.1, "...", 1.4],
    "lhCurve": [8.4, 6.2, "...", 0.9],
    "steadyStateAchieved": true,
    "equilibriumHour": 18.5
  }
  ```

---

## 5. Automated Test Suite Execution

We enforce 100% test pass rate and 100% line/branch code coverage across both .NET and JavaScript codebases.

### Running .NET Test Suite & Code Coverage
```bash
# Execute all unit and integration tests with Coverlet code coverage
XDG_RUNTIME_DIR=/tmp dotnet test dotnet-weaver-lammps-hrt.sln \
  --collect:"XPlat Code Coverage" \
  --settings coverlet.runsettings
```

### Running Frontend Visualization Unit Tests
```bash
cd src/WeaverLammpsHrt.TrajectoryParser/wwwroot
npm test
cd ../../..
```

---

## Next Steps
* Return to the [**Portal Home**](index.md).
* Review practical use cases in [**Example Biomedical Workflows**](example-workflows.md).
