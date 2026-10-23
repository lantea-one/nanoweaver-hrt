# Work package P8 — dataset manifest schema, canonical signing bytes, signing on export

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

**Agent:** coder. **Base commit: `6a00fdb`** (plus one pending comments-only commit; nothing that
landed after `5018359` touched `.cs`, migrations, tests or `docs/`). Your worktree branches from the
session-start commit, so your FIRST action is `git rebase main`. Report the rebase result before
anything else. Do not commit. Do not push.

**Dependencies:** none. **Blocks:** P9 (`work-packages/peer-key-registry.md`), which consumes every
type this package defines. **P8 must not depend on P9 existing:** no registry, no migration, no new
endpoint, no import enforcement here.

---

## Goal

`POST /api/datasets/import` compares the archive to a SHA-256 that the uploader supplied in the same
request, so it detects transit corruption and nothing else. This package builds the half of the fix
that can land on its own:

1. Optional signature fields on `DatasetManifest`, so an old unsigned manifest still deserializes.
2. **One** canonical, unambiguous byte encoding of a manifest. That encoding is what gets signed.
3. A persisted, per-node **Ed25519 dataset signing key**, and signing on export.
4. A pure verification primitive P9 will call. P8 wires it into **nothing**.

After P8 the import path behaves **exactly** as it does today. Signed manifests are accepted
because the signature is ignored. That is intended; P9 adds enforcement.

---

## Files in scope

| Path | Role |
|---|---|
| `src/NanoWeaverHrt/Models/DatasetManifest.cs` | Add four optional properties to `DatasetManifest`. Update its `<remarks>` so they describe what is actually signed. |
| `src/NanoWeaverHrt/Security/Signing/DatasetManifestCanonicalizer.cs` (new) | Static, pure. Produces the canonical signing bytes. Validates nothing except null and UTF-16 well-formedness. |
| `src/NanoWeaverHrt/Security/Signing/DatasetManifestSignature.cs` (new) | Static. The constants (tag, version, algorithm names), `GetState` (classifies a manifest as unsigned, signed or malformed), `TryParseAlgorithm`, `ComputeKeyId`. |
| `src/NanoWeaverHrt/Security/Signing/INodeDatasetSigner.cs` (new) | The public half of this node's dataset signing identity, plus `Sign`. Never exposes private bytes. |
| `src/NanoWeaverHrt/Security/Signing/NodeDatasetSigner.cs` (new) | Loads or creates `dataset_signing_ed25519.key` through the existing hardened `NodeSecretStore` file routine. |
| `src/NanoWeaverHrt/Security/Signing/DatasetManifestSignatureVerifier.cs` (new) | Checks a manifest against a caller-supplied trusted public key (Ed25519 or ML-DSA-65). Never throws on bad input. |
| `src/NanoWeaverHrt/Services/DatasetTransferService.cs` | The constructor gains an optional third parameter. The export path signs. A new private static helper goes **at the end of the class**. `ImportDatasetAsync` is **not touched**. |
| `src/NanoWeaverHrt/Program.cs` | Two type-based DI registrations, plus one eager-resolution statement and one log line after the existing node-secret block (`:216-220`). Nothing else. |
| `tests/NanoWeaverHrt.Tests/DatasetManifestSigningTests.cs` (new) | Every test this package requires. |
| `coverage-baseline.json` | Regenerate only if step 7 reports it stale (see Constraints 9). |

**Read-only context:** `Security/Secrets/NodeSecretStore.cs` (`LoadOrCreateKeyFile`, `ReadSecretFile`,
the type `<remarks>`), `Security/Cryptography/PostQuantumCryptoProvider.cs:267` (`VerifySignature`),
`Security/Authentication/UserAuthenticationService.cs:579-602` (house Ed25519 verify idiom),
`Security/Authentication/KeyAlgorithm.cs`.

**Must not change:** `ImportDatasetAsync`, `IDatasetManager`, `INodeSecretStore` (a test double at
`PatientPhiEncryptionTests.cs:117` implements it, so adding a member breaks the build),
`KeyManagementService`, any `Migration0*.cs`, the five round-trip test files listed under
Constraints 3.

---

## Decision 7 — which key signs

**A new key: a persisted Ed25519 key used only for dataset manifests, file `dataset_signing_ed25519.key`
in `INodeSecretStore.NodeKeysDirectory`.** Neither existing key can do the job:

| Candidate | Why it is rejected |
|---|---|
| `INodeSecretStore.TokenSigningKey` / `PhiMasterKey` (commit `4799f27`) | Both are **symmetric** (HMAC-SHA256, AES-256-GCM). A peer could only verify with the secret, and holding the secret would let the peer forge this node's bearer tokens or decrypt its PHI. Using either one would destroy the isolation `4799f27` introduced. |
| `IKeyManagementService.GetNodeSigningKey()` (the ML-DSA-65 key `/api/datasets/export-encrypted` uses) | **It is regenerated on every process start** (`KeyManagementService.cs:57`, constructor), with the fixed key id `"node_signing_master"`. No peer could register it, because it is gone after the next restart. It is also the Part 11 audit-ledger signing key (`ComplianceAuditService.cs:90,139,185`). Sharing one key between two protocols invites cross-protocol signature confusion. |

How isolation is preserved:
- It is a separate file, separate key material and a separate purpose. The token and PHI keys are not
  read, derived from or touched.
- The file is created by `NodeSecretStore.LoadOrCreateKeyFile` (internal static, same assembly), so it
  gets mode 0600 at `open(2)` time, atomic publication, the lost-race branch and the exact-length check
  with no new file-handling code. An Ed25519 private key **is** a 32-byte random seed (RFC 8032 §5.1.5),
  so `SecretLengthBytes = 32` fits exactly.
- The seed is held in a `private readonly byte[]` inside `NodeDatasetSigner` and **no member returns
  it**, whether public, internal or protected.
- `NodeKeysDirectory` is a subdirectory of the data volume. Export packs top-level files only
  (`DatasetTransferService.cs:175`), and `.key` is not in `DatasetMemberPolicy.AllowedMemberExtensions`.
  So the file is unreachable by export, import and download.
- **No configuration override** (no `WEAVER_DATASET_SIGNING_KEY`). It is a non-goal. Replicas sharing
  the data volume share the file.

Why Ed25519 and not ML-DSA-65 for *signing*: it is deterministic (RFC 8032), so the known-answer tests
are exact, and its private key is exactly the 32-byte unit the hardened file routine already handles.
*Verification* supports both algorithms, so a peer that signs with ML-DSA-65 can still be registered
in P9.

---

## Decision 2 — manifest schema additions

Add to `DatasetManifest`, **after** `IncludedFiles` and before `GetExamples`. Every property is
nullable and carries `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`, so **an unsigned
manifest serializes byte-for-byte as it does today**:

| Property | JSON name | Type | Meaning |
|---|---|---|---|
| `ManifestVersion` | `manifestVersion` | `int?` | `null` means a legacy v1 manifest. A signed manifest is always `2`. |
| `SignatureAlgorithm` | `signatureAlgorithm` | `string?` | Exactly `"Ed25519"` or `"MlDsa65"` (the `KeyAlgorithm` member names, compared ordinally). Not `"ML-DSA-65"`: one spelling per algorithm, so the canonical bytes have one form. It is a `string`, not the enum, so an unknown value reaches the controlled "malformed" path instead of a `JsonException`. |
| `SignerKeyId` | `signerKeyId` | `string?` | Lowercase hex SHA-256 of the signer's SubjectPublicKeyInfo DER: exactly 64 characters, each `0-9` or `a-f`. |
| `SignatureBase64` | `signature` | `string?` | Standard padded Base64 of the raw signature bytes. |

`GetExamples()` stays unsigned. Do not change it.

---

## Decision 1 — what is signed, and the canonical bytes

### The rule in one sentence

**Every manifest field except `SignatureBase64` is signed, framed by length, in a fixed order, read
from the deserialized object and never from JSON text.**

### Layout (all integers big-endian, written with `BinaryPrimitives`)

```
U32(n)  := 4-byte unsigned big-endian
I64(v)  := 8-byte two's-complement big-endian
F(s)    := U32(byte length of UTF8(s)) || UTF8(s)        -- length is in BYTES, not chars
UTF8    := new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)

canonical(m) :=
     F("NWHRT-DATASET-MANIFEST")        -- domain-separation tag, ASCII, 22 bytes
  || U32(m.ManifestVersion)              -- must be non-null; signer writes 2
  || F(m.SignatureAlgorithm)
  || F(m.SignerKeyId)
  || F(m.Sha256Checksum)
  || F(m.DatasetName)
  || F(m.ArchiveFileName)
  || I64(m.SizeInBytes)
  || F(m.TimestampUtc)
  || U32(m.IncludedFiles.Length) || F(m.IncludedFiles[0]) || ... || F(m.IncludedFiles[n-1])
```

### Rules that make two implementations agree

