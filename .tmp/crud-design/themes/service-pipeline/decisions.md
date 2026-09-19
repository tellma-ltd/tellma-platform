# CRUD service pipeline and capabilities — decisions (theme `service-pipeline`, future spec 0014)

The settled design of the reusable service every top-level entity gets for free: the service base
and its composition, the standard operations, the save pipeline, the validation framework, the
capability recipes, concurrency, the closed exception set, and what the pipeline needs from the
other nine themes. Written for a reader who has seen none of the proposals or the brain dump.
Contract blocks use the platform's contract notation (names are normative; shape is described,
not transcribed); SQL is the exact shape to emit.

Vocabulary fixed here and used throughout: a **stack** is one top-level entity's CRUD feature; a
**securable** is a `(resource, action)` pair a permission grants; a **tag** is an opaque cache
version stamp (`PermissionsTag`, `SettingsTag`); the four **audit columns** are `CreatedAt`,
`CreatedById`, `ModifiedAt`, `ModifiedById`; a **round trip** is one command executed on one
connection; tables are plural and schema-qualified (`core.Users`, `gl.Centers`), logical entity
names singular (`User`, `Center`).

---

## 1. Critique

### 1.1 The general design is right; its ordering and its cost model are not

The brain dump's skeleton — one bulk-shaped service per top-level entity, a save pipeline of
preprocess → validate → persist → side effects, row-level security composed as a `FilterTree`,
validators loading context in one batch, capability endpoints projected from a few shared
patterns, an intent-oriented MCP surface — is the correct shape and survives intact. Five things
inside it do not:

1. **The transaction opens before validation** ("Start the transaction (is this right?)"). It is
   not. Azure SQL runs `READ_COMMITTED_SNAPSHOT` by default and the migrator turns it on for
   on-premise databases, so a read inside an open transaction is a statement-level snapshot that
   the transaction protects in no way; on a database without RCSI it takes shared locks across
   client round trips. The transaction is the persist batch and nothing else, and every invariant
   that must hold at write time — concurrency stamps, the permissions tag, the row-level post-check,
   uniqueness, tree cycles — is re-checked inside that batch (D13).
2. **Four round trips for a save, two of them avoidable.** Connect, the row-level pre-check, the
   validation context, the persist. The connect facts ride the first batch as a guard; the
   pre-check *is* the "load the existing rows" statement that validation needs anyway. Common
   case: one round trip for every read, one for a create, two for an update (D27).
3. **"Pre-commit non-transactional side effects" should not exist.** Its only instance is blob
   creation, and staged uploads (upload through the blob endpoint first, save the record with the
   returned token, sweep unconfirmed blobs) move the blob write before the pipeline and the delete
   after commit. The pipeline has exactly two side-effect phases (D16).
4. **The draft never says what the distribution author writes.** Under this design a plain entity
   costs one entity class and one line of composition; business logic is one service class per
   entity that derives from a platform base whose operations are fixed and whose hooks are the
   only things an author overrides (D2).
5. **Cached permissions are safe for reads and unsafe for writes as drafted.** A tag read at the
   top of a batch can change before the persist transaction commits. The persist prologue takes an
   update lock on the caller's own stamp row, the row every permission-changing write must bump,
   so a role save and a member's in-flight save serialize (D13).

### 1.2 Security holes the draft leaves open

- **`Save` bypasses `Activate`.** `IsActive` is a column the save writes, so a user with `save`
  but not `activate` flips activation by editing the record. Capability-owned columns are
  *diff-gated*: changing one through save requires the capability's grant on that row (D11).
- **Child synchronization can re-parent rows.** A client-supplied child id that belongs to another
  parent (or to a parent the caller cannot see) must be rejected, never adopted (D12).
- **Write-once columns silently reset** would make an import "succeed" while discarding a value
  the sheet carried. A changed write-once value is a validation error (D11).
- **`DeleteByQuery` is called dangerous and given no guardrails**: it needs a non-empty filter, a
  row cap, a dry run, and no MCP exposure (D17).
- **A query whose Queryex text fails to compile has no outcome** in the draft; it is a 400 with
  the engine's diagnostics (D23).

### 1.3 Detailed choices

- `MERGE` is out (two surviving engine defects); the emitter emits separate `UPDATE`/`INSERT`/
  `DELETE`, and app-assigned ids remove the upsert race, so no `HOLDLOCK`.
- `toggle_activate` as a permission action becomes one securable `(entity, activate)` covering
  both directions, with two actions `activate`/`deactivate` (D19).
- "Save admits a single entity" contradicts the guiding principle; the service takes an array,
  the details page sends an array of one (D4).
- `SavedById` on weak entities is redundant; children carry no audit columns and no stamp; the
  parent's `ModifiedAt` guards the aggregate and is bumped by any child change (D14).
- Two audit vocabularies (`SavedAt/SavedById` + period columns on temporal entities, four columns
  on non-temporal) become one: the four audit columns on every top-level entity, system
  versioning an additive table option (seam 17).
- `Center.IsLeaf` is `SubtreeCount = 1`; `Level` is a persisted computed `Node.GetLevel()` that
  `level()` reads; neither is authored (D20).
- "Does the emitter need to know upsert vs synchronize?" — no flag: top-level rows are upserted,
  nested collections synchronized under their parents; the save graph says which is which (D12).
- "Queryex or raw SQL for details?" — both, by role: user- or permission-authored text compiles
  through Queryex; keyed reads (by id, by parent id, by id set) are model-emitted parameterized
  SQL. Mixing them in one command is what `BatchOrdinal` exists for (D18).
- "5 ids requested, 4 found" has three answers: a bulk read returns the four and names the
  missing; a single read is 404; a delete is all-or-nothing (D4, D17).
- The count "stops beyond 9999" must be a `TOP (@cap)`-wrapped count, not `count()` over the whole
  filtered set; the cap is 10 000 (D5).
- Ancestors for the tree view in one round trip need a list restriction on the engine (the page's
  ids captured into a table variable restrict the ancestor query) — an engine amendment spec 0011
  documents (seam 4).
- Id reservation "hitching a ride" is unnecessary in the common case: the allocator prefetches in
  the background, so a warm buffer costs nothing and a cold one is a rare, metered extra trip (D9).
- Import through the same pipeline has one consequence the draft misses: the persist statements'
  text is identical for one row and fifty thousand, and table-variable deferred compilation caches
  the first execution's plan. Large batches run in a separate plan lane (D13).
- The extensibility note "validate that the interface matches the DB" is moot: the class is the
  table; a distribution leaf inheriting a pack default cannot drop a pack column.

### 1.4 Internal inconsistencies

- Step 2 (no write permission → forbidden) and step 8 (row-level post-check → forbidden) are kept
  with distinct codes: `Permission.Missing` before any round trip; `Rls.PostCheck` inside the
  transaction with a message that names what happened.
- "Queuing background operations IS transactional" and "notifying a user rides the save" describe
  the same hook the draft has no place for: `ContributeAsync` on the persist batch (D16).
- The MCP goal wants every regular user action exposed while the service section exposes only
  CRUD; `[EntityAction]` methods close the gap by projecting custom actions into the same
  descriptor the endpoints and tools are generated from (D3, D19).
- Temporary ids: import must reference "a parent created in the same import" and the UI a new
  sibling; the draft says nothing. `Id < 0` is a batch-local temporary id (D9).

---

## 2. Decisions

### D1 — Placement: contracts in `Tellma.Core.Abstractions`, pipeline in `Tellma.Core`

**Decision.** Everything a module or distribution touches lives in `Tellma.Core.Abstractions`
under `Tellma.Core.Abstractions.Crud` (the service base, requests and results, capability
interfaces and annotations, the stack descriptor, the persist and details contexts),
`Tellma.Core.Abstractions.Validation` (validators, the context loader, errors), and
`Tellma.Core.Abstractions.Errors` (the closed exception set). Securable shapes are consumed from
`Tellma.Core.Abstractions.Security` (spec 0013). The implementation — `EntityPipeline<TEntity,
TKey>`, the operations, the query planner, the validation scheduler, the capability registry — is
`Tellma.Core` under `Tellma.Core.Crud`. `Tellma.Core.Abstractions` takes a package reference on
`Tellma.Core.Queryex` so that `FilterTree`, `QuerySpec`, `QueryexColumn`, `QueryexType`, and
`QueryexDiagnostic` appear in contracts (departure 1).

**Rationale.** `Tellma.Module.<m>` never references `Tellma.Core`; `CenterService` lives in the GL
module, so the class it derives from must be in Abstractions and framework-free. Queryex depends on
nothing (verified: its project file has no package or project reference), so the new edge carries
no transitive weight.

**Rejected.** A separate `Tellma.Core.Crud.Abstractions` package (the stack is how every entity
works, not an opt-in); mirroring `FilterTree` in Abstractions (two types for one concept).

**Confidence.** High.

### D2 — Composition: a thin service base as the authoring surface, a sealed pipeline underneath, composable components for multiplicity

**Decision.** Three parts:

1. **`EntityService<TEntity, TKey>`** (Abstractions, non-abstract, non-sealed) owns the public
   operations of D4 as non-virtual members that forward to the pipeline, and a closed set of
   `protected virtual` hooks with empty defaults: `PreprocessAsync`, `ValidateAsync`,
   `ValidateDeleteAsync`, `ContributeAsync`, `AfterCommitAsync`, `ContributeDetails`,
   `SearchFilter`, `BespokeGrant`. A stack with no logic registers the base itself; a stack with
   logic derives once (`CenterService : EntityService<Center>`). Custom bulk actions are
   `[EntityAction]` methods on the derived class (D19); non-bulk custom operations (a self-service
   profile update) are ordinary public methods on the same class that compose the pipeline's
   batch, mapped by the owning feature (spec 0015/0017).
2. **`IEntityPipeline<TEntity, TKey>`** (Abstractions interface; `Tellma.Core` sealed
   implementation, one closed generic per stack) runs every operation under the standard rules:
   connect guard, permission check, validation rounds, the persist transaction, effects,
   telemetry. A distribution cannot override or skip a step.
3. **Composable components** for concerns with multiplicity — `IEntityValidator<TEntity>`,
   `ISaveEffect<TEntity>`, `IDetailsContributor<TEntity>` — registered in DI by packs,
   compliance libraries, and distributions. They are contravariant (`in TEntity`) and the
   pipeline resolves them for the leaf and every base type up to `object`, once per stack at
   startup, in registration order; the service's own hooks run first. A pack's validator
   registered against `Invoice` therefore keeps running when a distribution extends `Invoice`.

Pack logic is reused with an extended entity by shipping the pack service generic over the leaf:
`UserService<TUser> : EntityService<TUser, int> where TUser : User`; the distribution registers
`UserService<User>` unchanged, `UserService<MyUser>` with its own leaf, or derives
`UserService : Core.UserService<MyUser>` to add a rule and calls `base.` in each override. Pack
code is written against the pack's entity class, never a paired interface.

**The worked recipe** — an entity with activation and a tree:

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK, sequence `sq_Centers` | |
| `Name` | `nvarchar(255)` | no | | searchable |
| `Name2` | `nvarchar(255)` | yes | | searchable, gated by tenant language |
| `Name3` | `nvarchar(255)` | yes | | searchable, gated by tenant language |
| `Code` | `nvarchar(50)` | no | unique `UX_Centers_Code` | natural key, searchable |
| `CenterType` | `varchar(20)` | no | CHECK on the enum's names | enum stored as string |

The entity carries `AuditedEntity`, `ActivatableEntity`, and `TreeEntity` (D19); the class
declares `[Table("Centers", Schema = "gl")]`, `[NaturalKey]` on `Code`, and implements
`IActivatable` and `ITreeEntity<int>`. Composition is one line:

*Illustration*
```csharp
declaration.Entity<Center>();                 // or .Entity<Center, CenterService>() when a service class exists
```

Fourteen lines of entity, one line of composition, zero lines of service, endpoint, permission, or
MCP code. What the author gets: query (search, paging, capped count, ancestors), details (related
dictionary, extras, row echo), save (array, upsert plus child synchronization, audit stamping,
concurrency, ids, tree recompute, cycle validation), delete by ids, by query, with descendants,
activate/deactivate, get by parent ids, the securables `(gl.Center, read | save | delete |
activate)`, the projected endpoints and MCP descriptors, the default filter `IsActive = true`, and
a conformance test base (D28). The one GL rule (only grouping centers may have children) is a
ten-line `CenterService`.

**Rationale.** A base class is what a coding agent expects to write against and what the brain
dump already names (`UserService`, `RoleService`, `CenterService`); non-virtual operations over a
sealed pipeline remove the fragile-base-class risk (no `override SaveAsync` that forgets the
post-check). Hooks take one context object each, so a platform minor adds members to a context,
never parameters to a hook (D29). DI components cover the one case inheritance cannot: a library
that cannot subclass the distribution's service but must add a rule.

**Rejected.** Pure composition (a sealed service plus a separately registered behavior class):
equally sound, but every entity with logic then has two types and the pack's non-bulk endpoints
need a third. Rich inheritance (every step virtual): the fragile base class. A `Tellma.Core`
base class: modules cannot reference it. Convention discovery of the service class (scan for
`: EntityService<Center>`): rejected in favour of explicit registration with a startup diagnostic
that names any class deriving from `EntityService<T>` that no stack registered — explicit is what
an agent gets right, the diagnostic catches the omission.

**Confidence.** High. **Review flag:** explicit registration plus tripwire versus convention
discovery (review flag 1).

### D3 — Registration and the stack descriptor

**Decision.** A stack is registered by type at composition: `declaration.Entity<TEntity>()` or
`declaration.Entity<TEntity, TService>()` inside a feature (the module's) or directly in
`AddTellma`. The platform derives one `StackDescriptor` per stack at startup from the leaf type
and the service type: logical name, resource, key type, capabilities (the interfaces the leaf
implements), operations (`[Stack(Operations = …)]`, default `All`; `Read` for lookups), actions
(capability actions plus `[EntityAction]` methods), searchable properties, natural key, default
filter, default select, child collections and expandable navigations (from the entity contract),
ceilings, and `[Description]` texts. The descriptor is the single input for endpoint projection
(spec 0015), the MCP catalogue, the SPA's schema metadata, the securables registration (spec
0013), and the conformance tests. It is validated at startup with the entity named: an
`[EntityAction]` on a non-service class, a duplicate action name, a searchable property that is
not a string, a `[GatedBy]` naming an unregistered action, a `[Unique]` without a backing index,
a projected operation without a securable, a service class no stack registered.

**Confidence.** High.

### D4 — The standard operations and their rules

**Decision.** `EntityService<TEntity, TKey>` exposes exactly:

| Operation | Signature (abridged) | Exists when | Securable action | Powers |
|---|---|---|---|---|
| Query | `QueryAsync(EntityQuery) → QueryResult` | always | `read` | search page, reports, pickers, export by query / by ids |
| Details (one) | `GetByIdAsync(id, DetailsRequest) → DetailsResult<TEntity>`; 404 when missing or hidden | always | `read` | details page |
| Details (many) | `GetByIdsAsync(ids, DetailsRequest) → DetailsSetResult<TEntity, TKey>` with `Missing` | always | `read` | export-for-import, import hydration, MCP get |
| Save | `SaveAsync(list<TEntity>, SaveOptions) → SaveResult<TEntity, TKey>` | `Operations` has `Save` | `save` | details page (array of one), import, seeding, actions |
| Delete by ids | `DeleteByIdsAsync(DeleteRequest<TKey>) → DeleteResult` | `Operations` has `Delete` | `delete` | search page multi-select, details page |
| Delete by query | `DeleteByQueryAsync(DeleteByQueryRequest) → DeleteResult` | `Operations` has `Delete`; web surface only | `delete` | the hidden "delete by filter" |
| Action | `ExecuteActionAsync(name, ids, arguments?, ActionOptions) → ActionResult<TEntity, TKey>` | per registered action | the action's `Permission` | activate/deactivate, custom actions, MCP `actions.run` |
| Get by parent ids | `GetByParentIdsAsync(parentIds, EntityQuery projection) → QueryResult` (extension, `TEntity : ITreeEntity<TKey>`) | tree | `read` | tree view (roots plus every expanded node in one call) |
| Delete with descendants | `DeleteWithDescendantsAsync(DeleteRequest<TKey>) → DeleteResult` (extension, tree) | tree | `delete` | "delete with children" |
| Activate / Deactivate | `ActivateAsync(ids)`, `DeactivateAsync(ids)` (extensions, `TEntity : IActivatable`, forward to `ExecuteActionAsync`) | activatable | `activate` | search-page and details-page buttons |

