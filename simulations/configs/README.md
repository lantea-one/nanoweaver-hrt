# Molecular Dynamics Simulation Configurations (`simulations/configs`)

This directory houses input decks, parameter definitions, and execution protocols for molecular dynamics (MD) simulation engines (LAMMPS and oxDNA) driving the NanoWeaver synthetic biology platform.

---

## 1. Directory Purpose & Mount Topology

In the NanoWeaver container architecture (`docker/simulation/Dockerfile.simulation` and `compose/docker-compose.yml`), simulation engines run within isolated compute containers equipped with hardware-accelerated OpenCL, SYCL, Level Zero, or CUDA drivers.

### Container Mount Mapping
- **Host Directory**: `simulations/configs/`
- **Container Path**: `/workspace/configs:ro` (mounted read-only into simulation containers to ensure simulation inputs are strictly deterministic and reproducible).
- **Trajectory Output Path**: Output trajectories, energy logs, and frame dumps are directed to `/workspace/data/` (mapped to `simulations/shared-data/` on the host).

---

## 2. Configuration Inventory

| Configuration File | Engine | Scale / Method | Biomedical Objective |
| :--- | :--- | :--- | :--- |
| `oxdna_input` | oxDNA 2.4 | Coarse-Grained DNA Model | Foundational benchmark template for DNA duplex folding and staple crossover thermodynamics. |
| `oxdna_serum_stability.in` | oxDNA 2.4 | Coarse-Grained (OpenCL/SYCL) | Physiological serum stability verification under human blood ionic conditions (37.0°C, 150 mM NaCl, 0.8 mM Mg²⁺) to detect crossover fraying or premature denaturation. |
| `lammps_aptamer_gating.in` | LAMMPS | Atomistic Steered MD (SMD) | Steered MD and umbrella sampling to compute the Potential of Mean Force (PMF) for aptamer latch opening; verifies nanocarrier remains locked (ΔG ≥ +8.5 kcal/mol) until triggered by LH/FSH receptor docking. |
| `lammps_steroid_payload.in` | LAMMPS | Atomistic NPT Ensemble | Steroid hormone confinement and micro-leakage dynamics of 17β-estradiol and testosterone inside DNA nanocage cavities with explicit TIP3P water solvation. |
| `minidna.in` | LAMMPS | Atomistic Benchmark | Minimalist DNA double-helix sanity deck for rapid engine verification across CPU and GPU hardware backends. |

---

## 3. Scientific Invariants & Simulation Parameters

### A. Thermodynamic Environments
- **Body Temperature**: Set to physiological human core temperature: `310.15 K` (`37.0°C`).
- **Serum Ionic Environment**: Human systemic circulation maintains approximately `150 mM NaCl` and `0.8 mM Mg²⁺`. oxDNA simulations evaluate structural stability under low magnesium concentration (0.8 mM Mg²⁺) to ensure staples do not dissociate in blood prior to reaching target tissues.
- **Aptamer Gating Barrier**: Aptamer latch mechanisms require a minimum free energy barrier of `ΔG ≥ +8.5 kcal/mol` in the basal unliganded state to eliminate off-target endocrine hormone release.

### B. Supported Accelerator Backends
- **oxDNA**:
  - `backend = OpenCL` (Default for Intel integrated GPUs, Intel Arc discrete GPUs, and AMD ROCm).
  - `backend = CUDA` (Used when running with NVIDIA GPU overlays: `compose/docker-compose.nvidia.yml`).
  - `backend_precision = mixed` (Balancing 32-bit single-precision acceleration with 64-bit coordinate integration stability).
- **LAMMPS**:
  - Accelerated via KOKKOS / OpenCL / GPU packages with Verlet time integration (0.5 to 2.0 fs timesteps).

---

## 4. Execution Guidelines

### Running Standalone Verification Simulations
To run a simulation manually inside the simulation container:

```bash
# 1. Start the simulation container
docker compose up -d simulation

# 2. Execute an oxDNA serum stability run
docker compose exec simulation oxDNA /workspace/configs/oxdna_serum_stability.in

# 3. Execute a LAMMPS steroid payload confinement simulation
docker compose exec simulation lmp -in /workspace/configs/lammps_steroid_payload.in
```

Parsed coordinate trajectories will stream immediately into `simulations/shared-data/`, where the .NET 10 microservice ingests them via zero-allocation pipelines.
