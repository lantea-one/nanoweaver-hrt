/**
 * ============================================================================
 * NanoWeaverHrt (nanoweaver-hrt): Client-Side Challenge-Response Auth
 * ----------------------------------------------------------------------------
 * WHAT: Browser-side private key parsing, PKCS#8 extraction, envelope decryption,
 *       cryptographic challenge signing, and session handshake.
 * WHY:  Prime Directive 12 & Audit C8 mandate asymmetric public-key challenge-response
 *       authentication without transmitting raw private keys to the server.
 * HOW:  Parses Ed25519 and ML-DSA-65 PKCS#8 DER bytes (BouncyCastle 2.7 layouts),
 *       decrypts AES-256-GCM / PBKDF2 envelopes in browser memory, requests a
 *       server challenge nonce, signs it with the vendored crypto module, zeroizes
 *       in-memory buffers, and submits the signature to /api/auth/web-token.
 * ============================================================================
 */

/**
 * Parses raw PEM text into private key metadata and DER bytes.
 *
 * WHAT: Validates PEM framing, detects encryption headers, and decodes Base64 DER bytes.
 * WHY:  User accounts provide Ed25519 or ML-DSA-65 keys in standard PEM envelopes.
 * HOW:  Inspects BEGIN/END headers, strips framing, and converts Base64 to Uint8Array.
 *
 * @param {string} pemText Raw private key PEM string.
 * @returns {{ ok: boolean, isEncrypted?: boolean, derBytes?: Uint8Array, error?: string }}
 */
function parsePrivateKeyPem(pemText) {
    if (typeof pemText !== 'string' || pemText.trim().length === 0) {
        return { ok: false, error: 'Username and private key are both required.' };
    }

    let isEncrypted = false;
    if (pemText.includes('-----BEGIN ENCRYPTED PRIVATE KEY-----')) {
        isEncrypted = true;
    } else if (pemText.includes('-----BEGIN PRIVATE KEY-----')) {
        isEncrypted = false;
    } else {
        return { ok: false, error: 'Failed to parse or decrypt private key. Check passphrase or format.' };
    }

    const cleanBase64 = pemText
        .replace(/-----BEGIN [A-Z\s]+-----/g, '')
        .replace(/-----END [A-Z\s]+-----/g, '')
        .replace(/\s+/g, '');

    if (cleanBase64.length === 0) {
        return { ok: false, error: 'Failed to parse or decrypt private key. Check passphrase or format.' };
    }

    try {
        const binaryString = atob(cleanBase64);
        const derBytes = new Uint8Array(binaryString.length);
        for (let i = 0; i < binaryString.length; i++) {
            derBytes[i] = binaryString.charCodeAt(i);
        }

        return { ok: true, isEncrypted, derBytes };
    } catch {
        return { ok: false, error: 'Failed to parse or decrypt private key. Check passphrase or format.' };
    }
}

/**
 * Extracts raw private key seeds or secret keys from PKCS#8 PrivateKeyInfo DER bytes.
 *
 * WHAT: Parses BouncyCastle 2.7 PKCS#8 DER structures for Ed25519 and ML-DSA-65.
 * WHY:  Identifies key algorithm and slices seed or expanded key bytes according to P13 spike layouts.
 * HOW:  Inspects ASN.1 byte lengths and OID markers to extract seeds or secret keys.
 *
 * @param {Uint8Array} derBytes Decoded PKCS#8 DER bytes.
 * @returns {{ ok: boolean, algorithm?: string, seed?: Uint8Array|null, secretKey?: Uint8Array|null, error?: string }}
 */
