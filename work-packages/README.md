# Work Packages (`PHASES-v2.md`) — Status & Lighter-Model Execution Playbook

> **Why this file is under `work-packages/`:** Gate step 5 scans `docs/**/*.md` and `README.md` at the repo root. Keeping work-package guides under `work-packages/` avoids interfering with documentation scans.

---

## 1. Status of Published Work Packages

### 1.1 Master Architecture & Design Documents
- [`PHASES-v2.md`](PHASES-v2.md) — Master Remediation & Feature Phase Plan (`P1–P19`)
- [`c8-challenge-response-proposal.md`](c8-challenge-response-proposal.md) — Audit C8 Browser Challenge-Response Design Proposal

### 1.2 Completed & Verified Work Packages (`P4–P9`, `P13`)
| Phase | Work Package File | Scope Summary | Status |
| :--- | :--- | :--- | :--- |
| **P4 / P5** | [`db-seam-rollout.md`](db-seam-rollout.md) | `IWeaverDbConnectionFactory` seam rollout across user, DNA, and configuration stores | **COMPLETED (7/7)** |
| **P6** | [`db-seam-profile-repo.md`](db-seam-profile-repo.md) | `UserProfileRepository` on `IWeaverDbConnectionFactory` seam | **COMPLETED (7/7)** |
| **P7.1–P7.3** | [`encrypt-regimen-identity.md`](encrypt-regimen-identity.md), [`ratchet-nondeterminism.md`](ratchet-nondeterminism.md) | `Migration006_EncryptGenderIdentity` + coverage ratchet determinism | **COMPLETED (7/7)** |
| **P7.4 (Stages A & B)** | [`subject-key-persistence.md`](subject-key-persistence.md) | `Migration008_SubjectKeyStore`, `ISubjectKeyRepository`/`SubjectKeyRepository`, `SubjectKeyVault` AES-256-GCM sealing, `SubjectErasureJournal`, startup reconciliation, irreversible GDPR Art. 17 erasure + `VACUUM` | **COMPLETED (7/7)** |
| **P7.5** | [`node-signing-key-persistence.md`](node-signing-key-persistence.md) | Persistent 32-byte FIPS 204 seed (`node_signing_mldsa65.key`), `PublicKeyFingerprint.ComputeKeyId`, `SignWithNodeSigningKey`, `AuditLedgerViolationKind` | **COMPLETED (7/7)** |
| **P8** | [`dataset-signing.md`](dataset-signing.md) | `DatasetManifest` v2 signature fields, `DatasetManifestCanonicalizer`, `DatasetManifestSignature`, `INodeDatasetSigner`/`NodeDatasetSigner` (`dataset_signing_ed25519.key`), `DatasetManifestSignatureVerifier` | **COMPLETED (7/7)** |
| **P9 (Stages A, B, C)** | [`peer-key-registry.md`](peer-key-registry.md) | `Migration007_PeerKeyRegistry` (`weaver_peer_keys`), `IPeerKeyRegistry`/`PeerKeyRepository`, `PeerKeyValidation`, `VerifiedDatasetImporter`, `Peer:RequireSignedDatasets`, `/api/peer-keys` admin endpoints, and documentation updates | **COMPLETED (7/7)** |
| **P10** | [`p10-auth-and-dna-hardening.md`](p10-auth-and-dna-hardening.md) | L4 (`AsSpan().IndexOf('.')`), L6 (`GET /api/patient/dna` validation & self/Admin scoping), L8 (`RegisterChallengeForTesting` docblock), lowercase username normalization, duplicate-user audit assertion | **COMPLETED (7/7)** |
| **P10.5** | [`p10.5-audit-ledger-persistence.md`](p10.5-audit-ledger-persistence.md) | `Migration009_AuditLedgerStore` (`weaver_audit_ledger`), `IAuditLedgerRepository`/`AuditLedgerRepository`, `ComplianceAuditService` database persistence across restarts (synchronous `LogAction`) | **COMPLETED (7/7)** |
| **P13** | [`c8-server.md`](c8-server.md) (§1.2) & [`c8-client.md`](c8-client.md) (§1) | BouncyCastle 2.7 Ed25519 (`48`/`83` byte) & ML-DSA-65 (`4098`/`54`/`4060` byte) PKCS#8 DER byte-layout spike, vendoring manifest policy, and `c8-server`/`c8-client`/`c8-remove-oracle` split | **COMPLETED (Spike & Design Published)** |

