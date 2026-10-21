# REST API Reference & Interactive Documentation

> **Minimal API Catalog, Multi-Format Serialization (JSON, XML & YAML), OpenAPI / Swagger / ReDoc Specifications, and Automated Test Harness**

The **NanoWeaverHrt** (`nanoweaver-hrt`) platform exposes a high-throughput, low-overhead REST API built on ASP.NET Core Minimal APIs. The API supports content negotiation across three representations:

* **JSON**: Strictly formatted in **lower camelCase** (e.g., `nodeName`, `framesParsed`, `targetKd`).
* **XML**: Clean, strongly-typed XML documents with explicit root naming.
* **YAML**: Serialized in **lower snake-case with hyphens** (kebab-case, e.g., `node-name`, `frames-parsed`, `target-kd`).

---

## 1. Interactive API Explorers & Schemas

The microservice automatically serves live, interactive OpenAPI 3.0 documentation with multi-format examples for all request and response contracts:

| Interface | URL Path | Description |
| :--- | :--- | :--- |
| **Swagger UI** | `/swagger` | Interactive, browser-based API explorer allowing live execution of requests, header configuration, and multi-format example preview. Redirects to `/swagger/index.html`. |
| **ReDoc** | `/documentation` | Responsive, three-column API reference rendering full OpenAPI schemas, status codes, and multi-format payload tabs. Redirects to `/documentation/index.html`. |
| **OpenAPI Specification** | `/swagger/v1/swagger.json` | Raw OpenAPI 3.0 JSON specification document containing schemas and multi-format example ASTs. |

### Content Negotiation Mechanics

Clients can request their desired representation using either standard HTTP `Accept` headers or the `?format=` query parameter:

```bash
# 1. Request lower camelCase JSON (Default)
curl -H "Accept: application/json" http://localhost:5000/api/system/telemetry

# 2. Request XML
curl -H "Accept: application/xml" http://localhost:5000/api/system/telemetry
# Or via query parameter:
curl "http://localhost:5000/api/system/telemetry?format=xml"

# 3. Request Hyphenated YAML
curl -H "Accept: application/x-yaml" http://localhost:5000/api/system/telemetry
# Or via query parameter:
curl "http://localhost:5000/api/system/telemetry?format=yaml"
```

Inbound request bodies (`POST`, `PUT`) can similarly be submitted in `application/json`, `application/xml`, or `application/x-yaml` via the `Content-Type` header.

---

## 2. System Hardware & Telemetry Endpoints

### `GET /api/system/telemetry`
Retrieves real-time CPU utilization, process memory, thread pool allocation, and Server GC generation metrics.

* **Method**: `GET`
* **Response Status**: `200 OK`
* **Response Payloads**:

<!-- tabs:start -->
#### **JSON (lower camelCase)**
```json
{
  "nodeName": "sovereign-node-01",
  "framesParsed": 128450,
  "allocatedMemoryMb": 48.25,
  "activeThreadPoolThreads": 4,
  "gen0Collections": 42,
  "gen1Collections": 3,
  "gen2Collections": 0,
  "uptime": "05:08:12.450"
}
```

#### **XML**
```xml
<SystemTelemetry>
  <nodeName>sovereign-node-01</nodeName>
  <framesParsed>128450</framesParsed>
  <allocatedMemoryMb>48.25</allocatedMemoryMb>
  <activeThreadPoolThreads>4</activeThreadPoolThreads>
  <gen0Collections>42</gen0Collections>
  <gen1Collections>3</gen1Collections>
  <gen2Collections>0</gen2Collections>
  <uptime>05:08:12.450</uptime>
</SystemTelemetry>
```

#### **YAML (lower snake-case with hyphens)**
```yaml
node-name: sovereign-node-01
frames-parsed: 128450
allocated-memory-mb: 48.25
active-thread-pool-threads: 4
gen0-collections: 42
gen1-collections: 3
gen2-collections: 0
uptime: 05:08:12.450
```
<!-- tabs:end -->

### `GET /api/system/event-bus`
Retrieves live operational metrics for the internal zero-allocation event bus and external Valkey cluster pub/sub bridge. Requires `Administrator` or `Researcher` role.

* **Method**: `GET`
* **Response Status**: `200 OK`
* **Response Payloads**:

<!-- tabs:start -->
#### **JSON (lower camelCase)**
```json
{
  "isInternalBusActive": true,
  "isValkeyConnected": true,
  "isDistributedCache": true,
  "valkeyEndpoint": "valkey-cluster:6379,password=[REDACTED]",
  "outboundEventsCount": 1420,
  "inboundEventsCount": 890,
  "activeSubscribers": {
    "TrajectoryFrameBatch": 2,
    "SimulationProgress": 4,
    "UserSessionActivity": 1,
    "AuditLogDispatched": 3,
    "SystemTelemetryUpdated": 2
  }
}
```

