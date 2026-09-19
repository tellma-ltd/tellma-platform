# CRUD service pipeline and capabilities (theme `service-pipeline`, future spec 0014)

Designed for the distribution author who is a coding agent: the entity class plus one line of
composition must yield the whole stack, every capability is declared once and projected everywhere
(columns, securables, service methods, endpoints, MCP tools, default filters, metadata), the common
save costs two round trips, and the escape hatches exist but never sit on the happy path.

The proposal is written for a reader who has not seen the brain dump or the other theme files.
Section 1 is the critique; section 2 the decisions; section 3 the C# contracts (owned and consumed);
section 4 the columns each capability projects; section 5 the answers to the brain dump's questions;
section 6 the cross-cutting seams; section 7 the departures from the architecture document;
section 8 what was verified.

---

## 1. Critique

### 1.1 The general design is right; its cost model is not

The brain dump's shape — a reusable service per top-level entity, a bulk save pipeline of
preprocess → validate → persist → side effects, capability endpoints projected from a few
shared patterns, and an MCP surface that is intent-oriented rather than 1:1 — is the correct
skeleton, and nothing below replaces it. What the draft under-specifies is *where the platform ends
and the distribution begins*, and what each operation costs. Four concrete problems:

1. **The save flow starts the transaction before validation** ("4. Start the transaction (is this
   right?)"). It is not right. Azure SQL runs `READ_COMMITTED_SNAPSHOT` by default, so a read inside
   an open transaction is a statement-level snapshot that protects nothing; on-prem SQL Server it
   takes shared locks that block writers across the validation round trips. Opening the transaction
   before validation buys no consistency and costs lock duration. The transaction is the persist
   batch and nothing else (D8), and the two invariants that a read-then-write race can break —
   uniqueness and the concurrency stamp — are enforced inside that batch by the unique index and
   the stamp predicate, not by the earlier read.

