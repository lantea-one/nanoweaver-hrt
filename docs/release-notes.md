# NanoWeaverHrt Release Notes & Architecture Evolution

> **Changelog and Security Hardening History Across Platform Phases**

---

## Phase 2 Architecture & Security Hardening (Current Release)

Phase 2 introduces critical post-quantum cryptographic upgrades, transport boundary enforcement, zero-information-disclosure error models, and hardened identity gating.

### 1. Zero-Private-Key-Transmission Authentication (Audit C8 & `POST /api/auth/web-token`)
* **Elimination of Server-Side Signing Oracle**: Completely removed the legacy `POST /api/auth/login-key` endpoint, `AuthenticateWithPrivateKeyAsync`, and associated server-side private key decryption/signing routines. The server never receives, parses, or signs with user private keys during authentication.
* **Client-Side Browser Signing**: Private key envelopes are parsed and decrypted in-memory inside the browser using vendored zero-dependency noble crypto modules (`weaver-crypto-vendor.js`). Challenges are requested from `POST /api/auth/challenge`, signed in browser memory, and exchanged at `POST /api/auth/web-token`.
* **Transport Gate Scoping**: Strictly enforces `Api`, `Web`, and `ApiWeb` transport boundaries across all challenge-response authentication paths.
* **Same-Origin & HttpOnly Cookies**: Interactive browser sessions establish authenticated cookies via `POST /api/auth/web-token` (`HttpOnly; SameSite=Strict; Secure`), with strict same-origin and configured public origin enforcement.
* **Self-or-Administrator Scoping**: Patient endpoints (`/api/patient/dna`, `/api/patient/results`, `/api/patient/imports`) now strictly enforce role gating, returning `404 SubjectRecordNotFoundDetail` to unauthorized callers to prevent user-enumeration attacks.

### 2. PHI Protection & Column-Level Encryption at Rest
* **Database Migrations 005, 006, 010**:
  * `Migration005_EncryptPatientPhi`: Encrypts patient profile demographics, address lines, and emergency contact details using AES-256-GCM authenticated envelopes (`data:application/encrypted;base64,...`).
  * `Migration006_EncryptGenderIdentity`: Widens PostgreSQL column types to `TEXT`, removes plaintext defaults, and idempotently seals `regimen`, `gender_assigned_at_birth`, and `pronouns_json`.
  * `Migration010_EncryptPatientDnaPhi`: Widens `weaver_patient_dna_records.regimen` to `TEXT` on PostgreSQL and encrypts `regimen` and `notes` at rest via `ProfileDataProtector`.
* **Zero Heap Leakage**: Coordinate and buffer rentals leverage `ArrayPool<T>.Shared` and cryptographic memory clearing (`CryptographicOperations.ZeroMemory`).

### 3. Subject Key Persistence & GDPR Article 17 Erasure Journal
* **Post-Quantum Subject Key Vault**: Individualized NIST FIPS 203 ML-KEM-768 keypairs (`sub_...`) are sealed at rest in `weaver_subject_keys` using AES-256-GCM with HKDF-SHA256 and subject-ID AAD binding.
* **Fsynced Erasure Journal**: Deletion requests record irreversible crypto-shredding operations in `.weaver-subject-erasure.jsonl`, providing a tamper-evident audit trail for GDPR Article 17 compliance.

### 4. Authoritative Node Key Persistence & Dedicated Mount
* **Persistent Node Secret Files**: Secrets generated on first launch are persisted to `WEAVER_NODE_KEYS_DIR` (`/workspace/node-keys`) with owner-only (`0600`) filesystem permissions:
  * `token_signing.key`: 256-bit HMAC-SHA256 bearer token signing key.
  * `phi_master.key`: 256-bit AES-256-GCM master key sealing patient PHI and subject keys.
  * `node_signing_mldsa65.key`: 32-byte FIPS 204 ML-DSA-65 seed for the FDA 21 CFR Part 11 audit ledger.
  * `dataset_signing_ed25519.key`: 32-byte Ed25519 seed for dataset export manifests.
* **Dedicated Volume Mount**: Standardized `./simulations/node-keys:/workspace/node-keys:rw` mount isolating secret material from simulation and analytics engines.

### 5. Audit Ledger Persistence Across Restarts
* **Database Migration 009**: Establishes `weaver_audit_ledger` backing the post-quantum FDA 21 CFR Part 11 compliance ledger across node restarts.
* **Cryptographic Hash Chain**: Genesis and action entries maintain an unbroken SHA-256 hash chain signed with the node ML-DSA-65 private key.

### 6. Dataset Provenance & Strict Verification Mode
* **Peer Key Registry (`weaver_peer_keys`)**: Database Migration 007 persists known peer public keys for dataset manifest verification.
* **Ed25519 Manifest Signing**: Exported `.tar.gz` dataset bundles carry an Ed25519 signature of the SHA-256 manifest.
* **Strict Verification Mode (`Peer:RequireSignedDatasets`)**: When enabled, dataset imports reject unsigned manifests or keys unrecognized in the peer registry with `400 Bad Request`.

### 7. Zero-Information-Disclosure RFC 7807 Error Model
* **`SecureExceptionHandlerMiddleware`**: Maps unhandled server exceptions to RFC 7807 `SecureProblemDetails` with opaque correlation IDs.
* **Sanitized Error Payloads**: Redacts database connection strings, credentials, private key PEM headers, and internal SQL fragments from client responses.
* **Corrupt Ciphertext Contract**: Tampered or un-decryptable PHI records return an opaque `500 InternalServerError` with an audited trace identifier, never leaking corrupt ciphertext fragments to callers.

### 8. Test Coverage Ratchet Closeout & Baseline Ratification (Phase P18)
* **Un-excluded Coverage Ratchet**: Tightened `coverage-baseline.json` from `146` uncovered lines down to `59` lines across `29` classes, elevating the line rate to `99.51%` and branch rate to `96.38%`.
* **Zero-Mock ADO.NET Invariant**: Preserved strict prohibition against mocking `DbConnection`/`DbCommand`/`DbDataReader` (Directive 1). All tests execute against real SQLite database instances with schema migrations applied.
* **Eliminated Target Service Uncovered Branches**: Achieved 100.00% coverage on:
  * `UserAuthenticationService`: All validation error branches (malformed/null claims, expired tokens, deleted/inactive users, setup lockout).
  * `UserProfileRepository`: Wrapped all `await using` sessions inside scoped blocks prior to `return`, eliminating compiler-generated state machine disposal branches.
  * `ChannelEventBus`: Addressed cancellation rethrow, full bounded queue handling, and closed stream unregistration.
  * `StackServiceManager`: Covered all `GetServiceByIdAsync` nodes and hardware accelerator fallback on empty attributes.
  * `ValkeyCacheService` & `ValkeyLockService`: Full coverage of in-memory fallbacks, expired key cleanup, retry delays, and double-disposal/fault resilience.
  * `ValkeyEventBridge`: Covered all inbound listener lambdas, echo suppression, outbound failure logging, and corrupted JSON handling.
* **Remaining Uncovered Lines Accounting**:
  * `Program.cs` minimal-API lambdas (lines 1830, 1691-1700, 1973-1980, 2050, 2090, 2134, etc.): Defense-in-depth authorization and conflict return paths that are intentionally unreachable over HTTP because the upstream `ResolveSubjectRecordAccess` middleware intercepts cross-subject requests.
  * Blazor components (`MainLayout.razor`, `Login.razor`, `SetupWizard.razor`): Compiler-generated Razor component state-machine transitions and JS interop disconnected fallbacks.

