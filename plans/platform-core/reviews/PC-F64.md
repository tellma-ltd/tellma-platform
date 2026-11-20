# PC-F64: Nightly integration-windows: TenantScopeTests cross-instance suspension refresh fails

## Implementer notes

### Failure

Nightly run 44745573605 on platform-core 007f0ff0, job integration-windows (windows-latest,
LocalDB, 4 xUnit threads): `TenantScopeTests.A_suspension_another_instance_wrote_refuses_the_next_scope_after_a_refresh`
failed after 1 min 5 s with `InvalidOperationException: Invalid operation. The connection is closed.`
The other 775 tests passed. The same job passed in nightlies 44701549452 and 44717126616 earlier
the same day.

### Root cause: the test never reached what it pins

The stack ends at the test's first line (TenantScopeTests.cs:145), inside the fixture:
`CatalogHost.CreateAsync` → `CatalogHost.MigrateAsync` → EF `Migrator.MigrateAsync` →
`HistoryRepository.CreateIfNotExistsAsync`. That is the first DDL EF runs in the freshly created
catalog (`IF OBJECT_ID(N'[catalog].[__EFMigrationsHistory]') IS NULL … CREATE TABLE …`). It runs
before the writer registers a tenant, before either registry refreshes, and before any scope is
created. No tenancy code under test ran.

The frames above it (`EndExecuteNonQueryAsync` → `PutStateObject` → `ProcessPendingAck` →
`ProcessAttention` → `ReadSniSyncOverAsync`) are SqlClient handling a command timeout. The client
sent an attention after the command's bound, and the connection was closed while it waited for
the server's acknowledgement. That statement ran under EF's default bound: `CatalogHost.MigrateAsync`
never set one, so it had SqlClient's 30 seconds. An attention a lock wait would honour at once (that
gives the usual "Execution Timeout Expired"). A missing acknowledgement means the server, or the
client's I/O, was not answering at all for several seconds.

The job's timeline shows a server-wide stall of about two minutes at that point. Gaps are
measured from the console's completion timestamps:

| Test (thread) | Normal on this job | 13:43:20–13:45:24 |
|---|---|---|
| this test | ~2 s | 1 min 5 s, failed in setup |
| `MigratorTests.Migrate_grants_the_role_every_catalog_schema_and_sequence_and_no_write_to_the_history` | 7–15 s (its siblings) | 1 min 31 s |
| `MembershipDirectoryTests.A_hint_written_after_the_users_were_read_survives_the_reconciliation` | 4–14 s (its siblings) | 1 min 32 s |
| `TenantScopeTests.A_scopes_own_connection_reaches_its_tenants_database_and_its_sandbox_flag_reads_the_category` (next on the same thread) | a few s | 54 s |
| `MigratorTests.Rerunning_a_line_that_spells_its_server_with_a_protocol_prefix_resumes_the_row` | ~10 s | 30 s |
| Shipment conformance fixture (4th thread) | seconds | ~1 min 45 s before its first test |

All four threads stalled together, and no test completed between 13:43:24 and 13:44:25. The
other three threads' statements ran under long bounds (the migrator's statement bounds, the
fixtures' five-minute creation and migration bounds), or each individually stayed under 30 s, so
they passed slowly. The one fixture statement on a 30-second bound was this catalog migration, and
it lost. The two passing nightlies show smaller slow windows of the same kind (MigratorTests at
25–31 s against a normal 7–15 s), so the stall is a property of that runner and LocalDB, not of
this run's code.

### Is the product wrong? No

Checked against spec 0011 §3.4 and §3.7 and the code:

- `TenantCatalog.SetStateAsync` runs the §7.5 transition in one transaction (`SET XACT_ABORT ON`)
  that also sets `CatalogState.Version = NEWID()` (CatalogStatements.Transition).
- `TenantRegistry.RefreshAsync(force: false)` with no run in flight starts a run that probes the
  version and reloads when it differs (`!=`). A non-forced call joins a run already in flight, but
  the worker host has no hosted refresh loop and nothing else triggers its refresh (the profile
  change hook does not fire here). Its only refreshes are the test's own awaited calls, so the
  refresh at line 157 always reads after the suspension committed.
- The scope factory refuses a `Suspended` tenant with `tenant-suspended` from the refreshed
  snapshot.