function extractKeyMaterialFromPkcs8(derBytes) {
    if (!(derBytes instanceof Uint8Array)) {
        return { ok: false, error: 'Failed to parse or decrypt private key. Check passphrase or format.' };
    }

    // 1. Ed25519 (OID 1.3.101.112 = 06 03 2B 65 70, length 48 [RFC 8410 v0] or 83 [BouncyCastle v1])
    if ((derBytes.length === 48 || derBytes.length === 83) &&
        derBytes[7] === 0x06 && derBytes[8] === 0x03 && derBytes[9] === 0x2B && derBytes[10] === 0x65 && derBytes[11] === 0x70 &&
        derBytes[14] === 0x04 && derBytes[15] === 0x20) {
        return {
            ok: true,
            algorithm: 'Ed25519',
            seed: derBytes.slice(16, 48),
            secretKey: null
        };
    }

    // 2. ML-DSA-65 SeedAndEncoding (length 4098: seed at [28..60] or [30..62], expanded key at [64..4096] or [66..4098])
    if (derBytes.length === 4098 && derBytes[19] === 0x12) {
        if (derBytes[28] === 0x04 && derBytes[29] === 0x20) {
            return {
                ok: true,
                algorithm: 'MlDsa65',
                seed: derBytes.slice(30, 62),
                secretKey: derBytes.slice(66, 4098)
            };
        }
        if (derBytes[26] === 0x04 && derBytes[27] === 0x20) {
            return {
                ok: true,
                algorithm: 'MlDsa65',
                seed: derBytes.slice(28, 60),
                secretKey: derBytes.slice(64, 4096)
            };
        }
    }

    // 3. ML-DSA-65 SeedOnly (length 54: seed at [22..54])
    if (derBytes.length === 54 &&
        derBytes[17] === 0x12 &&
        derBytes[20] === 0x80 && derBytes[21] === 0x20) {
        return {
            ok: true,
            algorithm: 'MlDsa65',
            seed: derBytes.slice(22, 54),
            secretKey: null
        };
    }

    // 4. ML-DSA-65 EncodingOnly (length 4060: expanded key at [28..4060])
    if (derBytes.length === 4060 &&
        derBytes[19] === 0x12 &&
        derBytes[24] === 0x04 && derBytes[25] === 0x82) {
        return {
            ok: true,
            algorithm: 'MlDsa65',
            seed: null,
            secretKey: derBytes.slice(28, 4060)
        };
    }

    return { ok: false, error: 'Failed to parse or decrypt private key. Check passphrase or format.' };
}

/**
 * Decrypts a password-protected private key envelope ([salt 16][nonce 12][tag 16][ciphertext]).
 *
 * WHAT: Decrypts AES-256-GCM encrypted DER bytes using PBKDF2-SHA256 derived keys.
 * WHY:  Client-side counterpart to UserAuthenticationService.EncryptPrivateKeyBytes.
 * HOW:  Splits salt, nonce, tag, ciphertext, and invokes adapter.decryptAesGcmPbkdf2.
 *
 * @param {Uint8Array} envelopeBytes Encrypted private key payload bytes.
 * @param {string} passphrase User-entered encryption passphrase.
 * @param {object} [cryptoAdapter] Optional crypto provider adapter (defaults to globalThis.WeaverCryptoVendor).
 * @returns {Promise<{ ok: boolean, derBytes?: Uint8Array, error?: string }>}
 */
async function decryptKeyEnvelope(envelopeBytes, passphrase, cryptoAdapter) {
    if (!(envelopeBytes instanceof Uint8Array) || envelopeBytes.length <= 44 ||
        typeof passphrase !== 'string' || passphrase.length === 0) {
        return { ok: false, error: 'Failed to parse or decrypt private key. Check passphrase or format.' };
    }

    const salt = envelopeBytes.subarray(0, 16);
    const nonce = envelopeBytes.subarray(16, 28);
    const tag = envelopeBytes.subarray(28, 44);
    const ciphertext = envelopeBytes.subarray(44);

    const ciphertextWithTag = new Uint8Array(ciphertext.length + 16);
    ciphertextWithTag.set(ciphertext, 0);
    ciphertextWithTag.set(tag, ciphertext.length);

    const adapter = cryptoAdapter || globalThis.WeaverCryptoVendor;
    if (!adapter || typeof adapter.decryptAesGcmPbkdf2 !== 'function') {
        return { ok: false, error: 'Failed to parse or decrypt private key. Check passphrase or format.' };
    }

    try {
        const derBytes = await adapter.decryptAesGcmPbkdf2(passphrase, salt, nonce, ciphertextWithTag);
        return { ok: true, derBytes };
    } catch {
        return { ok: false, error: 'Failed to parse or decrypt private key. Check passphrase or format.' };
    }
}

/**
 * Best-effort zeroization of sensitive key byte buffers.
 *
 * WHAT: Fills TypedArray with zeros.
 * WHY:  Mitigates in-memory credential persistence in client runtime.
 * HOW:  Invokes TypedArray.prototype.fill(0).
 *
 * @param {Uint8Array} buffer Buffer to clear.
 */
