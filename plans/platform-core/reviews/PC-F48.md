# PC-F48: migrator CREATE DATABASE model-lock retry and explicit statement bounds

## Implementer notes

### What was built

- **Bounds, one per statement class.** `src/core/Tellma.Core.Migrator/MigratorOptions.cs`, bound from `Tellma:Migrator` through `HostOptionsBinding.Bind` in `TellmaMigrator`. The startup gate's `core.options` check validates them, so a malformed or out-of-range value exits 3, naming its key. Every bound is a `TimeSpan` from 1 s to 1 day. A bound reaches SqlClient as whole seconds rounded up (`MigratorOptions.Seconds`), so it never shrinks to 0, which means "no bound".

  | Key | Default | Statements |
  |---|---|---|
  | `CommandTimeout` | 5 min | edition, DB_ID, collation, freshness, the role, the application principal, the grants, the fingerprint row; every `status` read (its catalog and tenant EF contexts too) |
  | `DatabaseCommandTimeout` | 30 min | `CREATE DATABASE` (until now `0`, unbounded), RCSI `ALTER DATABASE … WITH ROLLBACK IMMEDIATE`, the query store |
  | `MigrationCommandTimeout` | 1 h | the catalog's EF migrations (until now the 30 s default), the in-proc identity store's, a tenant's (until now a 1 h constant) |
  | `CreateDatabaseAttempts` | 5 (1–20) | the model-lock retry below |
  | `VersionTagBumpTimeout` | 30 s | the move on the way out of a failed tenant run; folded in from `MigratorContext`'s raw parse |

  - These choices follow what each class waits for. Reads and grants wait only for locks on small objects, so 5 min is generous yet still catches a stuck run. Database creation and option changes wait for other sessions to leave and, on Azure SQL Database, for the service (`CREATE DATABASE` there can take minutes). Migrations can rebuild large tables.
- **The identity store.** `migrateIdentityStore` is a `Func<string, Task>`, a signature the spec fixes, and the store builds its own EF context. EF sets a command's timeout only when its own is set (efcore `RelationalCommand`). SqlClient's `Command Timeout` connection-string keyword is the default for every command created on the connection. So `MigrateCommand` passes `MigratorOptions.MigrationConnectionString(catalog)`, which sets that keyword and nothing else. A unit test checks that a command created on such a connection inherits the bound.
- **The model-lock retry.** `src/core/Tellma.Core.Migrator/Sql/CreateDatabaseRetry.cs`, used by `TargetDatabase.EnsureExistsAsync` for the catalog, provisioning, and the missing-database check of a migrated tenant (which never creates).
  - **What is transient.** A `SqlException` whose errors include 1807 and nothing besides 1807 and 1802. Anything else surfaces at once, including a timeout (-2), a bare 1802, 5170 or 1801.
  - **Schedule.** At most `CreateDatabaseAttempts` runs. Waits double from 2 s to a 15 s ceiling, each multiplied by a factor from 0.75 to 1.25 drawn from `Random.Shared`, so about 2, 4, 8 and 15 s. The worst case with the defaults is about 5 × 25 s of server-side lock wait plus about 29 s of waits, roughly 2.5 min.
  - **Logging.** Each retry logs the structured warning `MigratorDatabaseCreationRetrying` with Database, Server, Attempt, MaxAttempts and RetryDelay. Running out logs `MigratorDatabaseCreationGaveUp` with Attempts. Both go through the migrator's own category (`MigratorLog`). The server's own exception then surfaces unchanged and is reported as `catalog: failed (SqlException: …)` or as the tenant's failure.
  - **Cancellation.** A failure after cancellation surfaces at once, and a wait honours the token.
  - **Connection.** The creation runs again on the same `master` connection, since 1807 has severity 16 and leaves the connection usable. An 1801 during a retry (another run created the database meanwhile) still counts as existing.
