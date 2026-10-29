# Work Package P46: Wet-Lab & Localization Verification, Wiki Documentation & Release Ratification

## 1. Context & Objective
- **Problem Statement**:
  Following completion of P40 through P45, the platform incorporates both Opentrons liquid handling automation and multi-language community localization. The system requires end-to-end quality gate verification across all 8 checks, comprehensive documentation in the Docsify wiki, publication of release notes, and version tagging.
- **Goal & Scope**:
  1. Execute `./scripts/verify-all.sh` asserting 100.00% Line & Branch coverage, zero build warnings, 0 docblock errors, and ratchet baseline compliance.
  2. Create documentation guide `docs/opentrons-automation.md` explaining automated OT-2/Flex pipetting, 96-well plate mapping, and on-deck thermocycler integration.
  3. Create documentation guide `docs/localization.md` explaining supported community languages, translation catalog structure, and contributing new language packs.
  4. Update `docs/_sidebar.md` and `docs/index.md` with the new documentation sections.
  5. Add release notes in `docs/release-notes.md` under `## Release v2026.10.29.3 (Opentrons Wet-Lab Automation & Community Localization)`.
  6. Update `work-packages/README.md` to record completion of P40–P46.
  7. Remove temporary work package instruction files (`p40-*.md` through `p46-*.md`) per the Clean Up Protocol.
- **Target Role / Model**: `architect` / parent coordinator (`inherit` / `pro`)
- **Transport Boundary**: System-Wide & CI/CD Release Pipeline

---

## 2. Technical Contracts & Invariants
- **Documentation Invariant**: Never wrap physical units (`50 µL`, `10 nM`) or chemical formulas in single-dollar LaTeX delimiters (`$ ... $`). Use clean Unicode symbols.
- **Coverage Invariant**: Step 4 coverage must be strictly `100.00%` Line and `100.00%` Branch.
- **Ratchet Invariant**: If un-excluded coverage improves, update `coverage-baseline.json` via `--update` flag.

---

## 3. Step-by-Step Implementation Tasks
1. Run pre-flight verification: `./scripts/verify-all.sh`.
2. Author `docs/opentrons-automation.md`.
3. Author `docs/localization.md`.
4. Update `docs/_sidebar.md` and `docs/index.md`.
5. Update `docs/release-notes.md` with release `v2026.10.29.3` notes.
6. Clean up temporary work package instruction files.
7. Commit, tag (`v2026.10.29.3`), push to `origin main` on `git.lantea.one`, and verify CI/CD release builds.

---

## 4. Verification Gate & Acceptance Criteria
- [ ] All 8 pre-flight gates pass (`./scripts/verify-all.sh`).
- [ ] Documentation renders cleanly on Docsify without broken markdown or LaTeX interpolation errors.
- [ ] Release pipeline succeeds on Forgejo and GitHub Actions.
