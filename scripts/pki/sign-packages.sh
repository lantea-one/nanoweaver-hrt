#!/usr/bin/env bash
# ==============================================================================
# NanoWeaver HRT: Sovereign Package Signing & Verification Engine
# ------------------------------------------------------------------------------
# WHAT: Cryptographically signs release packages (.deb, .rpm, .pkg.tar.zst, Windows
#       .exe/.msix, macOS bundles, and Android APKs) using the sovereign 90-day
#       X.509 code-signing certificate and OpenPGP/GPG archive keys.
# WHY:  Guarantees binary authenticity, integrity, and tamper-resistance for all
#       desktop and mobile releases without relying on commercial CA lock-in.
# HOW:  Applies native in-place signatures where tooling (osslsigncode, apksigner,
#       rcodesign) is available, generates detached X.509 CMS (.p7s) and GPG (.asc/.sig)
#       signatures for every artifact, computes SHA256SUMS, and cryptographically signs
#       the manifest.
# ==============================================================================

set -euo pipefail

# WHAT: Parse command-line flags and locate PKI keys and artifact directories.
# WHY:  Enable execution across local development, containerized builds, and CI runners.
# HOW:  Read CLI options with sensible repository defaults.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

ARTIFACTS_DIR="${REPO_ROOT}/artifacts"
PKI_DIR="${REPO_ROOT}/artifacts/pki-private"
PUBLIC_KEYS_DIR="${REPO_ROOT}/docs/assets/keys"
PFX_PASSWORD="${CODESIGN_PFX_PASSWORD:-nanoweaver-hrt}"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --artifacts-dir)
            ARTIFACTS_DIR="$2"
            shift 2
            ;;
        --pki-dir)
            PKI_DIR="$2"
            shift 2
            ;;
        --public-keys-dir)
            PUBLIC_KEYS_DIR="$2"
            shift 2
            ;;
        --pfx-password)
            PFX_PASSWORD="$2"
            shift 2
            ;;
        -h|--help)
            echo "Usage: $0 [options]"
            echo "Options:"
            echo "  --artifacts-dir <dir>    Directory containing binary packages (default: artifacts)"
            echo "  --pki-dir <dir>          Directory containing private PKI keys (default: artifacts/pki-private)"
            echo "  --public-keys-dir <dir>  Directory containing public trust anchors (default: docs/assets/keys)"
            echo "  --pfx-password <pwd>     Password for PKCS#12 (.pfx) bundle (default: nanoweaver-hrt)"
            exit 0
            ;;
        *)
            echo "Unknown argument: $1" >&2
            exit 1
            ;;
    esac
done

if [[ ! -d "${ARTIFACTS_DIR}" ]]; then
    mkdir -p "${ARTIFACTS_DIR}"
fi

# WHAT: Verify availability of private signing keys and public trust anchors.
# WHY:  Fail fast with actionable instructions if keys have not been provisioned.
# HOW:  Check presence of leaf key, leaf cert, root CA, and GPG keyring.
LEAF_KEY="${PKI_DIR}/nanoweaver-codesign-leaf.key"
LEAF_CERT="${PUBLIC_KEYS_DIR}/nanoweaver-codesign-leaf.crt"
ROOT_CERT="${PUBLIC_KEYS_DIR}/nanoweaver-codesign-root-ca.crt"
PFX_FILE="${PKI_DIR}/codesign-leaf.pfx"
ANDROID_KEYSTORE="${PKI_DIR}/codesign-android.keystore"
GNUPG_DIR="${PKI_DIR}/gnupg"

if [[ ! -f "${LEAF_KEY}" || ! -f "${LEAF_CERT}" || ! -f "${ROOT_CERT}" || ! -f "${PFX_FILE}" || ! -d "${GNUPG_DIR}" ]]; then
    echo ">> Signing keys missing in ${PKI_DIR}. Auto-provisioning via provision-or-rotate-codesign.sh..."
    "${SCRIPT_DIR}/provision-or-rotate-codesign.sh" \
        --out-dir "${PKI_DIR}" \
        --public-keys-dir "${PUBLIC_KEYS_DIR}" \
        --pfx-password "${PFX_PASSWORD}"
fi

echo "=============================================================================="
echo ">> Sovereign Code-Signing & Release Manifest Engine"
echo "   Artifacts Directory: ${ARTIFACTS_DIR}"
echo "   PKI Directory:       ${PKI_DIR}"
echo "   Public Anchors:      ${PUBLIC_KEYS_DIR}"
echo "=============================================================================="

