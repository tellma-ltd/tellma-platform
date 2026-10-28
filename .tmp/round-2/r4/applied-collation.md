# D-2 applied: every database on `Latin1_General_100_CI_AS`

Applied 27 October 2026 from `decisions.md` D-2, `facts-collation.md` and
`inventory-collation.md`. Only facts marked Verified are stated as fact in the specs.

## Edits

### Spec 0011 (owner of the catalog and tenant rule)

- **Context, "Other specs that carry parts of this design".** Added "spec 0010 §A.11 — the
  identity database's collation, which follows §6.7".
- **§3.2 The catalog database.** "migrated by the migrator before any tenant database" -> "created
  as §6.7 states and migrated by the migrator before any tenant database"; paragraph rewrapped.
- **§5.7 In-proc identity mode.** Added "The engine never creates the catalog (spec 0010
  §A.11)."; paragraph rewrapped.
- **§6.1 `migrate` row.** "Migrates the catalog under `sp_getapplock(…)`" -> "Creates a missing
  catalog (§6.7), then migrates it under `sp_getapplock(…)`".
- **§6.1 `provision` row.** The inline `CREATE DATABASE … COLLATE Latin1_General_100_CI_AS (Azure:
  followed by (EDITION = …, SERVICE_OBJECTIVE = ELASTIC_POOL(…)) from the template; on-prem:
  nothing more), failing when the database already exists with another collation` -> "Creates the
  database (§6.7)". The statement and the guard now live once, in §6.7.
