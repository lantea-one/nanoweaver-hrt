/**
 * ============================================================================
 * Weaver-Auth Test Suite: 100% Line, Branch & Function Coverage Verification
 * ----------------------------------------------------------------------------
 * WHAT: Unit tests for client-side challenge-response authentication (Audit C8):
 *       parsePrivateKeyPem, extractKeyMaterialFromPkcs8, decryptKeyEnvelope,
 *       zeroizeBytes, signChallenge, requestChallenge, submitSignature,
 *       weaverSignIn, and weaverBindKeyFileInput.
 * WHY:  Prime Directive 5 mandates 100.00% line, branch, and function coverage
 *       for all client-side JavaScript.
 * HOW:  Node.js test runner with isolated mock DOM and injected crypto/fetch adapters.
 * ============================================================================
 */

import test from 'node:test';
import assert from 'node:assert/strict';

// Set up mock window and document globals before loading module
globalThis.window = {};

const WeaverAuth = (await import('../src/NanoWeaverHrt/wwwroot/js/weaver-auth.js')).default;
const {
    parsePrivateKeyPem,
    extractKeyMaterialFromPkcs8,
    decryptKeyEnvelope,
    zeroizeBytes,
    signChallenge,
    requestChallenge,
    submitSignature,
    weaverSignIn,
    weaverBindKeyFileInput,
    uint8ArrayToBase64,
    formatPem,
    encryptKeyEnvelope,
    generateEd25519Keypair,
    generateMlDsa65Keypair,
    weaverGenerateKeyBundle
} = WeaverAuth;

/**
 * Creates a valid 48-byte Ed25519 PKCS#8 DER buffer.
 * @returns {Uint8Array}
 */
function createEd25519Der48() {
    const der = new Uint8Array(48);
    der[7] = 0x06;
    der[8] = 0x03;
    der[9] = 0x2b;
    der[10] = 0x65;
    der[11] = 0x70;
    der[14] = 0x04;
    der[15] = 0x20;
    der.subarray(16, 48).fill(0xaa);
    return der;
}

/**
 * Creates a valid 83-byte Ed25519 PKCS#8 DER buffer.
 * @returns {Uint8Array}
 */
function createEd25519Der83() {
    const der = new Uint8Array(83);
    der[7] = 0x06;
    der[8] = 0x03;
    der[9] = 0x2b;
    der[10] = 0x65;
    der[11] = 0x70;
    der[14] = 0x04;
    der[15] = 0x20;
    der.subarray(16, 48).fill(0xbb);
    return der;
}

/**
 * Converts bytes to Base64 string.
 * @param {Uint8Array} bytes
 * @returns {string}
 */
function toBase64(bytes) {
    let bin = '';
    for (let i = 0; i < bytes.length; i++) {
        bin += String.fromCharCode(bytes[i]);
    }
    return btoa(bin);
}

test('Module publishes all helpers as globals and named exports', () => {
    assert.equal(typeof window.parsePrivateKeyPem, 'function');
    assert.equal(typeof window.extractKeyMaterialFromPkcs8, 'function');
    assert.equal(typeof window.decryptKeyEnvelope, 'function');
    assert.equal(typeof window.zeroizeBytes, 'function');
    assert.equal(typeof window.signChallenge, 'function');
    assert.equal(typeof window.requestChallenge, 'function');
    assert.equal(typeof window.submitSignature, 'function');
    assert.equal(typeof window.weaverSignIn, 'function');
    assert.equal(typeof window.weaverBindKeyFileInput, 'function');
});

test('parsePrivateKeyPem: validates inputs, headers, and Base64 content', () => {
    // Non-string or empty inputs
    assert.equal(parsePrivateKeyPem(null).ok, false);
    assert.equal(parsePrivateKeyPem(123).ok, false);
    assert.equal(parsePrivateKeyPem('').ok, false);
    assert.equal(parsePrivateKeyPem('   ').ok, false);

    // Invalid header
    assert.equal(parsePrivateKeyPem('-----BEGIN CERTIFICATE-----\nabc\n-----END CERTIFICATE-----').ok, false);

    // Header without content
    assert.equal(parsePrivateKeyPem('-----BEGIN PRIVATE KEY-----\n-----END PRIVATE KEY-----').ok, false);

    // Corrupted Base64
    assert.equal(parsePrivateKeyPem('-----BEGIN PRIVATE KEY-----\n!!!invalid_base64!!!\n-----END PRIVATE KEY-----').ok, false);

    // Valid unencrypted PEM
    const der = createEd25519Der48();
    const pem = `-----BEGIN PRIVATE KEY-----\n${toBase64(der)}\n-----END PRIVATE KEY-----`;
    const res = parsePrivateKeyPem(pem);
    assert.equal(res.ok, true);
    assert.equal(res.isEncrypted, false);
    assert.deepEqual(res.derBytes, der);

    // Valid encrypted PEM
    const encPem = `-----BEGIN ENCRYPTED PRIVATE KEY-----\n${toBase64(der)}\n-----END ENCRYPTED PRIVATE KEY-----`;
    const encRes = parsePrivateKeyPem(encPem);
    assert.equal(encRes.ok, true);
    assert.equal(encRes.isEncrypted, true);
    assert.deepEqual(encRes.derBytes, der);
});