#### **XML**
```xml
<EventBusTelemetry>
  <isInternalBusActive>true</isInternalBusActive>
  <isValkeyConnected>true</isValkeyConnected>
  <isDistributedCache>true</isDistributedCache>
  <valkeyEndpoint>valkey-cluster:6379,password=[REDACTED]</valkeyEndpoint>
  <outboundEventsCount>1420</outboundEventsCount>
  <inboundEventsCount>890</inboundEventsCount>
  <activeSubscribers>
    <TrajectoryFrameBatch>2</TrajectoryFrameBatch>
    <SimulationProgress>4</SimulationProgress>
    <UserSessionActivity>1</UserSessionActivity>
    <AuditLogDispatched>3</AuditLogDispatched>
    <SystemTelemetryUpdated>2</SystemTelemetryUpdated>
  </activeSubscribers>
</EventBusTelemetry>
```

#### **YAML (lower snake-case with hyphens)**
```yaml
is-internal-bus-active: true
is-valkey-connected: true
is-distributed-cache: true
valkey-endpoint: valkey-cluster:6379,password=[REDACTED]
outbound-events-count: 1420
inbound-events-count: 890
active-subscribers:
  TrajectoryFrameBatch: 2
  SimulationProgress: 4
  UserSessionActivity: 1
  AuditLogDispatched: 3
  SystemTelemetryUpdated: 2
```
<!-- tabs:end -->

### `GET /api/system/health` (Aliases: `/healthz`, `/health`)
Multi-purpose operational and diagnostic health check endpoint.
* **Public & Unauthenticated (Container Healthchecks):** When no user session is present, returns a lightweight high-level overview (`SystemHealthOverview`) of node responsiveness, uptime, version, and first-launch setup status. Used by Docker and container orchestrators (`curl -f http://localhost:5000/healthz`).
* **Authenticated Sessions (Granular Subsystems):** When an authenticated session is present (via `Authorization: Bearer <token>` or `weaver_session` cookie), returns an in-depth diagnostic report (`GranularSystemHealth`) across database connectivity, applied migrations, event bus channels, Valkey pub/sub, distributed caching, memory allocations, storage mount accessibility, simulation campaign backends, and FDA/PQC audit compliance.

* **Method**: `GET`
* **Authentication**: None required (public). Optional Bearer token or `weaver_session` cookie activates granular telemetry.
* **Response Status**: `200 OK`
* **Response Payloads**:

<!-- tabs:start -->
#### **Unauthenticated JSON (SystemHealthOverview)**
```json
{
  "status": "Healthy",
  "timestampUtc": "2026-10-21T18:00:00.000Z",
  "version": "1.0.0",
  "uptime": "0d 4h 12m 30s",
  "uptimeSeconds": 15150.0,
  "setupRequired": false,
  "authenticatedSession": false,
  "description": "System operational. High-level health check. Authenticate with a valid session token for granular subsystem diagnostics."
}
```

#### **Authenticated JSON (GranularSystemHealth)**
```json
{
  "status": "Healthy",
  "timestampUtc": "2026-10-21T18:00:00.000Z",
  "version": "1.0.0",
  "uptime": "0d 4h 12m 30s",
  "uptimeSeconds": 15150.0,
  "setupRequired": false,
  "authenticatedSession": true,
  "description": "Authenticated session detected. Granular subsystem diagnostics active.",
  "user": {
    "username": "admin",
    "role": "Administrator",
    "accessGate": "ApiWeb"
  },
  "subsystems": {
    "database": {
      "status": "Healthy",
      "provider": "PostgreSQL",
      "canConnect": true,
      "appliedMigrationsCount": 2
    },
    "eventBus": {
      "status": "Healthy",
      "isInternalBusActive": true,
      "totalSubscribers": 6,
      "activeSubscribers": {
        "TrajectoryFrameBatchEvent": 2,
        "SimulationProgressEvent": 1,
        "UserSessionActivityEvent": 1,
        "AuditLogDispatchedEvent": 1,
        "SystemTelemetryUpdatedEvent": 1
      },
      "isValkeyBridgeConnected": true,
      "valkeyEndpoint": "valkey:6379,password=[REDACTED]"
    },
    "caching": {
      "status": "Healthy",
      "isDistributed": true,
      "mode": "Distributed (Valkey)"
    },
    "memory": {
      "status": "Healthy",
      "heapMemoryMb": 48.5,
      "workingSetMb": 120.0,
      "serverGc": true,
      "gen0Collections": 24,
      "gen1Collections": 2,
      "gen2Collections": 0
    },
    "storage": {
      "status": "Healthy",
      "incomingDirectory": "/workspace/data",
      "incomingWritable": true,
      "exportDirectory": "/workspace/exports",
      "exportWritable": true,
      "parquetDirectory": "/workspace/parquet",
      "parquetWritable": true
    },
    "simulations": {
      "status": "Healthy",
      "acceleratorBackend": "Intel Arc A770 (Level Zero / SYCL)",
      "totalFramesParsed": 450000,
      "availableCampaignsCount": 4
    },
    "compliance": {
      "status": "Healthy",
      "auditLedgerHealthy": true,
      "pqcProvider": "ML-KEM-768 + ML-DSA-65 (NIST FIPS 203/204)"
    }
  }
}
```

