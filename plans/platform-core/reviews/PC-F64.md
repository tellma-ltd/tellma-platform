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

All four threads stalled together, and no test completed between 13:43:22 and 13:44:25. The
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
- `TellmaIdentityStore.MigrateAsync` (product, Tellma.Identity.Migrations) migrates the in-proc
  identity store under EF's 30-second default, while the migrator that calls it bounds the catalog
  and tenant migrations by `MigrationCommandTimeout`. That is a product inconsistency (spec 0003 /
  0011 §6.1), reported to the orchestrator and not changed in this test-only fix.

### What reviewers should examine hardest

- Whether the timeline reading holds: the stack shows setup, not the refresh. Read the log lines
  around 13:43:20–13:45:24 in nightly 44745573605's integration-windows job.
- Whether anything else in the Core suite's fixture path still runs DDL under the 30-second default
  (grep `MigrateAsync` and `CommandTimeout` under test/).
- The registry's non-forced join rule (TenantRegistry.RefreshAsync). It is correct for this test
  because nothing else refreshes the worker. Any future test that adds a hosted refresh loop to a
  CatalogHost and then relies on `RefreshAsync(force: false)` to observe its own earlier write
  could join a run that began before that write.