So a suspension another instance wrote is seen by the next refresh. The test is deterministic once
its setup completes: it passed 5/5 locally alone and in every load run below. D-146 (the lease
renewal's use of a stale snapshot) does not touch this path.

### Fix

`CatalogHost.MigrateAsync` builds the catalog for 47 call sites in six Core integration classes
(CatalogMigrationTests, MembershipDirectoryTests, RegistryCatalogTests, SessionStoreTests,
TenantCatalogTests, TenantScopeTests). It now
runs the catalog migrations under `SqlDatabaseCreation.CommandTimeout` (five minutes) through
`context.Database.SetCommandTimeout`. That is the bound the suite already uses to create and
prepare the same database one step earlier, and the one `FixtureDatabase` uses to migrate the
data layer's fixture schema. The migrator itself runs catalog migrations under
`MigratorOptions.MigrationCommandTimeout` (an hour), so the helper documented as "as the migrator
does" now does. ARCHITECTURE.md's paragraph on test database creation says the fixtures' migrations
share the bound.

This is the same family as PC-F47/PC-F48. PC-F48's local gate lost 15 tests in exactly these six
classes to the fixture's `CREATE DATABASE` and RCSI `ALTER` timing out at the 30 s default, and
those statements got the five-minute bound. The migration that runs right after them in the same
helper was missed, and this nightly hit it.

Why this is not "a longer timeout that hides it": the bound sits on fixture setup, never on the
code under test. The registry, scope factory and catalog statements keep their own bounds, and a
real defect there still fails at once. The failure was a 30-second bound the fixture inherited by
accident, on the one setup step that missed the suite's established bound. Nothing is retried.

### Verification (local, LocalDB 15.0.4382, Windows, Debug)

- Baseline (pre-fix binary), TenantScopeTests alone: 5/5 runs green (7/7 each).
- Baseline, under load (TenantScopeTests + MigratorTests + all of Catalog.* + Crud.Conformance.*,
  197 tests per run, default xUnit parallelism on 20 cores, so about five times CI's 4 threads).
  Run 1: 166 passed, 31 skipped, 0 failed (10.7 min). Run 2: 96 passed, 89 failed (20.8 min).
- With the fix, under the same load. Run 1: 166 passed, 0 failed (23.3 min, with many tests taking
  over a minute). Run 2: 163 passed, 3 failed (33.9 min).
- A temporary probe (not committed) held EF's `__EFMigrationsLock` app lock on a fresh catalog for
  40 s while `CatalogHost.MigrateAsync` ran. On the baseline the migration failed after 34.2 s with
  "Execution Timeout Expired". A lock wait acknowledges the attention, hence the usual message rather
  than CI's "connection is closed". With the fix it migrated after 41.1 s.

At 20 threads the local server stalls the way the runner did, so the class reproduced, but its
local symptom is different. Every failure in both runs is `Connection Timeout Expired … post-login
phase`, a login that waited past the 15-second connect timeout. The baseline's 89 are 9 tests, 77
tests behind four conformance class fixtures that failed in InitializeAsync, and 3 migrator runs
whose catalog or tenant step failed to connect. The fixed run's 3 failures are the same kind, in
`SqlDatabaseCreation`'s own connection.
No local failure, baseline or fixed, was the CI signature: no command timeout in
`CatalogHost.MigrateAsync`. The fix is therefore verified by mechanism (the probe), and the load runs
show it introduces no failure of its own. They do not show recurrence, so they prove nothing about
CI frequency.

What the stall is, from a sampler that polled `sys.dm_exec_requests` every 2 s (requests older than
10 s). In baseline run 2, concurrent `CREATE DATABASE`s spent tens of seconds each in file I/O
(`ASYNC_IO_COMPLETION`, `PREEMPTIVE_OS_FILEOPS`, `PREEMPTIVE_OS_FLUSHFILEBUFFERS`). Meanwhile the
creations queued behind one another on `LCK_M_X`/`LCK_M_S` in `master` (chains such as 54 → 81 → 84,
with single creations at 50–70 s). System tasks (`TASK MANAGER`, `QUERY STORE BACKGROUND FLUSH`,
`GHOST CLEANUP`, `TEMPORAL_HK_FLUSH`) waited `LCK_M_S` behind them for up to 3 minutes. New logins'
post-login phase queues behind that convoy, which is the local symptom. A DDL statement in a fresh
database (CI's symptom) slows with the same I/O. The CI timeline (four threads starting
database-creating tests within three seconds at 13:43:20, then two minutes of nothing completing)
fits the same convoy at a smaller scale.

### Unverified until the next nightly

- That integration-windows stays green on the runner. A stall longer than five minutes would still
  fail setup, and it would be a different, much larger problem.
- A stall of more than 30 s can still fail a product statement on its legitimate bound mid-test (the
  catalog store's statements, registry reads, `DataOptions.CommandTimeout`). That would be a correct
  product verdict on an unhealthy server, and the fix does not and should not mask it. The same goes
  for a login that outwaits its 15-second connect timeout, the local symptom above.
- The lever on the stall itself is the creation convoy, and it is a ruling, not made here. Two
  options. (a) Gate `SqlDatabaseCreation.CreateAsync` to one `CREATE DATABASE` in flight per process,
  so contenders wait in the client holding no server locks. This conflicts with the deliberate
  design of the 1807 retry: `SqlDatabaseCreationServerTests.Creations_blocked_on_the_model_lock_are_retried_and_all_exist_once_it_is_free`
  runs contenders concurrently against the server. It also leaves the migrator's own creations
  (MigratorTests) ungated. (b) Move the runner's LocalDB data off the C: OS disk (the instance
  keeps its databases under the runner's profile; the workspace is on D:), a workflow change. Either
  can be verified only by nightlies.

### Residual exposure, not changed

- `CatalogMigrationTests.A_database_update_through_the_design_time_factory_creates_the_database_and_the_role_first`
  migrates through the design-time factory under EF's default bound and lets EF create the
  database (no 1807 retry). It emulates `dotnet ef database update` as an operator runs it. A bound
  would change what it emulates, so it is left alone. It is the next candidate if the stall recurs.

### What reviewers should examine hardest

- Whether the timeline reading holds: the stack shows setup, not the refresh. Read the log lines
  around 13:43:20–13:45:24 in nightly 44745573605's integration-windows job.
- Whether anything else in the Core suite's fixture path still runs DDL under the 30-second default
  (grep `MigrateAsync` and `CommandTimeout` under test/).
- The registry's non-forced join rule (TenantRegistry.RefreshAsync). It is correct for this test
  because nothing else refreshes the worker. Any future test that adds a hosted refresh loop to a
  CatalogHost and then relies on `RefreshAsync(force: false)` to observe its own earlier write
  could join a run that began before that write.

## Review 1

Scope: commit 6952a9d3 (CatalogHost.MigrateAsync bound, ARCHITECTURE.md paragraph, review file).

What I checked:

- **Root cause, from the nightly log.** The integration-windows log of 44745573605 (fetched read-only
  with `gh run view --log`) shows the failure at 13:44:25 after 1 min 5 s. The stack ends in
  `HistoryRepository.CreateIfNotExistsAsync` ← `Migrator.MigrateAsync` ← `CatalogHost.MigrateAsync`
  (CatalogHost.cs:145 at 007f0ff0) ← `CatalogHost.CreateAsync` ← TenantScopeTests.cs:145. That is
  the test's first line, before any registration, refresh or scope. The SqlClient frames
  (`EndExecuteNonQueryAsync` → `PutStateObject` → `ProcessPendingAck` → `ProcessAttention`) are the
  path where a command timeout's attention goes unacknowledged. The stall is in the log as stated.
  No test completed from 13:43:24.4 to 13:44:25.2. Two slow tests then finished:
  `MigratorTests.Migrate_grants_the_role…` (1 m 31 s) and `MembershipDirectoryTests.A_hint_written…`
  (1 m 32 s). `TenantScopeTests.A_scopes_own_connection…` took 54 s and finished at 13:45:19.
  `MigratorTests.Rerunning_a_line…` took 30 s. The notes say 13:43:22, but the last completion
  before the gap was at 13:43:24. That does not affect the conclusion. The implementer's probe
  logs (PC-F64-probe-base.log, PC-F64-probe-fix.log) back the mechanism: with the lock held 40 s,
  the baseline failed with "Execution Timeout Expired" after 34.2 s and the fixed code passed in
  45 s.
- **The product's cross-instance refresh is correct** (0011 §3.4, §3.7).
  - Every transition in `CatalogStatements` (lines 107, 120, 133, 145) bumps
    `CatalogState.Version = NEWID()` in the same batch.
  - `TenantRegistry.RefreshAsync(force: false)` (TenantRegistry.cs:144-180) starts a new run when
    none is running or pending. That run probes the version and reloads when it differs (lines
    360-373).
  - `CatalogHost` composes without hosted services, so the worker's only runs are the test's own
    awaited calls. The refresh at line 157 therefore starts after the suspension committed, and
    the scope factory refuses the tenant from the refreshed snapshot.
- **The bound sits only in test setup.** It is set on the context of the test helper
  (`CatalogHost.MigrateAsync`), and only `CatalogHost.CreateAsync` and direct test calls reach that
  helper. No product path changes. The registry's first load (`InitializeAsync`, still under its
  1-minute `FirstLoadTimeout`), the catalog writes and the scope factory keep their own bounds.
  The bound matches the migrator's practice, since MigrateCommand.cs:171 sets
  `MigrationCommandTimeout` on the same `CatalogDbContext`. No test depends on
  `CatalogHost.MigrateAsync` failing fast:
  - `CatalogMigrationTests.Without_the_role…` expects a permission `SqlException`, not a timeout.
  - No test holds `__EFMigrationsLock`.
- **Build and tests.**
  - `dotnet build` of Tellma.Core.IntegrationTests at 6952a9d3: Build succeeded, 0 warnings
    (PC-F64-r1-build.log).
  - The six classes that use the helper (CatalogMigrationTests, MembershipDirectoryTests,
    RegistryCatalogTests, SessionStoreTests, TenantCatalogTests, TenantScopeTests) on LocalDB,
    with the integration filter: 51/51 passed in 2 m 16 s (PC-F64-r1-six-classes.log). This covers
    every touched test the commit's list reaches only through the load runs.
  - The implementer's solution build log shows 0 warnings and the format log is clean.
  - The changed .cs file has no BOM.
- **AGENTS.md / PLAN §7.1.** The XML doc is updated. The inline comment explains the bound and
  cites no PC id, decision or `docs/` path. ARCHITECTURE.md is updated in the same change (item 6).

### Findings

R1-1 (minor). ARCHITECTURE.md:1231 states a rule broader than the code delivers.
- **What the sentence claims.** "The fixtures that then migrate such a database (the catalog a Core
  suite composes its tenancy runtime over, the data layer's fixture schema) run the migrations
  under the same five-minute bound … never the client's 30-second default." Read as a rule, that
  covers every test that migrates a database `SqlDatabaseCreation` created.
- **Where it does not hold.** These tests still migrate under EF's 30-second default:
  - test/core/Tellma.Core.EntityFrameworkCore.IntegrationTests/ColumnCollationTests.cs:90-95, a
    helper documented as "Creates a fresh database and applies the MigrationsHost's migrations",
    with the database from `SqlServerFixture.CreateDatabaseAsync` → `SqlDatabaseCreation.CreateAsync`.
  - DropGuardTests.cs:36-38 in the same project.
  - test/apps/Tellma.Identity.IntegrationTests/StoreCheckTests.cs:101, 122, 159, 169, 182, through
    `TellmaIdentityStore.MigrateAsync` with no `Command Timeout` in the string.
- **Failure scenario.** The next fix item for a stall in one of those suites (they run on Linux CI
  today, and would join a Windows LocalDB job if D-144's split changes) reads ARCHITECTURE.md as
  saying it is already covered.
- **Suggested fix.** Either narrow the sentence to what it covers ("The Core suite's catalog
  fixture and the data layer's fixture schema migrate under the same bound …"), or add that suites
  whose migrations are themselves under test (the EF Core suite's migration tests, and the identity
  store's own `MigrateAsync`) leave the bound to EF or the product. No code change is needed.

### Rulings on the implementer's open points

1. **The stall lever (option a or b): neither now; wait for a recurrence.**
   - The fix removes the only 30-second bound in the Core fixtures' setup path. Integration-windows
     is nightly, and a red there is a fix item, not a line stop (D-144).
   - Option (a) is not worth its cost. It undoes what
     `SqlDatabaseCreationServerTests.Creations_blocked_on_the_model_lock_are_retried_and_all_exist_once_it_is_free`
     exists to prove. It leaves the migrator's creations and EF's own creation (the design-time test
     and the identity engine's tests) ungated. It also serialises every suite's setup in-process.
   - Option (b) is the better lever if the stall recurs, but it is unproven. LocalDB places
     databases without explicit file paths under the instance owner's profile. Moving them needs
     either explicit `FILENAME`s from an environment variable in `SqlDatabaseCreation`, or an
     instance whose profile is on D:. Neither is a one-line workflow change, and only nightlies can
     verify it.
   - Recommendation: record (b) on the watchlist as the next step if integration-windows goes red
     again with a setup-path timeout or a connect timeout during a creation convoy.
2. **`TellmaIdentityStore.MigrateAsync` under EF's 30 s default: the claim is incorrect for the
   migrator path, so no fix item.**
   - MigrateCommand.cs:186 hands the store `bounds.MigrationConnectionString(catalog.ConnectionString)`.
     That string sets SqlClient's `Command Timeout` to `MigrationCommandTimeout`
     (MigratorOptions.cs:121-125, added by PC-F48).
   - `TellmaIdentityStore.CreateContext` sets no EF command timeout. EF therefore leaves each
     command's timeout at the connection's default, which is the migration bound.
   - Two tests pin this. `MigratorOptionsTests.The_migration_connection_string_gives_every_command_on_it_the_migration_bound`
     shows that a command on that connection inherits 5400 s. The Acme
     `MigratorTests.The_identity_store_migrates_under_the_configured_migration_bound` shows that the
     migrator hands the store that string.
   - The engine's own startup migration (`IdentityStoreCheck`, standalone mode) uses the engine's
     own composition. That is the engine's runtime path, and this item did not touch it.
   - The "Residual exposure" bullet in the implementer notes should not be carried into DECISIONS
     as a product inconsistency.
3. **The design-time-factory test keeping EF's default: agreed.**
   - `A_database_update_through_the_design_time_factory_creates_the_database_and_the_role_first`
     pins what an operator's `dotnet ef database update` does. A bound set by the test would no
     longer emulate that, and the factory sets none.
   - It is the most exposed remaining statement in the suite, since EF's own `CREATE DATABASE`
     runs under 30 s with no 1807 retry. Note it on the watchlist next to (b) and act only on a
     recurrence. If it does recur, the right change is in the factory (a bound an operator would
     also get), not in the test.

### Responses

R1-1: fixed in the "PC-F64: review 1 fixes" commit. The ARCHITECTURE.md sentence now names the two
fixtures it covers (the Core suite's catalog and the data layer's fixture schema), and says that
tests whose migrations are themselves under test (the EF Core suite's migration tests, the identity
store's own migration) leave the bound to EF or the product. No code change.

Rulings 1-3 accepted. The identity-store bullet is removed from "Residual exposure", because the
migrator hands the store a connection string bounded by `MigrationCommandTimeout`. The stall lever
and the design-time-factory test stay as they are until a recurrence. The notes' "13:43:22" now
reads 13:43:24, the last completion before the gap.

## Integration CI fix (run 44785071160)

build-test (windows-latest) failed one unit test that the item does not touch:
`SmtpProtocolTests.Sends_a_full_feature_message_as_correct_mime`. It threw a `TimeoutException`
from MailKit's `SocketUtils.ConnectAsync` after 10 s, which is the sender's whole connection budget
(`TimeoutSeconds = 10`, covering name resolution and every address attempt). ubuntu passed the same
run.

Cause, in the test infrastructure. `SmtpSenderFactory.Options` pointed every sender at
`"localhost"`, but `InProcessSmtpServer` bound by port alone, which SmtpServer turns into
`0.0.0.0:port`, so it listens on IPv4 only. On Windows `localhost` resolves to `::1` first. Nothing
listens on `[::1]:port`, so Windows refuses the connection only after retrying the SYN, and MailKit
then falls back to 127.0.0.1. Measured with a scratch probe on this machine:

- a refused `[::1]` connection took 2.1 to 2.4 s;
- a MailKit connect and greeting through `localhost` took 2.5 to 5.1 s;
- through `127.0.0.1` it took 12 to 19 ms.

Every connecting SMTP test on Windows paid that penalty, once per connection:
- Windows CI: 2 to 12 s per test;
- ubuntu CI, where `localhost` is 127.0.0.1: 6 to 700 ms;
- local Windows baseline: 2 to 6 s, 26 s for the class.

On a loaded runner the refused-SYN retries stretch, and this run used up the 10 s budget. The
adapter is not at fault. The configured host is the operator's choice, and the budget covering
every address is MailKit's documented behaviour.

Fix:
- `InProcessSmtpServer.Address` (IPv4 loopback) is now the one address the server binds
  (`Endpoint(new IPEndPoint(Address, port))`), probes its port on, and checks readiness on.
- `SmtpSenderFactory.Options` connects to that address by number.
- The project README says so.

Nothing is retried, and no bound changes. Binding an explicit address also means another process
listening on `[::1]` at the same port can no longer receive the sender's connection.

Local verification (Windows, Debug):
- the SMTP test project 5 times alone: 18 passed and 2 skipped each run, and the failing test took
  69 to 793 ms;
- 3 more runs while Tellma.Core.Tests and Tellma.Core.AspNetCore.Tests ran in parallel: all green,
  and the failing test took 70 ms to 1 s.
- One reconnect test took 6 s once during the loaded runs, at cold start, while the two load
  processes were starting. It is not the address penalty, which slowed every connecting test.
