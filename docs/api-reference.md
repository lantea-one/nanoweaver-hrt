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

## 5. Pharmacogenomics & Genetic Screening Endpoints

### `GET /api/genomics/markers`
Retrieves the catalog of clinically characterized pharmacogenomic and hemostatic markers.

* **Method**: `GET`
* **Response Status**: `200 OK`
* **Response Payload**:
  ```json
  [
    {
      "marker_id": "F5_LEIDEN",
      "gene_symbol": "F5",
      "variant_name": "Factor V Leiden (rs6025, c.1691G>A, p.Arg506Gln)",
      "category": "ThromboticRisk",
      "severity": "Critical",
      "observed_genotype": "G/A (Heterozygous Carrier)",
      "clinical_description": "Resistance to Activated Protein C (APC) prevents inactivation of clotting Factor Va...",
      "recommended_action": "In MtF regimens, cap peak 17β-estradiol setpoint to <= 500 pmol/L...",
      "associated_regimen": "MaleToFemale",
      "clearance_rate_multiplier": 1.0,
      "target_kd_offset_nm": 0.0,
      "max_safe_estradiol_pmol_l": 500.0,
      "thrombosis_risk_multiplier": 4.2
    }
  ]
  ```

---

### `GET /api/genomics/presets`
Retrieves standard clinical genetic test panels for rapid patient profiling.

* **Method**: `GET`
* **Response Status**: `200 OK`
* **Response Payload**:
  ```json
  [
    {
      "preset_id": "PRESET_BASELINE",
      "title": "Standard Cis-Typical Baseline (Zero Risk Alleles)",
      "regimen": "MaleToFemale",
      "marker_ids": []
    },
    {
      "preset_id": "PRESET_MTF_THROMBOSIS",
      "title": "MtF Patient - Factor V Leiden Heterozygote (High Clot Risk)",
      "regimen": "MaleToFemale",
      "marker_ids": ["F5_LEIDEN", "COMT_VAL158MET"]
    }
  ]
  ```

---

### `POST /api/genomics/analyze`
Evaluates patient genetic markers and generates severity-tiered call-out alerts and kinetic modifiers.

* **Method**: `POST`
* **Request Headers**: `Content-Type: application/json`
* **Request Payload**:
  ```json
  {
    "patient_id": "PATIENT-001",
    "marker_ids": ["F5_LEIDEN", "COMT_VAL158MET"],
    "regimen": "MaleToFemale"
  }
  ```
* **Response Status**: `200 OK`
* **Response Payload**:
  ```json
  {
    "patient_id": "PATIENT-001",
    "regimen": "MaleToFemale",
    "observed_markers": ["..."],
    "alerts": [
      {
        "alert_id": "ALERT_F5_LEIDEN_...",
        "gene_symbol": "F5",
        "severity": "Critical",
        "category": "ThromboticRisk",
        "title": "F5: Factor V Leiden (rs6025, c.1691G>A, p.Arg506Gln)",
        "call_out_message": "[CRITICAL] Resistance to Activated Protein C (APC)...",
        "clinical_guidance": "In MtF regimens, cap peak 17β-estradiol setpoint to <= 500 pmol/L...",
        "kinetic_guidance": "Limit peak hormone burst; cap E2 to <= 500 pmol/L."
      }
    ],
    "thrombosis_risk_index": 5.04,
    "overall_clearance_multiplier": 0.75,
    "cpg_methylation_mandatory": false,
    "recommended_max_estradiol_pmol_l": 500.0,
    "recommended_min_testosterone_nmol_l": null,
    "recommended_kd_offset_nm": 0.0
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
    "patient_id": "ANON-SEQ",
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
    "patient_id": "PATIENT-001",
    "marker_ids": ["F5_LEIDEN"],
    "regimen": "MaleToFemale"
  }
  ```
* **Response Status**: `200 OK`
* **Response Payload**:
  ```json
  {
    "genomic_profile": { "patient_id": "PATIENT-001", "..." : "..." },
    "adjusted_endocrine_profile": {
      "target_estradiol_pmol_l": 500.0,
      "target_testosterone_nmol_l": 1.20,
      "..." : "..."
    }
  }
  ```

---

## 6. Post-Quantum Cryptography & Regulatory Compliance Endpoints

### `GET /api/compliance/status`
Returns the operational health, active lattice cryptography ciphers, and regulatory compliance posture.

* **Method**: `GET`
* **Response Status**: `200 OK`
* **Response Payload**:
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
Retrieves the most recent FDA 21 CFR Part 11 cryptographic hash-chained audit events.

* **Method**: `GET`
* **Query Parameters**: `limit` (optional integer, default 100, clamped 1-1000)
* **Response Status**: `200 OK`
* **Response Payload**:
  ```json
  [
    {
      "sequenceNumber": 1,
      "timestampUtc": "2026-10-20T22:31:00.000Z",
      "previousHash": "384_bit_hex_hash...",
      "currentHash": "384_bit_hex_hash...",
      "operatorId": "USER_RESEARCHER",
      "action": "ScreenGenomics",
      "resource": "scaffold_01",
      "detailsJson": "{\"gc\":42.5}",
      "signature": {
        "algorithm": "ML-DSA-65",
        "keyId": "node_signing_master",
        "signatureBase64": "base64_sig..."
      }
    }
  ]
  ```

---

### `GET /api/compliance/verify-ledger`
Executes complete mathematical SHA-384 hash-chain dependency and ML-DSA-65 signature verification across the entire audit ledger.

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
Encrypts arbitrary strongly-typed health or endocrine records using the subject's post-quantum ML-KEM-768 public key.

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
* **Response Payload**:
  ```json
  {
    "algorithm": "ML-KEM-768+AES-256-GCM",
    "keyId": "sub_a1b2c3d4e5f67890",
    "encapsulatedKeyBase64": "...",
    "initializationVectorBase64": "...",
    "authenticationTagBase64": "...",
    "ciphertextBase64": "...",
    "createdAtUtc": "2026-10-20T22:35:00.000Z"
  }
  ```

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
* **Response Payload**:
  ```json
  {
    "subjectId": "sub_a1b2c3d4e5f67890",
    "recordPayload": {
      "regimen": "FemaleToMale",
      "targetTestosteroneNmolL": 20.0
    }
  }
  ```

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
* **Response Payload**:
  ```json
  {
    "bundlePath": "simulations/exports/dna_nanotube_sample.xyz.sub_a1b2c3d4e5f67890.20261020_223500.enc.tar.gz",
    "message": "Dataset successfully encrypted with ML-KEM-768 + AES-256-GCM."
  }
  ```

---

## 7. Automated Test Suite Execution

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
