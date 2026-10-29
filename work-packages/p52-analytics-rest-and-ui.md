# Work Package P52: Trajectory Analytics REST API & UI Query Drawer

## 1. Context & Objective
- **Problem Statement**:
  With `ITrajectoryAnalyticsService` and `DuckDbTrajectoryAnalyticsService` implemented in P51, researchers need an authenticated HTTP endpoint and an interactive browser-based SQL drawer to query molecular simulation datasets on-demand from the sovereign Web UI.
- **Goal & Scope**:
  1. Define REST API Request/Response DTO contracts:
     - `TrajectoryAnalyticsApiRequest` (`DatasetName`, `Query`, `MaxRows`)
     - `TrajectoryAnalyticsApiResponse` (`DatasetName`, `Columns`, `Rows`, `TotalRows`, `ElapsedMilliseconds`)
  2. Implement endpoint in `src/NanoWeaverHrt/Program.cs`:
     - `POST /api/trajectories/analytics/query`
     - Role-gated: `Researcher` or `Administrator`.
     - RFC 7807 error handling via `SecureExceptionHandlerMiddleware`.
     - Zero captured outer variables in minimal API lambda.
  3. Implement Blazor component / drawer:
     - `src/NanoWeaverHrt/Components/Pages/TrajectoryAnalyticsDrawer.razor` (or integrate into `TrajectoryBrowser.razor`).
     - Accessible dark-mode compliant SQL input textarea with pre-populated query templates (atom count, frame bounding box, velocity distribution, RMSD).
     - Responsive tabular visualization for returned columnar rows.
  4. Author unit and bUnit tests:
     - Full integration test with `WebApplicationFactory` in `tests/NanoWeaverHrt.Tests/EndpointIntegrationTests.cs`.
     - Component tests maintaining 100.00% Line & Branch coverage.
- **Target Role / Model**: `coder`
- **Transport Boundary**: REST API & Web UI

---

## 2. Technical Contracts & Invariants
- **Endpoint**: `POST /api/trajectories/analytics/query`
  - Input: `{"datasetName": "dna_origami_sample", "query": "SELECT frame_index, count(*) FROM trajectory GROUP BY frame_index LIMIT 10", "maxRows": 100}`
  - Output: `{"datasetName": "...", "columns": ["frame_index", "count"], "rows": [[0, 1500]], "totalRows": 1, "elapsedMilliseconds": 1.25}`
- **Security**:
  - Transport boundary: `ApiWeb`
  - Role: `Researcher`, `Administrator`
- **Invariants**:
  - Zero allocation in JSON formatting loops.
  - Append-order invariant in `Program.cs`.
  - 100% XML docblocks on all types, methods, and properties.
  - 100.00% Line & Branch test coverage.
