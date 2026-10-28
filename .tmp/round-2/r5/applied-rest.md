# Follow-up 3: E2's applied edits (specs 0011–0035, ARCHITECTURE.md)

Applied from `decisions.md` and `design.md` §1, §4–§8. Every edit used the Edit tool; files stay
LF, UTF-8 without BOM (`git ls-files --eol`: `w/lf`). No spec among 0013–0017 or 0019–0035
changed (design §6, §8: checked, none needed).

## Spec 0011 (`docs/specs/0011-distribution-host-and-multitenancy.md`)

| Section | Before → after | Decision |
|---|---|---|
| Context, "Other specs" list | §A.11 entry "the identity database's collation, which follows §6.7" → "the identity database's collation and migrations, and the store check an in-proc host runs (§5.7, §6.7)"; new last entry "spec 0010 §G.1 — the builder call behind `TenantMembershipList.Subject`'s collation (§7.3)" | D-5, D-6c |
| §1.1 table, `Tellma.Core.Abstractions` "Owns" | appended "; `Hosting.DatabaseCollations` (§6.7)" | D-6b |
| §1.2 layout, Migrator csproj comment | `→ Tellma.Distro.Acme, Tellma.Core.Migrator` → `…, Tellma.Identity.Migrations` | D-6c |
| §1.2 **Web project references** | "`Tellma.Identity` (in-proc mode)" → "`Tellma.Identity` and `Tellma.Identity.Migrations` (in-proc mode; the engine's store check reads the migrations, spec 0010 §A.11)" | D-6c |
| §1.2 **Migrator project references** | "`Tellma.Distro.Acme` and `Tellma.Core.Migrator` only, so a migrator build carries no ASP.NET package…" → the two plus `Tellma.Identity.Migrations` for in-proc; no esproj, no Node; ASP.NET Core's shared framework arrives only through the identity engine | D-6c |
| §1.3 Migrator `Program.cs` | `RunAsync(args, Slug, Compose)` → `RunAsync(args, Slug, Compose, TellmaIdentityStore.MigrateAsync)` | D-6c |
| §1.3 prose | "neither the composition library nor the migrator carries an ASP.NET package" → "the composition library carries no ASP.NET package" (paragraph re-wrapped) | D-6c |
| §3.2 **Contents** | "`idsvr` schema with its own migration history" → "`idsvr` schema, which `migrate` migrates (§6.1); its migration history is `dbo.__EFMigrationsHistory`" | D-6c |
| §3.8 sketch | `Subject` comment "varchar(255) COLLATE Latin1_General_100_BIN2 (§7.3)" → `[Collation("Latin1_General_100_BIN2")]` line + comment "varchar(255), as catalog.TenantMemberships.Subject (§7.3)" | D-5 |
| §5.7 options list | store bullet gains `MigrationsAssembly = Tellma.Identity.Migrations` (tables in `idsvr`); new bullet `Seed.ApplyMigrations = false`, `Seed.MigrateCommand` = §3.4's `migrate` command | D-6c |
| §5.7 paragraph | "The identity engine's own migrations migrate `idsvr`… the one schema the web process may migrate…" → `migrate` applies the engine's migrations (§6.1), the web process never migrates; catalog check first, then the engine's store check (spec 0010 §A.11) fails a missing or behind `idsvr` naming the command; seeding then writes rows only (scopes, `Distribution` entry, administrators of spec 0003 §10.4) | D-6c |
| §5.9 Part A list (see Deviations 1) | "the identity database's collation (§6.7)" → "the identity database's collation and migrations (§5.7, §6.7)" | D-6c |
| §6.1 sketch | `RunAsync` gains `Func<string, Task>? migrateIdentityStore = null` (exit-code comment kept) | D-6c |
| §6.1 `migrate` row opening | "…then migrates it under `sp_getapplock(…)` (`idsvr` too in in-proc mode)" → "…(§6.7). Then, under `sp_getapplock(…)`, it migrates the catalog and, in in-proc mode, `idsvr` through `migrateIdentityStore` (§5.7); registers …" | D-2, D-6c |
| §6.1 paragraph after the table | added: `migrateIdentityStore` receives the catalog connection string; in in-proc mode `migrate` without it exits 1 naming `Tellma.Identity.Migrations` | D-6c |
| §6.6 step 2 | "creates `Tellma.dev.acme.catalog`" → "…with its `idsvr` schema" | D-6c |
| §6.6 closing | added "; a missing or behind `idsvr` fails startup naming it (§5.7)" | D-6c |
| §6.7 intro | "Every database the platform creates…; The identity server's database follows the same rule" → "Every platform database…", the identity database listed with the others (spec 0010 §A.11) | D-3 |
| §6.7 sketch (new) | `DatabaseCollations.Platform` constant in `Tellma.Core.Abstractions.Hosting` | D-6b |
| §6.7 **Who creates** | migrator creates tenants and a missing catalog; "Nothing relies on EF's implicit creation…, and no model calls `UseCollation`" → migrator creates every distribution's catalog (SaaS and on-prem) and every tenant database; no infrastructure template declares a platform database; the web host never creates one | D-2, D-6b |
| §6.7 **Every model carries it** (new) | catalog, tenant and identity models call `UseCollation(DatabaseCollations.Platform)`; EF's creation writes the same `COLLATE` and RCSI; EF writes no Azure options or `CATALOG_COLLATION`, so the migrator keeps its statement | D-6b |
| §6.7 **No migration alters it** (new) | first migrations carry no collation operation; a change would scaffold `ALTER DATABASE … COLLATE`; pinned in §10 | D-6b |
| §6.7 **The statement** | "the options of `Tellma:Provisioning:CreateDatabaseTemplate`" → "`Tellma:Sql:CreateDatabaseOptions`" | D-6a |
| §6.7 **Infrastructure** | deleted (Bicep alternative) | D-2, D-3 |
| §6.7 **Development and tests** | "Every fixture that creates a database uses the SQL Server statement" → EF may create a database whose model sets the platform collation; a fixture with no EF model runs the statement | D-6b |
| §6.7 example caption | "with the template `(…)`" → "with `Tellma:Sql:CreateDatabaseOptions` set to `(…)`" | D-6a |
| §7 intro | added "Its model sets the platform collation (§6.7)." (paragraph re-wrapped) | D-6b |
| §7.3 table-type sentence | "…registered on `CatalogDbContext` (the physical name…)" → "…registered on `CatalogDbContext`, whose model maps its `[TableType]` and `[Collation]` with spec 0012's conventions (spec 0012 §1.3; the physical name…)" | D-5 |
| §8 JSON | `"Provisioning": { "CreateDatabaseTemplate": null }` deleted; `"Sql"` block gains `"CreateDatabaseOptions": null` | D-6a |
| §8 key table | `Provisioning:CreateDatabaseTemplate` row deleted; `Sql:CreateDatabaseOptions` row added after `Sql:Profiles…`; `Identity:Mode…` pointer "§5.5, §5.7" → "§5.5, §5.7, §6.1" | D-6a, D-6c |
| §10 `Tellma.Core.Tests` | "with and without a template (§6.7)" → "with and without `CreateDatabaseOptions` (§6.7); the collation pin over the catalog migrations" | D-6a, D-6b |
| §10 `Tellma.Distro.Acme.Web.Tests` | appended "; the collation pin over Acme's tenant migrations" | D-6b |
| §10 `Tellma.Distro.Acme.IntegrationTests` | after the in-proc sign-in item: `migrate` applies the engine's migrations to `idsvr` and a second run applies none; a web host whose `idsvr` lacks the last migration fails startup naming `migrate` | D-6c |
| §10 **The collation pin** (new paragraph before Fixture; see Deviations 2) | no-database test: design-time model's `GetCollation()` is `DatabaseCollations.Platform`; `HasPendingModelChanges()` false; no `AlterDatabaseOperation` changing `Collation`; runs over catalog, Acme tenant and identity migrations | D-6b |
| §10 Fixture | "created per test class as §6.7 states, then migrated" → "created per test class by `Migrate()` over the platform migrations, in the platform collation (§6.7)" | D-6b |
| §11 **Docs**, identity item | "…the catalog database's `idsvr` schema;" → "…`idsvr` schema, which `migrate` migrates;" | D-6c |
| §11 **Not in scope of done** | "the identity-server amendments (spec 0010 Part A)" → "the identity-server and table-type amendments (spec 0010 Parts A and G)" (bullet re-wrapped) | D-5 |
| Decision 20 | → "…`idsvr` schema, migrated by `migrate`, with stable configured secrets": web process never migrates, migrator references the engine's migrations (§1.2, §5.7, §6.1) | D-6c |
| Decision 26 | "the composition library and the migrator carry no ASP.NET package" → "the composition library carries no ASP.NET package" | D-6c |
| Decision 32 | rewritten per design §4.4: every model carries the collation; first migration carries none; migrator keeps its statement; no infrastructure template creates a platform database | D-2, D-6b |
| Review flag 23 (new) | the migrator references the identity engine's migrations; alternative: an idempotent identity script in `Tellma.Core.Migrator` | D-6c |

