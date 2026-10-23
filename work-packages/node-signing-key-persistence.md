# Work package P7.5: persist the node ML-DSA-65 signing key

> **Why this file is not under `docs/`.** Gate step 5 scans `docs/**/*.md` and `README.md` for paired
> single-dollar tokens. Work packages are not documentation. **Do not move this file under `docs/`.**


> ## Coordinator rulings (2026-10-23) — read before implementing
>
> - **This package is NOT next.** A subject key persistence package lands first: subject keys live
>   in an in-memory `ConcurrentDictionary`, so after a restart every encrypted record is
>   permanently undecryptable **and** `EncryptedStorageService.cs:145` reports the loss as
>   *"Crypto-Shredded pursuant to GDPR Article 17"* — a false legal claim, since `GetSubjectKey`
>   simply returned null. That outranks this package. Re-read `KeyManagementService` before
>   starting here; it will have changed.
> - **Tension A: ACCEPTED as specified.** A missing key file is treated as first launch and
>   generates a key, made visible in the startup log and the signed genesis record. Not refused.
> - **Tension C: ACCEPTED as specified.** `NodeSecretStore` repairs wrong permissions to 0600 and
>   loads. Refusing would mean changing behaviour for all three key files, which is out of scope
>   here. Do not widen the package to change it.
> - **No migration. `008` stays free** and is expected to be claimed by the subject key package.
> - Confirmed in source by the coordinator, not taken from the report: the audit ledger is a plain
>   in-memory `List<AuditRecord>` (`ComplianceAuditService.cs:54`) with no persistence anywhere, so
>   there are no legacy signed rows to migrate. **Persisting this key does not by itself make the
>   Part 11 trail survive a restart** — ledger persistence is a separate, still-unwritten package.
> - `GetNodeMasterKey()` confirmed to have no production caller (interface, impl, and
>   `PostQuantumComplianceTests.cs:183` only). Removing it is correct; no data is lost through it.

**Agent:** coder. **Base commit: `ce94d26`** (clean, pushed). The working tree also holds two untracked
design documents, `work-packages/dataset-signing.md` (P8) and `work-packages/peer-key-registry.md` (P9).
Do not edit them. Your worktree branches from the session-start commit, so your FIRST action is
`git rebase main`. Report the rebase result before doing anything else. Do not commit. Do not push.

**Order:** this package lands **before** P8 and P9. It adds **no migration**. `008` stays unclaimed (see
Decision 4).

---

## Goal

`KeyManagementService`'s constructor (`KeyManagementService.cs:57-58`, singleton at `Program.cs:109`)
creates a new ML-DSA-65 key pair every time the process starts, and never saves it. Every node uses the
same fixed key id, `"node_signing_master"`. That key signs every FDA 21 CFR Part 11 audit record
(`ComplianceAuditService.cs:90`, `:139`) and is the key the ledger is verified against (`:185`). It also
signs the `.manifest.pqc.json` files written by `/api/datasets/export-encrypted`
(`EncryptedStorageService.cs:227-231`). After a restart no signature it made can be verified by anyone, and
its key id does not identify any node.

After this package:

1. The node's ML-DSA-65 signing key is a **32-byte FIPS 204 seed** (the value FIPS 204 §5.1, Algorithm 1
   calls ξ). It is stored in `node_signing_mldsa65.key` in `INodeSecretStore.NodeKeysDirectory`, and it is
   created and loaded by the hardened routine that already exists, `NodeSecretStore.LoadOrCreateKeyFile`.
2. The key id is the lowercase hex SHA-256 of the public key's SubjectPublicKeyInfo DER. It is the same
   derivation P8 and P9 use.
3. No public member returns the private key. Signing goes through `IKeyManagementService.SignWithNodeSigningKey`.
4. Ledger verification reports *why* a record failed. "Signed by the old ephemeral key, which can never
   be verified" and "signed by a key this node does not hold" are different outcomes, and neither is
   reported as "invalid signature".
5. The ML-KEM-768 `_nodeMasterKey` is **removed**. Nothing uses it (see Decision 2).

---

## Findings this design rests on (verified in source at `ce94d26`)

| # | Finding | Evidence |
|---|---|---|
| F1 | The signing key is ephemeral, and every node uses the fixed id `node_signing_master`. | `KeyManagementService.cs:57`, `Program.cs:109` |
| F2 | The node key is used at the three places the brief named: two signing sites and one verification site. | `ComplianceAuditService.cs:90`, `:139`, `:185` |
| F3 | **The audit ledger is held in memory only.** It is a `List<AuditRecord>` field (`ComplianceAuditService.cs:54`). No migration, table or file holds audit records: `grep -i "audit\|ledger\|signature" src/NanoWeaverHrt/Database` finds only the name of `Migration002_UserAuditAndIndexes`. Each process start writes a new genesis record (`:84-108`). | see left |
| F4 | So today's verification path is **not** failing. Every record in the current ledger was signed by the current process's key, which is still in memory. After a restart the old records are not "unverifiable", they are simply gone. | F3 |
| F5 | **`GetNodeMasterKey()` has no production caller.** `grep -rn GetNodeMasterKey src/` returns only its declaration and its implementation. The single test reference is `PostQuantumComplianceTests.cs:183` (`Assert.NotNull`). | grep |
| F6 | `/api/datasets/export-encrypted` encrypts to the **subject's** ML-KEM key (`EncryptedStorageService.cs:218`, `profile.KeyMaterial.PublicKeyBase64`), not to the node master key. | see left |
| F7 | `GetNodeSigningKey()` returns a `PqcKeyMaterial` whose `PrivateKeyBase64` (JSON name `privateKey`) holds the PKCS#8 private key. The private key is therefore reachable through a public interface member today. | `PqcKeyMaterial.cs:49-51`, `KeyManagementService.cs:65` |
| F8 | `NodeSecretStore.LoadOrCreateKeyFile` (`NodeSecretStore.cs:357`) handles exactly 32 bytes (`SecretLengthBytes`). It sets mode 0600 inside `open(2)`, publishes with an atomic rename, handles the lost-race case and checks the exact length. **It repairs the permissions of an existing file to 0600 (`:303`); it does not refuse to load it.** | see left |
| F9 | BouncyCastle 2.7.0 provides `MLDsaPrivateKeyParameters.FromSeed(MLDsaParameters, byte[])` and `GetPublicKey()`, per its shipped XML docs (`~/.nuget/packages/bouncycastle.cryptography/2.7.0/lib/net6.0/BouncyCastle.Cryptography.xml:22750`, `:22770`). | see left |
| F10 | Nothing in the code verifies a `.manifest.pqc.json`. | grep |

