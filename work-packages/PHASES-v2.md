# nanoweaver-hrt — authoritative completion plan (v2, rev 3)

Supersedes `PHASES.md`. Nothing is deferred: every open item from the audit, the follow-up
queue, the C8 proposal and the CI diagnosis is assigned to a numbered package below, or is
explicitly dropped with an argument in §6.

`main` = `c506097`. Five commits this session, unpushed. Local gate green at `c506097`:
6 steps, 577 tests, 100.00% line/branch *of what it measures*, 0 warnings, 0 incomplete
docblocks. **The repo's own gates have never failed in CI** — the prior session verified all
of them against a pristine `git archive` export of HEAD.

**Rev 2 changes** (coordinator review against source): the auth transport-gate inversion is
pulled out of the C8 chain into its own early package, **P2**, so every package from the old
P2 onward is renumbered +1; the session-start SHA is dropped from the base preamble.

**Rev 3 changes:** the `/api/auth/login-key` line cites are corrected to `~1744/~1754/~1762`
plus `UserAuthenticationService.cs:367/:372` (the old `Program.cs:1651` cite pointed at an
unrelated username guard); the M5 cite list is replaced with the two sites that genuinely
remain; and **P1 is re-scoped from "make CI green" to a bounded flakiness investigation,
because CI is green at `origin/main`** — see §1.5.

---

## 0. Standing rules for every package

**Serial execution. One agent at a time.** User instruction, not a preference. The parent
session reviews each worktree diff, re-runs `./scripts/verify-all.sh` itself, and commits.
**Agents never commit, never push, never touch `AGENTS.md` Directives, `coverlet.runsettings`
thresholds or `.claude` configuration unless the package says so in its scope.**

**Base-commit preamble — copy verbatim into every dispatched work package, filling
`{{BASE_SHA}}` with `git rev-parse main` at dispatch time.** Omitting this already wasted one
agent run.

```
## 0. Fix your base commit FIRST (before reading anything else)

Your worktree was branched when this session started, NOT from current `main`, and `main`
has moved since. Do not trust your worktree's HEAD. Current `main` at dispatch is
{{BASE_SHA}}.

  git -C <your worktree> rebase main
  git -C <your worktree> log -1 --format=%H main     # must print {{BASE_SHA}}

If the rebase conflicts, resolve it and say so in your report — conflicts in
Program.cs / SystemConfigurationStore.cs / UserRepository.cs have been SEMANTIC twice
already (a superset docblock on one side, a catalog-entry union on the other). Take the
union; never take "ours" wholesale.
Do not start work until `git status` is clean and the rebase is done.
```

Do not quote a session-start SHA to agents. The two worktrees observed this session started at
`f3a2f50` (then `origin/main`), which is not derivable from the current log, so any hardcoded
base is a trap. `git rebase main` plus the `{{BASE_SHA}}` assertion is the whole mechanism and
it is sufficient.

**Migration numbering.** `Migration001`–`Migration004` exist. Any package adding one must
`ls src/NanoWeaverHrt/Database/Migrations/` and take the next free number — do not hardcode.
By the sequence below P5 lands `005` and P9 lands `006`, but both must still read the
directory, because a reordering silently changes the answer and `DatabaseMigrationRunner`
records versions by number.

**Work-package files.** Each architect package is written to
`docs/work-packages/<short-name>.md` in the repo so the coder that follows has it under
version control. This plan file stays in the scratchpad.

**Hard constraints on all code packages** (restating only what bites repeatedly):
Directive 1 (no `Split`/`Regex`/LINQ/`new byte[]` in parse loops; `ArrayPool` returned in
`finally`), Directive 2 + gate step 6 (one `<param>` per parameter and a `<returns>` on every
documented member, private included), Directive 3 (WHAT/WHY/HOW on algorithms, slices, async
loops), Directive 5 (100% line+branch on the measured scope; `[ExcludeFromCodeCoverage]` is
forbidden), Directive 6 (zero warnings, no sleeps, no ambient network/filesystem state — tests
use isolated temp data dirs, never `simulations/`), Directive 7 (`dotnet format
--verify-no-changes`), Directive 8 (no single-`$` around units/formulas), Directive 12
(Ed25519 and ML-DSA-65 only — ECDSA P-256 and RSA do not exist here and must not reappear even
in prose), Directive 13 (all DDL in an `IDatabaseMigration`, both dialects), Directive 14
(no raw `ex.Message`, paths, connection strings, PEM or SQL to clients).

---

## 1. The judgements I was asked to make

### 1.1 PostgreSQL testability vs the PHI-at-rest leak — **test seam first, but a much
narrower seam than the draft proposed**

I agree with the draft's ordering and disagree with its scope. All three load-bearing facts
below were independently re-confirmed against source by the coordinator.

The reason to go seam-first is stronger than "the Npgsql half is untested". Read
`UserRepository.cs:136-150`, and the same shape in `GetUserAsync` (173-194),
`ListUsersAsync` (223-243), `CreateUserAsync`, `UpdateLastLoginAsync`, and in all three sibling
repositories: **every Postgres branch is wrapped in a bare `catch` that silently falls through
to SQLite, per call.** Consequences that decide this question:

1. A PHI fix that encrypts correctly on SQLite and *throws* on Postgres does not fail loudly.
   It falls back and writes to the local `weaver_config.db` instead. A Postgres deployment
   gets **split-brain storage** — some rows in Postgres, some in a SQLite file — with a green
   gate and no error surfaced to the operator.
2. The specific way it will throw is already written into the schema.
   `Migration003_UserProfileAddressesAndContacts.cs` lines 31-66 (PostgreSQL DDL) declare
   `email_address VARCHAR(256)`, `phone_number VARCHAR(50)`, `date_of_birth VARCHAR(32)`,
   `postal_code VARCHAR(32)`; lines 85-120 (SQLite DDL) declare all of them `TEXT`. AES-256-GCM
   ciphertext, Base64-wrapped, does not fit the narrow ones. SQLite enforces no declared width,
   so the SQLite branch passes every test while the Postgres branch raises
   `22001 string_data_right_truncation` and silently degrades. **This is the exact failure mode
   the user asked me to weigh, and it is not hypothetical — it is in the DDL today.**
3. The PHI leak, while the highest-severity *item*, is at-rest and requires database access.
   The two primitives that gave an attacker that access (C3 arbitrary-overwrite import, C5
   `weaver_config.db` download) are already fixed and merged. So the leak is latent, not
   remotely exploitable this week. The split-brain regression would be shipped and live.

Where I disagree with the draft: it makes its Phase 2 "make the PostgreSQL half genuinely
testable" across all four repositories plus a coverage decision. That is three to four coder
passes, and blocking the PHI fix behind all of them is its own harm. So I split it:

- **P3/P4 build the seam and the real-Postgres fixture for `UserProfileRepository` only** —
  the one class the PHI fix touches — plus the honest-coverage baseline step.
- **P5 does the PHI fix**, verified against a real Postgres through that fixture.
- **P6 rolls the same seam out to the other three repositories** and removes the
  silent per-call fallback everywhere.

Net: PHI lands after two agent passes instead of four, and it lands on a path that can
actually be proven on the production backend.

**Not fully resolved, and it gates P4's design:** whether the Forgejo `archlinux` runner can
provide PostgreSQL in CI at all. The workflow uses no `services:` block and the runner is
host-mode (`runs-on: archlinux`, no container), which in Forgejo means service containers are
unavailable; Docker itself is clearly present because `docker/build-push-action` works. So the
likely answer is a `docker run -d --rm -p 127.0.0.1:55432:5432 postgres:16` workflow step plus
`WEAVER_TEST_POSTGRES`, with the suite **skipped** when that variable is absent so local
`dotnet test` stays hermetic. Cheapest resolution: the parent session adds a throwaway
workflow step that runs exactly that `docker run` plus `pg_isready` and reads the result. Do
this during P1, while already looking at CI.

### 1.2 The coverage exclusion — **stop flipping, start ratcheting**

Current state: `coverlet.runsettings` line 11 excludes `CompilerGeneratedAttribute`, hiding
11,050 of 20,372 lines. Real numbers with it lifted: 96.59% line / 89.28% branch, 694
uncovered lines over 82 classes. The gate reports 100.00% and is not lying about what it
measures — it is measuring the wrong thing. Two specific distortions matter:

- Every minimal-API endpoint lambda in `Program.cs` compiles into a `CompilerGenerated`
  display class. **The entire HTTP surface of the product is outside the number.** That is how
  `POST /api/users` — administrator account provisioning — sat at 0.00% unnoticed.
- Every async state machine is excluded, which in a codebase this `async`-heavy means the
  measured 9,322 lines are disproportionately synchronous helpers.

I do **not** recommend flipping the exclusion off (the gate goes red at 96.59% and the fastest
path back to green is mocking `DbConnection` and asserting against the mock — testing the
mock, which is worse than no test). I also do not recommend leaving it. Recommendation:

**Add gate step 7: a second, un-excluded measurement checked against a committed ratchet
baseline.**

- `coverage-baseline.json`, committed, holds: `line_rate`, `branch_rate`,
  `uncovered_lines_total`, and a per-class map `{class: uncovered_line_count}`.
- Step 7 runs `dotnet test` with a second runsettings file that drops
  `CompilerGeneratedAttribute` from `ExcludeByAttribute` (keep every other exclusion), then
  fails if: any rate drops below baseline, `uncovered_lines_total` rises, any class's uncovered
  count rises, or a class not in the baseline appears with uncovered lines.
- Baseline is seeded at the measured value on the rebased tree, so step 7 is green on day one
  and honest from day one.
