# Work package P11 — Code-hygiene and latent-bug sweep

> **Why this file is under `work-packages/`.** Gate step 5 scans `docs/**/*.md`, `docs/*.md` and `README.md` for paired single-`$` tokens. `work-packages/` is version-controlled and outside that scan. **Do not move this file under `docs/`.**

**Agent:** coder (suitable for lighter/smaller models: `flash`, `flash_lite`, Haiku, Sonnet).
**Dependencies:** None (all files are independent of P10/P10.5).

---

## Recorded User Decisions (2026-10-27)

1. **`WorkflowWizard.razor` Dead Busy Flags (`A3`):** Remove the dead `_isSimulating`, `_isParsing`, and `_isExportingParquet` boolean fields and their unreachable `@if (...)` spinner branches in `WorkflowWizard.razor`, since `RunSimulationAsync`, `RunStreamParseAsync`, and `RunParquetExportAsync` are synchronous and complete instantaneously.
2. **Full Hygiene Scope (`A4`):** Include:
   - **M5**: Add inline `// WHY !:` justification comments on `Caching/ValkeyCacheService.cs:98` (`byte[] bytes = (byte[])result!;`) and `Bridges/ValkeyEventBridge.cs:263` (`byte[] bytes = (byte[])message!;`).
   - **L7**: Replace `/home/fabigail` with `/home/operator` at all 4 remaining test sites in `tests/NanoWeaverHrt.Tests/ApiRoleGatingAndDisclosureTests.cs` (`:801`, `:809`, `:867`, `:885`).
   - **Culture-Invariant Timestamp Parsing**: Pass `CultureInfo.InvariantCulture` to `DateTimeOffset.Parse` in `src/NanoWeaverHrt/Database/Access/WeaverDbValues.cs:29` (`DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture)`).
   - **`Dockerfile.simulation` Ownership**: Add `/workspace/configs` to `chown 1000:1000` at `docker/simulation/Dockerfile.simulation:91`.

> **Pre-verified already landed in `89f77cb` (do not re-do):**
> - **L1** (`Task.Delay` calls in `WorkflowWizard.razor`) — already removed.
> - **L2** (`DuckDbParquetWriter.cs:76-79` comment explaining why in-memory `staging_trajectory` is outside Directive 13) — already present.
> - **L7** in `SecurityHardeningAndMigrationsTests.cs:324` — already uses `/home/operator`.

---

## Sizing & Lighter-Model Execution Rules

- **Single self-contained stage (~70 lines changed across 6 files).**
- **Step-7 Coverage Ratchet:** If `coverage-baseline.json` changes, run:
  ```bash
  python3 -I scripts/check-coverage-ratchet.py artifacts/coverage-full coverage-baseline.json --update
  ```

---

## Files in Scope

| Path | Role |
| :--- | :--- |
| `src/NanoWeaverHrt/Caching/ValkeyCacheService.cs` | Add `// WHY !:` comment at `:98`. |
| `src/NanoWeaverHrt/Bridges/ValkeyEventBridge.cs` | Add `// WHY !:` comment at `:263`. |
| `src/NanoWeaverHrt/Database/Access/WeaverDbValues.cs` | Add `using System.Globalization;` and pass `CultureInfo.InvariantCulture` to `DateTimeOffset.Parse` at `:29`. |
| `src/NanoWeaverHrt/Components/Pages/WorkflowWizard.razor` | Remove `_isSimulating`, `_isParsing`, and `_isExportingParquet` fields, assignments, `disabled="..."` attributes, and unreachable `@if` spinner branches. |
| `docker/simulation/Dockerfile.simulation` | Add `/workspace/configs` to `chown 1000:1000` at `:91`. |
| `tests/NanoWeaverHrt.Tests/ApiRoleGatingAndDisclosureTests.cs` | Replace `/home/fabigail` with `/home/operator` at `:801`, `:809`, `:867`, `:885`. |

---

## Exact Implementation Changes

### 1. `src/NanoWeaverHrt/Caching/ValkeyCacheService.cs` (`:96-100`)