### 1.3 Ready-to-Execute Work Packages (`P10–P18`, Ordered for Lighter Models)
Execute these packages in exact numerical order so migrations (`009`, `010`) and shared files (`Program.cs`, `UserAuthenticationService.cs`) never conflict:

| Order | Phase | Work Package File | Recommended Model | Scope Summary | Status |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **1** | **P10** | [`p10-auth-and-dna-hardening.md`](p10-auth-and-dna-hardening.md) | `flash_lite` / `flash` / Haiku | L4 (`AsSpan().IndexOf('.')`), L6 (`GET /api/patient/dna` validation & self/Admin scoping), L8 (`RegisterChallengeForTesting` docblock), lowercase username normalization, duplicate-user audit assertion | **COMPLETED (7/7)** |
| **2** | **P10.5** | [`p10.5-audit-ledger-persistence.md`](p10.5-audit-ledger-persistence.md) | `flash` / Haiku / Sonnet | `Migration009_AuditLedgerStore` (`weaver_audit_ledger`), `IAuditLedgerRepository`/`AuditLedgerRepository`, `ComplianceAuditService` database persistence across restarts (synchronous `LogAction`) | **COMPLETED (7/7)** |
| **3** | **P11** | [`p11-hygiene-and-latent-bugs.md`](p11-hygiene-and-latent-bugs.md) | `flash_lite` / `flash` / Haiku | M5 (`WHY !:` comments in `ValkeyCacheService.cs:98` & `ValkeyEventBridge.cs:263`), remove dead `_isSimulating`/`_isParsing`/`_isExportingParquet` flags in `WorkflowWizard.razor`, `CultureInfo.InvariantCulture` in `WeaverDbValues.ReadTimestamp`, `/workspace/configs` `chown` in `Dockerfile.simulation`, synthetic paths in `ApiRoleGatingAndDisclosureTests.cs` | **COMPLETED (7/7)** |
| **4** | **P11.5** | [`p11.5-encrypt-patient-dna-regimen.md`](p11.5-encrypt-patient-dna-regimen.md) | `flash` / Haiku / Sonnet | `Migration010_EncryptPatientDnaPhi` encrypting `regimen` and `notes` on `weaver_patient_dna_records` via `ProfileDataProtector` (`patient_dna.regimen` / `patient_dna.notes` scopes) | **COMPLETED (7/7)** |
| **5** | **P12** | [`p12-docs-and-deployment-truth.md`](p12-docs-and-deployment-truth.md) | `flash_lite` / `flash` / Haiku | Remove `P-256` and `node_storage_master` claims in `docs/compliance-and-security.md`, document all env vars in `example.env`, make `./simulations/node-keys:/workspace/node-keys:rw` default in `docker-compose.yml` & `Dockerfile.web`, create `docs/release-notes.md` | **COMPLETED (7/7)** |
| **6** | **P14** | [`c8-server.md`](c8-server.md) | `flash` / Haiku / Sonnet | `POST /api/auth/web-token` (`isWebRequest: true`, `HttpOnly` cookie, same-origin + configured public origin validation), `ChallengeLifetime = 120s`, L9 `_activeChallenges` 10,000 cap (`429 TooManyRequests`) & `PruneExpiredChallenges` timer | **COMPLETED (7/7)** |
| **7** | **P15 (Stages A & B)** | [`c8-client.md`](c8-client.md) | `flash` / Haiku / Sonnet | Stage A: Vendored `@noble/ed25519` + `@noble/post-quantum` (`ml_dsa65`), `wwwroot/js/weaver-auth.js` (100% coverage in `tests/weaver-auth.test.mjs`). Stage B: `docs/vendored-javascript.md`, `scripts/verify-vendored-js.sh` (Gate Step 8), `Login.razor` rewrite (no `<InputFile>`, no `@bind` on key/passphrase) | Ready |
| **8** | **P16 (Stages A & B)** | [`c8-remove-oracle.md`](c8-remove-oracle.md) | `flash` / Haiku / Sonnet | Stage A: `TestChallengeHelper.cs` & migrate integration test call sites to `/api/auth/challenge` + `/api/auth/web-token`. Stage B: Delete `POST /api/auth/login-key`, `AuthenticateWithPrivateKeyAsync`, `SignEphemeralNonce`, `ExtractPrivateKeyDerFromPem`, `DecryptPrivateKeyBytes`, and `KeyLoginRequest` | Ready |
| **9** | **P17 (Stages A & B)** | [`p17-bunit-security-components.md`](p17-bunit-security-components.md) | `flash` / Haiku / Sonnet | Stage A: Add `bunit` `1.38.5`, cover `Login.razor` & `SetupWizard.razor` at 100%. Stage B: Cover `MainLayout.razor` route guard & idle sign-out, un-exclude `MainLayout`, `Login`, and `SetupWizard` in `coverlet.runsettings` & `coverage-full.runsettings` | Ready |
| **10** | **P18 (Stages A & B)** | [`p18-coverage-baseline-closeout.md`](p18-coverage-baseline-closeout.md) | `flash` / Haiku / Sonnet | Stage A: Cover reachable `Program.cs` minimal-API endpoint lambda branches. Stage B: Cover reachable service state machines, ratify `coverage-baseline.json`, and document any remaining compiler-generated lines honestly | Ready |