I could not run `git show 4799f27`, because I had no shell. The isolation that commit introduced is
described in `NodeSecretStore`'s type remarks and in P8's Decision 7: separate per-node random files for
the token and PHI keys, neither derived from a published constant. Criterion 19 protects it.

---

## Decisions

### Decision 1: storage format and mechanism

- **File:** `node_signing_mldsa65.key`, in `INodeSecretStore.NodeKeysDirectory`.
- **Contents:** the raw 32-byte ML-DSA seed ξ, with no header or encoding. The key pair is derived from it
  deterministically on every start with `MLDsaPrivateKeyParameters.FromSeed(MLDsaParameters.ml_dsa_65, seed)`
  (FIPS 204 §6.1, Algorithm 6 `ML-DSA.KeyGen_internal`). The seed is what RFC 9881 §8.1 recommends
  storing.
- **Mechanism:** `NodeSecretStore.LoadOrCreateKeyFile(secretStore.NodeKeysDirectory, NodeSigningKeyFileName)`.
  It is `internal static` in the same assembly. **No new file-handling code** is written: no new write
  routine, no new chmod, no new rename.
- **Why a seed and not PKCS#8 or the 4032-byte expanded key:** a 32-byte seed fits the existing
  hardened routine with no change. It is also the same storage unit P8 uses for its Ed25519 key (an
  Ed25519 private key is likewise a 32-byte seed, RFC 8032 §5.1.5).
- **The P8 Ed25519 dataset key reuses this mechanism, and should.** P8 already specifies
  `LoadOrCreateKeyFile` with `dataset_signing_ed25519.key`. The file names differ, so the two keys can
  never collide. No shared class is needed, and P8 does not have to change. P8's
  `DatasetManifestSignature.ComputeKeyId` should be implemented as a one-line call to this package's
  `PublicKeyFingerprint.ComputeKeyId`, so the codebase has one key-id function. That is an
  implementation note for P8, not a change to P8's signature (see Open questions).

**Behaviour for every file state.** This comes entirely from the reused routine, and is pinned by
criteria 7-10:

| State at start | Behaviour |
|---|---|
| File absent | Generate 32 bytes with `RandomNumberGenerator`, create the file with mode 0600 at `open(2)`, rename it into place atomically. Origin `GeneratedFile`. |
| File present, exactly 32 bytes | Load it, re-assert mode 0600. Origin `ExistingFile`. |
| File present, any other length (0, 31, 33, ...) | `NodeSecretProvisioningException` naming the file only. The host does not start. The file is **not** touched. |
| Path unreadable (IO or permission error) | `NodeSecretProvisioningException`. The host does not start. |
| File mode wider than 0600 (for example 0644) | **Repaired** to 0600 and loaded, as the token and PHI keys already are (F8). See the consistency check, item C. |
| Lost creation race with another process | Our bytes are zeroed and the winner's file is adopted (existing branch). |

**Never regenerate-and-continue.** A new key is generated only when `File.Exists` is false. Every failure
when reading an existing path throws. Do not add a `catch` in `KeyManagementService` that falls back to
`GenerateSigningKeyPair`.

**Accepted limitation:** any 32 bytes form a valid seed, so a file corrupted to another 32-byte value
loads as a *different* key and no error is raised. The token and PHI keys have the same property.
Operators detect it through the fingerprint in the startup log line and in the genesis record
(Decision 6). A fingerprint sidecar file would be a second file mechanism and is a non-goal.

### Decision 2: `_nodeMasterKey` (ML-KEM-768) is removed

It is ephemeral, the same flaw as the signing key, but **no data is encrypted to it** (F5, F6). So it
causes no data loss today, and it is *not* a second instance of the audit-signature defect. It is still
a trap: the first developer who uses "the node's master key for infrastructure and dataset envelope
encryption" (as its docblock invites) would create data that becomes permanently unreadable after a
restart.

Persisting it was rejected. An ML-KEM-768 seed is 64 bytes (FIPS 203 §7.1, `d || z`), so it does not fit
the 32-byte routine. Persisting it would need a second file format for a key that nothing uses.

**So:** delete the `_nodeMasterKey` field, the `GenerateKemKeyPair("node_storage_master")` call,
`KeyManagementService.GetNodeMasterKey()` and `IKeyManagementService.GetNodeMasterKey()`. If a node KEM
key is needed later, it arrives with its first consumer and its own persistence design.

> **The real second instance is the subject keys, and it is OUT OF SCOPE here.** See "Defects found
> outside this package" below. Read that section before closing this package.

### Decision 3: key id

`KeyId = lowercase hex(SHA-256(SubjectPublicKeyInfo DER of the ML-DSA-65 public key))`: 64 characters,
each `0-9a-f`. This is the same derivation as P8's `ComputeKeyId` and P9's `weaver_peer_keys.key_id`.
The id is a hash of a random per-node key, so it identifies the node, and two nodes cannot share one
unless they share the key file. The algorithm is carried separately (`PqcSignature.Algorithm`), so the
id has no prefix.

### Decision 4: existing data, and why there is no migration

**There are no persisted audit ledger rows (F3).** The brief's premise that "audit ledger rows already
exist that were signed by ephemeral keys" does not hold in this code. Nothing can be annotated, so
**no migration is written and `008` is not consumed.**

The artifacts that *do* carry signatures from the old ephemeral key are:

1. `*.manifest.pqc.json` files in export directories, each with `"signerKeyId": "node_signing_master"`.
2. Copies of `AuditRecord` JSON that clients fetched from `GET /api/compliance/audit-ledger` before this
   package landed.

Both are permanently unverifiable, because the public half of the key that signed them was never stored
either. The package handles them as follows:

- **Not deleted, not rewritten, not re-signed.** Re-signing an old manifest with the new key would
  attest, today, to something the new key never saw.
- **Recognisable.** The literal `node_signing_master` can never collide with a new key id, which is
  always 64 hex characters. The package records that literal as
  `ComplianceAuditService.LegacyEphemeralSigningKeyId`.
- **Classified, not conflated.** `VerifyLedgerIntegrity` gains a `ViolationKind` with three
  signature-related outcomes that are deliberately separate (Decision 5). This is dormant for the ledger
  today, because the ledger is in memory and always carries current-key signatures. It is written now
  so that the package which persists the ledger (see "Defects found outside this package") inherits the
  distinction, instead of reporting old records as tampered.
- **Documented.** The file-level docblock of `ComplianceAuditService` states in plain words (Directive 4)
  that signatures made before this change cannot be verified by anyone, and why.

