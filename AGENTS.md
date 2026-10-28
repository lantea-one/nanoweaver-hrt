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
   - Gate step 4 enforces 100.00% line and branch coverage across all developer-authored C# code and security-critical Razor components (defined by `coverlet.runsettings`).
   - Gate step 7 tracks all compiler-generated state machines against a committed downward-only ratchet baseline (`coverage-baseline.json`, currently 99.51% line / 96.38% branch with 59 uncovered lines across 29 classes), prohibiting any regression until 100.00% is reached.
   - Any new method, logic branch (`if`, `switch`, `catch`, ternary operators), or boundary condition MUST have comprehensive unit tests in `tests/NanoWeaverHrt.Tests`.
   - Never use `[ExcludeFromCodeCoverage]` as a shortcut to avoid writing tests.
   - Client-side JavaScript (`weaver-visualizer.js`, `weaver-session.js`, `weaver-auth.js`) MUST maintain 100% line, branch, and function coverage via `npm test`.

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
    - Never push or finalize if any of the 8 verification steps fail.

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

15. **Autonomous Agent Phasing & Work Package Breakdown Playbook**:
    - When decomposing large architectural or multi-layer features into tasks (especially for execution by smaller context-window or subagent LLMs), decompose them into atomic, single-layer work packages:
      - **Phase Sizing (Bounded Architectural Layer)**: Exactly one architectural layer per phase (e.g., Migration & Repository → Service & Gating → REST API Endpoints → Blazor UI → Verification & Documentation). Never conflate schema migrations with UI rendering in a single phase.
      - **File Contract Independence**: Every work package must specify exact input/output interfaces, concrete types, method signatures, and invariants before code is written, ensuring downstream phases compile against fixed contracts without ambiguity.
      - **Downward Ratchet Preservation**: Minimize Roslyn state machine divergence. Always append new methods and minimal-API endpoints to the end of files. Avoid capturing outer local variables in endpoint lambdas. Synchronize `ExcludeByFile` rules across `coverlet.runsettings` and `coverage-full.runsettings`.
      - **Clean Up Protocol**: Upon successful merge and upstream push of a phase or phase group, remove temporary phase instruction files (e.g., `work-packages/pXX-*.md`) to prevent workspace clutter, while updating `PHASES-v2.md` and `work-packages/README.md` as permanent audit records.
      - **Interactive Decisions Pattern**: Whenever an architectural fork, scoping ambiguity, or design decision arises, present it to the user as an interactive question form with a clearly justified recommended default option.

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
   - `exports/`: `.tar.gz` dataset bundles with SHA-256 manifests. Manifests exported by the web service are Ed25519-signed with the node's persisted dataset signing key; `POST /api/datasets/import` verifies a signed manifest against this node's key and the peer key registry (`weaver_peer_keys`), and accepts unsigned manifests unless `Peer:RequireSignedDatasets` is `true`.
3. **`compose/` & `docker-compose.yml`**:
   - Self-contained container stack. Default is CPU/Intel iGPU via `/dev/dri`.
   - Overrides provided for Arc A770 (`docker-compose.arc.yml`), NVIDIA (`docker-compose.nvidia.yml`), AMD Radeon (`docker-compose.radeon.yml`), and public reverse proxies (Caddy, Nginx, Cloudflare).
4. **`docs/`**:
   - GitHub Pages interactive documentation wiki powered by Docsify, custom sovereign dark theme, and high-contrast accessibility.

---

## 3. Developer Workflows & Quality Verification