function zeroizeBytes(buffer) {
    if (buffer instanceof Uint8Array) {
        buffer.fill(0);
    }
}

/**
 * Computes a digital signature over an authentication challenge nonce.
 *
 * WHAT: Signs a 32-byte challenge nonce using Ed25519 or ML-DSA-65.
 * WHY:  Provides cryptographic proof of private key possession to the server.
 * HOW:  Decodes nonceBase64, calls cryptoAdapter signing function, and Base64-encodes signature.
 *
 * @param {{ algorithm: string, seed?: Uint8Array|null, secretKey?: Uint8Array|null }} keyMaterial Key data.
 * @param {string} nonceBase64 Base64-encoded challenge nonce.
 * @param {object} [cryptoAdapter] Optional crypto provider adapter (defaults to globalThis.WeaverCryptoVendor).
 * @returns {Promise<{ ok: boolean, signatureBase64?: string, error?: string }>}
 */
async function signChallenge(keyMaterial, nonceBase64, cryptoAdapter) {
    const adapter = cryptoAdapter || globalThis.WeaverCryptoVendor;
    if (!adapter) {
        return { ok: false, error: 'Cryptographic vendor module is unavailable.' };
    }

    const binaryString = atob(nonceBase64);
    const nonceBytes = new Uint8Array(binaryString.length);
    for (let i = 0; i < binaryString.length; i++) {
        nonceBytes[i] = binaryString.charCodeAt(i);
    }

    let rawSig;
    if (keyMaterial.algorithm === 'Ed25519') {
        rawSig = await adapter.signEd25519(keyMaterial.seed, nonceBytes);
    } else {
        rawSig = await adapter.signMlDsa65(keyMaterial, nonceBytes);
    }

    let bin = '';
    for (let i = 0; i < rawSig.length; i++) {
        bin += String.fromCharCode(rawSig[i]);
    }

    return { ok: true, signatureBase64: btoa(bin) };
}

/**
 * Requests an authentication challenge nonce from the server API.
 *
 * WHAT: Posts username to /api/auth/challenge.
 * WHY:  Obtains a fresh, single-use 120s nonce.
 * HOW:  Calls fetch and parses JSON body or RFC 7807 problem details.
 *
 * @param {string} username Username requesting authentication.
 * @param {Function} [fetchImpl] Optional fetch implementation (defaults to globalThis.fetch).
 * @returns {Promise<{ ok: boolean, challengeId?: string, nonceBase64?: string, error?: string }>}
 */
async function requestChallenge(username, fetchImpl) {
    const fetchFn = fetchImpl || globalThis.fetch;
    const res = await fetchFn('/api/auth/challenge', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username })
    });

    if (!res.ok) {
        let error = 'Failed to request authentication challenge.';
        try {
            const prob = await res.json();
            if (prob && prob.detail) error = prob.detail;
        } catch {
            // Fall back to default error
        }
        return { ok: false, error };
    }

    const body = await res.json();
    return { ok: true, challengeId: body.challengeId, nonceBase64: body.nonceBase64 };
}

/**
 * Submits signed challenge signature to /api/auth/web-token to establish browser session.
 *
 * WHAT: Posts username, challengeId, and signatureBase64 to /api/auth/web-token.
 * WHY:  Completes challenge-response authentication and sets HttpOnly weaver_session cookie.
 * HOW:  Sends POST request with credentials: 'same-origin'.
 *
 * @param {string} username User account name.
 * @param {string} challengeId Identifier of the consumed challenge.
 * @param {string} signatureBase64 Base64-encoded signature.
 * @param {Function} [fetchImpl] Optional fetch implementation (defaults to globalThis.fetch).
 * @returns {Promise<{ ok: boolean, token?: string, user?: object, error?: string }>}
 */
async function submitSignature(username, challengeId, signatureBase64, fetchImpl) {
    const fetchFn = fetchImpl || globalThis.fetch;
    const res = await fetchFn('/api/auth/web-token', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, challengeId, signatureBase64 })
    });

    if (!res.ok) {
        let error = 'Authentication failed.';
        try {
            const prob = await res.json();
            if (prob && prob.detail) error = prob.detail;
        } catch {
            // Fall back to default error
        }
        return { ok: false, error };
    }

    const body = await res.json();
    return { ok: true, token: body.token, user: body.user };
}

