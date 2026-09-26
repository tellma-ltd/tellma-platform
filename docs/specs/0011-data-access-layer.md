# Spec: Entity Contract and Data Access

- **Author:** Ahmad Akra
- **Date:** 4 September 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

This spec ships everything between an entity class and SQL Server: the entity contract a
distribution author writes against (base shapes, capability interfaces, annotations), the runtime
description of every entity (`EntityMetadata`), the batch abstraction through which every read and
every write reaches a tenant database (`IDataBatch`), the emitters behind it (`SaveEmitter`, the
update and delete statements, the tree statements), the id allocator, the Queryex host integration
(the EF-model schema adapter, the materializer, the engine amendments spec 0008 deferred), the
telemetry that makes the round-trip budget observable, and the analyzers that keep hand-written
SQL and hard-coded ordinals out of the code.

It builds on three frozen specs. Spec 0001 provides table types: a class opts in with
`[TableType]`, its UDTT is a row image addressed only through the model, and every bulk list
(`IdList`, `BigIdList`, `GuidList`, `StringList`) is a plain `[TableType]` class; this spec binds
those types at runtime by metadata, never by ordinal. Spec 0008 provides the query engine:
`QueryexEngine.CompileQuery` over a `QueryexSchema`, parameter slots the host binds, and the
`BatchOrdinal` that lets several compiled queries share one command; this spec builds the schema
from the EF model, binds the slots, and documents seven additive engine amendments (§11.2): the
key-set restriction, `level()` and the prologue/body split that spec 0008 §Non-goals left to its
first executing host, paging slots, tenant-zone context values, versioned leaves and
`FilterTree.Via`. Spec 0003 fixes the identity model this spec's audit columns point at through
`core.Users`.

The design has one shape: **one round trip is one command**. A request's reads, its validation
context, its id reservation, its connect prologue, its persist statements, its tag bumps and its
read-back are concatenated text walked with `NextResult()`; the transaction lives inside that text,
never across C# code; retry is the executor's job; every write-time invariant is a SQL statement
that aborts the batch. The entity class is the single source of truth for storage shape and wire
shape — no persistence DTO, no parallel `ForSave` hierarchy — and everything the platform needs to
know about it is derived once at startup into `EntityMetadata`.

What this spec deliberately leaves to its siblings: the request context and tenant resolution
that produce a connection (spec 0010), version tags and the caches they validate (spec 0012), the
connect prologue and row-level security filters (spec 0013), the service pipeline that composes
batches — validation rounds, the persist assembly, exception translation to HTTP (specs 0014 and
0015), blob staging (spec 0016), the Excel codec that consumes natural keys (spec 0018), and the
job and notification tables whose rows are inserted by fixed-text statements (specs 0019 and
0020). Each of those consumes the contracts fixed here.

## Goals / Non-goals

**Goals**

- Ship the entity contract in `Tellma.Core.Abstractions.Entities`: `Entity<TKey>`,
  `TopLevelEntity<TKey>`, `ChildEntity<TKey>`, `TreeEntity<TKey>`, `ActivatableTreeEntity<TKey>`,
  `IActivatable`, `IJobEntity`, and every entity annotation, with the conventions (enum-as-string,
  multilingual groups, JSON columns, tree columns, audit columns) that turn a nine-line class into
  a table, a UDTT, a Queryex entity and a wire shape.
- Ship `EntityMetadata` and `IEntityMetadataProvider`: one validated runtime description per
  mapped leaf, built from the EF model at startup, consumed by the emitter, the materializer, the
  allocator, the pipeline, the Excel codec and the capability recipes.
- Ship `ITenantDatabase`, `IDataBatch` and the executor `DataBatch`: typed statements, one
  command per round trip, in-text transactions, contributor prologues and epilogues, executor-owned
  retry with a fixed error taxonomy, `TellmaSqlErrors`.
- Ship `SaveEmitter` (two TVPs per table, separate `INSERT`/`UPDATE`/`DELETE`, a locking
  concurrency guard, aggregate-scoped stamping, skip-unchanged, child synchronisation, blob
  capture), the update and delete emitters, and the tree statements (id-path nodes, affected-set
  re-path, cycle fence, scoped recount).
- Ship `IIdAllocator`: exact-deficit reservation riding the batch, a warm buffer with a
  low-water refill, temporary-id rewriting, self-healing by consuming the gap.
- Ship the Queryex host integration: `IQueryexSchemaProvider` keyed by `MultilingualShape`,
  `KeySetRestriction`, `level()`, `CompiledQuery.Prologue/Body`, `Skip`/`Take` as parameter
  slots, `FilterTree.Via`, the `QueryRowSet` reader, the entity materializer with related
  entities, children and ancestors in one round trip.
- Ship the DB-call budget: `DataAccessScope`, the `tellma.data.*` instruments, and the six
  analyzers `TELLMA0001`–`TELLMA0006`.
- Pin the semantics with a unit suite (golden SQL) and a fixture-schema integration suite on
  LocalDB and Testcontainers.

**Non-goals (explicitly out of scope)**

- **Tenant resolution, connections, the request context, feature composition** — spec 0010
  (`RequestContext`, `ITenantConnectionProvider`, `TellmaBuilder`, the provisioning steps, the
  migrator commands, `READ_COMMITTED_SNAPSHOT` at provisioning, the `tellma_app` grants).
- **Version tags, the versioned caches, `MultilingualShape`, tenant settings** — spec 0012 (this
  spec consumes `VersionTagDependency`, `VersionTagSnapshot`, `UserVersionTagSnapshot`,
  `IVersionTagRegistry` and hosts the tag prelude and bump statements as contributor stages).
- **The connect prologue, securables, row-level security filters, `core.Users` and its
  siblings** — spec 0013 (this spec runs the prologue contributor and wraps the batch body in its
  guard; it never composes a filter).
- **The service pipeline: validation rounds, the persist assembly, capability projection,
  exception translation** — spec 0014; **wire shapes, JSON options, HTTP mapping** — spec 0015.
- **Blob kinds, staging, confirm and release** — spec 0016 (this spec emits the capture tables
  the blob effect reads).
- **Excel export and import** — spec 0018 (this spec supplies natural keys and metadata facets).
- **Job and notification statements, leases, schedules, the inbox** — specs 0019 and 0020
  (this spec supplies `IJobEntity`, `Sql`, `Tvp`, `OnCommitted` and `KeySetRestriction`).
- **Composite natural keys, `SqlBulkCopy` imports, `Guid` keys, EF parent→child navigations,
  tenantless (catalog-scoped) batches** — deferred; each has a named seat below.

## 1. Placement and architecture

### 1.1 Projects, namespaces, and dependency edges

| Piece | Location | Namespaces | References |
|---|---|---|---|
| Entity contract, data access API, standalone table types | `src/core/Tellma.Core.Abstractions/` (existing) | `Tellma.Core.Abstractions.Entities`, `.Data`, `.TableTypes` | `Tellma.Core.Queryex` — the package's one non-BCL edge, taken here because `FilterTree`, `QuerySpec`, `QueryexColumn`, `QueryexSchema` and `KeySetRestriction` appear in the contracts modules consume; nothing from EF Core, SqlClient or ASP.NET Core |
| Runtime: executor, emitters, allocator, schema provider, materializer, binder, conventions, `TellmaDbContext` | `src/core/Tellma.Core/` (existing runtime package) | `Tellma.Core.Data` | `Tellma.Core.Abstractions`, `Tellma.Core.EntityFrameworkCore` (spec 0001), `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.SqlServer.HierarchyId`, `Microsoft.Data.SqlClient`, `Microsoft.Extensions.*` |
| Analyzers | `src/core/Tellma.Core.Analyzers/` (new) | `Tellma.Core.Analyzers` | `Microsoft.CodeAnalysis.CSharp`, `Microsoft.SqlServer.TransactSql.ScriptDom`; shipped as an analyzer asset of `Tellma.Core.Abstractions`, so every project that references the contract also runs the analyzers |
| Unit tests | `test/core/Tellma.Core.Tests/Data/` | | golden SQL, metadata validation, allocator arithmetic, retry classification, materializer routing, natural-key inference, enum sizing |
| Integration tests | `test/core/Tellma.Core.IntegrationTests/Data/` (`Category=Integration`); fixture entities in `test/shared/Tellma.Testing.Entities/` | | the `fixture` schema (§13.2) on LocalDB (Windows) or Testcontainers SQL Server (Linux) |
| Analyzer tests | `test/core/Tellma.Core.Analyzers.Tests/` | | `Microsoft.CodeAnalysis.Testing` |

`Tellma.Core.Abstractions` holds every public contract of this spec because modules
(`Tellma.Module.Gl`) reference only Abstractions packages and must see the batch API, the
metadata and the entity bases; there is no separate `Tellma.Core.Data.Abstractions`. The runtime
types below (`DataBatch`, `SaveEmitter`, `TableTypeBinder`, `IdAllocator`, `QueryexSchemaProvider`,
`EntityMaterializer`, `TreeStatements`, `TellmaDbContext`, the conventions) live in
`Tellma.Core.Data`; a distribution names none of them and reaches them only through composition
and the `IDataBatch` surface.

Contract blocks are C# sketches: names and shapes are normative; `using` directives, XML
documentation, cancellation-token parameters, method bodies and accessibility details are omitted,
so a block is never pasted into code. SQL statements are the exact shape to emit.

### 1.2 Package pins

| Package | Version | Why |
|---|---|---|
| `Microsoft.EntityFrameworkCore.*` | 10.0.11 | required by the HierarchyId package |
| `Microsoft.EntityFrameworkCore.SqlServer.HierarchyId` | 10.0.11 | the tree `Node` column and the `HierarchyId` reader |
| `Microsoft.Data.SqlClient` | 6.1.6 | required by HierarchyId 10.0.11; 6.1.5 fixed a transaction-zombie defect the executor would otherwise hit |
| `Microsoft.SqlServer.TransactSql.ScriptDom` | the current 170-series release at implementation, pinned in central package management | `TELLMA0003` parses raw statement text |
| `Testcontainers.MsSql` | 4.14.0 | the Linux integration tier |

The pins are repo-wide through central package management; the identity server and the connectors
build against them unchanged.

### 1.3 The DbContext and the options helper

`TellmaDbContext` (`Tellma.Core.Data`) is the platform-owned EF context. Its model is built from
the leaf types composition registered (`FeatureContribution.Entity<T>()`, `Model<T>()` and
`TellmaBuilder.UseEntity<TDefault, TLeaf>()` of spec 0010): every default leaf a pack ships is
replaced in the model by the distribution's leaf when one is registered, and the model contains
exactly one mapped type per table. The context is used for model building and for migrations
(through spec 0010's migrator) and is never resolved by distribution or pack code: there is no
LINQ surface, tables are read and written only through `IDataBatch`, and `TELLMA0002` refuses any
EF query or `SaveChanges` over it outside the platform and the migrator.

`UseTellmaSqlServer(options, connectionString)` is the one place provider options are applied so
that every host and the migrator build one model: `UseSqlServer(...)`, `UseHierarchyId()`,
`UseTableTypes()` (spec 0001), `UseCompatibilityLevel(160)`, and `Application Name` =
`DeploymentIdentity.DeploymentId` on the connection string — the value spec 0010's connection
composition sets on every connection of a host — so EF's injected name and the executor's raw
connections share one pool key. The compatibility level is pinned so that
`nvarchar(max)` JSON columns never flip to the native `json` type behind the model's back.

`ConfigureConventions` and the model-finalizing conventions the context installs, in order:

| Convention | Effect |
|---|---|
| `EnumStringConvention` | `Properties<Enum>().HaveConversion<string>()`; nullable enums included (pinned by a unit test that enumerates them explicitly if the generic filter misses `TEnum?`) |
| `EnumStringLengthConvention` | non-unicode, `MaxLength = max(8, longest member name)` unless `[MaxLength]`/`[Unicode]` or fluent facets are present; no `CHECK` |
| `JsonColumnConvention` | `[JsonColumn]` → `HasColumnType("nvarchar(max)")` |
| `AuditConvention` | `TopLevelEntity<TKey>`: the four audit FKs to `core.Users` (`NO ACTION`), `datetimeoffset(7)` |
| `TableTypeConvention` | `[TableType]` on a type → `HasTableType(name?, schema?)`; `[ExcludeFromTableType]` on a property → the column's exclusion (spec 0001's fluent configuration, so the attributes live in `Tellma.Core.Abstractions` without an EF edge, §2.2); inherited by a leaf |
| `TemporalConvention` | `[Temporal]` → system-versioning with history `<schema>.<Table>History`, period columns as shadow properties excluded from the table type; children of a temporal root follow it unless `[Temporal(false)]`; a TPT root or leaf is a startup error |
| `ChildConvention` | `[ParentKey]` → FK `FK_<Table>_<Parent>Id` (`NO ACTION`) + `IX_<Table>_<Parent>Id (…) INCLUDE (Id)` |
| `TreeConvention` | `TreeEntity<TKey>`: self FK on `ParentId` without a CLR navigation, `IX_<Table>_ParentId`, the shadow `Node hierarchyid NOT NULL` with `UX_<Table>_Node`, `ExcludeFromTableType()` on `Node`, defaults `SubtreeCount`/`ActiveSubtreeCount` = 1, `[Tree(MaxDepth)]` recorded |
| `ActivatableConvention` | `IActivatable` → `DF_<Table>_IsActive (1)` |
| `JobEntityConvention` | `IJobEntity` → `FK_<Table>_JobId → core.Jobs(Id) ON DELETE SET NULL`, `UX_<Table>_JobId (JobId) WHERE JobId IS NOT NULL` |
| `UniqueConvention` | `[Unique]`, `[NaturalKey]` → `UX_<Table>_<Col>`, filtered `WHERE <Col> IS NOT NULL` when nullable; on a child, `(ParentKey, Col)` |
| `BlobReferenceConvention` | `[BlobReference]` → `FK_<Table>_<Column> → core.Blobs(Id)` (`NO ACTION`), `UX_<Table>_<Column>` filtered `WHERE <Column> IS NOT NULL` (single-column on a child too: a blob has one owner row) |
| `SequenceConvention` | every keyed table → `HasSequence` `<schema>.sq_<Table>` `AS int` (or `bigint`) `START WITH 1000 INCREMENT BY 1 NO CYCLE` |
| `PositiveKeyConvention` | every keyed table → `CK_<Table>_Id` `CHECK ([Id] > 0)`: the wire gives `0` (new) and negative ids (temporary) a meaning, so a stored non-positive id would be a row no save can address; sequences start at 1000 and seeded rows use 1–999 (§4.1), so only a hand-written seed, migration or script could produce one |
| `MultilingualConvention` | `[Multilingual]` groups share `MaxLength` and unicode; `P2`/`P3` nullable |

Constraint and index names follow `PK_<Table>`, `FK_<Table>_<Column>`, `UX_<Table>_<Cols>`,
`IX_<Table>_<Cols>`, `CK_<Table>_<Name>`, `DF_<Table>_<Column>`; the conventions emit them, so a
distribution never names one.

### 1.4 Reserved SQL names

Three parameter and table-variable prefixes are reserved and enforced by `TELLMA0003`:

- `@qx{b}_` — the engine's names (spec 0008 §13): `@qx{b}_p{n}` parameters, `@qx{b}_v{n}`
  values, `@qx{b}_skip`/`@qx{b}_take`.
- `@tb{b}_` — this spec's per-statement names: `@tb{b}_t{i}` TVPs, `@tb{b}_p{i}` scalars,
  `@tb{b}_now`, `@tb{b}_keys`, `@tb{b}_ids`, `@tb{b}_new`, `@tb{b}_saved`, `@tb{b}_touched`,
  `@tb{b}_conflicts`, `@tb{b}_msg`, `@tb{b}_cap{n}`, `@tb{b}_aff`, `@tb{b}_old`, `@tb{b}_paths`,
  `@tb{b}_anc`, `@tb{b}_first`, `@tb{b}_last`, `@tb{b}_blob_<Schema>_<Table>_<Column>`, and the
  names sibling specs' fixed-text statements declare under the same prefix (`@tb{b}_claimed`,
  `@tb{b}_due`, `@tb{b}_fired`, `@tb{b}_jobIds`).
- `@tm_` — the once-per-batch names of the schema guard, the connect prologue, the tag guard and the
  tag bumps (`@tm_schema`, `@tm_UserId`, `@tm_Guard`, `@tm_expectedTags`, `@tm_tag`, `@tm_tagNames`,
  `@tm_callerIds`, `@tm_userIds_<Column>`, `@tm_visible`, and the prologue's parameters of spec
  0013).

`{b}` is the statement's batch ordinal. Distribution and pack raw SQL may declare none of these;
it names its own parameters through the `FormattableString` holes of `IDataBatch.Sql`, which the
executor renames into the `@tb{b}_` namespace.

## 2. The entity contract

### 2.1 Bases and capability interfaces

```csharp
// Tellma.Core.Abstractions.Entities
public abstract class Entity<TKey>                      // TKey ∈ int | long
{
    public TKey Id { get; set; }                        // server-owned; PK; default means new
}

public abstract class TopLevelEntity<TKey> : Entity<TKey>
{
    public DateTimeOffset CreatedAt { get; set; }       // datetimeoffset(7); server-owned
    public int CreatedById { get; set; }                // server-owned; FK -> core.Users
    public DateTimeOffset ModifiedAt { get; set; }      // datetimeoffset(7); server-owned; concurrency token (read as the expected stamp on save)
    public int ModifiedById { get; set; }               // server-owned; FK -> core.Users
}

public abstract class TopLevelEntity : TopLevelEntity<int>;

public abstract class ChildEntity<TKey> : Entity<TKey>;   // saved only with its owner; no audit columns; owning FK marked [ParentKey]

public abstract class ChildEntity : ChildEntity<int>;

public abstract class TreeEntity<TKey> : TopLevelEntity<TKey>
{
    public TKey? ParentId { get; set; }                 // FK -> self; no CLR navigation
    public int SubtreeCount { get; set; }               // server-owned; 1 for a leaf
    // Node hierarchyid is a shadow column added by the tree convention; never a member
}

public abstract class TreeEntity : TreeEntity<int>;

public abstract class ActivatableTreeEntity<TKey> : TreeEntity<TKey>, IActivatable
{
    public bool IsActive { get; set; } = true;          // server-owned; created true; written by Update only
    public int ActiveSubtreeCount { get; set; }         // server-owned
}

public abstract class ActivatableTreeEntity : ActivatableTreeEntity<int>;

public interface IActivatable
{
    bool IsActive { get; set; }                         // server-owned
}

public interface IJobEntity
{
    int? JobId { get; set; }                            // server-owned; FK -> core.Jobs ON DELETE SET NULL; unique where not null
}
```