1. **Fixed order.** It is the order above, always. The brief's required five fields
   (`Sha256Checksum`, `DatasetName`, `ArchiveFileName`, `SizeInBytes`, `TimestampUtc`) appear in that
   relative order.
2. **No JSON.** The encoder reads properties off the `DatasetManifest` instance. Property order,
   whitespace, escaping (a raw micro sign versus its JSON escape sequence), number formatting and the
   serializer's options are irrelevant by construction. Never sign `JsonSerializer.Serialize(...)`
   output.
3. **Strings are taken verbatim.** No trimming, no case folding, no Unicode normalization, no
   re-parsing. `TimestampUtc` is a `string` in the model and is signed **as the string it is**. It is
   never parsed into a `DateTime` and re-formatted, so no culture and no round-trip precision can
   alter it. The signer produces it as `DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)`.
   That is the existing `"o"` format with the culture stated explicitly, and it changes no bytes.
4. **Numbers are binary, not text.** `ManifestVersion` is `U32`, `SizeInBytes` is `I64`, and the
   file count is `U32`. No `ToString`, so no culture, no thousands separator, no locale digit set.
   That is a stronger guarantee than "invariant-culture formatting".
5. **`IncludedFiles` is encoded in array order.** It is not sorted, so the signer and the verifier
   see the same order from the same JSON array.
6. **Strict UTF-8.** A string containing an unpaired surrogate throws `ArgumentException` (wrapping
   `EncoderFallbackException`). The default `Encoding.UTF8` silently replaces it with U+FFFD, which
   would let two different strings encode to the same bytes. That is exactly the ambiguity this
   design exists to rule out.
7. **Nulls are rejected, never defaulted.** A null `ManifestVersion`, null string field, null
   `IncludedFiles` array or null element throws `ArgumentException`. There is no "null means empty"
   equivalence.
8. **How the signature excludes itself.** `SignatureBase64` **is not in the field list**. The encoder
   never reads it. There is no "serialize, then delete the `signature` property" step that could leave
   a residue or depend on its position. The known-answer test proves this: it encodes the same
   manifest with `SignatureBase64 = null` and with `SignatureBase64 = "IGNORED"`, and asserts identical
   bytes.
9. **The canonicalizer does not validate semantics.** Key-id format, algorithm name and version value
   are checked by `DatasetManifestSignature.GetState`, not here. This keeps the encoder a pure function
   that the known-answer test can drive with short literals.

### Why length-prefixing and not plain concatenation or a delimiter

Plain concatenation lets bytes slide between adjacent fields while the signature stays valid. For
example, `DatasetName = "ab"`, `ArchiveFileName = "c.tar.gz"` and `DatasetName = "abc"`,
`ArchiveFileName = ".tar.gz"` produce the same concatenated bytes, so one signature authorizes both
manifests. A delimiter (pipe, newline, NUL) fails as soon as the delimiter can occur inside a field.
Linux file names may contain the first two, and `IncludedFiles` is attacker-shaped. A byte-length
prefix on every variable-length field, plus a count prefix on the list, makes the decomposition
unique. The domain tag means a signature made by this key over any other message format can never
parse as a manifest signature.

### Implementation constraints (Directive 1)

Compute the exact total length first, allocate **one** `byte[]` of that size, and write it with a
`Span<byte>` cursor and `BinaryPrimitives.WriteUInt32BigEndian` / `WriteInt64BigEndian` plus
`UTF8.GetBytes(string, Span<byte>)`. No LINQ, no `string.Split`, no `Regex`, no `MemoryStream`, no
intermediate per-field arrays. Each field is UTF-8-encoded twice (once to measure with
`UTF8.GetByteCount`, once to write). Accept that cost.

---

## Interfaces and signatures

