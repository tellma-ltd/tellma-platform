# D-3, D-6b, D-6c facts: the identity database, model collation, `migrate` owns `idsvr`

Research for decisions D-3, D-6b and D-6c of `.tmp/round-2/r5/decisions.md`, 28 October 2026.
Facts already verified in `.tmp/round-2/r4/facts-collation.md` are cited as "r4 §n" and not redone.
Every fact carries a marker:

- **[Verified: file:line]** — this repository (working tree) or the EF Core checkout of r4 §Sources
  (`C:\Users\ahmad\workspace\efcore`, `v10.0.9-58-g9d1b795935`, identical to `v10.0.9` for the
  files cited).
- **[Verified: URL]** — Microsoft Learn, read on 28 October 2026.
- **[Verified: probe Qn]** — observed by running the probe named in "Probes".
- **[Unverified]** — inference or memory; not confirmed by a source or a run.

Repository paths are relative to `C:\Users\ahmad\workspace\tellma\tellma-platform`. EF paths are
relative to the EF checkout.

## Probes

Code under `.tmp/round-2/r5/probes/` (isolated from the repository's build props), outputs under
`.tmp/round-2/r5/probes/evidence/`. EF Core 10.0.9, `dotnet-ef` 10.0.9 (the repository's local tool,
`.config/dotnet-tools.json`). Engines: LocalDB 15.0.4382.1 (`MSSQLLocalDB`), LocalDB 17.0.4025.3
(`MSSQLLocalDB17`), and a throwaway container of local image
`mcr.microsoft.com/mssql/server:2022-latest` (id `88ac6849d0a1`). Every probe database and the container were dropped afterwards.

- **Q1 `CollProbe` scaffolding.** A one-table model shaped like the identity engine's
  (`HasDefaultSchema("idsvr")`, no history-table override). `PROBE_STAGE` selects the model: 0 no
  collation, 1 `UseCollation("Latin1_General_100_CI_AS")`, 2 that plus a new column. Real `dotnet ef
  migrations add` at each stage: `Initial`, `PlatformCollation`, `AddNote`. `PlatformCollation`'s
  `Up` and `Down` were then emptied by hand; its Designer file and the snapshot were kept.
- **Q2 `CollProbe scenarios`** on all three engines (stage 1), and on LocalDB 15.0 at stage 2:
  (a) `Migrate()` on a missing database; (a') on an empty database created `COLLATE
  Latin1_General_100_CI_AS`; (b1) on an empty database in the server default; (b2) a server-default
  database migrated to `Initial` first, then `Migrate()`.
- **Q3 `CollProbe pending-check`.** The stage-2 model before `AddNote` existed.
- **Q4 `dotnet ef`** against LocalDB 15.0: `database update --connection` on an existing database
  and on a missing one; `migrations script --idempotent`.
- **Q5 `Rebaseline`.** The same model compiled into a second assembly with a freshly scaffolded
  `Initial` (new id), run against a database holding Q1's chain.
- **Q6 `HostProbe`.** A generic host whose first hosted service throws from `StartAsync`.
- **Q7 `SnapshotEdit`.** Q1's stage-0 `Initial` unchanged plus a snapshot hand-edited to carry
  `UseCollation`, no collation migration; then a stage-2 `migrations add`.
- **Q8 the real identity engine, read-only.** `dotnet ef migrations has-pending-model-changes` and
  `migrations list` with `--no-build` on `src/apps/Tellma.Identity.Migrations` (binaries of
  24 October 2026, newer than the last source change); `sqlcmd` reads of the developer's LocalDB.

## 1. The identity engine today

### 1.1 Model, constants and project references

- **`OnModelCreating` sets only the default schema**, then Identity, OpenIddict and the engine's
  configurations. No `UseCollation`. [Verified:
  `src/apps/Tellma.Identity/Data/TellmaIdentityDbContext.cs:23-35`]
- **The schema and migrations-assembly names are constants**: `Schema = "idsvr"`,
  `MigrationsAssemblyName = "Tellma.Identity.Migrations"`. [Verified:
  `src/apps/Tellma.Identity/TellmaIdentityConstants.cs:17`, `:26`]
- **The model's shape depends on an application service.** `TellmaIdentityModelDefaults`
  sets `IdentityOptions.Stores.SchemaVersion = Version3`; its doc says that without it the passkeys
  table "would silently vanish from generated migrations". [Verified:
  `src/apps/Tellma.Identity/Data/TellmaIdentityModelDefaults.cs:10-15`, `:20-26`]
- **The engine is a Razor class library on the ASP.NET Core shared framework** and references
  `Tellma.Core.Abstractions`. [Verified: `src/apps/Tellma.Identity/Tellma.Identity.csproj:1`,
  `:12`, `:34`]
- **The engine does not use the table-types library.** No reference to
  `Tellma.Core.EntityFrameworkCore`, and no `UseTableTypes`, table type or TVP in
  `src/apps/Tellma.Identity*`. [Verified: `Tellma.Identity.csproj:29-63`; grep for `TableType`,
  `UseTableTypes`, `SqlDbType.Structured`, `AS TABLE`, `READONLY`: none] So its contexts keep EF's
  stock differ and SQL generator. [Verified:
  `test/core/Tellma.Core.EntityFrameworkCore.Tests/Internal/EfInternalsPinningTests.cs:69-75`]

### 1.2 The migrations project

- **One migration**, `20260818225530_Initial` (`Migrations/20260818225530_Initial.cs` and
  `.Designer.cs`), and the snapshot
  `Tellma/Identity/Migrations/TellmaIdentityDbContextModelSnapshot.cs`. [Verified:
  `src/apps/Tellma.Identity.Migrations/` listing]
- **Neither carries a collation.** The snapshot's model builder has only `HasDefaultSchema("idsvr")`
  and the product-version annotations; `Initial.cs` has no `AlterDatabase`. [Verified:
  `TellmaIdentityDbContextModelSnapshot.cs:19-22`; `20260818225530_Initial.Designer.cs:22-25`;
  grep `AlterDatabase` in `Initial.cs`: 0]
- **`Initial` carries hand-written SQL that any regeneration must re-carry**: the
  `IX_OpenIddictTokens_CreationDate` index, "hand-carried through any regeneration of this file".
  [Verified: `20260818225530_Initial.cs:639-656`, `:665-666`]
- **The model and the chain are in step today.** "No changes have been made to the model since the
  last migration." [Verified: probe Q8]
- **The chain was re-baselined once already.** Commit `c2676a9` (18 August 2026, "Collapse the
  migration chain into one Initial") deleted `20260708055620_Initial`,
  `20260708064008_RateLimitCounters`, `20260805161750_AddUserGender`,
  `20260809051709_SessionPruneIndex` and `20260818004250_InvitationDispatchAndDelivery`.
  [Verified: `git log --name-status -- src/apps/Tellma.Identity.Migrations/Migrations`]
- **It is its own `dotnet ef` startup project**; the Design package is `PrivateAssets=all`.
  [Verified: `src/apps/Tellma.Identity.Migrations/Tellma.Identity.Migrations.csproj:4`, `:12-14`]
- **It brings the ASP.NET Core shared framework to whoever references it**, through
  `Tellma.Identity`: its built `runtimeconfig.json` lists `Microsoft.NETCore.App` and
  `Microsoft.AspNetCore.App`. [Verified: `csproj:8`;
  `src/apps/Tellma.Identity.Migrations/bin/Debug/net10.0/Tellma.Identity.Migrations.runtimeconfig.json`]
- **The standalone host references it "so runtime migration apply works".** [Verified:
  `src/apps/Tellma.Identity.Web/Tellma.Identity.Web.csproj:10-13`]

### 1.3 The design-time factory

- **Connection**: `TELLMA_IDENTITY_MIGRATIONS_SQL`, else LocalDB database `TellmaIdentityDesign`.
  The comment says the string matters only for `dotnet ef database update`. [Verified:
  `src/apps/Tellma.Identity.Migrations/TellmaIdentityDesignTimeDbContextFactory.cs:25-28`]
- **It builds the model as the runtime does**: `IdentityOptions` from `TellmaIdentityModelDefaults`
  through `UseApplicationServiceProvider`, and `MigrationsAssembly("Tellma.Identity.Migrations")`.
  [Verified: same file `:30-37`] Both the factory and `TellmaIdentityModelDefaults` are public.
  [Verified: `:20`; `TellmaIdentityModelDefaults.cs:16`]

### 1.4 The seeder

- **Order** in `SeedAsync`: (1) `MigrateAsync` when `Seed.ApplyMigrations`; (2) scopes; (3) seeded
  clients; (4) the development admin; (5) the bootstrap admin. There is no separate existence check:
  `MigrateAsync` itself creates a missing database (r4 §1.4). [Verified:
  `src/apps/Tellma.Identity/Services/Seeding/IdentitySeeder.cs:49-68`]
- **The first statement without migrations is a scope lookup** (`scopeManager.FindByNameAsync`).
  [Verified: same file `:59`, `:80`, `:99`]
- **In-proc branches.** Line 78: in-proc, the `tellma_api` scope's resource is the issuer origin;
  standalone, none (audiences arrive through provisioning). Line 85: the control-plane scope is
  seeded only when `Mode != InProc`. [Verified: same file `:75-78`, `:83-92`]
- **The development admin is seeded only in Development with `Seed.DevAdmin.Enabled`; the bootstrap
  admin only into an empty user store.** [Verified: same file `:165`, `:211`]
- **`Seed.ApplyMigrations` defaults to `false` in code.** Its doc says "Defaulted on in
  Development; explicit in production deployments", but nothing sets it per environment: no
  `PostConfigure`, no validator rule. [Verified:
  `src/apps/Tellma.Identity/Options/TellmaIdentitySeedOptions.cs:11-15`; grep `ApplyMigrations` and
  `PostConfigure` in `src/`]
- **Only configuration turns it on.** `Tellma.Identity.Web`'s `appsettings.Development.json` sets
  `true`; `appsettings.json` sets no `Seed` key, so production is `false`. [Verified:
  `src/apps/Tellma.Identity.Web/appsettings.Development.json:22-25`; `appsettings.json:32-40`]
- **Test hosts:** `true` in the database-backed standalone and in-proc factories and the E2E
  fixture; `false` in the bare `InProcFactory` and `StandaloneFactory`. [Verified:
  `test/apps/Tellma.Identity.IntegrationTests/Infrastructure/DatabaseBackedFactory.cs:26`, `:54`;
  `InProcFactory.cs:37`; `StandaloneFactory.cs:94`;
  `test/apps/Tellma.Identity.E2E/Infrastructure/IdentityServerFixture.cs:91`]

### 1.5 Registration and startup

- **Seeding is a hosted service registered first**, "before Quartz (hosted services start in
  registration order)". [Verified:
  `src/apps/Tellma.Identity/Hosting/TellmaIdentityServiceCollectionExtensions.cs:72-75`;
  `Services/Seeding/IdentitySeedHostedService.cs:19-24`]
- **A throwing `StartAsync` stops the host.** The host logs "Hosting failed to start",
  `StartAsync` rethrows, and later hosted services never start. [Verified: probe Q6]
- **The store.** `ConfigureDbContext` (the in-proc hook) wins; otherwise `UseSqlServer` with
  `MigrationsAssembly("Tellma.Identity.Migrations")`. [Verified:
  `TellmaIdentityServiceCollectionExtensions.cs:163-178`; `Options/TellmaIdentityOptions.cs:41-53`]
- **No migrations API works without the migrations assembly.** EF loads it with `Assembly.Load`
  when the context's migrations services are built. [Verified:
  `src/EFCore.Relational/Migrations/Internal/MigrationsAssembly.cs:38-40`]

### 1.6 In-proc startup today

The only in-proc host in the repository is the test asset, which binds the `TellmaIdentity`
section like the standalone host. [Verified:
`test/apps/assets/Tellma.Identity.TestInProcHost/Program.cs:27`] At startup:

1. `IdentitySeedHostedService` runs the seeder before any other engine hosted service (§1.5).
2. With `ApplyMigrations` on (every database-backed in-proc test, §1.4), `MigrateAsync` runs in the
   web process. It creates a missing database in the server default and applies the chain.
   [Verified: `IdentitySeeder.cs:53-57`; r4 §1.4]
3. With it off, the scope lookup is the first query. A missing database or missing `idsvr` tables
   make it throw, so the host does not start (§1.5). The SQL error numbers (4060 "Cannot open
   database", 208 "Invalid object name") are [Unverified]. A behind `idsvr` fails only where a
   seeded or later query touches a changed column [Unverified].

### 1.7 The developer's identity databases (D-4)

- **LocalDB `TellmaIdentity` is in the server default** (`SQL_Latin1_General_CP1_CI_AS`), with 19
  `idsvr` tables. [Verified: probe Q8]
- **Its history holds the pre-`c2676a9` chain** (`20260708055620_Initial`,
  `20260708064008_RateLimitCounters`), so today's `Initial` is already "(Pending)" against it.
  [Verified: probe Q8] By Q5's result, `MigrateAsync` would fail on it with Msg 2714 today
  [Unverified for this database: not run].
- **Read a LocalDB database's collation from inside it.** `DATABASEPROPERTYEX('TellmaIdentity',
  'Collation')` from `master` read `NULL` while the database was auto-closed;
  `DATABASEPROPERTYEX(DB_NAME(), 'Collation')` inside it read the value. [Verified: probe Q8; this
  refines r4 §5's LocalDB bullet]

## 2. `Tellma.Core.Abstractions`

- **It is BCL-only today.** The project file is empty: no package and no project reference.
  [Verified: `src/core/Tellma.Core.Abstractions/Tellma.Core.Abstractions.csproj:1-2`; the root
  `Directory.Build.props` and `Directory.Build.targets` add none]
- **Its README makes that a rule**: "No package references, ever … When a contract seems to need one
  (a hosting constant …), spell the value out instead." [Verified:
  `src/core/Tellma.Core.Abstractions/README.md:5`, `:24-26`]
- **It holds** the table-type row shapes, the email and webhook contracts, `DeploymentIdentity` and
  `ISandboxContext`. Constant classes already live there (`EmailTelemetryNames`). [Verified:
  `README.md:14-20`; `Email/EmailTelemetryNames.cs:19-28`]
- **Referenced today by** `Tellma.Identity`, `Tellma.Core`, `Tellma.Core.Email`,
  `Tellma.Core.Webhooks`, `Tellma.Core.Testing`, the three email adapters and test projects.
  [Verified: grep for `Tellma.Core.Abstractions.csproj` in `*.csproj`]
- **Not referenced by `Tellma.Core.EntityFrameworkCore`**, which "never references Tellma
  application projects" (frozen spec 0001). [Verified:
  `src/core/Tellma.Core.EntityFrameworkCore/Tellma.Core.EntityFrameworkCore.csproj:8-13`]
- **The specs keep it on the path of every platform model.**
  - `CatalogDbContext` and `TellmaDbContext` live in `Tellma.Core`, which references Abstractions.
    [Verified: `docs/specs/0011-distribution-host-and-multitenancy.md:123`;
    `docs/specs/0012-data-access-layer.md:111`; `src/core/Tellma.Core/Tellma.Core.csproj:3`]
  - `Tellma.Core.Migrator` references `Tellma.Core`. [Verified: spec 0011 `:125`]
  - Specs 0011 and 0012 give Abstractions one edge, to `Tellma.Core.Queryex`, which itself has
    "no PackageReference and no ProjectReference". [Verified: spec 0011 `:122`; spec 0012 `:110`;
    `src/core/Tellma.Core.Queryex/Tellma.Core.Queryex.csproj:8`]
- **So a `public const string` there is reachable from all three models and the migrator** with no
  new edge. [Verified: the facts above]
- **No spec names a collation constant today.** Spec 0011 and spec 0012 write the collation names
  as literals (`Latin1_General_100_CI_AS` in §6.7; `Latin1_General_100_BIN2` in §3.8, §7.3, §7.4,
  and spec 0012 §4.4). Abstractions namespaces the specs assign: `.Tenancy`, `.Composition`
  (spec 0011), `.Entities`, `.Data`, `.TableTypes` (spec 0012), `.Caching`, `.Settings`,
  `.Localization`, `.Calendars` (spec 0013). [Verified: grep for `Abstractions`, `BIN2`,
  `UseCollation` in specs 0011–0013; spec 0011 `:122`; spec 0012 `:110`; spec 0013 `:102`]
- **Generated code never reads the constant.** The scaffolder writes the literal
  `.UseCollation("Latin1_General_100_CI_AS")` into the Designer file and the snapshot. [Verified:
  probe Q1, `20261028162401_PlatformCollation.Designer.cs:23`, `ProbeContextModelSnapshot.cs:20`]

## 3. EF Core: adding `UseCollation` to a model that has migrations

### 3.1 What scaffolds

- **The next migration holds only `AlterDatabase`**: `Up` `AlterDatabase(collation:
  "Latin1_General_100_CI_AS")`, `Down` `AlterDatabase(oldCollation: "Latin1_General_100_CI_AS")`.
  [Verified: probe Q1, `evidence/PlatformCollation.scaffolded.cs.txt`; confirms r4 §1.6 with the
  real scaffolder]
- **Its script is `ALTER DATABASE … COLLATE` plus the history row.** [Verified: probe Q1,
  `evidence/PlatformCollation.scaffolded.sql.txt`; r4 §1.3]
- **The history table lands in `dbo`, not the model's default schema.** EF uses only
  `MigrationsHistoryTableSchema`; the script writes `[__EFMigrationsHistory]` unqualified. Every Q2
  database has `dbo.__EFMigrationsHistory`. [Verified:
  `src/EFCore.Relational/Migrations/HistoryRepository.cs:47`; probes Q1, Q2] The identity engine
  sets no history table (grep `MigrationsHistoryTable` in `src/`: none), so in in-proc mode its
  history is `dbo.__EFMigrationsHistory` in the catalog. [Verified: the facts above]

### 3.2 The emptied migration (`Up` and `Down` empty, Designer and snapshot kept)

- **No pending model changes.** `HasPendingModelChanges()` is `False`, so `Migrate()` raises no
  `PendingModelChangesWarning`. [Verified: probe Q1 `ops`, probe Q2] The check compares the
  snapshot with the model, never a migration's operations. [Verified:
  `src/EFCore.Relational/Migrations/Internal/Migrator.cs:379-395`, `:767-770`]
- **(a) A missing database** is created `COLLATE Latin1_General_100_CI_AS`, read-committed snapshot
  goes on, and both migrations apply with no warning. Columns take the platform collation. On all
  three engines. [Verified: probe Q2 (a), LocalDB 15.0, LocalDB 17.0, container 2022]
- **(a') An empty database created in the platform collation** gets no `CREATE DATABASE`; both
  migrations apply; read-committed snapshot stays off. [Verified: probe Q2 (a'), three engines]
- **(b) An existing server-default database** migrates with no error, whether empty (b1) or holding
  `Initial` (b2). Its collation stays `SQL_Latin1_General_CP1_CI_AS`, and so do its columns.
  [Verified: probe Q2 (b1), (b2), three engines] No log line of any Q2 run contains `ALTER DATABASE
  … COLLATE`. [Verified: probe Q2 logs]
- **(c) A later model change scaffolds no collation operation**: `AddNote` holds only `AddColumn` /
  `DropColumn`, and the full stage-2 chain applies in (a), (a'), (b1), (b2) with no `ALTER DATABASE
  … COLLATE`. [Verified: probe Q1 `evidence/AddNote.scaffolded.cs.txt`, `ops-stage2.txt`; probe Q2
  `scenarios-localdb15-stage2.txt`]
- **The idempotent script contains no database statement.** The emptied migration contributes only
  its history insert. [Verified: probe Q4, `evidence/idempotent-script.sql`]
- **The emptied `Down` matters too.** The scaffolded `Down` would alter the database back to the
  server default. [Verified: probe Q1; r4 §1.3]

### 3.3 Alternatives to the emptied migration

- **Re-baseline with a new id** (delete the chain, scaffold `Initial` again):
  - the new `Initial.cs` is identical to the old one except its namespace; the Designer file differs
    by the id and one `.UseCollation(…)` line; the new initial carries no `AlterDatabase`.
    [Verified: probe Q5 diff; r4 §1.2]
  - a database holding the old chain then fails: `GetAppliedMigrations` lists the old ids,
    `GetPendingMigrations` the new `Initial`, and `Migrate()` raises Msg 2714 "There is already an
    object named 'Items' in the database". The history table is left as it was. [Verified: probe
    Q5]
  - EF ignores applied ids it does not know and applies every unknown migration in id order.
    [Verified: `Migrator.cs:465-495`]
  - for the identity chain, the hand-written index must be re-carried (§1.2), and every existing
    identity database must be dropped. The developer's database is already behind a re-baseline
    (§1.7).
- **Edit the snapshot only, no new migration**:
  - `HasPendingModelChanges()` is `False`. `Migrate()` creates a missing database in the platform
    collation with read-committed snapshot. A later `migrations add` holds only `AddColumn`.
    [Verified: probe Q7]
  - an existing database sees no pending migration, because the id is unchanged. [Verified:
    `Migrator.cs:465-495`; not run]
  - `migrations remove` treats a last migration whose target model differs from the snapshot as
    "manually deleted" and rewrites the snapshot from that target model. With only the snapshot
    edited, that drops the collation again; editing `Initial.Designer.cs` the same way avoids it.
    [Verified: `src/EFCore.Design/Migrations/Design/MigrationsScaffolder.cs:263-268`, `:337-340`,
    `:366-385`]
  - editing both files gives exactly what re-scaffolding `Initial` with its old id would produce
    (probe Q5 diff), with `Up` and `Down` untouched. [Verified: probe Q5 diff]

### 3.4 APIs for a startup check or a test

- **`GetMigrations()`** reads the migrations assembly: no database. [Verified:
  `src/EFCore.Relational/Extensions/RelationalDatabaseFacadeExtensions.cs:29-30`]
- **`GetAppliedMigrations[Async]()`** returns an empty list, without throwing, when the database or
  the history table is missing. [Verified: same file `:43-45`, `:60-64`;
  `HistoryRepository.cs:129-138`, `:256-277`; probe Q2 (a)] SQL Server treats errors 4060, 1832 and 5120 as "does not exist".
  [Verified: `src/EFCore.SqlServer/Storage/Internal/SqlServerDatabaseCreator.cs:171`, `:293-294`]
- **`GetPendingMigrations[Async]()`** is `GetMigrations()` minus the applied ids. It lists every
  migration for a missing database and the unapplied tail for a behind one. [Verified: same file
  `:77-78`, `:93-97`; probe Q2 (a), (b2)]
- **`HasPendingModelChanges()`** compares the snapshot with the model and needs no database.
  [Verified: same file `:1031-1032`; `Migrator.cs:767-770`; probe Q1 `ops`] With an unscaffolded
  model change it is `True`, and `Migrate()` throws `PendingModelChangesWarning` before creating
  anything: the database did not exist afterwards. [Verified: probe Q3; `Migrator.cs:95-105`]
- **`IMigrationsAssembly.Migrations` and `CreateMigration(type, provider)`** expose each migration's
  `UpOperations` and `DownOperations` with no database. That is the shape of a test that no
  migration alters the database collation: no `AlterDatabaseOperation` whose `Collation` differs
  from `OldDatabase.Collation`. [Verified: probe Q1 `ops`, which prints the scaffolded
  `AlterDatabase(Collation=…, Old=<null>)` and the emptied `Up []`]
- **Presence alone is the wrong test.** An initial migration may carry an `AlterDatabaseOperation`
  holding only model annotations. [Verified: r4 §1.2]
- **The catalog and tenant models keep EF's initial-migration behaviour.** The table-types differ
  calls the base differ and only appends table-type operations. [Verified:
  `src/core/Tellma.Core.EntityFrameworkCore/TableTypes/Internal/EfCoreInternalsAdapter.cs:70-103`]
- **EF takes its own lock.** `Migrate()` runs `sp_getapplock @Resource = '__EFMigrationsLock',
  @LockOwner = 'Session'` in the target database. [Verified: probe Q4,
  `evidence/ef-database-update-existing.txt:59-61`]

## 4. Azure SQL Database: `ALTER DATABASE … COLLATE` on an unchanged collation

- **The Azure SQL Database syntax has no `COLLATE` branch.** Its branches are `MODIFY NAME`, `MODIFY
  (<edition_options>)`, `MODIFY BACKUP_STORAGE_REDUNDANCY`, `SET`, `ADD SECONDARY`,
  `PERFORM_CUTOVER`, `REMOVE SECONDARY`, `FAILOVER` and `FORCE_FAILOVER_ALLOW_DATA_LOSS`.
  [Verified:
  https://learn.microsoft.com/en-us/sql/t-sql/statements/alter-database-transact-sql?view=azuresqldb-current
  "Syntax" (Azure SQL Database)]
- **The SQL Server tab's `COLLATE` argument carries the note** "Collation can't be changed after
  database has been created on Azure SQL Database." [Verified: same page, SQL Server "Arguments"]
- **No page says what Azure does with a same-collation `ALTER DATABASE … COLLATE`**, whether
  accepted, a no-op, or an error. [Unverified: the page above, the "Set or change the database
  collation" page (r4 §2.3) and a Learn search have no such statement] On box SQL Server the same
  statement succeeds (r4 §1.7, LocalDB 15.0).
- **So the documentation gives no basis for letting that statement reach Azure.** [Verified: the
  facts above]

## 5. `dotnet ef database update` on an operator-created database

- **It is `Migrate()` with the given connection.** `--connection` sets the connection string on the
  design-time context, then `IMigrator.Migrate(target)` runs. [Verified:
  `src/EFCore.Design/Design/Internal/MigrationsOperations.cs:207-224`]
- **An existing database is never created or altered.** `Migrate()` calls `Create()` only when
  `Exists()` is false. [Verified: `Migrator.cs:102-105`]
- **Run on an empty database created in the platform collation**, it ran `SELECT 1`, the EF lock,
  the history table, `CREATE SCHEMA [idsvr]`, the tables and the history rows. No `CREATE
  DATABASE`, no `ALTER DATABASE`; collation unchanged; read-committed snapshot left off. [Verified:
  probe Q4, `evidence/ef-database-update-existing.txt`]
- **Run on a missing database**, it created it `COLLATE Latin1_General_100_CI_AS` with
  read-committed snapshot, as `Migrate()` does. [Verified: probe Q4,
  `evidence/ef-database-update-missing.txt:41-57`]
- **It never checks the collation of an existing database.** [Verified: r4 §1.5; probe Q2 (b1)]
- **On Azure SQL Database**, the operator's database exists, so EF issues no `CREATE DATABASE` and
  none of its Azure gaps (r4 §1.4) apply. [Verified: `Migrator.cs:102-105`] That the identity chain
  runs on Azure as on box SQL Server is [Unverified]; nothing in it is Azure-specific (§1.2).
- **The operator's command**, from the migrations project's own description: `dotnet ef database
  update --project src/apps/Tellma.Identity.Migrations --startup-project
  src/apps/Tellma.Identity.Migrations --connection "<connection string>"`, or the
  `TELLMA_IDENTITY_MIGRATIONS_SQL` variable instead of `--connection`. [Verified:
  `Tellma.Identity.Migrations.csproj:4`; `TellmaIdentityDesignTimeDbContextFactory.cs:27-28`;
  `dotnet ef database update --help`] It needs a checkout, the .NET SDK and `dotnet tool restore`
  (`dotnet-ef` 10.0.9). [Verified: `.config/dotnet-tools.json`]
- **`dotnet ef migrations script --idempotent`** gives a script that `sqlcmd` or SSMS can run
  instead. It holds no database statement. [Verified: probe Q4]

## 6. Specs 0011 and 0010 today

### 6.1 What the web host checks at startup

- **§3.4 compares only the catalog's history**: "the catalog's migration history is compared with
  the platform's latest catalog migration". A behind or missing catalog fails startup with the exact
  migrator command; "the web host never migrates". A reachability failure starts the host
  not-ready, so a slot swap "must not crash-loop every instance". [Verified:
  `docs/specs/0011-distribution-host-and-multitenancy.md:697-703`]
- **No spec says how the web host learns the latest catalog migration.** The catalog migrations ship
  in `Tellma.Core.Migrator`, which the Web project does not reference. [Verified: spec 0011 `:125`,
  `:200-203`; grep "latest catalog migration": only `:698`]
- **The schema fingerprint covers tenant databases only.** `dbo.__TellmaSchema` and the
  `tenant-schema-behind` verdict guard every tenant round trip. Nothing guards the catalog or
  `idsvr` this way. [Verified: `docs/specs/0012-data-access-layer.md:724-741`; spec 0011
  `:648-657`]
- **§6.6**: "A missing catalog at web startup prints that exact `migrate` command instead of a stack
  trace." [Verified: spec 0011 `:1822`]
- **§5.7 orders the catalog check before the engine's seeding**, "so a missing catalog prints the
  `migrate` command". [Verified: spec 0011 `:1519-1521`]

### 6.2 How a missing or behind `idsvr` surfaces under the specs as written

- **A missing catalog** is caught by §3.4 first. [Verified: spec 0011 `:697-703`, `:1519-1521`]
- **A present catalog with a missing or behind `idsvr`** is checked by nothing. §3.4 reads only the
  catalog's history, and `idsvr`'s history is a different table (§3.1).
  - With `Seed.ApplyMigrations` off, the seeder's first query throws and the host fails to start
    with a stack trace (§1.5, §1.6), unlike §6.6's printed command.
  - With it on, the web process migrates `idsvr`. That breaks §3.4's "the web host never migrates"
    and Decision 21's "DDL rights never reach the web identity". [Verified: spec 0011 `:701`,
    `:2364-2366`]
- **An unreachable catalog in in-proc mode crashes startup**, against §3.4's reachability rule. The
  seeder's query throws from a hosted service. [Verified: §1.5, §1.6; spec 0011 `:701-703`]

### 6.3 Who migrates `idsvr` today

- **§5.7** says the engine's migrations migrate `idsvr`, "the one schema the web process may
  migrate". [Verified: spec 0011 `:1518-1520`]
- **§6.1's `migrate`** also migrates the catalog "(`idsvr` too in in-proc mode)". [Verified: spec
  0011 `:1695`]
- **§5.7's option list does not mention `Seed.ApplyMigrations`.** `AddTellma` configures the engine
  in code from `Tellma:Identity:*`, so the code default `false` applies (§1.4). [Verified: spec 0011
  `:1509-1513`, `:1192-1193`, and the `Identity` row of §8]
- **§3.2**: the catalog holds "in in-proc identity mode, the identity engine's `idsvr` schema with
  its own migration history". [Verified: spec 0011 `:597-598`]
- **`ARCHITECTURE.md`**: migrations are "never run in the web process". [Verified:
  `ARCHITECTURE.md:678`]
- **Spec 0010 §A.11's in-proc bullet**: "The engine never creates it: a missing database fails
  seeding". That holds only with `Seed.ApplyMigrations` off: with it on, `MigrateAsync` creates the
  database. [Verified: `docs/specs/0010-amendments-to-implemented-specs.md:571-573`; §1.6]
- **The project references block the migrator from migrating `idsvr`.**
  - The distribution's Migrator references only `Tellma.Distro.Acme` and `Tellma.Core.Migrator`,
    "so a migrator build carries no ASP.NET package".
  - The Web project references `Tellma.Identity` but not `Tellma.Identity.Migrations`.
  - So neither project, as specified, can run the identity chain. [Verified: spec 0011
    `:200-205`; §1.2, §1.5]

## Implications

### D-3: the operator creates and migrates the shared identity database

- **The manual path is sound.**
  1. The operator runs spec 0011 §6.7's statement in `master`, the Azure form on Azure SQL
     Database.
  2. The operator runs `dotnet ef database update --project src/apps/Tellma.Identity.Migrations
     --startup-project src/apps/Tellma.Identity.Migrations --connection "<…>"`.
  3. EF finds the database existing, so it neither creates nor alters it, and it applies the chain
     (§5).
- **The spec states two consequences of that path.**
  - **Nothing checks the collation** unless someone reads it: `dotnet ef` never does (§5).
    §A.11's refusal runs "whenever the seeder migrates", and production has
    `Seed.ApplyMigrations` off (§1.4), so the refusal never sees the production database. Either
    the seeder reads the collation at every start (one query, before seeding), or the operator's
    step includes the check.
  - **Read-committed snapshot stays off on box SQL Server** (§5, probe Q4). Azure turns it on by
    default (spec 0011 §6.7 calls it a no-op there; [Unverified] here).
- **The operator's tooling is a checkout, the SDK and `dotnet tool restore`.** An idempotent script
  is the alternative (§5).
- **One Non-goals line covers the later convenience tools.**

### D-6b: every model sets the collation; nothing ever alters it

- **The owner of the value can be a `public const string` in `Tellma.Core.Abstractions`.** It is
  BCL-only, already referenced by `Tellma.Identity`, and on the path of `CatalogDbContext`,
  `TellmaDbContext` and the migrator through `Tellma.Core` (§2). The specs name no namespace for
  such a constant yet. The existing `*TelemetryNames` classes are the precedent for a constants
  class.
- **Snapshots and Designer files carry the literal, not the constant** (§2). A model test asserts
  `Model.GetCollation()` equals the constant. The no-alter test (below) covers the generated files.
- **Catalog and tenant models need no extra machinery.** Their first migrations carry no collation
  operation (r4 §1.2; §3.4 for the table-types differ).
- **The identity model needs one of three changes, and the least machinery is the emptied
  migration.**
  - **What it is.** Scaffold the migration that `UseCollation` produces, empty its `Up` and
    `Down`, and keep its Designer file and the snapshot.
  - **What it guarantees.** No `PendingModelChangesWarning`, and a missing database created in the
    platform collation. An existing database is never touched. Later migrations carry no collation
    operation. The idempotent script holds no database statement. Probed on LocalDB 15.0, LocalDB
    17.0 and SQL Server 2022 (§3.2).
  - **What it costs.** Two generated files with an explanatory comment, and one more history row.
  - **What it avoids.** It needs no re-carrying of the hand-written index and invalidates no
    database.
- **The two alternatives also work under D-4.**
  - **Re-baseline with a new id** (§3.3). It is the precedent `c2676a9` set. Every existing identity
    database fails with Msg 2714 and must be dropped; the developer's already is behind (§1.7). The
    index SQL must be re-carried.
  - **Add `.UseCollation(…)` by hand to `Initial.Designer.cs` and the snapshot** (§3.3). This adds
    no file and no history row, but it edits generated files by hand.
- **The pinning test runs without a database.** For each platform migrations assembly it walks
  `IMigrationsAssembly.Migrations` and fails on any `AlterDatabaseOperation`, in `Up` or `Down`,
  whose `Collation` differs from `OldDatabase.Collation`. It also asserts `HasPendingModelChanges()`
  is false (§3.4).
- **§A.11's `master`-connection creation path can go.** With `UseCollation`, `MigrateAsync` creates
  a missing development database `COLLATE Latin1_General_100_CI_AS` with read-committed snapshot
  (§3.2 (a)). EF's `Create()` writes no Azure options or `CATALOG_COLLATION` (r4 §1.4). So EF's
  creation is fit for development and tests only, which D-3 already implies.
- **The existing-database refusal still needs its own read.** `Migrate()` leaves a server-default
  database as it is, columns included (§3.2 (b)).
  - For the catalog, the migrator's §6.7 check covers it.
  - For a standalone engine, the seeder reads `DATABASEPROPERTYEX(DB_NAME(), 'Collation')` from
    inside the database, not from `master` (§1.7).

### D-6c: `migrate` owns `idsvr`; the web process never migrates

- **`AddTellma` must pass `Seed.ApplyMigrations = false` explicitly**, even though it is the code
  default. The property's doc claims a Development default (§1.4), and §5.7 lists what `AddTellma`
  passes.
- **Seeding still runs at in-proc startup with migrations off.** It writes the scopes, the seeded
  clients (the `Distribution` entry), the development admin (Development only) and the bootstrap
  admin (empty store only) into `idsvr` (§1.4). That is DML only, no DDL.
- **The migrator must build the identity context as the design-time factory does**:
  `IdentityOptions` from `TellmaIdentityModelDefaults` through the application service provider, and
  `MigrationsAssembly("Tellma.Identity.Migrations")`. Without that, the passkeys table leaves the
  model, `HasPendingModelChanges()` turns true, and `Migrate()` throws (§1.1, §1.3, §3.4).
- **It must also leave `UseTableTypes` off** (§1.1), and the spec states where `idsvr`'s history
  lives: `dbo.__EFMigrationsHistory` in the catalog unless the context sets
  `MigrationsHistoryTable` (§3.1).
- **That needs a reference the specs forbid today.** The migrator must reference
  `Tellma.Identity.Migrations`, which brings the ASP.NET Core shared framework (§1.2). That
  contradicts spec 0011 §1.2's "a migrator build carries no ASP.NET package" (§6.3).
  - **Resolution A.** The spec allows that framework reference for distributions that run in-proc.
  - **Resolution B.** The migrator runs a build-time idempotent script of the identity chain
    (§5). That costs a build step and a parity test.
  - Ahmad picks one.
- **A web host facing a missing or behind `idsvr` needs a startup check**, or it crashes with a
  stack trace (§6.2). The least machinery mirrors §3.4: in in-proc mode, compare `idsvr`'s history
  with the engine's latest migration, after the catalog check and before `IdentitySeedHostedService`
  starts, and print the same `migrate` command.
  - `GetPendingMigrationsAsync()` on the identity context does this in one query and does not throw
    for a missing history table (§3.4).
  - It needs `Tellma.Identity.Migrations` in the Web project (§1.5), as `Tellma.Identity.Web`
    already has (§1.2).
  - The alternative is to publish the latest id as a constant.
  - Either way the spec says where the web host gets the latest id. §3.4 leaves this open for the
    catalog too (§6.1).
- **An unreachable catalog must not crash an in-proc host** (§3.4). Today the seeder's hosted
  service throws (§6.2). The spec says whether seeding waits for, or is skipped until, readiness.
- **Revise every place that says the web process or the engine migrates `idsvr`.** These are spec
  0011 `:1518-1520` and `:1695`, which disagree with each other; spec 0010 `:571-573`; and the
  `idsvr` and engine wording in `ARCHITECTURE.md`. With the web process never migrating, §3.4,
  Decision 21 and `ARCHITECTURE.md:678` hold with no exception.
