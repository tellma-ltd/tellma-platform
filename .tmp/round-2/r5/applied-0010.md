# Applied to spec 0010 (E1, follow-up 3)

File: `docs/specs/0010-amendments-to-implemented-specs.md` (3776 -> 3957 lines; Part G is ~120 of
the growth, §A.11 ~40). LF, UTF-8 without BOM, no `.tmp` citation. Every edit by the Edit tool.

## Edits

### Front matter (design §2.1)

1. **Context, parts list.** Part F was last -> new bullet "**Part G — table types (spec 0001).**"
   naming spec 0011 §3.8, spec 0018 §3.1 and spec 0012's `[Collation]` annotation. (D-5)
2. **Context.** "Specs 0001, 0007 and 0009 need no amendment." -> "Specs 0007 and 0009 need no
   amendment." (D-5)
3. **Context, Other specs, first entry.** "spec 0011 §5.5, §5.7, §5.9, §6.7 … the in-proc seed
   entry, `IdentityInfo.AccountUrl`, the database collation" -> adds §6.1; "the in-proc seed entry
   and store options; … `migrate` applying the in-proc store's migrations; the database
   collation". (D-6c)
4. **Context, Other specs, new last entry.** -> "spec 0012 §1.3, §2.2, §4.4, spec 0011 §3.8, spec
   0018 §3.1 — the users of Part G: …". (D-5)
5. **Goals, Part A.** "…and the identity database on the platform collation." -> "…, migrated
   outside the web process." (D-6c)
6. **Goals, new Part G bullet** after Part F (§G.1). (D-5)
7. **Non-goals, Part A list.** Last item's "." -> ";" and new last item: tools that create and
   migrate the shared identity database, and on-prem deployment tooling, a later release reusing
   the migrator's approach (§A.11). The single "later" line. (D-3)
8. **How to read an amendment.** "in the library parts, **Budget**" -> "in Parts B–E,
   **Budget**". Nothing else there changed. (D-5)

### Part A (design §3)

9. **Order list, item 8.** "the database collation ships before … since a deployed database …" ->
   "the identity database's collation and migrations ship before any identity database is
   deployed: a deployed database keeps the collation it was created with." (D-3, D-4)
10. **Terms used in Part A.** New term "**Store check** — the seeder's first step at every start:
    the store's collation, then its migrations (§A.11)." (D-6c)
11. **Placement.** `Tellma.Identity.Migrations` row "none: … the model sets no collation (§A.11)"
    -> "regenerated: one `Initial` scaffolded from the model with the platform collation, and
    `TellmaIdentityStore` (§A.11); no table changes; metadata documents are cached in memory". New
    row `Tellma.Core.Abstractions` | `src/core/Tellma.Core.Abstractions/` | `DatabaseCollations`,
    as spec 0011 §6.7 declares it (§A.11). `Tellma.Identity` row and "No new package is
    referenced." unchanged. (D-6b)
12. **§A.11, whole section replaced.** Title "The identity database's collation" -> "The identity
    database's collation and migrations". Target gains 0003 §2.1 (the Migrations row); Requested
    by gains spec 0011 §5.7, §6.1; Why gains "a web process never migrates". Removed: the Bicep /
    "created by infrastructure" path, the engine's `master`-connection creation path, "The
    engine's model stays as it is", "No model collation … gains no migration (spec 0011 Decision
    32)", the in-proc "the engine never creates it" wording. Added: a sketch of
    `TellmaIdentityStore.MigrateAsync` and `TellmaIdentitySeedOptions.MigrateCommand`; bullets
    **The model** (`UseCollation(DatabaseCollations.Platform)`), **The migrations** (re-baselined,
    one `Initial`, hand-written index kept), **The store check** (collation, then EF's
    `MigrateAsync` when `Seed.ApplyMigrations` is on, else pending migrations fail naming
    `Seed.MigrateCommand`), **Development and tests** (EF creates, so only dev/test hosts turn
    `ApplyMigrations` on), **Standalone, deployed** (operator's three steps; repeat step 3 per
    engine release), **In-proc** (the migrator creates and migrates; the host runs with
    `ApplyMigrations` off), **The migrator's entry**; an **Example** with the operator's `dotnet
    ef database update` command; **Compatibility** now only the developer's database: refused,
    dropped, re-created on the next start. (D-2, D-3, D-4, D-6a, D-6b, D-6c)
