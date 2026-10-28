# D-2 inventory: database creation and collation-sensitive constructs

Scope: every place a database is created or assumed, and every collation-sensitive construct, in
specs 0010–0035 and `ARCHITECTURE.md` (editable), specs 0001–0009 (frozen), and the code under
`src`, `test`, `eng`, `infra` (read-only; `distributions/` does not exist yet, and `infra/` holds
only `monitoring/`, no Bicep). Line numbers are from the working tree on 27 October 2026.

Markers:

- **[spec]** an edit to an editable spec or `ARCHITECTURE.md`.
- **[via 0010 Part X]** a frozen spec; the change goes into spec 0010's part for it.
- **[code]** code follows the amended spec; no spec edit.
- **[none]** checked; no edit needed (listed so the next agent need not re-check).

Proposed new section numbers: spec 0011 **§6.7** (the owner rule) and spec 0010 **§A.11** (the
identity amendment). Rename freely; every pointer below uses these two.

## 1. Verified facts the recommendations rest on

- **F1. Azure's catalog collation defaults to the server default.** On Azure SQL Database,
  `CREATE DATABASE x COLLATE Latin1_General_100_CI_AS` sets the *data* collation only; system
  metadata (`sys.*` names) stays `SQL_Latin1_General_CP1_CI_AS` unless the statement adds
  `WITH CATALOG_COLLATION = DATABASE_DEFAULT`. The clause follows the edition options:
  `CREATE DATABASE x COLLATE … ( EDITION = …, SERVICE_OBJECTIVE = ELASTIC_POOL(name = …) ) WITH
  CATALOG_COLLATION = DATABASE_DEFAULT`. Box SQL Server, LocalDB and the container have no such
  option: their metadata always follows the database collation.
- **F2. Azure never changes either collation after creation.** Data and catalog collation are
  fixed at `CREATE DATABASE`; Azure's `ALTER DATABASE` has no `COLLATE`. A copy (`AS COPY OF`) or a
  restore keeps the source's collations.
- **F3. Batch-local tables.** A table-variable column without `COLLATE` takes the current
  database's collation. A `#temp` table column takes `tempdb`'s (the server default) in a
  non-contained database. TVP columns take their table type's column collation, which a type
  without `COLLATE` fixed from the database default when the type was created.
- **F4. EF Core 10 creates databases implicitly.** `Migrate()` and `EnsureCreated()` on a missing
  database run `CREATE DATABASE [name]`, adding `COLLATE` only when the model calls
  `UseCollation`, never Azure edition, pool or catalog options (efcore `release/10.0`:
  `SqlServerDatabaseCreator.CreateCreateOperations`, `SqlServerMigrationsSqlGenerator` for
  `SqlServerCreateDatabaseOperation`). A model-level `UseCollation` also scaffolds an
  `AlterDatabase` migration that emits `ALTER DATABASE … COLLATE`, which F2 rules out on Azure
  and which leaves existing columns on the old collation everywhere.
- **F5. Bicep.** `Microsoft.Sql/servers/databases` has `properties.collation` and
  `properties.catalogCollation` (`'DATABASE_DEFAULT' | 'SQL_Latin1_General_CP1_CI_AS'`).
- **F6. Verification query.** `DATABASEPROPERTYEX(DB_NAME(), 'Collation')` everywhere;
  `sys.databases.catalog_collation_type_desc` on Azure SQL Database only.
- **F7. Code page unchanged.** `Latin1_General_100_CI_AS` and `SQL_Latin1_General_CP1_CI_AS` both
  use code page 1252 for `varchar`, so `varchar` enum, code and language columns and spec 0008's
  ASCII literal adoption (0008 §11, line 2076) behave the same. Ethiopic is in the BMP, so the
  non-`_SC` platform collation and the Queryex fixture's `_SC` column collation agree on Amharic.
- **F8. Servers keep their default.** LocalDB's server collation is fixed at
  `SQL_Latin1_General_CP1_CI_AS`; the 2022 container uses it unless `MSSQL_COLLATION` is set, and
  so does Azure. Only the per-database `COLLATE` reaches every environment.

## 2. The owner rule (spec 0011 §6.7, new) — the text every other item points at

**[spec] spec 0011, new §6.7 "Database creation and collation"** (after §6.6, so nothing
renumbers). Proposed text:

