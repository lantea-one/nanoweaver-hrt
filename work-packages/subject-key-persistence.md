# Work package P7.4: persist subject ML-KEM-768 keys without weakening crypto-shredding

> **Why this file is not under `docs/`.** Gate step 5 scans `docs/**/*.md` and `README.md` for paired
> single-dollar tokens. Work packages are not documentation. **Do not move this file under `docs/`.**

**Agent:** coder. **Base commit: `fb5b807`** (clean, pushed). Your worktree branches from the session-start
commit, so your FIRST action is `git rebase main`. Report the rebase result before doing anything else. Do not
commit. Do not push.

**Order:** this package lands **first**, before P7.5 (`node-signing-key-persistence.md`), P8 and P9. It is
split into two stages. Each stage is one coder pass and is reviewed and merged separately:

- **Stage A:** migration `008`, the subject key repository, sealing at rest, eager load, erasure that destroys
  the stored key, refusal to re-issue a key to an erased subject, and the corrected error model.
- **Stage B:** the erasure journal outside the database, and reconciliation at start, so that restoring a
  database backup taken before an erasure does not silently undo that erasure.

Stage B is not optional. Until it lands, restoring an old database backup undoes erasures (see Decision 8).
**Recommendation:** land Stage B directly after Stage A, before P7.5.

---

## Goal

Subject key pairs live only in memory (`KeyManagementService.cs:43`). After any restart:

1. Every envelope from `/api/compliance/encrypt-record`, and every `.enc.tar.gz` from
   `/api/datasets/export-encrypted` (encrypted to the subject key, `EncryptedStorageService.cs:218`), can never be
   decrypted again.
2. `DecryptSubjectRecord` reports that loss as *"Private key has been permanently destroyed (Crypto-Shredded
   pursuant to GDPR Article 17)"* (`EncryptedStorageService.cs:143-146`). That is a false legal statement. The
   `profile == null` case (never persisted) takes the same branch as a deliberate erasure.
3. **Erasure does not survive a restart either** (finding F3). An erased subject gets a brand-new key on the
   next encrypt or export.

After this package:

- Subject private keys are stored in the database, **sealed** with the node PHI master key, and survive a
  restart.
- An erasure **destroys the stored key material** (the column becomes `NULL`, enforced by a `CHECK`
  constraint). It leaves a permanent tombstone. No code path ever re-issues a key for that subject.
- "Unknown subject" and "deliberately crypto-shredded" are separate outcomes, with separate, accurate wording.
- (Stage B) An erasure is also written to a journal outside the database, and is re-applied at start if the
  database does not show it.

---

## Findings this design rests on (verified in source at `fb5b807`)

| # | Finding | Evidence |
|---|---|---|
| F1 | The only store of subject keys is `ConcurrentDictionary<string, SubjectKeyProfile> _subjectProfiles` (`OrdinalIgnoreCase`). No table, file or migration holds subject keys. | `KeyManagementService.cs:43`; `Database/Migrations/` holds `001`-`006` only |
| F2 | `GetOrCreateSubjectKey` (`:78`, `GetOrAdd` at `:85`) mints a fresh key for any id not in the dictionary. `GetSubjectKey` (`:104-113`) returns null when absent. | source |
| F3 | **Erasure state is in memory only.** `CryptoShredSubjectKey` (`:120-154`) writes only `_subjectProfiles[subjectId] = shreddedProfile`. After a restart the dictionary is empty. `EncryptSubjectRecord` (`EncryptedStorageService.cs:102`) and `ExportEncryptedDatasetBundleAsync` (`:180`) both call `GetOrCreateSubjectKey`, which **mints a new key with `IsShredded = false`** for the erased subject. Encryption and export for that subject then succeed again, `/api/compliance/crypto-shred` returns 200 a second time, and `CryptoShreddedSubjectCount` falls back to 0. The old ciphertext stays unreadable, but only because the old key was never saved, not because of the erasure. **The erasure record and the "no new key" guarantee are both silently lost.** | source |
| F4 | The conflated throw is `EncryptedStorageService.cs:143-146`, exactly as briefed. | source |
| F5 | The at-rest sealing mechanism to reuse is `ProfileDataProtector` (AES-256-GCM, random 96-bit nonce, `data:application/encrypted;base64,` envelope). It is keyed by `INodeSecretStore.PhiMasterKey`, the 32-byte `phi_master.key`, or the SHA-256 of a configured `Security:ProfilePepper` or `SECURITY_PEPPER` (`NodeSecretStore.cs:139-148`). Migrations 005 and 006 use it. | `ProfileDataProtector.cs:100-110` |
| F6 | **`ProfileDataProtector` decryption falls back to a published legacy key** (`:207-215`, `LegacyPhiMasterKeyHex` at `:78`). Reused as-is, anyone with write access to the database could plant a subject key row sealed under that public key, choosing the private key. Every later encryption for that subject would then go to a key the attacker holds. Subject key rows must therefore be opened with the **current key only**. | source |
| F7 | **By default the database and the key that seals it sit in the same directory tree.** The SQLite file is `TRAJECTORY_INCOMING_DIR/weaver_config.db` (`WeaverDbConnectionFactory.cs:92-94`). `phi_master.key` is in `TRAJECTORY_INCOMING_DIR/node-keys/` unless `WEAVER_NODE_KEYS_DIR` is set (`NodeSecretStore.cs:179-194`). A copy of that volume contains both the sealed keys and the key that opens them. | source |
| F8 | A missing `phi_master.key` is **regenerated** at start (`NodeSecretStore.LoadOrCreateKeyFile`, `:357-399`; P7.5 "Tension A"). Without this package's load check, a lost PHI key would only show up later, as failed decryption. | source |
| F9 | `ISqlDialect` has exactly seven members (`Name`, `Backend`, `TrueLiteral`, `FalseLiteral`, `BooleanValue`, `TimestampValue`, `TimestampValueOrNull`). Repositories use one SQL text for both engines, including `ON CONFLICT ... DO NOTHING` (`UserRepository.cs:174-177`). The `INSERT OR REPLACE` versus `ON CONFLICT DO NOTHING` split exists **only** in version recording (`DatabaseMigrationRunner.cs:171-173`). | source |
| F10 | `SecureExceptionSanitizer.SanitizeDetail` redacts any `/word` sequence (`:13`), any `select...from` or `update...set` pair (`:48-50`), and connection-string-like `key=value` pairs (`:24`). New client-facing messages must contain none of these. | source |
| F11 | Coverage gate step 4 must stay at 100% with `WEAVER_TEST_POSTGRES` unset (`verify-all.sh:163-167`). A runtime branch on the database engine inside a repository cannot be covered, so this package has **none**. | source |
| F12 | The pseudonym from `DeIdentifySubject` is deterministic: SHA-384 of a salt published in this repository plus the lowercased identifier, truncated to 64 bits (`ComplianceAuditService.cs:311-318`). A tombstone therefore permanently blocks that person's pseudonym. Anyone who knows their raw identifier can recompute it. | source |
| F13 | Migrations: `001`-`006` are registered (`DatabaseMigrationRunner.cs:39-47`). `007` is reserved by P9 and lands **after** this package, so after this package the applied set is `{1,2,3,4,5,6,8}`. The runner applies by set difference (`:130-133`), so `007` applies cleanly later. | source |

---

## Decisions

### Decision 1: where the keys live

**Database for keys, a file for the erasure journal (Stage B). Not `NodeSecretStore`.**

- **Subject keys go in the database** (`weaver_subject_keys`, migration `008`). The number of subjects is
  unbounded. Creating a key and erasing it each need an atomic conditional write, and an erasure needs a
  tombstone that outlives the key. Both engines provide that. `NodeSecretStore.LoadOrCreateKeyFile` handles
  exactly one 32-byte secret per file, and P7.5 notes that a 64-byte FIPS 203 seed does not fit it. One file
  per subject would turn erasure into a file deletion with no tombstone and no transaction. It would also sit
  on the same volume (F7), so it would protect nothing more.
- **What is stored** is the PKCS#8 private key Base64 exactly as `GenerateKemKeyPair` produces it today
  (`PostQuantumCryptoProvider.cs:66`). That means no new cryptographic code and no seed handling.
- **The erasure journal goes in a file** in `INodeSecretStore.NodeKeysDirectory` (Stage B). It exists only
  because it must survive a *database* restore (Decision 8). It holds no key material.

### Decision 2: what seals subject private keys at rest

- **The key:** the node PHI master key (`INodeSecretStore.PhiMasterKey`), through `ProfileDataProtector`. This is
  the same key and the same AES-256-GCM envelope as the PHI columns sealed by migrations 005 and 006. **No
  second mechanism.**
- **Sealing:** `ProfileDataProtector.EncryptRequiredField(plaintext)`, unchanged.
- **Opening:** a **new** method, `DecryptRequiredFieldWithCurrentKeyOnly`, appended at the end of
  `ProfileDataProtector`. It never tries the legacy key and never touches `LegacyDecryptionCount` (F6).
- **Binding.** The sealed plaintext is bound to the row, so a sealed value cannot be moved to another subject,
  and the public key column cannot be swapped without detection:

  ```
  plaintext = "weaver-subject-key-v1\n" + binding + "\n" + privateKeyBase64
  binding   = lowercase hex SHA-256 of UTF-8( B64(UTF8(subject_key)) + "." + B64(UTF8(key_id)) + "." + public_key_base64 )
  ```

  `B64` is standard Base64. Its alphabet has no `.`, so the concatenation is unambiguous. `binding` is always 64
  characters long.
