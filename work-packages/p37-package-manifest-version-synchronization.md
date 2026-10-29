# Work Package P37: Package Manifest Version Dynamic Synchronization

## 1. Context & Objective
- **Problem Statement**:
  Platform packaging manifests and control files (`AppxManifest.xml`, `nanoweaver-hrt-installer.nsi`, `Info.plist`, `metainfo.xml`, `build-linux-packages.sh`, `build-mobile-packages.sh`, and AUR files) have hardcoded `1.0.0` version strings rather than dynamically synchronizing with the authoritative release tag (e.g., `2026.10.29.2`).
- **Goal & Scope**:
  1. Parameterize package versioning in `scripts/packaging/build-linux-packages.sh` and `scripts/packaging/build-mobile-packages.sh` with `PACKAGE_VERSION="${PACKAGE_VERSION:-${GITHUB_REF_NAME#v}}"`, falling back to `git describe --tags --exact-match 2>/dev/null || echo "2026.10.29.2"`.
  2. Update Windows NSIS installer and MSIX manifests to QuadVersion format `2026.10.29.2`.
  3. Update macOS `Info.plist`, AppStream metadata, and AUR definitions.
- **Target Role / Model**: `mechanic` (Recommended model: `flash_lite` or `flash`)
- **Transport Boundary**: Build & Packaging Infrastructure

---

## 2. Technical Contracts & Invariants
- **Files Modified**:
  - `scripts/packaging/build-linux-packages.sh`
  - `scripts/packaging/build-mobile-packages.sh`
  - `packaging/windows/AppxManifest.xml`
  - `packaging/windows/nanoweaver-hrt-installer.nsi`
  - `packaging/macos/Info.plist`
  - `packaging/linux/org.lantea.NanoWeaverHrt.metainfo.xml`
  - `packaging/aur/nanoweaver-hrt-desktop-bin/PKGBUILD`
  - `packaging/aur/nanoweaver-hrt-desktop-bin/.SRCINFO`
- **Invariants**:
  - Windows MSIX requires `QuadVersion`: `Major.Minor.Build.Revision` (all <= 65535). `2026.10.29.2` satisfies this format.
  - Debian package versions must be clean: `<version>-1` (e.g. `2026.10.29.2-1`).
  - RPM versions cannot contain hyphens in the `Version` tag; use `Version: 2026.10.29.2`, `Release: 1`.
  - Zero single-dollar LaTeX math delimiters in documentation or XML comments.

---

## 3. Step-by-Step Implementation Tasks
1. **Task 1 (Linux Packaging Script Parameterization)**:
   - Introduce `PACKAGE_VERSION` variable at top of `scripts/packaging/build-linux-packages.sh`.
   - Update Debian control generation, RPM spec generation, and Arch package generation to use `${PACKAGE_VERSION}`.
2. **Task 2 (Mobile Packaging Script Parameterization)**:
   - Introduce `PACKAGE_VERSION` in `scripts/packaging/build-mobile-packages.sh` for Android `AndroidManifest.xml` and iOS `Info.plist`.
3. **Task 3 (Windows Manifest Synchronization)**:
   - Update `packaging/windows/AppxManifest.xml` `Version="2026.10.29.2"`.
   - Update `packaging/windows/nanoweaver-hrt-installer.nsi` `!define PRODUCT_VERSION "2026.10.29.2"`.
4. **Task 4 (macOS & Metadata Synchronization)**:
   - Update `packaging/macos/Info.plist` `CFBundleShortVersionString` and `CFBundleVersion` to `2026.10.29.2`.
   - Add `<release version="2026.10.29.2" date="2026-10-29">` to `packaging/linux/org.lantea.NanoWeaverHrt.metainfo.xml`.
   - Update AUR PKGBUILD to `pkgver=2026.10.29.2`.

---

## 4. Verification Gate & Acceptance Criteria
- [ ] Package generation scripts read `PACKAGE_VERSION` properly.
- [ ] Windows MSIX and NSIS installer compile with `2026.10.29.2`.
- [ ] `./scripts/verify-all.sh` passes all 8 quality gates.