- **The baseline may only ever move down.** Every subsequent package that reduces it
  updates the file in the same diff; the parent verifies the number in review.
- Step 4 (the existing 100.00% gate on the excluded scope) stays, unchanged. Two numbers, both
  true, neither pretending.
- When the baseline reaches 100.00/100.00, the final package deletes
  `CompilerGeneratedAttribute` from the primary runsettings, deletes the second file and the
  baseline, and step 4 alone enforces the real number. P18 checks whether that point was
  reached; it probably will not be, and that is an honest outcome rather than a failure.

One thing this forces into the open: **Directive 5's text ("Zero uncovered lines and zero
uncovered branches are allowed across the entire solution") is currently false in fact.** Step
7 makes the gap a visible, shrinking number instead of a hidden one. Amending Directive 5's
wording to name the measured scope is a **user decision**, not an agent one — it is queued in
P19 as a proposal for the user, with the exact replacement sentence, and no agent may touch it.

Explicitly ruled out, so nobody re-litigates it mid-phase: mocking `NpgsqlConnection`,
`DbConnection` or `DbDataReader` to raise the number. If a branch cannot be reached by a real
database (real SQLite or real Postgres), it is covered by deleting the branch (see P6's removal
of the per-call silent fallback) or it stays in the baseline.

### 1.3 C8 scope — **the supply chain is the design, not an afterthought**

Directive 12 allows exactly Ed25519 and ML-DSA-65. Browser `crypto.subtle` is absent on every
non-`localhost` plain-HTTP origin (and the docs tell operators to reach peers over LAN
hostnames), and ML-DSA does not exist in any shipping browser. So vendored pure-JS crypto is
forced. My position, which narrows the proposal:

1. **The transport-boundary inversion is NOT part of C8 and ships early, as P2.** It is a
   confirmed Directive 12 violation, pure C#, small, and independent of every line of
   JavaScript. See P2 for the verified cites. Pulling it forward is the one re-sequencing in
   rev 2.
2. **Split C8's server half out and ship it next, with no JS at all** (P14). `POST
   /api/auth/web-token`; the cookie block as it already exists; challenge TTL as a named
   constant at 120 s; challenge-store cap + timer prune (which also closes L9). Pure C#, fully
   testable, zero supply-chain exposure.
3. **Spike before vendoring** (P13, architect). The decisive unknown is what BouncyCastle 2.7
   writes into the PKCS#8 `privateKey` field for ML-DSA-65 — seed, expanded key, or both, under
   which draft encoding. Generate one bundle via `POST /api/setup/generate-keypair`, dump the
   DER, and cross-sign/verify against the candidate JS implementation. The answer changes the
   plan, so it must precede it.
4. **Vendor Ed25519 only** (`@noble/ed25519` + `@noble/hashes/sha512`, both zero-dependency).
   Supply-chain terms, non-negotiable and all mechanically checkable:
   - Files land in `src/NanoWeaverHrt/wwwroot/lib/<package>@<exact-version>/`. No CDN, ever
     — Directive 11's sovereignty posture and Directive 9's hardening both forbid an external
     origin, and SRI does not make a CDN available.
   - A committed `docs/vendored-javascript.md` manifest records, per file: package, exact
     version, SHA-256, upstream URL, the date, and **the name of the human who read the
     source**. "Popular on npm" is not a review; these files will sign authentication
     challenges with patient-identifying key material.
   - **New gate step 8: `scripts/verify-vendored-js.sh`** re-hashes the tree against the
     manifest and exits non-zero on any mismatch, extra file or missing file. An update that
     skips the review is then a red gate, not a silent swap.
   - `integrity="sha384-…"` on every classic `<script>` that loads them.
   - **Zero transitive dependencies permitted.** If a candidate has any, it is rejected rather
     than audited.
5. **Keep the vendored tree out of the 100% JS coverage number by architecture, not by a
   flag.** `--test-coverage-exclude` availability on the CI Node version is unverified, and I
   will not hang the gate on it. Instead `weaver-auth.js` takes an injected signer
   (`{ sign(messageBytes, privateKeyBytes) }`); tests inject a fake and the vendored module
   never enters the test import graph. Our own code reaches 100% line/branch/function; theirs
   is covered by the hash manifest and the human review. This is also simply better design.
6. **ML-DSA-65 in the browser: decided by the spike, and either outcome is a fix, not a
   deferral** (P16). If the encodings line up, vendor `@noble/post-quantum`'s `ml_dsa65` under
   the identical terms. If they do not, **do not leave a broken combination in place**: add
   server-side validation that refuses to create or gate an ML-DSA-65 account as
   `AccessGate.Web`/`ApiWeb`, migrate any existing such account to `Api`, document the
   limitation in `docs/compliance-and-security.md`, and keep `/api/auth/challenge` +
   `/api/auth/token` usable from `curl`. Removing an unreachable configuration is a fix; a
   half-working web login for PQC accounts is not.
7. **Remove the oracle last** (P16), strictly after the client path is verified against a real
   Ed25519 account with and without a passphrase. If the oracle is deleted before the browser
   parser is proven on encrypted PEMs, every passphrase-protected account is locked out of the
   UI with no fallback.

Note the dependency the proposal flagged as a conflict: C7 is already merged (`25f13ae`), so
the session model is decided. C8's client half must *conform* to the merged HttpOnly-cookie +
server-side idle model and must not reintroduce `localStorage`.

### 1.4 Splits, merges, and what to drop

**Split** (each of these was one phase in the draft and is too big for one coder pass):
- Draft Phase 2 → P3 (architect design) + P4 (profile-repo seam, Postgres fixture, baseline
  gate) + P6 (seam rollout to the other three repositories).
- Draft Phase 4 → P7 (architect design) + P8 (manifest schema, canonical signing bytes, export
  signing) + P9 (peer registry migration, admin endpoints, import enforcement, strict mode,
  docs). A new migration + three endpoints + a canonical byte encoding + a config catalog entry
  + five existing round-trip test files is not one pass.
- Draft Phase 7 → **P2** (the transport-gate inversion, pulled out and shipped early) + P13
  (spike/design) + P14 (server) + P15 (client) + P16 (oracle removal and the ML-DSA decision).

**Merge / absorb:**
- The draft's "AGENTS.md is stale (5 vs 6 checks)" item is **done**. `AGENTS.md` §3 now says
  "All 6 Checks" and Directive 10 says "any of the 6". **It was fixed this session, in commit
  `89f77cb`, together with the step-6 docblock scanner** — it is not a pre-existing state, so
  nobody should re-open it as a discrepancy against an older note.
- `WorkflowWizard._isSimulating` (cosmetic spinner never renders) and the `GET /api/services`
  role note fold into P11 and P12 respectively; neither deserves a phase.
- L8 (`RegisterChallengeForTesting` is test-only production surface) becomes a one-line
  comment inside P10, which is already in that file.

**Add, because nobody had assigned them:**
- **CI fails intermittently** — 4 of 12 runs (run ids 963, 965, 976, 983 = runs 1, 2, 8, 11),
  always in the large simulation image build-and-push step. **The baseline is green:** run 12
  (id 987) ran against `f3a2f504`, which is current `origin/main`, and succeeded in 379 s — a
  duration consistent with a full simulation image rebuild. "Everything must be fixed" includes
  a ~33% flake rate, but it is flakiness, not a red tree. → P1, re-scoped. See §1.5.
- `docker/simulation/Dockerfile.simulation` creates `/workspace/configs` but omits it from the
  `chown 1000:1000` list, so under `USER 1000:1000` it is root-owned and unwritable. Latent
  runtime bug found by the CI diagnosis. → P11.
- **M5, re-verified against current source: two sites remain, not twelve** (see P11). The
  audit's list predates commit `89f77cb`, which added `WHY \`!\`:` justification comments across
  `ContentFormatSerializer.cs`, `OpenApiMultiFormatExampleFilter.cs` and
  `EndocrineDashboard.razor`, fixed the one claimed latent `NullReferenceException` in
  `ContentFormatSerializer.cs` (that line is now a guarded `InvalidOperationException` with two
  tests covering it), and left `SetupWizard.razor` with no `!` suppression at all. **Every line
  number in the audit's M5 list is now stale — do not chase them.** The whole of open M5 is two
  sites: `Caching/ValkeyCacheService.cs:98` and `Bridges/ValkeyEventBridge.cs:265`, both
  `byte[] bytes = (byte[])<RedisValue>!;` with no justification.
- L1: `await Task.Delay(150/100/150)` as UI pacing in `WorkflowWizard.razor` — a direct
  Directive 6 violation sitting in a file nothing tests. → P11.
- L3: `docs/example-workflows.md:142,153,156` and `docs/configuration-peers.md:102` tell
  operators to reach peers on port 5000 across the network, which is what Directive 9 forbids.
  → P12.
- L7: `SecurityHardeningAndMigrationsTests.cs:323` hardcodes
  `/home/fabigail/secret_simulations/test.xyz` — the operator's real username in a fixture. →
  P11.
- The `.razor` blind spot: no bUnit, `.razor` excluded from coverage, so the `MainLayout` route
  guard's rendered output is tested by nothing — which is how the gating regression in
  `be3d207` shipped green, and is how a C8 `Login.razor` rewrite would ship broken. → P17,
  bounded to the three security-critical components.

**Drop, with the argument:**
1. **L2 (DuckDB staging table → migration): drop the fix, add a comment.**
   `DuckDbParquetWriter.cs:81-89` creates `staging_trajectory` in an in-memory DuckDB session
   (`DataSource=:memory:`). `DatabaseMigrationRunner` targets the weaver schema over
   `SqliteConnection`/`NpgsqlConnection` and records versions in `weaver_schema_migrations`.
   Routing a transient DuckDB table through it would be an actively wrong fix: a version row
   claiming a migration applied to a database that ceases to exist when the session closes.
   The correct output is one comment saying why Directive 13 does not reach it. (P11.)
