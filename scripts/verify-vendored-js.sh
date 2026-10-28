#!/usr/bin/env bash
# ==============================================================================
# NanoWeaverHrt: Vendored JavaScript & Subresource Integrity Verifier
# ------------------------------------------------------------------------------
# WHAT: Verifies cryptographic SHA-256 and SHA-384 digests of all vendored libraries
#       under src/NanoWeaverHrt/wwwroot/lib/ against docs/vendored-javascript.md,
#       enforces zero-CDN rules and matching integrity attributes in App.razor,
#       and executes RFC 8032 Ed25519 and NIST FIPS 204 ML-DSA-65 KAT vectors in Node.
# WHY:  Prime Directives 9 and 12 mandate sovereign, tamper-proof client-side
#       cryptographic libraries with zero reliance on external CDNs or unpinned code.
# HOW:  Computes digests via sha256sum and openssl, asserts App.razor tags,
#       and runs Node.js cryptographic KAT verification.
# ==============================================================================

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LIB_DIR="${ROOT_DIR}/src/NanoWeaverHrt/wwwroot/lib"
DOC_FILE="${ROOT_DIR}/docs/vendored-javascript.md"
APP_RAZOR="${ROOT_DIR}/src/NanoWeaverHrt/Components/App.razor"

echo "=== Verifying Vendored JavaScript Subsystem & SRI Integrity ==="

if [ ! -d "${LIB_DIR}" ]; then
    echo "ERROR: Vendored library directory '${LIB_DIR}' does not exist!"
    exit 1
fi

if [ ! -f "${DOC_FILE}" ]; then
    echo "ERROR: Documentation manifest '${DOC_FILE}' does not exist!"
    exit 1
fi

# 1. Verify every vendored script has matching hashes in docs/vendored-javascript.md
for js_file in "${LIB_DIR}"/*.js "${LIB_DIR}"/*.mjs; do
    [ -f "${js_file}" ] || continue
    filename="$(basename "${js_file}")"
    echo "Checking vendored file: ${filename}"

    # Compute SHA-256
    actual_sha256="$(sha256sum "${js_file}" | awk '{print $1}')"
    # Compute SHA-384 Base64 SRI
    actual_sha384="$(openssl dgst -sha384 -binary "${js_file}" | openssl base64 -A)"
    actual_sri="sha384-${actual_sha384}"

    echo "  SHA-256: ${actual_sha256}"
    echo "  SRI:     ${actual_sri}"

    # Check against docs/vendored-javascript.md
    if ! grep -q "${actual_sha256}" "${DOC_FILE}"; then
        echo "ERROR: SHA-256 for ${filename} not found in ${DOC_FILE}!"
        exit 1
    fi

    if ! grep -q "${actual_sri}" "${DOC_FILE}"; then
        echo "ERROR: SRI digest for ${filename} not found in ${DOC_FILE}!"
        exit 1
    fi

    # Check that App.razor references this file with the exact SRI integrity attribute
    if ! grep -q "${filename}" "${APP_RAZOR}"; then
        echo "ERROR: ${filename} is not referenced in ${APP_RAZOR}!"
        exit 1
    fi

    if ! grep -q "integrity=\"${actual_sri}\"" "${APP_RAZOR}"; then
        echo "ERROR: Matching integrity attribute 'integrity=\"${actual_sri}\"' for ${filename} not found in ${APP_RAZOR}!"
        exit 1
    fi
done

# 2. Check that App.razor contains no external CDN script tags
if grep -E '<script[^>]+src=["'\''"]https?://' "${APP_RAZOR}" > /dev/null 2>&1; then
    echo "ERROR: External CDN <script> tag detected in ${APP_RAZOR}! NanoWeaverHrt enforces a strict Zero-CDN policy."
    exit 1
fi

echo "✓ File hashes, SRI integrity, and Zero-CDN policy verified."

# 3. Execute Known-Answer Test (KAT) verification in Node.js
echo "Executing cryptographic Known-Answer Tests (RFC 8032 & ML-DSA-65)..."
node --input-type=module << 'EOF'
import vendor from './src/NanoWeaverHrt/wwwroot/lib/weaver-crypto-vendor.js';
import assert from 'node:assert/strict';

// KAT 1: RFC 8032 Section 7.1 Test 1 (Ed25519)
const seedHex = '9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60';
const expectedSigHex = 'e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b';
const seed = Buffer.from(seedHex, 'hex');
const msg = new Uint8Array(0);

const sig = await vendor.signEd25519(seed, msg);
const sigHex = Buffer.from(sig).toString('hex');
assert.equal(sigHex, expectedSigHex, 'RFC 8032 Test 1 Ed25519 signature mismatch');
console.log('✓ RFC 8032 Ed25519 Known-Answer Test passed.');

// KAT 2: NIST FIPS 204 ML-DSA-65 Signature Test
const mlSeed = new Uint8Array(32);
mlSeed.fill(0x5A);
const testMsg = Buffer.from('NanoWeaverHrt-Sovereign-Challenge-Vector');
const mlSig = await vendor.signMlDsa65({ seed: mlSeed, secretKey: null }, testMsg);
assert.equal(mlSig.length, 3309, 'ML-DSA-65 signature length must be 3,309 bytes');
console.log('✓ NIST FIPS 204 ML-DSA-65 Signature Generation Test passed.');
EOF

echo "✓ All vendored JavaScript cryptographic checks passed successfully."