### Decision 5: verification outcomes

New enum `AuditLedgerViolationKind`, in the order the checks run:

| Value | Meaning | `IsValid` |
|---|---|---|
| `None` | Every record passed. | true |
| `SequenceDiscontinuity` | Existing check 1. | false |
| `BrokenHashLink` | Existing check 2. | false |
| `TamperedPayload` | Existing check 3. | false |
| `LegacyEphemeralSigningKey` | `Signature.SignerKeyId == "node_signing_master"`. The key never existed outside one process, so the signature **can never be checked**. This is **not evidence of tampering.** | false |
| `UnknownSigningKey` | `SignerKeyId` is not this node's current key id. The signature **cannot be checked here**, because this node does not hold that public key. | false |
| `InvalidSignature` | The record names this node's key, and the signature was checked against that key **and failed**. | false |

- Checks 1-3 run first, unchanged. A record with a tampered payload **and** a legacy key id reports
  `TamperedPayload` (criterion 15).
- `LegacyEphemeralSigningKey` and `UnknownSigningKey` are decided **without** calling
  `VerifySignature`. Asking the wrong key to verify a signature and reporting the inevitable `false` as
  "invalid" is exactly the conflation this package exists to remove.
- `IsValid` keeps its meaning: "every record was verified". An unverifiable record was not verified, so
  `IsValid` is false. The *kind* is what tells an auditor this is different from forgery. `IsValid` is
  never weakened to true for an unverifiable record.
- Verification still stops at the first violation, as it does today.
- The existing message `$"Invalid ML-DSA-65 signature on audit record {n}."` is kept **byte-for-byte**.
  `PostQuantumComplianceTests.cs:300` asserts it.

### Decision 6: generate on first start; no `/setup` step

The key is created the first time `IKeyManagementService` is resolved, and `Program.cs` resolves it
eagerly at start, right after the node-secret block (`:216-220`). This is the same posture as the token
and PHI keys. A damaged key file therefore fails the start, not a request hours later.

It is **not** a `/setup` wizard step. The wizard initializes the master *Administrator identity*
(Directive 12). This key is node infrastructure, and the wizard's own actions are written to the audit
ledger through `UserAuthenticationService` (`UserAuthenticationService.cs:38`), so the key must exist
before `/setup` completes. Nothing in `/setup` changes.

To make each process start's key visible in the audit trail itself, the genesis record's `DetailsJson`
becomes exactly:

```
{"genesis":true,"signingKeyId":"<64 hex>","signingKeyOrigin":"<GeneratedFile|ExistingFile>"}
```

Both values are server-generated: hex, and an enum name. Build the string by interpolation in that exact
form. Do not use `JsonSerializer`, because the bytes are hashed and must be predictable.

### Decision 7: rotation is not supported, and must not be foreclosed

- Today, deleting `node_signing_mldsa65.key` mints a new key on the next start, the same as for the
  token and PHI keys. Signatures from the old key then classify as `UnknownSigningKey`, not
  `InvalidSignature`. Write this in the `KeyManagementService` remarks as an operator warning.
- What must stay open:
  1. Every signature carries its signer's key id. That is now meaningful.
  2. The verifier decides "which key checks this record" by looking up `SignerKeyId`. Today the lookup
     is a set of one, in a private helper (`TryGetVerificationKey`). Rotation adds retired public keys
     to that lookup. It must not "re-sign history".
  3. The current-key file name is fixed. A future rotation retires public keys alongside it (for
     example, a retired-keys directory or a database table). It never overwrites the current seed in
     place.
- Rotation tooling, a key history and a configuration override (such as `WEAVER_NODE_SIGNING_KEY`) are
  non-goals. Replicas that share the data volume share the file, as with P8.

### Decision 8: what is never exposed

- The seed exists only as a local inside the `KeyManagementService` constructor. It is zeroed with
  `CryptographicOperations.ZeroMemory` once the key pair has been derived.
- The PKCS#8 private key is held in **one** `private readonly string` field. No public, internal or
  protected member returns it.
- `GetNodeSigningKey()` returns `PrivateKeyBase64 = null`, always.
- Logs, `SecureProblemDetails`, API responses and the genesis record carry only the key id, origin and
  algorithm, and (in the compliance status report) the **public** key.
- `NodeSecretProvisioningException` messages already name only the file (`NodeSecretStore.cs:275-291`).
- **Publishing the public key.** Without it, nobody outside the process can verify a signature, so
  `ComplianceStatusReport` gains `AuditSigningKeyId` and `AuditSigningPublicKeyBase64`.
  `GET /api/compliance/status` is already Administrator-only (`Program.cs:3162`). No new endpoint and no
  new lambda.

---

## Files in scope

| Path | Role |
|---|---|
| `src/NanoWeaverHrt/Security/Cryptography/PublicKeyFingerprint.cs` (new) | Static, pure. `ComputeKeyId(ReadOnlySpan<byte> spkiDer)`. The one key-id function. |
| `src/NanoWeaverHrt/Security/Cryptography/IPostQuantumCryptoProvider.cs` | Append **one** member, `DeriveSigningKeyPairFromSeed`, at the end of the interface. |
| `src/NanoWeaverHrt/Security/Cryptography/PostQuantumCryptoProvider.cs` | Implement it as the **last** method of the class. |
| `src/NanoWeaverHrt/Security/KeyManagement/IKeyManagementService.cs` | Remove `GetNodeMasterKey`. Append `NodeSigningKeyOrigin` and `SignWithNodeSigningKey` at the end. Update the `GetNodeSigningKey` docblock: it returns public material only. |
| `src/NanoWeaverHrt/Security/KeyManagement/KeyManagementService.cs` | New constructor signature. Loads the seed. Removes the KEM master key. New members **at the end of the class**. Rewrite the type remarks (Directive 4). |
| `src/NanoWeaverHrt/Compliance/AuditLedgerViolationKind.cs` (new) | The enum from Decision 5, with `[JsonConverter(typeof(JsonStringEnumConverter))]` (house style, see `KeyAlgorithm.cs:8`). |
| `src/NanoWeaverHrt/Compliance/AuditLedgerVerificationResult.cs` | Append `ViolationKind` and `SigningKeyId` after `VerifiedAtUtc`. Update `GetExamples`. |
| `src/NanoWeaverHrt/Compliance/ComplianceStatusReport.cs` | Append `AuditSigningKeyId` and `AuditSigningPublicKeyBase64` after `GeneratedAtUtc`. Update `GetExamples` with obvious placeholders, not a real key. |
| `src/NanoWeaverHrt/Compliance/ComplianceAuditService.cs` | Sign through `SignWithNodeSigningKey` at `:90` and `:139`. Classify signers in `VerifyLedgerIntegrity`. New genesis details. Fill the two report fields in `GetComplianceStatus`. New const and private helper **at the end of the class**. |
| `src/NanoWeaverHrt/Security/Storage/EncryptedStorageService.cs` | `:227-231` becomes `_keyManagement.SignWithNodeSigningKey(...)`. No other change. |
| `src/NanoWeaverHrt/Program.cs` | One eager-resolution statement and one log line directly after `:220`. **No lambda.** Nothing else. |
| `tests/NanoWeaverHrt.Tests/NodeSigningKeyPersistenceTests.cs` (new) | Every new test this package requires. |
| `tests/NanoWeaverHrt.Tests/PostQuantumComplianceTests.cs` | **Required edits, listed exactly under "Existing tests this breaks".** |
| `tests/NanoWeaverHrt.Tests/AuthenticationServiceTests.cs` | **Required edit at `:81-92`**, listed below. |
| `coverage-baseline.json` | Regenerate only if step 7 reports it stale (Constraint 9). |