- **If the PHI key is missing, regenerated, rotated, or replaced by a different pepper:** every active row fails
  to open. `InitializeAsync` throws `SubjectKeyStoreLoadException` and **the host does not start**
  (criteria A22 and A25). This turns F8's silent regeneration into a loud failure whenever subject keys exist.
  Note that `NodeSecretStore` will already have written a new `phi_master.key` by then. The operator must put
  the original back. The message says so.
- **Rotation is not supported.** Any future PHI key rotation package must re-seal `weaver_subject_keys` as well
  as the PHI columns. Put this in the `SubjectKeyVault` remarks.

### Decision 3: schema, migration `008`

`Migration008_SubjectKeyStore`: `Version => 8`, `Name => "008_SubjectKeyStore"`. **DDL only, no constructor
parameter.** It inherits `DatabaseMigrationBase.UpAsync`. The statement contains no `;` other than the one at the
end, so the base class's split on `;` is safe.

SQLite:

```sql
CREATE TABLE IF NOT EXISTS weaver_subject_keys (
    subject_key TEXT PRIMARY KEY,
    subject_id TEXT NOT NULL,
    key_id TEXT NULL,
    algorithm TEXT NOT NULL,
    public_key_base64 TEXT NULL,
    sealed_private_key TEXT NULL,
    is_shredded INTEGER NOT NULL CHECK (is_shredded IN (0, 1)),
    created_at_utc TEXT NOT NULL,
    shredded_at_utc TEXT NULL,
    CONSTRAINT ck_weaver_subject_keys_state CHECK (
        (is_shredded = 0 AND key_id IS NOT NULL AND public_key_base64 IS NOT NULL
             AND sealed_private_key IS NOT NULL AND shredded_at_utc IS NULL)
        OR (is_shredded = 1 AND public_key_base64 IS NULL
             AND sealed_private_key IS NULL AND shredded_at_utc IS NOT NULL))
);
```

PostgreSQL is identical except: `is_shredded BOOLEAN NOT NULL` with no `IN` check, `created_at_utc TIMESTAMPTZ
NOT NULL`, `shredded_at_utc TIMESTAMPTZ NULL`, and `FALSE`/`TRUE` in place of `0`/`1` in the constraint.

The columns:

- **`subject_key`:** `subjectId.ToUpperInvariant()`. .NET defines `OrdinalIgnoreCase` as an ordinal comparison of
  the invariant uppercase forms, so this keeps today's case-insensitive identity. The in-memory cache is keyed
  by the same string, with `StringComparer.Ordinal`.
- **`subject_id`:** the id as first presented, for display (`SubjectKeyProfile.SubjectId`).
- **The `CHECK` constraint** makes "a flag hiding recoverable key material" impossible to store. A tombstone
  holds no private key and no public key. The public key is cleared as well, for data minimisation, and because
  journal-rebuilt tombstones (Stage B) have none.

**Write idioms for this table: the answer to the brief's question.** `INSERT OR REPLACE` is correct for version
recording and **wrong here**. On SQLite it deletes the conflicting row and inserts the new one, so it would
replace a tombstone with an active key. `ON CONFLICT DO UPDATE` does the same on both engines. So for this table:

| Operation | SQL (one text, both engines) |
|---|---|
| Create an active row | `INSERT ... ON CONFLICT (subject_key) DO NOTHING` |
| Rebuild a tombstone (Stage B) | `INSERT ... ON CONFLICT (subject_key) DO NOTHING` |
| Erase | `UPDATE weaver_subject_keys SET sealed_private_key = NULL, public_key_base64 = NULL, is_shredded = {TrueLiteral}, shredded_at_utc = @t WHERE subject_key = @k AND is_shredded = {FalseLiteral}` |
| Delete | **Never.** No `DELETE`, no `OR REPLACE`, no `DO UPDATE` appears in the repository (criterion A7). |
| Compact after an erasure | `VACUUM` (Decision 7) |

Triggers that would forbid reverting a tombstone were considered and **rejected**. The PostgreSQL version needs a
PL/pgSQL body full of `;`, which breaks `DatabaseMigrationBase`'s split. The SQLite `REPLACE` path does not fire
delete triggers unless `recursive_triggers` is on. The `CHECK` constraint plus criteria A4 and A7 cover the
same risk.

### Decision 4: loading, eager and fail-loud

- `SubjectKeyVault.InitializeAsync` is called **once**, from `Program.Main`, directly after the legacy PHI
  re-encryption log line (`Program.cs:243-245`). It reads every row and opens every active row. It publishes
  the cache **only if every row is valid**.
- **Any invalid active row aborts the start:** `SubjectKeyStoreLoadException`, the host does not start, and no
  row is touched. An invalid row is one that fails to unseal, has a bad prefix, a binding mismatch, an
  algorithm other than `ML-KEM-768`, or an empty private key.
- **Never regenerate-and-continue.** No `catch` falls back to generating a key.
- Until `InitializeAsync` succeeds, every subject operation throws `InvalidOperationException` (`NotLoadedMessage`).
  The only exception is the whitespace-id short-circuit; see the consistency check, item 6.
- Eager loading is chosen because it is the only posture in which a lost PHI key fails the start rather than a
  patient's request hours later. That is the same reasoning as `Program.cs:210-215`.

### Decision 5: `GetOrCreateSubjectKey` for an unknown versus an erased subject

- **Unknown subject:** minting is still correct. Encrypting to a new subject must work. But the key is
  **persisted before it is returned**. If the insert fails, nothing is cached and the exception propagates, so
  no data is ever encrypted to an unpersisted key (criterion A19).
- **Erased subject:** **never minted.** Throws `SubjectKeyUnavailableException` with `Reason == Erased`, from the
  cache and, in a race, from the database (criteria A15 and A20).
- **Cache transitions allowed:** absent to active, absent to tombstone, active to tombstone. **Never tombstone to
  active.** Creation uses `TryAdd`. Only erasure overwrites an entry.

### Decision 6: the error model

| Situation | Type | Where it surfaces | HTTP |
|---|---|---|---|
| Subject was deliberately erased | `SubjectKeyUnavailableException` (derives from `CryptographicException`), `Reason = Erased` | encrypt, decrypt, export | 400, exactly as today (pinned by `PostQuantumComplianceTests.cs:528-535`). Detail = `ErasedMessage`. |
| No key was ever stored for the subject | `SubjectKeyUnavailableException`, `Reason = UnknownSubject` | decrypt only (encrypt and export create a key) | 400, as today. Detail = `UnknownSubjectMessage`. |
| Stored keys cannot be opened at start | `SubjectKeyStoreLoadException` (derives from `Exception`) | `InitializeAsync` | Host does not start. No HTTP. |
| Store not loaded (programming error) | `InvalidOperationException(NotLoadedMessage)` | every operation | Unreachable in production, because `Main` loads before serving. |
| Database unavailable | `WeaverDatabaseUnavailableException` (existing type) | create, erase | 500 with an opaque correlation id, through the existing middleware |
| Row changed by something other than this process | `InvalidOperationException(ExternalChangeMessage)` | erase | 400 through the middleware |

- Deriving from `CryptographicException` means the existing `catch (CryptographicException)` blocks in the
  encrypt and decrypt endpoints (`Program.cs:1465`, `:1499`) and the export `catch (Exception)` (`:1557`) handle
  the new outcomes with **no new branch**.
- Every message below is client-safe: no key material, path, SQL or connection detail. `SanitizeDetail` must
  return each one unchanged (criterion A29).

**Exact strings.** These are `internal const` or `internal static` builders on `SubjectKeyUnavailableException`,
`SubjectKeyVault` and `Program`.

```
ErasedMessage(id, t)        = $"Subject '{id}' was crypto-shredded at {t:O}. This node deliberately destroyed the subject's private decryption key (GDPR Article 17 erasure), so records encrypted to that key can no longer be decrypted and no new key will be issued for this subject identifier."
UnknownSubjectMessage(id)   = $"This node holds no stored key for subject '{id}'. No erasure took place: a key for this subject was never persisted on this node. Records encrypted before subject keys were persisted, or by another node, cannot be decrypted here."
NotLoadedMessage            = "The subject key store has not been loaded. SubjectKeyVault.InitializeAsync must complete before any subject key is used."
AlreadyLoadedMessage        = "The subject key store is already loaded."
ExternalChangeMessage       = "The subject's stored key row is no longer active in the database, although this process loaded it as active. Something other than this process changed the database; restart the service so the key store is reloaded. Nothing was erased by this call."
RowMissingMessage           = "The subject key row expected after a conflicting insert was not found."
LoadFailureMessage(f, n)    = $"The subject key store could not be loaded: {f} of {n} stored subject keys could not be opened with this node's PHI master key (phi_master.key, or the configured pepper override). The key was replaced, rotated or regenerated, or the rows were altered. The service will not start, because continuing would leave every record encrypted to those keys undecryptable. Restore the original key file; nothing was changed."
ShredOkMessage              = "Subject private decryption key destroyed in this node's key store (GDPR Article 17 erasure). Sealed copies remain in any backup taken before this moment until that backup is expired."
ShredAlreadyMessage(id)     = $"Subject '{id}' was already crypto-shredded; nothing further was destroyed."
ShredUnknownMessage(id)     = $"This node holds no stored key for subject '{id}'; nothing was erased."
```

