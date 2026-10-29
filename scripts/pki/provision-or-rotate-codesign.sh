#!/usr/bin/env bash
# ==============================================================================
# NanoWeaver HRT: Sovereign Code-Signing PKI & Automated 90-Day Rotation
# ------------------------------------------------------------------------------
# WHAT: Provisions or rotates the sovereign X.509 code-signing certificate hierarchy
#       and OpenPGP/GPG archive signing keypairs for nanoweaver-hrt binary packages.
# WHY:  Binary packages (.deb, .rpm, .pkg.tar.zst, Windows .exe/.msix, macOS bundles,
#       and Android APKs) require cryptographic signatures. Standard ACME/Let's Encrypt
#       strictly forbids Code Signing EKU (OID 1.3.6.1.5.5.7.3.3). This script provides
#       an automated 90-day short-lived rotation lifecycle matching modern security standards
#       without reliance on commercial CA vendor lock-in.
# HOW:  Uses OpenSSL to inspect leaf certificate expiration; if expiring (< 30 days)
#       or missing, issues a 90-day leaf certificate signed by the Sovereign Root CA,
#       exports PKCS#12 (.pfx) and Android keystores, creates an OpenPGP Ed25519 signing
#       key, and updates public trust anchors in docs/assets/keys/.
# ==============================================================================

set -euo pipefail

# WHAT: Parse command-line flags and establish directory targets.
# WHY:  Callers must be able to specify custom artifact destinations and rotation thresholds.
# HOW:  Iterate through argv and set canonical default paths.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

OUT_DIR="${REPO_ROOT}/artifacts/pki-private"
PUBLIC_KEYS_DIR="${REPO_ROOT}/docs/assets/keys"
CHECK_EXPIRY_SECONDS=2592000 # 30 days = 30 * 86400 seconds
FORCE_ROTATION=0
PFX_PASSWORD="${CODESIGN_PFX_PASSWORD:-nanoweaver-hrt}"
CA_KEY_FILE=""
CA_CERT_FILE=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --out-dir)
            OUT_DIR="$2"
            shift 2
            ;;
        --public-keys-dir)
            PUBLIC_KEYS_DIR="$2"
            shift 2
            ;;
        --check-expiry-seconds)
            CHECK_EXPIRY_SECONDS="$2"
            shift 2
            ;;
        --force)
            FORCE_ROTATION=1
            shift
            ;;
        --pfx-password)
            PFX_PASSWORD="$2"
            shift 2
            ;;
        --ca-key)
            CA_KEY_FILE="$2"
            shift 2
            ;;
        --ca-cert)
            CA_CERT_FILE="$2"
            shift 2
            ;;
        -h|--help)
            echo "Usage: $0 [options]"
            echo "Options:"
            echo "  --out-dir <dir>                 Private PKI output directory (default: artifacts/pki-private)"
            echo "  --public-keys-dir <dir>         Public trust anchor destination (default: docs/assets/keys)"
            echo "  --check-expiry-seconds <secs>   Expiry check buffer in seconds (default: 2592000 / 30 days)"
            echo "  --force                         Force certificate rotation even if unexpired"
            echo "  --pfx-password <pwd>            Password for PKCS#12 bundle (default: nanoweaver-hrt)"
            echo "  --ca-key <file>                 Path to existing Root CA private key"
            echo "  --ca-cert <file>                Path to existing Root CA certificate"
            exit 0
            ;;
        *)
            echo "Unknown argument: $1" >&2
            exit 1
            ;;
    esac
done

mkdir -p "${OUT_DIR}"
chmod 700 "${OUT_DIR}"
mkdir -p "${PUBLIC_KEYS_DIR}"

LEAF_CERT_PUB="${PUBLIC_KEYS_DIR}/nanoweaver-codesign-leaf.crt"
ROOT_CERT_PUB="${PUBLIC_KEYS_DIR}/nanoweaver-codesign-root-ca.crt"
ARCHIVE_KEYRING_PUB="${PUBLIC_KEYS_DIR}/nanoweaver-archive-keyring.asc"
LEAF_KEY_PRIV="${OUT_DIR}/nanoweaver-codesign-leaf.key"
PFX_PRIV="${OUT_DIR}/codesign-leaf.pfx"
GNUPG_DIR_PRIV="${OUT_DIR}/gnupg"