```csharp
namespace NanoWeaverHrt.Security.Signing;

/// Classification of a manifest's signature block, decided before any key lookup.
public enum DatasetManifestSignatureState
{
    Unsigned,    // SignatureAlgorithm, SignerKeyId and SignatureBase64 are ALL null
    Signed,      // all three present and well-formed, ManifestVersion == 2
    Malformed    // anything else: partial block, empty/whitespace value, unknown algorithm,
                 // key id not exactly 64 lowercase hex chars, ManifestVersion != 2
}

public static class DatasetManifestSignature
{
    public const string DomainTag = "NWHRT-DATASET-MANIFEST";
    public const int SignedManifestVersion = 2;
    public const string Ed25519AlgorithmName = "Ed25519";   // == nameof(KeyAlgorithm.Ed25519)
    public const string MlDsa65AlgorithmName = "MlDsa65";   // == nameof(KeyAlgorithm.MlDsa65)

    /// Pure, never throws for a non-null manifest. ManifestVersion alone never makes a manifest "signed".
    public static DatasetManifestSignatureState GetState(DatasetManifest manifest);

    /// Ordinal map from the manifest string to the enum; false for anything else.
    public static bool TryParseAlgorithm(string? name, out KeyAlgorithm algorithm);

    /// Lowercase hex SHA-256 of the DER bytes. The one key-id function both packages use.
    public static string ComputeKeyId(ReadOnlySpan<byte> subjectPublicKeyInfoDer);
}

public static class DatasetManifestCanonicalizer
{
    /// The bytes that are signed. Throws ArgumentException on any null field or ill-formed UTF-16.
    public static byte[] GetSigningBytes(DatasetManifest manifest);
}

public interface INodeDatasetSigner
{
    string KeyId { get; }                 // DatasetManifestSignature.ComputeKeyId(SPKI DER)
    KeyAlgorithm Algorithm { get; }       // always KeyAlgorithm.Ed25519
    string PublicKeyBase64 { get; }       // Base64 SubjectPublicKeyInfo DER, same format as weaver_users.public_key
    NodeSecretOrigin KeyOrigin { get; }   // from LoadOrCreateKeyFile, for the startup log line

    /// Returns a copy (`with`) carrying ManifestVersion = 2, SignatureAlgorithm = "Ed25519",
    /// SignerKeyId = KeyId and SignatureBase64. The three metadata fields are set FIRST, then the
    /// canonical bytes are computed (they are part of the signed input), then the signature is attached.
    /// Any SignatureBase64 already on the input is ignored and replaced.
    DatasetManifest Sign(DatasetManifest manifest);
}

public sealed class NodeDatasetSigner : INodeDatasetSigner
{
    internal const string KeyFileName = "dataset_signing_ed25519.key";
    public NodeDatasetSigner(INodeSecretStore secretStore);       // uses ONLY secretStore.NodeKeysDirectory
    internal byte[] SignCanonicalBytes(ReadOnlySpan<byte> data);  // raw Ed25519 signature; used by Sign and by the RFC 8032 test
}

public sealed class DatasetManifestSignatureVerifier
{
    public DatasetManifestSignatureVerifier(IPostQuantumCryptoProvider pqcProvider);

    /// True only if: GetState(manifest) == Signed; the manifest's SignatureAlgorithm names
    /// trustedAlgorithm; manifest.SignerKeyId == ComputeKeyId(DER of trustedPublicKeyBase64);
    /// and the signature verifies over GetSigningBytes(manifest). Every exception, including a bad
    /// Base64 value, a bad key or a canonicalizer ArgumentException, becomes false. Never throws for a
    /// non-null manifest.
    public bool Verify(DatasetManifest manifest, KeyAlgorithm trustedAlgorithm, string trustedPublicKeyBase64);
}
```

- Ed25519 verification uses BouncyCastle directly (`PublicKeyFactory.CreateKey`, `Ed25519Signer`), as
  in `UserAuthenticationService.cs:583-591`. ML-DSA-65 goes through
  `IPostQuantumCryptoProvider.VerifySignature`, so the codebase keeps **one** ML-DSA implementation.
- The public key's SPKI DER comes from
  `SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pub).GetDerEncoded()`. For Ed25519 that is
  the 12-byte prefix `302a300506032b6570032100` followed by the 32-byte key.
- Key-id format check (64 chars, each `0-9` or `a-f`) is a plain loop. No `Regex`.
- **No ECDSA and no RSA anywhere**, not even in a comment, an enum or an error message.

### `DatasetTransferService`

```csharp
public DatasetTransferService(
    IConfiguration configuration,
    ILogger<DatasetTransferService> logger,
    INodeDatasetSigner? signer = null);
```

- **One constructor with an optional parameter, not a second overload.** Every existing
  `new DatasetTransferService(config, logger)` call site keeps compiling unchanged, including
  `ComprehensiveCoverageTests.cs:394` (`null!` logger), `DatasetTransferTests.cs:53`,
  `DatasetImportHardeningTests.cs:205,749`, and the `sc.AddSingleton<IDatasetManager, DatasetTransferService>()`
  at `RazorComponentLifecycleTests.cs:94`, which registers no signer and therefore gets `null`.
  Microsoft.Extensions.DependencyInjection honours parameter default values, so the host gets the
  registered signer.
