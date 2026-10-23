# Work package P4 — database access seam for the profile path, real-Postgres fixture, gate step 7

> **Why this file is not under `docs/`.** Gate step 5 scans `docs/**/*.md`, `docs/*.md` and
> `README.md` for paired single-`$` tokens outside backticks and fenced blocks. Work packages are
> full of `$WEAVER_TEST_POSTGRES`, `$(nproc)` and `${VAR}`; two such tokens in prose turn the gate
> red locally and in CI. `work-packages/` is in-repo, version-controlled and outside that scan.
> **Do not move this directory under `docs/`.**

**Agent:** coder. **One pass.** **Dependencies:** none beyond current `main`.
**Blocks:** P5 (PHI at rest) and P6 (seam rollout).

---

## Goal

Three outcomes, in this order of importance:

1. **Make the split-brain failure impossible.** Today, when the active backend is PostgreSQL and
   the Postgres call throws, `UserProfileRepository.OpenConnectionAsync` (`:95-108`) swallows the
   exception and silently returns a **SQLite** connection instead. Every subsequent statement in
   that call lands in the local `weaver_config.db`. After this package, a configured-and-selected
   PostgreSQL backend that fails is an **error that propagates**, never a silent redirect to a
   different database.
2. **Give the profile path one execution path and one real-Postgres test fixture**, so P5 can
   prove its PHI encryption against the backend it will actually run on. This matters concretely:
   `Migration003_UserProfileAddressesAndContacts` declares `email_address VARCHAR(256)`,
   `phone_number VARCHAR(50)`, `date_of_birth VARCHAR(32)`, `postal_code VARCHAR(32)` on
   PostgreSQL and plain `TEXT` on SQLite. Base64-wrapped AES-256-GCM ciphertext does not fit the
   narrow ones. SQLite enforces no declared width, so P5's encryption would pass every test on
   SQLite while PostgreSQL raised `22001 string_data_right_truncation` — swallowed by the bare
   `catch`, written to the SQLite file instead, gate green.
3. **Make the coverage number honest** by adding gate step 7: a second, un-excluded measurement
   compared against a committed ratchet baseline. Step 4 is unchanged.

---

## Files in scope

### New production files

| Path | Role |
|---|---|
| `src/NanoWeaverHrt/Database/Access/WeaverDatabaseBackend.cs` | Two-value enum naming the backend. The only representation of "which engine" in new code. |
| `src/NanoWeaverHrt/Database/Access/ISqlDialect.cs` | The dialect seam: the *only* members are the handful of fragments and value coercions that genuinely differ between the two engines. |
| `src/NanoWeaverHrt/Database/Access/SqliteDialect.cs` | SQLite implementation. Pure, stateless, singleton instance. |
| `src/NanoWeaverHrt/Database/Access/PostgreSqlDialect.cs` | PostgreSQL implementation. Pure, stateless, singleton instance. |
| `src/NanoWeaverHrt/Database/Access/IWeaverDbConnectionFactory.cs` | The connection seam. One place decides which engine a connection targets. |
| `src/NanoWeaverHrt/Database/Access/WeaverDbConnectionFactory.cs` | The only implementation. Returns a real `SqliteConnection` or a real `NpgsqlConnection` — nothing else, ever, including under test. |
| `src/NanoWeaverHrt/Database/Access/WeaverDbSession.cs` | `IAsyncDisposable` pairing an open `DbConnection` with its `ISqlDialect`, plus `CreateCommand(sql)`. Replaces the `(DbConnection, string)` tuple. |
| `src/NanoWeaverHrt/Database/Access/WeaverDbParameters.cs` | Static `Add` / `AddNullable` helpers. Lifts the four private `AddParam` copies into one place. |
| `src/NanoWeaverHrt/Database/Access/WeaverDbValues.cs` | Static reader-side coercions (`ReadBoolean`, `ReadTimestamp`, `ReadNullableTimestamp`). Lifts the duplicated `GetFieldType(...) == typeof(bool)` / `typeof(DateTime)` runtime checks into one place. |
| `src/NanoWeaverHrt/Database/Access/WeaverDatabaseUnavailableException.cs` | Thrown when the selected backend cannot be reached. Carries the backend *name* and the inner driver exception; never the connection string. |

### Modified production files

| Path | Change |
|---|---|
| `src/NanoWeaverHrt/Security/Authentication/UserProfileRepository.cs` | Delete `OpenConnectionAsync`, the bare `catch`, the private `AddParam`, `ParseDateTimeOffset`, and the `IConfiguration`/`DatabaseMigrationRunner`/path/connection-string fields. Take `IWeaverDbConnectionFactory` instead. All nine public methods now obtain a `WeaverDbSession` from the factory. |
| `src/NanoWeaverHrt/Program.cs` | One registration line: `builder.Services.AddSingleton<IWeaverDbConnectionFactory, WeaverDbConnectionFactory>();` immediately after `AddSingleton<DatabaseMigrationRunner>()` (currently line 112) and before the repository registrations. Nothing else. |

