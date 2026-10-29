# Work Package P36: Packaging Binary Pickup & macOS Universal Mach-O Alignment

## 1. Context & Objective
- **Problem Statement**: 
  1. In `scripts/packaging/build-windows-packages.sh`, `dotnet publish -r win-x64` outputs `NanoWeaverHrt.Desktop.exe` (matching `<AssemblyName>NanoWeaverHrt.Desktop</AssemblyName>`). The packaging script previously checked for `${STAGING_PORTABLE}/NanoWeaverHrt.exe`, failing to recognize the generated binary and falling back to a dummy PE32+ header wrapping a zip file instead of the genuine 40 MB single-file .NET 10 standalone executable.
  2. In `scripts/packaging/build-macos-packages.sh`, `create_fat_macho_binary` used a hardcoded arm64 offset of 8192 bytes. Real x86_64 Mach-O binaries produced by dotnet are ~100 KB, so writing arm64 data at byte 8192 overwrites the x86_64 Mach-O segment.
- **Goal & Scope**:
  1. Fix `build-windows-packages.sh` to check for `NanoWeaverHrt.Desktop.exe` and stage it as both `NanoWeaverHrt.exe` and `nanoweaver-hrt-desktop-portable-x64.exe`.
  2. Fix `build-macos-packages.sh` to calculate arm64 Mach-O offsets dynamically with page-boundary alignment (`((4096 + len(x64_data) + 4095) // 4096) * 4096`).
  3. Ensure `build-all-packages.sh` completes cleanly and verifies genuine executables.
- **Target Role / Model**: `mechanic` / `coder` (Recommended model: `flash`)
- **Transport Boundary**: Build / Packaging Scripts (`ApiWeb`)

---

## 2. Technical Contracts & Invariants
- **Files Modified**:
  - `scripts/packaging/build-windows-packages.sh`
  - `scripts/packaging/build-macos-packages.sh`
- **Invariants**:
  - `build-windows-packages.sh` must check:
    ```bash
    if [ -f "${STAGING_PORTABLE}/NanoWeaverHrt.Desktop.exe" ] && [ ! -f "${STAGING_PORTABLE}/NanoWeaverHrt.exe" ]; then
        cp -f "${STAGING_PORTABLE}/NanoWeaverHrt.Desktop.exe" "${STAGING_PORTABLE}/NanoWeaverHrt.exe"
    fi
    ```
  - In `build-macos-packages.sh`, dynamic Mach-O Fat slice offset calculation:
    ```python
    x64_len = len(x64_data)
    arm64_len = len(arm64_data)
    # Align each architecture slice to 4096-byte (2^12) page boundaries
    x64_offset = 4096
    arm64_offset = ((x64_offset + x64_len + 4095) // 4096) * 4096
    total_size = arm64_offset + arm64_len

    fat_hdr = struct.pack('>II', 0xcafebabe, 2)
    arch1 = struct.pack('>IIIII', 0x01000007, 3, x64_offset, x64_len, 12)
    arch2 = struct.pack('>IIIII', 0x0100000c, 0, arm64_offset, arm64_len, 12)
    ```
  - Zero changes to C# source code that would affect the coverage ratchet.

---

## 3. Step-by-Step Implementation Tasks
1. **Task 1 (`build-windows-packages.sh`)**:
   - Update staging copy logic to alias `NanoWeaverHrt.Desktop.exe` to `NanoWeaverHrt.exe` in `${STAGING_DIR}`.
   - Update portable single-file publish logic to alias `${STAGING_PORTABLE}/NanoWeaverHrt.Desktop.exe` to `${PORTABLE_TARGET}`.
2. **Task 2 (`build-macos-packages.sh`)**:
   - Update `create_fat_macho_binary` in `build-macos-packages.sh` to compute `arm64_offset` dynamically.
3. **Task 3 (Local Verification)**:
   - Run `./scripts/packaging/build-windows-packages.sh` and `./scripts/packaging/build-macos-packages.sh`.
   - Verify generated artifacts with `file artifacts/packages/windows/*` and `file artifacts/packages/macos/*`.

---

## 4. Verification Gate & Acceptance Criteria
- [ ] `./scripts/packaging/build-windows-packages.sh` produces genuine PE32+ executables.
- [ ] `./scripts/packaging/build-macos-packages.sh` produces uncorrupted Mach-O universal fat binaries.
- [ ] `./scripts/verify-all.sh` passes all 8 quality gates with 100.00% line & branch coverage.
