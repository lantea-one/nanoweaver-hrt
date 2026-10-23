# Work package P6 — roll the database seam out to the remaining three repositories

> **Why this file is not under `docs/`.** Gate step 5 scans `docs/**/*.md`, `docs/*.md` and
> `README.md` for paired single-`$` tokens outside backticks and fenced blocks. Work packages are
> full of `$WEAVER_TEST_POSTGRES`, `$(nproc)` and `${VAR}`; two such tokens in prose turn the gate
> red locally and in CI. `work-packages/` is in-repo, version-controlled and outside that scan.
> **Do not move this directory under `docs/`.**

**Agent:** coder. **Dependencies:** P4 (the seam) and P5 (which rewrites Migration003-era dialect
SQL this package would otherwise re-derive from the pre-P5 shape). **Read
`work-packages/db-seam-profile-repo.md` first** — its Interfaces section is the contract you build
against and its Constraints apply here unchanged.

**Sizing.** Three ordered stages. Each one leaves the tree green at 7/7, so stopping between stages
is safe. **Stopping rule: if your diff exceeds roughly 600 changed lines at the end of stage B,
stop, report, and let the parent dispatch stage C as its own pass.** Do not carry a half-finished
stage into a report.

---

## Goal

1. Delete the remaining silent `PostgreSQL → SQLite` fallbacks, so no deployment can end up with
   some rows in PostgreSQL and some in a local `weaver_config.db` while the gate stays green.
2. Collapse the hand-written dialect twins in three repositories onto the single execution path P4
   introduced, which removes the large majority of the step-7 baseline's uncovered lines — those
   lines are duplicated PostgreSQL plumbing that no SQLite test can ever reach.
3. Move the **one** backend probe out of `SystemConfigurationStore` and into
   `WeaverDbConnectionFactory`, so the decision and the connections come from the same place, and
   make "a PostgreSQL connection string is configured but the server is unreachable" a logged
   warning instead of an indistinguishable silence.

---

## Files in scope

### Stage A — `UserRepository`

| Path | Role |
|---|---|
| `src/NanoWeaverHrt/Security/Authentication/UserRepository.cs` | Eight methods, each with a hand-written PostgreSQL twin and a bare `catch` that falls through to SQLite. All eight collapse to one path. |
| `tests/NanoWeaverHrt.Tests/AuthenticationServiceTests.cs` | Constructor/signature updates; `UserRepository_MapUserFromReader_PostgresTypes_Succeeds` (`:834`) stays as-is — it is permitted pure-mapper input. |
| `tests/NanoWeaverHrt.Tests/UserProvisioningEndpointTests.cs` | Constructor/signature updates only. |

### Stage B — `PatientDnaRepository`

| Path | Role |
|---|---|
| `src/NanoWeaverHrt/Storage/PatientDnaRepository.cs` | Already single-path; only `OpenConnectionAsync` (`:86-111`) and its bare `catch` need replacing, plus `ParseDateTimeOffset` and the inline `AddParam`-style blocks. |
| `tests/NanoWeaverHrt.Tests/PatientDnaAndImportTests.cs` | Rewrite `PatientDnaRepository_PostgresFallback_AndConstructorBranches` (`:678`); re-point `ParseDateTimeOffset_AllBranches_EvaluatedCorrectly` (`:623`) at `WeaverDbValues.ReadTimestamp`. |

### Stage C — `SystemConfigurationStore` and the probe inversion

| Path | Role |
|---|---|
| `src/NanoWeaverHrt/Configuration/SystemConfigurationStore.cs` | Nine dialect branch sites; the startup probe moves out; `ActiveBackendName` delegates. |
| `src/NanoWeaverHrt/Database/Access/WeaverDbConnectionFactory.cs` | Gains one-time backend resolution and the configured-but-unreachable warning. |
| `src/NanoWeaverHrt/Database/Access/IWeaverDbConnectionFactory.cs` | Gains `ActiveBackendDisplayName` and `EnsureBackendResolvedAsync`. |
| `src/NanoWeaverHrt/Program.cs` | Startup already calls `IConfigurationRepository.InitializeAsync` (`:223`); add nothing new unless the inversion requires the factory to be resolved first, in which case add exactly one line and say so. |
| `tests/NanoWeaverHrt.Tests/WebUiAndConfigurationTests.cs` | `MockPostgresConfigRepo` (`:829-834`) and the backend-name assertions. |
| `tests/NanoWeaverHrt.Tests/SystemHealthEndpointTests.cs` | Moq stubs of `IConfigurationRepository.ActiveBackendName` at `:588` and `:659`. |

