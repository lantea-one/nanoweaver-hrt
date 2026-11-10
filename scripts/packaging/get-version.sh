#!/usr/bin/env bash
# ==============================================================================
# NanoWeaver HRT: Dynamic CalVer Version Extraction Engine
# ------------------------------------------------------------------------------
# WHAT: Dynamically resolves the authoritative release version across branches,
#       tags, CI runners, and local developer workspaces.
# WHY:  Engineers cutting release branches (e.g., release/v2026.11.10.1) or tagging
#       releases should never need to manually update individual files across
#       packaging scripts, manifests, and project properties.
# HOW:  Checks hierarchy:
#       1. Explicit PACKAGE_VERSION environment variable
#       2. CI ref names (GITHUB_REF_NAME, FORGEJO_REF_NAME, CI_COMMIT_REF_NAME)
#       3. Current git release branch name (release/v* or release/*)
#       4. Exact git tag (v*)
#       5. Committed DefaultCalVerVersion in Directory.Build.props
#       6. Nearest git tag (v*)
#       7. Absolute fallback baseline
# ==============================================================================

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

RAW_REF="${PACKAGE_VERSION:-${GITHUB_REF_NAME:-${FORGEJO_REF_NAME:-${CI_COMMIT_REF_NAME:-}}}}"

# 1. Inspect CI or environment ref
if [[ -n "${RAW_REF}" ]]; then
    # Match release/v2026.11.10.1, release/2026.11.10.1, v2026.11.10.1, 2026.11.10.1
    if [[ "${RAW_REF}" =~ (release/)?v?([0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?) ]]; then
        echo "${BASH_REMATCH[2]}"
        exit 0
    fi
fi

# 2. Inspect active git branch
if command -v git &>/dev/null && git -C "${ROOT_DIR}" rev-parse --is-inside-work-tree &>/dev/null; then
    CURRENT_BRANCH="$(git -C "${ROOT_DIR}" branch --show-current 2>/dev/null || git -C "${ROOT_DIR}" rev-parse --abbrev-ref HEAD 2>/dev/null || true)"
    if [[ "${CURRENT_BRANCH}" =~ ^release/v?([0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?) ]]; then
        echo "${BASH_REMATCH[1]}"
        exit 0
    fi

    # 3. Inspect exact git tag on current commit
    EXACT_TAG="$(git -C "${ROOT_DIR}" describe --tags --exact-match 2>/dev/null || true)"
    if [[ "${EXACT_TAG}" =~ ^v?([0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?) ]]; then
        echo "${BASH_REMATCH[1]}"
        exit 0
    fi
fi

# 4. Extract DefaultCalVerVersion from Directory.Build.props
PROPS_FILE="${ROOT_DIR}/Directory.Build.props"
if [[ -f "${PROPS_FILE}" ]]; then
    PARSED_VERSION="$(grep -oP '<DefaultCalVerVersion>\K[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?(?=</DefaultCalVerVersion>)' "${PROPS_FILE}" || true)"
    if [[ -n "${PARSED_VERSION}" ]]; then
        echo "${PARSED_VERSION}"
        exit 0
    fi
fi

# 5. Inspect nearest git tag
if command -v git &>/dev/null && git -C "${ROOT_DIR}" rev-parse --is-inside-work-tree &>/dev/null; then
    NEAREST_TAG="$(git -C "${ROOT_DIR}" describe --tags --abbrev=0 2>/dev/null || true)"
    if [[ "${NEAREST_TAG}" =~ ^v?([0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?) ]]; then
        echo "${BASH_REMATCH[1]}"
        exit 0
    fi
fi

# Absolute baseline fallback
echo "2026.11.10.2"
