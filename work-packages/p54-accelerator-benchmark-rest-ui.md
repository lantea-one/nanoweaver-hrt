# Work Package P54: Hardware Accelerator Benchmark REST API & Diagnostic UI

## 1. Context & Objective
- **Problem Statement**:
  With `IAcceleratorBenchmarkService` implemented in P53, cluster administrators and researchers need an authenticated REST API to trigger hardware qualification runs and a visual diagnostic dashboard card on the web UI to inspect compute performance, thermal safety, and simulation readiness before launching workloads.
- **Goal & Scope**:
  1. Define REST API contracts:
     - `POST /api/system/benchmark/run` (triggers on-demand accelerator qualification benchmark)
     - `GET /api/system/benchmark/latest` (retrieves the cached latest benchmark report)
     - Role-gated: `Administrator` or `Researcher`.
     - RFC 7807 problem details error mapping.
  2. Implement Web UI Card:
     - Add visual Accelerator Qualification card to `src/NanoWeaverHrt/Components/Pages/StackServices.razor`.
     - High-contrast badges for readiness: `Go` (green), `Marginal` (amber), `Insufficient` (crimson).
     - GFLOPS throughput meters (FP32, FP64) and memory bandwidth bar (GB/s).
     - Single-click "Run Benchmark" button with spinner state.
  3. Author unit and bUnit tests:
     - Integration tests in `tests/NanoWeaverHrt.Tests/EndpointIntegrationTests.cs`.
     - bUnit component tests covering rendering, state transitions, and button clicks.
     - 100.00% Line & Branch coverage.
- **Target Role / Model**: `coder`
- **Transport Boundary**: REST API & Web UI

---

## 2. Technical Contracts & Invariants
- **Endpoints**:
  - `POST /api/system/benchmark/run`
  - `GET /api/system/benchmark/latest`
- **Security**:
  - Transport boundary: `ApiWeb`
  - Roles: `Researcher`, `Administrator`
- **Invariants**:
  - Zero captured outer variables in minimal API endpoint registrations.
  - Append-order invariant for minimal API mappings in `Program.cs`.
  - 100% XML docblock coverage across all C# code.
  - 100.00% Line & Branch test coverage.