# WHAT: Evaluate current leaf certificate validity, expiry threshold, and private key presence.
# WHY:  Avoid unnecessary rotation churn if the certificate still has sufficient validity
#       AND matching private signing keys already exist in the target output directory.
# HOW:  Check presence of private leaf key, PKCS#12 bundle, and GPG directory, verify public keys
#       match the private key, and execute openssl x509 -checkend with the configured threshold seconds.
if [[ ${FORCE_ROTATION} -eq 0 && -f "${LEAF_CERT_PUB}" && -f "${ROOT_CERT_PUB}" && -f "${LEAF_KEY_PRIV}" && -f "${PFX_PRIV}" && -d "${GNUPG_DIR_PRIV}" ]]; then
    if openssl x509 -in "${LEAF_CERT_PUB}" -checkend "${CHECK_EXPIRY_SECONDS}" -noout >/dev/null 2>&1; then
        if openssl verify -CAfile "${ROOT_CERT_PUB}" "${LEAF_CERT_PUB}" >/dev/null 2>&1; then
            PUB_FROM_CERT="$(openssl x509 -in "${LEAF_CERT_PUB}" -noout -pubkey 2>/dev/null || true)"
            PUB_FROM_KEY="$(openssl pkey -in "${LEAF_KEY_PRIV}" -pubout 2>/dev/null || true)"
            if [[ -n "${PUB_FROM_CERT}" && "${PUB_FROM_CERT}" == "${PUB_FROM_KEY}" ]]; then
                EXPIRY_DATE="$(openssl x509 -in "${LEAF_CERT_PUB}" -noout -enddate | cut -d= -f2)"
                echo "=============================================================================="
                echo ">> Sovereign Code-Signing Leaf Certificate & Private Keys are VALID (No rotation required)"
                echo "   Leaf:       ${LEAF_CERT_PUB}"
                echo "   Private:    ${LEAF_KEY_PRIV}"
                echo "   Expires on: ${EXPIRY_DATE}"
                echo "   Threshold:  ${CHECK_EXPIRY_SECONDS}s ($(( CHECK_EXPIRY_SECONDS / 86400 )) days)"
                echo "=============================================================================="
                exit 0
            fi
        fi
    fi
    echo ">> Existing leaf certificate is expiring within ${CHECK_EXPIRY_SECONDS}s, verification failed, or private keys mismatched. Proceeding with rotation..."
fi

echo "=============================================================================="
echo ">> Provisioning / Rotating Sovereign Code-Signing PKI Hierarchy..."
echo "=============================================================================="

# WHAT: Ensure Root CA private key and self-signed certificate availability.
# WHY:  Leaf certificates must be issued by the authoritative Sovereign Root CA anchor.
# HOW:  Load from environment secret, existing file, or mint a new ECDSA P-384 Root CA.
ROOT_CA_KEY="${OUT_DIR}/nanoweaver-codesign-root-ca.key"
ROOT_CA_CERT="${OUT_DIR}/nanoweaver-codesign-root-ca.crt"

if [[ -n "${CA_KEY_FILE}" && -f "${CA_KEY_FILE}" ]]; then
    echo ">> Using supplied Root CA private key: ${CA_KEY_FILE}"
    cp "${CA_KEY_FILE}" "${ROOT_CA_KEY}"
    chmod 600 "${ROOT_CA_KEY}"
elif [[ -n "${CODESIGN_CA_PRIVATE_KEY_PEM:-}" ]]; then
    echo ">> Ingesting Root CA private key from CODESIGN_CA_PRIVATE_KEY_PEM secret..."
    printf "%s\n" "${CODESIGN_CA_PRIVATE_KEY_PEM}" > "${ROOT_CA_KEY}"
    chmod 600 "${ROOT_CA_KEY}"
fi

if [[ -n "${CA_CERT_FILE}" && -f "${CA_CERT_FILE}" ]]; then
    echo ">> Using supplied Root CA certificate: ${CA_CERT_FILE}"
    cp "${CA_CERT_FILE}" "${ROOT_CA_CERT}"
elif [[ -f "${ROOT_CERT_PUB}" && -f "${ROOT_CA_KEY}" ]]; then
    cp "${ROOT_CERT_PUB}" "${ROOT_CA_CERT}"
fi

if [[ ! -f "${ROOT_CA_KEY}" || ! -f "${ROOT_CA_CERT}" ]]; then
    echo ">> Generating new Sovereign Code Signing Root CA (ECDSA P-384, 10-year validity)..."
    openssl ecparam -name secp384r1 -genkey -noout -out "${ROOT_CA_KEY}"
    chmod 600 "${ROOT_CA_KEY}"

    CA_CONFIG="${OUT_DIR}/root-ca.cnf"
    cat << 'EOF' > "${CA_CONFIG}"