> Every database the platform creates uses the collation `Latin1_General_100_CI_AS`: the catalog,
> every tenant database, and the development and test databases that stand in for them. The
> identity server's database follows the same rule (spec 0010 §A.11).
>
> - **Who creates.** The migrator creates every catalog and tenant database. `migrate` creates a
>   missing catalog before it takes the catalog lock; `provision` creates the tenant database
>   (§6.1). EF's `Migrate()` never creates a database, and no model sets a database collation.
> - **The statement.** `CREATE DATABASE [<name>] COLLATE Latin1_General_100_CI_AS`. On Azure SQL
>   Database the template's edition options follow (§8), then `WITH CATALOG_COLLATION =
>   DATABASE_DEFAULT`, so `sys.*` names compare in the same collation as data.
> - **An existing database.** The migrator refuses one with another collation, or on Azure with
>   another catalog collation, naming the database and both collations. Columns keep the
>   collation they were created with and Azure cannot change it, so nothing alters it.
> - **Copies.** A sandbox clone (§6.3) keeps its source's collations.
> - **Test servers.** Fixtures create their databases with the same statement. The server's own
>   collation, and with it `tempdb`'s, stays at its default, as on Azure SQL Database.

## 3. Editable specs

### Spec 0011 (owner of the catalog and tenant rule)

- **[spec] §3.2, 0011:588-595.** Today: the catalog is "reached through
  `Tellma:Catalog:ConnectionString` … migrated by the migrator before any tenant database"; nobody
  is named as its creator in SaaS (`tellma-<slug>-catalog`). Edit: "created (§6.7) and migrated by
  the migrator before any tenant database". See Gap 1.
- **[spec] §3.4 Startup, 0011:691-697.** Today: a missing catalog fails web startup with the
  `migrate` command. No edit; it already keeps the web host from creating the catalog.
- **[none] §3.5, 0011:730-735, 757-759.** Development profiles (LocalDB, container `sa`) and "never
  issues `USE` or `ChangeDatabase`". No edit: this is why no cross-database query exists.
- **[spec] §5.7, 0011:1503-1508.** Today: the in-proc identity store is the catalog connection,
  schema `idsvr`, "migrated by the identity engine's own migrations — the one schema the web
  process may migrate". Edit: add "The engine never creates the catalog: a missing catalog fails
  identity seeding as it fails startup (§3.4)". It inherits §6.7 through the catalog. See Gap 5.
- **[spec] §6.1 `migrate` row, 0011:1681.** Today: "Migrates the catalog under
  `sp_getapplock('tellma:migrate:catalog')`". Edit: "Creates a missing catalog (§6.7), then
  migrates it under …".
- **[spec] §6.1 `provision` row, 0011:1682.** Today, inline: "`CREATE DATABASE … COLLATE
  Latin1_General_100_CI_AS` (Azure: followed by `(EDITION = …, SERVICE_OBJECTIVE =
  ELASTIC_POOL(name = …))` from `Tellma:Provisioning:CreateDatabaseTemplate`; on-prem: nothing
  more), failing when the database already exists with another collation". It omits
  `CATALOG_COLLATION`, so on Azure the table-type sweep breaks (§7, item S1). Edit: replace the
  whole clause with "creates the database (§6.7);". The statement then lives once, in §6.7.
- **[spec] §6.3, 0011:1764-1766.** Sandbox cloning (`AS COPY OF`, backup-restore). No edit if §6.7
  carries the "Copies" bullet; otherwise add "A clone keeps its source's collations".
- **[spec] §6.6, 0011:1800-1802, 1809.** Today: "`migrate` creates `Tellma.dev.acme.catalog`".
  Edit: none required once §6.7 says `migrate` creates a missing catalog; optionally "(§6.7)"
  after "creates".
- **[spec] §8, 0011:1991 and 2013.** Today: `"Provisioning": { "CreateDatabaseTemplate": null }`
  and the row "§6.1; `{database}` is substituted." Edit the row: "§6.7: the Azure edition options
  of every database the migrator creates, catalog and tenants;" and settle what `{database}` is
  for. See Gap 4.
- **[spec] §10 tiers, 0011:2126-2131.** Today: LocalDB on Windows, Testcontainers
  `mssql/server:2022-latest` on Linux. Edit: add "Every suite creates its databases as §6.7
  states; servers keep their default collation."
- **[spec] §10 table, 0011:2138 (`Tellma.Core.IntegrationTests`).** Today: "Catalog migrations
  apply from empty and are idempotent". Edit: add "`migrate` creates a missing catalog with the
  platform collation and refuses an existing one with another collation".
- **[spec] §10 table, 0011:2141 (`Tellma.Distro.Acme.IntegrationTests`).** Today: "`migrate` then
  `provision` … from an empty server". Edit: add "the catalog and every tenant database report
  `Latin1_General_100_CI_AS`".
- **[spec] §10 fixture, 0011:2169-2170.** Today: "the catalog database is created per test class
  from the platform migrations". This reads as EF's implicit creation (F4). Edit: "is created per
  test class as §6.7 states, then migrated".
- **[spec] Decision 32, 0011:2325-2327.** Today: "Every tenant database is created with
  `Latin1_General_100_CI_AS` — … (§6.1)". Edit to: "**Every database the platform creates uses
  `Latin1_General_100_CI_AS`** — the server default `SQL_Latin1_General_CP1_CI_AS` defines no
  order on Amharic text, and one collation lets the catalog, the identity store, every tenant and
  every test compare text alike. The collation is set by `CREATE DATABASE` and never altered:
  columns keep their creation collation and Azure cannot change it; Azure's catalog collation
  matches the data's so `sys.*` names compare like data (§6.7)."
- **[spec] Context, 0011:46-56.** Add "spec 0010 §A.11 — the identity database's collation,
  following §6.7".
- **[none] §7.3, 0011:1857, 1862-1865; §7.4, 0011:1871-1873.** Explicit `COLLATE
  Latin1_General_100_BIN2` on `TenantMemberships.Subject`, the `TenantMembershipList` type's
  `Subject`, `Sessions.Key`, `Handle`, `Subject`. Purpose: case-sensitive exact match of `sub`,
  session keys and handles. The type's explicit collation is load-bearing: the upsert joins
  `r.[Subject] = m.[Subject]` (0011:1934, 1938, 1940), which would raise Msg 468 if the type took
  the database's CI collation. Keep.

### Spec 0010 (editable parts that are not amendments of the identity rule)

- **[spec] Context, 0010:31-32.** Add "§6.7" to "spec 0011 §5.5, §5.7, §5.9 — the distribution
  side of Part A", as "§6.7 — the collation rule §A.11 follows".
- **[spec] Goals Part A, 0010:57-61.** Append "and the identity database created with the platform
  collation".
- **[none] 0010:27.** "Specs 0001, 0007 and 0009 need no amendment." Stays true: spec 0001's text
  never fixes a database collation, so its fixture can follow spec 0011 §6.7 as code.
- **[spec] §F.15, 0010:3499-3500.** Today: "it agrees with the tenant collation (spec 0011 §6.1)".
  After the move this cites a section that no longer states the rule. Edit: "it agrees with the
  platform collation (spec 0011 §6.7)".
- **[none] §F.0, 0010:2696; §F.15, 0010:3424, 3427, 3603-3605, 3694.** Text ties "under the
  fixture's case-insensitive collation" and QX4010 for collation-dependent text. D-1: no edit.

### Spec 0012

- **[spec] §1.4, 0012:185-204.** Today: reserves table-variable prefixes; no statement uses a
  `#temp` table (verified across specs 0010–0035). Add one bullet: "**No `#temp` tables.**
  Batch-local tables are table variables, whose string columns take the database collation
  (spec 0011 §6.7); a `#temp` table would take `tempdb`'s. Distribution raw SQL that creates one
  declares every string column `COLLATE DATABASE_DEFAULT`." Optionally extend `TELLMA0003`.
