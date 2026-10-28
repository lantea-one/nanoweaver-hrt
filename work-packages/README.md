# Work Packages (`PHASES-v2.md`) — Status & Execution Playbook

> **Why this file is under `work-packages/`:** Gate step 5 scans `docs/**/*.md` and `README.md` at the repo root. Keeping work-package guides under `work-packages/` avoids interfering with documentation scans.

---

## 1. Status of Work Packages

### 1.1 Master Architecture & Design Documents
- [`PHASES-v2.md`](PHASES-v2.md) — Master Remediation & Feature Phase Plan (`P1–P20` permanent historical audit reference)

### 1.2 Active Work Packages (`P22–P25: Subject Access & Cohort Management`)
- [`p22-subject-access-service-and-gating.md`](p22-subject-access-service-and-gating.md) — Access control service (`ISubjectAccessService`), middleware route gating, and cryptographic audit ledger persistence
- [`p23-subject-access-api-endpoints.md`](p23-subject-access-api-endpoints.md) — REST API minimal endpoints for subject grants, cohort group CRUD, group membership, and group grants
- [`p24-cohort-web-ui.md`](p24-cohort-web-ui.md) — Sovereign Web UI unified cohort & access management across `DnaManagementPage` and `UserManagement`
- [`p25-cohort-verification-and-closeout.md`](p25-cohort-verification-and-closeout.md) — Full pre-flight verification gate, documentation updates, and coverage ratchet closeout

---

### 1.3 Completed & Ratified Work Packages (`P1–P21`)
*Note: Individual completed work package instruction files have been cleaned up and consolidated into the repository audit trail and `PHASES-v2.md`.*