### All stages

| Path | Role |
|---|---|
| `tests/NanoWeaverHrt.Tests/PostgresBackedRepositoryTests.cs` (new) | Extends the P4 `WEAVER_TEST_POSTGRES` fixture to the named low-coverage methods. |
| `coverage-baseline.json` | **Must be regenerated in this same diff** (`--update`) and must move **down**. |

### Read-only context

`work-packages/db-seam-profile-repo.md`, `src/NanoWeaverHrt/Security/Authentication/UserProfileRepository.cs`
(the converted reference implementation), `src/NanoWeaverHrt/Database/Migrations/DatabaseMigrationRunner.cs`.

---

## Every call site, enumerated

Line numbers are pre-P4/P5 and **will have moved**; locate by method name, not by number.

### `UserRepository.cs` — eight sites, each `if (isPostgres) { try { …Npgsql twin… } catch { /* fall back */ } }` followed by a SQLite twin

| Method | Postgres twin | Notes |
|---|---|---|
| `InitializeAsync` | `:96-120` | Keeps the `!isPostgres && _initializedPath == _sqlitePath && File.Exists(...)` double-checked short-circuit (`:80-91`) — see Risks. |
| `HasAnyAdministratorAsync` | `:136-150` | `is_active = TRUE` vs `= 1` → `session.Dialect.TrueLiteral`. |
| `GetUserAsync` | `:174-194` | Identical SQL text both sides; the twin disappears entirely. |
| `ListUsersAsync` | `:224-243` | Identical SQL text both sides. |
| `CreateUserAsync` | `:275-307` | `EXCLUDED.` vs `excluded.` is case only; SQL keywords are case-insensitive on both engines. Keep one spelling. |
| `TryCreateUserAsync` | `:354-380` | `ON CONFLICT (username) DO NOTHING` is valid on both. Preserve the "unique key decides" comment at `:348-350`. |
| `UpdateLastLoginAsync` | `:416-432` | Timestamp coercion only. |
| `DeleteUserAsync` | `:457-472` | Identical SQL text both sides. |

Also delete `CreateTablesPostgresAsync` (`:493`) and `CreateTablesSqliteAsync` (`:504`) — the factory
applies migrations. `MapUserFromReader` (`:514`) keeps its signature and `internal static`
accessibility; its inline `GetFieldType` checks (`:522-545`) become `WeaverDbValues` calls.

### `PatientDnaRepository.cs` — one site serving twelve methods

`OpenConnectionAsync` (`:86-111`) is the only fallback; replacing it with
`await _connectionFactory.OpenAsync(ct)` fixes `GetDnaRecordsAsync`, `GetDnaRecordByIdAsync`,
`UpsertDnaRecordAsync`, `DeleteDnaRecordAsync`, `GetImportsAsync`, `GetImportByIdAsync`,
`UpsertImportAsync`, `DeleteImportAsync`, `GetResultsAsync`, `GetResultByIdAsync`,
`UpsertResultAsync`, `DeleteResultAsync` at once. Then: the three `if (dialect == "postgresql")`
parameter blocks inside the three upserts (`:233`, `:409`, `:572` regions) collapse via
`session.Dialect`; `ParseDateTimeOffset` (`:785`) is deleted in favour of
`WeaverDbValues.ReadTimestamp`; the three `Map*FromReader` methods keep their signatures.

### `SystemConfigurationStore.cs` — nine dialect branch sites, and a correction to the brief

**Correction, verified against source:** this file does **not** have the per-call bare `catch`. Its
nine sites branch on `_activeBackend.StartsWith("PostgreSQL", ...)` with no `try`/`catch`, so a
PostgreSQL failure here already propagates. Its single fallback is the startup probe in
`InitializeAsync` (`:144-166`), which is legitimate and is being *moved*, not deleted. Do not go
looking for eight more `catch` blocks in this file; there are none.

