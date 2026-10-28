# Verify spec 0010 (follow-up 3)

Checked: D-3, D-4, D-5, D-6b and D-6c are applied, and D-6a has no mention in 0010. Part G has the
same structure as its sibling parts (intro, Placement, §G.1 with Target / Requested by / Why /
delta / rules / Compatibility / Tests, then Testing, DoD, Decisions, Flags). Its citations of 0001
§2, §3, §5 and the Testing plan exist and match. The cited sections of 0003 (§2.1, §2.2, §10.4,
§12, §16), 0011 (§3.8, §5.5, §5.7, §5.9, §6.1, §6.7, §10), 0012 (§1.3, §2.2, §4.4) and 0018 (§3.1)
exist and say what 0010 claims. The stale terms are gone, and the shared names match the design
and specs 0011, 0012, 0018 and `ARCHITECTURE.md`. The file is LF with no BOM, cites no `.tmp`
path, and `git diff --stat HEAD -- docs/specs/000*` is empty.

## docs/specs/0010-amendments-to-implemented-specs.md

- [A-01] (must) 0010 §G.1, bullet "Character columns only." - "Any other store type, `json`
  included, fails model finalization, naming the type, the column and the store type. SQL Server
  would otherwise fail the migration with Msg 447." -> replace the last sentence with "Without the
  check, SQL Server fails the migration with Msg 447, and the library silently replaces a `json`
  column's collation with the wire form's." Why: facts-tabletypes §2.3 says a JSON column's
  collation is replaced on both routes, so `json` never reaches Msg 447. §4 shows Msg 447 only for
  `int` and `datetimeoffset`. Part G Review flag 2 already says "keep replacing it silently".
- [A-02] (should) 0010 §G.1 Compatibility - "The character-column check moves a failure from
  `CREATE TYPE` to model finalization." -> append ", and refuses a `json` column with a collation,
  which today succeeds with the wire form's collation". Why: facts-tabletypes §2.3. Calling the
  change "additive" while saying it only moves a failure hides the one case where a model that
  builds today stops building.
- [A-03] (should) 0010 Part A — Testing, integration "The store (§A.11)", third item - "With
  `Seed.ApplyMigrations` off, a missing database, an empty one and a store whose last history row
  is deleted each fail the store check" -> "a missing database, an empty one created in the
  platform collation, and a store whose last history row is deleted …". Why: the store check
  (§A.11, step 1) refuses a server-default database before step 3 runs. An empty database in the
  server default, which a raw `CREATE DATABASE` gives (r4 facts-collation §5), therefore fails on
  collation and never names `Seed.MigrateCommand`.
- [A-04] (should) 0010 §A.11, bullet "Development and tests." - "EF writes no Azure options, so
  only development and test hosts turn `Seed.ApplyMigrations` on." -> "Only development and test
  hosts turn `Seed.ApplyMigrations` on (Decision 13)." Why: the stated reason covers only creating
  a database on Azure. A deployed engine on SQL Server, or one whose store already exists, needs no
  Azure options, yet D-3 and D-6c have an operator or the migrator migrate every deployed store.
  Rationale belongs in the Decisions record (docs/AGENTS.md).
