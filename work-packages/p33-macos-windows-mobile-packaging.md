# Work Package P33: macOS, Windows (Dual-Mode Installer + MSIX) & Mobile Artifact Packaging

## 0. Fix your base commit FIRST (before reading anything else)
```bash
git checkout feature/gui-application
```

## 1. Context & Objective
- **Problem Statement**: In addition to Linux packages, `nanoweaver-hrt` requires cross-platform binary packages for macOS (`NanoWeaverHrt.app` for `/Applications`), Windows (`nanoweaver-hrt-desktop-installer.exe` supporting Current User or Administrator System installation, plus a Windows Store `NanoWeaverHrt.msix` artifact), and Mobile (`Android`, `iOS`, and `iPadOS`) attached directly to CI/release artifacts without requiring paid developer store subscriptions.
- **Goal & Scope**:
  - **Included**:
    1. **macOS (`scripts/packaging/build-macos-packages.sh`)**: Builds `NanoWeaverHrt.app` bundle (`Contents/Info.plist`, `Contents/MacOS/NanoWeaverHrt`, `Contents/Resources/NanoWeaverHrt.icns`) packaged as `NanoWeaverHrt-macos-amd64.dmg` / `NanoWeaverHrt-macos-arm64.dmg` (and `NanoWeaverHrt-macos-universal.app.tar.gz`) ready to install into `/Applications`.
    2. **Windows (`scripts/packaging/build-windows-packages.sh` & `packaging/windows/nanoweaver-hrt-installer.nsi`)**:
       - Builds `nanoweaver-hrt-desktop-installer.exe`:
         - If running as a standard user: installs to **Current User** (`%LocalAppData%\Programs\NanoWeaverHrt`).
         - If running as an **Administrator**: presents an interactive installation mode page (`MultiUser.nsh` / custom radio selection) allowing the Administrator to choose between **Current User** (`HKCU` / `%LocalAppData%\Programs\NanoWeaverHrt`) or **System (All Users)** (`HKLM` / `%ProgramFiles%\NanoWeaverHrt`), defaulting to Current User so elevated users are never forced into a machine-wide install.
       - Builds Windows Store package `NanoWeaverHrt.msix` (`AppxManifest.xml`, `[Content_Types].xml`, `AppxBlockMap.xml`, signed with the P31 sovereign X.509 `id-kp-codeSigning` certificate) for artifact attachment (no Windows Store deployment).
    3. **Mobile (`scripts/packaging/build-mobile-packages.sh`)**:
       - Builds Android package `nanoweaver-hrt-mobile-android.apk` (signed with the P31 sovereign keystore for direct sideloading).
       - Builds iOS package `nanoweaver-hrt-mobile-ios.ipa` (`Payload/NanoWeaverHrt.app` with `UIDeviceFamily = [1]`).
       - Builds iPadOS package `nanoweaver-hrt-mobile-ipados.ipa` (`Payload/NanoWeaverHrt.app` with `UIDeviceFamily = [2]`, multitasking & split-view traits enabled).

## 2. Technical Contracts & Invariants

### 2.1 Artifact Naming Matrix
All artifacts are output to `artifacts/packages/{macos,windows,mobile}/`:
| Platform | Artifact Filename | Description |
| :--- | :--- | :--- |
| macOS (`amd64` & `arm64`) | `NanoWeaverHrt.dmg` (plus arch-specific `NanoWeaverHrt-macos-amd64.dmg` & `NanoWeaverHrt-macos-arm64.dmg`) | Standard macOS `NanoWeaverHrt.app` bundle with `/Applications` drop link |
| Windows Installer | `nanoweaver-hrt-desktop-installer.exe` | Dual-mode (`Current User` or `System` when Administrator, with explicit choice to install as `Current User`) |
| Windows Store Package | `NanoWeaverHrt.msix` | Signed MSIX AppX bundle attached to build/release artifacts |
| Android | `nanoweaver-hrt-mobile-android.apk` | Signed sideloadable Android application package |
| iOS | `nanoweaver-hrt-mobile-ios.ipa` | Sideloadable iOS application archive (`UIDeviceFamily` iPhone) |
| iPadOS | `nanoweaver-hrt-mobile-ipados.ipa` | Sideloadable iPadOS application archive (`UIDeviceFamily` iPad) |

### 2.2 Windows Installer Dual-Scope Requirement (`packaging/windows/nanoweaver-hrt-installer.nsi`)
- Uses `RequestExecutionLevel highest` (or `MULTIUSER_EXECUTIONLEVEL Highest` with `MULTIUSER_MUI`) so:
  - Standard users can install to `Current User` without UAC failure.
  - Administrators are explicitly shown the **Choose Installation Scope** page with two options:
    1. `Install just for me (Current User)` (`$LOCALAPPDATA\Programs\NanoWeaverHrt`)
    2. `Install for all users on this machine (System - Administrator)` (`$PROGRAMFILES64\NanoWeaverHrt`)
  - Includes silent CLI flags `/CURRENTUSER` and `/ALLUSERS` for automated verification.

## 3. Step-by-Step Implementation Tasks
1. **Task 1**: Create `packaging/macos/Info.plist` and `scripts/packaging/build-macos-packages.sh` to publish `src/NanoWeaverHrt.Desktop` for `osx-x64` and `osx-arm64`, assemble `NanoWeaverHrt.app`, and package `NanoWeaverHrt.dmg`.
2. **Task 2**: Create `packaging/windows/nanoweaver-hrt-installer.nsi`, `packaging/windows/AppxManifest.xml`, and `scripts/packaging/build-windows-packages.sh` to publish `src/NanoWeaverHrt.Desktop` for `win-x64`, build `nanoweaver-hrt-desktop-installer.exe` and `NanoWeaverHrt.msix`, and sign them via `scripts/pki/sign-packages.sh`.
3. **Task 3**: Create `scripts/packaging/build-mobile-packages.sh` to publish `src/NanoWeaverHrt.Mobile` and package `nanoweaver-hrt-mobile-android.apk`, `nanoweaver-hrt-mobile-ios.ipa`, and `nanoweaver-hrt-mobile-ipados.ipa`.
4. **Task 4**: Create master wrapper `scripts/packaging/build-all-packages.sh` that runs Linux (P32), macOS, Windows, Mobile, and PKI Code Signing (P31) end-to-end and verifies all expected artifact filenames.
5. **Task 5**: Run `./scripts/verify-all.sh` to confirm all 8 verification gates pass.

## 4. Verification Gate & Acceptance Criteria
- [ ] `scripts/packaging/build-all-packages.sh` produces all Linux, macOS, Windows (`nanoweaver-hrt-desktop-installer.exe` + `NanoWeaverHrt.msix`), and Mobile (`Android`, `iOS`, `iPadOS`) signed artifacts and `SHA256SUMS`.
- [ ] `packaging/windows/nanoweaver-hrt-installer.nsi` explicitly allows Administrators to choose `Current User` instead of `System`.
- [ ] `./scripts/verify-all.sh` passes all 8 gates.