- **[spec] §13.2, 0012:2010-2011.** Today: "The database is created with the tenant collation and
  `READ_COMMITTED_SNAPSHOT ON`, as spec 0011's provisioning does (§6.1)" (line 2011 also runs past
  130 columns). Edit: "The database is created as spec 0011 §6.7 states, with
  `READ_COMMITTED_SNAPSHOT ON` as provisioning sets it (spec 0011 §6.1)", rewrapped.
- **[none] §13.1, 0012:1994-1997; §1.1 row, 0012:114; Goals, 0012:78-79.** `TELLMA_TEST_SQL`,
  LocalDB, Testcontainers. No edit; §13.2 carries the pointer.
- **[none] §8.1 tables, 0012:1351-1354; §10.2, 0012:1656.** Table variables: all `int`, `char(1)`
  or `hierarchyid`, plus `@tb3_paths.[Path] varchar(892)` (digits and slashes, never compared with
  a table column). Safe under F3.
- **[none] §4.4, 0012:766; spec 0013:403-404.** `StringList` (`nvarchar(450)`) and
  `VersionTagList.Name` joined to `core.VersionTags.Name`. Safe: type and table columns both took
  the database collation, which never changes (Decision 32).
- **[flag] §8.1, 0012:1394, 1404; §8.2, 0012:1435-1438.** Not caused by D-2, but
  collation-sensitive: change detection is `EXISTS (SELECT s.[Name] … EXCEPT SELECT t.[Name] …)`
  under the column collation. Under any CI collation a case-only edit ("acme" → "Acme"), and under
  `Latin1_General_100_CI_AS` a width- or kana-only edit, counts as unchanged: the `UPDATE` skips
  the row and the edit is lost. Possible edit to the "Unchanged rows are skipped" bullet: "string
  columns compare under `COLLATE Latin1_General_100_BIN2` inside the `EXCEPT`, so an edit that
  changes only case or width is a change", plus a §13.3 assertion. Needs Ahmad's call (Gap 9).

