# Inventory: places decisions D-2 to D-6 touch (round 1, follow-up 3)

Scope: `docs/specs/0010`–`0035` and `ARCHITECTURE.md`, at HEAD `76f8746` (the follow-up 2 collation
work is committed there, so `git diff HEAD` is empty). Line numbers are current.

File keys: **0010** = `docs/specs/0010-amendments-to-implemented-specs.md`;
**0011** = `docs/specs/0011-distribution-host-and-multitenancy.md`;
**0012** = `docs/specs/0012-data-access-layer.md`;
**0018** = `docs/specs/0018-core-and-gl-reference-stacks.md`; **ARCH** = `ARCHITECTURE.md`; other
specs by number.

Each entry: `file:line` — section — "quote" — decision(s), with a locating note where useful.

## D-2. The migrator creates every catalog (no Bicep for the catalog)

- `0011:1846-1847` — §6.7 Database creation and collation, **Infrastructure** bullet — "Bicep that
  declares a platform database (`Microsoft.Sql/servers/databases`) sets `collation:
  'Latin1_General_100_CI_AS'` and `catalogCollation: 'DATABASE_DEFAULT'`." — D-2 (named in D-2);
  also D-3 (it covers the identity database).
- `0011:1832-1833` — §6.7, **Who creates** — "The migrator creates every tenant database
  (`provision`, §6.1) and a missing catalog (`migrate`, before it takes the catalog lock)." — D-2
  (already states the migrator path; no "SaaS too" wording).
- `0011:1868-1875` — §6.7 example — "**Example —** Acme's SaaS catalog, with the template
  `(SERVICE_OBJECTIVE = ELASTIC_POOL(name = pool1))`: … `CREATE DATABASE [tellma-acme-catalog]
  COLLATE Latin1_General_100_CI_AS …`" — D-2 (the migrator creating the SaaS catalog); D-6a ("the
  template").
- `0011:596` — §3.2 The catalog database, **Lifecycle** — "Created as §6.7 states, then migrated by
  the migrator before any tenant database." — D-2 (defers to §6.7).
- `0011:1695` — §6.1 Commands, `migrate` row — "Creates a missing catalog and turns on
  read-committed snapshot in a fresh one (§6.7), then migrates it under
  `sp_getapplock('tellma:migrate:catalog')` (`idsvr` too in in-proc mode); …" — D-2; D-6c (see
  there).
- `0010:556-558` — §A.11 The identity database's collation, opening paragraph — "The identity
  database uses the platform collation as spec 0011 §6.7 states it: its `CREATE DATABASE`
  statement, or its two Bicep properties. Whichever path creates the database sets the collation.
  The engine's model stays as it is." — D-2/D-3 (Bicep), D-6b ("model stays as it is").
- `0010:560-562` — §A.11, **Standalone, created by infrastructure** — "Infrastructure that creates
  the database uses spec 0011 §6.7's statement. Bicep sets the two collation properties. This is
  the path for a database that needs a service tier or an elastic pool." — D-2/D-3.
- `ARCH:685` — Data Layer › Migrations & seeding, "One collation for every database", **Azure**
  bullet — "The statement adds `WITH CATALOG_COLLATION = DATABASE_DEFAULT`, so system names compare
  like data (the table-type sweep joins them). Bicep that declares a database sets the matching
  `collation` and `catalogCollation`." — D-2/D-3.
- `ARCH:686` — same list, **The migrator** bullet — "It creates a missing catalog and every tenant
  database itself, and turns on read-committed snapshot isolation in every fresh one. It refuses
  an existing database in another collation." — D-2 (consistent); D-4.
- `ARCH:1199` — Hosting on Azure › Other notes, **Databases** — "the migrator creates a
  distribution's missing catalog and every tenant database in the shared pool. Any database Bicep
  declares, the identity database among them, sets the platform collation properties (see
  Migrations & seeding)." — D-2 (named in D-2); D-3 (identity database).
- Bicep mentions that do not name a database (listed so the editor can judge "implies"):
  - `ARCH:220` — Platform repo layout — "`infra/` # Bicep for shared platform infra (Log Analytics,
    ASPs, SQL, …)" — D-2/D-3 ("SQL" = the server; no database named).
  - `ARCH:486` — Distribution repo layout — "`infra/` # Bicep for the distribution's Azure
    resources" — D-2 (no database named).
  - `ARCH:1082` — Tenant & Distribution Onboarding, **New distribution** — "The script provisions
    Azure resources via Bicep, registers the OIDC client with the identity provider, and
    configures DNS." — D-2 (no database named).
  - `ARCH:1102` — Control Plane › Distribution vs. tenant — "deployed by its own repo's Bicep +
    GitHub CI/CD across successive PRs. Azure resources are created by CI/CD when Bicep merges." —
    D-2 (no database named).
  - `ARCH:1144` — Hosting diagram — "Azure SQL server +<br/>shared elastic pool(s)<br/>holds every
    distribution's<br/>Catalog DB + tenant DBs" — D-2 (no creator named).
- No DBA creating a platform database is mentioned anywhere (the one "DBA" hit, `0013:463`, is
  about out-of-band writes).

## D-3. The production identity database: created and migrated by hand, for now

Who creates or migrates the shared (standalone) identity database:

- `0010:560-562` — §A.11, **Standalone, created by infrastructure** (quoted under D-2) — D-3:
  the path that becomes "created by an operator".
- `0010:563-570` — §A.11, **Standalone, created by the engine** — "With `Seed.ApplyMigrations` on,
  the seeder does this before `MigrateAsync`: 1. It connects to the server's `master` … 2. … It
  creates a missing one with spec 0011 §6.7's statement, without edition options, so EF's implicit
  creation never runs. 3. It sets `READ_COMMITTED_SNAPSHOT ON` …" — D-3, D-6b (the custom
  `master` path D-6b removes).
- `0010:574-576` — §A.11, **An existing database** — "Whenever the seeder migrates, it first reads
  the database's collations as spec 0011 §6.7 states. Any other value fails seeding, naming the
  database, the value expected and the value found." — D-3, D-4 (kept refusal).