**Must not change:** `NodeSecretStore.cs`, `INodeSecretStore.cs` (a test double implements it at
`PatientPhiEncryptionTests.cs:108`), `NodeSecretOrigin.cs`, `NodeSecretProvisioningException.cs`,
`ISqlDialect`, any `Migration0*.cs`, `DatabaseMigrationRunner.cs`, `coverlet.runsettings`,
`coverage-full.runsettings`, `AGENTS.md`, anything under `docs/`, `work-packages/*.md`.

---

## Interfaces and signatures

```csharp
namespace NanoWeaverHrt.Security.Cryptography;

public static class PublicKeyFingerprint
{
    /// Lowercase hex SHA-256 of the SubjectPublicKeyInfo DER: 64 chars, 0-9a-f.
    /// Convert.ToHexStringLower(SHA256.HashData(spkiDer)). Accepts an empty span (hashes it).
    public static string ComputeKeyId(ReadOnlySpan<byte> subjectPublicKeyInfoDer);
}

public interface IPostQuantumCryptoProvider
{
    // ... existing members unchanged, in their existing order ...

    /// Deterministically expands a 32-byte FIPS 204 seed into an ML-DSA-65 key pair.
    /// Throws ArgumentException when seed.Length != 32 (message never contains seed bytes).
    /// Returns: KeyId = PublicKeyFingerprint.ComputeKeyId(SPKI DER), Algorithm = "ML-DSA-65",
    /// PublicKeyBase64 = Base64(SPKI DER), PrivateKeyBase64 = Base64(PKCS#8 DER from
    /// PrivateKeyInfoFactory), CreatedAtUtc = DateTimeOffset.UtcNow (documented as "time derived").
    PqcKeyMaterial DeriveSigningKeyPairFromSeed(ReadOnlySpan<byte> seed);
}
```

Implementation of `DeriveSigningKeyPairFromSeed`: copy the span to a `byte[]`, call
`MLDsaPrivateKeyParameters.FromSeed(MLDsaParameters.ml_dsa_65, copy)`, then `priv.GetPublicKey()`, and
encode with `SubjectPublicKeyInfoFactory` and `PrivateKeyInfoFactory` exactly as
`GenerateSigningKeyPair` does (`:93-94`). Zero the copy in a `finally`. If `FromSeed` or `GetPublicKey`
is missing or behaves differently in 2.7.0 than F9 says, **stop and report.** Do not hand-roll key
generation and do not change the package version.

```csharp
namespace NanoWeaverHrt.Security.KeyManagement;

public interface IKeyManagementService
{
    PqcKeyMaterial GetNodeSigningKey();           // KEPT; now ALWAYS PrivateKeyBase64 == null
    // GetNodeMasterKey()                         // REMOVED
    SubjectKeyProfile GetOrCreateSubjectKey(string subjectId);   // unchanged
    SubjectKeyProfile? GetSubjectKey(string subjectId);          // unchanged
    bool CryptoShredSubjectKey(string subjectId);                // unchanged
    IReadOnlyList<SubjectKeyProfile> GetAllSubjectProfiles();    // unchanged

    /// Provenance of the node signing key for this process start (GeneratedFile or ExistingFile).
    NodeSecretOrigin NodeSigningKeyOrigin { get; }               // NEW, appended

    /// Signs data with the node's ML-DSA-65 key. Returns SignerKeyId == GetNodeSigningKey().KeyId.
    PqcSignature SignWithNodeSigningKey(ReadOnlySpan<byte> data); // NEW, appended last
}

public sealed class KeyManagementService : IKeyManagementService
{
    /// File name of the persisted 32-byte ML-DSA-65 seed inside INodeSecretStore.NodeKeysDirectory.
    internal const string NodeSigningKeyFileName = "node_signing_mldsa65.key";   // appended at end

    /// Uses ONLY secretStore.NodeKeysDirectory. Never reads TokenSigningKey or PhiMasterKey.
    /// ArgumentNullException for either null. NodeSecretProvisioningException propagates unwrapped.
    public KeyManagementService(IPostQuantumCryptoProvider cryptoProvider, INodeSecretStore secretStore);
}
```

Constructor body, in order, with WHAT/WHY/HOW comments:

1. `ArgumentNullException.ThrowIfNull` for both parameters.
2. `(byte[] seed, NodeSecretOrigin origin) = NodeSecretStore.LoadOrCreateKeyFile(secretStore.NodeKeysDirectory, NodeSigningKeyFileName);`
3. `try { PqcKeyMaterial full = _cryptoProvider.DeriveSigningKeyPairFromSeed(seed); _nodeSigningPrivateKeyBase64 = full.PrivateKeyBase64!; /* justification: DeriveSigningKeyPairFromSeed always sets it */ _nodeSigningKey = full with { PrivateKeyBase64 = null }; } finally { CryptographicOperations.ZeroMemory(seed); }`
4. `NodeSigningKeyOrigin = origin;`

`SignWithNodeSigningKey(data) => _cryptoProvider.SignData(data, _nodeSigningPrivateKeyBase64, _nodeSigningKey.KeyId);`

