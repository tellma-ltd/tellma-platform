# PC-F56: LocalDB assembly cleanup failure after PC-068 (integration-windows)

## Implementer notes

### Diagnosis

- **The failure.** In CI run 44110298098 (1e9332ad), the integration-windows Core suite passed
  (395 passed, 3 skipped). Then `[Test Assembly Cleanup Failure] Xunit.Sdk.TestPipelineException`
  appeared 64 s after the last test output (15:15:12 → 15:16:16). The job log carries only the
  exception type. Its message and stack are not printed at the console logger's default verbosity.
- **The source.** `SqlServerFixture` is the Core suite's only assembly fixture, so the failure is
  its `DisposeAsync`. On an existing server, that method ran one loop on one `master` connection:
  `ALTER DATABASE … SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE …` for each database,
  under SqlClient's 30 s default command timeout. The first error stopped the loop and left every
  later database on the server. This code dates from PC-034 and PC-F47, not PC-068.
- **Intermittent, and not tied to PC-068's code paths.**
  - Run 44113932638 (05467a2f) tested identical code: its diff from 1e9332ad is only
    `plans/platform-core/LEDGER.md`. Its integration-windows job was green.
  - The full Core suite on local LocalDB at 1e9332ad was also green, cleanup included
    (395 passed, 3 skipped, 9.5 min).
  - In green runs of the same job, cleanup takes 4 to 5.5 min, measured from the last test output
    to the summary line:

    | Run | Last test output → summary |
    |---|---|
    | 44104115244 | 14:41:28 → 14:45:57 |
    | 44100563804 | 14:22:38 → 14:26:42 |
    | 44100453494 | 14:20:54 → 14:26:02 |
    | 44113932638 | 15:31:49 → 15:37:15 |

    So the red run failed about a quarter of the way through the drops.
  - None of the 40 most recent platform-core runs shows another cleanup failure. The only other
    integration-windows failure among them, run 43972112319, was a test failure
    (VersionTagLockTests).
  - PC-068's `QueryPipelineDatabase` disposes its `ConnectDatabase` the same way the six older
    fixtures dispose theirs. Its two `CreateInstance` providers are `await using`, and nothing it
    adds runs after its class ends. It adds 2 databases to the roughly 200 that get dropped.
- **Why each drop was slow (measured, local LocalDB 15.0, `.NET` file-based bench in the session
  scratchpad).** Rolling back the idle pooled sessions in a database cost its drop about 3 s,
  whatever their number: 3.0 s for 1, 6 and 12 sessions, and 3.8 s for 24. Eight databases took
  24.2 s with the pools kept and 0.46 s with `SqlConnection.ClearAllPools()` first (21 to 104 ms
  each).
  - So nearly all of the old cleanup's minutes went to killing the suite's own pooled sessions,
    one database at a time.
  - Each of those drops waited on server-side session terminations. A drop on a slow runner then
    had little margin under the 30 s default, which is the most likely cause of the red run.
  - The exact SQL error cannot be recovered from the log. It was either a timeout or a transient
    held-database error (3702 in use, 5061 lock).

### What was built

- **`SqlDatabaseDrop`** (`test/shared/Tellma.Testing.Support/Sql/`), the one drop path:
  - `DropAllAsync` calls `SqlConnection.ClearAllPools()` first. It then drops each database on
    one `master` connection. The drop is an `IF DB_ID(…) IS NOT NULL` guard around
    `SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE`, run under
    `SqlDatabaseCreation.CommandTimeout` (5 min).
  - A run that fails only because the database was held for a moment is rerun on
    `CreateDatabaseRetry.Default`'s waits, through its internal seam overload, under the drop's
    own classifier. The transient errors are 3702, 5061, 5064, 924, 952 and a 1205 deadlock,
    alone or with the 5069 that follows. Each rerun clears the pools again first. Retries are
    logged as test output, or as a diagnostic message outside a test.
  - A failed database does not stop the others. If the connection is lost, the remaining
    databases are left untried.
  - At the end, one `AggregateException` names every database left behind (failed or untried),
    with each failure as an inner exception. Nothing is swallowed: a cleanup that leaves databases
    behind still fails the run, but now it reports all of them, not the first.
- `SqlServerFixture.DisposeAsync` routes through it, after removing duplicate names under the
  list's lock. `SqlDatabaseCreation.Quote` is now internal so both helpers can share it.
- **Tests** (`Tellma.Core.Testing.Tests/Sql/`):
  - `SqlDatabaseDropTests` (unit) covers:
    - the statement;
    - the classifier, including the follow-up error alone, a missing database, the model lock,
      a timeout and a mixed real error;
    - the loop: all dropped, a failure in the middle, a lost connection, cancellation.
  - `SqlDatabaseDropServerTests` (Integration) covers a database with an idle pooled session and an
    open-transaction session being dropped, and a reserved-but-never-created database being passed
    over. The tests ran on both Docker and LocalDB.