test('extractKeyMaterialFromPkcs8: parses Ed25519 layouts (48 and 83 bytes)', () => {
    // Non-Uint8Array
    assert.equal(extractKeyMaterialFromPkcs8(null).ok, false);
    assert.equal(extractKeyMaterialFromPkcs8([1, 2, 3]).ok, false);

    // Valid 48-byte Ed25519
    const der48 = createEd25519Der48();
    const res48 = extractKeyMaterialFromPkcs8(der48);
    assert.equal(res48.ok, true);
    assert.equal(res48.algorithm, 'Ed25519');
    assert.equal(res48.seed.length, 32);
    assert.equal(res48.secretKey, null);

    // Valid 83-byte Ed25519
    const der83 = createEd25519Der83();
    const res83 = extractKeyMaterialFromPkcs8(der83);
    assert.equal(res83.ok, true);
    assert.equal(res83.algorithm, 'Ed25519');
    assert.equal(res83.seed.length, 32);
    assert.equal(res83.secretKey, null);

    // Corrupted Ed25519 marker
    const bad48 = createEd25519Der48();
    bad48[7] = 0x00;
    assert.equal(extractKeyMaterialFromPkcs8(bad48).ok, false);

    const bad83 = createEd25519Der83();
    bad83[14] = 0x00;
    assert.equal(extractKeyMaterialFromPkcs8(bad83).ok, false);
});

test('extractKeyMaterialFromPkcs8: parses ML-DSA-65 layouts (4098, 54, and 4060 bytes)', () => {
    // 1. SeedAndEncoding (4098 bytes - compact layout)
    const der4098 = new Uint8Array(4098);
    der4098[19] = 0x12;
    der4098[26] = 0x04;
    der4098[27] = 0x20;
    der4098.subarray(28, 60).fill(0x11);
    der4098.subarray(64, 4096).fill(0x22);

    const res4098 = extractKeyMaterialFromPkcs8(der4098);
    assert.equal(res4098.ok, true);
    assert.equal(res4098.algorithm, 'MlDsa65');
    assert.equal(res4098.seed.length, 32);
    assert.equal(res4098.secretKey.length, 4032);

    // 1b. SeedAndEncoding (4098 bytes - BouncyCastle sequence layout)
    const der4098Bc = new Uint8Array(4098);
    der4098Bc[19] = 0x12;
    der4098Bc[28] = 0x04;
    der4098Bc[29] = 0x20;
    der4098Bc.subarray(30, 62).fill(0x33);
    der4098Bc.subarray(66, 4098).fill(0x44);

    const res4098Bc = extractKeyMaterialFromPkcs8(der4098Bc);
    assert.equal(res4098Bc.ok, true);
    assert.equal(res4098Bc.algorithm, 'MlDsa65');
    assert.equal(res4098Bc.seed.length, 32);
    assert.equal(res4098Bc.secretKey.length, 4032);

    // Corrupted 4098
    const bad4098 = new Uint8Array(der4098);
    bad4098[19] = 0x00;
    assert.equal(extractKeyMaterialFromPkcs8(bad4098).ok, false);

    // 2. SeedOnly (54 bytes)
    const der54 = new Uint8Array(54);
    der54[17] = 0x12;
    der54[20] = 0x80;
    der54[21] = 0x20;
    der54.subarray(22, 54).fill(0x33);

    const res54 = extractKeyMaterialFromPkcs8(der54);
    assert.equal(res54.ok, true);
    assert.equal(res54.algorithm, 'MlDsa65');
    assert.equal(res54.seed.length, 32);
    assert.equal(res54.secretKey, null);

    // Corrupted 54
    const bad54 = new Uint8Array(der54);
    bad54[20] = 0x00;
    assert.equal(extractKeyMaterialFromPkcs8(bad54).ok, false);

    // 3. EncodingOnly (4060 bytes)
    const der4060 = new Uint8Array(4060);
    der4060[19] = 0x12;
    der4060[24] = 0x04;
    der4060[25] = 0x82;
    der4060.subarray(28, 4060).fill(0x44);

    const res4060 = extractKeyMaterialFromPkcs8(der4060);
    assert.equal(res4060.ok, true);
    assert.equal(res4060.algorithm, 'MlDsa65');
    assert.equal(res4060.seed, null);
    assert.equal(res4060.secretKey.length, 4032);

    // Corrupted 4060
    const bad4060 = new Uint8Array(der4060);
    bad4060[24] = 0x00;
    assert.equal(extractKeyMaterialFromPkcs8(bad4060).ok, false);

    // Unrecognized length
    assert.equal(extractKeyMaterialFromPkcs8(new Uint8Array(100)).ok, false);
});

