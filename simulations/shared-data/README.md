# Simulation Shared Data Directory (`simulations/shared-data`)

This directory serves as the high-throughput shared data volume mounted between the containerized molecular dynamics simulation engine (`weaver-simulation`) and the .NET 10 trajectory streaming parser microservice (`weaver-parser`).

---

## 1. Directory Purpose & Architecture

The shared-data volume enables zero-copy, pipeline-driven communication between scientific simulation executables and downstream analytical pipelines:
1. **Simulation Output Target**: The simulation daemon executes oxDNA or LAMMPS workloads inside `weaver-simulation`, writing raw atomic coordinate trajectories directly into this directory (`/workspace/data`).
2. **Real-Time Streaming Ingestion**: The .NET 10 service (`NanoWeaverHrt`) monitors this directory, streaming newly written or historical trajectory frames directly through a zero-allocation `System.IO.Pipelines.PipeReader` loop.
3. **Columnar Conversion & Storage**: Parsed coordinates are projected into columnar Apache Parquet files in `simulations/parquet/` and made queryable in PostgreSQL via `pg_duckdb`.

---

## 2. Supported Trajectory File Formats

The parser automatically detects and processes the following molecular dynamics trajectory formats:

| Format Extension | Generator Engine | Description & Parser Behavior |
| :--- | :--- | :--- |
| `.xyz` | oxDNA / LAMMPS / VMD | Standard ASCII Cartesian coordinates (`AtomCount`, header/comment, `Symbol X Y Z`). Parsed line-by-line via `Utf8Parser` without string heap allocation. |
| `.dcd` | LAMMPS / CHARMM / NAMD | High-density binary trajectory format with fixed-length Fortran header blocks and 32-bit single-precision floating point coordinate arrays. |
| `.dump` / `.lammpstrj` | LAMMPS | Standard LAMMPS ASCII atom dump format (`ITEM: TIMESTEP`, `ITEM: NUMBER OF ATOMS`, `ITEM: BOX BOUNDS`, `ITEM: ATOMS id type x y z`). |
| `.custom` | oxDNA / LAMMPS | Tabular simulation logs containing thermodynamic variables (potential energy, kinetic energy, temperature, pressure, RMSD). |

---

## 3. Storage and Repository Invariants

- **Git Exclusion**: Trajectory files can grow to tens or hundreds of gigabytes during long simulation runs. All simulation output extensions (`*.xyz`, `*.dcd`, `*.dump`, `*.lammpstrj`, `*.custom`) are ignored by `.gitignore`.
- **Sample Files**: Small synthetic baseline trajectory samples (`dna_sample.dump`, `dna_sample.dcd`, `dna_nanotube_sample.xyz`) are tracked alongside `.gitkeep` for deterministic integration testing and end-to-end benchmark validation.
- **File Ownership**: When launched with Docker Compose, containers map host user credentials (`WEAVER_UID:WEAVER_GID`, defaulting to `1000:1000`), ensuring output files are directly accessible and manageable on the host without root escalation.