### Run Full Verification Gate (All 8 Checks)
```bash
./scripts/verify-all.sh
```
This runs:
1. `dotnet format --verify-no-changes` (Zero formatting errors)
2. `npm test` (100% line, branch, function JS coverage)
3. `dotnet test ...` (100% C# unit test pass rate)
4. Cobertura coverage gate (100.00% line and branch coverage threshold)
5. Markdown scan (0 unescaped single-dollar LaTeX interpolation tokens)
6. Docblock scan (`scripts/check-docblocks.py`: every documented member has one `<param>` per parameter and a `<returns>`)
7. Un-excluded coverage ratchet (`scripts/check-coverage-ratchet.py`: re-measures with compiler-generated code included and compares against the committed `coverage-baseline.json`; no rate may fall and no class's uncovered-line count may rise)
8. Vendored JS & SRI integrity check (`scripts/verify-vendored-js.sh`: SHA-256 and SHA-384 SRI checks, zero external CDN scripts in templates, and Node RFC 8032 & ML-DSA-65 test vectors)

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

---

## 5. Work Package Breakdown & LLM Phasing Playbook

To ensure reliable, deterministic contributions from smaller context-window models or autonomous subagents, follow this standardized task decomposition process:

### 5.1 The 5-Layer Architectural Phase Sizing
Never assign an LLM agent a task spanning the full vertical stack (schema through UI) in a single prompt. Large compound tasks cause context overflow, missed docblocks, and coverage regressions. Always partition features into 5 discrete phases:

1. **Phase 1: Persistence & Schema Evolution**
   - Implements `IDatabaseMigration` supporting both SQLite and PostgreSQL.
   - Implements zero-allocation repository methods on `IWeaverDbConnectionFactory`.
   - Comprehensive repository unit tests with isolated SQLite connection pools (`SqliteConnection.ClearPool`).
2. **Phase 2: Domain Logic & Security Gating**
   - Implements domain services (`IService`), permission calculation, effective tiered access resolution, and audit logging to `weaver_audit_ledger`.
   - Unit tests covering 100% of branch logic, edge cases, and boundary constraints.
3. **Phase 3: Minimal API Endpoints & Contracts**
   - Defines request/response DTO records and maps endpoints at the end of `Program.cs`.
   - Uses typed dependency injection parameters; zero captured outer variables.
   - RFC 7807 problem details mappings and full endpoint integration tests with `WebApplicationFactory`.
4. **Phase 4: Sovereign Blazor UI & Client Interactions**
   - Implements accessible, dark-mode compliant Razor components (`.razor`) and modal dialogues.
   - Synchronizes `ExcludeByFile` in `coverlet.runsettings` and `coverage-full.runsettings` for non-security UI components, or writes 100% bUnit tests for security components.
   - Verifies browser state isolation, keyboard navigation, and zero inline styles.
5. **Phase 5: End-to-End Verification, Documentation & Closeout**
   - Runs full 8-gate `./scripts/verify-all.sh` verification.
   - Updates `docs/` Markdown specifications using clean Unicode (zero unescaped single-dollar symbols).
   - Consolidates phase notes into `PHASES-v2.md` and `work-packages/README.md`, removing temporary `work-packages/pXX-*.md` instruction files.

### 5.2 Downward Coverage Ratchet Invariants for Autonomous Agents
The compiler-generated state machine ratchet (`scripts/check-coverage-ratchet.py`) prohibits any increase in uncovered lines across Roslyn lambdas (`<<Main>b__4_N>d`) and async state machines (`<Method>d__N`):
- **Append Order Invariant**: Always append new methods and minimal API endpoint registrations to the bottom of classes and `Program.cs`. Never insert methods into the middle of existing files.
- **Async Block Scope**: Wrap `await using` statements inside explicit blocks `{ await using ... }` in repository methods to eliminate compiler exception-path branches.
- **Mock Setup Precision**: In Moq test setups, match parameter positions exactly; loose-mode Moq returns `null` for mismatched `Task<T?>` calls, leading to hard-to-trace `NullReferenceException` cascades in state machines.
- **bUnit Fresh Queries**: In bUnit component tests, always re-query DOM elements via `cut.Find(...)` after asynchronous state dispatches rather than reusing stale `IElement` references.

### 5.3 Interactive Decision Forms
When resolving architectural alternatives, ambiguities, or configuration choices:
- Formulate choices into multiple-choice options with a single recommended default option prefixed with `(Recommended)`.
- Explicitly explain the architectural rationale, trade-offs, and security implications of each option.
- Never make unratified assumptions on security boundaries, cryptographic algorithms, or database schemas.
