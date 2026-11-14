# Tellma.Core.EntityFrameworkCore

The runtime home of Tellma's EF Core extensions. It ships two features, each in its own namespace:
**table types** (`Tellma.Core.EntityFrameworkCore.TableTypes`), SQL Server user-defined table types
(UDTTs) as first-class citizens of EF Core migrations, and **lock escalation**
(`Tellma.Core.EntityFrameworkCore.LockEscalation`), a table's `LOCK_ESCALATION` option declared on
its entity type and kept by the same migrations. Future EF Core extensions get sibling namespaces.

## Why this exists

All Tellma persistence goes through a single bulk save path using table-valued parameters,
whose schemas are UDTTs (see [the spec](../../../docs/specs/0001-efcore-table-types.md)
and ARCHITECTURE.md → Data Layer). UDTTs are not a native EF concept; this library derives each
opted-in table's UDTT from the same entity classes that generate the table — a row image, no
separate DTO model — and creates/keeps it in sync through the same migrations pipeline.

## How it works

1. **Opt-in**: `optionsBuilder.UseSqlServer(...).UseTableTypes(sweepScope)` activates the
   extension ([TableTypesOptionsExtension](TableTypes/TableTypesOptionsExtension.cs)). The sweep
   scope is required (a stable string naming which types this context owns — no default, so a
   context rename never changes ownership). Tables opt in via
   `entity.HasTableType(name?, schema?)` or `[TableType]` on the entity class (inherited by
   leaf classes; fluent wins over attributes). Per-table knobs: column exclusions, rowversion
   exclusion, `MEMORY_OPTIMIZED = ON`, `GRANT EXECUTE` principals, and `ExcludeFromMigrations()`
   (declare a type for binding without this context owning it, when two contexts share a database)
   ([TableTypeBuilderExtensions](TableTypes/TableTypeBuilderExtensions.cs)).
2. **Derivation**: at model-finalizing time,
   [TableTypeFinalizingConvention](TableTypes/Conventions/TableTypeFinalizingConvention.cs)
   computes each type's full definition — included columns in the table's resolved order, store
   types, facets, nullability, mirrored PK — and serializes it as **canonical JSON** into one
   model annotation per type (`Tellma:TableTypeDefinition:<schema>.<name>`). Canonical JSON
   makes string equality equivalent to definition equality.
3. **Diffing**: the annotations round-trip through the model snapshot, so the differ
   ([TableTypeDiffer](TableTypes/TableTypeDiffer.cs), spliced in by the quarantined
   [adapter](TableTypes/Internal/EfCoreInternalsAdapter.cs)) compares them verbatim — never
   re-deriving from the snapshot side. In snapshot files each definition is rendered as a
   readable `HasTableTypeDefinition(...)` call (one line per column in PR diffs) whose replay
   rebuilds the annotation byte-for-byte. The differ emits **creates only** (each under a
   content-addressed physical name `<logical>_<hash8>`) plus one trailing
   `CleanupTableTypes` sweep carrying the keep-list — never drops. A definitional change is a
   new version created alongside the old one, which the sweep retires after a grace period; an
   N−1 app keeps binding the version it was compiled against (spec 0001 §3 → Versioning).
4. **SQL**: [TableTypesSqlServerMigrationsSqlGenerator](TableTypes/TableTypesSqlServerMigrationsSqlGenerator.cs)
   renders the idempotent `CREATE TYPE ... AS TABLE` (keyed on the physical name; completes the
   stamps of an aborted prior create, else throws 53103 on a content mismatch or 53104 on a
   foreign-scope conflict), the extended-property stamps (logical name, scope, definition hash),
   the In-Memory OLTP pre-flight (error 53101), the cleanup sweep (mark/clear/collect, with the
   dependency guard skipping-and-surfacing), the manual-drop dependency guard (error 53102,
   naming the offending modules), and `GRANT EXECUTE ON TYPE` with every version create. Values
   are escaped and identifiers delimited — no command parameters (the idempotent script is a
   static file).
5. **Metadata API** ([TableTypeModelExtensions](TableTypes/TableTypeModelExtensions.cs)):
   `model.GetTableTypes()`, `entityType.GetTableType()` — logical and physical (versioned) name,
   ordered columns with store types and facets, PK, grants. Runtime TVP binding MUST be driven by
   this API, addressing each type by its physical name from the app's own model, never hard-coded
   ordinals: column order is the contract.