2. **`package-lock.json`: do not track it — gitignore it, and say why.** `package.json` now has
   zero dependencies, so the lock file pins nothing and its only effect is diff noise and a
   false impression that the JS supply chain is locked. Add it to `.gitignore` with a comment.
   **Reconsider the instant anything is added to `dependencies` or `devDependencies`** — note
   that P15's vendoring is deliberately hand-vendored specifically so this stays true. (P12.)
3. **390px visual confirmation: drop the `resize_window` approach; substitute one CDP attempt,
   then record the limitation.** `resize_window` demonstrably reports success while the
   viewport stays at 2560px in this environment; repeating it is pure cost. P19 makes exactly
   one attempt via `Emulation.setDeviceMetricsOverride` and, if that also fails, records in the
   release notes that the mobile CSS is verified by reading the stylesheet and by the
   `scrollWidth === clientWidth` assertion being *unverified*, rather than claiming a visual
   pass that did not happen.
4. **`compose/docker-compose.nvidia.yml`: drop verification, keep it documented.** The NVIDIA
   Container Toolkit is not installed on this host; `--gpus all` fails with "failed to discover
   GPU vendor from CDI". `docs/getting-started.md:149` already lists the toolkit as a
   prerequisite. This is host setup, not a repo defect, and pretending otherwise would mean
   editing a correct file. P19 records it as untested-here.

### 1.5 The CI picture, corrected

Rev 1 and rev 2 of this plan said "CI is red". That was wrong, and the error came from reading
`ci-diagnosis.md` — which was accurate about run 11 — without checking whether a later run had
superseded it. The current facts, from the Forgejo API (`/api/v1/repos/.../actions/runs`,
readable unauthenticated):

| run | id | sha | status | duration |
|---|---|---|---|---|
| 12 | 987 | `f3a2f504` ← current `origin/main` | **success** | 379 s |
| 11 | 983 | `a3c6ce3b` | failure | 476 s |
| 10 | 980 | `8f693030` | success | 333 s |
| 9 | 978 | `b49a11f6` | success | 242 s |
| 8 | 976 | `473bc1f6` | failure | 251 s |
| 7 | 974 | `ee79aeba` | success | 162 s |
| 6 | 972 | `e96cf2c3` | success | 124 s |
| 5 | 971 | `05d8bc4d` | success | 372 s |
| 4 | 969 | `f434e12c` | success | 128 s |
| 3 | 967 | `9d9507ed` | success | 91 s |
| 2 | 965 | `a7e723d3` | failure | 185 s |
| 1 | 963 | `5881472a` | failure | 100 s |

So: **4 failures in 12 runs (~33%), the baseline is green, and a push does not land on a red
tree.** A push may still fail spuriously and simply needs a re-run. Rev 1 also under-counted
the failures (it named only 8 and 11; runs 1 and 2 failed too).

Two findings that re-rank the hypotheses in `ci-diagnosis.md`:

- **Hypothesis 2 (Intel apt channel rotation) is refuted.**
  `repositories.intel.com/gpu/intel-graphics.key`, `dists/noble/Release` and
  `dists/noble/client/binary-amd64/Packages.gz` all return 200, and all four requested packages
  resolve right now: `intel-opencl-icd` 24.39.31294.20-1032~24.04, `intel-level-zero-gpu`
  1.3.29735.27-914~24.04, `libze1` and `libze-dev` 1.17.44.0-1022~24.04. No rename, no
  rotation.
- **Hypothesis 1 (registry blob upload stalling under storage pressure) is now the strong
  one.** `git.lantea.one` resolves to `heimdall.lan`, and `heimdall` is also one of the four
  runner names (asgard 15, heimdall 14, meili 17, aesir 16). So the Forgejo server, its
  container registry storage, and at least one runner **share a single host**. That predicts
  the failures correlate with jobs landing on `heimdall`, where a multi-gigabyte simulation
  image build contends for the same disk as the registry it is uploading to.

**And to be unambiguous: the repo's own gates are not implicated.** The prior session verified
all of them against a pristine `git archive` export of HEAD, and the registry timestamps prove
the failing jobs ran past every gate step to a successful *web* image push. Do not "fix" the
flake by weakening a gate.

Confirming the cause needs one of two things, **both the user's call**: a logged-in browser
session on the Forgejo run log (the API on Forgejo 15.0.10 serves no actions-log route), or a
`df` on `heimdall`, which means SSH into the user's server. Neither is to be done uninvited.
This dev workstation is at 98% full with 47 G free, which is why a local multi-gigabyte
`--no-cache` reproduction was not attempted.

---

## 2. Dependency graph (the orderings that silently produce wrong work if swapped)

```
P1 (push + bounded CI flakiness investigation, parent)
   └─ independent of everything else; first only because unpushed work is the one
      unrecoverable state in the project. NOT because CI is red — it is green at
      origin/main (run 12, id 987). A push that flakes just needs a re-run.

P2 (auth transport-gate inversion, coder)
   └─ independent of everything; early because it is a live Directive 12 violation,
      pure C#, needs no fixture, and is small
   └─ its TESTS are inherited by P14/P16; its edit to /api/auth/login-key is deleted
      by P16. See P2's "durable part" note.

P3 (architect: DB seam + gate honesty)
   └─> P4 (profile-repo seam + Postgres fixture + baseline step 7)
          └─> P5 (PHI at rest)        <-- MUST NOT precede P4. See §1.1.
          └─> P6 (seam rollout, remove silent fallback)
                 └─ P6 after P5: P5 widens Migration columns; P6 must not
                    re-derive the dialect SQL P5 just changed

P7 (architect: dataset signing + peer registry)
   └─> P8 (manifest + canonical bytes + export signing)
          └─> P9 (peer registry migration 006 + admin endpoints + import enforcement)
             P9 after P5 so the migration numbers are 005 then 006

P10, P11, P12  — independent of each other and of the above; sequenced for review load

P13 (architect: C8 spike + vendoring policy)
   └─> P14 (server half; also closes L9)
          └─ P14 must land BEFORE P10 touches the challenge store, or both edit
             _activeChallenges and the second rebase is a semantic conflict.
             DECISION: L9 belongs to P14, NOT P10. P10 does not touch challenges.
          └─> P15 (client half: vendored Ed25519, weaver-auth.js, Login.razor)
                 └─> P16 (delete the oracle; resolve ML-DSA x Web gate)

P17 (bUnit for MainLayout/Login/SetupWizard)
   └─ after P15, so the rewritten Login.razor is what gets covered, not the old one

P18 (baseline closeout) — after every code phase

P19 (parent: release verification, push, Directive 5 proposal to the user)
   └─ last
```

Three orderings whose inversion produces silently wrong work, called out because they are not
obvious:

- **P5 before P4** would encrypt PHI on a path whose Postgres branch is unexercised, into
  `VARCHAR(50)`/`VARCHAR(32)` columns that cannot hold the ciphertext, with a bare `catch` that
  turns the resulting `22001` into a silent write to a different database. Green gate, split
  storage, plaintext-era rows left behind.
- **P10 before P14** would have two packages independently rework `_activeChallenges`. The
  second rebase looks clean (different lines) while landing two prune mechanisms, one of which
  no longer runs. L9 is therefore assigned to P14 only, and P10's package explicitly forbids
  touching the challenge store.
- **P16 before P15 verified end to end** locks every passphrase-protected account out of the
  Web UI with no fallback. "Merged" is not "verified"; browser confirmation of the Ed25519
  sign-in belongs inside P15's acceptance criteria, not after P16.

---

## 3. The packages

### P1 — Push `main`; bound the CI flake
**Fixes:** five unpushed commits at risk of loss, and a ~33% intermittent failure rate in the
simulation image build-and-push step. **Position:** first, for one reason only — unpushed work
is the single unrecoverable state in this project. **Not** because CI is red: it is green at
`origin/main` (run 12, id 987, against `f3a2f504`, 379 s). A push that flakes needs a re-run,
not a rescue.

**Agent:** parent session. Needs the sovereign remote and live Docker.

**Scope**
- `git push origin main` to `git.lantea.one` only. Never GitHub (Directive 11). Branch `main`.
  If the run fails in the simulation image step, **re-run it** — that is the expected
  behaviour at a 33% flake rate, and a second failure is more informative than the first.
- Record the run id, sha and outcome for the push, extending the §1.5 table. Twelve data points
  is barely enough to call 33%; every further run sharpens it, and if the failures cluster on
  one runner that is the answer.
- **Cheap, non-invasive hardening that is worth doing regardless of the root cause:** add
  `docker/setup-buildx-action`, an explicit `timeout-minutes` so a stall fails fast instead of
  burning a runner slot, and split the simulation image into its own job so a registry stall
  cannot sit behind the gate steps. None of this requires knowing the cause.
- **Do not weaken any gate step.** The repo's gates are not implicated — see §1.5.
- **Escalate, do not improvise, on root cause.** Confirming it needs either a logged-in browser
  session on the Forgejo run log (the API on Forgejo 15.0.10 serves no actions-log route) or a
  `df` on `heimdall`, i.e. SSH into the user's server. **Both are the user's decision.** Put the
  §1.5 evidence to the user — that `git.lantea.one` resolves to `heimdall.lan`, that `heimdall`
  is also a runner, and that the Intel apt channel is healthy so that hypothesis is refuted —
  and ask which of the two they want. Do not SSH uninvited.
