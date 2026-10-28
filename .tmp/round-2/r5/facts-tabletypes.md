# D-5 facts: column collations of class-derived table types

Verified facts for decision D-5 (amend spec 0001 so a class-derived standalone table type can
declare a column collation). Every fact is marked [Verified: source] or [Unverified].

## Sources and probes

- **Spec 0001** — `docs/specs/0001-efcore-table-types.md` (frozen), read in full.
- **Library** — `src/core/Tellma.Core.EntityFrameworkCore/` (runtime) and
  `src/core/Tellma.Core.EntityFrameworkCore.Design/`, working tree of branch `specs-backup`.
  Paths below drop the `src/core/Tellma.Core.EntityFrameworkCore/TableTypes/` prefix unless they
  say otherwise; a test path such as `Standalone/…Tests.cs` is relative to its test project.
- **Tests** — `test/core/Tellma.Core.EntityFrameworkCore.Tests`, `.Design.Tests`,
  `.IntegrationTests`, and the migrations host `test/core/assets/Tellma.Core.EntityFrameworkCore.MigrationsHost`.
- **EF Core** — `C:\Users\ahmad\workspace\efcore`, branch `release/10.0`, commit `9d1b7959357`
  (`v10.0.9-58`), the checkout r4 used; the platform pins EF Core 10.0.9.