- **Effect.** The full Core integration suite on local LocalDB took 3.3 min in total, against
  9.5 min before; nearly all of the difference is cleanup.

### Deviations

1. `ClearAllPools` clears every pool in the process. Where the fixture is a collection or class
   fixture (Identity, `Tellma.Core.Testing.Tests`), other collections still running lose their
   idle pooled connections and reopen them. In-use connections are not touched; they are discarded
   when they are returned.
2. A command timeout is not retried, the same as for creations. The 5-minute bound is the remedy;
   a timeout at that bound is a real hang and should surface.
3. The drop reuses `CreateDatabaseRetry`'s loop and waits through the classifier seam, rather than
   adding a second retry policy. The class remarks say so.

### For reviewers

- Is the classifier too broad or too narrow? 5064 and 924 only clear if the session holding the
  database leaves. The pool clear before each rerun covers this process's sessions; a foreign
  session would exhaust the retries and surface.
- When the connection is lost, the remaining databases are reported untried, not retried on a
  new connection. A broken `master` connection at cleanup means the server is going away, and
  reconnecting to each database in turn would multiply connect timeouts.
- The CI logging flags were first left to the orchestrator here. Review 1 (R1-1) brought them into
  this item.

## Review 1

Reviewed fd88ced2 against its parent. Checked by hand:
- Built `Tellma.Core.Testing.Tests` (0 warnings).
- Ran `FullyQualifiedName~SqlDatabaseDrop` on LocalDB: 23/23.
- Ran the Core integration suite on LocalDB twice. Cleanup left nothing behind either time: the
  server's user-database count was 5 before and after.
  - Run 1: 159 failed, 236 passed, 3 skipped. The failures were in fixture initialization
    (`CREATE DATABASE` through `ConnectDatabase.InitializeAsync`), which this commit does not
    touch. The error message was cut from my captured output, so the cause is not known. Other
    slots may have been using the same LocalDB instance.
  - Run 2: 395 passed, 3 skipped, in 4 min 25 s.
- Probed LocalDB 15.0 with sqlcmd. A session inside the database that has set it single-user makes the
  drop statement report 5064 followed by 5069, as the classifier expects. 5064 does not abort the
  batch, so the `DROP` still runs, and a rerun is a harmless no-op through the `DB_ID` guard. On a
  second run the same hold let the `ALTER` and `DROP` both succeed, so this hold is not
  deterministic.

The diagnosis holds up:
- 64 s fits about eleven 3 s drops plus one 30 s timeout, or about twenty drops ending in a held
  error.
- The 5-minute bound and the held-error retry cover both, and nothing new is swallowed.
- The error numbers and messages are right (3702, 5061, 5064, 924, 952, 1205, with the follow-up
  5069).
- `ClearAllPools` leaves in-use connections alone, so it is safe in these places:
  - an assembly fixture's disposal;
  - Identity's collection fixture (the other collections there use no SqlClient);
  - mid-run in `Tellma.Core.Testing.Tests`, where the `model` holder is unpooled.
- Acme's `AcmeMigrator` already calls it mid-suite.
- Every .NET integration suite is in the commit message's list. Every connector `Category=Integration`
  test is `Live=true`.
- XML docs, inline comments, READMEs, LF and no BOM all hold.

### Findings

**R1-1 (major): the new diagnostics cannot be seen in CI, which is where this failure happens.**
- `test/shared/Tellma.Testing.Support/Sql/SqlDatabaseDrop.cs:80` logs retries through
  `SqlDatabaseCreation.WriteToTestOutput` (`SqlDatabaseCreation.cs:93-104`).
- In an assembly or collection fixture's `DisposeAsync`, no test is running, so every retry and
  give-up line goes to `SendDiagnosticMessage`. No suite has an `xunit.runner.json`, and CI passes
  no `xUnit.DiagnosticMessages`, so those lines are dropped.
- The `AggregateException` naming the databases left behind is the fix's other diagnostic. By the
  implementer's own diagnosis, the console logger prints only its type
  (`.github/workflows/ci.yml:137-138` run `dotnet test` at default verbosity).
- Scenario: the next red integration-windows cleanup looks exactly like 44110298098, a bare
  `TestPipelineException`. A green cleanup that only got through by retrying leaves no trace either.
  So neither the timeout hypothesis nor the held-error one can ever be confirmed or ruled out.
- The notes leave this to the orchestrator. But this item exists for this failure, and without the
  change the fix's reporting gains never reach anyone.
- Fix (`.github/` is a shared trigger, so this widens the gate to both sides):
  - in this item, run the integration-windows `dotnet test` steps (and ideally the Linux
    `integration` step) with `--logger "console;verbosity=normal"` and
    `-- xUnit.DiagnosticMessages=true`;
  - or have the fixture-disposal path also write its retry lines and the final message to
    `Console.Error`;
  - then confirm on one CI run that a cleanup line actually shows up in the job log.