| Member | Meaning |
|---|---|
| `Entity<TKey>` | The keyed base. `TKey` is `int` from `sq_<Table>` by default, `long` by `TopLevelEntity<long>`; `Guid` keys are not offered. `Id = 0` means new on the wire, `Id < 0` is a temporary id unique within the payload (rewritten in every self-typed foreign key and child parent key by the allocator, §7.3), `Id > 0` is an update. System-written entities (`Job`, `Notification`, `Blob` of specs 0019, 0020, 0016) derive from `Entity<int>`, carry `CreatedAt` explicitly, and are never written through `Save`. |
| `TopLevelEntity<TKey>` | Adds the four audit columns, all server-owned and stamped server-side once per batch from `SYSUTCDATETIME()` (§8.2). `ModifiedAt` is the concurrency token: user-visible mutations (save, `Update` actions, invite) stamp it; bookkeeping never touches the entity row (activity, tags and inbox tracking live on `core.UserStamps`; leases on `core.Jobs`); the one documented exception is the connect prologue's `Invited → Joined` flip of spec 0013. No `rowversion` column exists anywhere. |
| `ChildEntity<TKey>` | Saved only with its owner and never addressed by a service of its own. No audit columns: a child change stamps the parent (§8.2). The owning foreign key carries `[ParentKey]`. |
| `TreeEntity<TKey>` | `ParentId` is a self-referencing foreign key configured without a navigation; the Queryex navigation `Parent` derives from the column name exactly like every other navigation-less FK, so `Parent.Name`, `descendantOf`, `ancestorOf` and `level(Node)` work unchanged, and a details read carries the parent row in `Related` (§11.5) because spec 0014's default details expansion covers every foreign-key navigation, `Parent` included, each projected per §2.5. A tree is extended by plain inheritance (`MyCenter : Center`) and registered with `UseEntity<Center, MyCenter>()`; `TLeaf : TDefault` is the one constraint for every entity kind. `Node` is a shadow `hierarchyid` (§10). |
| `ActivatableTreeEntity<TKey>` | Adds `IsActive` and `ActiveSubtreeCount` (expanders on the active-only tree view without a subquery). No `Level` column (`level(Node)`), no `IsLeaf` (`SubtreeCount = 1`). |
| `IActivatable` | A one-property gate. `IsActive` is `ServerOwned` by derivation: every row is created active (the `INSERT` writes the column's default and ignores the payload), and the column changes only through the activate/deactivate actions, which are `IDataBatch.Update` statements (§9.1) — the one path that writes it. A client value on a save or an import is overwritten silently, like every other server-owned column. |
| `IJobEntity` | The row's link to the job that is processing it. `JobId` is server-owned: spec 0019's enqueue statement sets it in the same transaction without touching `ModifiedAt`, and a schedule-fired entity handler that inserts its own row supplies it through `EnlistSaveOptions.ServerOwned` on the enlisted insert (spec 0014 §13.3). |

Capability declarations are exactly three kinds: a base class for a shape that adds columns, a
one-property interface for a gate, an annotation for a declaration. There is no `IAudited`,
`IMultilingual`, `ISystemVersioned` or `ITreeEntity<TKey>`: audit is on the base, temporal is
`[Temporal]`, multilingual is `[Multilingual]`, tree is the base. A capability interface is never
paired per entity.

### 2.2 Annotations

```csharp
// Tellma.Core.Abstractions.Entities
public sealed class TemporalAttribute(bool enabled = true) : Attribute;   // on type; inherited; children follow the parent unless [Temporal(false)]
public sealed class TableTypeAttribute : Attribute;                     // on type; inherited; optional Name and Schema (spec 0001); TableTypeConvention (§1.3) maps it onto the model
public sealed class ExcludeFromTableTypeAttribute : Attribute;          // on property; inherited: the column is absent from the row image's UDTT
public sealed class TreeAttribute(int MaxDepth = 32) : Attribute;       // on type; inherited
public sealed class ParentKeyAttribute : Attribute;                     // on property: the child's owning foreign key
public sealed class MaxChildrenAttribute(int Count) : Attribute;        // on a [NotMapped] child collection: rows per parent; default 10,000
public sealed class ServerOwnedAttribute : Attribute;                   // on property: client value overwritten from the before image (update) or the fresh default (insert)
public sealed class WriteOnceAttribute : Attribute;                     // on property: excluded from every UPDATE; a changed value on update is the error WriteOnce
public sealed class DerivedAttribute : Attribute;                       // on property: set by the pipeline's hooks (preprocess, or a validator needing the before image); the client's value is discarded; written on insert and update
public sealed class NaturalKeyAttribute(int Order = 0) : Attribute;     // on a scalar property with a list type in §4.4; implies [Unique]; composite keys reserved
public sealed class UniqueAttribute : Attribute;                        // on property: index + the pipeline's key-load validator + 2601/2627 mapping by index name
public sealed class SearchableAttribute(SearchKind Kind = SearchKind.Contains) : Attribute;   // on string property
public sealed class PreserveWhitespaceAttribute : Attribute;            // on string property: opts out of trim-and-null normalisation
public sealed class JsonColumnAttribute : Attribute;                    // on string property: nvarchar(max), opaque text
public sealed class MultilingualAttribute : Attribute;                  // on the primary property of a P/P2/P3 group; twins found by name
public sealed class CacheableAttribute(int MaxRows = 0) : Attribute;   // on type; inherited; 0 = spec 0012's EntityMaxRowsDefault
public sealed class BumpsVersionTagAttribute(string name) : Attribute;  // on type; repeatable; inherited
// on type; repeatable; inherited
public sealed class BumpsUserVersionTagAttribute(
    UserVersionTagNames column, string userIdProperty = "UserId") : Attribute;
// [BlobReference], BlobPreset and BlobReadAccess: spec 0016 §2.3
public sealed class ExcludeFromExcelAttribute : Attribute;              // on property
public sealed class SiblingAttribute(string navigation) : Attribute;    // on type: a one-row-per-owner entity whose key is a foreign key to its owner; declares the owner's navigation to it (§11.1)

public enum SearchKind { Contains, Prefix }
```

The data layer's own reading of each annotation:

| Annotation | What this spec does with it | Semantics owned elsewhere |
|---|---|---|
| `[Temporal]` | system-versioning per §4.2; skip-unchanged rows in the emitter (§8.2) | — |
| `[TableType]`, `[ExcludeFromTableType]` | `TableTypeConvention` (§1.3) maps them onto spec 0001's table-type configuration; the row image `TableTypeBinder` binds by column name | the UDTT's naming, versioning and migration: spec 0001 |
| `[Tree]` | `MAXRECURSION` cap of the tree statements (§10.2); metadata `Tree.MaxDepth` | — |
| `[ParentKey]` | child collections (§2.3); the synchronise statements (§8.3) | — |
| `[MaxChildren]` | metadata `Children[].MaxCount` (§2.3, §3.1) | the ceiling's enforcement, in save step 1: spec 0014 |
| `[Sibling]` | the owner-to-sibling navigation in the Queryex schema (§11.1) | the sibling's writer and reader: the feature that owns it (spec 0010 §2.4) |
| `[ServerOwned]`, `[WriteOnce]`, `[Derived]` | `PropertyOwnership` (§2.4); `SET`-list membership (§8.2) | the `WriteOnce` validation error and the hooks that fill derived columns: spec 0014 |
| `[NaturalKey]`, `[Unique]` | the index (§4.4), `EntityMetadata.NaturalKeys`/`UniqueIndexes`, 2601/2627 mapping by index name (§6.4) | the uniqueness validator and field error: spec 0014; the Excel row key: spec 0018 |
| `[Searchable]`, `[PreserveWhitespace]`, `[ExcludeFromExcel]` | metadata facets only | search disjunction and trim normalisation: spec 0014; Excel shape: spec 0018 |
| `[JsonColumn]` | `nvarchar(max)`, bound as text, absent from the Queryex schema (§11.1) | serialisation by the owning service |
| `[Multilingual]` | the group (§2.6); `Name2`/`Name3` gating in the schema by `MultilingualShape` (§11.1) | labels and negotiation: spec 0012 |
| `[Cacheable]`, `[BumpsVersionTag]`, `[BumpsUserVersionTag]` | metadata flags `IsCacheable`; the tables' tag names reach the epilogue through spec 0012's `IVersionTagRegistry` (§5.3, §5.5 step 7) | spec 0012 |
| `[BlobReference]` | FK and filtered unique index (§1.3, §4.2); `EntityMetadata.BlobReferences`; the capture tables the emitter fills and the handles list (§8.5) | staging, confirm and release: spec 0016 |

`[Stack]`, `[ApiResource]`, `[DefaultSelect]`, `[RelatedSelect]`, `[DetailsExpand]`,
`[EntityAction]`, `[ApiAction]` and `[ApiRoute]` are declared by specs 0014 and 0015; this spec
reads `[RelatedSelect]` only through the projection the pipeline hands the materializer (§11.5).

### 2.3 Child collections

A child collection is any `[NotMapped]` `list<TChild>` property on a parent whose `TChild :
ChildEntity<TKey>`; its name is the child's table name (`RoleMemberships`). The EF model never sees
it — no `Include`, no cartesian reads, no collections in Queryex — but save and details payloads
carry it, `EntityMetadata.Children` pairs it with the foreign key behind the child's `[ParentKey]`
and carries its `[MaxChildren]` cap — the rows one parent may carry in that collection, 10,000 by
default, enforced per parent at every depth by spec 0014's save step 1 — and the emitter
synchronises it. Nesting is recursive: a child may declare `[NotMapped]` grandchildren, which carry
a cap of their own under their own parent. A child type declares exactly one `[ParentKey]`; a
second foreign key to the parent type is an ordinary reference and never pairs a collection.

`null` versus empty is significant on the wire and in the emitter: `null` means "not supplied —
leave the children alone"; `[]` means "delete them all"; a list means "synchronise". A child row
whose parent id is not among the parents in the payload is never re-parented and never touched;
the pipeline reports it as `Entity.NotFound`.

### 2.4 Property ownership

`EntityMetadata` reports one `PropertyOwnership` per property:

| Ownership | Source | Emitter behaviour |
|---|---|---|
| `ServerOwned` | `[ServerOwned]` (spec 0013 §3.1 declares it on the invitation-evidence columns of `core.Users`); by derivation `Id`, the four audit columns, `SubtreeCount`, `ActiveSubtreeCount`, `IsActive` and `JobId` | Never in a `SET` list. On `INSERT` the audit columns are stamped by the statement; every other server-owned column is written from the in-memory row after `EntityMetadata.ResetServerOwned(entity)` restored its fresh-instance value (an enlisted insert re-applies the members its `EnlistSaveOptions.ServerOwned` names, spec 0014 §13.3). On update the before image wins silently. |
| `WriteOnce` | `[WriteOnce]` | Never in a `SET` list; written on `INSERT` from the payload. A changed value on update is reported by the pipeline as `WriteOnce` at the path. |
| `Derived` | `[Derived]` | In the `SET` list and the `INSERT` column list, from the in-memory row as the pipeline's hooks left it (the preprocess hook, or a validator whose value needs the before image); the client's value never reaches the emitter, because the pipeline calls `EntityMetadata.ResetDerived(entity)` on every row, new and existing, before the hook (spec 0014). Counts as an editable column in the unchanged-row comparison (§8.2). |
| `DatabaseOwned` | a column with computed SQL in the model (`HasComputedColumnSql`), by derivation; no attribute | Never in the `INSERT` column list, a `SET` list or the unchanged-row comparison, and absent from the UDTT (spec 0001 excludes computed columns); the read-back returns it; a client value is ignored silently. |
| `Editable` | everything else | In the `SET` list. |

`Derived` is for a value the service derives from the payload itself — a document total summed
over its lines, a normalised code — which `ServerOwned` cannot express (the before image would
win) and `Editable` would leave to the client. A value derivable from the row's own columns alone
is a persisted computed column in the model, `DatabaseOwned` by derivation, never `[Derived]`;
spec 0013's `User.State` is one.

Ownership declared by a capability interface (`IActivatable.IsActive`, `IJobEntity.JobId`) is
derived from the interface by the metadata builder, because attributes on interface members do not
reach implementing properties.

### 2.5 The related projection

When an entity carries no `[RelatedSelect]`, the projection through which it travels as a related
entity is fixed and narrow: `Id`, the `[Multilingual]` `Name` group, `Code` when the entity has
one, and every `[BlobReference]` column whose preset is `Avatar`. A declaration replaces the
default rather than extending it. Core's `User` and `Role` carry no declaration and are projected
by the default (`Id, Name, Name2, Name3, ImageId` and `Id, Name, Name2, Name3, Code`), so
`CreatedBy.Email`, `CreatedBy.Subject` and the contact columns travel through a navigation only
for a caller whose `Read` on `User` is unfiltered. The materializer records each related set's
projection (§11.5); the traversal rule (spec 0014 §5.2) confines a path to it unless the caller's
`Read` on the target is unfiltered.

### 2.6 Multilingual groups, enums, JSON columns

**Multilingual groups.** `[Multilingual]` on a string property `P` anchors the group `P`; the
twins `P2` and `P3` are found by name and must exist as nullable strings sharing `P`'s
`MaxLength` and unicode (startup error otherwise). A twin without an anchor is an ordinary column
(`Address2` is never mistaken for a translation). The Queryex schema declares `P2`/`P3` only when
the tenant's `MultilingualShape` admits them (§11.1); the wire omits absent twins.

**Enums.** A distribution writes `CenterType: CenterType` and nothing else: the conventions of
§1.3 store every enum as `varchar(n)`, `n = max(8, longest member name)`, without an `IN`-list
`CHECK` (the class is the truth; a member added is not a migration per table). The Queryex adapter
declares the property as `String` with the sized `VarChar` store type, so `CenterType = 'Service'`
compiles and seeks. Cross-column `CHECK`s that encode state rules stay where their owners declare
them.

**JSON columns.** A `[JsonColumn]` string is `nvarchar(max)`, bound as text, opaque to the data
layer and absent from the Queryex schema. `ToJson()` owned types and primitive collections are not
used.

### 2.7 Natural keys

`[NaturalKey(Order)]` declares a single-column natural key on any scalar property whose CLR type has
a standalone list type in §4.4 (`string`, `int`, `long`, `Guid`, `DateOnly`), backed by a
single-column unique index (composite keys are reserved behind `Order`). The key serves row identity
by value, so its CLR type decides only which list type a key restriction binds through (§11.2) and
the cell type of spec 0018's key columns. Without a declaration the key is inferred over string
properties backed by a single-column unique index in the EF model — uniqueness is read from the
model, never assumed — in this order: the `[Multilingual]` `Name` group, `Code`, required unique
strings in declaration order, nullable unique strings. `EntityMetadata.NaturalKeys` is the ordered
list (declared keys by `Order`, then inferred); `NaturalKey` is its first; none → the surrogate id
is the export row key, flagged `Surrogate` in spec 0018's manifest and refused across tenants. On a
child the uniqueness backing is `(ParentKey, Property)`. The realised startup gate fails when a
declared key lacks a unique index or sits on a scalar whose CLR type has no standalone list type in
§4.4, and warns for an entity referenced by a foreign key that has no key at all. Core declarations:
`User.Email`, `Role.Code`, `Center.Code`.

### 2.8 A distribution's entity

Illustration (a distribution's complete tree entity; `Center` is spec 0017's default leaf):

```csharp
[Table("Centers", Schema = "gl"), TableType]
public sealed class MyCenter : Center { [MaxLength(50)] public string? Region { get; set; } }
```

The leaf re-declares `[Table]` and `[TableType]`; one composition line
(`tellma.UseEntity<Center, MyCenter>()`) replaces the default in the model, the metadata, the
Queryex schema and every generic service. No UDTT class, no SQL, no repository, no child mapping,
no tree code.

## 3. Entity metadata

### 3.1 The contract

```csharp
// Tellma.Core.Abstractions.Data
public sealed class EntityMetadata
{
    public Type ClrType { get; }
    public string Name { get; }
    public TableName Table { get; }
    public string TableTypePhysicalName { get; }
    public PropertyMetadata Key { get; }
    public IReadOnlyList<PropertyMetadata> Properties { get; }
    public IReadOnlyList<ChildCollectionMetadata> Children { get; }
    public IReadOnlyList<ReferenceMetadata> References { get; }
    public IReadOnlyList<NaturalKeyMetadata> NaturalKeys { get; }
    public PropertyMetadata? NaturalKey { get; }        // first of NaturalKeys
    public IReadOnlyList<MultilingualGroup> MultilingualGroups { get; }
    public IReadOnlyList<UniqueIndexMetadata> UniqueIndexes { get; }
    public bool IsTopLevel { get; }
    public bool IsTemporal { get; }
    public bool IsActivatable { get; }
    public bool IsJobEntity { get; }
    public bool IsCacheable { get; }
    public TreeMetadata? Tree { get; }
    public IReadOnlyList<BlobReferenceMetadata> BlobReferences { get; }
    public string SchemaFingerprint { get; }            // hex SHA-256 of the editable shape
    public void ResetServerOwned(object entity);        // overwrites server-owned members from a fresh instance
    public void ResetDerived(object entity);            // overwrites derived members from a fresh instance
}

public sealed record PropertyMetadata(
    string Name, string Column, Type ClrType, PropertyOwnership Ownership, bool IsNullable, bool IsUnique,
    int? MaxLength, int? Precision, int? Scale, IReadOnlyList<string>? EnumValues, string? MultilingualGroup,
    bool IsSearchable, SearchKind? SearchKind, bool ExcludedFromExcel, Func<object, object?> Getter,
    Action<object, object?> Setter);

public enum PropertyOwnership { Editable, WriteOnce, ServerOwned, Derived, DatabaseOwned }

public sealed record ChildCollectionMetadata(
    string Property, EntityMetadata Child, PropertyMetadata ParentKey, int MaxCount,
    Func<object, IReadOnlyList<object>?> Getter);       // MaxCount: [MaxChildren], rows per parent

public sealed record ReferenceMetadata(string Navigation, PropertyMetadata ForeignKey, EntityMetadata Target);

public sealed record NaturalKeyMetadata(PropertyMetadata Property, bool IsDeclared, int Order);

public sealed record MultilingualGroup(
    string Name, PropertyMetadata Primary, PropertyMetadata? Secondary, PropertyMetadata? Tertiary);

public sealed record UniqueIndexMetadata(string IndexName, IReadOnlyList<PropertyMetadata> Properties);

public sealed record TreeMetadata(
    PropertyMetadata ParentKey, string NodeColumn, PropertyMetadata SubtreeCount,
    PropertyMetadata? ActiveSubtreeCount, int MaxDepth);

public sealed record BlobReferenceMetadata(PropertyMetadata Property, string Kind, BlobKindPolicy Policy);

public interface IEntityMetadataProvider                // singleton; built once at startup
{
    EntityMetadata Get(Type entityType);
    IReadOnlyList<EntityMetadata> All { get; }
    string StorageFingerprint { get; }                  // hex SHA-256 of the relational model (§4.3); bound as @tm_schema
}
```

| Member | Meaning |
|---|---|
| `Name` | The entity name: `<schema>.<CLR type name of the pack's default leaf>` (`core.User`, `gl.Center`); a distribution leaf (`MyCenter`) keeps the default's name. It is the Queryex entity name, the securable resource, the key of related-entity sets and the suffix of the `entity:<Name>` tag. |
| `TableTypePhysicalName` | From `model.GetTableTypes()` (spec 0001): `<schema>.<Table>List_<hash8>`; the executor binds TVPs by this name only. |
| `Properties` | Every mapped scalar CLR property in declaration order (bases first). Shadow columns (`Node`, period columns) are not properties; `Tree.NodeColumn` names the node. |
| `References` | Every many-to-one foreign key on the entity, named by the CLR navigation when one exists, else by the FK column minus its `Id` suffix (`ParentId` → `Parent`, `CreatedById` → `CreatedBy`); the Queryex navigation names (§11.1). |
| `SchemaFingerprint` | SHA-256 over the ordered tuples `(Name, Column, store type, IsNullable, Ownership)` of every `Editable`/`WriteOnce` property and, recursively, of every child collection's; hex, lower-case. Spec 0018 writes it into the manifest to detect a shape drift between export and import. |
| `ResetServerOwned` | Restores every `ServerOwned` property to its value on a freshly constructed instance; the pipeline calls it on new rows before the emitter reads them. |
| `ResetDerived` | Restores every `Derived` property to its fresh-instance value; the pipeline calls it on every row, new and existing, before the preprocess hook, so a hook that sets nothing persists the default and never the client's value. |
| `Get` | Accepts the leaf or any base in its chain (`Get(typeof(User))` returns `MyUser`'s metadata when the distribution registered one); two leaves sharing a base is a startup error. |
| `StorageFingerprint` | The relational model's fingerprint of §4.3, bound as `@tm_schema` on every batch; identical in the web host and the migrator. |

`BlobKindPolicy` is spec 0016's record (`MaxSize`, `AllowedContentTypes`, `Image`, `Thumbnail`,
`StagingTtl`, `ReadAccess`), resolved by the metadata builder from the attribute's preset and
spec 0016's kind registry.

### 3.2 Startup validation

The provider builds every leaf's metadata once from the runtime `IModel` plus reflection, and
reports every failure into spec 0010's realised startup gate (aggregated; the host refuses to
start on any error). Rules:

- **Mapped capability columns.** Every property declared by a platform base or capability
  interface is mapped: a leaf cannot un-inherit, so the only way to break the contract is
  `[NotMapped]`/`Ignore()`, which is refused.
- **Child pairing.** Every `[ParentKey]` is a foreign key to a top-level table; every `[NotMapped]`
  `list<TChild : ChildEntity>` pairs with the one `[ParentKey]` foreign key on `TChild`, which
  declares exactly one; a `[NotMapped]` list of a non-child type is an error.
- **Table type.** Every top-level entity and every child entity opted into `[TableType]`; the
  tree `Node` and the period columns are excluded from it; no other exclusion touches a
  capability column.
- **Trees.** A tree table has `UX_<Table>_Node`; `[Tree(MaxDepth)]` is between 2 and 32.
- **Temporal.** `[Temporal]` is not on a TPT root or leaf; a temporal child belongs to a temporal
  root.
- **Multilingual.** Twins exist, are nullable, share length and unicode.
- **Uniqueness.** A declared `[NaturalKey]` or `[Unique]` sits on a mapped scalar that is neither a
  `[JsonColumn]` nor binary and is backed by a unique index; a referenced
  entity without any natural key is a warning.
- **Navigations.** Derived navigation names do not collide with a property or another navigation.
- **Blob references.** `[BlobReference]` sits on an `int?` property; the kind is registered
  (spec 0016).
- **Enums.** Every enum property has the string conversion and a sized `varchar`.
- **Ownership.** `[WriteOnce]`, `[ServerOwned]` and `[Derived]` never decorate a `[ParentKey]` or a
  database-computed column, and a property carries at most one ownership attribute.
- **Leaf uniqueness.** One leaf per table; one leaf per default (`UseEntity` registered at most
  once per default).

`EntityMetadata` is immutable after the gate passes; `SchemaFingerprint` and the Queryex schema
provider's `Fingerprint` (§11.1) change only with the model.

### 3.3 Columnar rows and related sets

Shapes owned by spec 0015 but placed in `Tellma.Core.Abstractions.Data` so that `.Data` never
references `.Api`; spec 0015 owns their JSON converters:

```csharp
// Tellma.Core.Abstractions.Data
public sealed record QueryColumn(
    string Name, QueryexType Type, int? Scale, bool Nullable, IReadOnlyList<string>? Path, bool GroupingKey);

public sealed class QueryRowSet
{
    public IReadOnlyList<QueryColumn> Columns { get; }
    public int RowCount { get; }
    public Array GetBuffer(int column);                 // one of §11.3's buffers: int[], long[], short[], byte[], decimal[], double[], bool[], string?[], Guid[], DateOnly[], DateTime[], DateTimeOffset[]
    public bool IsNull(int column, int row);
    public sealed class Builder(IReadOnlyList<QueryColumn> columns, IReadOnlyList<Type> fieldTypes, int capacity);   // created from the open reader's metadata before the first Read (§11.3); one AppendRow per row
}

public sealed record RowPage(QueryRowSet Rows, int? Count, QueryRowSet? Ancestors);   // Count: the capped count when CountCap was given; cap + 1 means more than the cap

public sealed class RelatedEntities                     // typed sets restricted to each entity's related projection
{
    public void Add<T>(string entityName, IReadOnlyList<T> entities, IReadOnlySet<string> projection);
    public IReadOnlyDictionary<string, RelatedEntitySet> Sets { get; }
}

public sealed record RelatedEntitySet(
    Type EntityType, IReadOnlyList<object> Entities, IReadOnlySet<string> Projection);
```

`QueryColumn` is the projection of spec 0008's `QueryexColumn` (`Ordinal` becomes the position,
`Path` the navigation path, `GroupingKey` its `IsGroupingKey`). `Type` is the column's Queryex type
(spec 0008 §7.1). `Scale` is set on `Numeric` columns only: `0` for an integer result (`int`,
`bigint`, `smallint`, `tinyint`), the decimal's scale for a `decimal` result, `null` for a floating
result (`float`, `real`); it is `null` on every other type, and the reader takes it from the result
set's own metadata (§11.3).

`QueryRowSet` is the columnar buffer alone. `RowPage` is what `IDataBatch.Rows` returns (§5.2):
the page's rows, the capped count when `RowQueryOptions.CountCap` was given, and the ancestor rows
when `RowQueryOptions.IncludeAncestors` was set (§11.3); spec 0015 §3.5 serialises the page.

## 4. Column vocabulary and schema conventions

### 4.1 Naming

Tables are plural (`core.Users`, `gl.Centers`); the CLR type and the Queryex entity are singular.
Schemas are lower-case (`core`, `gl`, `catalog`, `idsvr`, `fixture`); `dbo` holds only
`__TellmaProvisioning`, `__TellmaSchema` (§4.3) and the standalone table types. Sequences on
tenant-model tables are `<schema>.sq_<Table>` (`gl.sq_Centers`), `AS int` (or `bigint`) `START WITH
1000 INCREMENT BY 1 NO CYCLE`; the band 1–999 is reserved for `HasData` rows (the catalog's
`sq_Tenants` starts at 1, spec 0010 §7.1). History tables are `<schema>.<Table>History`.
No `IDENTITY`, no `rowversion`, no triggers, no `ON DELETE CASCADE` except the one-row-per-owner
sibling tables their owning specs name: `core.UserStamps` and `core.UserPreferences` on `core.Users`
(spec 0013), `core.NotificationPreferences` on `core.Users` (spec 0020), `core.ScheduleStates` on
`core.Schedules` (spec 0019). Every timestamp is an instant stored as `datetimeoffset` and written
at offset zero from `SYSUTCDATETIME()` (SQL Server's implicit conversion carries `+00:00`): audit
columns `datetimeoffset(7)`, every other timestamp `datetimeoffset(3)`; the CLR type is
`DateTimeOffset`; Queryex types them `DateTimeOffset`, so `now()` compares with them directly and a
calendar function needs `local()`. The temporal period columns alone stay `datetime2(7)`, as system
versioning requires. A wall-clock value a distribution stores on purpose is a `datetime2` of its own
and is never a platform timestamp.

### 4.2 Standard column sets

**Keyed** (`Entity<TKey>`): `Id int NOT NULL` (or `bigint`), `PK_<Table>` clustered,
`CK_<Table>_Id CHECK ([Id] > 0)`, `sq_<Table>`.

**Top-level audit** (`TopLevelEntity<TKey>`):

| Column | Type | Null | Constraints |
|---|---|---|---|
| `CreatedAt` | `datetimeoffset(7)` | no | server-stamped on insert |
| `CreatedById` | `int` | no | `FK_<Table>_CreatedById → core.Users(Id)` NO ACTION |
| `ModifiedAt` | `datetimeoffset(7)` | no | concurrency token; no index |
| `ModifiedById` | `int` | no | `FK_<Table>_ModifiedById → core.Users(Id)` NO ACTION |

**Temporal** (`[Temporal]`): `ValidFrom`/`ValidTo datetime2(7) NOT NULL GENERATED ALWAYS AS ROW
START/END` (shadow, excluded from the UDTT), `PERIOD FOR SYSTEM_TIME`, `SYSTEM_VERSIONING = ON
(HISTORY_TABLE = <schema>.<Table>History)`; the history table keeps the default clustered
`(ValidTo, ValidFrom)` index only; never on TPT.

**Child** (`ChildEntity<TKey>`): `<Parent>Id int NOT NULL`, `FK_<Table>_<Parent>Id →
<ParentTable>(Id)` NO ACTION, `IX_<Table>_<Parent>Id (<Parent>Id) INCLUDE (Id)`; no audit columns.

**Tree** (`TreeEntity<TKey>`): `ParentId int NULL`, `FK_<Table>_ParentId → <Table>(Id)` NO ACTION,
`IX_<Table>_ParentId`; `Node hierarchyid NOT NULL` shadow, `UX_<Table>_Node`, excluded from the
UDTT; `SubtreeCount int NOT NULL DF_<Table>_SubtreeCount (1)`; `ActiveSubtreeCount int NOT NULL
DF_<Table>_ActiveSubtreeCount (1)` (`ActivatableTreeEntity` only). No `Level`, no `IsLeaf`.

**Activatable** (`IActivatable`): `IsActive bit NOT NULL DF_<Table>_IsActive (1)`; an optional
`IX_<Table>_Active (Id) WHERE IsActive = 1` on large tables, declared by the owner.

**Job entity** (`IJobEntity`): `JobId int NULL`, `FK_<Table>_JobId → core.Jobs(Id) ON DELETE SET
NULL`, `UX_<Table>_JobId (JobId) WHERE JobId IS NOT NULL`.

**Uniqueness** (`[Unique]`, `[NaturalKey]`): one `UX_<Table>_<Col>` per declaration, filtered
`WHERE <Col> IS NOT NULL` for a nullable column; on a child `UX_<Table>_<Parent>Id_<Col>`.

**Enum**: `varchar(n)`. **Multilingual group `P`**: `P nvarchar(k) NOT NULL` (or `NULL` when the
anchor is nullable), `P2`/`P3 nvarchar(k) NULL`. **JSON**: `nvarchar(max)`. **Blob reference**:
`int NULL`, `FK_<Table>_<Column> → core.Blobs(Id)` NO ACTION, `UX_<Table>_<Column> (<Column>)`
filtered `WHERE <Column> IS NOT NULL`, single-column on a child too (a blob has one owner row).

### 4.3 Schema evolution — the N−1 rule

A database one migration ahead of the running application keeps working, and a database one
migration behind is never written by an application that would need the missing column. The rule
binds the platform's tables and every distribution's own:

- **Readers bind by name.** The materializer, the row-set reader, the job reader and the prologue
  reader bind columns by name, never by ordinal; an unexpected extra column is ignored.
- **Writers list columns.** Every `INSERT` the emitter generates lists its columns explicitly;
  every `UPDATE` sets named columns.
- **Expand, then contract.** A new column is nullable or defaulted. Under rolling deployment two
  application versions run against one database during every rollout, so a column is dropped
  only when no version that can still be running writes it. A rename therefore takes three
  releases: the first adds the new column and copies the data while the application writes both
  columns and reads the old; the second copies again the rows the previous version wrote during
  the first rollout, while the application reads and writes the new column only; the third drops
  the old column. A stop-the-world deployment may collapse the first two.
- **Table types.** The UDTT window of spec 0001 covers the row image: a new nullable column joins
  the UDTT with the migration that adds it, and the binder's metadata-driven ordinals absorb the
  change.

**The schema fingerprint and the behind guard.** The rule is enforced, never assumed.
`IEntityMetadataProvider.StorageFingerprint` is the hex SHA-256 over every mapped table's `(schema,
name)` and, in model order, each column's `(name, store type, nullability, default)`, every index
name and every sequence name, computed once from the runtime model; the web host and the migrator
compute the same value because they compose the same model (spec 0010 pins the parity). Every tenant
database carries the platform-contributed table `dbo.__TellmaSchema (Fingerprint char(64) PK,
MigrationId nvarchar(150) NOT NULL, PlatformVersion nvarchar(64) NOT NULL, AppliedAt
datetimeoffset(3) NOT NULL)`, created by the platform's model contribution like
`__TellmaProvisioning`. After `Migrate()` succeeds, and only when the database's migration history
ends at the migrator's own last migration (a database already ahead of the migrator — a rollback
deploy — gets no row and a warning), the migrator inserts its model's fingerprint when absent and
deletes every row but the two newest by `AppliedAt`; a failed migration writes nothing. The executor
emits, as the first statement of every round trip against a tenant database (§5.3, §5.5):

```sql
IF NOT EXISTS (SELECT 1 FROM [dbo].[__TellmaSchema] WHERE [Fingerprint] = @tm_schema)
    THROW 50501, N'SchemaBehind', 1;
```

`@tm_schema` is the running model's fingerprint, bound by the executor once per batch. A database
the migrator has not yet brought to this model refuses every request with `503 tenant-schema-behind`
(spec 0010's `TenantUnavailableException`, `Retry-After: 30`) rather than serving a reader that
may select a missing column; a database one migration ahead still lists the previous fingerprint
and is served; a database two or more ahead is refused likewise, because compatibility is promised
across one migration only. The check costs one seek on a two-row table per round trip, is correct
on the first request after a migration with no cache to invalidate, and survives a restore from
backup. The migrator's own batches bind the migrator's fingerprint and run after it wrote the row.
The catalog database carries no fingerprint table: spec 0010's startup check compares its
migration history directly.

### 4.4 Standalone table types

Standalone shapes are plain `[TableType]` classes in `Tellma.Core.Abstractions.TableTypes`,
registered through spec 0010's `Model<T>()` and mapped by `TableTypeConvention` (§1.3) exactly
as an entity's attribute is; physical `[dbo].[<Name>_<hash8>]`, bound through `TableTypeBinder`
by column name like every entity row image. The one catalog type, spec 0010 §3.8's
`TenantMembershipList`, is the class `TenantMembershipRecord` in
`Tellma.Core.Abstractions.Tenancy` under schema `catalog`.

| Type | Columns | Owner / use |
|---|---|---|
| `IdList` (exists) | `Id int PK` | every id-shaped restriction and capture |
| `BigIdList`, `GuidList`, `StringList` (exist) | `Id bigint / uniqueidentifier / nvarchar(450) PK` | `KeySetRestriction` by key type; `@tm_tagNames` uses `StringList` |
| `DateList` | `Id date PK` | `KeySetRestriction` for `DateOnly` keys |
| `IdStampList` | `Id int PK`, `ModifiedAt datetimeoffset(7)` | `DeleteSpec.ByIds` with `ExpectedStamps` (§9.2) |
| `VersionTagList` | `Name nvarchar(128) PK`, `Tag uniqueidentifier`, `Bumped bit` | spec 0012's tag guard (§5.5 step 4) |
| `JobRequestList`, `JobOutcomeList`, `JobProgressList`, `JobLeaseList`, `ScheduleNextList` | per spec 0019 | job statements |
| `NotificationRowList`, `NotificationPreferenceList` | per spec 0020 | notification statements |
| `UserPreferenceList`, `UserInvitationOutcomeList` | per specs 0013 and 0017 | self-service and invite write-back |
| `TenantMembershipList` | per spec 0010 | catalog only |

Entity UDTTs are derived per spec 0001 (`<schema>.<Table>List_<hash8>`). `DateList` and
`IdStampList` are defined here; the others are defined by the specs that name them and registered
on `TellmaDbContext` through `FeatureContribution.Model<T>()`.

## 5. The tenant database and the batch

### 5.1 The database handle

```csharp
// Tellma.Core.Abstractions.Data
public interface ITenantDatabase                        // scoped: one per request or job scope
{
    QueryexSchema Schema { get; }                       // for the tenant's MultilingualShape
    QueryexContextValues Context { get; }
    IDataBatch CreateBatch(BatchPurpose purpose);
}

public interface ITenantDatabaseFactory                 // singleton; used by scope factories and the migrator
{
    Task<ITenantDatabase> OpenAsync(int tenantId);
}

public sealed record QueryexContextValues(
    DateOnly Today, DateTimeOffset Now, int? UserId, string TimeZoneName);

public enum BatchPurpose { Read, Validate, Persist, Maintenance }
public enum TransactionMode { Auto, None, Explicit }
public enum ConcurrencyMode { Check, Override }         // defined once, here; used by .Crud, .Api, .Excel
```

The scoped `ITenantDatabase` is opened by tenant id alone; `Context` and `Schema` are read from
spec 0010's `IRequestContextAccessor.Current` the first time a statement needs them, not at scope
creation, because spec 0013's connect prologue and spec 0012's settings load run on this handle
before `TenantSettings` exists and, as raw statements, need neither. `Context` is `Today` and
`Now` in the tenant zone, `UserId` (late-bound by the connect step) and `TimeZoneName`; `Schema`
is the one for `TenantSettings.Shape`. `TimeZoneName` is `TenantSettings.SqlServerTimeZoneName`
(spec 0012): the Windows id `AT TIME ZONE` accepts, converted once per settings load from the
tenant's IANA zone. `today()` and the `TimeZone` slot therefore bind the tenant zone; the display
zone formats and never binds. Job scopes open theirs through the factory with the scope's tenant
id; nothing here uses `AsyncLocal`.

### 5.2 The batch

```csharp
// Tellma.Core.Abstractions.Data
public interface IDataBatch
{
    BatchPurpose Purpose { get; }
    TransactionMode TransactionMode { get; set; }       // default Auto
    IReadOnlySet<TableName> WrittenTables { get; }      // union of every statement's declared writes
    IReadOnlySet<TableName> CallerAuthority { get; }    // stack-owned tables a caller statement may write; unowned tables always may
    BatchResult<EntityQueryResult<TEntity>> Query<TEntity>(EntityQuery<TEntity> query);
    BatchResult<RowPage> Rows(QuerySpec spec, QueryArguments? arguments, RowQueryOptions? options);
    BatchResult<int> Count(QuerySpec spec, QueryArguments? arguments, int cap = 10000);
    SaveHandle Save<TEntity>(
        IReadOnlyList<TEntity> rows, ConcurrencyMode concurrency = ConcurrencyMode.Check);
    BatchResult<UpdateReceipt> Update<TEntity>(UpdateSpec<TEntity> spec);
    DeleteHandle Delete<TEntity>(DeleteSpec<TEntity> spec);
    void Assert(QuerySpec countSpec, QueryArguments? arguments, int expected, int errorNumber, string code);
    BatchResult<RawResult> Sql(FormattableString sql, SqlOptions? options);
    SqlIdentifier Tvp<TRow>(IReadOnlyList<TRow> rows);
    SqlIdentifier DeclareIdTable();
    void DependsOn(IReadOnlyList<VersionTagDependency> dependencies);
    BatchResult<CacheResult<CachedEntitySet<TEntity>>> FromCache<TEntity>();   // Outcome = Oversized, Value null, when the probe overflows
    void BumpVersionTag(string name);
    void OnCommitted(Action<BatchOutcome> callback);
    Task<BatchOutcome> ExecuteAsync();
}

public sealed record BatchResult<T>                     // Value throws until executed
{
    public T Value { get; }
    public bool IsCompleted { get; }
}

public sealed record BatchOutcome(
    VersionTagSnapshot VersionTags, UserVersionTagSnapshot? UserVersionTags, int RoundTrips);   // UserVersionTags: null on a batch without a connect prologue

public interface IDataBatchContributor                  // scoped DI; prologues ascending by Order, epilogues descending
{
    int Order { get; }
    void Contribute(IDataBatch batch, DataBatchStage stage);
}

public enum DataBatchStage { Prologue, Epilogue }

public sealed record SqlOptions(
    IReadOnlySet<TableName> Writes = [], bool Idempotent = false, int ResultSets = 0,
    SqlIdentifier? UserIds = null)
{
    public static SqlOptions ForCaller(IReadOnlySet<TableName> writes);   // UserIds = @tm_callerIds, the one-row table holding @tm_UserId
}

public sealed record TableName(string Schema, string Name);
public sealed record SqlIdentifier(string Name);        // an object name, bracket-quoted by the batch, or a name the batch declared (Tvp, DeclareIdTable), emitted as is; never a value
public sealed class QueryArguments;                     // IReadOnlyDictionary<string, object?> of declared-parameter values

public sealed class RowQueryOptions
{
    public int? CountCap { get; set; }                  // adds the capped grand total
    public bool IncludeAncestors { get; set; } = false; // tree roots only: also returns the page's ancestor rows (§11.3)
    public FilterTree? AncestorsFilter { get; set; }    // the access filter alone, for the ancestor rows; null when Unrestricted
    public bool CaptureKeys { get; set; } = false;      // also writes the page's ids into a @tb{b}_keys table
}

public sealed class EntityQuery<TEntity>
{
    public string Select { get; set; }                  // required; bare paths only
    public FilterTree? Filter { get; set; }
    public string? OrderBy { get; set; }
    public int? Skip { get; set; }
    public int? Take { get; set; }
    public QueryArguments? Arguments { get; set; }
    public IReadOnlyList<KeySetRestriction> Restrictions { get; set; } = [];
    public IReadOnlyList<string> Children { get; set; } = [];   // [NotMapped] collection names; dotted for grandchildren
    public bool IncludeAncestors { get; set; } = false;
    public FilterTree? AncestorsFilter { get; set; }
}

public sealed record EntityQueryResult<TEntity>(IReadOnlyList<TEntity> Entities, RelatedEntities Related);

public sealed record SaveReceipt(DateTimeOffset Stamp, int Inserted, int Updated, int Deleted);   // Stamp: datetimeoffset(7)
public sealed record SaveHandle(
    BatchResult<SaveReceipt> Receipt, SqlIdentifier SavedIds, SqlIdentifier NewIds,
    SqlIdentifier TouchedIds, IReadOnlyList<ColumnCapture> Captures);   // the batch-local names the emitter declared; Receipt.Value after execution
public sealed record ColumnCapture(TableName Table, string Column, SqlIdentifier Identifier);   // one per captured column of every written table

public abstract record UpdateSpec<TEntity>              // exactly one shape
{
    public sealed record ByIds(
        IReadOnlyList<object> Ids, FilterTree? ReadFilter, FilterTree? Filter,
        IReadOnlyDictionary<string, object?> Assignments, bool Stamp = true, bool AllOrNothing = false)
        : UpdateSpec<TEntity>;
    public sealed record ByQuery(
        FilterTree Filter, QueryArguments? Arguments, IReadOnlyDictionary<string, object?> Assignments,
        int Cap, int ExpectedCount, bool Stamp = true) : UpdateSpec<TEntity>;
    // Filter: the action's grant filter (ByIds) or the query, conjoined with the access filter (ByQuery)
    // ReadFilter: the caller's Read filter; the AllOrNothing check counts existence under it (hidden equals missing)
    // Assignments: property -> uniform value; editable or server-owned. Stamp: false only for platform bookkeeping on tables without audit columns
    // AllOrNothing: every id must be readable under ReadFilter (50404) and pass Filter (50403) before the UPDATE (§9.1)
}

public sealed record UpdateReceipt(IReadOnlySet<object> UpdatedIds);

public abstract record DeleteSpec<TEntity>              // exactly one shape
{
    public sealed record ByIds(
        IReadOnlyList<object> Ids, FilterTree? ReadFilter, FilterTree? Filter,
        IReadOnlyList<DateTimeOffset?>? ExpectedStamps = null) : DeleteSpec<TEntity>;   // ExpectedStamps: datetimeoffset(7)
    public sealed record ByQuery(FilterTree Filter, QueryArguments? Arguments, int Cap, int ExpectedCount)
        : DeleteSpec<TEntity>;                          // Cap and ExpectedCount verified in SQL (§9.2)
    public sealed record WithDescendants(IReadOnlyList<object> Ids, FilterTree? ReadFilter, FilterTree? Filter)
        : DeleteSpec<TEntity>;
    // ReadFilter: the caller's Read filter — existence is counted under it (50404; hidden equals missing); Filter: the action's grant (50403)
}

public sealed record DeleteReceipt(IReadOnlySet<object> DeletedIds);
public sealed record DeleteHandle(
    BatchResult<DeleteReceipt> Receipt, SqlIdentifier DeletedIds, IReadOnlyList<ColumnCapture> Captures);
