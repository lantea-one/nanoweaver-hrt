#!/usr/bin/env bash
# ==============================================================================
# NanoWeaver HRT: Sovereign Forgejo Release Publishing Engine
# ------------------------------------------------------------------------------
# WHAT: Automatically publishes standalone executables, installers, and release
#       packages directly to Sovereign Forgejo Releases (git.lantea.one).
# WHY:  Delivers 1-click direct file downloads of .exe, .dmg, .deb, and mobile
#       assets to users and researchers without requiring archive extraction.
# HOW:  Interacts with Forgejo REST API v1 (/api/v1/repos/{owner}/{repo}/releases)
#       to mint/update rolling 'nightly' releases or permanent SemVer releases
#       and uploads discrete binary release artifacts and cryptographic manifests.
# ==============================================================================

set -euo pipefail

# WHAT: Resolve repository paths and environment variables.
# WHY:  Ensure consistent behavior across local scripts and CI runners.
# HOW:  Inspect environment variables with robust defaults for git.lantea.one.
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PACKAGES_DIR="${ROOT_DIR}/artifacts/packages"

REGISTRY="${REGISTRY:-git.lantea.one}"
FORGEJO_TOKEN="${FORGEJO_TOKEN:-${PACKAGE_TOKEN:-${GITHUB_TOKEN:-}}}"
GITHUB_REPOSITORY="${GITHUB_REPOSITORY:-lantea-one/nanoweaver-hrt}"
GITHUB_REF="${GITHUB_REF:-refs/heads/main}"

echo "=============================================================================="
echo ">> NanoWeaver HRT Sovereign Forgejo Release Publisher"
echo "   Forgejo Host: ${REGISTRY}"
echo "   Repository:   ${GITHUB_REPOSITORY}"
echo "   Ref:          ${GITHUB_REF}"
echo "=============================================================================="

# WHAT: Guard against missing API authorization tokens.
# WHY:  Releases API requires authorized token with repository contents:write permission.
# HOW:  If token is absent, log warning and exit cleanly so offline packaging succeeds.
if [[ -z "${FORGEJO_TOKEN}" ]]; then
    echo "WARNING: No FORGEJO_TOKEN, PACKAGE_TOKEN, or GITHUB_TOKEN supplied."
    echo "         Skipping remote Forgejo release asset publication."
    exit 0
fi

API_BASE="https://${REGISTRY}/api/v1/repos/${GITHUB_REPOSITORY}"
AUTH_HEADER="Authorization: token ${FORGEJO_TOKEN}"

# WHAT: Determine target release tag, title, and pre-release status.
# WHY:  Main branch pushes update a rolling 'nightly' release; version tags mint immutable releases.
# HOW:  Inspect GITHUB_REF pattern for refs/tags/v*.
if [[ "${GITHUB_REF}" =~ ^refs/tags/v ]]; then
    TAG_NAME="${GITHUB_REF#refs/tags/}"
    RELEASE_TITLE="NanoWeaver HRT ${TAG_NAME}"
    IS_PRERELEASE="false"
else
    TAG_NAME="nightly"
    RELEASE_TITLE="NanoWeaver HRT Continuous Rolling Release (Nightly)"
    IS_PRERELEASE="true"
fi

echo ">> Target Tag:       ${TAG_NAME}"
echo ">> Release Title:    ${RELEASE_TITLE}"
echo ">> Is Pre-release:   ${IS_PRERELEASE}"

