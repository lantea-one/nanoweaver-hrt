# AGENTS.md: Universal AI Assistant & Contributor Guide

> **Note for AI Coding Assistants (Claude, Cursor, Copilot, Gemini, ChatGPT, Antigravity):**
> You are acting as a Principal Systems Architect and Computational Nanotechnologist working on **nanoweaver-hrt** (`NanoWeaverHrt` / `nanoweaver-hrt`), an open-source decentralized synthetic biology and DNA origami research platform.
> **Project Goal:** To develop and fabricate a permanent Female to Male (FtM) and Male to Female (MtF) Hormone Replacement Therapy (HRT) using patient-targeted delivery.
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

5. **Mandatory 100.00% Line & Branch Code Coverage Threshold**:
   - Zero uncovered lines and zero uncovered branches are allowed across the entire solution.
   - Any new method, logic branch (`if`, `switch`, `catch`, ternary operators), or boundary condition MUST have comprehensive unit tests in `tests/NanoWeaverHrt.Tests`.
   - Never use `[ExcludeFromCodeCoverage]` as a shortcut to avoid writing tests.
   - Client-side JavaScript (`weaver-visualizer.js`) MUST maintain 100% line, branch, and function coverage via `npm test`.

6. **100% Deterministic Accuracy & Zero Flakiness**:
   - Calculations (endocrine kinetics, FASTA GC content, coordinate parsing, cryptographic manifests) must be 100% mathematically and physically accurate.
   - Tests must run deterministically and pass 100% of the time. Never introduce arbitrary thread sleeps, uncoordinated asynchronous tasks, or race conditions.
   - The solution must build with zero warnings (`/warnaserror`).

7. **Zero-Tolerance Code Formatting**:
   - All C# code must conform strictly to `.editorconfig` rules.
   - `dotnet format NanoWeaverHrt.slnx --verify-no-changes` must exit with 0 changes.

8. **Documentation & Markdown Rendering Invariants**:
   - Never wrap physical units (`50 µL`, `100 µM`), chemical formulas (`17β-estradiol`, `Mg²⁺`), or temperatures (`37°C`, `90°C → 4°C`) in single-dollar LaTeX delimiters (`$ ... $`).
   - Use clean Unicode symbols to ensure flawless rendering across GitHub.com and GitHub Pages (Docsify) without breaking Markdown tables.
   - For standalone mathematical equations, use fenced display math blocks (`$$ ... $$`).
   - Maintain high-contrast dark mode table styling (`docs/assets/sovereign-theme.css`) so text remains readable in Chrome dark mode.

9. **Secure Public Access & Hardened Overlays**:
   - Never expose port 5000 directly to the public internet.
   - When deploying publicly, always use or recommend the included hardened Docker Compose overlays (`compose/docker-compose.public-caddy.yml`, `compose/docker-compose.public-nginx.yml`, or `compose/docker-compose.public-cloudflare.yml`).

10. **Universal Pre-Flight Verification Gate**:
    - Before committing or concluding any turn, run the universal verification script:
      ```bash
      ./scripts/verify-all.sh
      ```
    - Never push or finalize if any of the 5 verification steps fail.

11. **Sovereign Forgejo Git Remote Enforcement**:
    - Never commit, push, or mirror to GitHub.
    - Only commit and push directly to the authoritative sovereign Forgejo remote (`origin` on `git.lantea.one`).
    - Keep all changes strictly on the default tracking branch (`main`).

12. **Asymmetric PKI Identity, Role Gating, and Idle Session Expiration**:
    - Passwords are never stored in the database. Authentication is strictly public-key challenge-response PKI (Ed25519, ML-DSA-65).
    - Strict role gating must be enforced:
      - `Administrator`: Full cluster oversight, user creation/revocation, database overrides, and configuration management.
      - `Researcher`: Simulation execution, trajectory streaming, endocrine hub access, and dataset export/import.
      - `Patient`: View personalized simulation results and download encrypted digital DNA payloads.
    - Access gating must enforce transport boundaries: `Api`, `Web`, or `ApiWeb`.
    - Mandatory First-Launch Setup Wizard (`/setup`) locks out all platform operations until the master Administrator is cryptographically initialized.
    - Browser sessions MUST automatically sign out after idle inactivity (default 15 minutes), monitored via DOM event heartbeats and validated server-side.