| Phase | Package / Scope | Key Deliverables & Milestones | Verification Status |
| :--- | :--- | :--- | :--- |
| **P1–P3** | Core Service Architecture | Minimal API pipeline, zero-allocation SIMD trajectory parser, memory pipelines | **COMPLETED** |
| **P4 / P5** | Database Seam Rollout | `IWeaverDbConnectionFactory` seam across user, DNA, and configuration stores | **COMPLETED** |
| **P6** | User Profile Seam | `UserProfileRepository` on `IWeaverDbConnectionFactory` seam | **COMPLETED** |
| **P7.1–P7.3** | Gender Identity Protection | `Migration006_EncryptGenderIdentity` + coverage ratchet determinism | **COMPLETED** |
| **P7.4** | Subject Key Store | `Migration008_SubjectKeyStore`, `ISubjectKeyRepository`, `SubjectKeyVault` AES-256-GCM sealing, `SubjectErasureJournal`, GDPR Art. 17 erasure + `VACUUM` | **COMPLETED** |
| **P7.5** | Node Signing Key | Persistent 32-byte FIPS 204 seed (`node_signing_mldsa65.key`), `PublicKeyFingerprint`, `SignWithNodeSigningKey` | **COMPLETED** |
| **P8** | Dataset Signing | `DatasetManifest` v2 signatures, `DatasetManifestCanonicalizer`, `NodeDatasetSigner` (`dataset_signing_ed25519.key`), `DatasetManifestSignatureVerifier` | **COMPLETED** |
| **P9** | Peer Key Registry | `Migration007_PeerKeyRegistry`, `IPeerKeyRegistry`, `VerifiedDatasetImporter`, `Peer:RequireSignedDatasets`, `/api/peer-keys` endpoints | **COMPLETED** |
| **P10** | Auth & DNA Hardening | Case-insensitive usernames, `GET /api/patient/dna` scope/format validation, L8 docblock, duplicate audit assertion | **COMPLETED** |
| **P10.5** | Audit Ledger Store | `Migration009_AuditLedgerStore` (`weaver_audit_ledger`), `IAuditLedgerRepository`, synchronous `LogAction` DB persistence across restarts | **COMPLETED** |
| **P11** | Hygiene & Latent Bugs | Dead flag removal in `WorkflowWizard.razor`, `ValkeyCacheService` invariants, `CultureInfo.InvariantCulture` timestamps, simulation container permissions | **COMPLETED** |
| **P11.5** | DNA Regimen Encryption | `Migration010_EncryptPatientDnaPhi` encrypting `regimen` and `notes` on `weaver_patient_dna_records` via `ProfileDataProtector` | **COMPLETED** |
| **P12** | Deployment Truth | Removal of legacy P-256 claims in docs, complete environment variable audit, dedicated `./simulations/node-keys` mount, `docs/release-notes.md` | **COMPLETED** |
| **P13** | PKCS#8 DER Spike | BouncyCastle 2.7 Ed25519 & ML-DSA-65 PKCS#8 byte layout specification, ASN.1 SEQUENCE detection | **COMPLETED** |
| **P14** | Server Web-Token Auth | `POST /api/auth/web-token` (`isWebRequest: true`, `HttpOnly` cookie, same-origin + public origin validation), 120s lifetime, 10,000 challenge cap with pruning | **COMPLETED** |
| **P15** | Client Noble Crypto | Vendored `@noble/ed25519` + `@noble/post-quantum` (`ml_dsa65`), `wwwroot/js/weaver-auth.js` (100% JS coverage), Gate Step 8 SRI validation, rewritten `Login.razor` | **COMPLETED** |
| **P16** | Key Oracle Elimination | Complete elimination of server-side private key upload endpoint (`POST /api/auth/login-key`) and server-side private key decryption routines | **COMPLETED** |
| **P17** | bUnit Security Testing | Integrated `bunit` `1.38.5`, 100% component testing across `Login.razor`, `SetupWizard.razor`, and `MainLayout.razor` route guards | **COMPLETED** |
| **P18** | Coverage Ratchet Closeout | Reached 100.00% line & branch coverage on non-compiler code, ratified baseline at 99.51% line / 96.38% branch across all state machines | **COMPLETED** |
| **P19** | Multi-Container & E2E Verification | Multi-container Docker Compose validation (strict 127.0.0.1 bindings, 0600 keys, health probes, clean migrations), Playwright 390px mobile & interactive Ed25519/ML-DSA-65 sign-in | **COMPLETED** |
| **P20** | In-Browser Key Generation & SRI | Pure client-side Ed25519 & ML-DSA-65 keypair derivation and AES-GCM envelope sealing in browser memory, removal of server-side key generation endpoints (`POST /api/setup/generate-keypair`, `POST /api/users/generate-keypair`), and server-side dynamic .NET W3C Subresource Integrity (SRI) SHA-384 generation for all authored scripts and styles via `IAssetIntegrityService`. | **COMPLETED** |
| **P21** | Subject Access Schema & Repo | `Migration011_SubjectAccessAndGroups`, `ISubjectAccessRepository`, `SubjectGrant`, `SubjectGroup`, `SubjectGroupMember`, `SubjectGroupGrant`, tiered access levels (`ReadAccess: 1`, `GenomicAccess: 2`, `FullReadAccess: 3`) | **COMPLETED** |

---

## 2. Summary of Recorded User Decisions (`P10–P20`)