- **Partially created database.** 1807 creates nothing. The integration test asserts DB_ID is null after a run gives up. A timed-out or cancelled creation may still finish server-side. That is why timeouts are never retried: the next run finds the database through DB_ID, or through 1801 if it races, checks its collation, and treats it as fresh because its history table is absent. RCSI (`IF NOT EXISTS`), the query store, the principal and the role are idempotent there. File leftovers (5170, or 1802 alone) are non-transient and surface for an operator.
- **--parallelism.** Each tenant's creation retries independently with its own jitter, so tenants that fail together spread out. A creation stuck on model holds its parallel slot and tenant lock for at most about 2.5 min with the defaults, after which that tenant fails and the run goes on.
  - **Rejected: a per-server creation gate in the client.** SQL Server already serializes on model. On Azure SQL Database, creations on one logical server run in parallel productively, and a client gate would serialize them for nothing. A gate also cannot see other runs, other distributions or a leftover session in model, which the retry handles all the same.
- **Integration test.** `test/core/Tellma.Core.IntegrationTests/Migrator/ModelLockTests.cs` reproduces the contention by holding an unpooled session in `model`.
  - **First test.** Two `migrate --catalog-only` runs start while model is held. The one with one attempt gives up, logs GaveUp with Attempts = 1, reports the 1807 text and creates nothing. The patient run logs Retrying with Attempt 1 of 5 and a delay within ±25 % of 2 s. The test releases model, and the patient run converges with the platform collation and RCSI on.
  - **Second test.** `DatabaseCommandTimeout = 00:00:01` while model is held. The run fails with "Execution Timeout Expired" and logs neither event: the timeout is not retried.
  - **Isolation.** The class is in the new `Infrastructure/SerialSuites` collection (`DisableParallelization = true`), so it runs alone after the parallel suites. The held lock would otherwise stall every other test's creation on the shared server: the assembly's one Testcontainers server, or LocalDB on the Windows CI job.
  - **Cost.** About 50 s for the first test (the server's own 25 s lock wait, then the retry) and about 2 s for the second.
- **Docs.** The migrator README gains "Statement bounds and the model lock", a `MigratorOptions` row and an updated `Sql/` row. The integration-test README gains the suite. ARCHITECTURE.md's "Tenant fan-out" gains the bullet "Bounded statements, one retry".

### Tests run

- `dotnet build Tellma.slnx`: Build succeeded, 0 warnings, 0 errors. `dotnet format Tellma.slnx --verify-no-changes`: clean.
- Unit filter across the solution: every project passed. Tellma.Core.Tests ran 4216 passed and 1 skipped; its 127 migrator tests include the new `CreateDatabaseRetryTests` and `MigratorOptionsTests` and the startup-gate theory.
- Integration on Testcontainers:
  - Tellma.Core.IntegrationTests: 314 of 329 passed on the full run. The 15 failures were all in CatalogMigrationTests, SessionStoreTests, RegistryCatalogTests, MembershipDirectoryTests, TenantCatalogTests and TenantScopeTests, at 13:09-13:15 while the unit tier ran beside them. Each was the fixture's own `CREATE DATABASE`/RCSI `ALTER` in `CatalogDatabases` timing out at the 30 s default, the known local load flake. Those six classes, rerun alone: 47 of 47 passed.
  - The trx shows ModelLockTests started only after the last other test ended (13:27:53). Durations: 49 s and 1.7 s.
  - Tellma.Distro.Acme.IntegrationTests: 11 of 11 passed.

### Deviations

1. New configuration keys under `Tellma:Migrator`: `CommandTimeout`, `DatabaseCommandTimeout`, `MigrationCommandTimeout` and `CreateDatabaseAttempts`. Spec 0011 §8 does not list them; it says nothing about timeouts or retries.
2. `VersionTagBumpTimeout` is now validated options. A malformed or out-of-range value now fails the startup gate (exit 3) instead of silently falling back to 30 s.
3. `CREATE DATABASE` goes from unbounded (`CommandTimeout = 0`) to a 30 min default bound. The catalog's EF migrations go from the 30 s default to 1 h.
4. The in-proc identity store gets the catalog connection string with `Command Timeout` set to the migration bound, rather than the bare catalog connection string.
5. `status` reads are now bounded (5 min) instead of using the 30 s default and EF's default.

### Open risks

- `MigrationLock` keeps its own statements and bounds, and `EnsureHeldAsync` still runs on the 30 s default. It is a cheap `APPLOCK_MODE` read. It was out of scope and is left unchanged.
- Connection-open timeouts (post-login, the 15 s `Connect Timeout`) are not addressed. They come from the deployment's connection string and are a separate failure class.
- Statements that Tellma.Core services run for the migrator keep their own bounds: the catalog store's registration and transitions, the version-tag seeder, and provisioning steps.
- The 25 s server-side wait comes from the engine, so a creation stuck behind a long-lived holder (for example a session left open in model) costs about 2.5 min before the tenant fails. The GaveUp message names that cause.
- The serial collection is new to this assembly. Any later suite that holds a server-wide lock belongs in it.

### What reviewers should examine hardest

- `CreateDatabaseRetry.IsTransient`: must 1807 be the only error, and is excluding a bare 1802 right?
- Whether the same-connection retry in `TargetDatabase.EnsureExistsAsync` is sound, including the 1801 race.
- The defaults in the bounds table, especially whether 30 min for RCSI with `ROLLBACK IMMEDIATE` and 1 h per migration statement are right for an elastic pool.
- The `Command Timeout` connection-string route to the identity store, which has no seam of its own.
- That `CreateDatabaseRetryPolicy()` is a method. A get-only property was read during binding or validation and threw for `CreateDatabaseAttempts = 0`, which hid the message naming the key.

## Review 1

Reviewed commit 77de323dec0bea872b944e3b281d66f54d2d084d. What I ran in this worktree:

- `dotnet build test/core/Tellma.Core.Tests`: Build succeeded, 0 warnings.
- `--filter FullyQualifiedName~Tellma.Core.Tests.Migrator`: 127 of 127 passed.
- `dotnet build test/core/Tellma.Core.IntegrationTests`: Build succeeded.
- `ModelLockTests` + `VersionTagSeedTests`: 7 of 8 passed. The model-lock convergence test failed.
- `ModelLockTests` alone: failed again, the same way.
- `ModelLockTests` alone with `Tellma__Catalog__RefreshInterval=00:04:00` in the environment: 2 of 2 passed. This confirms the cause in R1-1.
- `MigratorTests` alone: 29 of 29 passed.
- `dotnet build distributions/acme/test/Tellma.Distro.Acme.Web.Tests`: Build succeeded. Its unit filter: 27 of 27 passed.
- `dotnet format --verify-no-changes`, limited to the changed `.cs` files: clean.

Checks that came back clean:

- No EF model, snapshot or serialized migration changed.
- Every new member has XML docs.
- No spec or PC references in docs, messages, logs or test names.
- The READMEs were updated.
- The commit message lists the touched suites (PLAN §7.1 item 4): Tellma.Core.Tests, Tellma.Core.IntegrationTests and Tellma.Distro.Acme.IntegrationTests. Tellma.Distro.Acme.Web.Tests is unit-tier, so the unit filter covers it.
- The deviations against spec 0011 §6.1 and §8 are stated and justified. §8 keys are additive-only, and the identity store's connection string is the only change to a §6.1 contract.

The 1801-after-retry adoption adds no risk beyond the existing `DB_ID` check:

- Tenant creation runs under the tenant lock.
- `<DatabasePrefix>.<Id>` is unique.
- The fresh-database preparation is idempotent on a database that a timed-out creation left behind: `IF NOT EXISTS` RCSI, query store, `IF DATABASE_PRINCIPAL_ID(...) IS NULL CREATE USER`, and the role.

### Findings

**R1-1 (blocking): a catalog creation that the retry delays past the registry's first refresh tick succeeds, then fails at once with a cached 4060. `ModelLockTests`' convergence test fails 2 of 2 runs here.**

Where:
- `src/core/Tellma.Core.Migrator/Commands/MigrateCommand.cs:136-142`
- `src/core/Tellma.Core.Migrator/TellmaMigrator.cs:159-163`
- `src/core/Tellma.Core/Tenancy/TenantRegistryRefreshService.cs:63-70`
- `test/core/Tellma.Core.IntegrationTests/Migrator/ModelLockTests.cs:99-147`

Failure scenario:
1. The migrator host sets `LoadOnStart = false`, but it still starts `TenantRegistryRefreshService`. Its `PeriodicTimer` ticks every `Tellma:Catalog:RefreshInterval` (15 s by default) and opens the catalog connection string.
2. While the creation waits on `model`, each 1807 costs the server's own wait of about 20–25 s. So the first tick, at 15 s, opens a catalog that does not exist yet and fails with 4060.
3. Off Azure SQL Database, SqlClient's default `PoolBlockingPeriod=Auto` puts that connection string's pool into its error-blocking period: 5 s, doubling on each further failure up to 60 s. During that period every open on the pool rethrows the cached exception without contacting the server.
4. The retry then creates the catalog. `database.OpenAsync` at `MigrateCommand.cs:142` uses the same string, so it gets the cached error and the run ends `catalog: failed (SqlException: Cannot open database "ml_patient_…" requested by the login. The login failed.)`. That is exactly the failure both of my `ModelLockTests` runs show.

Setting the refresh interval to 4 minutes through the environment makes the same test pass, which isolates the cause. The implementer's single green run (49 s) passed only because of tick timing.

Impact:
- On SQL Server and LocalDB, which spec 0011 §6.7 supports for on-prem catalogs, the model-lock retry cannot make a delayed catalog creation converge. Any retry crosses the first tick, so the run fails right after creating the database.
- The ARCHITECTURE "one retry" claim does not hold for the catalog.
- The new integration test is red, or at best timing-dependent.
- The tenant path is unaffected, because nothing polls a tenant database before it exists.

Suggested fix:
- Keep the migrator's host from opening the catalog before the command has created it. For example:
  - give `TenantRegistryStartupOptions` a switch, set by `TellmaMigrator` next to `LoadOnStart = false`, so the loop stays idle until the first explicit `RefreshAsync`; or
  - have the loop skip its ticks while `LoadOnStart` is off and no load has happened.
- Alternatively or additionally, have the migrator's catalog connection string carry `PoolBlockingPeriod=NeverBlock`, so a failed open never poisons the opens that follow. This has to cover the lock and `CatalogDbContext` too, not only `TargetDatabase`, because they share the pool.
- Then make the test deterministic: set `Tellma:Catalog:RefreshInterval` to `00:00:01` in the patient harness, so it fails reliably without the fix and passes with it.

**R1-2 (minor): ARCHITECTURE.md and the README say every migrator statement is bounded, but several still run on SqlClient's 30 s default. `MigrationLock.EnsureHeldAsync`, which gates every write, is one of them.**

Where:
- `ARCHITECTURE.md:710` says "Every statement the migrator sends, beyond its locks' own, runs under an explicit … bound …, never the client's 30-second default".
- `src/core/Tellma.Core.Migrator/README.md:73` says the locks' statements "carry their own waits".
- `README.md:79` says "every read `status` makes".

Statements that stay on the 30 s default:
- `MigrationLock.EnsureHeldAsync` (`Sql/MigrationLock.cs:118`, a plain `APPLOCK_MODE` read with no wait of its own) and the release (`:144`).
- `status`'s `ITenantCatalog.ListAsync` (`Commands/StatusCommand.cs:52`).
- Every statement the migrator runs through Tellma.Core services: `TenantCatalog.ListAsync`, `RegisterAsync` and `SetStateAsync`; `TenantMembershipDirectory.RecordAsync`; `ProvisioningRecords`; the version-tag seeder's `SeedAsync` and `BumpAllAsync`; the registry's `RefreshAsync`.

On the loaded server that produced this item ("Execution Timeout Expired"), these can still fail a tenant at 30 s. The docs promise they cannot, and operators would tune `CommandTimeout` to no effect.

Suggested fix:
- Pass `CommandTimeout` into `MigrationLock` for `EnsureHeldAsync` and the release. They are the migrator's own statements, and the change is cheap.
- Reword ARCHITECTURE.md:710 and README:73/79 to "every statement the migrator's own code sends", and name the service-issued statements as running on their services' bounds, including `status`'s catalog listing.

**R1-3 (minor): no test would fail if most of the new bounds or the 1801-after-retry path broke.**

Where:
- `src/core/Tellma.Core.Migrator/Sql/TargetDatabase.cs:270-283` (`ScalarAsync`) and `:108-111`
- `Sql/SchemaFingerprints.cs:76,99`
- `Commands/MigrateCommand.cs:171`
- `Commands/StatusCommand.cs:48,145`

The gaps:
- Only `RunStatementAsync`'s bound is exercised, through the CREATE timeout test. Deleting the `CommandTimeout` assignment in `ScalarAsync` or `SchemaFingerprints`, or the catalog's `SetCommandTimeout(MigrationCommandTimeout)`, leaves every test green.
- Nothing drives an 1807 followed by an 1801, which the implementer notes call out as a case to examine. A regression that let the second attempt's 1801 escape, for example a catch moved inside the retry lambda, would go unnoticed.

Suggested tests:
- An integration test with `Tellma:Migrator:CommandTimeout = 00:00:01`, where an open transaction holds `TABLOCKX` on a migrated tenant's `dbo.__TellmaSchema`. The tenant must then fail with "Execution Timeout Expired" well under 30 s.
- Two harness runs on one catalog name started while `model` is held. Both retry, one creates the catalog, and the other adopts it through 1801 and converges. Once R1-1 is fixed, this also covers the pool path.

**R1-4 (minor): `ModelLockTests` is isolated only inside its own assembly. On a shared `TELLMA_TEST_SQL` server it fails other assemblies' fixtures.**

Where:
- `test/core/Tellma.Core.IntegrationTests/Infrastructure/SerialSuites.cs:13`
- `Migrator/ModelLockTests.cs:182-200`

`DisableParallelization` orders collections within Tellma.Core.IntegrationTests only. With `TELLMA_TEST_SQL` set to a developer's LocalDB or SQL Server, `dotnet test Tellma.slnx --filter "Category=Integration&…"` runs assemblies in parallel. Tellma.Distro.Acme.IntegrationTests honours the same variable. While the held `model` session lasts (about 25–50 s), the other assembly's fixture `CREATE DATABASE` fails with 1807. This looks like the "known local flake", but this test causes it deterministically. CI is unaffected: Linux gives each assembly its own container, and the Windows job runs the two suites one after the other.

Suggested fix: either skip the class unless the fixture owns its container (or an opt-in variable is set), or state in the integration-test README row and the class remarks that, against `TELLMA_TEST_SQL`, the integration suites must run one assembly at a time.

### Responses

- **R1-1: fixed in "PC-F48: review 1 fixes" (this commit, child of 77de323d).**
  - **Reproduced first.** `ModelLockTests` was pinned to `Tellma:Catalog:RefreshInterval = 00:00:01` before any fix. The convergence test then failed: the patient run's catalog creation succeeded, and its next open failed with the cached error of the registry's earlier tick (`catalog: failed (SqlException: Connection Timeout Expired … [Post-Login] complete=14157)`), a cached open failure of the same kind the reviewer saw. On that run, the earlier unpinned version passed only because of tick timing.
  - **Root cause fixed.** `TenantRegistryRefreshService` (Tellma.Core) now lets each tick pass while `TenantRegistryStartupOptions.LoadOnStart` is off and no snapshot has been loaded (`SnapshotLoadedAt is null`). The migrator host has no startup load, so its loop stays idle until the command's own forced refresh, which `migrate` runs only after creating and migrating the catalog. Nothing in the migrator reads the catalog before it exists.
  - **Why this layer.** The defect is a premature read, not the pool: a host that creates its catalog must not poll it first. Hosts that load at startup are unchanged. A web host whose first load failed still refreshes every tick, and a unit test pins that.
  - **Why it is right on each engine.**
    - On **SQL Server and LocalDB**, `PoolBlockingPeriod=Auto` blocks the pool after a failed open. With no open before the creation, nothing is cached.
    - On **Azure SQL Database**, `Auto` already means `NeverBlock` for `*.database.windows.net`. So the defect never failed a run there, only logged a failed refresh, and that refresh no longer happens either.
  - **Why not also `PoolBlockingPeriod=NeverBlock`.** Not added, deliberately.
    - It would mean rewriting the deployment's catalog connection string for every consumer in the migrator host (registry, catalog store, `CatalogDbContext`, the lock). That is a wide change for a cause that no longer exists.
    - The blocking period also has a legitimate job after a real fault, such as a failover mid-run: it keeps a client from hammering a server that is failing logins. A failed open then fails that database's work, and the next run converges.
  - **Tests.**
    - `TenantRegistryRefreshServiceTests` (unit): no read on three ticks before the host's own load, then a probe on the next tick; and, with a startup load, a reload on the tick after a failed first load. Mutation-verified: with the skip disabled, the first test fails.
    - `ModelLockTests`: every harness pins the 1 s refresh interval, so a tick always falls inside the about-25 s model wait. The convergence test also asserts that the patient run logged no `TenantSnapshotRefreshFailed`/`…Stale` and created its catalog exactly once (`MigratorDatabaseCreated`). The recorder now keeps every category's named events, not only the migrator's.
    - Green repeatedly: `ModelLockTests` passed 3 of 3 in each of three runs alone (1 m 49 s, 1 m 43 s, 1 m 42 s), and 39 of 39 beside `MigratorTests` and `VersionTagSeedTests` (all migrator classes, 9 m 42 s).
- **R1-2: fixed in "PC-F48: review 1 fixes" (this commit, child of 77de323d).**
  - `MigrationLock.AcquireAsync` takes a statement bound. `EnsureHeldAsync` and the release now run under it, and all three callers pass `Tellma:Migrator:CommandTimeout`. The take keeps its own command bound, 10 min plus 60 s, just past `sp_getapplock`'s wait.
  - ARCHITECTURE.md's "Bounded statements, one retry" and the migrator README now say "every statement the migrator's own code sends". They name the statements that run on their services' own bounds: the catalog store's listing (`status`'s and `migrate`'s), registrations and transitions, the membership hints, the provisioning records and steps, the version-tag seeder (its move on a failed run aside) and the registry's refresh. The README's `CommandTimeout` row now lists the locks' checks and releases, and `status`'s own reads (the fingerprints and the pending migrations) instead of "every read". Both docs also state the R1-1 rule: nothing reads the catalog before the command creates it.
  - The Tellma.Core README's `ITenantRegistry` row notes the deferred refresh.
- **R1-3: fixed in "PC-F48: review 1 fixes" (this commit, child of 77de323d).** Each new test is mutation-verified.
  - **`MigratorTests.A_statement_held_up_by_a_lock_fails_its_tenant_at_the_configured_bound`.**
    - Setup: a migrated tenant, then a rerun with `CommandTimeout = 00:00:02`, while an open transaction holds `dbo.__TellmaSchema` with `TABLOCKX, HOLDLOCK`.
    - The test waits until `sys.dm_exec_requests` shows the run's request in an `LCK` wait. It then times the rest of the run and asserts `tenant 1: failed (SqlException: Execution Timeout Expired` in under 20 s.
    - Mutation: with the `CommandTimeout` assignment removed from `SchemaFingerprints.RecordAsync`, it failed with "The run waited 00:00:30.03".
  - **`ModelLockTests.A_creation_that_finds_its_database_created_by_another_while_it_waited_counts_it_as_existing`.**
    - Setup: two `TargetDatabase.EnsureExistsAsync` calls for one database while `model` is held. Both log a retry, then the lock is released.
    - Assertion: exactly one returns "created" and the database exists. The other was told 1801 and counted it as existing.
    - Mutation: with the 1801 filter made never to match, the adopter failed with `SqlException : Database 'ml_adopt_…' already exists`.
  - **Why at the `TargetDatabase` level** and not two full `migrate` runs on one catalog name: two runs that both find a missing catalog also both treat it as fresh. Their concurrent `ALTER DATABASE … SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE` can then end each other's sessions. That race predates this item, since two runs racing past `DB_ID` already reach it, and the rerun converges. But it would make a whole-run test flaky without saying anything about the 1801 path. It is listed under open risks below.
  - **Not covered by a dedicated test:** the bounds of `TargetDatabase.ScalarAsync`'s metadata reads and of the catalog context's `SetCommandTimeout`. No lock can hold up `DATABASEPROPERTYEX`, `DB_ID` or `OBJECT_ID`, and a catalog migration that runs past 30 s would need a migration that exists only for the test.
- **R1-4: fixed in "PC-F48: review 1 fixes" (this commit, child of 77de323d).**
  - Every `ModelLockTests` test begins with `Assert.SkipUnless(sql.OwnsContainer, …)`. The skip message names `TELLMA_TEST_SQL` and how to run the suite.
  - `SqlServerFixture.OwnsContainer` is added in Tellma.Testing.Support. It is the same property, with the same text, that the sibling test-infrastructure fix adds, written here rather than cherry-picked, so the two merge cleanly.
  - On the Windows CI job, which uses LocalDB, the class is therefore skipped. It runs on Linux CI and locally on Testcontainers. The integration-test README row says so.

### Open risks added in review 1

- Two migrator runs creating one catalog at the same moment can still end each other's sessions through the fresh-database RCSI `ALTER … WITH ROLLBACK IMMEDIATE`. This predates this item. One run fails and a rerun converges.