test('decryptKeyEnvelope: decrypts envelope and validates length and errors', async () => {
    // Non-buffer or truncated buffer (< 45 bytes)
    assert.equal((await decryptKeyEnvelope(null, 'pass')).ok, false);
    assert.equal((await decryptKeyEnvelope(new Uint8Array(40), 'pass')).ok, false);
    assert.equal((await decryptKeyEnvelope(new Uint8Array(50), '')).ok, false);
    assert.equal((await decryptKeyEnvelope(new Uint8Array(50), 123)).ok, false);

    // Missing adapter
    const envelope = new Uint8Array(100);
    assert.equal((await decryptKeyEnvelope(envelope, 'pass', null)).ok, false);
    assert.equal((await decryptKeyEnvelope(envelope, 'pass', {})).ok, false);

    // Successful decryption
    const expectedPlain = createEd25519Der48();
    const mockAdapter = {
        decryptAesGcmPbkdf2: async (pass, salt, nonce, ciphertextWithTag) => {
            assert.equal(pass, 'secret123');
            assert.equal(salt.length, 16);
            assert.equal(nonce.length, 12);
            assert.equal(ciphertextWithTag.length, 100 - 28);
            return expectedPlain;
        }
    };

    const res = await decryptKeyEnvelope(envelope, 'secret123', mockAdapter);
    assert.equal(res.ok, true);
    assert.deepEqual(res.derBytes, expectedPlain);

    // Throwing adapter
    const throwingAdapter = {
        decryptAesGcmPbkdf2: async () => {
            throw new Error('decryption error');
        }
    };
    const failRes = await decryptKeyEnvelope(envelope, 'wrongpass', throwingAdapter);
    assert.equal(failRes.ok, false);
    assert.equal(failRes.error, 'Failed to parse or decrypt private key. Check passphrase or format.');
});

test('zeroizeBytes: fills Uint8Array with zeros and handles non-arrays cleanly', () => {
    zeroizeBytes(null);
    zeroizeBytes(undefined);
    zeroizeBytes('not a buffer');

    const buf = new Uint8Array([1, 2, 3, 4, 5]);
    zeroizeBytes(buf);
    assert.deepEqual(buf, new Uint8Array(5));
});

test('signChallenge: delegates to adapter for Ed25519 and ML-DSA-65', async () => {
    // Missing adapter
    assert.equal((await signChallenge({ algorithm: 'Ed25519' }, 'bm9uY2U=', null)).ok, false);

    const nonceBase64 = toBase64(new Uint8Array([10, 20, 30]));
    const mockAdapter = {
        signEd25519: async (seed, nonceBytes) => {
            assert.equal(seed[0], 0xaa);
            assert.equal(nonceBytes[0], 10);
            return new Uint8Array([1, 2, 3, 4]);
        },
        signMlDsa65: async (keyMaterial, nonceBytes) => {
            assert.equal(keyMaterial.algorithm, 'MlDsa65');
            assert.equal(nonceBytes[0], 10);
            return new Uint8Array([5, 6, 7, 8]);
        }
    };

    // Ed25519 signing
    const edRes = await signChallenge({ algorithm: 'Ed25519', seed: new Uint8Array([0xaa]) }, nonceBase64, mockAdapter);
    assert.equal(edRes.ok, true);
    assert.equal(edRes.signatureBase64, toBase64(new Uint8Array([1, 2, 3, 4])));

    // ML-DSA-65 signing
    const mlRes = await signChallenge({ algorithm: 'MlDsa65' }, nonceBase64, mockAdapter);
    assert.equal(mlRes.ok, true);
    assert.equal(mlRes.signatureBase64, toBase64(new Uint8Array([5, 6, 7, 8])));
});

test('requestChallenge and submitSignature: handle 200 OK and error responses', async () => {
    // requestChallenge 200 OK
    const fetch200 = async () => ({
        ok: true,
        json: async () => ({ challengeId: 'c1', nonceBase64: 'bm9uY2U=' })
    });
    const cRes = await requestChallenge('alice', fetch200);
    assert.equal(cRes.ok, true);
    assert.equal(cRes.challengeId, 'c1');
    assert.equal(cRes.nonceBase64, 'bm9uY2U=');

    // requestChallenge RFC 7807 problem details error
    const fetchProb = async () => ({
        ok: false,
        json: async () => ({ detail: 'Custom challenge error' })
    });
    const cErr = await requestChallenge('alice', fetchProb);
    assert.equal(cErr.ok, false);
    assert.equal(cErr.error, 'Custom challenge error');

    // requestChallenge non-JSON error fallback
    const fetchBadJson = async () => ({
        ok: false,
        json: async () => { throw new Error('parse error'); }
    });
    const cFallback = await requestChallenge('alice', fetchBadJson);
    assert.equal(cFallback.ok, false);
    assert.equal(cFallback.error, 'Failed to request authentication challenge.');

    // submitSignature 200 OK
    const fetchSubmit200 = async () => ({
        ok: true,
        json: async () => ({ token: 'mock-token', user: { username: 'alice' } })
    });
    const sRes = await submitSignature('alice', 'c1', 'sig', fetchSubmit200);
    assert.equal(sRes.ok, true);
    assert.equal(sRes.token, 'mock-token');
    assert.equal(sRes.user.username, 'alice');

    // submitSignature RFC 7807 error
    const fetchSubmitProb = async () => ({
        ok: false,
        json: async () => ({ detail: 'Bad signature' })
    });
    const sErr = await submitSignature('alice', 'c1', 'sig', fetchSubmitProb);
    assert.equal(sErr.ok, false);
    assert.equal(sErr.error, 'Bad signature');

    // submitSignature fallback error
    const sFallback = await submitSignature('alice', 'c1', 'sig', fetchBadJson);
    assert.equal(sFallback.ok, false);
    assert.equal(sFallback.error, 'Authentication failed.');

    // Test requestChallenge and submitSignature using default globalThis.fetch
    const oldFetch = globalThis.fetch;
    try {
        globalThis.fetch = async () => ({
            ok: true,
            json: async () => ({ challengeId: 'cfetch', nonceBase64: 'bm9uY2U=', token: 'tfetch', user: {} })
        });
        assert.equal((await requestChallenge('alice')).ok, true);
        assert.equal((await submitSignature('alice', 'c', 's')).ok, true);
    } finally {
        globalThis.fetch = oldFetch;
    }
});

