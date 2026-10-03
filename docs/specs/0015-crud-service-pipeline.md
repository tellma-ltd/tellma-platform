# Spec: CRUD Service Pipeline and Capabilities

- **Author:** Ahmad Akra
- **Date:** 4 September 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Every top-level entity in a Tellma distribution gets the same service for free: a bulk-shaped
query, a details read that returns everything a page needs in one call, an array-shaped save with
child synchronisation, deletes by ids and by query, and the actions its capabilities imply. This
spec ships that service — `EntityService<TEntity, TKey>` — together with the pipeline underneath
it, the validation framework a service author writes rules against, the capability recipes that
turn an interface or an annotation on an entity class into operations, securables, filters and
persist statements, the concurrency rule, the closed set of exceptions the web layer maps, and the
round-trip budget every operation is held to. A distribution author writes one entity class and
one line of composition; a service class exists only where there is logic.

The pipeline is a consumer of the data layer of spec 0012 (the batch abstraction, the save emitter,
the id allocator, the Queryex adapter and its engine amendments), of the connect prologue and the
access evaluator of spec 0014, of the version tags of spec 0013, of the request context and feature
composition of spec 0011, and of the wire records of spec 0016. It is itself consumed by spec 0016
(endpoint and MCP projection from the stack descriptor), spec 0014 (securable registration from the
same descriptor), spec 0017 (the blob-reference validator and effect), spec 0018 (the Core and GL
services), spec 0019 (the Excel codec saves and hydrates through it and streams rows through the
row source defined here), spec 0020 (`Schedule` is an ordinary activatable stack; job rows ride the
persist batch) and spec 0021 (notifications ride the persist batch through `PersistContext.Notify`).
It builds on the frozen engine of spec 0008: every user- or permission-authored expression compiles
through `QueryexEngine.CompileQuery` (spec 0008 §2), row-level security composes as a `FilterTree`
(spec 0008 §2.3), and several compiled queries and model-emitted statements concatenate into one
command through `BatchOrdinal` (spec 0008 §13.1).

Three properties govern the design. The transaction is the persist round trip's text and nothing
else: nothing holds a lock across client code, and every invariant that must hold at write time —
concurrency stamps, the permissions tag, the row-level post-check, uniqueness, tree cycles — is a
statement inside that text. Every check runs where its input first exists and where failure is
cheapest: shape and type-level permission before any round trip, existence and visibility in a
filtered load, everything else inside the transaction. And a capability is declared once, on the
entity, and projected everywhere from one descriptor; there is exactly one thing to forget, and
forgetting it removes the feature wholesale rather than half of it.

The spec deliberately leaves the HTTP and MCP projection, the problem-details body and the wire JSON
options to spec 0016; the emitter's statement text, the tree statements, the delete statements and
the executor's retry to spec 0012; the prologue text and the evaluator to spec 0014; the Excel codec
to spec 0019; and the job and notification statements to specs 0020 and 0021. Where this spec
restates a member of a contract another spec owns, it restates only what it calls, verbatim.

## Goals / Non-goals

**Goals**

- Ship `EntityService<TEntity, TKey>` in `Tellma.Core.Abstractions` as the one authoring surface —
  non-virtual operations forwarding to a sealed pipeline, a closed set of hooks with empty defaults
  — and the composable `IEntityValidator<T>`, `IPersistEffect<T>`, `IDetailsContributor<T>`
  components for concerns with multiplicity.
- Ship the stack: `StackDescriptor` derived once at startup from the leaf entity and the service,
  `IStackRegistry`, `IApiActionInvoker` as the one entry to every `[ApiAction]` method, the
  `[Stack]`, `[EntityAction]`, `[ApiAction]` and `[ApiRoute]` annotations, the contribution items,
  and the startup checks that make an omission fail startup with the entity named.
- Ship the standard operations with their rules: query returning spec 0012's `RowPage` with search,
  capped count and ancestors; details with the related projection, extras and row echo; save with
  child synchronisation, property ownership, temporary ids, two-stage pre-check, bounded validation
  rounds and the in-transaction post-check; delete by ids, by query and with descendants; get by
  parent ids; get all cached; built-in and custom actions.
- Ship the validation framework: contexts, the batched and deduplicated `IContextLoader`, parked
  validators and bounded rounds, errors carrying a structured `ValidationPath` beside a code and
  arguments, the platform codes, index-backed uniqueness and database-checked foreign keys.
- Ship the capability recipes for activatable, tree, temporal, blob-referencing, multilingual,
  cacheable, job-backed, searchable and Excel-capable entities as projections of the entity's own
  shape.
- Ship the concurrency rule (`ModifiedAt` as the stamp, `Check | Override`), the closed exception
  set the web layer maps by type, the SQL error translation, the Excel row source, the
  `tellma.crud.*` instruments and the round-trip budget the conformance tests assert.
- Ship `StackConformanceTests<TEntity, TKey>` in `Tellma.Core.Testing` so a distribution proves
  every stack against the same contract.

**Non-goals (explicitly out of scope)**

- **The batch executor, the save emitter, the id allocator, the tree and delete statements, the
  materializer and the engine amendments** — spec 0012; this spec composes them.
- **The connect prologue, the guarded runner's SQL, securable descriptors and permission
  evaluation** — spec 0014; this spec calls `IGuardedBatchRunner` and `IAccessEvaluator`.
- **Version tags, the cache, settings and localization** — spec 0013; the pipeline declares no
  bump by hand and renders no message.
- **Endpoint projection, the MCP server, problem details, JSON options and limits per surface** —
  spec 0016.
- **Blob storage, staging and the download endpoint** — spec 0017; the capability's validator and
  effect are registered by the blob feature and run inside this pipeline.
- **`UserService`, `RoleService`, `CenterService` and the GL module** — spec 0018; they are
  subclasses of the base defined here.
- **The Excel codec, manifests, natural-key resolution and chunked imports** — spec 0019; this spec
  ships only the row source it streams from and the save it calls.
- **Job leasing, schedules, notifications and the hub** — specs 0020 and 0021.
- **Deferred capabilities**: `[GatedBy]` (no user once `IsActive` is server-owned), composite
  natural keys, the public API's plumbing (spec 0016 §12.2).

## 1. Placement and architecture

### 1.1 Projects, namespaces, and dependency edges