### Modified test files

| Path | Change |
|---|---|
| `tests/NanoWeaverHrt.Tests/UserProfileAndContactTests.cs` | Rewrite `UserProfileRepository_ConstructorAndPostgresFallback_CoverBranches` (`:174-222`) — it currently *asserts the silent fallback as correct behaviour*. It must now assert the opposite. Update every `new UserProfileRepository(...)` call to the new signature. |
| `tests/NanoWeaverHrt.Tests/WeaverDbAccessSeamTests.cs` (new) | Direct unit tests for the dialects, backend resolution, parameter/value helpers, session disposal, and the exception type. No database server needed. |
| `tests/NanoWeaverHrt.Tests/PostgresTestEnvironment.cs` (new) | The `WEAVER_TEST_POSTGRES` collection fixture: availability detection, per-test database creation and teardown. |
| `tests/NanoWeaverHrt.Tests/PostgresBackedProfileRepositoryTests.cs` (new) | Profile / address / contact round-trips against a real PostgreSQL, plus the `22001` propagation test. |

### Gate files

| Path | Change |
|---|---|
| `coverage-full.runsettings` (new, repo root) | Copy of `coverlet.runsettings` with `CompilerGeneratedAttribute` removed from `ExcludeByAttribute` and the `<Threshold>` block removed. |
| `coverage-baseline.json` (new, repo root) | The committed ratchet baseline. |
| `.gitignore` | **Mandatory.** Line 590 is `coverage*.json`, which already matches `coverage-baseline.json`. Add a negation so the baseline is actually tracked. |
| `scripts/check-coverage-ratchet.py` (new) | The step-7 comparison. |
| `scripts/verify-all.sh` | Renumber banners to `N/7`; add step 7; narrow step 4's report glob (see Constraints). |
| `.forgejo/workflows/build-and-deploy.yml` | Add the two step-7 commands after the "Verify XML Docblock Completeness" step and before "Set Lowercase Image Name". |
| `AGENTS.md` **§3 only** | Add step 7 to the numbered list under "Run Full Verification Gate" and change the heading count. **Do not touch any of the 14 Directives** — Directive 10's "6 verification steps" and Directive 5's wording are a user decision recorded at the end of this file. |

### Read-only context (do not edit in this package)

`src/NanoWeaverHrt/Database/Migrations/DatabaseMigrationRunner.cs` (the `ApplyPendingMigrationsAsync(DbConnection, string dialect, ct)` contract you call),
`src/NanoWeaverHrt/Database/Migrations/Migration003_UserProfileAddressesAndContacts.cs` (the narrow Postgres column widths),
`src/NanoWeaverHrt/Configuration/SystemConfigurationStore.cs` (the single startup backend probe at `:144-166`),
`src/NanoWeaverHrt/Security/Authentication/UserRepository.cs`, `src/NanoWeaverHrt/Storage/PatientDnaRepository.cs` (P6 converts these; leave them alone).

---

## Interfaces and signatures

Namespace for everything new: `NanoWeaverHrt.Database.Access`.

```csharp
public enum WeaverDatabaseBackend
{
    Sqlite = 0,
    PostgreSql = 1
}
```

```csharp
public interface ISqlDialect
{
    /// Exactly "postgresql" or "sqlite" — the strings DatabaseMigrationRunner already expects.
    string Name { get; }
    WeaverDatabaseBackend Backend { get; }

    /// SQL text fragments. The ONLY textual difference across the four repositories is the
    /// boolean literal: `is_active = TRUE` (Postgres) vs `is_active = 1` (SQLite).
    string TrueLiteral { get; }
    string FalseLiteral { get; }

    /// Parameter-value coercions. Postgres takes native bool/DateTime; SQLite takes 0/1 and ISO-8601 "o".
    object BooleanValue(bool value);
    object TimestampValue(DateTimeOffset value);
    object TimestampValueOrNull(DateTimeOffset? value);   // DBNull.Value when null
}

public sealed class SqliteDialect : ISqlDialect
{
    public static ISqlDialect Instance { get; }            // stateless singleton
}

public sealed class PostgreSqlDialect : ISqlDialect
{
    public static ISqlDialect Instance { get; }
}
```

`ISqlDialect` has exactly these seven members. Adding a method that takes or returns SQL for a
*statement* (an upsert builder, a quoted-identifier helper, a `WHERE`-clause composer) is out of
bounds — see Constraints.

```csharp
public sealed class WeaverDbSession : IAsyncDisposable
{
    internal WeaverDbSession(DbConnection connection, ISqlDialect dialect);
    public DbConnection Connection { get; }
    public ISqlDialect Dialect { get; }
    public DbCommand CreateCommand(string sql);            // ArgumentException on null/whitespace sql
    public ValueTask DisposeAsync();                       // disposes the connection; idempotent
}
```