- **While already in CI:** probe whether the `archlinux` runner can host PostgreSQL —
  a throwaway step running `docker run -d --rm -p 127.0.0.1:55432:5432 -e
  POSTGRES_PASSWORD=... postgres:16` then `pg_isready`. The answer gates P4's design. Record it
  in the handoff whether or not it is kept.

**Done:** `origin/main == c506097`; at least one green run on that sha (re-running through a
flake is acceptable and expected); the flake-rate table extended; the two root-cause options
put to the user with the evidence; and a recorded yes/no on runner-hosted PostgreSQL.

**Risk / trap:** the four `archlinux` runners are interchangeable and the workflow installs no
toolchain, so it silently depends on them being identical — a "fix" that only works on the
runner you happened to land on will look fixed. Second trap: treating one green run as proof
the flake is gone. It was never reliably reproducible; one success proves nothing about a 1-in-3
failure.

---

### P2 — Coder: the auth transport boundary is inverted on the one endpoint that grants sessions
**Fixes:** a confirmed Directive 12 violation. **Position:** second, immediately after the push.
It is pure C#, needs no test fixture, touches no JavaScript, and is independent of every
other package — so there is no reason for it to wait behind twelve of them, which is where the
C8 chain had it. This is the one re-sequencing in rev 2.

**Agent:** coder.

**The defect, with verified cites.** An older note cited `Program.cs:1651`; **that line is a
username guard in `/api/setup/generate-keypair` and is not this defect — ignore it.** The
accurate references are:
- `POST /api/auth/login-key` — `Program.cs` **~1744**, header comment "Direct Private Key Login
  (Web & API)", HOW comment says it sets a session cookie — passes `isWebRequest: false` at
  **~1754** and then calls `context.Response.Cookies.Append("weaver_session", ...)` at
  **~1762**.
- The gate checks that invert are `UserAuthenticationService.cs:367` and `:372`. With
  `isWebRequest: false`, every **`AccessGate.Web` account is rejected** — on the one endpoint
  that grants browser sessions — while an **`AccessGate.Api` account passes and receives a
  browser session cookie**. Both directions are inverted.
- `POST /api/auth/token` also passes `isWebRequest: false` at **~1712** but issues no cookie, so
  it is the correct API path and the milder case. Leave its behaviour alone; pin it with a test.

**Scope**
- Decide the boundary **by endpoint, never by a client-supplied field** — a client-asserted
  `transport` would be self-certifying and would void the gate. Concretely: `/api/auth/login-key`
  sets a cookie, therefore it is the web path and passes `isWebRequest: true`; `/api/auth/token`
  issues no cookie, therefore it stays `isWebRequest: false`.
- Fix the header and HOW comments so they describe what the endpoint now does.
- Tests, all four directions: a `Web`-gated account can authenticate via the cookie-granting
  endpoint and receives the cookie; an `Api`-gated account is refused there; an `Api`-gated
  account authenticates via `/api/auth/token` and receives **no** cookie; a `Web`-gated account
  is refused at `/api/auth/token`. Check `SecurityPenetrationTests.cs:189-231` (the
  Web→403-on-`/api` boundary) and the `AuthEndpointsIntegrationTests` set for any existing test
  that asserts the *inverted* behaviour as if correct, and say which you changed and why.
- Release note: `Api`-gated clients currently using `login-key` will now get 403 and must move
  to `/api/auth/token`. That is the point of the fix, but it is a behaviour change.

**Done:** the four assertions above pass; gate 6/6 green; 0 warnings; 0 incomplete docblocks;
no `ex.Message` or path in any new rejection body (Directive 14).

**The durable part, stated so P14 and P16 do not undo it:** P16 deletes
`/api/auth/login-key` entirely. What survives is (a) the *principle* — transport decided by
endpoint — which P14 must apply to `/api/auth/web-token` without re-litigating it, and (b) the
four tests, which P16 must **re-point at `/api/auth/web-token` rather than delete**. P2's
package says this explicitly, and so do P14's and P16's.

**Risk / trap:** do not "simplify" by deriving `isWebRequest` from a header, `Origin`, or
`Accept` — all three are client-controlled. The endpoint identity is the only
non-forgeable signal available here.

---

### P3 — Architect: the database access seam, and an honest coverage gate
**Fixes:** the structural cause of 694 uncovered lines and of the silent Postgres→SQLite
fallback, before anyone writes code against it. **Position:** before P4/P5/P6, which all
implement its output; three coders re-deciding this independently is the expensive failure.

**Agent:** architect. Produces **two** work-package files and **no code**.

**Scope (read)**
`src/NanoWeaverHrt/Security/Authentication/UserRepository.cs` (esp. 50-74, 96-120, 136-150),
`UserProfileRepository.cs` (42-110, 174, 539-590), `Configuration/SystemConfigurationStore.cs`
(57, 109-200), `Storage/PatientDnaRepository.cs` (64-110),
`Database/Migrations/DatabaseMigrationRunner.cs`, `coverlet.runsettings`,
`scripts/verify-all.sh`, `.forgejo/workflows/build-and-deploy.yml`, and P1's runner answer.

**Scope (decide and write down)**
1. The seam. My strong recommendation, to be confirmed or overturned with reasons: a
   `IWeaverDbConnectionFactory` returning `DbConnection` plus an `ISqlDialect` that supplies
   *only* the dialect-specific fragments (identifier quoting, boolean literal, upsert clause —
   `INSERT OR REPLACE` vs `ON CONFLICT DO NOTHING` per Directive 13, timestamp default). Then
   one execution path per repository method over `DbConnection`/`DbCommand`/`DbDataReader`
   instead of two hand-written twins. This collapses most of the 694 lines without any mock,
   because the shared path is exercised by the existing SQLite tests and what remains
   Postgres-specific is SQL *text*, not branches.
2. **The per-call `catch`-and-fall-back-to-SQLite must go.** Specify the replacement:
   backend selected once at initialization, a failure to reach a *configured* Postgres is a
   startup-time fallback with a logged warning, and a mid-session failure is an error, not a
   silent redirect to a different database. Name every call site. This is the change that
   removes the split-brain risk and it also deletes a large share of the uncovered branches.
3. The real-Postgres test fixture: an xUnit collection fixture gated on `WEAVER_TEST_POSTGRES`,
   `Assert.Skip` when absent so local runs stay hermetic (memory rule: tests must never depend
   on ambient network state), per-test schema or database name for isolation, no sleeps.
4. The coverage decision of §1.2, in implementable detail: the second runsettings file, the
   `coverage-baseline.json` schema, the step-7 comparison script, the ratchet-only rule, and
   the exact sentence to propose to the user for Directive 5. State plainly that step 4 is
   unchanged and that mocking a database driver is out of bounds.
5. Order the two packages: `docs/work-packages/db-seam-profile-repo.md` (→ P4) and
   `docs/work-packages/db-seam-rollout.md` (→ P6). Each must be one coder pass.

**Done:** both files exist, each with Goal / Files in scope / Interfaces / Constraints /
testable Acceptance criteria / Non-goals, and neither requires a coder to make a design
decision. No `.cs` file changed.

**Risk / trap:** a `DbConnection` abstraction is exactly the shape that invites a mock. The
package must state, in the Constraints section, that the factory is only ever satisfied by
`SqliteConnection` or `NpgsqlConnection` in tests, and that `Moq` on `DbConnection`/
`DbDataReader` is prohibited. Second trap: over-abstracting into a micro-ORM. The seam
replaces duplicated plumbing; it does not hide SQL.

---

### P4 — Coder: seam + real-Postgres fixture for the profile path, and gate step 7
**Fixes:** makes the one repository the PHI fix touches provable on both backends, and makes
the coverage number honest. **Position:** immediately before P5, for the reasons in §1.1.

**Agent:** coder, from `docs/work-packages/db-seam-profile-repo.md`.

**Scope:** `UserProfileRepository.cs` converted to the seam; connection factory and dialect
types added; the per-call silent fallback removed *in this file only*; the
`WEAVER_TEST_POSTGRES` collection fixture added to `tests/NanoWeaverHrt.Tests`; profile,
address and contact round-trips asserted against both SQLite and (when the variable is set)
real Postgres; `coverlet.runsettings` left untouched; a second runsettings file,
`coverage-baseline.json` seeded from a measured run, and gate step 7 added to both
`scripts/verify-all.sh` and `.forgejo/workflows/build-and-deploy.yml`.

**Done:** `./scripts/verify-all.sh` passes 7 steps; step 4 still 100.00/100.00; step 7 green
against the seeded baseline and *proven to bite* by temporarily adding an uncovered line (the
step-6 lesson — verify the new gate actually fails, do not assume); with
`WEAVER_TEST_POSTGRES` set against a local container the Postgres-backed profile tests pass;
with it unset they skip and the suite is unchanged; 0 warnings; 0 incomplete docblocks.

**Dependencies:** P3. Blocks P5 and P6.

**Risk / trap:** the fixture must not touch `simulations/shared-data` (memory rule) and must
not leave a schema behind that makes a second run pass for the wrong reason. The baseline file
is the subtle one: seed it from a *measured* run on the rebased tree, never from the numbers
quoted in this plan — the rebase itself changes them.

---

### P5 — Coder: PHI at rest — profile PII, addresses and contacts
**Fixes:** the highest-severity open item. Patient addresses and emergency contacts are stored
in plaintext; only `picture` goes through `ProfileDataProtector`. **Position:** directly after
the seam that lets it be proven on Postgres.

**Agent:** coder.

