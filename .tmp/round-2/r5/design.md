# Follow-up 3: binding design for the two editors

This design applies `decisions.md` D-2 to D-6. It is binding for both editors:

- **E1** owns `docs/specs/0010-amendments-to-implemented-specs.md`.
- **E2** owns `docs/specs/0011`–`0035` and `ARCHITECTURE.md`.

Neither editor touches the other's files. Quoted spec text below is the text to write; polish
wording and wrapping freely, but keep every name, owner, section number and cross-reference
exactly as given here. Rules for both: replace, never append; no history narration; each fact in
the section that owns it, with pointers elsewhere; lines wrapped near 100 columns; LF; UTF-8
without BOM; no `.tmp` citation anywhere; Edit tool only.

Facts relied on are the Verified ones in `r4/facts-collation.md`, `r5/facts-tabletypes.md` and
`r5/facts-identity.md`. Where this design goes beyond them it says so (open questions, section 9).

## 1. Shared names

| Name | Exact shape | Where it lives | Owner (states it once) | Cited by |
|---|---|---|---|---|
| Platform collation constant | `public static class DatabaseCollations { public const string Platform = "Latin1_General_100_CI_AS"; }` | namespace `Tellma.Core.Abstractions.Hosting`, project `src/core/Tellma.Core.Abstractions` (beside `DeploymentIdentity`) | spec 0011 §6.7 (sketch) | 0011 §1.1, §7; 0012 §1.3, §13.2; 0010 §A.11 and Part A placement |
| Column-collation builder call | `public virtual TableTypeBuilder UseCollation(string column, string collation);` on `Tellma.Core.EntityFrameworkCore.TableTypes.TableTypeBuilder`, package `Tellma.Core.EntityFrameworkCore` | `src/core/Tellma.Core.EntityFrameworkCore/` | spec 0010 §G.1 | 0012 §1.3, §2.2, §4.4; 0011 Context list; ARCH UDTT section |
| Platform annotation for it | `public sealed class CollationAttribute(string name) : Attribute;`, written `[Collation("Latin1_General_100_BIN2")]` | namespace `Tellma.Core.Abstractions.Entities` | spec 0012 §2.2 (sketch), mapped by the new `CollationConvention` row of spec 0012 §1.3 | 0011 §3.8, §7.3; 0018 §3.1; 0010 Context list, §G.1 "Requested by" |
| Azure creation options key | `Tellma:Sql:CreateDatabaseOptions` (string, null on-prem) | the `Tellma:Sql` section | spec 0011 §8 key table (meaning); §6.7 (use) | ARCH (no edit needed: it lists `Tellma:Sql`) |
| Migrator entry of the identity engine | `public static class TellmaIdentityStore { public static Task MigrateAsync(string connectionString); }` | namespace and assembly `Tellma.Identity.Migrations` | spec 0010 §A.11 | 0011 §1.3, §5.7, §6.1 |
| Seed option naming the remedy | `public string? MigrateCommand { get; set; }` on `TellmaIdentitySeedOptions` (written `Seed.MigrateCommand`) | `Tellma.Identity` | spec 0010 §A.11 | 0011 §5.7 |
| Migrator hook | `TellmaMigrator.RunAsync(IReadOnlyList<string> args, string slug, Action<TellmaBuilder> compose, Func<string, Task>? migrateIdentityStore = null)` | `Tellma.Core.Migrator` | spec 0011 §6.1 | 0011 §1.3, §5.7 |
| The store check | the identity seeder's first step at every start: collation, then migrations | `Tellma.Identity` | spec 0010 §A.11 (defined in Part A's Terms) | 0011 §5.7, §6.6 |
| The collation pin | the no-database test over a migrations assembly | test suites | spec 0011 §10 (paragraph "The collation pin") | 0010 Part A — Testing |
| Section titles | spec 0010 §A.11 "The identity database's collation and migrations"; `## Part G — Table types (spec 0001)`; `### §G.1 Column collations on class-derived standalone types` | spec 0010 | E1 | E2 cites "spec 0010 §A.11", "spec 0010 §G.1", "spec 0010 Part G", "spec 0010 Part A — Testing" |

**Why these owners and homes (verified):**

- `Tellma.Core.Abstractions` is BCL-only and needs no new edge for a `const` (its README forbids
  package references, not constants). `Tellma.Identity` references it (`Tellma.Identity.csproj:34`);
  `CatalogDbContext` and `TellmaDbContext` live in `Tellma.Core`, which references it; the migrator
  reaches it through `Tellma.Core`. The `.Hosting` namespace already holds the one cross-cutting
  hosting value, `DeploymentIdentity`. Part A ships first, so Part A's placement adds the class;
  spec 0011 §6.7 keeps the sketch because it owns the rule.
- The builder call is the least library surface that reaches a class-derived column. A library
  attribute cannot sit on the two target classes, which live in EF-free
  `Tellma.Core.Abstractions` (facts-tabletypes §5.3). Spec 0012 already maps EF-free annotations
  onto spec 0001's fluent configuration, so `[Collation]` follows that pattern.
- Entity-derived table types need no amendment: spec 0001 §2 and the code already copy a column's
  collation (facts-tabletypes §2.4). `CollationConvention` therefore also maps `[Collation]` on an
  entity property onto EF's `PropertyBuilder.UseCollation`, so the annotation never silently does
  nothing.
- The model-level `UseCollation` never reaches a table-type column (facts-tabletypes §2.4), so
  D-6b changes no type definition or hash.

## 2. Spec 0010: front matter and Part G (E1)

### 2.1 Front matter

- **Context, parts list.** After the Part F bullet add:
  `- **Part G — table types (spec 0001).** It is needed by the catalog's membership directory (spec
  0011 §3.8) and the users stack's invite write-back (spec 0018 §3.1), through spec 0012's
  `[Collation]` annotation.`
- **Context, "Specs 0001, 0007 and 0009 need no amendment."** becomes "Specs 0007 and 0009 need
  no amendment."
