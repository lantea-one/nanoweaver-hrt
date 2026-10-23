# Work package: encrypt the gender-identity trio (migration 006)

**Base commit: `c5bdbfc`.** Your worktree branches from the session-start commit. Your FIRST
action is `git rebase main` (it should be a no-op — `main` is also at `c5bdbfc`). Confirm the
rebase result in your report before describing anything else. Do not commit. Do not push.

## Why this exists

`weaver_user_profiles` stores three columns in readable text:

| column | DDL (migration 003) | holds |
|---|---|---|
| `regimen` | `VARCHAR(64)` nullable | `MaleToFemale` / `FemaleToMale` |
| `gender_assigned_at_birth` | `VARCHAR(32) NOT NULL DEFAULT 'Female'` | `Male` / `Female` / … |
| `pronouns_json` | `TEXT NOT NULL DEFAULT '[]'` | JSON array of pronouns |

`regimen` paired with `gender_assigned_at_birth` identifies a person as transgender. That is
GDPR Article 9 special-category data and, for this patient population, a higher real-world risk
than the postal code migration 005 already sealed. Migration 005 left these three out of scope
and said so in its `<remarks>`. This package closes that gap.

## Scope — exactly this, nothing more

Encrypt those three columns of `weaver_user_profiles` at rest, as **migration 006**, following
`Migration005_EncryptPatientPhi` as the precedent.

**Explicitly OUT of scope** (do not touch; report if you think otherwise):
- The *other* `regimen` column, on the patient-DNA table from migration 004
  (`regimen VARCHAR(64) NOT NULL`, read by `PatientDnaRepository` at `:654` via
  `reader.GetString(3)`). Sealing it needs `PatientDnaRepository` ported to the protector,
  which belongs to the P6 seam rollout. Leave it alone and note it in your report.
- `language_preference`, `username`, `id`, timestamps, and every column migration 005 listed as
  deliberately-plaintext.

## What you must do

### 1. `Migration006_EncryptGenderIdentity`

Mirror `Migration005_EncryptPatientPhi` in structure, docblocks and `WHAT/WHY/HOW` comments.
- `Version => 6`, `Name => "006_EncryptGenderIdentity"`.
- Register it with the runner wherever 005 is registered, in order.
- PostgreSQL `GetSql`: `ALTER COLUMN … TYPE TEXT` for all three. SQLite needs no DDL
  (`pronouns_json` is already `TEXT`; the other two are `TEXT` under SQLite).
- Data step: rewrite existing rows in place, skipping values that already carry the envelope
  prefix, inside the migration transaction. Read all rows before updating (SQLite cannot update
  through an open reader — 005 already handles this; copy the approach).
- Throw loudly if a row needs encrypting and no protector was supplied.

### 2. The `NOT NULL DEFAULT` trap — this is a real defect, not a formality

`gender_assigned_at_birth` defaults to `'Female'` and `pronouns_json` to `'[]'` **in the DDL**.
Decryption in this codebase is **strict**: `ProfileDataProtector.DecryptField` /
`DecryptRequiredField` hand any non-empty non-envelope value to `DecryptEnvelope`, which
**throws `CryptographicException`** (see `ProfileDataProtector.cs:290` and `:322`, and the
`<remarks>` at `:286`: "after migration 005 no plaintext can legitimately be present").

So any row written by a path that relies on the column DEFAULT stores plaintext that will
**throw on the next read**. Migration 006 must therefore also `DROP DEFAULT` on both columns
under PostgreSQL, and you must verify no insert path omits them. `UpsertProfileAsync` supplies
all three explicitly (`@reg`, `@gen`, `@pro` at `UserProfileRepository.cs:128-130`), but prove
it rather than assume: grep for every INSERT into `weaver_user_profiles`, including in tests.
If dropping the default is impossible under SQLite without a table rebuild, say so and explain
what protects the invariant instead — do not rebuild the table.

### 3. `UserProfileRepository`

- Write path (`:128-130`): seal `@reg` with `EncryptField` (nullable), `@gen` and `@pro` with
  `EncryptRequiredField` (NOT NULL). Preserve the existing `DBNull.Value` behaviour for a null
  regimen — a null regimen must stay NULL, not become an envelope of `""`.
- Read path (`MapProfileFromReader`, `:398-400`): decrypt before `Enum.TryParse` and before
  `JsonSerializer.Deserialize`. Keep the existing tolerance of an unparseable enum and the
  existing `catch` fallback for corrupt JSON — order matters: decrypt first, then parse.

### 4. The modified test you must get right

`tests/NanoWeaverHrt.Tests/UserProfileAndContactTests.cs:328` writes raw `'{invalid_json'`
straight into `pronouns_json` with a `SqliteCommand` to cover the `catch` in the mapper. Line
`:340` does the same with `'null'`. **After 006 those raw writes make the strict decrypt throw,
so the `catch` branch they exist to cover is no longer reached by them.**

Do not delete these tests and do not weaken the decrypt. Rewrite each to store an **encrypted
envelope whose plaintext is the invalid JSON**, so the mapper decrypts successfully and *then*
hits the JSON `catch`. The branch must still be covered, by the same mechanism, for the same
reason. In your report, quote the before and after of both tests.

### 5. New tests

- Round-trip: a profile with each regimen value, a gender, and a pronoun array survives
  save→load unchanged.
- Null regimen stays NULL and loads back as null.
- **Raw-`SELECT` assertion**: after saving, read the three columns with a raw query and assert
  the stored bytes do **not** contain `MaleToFemale`, `FemaleToMale`, the gender literal, or any
  pronoun substring. Assert the rows EXIST, not merely that nothing threw.
- Migration idempotence: run the data step **twice** and assert values are unchanged and
  single-sealed.
- A pre-existing plaintext row (written raw, as migration 003 would have) is migrated and then
  readable.
- The loud failure when a row needs encrypting and no protector was supplied.
- Run the whole thing under PostgreSQL too (`WEAVER_TEST_POSTGRES`), proving the `TYPE TEXT`
  widening and the `DROP DEFAULT` actually apply. Use the explicit availability guard —
  `Assert.Skip` does **not** compile against this project's xunit 2.9.2.

## Hard constraints

- Tests use isolated temp data directories. Never bind to the live `simulations/` tree.
- Mocking `DbConnection`/`DbCommand`/`DbDataReader`/`NpgsqlConnection` is **prohibited**.
- `ISqlDialect` is capped at **seven** members. If you need an eighth: stop and report.
- 100% line and branch coverage on everything you add; full docblocks on every member,
  private included; `WHAT/WHY/HOW` on each algorithm and loop.
- Never put a work package or any new `.md` under `docs/` (gate step 5 flags paired `$`).
- Do not edit `AGENTS.md`.
- Finish by running `./scripts/verify-all.sh` and report each of the 7 steps' real result. If a
  step fails, report the failure — do not paper over it.

## Report back

1. The rebase result.
2. Whether anything does `WHERE`/`ORDER BY`/`GROUP BY`/`JOIN` on the three columns. My own
   single-line grep found none, but multi-line SQL could hide one — check the statements, not
   just the lines, and report before changing query semantics.
3. Whether `pronouns_json` is ever queried *as JSON* (a JSON operator or function). Sealing it
   ends that capability. I believe nothing does; confirm.
4. The before/after of both modified tests.
5. The 7 gate step results, verbatim.