**Scope — wider than the queue recorded, and confirmed against source.**
`UserProfileRepository.MapProfileFromReader` reads `email`, `phone`, `dob`,
`emergencyNotes` and `bio` as raw strings; only `encryptedPic` (ordinal 7) goes through the
protector (`:577`). So `Migration003` puts in plaintext not only
`weaver_patient_addresses` (`address_line1`, `address_line2`, `city`, `state_or_province`,
`postal_code`) and `weaver_patient_contacts` (`contact_name`, `relationship`, `phone_number`,
`email_address`, `notes`) but also `weaver_user_profiles.email_address`, `.phone_number`,
`.date_of_birth`, `.emergency_medical_notes`, `.bio`. Under HIPAA Safe Harbor those are
precisely the identifiers de-identification strips. All of them are in scope; the package must
list the exact column set and justify any column left plaintext (`country` and
`is_primary`/`priority_order` are defensible; names, street, postal code, phone, email, DOB and
free-text notes are not).

- Extend `ProfileDataProtector` usage in `UserProfileRepository` write and read paths
  (`:174` is the existing `picture` write precedent; `MapProfileFromReader`/`:577` the read
  precedent).
- **New migration, number read from the directory (expected `005`), both dialects.** Two jobs:
  widen every newly-encrypted Postgres column to `TEXT` (`VARCHAR(50)` cannot hold
  Base64 AES-256-GCM ciphertext — this is the trap that makes P4 a prerequisite), and
  re-encrypt existing rows in place, idempotently, so a re-run cannot double-encrypt.
- Corrupt or foreign ciphertext must behave as `picture` now does: a redacted RFC 7807 500,
  never rendered as plaintext, never echoing the ciphertext (Directive 14). Reuse the
  `LegacyDecryptionCount` plumbing at `Program.cs:3009-3016` rather than inventing a second
  mechanism.
- Tests on both backends, including: round-trip; a pre-migration plaintext row upgraded; the
  migration run twice; a row whose ciphertext was written under a different pepper.

**Done:** no plaintext PHI in either backend for the listed columns, asserted by a test that
reads the raw column value and checks it does not contain the submitted string; the
double-run idempotence test passes; gate 7/7 green; baseline in `coverage-baseline.json`
moves **down**, never up.

**Dependencies:** P4 (hard). Takes migration `005`, which fixes P9 at `006`.

**Risk / traps:** (1) column widths, above. (2) `ProfileDataProtector`'s pepper is now
mandatory (C6 fixed) — the migration must fail loudly, not silently skip, if it is absent, or
it will "succeed" having encrypted nothing. (3) searching or sorting on an encrypted column
stops working; check every `WHERE`/`ORDER BY` in the repository against the new column set and
say in the report which queries changed semantics. (4) do not encrypt `username`: it is a
foreign key with `ON DELETE CASCADE`.

---

### P6 — Coder: roll the seam out to the remaining three repositories
**Fixes:** the remaining ~600 uncovered lines and the last of the silent-fallback split-brain
risk. **Position:** after P5, because P5 rewrites Migration003-era dialect SQL that this
package would otherwise re-derive from the pre-P5 shape.

**Agent:** coder, from `docs/work-packages/db-seam-rollout.md`.

**Scope:** `UserRepository.cs`, `Configuration/SystemConfigurationStore.cs`,
`Storage/PatientDnaRepository.cs`. Same seam, same removal of the per-call `catch`. Extend the
`WEAVER_TEST_POSTGRES` suite to the named low-coverage methods:
`SystemConfigurationStore.FindKeyByTokenAsync` (53% line), `UserRepository.CreateUserAsync`
(59%), `InitializeAsync`, `GetUserAsync`, `ListUsersAsync`, `UpdateLastLoginAsync`,
`CreateShareKeyAsync`, `ConsumeShareKeyAsync`.

**Done:** gate 7/7; baseline drops substantially (state the new numbers in the report); every
removed fallback branch accounted for as either covered or deleted; no mock of any ADO.NET type
anywhere in the diff.

**Risk / trap:** `SystemConfigurationStore` has already produced two semantic rebase conflicts
this project; its `Definitions` catalog takes a **union**, never one side. Second trap:
`InitializeAsync`'s short-circuit (`_initializedPath == _sqlitePath && File.Exists(...)`) is
load-bearing for C3's mitigation — do not simplify it away.

---

### P7 — Architect: dataset signing, trusted peer key registry, import verification
**Fixes:** `POST /api/datasets/import` verifies a SHA-256 the uploader supplied alongside the
archive — self-certifying, so it detects transit corruption and nothing else.
**Position:** after the storage work, before the endpoints that depend on a new migration.

**Agent:** architect. `brief-peer-key-verification.md` holds the verified facts — **do not
re-derive them.** Produce `docs/work-packages/dataset-signing.md` (→ P8) and
`docs/work-packages/peer-key-registry.md` (→ P9). No code.

**Scope:** every one of the eight decisions enumerated in the brief, with these emphases:
- **What exactly is signed.** The manifest is attacker-supplied, so an archive-hash-only
  signature is not enough. Signature covers `Sha256Checksum` + `DatasetName` +
  `ArchiveFileName` + `SizeInBytes` + `TimestampUtc` under a canonical byte encoding with
  fixed field order, no reliance on JSON property order, no culture-sensitive formatting, and
  an explicit statement of how the signature field is excluded from its own input. Getting this
  wrong *is* the vulnerability; it must be unambiguous enough that two implementations agree.
- Manifest additions (signer identity, algorithm, signature, format version) all optional so
  an existing unsigned manifest still deserializes.
- `weaver_peer_keys` DDL in both dialects, with registering admin, timestamp, and a revocation
  representation.
- Administrator endpoints register/list/revoke, and **which entry is added to the route table
  near `Program.cs:3185` / `ResolveRouteRoleRequirement`** — a gate that silently stops
  matching is exactly the bug class already fixed once here (C4).
- A result type that lets the endpoint distinguish hash mismatch (400) from unknown signer
  (403) from bad signature (400) without leaking detail. `ImportDatasetAsync` returns `bool`
  today and cannot.
- The strict-mode setting: key, `Definitions` catalog placement, category, `IsSecret: false`,
  **default OFF** so the documented quickstart and the five existing round-trip test files keep
  working.
- Which key signs on export — new node signing key or an existing one — without weakening the
  node key isolation from `4799f27`.
- The `AGENTS.md` §2 correction: it claims "cryptographically signed `.tar.gz` dataset bundles
  with SHA-256 manifests", which is true only of the separate ML-DSA-65
  `/api/datasets/export-encrypted` path. This is a factual correction to a non-Directive
  section and is in scope; the 14 Directives are not.

**Done:** two self-contained packages; a coder can implement either without re-deciding
anything.

**Risk / trap:** conflating `weaver_share_keys` (single-use UI linking tokens, Migration001)
with a peer public key registry. Different concept; the design must say so explicitly so the
coder does not "reuse" it.

---

### P8 — Coder: manifest schema, canonical signing bytes, signing on export
**Agent:** coder. **Scope:** `Models/DatasetManifest.cs`, `Services/DatasetTransferService.cs`,
the canonical encoder, export-side signing. **Done:** a signed bundle verifies; an unsigned
bundle still round-trips; a known-answer test pins the canonical bytes so a future refactor
cannot change them silently; gate green with the baseline not rising.
**Dependencies:** P7. Blocks P9.
**Risk / trap:** the five existing round-trip test files (`DatasetTransferTests`,
`EndpointIntegrationTests`, `ComprehensiveCoverageTests`, `DatasetImportHardeningTests`,
`ApiRoleGatingAndDisclosureTests`) must keep passing unchanged, since strict mode defaults OFF.
If one needs editing, say which and why.

---

### P9 — Coder: peer key registry, admin endpoints, import enforcement, docs
**Agent:** coder. **Scope:** migration `006` (read the directory to confirm) with both
dialects; `weaver_peer_keys`; register/list/revoke endpoints, Administrator-only, with the
route-table entry named in the design; the strict-mode config entry; `ImportDatasetAsync`'s
result type and the endpoint's 400/403 mapping through `SecureProblemDetails`;
`docs/api-reference.md`, the peer/configuration docs, and the `AGENTS.md` §2 correction.
**Done:** all four rows of the brief's enforcement table are asserted by tests
(unsigned→allowed; signed+registered+valid→imported; signed+unregistered→403;
signed+invalid→400), plus unsigned+strict→rejected; role gating on all three new endpoints
tested; no private key material in any response; gate green.
**Dependencies:** P8, and P5 for the migration number.
**Risk / trap:** the role gate. Add the route-table entry and a test that a Researcher and a
Patient both get 403 on all three endpoints — the C4 bug was a gate that compiled, read
correctly, and matched nothing.

---

### P10 — Coder: the deferred low-severity items in `Program.cs` and `UserAuthenticationService.cs`
**Fixes:** L4, L6, L8, and the `DeleteUserAsync` case-sensitivity mismatch. **Position:** after
the storage and dataset work, before the C8 chain touches the same two files.

**Agent:** coder.

**Scope**
- **L4:** `UserAuthenticationService.cs:391` `token.Split('.')` on a per-request hot path →
  `AsSpan().IndexOf('.')`.
- **L6:** `Program.cs:2197-2240` `GET /api/patient/dna` takes an arbitrary `patientId` from any
  role and echoes it. No data leaks today (content comes from presets); validate the identifier
  and scope it to self-or-Administrator now, before a handler starts reading per-patient data.
- **L8:** one comment on `RegisterChallengeForTesting` recording that it is test-only surface
  reachable via `InternalsVisibleTo`.