`UnknownSubjectMessage` deliberately contains none of `Article 17`, `shred` or `destroy`. The decrypt-path
`ErasedMessage` and the encrypt/export-path `ErasedMessage` are the same string.

### Decision 7: what erasure destroys, and what it cannot

**Erasure order (Stage A).** All of it runs under the vault's write gate:

1. The `UPDATE` from Decision 3 is the commit point.
2. Zero rows affected: throw `ExternalChangeMessage`. The cache is unchanged.
3. One row affected: replace the cache entry with a tombstone (`PrivateKeyBase64 = null`, `PublicKeyBase64 = ""`).
4. `CompactStorageAsync` (`VACUUM`).

A failure at step 1 leaves the subject active in the database **and** in the cache, and propagates. A failure
at step 4 propagates after the erasure is committed and cached.

**What erasure destroys:**

- The only live copy of the sealed key: the column becomes `NULL`, enforced by the constraint.
- On SQLite, the leftover copy in free database pages. `VACUUM` rebuilds the file from live rows (criterion A16).
  The rollback journal that held the old page during the `UPDATE` is deleted at commit (SQLite's default
  `journal_mode`; nothing in `src/` changes it).
- The in-cache reference to the private key.

**What erasure cannot destroy.** State each of these in the `SubjectKeyVault` remarks:

- **Backups taken before the erasure.** They contain the sealed key. They are recoverable by anyone who also has
  `phi_master.key`, and by default the same volume copy contains it (F7). This cannot be prevented in software.
  It is an operator retention duty. `ShredOkMessage` states it.
- **On PostgreSQL:** old row versions until `VACUUM` reuses the space, WAL segments, and any WAL archive or PITR
  base backup. `VACUUM` there is best effort. It is the same SQL text, because a branch on the engine cannot be
  covered (F11).
- **Process memory.** The private key is a .NET `string` and cannot be zeroed. It becomes unreachable and is
  collected later. Moving `PqcKeyMaterial` to pinned byte buffers is a non-goal.

**Why `VACUUM` and not `PRAGMA secure_delete`.** The pragma is per-connection, SQLite-only and an error on
PostgreSQL, so it would need an uncoverable engine branch. Plain `VACUUM` is valid on both engines. It is a
maintenance command, not DDL, so Directive 13 does not apply. **Cost:** on PostgreSQL it vacuums the whole
database. Erasures are rare.

### Decision 8: backups and restores, the direct answer

**What an operator's database backup contains after this package:**

- For every active subject: the subject id, key id, public key, and the private key sealed with AES-256-GCM
  under `phi_master.key`.
- For every erased subject: a tombstone (subject id, key id when known, timestamps) and **no key material**.

A database-only backup (`pg_dump`, or a copy of `weaver_config.db` alone) is useless for decryption without
`phi_master.key`. A copy of the whole default volume is not, because it holds both (F7). **Recommend in the
remarks** that operators put `WEAVER_NODE_KEYS_DIR` on separate storage, with separate backup handling.

**Restoring a backup taken before an erasure:**

- **After Stage A only:** the erased subject comes back as active, with its key, and **is silently un-erased.**
  This is the gap Stage B closes.
- **After Stage B:**
  1. Every erasure is first written to `subject_erasures.journal` in the node-keys directory.
  2. At start, before any row is opened, every journalled subject is forced to a tombstone. An active row is
     erased again (`ReappliedErasureCount`). A missing row is re-created as a tombstone (`RebuiltTombstoneCount`).
  3. Then a Warning is logged.

  A restored *database* therefore never undoes an erasure.
- **Remaining limit, after Stage B.** If the operator restores the node-keys directory from the same old backup
  as the database, the journal goes back in time with it, and the erasure is undone. That restores the key
  material itself, so no software on this node can tell. Document it as an operator duty: re-apply erasures
  from the organisation's own erasure register after any full restore. **No criterion claims otherwise.**

### Decision 9: the erasure journal (Stage B)

**File:** `subject_erasures.journal` in `INodeSecretStore.NodeKeysDirectory`. UTF-8, with `\n` line endings.

```
weaver-subject-erasure-journal v1\n
E\t<Base64(UTF-8(subject_key))>\t<shreddedAtUtc.ToString("O", CultureInfo.InvariantCulture)>\n
...
```

**Append** (`SubjectErasureJournal.Append`):

- Under a `lock`, open with `FileMode.Append`, `FileAccess.Write`, `FileShare.Read`, and
  `UnixCreateMode = UserRead | UserWrite`. Use the same `#pragma warning disable CA1416` with justification as
  `NodeSecretStore.cs:323-331`.
- If `stream.Length == 0`, write the header first.
- Write the entry in one `Write` call, then `Flush(flushToDisk: true)`.

**Read** (`ReadAll`):

- A missing file gives an empty list.
- Anything else malformed throws `SubjectKeyStoreLoadException`. That covers a wrong header, a line not starting
  with `E\t`, invalid Base64, a timestamp that fails `ParseExact("O")`, or a final line without `\n`. The message
  names `subject_erasures.journal` and the 1-based line number, and never the directory.
- Parse with `IndexOf('\t')`. No `Split`, `Regex` or LINQ.
- Duplicate subject keys are allowed (retries append again). Keep the **earliest** timestamp for each key.

**Erasure order (Stage B), under the write gate:**

1. Append the journal entry.
2. Run the `UPDATE`. This is the database commit point.
3. Zero rows affected: throw `ExternalChangeMessage`.
4. Cache tombstone.
5. Run `VACUUM`.

**The cache becomes a tombstone only after the `UPDATE` succeeds, in both stages.** A failure after step 1
leaves the erasure *pending*. It is completed at the next start by reconciliation, and a retry completes it
earlier. Write this in the docblock.

**Reconciliation**, inside `InitializeAsync`, **before** any active row is opened:

1. Read the journal and all rows.
2. For each journalled key:
   - no row: `TryInsertTombstoneAsync`, giving `subject_id = subject_key`, `key_id = NULL`, and
     `created_at = shredded_at` = the journal time;
   - active row: `MarkShreddedAsync` with the journal time;
   - tombstone: nothing.
3. For each tombstone row not in the journal: `Append` (`JournalBackfillCount`). This upgrades Stage A databases.
4. If anything was re-applied or rebuilt, run `CompactStorageAsync` once.
5. Only then open the remaining active rows (Decision 4).

---

## Files in scope

### Stage A

| Path | Role |
|---|---|
| `src/NanoWeaverHrt/Database/Migrations/Migration008_SubjectKeyStore.cs` (new) | DDL from Decision 3. |
| `src/NanoWeaverHrt/Database/Migrations/DatabaseMigrationRunner.cs` | Append `new Migration008_SubjectKeyStore()` after `Migration006_EncryptGenderIdentity(protector)` (`:46`). Nothing else. |
| `src/NanoWeaverHrt/Security/KeyManagement/SubjectKeyRow.cs` (new) | Record mirroring one table row. |
| `src/NanoWeaverHrt/Security/KeyManagement/ISubjectKeyRepository.cs` (new) | Database seam. Lets tests wrap the real repository for fault injection without mocking any `Db*` type. |
| `src/NanoWeaverHrt/Security/KeyManagement/SubjectKeyRepository.cs` (new) | SQL only. No cryptography. |
| `src/NanoWeaverHrt/Security/KeyManagement/SubjectKeyVault.cs` (new) | Cache, seal and open, load, create, erase. Holds every async state machine for subject keys. |
| `src/NanoWeaverHrt/Security/KeyManagement/SubjectKeyLoadReport.cs` (new) | Counts returned by `InitializeAsync`. |
| `src/NanoWeaverHrt/Security/KeyManagement/SubjectErasureOutcome.cs` (new) | Enum. |
| `src/NanoWeaverHrt/Security/KeyManagement/SubjectKeyUnavailableReason.cs` (new) | Enum. |
| `src/NanoWeaverHrt/Security/KeyManagement/SubjectKeyUnavailableException.cs` (new) | Typed refusal. |
| `src/NanoWeaverHrt/Security/KeyManagement/SubjectKeyStoreLoadException.cs` (new) | Start-up failure. |
| `src/NanoWeaverHrt/Security/KeyManagement/IKeyManagementService.cs` | Two members replaced in place, one appended (see Interfaces). |
| `src/NanoWeaverHrt/Security/KeyManagement/KeyManagementService.cs` | New constructor. Remove `_subjectProfiles`. Subject members become **non-async** pass-throughs to the vault. Rewrite the type remarks (Directive 4). |
| `src/NanoWeaverHrt/Security/Authentication/ProfileDataProtector.cs` | Append `DecryptRequiredFieldWithCurrentKeyOnly` as the **last** member. Change nothing else. |
| `src/NanoWeaverHrt/Security/Storage/IEncryptedStorageService.cs` | `EncryptSubjectRecord` is replaced in place by `EncryptSubjectRecordAsync`. Update the `DecryptSubjectRecord` exception docs. |
| `src/NanoWeaverHrt/Security/Storage/EncryptedStorageService.cs` | Replace `EncryptSubjectRecord` **in place** with `EncryptSubjectRecordAsync`. Decrypt uses `GetSubjectPrivateKeyForDecryption`. Export uses `GetOrCreateSubjectKeyAsync`. Delete the three conflated throws (`:103-106`, `:143-146`, `:181-184`). |
| `src/NanoWeaverHrt/Components/Pages/ComplianceSecurity.razor` | `ExecuteCryptoShred` becomes `private async Task ExecuteCryptoShred()` (same name and position). Uses the three Razor messages. |
| `src/NanoWeaverHrt/Program.cs` | DI registrations. One `InitializeAsync` block and one log line. Two lambdas (`/api/compliance/crypto-shred` `:1414`, `/api/compliance/encrypt-record` `:1448`) become `async` **in place**. The success message changes. |
| `tests/NanoWeaverHrt.Tests/SubjectKeyTestHost.cs` (new) | The one place tests build `NodeSecretStore`, `ProfileDataProtector`, factory, repository, vault and `KeyManagementService` over a temp root. Has `CreateLoaded()` (sync, `.GetAwaiter().GetResult()`, as `AuthenticationServiceTests.cs:79` does), `CreateLoadedAsync()`, and `Reopen()`, which builds fresh instances over the same root to simulate a restart. |
| `tests/NanoWeaverHrt.Tests/FaultInjectingSubjectKeyRepository.cs` (new) | Wraps the real `SubjectKeyRepository`. Has per-method throw switches. |
| `tests/NanoWeaverHrt.Tests/SubjectKeyPersistenceTests.cs` (new) | Stage A criteria. `[Collection(PostgresTestEnvironment.CollectionName)]` for the PostgreSQL twins. |
| `tests/NanoWeaverHrt.Tests/PostQuantumComplianceTests.cs` | Required edits, listed exactly below. |
| `tests/NanoWeaverHrt.Tests/AuthenticationServiceTests.cs` | Required edit at `:81-92`. |
| `tests/NanoWeaverHrt.Tests/RazorComponentLifecycleTests.cs` | Required edit at `:106-129`. |
| `tests/NanoWeaverHrt.Tests/SecurityHardeningAndMigrationsTests.cs` | Required edit at `:168`, `:175`, `:179`, `:183`. |
| `tests/NanoWeaverHrt.Tests/PostgresBackedProfileRepositoryTests.cs` | Required edit at `:212`. |
| `coverage-baseline.json` | Only if step 7 reports it stale (Constraint 8). |