- `ExportDatasetAsync` builds the manifest exactly as now. The `TimestampUtc` line gains
  `CultureInfo.InvariantCulture`. Then `manifest = ApplySignature(manifest, _signer);` runs
  **before** the JSON file is written, so the file on disk and the returned object are the same
  signed manifest.
- New helper, **placed after `FormatBytes`, as the last member of the class**:
  `private static DatasetManifest ApplySignature(DatasetManifest manifest, INodeDatasetSigner? signer)`.
  It returns `signer is null ? manifest : signer.Sign(manifest)`. It is non-async on purpose, so gate
  step 4 sees both branches. The async state machine around it is invisible to step 4 (see
  Constraints 8).
- `ImportDatasetAsync` is **byte-for-byte unchanged** in P8.

### `Program.cs`

Next to the existing `AddSingleton<INodeSecretStore, NodeSecretStore>()` (`:107-108`):

```csharp
builder.Services.AddSingleton<INodeDatasetSigner, NodeDatasetSigner>();
builder.Services.AddSingleton<DatasetManifestSignatureVerifier>();
```

Directly after the node-secret log line (`:217-220`), with WHAT/WHY/HOW comments:

```csharp
INodeDatasetSigner datasetSigner = app.Services.GetRequiredService<INodeDatasetSigner>();
app.Logger.LogInformation(
    "Dataset signing key ready. Origin: {Origin}. Fingerprint: {KeyId}.",
    datasetSigner.KeyOrigin, datasetSigner.KeyId);
```

This is fail-at-start, for the same reason given at `:211-215`. The fingerprint is public. It is what
an operator reads out to a peer administrator so the peer can verify it out of band before
registering it in P9.

**Both registrations are type-based and the startup lines contain no lambda. That is load-bearing.**
Gate step 7 keys `Program`'s endpoint closures by compiler ordinal (`<<Main>b__4_N>d`, see
`coverage-baseline.json:24-37`). Any new lambda in `Main` (`sp => ...`, a delegate, a local
function) renumbers every later endpoint, and the ratchet reads that as per-class regressions.

---

## Constraints

1. **New members go at the END of their class.** The step-7 ratchet keys on compiler names such as
   `<ExportDatasetAsync>d__18` and `<<Main>b__4_60>d`. Inserting a method mid-class, or a lambda
   mid-`Main`, renumbers its successors and reads as a regression. In existing classes
   (`DatasetTransferService`, `DatasetManifest`, `Program`) append only. New files are unconstrained
   at creation.
2. **Do not add a member to `INodeSecretStore`, `IDatasetManager` or `IConfigurationRepository`.**
   Each has a hand-written implementation in tests that would stop compiling.
3. **The five round-trip files pass unchanged and are not edited:** `DatasetTransferTests`,
   `EndpointIntegrationTests`, `ComprehensiveCoverageTests`, `DatasetImportHardeningTests`,
   `ApiRoleGatingAndDisclosureTests`. They are safe for two reasons. Service-level tests build the
   service without a signer, so their exports stay unsigned. The host-level round trip
   (`ComprehensiveCoverageTests.ImportDataset_ValidArchiveShouldSucceed`, `:447`) now exports a
   *signed* manifest, which P8's unchanged import accepts by ignoring the signature. If any of the five
   fails, **stop and report**. Do not edit the test.
4. Directive 2 and gate step 6: a full docblock on every new member, private included, with one
   `<param>` per parameter and a `<returns>`. Directive 3: WHAT/WHY/HOW on the canonicalizer's write
   loop, `GetState`, `Sign`, `Verify` and the startup block. Directive 4: explain in the remarks of
   `DatasetManifestCanonicalizer`, in plain English, why ambiguous concatenation is forgeable, using
   the `"ab"`/`"c.tar.gz"` example.
5. Directive 6: no sleeps, no wall clock in any assertion that pins bytes, and every test in its own
   `Path.GetTempPath()` directory. **Nothing under `simulations/`.** Set `WEAVER_NODE_KEYS_DIR` or
   `TRAJECTORY_INCOMING_DIR` in the test configuration, as `NodeSecretStoreTests.cs:69` does.
6. The `!` operator only with an inline justification comment. Zero warnings.
   `dotnet format --verify-no-changes` clean.
7. PKI algorithms: Ed25519 and ML-DSA-65 only. ECDSA and RSA must not appear in code, comments,
   docblocks or test names.
8. **Async methods are currently invisible to gate step 4.** `coverlet.runsettings` excludes
   `CompilerGeneratedAttribute`, so the bodies of `ExportDatasetAsync` and every endpoint lambda are
   measured only by step 7. Write the tests anyway. Do not rely on the exclusion and **do not change
   `coverlet.runsettings` or `coverage-full.runsettings`**.