test('weaverSignIn: missing DOM element guard rejects before doing work', async () => {
    // Missing document or getElementById
    assert.equal((await weaverSignIn()).ok, false);
    assert.equal((await weaverSignIn({ domAdapter: {} })).ok, false);

    // Test missing each of the required DOM elements
    for (const missing of ['loginUser', 'privKeyPem', 'loginPassword']) {
        const dom = {
            getElementById: (id) => (id === missing ? null : { value: '' })
        };
        const res = await weaverSignIn({ domAdapter: dom });
        assert.equal(res.ok, false);
        assert.equal(res.error, 'Sign-in form element missing from DOM.');
    }
});

test('weaverSignIn: validates username, PEM parsing, and button disabled state', async () => {
    let btnDisabled = false;
    const dom = {
        getElementById: (id) => {
            if (id === 'loginSubmitBtn') return { set disabled(v) { btnDisabled = v; } };
            if (id === 'loginUser') return { value: '   ' };
            if (id === 'privKeyPem') return { value: 'some-key' };
            if (id === 'loginPassword') return { value: '' };
            return null;
        }
    };

    // Empty username
    const emptyUserRes = await weaverSignIn({ domAdapter: dom });
    assert.equal(emptyUserRes.ok, false);
    assert.equal(emptyUserRes.error, 'Username and private key are both required.');
    assert.equal(btnDisabled, false);

    // Empty PEM (exercises keyEl.value || '' branch)
    dom.getElementById = (id) => {
        if (id === 'loginSubmitBtn') return { set disabled(v) { btnDisabled = v; } };
        if (id === 'loginUser') return { value: 'alice' };
        if (id === 'privKeyPem') return { value: '' };
        if (id === 'loginPassword') return { value: '' };
        return null;
    };
    const emptyPemRes = await weaverSignIn({ domAdapter: dom });
    assert.equal(emptyPemRes.ok, false);
    assert.equal(emptyPemRes.error, 'Username and private key are both required.');
    assert.equal(btnDisabled, false);

    // Bad PEM
    dom.getElementById = (id) => {
        if (id === 'loginSubmitBtn') return { set disabled(v) { btnDisabled = v; } };
        if (id === 'loginUser') return { value: 'alice' };
        if (id === 'privKeyPem') return { value: 'bad pem' };
        if (id === 'loginPassword') return { value: '' };
        return null;
    };
    const badPemRes = await weaverSignIn({ domAdapter: dom });
    assert.equal(badPemRes.ok, false);
    assert.equal(badPemRes.error, 'Failed to parse or decrypt private key. Check passphrase or format.');
    assert.equal(btnDisabled, false);
});

test('weaverSignIn: executes complete Ed25519 and ML-DSA-65 unencrypted sign-in', async () => {
    const der = createEd25519Der48();
    const pem = `-----BEGIN PRIVATE KEY-----\n${toBase64(der)}\n-----END PRIVATE KEY-----`;

    let privValue = pem;
    let passValue = 'dontcare';
    let sessionToken = null;
    let btnDisabled = false;

    const dom = {
        getElementById: (id) => {
            if (id === 'loginSubmitBtn') return { set disabled(v) { btnDisabled = v; } };
            if (id === 'loginUser') return { value: 'alice' };
            if (id === 'privKeyPem') return { get value() { return privValue; }, set value(v) { privValue = v; } };
            if (id === 'loginPassword') return { get value() { return passValue; }, set value(v) { passValue = v; } };
            return null;
        }
    };

    const mockAdapter = {
        signEd25519: async () => new Uint8Array([1, 2, 3, 4])
    };

    const mockFetch = async (url) => {
        if (url === '/api/auth/challenge') {
            return {
                ok: true,
                json: async () => ({ challengeId: 'c123', nonceBase64: toBase64(new Uint8Array(32)) })
            };
        }
        if (url === '/api/auth/web-token') {
            return {
                ok: true,
                json: async () => ({ token: 'auth-token-123', user: { username: 'alice' } })
            };
        }
        throw new Error('unexpected url: ' + url);
    };

    const mockSession = {
        weaverSetSession: (t) => { sessionToken = t; }
    };

    const res = await weaverSignIn({
        domAdapter: dom,
        cryptoAdapter: mockAdapter,
        fetchImpl: mockFetch,
        sessionAdapter: mockSession
    });

    assert.equal(res.ok, true);
    assert.equal(res.token, 'auth-token-123');
    assert.equal(res.user.username, 'alice');
    assert.equal(sessionToken, 'auth-token-123');
    assert.equal(privValue, '');
    assert.equal(passValue, '');
    assert.equal(btnDisabled, false);

    // Also test ML-DSA-65 EncodingOnly (seed is null, secretKey is 4032 bytes)
    const der4060 = new Uint8Array(4060);
    der4060[19] = 0x12;
    der4060[24] = 0x04;
    der4060[25] = 0x82;
    der4060.subarray(28, 4060).fill(0x44);

    privValue = `-----BEGIN PRIVATE KEY-----\n${toBase64(der4060)}\n-----END PRIVATE KEY-----`;
    const mlAdapter = {
        signMlDsa65: async () => new Uint8Array([5, 6, 7, 8])
    };
    const mlRes = await weaverSignIn({
        domAdapter: dom,
        cryptoAdapter: mlAdapter,
        fetchImpl: mockFetch,
        sessionAdapter: mockSession
    });
    assert.equal(mlRes.ok, true);
    assert.equal(mlRes.token, 'auth-token-123');
});

