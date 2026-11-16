# PC-F47: integration fixtures retry CREATE DATABASE on SQL error 1807 (model lock)

## Implementer notes

### Diagnosis (measured on a throwaway mssql/server:2022 container)

- `CREATE DATABASE` copies `model` under an exclusive lock. With a session sitting in `model`, a
  `CREATE DATABASE` waited **about 25 s** (26.3 s and 24.6 s measured, the second with
  `SET LOCK_TIMEOUT 1000`, so the session's lock timeout does not shorten it) and then failed with
  two errors, in this order: **1807** "Could not obtain exclusive lock on database 'model'. Retry
  the operation later." and **1802** "CREATE DATABASE failed. Some file names listed could not be
  created." Nothing was created; the same statement succeeds once `model` is free.
- A suite's parallel creations queue on that lock. When the queue ahead of a creation takes longer
  than the server's ~25 s wait (a loaded host, many classes creating at once), it fails with 1807.
  That is PC-066's 37 failures.
- **The "SQL load timeouts" share the path.** `SqlServerFixture.CreateDatabaseAsync` and every
  direct `SqlServerFixture.ExecuteAsync(master, "CREATE DATABASE …")` ran under SqlClient's default
  30 s command timeout. A creation that waits ~25 s for `model` and then copies it on a loaded host
  crosses 30 s and fails with "Execution Timeout Expired" (error -2), the symptom LEDGER's 2026-11-08
  watch row records for the EF suite's `CREATE DATABASE`. `FixtureDatabase` already used a 5-minute
  bound. I could not see PC-049's "29 SQL load timeouts" logs (other slots are off limits).
- **My own first gate run reproduced the load timeouts** (Core integration, 21 min on a host
  loaded by other slots; 303/307, 4 "Execution Timeout Expired", none 1807), and their stacks show
  two sources:
  1. `CatalogDatabases.CreateEmptyCatalogAsync`'s `ALTER DATABASE … SET READ_COMMITTED_SNAPSHOT ON
     WITH ROLLBACK IMMEDIATE`, still on the 30 s default after the creation itself had moved to the
     long bound (1 failure, `TenantCatalogTests`). Fixed: the preparation statements now run through
     `SqlDatabaseCreation.PrepareAsync` under the same 5-minute bound, never retried.
     `FixtureDatabase`'s ALTERs were already on 5 minutes.
  2. **The migrator under test** ("catalog: failed (SqlException: Execution Timeout Expired)",
     3 failures in `MigratorTests` and `VersionTagSeedTests`). The migrator's catalog creation has
     no bound, but its later statements (`TargetDatabase.EnableReadCommittedSnapshotAsync`, the
     query store, EF's catalog migrations) run on SqlClient's 30 s default. That is production
     code (src/core/Tellma.Core.Migrator), not the fixture path, and a retry would not fix a
     timeout, so it is reported and not changed here.
- **A timeout is not retried, deliberately.** The client abandons a statement the server may still
  complete, so a re-run can meet the database the first run created (1801). The fix for timeouts
  is a creation bound long enough (5 minutes), not a retry.

### What was built

- `test/shared/Tellma.Testing.Support/Sql/CreateDatabaseRetry.cs`: the policy. Transient means the
  failed statement's errors contain 1807 and nothing besides 1807 and 1802. Bounded runs
  (default 6), waits doubling from a base to a ceiling (default 1 s to 10 s), jittered ±25 %.
  Each retry is logged, and so is giving up; the last run's own exception then surfaces unchanged.
  A non-transient failure, or any failure once the token is cancelled, surfaces at once with no
  wait. The loop's seams (classifier, wait, jitter) are internal, reached by
  `Tellma.Core.Testing.Tests` through InternalsVisibleTo.
- `test/shared/Tellma.Testing.Support/Sql/SqlDatabaseCreation.cs`: the one way fixtures and tests
  create a database: `CREATE DATABASE [name] COLLATE x;` on a fresh connection per run, 5-minute
  command bound, under the policy; and `PrepareAsync`, which runs a fresh database's preparation
  statements (ALTER DATABASE options, a role) under the same bound without retrying. Retry lines go to the running test's output
  (`TestContext.Current.TestOutputHelper`), or as a diagnostic message outside a test (a fixture's
  initialization). The collation is spliced unquoted, so it is refused unless it is ASCII letters,
  digits and underscores.
- Routed through it: `SqlServerFixture.CreateDatabaseAsync` (EF, Queryex, Identity suites),
  `FixtureDatabase.CreateAsync` (Testing.Entities now references Testing.Support),
  `CatalogDatabases.CreateEmptyCatalogAsync`, and every direct `CREATE DATABASE` in
  `TenantScopeTests`, `RegistryCatalogTests`, the Core `MigratorTests` and the Acme `MigratorTests`.
  No `CREATE DATABASE` text remains in a test under `test/` or `distributions/*/test/` except the
  string goldens of `Tellma.Core.Tests/Tenancy/DatabaseCreationTests.cs`.
- Tests: `Tellma.Core.Testing.Tests/Sql/CreateDatabaseRetryTests.cs` (unit: which numbers are
  transient, including 1807 beside another real error being non-transient; a non-SQL exception
  carrying the same message is not; the wait schedule, ceiling, jitter edges and overflow guard;
  argument domains; success after retries with the right waits and log lines; exhaustion surfacing
  the same exception instance after exactly MaxAttempts runs; a non-transient failure surfacing
  with no wait or log; cancellation before and during a wait).
  `Tellma.Core.Testing.Tests/Sql/CreateDatabaseContentionTests.cs` (`Category=Integration`, its own
  container): a session holds `model`; six concurrent creations and a one-run creation start; the
  one-run creation surfaces SQL Server's real `SqlException` with errors [1807, 1802], which
  `IsTransient` accepts, and logs "giving up"; every contender logs a retry citing
  "SQL error 1807, 1802"; the session leaves; all six databases then exist in the platform
  collation and the one-run database does not. About 30 s plus the container start.
- ARCHITECTURE.md (CI/CD & Quality Gates): a bullet stating that integration suites create
  databases through the shared helper, why 1807 happens, and that only 1807 is retried while
  timeouts and other errors fail at once. Three READMEs updated.

### Deviations

1. The item asked for a retry on 1807; the creation's command bound is also raised from the 30 s
   default to 5 minutes, because the measured ~25 s server-side lock wait alone nearly fills 30 s.
   This is the "load timeout" half of the brief, fixed without a retry (a timeout is not safe to
   retry, see above).
2. 1802 is accepted only beside a 1807, since SQL Server always follows 1807 with it; 1802 alone,
   and 1807 beside any other error, surface.
3. `Tellma.Testing.Entities` now references `Tellma.Testing.Support` (bringing Testcontainers and
   xunit's extensibility core transitively), so there is one creation helper instead of two. No
   EF model or migration changed.

### Open risks and things not covered

- **The production migrator is exposed to 1807 too.** `TargetDatabase.EnsureExistsAsync`
  (src/core/Tellma.Core.Migrator) runs `CREATE DATABASE` with no retry, and `MigrateCommand`
  provisions seeds with `Parallel.ForEachAsync` (`--parallelism`, default 4), so several tenants
  provisioned at once on one SQL Server, or a provision beside any other creation on that server,
  can fail a tenant with 1807. In the Core and Acme migrator suites the migrator under test is the
  creator, so those creations are still unprotected by this fix. Its statements after the creation
  also run on the 30 s default, which is what times out on a loaded test host (3 of the 4 timeouts
  above). A production retry and bound belong in the migrator with its own ILogger and tests; they
  are out of this fix item's test-infra scope. Until then the migrator suites can still fail on a
  loaded local host.
- Databases EF creates itself (Identity's `TellmaIdentityStore.MigrateAsync` over a reserved name,
  the identity E2E host) go through EF's own creation, not this helper.
- `eng/queryex-inspection` has its own `CREATE DATABASE`; it is a tool, not a suite, and is left.
- Worst case under the default policy a creation keeps trying for about 3 minutes (6 runs × ~25 s
  server wait + ~25 s of backoff) before surfacing 1807.

### Gates run (local, Windows, host shared with other slots)

- `dotnet build Tellma.slnx`: Build succeeded, 0 warnings. `dotnet format --verify-no-changes`: clean.
- Unit filter: every project passed.
- Integration, one suite at a time: Tellma.Core.IntegrationTests 307/307 (after the
  `PrepareAsync` fix; the run before it was the 303/307 described above), Tellma.Core.Testing.Tests
  1/1, Tellma.Core.EntityFrameworkCore.IntegrationTests 10/10,
  Tellma.Core.Queryex.IntegrationTests 289/289, Tellma.Distro.Acme.IntegrationTests 11/11,
  Tellma.Identity.IntegrationTests 205/206 in 36 min: `InvitationDeliveryStatusApiTests.A_delivered_invitation_reports_what_the_provider_said`
  failed on `expectsDeliveryEvents` and passed alone (class 6/6). That test gets its database
  through EF over `ReserveDatabase`, which this change does not touch, so I count it a load flake.

### What reviewers should examine hardest

- Whether the transient rule is tight enough: could 1807 with 1802 ever follow a partial creation
  that a re-run would trip over (1801)? The integration test re-runs the same names successfully.
- That no test relied on `SqlServerFixture.ExecuteAsync` running the creation and a following
  statement on one connection (CatalogDatabases and FixtureDatabase now run the ALTERs on a second
  connection after the creation).
- The contention test's determinism: it releases `model` only after every contender logged a
  retry, and it uses its own container so the held lock never stalls other suites.

## Review 1

Reviewed b153b6e1 (b153b6e1975b0f92eb8fe1fc8d116c3bd2dccf35). Verified locally: `dotnet build` of
Tellma.Core.Testing.Tests and Tellma.Core.IntegrationTests printed "Build succeeded", 0 warnings;
`Tellma.Core.Testing.Tests` filtered to `Sql` passed 19/19, including the real contention test on its
own container; `Tellma.Core.IntegrationTests` filtered to the changed classes (TenantScopeTests,
RegistryCatalogTests, TenantCatalogTests through CatalogDatabases, and the three edited MigratorTests)
passed 27/27. The integration suites the commit lists are every non-Live integration suite, which
PLAN §7.1 item 4 requires because `test/shared/*` changed. No EF model or migration changed. Apart
from the bare-name `eng/` tool and the string goldens, no test-authored `CREATE DATABASE` remains
outside the helper. The classifier is tight: 1807 alone or with 1802, nothing else, and nothing
after cancellation. The run bound and exhaustion rethrow are correct, and a 1807 that did leave a
database or files behind would fail the rerun with 1801 or 5170, which is not transient, so it
cannot be retried green.

### Findings

**R1-1 (major): the contention test holds `model` on a shared server whenever `TELLMA_TEST_SQL` is set.**
`test/core/Tellma.Core.Testing.Tests/Sql/CreateDatabaseContentionTests.cs:23-28` (remarks: "The
server is the class's own") and `:52-59` (the holder), and `test/core/Tellma.Core.Testing.Tests/README.md`
Running section ("needs Docker, or TELLMA_TEST_SQL"). `IClassFixture<SqlServerFixture>` gives the class
its own container only when `TELLMA_TEST_SQL` is unset. When it is set, the fixture returns the server
that variable names (`SqlServerFixture.cs:52-57`), and CONTRIBUTING.md documents that variable for
LocalDB and local-instance runs. On that server the test holds `model` for at least the server's
~25 s lock wait, then the six contenders' copies follow. A developer who runs the integration
filter across the solution with LocalDB, where assemblies run in parallel, therefore gets a
deterministic 1807 in every other suite's creation that overlaps that window. The helper's retries
absorb some of that, but the creations this item leaves unprotected fail outright: the migrator
under test in the Core and Acme migrator suites, and the Identity suite's EF-created databases
(R1-2). The test's own exact `[1807, 1802]` assertion has also never been run against LocalDB.
Suggested fix: when `Environment.GetEnvironmentVariable(SqlServerFixture.ServerVariable)` is set,
skip the test with xUnit v3's `Assert.Skip` and a reason, or have it start its own `MsSqlContainer`
regardless of the variable. Change the README's run line to say the test needs Docker.

**R1-2 (minor): ARCHITECTURE.md and the commit overstate the coverage; the Identity suite's creations remain exposed to 1807.**
`ARCHITECTURE.md:1229` says integration suites create their databases through the helper, "never a
bare `CREATE DATABASE`". The commit message also says "Every test creation now goes through one
helper". But the Identity integration suite is the largest parallel creator (206 tests). Most of
its databases come from `ReserveDatabase` plus EF's own creation in the host or in
`TellmaIdentityStore.MigrateAsync`: `test/apps/Tellma.Identity.IntegrationTests/Infrastructure/DatabaseBackedFactory.cs:23,52`,
`MigrationsTests.cs:26`, and `StoreCheckTests.cs:73,138,229`. The identity E2E host
(`IdentityServerFixture.cs:543-549`) and the migrator suites work the same way. Those creations
contend on the same `model` lock in the suite's own container, with no 1807 retry. The implementer
notes disclose this, but ARCHITECTURE.md states it as settled. Scenario: an Identity class fails
with 1807 on a loaded host, and the bullet leads the triager to rule the lock out. Suggested fix:
reword the bullet to "a database the test code itself creates goes through …; a database the
product under test creates (the migrator, EF in the identity host) is not covered". Also note the
Identity exposure for the orchestrator alongside the migrator follow-up. Where an Identity test is
not about creation, a further option is to pre-create the database through
`SqlDatabaseCreation.CreateAsync` in `DatabaseBackedFactory`, but only after checking that EF's
creation sets nothing the store check later requires, such as read-committed snapshot.

**R1-3 (minor): the five-minute bound, `PrepareAsync` and the collation guard have no test that fails when they break.**
`test/shared/Tellma.Testing.Support/Sql/SqlDatabaseCreation.cs:26,63-67,84-92,127`. The commit
calls the creation bound "the load-timeout half of the item", and `PrepareAsync` exists because of
an observed timeout. Yet setting `CommandTimeout` back to 30 s, dropping line 127, or deleting the
collation check (the only guard on an unquoted splice) leaves every test green.
`CreateDatabaseRetryTests` drives only `CreateDatabaseRetry`, and the contention test never reaches
30 s on a single command. Suggested fix: add unit tests that `CreateAsync` throws `ArgumentException`
for a collation such as `Latin1_General_100_CI_AS; DROP` and for a blank one. Pin the bound
directly too, for example by moving the command construction behind an internal factory the test
inspects, or at least by asserting that `SqlDatabaseCreation.CommandTimeout` is at least the
server's lock wait plus a margin.

**R1-4 (minor): `PrepareAsync`'s XML doc says its batches share one connection; each opens its own.**
`test/shared/Tellma.Testing.Support/Sql/SqlDatabaseCreation.cs:75-76` ("each a batch of its own on
one connection") against `:88-91` and `:117-131` (a fresh `SqlConnection` per call). A caller who
trusts the doc and passes batches that depend on session state, such as a `SET` option, a
`#temp` table or `USE`, sees the later batch run without it. Suggested fix: open one connection in
`PrepareAsync` and run every batch on it, which also matches `SqlServerFixture.ExecuteAsync`, the
API it replaced. Otherwise correct the doc to "each on a fresh connection".

### Responses

- R1-1: fixed in the review 1 fixes commit. `SqlServerFixture` exposes `OwnsContainer`, and the
  contention test calls `Assert.SkipUnless(sql.OwnsContainer, …)`, so it never holds `model` on a
  server `TELLMA_TEST_SQL` names. The class is renamed `SqlDatabaseCreationServerTests` because it
  now also holds the preparation test (R1-4), which runs on any server. The README's run line says
  Docker, and a sentence explains the skip.
- R1-2: fixed in the review 1 fixes commit, by closing the gap rather than only rewording.
  `DatabaseBackedFactory.CreateStore` pre-creates every Identity host store through the helper. It
  runs `CREATE DATABASE … COLLATE <platform>` and then `ALTER DATABASE … SET READ_COMMITTED_SNAPSHOT
  ON`, which is exactly what EF's SQL Server creation runs (checked in the EF source:
  `SqlServerMigrationsSqlGenerator` appends the RCSI ALTER when `EngineEdition <> 5`). The host
  then migrates the existing store, so the store check and the migration see what they saw before.
  That covers all 147 factory call sites. The tests that prove the engine's own creation or a
  missing store keep it: `MigrationsTests` ("against a database it creates itself"), and the
  `StoreCheckTests` that reserve a name (`idstoremissing`, `idstorenomigrations`,
  `idstoreinvalidseed`, and `idstorenew`, which now reserves directly instead of going through the
  factory, because it asserts the engine's creation gives the platform collation and RCSI). The
  identity E2E host is left too: its fixture says it deliberately creates the store as a
  developer's first start does, and it creates one database per fixture, so it does not contend.
  The migrator suites are left on purpose: they exist to exercise the migrator's own creation,
  which the orchestrator is fixing as PC-F48. ARCHITECTURE.md now says "a database that test code
  creates", states the Identity pre-creation, and names what is not covered. The commit message
  says the same, and so does the Identity README's database bullet.
- R1-3: fixed in the review 1 fixes commit. The statement and the command are built by internal
  `SqlDatabaseCreation.CreateStatement` and `CreateCommand`, which both creation and preparation
  use. New `SqlDatabaseCreationTests` (unit) pin the bracketed statement and the refusal, before
  any connection, of five non-plain collations (`; DROP …`, `--`, a space, a newline, a non-ASCII
  letter) and of blank names and collations. They also pin the 300 s `CommandTimeout` on a built
  command, and that the bound is at least five times the server's measured ~25-30 s lock wait.
  Reverting the bound to 30 s, dropping the timeout line or deleting the guard each fails a test.
- R1-4: fixed in the review 1 fixes commit, with the reviewer's first option. `PrepareAsync` runs
  every batch in order on one connection, as `SqlServerFixture.ExecuteAsync` did. The doc says so.
  New `Preparation_runs_every_batch_on_one_session` creates a `#temp` table in one batch and reads
  it in the next; it fails with a fresh connection per batch.