public sealed record RawResult(IReadOnlyList<QueryRowSet> ResultSets);
```

| Member | Meaning |
|---|---|
| `Purpose` | Set at creation. `Read` and `Validate` batches run untransacted; `Persist` batches carry the transaction frame of §5.5; `Maintenance` batches (jobs, sweeps, the migrator) carry the schema guard and spec 0012's prelude alone — no connect prologue and no `@tm_Guard` wrapper — and use the longer command timeout (§6.1). Contributors consult it. |
| `TransactionMode` | `Auto` (default): the text is wrapped in an in-text transaction when any statement declares a write. `None`: autocommit — for bookkeeping whose composer tolerates a partial re-run (lease renewal, sweeps, the job poll). `Explicit`: a `SqlTransaction` opened and committed from C# — the escape hatch for a decision that must be taken in C# before commit, costing two more round trips, never retried by the executor (§6.5) and never used by the platform's own paths. |
| `WrittenTables` | The union of every statement's declared writes: `Save`, `Update`, `Delete` derive theirs from metadata (children included); `Sql` declares through `SqlOptions.Writes`. Spec 0012's epilogue resolves tag bumps from it; the fixture tier audits it against change tracking. |
| `CallerAuthority` | The authority of the current appender (spec 0014 §13.3): the tables, children included, of the stack whose participant holds the batch — the pipeline hands each participant a view of the one batch carrying its own stack's tables, the host's for the host's hooks and effects, the target's for an enlisted group's; empty for a job or provisioning handler's own statements, an `[ApiRoute]` service and a batch outside any frame. This interface's members are the **caller channel**: a `Save`, `Update`, `Delete` or `Sql` whose declared writes include a table that spec 0014's `IStackRegistry.OwnerOf` maps to a stack outside the set is refused at append with `InvalidOperationException` naming the table, its owner and the enlistment remedy; a table no stack owns always passes. The platform's own composers — the emitter, the access guards, the blob effect and the job, notification and progress statements — append through an **internal channel** of `Tellma.Core`'s batch implementation that the check does not cover. |
| `Query` | Compiles the entity query for `TEntity` (every mapped leaf is a root; `TEntity` may be a base, resolved to its leaf through `IEntityMetadataProvider.Get`), appends it with its child and related queries, and materialises entities plus a `RelatedEntities` dictionary after execution (§11.5). |
| `Rows` | Appends one compiled `QuerySpec`; `Skip`/`Take` are parameter slots; returns a `RowPage`: the columnar `QueryRowSet` with the capped count and the ancestor rows its options ask for (§3.3, §11.3). |
| `Count` | `SELECT COUNT(*) FROM (<body with Select = "Id", OrderBy = "Id", Take = cap + 1>) AS q`; a result of `cap + 1` means "more than `cap`". |
| `Save` | Appends the emitter's statements for the root type and its supplied children (§8) and returns a `SaveHandle`: the receipt, read after execution, and the batch-local names the emitter declared — `SavedIds` (`@tb{b}_saved`), `NewIds` (`@tb{b}_new`), `TouchedIds` (`@tb{b}_touched`) and every blob capture of §8.5 in `Captures` — for the statements composed after it; `Override` disables the stamp comparison, never the existence check. |
| `Update` | One stamped `UPDATE` over the captured keys — `Ids` conjoined with `Filter`, or the query capped and count-verified exactly like delete-by-query (§9.1); assigned properties are editable or server-owned, never write-once; tree tables append the recount. |
| `Delete` | §9.2: children first, explicit statements, never cascades. Returns a `DeleteHandle`: the receipt, read after execution, `DeletedIds` (`@tb{b}_keys`) and every blob capture of §8.5 in `Captures`. |
| `Assert` | `IF (SELECT COUNT(*) FROM (<body>) AS q) <> @tb{b}_p0 THROW @errorNumber, N'<code>', 1;` inside the transaction; `errorNumber` must be a platform number or in the distribution band (§6.3). |
| `Sql` | Raw text; the holes of the `FormattableString` become scalar parameters (`@tb{b}_p{i}`), TVPs (`@tb{b}_t{i}` for a `list<TRow>` of a `[TableType]` row type), or identifiers (`SqlIdentifier`: an object name bracket-quoted, a `Tvp`/`DeclareIdTable` name as is); `TELLMA0003` requires `Writes` on any text containing DML and refuses `EXEC`/`sp_executesql` in distribution text except the allow-listed `sp_sequence_get_range` and `sp_getapplock`; result sets are counted by `ResultSets`. |
| `SqlOptions.UserIds` | Names a batch-local table or `IdList` TVP holding the user ids a raw statement affects; required (startup gate and `TELLMA0003`) when `Writes` touches a table that carries a user-level tag rule (spec 0012's `UserVersionTagRule`), because the epilogue cannot derive affected users from raw text. `ForCaller(writes)` is the self-service sugar. |
| `Tvp` | Binds a standalone or table-derived type through `TableTypeBinder` (§6.2) and returns its parameter name for use in a later `Sql` or as a `KeySetRestriction.TableSource`. |
| `DeclareIdTable` | Declares `@tb{b}_ids TABLE ([Id] int PRIMARY KEY)` for later statements to fill and read. |
| `DependsOn` | Declares cached inputs; after a `Read` or `Validate` batch the executor applies each dependency's `VersionTagMismatchPolicy` (spec 0012); on a `Persist` batch each dependency, with its `ExpectedTag`, joins `@tm_expectedTags` for the in-transaction guard (§5.5). |
| `FromCache` | Returns the cached list (`Hit`) when its tag matches the snapshot; otherwise appends spec 0012 §4.4's capped probe after the prologue, and reports `Oversized` with no list when the probe overflows, so the consumer re-reads through an ordinary query. On a `Validate` or `Persist` batch the dependency is declared with `Rerun`, never `Refresh`, so a validator never accepts a row from a list that changed under it. |
| `BumpVersionTag` | Adds a tenant-level tag name to the epilogue's bump set for this batch only — the way a service bumps a tag no written table declares. |
| `OnCommitted` | Runs after the round trip's commit, outside the transaction, in registration order; failures are logged (`DataBatch.OnCommittedFailed`, error level, with the operation name), never thrown. |
| `ExecuteAsync` | Runs contributors, assembles the text, executes with retry, reads every result set, replaces the tenant tag snapshot (spec 0012's `IVersionTagSnapshots.Replace`) and, after a batch that carried a connect prologue, the caller's user snapshot (`IUserVersionTagSnapshots.Replace`; both in `BatchOutcome`, spec 0012 §2.5), fires `OnCommitted`. A batch executes once; a second call throws. |

### 5.3 Contributors and stages

The executor asks every registered `IDataBatchContributor` for its prologue (ascending `Order`)
before the caller's statements and for its epilogue (descending `Order`) after them. The
contributors that exist are spec 0012's tag read at `Order` 50 (the prelude `SELECT [Name], [Tag]
FROM [core].[VersionTags]`, absorbed into the connect prologue on caller batches), spec 0012's
cold settings load at `Order` 60 (absorbed likewise), spec 0013's connect prologue at `Order` 100
(`IUserConnector.Contribute`, which spec 0013's runner calls at compose time and which marks the
batch as carrying the connect prologue), and spec 0012's bump epilogue. Contributors are scoped
services; the runtime `DataBatch` exposes to `Tellma.Core`-internal contributors that mark — at
`Order` 50 and 60 a marked batch yields, an unmarked one gets the statements bare — the explicit
bump set (`BumpVersionTag` calls) and the per-column user-id sources (the emitter's rows and
every `SqlOptions.UserIds`) from which the epilogue fills `@tm_userIds_<Column>`.

Every batch executed on a caller's behalf — `Read`, `Validate` and `Persist` alike — begins with
the schema guard of §4.3 and then spec 0013's prologue and has its body wrapped in
`IF @tm_Guard = 1 BEGIN … END;` the body's result sets are present only when the guard passed,
and the reader contract of spec 0013's `ConnectResult` tells the executor which sets precede the
body. A batch created in a `System` scope through spec 0013's connector carries the `System`
prologue variant (tags, the settings load on a miss; `@tm_Guard = 1`); a `Maintenance` batch
carries spec 0012's prelude alone. The schema guard precedes every variant.

### 5.4 Ordinals, names, key capture, deduplication

**Ordinals.** Each statement receives a batch ordinal `b` in composition order (contributors'
prologue statements first). Queryex statements compile with `BatchOrdinal = b` (`@qx{b}_…`);
every other statement names its scalars `@tb{b}_p{n}`, its TVPs `@tb{b}_t{n}` and its table
variables `@tb{b}_<word>`. A batch may declare at most 2,000 parameters in total (a TVP counts as
one; SQL Server's command limit is 2,100 and the prologue reserves the rest); exceeding it is an
`InvalidOperationException` at compose time — a platform defect, never a user error, because
lists ride TVPs.

**Key capture.** Any `Query`/`Rows` statement may capture its root keys: the batch compiles a
keys-only twin of the spec (`Select = "Id"`, same filter, ordering and paging) into
`INSERT INTO @tb{b}_keys ([Id]) <body>` using `CompiledQuery.Body` (§11.2), then compiles the
display query restricted by `KeySetRestriction("Id", "@tb{b}_keys")` with the ordering kept and
paging dropped. Later statements name `@tb{b}_keys` as a restriction source: child collections
(`ParentKey IN`), ancestors, the row-level-security post-check, delete-by-query, update-by-query.
`Query` captures when `Children` is non-empty and no id restriction already fixes the roots; `Rows`
captures when `CaptureKeys` or `IncludeAncestors` is set; deletes and updates always capture.

**Deduplication.** `Query`, `Rows` and `Count` compute a structural key (root, select text, filter
tree, having, order, skip, take, arguments by value, restrictions, options) and return the existing
handle for an identical key; `Sql` deduplicates on (text, parameter values). A select that is a
subset of another is not merged.

### 5.5 The persist frame

For a `Persist` batch on a caller's behalf the executor emits, in this order:

1. `SET NOCOUNT ON;`, the schema guard of §4.3 (`@tm_schema`), and the contributors' prologue
   statements (spec 0013's connect prologue, `@tm_` names, autocommit).
2. `IF @tm_Guard = 1 BEGIN`.
3. `SET XACT_ABORT ON; BEGIN TRAN;`.
4. The version-tag guard of spec 0012 §2.5, emitted by the executor from
   `@tm_expectedTags : VersionTagList` (always `permissions` and `settings` on a caller batch,
   plus every `DependsOn` dependency with its `ExpectedTag`, `Bumped = 1` on the names this
   batch's written tables and `BumpVersionTag` calls resolve to) and the caller's
   `UserStamps.PermissionsTag`; its lock hints hold to `COMMIT` (`THROW 50412,
   N'StaleVersionTag', 1`).
5. `IF NOT EXISTS (SELECT 1 FROM [core].[Users] WHERE [Id] = @tm_UserId AND [IsActive] = 1)
   THROW 50401, N'CallerInvalid', 1;`.
6. The caller's statements in composition order — spec 0014's persist assembly: spec 0013's
   `IAccessGuards.ContributeLock` (the application lock), the emitter's statements per table (§8)
   and the tree statements (§10), the service's and then every effect's `ContributeAsync`
   statements (spec 0016's blob confirm/release among them), spec 0013's
   `IAccessGuards.ContributeInvariants`, the row-level post-check over `@tb{b}_saved`, the
   read-back.
7. The epilogue: spec 0012's bumps over `@tm_tagNames` and `@tm_userIds_<Column>`, last before
   `COMMIT`; the executor binds one `@tm_tag uniqueidentifier` per batch from an
   application-generated `Guid`.
8. `COMMIT;` — asserted to be the last statement inside the transaction — then `END;`.

A `Read` or `Validate` batch has steps 1, 2, 6 and `END;` only; a `Maintenance` batch with writes
has step 1 with the prelude alone (§5.3), then steps 3, 6, 7 (bumps for declared writes) and 8
without the guard. Result sets preceding a `THROW` are read before the exception surfaces, so a
failed persist still returns the prologue's connect row to the runner.

### 5.6 Round-trip budgets

The budgets the fixture tier asserts (permissions cached, warm id buffer, prologue riding the
first round trip):

| Operation | Round trips | What rides each |
|---|---|---|
| Grid page (flat or tree, capped count, ancestors) | 1 | prologue, key capture, page rows, count, ancestors |
| Details by id (entity, children, related, row echo) | 1 | prologue, root query by ids, one child query per collection, related sets |
| Create (no context loads) | 1 | prologue, persist frame |
| Update (with children) | 2 | (1) prologue, before images, `Save`-grant count, validation context, id reservation; (2) persist frame |
| Delete by ids / by query / with descendants | 1 | prologue, keys, checks, child deletes, root delete, recount, bumps |
| Activate / deactivate (spec 0014's SQL-only fast path; a custom action is 2, as update) | 1 | prologue, stamped update, recount, bumps |
| Get by parent ids | 1 | one query restricted by a TVP |
| Import, per chunk of `MaxSaveCount` roots (spec 0014; 10,000 by default) | 2 (+1 hydration) | as update |

A cold or stale path adds one prologue-only round trip per user per instance; each dependent
validation round adds one (spec 0014's `MaxValidationRounds`, 3, counts round 1, so at most two).

## 6. Execution

### 6.1 The command

One round trip is one `SqlCommand` (`CommandType.Text`) on a connection from spec 0010's scoped
`ITenantConnectionProvider.OpenAsync()` (the factory's `OpenAsync(tenantId)` in job scopes and the
migrator): one `SqlParameter` per scalar, typed from the slot's store type (spec 0008's
`QueryexParameterSlot` for engine slots, the CLR value's mapping for `Sql` holes);
`SqlDbType.Structured` with the physical UDTT name per TVP, bound as a streaming
`IEnumerable<SqlDataRecord>` (§6.2) that is re-enumerated on retry; the concatenated text; the
cancellation token of the scope. Result sets are walked with `NextResult()` and handed to the
statements in ordinal order; every statement declares its result-set count, and a mismatch at the
end is an `InvalidOperationException` (a platform defect). `CommandTimeout` is
`DataOptions.CommandTimeout` (30 s) for `Read`/`Validate`/`Persist` and
`DataOptions.MaintenanceCommandTimeout` (600 s) for `Maintenance`.

```csharp
// Tellma.Core.Data — bound from Tellma:Data
public sealed class DataOptions
{
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan MaintenanceCommandTimeout { get; set; } = TimeSpan.FromSeconds(600);
    public int RetryAttempts { get; set; } = 3;                                    // re-runs after the first attempt: four attempts in all
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(50);   // 50, 200, 800 ms with ±25 % jitter
    public TimeSpan SlowRoundTripThreshold { get; set; } = TimeSpan.FromSeconds(5);
    public int IdBufferLowWater { get; set; } = 64;
    public int LargeBatchThreshold { get; set; } = 1000;
    public int ConflictIdsInMessage { get; set; } = 100;                            // upper bound per id list of the 50409 message; the effective cap is §8.2's
}
```

`DataOptions` is validated at startup (`ValidateOnStart`): `IdBufferLowWater` positive;
`RetryAttempts` ≥ 0; `RetryBaseDelay`, both timeouts, `SlowRoundTripThreshold`,
`LargeBatchThreshold` and `ConflictIdsInMessage` positive.

Every round trip is one OpenTelemetry span (the SqlClient instrumentation's `db.*` attributes)
carrying `tellma.db.role = tenant | catalog`, `tellma.data.purpose`, `tellma.data.operation` and
`tellma.data.attempt`; the tenant id is a span attribute and a log-scope value, never a metric tag.
The span's display name is the scope's operation and the purpose in lower case —
`gl.Center:save persist`, `job:core.import maintenance` — set by the executor when the command
starts, so a trace viewer names every round trip without the query summary the instrumentation
would otherwise derive by tokenising the whole batch text.

A round trip whose duration exceeds `SlowRoundTripThreshold` logs `DataBatch.SlowRoundTrip` at
Information level with the operation, the purpose, the attempt, the duration, the statement count,
the rows bound through TVPs, the rows read, and the full text of the batch. The batch text reaches
logs through this event alone and is never a span attribute, so the text of a slow round trip is
recoverable without putting every statement of every request into the trace.

### 6.2 Binding table-valued parameters

`TableTypeBinder` (`Tellma.Core.Data`, marked `[TableTypeBinder]` for `TELLMA0001`) builds
`SqlMetaData[]` once per spec 0001 `TableTypeDefinition` from its ordered `Columns` (store type,
size, precision, scale) and yields `SqlDataRecord`s lazily from the row list through compiled
accessors keyed by column name. `[JsonColumn]` strings bind as text; enums bind through their
string converter; `ModifiedAt` binds with seven fractional digits as `SqlDbType.DateTimeOffset`;
the shadow `Node` and the period columns are absent from every entity UDTT; a `long` key binds as
`bigint`. Standalone types bind through the same binder from their `[TableType]` class. Nothing
constructs a `SqlDataRecord` or calls `GetOrdinal` outside the binder.

### 6.3 Error numbers

```csharp
// Tellma.Core.Abstractions.Data
public static class TellmaSqlErrors                     // THROW numbers, severity 16, state 1; the message is a stable code
{
    // platform band 50400–50599:
    public const int CallerInvalid = 50401;
    public const int RowSecurity = 50403;
    public const int NotFound = 50404;
    public const int Concurrency = 50409;
    public const int StaleVersionTag = 50412;
    public const int LimitExceeded = 50413;
    public const int Invariant = 50422;
    public const int CountMismatch = 50428;
    public const int SchemaBehind = 50501;
    public const int Transient = 50503;
    // distribution and pack band 50600–50699:
    public const int DistroMin = 50600;
    public const int DistroMax = 50699;
}
```

| Number | Raised by | Message | Executor mapping |
|---|---|---|---|
| `50401` | the persist frame's caller re-check (§5.5) | `CallerInvalid` | `BatchAssertionFailedException(50401, "CallerInvalid")`; spec 0013's runner raises `TenantNotFoundException` |
| `50403` | the action-grant checks of delete-by-ids and delete-with-descendants (§9.2) and of the all-or-nothing `Update` (§9.1), the post-check (spec 0014) | `RowSecurity` | `RowSecurityException` |
| `50404` | the existence checks under `ReadFilter` of delete-by-ids and delete-with-descendants (§9.2) and of the all-or-nothing `Update` (§9.1); a hidden id counts as missing | `Entity.NotFound`; the missing ids are the result set preceding the `THROW` | `BatchAssertionFailedException(50404, "Entity.NotFound")` → `NotFoundException` over that result set |
| `50409` | the concurrency guard (§8.1), delete-by-ids with stamps (§9.2) | JSON `{"count","conflicts","missing"}`; the conflict rows are the result set preceding the `THROW` | `ConcurrencyConflictException(Conflicts)` over that result set; all-missing → `NotFoundException` (404), otherwise `ConcurrencyException` (409) |
| `50412` | the version-tag guard (§5.5) | `StaleVersionTag` | `BatchAssertionFailedException(50412, …)`; spec 0013's runner re-connects cold and recomposes once, then raises `StaleContextException` |
| `50413` | delete-by-query and update-by-query above `Cap` (§9.1, §9.2) | `Delete.CapExceeded`, `Update.CapExceeded` | `BatchAssertionFailedException(50413, …)` → `LimitExceededException` |
| `50422` | invariants: `Tree.Cycle` (§10.2), `Access.*` (spec 0013), `Blob.NotAttachable` and `Blob.StagingQuotaExceeded` (spec 0016), `Job.LeaseLost`, `Schedule.TickLeaseLost` (spec 0019), `VersionTag.Missing` (spec 0012) | the validation code | `TreeCycleException` when the code is `Tree.Cycle`; otherwise `BatchAssertionFailedException(50422, code)` → `ValidationException` with the code, except `Blob.StagingQuotaExceeded`, which spec 0016's `BlobService` translates to `BlobRejectedException` |
| `50428` | delete-by-query and update-by-query count verification (§9.1, §9.2) | `Delete.CountMismatch`, `Update.CountMismatch` | `BatchAssertionFailedException(50428, …)` → `CountMismatchException` |
| `50501` | the schema guard (§4.3) | `SchemaBehind` | `BatchAssertionFailedException(50501, "SchemaBehind")` → spec 0010's `TenantUnavailableException` (`tenant-schema-behind`, 503, `Retry-After: 30`) |
| `50503` | spec 0013's application-lock timeout (`Access.LockTimeout`) | the code | a reported transient failure: retried (§6.5), then `DataAccessRetryExhaustedException` → `DependencyUnavailableException` |
| `50600–50699` | distribution and pack guards through `Assert`/`Sql` | the validation code | `BatchAssertionFailedException(number, code)` → `ValidationException` with the message as the code |

No number outside these two bands may be thrown from platform or distribution SQL; `TELLMA0003`
refuses a `THROW` literal outside them in distribution text and the fixture tier asserts the
executor rejects an unknown `50xxx` as an `InvalidOperationException`.

### 6.4 Provider errors

| SQL error | Mapping |
|---|---|
| 2627 on `PK_<Table>` of a table the allocator serves | the sequence heal (§7.4), once; then `UniqueConstraintViolationException` |
| 2601 / 2627 on any other index | `UniqueConstraintViolationException(IndexName)`; the pipeline maps the index to the property through `EntityMetadata.UniqueIndexes`, never through the message text |
| 547 | `ForeignKeyViolationException(ConstraintName)`; the pipeline maps `FK_<Table>_<Column>` to `Fk.NotFound` on save and `Fk.InUse` on delete |
| 530 | `TreeDepthExceededException(EntityType)` → `ValidationException(Tree.TooDeep)` |
| 1205, 1222, 4060, 10928, 10929, 40197, 40501, 40613, 49918–49920, `THROW 50503` | reported transient (§6.5) |
| 233, 64, −2, 10053, 10054, any failure after the command was sent with no server response | ambiguous (§6.5) |
| every other number | final; surfaces as `SqlException` wrapped in `DataAccessException` (internal base) → 500 |

Internal data-layer exceptions (`Tellma.Core.Data`, never mapped by the web layer, translated by
spec 0014's pipeline): `DataAccessException` (base),
`UniqueConstraintViolationException(IndexName)`, `ForeignKeyViolationException(ConstraintName)`,
`TreeCycleException(EntityType)`, `TreeDepthExceededException(EntityType)`, `RowSecurityException`,
`ConcurrencyConflictException(Conflicts)` — one `ConflictRow(Id, IsMissing, ModifiedAt,
ModifiedById, Name, Name2, Name3)` per row of the guard's result set —
`BatchAssertionFailedException(Number, Code)`, `DataAccessRetryExhaustedException(Inner, Attempts)`,
`DataAccessAmbiguousException(Inner, Verdict)`. Every exception one statement raises — the seven
from `UniqueConstraintViolationException` to `BatchAssertionFailedException` — carries
`StatementOrdinal`, the batch ordinal (§5.4) of that statement, which the executor resolves from
`SqlException.LineNumber` against the line offsets it records per statement at assembly; spec 0014
§13.3 attributes a persist-time error to the enlisted group whose statements hold the ordinal.

### 6.5 Retry and the commit probe

Retry is the executor's job: SqlClient's own retry logic is inert inside a transaction and
`SqlBatch` has no retry provider.

- **Reported transient failure** (the server returned one of §6.4's transient numbers; under
  `XACT_ABORT ON` the transaction is rolled back): the whole round trip is re-run, regardless of
  idempotence, up to `RetryAttempts` times with the jittered delays of §6.1; TVPs re-enumerate;
  the prologue re-runs with the same premises. Exhaustion → `DataAccessRetryExhaustedException`.
- **Ambiguous failure** (the connection dropped; whether `COMMIT` ran is unknown): re-run only
  when every statement is `Idempotent` (`Query`, `Rows`, `Count`, `FromCache` and `Assert` are
  idempotent by construction; `Save`, `Update`, `Delete` are not; `Sql` declares). Otherwise, for
  a `Persist` batch that inserted rows, the **commit probe**: one autocommit round trip `SELECT
  COUNT(*) FROM <root table> WHERE [Id] IN (SELECT [Id] FROM @tb0_t0)` over the inserted root ids
  of the first root table. All present → `Verdict = Committed`: the write succeeded and the
  results are lost, surfaced as `DataAccessAmbiguousException(Committed)`, which spec 0014's
  pipeline answers by re-issuing the read-back on a `Read` batch and running its post-commit
  effects. None present → `NotCommitted`: the round trip is re-run (nothing partial exists under
  one transaction). No inserted rows to probe (an update-only batch) → `Unknown` →
  `DataAccessAmbiguousException(Unknown)` → spec 0014's `DependencyUnavailableException` with a
  reload instruction. Metered `tellma.data.commit.probes` by `outcome`.
- **`TransactionMode.None`** batches follow the reported-transient rule of the first bullet — the
  whole round trip is re-run even though statements that autocommitted before the failure then run
  twice, so a `None` batch's composer must tolerate that (spec 0019's stray-claim reconciliation) —
  and are re-run after an ambiguous failure only when every statement is `Idempotent`.
- **`TransactionMode.Explicit`** batches are never re-run in either class: a transient error has
  already rolled back the explicit transaction on the server, so the error surfaces at once and
  the caller re-runs its whole unit of work.
- **Every other error** is final and never retried.

`tellma.data.retries` counts every re-run by `class` and `error`.

## 7. Id allocation

### 7.1 Contract

```csharp
// Tellma.Core.Abstractions.Data
public interface IIdAllocator                           // singleton
{
    IdReservation Reserve(IDataBatch batch, Type entityType, IReadOnlyList<object> rows);
    Task<IReadOnlyList<object>> TakeAsync(Type entityType, int count);
}