test('weaverSignIn: decrypts encrypted PEM and handles failure branches', async () => {
    const der = createEd25519Der48();
    const envelope = new Uint8Array(60);
    const encPem = `-----BEGIN ENCRYPTED PRIVATE KEY-----\n${toBase64(envelope)}\n-----END ENCRYPTED PRIVATE KEY-----`;

    const makeDom = (pemText, password = 'secret') => ({
        getElementById: (id) => {
            if (id === 'loginUser') return { value: 'bob' };
            if (id === 'privKeyPem') return { value: pemText };
            if (id === 'loginPassword') return { value: password };
            return null;
        }
    });

    // 1. Decryption failure
    const failingDecryptAdapter = {
        decryptAesGcmPbkdf2: async () => { throw new Error('bad password'); }
    };
    const decFailRes = await weaverSignIn({
        domAdapter: makeDom(encPem),
        cryptoAdapter: failingDecryptAdapter
    });
    assert.equal(decFailRes.ok, false);

    // 2. Decryption succeeds but key material is invalid
    const badDerAdapter = {
        decryptAesGcmPbkdf2: async () => new Uint8Array(30)
    };
    const badDerRes = await weaverSignIn({
        domAdapter: makeDom(encPem),
        cryptoAdapter: badDerAdapter
    });
    assert.equal(badDerRes.ok, false);

    // 3. Challenge failure zeroizes key material (testing ML-DSA-65 with both seed and secretKey)
    const der4098 = new Uint8Array(4098);
    der4098[19] = 0x12;
    der4098[26] = 0x04;
    der4098[27] = 0x20;
    der4098.subarray(28, 60).fill(0x11);
    der4098.subarray(64, 4096).fill(0x22);

    const goodMlDsaAdapter = {
        decryptAesGcmPbkdf2: async () => new Uint8Array(der4098)
    };
    const failChalFetch = async () => ({
        ok: false,
        json: async () => ({ detail: 'Challenge rate limit' })
    });
    const chalFailRes = await weaverSignIn({
        domAdapter: makeDom(encPem),
        cryptoAdapter: goodMlDsaAdapter,
        fetchImpl: failChalFetch
    });
    assert.equal(chalFailRes.ok, false);
    assert.equal(chalFailRes.error, 'Challenge rate limit');

    // 3b. Signature failure zeroizes key material and returns error
    const unencryptedPem = `-----BEGIN PRIVATE KEY-----\n${toBase64(createEd25519Der48())}\n-----END PRIVATE KEY-----`;
    const goodChalFetch = async (url) => {
        if (url === '/api/auth/challenge') {
            return {
                ok: true,
                json: async () => ({ challengeId: 'c1', nonceBase64: toBase64(new Uint8Array(32)) })
            };
        }
        return { ok: true, json: async () => ({}) };
    };
    const failSigRes = await weaverSignIn({
        domAdapter: makeDom(unencryptedPem),
        cryptoAdapter: null, // missing adapter causes signChallenge to fail
        fetchImpl: goodChalFetch
    });
    assert.equal(failSigRes.ok, false);
    assert.equal(failSigRes.error, 'Cryptographic vendor module is unavailable.');

    // 4. Token submission failure
    const submitFailFetch = async (url) => {
        if (url === '/api/auth/challenge') {
            return {
                ok: true,
                json: async () => ({ challengeId: 'c1', nonceBase64: toBase64(new Uint8Array(32)) })
            };
        }
        return {
            ok: false,
            json: async () => ({ detail: 'Forbidden transport' })
        };
    };
    const signingAdapter = {
        decryptAesGcmPbkdf2: async () => createEd25519Der48(),
        signEd25519: async () => new Uint8Array([1, 2, 3])
    };
    const subFailRes = await weaverSignIn({
        domAdapter: makeDom(encPem),
        cryptoAdapter: signingAdapter,
        fetchImpl: submitFailFetch
    });
    assert.equal(subFailRes.ok, false);
    assert.equal(subFailRes.error, 'Forbidden transport');

    // 5. Successful login when sessionAdapter has no weaverSetSession
    const submitSuccessFetch = async (url) => {
        if (url === '/api/auth/challenge') {
            return {
                ok: true,
                json: async () => ({ challengeId: 'c1', nonceBase64: toBase64(new Uint8Array(32)) })
            };
        }
        return {
            ok: true,
            json: async () => ({ token: 'tok', user: { username: 'bob' } })
        };
    };
    const noSessionRes = await weaverSignIn({
        domAdapter: makeDom(encPem),
        cryptoAdapter: signingAdapter,
        fetchImpl: submitSuccessFetch,
        sessionAdapter: {}
    });
    assert.equal(noSessionRes.ok, true);
    assert.equal(noSessionRes.token, 'tok');
});