## Spec 0012 (`docs/specs/0012-data-access-layer.md`)

| Section | Before → after | Decision |
|---|---|---|
| Context, spec 0001 bullet | appended "A standalone class gives a column a collation through spec 0010 Part G (§4.4)." | D-5 |
| Non-goals | "**The Queryex engine amendments** — spec 0010 Part F (…§11.2)" → "**The Queryex and table-type amendments** — spec 0010 Parts F and G (…§4.4, §11.2)" | D-5 |
| §1.3 first paragraph | added "The model sets the database collation with `modelBuilder.UseCollation(DatabaseCollations.Platform)` (spec 0011 §6.7)." (re-wrapped) | D-6b |
| §1.3 conventions table | new `CollationConvention` row before `TableTypeConvention`: entity → EF `UseCollation`, copied by the row image (spec 0001 §2); standalone class → `TableTypeBuilder.UseCollation` (spec 0010 §G.1) | D-5 |
| §2.2 sketch | new `CollationAttribute(string name)` line after `ExcludeFromTableTypeAttribute` | D-5 |
| §2.2 annotation table | new `[Collation]` row | D-5 |
| §4.4 last paragraph | "…declare `Latin1_General_100_BIN2` for this reason" → "A standalone class declares it with `[Collation]` (§2.2, spec 0010 §G.1); … declare `Latin1_General_100_BIN2` this way" | D-5 |
| §13.2 fixture | "created as spec 0011 §6.7 states, with `READ_COMMITTED_SNAPSHOT ON` as provisioning sets it (spec 0011 §6.1)" → model sets the platform collation as `TellmaDbContext`'s does, so `Migrate()` creates the database in it with RCSI (spec 0011 §6.7) | D-6b |
| §14 **Not in scope of done** | "the Queryex engine amendments (spec 0010 Part F)" → "the Queryex and table-type amendments (spec 0010 Parts F and G)"; bullet re-wrapped (it had one over-long line) | D-5 |