#### **Unauthenticated XML**
```xml
<Response>
  <status>Healthy</status>
  <timestampUtc>2026-10-21T18:00:00.000Z</timestampUtc>
  <version>1.0.0</version>
  <uptime>0d 4h 12m 30s</uptime>
  <uptimeSeconds>15150.0</uptimeSeconds>
  <setupRequired>false</setupRequired>
  <authenticatedSession>false</authenticatedSession>
  <description>System operational. High-level health check. Authenticate with a valid session token for granular subsystem diagnostics.</description>
</Response>
```

#### **Unauthenticated YAML**
```yaml
status: Healthy
timestamp-utc: '2026-10-21T18:00:00.000Z'
version: 1.0.0
uptime: 0d 4h 12m 30s
uptime-seconds: 15150.0
setup-required: false
authenticated-session: false
description: System operational. High-level health check. Authenticate with a valid session token for granular subsystem diagnostics.
```
<!-- tabs:end -->

---

## 3. Configuration & Sovereign Peering Endpoints

### `GET /api/configuration`
Enumerates all active system configuration items stored in the authoritative database.

* **Method**: `GET`
* **Response Status**: `200 OK`
* **Response Payload (JSON)**:
  ```json
  [
    {
      "key": "PipeReaderHighWatermark",
      "value": "4194304",
      "description": "Maximum unconsumed byte buffer size before PipeReader pauses upstream disk I/O.",
      "updatedAtUtc": "2026-10-20T16:00:00Z"
    },
    {
      "key": "SimdVectorizationMode",
      "value": "Auto",
      "description": "Runtime vector engine: Auto, AVX512, AVX2, SSE42, or ScalarFallback.",
      "updatedAtUtc": "2026-10-20T16:00:00Z"
    }
  ]
  ```

---

### `POST /api/configuration/override`
Applies an authoritative configuration override in the database, superseding stack environment variables permanently pursuant to the **Database Precedence Law**.

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "key": "PipeReaderHighWatermark",
    "value": "8388608"
  }
  ```
* **Response Status**: `200 OK`

---

### `DELETE /api/configuration/override/{key}`
Removes a configuration override, reverting the parameter to default runtime configuration.

* **Method**: `DELETE`
* **Response Status**: `200 OK`

---

### `GET /api/share-keys`
Lists all active and consumed single-use cryptographic share keys.

* **Method**: `GET`
* **Response Status**: `200 OK`

---

### `POST /api/share-keys/create`
Issues a new single-use cryptographic share key with a configurable expiration window.

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "label": "Lab Beta Collab Key",
    "lifetime": "24:00:00"
  }
  ```
* **Response Status**: `200 OK`
* **Response Payload**:
  ```json
  {
    "token": "nanoweaver_hrt_a7f92b41d08e5c1234b6e7890abcdef1234567890abcdef1234567890abcdef1",
    "label": "Lab Beta Collab Key",
    "createdAtUtc": "2026-10-20T20:00:00Z",
    "expiresAtUtc": "2026-10-21T20:00:00Z",
    "isConsumed": false
  }
  ```

---

### `POST /api/share-keys/redeem`
Atomically consumes a single-use share key to authenticate a remote research node.

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "token": "nanoweaver_hrt_a7f92b41d08e5c1234b6e7890abcdef1234567890abcdef1234567890abcdef1"
  }
  ```
* **Response Status**:
  * `200 OK`: Key redeemed successfully; federation established.
  * `409 Conflict`: Key already consumed or expired.
  * `401 Unauthorized`: Invalid key token.

---

## 4. Trajectory Streaming, Parquet & Datasets

### `GET /api/datasets`
Lists all available trajectory and simulation files staged in the shared storage mount.

* **Method**: `GET`
* **Response Status**: `200 OK`

---

### `POST /api/datasets/export`
Packages trajectories and signed provenance manifests into a cryptographically verified `.tar.gz` archive.

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json` (also accepts `application/xml`, `application/x-yaml`)
* **Request Payload**:
  ```json
  {
    "datasetName": "nanotube-folding-run",
    "outputDirectory": "simulations/exports"
  }
  ```
* **Response Status**: `200 OK`
* **Response Payload**:
  ```json
  {
    "dataset": "nanotube-folding-run",
    "timestamp": "2026-10-20T20:00:00Z",
    "archive": "nanotube-folding-run.tar.gz",
    "sha256": "4b9e28f7...a812",
    "sizeBytes": 14205849
  }
  ```