test('weaverBindKeyFileInput: attaches change listener, checks file size, and handles read errors', async () => {
    // Missing DOM elements
    assert.equal(weaverBindKeyFileInput('missingFile', 'missingText', { domAdapter: { getElementById: () => null } }), false);

    let attachedHandlerWithCallbacks = null;
    let textAreaValue = '';
    let sizeErrorCalled = false;
    let readErrorCalled = false;

    const mockFileInput1 = {
        addEventListener: (event, handler) => {
            if (event === 'change') attachedHandlerWithCallbacks = handler;
        }
    };
    const mockTextarea1 = {
        set value(v) { textAreaValue = v; }
    };
    const mockDom1 = {
        getElementById: (id) => {
            if (id === 'fileIn') return mockFileInput1;
            if (id === 'textOut') return mockTextarea1;
            return null;
        }
    };

    const bound = weaverBindKeyFileInput('fileIn', 'textOut', {
        domAdapter: mockDom1,
        onSizeError: () => { sizeErrorCalled = true; },
        onReadError: () => { readErrorCalled = true; }
    });
    assert.equal(bound, true);
    assert.equal(typeof attachedHandlerWithCallbacks, 'function');

    // 1. Change event with no file
    await attachedHandlerWithCallbacks({ target: {} });
    await attachedHandlerWithCallbacks({ target: { files: [] } });
    assert.equal(textAreaValue, '');

    // 2. Change event with file > 64 KB (with onSizeError)
    let clearedValue = 'initial';
    await attachedHandlerWithCallbacks({
        target: {
            files: [{ size: 70000 }],
            set value(v) { clearedValue = v; }
        }
    });
    assert.equal(sizeErrorCalled, true);
    assert.equal(clearedValue, '');

    // 3. Change event with valid file <= 64 KB
    await attachedHandlerWithCallbacks({
        target: {
            files: [{
                size: 100,
                text: async () => 'sample private key pem content'
            }]
        }
    });
    assert.equal(textAreaValue, 'sample private key pem content');

    // 4. File read error (with onReadError)
    await attachedHandlerWithCallbacks({
        target: {
            files: [{
                size: 100,
                text: async () => { throw new Error('disk read failed'); }
            }]
        }
    });
    assert.equal(readErrorCalled, true);

    // 5. Handlers without callbacks and without options
    let attachedHandlerNoCallbacks = null;
    const mockFileInput2 = {
        addEventListener: (event, handler) => {
            if (event === 'change') attachedHandlerNoCallbacks = handler;
        }
    };
    const mockDom2 = {
        getElementById: (id) => {
            if (id === 'fileIn2') return mockFileInput2;
            if (id === 'textOut2') return mockTextarea1;
            return null;
        }
    };

    weaverBindKeyFileInput('fileIn2', 'textOut2', { domAdapter: mockDom2 });
    assert.equal(typeof attachedHandlerNoCallbacks, 'function');

    // File > 64 KB without onSizeError
    await attachedHandlerNoCallbacks({
        target: {
            files: [{ size: 70000 }],
            set value(_) {}
        }
    });

    // File read error without onReadError
    await attachedHandlerNoCallbacks({
        target: {
            files: [{
                size: 100,
                text: async () => { throw new Error('disk read failed'); }
            }]
        }
    });

    // Test with default options (omitted options argument)
    assert.equal(weaverBindKeyFileInput('fileIn', 'textOut'), false);

    // Test with real global WeaverCryptoVendor default fallback
    const origVendor = globalThis.WeaverCryptoVendor;
    try {
        globalThis.WeaverCryptoVendor = {
            keygenEd25519: async () => ({
                seed: new Uint8Array(32),
                publicKey: new Uint8Array(32),
                spkiDer: new Uint8Array(44),
                pkcs8Der: new Uint8Array(48)
            }),
            keygenMlDsa65: async () => ({
                seed: new Uint8Array(32),
                publicKey: new Uint8Array(1952),
                secretKey: new Uint8Array(4032),
                spkiDer: new Uint8Array(1974),
                pkcs8Der: new Uint8Array(4098)
            }),
            encryptAesGcmPbkdf2: async (_p, _s, _n, pt) => {
                const out = new Uint8Array(pt.length + 16);
                out.set(pt, 0);
                return out;
            }
        };

        const realEd = await generateEd25519Keypair();
        assert.equal(realEd.spkiDer.length, 44);

        const realMl = await generateMlDsa65Keypair();
        assert.equal(realMl.spkiDer.length, 1974);

        const realBundle = await weaverGenerateKeyBundle('eve', 'Researcher', 'ApiWeb', 'Ed25519', null);
        assert.equal(realBundle.username, 'eve');

        const realEncRes = await encryptKeyEnvelope(realEd.pkcs8Der, 'real_pass');
        assert.equal(realEncRes.ok, true);
    } finally {
        globalThis.WeaverCryptoVendor = origVendor;
    }
});