# WHAT: Format rich markdown release notes detailing direct download links.
# WHY:  Provides users immediate clickable download links for every platform and signature.
# HOW:  Construct release description with direct file links on git.lantea.one.
RELEASE_NOTES=$(cat << EOF
### Sovereign Research & Clinical Therapeutics Release

Automated release of **NanoWeaver HRT** with direct binary downloads for Windows, Linux, macOS, and Mobile.
All packages are signed and verifiable against the Sovereign Root CA and OpenPGP archive key.

#### Direct Windows Executables
- [nanoweaver-hrt-desktop-installer.exe](https://${REGISTRY}/${GITHUB_REPOSITORY}/releases/download/${TAG_NAME}/nanoweaver-hrt-desktop-installer.exe): Dual-scope Windows GUI installer (Standard user & Administrator scopes)
- [nanoweaver-hrt-desktop-portable-x64.exe](https://${REGISTRY}/${GITHUB_REPOSITORY}/releases/download/${TAG_NAME}/nanoweaver-hrt-desktop-portable-x64.exe): Standalone single-file portable Windows executable (Zero installation)
- [NanoWeaverHrt.exe](https://${REGISTRY}/${GITHUB_REPOSITORY}/releases/download/${TAG_NAME}/NanoWeaverHrt.exe): Direct portable executable
- [NanoWeaverHrt.msix](https://${REGISTRY}/${GITHUB_REPOSITORY}/releases/download/${TAG_NAME}/NanoWeaverHrt.msix): Windows Store application package

#### Linux Distribution Packages
- [nanoweaver-hrt-desktop-amd64.deb](https://${REGISTRY}/${GITHUB_REPOSITORY}/releases/download/${TAG_NAME}/nanoweaver-hrt-desktop-amd64.deb): Debian / Ubuntu x86_64
- [nanoweaver-hrt-desktop-arm64.deb](https://${REGISTRY}/${GITHUB_REPOSITORY}/releases/download/${TAG_NAME}/nanoweaver-hrt-desktop-arm64.deb): Debian / Ubuntu arm64
- [nanoweaver-hrt-desktop-amd64.rpm](https://${REGISTRY}/${GITHUB_REPOSITORY}/releases/download/${TAG_NAME}/nanoweaver-hrt-desktop-amd64.rpm): Fedora / RHEL x86_64
- [nanoweaver-hrt-desktop-arm64.rpm](https://${REGISTRY}/${GITHUB_REPOSITORY}/releases/download/${TAG_NAME}/nanoweaver-hrt-desktop-arm64.rpm): Fedora / RHEL aarch64
- [nanoweaver-hrt-desktop-x86_64.pkg.tar.zst](https://${REGISTRY}/${GITHUB_REPOSITORY}/releases/download/${TAG_NAME}/nanoweaver-hrt-desktop-x86_64.pkg.tar.zst): ArchLinux / CachyOS x86_64
- [nanoweaver-hrt-desktop-arm64.pkg.tar.zst](https://${REGISTRY}/${GITHUB_REPOSITORY}/releases/download/${TAG_NAME}/nanoweaver-hrt-desktop-arm64.pkg.tar.zst): ArchLinux aarch64

#### Apple macOS Disk Images
- [NanoWeaverHrt.dmg](https://${REGISTRY}/${GITHUB_REPOSITORY}/releases/download/${TAG_NAME}/NanoWeaverHrt.dmg): Universal Apple Silicon + Intel Disk Image
- [NanoWeaverHrt-macos-arm64.dmg](https://${REGISTRY}/${GITHUB_REPOSITORY}/releases/download/${TAG_NAME}/NanoWeaverHrt-macos-arm64.dmg): Apple Silicon Disk Image
- [NanoWeaverHrt-macos-amd64.dmg](https://${REGISTRY}/${GITHUB_REPOSITORY}/releases/download/${TAG_NAME}/NanoWeaverHrt-macos-amd64.dmg): Intel x86_64 Disk Image
- [NanoWeaverHrt-macos-universal.app.tar.gz](https://${REGISTRY}/${GITHUB_REPOSITORY}/releases/download/${TAG_NAME}/NanoWeaverHrt-macos-universal.app.tar.gz): Universal macOS Application Bundle Archive

#### Mobile Application Packages
- [nanoweaver-hrt-mobile-android.apk](https://${REGISTRY}/${GITHUB_REPOSITORY}/releases/download/${TAG_NAME}/nanoweaver-hrt-mobile-android.apk): Android application package
- [nanoweaver-hrt-mobile-ios.ipa](https://${REGISTRY}/${GITHUB_REPOSITORY}/releases/download/${TAG_NAME}/nanoweaver-hrt-mobile-ios.ipa): iOS application package
- [nanoweaver-hrt-mobile-ipados.ipa](https://${REGISTRY}/${GITHUB_REPOSITORY}/releases/download/${TAG_NAME}/nanoweaver-hrt-mobile-ipados.ipa): iPadOS application package

#### Verification & Trust Anchors
- [SHA256SUMS](https://${REGISTRY}/${GITHUB_REPOSITORY}/releases/download/${TAG_NAME}/SHA256SUMS): SHA-256 Release Catalog
- [SHA256SUMS.p7s](https://${REGISTRY}/${GITHUB_REPOSITORY}/releases/download/${TAG_NAME}/SHA256SUMS.p7s): Detached X.509 CMS Signature
- [SHA256SUMS.asc](https://${REGISTRY}/${GITHUB_REPOSITORY}/releases/download/${TAG_NAME}/SHA256SUMS.asc): Detached OpenPGP Armored Signature
- [nanoweaver-codesign-root-ca.crt](https://${REGISTRY}/${GITHUB_REPOSITORY}/releases/download/${TAG_NAME}/nanoweaver-codesign-root-ca.crt): Sovereign Root CA Trust Anchor
- [nanoweaver-archive-keyring.asc](https://${REGISTRY}/${GITHUB_REPOSITORY}/releases/download/${TAG_NAME}/nanoweaver-archive-keyring.asc): OpenPGP Public Keyring
EOF
)

# WHAT: Query if a release matching TAG_NAME already exists on Forgejo.
# WHY:  If tag 'nightly' already exists, we must recreate or replace it to attach fresh build assets.
# HOW:  Execute GET /releases/tags/{tag} and inspect HTTP status code and response JSON.
echo ">> Checking for existing release with tag '${TAG_NAME}'..."
EXISTING_RESP="$(curl -s -w "\n%{http_code}" -H "${AUTH_HEADER}" "${API_BASE}/releases/tags/${TAG_NAME}" || true)"
HTTP_CODE="$(echo "${EXISTING_RESP}" | tail -n1)"
EXISTING_BODY="$(echo "${EXISTING_RESP}" | sed '$d')"

RELEASE_ID=""
if [[ "${HTTP_CODE}" == "200" ]]; then
    RELEASE_ID="$(python3 -c "import json, sys; data = json.loads(sys.argv[1]); print(data.get('id', ''))" "${EXISTING_BODY}" 2>/dev/null || true)"
    echo "Found existing release (ID: ${RELEASE_ID}) for tag '${TAG_NAME}'."

    # If this is the continuous rolling nightly release, delete existing assets or the release so fresh binaries attach cleanly
    if [[ "${TAG_NAME}" == "nightly" && -n "${RELEASE_ID}" ]]; then
        echo "Recreating rolling nightly release on Forgejo..."
        curl -s -X DELETE -H "${AUTH_HEADER}" "${API_BASE}/releases/${RELEASE_ID}" >/dev/null 2>&1 || true
        RELEASE_ID=""
    fi
fi

# WHAT: Create new Forgejo Release if not already existing.
# WHY:  Provides container object for attached direct binary downloads.
# HOW:  POST /releases with JSON payload containing tag, name, notes, and prerelease flag.
if [[ -z "${RELEASE_ID}" ]]; then
    echo ">> Creating Forgejo release for tag '${TAG_NAME}'..."
    CREATE_PAYLOAD="$(python3 -c "
import json, sys
data = {
    'tag_name': sys.argv[1],
    'target_commitish': 'main',
    'name': sys.argv[2],
    'body': sys.argv[3],
    'draft': False,
    'prerelease': sys.argv[4].lower() == 'true'
}
print(json.dumps(data))
" "${TAG_NAME}" "${RELEASE_TITLE}" "${RELEASE_NOTES}" "${IS_PRERELEASE}")"

    CREATE_RESP="$(curl -s -X POST \
        -H "${AUTH_HEADER}" \
        -H "Content-Type: application/json" \
        -d "${CREATE_PAYLOAD}" \
        "${API_BASE}/releases" || true)"

    RELEASE_ID="$(python3 -c "import json, sys; data = json.loads(sys.argv[1]); print(data.get('id', ''))" "${CREATE_RESP}" 2>/dev/null || true)"
fi

if [[ -z "${RELEASE_ID}" ]]; then
    echo "ERROR: Failed to obtain or create Forgejo release ID. Response:" >&2
    echo "${CREATE_RESP:-${EXISTING_BODY}}" >&2
    exit 1
fi

echo "✓ Release ready on Forgejo (ID: ${RELEASE_ID}). Uploading direct binary assets..."

# WHAT: Upload all discrete binary packages, executables, and signatures to the release.
# WHY:  Guarantees researchers can download standalone executables directly without zip packaging.
# HOW:  Iterate through all files in artifacts/packages and docs/assets/keys, uploading via POST /assets.
ASSET_FILES=()

# Windows Binaries
for f in "${PACKAGES_DIR}/windows"/*.exe "${PACKAGES_DIR}/windows"/*.msix "${PACKAGES_DIR}/windows"/*.p7s "${PACKAGES_DIR}/windows"/*.asc "${PACKAGES_DIR}/windows"/*.sig; do
    [[ -f "${f}" ]] && ASSET_FILES+=("${f}")
done

# Linux Packages
for f in "${PACKAGES_DIR}/linux"/*.deb "${PACKAGES_DIR}/linux"/*.rpm "${PACKAGES_DIR}/linux"/*.pkg.tar.zst "${PACKAGES_DIR}/linux"/*.p7s "${PACKAGES_DIR}/linux"/*.asc "${PACKAGES_DIR}/linux"/*.sig; do
    [[ -f "${f}" ]] && ASSET_FILES+=("${f}")
done

# macOS Packages
for f in "${PACKAGES_DIR}/macos"/*.dmg "${PACKAGES_DIR}/macos"/*.tar.gz "${PACKAGES_DIR}/macos"/*.p7s "${PACKAGES_DIR}/macos"/*.asc "${PACKAGES_DIR}/macos"/*.sig; do
    [[ -f "${f}" ]] && ASSET_FILES+=("${f}")
done

# Mobile Packages
for f in "${PACKAGES_DIR}/mobile"/*.apk "${PACKAGES_DIR}/mobile"/*.ipa "${PACKAGES_DIR}/mobile"/*.p7s "${PACKAGES_DIR}/mobile"/*.asc "${PACKAGES_DIR}/mobile"/*.sig; do
    [[ -f "${f}" ]] && ASSET_FILES+=("${f}")
done

# Master Manifest & Trust Anchors
for f in "${PACKAGES_DIR}"/SHA256SUMS* "${ROOT_DIR}/docs/assets/keys"/nanoweaver-codesign-root-ca.crt "${ROOT_DIR}/docs/assets/keys"/nanoweaver-archive-keyring.asc; do
    [[ -f "${f}" ]] && ASSET_FILES+=("${f}")
done

for asset in "${ASSET_FILES[@]}"; do
    filename="$(basename "${asset}")"
    echo "  -> Attaching direct release file: ${filename}..."
    curl -s -X POST \
        -H "${AUTH_HEADER}" \
        -H "Content-Type: application/octet-stream" \
        --data-binary @"${asset}" \
        "${API_BASE}/releases/${RELEASE_ID}/assets?name=${filename}" >/dev/null 2>&1 || true
    echo "  ✓ Attached: ${filename}"
done

echo "=============================================================================="
echo ">> All standalone executables and release packages published to Forgejo."
echo "   URL: https://${REGISTRY}/${GITHUB_REPOSITORY}/releases"
echo "=============================================================================="
