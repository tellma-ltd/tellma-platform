# T5 — CRUD service pipeline and capabilities (future spec 0014)

Designed under the correctness, security, and long-term-maintainability lens: fail-closed access
control, no silent data loss, concurrency soundness under RCSI and optimized locking, schema
evolution with N−1 apps, testability, and clean package seams. Where this lens and the other two
conflict, the choice below names the cost it accepts.

Everything here is written for a reader who has not seen the brain dump or the other design files.
Names are final-looking. C# is compilable-looking (types, members, XML summaries) but block bodies
and file headers are omitted for brevity.

---

## 1. Critique

### 1.1 The general shape is right; the save flow is ordered wrong in three places

The brain dump's service layer — one bulk-shaped pipeline reused by details-page save and by Excel
import, RLS enforced as a composed `FilterTree`, validators that load context in one batch, side
effects riding the same transaction — is the correct shape. Three orderings inside it are not:

1. **The transaction starts before validation (step 4 before step 5).** Under `READ COMMITTED
   SNAPSHOT` (the default on Azure SQL, and what the migrator must turn on for on-prem so there is
   one isolation story) a read inside an open transaction is not protected by that transaction:
   each statement sees the latest committed state at its own start. Opening the transaction before
   validation therefore buys no consistency and costs lock duration across one or more round trips
   plus C# work. The transaction is the persist batch and nothing else; every invariant that must
   hold at write time is re-checked *inside* that batch (concurrency stamps, permissions tag, RLS
   post-check, tree cycles, uniqueness by index).

2. **"Pre-commit non-transactional side effects" (step 10) should not exist.** A step that writes to
   a non-transactional store inside an open database transaction is precisely the class of thing a
   correctness lens deletes: it cannot be rolled back, and it lengthens the transaction by an I/O
   call. With staged blob uploads (upload first through the blob endpoint, save the record with the
   returned token, sweep unconfirmed blobs), blob *writes* precede the pipeline entirely and blob
   *deletes* follow commit. The pipeline then has exactly two side-effect phases: transactional
   participants that append statements to the persist batch, and post-commit participants that run
   after the commit is acknowledged and can never fail the request.

3. **The RLS pre-check is drawn as its own round trip (DB call #2) before validation.** It is a
   read of the rows being updated, filtered by the caller's `Save` grant — which is exactly the
   "before image" load that validation and server-owned-column overwrite need anyway. One statement
   in the validation round serves all three, and a row the filtered load does not return is treated
   as not found (fail closed, indistinguishable from nonexistent).

### 1.2 Cached permissions: the collapse is sound only if the write path locks the tag

The brain dump reads the permissions tag "early on" so that "all subsequent logic relies on a fresh
server cache", then asks whether the connect call can ride the first business round trip
optimistically. It can, but *fresh at read time* is not a guarantee for writes: the tag can change
between the round trip that read it and the transaction that writes. The design below closes that
window for writes at the cost of one `UPDLOCK` on the caller's own stamp row inside the persist
transaction (the row a role save must update to bump the tag, so the two serialize correctly), and
documents the residual intra-batch window for reads as accepted.

### 1.3 The concurrency section under-specifies the dangerous cases

"Not all properties participate in conflict detection" is the observation, but the draft does not
say what happens to a row that was *deleted* by another user (a conflict, not an insert), whether a
client may send an update id that does not exist (never an insert — that would let a client pick
ids or resurrect rows), what happens to children (the parent stamp covers the aggregate), or what
the override flag overrides (only stamp mismatches — never a missing row, never permissions). All
four are specified below.

### 1.4 Child synchronization can be a mass-reparenting hole

"For a child entity, not including it in the payload means delete it" is stated, but the identity
rule is not: children must be keyed by *parent*, and a client-supplied child id that belongs to a
different parent (or to no parent) must be rejected, not silently re-parented — otherwise a user
with `Save` on parent Y can pull child X away from a parent Z they cannot see. Likewise a missing
collection and an empty collection must mean different things on the wire (`null` = untouched,
`[]` = delete all); the draft says so in a note, and the wire shape must make `null` the shape a
client gets when it did not load the collection.

### 1.5 Capability columns need diff-gating, or `Save` bypasses `Activate`

The `IsActive` recipe adds an `Activate` permission action, but `IsActive` is also a column on the
entity that `Save` writes. Without a rule, a user holding `Save` but not `Activate` flips
activation by editing the record. The rule below: a column owned by a capability action is
*diff-gated* — changing it through `Save` requires the capability's grant on that row.

### 1.6 Smaller inconsistencies

- "Save admits a single entity" contradicts the guiding principle that save endpoints accept
  arrays; the service takes an array and the UI sends an array of one.
- "GetByIds: 4 of 5 found — 4 or 404?" has different right answers for a read and for a delete;
  the draft treats them as one question.
- `DeleteByQuery` is described as the only compensation for a bad import and as "dangerous";
  it needs the guardrails of a dangerous operation (non-empty filter, row cap, dry run, no MCP
  exposure), which the draft does not list.
- The `Search` parameter question is asked as a client-vs-server burden question; the security
  question is whether server-side search touches only columns declared searchable and reaches SQL
  only as parameters. It does (Queryex `contains`/`startsWith` emit without pattern
  metacharacters), so keeping it costs nothing in safety.
- Write-once columns: the draft offers "reset silently or validate"; silent reset hides client
  bugs and makes import "succeed" while discarding a value the sheet carried. Validate.
- The "record + blobs" section's option 2 (multipart save) would force the save endpoint, import,
  and MCP to speak multipart; option 1 is what every surveyed SaaS API does.
- The audit vocabulary differs between temporal (`SavedById` + period) and non-temporal
  (`Created*`/`Modified*`) entities. One vocabulary — four audit columns on every top-level entity,
  system-versioning as an additive capability — is needed for the concurrency design below,
  because `ModifiedAt` is the stamp and must exist on every editable top-level table.

---

## 2. Decisions

### D1 — Placement: contracts in `Tellma.Core.Abstractions`, pipeline in `Tellma.Core`

**Decision.** The authoring surface and every contract a module or distribution touches live in
`Tellma.Core.Abstractions` under four namespaces: `Tellma.Core.Abstractions.Services` (the entity
service base, request/response records, operations, capability projection), `Tellma.Core.
Abstractions.Validation` (validators, the context loader, errors), `Tellma.Core.Abstractions.
Errors` (the closed exception set), and `Tellma.Core.Abstractions.Security` (`Securable`,
`SecurableAction`, the permission-evaluator seam). The implementation — `EntityPipeline<TEntity,
TKey>`, the operations, the query planner, the DataLoader scheduler — lives in `Tellma.Core` under
`Tellma.Core.Services`. `Tellma.Core.Abstractions` takes a package reference on
`Tellma.Core.Queryex` so that `FilterTree`, `QuerySpec`, and `QueryexDiagnostic` can appear in
contracts (see Departures).

**Rationale.** `Tellma.Module.<m>` never references `Tellma.Core`, and `CenterService` lives in
`Tellma.Module.Gl`, so the class a module derives from must be in Abstractions and must be
framework-free. Queryex depends on nothing, so the new edge carries no weight.

**Rejected.** A `Tellma.Core.Crud` package (a fourth optional Core-layer package): the CRUD stack is
not something a distribution opts into; it is how every entity works, like Queryex. Mirroring
`FilterTree` in Abstractions to avoid the reference: two types for one concept, drift by
construction.

**Confidence.** High. **Review flag:** the Abstractions → Queryex edge changes the dependency
diagram; T2 must accept it because `QueryexSchema` adapters and the materializer live on the same
seam.

### D2 — Composition: a thin base class as the authoring surface, a sealed pipeline underneath

**Decision.** `EntityService<TEntity, TKey>` is an abstract class in Abstractions that owns the
public operations and a closed set of `protected virtual` hooks; every operation forwards to
`IEntityPipeline<TEntity, TKey>` (registered by `Tellma.Core`), passing the service as the
behavior. Concerns with multiplicity — validators and save effects contributed by a pack *and* a
distribution — are DI-registered components (`IEntityValidator<TEntity>`, `ISaveEffect<TEntity>`)
that the pipeline composes in registration order; the service's own hooks run first. A pack ships
its service generic over the leaf type (`UserService<TUser> where TUser : User`) and a default
closure (`UserService : UserService<User>` is unnecessary — the distribution registers
`UserService<User>` or `UserService<DistroUser>`).

```csharp
public sealed class CenterService<TCenter>(IEntityPipeline<TCenter, int> pipeline)
    : EntityService<TCenter, int>(pipeline)
    where TCenter : Center
{
    protected override ValueTask ValidateAsync(SaveContext<TCenter, int> context) { … }
}
```

**Rationale.** A base class is what a coding agent expects to write against; it keeps the hook set
discoverable and gives one place for the pipeline to call in a fixed order. A sealed pipeline
behind it means a distribution cannot skip a step (no `override SaveAsync` that forgets the
post-check). Versioning is safer than a rich virtual base: the hook signatures take a single
context object, so a platform minor adds members to the context, never parameters to a hook. Pack
code works with the entity *class* (`Center`), never a paired interface — the generic constraint
`where TCenter : Center` gives fork compatibility, per the architecture's capability-interface
rule.

**Rejected.** Pure composition (a sealed `CrudService<TEntity>` plus `IEntityBehavior<TEntity>`
registered separately): equally sound, but the distribution then writes two types per entity and
discovers hooks by reading an interface list. Rich inheritance (every step `virtual`): the fragile
base class; a distribution overriding `SaveAsync` silently loses future platform steps.

**Confidence.** High.

### D3 — The standard operation set and its signatures

**Decision.** Every entity stack exposes the operations below; capabilities add the ones marked so.
Signatures are on `EntityService<TEntity, TKey>` (contracts in §3).

| Operation | Signature (abbreviated) | Exists when | Securable action |
|---|---|---|---|
| Query | `QueryAsync(QueryRequest) → QueryResult` | always | `Read` |
| Details | `GetDetailsAsync(DetailsRequest) → DetailsResult<TEntity>` | always | `Read` |
| GetByIds | `GetByIdsAsync(GetByIdsRequest<TKey>) → QueryResult` | always | `Read` |
| Save | `SaveAsync(IReadOnlyList<TEntity>, SaveOptions) → SaveResult<TEntity>` | stack not read-only | `Save` |
| Delete | `DeleteAsync(DeleteRequest<TKey>) → DeleteResult` | stack not read-only | `Delete` |
| DeleteByQuery | `DeleteByQueryAsync(DeleteByQueryRequest) → DeleteResult` | stack not read-only, web surface only | `Delete` |
| SetActive | `SetActiveAsync(SetActiveRequest<TKey>)` (extension, `TEntity : IActivatable`) | `IActivatable` | `Activate` |
| GetByParentIds | `GetByParentIdsAsync(GetByParentIdsRequest<TKey>) → QueryResult` (extension, `TEntity : ITreeEntity`) | `ITreeEntity` | `Read` |
| DeleteWithDescendants | `DeleteWithDescendantsAsync(DeleteRequest<TKey>) → DeleteResult` (extension) | `ITreeEntity` | `Delete` |

Rules that follow from the lens:

- **GetByIds returns what is visible.** Missing or RLS-hidden ids are simply absent from the result
  (a bulk read has no "partial" failure). `GetDetailsAsync` with one id returns 404 for missing and
  hidden alike.
- **Delete by ids is all-or-nothing.** Every id must be visible under the `Delete` grant and
  present; otherwise the whole call fails with a validation error keyed `Ids[i]` (code
  `Entity.NotFound`) and nothing is deleted. Silent partial deletes hide a concurrent change from
  the user who selected the rows a second ago.
- **Delete by query is guarded.** The filter must be non-empty (an empty filter is "delete all",
  which is never exposed); the matched row count is bounded by `CrudLimits.MaxDeleteByQueryRows`
  (default 10 000; exceeding it fails with `RequestLimitException` and the instruction to run it as
  a background task); `DryRun = true` returns the count without deleting; the operation is
  projected to the web surface only, never to MCP or the public API.
- **`Take` is capped** by `CrudLimits.MaxPageSize` (default 500); a larger value is a
  `RequestLimitException`, never silently clamped (silent clamping makes paging arithmetic wrong).
- **Count is capped**: `IncludeCount` returns `TotalCount` up to `CrudLimits.CountCap` (default
  10 000) and `IsCountCapped = true` beyond it.

**Confidence.** High. **Review flag:** strict delete-by-ids versus best-effort with a per-id
outcome list; the strict form is chosen because the UI always deletes from a list it just showed.

### D4 — The read path: one round trip, connect riding the batch, stale-tag re-run

**Decision.** A read (`Query`, `Details`, `GetByIds`, `GetByParentIds`) executes one round trip in
the common case. The batch is, in order:

1. the **connect statement** (T4 owns its text): resolve `sub` → `UserId`, `IsActive`, stamp
   `LastActiveAt` on the user's stamp row, and return the tenant-level and user-level tags
   (`SettingsTag`, `PermissionsTag`, `UserSettingsTag`);
2. the business statements compiled with the **cached** permission set for the *cached* tag: the
   page query (RLS conjoined), the capped count, the ancestors query when requested;
3. nothing else.

The executor reads result set 1 first. If `IsActive = 0` or no row: the remaining result sets are
drained and discarded and `ForbiddenException` (user inactive) is thrown — the SPA's session is
then ended by the host (T1). If `PermissionsTag` differs from the tag the cached set was built for:
results are discarded, the permission set is rebuilt (T4's evaluator, one round trip of its own or
folded into the re-run), and the business statements are re-executed once; a second mismatch is a
`ConcurrencyConflictException` with code `Permissions.Churning` (a role editor hammering saves).
If `SettingsTag` differs: the schema and settings caches are refreshed and the compile is redone
(the SQL text may differ — `Name2` gating), same single re-run budget.

**Accepted window.** The tag is read at the start of the batch and the query runs microseconds
later; a revocation committing in between is served with the old grant once. This is the same
window every non-locking read system has, and a read outside a transaction cannot hold a lock
past its own statement. Writes do not get this allowance (D6).

**Rationale.** One round trip for the dominant operation, with every failure mode enumerated:
deactivated user (discarded, 403), stale permissions (discarded, re-run), stale settings
(discarded, re-run), RLS evaluated only with permissions whose tag was verified in the same batch.

**Confidence.** High.

### D5 — The save pipeline, step by step, with its round-trip count

**Decision.** `SaveAsync` runs the steps below. "RT" marks a database round trip. Common case: two
round trips (RT1 validation, RT2 persist); a validator that needs a dependent load adds one round
trip per extra round, bounded by `CrudLimits.MaxValidationRounds` (default 3).

| # | Step | Where | Notes |
|---|---|---|---|
| 1 | Shape checks | pipeline | `entities` non-empty and ≤ `MaxSaveEntities`; every top-level `Id` is `0` (insert) or `> 0` (update), no duplicates; child ids `0` or `> 0`, no duplicates within the batch; `RequestLimitException`/`ValidationException`. |
| 2 | Coarse permission | pipeline | The caller holds *some* `Save` grant on the resource (cached set for the cached tag); else `ForbiddenException`. |
| 3 | Preprocess | pipeline, then service hook | Trim strings and collapse empty to `null` (`[PreserveWhitespace]` opts out); overwrite server-owned properties from metadata (D9); then `PreprocessAsync` hook (distro logic: defaults, derived fields). |
| 4 | Id assignment | pipeline | New rows get ids from the allocator (T2). Reservation for a cold buffer rides RT1. Children reference parent ids by then. Ids are never un-consumed. |
| 5 | Validation round(s) — **RT1…RTn** | pipeline + validators | The pipeline's own loads (before images with the `Save` grant conjoined — this *is* the RLS pre-check; child before-images by parent id; staged blob tokens) and every validator's declared loads execute as one batch together with the connect statement and tag reads (D4 rules apply: inactive → 403, stale tag → refresh and re-run RT1 once). Attribute validation, write-once checks, diff-gating (D9), uniqueness against loaded context and within the batch, tree cycle check over the loaded ancestor chain, then validator rules. Any error → `ValidationException` (422), nothing written. |
| 6 | Persist — **RT2** | pipeline + emitter + effects | One T-SQL transaction: guard prologue (D6), concurrency check under `UPDLOCK` (D7), emitter statements (T2: update changed rows, insert new, synchronize children), tree recompute (D17), tag bumps (derived from the write set), transactional effects (`ISaveEffect.ContributeAsync`: notification inserts, task rows, blob confirmations), RLS post-check, read-back, `COMMIT`. Any `THROW` or engine error rolls everything back. |
| 7 | Post-commit effects | pipeline + effects | `ISaveEffect.AfterCommitAsync` (blob deletes, nudges to the task runner, external calls the distro accepts as best-effort). Exceptions are logged with the save's trace id and metered; they never change the response. |
| 8 | Response | pipeline | `SaveResult<TEntity>`: the read-back entities in details shape when `SaveOptions.ReturnEntities` (default true; import passes false), the related-entity dictionary, the row echo. |

**Rationale.** Every check that protects data runs where it is sound: existence and visibility in
RT1 (a filtered read), and everything that must hold at write time inside the transaction in RT2.
The two-round-trip budget is a metric (D24), not a promise.

**Confidence.** High.

### D6 — The transaction is the persist batch text; guards are T-SQL `THROW`s; retry is the executor's

**Decision.** The persist round trip is one command whose text begins `SET XACT_ABORT ON; SET
NOCOUNT ON; BEGIN TRAN;` and ends `COMMIT;` followed by nothing that writes. No client-side
`SqlTransaction`, no `TransactionScope`. The **guard prologue** runs before any write:

```sql
DECLARE @tag uniqueidentifier;
SELECT @tag = PermissionsTag
FROM [core].[UserStamps] WITH (UPDLOCK, ROWLOCK)
WHERE UserId = @userId;                                       -- U lock held to COMMIT
IF @tag IS NULL OR @tag <> @expectedPermissionsTag THROW 50409, N'permissions_changed', 1;
IF NOT EXISTS (SELECT 1 FROM [core].[Users] WHERE Id = @userId AND IsActive = 1)
    THROW 50401, N'user_inactive', 1;
```

The `UPDLOCK` on the caller's own stamp row is what makes the collapsed connect sound for writes:
every statement that bumps a user's `PermissionsTag` (role save, membership save, deactivation —
T4) updates that row and therefore blocks until this transaction commits, so the permissions the
pre-check used in RT1 are exactly the permissions in force at commit. A role save that waits a
few milliseconds behind an in-flight save by one of its members is the intended serialization.