### Stage B

| Path | Role |
|---|---|
| `src/NanoWeaverHrt/Security/KeyManagement/SubjectErasureJournal.cs` (new) | Decision 9 file handling. |
| `src/NanoWeaverHrt/Security/KeyManagement/SubjectErasureJournalEntry.cs` (new) | `readonly record struct (string SubjectKey, DateTimeOffset ShreddedAtUtc)`. |
| `src/NanoWeaverHrt/Security/KeyManagement/SubjectKeyVault.cs` | Constructor gains `SubjectErasureJournal` (appended as the last parameter). Journal-first erasure. Reconciliation. New private helpers **at the end**. |
| `src/NanoWeaverHrt/Security/KeyManagement/SubjectKeyLoadReport.cs` | Append three counts. |
| `src/NanoWeaverHrt/Security/KeyManagement/ISubjectKeyRepository.cs`, `SubjectKeyRepository.cs` | Append `TryInsertTombstoneAsync` (last). |
| `src/NanoWeaverHrt/Program.cs` | Register `SubjectErasureJournal`. Add one conditional Warning after the Stage A log line. No lambda. |
| `tests/NanoWeaverHrt.Tests/SubjectKeyTestHost.cs` | Construct the journal. |
| `tests/NanoWeaverHrt.Tests/SubjectErasureJournalTests.cs` (new) | Stage B criteria. |

**Must not change (either stage):** `NodeSecretStore.cs`, `INodeSecretStore.cs`, `NodeSecretOrigin.cs`,
`NodeSecretProvisioningException.cs`, `ISqlDialect.cs` and both dialects, `DatabaseMigrationBase.cs`,
`Migration001`-`006`, `WeaverDbConnectionFactory.cs`, `SecureExceptionSanitizer.cs`,
`SecureExceptionHandlerMiddleware.cs`, `PostQuantumCryptoProvider.cs`, `ComplianceAuditService.cs`,
`coverlet.runsettings`, `coverage-full.runsettings`, `AGENTS.md`, anything under `docs/`, `work-packages/*.md`.

---

## Interfaces and signatures

```csharp
namespace NanoWeaverHrt.Security.KeyManagement;

public sealed record SubjectKeyRow
{
    public required string SubjectKey { get; init; }        // ToUpperInvariant form; primary key
    public required string SubjectId { get; init; }         // as first presented
    public string? KeyId { get; init; }                     // null only on journal-rebuilt tombstones
    public required string Algorithm { get; init; }         // "ML-KEM-768"
    public string? PublicKeyBase64 { get; init; }           // null iff IsShredded
    public string? SealedPrivateKey { get; init; }          // null iff IsShredded
    public required bool IsShredded { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? ShreddedAtUtc { get; init; }     // non-null iff IsShredded
}

public interface ISubjectKeyRepository
{
    /// Every row, ORDER BY subject_key ASC.
    Task<IReadOnlyList<SubjectKeyRow>> ListAllAsync(CancellationToken ct = default);
    /// The row; throws InvalidOperationException(RowMissingMessage) when absent.
    Task<SubjectKeyRow> GetRequiredAsync(string subjectKey, CancellationToken ct = default);
    /// INSERT ... ON CONFLICT (subject_key) DO NOTHING. True iff a row was inserted.
    /// ArgumentException when row.IsShredded or a key column is null.
    Task<bool> TryInsertActiveAsync(SubjectKeyRow row, CancellationToken ct = default);
    /// The erasure UPDATE from Decision 3. True iff exactly one row changed.
    Task<bool> MarkShreddedAsync(string subjectKey, DateTimeOffset shreddedAtUtc, CancellationToken ct = default);
    /// Executes exactly "VACUUM".
    Task CompactStorageAsync(CancellationToken ct = default);
    // Stage B, appended last:
    /// INSERT tombstone ... ON CONFLICT (subject_key) DO NOTHING. True iff inserted.
    Task<bool> TryInsertTombstoneAsync(string subjectKey, string subjectId, DateTimeOffset shreddedAtUtc, CancellationToken ct = default);
}

public sealed class SubjectKeyRepository : ISubjectKeyRepository
{
    public SubjectKeyRepository(IWeaverDbConnectionFactory connectionFactory); // ArgumentNullException
}

public enum SubjectErasureOutcome { Erased, AlreadyErased, UnknownSubject }
public enum SubjectKeyUnavailableReason { Erased, UnknownSubject }

public sealed class SubjectKeyUnavailableException : CryptographicException
{
    internal SubjectKeyUnavailableException(string subjectId, SubjectKeyUnavailableReason reason, DateTimeOffset? shreddedAtUtc);
    public SubjectKeyUnavailableReason Reason { get; }
    public DateTimeOffset? ShreddedAtUtc { get; }        // set iff Reason == Erased
    internal static string ErasedMessage(string subjectId, DateTimeOffset shreddedAtUtc);
    internal static string UnknownSubjectMessage(string subjectId);
}

public sealed class SubjectKeyStoreLoadException : Exception
{
    public SubjectKeyStoreLoadException(string message);
    public SubjectKeyStoreLoadException(string message, Exception innerException);
}

public sealed record SubjectKeyLoadReport
{
    public int ActiveCount { get; init; }
    public int ErasedCount { get; init; }
    // Stage B, appended:
    public int ReappliedErasureCount { get; init; }
    public int RebuiltTombstoneCount { get; init; }
    public int JournalBackfillCount { get; init; }
}

public sealed class SubjectKeyVault : IDisposable
{
    internal const string SealPrefix = "weaver-subject-key-v1\n";
    /// Stage B appends a fourth parameter: SubjectErasureJournal journal.
    public SubjectKeyVault(ISubjectKeyRepository repository, ProfileDataProtector protector, IPostQuantumCryptoProvider cryptoProvider);

    public bool IsLoaded { get; }
    /// Loads and validates all rows; publishes the cache only if all are valid. Second call: InvalidOperationException(AlreadyLoadedMessage).
    public Task<SubjectKeyLoadReport> InitializeAsync(CancellationToken ct = default);
    /// Whitespace -> null (before the loaded check). Else cached active or tombstone profile; unknown -> null.
    public SubjectKeyProfile? Find(string subjectId);
    /// Every cached profile as a copy with KeyMaterial.PrivateKeyBase64 = null. foreach, no LINQ.
    public IReadOnlyList<SubjectKeyProfile> ListWithoutPrivateKeys();
    /// Whitespace -> ArgumentException. Erased -> SubjectKeyUnavailableException(Erased). Persist-then-cache.
    public Task<SubjectKeyProfile> GetOrCreateAsync(string subjectId, CancellationToken ct = default);
    /// Whitespace -> UnknownSubject (before the loaded check).
    public Task<SubjectErasureOutcome> EraseAsync(string subjectId, CancellationToken ct = default);
    /// Whitespace -> ArgumentException. Unknown or Erased -> SubjectKeyUnavailableException. Else the private key.
    public string GetPrivateKeyForDecryption(string subjectId);
    public void Dispose();                                   // disposes the write-gate SemaphoreSlim

    internal static string NormalizeSubjectKey(string subjectId) => subjectId.ToUpperInvariant();
    internal static string ComputeBinding(string subjectKey, string keyId, string publicKeyBase64);
}
```

`ProfileDataProtector`, appended as the last member:

