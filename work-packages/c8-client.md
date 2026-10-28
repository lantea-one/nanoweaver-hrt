# Work Package P15 (`c8-client`): C8 Browser-Side Challenge Signing (`weaver-auth.js`), Vendored Crypto & `Login.razor` Rewrite

- **Phase ID**: `P15` (implementing the client half of `P13` Architect design)
- **Recommended Model Tier**: `flash` / Haiku / Sonnet — **Split into Stage A (Vendoring + `weaver-auth.js` + JS Tests) and Stage B (`Login.razor` + `App.razor` + Gate Step 8)** so each stage is under 350 lines.
- **Parent Document**: [`work-packages/PHASES-v2.md`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/work-packages/PHASES-v2.md) & [`work-packages/c8-challenge-response-proposal.md`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/work-packages/c8-challenge-response-proposal.md)
- **Prerequisites**: `P14` (`work-packages/c8-server.md`)
- **Blocks**: `P16` (`work-packages/c8-remove-oracle.md`) and `P17` (`work-packages/p17-bunit-security-components.md`)

---

## 1. Recorded User Decisions & P13 Spike Layout (Authoritative)

1. **Algorithms Supported in Browser**: Both **Ed25519** and **ML-DSA-65** accounts can sign in through `/login` in the browser.
   - Vendor `@noble/ed25519` (with `@noble/hashes/sha512`) and `@noble/post-quantum` (`ml-dsa`) as self-hosted, zero-CDN files under `src/NanoWeaverHrt/wwwroot/lib/`.
2. **Passphrase-Protected PEM Envelope Decryption**:
   - Reproduce `UserAuthenticationService.EncryptPrivateKeyBytes` (`[Salt: 16][Nonce: 12][Tag: 16][Ciphertext: N]`, PBKDF2-SHA256 with `100,000` iterations, `32`-byte AES-256-GCM key, `salt` passed as `additionalData`).
   - Note that WebCrypto's `subtle.decrypt({ name: 'AES-GCM', iv: nonce, additionalData: salt, tagLength: 128 }, key, ciphertextWithTag)` expects the 16-byte GCM authentication tag **appended to the end of the ciphertext** (`[Ciphertext: N][Tag: 16]`), whereas our envelope stores `[Salt: 16][Nonce: 12][Tag: 16][Ciphertext: N]`. Therefore `decryptKeyEnvelope` slices:
     - `salt = envelope.subarray(0, 16)`
     - `nonce = envelope.subarray(16, 28)`
     - `tag = envelope.subarray(28, 44)`
     - `ciphertext = envelope.subarray(44)`
     and concatenates `[ciphertext, tag]` before calling the injectable `aesGcmDecrypt(keyBytes, nonce, ciphertextWithTag, salt)` adapter (which defaults to `globalThis.crypto.subtle`).
3. **Injected Signer & Crypto Adapter Architecture (Zero Reliance on `--test-coverage-exclude`)**:
   - `src/NanoWeaverHrt/wwwroot/js/weaver-auth.js` must **not** statically `import` the vendored files in `wwwroot/lib/`. Instead, `weaver-auth.js` reads `globalThis.WeaverCryptoVendor` (or accepts an explicit `cryptoAdapter` parameter in unit tests).
   - This keeps `wwwroot/lib/*` completely out of `node --test --experimental-test-coverage`'s import graph during `npm test`, guaranteeing 100.00% line, branch, and function coverage on `weaver-auth.js` on all Node versions without needing `--test-coverage-exclude`.
   - Gate Step 8 (`scripts/verify-vendored-js.sh`) independently verifies:
     1. SHA-256 and SHA-384 (SRI) hashes of every file in `src/NanoWeaverHrt/wwwroot/lib/` against `docs/vendored-javascript.md`.
     2. Real cryptographic execution of the vendored `@noble/ed25519` and `@noble/post-quantum` modules against RFC 8032 Ed25519 and BouncyCastle 2.7 ML-DSA-65 known-answer test vectors.