```csharp
namespace NanoWeaverHrt.Compliance;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuditLedgerViolationKind
{
    None, SequenceDiscontinuity, BrokenHashLink, TamperedPayload,
    LegacyEphemeralSigningKey, UnknownSigningKey, InvalidSignature
}

// AuditLedgerVerificationResult, appended after VerifiedAtUtc:
[JsonPropertyName("violationKind")] public AuditLedgerViolationKind ViolationKind { get; init; } = AuditLedgerViolationKind.None;
[JsonPropertyName("signingKeyId")]  public string SigningKeyId { get; init; } = string.Empty;   // key the ledger was checked against

// ComplianceStatusReport, appended after GeneratedAtUtc:
[JsonPropertyName("auditSigningKeyId")]           public string AuditSigningKeyId { get; init; } = string.Empty;
[JsonPropertyName("auditSigningPublicKeyBase64")] public string AuditSigningPublicKeyBase64 { get; init; } = string.Empty;

// ComplianceAuditService, appended at end of class:
public const string LegacyEphemeralSigningKeyId = "node_signing_master";

/// true only when signerKeyId equals the current node key id (ordinal); publicKeyBase64 is then that key.
/// The single place rotation will later extend. Never throws.
private bool TryGetVerificationKey(string signerKeyId, out string publicKeyBase64);
```

### `VerifyLedgerIntegrity`: exact new step 4 (replaces `:240-255`)

```
signingKey = _keyManagement.GetNodeSigningKey()          // hoisted, as today (:185)
for each record (checks 1-3 unchanged, each result now also sets ViolationKind and SigningKeyId)
  4a. if SignerKeyId == LegacyEphemeralSigningKeyId (ordinal)
        -> ViolationKind = LegacyEphemeralSigningKey,
           Message = $"Audit record {seq} carries a signature from the pre-persistence ephemeral node key '{LegacyEphemeralSigningKeyId}'. That key was never stored, so this signature can never be verified. This is not evidence of tampering."
  4b. if !TryGetVerificationKey(SignerKeyId, out pub)
        -> ViolationKind = UnknownSigningKey,
           Message = $"Audit record {seq} is signed by a key that is not this node's audit signing key, so its signature cannot be verified here."
           (the record's SignerKeyId is NOT echoed: on a future persisted ledger it is stored data)
  4c. if !VerifySignature(UTF8(CurrentHash), SignatureBase64, pub)
        -> ViolationKind = InvalidSignature,
           Message = $"Invalid ML-DSA-65 signature on audit record {seq}."     // unchanged text
success -> IsValid = true, ViolationKind = None, SigningKeyId = signingKey.KeyId, Message unchanged (:263)
```

All failing results set `IsValid = false`, `TotalRecordsVerified = i`, and
`FirstViolationSequence = record.SequenceNumber`, as today.

### `Program.cs`: directly after `:220`

```csharp
// WHAT/WHY/HOW: same fail-at-start reasoning as :210-215; the fingerprint is public and is what an
// auditor uses to match ledger signatures to this node.
IKeyManagementService nodeKeys = app.Services.GetRequiredService<IKeyManagementService>();
app.Logger.LogInformation(
    "Node audit signing key ready. Algorithm: ML-DSA-65. Origin: {Origin}. Fingerprint: {KeyId}.",
    nodeKeys.NodeSigningKeyOrigin,
    nodeKeys.GetNodeSigningKey().KeyId);
```

`nodeKeys` is not used as a name anywhere in `Program.cs` today (checked). **P8's dataset-signer block
then goes after this block**, not directly after `:220` as P8 currently says.

---

## Existing tests this breaks, and the exact required change

Each one breaks because of a deliberate decision. **Do not delete any assertion other than the one
named at `:183`.** Every replacement keeps or strengthens what the original line guaranteed.

| File:line | Why it breaks | Required change |
|---|---|---|
| `PostQuantumComplianceTests.cs:179`, `:229`, `:311`, `:351`, `:545` | `new KeyManagementService(null!)` no longer compiles: the constructor now has two parameters. | Replace each one with **two** assertions: `new KeyManagementService(null!, store)` and `new KeyManagementService(provider, null!)`, both `ArgumentNullException`. This strengthens the guard coverage. |
| `PostQuantumComplianceTests.cs:180`, `:230`, `:312`, `:352`, `:546` | `new(provider)` does not compile. | `new(provider, store)`, where `store` is a `NodeSecretStore` built by a new private helper appended **at the end** of the test class. The helper sets `WEAVER_NODE_KEYS_DIR` to `Path.Combine(_testRoot, "unit-node-keys-" + Guid.NewGuid().ToString("N"))`. It never uses `_sharedDataDir`, which the class's web host also uses. |
| `PostQuantumComplianceTests.cs:183` | `GetNodeMasterKey()` is removed (Decision 2). | Replace it with `Assert.Null(keyMgmt.GetNodeSigningKey().PrivateKeyBase64);`. The old line guaranteed that a key nobody uses exists. The new line guarantees F7 is fixed. This is the **only** assertion removed, and the reason is Decision 2. |
| `AuthenticationServiceTests.cs:82` | `new KeyManagementService(_pqcProvider)` does not compile. | Move the existing `_secretStore = new NodeSecretStore(configuration);` (`:92`, with its comment block `:88-91`) **above** `:82`, then construct `new KeyManagementService(_pqcProvider, _secretStore)`. No assertion changes. |

Tests that **must pass unedited**. If one fails, stop and report. Do not edit it.

- `PostQuantumComplianceTests.cs:290-301`: the signature-tamper case. It keeps the key id and zeroes
  the bytes, so it lands in `InvalidSignature` with the unchanged message.
- `PostQuantumComplianceTests.cs:266-288`: the sequence, link and payload tamper cases.
- `PostQuantumComplianceTests.ComplianceEndpoints_HttpRequests_ReturnExpectedResponses` (`:425`): it
  deserializes `ComplianceStatusReport` and `AuditLedgerVerificationResult`. Both changes are additive.
- `RazorComponentLifecycleTests.cs:106-109`: DI already registers `INodeSecretStore` before
  `KeyManagementService`, so resolution succeeds.
- `SystemHealthEndpointTests.cs:603-604`, `:674-675`: the mocks build `AuditLedgerVerificationResult`
  with object initializers, and the new properties have defaults.
- `SystemIntegrationPipelineTests.cs:301`, `ApiRoleGatingAndDisclosureTests.cs:651`, `:1244`.
- `NodeSecretStoreTests.cs` in full. `NodeSecretStore` does not change.

Before finishing, run
`grep -rn "KeyManagementService(\|GetNodeMasterKey\|node_storage_master" tests/ src/` and account for
every hit in the report.

---

## Constraints