9. **Coverage ratchet:** no class's uncovered-line count may rise, and that includes a brand-new class
   arriving with uncovered lines. If step 7 reports the baseline **stale** (rates rose by more than its
   0.05 tolerance because covered code was added), run
   `python3 -I scripts/check-coverage-ratchet.py artifacts/coverage-full coverage-baseline.json --update`
   and include the new baseline in this diff. Quote the old and new `line_rate`, `branch_rate` and
   `uncovered_lines_total`. **Never** pass `--allow-regression`.
10. Directive 14: no exception message, path, seed, signature input or key material reaches a log
    line added here, except the public key id.
11. Do not edit `AGENTS.md` or any file under `docs/` in P8. P9 owns the documentation pass.
12. Finish with `./scripts/verify-all.sh` and report all seven step results verbatim. State whether
    `WEAVER_TEST_POSTGRES` was set. P8 adds no database code, so either is acceptable, but say which.

---

## Acceptance criteria

**Canonical bytes**

1. **Known-answer test, pinned as a hex literal.** `DatasetManifestCanonicalizer.GetSigningBytes` on
   this manifest:

   | field | value |
   |---|---|
   | `ManifestVersion` | `2` |
   | `SignatureAlgorithm` | `"Ed25519"` |
   | `SignerKeyId` | `"k1"` |
   | `Sha256Checksum` | `"ab"` |
   | `DatasetName` | the two-character string `d` followed by U+00B5 MICRO SIGN (three UTF-8 bytes); in C# write it with a `\u` escape for the second character so the source file stays ASCII |
   | `ArchiveFileName` | `"d.tar.gz"` |
   | `SizeInBytes` | `258` |
   | `TimestampUtc` | `"2026-10-20T18:00:00.0000000Z"` |
   | `IncludedFiles` | `["a.xyz"]` |
   | `SignatureBase64` | `null` in one assertion, `"IGNORED"` in a second |

   returns exactly these 125 bytes in both assertions. They are shown segmented here. The test literal
   is the concatenation with whitespace removed (250 lowercase hex characters), compared with
   `Convert.ToHexString(bytes).ToLowerInvariant()`:

   ```
   00000016 4e574852542d444154415345542d4d414e4946455354
   00000002
   00000007 45643235353139
   00000002 6b31
   00000002 6162
   00000003 64c2b5
   00000008 642e7461722e677a
   0000000000000102
   0000001c 323032362d31302d32305431383a30303a30302e303030303030305a
   00000001
   00000005 612e78797a
   ```

   This value was derived by hand from the layout above, independently of any implementation. If your
   implementation disagrees, **the implementation is wrong**, or the spec is ambiguous. In that case
   report it. Do not re-pin the literal to match your output.
2. Field-boundary tests: (`DatasetName "ab"`, `ArchiveFileName "c"`) and (`"a"`, `"bc"`) give
   different bytes. `IncludedFiles` of `["ab","c"]`, `["a","bc"]`, `["abc"]` and `[]` give four
   pairwise-different byte arrays.
3. Each of the nine signed inputs, changed alone (including `SizeInBytes` 258→259 and one character of
   `TimestampUtc`), changes the bytes. `SignatureBase64` changed alone does not.
4. `GetSigningBytes` throws `ArgumentException` for each of: null `ManifestVersion`, each null string
   field, null `IncludedFiles`, a null element, and a field containing a lone high surrogate (the C#
   `\u` escape for code unit D800).
5. A grep of the new `Security/Signing/*.cs` files finds no `JsonSerializer`, `Regex`, `Split(`,
   `Encoding.UTF8.` or LINQ operator (`.Select(`, `.Where(`, `.ToArray(`).

**Signature-block classification**

6. `GetState` returns `Unsigned` when all three signature fields are null, **including when
   `ManifestVersion` is 2**. It returns `Signed` for a well-formed block with version 2. It returns
   `Malformed` for each of these, one test case each: one field present; two fields present; an empty
   string; whitespace; algorithm `"ML-DSA-65"`; algorithm `"ed25519"` (wrong case); a 63-character key
   id; a 64-character key id containing an uppercase hex digit; a non-hex character; version null;
   version 1; version 3.
7. `TryParseAlgorithm` maps exactly the two names and rejects null, `""` and every other spelling.
   `ComputeKeyId` is checked against an independent `SHA256.HashData` of the same bytes.

**Signer**

