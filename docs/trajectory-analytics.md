# Trajectory Columnar Analytics & In-Process DuckDB SQL Engine

> **Vectorized, Zero-Copy Columnar SQL Analytics Across Molecular Trajectory Datasets**

Simulating nanoscale DNA origami carriers and synthetic hormone gene circuits yields massive coordinate streams (tens of thousands of atoms across hundreds of thousands of simulation steps). Traditional row-based relational engines or text-based CSV/XYZ parsers are too slow for real-time interactive exploration.

NanoWeaverHrt pairs **Apache Parquet columnar storage** with an embedded **DuckDB analytical SQL engine**, providing sub-millisecond query evaluation, SIMD vectorization, and zero-allocation frame slicing.

---

## 1. Architectural Overview

```
 +-------------------------------------------------------------------------+
 |                      Molecular Simulation Engines                       |
 |             oxDNA (.dcd)  /  LAMMPS (.xyz, custom dump)                 |
 +-----------------------------------+-------------------------------------+
                                     |
                                     v
 +-------------------------------------------------------------------------+
 |             Zero-Allocation SIMD Parser (System.IO.Pipelines)           |
 +-----------------------------------+-------------------------------------+
                                     |
                                     v
 +-------------------------------------------------------------------------+
 |            Columnar Parquet Serializer (Snappy Compressed)              |
 |                       ./simulations/parquet/*.parquet                   |
 +-----------------------------------+-------------------------------------+
                                     |
                                     v
 +-------------------------------------------------------------------------+
 |              DuckDB Trajectory Analytics Service (Embedded)             |
 |            - Vectorized Execution & SIMD Multi-Threading                |
 |            - Read-Only Security Gating & AST Validation                 |
 +-------------------+---------------------------------+-------------------+
                     |                                 |
                     v                                 v
        +-------------------------+       +-------------------------+
        |   REST API Endpoint     |       |    Web UI Query Drawer  |
        |   POST /api/trajectories|       |    TrajectoryBrowser    |
        |       /analytics/query  |       |        (.razor)         |
        +-------------------------+       +-------------------------+
```

---

## 2. Parquet Schema & Columnar Layout

Each frame of a trajectory dataset is stored as compressed columnar vectors:

| Column Name | Data Type | Physical Unit | Description |
| :--- | :--- | :--- | :--- |
| `frame_index` | `BIGINT` | dimensionless | Simulation snapshot timestep index |
| `atom_id` | `INTEGER` | dimensionless | Unique particle or nucleotide identifier |
| `atom_type` | `VARCHAR` | chemical symbol | Nucleotide or atom type (A, T, G, C, P, N) |
| `x` | `DOUBLE` | Ångströms (Å) | Cartesian X coordinate |
| `y` | `DOUBLE` | Ångströms (Å) | Cartesian Y coordinate |
| `z` | `DOUBLE` | Ångströms (Å) | Cartesian Z coordinate |
| `vx` | `DOUBLE` | Å / fs | Particle velocity in X direction (when enabled) |
| `vy` | `DOUBLE` | Å / fs | Particle velocity in Y direction (when enabled) |
| `vz` | `DOUBLE` | Å / fs | Particle velocity in Z direction (when enabled) |

---

## 3. The Analytical REST API

### `POST /api/trajectories/analytics/query`
Executes an authenticated, read-only SQL analytical query directly against a trajectory Parquet archive or in-memory session.

- **Role Gating**: Requires `Researcher` or `Administrator` role.
- **Transport Boundary**: `ApiWeb` or `Api`.
- **Request Format**:
  ```json
  {
    "datasetName": "dna_nanotube_folding.parquet",
    "query": "SELECT frame_index, AVG(x) AS center_x, AVG(y) AS center_y, AVG(z) AS center_z FROM read_parquet('dna_nanotube_folding.parquet') GROUP BY frame_index ORDER BY frame_index LIMIT 10;",
    "maxRows": 100
  }
  ```
- **Response Format**:
  ```json
  {
    "datasetName": "dna_nanotube_folding.parquet",
    "columns": ["frame_index", "center_x", "center_y", "center_z"],
    "rows": [
      [0, 12.45, -4.20, 8.15],
      [1, 12.48, -4.18, 8.16]
    ],
    "totalRows": 2,
    "elapsedMilliseconds": 1.42
  }
  ```

---

## 4. Example Analytical SQL Queries

### Calculating Center of Mass per Frame
```sql
SELECT
    frame_index,
    AVG(x) AS com_x,
    AVG(y) AS com_y,
    AVG(z) AS com_z
FROM read_parquet('dna_carrier_stability.parquet')
GROUP BY frame_index
ORDER BY frame_index;
```

### Measuring Nanostructure Radius of Gyration
```sql
SELECT
    frame_index,
    SQRT(AVG(POW(x - com_x, 2) + POW(y - com_y, 2) + POW(z - com_z, 2))) AS radius_of_gyration
FROM read_parquet('dna_carrier_stability.parquet')
CROSS JOIN (
    SELECT AVG(x) AS com_x, AVG(y) AS com_y, AVG(z) AS com_z
    FROM read_parquet('dna_carrier_stability.parquet')
)
GROUP BY frame_index
ORDER BY frame_index;
```

### Identifying Thermal Outliers & Fluctuation Extremes
```sql
SELECT
    atom_id,
    atom_type,
    STDDEV_SAMP(x) AS fluc_x,
    STDDEV_SAMP(y) AS fluc_y,
    STDDEV_SAMP(z) AS fluc_z
FROM read_parquet('annealing_45c.parquet')
GROUP BY atom_id, atom_type
ORDER BY (fluc_x + fluc_y + fluc_z) DESC
LIMIT 20;
```

---

## 5. Security & Read-Only Invariants

To prevent accidental data modification or denial-of-service in multi-tenant environments, the `DuckDbTrajectoryAnalyticsService` enforces strict query boundaries:
1. **Mutation Blocking**: Statements containing `INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`, `COPY`, `EXPORT`, or `ATTACH` are parsed and rejected before query dispatch.
2. **Execution Timeout**: Queries exceeding configured timeout limits (default 30 seconds) are automatically terminated with an RFC 7807 problem details response.
3. **Bounded Result Slicing**: Result sets are bounded by `MaxRows` to prevent memory exhaustion in web clients.