13. **Part A — Testing, fixture sentence.** "fixtures create their databases with spec 0011 §6.7's
    SQL Server statement" -> "fixtures let EF create their databases through the engine's model,
    as spec 0011 §6.7 allows for test databases." (D-6b)
14. **Part A — Testing, unit.** "**The creation statement (§A.11)** on SQL Server and on Azure SQL
    Database." -> "**The store's migrations (§A.11):** spec 0011 §10's collation pin over
    `Tellma.Identity.Migrations`." (D-6b)
15. **Part A — Testing, integration.** "**Database collation (§A.11).**" with three items ->
    "**The store (§A.11).**" with four: creation (kept); another collation fails the store check
    with `ApplyMigrations` on and off; with it off, missing / empty / behind stores fail, create
    nothing and name `Seed.MigrateCommand`; `TellmaIdentityStore.MigrateAsync` brings an empty
    platform-collation database current and the check passes. The in-proc "fails seeding and
    creates nothing" item is folded into the third. (D-3, D-4, D-6b, D-6c)
16. **Part A — DoD, Projects.** -> `Tellma.Identity` grown and `Tellma.Identity.Migrations`
    regenerated, each README updated; `DatabaseCollations` added to
    `src/core/Tellma.Core.Abstractions`; no new project. (D-6b)
17. **Part A — DoD, Docs.** "…the account entry URL and the identity database's collation." ->
    "…the account entry URL, and the identity database's collation and who creates and migrates
    it." (D-3, D-6c)
18. **Part A — DoD, Not in scope.** Adds "creating and migrating the shared identity database, an
    operator's step (§A.11)". (D-3)
19. **Part A — Decision 12** "Whoever creates the identity database sets its collation …; the
    engine creates its own database rather than letting EF do it" -> "The engine's model carries
    the platform collation, and its migrations are re-baselined so that none alters a database".
    (D-4, D-6b)