8. **RFC 8032 §7.1 TEST 1 wiring check.** Write the TEST 1 secret seed
   `9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60` as the 32-byte key file into a
   temp key directory, then construct `NodeDatasetSigner`. The last 32 bytes of the decoded
   `PublicKeyBase64` equal the RFC public key
   `d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a`. The first 12 bytes equal
   `302a300506032b6570032100`. `SignCanonicalBytes` of the empty message equals the RFC signature
   `e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b`.
   (Values are quoted from RFC 8032 §7.1. The RFC is authoritative if they differ.)
9. With that same seed, `KeyId` equals `ComputeKeyId` of the decoded `PublicKeyBase64`. **A regression
   pin:** `Sign` applied to a fixed manifest (fixed timestamp string, no wall clock) produces a
   `SignatureBase64` that the test pins as a literal. Derive the literal **once**, from the
   implementation, **after** criteria 1 and 8 both pass, and say so in a comment.
10. `Sign` sets `ManifestVersion = 2`, `SignatureAlgorithm = "Ed25519"` and `SignerKeyId = KeyId`. It
    replaces a pre-existing `SignatureBase64`. It leaves every other field equal to the input. The
    result has `GetState == Signed`.
11. Key file lifecycle: a first construction creates `dataset_signing_ed25519.key` (32 bytes, mode
    0600) and reports `KeyOrigin == GeneratedFile`. A second construction over the same directory
    reports `ExistingFile` and yields the same `KeyId`. A 31-byte file makes construction throw
    `NodeSecretProvisioningException`. The token and PHI key files in that directory are byte-identical
    before and after the signer is constructed.
12. **The seed never leaves the signer.** (a) Reflection over `NodeDatasetSigner` finds no non-private
    field or property of type `byte[]`, `ReadOnlyMemory<byte>`, `Memory<byte>` or any BouncyCastle
    `AsymmetricKeyParameter` subtype. (b) With the RFC seed loaded, the seed's 32 bytes appear neither
    in the decoded `PublicKeyBase64`, nor in the hex-decoded `KeyId`, nor in any `Sign` output
    serialized to JSON, nor in its Base64 form in that JSON. `SignCanonicalBytes` returns a
    *signature*, not key material; it is `internal` and is the only byte-returning member.

**Verifier**

13. `Verify` returns `true` for a manifest produced by `NodeDatasetSigner.Sign` checked against that
    signer's `Algorithm` and `PublicKeyBase64`.
14. `Verify` returns `true` for an **ML-DSA-65** signed manifest. Generate the key pair with
    `PostQuantumCryptoProvider.GenerateSigningKeyPair`. Build the manifest with
    `SignatureAlgorithm = "MlDsa65"` and `SignerKeyId = ComputeKeyId(DER)`. Sign
    `GetSigningBytes(manifest)` with `PostQuantumCryptoProvider.SignData`.
15. `Verify` returns `false`, without throwing, for each of: one flipped signature byte; each of the
    nine signed fields altered after signing; trusted algorithm differs from the manifest's;
    `SignerKeyId` does not match the trusted key; trusted key is not valid Base64; trusted key is valid
    Base64 but not an SPKI; an Ed25519 key supplied as `MlDsa65` and the reverse; `SignatureBase64`
    not Base64; a manifest with `GetState == Unsigned`; a manifest with `GetState == Malformed`; a
    signed field holding a lone surrogate.

**Export and compatibility**

16. A service built **with** a signer exports a manifest whose returned object **and** on-disk
    `.manifest.json` are both signed, identical to each other, and pass `Verify` against the signer's
    public key.
17. A service built **without** a signer (the existing 2-argument call) writes a `.manifest.json` that
    contains none of the strings `manifestVersion`, `signatureAlgorithm`, `signerKeyId` or
    `"signature"`. The unsigned on-disk format is unchanged.
18. Legacy JSON (exactly the six original properties) deserializes, with all four new properties null
    and `GetState == Unsigned`. Signed JSON round-trips through `JsonSerializer` with default options
    and still passes `Verify`. That is the path `ComprehensiveCoverageTests.cs:453-461` exercises.
19. Host-level: `POST /api/datasets/export` on a `WebApplicationFactory<Program>` bound to temp
    directories returns a signed manifest, and that manifest passes `Verify` against the host's
    `INodeDatasetSigner` (resolved from `factory.Services`). The host log contains the fingerprint
    line, and that line contains no path.