4. **Exact BouncyCastle 2.7 PKCS#8 `PrivateKeyInfo` DER Byte Layouts (Verified by P13 Spike)**:
   - **Ed25519 (`OID 1.3.101.112` = `06 03 2B 65 70`)**:
     - Emitted by BouncyCastle 2.7 (`len === 83`) or RFC 8410 v0 (`len === 48`).
     - In both forms, bytes `[7..12]` are `06 03 2B 65 70`, bytes `[12..16]` are `04 22 04 20`, and the **32-byte Ed25519 seed** is `der.subarray(16, 48)`.
   - **ML-DSA-65 (`OID 2.16.840.1.101.3.4.3.18` = `06 09 60 86 48 01 65 03 04 03 12`)**:
     - In all three BouncyCastle 2.7 forms, the 11-byte OID `06 09 60 86 48 01 65 03 04 03 12` appears starting at offset `9` (for `4098`-byte and `4060`-byte forms) or offset `7` (for `54`-byte form):
       1. **`SeedAndEncoding` (`der.length === 4098`, default from `GenerateSecureKeyBundle`)**:
          - Bytes `[9..20]` = `06 09 60 86 48 01 65 03 04 03 12`
          - Bytes `[26..28]` = `04 20`
          - **32-byte seed ($\xi$)**: `der.subarray(28, 60)`
          - **4032-byte expanded secret key ($sk$)**: `der.subarray(64, 4096)`
       2. **`SeedOnly` (`der.length === 54`)**:
          - Bytes `[7..18]` = `06 09 60 86 48 01 65 03 04 03 12`
          - Bytes `[20..22]` = `80 20`
          - **32-byte seed ($\xi$)**: `der.subarray(22, 54)`
       3. **`EncodingOnly` (`der.length === 4060`)**:
          - Bytes `[9..20]` = `06 09 60 86 48 01 65 03 04 03 12`
          - Bytes `[24..28]` = `04 82 0F C0`
          - **4032-byte expanded secret key ($sk$)**: `der.subarray(28, 4060)`

---

## 2. Files in Scope

| Stage | File | Action | Est. Lines |
| :--- | :--- | :--- | :--- |
| **Stage A** | `src/NanoWeaverHrt/wwwroot/lib/weaver-crypto-vendor.js` | Vendored ES module bundle exposing `window.WeaverCryptoVendor` (`ed25519`, `ml_dsa65`, WebCrypto PBKDF2 + AES-GCM) | +250 |
| **Stage A** | `src/NanoWeaverHrt/wwwroot/js/weaver-auth.js` | Pure PKCS#8 parser, envelope decryptor, challenge signer, and `window.weaverSignIn` / `window.weaverBindKeyFileInput` | +260 |
| **Stage A** | `tests/weaver-auth.test.mjs` | 100% line/branch/function coverage unit tests for `weaver-auth.js` | +310 |
| **Stage A** | `package.json` | Add `tests/weaver-auth.test.mjs` to `npm test` | +2 |
| **Stage B** | `docs/vendored-javascript.md` | Vendored JS manifest with versions, SHA-256, SHA-384 SRI, and human review record | +60 |
| **Stage B** | `scripts/verify-vendored-js.sh` | Gate Step 8 script verifying SHA-256/SHA-384 hashes, no-CDN rule, and KAT vectors | +90 |
| **Stage B** | `scripts/verify-all.sh` | Add Step 8/8 invoking `bash scripts/verify-vendored-js.sh` | +12 |
| **Stage B** | `src/NanoWeaverHrt/Components/App.razor` | Load `lib/weaver-crypto-vendor.js` (with `integrity="sha384-..."`) and `js/weaver-auth.js` | +10 |
| **Stage B** | `src/NanoWeaverHrt/Components/Pages/Login.razor` | Remove `<InputFile>`, `@bind="_privateKeyPem"`, `@bind="_password"`, and `AuthService.AuthenticateWithPrivateKeyAsync`; call `window.weaverSignIn` | +90 / -80 |

---

## 3. Stage A: `weaver-auth.js`, Vendored Crypto Adapter & 100% JS Unit Tests

