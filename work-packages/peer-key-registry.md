# Work package P9 — peer key registry, administrator endpoints, import enforcement, docs

> **Why this file is not under `docs/`.** Gate step 5 scans `docs/**/*.md` and `README.md` for paired
> single-dollar tokens. Work packages are not documentation. **Do not move this file under `docs/`.**

> ## Coordinator rulings (2026-10-23) — read before implementing
>
> - **Base commit is now `ce94d26`**, not `6a00fdb`. The three commits since are CI-workflow and
>   comment changes only (`platforms: linux/amd64`, `runs-on: archlinux-multi-platform`). No `.cs`,
>   migration, test or `docs/` file changed, so this design is unaffected.
> - **Self-trust: ACCEPTED** (P9 tension A, option 1). The node's own key is in the trust set, and
>   **the signature is still verified in full** — self-trust is never a verification bypass.
> - **`INSERT OR REPLACE`: criterion 8 STANDS** (P9 tension H). Directive 13 prescribes that idiom
>   for migration *version recording*, which is unchanged here. Peer-key inserts use
>   `ON CONFLICT DO NOTHING` on both engines; `INSERT OR REPLACE` would resurrect revoked keys.
> - **A package on node signing key persistence lands BEFORE this one.** The ML-DSA-65 node key is
>   regenerated on every process start (`KeyManagementService.cs:57`, singleton) and signs the Part 11
>   audit ledger. Expect node key storage to have changed; re-read it rather than assuming.
> - **Migration number is `007`.** PHASES-v2.md (`006`) and the brief (`Migration005`) are both stale.

**Agent:** coder. **Base commit: `6a00fdb`** (plus one pending comments-only commit), **with P8
merged on top**. Your worktree branches from the session-start commit, so your FIRST action is
`git rebase main`. Confirm that `src/NanoWeaverHrt/Security/Signing/` exists, which means P8 is
present. If it does not exist, stop and report. Do not commit. Do not push.

**Dependencies:** P8 (`work-packages/dataset-signing.md`). Read its *Interfaces and signatures*
section first: `DatasetManifestSignature`, `DatasetManifestCanonicalizer`, `INodeDatasetSigner` and
`DatasetManifestSignatureVerifier` are the contract you build on. Do not change them. If you need a
change, stop and report.

**Sizing.** Three ordered stages. Each one leaves the tree green at 7/7, so stopping between stages is
safe. **Stopping rule:** if the diff exceeds roughly 700 changed lines at the end of stage B, stop,
report, and let the parent dispatch stage C (docs) as its own pass.

- **Stage A:** migration 007, `PeerKeyRepository`, migration-count test updates.
- **Stage B:** strict-mode setting, `VerifiedDatasetImporter`, the import endpoint, the three admin
  endpoints and the route-table entry.
- **Stage C:** docs and the `AGENTS.md` §2 correction.

---

## Goal

Make `POST /api/datasets/import` authenticate *who* produced a bundle, not just whether it arrived
intact, under the enforcement table the user decided:

| bundle state | result |
|---|---|
| unsigned, strict mode OFF (default) | imported, exactly as today |
| unsigned, strict mode ON | **403** |
| signed, signer trusted, signature valid | imported |
| signed, signer **not** trusted | **403** |
| signed, signature invalid or signature block malformed | **400** |

"Trusted" means **either** an active (unrevoked) row in the new `weaver_peer_keys` table, **or**
this node's own dataset signing key (`INodeDatasetSigner.KeyId`, from P8). The second clause is
explained in the Criteria consistency check, item A.

> **This is not `weaver_share_keys`.** `weaver_share_keys` (Migration001,
> `SystemConfigurationStore.CreateShareKeyAsync` / `ConsumeShareKeyAsync`) holds **single-use random
> tokens** that link a remote browser into the collaborative UI. Each is consumed once and expires.
> It holds no public key, has no algorithm column, and its semantics (consume-once, expiry) are
> wrong for a long-lived trust anchor. **Do not add columns to it, do not store peer keys in it, do
> not reuse its repository methods, and do not route the new endpoints under `/api/share-keys`.** The
> peer key registry is a new table, a new repository and a new route prefix.

---

## Files in scope

### Stage A

| Path | Role |
|---|---|
| `src/NanoWeaverHrt/Database/Migrations/Migration007_PeerKeyRegistry.cs` (new) | DDL only, both dialects. `Version => 7`, `Name => "007_PeerKeyRegistry"`. |
| `src/NanoWeaverHrt/Database/Migrations/DatabaseMigrationRunner.cs` | Append `new Migration007_PeerKeyRegistry()` after `Migration006_EncryptGenderIdentity(protector)` (`:46`). Change nothing else. |
| `src/NanoWeaverHrt/Storage/IPeerKeyRegistry.cs` (new) | Repository contract. |
| `src/NanoWeaverHrt/Storage/PeerKeyRepository.cs` (new) | On the `IWeaverDbConnectionFactory` seam. Names no driver type. |
| `src/NanoWeaverHrt/Models/PeerKeyModels.cs` (new) | `PeerKeyRecord`, `RegisterPeerKeyRequest`, `LocalDatasetSigningKey`, `PeerKeyListResponse`, each with `[SwaggerSchema]` and `IExamplesProvider<T>` per house style (see `DatasetManifest.cs`). |
| `tests/NanoWeaverHrt.Tests/SecurityHardeningAndMigrationsTests.cs` | **Edit `:168`, `:175`, `:179`, `:183`.** Change the counts `6` to `7` and the version list `{1..6}` to `{1..7}`. This is required: the test pins the exact migration count. It is not one of the five protected files. |
| `tests/NanoWeaverHrt.Tests/PostgresBackedProfileRepositoryTests.cs` | **Edit `:212`**: `[1L..6L]` becomes `[1L..7L]`. It runs only with `WEAVER_TEST_POSTGRES` set, so it **will not fail locally without PostgreSQL. Change it anyway.** |
| `tests/NanoWeaverHrt.Tests/PeerKeyRegistryTests.cs` (new) | Migration and repository tests on SQLite, plus real-PostgreSQL tests under the existing guard. |

The two assertions in `PostgresBackedRepositoryTests.cs:143,350` count `version <= 6` and stay
correct. Do not touch them. Before finishing stage A, run
`grep -rn "RegisteredMigrations.Count\|5L, 6L\|5, 6 }" tests/` and account for every hit in the report.

### Stage B

| Path | Role |
|---|---|
| `src/NanoWeaverHrt/Configuration/SystemConfigurationStore.cs` | Append **one** `Definitions` entry (see decision 6). Change nothing else in the file. |
| `src/NanoWeaverHrt/Security/Signing/PeerKeyValidation.cs` (new) | Static, pure. Label and public-key normalization. Visible to step 4. |
| `src/NanoWeaverHrt/Services/DatasetImportResult.cs` (new) | `DatasetImportOutcome` enum and `DatasetImportResult` record. |
| `src/NanoWeaverHrt/Services/VerifiedDatasetImporter.cs` (new) | The single place where provenance is decided, followed by the integrity import. |
| `src/NanoWeaverHrt/Services/DatasetTransferService.cs` | **One change inside `ImportDatasetAsync`:** a size-mismatch `return false` after the hash comparison. No new method. Nothing else. |
| `src/NanoWeaverHrt/Abstractions/IDatasetManager.cs` | Docblock only on `ImportDatasetAsync`: it checks integrity only, its one production caller is `VerifiedDatasetImporter`, and it must not be called from an endpoint directly. |
| `src/NanoWeaverHrt/Program.cs` | Two DI lines, the modified import endpoint, three new endpoints appended at the end of `Main`, and one route-table entry. |
| `tests/NanoWeaverHrt.Tests/DatasetSignatureEnforcementTests.cs` (new) | The enforcement table, end to end, plus the `VerifiedDatasetImporter` unit tests. |
| `tests/NanoWeaverHrt.Tests/PeerKeyEndpointTests.cs` (new) | Register/list/revoke, role gating, disclosure. |