## Spec 0018 (`docs/specs/0018-core-and-gl-reference-stacks.md`)

| Section | Before → after | Decision |
|---|---|---|
| §3.1 sketch | `UserInvitationOutcome.Subject` comment "varchar(255) COLLATE Latin1_General_100_BIN2, as core.Users.Subject" → `[Collation("Latin1_General_100_BIN2")]` line + comment "varchar(255), as core.Users.Subject" | D-5 |
| §7 **Not in a migration** | "RCSI (`provision` sets it), …" → "The database collation and RCSI (`provision` sets both, spec 0011 §6.7), …" (re-wrapped) | D-6b |

## ARCHITECTURE.md

| Place | Before → after | Decision |
|---|---|---|
| Platform repo layout (`src/apps/`) | new line `Tellma.Identity.Migrations/` after `Tellma.Identity/`: the engine's migrations and design-time factory; referenced by `Tellma.Identity.Web` and, in-proc, a distribution's Web and Migrator | D-6c |
| Library architecture rule 8 | Web: "`Tellma.Identity` (in-proc)" → "`Tellma.Identity` with `Tellma.Identity.Migrations` (in-proc)"; migrator: adds "and, for the in-proc identity store, `Tellma.Identity.Migrations`" | D-6c |
| Distribution layout, Migrator comment | adds "(+ Tellma.Identity.Migrations when the distribution runs the identity engine in-proc)" | D-6c |
| UDTT generation, standalone sentence | adds "(the builder's `UseCollation` gives a class-derived column a collation, spec 0010 Part G)" | D-5 |
| Migrations & seeding, commands sentence | "`migrate` (converge the catalog and every tenant)" → "…the catalog, its in-proc identity schema `idsvr`, and every tenant" | D-6c |
| "One collation for every database" | opener "Every database the platform creates" → "Every platform database"; **Set once** adds "No migration carries a collation change."; **Azure** loses the Bicep sentence; **The migrator** → creates every distribution's catalog and every tenant database on every engine, no infrastructure template; **No EF collation** → **Every EF model carries it**; new **The identity database** (operator creates the shared one, migrates by hand; in-proc store is the catalog) | D-2, D-3, D-6b |
| Identity › In-proc identity mode | appended: the migrator applies the engine's migrations to `idsvr` with `migrate`; the web process never migrates; the engine only checks `idsvr` is current at startup | D-6c |
| Identity › Amendments paragraph (see Deviations 3) | ending "…which the in-proc engine never creates because its store is the catalog" → "…with a store check at every start that refuses another collation and, outside development and tests, a missing or behind store" | D-3, D-6c |
| Hosting › Other notes › **Databases** | migrator creates a missing catalog…; "Any database Bicep declares, the identity database among them…" → migrator creates every distribution's catalog and every tenant database; no Bicep declares a platform database; an operator creates and migrates the shared identity database by hand, once for the fleet | D-2, D-3 |