```csharp
public interface IWeaverDbConnectionFactory
{
    /// The backend the next OpenAsync will target. Pure read of the decision already made at startup.
    WeaverDatabaseBackend ActiveBackend { get; }

    /// Opens a connection to the active backend and applies pending migrations for its dialect.
    /// Throws WeaverDatabaseUnavailableException when the active backend cannot be reached.
    Task<WeaverDbSession> OpenAsync(CancellationToken ct = default);
}

public sealed class WeaverDbConnectionFactory : IWeaverDbConnectionFactory
{
    public WeaverDbConnectionFactory(
        IConfiguration configuration,
        IConfigurationRepository configurationRepository,
        DatabaseMigrationRunner migrationRunner);          // all three required; ArgumentNullException on null

    /// Internal so the pure decision can be unit-tested without a server.
    internal static WeaverDatabaseBackend ResolveBackend(string activeBackendName);
}
```

```csharp
public sealed class WeaverDatabaseUnavailableException : Exception
{
    public WeaverDatabaseUnavailableException(WeaverDatabaseBackend backend, Exception innerException);
    public WeaverDatabaseBackend Backend { get; }
}
```

```csharp
public static class WeaverDbParameters
{
    public static DbParameter Add(DbCommand command, string name, object value);
    public static DbParameter AddNullable(DbCommand command, string name, object? value);  // DBNull.Value when null
}

public static class WeaverDbValues
{
    public static bool ReadBoolean(DbDataReader reader, int ordinal);
    public static DateTimeOffset ReadTimestamp(DbDataReader reader, int ordinal);
    public static DateTimeOffset? ReadNullableTimestamp(DbDataReader reader, int ordinal);
}
```

`ReadBoolean` and `ReadTimestamp` must preserve today's logic byte for byte: dispatch on
`reader.GetFieldType(ordinal)`, `typeof(bool)` → `GetBoolean`, otherwise `GetInt64(...) != 0`;
`typeof(DateTime)` → `new DateTimeOffset(GetDateTime(ordinal), TimeSpan.Zero)`, otherwise
`DateTimeOffset.Parse(GetString(ordinal))`.

### Factory behaviour, exactly

```
OpenAsync(ct):
  backend = ResolveBackend(_configurationRepository.ActiveBackendName)
  if backend == PostgreSql:
      conn = new NpgsqlConnection(_postgresConnectionString)
      try { await conn.OpenAsync(ct);
            await _migrationRunner.ApplyPendingMigrationsAsync(conn, "postgresql", ct); }
      catch (Exception ex) when (ex is not OperationCanceledException)
          { await conn.DisposeAsync();
            throw new WeaverDatabaseUnavailableException(WeaverDatabaseBackend.PostgreSql, ex); }
      return new WeaverDbSession(conn, PostgreSqlDialect.Instance)
  else:
      conn = new SqliteConnection($"Data Source={_sqlitePath}")
      try { await conn.OpenAsync(ct);
            await _migrationRunner.ApplyPendingMigrationsAsync(conn, "sqlite", ct); }
      catch (Exception ex) when (ex is not OperationCanceledException)
          { await conn.DisposeAsync();
            throw new WeaverDatabaseUnavailableException(WeaverDatabaseBackend.Sqlite, ex); }
      return new WeaverDbSession(conn, SqliteDialect.Instance)
```

- `ResolveBackend(name)` is `name.StartsWith("PostgreSQL", StringComparison.OrdinalIgnoreCase) ? PostgreSql : Sqlite`. Null name → `Sqlite`.
- `_postgresConnectionString` and `_sqlitePath` are computed in the constructor by **moving**
  the existing block from `UserProfileRepository` (`:67-81`) verbatim, including its
  WHAT/WHY/HOW comment about not shipping a default credential.
- Migrations are applied **on every open**, exactly as `UserProfileRepository` does today. This is
  deliberately not optimised here; see Non-goals.
- There is **no retry, no probe and no fallback** inside the factory. The backend decision is made
  once, at startup, by `SystemConfigurationStore.InitializeAsync`, which probes PostgreSQL and
  records the outcome in `ActiveBackendName`. The factory only reads that decision.

### `UserProfileRepository` after the change

```csharp
public UserProfileRepository(IWeaverDbConnectionFactory connectionFactory, ProfileDataProtector protector);
```

- Keeps `_lock` (`SemaphoreSlim(1,1)`) and keeps acquiring it in every method, unchanged.
- `Dispose()` disposes `_lock` only. It must **not** dispose the factory or the migration runner —
  both are DI singletons shared with other consumers.
- Every method body becomes:
  `await using WeaverDbSession session = await _connectionFactory.OpenAsync(ct);` then
  `await using DbCommand cmd = session.CreateCommand(sql);`.
- The dialect-dependent parameter blocks replace `if (dialect == "postgresql") { ... } else { ... }`
  with single calls: `WeaverDbParameters.Add(cmd, "@ip", session.Dialect.BooleanValue(address.IsPrimary))`,
  `... session.Dialect.TimestampValue(address.CreatedAtUtc)`, `... session.Dialect.TimestampValue(DateTimeOffset.UtcNow)`.
  Three such blocks exist: `UpsertProfileAsync` (`:221-230`), `UpsertAddressAsync` (`:329-340`),
  `UpsertContactAsync` (`:467-478`). All three lose their `if`/`else`.