- If the orchestrator keeps it out of scope, it should open a follow-up fix item rather than leave
  it as a note.

**R1-2 (major): no test fails if the retry wiring or the pool clearing breaks.**
- `DropAsync` (`SqlDatabaseDrop.cs:203-227`) is private and has no seam. The server tests
  (`SqlDatabaseDropServerTests.cs:29-74`) pass with any of these mutations:
  - deleting both `SqlConnection.ClearAllPools()` calls (lines 72 and 212), since
    `ROLLBACK IMMEDIATE` still kills the idle pooled session, only more slowly;
  - passing `CreateDatabaseRetry.IsTransient` (the `model`-lock classifier) instead of
    `IsTransient` at line 223;
  - dropping the retry entirely and running the command once;
  - reverting to the 30 s default command timeout.
- The unit tests prove the classifier's table and the aggregation loop, but not that the drop path
  uses either. The two resilience mechanisms this item adds therefore have no regression guard.
- Fix: give the per-database drop an internal seam, for example:

  ```
  DropAllAsync(master, databases, retry, log, execute, clearPools)
  ```

  where `execute` runs the statement and `clearPools` defaults to `SqlConnection.ClearAllPools`.
  Then pin:
  - (a) an `execute` that first throws a held-database `SqlException` (3702, or 5064 with 5069) and
    then succeeds. Assert one retry line in `log`, `clearPools` called before the rerun, and
    success.
    - The exception can be built from SqlClient's `SqlError`/`SqlException` factories through
      reflection, or captured from a real server.
    - If captured, pick a hold that is shown to be deterministic first. My LocalDB probes were not:
      a plain `DROP DATABASE` beside an unpooled session in `WAITFOR` succeeded twice, and the
      single-user hold gave 5064 only once in two runs.
  - (b) the same with a non-held error (for example 3701), which gives no rerun.
  - (c) `clearPools` called once before the first drop.
- Also assert in `A_database_that_sessions_are_still_in_is_dropped` that the command a drop runs
  carries `SqlDatabaseCreation.CommandTimeout`, or cover the timeout through the same seam.

**R1-3 (minor): a failure to open the `master` connection escapes without naming any database.**
- `SqlDatabaseDrop.cs:77` lets `OpenAsync` throw a raw `SqlException`. That breaks the contract in
  the `<exception>` doc at lines 54-58 ("The message names each database left behind").
- `ClearAllPools()` at line 72 has just discarded every idle pooled connection, so this open is
  always a fresh login. On LocalDB, fresh logins are the step `ci.yml:119-122` already warns can
  fail transiently.
- Scenario: a login hiccup at cleanup fails the run with a connection error, and all ~200
  databases stay on the server unnamed.
- Fix: catch the open failure and throw the same `AggregateException` with every database as not
  tried and the open failure as the inner exception, for example by passing a `connected` that
  reports false and seeding `failures`. Optionally retry the open once.

**R1-4 (minor): the old drop pattern survives in `IdentityServerFixture`, and the new class's
summary says it is the only one.**
- `test/apps/Tellma.Identity.E2E/Infrastructure/IdentityServerFixture.cs:495-504` still drops its
  database with no pool clear, the 30 s default timeout and no retry, using an unescaped
  `DB_ID('…')` with no `N` prefix.
- Meanwhile `SqlDatabaseDrop.cs:13` calls itself "The one way a fixture drops the databases a suite
  created on an existing server".