Reserved error numbers, mapped by the executor to typed exceptions:

| Number | Meaning | Exception |
|---|---|---|
| 50401 | caller inactive at write time | `ForbiddenException` (`User.Inactive`) |
| 50403 | RLS post-check failed | `ForbiddenException` (`Rls.PostCheck`) |
| 50409 | permissions tag changed since RT1 | internal: refresh and re-run from RT1 once, then `ConcurrencyConflictException` (`Permissions.Churning`) |
| 50412 | concurrency stamp mismatch or row missing | `ConcurrencyConflictException` with the conflict rows (D7) |
| 50422 | invariant failed in SQL (tree cycle, capacity) | `ValidationException` with the code the message carries |
| 50500–50599 | reserved for distribution guards contributed through `ISaveEffect` | `ValidationException` (`Distro.<code>`) |

Result sets that precede a `THROW` are readable; the executor reads them before surfacing the
exception, which is how the conflict list (D7) reaches the caller.

**Retry.** The executor owns retry (driver retry is inert under any transaction and `SqlBatch` has
none). A read-only round trip is retried on the baseline transient list (SqlClient's twenty
numbers; taken from `SqlConfigurableRetryFactory.BaselineTransientErrors` on 7.0 and copied with a
pinning test until then). The persist round trip is retried on the same list when the failure
occurred **before** commit was sent (deadlock 1205, lock wait 1222, throttling) — nothing was
applied. A connection loss **during or after** `COMMIT` runs the **commit probe** on a fresh
connection before deciding:

```sql
SELECT COUNT(*) FROM [gl].[Centers] t JOIN @newIds n ON n.Id = t.Id;                       -- inserts landed?
SELECT COUNT(*) FROM [gl].[Centers] t JOIN @updIds u ON u.Id = t.Id WHERE t.ModifiedAt = @stamp; -- updates landed?
```

Any insert present or any update carrying this batch's stamp proves the commit (ids are
app-assigned and the stamp is unique to the batch on that row); the pipeline then re-issues the
read-back as a plain read and proceeds to post-commit effects. Neither present means the commit
never happened and the whole persist is re-run. Post-commit effects are never retried by the
executor. Three attempts total; then the error surfaces.

**Rationale.** `TransactionScope` defaults (Serializable, one minute, async flow suppressed) are
all wrong here and a stray second connection escalates to a distributed transaction that throws on
Linux. `THROW` honors `XACT_ABORT`, always severity 16, and needs no `sys.messages` entry. The
probe resolves the one ambiguous window without a transaction-tracking table.

**Confidence.** High for the shape; medium for the probe's rare edge (an update-only batch whose
rows were all re-modified by others between our commit and the probe reads as "not committed",
is re-run, and then fails the stamp check — surfaced to the user as a conflict, never a silent
overwrite).

### D7 — Optimistic concurrency: `ModifiedAt` is the stamp, checked under `UPDLOCK`, override is explicit

**Decision.** No `rowversion` column. Every editable top-level entity carries the four audit
columns (seam 17), and `ModifiedAt datetime2(7)` is the concurrency stamp:

- **Generation.** One stamp per persist batch, generated in the app from `TimeProvider`
  (`UtcNow.UtcDateTime`, tick precision equals `datetime2(7)` exactly). If it is not strictly
  greater than the largest `ModifiedAt` among the before images (clock skew across instances, a
  clock step), the stamp is `max + 1 tick`. So the stamp always differs from every stamp it
  replaces and is unique to the batch on each row it writes, which the commit probe relies on.
- **What bumps it.** Every write of the row image through the emitter — `Save`, `SetActive`, every
  capability action, background jobs that change business columns. There is no emitter path that
  writes a top-level table without stamping `ModifiedAt`/`ModifiedById`. Bookkeeping that must not
  disturb the stamp (`LastActiveAt`, tags, inbox counters, lease columns) lives in sibling
  non-temporal tables that carry no stamp.
- **Children.** Child rows carry no stamp. Any change to a child (insert, update, delete) bumps
  the parent's `ModifiedAt` even when no parent column changed — the stamp guards the aggregate.
- **The check.** Inside the persist transaction, before any write:

```sql
DECLARE @conflicts TABLE (Id int NOT NULL, ModifiedAt datetime2(7) NULL, ModifiedById int NULL, IsMissing bit NOT NULL);
INSERT @conflicts (Id, ModifiedAt, ModifiedById, IsMissing)
SELECT u.Id, t.ModifiedAt, t.ModifiedById, CASE WHEN t.Id IS NULL THEN 1 ELSE 0 END
FROM @updates u
LEFT JOIN [gl].[Centers] t WITH (UPDLOCK, ROWLOCK) ON t.Id = u.Id     -- U locks on every target row, held to COMMIT
WHERE t.Id IS NULL
   OR (@override = 0 AND u.ExpectedStamp IS NOT NULL AND t.ModifiedAt <> u.ExpectedStamp);
IF EXISTS (SELECT 1 FROM @conflicts)
BEGIN
    SELECT Id, ModifiedAt, ModifiedById, IsMissing FROM @conflicts;   -- readable before the error
    THROW 50412, N'concurrency', 1;
END;
```

  The `UPDLOCK` on target rows is what makes check-then-write race-free under RCSI and
  lock-after-qualification: two savers of the same row serialize on the U lock, and the second
  sees the first's committed stamp. A row that vanished is always a conflict (`IsMissing`), never
  an insert, whatever the override flag says.
- **Expected stamps on the wire.** The client sends back the `modifiedAt` string it received,
  verbatim; the pipeline parses it to `DateTime` (round-trip format, seven fractional digits). A
  client that parses it into a millisecond `Date` and re-serializes it has corrupted the stamp; the
  SPA keeps the raw string for the save and parses a copy for display (T6 records this rule).
  `ExpectedStamp = null` on an update means "no check"; the web surface's details-page save
  requires stamps for updates (a `SaveOptions.RequireStamps` flag T6 sets), import and MCP may
  omit them.
- **Override.** `SaveOptions.OverrideConcurrency = true` skips the stamp comparison only. Missing
  rows still conflict, permissions still apply, the U locks are still taken. Overrides are metered
  (`tellma.crud.concurrency.overrides`).
- **`SetActive` and other actions** do not check stamps (a state toggle is idempotent) but do bump
  the stamp, so a concurrent editor's later save conflicts, as it should.

**Evaluation against `rowversion`.** `rowversion` cannot be excluded from bookkeeping updates on
the same row, cannot be set by the app (so a restore, a cross-tenant import, or the commit probe
cannot reason about it), is opaque in the "user Y modified this at T" prompt, and adds a column
where `ModifiedAt` already exists. Its one advantage — the engine guarantees monotonicity — is
replaced by the `max + 1 tick` rule. The temporal period column `ValidFrom` was also rejected as
the stamp: it is the transaction begin time, shared by every row the transaction touched, and it
moves on any update of the row including ones the emitter does not control.

**Confidence.** High. **Review flag:** `ExpectedStamp = null` meaning "no check" is a deliberate
convenience for import and agents; the strict alternative (stamps mandatory on every update
everywhere) costs import a read round trip per sheet.

### D8 — Validation: a DataLoader over the batch, bounded rounds, codes not prose, indexes as the guarantee

**Decision.**