### Stage C

| Path | Role |
|---|---|
| `AGENTS.md` | **§2 only, line `:127`.** It is prose, not a Directive. See decision 8. **The 14 Prime Directives are out of scope; do not edit any of them.** |
| `docs/api-reference.md` | Import section (`:462` onward): the new 403 and 400 causes. Wording at `:403`, `:429`, `:455` ("signed"). A new "Peer Key Registry" section for the three endpoints. |
| `docs/trajectories-and-datasets.md` | §5 (`:116-150`): rewrite "Cryptographically Signed Bundles" to say what is true now. |
| `docs/configuration-peers.md` | The new setting, a peer key registry how-to, and a correction of the "signed DNA origami manifests" clause at `:113`. |
| `docs/getting-started.md` | The node-keys table (`:142`): add a `dataset_signing_ed25519.key` row stating the consequence of losing it. `:281`: replace "SHA-256 signed" with accurate wording. |

---

## Decision 3 — migration 007 and `weaver_peer_keys`

**Number: 007.** `PHASES-v2.md` (P9 scope) says `006`. That is stale.
`src/NanoWeaverHrt/Database/Migrations/` already contains `Migration001` through
`Migration006_EncryptGenderIdentity`. The class is **`Migration007_PeerKeyRegistry`**, following that
directory's `MigrationNNN_PascalCaseSummary` pattern, with `Version => 7` and
`Name => "007_PeerKeyRegistry"`. It takes no constructor parameter: there are no rows to transform.

`GetSql(dialect)` follows `Migration004`'s shape: `ArgumentNullException.ThrowIfNull(dialect)`, then
`dialect.StartsWith("postgres", StringComparison.OrdinalIgnoreCase)`.

**PostgreSQL:**

```sql
CREATE TABLE IF NOT EXISTS weaver_peer_keys (
    key_id            VARCHAR(64)  PRIMARY KEY,
    label             VARCHAR(128) NOT NULL,
    key_algorithm     VARCHAR(16)  NOT NULL,
    public_key        TEXT         NOT NULL,
    registered_by     VARCHAR(128) NOT NULL,
    registered_at_utc TIMESTAMPTZ  NOT NULL,
    revoked_by        VARCHAR(128),
    revoked_at_utc    TIMESTAMPTZ,
    CONSTRAINT ck_weaver_peer_keys_key_id_length CHECK (length(key_id) = 64),
    CONSTRAINT ck_weaver_peer_keys_algorithm CHECK (key_algorithm IN ('Ed25519', 'MlDsa65')),
    CONSTRAINT ck_weaver_peer_keys_revocation_pair CHECK ((revoked_by IS NULL) = (revoked_at_utc IS NULL))
);
```

**SQLite:**

```sql
CREATE TABLE IF NOT EXISTS weaver_peer_keys (
    key_id            TEXT PRIMARY KEY,
    label             TEXT NOT NULL,
    key_algorithm     TEXT NOT NULL,
    public_key        TEXT NOT NULL,
    registered_by     TEXT NOT NULL,
    registered_at_utc TEXT NOT NULL,
    revoked_by        TEXT,
    revoked_at_utc    TEXT,
    CONSTRAINT ck_weaver_peer_keys_key_id_length CHECK (length(key_id) = 64),
    CONSTRAINT ck_weaver_peer_keys_algorithm CHECK (key_algorithm IN ('Ed25519', 'MlDsa65')),
    CONSTRAINT ck_weaver_peer_keys_revocation_pair CHECK ((revoked_by IS NULL) = (revoked_at_utc IS NULL))
);
```

Column decisions:

- **`key_id`** is the primary key: lowercase hex SHA-256 of the SPKI DER, computed **by the server**
  with P8's `DatasetManifestSignature.ComputeKeyId`. A client never supplies it. Because it is a hash
  of the key, the primary key alone also guarantees that the same public key cannot be registered
  twice. No second unique index is needed.
- **`public_key`** is canonical Base64 (`Convert.ToBase64String`) of the decoded DER, re-encoded on
  the way in, so two spellings of one key cannot coexist. It is `TEXT` because an ML-DSA-65 SPKI is
  about 1.97 KB.
- **Revocation** is the pair `revoked_at_utc` + `revoked_by`, both NULL while the key is active and
  both set once it is revoked (enforced by `ck_..._revocation_pair`). Active means
  `revoked_at_utc IS NULL`. **No boolean column**, so none of the four existing boolean-literal
  pitfalls applies. **Revocation is permanent:** a revoked row is never deleted or reactivated, so the
  audit trail of who trusted what, and when, survives.
- **`registered_by` / `revoked_by`** hold the Administrator's username from the validated session.
  **No foreign key to `weaver_users`**, on purpose: `ON DELETE CASCADE` would silently delete trust
  history when an admin account is removed, and `SET NULL` contradicts `NOT NULL`.
- **`TIMESTAMPTZ`, not the older tables' `TIMESTAMP`.** `PostgreSqlDialect.TimestampValue` binds a
  `DateTime` of `Kind = Utc`, which Npgsql types as `timestamptz`. Writing that into a `TIMESTAMP`
  column relies on an assignment cast that depends on the session `TimeZone`. A new table should not
  inherit that. `WeaverDbValues.ReadTimestamp` already handles the `DateTime` that comes back.
- No index beyond the primary key. The table holds tens of rows.

**Directive 13 and upserts — read this, it departs from the brief's wording.** Directive 13 names
`INSERT OR REPLACE` (SQLite) and `ON CONFLICT DO NOTHING` (PostgreSQL). Those are the forms
`DatabaseMigrationRunner.cs:172-173` already uses to record the version row, and the runner records
version 7 that way with no change from you. Migration 007 itself is **DDL only** and contains no
`INSERT`. The registry's *runtime* insert uses `INSERT ... ON CONFLICT (key_id) DO NOTHING` on **both**
engines, as `UserRepository.TryCreateUserAsync` (`:169-177`) does. **`INSERT OR REPLACE` is forbidden
on `weaver_peer_keys`:** it deletes and re-inserts the conflicting row, which would **silently
reactivate a revoked key** and erase who revoked it. A test proves re-registration of a revoked key
fails (criterion 8).

**`ISqlDialect`:** the repository uses only `TimestampValue` and `TimestampValueOrNull`. No new
member is needed. The seven-member cap stands.

---

## Decision 4 — Administrator endpoints and the route-table entry

### Route-table entry — exactly this, in exactly this place

Append as the **last element** of `AdministratorRoutes` (`Program.cs:3153-3164`, currently ending
with `new("/api/compliance/audit-ledger", MatchDescendants: false)`):

```csharp
new("/api/peer-keys", MatchDescendants: true)
```