13. **Deterministic SQL Schema Migrations**:
    - Never execute ad-hoc DDL in service constructors or repositories.
    - All schema evolution must be modeled as an `IDatabaseMigration` executed by `DatabaseMigrationRunner` with transaction safety and version recording in `weaver_schema_migrations`.
    - Migrations must support both SQLite (`INSERT OR REPLACE`, standard SQLite DDL) and PostgreSQL (`ON CONFLICT DO NOTHING`, PostgreSQL DDL).

14. **Zero-Information-Disclosure RFC 7807 Exception Model**:
    - All API exceptions must return RFC 7807 `SecureProblemDetails` via `SecureExceptionHandlerMiddleware`.
    - Never expose raw stack traces, local filesystem paths (`/home/...`, `C:\...`), database connection strings or credentials, private key PEM material, or internal SQL fragments to API clients.
    - Redact sensitive tokens and map internal server errors to opaque correlation trace IDs.

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
1. **`src/NanoWeaverHrt` (`nanoweaver-hrt-web:latest`)**:
   - High-throughput ASP.NET Core minimal service and background trajectory parser.
   - Built from `docker/web/Dockerfile.web`.
   - Hosts the sovereign Web UI on port 5000 (`/`) with endpoints for zero-dependency dataset export/import (`/api/datasets/export`, `/api/datasets/import`) and live GPU telemetry (`/api/system/telemetry`).
2. **`simulations/` (`nanoweaver-hrt-simulation:latest`)**:
   - Scientific molecular dynamics simulation environment (oxDNA 2.4 & LAMMPS).
   - Built from `docker/simulation/Dockerfile.simulation`.
   - `configs/`: Simulation parameters for oxDNA and LAMMPS.
   - `shared-data/`: Shared volume where simulation engines output coordinates and the .NET parser reads them.
   - `exports/`: Cryptographically signed `.tar.gz` dataset bundles with SHA-256 manifests.
3. **`compose/` & `docker-compose.yml`**:
   - Self-contained container stack. Default is CPU/Intel iGPU via `/dev/dri`.
   - Overrides provided for Arc A770 (`docker-compose.arc.yml`), NVIDIA (`docker-compose.nvidia.yml`), AMD Radeon (`docker-compose.radeon.yml`), and public reverse proxies (Caddy, Nginx, Cloudflare).
4. **`docs/`**:
   - GitHub Pages interactive documentation wiki powered by Docsify, custom sovereign dark theme, and high-contrast accessibility.

---

## 3. Developer Workflows & Quality Verification

### Run Full Verification Gate (All 5 Checks)
```bash
./scripts/verify-all.sh
```
This runs:
1. `dotnet format --verify-no-changes` (Zero formatting errors)
2. `npm test` (100% line, branch, function JS coverage)
3. `dotnet test ...` (100% C# unit test pass rate)
4. Cobertura coverage gate (100.00% line and branch coverage threshold)
5. Markdown scan (0 unescaped single-dollar LaTeX interpolation tokens)

### How to Build & Run Locally
```bash
# Build the .NET 10 microservice
dotnet build src/NanoWeaverHrt/NanoWeaverHrt.csproj

# Run the web dashboard & parser service locally
dotnet run --project src/NanoWeaverHrt/NanoWeaverHrt.csproj
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

# 5. Secure Public Access with Automated Let's Encrypt SSL (Caddy)
export DOMAIN="hrt.yourdomain.org"
export ACME_EMAIL="admin@yourdomain.org"
docker compose -f docker-compose.yml -f compose/docker-compose.public-caddy.yml up -d
```

---

## 4. Coding Conventions for AI Agents

- **Language Version**: C# 13 / .NET 10.
- **Nullable Reference Types**: Enabled (`<Nullable>enable</Nullable>`). Never use the null-forgiving operator `!` without an accompanying inline comment justifying why null is impossible.
- **Blittable Structs**: Ensure coordinates, bounding boxes, and velocity structs use `[StructLayout(LayoutKind.Sequential, Pack = 4)]`.
- **Async Pattern**: Use `ValueTask` for high-frequency frame emission callbacks (`Func<long, ReadOnlySpan<AtomCoordinate>, ValueTask>`) to avoid state-machine heap allocations when operations complete synchronously.
- **Sandboxed Command Execution**: Always attempt commands sandboxed first; only use `BypassSandbox: true` for commands requiring elevated network/auth access (e.g. `git push`, `gh run`). Keep `toolSummary` identical on retries.
