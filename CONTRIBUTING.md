# Contributing to NanoWeaverHrt

Thank you for your interest in contributing to **NanoWeaverHrt** (`NanoWeaverHrt` / `nanoweaver-hrt`), an open-source decentralized synthetic biology and DNA origami research platform engineering patient-targeted, closed-loop Hormone Replacement Therapy (HRT).

This document outlines the development workflow, solution architecture, and quality invariants required for both human engineers and autonomous AI agents modifying this repository.

---

## 1. Sovereign Forgejo Repository & Issue Tracking

NanoWeaverHrt is developed and hosted on a sovereign Git forge powered by **Forgejo**:
* **Authoritative Repository**: [`https://git.lantea.one/lantea-one/nanoweaver-hrt`](https://git.lantea.one/lantea-one/nanoweaver-hrt)
* **Forgejo Issues & Work Packages**: [`https://git.lantea.one/lantea-one/nanoweaver-hrt/issues`](https://git.lantea.one/lantea-one/nanoweaver-hrt/issues)
* **Forgejo Releases & Artifacts**: [`https://git.lantea.one/lantea-one/nanoweaver-hrt/releases`](https://git.lantea.one/lantea-one/nanoweaver-hrt/releases)

> [!IMPORTANT]
> **Authoritative Remote Policy**:
> All forks, branches, pull requests, issues, and active development MUST be conducted directly on the sovereign Forgejo remote (`origin` on `git.lantea.one`). Never push code directly to GitHub mirrors.

---

## 2. Work Packages Playbook: Published & Found in Forgejo Issues

In NanoWeaverHrt, **all development tasks, architectural designs, refactors, and feature milestones MUST be published and tracked directly as Forgejo Issues**.

### 2.1 The 5-Layer Architectural Phase Sizing
To prevent scope creep, context exhaustion, and coverage regressions, features are decomposed into atomic, single-layer work packages:

1. **Phase 1: Persistence & Schema Evolution**:
   * Implement `IDatabaseMigration` supporting SQLite, PostgreSQL, DuckDB, and MySQL.
   * Implement zero-allocation repository methods on `IWeaverDbConnectionFactory`.
   * Unit tests with isolated SQLite connection pools (`SqliteConnection.ClearPool`).
2. **Phase 2: Domain Logic & Security Gating**:
   * Implement domain services (`IService`), asymmetric PKI (Ed25519, ML-DSA-65), tiered role gating (`Administrator`, `Researcher`, `Patient`), transport boundaries (`Api`, `Web`, `ApiWeb`), and audit logging to `weaver_audit_ledger`.
   * Unit tests covering 100% of branch logic, edge cases, and boundary constraints.
3. **Phase 3: Minimal API Endpoints & Contracts**:
   * Define request/response DTO records and map endpoints at the bottom of `Program.cs`.
   * Use typed dependency injection; zero captured outer variables in endpoint lambdas.
   * Map RFC 7807 `SecureProblemDetails` and write full `WebApplicationFactory` integration tests.
4. **Phase 4: Sovereign Blazor UI & Client Interactions**:
   * Implement accessible, dark-mode compliant Razor components (`.razor`) and modal dialogues.
   * Maintain 100% bUnit test coverage for security-critical UI components or synchronize `ExcludeByFile` in `coverlet.runsettings` and `coverage-full.runsettings`.
   * Verify keyboard accessibility and zero inline styles.
5. **Phase 5: End-to-End Verification, Documentation & Closeout**:
   * Execute universal 8-gate `./scripts/verify-all.sh` verification.
   * Update `docs/` Markdown specifications using clean Unicode symbols (zero unescaped single-dollar tokens).
   * Reference the Forgejo Issue in the commit message (e.g. `feat(endocrine,p57): ... (Forgejo #60)`).
   * Close the corresponding Forgejo Issue and update `docs/release-notes.md`.

### 2.2 Finding & Claiming Work Packages
1. Navigate to [Forgejo Issues](https://git.lantea.one/lantea-one/nanoweaver-hrt/issues).
2. Filter by open milestones or labels (e.g., `layer/persistence`, `layer/domain`, `layer/api`, `layer/ui`).
3. Each issue specifies exact files in scope, concrete interfaces, types, and acceptance criteria before implementation begins.

---

## 3. Solution Structure & How to Modify It

The solution is organized into modular .NET 10 projects, simulation environments, and client-side tooling:

```
nanoweaver-hrt/
├── src/
│   ├── NanoWeaverHrt/             # High-throughput ASP.NET Core minimal web service & parser
│   │   ├── Components/            # Sovereign Blazor UI pages & Razor components
│   │   ├── Database/              # Multi-engine migrations & zero-allocation repositories
│   │   ├── Models/                # Blittable coordinate structs & domain DTOs
│   │   ├── Security/              # Ed25519, ML-DSA-65 PKI, role gating & session management
│   │   ├── Services/              # Trajectory parsers, federation, endocrine & gel engines
│   │   └── Program.cs             # DI configuration & minimal API endpoint mappings
│   ├── NanoWeaverHrt.Gui.Core/    # Shared MVVM ViewModels & 18-language localization
│   ├── NanoWeaverHrt.Desktop/     # Avalonia UI 11 desktop app & system tray controller
│   └── NanoWeaverHrt.Mobile/      # Avalonia mobile app & hardware biometric vaults (Android/iOS)
├── simulations/                   # oxDNA & LAMMPS molecular dynamics Docker environments
├── tests/
│   └── NanoWeaverHrt.Tests/       # Unit, integration, and bUnit test suites (100% coverage)
├── docs/                          # Docsify interactive documentation wiki & research primers
├── .agents/                       # Tailored repository AI agent definitions & instructions
└── scripts/                       # Pre-flight verification gates & packaging automation
```

### 3.1 Local Build & Run Instructions
```bash
# 1. Build the solution (zero warnings required)
dotnet build NanoWeaverHrt.slnx

# 2. Run the trajectory parser & web service locally
dotnet run --project src/NanoWeaverHrt/NanoWeaverHrt.csproj

# 3. Run client-side visualizer tests
npm test

# 4. Run full C# test suite
dotnet test NanoWeaverHrt.slnx
```

---

## 4. Prime Coding & Quality Directives

Every contributor (human or AI) must uphold the following architectural standards:

1. **Strict Zero-Allocation Policy in Hot Paths**:
   * Never introduce `string.Split()`, `Regex`, `new byte[]`, or LINQ operations (`.Select()`, `.Where()`, `.ToArray()`) inside hot parsing loops.
   * Always parse raw byte buffers using `System.IO.Pipelines.PipeReader`, `ReadOnlySequence<byte>`, `ReadOnlySpan<byte>`, `SequenceReader<byte>`, and `System.Buffers.Text.Utf8Parser`.
   * Rent coordinate buffers from `ArrayPool<T>.Shared` and return them inside `finally` blocks.

2. **100% Docblock Coverage Rule**:
   * Every class, struct, record, interface, method, property, and field—**including private and internal members**—must have complete XML doc comments (`/// <summary>`, `<param>`, `<returns>`, `<remarks>`).
   * Checked automatically by `scripts/check-docblocks.py` during Gate 6.

3. **Inline "What / Why / How" Comments**:
   * For every algorithm, memory slice, or asynchronous loop, add inline comments:
     * `// WHAT: ...`
     * `// WHY: ...`
     * `// HOW: ...`

4. **100.00% Line & Branch Code Coverage Threshold**:
   * Gate 4 enforces **100.00% line and branch coverage** across all developer-authored C# code.
   * Gate 7 enforces the downward coverage ratchet against `coverage-baseline.json`. No class's uncovered lines may increase.
   * Never use `[ExcludeFromCodeCoverage]` to avoid writing tests.

5. **Append-Order Invariant**:
   * Always append new methods and minimal API endpoint registrations to the end of classes and `Program.cs`. Never insert methods into the middle of existing files, preventing Roslyn lambda state machine divergence.

6. **Clean Unicode Documentation Formatting**:
   * Never wrap physical units (`50 µL`, `100 µM`), chemical formulas (`17β-estradiol`, `Mg²⁺`), or temperatures (`37°C`) in raw LaTeX single-dollar tokens (`$ ... $`).
   * Use clean Unicode symbols in Markdown tables and text. Standalone equations must be fenced display math blocks (`$$ ... $$`).

7. **Zero-Tolerance Quality**:
   * The solution must build with zero compiler warnings (`/warnaserror`).
   * All tests must run deterministically with zero flakiness.

---

## 5. Universal Pre-Flight Verification Gate

Before committing code or submitting a pull request, run the universal verification script:
```bash
./scripts/verify-all.sh
```

All 8 gates must pass:
1. `dotnet format --verify-no-changes` (Zero formatting errors)
2. `npm test` (100% line, branch, function JS coverage)
3. `dotnet test` (100% C# unit test pass rate)
4. Cobertura coverage gate (100.00% line and branch coverage)
5. Markdown scan (0 unescaped single-dollar LaTeX tokens)
6. Docblock scan (`scripts/check-docblocks.py`: all parameters and returns documented)
7. Coverage ratchet check (`scripts/check-coverage-ratchet.py`: downward-only baseline enforcement)
8. Vendored JS & SRI integrity check (SHA-256 and SHA-384 SRI hashes verified)

---

## 6. Tailored Repository AI Agents (`.agents/`)

For AI assistants (Antigravity, Claude, Cursor, Copilot, ChatGPT), this repository provides tailored agent definitions registered in `.agents/agents.json`:

| Agent Identifier | Scope | Specialization |
| :--- | :--- | :--- |
| `nanoweaver-hrt-architect` | Architecture & Planning | Designs 5-layer work packages published to Forgejo Issues. Never writes production code. |
| `nanoweaver-hrt-coder` | Full Repository | Generalist senior engineer implementing Forgejo issue work packages under full AGENTS.md rules. |
| `nanoweaver-hrt-coder-web-service` | `src/NanoWeaverHrt` | Minimal APIs in `Program.cs`, SIMD parsers, multi-engine persistence, and bUnit Razor test suites. |
| `nanoweaver-hrt-coder-desktop` | `src/NanoWeaverHrt.Desktop`, `src/NanoWeaverHrt.Gui.Core` | Avalonia UI 11 desktop app, MVVM ViewModels, tray controller, 18-language localization, and desktop packaging. |
| `nanoweaver-hrt-coder-android` | `src/NanoWeaverHrt.Mobile` (Android) | Android Avalonia lifecycle, `MainActivity`, Keystore StrongBox PKI keys, and APK packaging. |
| `nanoweaver-hrt-coder-ios` | `src/NanoWeaverHrt.Mobile` (iOS) | iOS Avalonia lifecycle, `AppDelegate`, Keychain / Secure Enclave PKI keys, and iOS packaging. |
| `nanoweaver-hrt-mechanic` | Full Repository | Non-behavioral mechanical edits: renames, `dotnet format`, XML docblocks, and compiler warnings. |

See [`.agents/README.md`](.agents/README.md) for detailed invocation schemas and configuration.