with a comment saying that trust anchors are cluster oversight under Directive 12. Why this table
and this flag:

- `AdministratorRoutes` is tested **regardless of verb** (`ResolveRouteRoleRequirement`, `:3270`), so
  `GET /api/peer-keys` is gated too, not only the mutations. The list contains this node's public key
  and the trust policy, which is Administrator business.
- `MatchDescendants: true` covers `/api/peer-keys/{keyId}/revoke`. Matching uses
  `PathString.StartsWithSegments`, which is segment-exact, so `/api/peer-keysx` does **not** match.
  Criterion 15 asserts both directions, because a gate that compiles, reads correctly and matches
  nothing has already shipped here once (C4).
- Do **not** add it to `ResearchRoutes`, and do not add a `/api/share-keys` entry.

### The three endpoints

Append all three **after** the `DeletePatientResult` mapping (ends `Program.cs:2969`) and
**before** `app.Run();` (`:2971`), in this order: register, list, revoke. Every dependency is a
lambda parameter. **No captured local variable from `Main`**: a capture moves the lambda into a
`<>c__DisplayClass` and changes the step-7 class keys.

| # | Verb and route | Success | Errors |
|---|---|---|---|
| 1 | `POST /api/peer-keys` | `201 Created`, `Location: /api/peer-keys/{keyId}`, body `PeerKeyRecord` | `400` invalid label or key; `409` key already present (active **or** revoked) **or** equal to this node's own key |
| 2 | `GET /api/peer-keys` | `200 OK`, body `PeerKeyListResponse` | none beyond the gate |
| 3 | `POST /api/peer-keys/{keyId}/revoke` | `200 OK`, body the revoked `PeerKeyRecord` | `404` when no **active** row has that id: unknown, already revoked, or this node's own key |

Revoke is `POST .../revoke`, not `DELETE`, because nothing is deleted.

**Register flow** (lambda parameters: `RegisterPeerKeyRequest request, IPeerKeyRegistry registry,
INodeDatasetSigner localSigner, IComplianceAuditService audit, HttpContext context, CancellationToken ct`):

1. `PeerKeyValidation.TryNormalizeLabel(request.Label, out string label)`. If it fails, return 400
   with `"label must be 1 to 128 characters with no control characters."`
2. `PeerKeyValidation.TryNormalizePublicKey(request.Algorithm, request.PublicKeyBase64, out string publicKeyBase64, out string keyId)`.
   If it fails, return 400 with
   `"publicKeyBase64 must be a Base64 SubjectPublicKeyInfo public key matching the declared algorithm."`
3. If `keyId == localSigner.KeyId` (ordinal), return 409.
4. `TryRegisterAsync(record)`. If it returns `false`, return 409.
5. `audit.LogAction(caller.Username, "RegisterPeerKey", $"peer-key:{keyId}", JsonSerializer.Serialize(new { algorithm, label }))`,
   then return 201.

Both 409s use the one sentence `"That key is already registered, has been revoked, or is this node's own dataset signing key."`
No detail echoes the submitted key or label. The audit details JSON is built with `JsonSerializer`,
**never** by string interpolation, because `label` is free text.

`caller` is `(UserAccount)context.Items["CurrentUser"]!`, with the inline comment that the
`AdministratorRoutes` gate guarantees an authenticated Administrator before the handler runs.
**Do not write a null branch:** it is unreachable under the gate, could never be covered, and would
fail the step-7 ratchet.

**List flow:** return `new PeerKeyListResponse { LocalNode = new(localSigner.KeyId, localSigner.Algorithm, localSigner.PublicKeyBase64), Peers = await registry.ListAsync(ct) }`.
Revoked rows are included, with `isActive: false`. **This is how an operator obtains this node's
public key to give to a peer.** It is why no fourth endpoint is needed.

**Revoke flow** (parameters as register, minus `request`, plus `string keyId`):
`RevokeAsync(keyId, caller.Username, DateTimeOffset.UtcNow, ct)`. If it returns null, return 404 with
`"No active peer key has that identifier."` Otherwise write
`audit.LogAction(caller.Username, "RevokePeerKey", $"peer-key:{keyId}", "{}")` and return 200. The
route value is used only as a bound SQL parameter and in the audit resource string, and only after
the row was found. An unknown `keyId` is never logged or echoed.

All three use `SecureProblemResult` for every rejection. Add `.WithName`, `.WithTags("Peer Key Registry")`,
`.WithSummary`, `.WithDescription`, `.Accepts<RegisterPeerKeyRequest>` (register only) and `.Produces`
for every status listed, including 401 and 403.

### Private-key exposure

Nothing in this package reads, holds or returns a private key. The list endpoint returns
`localSigner.PublicKeyBase64`, which is the public half (P8 criterion 12 proves the signer exposes
nothing else). `TryNormalizePublicKey` rejects PKCS#8 private keys and PEM text, so an admin who
pastes a private key by mistake gets a 400 and the value is neither stored nor echoed. Criteria 12
and 14 assert this.

---

## Decision 5 — where verification runs, and the result type

**`IDatasetManager.ImportDatasetAsync` keeps its `bool` signature and integrity-only meaning.** The
five round-trip files call it directly and assert `true`/`false`, and
`DatasetImportHardeningTests.AssertImportRefusedAsync` (`:214`) relies on its
`InvalidOperationException`. Changing its type would force edits to protected files. Instead, the
new class `VerifiedDatasetImporter` decides provenance first, then calls `ImportDatasetAsync`, and
returns one result type that carries every outcome.

```csharp
namespace NanoWeaverHrt.Services;

public enum DatasetImportOutcome
{
    ImportedUnsigned,           // 200
    ImportedSigned,             // 200
    IntegrityMismatch,          // 400: SHA-256 or size does not match the manifest
    SignatureInvalid,           // 400: signature block malformed, or signature does not verify
    SignerNotTrusted,           // 403: well-formed signature by a key that is neither local nor active in the registry
    UnsignedRejectedByPolicy    // 403: unsigned manifest while Peer:RequireSignedDatasets is on
}

public sealed record DatasetImportResult(DatasetImportOutcome Outcome, string? SignerKeyId);

public sealed class VerifiedDatasetImporter
{
    public const string StrictModeConfigurationKey = "Peer:RequireSignedDatasets";
    public const string IntegrityMismatchDetail = "Dataset import rejected: the archive does not match the checksum or size recorded in its manifest.";
    public const string SignatureInvalidDetail  = "Dataset import rejected: the manifest signature is malformed or does not verify.";
    public const string SignerNotTrustedDetail  = "Dataset import rejected: the manifest is signed by a key this node does not trust.";
    public const string UnsignedRejectedDetail  = "Dataset import rejected: this node accepts only signed datasets from trusted peers.";

    public VerifiedDatasetImporter(
        IDatasetManager datasetManager,
        IPeerKeyRegistry peerKeys,
        INodeDatasetSigner localSigner,
        DatasetManifestSignatureVerifier signatureVerifier,
        IConfigurationRepository configuration,
        ILogger<VerifiedDatasetImporter> logger);

    /// Decides provenance, then performs the integrity-checked import. An InvalidOperationException
    /// from archive extraction is NOT caught: it propagates to SecureExceptionHandlerMiddleware
    /// exactly as today (DatasetImportHardeningTests.cs:767 depends on that).
    public Task<DatasetImportResult> ImportAsync(Stream archiveStream, DatasetManifest manifest, CancellationToken ct = default);

    /// Pure mapping for the endpoint. Throws ArgumentOutOfRangeException for the two Imported* values.
    public static (int StatusCode, string Detail) DescribeRejection(DatasetImportOutcome outcome);

    /// null when the raw value is not a bool; the caller treats null as ON (fail closed) and logs a warning.
    internal static bool? TryParseStrictMode(string? raw);
}
```

