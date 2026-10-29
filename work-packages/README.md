# Work Package & Architectural Phasing Playbook

> **Instructions for Human Architects and AI Assistants**:
> This directory contains the standard operating procedures, architectural templates, and quality invariants for authoring, decomposing, and executing work packages in **nanoweaver-hrt**.
> All architectural and feature phases (**P1–P46**) are fully completed, verified at 100.00% line and branch coverage, and shipped in release **`v2026.10.29.3`**.

---

## 1. Development & Release Status

All 46 development phases have concluded:
- **Phases P1–P3**: Core Minimal API service, zero-allocation SIMD trajectory parser, and `System.IO.Pipelines` frame streaming.
- **Phases P4–P6**: Database seam rollout across User, DNA, and Configuration stores on `IWeaverDbConnectionFactory`.
- **Phases P7–P12**: PHI column-level encryption at rest, post-quantum Subject Key Vault, FDA 21 CFR Part 11 audit ledger, Ed25519 dataset signing, and Peer Key Registry.
- **Phases P13–P20**: Zero-private-key-transmission PKI authentication (Ed25519 & ML-DSA-65), in-browser cryptographic signing, bUnit component security test suite, and Subresource Integrity (SRI) dynamic pipeline.
- **Phases P21–P25**: Tiered researcher access control (`ReadAccess`, `GenomicAccess`, `FullReadAccess`), cohort management UI, and OpenAPI contracts.
- **Phases P26–P35**: Standalone cross-platform desktop and mobile GUI applications (Avalonia UI 11 / .NET 10) for Linux, macOS, Windows, Android, iOS, iPadOS, sovereign code-signing PKI rotation, multi-platform packaging, and continuous release pipelines (shipped in `v2026.10.29.1`).
- **Phases P36–P39**: Hardening and distribution enhancements (shipped in `v2026.10.29.2`):
  - **P36**: Windows single-file portable executable packaging & universal macOS dynamic 4096-byte page-aligned fat Mach-O slice synthesis.
  - **P37**: Universal package version synchronization (`2026.10.29.2`) across Linux (.deb, .rpm, .pkg.tar.zst, AUR), Windows (MSIX, NSIS), macOS (.dmg, .app), and Mobile (.apk, .ipa).
  - **P38**: Standalone interactive windowed desktop GUI shell (`IDesktopUiHost`, `DefaultDesktopUiHost`) with tray controller, single-instance mutex gating, and 100.00% line & branch test coverage.
  - **P39**: Master cross-platform packaging validation, 8-gate pre-flight verification, release documentation, and dual-pipeline publishing.
- **Phases P40–P46**: Wet-Lab Automation & Community Localization (shipped in `v2026.10.29.3`):
  - **P40**: Opentrons Python Protocol API v2 liquid handler generator engine (`IOpentronsProtocolGenerator`, OT-2 and Flex support, stoichiometric dispensing, on-deck thermocycling).
  - **P41**: Opentrons REST API endpoints (`POST /api/fabrication/opentrons/generate` & `/download`) with role gating and RFC 7807 problem details.
  - **P42**: Guided DNA Origami Workflow Wizard UI integration (Stages 5 & 6 hardware cards, visual deck layout, preview/download modal).
  - **P43**: Localization domain architecture (`IWeaverLocalizer`, `WeaverLocalizer`) with 10 native embedded JSON catalogs (`en-US`, `es-ES`, `fr-FR`, `de-DE`, `pt-BR`, `it-IT`, `ja-JP`, `zh-CN`, `nl-NL`, `pl-PL`).
  - **P44**: Multi-platform desktop & ViewModel localization (`MainShellViewModel`, `DesktopTrayIconController`, `LanguagePreferenceService` with dual-engine SQLite/PostgreSQL support).
  - **P45**: Sovereign Web UI language switcher (`LanguageSelector.razor`, `MainLayout.razor`, `EndocrineDashboard.razor`, `weaver_lang` SameSite=Strict cookies).
  - **P46**: Pre-flight verification (1,162 tests, 100% line & branch coverage), downward ratchet improvement (97.56% branch, 99.65% line), Docsify documentation guides, and release ratification.

There are currently **0 active work packages**. All planned initiatives for `v2026.10.29.3` have concluded cleanly.

---

## 2. Playbook for Implementation & Verification

When executing work packages, ensure full compliance with the following quality invariants:

### Rule 1 — Protect the Step-7 Coverage Ratchet (`coverage-baseline.json`)
Gate step 7 (`scripts/check-coverage-ratchet.py`) keys compiler-generated state machines by their ordinal names (`<MethodName>d__N` and `<<Main>b__4_N>d`). To prevent ordinal renumbering or uncovered state-machine lines:
1. **Append new methods at the END of existing classes**, never in the middle.
2. **Append new minimal-API endpoints at the END of `Program.Main`** (immediately before `app.Run();`), and **never capture local variables from `Main`** inside endpoint lambdas (pass all dependencies as lambda parameters instead).
3. **Use type-based DI registration** (`builder.Services.AddSingleton<IService, ServiceImpl>()`) rather than `sp => ...` factory lambdas in `Program.Main`.
4. **Wrap `await using` inside a block** in async repository methods:
   ```csharp
   int affected;
   {
       await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
       await using DbCommand cmd = session.CreateCommand("...");
       affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
   }
   return affected == 1;
   ```
   This prevents the C# compiler from emitting an unreachable exception-path disposal branch at the end of the async state machine.
5. **Avoid unreachable defensive branches**.
6. If Step 7 reports `IMPROVED ... Baseline is stale`, update the baseline with:
   ```bash
   python3 -I scripts/check-coverage-ratchet.py artifacts/coverage-full coverage-baseline.json --update
   ```