[ req ]
distinguished_name = req_distinguished_name
prompt = no
x509_extensions = v3_ca

[ req_distinguished_name ]
CN = NanoWeaver HRT Sovereign Code Signing Root CA
O = Lantea Sovereign Research
C = US

[ v3_ca ]
basicConstraints = critical, CA:TRUE, pathlen:0
keyUsage = critical, keyCertSign, cRLSign
subjectKeyIdentifier = hash
authorityKeyIdentifier = keyid:always,issuer
EOF

    openssl req -new -x509 \
        -key "${ROOT_CA_KEY}" \
        -out "${ROOT_CA_CERT}" \
        -days 3650 \
        -config "${CA_CONFIG}"
    rm -f "${CA_CONFIG}"
fi

# Copy Root CA certificate to public trust anchors
cp "${ROOT_CA_CERT}" "${ROOT_CERT_PUB}"
echo "✓ Root CA Certificate published to ${ROOT_CERT_PUB}"

# WHAT: Mint fresh 90-day leaf certificate with Code Signing EKU.
# WHY:  Operating system package verifiers require digitalSignature keyUsage and codeSigning EKU.
# HOW:  Generate ECDSA P-384 private key, create CSR, and sign with Root CA specifying 90 days validity.
LEAF_KEY="${OUT_DIR}/nanoweaver-codesign-leaf.key"
LEAF_CERT="${OUT_DIR}/nanoweaver-codesign-leaf.crt"
LEAF_CSR="${OUT_DIR}/nanoweaver-codesign-leaf.csr"
LEAF_CONFIG="${OUT_DIR}/leaf-cert.cnf"

echo ">> Minting 90-day Leaf Code-Signing Certificate (ECDSA P-384, EKU = codeSigning)..."
openssl ecparam -name secp384r1 -genkey -noout -out "${LEAF_KEY}"
chmod 600 "${LEAF_KEY}"

cat << 'EOF' > "${LEAF_CONFIG}"
[ req ]
distinguished_name = req_distinguished_name
prompt = no
req_extensions = v3_req

[ req_distinguished_name ]
CN = NanoWeaver HRT Release Engineering
O = Lantea Sovereign Research
C = US

[ v3_req ]
basicConstraints = critical, CA:FALSE
keyUsage = critical, digitalSignature
extendedKeyUsage = critical, 1.3.6.1.5.5.7.3.3
subjectKeyIdentifier = hash

[ v3_leaf ]
basicConstraints = critical, CA:FALSE
keyUsage = critical, digitalSignature
extendedKeyUsage = critical, 1.3.6.1.5.5.7.3.3
subjectKeyIdentifier = hash
authorityKeyIdentifier = keyid,issuer
EOF

openssl req -new \
    -key "${LEAF_KEY}" \
    -out "${LEAF_CSR}" \
    -config "${LEAF_CONFIG}"

SERIAL_FILE="${OUT_DIR}/ca.srl"
if [[ ! -f "${SERIAL_FILE}" ]]; then
    echo "01" > "${SERIAL_FILE}"
fi

openssl x509 -req \
    -in "${LEAF_CSR}" \
    -CA "${ROOT_CA_CERT}" \
    -CAkey "${ROOT_CA_KEY}" \
    -CAserial "${SERIAL_FILE}" \
    -out "${LEAF_CERT}" \
    -days 90 \
    -extfile "${LEAF_CONFIG}" \
    -extensions v3_leaf

rm -f "${LEAF_CSR}" "${LEAF_CONFIG}"
cp "${LEAF_CERT}" "${LEAF_CERT_PUB}"
echo "✓ 90-Day Leaf Code-Signing Certificate published to ${LEAF_CERT_PUB}"

# WHAT: Package PKCS#12 (.pfx) bundle and Android keystore.
# WHY:  Windows Authenticode (osslsigncode) and Android (apksigner) require standard keystore formats.
# HOW:  Invoke openssl pkcs12 export with root chain and convert via keytool if present.
PFX_FILE="${OUT_DIR}/codesign-leaf.pfx"
openssl pkcs12 -export \
    -out "${PFX_FILE}" \
    -inkey "${LEAF_KEY}" \
    -in "${LEAF_CERT}" \
    -certfile "${ROOT_CA_CERT}" \
    -name "nanoweaver-codesign" \
    -password "pass:${PFX_PASSWORD}"