- **`DeleteUserAsync` case sensitivity** (found by the duplicate-username agent, out of its
  scope then): it matches usernames case-insensitively while the database key is
  case-sensitive, so a delete can silently miss. Pick **one** rule and apply it consistently
  across create, lookup and delete — and say which, because the 409-Conflict duplicate check
  just landed and must agree with it.
- **Carry-over from the duplicate-username commit:** assert that the compliance audit ledger
  stays empty when a duplicate user is refused. The code is right by inspection (early return
  before `LogAction`) but that property was the headline of the change and has no assertion.

**Done:** gate green; the case rule is documented in the report; baseline not rising.

**Dependencies:** none hard. **Must not touch `_activeChallenges`** — L9 belongs to P14. State
this in the package; two independent prunes is the silent-conflict failure of §2.

**Risk / trap:** the case-sensitivity decision has a security edge. Case-insensitive lookup
plus case-sensitive storage means `Admin` and `admin` can coexist while one of them shadows
the other at login. Whichever rule is chosen, add a test for the shadowing case.

---

### P11 — Coder: code-hygiene and latent-bug sweep
**Fixes:** five independent small defects, none of which warrants its own phase.
**Agent:** coder.

**Scope**
- **M5 — two sites, and that is the whole of it.** The audit's twelve-site list is stale:
  commit `89f77cb` added `WHY \`!\`:` justification comments across
  `ContentFormatSerializer.cs`, `OpenApiMultiFormatExampleFilter.cs` and
  `EndocrineDashboard.razor`, replaced the one claimed latent `NullReferenceException` in
  `ContentFormatSerializer.cs` with a guarded `InvalidOperationException` plus two tests, and
  `SetupWizard.razor` now has no `!` suppression at all. **Do not chase the audit's line
  numbers — every one of them has moved or been resolved.** What remains:
  - `Caching/ValkeyCacheService.cs:98` — `byte[] bytes = (byte[])result!;`.
    `StackExchange.Redis` annotates that conversion as returning `byte[]?`. The cast is guarded
    by `result.HasValue && !result.IsNullOrEmpty` at line 96, so a one-line justification
    comment is the correct fix.
  - `Bridges/ValkeyEventBridge.cs:265` — `byte[] bytes = (byte[])message!;`. **Read the guard
    at ~261 before deciding.** If it establishes the same non-null property, a justification
    comment suffices; if it does not, use a real null check. Report which you applied and why.
  - Re-grep `src/` for unjustified `!` before editing and report the delta. This list has
    already gone stale once.
- **L1:** `WorkflowWizard.razor:1249,1269,1290` — `await Task.Delay(150/100/150)` as UI pacing
  violates Directive 6 in a file nothing tests. Drive the animation from CSS.
- **`WorkflowWizard._isSimulating`** now flips true→false inside one synchronous method, so the
  button's spinner state never renders. Cosmetic, same file, same pass.
- **L2:** one comment in `Storage/DuckDbParquetWriter.cs:81-89` recording why Directive 13 does
  not reach a transient in-memory DuckDB staging table. No migration — see §1.4 drop 1.
- **L7:** `tests/NanoWeaverHrt.Tests/SecurityHardeningAndMigrationsTests.cs:323` hardcodes
  `/home/fabigail/secret_simulations/test.xyz`. Synthetic path.
- **`docker/simulation/Dockerfile.simulation`:** `/workspace/configs` is created but omitted
  from the `chown 1000:1000` list, so under `USER 1000:1000` it is root-owned and unwritable.

**Done:** gate green; a `grep` for an unjustified `!` across `src/` returns nothing; no
`Task.Delay` in `src/`; no `/home/` literal in `tests/`.

**Risk / trap:** `.razor` files are excluded from coverage and have no bUnit, so the L1 and
`_isSimulating` edits are **completely untested by the gate**. The report must state what was
verified by reading versus by running, and P19 must look at the wizard in a browser.

---

### P12 — Coder: documentation and deployment truth pass
**Fixes:** docs that contradict the Directives, undocumented environment variables, and a
deployment default that is less hardened than the commented block beside it.
**Agent:** coder.

**Scope**
- `docs/compliance-and-security.md` still claims in-browser "Ed25519 or NIST P-256" key
  generation. P-256 is not implemented and Directive 12 forbids reintroducing it even in prose.
- **L3:** `docs/example-workflows.md:142,153,156` and `docs/configuration-peers.md:102` tell
  operators to reach peers at `https://lab-a.hrt-lab.local:5000`. Directive 9 says never expose
  5000 across a network; point them at the proxied `https://<domain>/`.
- `example.env` documents none of `WEAVER_NODE_KEYS_DIR`, `WEAVER_TOKEN_SIGNING_KEY`,
  `WEAVER_POSTGRES_PASSWORD`, `SECURITY_PEPPER`/`Security:ProfilePepper` (now mandatory after
  C6), or `POSTGRES_CONNECTION_STRING`'s deliberately-empty default.
- `docs/api-reference.md`: role note on `GET /api/services`.
- Make the hardened **dedicated** node-key mount the default rather than a subdirectory of the
  existing bind mount: `docker/web/Dockerfile.web`, a tracked
  `simulations/node-keys/.gitkeep`, `.gitignore`, and un-comment the hardening block already
  sitting in `docker-compose.yml`.
- `.gitignore` entry for `package-lock.json` with a comment explaining that `package.json` has
  no dependencies so the lock pins nothing, and that this must be revisited if any dependency
  is ever added (§1.4 drop 2).
- Release notes: the corrupt-`picture` RFC 7807 500 behaviour change, the P5 PHI encryption,
  the P2 `login-key` transport change, and anything P8/P9 changed about export bundles.

**Done:** gate step 5 green (no single-`$` around units/formulas); `grep -ri "P-256"
docs/ README.md` returns nothing; every variable named above appears in `example.env`;
`docker compose config` shows the dedicated key mount as the default. **Compose changes are
verified by P19 running the stack, not by `docker compose config`** — that is exactly what hid
a `0.0.0.0:5000` exposure and a database that had never started.

**Risk / trap:** the compose default change can break the documented quickstart on a clean
machine, which is how the infra PostgreSQL volume-permission bug stayed invisible. Flag it
prominently for P19.

---

### P13 — Architect: C8 spike, vendoring policy, and the ML-DSA decision
**Fixes:** the last Critical — the private key is transmitted to the server, which signs a nonce
it generated and verifies its own signature. **Position:** after everything that touches
`Program.cs` and `UserAuthenticationService.cs` for other reasons, because this chain rewrites
both. (The transport-gate half of C8 already shipped as P2.)

**Agent:** architect. Reads `c8-challenge-response-proposal.md` (verified facts; do not
re-derive) — **but note that its `Program.cs:1651` cite is wrong; that line is a username guard
in `/api/setup/generate-keypair`. Use P2's verified references: `~1744`/`~1754`/`~1762` for
`/api/auth/login-key`, `UserAuthenticationService.cs:367`/`:372` for the gate checks, and
`~1712` for `/api/auth/token`.** Produces three work packages and **one spike result**. No
production code.

**Scope**
1. **Run the spike.** Generate one ML-DSA-65 bundle via `POST /api/setup/generate-keypair`,
   dump the PKCS#8 `privateKey` field, and determine what BouncyCastle 2.7 writes — seed,
   expanded key, or both, under which draft encoding — then cross-sign/verify against the
   candidate JS implementation. This is the single largest technical unknown in C8 and it
   changes the plan. Record the bytes and the verdict.
2. **Vendoring policy** per §1.3 item 4, written as a package a coder can execute: directory
   layout, the `docs/vendored-javascript.md` manifest schema, `scripts/verify-vendored-js.sh`
   as gate step 8, SRI attributes, no-CDN rule, zero-transitive-dependencies rule, and the
   named human review. Also: the injected-signer architecture that keeps the vendored tree out
   of the JS coverage number without depending on `--test-coverage-exclude`, whose availability
   on the CI Node version is unverified and must not be a load-bearing assumption.
3. **The ML-DSA × `AccessGate.Web` decision**, per §1.3 item 6 — vendor `ml_dsa65` if the spike
   says the encodings line up, otherwise refuse the ML-DSA + Web/ApiWeb combination
   server-side and document it. Write both branches so P16 does not have to decide.
4. Three packages: `docs/work-packages/c8-server.md` (→ P14),
   `docs/work-packages/c8-client.md` (→ P15), `docs/work-packages/c8-remove-oracle.md` (→ P16).
   All three must carry P2's "transport decided by endpoint" rule forward without reopening it.

**Done:** spike result recorded with the actual bytes; three packages; the vendoring terms are
mechanically checkable rather than aspirational.

**Risk / trap:** the proposal recommends raw-nonce signing unchanged and no domain separation.
Agree or overturn it explicitly — adding domain separation later is a breaking change for every
existing API client of `/api/auth/token` and doubles the branch count under the 100% gate.
Second trap: C7 is already merged, so the session model is **decided**. Conform to the
HttpOnly-cookie + server-side-idle model; do not reintroduce `localStorage`.

---

