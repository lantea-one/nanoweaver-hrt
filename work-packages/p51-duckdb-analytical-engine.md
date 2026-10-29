# Work Package P51: DuckDB In-Process Analytical Engine

## 1. Context & Objective
- **Problem Statement**:
  Researchers studying molecular dynamics of DNA origami nanostructures and synthetic endocrine circuits require ad-hoc analytical queries across multi-gigabyte trajectory coordinate frames (RMSD, radius of gyration, velocity distribution, distance histograms). External SQL databases require cumbersome data movement, whereas DuckDB.NET enables zero-copy in-process columnar SQL queries directly over trajectory Parquet files and memory tables.
- **Goal & Scope**:
  1. Define domain interfaces and models in `NanoWeaverHrt.Analytics`:
     - `ITrajectoryAnalyticsService`
     - `TrajectoryAnalyticsQuery` (dataset name, SQL query string, parameters, timeout)
     - `TrajectoryAnalyticsResult` (columns, rows, execution time ms, rows returned, memory bytes scanned)
     - `TrajectorySummaryStatistics` (atom count, frame count, bounding box volume, mean radius of gyration)
  2. Implement `DuckDbTrajectoryAnalyticsService`:
     - In-process analytical session using `DuckDB.NET.Data.Full`.
     - Direct `read_parquet('...')` query resolution pointing to `simulations/parquet/*.parquet`.
     - Query safety: read-only guard (disallow `DROP`, `DELETE`, `INSERT`, `UPDATE`, `ALTER`, `ATTACH`, `COPY`).
     - Zero-allocation parameter mapping and cancellation support.
  3. Author unit tests covering all methods, edge cases, SQL injection / mutation guards, and cancellation with 100.00% Line & Branch coverage.
- **Target Role / Model**: `coder` (`flash` or `inherit`)
- **Transport Boundary**: Analytical Engine

---

## 2. Technical Contracts & Invariants
- **Namespace**: `NanoWeaverHrt.Analytics`
- **Invariants**:
  - Read-only execution guarantee (reject any data-modifying or DDL keywords).
  - 100% XML docblock coverage on every type, method, and field.
  - 100.00% Line & Branch test coverage.