1. **New members go at the END of their class or interface.** The step-7 ratchet keys on compiler
   names such as `<ExportEncryptedDatasetBundleAsync>d__N` and `<<Main>b__4_N>d`
   (`coverage-baseline.json:13-58`). Inserting a method mid-class, or a lambda anywhere in `Main`,
   renumbers what follows it and reads as a regression. The `Program.cs` addition has no lambda. In
   `KeyManagementService`, `ComplianceAuditService`, `PostQuantumCryptoProvider` and both interfaces,
   append only.
   **The one permitted removal** is `GetNodeMasterKey` (above `GetOrCreateSubjectKey`). That class has no
   async method and no nested compiler-generated class. Its one lambda (`:85`) captures `this`, so it
   compiles to a method on `KeyManagementService` itself, and the class key the ratchet reads does not
   change. If step 7 nevertheless reports a new or renamed class under `KeyManagementService`, stop and
   report.
   Record types (`AuditLedgerVerificationResult`, `ComplianceStatusReport`) have no lambdas or state
   machines. Add the properties after the last existing property, as specified.
2. **`ISqlDialect` is untouched** (seven-member cap). This package has no database code.
3. **No migration.** Do not create `Migration008_*`. Do not touch `DatabaseMigrationRunner`.
4. **Directive 2 and gate step 6:** a full docblock on every new or changed member, private ones
   included, with one `<param>` per parameter and a `<returns>`. **Directive 3:** WHAT/WHY/HOW on the
   constructor load sequence, `DeriveSigningKeyPairFromSeed`, the new step 4 and the `Program.cs` block.
   **Directive 4:** the `KeyManagementService` remarks explain in plain English what a seed is, why
   deleting the file is destructive, and why pre-change signatures can never be verified.
5. **Directive 6:** no sleeps and no wall clock in any pinned value. Each test gets its own
   `Path.GetTempPath()` directory set through `WEAVER_NODE_KEYS_DIR` or `TRAJECTORY_INCOMING_DIR`.
   **Nothing under `simulations/`. No ambient network.** Unreadable-path tests use the directory-shaped-path
   technique from `NodeSecretStoreTests.cs:283-289`, which works the same as root and needs no guard. If
   any test does need an availability guard, write an explicit early return with a comment.
   **`Assert.Skip` does not compile against xunit 2.9.2.**
6. **No mocking of `DbConnection`, `DbCommand`, `DbDataReader` or `NpgsqlConnection`.** None is needed.
   Use the real `NodeSecretStore` and the real `PostQuantumCryptoProvider` throughout. Do not use a Moq
   `IKeyManagementService`.
7. **PKI algorithms are ML-DSA-65 and Ed25519 only.** No ECDSA and no RSA in code, comments, test names
   or messages.
8. The `!` operator only with an inline justification. Zero warnings (`/warnaserror`).
   `dotnet format NanoWeaverHrt.slnx --verify-no-changes` must be clean.
9. **Coverage ratchet:** no class's uncovered-line count may rise, and that includes a new class arriving
   with uncovered lines. If step 7 reports the baseline **stale**, run
   `python3 -I scripts/check-coverage-ratchet.py artifacts/coverage-full coverage-baseline.json --update`,
   include the result in the diff, and quote the old and new `line_rate`, `branch_rate` and
   `uncovered_lines_total`. **Never** pass `--allow-regression`. Do not change either runsettings file.
10. **Directive 14:** no log line, exception message, `SecureProblemDetails` detail or response body added
    or changed here contains the seed, the PKCS#8 key, a path or a directory name.
11. **Directive 1:** this is not a parser hot path, but still use no LINQ, `Regex` or `Split` in the new
    code.
12. Finish with `./scripts/verify-all.sh` and report all seven step results verbatim. State whether
    `WEAVER_TEST_POSTGRES` was set. This package has no database code, so either is acceptable, but say
    which.

---

## Acceptance criteria

All new tests go in `NodeSigningKeyPersistenceTests.cs` unless a criterion says otherwise.

**Key id and derivation**

1. `PublicKeyFingerprint.ComputeKeyId(ReadOnlySpan<byte>.Empty)` returns exactly
   `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`, the SHA-256 of the empty
   input. For random 100-byte input it equals `Convert.ToHexString(SHA256.HashData(x)).ToLowerInvariant()`.
2. `DeriveSigningKeyPairFromSeed` on the seed `00 01 02 ... 1f` (32 bytes), called twice, returns
   identical `KeyId` and `PublicKeyBase64`. `Algorithm == "ML-DSA-65"`. The decoded public key is
   **1974 bytes**, and its first 22 bytes are hex `308207b2300b0609608648016503040312038207a100` (DER SPKI,
   OID 2.16.840.1.101.3.4.3.18, no parameters, per RFC 9881). `KeyId` equals `ComputeKeyId` of those
   bytes and is 64 characters, each `0-9a-f`. These values were derived by hand from the DER rules. If
   BouncyCastle disagrees, **stop and report. Do not re-pin.**
3. Flipping one bit of the seed gives a different `KeyId`. Seeds of 31 and 33 bytes throw
   `ArgumentException`, and the message contains neither the hex nor the Base64 of the seed.
4. A signature from `SignData(data, derived.PrivateKeyBase64, derived.KeyId)` verifies with
   `VerifySignature(data, sig, derived.PublicKeyBase64)`, and fails for one changed byte of `data`.
   **Regression pin:** the `KeyId` for seed `00..1f` is pinned as a literal. Derive it once from the
   implementation, **after** criteria 1-3 pass, and say so in a comment.

**Persistence and restart survival**

5. Constructing `KeyManagementService` over an empty temp key directory creates
   `node_signing_mldsa65.key`: 32 bytes, `File.GetUnixFileMode == UserRead | UserWrite`,
   `NodeSigningKeyOrigin == GeneratedFile`. A second instance over the same directory reports
   `ExistingFile`, with the same `KeyId` and `PublicKeyBase64`.
6. **Restart survival, unit level:** a `SignWithNodeSigningKey` signature from instance A verifies with
   `VerifySignature` against instance B's `GetNodeSigningKey().PublicKeyBase64`, where B is a separate
   instance over the same directory. `SignerKeyId` equals both instances' `KeyId`. Two instances over
   **different** directories have different `KeyId`s.
7. Pre-writing the seed `00..1f` as the key file gives `KeyId` equal to criterion 4's pin, and
   `ExistingFile`.
8. A key file of 0, 31 and 33 bytes (one case each) makes the constructor throw
   `NodeSecretProvisioningException`. Afterwards the file is **byte-identical** to what the test wrote:
   it was not regenerated. The message contains `node_signing_mldsa65.key` and does not contain the temp
   directory path.