Sites: `SetDatabaseOverrideAsync` (`:350`), `RevertToDefaultAsync` (`:408`),
`CreateShareKeyAsync` (`:470`), `ConsumeShareKeyAsync` (`:561`), `ListShareKeysAsync` (`:635`),
`RevokeShareKeyAsync` (`:699`), `ReadAllDbOverridesAsync` (`:731`), `ReadSingleDbOverrideAsync`
(`:766`), `FindKeyByTokenAsync` (`:794`). Dialect-specific content at those sites is only: the
boolean literals `is_consumed = TRUE` / `= FALSE` (`:567`, `:570`, `:703`) → `TrueLiteral` /
`FalseLiteral`; `IsConsumed` read as `GetBoolean` vs `GetInt32(...) != 0` → `WeaverDbValues.ReadBoolean`;
and `DateTime` vs round-trip-string timestamps → `WeaverDbValues` / `ISqlDialect.TimestampValue`.
`ShareKeyRecord` exposes `DateTime`, not `DateTimeOffset`; convert at the boundary
(`WeaverDbValues.ReadTimestamp(...).UtcDateTime`) rather than changing the record.

---

## Interfaces and signatures

### Added to the P4 seam (stage C only)

```csharp
public interface IWeaverDbConnectionFactory
{
    WeaverDatabaseBackend ActiveBackend { get; }                 // from P4; now answered by the resolution below

    /// Human-readable name of the resolved backend: "PostgreSQL (pg-duckdb)" or "Local SQLite Fallback".
    /// Exactly the two strings SystemConfigurationStore produces today, so no UI, API response,
    /// telemetry assertion or test string changes.
    string ActiveBackendDisplayName { get; }

    /// Resolves the backend once per process and applies that backend's pending migrations.
    /// Idempotent and safe to call concurrently. Subsequent calls return the cached decision.
    Task<WeaverDatabaseBackend> EnsureBackendResolvedAsync(CancellationToken ct = default);

    Task<WeaverDbSession> OpenAsync(CancellationToken ct = default);
}
```

Resolution algorithm, exactly once, guarded by the factory's own `SemaphoreSlim`:

```
EnsureBackendResolvedAsync(ct):
  if _resolved is not null: return _resolved

  if string.IsNullOrWhiteSpace(_postgresConnectionString):
      // No PostgreSQL configured. SQLite is the intended backend, not a degradation.
      _resolved = Sqlite;  _displayName = "Local SQLite Fallback"
  else:
      try:  open an NpgsqlConnection once, apply "postgresql" migrations
            _resolved = PostgreSql;  _displayName = "PostgreSQL (pg-duckdb)"
      catch (ex) when not OperationCanceledException:
            // Configured but unreachable. THIS is the case that used to be silent.
            _logger.LogWarning(ex,
                "A PostgreSQL connection string is configured but the server could not be reached. "
                + "Falling back to the embedded SQLite database for this process. Data written now "
                + "will NOT be in PostgreSQL.");
            _resolved = Sqlite;  _displayName = "Local SQLite Fallback (PostgreSQL configured but unreachable)"
  if _resolved == Sqlite: open a SqliteConnection once, apply "sqlite" migrations
  return _resolved
```

- The warning is logged **once per process**, at resolution, never per call.
- `_logger` is `ILogger<WeaverDbConnectionFactory>`, injected as a fourth constructor parameter.
  The message must contain no connection string, host, port, password or file path (Directive 14).
  Assert that with a test using a capturing `ILogger` provider.
- `OpenAsync` calls `EnsureBackendResolvedAsync` first and otherwise behaves exactly as P4
  specified: once the backend is `PostgreSql`, a later failure throws
  `WeaverDatabaseUnavailableException`. There is no second fallback.
- The factory no longer takes `IConfigurationRepository`. **This is what breaks the dependency
  cycle** — see Risks.

### `SystemConfigurationStore` after stage C

```csharp
public SystemConfigurationStore(IWeaverDbConnectionFactory connectionFactory);
public string ActiveBackendName => _connectionFactory.ActiveBackendDisplayName;
public async Task InitializeAsync(CancellationToken ct = default)
    => await _connectionFactory.EnsureBackendResolvedAsync(ct);
```