test('uint8ArrayToBase64: encodes correctly with Buffer and chunked btoa fallback', () => {
    const data = new Uint8Array([0x48, 0x65, 0x6c, 0x6c, 0x6f]); // "Hello"
    const expected = 'SGVsbG8=';
    assert.equal(uint8ArrayToBase64(data), expected);

    // Test non-Uint8Array
    assert.equal(uint8ArrayToBase64('invalid'), '');
    assert.equal(uint8ArrayToBase64(null), '');

    // Test chunked fallback path with Buffer undefined
    const origBuffer = globalThis.Buffer;
    try {
        globalThis.Buffer = undefined;
        // Large buffer exceeding single chunk
        const large = new Uint8Array(9000);
        large.fill(0x41);
        const encoded = uint8ArrayToBase64(large);
        assert.equal(typeof encoded, 'string');
        assert.ok(encoded.length > 9000);
    } finally {
        globalThis.Buffer = origBuffer;
    }
});

test('formatPem: formats headers, footers and wraps at 64 characters', () => {
    const bytes = new Uint8Array(100);
    bytes.fill(0x42);
    const pem = formatPem('TEST KEY', bytes);
    assert.ok(pem.startsWith('-----BEGIN TEST KEY-----\n'));
    assert.ok(pem.endsWith('-----END TEST KEY-----\n'));
    const lines = pem.trim().split('\n');
    assert.equal(lines[1].length, 64);

    // Invalid arguments return empty string
    assert.equal(formatPem(123, bytes), '');
    assert.equal(formatPem('TEST', 'not-bytes'), '');
});

test('encryptKeyEnvelope: encrypts into standard envelope and round-trips with decryptKeyEnvelope', async () => {
    const plainDer = new Uint8Array([0x10, 0x20, 0x30, 0x40, 0x50]);
    const passphrase = 'test_secret_passphrase';

    // Mock crypto adapter
    const mockAdapter = {
        encryptAesGcmPbkdf2: async (_pass, _salt, _nonce, plaintext) => {
            // Emulate ciphertext + 16-byte tag
            const out = new Uint8Array(plaintext.length + 16);
            out.set(plaintext, 0);
            out.fill(0xee, plaintext.length);
            return out;
        },
        decryptAesGcmPbkdf2: async (_pass, _salt, _nonce, ciphertextWithTag) => {
            return ciphertextWithTag.slice(0, ciphertextWithTag.length - 16);
        }
    };

    const encRes = await encryptKeyEnvelope(plainDer, passphrase, mockAdapter);
    assert.equal(encRes.ok, true);
    assert.equal(encRes.envelopeBytes.length, 44 + plainDer.length);

    // Decrypt using existing decryptKeyEnvelope
    const decRes = await decryptKeyEnvelope(encRes.envelopeBytes, passphrase, mockAdapter);
    assert.equal(decRes.ok, true);
    assert.deepEqual(decRes.derBytes, plainDer);

    // Validation failures
    assert.equal((await encryptKeyEnvelope(null, passphrase, mockAdapter)).ok, false);
    assert.equal((await encryptKeyEnvelope(new Uint8Array(0), passphrase, mockAdapter)).ok, false);
    assert.equal((await encryptKeyEnvelope(plainDer, '', mockAdapter)).ok, false);
    assert.equal((await encryptKeyEnvelope(plainDer, 123, mockAdapter)).ok, false);

    // Missing adapter or missing method
    assert.equal((await encryptKeyEnvelope(plainDer, passphrase, {})).ok, false);

    // Adapter returns short/invalid ciphertext
    const badAdapter1 = {
        encryptAesGcmPbkdf2: async () => new Uint8Array(5)
    };
    assert.equal((await encryptKeyEnvelope(plainDer, passphrase, badAdapter1)).ok, false);

    // Adapter throws
    const badAdapter2 = {
        encryptAesGcmPbkdf2: async () => { throw new Error('subtle failure'); }
    };
    assert.equal((await encryptKeyEnvelope(plainDer, passphrase, badAdapter2)).ok, false);
});