Rules:

- **Bulk reads return what is visible**; `GetByIdsAsync` lists the ids it did not find in
  `Missing` and never throws for absence. `GetByIdAsync` throws `NotFoundException`. Hidden and
  missing are indistinguishable on every path.
- **Export by ids** is `QueryAsync` with `EntityQuery.Ids` set (a key restriction through a TVP);
  no separate operation.
- **Delete by ids is all-or-nothing**: every id must exist and be visible under the `delete`
  grant, otherwise `NotFoundException` carrying the offending ids and nothing is deleted. A
  silent partial delete would hide a concurrent change from the user who selected the rows a
  second ago.
- **Delete by query is guarded**: a non-empty filter (an empty filter is "delete all", never
  exposed); the matched count is bounded by `MaxDeleteByQueryRows` (exceeding it is
  `LimitExceededException` with the instruction to run it as a background task); `DryRun` returns
  the count only; projected to the web surface only, never to MCP or the public API.
- **`Take` is capped** by `MaxTake`; a larger value is `LimitExceededException`, never silently
  clamped (clamping breaks paging arithmetic). `Take` absent means the cap.
- **Count is capped**: `IncludeCount` returns `Count` up to `CountCap` and `IsCountCapped = true`
  beyond it.
- Excel (`ExportBy…`, `Import`) is not on the service; the codec composes `QueryAsync`,
  `GetByIdsAsync`, and `SaveAsync` (D22).

**Rationale.** One class per key type; capability operations as constrained extension methods so
a call on the wrong entity is a compile error rather than a runtime `NotSupported`; the single
read exists because a details page has single-id semantics a bulk read cannot express.

**Confidence.** High. **Review flag:** strict delete versus best-effort with per-id outcomes
(review flag 4).

### D5 — Request and result shapes, and the service-level ceilings

**Decision.** The shapes are in §3.3. `EntityQuery` mirrors `QuerySpec` plus `Search`, `Ids`,
`IncludeCount`, `IncludeInactive`, `IncludeAncestors`, and typed `Arguments`
(`QueryArgument(Name, Type, Value)`; the declaration handed to the engine is
`(Name, Type, IsNotNull: Value is not null)`). `QueryResult` carries the engine's `Columns`,
`Rows` as arrays of scalars, `Count` with `IsCountCapped`, and `Ancestors` in the same column
shape. `DetailsRequest` carries `Expand` (navigation paths whose targets go into the related
dictionary; null means every foreign-key navigation of the entity and its children at depth 1),
`Extras` (names the service declares), and `EchoSelect`. `DetailsResult<TEntity>` carries the
entity with child collections populated, `Related` (logical entity name → id → partial entity),
`Extras`, and `RowEcho`. `SaveOptions` carries `ReturnEntities` (default true), `Details`,
`OverrideConcurrency`, `RequireStamps`, `Source`. Wire encodings are spec 0015's.

Ceilings live on the stack, defaulted by the platform and overridable per entity with
`[Stack(...)]`: `MaxTake = 500`, `CountCap = 10 000`, `MaxSaveCount = 10 000`,
`MaxChildrenPerEntity = 10 000`, `MaxIds = 10 000`, `MaxDeleteByQueryRows = 10 000`,
`MaxExpandDepth = 3`, `MaxValidationRounds = 3`, `MaxSearchLength = 200`, `MaxExtras = 16`,
`LargeBatchThreshold = 1 000`. Exceeding one throws `LimitExceededException` before any round
trip. Per-surface overrides (a smaller MCP page) are spec 0015's configuration. `Select` absent
means the entity's default columns — natural key, `Name*`, `Code`, `IsActive`, audit columns —
the shape pickers and the MCP `query` tool use when they pass nothing.

**Rationale.** Typed arguments avoid running parameter inference on the hot path; ceilings on the
service rather than the web layer make MCP and background paths enforce the same numbers;
`IsCountCapped` lets the UI render "10 000+" honestly.

**Confidence.** High. **Review flag:** `CountCap = 10 000` versus the brain dump's 9 999
(review flag 8).

### D6 — `Search` stays, server-side, over declared columns

**Decision.** `EntityQuery.Search` (≤ `MaxSearchLength`) is lowered by the pipeline into
`FilterTree.Or` over the entity's `[Searchable(Kind)]` properties that exist in the tenant's
schema — each leaf `contains(Col, @tm_search)` or `startsWith(Col, @tm_search)` per the
declaration, `@tm_search` a declared parameter — plus `Id = @tm_searchId` when the text parses as
an integer and the key is integral. When no property is declared searchable the convention is the
properties named `Name`, `Name2`, `Name3`, `Code` that exist, all `Contains`. `Name2`/`Name3`
leaves are omitted when the tenant has no second or third language (the schema no longer declares
them). The final filter is `And(userFilter, defaultFilter, searchFilter, rlsFilter)`. A service
overrides `SearchFilter(search)` for anything else (a document searched by its counterparty's
name). No picker-versus-page hint: the select list already tells the client what to show.

**Rationale.** One declaration next to the columns versus every client re-implementing the same
disjunction and getting language gating wrong; the filter *text* is constant per entity and the
value a parameter, so the plan is cached once; Queryex emits `contains`/`startsWith` without
pattern metacharacters, so the value can only ever be data. `contains` scans; that is the accepted
cost for master data, and a dedicated seekable path is a later feature on the same declaration.

**Confidence.** High.

### D7 — The read path: one round trip, connect riding the batch, stale-tag re-run

**Decision.** `Query`, `GetById`, `GetByIds`, and `GetByParentIds` execute one round trip in the
common case. Each instance keeps a bounded cache keyed `(tenantId, subject)` → `ConnectSnapshot`
(`UserId`, `PermissionsTag`, `SettingsTag`, `UserSettingsTag`) plus the derived permission set
and the tenant's Queryex schema. The batch is:

1. the **connect statement** (spec 0013 owns its text; the pipeline places it first): resolve
   the subject to `UserId` and `IsActive`, stamp `LastActiveAt` on the caller's stamp row at most
   once per minute, return the tags;
2. the business statements, compiled with the **cached** permission set and schema for the cached
   tags;
3. nothing else.

The executor reads result set 1 first. No row or `IsActive = 0`: the remaining result sets are
drained and discarded, the cache entry is evicted, and `SessionInvalidException` is thrown (the
host ends the session; spec 0010). `PermissionsTag` differs from the cached one: results are
discarded, the permission set is rebuilt (spec 0013's evaluator), and the business statements are
re-executed once; `SettingsTag` differs: the schema and settings caches are refreshed, the
compilation redone (the SQL may differ — `Name2` gating), same single re-run budget. A second
mismatch is `StaleContextException` (retryable). Every re-run is counted on
`tellma.crud.stale_context.reruns`. On a cold cache (first request for this subject on this
instance) the connect trip runs first, then the operation.

**Accepted window.** The tag is read at the start of the batch and the query runs microseconds
later; a revocation committing in between is served with the old grant once — the window every
non-locking read has. Writes get no such allowance (D13).

**Confidence.** High. **Review flag:** the one-minute activity throttle (review flag 12).

### D8 — The save pipeline, step by step

**Decision.** `SaveAsync` runs the steps below; "RT" marks a round trip. Common case: one round
trip for a create with no context loads, two for an update; each dependent validation round adds
one, bounded by `MaxValidationRounds`.

| # | Step | Where | What |
|---|---|---|---|
| 1 | Shape and ceilings | pipeline | `entities` non-empty and ≤ `MaxSaveCount`; children ≤ `MaxChildrenPerEntity`; ids: `0` new, `< 0` a batch-local temporary id unique within the batch, `> 0` an update (never an insert); no duplicate ids per entity type in the batch; every `ValidationAttribute` on every entity and child evaluated from cached metadata, **all** failures reported (`IValidatableObject` is not consulted). Any error → `ValidationException` (422) with zero round trips. |
| 2 | Coarse permission | pipeline | The caller holds *some* `save` grant on the resource (cached set for the cached tag); else `ForbiddenException(Permission.Missing)`. |
| 3 | Preprocess | pipeline, then hook | Trim strings and collapse empty to null on nullable strings (`[PreserveWhitespace]` opts out); nested children get their parent key from the nesting (the FK is server-owned for nested children); server-owned properties are noted for overwrite (D11); then `PreprocessAsync` (defaults, derived fields). |
| 4 | Ids | pipeline | New rows receive ids from the allocator's warm buffer (D9); temporary ids are rewritten in every foreign-key property that targets the same entity type. Validators therefore see final identities. |
| 5 | Context round — **RT1** | pipeline + validators | One batch: the connect statement; for every update id the **before image** loaded through the caller's `save` grant (this *is* the row-level pre-check and the write-once and diff-gate source); child before images by parent id for every collection present in the payload; tree ancestor chains for changed parents; staged blob token validation (spec 0016); every load validators declared before their first await (D10). Skipped when nothing needs it (creates only, no blobs, no validator loads). D7's rules apply: inactive → session invalid; stale tag → refresh and re-run RT1 once. |
| 6 | Validate | pipeline + validators | An update id absent from the before images → `ValidationException` `Entity.NotFound` at `[i].Id` (hidden and missing alike); a before image whose `ModifiedAt` already differs from the echoed stamp with `OverrideConcurrency = false` → `ConcurrencyException` now (the authoritative check is step 8); write-once, diff-gate, child identity (D12), uniqueness within the batch and against loaded rows, tree cycles and depth (D20); then the validators' continuations, further rounds **RT1a…** as declared. Any error → `ValidationException`, nothing written. |
| 7 | Assemble the persist batch | pipeline | Guard prologue, concurrency check, emitter statements, capability statements, `ContributeAsync` statements (service hook then `ISaveEffect` components), blob confirmations, inbox and task rows, tag bumps, post-check, read-back, commit (D13). |
| 8 | Persist — **RT2** | executor | One T-SQL transaction. Engine and reserved errors map to platform exceptions (D13); transient errors re-run the whole batch. |
| 9 | Post-commit | pipeline + effects | `AfterCommitAsync` (hook then components): blob deletes, SignalR nudges, best-effort external calls. Failures are logged with the save's trace id and counted on `tellma.crud.effects.failures`; they never change the response. |
| 10 | Response | pipeline | `SaveResult`: ids and, when `ReturnEntities`, the read-back in details shape with `Related` and `RowEchoes`. |

**Rationale.** Every check runs where its input first exists and where its failure is cheapest:
shape and type-level permission before any round trip, existence and visibility in a filtered
read, everything that must hold at write time inside the transaction.

**Confidence.** High.

### D9 — Ids: warm buffer before validation, temporary ids, never un-consumed

**Decision.** The allocator (spec 0011) keeps a background-prefetched range per entity type;
`TakeAsync(entityType, count)` returns synchronously from a warm buffer and performs its own
reservation round trip only when the buffer cannot cover the payload (rare; counted on
`tellma.crud.id.refills`). The allocator may also append a refill to any batch the pipeline
executes so the buffer stays warm without a dedicated trip. Ids are assigned after preprocessing
and before validation so validators reason about final identities. A validation failure does not
return ids to the buffer: gaps cost nothing and un-consuming lets an id seen in a log attach to a
different row later. `Id < 0` in the payload is a temporary id: it must be unique within the
batch, and after allocation the pipeline rewrites every property the entity metadata reports as a
foreign key to the same entity type (`ParentId`, a child's parent key, any other self-typed FK);
a temporary id that nothing defines is `Entity.NotFound` at the referencing path.

**Rejected.** Sizing the reservation from the payload and riding RT1 with it (validators would
then run before ids exist, or RT1 would split in two); un-consuming on failure.

**Confidence.** High. **Review flag:** review flag 9.

### D10 — Validation: a DataLoader over the batch, bounded rounds, codes not prose, indexes as the guarantee

**Decision.**

- **Validators are plain async methods** — the service's `ValidateAsync` hook and every
  `IEntityValidator<TEntity>` component — run concurrently as tasks per round. A validator asks
  for context only through `context.Loader` and awaits a `ContextPromise<T>`. Awaiting an unloaded
  promise *parks* the validator; when every validator is parked or complete, the pipeline
  dispatches **one** batch containing every pending request (in round 1 together with its own
  loads and the connect statement), resolves the promises, and resumes. Each dispatch is one round
  trip and one round; exceeding `MaxValidationRounds` throws `InvalidOperationException` naming
  the validator (an authoring bug, never user input) and is metered. A validator that awaits
  something other than a promise is simply "active": dispatch waits, and the duration histogram
  shows it.
- **Requests are deduplicated by structural key**: the root entity, the key property, the filter
  tree rendered ordinally, the parameter values, the statement text for raw SQL. Key values are
  unioned into one TVP; requests that differ only in `Select` are merged by taking the union of
  selects. A key that answers "not found" is cached for the request like any value. The before
  images and the payload's own rows are primed so a validator asking for them costs no statement.
- **Context loads are not row-level filtered** (a uniqueness rule is global by definition);
  `Loader.Visible<T>(...)` conjoins the caller's `read` grant for rules that must respect
  visibility.
- **Errors are `(Path, Code, Arguments)`**: paths in the ASP.NET grammar with the top-level index
  first — `[3].Lines[1].Quantity`, `[0].Name2`, `Ids[2]` — using CLR property names (spec 0015
  applies its JSON naming policy); codes are stable resource keys the platform and packs publish
  (`Required`, `MaxLength`, `Range`, `Unique`, `Entity.NotFound`, `Entity.DuplicateId`,
  `WriteOnce`, `Fk.NotFound`, `Fk.InUse`, `Tree.Cycle`, `Tree.TooDeep`, `Blob.TokenInvalid`,
  `Concurrency.StampRequired`); arguments are named string pairs that feed ICU MessageFormat;
  spec 0015 localizes and returns both code and message. Every error is collected; nothing stops
  at the first.
- **Uniqueness is guaranteed by the index, explained by the validator.** A `[Unique]` declaration
  on the entity (spec 0011's contract; `[NaturalKey]` implies it) yields from one declaration: the
  unique index `UX_<Table>_<Col>[_<Col>]` (filtered `WHERE <Col> IS NOT NULL` for nullable
  columns), a built-in validator that checks the batch against itself and against loaded rows,
  and the persist-error map that turns 2601/2627 on that index into the same `Unique` error at the
  property path (the race case; the row is located by the duplicate value the engine's message
  carries). A C#-only uniqueness check is write-skew-prone under RCSI and SNAPSHOT alike and is
  not permitted without the index; a model test enumerates `[Unique]` declarations against
  indexes. No validation read takes `UPDLOCK, HOLDLOCK`.
- **Foreign keys are validated by the database**: 547 on `FK_<Table>_<Column>` maps to
  `Fk.NotFound` at the property path on save and `Fk.InUse` keyed `Ids[i]` on delete, with the
  referencing table's canonical name as an argument.

*Illustration* — a distribution validator: unique code and a grouping-parent rule in one round.
```csharp
protected override async ValueTask ValidateAsync(SaveContext<Center, int> c)
{
    var parents = await c.Loader.ByIds<Center, int>(c.Entities.Where(e => e.ParentId > 0).Select(e => e.ParentId!.Value), select: "Id,CenterType");
    for (var i = 0; i < c.Entities.Count; i++)
        if (c.Entities[i].ParentId is int p && (parents.GetValueOrDefault(p) ?? c.Entities.FirstOrDefault(e => e.Id == p))?.CenterType is not (CenterType.Abstract or CenterType.BusinessUnit))
            c.Errors.Add(i, x => x.ParentId, GlErrors.ParentMustBeGrouping);
}
```

**Rationale.** Neither the .NET 10 minimal-API validator (a per-parameter endpoint filter with
early returns, synchronous, PascalCase keys) nor FluentValidation (per instance, no batching) can
validate N entities after one batched load; both agree on the path grammar, which is kept. The
parking scheduler is the DataLoader contract (coalesce, one batch function per tick, missing keys
are explicit absences, manual dispatch) layered on the batch abstraction so that many loaders
share one round trip — the thing neither GreenDonut nor FluentValidation does. Straight-line
async code is what a coding agent writes correctly the first time.

**Rejected.** A two-phase declare/validate interface (every validator becomes a state machine
the compiler could have written); FluentValidation as a dependency (a second rule language, no
batching); the endpoint filter as the engine (it stays enabled for non-entity request DTOs and is
disabled on save endpoints so validation runs once, in bulk, with context).

**Confidence.** High on the contract; medium on the parking scheduler's implementation, which
needs a property test that validators awaiting foreign tasks still terminate and never dispatch
early.

### D11 — Editable, server-owned, write-once, and diff-gated properties

**Decision.** Which properties a client may set is derived from metadata, never from what the
payload contained:

- **Server-owned**: the audit columns, tree-maintained columns (`Node`, `Level`, `SubtreeCount`,
  `ActiveSubtreeCount`), computed columns, the parent key of a nested child, and any
  `[ServerOwned]` property. On update the value comes from the before image; on insert from the
  pipeline's default. A client-sent value is ignored, not an error (an old client during a swap
  may send fields a new server owns). One exception: `ModifiedAt` is read as the expected
  concurrency stamp before being overwritten (D14).
- **Write-once** (`[WriteOnce]`: `User.Subject`, `User.Email`): settable on insert; on update a
  value that *differs* from the before image is `ValidationException` `WriteOnce` at the path.
  Equal or default (the client did not send it) passes. Never a silent reset.
- **Diff-gated** (`[GatedBy("activate")]`, applied to `IsActive` by the activatable capability):
  changing the column through save requires the named action's grant on that row, evaluated over
  the before image with the action's filter; otherwise `ForbiddenException(Gate.<action>)`. The
  capability's own actions are the ordinary path; the gate keeps import and agents honest without
  a second endpoint.
- **Multilingual gating**: a client value for `Name2`/`Name3` when the tenant has no such
  language is ignored, not an error.
- The same rules apply per child type.

**Rationale.** A parallel DTO hierarchy would make the split explicit in the type system but
doubles every entity; metadata on the class keeps one source of truth and puts the rule in the
pipeline where an old client, an agent, and an import sheet all meet it.

**Confidence.** High. **Review flag:** write-once as validation versus silent reset (review
flag 6).

### D12 — Child synchronization keyed by parent; foreign ids rejected; no resurrection

**Decision.** For each child collection declared on the entity (`[NotMapped]` list with
`[Children(parentKeyProperty)]`, spec 0011's wire shape):

- `null` → untouched (no statements for that collection); `[]` → every existing child of that
  parent is deleted; otherwise children with `Id ≤ 0` are inserted, `Id > 0` updated, and the
  parent's existing children absent from the list deleted.
- A child `Id > 0` not among the parent's before-image children (belongs to another parent, to a
  parent the caller cannot see, or to nothing) → `Entity.NotFound` at `[i].Lines[j].Id`; never
  re-parented, never inserted.
- The nesting wins over a payload `ParentId` on a nested child (the FK is server-owned).
- The emitter's delete is bounded to the parents whose collection was present:
  `DELETE c FROM [core].[RoleMemberships] c WHERE c.[UserId] IN (SELECT [Id] FROM @tm_synced) AND NOT EXISTS (SELECT 1 FROM @tm_children x WHERE x.[Id] = c.[Id])`.
- Any child change bumps the parent's `ModifiedAt` even when no parent column changed (D14).

**Confidence.** High.

### D13 — The transaction is the persist batch text; guards are `THROW`s; retry and plan lanes are the executor's

**Decision.** The persist round trip is one command with no client-side `SqlTransaction` and no
`TransactionScope`. Its text, for `gl.Centers` (row-image TVP `@tm_rows : [gl].[CentersList_<hash8>]`,
`@tm_stamps : [SaveStampList]`, `@tm_touched : [IdList]`, `@tm_synced : [IdList]`):

```sql
SET NOCOUNT ON; SET XACT_ABORT ON;
BEGIN TRAN;

-- 1. guard prologue (spec 0013 emits the text; the pipeline places it first, inside the transaction)
DECLARE @tm_tag uniqueidentifier;
SELECT @tm_tag = [PermissionsTag] FROM [core].[UserStamps] WITH (UPDLOCK, ROWLOCK) WHERE [UserId] = @tm_userId;   -- U lock held to COMMIT
IF @tm_tag IS NULL OR @tm_tag <> @tm_expectedPermissionsTag THROW 50412, N'permissions', 1;
IF NOT EXISTS (SELECT 1 FROM [core].[Users] WHERE [Id] = @tm_userId AND [IsActive] = 1) THROW 50401, N'user-inactive', 1;
IF NOT EXISTS (SELECT 1 FROM [core].[TenantStamps] WHERE [SettingsTag] = @tm_expectedSettingsTag) THROW 50412, N'settings', 1;

-- 2. existence and concurrency, target rows U-locked until COMMIT (no S→X conversion deadlock)
DECLARE @tm_conflicts TABLE ([Id] int NOT NULL, [ModifiedAt] datetime2(7) NULL, [ModifiedById] int NULL, [IsMissing] bit NOT NULL);
INSERT @tm_conflicts ([Id], [ModifiedAt], [ModifiedById], [IsMissing])
SELECT s.[Id], t.[ModifiedAt], t.[ModifiedById], CASE WHEN t.[Id] IS NULL THEN 1 ELSE 0 END
FROM @tm_stamps s LEFT JOIN [gl].[Centers] t WITH (UPDLOCK, ROWLOCK) ON t.[Id] = s.[Id]
WHERE t.[Id] IS NULL OR (@tm_override = 0 AND s.[ExpectedStamp] IS NOT NULL AND t.[ModifiedAt] <> s.[ExpectedStamp]);
IF EXISTS (SELECT 1 FROM @tm_conflicts)
BEGIN
    SELECT [Id], [ModifiedAt], [ModifiedById], [IsMissing] FROM @tm_conflicts;   -- readable before the error
    THROW 50409, N'concurrency', 1;
END;

-- 3. emitter (spec 0011): UPDATE changed top-level rows only (EXCEPT predicate, so an unchanged row writes no
--    history row and keeps its stamp), then INSERT new rows, then per child table DELETE / UPDATE / INSERT
--    under the parents in @tm_synced; parents before children on insert, children before parents on delete;
--    tables in model order; every row written gets [ModifiedAt] = @tm_stamp, [ModifiedById] = @tm_userId
-- 4. capability statements: tree recompute (D20) for tree stacks
-- 5. ContributeAsync statements (service hook, then ISaveEffect components), blob confirmations (spec 0016),
--    inbox rows (spec 0019), task rows (spec 0019)
-- 6. tag bumps derived from the union of every statement's write set (spec 0012)

-- 7. row-level post-check: every touched row must still satisfy the caller's save grant
DECLARE @tm_visible int = (SELECT COUNT(*) FROM (<compiled: Root = Center, Select = "Id", Restriction = KeyIn(@tm_touched), Filter = save grant>) q);
IF @tm_visible <> @tm_touchedCount THROW 50403, N'rls-post', 1;

-- 8. read-back inside the transaction: entity rows, children, related rows, row echo for @tm_touched (D18)
COMMIT;
-- 9. extras (ContributeDetails) after COMMIT; they never extend lock duration
```

Rules the emitter and pipeline enforce: the transaction spans exactly one round trip;
`XACT_ABORT ON` makes every runtime error — `THROW`, 2601/2627, 547, a deadlock — roll the whole
batch back on the server; result sets preceding a `THROW` are read by the executor before the
exception surfaces (that is how the conflict list reaches the caller); the stamp `@tm_stamp` is
computed in C# (D14). The `UPDLOCK` on the caller's own stamp row is what makes the collapsed
connect sound for writes: every write that bumps a user's `PermissionsTag` (role save, membership
save, deactivation — spec 0013) updates that row and blocks until this transaction commits, so
the permissions the pre-check used in RT1 are exactly those in force at commit.

**Reserved error numbers** (`THROW`, severity 16, state 1; the message is a stable token):

| Number | Meaning | Becomes |
|---|---|---|
| 50401 | caller inactive or unknown at write time | `SessionInvalidException` |
| 50403 | row-level post-check failed | `ForbiddenException(Rls.PostCheck)` |
| 50404 | pre-check count mismatch (delete, action) | `NotFoundException` with the ids from the preceding result set |
| 50409 | concurrency stamp mismatch or target row missing | `ConcurrencyException` with the conflict rows |
| 50412 | permissions or settings tag changed since RT1 | refresh and re-run from RT1 once, then `StaleContextException` |
| 50413 | a SQL-side cap exceeded (delete by query) | `LimitExceededException` |
| 50422 | an invariant failed in SQL (`Tree.Cycle`) | `ValidationException` with the code the message carries |
| 50500–50599 | distribution guards contributed through `ContributeAsync` | `ValidationException` (`Distro.<code>`) |
| 2601 / 2627 | unique index / constraint | `ValidationException` `Unique` at the mapped property |
| 547 | foreign key | `ValidationException` `Fk.NotFound` (save) / `Fk.InUse` (delete) |
| 530 | tree recompute recursion limit | `ConcurrencyException(Tree.Cycle)` — a cycle formed between RT1 and RT2; the user retries and validation reports it |

**Retry.** The executor (spec 0011) owns retry, whole batch, never a statement: a read-only batch
on SqlClient's baseline transient list plus 1205 and 1222; the persist batch on the same list
only when the failure arrived **before** the first read-back result set (nothing was applied). A
connection loss during or after `COMMIT` runs the **commit probe** on a fresh connection:
`SELECT COUNT(*) FROM [gl].[Centers] t JOIN @tm_new n ON n.[Id] = t.[Id]` and `SELECT COUNT(*)
FROM [gl].[Centers] t JOIN @tm_upd u ON u.[Id] = t.[Id] WHERE t.[ModifiedAt] = @tm_stamp`. Any
insert present or any update carrying this batch's stamp proves the commit (ids are app-assigned;
the stamp is unique to the batch on that row); the pipeline re-issues the read-back as a plain
read and proceeds to post-commit effects. Neither present: the whole persist is re-run — a replay
of the inserts collides on the primary key if the commit did happen, never duplicates. Three
attempts total, jittered 50/200/800 ms; post-commit effects are never retried by the executor.

