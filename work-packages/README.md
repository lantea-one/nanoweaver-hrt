# Work Packages (`PHASES-v2.md`) — Status & Lighter-Model Execution Playbook

> **Why this file is under `work-packages/`:** Gate step 5 scans `docs/**/*.md` and `README.md` at the repo root. Keeping work-package guides under `work-packages/` avoids interfering with documentation scans.

---

## 1. Status of Published Work Packages

All currently published `PHASES-v2.md` work packages in `work-packages/` are **100% implemented and verified (`7/7` gates passing)**:

| Phase | Work Package File | Scope Summary | Status |
| :--- | :--- | :--- | :--- |
| **P4 / P5** | [`db-seam-rollout.md`](db-seam-rollout.md) | `IWeaverDbConnectionFactory` seam rollout across user, DNA, and configuration stores | **COMPLETED (7/7)** |
| **P6** | [`db-seam-profile-repo.md`](db-seam-profile-repo.md) | `UserProfileRepository` on `IWeaverDbConnectionFactory` seam | **COMPLETED (7/7)** |
| **P7.1–P7.3** | [`encrypt-regimen-identity.md`](encrypt-regimen-identity.md), [`ratchet-nondeterminism.md`](ratchet-nondeterminism.md) | `Migration006_EncryptGenderIdentity` + coverage ratchet determinism | **COMPLETED (7/7)** |
| **P7.4 (Stages A & B)** | [`subject-key-persistence.md`](subject-key-persistence.md) | `Migration008_SubjectKeyStore`, `ISubjectKeyRepository`/`SubjectKeyRepository`, `SubjectKeyVault` AES-256-GCM sealing, `SubjectErasureJournal`, startup reconciliation, irreversible GDPR Art. 17 erasure + `VACUUM` | **COMPLETED (7/7)** |
| **P7.5** | [`node-signing-key-persistence.md`](node-signing-key-persistence.md) | Persistent 32-byte FIPS 204 seed (`node_signing_mldsa65.key`), `PublicKeyFingerprint.ComputeKeyId`, `SignWithNodeSigningKey`, `AuditLedgerViolationKind` | **COMPLETED (7/7)** |
| **P8** | [`dataset-signing.md`](dataset-signing.md) | `DatasetManifest` v2 signature fields, `DatasetManifestCanonicalizer`, `DatasetManifestSignature`, `INodeDatasetSigner`/`NodeDatasetSigner` (`dataset_signing_ed25519.key`), `DatasetManifestSignatureVerifier` | **COMPLETED (7/7)** |
| **P9 (Stages A, B, C)** | [`peer-key-registry.md`](peer-key-registry.md) | `Migration007_PeerKeyRegistry` (`weaver_peer_keys`), `IPeerKeyRegistry`/`PeerKeyRepository`, `PeerKeyValidation`, `VerifiedDatasetImporter`, `Peer:RequireSignedDatasets`, `/api/peer-keys` admin endpoints, and documentation updates | **COMPLETED (7/7)** |

---

## 2. Playbook for Lighter / Smaller Models (`flash`, `flash_lite`, Haiku, Sonnet)

When executing any future work package published from `PHASES-v2.md` (e.g., **P10–P18**), lighter models can complete the work in a single pass with minimal token usage by following these exact rules:

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
- Migrations `001` through `008` are registered in `DatabaseMigrationRunner`:
  - `001_InitialSchema` (`Version = 1`)
  - `002_UserAuditAndIndexes` (`Version = 2`)
  - `003_UserProfileAddressesAndContacts` (`Version = 3`)
  - `004_PatientDnaImportsAndResults` (`Version = 4`)
  - `005_EncryptPatientPhi` (`Version = 5`)
  - `006_EncryptGenderIdentity` (`Version = 6`)
  - `007_PeerKeyRegistry` (`Version = 7`)
  - `008_SubjectKeyStore` (`Version = 8`)
- Any future migration added by a later phase starts at **`Migration009_...` (`Version => 9`)** and must update the pinned migration-count assertions in:
  - `tests/NanoWeaverHrt.Tests/SecurityHardeningAndMigrationsTests.cs`
  - `tests/NanoWeaverHrt.Tests/PostgresBackedProfileRepositoryTests.cs`
  - `tests/NanoWeaverHrt.Tests/SubjectKeyPersistenceTests.cs`
  - `tests/NanoWeaverHrt.Tests/PeerKeyRegistryTests.cs`

### Rule 6 — Single-Command Format & Verification
Always format before running `./scripts/verify-all.sh`:
```bash
XDG_RUNTIME_DIR=/tmp dotnet format NanoWeaverHrt.slnx --no-restore && ./scripts/verify-all.sh
```