/**
 * Client-side entry point for interactive user sign-in from the Login page.
 *
 * WHAT: Reads login form inputs, parses/decrypts private key, requests challenge, signs nonce,
 *       clears sensitive DOM fields and memory buffers, and establishes the authenticated session.
 * WHY:  Ensures private keys never cross the Blazor SignalR circuit or network wire (Audit C8).
 * HOW:  Orchestrates parsePrivateKeyPem, decryptKeyEnvelope, extractKeyMaterialFromPkcs8,
 *       requestChallenge, signChallenge, zeroizeBytes, and submitSignature.
 *
 * @param {object} [options] Injection options for testing (domAdapter, cryptoAdapter, fetchImpl, sessionAdapter).
 * @returns {Promise<{ ok: boolean, user?: object, token?: string, error?: string }>}
 */
async function weaverSignIn(options = {}) {
    const doc = options.domAdapter || globalThis.document;
    const adapter = options.cryptoAdapter || globalThis.WeaverCryptoVendor;
    const fetchFn = options.fetchImpl || globalThis.fetch;
    const session = options.sessionAdapter || globalThis;

    // Loud Missing-Element Guard (P15 Trap 1)
    if (!doc || typeof doc.getElementById !== 'function') {
        return { ok: false, error: 'Sign-in form element missing from DOM.' };
    }

    const userEl = doc.getElementById('loginUser');
    const keyEl = doc.getElementById('privKeyPem');
    const passEl = doc.getElementById('loginPassword');
    if (!userEl || !keyEl || !passEl) {
        return { ok: false, error: 'Sign-in form element missing from DOM.' };
    }

    const btnEl = doc.getElementById('loginSubmitBtn');
    if (btnEl) btnEl.disabled = true;

    try {
        const username = userEl.value?.trim() || '';
        const pemText = keyEl.value || '';
        const passphrase = passEl.value || '';

        if (!username) {
            return { ok: false, error: 'Username and private key are both required.' };
        }

        const parseRes = parsePrivateKeyPem(pemText);
        if (!parseRes.ok) {
            return parseRes;
        }

        let derBytes = parseRes.derBytes;
        if (parseRes.isEncrypted) {
            const decRes = await decryptKeyEnvelope(derBytes, passphrase, adapter);
            zeroizeBytes(derBytes);
            if (!decRes.ok) {
                return decRes;
            }
            derBytes = decRes.derBytes;
        }

        const matRes = extractKeyMaterialFromPkcs8(derBytes);
        zeroizeBytes(derBytes);
        if (!matRes.ok) {
            return matRes;
        }

        const keyMaterial = matRes;
        const chalRes = await requestChallenge(username, fetchFn);
        if (!chalRes.ok) {
            if (keyMaterial.seed) zeroizeBytes(keyMaterial.seed);
            if (keyMaterial.secretKey) zeroizeBytes(keyMaterial.secretKey);
            return chalRes;
        }

        let sigRes;
        try {
            sigRes = await signChallenge(keyMaterial, chalRes.nonceBase64, adapter);
        } finally {
            if (keyMaterial.seed) zeroizeBytes(keyMaterial.seed);
            if (keyMaterial.secretKey) zeroizeBytes(keyMaterial.secretKey);
        }

        if (!sigRes.ok) {
            return sigRes;
        }

        // Clear sensitive credentials from DOM
        keyEl.value = '';
        passEl.value = '';

        const subRes = await submitSignature(username, chalRes.challengeId, sigRes.signatureBase64, fetchFn);
        if (!subRes.ok) {
            return subRes;
        }

        if (typeof session.weaverSetSession === 'function') {
            session.weaverSetSession(subRes.token);
        }

        return { ok: true, user: subRes.user, token: subRes.token };
    } finally {
        if (btnEl) btnEl.disabled = false;
    }
}