```csharp
/// AES-256-GCM open with the CURRENT node key only. Never tries the legacy key and never changes
/// LegacyDecryptionCount. ArgumentNullException for null. CryptographicException (messages are the existing
/// UnprotectedFieldMessage, MalformedEnvelopeMessage, or "Protected field could not be decrypted with the
/// current node key.") for a non-envelope, an empty string, bad Base64, a payload shorter than nonce+tag,
/// or an authentication failure. The message never contains the input.
public string DecryptRequiredFieldWithCurrentKeyOnly(string storedValue);
```

`IKeyManagementService` after Stage A. `GetNodeMasterKey` stays (P7.5 removes it).

```csharp
PqcKeyMaterial GetNodeSigningKey();                                   // unchanged
PqcKeyMaterial GetNodeMasterKey();                                    // unchanged
Task<SubjectKeyProfile> GetOrCreateSubjectKeyAsync(string subjectId, CancellationToken ct = default); // REPLACES GetOrCreateSubjectKey, same slot
SubjectKeyProfile? GetSubjectKey(string subjectId);                   // unchanged signature; => vault.Find
Task<SubjectErasureOutcome> CryptoShredSubjectKeyAsync(string subjectId, CancellationToken ct = default); // REPLACES CryptoShredSubjectKey, same slot
IReadOnlyList<SubjectKeyProfile> GetAllSubjectProfiles();             // => vault.ListWithoutPrivateKeys (private keys now null)
string GetSubjectPrivateKeyForDecryption(string subjectId);           // NEW, appended last
```

`KeyManagementService`:

- Constructor: `KeyManagementService(IPostQuantumCryptoProvider cryptoProvider, SubjectKeyVault subjectKeys)`.
  `ArgumentNullException` for either argument. The node keys are generated exactly as today (`:57-58`).
- Every subject member is an expression-bodied call to the vault, **with no `async` keyword**, so
  `KeyManagementService` keeps zero compiler-generated state machines.

`IEncryptedStorageService` and `EncryptedStorageService`:

```csharp
Task<EncryptedEnvelope> EncryptSubjectRecordAsync<T>(string subjectId, T record, CancellationToken ct = default); // replaces EncryptSubjectRecord in place
```

- `EncryptSubjectRecordAsync` is `async`. Argument checks come first, then `await
  _keyManagement.GetOrCreateSubjectKeyAsync(subjectId, ct).ConfigureAwait(false)`, then exactly today's body from
  `:108` on.
- `DecryptSubjectRecord` keeps its signature. It calls `string privateKey =
  _keyManagement.GetSubjectPrivateKeyForDecryption(subjectId);` and then today's body from `:148` on.
- Export: `:180-184` becomes one `await _keyManagement.GetOrCreateSubjectKeyAsync(subjectId,
  cancellationToken).ConfigureAwait(false)`.

`Program.cs`:

- **DI**, directly after `:111`: `AddSingleton<ISubjectKeyRepository, SubjectKeyRepository>()` and
  `AddSingleton<SubjectKeyVault>()`. Stage B adds `AddSingleton<SubjectErasureJournal>()`.
  `ProfileDataProtector` is already registered (`:116`).
- **Load block**, directly after `:245`, with WHAT/WHY/HOW comments and **no lambda**:

  ```csharp
  SubjectKeyVault subjectKeyVault = app.Services.GetRequiredService<SubjectKeyVault>();
  SubjectKeyLoadReport subjectKeyReport = await subjectKeyVault.InitializeAsync();
  app.Logger.LogInformation(
      "Subject key store loaded. Active subject keys: {ActiveCount}. Crypto-shredded subjects: {ErasedCount}.",
      subjectKeyReport.ActiveCount,
      subjectKeyReport.ErasedCount);
  ```

  Stage B appends:

  ```csharp
  if (subjectKeyReport.ReappliedErasureCount + subjectKeyReport.RebuiltTombstoneCount > 0)
  {
      app.Logger.LogWarning(
          "Subject erasure reconciliation re-applied {ReappliedCount} erasures and restored {RebuiltCount} erasure records that the database did not contain. A database restored from a backup taken before those erasures is the usual cause.",
          subjectKeyReport.ReappliedErasureCount,
          subjectKeyReport.RebuiltTombstoneCount);
  }
  ```

  Check that `subjectKeyVault` and `subjectKeyReport` are unused names in `Main` before using them.
- **Crypto-shred lambda** (`:1414`): becomes `async (CryptoShredRequest req, IKeyManagementService keyMgmt,
  IComplianceAuditService compliance, CancellationToken ct) =>`. The empty check stays.
  - `SubjectErasureOutcome outcome = await keyMgmt.CryptoShredSubjectKeyAsync(req.SubjectId, ct);`
  - If `outcome != Erased`: return `Results.NotFound(new { error = outcome == SubjectErasureOutcome.AlreadyErased
    ? ShredAlreadyMessage(req.SubjectId) : ShredUnknownMessage(req.SubjectId) })`.
  - Otherwise `LogAction` exactly as today (`:1427-1431`), then `Ok` with `subject_id`, `status =
    "Crypto-Shredded"` and `message = ShredOkMessage`.
  - The three message builders are `internal static` members appended at the **end** of `Program`.
- **Encrypt lambda** (`:1448`): becomes `async (EncryptRecordRequest req, IEncryptedStorageService storage,
  HttpContext context, CancellationToken ct) =>`, with `await storage.EncryptSubjectRecordAsync(req.SubjectId,
  req.Payload, ct)`. Its `catch (CryptographicException)` is unchanged.
- **Every other lambda is untouched.** Nothing is inserted between lambdas.

---

## Existing tests this breaks, and the exact required change

Each one breaks because of a named decision. **Do not delete any assertion.** Every replacement keeps or
strengthens what the original line guaranteed.

| File:line | Why it breaks | Required change |
|---|---|---|
| `PostQuantumComplianceTests.cs:175-220` | Sync `GetOrCreateSubjectKey` and `CryptoShredSubjectKey` are replaced (Decision 5). | Make the method `async Task`. `:179`: **two** assertions, `new KeyManagementService(null!, vault)` and `new KeyManagementService(provider, null!)`, both `ArgumentNullException`. `:180`: obtain `keyMgmt` from `SubjectKeyTestHost`. `:186` and `:192`: `await keyMgmt.GetOrCreateSubjectKeyAsync(subjectId)`. `:201`: `await Assert.ThrowsAsync<ArgumentException>(() => keyMgmt.GetOrCreateSubjectKeyAsync(""))`. `:204-205`: `Assert.Equal(SubjectErasureOutcome.Erased, await keyMgmt.CryptoShredSubjectKeyAsync(subjectId))`. `:213`: `AlreadyErased`. `:214`: `UnknownSubject`. `:215`: `UnknownSubject` (whitespace keeps its old "not shredded" meaning). Keep `:182-183`, `:187-189`, `:193`, `:196-198`, `:207-210` and `:218-219` unchanged. **Add** after `:210`: `var ex = await Assert.ThrowsAsync<SubjectKeyUnavailableException>(() => keyMgmt.GetOrCreateSubjectKeyAsync(subjectId)); Assert.Equal(SubjectKeyUnavailableReason.Erased, ex.Reason);` |
| `PostQuantumComplianceTests.cs:229-230`, `:311-312`, `:545-546` | Constructor arity. These tests call `GetComplianceStatus`, which now needs a loaded store. | The same two-assertion null check, then `keyMgmt` from `SubjectKeyTestHost.CreateLoaded()` over `_testRoot`. The test methods stay synchronous. |
| `PostQuantumComplianceTests.cs:351-352` | Constructor arity. | As above, with `await CreateLoadedAsync()`. |
| `PostQuantumComplianceTests.cs:391` | `EncryptSubjectRecord` replaced. | `await storage.EncryptSubjectRecordAsync(subjectId, record)`. |
| `PostQuantumComplianceTests.cs:403` | Shred is async. | `Assert.Equal(SubjectErasureOutcome.Erased, await keyMgmt.CryptoShredSubjectKeyAsync(subjectId));` (strengthened: today the return value is ignored). |
| `PostQuantumComplianceTests.cs:404-406` | `Assert.Throws<CryptographicException>` is **exact-type** in xUnit, and the subtype now thrown fails it. | `ThrowsAsync<SubjectKeyUnavailableException>` for encrypt (`:404`) and export (`:406`), `Throws<SubjectKeyUnavailableException>` for decrypt (`:405`). Each asserts `Reason == Erased`. For `:405`, also assert `ex.Message == SubjectKeyUnavailableException.ErasedMessage(subjectId, ex.ShreddedAtUtc!.Value)`, with the justification comment "Erased always carries the timestamp". |
| `PostQuantumComplianceTests.cs:409-410` | Method replaced. | `await Assert.ThrowsAsync<ArgumentException>(...)` and `ThrowsAsync<ArgumentNullException>(...)` over `EncryptSubjectRecordAsync<object>`. |
| `PostQuantumComplianceTests.cs:413` | **This line pinned the conflation.** It accepted any `CryptographicException` for an unknown subject, which is how the false Article 17 message went unnoticed. | `Throws<SubjectKeyUnavailableException>`, then `Assert.Equal(SubjectKeyUnavailableReason.UnknownSubject, ex.Reason)`, `Assert.DoesNotContain("Article 17", ex.Message)`, and `Assert.DoesNotContain("shred", ex.Message, StringComparison.OrdinalIgnoreCase)` and the same for `"destroy"`. |
| `PostQuantumComplianceTests.cs:542`, `:591`, `:596` | `ExecuteCryptoShred` now returns `Task`. | Make the method `async Task`. `await (Task)executeShred.Invoke(page, null)!;` at both sites, with the comment "ExecuteCryptoShred returns Task, so Invoke returns non-null". |
| `AuthenticationServiceTests.cs:81-92` | Constructor arity. | Move `_secretStore = new NodeSecretStore(configuration);` and its comment `:88-92` **above** `:81`. Build `ProfileDataProtector(_secretStore)`, `SubjectKeyRepository(WeaverTestDb.CreateFactory(configuration))`, and a vault. Call `InitializeAsync().GetAwaiter().GetResult()`. Then `new KeyManagementService(_pqcProvider, vault)`. Dispose the vault in `Dispose`. No assertion changes. |
| `RazorComponentLifecycleTests.cs:106-129` | DI cannot build `KeyManagementService`. | After `:106`, add `AddSingleton<ProfileDataProtector>()`, `AddSingleton<ISubjectKeyRepository, SubjectKeyRepository>()` and `AddSingleton<SubjectKeyVault>()` (Stage B: also the journal). After `:129`: `_services.GetRequiredService<SubjectKeyVault>().InitializeAsync().GetAwaiter().GetResult();` |
| `SecurityHardeningAndMigrationsTests.cs:168`, `:175`, `:179` | Migration count 6 becomes 7. | `7`. |
| `SecurityHardeningAndMigrationsTests.cs:183` | The applied set gains 8, without 7 (F13). | `new long[] { 1, 2, 3, 4, 5, 6, 8 }`. |
| `PostgresBackedProfileRepositoryTests.cs:212` | Same. | `[1L, 2L, 3L, 4L, 5L, 6L, 8L]`. **Runs only with `WEAVER_TEST_POSTGRES` set, so it will not fail locally without PostgreSQL. Change it anyway.** |