**Plan lanes.** When any TVP in a batch carries more than `LargeBatchThreshold` rows the emitter
appends `OPTION (RECOMPILE)` to every DML statement of that batch; below the threshold statements
are cached normally. Table-variable deferred compilation caches the first execution's plan, and
without lanes a one-row save's plan would serve a fifty-thousand-row import.

**Rationale.** `TransactionScope` defaults (Serializable, one minute, async flow suppressed) are
all wrong, and a stray second connection escalates to a distributed transaction that throws on
Linux; a client `SqlTransaction` costs a `BEGIN`/`COMMIT` exchange each and disables driver retry
anyway. `THROW` honours `XACT_ABORT`, needs no `sys.messages` entry, and HTTP-mnemonic numbers
are memorable. The probe resolves the one ambiguous window without a transaction-tracking table.

**Rejected.** A `SqlTransaction`-controlled split (persist without commit → C# → commit) as the
default; a purely optimistic tag assertion without the stamp-row lock (a stale-permission write
could commit); `@@ROWCOUNT` comparison instead of a pre-check (loses the conflicting ids).

**Confidence.** High. **Review flags:** read-back inside versus after the transaction (review
flag 10); the plan-lane threshold and the `RECOMPILE` form (review flag 11).

### D14 — Optimistic concurrency: `ModifiedAt` is the stamp, echoed by the client, checked under `UPDLOCK`, override is explicit

**Decision.** No `rowversion` column. Every top-level entity carries the four audit columns and
`ModifiedAt datetime2(7)` is the stamp:

- **Generation.** One stamp per persist batch, from `TimeProvider` (`UtcNow.UtcDateTime`; tick
  precision equals `datetime2(7)`). If it is not strictly greater than the largest `ModifiedAt`
  among the before images (clock skew or a clock step), the stamp is `max + 1 tick`. It therefore
  differs from every stamp it replaces and is unique to the batch on each row — the commit probe
  relies on that.
- **What bumps it.** Every write of a row image through the emitter: save, activate/deactivate,
  every action, background jobs that change business columns. There is no emitter path that
  writes a top-level table without stamping `ModifiedAt`/`ModifiedById`. Bookkeeping that must
  not disturb the stamp (`LastActiveAt`, tags, inbox counters, lease columns) lives in sibling
  non-temporal tables; the tree recompute writes `Node` and the counts and never `ModifiedAt`.
- **Children** carry no stamp; any child change bumps the parent's.
- **The check** is statement 2 of D13, under `UPDLOCK, ROWLOCK`: two savers of one row serialize
  on the U lock and the second sees the first's committed stamp, which is what makes
  check-then-write race-free under RCSI and lock-after-qualification. A row that vanished is
  always a conflict (`IsMissing`), never an insert, whatever the override says.
- **On the wire** the client echoes the `ModifiedAt` it loaded; the pipeline reads it as the
  expected stamp before overwriting (the one exception to "server-owned values are ignored"). A
  default value means "no check"; `SaveOptions.RequireStamps = true` (set by the web details-page
  save, spec 0015) makes a default on an update `ValidationException` `Concurrency.StampRequired`;
  import and agents may omit stamps. The string must round-trip verbatim (seven fractional
  digits); the SPA keeps the raw string for the save and parses a copy for display (spec 0015).
- **Override.** `SaveOptions.OverrideConcurrency = true` skips the comparison only: missing rows
  still conflict, permissions still apply, U locks are still taken. The UI offers "overwrite" from
  the `ConcurrencyException` (ids, `ModifiedAt`, `ModifiedById`, resolved to a display name by
  spec 0015) and resends with the flag. Overrides are metered.
- **Actions** do not check stamps (a state toggle is a command over ids, not an edit of a loaded
  copy) but bump them, so a concurrent editor's later save conflicts.
- **Delete** accepts optional expected stamps per id (`DeleteRequest.ExpectedStamps`); a mismatch
  is a conflict.

**Against `rowversion`.** It bumps on any update of any column including bookkeeping, cannot be
set by the app (a restore, a cross-tenant import, and the commit probe cannot reason about it),
is opaque in the "user Y modified this at T" prompt, and adds a column where `ModifiedAt` already
exists; its one advantage, engine-guaranteed monotonicity, is replaced by the `max + 1 tick` rule.
The temporal `ValidFrom` was also rejected: it is the transaction time shared by every row the
transaction touched and moves on writes the emitter does not control.

**Confidence.** High. **Review flags:** echoing `ModifiedAt` versus a separate `ExpectedStamp`
member (review flag 5); early detection in RT1 kept as a fast-fail (review flag 7).

### D15 — Row-level security: pre-check by filtered load, post-check by compiled query inside the transaction, bespoke grants disjoined

**Decision.** Three rules:

- **Type level.** No grant on `(resource, action)` → `ForbiddenException(Permission.Missing)`
  before any round trip. The resource name is public metadata, so this leaks nothing.
- **Row level, before the write.** Every id-addressed operation restricts its target rows by the
  action's filter. For save, the before-image load of RT1 (missing → `Entity.NotFound`). For
  delete and actions, a statement inside the batch:

  ```sql
  DECLARE @tm_vis TABLE ([Id] int PRIMARY KEY);
  <compiled: Root = Center, Select = "Id", Restriction = KeyIn(@tm_ids), Filter = action grant, Sink = KeyTable("@tm_vis")>
  IF (SELECT COUNT(*) FROM @tm_vis) <> @tm_expected
  BEGIN
      SELECT i.[Id] FROM @tm_ids i WHERE NOT EXISTS (SELECT 1 FROM @tm_vis v WHERE v.[Id] = i.[Id]);
      THROW 50404, N'not-found', 1;
  END;
  ```

  and the operation's own statement targets `@tm_vis`.
- **Row level, after the write** — save always, actions that opt in with `PostCheck = true`:
  statement 7 of D13. A grant with no filter compiles to no post-check. Actions default to no
  post-check because an action commonly moves a row out of the filter that permitted it (`post`
  on `State = 'Draft'`).

**Bespoke grants.** `BespokeGrant(action, caller)` on the service returns a `FilterTree` (or
null) that the pipeline hands to the evaluator to disjoin with the stored grants — "assigned to
me" is `Leaf("AssigneeId = me()")`. The service never composes the final filter.

**Related entities** reached through a details read are **not** row-level filtered: a user who may
read a document may read what it points at (no partially visible document); they are reachable
only through a visible root.

**Rejected.** SQL Server security policies (logic in the database; cannot hold user-authored
Queryex; schema binding blocks expand/contract; history tables unprotected); the post-check in C#
after commit (too late).

**Confidence.** High. **Review flag:** unfiltered related entities (review flag 13).

### D16 — Side effects: transactional participants append statements; post-commit is best-effort; durable work is a task row

**Decision.** Exactly two side-effect phases:

- `ContributeAsync(PersistContext)` — the service hook and every `ISaveEffect<TEntity>`
  component — runs before the persist batch is sent with final ids and the batch's stamp, and may
  only append statements (with declared write sets) and result readers to the batch: inbox rows
  (spec 0019), task rows (spec 0019), blob confirmations (spec 0016), audit-trail rows, the
  distribution's own tables. It performs no I/O of its own (the context exposes no connection).
  Everything here commits or rolls back with the save.
- `AfterCommitAsync(SaveOutcome)` runs after the commit is acknowledged: blob deletes for replaced
  images, SignalR nudges, cache pushes, external calls the distribution accepts as best-effort.
  Failures are logged with the save's trace id and counted on `tellma.crud.effects.failures`
  (tag `crud.effect`, the closed set of registered effect names); they never fail the response.

Anything that must eventually happen (an email, an e-invoice filing) is a task row appended in
`ContributeAsync` and executed by the background machinery. Blob *writes* precede the request
(staged upload, token on the record); the pipeline's only blob duties are validating tokens in
RT1, confirming them in RT2, and deleting replaced blobs after commit.

**Rejected.** Multipart save (every entity with a file gets a second endpoint shape; MCP and
import must speak multipart); a pre-commit non-transactional phase (no correct failure story).

**Confidence.** High.

### D17 — Delete semantics

**Decision.** Delete by ids is one round trip: guard prologue, `@tm_vis` pre-check with the
`delete` grant (D15; all-or-nothing), the service's `ValidateDeleteAsync` when overridden (adds
RT1 before the batch), child tables then the parent, tree count refresh, tag bumps, commit.
Error 547 → `ValidationException` `Fk.InUse` at `Ids[i]` naming the referencing table; no C#
pre-check for it. Delete by query compiles `And(filter, deleteGrant)` with `Select = "Id"` into
`@tm_vis`, throws 50413 when the count exceeds `MaxDeleteByQueryRows`, deletes from `@tm_vis`,
and reports the count; `DryRun` stops after the count. Delete with descendants compiles
`Restriction = DescendantOfAny(@tm_ids)` twice — bare into `@tm_all`, with the delete grant into
`@tm_vis` — throws 50404 when they differ (a partially deleted subtree is never left behind),
deletes `@tm_vis` deepest first (`ORDER BY [Level] DESC` through an ordered CTE, satisfying the
self-referencing foreign key), then recomputes the remaining subtree counts. Soft-delete semantics
are a domain concern (`IActivatable`), never a delete substitute.