---

### `GET /api/datasets/download/{fileName}`
Directly downloads a signed dataset archive from the exports directory.

* **Method**: `GET`
* **Response Headers**: `Content-Type: application/gzip`

---

### `POST /api/datasets/import`
Streams an uploaded archive and its accompanying manifest into memory, performing byte-by-byte SHA-256 integrity verification.

* **Method**: `POST`
* **Request Headers**: `Content-Type: multipart/form-data`
* **Form Fields**:
  * `archive`: The compressed `.tar.gz` bundle.
  * `manifest`: The accompanying `manifest.json`.
* **Response Status**:
  * `200 OK`: Manifest verified; dataset staged.
  * `422 Unprocessable Entity`: Checksum mismatch or corrupted archive.

---

### `POST /api/parser/process/{fileName}`
Executes zero-allocation `System.IO.Pipelines` trajectory ingestion across `.xyz`, `.dump`, or `.dcd` files.

* **Method**: `POST`
* **Response Status**: `200 OK`

---

### `POST /api/parser/export-parquet/{fileName}`
Transforms raw text trajectory coordinate frames into compressed columnar Apache Parquet format.

* **Method**: `POST`
* **Response Status**: `200 OK`

---

## 5. Endocrine Regulatory Engine Endpoints

### `GET /api/endocrine/profile`
Retrieves the active endocrine target setpoints and current blood hormone concentrations.

* **Method**: `GET`
* **Response Status**: `200 OK`
* **Response Payload (JSON)**:
  ```json
  {
    "regimen": "MaleToFemale",
    "targetEstradiolPmolL": 550.0,
    "targetTestosteroneNmolL": 1.2,
    "currentEstradiolPmolL": 520.4,
    "currentTestosteroneNmolL": 1.35,
    "circulatingAptamerCount": 10000,
    "targetKdNm": 45.0,
    "isReleaseSwitchOpen": true
  }
  ```

---

### `POST /api/endocrine/toggle`
Toggles the bistable aptamer conformational state switch (Locked / Open).

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "isOpen": true
  }
  ```
* **Response Status**: `200 OK`

---

### `GET /api/endocrine/kinetics`
Retrieves the 72-hour simulated pharmacodynamics curve points.

* **Method**: `GET`
* **Response Status**: `200 OK`
* **Response Payload (JSON)**:
  ```json
  [
    {
      "hour": 0.0,
      "estradiolPmolL": 120.0,
      "testosteroneNmolL": 18.2
    },
    {
      "hour": 1.0,
      "estradiolPmolL": 185.4,
      "testosteroneNmolL": 14.1
    }
  ]
  ```

---

### `POST /api/endocrine/analyze/{fileName}`
Analyzes molecular dynamics coordinates to calculate aptamer binding pocket dimensions, center-of-mass drift, and binding affinity (Kd).

* **Method**: `POST`
* **Response Status**: `200 OK`
* **Response Payload**:
  ```json
  {
    "fileName": "aptamer_docking.xyz",
    "targetKd": 45.2,
    "pocketVolume": 12450.0,
    "bindingAffinityScore": 0.94,
    "centerOfMassDrift": 0.12
  }
  ```

---

## 6. Stack Microservice Orchestration

### `GET /api/services`
Inspects health status, process memory, and ping latencies across all registered microservice containers.

* **Method**: `GET`
* **Response Status**: `200 OK`

---

### `POST /api/services/ping/{serviceId}`
Executes an immediate round-trip latency probe against a target service (`oxdna`, `lammps`, `database`, `proxy`).

* **Method**: `POST`
* **Response Status**: `200 OK`

---

## 7. Benchtop DNA Origami Fabrication

### `GET /api/fabrication/presets`
Retrieves pre-engineered physical DNA nanocarrier fabrication packages (Feminizing MtF Nanotube, Masculinizing FtM Nanocage, Neutral Carrier).

* **Method**: `GET`
* **Response Status**: `200 OK`

---

### `POST /api/fabrication/generate`
Generates a complete benchtop fabrication package: staple routing, stoichiometric 50 µL pipetting recipe, 6-stage thermal annealing ramp, and quality control guidelines.

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "presetName": "FeminizingMtF",
    "reactionVolumeUl": 50.0,
    "targetKdNm": 45.0
  }
  ```
* **Response Status**: `200 OK`
* **Response Payload (JSON)**:
  ```json
  {
    "sample": {
      "sampleId": "SAMPLE-MTF-001",
      "name": "Feminizing MtF Nanotube",
      "regimen": "MaleToFemale",
      "scaffoldType": "M13mp18",
      "scaffoldLengthNt": 7249,
      "stapleCount": 184,
      "targetKdNm": 45.0,
      "thermalMeltingTempCelsius": 62.5
    },
    "guide": {
      "reactionVolumeUl": 50.0,
      "waterVolumeUl": 35.0,
      "foldingBufferVolumeUl": 5.0,
      "scaffoldVolumeUl": 5.0,
      "staplePoolVolumeUl": 5.0
    }
  }
  ```