20. **`ImportDatasetAsync` is integrity-only, and stays so.** At service level, a signed manifest with a
    valid signature, and one with a deliberately corrupted signature, both import through the
    unchanged `ImportDatasetAsync` (`true`) when the checksum and size match. This holds in P8 **and
    in P9**: P9 rejects the corrupted case at the *endpoint*, through a separate class, and leaves the
    service method integrity-only. Name the test so that is obvious, for example
    `ImportDatasetAsync_IsIntegrityOnly_AndIgnoresSignatures`.
21. The five round-trip files pass with **zero edits**. `git diff --stat` shows none of them.

**Gate**

22. `./scripts/verify-all.sh` 7/7. Step 4 is exactly 100.00% line and 100.00% branch, with 0 warnings
    and 0 incomplete docblocks. Step 7 shows no class's uncovered count rising. The baseline is either
    unchanged or updated in this diff per Constraints 9, with no per-class count rising.

---

## Non-goals

- **Any import-side enforcement**, the registry, migration 007, strict mode, endpoints or the
  400/403 mapping. All of that is P9.
- Changing `ImportDatasetAsync`, its `bool` return or the import endpoint.
- Touching `/api/datasets/export-encrypted`, `KeyManagementService` or the ephemeral ML-DSA node key.
  The defect in that path is reported, not fixed here (see the Criteria consistency check, last
  bullet).
- A configuration override or rotation mechanism for the dataset signing key.
- Signing on the Razor UI export path, *if* it bypasses `IDatasetManager`. It does not today
  (`RazorComponentLifecycleTests.cs:589` drives `ExportDatasetAsync`), so it inherits signing
  automatically wherever a signer is registered.
- Replay or freshness protection. A validly signed old bundle stays validly signed.
- Documentation. P9 owns it.

---

## Criteria consistency check

I re-read criteria 1–22 as a set and checked each pair, and checked them against P9's criteria.
Findings:

- **Criterion 1 vs criterion 6.** The known-answer test uses `SignerKeyId = "k1"`, which `GetState`
  classifies `Malformed`. **No conflict:** rule 9 states that the canonicalizer validates nothing
  semantic. Criterion 1 tests the encoder alone, and criterion 15's "Malformed → false" is the
  verifier's job. Implement the canonicalizer without key-id validation, or criterion 1 cannot pass.
- **Criterion 4 vs criterion 15.** The canonicalizer throws, but `Verify` must never throw. **No
  conflict:** `Verify` catches the `ArgumentException`. Criterion 15's lone-surrogate case is the test
  that proves it does.
- **Criterion 12 vs criterion 8.** Criterion 8 needs raw signature bytes out of the signer, and
  criterion 12 forbids exposing the seed. **No conflict:** the RFC test injects the seed through the
  key *file*, not an API, and `SignCanonicalBytes` returns a signature. Criterion 12(a) is worded
  around non-private fields and properties so it does not trip over that method.
- **Criterion 16 vs criterion 17.** The two are complementary: one is the with-signer path, the other
  the without-signer path of `ApplySignature`, and together they cover both branches.
- **Criterion 19 vs criterion 21.** The host export is now signed, yet the host round trip in
  `ComprehensiveCoverageTests` must still pass unedited. **Consistent in P8**, because criterion 20
  pins that the service ignores signatures. **This tension reappears in P9**, where a signed manifest
  from an unregistered key gets a 403. P9 resolves it with implicit self-trust and flags it in its own
  consistency check (item A). Do not "fix" it in P8 by leaving the host unsigned.
- **Criterion 20 vs P9's enforcement criteria.** Criterion 20 asserts that a corrupted signature still
  imports *through the service method*. P9 asserts that the same bundle is rejected *through the
  endpoint*. **Both hold permanently, because they test different layers.** P9's Constraint 4 forbids
  deleting this test. This is called out because it is exactly the shape of the `db-seam-rollout.md`
  contradiction, where a coder deleted the tests on one side.
- **Criterion 9's self-derived pin vs criterion 1's "do not re-pin".** They are not in conflict, but
  they work differently on purpose. Criterion 1 is an independent derivation and must never be
  adjusted to fit the code. Criterion 9 is a regression pin and is derived from the code only after
  criteria 1 and 8 have independently validated the encoder and the Ed25519 wiring.
- **No pair found that cannot hold together.**
- **Outside the criteria, found in the code:** `/api/datasets/export-encrypted` is described
  everywhere as "signed with ML-DSA-65". It is signed, but with a key that is regenerated on every
  process start and carries the constant id `node_signing_master` on every node, so no peer can ever
  verify one of its signatures after a restart. This package does not fix that. It is out of scope and
  is reported to the coordinator.