### The decision order — fixed, and every step is tested

```
ImportAsync(archive, manifest):
  1. state = DatasetManifestSignature.GetState(manifest)
  2. Malformed                           -> SignatureInvalid          (before any lookup)
  3. Unsigned:
       strict = TryParseStrictMode(await GetValueAsync(StrictModeConfigurationKey, "false"))
       strict is null -> log warning (key name only, never the value), treat as ON
       ON             -> UnsignedRejectedByPolicy
  4. Signed:
       keyId == localSigner.KeyId        -> trusted = (localSigner.Algorithm, localSigner.PublicKeyBase64)
       else row = FindActiveAsync(keyId)
            row is null                  -> SignerNotTrusted          (unknown OR revoked; same answer)
            else trusted = (row.Algorithm, row.PublicKeyBase64)
       !signatureVerifier.Verify(manifest, trusted...) -> SignatureInvalid
  5. ok = await datasetManager.ImportDatasetAsync(archive, manifest)   // may throw; not caught
  6. ok ? (Imported{Signed|Unsigned}, keyId-or-null) : IntegrityMismatch
```

- **Provenance is decided before the archive is read.** For steps 2–4, a rejected bundle's stream is
  never read: it is not buffered to a temp file, hashed or extracted. Criterion 21 proves this at
  unit level.
- Strict mode is read **only for unsigned manifests**, through
  `IConfigurationRepository.GetValueAsync`, on every request. An admin's toggle therefore takes
  effect on the next import, with no restart.
- Logging: one `LogWarning` per rejection naming the outcome. Include `SignerKeyId` **only** for
  `SignerNotTrusted` and `SignatureInvalid` from step 4, where `GetState == Signed` has already
  proved it is 64 lowercase hex characters. **Never** log the key id of a `Malformed` manifest: it is
  attacker-shaped and could carry log-injection characters.

### The size check in `ImportDatasetAsync`

After the hash comparison (`DatasetTransferService.cs:274-278`), add: if the number of bytes written
to the temp file `!= manifest.SizeInBytes`, log and `return false`. Without this, the signed
`SizeInBytes` would bind nothing. A SHA-256 match already pins the bytes, so the check only fires
when the manifest misstates the size. Every existing test that expects `true` builds its manifest
with the correct size (`DatasetImportHardeningTests.cs:179` `SizeInBytes = archiveBytes.Length`, and
the export-produced manifests), so none of the five files changes.

### The endpoint

Change the `/api/datasets/import` lambda (`Program.cs:626`) **in place**. Do not move it and do not
add a lambda inside it. The parameters become
`(IFormFile archive, IFormFile manifest, VerifiedDatasetImporter importer, HttpContext context, CancellationToken ct)`.

- The missing-file branch and the JSON-parse branch are **unchanged**, `Results.BadRequest` bodies
  included. Converting them is P10/P11 territory.
- `DatasetImportResult result = await importer.ImportAsync(archiveStream, parsedManifest, ct);`
- For `ImportedSigned` / `ImportedUnsigned`, return
  `Results.Ok(new { message = "Dataset successfully verified and imported.", manifest = parsedManifest, provenance = "signed" | "unsigned", signerKeyId = result.SignerKeyId })`.
  These are additive fields; the existing `message` and `manifest` stay.
- Otherwise: `(int status, string detail) = VerifiedDatasetImporter.DescribeRejection(result.Outcome); return SecureProblemResult(context, status, detail);`
  The old hash-mismatch body `{ error = "...SHA-256 hash mismatch." }` is replaced by the RFC 7807
  `IntegrityMismatchDetail`, still with status 400. The tests that hit it over HTTP
  (`EndpointIntegrationTests.cs:407`, `ComprehensiveCoverageTests.cs:950`) assert **status only** and
  are unaffected. `DatasetTransferTests.cs:111` calls the service directly and is unaffected too.
- Add `.Produces(StatusCodes.Status403Forbidden)`, and update `.WithSummary` / `.WithDescription` to
  mention signature verification.

**Leak contract (Directive 14):** every rejection detail is one of the four fixed constants. None
contains a key, signature, key id, path, SQL fragment, exception message or anything from the
request.

### DI

Next to P8's two lines, type-based only (no `sp =>` lambda; see Constraints 1):

```csharp
builder.Services.AddSingleton<IPeerKeyRegistry, PeerKeyRepository>();
builder.Services.AddSingleton<VerifiedDatasetImporter>();
```

---

## Decision 6 — the strict-mode setting

Append as the **last element** of `SystemConfigurationStore.Definitions` (`:49-62`), after
`Peer:CollaborativeWorkerEndpoint`:

```csharp
new("Peer:RequireSignedDatasets", "false", "PEER_REQUIRE_SIGNED_DATASETS", "Peer Collaboration",
    "When true, POST /api/datasets/import refuses every dataset whose manifest is not signed by this node or by an active registered peer key. Off by default so existing unsigned bundles and the documented quickstart keep working. While off, a signature identifies a trusted sender but does not stop an uploader who simply leaves the signature out.")
```

| Attribute | Value | Why |
|---|---|---|
| Key | `Peer:RequireSignedDatasets` | Same `Peer:` namespace as the other cross-node trust settings. |
| Default | `"false"` | **Default OFF**, so the quickstart and the five round-trip files keep passing unchanged. Required by the user's decision. |
| Environment variable | `PEER_REQUIRE_SIGNED_DATASETS` | Matches the `PEER_*` convention at `:59-61`. |
| Category | `"Peer Collaboration"` | Imports are how peer data arrives. |
| `IsSecret` | **false** (omit the argument) | It is a policy flag, not a credential. Also checked: the key contains none of the `SecretKeyNameMarkers` (`:41-44`), so `IsSecretConfigurationKey` returns false and `GET /api/configuration` shows the real value. |

Parsing: `bool.TryParse(raw?.Trim(), ...)`. `"true"`/`"false"` match in any case. **Anything else
fails closed (ON)** and logs a warning naming the key, never the value. A typo in a security flag
must not silently disable it. The default `"false"` parses, so fail-closed never applies to an
unconfigured node.

**Test-isolation trap:** `GetValueAsync` reads `PEER_REQUIRE_SIGNED_DATASETS` from the **process
environment** (`SystemConfigurationStore.cs:234`), which xunit shares across parallel tests. Tests
must toggle strict mode with a **database override** on their own host
(`POST /api/configuration/override` as Administrator, or `SetDatabaseOverrideAsync` on the host's
`IConfigurationRepository`), never with `Environment.SetEnvironmentVariable`. Note in the report that
a developer shell exporting `PEER_REQUIRE_SIGNED_DATASETS=true` would make unrelated tests fail. That
is expected and not a defect.

---

## Decision 8 — documentation

**`AGENTS.md:127` is false today for this path, and still overstated after P8.** It says
"Cryptographically signed `.tar.gz` dataset bundles with SHA-256 manifests". Before P8 nothing on
this path was signed. Only `/api/datasets/export-encrypted` signs, and that key is ephemeral (see the
consistency check, last item). Replace the bullet with:

