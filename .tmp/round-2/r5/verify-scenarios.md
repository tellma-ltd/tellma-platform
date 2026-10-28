# Follow-up 3: end-to-end scenario review

Scope: scenarios (a)–(g) of the brief walked through spec 0010 Parts A and G, spec 0011 §3.4,
§5.7, §6, §7, §8, §10, spec 0012 §4.4 and §13.2, spec 0018 §3.1, spec 0001 and `ARCHITECTURE.md`,
against `decisions.md`, `design.md`, the three facts files and the two applied notes. Findings of
`verify-0010.md` (A-01…A-11) and `verify-rest.md` (B-01…B-11) are not repeated; where a scenario
reaches one of them it is cited by its id.

Walked and found right (no finding): the fresh-clone path of (a) through `migrate` → `idsvr` →
`provision` → web startup → store check; EF creating a standalone development identity database in
the platform collation with RCSI; Ahmad's server-default database refused by step 1 of the store
check and re-created after a drop; the Azure statement's clause order and the migrator's refusal of
a catalog that EF or a DBA created without `CATALOG_COLLATION`; `dotnet ef database update` on an
operator-created Azure database issuing no `CREATE` or `ALTER DATABASE`; the re-baselined `Initial`
carrying no collation operation and the collation pin keeping it so; the migrator-first and
web-first orders of (d) (a behind `idsvr` fails the new web instance naming `migrate`; an ahead
`idsvr` passes the old instance's pending-migration count, since EF ignores unknown applied ids);
Part G's hash change creating a new physical type beside the old one and the sweep retiring it,
with the sweep's `sys.table_types` join safe on every engine the migrator creates on; the identity,
Core, spec 0012 fixture and Acme suites' creation paths and collations; a restored tenant passing
the existing-database check.

## docs/specs/0011-distribution-host-and-multitenancy.md

- [S-01] (must) 0011 §6.1 `migrate` row, §3.5, §6.4 - "it migrates the catalog and, in in-proc
  mode, `idsvr` through `migrateIdentityStore` (§5.7); registers" -> after "(§5.7);" insert
  "recomputes the catalog's `tellma_app` role and grants over schema `catalog` and, in in-proc
  mode, `idsvr`, and in a fresh catalog creates the web application's principal, as §6.4 does for
  a tenant;". In §3.5 **The application role**, "Every tenant database has a database role
  `tellma_app`, created by the migrator on first migration" -> "Every tenant database and the
  catalog have a database role `tellma_app`, created by the migrator on first migration". In §6.4,
  the comments "-- provisioning only, SaaS:" and "-- provisioning only, on-prem:" -> "-- fresh
  database only, SaaS:" and "-- fresh database only, on-prem:", and add after the role line a
  comment "-- the catalog: schemas [catalog] and, in-proc, [idsvr]; its one table type grants
  tellma_app too". Why: scenario (b) breaks at run time. §3.2 opens the catalog with
  `Tellma:Catalog:ConnectionString` (`Authentication=Active Directory Managed Identity`, §8), but
  no step creates that identity's user in the catalog or grants it anything: §3.5, §3.6 and §6.4
  scope `tellma_app`, `CREATE USER … FROM EXTERNAL PROVIDER` and the grants to tenant databases,
  and D-2 removed the only other creator of the catalog. In in-proc mode the engine also writes
  `idsvr` at every start (§5.7's seeding) through the same identity. The catalog's
  `TenantMembershipList` is created by a catalog migration with spec 0001's configured
  `GRANT EXECUTE ON TYPE` (0001 §3), which needs the role to exist in that database.
- [S-02] (should) 0011 §3.4 - "the catalog's migration history is compared with the platform's
  latest catalog migration" -> append ", which `Tellma.Core` publishes as the constant
  `CatalogMigrations.Latest` (`Tellma.Core.Tenancy`); a `Tellma.Core.Tests` test keeps it equal
  to the last migration in `Tellma.Core.Migrator`". Add that pin to §10's `Tellma.Core.Tests` row
  after "the collation pin over the catalog migrations;". Why: scenarios (a) and (d). §5.7 now
  rests on "§3.4's catalog check runs first", and §1.2 gives the web host
  `Tellma.Identity.Migrations` so the store check can count `idsvr`'s pending migrations, but the
  catalog migrations ship only in `Tellma.Core.Migrator` (§1.1), which the Web project never
  references (§1.2), so nothing says where the web host gets the id it compares with (facts-identity
  §6.1). The alternative is in the Notes.
- [S-03] (should) 0011 §5.7 - "At startup §3.4's catalog check runs first, so a missing catalog
  prints the `migrate` command (§6.6)." -> "At startup the registry's first load (§3.4) runs before
  the engine's seeding service, since `AddTellma` registers it first and hosted services start in
  registration order; so a missing catalog prints the `migrate` command (§6.6)." Why: scenario (a).
  The order is asserted but no mechanism carries it. The engine's seeder is a hosted service that
  starts in registration order (facts-identity §1.5), and §2.3 names only `TellmaStartupGate`'s
  place among hosted services. Without the order, a missing catalog in in-proc mode surfaces as the
  store check's "every migration pending" message instead of §3.4's.
- [S-04] (should) 0011 §6.1 `status` row - "and pending migrations; runs
  `TellmaComposition.Validate`" -> "and pending migrations (the catalog's, in in-proc mode
  `idsvr`'s, and each tenant's); runs `TellmaComposition.Validate`". Why: scenario (d). D-6c makes
  `migrate` own `idsvr`, and §5.7 fails a web host on a behind `idsvr`, so the operator's
  diagnostic must show the count `migrate` would apply; today the row names no schema.
- [S-05] (should) 0011 §6.7 **A fresh database** - "A database with no `__EFMigrationsHistory`
  table is fresh, whoever created it." -> "A database with no history table of its own model is
  fresh, whoever created it: `catalog.__EFMigrationsHistory` for the catalog, `dbo.` for a tenant;
  the in-proc `idsvr` history in `dbo` (§3.2) does not count." Why: scenario (a). §3.2 now puts a
  second history table, `dbo.__EFMigrationsHistory`, in the catalog, so "no `__EFMigrationsHistory`
  table" has two readings for the one database that holds both, and the RCSI statement hangs on it.
- [S-06] (should) 0011 §6.1 `provision` row - "Creates the database (§6.7);" -> "Creates the
  database when it is missing, and checks an existing one (§6.7);". Why: scenario (g). §6.7's
  **An existing database** and **Copies** bullets accept a database a DBA pre-created or restored,
  and §10's Acme row pins "`provision` naming an existing database in another collation fails", but
  the command's own row reads as an unconditional create.
- [S-07] (should) 0011 §6.1, paragraph after the table - "The design-time factory
  `AcmeDesignTimeFactory : TellmaDesignTimeDbContextFactory<TellmaDbContext>` overrides `Compose`
  so `dotnet ef` builds the context" -> after that sentence add: "`Tellma.Core.Migrator` carries
  the catalog's own factory, `CatalogDesignTimeFactory`, so `dotnet ef` targets that project for
  the catalog migrations. Each factory takes its connection string from `--connection`, else from
  `TELLMA_TENANT_MIGRATIONS_SQL` or `TELLMA_CATALOG_MIGRATIONS_SQL`, else LocalDB. A database
  `dotnet ef database update` creates serves migration authoring only: it has no `tellma_app`, no
  fingerprint row and no catalog row, so no host can serve it." Why: scenario (a). D-6b's stated
  purpose is `dotnet ef database update` on all three models, and spec 0010 §A.11 names the identity
  factory's command and variable (facts-identity §1.3, §5), but §6.1 names neither a factory for
  `CatalogDbContext` nor where either factory's connection string comes from; the last sentence
  stops a developer from expecting §3.3's verdicts to serve such a database. The names are
  proposals (Notes).
- [S-08] (should) 0011 §6.7 **Development and tests** - "A fixture with no EF model runs the SQL
  Server statement itself." -> "Any other fixture runs the SQL Server statement itself." Why:
  scenario (f). The EF table-types integration suite has an EF model (the migrations host) that
  cannot set the platform collation (the library never references Tellma application projects,
  facts-identity §2), so it is neither of the two cases the bullet names; see S-10.

## docs/specs/0010-amendments-to-implemented-specs.md

- [S-09] (should) 0010 §A.11 **The migrations** - "An initial migration carries no collation
  operation, so no identity migration alters the database's collation." -> append "Every later
  migration is compatible with the previous engine release, as spec 0012 §4.3 requires of tenant
  migrations: on every path the migrations land before the engine, since `migrate` precedes a
  web deployment and the operator's step 3 precedes the engine's." Why: scenario (d). The
  **Standalone, deployed** bullet orders step 3 before the engine deploys, spec 0011 §6.1 runs
  `migrate` before the web deployment, and a rolling deployment keeps old instances on the new
  `idsvr` (EF counts no pending migration for an unknown applied id, facts-identity §3.4), so the
  old engine runs on the new schema. Spec 0003 states no such rule, and `ARCHITECTURE.md`'s N−1
  rule names the platform's tables and a distribution's, not the engine's.
- [S-10] (should) 0010 Part G — Testing, `Tellma.Core.EntityFrameworkCore.IntegrationTests` row -
  "the collated type on SQL Server; the migrations host gains the type, so its migrations are
  re-scaffolded and its type counts updated" -> append "; the fixture creates each database with
  spec 0011 §6.7's SQL Server statement". Why: scenario (f). D-6b's fixture rule covers every
  fixture that creates a database, and the five creation sites in facts-collation §5 include this
  suite's `SqlServerFixture.cs:72`, which today creates with no `COLLATE`. Part G is the only text
  that touches this suite, and Part F — Testing already states the same for the Queryex suite.
- [S-11] (should) 0010 §A.11 **Standalone, deployed** - "3. The engine's migrations, through
  `dotnet ef database update` against `Tellma.Identity.Migrations`" -> after step 3 add "4. A
  database user for the engine's principal, with `SELECT`, `INSERT`, `UPDATE` and `DELETE` on
  schema `idsvr` and `SELECT` on `dbo.__EFMigrationsHistory`." Why: scenario (c). §A.11 now owns
  "who creates and migrates" the shared database (Part A — Definition of done, Docs), and the store
  check reads the history table at every start, but the operator's list ends at the migrations;
  0003 §12 and §16 say nothing about the database principal, and the engine's host connects with
  its own identity (`TellmaIdentity:ConnectionString`, `Tellma.Identity.Web`).
- [S-12] (should) 0010 Part A, Placement, `Tellma.Identity.Web` row - "configuration only (§A.3,
  §A.4, §A.5)" -> "configuration only (§A.3, §A.4, §A.5, §A.11)"; and in §A.11 **Standalone,
  deployed**, after "otherwise the store check stops it." add "`Tellma.Identity.Web` sets
  `Seed.MigrateCommand` to step 3's command, so that error names it." Why: scenario (c). The store
  check names `Seed.MigrateCommand` "when set", spec 0011 §5.7 sets it for the in-proc host, and
  nothing sets it for the deployed standalone engine, whose failure would then name a count and no
  remedy; the example command is the remedy.

## ARCHITECTURE.md

- [S-13] (should) Tenant & Distribution Onboarding, "New distribution" - "The script provisions
  Azure resources via Bicep, registers the OIDC client with the identity provider, and configures
  DNS." -> "The script provisions Azure resources via Bicep, makes the migrator job's identity a
  `dbmanager` member in the SQL server's `master` so it can create the catalog and tenant
  databases, registers the OIDC client with the identity provider, and configures DNS." Why:
  scenario (b). D-2 moved every database creation to the migrator, whose Azure principal must hold
  `dbmanager` or be the server administrator (spec 0011 §6.7, **Where it runs**); that is a
  server-level grant made once per distribution in `master`, and no step names it.

## Notes for Ahmad

1. **S-01's shape.** The catalog needs the web identity's user and rights; the finding proposes the
   tenant pattern (`tellma_app` recomputed by `migrate`, the principal created in a fresh catalog).
   The alternative is direct grants to `Tellma:Sql:ApplicationPrincipal` with no role, which
   Decision 22 argues against. Either way the catalog's `TenantMembershipList` grant principal
   must exist in the catalog before the first catalog migration creates the type, so `migrate`
   must create the role before it migrates.
2. **S-02's mechanism.** A constant pinned by a test is the least machinery. The alternative moves
   the catalog migrations into `Tellma.Core` (applying needs no Design package, as
   `ARCHITECTURE.md` says), so the web host counts pending migrations through `CatalogDbContext`
   exactly as the engine does for `idsvr`; that changes spec 0011 §1.1's `Tellma.Core.Migrator` row.
3. **S-07's names** (`CatalogDesignTimeFactory`, `TELLMA_CATALOG_MIGRATIONS_SQL`,
   `TELLMA_TENANT_MIGRATIONS_SQL`) mirror the identity factory's; pick others if you prefer.