/**
 * Binds client-side change handler on a file input to read private key files without SignalR streaming.
 *
 * WHAT: Attaches a change event listener to the file input that populates the textarea.
 * WHY:  Enforces the 64 KB cap client-side and keeps private key files out of server circuit traffic.
 * HOW:  Validates file.size <= 65536 and reads file text directly via Blob.text().
 *
 * @param {string} fileInputId DOM element ID for the file input.
 * @param {string} textareaId DOM element ID for the target textarea.
 * @param {object} [options] Injection options for testing (domAdapter, onSizeError, onReadError).
 * @returns {boolean} True if successfully bound; false if elements are missing.
 */
function weaverBindKeyFileInput(fileInputId, textareaId, options = {}) {
    const doc = options.domAdapter || globalThis.document;
    if (!doc || typeof doc.getElementById !== 'function') {
        return false;
    }

    const fileInput = doc.getElementById(fileInputId);
    const textarea = doc.getElementById(textareaId);

    if (!fileInput || !textarea) {
        return false;
    }

    const handler = async (event) => {
        const file = (event.target.files && event.target.files[0]) || null;
        if (!file) return;

        if (file.size > 65536) {
            if (typeof options.onSizeError === 'function') {
                options.onSizeError('Private key file exceeds 64 KB limit.');
            }
            event.target.value = '';
            return;
        }

        try {
            textarea.value = await file.text();
        } catch {
            if (typeof options.onReadError === 'function') {
                options.onReadError('Failed to read private key file.');
            }
        }
    };

    fileInput.addEventListener('change', handler);
    return true;
}

/**
 * Converts a Uint8Array into a Base64 string.
 *
 * WHAT: Converts raw byte buffer to standard RFC 4648 Base64.
 * WHY:  Standard encoding for SubjectPublicKeyInfo and PEM serialization.
 * HOW:  Uses Buffer.from in Node or chunked btoa in the browser.
 *
 * @param {Uint8Array} bytes Byte buffer to encode.
 * @returns {string} Base64 string.
 */
function uint8ArrayToBase64(bytes) {
    if (!(bytes instanceof Uint8Array)) {
        return '';
    }
    if (typeof Buffer !== 'undefined') {
        return Buffer.from(bytes).toString('base64');
    }
    let binary = '';
    const chunkSize = 8192;
    for (let i = 0; i < bytes.length; i += chunkSize) {
        binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunkSize));
    }
    return btoa(binary);
}

/**
 * Formats raw DER bytes into standard RFC 7468 PEM text with 64-character line wrapping.
 *
 * WHAT: Wraps Base64 DER bytes in BEGIN / END header lines.
 * WHY:  Enables saving key files in human-readable and standard tool-compatible format.
 * HOW:  Base64-encodes bytes, breaks into 64-char lines, and sandwiches between delimiters.
 *
 * @param {string} label PEM header label (e.g., 'PUBLIC KEY', 'PRIVATE KEY', 'ENCRYPTED PRIVATE KEY').
 * @param {Uint8Array} derBytes Raw DER or encrypted envelope bytes.
 * @returns {string} Standard PEM formatted string ending with a newline.
 */
function formatPem(label, derBytes) {
    if (typeof label !== 'string' || !(derBytes instanceof Uint8Array)) {
        return '';
    }
    const b64 = uint8ArrayToBase64(derBytes);
    const lines = [];
    for (let i = 0; i < b64.length; i += 64) {
        lines.push(b64.slice(i, i + 64));
    }
    return `-----BEGIN ${label}-----\n${lines.join('\n')}\n-----END ${label}-----\n`;
}

/**
 * Encrypts raw private key DER bytes into a password-protected envelope.
 *
 * WHAT: Encrypts DER bytes using AES-256-GCM with a PBKDF2-SHA256 derived key (100,000 iterations).
 * WHY:  Symmetric client-side implementation of the node's standard envelope format:
 *       [Salt: 16][Nonce: 12][Tag: 16][Ciphertext: N].
 * HOW:  Generates 16-byte salt and 12-byte nonce, invokes adapter.encryptAesGcmPbkdf2,
 *       and formats output bytes.
 *
 * @param {Uint8Array} derBytes Raw private key DER bytes to encrypt.
 * @param {string} passphrase User-entered passphrase.
 * @param {object} [cryptoAdapter] Optional crypto provider adapter (defaults to globalThis.WeaverCryptoVendor).
 * @returns {Promise<{ ok: boolean, envelopeBytes?: Uint8Array, error?: string }>}
 */