**Confidence.** High.

### D18 — Details: one round trip mixing Queryex and model-emitted keyed SQL

**Decision.** `GetByIdAsync` / `GetByIdsAsync` run one round trip: the connect statement; the
main ids into `@tm_main` (`Select = "Id"`, `Restriction = KeyIn(@tm_ids)`, `read` grant conjoined
— row-level security decides visibility here and nowhere else in the batch); the entity rows by
`@tm_main` (model-emitted, all columns); every declared child collection by parent id,
recursively for grandchildren; for every `Expand` path one statement per distinct related entity
type, restricted by a semi-join chain through the path (`WHERE [Id] IN (SELECT [CenterId] FROM
[gl].[InvoiceLines] WHERE [InvoiceId] IN (SELECT [Id] FROM @tm_main))`), depth ≤ `MaxExpandDepth`,
display columns only, no row-level filter (D15); the service's `ContributeDetails` statements for
the requested `Extras` (≤ `MaxExtras`); and the row echo (`EchoSelect` compiled with
`Restriction = KeyIn(@tm_main)`). The materializer (spec 0011) folds the result sets into the
entity, its collections, and `Related[entityName][id]`, so ten thousand lines pointing at one
center carry one center. Zero rows in `@tm_main` → `NotFoundException`, every other result set
drained and discarded. An unknown `Expand` path is `ArgumentException` (paths are static facts of
the model). `GetByParentIdsAsync` is a query with `Restriction = KeyIn(@tm_parents)` on
`ParentId` (plus `ParentId = null` for the roots), served by the parent-id index. The same
read-back statements serve the save (D13 step 8).

**Rationale.** Keyed reads compile to seeks and need no expression compiler; Queryex is used
exactly where a user or a permission wrote text. Everything a page needs in one call is the
one-action-one-call rule.

**Rejected.** Hand-written SQL per details page — kept only as the `Extras` escape hatch.

**Confidence.** High.

### D19 — One capability, declared once: an interface on the entity, `[EntityAction]` on the service, everything else projected

**Decision.** A capability is an interface the entity class implements. The stack feature
reflects the leaf's interfaces once at composition and projects everything else through a
`CapabilityDefinition` registered in the platform's `CapabilityRegistry`:

| Capability | Entity declares | Columns (spec 0011) | Securables | Operations | Gates / server-owned | Default query behaviour | Persist statements |
|---|---|---|---|---|---|---|---|
| Keyed, audited (every stack entity) | `IEntity<TKey>`, `IAudited` (carried by `Entity<TKey>`) | `Id`, the four audit columns | `read`, `save`, `delete` | D4 set | audit columns server-owned | — | stamping in every UPDATE/INSERT |
| Activatable | `IActivatable` | `IsActive bit NOT NULL DEFAULT 1` | `activate` (filterable; both directions) | actions `activate`, `deactivate` → `ActivateAsync`/`DeactivateAsync`, `POST …/activate`, `…/deactivate` | `IsActive` gated by `activate` on save | `IncludeInactive = false` conjoins `IsActive = true`; details banner metadata | guarded `UPDATE … SET [IsActive] = @v, [ModifiedAt] = @tm_stamp, [ModifiedById] = @tm_userId WHERE [Id] IN (SELECT [Id] FROM @tm_vis) AND [IsActive] <> @v`; tree count refresh |
| Tree | `ITreeEntity<TKey>` | `ParentId`, `Node` (platform-owned), `Level` (computed), `SubtreeCount`, `ActiveSubtreeCount` | — | `GetByParentIdsAsync`, `DeleteWithDescendantsAsync`, `IncludeAncestors`, `POST …/by-parent-ids`, `…/delete-with-descendants` | tree columns server-owned; cycle and depth validation | tree-view metadata | recompute (D20) |
| Temporal | `[Temporal]` (spec 0011) | period columns, history table | — | — | — | — | emitter skips unchanged rows |
| Record with blobs | `IHasImage` / `[BlobReference]` (spec 0016) | blob-id columns | — | retrieval endpoint is spec 0016's | tokens validated in RT1, confirmed in RT2, replaced blobs deleted after commit | — | confirmation |
| Multilingual | `IMultilingual` | `Name`, `Name2`, `Name3` | — | — | absent columns ignored | `Search` covers the configured name columns | — |

Activate/deactivate are `EntityAction`s registered by the capability with `Permission = "activate"`
and a **SQL-only** fast path: one batch with the guard, the `@tm_vis` pre-check, the update above,
the tree `ActiveSubtreeCount` refresh, the tag bump, and the read-back when requested. A service
that must veto (a user cannot deactivate themselves) overrides `ValidateActionAsync("deactivate",
…)`, which moves the action onto the general two-round-trip path. One securable covers both
directions: "may deactivate but not reactivate" has no precedent worth doubling every role's
configuration.

Custom actions are declared once on the service:

*Illustration*
```csharp
[EntityAction("post", SupportsFilter = true, LoadChildren = true)]
[Description("Posts draft invoices to the general ledger.")]
public async Task PostAsync(ActionContext<Invoice, int> context, PostArguments arguments) { … }
```

and project `(gl.Invoice, post)` into the securables registry, `ExecuteActionAsync("post", …)`,
`POST …/post` with `{ ids, arguments }`, the MCP `actions.run` entry with the argument schema
derived from `PostArguments`, and the UI button metadata. The action pipeline is the save
pipeline with the load replaced by "the rows in `ids` visible under the action's filter, children
included when `LoadChildren`", the method as validator and mutator (`context.Save(entities)` marks
rows for the emitter with stamping; `context.Batch` takes statements; `context.Errors` collects
validation errors), and `PostCheck` off by default. Read-only stacks are `[Stack(Operations =
StackOperations.Read)]`, not a type: write operations and securables are not registered and no
write routes are projected.

**Rationale.** "Declared once" only holds if the declaration is the entity's own shape (the
interface's properties are the columns) and everything else is derived; any second place is where
drift starts. There is exactly one thing to forget (the interface), and forgetting it removes the
feature wholesale rather than half of it. Capability interfaces are per *capability*, never per
entity — the architecture's "per-feature gating" rule, not the paired-interface pattern it rejects.