- SQL text is **unchanged**, including the `ON CONFLICT (...) DO UPDATE` clauses, which are valid on
  both engines. Do not rewrite a single query.
- `MapProfileFromReader`, `MapAddressFromReader`, `MapContactFromReader` keep their signatures and
  `internal static` accessibility; their bodies swap the inline type checks for `WeaverDbValues`.

### The rewritten fallback test (name it so review can find it)

Replace `UserProfileRepository_ConstructorAndPostgresFallback_CoverBranches` with
`UserProfileRepository_PostgresUnreachable_ThrowsInsteadOfWritingToSqlite`:

- Build an `IConfiguration` with `TRAJECTORY_INCOMING_DIR` = a fresh temp directory and
  `POSTGRES_CONNECTION_STRING` = `Host=127.0.0.1;Port=54399;Database=nonexistent;Timeout=1`.
- Use the existing `MockPostgresConfigRepo` (test-local `IConfigurationRepository` returning
  `"PostgreSQL (pg-duckdb)"`) — mocking **our own interface** is permitted.
- Assert `await Assert.ThrowsAsync<WeaverDatabaseUnavailableException>(() => repo.GetProfileAsync("anyone"))`.
- Assert the thrown exception's `Message` contains neither `"Host="` nor `"Password"` nor the temp path.
- Assert `File.Exists(Path.Combine(tempDir, "weaver_config.db")) == false` — nothing was written to
  the wrong database. **This assertion is the point of the test; do not drop it.**
- Keep the constructor `ArgumentNullException` coverage, retargeted at the new two-parameter
  signature plus the factory's three-parameter one.

---

## Gate step 7

### `coverage-full.runsettings`

Byte-identical to `coverlet.runsettings` except:
- `<ExcludeByAttribute>Obsolete,GeneratedCodeAttribute</ExcludeByAttribute>` (drop
  `CompilerGeneratedAttribute`; keep the other two).
- Remove `<Threshold>`, `<ThresholdType>`, `<ThresholdStat>` — the ratchet script, not coverlet,
  decides pass/fail here.
- `<Include>`, `<Exclude>` and `<ExcludeByFile>` are **unchanged**. `.razor` and
  `NanoWeaverHrt.Components.*` stay excluded (P17 owns those).
- A leading XML comment stating that this file exists only for step 7, that it is the honest
  measurement, and that it must be kept in sync with `coverlet.runsettings` whenever the primary
  include/exclude lists change.

### `coverage-baseline.json`

```json
{
  "schema_version": 1,
  "seeded_from_commit": "<output of: git rev-parse HEAD>",
  "seeded_on_utc": "<ISO-8601 date of the seeding run>",
  "measurement": {
    "runsettings": "coverage-full.runsettings",
    "weaver_test_postgres": "unset"
  },
  "line_rate": 0.0,
  "branch_rate": 0.0,
  "uncovered_lines_total": 0,
  "classes": {
    "NanoWeaverHrt.Program": 0,
    "NanoWeaverHrt.Security.Authentication.UserRepository": 0
  }
}
```

- `line_rate` / `branch_rate`: percentages rounded to exactly 2 decimal places (e.g. `96.59`).
- `uncovered_lines_total`: integer; every `<line hits="0">` in the report.
- `classes`: one entry per Cobertura `<class name=...>` that has at least one uncovered line, value
  = that class's uncovered line count. Classes with zero uncovered lines are **omitted**.
  Where Cobertura emits several `<class>` elements with the same `name` (partial classes, nested
  display classes), sum them into one entry.
- **Seed it from a measured run on your rebased tree**, with `WEAVER_TEST_POSTGRES` unset. Do not
  copy any number from the plan file or from this package; the rebase changes them.

### `scripts/check-coverage-ratchet.py`

Invocation: `python3 -I scripts/check-coverage-ratchet.py artifacts/coverage-full coverage-baseline.json [--update]`.

Behaviour:

1. Find `**/coverage.cobertura.xml` under the given directory only. **No report → print an error and
   exit 1.** Never pass by finding nothing.
2. More than one report → use them all, summing per class (coverlet writes one per test assembly;
   today there is one, and a silent single-file assumption would break later).
3. Compute `line_rate`, `branch_rate` (2 dp), `uncovered_lines_total`, and the per-class map, by the
   same definitions as above.
4. Compare against the baseline and classify every difference:
   - `REGRESSED` — a rate fell, `uncovered_lines_total` rose, a class's count rose, or a class not
     in the baseline now has uncovered lines.
   - `IMPROVED` — a rate rose, the total fell, a class's count fell, or a baseline class vanished.
5. Exit codes: `0` only when the measurement matches the baseline **exactly**. `1` on any
   `REGRESSED` difference, printing each one as `class: baseline N -> measured M`. `1` on a
   purely `IMPROVED` difference too, with the message
   `Baseline is stale. Re-run with --update and commit the improved numbers in this same diff.`
