# Work Package P34: Forgejo & Mirrored GitHub Actions Multi-Platform Artifact Matrix

## 0. Fix your base commit FIRST (before reading anything else)
```bash
git checkout feature/gui-application
```

## 1. Context & Objective
- **Problem Statement**: Forgejo (`git.lantea.one`) is our authoritative sovereign Git remote and mirrors automatically to GitHub on its backend. Both Forgejo Actions and GitHub Actions must run the full verification suite and build, sign, and attach all standalone GUI application packages (Linux `.pkg.tar.zst`, `.deb`, `.rpm`, AUR `PKGBUILD`s, macOS `NanoWeaverHrt.dmg`, Windows `nanoweaver-hrt-desktop-installer.exe` & `NanoWeaverHrt.msix`, and Mobile `.apk` & `.ipa`) as downloadable workflow and release artifacts on both `feature/gui-application` and `main`.
- **Goal & Scope**:
  - **Included**:
    - Update `.forgejo/workflows/build-and-deploy.yml` and `.github/workflows/build-and-deploy.yml` triggers to include `feature/gui-application` alongside `main` and `v*` tags.
    - Add a `build-and-publish-gui-artifacts` job to `.forgejo/workflows/build-and-deploy.yml` (pinned to `runs-on: archlinux-multi-platform`) and `.github/workflows/build-and-deploy.yml` (`runs-on: ubuntu-latest`) that executes `scripts/packaging/build-all-packages.sh`, signs all artifacts with `scripts/pki/sign-packages.sh`, and uploads all desktop, mobile, AUR, and `SHA256SUMS` artifacts via `actions/upload-artifact@v4`.
    - Ensure `.github/workflows/deploy-pages.yml` deploys updated documentation once merged into `main`.

## 2. Technical Contracts & Invariants

### 2.1 Uploaded Artifact Bundle Inventory (Both Forgejo & GitHub Actions)
Every workflow run on `feature/gui-application`, `main`, or `v*` tags must publish the following artifacts:
1. **Linux Packages (`nanoweaver-hrt-linux-packages`)**:
   - `nanoweaver-hrt-desktop-x86_64.pkg.tar.zst` (+ `.sig`)
   - `nanoweaver-hrt-desktop-arm64.pkg.tar.zst` (+ `.sig`)
   - `nanoweaver-hrt-desktop-amd64.rpm` (+ `.sig`)
   - `nanoweaver-hrt-desktop-arm64.rpm` (+ `.sig`)
   - `nanoweaver-hrt-desktop-amd64.deb` (+ `.sig`)
   - `nanoweaver-hrt-desktop-arm64.deb` (+ `.sig`)
   - `aur/nanoweaver-hrt-desktop-bin/PKGBUILD` & `.SRCINFO`
   - `aur/nanoweaver-hrt-desktop-git/PKGBUILD` & `.SRCINFO`
2. **macOS Packages (`nanoweaver-hrt-macos-packages`)**:
   - `NanoWeaverHrt.dmg` (`NanoWeaverHrt-macos-amd64.dmg` & `NanoWeaverHrt-macos-arm64.dmg`) (+ `.sig`)
3. **Windows Packages (`nanoweaver-hrt-windows-packages`)**:
   - `nanoweaver-hrt-desktop-installer.exe` (+ `.sig`)
   - `NanoWeaverHrt.msix` (+ `.sig`)
4. **Mobile Packages (`nanoweaver-hrt-mobile-packages`)**:
   - `nanoweaver-hrt-mobile-android.apk` (+ `.sig`)
   - `nanoweaver-hrt-mobile-ios.ipa` (+ `.sig`)
   - `nanoweaver-hrt-mobile-ipados.ipa` (+ `.sig`)
5. **Cryptographic Manifest & Trust Bundle (`nanoweaver-hrt-codesign-manifest`)**:
   - `SHA256SUMS`, `SHA256SUMS.sig`, `nanoweaver-codesign-root-ca.crt`, `nanoweaver-codesign-leaf.crt`, `nanoweaver-archive-keyring.asc`

### 2.2 Sovereign Git Remote Rule (Prime Directive 11)
- Never configure any workflow or local git remote to push directly to `github.com`. All commits and pushes go strictly to `origin` (`git.lantea.one`), which mirrors to GitHub on its backend.
- During `feature/gui-application` branch builds, container registry pushes (`docker/build-push-action`) remain gated to `github.ref == 'refs/heads/main' || startsWith(github.ref, 'refs/tags/v')` so feature branch builds do not overwrite `:latest` production container tags while still building and publishing all GUI artifacts.

## 3. Step-by-Step Implementation Tasks
1. **Task 1**: Update `.forgejo/workflows/build-and-deploy.yml` to trigger on `branches: [ "main", "feature/gui-application" ]`, run all 8 verification checks, and execute `build-and-publish-gui-artifacts`.
2. **Task 2**: Update `.github/workflows/build-and-deploy.yml` with matching triggers, all 8 verification checks, and `build-and-publish-gui-artifacts`.
3. **Task 3**: Validate YAML syntax of all `.forgejo/workflows/*.yml` and `.github/workflows/*.yml` files via Python `yaml.safe_load`.
4. **Task 4**: Run `./scripts/verify-all.sh`.

## 4. Verification Gate & Acceptance Criteria
- [ ] All `.forgejo/workflows/*.yml` and `.github/workflows/*.yml` parse cleanly with zero YAML errors.
- [ ] Both workflows build and upload all Linux, AUR, macOS, Windows, and Mobile artifacts.
- [ ] `./scripts/verify-all.sh` passes all 8 gates.