9. A **directory** at the key path (the technique in `NodeSecretStoreTests.cs:283-289`) makes the
   constructor throw `NodeSecretProvisioningException`. Afterwards the directory still exists, and no
   `*.tmp` file remains in the key directory.
10. A valid 32-byte key file with mode 0644 loads (`ExistingFile`, `KeyId` unchanged) and has mode 0600
    afterwards. This pins the reused routine's repair behaviour (Decision 1, consistency check item C).
11. **Restart survival, host level:** host 1, a `WebApplicationFactory<Program>` with
    `TRAJECTORY_INCOMING_DIR`, `TRAJECTORY_EXPORT_DIR` and `TRAJECTORY_PARQUET_DIR` bound to a temp
    root, as Administrator: reads `GET /api/compliance/status` (`auditSigningKeyId`, `auditSigningPublicKeyBase64`),
    reads `GET /api/compliance/audit-ledger`, and calls `POST /api/datasets/export-encrypted`. Dispose
    host 1. Host 2, over the **same** temp root, reports the same `auditSigningKeyId` and public key.
    Every ledger record fetched from host 1, and the `signature` in host 1's `.manifest.pqc.json` (over
    UTF-8 of its `sha256` field), verify with `PostQuantumCryptoProvider.VerifySignature` against
    **host 2's** public key. The manifest's `signerKeyId` equals that key id and is not
    `node_signing_master`.
12. A host whose key directory contains a 31-byte `node_signing_mldsa65.key` fails to start. Creating
    the client throws, with `NodeSecretProvisioningException` somewhere in the exception chain.
13. The host log contains exactly one line matching `Node audit signing key ready. Algorithm: ML-DSA-65. Origin: <origin>. Fingerprint: <64 hex>.`
    That line contains no `/`. Capture it with an `ILoggerProvider` registered in `WithWebHostBuilder`
    (`ConfigureLogging`). Do not read console output.

**Verification classification**

14. A fresh `ComplianceAuditService` plus two `LogAction` calls gives `IsValid == true`,
    `ViolationKind == None`, and `SigningKeyId == GetNodeSigningKey().KeyId`. The genesis record's
    `DetailsJson` equals, byte-for-byte,
    `{"genesis":true,"signingKeyId":"<that id>","signingKeyOrigin":"GeneratedFile"}` on a fresh key
    directory, and `...ExistingFile"}` on a second service over the same directory.
15. Using the reflection technique in `PostQuantumComplianceTests.cs:262-263`, one test case each:
    - (a) record 1's `Signature.SignerKeyId` set to `"node_signing_master"`, signature bytes left valid:
      `LegacyEphemeralSigningKey`, `FirstViolationSequence == 1`, the exact message from Decision 5.
    - (b) record 1 **re-signed by a different, valid ML-DSA-65 key** (derived from another seed), with
      that key's id: `UnknownSigningKey`, **not** `InvalidSignature`. This proves that a genuine signature
      from a key this node does not hold is not reported as forgery. The message does not contain the
      foreign key id.
    - (c) record 1's signature zeroed, key id unchanged: `InvalidSignature`, message
      `"Invalid ML-DSA-65 signature on audit record 1."`.
    - (d) record 1 with `Action` tampered **and** `SignerKeyId = "node_signing_master"`:
      `TamperedPayload`. Hash checks take precedence.
    - (e) the sequence, link and payload tampers report `SequenceDiscontinuity`, `BrokenHashLink` and
      `TamperedPayload` respectively.
16. For (a) and (b), prove `VerifySignature` was not consulted. Use a record whose `SignatureBase64` is
    **valid** for the current key, and assert the kind is still Legacy or Unknown. If the code verified
    first, it would pass. Also, for each of (a), (b) and (c), `GetComplianceStatus().OverallStatus == "Degraded"`.

**Exposure (Directive 14) and isolation (`4799f27`)**

17. `GetNodeSigningKey().PrivateKeyBase64` is null on every call. Reflection over `KeyManagementService`
    finds no **non-private** field or property of type `string`, `byte[]`, `ReadOnlyMemory<byte>` or
    `PqcKeyMaterial` other than what the interface declares. No member of `IKeyManagementService`
    returns `byte[]`, `ReadOnlyMemory<byte>`, `Memory<byte>` or `ReadOnlySpan<byte>`.
18. Over HTTP on a host bound to temp directories, with the seed read back from the test's
    `node-keys/node_signing_mldsa65.key`, none of these strings appear in the response bodies of
    `GET /api/compliance/status`, `GET /api/compliance/audit-ledger`, `GET /api/compliance/verify-ledger`
    and `POST /api/datasets/export-encrypted`, in the `.manifest.pqc.json` file, or in the captured host
    log:
    - the seed's Base64;
    - the seed's lowercase hex;
    - the PKCS#8 Base64, which the test re-derives through `DeriveSigningKeyPairFromSeed`;
    - the string `"privateKey":"` followed by any non-null value.
19. **Isolation:** `token_signing.key` and `phi_master.key` in the same directory are byte-identical
    before and after `KeyManagementService` is constructed. `node_signing_mldsa65.key` differs from both.
    `KeyManagementService.cs` contains neither `TokenSigningKey` nor `PhiMasterKey`.
20. Grep criteria over `src/`:
    - `GetNodeMasterKey`, `node_storage_master` and `GenerateSigningKeyPair("node_signing_master")`
      match nothing.
    - `node_signing_master` matches only `ComplianceAuditService.LegacyEphemeralSigningKeyId`, the
      existing remark at `NodeSecretStore.cs:17` and the existing comment at `UserAuthenticationService.cs:90`.
    - `signingKey.PrivateKeyBase64` and `nodeSigningKey.PrivateKeyBase64` match nothing.

**Unchanged behaviour and gate**

21. Every test listed under "must pass unedited" passes, and `git diff --stat` shows none of those files
    except the two named edit sites. The edits in `PostQuantumComplianceTests.cs` and
    `AuthenticationServiceTests.cs` are limited to the lines named above plus the one appended helper.
22. `./scripts/verify-all.sh` 7/7. Step 4 is exactly 100.00% line and 100.00% branch, with 0 warnings and
    0 incomplete docblocks. In step 7, no class's uncovered count rises. The baseline is either unchanged
    or updated per Constraint 9.

---

## Non-goals

- **Persisting the audit ledger itself** (F3). This is the actual Part 11 defect, and it needs its own
  package, with a table, migration `008` and a chain across restarts. This package is its prerequisite.
- **Persisting subject ML-KEM keys and crypto-shred state.** See "Defects found outside this package".
- Any key for `_nodeMasterKey`'s stated purpose. It is removed.
- Rotation tooling, a key history, a configuration override, a fingerprint sidecar file, and
  refuse-on-wrong-permissions (which would mean changing `NodeSecretStore` for all three keys).
