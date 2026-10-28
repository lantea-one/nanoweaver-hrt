# Phase P25: Full Verification, Documentation & Coverage Ratchet Closeout

## 1. Context & Architectural Decisions

- **Goal**: Execute full pre-flight verification gate (`./scripts/verify-all.sh`), document subject access gating and cohort management across Docsify documentation, and update coverage ratchet baseline.
- **Verification Invariants**:
  - Step 1: `dotnet format --verify-no-changes` (0 format issues).
  - Step 2: `npm test` (100% line, branch, function JS coverage).
  - Step 3: `dotnet test` (100% C# test pass rate).
  - Step 4: Cobertura coverage gate (100.00% line and branch coverage threshold on measured scope).
  - Step 5: Markdown scan (0 unescaped single-dollar LaTeX tokens).
  - Step 6: Docblock scan (`scripts/check-docblocks.py`: 100% complete docblocks with `<param>` and `<returns>`).
  - Step 7: Un-excluded coverage ratchet (`scripts/check-coverage-ratchet.py`: downward-only ratchet baseline).
  - Step 8: Vendored JS & SRI integrity check.

---

## 2. Step-by-Step Implementation Instructions

### Step 2.1: Documentation Update
- Update `docs/compliance-and-security.md` and `docs/api-reference.md` describing:
  - Subject and group access delegation model.
  - Three configurable access levels (`ReadAccess: 1`, `GenomicAccess: 2`, `FullReadAccess: 3`).
  - Cohort management and privacy boundaries.
- Ensure all physical units and chemical formulas use clean Unicode without single-dollar math delimiters.

### Step 2.2: Universal Pre-Flight Gate & Ratchet Closeout
- Run `XDG_RUNTIME_DIR=/tmp dotnet format NanoWeaverHrt.slnx --no-restore`.
- Run `./scripts/verify-all.sh`.
- If Step 7 reports improved baseline, ratify with:
  `python3 -I scripts/check-coverage-ratchet.py artifacts/coverage-full coverage-baseline.json --update`
- Re-run `./scripts/verify-all.sh` to ensure all 8 steps exit with code 0.

### Step 2.3: Final Git Commit & Push
- Update `work-packages/README.md` recording completion of P21–P25.
- Commit all changes:
  `feat(access,cohorts): implement subject access gating, tiered permissions, and cohort management (P21-P25)`
- Push directly to `origin/main` on sovereign Forgejo (`git.lantea.one`).
