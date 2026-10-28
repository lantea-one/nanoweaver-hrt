# Work Package P32: Linux Binary Packaging & ArchLinux AUR Packages

## 0. Fix your base commit FIRST (before reading anything else)
```bash
git checkout feature/gui-application
```

## 1. Context & Objective
- **Problem Statement**: We need reproducible Linux binary packages for `amd64` (`linux-x64`) and `arm64` (`linux-arm64`) across ArchLinux, Fedora, Debian, and Ubuntu, plus two ArchLinux User Repository (AUR) package definitions (`nanoweaver-hrt-desktop-bin` and `nanoweaver-hrt-desktop-git`).
- **Ratified Naming & Consolidation Decision**:
  - Because our published `.NET 10` desktop binary is self-contained and links only against universal POSIX/X11/Wayland/Fontconfig/DBus libraries, Debian and Ubuntu share unified `.deb` packages, and Fedora/RHEL/openSUSE share unified `.rpm` packages:
    | Distribution | Package Filename | Architecture |
    | :--- | :--- | :--- |
    | ArchLinux | `nanoweaver-hrt-desktop-x86_64.pkg.tar.zst` | `amd64` (`x86_64`) |
    | ArchLinux | `nanoweaver-hrt-desktop-arm64.pkg.tar.zst` | `arm64` (`aarch64`) |
    | Fedora (Unified RPM) | `nanoweaver-hrt-desktop-amd64.rpm` | `amd64` (`x86_64`) |
    | Fedora (Unified RPM) | `nanoweaver-hrt-desktop-arm64.rpm` | `arm64` (`aarch64`) |
    | Debian & Ubuntu (Unified DEB) | `nanoweaver-hrt-desktop-amd64.deb` | `amd64` |
    | Debian & Ubuntu (Unified DEB) | `nanoweaver-hrt-desktop-arm64.deb` | `arm64` |
  - Plus two ArchLinux AUR packages:
    - `packaging/aur/nanoweaver-hrt-desktop-bin/PKGBUILD` & `.SRCINFO`
    - `packaging/aur/nanoweaver-hrt-desktop-git/PKGBUILD` & `.SRCINFO`

## 2. Technical Contracts & Invariants

### 2.1 Filesystem Layout Inside Linux Packages
Every Linux package (`.pkg.tar.zst`, `.deb`, `.rpm`) installs the following FHS-compliant hierarchy:
- `/usr/lib/nanoweaver-hrt-desktop/NanoWeaverHrt` (chmod `0755`, self-contained .NET 10 executable)
- `/usr/bin/nanoweaver-hrt-desktop` (chmod `0755`, symlink or exec wrapper to `/usr/lib/nanoweaver-hrt-desktop/NanoWeaverHrt`)
- `/usr/share/applications/nanoweaver-hrt-desktop.desktop` (FreeDesktop entry with `Categories=Science;Biology;MedicalSoftware;`, `StartupWMClass=NanoWeaverHrt`)
- `/etc/xdg/autostart/nanoweaver-hrt-desktop-tray.desktop` (starts `nanoweaver-hrt-desktop --minimized-tray` in the background on desktop login)
- `/usr/share/icons/hicolor/scalable/apps/nanoweaver-hrt-desktop.svg`
- `/usr/share/metainfo/org.lantea.NanoWeaverHrt.metainfo.xml`
- `/usr/share/licenses/nanoweaver-hrt-desktop/LICENSE`

### 2.2 Unified `.deb` Control Metadata (Debian 12/13 + Ubuntu 22.04/24.04+)
- `Package: nanoweaver-hrt-desktop`
- `Architecture: amd64` (or `arm64`)
- `Depends: libc6 (>= 2.31), libgcc-s1 | libgcc1, libstdc++6, libssl3 | libssl1.1, zlib1g, libx11-6, libice6, libsm6, libfontconfig1, libdbus-1-3, libnotify-bin`
- Built deterministically via `dpkg-deb --root-owner-group --build` (works on any Linux host, including ArchLinux and Ubuntu runners, without needing root privileges).

### 2.3 Unified `.rpm` Spec / Builder (Fedora / RHEL / openSUSE)
- `Name: nanoweaver-hrt-desktop`
- `Requires: glibc, libX11, libICE, libSM, fontconfig, dbus-libs, libnotify`
- Built via `rpmbuild -bb --target x86_64-linux` / `aarch64-linux` (or deterministic RPM header/cpio builder fallback if `rpmbuild` is absent on minimal containers), outputting exact filenames `nanoweaver-hrt-desktop-amd64.rpm` and `nanoweaver-hrt-desktop-arm64.rpm`.