- Impact is small: one database, E2E only, and only under `TELLMA_TEST_SQL`. But the claim is false,
  and the fixture keeps the slow path (the stopped app's pooled sessions) that this item measured.
- Fix: either route it through `SqlDatabaseDrop.DropAllAsync`, which needs a `ProjectReference` to
  `Tellma.Testing.Support` (the project already references Testcontainers.MsSql and xunit.v3), or
  narrow the summary to the fixtures in `Tellma.Testing.Support`.

**R1-5 (minor): `Tellma.Core.Testing.Tests` now starts a second SQL Server container in Linux CI.**
- `SqlDatabaseDropServerTests.cs:18` takes its own `IClassFixture<SqlServerFixture>`, beside
  `SqlDatabaseCreationServerTests`' own. With no `TELLMA_TEST_SQL` set, that is a second ~2 GB
  container in the assembly, started for two sub-second tests.
- It runs in parallel with the first, and in the `integration` job (`ci.yml:83`) also alongside
  every other suite's container under the solution-wide `dotnet test`.
- That departs from the one-container-per-assembly norm (PROCESS.md §11) and adds memory pressure
  and startup time to a shared runner.
- The tests cannot just share the creation tests' class fixture, since those hold `model` and
  would block `CreateDatabaseAsync`.
- Fix: put both classes in one `[Collection]` with a shared `ICollectionFixture<SqlServerFixture>`.
  The collection serialises them, so the `model` hold and the drops never overlap, and
  `OwnsContainer` still holds.

### Responses

- **R1-1: fixed in ce4a9384.**
  - Every `dotnet test` step in `.github/workflows/ci.yml` now ends with
    `--logger "console;verbosity=normal" -- xUnit.DiagnosticMessages=true`: build-test, integration,
    both integration-windows suites and identity-e2e. This repo runs xUnit v3 through VSTest
    (`xunit.runner.visualstudio`), so the setting goes as a RunSettings argument after `--`.
  - `CONTRIBUTING.md` gives the same flags for a local run.
  - Checked locally with a throwaway assembly fixture in `Tellma.Core.Testing.Tests`, deleted
    afterwards. Its `DisposeAsync` reruns a drop that a fabricated 3702 held, through the fixtures'
    log sink, then throws the cleanup report.
    - The current CI form printed only
      `[Test Assembly Cleanup Failure (…)] Xunit.Sdk.TestPipelineException`, which reproduces the
      red run.
    - With both flags, the console showed the retry line
      (`DROP DATABASE [probe_held] failed with a transient error on attempt 1 of 3; retrying in 0.0 s: SQL error 3702: …`).
      It also showed the fixture line, the full `AggregateException` message naming the database
      left behind, the inner exception and the stack.
    - Each flag carries half. Diagnostic messages alone printed the retry line but not the report;
      normal verbosity alone printed the report but not the retry line.
  - Cost: one line per passed test. That is about 15,000 lines per OS in build-test, and no
    measurable run time.
  - The nightly workflows are unchanged.
  - The first CI run after integration is the confirmation in CI.
- **R1-2: fixed in ce4a9384.**
  - The drop path now has internal seams. `DropAllAsync(master, databases, retry, log, execute,
    clearPools, token)` takes the command runner and the pool clearer, and `DropAsync` is internal
    with the same two seams. The public overload passes `ExecuteNonQueryAsync` and
    `SqlConnection.ClearAllPools`.
  - Unit tests build real `SqlException`s through SqlClient's internal `SqlError` /
    `SqlErrorCollection` / `SqlException.CreateException` factories, and pin:
    - a held refusal (3702, and 5064 with 5069) runs again, with the order `run, clear, run`
      (no clear before the first run) and one retry line logged;
    - a refusal for another reason (3701, and a -2 timeout) surfaces unchanged, with one run, no
      clear and nothing logged;
    - every run carries `DropStatement(name)` and the 300-second bound, the rerun as well as the
      first run.
  - The server test drops two databases through the real path with recording seams. It asserts
    `clear, run 300 s, run 300 s`: one pool clear before the first drop, and the bound on each
    drop.
  - The reviewer's mutants, each applied alone to `SqlDatabaseDrop.cs`, then built (0 warnings) and
    run against the drop unit tests:
    - M1, both pool clears deleted: 2 failures (both held-refusal cases).
    - M2, `CreateDatabaseRetry.IsTransient` passed as the classifier: 3 failures.
    - M3, the retry dropped (a one-run policy): 3 failures.
    - M4, the command built with SqlClient's 30 s default: 1 failure (the bound test).
- **R1-3: fixed in ce4a9384.** A failed open now throws the same `AggregateException`. Every database
  is named as not tried ("the connection to the server could not be opened, its failure the inner
  exception"), and the open failure is the single inner exception.
  - The connection is also opened before the pools are cleared, so it can reuse a warm pooled
    `master` login rather than always making a fresh one. The open is not retried.
  - Unit test: a server that cannot be reached leaves both databases untried and named, with a
    `SqlException` inner, no drop run and no pool cleared.
- **R1-4: fixed in ce4a9384.**
  - `IdentityServerFixture.DisposeAsync` now drops its database through
    `SqlDatabaseDrop.DropAllAsync`.
  - `Tellma.Identity.E2E` references `Tellma.Testing.Support`, with an inline comment.
  - The class summary now says "a test fixture", and the Support and E2E READMEs name both users.
- **R1-5: fixed in ce4a9384.**
  - `SqlServerCollectionDefinition` (`[CollectionDefinition]`,
    `ICollectionFixture<SqlServerFixture>`) is shared by `SqlDatabaseCreationServerTests` and
    `SqlDatabaseDropServerTests`. The assembly starts one container, and the collection runs them
    one at a time, so the `model` hold never overlaps a creation or a drop. `OwnsContainer` still
    gates the hold.
  - The name ends in `Definition` because CA1711 refuses a type name ending in `Collection`. That
    matches Identity's `SqlServerCollectionDefinition`.
