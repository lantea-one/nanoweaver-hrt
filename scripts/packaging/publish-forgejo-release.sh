#!/usr/bin/env bash
# ==============================================================================
# NanoWeaver HRT: Sovereign Forgejo Release Publishing Engine
# ------------------------------------------------------------------------------
# WHAT: Automatically publishes standalone executables, installers, and release
#       packages directly to Sovereign Forgejo Releases (git.lantea.one).
# WHY:  Delivers 1-click direct file downloads of .exe, .dmg, .deb, and mobile
#       assets to users and researchers without requiring archive extraction.
# HOW:  Interacts with Forgejo REST API v1 (/api/v1/repos/{owner}/{repo}/releases)
#       to mint permanent CalVer/SemVer releases for version tags and release branches
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
# WHY:  Version tags and release branches mint immutable releases; continuous nightly builds on main are disabled.
# HOW:  Inspect GITHUB_REF pattern for tags (refs/tags/v*) or release branches (refs/heads/release/*), exiting cleanly otherwise.
if [[ "${GITHUB_REF}" =~ ^refs/tags/v ]]; then
    TAG_NAME="${GITHUB_REF#refs/tags/}"
    RELEASE_TITLE="NanoWeaver HRT ${TAG_NAME}"
    IS_PRERELEASE="false"