- **Context, "Other specs that carry parts of this design".**
  - First entry becomes: "spec 0011 §5.5, §5.7, §5.9, §6.1, §6.7 — the distribution side of
    Part A: `/bff/login` forwards `ui_locales`; the in-proc seed entry and store options;
    `IdentityInfo.AccountUrl`; `migrate` applying the in-proc store's migrations; the database
    collation."
  - New last entry: "spec 0012 §1.3, §2.2, §4.4, spec 0011 §3.8, spec 0018 §3.1 — the users of
    Part G: the `[Collation]` annotation and its convention, and the two `Subject` columns that
    declare it."
- **Goals.** Part A's bullet ends "…the sign-in language from the authorization request, and the
  identity database on the platform collation, migrated outside the web process." Add after
  Part F: "- **Part G.** A collation on a string column of a class-derived standalone table type,
  so a table-valued parameter can meet a binary-collated column (§G.1)."
- **Non-goals, Part A list.** Add one bullet (D-3's single "later" line): "tools that create and
  migrate the shared identity database, and on-prem deployment tooling: a later release, reusing
  the migrator's approach (§A.11)." No Part G non-goals list.
- **How to read an amendment.** One edit: "**Closing labels** — **Compatibility**, **Tests** and,
  in the library parts, **Budget**." becomes "…and, in Parts B–E, **Budget**." (Part G is a
  library part without a budget.) Nothing else there lists parts. Vocabulary: no edit.

### 2.2 Part G (new, appended after Part F's Review flags, at the end of the file)

