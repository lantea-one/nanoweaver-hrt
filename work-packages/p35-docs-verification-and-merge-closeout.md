# Work Package P35: Documentation, GitHub Pages, End-to-End Verification & Merge Closeout

## 0. Fix your base commit FIRST (before reading anything else)
```bash
git checkout feature/gui-application
```

## 1. Context & Objective
- **Problem Statement**: Before merging `feature/gui-application` into `main` and deleting the feature branch, all user-facing documentation, Docsify GitHub Pages navigation, compliance/security specifications, and release notes must be updated and verified against all 8 quality gates (`./scripts/verify-all.sh`), and CI workflows on `feature/gui-application` must pass.
- **Goal & Scope**:
  - **Included**:
    1. Author `docs/desktop-and-mobile-gui.md` covering:
       - Multi-instance connection architecture (local loopback + remote HTTPS instances, Ed25519 & ML-DSA-65 client-side key vault, 15-minute idle lock, background tray daemon & notifications).
       - Linux installation instructions for ArchLinux (`pacman -U nanoweaver-hrt-desktop-x86_64.pkg.tar.zst` / `arm64`, plus AUR `nanoweaver-hrt-desktop-bin` and `nanoweaver-hrt-desktop-git`), Fedora (`dnf install ./nanoweaver-hrt-desktop-amd64.rpm` / `arm64`), and Debian/Ubuntu (`apt install ./nanoweaver-hrt-desktop-amd64.deb` / `arm64`).
       - macOS installation (`NanoWeaverHrt.dmg` -> `/Applications/NanoWeaverHrt.app` & Gatekeeper trust instructions).
       - Windows installation (`nanoweaver-hrt-desktop-installer.exe` `Current User` vs `System` Administrator installation modes, plus sideloading `NanoWeaverHrt.msix`).
       - Mobile sideloading instructions for Android (`nanoweaver-hrt-mobile-android.apk`), iOS (`nanoweaver-hrt-mobile-ios.ipa`), and iPadOS (`nanoweaver-hrt-mobile-ipados.ipa`).
       - Sovereign Code-Signing PKI & 90-day automated rotation architecture (including why Let's Encrypt ACME only issues `serverAuth` TLS certificates and how to import `docs/assets/keys/nanoweaver-codesign-root-ca.crt` and `nanoweaver-archive-keyring.asc`).
    2. Update `docs/_sidebar.md`, `docs/compliance-and-security.md`, `docs/release-notes.md`, and `README.md`.
    3. Clean up temporary `work-packages/p26-*.md` through `p35-*.md` instruction files once consolidated into `work-packages/PHASES-v2.md` and `work-packages/README.md`.
    4. Merge `feature/gui-application` into `main`, push `main` to `origin` (`git.lantea.one`), verify actions succeed, and delete the `feature/gui-application` branch locally and on `origin`.

## 2. Technical Contracts & Invariants

### 2.1 Documentation & Markdown Rendering Invariants (Prime Directive 8)
- **Zero Single-Dollar Delimiters**: Never use `$ ... $` or inline `$$ ... $$` in any Markdown file under `docs/` or `README.md`. Be especially careful when writing shell examples containing environment variables (such as `$HOME` or `$LOCALAPPDATA`) outside fenced code blocks—always wrap shell variables in backticks (`` `$HOME` ``) so Gate Step 5 does not mistake pairs of `$` signs for inline LaTeX math!

### 2.2 Clean-Up & Branch Merge Protocol (Prime Directive 15)
1. Verify all 8 checks pass on `feature/gui-application`:
   ```bash
   XDG_RUNTIME_DIR=/tmp dotnet format NanoWeaverHrt.slnx --no-restore && ./scripts/verify-all.sh
   ```
2. Record final completion status of `P26–P35` in `work-packages/README.md` and `work-packages/PHASES-v2.md`, and remove temporary `work-packages/p26-*.md` through `work-packages/p35-*.md` files.
3. Push `feature/gui-application` to `origin` (`git.lantea.one`) and verify CI workflow success.
4. Merge into `main` and clean up `feature/gui-application`:
   ```bash
   git checkout main
   git merge --no-ff feature/gui-application -m "Merge branch 'feature/gui-application': standalone desktop and mobile GUI applications"
   git push origin main
   git push origin --delete feature/gui-application
   git branch -d feature/gui-application
   ```

## 3. Step-by-Step Implementation Tasks
1. **Task 1**: Create `docs/desktop-and-mobile-gui.md` and link it in `docs/_sidebar.md` and `README.md`.
2. **Task 2**: Update `docs/compliance-and-security.md` and `docs/release-notes.md` with the Sovereign Code-Signing PKI (`id-kp-codeSigning` 90-day automated rotation) and client key-ring vault details.
3. **Task 3**: Consolidate `P26–P35` into `work-packages/README.md` and `work-packages/PHASES-v2.md`, and remove temporary `work-packages/p26-*.md`..`p35-*.md` files.
4. **Task 4**: Run `./scripts/verify-all.sh` and `scripts/packaging/build-all-packages.sh`.
5. **Task 5**: Push `feature/gui-application`, verify CI passes, merge into `main`, push `main` to `origin`, and delete `feature/gui-application`.

## 4. Verification Gate & Acceptance Criteria
- [ ] `./scripts/verify-all.sh` passes all 8 gates with 100.00% line & branch coverage and zero markdown math violations.
- [ ] `docs/desktop-and-mobile-gui.md` is linked in `docs/_sidebar.md` for GitHub Pages (Docsify).
- [ ] `feature/gui-application` is cleanly merged into `main` and deleted after CI succeeds.
