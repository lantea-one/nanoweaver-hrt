# Simulation Shared Data Directory

This directory serves as the high-throughput shared volume mounted between:
1. Molecular dynamics simulation engines (LAMMPS and oxDNA in container runtime)
2. The .NET 10 trajectory pipeline service (`NanoWeaverHrt`)

## Data Ingestion
- Simulation outputs (`.xyz`, `.dcd`, `.lammpstrj`, `.dump`, `.custom`) are placed in this directory during execution.
- This directory is gitignored to prevent large multi-gigabyte simulation trajectories from polluting the source code repository.
