# Review round 1, follow-up 3 (28 October 2026): binding decisions

Ahmad's words are quoted; the interpretation under each is binding for every agent in this round.

All spec work happens in `C:\Users\ahmad\workspace\tellma\tellma-platform`. Specs 0001–0009 are
frozen: never edit them; a change to what they specify goes into spec 0010 (Part A amends 0003,
Part B 0002, Part C 0004, Part D 0005, Part E 0006, Part F 0008, and this round adds a part for
0001). Specs 0010–0035 and `ARCHITECTURE.md` are edited in place. Follow `docs/AGENTS.md`
("Revising specs across review rounds": replace, don't append; no history narration; each fact
once, in the section that owns it; rationale only in the Decisions record) and the readability
rules in `.tmp/round-2/style-guide.md`.

## Where this round starts

The previous step (follow-up 2, unstaged over commit `ae05981`; trail in `.tmp/round-2/r4/`) put
every database on `Latin1_General_100_CI_AS`, "the platform collation":

- spec 0011 §6.7 (new) owns the rule: the migrator creates a missing catalog and every tenant
  database itself with `CREATE DATABASE … COLLATE Latin1_General_100_CI_AS` (on Azure SQL Database
  followed by `Tellma:Provisioning:CreateDatabaseTemplate`'s options and `WITH CATALOG_COLLATION =
  DATABASE_DEFAULT`), refuses an existing database in another collation, turns read-committed
  snapshot on in a fresh catalog, and says no model calls `UseCollation`; Decision 32 records why;
- spec 0010 §A.11 (new) amends the identity server: standalone created by infrastructure (Bicep)
  or by the engine's seeder through a `master` connection; in-proc never created by the engine;
  the seeder refuses another collation; no model collation; Part A Decision 12 and Review flag 9;
- spec 0012 §4.4 requires a table-type string column to declare the collation of the explicitly
  collated column it is compared with (`TenantMembershipList.Subject`,
  `UserInvitationOutcomeList.Subject` → `Latin1_General_100_BIN2`); spec 0011 §3.8 and spec 0018
  §3.1 sketches carry it as a comment;
- `ARCHITECTURE.md` "Migrations & seeding" ("One collation for every database"), the Identity
  amendments paragraph, and a Hosting "Databases" bullet.

Verified facts from that round are in `.tmp/round-2/r4/facts-collation.md`; read §1 (EF Core) and
§2 (Azure) before relying on anything about EF or Azure. Key ones: an initial migration of a model
that calls `UseCollation` carries **no** collation operation (§1.2); adding `UseCollation` to a
model that already has migrations scaffolds `AlterDatabase`, which writes `ALTER DATABASE …
COLLATE` (§1.2, §1.3, §1.6), and Azure SQL Database does not support that statement (§2.3); EF's
`Create()` (used by `Migrate()`, `EnsureCreated()` and `dotnet ef database update`) writes `CREATE
DATABASE … COLLATE <model collation>` and turns read-committed snapshot on except on Azure, but
never writes Azure options or `CATALOG_COLLATION` (§1.4); `Migrate()` never compares collations on
an existing database (§1.5).

## D-1. Every settings save forgets page state

> "Fine"

No edit. Spec 0025 §10 and spec 0032 §7.7 stay as they are.

## D-2. The migrator creates every catalog

> "The migrator" (who creates the SaaS catalog)

The migrator creates every distribution's catalog database, SaaS included, as it creates tenant
databases. No Bicep or other infrastructure declares the catalog. Remove the Bicep alternative
wherever it is offered for the catalog (spec 0011 §6.7's Infrastructure bullet, `ARCHITECTURE.md`
Hosting "Databases", and anything else that says or implies infrastructure may create it).

## D-3. The production identity database is created and migrated manually, for now

> "For now manually, it's one time for the whole fleet, we will add convenience tools later to
> simplify on-prem deployment, and it will reuse the same migrator approach as every other db"

- The shared identity database (standalone mode, production) is created once for the whole fleet
  by an operator, by hand, with spec 0011 §6.7's statement (its Azure form on Azure SQL
  Database), and then migrated by hand with the identity engine's migrations (`dotnet ef
  database update` against `Tellma.Identity.Migrations` works, see D-6b).
- No Bicep declares it. Spec 0010 §A.11's "created by infrastructure" path becomes "created by an
  operator".
- Convenience tools for this (and on-prem deployment) come later and will reuse the migrator's
  approach. At most one short "later" line in a Non-goals or equivalent place; no design for them.

## D-4. No identity database exists outside Ahmad's development machine

> "No identity databases exist yet outside of this dev machine."

- The refusal of an identity database in another collation stands, with no escape hatch. Spec
  0010 Part A's Review flag 9 (refuse versus warn) is resolved: delete it and renumber nothing
  after it unless something follows it. Its reason may go into Part A's Decisions record in one
  clause if useful.
- The same holds for catalog and tenant databases: none exists, so the migrator's refusal needs no
  escape hatch. Do not add one.
- §A.11's Compatibility note covers only a developer's existing identity database: it is dropped
  and re-created.
- This also frees the identity migrations: no deployed database needs to be carried forward, so
  they are deleted and regenerated (D-6b).

## D-5. Amend spec 0001 for column collations of class-derived table types

> "Let's add an amendment to spec 0001"

- Spec 0001 (EF Core table types, implemented and frozen) gets a new part in spec 0010. Use the
  next free letter (Part G), placed after Part F, and give it the structure of the existing parts
  (its intro, terms if needed, placement, the amendment sections with Target / Requested by / Why /
  change / Compatibility / Tests, then Testing, Definition of done, Decisions record, Review
  flags), proportionate to its size.
- The amendment lets a class-derived standalone table type declare a string column's collation,
  through a mechanism spec 0001 §5 can honour for classes (today only the fluent
  `TableTypeBuilder.Column(…, collation)` / `StandaloneColumnConfiguration.Collation` offers it).
  Research chooses the mechanism from spec 0001 and the library code; prefer one that matches how
  spec 0001 already honours annotations. Settle too whether entity-derived table types carry
  their source column's collation, and amend only if the platform needs it.
- `TenantMembershipRecord.Subject` (spec 0011 §3.8) and `UserInvitationOutcome.Subject` (spec 0018
  §3.1) then declare `Latin1_General_100_BIN2` with that mechanism in their sketches, and spec 0012
  §4.4 cites the amendment.
- Spec 0010's intro changes with it: "Specs 0001, 0007 and 0009 need no amendment" loses 0001;
  the parts list, Goals, How to read an amendment, and every place in specs 0010–0035 and
  `ARCHITECTURE.md` that lists spec 0010's parts or says which specs it amends.

## D-6. Rename the template; collation in every model; `migrate` owns `idsvr`

> "Rename `CreateDatabaseTemplate`, set the collation in the model builder to support `dotnet ef
> database update`, `migrate` owns `idsvr`."

### D-6a. The rename

`Tellma:Provisioning:CreateDatabaseTemplate` becomes **`Tellma:Sql:CreateDatabaseOptions`**: the
Azure options the migrator appends after `COLLATE` when it creates a catalog or tenant database on
Azure SQL Database; null on-prem. The `Provisioning` configuration section has no other key, so it
disappears (spec 0011 §8's table and its JSON example). Rename every mention in specs and
`ARCHITECTURE.md`.

### D-6b. Every platform model sets the database collation

- The tenant model (`TellmaDbContext`, spec 0012), the catalog model (spec 0011 §7) and the
  identity model (`TellmaIdentityDbContext`, spec 0010 §A.11) each call `UseCollation` with the
  platform collation in their model builder. Then `dotnet ef database update`, `Migrate()` and
  `EnsureCreated()` create a missing database in the platform collation on SQL Server and LocalDB.
  Name one owner for the collation's value that all three models and the migrator read (research
  says where it can live, for example a constant in `Tellma.Core.Abstractions`, which
  `Tellma.Identity` already references; verify).
- No migration ever alters a database's collation. The catalog and tenant models are new, so
  their initial migrations carry no collation operation (fact §1.2). The identity model already
  has migrations, and adding `UseCollation` to it would scaffold an `AlterDatabase`, which Azure
  refuses.
  > Ahmad (added while this round ran): "To simplify, I am OK with deleting and regenerating the
  > identity migrations, they are not deployed anywhere outside this dev machine yet"

  So **the identity migrations are re-baselined**: every existing migration and the snapshot of
  `Tellma.Identity.Migrations` are deleted, and one initial migration is scaffolded from the model
  with `UseCollation`, so it carries no collation operation. No emptied or hand-edited migration.
  §A.11 states this (and the placement row says `Tellma.Identity.Migrations` is regenerated); the
  Compatibility note says a development identity database is dropped and re-created, since its
  history no longer matches. A test pins that no migration of any platform model alters the
  database collation.
- The migrator still creates the catalog and tenant databases with spec 0011 §6.7's own statement
  on every engine (Azure needs the options and `CATALOG_COLLATION`, which EF never writes), and
  still checks an existing database's collation before migrating it (EF never does).
- With EF creating databases in the platform collation, §A.11's custom `master`-connection
  creation path for the engine is no longer needed: with `Seed.ApplyMigrations` on, EF's
  `MigrateAsync` creates a missing development database in the platform collation, with
  read-committed snapshot. Simplify §A.11 accordingly (keep the existing-database refusal).
- Revise everything that says the opposite: spec 0011 §6.7 ("no model calls `UseCollation`",
  "Nothing relies on EF's implicit creation"), Decision 32, spec 0010 §A.11 ("No model
  collation", "the engine's model stays as it is"), Part A's placement row ("`Tellma.Identity.
  Migrations` … gains no migration"), Part A Decision 12, `ARCHITECTURE.md`'s "No EF collation"
  bullet, and test fixtures: a fixture with an EF model may let EF create its database; a fixture
  without one (the Queryex suite) uses §6.7's SQL Server statement.

### D-6c. `migrate` owns `idsvr`

- In in-proc mode the migrator's `migrate` applies the identity engine's migrations to the
  catalog's `idsvr` schema. The web process never migrates `idsvr` or any other schema, so
  `ARCHITECTURE.md`'s "migrations never run in the web process" holds with no exception.
- `AddTellma` runs the in-proc engine with its migrations off (spec 0011 §5.7 lists what
  `AddTellma` passes; research confirms the engine setting, for example `Seed.ApplyMigrations =
  false`, and what the engine's seeding still does at startup).
- Say what a web host does when `idsvr` is missing or behind, consistent with spec 0011 §3.4 and
  §6.6 (a missing catalog prints the `migrate` command).
- Revise every place that says the web process or the engine migrates `idsvr` (spec 0011 §5.7,
  §6.1, its Decisions record, spec 0010 §A.11's in-proc bullet, `ARCHITECTURE.md`'s identity and
  migrator text, tests).

## Still open, not decided this round

- The Queryex fixture's `_SC` text columns stay as they are.