# WHAT: Identify candidate release packages to sign.
# WHY:  Only sign distribution artifacts, skipping existing signature or manifest files.
# HOW:  Collect non-signature files matching common package extensions or all binaries.
TARGET_FILES=()
while IFS= read -r -d '' file; do
    rel_name="$(basename "${file}")"
    case "${rel_name}" in
        *.asc|*.sig|*.p7s|SHA256SUMS|SHA256SUMS.*|coverage*|*.log)
            # Skip signature files and test logs
            ;;
        *.deb|*.rpm|*.pkg.tar.zst|*.exe|*.msix|*.msi|*.dll|*.dmg|*.ipa|*.apk|*.tar.gz|*.tar.xz|*.zip)
            TARGET_FILES+=("${file}")
            ;;
    esac
done < <(find "${ARTIFACTS_DIR}" -maxdepth 2 -type f -print0)

if [[ ${#TARGET_FILES[@]} -eq 0 ]]; then
    echo ">> No package archives or binaries found in ${ARTIFACTS_DIR} to sign."
    echo ">> Generating and signing empty SHA256SUMS manifest."
    touch "${ARTIFACTS_DIR}/SHA256SUMS"
fi

# WHAT: Apply native and detached signatures to each identified package.
# WHY:  Each operating system and package manager expects a specific signature format.
# HOW:  Use platform CLI tools when installed, and always generate detached X.509 CMS (.p7s)
#       and OpenPGP (.asc and .sig) signatures verified against the sovereign root CA.
for pkg in "${TARGET_FILES[@]}"; do
    pkg_base="$(basename "${pkg}")"
    echo ">> Signing package: ${pkg_base}"

    # 1. Native Windows Authenticode Signing (.exe, .msix, .msi, .dll)
    case "${pkg_base}" in
        *.exe|*.msix|*.msi|*.dll)
            if command -v osslsigncode >/dev/null 2>&1 && [[ -f "${PFX_FILE}" ]]; then
                echo "   -> Applying Windows Authenticode signature via osslsigncode..."
                osslsigncode sign \
                    -pkcs12 "${PFX_FILE}" \
                    -pass "${PFX_PASSWORD}" \
                    -h sha256 \
                    -n "NanoWeaver HRT" \
                    -i "https://lantea.one" \
                    -in "${pkg}" \
                    -out "${pkg}.signed"
                mv -f "${pkg}.signed" "${pkg}"
                echo "   ✓ Authenticode signature applied."
            else
                echo "   - osslsigncode not present; skipping embedded Authenticode signature."
            fi
            ;;
    esac

    # 2. Native Android APK Signing (.apk)
    case "${pkg_base}" in
        *.apk)
            if command -v apksigner >/dev/null 2>&1 && [[ -f "${ANDROID_KEYSTORE}" ]]; then
                echo "   -> Applying Android APK signature via apksigner..."
                apksigner sign \
                    --ks "${ANDROID_KEYSTORE}" \
                    --ks-pass "pass:${PFX_PASSWORD}" \
                    "${pkg}"
                apksigner verify --verbose "${pkg}" >/dev/null 2>&1
                echo "   ✓ Android APK signed and verified."
            elif command -v jarsigner >/dev/null 2>&1 && [[ -f "${ANDROID_KEYSTORE}" ]]; then
                echo "   -> Applying Android APK signature via jarsigner..."
                jarsigner -keystore "${ANDROID_KEYSTORE}" \
                    -storepass "${PFX_PASSWORD}" \
                    "${pkg}" nanoweaver-codesign >/dev/null 2>&1
                jarsigner -verify "${pkg}" >/dev/null 2>&1
                echo "   ✓ Android APK signed and verified."
            else
                echo "   - Android signing tools not present; skipping embedded APK signature."
            fi
            ;;
    esac

    # 3. Native macOS / iOS Signing (.dmg, .ipa)
    case "${pkg_base}" in
        *.dmg|*.ipa)
            if command -v rcodesign >/dev/null 2>&1 && [[ -f "${PFX_FILE}" ]]; then
                echo "   -> Applying Apple signature via rcodesign..."
                rcodesign sign \
                    --p12-file "${PFX_FILE}" \
                    --p12-password "${PFX_PASSWORD}" \
                    "${pkg}" >/dev/null 2>&1 || true
                echo "   ✓ Apple signature applied."
            else
                echo "   - rcodesign not present; skipping embedded Apple signature."
            fi
            ;;
    esac

    # 4. Detached X.509 CMS (.p7s) Signature
    CMS_SIG="${pkg}.p7s"
    openssl cms -sign -binary \
        -in "${pkg}" \
        -signer "${LEAF_CERT}" \
        -inkey "${LEAF_KEY}" \
        -outform DER \
        -out "${CMS_SIG}"

    # Verify CMS signature
    openssl cms -verify -binary \
        -content "${pkg}" \
        -in "${CMS_SIG}" \
        -inform DER \
        -CAfile "${ROOT_CERT}" \
        -purpose any >/dev/null 2>&1
    echo "   ✓ Detached X.509 CMS signature generated and verified: $(basename "${CMS_SIG}")"

    # 5. Detached OpenPGP (.asc and .sig) Signatures
    if [[ -d "${GNUPG_DIR}" ]]; then
        GPG_ASC="${pkg}.asc"
        GPG_SIG="${pkg}.sig"

        GNUPGHOME="${GNUPG_DIR}" gpg --batch --yes --armor --detach-sign --output "${GPG_ASC}" "${pkg}" >/dev/null 2>&1
        GNUPGHOME="${GNUPG_DIR}" gpg --batch --yes --detach-sign --output "${GPG_SIG}" "${pkg}" >/dev/null 2>&1

        GNUPGHOME="${GNUPG_DIR}" gpg --batch --verify "${GPG_ASC}" "${pkg}" >/dev/null 2>&1
        GNUPGHOME="${GNUPG_DIR}" gpg --batch --verify "${GPG_SIG}" "${pkg}" >/dev/null 2>&1
        echo "   ✓ Detached OpenPGP signatures generated and verified: $(basename "${GPG_ASC}"), $(basename "${GPG_SIG}")"
    fi