- A verifier for `.manifest.pqc.json` files (F10).
- An ML-DSA context string (FIPS 204 §5.2) for domain separation between audit and manifest signatures.
  Today the two signed inputs cannot collide: the audit input is the UTF-8 of a 96-character SHA-384 hex
  string, the manifest input that of a 64-character SHA-256 hex string. A context string would need a
  provider signature change and is deferred.
- The Razor UI. `ComplianceSecurity.razor` needs no change.
- Documentation. `docs/compliance-and-security.md:78-79` will be false after this package: it describes
  `node_storage_master` and `node_signing_master`. That belongs to P9 Stage C or P12, and is reported,
  not fixed here (`docs/` is out of scope).

---

## Defects found outside this package (report to coordinator; do not fix here)

1. **Subject ML-KEM keys are ephemeral, and this is the real "second instance".**
   - `KeyManagementService._subjectProfiles` is an in-memory `ConcurrentDictionary` (`:43`).
   - The envelopes returned by `/api/compliance/encrypt-record`, and the `.enc.tar.gz` bundles written by
     `/api/datasets/export-encrypted` (`EncryptedStorageService.cs:218`), are encrypted to those subject
     keys.
   - **After a restart, all of them are permanently undecryptable.**
   - Worse, `DecryptSubjectRecord` then reports
     `"Private key has been permanently destroyed (Crypto-Shredded pursuant to GDPR Article 17)"`
     (`EncryptedStorageService.cs:143-146`) for a subject that was never shredded. That is a false
     compliance statement.
   - Crypto-shred state is also lost: after a restart, `GetOrCreateSubjectKey` mints a fresh key for a
     subject that was erased.
   - Fixing this means persisting private keys and keeping erasure semantics intact. That needs its own
     package, and it is a candidate for migration `008`.
2. **The audit ledger does not survive a restart** (F3). See Non-goals.
3. `PqcKeyMaterial` serializes the private key under the JSON name `privateKey`. After this package, the
   node key never carries it, but subject `KeyMaterial` still does. No endpoint returns a
   `SubjectKeyProfile` today. Any future one would leak.

---

## Open questions (non-blocking; defaults are stated)

- **P8 coordination.** After this lands, P8 Decision 7's rejection row for the ML-DSA key ("regenerated
  on every process start") is stale. Its other reason, cross-protocol separation, still holds, so P8's
  decision stands.
  - Recommended amendments to P8: (a) implement `ComputeKeyId` as `=> PublicKeyFingerprint.ComputeKeyId(der)`;
    (b) place its startup block after this package's block; (c) update the Decision 7 rationale. None of
    these changes P8's public contract.
- **Publishing the audit public key through P9's `GET /api/peer-keys` `localNode`.** The default is no:
  the compliance status report carries it, and P9's contract stays untouched.

---

## Criteria consistency check

I re-read criteria 1-22 as a set and checked every pair. Findings:

- **A. Criterion 5 (absent file → generate) versus the "never regenerate-and-continue" posture. Real
  tension, flagged.** A missing key file on a node that has signed before cannot be told apart from a
  first launch. Generating a key in that case is, in effect, a silent rotation.
  - Resolution, consistent with how the token and PHI keys already behave: *absent* means first launch.
    Every *present but unloadable* state fails (criteria 8, 9, 12). The rotation is never silent in the
    record: the origin `GeneratedFile` appears in the startup log (criterion 13) and inside the signed
    genesis record (criterion 14).
  - Also, an old signature then classifies as `UnknownSigningKey`, not `InvalidSignature` (criterion 15b).
  - If the coordinator wants a missing key file on a previously provisioned node to *fail*, that needs a
    durable "already provisioned" marker outside the key directory. That is a new mechanism for all three
    keys and a separate decision. **I recommend accepting the current resolution.**
- **B. Criterion 15(c) and the existing `PostQuantumComplianceTests.cs:300` versus the new
  classification.** Both hold, because 15(c) keeps the key id. The message text is pinned unchanged in
  Decision 5, so the existing test is not edited.
- **C. Criterion 10 (wrong mode is repaired) versus the brief's "fail loudly on wrong permissions".
  Real tension, flagged.** The reused routine repairs the mode to 0600 (F8). Refusing instead would mean
  either a second, divergent file routine (forbidden: reuse, do not invent) or changing `NodeSecretStore`
  for the token and PHI keys too (out of scope; it changes their behaviour).
  - The repair still fails closed in one sense: once it runs, the key is never left world-readable.
  - **I recommend keeping the repair.** If the coordinator rules for refusal, it belongs in a
    `NodeSecretStore` package covering all three keys. Criterion 10 would then be inverted there, not
    deleted.
- **D. Criterion 16 (Degraded for legacy and unknown keys) versus criterion 15's "not evidence of
  tampering".** These are consistent. `Degraded` says "not fully verified", which is true. The kind and
  the message say why. `IsValid` is not overloaded to mean "untampered".
- **E. Criterion 17 (no non-private key-bearing members) versus the provider's
  `DeriveSigningKeyPairFromSeed` returning `PrivateKeyBase64`.** No conflict. The provider holds no
  state, and returns key material only for a seed the caller already has, exactly as
  `GenerateSigningKeyPair` already does. Criterion 17 is scoped to `KeyManagementService` and
  `IKeyManagementService`, which hold the node seed.
- **F. Criterion 4 (self-derived pin) versus criterion 2 ("do not re-pin").** Different on purpose.
  Criterion 2's DER prefix and length are an independent derivation. Criterion 4's key id is a
  regression pin, taken only after 1-3 pass.
- **G. Criterion 11 (host restart) versus Directive 6 isolation.** Both hosts use one temp root, created
  by the test, and nothing else. Host 1 is disposed before host 2 is built, so there is no concurrency.
- **H. Criterion 20 (grep for `node_signing_master`) versus the legacy constant and two existing
  comments.** The criterion lists all three allowed sites explicitly, so the coder is not tempted to
  rewrite unrelated comments in `NodeSecretStore.cs` (a must-not-change file).
- **I. Constraint 1 (append only) versus Decision 2 (remove `GetNodeMasterKey`).** Removal is the single
  named exception. The reasoning is in Constraint 1, and so is the instruction to stop if step 7 sees a
  renamed class.
- No other pair conflicts. **No guarantee in an existing test is dropped except
  `PostQuantumComplianceTests.cs:183`.** That removal is named, justified by Decision 2, and replaced by a
  stronger assertion.