- The `_activeBackend`, `_sqlitePath`, `_postgresConnString`, `_initializedPath` and
  `_migrationRunner` fields are deleted, along with `CreateTablesPostgresAsync`,
  `CreateTablesSqliteAsync` and the probe. `Definitions`, `SecretKeyNameMarkers`,
  `IsSecretConfigurationKey` and `ConfigDefinition` are untouched.
- `Dispose()` keeps disposing `_lock` only; it must **not** dispose the factory or the runner.
- The `IConfiguration` constructor parameter goes away; the factory already reads
  `POSTGRES_CONNECTION_STRING` and `TRAJECTORY_INCOMING_DIR`. If any test needs a store bound to a
  temp directory, it builds a factory bound to that directory.

### Repository constructors after conversion

```csharp
public UserRepository(IWeaverDbConnectionFactory connectionFactory);
public PatientDnaRepository(IWeaverDbConnectionFactory connectionFactory);
public UserProfileRepository(IWeaverDbConnectionFactory connectionFactory, ProfileDataProtector protector);  // from P4
```

`Program.cs` registrations keep their current order and need no change beyond what P4 added,
because DI resolves the factory for all four.

---

## Constraints

Everything in `work-packages/db-seam-profile-repo.md` § Constraints applies unchanged. Restated
because these two are the ones that get re-litigated:

1. **Mocking a database driver is prohibited** — Moq, hand-written subclass or wrapper interface
   alike — for `DbConnection`, `DbCommand`, `DbTransaction`, `DbParameter`, `NpgsqlConnection`,
   `SqliteConnection` and every member of those hierarchies. `IWeaverDbConnectionFactory` is
   satisfied **only** by `WeaverDbConnectionFactory`, in tests included; there must be no
   `FakeWeaverDbConnectionFactory` or `InMemoryDbSession` in the diff. A branch that no real
   database can reach is covered by **deleting the branch**, or it stays in the step-7 baseline.
   Reporting an honest 97% is the correct outcome; a mocked 100% is not.
   - Permitted, unchanged from P4: a `DataTable.CreateDataReader()` reader or the existing
     `MockDateTimeDataReader`, used **only** as table-driven input to a pure static mapper
     (`MapUserFromReader`, `Map*FromReader`, `WeaverDbValues.*`). Permitted: Moq on our own
     interfaces (`IConfigurationRepository`).
2. **This is not a micro-ORM.** Every `SELECT`, `INSERT`, `UPDATE` and `DELETE` stays written out in
   its repository as a string literal. No query builder, no entity mapper, no `Repository<T>`, no
   new `ISqlDialect` member that composes a clause. `ISqlDialect` keeps exactly the seven members
   P4 defined. If you need an eighth, stop and report the case instead of adding it.
3. **One SQL text per statement, chosen for both engines.** Where the two twins' SQL differs only
   in keyword case (`EXCLUDED` vs `excluded`) or in a boolean literal, keep one text and use
   `TrueLiteral` / `FalseLiteral`. If you find a statement whose text genuinely cannot be shared,
   **do not invent a dialect hook** — name it in the report and leave both twins in place for that
   one statement, with a comment saying why.
4. **The baseline must drop, and it must drop in this diff.** Run
   `python3 -I scripts/check-coverage-ratchet.py artifacts/coverage-full coverage-baseline.json --update`
   and commit the new numbers alongside the code. Never pass `--allow-regression`. Quote the
   old→new `line_rate`, `branch_rate` and `uncovered_lines_total` in the report, plus the five
   classes that improved most.
5. **No DDL** (Directive 13): no migration is added, removed or edited in this package. P5 owns
   `005`; P9 owns `006`.
6. Directive 2 and gate step 6 on every new and edited member, private included. Directive 3
   WHAT/WHY/HOW on the resolution algorithm, the warning path and each converted upsert.
7. Directive 6 / the test-isolation memory rule: no sleeps, no ambient network or filesystem state,
   per-test temp directories under `Path.GetTempPath()`, nothing under `simulations/`.
8. Directive 14: no `ex.Message`, path, connection string, PEM or SQL fragment reaches a client or a
   log line added here.