---

## 2. Summary of Recorded User Decisions (`P10–P18`)

All architectural and scoping questions were presented via interactive forms and resolved as follows:
1. **Repository Tracking**: Track [`work-packages/PHASES-v2.md`](PHASES-v2.md) and [`work-packages/c8-challenge-response-proposal.md`](c8-challenge-response-proposal.md) in git alongside `P10–P18`.
2. **P5–P9 Follow-Up Sub-Packages**: Keep `P10–P18` strictly scoped to `PHASES-v2.md` and publish `P10.5` (`weaver_audit_ledger` DB persistence with synchronous `LogAction`) and `P11.5` (`weaver_patient_dna_records` `regimen` + `notes` encryption at rest via `Migration010_EncryptPatientDnaPhi`).
3. **[P10] Username Case Rule & `GET /api/patient/dna`**: Normalize all usernames to lowercase (`username.Trim().ToLowerInvariant()`) across create, lookup, challenge, and delete. On `GET /api/patient/dna`, default omitted `patientId` to `currentUser.Username`, validate format (`<= 64` chars, `[A-Za-z0-9._-]`) returning `400`, and return `404 SubjectRecordNotFoundDetail` when a non-Admin requests another user's `patientId`.
4. **[P11] `WorkflowWizard.razor` & Hygiene Sweep**: Remove the dead `_isSimulating`, `_isParsing`, and `_isExportingParquet` flags and unreachable spinner branches in `WorkflowWizard.razor`, add `// WHY !:` comments in `ValkeyCacheService.cs:98` and `ValkeyEventBridge.cs:263`, use `CultureInfo.InvariantCulture` in `WeaverDbValues.ReadTimestamp`, add `/workspace/configs` to `chown 1000:1000` in `Dockerfile.simulation`, and replace `/home/fabigail` with `/home/operator` in `ApiRoleGatingAndDisclosureTests.cs`.
5. **[P12] Dedicated Node-Key Mount & Release Notes**: Set `WEAVER_NODE_KEYS_DIR=/workspace/node-keys` and mount `./simulations/node-keys:/workspace/node-keys:rw` by default in `docker-compose.yml`, add `/workspace/node-keys` to `Dockerfile.web`, track `simulations/node-keys/.gitkeep`, and create `docs/release-notes.md`.
6. **[P13 / P14 / P15 / P16] C8 Challenge-Response & ML-DSA-65**:
   - Vendor both `@noble/ed25519` (with `@noble/hashes/sha512`) and `@noble/post-quantum` (`ml_dsa65`) in `wwwroot/lib/` so both `Ed25519` and `ML-DSA-65` accounts can sign in via the browser UI.
   - Keep signing the raw 32-byte challenge nonce.
   - Cap `_activeChallenges` at `10,000` (`429 TooManyRequests` when full) and prune expired challenges via `PruneExpiredChallenges(DateTimeOffset nowUtc)` + a periodic timer.
   - Split `P15` and `P16` into Stage A and Stage B so smaller models stay well within token and context limits.