- **§6.7 Database creation and collation (new, after §6.6; nothing renumbers).** The owner rule:
  - every platform database (catalog, tenants, dev and test stand-ins) uses
    `Latin1_General_100_CI_AS`, set by `CREATE DATABASE`, never altered; identity follows §A.11;
  - who creates: the migrator (`provision` for tenants, `migrate` for a missing catalog before the
    catalog lock); the web host never; no reliance on EF's implicit creation; no `UseCollation`;
  - the statement: SQL Server/LocalDB `CREATE DATABASE [<name>] COLLATE Latin1_General_100_CI_AS;`;
    Azure SQL Database (`EngineEdition = 5`) appends the template's options, then `WITH
    CATALOG_COLLATION = DATABASE_DEFAULT`;
  - the catalog clause's reason in operational terms (spec 0001's table-type sweep, Msg 468; facts
    §3.5);
  - Bicep: `collation` and `catalogCollation` on `Microsoft.Sql/servers/databases`;
  - existing database: `DATABASEPROPERTYEX(DB_NAME(), 'Collation')` and, on Azure,
    `sys.databases.catalog_collation_type_desc`, read before migrating; another value fails that
    database's command naming both values (the existing `provision` guard, widened to every
    database the migrator migrates); a concurrent creator's database counts as existing;
  - copies: `AS COPY OF` takes no clause, so the check runs on the copy (no claim that copies keep
    collations: Unverified);
  - development and tests: fixtures use the SQL Server statement; servers keep their default; no
    `MSSQL_COLLATION`;
  - example: the Acme SaaS catalog with a pool template (Microsoft's documented pool form, so the
    Unverified `EDITION` beside `ELASTIC_POOL` no longer appears anywhere).
- **§8 table, `Provisioning:CreateDatabaseTemplate`.** "§6.1; `{database}` is substituted." ->
  "§6.7: the options the migrator writes after `COLLATE` in every database it creates on Azure SQL
  Database, catalog and tenants, such as `(SERVICE_OBJECTIVE = ELASTIC_POOL(name = pool1))`; null
  on-prem." The template is options only, as §6.1 already used it; `{database}` had no place in an
  options fragment, and a full statement would let configuration drop the collation.
- **§10 `Tellma.Core.Tests` row.** Added "the database-creation statement on SQL Server and on
  Azure SQL Database, with and without a template (§6.7)".
- **§10 `Tellma.Core.IntegrationTests` row.** Added "`migrate` creates a missing catalog in
  `Latin1_General_100_CI_AS` and refuses an existing one created with another collation (§6.7)".
- **§10 `Tellma.Distro.Acme.IntegrationTests` row.** Added "the catalog and every tenant database
  report `Latin1_General_100_CI_AS`".
- **§10 Fixture paragraph.** "the catalog database is created per test class from the platform
  migrations" -> "created per test class as §6.7 states, then migrated by the platform
  migrations".
- **§11 Definition of done.** Behavior: "the migrator commands, steps, trigger and
  local-development story of §6" -> "…, local-development story and database creation of §6".
  Docs: the data layer's migrator list gains "one collation for every database". Two lines
  rewrapped to stay under 100 columns.
- **Decision 32.** "Every tenant database is created with `Latin1_General_100_CI_AS` — Amharic;
  columns keep their collation (§6.1)" -> "Every database the platform creates uses
  `Latin1_General_100_CI_AS`, set by `CREATE DATABASE` and never altered — Amharic reason kept;
  one collation across catalog, identity store, tenants and tests; Azure cannot change it, SQL
  Server columns keep theirs; a model collation reaches only EF's own `CREATE DATABASE` (no Azure
  options) and a later change emits `ALTER DATABASE … COLLATE` (§6.7)".

### Spec 0010

- **Context, "Other specs".** "spec 0011 §5.5, §5.7, §5.9" -> "… §6.7", with "the database
  collation" added to the gist.
- **Goals, Part A.** Appended "and the identity database on the platform collation".
- **How to read an amendment.** "§A.1 … §A.10" -> "§A.1 … §A.11".
- **Part A, implementation order.** Added item 8: §A.11 ships before any identity database is
  deployed, since a deployed database keeps its creation collation.
- **Part A, placement table.** `Tellma.Identity`: "§A.5–§A.10" -> "§A.5–§A.11".
  `Tellma.Identity.Migrations`: "none: …" gains "the model sets no collation (§A.11)".
- **§A.11 The identity database's collation (new, after §A.10).** Target 0003 §2.2 (Store row),
  §12, §16; requested by spec 0011 §6.7; why: one collation, Amharic. Rules:
  - standalone, created by infrastructure: Bicep sets §6.7's two properties (the path for a tier
    or a pool);
  - standalone, created by the engine: with `Seed.ApplyMigrations`, the seeder checks existence
    before `MigrateAsync` and creates a missing database with §6.7's statement, no edition
    options, so EF's implicit creation never runs (facts §1.4, §1.6);
  - in-proc: the store is the catalog, created by the migrator; the engine never creates it, and a
    missing database fails seeding (host does not start);
  - existing database: whenever the seeder migrates, it checks the collations as §6.7 does and
    fails seeding on another value;
  - no model collation, so `Tellma.Identity.Migrations` gains no migration.
  - Compatibility (`ApplyMigrations` off: no change; an old developer database fails once and is
    dropped, then re-created and re-seeded, 0003 §10.4) and a Tests pointer to Part A — Testing,
    following "How to read an amendment"; Part A keeps its cases in Part A — Testing.
- **Part A — Testing.** After the tier table: the integration and E2E fixtures create their
  databases with §6.7's SQL Server statement. Unit suite: "the creation statement (§A.11) on SQL
  Server and on Azure SQL Database". Integration suite: "Database collation (§A.11)" — standalone
  creation asserted by `DATABASEPROPERTYEX`, another collation fails seeding naming both, in-proc
  with a missing database fails and creates nothing.
- **Part A — Definition of done.** Behaviour "§A.5–§A.10" -> "§A.5–§A.11"; Docs gains "the
  identity database's collation".
- **Part A — Decisions record.** Added 12: the collation is set by whoever creates the database,
  never by a migration; the engine creates its own database rather than EF — Azure cannot change a
  collation, and EF writes no `COLLATE` for a model without one.
- **Part A — Review flags.** Added 9: refusing an identity database in another collation, versus a
  Warning that keeps serving; flips if a deployed identity database already holds users in the
  server default.
- **§F.15, text `min`/`max`.** "agrees with the tenant collation (spec 0011 §6.1)" -> "agrees with
  the platform collation (spec 0011 §6.7)" (§6.1 no longer states the collation).
- **Part F — Testing, `Tellma.Core.Queryex.IntegrationTests`.** Added "Its database is created as
  spec 0011 §6.7 states. The fixture's text columns keep the collation they declare (0008 §18.3)."
  (facts §5: that fixture creates its database with no `COLLATE` today).

### Spec 0012

- **§4.4 Standalone table types.** New paragraph: a standalone type's string column compared with
  an explicitly collated column declares the same collation (`TenantMembershipList.Subject`,
  `UserInvitationOutcomeList.Subject` declare `Latin1_General_100_BIN2`); without it the column
  takes the database collation (spec 0011 §6.7) and the comparison fails with Msg 468. Facts §3.1,
  §3.2 (P1 case 14, P5); this is the conflict-avoidance rule for a construct the platform uses
  (next item).
- **§13.2 The fixture schema.** "created with the tenant collation and `READ_COMMITTED_SNAPSHOT
  ON`, as spec 0011's provisioning does (§6.1), and change tracking …" (one 130-column line) ->
  "created as spec 0011 §6.7 states, with `READ_COMMITTED_SNAPSHOT ON` as provisioning sets it
  (spec 0011 §6.1). Change tracking is enabled …", rewrapped.

### Spec 0018

- **§3.1, `UserInvitationOutcome` sketch.** `Subject` comment "varchar(255)" -> "varchar(255)
  COLLATE Latin1_General_100_BIN2, as core.Users.Subject". The invite write-back joins `u.[Subject]
  = o.[Subject]` and `u.[Subject] <> o.[Subject]` (`core.Users.Subject` is BIN2, spec 0014 §3.1);
  without the type's own collation both comparisons raise Msg 468 under any database collation.
  The inventory missed this instance.

### ARCHITECTURE.md

- **Data Layer, "Migrations & seeding".** The collation clause in the provisioning sentence ->
  "creates the database (in the platform collation, below)"; new paragraph "**One collation for
  every database.**" after it: scope (catalog, tenants, identity, dev and test), the Amharic
  reason, set once and never altered, the Azure catalog clause and Bicep properties, the migrator
  creates and refuses, no model collation and no EF implicit creation, test servers keep their
  default, links to specs 0011 and 0010 Part A.
- **Identity, "Amendments to the identity server".** List gains "the identity database in the
  platform collation …, which the in-proc engine never creates because its store is the catalog".
- **Hosting on Azure, "Other notes".** New bullet "**Databases:**": the migrator creates a missing
  catalog and every tenant database in the shared pool; any database Bicep declares, the identity
  database among them, sets the platform collation properties.

### Checked, no edit

- Specs 0013–0017, 0019–0022: they use spec 0012's fixture database ("LocalDB or Testcontainers");
  spec 0012 §13.2 owns its creation. Spec 0018's Acme suite assertion lives in spec 0011 §10.
- `#temp` tables: none in the specs or `src`, so no `#temp` rule was added (facts, "Conflict
  risks"). `sys.databases` joins: none; §6.7's check reads values into the migrator instead.