chmod 600 "${PFX_FILE}"
echo "✓ PKCS#12 bundle generated at ${PFX_FILE}"

ANDROID_KEYSTORE="${OUT_DIR}/codesign-android.keystore"
if command -v keytool >/dev/null 2>&1; then
    rm -f "${ANDROID_KEYSTORE}"
    keytool -importkeystore \
        -srckeystore "${PFX_FILE}" \
        -srcstoretype PKCS12 \
        -destkeystore "${ANDROID_KEYSTORE}" \
        -deststoretype PKCS12 \
        -srcstorepass "${PFX_PASSWORD}" \
        -deststorepass "${PFX_PASSWORD}" \
        -noprompt >/dev/null 2>&1 || true
    if [[ -f "${ANDROID_KEYSTORE}" ]]; then
        chmod 600 "${ANDROID_KEYSTORE}"
        echo "✓ Android signing keystore generated at ${ANDROID_KEYSTORE}"
    fi
fi

# WHAT: Generate 90-day OpenPGP Ed25519 signing key and export public keyring.
# WHY:  Linux package managers and detached signature verifiers authenticate via OpenPGP.
# HOW:  Run unattended gpg --batch --generate-key in an isolated homedir and export armored public key.
GNUPG_DIR="${OUT_DIR}/gnupg"
mkdir -p "${GNUPG_DIR}"
chmod 700 "${GNUPG_DIR}"

GPG_CONF="${OUT_DIR}/gpg-batch.conf"
cat << 'EOF' > "${GPG_CONF}"
%no-protection
Key-Type: EDDSA
Key-Curve: ed25519
Key-Usage: sign
Subkey-Type: ECDH
Subkey-Curve: cv25519
Subkey-Usage: encrypt
Name-Real: NanoWeaver HRT Archive Signing Key
Name-Email: release@lantea.one
Expire-Date: 90d
%commit
EOF

GNUPGHOME="${GNUPG_DIR}" gpg --batch --generate-key "${GPG_CONF}" >/dev/null 2>&1
rm -f "${GPG_CONF}"

GNUPGHOME="${GNUPG_DIR}" gpg --armor --export "release@lantea.one" > "${ARCHIVE_KEYRING_PUB}"
cp "${ARCHIVE_KEYRING_PUB}" "${OUT_DIR}/nanoweaver-archive-keyring.asc"
echo "✓ OpenPGP Public Keyring exported to ${ARCHIVE_KEYRING_PUB}"

# WHAT: Verify cryptographic validity of all minted artifacts.
# WHY:  Fail immediately if the leaf certificate fails validation against the root CA.
# HOW:  Execute openssl verify with root CA and verify codeSigning purpose.
echo "=============================================================================="
echo ">> Validating Issued PKI Assets..."
echo "=============================================================================="
openssl verify -CAfile "${ROOT_CERT_PUB}" "${LEAF_CERT_PUB}"
openssl verify -purpose codesign -CAfile "${ROOT_CERT_PUB}" "${LEAF_CERT_PUB}"

EKU_OUTPUT="$(openssl x509 -in "${LEAF_CERT_PUB}" -noout -ext extendedKeyUsage)"
if [[ "${EKU_OUTPUT}" != *"Code Signing"* ]]; then
    echo "ERROR: Leaf certificate does not contain required 'Code Signing' Extended Key Usage!" >&2
    echo "Actual output: ${EKU_OUTPUT}" >&2
    exit 1
fi
echo "✓ Leaf certificate contains critical Code Signing EKU."

openssl pkcs12 -in "${PFX_FILE}" -noout -password "pass:${PFX_PASSWORD}"
echo "✓ PKCS#12 archive bundle successfully decrypted and validated."

LEAF_FINGERPRINT="$(openssl x509 -in "${LEAF_CERT_PUB}" -noout -fingerprint -sha256)"
ROOT_FINGERPRINT="$(openssl x509 -in "${ROOT_CERT_PUB}" -noout -fingerprint -sha256)"
echo "------------------------------------------------------------------------------"
echo "Root CA Fingerprint: ${ROOT_FINGERPRINT}"
echo "Leaf CA Fingerprint: ${LEAF_FINGERPRINT}"
echo "=============================================================================="
echo ">> Sovereign Code-Signing PKI Provisioning & Rotation Complete."
echo "=============================================================================="