> `exports/`: `.tar.gz` dataset bundles with SHA-256 manifests. Manifests exported by the web service are Ed25519-signed with the node's persisted dataset signing key; `POST /api/datasets/import` verifies a signed manifest against this node's key and the peer key registry (`weaver_peer_keys`), and accepts unsigned manifests unless `Peer:RequireSignedDatasets` is `true`.

That bullet is the **only** `AGENTS.md` change. §1's 14 Directives are untouched.

Every doc page you touch must state, in plain words (Directive 4), the property the user's posture
implies:

> **With strict mode off (the default), signing tells you who sent a bundle. It does not protect you
> from an uploader who omits the signature.** An unsigned bundle is imported exactly as before.
> Turn on `Peer:RequireSignedDatasets` to make signatures mandatory.

Also document: how to read this node's key and fingerprint (`GET /api/peer-keys` → `localNode`, and
the startup log line from P8); how to register a peer's key **after confirming its fingerprint out of
band**, never by copying it from a bundle you received; that revocation is permanent and a revoked
key can never be re-registered; that this node always trusts its own key; and that deleting
`dataset_signing_ed25519.key` rotates it, so bundles signed by the old key then return 403 here
unless the old public key is registered as a peer.

`docs/configuration-peers.md:113` describes certificate verification and an "atomic key exchange"
for federated links. I did not verify that against the code. **Correct only the clause about signed
manifests.** List the rest in the report as a candidate for P12's documentation truth pass. Do not
rewrite it.

Markdown (Directive 8): no single-dollar delimiters anywhere. Gate step 5 scans `docs/**/*.md` and
`README.md`.

---

## Interfaces and signatures — repository and validation

```csharp
namespace NanoWeaverHrt.Storage;

public interface IPeerKeyRegistry
{
    /// false when key_id already exists, whether active or revoked. INSERT ... ON CONFLICT (key_id) DO NOTHING.
    Task<bool> TryRegisterAsync(PeerKeyRecord record, CancellationToken ct = default);

    /// WHERE key_id = @id AND revoked_at_utc IS NULL. Null for unknown or revoked.
    Task<PeerKeyRecord?> FindActiveAsync(string keyId, CancellationToken ct = default);

    /// All rows, revoked included. ORDER BY registered_at_utc, key_id.
    Task<IReadOnlyList<PeerKeyRecord>> ListAsync(CancellationToken ct = default);

    /// UPDATE ... SET revoked_by, revoked_at_utc WHERE key_id = @id AND revoked_at_utc IS NULL;
    /// null when 0 rows were affected, otherwise the re-read row.
    Task<PeerKeyRecord?> RevokeAsync(string keyId, string revokedBy, DateTimeOffset revokedAtUtc, CancellationToken ct = default);
}

public sealed class PeerKeyRepository : IPeerKeyRegistry
{
    public PeerKeyRepository(IWeaverDbConnectionFactory connectionFactory);
    internal static PeerKeyRecord MapPeerKey(DbDataReader reader);   // Enum.Parse<KeyAlgorithm>; WeaverDbValues for timestamps
}
```

- One SQL text per statement, shared by both engines. Parameters go through `WeaverDbParameters`.
  Timestamps go through `session.Dialect.TimestampValue` / `TimestampValueOrNull`. No `NpgsqlConnection`,
  `SqliteConnection` or other driver type in this file.
- **Mocking a database driver is prohibited**, as in P4/P6: real SQLite in tests, and real PostgreSQL
  under `WEAVER_TEST_POSTGRES` using the guard pattern in `PostgresBackedRepositoryTests.cs`. Never
  `Assert.Skip` (it does not compile against xunit 2.9.2).

```csharp
namespace NanoWeaverHrt.Security.Signing;

public static class PeerKeyValidation
{
    public const int MaxLabelLength = 128;
    public const int MaxPublicKeyBase64Length = 8192;

    /// Trim; 1..128 chars; no char.IsControl. false otherwise.
    public static bool TryNormalizeLabel(string? label, out string normalized);

    /// false for: null or whitespace; longer than MaxPublicKeyBase64Length; contains "-----BEGIN";
    /// not Base64; PublicKeyFactory.CreateKey throws (any exception, so a PKCS#8 private key lands
    /// here); Ed25519 declared but the key is not Ed25519PublicKeyParameters; MlDsa65 declared but
    /// the key is not MLDsaPublicKeyParameters whose Parameters is MLDsaParameters.ml_dsa_65.
    /// On success: canonicalBase64 = Convert.ToBase64String(der), keyId = DatasetManifestSignature.ComputeKeyId(der).
    public static bool TryNormalizePublicKey(KeyAlgorithm algorithm, string? publicKeyBase64,
                                             out string canonicalBase64, out string keyId);
}
```

The ML-DSA-65 parameter-set check is required because `PostQuantumCryptoProvider.VerifySignature`
(`:280`) accepts **any** ML-DSA parameter set. The registry is where `MlDsa65` must actually mean
ML-DSA-65. If BouncyCastle 2.7 turns out not to hand back its static parameter instance, so that
reference comparison fails for a genuine ML-DSA-65 key, compare `.Parameters.Name` instead and say so
in the report.

`PeerKeyRecord` JSON names: `keyId`, `label`, `algorithm` (the enum is already a
`JsonStringEnumConverter`), `publicKeyBase64`, `registeredBy`, `registeredAtUtc`, `revokedBy`,
`revokedAtUtc`, and `isActive` (computed: `RevokedAtUtc is null`). `PeerKeyListResponse`:
`localNode` { `keyId`, `algorithm`, `publicKeyBase64` } and `peers`.

---

## Constraints

1. **New members go at the END of their class, and new lambdas at the END of `Main`.** The step-7
   ratchet keys on compiler names: `<ImportDatasetAsync>d__N` for methods, `<<Main>b__4_N>d` for
   endpoint lambdas (`coverage-baseline.json:24-37`). Inserting a method mid-class, or a lambda
   anywhere in `Main` before the last existing one (DI `sp =>` factories included), renumbers its
   successors and reads as a regression. That is why: the three endpoints go immediately before
   `app.Run()`; DI registrations are type-based; the import endpoint is edited in place with no
   nested lambda; and `DatasetTransferService` gains **no method**.
2. **The five round-trip files are not edited:** `DatasetTransferTests`, `EndpointIntegrationTests`,
   `ComprehensiveCoverageTests`, `DatasetImportHardeningTests`, `ApiRoleGatingAndDisclosureTests`.
   New tests go in the three new files. The role-gate tests need Researcher and Patient tokens: copy
   the `ProvisionRoleTokensAsync` / `CreateClientWithToken` approach from
   `ApiRoleGatingAndDisclosureTests.cs` into `PeerKeyEndpointTests.cs`. **Do not refactor the
   original into a shared helper**, because that edits a protected file. If any of the five fails,
   **stop and report**. Do not edit the test to make it pass.
3. **The only existing tests you may edit** are the two migration-count files named under Stage A,
   at the lines named there, and only those assertions.
4. **Do not delete or weaken P8's tests.** In particular, P8's criterion 20
   (`ImportDatasetAsync_IsIntegrityOnly_AndIgnoresSignatures` or similar) asserts that the *service
   method* imports a manifest with a corrupted signature. That **stays true in P9**: the service is
   integrity-only, and the endpoint rejects the bundle through `VerifiedDatasetImporter`. Both tests
   coexist.