### Step A1: Create `src/NanoWeaverHrt/wwwroot/js/weaver-auth.js`
Implement `src/NanoWeaverHrt/wwwroot/js/weaver-auth.js` with 100% JSDoc comments and `WHAT / WHY / HOW` inline comments:

1. **`parsePrivateKeyPem(pemText)`**:
   - Validates `typeof pemText === 'string'` and `pemText.trim().length > 0`; otherwise returns `{ ok: false, error: 'Username and private key are both required.' }`.
   - Checks whether the header contains `-----BEGIN ENCRYPTED PRIVATE KEY-----` (`isEncrypted = true`) or `-----BEGIN PRIVATE KEY-----` (`isEncrypted = false`). Any other header returns `{ ok: false, error: 'Failed to parse or decrypt private key. Check passphrase or format.' }`.
   - Strips `-----BEGIN ...-----` and `-----END ...-----` lines and whitespace, Base64-decodes into a `Uint8Array`. If Base64 decoding fails or produces `0` bytes, returns `{ ok: false, error: 'Failed to parse or decrypt private key. Check passphrase or format.' }`.
   - Returns `{ ok: true, isEncrypted, derBytes }`.

2. **`extractKeyMaterialFromPkcs8(derBytes)`**:
   - Inspects `derBytes` (`Uint8Array`) against the P13 BouncyCastle 2.7 PKCS#8 byte layouts:
     - **Ed25519**: `(derBytes.length === 48 || derBytes.length === 83)` and `derBytes[7] === 0x06 && derBytes[8] === 0x03 && derBytes[9] === 0x2B && derBytes[10] === 0x65 && derBytes[11] === 0x70 && derBytes[14] === 0x04 && derBytes[15] === 0x20`:
       - Returns `{ ok: true, algorithm: 'Ed25519', seed: derBytes.slice(16, 48), secretKey: null }`.
     - **ML-DSA-65 `SeedAndEncoding` (`derBytes.length === 4098`)**:
       - Checks OID byte `derBytes[19] === 0x12` and `derBytes[26] === 0x04 && derBytes[27] === 0x20`:
       - Returns `{ ok: true, algorithm: 'MlDsa65', seed: derBytes.slice(28, 60), secretKey: derBytes.slice(64, 4096) }`.
     - **ML-DSA-65 `SeedOnly` (`derBytes.length === 54`)**:
       - Checks OID byte `derBytes[17] === 0x12` and `derBytes[20] === 0x80 && derBytes[21] === 0x20`:
       - Returns `{ ok: true, algorithm: 'MlDsa65', seed: derBytes.slice(22, 54), secretKey: null }`.
     - **ML-DSA-65 `EncodingOnly` (`derBytes.length === 4060`)**:
       - Checks OID byte `derBytes[19] === 0x12` and `derBytes[24] === 0x04 && derBytes[25] === 0x82`:
       - Returns `{ ok: true, algorithm: 'MlDsa65', seed: null, secretKey: derBytes.slice(28, 4060) }`.
     - Otherwise returns `{ ok: false, error: 'Failed to parse or decrypt private key. Check passphrase or format.' }`.

3. **`decryptKeyEnvelope(envelopeBytes, passphrase, cryptoAdapter)`**:
   - Validates `envelopeBytes instanceof Uint8Array && envelopeBytes.length > 44` and `typeof passphrase === 'string' && passphrase.length > 0`; otherwise returns `{ ok: false, error: 'Failed to parse or decrypt private key. Check passphrase or format.' }`.
   - Slices:
     - `salt = envelopeBytes.subarray(0, 16)`
     - `nonce = envelopeBytes.subarray(16, 28)`
     - `tag = envelopeBytes.subarray(28, 44)`
     - `ciphertext = envelopeBytes.subarray(44)`
   - Builds `ciphertextWithTag = new Uint8Array(ciphertext.length + 16)` with `ciphertext` at `0` and `tag` at `ciphertext.length`.
   - Calls `await adapter.decryptAesGcmPbkdf2(passphrase, salt, nonce, ciphertextWithTag)` inside `try/catch`.
   - On success returns `{ ok: true, derBytes: plaintextBytes }`; on error returns `{ ok: false, error: 'Failed to parse or decrypt private key. Check passphrase or format.' }`.

