# Follow-up 3: verification of specs 0011–0035 and ARCHITECTURE.md

Scope: `git diff HEAD` of specs 0011, 0012, 0018 and `ARCHITECTURE.md` (the only files E2 changed),
each edited section read in full, plus greps of specs 0011–0035 and `ARCHITECTURE.md`.

Checked and right (no finding): D-2, D-3, D-5 sketches and citations, D-6a (no
`CreateDatabaseTemplate` or `"Provisioning"` left; §8 JSON and key table), D-6b (`UseCollation` in
the catalog model §7, the tenant model 0012 §1.3, the fixture 0012 §13.2; no migration alters a
collation; the collation pin; fixtures), D-6c (`migrate` owns `idsvr`; `Seed.ApplyMigrations =
false`; missing or behind `idsvr` fails startup naming the command), every inventory item, every
cited section (spec 0010 §A.11, §G.1, Part G, Part A — Testing, spec 0003 §10.4, spec 0001 §2),
the design's shared names, LF and UTF-8 without BOM, no `.tmp` citation, no narration. The stale-term
greps find nothing stale: `Bicep` remains only in `ARCHITECTURE.md` lines that name no database,
and `UseCollation`, `idsvr`, `web process`, `Parts A`, `Part F` hits are all current text. The
collation pin's `IDesignTimeModel` reading checks out against EF (`GetCollation` throws on a
`RuntimeModel`).

## docs/specs/0011-distribution-host-and-multitenancy.md

- [B-01] (should) 0011 §5.7 - "The engine's store check then runs (spec 0010 §A.11): a missing or
  behind `idsvr` fails startup with an error naming that command." -> In in-proc mode an
  unreachable catalog makes the store check throw from a hosted service, so the host stops. §3.4
  and Decision 4 say such a host starts not-ready. Until Ahmad decides (Notes 1), add after Review
  flag 23: "24. **An in-proc host with an unreachable catalog does not start** (§3.4, §5.7). The
  engine's store check and seeding run in a hosted service and throw, so the host stops instead of
  starting not-ready. Alternative: the store check waits until the catalog is reachable. Flips when
  on-premises in-proc delivery is designed." In §10's `Tellma.Distro.Acme.IntegrationTests` row,
  change "readiness before and after catalog availability" to "readiness before and after catalog
  availability (standalone identity mode)". Why: §3.4 **Startup**; Decision 4; facts-identity §1.5,
  §6.2; design §9 Q2.
- [B-02] (should) 0011 §6.1, paragraph after the command table - "The migrator is the single
  DDL-privileged executable; `MigrationsAssembly` is the distribution's Migrator project." ->
  Replace with "The migrator is the single DDL-privileged executable; the tenant model's
  `MigrationsAssembly` is the distribution's Migrator project." Then rewrap the paragraph. Its line
  "The design-time factory `AcmeDesignTimeFactory :" is 48 columns long. Why: §5.7 now sets the
  identity store's `MigrationsAssembly = Tellma.Identity.Migrations`, so the bare name is ambiguous;
  the line-wrap rule.
- [B-03] (should) 0011 §6.1 / §10 - "in in-proc mode (`Tellma:Identity:Mode = InProc`) `migrate`
  without it exits 1, naming `Tellma.Identity.Migrations`." -> No suite pins this. In §10's
  `test/core/Tellma.Core.Tests` row, after "the collation pin over the catalog migrations;" insert
  "`migrate` under `Tellma:Identity:Mode = InProc` without `migrateIdentityStore` exits 1 naming
  `Tellma.Identity.Migrations`;". Why: §6.1; D-6c (tests); style guide, Testing.
- [B-04] (should) 0011 §1.2 **Migrator project references** - "because Acme runs the identity
  engine in-proc, `Tellma.Identity.Migrations`, whose migrations `migrate` applies" -> Replace
  "because Acme runs the identity engine in-proc" with "because Acme can select in-proc identity
  mode (§5.7)". Why: §8's example sets `"Mode": "Standalone"`. §11 **Docs** says Acme "selects
  in-proc by configuration", and `ARCHITECTURE.md` says it "runs in-proc in Development".
- [B-05] (should) 0011 §7.3 - "registered on `CatalogDbContext`, whose model maps its
  `[TableType]` and `[Collation]` with spec 0012's conventions (spec 0012 §1.3;" -> Replace "whose
  model maps its `[TableType]` and `[Collation]` with spec 0012's conventions" with "whose model
  installs spec 0012's `TableTypeConvention` and `CollationConvention` for it". Why: the §7 intro
  says "the catalog keeps its own conventions … tenant tables follow spec 0012's". The reader
  cannot tell which conventions the catalog model installs. Spec 0010 §G.1's example registers this
  same type fluently.