20. **Part A — Decision 13 (new).** The engine checks its store at every start and migrates only
    in development and tests; the migrator or an operator migrates every other store; refusal
    outright since no deployed database needs a fallback (D-4's one clause). (D-4, D-6c)
21. **Part A — Review flag 9** (refuse versus warn) deleted. It was the last flag; nothing
    renumbered; no new Part A flag. (D-4)

### Part G (design §2.2), appended after Part F's Review flags

22. **New `## Part G — Table types (spec 0001)`**: intro, Placement, `### §G.1 Column collations
    on class-derived standalone types` (Target, Requested by, Why, sketch of
    `TableTypeBuilder.UseCollation(string column, string collation)`, Example, six rule bullets,
    Compatibility, Tests), `### Part G — Testing`, `### Part G — Definition of done`, `### Part G
    — Decisions record` (4), `### Part G — Review flags` (3). Text as the design gives it, except
    the deviations below. (D-5)

### Checked, no edit

- Non-goals "Editing specs 0001–0009"; How to read's other lines; Vocabulary.
- Part F — Testing, the Queryex fixture "created as spec 0011 §6.7 states": no EF model, so
  §6.7's statement still applies.
- §F.15's platform-collation citation.
- Part A Placement `Tellma.Identity` and `Tellma.Identity.Web` rows; Part A DoD Behaviour.

## Deviations

1. **§G.1 example names the type.** The design's call
   `HasTableType<TenantMembershipRecord>(schema: "catalog", buildAction: …)` would register
   `[catalog].[TenantMembershipRecord]`: the library takes the name from the argument, else its
   own `[TableType].Name`, else the class name
   (`src/core/Tellma.Core.EntityFrameworkCore/TableTypes/TableTypesModelBuilderExtensions.cs:89-95`),
   and `TenantMembershipRecord` carries spec 0012's `[TableType]`, which the library does not read
   (facts-tabletypes §5.3). The example passes `name: "TenantMembershipList"`, says why in one
   sentence, and is wrapped one argument per line.
2. **The in-proc host's reference moved out of Compatibility.** The design's Compatibility ended
   "An in-proc web host now references `Tellma.Identity.Migrations` too, so the store check can
   read the engine's migrations." D-4 makes the Compatibility note cover only a developer's
   database, and "now" narrates history. The fact moved into **The store check** bullet: "The
   check reads the engine's migrations, so every host of the engine references
   `Tellma.Identity.Migrations`, in-proc ones included."
3. **In-proc bullet scoped to the distribution's host.** The design wrote "The host runs the
   engine with `Seed.ApplyMigrations` off, so the engine never creates or migrates its store in
   this mode." The engine's own in-proc test hosts run with it on
   (`test/apps/Tellma.Identity.IntegrationTests/Infrastructure/DatabaseBackedFactory.cs:54`,
   facts-identity §1.4), as §A.11's Development and tests bullet allows. Written: "The
   distribution's host runs the engine with `Seed.ApplyMigrations` off, so the engine never
   creates or migrates the catalog."
4. **"EF's `MigrateAsync`".** The store check's step 2 and the Development bullet say "EF's
   `MigrateAsync`" so it is not read as the new `TellmaIdentityStore.MigrateAsync` in the sketch
   just above.
5. **Part G placement names the migrations host.** The Tests row adds
   `test/core/assets/Tellma.Core.EntityFrameworkCore.MigrationsHost`, whose migrations Part G —
   Testing re-scaffolds (facts-tabletypes §1.4 and "Implications for Part G").

## Open questions

None for spec 0010. (The design's §9 questions stand; none changes this file's text unless Ahmad
picks the idempotent-script alternative of question 1, which would change §A.11's in-proc and
migrator-entry bullets and the `TellmaIdentityStore` sketch.)

## Requests for other files

Spec 0010's text now relies on these E2 edits from the design; please confirm they land with the
exact anchors:

- **spec 0011 §6.7** — the `DatabaseCollations` sketch (Part A Placement cites "as spec 0011 §6.7
  declares it"); the **Development and tests** bullet allowing EF to create a test database whose
  model sets the collation (Part A — Testing cites "as spec 0011 §6.7 allows for test
  databases"); the **An existing database** read and the **A fresh database** RCSI statement
  (§A.11's store check step 1 and operator step 2 cite them).
- **spec 0011 §5.7** — `Seed.ApplyMigrations = false` and `Seed.MigrateCommand` in `AddTellma`'s
  options (§A.11's In-proc bullet cites §5.7).
- **spec 0011 §6.1** — `migrateIdentityStore` on `TellmaMigrator.RunAsync` (§A.11's sketch comment
  and In-proc bullet cite §6.1).
- **spec 0011 §10** — the paragraph "**The collation pin.**", naming spec 0010 Part A — Testing
  for the identity migrations (Part A's unit item cites "spec 0011 §10's collation pin").
- **spec 0012 §1.3, §2.2, §4.4** — `CollationConvention` and `[Collation]` mapping onto
  `TableTypeBuilder.UseCollation` (Part G's intro, Requested by and Not in scope cite them). The
  convention must also pass the type name from spec 0012's `[TableType]`, since the library reads
  only its own attribute (Deviation 1); spec 0012's `TableTypeConvention` presumably already does.
- **ARCHITECTURE.md, UDTT generation** — the class-derived column collation sentence (Part G DoD
  Docs).

## Fix pass

Inputs: `verify-0010.md` (A-01…A-11) and the 0010 findings of `verify-scenarios.md` (S-09…S-12).
File grew 3957 -> 3967 lines; LF, no BOM, no `.tmp` citation; specs 0001–0009 untouched.

- A-01 -> applied (§G.1 "Character columns only": Msg 447 for other types, `json` silently replaced).
- A-02 -> applied (§G.1 Compatibility; worded "which today builds with the wire form's collation").
- A-03 -> applied (Part A — Testing, the store's third item: "an empty one created in the platform
  collation").
- A-04 -> applied (§A.11 Development and tests: the Azure-options reason replaced by Decision 13).
- A-05 -> applied (§A.11 Why).
- A-06 -> applied (Goals, Part A: "once deployed").
- A-07 -> applied (Part A — DoD, Projects: the Abstractions README lists `DatabaseCollations`).
- A-08 -> applied (Part G — DoD, Docs: no ARCHITECTURE.md section named).
- A-09 -> applied, reworded: "except where Part G — Testing adds the collated type: the snapshot
  round-trip model, the migrations host's migrations and the integration suite's type counts"
  (the finding's text made "type counts" gain a type, and omitted the re-scaffolded migrations).
- A-10 -> applied (§G.1 Example lead-in names spec 0012's conventions and its `[TableType]`).
- A-11 -> applied (Context, first "Other specs" entry: §1.2 and §10 added, with what they carry).
- S-09 -> applied, shortened: "Every later migration follows the N−1 rule of spec 0012 §4.3: it
  lands before the engine deploys, so the previous engine release runs on it during a rollout."
- S-10 -> applied (Part G — Testing, integration row). It pairs with S-08 in spec 0011 §6.7; Note 5
  lets Ahmad drop both, so if the 0011 pass rejects S-08, drop this cell's last clause too.
- S-11 -> applied (§A.11 Standalone, deployed: step 4, the engine principal's DML-only user). It
  implements Decision 13's "no DDL rights"; the in-proc counterpart is S-01 (spec 0011).
- S-12 -> applied (Placement `Tellma.Identity.Web` row cites §A.11; §A.11 says the host sets
  `Seed.MigrateCommand` to step 3's command).

**For Ahmad** (notes, not findings; nothing applied):

- verify-0010 Note 1: an in-proc host with an unreachable catalog. §A.11's "a failed check stops
  the host" against spec 0011 §3.4's not-ready start.
- verify-0010 Note 2: the migrator's reference to `Tellma.Identity.Migrations` (spec 0011 flag
  23) versus an idempotent script.
- verify-0010 Note 3: whether "Standalone, deployed" covers every isolated standalone database,
  not only the shared one.
- verify-scenarios Note 7: whether LocalDB `TellmaIdentityDesign` exists on Ahmad's machine;
  §A.11's Compatibility names one developer database.

**Requests for other files (fix pass)**

- **ARCHITECTURE.md, the N−1 rule** — S-09 binds the identity engine's later migrations to spec
  0012 §4.3's rule; the architecture text names only the platform's and a distribution's tables.

## Readability pass

File 3967 -> 3979 lines (bullets only, no new facts); LF, no BOM, no `.tmp` citation. Meaning,
names, numbers, section numbers and cross-references kept.

- Context, parts list (Part G bullet) and "Specs 0007 and 0009": no change.
- Context, Other specs, spec 0011 entry: the seven-item, six-semicolon line split into one
  sub-bullet per section (§1.2 … §10), each paired with its item, like the spec 0023 entry.
- Context, Other specs, Part G entry: no change.
- Goals (Part A, Part G), Non-goals (Part A), How to read: no change.
- Part A order list, item 8: no change.
- Terms, Store check: "it checks the store's collation, then its migrations".
- Placement, `Tellma.Identity.Migrations` row: led with "§A.11:" like Part G's row; the
  semicolon chain split into two sentences. Other rows: no change.
- §A.11 Why and opener: "so the engine checks a store that the migrator or an operator migrates"
  moved from Why into the opener, which now also says what the section decides about migrations.
- §A.11 The migrations: four facts split into a lead sentence and three sub-bullets.
- §A.11 Store check step 1: "Any other value" -> "A value other than the platform collation".
- §A.11 Development and tests: who turns `ApplyMigrations` on comes first, then what EF does.
- §A.11 Standalone, deployed: steps made imperative ("Run", "Apply", "Create"); lead "sets up …
  once for the whole fleet"; the closing semicolon sentence split.
- §A.11 In-proc, The migrator's entry, Example: no change.
- §A.11 Compatibility: "history predates" -> "migration history does not match"; "the server
  default" -> "the server's default collation".
- Part A — Testing: fixture sentence no change; unit item uses the suite's "**Lead.** text" form;
  integration first item gains "on"; third item's list moved after the colon.
- Part A — DoD, Projects: the two-semicolon sentence split into four. Docs, Not in scope: no
  change.
- Part A — Decision 12: the "while" clause rewritten as one cause; "the chain" -> "the migration
  chain". Decision 13: the three-clause why split into two sentences.
- Part G intro, Placement, Example: no change.
- §G.1 Why: "Today only …" -> "Spec 0001 sets one only through …"; "cannot be added again" ->
  "`Column(…)` cannot add a column the class already has".
- §G.1 rules: When it applies gains a subject; Character columns only names the two calls for
  "either route"; Names are not validated drops "as EF validates none" (kept in Decision 3).
- §G.1 Compatibility: last sentence split in two. Tests: the unit list became sub-bullets, the
  "class route" / "ad-hoc route" jargon spelled out.
- Part G — Testing: no change. DoD, Compatibility: semicolon split. Decisions 1 and 4: plainer
  why ("through the fluent API"; "the error comes at build time and names the column"). Review
  flags: no change.