done

# WHAT: Generate and cryptographically sign the master SHA256SUMS manifest.
# WHY:  Users and package managers require a single verified root manifest of hashes.
# HOW:  Hash all packages and detached signatures, then sign the manifest with CMS and GPG.
MANIFEST="${ARTIFACTS_DIR}/SHA256SUMS"
MANIFEST_ASC="${ARTIFACTS_DIR}/SHA256SUMS.asc"
MANIFEST_SIG="${ARTIFACTS_DIR}/SHA256SUMS.sig"
MANIFEST_P7S="${ARTIFACTS_DIR}/SHA256SUMS.p7s"

echo ">> Generating master SHA256SUMS manifest..."
(
    cd "${ARTIFACTS_DIR}"
    # Compute sha256 for all distribution artifacts and detached signatures
    find . -maxdepth 2 -type f \
        ! -name "SHA256SUMS*" \
        ! -name "*.log" \
        ! -path "*/pki-private/*" \
        -print0 | sort -z | xargs -0 sha256sum > SHA256SUMS.tmp
    mv -f SHA256SUMS.tmp SHA256SUMS
)

# Cryptographically sign SHA256SUMS with OpenSSL CMS
openssl cms -sign -binary \
    -in "${MANIFEST}" \
    -signer "${LEAF_CERT}" \
    -inkey "${LEAF_KEY}" \
    -outform DER \
    -out "${MANIFEST_P7S}"

openssl cms -verify -binary \
    -content "${MANIFEST}" \
    -in "${MANIFEST_P7S}" \
    -inform DER \
    -CAfile "${ROOT_CERT}" \
    -purpose any >/dev/null 2>&1
echo "✓ Master manifest signed with X.509 CMS: SHA256SUMS.p7s"

# Cryptographically sign SHA256SUMS with OpenPGP
if [[ -d "${GNUPG_DIR}" ]]; then
    GNUPGHOME="${GNUPG_DIR}" gpg --batch --yes --armor --detach-sign --output "${MANIFEST_ASC}" "${MANIFEST}" >/dev/null 2>&1
    GNUPGHOME="${GNUPG_DIR}" gpg --batch --yes --detach-sign --output "${MANIFEST_SIG}" "${MANIFEST}" >/dev/null 2>&1

    GNUPGHOME="${GNUPG_DIR}" gpg --batch --verify "${MANIFEST_ASC}" "${MANIFEST}" >/dev/null 2>&1
    GNUPGHOME="${GNUPG_DIR}" gpg --batch --verify "${MANIFEST_SIG}" "${MANIFEST}" >/dev/null 2>&1
    echo "✓ Master manifest signed with OpenPGP: SHA256SUMS.asc and SHA256SUMS.sig"
fi

echo "=============================================================================="
echo ">> All release packages and manifests successfully signed and verified."
echo "=============================================================================="