- **BCL** — reference pack `Microsoft.NETCore.App.Ref/10.0.12`, `System.ComponentModel.Annotations.xml`.
- **Probe P9** — `tt-coll.sql` in this session's scratchpad
  (`C:\Users\ahmad\AppData\Local\Temp\claude\C--Users-ahmad-workspace-tellma-tellma-platform\ab119a81-92e3-4ada-8784-ec682d808960\scratchpad\`),
  run on LocalDB 15.0.4382.1 (`MSSQLLocalDB`, SQL Server 2019 Express) in a throwaway database
  created `COLLATE Latin1_General_100_CI_AS` and dropped afterwards.

## 1. Spec 0001 on standalone table types

### 1.1 Two definition sources

- **Table-derived types** derive from an entity's relational mapping; column names, store types,
  max length, precision, scale, nullability **and collation** "are taken from the relational
  model EF already built for the table — never re-declared", except a JSON column's store type and
  collation. [Verified: 0001 §2, lines 133-136]
- **Standalone types** have no table and "flow through the exact same annotations, differ,
  operations, SQL generation and metadata API"; only the definition source differs, versioning
  included. [Verified: 0001 §5, lines 555-561]
- **Two standalone routes**: ad-hoc fluent (`modelBuilder.HasTableType("IdStateList", schema,
  type => type.Column<int>("Id")…)`) and class-derived (a plain class, not an entity,
  registered with `modelBuilder.HasTableType<T>()`). [Verified: 0001 §5, lines 563-586]
- **Class-derived columns** come from public read-write properties in reflection metadata order,
  pinned by the snapshot. [Verified: 0001 §5, lines 575-581]
- **The four bulk shapes** (`IdList`, `BigIdList`, `GuidList`, `StringList`) are plain classes in
  `Tellma.Core.Abstractions` with "BCL DataAnnotations only; that assembly stays EF-free",
  registered through the class-derived route. [Verified: 0001 §5, lines 607-612]

### 1.2 What §5 honours, and how

- **Honoured on a class**: `[Key]` (composite too), `[MaxLength]`/`[StringLength]`, `[Unicode]`,
  `[Precision]`, `[Column(TypeName = ...)]`, `[Required]`, `[NotMapped]`,
  `[ExcludeFromTableType]`, and nullable reference types. [Verified: 0001 §5, lines 582-585]
- **Three kinds of source**: BCL DataAnnotations (`Key`, `MaxLength`, `StringLength`, `Required`,
  `Column`, `NotMapped`), EF Core's own attributes (`Unicode`, `Precision`, from
  `Microsoft.EntityFrameworkCore.Abstractions`), and the library's own `[ExcludeFromTableType]`.
  [Verified: `TableTypesModelBuilderExtensions.cs:6-8`, `:166-199`; efcore
  `src/EFCore.Abstractions/UnicodeAttribute.cs:12-13`, `PrecisionAttribute.cs`]
- **Collation is not in the list.** Spec 0001 never mentions a collation for standalone columns;
  its only collation text is §2's table-derived rule and the JSON wire form. [Verified: grep
  `collat` in spec 0001: lines 111, 134-136, 153-158, 720 only]
- **Deployment knobs stay fluent**: "Grants and memory-optimization are configured through the
  fluent builder (deployment concerns stay out of attributes)". Store types resolve through the
  provider's type mapping with facets honoured; explicit store types override. [Verified: 0001 §5,
  lines 597-599]
- **Fluent beats attribute** everywhere spec 0001 offers both: "Fluent configuration always takes
  precedence over attributes". [Verified: 0001 §1, lines 88-92]

### 1.3 How a change migrates

- **Creates only.** The differ emits a `CreateTableTypeOperation` for every definition whose
  canonical JSON has no exact match on the source side, including a "facet change", plus one
  `CleanupTableTypesOperation` keep-list sweep. [Verified: 0001 §3, lines 257-269]
- **New physical version, old one swept.** The physical name is `<LogicalName>_<hash8>` of the
  canonical JSON; a change creates the new version beside the old one, and the sweep orphan-marks
  and later drops the old one (48 h grace). [Verified: 0001 §3 Versioning, lines 392-399, 434-453]
- **Nulls omitted = forward compatible.** A new optional knob serializes only when set, so
  upgrading the library never changes existing definitions' JSON. [Verified: 0001 §3, lines
  351-371]

### 1.4 Test layout

- **Spec 0001's Testing plan names suites by kind, not paths**: SQL generation (golden SQL,
  "every supported column type and facet"), differ ("facet change"), versioning and sweep,
  scoping, derivation validations (actionable finalizing-time errors), JSON, snapshot round-trip,
  scaffolding, integration on SQL Server, boundary, internal-API pins. [Verified: 0001 Testing
  plan, lines 675-742]
- **The code's suites**:
  - unit `test/core/Tellma.Core.EntityFrameworkCore.Tests`, folders `Standalone/`, `Diffing/`,
    `SqlGeneration/`, `Metadata/`, `Conventions/`, `Versioning/`, `Boundary/`, `Internal/`;
  - design `test/core/Tellma.Core.EntityFrameworkCore.Design.Tests` (`Scaffolding/OperationScaffoldingTests.cs`,
    `Scaffolding/SnapshotRoundTripTests.cs`);
  - integration `test/core/Tellma.Core.EntityFrameworkCore.IntegrationTests` (`ApplyMigrationsTests.cs`,
    `DropGuardTests.cs`, `MemoryOptimizedTests.cs`) over the migrations host, which registers the
    four bulk shapes and one class-derived `DocumentState` and ships a scaffolded `Initial`
    migration. [Verified: file listing; `MigrationsHostContext.cs:55-62`;
    `Migrations/20260617015412_Initial.cs`]

## 2. The library code

### 2.1 The class-derived route never sets a collation

- `HasTableType(Type clrType, name, schema, buildAction)` builds a `TableTypeBuilder`, adds the
  class's columns, **then** runs `buildAction`, then builds. [Verified:
  `TableTypesModelBuilderExtensions.cs:76-98`, order at `:86-88`]
- `AddClassColumns` reads `[NotMapped]`, `[ExcludeFromTableType]`, NRT/`Nullable<T>`, `[Required]`,
  `[MaxLength]`/`[StringLength]`, `[Precision]`, `[Column(TypeName)]`, `[Unicode]` and `[Key]`,
  and creates each `StandaloneColumnConfiguration` **without** `Collation` or `IsFixedLength`.
  [Verified: `TableTypesModelBuilderExtensions.cs:145-205`; the initializer at `:187-197`]
- `[Column(TypeName)]` becomes `StoreType` verbatim. [Verified: `TableTypesModelBuilderExtensions.cs:191`]
- Attributes are matched by exact type (`GetCustomAttribute<T>(inherit: true)`), never by name.
  [Verified: `TableTypesModelBuilderExtensions.cs:166-199`]

### 2.2 The fluent builder offers a collation only on columns it adds

- `Column<T>(name, nullable, maxLength, precision, scale, unicode, fixedLength, collation)` and
  `Column(name, storeType, nullable, maxLength, precision, scale, collation)` set
  `StandaloneColumnConfiguration.Collation`. [Verified: `TableTypeBuilder.cs:41-67` (`:64`),
  `:78-101` (`:98`)]
- **No member configures an existing column.** The public surface is `Column<T>`, `Column`,
  `HasKey`, `HasGrants`, `IsMemoryOptimized`, `ExcludeFromMigrations`; `AddColumn` and
  `AddDerivedKeyColumn` are internal to the class route. [Verified: `TableTypeBuilder.cs:41-174`]
- **Re-adding a class-derived column fails**: a second `Column` with the same name trips the
  finalizing convention's duplicate check, case-insensitive. [Verified:
  `Conventions/TableTypeFinalizingConvention.cs:711-715`]
- **Precedent for a builder override of a class-derived fact**: `HasKey` replaces the
  attribute-derived key. [Verified: `TableTypeBuilder.cs:109-116`; test
  `Standalone/StandaloneTableTypesTests.cs:123-124` `Builder_action_can_override_the_attribute_derived_key`]

### 2.3 From configuration to definition

- `StandaloneColumnConfiguration.Collation` is a `string?` already in the record. [Verified:
  `StandaloneTableTypeConfiguration.cs:81-82`]
- The finalizing convention resolves the store type (explicit, else CLR type + facets through the
  type mapping) and copies `column.Collation` into `TableTypeColumnDefinition.Collation`. It does
  not validate the name or the store type's kind. [Verified:
  `Conventions/TableTypeFinalizingConvention.cs:747-788`, `:773-774`, `:784`]
- **A JSON column's collation is replaced** by the wire form's (`Latin1_General_100_BIN2_UTF8`
  on-disk, none when memory-optimized), on both routes. [Verified:
  `Conventions/TableTypeFinalizingConvention.cs:515-540`]

### 2.4 Entity-derived types already carry the source column's collation

- `DeriveColumn` passes `property.GetCollation(storeObject)` through `NormalizeJsonColumn` into
  `Collation`. [Verified: `Conventions/TableTypeFinalizingConvention.cs:424-425`, `:437`]
- A unit test pins it: an entity property with `UseCollation("Latin1_General_100_CI_AS")` yields
  a type column whose `Collation` is that name. [Verified:
  `test/core/Tellma.Core.EntityFrameworkCore.Tests/Metadata/TableTypeModelExtensionsTests.cs:66-114`
  (`:79`, `:111-112`), `Columns_carry_store_types_facets_and_nullability`]
- **Only an explicit property collation flows.** `GetCollation(storeObject)` reads the property's
  `Relational:Collation` annotation, then the shared-store-object root property's; it never reads
  the model's `UseCollation`. [Verified: efcore
  `src/EFCore.Relational/Extensions/RelationalPropertyExtensions.cs:1376-1387`]

### 2.5 The canonical JSON and the differ

- `TableTypeColumnDefinition.Collation` is part of the definition. [Verified:
  `TableTypeColumnDefinition.cs:54-55`]
- The canonical JSON is camelCase, declaration order, `WhenWritingNull`: a column without a
  collation serializes no `collation` key. [Verified: `Json/TableTypeJson.cs:80-82`]
- The differ compares definition JSON strings and emits a create when they differ. [Verified:
  `TableTypeDiffer.cs:76-78`]
- So adding, changing or removing a column's collation creates a new physical version, and a type
  without a collation keeps its hash. [Verified: the three facts above]
- **Case matters to the hash, not to SQL Server.** `latin1_general_100_bin2` and
  `Latin1_General_100_BIN2` hash differently, while SQL Server stores the canonical spelling
  (§4). [Verified: `TableTypeDiffer.cs:76`; probe P9 case 8]

### 2.6 The SQL generator

- `CREATE TYPE … AS TABLE (` writes each column as delimited name, store type, then
  ` COLLATE <collation>` when set, then `NULL`/`NOT NULL`. Store type and collation are emitted
  verbatim, "the same trust boundary as HasColumnType()/UseCollation()". [Verified:
  `TableTypesSqlServerMigrationsSqlGenerator.cs:360-379` (comment `:368-370`, `COLLATE` `:376-379`)]
- A golden test pins the output `[Name] nvarchar(100) COLLATE Latin1_General_100_CI_AS NULL`.
  [Verified: `test/core/Tellma.Core.EntityFrameworkCore.Tests/SqlGeneration/TableTypeSqlGenerationTests.cs:99`, `:120`, `:127`]

### 2.7 Design time already carries a collation

- The snapshot generator renders `collation: "…"` in `HasTableTypeDefinition(...)`, and the
  operation generator renders `Collation = "…"` in scaffolded creates. [Verified:
  `src/core/Tellma.Core.EntityFrameworkCore.Design/TableTypesCSharpSnapshotGenerator.cs:138-141`;
  `TableTypesCSharpMigrationOperationGenerator.cs:219-222`; `TableTypeDefinitionBuilder.cs:53-80`]
- The raw standalone configuration annotation is filtered from snapshots; only the definition
  reaches them. [Verified: `src/core/Tellma.Core.EntityFrameworkCore.Design/TableTypesSqlServerAnnotationCodeGenerator.cs:51-57`]
- Scaffolding tests cover a collated column; the snapshot round-trip test model has none.
  [Verified: `Scaffolding/OperationScaffoldingTests.cs:48`, `:108`; grep of
  `Scaffolding/SnapshotRoundTripTests.cs`]
- The integration suite creates no collated type column. [Verified: grep of
  `Tellma.Core.EntityFrameworkCore.IntegrationTests` and the migrations host]

## 3. EF Core 10 and the BCL

- **No collation attribute exists in EF Core 10.** `EFCore.Abstractions` ships `BackingField`,
  `Comment`, `DbFunction`, `DeleteBehavior`, `EntityTypeConfiguration`, `Index`, `Keyless`,
  `Owned`, `Precision`, `PrimaryKey` and `Unicode` attributes. No class in `src/` matches
  `class *Collation*Attribute`. [Verified: efcore `src/EFCore.Abstractions/` listing; grep]
- **No convention reads one.** The attribute conventions are the `*AttributeConvention` classes in
  `src/EFCore/Metadata/Conventions/` (backing field, complex type, concurrency check, database
  generated, delete behavior, foreign key, index, inverse property, key, keyless, max length,
  not mapped, owned, precision, required, string length, timestamp, unicode, …) and
  `src/EFCore.Relational/Metadata/Conventions/` (column, column comment, db function, JSON
  property name, table, table comment). None mentions `Collation`; the SQL Server provider has no
  attribute convention. [Verified: efcore directory listings; grep `Collation` in both folders and
  `AttributeConvention` in `src/EFCore.SqlServer`]
- **`[Column]` carries name, order and type name only.** EF's convention maps exactly those.
  [Verified: efcore `src/EFCore.Relational/Metadata/Conventions/RelationalColumnAttributeConvention.cs:33-52`;
  BCL `ColumnAttribute` members `Name`, `Order`, `TypeName` in `System.ComponentModel.Annotations.xml`]
- **The BCL has no collation attribute** in `System.ComponentModel.DataAnnotations[.Schema]`.
  [Verified: the 38 attribute types listed in `System.ComponentModel.Annotations.xml`, 0 matches
  for `collation`]
- **Collation is fluent-only in EF**: `PropertyBuilder.UseCollation(string?)` and
  `ModelBuilder.UseCollation(string)`. [Verified: efcore
  `src/EFCore.Relational/Extensions/RelationalPropertyBuilderExtensions.cs:921`, `:940`, `:959`;
  `RelationalModelBuilderExtensions.cs:514`, `:538`]
- **EF validates no collation name** and writes it verbatim after the column type. [Verified:
  efcore `RelationalPropertyExtensions.cs:1394-1410`; `src/EFCore.Relational/Migrations/MigrationsSqlGenerator.cs:1366-1371`]

## 4. SQL Server behaviour (probe P9)

| Case | Statement | Result |
|---|---|---|
| 1 | `CREATE TYPE … ([Subject] varchar(255) COLLATE Latin1_General_100_BINX …)` | Msg 448 "Invalid collation 'Latin1_General_100_BINX'." |
| 2 | `… ([Id] int COLLATE Latin1_General_100_BIN2 …)` | Msg 447 "Expression type int is invalid for COLLATE clause." |
| 6 | `… ([At] datetimeoffset(3) COLLATE …)` | Msg 447, same text for `datetimeoffset` |
| 3, 7 | `COLLATE Latin1_General_100_BIN2` on `varchar(255)`, `nvarchar(max)`, `char(22)` | created; `sys.columns.collation_name` reads `Latin1_General_100_BIN2`; an uncollated column reads the database's `Latin1_General_100_CI_AS` |
| 4 | a variable of the type, uncollated column `=` a `Latin1_General_100_BIN2` table column | Msg 468 "Cannot resolve the collation conflict …" |
| 5 | the type's `BIN2` column `=` the `BIN2` table column | compiles and runs |
| 8 | `COLLATE latin1_general_100_bin2` | created; stored as `Latin1_General_100_BIN2` |

- Each error fails the batch at compile time, so a bad name or a collated non-character column
  fails the migration's `CREATE TYPE`. [Verified: probe P9; the failing batches' `PRINT` never ran]
- r4 already showed the TVP case of row 4 (P5) and that an uncollated type column takes the
  database collation fixed at `CREATE TYPE`. [Verified: `.tmp/round-2/r4/facts-collation.md` §3.2]
- Memory-optimized types with a `BIN2` column were not probed; no platform type is
  memory-optimized. [Unverified for the engine; Verified for the specs: grep `memory-optimized`
  in specs 0010–0035, no match]

## 5. The platform's consumers (specs 0010–0035)

### 5.1 Explicitly collated table columns

Every explicit collation in specs 0010–0035 is `Latin1_General_100_BIN2`:

- `catalog.TenantMemberships.Subject` [Verified: `0011-distribution-host-and-multitenancy.md:1923`]
- `catalog.Sessions.Key`, `.Handle`, `.Subject` [Verified: `0011-…:1937-1939`]
- `core.Users.Subject` [Verified: `0014-users-roles-and-permissions.md:305`]

[Verified: grep `COLLATE|BIN2|UseCollation|collation:` in `docs/specs/001*.md 002*.md 003*.md`]

### 5.2 Standalone types compared with them

| Type (class) | Column | Compared with | Where |
|---|---|---|---|
| `[catalog].[TenantMembershipList]` (`TenantMembershipRecord`) | `Subject varchar(255)` | `catalog.TenantMemberships.Subject`: `r.[Subject] = m.[Subject]` in the upsert and reconciliation | sketch `0011-…:844-849` (collation as a comment, `:847`); table `:1928-1931`; statements `:1998-2006` |
| `UserInvitationOutcomeList` (`UserInvitationOutcome`) | `Subject varchar(255)` | `core.Users.Subject`: `u.[Subject] = o.[Subject]`, `u.[Subject] <> o.[Subject]` | sketch `0018-core-and-gl-reference-stacks.md:401-408` (comment `:405`); statement `:520-533` |

[Verified: the cited lines; spec 0012 states the rule and names these two at
`0012-data-access-layer.md:780-783`]

- **No other standalone type meets an explicitly collated column.** The others with string
  columns are `StringList`, `VersionTagList`, `UserPreferenceList`, the five job lists, the two
  notification lists and the two outbox lists; they compare with `core.VersionTags`,
  `core.UserPreferences`, `core.Jobs`, `core.Notifications`, `core.OutgoingEmails` and natural-key
  columns such as `Code`, none collated. `catalog.Sessions` statements use scalar parameters only.
  [Verified: `0012-…:763-774`; `0013-…:296-300`, `:415-423`, `:540-551`; `0014-…:455-462`;
  `0019-…:1193-1197`; `0020-…:344-358`, `:947-955`; `0021-…:472-486`; `0022-…:180-185`;
  `0011-…:2008-2024`]
- **No `DECLARE @… TABLE` in specs 0010–0035 has a string column compared with a collated
  column.** [Verified: grep `DECLARE @[A-Za-z_{}]+ TABLE`: hits in 0013, 0020, 0021, 0028, all
  integer, `Resource varchar(128)` or ordinal columns]

### 5.3 Both target classes live in EF-free `Tellma.Core.Abstractions`

- `TenantMembershipRecord` is in `Tellma.Core.Abstractions.Tenancy`; `ITenantMembershipDirectory.RecordAsync`
  takes it, so it cannot move to a runtime assembly. [Verified: `0011-…:840-855`;
  `0012-…:759-761`]
- `UserInvitationOutcome` is in `Tellma.Core.Abstractions.Access`. [Verified: `0018-…:370`, block
  from `:348`, class at `:401-402`]
- `Tellma.Core.Abstractions` references nothing from EF Core; its one non-BCL edge is
  `Tellma.Core.Queryex`. Today its csproj has no references. [Verified: `0012-…:110`;
  `src/core/Tellma.Core.Abstractions/Tellma.Core.Abstractions.csproj`]
- **Consequence**: a collation attribute declared in `Tellma.Core.EntityFrameworkCore` cannot be
  placed on either class. Nor can EF's `[Unicode(false)]`, so their `varchar(255)` needs
  `[Column(TypeName = "varchar(255)")]` (BCL) on the class route. [Verified: the three facts above
  and §2.1]
- **The platform's own pattern for EF-free annotations**: spec 0012 declares its own
  `TableTypeAttribute` and `ExcludeFromTableTypeAttribute` in `Tellma.Core.Abstractions.Entities`,
  and `TableTypeConvention` maps them "onto spec 0001's fluent configuration, so the attributes
  live in `Tellma.Core.Abstractions` without an EF edge". Standalone classes are "mapped by
  `TableTypeConvention` exactly as an entity's attribute is". [Verified: `0012-…:169`, `:280-281`,
  `:313`, `:756-758`] `UserInvitationOutcome`'s `[TableType("UserInvitationOutcomeList")]` takes a
  positional name, which only spec 0012's attribute can. The library's `TableTypeAttribute` has
  `Name`/`Schema` properties and no constructor argument. [Verified: `0018-…:401`;
  `TableTypeAttribute.cs:24-32`]

### 5.4 Entity-derived types with a collated source column

- **`core.Users`** is `[TableType]` (UDTT `UsersList`) and its `Subject` is `BIN2`, so
  `UsersList.Subject` carries `COLLATE Latin1_General_100_BIN2` through §2.4, provided the EF
  property is configured with `UseCollation`. [Verified: `0014-…:269`, `:298`, `:305`; §2.4]
  Spec 0014 places "the EF configuration of the six tables" in the runtime but does not name the
  call. [Verified: `0014-…:105`]
- `catalog.TenantMemberships` and `catalog.Sessions` have no entity-derived type: §7.3 names only
  the standalone `TenantMembershipList`, §7.4 none. [Verified: `0011-…:1919-1945`]
- **No amendment is needed for entity-derived types**: spec 0001 §2 and the code already copy the
  column's collation. [Verified: §1.1, §2.4]

## 6. The mechanism

### 6.1 Options

- **A. A library attribute honoured by the class route** (for example
  `[TableTypeCollation("Latin1_General_100_BIN2")]`, `Tellma.Core.EntityFrameworkCore.TableTypes`,
  package `Tellma.Core.EntityFrameworkCore`). `AddClassColumns` reads it like
  `[ExcludeFromTableType]` and sets `Collation`.
  - For: the closest match to §5's list; the collation sits on the class.
  - Against: unusable on both target classes (§5.3), so the platform could not use it; a new
    public type with no consumer.
- **B. A fluent override on `TableTypeBuilder`**: `UseCollation(string column, string collation)`,
  named after EF's `PropertyBuilder.UseCollation`, which sets the collation of a column the type
  already has, whether derived from the class or added by `Column`.
  - For: one member; works for EF-free classes; it is exactly the "spec 0001 fluent configuration"
    that spec 0012's `TableTypeConvention` maps EF-free attributes onto (§5.3); `HasKey` is the
    precedent for a builder override of a class-derived fact (§2.2); EF itself offers collation
    only fluently (§3).
  - Against: in a plain spec-0001 consumer the collation sits at the registration site, not on
    the class.
- **C. Both A and B**, fluent winning, as spec 0001 §1 pairs attributes and fluent calls.
  - Against: A still has no consumer in the platform.
- **D. `[Column(TypeName = "varchar(255) COLLATE Latin1_General_100_BIN2")]`** with no library
  change: SQL Server accepts it, because the store type is emitted verbatim (§2.1, §2.6).
  - Against: `TableTypeColumnDefinition.Collation` stays null, so the metadata API misreports it;
    `StoreType` is documented as the store type alone; spec 0012's binder builds `SqlMetaData`
    from the store type (`0012-…:1147-1150`). Rejected.
- **E. Honour any attribute by simple name** (duck typing, so Abstractions declares its own).
  - Against: neither EF nor the library matches attributes by name (§2.1, §3). Rejected.

### 6.2 Recommendation: B in Part G, the attribute in the platform

- **Part G adds B only.** It is the least surface that reaches a class-derived column, and it
  follows the pattern spec 0012 already uses for `[TableType]` and `[ExcludeFromTableType]`: an
  EF-free platform attribute mapped by `TableTypeConvention` onto a spec 0001 fluent call.
- **The platform annotation lives in spec 0012**, edited in place, not in Part G. For example
  `CollationAttribute(string name)` in `Tellma.Core.Abstractions.Entities`.
  `TableTypeConvention` maps it onto `TableTypeBuilder.UseCollation` for a standalone class's
  property. It could also map it onto EF's `UseCollation` for an entity property, which would
  give `core.Users.Subject` a stated mechanism.
- **Record A as a review flag**: add a library attribute when a class outside
  `Tellma.Core.Abstractions` needs one.

### 6.3 What the amendment must state

- **Target**: 0001 §5 (the class-derived route and the fluent builder) and 0001's Testing plan
  (derivation validations, differ, snapshot round-trip, integration). §2, §3 and §6 stand
  unchanged; say so once.
- **Requested by**: spec 0012 §4.4 (the rule), spec 0011 §3.8 and §7.3 (`TenantMembershipList`),
  spec 0018 §3.1 (`UserInvitationOutcomeList`).
- **Why**: a type's string column compared with an explicitly collated column must carry that
  collation, or the statement fails to compile with Msg 468 (§4).
- **The delta**, as a C# sketch:
  `public virtual TableTypeBuilder UseCollation(string column, string collation);` on
  `Tellma.Core.EntityFrameworkCore.TableTypes.TableTypeBuilder`, package
  `Tellma.Core.EntityFrameworkCore`. Usage:
  `modelBuilder.HasTableType<TenantMembershipRecord>(schema: "catalog", t => t.UseCollation("Subject", "Latin1_General_100_BIN2"))`.
- **Rules**:
  - It sets `StandaloneColumnConfiguration.Collation` of the named column, on either route. The
    last call wins over an earlier call or `Column(…, collation)`.
  - It applies when the builder builds, so call order against `Column` does not matter.
  - A name no column has fails registration with an `InvalidOperationException` naming the type
    and the column. Names match case-insensitively, like the duplicate and key checks
    (`TableTypeFinalizingConvention.cs:711-727`).
  - **Collation names are not validated**, as EF and the store types are not (§2.6, §3). SQL
    Server refuses an unknown name at `CREATE TYPE` with Msg 448 (§4). Write the canonical
    spelling, since case changes the hash (§2.5).
  - **Non-character columns are refused at model finalization.** A standalone column with a
    collation whose resolved store type is not `char`, `varchar`, `nchar` or `nvarchar` fails
    with an error naming the type, the column and the store type. Otherwise SQL Server fails the
    migration with Msg 447 (§4). This covers both routes, since the store type is known only at
    finalization (`TableTypeFinalizingConvention.cs:747-770`). Decide whether `json` is refused or
    keeps today's silent wire-form replacement (§2.3); refusing is clearer, and no existing model
    or test declares one (grep of the three test projects).
- **Differ**: no change. The collation is part of the canonical JSON, so adding or changing one
  creates a new physical version beside the old one and the sweep retires the old (0001 §3).
- **Compatibility**: additive. No existing definition's JSON changes, because nulls are omitted
  (§2.5). Snapshot and scaffolding code already carry the collation (§2.7). The non-character
  refusal turns a model that would fail at migrate time into one that fails earlier.
- **Tests**, in spec 0001's existing suites:
  - unit `Standalone/StandaloneTableTypesTests`: class route plus `UseCollation` sets the
    definition's `Collation` and leaves `StoreType` alone; ad-hoc route in either call order;
    absent column throws; an `int` property and an explicit `int` store type with a collation
    throw at finalization; the `json` case as decided;
  - unit `Diffing/TableTypeDifferTests`: adding a collation to a standalone column emits a create
    plus the cleanup and no drop; a definition without a collation keeps its canonical JSON;
  - unit `SqlGeneration`: none new, `Create_emits_collation_and_composite_primary_key` already
    pins the `COLLATE` output (§2.6);
  - design `Scaffolding/SnapshotRoundTripTests`: the round-trip model gains a collated standalone
    column;
  - integration `ApplyMigrationsTests`: a class-derived type with a `Latin1_General_100_BIN2`
    column creates; `sys.columns.collation_name` reads it; and a variable of that type joins a
    `BIN2` table column without Msg 468. Adding the type to the migrations host means
    re-scaffolding its migrations and updating the type-count assertions
    (`ApplyMigrationsTests.cs:34-55`, `:230`, `:244`). An `EnsureCreated` context of its own
    avoids both.

## Implications for Part G

- **Shape**: Part G is small. It needs an intro, a placement row (`Tellma.Core.EntityFrameworkCore`,
  `src/core/Tellma.Core.EntityFrameworkCore/`, and the three test projects plus the migrations
  host), and one amendment, §G.1 `TableTypeBuilder.UseCollation`, with Target, Requested by, Why,
  delta, rules, Compatibility and Tests (§6.3). Then Testing, Definition of done (including the
  library README's standalone paragraph), a Decisions record and Review flags. It needs no terms
  list.
- **Decisions record**:
  - fluent hook rather than a library attribute, because the platform's classes are EF-free and
    spec 0012 maps EF-free annotations onto spec 0001's fluent configuration;
  - entity-derived types need nothing, because they already copy the column's collation;
  - no collation-name validation, matching EF.
- **Review flags**:
  - a library attribute (A), which would flip if a non-Abstractions class needs one;
  - the `json` refusal;
  - case normalisation of collation names, which would flip if a hash churn from a spelling change
    is ever seen.
- **Outside Part G, for the spec 0012/0011/0018 edits**:
  - spec 0012 §2.2 gains the platform annotation, and §1.3's `TableTypeConvention` row maps it
    (standalone class → `UseCollation` on the builder; optionally entity property → EF
    `UseCollation`);
  - spec 0012 §4.4 cites §G.1;
  - the two sketches replace the `COLLATE` comment with the annotation, and declare `varchar(255)`
    with BCL `[Column(TypeName = "varchar(255)")]` and `[MaxLength(255)]`, since EF's
    `[Unicode]` is out of reach (§5.3).
- **D-6b does not touch table types**: a model-level `UseCollation` never reaches a property's
  collation (§2.4), so no type definition or hash changes. Uncollated type columns keep taking the
  database's collation at `CREATE TYPE` (§4).
- **Observed, not in scope**: spec 0012 §4.4 registers standalone classes "through spec 0011's
  `Model<T>()`", but spec 0011 requires `Model<T>`'s `T` to implement `IEntityTypeConfiguration<>`
  (`0011-…:407`). A plain row class does not, so the call site that hands `UseCollation` to the
  builder is unstated there.
