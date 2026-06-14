# Spec: EF Core Migrations Extension for SQL Server Table Types (UDTTs)

## Context

Performance is one of the most important design goals in Tellma ERP. All persistence goes
through a single **bulk save path** — even when a single entity is saved — using SQL Server
table-valued parameters (TVPs), whose schemas are user-defined table types (UDTTs). This
allows a user to upload 10,000 entities without timing out.

Two architectural decisions shape this spec:

1. **IDs are assigned in C#, not by the database.** Every table draws its surrogate keys from
   a per-table SQL sequence; the application reserves ranges (via `sp_sequence_get_range`)
   through an in-process buffered allocator. There are **no IDENTITY columns** anywhere in
   the schema. Consequently, rows arrive at the persistence boundary with real PKs and real
   FKs already wired — **UDTTs contain no `[Index]`, `[HeaderIndex]`, or any other
   index/ordinal columns**, and no ID-mapping results need to be returned from inserts.
2. **There is no logic in the database.** All business logic lives in C#. The payload sent to
   SQL Server is the fully enriched row image (audit columns, denormalizations, and workflow
   state already computed by the service layer). All SQL that consumes UDTTs is **dynamic SQL
   generated in C#** — no stored procedures or functions ever reference a generated UDTT.

(Terminology: prose and documentation use the SQL Server term **UDTT**; public API names use
**TableType** — shorter, and not an acronym.)

## Task

Create two new projects under `src/core` that extend EF Core migrations so that opted-in
tables automatically get a matching UDTT, created and kept in sync by the same migrations
pipeline that manages the tables themselves:

- **`Tellma.Core.EntityFrameworkCore`** (runtime): the options extension, annotations,
  fluent/attribute configuration, migration operations and `MigrationBuilder` extensions,
  migrations SQL generation, and the metadata API. References EF Core and the SQL Server
  provider only — never `Microsoft.EntityFrameworkCore.Design`. (Scaffolded migration files
  compile into the application and call the operations and builder extensions at apply time,
  which is why these must live runtime-side.) This project is the home for all of Tellma's
  EF Core extensions; the table-types feature lives in the
  `Tellma.Core.EntityFrameworkCore.TableTypes` namespace, and future features get sibling
  namespaces. **Admission rule** (to prevent junk-drawer drift): code belongs in this project
  only if it extends EF Core's own surface (options extensions, migration operations,
  conventions, metadata/annotations) and is generic over any model. The runtime ID allocator
  and save pipeline do not qualify (runtime persistence), and nothing referencing Tellma's
  entities qualifies (Rule 3).
- **`Tellma.Core.EntityFrameworkCore.Design`** (design-time): the C# migration operation generator
  (deriving `CSharpMigrationOperationGenerator`) and the `IDesignTimeServices` registration —
  the only types that reference the `Microsoft.EntityFrameworkCore.Design` package and its
  dependency tree (Roslyn, templating). Referenced only by the distribution's **migrator
  project** (the console host that is also the EF design-time target and owns the scaffolded
  `Migrations/` folder — never by the web host). Discovery: EF tooling scans **only the startup
  and migrations assemblies** for `[assembly: DesignTimeServicesReference]` — never referenced
  libraries — so the Design package ships MSBuild targets (`build/`) that inject the attribute
  into the **consuming migrator project's assembly** at build time via `WriteCodeFragment`, the
  same mechanism EF's own extension packages (e.g. SqlServer.HierarchyId) use; referencing the
  package remains sufficient, with no manual wiring, and the runtime assembly carries nothing.
  The discovery flow must work identically under Phase-1 in-repo `ProjectReference`s (where the
  repo's `Directory.Build.targets` imports the same targets file, self-gated on a
  `ProjectReference` to the Design project) and under published-package references (NuGet
  imports the packaged targets); tests cover both.

This split mirrors EF's own `Microsoft.EntityFrameworkCore` / `.Design` separation. It is a
day-one requirement, not a refactor: an unsplit assembly would carry IL references to Design
types in its type signatures, which either pulls Roslyn into the web server's publish output
or (with `PrivateAssets`) plants a latent `ReflectionTypeLoadException` for any code that
scans the assembly in environments where `Design.dll` is absent — i.e., production.

### 1. Configuration and opt-in

- The extension is **additive**: `optionsBuilder.UseSqlServer(...).UseTableTypes()`,
  implemented as an `IDbContextOptionsExtension`. Do **not** wrap or replace `UseSqlServer()`
  (no mirroring of its overloads, no coupling to its release cadence). `UseTableTypes` takes
  a **required sweep scope** — a stable string naming which types this context owns (see
  §3 → Versioning → scoping). It has no default: deriving it from the context's type name
  would silently change ownership on a class rename.
- **0 or 1 UDTT per table.** A table opts in via the fluent API
  (`entity.HasTableType(name?, schema?)`) or an attribute on the entity class
  (`[TableType(Name = ..., Schema = ...)]`). Default name/schema follow a single
  convention — the table's own schema plus a `List` suffix: `[<TableSchema>].[<TableName>List]`,
  e.g. `[gl].[InvoicesList]` — overridable per entity. The configured name is the type's
  **logical name**; the deployed **physical name** appends a content-hash version suffix
  (`[gl].[InvoicesList_3fa9c2d1]` — see §3 → Versioning).
- **Attribute inheritance**: `[TableType]` and per-property exclusion attributes are inherited
  by derived entity classes — under leaf-only mapping, a distribution leaf that extends a pack
  default inherits the pack's opt-in and exclusions. Fluent configuration always takes
  precedence over attributes, and explicit fluent opt-outs (`HasNoTableType()`, re-including an
  attribute-excluded column) let a leaf override inherited attribute configuration.
- There is **no separate DTO model**. UDTTs are derived from the same entity classes /
  relational model that generate the tables. (Request/response DTOs at the HTTP boundary are
  an application concern and are invisible to this project.)
