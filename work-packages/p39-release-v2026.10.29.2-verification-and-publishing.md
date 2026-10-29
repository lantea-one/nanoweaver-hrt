# Work Package P39: Release v2026.10.29.2 Verification & Upstream Publishing

## 1. Context & Objective
- **Problem Statement**:
  Following completion of P36, P37, and P38, the platform requires complete end-to-end quality gate verification, package generation, release documentation, git tagging, and automated release asset publication across sovereign Forgejo and GitHub.
- **Goal & Scope**:
  1. Execute `./scripts/verify-all.sh` asserting 100.00% line & branch code coverage, zero formatting defects, and 100% ratchet baseline compliance.
  2. Execute `./scripts/packaging/build-all-packages.sh` verifying all 16 platform packages and cryptographic signatures.
  3. Document changes in `docs/release-notes.md` under `## Release v2026.10.29.2`.
  4. Tag `v2026.10.29.2`, push to `origin main` on sovereign Forgejo remote (`git.lantea.one`), and monitor CI release workflows.
- **Target Role / Model**: `architect` / parent coordinator (Recommended model: `inherit` or `pro`)
- **Transport Boundary**: Authoritative Release Pipeline (`origin main`)

---

## 2. Technical Contracts & Invariants
- **Files Modified**:
  - `docs/release-notes.md`
  - `work-packages/README.md`
- **Invariants**:
  - Never push directly to GitHub; only push to `origin main` on `git.lantea.one`.
  - Maintain 100.00% Line & Branch coverage on all developer code.
  - Zero unescaped single-dollar LaTeX math delimiters across documentation.
  - All 16 platform release artifacts must be signed with detached CMS (`.p7s`) and OpenPGP (`.asc`) signatures verified against `nanoweaver-codesign-root-ca.crt`.

---

## 3. Step-by-Step Implementation Tasks
1. **Task 1 (Local Verification Gate)**:
   - Run `XDG_RUNTIME_DIR=/tmp dotnet format NanoWeaverHrt.slnx --verify-no-changes`.
   - Run `./scripts/verify-all.sh` and ensure all 8 steps pass cleanly.
2. **Task 2 (Full Cross-Platform Packaging)**:
   - Run `./scripts/packaging/build-all-packages.sh`.
   - Assert `nanoweaver-hrt-desktop-portable-x64.exe` is a genuine PE32+ executable.
3. **Task 3 (Release Notes & Work Package Ratification)**:
   - Add `## Release v2026.10.29.2 (Desktop & Packaging Hardening — 2026-10-29)` to `docs/release-notes.md`.
   - Update `work-packages/README.md`.
4. **Task 4 (Tagging & Upstream Push)**:
   - Commit changes to `main`.
   - Tag `v2026.10.29.2`.
   - Push `main` and `v2026.10.29.2` to `origin`.
5. **Task 5 (Release Verification)**:
   - Monitor Forgejo Actions and GitHub Actions runs to conclusion `success`.
   - Verify direct `.exe` and platform downloads on Forgejo and GitHub release pages.

---

## 4. Verification Gate & Acceptance Criteria
- [ ] All 8 quality gates pass.
- [ ] Tag `v2026.10.29.2` is live on Forgejo and GitHub.
- [ ] Direct single-click `.exe` download URLs are functional.