```csharp
                if (result.HasValue && !result.IsNullOrEmpty)
                {
                    // WHY !: StackExchange.Redis annotates the explicit (byte[]?) conversion as nullable,
                    //        but result.HasValue && !result.IsNullOrEmpty guarantees a non-null byte[].
                    byte[] bytes = (byte[])result!;
                    return JsonSerializer.Deserialize<T>(bytes);
                }
```

### 2. `src/NanoWeaverHrt/Bridges/ValkeyEventBridge.cs` (`:256-265`)

```csharp
        if (message.IsNullOrEmpty)
        {
            return;
        }

        try
        {
            // WHY !: StackExchange.Redis annotates the explicit (byte[]?) conversion as nullable,
            //        but the preceding message.IsNullOrEmpty guard guarantees a non-null byte[].
            byte[] bytes = (byte[])message!;
            TEvent? parsed = JsonSerializer.Deserialize<TEvent>(bytes);
```

### 3. `src/NanoWeaverHrt/Database/Access/WeaverDbValues.cs` (`:26-30`)

Add `using System.Globalization;` at top of file, and update `ReadTimestamp`:
```csharp
    public static DateTimeOffset ReadTimestamp(DbDataReader reader, int ordinal) =>
        reader.GetFieldType(ordinal) == typeof(DateTime)
            ? new DateTimeOffset(reader.GetDateTime(ordinal), TimeSpan.Zero)
            : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);
```

### 4. `src/NanoWeaverHrt/Components/Pages/WorkflowWizard.razor`

1. At `:361-372`, replace:
   ```razor
   <div class="mt-3">
       <button class="btn btn-accent w-100" @onclick="RunSimulationAsync">
           <span>🚀 Execute In-Silico Simulation</span>
       </button>
   </div>
   ```
2. At `:427-451`, replace:
   ```razor
   <div class="mt-3">
       <button class="btn btn-primary w-100" @onclick="RunStreamParseAsync">
           <span>⚡ Trigger Zero-Allocation Stream Parser</span>
       </button>
   </div>

   <div class="mt-2">
       <button class="btn btn-outline w-100" @onclick="RunParquetExportAsync">
           <span>📦 Convert to Columnar Parquet (pg_duckdb)</span>
       </button>
   </div>
   ```
3. Delete `private bool _isSimulating;` (`:994-997`), `private bool _isParsing;` (`:1014-1017`), and `private bool _isExportingParquet;` (`:1019-1022`) and their `_isSimulating = true/false`, `_isParsing = true/false`, `_isExportingParquet = true/false` assignments inside `RunSimulationAsync`, `RunStreamParseAsync`, and `RunParquetExportAsync`.

### 5. `docker/simulation/Dockerfile.simulation` (`:90-91`)

Replace:
```dockerfile
RUN mkdir -p /workspace/configs /workspace/data /workspace/exports \
    && chown 1000:1000 /workspace /workspace/data /workspace/exports
```
With:
```dockerfile
RUN mkdir -p /workspace/configs /workspace/data /workspace/exports \
    && chown 1000:1000 /workspace /workspace/configs /workspace/data /workspace/exports
```

### 6. `tests/NanoWeaverHrt.Tests/ApiRoleGatingAndDisclosureTests.cs` (`:801, 809, 867, 885`)

Replace all 4 occurrences of `/home/fabigail` with `/home/operator`.

---

## Acceptance Criteria

1. `grep -rn "_isSimulating\|_isParsing\|_isExportingParquet" src/NanoWeaverHrt/Components/Pages/WorkflowWizard.razor` returns 0 matches.
2. `grep -rn "/home/fabigail" tests/` returns 0 matches.
3. `grep -n "/workspace/configs" docker/simulation/Dockerfile.simulation` shows `/workspace/configs` in both `mkdir -p` and `chown 1000:1000`.
4. `grep -n "CultureInfo.InvariantCulture" src/NanoWeaverHrt/Database/Access/WeaverDbValues.cs` matches `ReadTimestamp`.
5. `./scripts/verify-all.sh` passes all 7 steps (`100.00%` Step 4 line/branch coverage, 0 warnings, 0 docblock issues, Step 7 ratchet green).