4. **`zeroizeBytes(buffer)`**:
   - Best-effort zero-fill (`if (buffer instanceof Uint8Array) buffer.fill(0);`).

5. **`signChallenge(keyMaterial, nonceBase64, cryptoAdapter)`**:
   - Decodes `nonceBase64` to `nonceBytes` (`Uint8Array`).
   - If `keyMaterial.algorithm === 'Ed25519'`, calls `await adapter.signEd25519(keyMaterial.seed, nonceBytes)`.
   - Else (`MlDsa65`), calls `await adapter.signMlDsa65(keyMaterial, nonceBytes)`.
   - Base64-encodes the resulting signature `Uint8Array` and returns `{ ok: true, signatureBase64 }`.

6. **`requestChallenge(username, fetchImpl)`** and **`submitSignature(username, challengeId, signatureBase64, fetchImpl)`**:
   - `requestChallenge` POSTs `JSON.stringify({ username })` to `/api/auth/challenge`. On non-2xx, parses RFC 7807 `detail` (falling back to `'Failed to request authentication challenge.'`) and returns `{ ok: false, error }`. On 200, returns `{ ok: true, challengeId: body.challengeId, nonceBase64: body.nonceBase64 }`.
   - `submitSignature` POSTs `JSON.stringify({ username, challengeId, signatureBase64 })` to `/api/auth/web-token` with `credentials: 'same-origin'`. On non-2xx, parses RFC 7807 `detail` (falling back to `'Authentication failed.'`) and returns `{ ok: false, error }`. On 200, returns `{ ok: true, token: body.token, user: body.user }`.

7. **`weaverSignIn(options)`**:
   - Reads DOM elements by ID (`loginUser`, `privKeyPem`, `loginPassword`, `loginSubmitBtn`) from `document` (or injected `domAdapter` in tests).
   - **Loud Missing-Element Guard (P15 Trap 1)**: If any of `#loginUser`, `#privKeyPem`, or `#loginPassword` is missing from the DOM, immediately returns `{ ok: false, error: 'Sign-in form element missing from DOM.' }`.
   - Disables `#loginSubmitBtn` while in-flight (and restores it in `finally`) so users do not double-click during the 100,000-iteration PBKDF2 derivation (**P15 Trap 2**).
   - Parses PEM, decrypts if encrypted, extracts key material, requests challenge, signs nonce, zeroes `derBytes` / `seed` / `secretKey`, clears `#privKeyPem` and `#loginPassword` DOM `.value`s, submits signature to `/api/auth/web-token`, calls `window.weaverSetSession(token)` if present, and returns `{ ok: true, user, token }`.

8. **`weaverBindKeyFileInput(fileInputId, textareaId)`**:
   - Attaches a client-side `change` listener to `<input type="file" id="keyFileUpload">` that enforces the `65536`-byte cap (`file.size > 65536` -> error) and populates `#privKeyPem.value` via `file.text()` purely in browser memory without ever touching the Blazor SignalR circuit.

### Step A2: Vendor `@noble/ed25519`, `@noble/hashes/sha512`, and `@noble/post-quantum` (`ml-dsa`)
Place the self-contained vendored ES module bundle under `src/NanoWeaverHrt/wwwroot/lib/` and wire `window.WeaverCryptoVendor = { signEd25519, signMlDsa65, decryptAesGcmPbkdf2 }`.

### Step A3: Update `package.json` and Write `tests/weaver-auth.test.mjs`
Update `package.json`:
```json
"test": "node --test --experimental-test-coverage --test-coverage-branches=100 --test-coverage-lines=100 --test-coverage-functions=100 tests/weaver-visualizer.test.mjs tests/weaver-session.test.mjs tests/weaver-auth.test.mjs"
```
Write `tests/weaver-auth.test.mjs` covering 100% of lines, branches, and functions in `weaver-auth.js`, including:
- RFC 8032 Ed25519 PKCS#8 (both 48-byte and 83-byte forms)
- ML-DSA-65 PKCS#8 (`4098`-byte `SeedAndEncoding`, `54`-byte `SeedOnly`, and `4060`-byte `EncodingOnly` forms)
- Encrypted private key envelope decryption (`[salt 16][nonce 12][tag 16][ciphertext]`, wrong passphrase, truncated envelope)
- Missing DOM element guard (`Sign-in form element missing from DOM.`)
- Button disable/enable lifecycle during `weaverSignIn`
- `weaverBindKeyFileInput` 64 KB file size cap and `file.text()` population
- Best-effort `zeroizeBytes` verification (`seed.every(b => b === 0)`)