All architectural and scoping questions were presented via interactive forms and resolved as follows:
1. **Repository Tracking**: Track [`work-packages/PHASES-v2.md`](PHASES-v2.md) and [`work-packages/c8-challenge-response-proposal.md`](c8-challenge-response-proposal.md) in git alongside active packages.
2. **P5–P9 Follow-Up Sub-Packages**: Keep `P10–P18` strictly scoped to `PHASES-v2.md` and publish `P10.5` (`weaver_audit_ledger` DB persistence with synchronous `LogAction`) and `P11.5` (`weaver_patient_dna_records` `regimen` + `notes` encryption at rest via `Migration010_EncryptPatientDnaPhi`).
3. **[P10] Username Case Rule & `GET /api/patient/dna`**: Normalize all usernames to lowercase (`username.Trim().ToLowerInvariant()`) across create, lookup, challenge, and delete. On `GET /api/patient/dna`, default omitted `patientId` to `currentUser.Username`, validate format (`<= 64` chars, `[A-Za-z0-9._-]`) returning `400`, and return `404 SubjectRecordNotFoundDetail` when a non-Admin requests another user's `patientId`.
4. **[P11] `WorkflowWizard.razor` & Hygiene Sweep**: Remove the dead `_isSimulating`, `_isParsing`, and `_isExportingParquet` flags and unreachable spinner branches in `WorkflowWizard.razor`, add `// WHY !:` comments in `ValkeyCacheService.cs:98` and `ValkeyEventBridge.cs:263`, use `CultureInfo.InvariantCulture` in `WeaverDbValues.ReadTimestamp`, add `/workspace/configs` to `chown 1000:1000` in `Dockerfile.simulation`, and replace `/home/fabigail` with `/home/operator` in `ApiRoleGatingAndDisclosureTests.cs`.
5. **[P12] Dedicated Node-Key Mount & Release Notes**: Set `WEAVER_NODE_KEYS_DIR=/workspace/node-keys` and mount `./simulations/node-keys:/workspace/node-keys:rw` by default in `docker-compose.yml`, add `/workspace/node-keys` to `Dockerfile.web`, track `simulations/node-keys/.gitkeep`, and create `docs/release-notes.md`.
6. **[P13 / P14 / P15 / P16] C8 Challenge-Response & ML-DSA-65**:
   - Vendor both `@noble/ed25519` (with `@noble/hashes/sha512`) and `@noble/post-quantum` (`ml_dsa65`) in `wwwroot/lib/` so both `Ed25519` and `ML-DSA-65` accounts can sign in via the browser UI.
   - Keep signing the raw 32-byte challenge nonce.
   - Cap `_activeChallenges` at `10,000` (`429 TooManyRequests` when full) and prune expired challenges via `PruneExpiredChallenges(DateTimeOffset nowUtc)` + a periodic timer.
7. **[P17 & P18] bUnit & Coverage Ratchet Closeout**:
   - Use `bunit` `1.38.5`, un-exclude `MainLayout`, `Login`, and `SetupWizard` in `coverlet.runsettings` & `coverage-full.runsettings` while keeping the remaining 14 non-security `.razor` files excluded.
   - In `P18`, test real reachable branches using SQLite/in-memory services (never mocking `DbConnection`), ratify the reduced `coverage-baseline.json`, and document any remaining compiler-generated state-machine lines honestly.
8. **[P19 Decisions] Release Verification, Directive 5 & In-Browser Key Generation**:
   - **Directive 5 Ratification**: Updated `AGENTS.md` Directive 5 to name the exact measured scope: 100.00% line and branch coverage across developer-authored code and security Razor components (Step 4), and a downward-only ratchet baseline for compiler-generated code (Step 7).
   - **In-Browser Key Generation (Phase P20)**: Scheduled follow-up work package [`p20-in-browser-key-generation.md`](p20-in-browser-key-generation.md) for pure client-side key generation in the browser (WebCrypto for Ed25519 and Noble for ML-DSA-65) so private keys never touch the server even during `/setup` or onboarding.
   - **Forgejo CI Runner Flakes**: Recorded user disposition treating intermittent simulation-image build timeouts on `heimdall` as known host infrastructure latency to be re-run on demand.