- **Validators are plain async methods** on the service (`ValidateAsync`) and on registered
  `IEntityValidator<TEntity>` components; the pipeline runs all of them concurrently as tasks per
  round. A validator asks for context through `context.Loader` and `await`s a `ContextPromise<T>`.
  Awaiting an unloaded promise *parks* the validator; when every validator is parked or complete,
  the pipeline dispatches one batch containing every pending request (plus its own loads and the
  connect statement in round 1), resolves the promises, and resumes them. Each dispatch is one
  round trip and one validation round; `CrudLimits.MaxValidationRounds` (3) is enforced by throwing
  `InvalidOperationException` (a developer error, never a user error) and metered.
- **Requests are deduplicated by structural key**: the root entity, the filter tree rendered
  ordinally, the parameter values, and the statement text for raw SQL. Two requests that differ
  only in `Select` are merged (union of selects) rather than deduplicated apart. A key that
  answers "not found" is cached for the request like any value.
- **Context loads are not RLS-filtered.** A uniqueness rule is global by definition and the unique
  index would reveal the same fact; a validator that must respect visibility loads through
  `context.Loader.Visible<T>(...)`, which conjoins the caller's `Read` grant.
- **Attribute validation is the pipeline's, not the framework's.** The pipeline evaluates BCL
  `ValidationAttribute`s per property from cached metadata for every entity and child, reporting
  every failure (not first-level-only), and never invokes `IValidatableObject` (the BCL walker
  calls it only when no other error exists, which hides errors in bulk). The minimal-API
  endpoint validation filter is disabled on save endpoints (`DisableValidation()`, T6) so
  validation runs once, in bulk, with context.
- **Errors are `(Path, Code, Arguments)`.** Paths use the ASP.NET grammar with the top-level index
  first: `[3].Lines[1].Quantity`, `[0].Name2`, `Ids[2]`. Codes are stable strings the platform
  and packs publish (`Required`, `MaxLength`, `Unique`, `Entity.NotFound`, `WriteOnce`,
  `Tree.Cycle`, `Fk.InUse`, …); arguments are named string pairs. The web layer localizes to the
  request culture and returns both message and code (T6).
- **Uniqueness is guaranteed by the index, explained by the validator.** A `[Unique]`
  declaration on an entity (T2's natural-key/uniqueness metadata) yields three things from one
  declaration: the unique index (named `UX_<Table>_<Col>[_<Col>]`, filtered `WHERE <col> IS NOT
  NULL` for nullable columns), a built-in validator that checks the batch against itself and
  against loaded rows (the friendly error), and the persist-error map that turns 2601/2627 on
  that index into the same `Unique` error at the property path (the race case). A C#-only
  uniqueness check is write-skew-prone under RCSI and SNAPSHOT alike and is therefore not
  permitted without the index; a test enumerates `[Unique]` declarations against the model's
  indexes.
- **Foreign keys** are validated by the database: error 547 on `FK_<Table>_<Column>` maps to
  `Fk.NotFound` at the property path on save and to `Fk.InUse` keyed `Ids[i]` on delete.

**Rationale.** Neither the .NET 10 validator (per-parameter endpoint filter, early returns,
synchronous, PascalCase keys) nor FluentValidation (per-instance, no batching) can validate N
entities after one batched load; both agree on the path grammar, which is kept. The parking
scheduler is Facebook's DataLoader contract with an explicit dispatch point, layered on the T2
batch so that many loaders share one round trip.

**Rejected.** Two-phase `Declare`/`Validate` methods with explicit rounds: more mechanical but
every validator becomes a small state machine; the parking form is the same contract with the
compiler writing the state machine. FluentValidation as a dependency: a second rule language for
distributions to learn with no batching.

**Confidence.** High on the model; medium on the parking ergonomics (a validator that awaits
something other than a promise is simply "active", so dispatch waits for it — correct, and a
metric shows when it is slow).

### D9 — Editable, server-owned, write-once, and diff-gated properties

**Decision.** Which properties a client may set is derived from metadata, never from what the
payload happened to contain:

- **Server-owned** (overwritten on every save, regardless of the client's value): the audit
  columns; tree-maintained columns (`Node`, `SubtreeCount`, `ActiveSubtreeCount`, `Level`);
  computed columns; any property marked `[ServerOwned]`. On update the values come from the
  before image; on insert from the pipeline's defaults. A client-sent value is ignored (it is
  not an error — old clients during a swap may send fields a new server owns).
- **Write-once** (`[WriteOnce]`, e.g. `User.Subject`, `User.Email`): settable on insert; on update
  a client value that *differs* from the before image is `ValidationException` code `WriteOnce` at
  the property path. Equal or `null` (the client did not send it) passes. No silent reset.
- **Diff-gated** (`[GatedBy("Activate")]` on `IsActive`, applied by the `IActivatable` capability
  automatically): a client may change the column through `Save` only if the caller holds the
  named action's grant on that row; otherwise `ForbiddenException` naming the action. The
  capability's own operation (`SetActiveAsync`) is the ordinary path; the gate keeps import and
  agents honest without a second endpoint.
- **Children:** the same rules apply per child type.

**Rationale.** A parallel DTO hierarchy would make the editable split explicit in the type system
but doubles every entity; metadata on the entity class keeps one class as the source of truth and
puts the rule in the pipeline where an old client, an agent, and an import sheet all meet it.

**Confidence.** High. **Review flag:** write-once as validation versus silent reset; the
simplicity lens may prefer reset, but a sheet that carries a changed `Email` and "succeeds" has
lost data the user typed.

### D10 — Child synchronization keyed by parent; foreign ids rejected; no resurrection

**Decision.** For each child collection declared on the entity (`[NotMapped]` list, T2's wire
shape) the semantics are:

- `null` collection → untouched (no statements emitted for that collection);
- `[]` → every existing child of that parent is deleted;
- otherwise → children with `Id = 0` are inserted, children with `Id > 0` are updated, and
  existing children of the parent absent from the list are deleted.

Identity rules, enforced in RT1 against the before image loaded **by parent id**:

- a child `Id > 0` that is not among the parent's existing children (belongs to another parent,
  to a parent the caller cannot see, or to nothing) → `ValidationException` code
  `Entity.NotFound` at `[i].Lines[j].Id`; it is never re-parented and never inserted;
- a child `Id > 0` whose `ParentId` in the payload differs from the parent it is nested under →
  the nesting wins and the FK is overwritten (the FK is server-owned for nested children);
- a top-level `Id > 0` not returned by the RLS-filtered before-image load → `Entity.NotFound` at
  `[i].Id` (fail closed: nonexistent and invisible are the same answer).

The emitter's delete statement is bounded to the parents in the batch:
`DELETE c FROM [core].[RoleMemberships] c WHERE c.UserId IN (SELECT Id FROM @parents) AND NOT
EXISTS (SELECT 1 FROM @children x WHERE x.Id = c.Id)`.

**Confidence.** High.

### D11 — Ids: assigned after preprocessing, before validation; never un-consumed

**Decision.** The pipeline assigns ids (T2's allocator) at step 4 so that validators reason about
final identities (a tree validator needs the new parent's id; a uniqueness validator needs to
distinguish "myself" from "another row"). Validation failure does not return ids to the buffer:
un-consuming reintroduces the possibility that an id observed in a log or a diagnostic is later
attached to a different row, and gaps cost nothing. The cold-start reservation rides RT1 as one
`sp_sequence_get_range` call per entity type that needs it; a warm buffer costs nothing.

**Confidence.** High.

### D12 — RLS: pre-check as a filtered read, post-check as a `THROW` inside the transaction

**Decision.** For updates, the before image is loaded in RT1 with the caller's `Save` filter
conjoined (`FilterTree.And([byIds, saveGrant])`); a requested id not returned is `Entity.NotFound`
(D10). For inserts there is nothing to pre-check. After the writes, still inside the transaction:

```sql
IF EXISTS (
    SELECT 1 FROM @touched s
    WHERE NOT EXISTS (SELECT 1 FROM [gl].[Centers] t WHERE t.Id = s.Id AND (<save grant filter, compiled by Queryex>)))
    THROW 50403, N'rls_post', 1;
```

A grant with no filter compiles to no post-check. The grant filter is compiled by
`QueryexEngine.CompileQuery` with `Select = "Id"` and `Filter = And([Leaf("Id in @touchedIds")…])`
— the restriction source is the batch's `@touched` table variable (T2's list restriction), so the
post-check is a compiled statement, never concatenated text.

**Rationale.** The post-check is what stops a user from moving a row out of their own grant (an
`Update` that changes the cost center to one they cannot see) or inserting one they could not read
back. It runs under the U locks taken in D7, so no concurrent change can interleave.

**Confidence.** High.

### D13 — Side effects: transactional participants append statements; post-commit is best-effort

**Decision.** `ISaveEffect<TEntity>` has two members. `ContributeAsync(PersistContext<TEntity>)`
runs before the persist batch is sent and may only append statements (and register result readers)
to it — notifications (T10), task rows (T10), blob confirmations (T7), audit trail rows, the
distribution's own tables. It cannot perform I/O of its own (the context exposes no connection).
`AfterCommitAsync(SaveOutcome<TEntity>)` runs after the commit is acknowledged; the pipeline
awaits all effects, logs and meters failures (`tellma.crud.effects.failures` with `effect` tag as
the closed set of registered effect names), and never fails the response. There is no third
phase.

Durable "must happen" work (an email, an e-invoice filing) is never an `AfterCommitAsync` body; it
is a task row appended in `ContributeAsync` and executed by the background machinery (T10). The
post-commit phase exists for things whose failure is harmless (deleting a replaced blob the sweep
will catch anyway, nudging a runner that polls anyway).

**Confidence.** High.

### D14 — Delete semantics

Stated in D3 (strict by ids, guarded by query). Additionally: `DeleteRequest.ExpectedStamps` is
optional per id; when present, a mismatch is a `ConcurrencyConflictException` (deleting a row
someone just changed is the classic lost-intent case). `DeleteWithDescendants` computes the closure
in SQL (`descendantOf(Id, @ids)`), requires the `Delete` grant to hold for every row in the closure
(a `THROW 50403` if any fails — a partially deleted subtree is never left behind), deletes leaves
first by `Level DESC` to satisfy the self-FK, then recomputes the remaining subtree counts. Error
547 on any delete maps to `Fk.InUse` at `Ids[i]`, listing the referencing table's canonical name in
the arguments.

**Confidence.** High.

### D15 — Details: one round trip, expansion through navigation selects, no partially visible documents