6. `--update` rewrites the baseline from the measurement, preserving `schema_version`, and prints
   an old→new summary for the report. `--update` **refuses** (exit 1) when any difference is
   `REGRESSED`, unless `--allow-regression` is also passed. **Do not pass `--allow-regression` in
   this package, in `verify-all.sh`, or in CI** — it exists so a future user-approved exception is
   explicit and greppable.
7. Deterministic output ordering (sort class names) so two runs diff cleanly.
8. Pure standard library, no third-party imports, run under `python3 -I` like the existing
   `check-docblocks.py`.

### `scripts/verify-all.sh`

- Renumber every banner from `N/6` to `N/7`.
- Step 4's inline Python uses `glob('**/coverage.cobertura.xml', recursive=True)` and takes the
  newest file. Step 7 writes a second report into the tree. Add a filter so step 4 can never
  evaluate step 7's un-excluded report: drop any path containing `artifacts/` from the glob result
  before `max(...)`, and keep step 4's error-on-empty behaviour.
- New step 7, after step 6:

```bash
echo ">> Step 7/7: Enforcing the un-excluded coverage ratchet..."
rm -rf artifacts/coverage-full
# WHY: the ratchet must be a machine-independent number. With WEAVER_TEST_POSTGRES set, the
#      Postgres-only paths become covered and the measurement improves; with it unset they do
#      not. Comparing a baseline across both configurations would make step 7 flap. Step 7
#      therefore always measures the Postgres-absent configuration.
env -u WEAVER_TEST_POSTGRES XDG_RUNTIME_DIR=/tmp dotnet test NanoWeaverHrt.slnx \
    --configuration Release --no-build \
    --settings coverage-full.runsettings \
    --collect:"XPlat Code Coverage" \
    --results-directory artifacts/coverage-full \
    --verbosity minimal
python3 -I scripts/check-coverage-ratchet.py artifacts/coverage-full coverage-baseline.json
```

- `--no-build` is correct because step 3 already built `Release`. If that proves not to hold on your
  machine, drop `--no-build` rather than reordering the steps, and say so in the report.
- Also add one line immediately before step 3 reporting the Postgres mode, so a human reading the
  gate output can tell whether the Postgres assertions ran:
  `echo "WEAVER_TEST_POSTGRES: ${WEAVER_TEST_POSTGRES:+set (PostgreSQL-backed tests WILL run)}${WEAVER_TEST_POSTGRES:-unset (PostgreSQL-backed tests will be skipped)}"`

### `.forgejo/workflows/build-and-deploy.yml`