**Must pass unedited.** If one fails, stop and report. Do not edit it.

- `PostQuantumComplianceTests.ComplianceEndpoints_HttpRequests_ReturnExpectedResponses` (`:425-536`), including
  the 200, 404, 400, 400, 400 and 400 status codes at `:518-535`. Decision 6 keeps them.
- `SystemIntegrationPipelineTests.cs:410-434`.
- `ApiRoleGatingAndDisclosureTests.cs:341-343`, `:648-650` (the routes and their roles do not change).
- `SystemRegressionTests.cs:150-158` (it reads `RegisteredMigrations` dynamically).
- `PostgresBackedRepositoryTests.cs:142-143`, `:349-350` (`version <= 6`).
- `GenderIdentityEncryptionTests.cs`, `PatientPhiEncryptionTests.cs`, `NodeSecretIsolationAndPhiMigrationTests.cs`,
  `NodeSecretStoreTests.cs` in full. `ProfileDataProtector` only gains a member.

Before finishing, run `grep -rn "GetOrCreateSubjectKey\b\|CryptoShredSubjectKey\b\|EncryptSubjectRecord\b\|new KeyManagementService(\|RegisteredMigrations.Count\|5, 6 }\|5L, 6L" src/ tests/`
and account for every hit in the report.

---

## Constraints

1. **Ratchet: replace in place, append what is new.**
   - `EncryptSubjectRecord` becomes `EncryptSubjectRecordAsync` in the **same position**, so
     `<ExportEncryptedDatasetBundleAsync>d__N` keeps its N.
   - The two `Program` lambdas become `async` **in their positions**. A lambda stays a lambda, so no
     `<Main>b__4_N` ordinal shifts.
   - Every genuinely new member goes at the **end** of its class or interface.
   - `KeyManagementService` gains **no** `async` method.
   - A new compiler-generated class must arrive with zero uncovered lines. In async methods, assign results to a
     local and return after `try`/`finally`. Never return from inside a `try` that holds `await using` locals
     (`UserRepository.cs:11-14`).
   - If step 7 reports a renamed class that has uncovered lines, stop and report.
2. **`ISqlDialect` is untouched** (it is capped at seven members). Use `TrueLiteral`, `FalseLiteral`,
   `BooleanValue`, `TimestampValue`, `TimestampValueOrNull` and `WeaverDbValues`. If you believe you need an
   eighth member, stop and report.
3. **No runtime branch on the database engine** in the repository or the vault (F11). One SQL text per operation.
4. **No mocking** of `DbConnection`, `DbCommand`, `DbDataReader` or `NpgsqlConnection`. Fault injection goes
   through `FaultInjectingSubjectKeyRepository` wrapping the real repository only. Use the real
   `PostQuantumCryptoProvider`, `ProfileDataProtector` and `NodeSecretStore`. No Moq for any type in this package.
5. **Isolation (Directive 6).**
   - Every test gets its own `Path.GetTempPath()` root, set through `TRAJECTORY_INCOMING_DIR` and
     `WEAVER_NODE_KEYS_DIR`.
   - Nothing under `simulations/`. No ambient network. No sleeps. No wall-clock value pinned. Timestamps are
     asserted between `before` and `after` bounds that the test captures.
   - The vault truncates every timestamp it persists to whole microseconds, `ticks - ticks % 10`, so values from
     memory equal values reloaded from either engine.
   - Before copying or deleting a SQLite file, call `SqliteConnection.ClearAllPools()` (precedent:
     `PostgresBackedRepositoryTests.cs:221`).
   - After the full run, `git status` shows nothing new under `simulations/`.
6. **PostgreSQL twins.** These use `[Theory] [InlineData(false)] [InlineData(true)]` with an explicit early return
   when `postgres && !_postgres.IsAvailable`, in the style of `GenderIdentityEncryptionTests.cs:125-133`.
   **Flagged: every `InlineData(true)` case silently does not run without `WEAVER_TEST_POSTGRES`.** These are
   A3, A4, A5, A8, A11, A15 and B2. `Assert.Skip` does not compile against xUnit 2.9.2.
7. **Directives 2, 3 and 4, and gate step 6.**
   - A full docblock on every new or changed member, private ones included.
   - WHAT/WHY/HOW comments on load, seal/open, create, erase, reconciliation, journal append/read and the
     `Program` block.
   - The `SubjectKeyVault` remarks explain, in plain English: what sealing is; why erasure clears the column and
     compacts the database; and what backups still contain (Decisions 7 and 8).
8. **Coverage.** Step 4 is exactly 100.00% line and branch, with `WEAVER_TEST_POSTGRES` unset. If step 7 reports the
   baseline **stale**, run `python3 -I scripts/check-coverage-ratchet.py artifacts/coverage-full coverage-baseline.json --update`,
   include the result, and quote the old and new `line_rate`, `branch_rate` and `uncovered_lines_total`. Never
   pass `--allow-regression`. Do not change either runsettings file.
9. **Directive 14.** No message, log line, `SecureProblemDetails` detail, response body or journal line contains a
   private key, a sealed value, a path, a directory name, SQL or connection details.
10. **No LINQ, `Regex` or `Split` in new production code** (in the spirit of Directive 1). Use `foreach` and
    `IndexOf`.
11. **PKI.** ML-KEM-768 for subject keys. No ECDSA and no RSA anywhere, including comments and test names.
12. **`!`** only with an inline justification. Zero warnings. `dotnet format NanoWeaverHrt.slnx --verify-no-changes`
    is clean.
13. Finish each stage with `./scripts/verify-all.sh`, run **once with `WEAVER_TEST_POSTGRES` set**, and report all
    seven step results verbatim. If no PostgreSQL is available to you, say so explicitly. In that case the
    PostgreSQL twins listed in Constraint 6 have **not** been exercised, and the report must not claim they were.

---

## Acceptance criteria

### Stage A (in `SubjectKeyPersistenceTests.cs` unless stated)

**Schema and repository**

- **A1.** `Migration008_SubjectKeyStore`:
  - `Version == 8` and `Name == "008_SubjectKeyStore"`.
  - `GetSql("sqlite")` and `GetSql("postgresql")` each contain `CREATE TABLE IF NOT EXISTS weaver_subject_keys` and
    `ck_weaver_subject_keys_state`. The PostgreSQL text also contains `BOOLEAN` and `TIMESTAMPTZ`.
  - Neither text contains `REPLACE`, `DELETE` or `TRIGGER` (case-insensitive).
  - `GetSql(null!)` throws `ArgumentNullException`.
- **A2.** `new DatabaseMigrationRunner().RegisteredMigrations` has 7 entries, the last being
  `Migration008_SubjectKeyStore`. A fresh SQLite database records `{1,2,3,4,5,6,8}`. A second run applies 0.
- **A3.** On a migrated database (twin), each of these raw statements fails with the driver's constraint exception:
  - inserting an active row with `sealed_private_key` NULL;
  - inserting a shredded row with `sealed_private_key` non-NULL;
  - `UPDATE ... SET is_shredded = 0` (or `FALSE`) on a tombstone.
- **A4.** The no-resurrection pin (twin):
  - `TryInsertActiveAsync` returns `true`, then `false` for the same key, and the stored row is unchanged.
  - With a tombstone already stored, `TryInsertActiveAsync` for that key returns `false`, and the row is still a
    tombstone with `sealed_private_key` and `public_key_base64` NULL.
- **A5.** `MarkShreddedAsync` (twin):
  - Returns `true` on an active row and `false` on a tombstone and on an absent key.
  - After `true`, the row is a tombstone whose `shredded_at_utc` equals the argument.