### Specs whose suites use spec 0012's fixture database

- **[none]** spec 0013:1915-1916, spec 0014:107 and 1768, spec 0015:136 and 2571 ("one database
  per class"), spec 0017:1156 and 1182, spec 0018:1240-1241 and 1303, spec 0019:1735-1736, spec
  0020:1854-1855, spec 0021:1308 and 1312, spec 0022:566, spec 0028:1174. Each says "LocalDB or
  Testcontainers" over spec 0012's fixture; spec 0012 §13.2 owns how it is created. No edit.
- **[none] spec 0018:1290-1291.** "`migrate` then `provision` from an empty server" in the Acme
  suite; the collation assertion belongs to spec 0011 §10's row for the same suite.
- **[spec, low] spec 0019 §10.3, 0019:1216-1218.** Today: "a collation equivalence beyond case:
  accent- or width-insensitivity". `Latin1_General_100_CI_AS` is accent-sensitive, so accents
  never match; width, kana and ignorable characters do. Edit: "a collation equivalence beyond
  case, such as width, kana or an ignorable character".

### Explicit collations and client-side collators in other specs

- **[none] spec 0014:305, 1826, 1950.** `core.Users.Subject varchar(255) COLLATE
  Latin1_General_100_BIN2`: case-sensitive `sub` match in the prologue seek. Keep; spec 0001
  copies the column collation into the users table type, so TVP joins stay conflict-free.
- **[none] spec 0027:461, spec 0029:848 and 1236-1237, spec 0032:288, spec 0034:665.**
  `Intl.Collator` ordering on the client and the picker's client-side search that "may differ
  slightly" from SQL. Not database collation. No edit.

## 4. Frozen specs (changes go through spec 0010)

### Spec 0003 — via spec 0010 Part A, new §A.11

- **[via 0010 Part A] 0003:80 (§2.2 Store row).** "Shared Identity database" / "The
  distribution's own database (dedicated schema)". Nothing about collation or who creates it.
- **[via 0010 Part A] 0003:720-722 (§12).** "Backing store is SQL Server / Azure SQL". Nothing
  about creation or collation.
- **[via 0010 Part A] 0003:604-615 (§10.4 Local development).** First run seeds the dev admin; "a
  local database reset re-seeds". Creation is implicit (EF, F4).
- **[via 0010 Part A] 0003:843-855 (§16).** Integration "against real SQL"; "migrations" pinned.
  No collation assertion.
- **[none] 0003:65.** `Tellma.Identity.Migrations` "kept separate so hosts opt into schema
  management". It gains no migration under §A.11 (no model collation, F4).

Proposed **§A.11 "The identity database's collation"**:

> **Target:** 0003 §2.2 (the Store row), §12, §16. **Requested by:** spec 0011 §6.7. **Why:** one
> collation on every platform database; the server default defines no order on Amharic text,
> such as users' names in the shared directory.
>
> - **The rule.** The identity database uses `Latin1_General_100_CI_AS`, and on Azure SQL
>   Database `CATALOG_COLLATION = DATABASE_DEFAULT`, as spec 0011 §6.7 states. In in-proc mode the
>   store is the catalog database, which §6.7 already covers.
> - **Who creates it.** A standalone deployment's infrastructure creates it (Bicep: `collation`
>   and `catalogCollation`). With `Seed.ApplyMigrations` on, a standalone engine creates a missing
>   database with the same statement before `MigrateAsync`, never through EF's implicit creation.
>   An in-proc engine never creates its database.
> - **An existing database with another collation.** The engine logs a Warning naming both
>   collations and keeps serving; moving needs a new database (Gap 3).
> - **No model change.** The model sets no database collation, so `Tellma.Identity.Migrations`
>   gains no migration: one would emit `ALTER DATABASE … COLLATE`, which Azure refuses.
> - **Tests.** The integration and E2E fixtures create their databases with the platform
>   collation. A migrations case lets the engine create a missing database and asserts
>   `DATABASEPROPERTYEX(DB_NAME(), 'Collation')`.