**Rejected.** Marker attributes without properties (the property must exist for C# and Queryex);
fluent capability registration at the composition site (declared twice); a separate
`IActivatableEntityService` (two calls `ExecuteActionAsync` already covers).

**Confidence.** High. **Review flag:** one `activate` securable versus two (review flag 3).

### D20 — Trees: platform-owned `Node`, id-based paths recomputed in SQL over affected roots, cycles validated in C# and guarded in SQL

**Decision.** `ITreeEntity<TKey>` requires `ParentId`, `SubtreeCount`, `ActiveSubtreeCount`
(equal to `SubtreeCount` when the entity is not activatable, so the tree view has one code path).
`Node` (`hierarchyid`, unique depth-first index) is a **platform-owned column that never appears
on the class or the wire**: configured by the platform's EF convention, excluded from the UDTT,
exposed to Queryex as the entity's `TreeNode`, written only by the recompute. `Level` is a
persisted computed column `[Node].GetLevel()` that `level()` reads; `IsLeaf` is `SubtreeCount = 1`.
Node paths use the row's own id as the path component (`/15/342/1207/`), so a node value depends
only on the ancestor chain, sibling order is insertion order, and a recompute never renumbers
untouched siblings.

The recompute rides the persist batch after the emitter's statements, scoped to the roots of every
touched row's old and new position (`@tm_affected : [IdList]` = touched ids ∪ new parent ids ∪
old parent ids, computed in C# from the payload and the before images):

```sql
DECLARE @tm_roots TABLE ([Id] int PRIMARY KEY);
;WITH Up AS (
    SELECT c.[Id], c.[ParentId] FROM [gl].[Centers] c WHERE c.[Id] IN (SELECT [Id] FROM @tm_affected)
    UNION ALL
    SELECT p.[Id], p.[ParentId] FROM [gl].[Centers] p JOIN Up u ON u.[ParentId] = p.[Id])
INSERT @tm_roots ([Id]) SELECT DISTINCT [Id] FROM Up WHERE [ParentId] IS NULL OPTION (MAXRECURSION 64);

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

Neither statement touches `ModifiedAt`. Cycle validation runs twice by design: in RT1 the
pipeline loads `Restriction = AncestorOfAny(newParentIds)` (ids and parent ids only), joins the
chain with the in-batch parent links, and reports `Tree.Cycle` at `[i].ParentId` when a chain from
a row's new parent reaches the row itself (or `ParentId = Id`), and `Tree.TooDeep` beyond depth
64; in RT2 a cycle that formed between the rounds makes the `Up` recursion exceed
`MAXRECURSION 64` — error 530, rolled back, mapped to `ConcurrencyException(Tree.Cycle)`; the
user retries and validation now reports it. No additional locks: the concurrency check already
U-locks the saved rows, so two saves that would close a cycle serialize and the second sees the
first's committed state.

**Rationale.** A per-row `GetDescendant` needs sibling coordination and a loop; an id-based path
is collision-free, deterministic, and O(affected subtree); keeping `Node` off the class keeps
`hierarchyid` out of Abstractions and out of the save path (`ExcludeFromTableTypeAttribute` lives
in `Tellma.Core.EntityFrameworkCore`, which a module's Abstractions cannot reference anyway). The
CTE cannot detect a cycle (it recurses until the limit), so C# owns that rule.

**Rejected.** In-memory recompute (C# must know every row's siblings); `ROW_NUMBER` sibling
numbering (renumbers siblings on every recompute, forcing whole-root rewrites); `Node` as a class
property (an EF type in Abstractions, or a string that leaks the encoding).

**Confidence.** High on the statements; medium on shadow property versus a platform value
converter (spec 0011's call; review flag 14). The `IsDescendantOf` count refresh versus a second
recursive CTE is to be measured on the fixture.

### D21 — Distribution extension of pack entities and services

**Decision.** A pack ships `Center` (non-abstract, unsealed, `[Table]`-mapped) and, where it
needs logic, `CenterService<TCenter> : EntityService<TCenter, int> where TCenter : Center`. Its
feature takes the leaf as a type parameter with the pack default as the default:
`t.Gl(g => g.Center<MyCenter>())`. The platform registers `EntityService<MyCenter, int>` (the
pack service closed over the leaf, or the distribution's subclass when registered), the pipeline,
and the capability operations detected on the leaf. Columns the distribution adds are ordinary
properties; capability interfaces added on the leaf (`IHasImage`) project exactly as on a pack
entity. Pack hook overrides call `base.` so a distribution rule composes with the pack rule;
`IEntityValidator<>`/`ISaveEffect<>` components resolve along the type chain (D2), so a pack's
invariants keep running after a distribution extends the entity.

**Confidence.** High.

### D22 — Import reuses `SaveAsync` unchanged

**Decision.** The Excel codec calls `SaveAsync(entities, new SaveOptions { ReturnEntities =
false, RequireStamps = false, Source = SaveSource.Import })` in chunks of at most `MaxSaveCount`.
Natural-to-surrogate translation and partial-sheet hydration are the codec's (spec 0018);
hydration uses `GetByIdsAsync(ids, DetailsRequest.None)` (children, no extras, no echo). Insert
mode sends ids of `0`; update and merge send the ids the natural-key lookup resolved (an
unresolved key in update mode is the codec's validation error). Each chunk is its own transaction;
all-or-nothing across chunks is a background-task concern (spec 0018/0019). `SaveSource`
(`Web | PublicApi | Agent | Import | System`) is recorded in telemetry and visible to hooks; it
never weakens a rule (a rule that differs by source is a rule that will be bypassed).

**Confidence.** High.

### D23 — The closed exception set (seam 10)

**Decision.** Seven sealed exceptions derive from `TellmaException` (stable `Code`, named
`Arguments`; the message is for logs); spec 0015 maps them:

| Exception | Carries | Status |
|---|---|---|
| `ValidationException` | `list<ValidationError>` | 422 |
| `NotFoundException` | resource, ids | 404 |
| `ForbiddenException` | code (`Permission.Missing`, `Rls.PostCheck`, `Gate.<action>`), resource, action | 403 |
| `ConcurrencyException` | code (`Concurrency.Stamp`, `Tree.Cycle`), `list<ConcurrencyConflict>` | 409 |
| `LimitExceededException` | limit name, actual, maximum | 413 |
| `InvalidQueryException` | `list<QueryexDiagnostic>` | 400 |
| `SessionInvalidException` | reason (`User.Inactive`, `User.Unknown`) | 401 (the host ends the session) |
| `StaleContextException` | tag kind | 503, retryable |

Every other exception is a 500 and is never mapped by name. Internal invariants (a validator
exceeding its rounds, an unknown `Expand` path, an unsupported operation on a read-only stack)
throw BCL exceptions — authoring bugs, not user outcomes. Codes are stable strings; a platform
minor may add codes, never rename one.

**Rationale.** A closed set the web layer maps by type is simpler and safer than an interface
every exception implements; codes plus arguments give the SPA and agents what they need.

**Confidence.** High.

### D24 — Resource and action naming (position; spec 0013 owns the scheme)

**Decision.** A securable's `Resource` is the entity's schema-qualified logical name —
`core.User`, `gl.Center` — fork-stable (a distribution leaf inheriting the pack default keeps the
pack's resource), unique by construction, and readable in the role editor; `FilterRoot` is the
Queryex logical name (`Center`). `Action` is a lowercase verb: the standard `read`, `save`,
`delete`, the capability action `activate`, custom action names (`post`, `invite`); `all` is the
wildcard on either axis; `save` implies `read`. Non-entity securables use a dotted namespace
(`settings.general`). Action names double as endpoint segments and MCP action names.

**Confidence.** Medium — spec 0013 owns the scheme (conflict 1).

### D25 — Every write declares its write set; tag bumps derive from it; bypasses are mechanically caught

**Decision.** Every statement added to a batch carries a write set (the tables it may modify):
emitter statements derive it from the model; raw statements declare it (`Writes`). The executor
appends, inside the same transaction, one bump per tag the union of write sets touches (cacheable
entity types, settings, securables — spec 0012's registry), so this pipeline never bumps a tag by
hand; stack-specific bumps (a role save bumps its members' `PermissionsTag`) are `ContributeAsync`
statements owned by spec 0013's `RoleService`. There is no API to execute SQL against a tenant
database outside a batch: `DbContext.SaveChanges*` is forbidden by an analyzer in pack and
distribution code (the context exists for the model and migrations), a Roslyn analyzer shipped
with spec 0011's ordinal-binding analyzer parses literal raw SQL with `ScriptDom` and reports any
DML target outside the declared set, and the executor is the only holder of a connection.
Out-of-band SQL is outside the guarantee; the "refresh all tags" administrative operation (spec
0012) is the remedy.

**Confidence.** High on the mechanism; medium on analyzer coverage of dynamically composed SQL
(mitigated by steering distributions to `SqlBuilder<T>`).

### D26 — Telemetry

**Decision.** Meter `Tellma.Core` through `IMeterFactory`; names as `const`s in
`Tellma.Core.Abstractions.Crud.CrudTelemetryNames`:

| Instrument | Type | Unit | Tags (closed sets) |
|---|---|---|---|
| `tellma.crud.operation.duration` | Histogram | `s` | `crud.operation` (query, details, details_set, save, delete, delete_by_query, action, by_parent_ids, delete_with_descendants), `crud.outcome` (ok, validation, forbidden, not_found, conflict, limit, session, stale, error), `crud.source` |
| `tellma.crud.validation.rounds` | Histogram | `{round}` | `crud.operation` |
| `tellma.crud.save.entities` | Histogram | `{entity}` | `crud.source`, `crud.lane` (small, large) |
| `tellma.crud.concurrency.conflicts` | Counter | `{conflict}` | `crud.kind` (stamp, missing, cycle) |
| `tellma.crud.concurrency.overrides` | Counter | `{save}` | — |
| `tellma.crud.effects.failures` | Counter | `{failure}` | `crud.effect` |
| `tellma.crud.stale_context.reruns` | Counter | `{rerun}` | `crud.tag` (permissions, settings) |
| `tellma.crud.id.refills` | Counter | `{refill}` | — |

The per-request round-trip budget is spec 0011's (`IDbCallBudget`, incremented by the executor);
this pipeline supplies the low-cardinality operation identity (`data.operation =
<Entity>.<operation>`) it tags with, and sets `tellma.crud.entity` and `tellma.crud.operation` on
the request span. No entity tag on instruments (it multiplies every dimension); the entity is a
span attribute and a structured-log field. Tenant and user are never tags. Logs carry tenant,
user, entity, id count, round count, and the error code.

**Confidence.** High. **Review flag:** entity as a metric tag (review flag 15).

### D27 — The round-trip budget per operation

| Operation | Common case | When it costs one more |
|---|---|---|
| Query (with count, with ancestors) | **1** | cold connect cache; stale tag (re-run) |
| Details (one or many) | **1** | same |
| Save — creates only, no context loads, warm id buffer | **1** | cold id buffer; validator loads (→ 2) |
| Save — any update, or any validator load | **2** | +1 per dependent validation round (max 3 → 4) |
| Delete by ids / by query | **1** | `ValidateDeleteAsync` overridden (2) |
| Delete with descendants | **1** | same |
| Activate / deactivate | **1** | `ValidateActionAsync` overridden (2) |
| Custom action | **2** | +1 per dependent validation round |
| Get by parent ids | **1** | — |
| Import, per chunk | **2** (+1 hydration when the sheet is partial) | as for save |
| Any operation after a transient failure | +1 per retry (max 3 attempts) | — |

Failure modes of the collapsed connect: **deactivated user** — the connect statement (reads) or
the prologue (writes) fails before any business statement; `SessionInvalidException` ends the
session. **Stale permissions** — 50412 before any business statement, so nothing runs under a
stale filter; the pipeline reloads, rebuilds, re-runs once, then `StaleContextException`. **Row-
level pre-check ordering** — the pre-check is compiled from the permissions the guard verified in
the same batch; the two cannot disagree. **Between RT1 and RT2** — the prologue re-asserts the
tag under the stamp-row lock; a change in between aborts RT2 and the save restarts from RT1 once.
The budget is asserted by the conformance tests through
`SqlConnection.RetrieveStatistics()["ServerRoundtrips"]` and observed in production through the
executor's budget instrument.

### D28 — Testing

**Decision.** `test/core/Tellma.Core.Tests` (pure: the pipeline over a scripted batch double, the
attribute walker, the path grammar, the parking scheduler property tests, exception mapping,
ceilings) and `test/core/Tellma.Core.IntegrationTests` (`Category=Integration`, LocalDB or
Testcontainers, fixture entities shared with spec 0011 under `test/shared/Tellma.Testing.Entities`:
`Widget` — audited, activatable, `[Unique] Code`, `[WriteOnce] Serial`, `[Searchable] Name`;
`WidgetLine` — child; `Bucket` — tree, activatable). `Tellma.Core.Testing` ships
`StackConformanceTests<TEntity, TKey>`, an abstract xUnit class a distribution closes per entity
by supplying `NewValidEntity(ordinal)`, asserting: every descriptor operation reachable and every
absent one refused; the D27 budget; the securables registered; hidden-equals-missing; concurrency
conflict, override, missing-row conflict; write-once rejection; server-owned values ignored;
diff-gated `IsActive`; `null` versus `[]` collections and foreign child ids; unique race from two
connections (one 422, never a 500) and `[Unique]`-has-index; `Fk.InUse`; delete-by-query guards;
tree recompute for ancestors not in the payload, C# cycle message, SQL cycle race rolled back; a
permissions tag bumped between RT1 and RT2 refused and re-run; a role save blocking behind a
member's in-flight persist; the commit probe after a killed connection; validation rounds beyond
the limit; every raw statement's write set covering its ScriptDom targets;
`MetricCollector<T>` over the instruments of D26.

**Confidence.** High.

### D29 — Compatibility rules (what a platform minor may and may not do)

**Decision.** A platform minor may add hooks (with default bodies), context members, capability
interfaces, error codes, request/response members, and instruments. It may not rename a hook,
remove a code, change the path grammar, reorder pipeline steps observably, or change a default
ceiling. Servers ignore unknown JSON members and clients tolerate new ones (N−1 during swaps).
The emitter binds by metadata, so a pack adding a column reorders nothing observable. A test pins
the public surface of `Tellma.Core.Abstractions.Crud` with an API golden.

**Confidence.** High.

---

## 3. Contracts

Contract blocks use the platform's contract notation: names are normative; shape is described,
not transcribed. Owned by this theme unless marked *needed from*. Namespaces:
`Tellma.Core.Abstractions.Crud` (3.1–3.3, 3.5, 3.7), `Tellma.Core.Abstractions.Validation` (3.4),
`Tellma.Core.Abstractions.Errors` (3.6).

### 3.1 The service base and the pipeline

```contract
// The authoring surface of a stack. Non-abstract: a stack with no logic registers it as is.
base EntityService<TEntity, TKey>
    where TEntity: class, IEntity<TKey>
    where TKey: struct
  Descriptor: StackDescriptor                                     // computed at startup
  QueryAsync(query: EntityQuery) -> QueryResult
  GetByIdAsync(id: TKey, request: DetailsRequest) -> DetailsResult<TEntity>
  GetByIdsAsync(ids: list<TKey>, request: DetailsRequest) -> DetailsSetResult<TEntity, TKey>
  SaveAsync(entities: list<TEntity>, options: SaveOptions) -> SaveResult<TEntity, TKey>
  DeleteByIdsAsync(request: DeleteRequest<TKey>) -> DeleteResult
  DeleteByQueryAsync(request: DeleteByQueryRequest) -> DeleteResult
  ExecuteActionAsync(action: string, ids: list<TKey>, arguments: object?, options: ActionOptions) -> ActionResult<TEntity, TKey>
  // protected virtual hooks, empty defaults
  PreprocessAsync(context: SaveContext<TEntity, TKey>)
  ValidateAsync(context: SaveContext<TEntity, TKey>)
  ValidateDeleteAsync(context: DeleteContext<TEntity, TKey>)
  ValidateActionAsync(action: string, context: ActionContext<TEntity, TKey>)
  ContributeAsync(context: PersistContext<TEntity, TKey>)
  AfterCommitAsync(outcome: SaveOutcome<TEntity, TKey>)
  ContributeDetails(plan: DetailsPlan<TEntity, TKey>)                       sync
  SearchFilter(search: string) -> FilterTree?                                sync
  BespokeGrant(action: string, caller: ICallerContext) -> FilterTree?        sync

base EntityService<TEntity> = EntityService<TEntity, int>                  // the alias distributions name

// The platform pipeline behind every service; sealed in Tellma.Core, one closed generic per stack.
service IEntityPipeline<TEntity, TKey>
  QueryAsync(behavior: IEntityBehavior<TEntity, TKey>, query: EntityQuery) -> QueryResult
  GetByIdAsync(behavior, id: TKey, request: DetailsRequest) -> DetailsResult<TEntity>
  GetByIdsAsync(behavior, ids: list<TKey>, request: DetailsRequest) -> DetailsSetResult<TEntity, TKey>
  SaveAsync(behavior, entities: list<TEntity>, options: SaveOptions) -> SaveResult<TEntity, TKey>
  DeleteByIdsAsync(behavior, request: DeleteRequest<TKey>) -> DeleteResult
  DeleteByQueryAsync(behavior, request: DeleteByQueryRequest) -> DeleteResult
  ExecuteActionAsync(behavior, action: string, ids: list<TKey>, arguments: object?, options: ActionOptions) -> ActionResult<TEntity, TKey>
  GetByParentIdsAsync(behavior, parentIds: list<TKey?>, projection: EntityQuery) -> QueryResult      // tree stacks
  DeleteWithDescendantsAsync(behavior, request: DeleteRequest<TKey>) -> DeleteResult                  // tree stacks

// The hooks as the pipeline sees them; implemented explicitly by EntityService, never directly.
contract IEntityBehavior<TEntity, TKey>
  PreprocessAsync, ValidateAsync, ValidateDeleteAsync, ValidateActionAsync, ContributeAsync,
  AfterCommitAsync, ContributeDetails, SearchFilter, BespokeGrant          // same signatures as above

// Composable components, contravariant, resolved for the leaf and every base type, registration order.
contract IEntityValidator<in TEntity>
  ValidateAsync(context: SaveContext<TEntity>)
  ValidateDeleteAsync(context: DeleteContext<TEntity>)                       // default: nothing
contract ISaveEffect<in TEntity>
  ContributeAsync(context: PersistContext<TEntity>)
  AfterCommitAsync(outcome: SaveOutcome<TEntity>)                            // default: nothing
contract IDetailsContributor<in TEntity>
  Extras: list<string>                                                       // names it can produce
  Contribute(plan: DetailsPlan<TEntity>, requested: set<string>)             sync

// Capability shortcuts; the generic constraint is the compile-time gate.
ActivateAsync(service: EntityService<TEntity, TKey>, ids: list<TKey>, options: ActionOptions?) -> ActionResult<TEntity, TKey>
    where TEntity: IActivatable                                              // extension → ExecuteActionAsync("activate")
DeactivateAsync(...)                                                         // → ExecuteActionAsync("deactivate")
GetByParentIdsAsync(service, parentIds: list<TKey?>, projection: EntityQuery) -> QueryResult
    where TEntity: ITreeEntity<TKey>                                         // extension
DeleteWithDescendantsAsync(service, request: DeleteRequest<TKey>) -> DeleteResult
    where TEntity: ITreeEntity<TKey>                                         // extension

annotation [EntityAction(Name, Permission?, SupportsFilter = true, LoadChildren = false, PostCheck = false)]
    on method of an EntityService: `Task M(ActionContext<TEntity, TKey> context [, TArguments arguments])`
annotation [Stack(Operations = All, MaxTake = 500, CountCap = 10000, MaxSaveCount = 10000, MaxChildrenPerEntity = 10000,
    MaxIds = 10000, MaxDeleteByQueryRows = 10000, MaxExpandDepth = 3, MaxValidationRounds = 3, MaxSearchLength = 200,
    MaxExtras = 16, LargeBatchThreshold = 1000, DefaultFilter?, DefaultSelect?)]   on entity type, inherited

enum StackOperations = None | Query | Details | Save | Delete | Import | Export     // flags; Read = Query|Details|Export; All
```

| Member | Meaning |
|---|---|
| `[EntityAction].Name` | Lowercase, unique per stack; the endpoint segment and the MCP action name. |
| `[EntityAction].Permission` | The securable action; defaults to `Name`. Several actions may share one (activate/deactivate). |
| `[EntityAction].PostCheck` | Re-run the action's filter over the rows after the write; off because actions commonly leave the filter that permitted them. |
| `SearchFilter` | Null keeps the platform's disjunction over the searchable properties. |
| `BespokeGrant` | A criterion the caller satisfies regardless of roles, disjoined with the stored grants; null adds nothing. |

### 3.2 Capability interfaces and annotations (entity contract members this theme requires; spec 0011 owns the contract)

```contract
contract IEntity<TKey>          Id: TKey                       // 0 = new, < 0 = batch-local temporary id, > 0 = update
contract IAudited               CreatedAt: datetime2(7)  CreatedById: int  ModifiedAt: datetime2(7)  ModifiedById: int
contract IActivatable           IsActive: bool
contract ITreeEntity<TKey>      ParentId: TKey?  SubtreeCount: int  ActiveSubtreeCount: int     // Node and Level are platform-owned
contract IMultilingual          Name: string  Name2: string?  Name3: string?
contract IHasImage              ImageId: string?                                              // spec 0016

annotation [ServerOwned]                 on property   // client value ignored; overwritten from the before image or a default
annotation [WriteOnce]                   on property   // settable on insert; a changed value on update is `WriteOnce`
annotation [GatedBy(action)]             on property   // a change through save needs the action's grant on that row
annotation [Searchable(Kind = Contains)] on property   // Kind: Contains | Prefix
annotation [PreserveWhitespace]          on property   // opts out of trim-and-null normalization
annotation [Unique]                      on property   // index + built-in validator + 2601/2627 map (spec 0011 emits the index)
annotation [NaturalKey]                  on property   // implies [Unique]; export/import identity (spec 0011 inference rules)
annotation [Children(parentKeyProperty)] on property   // a [NotMapped] child collection the pipeline saves and loads
enum SearchKind = Contains | Prefix
```

### 3.3 Requests, results, options

```contract
data EntityQuery
  Select: string?                      // null = the stack's default select
  Aggregate: bool = false
  Search: string?
  Filter: FilterTree?
  Having: FilterTree?
  OrderBy: string?
  Skip: int = 0
  Take: int?                           // null = MaxTake; larger than MaxTake is a limit error
  Ids: list<TKey>?                     // key restriction through a TVP (export by ids)
  IncludeCount: bool = false
  IncludeInactive: bool = false        // activatable stacks: lifts the default IsActive = true conjunct
  IncludeAncestors: bool = false       // tree stacks
  Arguments: list<QueryArgument> = []

record QueryArgument(Name: string, Type: QueryexType, Value: object?)
record QueryResult(Columns: list<QueryexColumn>, Rows: list<object?[]>, Count: int?, IsCountCapped: bool, Ancestors: list<object?[]>?)

data DetailsRequest
  Expand: list<string>?                // navigation paths; null = every FK navigation at depth 1; [] = none
  Extras: list<string> = []
  EchoSelect: string?
  None: DetailsRequest                 // static: no expand, no extras, no echo (import hydration)

record RelatedEntities(ByEntity: map<string, map<object, object>>)          // logical name → id → partial entity
record DetailsResult<TEntity>(Entity: TEntity, Related: RelatedEntities, Extras: map<string, object?>, RowEcho: object?[]?)
record DetailsSetResult<TEntity, TKey>(Entities: list<TEntity>, Related: RelatedEntities, Extras: map<string, object?>,
                                       RowEchoes: list<object?[]>?, Missing: list<TKey>)

data SaveOptions
  ReturnEntities: bool = true
  Details: DetailsRequest = DetailsRequest.None
  OverrideConcurrency: bool = false    // skips the stamp comparison only
  RequireStamps: bool = false          // a default stamp on an update is `Concurrency.StampRequired`
  Source: SaveSource = Web
enum SaveSource = Web | PublicApi | Agent | Import | System

record SaveResult<TEntity, TKey>(Ids: list<TKey>, Details: DetailsSetResult<TEntity, TKey>?)
record DeleteRequest<TKey>(Ids: list<TKey>, ExpectedStamps: list<datetime2(7)?>?)
record DeleteByQueryRequest(Filter: FilterTree, Arguments: list<QueryArgument>, DryRun: bool = false)
record DeleteResult(Count: int)
record ActionOptions(ReturnEntities: bool = false, Details: DetailsRequest?)
record ActionResult<TEntity, TKey>(Ids: list<TKey>, Details: DetailsSetResult<TEntity, TKey>?)
```

### 3.4 Validation

```contract
// Everything a validator sees during a save; immutable except Errors.
data SaveContext<TEntity, TKey>
  Entities: list<TEntity>              // after preprocessing, ids assigned, payload order
  Before(index: int) -> TEntity?       sync   // before image (null for inserts), loaded through the save grant
  Children: ChildImages                // child before images by child type then parent id
  Options: SaveOptions
  Caller: ICallerContext
  Loader: IContextLoader
  Errors: ValidationErrors
  Round: int                           // 1-based
  IsNew(index: int) -> bool            sync
  Changed<TValue>(index: int, property: TEntity -> TValue) -> bool   sync   // false for inserts

data DeleteContext<TEntity, TKey>      Ids: list<TKey>  Entities: list<TEntity>  Caller  Loader  Errors
data ActionContext<TEntity, TKey>
  Entities: list<TEntity>              // the visible target rows, children when LoadChildren
  Arguments: object?
  Caller: ICallerContext
  Loader: IContextLoader
  Errors: ValidationErrors
  Batch: IPersistBatch
  Save(entities: list<TEntity>)        sync   // marks rows for the emitter with stamping
  Notify(draft: InboxDraft)            sync

// Batched, deduplicated context loading. Every request is a promise resolved by the round's one round trip.
service IContextLoader
  ByIds<T, TK>(ids: list<TK>, select: string?) -> ContextPromise<map<TK, T>>
  ByKey<T, TK>(key: property, values: list<TK>, select: string?) -> ContextPromise<map<TK, T>>     // TVP-restricted on a unique property
  ByParentIds<TChild, TK>(parentIds: list<TK>, select: string?) -> ContextPromise<map<TK, list<TChild>>>
  Query<T>(filter: FilterTree, arguments: list<QueryArgument>?, select: string?) -> ContextPromise<list<T>>
  Visible<T>(filter: FilterTree, arguments: list<QueryArgument>?, select: string?) -> ContextPromise<list<T>>   // read grant conjoined
  Rows(spec: QuerySpec, arguments: list<QueryArgument>) -> ContextPromise<list<object?[]>>
  Exists(root: string, filter: FilterTree, arguments: list<QueryArgument>?) -> ContextPromise<bool>
  Count(root: string, filter: FilterTree, cap: int, arguments: list<QueryArgument>?) -> ContextPromise<int>
  Ancestors<T, TK>(ids: list<TK>) -> ContextPromise<map<TK, list<TK>>>                            // tree stacks
  Sql<TRow>(statement: SqlStatement, reader: RowReader<TRow>) -> ContextPromise<list<TRow>>       // escape hatch; no @qx/@tm names
  Prime<T, TK>(entity: T)              sync

record ContextPromise<T>               // awaitable; IsLoaded; Value throws before the round executes

data ValidationErrors
  HasErrors: bool
  Items: list<ValidationError>
  Add(path: string, code: string, arguments: list<(string, string)>)                       sync
  Add<TEntity, TValue>(index: int, property: TEntity -> TValue, code: string, arguments)   sync   // "[index].Property"
record ValidationError(Path: string, Code: string, Arguments: list<(string, string)>)

// Platform codes; packs and distributions publish theirs beside them.
ValidationCodes: Required | MaxLength | Range | Unique | Entity.NotFound | Entity.DuplicateId | WriteOnce
               | Fk.NotFound | Fk.InUse | Tree.Cycle | Tree.TooDeep | Blob.TokenInvalid | Concurrency.StampRequired
```

### 3.5 Persist, post-commit, and details contexts

```contract
data PersistContext<TEntity, TKey>
  Entities: list<TEntity>
  Before(index: int) -> TEntity?       sync
  Stamp: datetime2(7)                  // the batch's stamp, written to every touched row
  Caller: ICallerContext
  Options: SaveOptions
  Batch: IPersistBatch
  TouchedIdsSource: string             // the batch-local id table (@tm_touched)
  Notify(draft: InboxDraft)            sync

// Append-only surface of the persist batch for participants.
service IPersistBatch
  Add(statement: SqlStatement, writes: list<TableIdentity>)                                       sync
  AddReader<TRow>(statement: SqlStatement, writes: list<TableIdentity>, reader: RowReader<TRow>) -> ResultHandle<TRow>   sync
  AddQuery(spec: QuerySpec, arguments: list<QueryArgument>) -> ResultHandle<object?[]>            sync
  AddTableValuedParameter<TRow>(rows: list<TRow>) -> string                                        sync   // returns the parameter name

data SaveOutcome<TEntity, TKey>
  Entities: list<TEntity>  Before(index) -> TEntity?  Ids: list<TKey>  DeletedIds: list<TKey>  Stamp  Caller
  ResultOf<TRow>(handle: ResultHandle<TRow>) -> list<TRow>   sync

data DetailsPlan<TEntity, TKey>
  Ids: list<TKey>  Request: DetailsRequest  Caller: ICallerContext  IdsSource: string           // @tm_main
  AddExtra<TRow>(name: string, statement: SqlStatement, reader: RowReader<TRow>)                 sync
  AddExtra(name: string, spec: QuerySpec, arguments: list<QueryArgument>)                        sync
```

### 3.6 Errors

```contract
base TellmaException            Code: string   Arguments: list<(string, string)>      // message is for logs
record ValidationException(Errors: list<ValidationError>)                                 : TellmaException   // 422
record NotFoundException(Resource: string, Ids: list<string>)                             : TellmaException   // 404
record ForbiddenException(Code: string, Resource: string, Action: string?)                : TellmaException   // 403
record ConcurrencyException(Code: string, Conflicts: list<ConcurrencyConflict>)            : TellmaException   // 409
record ConcurrencyConflict(Id: string, ModifiedAt: datetime2(7)?, ModifiedById: int?, IsMissing: bool)
record LimitExceededException(Limit: string, Actual: long, Maximum: long)                 : TellmaException   // 413
record InvalidQueryException(Diagnostics: list<QueryexDiagnostic>)                        : TellmaException   // 400
record SessionInvalidException(Reason: string)                                            : TellmaException   // 401
record StaleContextException(Tag: string)                                                 : TellmaException   // 503
TellmaSqlErrors: SessionInvalid = 50401 | Forbidden = 50403 | NotFound = 50404 | Concurrency = 50409
               | StaleContext = 50412 | LimitExceeded = 50413 | Invariant = 50422 | DistroMin = 50500 | DistroMax = 50599
```

### 3.7 The stack descriptor

```contract
record StackDescriptor(Entity: string, Resource: string, EntityType: Type, ServiceType: Type, KeyType: Type,
    Description: string?, Operations: StackOperations, Capabilities: set<string>, Actions: list<ActionDescriptor>,
    SearchableProperties: list<(string, SearchKind)>, NaturalKey: list<string>, DefaultFilter: string?, DefaultSelect: string,
    Children: list<ChildCollectionDescriptor>, ExpandableNavigations: list<string>, Limits: StackLimits)
record ActionDescriptor(Name: string, Permission: string, SupportsFilter: bool, ArgumentsType: Type?, Description: string?,
    IsBuiltIn: bool, PostCheck: bool)
record ChildCollectionDescriptor(Property: string, ChildType: Type, ParentKeyProperty: string)
record StackLimits(MaxTake, CountCap, MaxSaveCount, MaxChildrenPerEntity, MaxIds, MaxDeleteByQueryRows, MaxExpandDepth,
    MaxValidationRounds, MaxSearchLength, MaxExtras, LargeBatchThreshold: int)
service IStackRegistry
  Stacks: list<StackDescriptor>
  Find(entity: string) -> StackDescriptor?      sync
```

### 3.8 Needed from other themes (the exact shape this pipeline calls)

```contract
// spec 0010 — request context (seam 9): scoped holder, copied into job scopes, never AsyncLocal
contract ICallerContext
  TenantId: int  UserId: int?  Subject: string?  Culture: CultureInfo  CalendarId: string
  TimeZone: TimeZoneInfo  Today: DateOnly  IsSandbox: bool

// spec 0010 — feature composition at minimal fidelity (seam 6)
contract ITellmaFeature       Declare(declaration: FeatureDeclaration)   Contribute(contribution: FeatureContribution)
data FeatureDeclaration       Requires<TFeature>()   Entity<TEntity>() -> StackBuilder   Entity<TEntity, TService>() -> StackBuilder
data StackBuilder             WithOperations(operations: StackOperations) -> StackBuilder

// spec 0011 — batch abstraction (seam 1), as consumed
service IDbBatchBuilder
  AddQuery(spec: QuerySpec, arguments: list<QueryArgument>, mayRetry: bool = true) -> ResultHandle<object?[]>
  AddReader<TRow>(statement: SqlStatement, writes: list<TableIdentity>, reader: RowReader<TRow>, mayRetry: bool) -> ResultHandle<TRow>
  Add(statement: SqlStatement, writes: list<TableIdentity>, mayRetry: bool)
  AddSave(spec: SaveSpec)                                  // UPDATE changed / INSERT new / synchronize children; stamps; derives writes
  AddTreeRecompute(table: TableIdentity, affectedIdsSource: string)
  AddTableValuedParameter<TRow>(rows: list<TRow>) -> string
  DeclareIdTable(name: string) -> string                   // batch-local table variable in the @tm_ namespace
  NextBatchOrdinal() -> int
  ExecuteAsync(mode: BatchExecution) -> BatchResult        // ReadOnly | Transaction (wraps in XACT_ABORT/BEGIN TRAN/COMMIT)
record SqlStatement(Text: string, Parameters: list<SqlValue>, Writes: list<TableIdentity>)
record SaveSpec(Rows: list<object>, EntityType: Type, StampsSource: string, SyncedParentsSource: string?, Lane: SaveLane, Stamp: datetime2(7))
enum SaveLane = Small | Large
service IIdAllocator          TakeAsync(entityType: Type, count: int) -> IdRange    AppendRefill(batch: IDbBatchBuilder)
service IDbCallBudget         Record(operation: string, roundTrips: int, duration: TimeSpan)
service IQueryexHost
  Engine: QueryexEngine   Schema(caller: ICallerContext) -> QueryexSchema   Entity<TEntity>() -> EntityDescriptor
  Declare(arguments: list<QueryArgument>) -> list<QueryexParameterDeclaration>
  Bind(query: CompiledQuery, arguments: list<QueryArgument>, caller: ICallerContext) -> list<SqlValue>   // today(), now(), me(), zone
// Engine amendments documented by spec 0011 (spec 0008 is frozen):
//   QuerySpec.Restriction: KeyIn(source) | DescendantOfAny(source) | AncestorOfAny(source), source = a TVP parameter or a
//     batch-local table; KeyIn also over a unique scalar property; part of the cache key by shape, not by values
//   QuerySpec.CountCap: int?   — grand-total count emitted as COUNT(*) over TOP (@cap)
//   QueryCompilationOptions.Sink: ResultSet | KeyTable(name)  — emits INSERT INTO <name> ([Id]) SELECT [T].[Id] …
//   QueryCompilationOptions.KeyListTypes — physical TVP type names per key store type
//   level() -> Numeric — emits the entity's persisted [Level]

// spec 0013 — permissions and the connect statement (seams 11, 16)
service IPermissionEvaluator
  GetAsync(caller: ICallerContext, permissionsTag: Guid) -> PermissionSet
record PermissionSet(Tag: Guid)         For(securable: Securable, bespoke: FilterTree?) -> Grant    sync
record Grant(Allowed: bool, Filter: FilterTree?, Reasons: list<GrantReason>)               // empty disjunction is denial
record Securable(Resource: string, Action: string, SupportsFilter: bool, FilterRoot: string?)
service ISecurableRegistry    Add(securable: Securable, description: string?)   All: list<Securable>
service IConnectStatementSource
  AppendConnect(batch: IDbBatchBuilder)                                            // reads: subject → user, activity stamp, tags
  AppendGuard(batch: IDbBatchBuilder, expected: ConnectSnapshot)                    // writes: the D13 prologue, inside the transaction
  ConnectAsync(caller: ICallerContext) -> ConnectSnapshot                           // the dedicated connect trip
record ConnectSnapshot(UserId: int, PermissionsTag: Guid, SettingsTag: Guid, UserSettingsTag: Guid)

// spec 0012 — tags (seam 5): core.TenantStamps (SettingsTag, SecurablesTag, cacheable-entity tags) and
//   core.UserStamps (PermissionsTag, UserSettingsTag, LastActiveAt); ITagRegistry.AppendBumps(batch, writtenTables)

// spec 0016 — blob staging (seam 12)
service IBlobStaging
  AppendValidate(batch: IDbBatchBuilder, tokens: list<string>) -> ResultHandle<string>   // invalid tokens → Blob.TokenInvalid
  AppendConfirm(batch: IDbBatchBuilder, tokens: list<string>)
  DeleteAsync(blobIds: list<string>)

// spec 0019 — notifications and tasks (seams 8, 15)
record InboxDraft(UserId: int, Type: string, Title: string, Body: string?, Entity: string?, EntityId: object?, Data: map<string, string>?)
service IInboxWriter          Append(batch: IDbBatchBuilder, drafts: list<InboxDraft>)   NudgeAsync(userIds: list<int>)
service ITaskEnqueuer         Append(batch: IDbBatchBuilder, tasks: list<TaskDraft>)
```

---

## 4. Schema

This theme owns no business table. It fixes the column contracts every stack's table carries,
two standalone table types, the reserved error numbers (D13), and the exact shape it needs from
the stamp tables other themes own.

**Every top-level stack table (spec 0011 emits; base `AuditedEntity`).**

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `<TKey>` (`int` default) | no | PK, sequence `sq_<Table>` | app-assigned |
| `CreatedAt` | `datetime2(7)` | no | | UTC; server-owned |
| `CreatedById` | `int` | no | FK `core.Users`, `FK_<Table>_CreatedById`, no cascade | server-owned |
| `ModifiedAt` | `datetime2(7)` | no | | UTC; the concurrency stamp; bumped by every platform write, never by bookkeeping |
| `ModifiedById` | `int` | no | FK `core.Users`, `FK_<Table>_ModifiedById` | server-owned |

No `rowversion`; no index on `ModifiedAt` (compared by primary key). Temporal tables keep the
same four columns; period columns are history.

**Activatable (`IActivatable`).**

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `IsActive` | `bit` | no | `DF_<Table>_IsActive DEFAULT (1)` | optional filtered index `IX_<Table>_Active ON (Id) WHERE IsActive = 1` on large tables |

**Tree (`ITreeEntity<TKey>`).**

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `ParentId` | `<TKey>` | yes | FK same table, `FK_<Table>_ParentId`, no cascade; `IX_<Table>_ParentId` | |
| `Node` | `hierarchyid` | yes | `UX_<Table>_Node UNIQUE (Node) WHERE Node IS NOT NULL` (depth-first) | platform-owned; excluded from the UDTT; never on the wire; null only between insert and recompute inside one transaction |
| `Level` | `AS ([Node].GetLevel()) PERSISTED` | | optional `IX_<Table>_Level_Node (Level, Node)` | read by `level()` |
| `SubtreeCount` | `int` | no | `DF_<Table>_SubtreeCount DEFAULT (1)` | self + descendants |
| `ActiveSubtreeCount` | `int` | no | `DF_<Table>_ActiveSubtreeCount DEFAULT (1)` | active self + active descendants; equals `SubtreeCount` when not activatable |

**Child (weak) tables.**

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK, sequence `sq_<Table>` | |
| `<ParentKey>` | `int` | no | FK parent, `FK_<Table>_<ParentKey>`; `IX_<Table>_<ParentKey> (<ParentKey>) INCLUDE (Id)` | the synchronization predicate seeks on it |

No audit columns, no stamp (the parent's stamp guards the aggregate).

**Uniqueness declarations (spec 0011 emits from `[Unique]` / `[NaturalKey]`).** One index per
declaration, `UX_<Table>_<Col>[_<Col>]`, filtered `WHERE <Col> IS NOT NULL` for nullable columns;
the deterministic name is what 2601/2627 map back to a property path. FK constraints are named
`FK_<Table>_<Column>` so 547 maps to a property path.

**Worked example — `gl.Centers`.**

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK, `sq_Centers` | |
| `Name` | `nvarchar(255)` | no | | |
| `Name2` | `nvarchar(255)` | yes | | |
| `Name3` | `nvarchar(255)` | yes | | |
| `Code` | `nvarchar(50)` | no | `UX_Centers_Code` | natural key |
| `CenterType` | `varchar(20)` | no | CHECK on the enum's names | enum as string |
| `IsActive` | `bit` | no | `DEFAULT (1)` | |
| `ParentId` | `int` | yes | FK `gl.Centers`; `IX_Centers_ParentId` | |
| `Node` | `hierarchyid` | yes | `UX_Centers_Node` filtered | platform-owned |
| `Level` | computed persisted | | | |
| `SubtreeCount` | `int` | no | `DEFAULT (1)` | |
| `ActiveSubtreeCount` | `int` | no | `DEFAULT (1)` | |
| `CreatedAt`, `CreatedById`, `ModifiedAt`, `ModifiedById` | | | as above | |

UDTT `[gl].[CentersList_<hash8>]`: every column above except `Node` and `Level`; `Id` mirrored as
the key.

**Standalone table types the pipeline binds** (spec 0001 class-derived route; plain classes in
`Tellma.Core.Abstractions.TableTypes`).

| Type | Columns | Use |
|---|---|---|
| `IdList` (exists) | `Id int PK` | `@tm_ids`, `@tm_touched`, `@tm_synced`, `@tm_affected`, `@tm_new`, `@tm_upd` |
| `SaveStampList` (new) | `Id int PK`, `ExpectedStamp datetime2(7) NULL` | expected stamps for updated rows (`@tm_stamps`) |

**Stamp tables the pipeline reads (owned by spec 0012 / 0013; the D13 prologue depends on this
exact shape).**

| Table | Columns the pipeline needs | Notes |
|---|---|---|
| `core.UserStamps` | `UserId int PK FK core.Users`, `PermissionsTag uniqueidentifier NOT NULL`, `UserSettingsTag uniqueidentifier NOT NULL`, `LastActiveAt datetime2(3) NULL` | non-temporal; every write that can change the user's effective permissions sets `PermissionsTag = NEWID()` in the same transaction; the persist prologue takes `UPDLOCK` on the caller's row |
| `core.TenantStamps` | one row; `SettingsTag`, `SecurablesTag`, one tag per cacheable entity type (`uniqueidentifier NOT NULL`) | non-temporal |
| `core.Users` | `Id`, `Subject`, `IsActive`, `Name*` | the connect statement resolves the subject; conflict display names |

**Reserved namespaces.** Platform raw statements use `@tm_` variables, parameters, and table
variables; compiled queries use `@qx{b}_`; distribution raw SQL must use neither.

---

## 5. Answers

| Brain-dump question (abridged) | Answer | Where |
|---|---|---|
| Are Data / Service / Web the proper layer names? | Conventional folders in one distribution project: `Data`, `Services`, `Api`; spec 0010 fixes them. The platform's own names are namespaces (`Tellma.Core.Abstractions.Crud`, `Tellma.Core.Crud`). | D1 |
| Record + blobs: which pattern? | Staged upload with tokens validated in RT1, confirmed in RT2, replaced blobs deleted after commit; no multipart save; no pre-commit non-transactional step. | D16 |
| Write-once columns: two UDTTs or a service rule? | One row image; `[WriteOnce]` validated against the before image (`WriteOnce` error), never silently reset. | D11 |
| `SavedById` on weak entities? | No; children carry no audit columns or stamp; the parent's stamp is bumped by any child change. | D14 |
| HierarchyId maintenance: in memory or SQL after save? | SQL recompute appended to the persist batch over affected roots with id-based paths; cycles validated in C# and guarded by `MAXRECURSION`. | D20 |
| How to model `CenterType`? | C# enum stored as a string with a CHECK (spec 0011 convention); Queryex compares `'Service'`. | §4 |
| Extensibility on interfaces; validate the interface matches the DB? | Generic pack services and hooks closed over the leaf; components resolved along the type chain; no interface/DB check (the class is the table). | D2, D21 |
| Names for the emitter and the multi-statement executor? | Positions: `SaveEmitter`; `DbBatchBuilder` + `DbBatchExecutor` (spec 0011 decides). | seam 1 |
| Does the emitter need to know upsert vs synchronize? | No flag: top-level rows upsert, nested collections synchronize under the parents whose collection was present. | D12 |
| Id ranges without a dedicated round trip; who maintains; un-consume; who assigns? | Background-prefetched buffer; a cold buffer is a rare metered extra trip; the pipeline assigns before validation; never un-consumed; temporary ids `< 0` rewritten by the pipeline. | D9 |
| Keep `Search`? Picker-vs-page hint? | Keep, server-side over `[Searchable]` columns (convention `Name*`, `Code`); no hint. | D6 |
| Ancestors: another array of arrays? | Yes, `QueryResult.Ancestors`, in the same round trip through the key-table restriction. | D5, D18 |
| Count stops beyond 9999? | `TOP (@cap)`-wrapped count via the `CountCap` amendment; `IsCountCapped`; cap 10 000. | D5 |
| Details: Queryex or raw SQL? | Both by role: Queryex for expressions and grants, model-emitted keyed SQL for reads by id; raw SQL only through `Extras`. | D18 |
| Start the transaction — is this right? | No; the transaction is the persist batch text only. | D13 |
| One DB call loads validation context, sometimes a second? | DataLoader rounds bounded at 3, metered; round 1 rides the pipeline's own loads. | D10 |
| Inject custom validators that participate in the batch load? | `ValidateAsync` hook and `IEntityValidator<T>` components awaiting `ContextPromise<T>` from `IContextLoader`. | D10 |
| Dedupe context queries? | Structural keys; key values unioned; differing selects unioned. | D10 |
| Collapse DB calls #1 and #2 with cached permissions? | Yes: connect rides the first batch; stale → refresh and re-run once; the persist prologue re-verifies under `UPDLOCK` on the caller's stamp row. | D7, D13 |
| RLS fails on save → roll back and forbidden? | `THROW 50403` inside the transaction after the DML, before `COMMIT`. | D13, D15 |
| Empty vs missing child collection? | `[]` deletes all; `null` untouched (skipped, not hydrated). | D12 |
| Save admits a single entity? | The service takes an array; the details page sends one. | D4 |
| Import reuses the pipeline; must stay fast? | Same `SaveAsync` with `ReturnEntities = false`; large-batch plan lane; per-chunk transactions. | D22, D13 |
| Base service class vs composition? | Thin base as the authoring surface; sealed pipeline; DI components for multiplicity. | D2 |
| 5 ids requested, 4 found? | Bulk read: 4 plus `Missing`; single read: 404; delete: all-or-nothing 404. | D4, D17 |
| TVP list restriction and `level()` on the engine? | Required amendments: `Restriction` (`KeyIn`/`DescendantOfAny`/`AncestorOfAny`), `CountCap`, `Sink`, `KeyListTypes`, `level()`. | seam 4 |
| Capability boilerplate reused across distros? | Interface on the entity → projected everywhere; `[EntityAction]` for custom logic; `StackDescriptor` as the single output. | D19, D3 |
| `UserService` custom endpoints? | Bulk ones (`invite`) are `[EntityAction]`s; self-service ones are ordinary methods on `UserService<TUser>` mapped by the feature (spec 0017). | D2, D19 |
| Extract endpoint boilerplate; source-generated JSON; `X-Today`? | Positions for spec 0015: endpoints projected from `StackDescriptor`; STJ source-generated contexts; carry time zone and calendar as headers and bind `today()` from `ICallerContext.Today`. | seam 13 |
| Registering securables hard to forget? | Every projected operation carries a securable from the descriptor; an operation without one is not projected; startup audit over the endpoint table. | D3, D24 |
| Is "securable" the right word? | Yes. | D24 |
| Permissions invalidated by schema change? | Fail closed per grant (spec 0013); a non-compiling grant contributes nothing and never blocks the user's other grants. | seam 11 |
| Alternative to `rowversion`, implementable in C#? | `ModifiedAt` as the stamp, app-generated per batch, checked under `UPDLOCK` inside the persist transaction; override flag skips the comparison only. | D14 |
| Rate and payload limits? | `[Stack]` ceilings at the service; per-surface overrides in spec 0015. | D5 |
| Guarantee the cache tag is bumped on every write? | Write sets on every statement; automatic bumps from their union; no connection outside a batch; analyzers. | D25 |
| Exceptions: enumerate or interface? | A closed set of eight sealed `TellmaException`s; spec 0015 maps by type. | D23 |
| Messages or codes? | Codes plus arguments from the pipeline; spec 0015 localizes and returns both. | D10, D23 |
| Notification without another round trip? | `PersistContext.Notify` / `IInboxWriter.Append` on the persist batch; the nudge after commit. | D16 |

---

## 6. Seams

1. **Batch abstraction (spec 0011 owns).** Needed: `IDbBatchBuilder` as in §3.8 — compiled
   queries with materializers, raw statements with declared write sets, `AddSave`,
   `AddTreeRecompute`, TVPs, batch-local id tables in the `@tm_` namespace, `NextBatchOrdinal`,
   `BatchExecution.Transaction` wrapping the text in `XACT_ABORT ON`/`BEGIN TRAN`/`COMMIT`;
   per-statement `mayRetry` AND-ed into round-trip eligibility; an executor that re-runs a whole
   batch on transient errors under the D13 rules, runs the commit probe, maps 50401–50599,
   2601/2627, 547, 530, and reads result sets preceding a `THROW`; `IDbCallBudget` incremented per
   round trip. Concatenated text on one `SqlCommand` walked with `NextResult()`, never `SqlBatch`.
   The emitter: UPDATE changed rows only (`EXCEPT`), INSERT new, child DELETE/UPDATE/INSERT under
   `@tm_synced`, parents before children on insert and the reverse on delete, tables in model
   order, `[ModifiedAt] = @tm_stamp` on every written row, `OPTION (RECOMPILE)` in the large lane.
2. **Entity class vs wire shape (spec 0011 owns; this theme consumes).** One class; `[NotMapped]`
   child collections declared with `[Children(parentKey)]` and `null`/`[]` semantics;
   `[ServerOwned]`, `[WriteOnce]`, `[GatedBy]`, `[Unique]`, `[NaturalKey]`, `[Searchable]`,
   `[PreserveWhitespace]` on properties; `Id ≤ 0` new, `< 0` temporary. Risks named and closed:
   server-owned client values overwritten (D11); write-once validated (D11); foreign child ids
   rejected (D12); the stamp string round-trips verbatim (D14).
3. **One capability, declared once (this theme owns).** The entity interface is the declaration;
   spec 0011 supplies the columns, spec 0013 registers the securables the descriptor lists, spec
   0015 projects endpoints from `Operations`, `Capabilities`, `Actions`, the SPA and MCP read the
   descriptor for default filters and buttons. Every consumer reads `StackDescriptor`; none
   re-derives.
4. **Queryex schema per tenant (spec 0011 owns).** Schema identity changes when `SettingsTag`
   changes (D7 re-run); `Name2`/`Name3` gating; the entity's `TreeNode` mapped to the
   platform-owned `Node`; the engine amendments of §3.8. Weak-entity path rewriting is spec 0013's
   and not used here (children are never query roots in this pipeline).
5. **Version tags (spec 0012 owns).** The connect statement returns `SettingsTag`,
   `PermissionsTag`, `UserSettingsTag`; `core.UserStamps` and `core.TenantStamps` have the shape
   in §4; entity-type tags are bumped by the executor from write sets; tags are opaque
   `uniqueidentifier`s regenerated with `NEWID()` (restore-safe, equality only).
6. **Feature composition (spec 0010 owns).** `FeatureDeclaration.Entity<T>()` /
   `Entity<T, TService>()` returning a `StackBuilder`; `Requires<T>()` only; one aggregated
   startup validation into which the stack registry reports D3's failures. The module package and
   the distribution use the same call.
7. **Natural keys (spec 0011 owns; spec 0018 consumes).** `[NaturalKey]` implies `[Unique]`;
   inference when absent: unique required `Name`, else unique required `Code`, else the first
   unique required string, else the first unique string, else none (export-for-import refuses
   with a clear error rather than falling back to surrogate ids across tenants). The descriptor
   exposes it; `GetByIdsAsync(ids, DetailsRequest.None)` is the hydration path.
8. **Background-task columns and lease statements (spec 0019 owns semantics).** Not emitted by
   this pipeline; task rows are appended through `ITaskEnqueuer` in `ContributeAsync`; the runner
   is nudged in `AfterCommitAsync`; a task entity is an ordinary stack whose lease columns are
   `[ServerOwned]` and written by the runner's own statements, which never touch `ModifiedAt`.
9. **Request context (spec 0010 owns).** `ICallerContext` as in §3.8, a scoped holder copied
   into job scopes; this pipeline binds `today()` from `Today`, `me()` from `UserId`, the zone from
   `TimeZone`, and never reads `HttpContext`.
10. **Platform exceptions (this theme owns the types; spec 0015 maps).** D23 with the suggested
    statuses; `ValidationError.Path` is CLR-named and spec 0015 applies its JSON naming policy.
11. **Permission evaluation (spec 0013 owns).** `IPermissionEvaluator.GetAsync(caller, tag)` →
    `PermissionSet.For(securable, bespoke)` → `Grant`; `save` implies `read`; an empty
    disjunction is denial; bespoke grants disjoined through `BespokeGrant`; a grant whose filter
    fails to compile contributes nothing. The `activate` securable is registered by the
    capability with `FilterRoot = <entity>`. `IConnectStatementSource` emits the connect
    statement, the write prologue, and performs the connect trip.
12. **Blob staging tokens (spec 0016 owns).** `IBlobStaging.AppendValidate` in RT1,
    `AppendConfirm` in RT2, `DeleteAsync` after commit; the built-in `BlobAttachmentEffect<T>`
    implements the recipe for every `IHasImage`/`[BlobReference]` property.
13. **Wire shapes (spec 0015 owns).** `QueryResult` as arrays of arrays with columns; details and
    save envelopes as in §3.3; the RFC 9457 `errors` dictionary keyed by path with both code and
    message; the concurrency conflicts as an extension member of the 409 problem; the stamp
    round-trip rule; `RequireStamps = true` on the details-page save; `DeleteByQuery` web-only;
    `DisableValidation()` on save endpoints.
14. **Telemetry (spec 0011 owns the round-trip instrument; this theme names its own).** D26.
15. **Notification enqueue riding the save (spec 0019 owns).** `IInboxWriter.Append` from
    `PersistContext.Notify` / `ActionContext.Notify`; `NudgeAsync` after commit.
16. **Connect-call collapse (spec 0013 and this theme).** Adopted with D7's failure modes for
    reads and closed for writes by the D13 prologue's `UPDLOCK` on the caller's stamp row; the
    activity stamp throttled to once per minute so a read does not write.
17. **Vocabulary.** `CreatedAt/CreatedById/ModifiedAt/ModifiedById` on every top-level entity;
    `ModifiedAt` as the stamp; temporal as an additive table option; no audit columns on weak
    entities; plural schema-qualified tables, singular logical names; `int` keys by default,
    `long` by `Entity<long>`; "securable" for the tuple, "action" for the verb, "stack" for the
    unit, "tag" for cache versions; `core.UserStamps`/`core.TenantStamps` for the churn columns.

---

## 7. Departures

| ARCHITECTURE.md | Departure | Reason |
|---|---|---|
| Dependency rules: only `Tellma.Core` references `Tellma.Core.Queryex`; the Abstractions README says "no package references, ever". | `Tellma.Core.Abstractions` references `Tellma.Core.Queryex`. | `FilterTree`, `QuerySpec`, `QueryexColumn`, `QueryexType`, `QueryexDiagnostic` appear in the contracts every module consumes; mirroring them creates two types per concept and the drift `FilterTree` exists to prevent. Queryex has no package or project reference (verified), so the edge carries no weight — the README's stated reason for its rule. The README and the dependency diagram change. |
| Endpoints projected as read → GET, save → POST, delete → DELETE. | The projection is spec 0015's; this theme requires only that `DeleteByQuery` is web-only and that an operation without a securable is not projected. | Guardrails on a dangerous operation; request shapes assume bodies. |
| "Pack code that needs lines issues two explicit bulk queries and stitches in C#." | The details read stitches inside the platform in one round trip (several result sets); pack code never writes the stitch. | Same principle, moved into Core. |
| "Context loading: C# → typed bulk queries (Queryex)." | Keyed context loads (by id, by parent id, by natural key) are model-emitted SQL; Queryex remains the only path for user- or permission-authored text. | Keyed reads have no expression to compile and must be seekable and plan-stable. |
| Capability interfaces are for per-feature column gating only. | Capability interfaces are also the single declaration of a stack capability (operations, securables, gates, default filters). | Reconciles "declared once" with "no paired interface per entity": the interface is per capability, never per entity. |
| No shadow properties on mapped entities. | `Node` on tree entities is a platform-configured, platform-written column absent from the class, the wire, and the table type. | Abstractions cannot expose `HierarchyId` or `[ExcludeFromTableType]` (both EF-package types). Whether it is a shadow property or a converter-backed platform property is spec 0011's call; the rule narrows to "no *implicit* shadow properties". |
| `SqlBuilder<T>` and raw `Sql(...)` as the tier-2 escape hatch. | Retained, but every raw statement declares its write set and an analyzer checks it; `DbContext.SaveChanges*` is forbidden in pack and distribution code. | Without this a write path bypasses tag bumps and stamps silently. |
| "Every UPDATE writes a history row" accepted knowingly. | The emitter skips unchanged top-level rows; parents whose children changed are still bumped. | The stamp then means "something changed", which the concurrency prompt relies on. |
| Feature composition in full. | Minimal fidelity: `Requires` edges only, no manifest generator, no Builder tool, explicit stack registration with a startup tripwire. | The breakdown's "first release builds it at minimal fidelity". |
| Audit vocabulary (silent); the brain dump uses two. | One vocabulary (seam 17). | The concurrency design needs `ModifiedAt` on every editable top-level table. |

---

## 8. Verification

Facts relied on, verified 2026-09-01 in the research file (`research/service-pipeline.md`) and
the briefing digest unless noted otherwise:

- SqlClient's built-in retry is inert for any command with a `SqlTransaction` or ambient scope;
  `SqlBatch` has no `RetryLogicProvider`; the baseline transient list; 7.0 exposes
  `BaselineTransientErrors` (research §3).
- `TransactionScope` defaults (Serializable, one minute, async flow suppressed); distributed
  transactions throw off Windows (research §2.3–2.4).
- `SET XACT_ABORT ON` rolls back the whole transaction on any run-time error and `THROW` honours
  it while `RAISERROR` does not (research §2.5).
- RCSI is on by default on Azure SQL and off on-premise; Azure SQL always runs optimized locking,
  disabled on statements carrying `UPDLOCK`/`HOLDLOCK`/`OUTPUT`; C# uniqueness checks are
  write-skew-prone under RCSI and SNAPSHOT; 2601/2627 are the guarantee; `UPDLOCK` on a
  select-then-update is the documented fix; the stamp check inside the write is a correct
  row-level conflict check (research §6).
- The .NET 10 minimal-API validator is a per-parameter endpoint filter with early returns,
  synchronous, PascalCase keys (dotnet/aspnetcore #61764 open), `Lines[3].Quantity` grammar
  shared with FluentValidation; `IAsyncValidatableObject` is .NET 11; FluentValidation 12.1.1 has
  no batching; GreenDonut 16.6.2 batches per loader; the DataLoader contract (research §1, §4).
- OpenTelemetry DB conventions are stable; the SqlClient instrumentation emits stable names;
  `IHttpActivityFeature`, `IMeterFactory`; the repo's naming rules (research §5; ARCHITECTURE.md
  Observability).
- `MERGE` is out; `NEXT VALUE FOR` is banned inside it; `hierarchyid` cannot ride a TVP;
  `Level` as computed `GetLevel()`; every temporal `UPDATE` writes a history row; period columns
  are shadow properties in EF 10; JSON stays `nvarchar(max)`; staged uploads are the surveyed
  pattern; `AsyncLocal` must not carry tenant context; an app-generated `Guid` tag is restore-safe
  and `rowversion` moves on bookkeeping (briefing digest §8).
- Queryex facts read from the frozen spec and the code: `CompileQuery` is the only door to SQL;
  `FilterTree.Or([])` is false and `Leaf("")` is a caller error; `@qx{b}_p{n}` / `@qx{b}_v{n}`
  namespacing with `BatchOrdinal`; `QueryexLimits.MaxParameters = 512`, `MaxListItems = 128`;
  string predicates emit `CHARINDEX`-style without pattern metacharacters (spec 0008 §10.9, line
  1757, and the summary rule at line 2784); `descendantOf`/`ancestorOf` exist and `level()` does
  not (§10.10); `EntityDescriptor.TreeNode` and `EntityBuilder.TreeNode(name)` exist in
  `QueryexSchemaBuilder.cs`; `Validate` mints the language-version stamp; caches key on schema
  identity; `QueryexParameterSlot` origins (`Today` is "the current date in the tenant's time
  zone") (spec 0008 §1.4, §2, §13.1, §13.4, §15).
- Table-type facts: `[TableType]` opt-in; the four bulk lists as plain classes in
  `Tellma.Core.Abstractions.TableTypes` (`BulkLists.cs`); physical names `<Logical>_<hash8>`
  addressed only through `model.GetTableTypes()`; metadata-driven binding with the ordinal
  analyzer deferred to the save-pipeline spec; `ExcludeFromTableTypeAttribute` lives in
  `Tellma.Core.EntityFrameworkCore` (verified in the working tree), not in Abstractions (spec
  0001 §5–6).
- Package facts verified in the working tree: `Tellma.Core.Abstractions.csproj` has no references;
  `Tellma.Core.Queryex.csproj` has "deliberately no PackageReference and no ProjectReference"; the
  Abstractions README says "no package references, ever" (departure 1 changes it).
- Table-variable deferred compilation (compatibility level 150+) compiles a statement on first
  execution with that execution's row count and caches the plan — verified by a proposal author
  on learn.microsoft.com (ms.date 2026-06-12); the applicability to TVPs is inferred, which is
  why the lane threshold is a review flag to be measured.
- BCL `Validator.TryValidateObject` runs property attributes, then type-level attributes, then
  `IValidatableObject` only if no earlier error, and does not recurse into child objects —
  verified by proposal authors in dotnet/runtime `Validator.cs` and the docs page (ms.date
  2025-07-01); the pipeline therefore owns the attribute walk.
- `THROW` needs error numbers ≥ 50 000, severity 16 — verified by a proposal author on
  learn.microsoft.com (page updated 2026-08-24).

Unverified or left to implementation:

- The parking scheduler (D10) is a design, not a measured implementation; it needs the property
  test named in D28.
- Whether `INSERT INTO @t … SELECT …` through the key-table sink preserves the compiled query's
  paging semantics when rows are also wanted (the design re-applies `OrderBy` on the restricted
  page instead of relying on `OUTPUT` order, so nothing depends on it).
- The relative cost of the `IsDescendantOf` count refresh versus a second recursive CTE at Center
  scale; whether `OPTION (RECOMPILE)` on a TVP-sourced `UPDATE … EXCEPT` produces seek plans at
  50 000 rows (measure on the fixture).
- Whether a self-referencing foreign key tolerates deleting a parent and its children in one
  ordered `DELETE` in every case (constraint checking at statement end is documented for single
  statements; the design deletes deepest first regardless).
- The ScriptDom write-set analyzer's coverage of dynamically composed raw SQL.
- Whether `Properties<Enum>()` pre-convention configuration matches nullable enums (spec 0011).

---

## 9. Review flags

1. **Explicit stack registration plus a startup tripwire** (`Entity<Center, CenterService>()`; an
   unregistered class deriving from `EntityService<T>` fails startup) versus convention discovery
   of the service class. Explicit is one more generic argument; discovery is one less thing to
   write and one more thing to explain.
2. **One authoring base class merging operations and hooks** (`CenterService : EntityService<Center>`)
   versus a sealed platform service plus a separate behavior class. The merged form matches the
   brain dump's names and gives non-bulk custom operations a home; the split form keeps the
   author's class free of the operation surface.
3. **One `activate` securable for both directions** versus `activate` and `deactivate` securables.
4. **Strict all-or-nothing delete by ids** (404 naming the offending ids) versus best-effort with a
   per-id outcome list.
5. **Echoing `ModifiedAt` as the expected stamp** (one class, one wire member, default = no check)
   versus a separate `[NotMapped] ExpectedStamp` member (explicit semantics, one more property on
   every entity).
6. **Write-once as a validation error** versus silent reset to the before image.
7. **Early concurrency detection in RT1** kept as a fast-fail before validators run; a purist
   design keeps only the authoritative RT2 check.
8. **`CountCap = 10 000`** versus the brain dump's 9 999.
9. **Ids from a background-prefetched buffer before validation** (a cold buffer costs one rare
   extra trip) versus a payload-sized reservation riding RT1 (never an extra trip, but validators
   run before ids exist or RT1 splits).
10. **Read-back inside the transaction** (returns exactly what was committed) versus after
    `COMMIT` (shorter locks by a few milliseconds, may show a later concurrent change).
11. **Plan lanes by `OPTION (RECOMPILE)` above 1 000 TVP rows** versus a size-bucket comment token
    yielding a second cached plan per lane; the threshold is to be measured.
12. **The one-minute `LastActiveAt` throttle** (one write per user per minute) versus stamping
    every request.
13. **Related entities in a details read are not row-level filtered** (a deliberate leak of the
    names of referenced rows to a user who could not list them) versus filtering them and showing
    blanks, which breaks the page for ordinary users.
14. **`Node` as a platform-configured shadow column** versus a converter-backed platform property
    (spec 0011's call); id-based hierarchy paths versus `ROW_NUMBER` sibling numbering.
15. **No entity tag on metrics** (entity in spans and logs) versus `crud.entity` as a closed-set
    tag; the brain dump asks for per-entity counts, cardinality argues against.
16. **`SessionInvalidException` → 401** (the host ends the session) versus 403 for a deactivated
    user.
17. **Schema-qualified singular resource names** (`gl.Center`) versus bare logical names
    (`Center`) versus plural table identities (`gl.Centers`) — spec 0013 owns the scheme.
18. **`MaxSaveCount = 10 000`** (matches the "accidentally imported 10K records" story) versus a
    smaller per-request ceiling (5 000) with the codec chunking earlier.

---

## 10. Conflicts

Positions other themes must reconcile with this one:

1. **Resource naming (spec 0013).** This theme's position is `<schema>.<LogicalName>`
   (`gl.Center`); spec 0013 owns the scheme and must also accept lowercase action verbs
   (`read`, `save`, `delete`, `activate`, custom names) that double as endpoint segments.
2. **`Tellma.Core.Abstractions` → `Tellma.Core.Queryex` (spec 0011, ARCHITECTURE.md).** The
   entity contract and the Queryex host adapter sit on the same seam; spec 0011 must accept the
   edge or supply an alternative home for `FilterTree` in contracts.
3. **Entity-contract annotations (spec 0011).** `[ServerOwned]`, `[WriteOnce]`, `[GatedBy]`,
   `[Unique]`, `[NaturalKey]` (implies `[Unique]`), `[Searchable(Kind)]`, `[PreserveWhitespace]`,
   `[Children(parentKey)]`, `IEntity<TKey>`, `IAudited`, `IActivatable`, `ITreeEntity<TKey>`
   (`ParentId`, `SubtreeCount`, `ActiveSubtreeCount`; no `Node`, no `Level` on the class),
   `IMultilingual`, `[Temporal]`, `[Stack]`, temporary ids `< 0`.
4. **Engine amendments (spec 0011 documents).** `QuerySpec.Restriction` (`KeyIn` /
   `DescendantOfAny` / `AncestorOfAny` over a TVP or batch-local table), `QuerySpec.CountCap`,
   `QueryCompilationOptions.Sink = KeyTable(name)`, `KeyListTypes`, `level()` reading the
   persisted `Level`.
5. **Batch and emitter behaviour (spec 0011).** Concatenated text, never `SqlBatch`; the executor
   owns whole-batch retry, the commit probe, and the 50401–50599 / 2601 / 2627 / 547 / 530 map;
   result sets preceding a `THROW` are readable; the emitter skips unchanged rows with `EXCEPT`,
   stamps `ModifiedAt` from a C#-computed batch stamp (not `SYSUTCDATETIME()`), bounds child
   deletes to `@tm_synced`, supports `SaveLane.Large` with `OPTION (RECOMPILE)`; the allocator's
   `TakeAsync` warm fast path and `AppendRefill`; `SaveStampList` as a new standalone table type;
   the ScriptDom write-set analyzer ships beside the ordinal-binding analyzer.
6. **Stamp tables and the connect statement (spec 0012, spec 0013).** `core.UserStamps`
   (`PermissionsTag`, `UserSettingsTag`, `LastActiveAt`) and `core.TenantStamps` (`SettingsTag`,
   `SecurablesTag`, per-type tags) must have the shapes in §4; every write that can change a
   user's effective permissions bumps that user's `PermissionsTag` row in the same transaction
   (the D13 prologue's `UPDLOCK` depends on it); the connect statement is read-only apart from
   the once-per-minute activity stamp; `SettingsTag` is part of the schema identity.
7. **Status mapping (spec 0015).** `SessionInvalidException` → 401 (the host ends the session,
   spec 0010), `StaleContextException` → 503 with `Retry-After`, `InvalidQueryException` → 400
   with diagnostics; `RequireStamps = true` on the details-page save; the stamp string
   round-trips verbatim; `DeleteByQuery` web-only; `DisableValidation()` on save endpoints; error
   paths CLR-named and renamed by the JSON policy; `crud.source` set per surface.
8. **Blob staging (spec 0016).** `IBlobStaging.AppendValidate` (RT1), `AppendConfirm` (RT2),
   `DeleteAsync` (after commit); tokens travel in the `ImageId` / `[BlobReference]` property; the
   orphan sweep is spec 0019's consumer.
9. **Inbox and tasks (spec 0019).** `InboxDraft` and `IInboxWriter.Append` / `NudgeAsync`;
   `ITaskEnqueuer.Append` for durable post-save work; lease columns never touch `ModifiedAt`.
10. **Excel (spec 0018).** Import calls `SaveAsync` with `Source = Import`, `ReturnEntities =
    false`, `RequireStamps = false`, chunks ≤ `MaxSaveCount`; hydration through
    `GetByIdsAsync(ids, DetailsRequest.None)`; natural-key translation is the codec's.
11. **Feature composition (spec 0010).** `FeatureDeclaration.Entity<T>()` / `Entity<T,
    TService>()` and the aggregated startup validation must carry D3's stack diagnostics;
    `ICallerContext` with `UserId: int?` (null before connect resolves it).
12. **Core services (spec 0017).** `UserService<TUser>` and `RoleService<TRole>` are
    `EntityService` subclasses: `invite` is an `[EntityAction]`; self-service profile and
    preference operations are public methods on `UserService<TUser>` mapped by the Core feature;
    `RoleService.ContributeAsync` bumps the members' `PermissionsTag`; the self-lockout guards are
    `ValidateAsync`/`ValidateDeleteAsync`/`ValidateActionAsync("deactivate")` overrides;
    `Center`'s grouping rule is a `CenterService<TCenter>` validator.
13. **Vocabulary (all).** Four audit columns on every top-level entity and none on children;
    `ModifiedAt` as the stamp; plural schema-qualified tables; "tag", "securable", "stack";
    `core.UserStamps`/`core.TenantStamps` versus any `UserActivities`/`TenantTags` naming another
    theme chose.