## Deviations

1. **Spec 0011 §5.9, the Part A list** (not in the design's edit list). Its last bullet named
   §A.11 as "the identity database's collation (§6.7)". §A.11 now also carries
   `TellmaIdentityStore` and the store check that §5.7 and §6.1 depend on, so the bullet reads "the
   identity database's collation and migrations (§5.7, §6.7)".
2. **Spec 0011 §10, the collation pin.** The design wrote `Model.GetCollation()`. EF's
   `GetCollation(this IReadOnlyModel)` throws `RuntimeModelMissingData` on a `RuntimeModel`
   (efcore `src/EFCore.Relational/Extensions/RelationalModelExtensions.cs:497-500`), and
   `DbContext.Model` is the runtime model. The pin therefore reads the design-time model
   (`IDesignTimeModel`, efcore `src/EFCore/Metadata/IDesignTimeModel.cs:22`). The paragraph is
   split into bullets (style guide: no sentence with two semicolons); "(rows above)" became "(the
   rows above)".
3. **ARCHITECTURE.md, Identity amendments ending.** The design's "refuses another collation and a
   missing or behind store" is not true of a development or test host: with
   `Seed.ApplyMigrations` on, spec 0010 §A.11's store check migrates a missing or behind store.
   The text says "…refuses another collation and, outside development and tests, a missing or
   behind store."
4. **Minor wording.** Spec 0011 §5.7 cites "spec 0003 §10.4" (the design wrote "0003 §10.4"; spec
   0011 cites frozen specs with the word "spec"). §6.7 **Every model carries it** uses parentheses
   instead of dashes around the three creation paths. No name, owner or section number changed.

## Open questions

1. **`ARCHITECTURE.md` Migrations & seeding, first paragraph** (left unchanged, as design §7
   says): "the web publish needs neither the Design packages nor the migration-apply path at
   all". An in-proc web publish now carries `Tellma.Identity.Migrations` so the store check can
   count pending migrations; it never applies them (`Seed.ApplyMigrations = false`, spec 0011
   §5.7). Whether "migration-apply path" should be read as excluding that assembly is Ahmad's
   call; no edit made.