9. Directive 7 and zero warnings.
10. **Do not touch** `coverlet.runsettings`, `coverage-full.runsettings`, any of the 14 Directives in
    `AGENTS.md`, `.claude` configuration, `ProfileDataProtector`, any `Migration0*.cs`, or any
    `.razor` file.
11. Do not commit and do not push.

---

## Acceptance criteria

**Fallback removal**

1. `grep -rn "catch$\|catch {\|catch$" src/NanoWeaverHrt/Security/Authentication/UserRepository.cs
   src/NanoWeaverHrt/Storage/PatientDnaRepository.cs
   src/NanoWeaverHrt/Configuration/SystemConfigurationStore.cs` finds no exception handler whose
   body falls through to a SQLite connection. The only `catch` left in the three files is one that
   re-throws or one with a WHAT/WHY/HOW comment justifying it, and the report lists each survivor.
2. `grep -rn "NpgsqlConnection\|SqliteConnection\|NpgsqlCommand\|SqliteCommand" src/NanoWeaverHrt/`
   returns hits **only** in `Database/Access/WeaverDbConnectionFactory.cs`. No repository, no store
   and no endpoint names a driver type any more.
3. For each of the three classes, a test with a `MockPostgresConfigRepo`-equivalent setup proves
   that a selected-but-unreachable PostgreSQL makes the operation **throw**
   `WeaverDatabaseUnavailableException` and that **no `weaver_config.db` is created** in the test's
   temp directory. Three tests, one per class, each with that file-absence assertion.
4. A test asserts that when `POSTGRES_CONNECTION_STRING` is set to an unreachable endpoint, the
   resolution warning is logged exactly once across several repository calls, and that the logged
   message contains no host, port, password or path.
5. A test asserts that when no connection string is configured at all, **no warning is logged** —
   SQLite-only operation is the intended configuration, not a degradation, and warning on it would
   train operators to ignore the warning that matters.

**Behaviour preserved**

6. The whole pre-existing suite passes. `IConfigurationRepository.ActiveBackendName` still returns
   exactly `"PostgreSQL (pg-duckdb)"` or a string beginning `"Local SQLite Fallback"`, so
   `WebUiAndConfigurationTests.cs:350`, the `/api/services` telemetry assertions and
   `SystemHealthEndpointTests.cs:588,659` keep working. Name any assertion you changed and why.
7. `InitializeAsync`'s short-circuit semantics are preserved: a deleted SQLite file is re-created on
   the next call. Assert it — delete the file mid-test and make a second call succeed. This
   short-circuit is load-bearing for the C3 mitigation; do not simplify it away.
8. The `weaver_share_keys` single-use semantics are unchanged: a test consumes a token twice and the
   second attempt still fails, on both backends.
9. `DatabaseMigrationRunner` still records versions in `weaver_schema_migrations` with the same
   dialect strings (`"postgresql"` / `"sqlite"`); a test reads the table and asserts versions
   1–5 present after a fresh start on each backend.

**Real-PostgreSQL coverage of the named methods**

10. With `WEAVER_TEST_POSTGRES` set, round-trips pass against real PostgreSQL for every method the
    plan named as low-coverage: `SystemConfigurationStore.FindKeyByTokenAsync` (via
    `ConsumeShareKeyAsync`), `UserRepository.CreateUserAsync`, `TryCreateUserAsync`,
    `InitializeAsync`, `GetUserAsync`, `ListUsersAsync`, `UpdateLastLoginAsync`,
    `HasAnyAdministratorAsync`, `DeleteUserAsync`, `CreateShareKeyAsync`, `ConsumeShareKeyAsync`,
    plus one DNA record, one import and one result round-trip. The report states how many tests ran
    against PostgreSQL.
11. A PostgreSQL-backed test proves `TryCreateUserAsync` returns `false` (not an exception) on a
    duplicate username, matching the SQLite behaviour and the 409-Conflict contract that landed in
    `c506097`.
12. A PostgreSQL-backed test proves `HasAnyAdministratorAsync` returns `true` only for an active
    administrator, exercising `TrueLiteral` on the real engine.
