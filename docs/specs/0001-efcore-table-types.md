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
  (no mirroring of its overloads, no coupling to its release cadence).
- **0 or 1 UDTT per table.** A table opts in via the fluent API
  (`entity.HasTableType(name?, schema?)`) or an attribute on the entity class
  (`[TableType(Name = ..., Schema = ...)]`). Default name/schema follow a single
  convention — the table's own schema plus a `List` suffix: `[<TableSchema>].[<TableName>List]`,
  e.g. `[gl].[InvoicesList]` — overridable per entity.
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
    `GRANT EXECUTE ON TYPE::<type> TO <principal>` after every create/recreate.

### 2. UDTT derivation rules

The UDTT is a **derived row image** of the table:

- **Included columns** = all insertable/updatable table columns, minus the exclusion list.
  Computed columns are always excluded. Column names, store types, max length, precision,
  scale, nullability, and collation are taken from the relational model EF already built for
  the table — never re-declared.
- **Normalization**: the type never carries IDENTITY (moot given sequences, but enforce it),
  defaults, FK constraints, or named constraints.
- **Primary key**: the type's PK mirrors the table's PK columns. (IDs are app-assigned and
  always present, for inserts and updates alike; the PK enforces in-batch uniqueness and
  aids join plans.)
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

### 3. Migrations pipeline behavior

- New migration operations: `CreateTableTypeOperation`, `DropTableTypeOperation`, plus
  `MigrationBuilder` extension methods (`CreateTableType`, `DropTableType`) so the
  operations can also be authored manually.
- **Diffing**: table-type definitions are stored as model annotations (the way sequences
  are) and therefore round-trip through the model snapshot. The differ emits operations when:
  - a table opts in / out of having a type (Create / Drop);
  - any aspect of the derived definition changes — column added/removed/renamed/retyped,
    facet change, order change, PK change, exclusion-list change, memory-optimized or grant
    config change.
- **There is no ALTER for table types in SQL Server.** Every definitional change is emitted
  as `DropTableType` + `CreateTableType` within the same migration (transactional),
  ordered deterministically relative to the corresponding table operations.
- **Drop safety**: the generated drop SQL begins with a pre-flight check against
  `sys.sql_expression_dependencies` (`referenced_class = 6`, i.e. TYPE) and `THROW`s with the
  list of dependent modules by name if any persisted module references the type — converting
  a cryptic error 3732 into an actionable failure. (Per the architecture, no such dependents
  may exist; this guard enforces it at the moment it matters.)
- After every (re)create, grants from §1 are re-emitted (grants do not survive a drop).
- **Scaffolding**: `dotnet ef migrations add` must scaffold the new operations into migration
  files. Implement via the design-time C# operation generator in the `.Design` project,
  discovered through the `[assembly: DesignTimeServicesReference]` that the Design package's
  MSBuild targets inject into the consuming migrator assembly (see the `.Design` bullet above)
  so that referencing the libraries is sufficient — no manual wiring in consuming projects.
- The operations must work with `dotnet ef migrations script` (including `--idempotent`) and
  migration bundles.

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
their definition source differs.

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
  class's public read-write properties in declaration order, honoring the standard annotations
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

The library itself ships four predefined standalone types for bulk delete / bulk lookup —
`[IdList]` (`[Id] int`), `[BigIdList]` (`[Id] bigint`), `[GuidList]` (`[Id] uniqueidentifier`),
and `[StringList]` (`[Id] nvarchar(450)`) — opted into via `HasBuiltInTableTypes(...)`.

### 6. Metadata API

All configuration must be queryable from the EF model via public extension methods (the same
surface the dynamic SQL generator, the drop guard, and tests consume):

- `model.GetTableTypes()` → all generated types (name + schema).
- Per entity type: type name/schema, included columns **in order** with store types and
  nullability, PK columns, rowversion inclusion, memory-optimized flag, grant principals.
- Runtime TVP binding (`SqlDataRecord`/`DataTable`) MUST be driven by this metadata API,
  never by hard-coded ordinals — a pack adding a column in a base class legitimately reorders
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
   published output must contain no Design-package assemblies (Roslyn, templating); a CI
   check asserts this.
4. **Design-time efficiency.** Migration/design-path code must not be unnecessarily
   inefficient: the finalizing convention makes one pass over the entity types, derived
   definitions are serialized once and diffed as strings, and parsed definitions are cached by
   content. Automated performance testing is out of scope.
5. **No persisted modules may reference generated UDTTs.** All consumers are dynamic SQL.
   Enforced in three layers: the drop-time guard (§3), a CI integration test asserting zero
   rows in the `sys.sql_expression_dependencies` query after applying all migrations to a
   fresh database, and a fast static tripwire that reflects over the migrations assembly,
   enumerates every `SqlOperation` across all migrations' `UpOperations`, and flags any
   generated type name appearing inside a `CREATE/ALTER PROCEDURE|FUNCTION` batch.

## Testing plan (required scope)

- **SQL generation (no database)**: golden-SQL assertions for Create/Drop
  against the migrations SQL generator, covering: every supported column type and facet,
  PK mirroring, column order, exclusions, rowversion on/off, memory-optimized on/off,
  grants, the drop-time dependency guard, idempotent-script output.
- **Differ**: model-pair tests asserting the exact operations emitted for each change class
  (opt-in, opt-out, add/remove/rename/retype/reorder column, facet change, config change,
  no-op).
- **Snapshot round-trip**: model → snapshot code → compile → diff against the live model
  must be empty (the standard EF technique), proving annotations survive snapshots.
- **Scaffolding**: design-time tests asserting the C# emitted into migration files for the
  new operations compiles and round-trips.
- **Integration (containerized SQL Server)**: apply migrations to a fresh database; assert
  the types exist with correct columns, order, PK, and grants; assert drop/recreate works;
  assert the dependency guard fires when a proc referencing a type is planted.
- **Seed-band test**: enumerate `IEntityType.GetSeedData()` across the model and assert all
  seeded key values fall inside the reserved band.
- **Dependency-boundary check** per Rule 3: assert `Tellma.Core.EntityFrameworkCore` has no reference
  to `Microsoft.EntityFrameworkCore.Design`, and that a representative publish of the web
  server contains no Design-package assemblies. (Until a web host exists — Phase 1 — a minimal
  host test asset referencing only the runtime library stands in for the publish check.)
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

## Alternatives considered and rejected

- **OPENJSON instead of TVPs**: zero DDL and no migration machinery, but slower for large
  payloads (LOB transfer + parse vs. binary TDS streaming), weaker type fidelity, and errors
  surface at parse time instead of bind time. Rejected given the performance design goal.
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