---

## 8. Pharmacogenomics & Genetic Screening

### `GET /api/genomics/markers`
Retrieves the catalog of clinically characterized pharmacogenomic and hemostatic markers.

* **Method**: `GET`
* **Response Status**: `200 OK`
* **Response Payload (JSON)**:
  ```json
  [
    {
      "markerId": "F5_LEIDEN",
      "geneSymbol": "F5",
      "variantName": "Factor V Leiden (rs6025, c.1691G>A, p.Arg506Gln)",
      "category": "ThromboticRisk",
      "severity": "Critical",
      "observedGenotype": "G/A (Heterozygous Carrier)",
      "clinicalDescription": "Resistance to Activated Protein C (APC) prevents inactivation of clotting Factor Va...",
      "recommendedAction": "In MtF regimens, cap peak 17β-estradiol setpoint to <= 500 pmol/L...",
      "associatedRegimen": "MaleToFemale",
      "clearanceRateMultiplier": 1.0,
      "targetKdOffsetNm": 0.0,
      "maxSafeEstradiolPmolL": 500.0,
      "thrombosisRiskMultiplier": 4.2
    }
  ]
  ```

---

### `GET /api/genomics/presets`
Retrieves standard clinical genetic test panels for rapid patient profiling.

* **Method**: `GET`
* **Response Status**: `200 OK`

---

### `POST /api/genomics/analyze`
Evaluates patient genetic markers and generates severity-tiered call-out alerts and kinetic modifiers.

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "patientId": "PATIENT-001",
    "markerIds": ["F5_LEIDEN", "COMT_VAL158MET"],
    "regimen": "MaleToFemale"
  }
  ```
* **Response Status**: `200 OK`
* **Response Payload**:
  ```json
  {
    "patientId": "PATIENT-001",
    "regimen": "MaleToFemale",
    "alerts": [
      {
        "alertId": "ALERT_F5_LEIDEN",
        "geneSymbol": "F5",
        "severity": "Critical",
        "category": "ThromboticRisk",
        "title": "F5: Factor V Leiden",
        "callOutMessage": "[CRITICAL] Resistance to Activated Protein C...",
        "clinicalGuidance": "Cap peak 17β-estradiol setpoint to <= 500 pmol/L."
      }
    ],
    "thrombosisRiskIndex": 5.04,
    "overallClearanceMultiplier": 0.75,
    "recommendedMaxEstradiolPmolL": 500.0
  }
  ```

---

### `POST /api/genomics/analyze-sequence`
Scans raw nucleotide sequences for CpG dinucleotide density (TLR9 immunogenicity) and tandem CAG repeats (AR sensitivity).

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "patientId": "ANON-SEQ",
    "sequence": "5'-CGCGCGCGCGCGCGCGCGCG-3'",
    "regimen": "MaleToFemale"
  }
  ```
* **Response Status**: `200 OK`

---

### `POST /api/genomics/apply-adjustments`
Evaluates markers and applies personalized setpoints and kinetic modifiers directly to the active endocrine engine.

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "patientId": "PATIENT-001",
    "markerIds": ["F5_LEIDEN"],
    "regimen": "MaleToFemale"
  }
  ```
* **Response Status**: `200 OK`

---

## 9. In-Silico Simulation Campaigns

### `GET /api/simulations/campaigns`
Retrieves metadata, biophysical goals, and hardware accelerator specifications for all four in-silico simulation campaigns.

* **Method**: `GET`
* **Response Status**: `200 OK`
* **Response Payload (JSON)**:
  ```json
  [
    {
      "campaignId": "CAMPAIGN_SERUM_STABILITY",
      "name": "Campaign 1: oxDNA Physiological Serum & Low-Magnesium Stress",
      "simulationEngine": "oxDNA (OpenCL / SYCL)",
      "description": "Models structural fluctuations (RMSF) of DNA origami nanostructures at 37°C in human serum salt conditions (150 mM NaCl, 0.8 mM Mg2+).",
      "physicsGoal": "Verify that staple crossovers do not fray or melt under physiological ionic stress before ordering oligos.",
      "keyParameters": ["RMSF (< 1.2 nm)", "Melting Point Shift", "Crossover Fraying Count", "0.8 mM Mg2+ Tolerance"],
      "status": "Ready",
      "configFileTemplate": "oxdna_serum_stability.in"
    }
  ]
  ```

### `POST /api/simulations/run/serum-stability`
Executes Campaign 1: oxDNA physiological serum stability analysis calculating RMSF, Debye screening length, and crossover fraying.

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "scaffoldName": "Bacteriophage M13mp18 (7,249 nt)",
    "temperatureCelsius": 37.0,
    "monovalentSaltMolar": 0.15,
    "divalentMagnesiumMilliMolar": 0.8,
    "stapleCrossoverCount": 180,
    "pllPegCoatingApplied": true,
    "cpGMethylationApplied": true
  }
  ```