- [B-06] (should) 0011 §1.3, paragraph after the illustrations - "first on-premises distribution.
  The web host and the migrator build" -> Rewrap the last two lines. They are 67 and 70 columns
  long after the edit shortened the paragraph. Why: the line-wrap rule.
- [B-07] (should) 0011 Decisions record, item 32 - "A model collation reaches EF's own creation
  and `dotnet ef database update`, and a first migration carries none." -> Keep the bold lead.
  Replace the text after it with: "— the server default `SQL_Latin1_General_CP1_CI_AS` defines no
  order on Amharic text, and one collation makes every database and every test compare text alike.
  Azure SQL Database cannot change a collation, and EF's own creation writes neither Azure's options
  nor `CATALOG_COLLATION`, so the migrator keeps its own statement (§6.7)." Why: docs/AGENTS.md
  (each fact once; a proportionate why). §6.7's **Every model carries it** and **No migration
  alters it** already state the rest. At ten lines, item 32 is the longest in the record (style
  guide, Decisions record).

## docs/specs/0012-data-access-layer.md

- [B-08] (should) 0012 §1.3, conventions table, `CollationConvention` row - "`TableTypeBuilder.UseCollation`
  on a standalone class (spec 0010 §G.1); inherited by a leaf" -> Replace with "on a standalone
  class, a `TableTypeBuilder.UseCollation(column, name)` call in the build action that
  `TableTypeConvention` passes to `HasTableType` (spec 0010 §G.1); inherited by a leaf". In the
  `TableTypeConvention` row, after "`[TableType]` on a type → `HasTableType(name?, schema?)`", add
  "with a build action carrying the class's `[Collation]` columns". Why: the table lists
  conventions "in order", and this row runs before `TableTypeConvention`. A `TableTypeBuilder`
  exists only while `HasTableType` runs its build action (facts-tabletypes §2.1), and spec 0010
  §G.1 applies the call "when the builder builds".
- [B-09] (should) 0012 §13.1, Unit row - "metadata construction and every §3.2 rule as a failing
  model;" -> After it, insert "`CollationConvention`: `[Collation]` on an entity property sets the
  column's collation and its row image's, and on a standalone class property the UDTT column's;".
  Why: the convention is new in §1.3, and §14 **Behavior** ("the entity contract and conventions of
  §2 and §1.3") is pinned by no named test for it.

## docs/specs/0024-client-app-foundation.md

- [B-10] (should) 0024 Decisions record, item 2 - "the Migrator composes without ASP.NET, so the
  esproj references it without a cycle" -> Replace with "the Migrator never references the Web
  project, so the esproj references it without a cycle". Why: an in-proc distribution's Migrator
  now carries ASP.NET Core's shared framework through `Tellma.Identity.Migrations` (spec 0011 §1.2,
  Review flag 23; D-6c). The old reason no longer holds, but the cycle argument does.

## ARCHITECTURE.md

- [B-11] (should) Data Layer › Migrations & seeding, first paragraph - "the web publish needs
  neither the Design packages nor the migration-apply path at all." -> Replace with "the web
  publish needs no Design packages and never applies a migration; an in-proc web host carries the
  identity engine's migrations only so its startup check can count pending ones." Why: spec 0011
  §1.2 (the Web project references `Tellma.Identity.Migrations` in in-proc mode) and §5.7. Spec
  0010 §A.11 defines the store check, which reads the migrations. E2 raised this as an open
  question (applied-rest, Open questions 1).

## Notes for Ahmad

1. **An in-proc host with an unreachable catalog does not start** (B-01; design §9 Q2). Spec 0011
   §3.4 and Decision 4 promise a not-ready start. The store check, like seeding before it, stops
   the host. Choose between recording the gap as a review flag (B-01) and designing a wait or skip
   now.
2. **Registering standalone classes** (design §9 Q3, unchanged). Spec 0012 §4.4 registers them
   "through spec 0011's `Model<T>()`", whose `T` must implement `IEntityTypeConfiguration<>` (spec
   0011 §2.1 table). So the call site that applies `TableTypeConvention` and `CollationConvention`
   to `UserInvitationOutcome` is unstated. For `TenantMembershipRecord` on `CatalogDbContext`, see
   B-05.
3. **`varchar(255)` on the two `Subject` sketches** (pre-existing). `[Collation]` now sits on both
   classes, but their store type is only a comment. On the class route a `string` maps to
   `nvarchar(max)` unless the class says otherwise. EF's `[Unicode(false)]` cannot be used in EF-free
   Abstractions, so the class would need `[Column(TypeName = "varchar(255)")]` (facts-tabletypes
   §5.3). Decide whether the sketches show it.
4. **Review flag 23** (the migrator references `Tellma.Identity.Migrations`) is still open. B-10's
   wording holds under either choice.