- Explicit collations kept: `Latin1_General_100_BIN2` (catalog `Subject`, `Key`, `Handle`;
  `core.Users.Subject`), the UTF-8 JSON wire columns, the Queryex fixture's `_SC` columns.
- Spec 0010 §F.0/§F.15 QX4010 text rules: unchanged (D-1).
- Spec 0019 §10.3 "accent- or width-insensitivity" names an equivalence the platform collation
  lacks for accents; it is an example, not a rule, and the width/kana behaviour is not among the
  verified facts, so it was left.

## Open questions

1. **Who creates the SaaS catalog (`tellma-<slug>-catalog`)?** §6.7 lets the migrator create a
   missing catalog and lets Bicep declare it instead (then the migrator only checks it). If Bicep
   should never declare it, say so and §6.7's Infrastructure bullet narrows to the identity
   database.
2. **Who creates and migrates the shared identity database in production?** Spec 0003 is silent
   and production `Seed.ApplyMigrations` defaults to false. §A.11 covers both paths (Bicep, or the
   engine with `ApplyMigrations`), but no spec names the production path or the deploy step that
   migrates it.
3. **Does any identity database already exist in the server default?** §A.11 refuses one when the
   seeder migrates (Part A review flag 9); a deployed one would need a new database and a data
   copy, since Azure cannot convert it in place. If one exists, the Warning alternative wins.
