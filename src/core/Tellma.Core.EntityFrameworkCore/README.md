# Tellma.Core.EntityFrameworkCore

The runtime home of Tellma's EF Core extensions. Today it ships one feature, **table types**
(namespace `Tellma.Core.EntityFrameworkCore.TableTypes`): SQL Server user-defined table types
(UDTTs) as first-class citizens of EF Core migrations. Future EF Core extensions get sibling
namespaces.

## Why this exists

All Tellma persistence goes through a single bulk save path using table-valued parameters,
whose schemas are UDTTs (see [the spec](../../../docs/specs/0001-efcore-table-types.md)
and ARCHITECTURE.md → Data Layer). UDTTs are not a native EF concept; this library derives each
opted-in table's UDTT from the same entity classes that generate the table — a row image, no
separate DTO model — and creates/keeps it in sync through the same migrations pipeline.

## How it works

1. **Opt-in**: `optionsBuilder.UseSqlServer(...).UseTableTypes()` activates the extension
   ([TableTypesOptionsExtension](TableTypes/TableTypesOptionsExtension.cs)). Tables opt in via
   `entity.HasTableType(name?, schema?)` or `[TableType]` on the entity class (inherited by
   leaf classes; fluent wins over attributes). Per-table knobs: column exclusions, rowversion
   exclusion, `MEMORY_OPTIMIZED = ON`, and `GRANT EXECUTE` principals
   ([TableTypeBuilderExtensions](TableTypes/TableTypeBuilderExtensions.cs)).
2. **Derivation**: at model-finalizing time,
   [TableTypeFinalizingConvention](TableTypes/Conventions/TableTypeFinalizingConvention.cs)
   computes each type's full definition — included columns in the table's resolved order, store
   types, facets, nullability, mirrored PK — and serializes it as **canonical JSON** into one
   model annotation per type (`Tellma:TableTypeDefinition:<schema>.<name>`). Canonical JSON
   makes string equality equivalent to definition equality.
3. **Diffing**: the annotations round-trip through the model snapshot untouched, so the differ
   ([TableTypeDiffer](TableTypes/TableTypeDiffer.cs), spliced in by the quarantined
   [adapter](TableTypes/Internal/EfCoreInternalsAdapter.cs)) compares them verbatim — never
   re-deriving from the snapshot side. SQL Server has no `ALTER TYPE`, so every definitional
   change emits drop + create within the same migration.
4. **SQL**: [TableTypesSqlServerMigrationsSqlGenerator](TableTypes/TableTypesSqlServerMigrationsSqlGenerator.cs)
   renders `CREATE TYPE ... AS TABLE`, the In-Memory OLTP pre-flight (error 53101), the
   drop-time dependency guard over `sys.sql_expression_dependencies` (error 53102, naming the
   offending modules), and `GRANT EXECUTE ON TYPE` after every (re)create.
5. **Metadata API** ([TableTypeModelExtensions](TableTypes/TableTypeModelExtensions.cs)):
   `model.GetTableTypes()`, `entityType.GetTableType()` — ordered columns with store types and
   facets, PK, grants. Runtime TVP binding MUST be driven by this API, never hard-coded
   ordinals: column order is the contract.
6. **Built-in primitive types** (`modelBuilder.HasBuiltInTableTypes(...)`): `[IdList]`,
   `[BigIdList]`, `[GuidList]`, `[StringList]` for bulk delete / bulk lookup, outside the
   0-or-1-per-table rule.

## Rules this project lives by

- **Never references `Microsoft.EntityFrameworkCore.Design`** (directly or transitively) and
  never references Tellma application projects. Enforced by tests and a CI publish check.
- **Internal EF APIs are quarantined** in a single file,
  [TableTypes/Internal/EfCoreInternalsAdapter.cs](TableTypes/Internal/EfCoreInternalsAdapter.cs),
  pinned by tests that fail loudly on EF upgrades. The EF version is pinned centrally in
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