### P14 — Coder: C8 server half, and L9
**Agent:** coder, from `docs/work-packages/c8-server.md`. No JavaScript in this package.
**Scope:** `POST /api/auth/web-token` beside `/api/auth/token`, with `isWebRequest` decided
**by endpoint** per P2's already-landed rule — do not re-litigate it, and do not accept a
client-supplied `transport` field; the existing HttpOnly/Secure/SameSite=Strict cookie block;
the unauthenticated allow-list entry; `ChallengeLifetime` as a named constant at 120 s;
**L9** — cap `_activeChallenges` and prune on a timer rather than on the next call, which today
makes an unauthenticated endpoint a cheap memory-pressure lever; same-origin validation against
the *configured* public origin, not `HttpContext.Request.Host`, or the Caddy/Nginx overlays will
refuse logins. `/api/auth/login-key` keeps working, so nothing breaks.
**Done:** new endpoint tested at 100% branch including the cap, the prune and the same-origin
rejection; prune is deterministic in tests (injected interval or a directly callable method —
no sleeps); `AccessGate.Web` accounts can authenticate against `web-token` and `AccessGate.Api`
accounts do not receive a browser cookie, both asserted; gate green.
**Dependencies:** P13, and P2's rule. **Blocks P15.** Owns L9 exclusively.
**Risk / trap:** keep `_activeChallenges.TryRemove` **before** the signature check so a failed
attempt burns the nonce, and keep `CreateChallenge` issuing a challenge for unknown usernames —
"improving" that into a 404 adds username enumeration. Both are currently correct.

---

### P15 — Coder: C8 client half
**Agent:** coder, from `docs/work-packages/c8-client.md`.
**Scope:** vendored Ed25519 + SHA-512 under the P13 terms; `scripts/verify-vendored-js.sh` as
gate step 8; `wwwroot/js/weaver-auth.js` (`requestChallenge`, `parsePrivateKeyPem`,
`decryptKeyEnvelope`, `signChallenge`, `submitSignature`, and one `window.weaverSignIn` shell)
at 100% line/branch/function with pure functions separated from `fetch`/DOM — the same split
that makes `weaver-visualizer.js` testable; `Login.razor` rewritten with **no `InputFile`**
(it streams the file to the server over the SignalR circuit) and **no `@bind`** on the key or
passphrase fields (two-way binding ships every keystroke to the server — the existing textarea
already does this on blur).
**Done:** `npm test` 100% on our files; step 8 passes and is proven to bite by mutating one
vendored byte; **a real Ed25519 account signs in in a browser, with and without a passphrase —
this is an acceptance criterion of P15, not something P19 discovers**; RFC 8032 known-answer
vectors pass on the JS side; no key material in any request body; gate green.
**Dependencies:** P14. Blocks P16 and P17.
**Risk / traps:** (1) `.razor` markup is covered by nothing — a typo in an element id breaks
sign-in with a green gate, so `weaver-auth.js` must fail loudly with a specific message when an
expected element is missing, and that branch must be covered. (2) Pure-JS PBKDF2 at 100 000
iterations blocks the main thread for seconds on a phone; without visible feedback users click
repeatedly and **each click burns a challenge**. (3) The project's own envelope format is
`[salt 16][nonce 12][tag 16][ciphertext]`, PBKDF2-SHA256 100 000, AES-256-GCM with the salt as
associated data — reproduce it exactly, and keep today's user-facing error text. (4) Key
lifetime claims must say "best effort": JS cannot guarantee erasure, and overclaiming it in
docs is its own defect.

---

### P16 — Coder: delete the signing oracle; resolve ML-DSA × Web gate
**Agent:** coder, from `docs/work-packages/c8-remove-oracle.md`.
**Scope:** delete `AuthenticateWithPrivateKeyAsync`, `SignEphemeralNonce`,
`ExtractPrivateKeyDerFromPem`, `DecryptPrivateKeyBytes` (keep `EncryptPrivateKeyBytes` —
`GenerateSecureKeyBundle` still uses it), `KeyLoginRequest` including its sample private key in
`GetExamples()`, `POST /api/auth/login-key` (`~1744`) and its allow-list entry, and the Swagger
security description at `Program.cs:143` that names it. **Re-point P2's four transport-boundary
tests at `/api/auth/web-token` rather than deleting them** — they are the regression net for a
Directive 12 violation that has already shipped once. Triage the ~51 test hits on
`login-key|AuthenticateWithPrivateKeyAsync|PrivateKeyPem` — many are `GeneratedKeyBundle`
(server-generated-key download) and must stay. Implement the P13 ML-DSA branch. Release note:
this is a breaking API change.
**Done:** no path transmits a private key to the server; the compliance audit ledger now records
every interactive sign-in and failure (the challenge path logs both; the deleted method logged
nothing, so the 21 CFR Part 11 ledger has had **no** record of interactive sign-ins); P2's four
assertions still pass against the new endpoint; gate green; baseline drops because covered
production code was deleted.
**Dependencies:** P15 **verified end to end**, not merely merged. Inverting this locks every
passphrase-protected account out of the UI with no fallback.
**Risk / trap:** `GenerateSecureKeyBundle` still mints keypairs server-side for `/setup` and
user creation, so the key still travels *from* the server. Do not let the docs claim "the key
never leaves the browser". Say plainly what was fixed. In-browser key generation is a separate,
larger piece of work — and because nothing is deferred, P19 must file it as a named follow-up
item for the user with that scope stated, not drop it.

---

### P17 — Coder: bUnit for the three security-critical components
**Fixes:** the `.razor` blind spot. `coverlet.runsettings` excludes
`**/Components/**/*.razor` and `NanoWeaverHrt.Components.*`, and there is no bUnit, so the
rendered output of the `MainLayout` route guard is tested by nothing — which is how the UI
gating regression in `be3d207` shipped with a green gate.
**Agent:** coder. **Position:** after P15, so it covers the rewritten `Login.razor`.
**Scope:** add bUnit to `tests/NanoWeaverHrt.Tests`; cover **only** `MainLayout` (route guard
and idle sign-out), `Login`, `SetupWizard`; remove those three from the coverage exclusions and
bring them to 100%. Everything else stays excluded and stays visible in the step-7 baseline.
**Done:** a test fails if the route guard stops rendering the gate; gate green; the three
components' exclusions are gone from `coverlet.runsettings` and the baseline reflects it.
**Risk / trap:** scope creep. bUnit-ing every page is the largest single item in the project and
is **not** this package. Second trap: adding bUnit adds a NuGet dependency to the test project
— check `NuGetAudit` and the zero-warnings requirement.

---

### P18 — Coder: step-7 baseline closeout
**Fixes:** whatever uncovered lines remain in the ratchet after P4–P17.
**Agent:** coder. **Position:** after every code phase, before release verification.
**Scope:** read `coverage-baseline.json`, drive it as close to 100.00/100.00 as real tests
allow. If it reaches 100.00/100.00: delete `CompilerGeneratedAttribute` from
`coverlet.runsettings`, delete the second runsettings file and the baseline, and fold step 7
back into step 4. If it does not: leave the baseline at its honest value and report exactly
which classes remain and why.
**Done:** either the exclusion is gone and step 4 enforces the real number, or the report names
every remaining class with a reason that is not "hard to test".
**Risk / trap:** this is the package where someone will be tempted to mock `DbConnection` to
close the last few percent. Prohibited, in the package's Constraints, in the same words as P3.
Reporting 97% honestly is the correct outcome; a mocked 100% is not.

---

### P19 — Parent: release verification, push, and the Directive 5 proposal
**Agent:** parent session. Needs live Docker, the sovereign remote, and a browser.
**Scope**
- **Verify by running it, not by `docker compose config`.** Bring up the default stack, the
  infra profile, and each public overlay. After every compose change:
  `ss -ltnH | grep -E '^(0\.0\.0\.0|\[::\]|\*):'` must be empty. Confirm 5000, 5432 and 8080
  bind `127.0.0.1` only; `/healthz` 200; all containers healthy; node keys generated
  in-container as `-rw------- 32 bytes`; the parser's schema genuinely created in PostgreSQL
  (not silently fallen back to SQLite — P6 should now make that loud); a token forged with an
  old published key returns a clean 401 with no stack trace or paths.
- Re-verify the P12 compose default change on a **clean** machine state, including a fresh
  named volume — the infra PostgreSQL container had never started on a clean machine because
  the volume was created `root:root` and uid 999 could not mkdir PGDATA.
- Re-exercise the C8 sign-in in a browser (P15 already proved Ed25519; confirm it survived P16)
  plus whichever ML-DSA outcome P16 implemented, and idle sign-out and F5 after idle.
- Walk the wizard in a browser (P11 changed untested `.razor` code).
- One attempt at a 390px viewport via CDP `Emulation.setDeviceMetricsOverride`; if it fails,
  record the limitation honestly rather than claiming a visual pass (§1.4 drop 3).
- Record that `compose/docker-compose.nvidia.yml` is untestable on this host and why (§1.4
  drop 4).
- Push `main` to `git.lantea.one` (`origin`, branch `main`, never GitHub) and confirm a green
  run. Re-run through a simulation-image flake if one occurs (§1.5) rather than treating it as
  a regression.
- **Put three items to the user, not to an agent:** (a) the exact replacement wording for
  Directive 5 so it names the measured scope instead of claiming a zero that step 7 now shows
  is not zero; (b) in-browser key generation for `/setup` and user creation, so the server
  never mints a private key — scoped, named, and left as the user's call; (c) if the CI flake
  is still unexplained, the choice between a browser session on the run log and a `df` on
  `heimdall` (§1.5).
**Done:** `origin/main` current, CI green on that sha, every live check above recorded with its
actual output, and the three user decisions stated.
**Risk / trap:** the method lesson that produced two of this project's worst defects —
`docker compose config` is not verification, and a green gate is not a working deployment.

---

## 4. Agent-type summary