* **Response Status**: `200 OK`
* **Response Payload**:
  ```json
  {
    "scaffoldName": "Bacteriophage M13mp18 (7,249 nt)",
    "temperatureCelsius": 37.0,
    "divalentMagnesiumMilliMolar": 0.8,
    "averageRmsfNm": 0.82,
    "maxCrossoverRmsfNm": 1.15,
    "debyeScreeningLengthNm": 0.78,
    "structuralIntegrityScore": 0.94,
    "compromisedCrossoverCount": 0,
    "totalCrossoverCount": 180,
    "verdict": "Structurally Stable Under Physiological Ionic Stress",
    "recommendedStabilization": "Lattice is fully passivated and rigid. Safe to proceed to Campaign 2 (Aptamer Gating)."
  }
  ```

### `POST /api/simulations/run/aptamer-gating`
Executes Campaign 2: LAMMPS Steered MD and umbrella sampling computing unbinding free energy and pituitary GnRH-R affinity.

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "targetReceptor": "Anterior Pituitary Gonadotrope (GnRH-R)",
    "targetGonadotropin": "Luteinizing Hormone (LH)",
    "umbrellaSamplingWindows": 24,
    "reactionCoordinateMaxNm": 3.0
  }
  ```
* **Response Status**: `200 OK`

### `POST /api/simulations/run/payload-leakage`
Executes Campaign 3: Steroid payload cavity confinement measuring passive leakage in closed state and active release when opened.

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "chassisGeometry": "Hexagonal Nanotube (6-HB)",
    "hormoneMolecule": "17β-Estradiol",
    "cavityFunctionalization": "Cyclodextrin Anchored",
    "loadedMoleculeCount": 50
  }
  ```
* **Response Status**: `200 OK`

### `POST /api/simulations/run/pbpk-endocrine`
Executes Campaign 4: Quantitative Systems Pharmacology (QSP) whole-body 5-compartment PBPK simulation over 30/60/90 days with patient genomic modifiers.

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "patientId": "SUBJECT-001",
    "regimen": "MaleToFemale",
    "depotDoseMg": 25.0,
    "simulationDurationDays": 90,
    "deliveryIntervalDays": 180,
    "genomicMarkerIds": ["F5_LEIDEN", "COMT_VAL158MET"]
  }
  ```
* **Response Status**: `200 OK`

### `GET /api/simulations/configs/{campaignId}`
Exports runnable oxDNA or LAMMPS input deck scripts formatted for GPU/SYCL hardware execution.

* **Method**: `GET`
* **Response Status**: `200 OK`

---

## 10. Post-Quantum Cryptography & Regulatory Compliance

### `GET /api/compliance/status`
Returns active lattice cryptography ciphers (NIST FIPS 203 ML-KEM-768, FIPS 204 ML-DSA-65) and regulatory compliance posture.

* **Method**: `GET`
* **Response Status**: `200 OK`
* **Response Payload (JSON)**:
  ```json
  {
    "encryptionAtRestAlgorithm": "ML-KEM-768 + AES-256-GCM (NIST FIPS 203)",
    "auditSignatureAlgorithm": "ML-DSA-65 (NIST FIPS 204)",
    "isEncryptionAtRestActive": true,
    "isAuditLedgerIntact": true,
    "auditRecordCount": 42,
    "hipaaSafeHarborEnforced": true,
    "gdprArticle9And17Active": true,
    "ginaNonDiscriminationVerified": true,
    "totalSubjectsRegistered": 5,
    "cryptoShreddedSubjectCount": 1,
    "overallStatus": "Compliant",
    "generatedAtUtc": "2026-10-20T22:30:00.000Z"
  }
  ```

---

### `GET /api/compliance/audit-ledger`
Retrieves FDA 21 CFR Part 11 cryptographic hash-chained audit events.

* **Method**: `GET`
* **Query Parameters**: `limit` (optional integer, default 100, clamped 1-1000)
* **Response Status**: `200 OK`

---

### `GET /api/compliance/verify-ledger`
Executes mathematical SHA-384 hash-chain dependency and ML-DSA-65 signature verification across the entire audit ledger.

* **Method**: `GET`
* **Response Status**: `200 OK`
* **Response Payload**:
  ```json
  {
    "isValid": true,
    "totalRecordsVerified": 42,
    "message": "All 42 audit records verified cleanly with valid SHA-384 hashes and ML-DSA-65 signatures.",
    "firstViolationSequence": null
  }
  ```

---

### `POST /api/compliance/de-identify`
Sanitizes raw patient and clinical FASTA headers pursuant to HIPAA Safe Harbor (§ 164.514) and enforces GDPR Article 9 & GINA purpose limitation consent.

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "patientIdentifier": "Jane Doe (Hospital-MRN-98432)",
    "rawData": ">Patient: Jane Doe, MRN: 98432\nATGCGATCGATCGATC",
    "gdprConsentConfirmed": true,
    "ginaPurposeLimited": true
  }
  ```