4. **On-prem, a DBA pre-creates (g).** Spec 0011 §6.7 requires the migrator's principal to hold
   `CREATE ANY DATABASE` on SQL Server. An on-premises DBA who pre-creates the databases will
   often refuse that right. The checks already accept a pre-created database (S-06); decide whether
   §6.7 relaxes the right to "or every database it migrates exists" now, or with the on-prem
   tooling D-3 defers.
5. **S-10 is consistency, not necessity.** Part G's BIN2-to-BIN2 join and the sweep work in any
   database collation on box SQL Server; the finding follows D-6b's "every fixture" rule. Drop
   S-08 and S-10 together if the library suite may keep the server default.
6. **Pre-existing, noticed in (g), not this round's text.** A tenant restored to another server has
   no migrator command that updates `catalog.Tenants.Server` and `Database` (`set-state` changes
   state only), and on Linux on-prem the restored database's `tellma_app_user` is orphaned, since
   `CREATE USER … FOR LOGIN` is "provisioning only" (§6.4) and `migrate` never re-maps it.
7. **Unverified, left alone.** Whether a second developer database, LocalDB `TellmaIdentityDesign`
   (the identity design-time factory's default, facts-identity §1.3), exists on your machine in
   the server default. `dotnet ef database update` would apply the new chain to it unchecked (EF
   never reads a collation); §A.11's Compatibility note names one database.