Small: about 100 lines. No terms list (spec 0001's vocabulary applies). Text to write:

````markdown
## Part G — Table types (spec 0001)

Part G amends spec 0001, the table-types library in `src/core/Tellma.Core.EntityFrameworkCore`.
It lets a class-derived standalone table type give a string column a collation (§G.1). Every rule
of spec 0001 that Part G does not name stands. Table-derived types need no change: they already
take each column's collation from the table (0001 §2).

**Placement**

| Project | Location | Change |
|---|---|---|
| `Tellma.Core.EntityFrameworkCore` | `src/core/Tellma.Core.EntityFrameworkCore/` | §G.1: `TableTypeBuilder.UseCollation` and the character-column check |
| Tests | `test/core/Tellma.Core.EntityFrameworkCore.Tests`, `.Design.Tests`, `.IntegrationTests` | Part G — Testing |

No new project or package reference. Part G waits on no other part. It ships before spec 0012,
whose `[Collation]` annotation maps onto it (spec 0012 §1.3).

### §G.1 Column collations on class-derived standalone types

**Target:** 0001 §5 (standalone table types) and its Testing plan. **Requested by:** spec 0012
§1.3, §2.2 and §4.4; spec 0011 §3.8; spec 0018 §3.1. **Why:** a type's string column compared
with an explicitly collated column must carry that collation, or the statement fails to compile
with Msg 468. Today only an ad-hoc `Column(…, collation)` sets one, and a class-derived column
cannot be added again.

```csharp
// Tellma.Core.EntityFrameworkCore.TableTypes.TableTypeBuilder — new member
public virtual TableTypeBuilder UseCollation(string column, string collation);
```

**Example —** the catalog's membership type, whose `Subject` meets a `Latin1_General_100_BIN2`
column:

```csharp
modelBuilder.HasTableType<TenantMembershipRecord>(schema: "catalog",
    buildAction: type => type.UseCollation("Subject", "Latin1_General_100_BIN2"));
```

- **What it sets.** The named column's collation, whether the class or `Column(…)` added the
  column. The last call wins, over an earlier call and over `Column(…, collation)`.
- **When it applies.** When the builder builds, so its order against `Column(…)` does not matter.
- **An unknown column.** A name no column has fails registration with an
  `InvalidOperationException` naming the type and the column. Names match case-insensitively, as
  the duplicate-column and key checks do.
- **Character columns only.** A standalone column with a collation, on either route, must resolve
  to `char`, `varchar`, `nchar` or `nvarchar`. Any other store type, `json` included, fails model
  finalization, naming the type, the column and the store type. SQL Server would otherwise fail
  the migration with Msg 447.
- **Names are not validated,** as EF validates none. SQL Server refuses an unknown name at
  `CREATE TYPE` with Msg 448. Write the canonical spelling: the name is part of the canonical
  JSON, so a change of case creates a new version.
- **Versioning.** Adding, changing or removing a column's collation changes the definition. The
  differ creates a new physical version beside the old one, and the sweep retires the old (0001
  §3).

**Compatibility:** additive. A column without a collation serializes as before, so no existing
definition changes its hash. The snapshot and operation generators already write a column's
collation. The character-column check moves a failure from `CREATE TYPE` to model finalization.

**Tests:**

- unit — the class route with `UseCollation` sets the definition's collation and leaves its store
  type alone; the ad-hoc route in either call order; the last call wins; an unknown column
  throws; `int`, `datetimeoffset` and `json` columns with a collation fail finalization; adding a
  collation emits a create and the cleanup, and no drop; a type without one keeps its canonical
  JSON;
- design — the snapshot round-trip model gains a collated class-derived column;
- integration — a class-derived type with a `Latin1_General_100_BIN2` column is created,
  `sys.columns.collation_name` reads that collation, and a variable of the type joins a `BIN2`
  table column without Msg 468.

### Part G — Testing

| Suite | Tier | Pins |
|---|---|---|
| `Tellma.Core.EntityFrameworkCore.Tests` | unit | §G.1's builder, finalization and differ cases |
| `Tellma.Core.EntityFrameworkCore.Design.Tests` | unit | the snapshot round trip with a collated column |
| `Tellma.Core.EntityFrameworkCore.IntegrationTests` | `Category=Integration` | the collated type on SQL Server; the migrations host gains the type, so its migrations are re-scaffolded and its type counts updated |

All three run on every pull request on Windows and Linux; none is nightly.

### Part G — Definition of done

- **Library:** §G.1 in `src/core/Tellma.Core.EntityFrameworkCore`, with XML docs on the new
  member and the README's standalone paragraph updated. Warnings-as-errors green on Windows and
  Linux; no new project or package reference.
- **Behaviour:** pinned by Part G's suites.
- **Compatibility:** no existing definition's hash changes; every existing test passes unchanged
  except the integration suite's type counts.
- **Docs:** the architecture document's UDTT section names the column collation of a
  class-derived type. Public XML docs and error messages reference no `docs/` paths.
- **Not in scope of done:** spec 0012's `[Collation]` annotation and its convention; the two
  `Subject` columns (specs 0011 and 0018).

### Part G — Decisions record

1. **A builder call, not a library attribute** — the platform's standalone classes live in EF-free
   `Tellma.Core.Abstractions`, where a library attribute cannot go. Spec 0012 maps its own
   annotation onto this call, as it maps `[TableType]`, and EF itself sets collations only
   fluently (§G.1).
2. **Table-derived types are unchanged** — they already copy each column's collation (0001 §2).
3. **Collation names are not validated** — EF validates none, and SQL Server refuses an unknown
   name at `CREATE TYPE` (§G.1).
4. **A collation on a non-character column fails model finalization** — the error names the
   column at build time instead of at `CREATE TYPE` (§G.1).

### Part G — Review flags

1. **No library attribute** (§G.1). Alternative: a collation attribute in the library beside the
   builder call. Flips if a class outside `Tellma.Core.Abstractions` needs a collation.
2. **Refusing `json` with a collation** (§G.1). Alternative: keep replacing it silently with the
   wire form's. Flips if a model must state the wire collation.
3. **Case-sensitive names in the hash** (§G.1). Alternative: normalise the spelling before
   hashing. Flips if a spelling change ever churns a version.
````

Note the example uses `buildAction:` by name: `HasTableType<T>(string? name, string? schema,
Action<TableTypeBuilder>? buildAction)` (verified in `TableTypesModelBuilderExtensions.cs`), and a
positional lambda after `schema:` would not compile.

## 3. Spec 0010 Part A (E1)

### 3.1 §A.11, replacing the whole section

The identity migrations are **re-baselined** (binding D-6b: Ahmad's addition overrides the
facts' emptied-migration recommendation). No identity database is deployed (D-4), so restarting
the chain costs only a developer's local database. An initial migration carries no collation
operation (r4 §1.2), so `ALTER DATABASE … COLLATE` reaches no database. The collation pin (spec
0011 §10) keeps it that way.

Text to write:

````markdown
### §A.11 The identity database's collation and migrations

**Target:** 0003 §2.1 (the `Tellma.Identity.Migrations` row), §2.2 (the Store row), §12, §16.
**Requested by:** spec 0011 §5.7, §6.1, §6.7. **Why:** every platform database shares one
collation, and the server default defines no order on Amharic text, such as users' names in the
shared directory. A distribution's web process never migrates, so the engine checks a store that
something else migrates.

The identity database uses the platform collation of spec 0011 §6.7. The engine's model carries
it, and no migration ever changes it.

```csharp
// Tellma.Identity.Migrations — new
public static class TellmaIdentityStore
{
    public static Task MigrateAsync(string connectionString);   // applies the engine's migrations (spec 0011 §6.1)
}

// Tellma.Identity.Options.TellmaIdentitySeedOptions — new member
public string? MigrateCommand { get; set; }                    // the remedy a failed store check names
```

- **The model.** `TellmaIdentityDbContext` calls `UseCollation(DatabaseCollations.Platform)`
  (spec 0011 §6.7).
- **The migrations.** `Tellma.Identity.Migrations` is re-baselined: its migrations and snapshot
  are deleted, and one `Initial` is scaffolded from the model with the collation. An initial
  migration carries no collation operation, so no identity migration alters a database. `Initial`
  keeps the hand-written `IX_OpenIddictTokens_CreationDate` index.
- **The store check.** At every start, before it seeds, the seeder checks its store:
  1. It reads an existing database's collations as spec 0011 §6.7 states. Any other value fails
     seeding, naming the database, the value expected and the value found.
  2. With `Seed.ApplyMigrations` on, it runs `MigrateAsync`.
  3. With it off, any pending migration fails seeding, naming the schema, the number pending and
     `Seed.MigrateCommand` when set. A missing database or schema has every migration pending.

  A failed check stops the host, as any failed seeding does.
- **Development and tests.** With `Seed.ApplyMigrations` on, `MigrateAsync` creates a missing
  database. The model's collation makes EF write `COLLATE Latin1_General_100_CI_AS` and turn
  read-committed snapshot on. EF writes no Azure options, so only development and test hosts turn
  `Seed.ApplyMigrations` on.
- **Standalone, deployed.** An operator creates the shared identity database once for the fleet,
  by hand:
  1. In `master`, spec 0011 §6.7's statement; on Azure SQL Database its Azure form, with the
     service tier or pool options the database needs.
  2. On SQL Server, `ALTER DATABASE … SET READ_COMMITTED_SNAPSHOT ON`, as the migrator does in
     a fresh catalog (spec 0011 §6.7).
  3. The engine's migrations, through `dotnet ef database update` against
     `Tellma.Identity.Migrations`, or the script `dotnet ef migrations script --idempotent`
     writes.

  An engine release that adds a migration repeats step 3 before the engine deploys; otherwise
  the store check stops it.
- **In-proc** (`TellmaIdentityOptions.Mode = InProc`, set by spec 0011 §5.7). The store is the
  catalog database. The distribution's migrator creates it and applies the engine's migrations
  through `TellmaIdentityStore.MigrateAsync` (spec 0011 §6.1). The host runs the engine with
  `Seed.ApplyMigrations` off, so the engine never creates or migrates its store in this mode.
- **The migrator's entry.** `TellmaIdentityStore.MigrateAsync` builds the context as the
  design-time factory does: `IdentityOptions` from `TellmaIdentityModelDefaults`, and the engine's
  migrations assembly. It then applies the migrations to the database the connection string names.

**Example —** the operator's step 3, from a checkout after `dotnet tool restore`:

```
dotnet ef database update --project src/apps/Tellma.Identity.Migrations --startup-project src/apps/Tellma.Identity.Migrations --connection "<connection string>"
```

**Compatibility:** the only identity database that exists is a developer's standalone one. Its
history predates the re-baselined `Initial`, and it uses the server default, so the store check
refuses it. The developer drops it, and the next start with `Seed.ApplyMigrations` on re-creates
and re-seeds it (0003 §10.4). An in-proc web host now references `Tellma.Identity.Migrations`
too, so the store check can read the engine's migrations.
````

Verified basis: the seeder order and code default (facts-identity §1.4); `MigrateAsync` creation
with the model collation (§3.2 (a), §5); `GetPendingMigrations` on a missing database (§3.4); the
design-time factory's model (§1.3); the hand-carried index (§1.2); the operator's command (§5);
reading the collation from inside the database (§1.7).

### 3.2 Part A front, placement, terms

- **Order list, item 8** becomes: "**§A.11** — the identity database's collation and migrations
  ship before any identity database is deployed: a deployed database keeps the collation it was
  created with."
- **Terms used in Part A**, new term: "- **Store check** — the seeder's first step at every
  start: the store's collation, then its migrations (§A.11)."
- **Placement table.**
  - `Tellma.Identity` row: unchanged ("the code of §A.1–§A.3 and §A.5–§A.11").
  - `Tellma.Identity.Migrations` row: "regenerated: one `Initial` scaffolded from the model with
    the platform collation, and `TellmaIdentityStore` (§A.11); no table changes; metadata
    documents are cached in memory".
  - New row: "`Tellma.Core.Abstractions` | `src/core/Tellma.Core.Abstractions/` |
    `DatabaseCollations`, as spec 0011 §6.7 declares it (§A.11)".
  - The sentence "No new package is referenced." stands.

### 3.3 Part A — Testing

- **Fixture sentence** ("The integration and E2E fixtures create their databases with spec 0011
  §6.7's SQL Server statement.") becomes: "The integration and E2E fixtures let EF create their
  databases through the engine's model, as spec 0011 §6.7 allows for test databases."
- **Unit**, replace "**The creation statement (§A.11)** on SQL Server and on Azure SQL Database."
  with "**The store's migrations (§A.11):** spec 0011 §10's collation pin over
  `Tellma.Identity.Migrations`."
- **Integration**, the "Database collation (§A.11)" bullet becomes "**The store (§A.11).**" with:
  - A standalone engine with `Seed.ApplyMigrations` creates a missing database;
    `DATABASEPROPERTYEX(DB_NAME(), 'Collation')` reads `Latin1_General_100_CI_AS`, and
    `is_read_committed_snapshot_on` reads 1. (kept)
  - A database created in another collation fails the store check with `Seed.ApplyMigrations` on
    and off, and the error names both collations.
  - With `Seed.ApplyMigrations` off, a missing database, an empty one and a store whose last
    history row is deleted each fail the store check, create nothing, and name
    `Seed.MigrateCommand`.
  - `TellmaIdentityStore.MigrateAsync` brings an empty database in the platform collation to the
    engine's last migration, and the store check then passes.

### 3.4 Part A — Definition of done

- **Projects:** "`src/apps/Tellma.Identity` grown and `src/apps/Tellma.Identity.Migrations`
  regenerated, each README updated, XML docs on every member, building and testing on Windows and
  Linux under warnings-as-errors; `DatabaseCollations` added to `src/core/Tellma.Core.Abstractions`;
  no new project."
- **Behaviour:** unchanged.
- **Docs:** "…the account entry URL, and the identity database's collation and who creates and
  migrates it."
- **Not in scope of done:** add "creating and migrating the shared identity database, an
  operator's step (§A.11)".

### 3.5 Part A — Decisions record and Review flags

- **Decision 12** becomes:
  "12. **The engine's model carries the platform collation, and its migrations are re-baselined so
  that none alters a database** — EF's own creation then writes the collation, while Azure SQL
  Database cannot change one. No identity database is deployed, so the chain can restart (§A.11)."
- **Decision 13** (new):
  "13. **The engine checks its store at every start and migrates it only in development and
  tests; the migrator or an operator migrates every other store** — the web process holds no DDL rights, a
  missing or behind store fails with the command that fixes it, and a store in another collation
  is refused outright, since no deployed database needs a fallback (§A.11)."
- **Review flag 9** is deleted. It is the last flag, so nothing is renumbered. No new Part A flag.

## 4. Spec 0011 (E2)

### 4.1 §6.7, replacing the section's prose and bullets

````markdown
### 6.7 Database creation and collation

Every platform database uses one collation, `Latin1_General_100_CI_AS`, called the platform
collation. This covers the catalog, every tenant database, the identity database (spec 0010
§A.11), and the development and test databases that stand in for them. `CREATE DATABASE` sets
the collation, and nothing ever alters it.

```csharp
// Tellma.Core.Abstractions.Hosting
public static class DatabaseCollations
{
    public const string Platform = "Latin1_General_100_CI_AS";   // every platform model and the migrator read it
}
```

- **Who creates.** The migrator creates every distribution's catalog, SaaS and on-prem alike,
  when `migrate` finds it missing, before it takes the catalog lock. It creates every tenant
  database too (`provision`, §6.1). No infrastructure template declares a platform database, and
  the web host never creates one (§3.4).
- **Every model carries it.** The catalog model (§7), the tenant model (spec 0012 §1.3) and the
  identity engine's model (spec 0010 §A.11) call `UseCollation(DatabaseCollations.Platform)`.
  When EF creates a missing database — `Migrate()`, `EnsureCreated()` or `dotnet ef database
  update` — it writes the same `COLLATE` and turns read-committed snapshot on. EF never writes
  Azure's options or `CATALOG_COLLATION`, so the migrator runs its own statement on every engine.
- **No migration alters it.** A model's first migration carries no collation operation, and no
  model ever changes its collation. A later change would scaffold `ALTER DATABASE … COLLATE`,
  which Azure SQL Database refuses. The collation pin enforces this (§10).
- **Where it runs.** (unchanged)
- **The statement.** On SQL Server and LocalDB it is `CREATE DATABASE [<name>] COLLATE
  Latin1_General_100_CI_AS;`. On Azure SQL Database (`EngineEdition = 5`) the migrator appends
  `Tellma:Sql:CreateDatabaseOptions`, when set, and then `WITH CATALOG_COLLATION =
  DATABASE_DEFAULT`.
- **An existing database.** (unchanged)
- **A fresh database.** (unchanged)
- **Copies.** (unchanged)
- **Development and tests.** EF may create a development or test database whose model sets the
  platform collation, as every platform model and spec 0012's fixture model do. A fixture with no
  EF model runs the SQL Server statement itself. Test servers keep their own default collation:
  LocalDB cannot change it, and no suite sets `MSSQL_COLLATION` on the container. So a test
  database's collation differs from `tempdb`'s, as a production database's can.

**Example —** Acme's SaaS catalog, with `Tellma:Sql:CreateDatabaseOptions` set to
`(SERVICE_OBJECTIVE = ELASTIC_POOL(name = pool1))`:
(SQL block unchanged)
````

The **Infrastructure** bullet is deleted (D-2, D-3). "Nothing relies on EF's implicit creation"
and "no model calls `UseCollation`" are gone with the old **Who creates** bullet.

### 4.2 §5.7 In-proc identity mode (D-2, D-6a, D-6b, D-6c)

- **Options list** becomes:
  - `Mode = InProc`;
  - `PathBase = /id` and `Issuer = {PublicOrigin}/id`;
  - the identity store pointed at the catalog connection, with `MigrationsAssembly =
    Tellma.Identity.Migrations`; the engine's model keeps its tables in schema `idsvr`;
  - `Seed.ApplyMigrations = false`, and `Seed.MigrateCommand` set to the `migrate` command that
    §3.4's message names.
- **The paragraph** "The identity engine's own migrations migrate `idsvr`. It is the one schema
  the web process may migrate, … prints the `migrate` command (§6.6)." becomes:
  "The migrator's `migrate` applies the engine's migrations to `idsvr` (§6.1); the web process
  never migrates. At startup §3.4's catalog check runs first, so a missing catalog prints the
  `migrate` command (§6.6). The engine's store check then runs (spec 0010 §A.11): a missing or
  behind `idsvr` fails startup with an error naming that command. With `idsvr` current, seeding
  writes rows only: the scopes, the `Distribution` seed entry and the administrators of 0003
  §10.4."
- The secrets paragraph that follows is unchanged.

### 4.3 §6.1 Commands

- **Sketch.** `RunAsync` gains the parameter (keep the exit-code comment on that line):

  ```csharp
  public static Task<int> RunAsync(
      IReadOnlyList<string> args, string slug, Action<TellmaBuilder> compose,
      Func<string, Task>? migrateIdentityStore = null);   // exit 0 converged, 1 usage, 2 partial failure, 3 composition problems (emit-client-assets)
  ```
- **`migrate` row**, its opening becomes: "Creates a missing catalog and turns on read-committed
  snapshot in a fresh one (§6.7). Then, under `sp_getapplock('tellma:migrate:catalog')`, it
  migrates the catalog and, in in-proc mode, `idsvr` through `migrateIdentityStore` (§5.7);
  registers …" (rest of the row unchanged).
- **Paragraph after the table** ("The migrator is the single DDL-privileged executable; …"), add
  one sentence: "`migrateIdentityStore` receives the catalog connection string; in in-proc mode
  (`Tellma:Identity:Mode = InProc`) `migrate` without it exits 1, naming
  `Tellma.Identity.Migrations`."

### 4.4 Other spec 0011 sections

| Section | Change |
|---|---|
| Context, "Other specs that carry parts of this design" | The §A.11 entry becomes "spec 0010 §A.11 — the identity database's collation and migrations, and the store check an in-proc host runs (§5.7, §6.7)". New entry: "spec 0010 §G.1 — the builder call behind `TenantMembershipList.Subject`'s collation (§7.3)". |
| §1.1 table, `Tellma.Core.Abstractions` row, "Owns" | Append "; `Hosting.DatabaseCollations` (§6.7)". |
| §1.2 layout | Migrator csproj comment: `# → Tellma.Distro.Acme, Tellma.Core.Migrator, Tellma.Identity.Migrations`. |
| §1.2 **Web project references** | "`Tellma.Identity` (in-proc mode)" becomes "`Tellma.Identity` and `Tellma.Identity.Migrations` (in-proc mode; the engine's store check reads the migrations, spec 0010 §A.11)". |
| §1.2 **Migrator project references** | "`Tellma.Distro.Acme`, `Tellma.Core.Migrator` and, because Acme runs the identity engine in-proc, `Tellma.Identity.Migrations`, whose migrations `migrate` applies (§5.7). A migrator build never builds the Client esproj and needs no Node. ASP.NET Core's shared framework reaches it only through the identity engine." |
| §1.3 Migrator `Program.cs` illustration | `return await TellmaMigrator.RunAsync(args, AcmeComposition.Slug, AcmeComposition.Compose, TellmaIdentityStore.MigrateAsync);` |
| §1.3 prose | "…so neither the composition library nor the migrator carries an ASP.NET package." becomes "…so the composition library carries no ASP.NET package." |
| §3.2 **Contents** | "…in in-proc identity mode, the identity engine's `idsvr` schema, which `migrate` migrates (§6.1); its migration history is `dbo.__EFMigrationsHistory`." |
| §3.8 sketch | `Subject` becomes two lines: `[Collation("Latin1_General_100_BIN2")]` then `public string Subject { get; set; }  // varchar(255), as catalog.TenantMemberships.Subject (§7.3)`. |
| §6.6 step 2 | "…creates `Tellma.dev.acme.catalog` with its `idsvr` schema and provisions tenants 1 and 2 …". |
| §6.6 closing | "A missing catalog at web startup prints that exact `migrate` command instead of a stack trace; a missing or behind `idsvr` fails startup naming it (§5.7)." |
| §7 intro | After "…through `CatalogDbContext` (`Tellma.Core.Tenancy`)." add "Its model sets the platform collation (§6.7)." |
| §7.3 table-type sentence | End: "…registered on `CatalogDbContext`, whose model maps its `[TableType]` and `[Collation]` with spec 0012's conventions (spec 0012 §1.3; the physical name carries spec 0001's content hash)." |
| §8 JSON | Delete the `"Provisioning"` line. The `"Sql"` block gains a third line `"CreateDatabaseOptions": null` (comma after `"ApplicationPrincipal": "tellma-acme"`). |
| §8 key table | Delete the `Provisioning:CreateDatabaseTemplate` row. After the `Sql:Profiles…` row add a row: key `Sql:CreateDatabaseOptions`; meaning "§6.7; the options the migrator appends after `COLLATE` when it creates a catalog or tenant database on Azure SQL Database; null on-prem." The `Identity:Mode…` row's pointer becomes "§5.5, §5.7, §6.1". |
| §10 `Tellma.Core.Tests` row | "…with and without a template (§6.7)" becomes "…with and without `CreateDatabaseOptions` (§6.7); the collation pin over the catalog migrations". |
| §10 `Tellma.Distro.Acme.Web.Tests` row | Append "; the collation pin over Acme's tenant migrations". |
| §10 `Tellma.Distro.Acme.IntegrationTests` row | After the in-proc sign-in item insert "; `migrate` applies the identity engine's migrations to `idsvr`, and a second run applies none; a web host whose `idsvr` lacks the engine's last migration fails startup naming the `migrate` command". |
| §10, new paragraph before "Fixture:" | "**The collation pin.** A test with no database, over one migrations assembly and its model: `Model.GetCollation()` is `DatabaseCollations.Platform`; `HasPendingModelChanges()` is false; and no migration's `UpOperations` or `DownOperations` holds an `AlterDatabaseOperation` whose `Collation` differs from its `OldDatabase.Collation` (an initial migration's annotation-only operation passes). It runs over the catalog migrations and Acme's tenant migrations (rows above), and over the identity engine's (spec 0010 Part A — Testing)." |
| §10 Fixture | "Fixture: the catalog database is created per test class by `Migrate()` over the platform migrations, in the platform collation (§6.7). Tenant databases in the distribution suite come from `provision`. Test names describe behaviour and never cite a document." |
| §11 **Docs** | Identity item: "…the in-proc store is the catalog database's `idsvr` schema, which `migrate` migrates; …". |
| §11 **Not in scope of done** | "the identity-server amendments (spec 0010 Part A)" becomes "the identity-server and table-type amendments (spec 0010 Parts A and G)". |
| Decision 20 | "20. **In-proc identity stores in the catalog database's `idsvr` schema, migrated by `migrate`, with stable configured secrets** — the only distribution-wide database. The web process never migrates, so DDL rights never reach the web identity, and the migrator references the engine's migrations for it. Multi-instance standalone deployments cannot survive per-boot secrets (§1.2, §5.7, §6.1)." |
| Decision 26 | "…so the composition library and the migrator carry no ASP.NET package" becomes "…so the composition library carries no ASP.NET package". |
| Decision 32 | See below. |
| Review flags | New 23 (see below). |

**Decision 32** becomes:

"32. **Every platform database uses `Latin1_General_100_CI_AS`, set at creation and never
altered; every model carries it; the migrator creates every catalog and tenant database** — the
server default `SQL_Latin1_General_CP1_CI_AS` defines no order on Amharic text, and one collation
makes the catalog, the identity store, every tenant and every test compare text alike. Azure SQL
Database cannot change a collation, and on SQL Server existing columns keep theirs. A model
collation reaches EF's own creation and `dotnet ef database update`, and a first migration
carries none. EF writes neither Azure's options nor `WITH CATALOG_COLLATION = DATABASE_DEFAULT`,
without which spec 0001's table-type sweep fails with Msg 468. So the migrator keeps its own
statement, and no infrastructure template creates a platform database (§6.7)."

**Review flag 23** (new):

"23. **The migrator references the identity engine's migrations** (§1.2, §6.1) in a distribution
that runs in-proc, which brings ASP.NET Core's shared framework into the migrator. Alternative:
`Tellma.Core.Migrator` carries an idempotent script of the identity chain, built from
`Tellma.Identity.Migrations`, and runs it (no ASP.NET in the migrator; a build step and a parity
test). Flips if the migrator's image size or its coupling to the engine's packages matters."

## 5. Spec 0012 (E2)

| Section | Change |
|---|---|
| Context, the spec 0001 bullet | Append: "A standalone class gives a column a collation through spec 0010 Part G (§4.4)." |
| Non-goals, "The Queryex engine amendments" | Becomes "**The Queryex and table-type amendments** — spec 0010 Parts F and G (this spec composes them, §4.4, §11.2)." |
| §1.3, first paragraph | After "…the model contains exactly one mapped type per table." add: "The model sets the database collation with `modelBuilder.UseCollation(DatabaseCollations.Platform)` (spec 0011 §6.7)." This is where `UseCollation` is stated for the tenant model. |
| §1.3 conventions table | New row directly before `TableTypeConvention`: "`CollationConvention` | `[Collation(name)]` on a property → the column's collation: EF's `UseCollation(name)` on an entity, which the row image copies (spec 0001 §2); `TableTypeBuilder.UseCollation` on a standalone class (spec 0010 §G.1); inherited by a leaf". |
| §2.2 sketch | New line after `ExcludeFromTableTypeAttribute`: `public sealed class CollationAttribute(string name) : Attribute;  // on a string property: the column's collation; CollationConvention (§1.3) maps it` |
| §2.2 annotation table | New row after the `[TableType]`, `[ExcludeFromTableType]` row: "`[Collation]` | `CollationConvention` (§1.3) sets the column's collation | a standalone column's collation: spec 0010 §G.1; an entity-derived UDTT copies its column's: spec 0001". |
| §4.4, last paragraph | "A string column of a standalone type or a table variable declares the collation of any explicitly collated column a statement compares it with. Otherwise it takes the database collation (spec 0011 §6.7), and the comparison fails to compile with Msg 468. A standalone class declares it with `[Collation]` (§2.2, spec 0010 §G.1); `TenantMembershipList.Subject` and `UserInvitationOutcomeList.Subject` declare `Latin1_General_100_BIN2` this way." |
| §13.2 fixture | Replace "The database is created as spec 0011 §6.7 states, with `READ_COMMITTED_SNAPSHOT ON` as provisioning sets it (spec 0011 §6.1)." with "Its model sets the platform collation as `TellmaDbContext`'s does (§1.3), so `Migrate()` creates the database in that collation with read-committed snapshot on, as provisioning has it (spec 0011 §6.7)." |
| §13 DoD, **Not in scope of done** | "the Queryex engine amendments (spec 0010 Part F)" becomes "the Queryex and table-type amendments (spec 0010 Parts F and G)". |

## 6. Spec 0018 (E2)

- **§3.1 sketch.** `UserInvitationOutcome.Subject` becomes two lines:
  `[Collation("Latin1_General_100_BIN2")]` then
  `public string? Subject { get; set; }                     // varchar(255), as core.Users.Subject`.
- **§7, "Not in a migration."** Starts "The database collation and RCSI (`provision` sets both,
  spec 0011 §6.7), the `tellma_app` role and grants …" (rest unchanged).
- No other spec among 0013–0035 changes (inventory: fixtures there defer to spec 0012's; spec
  0014's `core.Users.Subject` row is storage truth and stands).

## 7. ARCHITECTURE.md (E2)

- **"One collation for every database"** (Migrations & seeding), replacing its opener and bullets:

  > **One collation for every database.** Every platform database uses `Latin1_General_100_CI_AS`,
  > the platform collation: the catalog, each tenant database, the identity database, and the
  > development and test databases that stand in for them. The server default
  > `SQL_Latin1_General_CP1_CI_AS` defines no order on Amharic text.
  >
  > - **Set once.** `CREATE DATABASE` sets the collation, and nothing alters it: Azure SQL Database
  >   cannot change it, and on SQL Server existing columns would keep the old one. No migration
  >   carries a collation change.
  > - **Azure.** The statement adds `WITH CATALOG_COLLATION = DATABASE_DEFAULT`, so system names
  >   compare like data (the table-type sweep joins them).
  > - **The migrator.** It creates every distribution's catalog and every tenant database itself,
  >   on every engine, and turns on read-committed snapshot isolation in every fresh one. No
  >   infrastructure template declares a platform database. It refuses an existing database in
  >   another collation.
  > - **Every EF model carries it.** The catalog, tenant and identity models set the platform
  >   collation, so EF's own creation (`dotnet ef database update`, or `Migrate()` on a missing
  >   development or test database) writes it too.
  > - **The identity database.** An operator creates the shared one once with the migrator's
  >   statement and migrates it by hand; the in-proc store is the catalog.
  > - **Tests.** Test servers keep their default collation.

  The closing pointer sentence ("The rule is in … Part A of …") stands.
- **Migrations & seeding, commands sentence:** "`migrate` (converge the catalog and every
  tenant)" becomes "`migrate` (converge the catalog, its in-proc identity schema `idsvr`, and
  every tenant)".
- **Hosting › Other notes, "Databases"** becomes: "**Databases:** the migrator creates every
  distribution's catalog and every tenant database in the shared pool; no Bicep declares a
  platform database. An operator creates and migrates the shared identity database by hand, once
  for the fleet (see [Migrations & seeding](#migrations--seeding--the-deploy-time-migrator))."
- **Identity › In-proc identity mode,** append: "The distribution's migrator applies the engine's
  migrations to `idsvr` with `migrate`; the web process never migrates, and the engine only checks
  at startup that `idsvr` is current."
- **Identity › Amendments to the identity server,** replace the ending "; and the identity
  database in the platform collation (see Migrations & seeding), which the in-proc engine never
  creates because its store is the catalog." with "; and the identity database in the platform
  collation (see [Migrations & seeding](…)), with a store check at every start that refuses
  another collation and a missing or behind store." The "one part per implemented spec that later
  specs change" description stands.
- **Library architecture, rule 8 (line ~454):** web references end "…`Tellma.Defaults.Azure.AspNetCore`
  and `Tellma.Identity` with `Tellma.Identity.Migrations` (in-proc)"; migrator: "its migrator
  references that library, `Tellma.Core.Migrator` and, for the in-proc identity store,
  `Tellma.Identity.Migrations`."
- **Distribution repo layout, Migrator comment (line ~509):** "References Tellma.Distro.<Slug> +
  Tellma.Core.Migrator, which carries the Design packages (+ Tellma.Identity.Migrations when the
  distribution runs the identity engine in-proc); never the Web project or the esproj, so its
  build needs no Node."
- **Platform repo layout (line ~278):** add after `Tellma.Identity/` a line
  `Tellma.Identity.Migrations/  # .csproj — the engine's EF Core migrations and design-time factory; referenced by Tellma.Identity.Web and, in-proc, by a distribution's Web and Migrator projects`
  (match the tree's indentation and comment column).
- **UDTT generation, standalone sentence (line ~656):** after "…derived from a plain
  `[TableType]`-annotated class that doubles as the TVP row DTO" insert "(the builder's
  `UseCollation` gives a class-derived column a collation, [spec 0010](docs/specs/0010-amendments-to-implemented-specs.md)
  Part G)".
- **No edit:** the "never run in the web process" sentence (now holds with no exception), the
  Reports Part F mention, the UI component library Parts B–E mention, `Tellma:Sql` in
  Configuration sections, the Rollout & Phasing list (the "later" line lives in spec 0010's
  Non-goals), and the Bicep mentions that name no database.

## 8. Edit list

Every inventory item, by file. "None" rows were checked and stay as they are; their owner is
still named so nothing is unowned.

### Spec 0010 (all E1)

| Section | Change | Decision |
|---|---|---|
| Context, parts list; "Specs 0001, 0007 and 0009…" | §2.1 | D-5 |
| Context, Other specs list | §2.1 | D-5, D-6c |
| Goals (Part A bullet; new Part G bullet) | §2.1 | D-5, D-6c |
| Non-goals, Part A list ("later" line) | §2.1 | D-3 |
| Non-goals "Editing specs 0001–0009" | none | — |
| How to read: closing labels | §2.1 | D-5 |
| How to read, other lines; Vocabulary | none | — |
| Part A order item 8 | §3.2 | D-3, D-4 |
| Part A Terms (new "Store check") | §3.2 | D-6c |
| Part A Placement (three rows) | §3.2 | D-6b |
| §A.11 whole section (title, header, opening, every bullet, Compatibility) | §3.1 | D-2, D-3, D-4, D-6a, D-6b, D-6c |
| Part A — Testing: fixture sentence, unit item, integration items | §3.3 | D-3, D-4, D-6b, D-6c |
| Part A — DoD: Projects, Docs, Not in scope | §3.4 | D-6b |
| Part A Decision 12 (revised), Decision 13 (new) | §3.5 | D-4, D-6b, D-6c |
| Part A Review flag 9 | delete | D-4 |
| Part F — Testing, Queryex fixture ("created as spec 0011 §6.7 states") | none: it has no EF model, so §6.7's statement still applies | D-6b |
| §F.15 platform-collation citation | none | — |
| Part G (new, end of file) | §2.2 | D-5 |

### Spec 0011 (all E2)

| Section | Change | Decision |
|---|---|---|
| Context "Other specs" list | §4.4 | D-5, D-6c |
| Context "Spec 0001 supplies the table types…" | none | — |
| Non-goals (self-serve, template, identity-server lines) | none | — |
| §1.1 Abstractions row | §4.4 | D-6b |
| §1.1 Core, Migrator rows; catalog-migrations paragraph | none | — |
| §1.2 layout comment; Web and Migrator references | §4.4 | D-6c |
| §1.3 Migrator illustration and "neither … nor the migrator" | §4.4 | D-6c |
| §2.3 startup checks; §3.4 Startup; §5.1 `AddTellma` line; §5.9 health | none (§5.7 owns the in-proc store's startup behaviour) | D-6c |
| §3.2 Lifecycle | none | D-2 |
| §3.2 Contents | §4.4 | D-6c |
| §3.8 sketch | §4.4 | D-5 |
| §5.7 options and paragraph | §4.2 | D-6c |
| §6.1 sketch, `migrate` row, paragraph | §4.3 | D-2, D-6c |
| §6.1 `provision`, `status` rows; design-time factory text | none | D-6b |
| §6.4 RCSI statement | none | D-6b |
| §6.5 tenant model host | none (spec 0012 §1.3 owns the context) | D-6b |
| §6.6 step 2 and closing | §4.4 | D-6c |
| §6.7 intro, constant, every bullet, example | §4.1 | D-2, D-3, D-6a, D-6b |
| §7 intro | §4.4 | D-6b |
| §7.3 table-type sentence | §4.4 | D-5 |
| §7.3, §7.4 column tables | none | — |
| §8 JSON, key table | §4.4 | D-6a, D-6c |
| §10 rows (Core.Tests, Acme.Web.Tests, Acme.IntegrationTests) | §4.4 | D-4, D-6a, D-6b, D-6c |
| §10 Core.IntegrationTests row | none (refusal and creation pins stand) | D-4 |
| §10 collation pin paragraph; Fixture | §4.4 | D-6b |
| §11 Behavior | none | — |
| §11 Docs; Not in scope of done | §4.4 | D-5, D-6c |
| Decisions 4, 21 | none | D-6c |
| Decisions 20, 26, 32 | §4.4 | D-2, D-6b, D-6c |
| Review flag 11 | none | — |
| Review flag 23 (new) | §4.4 | D-6c |

### Specs 0012–0035 (all E2)

| File, section | Change | Decision |
|---|---|---|
| 0012 Context; Non-goals; §1.3 paragraph and table; §2.2 sketch and table; §4.4; §13.2; DoD | §5 | D-5, D-6b |
| 0012 §1.1, §4.3, §13.1 | none | — |
| 0018 §3.1 sketch; §7 "Not in a migration" | §6 | D-5, D-6b |
| 0018 §7 initial migration list | none | D-6b |
| 0014 §3.1 `core.Users.Subject` | none | D-5 |
| 0023 Parts B–E mentions | none | D-5 |
| Fixture mentions in 0013, 0014, 0015, 0017, 0019, 0020, 0021, 0022, 0028 | none (they defer to spec 0012's fixture) | D-6b |
| Single-part citations of spec 0010 (0011–0018) | none | D-5 |

### ARCHITECTURE.md (all E2)

| Place | Change | Decision |
|---|---|---|
| Migrations & seeding: "One collation for every database" (opener and all bullets) | §7 | D-2, D-3, D-6b |
| Migrations & seeding: commands sentence | §7 | D-6c |
| Migrations & seeding: "never run in the web process", Provisioning sentence, Design note | none | D-6b, D-6c |
| Hosting › Other notes › Databases | §7 | D-2, D-3 |
| Identity › In-proc identity mode | §7 | D-6c |
| Identity › Amendments paragraph | §7 | D-3, D-6c |
| Library architecture rule 8; distribution layout Migrator comment; platform layout | §7 | D-6c |
| UDTT generation, standalone sentence | §7 | D-5 |
| UDTT "Derived row image" (collation copied) | none | D-5 |
| Reports Part F; UI library Parts B–E; Configuration sections | none | D-5, D-6a |
| Container Apps Job, version ordering, CI/CD fleet `migrate` | none | D-6c |
| Bicep mentions naming no database (layouts, onboarding, control plane, diagram) | none | D-2 |
| Rollout & Phasing | none (the "later" line is in spec 0010) | D-3 |

## 9. Open questions for Ahmad

1. **The migrator references `Tellma.Identity.Migrations` (recommended, designed to).** `migrate`
   owning `idsvr` needs the identity engine's migrations in the migrator. That brings ASP.NET
   Core's shared framework and the engine's packages into an in-proc distribution's migrator,
   against today's "a migrator build carries no ASP.NET package". The alternative is a
   build-time idempotent script of the identity chain shipped in `Tellma.Core.Migrator`: no
   ASP.NET there, but a build step, batch splitting and a parity test. Recorded as spec 0011
   flag 23. Choosing the script changes §4.3, §4.4 (§1.2, §1.3, Decision 20, 26) and §7's
   reference edits.
2. **An in-proc host with an unreachable catalog does not start.** The store check, like today's
   seeding, throws from a hosted service, while spec 0011 §3.4 starts a host not-ready when the
   catalog is unreachable. This predates the round and is not designed here. Recommendation: leave
   it, since in-proc mode serves development and on-prem standalone hosting; revisit when on-prem
   delivery is designed.
3. **Noticed, not changed:** spec 0012 §4.4 registers standalone classes "through spec 0011's
   `Model<T>()`", but spec 0011 requires `Model<T>`'s `T` to implement
   `IEntityTypeConfiguration<>`. The call site that registers a plain row class is unstated.