13. With `WEAVER_TEST_POSTGRES` unset, the suite passes unchanged and the PostgreSQL-backed tests
    print their skip line. With it set to an unreachable endpoint, they **fail** rather than skip.
14. No `weaver_test_*` database and no file under `simulations/` survives a full run.

**Gate**

15. `./scripts/verify-all.sh` 7/7 green; step 4 still exactly `100.00%` line and `100.00%` branch;
    0 warnings; 0 incomplete docblocks; `dotnet format --verify-no-changes` clean.
16. `coverage-baseline.json` is updated in this diff and every one of `line_rate`, `branch_rate`
    (both up) and `uncovered_lines_total` (down) moves in the improving direction. No class's
    uncovered count rises. If any class got worse, the package is not done.
17. Every fallback branch the plan counted is accounted for in the report as either **covered by a
    real database** or **deleted**. No branch is accounted for as "mocked".

---

## Non-goals

- **No PHI encryption, no column widening, no new migration.** P5 landed those; re-deriving the
  dialect SQL P5 just changed is the specific mistake this package's position in the order exists to
  prevent. Read the post-P5 file, not the shape described in the original audit.
- **Do not change `Definitions`, `SecretKeyNameMarkers` or `IsSecretConfigurationKey`** in
  `SystemConfigurationStore`. The redaction classifier is a security control with its own tests and
  is out of scope.
- **Do not touch `_activeChallenges`, `/api/auth/*` or `UserAuthenticationService`.** Those belong
  to P10/P14 and a second independent edit there is the known silent-conflict failure mode.
- **Do not add a PostgreSQL service to `.forgejo/workflows/build-and-deploy.yml`** unless the parent
  hands you P1's recorded yes/no on runner-hosted PostgreSQL *and* explicitly asks for it. Step 7
  deliberately measures the PostgreSQL-absent configuration, so the ratchet does not depend on the
  answer either way.
- **Do not optimise connection pooling, caching or migration application.** One connection per
  operation, migrations applied per open, exactly as today.
- **Do not raise the step-7 baseline.** It moves down or the package is not finished.
- **No bUnit, no `.razor`, no `Components.*` coverage exclusion changes.** That is P17.

---

## Risks and traps

1. **The dependency cycle.** `WeaverDbConnectionFactory` must stop taking `IConfigurationRepository`
   in stage C. If it keeps that dependency while `SystemConfigurationStore` starts taking the
   factory, DI construction is circular and the app fails to start — or worse, `ActiveBackendName`
   asks the factory which asks the store. Invert cleanly: the factory depends on `IConfiguration`,
   `DatabaseMigrationRunner` and `ILogger` only, and the store depends on the factory. Verify by
   starting the host in a test (`WebApplicationFactory<Program>`), not by reading the registrations.
2. **`SystemConfigurationStore` has produced two semantic rebase conflicts in this project.** Its
   `Definitions` catalog takes a **union** on conflict, never one side wholesale. If your rebase
   touches it, say so in the report and state what you unioned.
3. **`InitializeAsync`'s short-circuit** (`_initializedPath == _sqlitePath && File.Exists(...)`) is
   load-bearing for the C3 mitigation. The factory's one-time resolution must not lose the
   "re-create the schema if the file disappeared" property. Criterion 7 is the test for it.
4. **`is_active`, `is_consumed`, `is_primary`, `can_discuss_medical_info`** are the four boolean
   columns. Each has a `TRUE`/`1` literal form, a parameter form and a reader form. Missing one of
   the three gives a bug that only shows on one engine — exactly the class of defect this package
   exists to remove. Enumerate all four in the report with the three forms you used.
5. **Timestamps.** PostgreSQL stores `TIMESTAMPTZ` and returns `DateTime`; SQLite stores the
   round-trip `"o"` string. `ShareKeyRecord` exposes `DateTime` while the profile types expose
   `DateTimeOffset`. Convert at the boundary; do not change a public record's property type to make
   the seam tidier.
6. **A green gate is not a working deployment.** P19 brings the stack up and checks that the
   parser's schema genuinely exists in PostgreSQL rather than having silently fallen back. This
   package is what makes that check meaningful — so make sure the warning in criterion 4 is
   actually visible in container logs, not swallowed by a log-level filter.