- **A6.** `GetRequiredAsync` on an absent key throws `InvalidOperationException` with exactly `RowMissingMessage`.
  `ListAllAsync` returns rows ordered by `subject_key`. The constructor rejects null.
- **A7.** Source pin: `SubjectKeyRepository.cs` contains none of `OR REPLACE`, `DO UPDATE` or `DELETE`
  (case-insensitive), and contains the literal `"VACUUM"` exactly once.
- **A8.** The repository round-trips every column, null and non-null, on both engines (twin).

**Sealing at rest**

- **A9.** After creating subject `S`:
  - its `sealed_private_key` starts with `data:application/encrypted;base64,`;
  - it does not contain the private key Base64, or any 32-character substring of it;
  - the bytes of `weaver_config.db`, read after `ClearAllPools`, do not contain the private key Base64.
- **A10.** `DecryptRequiredFieldWithCurrentKeyOnly`:
  - round-trips `EncryptRequiredField`;
  - throws `CryptographicException` for an envelope sealed with the **legacy** key (build it with that hex
    constant, as `NodeSecretIsolationAndPhiMigrationTests.cs` does), and `LegacyDecryptionCount` stays 0;
  - throws `CryptographicException` for plaintext, for `""`, for bad Base64 after the prefix, and for a payload
    under 28 bytes;
  - throws `ArgumentNullException` for null;
  - never includes its input in a message.

**Persistence and restart**

- **A11.** Restart survival, unit level (twin):
  - Vault 1 creates `S` and encrypts with the provider.
  - `Reopen()` gives vault 2, whose report has `ActiveCount == 1`. Its `Find(S)` returns the same public and
    private key, and the envelope decrypts.
- **A12.** Restart survival, host level:
  - Host 1 (`WebApplicationFactory<Program>` over a temp root) calls `encrypt-record` and `export-encrypted` for
    `S`, then is disposed.
  - Host 2 over the same root: `decrypt-record` of host 1's envelope returns 200 with the original payload.
  - The `.enc.tar.gz` from host 1, deserialized as `EncryptedEnvelope` and decrypted with
    `GetSubjectPrivateKeyForDecryption(S)` from a vault reopened over that root, gunzips to bytes equal to the
    source trajectory file.
- **A13.** Case-insensitivity: `GetOrCreateAsync("Sub_X")`, then `GetOrCreateAsync("sub_x")`, return the same public
  key, before and after `Reopen()`. The stored `subject_key` is `"SUB_X"` and `subject_id` is `"Sub_X"`.

**Erasure**

- **A14.** `EraseAsync(S)` returns `Erased`. Then:
  - `Find(S)` has `IsShredded == true`, `PrivateKeyBase64 == null`, `PublicKeyBase64 == ""`, and `ShreddedAtUtc`
    between the bounds;
  - the database row is a tombstone;
  - a second call returns `AlreadyErased`;
  - `EraseAsync("never_seen")` and `EraseAsync("  ")` return `UnknownSubject` and write nothing.
- **A15.** Erasure survives restart (twin). After `Reopen()`:
  - `ErasedCount == 1`;
  - `Find(S)` is a tombstone;
  - `GetOrCreateAsync(S)` throws `SubjectKeyUnavailableException` with `Reason == Erased`;
  - the row count and every column of every row are identical before and after that call, so no key was minted.
- **A16.** After `EraseAsync(S)` on SQLite:
  - the bytes of `weaver_config.db` (after `ClearAllPools`) contain neither `S`'s former sealed value nor its
    private key Base64;
  - no `weaver_config.db-journal` file exists.

  **If this fails, stop and report. Do not weaken it.**
- **A17.** With `MarkShreddedAsync` set to throw:
  - `EraseAsync(S)` propagates that exception;
  - `Find(S)` is still active, and decryption still works;
  - after the fault is cleared, `EraseAsync(S)` returns `Erased`.
- **A18.** With `CompactStorageAsync` set to throw:
  - `EraseAsync(S)` propagates that exception;
  - `Find(S)` is a tombstone, and the row is a tombstone;
  - after `Reopen()`, `ErasedCount == 1`.
- **A19.** With `TryInsertActiveAsync` set to throw:
  - `GetOrCreateAsync(S)` propagates, `Find(S)` is null, and no row exists;
  - `EncryptSubjectRecordAsync(S, ...)` propagates the same exception and produces no envelope.
- **A20.** Create race, simulated deterministically:
  - After load, insert an active row for `T` directly through the repository. `GetOrCreateAsync(T)` then returns
    **that row's** public key, not a fresh one.
  - The same with a tombstone row for `U`: `GetOrCreateAsync(U)` throws `Reason == Erased`.
  - A row for `V` sealed under a foreign PHI key: `GetOrCreateAsync(V)` throws `SubjectKeyStoreLoadException`.
- **A21.** After load, delete `weaver_config.db` (after `ClearAllPools`). `EraseAsync(S)` for a cached active `S` then
  throws `InvalidOperationException` with exactly `ExternalChangeMessage`, and `Find(S)` is still active.

**Fail-loud loading**

- **A22.** A row sealed under a different PHI key (a second `NodeSecretStore` directory):
  - `InitializeAsync` throws `SubjectKeyStoreLoadException` with exactly `LoadFailureMessage(1, 1)`;
  - the message contains neither the temp root path nor the sealed value;
  - `IsLoaded` is false, and `Find("x")` throws `InvalidOperationException(NotLoadedMessage)`.
  - A host over that root fails to start, with `SubjectKeyStoreLoadException` in the exception chain.
- **A23.** Each of these makes `InitializeAsync` throw `SubjectKeyStoreLoadException`:
  - (a) the sealed values of two rows swapped with raw SQL (message `LoadFailureMessage(2, 2)`);
  - (b) `public_key_base64` replaced with another valid ML-KEM-768 public key;
  - (c) a value sealed with the legacy key;
  - (d) a correctly sealed plaintext with a wrong prefix;
  - (e) `algorithm` changed to `"ML-KEM-1024"`.
- **A24.** After every A22 and A23 case, every row is byte-identical to its state before `InitializeAsync`, and the
  row count is unchanged. Nothing is regenerated.
- **A25.** With an active row present, delete `phi_master.key`, then build a fresh `NodeSecretStore` (which
  generates a new key) and vault. `InitializeAsync` throws `SubjectKeyStoreLoadException`.
- **A26.** Before `InitializeAsync`, these throw `InvalidOperationException(NotLoadedMessage)` for a non-empty id:
  `Find`, `ListWithoutPrivateKeys`, `GetOrCreateAsync`, `EraseAsync` and `GetPrivateKeyForDecryption`. A second
  `InitializeAsync` throws `InvalidOperationException(AlreadyLoadedMessage)`.

**Error model**

- **A27.** `DecryptSubjectRecord` for an unknown subject throws `Reason == UnknownSubject`, and
  `Message == UnknownSubjectMessage(id)`.
- **A28.** For an erased subject, encrypt, decrypt and export each throw `Reason == Erased`, with `Message ==
  ErasedMessage(id, ShreddedAtUtc)`.
- **A29.** For `id = "sub_0123abcd"`, `SecureExceptionSanitizer.SanitizeDetail(m, 400, "t") == m` for each of:
  `ErasedMessage`, `UnknownSubjectMessage`, `ExternalChangeMessage`, `ShredOkMessage`, `ShredAlreadyMessage` and
  `ShredUnknownMessage`.
- **A30.** Over HTTP, as Administrator:
  - `decrypt-record` for an unknown subject returns 400, with problem-details `detail ==
    UnknownSubjectMessage(id)`;
  - after erasure it returns 400 with `detail == ErasedMessage(...)`;
  - `crypto-shred` returns 200 with `message == ShredOkMessage`;
  - a repeat returns 404 with `error == ShredAlreadyMessage(id)`;
  - an unknown id returns 404 with `error == ShredUnknownMessage(id)`.
- **A31.** `grep -rn` over `src/` finds none of:
  - `Private key has been permanently destroyed`;
  - `Profile was permanently crypto-shredded`;
  - `Subject has been crypto-shredded.`;
  - `permanently destroyed pursuant to GDPR Article 17`;
  - `Private decryption key was purged`.

**Exposure**

- **A32.** `GetAllSubjectProfiles()` returns `PrivateKeyBase64 == null` for every profile, active ones included.
  None of the following contain any subject's private key Base64 or sealed value:
  - the response bodies of `GET /api/compliance/status`, `encrypt-record`, `crypto-shred` and `export-encrypted`;
  - the captured host log.
- **A33.** The host log contains exactly one line matching
  `Subject key store loaded. Active subject keys: <n>. Crypto-shredded subjects: <m>.`, with no `/` in it.
  Capture it with an `ILoggerProvider` registered through `WithWebHostBuilder` `ConfigureLogging`.

**Gate**

- **A34.** Every test listed under "Must pass unedited" passes. The edits to existing test files are limited to the
  lines named above, plus any helper appended at the end of a test class.
- **A35.** `./scripts/verify-all.sh` passes 7/7. Step 4 is exactly 100.00%/100.00%, with 0 warnings and 0
  incomplete docblocks. In step 7 no class's uncovered count rises.

### Stage B (in `SubjectErasureJournalTests.cs`)

- **B1.** With `MarkShreddedAsync` set to throw:
  - `EraseAsync(S)` propagates the exception;
  - the journal has one entry for `S`;
  - `Find(S)` is still **active**, consistent with A17.
  - `Reopen()` then reports `ReappliedErasureCount == 1`, `Find(S)` is a tombstone, and the row is a tombstone.