One new step named `Enforce Un-Excluded Coverage Ratchet`, placed after
`Verify XML Docblock Completeness` and before `Set Lowercase Image Name...`, running the same two
commands (without `--no-build`, since the workflow's test step and this one are separate
invocations and the runner's state is not ours to assume). Do not add a PostgreSQL service,
container or environment variable to the workflow in this package — see Constraints.

### `.gitignore`

`.gitignore:590` is `coverage*.json`. Without a negation, `coverage-baseline.json` is silently
untracked and the ratchet is a local-only file that review never sees. Add, directly after the
Coverlet block:

```gitignore
# The step-7 ratchet baseline is deliberately tracked: the rule above would otherwise hide it.
!/coverage-baseline.json
```

`artifacts/` is already ignored at line 56, so step 7's report directory needs no change.

---

## Constraints

1. **Mocking a database driver is prohibited.** Not with Moq, not with a hand-written subclass, not
   behind a wrapper interface. This covers `DbConnection`, `DbCommand`, `DbTransaction`,
   `DbParameter`, `NpgsqlConnection`, `SqliteConnection` and every member of those hierarchies.
   `IWeaverDbConnectionFactory` is satisfied **only** by `WeaverDbConnectionFactory`, in production
   and in tests alike; there must be no `FakeWeaverDbConnectionFactory`, `InMemoryDbSession` or
   equivalent anywhere in the diff. A test that asserts against a faked driver tests the fake.
   - **Permitted, and pre-existing:** a `DbDataReader` obtained from
     `System.Data.DataTable.CreateDataReader()` (see `AuthenticationServiceTests.cs:840-856`) or the
     existing hand-written `MockDateTimeDataReader` (`PatientDnaAndImportTests.cs:~580-618`), used
     **only** as table-driven input to a pure static mapping function (`MapProfileFromReader`,
     `WeaverDbValues.*`). Those are real in-memory readers, they execute no SQL, and they are the
     only way to feed PostgreSQL's native `DateTime`/`bool` column types to a mapper without a
     server. They must never be handed to repository code that would otherwise run a query.
   - **Permitted:** Moq or a hand-written stub for **our own** interfaces, e.g.
     `IConfigurationRepository`. That is a configuration value, not a database.
2. **This is not a micro-ORM.** The seam removes duplicated plumbing; it does not hide SQL. Every
   `SELECT`, `INSERT` and `DELETE` stays written out in the repository, in full, as a string
   literal. Do not add an entity mapper, a query builder, a `Repository<T>` base class, a
   statement cache, or an `ISqlDialect` member that composes a clause. If you find yourself
   generating SQL, stop and report instead.
3. **The per-call fallback is deleted, not relocated.** After this package, grep must find no bare
   `catch` or `catch { }` in `UserProfileRepository.cs`, and no path in which a PostgreSQL failure
   results in a SQLite write. The one `catch` permitted in the new factory re-throws
   `WeaverDatabaseUnavailableException`.
4. **Directive 14.** `WeaverDatabaseUnavailableException.Message` must name only the backend
   (`"PostgreSql"` / `"Sqlite"`). No connection string, no host, no password, no file path, no SQL.
   The driver exception goes in `InnerException`, which `SecureExceptionHandlerMiddleware` must not
   surface; assert that with a test.
5. **Directive 13.** No DDL in this package. All schema work remains in `IDatabaseMigration`
   implementations run by `DatabaseMigrationRunner`. The factory calls
   `ApplyPendingMigrationsAsync`; it does not execute `CREATE`.
6. **Directive 2 + gate step 6.** Every new type, member and field — private included — carries a
   `<summary>`, one `<param>` per parameter and a `<returns>` where there is a return value.
   Directive 3: WHAT/WHY/HOW on the factory's open path, the exception-to-dispose sequence and the
   fixture's database lifecycle.
7. **Directive 6 / the test-isolation memory rule.** No sleeps, no polling loops, no dependence on
   ambient state. The SQLite side of every test uses a per-test directory under
   `Path.GetTempPath()`; **nothing** in this diff may read or write `simulations/`,
   `simulations/shared-data/` or the repository working tree. Every temp directory and every
   created PostgreSQL database is removed in teardown.
8. **Step 4 must stay at 100.00% with `WEAVER_TEST_POSTGRES` unset.** Consequence to design for:
   every new *synchronous* member must be reachable without a server. The dialects, `ResolveBackend`,
   `WeaverDbParameters`, `WeaverDbValues`, `WeaverDbSession` and the exception type are all pure or
   SQLite-reachable and get direct unit tests. The only new code permitted to require a live
   PostgreSQL is inside the `async` body of `OpenAsync`, whose state machine the primary
   runsettings excludes — those lines land in the step-7 baseline instead, honestly.
9. **Do not rely on a runtime skip API, and do not add a package to get one.** The test project
   pins `xunit` 2.9.2 and `xunit.runner.visualstudio` 2.8.2
   (`tests/NanoWeaverHrt.Tests/NanoWeaverHrt.Tests.csproj:13-17`). `Assert.Skip` is an xUnit v3
   API; whether it resolves against 2.9.2's assert assembly, and whether this runner would report
   it as *Skipped* rather than *Failed*, was **not verified** by the architect — and a wrong guess
   there costs a whole coder pass. Use the guard shape mandated below, which works unconditionally
   and is deterministic. **Do not add `Xunit.SkippableFact` or any other package, and do not
   upgrade xUnit** — both are out of scope and would touch `NuGetAudit` and the zero-warnings
   requirement. Test bodies are outside the measured coverage scope (`coverlet.runsettings`
   includes only `[NanoWeaverHrt]*` and excludes `[NanoWeaverHrt.Tests]*`), so the guard branch
   costs no coverage.
10. **"Available but broken" must be red, never quiet.** When `WEAVER_TEST_POSTGRES` is set and the
    server cannot be reached or the database cannot be created, the fixture throws and the tests
    **fail**. Silence is only acceptable when the variable is absent.
11. Directive 7: `dotnet format NanoWeaverHrt.slnx --verify-no-changes` clean. Directive 6: zero
    warnings.
12. **Do not touch** `coverlet.runsettings`, any of the 14 Directives in `AGENTS.md`, `.claude`
    configuration, `UserRepository.cs`, `SystemConfigurationStore.cs`, `PatientDnaRepository.cs`,
    any `Migration0*.cs`, or any file not listed in "Files in scope".
13. Do not commit and do not push. The parent session reviews the worktree diff and commits.

### The mandated Postgres fixture shape

```csharp
[CollectionDefinition(PostgresTestEnvironment.CollectionName)]
public sealed class PostgresTestCollection : ICollectionFixture<PostgresTestEnvironment> { }

public sealed class PostgresTestEnvironment : IAsyncLifetime
{
    public const string CollectionName = "postgres";

    /// True when WEAVER_TEST_POSTGRES holds a non-empty connection string.
    public bool IsAvailable { get; }

    /// Creates a fresh, uniquely named database and returns a connection string pointing at it.
    /// Throws when IsAvailable is false, or when the server rejects the request.
    public Task<string> CreateIsolatedDatabaseAsync(CancellationToken ct = default);

    /// Drops every database this environment created. Called by InitializeAsync/DisposeAsync.
    public Task DisposeAsync();
}
```

- `WEAVER_TEST_POSTGRES` holds a full Npgsql connection string, e.g.
  `Host=127.0.0.1;Port=55432;Username=postgres;Password=...;Database=postgres`. Absent or empty →
  `IsAvailable == false`.
- `CreateIsolatedDatabaseAsync` connects to the database named in that string (the maintenance
  database), runs `CREATE DATABASE weaver_test_<Guid:N>`, then returns the same connection string
  with `Database=` swapped via `NpgsqlConnectionStringBuilder`. A fresh database per test, not a
  schema: `Migration001`–`Migration004` use unqualified table names, and a per-test database
  guarantees the migration chain runs from zero with no `search_path` games.
- Teardown drops each created database with `DROP DATABASE IF EXISTS "<name>" WITH (FORCE)`
  (PostgreSQL 13+). A leftover database must not be able to make a later run pass for the wrong
  reason.
- The guard in every Postgres-backed test, and nowhere else:

```csharp
if (!_postgres.IsAvailable)
{
    // WHAT: Leaves the test body unexecuted when no PostgreSQL server was supplied.
    // WHY:  Directive 6 forbids depending on ambient network state, and this project's xUnit pin
    //       offers no runtime-skip API we have verified. The gate prints the WEAVER_TEST_POSTGRES
    //       mode so a vacuous pass is visible rather than silent, and a variable that IS set but
    //       unreachable fails loudly instead of quietly skipping.
    // HOW:  Reports the reason through ITestOutputHelper and returns before touching the network.
    _output.WriteLine("SKIPPED: WEAVER_TEST_POSTGRES is not set.");
    return;
}
```

---

## Acceptance criteria

Each is checkable by a reviewer running one command or reading one named test.

**Seam and loudness**

1. `grep -n "catch" src/NanoWeaverHrt/Security/Authentication/UserProfileRepository.cs` returns
   only the existing corrupted-pronouns-JSON `catch` in `MapProfileFromReader`; the connection
   fallback `catch` is gone.
2. `grep -rn "NpgsqlConnection\|SqliteConnection" src/NanoWeaverHrt/Security/Authentication/UserProfileRepository.cs`
   returns nothing. The repository no longer names a driver type.
3. `UserProfileRepository_PostgresUnreachable_ThrowsInsteadOfWritingToSqlite` passes, including the
   assertion that no `weaver_config.db` was created.
4. A test asserts that an HTTP request which triggers `WeaverDatabaseUnavailableException` returns
   an RFC 7807 `SecureProblemDetails` body containing no `Host=`, no `Password`, no `/home/`, no
   `/tmp/`, and no SQL keyword.
5. Every profile, address and contact round-trip that passed before this package still passes
   against SQLite, unmodified in intent. If any existing assertion had to change, the report names
   it and says why.
6. `grep -rn "Mock<Db\|Mock<Npgsql\|Mock<Sqlite\|: DbConnection\|: DbCommand" tests/` returns
   nothing. (`: DbDataReader` may still match the pre-existing `MockDateTimeDataReader`; that one is
   permitted by Constraint 1 and must remain confined to pure mapper input.)

**Dialect and helpers**

7. Direct unit tests cover all seven `ISqlDialect` members on both implementations, including
   `TimestampValueOrNull(null)` returning `DBNull.Value` on both.
8. `WeaverDbValues.ReadBoolean` and `ReadTimestamp` are each covered on both the native-type branch
   and the string/integer branch, using a `DataTable`-backed reader.
9. `WeaverDbConnectionFactory.ResolveBackend` is covered for `"PostgreSQL (pg-duckdb)"`,
   `"postgresql"`, `"Local SQLite Fallback"`, `""` and `null`.
10. `WeaverDbSession.DisposeAsync` is asserted idempotent (two calls, no throw), and
    `CreateCommand("")` throws `ArgumentException`.

**PostgreSQL fixture**

11. With `WEAVER_TEST_POSTGRES` set against a local container
    (`docker run -d --rm -p 127.0.0.1:55432:5432 -e POSTGRES_PASSWORD=... postgres:16`):
    profile, address and contact round-trips all pass against real PostgreSQL, and the report
    quotes the test count that ran.
12. **The trap test passes:** with PostgreSQL available, `UpsertAddressAsync` with a 64-character
    `PostalCode` (the Postgres column is `VARCHAR(32)`) throws out of the repository — assert the
    `PostgresException.SqlState == "22001"` reaches the caller — and the temp-directory SQLite file
    still does not exist. This is the assertion that proves P5 can no longer be silently wrong.
13. With `WEAVER_TEST_POSTGRES` unset, `dotnet test` runs with the same pass count as before the
    package plus the new SQLite-side and unit tests, zero failures, and the Postgres-backed tests
    print their skip line.
14. With `WEAVER_TEST_POSTGRES` set to an unreachable endpoint, the Postgres-backed tests **fail**
    (verified once by the coder and quoted in the report), not skip.
15. After a full run, `psql -c "\l"` on the test server shows no `weaver_test_*` database left
    behind; and no file was created under `simulations/`.

**Gate step 7**

16. `./scripts/verify-all.sh` prints `Step 7/7` and exits 0. Step 4 still reports exactly
    `100.00%` line and `100.00%` branch.
17. **Step 7 is proven to bite.** Temporarily add a private static method that nothing calls to
    `src/NanoWeaverHrt/Database/Access/SqliteDialect.cs`, re-run step 7, and confirm it exits 1
    naming that class with `baseline N -> measured M`. Revert, re-run, confirm green. **The report
    must quote the actual failing output.** Do not declare step 7 done without this — step 6 was
    verified this way and it mattered.
18. **Step 7 fails closed.** `rm -rf artifacts/coverage-full && python3 -I
    scripts/check-coverage-ratchet.py artifacts/coverage-full coverage-baseline.json` exits 1 with a
    "no coverage report found" message rather than passing on an empty set.
19. **Step 7 rejects a stale baseline in the improving direction too.** Hand-edit one class's count
    in `coverage-baseline.json` upward by 1, re-run, confirm exit 1 with the
    `Baseline is stale. Re-run with --update ...` message. Restore the file.
20. `git check-ignore -v coverage-baseline.json` exits non-zero (the file is **not** ignored) and
    `git status --porcelain` lists it as added. Without this, the baseline never reaches review.
21. The baseline's `seeded_from_commit` equals the `HEAD` of your rebased worktree, and
    `measurement.weaver_test_postgres` is `"unset"`.
22. `.forgejo/workflows/build-and-deploy.yml` contains the step-7 commands between the docblock step
    and the image-name step, and the workflow still has no `services:` block and no PostgreSQL
    environment variable.
23. `AGENTS.md` §3 lists seven checks; `git diff AGENTS.md` shows **no** change inside §1 Prime
    Directives.
24. Gate green end to end: 7/7, 0 warnings, 0 incomplete docblocks, `dotnet format
    --verify-no-changes` clean.

---

## Non-goals

- **Do not convert `UserRepository`, `SystemConfigurationStore` or `PatientDnaRepository`.** They
  are P6. Touching them here makes the diff unreviewable and collides with P6's rebase.
- **Do not add a backend probe to the factory.** The single probe stays in
  `SystemConfigurationStore.InitializeAsync` for now; P6 moves it into the factory and makes
  `ActiveBackendName` delegate. Two probes would be a second source of truth, which is the bug class
  this package exists to remove.
- **Do not change the startup fallback.** "No connection string configured → use SQLite" and
  "configured PostgreSQL unreachable at startup → use SQLite with a recorded backend name" both
  stay as they are. Adding the warning log and distinguishing those two cases is P6's.
- **Do not optimise migration application.** Applying pending migrations on every open is today's
  behaviour; caching it would break tests that delete the SQLite file between calls. A later
  package may revisit it.
- **No PHI encryption.** Not one column. That is P5, and it must land after this.
- **No new migration.** P5 takes the next free number; adding one here would shift it.
- **Do not flip `CompilerGeneratedAttribute` off in `coverlet.runsettings`.** Step 4 keeps measuring
  what it measures; step 7 is the honest second number. Both stay.
- **Do not widen any column, and do not "fix" the `VARCHAR` widths.** P5 owns that, and criterion 12
  depends on the narrow widths still being there.
- **No bUnit, no `.razor` coverage.** That is P17.
- **Do not raise the baseline to make something pass.** The baseline moves down or stays. If your
  change would raise it, the change is wrong.

---

## USER DECISION — not for any agent to apply

Step 7 makes visible a statement in `AGENTS.md` that is false in fact today. Do not edit §1 Prime
Directives. Record the following in the P4 report for the user to accept, amend or reject.

**Directive 5, first bullet. Current text:**

> - Zero uncovered lines and zero uncovered branches are allowed across the entire solution.

**Proposed replacement (two bullets in its place):**

> - Gate step 4 enforces zero uncovered lines and zero uncovered branches across the measured scope
>   defined by `coverlet.runsettings`, which excludes compiler-generated code (minimal-API endpoint
>   lambdas and async state machines), `.razor` components and generated code.
> - Gate step 7 measures the same solution with compiler-generated code included and enforces the
>   committed `coverage-baseline.json`: no rate may fall, no class's uncovered-line count may rise,
>   and the baseline may only ever be revised downward — until it reaches 100.00%, at which point
>   the exclusion, the second runsettings file and the baseline are deleted and step 4 alone
>   enforces the real number.

**Directive 10, second bullet.** It reads "Never push or finalize if any of the 6 verification steps
fail." With step 7 added this becomes 7. The count is a factual number inside a Directive, so this
package changes §3 only and leaves Directive 10 to the user in the same decision.

**Why the wording matters.** With `CompilerGeneratedAttribute` excluded, the gate reports 100.00% of
9,322 lines while 11,050 of the solution's 20,372 are outside the measurement — which is how
`POST /api/users`, administrator account provisioning, sat at 0.00% unnoticed. Step 7 turns that gap
into a visible, shrinking number. The Directive should describe the gate that exists.