6. **Standalone types** (spec 0001 §5), paired with no table, for operation-specific shapes
   (bulk state updates, bulk assignments): ad hoc via
   `modelBuilder.HasTableType("IdStateList", "dbo", t => t.Column<int>("Id")...)` or derived
   from a plain class via `modelBuilder.HasTableType<T>()` — the class then doubles as the TVP
   row DTO. The platform's canonical bulk shapes (`IdList`, `BigIdList`, `GuidList`,
   `StringList`) are plain classes in `Tellma.Core.Abstractions`, which the platform's tenant model
   registers through this same route — one mechanism, no special handling. A column gets a collation
   through `UseCollation(column, collation)` in the build action, whether the class or
   `Column(…)` added it — e.g. `t => t.UseCollation("Subject", "Latin1_General_100_BIN2")` for a
   key compared with a binary-collated table column, which SQL Server otherwise refuses (Msg 468).
   It applies after every column is added, so its position against `Column(…)` does not matter;
   the last call wins, also over `Column(…, collation)`; names match case-insensitively, and an
   unknown name fails the registration. Only `char`, `varchar`, `nchar` and `nvarchar` columns
   take a collation (any other store type, `json` included, fails model finalization). The
   collation is part of the definition, so adding, changing or removing one creates a new
   version; a type without one serializes exactly as before. The build action can also replace a
   column's store type and facets with `HasColumnType(column, storeType, …)` (applied before the
   collations, so a retyped column can take one; the last call wins, also over the class's
   `[Column(TypeName = …)]`) and drop a column with `ExcludeColumn(column)` (applied last; never a
   key column) — the way a model that stores enums by name, or marks exclusions with an attribute
   of its own, shapes a class-derived type. Both match names case-insensitively and fail the
   registration for an unknown one.

## Lock escalation

`entity.HasLockEscalation(LockEscalationMode.Disable)` (or `Table`, `Auto`) records the option as the
annotation `Tellma:LockEscalation` on the entity type
([LockEscalationExtensions](LockEscalation/LockEscalationExtensions.cs)); `GetLockEscalation()`
reads it back as the SQL Server keyword. It rides `UseTableTypes`: the extension replaces EF's
SQL Server annotation provider with a subclass (in the quarantined adapter) that carries the
annotation onto the table, so the differ surfaces it on `CreateTableOperation` and, when it changes,
on `AlterTableOperation`, and the snapshot round-trips it. The SQL generator then emits
`ALTER TABLE … SET (LOCK_ESCALATION = …);` as a statement of its own after the table is created,
and again whenever the option changes; removing the declaration restores the server default,
`TABLE`. A keyword other than `TABLE`, `AUTO` or `DISABLE` fails generation.

## Rules this project lives by

- **Never references `Microsoft.EntityFrameworkCore.Design`** (directly or transitively) and
  never references Tellma application projects. Enforced by unit tests three ways: assembly
  references, the transitive dependency closure (what a host's publish output ships), and a
  ground-truth scan of a Design-free app's output directory.
- **Internal EF APIs are quarantined** in a single file,
  [TableTypes/Internal/EfCoreInternalsAdapter.cs](TableTypes/Internal/EfCoreInternalsAdapter.cs)
  (the differ splice and the annotation provider subclass), pinned by tests that fail loudly on EF
  upgrades. The EF version is pinned centrally in
  `Directory.Packages.props`.
- **Admission rule**: code belongs here only if it extends EF Core's own surface (options
  extensions, migration operations, conventions, metadata/annotations) and is generic over any
  model. Runtime persistence (ID allocator, save pipeline) does not qualify.
- **Efficiency**: migration/design-path code must not be unnecessarily inefficient — the
  finalizing convention is one pass over the entity types, definitions are serialized once and
  compared as strings, and parsed definitions are cached by content.

The design-time companion (`dotnet ef` scaffolding of the operations) lives in
[Tellma.Core.EntityFrameworkCore.Design](../Tellma.Core.EntityFrameworkCore.Design/README.md);
this assembly stays clean of it.
