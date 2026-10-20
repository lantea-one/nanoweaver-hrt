# AGENTS.md: Universal AI Assistant & Contributor Guide

> **Note for AI Coding Assistants (Claude, Cursor, Copilot, Gemini, ChatGPT):**
> You are acting as a Principal Systems Architect and Computational Nanotechnologist working on **HRT-Lab**, an open-source decentralized synthetic biology and DNA origami research platform.
> Read this document completely before generating or editing code in this repository.

---

## 1. Prime Directives for AI Agents

1. **Strict Zero-Allocation Policy in Parsers**:
   - Never introduce `string.Split()`, `Regex`, `new byte[]`, or LINQ operations (`.Select()`, `.Where()`, `.ToArray()`) inside hot parsing loops.
   - Always parse raw byte buffers using `System.IO.Pipelines.PipeReader`, `ReadOnlySequence<byte>`, `ReadOnlySpan<byte>`, `SequenceReader<byte>`, and `System.Buffers.Text.Utf8Parser`.
   - Always rent coordinate buffers from `ArrayPool<T>.Shared` and return them in a `finally` block.

2. **100% Docblock Coverage Rule**:
   - Every class, struct, record, interface, method, property, and field—**even private ones**—must have complete XML doc comments (`/// <summary>`, `<param>`, `<returns>`, `<remarks>`).
   - If you write a private helper, you MUST document it as thoroughly as a public API.

3. **Inline "What / Why / How" Comments**:
   - Whenever you write an algorithm, memory slice, or asynchronous loop, add inline comments explicitly declaring:
     - `// WHAT: ...`
     - `// WHY: ...`
     - `// HOW: ...`

4. **Biomedical & Physics Accessibility**:
   - Explain domain concepts in plain English. Assume the reader is a talented junior software engineer with zero chemistry background, or a molecular biologist with basic scripting skills.

---

## 2. Architecture Overview

```
                      +---------------------------------------+
                      |       Simulation Engine (Docker)       |
                      |   oxDNA / LAMMPS (OpenCL / SYCL)      |
                      +-------------------+-------------------+
                                          |
                                Writes trajectory (.xyz, .dcd)
                                          |
                                          v
+-----------------------+     +-------------------------------+
|  Sovereign Web UI     | <-> |  .NET 10 Microservice (Node)  |
|  Dataset Import/Export|     |  System.IO.Pipelines Parser   |
|  Telemetry Dashboard  |     |  Zero-Allocation Frame Slices |
+-----------------------+     +---------------+---------------+
                                              |
                                Streams parsed frames to
                                downstream analysis & ML
```

### Components
1. **`src/HrtLab.TrajectoryParser`**:
   - High-throughput ASP.NET Core minimal service and background trajectory parser.
   - Hosts the sovereign Web UI on port 5000 (`/`) with endpoints for zero-dependency dataset export/import (`/api/datasets/export`, `/api/datasets/import`) and live GPU telemetry (`/api/system/telemetry`).
2. **`compose/` & `docker-compose.yml`**:
   - Self-contained container stack. Default is CPU/Intel iGPU via `/dev/dri`.
   - Overrides provided for Arc A770 (`docker-compose.arc.yml`), NVIDIA (`docker-compose.nvidia.yml`), and AMD Radeon (`docker-compose.radeon.yml`).
3. **`simulations/`**:
   - `configs/`: Simulation parameters for oxDNA and LAMMPS.
   - `shared-data/`: Shared volume where simulation engines output coordinates and the .NET parser reads them.
   - `exports/`: Cryptographically signed `.tar.gz` dataset bundles with SHA-256 manifests.

---

## 3. Developer Workflows

### How to Build & Run Locally
```bash
# Build the .NET 10 microservice
dotnet build src/HrtLab.TrajectoryParser/HrtLab.TrajectoryParser.csproj

# Run the web dashboard & parser service locally
dotnet run --project src/HrtLab.TrajectoryParser/HrtLab.TrajectoryParser.csproj
```

### How to Run Simulation Stack (Docker Compose)
```bash
# 1. Default: Intel integrated GPU / CPU fallback
docker compose up -d

# 2. Intel Arc discrete GPU (e.g., A770)
docker compose -f docker-compose.yml -f compose/docker-compose.arc.yml up -d

# 3. NVIDIA CUDA GPUs
docker compose -f docker-compose.yml -f compose/docker-compose.nvidia.yml up -d

# 4. AMD Radeon GPUs (ROCm)
docker compose -f docker-compose.yml -f compose/docker-compose.radeon.yml up -d
```

### Testing & Verification
```bash
# Run unit and benchmark tests
dotnet test tests/HrtLab.TrajectoryParser.Tests/
```

---

## 4. Coding Conventions for AI Agents

- **Language Version**: C# 13 / .NET 10.
- **Nullable Reference Types**: Enabled (`<Nullable>enable</Nullable>`). Never use the null-forgiving operator `!` without an accompanying inline comment justifying why null is impossible.
- **Blittable Structs**: Ensure coordinates, bounding boxes, and velocity structs use `[StructLayout(LayoutKind.Sequential, Pack = 4)]`.
- **Async Pattern**: Use `ValueTask` for high-frequency frame emission callbacks (`Func<long, ReadOnlySpan<AtomCoordinate>, ValueTask>`) to avoid state-machine heap allocations when operations complete synchronously.