public sealed class IdReservation : IDisposable         // disposable hold
{
    public bool IsImmediate { get; }                    // true when the buffer covered the deficit; Assign may run before ExecuteAsync
    public void Assign(IReadOnlyList<object> rows);     // assigns Id to every new row; rewrites temporary ids in self-typed FKs and child parent keys
    public void Dispose();                              // returns every range the reservation took when no persist was attempted; after a persist attempt nothing returns
}
```

### 7.2 Reservation

The allocator keeps a buffer of contiguous ranges per `(TenantId, sequence)`. `Reserve` computes
the exact deficit per table (rows with `Id <= 0`, roots and supplied children, recursively), takes
what the buffer holds — those ranges leave the buffer at `Reserve` time, so concurrent saves on one
instance never double-count — and, for every sequence still short, appends one statement to the
batch it is given (the pipeline's first round trip) with a range size of the remaining deficit plus
the refill to the low-water mark:

```sql
DECLARE @tb1_first sql_variant, @tb1_last sql_variant;
EXEC sys.sp_sequence_get_range @sequence_name = N'[gl].[sq_Centers]', @range_size = @tb1_p0,
     @range_first_value = @tb1_first OUTPUT, @range_last_value = @tb1_last OUTPUT;
SELECT CAST(@tb1_first AS int) AS [First], CAST(@tb1_last AS int) AS [Last],
       (SELECT MAX([Id]) FROM [gl].[Centers]) AS [MaxId];
```

`sp_sequence_get_range` needs only `UPDATE` on the sequence (granted to `tellma_app` by spec 0010)
and generates values outside the transaction. Every batch the executor runs may additionally carry a
refill statement for any sequence whose table is in the batch's `WrittenTables` and whose buffer is
below `IdBufferLowWater`, so id allocation adds no round trip to a create. The buffer is not capped:
a returned range re-enters in full, because a range that returns was never observed outside the
process and dropping it would only widen the gap; `IdBufferLowWater` sizes refills and nothing else,
so the buffer holds at most the largest reservation ever returned.

### 7.3 Assignment

Ids are assigned after the reservation's round trip (immediately when `IsImmediate`), **before
validators run**, so validators see final ids and cross-references. `Assign` walks the payload in
topological order (roots, then each child collection, recursively), assigns `Id` to every new row,
and rewrites every temporary id (`Id < 0`, unique within the payload) wherever it appears: a
self-typed foreign key (`ParentId` of a tree row pointing at a sibling new row), a child's parent
key, and any `[ParentKey]` of a grandchild. A temporary id referenced but never defined is
`Entity.NotFound` at the referencing path (reported by the pipeline). A validation failure disposes
the reservation before any persist, and every range it took returns to the buffer: the payload
that carried the assigned ids is discarded, so nothing external saw them; a persist attempt — even
one that fails — never returns ids. `TakeAsync` serves callers outside any
batch (a job handler of spec 0019 or 0020 assigning ids to rows it inserts through `Sql` outside
the pipeline) from the buffer, or with one dedicated autocommit round trip when the buffer is
empty, metered `tellma.data.ids.refills`. System-written rows inserted by fixed-text statements
(`core.Jobs`, `core.Notifications`, `core.Blobs`) take their ids inside the statement from the
table's sequence — `sp_sequence_get_range` with `Id = first + ordinal` for a row list, and
`NEXT VALUE FOR` for a single row — and never touch the buffer, so `INotifier.Notify` and
`IJobQueue.Enqueue` are synchronous and spec 0016's staging has no allocator dependency.

### 7.4 Self-healing

A sequence falls behind its table only through an out-of-band insert. Two detections, one cure:

- At reservation, `First <= MaxId`: the range is discarded and one extra round trip calls
  `sp_sequence_get_range` with `@range_size = (MaxId − First + 1) + deficit + IdBufferLowWater`,
  keeping only the tail above `MaxId`.
- At persist, error 2627 whose violated constraint is `PK_<Table>` of a table the allocator
  serves: the same cure runs once, the in-memory rows are reassigned (parent keys rewired), and
  the persist is retried once; a second 2627 surfaces as `UniqueConstraintViolationException`.

Consuming the gap needs `UPDATE` on the sequence, never `ALTER`. A 2601/2627 on any other index is
never a heal. Every heal logs `IdAllocator.Healed` at Warning and increments
`tellma.data.ids.healed` (tag `sequence`), which is alerted on. Gaps arise only from process
crashes with buffered leftovers, from a reservation whose round trip failed after the procedure
ran, and from the engine's sequence cache on an abnormal shutdown; a distribution wanting a
smaller cache issues `ALTER SEQUENCE … CACHE n` in a raw migration, because EF cannot emit `CACHE`.

## 8. The save emitter

### 8.1 Statements

`Save<TEntity>(rows, concurrency)` names the root type and the rows with their child collections
populated or `null`. Per table in topological order (roots first, then each supplied child
collection, recursively) the emitter binds two TVPs of the table's own UDTT — `@tb{b}_t{i}` new
rows (ids assigned), `@tb{b}_t{i+1}` existing rows — plus, per child collection, an `IdList` TVP of
the parents whose collection was supplied. Scalars: `@tb{b}_p0` override flag (`bit`; 1 for
`Override`), `@tb{b}_p1` the current user id. Three batch-local id sets per root table, with fixed
meanings: `@tb{b}_new` (every inserted root id), `@tb{b}_saved` (every root id in the payload, new
and existing — the set the row-level post-check runs over), `@tb{b}_touched` (parents of
synchronised children, with the collection and the operation). Exact text for a root with one
child collection (`core.Users` with `core.RoleMemberships`), ordinal 3:

```sql
DECLARE @tb3_now datetimeoffset(7) = SYSUTCDATETIME();
DECLARE @tb3_new TABLE ([Id] int NOT NULL PRIMARY KEY);
DECLARE @tb3_saved TABLE ([Id] int NOT NULL PRIMARY KEY);
DECLARE @tb3_touched TABLE ([Id] int NOT NULL, [Collection] tinyint NOT NULL, [Op] char(1) NOT NULL);
DECLARE @tb3_conflicts TABLE ([Id] int PRIMARY KEY, [Reason] char(1) NOT NULL);
INSERT INTO @tb3_saved ([Id]) SELECT [Id] FROM @tb3_t0 UNION SELECT [Id] FROM @tb3_t1;   -- before any DML

-- 1. Concurrency guard: U locks on every existing root held to COMMIT; a missing row conflicts even under override.
INSERT INTO @tb3_conflicts ([Id], [Reason])
SELECT s.[Id], CASE WHEN t.[Id] IS NULL THEN 'M' ELSE 'C' END
FROM @tb3_t1 AS s
LEFT JOIN [core].[Users] AS t WITH (UPDLOCK, ROWLOCK) ON t.[Id] = s.[Id]
WHERE t.[Id] IS NULL OR (@tb3_p0 = 0 AND t.[ModifiedAt] <> s.[ModifiedAt]);
IF EXISTS (SELECT 1 FROM @tb3_conflicts)
BEGIN
    SELECT c.[Id], c.[Reason], t.[ModifiedAt], t.[ModifiedById], u.[Name], u.[Name2], u.[Name3]   -- result set: the conflict rows, read before the THROW surfaces
    FROM @tb3_conflicts AS c
    LEFT JOIN [core].[Users] AS t ON t.[Id] = c.[Id]
    LEFT JOIN [core].[Users] AS u ON u.[Id] = t.[ModifiedById];
    DECLARE @tb3_msg nvarchar(2048) = N'{"count":' + CAST((SELECT COUNT(*) FROM @tb3_conflicts) AS nvarchar(10))
        + N',"conflicts":[' + ISNULL((SELECT STRING_AGG(CAST([Id] AS nvarchar(20)), ',') FROM (SELECT TOP (90) [Id] FROM @tb3_conflicts WHERE [Reason] = 'C' ORDER BY [Id]) AS c), N'')   -- TOP (n) is the per-list cap of §8.2: 90 for an int key
        + N'],"missing":['   + ISNULL((SELECT STRING_AGG(CAST([Id] AS nvarchar(20)), ',') FROM (SELECT TOP (90) [Id] FROM @tb3_conflicts WHERE [Reason] = 'M' ORDER BY [Id]) AS m), N'')
        + N']}';
    THROW 50409, @tb3_msg, 1;
END;