### Rule 2 — Maintain 100.00% Step-4 Line & Branch Coverage
- Gate step 4 requires `100.00%` line and `100.00%` branch coverage across all non-compiler-generated code.
- Every `if`, `switch`, ternary `? :`, `??`, `?.`, and `catch` in a synchronous method must have a unit test exercising both branches.

### Rule 3 — Pass Step-6 XML Docblock Check (`scripts/check-docblocks.py`)
- Every type, method, property, field, and constant — **including `private` and `internal` ones** — must have an XML doc comment (`/// <summary>`).
- Every method or constructor with parameters must have one `/// <param name="...">` per parameter, and every non-`void` method must have `/// <returns>` (or `/// <inheritdoc/>` when implementing an interface/base member).
- Every justification for the null-forgiving operator `!` must have an inline `// WHY '!': ...` comment, and every algorithm/query/endpoint must have `// WHAT:`, `// WHY:`, `// HOW:` comments.

### Rule 4 — SQLite Connection Pool Isolation in Tests
- **Never call `SqliteConnection.ClearAllPools()`** in tests! Clear only the specific test's SQLite file pool:
   ```csharp
   using SqliteConnection connection = new($"Data Source={dbPath}");
   SqliteConnection.ClearPool(connection);
   ```

### Rule 5 — Single-Command Format & Verification
Always format before running `./scripts/verify-all.sh`:
```bash
XDG_RUNTIME_DIR=/tmp dotnet format NanoWeaverHrt.slnx --no-restore && ./scripts/verify-all.sh
```

---

## 3. Work Package Authoring & LLM Task Phasing Guide

This guide establishes the standard operating procedure for human architects and AI assistants when specifying, decomposing, and executing complex features. It cross-references and implements the requirements of [`AGENTS.md`](../AGENTS.md) (Section 1.15 and Section 5).

### 3.1 When and How to Create Work Packages
Whenever a new feature or remediation spans multiple layers (e.g., schema migration, service logic, HTTP API, or UI rendering), create a dedicated work package file under `work-packages/` following the naming convention:
```
work-packages/p<Number>-<descriptive-slug>.md
```
*(Example: `work-packages/p36-biometric-audit-stream.md`)*

### 3.2 Standard Work Package Template
Every authored work package MUST follow this markdown structure:

```markdown
# Work Package PXX: <Feature Title>

## 1. Context & Objective
- **Problem Statement**: Plain-English description of the gap or capability needed.
- **Goal & Scope**: High-level boundaries of what is included and explicitly excluded.
- **Target Role / Boundary**: Target actors (`Administrator`, `Researcher`, `Patient`) and transport boundary (`Api`, `Web`, `ApiWeb`).

## 2. Technical Contracts & Invariants
- **Interfaces & Types**: Concrete method signatures, models, and return types before writing implementation.
- **Database Schema**: Explicit SQL DDL (SQLite & PostgreSQL) and migration version identifier.
- **REST Endpoints**: HTTP method, URI, request DTO, response DTO, and RFC 7807 problem details mappings.
- **Coverage & Ratchet Invariants**: Applicable `ExcludeByFile` rules or required test fixture locations.

## 3. Step-by-Step Implementation Tasks
1. Task 1: Schema migration & connection factory queries.
2. Task 2: Service layer implementation & unit tests.
3. Task 3: API route mapping & integration tests.
4. Task 4: Blazor / Avalonia UI component & component tests.
5. Task 5: Pre-flight gate verification & documentation.

## 4. Verification Gate & Acceptance Criteria
- [ ] Zero build warnings (`/warnaserror`).
- [ ] 100.00% line & branch coverage on developer-authored code (Step 4).
- [ ] 100% downward ratchet compliance (Step 7).
- [ ] 100% XML docblock coverage on all members (Step 6).
- [ ] Zero unescaped single-dollar math tokens in documentation (Step 5).
```

### 3.3 Phasing Rules for Smaller LLMs
Smaller context-window models or autonomous subagents succeed when tasks have tightly constrained scopes and deterministic boundaries. When orchestrating work for smaller LLMs:
1. **One Architectural Layer Per Turn**: Decompose the master plan into 1-layer units (Migration/Repo → Service → Minimal API → UI → Verification). Never combine database migration writing with UI component authoring in the same prompt.
2. **Pre-Specify Method Signatures**: Never allow the LLM to invent its own interface method signatures during service or UI implementation. Always provide the exact `Task<T>` signature, parameter names, and order in the work package prompt.
3. **Moq Argument Order Invariance**: In tests, ensure mock setups (`Setup(x => x.Method(...))`) match the exact parameter order of the pre-specified interface to avoid Moq loose-mode silent `null` returns.
4. **Interactive Form Pattern**: When a requirement fork is encountered, prompt the user with a structured form containing a clearly reasoned `(Recommended)` default option before proceeding.

### 3.4 Lifecycle & Clean-Up Protocol
To prevent workspace drift, token bloat, and stale branch clutter:
1. **Active State**: The active work package instruction file (`pXX-*.md`) resides in `work-packages/` while in progress.
2. **Review & Gate**: The implementation is verified via `./scripts/verify-all.sh`.
3. **Ratification & Pruning**:
   - Record user decisions and key milestones in `docs/release-notes.md`.
   - Update `work-packages/README.md` to reflect completion.
   - Delete the temporary `pXX-*.md` instruction file from `work-packages/`.
   - Prune all ephemeral git worktrees (`git worktree prune`) and delete stale local branches (`git branch -d ...`).
   - Commit and push clean state to sovereign Forgejo remote `origin/main`.