2. Design §9's three questions stand as written there (flag 23 records the first); nothing in
   this pass changes them.

## Requests for other files

None. Spec 0010's §A.11 (`TellmaIdentityStore`, `Seed.MigrateCommand`, the store check), §G.1,
Part G and "Part A — Testing" exist under the names cited above (checked in the working tree).

## Fix pass

Inputs: `verify-rest.md` (B-01…B-11) and `verify-scenarios.md` (S-01…S-08, S-13; S-09…S-12 are
spec 0010's and belong to the 0010 pass). Edits by the Edit tool; files stay LF, UTF-8 without
BOM; no `.tmp` citation.

- B-01 -> applied: Review flag 24 and "(standalone identity mode)" on the Acme readiness item. Also
  one §5.7 bullet ("An unreachable catalog") so the body states what the flag records.
- B-02 -> applied: "the tenant model's `MigrationsAssembly`"; the paragraph became a lead sentence
  plus bullets (it reached eight sentences with S-07).
- B-03 -> applied (§10 `Tellma.Core.Tests`).
- B-04 -> applied.
- B-05 -> applied.
- B-06 -> applied.
- B-07 -> applied, keeping one clause the finding dropped: `CATALOG_COLLATION` "without which spec
  0001's table-type sweep fails with Msg 468". It is the only stated why for that clause.
- B-08 -> applied (both 0012 §1.3 rows).
- B-09 -> applied (0012 §13.1 Unit row).
- B-10 -> applied (0024 Decision 2).
- B-11 -> applied (closes E2's Open question 1).
- S-01 -> applied, with three corrections to the finding's text:
  - the role is created before the catalog migration, not after it: the migration's
    `GRANT EXECUTE ON TYPE` needs it (the finding's Notes 1);
  - §6.4 gains `GRANT SELECT ON OBJECT::[dbo].[__EFMigrationsHistory]` for the in-proc catalog,
    because the store check reads it through the web identity;
  - §3.6's passwordless bullet, the `Tellma.Core.IntegrationTests` row and `ARCHITECTURE.md`'s
    ad-hoc SQL answer now name the catalog too.
- S-02 -> applied as the constant `CatalogMigrations.Latest`, also listed in §1.1's `Tellma.Core`
  row. The alternative (catalog migrations in `Tellma.Core`) is for Ahmad.
- S-03 -> applied, worded as "`AddTellma` registers the registry's hosted service before it calls
  `AddTellmaIdentity`".
- S-04 -> applied.
- S-05 -> applied.
- S-06 -> applied.
- S-07 -> applied, with LocalDB defaults `TellmaTenantDesign` and `TellmaCatalogDesign` (the finding
  said only "LocalDB"), and `CatalogDesignTimeFactory` added to §1.1's `Tellma.Core.Migrator` row.
  All names are proposals Ahmad may rename.
- S-08 -> applied. It pairs with S-10 (spec 0010 Part G — Testing), which is not in this pass.
- S-13 -> applied (`ARCHITECTURE.md`, "New distribution").

For Ahmad (not applied as decisions; see the verifiers' notes):

1. In-proc host with an unreachable catalog: recorded as flag 24; design a wait or skip now?
   (verify-rest Notes 1)
2. The call site that registers standalone classes, given `Model<T>()`'s
   `IEntityTypeConfiguration<>` constraint (verify-rest Notes 2).
3. `[Column(TypeName = "varchar(255)")]` on the two `Subject` sketches (verify-rest Notes 3).
4. Review flag 23 (the migrator references `Tellma.Identity.Migrations`) is still open.
5. The catalog's application role versus direct grants (verify-scenarios Notes 1); the role pattern
   was applied.
6. The on-premises DBA who pre-creates databases and withholds `CREATE ANY DATABASE`
   (verify-scenarios Notes 4).
7. Pre-existing: there is no command to relocate a restored tenant, and an orphaned on-premises
   `tellma_app_user` after a restore (verify-scenarios Notes 6).

Requests for other files: none beyond S-09…S-12, which the 0010 pass owns.

## Readability pass

Edits by the Edit tool; meaning, names, numbers, section numbers and cross-references unchanged;
files stay LF, UTF-8 without BOM. Only text this round changed was rewritten.

Spec 0011:

- Context, "Other specs" list: no change.
- §1.1 table: no change.
- §1.2 **Web project references**: the long parenthetical moved into its own sentence ("For in-proc
  mode it also references … whose migrations the engine's store check reads").
- §1.2 **Migrator project references**: split into two sentences; the in-proc reason leads the second.
- §1.3 sketch and paragraph: no change.
- §3.2 **Contents**: split; "its migration history" became "whose migration history is the catalog's
  `dbo.__EFMigrationsHistory`" (removes the ambiguous "its").
- §3.4 **Startup**: the `CatalogMigrations.Latest` sentence split in two.
- §3.5 **The application role**: the grant list became three bullets under a two-sentence lead.
- §3.6, §3.8 sketch, §5.9: no change.
- §5.7: the startup order became a numbered list (registry load, store check, seeding); the
  unreachable-catalog consequence is a closing sentence; first sentence split.
- §6.1 `migrate` row: the catalog phase's semicolon chain became short sentences.
- §6.1 `status` row: "(the catalog's, `idsvr`'s in in-proc mode, and each tenant's)".
- §6.1 paragraph after the table: lead sentence split; **Design-time connections** pairs each
  factory with its variable and LocalDB default in two sub-bullets (the "or … or" pairing was
  ambiguous).
- §6.4 SQL comments, `provision` row: no change.
- §6.6 closing: split; "naming it" became "naming the same command".
- §6.7 **Who creates**: split the three-qualifier sentence; `migrate` and `provision` each own a
  sentence.
- §6.7 **Every model carries it**, **No migration alters it**, **The statement**, **A fresh
  database**, sketch, example caption: no change.
- §6.7 **Development and tests**: first sentence split ("when that database's model sets the
  platform collation. Every platform model does, and so does spec 0012's fixture model.").
- §7 intro, §8: no change.
- §7.3: the long sentence split; the hash clause and the conventions sentence stand alone.
- §10 `Tellma.Core.IntegrationTests` row: "the catalog's role existing before" → "; the catalog's
  role exists before".
- §10 **The collation pin**: "An initial migration's operation" → "An initial migration's
  `AlterDatabaseOperation`".
- §10 other rows, Fixture, §11: no change.
- Decision 20: dropped the clause restating §1.2 ("the migrator references the engine's migrations
  for it"); "the catalog is the only distribution-wide database" as a sentence.
- Decision 26: no change.
- Decision 32: bold lead down to one semicolon; the Msg 468 clause became its own sentence.
- Review flag 23: the dense parenthetical became "That keeps ASP.NET out of the migrator at the cost
  of a build step and a parity test."
- Review flag 24: no change.

Spec 0012:

- Context, Non-goals, §1.3 paragraph, §2.2, §13.1, §14: no change.
- §1.3 `CollationConvention` row: parallel "on an entity, …; on a standalone class, …".
- §4.4 last paragraph: semicolon split into two sentences.
- §13.2: split into two sentences.

Spec 0018 (§3.1, §7) and spec 0024 (Decision 2): no change.

ARCHITECTURE.md:

- Platform repo layout, rule 8, Migrations & seeding first and second paragraphs, Identity
  amendments, New distribution, Databases, Permissions: no change.
- Distribution layout, Migrator comment: the parenthetical read as if `Tellma.Core.Migrator` carried
  `Tellma.Identity.Migrations`; the three references are now listed in order.
- UDTT generation: the inserted parenthetical became a closing sentence ("A class-derived column
  gets a collation through the builder's `UseCollation` …").
- "One collation for every database": **The migrator** puts the refusal before the infrastructure
  sentence; **The identity database** split into two sentences.
- Identity › In-proc identity mode: last sentence split.
