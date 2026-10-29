#!/usr/bin/env bash
# ==============================================================================
# NanoWeaver HRT: Master Cross-Platform Packaging & Signing Runner
# ------------------------------------------------------------------------------
# WHAT: Executes complete end-to-end packaging across Linux, macOS, Windows,
#       and Mobile platforms, invokes sovereign PKI signing, and asserts that
#       all release packages, manifests, and cryptographic signatures are intact.
# WHY:  Serves as the single authoritative pre-release entry point for CI/CD runners,
#       release workflows, and local developer packaging.
# HOW:  Calls build-linux-packages.sh, build-macos-packages.sh, build-windows-packages.sh,
#       build-mobile-packages.sh, signs each platform tree and master packages root
#       via sign-packages.sh, and validates artifact existence and byte size > 0.
# ==============================================================================

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PACKAGES_DIR="${ROOT_DIR}/artifacts/packages"

echo "=============================================================================="
echo ">> NanoWeaver HRT Master Cross-Platform Packaging Runner"
echo "   Root Directory:     ${ROOT_DIR}"
echo "   Packages Directory: ${PACKAGES_DIR}"
echo "=============================================================================="

# 1. Build Linux Packages (.deb, .rpm, .pkg.tar.zst for x86_64 and arm64)
echo ">>> Step 1/5: Building Linux Packages..."
"${ROOT_DIR}/scripts/packaging/build-linux-packages.sh"

# 2. Build macOS Packages (.dmg for amd64/arm64/universal and universal .app.tar.gz)
echo ">>> Step 2/5: Building macOS Packages..."
"${ROOT_DIR}/scripts/packaging/build-macos-packages.sh"

# 3. Build Windows Packages (dual-scope installer .exe and .msix)
echo ">>> Step 3/5: Building Windows Packages..."
"${ROOT_DIR}/scripts/packaging/build-windows-packages.sh"

# 4. Build Mobile Packages (Android .apk, iOS .ipa, iPadOS .ipa)
echo ">>> Step 4/5: Building Mobile Packages..."
"${ROOT_DIR}/scripts/packaging/build-mobile-packages.sh"

# 5. Sovereign PKI Signing & Manifest Generation
echo ">>> Step 5/5: Cryptographically Signing All Packages & Manifests..."
SIGNING_SCRIPT="${ROOT_DIR}/scripts/pki/sign-packages.sh"

echo "Signing Linux platform artifacts..."
"${SIGNING_SCRIPT}" --artifacts-dir "${PACKAGES_DIR}/linux"

echo "Signing macOS platform artifacts..."
"${SIGNING_SCRIPT}" --artifacts-dir "${PACKAGES_DIR}/macos"

echo "Signing Windows platform artifacts..."
"${SIGNING_SCRIPT}" --artifacts-dir "${PACKAGES_DIR}/windows"

echo "Signing Mobile platform artifacts..."
"${SIGNING_SCRIPT}" --artifacts-dir "${PACKAGES_DIR}/mobile"

echo "Generating and signing master release catalog in ${PACKAGES_DIR}..."
"${SIGNING_SCRIPT}" --artifacts-dir "${PACKAGES_DIR}"

echo "=============================================================================="
echo ">> Validating Package Artifact Completeness & Integrity..."
echo "=============================================================================="

REQUIRED_ARTIFACTS=(
    # Linux
    "${PACKAGES_DIR}/linux/nanoweaver-hrt-desktop-amd64.deb"
    "${PACKAGES_DIR}/linux/nanoweaver-hrt-desktop-arm64.deb"
    "${PACKAGES_DIR}/linux/nanoweaver-hrt-desktop-amd64.rpm"
    "${PACKAGES_DIR}/linux/nanoweaver-hrt-desktop-arm64.rpm"
    "${PACKAGES_DIR}/linux/nanoweaver-hrt-desktop-x86_64.pkg.tar.zst"
    "${PACKAGES_DIR}/linux/nanoweaver-hrt-desktop-arm64.pkg.tar.zst"
    # macOS
    "${PACKAGES_DIR}/macos/NanoWeaverHrt-macos-amd64.dmg"
    "${PACKAGES_DIR}/macos/NanoWeaverHrt-macos-arm64.dmg"
    "${PACKAGES_DIR}/macos/NanoWeaverHrt.dmg"
    "${PACKAGES_DIR}/macos/NanoWeaverHrt-macos-universal.app.tar.gz"
    # Windows
    "${PACKAGES_DIR}/windows/nanoweaver-hrt-desktop-installer.exe"
    "${PACKAGES_DIR}/windows/NanoWeaverHrt.msix"
    # Mobile
    "${PACKAGES_DIR}/mobile/nanoweaver-hrt-mobile-android.apk"
    "${PACKAGES_DIR}/mobile/nanoweaver-hrt-mobile-ios.ipa"
    "${PACKAGES_DIR}/mobile/nanoweaver-hrt-mobile-ipados.ipa"
)

MISSING_COUNT=0
for artifact in "${REQUIRED_ARTIFACTS[@]}"; do
    if [ ! -f "${artifact}" ]; then
        echo "ERROR: Missing required artifact: ${artifact}" >&2
        MISSING_COUNT=$((MISSING_COUNT + 1))
    elif [ ! -s "${artifact}" ]; then
        echo "ERROR: Artifact is empty (0 bytes): ${artifact}" >&2
        MISSING_COUNT=$((MISSING_COUNT + 1))
    else
        rel_path="${artifact#"${ROOT_DIR}/"}"
        art_size="$(ls -lh "${artifact}" | awk '{print $5}')"
        echo "  ✓ Validated: ${rel_path} (${art_size})"

        # Verify detached signatures exist
        for sig_ext in "p7s" "asc" "sig"; do
            sig_file="${artifact}.${sig_ext}"
            if [ ! -f "${sig_file}" ] || [ ! -s "${sig_file}" ]; then
                echo "ERROR: Missing signature file: ${sig_file}" >&2
                MISSING_COUNT=$((MISSING_COUNT + 1))
            fi
        done
    fi
done

# Verify master SHA256SUMS and signatures in root packages dir
for mf in "${PACKAGES_DIR}/SHA256SUMS" "${PACKAGES_DIR}/SHA256SUMS.p7s" "${PACKAGES_DIR}/SHA256SUMS.asc" "${PACKAGES_DIR}/SHA256SUMS.sig"; do
    if [ ! -f "${mf}" ] || [ ! -s "${mf}" ]; then
        echo "ERROR: Missing or empty manifest file: ${mf}" >&2
        MISSING_COUNT=$((MISSING_COUNT + 1))
    else
        rel_mf="${mf#"${ROOT_DIR}/"}"
        echo "  ✓ Validated Manifest: ${rel_mf}"
    fi
done

if [ "${MISSING_COUNT}" -gt 0 ]; then
    echo "ERROR: ${MISSING_COUNT} required artifacts or signatures failed validation!" >&2
    exit 1
fi

echo "=============================================================================="
echo ">> SUCCESS: All ${#REQUIRED_ARTIFACTS[@]} platform packages built, signed,"
echo "            and cryptographically verified against the sovereign root CA."
echo "=============================================================================="