### 2.4 ArchLinux `.pkg.tar.zst` & AUR Packages
1. **Binary Package Builder (`scripts/packaging/build-linux-packages.sh`)**:
   - Generates `.PKGINFO` and `.BUILDINFO` with `pkgname = nanoweaver-hrt-desktop`, `arch = x86_64` (for `nanoweaver-hrt-desktop-x86_64.pkg.tar.zst`) and `arch = aarch64` (for `nanoweaver-hrt-desktop-arm64.pkg.tar.zst`) compressed with `zstd -c -T0`.
2. **AUR `nanoweaver-hrt-desktop-bin` (`packaging/aur/nanoweaver-hrt-desktop-bin/PKGBUILD`)**:
   - `pkgname=nanoweaver-hrt-desktop-bin`
   - `arch=('x86_64' 'aarch64')`
   - `provides=('nanoweaver-hrt-desktop')`
   - `conflicts=('nanoweaver-hrt-desktop' 'nanoweaver-hrt-desktop-git')`
   - `source_x86_64=("https://git.lantea.one/fabigail/nanoweaver-hrt/releases/download/v${pkgver}/nanoweaver-hrt-desktop-x86_64.pkg.tar.zst")`
   - `source_aarch64=("https://git.lantea.one/fabigail/nanoweaver-hrt/releases/download/v${pkgver}/nanoweaver-hrt-desktop-arm64.pkg.tar.zst")`
3. **AUR `nanoweaver-hrt-desktop-git` (`packaging/aur/nanoweaver-hrt-desktop-git/PKGBUILD`)**:
   - `pkgname=nanoweaver-hrt-desktop-git`
   - `arch=('x86_64' 'aarch64')`
   - `makedepends=('git' 'dotnet-sdk>=10.0')`
   - `provides=('nanoweaver-hrt-desktop')`
   - `conflicts=('nanoweaver-hrt-desktop' 'nanoweaver-hrt-desktop-bin')`
   - `source=("git+https://git.lantea.one/fabigail/nanoweaver-hrt.git#branch=main")`
   - `build()` runs `dotnet publish src/NanoWeaverHrt.Desktop/NanoWeaverHrt.Desktop.csproj -c Release --self-contained true` and `package()` installs the binary, `.desktop` launcher, autostart tray entry, icon, and license into `${pkgdir}`.

## 3. Step-by-Step Implementation Tasks
1. **Task 1**: Create desktop integration assets under `packaging/linux/` (`nanoweaver-hrt-desktop.desktop`, `nanoweaver-hrt-desktop-tray.desktop`, `org.lantea.NanoWeaverHrt.metainfo.xml`, `nanoweaver-hrt-desktop.svg`).
2. **Task 2**: Create `packaging/aur/nanoweaver-hrt-desktop-bin/PKGBUILD`, `packaging/aur/nanoweaver-hrt-desktop-bin/.SRCINFO`, `packaging/aur/nanoweaver-hrt-desktop-git/PKGBUILD`, and `packaging/aur/nanoweaver-hrt-desktop-git/.SRCINFO`.
3. **Task 3**: Create `scripts/packaging/build-linux-packages.sh` that publishes `src/NanoWeaverHrt.Desktop` for `linux-x64` and `linux-arm64` and produces all 6 Linux binary artifacts in `artifacts/packages/linux/`:
   - `nanoweaver-hrt-desktop-x86_64.pkg.tar.zst`
   - `nanoweaver-hrt-desktop-arm64.pkg.tar.zst`
   - `nanoweaver-hrt-desktop-amd64.rpm`
   - `nanoweaver-hrt-desktop-arm64.rpm`
   - `nanoweaver-hrt-desktop-amd64.deb`
   - `nanoweaver-hrt-desktop-arm64.deb`
4. **Task 4**: Run `scripts/packaging/build-linux-packages.sh` and `./scripts/verify-all.sh` to verify all 6 Linux packages and both AUR `PKGBUILD` definitions.

## 4. Verification Gate & Acceptance Criteria
- [ ] All 6 Linux packages exist in `artifacts/packages/linux/` with non-zero size and valid archive headers.
- [ ] Both `nanoweaver-hrt-desktop-bin` and `nanoweaver-hrt-desktop-git` `PKGBUILD` and `.SRCINFO` files pass syntax and metadata validation.
- [ ] `./scripts/verify-all.sh` passes all 8 gates.