- [A-05] (should) 0010 §A.11, header "Why" - "A distribution's web process never migrates, so the
  engine checks a store that something else migrates." -> "A deployed web process never migrates,
  so the engine checks a store that the migrator or an operator migrates." Why: the store check
  also covers the standalone deployed store, which the operator migrates (D-3, "Standalone,
  deployed" bullet). "A distribution's web process" names only the in-proc case.
- [A-06] (should) 0010 Goals, Part A bullet - "and the identity database on the platform
  collation, migrated outside the web process." -> "…, migrated outside the web process once
  deployed." Why: in §A.11's "Development and tests" bullet, a standalone development host migrates
  in its own web process through `Seed.ApplyMigrations`. Decision 13 says the engine migrates
  "only in development and tests".
- [A-07] (should) 0010 Part A — Definition of done, Projects - "`DatabaseCollations` added to
  `src/core/Tellma.Core.Abstractions`; no new project." -> "`DatabaseCollations` added to
  `src/core/Tellma.Core.Abstractions`, and that project's README lists it; no new project." Why:
  the root AGENTS.md README rule. The Abstractions README lists what the project holds
  (facts-identity §2, `README.md:14-20`). The same bullet's "each README updated" covers only the
  two identity projects.
- [A-08] (should) 0010 Part G — Definition of done, Docs - "the architecture document's UDTT
  section names the column collation of a class-derived type." -> "the architecture document names
  the column collation of a class-derived type where it describes standalone table types." Why:
  docs/AGENTS.md ("Docs must not reference specific sections of ARCHITECTURE.md") and the style
  guide's "Never" list. Part F — Definition of done words this the same way ("where it describes
  Queryex").
- [A-09] (should) 0010 Part G — Definition of done, Compatibility - "every existing test passes
  unchanged except the integration suite's type counts." -> "every existing test passes unchanged
  except the integration suite's type counts and the snapshot round-trip model, which gain the
  collated type." Why: §G.1 Tests ("the snapshot round-trip model gains a collated class-derived
  column") and the `.Design.Tests` row of Part G — Testing.
- [A-10] (should) 0010 §G.1, Example lead-in - "the catalog's membership type, whose `Subject`
  meets a `Latin1_General_100_BIN2` column. The class carries no library `[TableType]`, so the
  registration names the type:" -> "the registration that spec 0012's conventions make for the
  catalog's membership type, whose `Subject` meets a `Latin1_General_100_BIN2` column. The class
  carries spec 0012's `[TableType]`, not the library's, so the call names the type:". Why: spec
  0011 §7.3 says `CatalogDbContext` maps `[TableType]` and `[Collation]` through spec 0012's
  conventions (spec 0012 §1.3, the `CollationConvention` and `TableTypeConvention` rows). As
  written, the example reads as a call the catalog writes by hand.
- [A-11] (should) 0010 Context, "Other specs that carry parts of this design", first entry - "spec
  0011 §5.5, §5.7, §5.9, §6.1, §6.7 — the distribution side of Part A:" -> "spec 0011 §1.2, §5.5,
  §5.7, §5.9, §6.1, §6.7, §10 — the distribution side of Part A: the Web and Migrator projects'
  reference to `Tellma.Identity.Migrations`; …; the database collation; the collation pin." Why:
  §A.11's "every host of the engine references `Tellma.Identity.Migrations`, in-proc ones included"
  is carried by spec 0011 §1.2 (Web and Migrator references). Part A — Testing relies on spec 0011
  §10's collation pin. Under the style guide's Context rule, the list names every section that
  carries part of this design.

## Notes for Ahmad

1. **An in-proc host with an unreachable catalog.** §A.11 says "A failed check stops the host, as
   any failed seeding does". Spec 0011 §3.4 starts the host not-ready on a reachability failure,
   so a slot swap must not crash-loop. In in-proc mode the store check's first read hits the
   catalog, so the two rules disagree. The design (open question 2) recommended leaving this until
   on-prem delivery is designed. It needs your call.
2. **The migrator references `Tellma.Identity.Migrations`** (spec 0011 Review flag 23). This
   brings ASP.NET into an in-proc distribution's migrator. If you choose the idempotent-script
   alternative instead, §A.11 changes: the `TellmaIdentityStore` sketch, the "In-proc" bullet and
   "The migrator's entry".
3. **Isolated standalone deployments** (0003 §2: same binary, its own database, on Azure or
   on-prem). §A.11's "Standalone, deployed" bullet names only "the shared identity database once
   for the fleet". Presumably an operator creates and migrates each isolated database the same way.
   Should the bullet say "every deployed standalone identity database"?
4. **Noticed outside 0010, not changed.**
   - Spec 0012 §2.2 sketches `TableTypeAttribute` with no constructor ("optional Name and
     Schema"), but spec 0018 §3.1 writes `[TableType("UserInvitationOutcomeList")]` with a
     positional name.
   - Design open question 3: spec 0012 §4.4 registers plain row classes through spec 0011's
     `Model<T>()`, which requires `IEntityTypeConfiguration<>`.