9. **[P21–P25 Decisions] Subject Access Gating, Tiered Access Levels & Unified Cohorts**:
   - **Tiered Researcher Access Levels**: Supported three configurable access tiers:
     - `ReadAccess` (`1`): Read-only access to clinical/demographic profile (`/api/users/{username}/profile`, pronouns, regimen).
     - `GenomicAccess` (`2`): Level 1 + full read/write access to DNA origami nanocarrier blueprints (`/api/patients/{username}/dna`), sequence imports (`/api/patients/{username}/imports`), and analytical/simulation results (`/api/patients/{username}/results`).
     - `FullReadAccess` (`3`): Level 2 + read-only access to delivery addresses and outreach emergency contacts (`/api/users/{username}/addresses`, `/api/users/{username}/contacts`).
   - **Cohort Group Creation Boundary**: Both Administrators and Researchers can create and manage subject groups (cohorts). Administrators can add any subject; Researchers can **only** add subjects they already have access to.
   - **Unified Cohort Web UI**: Dedicated cohort and group management interface accessible from both `DnaManagementPage.razor` (with cohort-based patient filtering) and `UserManagement.razor` (with grant management modal).
   - **Cleanup of Completed Work Items**: Removed completed instruction files (`docs/work-packages/non-root-containers.md` and `work-packages/c8-challenge-response-proposal.md`), while retaining `PHASES-v2.md` as the permanent completed historical reference.

---

## 3. Playbook for Implementation & Verification

When executing work packages, ensure full compliance with the following quality invariants:

### Rule 1 — Protect the Step-7 Coverage Ratchet (`coverage-baseline.json`)
Gate step 7 (`scripts/check-coverage-ratchet.py`) keys compiler-generated state machines by their ordinal names (`<MethodName>d__N` and `<<Main>b__4_N>d`). To prevent ordinal renumbering or uncovered state-machine lines:
1. **Append new methods at the END of existing classes**, never in the middle.
2. **Append new minimal-API endpoints at the END of `Program.Main`** (immediately before `app.Run();`), and **never capture local variables from `Main`** inside endpoint lambdas (pass all dependencies as lambda parameters instead).
3. **Use type-based DI registration** (`builder.Services.AddSingleton<IService, ServiceImpl>()`) rather than `sp => ...` factory lambdas in `Program.Main`.
4. **Wrap `await using` inside a block** in async repository methods:
   ```csharp
   int affected;
   {
       await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
       await using DbCommand cmd = session.CreateCommand("...");
       affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
   }
   return affected == 1;
   ```
   This prevents the C# compiler from emitting an unreachable exception-path disposal branch at the end of the async state machine.
5. **Avoid unreachable defensive branches**.
6. If Step 7 reports `IMPROVED ... Baseline is stale`, update the baseline with:
   ```bash
   python3 -I scripts/check-coverage-ratchet.py artifacts/coverage-full coverage-baseline.json --update
   ```

### Rule 2 — Maintain 100.00% Step-4 Line & Branch Coverage
- Gate step 4 requires `100.00%` line and `100.00%` branch coverage across all non-compiler-generated code.
- Every `if`, `switch`, ternary `? :`, `??`, `?.`, and `catch` in a synchronous method must have a unit test exercising both branches.

### Rule 3 — Pass Step-6 XML Docblock Check (`scripts/check-docblocks.py`)
- Every type, method, property, field, and constant — **including `private` and `internal` ones** — must have an XML doc comment (`/// <summary>`).
- Every method or constructor with parameters must have one `/// <param name="...">` per parameter, and every non-`void` method must have `/// <returns>` (or `/// <inheritdoc/>` when implementing an interface/base member).
- Every justification for the null-forgiving operator `!` must have an inline `// WHY '!': ...` comment, and every algorithm/query/endpoint must have `// WHAT:`, `// WHY:`, `// HOW:` comments.

### Rule 4 — SQLite Connection Pool Isolation in Tests
- **Never call `SqliteConnection.ClearAllPools()`** in tests! Clear only the specific test's SQLite file pool:
   ```csharp
   using SqliteConnection connection = new($"Data Source={dbPath}");
   SqliteConnection.ClearPool(connection);
   ```

### Rule 5 — Single-Command Format & Verification
Always format before running `./scripts/verify-all.sh`:
```bash
XDG_RUNTIME_DIR=/tmp dotnet format NanoWeaverHrt.slnx --no-restore && ./scripts/verify-all.sh
```