4. **Keep the Queryex fixture's `_SC` text columns?** Under D-2 the database is
   `Latin1_General_100_CI_AS`, and `_SC` changes nothing for Amharic (all BMP). Keeping `_SC`
   means any `StringList` restriction (spec 0010 §F.1) or table variable compared with a fixture
   text column fails with Msg 468 (facts §4.4, probe P5). No such test exists today.
5. **Spec 0012 §8.1–§8.2 change detection drops case-only edits.** `EXCEPT` compares under the
   column collation, so under any CI collation "acme" -> "Acme" counts as unchanged and the
   `UPDATE` skips the row. Not caused by D-2; comparing string columns under
   `Latin1_General_100_BIN2` inside the `EXCEPT` would fix it.
6. **The full Azure statement has not run on a live Azure SQL Database.** Each clause and the
   clause order are verified from Microsoft Learn (facts §2.1); the first SaaS smoke deployment
   should confirm `CREATE DATABASE … COLLATE … (SERVICE_OBJECTIVE = ELASTIC_POOL(…)) WITH
   CATALOG_COLLATION = DATABASE_DEFAULT` once.

## Requests for other files

- **Specs 0001–0009 (frozen):** none. Spec 0001's text fixes no database collation; hardening the
  table-type sweep's join with `COLLATE DATABASE_DEFAULT` would be a code change only (facts,
  "Conflict risks").
- **Specs 0023–0035:** none; none of them creates or assumes a database.
- **Code that follows these specs (no spec edit):** the five fixtures of facts §5 add `COLLATE
  Latin1_General_100_CI_AS`; `IdentitySeeder` creates and checks per §A.11; the identity design-time
  factory, `Tellma.Core.EntityFrameworkCore.MigrationsHost` and `eng/queryex-inspection` create
  their LocalDB databases the same way or document the exception.

## Fix pass

Applied 27 October 2026 from `verify-collation.md` (C-01–C-14) and `verify-scenarios.md`
"## collation" (S-01–S-07). Files edited: specs 0010, 0011, 0012, 0019 and `ARCHITECTURE.md`.

- C-01 -> applied (§5.9 range §A.1–§A.11; new last bullet "the identity database's collation (§6.7)").
- C-02 -> applied (§6.7 opener defines "the platform collation").
- C-03 -> applied (bullet deleted; the reason is one more sentence in Decision 32).
- C-04 -> applied.
- C-05 -> applied, worded "The existing-database check runs on every clone…", since the new
  "A fresh database" bullet now sits between it and the check.
- C-06 -> applied.
- C-07 -> applied.
- C-08 -> applied, merged into §5.7's rewritten sentence (S-04), without "still".
- C-09 -> applied.
- C-10 -> applied.
- C-11 -> applied.
- C-12 -> applied (`**Tests:**` label deleted; Compatibility kept).
- C-13 -> applied (paragraph rewrapped).
- C-14 -> applied (paragraph rewrapped).
- S-01 -> applied, merged with S-03 into a new §6.7 bullet "A fresh database": the migrator runs
  `ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON` before migrating any fresh catalog, so a
  Bicep- or DBA-created catalog gets it too, not only a migrator-created one. §6.1 `migrate` row:
  "Creates a missing catalog and turns on read-committed snapshot in a fresh one (§6.7)". §10
  `Tellma.Core.IntegrationTests` gains `is_read_committed_snapshot_on = 1`. `ARCHITECTURE.md`
  "One collation for every database" gains "turns on read-committed snapshot isolation in every
  fresh one".
- S-02 -> applied. Names confirmed on Microsoft Learn's CREATE DATABASE page (SQL Server
  "Permissions": `CREATE DATABASE`, `CREATE ANY DATABASE` or `ALTER ANY DATABASE`; SQL Database
  "Permissions": the server-level principal login, the Microsoft Entra administrator, or a member
  of `dbmanager`). The connection is worded "the connection string it uses for that database
  (§3.5)", since §3.5 says the migrator runs under its own identity.
- S-03 -> applied as its own §6.7 bullet (with S-01) instead of inside "An existing database";
  the `provision` row reads "on a fresh database (§6.7)".
