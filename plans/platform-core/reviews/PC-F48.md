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