elif [[ "${GITHUB_REF}" =~ refs/heads/release/v?([0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?) ]]; then
    RESOLVED_VERSION="$("${ROOT_DIR}/scripts/packaging/get-version.sh")"
    TAG_NAME="v${RESOLVED_VERSION}"
    RELEASE_TITLE="NanoWeaver HRT ${TAG_NAME}"
    IS_PRERELEASE="false"
else
    echo "Nightly rolling releases are disabled; skipping Forgejo release publication for ref '${GITHUB_REF}'."
    exit 0
fi

echo ">> Target Tag:       ${TAG_NAME}"
echo ">> Release Title:    ${RELEASE_TITLE}"
echo ">> Is Pre-release:   ${IS_PRERELEASE}"

# WHAT: Extract detailed milestone release notes from docs/release-notes.md and append direct download links.
# WHY:  Guarantees every Forgejo Release carries full phase-by-phase release notes plus clickable signed downloads.
# HOW:  Invokes scripts/packaging/extract-release-notes.py targeting TAG_NAME, REGISTRY, and GITHUB_REPOSITORY.
RELEASE_NOTES="$(python3 "${ROOT_DIR}/scripts/packaging/extract-release-notes.py" \
    --tag "${TAG_NAME}" \
    --registry "${REGISTRY}" \
    --repo "${GITHUB_REPOSITORY}" \
    --notes-file "${ROOT_DIR}/docs/release-notes.md")"

# WHAT: Query if a release matching TAG_NAME already exists on Forgejo.
# WHY:  Ensure existing versioned releases synchronize their release notes cleanly before uploading assets.
# HOW:  Execute GET /releases/tags/{tag} and inspect HTTP status code and response JSON.
echo ">> Checking for existing release with tag '${TAG_NAME}'..."
EXISTING_RESP="$(curl -s -w "\n%{http_code}" -H "${AUTH_HEADER}" "${API_BASE}/releases/tags/${TAG_NAME}" || true)"
HTTP_CODE="$(echo "${EXISTING_RESP}" | tail -n1)"
EXISTING_BODY="$(echo "${EXISTING_RESP}" | sed '$d')"

RELEASE_ID=""
if [[ "${HTTP_CODE}" == "200" ]]; then
    RELEASE_ID="$(python3 -c "import json, sys; data = json.loads(sys.argv[1]); print(data.get('id', ''))" "${EXISTING_BODY}" 2>/dev/null || true)"
    echo "Found existing release (ID: ${RELEASE_ID}) for tag '${TAG_NAME}'."

    if [[ -n "${RELEASE_ID}" ]]; then
        echo "Synchronizing detailed release notes onto existing release (ID: ${RELEASE_ID})..."
        PATCH_PAYLOAD="$(python3 -c "import json, sys; print(json.dumps({'name': sys.argv[1], 'body': sys.argv[2]}))" "${RELEASE_TITLE}" "${RELEASE_NOTES}")"
        curl -s -X PATCH \
            -H "${AUTH_HEADER}" \
            -H "Content-Type: application/json" \
            -d "${PATCH_PAYLOAD}" \
            "${API_BASE}/releases/${RELEASE_ID}" >/dev/null 2>&1 || true
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
    # Concurrent race guard: check if another runner (e.g., tag push) just created it
    EXISTING_RESP="$(curl -s -H "${AUTH_HEADER}" "${API_BASE}/releases/tags/${TAG_NAME}" || true)"
    RELEASE_ID="$(python3 -c "import json, sys; data = json.loads(sys.argv[1]); print(data.get('id', ''))" "${EXISTING_RESP}" 2>/dev/null || true)"
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
    [[ -f "${f}" && "$(basename "${f}")" != SHA256SUMS* ]] && ASSET_FILES+=("${f}")
done

# Linux Packages
for f in "${PACKAGES_DIR}/linux"/*.deb "${PACKAGES_DIR}/linux"/*.rpm "${PACKAGES_DIR}/linux"/*.pkg.tar.zst "${PACKAGES_DIR}/linux"/*.p7s "${PACKAGES_DIR}/linux"/*.asc "${PACKAGES_DIR}/linux"/*.sig; do
    [[ -f "${f}" && "$(basename "${f}")" != SHA256SUMS* ]] && ASSET_FILES+=("${f}")
done

# macOS Packages
for f in "${PACKAGES_DIR}/macos"/*.dmg "${PACKAGES_DIR}/macos"/*.tar.gz "${PACKAGES_DIR}/macos"/*.p7s "${PACKAGES_DIR}/macos"/*.asc "${PACKAGES_DIR}/macos"/*.sig; do
    [[ -f "${f}" && "$(basename "${f}")" != SHA256SUMS* ]] && ASSET_FILES+=("${f}")
done

# Mobile Packages
for f in "${PACKAGES_DIR}/mobile"/*.apk "${PACKAGES_DIR}/mobile"/*.ipa "${PACKAGES_DIR}/mobile"/*.p7s "${PACKAGES_DIR}/mobile"/*.asc "${PACKAGES_DIR}/mobile"/*.sig; do
    [[ -f "${f}" && "$(basename "${f}")" != SHA256SUMS* ]] && ASSET_FILES+=("${f}")
done

# Master Manifest & Trust Anchors
for f in "${PACKAGES_DIR}"/SHA256SUMS* "${ROOT_DIR}/docs/assets/keys"/nanoweaver-codesign-root-ca.crt "${ROOT_DIR}/docs/assets/keys"/nanoweaver-archive-keyring.asc; do
    [[ -f "${f}" ]] && ASSET_FILES+=("${f}")
done

# WHAT: Fetch existing release assets to prevent duplicate asset uploads on re-runs.
# WHY:  Re-running or concurrently pushing a release branch and tag otherwise attaches duplicate assets.
# HOW:  Query GET /releases/{id}/assets and remove any existing asset matching the incoming filename before upload.
EXISTING_ASSETS_JSON="$(curl -s -H "${AUTH_HEADER}" "${API_BASE}/releases/${RELEASE_ID}/assets" || echo "[]")"

for asset in "${ASSET_FILES[@]}"; do
    filename="$(basename "${asset}")"
    EXISTING_ASSET_IDS="$(python3 -c "
import json, sys
try:
    assets = json.loads(sys.argv[1])
    for a in assets:
        if isinstance(a, dict) and a.get('name') == sys.argv[2]:
            print(a.get('id', ''))
except Exception:
    pass
" "${EXISTING_ASSETS_JSON}" "${filename}")"
    for old_id in ${EXISTING_ASSET_IDS}; do
        [[ -n "${old_id}" ]] && curl -s -X DELETE -H "${AUTH_HEADER}" "${API_BASE}/releases/${RELEASE_ID}/assets/${old_id}" >/dev/null 2>&1 || true
    done
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