2. **Four DB calls for a save, two of them avoidable.** The draft counts connect (#1), RLS pre-check
   (#2), validation context (#3), persist (#4). Connect rides the first batch as a guard statement
   that aborts the batch when the cached permissions tag is stale; the RLS pre-check *is* the
   "load the existing rows" statement every save needs anyway (a row the user cannot see under the
   save filter is simply absent from that result set). Common case: **two round trips for a save,
   one for everything else** (D6, D22).

3. **"Pre-commit non-transactional side effects" (step 10) should not exist.** Its only instance
   is blob creation, and staged uploads (upload first through the blob endpoint, save the record
   with the token) move the blob write before the pipeline and the blob delete after commit. The
   pipeline then has exactly two side-effect hooks: statements that ride the persist batch
   (transactional) and post-commit work (D10). Removing the hook removes the hardest-to-reason-about
   step in the draft.

4. **The draft never says what the distribution author writes.** "Plumbing lives in the platform"
   is the goal; the draft's `UserService`/`RoleService`/`CenterService` reads as three hand-written
   classes sharing a base. Under this proposal there is **no `CenterService` class at all**: the
   platform's `EntityService<Center, int>` is the service, the GL module's `Center` entity declares
   `IActivatable` and `ITreeEntity<int>`, and an optional `CenterBehavior` carries the one business
   rule (only grouping centers may have children). The worked recipe in D1 counts the lines.

### 1.2 Detailed choices

- **`toggle_activate` as a permission action** — the action is named `activate`; one securable
  `(Center, activate)` with filter support covers both directions. Nobody grants "deactivate but
  not activate" (D13).
- **"Save admits a single entity"** contradicts the guiding principle that save endpoints accept
  arrays and the sentence two lines later that import reuses the same bulk pipeline. Save takes an
  array everywhere; the details page sends an array of one (D2).
- **`SavedById` on weak entities** — redundant. A child is saved with its parent; the parent's four
  audit columns describe the aggregate. Weak entities carry `Id`, the parent key, and their own
  columns only (D11, seam 17).
- **Audit vocabulary** — the draft mixes `SavedAt/SavedById` + period columns (temporal entities)
  with four audit columns (non-temporal). One vocabulary: `CreatedAt/CreatedById/ModifiedAt/
  ModifiedById` on every top-level entity, carried by the `Entity<TKey>` base so no author types
  them; system versioning is an additive table option, never a different base class (D11).
- **`Center.IsLeaf` and `Level`** — derivable (`SubtreeCount = 1`; `Node.GetLevel()`); neither is
  stored. `level()` becomes a Queryex function emitting `GetLevel()` (D15, seam 4).
- **`ExportByQueryForImport` must "make a good guess" at natural keys** — the guess rule belongs to
  the entity contract (`[NaturalKey]` with the documented inference order) so the Excel codec, the
  MCP `describe` tool and the UI picker all agree (seam 7).
- **"If the user requests 5 ids and 4 are found"** — the bulk read returns the four plus the
  missing ids; the single read is the 404 (D4). A row hidden by row-level security is reported
  exactly like a row that does not exist, as the draft's access-control rule requires.
- **"Should the Search parameter be kept?"** — yes, driven by the entity's searchable columns
  (convention: `Name`, `Name2`, `Name3`, `Code`); no picker-versus-page hint, because the select
  list already tells the UI what to show and the filter is the same either way (D5).
- **"Does the emitter need to know upsert vs synchronize?"** — the persistence layer does not
  decide it; the pipeline hands the emitter a `SaveSpec` whose `Mode` is `Upsert`, `InsertOnly` or
  `UpdateOnly` for top-level rows and always `Synchronize` for children under their parents (D18).
- **"Accessing a record I have no read permission on returns the same as non-existent"** — the
  draft applies this to reads. The proposal applies it to every id-addressed operation: the
  pre-check inside the batch throws the same not-found for a hidden row and for a missing one.
  Type-level absence of permission (no grant at all on the resource) is a 403, because the resource
  name is public metadata (D9).

### 1.3 Gaps the draft does not cover

- **Temporary ids on the wire.** Import must reference "a parent created in the same import" and
  the UI must reference a new sibling; nothing in the draft says how. Rule: `Id <= 0` is new; a
  negative value is a batch-local temporary id that any foreign key targeting the same entity may
  reference; the pipeline rewrites every such reference after allocation (D6, step 5).
- **Who assigns ids, and when.** The service layer, in the first round: the id reservation rides the
  context round, and entities carry real ids before any validator runs, so validators and children
  see final keys. Ids consumed by a save that later fails are not un-consumed (gaps are cheaper than
  the bookkeeping; the ranges are small) (D6).
- **Limits.** The draft asks for payload limits under the web layer only. The service layer owns
  `MaxTake` (500), `CountCap` (10 000), `MaxSaveCount` (5 000), `MaxExpandDepth` (3) and
  `MaxValidationRounds` (3) as per-entity, attribute-overridable ceilings so the MCP and background
  paths enforce the same numbers as HTTP (D3).
- **Custom actions.** The draft lists Activate/Deactivate, Invite, and "actions" in the MCP goal but
  offers no mechanism. `[EntityAction]` on a behavior method is the one mechanism: it projects the
  securable, the service call, the endpoint, the MCP tool and the UI button (D12).
- **Bespoke read criteria** ("a document may be visible to any user it is assigned to") — the
  behavior contributes a `FilterTree` that the evaluator OR-s with the permission filters (D9).
- **What "public top-level entity" means** — every registered stack is exposed; weak entities are
  never stacks; the operation set is a flag enum per stack (read-only lookups yield no write routes)
  (D23).
- **Concurrency for children** — a child row has no stamp; the parent's `ModifiedAt` covers the
  aggregate (D11).
- **The extensibility note "validate that the interface matches the DB"** — unnecessary: the class
  *is* the table; a distribution leaf inheriting a pack default cannot drop a pack column.

### 1.4 Internal inconsistencies

- The save flow's step 2 rejects a caller with no write permission, but step 8 (RLS post-check)
  also produces "forbidden" — after the transaction has already rolled back a write the user was
  allowed to attempt. Both are kept, with distinct codes: type-level forbidden before any DB call;
  post-check forbidden with a message that names what happened ("this change would move the record
  outside your permissions") (D9).
- "Queuing background operations IS transactional, and therefore not handled here" (step 12) and
  "Notifying a user … rides on the same train as the save" (Inbox) describe the same hook; the draft
  has no place for it. `PersistContext.Notify(...)` is that place (D10, seam 15).
- The MCP goal wants "every Tellma action a user performs regularly" exposed, while the service
  layer section exposes only CRUD. The `[EntityAction]` registry closes the gap: the MCP tool list is
  generated from the same descriptors the endpoints are (D12, D23).

---

## 2. Decisions

### D1 — Composition: a platform-owned generic service, an author-owned behavior, capability interfaces on the entity

**Decision.** Three parts, each with one job:

1. `EntityService<TEntity, TKey>` (in `Tellma.Core`, `sealed`) implements
   `IEntityService<TEntity, TKey>` and runs every standard operation. Distributions and modules never
   subclass it and never register it; the composition root does, once per stack.
2. `EntityBehavior<TEntity, TKey>` (in `Tellma.Core.Abstractions`, `abstract`, all members virtual
   with empty defaults) is what an author writes when an entity needs business logic: preprocessing,
   save and delete validation, statements riding the persist batch, post-commit work, details
   extras, a custom search filter, bespoke read criteria, and `[EntityAction]` methods. It is
   discovered by convention (the one class deriving from `EntityBehavior<TEntity, …>` in the
   composition's assemblies) and overridable at the selection site. A stack with no behavior class
   is the common case.
3. **Capability interfaces on the entity class** (`IActivatable`, `ITreeEntity<TKey>`, the
   `IAudited` members carried by `Entity<TKey>`) are the only declaration of a capability. The
   platform's `CapabilityRegistry` projects each one into columns (via the properties the interface
   requires), securables, service methods, endpoints, MCP tools, default filters, and metadata.

Pack logic is reused with an extended entity by closing an **open generic behavior over the leaf
type**: `Tellma.Core` ships `UserBehavior<TUser> where TUser : User`, the reference distribution
registers `t.Users<User>()` with the pack default or `t.Users<MyUser>()` with its own leaf, and a
distribution that adds a rule subclasses `UserBehavior<MyUser>`. Pack code is written against the
pack's own base class, never against a paired interface.

**The worked recipe** ("how many lines does a distro write to add an entity with IsActive and a
tree?"):

```csharp
// Tellma.Module.Gl.Abstractions/Entities/Center.cs — the pack default; a distro leaf would be
// `public sealed class Center : Tellma.Module.Gl.Abstractions.Entities.Center { … }`
[Table("Centers", Schema = "gl")]
[Description("A responsibility center: the organizational unit that costs and revenues are attributed to.")]
public class Center : Entity<int>, IActivatable, ITreeEntity<int>
{
    [Required, MaxLength(255)] public string Name { get; set; } = null!;
    [MaxLength(255)] public string? Name2 { get; set; }
    [MaxLength(255)] public string? Name3 { get; set; }
    [Required, MaxLength(50), NaturalKey] public string Code { get; set; } = null!;
    public CenterType CenterType { get; set; }
    public bool IsActive { get; set; } = true;
    public int? ParentId { get; set; }
    public Center? Parent { get; set; }
    public int SubtreeCount { get; set; }
    public int ActiveSubtreeCount { get; set; }
}

public enum CenterType { Abstract, BusinessUnit, Service, Operation, Sale }
```

```csharp
// composition — the GL feature declares it; a distro-only entity is the same one line
declaration.Entity<Center>();
```

Fourteen lines of entity, one line of composition, zero lines of service, endpoint, permission or
MCP code. What the author gets: query (with search, paging, capped count, ancestors), details (with
related dictionary and row echo), save (array, upsert + child synchronize, audit stamping,
concurrency, id allocation, tree recompute, cycle validation), delete by ids / by query / with
descendants, activate / deactivate, get by parent ids, the four securables
`(Center, read|save|delete|activate)`, eleven endpoints, the MCP descriptors, the default filter
`IsActive = true`, and a conformance test base class (D24). The one GL rule — only `Abstract` and
`BusinessUnit` centers may have children — is a ten-line `CenterBehavior` (example under D7).

**Rationale.** Inheritance from a fat base service makes every distribution class a subclass of a
platform type whose protected surface becomes public API by accident; composition (a behavior
*plugged into* the platform service) keeps the pipeline's evolution private and the author's surface
tiny and mechanical. The behavior itself is still a class with virtual methods because that is the
most mechanical thing for a coding agent to write and the easiest to diff against the pack's
behavior it overrides. Generic services closed over leaf types satisfy the architecture's "no
paired interface per entity" rule and the brain dump's "reuse service logic with an extended entity"
at once.

**Alternatives rejected.** (a) A base class `CrudService<T>` that distros subclass per entity —
every entity then has a class, and platform changes to the base leak into every subclass. (b) Pure
delegates registered at composition (`t.Entity<Center>(e => e.OnValidate(...))`) — fine for one
rule, unreadable at five, and not discoverable by convention. (c) Interfaces per entity
(`ICenter`) so services code against interfaces — rejected by the architecture, and unnecessary once
pack behaviors are generic over the leaf.

**Confidence.** High. **Review flag.** The name `EntityBehavior` (alternatives: `EntityRules`,
`EntityHooks`, `EntityLogic`); the choice is cosmetic but the name appears in every distribution.

### D2 — The standard operations and their signatures

**Decision.** `IEntityService<TEntity, TKey>` (contracts in §3.1) exposes exactly:

| Operation | Signature (abridged) | Powers |
|---|---|---|
| Query | `QueryAsync(EntityQuery) → QueryResult` | search page, reports, pickers, export by query |
| Details (one) | `GetByIdAsync(TKey, DetailsRequest) → DetailsResult<TEntity>`; throws `NotFoundException` | details page |
| Details (many) | `GetByIdsAsync(IReadOnlyList<TKey>, DetailsRequest) → DetailsSetResult<TEntity,TKey>` with `Missing` | export by ids, import hydration, MCP get |
| Save | `SaveAsync(IReadOnlyList<TEntity>, SaveOptions) → SaveResult<TEntity,TKey>` | details page (array of one), import, seeding, actions |
| Delete by ids | `DeleteByIdsAsync(IReadOnlyList<TKey>) → DeleteResult` | search page multi-select, details page |
| Delete by query | `DeleteByQueryAsync(FilterTree, IReadOnlyList<QueryArgument>) → DeleteResult` | the hidden "delete by filter" |
| Action | `ExecuteActionAsync(string action, IReadOnlyList<TKey>, object? arguments, ActionOptions) → ActionResult<TEntity,TKey>` | activate/deactivate, custom actions, MCP `actions.run` |

`ITreeEntityService<TEntity, TKey> : IEntityService<TEntity, TKey>` adds
`GetByParentIdsAsync(IReadOnlyList<TKey?> parentIds, EntityQuery projection) → QueryResult` (a `null`
parent means the roots; one call loads roots plus every expanded node) and
`DeleteWithDescendantsAsync(IReadOnlyList<TKey>) → DeleteResult`. Typed convenience extension
methods (`ActivateAsync`, `DeactivateAsync`) are constrained `where TEntity : IActivatable`, so a
call on a non-activatable entity is a compile error rather than a runtime `NotSupported`.
`IEntityService<TEntity>` and `EntityBehavior<TEntity>` are the `int`-keyed aliases every
distribution actually names.

Excel (`ExportBy…`, `Import`) is not on this interface: the codec is a consumer that composes
`QueryAsync`, `GetByIdsAsync` and `SaveAsync` (D18).

**Rationale.** One interface per key type, capability sub-interfaces only where the operation set
genuinely differs (tree), extension methods where only a constraint differs (activatable). Every
operation is bulk-shaped; the single-entity read exists because a details page has a single-id
semantics (404) that a bulk read cannot express.

**Alternatives rejected.** Separate `IActivatableEntityService` — an interface whose only content
is two calls that `ExecuteActionAsync` already covers. A single `ExecuteAsync(Operation)` dispatcher
— erases the type information the web projection and MCP need.

**Confidence.** High.

### D3 — Request and result shapes, and the service-level ceilings

**Decision.** The request/result records (§3.2) are service-level types: `EntityQuery` mirrors
`QuerySpec` plus `Search`, `IncludeCount`, `IncludeAncestors`, and typed `Arguments`
(`QueryArgument(Name, QueryexType Type, object? Value)`, the declaration's `IsNotNull` being
`Value is not null`); `QueryResult` carries `Columns` (the engine's `QueryexColumn`s), `Rows` as
`object?[][]`, `Count` with `CountIsCapped`, and `Ancestors` in the same column shape as `Rows`;
`DetailsRequest` carries `Expand` (navigation paths), `Extras` (names the behavior understands) and
`EchoSelect` (the search page's select for the row echo); `DetailsResult<TEntity>` carries the
entity with its child collections populated, the `RelatedEntities` dictionary (logical entity name →
id → entity), `Extras`, and the `RowEcho`. `SaveOptions` carries `Mode` (`Upsert` default,
`InsertOnly`, `UpdateOnly`), `ReturnEntities` (default true), `Details`, `OverrideConcurrency`,
`Source` (`Api`, `Import`, `Seed`, `Action`). Wire encodings are the web spec's concern.

Ceilings live on the stack, defaulted by the platform and overridable per entity through
`[Stack(...)]`: `MaxTake = 500`, `CountCap = 10_000`, `MaxSaveCount = 5_000`, `MaxExpandDepth = 3`,
`MaxValidationRounds = 3`, `MaxActionIds = 5_000`. Exceeding one throws `LimitExceededException`
before any DB call. `Select` absent means the entity's default columns (natural key, `Name*`,
`Code`, `IsActive`, audit columns) — the shape pickers and MCP `query` use when they pass nothing.

**Rationale.** Typed arguments avoid running parameter inference on the hot path (inference is an
authoring aid); ceilings on the service layer rather than the web layer are what make the MCP and
background paths enforce the same numbers. `CountIsCapped` lets the UI render "10 000+" honestly.

**Confidence.** High. **Review flag.** `CountCap = 10_000` versus the brain dump's 9 999; pick one.

### D4 — Partial results for bulk reads, 404 for single reads

**Decision.** `GetByIdsAsync` returns the entities it found under the caller's read filter and the
list of ids it did not (`Missing`); it never throws for absence. `GetByIdAsync` throws
`NotFoundException` when the row is missing or hidden. Hidden and missing are indistinguishable on
both paths.

**Rationale.** The bulk read serves export-by-ids, import hydration and agents, all of which want
"what exists" without a round trip per id; the single read serves a page whose only honest answer for
a missing record is not-found. Reporting hidden rows as missing is the access-control rule of the
brain dump applied uniformly.

**Alternatives rejected.** 404 on any missing id in a bulk read (the export of a multi-select that
raced a delete would fail entirely). **Confidence.** High. **Review flag.** Yes — an equally
plausible design returns 404 for the bulk read when *all* ids are missing.

### D5 — `Search` stays, driven by declared searchable columns

**Decision.** `EntityQuery.Search` is kept. The platform translates it into a `FilterTree`
disjunction over the entity's searchable properties:
`Or(contains(Name, @s), contains(Name2, @s), contains(Name3, @s), contains(Code, @s))`, plus
`Id = @n` when the text parses as an integer and the key is numeric, plus an exact match on the
natural key when it is not one of the above. Searchable properties are declared with
`[Searchable]` on the entity; when none is declared the convention is the properties named `Name`,
`Name2`, `Name3`, `Code` that exist. `Name2`/`Name3` leaves are omitted when the tenant has no
second/third language (the Queryex schema no longer declares them). The final filter is
`And(userFilter, searchFilter, rlsFilter)`. A behavior overrides `SearchFilter(string)` for
anything else (a document searched by its counterparty's name, say). There is no picker-versus-page
hint.

**Rationale.** One line of declaration (often zero) versus every client — the SPA, every MCP host,
every script — re-implementing the same disjunction and getting `Name2` gating wrong. `contains` is
`CHARINDEX`-based and scans; that is the accepted cost for master data, and a behavior can swap in
`startsWith` or a dedicated path when a table earns it.

**Alternatives rejected.** Client-built filters (duplicated logic, leaks language gating to
clients); a picker hint (the select list already says what the picker shows). **Confidence.** High.

### D6 — The save pipeline, step by step

**Decision.** `SaveAsync` runs these steps in order; steps marked ⟲ are one DB round trip.

1. **Ceilings and shape.** Count ≤ `MaxSaveCount`; every entity and child is validated with the BCL
   `Validator.TryValidateObject(…, validateAllProperties: true)` (DataAnnotations: `[Required]`,
   `[MaxLength]`, `[Range]`, `[EmailAddress]`, plus `IValidatableObject` when present). Any shape
   error ends the call with a `ValidationException` (422) and **zero DB calls**; custom validators
   therefore never see a shape-invalid entity. Ids within the batch must be unique per entity type;
   duplicates are a shape error.
2. **Authorization.** `IPermissionEvaluator.EvaluateAsync(resource, "save")`. Not allowed →
   `ForbiddenException` (403). The decision carries the save filter used by steps 4 and 8.
3. **Preprocess.** Platform: trim strings (`[NoTrim]` opts a property out), empty strings become
   null on nullable properties, server-owned properties are cleared to "unknown" so no client value
   survives, child rows receive their parent key. Then `EntityBehavior.Preprocess`.
4. ⟲ **Context round A** (one batch): the connect guard (seam 16); the reservation of one id range
   per entity type with new rows (`IIdAllocator` decides whether it needs the round trip at all —
   a warm buffer skips the statement); for every update id, the **existing row image** compiled as
   `QuerySpec { Root, Select = <all columns>, Restriction = In(updateIds), Filter = saveFilter }`
   — this is the RLS pre-check, the write-once source and the concurrency early-warning in one
   statement; the tree capability's ancestor load for the new parents (D15); and every load the
   behavior's `ValidateSaveAsync` declared before its first await (D7). A round A whose only content
   would be the connect guard is skipped when nothing needs it.
5. **Assign ids.** New rows (`Id <= 0`) receive ids from the reserved range in payload order;
   negative temporary ids are rewritten wherever a foreign-key property targeting the same entity
   type holds them (`ParentId`, children's parent key, any FK the model metadata reports). Existing
   ids that round A did not return → `NotFoundException` (the hidden-or-missing rule). A write-once
   property whose value differs from the existing row → validation error `Core.Errors.WriteOnce`.
   `ModifiedAt` differing from the existing row and `OverrideConcurrency` false →
   `ConcurrencyException` now (the authoritative check is step 8).
6. **Validate.** Platform rules (uniqueness *within the batch* on natural keys, tree cycles, parent
   existence for children), then the behavior's continuations run to completion; further rounds
   ⟲ as needed, bounded and metered (D7). Any error → `ValidationException` with every error
   collected.
7. **Persist statements are assembled** (D8): stamp check, emitter statements, capability
   statements (tree recompute), behavior statements (`ContributeToPersist`), tag bumps, inbox
   inserts, RLS post-check, commit, read-back.
8. ⟲ **Persist round B** executes the batch. SQL error numbers map to platform exceptions
   (D19): 50004 → `ConcurrencyException` (conflicting ids and `ModifiedById` in the preceding
   result set), 50003 → `ForbiddenException(Core.Errors.RowSecurityAfterSave)`, 2601/2627 →
   `ValidationException` on the property the unique index names, 547 →
   `ValidationException(Core.Errors.InvalidReference)`. A transient error (deadlock, throttling)
   re-runs the whole batch (the executor's job; the batch is one transaction so nothing partial
   survives).
9. **Post-commit.** `EntityBehavior.AfterCommitAsync` (blob deletes, third-party calls, anything
   that cannot roll back); the SignalR nudge for inbox items inserted in step 7. Failures here are
   logged and metered, never surfaced as a save failure — the save committed.
10. **Return** `SaveResult` with the ids and, when `ReturnEntities`, the details read back in the
    same round B after `COMMIT`.

**Rationale.** Every step is placed where its input is first available and where its failure is
cheapest. Shape validation before any DB call, authorization before any DB call, one context round
that serves five purposes, and one persist round that serves ten. The design pushes every
correctness check that a read-then-write race could defeat into the persist batch.

**Alternatives rejected.** Loading existing rows lazily only when a validator asks (then the RLS
pre-check and write-once need their own statement anyway); running custom validators even when shape
validation failed (validators then defend against nulls the attributes already forbid).

**Confidence.** High on the shape; medium on skipping round A when only the connect guard remains
(the guard then rides round B, which is also where it must be re-asserted — see seam 16).

### D7 — The validation framework: straight-line async validators over one batched, deduplicated round

**Decision.** A behavior's validator is ordinary async code against `SaveValidation<TEntity, TKey>`:

```csharp
public sealed class CenterBehavior : EntityBehavior<Center>
{
    protected override async Task ValidateSaveAsync(SaveValidation<Center, int> v)
    {
        // Parents referenced by this batch that already exist: one TVP-restricted load, batched with
        // every other load declared before the first await, deduplicated by (entity, key, select).
        var parents = await v.LoadByKeyAsync<Center, int>(c => c.Id,
            v.Entities.Where(c => c.ParentId > 0).Select(c => c.ParentId!.Value), select: "Id,CenterType");

        foreach (var c in v.Entities)
        {
            var parent = c.ParentId is int id
                ? (parents.GetValueOrDefault(id) ?? v.Entities.FirstOrDefault(e => e.Id == id))
                : null;
            if (parent is not null && parent.CenterType is not (CenterType.Abstract or CenterType.BusinessUnit))
            {
                v.Error(c, x => x.ParentId, GlErrors.ParentMustBeGrouping, parent.Code);
            }
        }
    }
}
```

Mechanics: the platform starts every validator as a task; each `Load…` call registers a request
and returns a task that completes when the round executes. When every validator task is either
complete or waiting on a load, the pipeline dispatches **one batch** containing all pending loads —
deduplicated by structural key (entity, key property, select; key values are unioned into one TVP;
requests differing only in `select` are merged by taking the union of the selects) — then resumes
the validators. A validator that awaits something other than a load is simply awaited (the
dispatcher waits on `Task.WhenAny`), so nothing deadlocks; it just cannot be batched. Rounds repeat
while validators keep declaring loads, up to `MaxValidationRounds` (3); exceeding it throws
`InvalidOperationException` naming the behavior — an authoring bug, not user input. The round count
is recorded on `tellma.crud.validation.rounds`.

Loads available: `LoadByKeyAsync<TOther, TK>(key, keys, select)` (TVP restriction on a unique
property; the result is a dictionary), `LoadEntitiesAsync<TOther>(filter, arguments, select)`
(materialized entities), `LoadRowsAsync<TRow>(QuerySpec, arguments, materializer)` (raw Queryex
rows), `LoadSqlAsync<TRow>(SqlStatement, materializer)` (the raw-SQL escape hatch),
`LoadExistingChildrenAsync<TChild>()` (the pre-save children of the update rows, for before/after
rules). `Existing(entity)` is the top-level row image round A always loads, primed into the loader
cache at no cost. The first validation round *is* round A of D6, so the common validator adds no
round trip.

Errors: `v.Error(entity, x => x.Code, code, args)` produces `ValidationError(Path, Code, Arguments,
Message)` where `Path` is `[i].Code` or `[i].Lines[j].Quantity` (index of the entity in the request,
CLR property names; the web layer rewrites names to its JSON policy), `Code` is a resource key
(`Core.Errors.Duplicate`, `Gl.Errors.ParentMustBeGrouping`), `Arguments` feed ICU MessageFormat and
`Message` is localized at throw time in the request culture. Every error is collected; nothing stops
at the first.

Uniqueness is validated in C# **only for a good message and for intra-batch duplicates**; the unique
index is the guarantee, and 2601/2627 at persist are translated to the same field error via the
model's unique-index → property mapping. No validation read takes `UPDLOCK, HOLDLOCK`.

**Rationale.** The DataLoader contract (batch per tick, dedup by key, explicit dispatch, missing
keys are explicit absences) is exactly the brain dump's ask, and putting it *on top of* the batch
abstraction folds every validator's I/O into one round trip — which neither FluentValidation nor
GreenDonut does. Straight-line async code is what a coding agent writes correctly on the first try;
a two-phase "declare then run" API is what it gets subtly wrong.

**Alternatives rejected.** FluentValidation (no batching, a second rule language); ASP.NET's
built-in validation as the engine (endpoint filter, sync, first-level-only, no context); a declare
phase with explicit handles (`var h = v.Request(...); await v.Round(); h.Value`) — correct but
twice the code and easy to misorder.

**Confidence.** High on the contract; medium on the "dispatch when all tasks are blocked" scheduler
(it needs a careful implementation and a property test that validators awaiting foreign tasks
still terminate).

### D8 — The transaction is the persist batch, expressed in T-SQL inside the batch text

**Decision.** Nothing before round B runs in a transaction. Round B is one command whose text is:

```sql
-- connect guard (seam 16): declares @tm_UserId, @tm_Now; THROWs 50001/50002 before anything else
SET XACT_ABORT ON;
BEGIN TRAN;
-- 1. concurrency stamp check (D11)
-- 2. emitter statements per table, parents before children: UPDATE … ; INSERT … ; DELETE children … (spec 0011)
-- 3. capability statements: tree recompute (D15)
-- 4. behavior statements: EntityBehavior.ContributeToPersist (raw SQL or compiled queries with Writes declared)
-- 5. cache-tag bumps, derived from the tables every statement above declared it writes (spec 0012)
-- 6. inbox inserts (spec 0019)
-- 7. RLS post-check: THROW 50003 when a saved row no longer satisfies the save filter (D9)
COMMIT;
-- 8. read-back: the details statements for the saved ids (D16), outside the transaction
```

`SET XACT_ABORT ON` makes any run-time error — a unique violation, a THROW, a deadlock — roll the
whole transaction back on the server before the client hears about it. No `SqlTransaction`,
no `TransactionScope`. The executor re-runs the entire batch on a transient error; because
app-assigned ids make a replayed insert collide on the primary key instead of duplicating, the one
ambiguous case (connection lost during commit) is settled by a `SELECT Id, ModifiedAt … WHERE Id IN
@ids` probe that compares the stamp the batch wrote.

The escape hatch: a behavior that must read inside the transaction and decide in C# sets
`PersistScope.ClientTransaction` in `ContributeToPersist`; the pipeline then splits round B into a
`SqlTransaction`-controlled pair on one connection (persist without commit → behavior callback →
commit). It costs one round trip and is metered (`crud.persist.scope = client`), so its use is
visible.

**Rationale.** Under RCSI a transaction opened before validation protects nothing and on-prem it
blocks writers; the stamp predicate and the unique index protect the two invariants that matter
inside the write itself. One transactional batch also keeps the driver's built-in retry irrelevant
(it is inert inside any transaction anyway) and lets the whole save be a single span.

**Alternatives rejected.** `TransactionScope` (Serializable by default, async flow off, DTC
promotion throws on Linux); a client `SqlTransaction` around two commands as the default (one extra
round trip on every save for a case one behavior in fifty needs).

**Confidence.** High.

### D9 — Row-level security: pre-check by loading, post-check by compiled query inside the batch

**Decision.** Three rules:

- **Type level.** No grant on `(resource, action)` → `ForbiddenException` before any DB call. The
  resource name is public (it is in the schema metadata), so this leaks nothing.
- **Row level, before the write.** Every id-addressed operation restricts its target rows by the
  action's filter. For save, that is the existing-row load of round A (missing → `NotFoundException`,
  identical for hidden and absent rows). For delete and actions, it is a statement inside the batch:

  ```sql
  DECLARE @tm_vis TABLE ([Id] int PRIMARY KEY);
  -- compiled by Queryex with Select = "Id", Restriction = In(@tm_ids), Filter = <action filter>,
  -- Sink = KeyTable("@tm_vis") (seam 4):
  DECLARE @qx1_v0 …; INSERT INTO @tm_vis ([Id]) SELECT [T].[Id] FROM [gl].[Centers] AS [T] WHERE … ;
  IF (SELECT COUNT(*) FROM @tm_vis) <> @tm_expected THROW 50006, N'not-found', 1;
  ```

  and the operation's own statement targets `@tm_vis`.
- **Row level, after the write** (save only, and actions that opt in with
  `[EntityAction(PostCheck = true)]`). The same compiled shape with the saved ids and the save
  filter; a count mismatch throws 50003 and the transaction rolls back →
  `ForbiddenException(Core.Errors.RowSecurityAfterSave)` with a message that says the change would
  move the record outside the caller's permissions. Actions default to no post-check because an
  action commonly moves a row out of the filter that permitted it (`post` on `State = 'Draft'`).

**Bespoke criteria.** `EntityBehavior.BespokeCriteria(action)` returns a `FilterTree` (or null)
that the pipeline hands to the evaluator to OR with the permission filters — "assigned to me" is
`Leaf("AssigneeId = me()")`. The behavior never composes the final filter itself.

**Rationale.** The filter compiles once per (entity, permission-set shape) and every per-user value
is a parameter; keys ride a TVP; the pre-check for reads costs nothing extra because it *is* the
load; the post-check is the only place a privilege-escalating write can be caught, and it runs inside
the transaction that would commit it.

**Alternatives rejected.** SQL Server security policies (logic in the database; cannot hold
user-authored Queryex; schema binding blocks expand/contract migrations; history tables unprotected;
dbo filtered too). Post-check in C# after commit (too late).

**Confidence.** High.

### D10 — Side effects: two hooks, no pre-commit non-transactional step

**Decision.** `EntityBehavior` has exactly two side-effect hooks:

- `ContributeToPersist(PersistContext)` — runs during step 7 with the final ids; adds statements
  to the transactional segment (`context.Batch.AddSql(statement, writes: ["gl.Centers"])` or
  compiled queries), records inbox notifications (`context.Notify(new InboxDraft(...))`, seam 15),
  and can enqueue outbox emails through the same batch once the outbox spec ships. Everything here
  commits or rolls back with the save.
- `AfterCommitAsync(CommittedContext)` — runs after `COMMIT` with the saved entities; blob deletes
  for replaced images (seam 12), third-party calls, cache warming. Exceptions are logged with the
  operation's trace id and counted on `tellma.crud.after_commit.failures`; they never fail the save.

Blob *writes* precede the pipeline (staged upload, token on the record); the pipeline's only blob
duty is the confirmation statement the blob capability contributes in step 7 and the delete in
`AfterCommitAsync`.

**Rationale.** A hook that runs non-transactional work before commit has no correct failure story
(the blob exists, the row does not, or vice versa); staging removes the need for it entirely.
Two hooks with clear guarantees are what an agent can reason about.

**Alternatives rejected.** Multipart save (a second endpoint shape for every entity with a file;
MCP and import clients must build multipart bodies). **Confidence.** High.

### D11 — Concurrency: `ModifiedAt` is the token, the override flag re-runs the save

**Decision.** Every top-level entity carries `CreatedAt`, `CreatedById`, `ModifiedAt`,
`ModifiedById` (on `Entity<TKey>`; `datetime2(7)` UTC, stamped from the batch's `@tm_Now =
SYSUTCDATETIME()` and `@tm_UserId`). `ModifiedAt` is the concurrency token: only user-visible
mutations (save, actions, import) bump it; bookkeeping columns (activity stamps, inbox tracking,
task leases) live in sibling tables and never touch it. The client echoes `ModifiedAt` back; the
persist batch's first statement is

```sql
IF @tm_override = 0 AND EXISTS (SELECT 1 FROM [gl].[Centers] t JOIN @tm_rows s ON s.[Id] = t.[Id] WHERE t.[ModifiedAt] <> s.[ModifiedAt])
BEGIN
    SELECT t.[Id], t.[ModifiedAt], t.[ModifiedById] FROM [gl].[Centers] t JOIN @tm_rows s ON s.[Id] = t.[Id] WHERE t.[ModifiedAt] <> s.[ModifiedAt];
    THROW 50004, N'concurrency', 1;
END
```

which reaches the client as `ConcurrencyException` carrying the conflicting ids and who modified
them; the UI offers "overwrite", which resends with `SaveOptions.OverrideConcurrency = true`. Child
rows carry no token; the parent's stamp covers the aggregate. Rows with `Id <= 0` are exempt. Weak
entities carry no audit columns. Temporal (system-versioned) tables keep the same four columns; the
period columns are history, not audit.

**Rationale.** `rowversion` moves on every update including bookkeeping, and cannot express "only
what the user sees changed"; a hash of columns is a second mechanism to maintain. A `datetime2(7)`
stamp that the pipeline alone writes is one mechanism, human-readable, and already needed for
display. Checking inside the persist statement makes lost updates impossible regardless of
isolation level.

**Alternatives rejected.** `rowversion`/`[Timestamp]` (see above); per-child stamps (children are
never saved alone). **Confidence.** High. **Review flag.** The early detection in round A (D6 step
5) is a UX nicety that costs nothing; the authoritative check is in round B. Both are kept; a purist
design keeps only the latter.

### D12 — One capability, declared once: interfaces on the entity, `[EntityAction]` on the behavior

**Decision.** A capability is a `CapabilityDefinition` registered with the platform's
`CapabilityRegistry`; the four built-ins and their projections:

| Capability | Declared by | Columns (properties required) | Securable(s) | Service / endpoints | Default filter | Statements |
|---|---|---|---|---|---|---|
| Audited | every `Entity<TKey>` | `CreatedAt, CreatedById, ModifiedAt, ModifiedById` | — | — | — | stamping in every UPDATE/INSERT |
| Activatable | `IActivatable` | `IsActive` | `(E, activate)`, filterable | `activate`, `deactivate` actions → `ActivateAsync`/`DeactivateAsync`, `POST …/activate`, `…/deactivate` | `IsActive = true` | guarded `UPDATE … SET IsActive = @v` fast path |
| Tree | `ITreeEntity<TKey>` | `ParentId, SubtreeCount, ActiveSubtreeCount` (+ platform `Node`) | — (uses `read`/`save`/`delete`) | `ITreeEntityService`: `GetByParentIdsAsync`, `DeleteWithDescendantsAsync`; `IncludeAncestors`; `POST …/children`, `…/delete-with-descendants` | — | recompute (D15) |
| Record + blobs | `IHasImage` (spec 0016) | `ImageId` | — | `GET …/image` (spec 0016) | — | token confirmation; post-commit delete |
| Temporal | `[Temporal]` (spec 0011) | period columns (shadow) | — | — | — | emitter skips unchanged rows |

Custom actions are declared once on the behavior:

```csharp
[EntityAction("post", SupportsFilter = true, LoadChildren = true)]
[Description("Posts draft invoices to the general ledger.")]
public async Task PostAsync(ActionContext<Invoice, int> ctx, PostArguments args) { … }
```

and project `(Invoice, post)` into the securable registry, `ExecuteActionAsync("post", …)`,
`POST …/post` with `{ ids, arguments }`, the MCP `actions.run` catalogue entry with the argument
schema derived from `PostArguments`, and the UI button metadata. The action pipeline is the save
pipeline with the load replaced by "the rows in `ids` visible under the action's filter, children
included when `LoadChildren`", the method as the validator/mutator (`ctx.Save(entities)` marks rows
for the emitter; `ctx.Batch` takes statements; `ctx.Error(...)` collects validation errors), and
`PostCheck` off by default.

All projections are visible through one object, `StackDescriptor` (§3.6), which the web layer maps
to endpoints, the MCP layer to tool descriptions, the SPA to buttons and default filters, and the
conformance tests to assertions.

**Rationale.** "Declared once" only holds if the declaration is the *entity's own shape* (the
interface's properties are the columns) and everything else is derived by the platform; any second
place — a fluent registration, an endpoint file — is where drift starts. `[EntityAction]` gives
custom logic the same treatment so that Invite, Post, Assign and Activate are one mechanism.

**Alternatives rejected.** Marker attributes without properties (`[Activatable]` generating a
shadow `IsActive`) — the property must exist for C# code and Queryex to read it. Fluent capability
registration at the composition site — declared twice. **Confidence.** High.

### D13 — Activate/deactivate are built-in actions with a one-round-trip SQL path

**Decision.** `activate`/`deactivate` are `EntityAction`s registered by the Activatable capability
with `Permission = "activate"`, `SupportsFilter = true`, and a **SQL-only** implementation: one batch
with the connect guard, the RLS pre-check into `@tm_vis`, `UPDATE t SET IsActive = @v, ModifiedAt =
@tm_Now, ModifiedById = @tm_UserId FROM … WHERE Id IN (SELECT Id FROM @tm_vis) AND IsActive <> @v`,
the tree `ActiveSubtreeCount` refresh when the entity is a tree, the tag bump, and the read-back
when `ReturnEntities`. A behavior that needs to veto (a user cannot deactivate themselves) overrides
`ValidateActionAsync("deactivate", …)`, which turns the action into the two-round-trip general path.

**Rationale.** The most common action must be the cheapest; the general mechanism must still be the
only mechanism, so the fast path is an optimization *of* `EntityAction`, not a sibling.
**Confidence.** High.

### D14 — Delete: one round trip in the common case; FK violations are validation errors

**Decision.** `DeleteByIdsAsync`: connect guard → `@tm_vis` pre-check (D9, filter of `delete`) →
`DELETE FROM t WHERE Id IN (SELECT Id FROM @tm_vis)` for children tables then the parent table →
tree count refresh → tag bump → commit. SQL error 547 (a foreign key elsewhere references the row)
maps to `ValidationException` with `Core.Errors.InUse` on the entity and the referencing table's
logical name as an argument; no C# pre-check for it. A behavior's `ValidateDeleteAsync` (which may
load the rows and anything else) adds round A before the delete batch. `DeleteByQueryAsync`
compiles `And(filter, deleteFilter)` with `Select = "Id"` into `@tm_vis` and deletes from it; the
result reports the count. `DeleteWithDescendantsAsync` compiles `Restriction = DescendantsOf(ids)`
twice — once bare into `@tm_all`, once with the delete filter into `@tm_vis` — throws 50006 when
they differ, and deletes `@tm_vis` in one statement (self-referencing foreign keys are checked at
statement end, so a subtree deletes in one `DELETE`). Soft-delete semantics are a domain concern
(`IActivatable`), never a delete substitute.

**Rationale.** The database already enforces referential integrity; asking it first is a wasted
round trip and a race. **Confidence.** High.

### D15 — The tree capability: platform-owned `Node`, SQL recompute over affected roots, cycles validated in C#

**Decision.** `ITreeEntity<TKey>` requires `ParentId`, `SubtreeCount`, `ActiveSubtreeCount`
(equal to `SubtreeCount` when the entity is not `IActivatable`, so the tree view has one code path).
`Node` (`hierarchyid`, nullable, unique filtered index `WHERE Node IS NOT NULL`, depth-first) is a
**platform-owned column that never appears on the class**: configured by the platform's EF
convention as a shadow property, excluded from the UDTT, exposed to Queryex as the entity's
`TreeNode`, and written only by the recompute statement. `Level` is `level()` (emits
`GetLevel()`); `IsLeaf` is `SubtreeCount = 1`. Node paths use the row's own `Id` as the path
component (`/15/342/1207/`), so a node value depends only on the ancestor chain, sibling order is
insertion order, and recomputes never renumber untouched siblings.

The recompute rides round B after the emitter's statements, scoped to the roots of every touched
row's old and new position:

```sql
DECLARE @tm_roots TABLE ([Id] int PRIMARY KEY);
;WITH Up AS (
    SELECT c.[Id], c.[ParentId] FROM [gl].[Centers] c WHERE c.[Id] IN (SELECT [Id] FROM @tm_touched)
    UNION ALL
    SELECT p.[Id], p.[ParentId] FROM [gl].[Centers] p JOIN Up u ON u.[ParentId] = p.[Id])
INSERT INTO @tm_roots ([Id]) SELECT DISTINCT [Id] FROM Up WHERE [ParentId] IS NULL OPTION (MAXRECURSION 64);
INSERT INTO @tm_roots ([Id])                                     -- old position, from the stale node
SELECT DISTINCT r.[Id]
FROM [gl].[Centers] c JOIN [gl].[Centers] r ON r.[Node] = c.[Node].GetAncestor(c.[Node].GetLevel() - 1)
WHERE c.[Id] IN (SELECT [Id] FROM @tm_touched) AND c.[Node] IS NOT NULL AND c.[Node].GetLevel() > 1
  AND r.[Id] NOT IN (SELECT [Id] FROM @tm_roots);

;WITH Paths AS (
    SELECT r.[Id], CAST('/' + CAST(r.[Id] AS varchar(11)) + '/' AS varchar(892)) AS [Path]
    FROM [gl].[Centers] r WHERE r.[Id] IN (SELECT [Id] FROM @tm_roots)
    UNION ALL
    SELECT c.[Id], CAST(p.[Path] + CAST(c.[Id] AS varchar(11)) + '/' AS varchar(892))
    FROM [gl].[Centers] c JOIN Paths p ON c.[ParentId] = p.[Id])
UPDATE t SET [Node] = CAST(p.[Path] AS hierarchyid)
FROM [gl].[Centers] t JOIN Paths p ON p.[Id] = t.[Id]
WHERE t.[Node] IS NULL OR t.[Node] <> CAST(p.[Path] AS hierarchyid)
OPTION (MAXRECURSION 64);

UPDATE t SET [SubtreeCount] = x.[Total], [ActiveSubtreeCount] = x.[Active]
FROM [gl].[Centers] t
CROSS APPLY (SELECT COUNT(*) AS [Total], SUM(CASE WHEN d.[IsActive] = 1 THEN 1 ELSE 0 END) AS [Active]
             FROM [gl].[Centers] d WHERE d.[Node].IsDescendantOf(t.[Node]) = 1) x
WHERE EXISTS (SELECT 1 FROM [gl].[Centers] r JOIN @tm_roots o ON o.[Id] = r.[Id] WHERE t.[Node].IsDescendantOf(r.[Node]) = 1)
  AND (t.[SubtreeCount] <> x.[Total] OR t.[ActiveSubtreeCount] <> x.[Active]);
```

Cycle validation is C#, in step 6: round A loads `Restriction = AncestorsOf(newParentIds)` (ids and
parent ids only), the pipeline joins that chain with the in-batch parent links, and a chain from a
row's new parent that reaches the row itself — or `ParentId == Id` — is `Core.Errors.TreeCycle` on
`ParentId`. `MAXRECURSION 64` on the recompute is the defense in depth (error 530 maps to an internal
error, since validation should have caught it) and the maximum depth of a tree.

Tree deletes refresh counts through the same statement; `GetByParentIdsAsync` is a plain query with
`Restriction = In(parentIds)` on `ParentId` (plus `ParentId is null` for the roots), served by the
`ParentId` index; a breadth-first `(Level, Node)` index is added only when `level()` predicates
appear.

**Rationale.** A per-row `GetDescendant` needs sibling coordination and a loop; a path built from
ids is collision-free, deterministic and O(affected subtree). Keeping `Node` off the class keeps the
`Microsoft.SqlServer.Types` dependency out of the save path and the author's file free of
`hierarchyid`. Cycles cannot be detected by the CTE (it recurses until the limit), so C# owns that
rule.

**Alternatives rejected.** In-memory recompute (the C# side must know every row's siblings);
`ROW_NUMBER` sibling numbering (renumbers siblings on every recompute and forces whole-root
recomputes for stable roots); `Node` as a class property typed `HierarchyId` (an EF type in
Abstractions) or as a string (leaks the encoding).

**Confidence.** High on the statements; medium on the shadow-property choice (departure 7.3;
the alternative is a BCL `TreeNode` struct with a platform value converter, owned by spec 0011).

### D16 — Details: children always, `Expand` for related entities, extras from the behavior, one round trip

**Decision.** A details read is one batch: the connect guard; the entity rows (`Restriction =
In(ids)`, read filter); every child collection the entity contract declares (`WHERE ParentKey IN
(SELECT Id FROM @tm_ids)`), recursively for grandchildren; for every `Expand` path, one statement
per distinct related entity type restricted by a semi-join chain through the path (`WHERE Id IN
(SELECT CenterId FROM [gl].[InvoiceLines] WHERE InvoiceId IN (SELECT Id FROM @tm_ids))`) — depth ≤
`MaxExpandDepth`; the behavior's `ContributeDetails` statements for the requested `Extras`; and the
row echo (`EchoSelect` compiled with `Restriction = In(ids)`). The materializer (spec 0011) folds
the flat results into the entity, its collections, and the `RelatedEntities` dictionary. A missing
`Expand` path is a caller error (`ArgumentException`), since paths are static facts of the model.
"If you can read an entity you can read what its details carry" — related entities are loaded
without their own read filters, as the brain dump requires (no partially visible document).

**Rationale.** Everything a page needs in one call is the brain dump's "1-1 rule"; deriving the
related dictionary from `Expand` paths against the model keeps the platform in charge of the joins
while the behavior adds only what the platform cannot know (workflow history, embedded aggregates).

**Alternatives rejected.** Hand-written SQL per details page (the draft's "SQL might be better
here") — kept as the `Extras` escape hatch only. **Confidence.** High.

### D17 — Distribution extension of pack entities

**Decision.** A pack ships `Center` (non-abstract, unsealed, `[Table]`-mapped) and, where it needs
logic, `CenterBehavior<TCenter> : EntityBehavior<TCenter, int> where TCenter : Center`. Its feature
takes the leaf as a type parameter with the pack default as the default:
`t.Gl(g => g.Center<MyCenter>())`. The platform registers `IEntityService<MyCenter, int>`,
`ITreeEntityService<MyCenter, int>` (capability detected on the leaf), and the behavior — the
distribution's subclass when one exists (convention), else the pack's generic closed over the leaf.
Columns the distribution adds are ordinary properties; capability interfaces added on the leaf
(`IHasImage`) project exactly as on a pack entity. A pack behavior's virtual methods call
`base.…` chains so a distribution rule composes with the pack rule rather than replacing it.

**Rationale.** This is the architecture's "extend a pack entity" mechanism carried through the
service layer with zero duplicated declarations. **Confidence.** High.

### D18 — Import reuses the bulk pipeline unchanged

**Decision.** The Excel codec calls `SaveAsync(entities, new SaveOptions { Mode, ReturnEntities =
false, Source = SaveSource.Import })` in chunks of `MaxSaveCount`; `Mode` maps the three import
modes (`Insert → InsertOnly`, `Update → UpdateOnly`, `Merge → Upsert`). Partial-sheet hydration is
the codec's job through `GetByIdsAsync` (one round trip per chunk, which also resolves the natural
key to the id through `LoadByKeyAsync`-style TVP lookups); write-once and server-owned rules apply
exactly as for the UI. Each chunk is its own transaction; the codec reports per-chunk outcomes
(all-or-nothing across chunks belongs to spec 0018/0019 as a background task). `SaveSource.Import`
is a tag on telemetry and available to behaviors (a rule may legitimately differ for imports, e.g.
skipping a UI-only warning) but never bypasses authorization or validation.

**Rationale.** The pipeline is bulk by construction; the only import-specific concerns (hydration,
natural keys, chunking) are the codec's. **Confidence.** High.

### D19 — The closed set of platform exceptions

**Decision.** `TellmaException` (abstract; `Code`, `Arguments`, localized `Message`) with the
derived set: `NotFoundException`, `ForbiddenException`, `ValidationException` (`Errors`),
`ConcurrencyException` (`Conflicts`), `LimitExceededException`, `SessionInvalidException` (the
connect guard's user-missing/inactive), `TenantUnavailableException` (spec 0010's suspension). The
web spec maps them to 404, 403, 422, 409, 413/400, 401, 503 respectively; anything else is a 500.
Internal invariants (a validator exceeding its rounds, an unknown `Expand` path, an unsupported
operation on a read-only stack) throw BCL exceptions — they are authoring bugs, not user outcomes.

SQL error numbers the executor maps (owned here, emitted by the pipeline's statements):

| Number | Raised by | Becomes |
|---|---|---|
| 50001 | connect guard: no active user for the subject | `SessionInvalidException` |
| 50002 | connect guard: cached permissions tag differs | executor re-runs once with fresh permissions (seam 16) |
| 50003 | RLS post-check | `ForbiddenException(Core.Errors.RowSecurityAfterSave)` |
| 50004 | concurrency stamp check | `ConcurrencyException` (conflicts from the preceding result set) |
| 50006 | RLS pre-check inside a batch | `NotFoundException` |
| 2601 / 2627 | unique index / constraint | `ValidationException` on the mapped property (`Core.Errors.Duplicate`) |
| 547 | foreign key | `ValidationException` (`Core.Errors.InvalidReference` on save; `Core.Errors.InUse` on delete) |
| 530 | tree recompute recursion limit | internal error (validation should have caught the cycle) |

**Rationale.** A closed set the web layer maps by type is simpler and safer than an interface every
exception implements ("what status am I?"); machine codes plus localized messages give both the SPA
and agents what they need. **Confidence.** High.

### D20 — Resource and action naming

**Decision.** A securable's `Resource` is the entity's logical name as Queryex knows it (`Center`,
`User`, `Invoice`) — unique across the schema by construction; `Action` is a lowercase verb from the
standard set `read`, `save`, `delete` plus capability and custom actions (`activate`, `post`,
`invite`); `all` is the wildcard on either. `save` implies `read`. Filter root for every action is
the entity itself. Non-entity securables (settings categories, admin operations) use a dotted
namespace `settings.general`; that naming is spec 0013's.

**Rationale.** One name per concept: the name the user writes in a filter, sees in the role editor,
and reads in the MCP catalogue is the same. **Confidence.** High. **Review flag.** Schema-qualified
resources (`gl.Center`) are equally plausible if two modules could ever share a logical name; the
Queryex schema forbids that today.

### D21 — Telemetry

**Decision.** Meter `Tellma.Core`; instruments as `const`s in `CrudTelemetryNames`
(`Tellma.Core.Abstractions.Crud`): `tellma.crud.operations` (counter, `{operation}`; tags
`crud.entity`, `crud.operation`, `crud.outcome` ∈ `ok|validation|not_found|forbidden|concurrency|
conflict|error`), `tellma.crud.operation.duration` (histogram, `s`, same tags minus outcome),
`tellma.crud.save.batch.size` (histogram, `{entity}`), `tellma.crud.validation.rounds` (histogram,
`{round}`), `tellma.crud.connect.reruns` (counter), `tellma.crud.after_commit.failures` (counter,
`error.type`), `tellma.crud.persist.scope` tag on the duration histogram (`batch|client`). The
per-request DB-call budget is spec 0011's `tellma.data.roundtrips`, tagged `data.operation =
<Entity>.<operation>` by this pipeline. No tenant or user tag anywhere; entity names are a closed set
per distribution. Logs carry tenant, user, entity, ids count, round count, and the error code.

**Confidence.** High.

### D22 — Round-trip budget per operation, and the failure modes

| Operation | Common case | When it grows |
|---|---|---|
| Query (with count, with ancestors) | 1 | — |
| Details (one or many) | 1 | — |
| Save, no behavior validators | 2 | +1 per dependent validation round (max 3) |
| Save, behavior validators declared before first await | 2 | same |
| Delete by ids / by query / with descendants | 1 | 2 with a `ValidateDeleteAsync` override |
| Activate / deactivate | 1 | 2 with a `ValidateActionAsync` override |
| Custom action | 2 | +1 for `PersistScope.ClientTransaction` |
| Import, per chunk | 3 (hydrate, round A, round B) | codec lookups may add 1 |

Failure modes of the collapsed connect: **deactivated user** — the guard throws 50001 before any
business statement; `SessionInvalidException` ends the session (401). **Stale permissions** — the
guard compares the cached permissions tag with the stored one and throws 50002 before any business
statement, so no statement ever runs under a stale filter; the executor reloads permissions (one
extra round trip), rebuilds the batch, and re-runs once; a second mismatch surfaces as a 409-class
`ConflictException`… no: it is retried once more and then fails as an internal error, because two
permission changes inside one request window is not a user scenario. **RLS pre-check ordering** —
the pre-check statement is compiled from the permissions that passed the guard in the same batch;
the two cannot disagree. **Between round A and round B** — the guard runs again in round B; a change
in between aborts round B and the save restarts from round A (bounded once). **Ancestors** —
computed in the same batch as the page through a key table (seam 4), so trees cost no extra trip.

### D23 — Registration and the stack descriptor

**Decision.** A stack is registered by type at composition: `declaration.Entity<Center>()` inside a
feature (the module's) or directly in `AddTellma(t => t.Entity<MyLookup>())`. The platform derives
the `StackDescriptor` (§3.6) from the leaf type: logical name (class name), resource, key type,
capabilities (interfaces), operations (`[Stack(Operations = …)]`, default `All`; `ReadOnly` for
lookups), actions (capabilities + `[EntityAction]` methods on the discovered behavior), searchable
properties, natural key, default filter, child collections and expandable navigations (from the
entity contract), ceilings, and the `[Description]` texts. The descriptor is the single input for
endpoint projection (spec 0015), the MCP `describe` tool, the SPA's schema metadata, the
securables registration (spec 0013), and the conformance tests. It is computed once at startup and
validated then: an `[EntityAction]` on a non-behavior class, a duplicate action name, a searchable
property that is not a string, an `IActivatable` without `IsActive` (impossible — the interface
requires it) are startup failures with the entity named.

**Rationale.** AI-native authoring needs one reflectable description of what an entity can do; the
descriptor is that, and it exists whether or not a human ever reads it. **Confidence.** High.

### D24 — Testing: a conformance base class per stack

**Decision.** `Tellma.Core.Testing` ships `StackConformanceTests<TEntity, TKey>` — an abstract xUnit
class a distribution closes per entity in three lines — that, against the LocalDB fixture
(`Category=Integration`), asserts: every operation of the descriptor is reachable and every absent
one throws; the round-trip budget of D22 (via `SqlConnection.RetrieveStatistics()["ServerRoundtrips"]`);
the securables the stack registered; the capability projections (default filter, actions); RLS
hidden-equals-missing; concurrency conflict and override; write-once rejection; and, for trees,
recompute correctness and cycle rejection. The pipeline's own suites live in
`test/core/Tellma.Core.Tests` (in-memory batch double for the orchestration, validation scheduler
property tests) and `test/core/Tellma.Core.IntegrationTests` (fixture entities `Node`, `Item`,
`ItemLine` with every capability, on LocalDB/Testcontainers).

**Rationale.** A coding agent that adds an entity should get its tests for free; the conformance
class is the mechanical way to make "the stack works" a build gate rather than a review question.
**Confidence.** High.

---

## 3. Contracts

Compilable-looking shapes. Section 3.1–3.7 are owned by this theme; 3.8 lists what it needs from
other themes' seams, in the shape it needs. Namespaces: contracts in `Tellma.Core.Abstractions.Crud`;
implementation in `Tellma.Core` (`Tellma.Core.Crud`). `Tellma.Core.Abstractions` takes a reference
to `Tellma.Core.Queryex` for `FilterTree`, `QuerySpec`, `QueryexType`, `QueryexColumn` (departure
7.1).

### 3.1 The service

```csharp
namespace Tellma.Core.Abstractions.Crud;

/// <summary>
///     The standard operations of one registered top-level entity. Implemented once by the platform;
///     never implemented or subclassed by a distribution or module.
/// </summary>
public interface IEntityService<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : struct, IEquatable<TKey>
{
    /// <summary>What this stack can do, as computed at startup from the entity class and its behavior.</summary>
    StackDescriptor Descriptor { get; }

    /// <summary>Rows in the caller's select shape under the caller's read filter, with optional capped count and tree ancestors.</summary>
    Task<QueryResult> QueryAsync(EntityQuery query, CancellationToken cancellationToken);

    /// <summary>One entity with children, related entities, extras and row echo; throws <see cref="NotFoundException"/> when missing or hidden.</summary>
    Task<DetailsResult<TEntity>> GetByIdAsync(TKey id, DetailsRequest request, CancellationToken cancellationToken);

    /// <summary>The found entities plus the ids that were missing or hidden; never throws for absence.</summary>
    Task<DetailsSetResult<TEntity, TKey>> GetByIdsAsync(IReadOnlyList<TKey> ids, DetailsRequest request, CancellationToken cancellationToken);

    /// <summary>Upserts top-level rows and synchronizes their children in one transaction; bulk by construction.</summary>
    Task<SaveResult<TEntity, TKey>> SaveAsync(IReadOnlyList<TEntity> entities, SaveOptions options, CancellationToken cancellationToken);

    /// <summary>Deletes the rows visible under the caller's delete filter; a hidden id is reported as not found.</summary>
    Task<DeleteResult> DeleteByIdsAsync(IReadOnlyList<TKey> ids, CancellationToken cancellationToken);

    /// <summary>Deletes every row matching the filter under the caller's delete filter and reports the count.</summary>
    Task<DeleteResult> DeleteByQueryAsync(FilterTree filter, IReadOnlyList<QueryArgument> arguments, CancellationToken cancellationToken);

    /// <summary>Runs a capability or custom action over the given ids under the action's permission and filter.</summary>
    Task<ActionResult<TEntity, TKey>> ExecuteActionAsync(string action, IReadOnlyList<TKey> ids, object? arguments, ActionOptions options, CancellationToken cancellationToken);
}

/// <summary>The <c>int</c>-keyed alias every distribution names.</summary>
public interface IEntityService<TEntity> : IEntityService<TEntity, int> where TEntity : class, IEntity<int> { }

/// <summary>Adds the tree operations. Registered by the platform for every entity implementing <see cref="ITreeEntity{TKey}"/>.</summary>
public interface ITreeEntityService<TEntity, TKey> : IEntityService<TEntity, TKey>
    where TEntity : class, IEntity<TKey>, ITreeEntity<TKey>
    where TKey : struct, IEquatable<TKey>
{
    /// <summary>Children of each parent (a null parent means the roots), in the projection's select shape and filter; one call loads an auto-expanded tree.</summary>
    Task<QueryResult> GetByParentIdsAsync(IReadOnlyList<TKey?> parentIds, EntityQuery projection, CancellationToken cancellationToken);

    /// <summary>Deletes the rows and every descendant; any hidden row in a subtree makes the whole request not found.</summary>
    Task<DeleteResult> DeleteWithDescendantsAsync(IReadOnlyList<TKey> ids, CancellationToken cancellationToken);
}

/// <summary>Typed shortcuts for the activatable capability's actions; a compile error on an entity that lacks the capability.</summary>
public static class ActivatableEntityServiceExtensions
{
    public static Task<ActionResult<TEntity, TKey>> ActivateAsync<TEntity, TKey>(this IEntityService<TEntity, TKey> service, IReadOnlyList<TKey> ids, CancellationToken cancellationToken)
        where TEntity : class, IEntity<TKey>, IActivatable where TKey : struct, IEquatable<TKey>
        => service.ExecuteActionAsync(StandardActions.Activate, ids, null, ActionOptions.Default, cancellationToken);

    public static Task<ActionResult<TEntity, TKey>> DeactivateAsync<TEntity, TKey>(this IEntityService<TEntity, TKey> service, IReadOnlyList<TKey> ids, CancellationToken cancellationToken)
        where TEntity : class, IEntity<TKey>, IActivatable where TKey : struct, IEquatable<TKey>
        => service.ExecuteActionAsync(StandardActions.Deactivate, ids, null, ActionOptions.Default, cancellationToken);
}

/// <summary>The action and securable names the platform owns.</summary>
public static class StandardActions
{
    public const string Read = "read";
    public const string Save = "save";
    public const string Delete = "delete";
    public const string Activate = "activate";     // the securable for both directions
    public const string Deactivate = "deactivate"; // the action name; permission = Activate
    public const string All = "all";
}
```

### 3.2 Requests and results

```csharp
namespace Tellma.Core.Abstractions.Crud;

/// <summary>A query over the stack's entity: Queryex clauses plus the service-level conveniences.</summary>
public sealed record EntityQuery
{
    /// <summary>The select list; null means the entity's default columns.</summary>
    public string? Select { get; init; }
    /// <summary>True for a grouped query.</summary>
    public bool Aggregate { get; init; }
    /// <summary>Free text, translated by the platform (or the behavior) into a filter over the searchable properties.</summary>
    public string? Search { get; init; }
    public FilterTree? Filter { get; init; }
    public FilterTree? Having { get; init; }
    public string? OrderBy { get; init; }
    public int? Skip { get; init; }
    /// <summary>Capped at the stack's <c>MaxTake</c>; null means the cap.</summary>
    public int? Take { get; init; }
    /// <summary>Also compute the paging-ignored count, capped at the stack's <c>CountCap</c>.</summary>
    public bool IncludeCount { get; init; }
    /// <summary>Tree entities only: also return the ancestors of the page's rows so a filtered page renders as a tree.</summary>
    public bool IncludeAncestors { get; init; }
    /// <summary>Typed values for every <c>@name</c> the clauses mention.</summary>
    public IReadOnlyList<QueryArgument> Arguments { get; init; } = [];
}

/// <summary>A typed argument. The declaration passed to the engine is <c>(Name, Type, IsNotNull: Value is not null)</c>.</summary>
public sealed record QueryArgument(string Name, QueryexType Type, object? Value);

/// <summary>The rows of a query in the select shape, with the count and the ancestors when asked for.</summary>
public sealed record QueryResult(
    IReadOnlyList<QueryexColumn> Columns,
    IReadOnlyList<object?[]> Rows,
    int? Count,
    bool CountIsCapped,
    IReadOnlyList<object?[]>? Ancestors);

/// <summary>What a details read should carry beyond the entity and its children.</summary>
public sealed record DetailsRequest
{
    public static DetailsRequest Default { get; } = new();
    /// <summary>Navigation paths whose targets go into the related dictionary: "Parent", "Lines.Center".</summary>
    public IReadOnlyList<string> Expand { get; init; } = [];
    /// <summary>Names of the extras the behavior knows how to load.</summary>
    public IReadOnlyList<string> Extras { get; init; } = [];
    /// <summary>The search page's select list, for the row echo.</summary>
    public string? EchoSelect { get; init; }
}

/// <summary>Related entities keyed by logical entity name, then by id.</summary>
public sealed class RelatedEntities
{
    public IReadOnlyDictionary<string, IReadOnlyDictionary<object, object>> ByEntity { get; }
    public TRelated? Get<TRelated>(object id) where TRelated : class;
}

public sealed record DetailsResult<TEntity>(TEntity Entity, RelatedEntities Related, IReadOnlyDictionary<string, object?> Extras, object?[]? RowEcho);

public sealed record DetailsSetResult<TEntity, TKey>(
    IReadOnlyList<TEntity> Entities,
    RelatedEntities Related,
    IReadOnlyDictionary<string, object?> Extras,
    IReadOnlyList<object?[]>? RowEchoes,
    IReadOnlyList<TKey> Missing);

public enum SaveMode { Upsert, InsertOnly, UpdateOnly }
public enum SaveSource { Api, Import, Seed, Action }

public sealed record SaveOptions
{
    public static SaveOptions Default { get; } = new();
    public SaveMode Mode { get; init; } = SaveMode.Upsert;
    /// <summary>Read the saved entities back in the persist round, in details shape.</summary>
    public bool ReturnEntities { get; init; } = true;
    public DetailsRequest Details { get; init; } = DetailsRequest.Default;
    /// <summary>Skip the concurrency stamp comparison — the "overwrite their changes" retry.</summary>
    public bool OverrideConcurrency { get; init; }
    public SaveSource Source { get; init; } = SaveSource.Api;
}

public sealed record SaveResult<TEntity, TKey>(IReadOnlyList<TKey> Ids, DetailsSetResult<TEntity, TKey>? Details);
public sealed record DeleteResult(int Count);
public sealed record ActionOptions(bool ReturnEntities = false, DetailsRequest? Details = null)
{
    public static ActionOptions Default { get; } = new();
}
public sealed record ActionResult<TEntity, TKey>(IReadOnlyList<TKey> Ids, DetailsSetResult<TEntity, TKey>? Details);
```

### 3.3 The behavior and its contexts

```csharp
namespace Tellma.Core.Abstractions.Crud;

/// <summary>
///     The business logic of one entity, plugged into the platform's service. Every member has an
///     empty default; a stack with no behavior class is the common case. Discovered by convention:
///     the one non-abstract class deriving from this type for the entity in the composition's
///     assemblies, overridable at the selection site.
/// </summary>
public abstract class EntityBehavior<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : struct, IEquatable<TKey>
{
    /// <summary>Normalize the payload before any DB call; runs after the platform's own trimming and server-owned clearing.</summary>
    protected internal virtual void Preprocess(PreprocessContext<TEntity, TKey> context) { }

    /// <summary>Validate a save with batched context loads; every error is collected. Loads declared before the first await ride the first round.</summary>
    protected internal virtual Task ValidateSaveAsync(SaveValidation<TEntity, TKey> validation) => Task.CompletedTask;

    /// <summary>Validate a delete; overriding this adds one round trip before the delete batch.</summary>
    protected internal virtual Task ValidateDeleteAsync(DeleteValidation<TEntity, TKey> validation) => Task.CompletedTask;

    /// <summary>Validate an action by name; overriding this moves a SQL-only action (activate) onto the general path.</summary>
    protected internal virtual Task ValidateActionAsync(string action, ActionValidation<TEntity, TKey> validation) => Task.CompletedTask;

    /// <summary>Add statements and notifications to the persist batch; they commit or roll back with the save.</summary>
    protected internal virtual void ContributeToPersist(PersistContext<TEntity, TKey> context) { }

    /// <summary>Work that cannot roll back, after the transaction committed. Failures are logged, never surfaced.</summary>
    protected internal virtual Task AfterCommitAsync(CommittedContext<TEntity, TKey> context, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Add the statements that produce the named extras of a details read, in the same round trip.</summary>
    protected internal virtual void ContributeDetails(DetailsContext<TEntity, TKey> context) { }

    /// <summary>Translate free text into a filter; null keeps the platform's disjunction over the searchable properties.</summary>
    protected internal virtual FilterTree? SearchFilter(string search) => null;

    /// <summary>A criterion the caller satisfies regardless of roles ("assigned to me"), OR-ed with the permission filters; null adds nothing.</summary>
    protected internal virtual FilterTree? BespokeCriteria(string action) => null;
}

/// <summary>The <c>int</c>-keyed alias.</summary>
public abstract class EntityBehavior<TEntity> : EntityBehavior<TEntity, int> where TEntity : class, IEntity<int> { }

/// <summary>Declares a bulk action on a behavior method: <c>Task M(ActionContext&lt;TEntity,TKey&gt; ctx[, TArguments args])</c>.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class EntityActionAttribute(string name) : Attribute
{
    /// <summary>The action name; lowercase; unique per stack. Also the endpoint segment and the MCP action name.</summary>
    public string Name { get; } = name;
    /// <summary>The securable action; defaults to <see cref="Name"/>. Several actions may share one securable (activate/deactivate).</summary>
    public string? Permission { get; init; }
    /// <summary>Whether the securable accepts a row filter.</summary>
    public bool SupportsFilter { get; init; } = true;
    /// <summary>Load the child collections of the target rows before the method runs.</summary>
    public bool LoadChildren { get; init; }
    /// <summary>Re-check the action's filter on the rows after the write; off because actions commonly leave the filter that permitted them.</summary>
    public bool PostCheck { get; init; }
}

public sealed class PreprocessContext<TEntity, TKey>
{
    public IReadOnlyList<TEntity> Entities { get; }
    public IRequestContext Request { get; }
    public SaveSource Source { get; }
    public bool IsNew(TEntity entity);
}

/// <summary>The validation surface: entities, the pre-save images, batched loads, and error collection.</summary>
public sealed class SaveValidation<TEntity, TKey>
{
    public IReadOnlyList<TEntity> Entities { get; }
    public IRequestContext Request { get; }
    public SaveSource Source { get; }
    public QueryexSchema Schema { get; }
    public bool IsNew(TEntity entity);
    /// <summary>The top-level row image before this save, loaded in the first round; null for a new entity.</summary>
    public TEntity? Existing(TEntity entity);
    public Task<IReadOnlyDictionary<TKey2, TChild[]>> LoadExistingChildrenAsync<TChild, TKey2>() where TChild : class;
    /// <summary>TVP-restricted lookup on a unique property; keys are batched and deduplicated across validators.</summary>
    public Task<IReadOnlyDictionary<TK, TOther>> LoadByKeyAsync<TOther, TK>(Expression<Func<TOther, TK>> key, IEnumerable<TK> keys, string? select = null)
        where TOther : class where TK : notnull;
    public Task<IReadOnlyList<TOther>> LoadEntitiesAsync<TOther>(FilterTree filter, IReadOnlyList<QueryArgument>? arguments = null, string? select = null) where TOther : class;
    public Task<IReadOnlyList<TRow>> LoadRowsAsync<TRow>(QuerySpec spec, IReadOnlyList<QueryArgument> arguments, Func<IDataRecord, TRow> materialize);
    /// <summary>The raw-SQL escape hatch; the statement must stay out of the <c>@qx</c> and <c>@tm_</c> namespaces.</summary>
    public Task<IReadOnlyList<TRow>> LoadSqlAsync<TRow>(SqlStatement statement, Func<IDataRecord, TRow> materialize);
    public void Error<TProperty>(TEntity entity, Expression<Func<TEntity, TProperty>> property, string code, params object?[] arguments);
    public void Error(TEntity entity, string propertyPath, string code, params object?[] arguments);
    public void Error(TEntity entity, string code, params object?[] arguments);
    public bool HasErrors { get; }
}

/// <summary>Delete validation: the target ids, their row images under the delete filter, and the same loads.</summary>
public sealed class DeleteValidation<TEntity, TKey>
{
    public IReadOnlyList<TKey> Ids { get; }
    public IReadOnlyList<TEntity> Entities { get; }
    public IRequestContext Request { get; }
    public Task<IReadOnlyDictionary<TK, TOther>> LoadByKeyAsync<TOther, TK>(Expression<Func<TOther, TK>> key, IEnumerable<TK> keys, string? select = null) where TOther : class where TK : notnull;
    public Task<IReadOnlyList<TRow>> LoadSqlAsync<TRow>(SqlStatement statement, Func<IDataRecord, TRow> materialize);
    public void Error(TEntity entity, string code, params object?[] arguments);
}

public sealed class ActionValidation<TEntity, TKey> : SaveValidation<TEntity, TKey> { public object? Arguments { get; } }

/// <summary>The persist-time surface: final ids, the batch's transactional segment, notifications.</summary>
public sealed class PersistContext<TEntity, TKey>
{
    public IReadOnlyList<TEntity> Entities { get; }
    public TEntity? Existing(TEntity entity);
    public IRequestContext Request { get; }
    /// <summary>Statements added here run after the emitter's and before the post-check, inside the transaction.</summary>
    public DbBatch Batch { get; }
    public void Notify(InboxDraft draft);
    /// <summary>Request the client-controlled two-command persist (one extra round trip); metered.</summary>
    public PersistScope Scope { get; set; }
    public Func<PersistContext<TEntity, TKey>, CancellationToken, Task>? InTransaction { get; set; }
}

public enum PersistScope { Batch, ClientTransaction }

public sealed class CommittedContext<TEntity, TKey>
{
    public IReadOnlyList<TEntity> Entities { get; }
    public IReadOnlyList<TKey> Ids { get; }
    public IReadOnlyList<TKey> DeletedIds { get; }
    public IRequestContext Request { get; }
}

public sealed class DetailsContext<TEntity, TKey>
{
    public IReadOnlyList<TKey> Ids { get; }
    public DetailsRequest Request { get; }
    public IRequestContext RequestContext { get; }
    public DbBatch Batch { get; }
    /// <summary>Register an extra by name; the materializer receives the statement's result set.</summary>
    public void AddExtra<TRow>(string name, SqlStatement statement, Func<IDataRecord, TRow> materialize);
    public void AddExtra(string name, QuerySpec spec, IReadOnlyList<QueryArgument> arguments);
}

/// <summary>What an <see cref="EntityActionAttribute"/> method receives.</summary>
public sealed class ActionContext<TEntity, TKey>
{
    public IReadOnlyList<TEntity> Entities { get; }
    public IRequestContext Request { get; }
    public DbBatch Batch { get; }
    /// <summary>Mark entities for the emitter; they persist with audit stamping and the concurrency check like a save.</summary>
    public void Save(IEnumerable<TEntity> entities);
    public void Notify(InboxDraft draft);
    public void Error<TProperty>(TEntity entity, Expression<Func<TEntity, TProperty>> property, string code, params object?[] arguments);
    public void Error(TEntity entity, string code, params object?[] arguments);
}
```

### 3.4 Errors and exceptions

```csharp
namespace Tellma.Core.Abstractions.Crud;

/// <summary>One validation error at a path in the request body.</summary>
/// <param name="Path">"[3].Code", "[0].Lines[2].Quantity", or "[1]" for an entity-level error; CLR property names.</param>
/// <param name="Code">The resource key, e.g. "Core.Errors.Duplicate".</param>
/// <param name="Arguments">Named values for the message.</param>
/// <param name="Message">The message localized in the request culture at throw time.</param>
public sealed record ValidationError(string Path, string Code, IReadOnlyList<KeyValuePair<string, string>> Arguments, string Message);

/// <summary>Base of the closed set the web layer maps to statuses.</summary>
public abstract class TellmaException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
    public IReadOnlyList<KeyValuePair<string, string>> Arguments { get; init; } = [];
}

public sealed class NotFoundException(string entity, string message) : TellmaException("Core.Errors.NotFound", message);
public sealed class ForbiddenException(string code, string message) : TellmaException(code, message);
public sealed class ValidationException(IReadOnlyList<ValidationError> errors) : TellmaException("Core.Errors.Validation", "…")
{
    public IReadOnlyList<ValidationError> Errors { get; } = errors;
}
public sealed record ConcurrencyConflict(object Id, DateTime ModifiedAt, int ModifiedById);
public sealed class ConcurrencyException(IReadOnlyList<ConcurrencyConflict> conflicts) : TellmaException("Core.Errors.Concurrency", "…")
{
    public IReadOnlyList<ConcurrencyConflict> Conflicts { get; } = conflicts;
}
public sealed class LimitExceededException(string limit, int value, int maximum) : TellmaException("Core.Errors.LimitExceeded", "…");
public sealed class SessionInvalidException(string reason) : TellmaException("Core.Errors.SessionInvalid", "…");

/// <summary>The error codes this pipeline raises; resource keys under <c>Core.Errors</c>.</summary>
public static class CoreErrors
{
    public const string Duplicate = "Core.Errors.Duplicate";
    public const string InvalidReference = "Core.Errors.InvalidReference";
    public const string InUse = "Core.Errors.InUse";
    public const string WriteOnce = "Core.Errors.WriteOnce";
    public const string TreeCycle = "Core.Errors.TreeCycle";
    public const string HasChildren = "Core.Errors.HasChildren";
    public const string RowSecurityAfterSave = "Core.Errors.RowSecurityAfterSave";
    public const string Required = "Core.Errors.Required";
    public const string MaxLength = "Core.Errors.MaxLength";
}
```

### 3.5 Stack options and telemetry names

```csharp
namespace Tellma.Core.Abstractions.Crud;

[Flags]
public enum StackOperations
{
    None = 0, Query = 1, Details = 2, Save = 4, Delete = 8, Import = 16, Export = 32,
    Read = Query | Details | Export,
    All = Read | Save | Delete | Import,
}

/// <summary>Optional per-entity stack options; omitted in the common case.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class StackAttribute : Attribute
{
    public StackOperations Operations { get; init; } = StackOperations.All;
    public int MaxTake { get; init; } = 500;
    public int CountCap { get; init; } = 10_000;
    public int MaxSaveCount { get; init; } = 5_000;
    public int MaxActionIds { get; init; } = 5_000;
    public int MaxExpandDepth { get; init; } = 3;
    public int MaxValidationRounds { get; init; } = 3;
    /// <summary>Queryex text the search page applies by default; the activatable capability supplies "IsActive = true" when unset.</summary>
    public string? DefaultFilter { get; init; }
    /// <summary>The default select list; null derives it from the natural key, names, code, IsActive and audit columns.</summary>
    public string? DefaultSelect { get; init; }
}

public static class CrudTelemetryNames
{
    public const string MeterName = "Tellma.Core";
    public const string OperationsInstrument = "tellma.crud.operations";
    public const string OperationDurationInstrument = "tellma.crud.operation.duration";
    public const string SaveBatchSizeInstrument = "tellma.crud.save.batch.size";
    public const string ValidationRoundsInstrument = "tellma.crud.validation.rounds";
    public const string ConnectRerunsInstrument = "tellma.crud.connect.reruns";
    public const string AfterCommitFailuresInstrument = "tellma.crud.after_commit.failures";
    public const string EntityTag = "crud.entity";
    public const string OperationTag = "crud.operation";
    public const string OutcomeTag = "crud.outcome";
    public const string PersistScopeTag = "crud.persist.scope";
    public const string SourceTag = "crud.source";
}
```

### 3.6 The stack descriptor

```csharp
namespace Tellma.Core.Abstractions.Crud;

/// <summary>Everything a stack can do, computed once at startup; the single input for endpoint projection, MCP tools, SPA metadata, securables and tests.</summary>
public sealed record StackDescriptor(
    string Entity,                                   // logical name: "Center"
    string Resource,                                 // securable resource: "Center"
    Type EntityType,
    Type KeyType,
    string? Description,
    StackOperations Operations,
    IReadOnlySet<string> Capabilities,              // "activatable", "tree", "image", "temporal"
    IReadOnlyList<ActionDescriptor> Actions,
    IReadOnlyList<string> SearchableProperties,
    IReadOnlyList<string> NaturalKey,
    string? DefaultFilter,
    string DefaultSelect,
    IReadOnlyList<ChildCollectionDescriptor> Children,
    IReadOnlyList<string> ExpandableNavigations,
    StackLimits Limits);

public sealed record ActionDescriptor(string Name, string Permission, bool SupportsFilter, Type? ArgumentsType, string? Description, bool IsBuiltIn);
public sealed record ChildCollectionDescriptor(string Property, Type ChildType, string ParentKeyProperty);
public sealed record StackLimits(int MaxTake, int CountCap, int MaxSaveCount, int MaxActionIds, int MaxExpandDepth, int MaxValidationRounds);

/// <summary>Every registered stack, for the web, MCP and metadata layers.</summary>
public interface IStackRegistry
{
    IReadOnlyList<StackDescriptor> Stacks { get; }
    StackDescriptor? Find(string entity);
}
```

### 3.7 Conformance tests (in `Tellma.Core.Testing`)

```csharp
namespace Tellma.Core.Testing.Crud;

/// <summary>Closed per entity by a distribution's test project; asserts the stack's contract against the LocalDB fixture.</summary>
[Trait("Category", "Integration")]
public abstract class StackConformanceTests<TEntity, TKey>(StackFixture fixture)
    where TEntity : class, IEntity<TKey> where TKey : struct, IEquatable<TKey>
{
    /// <summary>A valid new entity the tests can save; the only member a subclass must supply.</summary>
    protected abstract TEntity NewValidEntity(int ordinal);
    // Facts: Descriptor_matches_capabilities; Query_costs_one_roundtrip; Details_costs_one_roundtrip;
    // Save_costs_two_roundtrips; Hidden_rows_read_as_missing; Concurrency_conflict_and_override;
    // WriteOnce_rejected; Securables_registered; Tree_recompute_and_cycle (when ITreeEntity); …
}
```

### 3.8 Contracts needed from other themes (the shape this pipeline consumes)

```csharp
// ---- spec 0011 (entity contract) — namespace Tellma.Core.Abstractions.Entities
public interface IEntity<TKey> where TKey : struct, IEquatable<TKey> { TKey Id { get; set; } }
/// <summary>Top-level base: key plus the four audit columns; system versioning is an additive table option, not a base class.</summary>
public abstract class Entity<TKey> : IEntity<TKey>, IAudited
{
    public TKey Id { get; set; }
    [ServerOwned] public DateTime CreatedAt { get; set; }
    [ServerOwned] public int CreatedById { get; set; }
    [ServerOwned] public DateTime ModifiedAt { get; set; }      // the concurrency token; echoed by clients
    [ServerOwned] public int ModifiedById { get; set; }
}
public abstract class ChildEntity<TKey> : IEntity<TKey> { public TKey Id { get; set; } }
public interface IAudited { DateTime CreatedAt { get; } int CreatedById { get; } DateTime ModifiedAt { get; } int ModifiedById { get; } }
public interface IActivatable { bool IsActive { get; set; } }
public interface ITreeEntity<TKey> where TKey : struct { TKey? ParentId { get; set; } int SubtreeCount { get; set; } int ActiveSubtreeCount { get; set; } }
[AttributeUsage(AttributeTargets.Property)] public sealed class ServerOwnedAttribute : Attribute;   // client values ignored; pipeline writes
[AttributeUsage(AttributeTargets.Property)] public sealed class WriteOnceAttribute : Attribute;     // may be set on insert only
[AttributeUsage(AttributeTargets.Property)] public sealed class NaturalKeyAttribute : Attribute;    // export/import identity; inference rules in spec 0011
[AttributeUsage(AttributeTargets.Property)] public sealed class SearchableAttribute : Attribute;
[AttributeUsage(AttributeTargets.Property)] public sealed class NoTrimAttribute : Attribute;
/// <summary>A [NotMapped] child collection: the EF model has no parent→child navigation; the pipeline saves and loads it.</summary>
[AttributeUsage(AttributeTargets.Property)] public sealed class ChildrenAttribute(string parentKeyProperty) : Attribute { public string ParentKeyProperty { get; } = parentKeyProperty; }
/// <summary>Model facts the pipeline reads: columns, key, children, FK targets (for temp-id rewriting), unique indexes (for 2601 mapping), write-once/server-owned sets.</summary>
public interface IEntityMetadata<TEntity> { … }

// ---- spec 0011 (batch abstraction) — namespace Tellma.Core.Abstractions.Data
public interface IDbBatchFactory { DbBatch Create(); }
/// <summary>One round trip: compiled queries, save specs, raw statements, id reservations, tag bumps, in one command walked with NextResult().</summary>
public abstract class DbBatch
{
    public abstract BatchRows<TRow> AddQuery<TRow>(QuerySpec spec, IReadOnlyList<QueryArgument> arguments, Func<IDataRecord, TRow> materialize, bool mayRetry = true);
    public abstract BatchRows<TRow> AddSql<TRow>(SqlStatement statement, Func<IDataRecord, TRow> materialize, bool mayRetry);
    public abstract void AddSql(SqlStatement statement, bool mayRetry);
    public abstract BatchSave AddSave<TEntity>(SaveSpec<TEntity> spec) where TEntity : class;   // mayRetry = false
    public abstract BatchIdRange AddIdReservation(Type entityType, int count);                  // mayRetry = true
    public abstract void BeginTransaction();   // emits SET XACT_ABORT ON; BEGIN TRAN
    public abstract void Commit();             // emits COMMIT; later statements run outside
    public abstract Task<DbBatchResult> ExecuteAsync(CancellationToken cancellationToken);
}
/// <summary>Raw SQL with BCL-typed parameters and the tables it writes (for automatic tag bumps).</summary>
public sealed record SqlStatement(string Text, IReadOnlyList<SqlValue> Parameters, IReadOnlyList<string> Writes);
public sealed record SqlValue(string Name, object? Value, QueryexStoreType StoreType);
public sealed record TableValuedValue(string Name, string TableTypeLogicalName, IEnumerable<object> Rows);
/// <summary>What the emitter needs: rows with final ids, the mode, the key/parent-key, audit stamping on, concurrency check unless overridden.</summary>
public sealed record SaveSpec<TEntity>(IReadOnlyList<TEntity> Rows, SaveMode Mode, bool CheckConcurrency, IReadOnlyList<SaveChildSpec> Children);
public interface IIdAllocator { bool TryTake(Type entityType, int count, out IdRange range); void Append(DbBatch batch, Type entityType, int count); }

// ---- spec 0011 (Queryex host + engine amendments documented there)
public interface IQueryexHost
{
    QueryexEngine Engine { get; }
    /// <summary>The tenant's schema (languages gated); identity changes when the tenant configuration changes.</summary>
    QueryexSchema Schema { get; }
    EntityDescriptor Entity<TEntity>();
    IReadOnlyList<QueryexParameterDeclaration> Declare(IReadOnlyList<QueryArgument> arguments);
    IReadOnlyList<QueryexParameterSlot> Bind(CompiledQuery query, IReadOnlyList<QueryArgument> arguments);   // today(), now(), me(), zone
}
// QuerySpec amendments this pipeline needs (owned by spec 0011 as spec 0008 amendments):
//   QuerySpec.Restriction : KeyRestriction?      — In | AncestorsOf | DescendantsOf over a unique property,
//                                                  keys from a TVP (IdList/BigIdList/GuidList/StringList) or a batch table variable
//   QuerySpec.CountCap : int?                    — grand-total count emitted as COUNT(*) over TOP (@cap)
//   QueryCompilationOptions.Sink                 — ResultSet | KeyTable("@tm_x") (Select must be the key path;
//                                                  emits INSERT INTO @tm_x ([Id]) SELECT …, with OUTPUT when rows are also wanted)
//   QueryCompilationOptions.KeyListTypes         — physical TVP type names per key store type
//   level() -> Numeric                           — emits [Node].GetLevel()

// ---- spec 0013 (permissions) — namespace Tellma.Core.Abstractions.Security
public interface IPermissionEvaluator
{
    /// <summary>Allowed, and if so under which filter (null = unrestricted); reasons for the "why" query. Cached by the permissions tag.</summary>
    ValueTask<PermissionDecision> EvaluateAsync(string resource, string action, FilterTree? bespokeCriteria, CancellationToken cancellationToken);
    /// <summary>The cached tag the connect guard compares against; null forces a load.</summary>
    Guid? CachedPermissionsTag { get; }
    Task ReloadAsync(CancellationToken cancellationToken);
}
public sealed record PermissionDecision(bool IsAllowed, FilterTree? Filter, IReadOnlyList<PermissionReason> Reasons);
public sealed record SecurableDeclaration(string Resource, string Action, bool SupportsFilter, string? FilterRootEntity, string? Description);
public interface ISecurableRegistry { void Add(SecurableDeclaration securable); IReadOnlyList<SecurableDeclaration> All { get; } }
/// <summary>The connect guard: resolves the subject, stamps activity (throttled), reads tags, THROWs 50001/50002; declares @tm_UserId and @tm_Now for later statements.</summary>
public interface IConnectStatement { void Prepend(DbBatch batch, Guid? cachedPermissionsTag, bool assertOnly); }

// ---- spec 0010 (request context) — namespace Tellma.Core.Abstractions.Hosting
public interface IRequestContext
{
    int TenantId { get; }
    string? Subject { get; }
    int? UserId { get; }                 // set by the connect step
    CultureInfo Culture { get; }
    string Calendar { get; }             // "gregorian" | "umalqura" | "ethiopian" (spec 0012 codes)
    TimeZoneInfo TimeZone { get; }
    DateOnly Today { get; }
    bool IsSandbox { get; }
}

// ---- spec 0012 (localization)
public interface IPlatformLocalizer { string Format(string code, IReadOnlyList<KeyValuePair<string, string>> arguments, CultureInfo culture); }

// ---- spec 0019 (inbox) — namespace Tellma.Core.Abstractions.Inbox
public sealed record InboxDraft(int UserId, string Type, string Title, string? Body, string? Entity, object? EntityId, IReadOnlyDictionary<string, string>? Data);
public interface IInboxWriter { void Append(DbBatch batch, IReadOnlyList<InboxDraft> drafts); Task NudgeAsync(IReadOnlyList<int> userIds, CancellationToken cancellationToken); }

// ---- spec 0016 (blobs) — namespace Tellma.Core.Abstractions.Blobs
public interface IHasImage { string? ImageId { get; set; } }      // the wire carries the staging token in ImageId for a new upload
public interface IBlobStaging { void AppendConfirm(DbBatch batch, IReadOnlyList<string> tokens); Task DeleteAsync(IReadOnlyList<string> blobIds, CancellationToken cancellationToken); }

// ---- spec 0010 (feature composition, minimal fidelity)
public interface ITellmaFeature { void Declare(FeatureDeclaration declaration); void Contribute(FeatureContribution contribution); }
public sealed class FeatureDeclaration { public void Requires<TFeature>() where TFeature : ITellmaFeature; public StackBuilder Entity<TEntity>() where TEntity : class; }
public sealed class StackBuilder { public StackBuilder WithBehavior<TBehavior>(); public StackBuilder WithOperations(StackOperations operations); }
```

---

## 4. Schema

This theme owns no table of its own; it owns the columns each capability projects onto an entity's
table, the platform-owned tree column, and the reserved SQL error numbers (D19). Column blocks below
are per capability; the worked `gl.Centers` block shows them composed. Table names are plural,
schema-qualified (`core.Users`, `gl.Centers`), matching the architecture document; logical entity
names are singular.

**Audited (every top-level entity, via `Entity<TKey>`)**

```
CreatedAt      datetime2(7)   NOT NULL                        -- UTC, from @tm_Now of the inserting batch
CreatedById    int            NOT NULL  FK core.Users(Id)     -- no cascade; named FK_<Table>_CreatedById
ModifiedAt     datetime2(7)   NOT NULL                        -- UTC; the concurrency token; bumped only by save/actions/import
ModifiedById   int            NOT NULL  FK core.Users(Id)
-- no index; read by primary key only
```

**Activatable (`IActivatable`)**

```
IsActive       bit            NOT NULL  CONSTRAINT DF_<Table>_IsActive DEFAULT (1)
-- no dedicated index; the default filter applies to master data; a filtered index is a per-entity choice in spec 0011/0017
```

**Tree (`ITreeEntity<TKey>`)**

```
ParentId            <TKey>       NULL      FK <same table>(Id), no cascade, FK_<Table>_ParentId; index IX_<Table>_ParentId
Node                hierarchyid  NULL      -- platform-owned shadow column, excluded from the UDTT, written only by the recompute;
                                           -- UNIQUE filtered index UX_<Table>_Node ON (Node) WHERE Node IS NOT NULL (depth-first)
SubtreeCount        int          NOT NULL  CONSTRAINT DF_<Table>_SubtreeCount DEFAULT (1)        -- self + descendants
ActiveSubtreeCount  int          NOT NULL  CONSTRAINT DF_<Table>_ActiveSubtreeCount DEFAULT (1)  -- active self + active descendants; = SubtreeCount when not activatable
-- Level is level() = Node.GetLevel(); IsLeaf is SubtreeCount = 1; neither is stored.
-- Breadth-first index (Level persisted computed, Node) only if level() predicates are expected.
```

**Record + blobs (`IHasImage`, spec 0016)**

```
ImageId        nvarchar(64)   NULL      -- blob id after confirmation; the wire may carry a staging token here on save
```

**Worked example — `gl.Centers`**

```
Id                  int           NOT NULL  PRIMARY KEY (app-assigned from gl.sq_Centers)
Name                nvarchar(255) NOT NULL
Name2               nvarchar(255) NULL
Name3               nvarchar(255) NULL
Code                nvarchar(50)  NOT NULL  UNIQUE (UX_Centers_Code)            -- natural key
CenterType          varchar(20)   NOT NULL  CHECK (CenterType IN ('Abstract','BusinessUnit','Service','Operation','Sale'))  -- enum as string (spec 0011)
IsActive            bit           NOT NULL  DEFAULT (1)
ParentId            int           NULL      FK gl.Centers(Id); IX_Centers_ParentId
Node                hierarchyid   NULL      UNIQUE filtered (UX_Centers_Node)   -- platform-owned
SubtreeCount        int           NOT NULL  DEFAULT (1)
ActiveSubtreeCount  int           NOT NULL  DEFAULT (1)
CreatedAt           datetime2(7)  NOT NULL
CreatedById         int           NOT NULL  FK core.Users(Id)
ModifiedAt          datetime2(7)  NOT NULL
ModifiedById        int           NOT NULL  FK core.Users(Id)
-- UDTT [gl].[CentersList_<hash8>]: every column above except Node; Id mirrored as key
```

**Reserved SQL error numbers** (`THROW`, severity 1, state 1; message text is a stable token):
50001 `session-inactive`, 50002 `permissions-stale`, 50003 `rls-post-check`, 50004 `concurrency`,
50006 `not-found`. 50005 is reserved. Platform raw statements use the `@tm_` variable namespace;
compiled queries use `@qx{b}_`; distribution raw SQL must use neither.

---

## 5. Answers

| Brain-dump question (abridged) | Answer | Where |
|---|---|---|
| Write-once columns: two UDTTs (create/update) or a service rule? | Service rule: `[WriteOnce]`, compared against the existing row loaded in round A; one UDTT. | D6 step 5, §3.8 |
| Does the emitter need to know upsert vs synchronize? | The pipeline tells it: `SaveSpec.Mode` for top-level rows; children are always synchronized under their parents. | D18, §3.8 |
| Id ranges: hitch a ride on connect / validation? Who maintains? Un-consume on failure? Who assigns? | Yes — the reservation rides round A when the buffer is cold; a singleton allocator (spec 0011) maintains ranges; no un-consuming; the service pipeline assigns in step 5 and rewrites temporary ids. | D6 |
| Keep the `Search` parameter? Picker-vs-page hint? | Keep; convention-driven searchable columns; no hint. | D5 |
| Details: Queryex or raw SQL? | Platform-derived compiled queries for entity, children, related, echo; raw SQL only through `Extras`. | D16 |
| Injectable validators that participate in the batch load? Dedup of context queries? | `SaveValidation` with DataLoader-style loads; dedup by (entity, key, select) with key union and select union. | D7 |
| Where does the transaction begin and end? | Begins at the first persist statement, ends at `COMMIT`, both inside the round-B batch text. | D8 |
| Collapse DB calls #1 and #2 with cached permissions; re-run when stale? | Yes; the guard aborts the batch before any business statement when the tag is stale; re-run once. | D6, D22, seam 16 |
| Base service class vs composition? | Composition: platform `EntityService` + author `EntityBehavior` + capability interfaces. | D1 |
| 5 ids requested, 4 found: 4 or 404? | Bulk read returns 4 plus `Missing`; single read is 404. | D4 |
| Capability boilerplate reuse while staying flexible? | Interface on the entity → `CapabilityRegistry` projections; `[EntityAction]` for custom logic; `StackDescriptor` as the single output. | D12, D23 |
| Alternative to RowVersion, implementable in C#? | `ModifiedAt` bumped only by user-visible mutations; compared inside the persist batch; `OverrideConcurrency` flag. | D11 |
| Do weak entities need `SavedById`? | No. | D11 |
| HierarchyId maintenance: in memory or SQL appended to the save? | SQL appended, scoped to affected roots, id-based paths; cycles in C#. | D15 |
| How to model `CenterType`? | C# enum stored as a string with a CHECK (spec 0011 convention); Queryex compares `'Service'`. | §4 |
| Best convention for the permission `Resource`? | The entity's logical name; lowercase verb actions; `all`. | D20 |
| API design that makes forgetting to secure an endpoint difficult? | Every endpoint is projected from a descriptor that names its securable; the web spec's startup audit refuses any tenant endpoint without one. | D23, seam 3/6 |
| Exceptions: enumerate or interface? | Closed set of `TellmaException` types; the web layer maps by type. | D19 |
| Notification without another round trip? | `PersistContext.Notify` appends the inbox insert to round B; the nudge is post-commit. | D10 |
| Extensibility: services on interfaces so distros can replace entities; validate the interface matches the DB? | Open generic pack behaviors closed over the leaf; capability interfaces only; no DB check needed (class = table). | D1, D17 |
| Custom endpoints on `UserService` (invite, self-service, test notification, preferences)? | Bulk ones are `[EntityAction]`s (`invite`); self-service calls are ordinary methods on the pack's `UserService<TUser>` that compose `IEntityService` and `DbBatch`, mapped by the pack's feature (spec 0017). | D12 |
| "Even though Save admits a single entity, the reused part is bulk" | Save admits an array everywhere; the details page sends one. | D2 |

---

## 6. Seams

1. **Batch abstraction (spec 0011 owns).** Needed: `DbBatch` as in §3.8 — compiled queries with
   materializers, `AddSave(SaveSpec)`, raw `SqlStatement` with a `Writes` list, id reservation,
   `BeginTransaction()`/`Commit()` markers emitting T-SQL, per-statement `mayRetry`, results
   addressed by handle, an executor that (a) re-runs a whole batch on transient errors when every
   statement allows it or the batch is one transaction that had not committed, (b) maps the
   reserved 5000x numbers and 2601/2627/547/530 through a registry, (c) reads a result set that
   precedes a THROW (the concurrency conflicts, the tag row), (d) records `tellma.data.roundtrips`
   with the `data.operation` tag this pipeline sets. Concatenated text with `NextResult()`, never
   `SqlBatch`.
2. **Entity class vs wire shape (spec 0011 owns; consumed).** Position: one entity class;
   `[NotMapped]` child collections declared with `[Children(parentKey)]`; `[ServerOwned]` and
   `[WriteOnce]` on properties; server-owned values are cleared by this pipeline before validation
   and rewritten from the DB or computed, except `ModifiedAt`, which is read as the expected stamp.
   `Id <= 0` means new; negative ids are batch-local references. Risks named: a client that omits
   `ModifiedAt` gets a conflict on the first save (treat null as "no check" only for `Source =
   Import` with `InsertOnly`); a write-once property the client omits reads as changed to null —
   the pipeline treats *absent-or-default* on write-once as "keep the existing value".
3. **One capability, declared once (this theme owns).** The contract is D12 + `StackDescriptor`:
   spec 0011 supplies the interfaces and column conventions, spec 0013 registers the securables the
   descriptor lists, spec 0015 projects endpoints from `Operations`, `Capabilities` and `Actions`,
   spec 0019 reads nothing here. Every consumer reads the descriptor; none re-derives.
4. **Queryex schema per tenant (spec 0011 owns; consumed).** Needed: `IQueryexHost` with a schema
   whose identity changes when languages change (so `Name2` disappears and caches invalidate); the
   `TreeNode` mapped to the shadow `Node`; the engine amendments listed in §3.8 (`Restriction`,
   `CountCap`, `Sink`, `KeyListTypes`, `level()`); weak-entity path rewriting is spec 0013's and
   not used by this pipeline (children are never query roots here).
5. **Version tags (spec 0012 owns; consumed).** Needed: the emitter bumps the tag of every table a
   statement declares it writes (`SaveSpec` tables and `SqlStatement.Writes`), so this pipeline
   never bumps manually; the connect guard returns the tags in its first result set; `Guid` tags.
6. **Feature composition (spec 0010 owns; consumed).** Needed: `FeatureDeclaration.Entity<T>()`
   returning a `StackBuilder`, behavior discovery by convention with `WithBehavior<T>()` override,
   `Requires<T>()` only, one aggregated startup validation into which the stack registry reports its
   own failures. The module package and the distribution use the same `Entity<T>()` call.
7. **Natural keys (spec 0011 owns).** Position: `[NaturalKey]` on one or more properties; inference
   order when absent: unique required `Name`, else unique required `Code`, else the first unique
   required string, else the first unique string, else none (export-for-import then refuses with a
   clear error rather than falling back to surrogate ids across tenants). The descriptor exposes it.
8. **Background-task columns and lease statements (spec 0019 owns).** Not consumed by this
   pipeline; a task entity is an ordinary stack whose lease columns are `[ServerOwned]` and bumped
   by the runner's own statements, which must never touch `ModifiedAt`.
9. **Request context (spec 0010 owns; consumed).** `IRequestContext` as in §3.8, scoped, copied
   into job scopes; this pipeline binds `today()` from `Today`, `me()` from `UserId`, the zone
   from `TimeZone`, and never reads `HttpContext`.
10. **Platform exceptions (this theme owns the types; spec 0015 maps).** D19's set and the
    suggested statuses; `ValidationException.Errors` paths are CLR-named and the web layer applies
    its JSON naming policy.
11. **Permission evaluation (spec 0013 owns; consumed).** `IPermissionEvaluator` as in §3.8, with
    the bespoke criteria parameter, the cached tag, and reload; `save` implies `read`; empty grants
    compile to `false`. The `activate` securable is registered by the capability with
    `FilterRootEntity = <entity>`.
12. **Blob staging tokens (spec 0016 owns; consumed).** `IBlobStaging.AppendConfirm` rides round B
    for every `IHasImage` row whose `ImageId` is a token; `DeleteAsync` runs in `AfterCommitAsync`
    for replaced ids; the sweep is spec 0019's consumer.
13. **Wire shapes (spec 0015 owns; consumed).** `QueryResult.Rows` as arrays of arrays;
    `DetailsResult` and `SaveResult` as envelopes; `RelatedEntities.ByEntity` as a nested object;
    `ValidationError` in the RFC 9457 `errors` dictionary keyed by path; the concurrency conflicts as
    an extension member of the 409 problem.
14. **Telemetry (this theme names its own; spec 0011 owns the DB-call budget).** D21.
15. **Notification enqueue (spec 0019 owns; consumed).** `IInboxWriter.Append` and `NudgeAsync` as
    in §3.8; drafts collected during `ContributeToPersist` and `ActionContext.Notify`.
16. **Connect-call collapse (spec 0013 and this theme).** Position: `IConnectStatement.Prepend`
    is the first statement of the first batch of every request and of every persist batch
    (`assertOnly` on the second, which skips the activity stamp). It selects the tag row, then
    `THROW 50002` when the caller's cached permissions tag differs, `THROW 50001` when the subject
    has no active user. The executor exposes the tag row even when the batch throws; the pipeline
    reloads permissions, rebuilds, re-runs once, and counts `tellma.crud.connect.reruns`. The
    activity stamp is throttled server-side (`IF LastActive < DATEADD(minute, -5, @tm_Now)`) so a
    read does not write.
17. **Vocabulary.** `CreatedAt/CreatedById/ModifiedAt/ModifiedById`; plural schema-qualified
    tables; singular logical names; `int` keys by default, `long` opt-in through `Entity<long>`;
    "securable" for the tuple; "action" for the verb; "stack" for the unit; "behavior" for the
    author's class; "version tag" (not etag, not fingerprint) for cache stamps.

---

## 7. Departures

1. **`Tellma.Core.Abstractions` references `Tellma.Core.Queryex`.** The architecture's dependency
   graph has no such edge and the Abstractions README says "no package references, ever". The CRUD
   contracts need `FilterTree`, `QuerySpec`, `QueryexType` and `QueryexColumn`; duplicating them as
   wire mirrors would create the two-implementations drift the engine's `FilterTree` exists to
   prevent. Queryex depends on nothing, so the edge carries no transitive weight — the README's
   stated reason for the rule. The graph gains `CoreAbs → Queryex`.
2. **Endpoint verbs.** The architecture projects read → GET and delete → DELETE; this pipeline is
   verb-agnostic, but its request shapes (Queryex text, filter trees, argument lists) assume bodies,
   which is the all-POST web surface spec 0015 decides. Recorded here because the descriptor's
   `Operations` is what the projection reads.
3. **A shadow property on a mapped entity (`Node`).** The architecture forbids shadow properties
   on mapped entities (the analyzer targets implicit FK shadows). `Node` is an explicit,
   platform-configured, platform-written column that no C# code reads; keeping it off the class is
   what keeps `hierarchyid` out of Abstractions and out of the save path. The rule should be
   narrowed to "no *implicit* shadow properties". Review flag: the alternative is a BCL `TreeNode`
   struct property with a platform value converter (spec 0011's call).
4. **Feature composition at minimal fidelity.** Only `Requires` edges, no `Recommends`/`Excludes`,
   no manifest source generator, no Builder tool; behavior discovery by runtime reflection. Aligned
   with the breakdown's "first release builds it at minimal fidelity".
5. **"Pack code that needs lines issues two explicit bulk queries and stitches in C#."** The
   details read stitches inside the platform in one round trip (several result sets); pack code
   never writes the stitch. Same principle, moved into Core.
6. **Validation "class ↔ table consistency check" and the extensibility note.** No such check
   exists; the class is the table. Aligned with the architecture, contradicting the brain dump.
7. **Audit vocabulary.** The architecture is silent; the brain dump uses two vocabularies. One is
   chosen (D11).

---

## 8. Verification

Facts relied on from the research digest and research files (all verified 2026-09-01 unless noted):

- `MERGE` is out (two surviving defects on temporal targets and DELETE-action-under-indexed-view);
  separate UPDATE/INSERT/DELETE; app-assigned ids remove the upsert race — research
  `data-access.md` §4, `service-pipeline.md` §6.3.
- SqlClient's retry is inert inside any transaction; `SqlBatch` has no `RetryLogicProvider` and
  exposes only the last statement to OpenTelemetry; concatenated text with `NextResult()` is the
  batch mechanism; distributed transactions throw on Linux; `TransactionScope` defaults are wrong
  for this pipeline; `SET XACT_ABORT ON` rolls back the whole transaction on any run-time error and
  `THROW` honours it — `service-pipeline.md` §2–3.
- RCSI is on by default on Azure SQL, off on-prem; C#-side uniqueness checks are write-skew-prone
  under both; unique indexes plus 2601/2627 mapping are the guarantee; a read-then-write pair in one
  transaction is unprotected, so the transaction may start at the persist batch; the stamp check
  inside the UPDATE is a correct row-level conflict check — `service-pipeline.md` §6, `data-access.md`
  §8.
- `OUTPUT` rows are returned to the client even if the statement later fails; period columns are
  shadow properties in EF 10; every temporal UPDATE writes a history row — `data-access.md` §2.
- .NET 10 built-in validation is a per-parameter endpoint filter, synchronous, first-level-only,
  PascalCase keys (dotnet/aspnetcore #61764 open); `Validator.TryValidateObject(…,
  validateAllProperties: true)` evaluates all properties; the `Lines[3].Quantity` path grammar is
  shared with FluentValidation; FluentValidation 12.1.1 has no batching; GreenDonut 16.6.2 batches
  per loader only — `service-pipeline.md` §1, §4.
- `hierarchyid` cannot ride a TVP without `Microsoft.SqlServer.Types.SqlHierarchyId`; the
  documented bulk pattern builds paths in a recursive CTE and casts once; `GetReparentedValue` is
  per moved root; depth-first unique index on `Node`; the EF HierarchyId package requires SqlClient
  ≥ 6.1.6 — `data-access.md` §1, `core-gl-stacks.md` §2.
- Legacy Tellma `dbo.Centers` used `Node UNIQUE CLUSTERED`, computed `Level`, trigger-maintained
  `IsLeaf`, and the `CenterType` set including `Abstract`/`BusinessUnit` with the "only those may
  have children" check — `core-gl-stacks.md` §5.
- OpenTelemetry DB conventions are stable; the SqlClient instrumentation emits one span per
  command; counting round trips in the executor plus `RetrieveStatistics()["ServerRoundtrips"]` in
  tests is the recommended budget mechanism — `service-pipeline.md` §5, `data-access.md` §9.
- Staged uploads are the pattern of every surveyed SaaS API; the app-side orphan sweep is mandatory
  — `blobs.md` §5–6.
- `AsyncLocal` must not carry tenant context; a scoped holder copied into job scopes — `host-tenancy.md` §6.
- An app-generated `Guid` tag is restore-safe and bumped only by the statements that declare a
  write; `rowversion` moves on bookkeeping — `users-roles-permissions.md` §4.
- Queryex facts read from the frozen spec and the code: `CompileQuery` is the only door to SQL (no
  fragment API); `FilterTree.Or([])` is false; `@qx{b}_` namespaces; `in` takes a literal list (no
  TVP); `descendantOf`/`ancestorOf` exist, `level()` does not; `contains`/`startsWith`/`endsWith`
  are `CHARINDEX`-style and total; `Validate` requires the language-version stamp; caches key on
  schema identity — spec 0008 §1.4, §2, §3, §9.4, §10.6, §10.9, §10.10, §13.1, §13.4, §15–17.
- Table-type facts: `[TableType]` opt-in, per-property `[ExcludeFromTableType]`, physical name
  `<Logical>_<hash8>` addressed only through `model.GetTableTypes()`, the four bulk lists as plain
  classes, metadata-driven binding with the ordinal analyzer deferred to this exercise — spec 0001
  §5–6, `BulkLists.cs`.

Verified by me on 2026-09-01 (primary sources):

- **C# 14 shipped with .NET 10** with extension members (`extension` blocks incl. static extension
  members and properties), the `field` keyword, null-conditional assignment, `nameof` on unbound
  generics — learn.microsoft.com "What's new in C# 14" (ms.date 2025-11-18). Implication: typed
  capability shortcuts can be plain constrained extension methods; nothing here needs the new
  syntax, so the contracts stay readable on older tooling.
- **`JsonIgnoreCondition.WhenReading` (5) and `WhenWriting` (4) exist** in the .NET 10 API
  surface (enum page, updated 2026-05-27). Implication for seam 2: server-owned properties could be
  ignored on read by attribute, but this proposal deliberately does *not* do that — `ModifiedAt`
  must be read as the expected stamp — and clears server-owned values in the pipeline instead. The
  version that introduced the two members was not re-verified (believed .NET 9).

Unverified or open:

- The "dispatch when every validator task is complete or waiting on a load" scheduler (D7) is a
  design, not a measured implementation; it needs a property test that validators awaiting foreign
  tasks still terminate and never dispatch early.
- Whether `INSERT INTO @t … OUTPUT inserted.* SELECT …` (the key-table sink with rows) keeps the
  compiled query's `ORDER BY … OFFSET/FETCH` semantics on the OUTPUT result set (OUTPUT order is
  not guaranteed by SQL Server; the page rows may need to be re-read from the key table with the
  ordering re-applied — a spec 0011 detail).
- Whether a self-referencing FK tolerates deleting a parent and its children in one `DELETE`
  statement in every case (constraint checking at statement end is documented behaviour for
  single statements; test on LocalDB with the tree fixture).
- Relative cost of the `IsDescendantOf` `CROSS APPLY` count refresh versus a `ParentId` CTE at
  Center scale (measure in the spec 0011 fixture tests).
- Whether `Properties<Enum>()` pre-convention configuration matches nullable enum properties
  (spec 0011; affects `CenterType?` if ever nullable).