5. Do not add a member to `ISqlDialect` (seven-member cap), `IDatasetManager`,
   `IConfigurationRepository`, `INodeSecretStore` or `INodeDatasetSigner`. If one seems necessary,
   stop and report.
6. Do not touch `weaver_share_keys`, its repository methods or the `/api/share-keys` routes.
7. Directive 2 and gate step 6: a full docblock on every new member, private included. Directive 3:
   WHAT/WHY/HOW on the decision order in `ImportAsync`, each repository statement, both validators
   and each new endpoint. Directive 4: `VerifiedDatasetImporter`'s remarks explain the
   strict-mode-off downgrade property in plain English.
8. Directive 6: no sleeps, each host on its own temp `TRAJECTORY_INCOMING_DIR` / `TRAJECTORY_EXPORT_DIR`
   / `TRAJECTORY_PARQUET_DIR` (so each gets its own SQLite file, registry and node signing key),
   nothing under `simulations/`, and no process environment mutation (see decision 6).
9. **Async methods and endpoint lambdas are invisible to gate step 4**, because `coverlet.runsettings`
   excludes `CompilerGeneratedAttribute`. They are measured by step 7. Write full tests for them
   anyway, because step 7 counts a new class arriving with uncovered lines as a regression. Do not
   change `coverlet.runsettings` or `coverage-full.runsettings`.
10. **Coverage ratchet:** no class's uncovered-line count may rise. If step 7 reports the baseline
    stale, run
    `python3 -I scripts/check-coverage-ratchet.py artifacts/coverage-full coverage-baseline.json --update`
    and include it in this diff, quoting old and new `line_rate`, `branch_rate` and
    `uncovered_lines_total`. Never `--allow-regression`.
11. Ed25519 and ML-DSA-65 only. No ECDSA and no RSA in code, tests, Swagger text or any doc text you
    write.
12. The `!` operator only with an inline justification. Zero warnings.
    `dotnet format --verify-no-changes` clean.
13. Finish with `./scripts/verify-all.sh`, run **once with `WEAVER_TEST_POSTGRES` set** (the
    migration and repository must be proven on real PostgreSQL) and report all seven step results
    verbatim, plus how many tests ran against PostgreSQL. A "7/7" without PostgreSQL is a smaller
    suite. Say so if that is all you could run.

---

## Acceptance criteria

**Migration and repository (stage A)**

1. `DatabaseMigrationRunner.RegisteredMigrations` has 7 entries, ending with
   `Migration007_PeerKeyRegistry` (`Version == 7`, `Name == "007_PeerKeyRegistry"`). On a fresh SQLite
   database, versions `{1..7}` are recorded. A second apply reports 0 pending.
2. `GetSql("sqlite")` and `GetSql("postgresql")` each contain `CREATE TABLE IF NOT EXISTS weaver_peer_keys`,
   all three named `CHECK` constraints and **no** `INSERT`. `GetSql(null!)` throws `ArgumentNullException`.
3. On SQLite **and** on real PostgreSQL: a raw insert violating each `CHECK` fails. The three cases
   are a 63-character `key_id`, `key_algorithm = 'Rsa'`, and `revoked_by` set with `revoked_at_utc`
   NULL. (The literal `'Rsa'` is test data proving rejection, not an implemented algorithm.)
4. On SQLite **and** on real PostgreSQL: `TryRegisterAsync` then `FindActiveAsync` round-trips every
   field (timestamps equal to the microsecond, offset UTC), for one Ed25519 row and one ML-DSA-65
   row.
5. `TryRegisterAsync` returns `false`, not an exception, for a duplicate `key_id`, on both engines.
6. `ListAsync` returns active and revoked rows in `registered_at_utc, key_id` order.
7. `RevokeAsync` returns the revoked record with both revocation columns set. `FindActiveAsync` then
   returns null. A second `RevokeAsync` and a `RevokeAsync` of an unknown id both return null.
8. **Revocation is permanent:** `TryRegisterAsync` of a revoked key's id returns `false`, and the row's
   `revoked_by` / `revoked_at_utc` are unchanged afterwards. This test exists because `INSERT OR REPLACE`
   would pass every other criterion and fail this one.
9. `grep -n "INSERT OR REPLACE\|NpgsqlConnection\|SqliteConnection" src/NanoWeaverHrt/Storage/PeerKeyRepository.cs`
   finds nothing.

**Validation (pure, step-4 visible)**

10. `TryNormalizeLabel`: trims, accepts 1 and 128 characters, rejects null, `""`, whitespace-only,
    129 characters and a string containing a line feed.
11. `TryNormalizePublicKey` accepts a genuine Ed25519 SPKI and a genuine ML-DSA-65 SPKI. Re-encoding
    a valid key given with different but equivalent Base64 (for example with whitespace stripped)
    yields the identical `canonicalBase64` and `keyId`, and `keyId` equals P8's `ComputeKeyId` of the DER.
12. `TryNormalizePublicKey` rejects each of: null; whitespace; 8193 characters; a PEM block; non-Base64;
    random bytes; an Ed25519 key declared `MlDsa65` and the reverse; an **ML-DSA-44** (or `-87`) key
    declared `MlDsa65`; and an Ed25519 **PKCS#8 private key** (Base64 beginning `MC4CAQAwBQYDK2Vw`).

**Endpoints and gating (stage B)**

13. As Administrator: register an Ed25519 key returns 201 with a `Location` of
    `/api/peer-keys/{keyId}` and `isActive: true`. Register an ML-DSA-65 key returns 201. Register
    the first again returns 409. Register this host's own `localNode.publicKeyBase64` returns 409.
    `GET` returns 200, with `localNode.keyId` equal to the host's `INodeDatasetSigner.KeyId` and both
    peers listed. Revoke returns 200 with `isActive: false`. Revoke again returns 404. Revoke of
    `localNode.keyId` returns 404. Re-register the revoked key returns 409. Each successful register
    and revoke adds one compliance-ledger record whose operator is the admin's username; read it back
    through `GET /api/compliance/audit-ledger`.
14. **No private key material in any response.** Across every response body from criterion 13 and
    every 400 from criterion 12 sent over HTTP: the Base64 of the host's 32-byte seed (read from
    `node-keys/dataset_signing_ed25519.key` in the test's temp dir) does not appear; neither do
    `PRIVATE KEY`, `MC4CAQAwBQYDK2Vw`, nor the submitted invalid value; and no 400/409 detail contains
    the submitted label.
15. **Pure gate, the C4 regression net:**
    `ResolveRouteRoleRequirement` returns `AdministratorOnly` for (`GET`, `/api/peer-keys`),
    (`POST`, `/api/peer-keys`) and (`POST`, `/api/peer-keys/{64 hex}/revoke`). It returns `None` for
    (`GET`, `/api/peer-keysx`), proving a segment-exact match rather than a string prefix.
16. **HTTP gate:** a **Researcher** token and a **Patient** token each receive **403** on **all three**
    endpoints. Requests carry valid bodies and real route values, so the 403 cannot be confused with
    a validation 400. In the same test, the Administrator token receives 201 / 200 / 200 on the same
    three requests, which proves the routes exist and the gate admits the right role.