- S-04 -> rejected as proposed: its premise is wrong. `TellmaIdentityOptions.Mode`
  (`TellmaIdentityDeploymentMode { Standalone, InProc }`,
  `src/apps/Tellma.Identity/Options/TellmaIdentityOptions.cs:18`) exists, and the seeder already
  reads it (`IdentitySeeder.cs:78`, `:85`). The gap is closed instead by §5.7 listing `Mode =
  InProc` among the options `AddTellma` passes ("In that mode the engine never creates the
  catalog").
- S-05 -> applied, worded "sets `READ_COMMITTED_SNAPSHOT ON` on the new database", because the
  seeder is connected to `master` at that point, so `ALTER DATABASE CURRENT` would be wrong; the
  integration case gains `is_read_committed_snapshot_on` reads 1.
- S-06 -> rejected: same wrong premise as S-04, so no `Seed.CreateDatabase` option is added.
  §A.11's In-proc bullet names the existing switch (`TellmaIdentityOptions.Mode = InProc`, set by
  spec 0011 §5.7). The in-proc integration case composes from it.
- S-07 -> first half applied (the seeder's `master` connection). Second half (the statement is the
  engine's own copy) not applied, for Ahmad: its reason is false. `Tellma.Identity` references
  `Tellma.Core.Abstractions` (`src/apps/Tellma.Identity/Tellma.Identity.csproj:34`), and 0003 §2.1
  says nothing about Core packages. A shared home is therefore possible, so the choice between
  duplicating the statement and sharing it is scenario Note 2.
- Notes for Ahmad: `verify-collation.md` Notes 1–7 and `verify-scenarios.md` Notes 2–5 stand
  unchanged. None of them was applied.

## Readability pass

Applied 27 October 2026 per `.tmp/round-2/style-guide.md`. No meaning, name, number, section
number or cross-reference changed.

- 0011 Context, "Other specs" bullet: no change.
- 0011 §3.2: the one-sentence description split into bullets (Name, Connection, Lifecycle,
  Contents).
- 0011 §3.8 sketch comment: no change.
- 0011 §5.7: the `AddTellmaIdentity` options as a list; the `idsvr` and catalog-creation sentences
  grouped in one paragraph after the OIDC sentence (the paragraph had five sentences).
- 0011 §5.9 list: no change.
- 0011 §6.1 `migrate` and `provision` rows: no change.
- 0011 §6.7: opener split, active voice ("`CREATE DATABASE` sets the collation"); "Where it runs"
  rights as sub-bullets; "An existing database" reads as sub-bullets, the error naming "the value
  expected and the value found" (replaces the ambiguous "both values"); last "Development and
  tests" sentence reworded.
- 0011 §8 `Provisioning:CreateDatabaseTemplate` row: shortened; the example dropped, since
  §6.7's Example shows it.
- 0011 §10 suite rows: no change. Fixture paragraph: split at the semicolon.
- 0011 §11 Docs bullet: rewrapped the short line the editor pass left.
- 0011 Decision 32: the two long sentences split.
- 0010 Context, Goals, "How to read an amendment", Part A order item 8, placement table: no
  change.
- 0010 §A.11: opener split; "its two collation properties" -> "the two"; the engine-created path
  as three numbered steps; the existing-database error worded as spec 0011 §6.7; Compatibility
  split into short sentences ("created earlier" -> "already created").
- 0010 Part A — Testing: "the error names both" -> "names both collations"; rest no change.
- 0010 Part A — Definition of done, Review flag 9: no change.
- 0010 Part A — Decision 12: bold lead in active voice.
- 0010 §F.15, Part F — Testing: no change.
- 0012 §4.4: paragraph reordered as rule, consequence, example.
- 0012 §13.2: no change.
- 0018 §3.1: no change.
- 0019 §10.3: no change.
- `ARCHITECTURE.md` "Migrations & seeding" provisioning sentence: no change. "One collation for
  every database": a two-sentence lead (now defining "the platform collation") plus five
  bold-led bullets, links on their own line.
- `ARCHITECTURE.md` Identity amendments list: no change.
- `ARCHITECTURE.md` Hosting "Databases" bullet: split at the semicolon.