async function encryptKeyEnvelope(derBytes, passphrase, cryptoAdapter) {
    if (!(derBytes instanceof Uint8Array) || derBytes.length === 0 ||
        typeof passphrase !== 'string' || passphrase.length === 0) {
        return { ok: false, error: 'Failed to encrypt private key. Invalid input or empty passphrase.' };
    }

    const adapter = cryptoAdapter || globalThis.WeaverCryptoVendor;
    if (!adapter || typeof adapter.encryptAesGcmPbkdf2 !== 'function') {
        return { ok: false, error: 'Failed to encrypt private key. Cryptographic provider unavailable.' };
    }

    try {
        const salt = new Uint8Array(16);
        const nonce = new Uint8Array(12);
        globalThis.crypto.getRandomValues(salt);
        globalThis.crypto.getRandomValues(nonce);

        const encryptedWithTag = await adapter.encryptAesGcmPbkdf2(passphrase, salt, nonce, derBytes);
        if (!(encryptedWithTag instanceof Uint8Array) || encryptedWithTag.length < 16) {
            return { ok: false, error: 'Failed to encrypt private key. Encryption returned invalid ciphertext.' };
        }

        const tag = encryptedWithTag.slice(encryptedWithTag.length - 16);
        const ciphertext = encryptedWithTag.slice(0, encryptedWithTag.length - 16);

        // Standard layout: [Salt: 16][Nonce: 12][Tag: 16][Ciphertext: N]
        const envelopeBytes = new Uint8Array(16 + 12 + 16 + ciphertext.length);
        envelopeBytes.set(salt, 0);
        envelopeBytes.set(nonce, 16);
        envelopeBytes.set(tag, 28);
        envelopeBytes.set(ciphertext, 44);

        return { ok: true, envelopeBytes };
    } catch {
        return { ok: false, error: 'Failed to encrypt private key envelope.' };
    }
}

/**
 * Generates an Ed25519 keypair in browser memory.
 *
 * WHAT: Mints 32-byte Ed25519 seed and derives SPKI (44 bytes) and PKCS#8 (48 bytes) DER bytes.
 * WHY:  Client-side key derivation ensuring private keys never touch the network.
 * HOW:  Invokes adapter.keygenEd25519.
 *
 * @param {object} [options] Optional parameters (e.g. seed, cryptoAdapter).
 * @returns {Promise<{ seed: Uint8Array, publicKey: Uint8Array, spkiDer: Uint8Array, pkcs8Der: Uint8Array }>}
 */
async function generateEd25519Keypair(options = {}) {
    const adapter = options.cryptoAdapter || globalThis.WeaverCryptoVendor;
    if (!adapter || typeof adapter.keygenEd25519 !== 'function') {
        throw new Error('Ed25519 key generation provider is unavailable.');
    }
    return adapter.keygenEd25519(options.seed);
}

/**
 * Generates an ML-DSA-65 post-quantum keypair in browser memory.
 *
 * WHAT: Mints 32-byte seed and derives ML-DSA-65 SPKI (1974 bytes) and PKCS#8 (4098 bytes) DER bytes.
 * WHY:  NIST FIPS 204 client-side post-quantum key generation.
 * HOW:  Invokes adapter.keygenMlDsa65.
 *
 * @param {object} [options] Optional parameters (e.g. seed, cryptoAdapter).
 * @returns {Promise<{ seed: Uint8Array, publicKey: Uint8Array, secretKey: Uint8Array, spkiDer: Uint8Array, pkcs8Der: Uint8Array }>}
 */
async function generateMlDsa65Keypair(options = {}) {
    const adapter = options.cryptoAdapter || globalThis.WeaverCryptoVendor;
    if (!adapter || typeof adapter.keygenMlDsa65 !== 'function') {
        throw new Error('ML-DSA-65 key generation provider is unavailable.');
    }
    return adapter.keygenMlDsa65(options.seed);
}