---

## 4. Stage B: `Login.razor` Rewrite, `App.razor`, Vendoring Manifest & Gate Step 8

### Step B1: Create `docs/vendored-javascript.md` and `scripts/verify-vendored-js.sh`
1. Record the exact file path, upstream package/version, license (`MIT`), SHA-256 hash, SHA-384 SRI digest, and human review record in `docs/vendored-javascript.md`.
2. Create `scripts/verify-vendored-js.sh` and wire it as **Step 8** in `scripts/verify-all.sh`:
   - Recomputes SHA-256 and SHA-384 for every `.js`/`.mjs` file under `src/NanoWeaverHrt/wwwroot/lib/` and compares against `docs/vendored-javascript.md`.
   - Verifies that `src/NanoWeaverHrt/Components/App.razor` carries the matching `integrity="sha384-..."` attribute and contains no external CDN `<script>` tags.
   - Executes a Node verification check importing the vendored crypto module and verifying both an RFC 8032 Ed25519 vector and a BouncyCastle 2.7 ML-DSA-65 signature vector.

### Step B2: Update `src/NanoWeaverHrt/Components/App.razor`
Include the vendored crypto script (with `integrity="sha384-..."`) and `<script src="js/weaver-auth.js"></script>` after `js/weaver-session.js`.

### Step B3: Rewrite `src/NanoWeaverHrt/Components/Pages/Login.razor`
In [`src/NanoWeaverHrt/Components/Pages/Login.razor`](file:///home/fabigail/Documents/project.d/nanoweaver-hrt/src/NanoWeaverHrt/Components/Pages/Login.razor):
1. **Remove** `@inject IUserAuthenticationService AuthService`.
2. **Replace `<InputFile>`** with a plain HTML `<input id="keyFileUpload" type="file" accept=".pem,.key,.txt" style="display:none;" />` bound on `OnAfterRenderAsync(firstRender)` via `await JS.InvokeVoidAsync("weaverBindKeyFileInput", "keyFileUpload", "privKeyPem")`.
3. **Remove `@bind="_username"`, `@bind="_privateKeyPem"`, and `@bind="_password"`** from `#loginUser`, `#privKeyPem`, and `#loginPassword` so keystrokes and pasted PEM text are **never** sent across the Blazor SignalR circuit to the server.
4. Add `id="loginSubmitBtn"` to the sign-in button.
5. In `Authenticate()`, invoke `window.weaverSignIn` via `IJSRuntime`:
   ```csharp
   WeaverSignInResult result = await JS.InvokeAsync<WeaverSignInResult>("weaverSignIn");
   if (!result.Ok || result.User == null)
   {
       _errorMessage = result.Error ?? "Authentication failed.";
       return;
   }

   SessionState.SetUser(result.User);
   Navigation.NavigateTo("/");
   ```
6. Define the private `WeaverSignInResult` record inside `Login.razor` (with complete XML doc comments) holding `bool Ok`, `string? Error`, `string? Token`, and `UserAccount? User`.

---

## 5. Verification Gate Checklist

1. Run `npm test` and confirm 100.00% line, branch, and function coverage across `weaver-visualizer.js`, `weaver-session.js`, and `weaver-auth.js`.
2. Mutate 1 byte in `src/NanoWeaverHrt/wwwroot/lib/weaver-crypto-vendor.js`, run `bash scripts/verify-vendored-js.sh`, confirm it fails loudly, and revert the byte.
3. Run the full verification gate:
   ```bash
   ./scripts/verify-all.sh
   ```
   Confirm all steps pass.