**Decision.** `GetDetailsAsync` runs one round trip: connect; the main row by id with the `Read`
grant conjoined (`Select` = the entity's editable properties plus the declared expansion paths,
e.g. `Parent.Id, Parent.Name, Parent.Name2, CreatedBy.Id, CreatedBy.Name`); each child collection
by parent id (list restriction, T2) with its own expansion paths; the service's declared extras
(`ContributeDetails` hook: raw statements or compiled queries added to the same batch); the row
echo (the entity compiled with the request's `Select`, the same shape `Query` returns). The
materializer (T2) turns path-carrying columns into the **related-entity dictionary**
(`Related[entityName][id]`), so 10 000 lines pointing at one center carry one center.

If the main statement returns no row, the response is 404 and every other result set is drained
and discarded — child rows may have been read by the server, never by the client. Related entities
reached through navigations are **not** RLS-filtered (the architecture's "no partially visible
document" rule): a user who may read a document may read what it points at. They are reachable
only through a visible root; there is no endpoint that returns them by arbitrary id outside their
own stack's `Read` grant.

**Confidence.** High. **Review flag:** unfiltered related entities is a deliberate leak of
*names* of referenced rows to users who could not list them; the alternative (filter them and
show blanks) breaks the details page for ordinary users.

### D16 — Capabilities: one interface on the entity, everything else projected

**Decision.** A capability is an interface the entity class implements. Implementing it is the
single declaration; the stack feature (T1's composition) reflects the entity's interfaces once at
composition and projects everything else:

| Capability | Entity interface | Columns (T2) | Service operations | Securable actions | Server-owned / gated | Default query behavior | Endpoints (T6) |
|---|---|---|---|---|---|---|---|
| Keyed, audited (every stack entity) | `IEntity<TKey>`, `IAudited` | `Id`, `CreatedAt/ById`, `ModifiedAt/ById` | D3 set | `Read`, `Save`, `Delete` | audit columns server-owned | — | projected |
| Activatable | `IActivatable` | `IsActive bit NOT NULL DEFAULT 1` | `SetActiveAsync` | `Activate` (filterable) | `IsActive` gated by `Activate` | `IncludeInactive = false` conjoins `IsActive = true` | `…/set-active` |
| Tree | `ITreeEntity<TKey>` | `ParentId`, `Node` (server-owned, not in UDTT, not on the wire), `SubtreeCount`, `ActiveSubtreeCount` (only with `IActivatable`), `Level` (computed) | `GetByParentIdsAsync`, `DeleteWithDescendantsAsync`; `IncludeAncestors` accepted | none new | tree columns server-owned; cycle validation; recompute statement | — | `…/by-parent-ids`, `…/delete-with-descendants` |
| Temporal | `ISystemVersioned` (marker) | period columns, history table (T2) | none | none | — | — | — |
| Record with blobs | `IHasBlobs` + `[BlobReference]` properties | blob-id columns | none (upload/retrieve endpoints are T7's) | none | staged tokens validated in RT1, confirmed in RT2, replaced blobs deleted after commit | — | T7's |
| Multilingual | `IMultilingual` | `Name`, `Name2`, `Name3` (T2/T3) | none | none | `Name2`/`Name3` absent from the schema when the tenant has no such language; a client value for an absent column is ignored, not an error | `Search` covers the configured name columns | — |
| Searchable columns | `[Searchable(SearchKind.Contains|Prefix)]` on properties | — | `Search` honored | none | — | `FilterTree.Or` of `contains(Col, @search)`/`startsWith(Col, @search)` over declared columns present in the schema | — |

One securable action, `Activate`, covers both directions; "may deactivate but not reactivate" has
no precedent worth a second action and doubles every role's configuration.

Read-only stacks are a feature option (`EntityStack<TEntity>(readOnly: true)`), not a type: the
`Save`/`Delete` operations and securables are not registered and T6 projects no write routes.

**Rationale.** Everything a capability implies is derivable from the interface at composition
time, so there is exactly one thing to forget (the interface) and forgetting it removes the
feature wholesale rather than half of it. The diff-gate closes the `Save`-bypasses-`Activate`
hole.

**Confidence.** High. **Review flag:** one `Activate` action versus two.

### D17 — Trees: SQL recompute in the persist batch is the guarantee; C# validation is the message

**Decision.** Cycle validation runs twice by design. In RT1 the pipeline loads the ancestor chains
of every row whose `ParentId` changed (`ancestorOf` over the current tree) and the C# check
reports `Tree.Cycle` at `[i].ParentId` with a readable message. In RT2 the recompute statement
(emitted by T2, appended by the pipeline for every `ITreeEntity` save) rebuilds `Node`,
`SubtreeCount`, and `ActiveSubtreeCount` for the affected subtrees with a recursive CTE from the
affected roots, and a row in the affected set that the recursion never reaches is a cycle that
formed between RT1 and RT2 (two users each making the other's node their parent): the statement
`THROW 50422, N'Tree.Cycle', 1`. The affected roots are locked with `UPDLOCK` by the D7 statement
(the parents being saved) plus the rows whose subtree counts change, so two concurrent tree edits
serialize.

`IsLeaf` is not a column (`SubtreeCount = 1`); `Level` is a computed column over `Node`
(`GetLevel()`), persisted so the breadth-first index `(Level, Node)` exists. `Node` is excluded
from the UDTT (`hierarchyid` cannot ride a TVP without `Microsoft.SqlServer.Types`) and from the
wire; it exists in the Queryex schema as the entity's `TreeNode`.

**Confidence.** High on the double check; medium on locking the counts' ancestors (T2 decides the
recompute statement's exact lock footprint).

### D18 — `Search` stays, server-side, over declared columns

**Decision.** `QueryRequest.Search` (≤ `CrudLimits.MaxSearchLength`, default 200 characters) is
lowered by the pipeline to `FilterTree.Or` over the entity's `[Searchable]` properties that exist
in the tenant's schema, each leaf `contains(Col, @search)` or `startsWith(Col, @search)` per the
declaration, with `@search` a declared Queryex parameter. It is conjoined with the user's `Filter`
and the RLS filter. No picker-versus-page hint: the select list already tells the client what it
needs, and the server has no reason to interpret one word two ways.

**Rationale.** Queryex emits these predicates without pattern metacharacters, so the value can
only ever be data; keeping the lowering server-side means the searchable set is declared once
next to the columns and cannot drift between the picker and the page.

**Confidence.** High.

### D19 — The closed exception set (seam 10)

**Decision.** Six sealed exceptions derive from `TellmaException` (code + named arguments) in
`Tellma.Core.Abstractions.Errors`; T6 maps them to statuses:

| Exception | Carries | Status (T6) |
|---|---|---|
| `ValidationException` | `IReadOnlyList<ValidationError>` | 422 |
| `NotFoundException` | resource, id | 404 |
| `ForbiddenException` | resource, action, code (`User.Inactive`, `Rls.PostCheck`, `Permission.Missing`, `Gate.<Action>`) | 403 |
| `ConcurrencyConflictException` | `IReadOnlyList<ConcurrencyConflict>` or code `Permissions.Churning` | 409 |
| `RequestLimitException` | limit name, actual, maximum | 413 |
| `InvalidQueryException` | `IReadOnlyList<QueryexDiagnostic>` | 400 |

Every other exception is a 500 and is never mapped by type name. Codes are stable strings; a
platform minor may add codes, never rename one.

**Confidence.** High.

### D20 — Securables come from operation descriptors; resource names are table identities

**Decision.** At composition, the stack feature emits an `EntityStackDescriptor` whose
`Operations` each carry a `Securable(Resource, Action, SupportsFilter, FilterRoot)`. T4's registry
is populated from these descriptors (plus the distribution's custom operations, which must declare
a securable to be projected at all — T6 refuses to map an undeclared one). `Resource` is the
entity's canonical schema-qualified table identity in singular form (`core.User`, `gl.Center`),
never the CLR type name: a distribution leaf `sealed class User : Core.User` keeps the pack's
resource, so stored permissions survive forks, renames, and promotion of a feature into a pack.
The pipeline's coarse check (step 2) and the endpoint metadata (T6) consult the same descriptor,
and a startup audit over the endpoint table fails the host for any tenant-API endpoint without a
securable or an explicit public marker.

**Confidence.** High. **Review flag:** singular resource names (`core.User`) versus the plural
table names ARCHITECTURE.md uses (`gl.Invoices`); seam 17 decides the table names and the resource
name follows whichever wins, with one rule — resource equals table identity.

### D21 — Distribution extension of pack entities and services

**Decision.** A pack ships `Entity` (non-sealed default leaf) and `EntityService<TEntity>` generic
over it. A distribution that adds columns writes `public sealed class User : Tellma.Core.User { …
}` and registers the stack with its leaf: `t.AddEntityStack<User, UserService<User>>()`. A
distribution that adds logic derives the service: `public sealed class UserService :
Tellma.Core.UserService<User> { override hooks }`. Pack validators registered against
`IEntityValidator<Core.User>` do not apply to `DistroUser` automatically; the pipeline resolves
validators for `TEntity` and every base type up to the pack's abstract base (contravariant
resolution by walking the type chain), so pack rules keep running on extended leaves. The same
walk applies to `ISaveEffect<>`.

**Rationale.** Generic services over leaf types with class constraints reconcile "code against
interfaces so distros can replace entities" with "no paired interface per entity". Walking the
type chain for components is what keeps a pack's invariants enforced after a distribution extends
the entity — the failure mode this lens most wants to prevent.

**Confidence.** High.

### D22 — Import reuses `SaveAsync` unchanged

**Decision.** `SaveAsync(entities, new SaveOptions { ReturnEntities = false, RequireStamps =
false, Source = SaveSource.Import })` is the import path. Natural-to-surrogate translation and
partial-sheet hydration are T9's, performed before the call; hydration uses
`GetForEditAsync(ids)` — the details loader without extras — exposed on the service for that
purpose. `SaveSource` is a closed enum (`Ui`, `Import`, `Agent`, `System`) recorded in the trace and
available to hooks; it never changes validation rules (a rule that differs by source is a rule
that will be bypassed).

**Confidence.** High.

### D23 — Every write declares its write set; tag bumps derive from it; bypasses are mechanically caught

**Decision.** Every statement added to a batch carries a `WriteSet` (the tables it may modify):
emitter statements derive it from the model; `SqlBuilder<T>` statements derive it from the
builder; raw SQL statements must declare it (`Writes: [...]`) and a Roslyn analyzer shipped with
the ordinal-binding analyzer (T2) parses literal raw SQL with `Microsoft.SqlServer.TransactSql.
ScriptDom` at build time and reports any DML target not in the declared set. The batch executor
appends, inside the same transaction, one bump per tag that the union of write sets touches
(cacheable entity types, settings, securables — T3's table). There is no API to execute SQL against
a tenant database outside a batch: `DbContext.SaveChanges*` is forbidden by an analyzer in
distribution and pack code (the context exists for the model and migrations), and the executor
is the only holder of a connection. Out-of-band SQL (a DBA) is outside the guarantee; the
tenant-level "refresh all tags" administrative operation (T3) is the documented remedy.

**Confidence.** High on the mechanism; medium on the analyzer's coverage of dynamically composed
raw SQL, which is why raw SQL in distributions is discouraged in favor of `SqlBuilder<T>`.

### D24 — Observability: the round-trip budget is an instrument

**Decision.** Meter `Tellma.Core` (`IMeterFactory`), names as `const`s in
`Tellma.Core.Abstractions.Services.CrudTelemetryNames`:

| Instrument | Type | Unit | Tags (closed sets) |
|---|---|---|---|
| `tellma.crud.operation.duration` | Histogram | `s` | `crud.operation` (Query, Details, GetByIds, Save, Delete, DeleteByQuery, SetActive, GetByParentIds, DeleteWithDescendants), `crud.outcome` (ok, validation, forbidden, not_found, conflict, limit, error), `crud.source` (ui, import, agent, system) |
| `tellma.crud.roundtrips` | Histogram | `{roundtrip}` | `crud.operation`, `crud.retried` (true/false) |
| `tellma.crud.validation.rounds` | Histogram | `{round}` | `crud.operation` |
| `tellma.crud.entities` | Histogram | `{entity}` | `crud.operation` |
| `tellma.crud.concurrency.conflicts` | Counter | `{conflict}` | `crud.kind` (stamp, missing, permissions) |
| `tellma.crud.concurrency.overrides` | Counter | `{save}` | — |
| `tellma.crud.effects.failures` | Counter | `{failure}` | `crud.effect` (the registered effect's name, a closed set at composition) |
| `tellma.crud.stale_tag.reruns` | Counter | `{rerun}` | `crud.tag` (permissions, settings) |

Per-request: the request `Activity` gets `tellma.db.roundtrips` and `tellma.crud.operation` tags.
No entity-type tag on instruments (it multiplies every dimension and can be large); the entity is
a structured-log field. Tenant is never a tag. The per-round-trip counter is incremented by the
batch executor (T2's `IDbCallBudget`), which is the only thing that sends commands; tests assert
budgets with `SqlConnection.RetrieveStatistics()["ServerRoundtrips"]` independently.

**Confidence.** High.

### D25 — Testing

**Decision.** `test/core/Tellma.Core.Tests` (pure: the pipeline over a scripted batch double,
attribute walker, path grammar, the parking scheduler, exception mapping, request limits) and
`test/core/Tellma.Core.IntegrationTests` (`Category=Integration`, LocalDB or Testcontainers, the
fixture entities shared with T2 under `test/shared/Tellma.Testing.Entities`: `Widget` (audited,
activatable, `[Unique] Code`, `[WriteOnce] Serial`, `[Searchable] Name`), `WidgetLine` (child),
`Bucket` (tree, activatable)). Pinned behaviors, each a named test:

- round-trip budget per operation (one for reads, two for a save, +1 per validation round) via
  `ServerRoundtrips`;
- concurrent saves of one row from two connections: exactly one succeeds, the other reports the
  winner's stamp and `ModifiedById`;
- stamp bump on child-only change; override skips stamps but not missing rows;
- a permissions tag bumped between RT1 and RT2 is refused by the prologue and the pipeline
  re-runs once; a user deactivated between RT1 and RT2 gets 403 and nothing is written;
- a role save blocks behind a member's in-flight persist (lock ordering test with a barrier);
- RLS pre-check reports hidden ids as not found; post-check rolls back a row moved out of grant;
- foreign child id under the wrong parent is rejected; `null` versus `[]` collections;
- write-once change rejected; server-owned client values ignored; diff-gated `IsActive` without
  the `Activate` grant is 403, with it passes;
- unique race: two inserts of the same `Code` from two connections — one 422 `Unique` at the
  path, never a 500; `[Unique]` declarations all have a backing index (model test);
- FK delete maps to `Fk.InUse`; strict delete-by-ids leaves nothing deleted on one bad id;
- delete-by-query refuses an empty filter, honors the cap and dry run;
- tree: C# cycle message; SQL cycle formed between rounds is rolled back with `Tree.Cycle`;
  counts recomputed for ancestors not in the payload;
- commit probe: a connection killed after `COMMIT` (a `KILL` from a second connection at the right
  moment, or a transient injected by a proxy) yields exactly one committed write and a successful
  response;
- validation rounds beyond the limit throw `InvalidOperationException` and are metered;
- every raw statement's declared write set covers its ScriptDom targets (analyzer test corpus);
- every projected operation has a securable (descriptor test), and `MetricCollector<T>` asserts
  the instruments above.

**Confidence.** High.

### D26 — Compatibility rules (what a platform minor may and may not do)

**Decision.** A platform minor may add hooks (with default bodies), context members, capability
interfaces, error codes, request/response members, and instruments. It may not rename a hook,
remove a code, change a path grammar, reorder pipeline steps observably, or change a default in
`CrudLimits`. Servers ignore unknown JSON members and clients tolerate new ones (N−1 during
swaps). The emitter binds by metadata, so a pack adding a column reorders nothing observable.
A test pins the public surface of `Tellma.Core.Abstractions.Services` with an API golden (the
C# equivalent of the client's API Extractor gate).

**Confidence.** High.

---

## 3. Contracts

### 3.1 The service base and its hooks (`Tellma.Core.Abstractions.Services`)

```csharp
namespace Tellma.Core.Abstractions.Services;

/// <summary>
///     The authoring surface of an entity stack: the standard operations, forwarded to the
///     platform pipeline, and the closed set of hooks a pack or distribution overrides. Concrete
///     services are generic over the leaf entity type so a distribution that extends the entity
///     reuses the service unchanged.
/// </summary>
/// <typeparam name="TEntity">The leaf entity type registered for this stack.</typeparam>
/// <typeparam name="TKey">The key type (<c>int</c> by default, <c>long</c> by opt-in).</typeparam>
public abstract class EntityService<TEntity, TKey>(IEntityPipeline<TEntity, TKey> pipeline)
    : IEntityBehavior<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : struct, IEquatable<TKey>
{
    /// <summary>The pipeline this service forwards to; exposed for capability extension methods.</summary>
    protected IEntityPipeline<TEntity, TKey> Pipeline { get; } = pipeline;

    /// <summary>Runs a query over the entity in the caller's row-level scope (one round trip).</summary>
    public Task<QueryResult> QueryAsync(QueryRequest request, CancellationToken cancellationToken);

    /// <summary>Loads one entity for the details page: the editable shape with children, the
    ///     related-entity dictionary, the declared extras, and the row echo. 404 when missing or
    ///     hidden.</summary>
    public Task<DetailsResult<TEntity>> GetDetailsAsync(DetailsRequest<TKey> request, CancellationToken cancellationToken);

    /// <summary>Loads the editable shape with children for a set of ids without extras — the
    ///     import hydration path. Hidden and missing ids are absent from the result.</summary>
    public Task<IReadOnlyList<TEntity>> GetForEditAsync(GetByIdsRequest<TKey> request, CancellationToken cancellationToken);

    /// <summary>Returns rows in query shape for a set of ids; hidden and missing ids are absent.</summary>
    public Task<QueryResult> GetByIdsAsync(GetByIdsRequest<TKey> request, CancellationToken cancellationToken);

    /// <summary>Saves a batch of entities with their child collections (upsert of top-level rows,
    ///     synchronization of children). Two round trips in the common case.</summary>
    public Task<SaveResult<TEntity>> SaveAsync(IReadOnlyList<TEntity> entities, SaveOptions options, CancellationToken cancellationToken);

    /// <summary>Deletes by ids; all-or-nothing.</summary>
    public Task<DeleteResult> DeleteAsync(DeleteRequest<TKey> request, CancellationToken cancellationToken);

    /// <summary>Deletes every visible row matching a non-empty filter, bounded by the row cap;
    ///     <see cref="DeleteByQueryRequest.DryRun"/> returns the count only.</summary>
    public Task<DeleteResult> DeleteByQueryAsync(DeleteByQueryRequest request, CancellationToken cancellationToken);

    /// <summary>Hook: adjust entities after trimming and server-owned overwrite, before ids are
    ///     assigned. Runs once per save.</summary>
    protected virtual ValueTask PreprocessAsync(SaveContext<TEntity, TKey> context);

    /// <summary>Hook: declare context loads and report errors. Runs concurrently with registered
    ///     validators; awaiting an unloaded promise parks it until the round's batch executes.</summary>
    protected virtual ValueTask ValidateAsync(SaveContext<TEntity, TKey> context);

    /// <summary>Hook: append statements to the persist transaction (no I/O of its own).</summary>
    protected virtual ValueTask ContributeAsync(PersistContext<TEntity, TKey> context);

    /// <summary>Hook: best-effort work after commit; failures are logged, never surfaced.</summary>
    protected virtual ValueTask AfterCommitAsync(SaveOutcome<TEntity, TKey> outcome, CancellationToken cancellationToken);

    /// <summary>Hook: add extras (and their readers) to the details batch.</summary>
    protected virtual void ContributeDetails(DetailsPlan<TEntity, TKey> plan);

    /// <summary>Hook: bespoke row-level grants beyond the permissions table (for example "visible to
    ///     its assignee"), disjoined with the stored grants for the given action. Null adds nothing.</summary>
    protected virtual FilterTree? BespokeGrant(SecurableAction action, ICallerContext caller);

    // Explicit implementation forwards each hook so the Core pipeline can call them.
    ValueTask IEntityBehavior<TEntity, TKey>.PreprocessAsync(SaveContext<TEntity, TKey> context);
    ValueTask IEntityBehavior<TEntity, TKey>.ValidateAsync(SaveContext<TEntity, TKey> context);
    ValueTask IEntityBehavior<TEntity, TKey>.ContributeAsync(PersistContext<TEntity, TKey> context);
    ValueTask IEntityBehavior<TEntity, TKey>.AfterCommitAsync(SaveOutcome<TEntity, TKey> outcome, CancellationToken cancellationToken);
    void IEntityBehavior<TEntity, TKey>.ContributeDetails(DetailsPlan<TEntity, TKey> plan);
    FilterTree? IEntityBehavior<TEntity, TKey>.BespokeGrant(SecurableAction action, ICallerContext caller);
}

/// <summary>Convenience base for the default key type.</summary>
public abstract class EntityService<TEntity>(IEntityPipeline<TEntity, int> pipeline)
    : EntityService<TEntity, int>(pipeline)
    where TEntity : class, IEntity<int>;

/// <summary>The hooks of a service as the pipeline sees them. Implemented explicitly by
///     <see cref="EntityService{TEntity,TKey}"/>; never implemented directly.</summary>
public interface IEntityBehavior<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : struct, IEquatable<TKey>
{
    ValueTask PreprocessAsync(SaveContext<TEntity, TKey> context);
    ValueTask ValidateAsync(SaveContext<TEntity, TKey> context);
    ValueTask ContributeAsync(PersistContext<TEntity, TKey> context);
    ValueTask AfterCommitAsync(SaveOutcome<TEntity, TKey> outcome, CancellationToken cancellationToken);
    void ContributeDetails(DetailsPlan<TEntity, TKey> plan);
    FilterTree? BespokeGrant(SecurableAction action, ICallerContext caller);
}

/// <summary>The platform pipeline behind every service; registered by the Core composition, one
///     closed generic per stack. Sealed in Core: a distribution cannot skip a step.</summary>
public interface IEntityPipeline<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : struct, IEquatable<TKey>
{
    Task<QueryResult> QueryAsync(IEntityBehavior<TEntity, TKey> behavior, QueryRequest request, CancellationToken cancellationToken);
    Task<DetailsResult<TEntity>> GetDetailsAsync(IEntityBehavior<TEntity, TKey> behavior, DetailsRequest<TKey> request, CancellationToken cancellationToken);
    Task<IReadOnlyList<TEntity>> GetForEditAsync(IEntityBehavior<TEntity, TKey> behavior, GetByIdsRequest<TKey> request, CancellationToken cancellationToken);
    Task<QueryResult> GetByIdsAsync(IEntityBehavior<TEntity, TKey> behavior, GetByIdsRequest<TKey> request, CancellationToken cancellationToken);
    Task<SaveResult<TEntity>> SaveAsync(IEntityBehavior<TEntity, TKey> behavior, IReadOnlyList<TEntity> entities, SaveOptions options, CancellationToken cancellationToken);
    Task<DeleteResult> DeleteAsync(IEntityBehavior<TEntity, TKey> behavior, DeleteRequest<TKey> request, CancellationToken cancellationToken);
    Task<DeleteResult> DeleteByQueryAsync(IEntityBehavior<TEntity, TKey> behavior, DeleteByQueryRequest request, CancellationToken cancellationToken);

    /// <summary>Runs a capability operation registered for this stack (activation, tree operations,
    ///     distribution-defined actions) under the standard connect, permission, and effect rules.</summary>
    Task<TResult> RunAsync<TOperation, TRequest, TResult>(IEntityBehavior<TEntity, TKey> behavior, TRequest request, CancellationToken cancellationToken)
        where TOperation : IEntityOperation<TEntity, TKey, TRequest, TResult>;
}
```

### 3.2 Capability interfaces and projected operations

```csharp
namespace Tellma.Core.Abstractions.Services;

/// <summary>Every stack entity: a single-column surrogate key. Zero means "new".</summary>
public interface IEntity<TKey> where TKey : struct, IEquatable<TKey>
{
    TKey Id { get; set; }
}

/// <summary>Every top-level stack entity: four audit columns. <see cref="ModifiedAt"/> is also
///     the optimistic-concurrency stamp; every write through the platform bumps it.</summary>
public interface IAudited
{
    DateTime CreatedAt { get; set; }
    int CreatedById { get; set; }
    DateTime ModifiedAt { get; set; }
    int ModifiedById { get; set; }
}

/// <summary>Activation capability: adds <c>SetActive</c>, the <c>Activate</c> action, the default
///     active-only filter, and gates <see cref="IsActive"/> behind that action on save.</summary>
public interface IActivatable
{
    bool IsActive { get; set; }
}

/// <summary>Tree capability. <c>Node</c> is server-owned, absent from the wire and the table
///     type, and exposed to Queryex as the entity's tree node.</summary>
public interface ITreeEntity<TKey> where TKey : struct, IEquatable<TKey>
{
    TKey? ParentId { get; set; }
    int SubtreeCount { get; set; }
    int Level { get; set; }
}

/// <summary>Tree plus activation: the active-only subtree count the tree view needs.</summary>
public interface IActiveTreeEntity<TKey> : ITreeEntity<TKey>, IActivatable where TKey : struct, IEquatable<TKey>
{
    int ActiveSubtreeCount { get; set; }
}

/// <summary>Marks a property the client may never set; the pipeline overwrites it from the before
///     image (update) or the platform default (insert).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ServerOwnedAttribute : Attribute;

/// <summary>Marks a property settable on insert only; a changed value on update is a validation
///     error, never silently reset.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class WriteOnceAttribute : Attribute;

/// <summary>Marks a property whose change through save requires the named action's grant on that
///     row. Applied to <see cref="IActivatable.IsActive"/> by the capability itself.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class GatedByAttribute(string action) : Attribute
{
    public string Action { get; } = action;
}

/// <summary>Declares a column that the <c>Search</c> parameter covers.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SearchableAttribute(SearchKind kind = SearchKind.Contains) : Attribute
{
    public SearchKind Kind { get; } = kind;
}

public enum SearchKind { Contains, Prefix }

/// <summary>Opts a string property out of the default trim-and-null normalization.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PreserveWhitespaceAttribute : Attribute;

/// <summary>A capability or distribution operation the pipeline runs under the standard rules.
///     Registered per stack at composition; its descriptor feeds securables and endpoints.</summary>
public interface IEntityOperation<TEntity, TKey, in TRequest, TResult>
    where TEntity : class, IEntity<TKey>
    where TKey : struct, IEquatable<TKey>
{
    /// <summary>The operation's name, kind, and securable — reflected once at composition.</summary>
    static abstract OperationDescriptor Descriptor { get; }

    /// <summary>Runs after connect and the coarse permission check; receives the batch to append
    ///     statements to and the caller's grant filter for the operation's action.</summary>
    ValueTask<TResult> ExecuteAsync(OperationContext<TEntity, TKey, TRequest> context, CancellationToken cancellationToken);
}

/// <summary>Capability extension methods; the constraint is the compile-time gate.</summary>
public static class ActivatableEntityServiceExtensions
{
    /// <summary>Activates or deactivates rows; bumps the stamp, never checks it.</summary>
    public static Task<SetActiveResult> SetActiveAsync<TEntity, TKey>(
        this EntityService<TEntity, TKey> service, SetActiveRequest<TKey> request, CancellationToken cancellationToken)
        where TEntity : class, IEntity<TKey>, IActivatable
        where TKey : struct, IEquatable<TKey>;
}

public static class TreeEntityServiceExtensions
{
    public static Task<QueryResult> GetByParentIdsAsync<TEntity, TKey>(
        this EntityService<TEntity, TKey> service, GetByParentIdsRequest<TKey> request, CancellationToken cancellationToken)
        where TEntity : class, IEntity<TKey>, ITreeEntity<TKey>
        where TKey : struct, IEquatable<TKey>;

    public static Task<DeleteResult> DeleteWithDescendantsAsync<TEntity, TKey>(
        this EntityService<TEntity, TKey> service, DeleteRequest<TKey> request, CancellationToken cancellationToken)
        where TEntity : class, IEntity<TKey>, ITreeEntity<TKey>
        where TKey : struct, IEquatable<TKey>;
}
```

### 3.3 Requests, results, options, limits

```csharp
namespace Tellma.Core.Abstractions.Services;

/// <summary>A query over one entity. Every text member is Queryex; <see cref="Filter"/> is a
///     tree so the pipeline can conjoin the search, the default filters, and the row-level
///     grant without concatenation.</summary>
public sealed record QueryRequest
{
    public required string Select { get; init; }
    public FilterTree? Filter { get; init; }
    public string? Search { get; init; }
    public string? OrderBy { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 50;
    public bool IncludeCount { get; init; }
    /// <summary>Activatable entities only: lifts the default <c>IsActive = true</c> conjunct.</summary>
    public bool IncludeInactive { get; init; }
    /// <summary>Tree entities only: also returns the visible ancestors of the page's rows.</summary>
    public bool IncludeAncestors { get; init; }
    /// <summary>Values for the declared parameters the expressions reference.</summary>
    public IReadOnlyDictionary<string, object?> Arguments { get; init; } = EmptyArguments;
}

/// <summary>Rows as arrays of scalars, positional to <see cref="Columns"/>.</summary>
public sealed record QueryResult(
    IReadOnlyList<QueryexColumn> Columns,
    IReadOnlyList<object?[]> Rows,
    int? TotalCount,
    bool IsCountCapped,
    IReadOnlyList<object?[]>? Ancestors);

public sealed record DetailsRequest<TKey>(TKey Id, string? Select = null, IReadOnlyList<string>? Extras = null)
    where TKey : struct, IEquatable<TKey>;

/// <summary>The details payload: the editable entity with children, related entities keyed by
///     logical entity name then id, extras by name, and the row in query shape when a select was
///     supplied.</summary>
public sealed record DetailsResult<TEntity>(
    TEntity Entity,
    RelatedEntities Related,
    IReadOnlyDictionary<string, object> Extras,
    object?[]? RowEcho);

public sealed record GetByIdsRequest<TKey>(IReadOnlyList<TKey> Ids, string? Select = null)
    where TKey : struct, IEquatable<TKey>;

public sealed record SaveOptions
{
    /// <summary>Read the saved entities back in details shape (false for import).</summary>
    public bool ReturnEntities { get; init; } = true;
    /// <summary>Skip the stamp comparison; never affects missing rows or permissions.</summary>
    public bool OverrideConcurrency { get; init; }
    /// <summary>Refuse updates whose <c>ExpectedStamp</c> is null (the web details-page save).</summary>
    public bool RequireStamps { get; init; }
    /// <summary>Recorded in telemetry and visible to hooks; never changes a rule.</summary>
    public SaveSource Source { get; init; } = SaveSource.Ui;
    /// <summary>The row echo select, when <see cref="ReturnEntities"/>.</summary>
    public string? Select { get; init; }
}

public enum SaveSource { Ui, Import, Agent, System }

/// <summary>The stamp a client sends back with an update. Carried on the entity as
///     <c>ExpectedStamp</c> by the wire shape (T6); null means "no check".</summary>
public interface IHasExpectedStamp
{
    DateTime? ExpectedStamp { get; set; }
}

public sealed record SaveResult<TEntity>(
    IReadOnlyList<TEntity>? Entities,
    RelatedEntities? Related,
    IReadOnlyList<object?[]>? RowEchoes,
    IReadOnlyList<int> Ids);

public sealed record DeleteRequest<TKey>(IReadOnlyList<TKey> Ids, IReadOnlyList<DateTime?>? ExpectedStamps = null)
    where TKey : struct, IEquatable<TKey>;

public sealed record DeleteByQueryRequest(FilterTree Filter, IReadOnlyDictionary<string, object?> Arguments, bool DryRun);

public sealed record DeleteResult(int Count);

public sealed record SetActiveRequest<TKey>(IReadOnlyList<TKey> Ids, bool IsActive) where TKey : struct, IEquatable<TKey>;

public sealed record SetActiveResult(int Count);

public sealed record GetByParentIdsRequest<TKey>(IReadOnlyList<TKey?> ParentIds, string Select, bool IncludeInactive)
    where TKey : struct, IEquatable<TKey>;

/// <summary>Bounds every operation; per-surface overrides are configuration (T6).</summary>
public sealed record CrudLimits
{
    public static CrudLimits Default { get; } = new();
    public int MaxPageSize { get; init; } = 500;
    public int CountCap { get; init; } = 10_000;
    public int MaxSaveEntities { get; init; } = 10_000;
    public int MaxChildrenPerEntity { get; init; } = 10_000;
    public int MaxIds { get; init; } = 10_000;
    public int MaxDeleteByQueryRows { get; init; } = 10_000;
    public int MaxValidationRounds { get; init; } = 3;
    public int MaxSearchLength { get; init; } = 200;
    public int MaxExtras { get; init; } = 16;
}
```

### 3.4 Validation (`Tellma.Core.Abstractions.Validation`)

```csharp
namespace Tellma.Core.Abstractions.Validation;

/// <summary>A registered rule set for an entity type; resolved for the leaf and every base type
///     up to the pack's abstract base so pack rules keep running on extended leaves.</summary>
public interface IEntityValidator<TEntity> where TEntity : class
{
    /// <summary>Declares loads and reports errors; may await context promises (parking until the
    ///     round's batch executes) but must not perform I/O of its own.</summary>
    ValueTask ValidateAsync(SaveContext<TEntity> context);
}

/// <summary>Everything a validator can see during a save. Immutable except for
///     <see cref="Errors"/>.</summary>
public sealed class SaveContext<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : struct, IEquatable<TKey>
{
    /// <summary>The entities after preprocessing, ids assigned, in payload order.</summary>
    public IReadOnlyList<TEntity> Entities { get; }
    /// <summary>The before image of each update (null for inserts), loaded with the caller's
    ///     save grant conjoined — an update whose before image is null was already rejected.</summary>
    public IReadOnlyList<TEntity?> Before { get; }
    /// <summary>The child before images, keyed by child type then parent id.</summary>
    public ChildImages Children { get; }
    public SaveOptions Options { get; }
    public ICallerContext Caller { get; }
    public IContextLoader Loader { get; }
    public ValidationErrors Errors { get; }
    /// <summary>The current validation round, 1-based.</summary>
    public int Round { get; }
    /// <summary>True when <paramref name="index"/> is an insert.</summary>
    public bool IsNew(int index);
    /// <summary>True when the property changed relative to the before image (false for inserts).</summary>
    public bool Changed<TValue>(int index, Func<TEntity, TValue> property);
}

/// <summary>Batched, deduplicated context loading. Every request is a promise resolved by the
///     round's single round trip; loads are not row-level filtered unless asked.</summary>
public interface IContextLoader
{
    ContextPromise<IReadOnlyList<T>> ByIds<T, TKey>(IEnumerable<TKey> ids, string? select = null)
        where T : class, IEntity<TKey> where TKey : struct, IEquatable<TKey>;
    ContextPromise<IReadOnlyList<T>> ByParentIds<T, TKey>(IEnumerable<TKey> parentIds, string? select = null)
        where T : class where TKey : struct, IEquatable<TKey>;
    ContextPromise<IReadOnlyList<T>> Query<T>(QuerySpec spec, IReadOnlyDictionary<string, object?>? arguments = null)
        where T : class;
    /// <summary>Like <see cref="Query{T}"/> but conjoined with the caller's read grant.</summary>
    ContextPromise<IReadOnlyList<T>> Visible<T>(QuerySpec spec, IReadOnlyDictionary<string, object?>? arguments = null)
        where T : class;
    ContextPromise<IReadOnlyList<TRow>> Sql<TRow>(SqlStatement statement, RowReader<TRow> reader);
    /// <summary>Seeds the cache so a validator that asks for a row already in the payload gets it
    ///     without a round trip.</summary>
    void Prime<T, TKey>(T entity) where T : class, IEntity<TKey> where TKey : struct, IEquatable<TKey>;
}

/// <summary>A value that becomes available when the round's batch executes. Awaiting it before
///     then parks the validator; the pipeline dispatches when every validator is parked or done.</summary>
public readonly struct ContextPromise<T>
{
    public bool IsLoaded { get; }
    /// <summary>The value; throws <see cref="InvalidOperationException"/> when not yet loaded.</summary>
    public T Value { get; }
    public ContextPromiseAwaiter<T> GetAwaiter();
}

/// <summary>Accumulates errors keyed by path; thread-safe because validators run concurrently.</summary>
public sealed class ValidationErrors
{
    public bool HasErrors { get; }
    public IReadOnlyList<ValidationError> Items { get; }
    /// <summary>Adds an error at a path such as <c>[3].Lines[1].Quantity</c>.</summary>
    public void Add(string path, string code, params ReadOnlySpan<KeyValuePair<string, string>> arguments);
    /// <summary>Adds an error at <c>[index].Property</c>.</summary>
    public void Add<TEntity, TValue>(int index, Expression<Func<TEntity, TValue>> property, string code, params ReadOnlySpan<KeyValuePair<string, string>> arguments);
}

/// <summary>One error: a path, a stable code, and named arguments. The host localizes.</summary>
public sealed record ValidationError(string Path, string Code, IReadOnlyList<KeyValuePair<string, string>> Arguments);

/// <summary>The platform's own error codes. Packs and distributions publish theirs beside them.</summary>
public static class ValidationCodes
{
    public const string Required = "Required";
    public const string MaxLength = "MaxLength";
    public const string Range = "Range";
    public const string Unique = "Unique";
    public const string EntityNotFound = "Entity.NotFound";
    public const string DuplicateId = "Entity.DuplicateId";
    public const string WriteOnce = "WriteOnce";
    public const string FkNotFound = "Fk.NotFound";
    public const string FkInUse = "Fk.InUse";
    public const string TreeCycle = "Tree.Cycle";
    public const string BlobTokenInvalid = "Blob.TokenInvalid";
}
```

### 3.5 Persist and post-commit contexts

```csharp
namespace Tellma.Core.Abstractions.Services;

/// <summary>A transactional participant of a save. Resolved for the leaf and its base types.</summary>
public interface ISaveEffect<TEntity> where TEntity : class
{
    /// <summary>Appends statements to the persist transaction; must not perform I/O.</summary>
    ValueTask ContributeAsync(PersistContext<TEntity> context);
    /// <summary>Best-effort work after the commit is acknowledged; failures are logged and metered.</summary>
    ValueTask AfterCommitAsync(SaveOutcome<TEntity> outcome, CancellationToken cancellationToken);
}

/// <summary>What a transactional participant sees: the batch (append-only) and the save's facts.</summary>
public sealed class PersistContext<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : struct, IEquatable<TKey>
{
    public IReadOnlyList<TEntity> Entities { get; }
    public IReadOnlyList<TEntity?> Before { get; }
    /// <summary>The batch's stamp, written to every touched row's <c>ModifiedAt</c>.</summary>
    public DateTime Stamp { get; }
    public ICallerContext Caller { get; }
    public SaveOptions Options { get; }
    /// <summary>Append-only view of the persist batch. Every statement declares its write set.</summary>
    public IPersistBatch Batch { get; }
    /// <summary>The batch-local table variable holding the touched top-level ids (<c>@touched</c>).</summary>
    public string TouchedIdsSource { get; }
}

/// <summary>Append-only batch surface for participants: raw statements, compiled queries, and
///     result readers whose values are available in <see cref="SaveOutcome{TEntity}"/>.</summary>
public interface IPersistBatch
{
    /// <summary>Appends a statement; <paramref name="writes"/> lists every table it may modify and
    ///     drives the automatic tag bumps.</summary>
    void Add(SqlStatement statement, IReadOnlyList<TableIdentity> writes);
    /// <summary>Appends a statement that returns a result set, read after commit.</summary>
    ResultHandle<TRow> AddReader<TRow>(SqlStatement statement, IReadOnlyList<TableIdentity> writes, RowReader<TRow> reader);
    /// <summary>Binds a table-valued parameter for statements in this batch.</summary>
    string AddTableValuedParameter<TRow>(IReadOnlyList<TRow> rows) where TRow : class;
}

/// <summary>What post-commit participants see.</summary>
public sealed class SaveOutcome<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : struct, IEquatable<TKey>
{
    public IReadOnlyList<TEntity> Entities { get; }
    public IReadOnlyList<TEntity?> Before { get; }
    public DateTime Stamp { get; }
    public ICallerContext Caller { get; }
    /// <summary>The rows a participant's reader returned inside the transaction.</summary>
    public IReadOnlyList<TRow> ResultOf<TRow>(ResultHandle<TRow> handle);
}
```

### 3.6 Errors (`Tellma.Core.Abstractions.Errors`)

```csharp
namespace Tellma.Core.Abstractions.Errors;

/// <summary>Base of the closed set of exceptions the web layer maps to statuses. Carries a stable
///     code and named arguments; the message is for logs, never for users.</summary>
public abstract class TellmaException(string code, IReadOnlyList<KeyValuePair<string, string>> arguments, string message)
    : Exception(message)
{
    public string Code { get; } = code;
    public IReadOnlyList<KeyValuePair<string, string>> Arguments { get; } = arguments;
}

/// <summary>Field-level or batch-level validation failures (422).</summary>
public sealed class ValidationException(IReadOnlyList<ValidationError> errors) : TellmaException("Validation", [], "Validation failed.")
{
    public IReadOnlyList<ValidationError> Errors { get; } = errors;
}

/// <summary>A single requested entity is missing or hidden (404).</summary>
public sealed class NotFoundException(string resource, string id) : TellmaException("NotFound", [new("resource", resource), new("id", id)], "Not found.");

/// <summary>The caller lacks the grant, is inactive, or moved a row out of scope (403).</summary>
public sealed class ForbiddenException(string code, string resource, string? action)
    : TellmaException(code, [new("resource", resource), new("action", action ?? string.Empty)], "Forbidden.");

/// <summary>Stamp mismatches, deleted rows, or churning permissions (409).</summary>
public sealed class ConcurrencyConflictException(string code, IReadOnlyList<ConcurrencyConflict> conflicts)
    : TellmaException(code, [], "Concurrency conflict.")
{
    public IReadOnlyList<ConcurrencyConflict> Conflicts { get; } = conflicts;
}

/// <summary>One conflicting top-level row.</summary>
public sealed record ConcurrencyConflict(string Id, DateTime? ModifiedAt, int? ModifiedById, bool IsMissing);

/// <summary>A request exceeded a platform limit (413).</summary>
public sealed class RequestLimitException(string limit, long actual, long maximum)
    : TellmaException("Limit", [new("limit", limit), new("actual", actual.ToString(CultureInfo.InvariantCulture)), new("maximum", maximum.ToString(CultureInfo.InvariantCulture))], "Limit exceeded.");

/// <summary>Queryex text in a request failed to compile (400).</summary>
public sealed class InvalidQueryException(IReadOnlyList<QueryexDiagnostic> diagnostics) : TellmaException("InvalidQuery", [], "Invalid query.")
{
    public IReadOnlyList<QueryexDiagnostic> Diagnostics { get; } = diagnostics;
}
```

### 3.7 Securables and the descriptor the feature emits (`Tellma.Core.Abstractions.Security`)

```csharp
namespace Tellma.Core.Abstractions.Security;

/// <summary>The unit of authorization: a resource, an action, and whether a row filter applies.</summary>
/// <param name="Resource">The entity's canonical table identity, e.g. "gl.Center".</param>
/// <param name="Action">One of <see cref="SecurableAction"/> or a distribution-defined name.</param>
/// <param name="SupportsFilter">Whether a grant may carry a row-level criterion.</param>
/// <param name="FilterRoot">The Queryex root entity the criterion binds against; null when no filter.</param>
public sealed record Securable(string Resource, string Action, bool SupportsFilter, string? FilterRoot);

/// <summary>The platform's action names. Save implies Read.</summary>
public static class SecurableAction
{
    public const string Read = "Read";
    public const string Save = "Save";
    public const string Delete = "Delete";
    public const string Activate = "Activate";
}

/// <summary>What the stack feature emits at composition for one entity; consumed by the
///     securables registry, endpoint projection, and the MCP tool builder.</summary>
public sealed record EntityStackDescriptor(
    Type EntityType,
    Type ServiceType,
    string Resource,
    IReadOnlyList<OperationDescriptor> Operations,
    IReadOnlyList<string> Capabilities,
    bool IsReadOnly);

public sealed record OperationDescriptor(string Name, OperationKind Kind, Securable Securable, bool IsBulk, bool WebSurfaceOnly);

public enum OperationKind { Read, Save, Delete, Action }
```

### 3.8 Shapes needed from other themes' seams

```csharp
// T1 — request context (seam 9). Scoped holder populated once per request or job scope; never AsyncLocal.
public interface ICallerContext
{
    int TenantId { get; }
    int UserId { get; }
    string Subject { get; }
    CultureInfo Culture { get; }
    string CalendarId { get; }
    TimeZoneInfo TimeZone { get; }
    DateOnly Today { get; }
    bool IsSandbox { get; }
}

// T4 — permission evaluation (seam 11). Sync over a cached, tag-validated set.
public interface IPermissionEvaluator
{
    /// <summary>The caller's permission set for the tag the connect statement returned; rebuilt
    ///     on a miss. Cheap on a hit.</summary>
    ValueTask<PermissionSet> GetAsync(ICallerContext caller, Guid permissionsTag, CancellationToken cancellationToken);
}
public sealed class PermissionSet
{
    public Guid Tag { get; }
    /// <summary>Allowed plus the disjunction of the grants' criteria (null = unrestricted);
    ///     empty disjunction is denial.</summary>
    public Grant For(Securable securable);
}
public sealed record Grant(bool Allowed, FilterTree? Filter, IReadOnlyList<GrantReason> Reasons);

// T2 — the batch abstraction (seam 1), as the pipeline consumes it.
public interface IDbBatchBuilder
{
    QueryHandle AddQuery(QuerySpec spec, QueryBindings bindings, bool mayRetry = true);
    ResultHandle<TRow> AddReader<TRow>(SqlStatement statement, IReadOnlyList<TableIdentity> writes, RowReader<TRow> reader, bool mayRetry);
    void Add(SqlStatement statement, IReadOnlyList<TableIdentity> writes, bool mayRetry);
    void AddSave(SaveSpec spec);                       // emitter: update changed / insert new / synchronize children; stamps; derives writes
    void AddTreeRecompute(TableIdentity table, string affectedIdsSource);
    void AddIdReservation(TableIdentity table, int count);
    string AddTableValuedParameter<TRow>(IReadOnlyList<TRow> rows) where TRow : class;
    string DeclareIdTable(string name);                 // batch-local table variable in the reserved namespace
    Task<BatchResult> ExecuteAsync(BatchExecution mode, CancellationToken cancellationToken); // ReadOnly | Transaction
}
// Queryex amendments (documented by T2): list restriction { KeyIn | DescendantOfAny | AncestorOfAny } with a
// TVP or batch-local table source; capped count; level().

// T3 — tags (seam 5): the connect statement returns TenantTags + UserTags; the persist prologue locks the caller's
// UserStamps row; the executor bumps entity-type tags from the write set.

// T7 — blob staging (seam 12): IBlobStaging.ValidateTokensStatement(tokens) for RT1 and
// ConfirmStatement(tokens) / ReleaseStatement(oldIds) for RT2; DeleteAsync(oldIds) after commit.

// T10 — notifications (seam 15): INotificationEnqueuer.Contribute(IPersistBatch, IReadOnlyList<NotificationRequest>).
```

---

## 4. Schema

This theme owns no table. It imposes column contracts on tables other themes own; each block names
the owner and states exactly what the pipeline requires.

**Every editable top-level table (owner T2 — audit and stamp columns).**

```sql
-- Present on every table whose entity implements IAudited (all stack entities).
[CreatedAt]     datetime2(7)   NOT NULL,   -- UTC; set on insert; server-owned
[CreatedById]   int            NOT NULL,   -- FK → core.Users(Id); server-owned
[ModifiedAt]    datetime2(7)   NOT NULL,   -- UTC; the concurrency stamp; bumped by every platform write; server-owned
[ModifiedById]  int            NOT NULL,   -- FK → core.Users(Id); server-owned
-- No rowversion column. No index on ModifiedAt is required by the pipeline (compared by PK).
```

**Activatable tables (owner T2).**

```sql
[IsActive]      bit            NOT NULL CONSTRAINT [DF_<Table>_IsActive] DEFAULT (1),
-- Optional filtered index for the active-only default filter on large tables: IX_<Table>_Active ON (Id) WHERE IsActive = 1.
```

**Tree tables (owner T2; recompute statement emitted by T2, appended by this pipeline).**

```sql
[ParentId]            int          NULL,      -- FK → same table (Id); FK name FK_<Table>_ParentId
[Node]                hierarchyid  NOT NULL,  -- server-owned; excluded from the table type; never on the wire
[Level]               AS ([Node].GetLevel()) PERSISTED,
[SubtreeCount]        int          NOT NULL CONSTRAINT [DF_<Table>_SubtreeCount] DEFAULT (1),
[ActiveSubtreeCount]  int          NOT NULL CONSTRAINT [DF_<Table>_ActiveSubtreeCount] DEFAULT (1),  -- only with IsActive
-- UX_<Table>_Node UNIQUE (Node); IX_<Table>_Level_Node (Level, Node) for breadth-first loads.
```

**Uniqueness declarations (owner T2; consumed by D8).**

```sql
-- One index per [Unique] declaration, deterministic name so error 2601/2627 maps to a property path:
CREATE UNIQUE INDEX [UX_<Table>_<Col>[_<Col>]] ON [<schema>].[<Table>] ([<Col>] [, <Col>]) WHERE [<Col>] IS NOT NULL;  -- filter only for nullable columns
-- FK constraint names FK_<Table>_<Column> so error 547 maps to a property path.
```

**Caller stamps (owner T3/T4; the prologue in D6 depends on this exact shape).**

```sql
CREATE TABLE [core].[UserStamps] (
    [UserId]           int              NOT NULL CONSTRAINT [PK_UserStamps] PRIMARY KEY,   -- FK → core.Users(Id)
    [PermissionsTag]   uniqueidentifier NOT NULL,   -- regenerated by every write that can change this user's effective permissions
    [UserSettingsTag]  uniqueidentifier NOT NULL,
    [LastActiveAt]     datetime2(3)     NULL        -- stamped by the connect statement; never bumps any stamp
);
-- Non-temporal. Bumps are UPDATE … SET PermissionsTag = NEWID() WHERE UserId IN (<affected>), executed in the same
-- transaction as the role/membership/deactivation write. The persist prologue takes UPDLOCK on the caller's row.
```

**Table-valued shapes the pipeline binds (owner T2, spec 0001 route).** The entity's own row-image
type for `@inserts` and `@updates` (two parameters of one type), `IdList` for id sets, and one
standalone type:

```sql
-- Logical name core.SaveStampList; a plain [TableType] class in Abstractions (BCL annotations only).
[Id]             int           NOT NULL PRIMARY KEY,
[ExpectedStamp]  datetime2(7)  NULL
```

---

## 5. Answers

| Brain-dump question (abridged) | Answer | Decision |
|---|---|---|
| Are Data / Service / Web the proper layer names? | Yes; conventional .NET names are `Data`, `Services`, `Api` (or `Web`) as folders in one project; T1 fixes the folder names. | T1; noted |
| Record + blobs: which pattern? | Staged upload with tokens, confirmed in the persist batch, swept after N hours; no multipart save; the pipeline has no pre-commit non-transactional step. | D13, D16 |
| Write-once columns (Subject, Email): two UDTTs, or a service rule? | One row image; the pipeline validates `[WriteOnce]` against the before image and reports `WriteOnce` — never silent reset, never a second UDTT. | D9 |
| `SavedById` on weak entities? | No audit columns on children; the parent's `ModifiedAt/ById` is bumped by any child change and covers the aggregate. | D7, seam 17 |
| Keep hierarchyid in sync: in memory or SQL after save? | SQL recompute appended to the persist batch (T2 emits); C# cycle check for the message; SQL cycle `THROW` as the guarantee. | D17 |
| Model `CenterType` as an enum stored as string? | Yes (T2's enum-as-string convention); the pipeline validates enum members as attribute validation. | T2 |
| Extensibility: services against interfaces, validate interface matches DB? | Generic services over leaf types with class constraints; the class is the table, so no interface/DB drift exists. | D2, D21 |
| Names for the save emitter and multi-statement executor? | Positions: `SaveEmitter` and `DbBatch`/`DbBatchBuilder` + `DbBatchExecutor`; T2 decides. | seam 1 |
| Does the emitter need to know upsert vs synchronize? | No flag: top-level rows are upserted, nested collections are synchronized under their parents — derived from the save graph. | D10, seam 1 |
| Ids: reserve without a dedicated round trip; who assigns; un-consume on failure? | Reservation rides RT1; the pipeline assigns after preprocess; never un-consumed. | D11 |
| Keep the `Search` parameter? Picker vs page hint? | Keep, server-side over `[Searchable]` columns; no hint. | D18 |
| Details: Queryex or raw SQL? | Queryex for the entity, children (list restriction) and related entities (navigation selects); raw/`SqlBuilder<T>` only for extras via `ContributeDetails`. | D15 |
| Inject custom validators that participate in the batch? | `IEntityValidator<TEntity>` + the `ValidateAsync` hook, awaiting `ContextPromise<T>` from `IContextLoader`; parked validators are resumed after one batch. | D8 |
| Dedupe validation context queries? | Structural key (root, filter tree, arguments, raw text); differing selects are unioned. | D8 |
| Where does the transaction begin and end? | The persist batch text only: `SET XACT_ABORT ON; BEGIN TRAN … COMMIT`. | D6 |
| Collapse DB calls #1 and #2 with cached permissions? | Yes: connect rides RT1; stale tag → refresh and re-run once; the persist prologue re-verifies the tag under `UPDLOCK`. | D4, D6 |
| Empty vs missing child collection? | `[]` deletes all; `null` leaves untouched (skipped, not hydrated). | D10 |
| Base service class vs composition? | Thin abstract base as the authoring surface; sealed pipeline; DI-registered validators and effects for multiplicity. | D2 |
| 5 ids requested, 4 found: 4 or 404? | Reads: the 4. Single details: 404. Delete: all-or-nothing 422 at `Ids[i]`. | D3, D14 |
| Custom filter (TVP list restriction) and `level()` | Required from T2 as engine amendments; the pipeline uses `KeyIn`, `DescendantOfAny`, `AncestorOfAny` restrictions with TVP or batch-local sources. | seam 1 |
| Capability boilerplate: a design that reuses across distros? | One interface on the entity; operations, securables, gates, default filters, endpoints projected at composition. | D16 |
| UserService custom endpoints | Distribution/pack operations implement `IEntityOperation` with a declared securable; T8 owns the bodies. | D16, T8 |
| Extract endpoint boilerplate; source-generated JSON; `X-Today` header | Positions for T6: endpoints projected from `EntityStackDescriptor`; STJ source-generated contexts; carry time zone and calendar as headers and bind `today()` from the zone. | seam 13 |
| Registering securables hard to forget? | Securables come from operation descriptors; an operation without one is not projected; startup audit over the endpoint table. | D20 |
| Is "securable" the right word? | Yes. | D20 |
| Permissions invalidated by schema change? | Fail closed per grant (T4); the pipeline treats a non-compiling grant as absent and never blocks the user's other grants. | seam 11 |
| Alternative to `rowversion`, implementable in C#? | `ModifiedAt` as the stamp, app-generated per batch, checked under `UPDLOCK` in the persist transaction; override flag skips the comparison only. | D7 |
| Rate/payload limits | `CrudLimits` at the service; per-surface overrides at T6. | D3 |
| How to guarantee the cache tag is bumped on every write? | Write sets on every statement; automatic bumps from their union; no connection outside a batch; analyzers for raw SQL and `SaveChanges`. | D23 |
| Status code from exceptions: enumerate or interface? | A closed set of six sealed `TellmaException`s; T6 maps by type. | D19 |
| Messages or codes? | Codes + arguments from the pipeline; T6 localizes and returns both. | D8, D19 |

---

## 6. Seams

1. **Batch abstraction (T2).** Required: the `IDbBatchBuilder` shape in §3.8 — compiled queries,
   raw statements with declared write sets, the save emitter call, tree recompute, id
   reservation, TVPs, batch-local id tables in the reserved namespace, per-statement `mayRetry`
   AND-ed into a round-trip eligibility, `BatchExecution.Transaction` wrapping the text in
   `XACT_ABORT ON`/`BEGIN TRAN`/`COMMIT`, error-number mapping for 50400–50599, the commit probe
   hook, and `IDbCallBudget` incremented per round trip. Result sets before a `THROW` must be
   readable. Statement order is the insertion order.
2. **Entity class vs wire shape (T2 owns; T5 consumes).** One class with `[NotMapped]` child
   collections and `ExpectedStamp`; the editable/server-owned split is metadata on the class
   (`[ServerOwned]`, `[WriteOnce]`, `[GatedBy]`, capability-derived). Risks named and closed: client
   values for server-owned columns are overwritten from metadata (D9); write-once validated (D9);
   foreign child ids rejected (D10); `null` vs `[]` (D10); the stamp string must round-trip
   verbatim (D7).
3. **One capability, declared once (T5 owns).** The entity interface is the declaration; the
   table (§4) and the projected operations, securables, gates, default filters, and endpoints
   are derived at composition from `EntityStackDescriptor` (D16).
4. **Queryex schema per tenant (T2 owns).** The pipeline needs schema identity to change when
   `SettingsTag` changes (D4 re-run), `Name2`/`Name3` gating to make a client value for an absent
   column ignorable, and the three list restrictions plus capped count and `level()`.
5. **Version tags (T3 owns).** The connect statement returns `SettingsTag`, `PermissionsTag`,
   `UserSettingsTag`; `core.UserStamps` has the exact shape in §4; entity-type tags are bumped
   by the executor from write sets; tags are opaque `uniqueidentifier`s regenerated on bump.
6. **Feature composition (T1 owns).** The stack feature declares `EntityStackDescriptor` and
   contributes: the closed `IEntityPipeline<TEntity,TKey>`, the service, the capability
   operations, validators and effects resolved along the type chain. `readOnly` is a feature
   option. Startup validation aggregates: every projected operation has a securable; every
   `[Unique]` has an index; every `[GatedBy]` names a registered action.
7. **Natural keys (T2).** The pipeline exposes `GetForEditAsync` for hydration and treats
   `[Unique]` as the source of the index, the validator, and the error map.
8. **Background-task columns (T10 semantics, T2 emits).** Task rows are appended through
   `IPersistBatch` in `ContributeAsync`; the runner is nudged in `AfterCommitAsync`.
9. **Request context (T1).** `ICallerContext` as in §3.8, a scoped holder copied into job scopes;
   the pipeline never reads `HttpContext`.
10. **Platform exceptions (T5 owns the types).** §3.6; T6 owns the status map (D19).
11. **Permission evaluation (T4).** `IPermissionEvaluator.GetAsync(caller, tag)` → `PermissionSet`
    with `For(securable)` → `Grant(Allowed, Filter, Reasons)`; `Save` implies `Read`; bespoke
    grants disjoined via the service's `BespokeGrant` hook; a grant whose filter fails to compile
    contributes nothing.
12. **Blob staging tokens (T7).** Validate-tokens statement in RT1, confirm/release statements in
    RT2, deletes after commit; the built-in `BlobAttachmentEffect<TEntity>` implements the recipe.
13. **Wire shapes (T6).** `QueryResult` as arrays of arrays with columns; details/save envelopes
    as in §3.3; the `errors` dictionary keyed by path with both message and code; the stamp
    round-trip rule; `DeleteByQuery` web-only; `RequireStamps` on the details-page save.
14. **Telemetry (T2 owns the round-trip instrument; T5 names its own in D24).**
15. **Notification enqueue riding the save (T10).** `INotificationEnqueuer.Contribute(IPersistBatch, …)`
    called from a platform `ISaveEffect`.
16. **Connect-call collapse (T4/T5).** Adopted with the failure modes enumerated in D4 and closed
    for writes in D6.
17. **Vocabulary.** Four audit columns on every top-level entity, `ModifiedAt` as the stamp,
    temporal as an additive marker capability; no audit columns on weak entities; resource names
    equal table identities (singular preferred, but whichever seam 17 fixes for tables);
    "securable" for the tuple; "tag" for cache versions (`PermissionsTag`, `SettingsTag`);
    `CrudLimits` for bounds.

---

## 7. Departures

| ARCHITECTURE.md | Departure | Reason |
|---|---|---|
| Dependency graph: only `Tellma.Core` references `Tellma.Core.Queryex`. | `Tellma.Core.Abstractions` references `Tellma.Core.Queryex`. | `FilterTree`, `QuerySpec`, and `QueryexDiagnostic` appear in the contracts every module consumes; mirroring them would create two types per concept. Queryex depends on nothing, so the edge carries no weight. |
| Endpoints projected as read → GET, save → POST, delete → DELETE. | The projection is T6's; this theme only requires that `DeleteByQuery` is web-surface-only and that operations without a securable are not projected. | Security guardrail on a dangerous operation. |
| "Every UPDATE writes a history row" is accepted knowingly for temporal tables. | The emitter (T2) skips unchanged top-level rows; the pipeline still bumps parents whose children changed. | The stamp then means "something changed", which the concurrency prompt relies on. |
| Capability interfaces are for per-feature column gating only. | Capability interfaces are also the single declaration of a stack capability (operations, securables, gates, default filters). | Reconciles the brain dump's "declared once" with the architecture's "no paired interface per entity": the interface is per capability, never per entity. |
| No shadow properties on mapped entities. | `Node` on tree entities is server-owned, absent from the wire and the table type; whether it is a CLR property or a configured shadow column is T2's call. | `Tellma.Core.Abstractions` is EF-free and cannot expose `HierarchyId`. |
| `SqlBuilder<T>` and raw `Sql(...)` as the tier-2 escape hatch. | Retained, but every raw statement declares its write set and an analyzer checks it; `DbContext.SaveChanges*` is forbidden in pack and distribution code. | Without this, a write path can bypass tag bumps and stamps silently. |

---

## 8. Verification

Facts relied on, with where they were verified (all 2026-09-01 unless noted):

- **RCSI semantics, optimized locking, write skew, `UPDLOCK` as the select-then-update fix, error
  2601/2627/3960 texts** — research file §6 (Microsoft Learn locking guide, table hints, optimized
  locking, errors 2000–2999 and 3000–3999).
- **SqlClient retry is inert under any transaction; `SqlBatch` has no `RetryLogicProvider`;
  baseline transient list; 7.0 exposes `BaselineTransientErrors`** — research §3.1–3.2 and the
  data-access research §3.2–3.3.
- **`TransactionScope` defaults and distributed-transaction escalation throwing off Windows** —
  research §2.3–2.4.
- **`THROW` requires error numbers ≥ 50 000, severity 16, honors `SET XACT_ABORT ON`** — verified
  directly at learn.microsoft.com/…/throw-transact-sql (page updated 2026-08-24).
- **`sp_getapplock` transaction-owned locks release at commit/rollback, `@Resource` ≤ 255 chars,
  available on Azure SQL Database** — verified directly (page dated 2026-06-19). Not used by the
  pipeline's common path; recorded because the tree recompute (T2) may prefer it to row locks.
- **BCL `Validator.TryValidateObject` runs property attributes, then type-level attributes, then
  `IValidatableObject` only if no earlier error** — verified in dotnet/runtime `Validator.cs`
  (`GetObjectValidationErrors`: "We only proceed to Step 2 if there are no errors", "We only
  proceed to Step 3 if there are no errors"); the docs page (ms.date 2025-07-01) states
  `validateAllProperties=false` checks only `[Required]`. This is why the pipeline owns the
  attribute walk and never uses `IValidatableObject`.
- **.NET 10 minimal-API validation is a per-endpoint filter with early returns, PascalCase keys,
  `Lines[3].Quantity` grammar; `IAsyncValidatableObject` is .NET 11** — research §1.1.
- **FluentValidation 12.1.1 has no batching; GreenDonut batches per loader** — research §1.2, §4.2.
- **Facebook DataLoader contract (coalesce, batch function answers every key, per-request cache,
  manual dispatch)** — research §4.1.
- **`MERGE` is out (temporal targets, DELETE action under indexed views)** — data-access research
  §4; separate statements are assumed throughout.
- **Temporal tables write a history row on every UPDATE; period columns are shadow properties in
  EF 10; `OUTPUT` of period columns is legal** — data-access research §2.
- **`hierarchyid` cannot ride a TVP without `Microsoft.SqlServer.Types`; `Level` as computed
  `GetLevel()`** — data-access research §1 and the briefing digest.
- **`ancestorOf`/`descendantOf` semantics, `contains`/`startsWith` emission without pattern
  metacharacters, `in` list bound by `MaxListItems = 128`, parameter naming `@qx{b}_p{n}`,
  `FilterTree.Or([])` is false** — spec 0008 §2.3, §9.4, §10.9, §10.10, §13.1, §15.
- **`Validate` mints the language-version stamp; caches keyed on schema identity** — spec 0008
  §16–17.
- **Queryex string predicates on an indexed column scan (`startsWith` emits `CHARINDEX`-style)** —
  spec 0008 §10.9; accepted for `Search`, with prefix search over large tables named as a
  dedicated-path concern.
- **OpenTelemetry: `IMeterFactory` meters named after the package, instrument names as `const`s,
  no tenant tags; SqlClient instrumentation emits stable conventions; per-request round-trip
  counting is the executor's** — research §5, data-access research §9, ARCHITECTURE.md
  Observability.
- **Identity invite API statuses and the tenant-side state machine** — T4 research §5 (relevant
  only to `UserService`, T8).

Unverified or left to implementation:

- Whether `SqlBatch.Execute*` raises the diagnostic-listener events — irrelevant because
  concatenated text through `SqlCommand` is chosen.
- The exact lock footprint of the tree recompute statement for the ancestors whose counts change
  (T2 decides between row `UPDLOCK`s on the affected roots and an `sp_getapplock` per table).
- The parking scheduler's behavior when a validator awaits a non-promise for a long time is
  "dispatch waits"; whether to add a watchdog metric beyond `tellma.crud.operation.duration` is a
  tuning question.
- The ScriptDom-based write-set analyzer's coverage of dynamically composed raw SQL (mitigated by
  steering distributions to `SqlBuilder<T>`).
- Whether `Properties<Enum>().HaveConversion<string>()` matches nullable enums (T2 confirms with a
  test); the pipeline's enum-member validation does not depend on it.
