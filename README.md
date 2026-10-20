# dotnet-weaver-lammps-hrt (weaver-lammps-hrt)

A sovereign, open-source computational platform for synthetic biology and DNA origami research. This repository provides:
1. **A Sovereign .NET 10 Microservice**: Zero-allocation trajectory parser (`System.IO.Pipelines`, `ReadOnlySpan<byte>`, `Utf8Parser`) capable of streaming massive molecular simulation trajectories (`.xyz`, `.dcd`, oxDNA).
2. **Sovereign Web Dashboard**: Built-in UI for real-time memory telemetry, interactive stream parsing, and cryptographically verified dataset export/import with SHA-256 provenance.
3. **Multi-Hardware Simulation Stack**: Self-contained Docker Compose infrastructure running oxDNA and LAMMPS with out-of-the-box support for Intel iGPUs, with drop-in overrides for **Intel Arc A770**, **NVIDIA CUDA**, and **AMD Radeon ROCm**.
4. **Automated CI/CD**: GitHub Actions workflow that builds, tests, and publishes container images to GitHub Container Registry (`ghcr.io`).

---

## 🚀 Quickstart

### 1. Run the Default Stack (Intel iGPU & CPU)
By default, the platform boots with support for Intel integrated graphics (Iris Xe / UHD via `/dev/dri`) and standard CPUs:
```bash
docker compose up -d
```
Visit the Sovereign Dashboard at **`http://localhost:5000`**.

### 2. Run with Hardware Overrides

#### Discrete Intel Arc GPU (e.g., A770)
Enables Level Zero / SYCL hardware acceleration and scales shared memory to 16GB:
```bash
docker compose -f docker-compose.yml -f compose/docker-compose.arc.yml up -d
```

#### NVIDIA Workstations / Servers (CUDA)
Utilizes NVIDIA Container Toolkit and CUDA/OpenCL:
```bash
docker compose -f docker-compose.yml -f compose/docker-compose.nvidia.yml up -d
```

#### AMD Radeon Systems (ROCm)
Utilizes the Kernel Fusion Driver (`/dev/kfd`):
```bash
docker compose -f docker-compose.yml -f compose/docker-compose.radeon.yml up -d
```

---

## 📊 Sovereign Web Dashboard (`http://localhost:5000`)

The embedded dashboard provides:
- **Zero-Allocation Telemetry**: Real-time monitoring of GC Generation 0, 1, and 2 collections to ensure the zero-allocation invariant is maintained during multi-gigabyte parsing runs.
- **Interactive Trajectory Slicing**: Instantly trigger the streaming pipeline on files in `/workspace/data` and inspect coordinate throughput (frames/sec and atom counts).
- **Cryptographic Dataset Export**: Package trajectories into compressed `.tar.gz` bundles with an automated, immutable `provenance.manifest.json` featuring SHA-256 digest validation.
- **Peer Dataset Import**: Drag-and-drop peer archives; the server validates SHA-256 integrity before unpacking into the simulation volume.

---

## 🛠️ Local Development & Testing

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/)
- [Docker Engine & Docker Compose](https://docs.docker.com/engine/install/)

### Run the Parser & Web Dashboard Locally
```bash
dotnet run --project src/WeaverLammpsHrt.TrajectoryParser/WeaverLammpsHrt.TrajectoryParser.csproj
```

### Run Test Suite
```bash
# Run all unit tests across parsers and cryptographic dataset services
dotnet test dotnet-weaver-lammps-hrt.sln
```

---

## 📖 Standards & AI Contributor Instructions

- **[CODING_STANDARDS.md](CODING_STANDARDS.md)**: The "Zero-Ignorance" Open-Bio documentation protocol, requiring 100% docblocks on all members (including private) and the "What / Why / How" commenting triad.
- **[AGENTS.md](AGENTS.md)**: Universal instructions for AI coding assistants (Claude, Cursor, Copilot, Gemini, ChatGPT).
- **[.cursorrules](.cursorrules)**: IDE rules for Cursor users.
- **[.github/copilot-instructions.md](.github/copilot-instructions.md)**: Context guidelines for GitHub Copilot.