- Per-table configuration knobs:
  - **Column exclusions** (fluent or attribute on the property) for columns that should not
    appear in the type.
  - **Rowversion handling**: if the table has a rowversion/concurrency-token column, the type
    includes it as **nullable** `binary(8)` by default (nullable because insert rows carry no
    value; present so bulk UPDATEs can perform optimistic-concurrency checks); excludable per
    table.
  - **Memory-optimized types**: opt-in flag emitting `MEMORY_OPTIMIZED = ON` on the type.
    In-Memory OLTP is a hosting prerequisite (Premium/Business Critical on Azure SQL — not the
    default shared standard elastic pools — or a memory-optimized filegroup on-prem), so the
    generated SQL pre-flights support (`DATABASEPROPERTYEX(DB_NAME(), 'IsXTPSupported')`) and
    THROWs an actionable error on unsupported tiers. Deliberately **no silent fallback** to an
    on-disk type: the two declarations differ structurally (index kinds), so a fallback would
    create cross-environment schema drift.
  - **Grants**: a configurable set of database principals for which the migration emits
    `GRANT EXECUTE ON TYPE::<type> TO <principal>` with every version create.

### 2. UDTT derivation rules

The UDTT is a **derived row image** of the table:

- **Included columns** = the insertable/updatable columns of the entity type's **own scalar
  properties**, minus the exclusion list — CLR-declared and shadow alike: a
  convention-created shadow FK column is included like any other mapped scalar column, at
  the table's resolved position for it (UDTT order mirrors the table for shadow columns
  exactly as for CLR-declared ones). Computed columns are always excluded. Columns
  contributed by complex types, by owned entity types mapped into the owner's table, or by
  `ToJson()` document mappings are outside the derivation's reach (it walks the entity's
  own properties only) — and a row image missing columns its table has is the same silent
  drift/truncation failure mode that killed `ForSave`. The finalizing convention therefore
  **rejects the opt-in** of an entity whose mapped table carries any such columns, with an
  actionable error naming them: the partial row image is an impossible state, not a
  documented hazard. (Tellma's models use none of these mappings today.) Column names,
  store types, max length, precision, scale, nullability, and collation are taken from the
  relational model EF already built for the table — never re-declared.
- **Normalization**: the type never carries IDENTITY (moot given sequences, but enforce it),
  defaults, FK constraints, or named constraints.
- **Primary key**: the type's PK mirrors the table's PK columns. (IDs are app-assigned and
  always present, for inserts and updates alike; the PK enforces in-batch uniqueness and
  aids join plans.) Keyless entities therefore cannot opt in — rejected at finalizing time
  with an actionable error.
- **Column order** is taken from the table's resolved column order and is **part of the
  type's contract**, because TVP binding (`SqlDataRecord`/`DataTable`) is ordinal. The
  resolved order MUST be captured in the model snapshot so that a pure reorder produces a
  diff (see §3). Explicit ordering via EF's `HasColumnOrder()` flows through.
- **TPT mirrors the tables.** Under TPT inheritance each mapped entity type's table gets its
  own row-image type: the base table's type carries the shared columns, each leaf table's
  type carries the PK plus the leaf's own columns. A flattened (TPC-style) leaf type was
  considered and rejected: the per-table `INSERT … SELECT FROM @tvp` pipeline needs per-table
  row sets under TPT regardless, a flattened type would force every consumer to split one TVP
  across multiple tables, and it would break the 1:1 type↔table semantics the derivation,
  metadata API and concurrency story (the base table owns the rowversion) rest on. The costs
  of mirroring are one extra TVP per save and repeated base-column *declarations* across leaf
  types — no data is duplicated, since TVPs carry data only transiently. TPT is rare in the
  architecture (Core's abstract `Document` root); revisit only if the save pipeline surfaces
  a concrete need.
- **Shared tables: TPH, owned types, table splitting.** A table type belongs to the entity
  type that owns the table, and its row image covers that entity type's own columns. Under
  TPH the root derives the single type (root-declared columns plus the discriminator) — and
  the root's opt-in is **rejected** if any TPH-derived type declares mapped scalar columns,
  since the root's image would silently miss them; a pure-discriminator hierarchy stays
  legal, because its row image is genuinely complete. TPH with derived-declared columns is
  unsuitable for UDTT-saved aggregates (the architecture uses leaf-only mapping, with TPT
  for the rare shared root). An explicit fluent opt-in on a TPH-derived type is a
  configuration error (actionable throw); a `[TableType]` attribute inherited onto one is
  simply covered by the root's type. Owned entity types map into their owner's table and
  likewise cannot opt in (the owner does, subject to the included-columns rejection above).
  Under table splitting each sharing entity may derive the image of its own column slice,
  but the default names collide by construction (both `<TableName>List`) and fail the
  global uniqueness check — explicit names are required, and the resulting types are
  slices, not full row images. (Slices do not violate the partial-row-image rejection
  above; the principle differs: a slice omits only columns that belong to the *other*
  sharing entity's save unit — the union of the slices covers the table — whereas
  complex/owned/JSON omissions would silently drop columns of *this* entity's own save.)

### 3. Migrations pipeline behavior

- New migration operations: `CreateTableTypeOperation`, `DropTableTypeOperation`, and
  `CleanupTableTypesOperation` (the keep-list sweep — see Versioning below), plus
  `MigrationBuilder` extension methods (`CreateTableType`, `DropTableType`,
  `CleanupTableTypes`) so the operations can also be authored manually. The differ never
  emits `DropTableTypeOperation`; it exists for manual authoring, addresses exactly one
  **physical** name, and keeps the hard dependency THROW (see Drop safety).
  `CleanupTableTypes` has two authoring shapes: the explicit keep-list overload (what the
  scaffolder emits — the literal name list is part of what review sees) and a no-list
  overload for hand-written migrations, which resolves the keep-list from the migration's
  target model at SQL-generation time (the generator receives each migration's model via
  its `.Designer` attribute) — hand-listing hash-suffixed physical names is hopeless.
  Note that "collect everything stale **now**" is *not* a migration: a scaffolded migration
  applies during future deployment windows on every database that replays the chain —
  exactly when a shortened grace is wrong. Immediate collection is therefore an out-of-band
  concern, deferred (see Versioning → fresh-database replay).
- **Diffing**: table-type definitions are stored as model annotations (the way sequences
  are) and therefore round-trip through the model snapshot. In snapshot files each definition
  is rendered as a readable, multi-line `HasTableTypeDefinition(...)` fluent call (one line
  per column, so definition changes appear as per-line diffs in review), and replaying the
  call rebuilds the annotation exactly. The differ emits **creates only**:
  - a `CreateTableTypeOperation` for every definition whose canonical JSON has no exact
    match on the source side — a new opt-in, or any aspect of the derived definition
    changing (column added/removed/renamed/retyped, facet change, order change, PK change,
    exclusion-list change, memory-optimized or grant config change), each of which yields a
    new physical version name (see Versioning below);
  - one `CleanupTableTypesOperation` carrying the target model's complete physical-name
    keep-list, appended whenever the two definition sets differ at all — including pure
    removals, which emit no other operation.

  Old versions, renamed types, and removed types are never dropped by the migration that
  obsoleted them; retirement is exclusively the sweep's job. Because this rule is
  **direction-agnostic** — creates for whatever the target side has, keep-list from the
  target model — the scaffolded `Down()` is automatically correct, not a special case: it
  re-creates the old version (a no-op when it still exists, since creates are idempotent by
  content-addressed name), and its keep-list sweep un-orphans the old version while
  orphaning the new one, which is then garbage-collected after the grace period like any
  other stale version.
- **There is no ALTER for table types in SQL Server** — and no in-place recreate happens
  either: a definitional change creates the **new version alongside the old one** (see
  Versioning below), so at no instant is a type that some running app needs absent or
  reshaped. Memory-optimized types still require non-transactional DDL: In-Memory DDL cannot
  run inside a user transaction, so the generator emits those commands with suppressed
  transactions (mirroring the SQL Server provider's own memory-optimized handling), which
  splits the surrounding migration's single-transaction guarantee into separately committed
  chunks. But create-before-retire means a mid-migration failure never leaves a needed type
  missing: the old version is untouched, and re-running the migration completes the
  idempotent create.
- **Drop safety**: the generated drop SQL begins with a pre-flight check against
  `sys.sql_expression_dependencies` (`referenced_class = 6`, i.e. TYPE) and `THROW`s with the
  list of dependent modules by name if any persisted module references the type — converting
  a cryptic error 3732 into an actionable failure. (Per the architecture, no such dependents
  may exist; this guard enforces it at the moment it matters.) The THROW is reserved for
  explicitly authored drops (`DropTableType`), where the operator's intent is "this must go
  now." The sweep's garbage-collection drops run the same dependency check but **skip the
  offending orphan and surface it** instead of throwing: blocking every future type-touching
  deployment to protect the GC of a version nothing in the app uses would be
  disproportionate — failure to collect is, by the sweep's own contract, harmless clutter.
  The skipped orphan stays visible — the catalog state is the authoritative record (an
  over-aged orphan, queryable via its `OrphanedAtUtc` stamp), while the low-severity
  message in the migration output is best-effort (visible only when the migrator captures
  info messages) — and the out-of-band module that caused it is exactly what Rule 5's
  other layers exist to flag, alongside the production drift check described in
  ARCHITECTURE.md (which compares a clean migrations-deployed schema against the live
  databases, and would surface the unexpected module itself).
- Every version create emits the configured grants from §1 with it. Grants are part of the
  derived definition, so a grant-list-only change also produces a new version rather than a
  `GRANT`/`REVOKE` delta against the existing one. This is a deliberate trade-off: one
  change mechanism instead of two, and revocation needs no separate diffing path — the new
  version carries exactly the configured set, and the over-granted old version ages out via
  the sweep (revocation therefore completes when the old version is collected:
  grace-period-delayed, consistent with the N−1 window in which the old app legitimately
  still executes against it). Acceptable because grant changes are rare and a version
  create is pure metadata DDL — no data motion.
- **Values are escaped; identifiers are delimited — never concatenated raw.**
  `dotnet ef migrations script` emits a static `.sql` file, so the generator uses **no
  command parameters**; every caller-supplied string is made safe inline instead. *Values*
  — the sweep scope, extended-property values (logical name, hash), the comparison literals
  in the sweep's `WHERE` clauses, and `THROW` message substitutions — are rendered through
  the relational type mapping's safe SQL-literal generation (single quotes doubled, `N`
  prefix). *Identifiers* — type name, schema, and grant **principals** — go through the
  provider's `DelimitIdentifier`/`QUOTENAME`, including any name composed into the runtime
  dynamic SQL inside the drop guard or the sweep. This mirrors the platform rule
  (ARCHITECTURE.md → Data Layer): user-input values are never concatenated, and identifiers
  are interpolated only after delimiting. A scope or principal containing `'`, `]`, or `--`
  must therefore produce valid, injection-free SQL.
- **Annotation contract evolution**: definitions serialize as canonical JSON with a fixed
  property order and **nulls omitted**. The canonical JSON **includes the logical name and
  schema** (and, for table-derived types, the paired table's name and schema). This is
  load-bearing twice: the JSON is the differ's set-membership identity, so a pure rename
  with an identical shape makes the definition sets differ (emitting the new version's
  create and the sweep that retires the old name's versions), and it is the hash input, so
  the renamed type gets a new physical name. A consequence in the conservative direction:
  renaming a paired *table* also produces a new type version even when the type's shape is
  unchanged — a new name for an unchanged shape is harmless (the old version ages out),
  whereas a reused name for a changed shape is the corruption this design exists to
  prevent. Omitting nulls is the forward-compatibility story: a
  new optional knob added to the definition shape serializes only when set, so upgrading the
  library never changes the serialized form of existing definitions and never produces
  spurious drop/creates. Conversely, any change that alters the serialized form of existing
  definitions (an always-serialized field, a default flip, a property rename) is a breaking
  contract change: it makes every existing type appear changed, and must ship with a release
  note stating that the next scaffolded migration produces new versions of every type (the
  old versions then age out through the sweep).
- **Scaffolding**: `dotnet ef migrations add` must scaffold the new operations into migration
  files. Implement via the design-time C# operation generator in the `.Design` project,
  discovered through the `[assembly: DesignTimeServicesReference]` that the Design package's
  MSBuild targets inject into the consuming migrator assembly (see the `.Design` bullet above)
  so that referencing the libraries is sufficient — no manual wiring in consuming projects.
- The operations must work with `dotnet ef migrations script` (including `--idempotent`) and
  migration bundles.

#### Versioning — content-hash names, retention, and the deployment window

Zero-downtime deployments migrate the database **before** the app fleet swaps, and a
slot-swap rollback puts the *old* app back on the *new* schema — so an app one version
behind must keep working against the migrated database. TVP binding is positional and
full-width: the client streams rows against the server-side type's column list by ordinal,
and client-supplied column names are ignored. Under a single mutable type name, an N−1 app
binding a reshaped definition fails loudly on a retype or column-count change — but two
same-typed columns swapped (or a same-type rename) binds cleanly and writes values into the
wrong columns: **silent data corruption**. The mitigation is to make type identity
content-addressed:

- **Physical names are versioned.** The configured name (§1) is the *logical* name; the
  deployed physical name is `<LogicalName>_<hash8>`, where `hash8` is the first 8 hex chars
  of the SHA-256 of the definition's canonical JSON. Equal definitions ⇔ equal JSON ⇔ equal
  physical name; any definitional change yields a *different* physical name, created
  **alongside** the old version. Each app instance derives physical names from its own
  compiled model through the metadata API (§6) — never from the database — so an N−1 app
  (or a rolled-back app) keeps binding the exact shape it was built against. The hash is
  always derived from the definition JSON, never stored in it, so no drift is possible.
  Physical names must fit SQL Server's 128-character identifier limit, so the finalizing
  convention validates logical names at ≤ 119 characters (128 minus the 9-character version
  suffix) with an actionable error. Versioned names are viable precisely because of Rule 5:
  physical names are app-internal — no persisted module, hand-written SQL, or external
  consumer ever spells them.
- **Creates are idempotent — including partial creates.** `CreateTableType` SQL keys on
  `IF TYPE_ID(N'<physical name>') IS NULL`; because the name pins the content, "already
  exists" means "already correct", and creation resolves into three cases:
  - *Absent* → create the type, then stamp it.
  - *Present but unstamped or partially stamped* → an **aborted prior create**. The
    memory-optimized path runs without a transaction (see the no-ALTER bullet above), so
    `CREATE TYPE` can commit while the follow-up `sp_addextendedproperty` stamping does not —
    leaving a correctly-shaped type at the content-addressed name with missing or incomplete
    stamps. Treat this as our own unfinished create and **complete the stamps idempotently**
    (each stamp is add-or-update, since a re-run can re-enter mid-stamp), then proceed — no
    THROW. The name *is* the content, so a same-named type is by construction the one
    intended; the only residual risk is adopting an unstamped out-of-band type sitting at
    that exact hash-suffixed name, acceptable because names are content-addressed and
    app-internal (Rule 5).
  - *Present and fully stamped* → verify: a stamped hash ≠ mine `THROW`s **error 53103**
    (integrity — wrong bytes at the name, an astronomically unlikely truncated-hash collision
    or a squatter); a matching hash with a foreign scope `THROW`s **error 53104** (ownership
    — see the scoping bullet below); otherwise a no-op.

  These cases are what make `Down()` migrations, squash re-runs, mid-migration failure
  re-runs, and the non-transactional memory-optimized path all converge.
- **Every created type is stamped** with extended properties — SQL Server's key-value
  catalog metadata (`sp_addextendedproperty`, class 6 = TYPE, queryable from
  `sys.extended_properties`): `Tellma:TableType:LogicalName` (the sweep's grouping key,
  which also restores DBA legibility of hashed names in `sys.table_types`),
  `Tellma:TableType:Scope` (the sweep-scope key — see the scoping bullet below),
  `Tellma:TableType:DefinitionHash` (the full SHA-256 — the collision check above, and a
  ready-made verification hook for the future save pipeline), and
  `Tellma:TableType:OrphanedAtUtc` (written and cleared by the sweep below).
- **Retirement is garbage collection, not a diff event.** The `CleanupTableTypesOperation`
  appended to every type-touching migration carries the target model's complete
  physical-name keep-list; at apply time it sweeps every Tellma-stamped type **in its own
  sweep scope** (never another context's — see the scoping bullet below): (1) in the
  keep-list → clear any orphan mark (this is what restores a version
  on `Down()`, or on an A→B→A flip-flop); (2) not in the keep-list and unmarked → mark
  `OrphanedAtUtc` from the database server's own UTC clock (`SYSUTCDATETIME()`; marking and
  comparison are both server-side, so migrator-host clock skew is irrelevant); (3)
  orphan-marked longer than the **grace period** ago — 48 hours; the grace is a property of
  `CleanupTableTypesOperation`, the differ always emits the default, and the scaffolded
  value is frozen into the migration file, so changing the library default never
  retroactively changes already-scaffolded sweeps — → `DROP TYPE`,
  unless a persisted module references the orphan, in which case the sweep skips it and
  surfaces the offender (see Drop safety: the THROW is reserved for manual drops). The
  grace is time-based rather than "drop on the next deployment" because
  migrations cannot see deployment boundaries: several migrations often apply in one
  deployment, and a mark-then-drop-on-next-migration rule would collect a version *within*
  a single deployment window while the old app still needs it. The failure direction is
  safe by construction: sweeping too late is harmless catalog clutter; only sweeping too
  early can corrupt.
- **The sweep is one trailing, non-transactional command.** Create/Drop operations carry
  `IsMemoryOptimized` at scaffold time, so the SQL generator knows when to suppress the
  transaction — but the sweep's drop set is *discovered at apply time*, and whether a
  discovered orphan is memory-optimized is unknowable when the migration is scaffolded,
  while transaction suppression is a command-level decision made at SQL-generation time.
  There is no per-type conditional suppression. So `CleanupTableTypesOperation` is always
  emitted as the migration's last command with the transaction suppressed — safe by the
  sweep's own philosophy: it is idempotent GC, every step (marking, clearing, collecting)
  converges on re-run, and partial completion just means some orphans survive until the
  next sweep.
- **The sweep discovers, never remembers.** No lineage is recorded anywhere — the live
  model cannot know history (the finalizing convention sees only the present), and
  squashing would erase any recorded lineage anyway. "All physical versions of logical type
  X" is a catalog query over the `LogicalName` stamp. Squashing the migrations folder
  (delete `Migrations/`, scaffold a fresh `Initial` once all databases are current)
  therefore self-heals: the squashed `Initial` knows nothing about old versions, but the
  next sweep discovers and retires them regardless. The same property makes renames and
  opt-outs ordinary: a renamed logical type's old versions and a removed type's versions
  are just orphans — retained for the grace period, then collected. No special cases, and
  nothing lingers forever.
- **Scope is ownership** (the single home for scoping; other bullets cross-reference here).
  `UseTableTypes` requires a stable **sweep-scope** string, stamped on every type the
  context creates (`Tellma:TableType:Scope`). It has no default — deriving it from the
  context's type name would silently change ownership on a class rename. Scope does two
  things:
  - **GC isolation.** The sweep's scan is database-global by discovery, so without a
    boundary two `UseTableTypes()` contexts sharing one database would garbage-collect each
    other — each context's keep-list comes from its own model, so the other's live types
    look like orphans. The sweep therefore touches only types stamped with its own scope; a
    context never marks or collects another's.
  - **Single ownership, with an explicit opt-out for sharing.** Exactly one context *owns*
    (creates and sweeps) a given physical type. To share a type across contexts in one
    database — the canonical case being `IdList` and the other bulk shapes — one context
    owns it and the others declare it `ExcludeFromMigrations()` (mirroring EF Core's
    table-level opt-out, [TableBuilder.ExcludeFromMigrations]): an excluded type stays in
    the model and the metadata API, so its context binds it at runtime by computing its
    content-addressed name, but the differ emits no create for it and the sweep ignores it.
    Because the name is content-addressed, the excluded context computes the identical name
    and binds the owner's type directly — no duplication, no second physical type.
  - **The ownership guard.** If two contexts both try to *own* the same-shaped type (neither
    excludes it), the second's idempotent create finds the name present with a *matching*
    hash but a foreign scope and `THROW`s **error 53104** — actionable: "type already owned
    by scope '<X>'; exclude it from this context's migrations, or own it in only one
    context." This is deliberately distinct from the content-integrity 53103 (wrong bytes at
    the name): 53104 means the data is safe but ownership is misconfigured.

  Changing a context's scope is a rare, deliberate act that strands its old-scope types as
  orphans of a scope nothing sweeps; until an out-of-band sweep entry point exists (deferred
  — see fresh-database replay below), those are dropped manually.

Walkthrough of the motivating case: a migration swaps two same-typed columns of
`[gl].[InvoicesList]`. Migrate creates `[gl].[InvoicesList_<new>]` alongside
`[gl].[InvoicesList_<old>]`, and the sweep orphan-marks the old version. Through the swap
window — and through any app rollback — N−1 instances keep binding `InvoicesList_<old>`,
whose ordinals match their compiled model exactly; new instances bind `InvoicesList_<new>`.
No instant of mis-binding exists. The first type-touching migration to run at least 48
hours later collects the old version. Running `Down()` instead un-orphans the old version
and orphans the new one.

**Fresh-database replay materializes the version history.** Replaying the full migration
chain on a new database creates every historical version: each migration's creates run,
each sweep orphan-marks the versions the next model abandoned, and the marks are all
younger than the grace period — so a freshly provisioned database ends with the current
version of each type plus a pile of orphan-marked ancestors. This is harmless (pure
metadata, collected by a later sweep) and bounded by the squash cadence, but it is
deliberate that the sweep itself never collects them early. A "too-young" collection rule
cannot be made safe: orphan-mark recency is exactly what the multi-migration deployment
batch produces for a version the still-running app needs, and creation-date recency
(`sys.types.create_date`) fails for back-to-back deployments — a hotfix deployed minutes
after a release would see the release's *live* versions as recently created and collect
them out from under the running fleet. Both rules fail in the corrupting direction, so
neither is allowed. Immediate collection is an *orchestrator* decision — expressible only
where "no app has ever bound this database" is knowable, and never as a scaffolded migration
(which applies during future deployment windows on every database that replays the chain).
The spec ships **no immediate-collection entry point today**: the replay orphans are
harmless (pure metadata), and the next type-touching migration's sweep collects them after
the grace period. A small out-of-band sweep entry point — runtime project, no Design
dependency, keep-list resolved from the app's compiled model, explicit grace, scoped — can
be added later if replay clutter or exact post-swap retention ever justifies it (see Out of
scope); the time-based in-migration sweep is correct without it.

### 4. Sequences and seeding (conventions only — no custom support)

Per-table ID sequences are declared with EF's standard `HasSequence` / `CreateSequence`,
under the platform naming convention `sq_<TableName>` in the table's schema; this library
ships **no sequence-related operations**. (Disabling database key generation on UDTT-paired
tables and wiring the per-table sequences and the allocator is specified separately.)
Anyone inserting rows directly via
SQL (imports, restores, backdoor fixes) is responsible for keeping the sequence consistent;
the runtime allocator additionally self-heals from forgotten resets (see Out of scope).

**Seed conventions** (documented by this project, enforced by tests in §Testing): `HasData`
is restricted to well-known rows whose IDs code references, confined to a reserved band
disjoint from sequence output (low band with `StartsAt` above it, or negative IDs). Ordinary
reference data is seeded at runtime through the bulk save pipeline by the deploy-time
migrator and therefore draws IDs from the allocator, keeping sequences consistent by
construction. The allocator's self-healing makes band violations non-fatal, but the band
remains required so well-known IDs stay deterministic.

### 5. Standalone table types (built-in and custom)

The "0 or 1 per table" rule applies to *table-derived* types only. Operation-specific shapes —
bulk delete by ID, bulk state updates, bulk assignment of a handful of targeted columns — need
types paired with **no** table. These **standalone table types** flow through the exact same
annotations, differ, operations, SQL generation and metadata API as table-derived types; only
their definition source differs. That includes versioning (§3): their physical names carry
the same content-hash suffix and the same sweep retires their stale versions — one
mechanism, no special handling.

Two authoring routes:

- **Ad-hoc fluent**, for one-off shapes declared inline:

  ```csharp
  modelBuilder.HasTableType("IdStateList", schema: "dbo", type => type
      .Column<int>("Id")
      .Column<short>("State")
      .HasKey("Id")
      .HasGrants("tellma_app"));
  ```

- **Class-derived**, for shapes worth a named C# type: a plain class (NOT an entity) annotated
  with `[TableType]`, registered with `modelBuilder.HasTableType<T>()`. Columns derive from the
  class's public read-write properties in **reflection metadata order** — de-facto C# source
  declaration order, but not a CLR guarantee (a partial class split across files is the
  realistic reordering vector). The order is not re-derived at runtime: it is captured in the
  derived definition and pinned by the model snapshot, so a flip surfaces as a scaffolded
  drop/create diff in review rather than silently re-binding ordinals — the same reliance EF
  itself accepts for table column order. Derivation honors the standard annotations
  (`[Key]` incl. composite, `[MaxLength]`/`[StringLength]`, `[Unicode]`, `[Precision]`,
  `[Column(TypeName = ...)]`, `[Required]`, `[NotMapped]`, `[ExcludeFromTableType]`) and
  nullable reference types. The class then doubles as the natural DTO for the rows bound into
  the TVP at runtime. The type name defaults to the class name (no `List` suffix is appended).

  ```csharp
  [TableType(Name = "DocumentAssignmentsList")]
  public class DocumentAssignment
  {
      [Key] public int DocumentId { get; set; }
      public int AssigneeId { get; set; }
  }
  ```

Store types resolve through the provider's type mapping (facets honored); explicit store types
override. Grants and memory-optimization are configured through the fluent builder (deployment
concerns stay out of attributes). Standalone definitions participate in the same global
name-uniqueness check as table-derived types.

**Guardrail**: standalone types exist for operation-specific shapes. They are NOT a backdoor to
hand-maintained alternates of table row images — that is the rejected `ForSave` pattern (see
Alternatives), with the same silent drift/truncation failure mode. If the shape is "this
table's writable columns", derive it from the table.

The platform's four canonical bulk shapes — `[IdList]` (`[Id] int`), `[BigIdList]`
(`[Id] bigint`), `[GuidList]` (`[Id] uniqueidentifier`), and `[StringList]`
(`[Id] nvarchar(450)`) — are plain classes in `Tellma.Core.Abstractions` (BCL DataAnnotations
only; that assembly stays EF-free), registered by each distribution's composition through the
class-derived route like any other standalone type: there is deliberately **one mechanism, no
special handling**, and the classes double as the TVP row DTOs.

When two contexts share one database (an advanced layout — the norm is one context per
database) and both need a bulk shape such as `IdList`, exactly one context owns it and the
others register it with `ExcludeFromMigrations()`: the shape stays in their model and
metadata API for runtime binding, but only the owner creates and sweeps the physical type.
Because the name is content-addressed, all of them compute the identical name and bind one
shared type. See §3 → Versioning → scoping for the ownership rule and the 53104 guard
against two contexts both trying to own it.

### 6. Metadata API

All configuration must be queryable from the EF model via public extension methods (the same
surface the dynamic SQL generator, the drop guard, and tests consume):

- `model.GetTableTypes()` → all generated types (logical **and physical** name + schema;
  the physical name is the content-hash-versioned deployed name per §3 → Versioning).
- Per entity type: type names/schema, included columns **in order** with store types and
  nullability, PK columns, rowversion inclusion, memory-optimized flag, grant principals.
- Runtime TVP binding (`SqlDataRecord`/`DataTable`) MUST be driven by this metadata API,
  addressing each type by the **physical name from the app's own model** (never a name
  discovered from the database), and never by hard-coded ordinals — a pack adding a column in a base class legitimately reorders
  the flattened leaf table. A Roslyn analyzer flags hard-coded ordinal binding. (*Deferred*:
  the analyzer ships with the save-pipeline spec, alongside the binding code it polices.)
- No annotations are needed for insert ordering across tables: the runtime save pipeline
  derives a static topological order from the model's existing FK metadata (public API).
  Schema-level FK cycles are a runtime/modeling concern (nullable edge + targeted UPDATE)
  and are out of scope for this project.

## Rules

1. **Prefer the public API; quarantine the rest.** Use public EF Core APIs wherever possible.
   Where the migrations pipeline forces use of internal/`.Internal`-namespace EF APIs (e.g.,
   composing the model differ), confine ALL such usage to a single thin adapter, pin the EF
   Core major version the project targets, and cover the adapter with tests that fail loudly
   on EF upgrades. Supporting multiple EF majors is a non-goal; the library tracks the EF
   version pinned by the Tellma solution.
2. **Comprehensive, non-flaky tests.** See the Testing plan below; it is part of the
   deliverable, not an afterthought.
3. **Self-contained, with a hard runtime/design boundary.** Both projects are internal
   libraries of Tellma Core but must remain self-contained: they reference only EF Core
   packages — never Tellma application projects — so they stay testable in isolation. The
   boundary is enforced mechanically: `Tellma.Core.EntityFrameworkCore` MUST NOT reference
   `Microsoft.EntityFrameworkCore.Design` (directly or transitively), and only the
   distribution's migrator project references `Tellma.Core.EntityFrameworkCore.Design`. The web server's
   published output must contain no Design-package assemblies (Roslyn, templating) — enforced
   by unit tests asserting the runtime library's transitive dependency closure is free of the
   Design tree (a framework-dependent publish ships exactly that closure); once a real web
   host exists, its pipeline additionally asserts the literal publish output.
4. **Design-time efficiency.** Migration/design-path code must not be unnecessarily
   inefficient: the finalizing convention makes one pass over the entity types, derived
   definitions are serialized once and diffed as strings, and parsed definitions are cached by
   content. Automated performance testing is out of scope.
5. **No persisted modules may reference generated UDTTs.** All consumers are dynamic SQL.
   Enforced in three layers: the drop-path dependency check (§3 — a hard THROW on
   explicitly authored drops, skip-and-surface in the sweep), a CI integration test
   asserting zero rows in the `sys.sql_expression_dependencies` query after applying all
   migrations to a fresh database (the hard gate), and a fast static tripwire that reflects over the migrations assembly,
   enumerates every `SqlOperation` across all migrations' `UpOperations`, and flags any
   generated type name appearing inside a `CREATE/ALTER PROCEDURE|FUNCTION` batch.

## Testing plan (required scope)

- **SQL generation (no database)**: golden-SQL assertions for Create/Drop/Cleanup
  against the migrations SQL generator, covering: every supported column type and facet,
  PK mirroring, column order, exclusions, rowversion on/off, memory-optimized on/off,
  grants, the idempotent create wrapper and extended-property stamps, the sweep's
  mark/clear/collect statements and its emission as the migration's trailing
  non-transactional command, both `CleanupTableTypes` authoring shapes (explicit keep-list
  and target-model-resolved), the drop-time dependency guard, idempotent-script output, and
  injection safety — a scope or grant principal containing `'`, `]`, or `--` yields valid,
  escaped/delimited SQL in the generated output (no command parameters; `migrations script`
  is a static file).
- **Differ**: model-pair tests asserting the exact operations emitted for each change class
  (opt-in, opt-out, add/remove/rename/retype/reorder column, facet change, config change,
  no-op) — creates plus the keep-list cleanup, never drops — and that reverse diffing (the
  `Down()` direction) emits the old version's create plus the old keep-list. Includes a
  pure type rename with an identical shape, which must register as a definition-set
  difference (pinning that the canonical JSON embeds the logical name and schema).
- **Versioning & sweep**: physical-name derivation is stable (same canonical JSON → same
  hash on every machine); idempotent create no-ops on re-run; an unstamped or
  partially-stamped type at the physical name (simulating an aborted memory-optimized create)
  is adopted and its stamps completed on re-run, never thrown; a planted same-named type
  with a different stamped hash THROWs 53103; the sweep's three rules (keep-list membership
  clears orphan marks, non-members get marked, marks older than the grace period are
  collected); a dependent-module orphan is skipped and surfaced, never thrown; `Down()`
  restores the old version and orphans the new one; full-chain replay on a fresh database
  ends with every current version present and the ancestors orphan-marked; the squash
  scenario (fresh `Initial` applied over a database carrying stale versions → the next sweep
  retires them); rename and opt-out retire through the same path.
- **Scoping & ownership**: two contexts sharing one database sweep only their own scope
  (neither collects the other's types); a shared type declared with `ExcludeFromMigrations()`
  in one context is bound (present in its metadata API) but neither created nor swept by it,
  and both contexts resolve the identical physical name; two contexts both *owning* the
  same-shaped type THROW 53104 on the second create; a content mismatch at a name still
  THROWs 53103 (kept distinct from 53104).
- **Derivation validations**: actionable finalizing-time errors for opt-ins the derivation
  cannot honor — complex/owned/`ToJson` columns on the mapped table, a TPH root whose
  derived types declare mapped scalar columns (a pure-discriminator hierarchy passes),
  shared-table fluent opt-ins, keyless entities, logical names exceeding the physical-name
  length budget (> 119 characters).
- **Snapshot round-trip**: model → snapshot code → compile → diff against the live model
  must be empty (the standard EF technique), proving annotations survive snapshots.
- **Scaffolding**: design-time tests asserting the C# emitted into migration files for the
  new operations compiles and round-trips.
- **Integration (containerized SQL Server)**: apply migrations to a fresh database; assert
  the types exist under their physical names with correct columns, order, PK, grants, and
  extended-property stamps; assert a definitional change creates the new version alongside
  the old and that the sweep orphan-marks and (past the grace period) collects stale
  versions; assert the dependency guard fires when a proc referencing a type is planted.
- **Seed-band test**: enumerate `IEntityType.GetSeedData()` across the model and assert all
  seeded key values fall inside the reserved band.
- **Dependency-boundary check** per Rule 3: assert `Tellma.Core.EntityFrameworkCore` has no
  assembly reference to `Microsoft.EntityFrameworkCore.Design`, that its transitive dependency
  closure (walked from the test app's `deps.json`) contains no Design-tree package, and that a
  Design-free host's output directory carries no Design-tree assembly. (The literal
  publish-output check moves to the real web host's pipeline once one exists.)
- **Internal-API adapter tests** pinning behavior per Rule 1.

## Out of scope (decided elsewhere, recorded for traceability)

- The **runtime ID allocator** (buffered per-table ranges over `sp_sequence_get_range`, with
  background prefetch and single-round-trip multi-sequence cold start via a dynamic batch —
  deliberately not a stored proc, per Rule 5). The allocator **self-heals from sequence
  desync** caused by out-of-band inserts: (a) at every range refill, the same round-trip
  compares the obtained range against `MAX([Id])` and discards/jumps past it via one
  oversized `sp_sequence_get_range` if behind (no DDL, no extra permissions); (b) on a bulk
  insert failing with a PK-constraint violation specifically, the pipeline flushes the
  table's buffered range, jumps the sequence past `MAX([Id])`, re-assigns IDs to the
  in-memory batch (rewiring intra-batch FKs), and retries exactly once; (c) every recovery
  event is logged/alerted, since it is evidence of out-of-band writes.
- The **bulk save pipeline** (per-table `INSERT … SELECT FROM @tvp` in static topological FK
  order; deletes in reverse order; self-referencing tables load in one statement since
  intra-statement FK checks see all inserted rows; cross-table schema cycles handled by a
  nullable edge + targeted UPDATE).
- The **deploy-time migrator**: a per-distribution console project (also the EF design-time
  target owning the `Migrations/` folder), run once per deployment from CI/CD (pipeline step,
  k8s Job, or equivalent — never per-instance at startup), executing migrate → versioned data
  seeds (tracked in a `__SeedHistory` table, transactional and idempotent) through the bulk
  pipeline — fanned out across the Catalog DB and every tenant DB with bounded parallelism,
  converging idempotently; partial failure blocks the deployment swap, and a re-run touches
  only the databases that are behind (see ARCHITECTURE.md → Migrations & seeding — the
  deploy-time migrator).
- HTTP-boundary request DTOs / binding allow-lists (mass-assignment protection).
- Legal/gapless document numbering (a domain concern; never derived from surrogate keys —
  sequences guarantee gaps).
- **Runtime definition-hash verification** before bulk save: compare the hash computed from
  the app's own model against the type's `DefinitionHash` stamp (§3 → Versioning) and
  refuse with an actionable error on mismatch — defense in depth that also catches
  out-of-band DDL. A save-pipeline concern; the stamp this spec deploys is its ready-made
  hook.
- An **out-of-band sweep entry point** (runtime project, no Design dependency: keep-list
  resolved from the app's compiled model, explicit grace, scoped) and the **post-swap exact
  cleanup** it would enable in the deploy-time migrator (collecting orphans immediately once
  the swap completes, instead of waiting out the grace period — the migrator knows
  deployment boundaries, so it could tighten retention to exactly N−1). Also the natural way
  to collect types stranded by a deliberate scope change. Deferred — added only if replay
  clutter, exact post-swap retention, or scope migration ever justifies it; the time-based
  in-migration sweep is correct without it.

## Alternatives considered and rejected

- **OPENJSON instead of TVPs**: zero DDL and no migration machinery, but slower for large
  payloads (LOB transfer + parse vs. binary TDS streaming), weaker type fidelity, and errors
  surface at parse time instead of bind time. Rejected given the performance design goal.
- **Unconditionally drop and recreate every UDTT in every migration**: dramatically simpler —
  no canonical-JSON contract, no snapshot round-trip, no differ extension; just regenerate
  everything from the current model each time. Rejected:
  - Migrations stop describing changes. Every migration carries every type, so review loses
    the one signal that matters most here: "this PR altered a TVP contract." A column reorder
    silently changes ordinal binding semantics — exactly the kind of change that must surface
    as a focused, reviewable diff, not drown in a full regeneration.
  - It turns every deployment into a type outage: during the drop→create window, in-flight
    TVP saves fail with "type not found" even when no definition changed, and the drop guard
    (§3) runs for every type on every deployment — one out-of-band dependent module would
    block all future deployments, not just the migration that actually touches that type.
  - Scripts and bundles grow O(types × migrations): an idempotent script replays every type's
    drop/create inside every migration's `IF NOT EXISTS` block.
  - Memory-optimized types would inject their non-transactional, atomicity-chunking commands
    (§3) into every migration, instead of only into the rare migration with a real
    definitional change.
  - The simplification is smaller than it looks: drop ordering, the drop guard, grants
    re-emission, and memory-optimized handling are all still needed — only the differ's
    string comparison is saved, and that comparison is the cheap part.
- **In-place recreate under a stable name** (`DropTableType` + `CreateTableType` of the same
  name in the same migration) — this design's own first iteration. Rejected for the
  deployment window: the database migrates before the app fleet swaps (and a slot-swap
  rollback puts the old app back on the new schema), and TVP binding is positional and
  name-blind — so an N−1 app binding a reshaped same-named type either fails at bind time
  (retype, column-count change) or, the disqualifying case, silently writes values into the
  wrong columns when same-typed columns swap positions. Content-hash versioned names
  (§3 → Versioning) close this by construction, and dissolve the in-place recreate's
  non-atomicity for memory-optimized types as a side effect. Nor does the milder repair
  rescue it: even paired with the runtime `DefinitionHash` stamp check (see Out of scope),
  stable names merely convert the silent corruption into an N−1 save *outage* during every
  type-touching deployment — versioning removes the outage, not just the corruption.
- **Append-only evolution policy as the deployment-window mitigation** (forbid
  reorders/retypes/removals; only tail-append nullable columns): doesn't work. TVP binding
  requires the client to send the type's full column list, so an N−1 app sending fewer
  columns than the new definition declares fails at bind time anyway (the TDS
  default-column escape requires the client to mark the very columns it cannot know about).
  It converts corruption into an outage, not into safety. Still sound as a *style*
  preference; just not a mechanism.
- **Schema-adaptive runtime binding** (the save pipeline reads the live type's shape from
  the catalog and reorders/pads its rows to match): handles reorders and appends without
  versioned names, but the shape cache must invalidate exactly at the deployment moment
  (the hard part), a renamed column silently degrades to sending NULL, and it puts catalog
  discovery on the hot save path. Strictly weaker guarantee than content-addressed names.
- **Separate `ForSave` classes as the UDTT source**: rejected. With all logic in C#, the
  persistence payload is the full row image, so a parallel class hierarchy is duplication
  with a silent drift/truncation failure mode; deriving from the table model eliminates that
  bug class by construction and removes model pollution, implicit graph traversal, and
  depth-encoded index columns from this project entirely.
- **Database-generated IDs (IDENTITY) + index columns**: rejected. App-assigned sequence IDs
  delete the `[Index]`/`[HeaderIndex]` machinery, MERGE…OUTPUT mapping, two-pass tree
  inserts, and per-payload dependency analysis.
- **App-generated GUIDs (UUIDv7) / Snowflake IDs**: viable for multi-master or offline
  scenarios; rejected for a single-database deployment due to key width (GUID) or node-ID
  coordination and clock handling (Snowflake) versus the simplicity of sequences.
- **`UseTellmaSqlServer()` wrapper**: rejected in favor of the additive `UseTableTypes()`
  extension (overload-mirroring maintenance trap).