- **B2.** Restoring a backup taken before the erasure (twin; on PostgreSQL the "backup" is a captured copy of the
  row, written back with raw SQL):
  1. Create `S` and copy the database.
  2. Erase `S`, then restore the copy.
  3. `Reopen()` gives `ReappliedErasureCount == 1`, a tombstone row with a NULL sealed value, and
     `GetOrCreateAsync(S)` throwing `Erased`.
- **B3.** Restoring a backup taken before the subject existed:
  1. Copy the database, then create `S` and erase `S`.
  2. Restore the copy and `Reopen()`.
  3. The report has `RebuiltTombstoneCount == 1`. `Find(S)` is a tombstone with `SubjectId == "S"` normalized,
     and `GetOrCreateAsync(S)` throws `Erased`.
- **B4.** A Stage A database with two tombstones and no journal: `InitializeAsync` gives `JournalBackfillCount == 2`,
  and the journal has two entries. The next `Reopen()` gives `JournalBackfillCount == 0`.
- **B5.** Journal file:
  - mode `UserRead | UserWrite`, set at creation;
  - first line exactly `weaver-subject-erasure-journal v1`;
  - entries exactly `E\t<b64>\t<O>`;
  - two entries for the same key are reconciled once, using the earlier timestamp.
- **B6.** Each of these makes `InitializeAsync` throw `SubjectKeyStoreLoadException`: a wrong header, a bad line tag,
  bad Base64, a bad timestamp, and a final line without `\n`. The message names `subject_erasures.journal` and the
  line number. It does not contain the directory. Every database row is byte-identical afterwards.
- **B7.** No journal and an empty database give an all-zero report, and no journal file is created by loading.
- **B8.** Host-level restore:
  1. Host 1 encrypts for `S`; dispose it and copy `weaver_config.db`.
  2. Host 2 crypto-shreds `S`; dispose it and restore the copy.
  3. Host 3: `encrypt-record` for `S` returns 400 with `detail == ErasedMessage(...)`.
  4. Host 3's log contains exactly one line matching the Stage B Warning, with `1` re-applied. Hosts 1 and 2 log
     no such Warning.
- **B9.** Reconciliation runs before opening: a journalled subject whose row is sealed under a foreign PHI key does
  **not** block the start. It is re-applied, and its sealed value becomes NULL.
- **B10.** The journal bytes never contain a private key Base64, a sealed value, or the temp root path.
- **B11.** Every Stage A criterion still passes unedited.

---

## Non-goals

- **Multiple processes writing one PostgreSQL database.** The cache is per process. A second process would not see
  another process's erasure until it restarts. Single-writer is the supported deployment. This is also why
  A20 and A21 handle external changes by failing loudly, not by trying to stay coherent.
- PHI master key rotation (Decision 2), a key escrow tool, and changing where `phi_master.key` lives by default.
- Purging PostgreSQL WAL, archives or backups. Zeroing private keys in managed memory.
- Recovering anything encrypted before this package lands. Those keys were never stored, and nothing can bring
  them back.
- A key fingerprint inside `EncryptedEnvelope`. An envelope made under an older, unpersisted key for a subject who
  now has a key still fails with the provider's authentication error. That is accurate, but it is not specific.
- Changing pseudonym derivation (F12). Tombstoning an unknown subject on erasure request. Docs (`docs/` is out of
  scope).

---

## Coordination with other packages (report; do not edit them)

1. **P7.5 (`node-signing-key-persistence.md`) is stale after this package and needs amending before it starts:**
   - `KeyManagementService`'s constructor will already be `(IPostQuantumCryptoProvider, SubjectKeyVault)`. P7.5's
     `(IPostQuantumCryptoProvider, INodeSecretStore)` becomes three parameters.
   - Its interface listing shows `GetOrCreateSubjectKey` and `CryptoShredSubjectKey` as "unchanged". They are now
     async, and `GetSubjectPrivateKeyForDecryption` is the last member.
   - Its test edit table (`PostQuantumComplianceTests.cs:179/180/...`, `AuthenticationServiceTests.cs:82`) points
     at lines this package moves.
   - Its "Defects found outside this package", item 1, is resolved here.
   - Its Constraint 1 reasoning still holds, because this package adds **no** async method to
     `KeyManagementService`.
   - Its `Program.cs` insertion point (after `:220`) does not collide with this one (after `:245`).
2. **P9 (`peer-key-registry.md`)** says to change the migration counts `6` to `7` and the list `{1..6}` to `{1..7}`.
   After this package they are `7` to `8` and `{1,2,3,4,5,6,8}` to `{1..8}`.
3. Storage split, for the record: the node signing key goes in a file (a 32-byte seed fits `NodeSecretStore`).
   Subject keys go in the database (Decision 1). The erasure journal goes in a file, only to survive a database
   restore (Decision 9).

## Open questions (non-blocking; defaults stated)

- **Permanent non-reissue.** Because pseudonyms are deterministic (F12), an erased person who later re-consents
  gets the same pseudonym and is refused a key forever. Default: refuse. A re-enrollment path is a separate policy
  decision.
- **Erasure request for an unknown subject.** Default: `UnknownSubject`, and nothing is stored (this keeps
  `PostQuantumComplianceTests.cs:214`). The alternative, a pre-emptive tombstone, would block a typo'd id forever.
- **Whole-database `VACUUM` on large PostgreSQL deployments.** Default: accept it, since erasures are rare.

---

## Criteria consistency check

I re-read A1-A35 and B1-B11 as a set and checked every pair. Findings:

1. **"Keys persist" (A11, A12, A13) versus "erasure is irreversible" (A14, A15, A16). Consistent.**
   - Only active rows carry key material, and the `CHECK` constraint makes a shredded row with key material
     unstorable (A3).
   - The only write that touches an existing row is the active-to-tombstone `UPDATE`. No statement can turn a
     tombstone back (A4, A7).
   - Restart reloads the tombstone (A15).
   - Persistence never outranks erasure. In Stage B the precedence is explicit: a journalled erasure beats an
     active database row (B2).
2. **A16 ("the database file no longer contains the erased key") versus Decision 8 ("backups taken earlier still
   do").** These are about different files at different times. No criterion claims old backups are cleaned, and
   `ShredOkMessage` says they are not.
3. **A17 (a failed `UPDATE` leaves the cache active) versus B1 (the journal is written before the `UPDATE`). Real
   tension, resolved.** These would conflict if Stage B tombstoned the cache at the journal append. I did not
   design it that way. In **both** stages the cache becomes a tombstone only after the database `UPDATE`
   succeeds. B1 asserts the cache is still active, which is A17's guarantee. Stage B adds only that the pending
   erasure is completed at the next start.
   - The accepted consequence: in Stage B, an operator who saw an erasure fail will find it completed after a
     restart. That is the correct legal direction, and it is written in the docblock.
   - If the coordinator prefers that a failed erasure leave no trace, the journal append has to move after the
     `UPDATE`. That reopens the crash window in which a restore silently undoes an erasure. **I recommend
     against it.**
4. **A18 (a compaction failure propagates, yet the erasure is committed) versus fail-loud.** Both hold. The
   exception is loud, and the erasure is durable. A retry reports `AlreadyErased`, which is true.
5. **A22 (any row that cannot be opened aborts the start) versus B9 (a journalled row that cannot be opened does
   not).** Consistent, because reconciliation runs before rows are opened, and a re-applied row is no longer
   active. The A22 and A23 fixtures must not journal their rows. Stage B must keep A22 and A23 passing
   **unedited** (B11).
6. **A14 (`EraseAsync("  ")` returns `UnknownSubject`) and the existing `GetSubjectKey("") == null` versus A26 ("not
   loaded" throws).** These would conflict if the loaded check ran first. Resolution: whitespace is handled
   **before** the loaded check in `Find` and `EraseAsync`, as the signatures state. A26 uses non-empty ids only.
7. **A20 (a race against a pre-inserted tombstone gives `Erased`) versus A15 (no key minted for an erased
   subject).** These are the same guarantee by two routes, cache and database. Consistent.
8. **A30 and the unedited `:518-535` (status codes unchanged) versus Decision 6 (distinct outcomes).** The outcomes
   are distinguished by exception type, `Reason`, and the exact `detail` and `error` text, **not** by status code.
   So the pinned codes stay. Using 410 Gone for erased subjects was considered and rejected, because it would
   break pinned guarantees in an existing test.
9. **A25 (a regenerated PHI key aborts the start) versus P7.5 Tension A (a missing key file is treated as first
   launch).** Both hold. `NodeSecretStore` still generates the key, and the vault turns that into a failed start
   whenever subject keys exist. Flagged: the regenerated `phi_master.key` stays on disk, and the operator must put
   the original back. `LoadFailureMessage` says so.
10. **A2 and the migration count edits (7 migrations) versus P9 (also edits them).** This is between packages, not
    within this one. Flagged under Coordination, item 2.
11. **Decision 8's remaining limit (restoring the node-keys directory together with the database undoes an
    erasure) versus any criterion.** I checked that no criterion claims erasure survives a restore of **both**. B2,
    B3 and B8 restore the database only.

No other pair conflicts. **No existing assertion is removed.** `PostQuantumComplianceTests.cs:413` and `:404-406`
are strengthened from "any `CryptographicException`" to a specific reason and message. That loose assertion is
how the false Article 17 claim went unnoticed.