**Enforcement table (stage B).** Each case runs through `POST /api/datasets/import` on a host bound
to temp directories, unless stated otherwise. The "peer" is a second `NodeDatasetSigner` over its
own temp key directory, exporting through `new DatasetTransferService(peerConfig, logger, peerSigner)`.

17. **Unsigned, strict OFF (default):** 200, `provenance: "unsigned"`, files extracted.
18. **Signed by a registered Ed25519 peer, valid:** 200, `provenance: "signed"`, `signerKeyId` equal
    to the peer's key id, files extracted. The same import by a **Researcher** token also returns 200.
19. **Signed by a registered ML-DSA-65 peer, valid** (manifest built and signed as in P8 criterion
    14): 200.
20. **Signed by the host's own key** (export on the host, then import on the same host, with nothing
    registered): 200. This is `ComprehensiveCoverageTests.cs:447`'s path, asserted explicitly here.
21. **Signed, signer not registered:** 403 with exactly `SignerNotTrustedDetail`, and the host data
    volume's file list is unchanged. **Unit level:** construct `VerifiedDatasetImporter` directly
    (real `DatasetTransferService`, real `PeerKeyRepository` over a temp SQLite factory, real
    `NodeDatasetSigner`, real `SystemConfigurationStore`), and pass an archive `Stream` subclass whose
    `Read`/`ReadAsync` throw. For the outcomes `SignatureInvalid` (from both step 2 and step 4),
    `SignerNotTrusted` and `UnsignedRejectedByPolicy`, `ImportAsync` returns the outcome without
    throwing. That proves the archive is never read on a provenance rejection. (A throwing `Stream`
    is a test double for an I/O stream, not a database driver, so the driver-mocking ban does not
    apply.)
22. **Signed, signer registered then revoked:** 403, `SignerNotTrustedDetail`.
23. **Signed by a registered peer, invalid:** 400 with exactly `SignatureInvalidDetail`, in three
    cases: one signature byte flipped; `DatasetName` edited after signing; `SizeInBytes` edited after
    signing (with the archive unchanged).
24. **Malformed signature block:** 400, `SignatureInvalidDetail`, in two cases: `signerKeyId` removed;
    `signatureAlgorithm: "ML-DSA-65"`.
25. **Unsigned, strict ON** (set through a database override): 403 with exactly `UnsignedRejectedDetail`.
    Then, **on the same host with strict still ON**, criterion 18's bundle returns 200 and criterion
    20's self-signed bundle returns 200.
26. **Strict value `"yes"`:** an unsigned import returns 403 (fail closed), and a warning is logged
    that names `Peer:RequireSignedDatasets` and does not contain `yes`. **Strict value `" FALSE "`:**
    an unsigned import returns 200.
27. **Precedence**, each pinned by a test: (a) unregistered signer **and** corrupted signature: 403
    `SignerNotTrustedDetail`; (b) malformed block **and** unknown key id: 400 `SignatureInvalidDetail`;
    (c) registered signer, valid signature, but archive bytes **not** matching `sha256`: 400
    `IntegrityMismatchDetail`; (d) registered signer, corrupted signature, **and** wrong archive: 400
    `SignatureInvalidDetail`.
28. **Size binding:** a service-level test where the archive's SHA-256 matches but `SizeInBytes` is off
    by one makes `ImportDatasetAsync` return `false` with nothing extracted. Over HTTP with an unsigned
    manifest, the same case gives 400 `IntegrityMismatchDetail`.
29. **Leak contract:** every HTTP rejection in criteria 21–28 is `application/problem+json`, and its
    `detail` is byte-equal to one of the four constants. No body contains the temp directory path,
    `Path.GetTempPath()`, `weaver_peer_keys`, `SELECT`, `   at `, the manifest's `signature` value,
    or any registered public key.
30. `DescribeRejection` maps `IntegrityMismatch`, `SignatureInvalid`, `SignerNotTrusted` and
    `UnsignedRejectedByPolicy` to 400, 400, 403 and 403 with their constants, and throws
    `ArgumentOutOfRangeException` for `ImportedSigned` and `ImportedUnsigned`. `TryParseStrictMode`
    returns `true` for `"True"`, `false` for `"false"`, and `null` for each of `"yes"`, `null` and `""`.
31. `grep -rn "ImportDatasetAsync(" src/` matches only `IDatasetManager.cs`, `DatasetTransferService.cs`
    and `VerifiedDatasetImporter.cs`. No endpoint calls the integrity-only method directly.

**Configuration**

32. `GET /api/configuration` as Administrator lists `Peer:RequireSignedDatasets` with
    `value: "false"`, `defaultValue: "false"`, `category: "Peer Collaboration"`, `isSecret: false`,
    and the value is **not** `[REDACTED]`. `SystemConfigurationStore.IsSecretConfigurationKey("Peer:RequireSignedDatasets")`
    is false. `VerifiedDatasetImporter.StrictModeConfigurationKey` equals the catalog key.

**Unchanged behaviour**

33. The five protected files pass with **zero edits** (`git diff --stat` shows none of them). This
    includes `DatasetImportHardeningTests.ImportEndpoint_ReturnsRfc7807ProblemDetails_WithoutLeakingFilesystemPaths`
    (unsigned, strict off, hostile member, still 400 from the middleware) and
    `ComprehensiveCoverageTests.ImportDataset_ValidArchiveShouldSucceed` (self-signed, 200).
34. All of P8's tests pass unmodified, including its criterion 20.
35. `/swagger/v1/swagger.json` still generates (that is the existing `EndpointIntegrationTests:654`
    check) and contains `/api/peer-keys`.

**Docs (stage C)**

36. `AGENTS.md` differs from the base only in the §2 `exports/` bullet. `git diff AGENTS.md` shows a
    single changed bullet and no line inside §1.
37. `docs/api-reference.md` documents all three endpoints, with request and response shapes and
    every status code from decision 4, and the import endpoint's full status table: 200; 400 for each
    of the five causes (missing file, unparseable manifest, malformed or non-verifying signature,
    checksum or size mismatch, hostile archive member); 403 for role, untrusted signer and strict mode.
38. `docs/trajectories-and-datasets.md`, `docs/configuration-peers.md` and `docs/api-reference.md`
    each contain the strict-mode-off sentence from decision 8 in substance. None claims that an
    unsigned import is rejected by default, and none attributes peer-verifiable signatures to
    `/api/datasets/export-encrypted`.
39. No text added or changed by this package mentions ECDSA or RSA. Gate step 5 passes (no
    single-dollar tokens).

**Gate**

40. `./scripts/verify-all.sh` 7/7 with `WEAVER_TEST_POSTGRES` set. Step 4 exactly 100.00% / 100.00%,
    0 warnings, 0 incomplete docblocks. Step 7 has no class's uncovered count rising. The report
    states how many tests ran against PostgreSQL.

---

## Non-goals

- Fixing the ephemeral ML-DSA-65 key behind `/api/datasets/export-encrypted` and the audit ledger.
  This is reported (see the consistency check) but not fixed here.
- Re-trusting a revoked key, key rotation tooling, expiry on peer keys, or importing keys from a
  received bundle. Trust is established out of band, deliberately.
- Replay or freshness checks on signed manifests.
- Converting the import endpoint's missing-file and parse-failure branches to `SecureProblemDetails`.
- A Razor UI for the registry. The configuration page shows the new setting automatically, because
  it renders `Definitions`, but no registry page is built. `.razor` coverage is P17.