| Package | Agent | One-line |
|---|---|---|
| P1 | parent | push `main`; bound the ~33% simulation-image CI flake; probe runner PostgreSQL |
| **P2** | **coder** | **auth transport boundary inverted on `/api/auth/login-key`** |
| P3 | architect | DB access seam + honest coverage gate design |
| P4 | coder | seam for the profile path, Postgres fixture, gate step 7 |
| P5 | coder | PHI at rest: profile PII, addresses, contacts (migration 005) |
| P6 | coder | seam rollout to the other three repositories |
| P7 | architect | dataset signing + peer key registry design |
| P8 | coder | manifest schema, canonical signing bytes, export signing |
| P9 | coder | peer registry (migration 006), admin endpoints, import enforcement |
| P10 | coder | L4, L6, L8, delete-user case rule, audit-ledger assertion |
| P11 | coder | M5 (two sites), L1, L2, L7, wizard spinner, Dockerfile configs chown |
| P12 | coder | docs and deployment truth pass |
| P13 | architect | C8 spike, vendoring policy, ML-DSA decision |
| P14 | coder | C8 server half + L9 |
| P15 | coder | C8 client half: vendored Ed25519, `weaver-auth.js`, `Login.razor` |
| P16 | coder | delete the oracle; resolve ML-DSA × Web gate |
| P17 | coder | bUnit for `MainLayout`, `Login`, `SetupWizard` |
| P18 | coder | step-7 baseline closeout |
| P19 | parent | release verification, push, three user decisions |

Renumbering from rev 1: old P2–P18 are now P3–P19. New P2 is the transport-gate package.

---

## 5. Where I am uncertain, and what would resolve it

1. **Why CI flakes ~33% of the time** in the simulation image build-and-push step. The
   storage-contention hypothesis is strong (§1.5: `git.lantea.one` is `heimdall.lan`, and
   `heimdall` is also a runner, so server, registry storage and runner share a host) and the
   Intel apt hypothesis is refuted. **Resolver:** a logged-in browser session on the Forgejo run
   log, or a `df` on `heimdall`. Both are the user's call; P1 asks rather than acts.
2. **Can CI host PostgreSQL?** Host-mode `archlinux` runners mean Forgejo `services:` is
   probably unavailable, but Docker is clearly present. **Resolver:** the throwaway `docker run
   postgres:16` + `pg_isready` step in P1. If the answer is no, P4's fixture stays local-only
   and step 7's baseline simply improves more slowly — the plan does not change shape.
3. **ML-DSA-65 PKCS#8 encoding.** Unknown what BouncyCastle 2.7 writes. **Resolver:** the P13
   spike, with the actual bytes recorded. Both outcomes are planned for.
4. **The `ValkeyEventBridge.cs:265` guard.** I confirmed the cast has no justification comment
   and that `ValkeyCacheService.cs:98` is guarded by `HasValue && !IsNullOrEmpty` at line 96; I
   did not read the bridge's guard at ~261 closely enough to say whether a comment suffices or a
   real null check is needed. **Resolver:** P11 reads it and reports which fix it applied.
5. **Whether `--test-coverage-exclude` exists on the CI Node version.** Deliberately made
   irrelevant by the injected-signer design, but if someone wants the simpler route,
   `node --version` on a runner settles it.
6. **How much of the 694-line gap survives P6.** My estimate is that most of it is the
   duplicated Postgres plumbing and the per-call fallback branches, so P6 should move the
   baseline a long way — but it is an estimate, and P18 is where the real number is reported
   rather than predicted.

## 6. Nothing deferred — disposition of every open item

| Item | Package |
|---|---|
| Auth transport boundary inverted (Directive 12) | **P2** |
| PHI plaintext (addresses, contacts, and profile PII the queue did not list) | P5 |
| PostgreSQL untestability / coverage exclusion | P3, P4, P6, P18 |
| Dataset signing + peer key registry + import verification | P7, P8, P9 |
| C8 private-key transmission | P13, P14, P15, P16 |
| L9 unauthenticated challenge-store growth | P14 |
| L4, L6, L8 | P10 |
| `DeleteUserAsync` case sensitivity | P10 |
| Duplicate-user audit-ledger assertion | P10 |
| M5 null-forgiving operators (two remaining sites) | P11 |
| L1 `Task.Delay` UI pacing; wizard spinner | P11 |
| L7 operator path in a fixture | P11 |
| `Dockerfile.simulation` `/workspace/configs` ownership | P11 |
| L3 docs pointing at port 5000 across a network | P12 |
| `docs/compliance-and-security.md` P-256 claim | P12 |
| `example.env` undocumented variables | P12 |
| `GET /api/services` role note | P12 |
| Dedicated node-key mount as default | P12 |
| Corrupt-`picture` RFC 7807 release note | P12 |
| `AGENTS.md` §2 overstated signing claim | P9 |
| CI flakes ~33% in the simulation image step | P1 (bound + escalate), P19 (re-check) |
| `.razor` coverage blind spot | P17 |
| Live deployment verification, 390px, NVIDIA, push | P19 |
| `AGENTS.md` "5 checks" staleness | **done this session in `89f77cb`** — closed, do not re-open |
| M5's other ten sites, incl. the claimed latent NRE | **done this session in `89f77cb`** — closed |
| L2 DuckDB staging table | dropped as a mis-fix; comment only (P11) |
| `package-lock.json` tracking | dropped; gitignored with a reason (P12) |
| 390px via `resize_window` | dropped; one CDP attempt then an honest note (P19) |
| `docker-compose.nvidia.yml` verification | dropped; host setup, documented (P19) |


---

## 7. Standalone Cross-Platform Desktop & Mobile GUI Phases (`P26–P35`, branch `feature/gui-application`)

All work for the standalone multi-instance desktop and mobile GUI applications is executed on the `feature/gui-application` branch until code, 100% test coverage, compliance, documentation, GitHub Pages, code signing, and CI/CD artifact builds are complete, after which `feature/gui-application` is merged into `main` and deleted.

| Phase | Work Package | Target LLM Tier | Summary of Scope | Status |
| :--- | :--- | :--- | :--- | :--- |
| **P26** | `p26-gui-instance-store-and-key-vault.md` | `flash` / `sonnet` / `haiku` | `NanoWeaverHrt.Client` SQLite multi-instance connection profile repository (`IInstanceProfileRepository`) and `AES-256-GCM` client key vault (`IClientKeyRingVault`) for `Ed25519` and `ML-DSA-65`. | **COMPLETED** |
| **P27** | `p27-multi-instance-api-client-sdk.md` | `flash` / `sonnet` | `INanoWeaverNodeClient` PKI challenge-response authentication (`/api/auth/challenge` -> `/api/auth/token`), telemetry/simulation REST client, and `IMultiInstanceCoordinator` alert diffing. | **COMPLETED** |
| **P28** | `p28-background-tray-and-notifications.md` | `flash_lite` / `haiku` | `NanoWeaverHrt.Gui.Core` background monitoring daemon (`IBackgroundInstanceMonitor`), system tray state machine (`ITrayIconController`), notification dispatcher (`INativeNotificationSink`), and 15-minute idle lock. | **COMPLETED** |
| **P29** | `p29-avalonia-core-views-and-viewmodels.md` | `flash` / `sonnet` | Cross-platform MVVM ViewModels (`InstanceOnboardingViewModel`, `MainShellViewModel`), sovereign high-contrast dark theme palette, and 100% unit tests. | **COMPLETED** |
| **P30** | `p30-desktop-and-mobile-platform-heads.md` | `flash` / `sonnet` | `NanoWeaverHrt.Desktop` (`NanoWeaverHrt`) and `NanoWeaverHrt.Mobile` platform executables, OS-native notification and tray adapters (`Linux`, `macOS`, `Windows`, `Android`, `iOS`, `iPadOS`). | **COMPLETED** |
| **P31** | `p31-sovereign-codesign-pki-and-rotation.md` | `flash` / `haiku` | Sovereign X.509 `id-kp-codeSigning` CA + 90-day short-lived leaf certificate rotation scripts, GPG archive signing keyring, and scheduled monthly CI rotation workflows on Forgejo and GitHub Actions. | **COMPLETED** |
| **P32** | `p32-linux-packages-and-aur.md` | `flash_lite` / `haiku` | Reproducible Linux binary package builders for ArchLinux (`nanoweaver-hrt-desktop-{x86_64,arm64}.pkg.tar.zst`), unified Fedora RPM (`nanoweaver-hrt-desktop-{amd64,arm64}.rpm`), unified Debian/Ubuntu DEB (`nanoweaver-hrt-desktop-{amd64,arm64}.deb`), and AUR packages (`nanoweaver-hrt-desktop-bin` & `nanoweaver-hrt-desktop-git`). | **COMPLETED** |
| **P33** | `p33-macos-windows-mobile-packaging.md` | `flash` / `sonnet` | macOS `/Applications` bundle (`NanoWeaverHrt.dmg`), Windows dual-scope installer (`nanoweaver-hrt-desktop-installer.exe` supporting `Current User` or `System` with explicit choice when elevated) + Windows Store `NanoWeaverHrt.msix` artifact, and sideloadable Mobile packages (`nanoweaver-hrt-mobile-android.apk`, `nanoweaver-hrt-mobile-ios.ipa`, `nanoweaver-hrt-mobile-ipados.ipa`). | **COMPLETED** |
| **P34** | `p34-ci-cd-artifact-workflows.md` | `flash_lite` / `haiku` | Forgejo Actions (`.forgejo/workflows/build-and-deploy.yml`) and mirrored GitHub Actions (`.github/workflows/build-and-deploy.yml`) multi-platform GUI build, signing, and artifact upload jobs. |
| **P35** | `p35-docs-verification-and-merge-closeout.md` | `flash` / `sonnet` | Author `docs/desktop-and-mobile-gui.md`, update Docsify `_sidebar.md` and security docs, run full 8-gate verification, merge `feature/gui-application` into `main`, push to `origin`, and delete `feature/gui-application`. |
