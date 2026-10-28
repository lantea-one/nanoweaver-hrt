# Work Package P18: Step-7 Un-Excluded Coverage Ratchet Baseline Closeout

- **Phase ID**: `P18`
- **Recommended Model Tier**: `flash` / Haiku / Sonnet — **Split into Stage A (`Program.cs` Minimal-API Endpoint Lambdas) and Stage B (Service State Machines + Ratchet Ratification & Report)**
- **Parent Document**: [`work-packages/PHASES-v2.md`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/work-packages/PHASES-v2.md) (Phase P18)
- **Prerequisites**: `P10` through `P17`
- **Blocks**: `P19` (Release Verification & Push)

---

## 1. Recorded User Decisions & Constraints (Authoritative)

1. **Strict Prohibition on Mocking `DbConnection` (P3 / P18 Invariant)**:
   - Never mock `DbConnection`, `DbCommand`, or `DbDataReader` (`Moq` on ADO.NET classes) to artificially inflate coverage. All database coverage must come from real SQLite (or real PostgreSQL when `WEAVER_TEST_POSTGRES` is set) via `IWeaverDbConnectionFactory`.
2. **No Artificial Sync-Over-Async Refactoring**:
   - Do not rewrite clean `async Task` methods into synchronous ones merely to eliminate compiler-generated state-machine branches.
3. **Target the Real Uncovered Branches First, Then Ratify Honestly**:
   - Inspect `artifacts/coverage-full/**/coverage.cobertura.xml` against [`coverage-baseline.json`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/coverage-baseline.json) (currently `124` uncovered lines across `46` classes at `98.92%` line rate).
   - Add real integration and unit tests covering the reachable uncovered lines in:
     - `NanoWeaverHrt.Program/<>c/<<Main>b__...>` minimal-API endpoint lambdas (`Program.cs`)
     - `NanoWeaverHrt.Security.Authentication.UserAuthenticationService` (`ValidateTokenAsync`, `VerifyChallengeAndIssueTokenAsync`)
     - `NanoWeaverHrt.Security.Authentication.UserProfileRepository`
     - `NanoWeaverHrt.Events.ChannelEventBus` (`PublishAsync`)
     - `NanoWeaverHrt.Services.Stack.StackServiceManager` (`GetServiceByIdAsync`, `ExtractAcceleratorAsync`)
     - `NanoWeaverHrt.Caching.ValkeyCacheService` / `ValkeyLockService` / `ValkeyEventBridge`
   - Ratify the reduced `coverage-baseline.json` using:
     ```bash
     python3 -I scripts/check-coverage-ratchet.py artifacts/coverage-full coverage-baseline.json --update
     ```
   - If `uncovered_lines_total` reaches `0` (`100.00%` line and `100.00%` branch), remove `CompilerGeneratedAttribute` from `coverlet.runsettings`, delete `coverage-full.runsettings` and `coverage-baseline.json`, and fold Step 7 into Step 4. Otherwise, commit the ratified lower `coverage-baseline.json` and document every remaining class and line in `docs/release-notes.md` (or the P18 completion report) with the exact reason it remains (e.g. compiler-generated `async` state-machine `MoveNext` disposal or external Valkey socket race).

---

## 2. How to Inspect Exact Uncovered Lines in `artifacts/coverage-full`

Run the following one-liner after running Gate Step 7 to print the exact source file and line numbers for every class in `coverage-baseline.json`:

```bash
python3 - << 'EOF'
import glob, xml.etree.ElementTree as ET

reports = sorted(glob.glob("artifacts/coverage-full/**/coverage.cobertura.xml", recursive=True))
root = ET.parse(reports[-1]).getroot()
for cls in root.iter("class"):
    uncovered = [l.attrib["number"] for l in cls.findall("lines/line") if int(l.attrib.get("hits", "0")) == 0]
    if uncovered:
        print(f"{cls.attrib['name']} ({cls.attrib['filename']}): lines {', '.join(uncovered)}")
EOF
```

---

## 3. Two-Stage Execution Plan for Smaller Models

### Stage A: Cover Reachable `Program.cs` Minimal-API Endpoint Lambdas
1. Run the inspection snippet in §2 to list the exact line numbers in `src/NanoWeaverHrt/Program.cs` for `NanoWeaverHrt.Program/<>c/<<Main>b__...>d`.
2. In `tests/NanoWeaverHrt.Tests/EndpointIntegrationTests.cs` (or `ApiRoleGatingAndDisclosureTests.cs`), add targeted HTTP requests via `WebApplicationFactory<Program>` that hit each uncovered guard/validation return in `Program.cs`.
3. Re-run Gate Step 7 and update `coverage-baseline.json`:
   ```bash
   rm -rf artifacts/coverage-full
   env -u WEAVER_TEST_POSTGRES XDG_RUNTIME_DIR=/tmp dotnet test NanoWeaverHrt.slnx \
       --configuration Release \
       --settings coverage-full.runsettings \
       --collect:"XPlat Code Coverage" \
       --results-directory artifacts/coverage-full \
       --verbosity minimal
   python3 -I scripts/check-coverage-ratchet.py artifacts/coverage-full coverage-baseline.json --update
   ```

### Stage B: Cover Reachable Service State Machines & Ratify Final Baseline
1. Inspect the remaining classes in `coverage-baseline.json`:
   - `UserAuthenticationService/<ValidateTokenAsync>` & `<VerifyChallengeAndIssueTokenAsync>`
   - `UserProfileRepository` (`GetProfileAsync`, `GetAddressesAsync`, `DeleteAddressAsync`, `GetContactsAsync`, `DeleteContactAsync`)
   - `ChannelEventBus/<PublishAsync>`
   - `StackServiceManager` (`GetServiceByIdAsync`, `ExtractAcceleratorAsync`)
   - `ValkeyCacheService` / `ValkeyLockService` / `ValkeyEventBridge`
2. Add targeted unit tests in `tests/NanoWeaverHrt.Tests/` exercising the reachable branches.
3. Re-run `python3 -I scripts/check-coverage-ratchet.py artifacts/coverage-full coverage-baseline.json --update`.
4. Record the final `line_rate`, `branch_rate`, `uncovered_lines_total`, and the exact per-class breakdown of any remaining compiler-generated lines.

---

## 4. Verification Gate Checklist

Run the full verification gate:
```bash
./scripts/verify-all.sh
```
Confirm all steps pass with zero regressions and that `coverage-baseline.json` matches the un-excluded measurement.