- `0010:580-583` — §A.11 **Compatibility** (quoted under D-4) — D-3 ("A standalone engine then
  re-creates and re-seeds it on the next start (0003 §10.4)").
- `0010:175-176` — Part A intro, implementation order item 8 — "**§A.11** — the database collation
  ships before any identity database is deployed, since a deployed database keeps the collation it
  was created with." — D-3, D-4.
- `0010:632-635` — Part A — Testing, integration, **Database collation (§A.11)** — "A standalone
  engine with `Seed.ApplyMigrations` creates a missing database, `DATABASEPROPERTYEX(DB_NAME(),
  'Collation')` reads `Latin1_General_100_CI_AS`, and `is_read_committed_snapshot_on` reads 1." —
  D-3, D-6b.
- `0011:1830` — §6.7 intro — "The identity server's database follows the same rule (spec 0010
  §A.11)." — D-3.
- `0011:1846-1847` — §6.7 **Infrastructure** (quoted under D-2) — D-3 ("a platform database"
  includes the identity database).
- `ARCH:682` — "One collation for every database" — "Every database the platform creates uses
  `Latin1_General_100_CI_AS`, the platform collation: the catalog, each tenant database, the
  identity database, and the development and test databases that stand in for them." — D-3
  ("the platform creates" now includes an operator's hand creation).
- `ARCH:893` (end of the Identity amendments paragraph) — "and the identity database in the
  platform collation (see Migrations & seeding), which the in-proc engine never creates because
  its store is the catalog." — D-3, D-6c.
- `ARCH:1199` — Hosting, **Databases** (quoted under D-2) — D-3: "Any database Bicep declares, the
  identity database among them".
- `ARCH:1142` — Hosting diagram — "`IDP_SQL[("Identity DB<br/>(on the shared SQL server)")]`" —
  D-3 (no creator named; context only).

§A.11's paths, in order (`0010:550-583`): opening paragraph (556-558), **Standalone, created by
infrastructure** (560-562), **Standalone, created by the engine** (563-570), **In-proc** (571-573),
**An existing database** (574-576), **No model collation** (577-578), **Compatibility** (580-583).
Its header: `0010:552-554` — "**Target:** 0003 §2.2 (the Store row), §12, §16. **Requested by:**
spec 0011 §6.7. **Why:** every platform database shares one collation, and the server default
defines no order on Amharic text, such as users' names in the shared directory."

Candidate homes for the one "later" line (convenience tools reusing the migrator's approach):

- `0010:93-100` — Non-goals, **Part A** list — "service-account creation from a tenant …;
  distribution self-registration, and the operator surface beyond spec 0003 §11.4; the MCP tools,
  request filters and rate limits (spec 0016 §11); … the Manage pages' content, and every
  deferral of spec 0003 §17." — D-3.
- `0011:101-102` — Non-goals — "**Self-serve provisioning, sandbox cloning, the manifest source
  generator, the Builder tool, the bypass analyzer, `Recommends`/`Excludes`/slots** — later
  releases." — D-3.
- `0011:108-110` — Non-goals, **The distribution template** — "`dotnet new tellma-distribution`
  scaffolds a distribution's server and client projects together. A later distribution-tooling
  spec owns it; this spec fixes the shape it scaffolds (§1.2)." — D-3 (an existing "later
  tooling" seat).
- `ARCH:1082` — Onboarding, **New distribution** — "a later distribution-tooling spec owns it." —
  D-3 (the architecture's existing "later tooling" seat).
- `ARCH:1232-1234` — Rollout & Phasing intro — "this section records what is sequenced and why —
  deferrals are captured here rather than by rewriting the target." — D-3 (the architecture's
  deferral list).

## D-4. No identity database exists outside Ahmad's machine

- `0010:715-717` — Part A — Review flags, **flag 9** (the last flag; nothing follows it) — "9.
  **Refusing an identity database in another collation** (§A.11). Alternative: a Warning that
  keeps serving. Flips if a deployed identity database already holds users in the server default,
  since moving it needs a new database and a copy of its data." — D-4 (delete).
- Citations of Part A flag 9: none. (`0021:280` "review flag 9" is spec 0021's own flag.)
- `0010:692-694` — Part A — Decisions record, **Decision 12** (possible home for the one-clause
  reason) — "**Whoever creates the identity database sets its collation, never a migration; the
  engine creates its own database rather than letting EF do it** — Azure SQL Database cannot
  change a collation, and EF's implicit creation writes no `COLLATE` for a model without one
  (§A.11)." — D-4, D-6b.
- `0010:580-583` — §A.11 **Compatibility** — "a host with `Seed.ApplyMigrations` off sees no
  change. An identity database already created in the server default fails seeding once the
  seeder migrates it. A developer drops it. A standalone engine then re-creates and re-seeds it on
  the next start (0003 §10.4). For an in-proc store, the migrator's `migrate` re-creates it (spec
  0011 §6.6)." — D-4 (developer's database only), D-3, D-6c.
- `0010:574-576` — §A.11 **An existing database** (quoted under D-3) — D-4 (refusal stands).
- `0010:636` — Part A — Testing — "A database created in another collation fails seeding, and the
  error names both collations." — D-4.
- `0010:175-176` — Part A order item 8 (quoted under D-3) — D-4 ("before any identity database is
  deployed").
- Catalog and tenant refusal (stands, no escape hatch to add):
  - `0011:1848-1855` — §6.7 **An existing database** — "Before it migrates a database, the
    migrator reads: `DATABASEPROPERTYEX(DB_NAME(), 'Collation')`, which must be
    `Latin1_General_100_CI_AS`; on Azure SQL Database, the database's `catalog_collation_type_desc`
    … Any other value fails that database's command …" — D-4.
  - `0011:2204` — §10, `Tellma.Core.IntegrationTests` — "… and refuses an existing one created
    with another collation (§6.7)" — D-4.
  - `0011:2207` — §10, `Tellma.Distro.Acme.IntegrationTests` — "`provision` naming an existing
    database in another collation fails without migrating it" — D-4.
  - `ARCH:686` (quoted under D-2) — "It refuses an existing database in another collation." —
    D-4.
- Escape hatch or warning alternative for a database in another collation: none outside Part A
  flag 9.

## D-5. Amend spec 0001 (Part G) for column collations of class-derived table types

### Spec 0010's intro and front matter

- `0010:12-14` — Context — "Specs 0001–0009 are implemented and frozen. … When a later design
  needs one of them to change, the change is written here as an amendment. This spec is the one
  home for those amendments." — D-5 (context only).
- `0010:16-17` — Context — "It has one part per target spec. Each part carries its own Testing,
  Definition of done, Decisions record and Review flags, so it is implemented and merged on its
  own:" — D-5.
- `0010:19-25` — Context, the parts list — "**Part A — identity server (spec 0003).** It blocks
  spec 0011 … **Parts B–E — the component library (specs 0002, 0004, 0005, 0006).** … **Part F —
  Queryex (spec 0008).** It is needed by the data access layer (spec 0012), the permission
  evaluator (spec 0014), the report engine (spec 0033) and the client's `queryex/discover` (spec
  0029 §9)." — D-5 (add Part G).
- `0010:27` — Context — "Specs 0001, 0007 and 0009 need no amendment." — D-5 (loses 0001).
- `0010:29-51` — Context, "**Other specs that carry parts of this design**" — first entry
  `0010:31-32`: "spec 0011 §5.5, §5.7, §5.9, §6.7 — the distribution side of Part A: `/bff/login`
  forwards `ui_locales`, the in-proc seed entry, `IdentityInfo.AccountUrl`, the database
  collation." Other entries are per consumer (0016, 0018, 0023, 0024, 0025, 0012, 0013, 0029/0033/
  0035). — D-5 (Part G's consumers 0011 §3.8, 0018 §3.1, 0012 §4.4 belong here); D-6c (§5.7).
- `0010:53-84` — Goals, one bullet per part; Part A's ends `0010:61-62` "the sign-in language from
  the authorization request, and the identity database on the platform collation."; Part F's is
  `0010:81-84` "**Part F.** The Queryex changes the data access layer, the permission evaluator and
  the report engine need: the seven amendments moved from spec 0012 (§F.1–§F.7), …" — D-5 (add a
  Part G goal).
- `0010:86-114` — Non-goals: `0010:88-89` "**Editing specs 0001–0009.** Their text stays as merged;
  an amendment here supersedes only the section it names."; per-part lists "**Part A:**" (93),
  "**Parts B–E:**" (101), "**Part F:**" (108) — D-5 (a Part G list if needed).
- `0010:116-139` — How to read an amendment:
  - `0010:118-119` — "Every amendment has a letter-dotted number: the part's letter, then its place
    in the part (§A.1 … §A.11, §B.1 …)." — D-5.
  - `0010:124-129` — "Each amendment opens with three labels in one short paragraph: **Target** …
    **Requested by** … **Why** …" — D-5 (structure Part G follows).
  - `0010:131-135` — "Then it states the change, in this order: **The delta** — a sketch. A changed
    member's comment shows its old shape (`// old: …`). **The rules** … **Closing labels** —
    **Compatibility**, **Tests** and, in the library parts, **Budget**." — D-5.
  - `0010:137-139` — "Contract blocks are TypeScript and C# sketches: names and shapes are
    normative; imports, documentation comments, bodies and cancellation tokens are omitted." — D-5.
- `0010:141-150` — Vocabulary — "**Frozen spec** — one of specs 0001–0009, implemented and never
  edited." (143); the rest is library vocabulary (Entry point, Ceiling, Showcase). — D-5.

### Structure a Part G must mirror

- Part A front matter: `0010:152` "## Part A — Identity server (spec 0003)"; intro paragraphs
  (154-160); implementation order list (162-176); "**Terms used in Part A**" (178); "**Placement**"
  table (188-195).
- `0010:719` — "## Parts B–E: rules shared by the library amendments" — the only cross-part
  structural section (library parts only).
- Part F's top-level headings, in order (copy for Part G):
  - `0010:2647` — `## Part F — Queryex (spec 0008)`; intro `0010:2649-2653` — "Part F amends spec
    0008, the Queryex compiler in `src/core/Tellma.Core.Queryex`. … Every item is additive (§F.0)."
  - `0010:2655` — `### §F.0 Shared rules` (header line `0010:2657-2659`: "**Target:** … **Requested
    by:** every item below. **Why:** each item then states only its own change."), with bold
    sub-blocks "**Placement and order**" (2661), "**Vocabulary**" (2679), "**Compatibility**"
    (2696), "**Cache keys.**" (2711), "**Diagnostic codes.**" (2720), "**Reference interpreter and
    corpus**" (2736).
  - `0010:2753` … `0010:3404` — `### §F.1 Key-set restrictions` … `### §F.15 Measure evaluation in
    C#` (each opens "**Target:** … **Requested by:** … **Why:** …" and closes with
    "**Compatibility:** … **Tests:** …").
  - `0010:3659` — `### Part F — Testing`
  - `0010:3688` — `### Part F — Definition of done` (bullets **Engine**, **Surface**, **Behaviour**,
    **Compatibility**, **Tool**, **Benchmark**, **Docs**, **Not in scope of done**)
  - `0010:3709` — `### Part F — Decisions record`
  - `0010:3752` — `### Part F — Review flags` (ends the file at `0010:3775`; Part G goes after).
- A short part for proportion: Part E, `0010:2392-2396` intro ("Part E extends the entity picker …
  Every member is additive …"), then §E.1–§E.5, Testing (2599), Definition of done (2608),
  Decisions record (2628), Review flags (2640).

### Places that list spec 0010's parts or say which specs it amends

- `ARCH:893` — Identity — "**Amendments to the identity server** are Part A of
  docs/specs/0010-amendments-to-implemented-specs.md (Amendments to Implemented Specs, one part per
  implemented spec that later specs change), deltas to docs/specs/0003-identity-server.md …" —
  D-5 (generic; no part list).
- `ARCH:748` — Reports — "These are Queryex amendments, in spec 0010 Part F." — D-5 (single part).
- `ARCH:824` — UI component library — "Changes to components that already exist are amendments in
  docs/specs/0010-amendments-to-implemented-specs.md, Parts B–E." — D-5 (single range).
- `0023:24-25` — Context — "It also builds on spec 0010 Parts B–D. A change to an entry point that
  already exists is an amendment in spec 0010, never here." and `0023:59` "— spec 0010 Parts B–E."
  — D-5 (library range only; no full list).
- No spec 0011–0035 lists all of spec 0010's parts or says which frozen specs it amends; the rest
  cite one part each (0011:44, 103, 1634, 2296; 0012:29, 70, 99, 1796, 2112, 2133, 2206; 0013:85,
  1985; 0014:84, 1816; 0015:26, 105; 0016:84, 1917, 2155; 0018:1337, 1417).

### Where spec 0001 and table-type collation are described (Part G's neighbours)

- `0011:23-24` — Context — "Spec 0001 supplies the table types the catalog and the membership
  reconciliation ride on, and the `EXECUTE ON TYPE` grants the migrator emits." — D-5 (a Part G
  citation could join).
- `0012:20-25` — Context — "**Spec 0001 — table types.** A class opts in with `[TableType]`; its UDTT
  is a row image addressed only through the model; every bulk list (`IdList`, `BigIdList`,
  `GuidList`, `StringList`) is a plain `[TableType]` class." — D-5.
- `0012:169` — §1.3 conventions table, `TableTypeConvention` — "`[TableType]` on a type →
  `HasTableType(name?, schema?)`; `[ExcludeFromTableType]` on a property → the column's exclusion
  (spec 0001's fluent configuration, so the attributes live in `Tellma.Core.Abstractions` without
  an EF edge, §2.2); inherited by a leaf" — D-5 (how the platform maps table-type attributes today).
- `0012:280-281` — §2.2 Annotations — "`public sealed class TableTypeAttribute : Attribute; // on
  type; inherited; optional Name and Schema (spec 0001); TableTypeConvention (§1.3) maps it onto
  the model`" / "`ExcludeFromTableTypeAttribute … on property; inherited`" — D-5.
- `0012:313` — §2.2 annotation table — "`[TableType]`, `[ExcludeFromTableType]` |
  `TableTypeConvention` (§1.3) maps them onto spec 0001's table-type configuration …" — D-5.
- `ARCH:652` — UDTT generation, point 2 **Derived row image** — "The UDTT's columns, store types,
  facets, nullability, collation, and order are taken from the relational model EF already built
  for the table" — D-5 (states entity-derived types carry their source column's collation).
- `ARCH:656` — UDTT generation — "Operation-specific shapes with no paired table … are
  **standalone table types**, declared ad hoc through the fluent builder or derived from a plain
  `[TableType]`-annotated class that doubles as the TVP row DTO" — D-5.

### Standalone table type sketches compared with an explicitly collated column

- `0011:844-849` — §3.8 The membership directory — "`public sealed class TenantMembershipRecord //
  [TableType] standalone -> [catalog].[TenantMembershipList]` … `public string Subject { get; set;
  } // varchar(255) COLLATE Latin1_General_100_BIN2 (§7.3)`" — D-5 (declare via the mechanism).
- `0011:1928-1931` — §7.3 `catalog.TenantMemberships` — "Table type `[catalog].[TenantMembershipList]
  (TenantId int, Subject varchar(255) COLLATE Latin1_General_100_BIN2, IsActive bit)` is the
  standalone `[TableType]` class `TenantMembershipRecord` registered on `CatalogDbContext`" — D-5.
- `0011:1923` — §7.3 table, `Subject` row — "`PK_TenantMemberships (Subject, TenantId)`; `COLLATE
  Latin1_General_100_BIN2`" — D-5 (the collated column compared with).
- `0018:401-408` — §3.1 Service and operations — "`[TableType("UserInvitationOutcomeList")]
  public sealed class UserInvitationOutcome // standalone; the invite write-back row` … `public
  string? Subject { get; set; } // varchar(255) COLLATE Latin1_General_100_BIN2, as
  core.Users.Subject`" — D-5.
- `0014:305` — §3.1 `core.Users`, `Subject` row — "`COLLATE Latin1_General_100_BIN2`;
  `UX_Users_Subject (Subject) WHERE Subject IS NOT NULL INCLUDE (Kind, IsActive, State)`" — D-5
  (the collated column `UserInvitationOutcome.Subject` is compared with).
- `0012:754-761` — §4.4 Standalone table types intro — "Standalone shapes are plain `[TableType]`
  classes in `Tellma.Core.Abstractions.TableTypes`, registered through spec 0011's `Model<T>()` and
  mapped by `TableTypeConvention` (§1.3) exactly as an entity's attribute is; …" — D-5.
- `0012:780-783` — §4.4 — "A string column of a standalone type or a table variable declares the
  collation of any explicitly collated column a statement compares it with. Otherwise it takes the
  database collation (spec 0011 §6.7), and the comparison fails to compile with Msg 468.
  `TenantMembershipList.Subject` and `UserInvitationOutcomeList.Subject` declare
  `Latin1_General_100_BIN2` for this reason." — D-5 (cite Part G).
- Other explicitly collated columns: `catalog.Sessions` `Key`, `Handle`, `Subject` (`0011:1937-1939`),
  read and written only through scalar parameters (`0011:2008-2027`); no table type compares with
  them.

## D-6a. Rename `Tellma:Provisioning:CreateDatabaseTemplate` → `Tellma:Sql:CreateDatabaseOptions`

- `0011:1842-1845` — §6.7 **The statement** — "On Azure SQL Database (`EngineEdition = 5`) the
  migrator appends the options of `Tellma:Provisioning:CreateDatabaseTemplate`, when set, and then
  `WITH CATALOG_COLLATION = DATABASE_DEFAULT`." — D-6a.
- `0011:1868` — §6.7 example — "**Example —** Acme's SaaS catalog, with the template
  `(SERVICE_OBJECTIVE = ELASTIC_POOL(name = pool1))`:" — D-6a.
- `0011:2057` — §8 Configuration JSON — "`"Provisioning": { "CreateDatabaseTemplate": null },`" —
  D-6a (the section disappears).
- `0011:2045-2048` — §8 JSON `"Sql"` block — "`"Sql": { "Profiles": { … }, "MaxPoolSizePerTenant":
  20, "MaxWarmTenants": 10, "AllowSqlPassword": false, "DatabasePrefix": "tellma-acme",
  "ApplicationPrincipal": "tellma-acme" },`" — D-6a (where the new key lands).
- `0011:2079` — §8 key table — "`Provisioning:CreateDatabaseTemplate` | §6.7; the options the
  migrator appends after `COLLATE` when it creates a catalog or tenant database on Azure SQL
  Database; null on-prem." — D-6a.
- `0011:2074` — §8 key table, `Sql:*` row — "`Sql:Profiles`, `MaxPoolSizePerTenant`,
  `MaxWarmTenants`, `AllowSqlPassword` | §3.5. `DatabasePrefix` names provisioned databases …;
  `ApplicationPrincipal` is the web identity created at provisioning (§6.4)." — D-6a (neighbour
  row).
- `0011:2203` — §10, `Tellma.Core.Tests` — "the database-creation statement on SQL Server and on
  Azure SQL Database, with and without a template (§6.7)" — D-6a.
- `ARCH:1204` — Hosting, **Configuration sections** — "every platform option binds under `Tellma:`
  — `Tellma:Sql`, `Tellma:Tenancy`, `Tellma:Identity`, …" — D-6a (lists no `Tellma:Provisioning`;
  already consistent).
- `0010:568` — §A.11 — "It creates a missing one with spec 0011 §6.7's statement, without edition
  options" — D-6a (refers to the options without naming the key).

## D-6b. Every platform model sets the database collation

Statements that say the opposite:

- `0011:1832-1835` — §6.7 **Who creates** — "The web host never creates a database (§3.4). Nothing
  relies on EF's implicit creation in `Migrate()`, and no model calls `UseCollation` (Decision
  32)." — D-6b (named in D-6b).
- `0011:2393-2401` — Decisions record, **Decision 32** — "… Azure SQL Database cannot change a
  collation, and on SQL Server existing columns keep theirs. A model collation (`UseCollation`)
  would reach only EF's own `CREATE DATABASE`, which writes no Azure options. A later change to it
  would emit `ALTER DATABASE … COLLATE`. …" — D-6b.
- `0010:556-558` — §A.11 opening — "The engine's model stays as it is." — D-6b.
- `0010:577-578` — §A.11 **No model collation** — "`TellmaIdentityDbContext` sets no database
  collation, so `Tellma.Identity.Migrations` gains no migration (spec 0011 Decision 32)." — D-6b.
- `0010:193` — Part A Placement row — "`Tellma.Identity.Migrations` | `src/apps/Tellma.Identity.Migrations/`
  | none: no table changes; metadata documents are cached in memory; the model sets no collation
  (§A.11)" — D-6b.
- `0010:692-694` — Part A **Decision 12** (quoted under D-4) — D-6b ("the engine creates its own
  database rather than letting EF do it").
- `ARCH:687` — "One collation for every database", **No EF collation** — "No EF model sets a
  database collation, and no path relies on EF's implicit creation." — D-6b.
- `ARCH:684` — **Set once** — "`CREATE DATABASE` sets the collation, and nothing alters it: Azure
  SQL Database cannot change it, and on SQL Server existing columns would keep the old one." —
  D-6b (consistent; "no migration alters").

§A.11's custom creation path (to simplify): `0010:563-570` (quoted under D-3); the in-proc bullet
`0010:571-573` (under D-6c); tests `0010:613` "**The creation statement (§A.11)** on SQL Server
and on Azure SQL Database." and `0010:632-637`; DoD `0010:647` "§A.1–§A.3 and §A.5–§A.11
implemented"; Docs `0010:658-660` "… the account entry URL and the identity database's
collation."; Placement `0010:192` "`Tellma.Identity` … the code of §A.1–§A.3 and §A.5–§A.11".

Where each model is specified (owner of the `UseCollation` call):

- Tenant model — `0012:144-151` — §1.3 The DbContext and the options helper — "`TellmaDbContext`
  (`Tellma.Core.Data`) is the platform-owned EF context. Its model is built from the leaf types
  composition registered … The context is used for model building and for migrations (through
  spec 0011's migrator) …" — D-6b.
- Tenant model — `0012:153-159` — §1.3 — "`UseTellmaSqlServer(options, connectionString)` is the
  one place provider options are applied so that every host and the migrator build one model:
  `UseSqlServer(...)`, `UseHierarchyId()`, `UseTableTypes()` (spec 0001), `UseCompatibilityLevel(160)`,
  …" — D-6b.
- Tenant model — `0012:161-179` — §1.3 — "`ConfigureConventions` and the model-finalizing
  conventions the context installs, in order:" followed by the convention table — D-6b.
- Tenant model — `0011:1803-1807` — §6.5 The tenant model host — "`AddTellma` registers
  `TellmaDbContext` (`Tellma.Core.Data`), whose `OnModelCreating` applies every
  `ModelContributionItem` in feature order and calls `UseTableTypes(...)`. … Spec 0012 owns
  everything the context exposes beyond this registration." — D-6b.
- Catalog model — `0011:1879-1884` — §7 The catalog schema intro — "All tables live in the catalog
  database, schema `catalog`, created by the platform-owned migrations in `Tellma.Core.Migrator`
  (history table `catalog.__EFMigrationsHistory`) through `CatalogDbContext`
  (`Tellma.Core.Tenancy`). No IDENTITY; `datetimeoffset(3)`; …" — D-6b.
- Catalog model — `0011:123` (§1.1 `Tellma.Core` row: "`Tellma.Core.Tenancy` (`CatalogDbContext`,
  registry, …)") and `0011:506` (§2.3 "then the platform registers `CatalogDbContext`, …") — D-6b.
- Identity model — `0010:577-578` (quoted above) — D-6b.
- Owner for the collation value: `0011:122` — §1.1 `Tellma.Core.Abstractions` row — "namespaces
  `.Tenancy` (§3, §4) and `.Composition` (§2): every enum, record and contract a module or
  distribution names; …" — D-6b (candidate home); today the value is stated as a literal in
  `0011:1827-1829` "Every database the platform creates uses one collation,
  `Latin1_General_100_CI_AS`, called the platform collation." Other citations of "the platform
  collation (spec 0011 §6.7)": `0012:781`, `0010:3551-3552` (§F.15 text `min`/`max`).

`Migrate()`, EF creation, read-committed snapshot and fresh databases:

- `0011:1695` — §6.1 `migrate` row — "… then for every `Active`, `ReadOnly` or `Provisioning`
  tenant …: the tenant lock of §7.5, `Migrate()`, the schema fingerprint row …" — D-6b.
- `0011:1696` — §6.1 `provision` row — "Creates the database (§6.7); `ALTER DATABASE CURRENT SET
  READ_COMMITTED_SNAPSHOT ON` and `SET QUERY_STORE = ON (…)` on a fresh database (§6.7); …" — D-6b.
- `0011:1793-1794` — §6.4 — "`-- provisioning only, fresh database (a no-op on Azure SQL, where it
  is the default): ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON;`" — D-6b.
- `0011:1836-1841` — §6.7 **Where it runs** — "The migrator runs the statement in the target
  server's `master`, as its own batch. … Its principal holds the right to create databases" — D-6b
  (stays: the migrator keeps its own statement).
- `0011:1856-1859` — §6.7 **A fresh database** — "A database with no `__EFMigrationsHistory` table
  is fresh, whoever created it. Before it migrates a fresh catalog, the migrator runs `ALTER
  DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON` …" — D-6b.
- `0011:1863-1866` — §6.7 **Development and tests** — "Every fixture that creates a database uses
  the SQL Server statement. Test servers keep their own default collation: LocalDB cannot change
  it, and no suite sets `MSSQL_COLLATION` on the container. …" — D-6b (fixture rule changes).
- `0012:732` — §4.3 — "After `Migrate()` succeeds, and only when the database's migration history
  ends at the migrator's own last migration …" — D-6b (context only; not creation).
- `ARCH:678` — Migrations & seeding — "(*Applying* migrations needs no Design packages regardless —
  scaffolded migration files call runtime-side operations and `Migrate()` lives in the runtime
  relational package; Design is needed only to *scaffold*.)" — D-6b (context only).
- `ARCH:680` — Migrations & seeding — "Provisioning also creates the database (in the platform
  collation, below), enables read-committed snapshot isolation and creates the `tellma_app`
  database role with its grants" — D-6b.

Design-time factories and `dotnet ef`:

- `0011:1683-1686` — §6.1 sketch — "`public abstract class TellmaDesignTimeDbContextFactory<TContext>
  : IDesignTimeDbContextFactory<TContext> { public abstract void Compose(TellmaBuilder builder); }`"
  — D-6b.
- `0011:1707-1710` — §6.1 — "The migrator is the single DDL-privileged executable;
  `MigrationsAssembly` is the distribution's Migrator project. The design-time factory
  `AcmeDesignTimeFactory : TellmaDesignTimeDbContextFactory<TellmaDbContext>` overrides `Compose` so
  `dotnet ef` builds the context from the same composition without host discovery." — D-6b, D-6c.
- `0011:125`, `0011:180` — §1.1 Migrator row "`TellmaDesignTimeDbContextFactory<TContext>`"; §1.2
  layout "`AcmeDesignTimeFactory.cs` # derives TellmaDesignTimeDbContextFactory<TellmaDbContext>" —
  D-6b.
- `0018:1096-1102` — §7 Reference distribution migrations — "the **migrations** are the
  distribution's (spec 0011: only the distribution generates and ships migrations, through
  `AcmeDesignTimeFactory`). … generated by `dotnet ef migrations add InitialTenant --context
  TellmaDbContext` and reviewed by hand." — D-6b (initial tenant migration: no collation operation).
- `0018:1129-1131` — §7 **Not in a migration** — "RCSI (`provision` sets it), the `tellma_app` role
  and grants (recomputed by the migrator after every run), `core.VersionTags` seeding …" — D-6b
  (the database collation is likewise not in a migration).
- `ARCH:510`, `ARCH:643`, `ARCH:645`, `ARCH:678` — "`<Slug>DesignTimeFactory.cs` # derives
  TellmaDesignTimeDbContextFactory<TellmaDbContext>"; "EF tooling (`dotnet ef migrations add`)
  targets the migrator project"; "the migrator's design-time `DbContext` factory"; "the migrator is
  also the `dotnet ef` design-time target" — D-6b (context only).

Test fixtures that create databases:

- `0011:2235-2237` — §10 Fixture — "the catalog database is created per test class as §6.7 states,
  then migrated by the platform migrations. Tenant databases in the distribution suite come from
  `provision`." — D-6b (an EF-model fixture may let EF create it).
- `0011:2204` — §10 `Tellma.Core.IntegrationTests` — "`migrate` creates a missing catalog in
  `Latin1_General_100_CI_AS` with `is_read_committed_snapshot_on = 1` …; catalog migrations apply
  from empty and are idempotent" — D-6b.
- `0011:2207` — §10 Acme integration — "`migrate` then `provision` against the tier's SQL Server
  from an empty server; the seed tenants exist and are `Active`; the catalog and every tenant
  database report `Latin1_General_100_CI_AS`" — D-6b.
- `0011:2203` — §10 `Tellma.Core.Tests` — "the database-creation statement on SQL Server and on
  Azure SQL Database, with and without a template (§6.7)" — D-6b (also D-6a); a home for "no
  migration of any platform model alters the database collation" among unit pins, or
  `0011:2206` Acme `Web.Tests` ("Composition parity (the web host and the migrator build
  byte-identical models)").
- `0012:2015-2018` — §13.2 The fixture schema — "The database is created as spec 0011 §6.7 states,
  with `READ_COMMITTED_SNAPSHOT ON` as provisioning sets it (spec 0011 §6.1). Change tracking is
  enabled on every fixture table …" — D-6b (`TellmaFixtureDbContext` has an EF model, `0012:2006`
  "built through `UseTellmaSqlServer` with the platform conventions").
- `0012:1999-2002` — §13.1 — "`TELLMA_TEST_SQL`, carries the connection string; absent on Windows
  it defaults to LocalDB …, absent on Linux the fixture starts a `Testcontainers.MsSql` container."
  — D-6b (context).
- `0010:593-594` — Part A — Testing — "The integration and E2E fixtures create their databases
  with spec 0011 §6.7's SQL Server statement." — D-6b (identity fixtures have an EF model).
- `0010:3676-3677` — Part F — Testing, `Tellma.Core.Queryex.IntegrationTests` — "Its database is
  created as spec 0011 §6.7 states. The fixture's text columns keep the collation they declare
  (0008 §18.3)." — D-6b (the no-EF-model fixture; keeps §6.7's statement).
- Secondary fixture mentions that defer to spec 0012's fixture and do not say how the database is
  created: `0015:2571` "`Category=Integration`; LocalDB or Testcontainers (`Testcontainers.MsSql`),
  one database per class."; `0020:1854-1855` "LocalDB on Windows, Testcontainers SQL Server on
  Linux, both with RCSI on and off"; `0021:1308` "(LocalDB, RCSI on and off)"; `0013:1915-1916`,
  `0014:1768`, `0017:1156`, `0019:1735-1736`, `0022:566`, `0028:1174` — D-6b (likely untouched).

## D-6c. `migrate` owns `idsvr`; the web process never migrates

Statements that the web process or the engine migrates `idsvr`:

- `0011:1518-1521` — §5.7 In-proc identity mode — "The identity engine's own migrations migrate
  `idsvr`. It is the one schema the web process may migrate, because the engine already owns that
  path. The engine never creates the catalog in this mode (spec 0010 §A.11). §3.4's catalog check
  runs before the engine's seeding, so a missing catalog prints the `migrate` command (§6.6)." —
  D-6c (named in D-6c).
- `0011:1509-1513` — §5.7 — "`Tellma:Identity:Mode = InProc` makes `AddTellma` call
  `AddTellmaIdentity` with these options: `Mode = InProc`; `PathBase = /id` and `Issuer =
  {PublicOrigin}/id`; the identity store pointed at the catalog connection, in schema `idsvr`." —
  D-6c (the list of what `AddTellma` passes; no migrations setting today).
- `0011:1192-1193` — §5.1 `AddTellma` — "and — when `Tellma:Identity:Mode = InProc` —
  `AddTellmaIdentity` (§5.7)." — D-6c.
- `0010:571-573` — §A.11 **In-proc** — "(`TellmaIdentityOptions.Mode = InProc`, set by spec 0011
  §5.7). The store is the catalog database, which the distribution's migrator creates (spec 0011
  §6.7). The engine never creates it: a missing database fails seeding, so the host does not
  start." — D-6c.
- `0010:637` — Part A — Testing — "An in-proc engine facing a missing database fails seeding and
  creates nothing." — D-6c.
- `0010:582-583` — §A.11 Compatibility — "For an in-proc store, the migrator's `migrate` re-creates
  it (spec 0011 §6.6)." — D-6c.
- `0011:1695` — §6.1 `migrate` row — "then migrates it under `sp_getapplock('tellma:migrate:catalog')`
  (`idsvr` too in in-proc mode)" — D-6c (already says `migrate` migrates `idsvr`; contradicts
  `0011:1518-1519`).
- `0011:597-598` — §3.2 **Contents** — "Schema `catalog` (§7) and, in in-proc identity mode, the
  identity engine's `idsvr` schema with its own migration history." — D-6c.
- `0011:135-137` — §1.1 — "carries pre-generated migrations for the catalog alone, because no
  distribution contributes to the catalog schema and every catalog must be identical for the
  identity engine's schema to sit beside it" — D-6c (context).
- `ARCH:893` (quoted under D-3) — "which the in-proc engine never creates because its store is the
  catalog." — D-6c.
- `ARCH:889` — Identity, **In-proc identity mode** — "the reference distribution references
  `Tellma.Identity` and runs in-proc in Development, and the in-proc store is the distribution's
  **catalog database**, schema `idsvr`." — D-6c (context; no migration statement).

"Migrations never run in the web process" and the web host's behaviour:

- `ARCH:678` — Migrations & seeding — "deployed as its own versioned artifact and invoked on demand,
  **never run in the web process**. … because the web app triggers the migrator rather than
  migrating itself (see *Hosting* below), the web publish needs neither the Design packages nor
  the migration-apply path at all." — D-6c (holds with no exception).
- `ARCH:680` — "The migrator has four commands: `migrate` (converge the catalog and every tenant),
  …" — D-6c (`migrate` also converges `idsvr` in in-proc mode).
- `ARCH:700` — Hosting — a Container Apps Job — "One image, one DDL-privileged managed identity (so
  the web app and the pipeline runner never hold `CREATE DATABASE`/DDL rights), one code path" —
  D-6c (consistent once the engine stops migrating from the web).
- `ARCH:702` — Version ordering — "An in-process or web-bundled migrator would instead carry the
  web app's own version, … that is precisely why the migrator is a separate, ahead-deployed
  artifact." — D-6c (context).
- `ARCH:1215` — CI/CD — "the release pipeline triggers a fleet execution of the distribution's
  migrator job (`migrate`: catalog migrations, tenant migrations, and versioned provisioning steps
  across every tenant DB …)" — D-6c (`idsvr` not named).
- `ARCH:539`, `ARCH:641` — "its migrations ship in `Tellma.Core.Migrator`"; "the **catalog**
  database's migrations are platform-owned and ship inside `Tellma.Core.Migrator`" — D-6c
  (context: which migrations `migrate` applies).
- `0011:697-703` — §3.4 **Startup** — "On success the catalog's migration history is compared with
  the platform's latest catalog migration: a behind or missing catalog fails startup with a message
  naming the exact migrator command (in Development, a missing catalog database prints `dotnet run
  … -- migrate`); the web host never migrates." — D-6c (where "`idsvr` missing or behind" fits).
- `0011:1822` — §6.6 Local development — "A missing catalog at web startup prints that exact
  `migrate` command instead of a stack trace." — D-6c.
- `0011:1813-1816` — §6.6 step 2 — "`dotnet run --project … Migrator -- migrate` creates
  `Tellma.dev.acme.catalog` and provisions tenants 1 and 2 from `Tellma:Seed:Tenants` with the
  development administrator." — D-6c (context).
- `0011:530-534` — §2.3 — "`Tellma.Core.AspNetCore` adds `host.data-protection` (§5.8),
  `host.endpoint-audit` (§5.3) and `host.identity` (in-proc secrets, §5.7) … A check that needs the
  catalog and finds it unreachable reports nothing: reachability is a readiness concern (§3.4)" —
  D-6c (existing startup-check seat).
- `0011:1605-1607` — §5.9 — "**`GET /health/ready`** (registry snapshot loaded and within
  `MaxStaleness`; Data Protection keys loadable; the authority's discovery document cached)" —
  D-6c (context).

Migrator references (the migrator must reach the identity engine's migrations):

- `0011:204-205` — §1.2 **Migrator project references** — "`Tellma.Distro.Acme` and
  `Tellma.Core.Migrator` only, so a migrator build carries no ASP.NET package, never builds the
  Client esproj and needs no Node." — D-6c (conflicts with applying `Tellma.Identity` migrations
  if they need `Tellma.Identity`, a Razor Class Library per `ARCH:278`).
- `0011:182` — §1.2 layout — "`Tellma.Distro.Acme.Migrator.csproj` # → Tellma.Distro.Acme,
  Tellma.Core.Migrator" — D-6c.
- `0011:200-201` — §1.2 **Web project references** — "`Tellma.Distro.Acme`,
  `Tellma.Core.AspNetCore`, `Tellma.Defaults.Azure.AspNetCore`, `Tellma.Identity` (in-proc mode),
  …" — D-6c.
- `0011:125` — §1.1 `Tellma.Core.Migrator` row — "References `Tellma.Core`,
  `Tellma.Core.EntityFrameworkCore.Design`, `Microsoft.EntityFrameworkCore.Design`" — D-6c.
- `ARCH:234`, `ARCH:348`, `ARCH:509` — `Tellma.Core.Migrator` "references Tellma.Core + the EF
  Design packages"; distribution Migrator "References Tellma.Distro.<Slug> + Tellma.Core.Migrator
  … never the Web project or the esproj" — D-6c.
- `ARCH:278-279` — Platform repo layout — "`Tellma.Identity/` # .csproj — OpenIddict + ASP.NET
  Core Identity engine (Razor Class Library); referenced by Tellma.Identity.Web and by
  distributions running in-proc" — D-6c (no `Tellma.Identity.Migrations` listed).

Decisions records and Definition of done:

- `0011:2361-2363` — **Decision 20** — "**In-proc identity stores in the catalog database's `idsvr`
  schema with stable configured secrets** — the only distribution-wide database, and
  multi-instance standalone deployments cannot survive per-boot secrets (§5.7)." — D-6c.
- `0011:2364-2366` — **Decision 21** — "**Provisioning is a migrator command with a versioned step
  seam; the web application only records intent and calls a trigger** — DDL rights never reach
  the web identity, and seeds re-run by version rather than by force (§6.1, §6.2)." — D-6c.
- `0011:2312-2314` — **Decision 4** — "an unreachable catalog is a readiness concern, never a
  startup failure, so a slot swap cannot crash-loop (§2.3, §3.4)." — D-6c (context for a behind
  `idsvr`).
- `0010:692-694` — Part A **Decision 12** — D-6c only insofar as it explains the engine creating
  its database.
- `0011:2254-2262` — §11 Definition of done, **Behavior** — "the host pipeline, … in-proc mode, …
  (§5); the migrator commands, steps, trigger, local-development story and database creation of
  §6" — D-6c, D-6b.
- `0011:2286-2289` — §11 **Docs** — "identity (the reference distribution references
  `Tellma.Identity` and selects in-proc by configuration; the in-proc store is the catalog
  database's `idsvr` schema; the distribution owns its BFF)" — D-6c.

Tests:

- `0011:2207` — §10 Acme integration — "full sign-in through the in-proc engine (reusing the
  identity suite's OIDC flow client)" and "readiness before and after catalog availability" —
  D-6c (`idsvr` comes from `migrate`).
- `0011:2204` — §10 `Tellma.Core.IntegrationTests` — "`migrate` creates a missing catalog …;
  catalog migrations apply from empty and are idempotent" — D-6c (no `idsvr` pin today).
- `0010:637` (quoted above) — D-6c.

## Not found

- `EnsureCreated`: no mention in specs 0010–0035 or `ARCHITECTURE.md`.
- `dotnet ef database update`: no mention anywhere (only `dotnet ef migrations add` and `dotnet ef
  migrations script --idempotent`).
- A `UseCollation` call or a model collation specified for any model: none; only the negations at
  `0011:1834-1835`, `0011:2397`, `0010:577-578`, `0010:193`, `ARCH:687`.
- `OnModelCreating`, conventions or model-builder text for `CatalogDbContext`: none beyond the §7
  intro (`0011:1879-1884`) and its registration (`0011:123`, `0011:506`).
- Model-builder text for `TellmaIdentityDbContext` beyond §A.11's bullet: none (spec 0003 owns it).
- A design-time factory for the catalog or identity contexts: none named; only the generic
  `TellmaDesignTimeDbContextFactory<TContext>` and Acme's tenant factory.
- An options class for `Tellma:Provisioning` (or a `Tellma:Sql` options sketch): none; the §8 JSON
  and key table are the only declarations.
- The engine setting `AddTellma` would pass to turn migrations off (`Seed.ApplyMigrations`): not in
  spec 0011; only spec 0010 names it (`0010:563`, `0010:580`, `0010:633`). What the engine's seeding
  still does at startup is stated only by reference to frozen 0003 §10.4 (`0010:582`).
- Rights of the web identity on the catalog database: not stated (relevant to D-6c: no DDL is
  needed once `migrate` owns `idsvr`).
- An escape hatch or warning alternative for a catalog or tenant database in another collation:
  none (D-4 needs no removal there).
- Citations of Part A Review flag 9: none.
- Another standalone table type with a string column compared with an explicitly collated column:
  none beyond `TenantMembershipList.Subject` and `UserInvitationOutcomeList.Subject` (checked
  `UserPreferenceList.Key`, `VersionTagList.Name`, the job, notification and outbox lists; their
  partner columns carry no explicit collation).
- A spec among 0011–0035 that lists all of spec 0010's parts or says which frozen specs it amends:
  none (see D-5 for the per-part citations).
- An existing Non-goal or "later" line about convenience tools for the identity database or
  on-prem deployment: none (candidate homes listed under D-3).