| Piece | Location | References | Contents |
|---|---|---|---|
| Contracts | `src/core/Tellma.Core.Abstractions/`, namespaces `Tellma.Core.Abstractions.Crud`, `.Validation`, `.Errors` | `Tellma.Core.Queryex` only (the package's one non-BCL edge; `FilterTree`, `QuerySpec`, `QueryexDiagnostic`, `KeySetRestriction` appear in contracts) | `EntityService<,>`, `IEntityPipeline<,>`, `IEntityPipelineFactory`, `IEntityBehavior<,>`, `IApiActionInvoker`, the components, the stack types and annotations, requests, options, contexts, the loader with `ContextRef<>`, errors, `ValidationPath`, codes, `CrudTelemetryNames`, the exception set |
| Runtime | `src/core/Tellma.Core/`, namespace `Tellma.Core.Crud` | `Tellma.Core.Abstractions`, the data layer inside `Tellma.Core` (spec 0012), the access layer (spec 0014), caching (spec 0013) | `EntityPipeline<,>` (sealed; one instance per service instance, created by `EntityPipelineFactory`), `EntityPipelineFactory`, `ApiActionInvoker`, `StackRegistry`, `StackDescriptorBuilder`, `StackSecurableContributor`, `ContextLoader`, `ValidationScheduler`, `QueryPlanner`, `DetailsPlanner`, `ActivatableRecipe`, `TreeRecipe`, `CacheableRecipe`, `ExcelRowSource<,>`, the realizers of the stack contribution items, the startup checks |
| Module consumer | `src/module/gl/Tellma.Module.Gl/` (spec 0018) | `Tellma.Core.Abstractions` only — never `Tellma.Core` | `CenterService<TCenter> : EntityService<TCenter>` compiles against Abstractions and receives the pipeline factory through its constructor |
| Conformance base | `src/core/Tellma.Core.Testing/` | `Tellma.Core.Abstractions`, xUnit v3 | `StackConformanceTests<TEntity, TKey>` (§18.3) |
| Unit tests | `test/core/Tellma.Core.Tests/` (`Crud/` folder) | — | the pipeline over a scripted batch double, the attribute walker, the path segments, the parking scheduler property tests, exception translation, ceilings, the API golden |
| Integration tests | `test/core/Tellma.Core.IntegrationTests/` (`Crud/` folder), over spec 0012's fixture schema (`TellmaFixtureDbContext`, §18.2) | LocalDB or Testcontainers | every semantic of §5–§13 against a real database |

`Tellma.Core.Abstractions` stays EF-free and framework-free: nothing in `.Crud`, `.Validation` or
`.Errors` names an EF Core, SqlClient or ASP.NET Core type. The runtime types above are internal to
`Tellma.Core` except where a contract names them; a distribution reaches them only through
composition.

Contract blocks are C# sketches: names and shapes are normative; `using` directives, XML
documentation, cancellation-token parameters, method bodies and accessibility details are omitted,
so a block is never pasted into code. SQL statements are the exact shape to emit. Labelled
illustrations show a distribution author's call site.

### 1.2 Design principles

1. **The transaction is the persist text.** No `SqlTransaction`, no `TransactionScope`, no lock
   held across client code. A save is at most one read round trip per validation round and one
   persist round trip whose text opens and commits the transaction.
2. **Every check where its input first exists.** Shape, ceilings and the type-level permission
   before any round trip; existence and visibility in a filtered load; concurrency, uniqueness,
   cycles, the row-level post-check and the permissions guard inside the transaction.
3. **Declared once, projected everywhere.** The entity's base class, capability interfaces and
   annotations plus `[EntityAction]`/`[ApiAction]` on the service are the whole declaration.
   `StackDescriptor` is the only thing HTTP, MCP, the SPA schema, securable registration, Excel
   and the conformance tests read; none re-derives.
4. **Non-virtual operations over a sealed pipeline.** A service overrides hooks, never steps. No
   subclass can forget the post-check.
5. **One class is the storage shape and the wire shape.** Which members a client may set is
   metadata (`PropertyOwnership`), never a second DTO hierarchy.
6. **Codes, not prose.** Validation and problem outcomes are stable codes with named arguments;
   messages are rendered by spec 0016 under the request culture.
7. **Bulk everywhere.** Every operation takes and returns collections; the details page sends an
   array of one; import calls the same save.

### 1.3 Vocabulary

A **stack** is one top-level entity's CRUD feature: entity, service, descriptor, securables,
operations. A **securable** is a `(Resource, Action)` pair a permission grants; the resource is the
entity name `<schema>.<Entity>` (`core.User`, `gl.Center`), spec 0012's `EntityMetadata.Name`, and
actions are PascalCase (`Read`, `Save`, `Delete`, `Activate`, `Invite`); the display form is
`resource:action`. A **round trip** is one command on one connection. A **round** is one dispatch of
the validation scheduler. The four **audit columns** are `CreatedAt`, `CreatedById`, `ModifiedAt`,
`ModifiedById`; the **stamp** is `ModifiedAt`. Tables are plural and schema-qualified
(`gl.Centers`); the entity name is singular and schema-qualified (`gl.Center`) and is also the
Queryex name.

## 2. Stacks: declaration, descriptor, registration

### 2.1 What declares a stack

A stack is registered by type at composition: `contribution.Entity<TEntity>()` registers the entity
with the default `EntityService<TEntity>`; `contribution.Entity<TEntity, TService>()` names a
service class; both produce a `StackContributionItem` (spec 0011's `FeatureContributionItem`) that
`Tellma.Core` realises. A distribution substitutes its leaf with `tellma.UseEntity<TDefault,
TLeaf>()` where `TLeaf : TDefault` is the one constraint for every entity kind: the stack, its
service closed over the leaf, and its securables follow. Registration is explicit; a startup check
fails when a class deriving from `EntityService<T>` was registered by no stack (§2.6).

*Illustration* — a pack's composition and a distribution's substitution:

```csharp
contribution.Entity<Center, CenterService<Center>>();          // in GlFeature.Contribute
builder.AddTellma("acme", t => t.AddFeature<GlFeature>().UseEntity<Center, MyCenter>());
```

The declaration of a stack's capabilities is the entity's own shape (spec 0012's bases,
`IActivatable`, `IJobEntity`, and the annotations of spec 0012's entity contract) plus
`[EntityAction]` and `[ApiAction]` methods on the service. Nothing is declared twice.

### 2.2 The descriptor and the registry

```csharp
// Tellma.Core.Abstractions.Crud
public sealed record StackDescriptor(
    string Resource, string ResourceSegment, Type EntityType, Type ServiceType, Type KeyType,
    string? Description, StackOperations Operations, IReadOnlySet<string> Capabilities,
    IReadOnlyList<ActionDescriptor> Actions, IReadOnlyList<PropertyMetadata> Properties,
    IReadOnlyList<ChildCollectionDescriptor> Children,
    IReadOnlyList<(string, SearchKind)> SearchableProperties, IReadOnlyList<string> NaturalKeys,
    string DefaultSelect, IReadOnlySet<string> RelatedSelect,
    IReadOnlyList<string> DetailsExpand, StackLimits Limits, McpExposure Mcp, int ContractRevision);

public abstract record ActionDescriptor(
    string Name, string? Description, bool Mutation, bool Idempotent, bool Destructive, McpExposure Mcp,
    int ContractRevision, Type ResultType);

public sealed record EntityActionDescriptor(
    string Name, string? Description, bool Mutation, bool Idempotent, bool Destructive, McpExposure Mcp,
    int ContractRevision, Type ResultType, string Action, bool SupportsFilter, bool LoadChildren,
    bool PostCheck, bool SingleTarget, bool IsBuiltIn, Type? ArgumentsType)
    : ActionDescriptor(Name, Description, Mutation, Idempotent, Destructive, Mcp, ContractRevision, ResultType);

public sealed record ApiActionDescriptor(
    string Name, string? Description, bool Mutation, bool Idempotent, bool Destructive, McpExposure Mcp,
    int ContractRevision, Type ResultType, SecurableRef? Securable, Type? RequestType, Type HandlerType)
    : ActionDescriptor(Name, Description, Mutation, Idempotent, Destructive, Mcp, ContractRevision, ResultType);

public sealed record ChildCollectionDescriptor(string Property, Type ChildType, string ParentKeyProperty, int MaxCount);

public sealed record StackLimits(
    int MaxTake, int CountCap, int MaxSkipWindow, int MaxSaveCount, int MaxRowsPerSave, int MaxIds,
    int MaxDeleteByQueryRows, int MaxExpandDepth, int MaxValidationRounds, int MaxSearchLength,
    int MaxExtras);

[Flags]
public enum StackOperations
{
    None = 0, Query = 1, Details = 2, Save = 4, Delete = 8, Import = 16, Export = 32,
    Enlist = 64,                                        // accepts enlisted saves and deletes (§13.3); no securable, segment or member
    Read = Query | Details | Export,
    All = Read | Save | Delete | Import
}

public sealed record ApiServiceDescriptor(
    string Route, Type ServiceType, IReadOnlyList<ApiActionDescriptor> Actions);

public sealed record EnlistmentDescriptor(Type WriterKey, string WriterName, string TargetResource);   // one declared writer → target pair (§13.3)

public interface IStackRegistry                         // singleton; built once at startup
{
    IReadOnlyList<StackDescriptor> Stacks { get; }
    IReadOnlyList<ApiServiceDescriptor> Services { get; }
    IReadOnlyList<EnlistmentDescriptor> Enlistments { get; }    // the declared pair graph over stacks; acyclic apart from self-pairs (§13.3)
    StackDescriptor? Find(string resource);
    StackDescriptor? OwnerOf(TableName table);          // root or child table → the owning stack; null when unowned
}

public sealed record StackContributionItem(Type EntityType, Type? ServiceType, StackOperations? Operations)
    : FeatureContributionItem;
public sealed record StackCompanionContributionItem(Type EntityType, Type CompanionType)
    : FeatureContributionItem;                          // §11.3
public sealed record ApiServiceContributionItem(Type ServiceType) : FeatureContributionItem;
```

| Member | Meaning |
|---|---|
| `Resource` | The entity name (spec 0012's `EntityMetadata.Name`, `gl.Center`): the Queryex root, the securable resource and the `FilterRoot` of the stack's securables; fork-stable — a distribution leaf inheriting a pack default keeps the pack's name. |
| `ResourceSegment` | The kebab-case plural of the table (`centers`, `invoice-lines`), overridden by `[ApiResource(Segment = …)]` (spec 0016); routing only, never a securable. |
| `EntityType`, `ServiceType`, `KeyType` | The leaf, the closed service type, `int` or `long`. |
| `Operations` | `[Stack(Operations)]` on the entity, overridden by `StackContributionItem.Operations`; default `All`. An absent operation is not projected, registers no securable, and its service member throws `InvalidOperationException` (an authoring bug, never a user outcome). `Enlist` is outside `All`: a stack accepts enlisted writes (§13.3) only by naming it. |
| `Capabilities` | Closed vocabulary: `activatable`, `tree`, `temporal`, `multilingual`, `cacheable`, `blobs`, `job-entity`, `searchable`, `excel` (§12). |
| `Actions` | Every action of the stack, each as its kind: an `EntityActionDescriptor` per built-in action (`activate`, `deactivate`) and per `[EntityAction]` method, an `ApiActionDescriptor` per `[ApiAction]` method. Order: the built-in actions, the service's `[EntityAction]` and `[ApiAction]` methods in declaration order, then the `[ApiAction]` methods of each stack companion (§11.3; spec 0019's `ExcelOperations<T>`) in registration order. |
| `ActionDescriptor` | The members both kinds share. `Name` is the action's whole route under the stack or route segment (§2.3); `Description`, `Mutation`, `Idempotent`, `Destructive`, `Mcp` and `ContractRevision` come from the attribute (§2.3), or from the capability for a built-in action (§11.1). `ResultType` is what the caller receives: for an `EntityActionDescriptor`, `EntitiesResult<TEntity>` when the method returns no value, else the method's `TResult` (§3.1, §11.2); for an `ApiActionDescriptor`, the method's result type. |
| `EntityActionDescriptor` | `Action`, `SupportsFilter`, `LoadChildren`, `PostCheck` and `SingleTarget` as `[EntityAction]` declares them (§2.3), or as the capability declares a built-in action, which carries `IsBuiltIn` (§11.1); `ArgumentsType` is the method's `TArguments`, null when it takes none. The handler is the stack's `ServiceType`, the one class an `[EntityAction]` is declared on. |
| `ApiActionDescriptor` | `Securable` is the spec 0014 §4.1 `SecurableRef(Resource, Action)` the invoker evaluates (§11.3) — the stack's `Resource` for an action of the entity service or a stack companion, the attribute's `Resource` for an `[ApiRoute]` action — and null exactly for a member-only action (`MemberOnly = true`); `RequestType` is the body type, null when the method takes none; `HandlerType` is the type declaring the method — the service, the companion closed over the leaf, or the `[ApiRoute]` service — which `IApiActionInvoker` (§11.3) resolves from DI per request. |
| `Properties`, `Children` | From spec 0012's `EntityMetadata` for the leaf: every mapped property with its `PropertyOwnership`, and every `[NotMapped]` child collection with the `MaxCount` its `[MaxChildren]` declares. |
| `SearchableProperties`, `NaturalKeys` | The `[Searchable(Kind)]` string properties and `EntityMetadata.NaturalKeys` in order. |
| `DefaultSelect` | `[DefaultSelect]` text, else the platform default: `Id`, the natural keys, the `[Multilingual]` `Name` group, `Code`, `IsActive`, the four audit columns — the columns pickers and the MCP query tool use when they pass nothing. |
| `RelatedSelect` | The projection visible through navigations (spec 0012's default when `[RelatedSelect]` is absent: `Id`, the `Name` group, `Code`, `Avatar`-preset blob columns). |
| `DetailsExpand` | `[DetailsExpand]` navigations, else every foreign-key navigation of the entity and its children at depth 1. |
| `Limits` | `[Stack(...)]` ceilings with the platform defaults of §2.3. |
| `Mcp`, `ContractRevision`, `Description` | From `[ApiResource]` (spec 0016) when present; defaults `Full`, `0`, the entity's XML summary. `ContractRevision` is bumped by hand when the stack's wire meaning changes with no change of shape, and enters the contract fingerprint of every endpoint of the stack (spec 0016 §3.10). |

### 2.3 Annotations

```csharp
// Tellma.Core.Abstractions.Crud
// on entity type; inherited
public sealed class StackAttribute(
    StackOperations Operations = StackOperations.All, int MaxTake = 10000, int CountCap = 10000,
    int MaxSkipWindow = 100000, int MaxSaveCount = 10000, int MaxRowsPerSave = 100000,
    int MaxIds = 10000, int MaxDeleteByQueryRows = 10000, int MaxExpandDepth = 3,
    int MaxValidationRounds = 3, int MaxSearchLength = 200, int MaxExtras = 16,
    string? DefaultSelect = null)
    : Attribute;

// on a method of an EntityService returning Task, ValueTask, Task<TResult> or ValueTask<TResult>: M(ActionContext<TEntity, TKey> context [, TArguments arguments])
public sealed class EntityActionAttribute(
    string Name, string? Action = null, bool SupportsFilter = true, bool LoadChildren = false,
    bool PostCheck = false, bool SingleTarget = false, bool Mutation = true,
    bool Idempotent = false, bool Destructive = false, McpExposure Mcp = McpExposure.Full,
    string? Description = null, int ContractRevision = 0)
    : Attribute;

// on a public method taking one body (or none) on an entity service, a stack companion or an [ApiRoute] service
public sealed class ApiActionAttribute(
    string Name, string? Action = null, string? Resource = null, bool MemberOnly = false,
    bool Mutation = true, bool Idempotent = false, bool Destructive = false,
    McpExposure Mcp = McpExposure.Full, string? Description = null, int ContractRevision = 0)
    : Attribute;

// on a non-entity service class ("inbox", "settings", "access")
public sealed class ApiRouteAttribute(string Route) : Attribute;
```

| Member | Meaning |
|---|---|
| `[EntityAction].Name` | One or more kebab-case segments joined by `/` (`invite`, `take-over`, `preferences/set`; `[ApiAction].Name` likewise, `me/save`): the path under the stack or route segment, which spec 0016 maps at its literal path, and the MCP action name (spec 0016 §11.5); unique per stack over the whole string; reserved names (the standard operation segments of §2.4) are a startup failure. `Action` defaults to the PascalCase form of the last segment (`Invite`, `TakeOver`, `preferences/set` → `Set`); several actions may share one (`activate`/`deactivate` → `Activate`). |
| `[EntityAction].SupportsFilter` | The securable is registered with `FilterRoot = Resource`; the action's target rows are loaded under the grant's filter. `false` registers `FilterRoot = null` and the grant is all-or-nothing. |
| `[EntityAction].LoadChildren` | The target rows arrive with every child collection populated (one statement per collection, keyed by the target ids). |
| `[EntityAction].PostCheck` | Re-run the grant's filter over the target rows after the write (§9.3); off by default because an action commonly moves a row out of the filter that permitted it. |
| `[EntityAction].SingleTarget` | The action targets exactly one row: spec 0016 maps its body as `IdRequest` or `IdRequest<TArguments>` (spec 0016 §3.3) and its MCP action takes one `id` (spec 0016 §11.5); the pipeline runs §11.2 over a one-element list; the method must return its own result (§3.1), so the answer is the method's `TResult` and never a read-back. |
| `[EntityAction].Mutation`, `[ApiAction].Mutation` | `ActionDescriptor.Mutation`: whether the operation writes tenant data; the tenant-state verdict (spec 0011 §3.3) and the MCP read-only gate (spec 0016 §11.6) read it and nothing else. A read-shaped action declares `Mutation = false`. Standard operations are classified once: `query`, `get`, `get-by-ids`, `get-by-parent-ids`, `all`, `export`, `export-for-import` and `inspect-import` are not mutations; `save`, `delete`, `delete-by-query`, `delete-with-descendants`, `activate`, `deactivate`, `import`, `export/start`, `export-for-import/start` and `import/start` (each of the last three enqueues a job) are. |
| `[EntityAction]` and `[ApiAction]` `Idempotent`, `Destructive`, `Mcp`, `Description`, `ContractRevision` | Projection metadata read by spec 0016: `Idempotent` is retry safety (spec 0016 §2.2) and says nothing about writing — a mutation may be retry-safe; `Destructive` is confirmation, `Mcp` tool exposure, `Description` tool text; `ContractRevision` (default 0) is bumped by hand when the action's meaning changes with no change of shape, and enters its endpoint's contract fingerprint (spec 0016 §3.10). |
| `[ApiAction].Action` | The securable action the operation requires on the entity service's resource (`users/invitation-status` names `Read`, spec 0018); `MemberOnly = true` means any connected active member and no securable. On an `[ApiRoute]` service `Resource` is required beside `Action` unless `MemberOnly`. |
| `[Stack].Operations` | `Read` for lookups; `Query \| Details \| Delete \| Enlist` for platform-written stacks such as spec 0019's `Export`, whose rows arrive only by enlistment (§13.3); `All` otherwise, plus `Enlist` on any stack an enlisted write targets (§13.3). |
| `[Stack]` ceilings | §2.5. |

### 2.4 Standard operations, segments, and securable actions

| Operation | Service member | Exists when | Segment | Securable action |
|---|---|---|---|---|
| Query | `QueryAsync` | `Query` | `query` | `Read` |
| Details, one | `GetByIdAsync` | `Details` | `get` | `Read` |
| Details, many | `GetByIdsAsync` | `Details` | `get-by-ids` | `Read` |
| All cached | `GetAllCachedAsync` | `Details` and `cacheable` | `all` | `Read` (`FilterRoot = null`) |
| Get by parent ids | `GetByParentIdsAsync` (extension) | `Query` and `tree` | `get-by-parent-ids` | `Read` |
| Save | `SaveAsync` | `Save` | `save` | `Save` |
| Delete by ids | `DeleteByIdsAsync` | `Delete` | `delete` | `Delete` |
| Delete by query | `DeleteByQueryAsync` | `Delete`; web surface only | `delete-by-query` | `Delete` |
| Delete with descendants | `DeleteWithDescendantsAsync` (extension) | `Delete` and `tree` | `delete-with-descendants` | `Delete` |
| Activate, deactivate | `ActivateAsync`, `DeactivateAsync` (extensions) | `Save` and `activatable` | `activate`, `deactivate` | `Activate` |
| Export | spec 0019's `ExcelOperations<T>`, a stack companion (§11.3) | `Export` | `export`, `export/start` | `Read` |
| Export for import | spec 0019's `ExcelOperations<T>`, a stack companion (§11.3) | `Import` | `export-for-import`, `export-for-import/start` | `Read` |
| Inspect import, import | spec 0019's `ExcelOperations<T>`, a stack companion (§11.3) | `Import` | `inspect-import`, `import`, `import/start` | `Save` |
| Custom bulk action | an `[EntityAction]` method | declared | its `Name` | its `Action` |
| Custom operation | an `[ApiAction]` method | declared | its `Name` | its `Action` |
| Enlisted save, enlisted delete | none — `EnlistSaveAsync`/`EnlistDelete` on the writer (§13.3) | `Enlist` | — | — (the host's) |

Segments and action names are derived separately: the securable id is the entity name ×
PascalCase action; the segment is kebab-case and belongs to spec 0016's projection only. Whether
an operation is a mutation is §2.3's `Mutation` classification.

### 2.5 Ceilings

Every ceiling lives on the stack, defaulted by the platform and overridable per entity with
`[Stack(...)]`; the same numbers bind the web surface, MCP, import and background callers, and
spec 0016 may only lower them per surface (the web surface's `MaxEntitiesPerSave = 1000` and MCP's
`MaxTop = 500` are such lowerings). Exceeding one throws `LimitExceededException(Limit, Actual,
Maximum)` before any round trip; a value is never silently clamped, because clamping breaks paging
arithmetic.

| Ceiling | Default | Bounds |
|---|---|---|
| `MaxTake` | 10,000 | `QueryRequest.Take`; absent = 50 |
| `CountCap` | 10,000 | the capped grand-total count; `RowPage.Count` is `cap + 1` above it (spec 0012 §3.3) |
| `MaxSkipWindow` | 100,000 | `QueryRequest.Skip + Take` (`Limit = "skipWindow"`) |
| `MaxSaveCount` | 10,000 | entities per `SaveAsync`; the codec's import chunk never exceeds it (spec 0019 §12.4) |
| `MaxRowsPerSave` | 100,000 | rows a save carries at every depth; the codec's chunk never exceeds it |
| `MaxIds` | 10,000 | ids per `GetByIdsAsync`, `DeleteByIdsAsync`, `ExecuteActionAsync`, `GetByParentIdsAsync` |
| `MaxDeleteByQueryRows` | 10,000 | rows a delete by query may match (`THROW 50413` in SQL) |
| `MaxExpandDepth` | 3 | navigation depth of the details projection |
| `MaxValidationRounds` | 3 | dispatches of the validation scheduler per save, delete or action |
| `MaxSearchLength` | 200 | characters of `Search` |
| `MaxExtras` | 16 | extras per details request |

A child collection's own cap is `[MaxChildren]` (spec 0012 §2.2), 10,000 rows per parent by default,
enforced per parent at every depth. The plan-lane boundary is spec 0012 §6.1's
`DataOptions.LargeBatchThreshold`, a host-wide value and never a stack ceiling.

### 2.6 Startup checks

The stack feature registers one `IStartupCheck` (spec 0011's realised gate) per rule; every problem
names the entity, the service or the method. It fails startup on: a class deriving from
`EntityService<T>` registered by no stack; two stacks over one entity; a `[Stack]` operation set
that omits `Details` while keeping `Save` (a save reads back through the details plan); `Import` in
a `[Stack]` operation set without `Query`, `Details` and `Save`, or `Export` without `Query`; an
`[EntityAction]` on a class that is not the stack's service, with a non-conforming signature, with a
duplicate or reserved `Name`, whose `TArguments` is not a JSON-serialisable record, or declaring
`SingleTarget` on a method that returns no result; an `[ApiAction]` with more than one body
parameter, or on an `[ApiRoute]` service with neither `Resource` nor `MemberOnly`; a stack companion
registered for an entity no stack owns, declaring no `[ApiAction]`, or whose action `Name` collides
with a service action or a standard segment; a `[Searchable]` property that is not a string; a
`[Unique]` or `[NaturalKey]` without a backing unique index (spec 0012's check, reported here with
the stack named); a `[DefaultSelect]`, `[RelatedSelect]` or `[DetailsExpand]` text that fails
`Validate` against the tenant-independent schema; a `[Cacheable]` stack whose `Read` securable would
carry a filter root; an `IDetailsContributor<T>` declaring an extra name twice; a `ContributeAsync`
participant registered for an entity no stack owns; a registration of `IEntityPipelineFactory` or of
any `IEntityPipeline<,>` whose implementation type is not the platform's. The check
`core.enlistments` fails startup on an `IEnlists<T>` whose `T` is no stack's entity or whose stack
omits `Enlist`, and on a cycle in the declared pair graph other than a self-pair (§13.3). The
securables audit — that every projected operation, every `EntityActionDescriptor.Action` and every
non-null `ApiActionDescriptor.Securable` is registered — is spec 0014's check over `IStackRegistry`,
run in the same gate.

### 2.7 Securable registration

`StackSecurableContributor` (`Tellma.Core.Crud`, an `ISecurableContributor` of spec 0014) reads
`IStackRegistry` and registers, per stack, every standard operation's action of §2.4 as
`(Resource, Action, FilterRoot = Resource)`, every `EntityActionDescriptor.Action` (with
`FilterRoot = Resource` when its `SupportsFilter`, else `null`), every non-null
`ApiActionDescriptor.Securable` of the service and of its companions with `FilterRoot = Resource`,
and — for `[Cacheable]` stacks — `Read` with `FilterRoot = null`. For every `[ApiRoute]` service it
registers each non-null `ApiActionDescriptor.Securable` with `FilterRoot = null`. It reads the
descriptors of §2.2, never the attributes. `IsSensitive` defaults are spec 0014's table. A
distribution therefore writes no securable registration for its own entities and actions; spec
0011's `Securables(...)` exists for non-entity resources only.

## 3. The service base, the pipeline, and the hooks

### 3.1 `EntityService<TEntity, TKey>`

```csharp
// Tellma.Core.Abstractions.Crud
public abstract class EntityService<TEntity, TKey>
    where TEntity : Entity<TKey>
    where TKey : struct
{
    protected EntityService(IEntityPipelineFactory factory);
    public StackDescriptor Descriptor { get; }
    public Task<RowPage> QueryAsync(QueryRequest request);
    public Task<EntitiesResult<TEntity>> GetByIdAsync(TKey id, DetailsRequest request);
    public Task<EntitiesResult<TEntity>> GetByIdsAsync(IReadOnlyList<TKey> ids, DetailsRequest request);
    public Task<EntitiesResult<TEntity>> GetAllCachedAsync(DetailsRequest request);
    public Task<EntitiesResult<TEntity>> SaveAsync(IReadOnlyList<TEntity> entities, SaveOptions options);
    public Task<AffectedResult> DeleteByIdsAsync(
        IReadOnlyList<TKey> ids, IReadOnlyList<DateTimeOffset?>? expectedStamps = null);   // stamps are datetimeoffset(7)
    public Task<AffectedResult> DeleteByQueryAsync(DeleteByQueryRequest request);
    public Task<EntitiesResult<TEntity>> ExecuteActionAsync(
        string action, IReadOnlyList<TKey> ids, object? arguments, ActionOptions options);    // an action without a result
    public Task<TResult> ExecuteActionAsync<TResult>(
        string action, IReadOnlyList<TKey> ids, object? arguments, ActionOptions options);    // an action with its own result
    public IExcelRowSource ExcelRowSource(ExportSource source);
    // overridable hooks, empty defaults; the pipeline sees them through IEntityBehavior
    protected virtual Task PreprocessAsync(SaveContext<TEntity> context);
    protected virtual Task ValidateAsync(SaveContext<TEntity> context);
    protected virtual Task ValidateDeleteAsync(DeleteContext<TEntity, TKey> context);
    protected virtual Task ValidateActionAsync(string action, ActionContext<TEntity, TKey> context);
    protected virtual Task ContributeAsync(PersistContext<TEntity> context);
    protected virtual Task AfterCommitAsync(PersistOutcome<TEntity> outcome);
    protected virtual void ContributeDetails(DetailsPlan<TEntity, TKey> plan);
    protected virtual FilterTree? SearchFilter(string search);
}

public abstract class EntityService<TEntity> : EntityService<TEntity, int>;

// Tellma.Core.Crud implements; public because EntityService lives in Abstractions; not for distributions
public interface IEntityPipeline<TEntity, TKey>
{
    Task<RowPage> QueryAsync(QueryRequest request);
    Task<EntitiesResult<TEntity>> GetByIdAsync(TKey id, DetailsRequest request);
    Task<EntitiesResult<TEntity>> GetByIdsAsync(IReadOnlyList<TKey> ids, DetailsRequest request);
    Task<EntitiesResult<TEntity>> GetAllCachedAsync(DetailsRequest request);
    Task<EntitiesResult<TEntity>> SaveAsync(IReadOnlyList<TEntity> entities, SaveOptions options);
    Task<AffectedResult> DeleteByIdsAsync(
        IReadOnlyList<TKey> ids, IReadOnlyList<DateTimeOffset?>? expectedStamps);
    Task<AffectedResult> DeleteByQueryAsync(DeleteByQueryRequest request);
    Task<EntitiesResult<TEntity>> ExecuteActionAsync(
        string action, IReadOnlyList<TKey> ids, object? arguments, ActionOptions options);
    Task<TResult> ExecuteActionAsync<TResult>(
        string action, IReadOnlyList<TKey> ids, object? arguments, ActionOptions options);
    Task<QueryRowSet> GetByParentIdsAsync(ParentIdsRequest request);                        // tree stacks
    Task<AffectedResult> DeleteWithDescendantsAsync(IReadOnlyList<TKey> ids);               // tree stacks
    IExcelRowSource CreateExcelRowSource(ExportSource source);
    StackDescriptor Descriptor { get; }
}

// Tellma.Core implements; not for distributions
public interface IEntityPipelineFactory
{
    IEntityPipeline<TEntity, TKey> Create<TEntity, TKey>(IEntityBehavior<TEntity, TKey> behavior)
        where TEntity : Entity<TKey>
        where TKey : struct;
}

// the hooks as the pipeline sees them; EntityService implements it explicitly
public interface IEntityBehavior<TEntity, TKey>
{
    Task PreprocessAsync(SaveContext<TEntity> context);
    Task ValidateAsync(SaveContext<TEntity> context);
    Task ValidateDeleteAsync(DeleteContext<TEntity, TKey> context);
    Task ValidateActionAsync(string action, ActionContext<TEntity, TKey> context);
    Task ContributeAsync(PersistContext<TEntity> context);
    Task AfterCommitAsync(PersistOutcome<TEntity> outcome);
    void ContributeDetails(DetailsPlan<TEntity, TKey> plan);
    FilterTree? SearchFilter(string search);
}

// extension methods on EntityService; the constraint is the compile-time gate
public static class EntityServiceExtensions
{
    public static Task<EntitiesResult<TEntity>> ActivateAsync<TEntity, TKey>(
        this EntityService<TEntity, TKey> service, IReadOnlyList<TKey> ids, ActionOptions? options = null)
        where TEntity : IActivatable;
    public static Task<EntitiesResult<TEntity>> DeactivateAsync<TEntity, TKey>(
        this EntityService<TEntity, TKey> service, IReadOnlyList<TKey> ids, ActionOptions? options = null)
        where TEntity : IActivatable;
    public static Task<QueryRowSet> GetByParentIdsAsync<TEntity, TKey>(
        this EntityService<TEntity, TKey> service, ParentIdsRequest request)
        where TEntity : TreeEntity<TKey>;
    public static Task<AffectedResult> DeleteWithDescendantsAsync<TEntity, TKey>(
        this EntityService<TEntity, TKey> service, IReadOnlyList<TKey> ids)
        where TEntity : TreeEntity<TKey>;
}
```

| Member | Meaning |
|---|---|
| Operations | Non-virtual; each forwards to the pipeline the factory bound to this service. A subclass cannot override a step. |
| `GetByIdAsync` | `NotFoundException` when the id is absent or invisible under the caller's `Read` filter; hidden and missing are indistinguishable. |
| `GetByIdsAsync` | Partial: absent or invisible ids are omitted; `EntitiesResult.Ids` lists what was returned in request order. Never throws for absence. |
| `GetAllCachedAsync` | `[Cacheable]` stacks only (§5.5; the cacheable row of §12); served from spec 0013's `ICacheableEntities` under the tenant `entity:<Name>` tag; no row-level filter (the securable carries none). |
| `SaveAsync` | §6. Takes an array; the details page sends one. |
| `DeleteByIdsAsync` | All-or-nothing over the ids (§10.1); `expectedStamps`, when given, is parallel to `ids`; a `null` entry beside an id is `Concurrency.StampRequired` at `Ids[i]`. |
| `ExecuteActionAsync` | Built-in or `[EntityAction]` actions by `Name` (§11), through the overload the action's `EntityActionDescriptor.ResultType` (§2.2) selects: an action whose method returns no value answers `EntitiesResult<TEntity>` — the target ids and, per `ActionOptions.ReturnEntities`, the read-back; an action with its own result answers the method's `TResult` through `ExecuteActionAsync<TResult>`, and no read-back runs. An unknown name, or the overload that does not match the action, is `InvalidOperationException` (an authoring bug). |
| `ExcelRowSource` | The row source spec 0019's codec streams from, bound to one `ExportSource` case (§4.2, §15). |
| `PreprocessAsync` | After the platform's normalisation and before id assignment: defaults, derived fields, every `[Derived]` value, normalisations (`User.Email` lower-casing). Runs once, before round 1. |
| `ValidateAsync`, `ValidateDeleteAsync`, `ValidateActionAsync` | Rules with batched context loads (§7). The service's hook runs first; components follow in registration order. |
| `ContributeAsync` | Transactional participation: statements appended to the persist batch with declared writes, notifications, job rows (§13.1). |
| `AfterCommitAsync` | Best-effort post-commit work over the `PersistOutcome` (§13.2). |
| `ContributeDetails` | Extras for the details read (§5.3). |
| `SearchFilter` | `null` keeps the platform disjunction over `[Searchable]` columns (§5.2); a returned tree replaces it. |

`IEntityPipeline<TEntity, TKey>` and `IEntityBehavior<TEntity, TKey>` are public contracts marked
not-for-distributions (`EditorBrowsable(Never)` in code) because `EntityService` lives in
Abstractions and `Tellma.Module.Gl` must compile against it while the implementation lives in
`Tellma.Core`. The base constructor calls `factory.Create(this)` once and holds the pipeline, so
every operation runs against a pipeline bound to this service; DI registers the factory once and a
pipeline instance exists per service instance, both scoped. A distribution's service subclass passes
the factory through to the base constructor and overrides hooks; DI resolves
`IEntityPipelineFactory`.

### 3.2 Composable components

```csharp
// Tellma.Core.Abstractions.Crud
public interface IEntityValidator<in TEntity>
{
    Task ValidateAsync(SaveContext<TEntity> context);
    Task ValidateDeleteAsync(DeleteContext<TEntity> context);           // default: nothing
    Task ValidateActionAsync(string action, ActionContext<TEntity> context);   // default: nothing
}

public interface IPersistEffect<in TEntity>
{
    Task ContributeAsync(PersistContext<TEntity> context);
    Task AfterCommitAsync(PersistOutcome<TEntity> outcome);             // default: nothing
}

public interface IDetailsContributor<in TEntity>
{
    IReadOnlyList<string> Extras { get; }                               // the names it can produce
    void Contribute(DetailsPlan<TEntity> plan, IReadOnlySet<string> requested);
}
```

Components exist for concerns with multiplicity — a pack or compliance library that cannot subclass
the distribution's service but must add a rule; the blob capability's validator and effect (spec
0017); spec 0014's `UserAccessRules<TUser>` and `RoleAccessRules<TRole>`. They are registered
through spec 0011's `contribution.Validator<TEntity, TValidator>()`, `PersistEffect<,>()` and
`DetailsContributor<,>()`; they are contravariant, so a component registered for a base (`Center`)
runs for the leaf (`MyCenter`); the pipeline resolves them once per stack at startup for the leaf
and every base up to `Entity<TKey>`. A component may implement `IEnlists<TTarget>` and, from its
validation methods, enlist writes into another stack through the context's `Host` (§13.3). Order:
the service's own hook first, then components in registration order — feature order (the `Requires`
closure) then declaration order within a feature. `DeleteContext<TEntity>`,
`ActionContext<TEntity>`, `DeletePersistOutcome<TEntity>`, `ActionPersistOutcome<TEntity>` and
`DetailsPlan<TEntity>` are the key-erased views of the contexts of §7, the outcomes of §13.1 and the
plan of §5.3 that a component written against a base sees; `SaveContext<TEntity>`, the
`PersistContext<TEntity>` kinds of §13.1 and `SavePersistOutcome<TEntity>` carry no key and have no
erased view.

### 3.3 Pack services and distribution extension

A pack ships its default entity non-abstract and unsealed (`Center : ActivatableTreeEntity`) and,
where it needs logic, a service generic over the leaf: `CenterService<TCenter> :
EntityService<TCenter> where TCenter : Center`. The pack registers `contribution.Entity<Center,
CenterService<Center>>()`; a distribution either keeps that, extends the entity by plain inheritance
(`MyCenter : Center`) and calls `tellma.UseEntity<Center, MyCenter>()` — the platform closes
`CenterService<MyCenter>` and `EntityPipeline<MyCenter, int>` — or derives `AcmeCenterService :
CenterService<MyCenter>` and registers it, calling `base.` in each override so the pack rule
composes with its own. Columns the distribution adds are ordinary properties; capability interfaces
and annotations added on the leaf project exactly as on a pack entity. Pack
`IEntityValidator<Center>` components keep running for `MyCenter` by contravariance. Pack code is
written against the pack's entity class, never a paired interface per entity.

### 3.4 The worked recipe

A GL center — activatable, tree, multilingual, searchable, naturally keyed — is one class (spec
0018 ships it) and one line of composition. What the class declares:

| Declaration | On | Projects |
|---|---|---|
| `ActivatableTreeEntity` (base) | class | audit columns, `ModifiedAt` concurrency, `IsActive` server-owned, `ParentId`, `SubtreeCount`, `ActiveSubtreeCount`, the shadow `Node`; `Read/Save/Delete/Activate`; `query`, `get`, `get-by-ids`, `get-by-parent-ids`, `save`, `delete`, `delete-by-query`, `delete-with-descendants`, `activate`, `deactivate`; the activatable conjunct; cycle validation and the recount |
| `[Table("Centers", Schema = "gl")]`, `[TableType]` | class | the table `gl.Centers`, resource `gl.Center`, segment `centers`, the UDTT the emitter binds |
| `[Multilingual]` on `Name` | property | `Name2`/`Name3` gated by the tenant's languages; `(E)`/`(ع)` labels; search over the group |
| `[NaturalKey]` on `Code` | property | `UX_Centers_Code`, the uniqueness validator and the 2627 map, the Excel row key |
| `[Searchable]` on `Name`, `[Searchable(SearchKind.Prefix)]` on `Code` | property | the `Search` disjunction: the `Name` group by contains, `Code` by prefix |
| `CenterType` enum | property | stored as `varchar(12)`; queryable as text |

The resulting table, column by column, is spec 0018 §5.3.

What the author gets without writing it: query (search, paging, capped count, ancestors), details
(related projection, extras, row echo), save (array, child synchronisation, stamping, concurrency,
ids, tree recompute, cycle validation), the three deletes, activate and deactivate, get by parent
ids, the four securables, the projected endpoints and MCP descriptors, Excel export and import, and
a conformance test base. The one GL rule — only grouping centers may have children — is a validator
of a few lines in `CenterService<TCenter>` (spec 0018).

## 4. Requests, options, and results

### 4.1 Service-level shapes

```csharp
// Tellma.Core.Abstractions.Crud
public sealed class DetailsRequest
{
    public string? Select { get; set; }                     // row-echo select; null = no echo
    public IReadOnlyList<string> Include { get; set; } = [];   // extras by name; ≤ MaxExtras
    public static DetailsRequest None { get; }              // no echo, no extras (import hydration)
}

public sealed class SaveOptions
{
    public bool ReturnEntities { get; set; } = true;        // false by default when Source = Import
    public DetailsRequest Details { get; set; } = DetailsRequest.None;
    public ConcurrencyMode Concurrency { get; set; } = ConcurrencyMode.Check;
    public SaveSource Source { get; set; } = SaveSource.Web;
    public Action<IDataBatch>? OnPersist { get; set; }      // the caller's own statements on the persist batch
}

public enum SaveSource { Web, PublicApi, Agent, Import, System }

public sealed record ActionOptions(bool ReturnEntities = false, DetailsRequest? Details = null);
```

| Member | Meaning |
|---|---|
| `SaveOptions.ReturnEntities` | The read-back rides the persist transaction (§6.9); `false` skips it and the result carries ids only. The default flips to `false` when `Source = Import` unless set explicitly. |
| `SaveOptions.Concurrency` | Spec 0012's `ConcurrencyMode`. `Check`: a default `ModifiedAt` on an update is `Concurrency.StampRequired`; `Override`: the comparison is skipped, never the existence check (§8). |
| `SaveOptions.Source` | Recorded on `tellma.crud.*` as `crud.source` and visible to hooks through `SaveContext.Options`; it never weakens a rule — a rule that differs by source is a rule that will be bypassed — and it admits nothing: an operation a stack omits is refused whatever the source (§2.2), and a platform-written stack is written by enlistment (§13.3). The web projection sets `Web`, MCP `Agent`, spec 0019 `Import`, provisioning steps and handlers `System`; an enlisted write records the host's source unless the `Source` of its `EnlistSaveOptions` or `EnlistDeleteOptions` names another (§13.3). |
| `SaveOptions.OnPersist` | Invoked once with the persist batch after the effects and before the access guards' invariants (§6.7), under the caller's authority (§13.3): it may write unowned tables and append through the platform composers — `IJobProgress.Append`, `IJobQueue.Enqueue`, `INotifier.Notify` — and a statement writing a stack-owned table is refused at append. Everything it appends commits or rolls back with the save; a validation failure never reaches it. Never set from the wire; its consumer is an open-frame host's checkpoint (spec 0019's chunked import). |
| `ActionOptions` | Whether an action without its own result returns the affected rows in details shape and with which echo and extras; an action with its own result (§3.1) runs no read-back and ignores them. |

### 4.2 Wire records this spec consumes

The service takes and returns spec 0016's wire records directly (`Tellma.Core.Abstractions.Api`),
so the web projection adds no mapping layer. The members the pipeline reads and writes, verbatim
from spec 0016's contracts:

```csharp
public sealed class QueryRequest
{
    public string? Select { get; set; }
    public string? Filter { get; set; }
    public string? Having { get; set; }
    public string? OrderBy { get; set; }
    public int Skip { get; set; } = 0;
    public int? Take { get; set; }
    public string? Search { get; set; }
    public IReadOnlyDictionary<string, JsonElement>? Arguments { get; set; }
    public bool IncludeCount { get; set; } = false;
    public bool IncludeAncestors { get; set; } = false;
    public bool IncludeInactive { get; set; } = false;
    public bool Aggregate { get; set; } = false;
}

public sealed record ParentIdsRequest(
    IReadOnlyList<long>? ParentIds, string? Select, string? Filter,
    IReadOnlyDictionary<string, JsonElement>? Arguments, bool IncludeInactive = false);

public sealed record DeleteByQueryRequest(
    string Filter, IReadOnlyDictionary<string, JsonElement>? Arguments, int ExpectedCount);

public sealed class EntitiesResult<TEntity>
{
    public IReadOnlyList<long> Ids { get; set; }
    public IReadOnlyList<TEntity> Entities { get; set; }
    public RelatedEntities? Related { get; set; }
    public IReadOnlyDictionary<string, JsonElement>? Extras { get; set; }
    public QueryRowSet? Rows { get; set; }
}

public sealed record AffectedResult(int Count);

public abstract record ExportSource                     // Tellma.Core.Abstractions.Excel (spec 0019)
{
    public sealed record ByIds(IReadOnlyList<long> Ids, string? OrderBy) : ExportSource;   // rows in OrderBy order, else by Id
    public sealed record ByQuery(
        string? Filter, string? Search, string? OrderBy, IReadOnlyDictionary<string, JsonElement>? Arguments,
        bool IncludeInactive)
        : ExportSource;
    public sealed record All : ExportSource;                                                // no clauses: the access filter alone (the importer's lookups, spec 0019 §10.1)
}
```

Wire ids arrive as `long` in request records; the pipeline narrows them to `TKey` and reports a
value outside the key's range as `BadRequestException`. `EntitiesResult.Ids` is in input order for a
save and in request order for a get-by-ids; `Entities` is parallel to it. `Related` is spec 0012's
`RelatedEntities` restricted to each entity's `RelatedSelect` projection; `Rows` is the row echo —
one row per entity, in the same order, compiled from `DetailsRequest.Select`. `QueryAsync` returns
spec 0012's `RowPage` (the page's `Rows`, the capped `Count` when requested and the `Ancestors`
set), which spec 0016 §3.5 serialises; `GetByParentIdsAsync` returns a bare `QueryRowSet`.

### 4.3 Arguments

`Arguments` on the wire are JSON values keyed by parameter name. The pipeline declares them to the
engine by running spec 0008 §2.4's `DiscoverQuery` over the request's clauses once per distinct
`(schema fingerprint, clause texts)` — cached beside the compiled query, never per request on a
warm path — takes the inferred type of every parameter, converts each JSON value to that type
(`BadRequestException` on a value that does not convert, naming the parameter), and hands spec
0012's `QueryArguments` map to `IDataBatch.Rows`/`Query`. An argument the clauses never mention is
ignored; a parameter the clauses mention without an argument gets an explicit `null` entry, so the
map covers every declared parameter and spec 0012's missing-argument check never fires on this
path. The engine's own slots (`me()`, `today()`, `now()`, the time zone) are bound by spec 0012's
`ITenantDatabase.Context` from the tenant zone; the pipeline never binds them.

## 5. The guarded runner and the read operations

### 5.1 Every batch on a caller's behalf

The pipeline executes every batch through spec 0014's `IGuardedBatchRunner`:

```csharp
public interface IGuardedBatchRunner
{
    Task<TResult> RunAsync<TResult>(
        BatchPurpose purpose, Action<ConnectedUser, IDataBatch> compose,
        Func<ConnectedUser, BatchOutcome, TResult> read);
}
```

The runner connects (from the `(TenantId, Subject)` cache or a cold prologue-only round trip),
creates the batch, calls `compose`, executes, and calls `read`; the prologue rides the batch as a
fixed prefix and wraps the body in `IF @tm_Guard = 1`. The pipeline composes every statement with
the `ConnectedUser` it is handed — `UserId`, `Access` (the permission set for the tags the prologue
verifies), `TenantTags` — so the filters a batch runs under are exactly those the prologue confirmed
in the same round trip. Outcomes the pipeline relies on and never re-implements: an unknown or
deactivated caller is `TenantNotFoundException` before any business statement; a stale permissions
tag makes the runner recompose once with fresh grants (the pipeline's `compose` is re-entered with a
new `ConnectedUser`) and a second failure is `StaleContextException`; a stale `settings` tag on a
`Read` batch is served while the fresh settings ride the same round trip, and on a `Validate` or
`Persist` batch fails the guard the same way as stale permissions, the runner re-running spec 0013's
initializer before `compose` is re-entered. The persist batch re-declares every `entity:*`
dependency the validation round trips declared, with the tags they were served under (§6.7), so a
list that changed between RT1 and RT2 fails RT2's guard (`50412`; spec 0014's runner re-connects
cold and re-enters `compose` once). Whenever the persist `compose` is re-entered — after a prologue
refusal or a `50412` — it first re-runs the in-memory validators over the hydrated context, a cached
list the new snapshot invalidates reloading on its miss, so no row is written under a rule that
moved. On a `ReadOnly` tenant the prologue writes nothing; the pipeline refuses `Persist` batches
there before composing them (`TenantUnavailableException`, spec 0011's verdict).

Every operation opens spec 0012's `DataAccessScope` with `Operation = "<Resource>:<operation>"`
(`gl.Center:query`, `core.User:save`) so the round-trip instrument and the conformance tests
attribute round trips per operation. Reads run on `BatchPurpose.Read`; validation rounds on
`Validate`; the persist on `Persist`.

### 5.2 Query

`QueryAsync(request)` is one round trip on `BatchPurpose.Read`. The pipeline:

1. **Checks the type-level grant.** `RequireAsync(Resource, "Read")` per §9.1. The resource name in
   a refusal is public metadata, so it leaks nothing.
2. **Enforces ceilings.** `Take` absent = 50; `Take > MaxTake`, `Skip + Take > MaxSkipWindow` or
   `Search` longer than `MaxSearchLength` → `LimitExceededException`.
3. **Builds the `QuerySpec`.** `Root = Resource`; `Select = request.Select ?? DefaultSelect`;
   `Filter = And(user filter, activatable conjunct, search filter, access filter)` where the user
   filter is `Leaf(request.Filter)`, the activatable conjunct is `Leaf("IsActive = true")` on
   activatable stacks unless `IncludeInactive`, the search filter is §5.2.1, and the access filter
   is `decision.Filter` (`null` when `Unrestricted`; an empty conjunct is dropped); `Having`,
   `OrderBy`, `Aggregate` as sent; `Skip`/`Take` as spec 0012's parameter slots. Every clause
   compiles through spec 0008's engine against `ITenantDatabase.Schema` under `PipelineLimits`,
   the `QueryexLimits` profile of every pipeline compile — `MaxParameters = 1024`, `MaxJoins = 64`
   (the floor the composed permission disjunction of spec 0014 §12.2 requires), every other member
   as `QueryexLimits.Default`; any diagnostic is `InvalidQueryException(Diagnostics)` before the
   round trip — the engine never throws for user input, and neither does the pipeline.
4. **Applies the navigation-traversal and select-type rules.** After discovery, every navigation
   path the request's clauses traverse is checked. A path into an entity `E` other than the root may
   touch every column of `E` only when the caller's `Read` decision on `E` is unrestricted (no
   row-level filter); a filtered grant, like no grant, limits the path to `E`'s `RelatedSelect`
   projection in select, filter, order and having alike (`CreatedBy.Name` passes, `CreatedBy.Email`
   is `ForbiddenException("forbidden", E, "Read")`). A traversal therefore never reveals more than a
   direct query of `E` would, apart from the declared display projection. The joined rows are not
   filtered by `E`'s row-level security (a query is filtered at its root only); only projection
   columns are reachable through a filtered or absent grant, so nothing hidden leaks. A select item
   whose Queryex type is `Geography` is refused until a spec defines a JSON encoding for geography
   values: `InvalidQueryException` carrying one `QueryexDiagnostic` with
   `Code = ValidationCodes.QueryGeographyNotSelectable`, `Location = "Select[i]"` (the item's
   zero-based ordinal), a `Span` covering the whole item and
   `Arguments = [("item", <the item text>)]`; filters and orderings over geography functions are
   unaffected.
5. **Appends the statements.** `IDataBatch.Rows(spec, arguments, RowQueryOptions { CountCap =
   IncludeCount ? CountCap : null, IncludeAncestors = request.IncludeAncestors, AncestorsFilter =
   the access filter (null when Unrestricted), CaptureKeys = request.IncludeAncestors })`. With
   `IncludeAncestors` on a tree stack the executor captures the page's ids into `@tb{b}_keys` and
   appends the ancestor query — `Select = the same select`, `Restrictions = [KeySetRestriction("Id",
   ancestor closure of @tb{b}_keys)]` through spec 0012's `IncludeAncestors` option, filtered by
   `AncestorsFilter` alone (never by the user's filter, so the tree renders complete branches),
   excluding the page's own rows — in the same round trip.
6. **Returns the `RowPage`**: the page's `Rows`, `Count` when requested, and `Ancestors` as a
   separate `QueryRowSet` in the same column shape, so the client distinguishes matches from
   context.

#### 5.2.1 Search

`Search` is lowered server-side into `FilterTree.Or` over the stack's `SearchableProperties` that
exist in the tenant's schema (a `[Multilingual]` twin absent from the tenant's languages is
omitted): per property `contains(P, @search)` (`Contains`) or `startsWith(P, @search)` (`Prefix`),
with `search` a declared parameter bound through `QueryArguments`; plus `Id = @searchId` when the
text parses as an integer and the key is integral. When the entity declares nothing searchable the
convention is the properties named `Name`, `Name2`, `Name3`, `Code` that exist, all `Contains`. The
filter text is constant per entity and the value a parameter, so the plan caches once; the engine's
`contains`/`startsWith` carry no pattern metacharacters, so the value can only ever be data. A
service returning a tree from `SearchFilter(search)` replaces the disjunction. There is no
picker-versus-page hint: the select list already says what the client shows.

### 5.3 Details

`GetByIdAsync(id, request)` and `GetByIdsAsync(ids, request)` run one round trip on
`BatchPurpose.Read` after the `Read` check and the `MaxIds`/`MaxExtras` ceilings. The details plan,
in statement order:

1. **The main ids.** `Rows(QuerySpec { Root, Select = "Id", Filter = And(access filter),
   Restrictions = [KeySetRestriction("Id", ids TVP)] }, CaptureKeys = true)` — row-level security
   decides visibility here and nowhere else in the batch; the captured `@tb{b}_keys` is the plan's
   `IdsSource`. The activatable conjunct is **not** applied: a details read by id shows inactive
   rows.
2. **The entity rows, children and related.** `Query<TEntity>(EntityQuery { Select = every mapped
   property, Restrictions = [KeySetRestriction("Id", IdsSource)], Children = every child
   collection (dotted for grandchildren), … })`; spec 0012's materializer fills the entities,
   their collections, and `RelatedEntities` for every navigation in `DetailsExpand` (depth ≤
   `MaxExpandDepth`, each related entity restricted to its own `RelatedSelect` projection, no
   row-level filter — a caller who may read a row may read what it points at; a partially visible
   document does not exist). Ten thousand lines pointing at one center carry one center.
3. **Extras.** For every name in `request.Include` the service's `ContributeDetails(plan)` and each
   `IDetailsContributor<T>` whose `Extras` contains it append statements through
   `DetailsPlan.AddExtra` (raw SQL with `SqlOptions.Writes = []`, or a `QuerySpec`; either may join
   `IdsSource`); an unknown name is `BadRequestException`; the result set becomes
   `EntitiesResult.Extras[name]` (a `QueryRowSet` or the raw result's first set, serialised by spec
   0016).
4. **The row echo.** When `request.Select` is given, `Rows(QuerySpec { Root, Select =
   request.Select, Restrictions = [KeySetRestriction("Id", IdsSource)] })` — the search page's
   cached row refreshed from the details read so the two never desync.

`GetByIdAsync` throws `NotFoundException(Resource, [id])` when `IdsSource` is empty; every other
result set is drained and discarded. `GetByIdsAsync` returns what is visible in request order. The
same plan serves the save read-back (§6.9) and spec 0019's hydration (`DetailsRequest.None`).

The plan as `ContributeDetails` and `IDetailsContributor<T>.Contribute` see it:

```csharp
// Tellma.Core.Abstractions.Crud
public abstract class DetailsPlan<TEntity>              // the TKey-erased view a component sees
{
    public DetailsRequest Request { get; set; }
    public RequestContext Context { get; set; }
    // the visible ids: @tb{b}_keys of the main-ids statement (@tb{b}_saved on a read-back)
    public SqlIdentifier IdsSource { get; set; }
    public void AddExtra(string name, FormattableString sql, SqlOptions? options);
    public void AddExtra(string name, QuerySpec spec, QueryArguments? arguments);
}

public sealed class DetailsPlan<TEntity, TKey> : DetailsPlan<TEntity>
{
    public IReadOnlyList<TKey> Ids { get; set; }            // the requested ids, request order
}
```

| Member | Meaning |
|---|---|
| `IdsSource` | The only identifier an extra may join; opaque — a contributor interpolates it and never spells the name. Empty on a `get` whose id is invisible, so an extra over it yields no rows. |
| `AddExtra` | Appends one result set under `name`; `name` must be in `Request.Include` and declared by the caller's `Extras` (the service declares none and may add any requested name); a second `AddExtra` for one name is `InvalidOperationException`. Raw SQL carries `SqlOptions.Writes = []`. |

### 5.4 Get by parent ids

`GetByParentIdsAsync(request)` on tree stacks is one `Read` round trip returning a `QueryRowSet`:
the `Rows` of the `RowPage` that `IDataBatch.Rows` yields for a `QuerySpec` with `Root`,
`Select = request.Select ?? DefaultSelect`, `Filter` = the `And` of `Leaf(request.Filter)`, the
activatable conjunct unless `IncludeInactive`, the access filter and, for the roots,
`Leaf("ParentId = null")`; `Restrictions` = `[KeySetRestriction("ParentId", parent ids TVP)]` except
for the roots; and `OrderBy = "Id"` unless the select carries an `OrderBy`. A `null` or empty
`ParentIds` means the roots; ids beyond `MaxIds` are `LimitExceededException`. The parent-id index
serves the seek; the result renders the initial tree plus every expanded node in one call.

### 5.5 Get all cached

`GetAllCachedAsync(request)` on `[Cacheable]` stacks answers from spec 0013's
`ICacheableEntities.GetAsync<TEntity>()` after the `Read` check (`FilterRoot = null`, so no row
filter and no criteria provider); a miss loads the whole table in one round trip through the capped
probe of spec 0013 §4.4, and a table beyond `MaxRows` (`CacheOutcome.Oversized`, no list) is read by
one ordinary `Query<TEntity>` over the whole table — one extra round trip, on every call, until an
operator raises `MaxRows`. `Related` and `Extras` follow §5.3 on the returned ids when requested;
the common call passes `DetailsRequest.None` and costs no round trip on a warm cache.

## 6. The save pipeline

### 6.1 Steps

`SaveAsync(entities, options)` runs the steps below; **RT** marks a round trip.

| # | Step | Where | What |
|---|---|---|---|
| 1 | Shape and ceilings | pipeline | `entities` non-empty and ≤ `MaxSaveCount`; every child collection at most its `MaxCount` per parent at every depth and the payload at most `MaxRowsPerSave` rows at every depth (beyond either, as beyond `MaxSaveCount`, `LimitExceededException` before any round trip); ids `0` new, `< 0` temporary (unique within the payload), `> 0` update — never an insert; a duplicate `Id > 0` or a duplicate temporary id per entity type is `Entity.DuplicateId`; every BCL `ValidationAttribute` on every entity and child evaluated from cached metadata with **all** failures reported (`IValidatableObject` is not consulted; the walk recurses into child collections, which the BCL validator does not). Any error → `ValidationException` (422) with zero round trips. |
| 2 | Type-level permission | pipeline | `RequireAsync(Resource, "Save")` per §9.1; a refusal (`ForbiddenException`, `StepUpRequiredException`) costs no round trip beyond §9.1's deny re-check. |
| 3 | Preprocess | pipeline, then hook | §6.3; then `PreprocessAsync`. |
| 4 | Ids | pipeline + allocator | §6.4: `IIdAllocator.Reserve(batch, EntityType, new rows)`; assignment before validators run. |
| 5 | Context round — **RT1** | pipeline + validators | One `Validate` batch: the prologue; the reservation statement when the buffer was short; per root table with existing rows in the payload, the **before images** under the caller's `Read` filter and the **`Save`-grant count** over the same ids (§9.2); child before images by parent id for every collection present in the payload; the tree ancestor chains and the `(Id, ParentId)` set for `TreeCycleValidator<T>`; every load a validator declared before its first await (§7.2), the loads of every enlisted group registered by then among them (§13.3). Skipped entirely when nothing needs it — creates only, a warm buffer, no unique property, no blob references, no validator loads — which is the one-round-trip create. |
| 6 | Validate | pipeline + validators | §6.6: existence and the two-stage pre-check, early concurrency detection, ownership rules, child identity, batch-internal uniqueness, cycles and depth; then the validators' continuations and further rounds **RT1a…** as declared, bounded by `MaxValidationRounds`; a validator may enlist writes into other stacks, whose checks and validators join the rounds as tasks (§13.3). Any error → `ValidationException`, nothing written. |
| 7 | Assemble the persist batch | pipeline | §6.7, every enlisted group at its declared position. |
| 8 | Persist — **RT2** | executor | One `Persist` batch, one T-SQL transaction holding the host's statements and every group; SQL errors translated per §14.2; transient failures re-run by the executor; the commit probe on an ambiguous failure. |
| 9 | Post-commit | pipeline + effects | `AfterCommitAsync` — the hook, then every `IPersistEffect<T>`, then the same pair for each group's target — and the batch's `OnCommitted` callbacks (§13.2). Failures are logged and metered, never thrown. |
| 10 | Response | pipeline | `EntitiesResult` with `Ids` in input order and, when `ReturnEntities`, the read-back (§6.9). |

Common case: one round trip for a create with no context loads, no unique property and a warm id
buffer, two for an update; each dependent validation round adds one.

### 6.2 Property ownership

Which members a client may set is derived from spec 0012's `PropertyOwnership`, never from what the
payload contained:

- **`ServerOwned`** (spec 0012 §2.4's derivation list and any `[ServerOwned]` property): on update
  the value is overwritten from the before image; on insert from a fresh instance's default
  (`EntityMetadata.ResetServerOwned`). A client-sent value is ignored, never an error — an old
  client during a swap may send a member a new server owns. One exception: `ModifiedAt` is read
  as the expected stamp before being overwritten (§8). A nested child's parent key is set by the
  nesting (§6.3), whatever the payload carried.
- **`WriteOnce`** (`HandlerKey` on spec 0020's `Schedule`, any `[WriteOnce]` property): settable on
  insert (the emitter's `INSERT` takes it from the payload); on update a value that *differs* from
  the before image is the validation error `WriteOnce` at the path; equal or default (the client did
  not send it) passes. The emitter never lists a write-once column in a `SET` list, so the rule is
  validated and excluded. Never a silent reset: an import that carried a value the server would
  discard fails visibly.
- **`Derived`** (any `[Derived]` property): the client's value is discarded on insert and update —
  the pipeline calls `EntityMetadata.ResetDerived` on every row before preprocessing — and
  `PreprocessAsync` sets it with the children in hand (a document total summed over its lines);
  the emitter writes it on insert and in every `SET` list. A derived value that depends on the
  before image is set by a validator after RT1 instead; the emitter reads the in-memory row when it
  assembles the persist batch (§6.7). A hook that sets nothing persists the fresh-instance default.
- **`DatabaseOwned`** (a column with computed SQL in the model, spec 0012 §2.4): never written by
  the emitter; a client-sent value is ignored, never an error; the read-back carries the database's
  value.
- **`Editable`**: everything else.
- **Multilingual gating**: a client value for a twin the tenant has not configured (`Name3` on a
  two-language tenant) is discarded before validation, never an error, except under `Source =
  Import`, where a non-empty value for an unconfigured twin is `Import.LanguageNotConfigured` at
  the path.

The same rules apply per child type. A `[BlobReference]` property is `Editable` with the attach
validator of spec 0017 behind it.

### 6.3 Preprocessing

Before the hook: every string is trimmed and an empty nullable string becomes `null`
(`[PreserveWhitespace]` opts out); nested children receive their parent key from the nesting (a
payload `ParentId` on a nested child is overwritten — the nesting wins); server-owned and derived
members are reset per §6.2; unconfigured multilingual twins are dropped. Then
`PreprocessAsync(context)` runs once with the payload in order and `context.Loader` available (its
loads join round 1).

### 6.4 Ids and temporary ids

After preprocessing the pipeline calls spec 0012's `IIdAllocator.Reserve(batch, EntityType, rows)`
with every new root and every new child row, per table. When `IdReservation.IsImmediate` (the warm
buffer covered the exact deficit) ids are assigned at once and validators start before RT1, their
loads riding it. Otherwise the reservation statement rides RT1 beside the pipeline's own loads, ids
are assigned from the returned range after RT1, and validators start after it — their declared
loads form the next round, so a cold buffer costs exactly one round trip more and validators always
reason about final identities. Assignment rewrites every temporary id (`Id < 0`) in every property
`EntityMetadata.References` reports as a foreign key to the same entity type (`ParentId`, a child's
parent key, any other self-typed FK); a temporary id that nothing in the payload defines is
`Entity.NotFound` at the referencing path. A validation failure disposes the reservation and the
unused ids return to the buffer (nothing outside the process saw them); a persist attempt never
returns ids, whatever its outcome. Every batch the executor runs may carry a refill to the buffer's
low-water mark, so the common create pays no reservation round trip.

### 6.5 Child synchronisation

For each child collection on the entity (any `[NotMapped]` `list<TChild : ChildEntity>`; its name
is the child's table name):

- `null` → untouched: the collection's parents TVP is bound empty, so its statements touch
  nothing; no before images loaded.
- `[]` → every existing child of that parent is deleted.
- a list → children with `Id ≤ 0` are inserted, `Id > 0` updated, and the parent's existing children
  absent from the list deleted — the emitter's delete is bounded to the parents whose collection
  was present.
- A child `Id > 0` that is not among the parent's before-image children — it belongs to another
  parent, to a parent the caller cannot see, or to nothing — is `Entity.NotFound` at
  `[i].<Collection>[j].Id`; never re-parented, never inserted.
- Any child change stamps the parent's `ModifiedAt` even when no parent column changed; children
  carry no audit columns and no stamp.
- Grandchildren follow the same rules under their own parent.

The emitter needs no upsert-versus-synchronise flag: roots are upserted by id, nested collections
synchronised under the parents whose collection was present; the save graph says which is which.

### 6.6 Validation

In order, after RT1 and before any validator continuation resumes:

1. **Existence and the pre-check.** An update id absent from the before images is
   `NotFoundException(Resource, missing ids)` — hidden and missing alike (404); a `Save`-grant
   count below the loaded count is `ForbiddenException("forbidden", Resource, "Save")` with no ids
   named (403) (§9.2).
2. **Early concurrency detection.** Under `Concurrency = Check`, an update whose payload
   `ModifiedAt` is default is `Concurrency.StampRequired` at `[i].ModifiedAt`; one whose stamp
   already differs from the before image's is `ConcurrencyException` now, with the conflict rows
   built from the before images. The authoritative check is inside the transaction (§8); this one
   fails fast before validators spend a round.
3. **Ownership.** `WriteOnce` per §6.2.
4. **Child identity** per §6.5.
5. **Uniqueness.** For every `[Unique]`/`[NaturalKey]` property present in the payload, duplicates
   within the payload are `Unique` at the path, and RT1 carries one `ByKey` load (§7.2) over the
   payload's values as preprocessing left them — on a child, scoped to the parent key in C# — so a
   loaded row whose id is not the payload row's is `Unique` at the path too, on create and update
   alike. A payload carrying such a property therefore always has an RT1. The index is the
   guarantee for the race (§7.4).
6. **Trees.** `TreeCycleValidator<T>` (spec 0012) overlays the payload's parents on the loaded
   `(Id, ParentId)` set and reports `Tree.Cycle` at `[i].ParentId` on a revisit (including
   `ParentId = Id`) and `Tree.TooDeep` beyond `[Tree(MaxDepth)]`; the SQL fence in RT2 is the
   guarantee.
7. **Validators.** The service's `ValidateAsync`, then every `IEntityValidator<T>`, resumed with
   their round-1 refs filled; further rounds per §7.2.

Every error is collected; nothing stops at the first. Any error after any round →
`ValidationException(Errors)`; the reservation is disposed; nothing was written.

### 6.7 The persist batch

The pipeline composes one `Persist` batch through the runner, re-declaring through `DependsOn`
every `entity:*` dependency the validation round trips declared, with the tags they were served
under, so that spec 0013's guard holds them (§5.1). Every enlisted write is a group of §13.3 placed
among the steps below at its declared position: `BeforeHost` groups precede step 1 and `AfterHost`
groups follow step 3, each set in enlistment order, and a nested group sits at its position inside
its host's group. A save's `SaveOptions.OnPersist` (§4.1) is invoked once, after step 3 and the
`AfterHost` groups and before step 4's `ContributeInvariants`: it is the host's statement, never
repeated per group. What the pipeline appends, in order, between the fixed prefix (the schema guard
of spec 0012 §4.3, the prologue, the guard, `BEGIN TRAN`, the version-tag guard, the caller
re-check — spec 0012, 0014 and 0013 text the executor places) and the fixed suffix (tag bumps,
`COMMIT`):

1. **The emitter.** `IDataBatch.Save<TEntity>(rows, options.Concurrency)` with the payload's roots
   and their supplied collections, returning the `SaveHandle` whose `SavedIds` (as `AffectedIds`),
   `NewIds`, `TouchedIds` and `Captures` the `SavePersistContext` of §13.1 carries. Spec 0012's
   emitter binds the new/existing TVPs per table, keeps `@tb{b}_saved`, `@tb{b}_new`,
   `@tb{b}_touched`, guards concurrency under `UPDLOCK`, inserts, synchronises, updates changed rows
   only, stamps, captures blob references (spec 0012 §8.5), and appends the tree statements for tree
   tables.
2. **The service hook.** `ContributeAsync` on the service, receiving the
   `SavePersistContext<TEntity>` (§13.1): raw `Sql` with declared writes (a distribution guard is
   `THROW 50600–50699`, mapped to `ValidationException` with the message as the code), notifications
   through `Notify`, job rows through spec 0020's `IJobQueue.Enqueue(batch, …)`.
3. **The effects.** Every `IPersistEffect<T>` in registration order with the same context; spec
   0017's `BlobReferenceEffect<T>` (the blob release/confirm statements), registered by
   `CoreFeature`, precedes every pack's effect.
4. **The access guards.** Spec 0014's `IAccessGuards`, called by the pipeline whenever the
   batch's declared writes (the stack's tables and every group's, known from metadata before the
   first statement) meet `core.Users`, `core.Roles`, `core.RoleMemberships` or `core.Permissions`
   and `RequestContext.Kind ≠ System` — save, action and delete persists alike — at the two
   positions spec 0014 states: `ContributeLock(batch)` after the executor's re-checks and before
   the first statement of any group or of the emitter (ahead of step 1);
   `ContributeInvariants(batch)` after every `ContributeAsync` contribution of steps 2 and 3 and
   of every group, before the post-check of step 5.
5. **The row-level post-check** (§9.3) over `@tb{b}_saved`.
6. **The read-back** (§6.9) when `ReturnEntities`.

`XACT_ABORT ON` makes every runtime error — a `THROW`, 2601/2627, 547, a deadlock — roll the whole
batch back on the server; result sets preceding a `THROW` are read by the executor before the
exception surfaces, which is how the conflict list reaches the caller. Nothing in the pipeline
holds a connection; `PersistContext` exposes the batch, never a connection.

### 6.8 Import

Spec 0019's importer calls `SaveAsync` in chunks of at most the smaller of `MaxSaveCount` and
`MaxIds` parents (hydration reads them in one `GetByIdsAsync`) and `MaxRowsPerSave` rows, each
chunk's `SaveOptions` carrying `ReturnEntities = false`, `Concurrency = Check`, `Source = Import`
and, on the background path, an `OnPersist` that appends the job's checkpoint (spec 0019 §12.5);
`Update`/`Upsert` hydrate through `GetByIdsAsync(ids, DetailsRequest.None)` (spec 0019 §9.3).
`Insert` sends ids of `0`, or the codec's temporary ids for new rows a self-reference names
(spec 0019 §9.6); `Update` and `Upsert` send the ids the row key resolved (the sheet's `Id` on a
same-source file, else a natural-key lookup) with the hydrated `ModifiedAt` (or the sheet's
`Stamp`) as the expected stamp, so a concurrent edit between hydration and persist is a conflict
per row, never a silent overwrite. `IsActive` is server-owned (§12.2): `Insert` rows and the insert
half of `Upsert` are created active, an update leaves it untouched, and a sheet column mapped to it
is `Excel.Import.ServerOwnedColumn` (spec 0019) — deactivation is the action, never an import. Each
chunk is its own transaction, its checkpoint inside it; atomicity across chunks is a background-job
concern. The persist statements' text is identical for one row and ten thousand; above
`DataOptions.LargeBatchThreshold` (spec 0012 §8.2) the emitter switches plan lanes so a one-row
save's plan never serves a large import.

### 6.9 The read-back and the response

With `ReturnEntities`, the details plan of §5.3 runs inside the transaction over `@tb{b}_saved`
(entities, children, related, extras, row echo), bounded by `MaxSaveCount`, so the response is
exactly what was committed and a concurrent change committing a millisecond later cannot show. The
plan's extras from `ContributeDetails` run inside the transaction too; they are reads. With
`ReturnEntities = false` — the import default — no read-back rides the transaction and no lock
outlives the writes. `EntitiesResult.Ids` is in input order with final ids (temporary ids resolved);
`Entities` parallel; `Related`, `Extras`, `Rows` per the request.

## 7. Validation: contexts, the loader, errors

### 7.1 Contexts

```csharp
// Tellma.Core.Abstractions.Validation
public sealed class SaveContext<TEntity>
{
    public IReadOnlyList<TEntity> Entities { get; set; }    // after preprocessing, ids assigned, payload order
    // before image (null for inserts), loaded under the caller's Read filter, with every child
    // collection present in the payload populated (grandchildren nested); a collection absent from
    // the payload is null on the before image
    public TEntity? Before(int index);
    public SaveOptions Options { get; set; }
    public RequestContext Context { get; set; }
    public IContextLoader Loader { get; set; }
    public ValidationErrors Errors { get; set; }
    public int Round { get; set; }                          // 1-based; bounded by MaxValidationRounds
    public IWriteHost Host { get; set; }                    // the frame this context runs in (§13.3)
    public EnlistmentDescriptor? Enlistment { get; set; }   // non-null when this payload is an enlisted write
    public bool IsNew(int index);
    public bool Changed<TValue>(int index, Func<TEntity, TValue> property);   // false for inserts
}

public abstract class DeleteContext<TEntity>
{
    public IReadOnlyList<TEntity> Entities { get; set; }
    public RequestContext Context { get; set; }
    public IContextLoader Loader { get; set; }
    public ValidationErrors Errors { get; set; }
    public IWriteHost Host { get; set; }                    // the frame (§13.3)
    public EnlistmentDescriptor? Enlistment { get; set; }   // non-null for an enlisted delete
}

public sealed class DeleteContext<TEntity, TKey> : DeleteContext<TEntity>
{
    public IReadOnlyList<TKey> Ids { get; set; }
}

public abstract class ActionContext<TEntity>
{
    // the visible target rows; children when LoadChildren
    public IReadOnlyList<TEntity> Entities { get; set; }
    public object? Arguments { get; set; }
    public RequestContext Context { get; set; }
    public IContextLoader Loader { get; set; }
    public ValidationErrors Errors { get; set; }
    public IDataBatch Batch { get; set; }                   // the persist batch
    public Task Persisted { get; }                          // completes after RT2 commits (§11.2)
    public void Save(IReadOnlyList<TEntity> entities);      // marks rows for the emitter with stamping
    // one UpdateSpec<TEntity>.ByIds over the target ids under the grant's filter
    public void Update(IReadOnlyDictionary<string, object?> assignments);
    public void Notify(NotificationRequest request);
    public IWriteHost Host { get; set; }                    // the pipeline frame; the method enlists through it (§13.3)
}

public sealed class ActionContext<TEntity, TKey> : ActionContext<TEntity>
{
    public IReadOnlyList<TKey> Ids { get; set; }            // the target ids, request order
}
```

`Before` is primed from RT1: a validator asking for a before image or its children through the
loader costs no statement. `DeleteContext.Entities` are the rows the delete targets, loaded under
the `Delete` grant when `ValidateDeleteAsync` is overridden. `ActionContext` is §11. `Host` is the
frame the context runs in; `Enlistment` on a save or delete context is the declared pair the payload
was enlisted under, null on a front-door payload, and an action context has none because an action
is never enlisted (§13.3).

### 7.2 The loader and the rounds

```csharp
// Tellma.Core.Abstractions.Validation
// DataLoader over the round's batch; dedup by structural key
public interface IContextLoader
{
    ContextRef<IReadOnlyDictionary<TK, T>> ByIds<T, TK>(IReadOnlyList<TK> ids, string? select);
    ContextRef<IReadOnlyDictionary<TK, T>> ByKey<T, TK>(
        string key, IReadOnlyList<TK> values, string? select);              // unique property, TVP-restricted
    ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TChild>>> ByParentIds<TChild, TK>(
        IReadOnlyList<TK> parentIds, string? select);
    ContextRef<IReadOnlyList<T>> Query<T>(FilterTree filter, QueryArguments? arguments, string? select);
    ContextRef<IReadOnlyList<T>> Visible<T>(
        FilterTree filter, QueryArguments? arguments, string? select);      // read grant conjoined
    ContextRef<QueryRowSet> Rows(QuerySpec spec, QueryArguments? arguments);
    ContextRef<bool> Exists(string root, FilterTree filter, QueryArguments? arguments);
    ContextRef<int> Count(string root, FilterTree filter, int cap, QueryArguments? arguments);
    ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TK>>> Ancestors<T, TK>(IReadOnlyList<TK> ids);
    ContextRef<RawResult> Sql(FormattableString sql, SqlOptions? options);
    Task LoadAsync();                                   // one round: every pending request in one batch
    void Prime<T, TK>(T entity);
}

public sealed class ContextRef<T>
{
    public bool IsLoaded { get; }
    public T Value { get; }                             // InvalidOperationException before its round ran
}
```

| Member | Meaning |
|---|---|
| `ByIds`, `ByKey`, `ByParentIds` | Keyed loads: model-emitted, TVP-restricted `Query<T>` statements on the round's batch (`KeySetRestriction` over `Id`, the unique property, or the parent key); missing keys are explicit absences. Not row-level filtered — a uniqueness rule is global by definition. |
| `Query`, `Rows`, `Exists`, `Count` | User- or rule-authored `FilterTree`s compiled through the engine; `Visible` conjoins the caller's `Read` grant for rules that must respect visibility. |
| `Ancestors` | Tree stacks: the ancestor chain per id, from the loaded `(Id, ParentId)` set. |
| `Sql` | The escape hatch: raw text with `SqlOptions.Writes = []` (a `Validate` batch never writes; the analyzer refuses DML here); no `@qx`, `@tb`, `@tm` names. |
| `Prime` | Registers an in-memory row (a payload entity) so a later `ByIds` for it costs nothing. |

**Validators are plain async methods** run concurrently as tasks per round. Every load method
returns a `ContextRef<T>` synchronously and registers the request; a validator asks for context only
through the loader and *parks* at `LoadAsync`. When every running validator has parked at
`LoadAsync` or completed, the scheduler dispatches **one** batch holding every pending request
(round 1 rides RT1 together with the pipeline's own loads), fills every ref, and resumes all parked
validators. A task may register in a running round — an enlisted write's checks and validators
join the round in progress (§13.3) — and the round closes when every registered task has parked or
completed. Rounds are shared, so an operation costs as many rounds as the largest number of
`LoadAsync` awaits any one validator makes. Each dispatch is one round trip and one round; exceeding
`MaxValidationRounds` throws `InvalidOperationException` naming the validator — an authoring bug,
never user input — and increments `tellma.crud.validation.rounds` with `crud.outcome = exceeded`.
Reading `Value` before its round ran throws `InvalidOperationException`, an authoring bug too. A
validator awaiting something other than `LoadAsync` is "active": dispatch waits, and the
operation's duration histogram shows it. The scheduler's termination property — validators
awaiting foreign tasks still terminate and never dispatch early — is pinned by a property test
(§18.1).

**Requests are deduplicated by structural key**: the root entity, the key property, the filter tree
rendered ordinally, the parameter values, the statement text for raw SQL. Key values are unioned
into one TVP; requests differing only in `Select` are merged by unioning the selects. A key that
answers "not found" is cached for the request like any value.

*Illustration* — a grouping-parent rule inside a service's `ValidateAsync`; the loop over
`c.Entities` and the lookup `parents.Value[...]` of parents that are themselves in the payload are
prose:

```csharp
var parents = ctx.Loader.ByIds<Center, int>(parentIds, select: "Id,CenterType");
await ctx.Loader.LoadAsync();
if (parent.CenterType is not (CenterType.Abstract or CenterType.BusinessUnit))
    ctx.Errors.Add(i, x => x.ParentId, GlValidationCodes.ParentMustBeGrouping);
```

### 7.3 Errors and codes

```csharp
// Tellma.Core.Abstractions.Validation
public sealed record ValidationPath(IReadOnlyList<PathSegment> Segments)
{
    public static ValidationPath Root { get; }          // the whole payload
    public ValidationPath Index(int index);
    public ValidationPath Property(string name);        // CLR property name
    public override string ToString();                  // "[3].Lines[1].Quantity"; logs and tests only
}

public abstract record PathSegment;
public sealed record IndexSegment(int Index) : PathSegment;
public sealed record PropertySegment(string Name) : PathSegment;

public sealed class ValidationErrors
{
    public bool HasErrors { get; set; }
    public IReadOnlyList<ValidationError> Items { get; set; }
    public void Add(ValidationPath path, string code, IReadOnlyDictionary<string, object?>? arguments = null);
    public void Add<TEntity, TValue>(
        int index, Func<TEntity, TValue> property, string code,
        IReadOnlyDictionary<string, object?>? arguments = null);            // Root.Index(index).Property(name)
}

public static class ValidationCodes
{
    public const string Required = "Required";
    public const string MaxLength = "MaxLength";
    public const string Range = "Range";
    public const string Unique = "Unique";
    public const string WriteOnce = "WriteOnce";
    public const string Precision = "Precision";
    public const string EntityNotFound = "Entity.NotFound";
    public const string EntityDuplicateId = "Entity.DuplicateId";
    public const string FkNotFound = "Fk.NotFound";
    public const string FkInUse = "Fk.InUse";
    public const string TreeCycle = "Tree.Cycle";
    public const string TreeTooDeep = "Tree.TooDeep";
    public const string BlobNotAttachable = "Blob.NotAttachable";
    public const string BlobDuplicateReference = "Blob.DuplicateReference";
    public const string ConcurrencyStampRequired = "Concurrency.StampRequired";
    public const string ImportLanguageNotConfigured = "Import.LanguageNotConfigured";
    public const string QueryGeographyNotSelectable = "Query.GeographyNotSelectable";   // a query diagnostic (§5.2)
}

// Tellma.Core.Abstractions.Errors
public abstract record CodedError(string Code, IReadOnlyDictionary<string, object?> Arguments);

public sealed record ValidationError(ValidationPath Path, string Code, IReadOnlyDictionary<string, object?> Arguments)
    : CodedError(Code, Arguments);
```

A path is a segment list, never parsed: spec 0016 renders property segments through its JSON naming
policy (`entities[0].roleMemberships[2].roleId`), spec 0019 maps segments through its coordinate
map, and `ToString()` renders the bracket form (`[3].Lines[1].Quantity`) for logs and tests. A
root-level name (`Ids[2]`) is a property segment then an index; a whole-payload error has
`ValidationPath.Root`. An error a participant of an enlisted write adds is re-rooted through the
enlistment's `MapPath` into the host's list and carries the target's entity name under
`enlistedResource` (§13.3). Prose elsewhere writes paths in the rendered form (`[i].ParentId`,
`Ids[i]`). Codes are dotted PascalCase resource keys; the platform's are the constants above, packs
and distributions publish theirs beside them (`Centers.ParentMustBeGrouping`, `Users.NotService`);
arguments are named raw values — strings, numbers, booleans, `DateOnly` and `DateTimeOffset`, never
pre-formatted text — that feed ICU MessageFormat twice: on the server under the request culture for
the rendered message, and in the SPA from spec 0013's string pack in the language the user is
viewing; a property is named by its CLR name under `property`, which each renderer resolves to a
label itself. Spec 0016 returns code, arguments and rendered message. A platform minor may add
codes, never rename one. `CodedError` is what every coded error item shares — the code and its
arguments; `ValidationError` adds the path and spec 0019 §2.1's `ImportError` the coordinates.

### 7.4 Uniqueness and foreign keys

**Uniqueness is validated by the pipeline and guaranteed by the index.** One `[Unique]` (or
`[NaturalKey]`, which implies it) yields, from one declaration: the unique index
`UX_<Table>_<Col>` (spec 0012), the key-load validator of §6.6 step 5, which reports the ordinary
duplicate at its path before anything is written, and the persist-error map that turns 2601/2627
on that index into `Unique` for the race the validator cannot see, located by the index name alone.
A C#-only check without the index is write-skew-prone under RCSI and SNAPSHOT alike and is refused
by the startup check of §2.6. No validation read takes `UPDLOCK` or `HOLDLOCK`.

**Foreign keys are validated by the database and, on the JSON path as on the Excel path, under
the target's `Read` filter.** For every reference whose value changed, RT1 loads the referenced ids
through `Loader.Visible<TTarget>` (the target's `Read` decision conjoined; a target on which the
caller holds no `Read` at all makes every reference to it `Fk.NotFound`); a missing or invisible
target is `Fk.NotFound` at the property path, carrying `entity` (the target's entity name) and `id`
(the value). The database check remains the guarantee: 547 on `FK_<Table>_<Column>` maps to
`Fk.NotFound` at the property on save and to `Fk.InUse` at `Ids[i]` on delete, the latter carrying
`entity` (the referencing entity name) and `property`, both derived from the constraint name; SQL
Server names the first violated constraint only.

## 8. Concurrency

`ModifiedAt datetimeoffset(7)` is the stamp; there is no `rowversion`.

- **Generation.** Every persist batch stamps every written root with one `SYSUTCDATETIME()` taken
  at the top of the emitter's text (spec 0012's `@tb{b}_now`) — one source per tenant database,
  immune to instance clock skew. Every write of a row image through the emitter stamps: save,
  activate/deactivate, every action, background jobs that change business columns. Bookkeeping
  never touches the entity row (tags, `LastActiveAt`, `InboxSeenAt` live on `core.UserStamps`; lease
  columns on `core.Jobs`; the tree recompute writes `Node` and the counts and never `ModifiedAt`).
  The one documented exception is the prologue's `Invited → Joined` flip on `core.Users`.
- **On the wire** the client echoes the `ModifiedAt` it loaded; the pipeline reads it as the
  expected stamp before overwriting it. The string round-trips verbatim with seven fractional
  digits (the SPA keeps the raw string and parses a copy for display).
- **The check** is the emitter's first statement under `UPDLOCK, ROWLOCK` on every existing root:
  two savers of one row serialise on the U lock and the second sees the first's committed stamp,
  which is what makes check-then-write race-free under RCSI and lock-after-qualification. A row
  that vanished is always a conflict (`IsMissing`), never an insert, whatever the mode.
- **Outcome.** The guard selects the conflict rows as a result set — id, reason, the stored
  `ModifiedAt` and `ModifiedById`, the modifier's `Name`, `Name2` and `Name3` — and throws
  `50409`; the executor reads the set before the exception surfaces and raises
  `ConcurrencyConflictException(Conflicts)`, which the pipeline translates: every conflict missing
  → `NotFoundException(Resource, ids)` (a row that never existed or was deleted under the caller is
  not a concurrency conflict); otherwise `ConcurrencyException("concurrency-conflict", conflicts)`
  with `IsMissing` per id and, for existing rows, `ModifiedAt`, `ModifiedById` and
  `ModifiedByName`, resolved under the request culture through spec 0013's language shape, the
  primary name when the culture's twin is empty; the wire carries one string. The UI offers
  "overwrite" and resends with `Concurrency = Override`.
- **`Override`** skips the comparison only: missing rows still conflict, permissions still apply,
  U locks are still taken. Overrides are metered (`tellma.crud.concurrency.overrides`).
- **`Check` with no stamp.** A default `ModifiedAt` on an update is `Concurrency.StampRequired`
  (§6.6). The web details-page save always sends `Check`; import derives the stamp from hydration
  (§6.8); an agent must load before it saves.
- **Actions** do not check stamps — a state toggle is a command over ids, not an edit of a loaded
  copy — but stamp the rows they change, so a concurrent editor's later save conflicts.
- **Delete** accepts optional expected stamps per id; a mismatch is a conflict (§10.1).
- **Children** carry no stamp; any child change stamps the parent.
- **Ambiguous failures.** When the persist's connection drops during or after `COMMIT`, the
  executor's probe checks the inserted ids; any present proves the commit and the pipeline
  re-issues the read-back as a plain `Read` and proceeds to post-commit effects; none present
  re-runs the persist (a replay collides on the primary key if the commit did happen, never
  duplicates). An update-only batch cannot be probed and surfaces `DependencyUnavailableException`
  with a reload instruction — the honest answer on a rare path.

## 9. Row-level security in the pipeline

### 9.1 Type level

Every operation opens with spec 0014's `IAccessEvaluator.RequireAsync(Resource, Action)`; the
evaluator disjoins the stored grants with every criterion the `IAccessCriteriaProvider`s registered
for the resource supply (spec 0014 §5.1), and the service never composes the final filter. No grant
and no criterion on `(Resource, Action)` → `ForbiddenException("forbidden", Resource, Action)`; a
sensitive pair (spec 0014 §4.3) on a session below the configured assurance → spec 0011's
`StepUpRequiredException`, raised before the first batch for every caller — a request the web
guard's `RequireAssuranceMetadata` check (spec 0011 §4.3) admitted, an MCP tool, a job or a test.
The decision is answered from the cached set, a denial older than spec 0014's `FastDenyWindow`
re-verified by one prologue-only round trip (spec 0014 §6.4), so a caller granted a permission a
moment ago is admitted within one window. `decision.Filter` — the `Or` of every matching filter, the
providers' criteria included — is conjoined into every read, update and delete; its leaves carry
their stored language stamps and the user's filter the current version (spec 0012 §11.2);
`Unrestricted` conjoins nothing. The access filter alone is passed as `AncestorsFilter`. A criterion
alone yields `Filtered`.

### 9.2 Before the write: the two-stage pre-check

Every id-addressed write restricts its targets by the grant's filter before it writes:

- **Save.** RT1 loads the before images of every existing id under the caller's **`Read`** filter
  and counts the same ids under the **`Save`** grant's filter, in the same batch, compared in C#: an
  id absent from the before images is `NotFoundException` (404, indistinguishable from
  non-existence); a `Save` count below the loaded count is `ForbiddenException` (403, no ids
  named). Inserts have no pre-check; the post-check covers them.
- **Delete and actions.** The check is two statements inside the batch (§10, §11.1), all-or-nothing:
  the target ids are counted under the caller's `Read` filter — `ReadFilter` on spec 0012's
  `DeleteSpec.ByIds`, `DeleteSpec.WithDescendants` and `UpdateSpec.ByIds` — and the ids absent or
  hidden alike are selected as a result set before `THROW 50404, N'Entity.NotFound'`, so the
  `NotFoundException` names them and hidden equals missing; then the readable ids are counted under
  the action's grant (`Filter`) and a short count is `THROW 50403, N'RowSecurity'` →
  `ForbiddenException` (403, no ids named). The general action path (§11.2) pre-checks through its
  RT1 load under the grant's filter, as save does.
- **Enlisted writes.** Neither stage: the target's before images load unfiltered and no grant is
  counted; the frame's authority covers every group (§13.3).

### 9.3 After the write: the post-check

Save always, and actions with `PostCheck = true`, append after every write and before the tag bumps:

```sql
DECLARE @tm_visible int = (SELECT COUNT(*) FROM (<compiled: Root = gl.Center, Select = "Id",
    Restrictions = [KeySetRestriction("Id", "@tb{b}_saved")], Filter = save grant>) AS q);
IF @tm_visible <> (SELECT COUNT(*) FROM @tb{b}_saved) THROW 50403, N'RowSecurity', 1;
```

`@tb{b}_saved` holds every root id of the payload — inserted, edited, and parents of synchronised
children — so a caller cannot insert or edit a row out of the grant that permitted the save. A
grant with no filter compiles to no post-check. The executor raises `RowSecurityException`; the
pipeline translates it to `ForbiddenException("forbidden", Resource, Action)` — the transaction is
already rolled back. Actions default to no post-check because an action commonly moves a row out of
the filter that permitted it (`post` on `State = 'Draft'`).

### 9.4 Related entities and references

Related entities reached through a details read are not row-level filtered and are restricted to
their `RelatedSelect` projection (§5.3); a user who may read a document may read the display
projection of what it points at. Query clauses traversing navigations follow §5.2 step 4. Foreign
keys a save assigns are validated under the target's `Read` filter (§7.4), so the JSON path and the
Excel path agree: a caller cannot reference a row they could not see.

## 10. Delete operations

Every delete composes spec 0012's `IDataBatch.Delete<TEntity>(DeleteSpec)` — the statements are
that spec's, and the `DeletePersistContext` of §13.1 carries the returned `DeleteHandle`'s
`DeletedIds` (as `AffectedIds`) and `Captures` — and adds the type-level check, the ceilings, the
hook round, and the translation.

### 10.1 Delete by ids

`DeleteByIdsAsync(ids, expectedStamps)`: `MaxIds`; the `Delete` check of §9.1; when `expectedStamps`
is given, a `null` entry beside an id is `Concurrency.StampRequired` at `Ids[i]` before any round
trip. Without an overridden `ValidateDeleteAsync` and no `IEntityValidator<T>` overriding
`ValidateDeleteAsync`, the operation is one `Persist` round trip:
`Delete(DeleteSpec.ByIds(ids, read filter, decision.Filter, expectedStamps))` — spec 0012's
statement counts the ids under the caller's `Read` filter (`50404`, hidden equals missing), then
under the `Delete` grant (`50403`), then checks stamps (`50409`), inside the transaction and
all-or-nothing, deletes children deepest first by explicit statements, then the roots, captures blob
references for spec 0017's release effect, and appends the tree recount on tree tables; then the
`ContributeAsync` participants of §13.1 over a `DeletePersistContext`, the access guards of §6.7
step 4, and the tag bumps. With a hook, RT1 first loads the target rows under the `Delete` grant
(`Query<TEntity>` restricted by the ids; a short result is `NotFoundException` with the missing
ids), runs `ValidateDeleteAsync` and the components with their rounds, then the persist.
Translation: `50404` → `NotFoundException(Resource, missing ids)`; `50403` → `ForbiddenException`;
`50409` → `ConcurrencyException`; 547 → `ValidationException` with `Fk.InUse` at `Ids[i]`, its
`entity` the referencing entity name and its `property` the referencing property — no C# pre-check
for references, the constraint is the guarantee. A silent partial delete would hide a concurrent
change from the user who selected the rows a second ago; every id must exist and be visible or
nothing is deleted. An enlisted delete is a group of the host's persist batch, composing
`DeleteSpec.ByIds(ids, ReadFilter: null, Filter: null)` with no expected stamps, its hook round
riding the host's rounds (§13.3).

### 10.2 Delete by query

`DeleteByQueryAsync(request)`: a null or blank `Filter` is `BadRequestException` (an empty filter is
"delete all", never exposed); the `Delete` check of §9.1; the filter compiles as
`And(Leaf(request.Filter), decision.Filter)` with the request's arguments — no activatable conjunct,
so a filter that names inactive rows deletes them. One `Persist` round trip:
`Count(spec, arguments, cap = MaxDeleteByQueryRows + 1)` for the actual count, then
`Delete(DeleteSpec.ByQuery(filter, arguments, …))` with `Cap = MaxDeleteByQueryRows` and
`ExpectedCount = request.ExpectedCount` — spec 0012's statement collects the keys `TOP (cap + 1)`,
throws `50413` above the cap and `50428` when the count differs from `ExpectedCount` (both inside
the transaction, so the count the user confirmed is the count deleted), deletes children then roots,
appends the recount; then participants and bumps. Translation: `50413` →
`LimitExceededException("MaxDeleteByQueryRows", actual, cap)` whose message tells the caller to run
it as a background job; `50428` → `CountMismatchException(request.ExpectedCount, actual)`. The
operation is projected to the web surface only (`Mcp = Hidden`, never on the public surface); the
web UI keeps it under an advanced menu. Returns `AffectedResult(count)`.

### 10.3 Delete with descendants

`DeleteWithDescendantsAsync(ids)` on tree stacks: `MaxIds`; the `Delete` check of §9.1;
`Delete(DeleteSpec.WithDescendants(ids, read filter, decision.Filter))` — spec 0012's statement
counts the roots under the caller's `Read` filter (`50404`, hidden equals missing), closes the set
by node, asserts every closed row visible under the `Delete` grant (`50403`; a partially deleted
subtree is never left behind), deletes children then the roots deepest-first, fills the old nodes
before the delete and recounts the ancestors after it. The hook round and the translation are
§10.1's.

## 11. Actions and custom operations

### 11.1 The built-in `activate` and `deactivate`

Registered by the activatable capability as `EntityActionDescriptor`s with `Action = "Activate"`,
`SupportsFilter = true`, `PostCheck = false`, `Idempotent = true`. When neither the service nor a
component overrides `ValidateActionAsync` for the action, the action is a **SQL-only fast path** —
one `Persist` round trip: the update with its pre-check, the participants of §13.1 (with an empty
`Entities`; `AffectedIds` names the target ids), the recount, the bumps, the read-back when
`ReturnEntities`. The pipeline appends one statement of its own,
`IDataBatch.Update(new UpdateSpec<TEntity>.ByIds(ids, read filter, activate grant, { IsActive:
target }, Stamp: true, AllOrNothing: true))`: spec 0012's statement, in its all-or-nothing form,
counts every id under the caller's `Read` filter — a hidden id counts as missing — and selects the
missing ids as a result set before `THROW 50404, N'Entity.NotFound'` (§9.2), then captures the ids
visible under the activate grant into `@tb{b}_keys` and asserts `COUNT(@tb{b}_keys) =
COUNT(@tb{b}_t0)` (`THROW 50403, N'RowSecurity'`) before the `UPDATE` — the pipeline declares no
table and emits no check of its own. Rows already in the requested state are skipped by the
statement (no stamp, no history row), the others are stamped; the executor appends the
`ActiveSubtreeCount` recount on activatable trees. `Entity.NotFound` → `NotFoundException` with the
ids from the preceding result set. One securable covers both directions: "may deactivate but not
reactivate" has no precedent worth doubling every role's configuration. A service or component
that must veto (spec 0014's `UserAccessRules<TUser>` raising `Users.CannotDeactivateSelf`)
overrides `ValidateActionAsync("deactivate", …)`, which moves the action onto the general path.

### 11.2 The general action path

`ExecuteActionAsync(name, ids, arguments, options)` — or `ExecuteActionAsync<TResult>` for an action
with its own result (§3.1) — for an `[EntityAction]` method (and for a built-in action with a
validation hook): the `EntityActionDescriptor` named `name`; `MaxIds`; a `SingleTarget` action
reaches `ExecuteActionAsync<TResult>` with one id, and another count from a service-level caller is
`InvalidOperationException` (an authoring bug); the `descriptor.Action` check of §9.1; `arguments`
used as is when it arrives as a `TArguments` instance (the web layer), or deserialised to
`TArguments` under spec 0016's options when it arrives as a `JsonElement` (MCP's `tellma_action`,
jobs), a shape failure being `BadRequestException`; either way its `ValidationAttribute`s are
walked. **RT1** (`Validate`): the target rows through `Query<TEntity>` restricted by the ids under
the grant's filter (`SupportsFilter`) — with every child collection when the descriptor's
`LoadChildren` — plus every load `ValidateActionAsync` and the method declare before their first
await; a row missing from the result is `NotFoundException` with the missing ids (the filtered load
is the pre-check, as for save). Then `ValidateActionAsync(name, context)` and the method run as
validator and mutator over `ActionContext`: `Errors` collects; `Save(entities)` marks rows for the
emitter, `Update(assignments)` composes one `UpdateSpec<TEntity>.ByIds` over the target ids under
the grant's filter, `Batch` takes statements with declared writes, `Notify` appends notifications,
`Host` takes enlisted writes into other stacks (§13.3); further rounds as declared. Any error →
`ValidationException`. **RT2** (`Persist`): the emitter for the rows marked by `Save` under
`ConcurrencyMode.Override` (an action is a command over rows it just loaded, never an edit of a
loaded copy; a row deleted in between still conflicts as missing), the update specs, the method's
statements, the `ContributeAsync` participants over an `ActionPersistContext` (§13.1), the enlisted
groups at their declared positions, the post-check over the marked and updated ids when `PostCheck`,
the read-back when `options.ReturnEntities` and the action has no result of its own, the bumps. An
action without a result answers `EntitiesResult` with the target ids in request order; an action
with one answers the value its method returned, once RT2 committed. Common cost: two round trips.

**Reading its own writes.** `ActionContext.Persisted` completes after RT2 commits and faults with
the exception that ended the operation: the persist's, or the `ValidationException` raised when
`Errors` is non-empty and RT2 never runs. A method that must read a `BatchResult` it appended —
spec 0018's `invite` and `preferences/set` — awaits `Persisted` before returning; the pipeline
runs RT2 once every participant is complete or parked on `Persisted`, and a method that returns
without awaiting it is answered as returned.

**Partial failure.** A method that calls an external system per id (spec 0018's `invite`) may throw
`PartialFailureException(Code, Results, Failed)` after composing — marking rows through
`Save`/`Update`, appending to `Batch` — and before awaiting `Persisted`. It is the one exception
that does not abort the operation before RT2: the pipeline finishes the validation outcome (an error
in `Errors` is still `ValidationException`, nothing written), executes RT2 with exactly what the
method composed before the throw, runs the post-commit phase, and rethrows the exception unchanged —
the caller's 502 carries the method's `Results` and `Failed`, and the rows the external call
succeeded on are recorded. Every other exception from the method abandons the batch unsent.

*Illustration* — a custom action on a pack service:

```csharp
[EntityAction("post", LoadChildren = true, Destructive = false, Description = "Posts draft invoices to the ledger.")]
public async ValueTask PostAsync(ActionContext<Invoice, int> context, PostArguments arguments) { /* validate, then context.Save(...) */ }
```

projects `(gl.Invoice, Post)` into the securables registry, `ExecuteActionAsync("post", …)`,
`POST …/invoices/post` with `{ ids, arguments }`, the MCP action with the argument schema derived
from `PostArguments`, and the UI button metadata.

### 11.3 `[ApiAction]` operations and `[ApiRoute]` services

A single-body operation that is not id-shaped — `me/save`, the Excel operations, everything on
`inbox`, `settings`, `access` — is a public method marked `[ApiAction]` on an entity service, on a
**stack companion**, or on an `[ApiRoute]` service registered with
`contribution.ApiService<TService>()`. A companion is a class generic over the entity that a feature
attaches to every stack it applies to with `contribution.EntityCompanion<TEntity, TCompanion>()` (a
`StackCompanionContributionItem`; spec 0019 attaches `ExcelOperations<TEntity>` to every stack with
`Export` or `Import`): the realizer closes it over the leaf, resolves it from DI per request, and
projects its `[ApiAction]` methods into the stack's `Actions` as `ApiActionDescriptor`s whose
`HandlerType` is the closed companion — under the stack's segment and resource, exactly as the
service's own. The pipeline registers each operation's securable (§2.7) and lists it in the
descriptor.

Every `[ApiAction]` method is reached through one entry:

```csharp
// Tellma.Core.Abstractions.Crud; Tellma.Core implements; the one entry to every [ApiAction] method
public interface IApiActionInvoker
{
    Task<object?> InvokeAsync(ApiActionDescriptor action, object? body);
}
```

It resolves `HandlerType` from DI per request (the service, the closed companion or the `[ApiRoute]`
service), calls `RequireAsync(Securable.Resource, Securable.Action)` per §9.1 or, when `Securable`
is null, requires a connected active member (the connect cache, else a prologue-only round trip),
registers the frame of §13.3 in the scope as the `IOpenWriteHost` with `Kind = ApiAction` and
`Operation = "<Resource>:<name>"`, then invokes the method with the body. Spec 0016 projects
`POST /{tenantId}/api/web/<segment>/<name>` and the MCP entry from `HandlerType` and calls
`IApiActionInvoker` per request; the MCP tools, jobs and tests call through it too, and
`[EntityAction]` methods are reached only through `ExecuteActionAsync`, which evaluates the same
way. A call to a method marked `[ApiAction]` or `[EntityAction]` from outside its declaring type and
`Tellma.Core` is the analyzer error `TELLMA0004` (spec 0012 §12.3), so a caller cannot reach a
method whose securable nobody evaluated. Inside, the method composes what it needs: the stack's own
operations (`SaveAsync`, `GetByIdsAsync`), enlisted writes — into other stacks, or into its own
through §13.3's self-pair — persisted through `PersistAsync` on the `IOpenWriteHost` it injects
(§13.3), or its own batches through spec 0014's `IGuardedBatchRunner` injected into the subclass,
with declared writes on every raw statement. It never opens a connection and never throws outside
the closed set of §14.

## 12. Capability recipes

The declaration is the entity's own shape; everything in the third column is projected from it
through `StackDescriptor` and the pipeline. Nothing is declared a second time.

| Capability | Declared by | Projects |
|---|---|---|
| Keyed, audited | `TopLevelEntity<TKey>` | `Read`/`Save`/`Delete`; `query`, `get`, `get-by-ids`, `save`, `delete`, `delete-by-query`; `ModifiedAt` concurrency; the audit columns server-owned; `[Unique]`/`[NaturalKey]` validators and the 2601/2627 map |
| Activatable | `IActivatable` | `Activate` securable (both directions); `activate`, `deactivate` fast path (§11.1); the activatable conjunct lifted by `IncludeInactive`; `IsActive` server-owned (created active, changed only through the actions); the inactive-banner metadata; `ActiveSubtreeCount` recount on trees |
| Tree | `TreeEntity<TKey>` | `get-by-parent-ids`, `delete-with-descendants`; `IncludeAncestors`; `ParentId` editable, `SubtreeCount`/`ActiveSubtreeCount`/`Node` server-owned; `TreeCycleValidator<T>` in RT1 and the SQL fence in RT2; the recount after save, `Update` actions and deletes; temporary ids in `ParentId` |
| Temporal | `[Temporal]` | skip-unchanged rows (no no-op history row); a parent whose children changed is still stamped |
| Blob reference | `[BlobReference]` on an `int?` property of the root or of a child entity of the aggregate | `BlobReferenceValidator<T>` (context load of the staged rows, `Blob.NotAttachable`, `Blob.DuplicateReference`) and `BlobReferenceEffect<T>`, an `IPersistEffect` (release/confirm ending in `THROW 50422, N'Blob.NotAttachable'`), registered by the blob feature; the property stays `Editable`; excluded from Excel; the download authorisation is spec 0017's |
| Multilingual | `[Multilingual]` on the primary of a `P/P2/P3` group | twins gated by `MultilingualShape` (dropped from the payload and absent from the schema); `(E)`/`(ع)` labels; search over the configured columns of the group |
| Cacheable | `[Cacheable(MaxRows)]` | the `entity:<Name>` tag bumped by every write; `all` (`GetAllCachedAsync`); `Read` registered with `FilterRoot = null`; `settings/entity-tags` lists the tag |
| Job entity | `IJobEntity` | `JobId` server-owned; the claim's entity join load (spec 0020); `JobRequest.Entity` sets the column inside the enqueue statement, and a handler that inserts its own row (`core.export`) supplies it through `EnlistSaveOptions.ServerOwned` on the enlisted insert (§13.3) |
| Searchable | `[Searchable(Kind)]` | the `Search` disjunction (§5.2.1); `SearchableProperties` in the descriptor |
| Excel | `Export` or `Import` in `Operations` | `export` and `export/start` (`Read`) under `Export`; `export-for-import` and `export-for-import/start` (`Read`), `inspect-import`, `import` and `import/start` (`Save`) under `Import` — spec 0019's `ExcelOperations<T>` attached as a stack companion (§11.3); `ExcelRowSource` (§15) |

Read-only stacks are `[Stack(Operations = Read)]`, not a type: write operations and securables are
not registered and no write routes are projected. A distribution leaf adding a capability interface
or annotation projects exactly as a pack entity would; a distribution cannot remove a pack's
capability from a leaf (the base carries it).

### 12.1 Trees

`Node` is a platform-owned shadow column that never appears on the class or the wire: configured by
spec 0012's tree convention, excluded from the UDTT, exposed to Queryex as the entity's `TreeNode`
(`level(Node)` emits `GetLevel()`; no `Level` column, no `IsLeaf` — a leaf is `SubtreeCount = 1`),
written only by the recompute. New rows are inserted with the provisional node `/0/<Id>/` and
re-pathed in the same transaction over the affected set (saved rows plus descendants of saved
existing rows); the path is the id path (`/15/342/1207/`), so a node depends only on the ancestor
chain and a recompute never renumbers untouched siblings; the counts are recomputed over the
affected rows and the ancestors of their old and new nodes, never the whole table (the weekly
`core.tree-verify` job of spec 0020 is the whole-table repair). Cycle validation runs twice by
design: `TreeCycleValidator<T>` in RT1 reports `Tree.Cycle` at `[i].ParentId` from the loaded
`(Id, ParentId)` set overlaid with the payload; a cycle that forms between the rounds trips the SQL
path-count fence (`THROW 50422, N'Tree.Cycle'`), the transaction rolls back, and the pipeline
raises `ValidationException(Tree.Cycle)` — the user retries and validation reports it. Recursion
overflow (error 530) is `ValidationException(Tree.TooDeep)`. No additional locks: the emitter's
concurrency guard U-locks the saved rows, so two saves that would close a cycle serialise.

### 12.2 Activatable

`IsActive` is `ServerOwned`: every row is created active — a save with `Id = 0`, an `Insert` import
and the insert half of `Upsert` ignore the payload's value, like every server-owned column — and
afterwards it changes only through the actions under the `Activate` securable, the one path that
writes it; the save-bypasses-activate hole is closed without a diff gate. The activatable conjunct
`Leaf("IsActive = true")` joins the filter of `query`, `get-by-parent-ids` and the Excel row
source's `ByQuery` (§15) unless `IncludeInactive`, and never that of details by id, delete by query
or actions. On `ActivatableTreeEntity` the `ActiveSubtreeCount` recount follows every activation.

## 13. Side effects and post-commit

### 13.1 Transactional participation

```csharp
// Tellma.Core.Abstractions.Crud
public abstract class PersistContext<TEntity>
{
    public IReadOnlyList<TEntity> Entities { get; set; }    // save: the payload with final ids; action: the target rows (empty on §11.1's fast path); delete: empty
    public RequestContext Context { get; set; }
    public IDataBatch Batch { get; set; }                   // the persist batch; append-only for participants
    public SqlIdentifier AffectedIds { get; set; }          // save: @tb{b}_saved; action: the target ids; delete: the deleted key table
    public IReadOnlyList<ColumnCapture> Captures { get; set; }   // the emitter's captures of this persist; empty on an action persist
    public EnlistmentDescriptor? Enlistment { get; set; }   // non-null on the context of an enlisted group
    public void Notify(NotificationRequest request);        // INotifier.Notify on Batch
}

public sealed class SavePersistContext<TEntity> : PersistContext<TEntity>
{
    public TEntity? Before(int index);
    public SaveOptions Options { get; set; }                // an enlisted save: Concurrency and Source from EnlistSaveOptions, the rest default
    public SqlIdentifier NewIds { get; set; }               // @tb{b}_new — inserted roots
    public SqlIdentifier TouchedIds { get; set; }           // @tb{b}_touched — parents of synchronised children
}

public sealed class ActionPersistContext<TEntity> : PersistContext<TEntity>
{
    public string Action { get; set; }                      // the action's Name
    public object? Arguments { get; set; }
}

public sealed class DeletePersistContext<TEntity> : PersistContext<TEntity>;

public abstract class PersistOutcome<TEntity>
{
    public IReadOnlyList<TEntity> Entities { get; set; }    // save: the payload with final ids; action: the target rows (empty on §11.1's fast path); delete: empty
    public DateTimeOffset Stamp { get; set; }               // datetimeoffset(7)
    public RequestContext Context { get; set; }
    public VersionTagSnapshot VersionTags { get; set; }     // after the bumps; BatchOutcome.VersionTags
    public UserVersionTagSnapshot? UserVersionTags { get; set; }   // BatchOutcome.UserVersionTags (spec 0013 §2.5)
}

public sealed class SavePersistOutcome<TEntity> : PersistOutcome<TEntity>
{
    public TEntity? Before(int index);
    public SaveOptions Options { get; set; }
}

public abstract class DeletePersistOutcome<TEntity> : PersistOutcome<TEntity>;

public sealed class DeletePersistOutcome<TEntity, TKey> : DeletePersistOutcome<TEntity>
{
    public IReadOnlyList<TKey> DeletedIds { get; set; }     // descendants included for a delete with descendants
}

public abstract class ActionPersistOutcome<TEntity> : PersistOutcome<TEntity>
{
    public string Action { get; set; }                      // the action's Name
    public object? Arguments { get; set; }
}

public sealed class ActionPersistOutcome<TEntity, TKey> : ActionPersistOutcome<TEntity>
{
    public IReadOnlyList<TKey> Ids { get; set; }            // the target ids, request order
}
```

`ContributeAsync(PersistContext<TEntity>)` — the service hook, then every `IPersistEffect<T>` in
registration order — runs for **every** `Persist` batch on the stack's table, with the context kind
of its operation: a `SavePersistContext` for a save, a `DeletePersistContext` for a delete by ids,
by query or with descendants, an `ActionPersistContext` for `activate`/`deactivate` and every
action. Every enlisted group has a context of its own kind with `Enlistment` set (§13.3). A
participant that needs a save-only member type-tests `context is SavePersistContext<TEntity> save`.
It runs after the operation's own statements are appended and before the batch is sent, with final
ids. An effect that acts on a captured column — spec 0017's blob confirm and release, on a save and
a delete alike — looks the capture's identifier up by `(Table, Column)` in `Captures` and needs no
second contract. It may only append to `Batch`: raw `Sql` with declared `Writes` (and
`SqlOptions.UserIds` when the written table carries a user-level tag rule), `Tvp`, `DeclareIdTable`,
`Query`/`Rows` readers whose results are read in `AfterCommitAsync` through `BatchResult.Value`,
`Notify` (spec 0021's `INotifier.Notify(batch, …)`: ids assigned inside the statement, the
`inbox.changed` event registered through `OnCommitted`), spec 0020's `IJobQueue.Enqueue(batch, …)`
for durable work, spec 0017's confirm/release. It performs no I/O of its own; the context exposes no
connection. Everything appended commits or rolls back with the operation; a distribution guard is
`THROW` in the band `50600–50699`, which the executor maps to `BatchAssertionFailedException` and
the pipeline to `ValidationException` with the message as the code at `ValidationPath.Root`. Tag
bumps are never hand-written: the executor derives them from every statement's declared writes plus
explicit `BumpVersionTag` calls (spec 0013). Anything that must eventually happen — an email, an
e-invoice filing — is a job row appended here and executed by spec 0020's worker; the pipeline has
no pre-commit non-transactional phase. A front-door caller's own statements reach a save's persist
batch through `SaveOptions.OnPersist` (§4.1, §6.7), never through an effect or ambient state.

### 13.2 Post-commit

`AfterCommitAsync(PersistOutcome)` — the hook, then every `IPersistEffect<T>` — runs after the
commit is acknowledged, outside any transaction, with the outcome kind of the operation that
committed, paired with the context kind of §13.1: a `SavePersistOutcome` (the payload with final
ids, the before images, the options), a `DeletePersistOutcome` (the deleted ids) or an
`ActionPersistOutcome` (the action's name, its arguments, the target ids), each carrying the batch
stamp and the version tags after the bumps. A participant that reacts to one kind type-tests
`outcome is ActionPersistOutcome<TEntity> { Action: "activate" }`; the service hook, knowing its
key, tests the keyed kind for the ids. The batch's `OnCommitted` callbacks (the hub nudge,
`job.changed`) run in the same phase. Uses: SignalR pushes, best-effort external calls a
distribution accepts as best-effort, spec 0011's membership hint write-back. Failures are logged
with the operation's trace id and counted on `tellma.crud.effects.failures` (tag `crud.effect` = the
component's type name); they never change the response and are never retried by the executor. No
blob is deleted here: replaced blobs are released inside the transaction and swept by spec 0017's
job.

### 13.3 Enlisted writes

A write into another stack's table runs through that stack's own pipeline as a participant of the
frame the platform opened around the caller's work; nothing calls a second `EntityService` and
nothing opens a second transaction.

```csharp
// Tellma.Core.Abstractions.Crud
public interface IEnlists<TTarget>;                             // marker on a writer: this type writes rows of TTarget's stack

public enum WriteHostKind { Pipeline, ApiAction, Job, Provisioning }

public interface IWriteHost                                     // the current frame; contexts expose it as Host
{
    WriteHostKind Kind { get; }
    string Operation { get; }                                   // "gl.Invoice:post", "gl.Center:import/start", "job:core.export", "provisioning:<step>"
}

public interface IOpenWriteHost : IWriteHost                    // scoped; the frame of an [ApiAction], a job or a provisioning step
{
    Task<EnlistmentOutcome> PersistAsync();
}

public sealed record EnlistmentOutcome(
    DateTimeOffset Stamp, VersionTagSnapshot VersionTags,
    UserVersionTagSnapshot? UserVersionTags);                   // null when the persist batch carried no connect prologue (spec 0012 §5.2's BatchOutcome)

public enum EnlistOrder { AfterHost, BeforeHost }

public sealed class EnlistSaveOptions
{
    public EnlistOrder Order { get; set; } = EnlistOrder.AfterHost;
    public Func<ValidationPath, ValidationPath>? MapPath { get; set; }          // target path → host path; null: everything to Root
    public ConcurrencyMode Concurrency { get; set; } = ConcurrencyMode.Override;   // Check for a row the writer loaded itself
    public IReadOnlySet<string> ServerOwned { get; set; } = [];                    // server-owned members the writer supplies; never an audit column or IsActive
    public SaveSource? Source { get; set; }                                        // default: the host's
}

public sealed class EnlistDeleteOptions
{
    public EnlistOrder Order { get; set; } = EnlistOrder.BeforeHost;
    public Func<ValidationPath, ValidationPath>? MapPath { get; set; }
    public SaveSource? Source { get; set; }
}

public sealed class EnlistedSave<TEntity>
{
    public IReadOnlyList<TEntity> Rows { get; }                 // final ids once the task completed; stamped after commit
    public string Resource { get; }
}

public sealed class EnlistedDelete<TKey>
{
    public IReadOnlyList<TKey> Ids { get; }
    public string Resource { get; }
}

public static class EnlistmentExtensions                        // the receiver constraint is the compile-time gate
{
    public static Task<EnlistedSave<TTarget>> EnlistSaveAsync<TTarget>(
        this IEnlists<TTarget> writer, IWriteHost host, IReadOnlyList<TTarget> rows, EnlistSaveOptions? options = null);
    public static EnlistedDelete<TKey> EnlistDelete<TTarget, TKey>(
        this IEnlists<TTarget> writer, IWriteHost host, IReadOnlyList<TKey> ids, EnlistDeleteOptions? options = null)
        where TTarget : Entity<TKey> where TKey : struct;
}
```

`Enlist`, `EnlistmentDescriptor` and the registry's `Enlistments` and `OwnerOf` are §2.2; `Host` on
the validation contexts and `Enlistment` on the save and delete contexts are §7.1, `Enlistment` on
the persist contexts §13.1.

**The shape.** A **writer** — a service, an `IEntityValidator<T>` component, a stack companion, a
job handler, a provisioning step — that must write rows of another stack's entity enlists the write
with its **host**, the frame the platform opened around the work it is doing, and the target stack's
stages run as participants of that frame: the target's checks and validators are more tasks on the
host's scheduler, the target's statements are one more **group** in the host's persist batch at a
declared position, and the target's post-commit runs after the host's commit. Two declarations gate
a pair: the target's entity carries `Enlist` in its `[Stack]` operations, and the writer implements
`IEnlists<TTarget>`, the receiver constraint of `EnlistSaveAsync` and `EnlistDelete`, so an
undeclared pair is a compile error. The realizer reads every pair from the types the contribution
items already register into `IStackRegistry.Enlistments`; nothing is declared a second time. A
**frame** is an `IWriteHost` the platform opens around the work: the pipeline around a save, a
delete or an action (`Kind = Pipeline`), `IApiActionInvoker` around an `[ApiAction]` method (§11.3),
spec 0020's worker around a handler's `ExecuteAsync`, spec 0011's step runner around a provisioning
step. The last three are *open* frames: each registers its frame in the scope as an
`IOpenWriteHost`, the `IWriteHost` that adds `PersistAsync`, which the method, handler or step
injects; a pipeline frame is an `IWriteHost` alone, reached only as a context's `Host`. Frames nest:
a front-door operation entered inside an open frame — spec 0019's import handler saving a chunk on
the target stack, an `[ApiAction]` calling `SaveAsync` — runs its own pipeline frame with its own
transaction inside it, and the innermost frame is the `Host` its participants see; the outer frame's
groups are untouched by it. `Operation` is `"<Resource>:<operation>"` for a pipeline or
`[ApiAction]` host (`gl.Invoice:post`, `gl.Center:import/start`), `"job:<key>"` for a job and
`"provisioning:<step>"` for a step. A pipeline frame is *composing* from id assignment to the close
of the last validation round; an open frame stays open until its `PersistAsync`.

**Where enlistment is allowed.** In a pipeline host, while the frame is composing: `ValidateAsync`,
`ValidateDeleteAsync`, `ValidateActionAsync`, an `[EntityAction]` method, an `IEntityValidator<T>`.
`PreprocessAsync` is refused (host ids are not yet assigned, §6.1 step 3 precedes step 4);
`ContributeAsync` and `AfterCommitAsync` are refused (the rounds are over). In an open frame — an
`[ApiAction]`, job or provisioning host — any time before its `PersistAsync`. The receiver must be a
participant of the frame — the host's service, one of its components, the type the frame was opened
for, or a participant of a group inside it. Each refusal is `InvalidOperationException`: an
authoring bug, never a user outcome.

**What the call does.** `EnlistSaveAsync` checks the pair is declared and the receiver a
participant, then runs the target's pre-persist stages over `rows` as a task on the host's
scheduler: shape and ceilings (§6.1 step 1), the platform preprocessing and the target's
`PreprocessAsync` (§6.3), `IIdAllocator.Reserve` (immediate on a warm buffer, otherwise parked at
`LoadAsync` like a validator's load, §6.4), the before images of every update row loaded unfiltered
in the current round, the checks of §6.6 — a missing before image is `Entity.NotFound` at the mapped
path, never a 404 — and then the target's `ValidateAsync` and every `IEntityValidator<TTarget>` as
further tasks. Every payload row of every participating stack is primed on the shared loader (§7.2),
so a target `ByIds` for a host id costs nothing and a target row referencing a host row inserted in
the same batch validates; the FK pre-load of §7.4 is skipped for enlisted rows, and 547 through the
target's constraint names is their check. Rounds are shared and bounded by the host's
`MaxValidationRounds`. `EnlistedSave.Rows` carry final ids once the task completed and the batch
stamp after commit. `EnlistDelete` registers a delete group and runs the target's
`ValidateDeleteAsync` and its components over the target rows, loaded unfiltered in the current
round. `EnlistSaveOptions.ServerOwned` names server-owned members the writer supplies (`JobId` on an
`IJobEntity`): the pipeline re-applies them from the writer's rows after
`EntityMetadata.ResetServerOwned` (spec 0012 §2.4); an audit column or `IsActive` there is refused.
`Source` is recorded, never a rule input (§4.1): a pipeline host's `SaveOptions.Source`, the
request's client (`Web`, `Agent`, `PublicApi`) for an `[ApiAction]` host, `System` for a job or
provisioning frame, unless the `Source` of the `EnlistSaveOptions` or `EnlistDeleteOptions` names
another.

**Assembly for a pipeline host.** A group is the target's emitter — for a save
`Save<TTarget>(rows, options.Concurrency)`, for a delete `Delete<TTarget>` of the
`DeleteSpec.ByIds(ids, ReadFilter: null, Filter: null)` of §10.1 with no expected stamps — followed
by the target's `ContributeAsync` and every `IPersistEffect<TTarget>` with a
`SavePersistContext<TTarget>` or `DeletePersistContext<TTarget>` of their own (§13.1). The persist
batch of §6.7 places `BeforeHost` groups ahead of the host's emitter and `AfterHost` groups after
the host's effects, each set in enlistment order, and calls the access guards over the union of
every group's declared writes. A host's group is composite — its own statements plus its nested
groups in their declared positions — and a nested enlistment is positioned relative to its
*immediate* host's group: when A enlists B with `BeforeHost` and B enlists C with `AfterHost`, the
batch order is B, C, A. Ids flow both ways in memory because every stack's ids are assigned before
assembly; a row that must reference a row of a *later* group is unsupported (SQL Server checks a
foreign key per statement), so the author orders or splits the enlistments. One target enlisted
twice in one host is two groups; a root id present in both is refused at the second enlistment.

**Nesting.** A target's own hooks may enlist a further target through `context.Host` when that pair
is declared. The declared pair graph over stacks is acyclic at startup, self-pairs excluded from the
check (§2.6): a stack's service may implement `IEnlists<>` of its own entity, usable only from an
open frame its own `[ApiAction]` opened (spec 0018's `me/save`), and a self-pair enlistment from a
pipeline frame is refused at run time (`InvalidOperationException`), so a hook never re-enters its
own stack and depth stays bounded by the longest declared path, with no runtime constant. Write-back
belongs to the document's own action: an invoice's `void` enlists the transaction delete; a
transaction never writes its source.

**The `[ApiAction]` host.** The invoker evaluates the securable, opens the frame and invokes the
method (§11.3); the method enlists what it needs and awaits `host.PersistAsync()` on the
`IOpenWriteHost` it injects: the pending rounds run through the guarded runner on `Validate` batches
(zero round trips when nothing declared a load), then one `Persist` batch carries every group in
declared order, the access guards when due, no post-check and the bumps; post-commit follows §13.2.
The outcome, an `EnlistmentOutcome`, is the batch stamp and the version and user-level tags after
the bumps. A second call is `InvalidOperationException`.

**The job host.** Spec 0020's worker opens the frame around each `ExecuteAsync`, bound to the
partition's completion batch. `PersistAsync` runs the pending rounds at once (zero or one round
trip) and appends every group to `JobBatch.Batch` after the completion statement, so the target's
rows commit with the outcome and never survive a lost lease; the target's post-commit runs in the
worker's post-commit phase. A provisioning frame (spec 0011 §6.2) behaves as an `[ApiAction]` host
under the step's system scope.

**Authority.** The host's securable (the member check for a member-only `[ApiAction]`), the job's
run-as scope or the step's system scope authorises the whole frame; an enlisted group carries no
decision of its own (spec 0014 §5.3). Skipped for an enlisted write:
`Evaluate(TTarget, Save | Delete)`, the `Read`-filtered before images and the `Save`-grant count of
§9.2, the FK visibility pre-load of §7.4, the criteria providers, the row-level post-check of §9.3
and the client stamp comparison. Everything else runs: shape, ceilings, ownership, uniqueness,
trees, validators, `ContributeAsync`, effects, blob capture, audit stamps, tag bumps and the access
guards. `RequestContext` is the host's; the target's audit columns name the acting user.

**Errors.** Each participant's `Errors` is a re-homing view over the host's one
`ValidationErrors`: a target hook keeps writing `Errors.Add(i, x => x.Amount, code)` and the path
lands in the host's list through `MapPath`, applied to the whole `ValidationPath`, so a
transaction-line error lands on the invoice line that produced it; the default maps everything to
`ValidationPath.Root`. `MapPath` composes along the nesting: a C path maps through B's `MapPath`
first and then through A's, so C's errors land under B's location inside A, never directly on A.
Every re-rooted error carries the target's entity name under `enlistedResource`. A persist-time
error from a group is attributed by `StatementOrdinal` on spec 0012's executor exceptions (spec
0012 §6.4) to the group whose statements hold the ordinal and translated by §14.2 with the target's
resource and properties: a `50404` from an enlisted delete is `Entity.NotFound` at the mapped path
with the ids as an argument; a `50422` from the target's blob effect, a 2601/2627, a 547 or a
`5060x` from the target's `ContributeAsync` land under the group's mapped path — even when two
writers enlist one target in one host.

**Concurrency.** An enlisted save is a platform command: `Override` by default — U locks still
taken, a vanished row still conflicts as missing (§8) — with every written target root stamped with
the batch stamp and the acting user; `EnlistSaveOptions.Concurrency = Check` when the writer carries
a stamp it loaded itself, a default stamp then being `Concurrency.StampRequired` at the mapped path.
An enlisted delete carries no expected stamps (§10.1). Two writers of one mutable target row
serialise on the lock; a target meant for many writers is append-or-delete.

**Telemetry.** One counter, `tellma.crud.enlistments` (§16.1). Round trips stay on the host's
`DataAccessScope.Operation`; spec 0012's `tellma.data.rows.saved` counts the target's rows; no
existing instrument gains a tag.

**Bypass resistance.** Three layers. At compile time, `TELLMA0005` refuses a front-door call of any
`EntityService<,>` — `SaveAsync`, `DeleteByIdsAsync`, `DeleteByQueryAsync`, `ExecuteActionAsync`,
`ActivateAsync`, `DeactivateAsync`, `DeleteWithDescendantsAsync` — from a pipeline participant, and
`TELLMA0006` refuses a raw statement or a `Save<T>`, `Update<T>` or `Delete<T>` that writes a
stack-owned table from a type outside `Tellma.Core` that is not the table's owner; spec 0012 §12.3
states both. A pipeline participant holds its frame as an `IWriteHost`, which has no `PersistAsync`.
At startup, the `core.enlistments` check of §2.6. At run time, `InvalidOperationException` — a 500
with a trace id, never a user outcome — for a front-door operation entered while a frame is
composing (what the analyzer cannot follow through helpers), an undeclared pair, a non-participant
receiver, a closed phase, a `PersistAsync` on an open frame while a pipeline frame nested in it is
composing, and a statement appended through `IDataBatch`'s public members whose declared writes
include a table `OwnerOf` maps to a stack outside the appender's `CallerAuthority` — the stack whose
participant holds the batch: the host's stack for the host's hooks and effects, the target's stack
for a group's, the caller's for a `SaveOptions.OnPersist` delegate (§4.1), nothing for a job or
provisioning handler's own statements or delegate — refused at append naming the table, its owner
and the remedy. The platform's own composers — the emitter, `IJobQueue`, `INotifier`,
`IJobProgress.Append`, `IAccessGuards`, the blob effect — append through the internal channel of
spec 0012 §5.2, which the authority check does not cover.

**Round trips.** An enlistment adds no round trip to its host; it adds one only when made after
the writer's last dispatch and the target declares a load (§16.2). An action host whose target
declares validator loads costs three, because the rows are built from RT1-loaded targets and the
target's loads land in a further round. The recipe back to two is a target whose rules are
persist-time checks in its `ContributeAsync` — `THROW` in the `50600–50699` band (§13.1) — rather
than validators: rows a target receives from application logic are valid unless a race or a
posting bug occurred, and both are caught at persist time.

The consumers:

| Consumer | Writer and host | Round trips |
|---|---|---|
| `import/start` (spec 0019 §12.2) | `ExcelOperations<TEntity> : IEnlists<Import>` enlists one `Import` row in the invoker's frame and awaits `PersistAsync` on the injected `IOpenWriteHost`; `ImportService.ContributeAsync` enqueues every new row whose `JobId` is null | 2 (the staged-upload attach loads the blob row) |
| The export handler's row (spec 0019 §12.3) | `ExportJobHandler : IEnlists<Export>`: one enlisted insert at the end of every run with `FileId` set, `JobId = Job.Id` named in `EnlistSaveOptions.ServerOwned`; the blob effect confirms inside the completion transaction | 1, atomic with completion |
| The import handler's completion (spec 0019 §12.4, §12.6) | `ImportJobHandler : IEnlists<Import>`: the chunks are front-door `SaveAsync` on the target stack (the handler is a host, not a participant) carrying the checkpoint through `SaveOptions.OnPersist`; the completion row is an enlisted update on the completion batch | 1 per completion |
| A document's `post` into a ledger transaction stack (the `post` action of §11.2) | the transaction stack declares `Enlist`; the document service implements `IEnlists<>` of it; `post` builds the transaction rows and enlists them with a `MapPath` onto the document lines; `unpost` enlists a delete (`BeforeHost` by default) | post 2, or 3 when the target declares validator loads (the recipe above); unpost 2, or 3 with a delete validator |
| `users/me/save` (spec 0018 §3.5) | `UserService<TUser> : IEnlists<TUser>`: the caller's own row, loaded by the method, enlisted in the invoker's frame with `Concurrency = Check` | 3: the load, the validation round, the persist |

## 14. Exceptions and error translation

### 14.1 The closed set

```csharp
// Tellma.Core.Abstractions.Errors
// Code: kebab-case problem code; Arguments: raw display values (§7.3); the message is for logs
public abstract class TellmaException : Exception
{
    public string Code { get; }
    public IReadOnlyDictionary<string, object?> Arguments { get; }
}

public sealed class ValidationException(IReadOnlyList<ValidationError> Errors)
    : TellmaException;                                  // 422 validation
public sealed class NotFoundException(string Resource, IReadOnlyList<string> Ids)
    : TellmaException;                                  // 404 not-found
public sealed class ForbiddenException(string Code, string Resource, string? Action)
    : TellmaException;                                  // 403 forbidden
public sealed class ConcurrencyException(string Code, IReadOnlyList<ConcurrencyConflict> Conflicts)
    : TellmaException;                                  // 409 concurrency-conflict
public sealed record ConcurrencyConflict(
    long Id, string? ModifiedAt, long? ModifiedById, string? ModifiedByName, bool IsMissing);
public sealed class CountMismatchException(int Expected, int Actual)
    : TellmaException;                                  // 409 count-mismatch
public sealed class LimitExceededException(string Limit, long Actual, long Maximum)
    : TellmaException;                                  // 413 limit-exceeded
public sealed class InvalidQueryException(IReadOnlyList<QueryexDiagnostic> Diagnostics)
    : TellmaException;                                  // 400 query-invalid
public sealed class BadRequestException(string? Detail)
    : TellmaException;                                  // 400 bad-request
public sealed class IndividualRequiredException()
    : TellmaException;                                  // 403 individual-required
public sealed class DependencyUnavailableException(string Dependency, TimeSpan? RetryAfter, Exception? Inner)
    : TellmaException;                                  // 503 dependency-unavailable
public sealed class PartialFailureException(string Code, object Results, IReadOnlyList<string> Failed)
    : TellmaException;                                  // 502 partial-failure
```

The remaining members of the closed set spec 0016 maps by type are defined by their owners and
derive from the same base: `StaleContextException` (spec 0013, 503), `TenantNotFoundException`,
`TenantUnavailableException`, `TenantStateException` and `StepUpRequiredException` (spec 0011),
`BlobRejectedException` (spec 0017) and `ImportException` (spec 0019). Everything else is a 500 with
a trace id and is never mapped by name; internal invariants — a validator exceeding its rounds, an
unknown action, an unsupported operation on a read-only stack — throw BCL exceptions, because they
are authoring bugs, not user outcomes. `ConcurrencyConflict.ModifiedAt` is the opaque stamp string;
`ModifiedByName` is the modifier's name in the request's language (§8). Codes are stable strings; a
platform minor may add codes, never rename one.

An `OperationCanceledException` raised on the request's aborted token is neither mapped nor logged:
the operation is abandoned, nothing is written to the response, and `tellma.crud.operations` counts
it under `crud.outcome = cancelled`. A timeout the host imposes is spec 0016's 504.

### 14.2 Translation

The executor raises spec 0012's internal data-layer exceptions; the pipeline translates them for
every stack operation, a service that runs its own batch outside the pipeline translates its own
assertions (spec 0017 §3.4's staging quota, spec 0020 §5.3's lease fence), and spec 0020's worker
translates a completion batch's failure by the rows below (spec 0020 §5.6); nothing else does.

| Raised by the executor | Trigger | The pipeline raises |
|---|---|---|
| `ConcurrencyConflictException(Conflicts)` | `THROW 50409` after the conflict result set | `NotFoundException` when every row is missing; else `ConcurrencyException("concurrency-conflict", conflicts)` built from the rows |
| `RowSecurityException` | `THROW 50403` | `ForbiddenException("forbidden", Resource, Action)` |
| `BatchAssertionFailedException(50404, "Entity.NotFound")` | delete/action pre-check | `NotFoundException(Resource, ids from the preceding result set)` |
| `BatchAssertionFailedException(50413, …)` | delete-by-query cap | `LimitExceededException("MaxDeleteByQueryRows", actual, cap)` |
| `BatchAssertionFailedException(50428, …)` | delete-by-query count | `CountMismatchException(expected, actual)` |
| `BatchAssertionFailedException(50422, code)` | any invariant other than `Tree.Cycle` (`Blob.NotAttachable`, `Access.LastAdministrator`, `VersionTag.Missing`, `Job.LeaseLost`) | `ValidationException` with one error, `Path = ValidationPath.Root`, `Code = code`; `Blob.NotAttachable` carries the argument `reason = persist` (spec 0017 §4.4) |
| `BatchAssertionFailedException(50600–50699, code)` | a distribution guard | `ValidationException` with one error, `Path = ValidationPath.Root`, `Code = code` |
| `TreeCycleException`, `TreeDepthExceededException` | the path-count fence; error 530 | `ValidationException(Tree.Cycle)`, `ValidationException(Tree.TooDeep)` at `ValidationPath.Root` |
| `UniqueConstraintViolationException(IndexName)` | 2601/2627 on `UX_<Table>_<Col>` | `ValidationException(Unique)` at `[i].<Property>` when one payload row carries that property, else at `ValidationPath.Root` with `property` as an argument — the race case; the validator of §6.6 reports the ordinary duplicate. A `UX_<Table>_<Column>` of a `[BlobReference]` column maps to `Blob.NotAttachable` (`reason = persist`) at the property path — two concurrent saves attaching one staged id |
| `ForeignKeyViolationException(ConstraintName)` | 547 on `FK_<Table>_<Column>` | save: `ValidationException(Fk.NotFound)` at `[i].<Property>` with `entity` and `id`; delete: `ValidationException(Fk.InUse)` at `Ids[i]` with `entity` and `property`, both derived from the constraint name |
| `DataAccessAmbiguousException` after an inconclusive probe | connection lost around `COMMIT`, update-only | `DependencyUnavailableException("database", RetryAfter = 1 s)` |
| `DataAccessRetryExhaustedException(Inner, Attempts)` | retries exhausted on `50503` (applock timeout) or a reported transient failure (deadlocks) | `DependencyUnavailableException("database", RetryAfter = 1 s)` |
| `THROW 50412`, `50401` | stale tags, caller invalid at write time | the runner's `StaleContextException` / `TenantNotFoundException`; passed through |

Every `THROW` the pipeline itself emits uses a `TellmaSqlErrors` number (`50403`, `50404`); no
number outside the two bands appears in platform or distribution SQL.

A persist-time error raised by an enlisted group's statements is attributed to that group by the
`StatementOrdinal` the executor's exception carries (spec 0012 §6.4), translated by the rows above
with the target's resource, index names and constraint names, and re-rooted through the group's
`MapPath` with `enlistedResource` among the arguments (§13.3). A group's statements carry no read
filter and no grant count, so `50403` never arises from one, and its `50404` is
`ValidationException(Entity.NotFound)` at the mapped path with the missing ids as an argument.

## 15. The Excel row source

Spec 0019's codec reads rows through `IExcelRowSource` and never opens a connection; the pipeline
implements it: after the `Read` check of §9.1, `EntityService.ExcelRowSource(source)` returns an
`ExcelRowSource<TEntity, TKey>` bound to one `ExportSource` case (§4.2). The members of spec 0019's
contract it implements, verbatim:

```csharp
public sealed record ExcelQuery(
    string RootEntity, IReadOnlyList<string> SelectPaths, string? OrderBy, KeySetRestriction? Restriction,
    IReadOnlyList<object>? RestrictionValues, int? Take, int? CountCap, IReadOnlyList<ExcelQuery>? Dependents);

public sealed record ExcelPage(
    IReadOnlyList<IReadOnlyList<object?>> Rows, IReadOnlyList<ExcelPage> Dependents, int? Count);

// implemented by spec 0015 over IDataBatch.Rows with the caller's read filter
public interface IExcelRowSource
{
    IAsyncEnumerable<ExcelPage> Pages(ExcelQuery query);
    Task<IReadOnlyList<IReadOnlyList<IReadOnlyList<object?>>>> ReadAsync(IReadOnlyList<ExcelQuery> queries);
}
```

Every `RootEntity` is an entity name (`gl.Center`, `core.RoleMembership`); the stack's own is
`Descriptor.Resource`. A query on the stack's own root builds a `QuerySpec` with `Root`, `Select` =
the `SelectPaths` joined, `Filter = And(source filter, access filter)`,
`OrderBy = query.OrderBy ?? source.OrderBy ?? "Id"` and `Restrictions` = the source restriction plus
`query.Restriction` bound to `RestrictionValues`, composing the source per case: `ByIds` is the
restriction `KeySetRestriction("Id", TVP)` over its `Ids`; `ByQuery` is the filter `Leaf(Filter)`
with the search filter of its `Search` (§5.2.1), bound with its `Arguments`, joined with the
activatable conjunct `Leaf("IsActive = true")` on an activatable stack unless its
`IncludeInactive`, exactly as `query` does (§5.2); `All` has no clause and no `OrderBy`, and
neither it nor `ByIds` joins the conjunct. With no clause and no conjunct the access filter alone
applies.

`Pages(query)` runs one `Read` batch per page of `MaxTake` root rows — `Skip`/`Take` as parameter
slots, the same `ConnectedUser` reconnected per page through the runner so a revocation mid-export
stops the stream: `Rows` on the root spec with `RowQueryOptions.CountCap = query.CountCap` on the
first page (null on the rest) and `CaptureKeys` when `Dependents` is non-empty, then one `Rows` per
dependent on the child root, restricted by `KeySetRestriction("<ParentKey>", "@tb{a}_keys")` of its
owner statement and itself capturing when it has dependents, at any depth (spec 0012 §5.4). A
dependent that reaches `MaxTake` rows is completed by follow-up `Read` round trips over the page's
owner ids, bound as an `IdList` TVP with `Skip` advancing, before the page is yielded.
`ReadAsync(queries)` composes one `Rows` with its `Tvp` per query on one `Read` batch, completing
any result that reaches `MaxTake` the same way. A row is the column values in `SelectPaths` order.
A `RootEntity` that is a lookup target (natural-key resolution) is compiled on that entity under
the caller's `Read` decision for its resource — `Denied` yields no rows, so an unreadable reference
resolves as not found. `Take` caps the total; the exporter's row limit is spec 0019's. A cell value
is the reader's CLR value (`long`, `decimal`, `DateOnly`, `string`, `bool`, `Guid`); formatting is
the codec's.

## 16. Telemetry and the round-trip budget

### 16.1 Instruments

Meter `Tellma.Core` through `IMeterFactory`; names as constants in
`Tellma.Core.Abstractions.Crud.CrudTelemetryNames`:

```csharp
public static class CrudTelemetryNames                  // constants
{
    public const string MeterName = "Tellma.Core";
    public const string Operations = "tellma.crud.operations";
    public const string OperationDuration = "tellma.crud.operation.duration";
    public const string ValidationRounds = "tellma.crud.validation.rounds";
    public const string SaveEntities = "tellma.crud.save.entities";
    public const string ConcurrencyConflicts = "tellma.crud.concurrency.conflicts";
    public const string ConcurrencyOverrides = "tellma.crud.concurrency.overrides";
    public const string EffectFailures = "tellma.crud.effects.failures";
    public const string StaleContextReruns = "tellma.crud.stale_context.reruns";
    public const string QueryDiscoverDuration = "tellma.crud.query.discover.duration";
    public const string Enlistments = "tellma.crud.enlistments";
    public const string OperationTag = "crud.operation";
    public const string OutcomeTag = "crud.outcome";
    public const string SourceTag = "crud.source";
    public const string LaneTag = "crud.lane";
    public const string KindTag = "crud.kind";
    public const string EffectTag = "crud.effect";
    public const string TagTag = "crud.tag";
}
```

| Instrument | Type | Unit | Tags (closed sets) |
|---|---|---|---|
| `tellma.crud.operations` | Counter | `{operation}` | `crud.operation` (`query`, `get`, `get_by_ids`, `all`, `save`, `delete`, `delete_by_query`, `delete_with_descendants`, `action`, `get_by_parent_ids`, `excel_rows`), `crud.outcome` (`ok`, `validation`, `forbidden`, `not_found`, `conflict`, `limit`, `stale`, `unavailable`, `cancelled`, `error`), `crud.source` |
| `tellma.crud.operation.duration` | Histogram | `s` | the same, plus `crud.lane` |
| `tellma.crud.validation.rounds` | Histogram | `{round}` | `crud.operation`, `crud.outcome` (`ok`, `exceeded`) |
| `tellma.crud.save.entities` | Histogram | `{entity}` | `crud.source`, `crud.lane` |
| `tellma.crud.concurrency.conflicts` | Counter | `{conflict}` | `crud.kind` (`stamp`, `missing`, `cycle`) |
| `tellma.crud.concurrency.overrides` | Counter | `{save}` | — |
| `tellma.crud.effects.failures` | Counter | `{failure}` | `crud.effect` |
| `tellma.crud.stale_context.reruns` | Counter | `{rerun}` | `crud.tag` ∈ `permissions`, `settings`, `entity` — the mismatch the prologue or the executor reported — or `guard` for a `50412`, which names none (§5.1) |
| `tellma.crud.query.discover.duration` | Histogram | `s` | — ; the discovery of §4.3 on a cache miss |
| `tellma.crud.enlistments` | Counter | `{enlistment}` | `crud.operation` (the host's: `save`, `delete`, `action`, `api_action`, `job`, `provisioning`), `crud.kind` (`save`, `delete`), `crud.outcome` (`ok`, `validation`, `error`); one per enlisted write (§13.3) |

`crud.lane` is `small` when the operation's payload rows — entities at every depth, or ids — are at
most `DataOptions.LargeBatchThreshold` (spec 0012 §6.1), else `large`.

No entity tag on any `tellma.crud.*` instrument (it multiplies every dimension) and never a tenant
or user tag: the entity is a request-span attribute (spec 0016 §10.1's `tellma.resource` and
`tellma.operation`) and a structured-log field. Logs carry tenant, user, entity, id count, round
count, and the error code. Spec 0012's `tellma.data.roundtrips` observes every operation's round
trips from below through `DataAccessScope`.

### 16.2 The round-trip budget

Warm caches, warm id buffer, no transient failure:

| Operation | Common | One more when |
|---|---|---|
| Query (with count, with ancestors) | **1** | cold connect cache; stale permissions tag (re-run); a denial older than `FastDenyWindow` (the re-check) |
| Details, one or many; get by parent ids; all cached | **1** (all cached: **0**) | same; all cached on a table beyond `MaxRows`: the probe and the re-read of §5.5, **2** on every call |
| Save — creates only, no context loads, no unique property | **1** | cold id buffer; a `[Unique]`/`[NaturalKey]` property in the payload or any validator load (→ 2) |
| Save — any update, or any validator load | **2** | +1 per dependent validation round (max 3 → 4); +1 on a stale `permissions` or `settings` tag the prologue refuses; +2 on a `50412` (a cold re-connect and the recomposed persist) |
| Delete by ids / by query / with descendants | **1** | `ValidateDeleteAsync` overridden (→ 2) |
| Activate / deactivate | **1** | `ValidateActionAsync` overridden (→ 2) |
| Custom action | **2** | +1 per dependent validation round |
| Enlisted write, any host | **+0** | +1 when enlisted after the writer's last dispatch and the target declares a load; an action host whose target declares validator loads: 3, back to 2 with persist-time checks in the target's `ContributeAsync` (§13.3); a standalone `[ApiAction]` or provisioning host: as a save of the target; a job host: 0–1 for validation, the persist rides the completion batch |
| Import, per chunk | **2**, +1 hydration for `Update`/`Upsert`, +1 for every lookup together (spec 0019 §1.4) | as for save |
| Excel export | 1 per page of `MaxTake`, child collections at every depth riding the page | +1 per follow-up read of a collection with more than `MaxTake` rows under one page (§15) |
| Any operation after a transient failure | +1 per retry (up to `DataOptions.RetryAttempts`, 3 by default: four attempts in all) | — |

The budget is asserted by the conformance tests through `DataAccessScope.RoundTrips` (and
`SqlConnection.RetrieveStatistics()["ServerRoundtrips"]` on the fixture) and observed in production
through `tellma.data.roundtrips`.

## 17. Compatibility rules

A platform minor may add hooks (with default bodies), context members, capability interfaces and
annotations, validation codes, request and result members, descriptor members, and instruments. It
may not rename a hook, remove or rename a code, change the path segment kinds or their rendering,
reorder pipeline steps observably, or change a default ceiling. Servers ignore unknown JSON members;
a web client built against a different contract is refused before anything runs (spec 0016 §3.10).
The emitter binds by metadata, so a pack adding a column reorders nothing observable. Hooks take one
context object each, so a minor adds members to a context, never parameters to a hook. A test pins
the public surface of `Tellma.Core.Abstractions.Crud`, `.Validation` and `.Errors` with an API
golden.

## 18. Testing

### 18.1 Unit suite — `test/core/Tellma.Core.Tests` (`Crud/`)

Pure, hermetic, cross-platform; the pipeline runs over a scripted `IDataBatch` double that records
the statements appended and replays canned result sets. Pins: the attribute walker (every
`ValidationAttribute` on roots and children, all failures reported, `IValidatableObject` ignored);
id assignment and temporary-id rewriting across every self-typed FK, `Entity.NotFound` for an
undefined temporary id, ids returned on validation failure and never on a persist attempt; the
ordering rule of §6.4 on an immediate versus a deferred reservation; property ownership (§6.2) per
ownership kind including the multilingual gate and the `Import.LanguageNotConfigured` exception;
child synchronisation semantics (`null`, `[]`, list; foreign ids; nesting overrides the payload
parent key; grandchildren); the path segments and the JSON-policy independence; the parking
scheduler — dedup by structural key, select union, one dispatch per `LoadAsync` round, the
`MaxValidationRounds` bug surfacing, and the property test that validators awaiting foreign tasks
still terminate and never dispatch early; `Search` lowering per `SearchKind` and per language shape;
the final filter composition `And(user, activatable, search, access)` with empty conjuncts dropped;
the navigation-traversal rule; argument discovery, conversion and `BadRequestException`; every
ceiling; every translation row of §14.2; the two `ExecuteActionAsync` overloads (the method's own
`TResult` answered with no read-back, the overload that does not match the action's `ResultType`
refused); a `SingleTarget` action called with other than one id refused; `Persisted` (§11.2) — RT2
sent while the method is parked on it, the method reading its `BatchResult` after it completes; the
enlistment rules of §13.3 over the scripted batch — the pair gate (an undeclared pair, a
non-participant receiver, a closed phase and a self-pair from a pipeline frame refused), a
front-door call from inside a composing frame refused, the group order including the nested
`BeforeHost`-then-`AfterHost` case (B, C, A) and the composite group, `MapPath` composed through the
nesting with `enlistedResource` on every re-rooted error, the double enlistment of one root id
refused, a `StatementOrdinal` attributed to its group; `SaveOptions.OnPersist` invoked once at its
§6.7 position, never on a validation failure, and a stack-owned write from it refused at append;
descriptor derivation for every fixture entity and every startup check of §2.6 (each with a failing
fixture, `core.enlistments` included, a self-pair passing it); the API golden of §17.

### 18.2 Integration suite — `test/core/Tellma.Core.IntegrationTests` (`Crud/`)

`Category=Integration`; LocalDB or Testcontainers (`Testcontainers.MsSql`), one database per class;
the fixture schema is spec 0012's `TellmaFixtureDbContext` in the shared project
`test/shared/Tellma.Testing.Entities` — `fixture.Widgets` (`TopLevelEntity`, `[Temporal]`,
`IActivatable`, `[NaturalKey] Code`, `[Multilingual] Name`, `[WriteOnce] [Unique] Subject`,
`[BlobReference] ImageId`), `fixture.WidgetParts` and `fixture.WidgetPartNotes` (child and
grandchild), `fixture.Nodes` (`ActivatableTreeEntity`, unique `Code`) with `fixture.Links`,
`fixture.Shipments` (`TopLevelEntity<long>`, `IJobEntity`), the full `core.Blobs`, spec 0013's
`fixture.Lookups`, and the real core schema: the context composes spec 0014's access contribution
(spec 0012 §13.2), so `core.Users`, `core.Roles`, `core.Permissions` and `core.UserStamps` are the
production tables, and the suite's callers are seeded fixture users — one per grant shape the pins
need (unrestricted, filtered, denied) — connecting through the ordinary prologue, so the row-level
pins (hidden-equals-missing, the pre-check, the tag re-run, the deny re-check) run as spec 0014's
runner runs them. This spec adds to that context `fixture.Gadgets` (`TopLevelEntity`,
`[Stack(Operations = Read)]`, `[Cacheable]`, `[Searchable] Name`, `[Searchable(Prefix)] Code`) and,
in `Crud/`, the stack registrations: `Widget` through a `WidgetService` with one `[EntityAction]`,
one `[ApiAction]`, a validator that declares dependent loads across three rounds, a
`ContributeAsync` guard in the distribution band and `IEnlists<Shipment>`; `Node`, `Shipment`
(with `Enlist` among its operations) and `Gadget` through the default service; plus an
`IEntityValidator<Widget>`, an `IPersistEffect<Widget>` and an `IDetailsContributor<Widget>`
component and one stack companion with an `[ApiAction]` (§11.3). Pins:
the round-trip budget of §16.2 for every row; hidden-equals-missing on `get`, `get-by-ids`, `save`,
`delete`, `delete-with-descendants`, `activate` and `deactivate`; the two-stage pre-check on save
and, as statements inside the persist batch, on delete, delete-with-descendants and the built-in
actions (hidden id → 404, a readable id outside the action's grant → 403, insert outside the filter
→ 403, edit leaving the filter → 403); concurrency (stamp conflict, override, missing-row conflict
under both modes, `StampRequired`, `ModifiedByName` resolved, delete with stamps, actions stamp);
write-once rejection and server-owned values ignored; `IsActive` created active, ignored on save and
import (a changed value is neither applied nor an error), changed only by `activate`/`deactivate`;
the unique race from two connections (one 422, never a 500) and index backing; the key-load
pre-check reporting a duplicate against a row outside the payload at its path on create and on
update; `Fk.NotFound` under the target's `Read` filter and `Fk.InUse` naming the referencing entity
and property; delete-by-query guards (blank filter, cap, expected-count mismatch inside the
transaction); delete with descendants refusing a partially visible subtree; tree recompute for
ancestors outside the payload, the C# cycle message, the SQL cycle race rolled back; the activate
fast path leaving already-active rows unstamped; a permissions tag bumped between RT1 and RT2
refused and re-run, then `StaleContextException`; a role save blocking behind a member's in-flight
persist (spec 0013's guard, observed here); the commit probe after a killed connection on an insert
and `DependencyUnavailableException` on an update-only batch; validation rounds beyond the limit; a
distribution `THROW 50650` surfacing as `ValidationException` with the code; the enlisted write of
§13.3 from `WidgetService` into `Shipment` — one transaction, a reference in each direction over
the fixture's foreign keys (a `Shipment` row pointing at a `Widget` inserted in the same batch and
a `Widget` pointing at a `BeforeHost` `Shipment`), the target's validators and effect run, its
errors re-rooted at the mapped path with `enlistedResource`, `Concurrency = Check` on a loaded row
refusing a default stamp; a job handler's enlisted update appended to the completion batch and
rolled back with a lost lease; a `SaveOptions.OnPersist` statement committed inside the save's
transaction and rolled back with it when a later statement throws; a caller-channel statement
writing a `Shipment` column from `WidgetService` refused at append; every raw statement's declared
writes covering its targets under the fixture tier's change-tracking audit; the cacheable stack
served with zero round trips after warm-up and refreshed after a write; `MaxRowsPerSave` and a
`[MaxChildren]` cap at depth two; a denial re-checked after `FastDenyWindow` and admitted; an
`[ApiAction]` invoked through `IApiActionInvoker` refused for a caller without its action; the Excel
row source — `WidgetParts` and `WidgetPartNotes` riding each page's one round trip, the first
page's capped count, an empty `ByQuery` in `Id` order, a `ByQuery` excluding inactive rows unless
`IncludeInactive`, a dependent beyond `MaxTake` completed before its page is yielded, `ReadAsync`
answering several lookups in one round trip, an unreadable lookup target yielding nothing, and a
revocation mid-stream; `MetricCollector<T>` over every instrument of §16.1.

### 18.3 The conformance base — `Tellma.Core.Testing`

`StackConformanceTests<TEntity, TKey>` is an abstract xUnit class a distribution closes once per
stack by supplying `NewValidEntity(ordinal)` (and, for tree stacks, `NewChildOf(parent)`). It runs
against the distribution's own database (`Category=Integration`) and asserts, from the stack's
descriptor alone: every declared operation reachable and every absent one refused, an `Enlist`
stack accepting an enlisted write from a declared writer and refusing an undeclared one (§13.3); the
securables of §2.4 registered with the right filter roots; the round-trip budget;
hidden-equals-missing; conflict, override and missing-row conflict; write-once rejection and
server-owned values ignored; `null` versus `[]` collections and foreign child ids for every child
collection; `Unique` for every `[Unique]`/`[NaturalKey]`; `Fk.InUse` for every incoming reference;
the activatable, tree, cacheable and searchable recipes when declared; the instruments. Spec 0018
closes it for `User`, `Role` and `Center`.

### 18.4 CI tiers

The unit suite and the descriptor checks run on every PR; the integration suite and every closed
conformance class run on PR against LocalDB on Windows and Testcontainers on Linux; nothing in this
spec carries `Live=true`.

## 19. Definition of done

- **Projects**: the `.Crud`, `.Validation` and `.Errors` namespaces of
  `src/core/Tellma.Core.Abstractions`, the `Tellma.Core.Crud` namespace of `src/core/Tellma.Core`,
  `StackConformanceTests<,>` in `src/core/Tellma.Core.Testing`, the `Crud/` folders of
  `test/core/Tellma.Core.Tests` and `test/core/Tellma.Core.IntegrationTests`, and the fixture
  additions of §18.2 to spec 0012's `TellmaFixtureDbContext` in
  `test/shared/Tellma.Testing.Entities` — each project with a README stating purpose and usage, XML
  docs on every public member, building and testing on Windows and Linux under the repo's
  warnings-as-errors gates, wired into `Tellma.slnx`.
- **Behavior**: the stack derivation, registration, annotations, ceilings, startup checks and
  securable registration of §2; the service base, pipeline, hooks, components and extension of §3;
  the shapes of §4; the guarded reads of §5; the save pipeline of §6 (steps, ownership,
  preprocessing, ids, child synchronisation, validation, persist assembly, import, read-back); the
  validation framework of §7; the concurrency rule of §8; the row-level rules of §9; the deletes of
  §10; the actions of §11; every recipe of §12; the effect phases of §13; the exception set and
  translation of §14; the row source of §15 — all implemented and pinned by the suites of §18,
  green in CI on both platforms.
- **Observability**: every instrument of §16.1 emitted with its closed tag sets and asserted by
  `MetricCollector<T>`; the span attributes and log fields of §16.1; the round-trip budget of §16.2
  asserted by the conformance base through `DataAccessScope.RoundTrips`.
- **CI**: the tiers of §18.4.
- **Docs**: ARCHITECTURE.md updated where this spec touches it — the capability-interfaces row of
  the data layer (a capability interface, base or annotation is also the single declaration of a
  stack capability — operations, securables, gates, filters — never a paired interface
  per entity); the validation row (keyed context loads are model-emitted SQL, Queryex only for
  user- or permission-authored text, the DataLoader-style `IContextLoader`); the reports row (raw
  SQL only through `IDataBatch.Sql` with declared writes and the analyzer, `SaveChanges` banned, the
  details read stitched inside the platform); and the package-dependency row
  (`Tellma.Core.Abstractions` references `Tellma.Core.Queryex`, shared with spec 0012). Public XML
  docs and error messages reference no `docs/` paths, per repo rule.
- **Not in scope of done**: the endpoint and MCP projection (spec 0016), the Core and GL services
  (spec 0018), the Excel codec (spec 0019), the blob validator and effect (spec 0017), and the job
  and notification statements (specs 0020, 0021) — each lands with its own spec against the
  contracts frozen here.

## Decisions record

The load-bearing decisions, where not already evident above:

1. **Contracts in Abstractions, pipeline in `Tellma.Core`, one Queryex edge** — a module's service
   derives from a base it can reference, and `FilterTree` in contracts beats a mirrored type (§1.1).
2. **A thin base with non-virtual operations over a sealed pipeline, components for multiplicity;
   a factory binds the pipeline to the service once** — what an author expects to write against,
   without the fragile-base-class risk; DI components cover the one case inheritance cannot
   (§3.1, §3.2).
3. **Explicit registration with a startup tripwire** — explicit is what a coding agent gets right;
   the check catches the omission (§2.1, §2.6).
4. **One descriptor for every consumer** — the only way "declared once" holds is that nothing
   re-derives (§2.2, §12).
5. **Ceilings on the stack, never silently clamped** — MCP, import and background callers meet the
   same numbers; clamping breaks paging (§2.5).
6. **Search stays server-side over declared columns** — one declaration beside the columns versus
   every client re-implementing language gating; the plan caches once (§5.2.1).
7. **Details reads mix Queryex and keyed SQL by role** — keyed reads compile to seeks; Queryex is
   used exactly where a user or a permission wrote text (§5.3).
8. **The transaction is the persist text; every write-time invariant is a statement inside it** —
   nothing protects a read inside an open transaction under RCSI, and nothing may hold locks across
   client I/O (§1.2, §6.7).
9. **Ids before validators, from a warm buffer, returned on validation failure** — validators reason
   about final identities; a cold buffer costs one round, never a split round (§6.4).
10. **Ownership from metadata; write-once validated and excluded; derived values reset before the
    hook; `IsActive` server-owned** — one class as storage and wire shape, a derived value the
    client can never smuggle, and the save-bypasses-activate hole closed without a diff gate
    (§6.2, §12.2).
11. **Foreign child ids rejected, never re-parented; no resurrection** — the nesting is the truth
    (§6.5, §8).
12. **A DataLoader over the batch with bounded rounds, structural dedup and one `LoadAsync` per
    round** — many loaders share one round trip; straight-line async is what an author writes
    correctly the first time (§7.2).
13. **Uniqueness validated by key loads and guaranteed by the index; foreign keys by the database
    under the target's `Read` filter** — a C# check alone is write-skew-prone; the JSON and Excel
    paths agree (§7.4).
14. **`ModifiedAt` as the stamp, server-stamped, `Check | Override`, actions stamp without
    checking** — one column already present, one clock per tenant, override that never resurrects
    (§8).
15. **Two-stage pre-check by filtered load, post-check in SQL over `@tb{b}_saved`** — 404 before
    403, and no row leaves the grant that permitted it (§9).
16. **Strict all-or-nothing delete by ids; guarded delete by query, web-only** — a partial delete
    hides a concurrent change; an empty filter is never "delete all" (§10).
17. **One `Activate` securable, a SQL-only fast path, custom actions as `[EntityAction]` methods** —
    the action pipeline is the save pipeline with the load replaced (§11).
18. **Two side-effect phases: transactional participants append statements; post-commit is
    best-effort; durable work is a job row** — no pre-commit non-transactional phase has a correct
    failure story (§13).
19. **A closed exception set mapped by type; codes with arguments, rendered elsewhere** — simpler
    and safer than an interface every exception implements (§14).
20. **The Excel row source is the pipeline's** — the codec never opens a connection and never
    re-implements the read filter (§15).
21. **No entity tag on the `tellma.crud.*` instruments** — cardinality; the entity is a span
    attribute and a log field (§16.1).
22. **Stack companions project `[ApiAction]`s through the descriptor with `HandlerType`** — a
    feature adds operations to every stack without a member on `EntityService` (§2.2, §11.3).
23. **`IPersistEffect` contributing on every persist, deletes included, through one base context
    and one base outcome, each with a kind per operation** — one effect contract covers attach and
    release (a second delete-time contract would be forgotten), and a kind-only member is reached by
    a type test, never read empty on another kind (§13.1, §13.2).
24. **`[ApiAction]` methods run only through `IApiActionInvoker`, `[EntityAction]` methods only
    through `ExecuteActionAsync`, and the analyzer refuses a direct call** — one check per operation
    for every caller, HTTP or not (§11.3).
25. **The entity name is the securable resource** — one qualified name for Queryex, securables, tags
    and tools, so two packs may ship an `Invoice` (§1.3).
26. **Structured validation paths** — spec 0016 and spec 0019 map segments, nobody parses (§7.3).
27. **`Mutation` is declared, never inferred** — the tenant-state verdict and the MCP read-only
    gate read one flag on the descriptor; `Idempotent` is retry safety alone (§2.3).
28. **A cross-stack write is enlisted into the host's frame, never a nested pipeline** — one
    transaction, one authority and one error list per operation; the pair is declared on both
    ends (`Enlist` on the target, `IEnlists<>` on the writer) and gated at compile time, at
    startup and at run time, so no write reaches a stack-owned table around its owner's rules
    (§13.3).
29. **An action descriptor's record type is its kind** — over one shared base,
    `EntityActionDescriptor` and `ApiActionDescriptor` carry only the members their kind has, and a
    member-only action is a null `Securable`, so no consumer reads a member that means nothing for
    the action it holds (§2.2).
30. **Only an open frame persists** — `PersistAsync` is on `IOpenWriteHost`, which an `[ApiAction]`
    method, a job handler or a provisioning step injects; a pipeline frame is an `IWriteHost` alone,
    so no participant can persist a frame that is still composing (§13.3).

## Review flags

1. **Explicit stack registration plus a startup tripwire** (§2.1) versus convention discovery of the
   service class. Explicit costs one generic argument; discovery is one less thing to write and one
   more to explain. Flips if authoring agents repeatedly omit the registration and the tripwire's
   message proves insufficient.
2. **One authoring base class merging operations and hooks** (§3.1) versus a sealed platform service
   plus a separate behavior class. The merged form matches the service names and gives `[ApiAction]`
   methods a home; the split form keeps the author's class free of the operation surface. Flips if
   the operation surface on the subclass is found to invite misuse.
3. **One `Activate` securable for both directions** (§11.1) versus `Activate` and `Deactivate`
   securables. Flips on a real customer needing "may deactivate but not reactivate".
4. **Strict all-or-nothing delete by ids** (§10.1) versus best-effort with a per-id outcome list.
   Flips if bulk-delete UX demands partial success.
5. **Echoing `ModifiedAt` as the expected stamp** (§8) versus a separate `[NotMapped] ExpectedStamp`
   member — explicit semantics, one more property on every entity. Flips if client-side confusion
   between the display value and the stamp string proves common.
6. **Write-once as a validation error** (§6.2), never a silent reset. Flips if imports of stale
   sheets produce more support load than silent resets would.
7. **Early concurrency detection in RT1** (§6.6) kept as a fast-fail before validators spend a
   round; a purist design keeps only the authoritative RT2 check. Flips if the duplicated logic
   drifts.
8. **`CountCap = 10,000`** (§2.5) versus 9,999. Flips on nothing but preference.
9. **Ids from the warm buffer before validation, deferred assignment on a cold buffer, returned on
   validation failure** (§6.4) versus never un-consuming or assigning after validation. Flips if a
   returned id is ever observed attached to a different row (it cannot be, nothing external saw it).
10. **Read-back inside the transaction** (§6.9) versus after `COMMIT` — shorter locks by
    milliseconds, may show a later concurrent change. Flips if lock duration on large saves with
    `ReturnEntities` shows up in the histograms.
11. **Plan lanes by `OPTION (RECOMPILE)` above 1,000 TVP rows** (§2.5, spec 0012) versus a
    size-bucket token yielding a second cached plan; the threshold is to be measured at 50,000 rows.
12. **The one-minute `LastActiveAt` throttle** in the prologue the pipeline rides (§5.1) versus
    stamping every request. Flips if per-minute precision proves too coarse for the activity signal.
13. **Related entities in a details read are not row-level filtered but narrowed to the
    `RelatedSelect` projection** (§5.3, §9.4) versus filtering them and showing blanks. The default
    projection (`Id`, `Name` group, `Code`, avatar columns) is what leaks; flips if a projected
    column is ever sensitive.
14. **`Node` as a platform-configured shadow column with id-path values** (§12.1) versus a
    converter-backed platform property or `ROW_NUMBER` sibling numbering. Flips on a measured
    problem with the shadow-property approach in spec 0012.
15. **No entity tag on the `tellma.crud.*` instruments** (§16.1) versus `crud.entity` as a
    closed-set tag. Flips if per-entity dashboards prove worth the cardinality.
16. **A non-member or deactivated caller gets `404 tenant-not-found`** (§5.1, spec 0014) versus
    401 or 403. Flips if the SPA's session handling needs to distinguish the cases.
17. **Resource names `<schema>.<Entity>` with PascalCase actions and separately derived segments**
    (§1.3, §2.4) versus lowercase verbs doubling as segments. Flips on nothing but taste; the
    projection isolates the choice.
18. **`MaxSaveCount = 10,000`** (§2.5) versus a smaller ceiling with the codec chunking earlier.
    Flips on measured memory of a 10,000-row payload with children.
19. **Server-stamped `ModifiedAt` with an insert-only commit probe** (§8) versus a C#-computed
    `max + 1 tick` stamp that also proves update-only commits. The database clock is one source per
    tenant; the price is `DependencyUnavailableException` on a rare update-only ambiguous failure.
    Flips if that path is observed more than negligibly.
20. **The guarded prologue on every batch including `Persist`, the in-transaction tag re-check under
    shared locks on the rows the batch reads and update locks on the rows it bumps, no stamp-row
    `UPDLOCK`** (§5.1, §6.7) versus closing the window by U-locking the caller's stamp row. Writers
    of a locked tag row wait at most one round trip; other saves wait only while such a bump is
    queued. Flips if permission-write latency under load proves unacceptable.
21. **`IsActive` server-owned** (§12.2) versus write-once (creatable inactive through save and
    import, the actions afterwards) versus a diff-gated editable column. Flips if reference data
    that must arrive inactive becomes a common import need.
22. **Ids assigned after RT1 on a cold buffer, validators starting after it** (§6.4) versus
    validators starting before RT1 with unassigned ids. Costs one round trip on a cold buffer only.
    Flips if cold buffers prove frequent enough to matter.
23. **Foreign keys validated under the target's `Read` filter on the JSON path as well as the Excel
    path** (§7.4) versus Excel-only strictness. Flips if a legitimate workflow assigns references
    the assigning user may not read.
24. **`ExcelRowSource` on the service and `CreateExcelRowSource` on the pipeline** (§15) versus a
    separate factory service. The service is where the read filter and the descriptor already live;
    flips if the codec needs row sources for entities that are not stacks.
25. **Delete by query precedes the delete with a capped `Count` in the same batch** (§10.2) so the
    mismatch names the actual count, versus reading only the SQL assertion. One extra statement,
    no extra round trip. Flips if the count's cost on large filters shows in the histograms.
26. **`MaxRowsPerSave = 100,000`** (§2.5) versus a smaller ceiling. Flips on measured memory of a
    full payload with children at depth two.