-- 2. Insert new roots; audit columns stamped here; other server-owned columns come from the reset in-memory row; write-once columns come from the payload; IsActive is written as 1.
INSERT INTO [core].[Users] ([Id], [Subject], [Email], [Name], [Name2], [Name3], [IsActive],
    [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
OUTPUT inserted.[Id] INTO @tb3_new ([Id])
SELECT s.[Id], s.[Subject], s.[Email], s.[Name], s.[Name2], s.[Name3], s.[IsActive],
    @tb3_now, @tb3_p1, @tb3_now, @tb3_p1
FROM @tb3_t0 AS s;

-- 3. Synchronise each supplied child collection (parents @tb3_t2 : IdList; new children @tb3_t3; existing @tb3_t4); collection ordinal 0.
DELETE c OUTPUT deleted.[UserId], 0, 'D' INTO @tb3_touched ([Id], [Collection], [Op])
FROM [core].[RoleMemberships] AS c
WHERE c.[UserId] IN (SELECT [Id] FROM @tb3_t2)
  AND NOT EXISTS (SELECT 1 FROM @tb3_t3 AS s WHERE s.[Id] = c.[Id])
  AND NOT EXISTS (SELECT 1 FROM @tb3_t4 AS s WHERE s.[Id] = c.[Id]);
UPDATE c SET c.[RoleId] = s.[RoleId], c.[Notes] = s.[Notes]
OUTPUT inserted.[UserId], 0, 'U' INTO @tb3_touched ([Id], [Collection], [Op])
FROM [core].[RoleMemberships] AS c JOIN @tb3_t4 AS s ON s.[Id] = c.[Id]
WHERE c.[UserId] IN (SELECT [Id] FROM @tb3_t2)
  AND EXISTS (SELECT s.[RoleId], s.[Notes] EXCEPT SELECT c.[RoleId], c.[Notes]);
INSERT INTO [core].[RoleMemberships] ([Id], [UserId], [RoleId], [Notes])
OUTPUT inserted.[UserId], 0, 'I' INTO @tb3_touched ([Id], [Collection], [Op])
SELECT s.[Id], s.[UserId], s.[RoleId], s.[Notes] FROM @tb3_t3 AS s;

-- 4. Update existing roots last: editable and derived columns only; changed or touched rows only; stamp.
UPDATE t SET t.[Name] = s.[Name], t.[Name2] = s.[Name2], t.[Name3] = s.[Name3],
    t.[ModifiedAt] = @tb3_now, t.[ModifiedById] = @tb3_p1
OUTPUT inserted.[Id]                                                          -- result set: stamped ids
FROM [core].[Users] AS t JOIN @tb3_t1 AS s ON s.[Id] = t.[Id]
WHERE EXISTS (SELECT s.[Name], s.[Name2], s.[Name3] EXCEPT SELECT t.[Name], t.[Name2], t.[Name3])
   OR t.[Id] IN (SELECT [Id] FROM @tb3_touched);

SELECT @tb3_now AS [Stamp],
       (SELECT COUNT(*) FROM @tb3_touched WHERE [Op] = 'U') AS [ChildUpdated],
       (SELECT COUNT(*) FROM @tb3_touched WHERE [Op] = 'D') AS [Deleted];      -- result set: the receipt
```

The receipt is read from the result sets: `Stamp` and `Deleted` from the receipt row; `Inserted`
from the new-rows TVP sizes (roots and children); `Updated` from the row count of step 4's
stamped-ids set plus the receipt row's `ChildUpdated`; step 4's stamped ids stay internal to the
emitter, which uses them to stamp the in-memory entities: new roots receive all four audit
columns, stamped roots receive `ModifiedAt`/`ModifiedById`.

### 8.2 Rules

- **Ownership.** `ServerOwned` and `WriteOnce` columns never appear in a `SET` list (§2.4). The
  `INSERT` writes audit columns from the statement and every other server-owned column from the
  reset in-memory row; write-once columns come from the payload; derived columns come from the
  in-memory row as the pipeline's hooks left it (the preprocess hook, or a validator whose value
  needs the before image), on `INSERT` and in every `SET` list, and count as editable columns in
  the unchanged-row comparison; database-owned columns appear in no list and come back in the
  read-back.
- **The expected stamp** is the client's `ModifiedAt`, carried in the existing-rows TVP's own
  column; `Override` disables the comparison, never the existence check — override rewrites, it
  never resurrects. A default stamp on an update under `Check` is refused before the batch
  (`Concurrency.StampRequired`, spec 0014).
- **The guard takes U locks** (`UPDLOCK, ROWLOCK`) on every existing root and holds them to
  `COMMIT`, so the stamp comparison is sound under both locking read committed and read-committed
  snapshot (a locking hint reads the latest committed row). Two saves of overlapping sets can
  deadlock; 1205 is retried (§6.5).
- **Unchanged rows are skipped.** `EXCEPT` over the editable columns treats `NULL = NULL`, so a
  temporal table gets no history row for a no-op; a `[Temporal]` child's unchanged rows are
  skipped likewise. A child change marks its parent touched; a touched or changed root is stamped.
  The root's `ModifiedAt` moves exactly when the aggregate changed.
- **The stamp is server time**, `SYSUTCDATETIME()` once per batch: instances skew, one database
  clock per tenant does not.
- **Children are scoped to their parents.** A child row whose parent is not in the parents TVP is
  neither deleted, updated nor re-parented; `[]` deletes every child of the supplied parents; a
  `null` collection binds an empty parents TVP and touches nothing.
- **The conflict rows** are a result set the guard selects before its `THROW`: `Id`, `Reason` (`'C'`
  a stamp mismatch, `'M'` missing), the stored `ModifiedAt` and `ModifiedById`, and the modifier's
  `Name`, `Name2` and `Name3`; the executor reads it before the exception surfaces and raises
  `ConcurrencyConflictException(Conflicts)` over it, so the pipeline needs no second read. The
  `THROW` message is JSON for logs — `count`, then the `conflicts` and `missing` id lists — and fits
  `THROW`'s `nvarchar(2048)` message: the emitter caps each list at
  `min(ConflictIdsInMessage, floor((2048 − 48) / 2 / (digits + 1)))`, where 48 is the fixed JSON
  text around a ten-digit count and `digits` is the key type's maximum digit count, so the cap is 90
  for an `int` key and 50 for a `bigint` key. A `50409` whose conflicts are all `'M'` surfaces as
  `NotFoundException` — a row deleted under the caller is not a concurrency conflict — and as
  `ConcurrencyException` (carrying `IsMissing` per id) when any `'C'` is present.
- **Plan lanes.** Above `LargeBatchThreshold` (1,000 rows in any TVP of the batch) every DML
  statement gets `OPTION (RECOMPILE)`, so a 10,000-row import does not reuse the one-row plan.
- **Import chunks** at `MaxSaveCount` root rows per round-trip pair (spec 0014 §2.5; 10,000 by
  default); `SqlBulkCopy` into a staging table is deferred until measured.
- **Tree tables** append §10.2's statements after step 4.
- **Text is cached** per (entity metadata, statement kind, capture layout); the physical UDTT
  names are part of the text, so a model change invalidates it.
- **`OUTPUT` targets are table variables** and every `OUTPUT` to the client comes from a table
  without triggers (no logic in the database).

### 8.3 Nested children

A grandchild collection repeats step 3 under its parent's synchronise block: its parents TVP is
the `IdList` of the child rows whose grandchild collection was supplied, its new and existing TVPs
are the grandchild's UDTT, and its `OUTPUT` marks the *child* touched (which in turn marks the
root touched through the child's `UPDATE` where a column changed, or directly through the touched
set). Child collections are processed in metadata order; `[Collection]` is the position of the
collection in `EntityMetadata.Children` of its owner.

### 8.4 The capture rule

SQL Server allows one `OUTPUT … INTO` and one plain `OUTPUT` per statement, so every DML statement
feeds at most one batch-local table directly. A statement that must feed two sets — a
`[BlobReference]` capture beside `@tb{b}_new` or `@tb{b}_touched` — outputs into a per-statement
capture table `@tb{b}_cap{n} ([Id] int NOT NULL, [ParentId] int NULL, [Op] char(1) NOT NULL,
[Old_<Column>] int NULL, [New_<Column>] int NULL, …)` carrying every captured column, and the sets
are filled from it by `INSERT … SELECT` immediately after the statement.

### 8.5 Blob capture

For every `[BlobReference]` column of every table the emitter writes (root, child and grandchild;
a save, and a delete by ids, by query or with descendants) the emitter declares, before the
table's statements, the capture table `@tb{b}_blob_<Schema>_<Table>_<Column>` with the columns
`[RowId] int NOT NULL`, `[OldBlobId] int NULL` and `[NewBlobId] int NULL`, and fills it — directly
when the statement feeds nothing else, through `@tb{b}_cap{n}` otherwise:

| Statement | `OUTPUT` clause |
|---|---|
| `INSERT` | `inserted.[Id], NULL, inserted.[<Column>]` |
| `UPDATE` | `inserted.[Id], deleted.[<Column>], inserted.[<Column>]` |
| `DELETE` (child synchronise, delete by ids, by query, with descendants) | `deleted.[Id], deleted.[<Column>], NULL` |

The column joins the `UPDATE`'s `EXCEPT` list like any editable column. The handle lists every
capture in `Captures` (§5.2) as a `ColumnCapture` carrying its table, its column and the declared
identifier, so no consumer forms the name. Spec 0016's release and confirm statements read the
capture table after the table's statements, inside the same transaction; the emitter's only
obligation is that every old and new value on every path — root, child, query-driven delete — is
captured.

## 9. Update and delete statements

### 9.1 Update

`Update<TEntity>(UpdateSpec)` is the statement every column action uses (activate, deactivate,
any state transition, spec 0019's `take-over`), by ids or by query. The keys are captured first —
`ByIds`: the ids conjoined with `Filter`, the caller's access filter; `ByQuery`: the query conjoined
with the access filter, capped and count-verified — then one stamped `UPDATE`; ordinal 5,
`gl.Centers`, assignment `IsActive = @tb5_p0`:

```sql
DECLARE @tb5_now datetimeoffset(7) = SYSUTCDATETIME();
DECLARE @tb5_keys TABLE ([Id] int PRIMARY KEY);
INSERT INTO @tb5_keys ([Id]) SELECT [Id] FROM (<compiled: Root, Select = "Id", Restrictions = [KeySetRestriction("Id", "@tb5_t0")], Filter>) AS q;
-- ByQuery instead (@tb5_p2 = Cap, @tb5_p3 = ExpectedCount):
INSERT INTO @tb5_keys ([Id]) SELECT TOP (@tb5_p2 + 1) [Id] FROM (<compiled: Root, Select = "Id", Filter conjoined with the access filter>) AS q;
IF (SELECT COUNT(*) FROM @tb5_keys) > @tb5_p2 THROW 50413, N'Update.CapExceeded', 1;
IF (SELECT COUNT(*) FROM @tb5_keys) <> @tb5_p3 THROW 50428, N'Update.CountMismatch', 1;
UPDATE t SET t.[IsActive] = @tb5_p0, t.[ModifiedAt] = @tb5_now, t.[ModifiedById] = @tb5_p1
OUTPUT inserted.[Id]
FROM [gl].[Centers] AS t
WHERE t.[Id] IN (SELECT [Id] FROM @tb5_keys) AND t.[IsActive] <> @tb5_p0;
```

`@tb5_t0` is the `IdList` of `Ids`; `ByQuery` binds no list, and its `ExpectedCount` is verified
inside the transaction exactly as delete-by-query's is, so a row that appeared or vanished between
the caller's count and the update fails the whole statement. Assigned properties must be `Editable`
or `ServerOwned` — `IsActive` of an `IActivatable` is the server-owned column the
activate/deactivate actions write, and this statement is the only path that writes it — and a
`WriteOnce` assignment is a compose-time `ArgumentException`. Rows whose values are already the
assigned ones are skipped, so a repeated activate writes no history row and moves no stamp.
`Stamp = false` omits the two stamp columns and is permitted only for platform bookkeeping on tables
without audit columns. The receipt's `UpdatedIds` are the `OUTPUT` rows; a requested id absent from
them was missing, invisible or already in the target state, which the pipeline distinguishes from
the keys table when it needs to. `AllOrNothing = true` (spec 0014's built-in
`activate`/`deactivate`) applies the delete-by-ids rule of §9.2 ahead of the `UPDATE`: the ids of
`@tb5_t0` with no row readable under `ReadFilter` are selected as a result set and
`THROW 50404, N'Entity.NotFound', 1` follows when any exist — a hidden id counts as missing; then
`THROW 50403, N'RowSecurity', 1` follows when the count of `@tb5_keys` differs from the count of
`@tb5_t0` — every id must be readable and pass `Filter`, and the `UPDATE` then covers every id
not already in the target state. Tree tables append §10.3's recount over `@tb5_keys` after the
`UPDATE`.

### 9.2 Delete

`Delete<TEntity>(DeleteSpec)` has three shapes; each captures `@tb{b}_keys`, checks, deletes
children deepest first by explicit statements, then the root, and reads the deleted ids back.
Ordinal 4, `gl.Centers`:

```sql
DECLARE @tb4_keys TABLE ([Id] int PRIMARY KEY);
-- ByIds (@tb4_t0 : IdList, or IdStampList when ExpectedStamps was given): 404 under ReadFilter before 403 under Filter, both in SQL, all-or-nothing; the ids absent or hidden are a declared result set (empty when none) so the 50404 names them.
INSERT INTO @tb4_keys ([Id]) SELECT t.[Id] FROM [gl].[Centers] AS t WITH (UPDLOCK, ROWLOCK) JOIN @tb4_t0 AS s ON s.[Id] = t.[Id]
    WHERE t.[Id] IN (SELECT [Id] FROM (<compiled: Root, Select = "Id", Restrictions = [KeySetRestriction("Id", "@tb4_t0")], ReadFilter>) AS q);   -- the WHERE is omitted when ReadFilter is null
SELECT s.[Id] FROM @tb4_t0 AS s WHERE s.[Id] NOT IN (SELECT [Id] FROM @tb4_keys);   -- result set: the missing ids, absent and hidden alike
IF (SELECT COUNT(*) FROM @tb4_keys) <> (SELECT COUNT(*) FROM @tb4_t0) THROW 50404, N'Entity.NotFound', 1;
IF (SELECT COUNT(*) FROM (<compiled: Root, Select = "Id", Restrictions = [KeySetRestriction("Id", "@tb4_keys")], Filter>) AS q) <> (SELECT COUNT(*) FROM @tb4_keys)
    THROW 50403, N'RowSecurity', 1;
-- ByIds with stamps only (IdStampList carries ModifiedAt; a NULL stamp is Concurrency.StampRequired in C# before the batch):
DECLARE @tb4_conflicts TABLE ([Id] int PRIMARY KEY);
INSERT INTO @tb4_conflicts ([Id]) SELECT t.[Id] FROM [gl].[Centers] AS t JOIN @tb4_t0 AS s ON s.[Id] = t.[Id] WHERE t.[ModifiedAt] <> s.[ModifiedAt];
IF EXISTS (SELECT 1 FROM @tb4_conflicts)
BEGIN  -- the conflict result set and @tb4_msg exactly as in §8.1 step 1 (all 'C'; 'M' is impossible after the 50404 check)
    SELECT c.[Id], 'C' AS [Reason], t.[ModifiedAt], t.[ModifiedById], u.[Name], u.[Name2], u.[Name3]   -- result set: the conflict rows
    FROM @tb4_conflicts AS c JOIN [gl].[Centers] AS t ON t.[Id] = c.[Id] LEFT JOIN [core].[Users] AS u ON u.[Id] = t.[ModifiedById];
    THROW 50409, @tb4_msg, 1;
END;
-- ByQuery: the query conjoined with Filter, capped (Cap = MaxDeleteByQueryRows), then ExpectedCount verified (@tb4_p0 = Cap, @tb4_p1 = ExpectedCount):
INSERT INTO @tb4_keys ([Id]) SELECT TOP (@tb4_p0 + 1) [Id] FROM (<compiled keys query>) AS q;
IF (SELECT COUNT(*) FROM @tb4_keys) > @tb4_p0 THROW 50413, N'Delete.CapExceeded', 1;
IF (SELECT COUNT(*) FROM @tb4_keys) <> @tb4_p1 THROW 50428, N'Delete.CountMismatch', 1;
-- WithDescendants: the ByIds check over @tb4_t0 under ReadFilter (missing-ids result set, then 50404), then the closure by node, asserted visible under Filter:
SELECT s.[Id] FROM @tb4_t0 AS s WHERE s.[Id] NOT IN (SELECT [Id] FROM (<compiled: Root, Select = "Id", Restrictions = [KeySetRestriction("Id", "@tb4_t0")], ReadFilter>) AS q);   -- result set: the missing ids, absent and hidden alike
IF EXISTS (SELECT 1 FROM @tb4_t0 AS s WHERE s.[Id] NOT IN (SELECT [Id] FROM (<compiled: Root, Select = "Id", Restrictions = [KeySetRestriction("Id", "@tb4_t0")], ReadFilter>) AS q)) THROW 50404, N'Entity.NotFound', 1;
INSERT INTO @tb4_keys ([Id])
SELECT d.[Id] FROM [gl].[Centers] AS d
WHERE EXISTS (SELECT 1 FROM [gl].[Centers] AS a JOIN @tb4_t0 AS s ON s.[Id] = a.[Id]
              WHERE d.[Node].IsDescendantOf(a.[Node]) = 1);
IF (SELECT COUNT(*) FROM (<compiled: Root, Select = "Id", Restrictions = [KeySetRestriction("Id", "@tb4_keys")], Filter>) AS q) <> (SELECT COUNT(*) FROM @tb4_keys)
    THROW 50403, N'RowSecurity', 1;
-- tree tables: old nodes captured before the delete (§10.3)
DECLARE @tb4_old TABLE ([Node] hierarchyid NOT NULL);
INSERT INTO @tb4_old ([Node]) SELECT t.[Node] FROM [gl].[Centers] AS t JOIN @tb4_keys AS k ON k.[Id] = t.[Id];
-- children deepest first, explicit (gl.Centers has none; the shape per child table, keyed by its parent key); then the root; blob capture OUTPUT … INTO where declared:
DELETE c FROM [<schema>].[<Child>] AS c WHERE c.[<ParentKey>] IN (SELECT [Id] FROM @tb4_keys);
DELETE t OUTPUT deleted.[Id] FROM [gl].[Centers] AS t WHERE t.[Id] IN (SELECT [Id] FROM @tb4_keys);
```

`ExpectedCount` for `ByQuery` is spec 0014's request member; `Cap` is its `MaxDeleteByQueryRows`.
The `WithDescendants` closure orders nothing: rows are deleted in one statement, and the
self-referencing FK is satisfied because parents and descendants leave together. A 547 on a tree
row whose children were not requested, or on any row still referenced, is
`ForeignKeyViolationException` → `ValidationException(Fk.InUse)`. No `ON DELETE CASCADE` exists
on entity tables. Tree tables append §10.3's recount after the root delete; deletes bump tags
through the epilogue like saves.

## 10. Trees

### 10.1 The node scheme

`Node` is a shadow `hierarchyid NOT NULL` whose value is the **id path**: `/15/342/1207/` for the
row `1207` under `342` under the root `15`. A row's node depends only on its ancestor chain, so
siblings never renumber, and re-parenting a subtree changes exactly that subtree. New rows are
inserted with the provisional node `/0/<Id>/` — the `INSERT` of §8.1 step 2 adds the column
expression `hierarchyid::Parse('/0/' + CAST(s.[Id] AS varchar(20)) + '/')` — which is unique and
disjoint from every real path (no real row has id 0), so `NOT NULL` and `UX_<Table>_Node` hold
until the re-path runs in the same transaction. `[Tree(MaxDepth = 32)]` caps recursion. There is
no `Level` column (Queryex `level(Node)` emits `GetLevel()`, §11.2) and no `IsLeaf`
(`SubtreeCount = 1`). `hierarchyid` never crosses the TVP boundary and never appears in
`Tellma.Core.Abstractions`; a `hierarchyid` result column is read through the EF `HierarchyId`
reader and returned as its path string (§11.3).

### 10.2 After a save

Appended after a tree table's step 4; `gl.Centers`, ordinal 3 (`@tb3_t0` new, `@tb3_t1`
existing):

```sql
-- T1. Affected set: every saved row, plus every descendant (by its pre-save node) of a saved existing row.
DECLARE @tb3_aff TABLE ([Id] int PRIMARY KEY);
INSERT INTO @tb3_aff ([Id])
SELECT [Id] FROM @tb3_t0
UNION SELECT [Id] FROM @tb3_t1
UNION SELECT d.[Id] FROM [gl].[Centers] AS d
      WHERE EXISTS (SELECT 1 FROM [gl].[Centers] AS b JOIN @tb3_t1 AS s ON s.[Id] = b.[Id]
                    WHERE d.[Node].IsDescendantOf(b.[Node]) = 1);

-- T2. Paths, top-down from affected rows whose parent lies outside the set (those parents' nodes are final).
DECLARE @tb3_paths TABLE ([Id] int PRIMARY KEY, [Path] varchar(892) NOT NULL);
WITH tb3_walk ([Id], [Path]) AS (
    SELECT t.[Id], CAST(ISNULL(p.[Node].ToString(), '/') + CAST(t.[Id] AS varchar(20)) + '/' AS varchar(892))
    FROM [gl].[Centers] AS t
    JOIN @tb3_aff AS a ON a.[Id] = t.[Id]
    LEFT JOIN [gl].[Centers] AS p ON p.[Id] = t.[ParentId]
    WHERE t.[ParentId] IS NULL OR t.[ParentId] NOT IN (SELECT [Id] FROM @tb3_aff)
    UNION ALL
    SELECT c.[Id], CAST(x.[Path] + CAST(c.[Id] AS varchar(20)) + '/' AS varchar(892))
    FROM [gl].[Centers] AS c
    JOIN @tb3_aff AS a ON a.[Id] = c.[Id]
    JOIN tb3_walk AS x ON x.[Id] = c.[ParentId]
)
INSERT INTO @tb3_paths ([Id], [Path]) SELECT [Id], [Path] FROM tb3_walk
OPTION (MAXRECURSION 32);                                                      -- [Tree(MaxDepth)]

-- T3. Cycle fence: an affected row no anchor reached lies on a cycle.
IF (SELECT COUNT(*) FROM @tb3_paths) <> (SELECT COUNT(*) FROM @tb3_aff)
    THROW 50422, N'Tree.Cycle', 1;

-- T3b. Old nodes of the affected rows, captured before any re-path (the ancestors that lose descendants).
DECLARE @tb3_old TABLE ([Node] hierarchyid NOT NULL);
INSERT INTO @tb3_old ([Node]) SELECT t.[Node] FROM [gl].[Centers] AS t JOIN @tb3_aff AS a ON a.[Id] = t.[Id];

-- T4. Re-path rows whose node changed (never stamps ModifiedAt).
UPDATE t SET t.[Node] = hierarchyid::Parse(x.[Path])
FROM [gl].[Centers] AS t JOIN @tb3_paths AS x ON x.[Id] = t.[Id]
WHERE t.[Node] <> hierarchyid::Parse(x.[Path]);

-- T5a. Recount scope: the affected rows plus every ancestor of their old and new nodes (O(|aff| × depth) rows, never the table).
DECLARE @tb3_anc TABLE ([Node] hierarchyid NOT NULL PRIMARY KEY);
WITH tb3_anc ([Node]) AS (
    SELECT [Node] FROM @tb3_old
    UNION SELECT t.[Node] FROM [gl].[Centers] AS t JOIN @tb3_aff AS a ON a.[Id] = t.[Id]
    UNION ALL SELECT x.[Node].GetAncestor(1) FROM tb3_anc AS x WHERE x.[Node].GetLevel() > 1)
INSERT INTO @tb3_anc ([Node]) SELECT DISTINCT [Node] FROM tb3_anc OPTION (MAXRECURSION 32);

-- T5b. Recount rows in scope whose counts changed (SubtreeCount only on a non-activatable tree).
UPDATE t SET t.[SubtreeCount] = x.[Cnt], t.[ActiveSubtreeCount] = x.[ActiveCnt]
FROM [gl].[Centers] AS t
JOIN @tb3_anc AS n ON n.[Node] = t.[Node]
CROSS APPLY (SELECT COUNT(*) AS [Cnt], SUM(CASE WHEN d.[IsActive] = 1 THEN 1 ELSE 0 END) AS [ActiveCnt]
             FROM [gl].[Centers] AS d WHERE d.[Node].IsDescendantOf(t.[Node]) = 1) AS x
WHERE t.[SubtreeCount] <> x.[Cnt] OR t.[ActiveSubtreeCount] <> x.[ActiveCnt];
```

The affected set is computed first because a saved row whose parent is itself a descendant of
another saved row would otherwise read a stale parent node and be pathed twice; anchors are the
affected rows whose parent lies outside the set. `MAXRECURSION` detects nothing — a cycle's members
all have parents inside the set and are unreachable from any anchor — so the fence is "every
affected row received a path". The T3 mismatch → `TreeCycleException` → `ValidationException
(Tree.Cycle)`; error 530 (a chain deeper than `MaxDepth`) → `TreeDepthExceededException` →
`ValidationException(Tree.TooDeep)`. T4 never stamps `ModifiedAt`: moving a subtree is an edit of
its root, which the emitter stamped; descendants changed no user-visible value.

### 10.3 After an update or a delete

After an `Update` action on an activatable tree the affected set is `@tb{b}_keys` and T3b, T5a
and T5b run alone (nodes are unchanged). After a delete, `@tb{b}_old` is filled from the deleted
rows' nodes before the `DELETE` (§9.2) and T5a–T5b run after it with the affected set empty (the
scope is the old nodes' ancestor chains). Every tree statement increments
`tellma.data.tree.recomputes` (tag `entity`).

### 10.4 Validation and verification

`TreeCycleValidator<T>` (`Tellma.Core.Data`, registered by spec 0014's tree recipe) runs in the
validation round trip: it loads `(Id, ParentId)` of the whole table through `Rows`, overlays the
payload's `ParentId` values (temporary ids already rewritten), walks up from every saved row, and
reports a revisit as `Tree.Cycle` on that row's `ParentId`. It also refuses a `ParentId` that is
the row's own id and a depth beyond `MaxDepth` (`Tree.TooDeep`). Under write skew the SQL fence
catches what the C# check missed; the C# check exists so the common case is a field error with a
path rather than a transaction abort.

`TreeStatements.Verify(metadata)` is the whole-table form of T5b (every row, no scope), shipped
here and run by spec 0019's built-in weekly job `core.tree-verify` (§14.5 there) over every tree
table in the model; it repairs drift and meters it
(`tellma.data.tree.repairs`, tag `entity`), and a non-zero repair count logs `TreeVerify.Repaired`
at Warning because it means a persist-time recount missed a row.

## 11. Queryex host integration

### 11.1 The schema provider

```csharp
// Tellma.Core.Abstractions.Data
public interface IQueryexSchemaProvider                 // singleton; at most three schemas per process, built from the EF model
{
    QueryexSchema GetSchema(MultilingualShape shape);
    string Fingerprint { get; }                         // changes with the model; part of the engine's cache identity
}
```

`QueryexSchemaProvider` builds one `QueryexSchema` per `MultilingualShape` value (`Primary |
PrimaryAndSecondary | All`, spec 0012) through spec 0008's `QueryexSchemaBuilder`, lazily, cached
for the process; the engine's caches are keyed on schema identity, so per-shape schemas are the
smallest key that neither leaks `Name3` to a monolingual tenant nor rebuilds per request.
`Fingerprint` is the hex SHA-256 of every entity's `(Name, Source, Key, properties, navigations)`
in model order, and is part of `EntityMetadata`'s startup validation output.

Schema rules:

- **Entities.** Every mapped leaf, children included, is a root declared under
  `EntityMetadata.Name` (§3.1) — `gl.Center`, `core.RoleMembership` — with `Source` =
  `[schema].[Table]` and `Key` = the PK property; every navigation names its target entity by that
  same name.
- **Properties.** Every mapped scalar (CLR or shadow) whose store type has a Queryex type, with
  `Column`, `IsNotNull` from the model, `IsUnique` when the PK or a single-column unique index
  (filtered included) covers it, and the structured `StoreType` from EF's type mapping (enums
  through their converter: `String` with `VarChar(n)`). Absent: `[NotMapped]` members, period
  columns, `[JsonColumn]` strings, `varbinary`, `time`, `float`/`real`, `Blob.StorageKey`,
  `Blob.Sha256`, `Job.ErrorDetails`. `P2`/`P3` of a `[Multilingual]` group are present per shape.
- **Navigations.** Every many-to-one foreign key, named by the CLR navigation when one exists and
  by the FK column minus `Id` otherwise (`ParentId` → `Parent`, `CreatedById` → `CreatedBy`,
  `UserId` → `User`, `RoleId` → `Role`, `ImageId` → `Image`); a `[ParentKey]` yields the child's
  navigation to its parent; a `[Sibling(navigation)]` entity — one row per owner, its primary key a
  foreign key to the owner — yields on the owner the named one-to-one navigation to it, emitted as
  a left join so an owner without a sibling row yields nulls (a pack's extension table, spec 0010
  §2.4). No collections.
- **Tree node.** The shadow `Node` of every tree entity, declared `HierarchyId` and registered as
  `TreeNode`, so `descendantOf`/`ancestorOf` and `level(Node)` bind.
- **Read-only roots.** `core.UserStamp` (over `core.UserStamps`, navigation `User`) and
  `core.Blob` are declared by their owning specs' entity classes and reachable as documented there.

Row-level-security composition and child-entity rebasing (`FilterTree.Via`, §11.2 item 7) are
`FilterTree` constructions at request time (spec 0013), never schema variants; a
`map<EntityDescriptor, EntityMetadata>` built beside each schema lets the materializer map
descriptors back to CLR types without an engine change. A cold instance reads the tenant's shape
from the settings load that rides the connect prologue (spec 0012) before its first compilation.

### 11.2 Engine amendments

Spec 0008 is frozen; these additive changes are documented here and `QueryexLanguage.Version`
stays 1:

```csharp
// Tellma.Core.Queryex — amendments
public sealed record KeySetRestriction(string Path, string TableSource);   // "<column> IN (SELECT [Id] FROM <TableSource>)"

public sealed record QuerySpec
{
    public IReadOnlyList<KeySetRestriction> Restrictions { get; init; } = [];   // part of the cache key by shape, never by values
}

public sealed record CompiledQuery                      // Sql = Prologue + Body
{
    public string Prologue { get; init; }
    public string Body { get; init; }
}

public abstract class FilterTree
{
    public static FilterTree Leaf(string text, int languageVersion);   // the one-argument form binds under the options' version
    public static FilterTree Via(string navigation, FilterTree inner);  // inner's root is the navigation's target
    public sealed class LeafNode : FilterTree { public int? LanguageVersion { get; } }
    public sealed class ViaNode : FilterTree { public string Navigation { get; } public FilterTree Inner { get; } }
}
```

1. **Key-set restriction.** The engine binds `Path` in Filter mode (a bare path; joins as needed),
   validates `TableSource` as a plain `@`-identifier outside the `@qx` namespace, and emits `AND
   <column> IN (SELECT [Id] FROM <TableSource>)` conjoined after the filter tree (an empty filter
   yields the restriction alone). The host binds an `IdList`, `BigIdList`, `GuidList`, `StringList`
   or `DateList` TVP by the path's key type, or names a `@tb{b}_` table with an `[Id]` column.
   Restrictions participate in the compilation cache key and in structural deduplication (§5.4).
   Consumers: get-by-ids, get-by-parent-ids, child loading, ancestors, deletes, updates,
   validation context, natural-key translation (the list type of the key's CLR type on the
   natural-key path), the job
   join load (`KeySetRestriction("JobId", "@tb{b}_claimed")`).
2. **`level(node: HierarchyId) -> Numeric`**, `NotNull` when its argument is, emitted as
   `.GetLevel()`. An additive registry entry.
3. **Prologue/body split.** `CompiledQuery.Prologue` holds the hoisted `DECLARE`s (may be empty)
   and `Body` the `SELECT`. A host embeds `Body` as a derived table (`SELECT COUNT(*) FROM
   (<body>) AS q`, legal with `ORDER BY … OFFSET … FETCH`) or after `INSERT INTO @t ([Id])`; the
   prologue is emitted earlier in the same batch. Consumers: capped counts, key capture,
   assertions, delete-by-query.
4. **`Skip`/`Take` as parameter slots** (`Origin = Declared`, names `@qx{b}_skip`/`@qx{b}_take`),
   so a page change reuses the plan.
5. **`today()` and the `TimeZone` slot bind the tenant zone** through `QueryexContextValues`
   (§5.1); `now()` binds `Now`; `me()` binds `UserId`.
6. **Versioned leaves.** Each leaf binds under its own language version, the one-argument form under
   `QueryCompilationOptions.LanguageVersion`; an unsupported version is refused at construction, as
   the options records refuse theirs; the L3 key's rendering of the tree carries each leaf's version
   beside its text. Consumers: spec 0013's evaluator, whose leaves carry each stored row's
   `FilterLanguageVersion`. Every clause a host authors — a user's filter, `SearchFilter`,
   `BespokeGrant`, a stack's declared filters — compiles under `QueryexLanguage.Version`; only
   stored text carries an older stamp, per leaf.
7. **`Via(navigation, inner)`.** `navigation` is a dotted path of to-one navigations from the
   current root (`Invoice`, `Line.Invoice`); `inner` binds with the path's target as its root:
   every path in `inner` resolves from the target, the joins ride the path, context functions bind
   as usual, and a to-one path cannot fan out, so the node is a root rebase, never a rewrite of
   filter text. A segment that is not a to-one navigation is a compile-time diagnostic.
   Diagnostics inside `inner` carry `Filter.Via[<navigation>]` locations; the L3 key renders the
   node around `inner`'s rendering; nesting is allowed. Consumers: spec 0013's evaluator (a child
   securable's owner grants, spec 0013 §5.5) and spec 0016's download resolution (spec 0016 §5.1).

A language-level list parameter (`x in (@ids)`) and `descendantOf` over a TVP remain unbuilt:
restrictions on `Id`/`ParentId` cover every consumer.

### 11.3 Rows and Count

`Rows(spec, arguments, options)` compiles the spec with `BatchOrdinal = b` and `LanguageVersion`
per §11.2 item 6, binds `Literal` slots from the compiled query, `Today`/`Now`/`UserId`/`TimeZone`
slots from `QueryexContextValues`, and `Declared` slots from `QueryArguments` (a declared parameter
bound against several store types fills every slot of that name from one value; a missing argument
is a compose-time `ArgumentException`, a composer bug — spec 0014's pipeline binds every declared
parameter, `null` when the request carries none).

SQL Server sends a result set's column metadata before its first row, even for an empty result, so
the buffers and `Columns` never depend on the data. After `ExecuteReader` and before the first
`Read`, the reader creates the `QueryRowSet.Builder` from the compiled `Columns` and the open
reader's metadata. Each `Numeric` column's `Scale` (§3.3) comes from the reader's field type, and
for a `decimal` from `DbColumn.NumericScale` in the reader's column schema, so a computed decimal
(`Sum(Amount)`) reports the scale SQL Server computed. Each column's buffer is chosen by the pair
(Queryex type, the logical target; the reader's `GetFieldType(i)`, the physical source):

| Queryex type | Field type | Buffer | Read |
|---|---|---|---|
| `Numeric` | `int` / `long` / `short` / `byte` / `decimal` / `double` / `float` | `int[]` / `long[]` / `short[]` / `byte[]` / `decimal[]` / `double[]` (a `real` widens to `double`) | the matching typed getter |
| `Bool` | `bool` or an integer | `bool[]` | `GetBoolean`, or non-zero for an integer (how Queryex emits a predicate is its concern, spec 0008 §13.2) |
| `String` | `string` | `string?[]` | `GetString` |
| `Guid` | `Guid` | `Guid[]` | `GetGuid` |
| `Date` | `DateTime` (SQL `date`) | `DateOnly[]` | `GetFieldValue<DateOnly>` |
| `DateTime` | `DateTime` | `DateTime[]` | `GetDateTime` |
| `DateTimeOffset` | `DateTimeOffset` | `DateTimeOffset[]` | `GetDateTimeOffset` |
| `HierarchyId` | the UDT | `string?[]` | EF's `HierarchyId` reader, surfaced as its path string |

Any other pair is a composer bug and throws `InvalidOperationException` naming the column; `DBNull`
reads as null. With `CountCap` the capped count (§5.2) rides the same statement and lands in
`RowPage.Count`; with `IncludeAncestors` (tree roots only) the page keys are captured and:

```sql
DECLARE @tb2_anc TABLE ([Id] int PRIMARY KEY);
INSERT INTO @tb2_anc ([Id])
SELECT a.[Id] FROM [gl].[Centers] AS a
WHERE a.[Id] NOT IN (SELECT [Id] FROM @tb2_keys)
  AND EXISTS (SELECT 1 FROM [gl].[Centers] AS p JOIN @tb2_keys AS k ON k.[Id] = p.[Id]
              WHERE p.[Node].IsDescendantOf(a.[Node]) = 1 AND p.[Id] <> a.[Id]);
```

followed by the same select restricted by `KeySetRestriction("Id", "@tb2_anc")` under
`AncestorsFilter` (the access filter alone — an ancestor need not satisfy the user's filter but must
be visible; null conjoins nothing), ordering kept, no paging; the rows land in
`RowPage.Ancestors`. `Count(spec, arguments, cap)` is the standalone form of the capped count.

### 11.4 Entity queries

`Query<TEntity>(EntityQuery)` restricts `Select` to bare paths (a computed item is a compose-time
`ArgumentException`; user text is validated by spec 0014 before it reaches the batch). The host runs
spec 0008's `DiscoverQuery` on the select text (cached by the engine) to learn the paths, then
appends the root `Id` and, for every navigation prefix used, its key path (`Customer.Id`) and the FK
on its owner (`CustomerId`), compiles, and binds as `Rows` does. `Children` adds one `Query` per
named collection over the child, restricted by `KeySetRestriction("<ParentKey>", <source>)` where
the source is the root's ids TVP when the root is restricted by ids and the captured `@tb{b}_keys`
otherwise; grandchildren recurse through dotted names. `IncludeAncestors` and `AncestorsFilter`
behave as for `Rows`; the ancestor rows materialise into `Related` under the root's own entity name.

### 11.5 The materializer

`EntityMaterializer` walks each `QueryexColumn.Path`: a depth-1 column lands on the root instance
through the property's setter (the EF converter turns enum strings into enums; `ModifiedAt` keeps
seven fractional digits); a deeper column lands on a related instance of the CLR type behind the
path's descriptor, keyed by (entity name, id) in `EntityQueryResult.Related`, deduplicated
across rows and never assigned to a navigation property (no cycles on the wire). Related instances
are partial (selected columns only), recorded with their projection in `RelatedEntitySet
.Projection`, and never travel back into a save (a related instance in a save payload is ignored
by the emitter because only the root type and its child collections bind). Children are stitched
onto their parents after execution by parent key, in the order the child query returned them
(`OrderBy` of the child query = the child's key). Missing columns (the N−1 rule) leave the
property at its default.

## 12. Telemetry, the DB-call budget, and the analyzers

### 12.1 Instruments

```csharp
// Tellma.Core.Abstractions.Data
public static class DataTelemetryNames
{
    public const string MeterName = "Tellma.Core";
    public const string RoundTrips = "tellma.data.roundtrips";
    public const string RoundTripDuration = "tellma.data.roundtrip.duration";
    public const string Statements = "tellma.data.batch.statements";
    public const string Retries = "tellma.data.retries";
    public const string CommitProbes = "tellma.data.commit.probes";
    public const string IdRefills = "tellma.data.ids.refills";
    public const string IdHealed = "tellma.data.ids.healed";
    public const string IdReserved = "tellma.data.ids.reserved";
    public const string IdReturned = "tellma.data.ids.returned";
    public const string RowsSaved = "tellma.data.rows.saved";
    public const string RowsDeleted = "tellma.data.rows.deleted";
    public const string ConcurrencyConflicts = "tellma.data.concurrency.conflicts";
    public const string TreeRecomputes = "tellma.data.tree.recomputes";
    public const string TreeRepairs = "tellma.data.tree.repairs";
    public const string PurposeTag = "purpose";
    public const string OutcomeTag = "outcome";
    public const string OperationTag = "operation";
    public const string EntityTag = "entity";
    public const string ClassTag = "class";
    public const string ErrorTag = "error";
    public const string SequenceTag = "sequence";
    public const string RoleTag = "tellma.db.role";        // span attribute (§6.1), never a metric tag
}

public sealed class DataAccessScope                     // scoped; copied into job scopes
{
    public string Operation { get; set; }
    public int RoundTrips { get; set; }
    public int Retries { get; set; }
}
```

| Instrument | Kind | Unit | Tags |
|---|---|---|---|
| `tellma.data.roundtrips` | histogram per scope | `{roundtrip}` | `operation` |
| `tellma.data.roundtrip.duration` | histogram | `s` | `operation`, `purpose`, `outcome` (`ok`, `retried`, `failed`) |
| `tellma.data.batch.statements` | histogram | `{statement}` | `purpose` |
| `tellma.data.retries` | counter | `{retry}` | `class` (`reported`, `ambiguous`), `error` |
| `tellma.data.commit.probes` | counter | `{probe}` | `outcome` (`committed`, `not-committed`, `unknown`) |
| `tellma.data.ids.reserved`, `tellma.data.ids.returned` | counters | `{id}` | `sequence` |
| `tellma.data.ids.refills`, `tellma.data.ids.healed` | counters | `{event}` | `sequence` |
| `tellma.data.rows.saved`, `tellma.data.rows.deleted` | counters | `{row}` | `entity` |
| `tellma.data.concurrency.conflicts` | counter | `{conflict}` | `entity`, `outcome` (`conflict`, `missing`) |
| `tellma.data.tree.recomputes`, `tellma.data.tree.repairs` | counters | `{statement}`, `{row}` | `entity` |

The meter is `Tellma.Core` through `IMeterFactory`; every other area of the runtime package
declares its own `<Area>TelemetryNames` holder with a `MeterName` member under the same meter,
and the adapter packages declare theirs (`Tellma.Blobs`, `Tellma.Core.AspNetCore`,
`Tellma.Core.Mcp`). No instrument carries a tenant or user tag; tenant and user ids go to the log
scope and to trace attributes. Alert queries live under `infra/monitoring/`.

### 12.2 The DB-call budget

`DataAccessScope` is a scoped value whose `Operation` its opener names and the executor increments
per round trip and per retry; job scopes receive a fresh copy. The pipeline names it
`"<Resource>:<operation>"` (`gl.Center:query`, `core.User:save`) and a job `"job:<key>"`; the value
becomes the `operation` tag of `tellma.data.roundtrips` and `tellma.data.roundtrip.duration`. When
the scope ends the executor records `tellma.data.roundtrips` and stamps it on the current
`Activity`: it is the one round-trip instrument, and every scope — request, job or provisioning — is
observed through it. Tests assert the budgets of §5.6 with
`SqlConnection.RetrieveStatistics()["ServerRoundtrips"]` as an independent oracle.

### 12.3 Analyzers

`Tellma.Core.Analyzers` ships six diagnostics, error severity, in every project that references
`Tellma.Core.Abstractions`:

| Id | Condition |
|---|---|
| `TELLMA0001` | `SqlDataRecord` constructed, or `SqlDataRecord.Set*`/`GetOrdinal` called, outside a type marked `[TableTypeBinder]` (hard-coded ordinal binding). |
| `TELLMA0002` | An EF query (`Set<T>()`, a `DbSet<T>`, `Database.SqlQuery`) or `SaveChanges`/`SaveChangesAsync` over `TellmaDbContext` outside `Tellma.Core` and projects whose assembly name ends in `.Migrator`: the context is platform-internal. |
| `TELLMA0003` | An `IDataBatch.Sql` call whose text (parsed with ScriptDom) contains `INSERT`/`UPDATE`/`DELETE`/`MERGE` without `Writes`; contains `MERGE` anywhere; contains `EXEC`/`EXECUTE`/`sp_executesql` other than `sp_sequence_get_range` or `sp_getapplock`; declares a name with a reserved prefix (`@qx`, `@tb`, `@tm`) outside `Tellma.Core`; throws a literal number outside 50400–50699; or has a hole that is neither a `SqlIdentifier`, a `nameof`, a `Tvp` result nor a value. A non-constant text (built by concatenation) is refused because it cannot be parsed. |
| `TELLMA0004` | A call to a method marked `[ApiAction]` or `[EntityAction]` from outside its declaring type and `Tellma.Core`: an action is invoked through spec 0014's invoker and pipeline, which evaluate its securable. |
| `TELLMA0005` | A call to `SaveAsync`, `DeleteByIdsAsync`, `DeleteByQueryAsync`, `ExecuteActionAsync`, `ActivateAsync`, `DeactivateAsync` or `DeleteWithDescendantsAsync` of any `EntityService<,>` from a pipeline participant — a hook override, an `[EntityAction]` method, any member of an `IEntityValidator<>` or `IPersistEffect<>`: a participant never runs a nested pipeline; a write into another stack is enlisted through `IEnlists<T>` (spec 0014 §13.3). |
| `TELLMA0006` | An `IDataBatch.Sql` text whose `INSERT`/`UPDATE`/`DELETE` target (parsed with ScriptDom, as `TELLMA0003`) is the `[Table]` of a mapped entity type or of one of its child collections, or a call to `IDataBatch.Save<T>`, `Update<T>` or `Delete<T>`, from a type outside `Tellma.Core` that is not the table's owner — the service, validator, effect or companion closed over the entity whose table it is: a stack-owned table is written by its owner or through an enlisted write (spec 0014 §13.3). A table no stack owns passes with `Writes`. |

The analyzers run on the platform's own projects too, except where a condition exempts `Tellma.Core`
by name.

## 13. Testing

### 13.1 Suites and tiers

| Suite | Location | Tier | What it pins |
|---|---|---|---|
| Unit | `test/core/Tellma.Core.Tests/Data/` | PR | golden SQL per emitter statement kind against the fixture metadata (§8, §9, §10, §11.3); `SaveReceipt` reading from canned result sets; the `SaveHandle` and `DeleteHandle` listing one `ColumnCapture` per `[BlobReference]` column of every written table; `FilterTree.Via` binding (a one-segment and a two-segment path, a nested node, a to-many segment refused at compile time, a diagnostic inside `inner` located at `Filter.Via[<navigation>]`, distinct L3 keys for one inner filter under two navigations); allocator deficit and buffer arithmetic, temporary-id rewriting, hold/return; metadata construction and every §3.2 rule as a failing model; `SchemaFingerprint` stability; natural-key inference order; enum sizing (nullable enums included); retry classification for every number of §6.4; the commit-probe verdicts; `QueryRowSet.Builder` buffers for every (Queryex type, field type) pair of §11.3, `Scale` per numeric field type, and the `InvalidOperationException` for any other pair; materializer routing (root, related, children, grandchildren, missing column); the persist frame assembly order of §5.5; parameter-count and result-set-count guards |
| Integration | `test/core/Tellma.Core.IntegrationTests/Data/` | `Category=Integration`, PR on Windows (LocalDB) and Linux (Testcontainers), nightly full matrix | everything in §13.3 |
| Analyzers | `test/core/Tellma.Core.Analyzers.Tests/` | PR | each `TELLMA000n` condition and its negative |

No `Live=true` suite exists in this spec: nothing here talks to an external service. One
environment variable, `TELLMA_TEST_SQL`, carries the connection string; absent on Windows it
defaults to LocalDB (`(localdb)\MSSQLLocalDB`), absent on Linux the fixture starts a
`Testcontainers.MsSql` container.

### 13.2 The fixture schema

This spec owns `TellmaFixtureDbContext`, built through `UseTellmaSqlServer` with the platform
conventions, and its migrations for schema `fixture`. The context composes the platform's model
contribution (`dbo.__TellmaSchema`, §4.3), spec 0012's (`core.VersionTags`, seeded as spec 0012 §2.8
states) and spec 0013's access contribution (`core.Users`, `core.UserStamps`, `core.Roles`,
`core.RoleMemberships`, `core.Permissions` and `core.UserPreferences`; spec 0013 §1.2), so the
schema guard, the connect prologue, the persist frame's tag guard (§5.5 step 4) and the audit FKs
resolve to the production tables. The migrations also create a minimal stand-in
`core.Jobs (Id int PK)` so the job FK resolves to its production name, and `core.Blobs` with
`core.sq_Blobs` in full (spec 0016 §2.1) so the `[BlobReference]` FKs resolve and spec 0016 §11's
suite runs against the fixture database. The database is created with `READ_COMMITTED_SNAPSHOT ON`,
as spec 0010's provisioning does, and change tracking is enabled on every fixture table so the
executor in test mode compares `CHANGETABLE(CHANGES …)` with `WrittenTables` after every batch.

| Table | Shape | Columns beyond the capability sets |
|---|---|---|
| `fixture.Widgets` | `TopLevelEntity`, `[Temporal]`, `IActivatable`, `[NaturalKey] Code`, `[Multilingual] Name` | `Code varchar(50) NULL` (`UX_Widgets_Code`); `Kind varchar(8)` (enum `WidgetKind = Plain \| Fancy`); `Name/Name2/Name3 nvarchar(255)`; `Subject nvarchar(64) [WriteOnce] [Unique]`; `Price decimal(19,4) NULL`; `Settings nvarchar(max) [JsonColumn]`; `ImageId int? [BlobReference("fixture-image", Avatar)]`; history `fixture.WidgetsHistory` |
| `fixture.WidgetParts` | `ChildEntity`, temporal with its parent | `WidgetId [ParentKey]`; `Ordinal int`; `Notes nvarchar(1024) NULL`; `UX_WidgetParts_WidgetId_Ordinal` |
| `fixture.WidgetPartNotes` | grandchild | `WidgetPartId [ParentKey]`; `Text nvarchar(max)` |
| `fixture.Nodes` | `ActivatableTreeEntity`, `[Tree(MaxDepth = 8)]` | `Code varchar(50)` unique; `Name nvarchar(255)` |
| `fixture.Links` | `ChildEntity` of `Nodes` with two FKs to the parent | `NodeId [ParentKey]`; `OtherNodeId`, an ordinary reference |
| `fixture.Shipments` | `TopLevelEntity<long>`, `IJobEntity` | `State varchar(8)`; `Payload nvarchar(max) NULL`; `long` keys through `BigIdList` |
| `core.Blobs` | spec 0016 §2.1 in full, with `core.sq_Blobs` | the capture tables' and the `[BlobReference]` FKs' target; this spec's suites run no blob effect |

The fixture entities and `TellmaFixtureDbContext` live in the shared test project
`test/shared/Tellma.Testing.Entities/`, referenced by every integration project of the runtime
package. Sibling specs add tables to the same schema through that project for their own suites
(`fixture.Lookups`, `[Cacheable(MaxRows = 50)]`, spec 0012; `fixture.BlobOwners`, spec 0016); the
tables above are the ones this spec's suites use. A `FixtureTenant` helper opens `ITenantDatabase`
for the fixture database with a `System` prologue variant and exposes `TenantId = 1`; a suite that
exercises the caller path seeds `core.Users` rows with their memberships and permissions and
connects through the ordinary prologue (spec 0014 §18.2).

### 13.3 Integration assertions

Each is a named test; the list is the behavioural contract of this spec:

- **Budgets.** Every row of §5.6 against the statistics oracle; a validation round adds exactly
  one.
- **Schema guard.** A host whose `StorageFingerprint` is absent from `dbo.__TellmaSchema` is
  refused with `50501` on a read, a validate and a persist batch alike; present as the older of
  two rows it is served; the migrator keeps exactly two rows across three migrations and writes
  none when the history is ahead of it.
- **Emitter.** Skip-unchanged writes no history row; a parent whose only change is a child is
  stamped; a child of an unsupplied parent is untouched; `[]` deletes all children of the
  supplied parents; a grandchild change stamps the root; deleted-under-you is `NotFoundException`;
  a stale stamp is `ConcurrencyException` with `IsMissing` false; `Override` writes and never
  resurrects; two racing saves of one row produce exactly one conflict; a write-once column
  changed on update is not written; a server-owned value sent by the client is overwritten; the
  blob capture table carries old and new ids on insert, update, child delete and query delete;
  `OPTION (RECOMPILE)` appears above 1,000 rows and not below.
- **Allocator.** A warm buffer adds no round trip to a create; a cold reservation adds one
  statement to the batch it is given — spec 0014's first round trip, which a create with nothing
  else to load runs for the reservation alone; validation failure returns ids; a persist failure
  does not; a sequence behind `MAX(Id)` heals once by consuming; a 2627 on the PK heals once and
  retries; a 2627 on another index does not heal; temporary ids are rewritten in a self-typed FK, a
  child parent key and a grandchild parent key; `TakeAsync` pays one round trip when the buffer is
  empty and none when it is not.
- **Trees.** A saved row whose parent is a descendant of another saved row is pathed once;
  id-path nodes survive re-parenting; a cycle written under write skew fails in SQL with
  `Tree.Cycle`; a chain deeper than `MaxDepth` fails with `Tree.TooDeep`; counts of old and new
  ancestors are correct after a move, an activate, a deactivate and a delete-with-descendants;
  `Verify` finds and repairs an injected drift; `hierarchyid` never crosses the TVP boundary; a
  `hierarchyid` result column reads as its path string; `level(Node)` equals `GetLevel()`.
- **Deletes and updates.** `ByIds` with a missing id is 404; with an id hidden under `ReadFilter` is
  404, indistinguishable from missing; with a readable id outside `Filter` is 403; with stamps
  mismatching is 409; `ByQuery` above `Cap` is `50413`, with a wrong `ExpectedCount` is `50428`;
  `WithDescendants` refuses a hidden root with 404 and a closure not wholly visible under `Filter`
  with 403, and deletes the closure in one statement; 547 on a referenced row is `Fk.InUse`; a
  repeated activate writes no history row.
- **Executor.** A deadlock victim is retried and succeeds; an ambiguous failure on an all-idempotent
  batch is re-run; on a persist with inserts the probe returns `Committed` after a killed connection
  post-commit and `NotCommitted` pre-commit; a `THROW 50503` is retried three times then surfaces;
  result sets before a `THROW` are read; `TransactionMode.None` leaves autocommit; the persist
  frame's guard blocks a stale `permissions` tag with `50412`; a `THROW` outside the bands is
  refused; `OUTPUT inserted.[Id]` from an `UPDATE` on a system-versioned table works; `INSERT INTO
  @t … SELECT … OFFSET/FETCH` and a derived table with `OFFSET/FETCH` compile; the change-tracking
  audit matches `WrittenTables` for every statement kind; a `THROW` from the statement at ordinal 3
  and a 547 from the one at ordinal 4 surface with `StatementOrdinal` 3 and 4.
- **Queries.** A grid page with count and ancestors is one round trip; `cap + 1` is returned above
  the cap; `Skip`/`Take` reuse one plan (two pages, one `sys.dm_exec_query_stats` entry);
  `KeySetRestriction` over `IdList`, `BigIdList`, `StringList`, `DateList` and a `@tb{b}_` table;
  children and grandchildren stitch by parent key; related entities dedupe across rows and carry the
  projection; `Properties<Enum>()` matches nullable enums; the three schemas differ only in
  `Name2`/`Name3`; a `FilterTree.Via` filter on `fixture.WidgetPartNotes` through
  `WidgetPart.Widget` returns exactly the notes of the matching widgets.
- **Isolation.** Two fixture databases sharing ids never share a buffer or a schema cache.

## 14. Definition of done

- **Projects**: `src/core/Tellma.Core.Abstractions` (the `.Entities`, `.Data`, `.TableTypes`
  namespaces and the `Tellma.Core.Queryex` reference), `src/core/Tellma.Core` (the
  `Tellma.Core.Data` namespace), `src/core/Tellma.Core.Analyzers`, the three test projects of
  §13.1 and the shared fixture project `test/shared/Tellma.Testing.Entities` (§13.2) — each with a
  README stating purpose and usage, XML docs on every public member, building
  and testing on Windows and Linux under the repository's warnings-as-errors gates, wired into
  `Tellma.slnx`; central package management carries the pins of §1.2.
- **Behavior**: the entity contract and conventions of §2 and §1.3; the metadata and startup
  rules of §3; the column vocabulary and the N−1 rule of §4; the batch, contributors, key
  capture, deduplication and persist frame of §5; the command, binder, error taxonomy, retry and
  probe of §6; the allocator of §7; the emitter of §8; the update and delete statements of §9;
  the tree statements, validator and verify job of §10; the schema provider, the seven engine
  amendments (`FilterTree.Via` among them), row-set reader, entity queries and materializer of
  §11 — implemented and pinned by the suites of §13, green in CI on both platforms.
- **Observability**: every instrument of §12.1 emitted under the `Tellma.Core` meter and asserted
  by the integration tier; `DataAccessScope` stamped on the current `Activity`; the span attributes
  of §6.1; log events `DataBatch.SlowRoundTrip`, `DataBatch.OnCommittedFailed`,
  `IdAllocator.Healed`, `TreeVerify.Repaired` asserted by name.
- **CI**: the unit and analyzer suites on every PR; the integration suite on every PR on LocalDB
  and Testcontainers; the nightly run adds the full retry and probe matrix with connection-kill
  fault injection.
- **Docs**: ARCHITECTURE.md updated where this spec touches it — the package naming and dependency
  rules (`Tellma.Core.Abstractions` references `Tellma.Core.Queryex`; one runtime `Tellma.Core`
  carrying data access; `Tellma.Core.Analyzers`); the data-layer entity class and hierarchy (Core's
  default leaves live in Abstractions, `[NotMapped]` child collections exist on the class, two
  platform shadow properties exist and "no *implicit* shadow properties" is the rule); the
  data-layer ID allocation row (exact-deficit reservation on the batch with a warm buffer, healing
  by consuming the gap, landing here); the UDTT/rowversion row (no `rowversion`; `ModifiedAt
  datetimeoffset(7)` is the concurrency token); the reports row (raw SQL only through
  `IDataBatch.Sql` with declared writes and the ScriptDom analyzer; `SaveChanges` banned); the
  temporal row (the emitter skips unchanged rows); the observability row (the `Tellma.Core` meter,
  per-tenant identity on traces and logs only); and the spec-pointers row (ID allocation and the
  EF-to-Queryex adapter land here). Public XML docs and error messages reference no `docs/` paths,
  per repo rule.
- **Not in scope of done**: the pipeline that composes batches (spec 0014), the contributors this
  executor runs (specs 0012 and 0013), the tables whose rows the fixed-text statements insert
  (specs 0019 and 0020), the blob effect that reads the capture tables (spec 0016), the Excel
  codec that reads natural keys (spec 0018), and `SqlBulkCopy`, composite natural keys and `Guid`
  keys.

## Decisions record

The load-bearing decisions, where not already evident above:

1. **One runtime package and the Queryex edge on Abstractions** — modules must see `FilterTree`,
   `QuerySpec` and `KeySetRestriction` in contracts, and a second `Data.Abstractions` would split
   one contract in two (§1.1).
2. **One class is storage shape and wire shape; child collections are `[NotMapped]` lists** —
   the least a distribution can write; the ban on parent→child navigations is an EF-model rule
   whose reasons hold because EF never sees the list (§2.3).
3. **Four audit columns on every top-level entity, none on children; temporal is additive** —
   one vocabulary; `ModifiedAt` must be an ordinary UDTT column, which a period column cannot be
   (§2.1, §4.2).
4. **`ModifiedAt` is the concurrency token, server-stamped once per batch; no `rowversion`** —
   user-visible mutations stamp it and bookkeeping never touches the row, which `rowversion`
   cannot express; one database clock per tenant beats skewed instances (§2.1, §8.2).
5. **`IsActive` is server-owned** — every row is created active and only the actions change it:
   one securable, one write path, no save-bypasses-activate hole (§2.1, §9.1).
6. **No CLR `Parent` on tree bases** — plain-inheritance extension with one `UseEntity`
   constraint; the Queryex navigation derives from the column (§2.1).
7. **Ownership is declared once and enforced by the emitter** — a forgotten validator cannot
   write a server-owned column because the `SET` list never contains it (§2.4, §8.2).
8. **`[Multilingual]` anchors a group; twins by name** — a pure naming convention mistakes
   `Address2` for a translation (§2.6).
9. **Enums as `varchar(n)` by convention, without an `IN` CHECK** — zero distribution lines; a
   member added is not a migration per table (§2.6).
10. **`Name` before `Code` in natural-key inference** — the display name is what a sheet author
    types (§2.7).
11. **One command per round trip, concatenated text, in-text transaction, `NextResult()`** —
    `SqlBatch` sends a TVP per command, has no retry provider and shows one statement to
    telemetry; `TransactionScope` is Serializable by default and fails on Linux for distributed
    work (§5, §6.1).
12. **The prologue rides every caller batch, `Persist` included, and the guard wraps the body**
    — a body never runs under stale premises; the transaction opens inside the guarded block
    (§5.3, §5.5).
13. **Shared locks on the tag rows a persist reads, an update lock on the rows it bumps** — a
    concurrent bump waits one round trip instead of committing under the persist, and two writers
    of one row never deadlock (§5.5).
14. **Retry classes are reported versus ambiguous, and the flag is `Idempotent`** — a reported
    failure under `XACT_ABORT` is always safe to re-run; an ambiguous one only when every
    statement may run twice; the insert-only commit probe answers the rest honestly (§6.5).
15. **Two THROW bands with HTTP-mnemonic numbers** — one taxonomy for platform SQL and a
    disjoint band for distributions, mapped mechanically (§6.3).
16. **Exact-deficit reservation on the first round trip, a warm buffer with a low-water refill, ids
    assigned before validators** — allocation adds no round trip to a create; validators see final
    ids; a failed validation costs no gap (§7).
17. **Healing by consuming the gap** — `sp_sequence_get_range` needs `UPDATE`, `ALTER SEQUENCE`
    needs `ALTER`, which the web identity must not hold (§7.4).
18. **Separate `INSERT`/`UPDATE`/`DELETE` with two TVPs per table; never `MERGE`** — two live
    `MERGE` defects on temporal targets and indexed views; app-assigned ids make new-versus-existing
    a C# fact, so a deleted row is a conflict, never a resurrection (§8.1).
19. **U locks in the concurrency guard held to `COMMIT`** — the stamp comparison is sound under
    both isolation defaults without a second count; deadlocks are retried (§8.2).
20. **Skip unchanged rows through `EXCEPT`** — temporal history stays honest under merge imports
    (§8.2).
21. **Conflict rows in a result set before the `THROW`, ids capped in its JSON message** — the
    executor reads the set before the exception surfaces, so every conflict reaches the pipeline
    in the persist round trip; `ROLLBACK; RETURN` after a result-set walk is the alternative
    (§8.2).
22. **Id-path nodes with the `/0/<Id>/` provisional, the affected set computed first, the
    path-count cycle fence, a scoped recount, and a weekly verify job** — siblings never renumber;
    `MAXRECURSION` detects nothing; the recount is O(affected × depth), never the table (§10).
23. **Key capture through a keys-only twin compile** — children of filtered pages, ancestors,
    deletes and updates in one round trip (§5.4).
24. **Schemas per `MultilingualShape`, at most three per process** — the engine caches by schema
    identity; per tenant would defeat every cache, one schema would leak `Name3` (§11.1).
25. **Additive engine amendments, language version unchanged** — a list parameter would need a
    version bump; restrictions on paths cover every consumer (§11.2).
26. **`today()` binds the tenant zone** — a client must not be able to move a security predicate
    (§5.1).
27. **`SqlOptions.UserIds` names the users a raw statement's bumps affect, gated at startup** —
    the epilogue cannot read raw text; bumping every user is the alternative (§5.2).
28. **The N−1 schema-evolution rule** — named columns in, named columns out, expand then contract,
    so a fleet migrates one database at a time (§4.3).
29. **`UseTellmaSqlServer` pins `UseCompatibilityLevel(160)` and the application name** — one
    model for every host and the migrator; JSON columns never flip type behind the model (§1.3).
30. **Six analyzers** — hard-coded ordinals, `SaveChanges`, undeclared or reserved raw SQL, a direct
    call to an action method, a nested pipeline run from a pipeline participant and a write into a
    stack-owned table by a type that is not its owner are refused at build time rather than found in
    production (§12.3).
31. **The schema guard on every round trip** — the N−1 rule is enforced by a two-row fingerprint
    table and one seek, correct on the first request after a migration and after a restore (§4.3).
32. **`[Derived]` as an ownership of its own** — a value the service derives from the payload is
    reset before the hook and written by the emitter, so a client can never smuggle one (§2.4).
33. **`CallerAuthority` on the caller channel, an internal channel for the platform's composers,
    `StatementOrdinal` on statement-bound exceptions** — a statement a caller appends may write only
    the tables of its own stack, so another stack's table reaches a batch only as an enlisted group,
    and a persist-time error is attributed to the group whose statement raised it (§5.2, §6.4).

## Review flags

1. **Audit FKs to `core.Users` on every table** (§4.2) — every distribution table carries two
   foreign keys into Core and users are never hard-deleted. Alternative: no FK on audit columns
   (fewer dependencies, dangling creators possible). Flips if a distribution needs a table with no
   dependency on `core.Users` (an archive schema, a table fed by bulk load).
2. **`IsActive` server-owned** (§2.1; S5) — every row is created active and only the actions
   change it, so neither a save nor an import can (de)activate. Alternatives: write-once (creatable
   inactive through save and import, the actions afterwards) or a diff-gated editable column. Flips
   if reference data that must arrive inactive becomes a common import need.
3. **`Name` before `Code` in natural-key inference** (§2.7; S9) — versus `Code` first. Flips if
   the first distributions consistently declare `[NaturalKey]` on `Code`, making the inference
   order moot, or if duplicate display names in imports become the common failure.
4. **`ActivatableTreeEntity<TKey>` as a second base** (§2.1) — versus `TreeEntity` always carrying
   `ActiveSubtreeCount` (one base, one redundant column on non-activatable trees). Flips if no
   non-activatable tree ever ships.
5. **Recount over the affected rows and their old and new ancestor chains, plus a weekly verify
   job** (§10.2–10.4; S33) — versus the whole-table recount per save, which is simpler and matters
   only above roughly 100,000 rows. Flips if the measured `IsDescendantOf` recount over the scoped
   set is not faster than the whole-table form at reference sizes.
6. **Conflict rows as a result set before the `THROW`** (§8.2) — versus `ROLLBACK; RETURN` after
   a result-set walk (no exception, a stateful executor). Flips if reading a result set that
   precedes a thrown error proves unreliable across SqlClient versions.
7. **U locks in the concurrency guard** (§8.2) — versus an unlocked guard plus a post-update count
   against a precomputed expected set (no U locks held, one more statement, deadlock-free but
   weaker). Flips if deadlock retries on overlapping saves become measurable in production.
8. **Healing by consuming the gap** (§7.4) — versus `ALTER SEQUENCE … RESTART` with `ALTER`
   granted to the web identity. Flips only if the platform ever grants DDL to the application
   role.
9. **One `Tellma.Core` runtime package and the Queryex edge on Abstractions** (§1.1; S1) — versus
   a separate `Tellma.Core.Data.Abstractions` and `Tellma.Core.Data`. Flips if a consumer emerges
   that needs the data layer without the pipeline, access, settings and jobs that share the
   package.
10. **Three-attempt retry of a non-idempotent persist on reported failures** (§6.5) — relies on
    `XACT_ABORT ON` having rolled back and on `COMMIT` being the last statement, which the executor
    asserts. Alternative: retry only idempotent batches. Flips if a reported error is ever observed
    after a committed transaction.
11. **Key capture by a keys-only twin compile** (§5.4) — versus a second round trip for children
    of filtered pages and for tree ancestors (simpler, one more round trip on tree grids). Flips if
    the twin compile measurably doubles compile cost on large selects without the cache absorbing
    it.
12. **`ModifiedAt` as the token** (§2.1, §8.2; S2) — versus `rowversion` with the same
    sibling-table discipline; and server-stamped with an insert-only commit probe versus a
    C#-computed stamp that could also prove update-only commits. Flips if an equal stamp after a
    backwards clock step is ever observed, or if update-only ambiguous failures prove frequent
    enough that the honest `DependencyUnavailableException` costs more than a client-side stamp.
13. **`UseCompatibilityLevel(160)` pinned** (§1.3) — versus the provider default and a revisit when
    native `json` is wanted. Flips when Azure SQL and on-prem both ship the native type and EF
    maps it without `UseAzureSql()`.
14. **Id-path nodes with the `/0/<Id>/` provisional and no `Level` column** (§10.1; S6) — versus
    sibling ordinals or a persisted `Level`. Flips if the encoded size of id-path nodes for
    `bigint` keys at depth 32 exceeds what the unique index handles well (measure before a
    `long`-keyed tree ships).
15. **Enum columns `varchar(n)` without an `IN`-list CHECK** (§2.6; S8) — versus a CHECK per enum.
    Flips if an out-of-band writer (a bulk load) ever inserts an invalid member and the class
    cannot read it.
16. **Ids assigned after the first round trip and before validators, returned on validation
    failure** (§7.3; S10) — versus assign-after-validation (validators see temporary ids) or
    never-return (simpler buffer). Flips if validators prove not to need final ids.
17. **A platform THROW band 50400–50599 with HTTP-mnemonic numbers plus a distribution band
    50600–50699** (§6.3; S13) — versus one band and code-only messages. Flips if a distribution
    needs more than 100 numbers, which the message-as-code design makes unlikely.
18. **System-written rows take ids inside their statements** (§7.3; S27) — versus an async chain
    through the allocator's buffer. Flips if in-statement `sp_sequence_get_range` shows up in
    persist duration.
19. **A default related projection of `Id` + `Name` group + `Code` + avatar columns** (§2.5; S34)
    — versus requiring a `[RelatedSelect]` declaration on every navigable entity. Flips if the
    default proves too narrow for most details pages and every entity declares one anyway.
20. **`SqlOptions.UserIds` as the way raw statements name the users whose tags they bump**
    (§5.2; S37) — versus deriving nothing from raw SQL and bumping every user. Flips if raw
    statements against user-tagged tables turn out to be rare enough that a wholesale bump is
    cheaper than the gate.
21. **No CLR `Parent` on tree bases** (§2.1; S38) — versus `TreeEntity<TSelf>` with a typed `Parent`
    (LINQ ergonomics; every tree extension must close a generic). Flips if a LINQ surface is ever
    reintroduced and tree code authored there needs the navigation.
22. **Contributors ordered by `Order` with the executor emitting the guard statements itself**
    (§5.3, §5.5) — versus a third `DataBatchStage` for transaction-head statements owned by the
    contributors. Flips if a fourth contributor needs a transaction-head statement that the
    executor's fixed frame cannot express.
23. **`Rows` returns a `RowPage` carrying the count and the ancestor rows beside a buffer-only
    `QueryRowSet`** (§3.3) — versus `Count` and `Ancestors` as members of the row set (one type
    fewer; the row-set converter must skip them, and every other row set, a raw result set or the
    ancestor rows, carries two members it never fills). Flips if the wrapper proves noise at every
    `Rows` call site.
24. **No LINQ surface; `TellmaDbContext` platform-internal** (§1.3, §5.1) — versus a counted,
    unfiltered `IQueryable` escape hatch for pack reads. Flips if a pack read proves inexpressible
    in Queryex and raw SQL alike.
25. **The schema guard as a statement on every round trip** (§4.3) — versus a per-process verdict
    cached per (tenant, fingerprint) until a state change (no per-batch seek; a restored backup or
    a re-run migration goes undetected until the cache clears). Flips if the seek shows in persist
    duration at scale.
26. **`[Derived]` as an ownership of its own** (§2.4) — versus leaving derived columns `Editable`
    with a preprocess hook that overwrites them (no metadata; a hook that forgets ships the client's
    value). Flips if no pack entity ever needs one.