/**
 * Generates an asymmetric cryptographic keypair and bundle in client-side memory.
 *
 * WHAT: Derives Ed25519 or ML-DSA-65 keypair, optionally encrypts private key with passphrase,
 *       formats PEM text, and returns full GeneratedKeyBundle matching the server DTO.
 * WHY:  Fulfills Phase P20 (Audit C8 zero-knowledge architecture): private keys are minted directly
 *       in browser memory and NEVER transmitted to the server.
 * HOW:  Calls generateEd25519Keypair or generateMlDsa65Keypair, invokes encryptKeyEnvelope if
 *       passphrase is provided, formats PEMs, and returns bundle object.
 *
 * @param {string} username Target identity handle.
 * @param {string|number} role User authorization role.
 * @param {string|number} accessGate Access gating constraint.
 * @param {string|number} algorithm KeyAlgorithm (Ed25519 or MlDsa65).
 * @param {string|null} [passphrase] Optional user passphrase to encrypt private key.
 * @param {object} [options] Optional overrides (e.g., cryptoAdapter, seed).
 * @returns {Promise<{
 *   username: string,
 *   role: string|number,
 *   accessGate: string|number,
 *   algorithm: string|number,
 *   publicKeyBase64: string,
 *   publicKeyPem: string,
 *   privateKeyPem: string,
 *   isPasswordProtected: boolean
 * }>}
 */
async function weaverGenerateKeyBundle(username, role, accessGate, algorithm, passphrase, options = {}) {
    if (typeof username !== 'string' || username.trim().length === 0) {
        throw new Error('Username must not be empty.');
    }

    const isMlDsa = algorithm === 1 || algorithm === 'MlDsa65';
    const key = isMlDsa
        ? await generateMlDsa65Keypair(options)
        : await generateEd25519Keypair(options);

    const publicKeyPem = formatPem('PUBLIC KEY', key.spkiDer);
    const publicKeyBase64 = uint8ArrayToBase64(key.spkiDer);
    let privateKeyPem;
    let isPasswordProtected = false;

    if (typeof passphrase === 'string' && passphrase.length > 0) {
        const encRes = await encryptKeyEnvelope(key.pkcs8Der, passphrase, options.cryptoAdapter);
        if (!encRes.ok) {
            throw new Error(encRes.error);
        }
        privateKeyPem = formatPem('ENCRYPTED PRIVATE KEY', encRes.envelopeBytes);
        isPasswordProtected = true;
    } else {
        privateKeyPem = formatPem('PRIVATE KEY', key.pkcs8Der);
    }

    zeroizeBytes(key.seed);
    if (key.secretKey) {
        zeroizeBytes(key.secretKey);
    }
    zeroizeBytes(key.pkcs8Der);

    return {
        username: username.trim().toLowerCase(),
        role: role,
        accessGate: accessGate,
        algorithm: algorithm,
        publicKeyBase64: publicKeyBase64,
        publicKeyPem: publicKeyPem,
        privateKeyPem: privateKeyPem,
        isPasswordProtected: isPasswordProtected
    };
}

const WeaverAuth = {
    parsePrivateKeyPem,
    extractKeyMaterialFromPkcs8,
    decryptKeyEnvelope,
    encryptKeyEnvelope,
    zeroizeBytes,
    signChallenge,
    requestChallenge,
    submitSignature,
    weaverSignIn,
    weaverBindKeyFileInput,
    uint8ArrayToBase64,
    formatPem,
    generateEd25519Keypair,
    generateMlDsa65Keypair,
    weaverGenerateKeyBundle
};

if (typeof window !== 'undefined') {
    window.parsePrivateKeyPem = parsePrivateKeyPem;
    window.extractKeyMaterialFromPkcs8 = extractKeyMaterialFromPkcs8;
    window.decryptKeyEnvelope = decryptKeyEnvelope;
    window.encryptKeyEnvelope = encryptKeyEnvelope;
    window.zeroizeBytes = zeroizeBytes;
    window.signChallenge = signChallenge;
    window.requestChallenge = requestChallenge;
    window.submitSignature = submitSignature;
    window.weaverSignIn = weaverSignIn;
    window.weaverBindKeyFileInput = weaverBindKeyFileInput;
    window.uint8ArrayToBase64 = uint8ArrayToBase64;
    window.formatPem = formatPem;
    window.generateEd25519Keypair = generateEd25519Keypair;
    window.generateMlDsa65Keypair = generateMlDsa65Keypair;
    window.weaverGenerateKeyBundle = weaverGenerateKeyBundle;
}

if (typeof module !== 'undefined') {
    module.exports = WeaverAuth;
}