* **Response Status**: `200 OK`
* **Response Payload**:
  ```json
  {
    "pseudonymSubjectId": "sub_a1b2c3d4e5f67890",
    "sanitizedData": ">Patient: sub_a1b2c3d4e5f67890, MRN: [DE-IDENTIFIED-MRN]\nATGCGATCGATCGATC",
    "strippedIdentifiers": ["Jane Doe", "MRN: 98432"],
    "gdprConsentConfirmed": true,
    "ginaSafeguardVerified": true
  }
  ```

---

### `POST /api/compliance/crypto-shred`
Permanently destroys a subject's private ML-KEM-768 decryption key pursuant to GDPR Article 17 ("Right to Erasure").

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "subjectId": "sub_a1b2c3d4e5f67890",
    "operatorId": "DATA_PROTECTION_OFFICER"
  }
  ```
* **Response Status**: `200 OK`
* **Response Payload**:
  ```json
  {
    "subjectId": "sub_a1b2c3d4e5f67890",
    "shredded": true,
    "message": "Subject sub_a1b2c3d4e5f67890 ML-KEM-768 private key zeroized pursuant to GDPR Article 17."
  }
  ```

---

### `POST /api/compliance/encrypt-record`
Encrypts health or endocrine records using the subject's post-quantum ML-KEM-768 public key with AES-256-GCM bulk encryption.

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "subjectId": "sub_a1b2c3d4e5f67890",
    "recordPayload": {
      "regimen": "FemaleToMale",
      "targetTestosteroneNmolL": 20.0
    }
  }
  ```
* **Response Status**: `200 OK`

---

### `POST /api/compliance/decrypt-record`
Decrypts an authenticated post-quantum envelope using the subject's private ML-KEM-768 key.

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "subjectId": "sub_a1b2c3d4e5f67890",
    "envelope": {
      "algorithm": "ML-KEM-768+AES-256-GCM",
      "keyId": "sub_a1b2c3d4e5f67890",
      "encapsulatedKeyBase64": "...",
      "initializationVectorBase64": "...",
      "authenticationTagBase64": "...",
      "ciphertextBase64": "...",
      "createdAtUtc": "2026-10-20T22:35:00.000Z"
    }
  }
  ```
* **Response Status**: `200 OK`

---

### `POST /api/datasets/export-encrypted`
Exports an authenticated `.enc.tar.gz` trajectory bundle encrypted with ML-KEM-768 + AES-256-GCM and signed with ML-DSA-65.

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "trajectoryFileName": "dna_nanotube_sample.xyz",
    "subjectId": "sub_a1b2c3d4e5f67890",
    "destinationDirectory": "simulations/exports"
  }
  ```
* **Response Status**: `200 OK`

---

## 11. Authentication & Sovereign User Management Endpoints

The authentication subsystem provides public-key challenge-response authentication, role authorization (`Administrator`, `Researcher`, `Patient`), transport access boundaries (`Api`, `Web`, `ApiWeb`), and master setup locking.

### `GET /api/auth/setup-status`
Checks whether initial master Administrator setup is required on this node.

* **Method**: `GET`
* **Response Status**: `200 OK`
* **Response Payload (JSON)**:
  ```json
  {
    "isSetupRequired": false,
    "hasAnyUsers": true,
    "hasAdministrator": true
  }
  ```

---

### `POST /api/auth/setup`
Initializes the master Administrator identity. Permanently locked once an Administrator account exists.

* **Method**: `POST`
* **Request Payload (JSON)**:
  ```json
  {
    "username": "admin_primary",
    "publicKeyPemOrBase64": "-----BEGIN PUBLIC KEY-----\nMC4CAQAwBQYDK2VwBCIEIA...\n-----END PUBLIC KEY-----",
    "algorithm": "Ed25519",
    "accessGate": "ApiWeb"
  }
  ```
* **Response Status**: `200 OK`

---

### `POST /api/auth/challenge`
Generates a cryptographically random, ephemeral 256-bit challenge for public key authentication.

* **Method**: `POST`
* **Request Payload (JSON)**:
  ```json
  {
    "username": "lead_researcher"
  }
  ```
* **Response Status**: `200 OK`
* **Response Payload (JSON)**:
  ```json
  {
    "challengeId": "ch_7a8b9c0d1e2f3456",
    "challengeBase64": "vj08kQW9fE+3K...==",
    "algorithm": "Ed25519",
    "expiresAtUtc": "2026-10-21T13:35:00.000Z"
  }
  ```