test('generateEd25519Keypair and generateMlDsa65Keypair: delegate to adapter and validate error branches', async () => {
    const mockAdapter = {
        keygenEd25519: async (seed) => ({
            seed: seed || new Uint8Array(32),
            publicKey: new Uint8Array(32),
            spkiDer: new Uint8Array(44),
            pkcs8Der: new Uint8Array(48)
        }),
        keygenMlDsa65: async (seed) => ({
            seed: seed || new Uint8Array(32),
            publicKey: new Uint8Array(1952),
            secretKey: new Uint8Array(4032),
            spkiDer: new Uint8Array(1974),
            pkcs8Der: new Uint8Array(4098)
        })
    };

    const edKey = await generateEd25519Keypair({ cryptoAdapter: mockAdapter });
    assert.equal(edKey.spkiDer.length, 44);
    assert.equal(edKey.pkcs8Der.length, 48);

    const mlKey = await generateMlDsa65Keypair({ cryptoAdapter: mockAdapter });
    assert.equal(mlKey.spkiDer.length, 1974);
    assert.equal(mlKey.pkcs8Der.length, 4098);

    // Missing adapter error branches
    await assert.rejects(async () => {
        await generateEd25519Keypair({ cryptoAdapter: {} });
    }, /Ed25519 key generation provider is unavailable/);

    await assert.rejects(async () => {
        await generateMlDsa65Keypair({ cryptoAdapter: {} });
    }, /ML-DSA-65 key generation provider is unavailable/);
});

test('weaverGenerateKeyBundle: generates unencrypted and password-protected bundles for Ed25519 and ML-DSA-65', async () => {
    const mockAdapter = {
        keygenEd25519: async () => ({
            seed: new Uint8Array(32).fill(0x01),
            publicKey: new Uint8Array(32).fill(0x02),
            spkiDer: new Uint8Array(44).fill(0x03),
            pkcs8Der: new Uint8Array(48).fill(0x04)
        }),
        keygenMlDsa65: async () => ({
            seed: new Uint8Array(32).fill(0x05),
            publicKey: new Uint8Array(1952).fill(0x06),
            secretKey: new Uint8Array(4032).fill(0x07),
            spkiDer: new Uint8Array(1974).fill(0x08),
            pkcs8Der: new Uint8Array(4098).fill(0x09)
        }),
        encryptAesGcmPbkdf2: async (_pass, _salt, _nonce, plaintext) => {
            const out = new Uint8Array(plaintext.length + 16);
            out.set(plaintext, 0);
            out.fill(0xaa, plaintext.length);
            return out;
        }
    };

    // 1. Ed25519 Plain
    const edPlain = await weaverGenerateKeyBundle('Alice', 'Administrator', 'ApiWeb', 'Ed25519', null, { cryptoAdapter: mockAdapter });
    assert.equal(edPlain.username, 'alice');
    assert.equal(edPlain.algorithm, 'Ed25519');
    assert.equal(edPlain.isPasswordProtected, false);
    assert.ok(edPlain.publicKeyPem.includes('BEGIN PUBLIC KEY'));
    assert.ok(edPlain.privateKeyPem.includes('BEGIN PRIVATE KEY'));
    assert.ok(!edPlain.privateKeyPem.includes('BEGIN ENCRYPTED PRIVATE KEY'));

    // 2. Ed25519 Encrypted
    const edEnc = await weaverGenerateKeyBundle('Bob', 'Researcher', 'Web', 'Ed25519', 'secret123', { cryptoAdapter: mockAdapter });
    assert.equal(edEnc.username, 'bob');
    assert.equal(edEnc.isPasswordProtected, true);
    assert.ok(edEnc.privateKeyPem.includes('BEGIN ENCRYPTED PRIVATE KEY'));

    // 3. ML-DSA-65 Plain (using enum integer 1)
    const mlPlain = await weaverGenerateKeyBundle('Charlie', 'Patient', 'Api', 1, '', { cryptoAdapter: mockAdapter });
    assert.equal(mlPlain.username, 'charlie');
    assert.equal(mlPlain.isPasswordProtected, false);
    assert.ok(mlPlain.publicKeyPem.includes('BEGIN PUBLIC KEY'));
    assert.ok(mlPlain.privateKeyPem.includes('BEGIN PRIVATE KEY'));

    // 4. ML-DSA-65 Encrypted (using string 'MlDsa65')
    const mlEnc = await weaverGenerateKeyBundle('Dana', 'Researcher', 'ApiWeb', 'MlDsa65', 'pqcPass!456', { cryptoAdapter: mockAdapter });
    assert.equal(mlEnc.username, 'dana');
    assert.equal(mlEnc.isPasswordProtected, true);
    assert.ok(mlEnc.privateKeyPem.includes('BEGIN ENCRYPTED PRIVATE KEY'));

    // 5. Username validation errors
    await assert.rejects(async () => {
        await weaverGenerateKeyBundle('', 'Admin', 'Api', 'Ed25519', null, { cryptoAdapter: mockAdapter });
    }, /Username must not be empty/);

    await assert.rejects(async () => {
        await weaverGenerateKeyBundle(12345, 'Admin', 'Api', 'Ed25519', null, { cryptoAdapter: mockAdapter });
    }, /Username must not be empty/);

    // 6. Encrypt failure error branch
    const failingAdapter = {
        ...mockAdapter,
        encryptAesGcmPbkdf2: async () => new Uint8Array(5) // Too short
    };
    await assert.rejects(async () => {
        await weaverGenerateKeyBundle('Frank', 'Admin', 'Api', 'Ed25519', 'pass', { cryptoAdapter: failingAdapter });
    }, /Failed to encrypt private key/);
});