Follow-on edits inside Part A: the implementation-order list (0010:161-173) gains §A.11; the
placement table (0010:187-192) adds "§A.11" to the `Tellma.Identity` row and keeps
`Tellma.Identity.Migrations` at "none"; Testing (0010:557-595) gains the bullets above; Definition
of done (0010:601) "§A.1–§A.3 and §A.5–§A.10" becomes "… §A.5–§A.11"; Decisions record
(0010:620-645) gains "**The identity database's collation is set where it is created, never by a
migration** — Azure cannot change it, and existing columns would keep the old one (§A.11)."

### Spec 0001 — no part needed

- **[none] 0001:111, 134-136, 153-158, 720.** UDTT columns copy the table column's collation; JSON
  carried as `varchar(max) COLLATE Latin1_General_100_BIN2_UTF8` (purpose: lossless non-Latin
  text, collation-clean implicit convert to `json`). Assignment into the table column needs no
  collation resolution. Keep (D-2's listed exception).
- **[none] 0001:296, 428-439.** The drop guard and the sweep over `sys.sql_expression_dependencies`
  and `sys.table_types`. The spec text has no SQL; the hazard is in the code (§7, item S1).
- **[code] 0001:732.** "Integration (containerized SQL Server): apply migrations to a fresh
  database". The text fixes no collation, so the fixture follows spec 0011 §6.7 as code.

### Spec 0008 — via spec 0010 Part F (testing only)

- **[none] 0008:1398-1405, 1758-1759, 2235-2238.** Comparison and matching follow the column's
  backend collation; no reliance on session collation. Unaffected.
- **[none] 0008:2074-2080.** ASCII literal adoption into `VarChar`: unchanged (F7).
- **[none] 0008:2479-2484, 2523-2526.** The fixture declares one CI, AS collation for its string
  columns; the code uses `Latin1_General_100_CI_AS_SC`. Keep (D-2's listed exception; F7).
- **[via 0010 Part F] 0008:2516-2519 (§18.3).** "deploying the fixture as real tables on the
  developer/CI SQL Server": the fixture *database* takes the server default today. Edit spec 0010
  Part F Testing, 0010:3623: "`Tellma.Core.Queryex.IntegrationTests` creates its database as spec
  0011 §6.7 states; the fixture's text columns keep their declared collation (0008 §18.3)."
  Literal-only comparisons (constant statements, §F.9) then follow the platform collation.

### Specs 0002, 0004–0007, 0009

- **[none]** No database is created or assumed (0007's `TenantId` is a pointer; 0009 has none).

## 5. ARCHITECTURE.md

- **[spec] ARCHITECTURE.md:680 (Migrations & seeding).** Today: "Provisioning also creates the
  database with the `Latin1_General_100_CI_AS` collation (the server default
  `SQL_Latin1_General_CP1_CI_AS` defines no order on Amharic text), enables read-committed
  snapshot isolation …". Edit: drop the collation clause there and add one paragraph after the
  migrator paragraph: "**One collation for every database.** Every database the platform creates
  — the catalog, each tenant database, the identity database, and the development and test
  databases standing in for them — is created `COLLATE Latin1_General_100_CI_AS`, on Azure SQL
  Database `WITH CATALOG_COLLATION = DATABASE_DEFAULT`. The server default defines no order on
  Amharic text, and neither collation can change after creation on Azure, so it is set once at
  `CREATE DATABASE`. Batch-local tables are table variables, never `#temp` tables."
- **[spec] ARCHITECTURE.md:883 (Identity, "Amendments to the identity server").** Append to the
  list: "and the identity database created with the platform collation".
- **[none] ARCHITECTURE.md:879.** In-proc store is the catalog's `idsvr` schema: covered.
- **[spec] ARCHITECTURE.md:1120-1192 (Hosting on Azure).** The diagram's `IDP_SQL` "Identity DB
  (on the shared SQL server)" (1132) and `SQL_POOL` "holds every distribution's Catalog DB +
  tenant DBs" (1134); no word on who creates them. Add an "Other notes" bullet: "**Database
  collation:** Bicep that creates a database (the identity database) sets `collation:
  'Latin1_General_100_CI_AS'` and `catalogCollation: 'DATABASE_DEFAULT'`; the migrator creates
  every catalog and tenant database itself." Depends on Gap 1.
- **[none] ARCHITECTURE.md:220-221, 486-487, 1072, 1092.** Bicep layout and "Azure resources are
  created by CI/CD when Bicep merges": no Bicep file exists yet; the note above is enough.
- **[spec, low] ARCHITECTURE.md:1074.** "a single PowerShell script + `docker-compose` for SQL
  Server". Optional: "(the server keeps its default collation; the migrator creates the
  databases)".
- **[none] ARCHITECTURE.md:89, 748-749, 1202, 652.** Dev database names, LocalDB report tests,
  Testcontainers trait, UDTT collation copy: unaffected.

## 6. Code (read-only)

### Identity server

- **[code] src/apps/Tellma.Identity/Services/Seeding/IdentitySeeder.cs:53-57.** With
  `Seed.ApplyMigrations`, `context.Database.MigrateAsync` creates a missing database implicitly
  with the server default (F4). Change per §A.11: create a missing database with the platform
  statement first; Warning on another collation; never create in in-proc mode.
- **[code] src/apps/Tellma.Identity.Web/appsettings.Development.json:13, 23.** LocalDB
  `TellmaIdentity` with `ApplyMigrations: true`: created by the seeder above. An existing
  developer database keeps `SQL_Latin1_General_CP1_CI_AS` until dropped
  (eng/identity-ui-inspection/inspect.ps1:1280 and ui-inspection-guide.md:352-356 already drop
  it).
- **[none] src/apps/Tellma.Identity.Web/appsettings.json.** `ConnectionString: null`,
  `ApplyMigrations` defaults to false: production creation and migration are outside the code
  (Gap 2).
- **[code, low] src/apps/Tellma.Identity.Migrations/TellmaIdentityDesignTimeDbContextFactory.cs:28.**
  LocalDB `TellmaIdentityDesign` for `dotnet ef database update` only; EF would create it with the
  server default. Scaffolding never connects. Low value; leave or document.
- **[none] src/apps/Tellma.Identity/Hosting/TellmaIdentityServiceCollectionExtensions.cs:163-177.**
  `ConfigureDbContext` lets an in-proc host point the store at the catalog. No model collation
  (`HasDefaultSchema("idsvr")` only, Data/TellmaIdentityDbContext.cs:26). Keep it that way (F4).
- **[code] test/apps/Tellma.Identity.IntegrationTests/Infrastructure/SqlServerFixture.cs:73.**
  `CREATE DATABASE [{name}]`: server default. Change to `CREATE DATABASE [{name}] COLLATE
  Latin1_General_100_CI_AS`.
- **[code] test/apps/Tellma.Identity.IntegrationTests/MigrationsTests.cs:27-40.** Migrates a
  fixture database; add the §A.11 collation assertion here.
- **[code] test/apps/Tellma.Identity.E2E/Infrastructure/IdentityServerFixture.cs:498.**
  `CREATE DATABASE [{database}]` (container or `TELLMA_TEST_SQL`), then `ApplyMigrations` (:91).
  Same change as the integration fixture.
- **[none] test/apps/Tellma.Identity.IntegrationTests/Infrastructure/StandaloneFactory.cs:89,
  InProcFactory.cs:32, test/apps/assets/Tellma.Identity.TestInProcHost/appsettings.json:7.**
  Placeholder connection strings, `ApplyMigrations: false`. Nothing created.
- **[none] eng/identity-ui-inspection/ui-inspection-guide.md:16.** "the app creates and migrates
  `TellmaIdentity` itself": stays true under §A.11.

### Table types (spec 0001's extension)

- **[code] src/core/Tellma.Core.EntityFrameworkCore/TableTypes/TableTypesSqlServerMigrationsSqlGenerator.cs:274, 297.**
  Item S1 of §7. `DECLARE @keep TABLE ([name] sysname PRIMARY KEY)` then `LEFT JOIN @keep AS k ON
  k.[name] = tt.[name]` against `sys.table_types`. `@keep.name` takes the data collation (F3),
  `tt.name` the catalog collation. On Azure without `CATALOG_COLLATION = DATABASE_DEFAULT` these
  differ once D-2 applies, so every type-touching migration's sweep fails with Msg 468, for tenant
  databases and for the catalog (`TenantMembershipList`). §6.7's catalog-collation clause
  prevents it. Optional hardening (Gap 6): `ON k.[name] = tt.[name] COLLATE DATABASE_DEFAULT`.
- **[none] same file :118-186, :200-245.** Existence, hash, scope and drop-guard checks compare
  `sys.*` columns with literals or variables: no conflict (a literal or variable never conflicts
  with one column).
- **[none] same file :378; TableTypes/Conventions/TableTypeFinalizingConvention.cs:62, 539.**
  `COLLATE` copied from the model column into `CREATE TYPE`; JSON columns forced to
  `Latin1_General_100_BIN2_UTF8`. Purposes as in §4 (spec 0001). Keep.
- **[code] test/core/Tellma.Core.EntityFrameworkCore.IntegrationTests/Infrastructure/SqlServerFixture.cs:72.**
  `CREATE DATABASE [{name}]`: server default. Change to the platform statement so the suite
  migrates under the production collation (it cannot reproduce Azure's catalog collation, F1).
- **[none] same suite: ApplyMigrationsTests.cs:31, 126, 194, 216, 219, 240; DropGuardTests.cs:38.**
  `MigrateAsync` and `EnsureCreatedAsync` on fixture-created databases: they never create one.
- **[code, low] test/core/assets/Tellma.Core.EntityFrameworkCore.MigrationsHost/Program.cs:16,
  MigrationsHostContextFactory.cs:24.** `--migrate` runs `Migrate()` against LocalDB
  `TellmaMigrationsHost`, letting EF create it. A manual tool; CI's design-e2e job
  (.github/workflows/ci.yml:62-100) only scaffolds, scripts and bundles. Leave or create first.
- **[none] test/core/Tellma.Core.EntityFrameworkCore.Tests: Infrastructure/TestModel.cs:40;
  SqlGeneration/TableTypeSqlGenerationTests.cs:120, 127; Metadata/TableTypeModelExtensionsTests.cs:79, 112;
  Conventions/JsonColumnTests.cs:29, 143, 147; Design.Tests/Scaffolding/OperationScaffoldingTests.cs:48.**
  Unit tests: an unopened connection string, and explicit collations as inputs that pin
  collation copying into the type. Keep.

### Queryex

- **[code] test/core/Tellma.Core.Queryex.IntegrationTests/SqlServerFixture.cs:68-69.** `IF DB_ID(…)
  IS NULL CREATE DATABASE [TellmaQueryexTests]`: server default. Change to the platform statement
  (via spec 0010 Part F). An existing developer database keeps its old collation; drop it once.
- **[none] test/shared/Tellma.Queryex.Testing/Schema/LedgerDdl.cs:22-28, 108.** Every text column
  `COLLATE Latin1_General_100_CI_AS_SC`. Purpose: comparison must not depend on the server.
  Keep (F7). Its columns never meet a table variable or TVP in that database, so no conflict.
- **[code, low] eng/queryex-inspection/Tellma.Queryex.Inspection/Fixture.cs:73-75.** The
  playground creates the developer-named database without `COLLATE`. Same change.

### Infrastructure and CI

- **[none] infra/.** Only `monitoring/`; no Bicep creates a database yet. When
  `infra/shared-platform.bicep` and a distribution's `infra/main.bicep` arrive, any database they
  declare sets `collation` and `catalogCollation` (F5).
- **[none] .github/workflows/ci.yml:50-59.** The integration job runs on `ubuntu-latest` only,
  through Testcontainers with the image's default server collation. Keep the default (F8); never
  set `MSSQL_COLLATION`, so `tempdb` mismatches surface in tests as they would on Azure.

## 7. Collation-sensitive constructs, summarised

- **`#temp` tables and `SELECT … INTO #`.** None in specs 0001–0035 or the code. Rule proposed
  in spec 0012 §1.4.
- **Cross-database queries.** None: spec 0011 §3.5 (0011:757-759) forbids `USE` and
  `ChangeDatabase`; catalog and tenant work use separate connections. The in-proc `idsvr` schema
  shares the catalog database but no query joins it to `catalog`.
- **S1. `sys.*` names joined to user values.** Only the table-type sweep (§6 above): safe on box
  SQL Server, Msg 468 on Azure unless the catalog collation matches. Every other `sys.*` use
  compares with literals, variables or ids: EF's history and default-constraint lookups, spec
  0011 §6.4's `DATABASE_PRINCIPAL_ID('tellma_app')`, the drop guard, and the test helpers.
- **`COLLATE DATABASE_DEFAULT` / `CATALOG_DEFAULT`.** None anywhere. Proposed only for
  distribution raw SQL that creates a `#temp` table, and optionally the sweep join.
- **Explicit `COLLATE` clauses.**
  - `Latin1_General_100_BIN2`: catalog `TenantMemberships.Subject` and its table type, `Sessions`
    `Key`, `Handle`, `Subject` (spec 0011 §7.3-§7.4); `core.Users.Subject` (spec 0014 §3.1).
    Case-sensitive identifiers. Keep.
  - `Latin1_General_100_BIN2_UTF8`: JSON columns in table types (spec 0001; code
    TableTypeFinalizingConvention.cs:62). Lossless non-Latin text. Keep.
  - `Latin1_General_100_CI_AS_SC`: Queryex fixture text columns (LedgerDdl.cs:28). Keep.
  - Column collations copied verbatim into `CREATE TYPE` (generator :378). Keep.
  - `Latin1_General_100_CI_AS` at `CREATE DATABASE` (spec 0011 §6.1 today, §6.7 proposed).
- **Table variables and TVPs with string columns.** Safe while every type and table in a database
  was created under that database's never-changing collation (Decision 32). A TVP type carrying
  an explicit collation must match the column it joins (`TenantMembershipList` does).
- **Change detection by `EXCEPT` (spec 0012 §8.1-§8.2).** Case-only and width-only edits count as
  unchanged under CI collation. Flagged above; not caused by D-2.

## Gaps

1. **Who creates the SaaS catalog database** (`tellma-<slug>-catalog`)? Spec 0011 says only that
   the migrator migrates it; `ARCHITECTURE.md` says Bicep creates a distribution's Azure
   resources. Recommended: the migrator, through the same statement and template as tenants, with
   Bicep owning only the server and pools. If Bicep creates it instead, §6.7's "Who creates"
   bullet and the Hosting note change, and the Bicep must set both collation properties.
2. **Who creates and migrates the shared identity database in production?** Spec 0003 is silent,
   production `ApplyMigrations` defaults to false, and `infra/shared-platform.bicep` does not
   exist. §A.11 assumes infrastructure creates it; a deploy step must still migrate it.
3. **Does any database already exist under `SQL_Latin1_General_CP1_CI_AS`** (a deployed identity
   database, a long-lived developer catalog)? Azure cannot convert it in place; it needs a new
   database and a data copy. That decides whether the identity engine warns (proposed) or refuses,
   and whether the migrator's refusal needs an escape hatch for existing tenants.
4. **`Tellma:Provisioning:CreateDatabaseTemplate`'s shape.** §6.1 treats it as the Azure options
   that follow `COLLATE`; §8 says "`{database}` is substituted", which suggests a full statement.
   A full statement would let configuration drop the collation. Recommended: options only; the
   migrator writes `CREATE DATABASE … COLLATE …` and `WITH CATALOG_COLLATION` itself.
5. **In-proc identity migration owner.** Spec 0011 §5.7 lets the web process migrate `idsvr`,
   while §6.1's `migrate` also migrates it. If the engine's `MigrateAsync` runs before §3.4's
   catalog check, EF would create a missing catalog with the server default and outside the pool.
   §5.7's proposed sentence closes this; who migrates `idsvr` is worth settling in the same edit.
6. **Sweep hardening in code.** §6.7's catalog collation fixes S1, and no PR suite can catch S1
   because box SQL Server always matches catalog to data. An explicit `COLLATE DATABASE_DEFAULT`
   in the sweep join would protect databases created outside the migrator. Needs no spec text.
7. **`#temp` tables on Azure SQL Database.** Microsoft documents `tempdb` collation for temp data
   in non-contained databases, but says nothing Azure-specific. The proposed "no `#temp` tables"
   rule is the conservative reading; not verified on a live Azure database.
8. **Creation race.** Two concurrent `migrate` runs could both find the catalog missing; the
   catalog applock cannot be taken before the database exists. Suggested: treat Msg 1801 (already
   exists) as present, then run the collation check. Implementation detail for §6.7.
9. **`EXCEPT` change detection** (spec 0012 §8.2) drops case-only edits. A semantic decision for
   Ahmad, outside D-2's literal scope; listed because it is collation-sensitive.
10. **Spec 0019's wording** (0019:1217) names accent-insensitivity, which `Latin1_General_100_CI_AS`
    does not have. A low-priority accuracy fix.

Sources:

- [CREATE DATABASE (Azure SQL Database): `COLLATE`, `CATALOG_COLLATION`, `ELASTIC_POOL`](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-database-transact-sql?view=azuresqldb-current)
- [Contained database collations: temp data, metadata and the Azure note](https://learn.microsoft.com/en-us/sql/relational-databases/databases/contained-database-collations?view=sql-server-ver17)
- [DECLARE @local_variable: table-variable column collation](https://learn.microsoft.com/en-us/sql/t-sql/language-elements/declare-local-variable-transact-sql?view=sql-server-ver17)
- [sys.databases: `catalog_collation_type_desc`](https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-databases-transact-sql?view=azuresqldb-current)
- [Microsoft.Sql/servers/databases (Bicep): `collation`, `catalogCollation`](https://learn.microsoft.com/en-us/azure/templates/microsoft.sql/servers/databases?pivots=deployment-language-bicep)
- EF Core source, `C:\Users\ahmad\workspace\efcore` (`release/10.0`, 9d1b795935):
  `src/EFCore.SqlServer/Storage/Internal/SqlServerDatabaseCreator.cs:150-162`,
  `src/EFCore.SqlServer/Migrations/SqlServerMigrationsSqlGenerator.cs:985-1047, 1117-1146`.