- Rewriting `docs/configuration-peers.md` beyond the signed-manifest clause.
- `WeaverDbValues.ReadTimestamp` parses SQLite text with the current culture (`DateTimeOffset.Parse`
  with no provider). Observed, pre-existing, out of scope. Mention it in the report for P11.

---

## Criteria consistency check

I re-read criteria 1–40 as a set, and against P8's criteria, checking every pair for conflict. Two
items (A and H) are **real tensions**, not just apparent ones, and I flag them rather than resolving
them silently. Item A comes from the decided posture itself.

**A. Self-trust versus "signed + unregistered → 403". RULED BY THE USER (2026-10-23): implicit
self-trust, option 1 below. This section is retained as the rationale; the decision is closed.**

> **Binding clarification on what self-trust means.** The node's own public key is added to the
> **trust set**. It is NOT a verification bypass. The signature over the canonical bytes MUST still
> be verified in full, by the same code path and with the same failure handling as any peer key. A
> bundle claiming this node's key id but carrying a signature that does not verify MUST be rejected
> 400, exactly as criterion 27 step 3 requires for a peer. An implementation that short-circuits
> verification when the key id matches the local node is WRONG and must fail review: it would let
> anyone who learns the node's key id import arbitrary data by attaching a garbage signature.

The original analysis follows. The user's table says a signer that is not registered gets 403. P8 makes the host sign
every export. `ComprehensiveCoverageTests.ImportDataset_ValidArchiveShouldSucceed` (one of the five
protected files) exports on the host and re-imports on the same host, asserting 200, and nothing ever
registers the host's key. Read literally, criterion 21 (unregistered → 403) and criterion 33 (that
file passes unedited) cannot both hold. The options:
  1. **Implicit self-trust (recommended, specified above).** The node's own key counts as
     registered. It is not stored, so it cannot be revoked by accident, and it is published through
     `localNode`. Cost: one extra branch, and "registered" in the table means "registered or local".
     A Researcher can already import unsigned data while strict is off, so self-trust grants no
     capability they lack.
  2. Auto-insert the local key into `weaver_peer_keys` at startup. Rejected: it writes to the
     database on every start, needs a synthetic `registered_by`, and lets an admin revoke the node's
     own key and break local backup restores without knowing why.
  3. Edit `ComprehensiveCoverageTests` to register the key first. Rejected: it edits a protected file,
     which the brief forbids without explicit justification, and it would also break the documented
     export-then-import quickstart.
  With option 1, criteria 20, 21 and 33 all hold. **If the coordinator rejects self-trust, criterion 33
  must be relaxed for that one test. Say so explicitly; do not resolve it silently.**

**B. The user's table has overlapping rows. Resolved by fixed precedence, pinned in criterion 27.**
A manifest can be both "signer NOT registered" (403) and "signature invalid" (400). A signature cannot
be checked without a trusted key, so the trust lookup comes first and the result is **403**
(criterion 27a). A *malformed* block is rejected **400** before any lookup, even when its key id is
unknown (27b), because "malformed" is a property of the request alone. Signature checks precede the
integrity check (27c, 27d), so a bundle never reaches the archive stage on a bad signature.
Criteria 21, 23 and 24 each use a manifest that falls in exactly one row, so they do not contradict
27.

**C. Revoked versus unregistered.** Both give 403 with the same detail (criteria 21 and 22). This is
intentional, so an uploader cannot learn whether a key was ever trusted. No conflict with criterion
13, where the admin-facing list *does* show revocation: that list is Administrator-only.

**D. Strict mode versus signed bundles.** Criterion 25 asserts strict ON still admits valid signed
and self-signed bundles. That is consistent with 18 and 20; strict mode governs unsigned bundles only.

**E. Fail-closed parsing versus "default OFF".** Criterion 26 (garbage → ON) and criterion 32
(default `"false"`) both hold, because the default parses. Fail-closed applies only to an explicitly
set, unparseable value.

**F. The size check versus the five protected files.** Criterion 28 adds a new `false` path to
`ImportDatasetAsync`, and criterion 33 says the five files stay unedited and green. I verified every
`true`-expecting call site in those files: each builds its manifest with the exact archive length,
either from `BuildMatchingManifest` at `DatasetImportHardeningTests.cs:179` or from an export. The
mismatch tests already fail on the hash. Both criteria hold.

**G. P8 criterion 20 versus P9 criterion 23.** P8 asserts that the *service* imports a corrupted
signature, and P9 asserts that the *endpoint* rejects it. Both hold permanently, because they test
different layers. Constraint 4 forbids deleting the P8 test. This is exactly the pattern that went
wrong in `db-seam-rollout.md`, so it is named here in advance.

**H. Directive 13's `INSERT OR REPLACE` versus criterion 8. This departs from the brief's wording;
flagged.** The brief says "SQLite uses `INSERT OR REPLACE`". For the migration's version row, that
remains true and untouched (the runner does it). For the registry's runtime insert, `INSERT OR REPLACE`
would reactivate revoked keys, and criterion 8 exists to catch exactly that. I recommend
`ON CONFLICT DO NOTHING` on both engines, which `UserRepository` already does. If the coordinator reads
Directive 13 as mandating `INSERT OR REPLACE` for every SQLite insert, criterion 8 and that reading
**cannot both hold**, and the coordinator must decide. I recommend criterion 8, because the alternative
silently undoes a security revocation.

**I. "Three endpoints" versus publishing the local public key.** Peers need this node's public key,
and the brief allows three endpoints. Folding `localNode` into `GET /api/peer-keys` satisfies both, so
criterion 16's "all three" is exhaustive.

**J. Criterion 14 (no key material) versus criterion 13 (list returns `localNode.publicKeyBase64`).**
These are consistent. Criterion 14 targets the *seed* and the PKCS#8 private-key header, and the
public key is meant to be published.

**K. Criterion 21's throwing `Stream` versus the driver-mocking ban.** No conflict. The ban covers
`DbConnection` and its hierarchy, and an I/O stream test double is outside it. Criterion 21 states
this so a reviewer does not read it as a violation.

**L. The downgrade property is not a criteria conflict, but it must not be lost.** With strict mode
OFF, anyone who can upload can strip the three signature fields and be accepted as unsigned
(criterion 17). This is the user's decided posture, not a defect in this design. Criterion 38 makes
the docs say so plainly, so nobody later reads "signed bundles" as "protected by default".

**Found in the code, contradicting the brief or PHASES-v2.md:**
- `PHASES-v2.md` P9 says migration `006`. **It is 007.** `Migration006_EncryptGenderIdentity` exists.
- The brief's fact base says "a new one is therefore **Migration005**". **Also stale.** 005 and 006
  both exist.
- The brief calls `/api/datasets/export-encrypted` "genuinely signed". **It is signed with an
  ephemeral key.** `KeyManagementService`'s constructor (`:57`) generates a fresh ML-DSA-65 pair on
  every start, under the constant key id `"node_signing_master"` on every node. Its signatures cannot
  be verified by anyone after a restart, and the key id identifies nothing. The same key signs the
  Part 11 audit ledger. This is out of scope here and needs its own queue item.
- `SecurityHardeningAndMigrationsTests.cs:168-183` and `PostgresBackedProfileRepositoryTests.cs:212`
  pin the migration count, so adding migration 007 *requires* editing them. Neither is one of the
  five protected files.