7. **[P17 & P18] bUnit & Coverage Ratchet Closeout**:
   - Use `bunit` `1.38.5`, un-exclude `MainLayout`, `Login`, and `SetupWizard` in `coverlet.runsettings` & `coverage-full.runsettings` while keeping the remaining 14 non-security `.razor` files excluded.
   - In `P18`, test real reachable branches using SQLite/in-memory services (never mocking `DbConnection`), ratify the reduced `coverage-baseline.json`, and document any remaining compiler-generated state-machine lines honestly.

---

## 3. Playbook for Lighter / Smaller Models (`flash`, `flash_lite`, Haiku, Sonnet)

When executing any work package in `work-packages/`, lighter models can complete the work in a single pass with minimal token usage by following these exact rules:

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
5. **Avoid unreachable defensive branches** (for example, checking `if (caller is null)` behind an `AdministratorRoutes` gate that already guarantees `CurrentUser` is non-null, or checking `if (await reader.ReadAsync())` right after `UPDATE ... WHERE key_id = @id` returned `affected == 1`).
6. If Step 7 reports `IMPROVED ... Baseline is stale`, update the baseline with:
   ```bash
   python3 -I scripts/check-coverage-ratchet.py artifacts/coverage-full coverage-baseline.json --update
   ```

### Rule 2 — Maintain 100.00% Step-4 Line & Branch Coverage
- Gate step 4 requires `100.00%` line and `100.00%` branch coverage across all non-compiler-generated code.
- Every `if`, `switch`, ternary `? :`, `??`, `?.`, and `catch` in a synchronous method must have a unit test exercising both branches.
- If Step 4 fails, the script prints the exact file, class, and line number of every uncovered line and branch.

### Rule 3 — Pass Step-6 XML Docblock Check (`scripts/check-docblocks.py`) on the First Try
- Every type, method, property, field, and constant — **including `private` and `internal` ones** — must have an XML doc comment (`/// <summary>`).
- Every method or constructor with parameters must have one `/// <param name="...">` per parameter, and every non-`void` method must have `/// <returns>` (or `/// <inheritdoc/>` when implementing an interface/base member).
- Every justification for the null-forgiving operator `!` must have an inline `// WHY '!': ...` comment, and every algorithm/query/endpoint must have `// WHAT:`, `// WHY:`, `// HOW:` comments.

### Rule 4 — SQLite Connection Pool Isolation in Tests
- **Never call `SqliteConnection.ClearAllPools()`** in tests! xUnit runs test classes in parallel, and `ClearAllPools()` disposes active `SQLitePCL.sqlite3` handles used by concurrent `EndpointIntegrationTests`.
- Instead, clear only the specific test's SQLite file pool:
   ```csharp
   using SqliteConnection connection = new($"Data Source={dbPath}");
   SqliteConnection.ClearPool(connection);
   ```

### Rule 5 — Current Migration Numbering
- Migrations `001` through `008` are currently registered in `DatabaseMigrationRunner`:
  - `001_InitialSchema` (`Version = 1`)
  - `002_UserAuditAndIndexes` (`Version = 2`)
  - `003_UserProfileAddressesAndContacts` (`Version = 3`)
  - `004_PatientDnaImportsAndResults` (`Version = 4`)
  - `005_EncryptPatientPhi` (`Version = 5`)
  - `006_EncryptGenderIdentity` (`Version = 6`)
  - `007_PeerKeyRegistry` (`Version = 7`)
  - `008_SubjectKeyStore` (`Version = 8`)
- `P10.5` adds **`Migration009_AuditLedgerStore` (`Version => 9`)**, and `P11.5` adds **`Migration010_EncryptPatientDnaPhi` (`Version => 10`)**. Each migration package lists the exact 4 test files whose pinned migration-count assertions must be incremented.

### Rule 6 — Single-Command Format & Verification
Always format before running `./scripts/verify-all.sh`:
```bash
XDG_RUNTIME_DIR=/tmp dotnet format NanoWeaverHrt.slnx --no-restore && ./scripts/verify-all.sh
```