---

### `POST /api/auth/verify`
Verifies the cryptographic signature over the challenge and issues an authenticated session token.

* **Method**: `POST`
* **Request Payload (JSON)**:
  ```json
  {
    "challengeId": "ch_7a8b9c0d1e2f3456",
    "signatureBase64": "MEQCID...=="
  }
  ```
* **Response Status**: `200 OK`

---

### `GET /api/users`
Retrieves all registered user accounts. Requires Administrator role.

* **Method**: `GET`
* **Response Status**: `200 OK`
* **Response Payload (JSON)**:
  ```json
  [
    {
      "username": "admin_primary",
      "role": "Administrator",
      "accessGate": "ApiWeb",
      "algorithm": "Ed25519",
      "publicKeyBase64": "MC4CAQAwBQYDK2VwBCIEIA==",
      "isActive": true,
      "createdAtUtc": "2026-10-21T08:30:00.000Z",
      "lastLoginUtc": "2026-10-21T13:05:00.000Z"
    }
  ]
  ```

---

### `POST /api/users`
Creates a new user account with specified role and access gate. Requires Administrator role.

* **Method**: `POST`
* **Request Payload (JSON)**:
  ```json
  {
    "username": "patient_0042",
    "role": "Patient",
    "accessGate": "Web",
    "algorithm": "ECDSA_P256",
    "publicKeyPemOrBase64": "-----BEGIN PUBLIC KEY-----\nMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE...\n-----END PUBLIC KEY-----"
  }
  ```
* **Response Status**: `201 Created`

---

### `DELETE /api/users/{username}`
Revokes and deactivates a user account. Master Administrator cannot be deleted.

* **Method**: `DELETE`
* **Response Status**: `200 OK`

---

## 12. Secure Exception Handling & RFC 7807 ProblemDetails

All API error responses conform to **RFC 7807** Problem Details specifications with guaranteed zero-sensitive-data disclosure.

### Content Negotiation
Clients receive errors in either JSON or XML:
* `Accept: application/problem+json` (or `application/json`)
* `Accept: application/problem+xml` (or `application/xml`)

### Example RFC 7807 Error Payloads

<!-- tabs:start -->
#### **JSON (`application/problem+json`)**
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Not Found",
  "status": 404,
  "detail": "Trajectory dataset 'missing_file.xyz' not found in incoming queue.",
  "instance": "/api/trajectories/missing_file.xyz",
  "traceId": "00-a1b2c3d4e5f67890-123456789abcdef0-00",
  "timestamp": "2026-10-21T13:30:00.000Z"
}
```

#### **XML (`application/problem+xml`)**
```xml
<?xml version="1.0" encoding="utf-8"?>
<ProblemDetails>
  <Type>https://tools.ietf.org/html/rfc9110#section-15.5.5</Type>
  <Title>Not Found</Title>
  <Status>404</Status>
  <Detail>Trajectory dataset 'missing_file.xyz' not found in incoming queue.</Detail>
  <Instance>/api/trajectories/missing_file.xyz</Instance>
  <TraceId>00-a1b2c3d4e5f67890-123456789abcdef0-00</TraceId>
  <Timestamp>2026-10-21T13:30:00.000Z</Timestamp>
</ProblemDetails>
```
<!-- tabs:end -->

### Security Sanitization Guarantees
* **Path Masking**: System paths are replaced with `[REDACTED_PATH]`.
* **Password Stripping**: Connection string passwords and sensitive query parameters are masked as `[REDACTED]`.
* **Private Key Redaction**: PEM private keys are converted to `[REDACTED_KEY]`.
* **Internal Server Errors (500)**: Masked behind generic descriptions and correlation trace IDs, preventing database stack traces or SQL fragments from leaking.

---

## 13. Automated Test Suite Execution & Coverage Gate

We mandate a 100% test pass rate and 100.00% line and branch code coverage across both .NET and JavaScript codebases.

### Universal Pre-Flight Verification Script
Run the automated verification gate before pushing changes:
```bash
./scripts/verify-all.sh
```
This script executes:
1. `dotnet format --verify-no-changes` (Zero formatting errors)
2. `npm test` (100% line, branch, and function JS coverage)
3. `dotnet test` (100% .NET unit and integration test pass rate)
4. Cobertura coverage gate (100.00% Line and 100.00% Branch threshold)
5. Markdown delimiter scan (Zero unescaped single-dollar LaTeX tokens)

---

## Next Steps
* Return to the [**Portal Home**](index.md).
* Review practical use cases in [**Example Biomedical Workflows**](example-workflows.md).
* Explore physical synthesis in [**DIY & HomeLab Fabrication**](diy-fabrication-guide.md).
