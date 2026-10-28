# Work Package P20: In-Browser Client-Side Asymmetric Key Generation

- **Phase ID**: `P20`
- **Recommended Model Tier**: `flash` / Haiku / Sonnet
- **Parent Document**: [`work-packages/PHASES-v2.md`](PHASES-v2.md) & [`work-packages/README.md`](README.md)
- **Prerequisites**: `P19` verified and ratified.

---

## 1. Objective & Scope

With Audit C8 (`P14`–`P16`) fully implemented, interactive authentication is strictly asymmetric challenge-response (`POST /api/auth/challenge` + `POST /api/auth/web-token`), guaranteeing that private keys never cross the network during sign-in.

However, during initial system setup (`/setup`) and user creation (`/api/users/generate-keypair`), keypairs are currently minted by the server and returned to the browser for download. Phase **P20** eliminates server-side private key generation completely by moving keypair generation directly into browser memory:
1. **Client-Side Keypair Generation**:
   - **Ed25519**: Minted in browser memory via `@noble/ed25519.utils.randomPrivateKey()` or WebCrypto.
   - **ML-DSA-65**: Minted in browser memory via `@noble/post-quantum` (`ml_dsa65.keygen()`).
2. **Client-Side Envelope Sealing**:
   - Formatted into standard PKCS#8 PEM in browser memory.
   - If a passphrase is provided, encrypts the private key client-side using WebCrypto PBKDF2-SHA256 (100,000 iterations) + AES-256-GCM matching the node's standard envelope layout.
3. **Download & Public Key Registration**:
   - Triggers browser file download of the sealed private key file directly from memory (`URL.createObjectURL(blob)`).
   - Transmits **only** the `publicKeyBase64`, `username`, `role`, and `accessGate` to `/api/setup/initialize` or `POST /api/users`.
   - The private key never leaves the client device.
4. **Endpoint Deprecation & Cleanup**:
   - Delete `POST /api/setup/generate-keypair` and `POST /api/users/generate-keypair`.
   - Remove `GenerateSecureKeyBundle` and `EncryptPrivateKeyBytes` from `UserAuthenticationService.cs`.

---

## 2. Acceptance Criteria

1. In `/setup`, generating an Administrator account derives the keypair in browser memory, triggers a local `.key` file download, and posts only the public key.
2. In User Management, generating credentials for a new user derives the keypair in browser memory, triggers a local `.key` download, and posts only the public key.
3. No server endpoint generates private keys or returns private key PEM material in response bodies.
4. All unit, bUnit, and integration tests pass at 100.00% coverage.
5. All 8 verification gates pass cleanly.
