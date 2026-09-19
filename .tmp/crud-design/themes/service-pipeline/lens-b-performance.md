# CRUD service pipeline and capabilities — design proposal (Tier-2 performance and operations)

Theme key `service-pipeline`, future spec 0014. Every decision below is optimised first for round trips,
bulk shapes, plan-cache stability, lock duration, cache correctness across instances, and day-one
observability; where those conflict with author convenience or with the brain dump's draft, the
conflict is named and resolved in favour of the database.

Vocabulary used throughout (final-looking, to be reconciled by the synthesizer): tables are plural and
schema-qualified (`core.Users`, `core.Roles`, `core.Permissions`, `gl.Centers`); the four audit columns
are `CreatedAt`, `CreatedById`, `ModifiedAt`, `ModifiedById`; cache stamps are called **tags**
(`PermissionsTag`, `SettingsTag`); a **securable** is the `(resource, action)` pair a permission grants;
a **stack** is one entity's CRUD feature; a **round trip** is one `ExecuteReaderAsync` on one connection.

---

## 1. Critique

### 1.1 The general design

The brain dump's service layer is sound in its instincts — bulk everywhere, one network call per user
action, plumbing in the platform — but it under-counts database round trips and over-specifies the
transaction. Counted as written, a save costs **four** round trips before any side effect
(connect, RLS pre-check, validation context, persist), and the transaction is opened before validation
("Start the transaction (is this right?)" — it is not). Under read-committed snapshot isolation, which
is the default on Azure SQL and which the migrator must switch on for on-premise databases, a
transaction opened before the validation reads protects nothing: every read sees the latest committed
version at statement start regardless of the enclosing transaction, so the only effect of the early
`BEGIN TRAN` is to hold the transaction — and, on Azure, the transaction's row-version bookkeeping —
open across two or three client round trips. The correct shape is: no transaction at all until the
persist batch, and the persist batch is one round trip that opens, guards, writes, checks, and commits
inside its own T-SQL.

The second structural gap is that the draft treats "OnConnect" as a separate call on every request.
It does not need to be one. The connect facts (does this subject map to an active user; what are the
tenant's and the user's tags) are one small `SELECT` that can lead any batch, and the tags can be
*asserted* in T-SQL against the values the instance cached — so a request whose cached permissions are
stale never executes its business statements at all. That turns the common read into **one** round
trip and the common update into **two**, with a cold-start and a stale-cache path that cost one more.

The third gap is validation. "Only one DB call loads the validation context, but sometimes a second is
needed" is right, but the draft has no mechanism for it. A DataLoader-shaped context loader over the
batch builder — validators `await` typed requests, the pipeline dispatches all outstanding requests as
one round trip, continuations run, and the loop repeats a bounded number of times — gives distro
validators an ordinary async API while keeping the round-trip count independent of entity cardinality
and of validator count. Neither FluentValidation nor the .NET 10 minimal-API validator can do this
(both are per-instance and I/O-agnostic), so the platform owns the engine and reuses only the
DataAnnotations attributes and the `Lines[3].Quantity` path grammar.

### 1.2 The detailed choices

- **`MERGE`.** The draft says "UPSERT". `MERGE` is out (two surviving defects on temporal targets and
  under indexed views, and `NEXT VALUE FOR` is banned inside it). Separate `UPDATE` / `INSERT` /
  `DELETE` statements per table, in a fixed table order, are also better for plan stability and
  blocking. Because ids are app-assigned there is no upsert race on the key and no `HOLDLOCK`.
- **Concurrency.** The draft asks for an alternative to `rowversion`. `ModifiedAt` as a `datetime2(7)`
  stamp is the right alternative *if* two rules hold: only user-visible mutations bump it (save,
  activate/deactivate, other actions), and bookkeeping never touches the row (activity, tags, inbox
  counters live in sibling non-temporal tables; the tree recompute writes `Node`/`SubtreeCount` but
  never `ModifiedAt`). One refinement the hint misses: the new stamp must be **strictly greater** than
  the expected stamp per row, computed in C# as `max(UtcNow, expected + 1 tick)`, so a clock step
  backwards on one instance cannot re-issue a stamp another instance already wrote.
- **"Pre-commit non-transactional side effects (creating blobs)".** This category should not exist in
  the pipeline. With staged uploads (T7) the blob is written before the save request, the save confirms
  a token inside the transaction, and deletes happen after commit. The pipeline then has exactly three
  side-effect phases: in-batch (transactional), post-commit (best effort), and background (durable).
- **Search.** "Should we keep the search parameter?" Yes, and the picker-versus-page hint is
  unnecessary. What the draft does not say is that `contains`-style search scans regardless of how it
  is expressed (Queryex emits `CHARINDEX`, `LIKE '%x%'` scans too), so nothing is lost by routing it
  through Queryex over declared searchable columns, and everything is gained in plan-cache stability
  (one parameterised filter text per entity).
- **GetByIdForDetails: "Queryex or raw SQL?"** Both, by role: user-authored expressions (the row-echo
  `Select`, the RLS filter) compile through Queryex; keyed reads (entity by id, children by parent id,
  related rows by id set) are emitted from the EF model as plain parameterised SQL. Mixing them in one
  batch is what `BatchOrdinal` exists for.
- **Ancestors for the tree view.** Spec 0008 documents the two-query shape ("query the page, then
  compose a second query with one key per row"), which is two round trips and bounded by
  `MaxParameters` (512). The single-trip shape needs one engine amendment: a **list restriction**
  (`property IN (SELECT Id FROM @host_table)`) so the page's ids, captured into a table variable in the
  same batch, can restrict both the page's own select and the ancestor query.
- **Count "stops counting beyond 9999".** As written this would be `count()` over the whole filtered
  set. The cheap form is `SELECT COUNT(*) FROM (SELECT TOP (@cap) 1 FROM (<filter query>) q) c`, which
  stops scanning at the cap.
- **Extensibility ("service logic should rely on interfaces rather than the concrete entity").** The
  architecture already rejected a paired interface per entity, and the right reconciliation is
  generics: pack hooks are declared against the pack's entity type (`ICrudValidator<User>`), and the
  pipeline for a distro leaf `MyUser : User` resolves hooks along the leaf's base chain. No interface per
  entity, no reflection on properties, and the pack's logic applies to every fork by construction.
- **"If the user requests 5 ids and 4 are found."** Return four. Bulk operations return the readable
  subset; only the single-entity details operation returns 404. Inaccessible and non-existent are
  indistinguishable in both.
- **Id ranges "hitching a ride on OnConnect".** Correct instinct, wrong vehicle: the reservation rides
  the *validation* round trip, sized from the payload (the exact number of rows with no id) plus the
  refill, so a save never pays a dedicated round trip for ids. Un-consuming on failure is worth doing
  only in the trivial case (the block is still adjacent to the buffer's low end).
- **Import through the same pipeline.** Right, with one performance consequence the draft does not
  mention: the persist statements' text is identical for a one-row save and a fifty-thousand-row
  import, and SQL Server 2019+ compiles a table-variable statement on first execution with that
  execution's row count and then caches the plan. The pipeline must therefore run large batches in a
  separate plan lane (`OPTION (RECOMPILE)`) or a 1-row plan will be reused for a 50k-row import, or
  worse.

### 1.3 Gaps and inconsistencies

- No round-trip budget per operation, and no instrument to hold the platform to it. Both are defined
  below (§2 D2, §3 telemetry) and asserted in tests with `SqlConnection.RetrieveStatistics()`.
- "Access control: accessing a record I cannot read returns the same response as a non-existent
  record" versus "If RLS fails on save, return ForbiddenException": consistent once the *pre*-check is
  folded into the existing-rows load (an update of an invisible row is a 404, like reading it) and only
  the *post*-check (the row became invisible *because of* this save) is a 403.
- Retry is mentioned ("MayRetry") but SqlClient's retry is inert for any command with a transaction
  attached and `SqlBatch` has no retry provider at all; the executor owns retry, whole-batch, with an
  eligibility rule and a verify probe for the ambiguous-commit case.
- The draft's Save takes one entity while the guiding principle says arrays. The service takes an array
  always; the web surface (T6) sends an array of one.
- Missing entirely: the concurrency override's interaction with `ModifiedAt`, uniqueness under RCSI
  (a C# check is write-skew-prone; the unique index is the guarantee and 2601/2627 must map to field
  errors), lock escalation for imports, and the overlap between "validation context" and the RLS
  pre-check (they are the same statement).

---

## 2. Decisions

### D1 — Composition, not inheritance: a sealed Core pipeline plus hook interfaces resolved along the leaf's base chain

**Decision.** The runtime service is one sealed generic class in `Tellma.Core`,
`CrudPipeline<TEntity>`, implementing `ICrudService<TEntity>` (Abstractions). A pack or distribution
never subclasses it. An entity's stack is declared once by a `CrudStack<TEntity>` descriptor
(Abstractions: securable resource, searchable columns, natural key, details expansion, limits) and
extended by small hook implementations registered in DI:

| Hook | Runs | Purpose |
|---|---|---|
| `ICrudPreprocessor<TEntity>` | before validation, in memory | trimming beyond the platform default, derived fields, defaults |
| `ICrudValidator<TEntity>` | validation rounds | rules over the payload and loaded context |
| `ICrudSideEffect<TEntity>` | in-batch / post-commit | extra statements riding the persist batch; best-effort work after commit |
| `ICrudDetailsContributor<TEntity>` | details/read-back batch | extras that ride the same round trip |

The pipeline for a leaf `MyUser : User` resolves every hook registered for `MyUser`, `User`, and each
further base up to (excluding) `object`, once per stack at startup, and treats them as one ordered
list. Hook interfaces are declared contravariant (`in TEntity`) so an `ICrudValidator<User>` is
assignable to `ICrudValidator<MyUser>` without adapters.

**Rationale.** `Tellma.Module.<m>` may not reference `Tellma.Core`, so a base class with pipeline logic
cannot live where modules could inherit it; the only Core surface a module sees is Abstractions, which
must stay framework-free. Composition keeps the round-trip discipline in one place (the pipeline)
where an inherited `override` could not accidentally add a query per entity. Base-chain resolution is
what lets a distribution extend `User` and inherit `UserService`'s rules with zero lines.

**Alternatives rejected.** An abstract `CrudServiceBase<T>` in Abstractions (drags implementation into
the contract package; every override is a place to add an N+1). A paired interface per entity
(rejected by the architecture; restates the property list). Per-leaf hook registration only (a fork
would silently lose the pack's validators).

**Confidence:** high. **Review flag:** whether `CrudStack<TEntity>` is itself the T1 feature
declaration or is wrapped by one; this proposal makes it the feature's *declaration* and lets the T1
composition contribute it.

### D2 — The round-trip budget is part of the contract and is measured

**Decision.** Every standard operation has a documented warm-path round-trip count, enforced by an
integration test that reads `ServerRoundtrips` from `SqlConnection.RetrieveStatistics()` on LocalDB,
and observed in production by `tellma.crud.roundtrips` (§3).

| Operation | Warm path | When it costs one more |
|---|---|---|
| Query (with count, with ancestors) | **1** | cold instance cache for this user (connect trip), stale permissions or settings tag (reload + rerun) |
| Details | **1** | same |
| Save — create only, no validator loads context, id buffer warm | **1** | id buffer refill (rides trip 1 → 2) |
| Save — update, or any validator loads context | **2** | a second validation round (bounded at 3 rounds → 4 trips, metered) |
| Delete by ids | **1** | a delete validator loads context (2) |
| Delete by query | **1** | — |
| Activate / deactivate | **1** | — |
| Get by parent ids | **1** | — |
| Delete with descendants | **1** | — |
| Import (N rows, natural-key translation) | **2** | as for save |
| Any operation, after a transient failure | +1 per retry (max 3 attempts) | — |

**Rationale.** A budget that is not measured drifts. The counts follow from D3 (connect collapse), D4
(one persist batch), D6 (loader rounds), and D10 (single-trip details).

**Confidence:** high.

### D3 — The connect step rides the first business batch, asserted in T-SQL against cached tags

**Decision.** Each instance keeps a bounded cache keyed `(tenantId, subject)` →
`ConnectSnapshot { UserId, PermissionsTag, UserSettingsTag, SettingsTag, SecurablesTag, LookupsTag }`
plus the derived permission set and Queryex schema. Every batch begins with the **connect guard**
emitted by T4's `IConnectStatementSource`:

```sql
-- prologue of every batch; parameters bound from the instance cache
SET NOCOUNT ON;
IF NOT EXISTS (SELECT 1 FROM [core].[Users] WHERE [Id] = @tx_userId AND [IsActive] = 1)
    THROW 50403, N'user-inactive', 1;
IF NOT EXISTS (SELECT 1 FROM [core].[UserActivities]
               WHERE [UserId] = @tx_userId AND [PermissionsTag] = @tx_permissionsTag)
    THROW 50412, N'permissions', 1;
IF NOT EXISTS (SELECT 1 FROM [core].[TenantTags] WHERE [SettingsTag] = @tx_settingsTag)
    THROW 50412, N'settings', 1;
-- activity stamp, at most once per minute per user, never on a temporal table
UPDATE [core].[UserActivities] SET [LastActive] = SYSUTCDATETIME()
WHERE [UserId] = @tx_userId
  AND ([LastActive] IS NULL OR [LastActive] < DATEADD(minute, -1, SYSUTCDATETIME()));
```

The guard compares only the tags the batch's statements were *compiled against*: permissions (RLS
filter text, securable decisions) and settings (the Queryex schema's language gating, calendar); a
change of `UserSettingsTag` or `LookupsTag` is read back at the end of the batch
(`SELECT ... FROM [core].[UserActivities] ... CROSS JOIN [core].[TenantTags]`) and only refreshes
caches. On error 50412 the pipeline runs the dedicated **connect trip** (subject → user, all tags,
permission rows, settings) and re-executes the operation once; a second 50412 surfaces as a 503-class
`StaleContextException`. On 50403 the request ends as forbidden and the cache entry is evicted.

With no cache entry (first request on this instance for this subject) the connect trip runs first;
the operation then runs with the fresh snapshot. The `LastActive` write is a single-row update on a
non-temporal sibling table and is skipped in-process when the instance wrote it within the last
minute.

**Rationale.** One round trip for every warm read, with correctness independent of instance count:
tags are read from the database on every request, so a permission or settings change on any
instance is observed by every other instance on its very next batch. Asserting in T-SQL rather than
returning tags and comparing in C# avoids executing (and paying for) statements whose results would
be discarded, and — decisive for writes — makes it impossible for a persist batch compiled against
stale permissions to commit. Failure modes are explicit: deactivated user → 50403 before any
business statement runs; stale permissions → 50412 before any business statement runs; RLS
pre-check ordering is moot because the pre-check is the existing-rows load of the same batch (D5)
and the persist batch re-asserts the tags inside its transaction (D4).

**Alternatives rejected.** A separate connect call per request (the draft: +1 trip on every request).
Returning tags and comparing in C# (wasted execution on the stale path; a stale-permission write
could commit). `SESSION_CONTEXT`-based checks (a stateful connection pattern that fights pooling and
buys nothing here).

**Confidence:** high. **Review flag:** the one-minute `LastActive` throttle (a coarser stamp trades
precision for one write per user per minute; the UI's "last active" column does not need seconds).

### D4 — One persist round trip: T-SQL transaction control, guards, separate DML, checks, commit, read-back

**Decision.** The persist batch is one command whose text is assembled by the pipeline from T2's
save emitter and executed with no client-side `SqlTransaction`. Statement order, shown for
`core.Roles` with its `core.Permissions` children (real column lists per T4's schema; row-image TVPs
`@roles : [core].[RolesList_<hash>]`, `@permissions : [core].[PermissionsList_<hash>]`, guard TVP
`@stamps : [StampList]`, `@syncedRoles : [IdList]`, `@savedIds : [IdList]`):

```sql
SET NOCOUNT ON; SET XACT_ABORT ON;
BEGIN TRAN;

-- 1. connect guard (D3), inside the transaction so a stale write can never commit
IF NOT EXISTS (SELECT 1 FROM [core].[Users] WHERE [Id] = @tx_userId AND [IsActive] = 1) THROW 50403, N'user-inactive', 1;
IF NOT EXISTS (SELECT 1 FROM [core].[UserActivities] WHERE [UserId] = @tx_userId AND [PermissionsTag] = @tx_permissionsTag) THROW 50412, N'permissions', 1;
IF NOT EXISTS (SELECT 1 FROM [core].[TenantTags] WHERE [SettingsTag] = @tx_settingsTag) THROW 50412, N'settings', 1;

-- 2. existence and concurrency, rows U-locked until commit (no S→X conversion deadlock)
DECLARE @missing nvarchar(2048) = (
    SELECT STRING_AGG(CAST(s.[Id] AS nvarchar(11)), ',')
    FROM @stamps s WHERE NOT EXISTS (SELECT 1 FROM [core].[Roles] t WITH (UPDLOCK, ROWLOCK) WHERE t.[Id] = s.[Id]));
IF @missing IS NOT NULL THROW 50404, @missing, 1;
IF @overrideConcurrency = 0
BEGIN
    DECLARE @conflicts nvarchar(2048) = (
        SELECT STRING_AGG(CAST(s.[Id] AS nvarchar(11)), ',')
        FROM @stamps s JOIN [core].[Roles] t WITH (UPDLOCK, ROWLOCK) ON t.[Id] = s.[Id]
        WHERE t.[ModifiedAt] <> s.[Stamp]);
    IF @conflicts IS NOT NULL THROW 50409, @conflicts, 1;
END;

-- 3. top-level UPDATE; unchanged rows are skipped (no history row, no stamp bump)
UPDATE t SET t.[Name] = s.[Name], t.[Name2] = s.[Name2], t.[Name3] = s.[Name3], t.[Code] = s.[Code],
             t.[IsActive] = s.[IsActive], t.[ModifiedAt] = s.[ModifiedAt], t.[ModifiedById] = @tx_userId
FROM [core].[Roles] t JOIN @roles s ON t.[Id] = s.[Id]
WHERE EXISTS (SELECT s.[Name], s.[Name2], s.[Name3], s.[Code], s.[IsActive]
              EXCEPT SELECT t.[Name], t.[Name2], t.[Name3], t.[Code], t.[IsActive]);

-- 4. top-level INSERT (ids are app-assigned; a retry after an ambiguous commit collides on the PK, never duplicates)
INSERT [core].[Roles] ([Id], [Name], [Name2], [Name3], [Code], [IsActive], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
SELECT s.[Id], s.[Name], s.[Name2], s.[Name3], s.[Code], s.[IsActive], s.[CreatedAt], @tx_userId, s.[ModifiedAt], @tx_userId
FROM @roles s WHERE NOT EXISTS (SELECT 1 FROM @stamps st WHERE st.[Id] = s.[Id]);

-- 5. child synchronisation, only under parents whose collection was present in the payload
DELETE c FROM [core].[Permissions] c
WHERE c.[RoleId] IN (SELECT [Id] FROM @syncedRoles)
  AND NOT EXISTS (SELECT 1 FROM @permissions s WHERE s.[Id] = c.[Id]);
UPDATE c SET c.[Resource] = s.[Resource], c.[Action] = s.[Action], c.[Filter] = s.[Filter],
             c.[FilterLanguageVersion] = s.[FilterLanguageVersion], c.[Notes] = s.[Notes]
FROM [core].[Permissions] c JOIN @permissions s ON c.[Id] = s.[Id]
WHERE EXISTS (SELECT s.[Resource], s.[Action], s.[Filter], s.[FilterLanguageVersion], s.[Notes]
              EXCEPT SELECT c.[Resource], c.[Action], c.[Filter], c.[FilterLanguageVersion], c.[Notes]);
INSERT [core].[Permissions] ([Id], [RoleId], [Resource], [Action], [Filter], [FilterLanguageVersion], [Notes])
SELECT s.[Id], s.[RoleId], s.[Resource], s.[Action], s.[Filter], s.[FilterLanguageVersion], s.[Notes]
FROM @permissions s WHERE NOT EXISTS (SELECT 1 FROM [core].[Permissions] c WHERE c.[Id] = s.[Id]);

-- 6. capability statements (tree recompute for tree stacks — see D8; none for Role)
-- 7. tag bumps: table-registered (T3) and stack-declared (here: every member of a saved role)
UPDATE ua SET ua.[PermissionsTag] = NEWID()
FROM [core].[UserActivities] ua
WHERE ua.[UserId] IN (SELECT [UserId] FROM [core].[RoleMemberships] WHERE [RoleId] IN (SELECT [Id] FROM @savedIds));
UPDATE [core].[TenantTags] SET [SecurablesTag] = [SecurablesTag] WHERE 1 = 0;   -- emitted only when the registry maps the table
-- 8. notifications riding the batch (T10 shape), blob token confirmation (T7 shape)
INSERT [core].[InboxItems] (...) SELECT ... FROM @inbox;
UPDATE [core].[Blobs] SET [State] = 1 WHERE [Token] IN (SELECT [Id] FROM @blobTokens) AND [State] = 0;

-- 9. RLS post-check: every saved row must still satisfy the caller's Save filter
DECLARE @visible int = (SELECT COUNT(*) FROM (
    <Queryex: Root = Role, Select = "Id", Filter = <Save filter tree>, Restriction = Id IN @savedIds> ) q);
IF @visible <> @savedCount THROW 50403, N'rls', 1;

-- 10. read-back inside the transaction: the saved rows exactly as committed
<details statements of D10 restricted to @savedIds: entity, children, related, row echo>

COMMIT;

-- 11. extras after commit (informational; never extends lock duration)
<ICrudDetailsContributor statements>
```

Rules the emitter and pipeline enforce:

- **Table order is deterministic** (parents before children on insert/update, children before parents
  on delete, tables in model order) so concurrent saves lock in the same order and deadlocks are
  reduced to the retryable minimum.
- **`@stamps` carries `(Id, Stamp)` only for rows that exist**; the pipeline knows which rows are
  updates because the validation trip loaded them (D5). Inserts are "not in `@stamps`".
- **The new `ModifiedAt` is computed in C#** as `max(UtcNow, expected + 1 tick)` per updated row and
  `UtcNow` for inserts, bound in the row-image TVP. `SYSUTCDATETIME()` is not used for the stamp
  (it would make the ambiguous-commit probe of D11 inexact).
- **Unchanged rows are skipped** by the `EXCEPT` predicate (null-safe, one comparison per row): no
  temporal history row, no `ModifiedAt` bump, no spurious concurrency conflict for a re-save.
- **The transaction spans exactly one round trip.** No client transaction object; `XACT_ABORT ON`
  makes every runtime error (including 2601/2627 and every `THROW`) roll back the whole batch.
- **Platform errors are `THROW`n with reserved numbers** (`50403` forbidden, `50404` missing,
  `50409` concurrency, `50412` stale context, `50413` limit exceeded, `50423` locked-by-lease) and
  the executor maps them to the platform exceptions of §3 before any other handling.
- **Read-back sits inside the transaction** for entity, children, related rows and row echo (keyed
  reads, milliseconds); **extras run after `COMMIT`**.

**Rationale.** Lock duration equals one round trip. Retry eligibility stays simple (D11). The guard
inside the transaction is what makes the optimistic connect collapse safe for writes. The `UPDLOCK`
on the concurrency check is the documented select-then-update pattern that avoids the S→X
conversion deadlock; it disables lock-after-qualification on that one statement only.

**Alternatives rejected.** `SqlTransaction` from the client (an extra `BEGIN`/`COMMIT` exchange
each, driver retry disabled, and the read-back would need the same connection anyway).
`TransactionScope` (Serializable by default, async flow off, a stray second connection escalates to
DTC which throws on Linux). `MERGE` (§1.2). Compare `@@ROWCOUNT` instead of a pre-check (loses the
conflicting ids).

**Confidence:** high. **Review flag:** read-back inside versus after the transaction (inside returns
exactly what was committed; after shortens the lock by the read-back's few milliseconds).

### D5 — The validation trip: existing rows through the RLS filter, id reservation, natural-key translation, and validator context in one round trip

**Decision.** Trip 1 of a save is assembled as:

1. connect guard (D3);
2. **existing rows**: for every top-level id in the payload that is non-zero, the current row and
   its child collections, loaded *through the caller's Save filter*
   (`Queryex: Select = "Id", Filter = <Save filter>, Restriction = Id IN @ids` captured into a table
   variable, then model-emitted keyed selects joined to it). An id absent from the result is either
   non-existent or invisible; both are reported as `NotFoundException` — the RLS pre-check and the
   existing-rows load are one statement;
3. **id reservation** for each table whose buffer cannot cover the payload's new rows
   (`EXEC sys.sp_sequence_get_range` with `OUTPUT` parameters), sized as `needed + refill`;
4. **natural-key translation** for import (T9): `SELECT s.[Id] AS RowKey, t.[Id] FROM @keys s JOIN
   [core].[Roles] t ON t.[Code] = s.[Value]` per referenced entity, ambiguity and not-found reported
   as validation errors by row;
5. every **context request** declared by validators in round 1 (D6);
6. tags read-back.

Trip 1 is skipped entirely when the payload has no updates, no natural keys, the id buffer is warm,
and no validator declares a request — the common "create one record" then costs one round trip.

**Rationale.** Everything a save needs to know before writing is keyed by the payload and can be
fetched in one command; loading existing rows through the filter gives the pre-check for free and
gives validators the "before" image (write-once columns, ownership of children, tree cycle checks)
without a second statement.

**Confidence:** high.

### D6 — Validation is a bounded DataLoader over the batch: validators `await` typed requests, the pipeline dispatches rounds

**Decision.** `ICrudValidator<TEntity>.ValidateAsync(CrudValidationScope<TEntity> scope, CancellationToken)`
receives a scope exposing the payload (with indices), the existing rows, the request context, an
error sink keyed by path, and a `ContextLoader`. A validator obtains context only through the loader:

```csharp
// A distribution validator: unique code across the batch and the database, in one round trip
public sealed class CenterCodeValidator : ICrudValidator<Center>
{
    public async ValueTask ValidateAsync(CrudValidationScope<Center> scope, CancellationToken ct)
    {
        var byCode = scope.Entities.Where(e => e.Value.Code is not null).ToLookup(e => e.Value.Code!);
        foreach (var dup in byCode.Where(g => g.Count() > 1))
            foreach (var e in dup) scope.Errors.Add(e.Path("Code"), CrudErrorCodes.Duplicate, ("value", dup.Key));

        // one keyed lookup for every code in the payload; deduplicated with any other validator asking for the same
        var existing = await scope.Load(ContextRequests.ByKey<Center, string>(c => c.Code, byCode.Select(g => g.Key)), ct);
        foreach (var e in scope.Entities)
            if (e.Value.Code is { } code && existing.TryGetValue(code, out var other) && other.Id != e.Value.Id)
                scope.Errors.Add(e.Path("Code"), CrudErrorCodes.AlreadyExists, ("value", code));
    }
}
```

Mechanics: the pipeline starts every validator's `ValidateAsync`; each runs synchronously until its
first `await` on an unresolved loader promise; the pipeline then dispatches **all** outstanding
requests as statements of one batch (deduplicated by structural key; two `Query` requests that
differ only in `Select` are merged into one statement selecting the union of paths), resolves the
promises with continuations run inline, and repeats while any request is outstanding. Rounds are
bounded (`CrudStack.MaxValidationRounds`, default 3); the round count is recorded
(`tellma.crud.validation.rounds`); a validator task that is still incomplete after a dispatch that
resolved every promise and produced no new request has awaited something other than the loader and
fails the request with a platform `InvalidOperationException` (a bug, not user input). Requests
already answered in the round (the existing rows, `ByIds` of payload ids) are primed so they never
cost a statement.

Standard request kinds (Abstractions, `Tellma.Core.Abstractions.Crud.ContextRequests`):
`ByIds<T>(ids)`, `ByKey<T, TKey>(property, values)` (TVP-backed `IN`), `ByParentIds<T>(parentIds)`,
`Exists(root, filterTree, parameters)`, `Count(root, filterTree, parameters, cap)`,
`Query(root, select, filterTree, parameters)` (materialized rows keyed by path),
`Ancestors<T>(ids)` (tree stacks), `Raw(sql, parameters, reader)` (escape hatch; statement text must
avoid the `@qx` and `@tx` prefixes).

Shape rules that stay declarative run before any round: BCL `Validator.TryValidateObject(entity,
ctx, results, validateAllProperties: true)` per top-level entity **and per child** (the BCL method
does not recurse), producing the same path keys; `[Required]`, `[MaxLength]`, `[Range]`,
`[RegularExpression]` on the entity class are therefore the whole story for shape validation.
Uniqueness is validated in C# for the message and for in-batch duplicates only; the unique index is
the guarantee, and 2601/2627 from the persist batch are translated to the same field error
(`AlreadyExists` on the index's property path, row located by the duplicate value in the message).

**Rationale.** Round trips stay O(1) in entity count and validator count; validator code is ordinary
async C#; dedup is structural; the loader sits *on top of* T2's batch so every request becomes one
statement of one command. The DataLoader contract (batch function answers every key, missing ⇒
explicit absent, per-request memoisation, manual dispatch) is copied; neither GreenDonut nor
FluentValidation is referenced.

**Alternatives rejected.** FluentValidation (no batching; a second rule language). The .NET 10
minimal-API validator for entity payloads (shape-only, early return, no context; it stays enabled for
non-entity request DTOs on the web surface and is disabled on save endpoints). A two-phase
`Declare()/Validate()` interface (explicit rounds; more ceremony for authors, and no gain since the
await form detects misuse).

**Confidence:** medium-high. **Review flag:** the "validators may not await anything but the
loader" rule is enforced at run time, not compile time.

### D7 — Concurrency: `ModifiedAt` is the stamp, guard rows travel as a `StampList` TVP, override is a save option

**Decision.** `ModifiedAt datetime2(7) NOT NULL` on every top-level entity is the concurrency
token. The client echoes the value it loaded; the pipeline copies it into `StampList(Id, Stamp)`
for updated rows, computes the new stamp per row (D4), and the persist batch checks and writes as
shown. `SaveOptions.OverrideConcurrency = true` skips the check (the UI re-submits with it after the
"user X changed this record" prompt); the new stamp is still written. Children carry no stamp: a
child is protected by its parent's stamp because a save always sends the whole collection. Actions
(activate, deactivate) bump the stamp and take no expected stamp.

On 50409 the pipeline runs one follow-up query `SELECT [Id], [ModifiedAt], [ModifiedById] FROM
... WHERE [Id] IN @conflictIds` joined to `core.Users` for the display name, and raises
`ConcurrencyConflictException(conflicts)` so the UI can name the other user.

**Rationale versus `rowversion`.** `rowversion` bumps on any update of any column, including
bookkeeping and the tree recompute, cannot be selective, adds a read-back to learn the new value,
and its byte[] form is awkward on the wire. `ModifiedAt` is already a column the UI shows, costs no
extra column, is selective by construction (only user-visible mutations write it), and the strict
`expected + 1 tick` rule removes the only theoretical weakness (clock steps). Row versioning
handles the check correctly: the `UPDLOCK` read qualifies on the latest committed version.

**Alternatives rejected.** A hash of the editable columns (computed on every read, brittle across
schema changes). A `Version int` counter (a new column with no user meaning). `ValidFrom` of the
temporal period (bumps on bookkeeping unless the row is never bookkept; not present on non-temporal
entities).

**Confidence:** high. **Review flag:** whether actions should be stamp-checked too (this proposal:
no — an action is a command over ids, not an edit of a loaded copy).

### D8 — Capabilities are interfaces on the entity, discovered once, each contributing to every projection

**Decision.** A capability is a marker interface in Abstractions implemented by the entity class,
recognised by the pipeline at startup and expanded by a Core `CrudCapability` object into: EF
conventions (T2), securable actions (T4), pipeline operations, endpoint projections (T6), default
filters and UI hints published in the stack metadata, validators, and persist-batch statements.

| Capability | Entity declares | Columns (EF convention) | Securable actions | Operations added | Persist statements | Metadata |
|---|---|---|---|---|---|---|
| Activatable | `IActivatable { bool IsActive }` | `IsActive bit NOT NULL DEFAULT 1` | `Activate` (both directions; filter-capable) | `ActivateAsync(ids)`, `DeactivateAsync(ids)` | bump `ModifiedAt`; tree: `ActiveSubtreeCount` recompute | default filter `IsActive = true`; details banner |
| Tree | `ITreeEntity { int? ParentId; int SubtreeCount; int ActiveSubtreeCount }` | `Node hierarchyid NOT NULL` (Core-mapped, unique index), `Level AS Node.GetLevel()`, index on `ParentId` | — | `GetByParentIdsAsync`, `DeleteWithDescendantsAsync`, `IncludeAncestors` in query | node/count recompute (D9); cycle guard | tree view hints |
| Audited | `TopLevelEntity` base (`CreatedAt/ById`, `ModifiedAt/ById`) | four columns, FKs to `core.Users` | — | — | stamping (D4) | server-owned columns |
| Attachments | `IHasBlobs` | per T7 (`ImageId` etc.) | — | staged-token confirmation | `UPDATE core.Blobs ... State = Committed` | — |
| Multilingual | `IMultilingual { Name, Name2, Name3 }` | `Name nvarchar(255) NOT NULL`, `Name2/3 nvarchar(255) NULL` | — | — | — | searchable defaults; Queryex gating by tenant languages (T3) |

A distribution adding a tree entity with activation writes: the entity class implementing
`IActivatable, ITreeEntity`, and one `CrudStack<Center>` declaration (resource name, searchable
columns, natural key). Everything else is projected.

**Rationale.** Declared once, consumed by five specs, with no per-entity ceremony; interface
detection is a startup cost, not a request cost. Capability interfaces are precisely the
architecture's "per-feature column gating".

**Confidence:** high. **Review flag:** one securable action `Activate` for both directions versus
two (`Activate`, `Deactivate`); this proposal keeps one because the UI and the permission editor
treat them as one toggle.

### D9 — Tree maintenance is one appended recompute statement; cycles are validated in C# and guarded by `MAXRECURSION`

**Decision.** `Node` is never in the UDTT and never travels in the TVP. After the tree table's
DML, the pipeline appends (with `@affected : [IdList]` = saved ids ∪ their new parent ids ∪ their
old parent ids, computed in C# from the payload and the existing rows):

```sql
-- roots of every tree that contains an affected node
DECLARE @roots TABLE ([Id] int PRIMARY KEY);
WITH up AS (
    SELECT c.[Id], c.[ParentId] FROM [gl].[Centers] c WHERE c.[Id] IN (SELECT [Id] FROM @affected)
    UNION ALL
    SELECT p.[Id], p.[ParentId] FROM [gl].[Centers] p JOIN up ON up.[ParentId] = p.[Id])
INSERT @roots ([Id]) SELECT DISTINCT [Id] FROM up WHERE [ParentId] IS NULL
OPTION (MAXRECURSION 64);

-- deterministic paths from ParentId: siblings numbered by Id, so appends never renumber existing siblings
WITH numbered AS (
    SELECT c.[Id], c.[ParentId], ROW_NUMBER() OVER (PARTITION BY c.[ParentId] ORDER BY c.[Id]) AS n
    FROM [gl].[Centers] c),
paths AS (
    SELECT n.[Id], CAST('/' + CAST(n.n AS varchar(10)) + '/' AS varchar(892)) AS path
    FROM numbered n WHERE n.[ParentId] IS NULL AND n.[Id] IN (SELECT [Id] FROM @roots)
    UNION ALL
    SELECT n.[Id], CAST(p.path + CAST(n.n AS varchar(10)) + '/' AS varchar(892))
    FROM numbered n JOIN paths p ON n.[ParentId] = p.[Id])
UPDATE c SET c.[Node] = CAST(p.path AS hierarchyid)
FROM [gl].[Centers] c JOIN paths p ON p.[Id] = c.[Id]
WHERE c.[Node] IS NULL OR c.[Node] <> CAST(p.path AS hierarchyid)
OPTION (MAXRECURSION 64);

-- subtree counters, written only where they changed
UPDATE c SET c.[SubtreeCount] = x.cnt, c.[ActiveSubtreeCount] = x.active
FROM [gl].[Centers] c
CROSS APPLY (SELECT COUNT(*) AS cnt, SUM(CASE WHEN d.[IsActive] = 1 THEN 1 ELSE 0 END) AS active
             FROM [gl].[Centers] d WHERE d.[Node].IsDescendantOf(c.[Node]) = 1) x
WHERE EXISTS (SELECT 1 FROM @roots r JOIN [gl].[Centers] rr ON rr.[Id] = r.[Id] WHERE c.[Node].IsDescendantOf(rr.[Node]) = 1)
  AND (c.[SubtreeCount] <> x.cnt OR c.[ActiveSubtreeCount] <> x.active);
```

Neither statement touches `ModifiedAt`. Cycle validation runs in C# in round 1 over the payload
plus `ContextRequests.Ancestors<Center>(newParentIds)` (one statement using `IsDescendantOf` on the
depth-first index): walking up from each moved node through payload-overridden parents must not
reach the node itself, and the parent must exist and be of a type allowed to have children (T8's
rule). A concurrent move that slips past both validations (write skew) makes the recursive CTE
exceed `MAXRECURSION` → error 530 → rollback → mapped to `ConcurrencyConflictException`; the user
retries and validation now sees the cycle.

**Rationale.** One set-based pass per save, deterministic, no `GetDescendant` collisions, writes
only changed rows, no `Microsoft.SqlServer.Types` in the binder. `IsLeaf` and `Level` are derived,
not stored (`SubtreeCount = 1`, `Node.GetLevel()`).

**Confidence:** medium-high (the counters' `IsDescendantOf` self-apply versus a second recursive
CTE should be measured on LocalDB at a few thousand rows; either fits in the same statement slot).
**Review flag:** renumbering of later siblings when an earlier sibling is deleted (accepted churn on
master-data-sized trees) versus preserving existing `Node` values with `GetReparentedValue`.

### D10 — Details, query, and the tree view are single-trip scripts mixing Queryex and model-emitted keyed SQL

**Decision.** Query script (`QueryAsync`):

```sql
<connect guard>
DECLARE @page TABLE ([Id] int PRIMARY KEY, [Ord] int NOT NULL);
-- (a) the page's ids only: compiled Queryex with the user's Filter ∧ RLS, OrderBy, Skip/Take; Select = "Id"
<prologue>; INSERT @page ([Id], [Ord]) SELECT [Id], ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM (<statement>) q;
-- (b) the page's rows: compiled Queryex with the user's Select, Restriction = Id IN @page, no paging, ordered by Ord
-- (c) ancestors (IncludeAncestors on tree stacks): ids of ancestors of the page not in the page, then the same Select restricted to them
DECLARE @anc TABLE ([Id] int PRIMARY KEY);
INSERT @anc SELECT a.[Id] FROM [gl].[Centers] a
WHERE EXISTS (SELECT 1 FROM [gl].[Centers] p JOIN @page pg ON pg.[Id] = p.[Id] WHERE p.[Node].IsDescendantOf(a.[Node]) = 1)
  AND NOT EXISTS (SELECT 1 FROM @page pg WHERE pg.[Id] = a.[Id]);
<compiled Select restricted to @anc, RLS applied>
-- (d) capped count (IncludeCount): the filter query without ordering, wrapped
SELECT COUNT(*) FROM (SELECT TOP (@cap) 1 AS x FROM (<statement of Select="Id", no OrderBy/paging>) q) c;
<tags read-back>
```

The result is `QueryResult { Columns, Rows: object?[][], Ancestors: object?[][]?, TotalCount: int?,
CountCapped: bool }`. A query without `IncludeAncestors` and without `IncludeCount` skips (a)'s
indirection and runs the compiled page query directly (one statement).

Details script (`GetForDetailsAsync(id, DetailsRequest)`):

```sql
<connect guard>
DECLARE @main TABLE ([Id] int PRIMARY KEY);
INSERT @main <Queryex: Select "Id", Filter = And(Leaf("Id = @id"), <Read filter>)>;     -- RLS decides visibility
SELECT <all columns> FROM [core].[Roles] t WHERE t.[Id] IN (SELECT [Id] FROM @main);   -- model-emitted
SELECT <all columns> FROM [core].[Permissions] c WHERE c.[RoleId] IN (SELECT [Id] FROM @main);
-- related rows: one statement per target entity, display columns only, ids gathered from main + children; no RLS (readable-implies-readable)
SELECT r.[Id], r.[Name], r.[Name2], r.[Name3], r.[Code] FROM [core].[Users] r
WHERE r.[Id] IN (SELECT [CreatedById] FROM [core].[Roles] WHERE [Id] IN (SELECT [Id] FROM @main)
                 UNION SELECT [ModifiedById] FROM [core].[Roles] WHERE [Id] IN (SELECT [Id] FROM @main));
-- row echo: the search page's Select compiled with Restriction = Id IN @main
<compiled Select>
-- extras declared by ICrudDetailsContributor, only those named in DetailsRequest.Extras
<tags read-back>
```

Zero rows in `@main` → `NotFoundException` (missing and invisible are one answer). Get-by-parent-ids
uses the same shape with `Restriction = ParentId IN @parentIds` and returns the readable subset.
Get-by-ids (used by export and MCP) returns the readable subset with no 404.

**Rationale.** Keyed reads compile to seeks and need no expression compiler; the expression compiler
is used exactly where a user or a permission wrote text. The `@main`/`@page` indirection lets one
batch answer everything the page needs and keeps the RLS filter in a `FilterTree`, never
concatenated. Related rows are one statement per *entity type* (never per row) and skip RLS by
design (a readable document is wholly readable).

**Engine amendments required (documented by spec 0011):** (1) `QuerySpec.Restriction` — a
host-named list source (`@page`, `@savedIds`, a TVP) applied as `<property> IN (SELECT [Id] FROM
<source>)`, on the key or any scalar property, participating in cache keys; (2) `CompiledQuery`
exposing `Prologue` (the `DECLARE` block) and `Statement` (the `SELECT`) separately so the host can
`INSERT @t <Statement>` and wrap `SELECT COUNT(*) FROM (<Statement>)` without touching the text;
(3) `level()`.

**Confidence:** high for the shape; medium for (2), which is the kind of surface spec 0008's
"`CompileQuery` is the only door to SQL" rule frowns on — the split exposes no fragment SQL, only the
same statement in two pieces, so the injection boundary is unchanged.

### D11 — Retry, the ambiguous commit, and the plan-cache lanes belong to the executor, driven by statement metadata

**Decision.** Every `BatchStatement` (T2) declares `MayRetry`, `WritesTables`, and `Kind`
(`Read`, `Write`, `Transactional`). The executor retries a **whole batch** (never a statement) on
SqlClient's baseline transient error numbers plus 1205, 1222, 3960 and 530-as-cycle handling, with
jittered delays of roughly 50 ms, 200 ms, 800 ms (three attempts): always for read-only batches; for
a transactional batch only when the error arrived **before** the first read-back result set (i.e.
before `COMMIT`); when the connection drops after `COMMIT` may have been sent, it runs the **verify
probe** `SELECT COUNT(*) FROM <table> WHERE [Id] IN @savedIds AND [ModifiedAt] = <that row's new
stamp>` (a `StampList` bound with the *new* stamps) and treats an exact count as committed
(re-running only the post-commit read-back), otherwise re-executes the batch — a retry of the
inserts collides on the PK if the commit did happen, never duplicates. `tellma.crud.retries`
records every attempt beyond the first with the error number as a bounded tag.

Plan lanes: when any TVP in a batch carries more than `CrudStack.LargeBatchThreshold` rows
(default 1,000), the emitter appends `OPTION (RECOMPILE)` to every DML statement of that batch.
Below the threshold statements are cached normally.

**Rationale.** SqlClient's retry is inert with a transaction attached and absent from `SqlBatch`;
concatenated text on one `SqlCommand` is the mechanism anyway. SQL Server 2019+ compiles a
table-variable statement at first execution using that execution's row count and caches the plan;
without lanes a one-row save's plan serves a fifty-thousand-row import (or the reverse). A
`RECOMPILE` on a statement that moves thousands of rows costs milliseconds against seconds of work,
and `RECOMPILE`d plans are not cached, so the small lane is never polluted.

**Confidence:** high. **Review flag:** the threshold value; the alternative of a size-bucket comment
token (`/* lane:L */`) that yields a second cached plan per lane instead of recompiling.

### D12 — Server-side `Search` over declared columns, one filter text per entity

**Decision.** `EntityQuery.Search` is translated by the pipeline into
`FilterTree.Or([Leaf("contains(Name, @tx_search)"), Leaf("contains(Code, @tx_search)"), …])` over the
columns the stack declares (`CrudStack.SearchableColumns`; default: the multilingual `Name*` columns
gated by the tenant's languages, plus `Code`), conjoined with the user filter and the RLS tree. No
page-versus-picker hint. If the text parses as an integer and the stack declares an integer key or
code, `Id = @tx_searchInt` is added as one more disjunct.

**Rationale.** The filter *text* is constant per entity and the value is a parameter, so the plan
is cached once; `contains` is a scan whichever way it is spelled, and the picker's small `Take`
bounds the work. A dedicated seekable path (full-text or prefix) is a later feature on the same
declaration.

**Confidence:** high.

### D13 — One entity class on the wire: `[NotMapped]` child collections, server-owned columns overwritten, write-once columns enforced from the existing row

**Decision.** The save payload is the entity class itself (T2 owns the contract; this theme
consumes it): child collections are `[NotMapped] List<TChild>?` properties on the parent
(`null` = untouched, empty = delete all); server-owned properties (`CreatedAt`, `CreatedById`,
`ModifiedById`, `Node`, `SubtreeCount`, `ActiveSubtreeCount`, and any `[ServerOwned]`) are
overwritten by the pipeline from the existing row or the stamping rule before persistence;
`ModifiedAt` is read as the expected stamp and then overwritten; `[WriteOnce]` properties
(`Subject`, `Email`) are reset to the existing row's value and, if the client changed them, reported
as a validation error (`ReadOnlyAfterCreate`). There is no second UDTT for updates.

**Rationale.** One class, one JSON shape, one TVP; the platform, not the author, guarantees that
client-sent server-owned values never reach the table. Enforcing write-once from the loaded row
costs nothing (the row is loaded anyway, D5) and keeps the data layer dumb.

**Confidence:** high.

### D14 — Delete, activate, and delete-by-query are single-trip transactional scripts with the RLS check in the write itself

**Decision.** Delete by ids:

```sql
SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRAN;
<connect guard>
DECLARE @deletable TABLE ([Id] int PRIMARY KEY);
INSERT @deletable <Queryex: Select "Id", Filter = <Delete filter>, Restriction = Id IN @ids>;
IF (SELECT COUNT(*) FROM @deletable) <> @count THROW 50404, N'', 1;
DELETE c FROM [core].[Permissions] c WHERE c.[RoleId] IN (SELECT [Id] FROM @deletable);
DELETE t FROM [core].[Roles] t WHERE t.[Id] IN (SELECT [Id] FROM @deletable);
<tag bumps>
COMMIT;
```

A foreign-key violation (547) from another table maps to `EntityInUseException` (409) naming the
referencing entity resolved from the constraint name through the EF model. Delete validators
(lockout guards, T4) run before the script and may load context (then 2 trips). Delete with
descendants restricts to `Node.IsDescendantOf(...)` of the visible ids and deletes deepest-first
(`ORDER BY Level DESC` is unnecessary because children are deleted by the subtree predicate in one
statement per table). Activate/deactivate: `UPDATE t SET [IsActive] = @isActive, [ModifiedAt] =
@stamp, [ModifiedById] = @tx_userId WHERE [Id] IN (SELECT [Id] FROM @visible) AND [IsActive] <>
@isActive`, followed by the tree counter recompute where applicable. Delete by query: the compiled
filter's ids into a table variable, `IF (SELECT COUNT(*) FROM @ids) > @maxRows THROW 50413`, then
the delete statements — capped (`CrudStack.MaxDeleteByQueryRows`, default 10,000).

**Confidence:** high.

### D15 — Side effects have three homes, none of them "pre-commit non-transactional"

**Decision.** `ICrudSideEffect<TEntity>` has two members: `Contribute(CrudPersistScript<TEntity>)`
appends statements to the persist batch (transactional: inbox rows via T10's `InboxItemsList` TVP,
blob confirmations via T7's tokens, denormalised counters), and `AfterCommitAsync(CrudCommitted<TEntity>,
CancellationToken)` runs best-effort work (SignalR nudges, blob deletes, cache pushes) whose failure
is logged and metered but never fails the request. Anything that must eventually happen (external
calls, emails) is enqueued as a T10 task row *inside* the persist batch and handled in the
background. Blob writes precede the request (staged upload).

**Confidence:** high.

---

## 3. Contracts

Code blocks are normative for shape, not formatting. Owned by this theme unless marked "needed
from".

### 3.1 The service and its operations (`Tellma.Core.Abstractions.Crud`)

```csharp
namespace Tellma.Core.Abstractions.Crud;

/// <summary>
///     The standard operations every CRUD stack exposes, implemented once by the platform pipeline.
///     Every member is bulk-shaped; every member costs the round trips its documentation states.
/// </summary>
public interface ICrudService<TEntity> where TEntity : TopLevelEntity
{
    /// <summary>Runs a paged Queryex query under the caller's row-level security. One round trip.</summary>
    Task<QueryResult> QueryAsync(EntityQuery query, CancellationToken ct);

    /// <summary>Loads one entity with children, related rows, extras and the row echo. One round trip; 404 when absent or invisible.</summary>
    Task<DetailsResult<TEntity>> GetForDetailsAsync(int id, DetailsRequest request, CancellationToken ct);

    /// <summary>Loads the readable subset of the given ids as full entities with children; never 404.</summary>
    Task<IReadOnlyList<TEntity>> GetByIdsAsync(IReadOnlyList<int> ids, CancellationToken ct);

    /// <summary>Saves a batch (insert or update per element, children synchronised). One or two round trips; see <see cref="SaveOptions"/>.</summary>
    Task<SaveResult<TEntity>> SaveAsync(IReadOnlyList<TEntity> entities, SaveOptions options, CancellationToken ct);

    /// <summary>Deletes the given ids; every id must exist and be visible under the Delete filter. One round trip.</summary>
    Task DeleteByIdsAsync(IReadOnlyList<int> ids, CancellationToken ct);

    /// <summary>Deletes every row matching the filter, up to the stack's cap. One round trip.</summary>
    Task<int> DeleteByQueryAsync(FilterTree filter, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct);

    /// <summary>Runs a capability or stack-declared action (Activate, Deactivate, …) over ids. One round trip.</summary>
    Task<ActionResult> RunActionAsync(string action, IReadOnlyList<int> ids, CancellationToken ct);

    /// <summary>Tree stacks only: the readable children of the given parents (null for roots). One round trip.</summary>
    Task<IReadOnlyList<TEntity>> GetByParentIdsAsync(IReadOnlyList<int?> parentIds, CancellationToken ct);

    /// <summary>Tree stacks only: deletes the visible ids and their whole subtrees. One round trip.</summary>
    Task DeleteWithDescendantsAsync(IReadOnlyList<int> ids, CancellationToken ct);
}

/// <summary>A paged query in Queryex terms; the wire shape is spec 0015's, this is the service shape.</summary>
public sealed record EntityQuery
{
    public required string Select { get; init; }
    public string? Search { get; init; }
    public FilterTree? Filter { get; init; }
    public bool Aggregate { get; init; }
    public FilterTree? Having { get; init; }
    public string? OrderBy { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 50;
    public bool IncludeCount { get; init; }
    public bool IncludeAncestors { get; init; }
    public IReadOnlyDictionary<string, object?> Arguments { get; init; } = new Dictionary<string, object?>();
}

/// <summary>Rows as positional arrays; ancestors are separate so the UI can style them.</summary>
public sealed record QueryResult(
    IReadOnlyList<QueryColumn> Columns,
    IReadOnlyList<object?[]> Rows,
    IReadOnlyList<object?[]>? Ancestors,
    int? TotalCount,
    bool CountCapped);

/// <summary>Column metadata the client renders from (name, Queryex type, path when a bare path).</summary>
public sealed record QueryColumn(string Text, string Type, IReadOnlyList<string>? Path);

public sealed record DetailsRequest
{
    /// <summary>The search page's Select, for the row echo; null skips the echo.</summary>
    public string? Select { get; init; }
    /// <summary>Names of extras to load (declared by contributors); empty loads none.</summary>
    public IReadOnlySet<string> Extras { get; init; } = new HashSet<string>();
}

/// <summary>Everything a details page renders, from one round trip.</summary>
public sealed record DetailsResult<TEntity>(
    TEntity Entity,
    /// <summary>Related rows keyed by entity name, each a partial entity carrying display columns only.</summary>
    IReadOnlyDictionary<string, IReadOnlyList<object>> Related,
    IReadOnlyDictionary<string, object> Extras,
    object?[]? RowEcho);

public sealed record SaveOptions
{
    /// <summary>Skip the ModifiedAt check; the UI sets it after the conflict prompt.</summary>
    public bool OverrideConcurrency { get; init; }
    /// <summary>Return the saved entities in details shape (the UI) or ids only (import).</summary>
    public bool ReturnEntities { get; init; } = true;
    /// <summary>The row-echo Select and extras for the read-back, when returning entities.</summary>
    public DetailsRequest? Details { get; init; }
    /// <summary>Import only: the natural-key column used to match existing rows and the merge mode.</summary>
    public ImportMatch? Import { get; init; }
}

public sealed record SaveResult<TEntity>(IReadOnlyList<int> Ids, IReadOnlyList<DetailsResult<TEntity>>? Entities);

public sealed record ActionResult(IReadOnlyList<int> AffectedIds);
```

### 3.2 The stack declaration and capabilities

```csharp
namespace Tellma.Core.Abstractions.Crud;

/// <summary>Declares one entity's CRUD stack. One class per entity; the composition root registers it.</summary>
public abstract class CrudStack<TEntity> where TEntity : TopLevelEntity
{
    /// <summary>The securable resource name, e.g. "core.User" (T4 naming).</summary>
    public abstract string Resource { get; }

    /// <summary>Columns the Search parameter matches; default: gated Name columns plus Code.</summary>
    public virtual IReadOnlyList<string> SearchableColumns => CrudDefaults.SearchableColumns(typeof(TEntity));

    /// <summary>The default natural key for export/import (T2 inference when null).</summary>
    public virtual string? NaturalKey => null;

    /// <summary>Navigations expanded into the details Related dictionary; default: every FK of the entity and its children.</summary>
    public virtual IReadOnlyList<string> DetailsExpand => CrudDefaults.AllNavigations(typeof(TEntity));

    /// <summary>Filters the UI applies by default (e.g. "IsActive = true"); capabilities add theirs.</summary>
    public virtual IReadOnlyList<string> DefaultFilters => [];

    public virtual int MaxSaveEntities => 10_000;
    public virtual int MaxDeleteByQueryRows => 10_000;
    public virtual int MaxValidationRounds => 3;
    public virtual int LargeBatchThreshold => 1_000;
    public virtual int CountCap => 10_000;
}

/// <summary>The entity can be switched on and off; projects Activate/Deactivate everywhere.</summary>
public interface IActivatable { bool IsActive { get; set; } }

/// <summary>The entity is a tree; Node is platform-mapped and never on the wire.</summary>
public interface ITreeEntity
{
    int? ParentId { get; set; }
    int SubtreeCount { get; }
    int ActiveSubtreeCount { get; }
}

/// <summary>Up to three content-language names, gated by the tenant's languages.</summary>
public interface IMultilingual { string Name { get; set; } string? Name2 { get; set; } string? Name3 { get; set; } }

/// <summary>The value is set by the server; a client-sent value is overwritten, never persisted.</summary>
[AttributeUsage(AttributeTargets.Property)] public sealed class ServerOwnedAttribute : Attribute { }

/// <summary>The value is set on create and reported as an error if changed on update.</summary>
[AttributeUsage(AttributeTargets.Property)] public sealed class WriteOnceAttribute : Attribute { }

/// <summary>Column-level search opt-in on string properties.</summary>
[AttributeUsage(AttributeTargets.Property)] public sealed class SearchableAttribute : Attribute { }
```

### 3.3 Hooks

```csharp
namespace Tellma.Core.Abstractions.Crud;

/// <summary>Rewrites the payload in memory before validation (trimming is platform-default).</summary>
public interface ICrudPreprocessor<in TEntity>
{
    void Preprocess(CrudPreprocessScope<TEntity> scope);
}

/// <summary>Validates a batch; loads context only through the scope's loader (one round trip per round).</summary>
public interface ICrudValidator<in TEntity>
{
    ValueTask ValidateAsync(CrudValidationScope<TEntity> scope, CancellationToken ct);
    ValueTask ValidateDeleteAsync(CrudDeleteScope<TEntity> scope, CancellationToken ct) => ValueTask.CompletedTask;
}

/// <summary>Adds statements to the persist batch and best-effort work after commit.</summary>
public interface ICrudSideEffect<in TEntity>
{
    void Contribute(CrudPersistScript<TEntity> script);
    Task AfterCommitAsync(CrudCommitted<TEntity> committed, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Adds named extras to the details batch (and the save read-back).</summary>
public interface ICrudDetailsContributor<in TEntity>
{
    IReadOnlyList<string> Extras { get; }
    void Contribute(CrudDetailsScript<TEntity> script, IReadOnlySet<string> requested);
}

/// <summary>What a validator sees: the payload with paths, the existing rows, the loader, the error sink.</summary>
public sealed class CrudValidationScope<TEntity>
{
    public IReadOnlyList<IndexedEntity<TEntity>> Entities { get; }
    /// <summary>Existing rows for updated ids, loaded through the caller's filter; missing ids already failed.</summary>
    public IReadOnlyDictionary<int, TEntity> Existing { get; }
    public IRequestContext Context { get; }
    public CrudErrorSink Errors { get; }
    /// <summary>Requests are deduplicated structurally and dispatched as one round trip per round.</summary>
    public ValueTask<TResult> Load<TResult>(ContextRequest<TResult> request, CancellationToken ct);
    public int Round { get; }
}

/// <summary>An entity with its position in the batch, for error paths like "[3].Permissions[1].Resource".</summary>
public sealed record IndexedEntity<TEntity>(int Index, TEntity Value)
{
    public string Path(string property) => $"[{Index}].{property}";
    public string ChildPath(string collection, int childIndex, string property) => $"[{Index}].{collection}[{childIndex}].{property}";
}

/// <summary>Errors keyed by path; codes and arguments are localized by the web layer.</summary>
public sealed class CrudErrorSink
{
    public void Add(string path, string code, params (string Name, object? Value)[] arguments);
    public bool HasErrors { get; }
    public IReadOnlyList<CrudError> Errors { get; }
}

public sealed record CrudError(string Path, string Code, IReadOnlyList<KeyValuePair<string, object?>> Arguments);

/// <summary>A typed, structurally comparable request for validation context.</summary>
public abstract record ContextRequest<TResult>;

public static class ContextRequests
{
    public static ContextRequest<IReadOnlyDictionary<int, T>> ByIds<T>(IEnumerable<int> ids) where T : TopLevelEntity;
    public static ContextRequest<IReadOnlyDictionary<TKey, T>> ByKey<T, TKey>(Expression<Func<T, TKey?>> property, IEnumerable<TKey> values) where TKey : notnull;
    public static ContextRequest<ILookup<int, TChild>> ByParentIds<TChild>(IEnumerable<int> parentIds);
    public static ContextRequest<bool> Exists(string root, FilterTree filter, IReadOnlyDictionary<string, object?>? arguments = null);
    public static ContextRequest<int> Count(string root, FilterTree filter, int cap, IReadOnlyDictionary<string, object?>? arguments = null);
    public static ContextRequest<IReadOnlyList<object?[]>> Query(string root, string select, FilterTree? filter, IReadOnlyDictionary<string, object?>? arguments = null);
    public static ContextRequest<IReadOnlyDictionary<int, IReadOnlyList<int>>> Ancestors<T>(IEnumerable<int> ids) where T : TopLevelEntity, ITreeEntity;
    public static ContextRequest<TResult> Raw<TResult>(string sql, IReadOnlyList<BatchParameter> parameters, Func<IResultReader, TResult> read);
}
```

### 3.4 Platform exceptions (owned here; HTTP mapping is T6's)

```csharp
namespace Tellma.Core.Abstractions.Errors;

/// <summary>The closed set of failures a pipeline reports; the web layer maps each to a status and a problem type.</summary>
public abstract class TellmaException(string message) : Exception(message)
{
    /// <summary>A stable, non-localized code such as "concurrency-conflict".</summary>
    public abstract string Code { get; }
}

/// <summary>Field-level errors; 422.</summary>
public sealed class ValidationException(IReadOnlyList<CrudError> errors) : TellmaException("Validation failed.") { public IReadOnlyList<CrudError> Errors => errors; public override string Code => "validation"; }
/// <summary>Absent or invisible; 404.</summary>
public sealed class NotFoundException(string resource, IReadOnlyList<int> ids) : TellmaException("Not found.") { public override string Code => "not-found"; }
/// <summary>No permission for the securable, or the RLS post-check failed; 403.</summary>
public sealed class ForbiddenException(string resource, string action) : TellmaException("Forbidden.") { public override string Code => "forbidden"; }
/// <summary>Another user changed a row since it was loaded; 409 with the conflicts.</summary>
public sealed class ConcurrencyConflictException(IReadOnlyList<ConcurrencyConflict> conflicts) : TellmaException("Modified by another user.") { public override string Code => "concurrency-conflict"; }
public sealed record ConcurrencyConflict(int Id, DateTime ModifiedAt, int ModifiedById, string? ModifiedByName);
/// <summary>A delete is blocked by a reference; 409.</summary>
public sealed class EntityInUseException(string resource, string referencedBy) : TellmaException("In use.") { public override string Code => "in-use"; }
/// <summary>A payload or result exceeds a declared cap; 413.</summary>
public sealed class LimitExceededException(string limit, int actual, int max) : TellmaException("Limit exceeded.") { public override string Code => "limit-exceeded"; }
/// <summary>Cached context was stale twice in a row; 503 (retryable).</summary>
public sealed class StaleContextException() : TellmaException("Context changed.") { public override string Code => "stale-context"; }
/// <summary>The tenant user is inactive; 403 and cache eviction.</summary>
public sealed class UserInactiveException() : TellmaException("User inactive.") { public override string Code => "user-inactive"; }

/// <summary>Reserved T-SQL error numbers the executor translates before any other handling.</summary>
public static class TellmaSqlErrors
{
    public const int Forbidden = 50403, NotFound = 50404, Concurrency = 50409, StaleContext = 50412, LimitExceeded = 50413, Leased = 50423;
}
```

### 3.5 Telemetry names (owned here; constants live in Abstractions)

```csharp
namespace Tellma.Core.Abstractions.Crud;

public static class CrudTelemetryNames
{
    public const string MeterName = "Tellma.Core.Crud";
    public const string ActivitySourceName = "Tellma.Core.Crud";
    /// <summary>Histogram, seconds, one measurement per operation. Tags: entity, operation, outcome.</summary>
    public const string OperationDurationInstrument = "tellma.crud.operation.duration";
    /// <summary>Histogram, {roundtrip}, per operation. Tags: entity, operation. The budget of D2 is asserted against this.</summary>
    public const string RoundTripsInstrument = "tellma.crud.roundtrips";
    /// <summary>Histogram, {round}, per save/delete. Tags: entity.</summary>
    public const string ValidationRoundsInstrument = "tellma.crud.validation.rounds";
    /// <summary>Histogram, {row}, top-level rows per save. Tags: entity, lane (small|large).</summary>
    public const string SaveRowsInstrument = "tellma.crud.save.rows";
    /// <summary>Counter, {attempt}. Tags: entity, operation, error.number (closed set of transient numbers).</summary>
    public const string RetriesInstrument = "tellma.crud.retries";
    /// <summary>Counter of optimistic-connect misses. Tags: reason (permissions|settings|cold|inactive).</summary>
    public const string ContextMissesInstrument = "tellma.crud.context.misses";
    /// <summary>Counter of 409s. Tags: entity, overridden (true|false).</summary>
    public const string ConcurrencyConflictsInstrument = "tellma.crud.concurrency.conflicts";
    public const string EntityTag = "tellma.entity";
    public const string OperationTag = "tellma.operation";
    public const string OutcomeTag = "tellma.outcome";
}
```

`tellma.entity` is a closed set (the model's stacks) and is allowed; tenant and user are never
tags. The request `Activity` gets `tellma.db.roundtrips` and `tellma.db.duration` tags at the end
of the operation, and the ASP.NET request-duration metric is enriched with a bucketed
`tellma.db.roundtrips.bucket` (`1|2|3-5|6+`) by T6.

### 3.6 Needed from other themes (exact members this pipeline calls)

```csharp
// T2 — batch abstraction (Tellma.Core.Abstractions.Data)
public sealed class BatchScript
{
    public BatchStatement Add(string sql, IReadOnlyList<BatchParameter> parameters, BatchStatementKind kind, bool mayRetry, IReadOnlyList<string> writesTables, IResultReaderFactory? reader);
    public string TableSource(string name, TableSource source);        // declares @page/@stamps/@ids for restrictions; returns the host name
    public void BeginTransaction(); public void Commit();               // emits SET XACT_ABORT ON; BEGIN TRAN / COMMIT
    public int NextBatchOrdinal();                                      // for Queryex compilations composed into this script
}
public sealed record BatchParameter(string Name, BatchValueType Type, object? Value, string? TableTypeName = null, IEnumerable<object>? Rows = null);
public interface IBatchExecutor { Task<BatchResults> ExecuteAsync(BatchScript script, IRequestContext context, CancellationToken ct); }
public interface IResultReader { bool Read(); T Get<T>(int ordinal); bool NextResult(); }
// T2 — save emitter
public interface ISaveEmitter { void EmitUpsert<T>(BatchScript s, IReadOnlyList<T> rows, string stampsSource, SaveLane lane); void EmitSync<TChild>(BatchScript s, IReadOnlyList<TChild> rows, string parentIdsSource, SaveLane lane); void EmitDelete<T>(BatchScript s, string idsSource); }
// T2 — id allocator
public interface IIdAllocator { IdBlock Take(Type entityType, int count, BatchScript? rideOn); void Release(IdBlock block); }
// T2 — Queryex host integration
public interface IQueryexHost { QueryexSchema Schema(IRequestContext ctx); CompiledQuery Compile(QuerySpec spec, QueryCompilationOptions options); ValueMaterializer Materializer(EntityDescriptor root); }
// T4 — permissions and the connect statement
public interface IPermissionEvaluator { PermissionDecision Evaluate(IRequestContext ctx, string resource, string action); }
public sealed record PermissionDecision(bool Allowed, FilterTree? Filter, Guid PermissionsTag);
public interface IConnectStatementSource { void AppendGuard(BatchScript s, ConnectSnapshot expected, bool insideTransaction); void AppendTagsReadBack(BatchScript s); Task<ConnectSnapshot> ConnectAsync(IRequestContext ctx, CancellationToken ct); }
// T3 — tag registry
public interface ITagRegistry { void AppendBumps(BatchScript s, IReadOnlyList<string> writtenTables); }
// T1 — request context
public interface IRequestContext { int TenantId { get; } int UserId { get; } string Subject { get; } CultureInfo Culture { get; } string Calendar { get; } TimeZoneInfo TimeZone { get; } DateOnly Today { get; } bool IsSandbox { get; } }
// T7 — staged blobs
public interface IBlobStagingConfirmation { void AppendConfirm(BatchScript s, IReadOnlyList<Guid> tokens); Task DeleteAfterCommitAsync(IReadOnlyList<string> blobIds, CancellationToken ct); }
// T10 — notifications
public interface IInboxEnqueue { void AppendInserts(BatchScript s, IReadOnlyList<InboxNotification> notifications); Task NudgeAsync(IReadOnlyList<int> userIds, CancellationToken ct); }
```

---

## 4. Schema

This theme owns no business table. It fixes the column shapes every stack's table must carry for
the pipeline to work, two standalone table types, and the columns it reads from tables owned by
T3, T4, T7 and T10.

```text
-- Every top-level entity table (T2 base class TopLevelEntity)
Id              int             NOT NULL  PRIMARY KEY (clustered unless a tree; sequence sq_<Table>)
CreatedAt       datetime2(7)    NOT NULL                                  -- server-owned
CreatedById     int             NOT NULL  FK core.Users(Id)               -- server-owned
ModifiedAt      datetime2(7)    NOT NULL                                  -- the concurrency stamp; bumped only by user-visible mutations
ModifiedById    int             NOT NULL  FK core.Users(Id)               -- server-owned
-- IActivatable
IsActive        bit             NOT NULL  DEFAULT 1                       -- index only where a default filter is hot: filtered index WHERE IsActive = 1 on (Id) is optional
-- ITreeEntity
ParentId        int             NULL      FK self, INDEX IX_<Table>_ParentId (ParentId)
Node            hierarchyid     NOT NULL  UNIQUE INDEX UX_<Table>_Node (depth-first; backs descendantOf/ancestorOf, the recompute, and DeleteWithDescendants)
Level           AS Node.GetLevel()  PERSISTED (optional index (Level, Node) only when level() predicates are expected)
SubtreeCount    int             NOT NULL  DEFAULT 1
ActiveSubtreeCount int          NOT NULL  DEFAULT 1
-- IMultilingual
Name            nvarchar(255)   NOT NULL
Name2           nvarchar(255)   NULL
Name3           nvarchar(255)   NULL
Code            nvarchar(50)    NULL      UNIQUE INDEX UX_<Table>_Code WHERE Code IS NOT NULL   -- the index name is what 2601 maps back to the property
```

```text
-- Every child (weak) entity table
Id              int             NOT NULL  PRIMARY KEY (sequence sq_<Table>)
<ParentId>      int             NOT NULL  FK <parent>(Id), INDEX IX_<Table>_<ParentId> (<ParentId>) INCLUDE (Id)   -- the synchronisation predicate seeks on it
-- no audit columns, no stamp (protected by the parent's stamp)
```

```text
-- Standalone table types bound by the pipeline (spec 0001 class-derived route, Tellma.Core.Abstractions.TableTypes)
[StampList]      Id int NOT NULL PRIMARY KEY, Stamp datetime2(7) NOT NULL   -- expected stamps for updated rows; new stamps for the verify probe
[IdList]         Id int NOT NULL PRIMARY KEY                                -- restrictions (@ids, @savedIds, @syncedRoles, @affected)
[KeyLookupList]  RowKey int NOT NULL PRIMARY KEY, Value nvarchar(450) NOT NULL   -- import natural-key translation (T9 consumes)
```

```text
-- Read by the connect guard (owned by T4 / T3; the pipeline needs exactly these)
core.UserActivities   UserId int PK FK core.Users, LastActive datetime2(3) NULL, PermissionsTag uniqueidentifier NOT NULL, UserSettingsTag uniqueidentifier NOT NULL, ... (inbox counters)
core.TenantTags       Id int PK CHECK (Id = 1), SettingsTag uniqueidentifier NOT NULL, SecurablesTag uniqueidentifier NOT NULL, LookupsTag uniqueidentifier NOT NULL
core.Users            Id, IsActive, Subject (for ConnectAsync), Name/Name2/Name3 (for conflict display)
-- Written by side effects (owned by T10 / T7)
core.InboxItems       via [InboxItemsList] TVP
core.Blobs            Token uniqueidentifier, State tinyint (0 staged, 1 committed)
```

Index notes for the statements above: the persist batch seeks by PK on the row-image join and on
`@stamps`; child synchronisation seeks the parent-id index; the recompute seeks the `Node` unique
index; the connect guard seeks `core.UserActivities` and `core.TenantTags` by PK; the count query
uses whatever the filter's leading predicate allows and stops at `@cap`.

---

## 5. Answers

| Brain-dump question (abridged) | Answer |
|---|---|
| "Are Data / Service / Web the proper layer names?" | Data access, application services, web API — folders inside one distribution project; the platform's names are the package names (`Tellma.Core.Abstractions.Crud`, `Tellma.Core.Crud`). |
| "How do we extract the common API of these services? Base class vs composition?" | Composition (D1): sealed `CrudPipeline<TEntity>` behind `ICrudService<TEntity>`, hooks resolved along the leaf's base chain. |
| "Distros must extend or replace entities while reusing service logic … interfaces rather than the concrete entity" | Hooks are generic over the pack's entity type and contravariant; a leaf inherits every pack hook with zero lines (D1). No paired interfaces. |
| "If the user requests 5 ids and 4 are found, 4 or 404?" | Four; only single-entity details returns 404 (D10). |
| "Search: should we keep it? Page-vs-picker hints?" | Keep; server-side over declared columns; no hint (D12). |
| "Details: Queryex or raw SQL?" | Both by role: Queryex for expressions and RLS, model-emitted keyed SQL for reads by id (D10). |
| "Ancestors: another array of arrays?" | Yes, `QueryResult.Ancestors`, loaded in the same round trip via the list restriction (D10). |
| "Count stops beyond 9999" | `TOP (@cap)` wrapped count; `CountCapped` flag (D10). |
| "Start the transaction — is this right?" | No; the transaction is the persist batch only, expressed in T-SQL (D4). |
| "One DB call loads the validation context, but sometimes a second …" | DataLoader rounds, bounded at 3, metered (D6). |
| "How to inject custom validators that participate in the batch DB call?" | `ICrudValidator<T>` awaiting `scope.Load(request)` (D6). |
| "How to dedupe validation context queries?" | Structural request keys; `Query` requests differing only by Select are merged (D6). |
| "Can DB calls #1 and #2 collapse with cached permissions?" | Yes, into every batch, asserted in T-SQL; stale ⇒ 50412 ⇒ connect trip ⇒ rerun (D3). |
| "How many round trips?" | The budget of D2. |
| "RLS post-check … roll back and Forbidden" | `THROW 50403` inside the batch after the DML, before COMMIT (D4). |
| "Pre-commit non-transactional side effects (blobs)" | Eliminated by staged uploads; three homes for side effects (D15). |
| "Notification enqueue rides the save" | `IInboxEnqueue.AppendInserts` on the persist script; nudge after commit (D15). |
| "Write-once columns: two UDTTs or the service layer?" | Service layer, from the existing row loaded in trip 1 (D13). |
| "Capability endpoints: a recipe of a few lines" | Interface on the entity + one stack declaration (D8). |
| "Alternative to RowVersion implementable in C#?" | `ModifiedAt` with strict per-row monotonic stamps and a `StampList` guard (D7). |
| "Concurrency override flag" | `SaveOptions.OverrideConcurrency` (D7). |
| "Id ranges without a dedicated round trip; un-consume on failure?" | Reservation rides trip 1 sized from the payload; release only when the block is still adjacent (D5). |
| "Who assigns ids, service or data layer?" | The pipeline assigns after validation, before the persist batch, from T2's allocator. |
| "Hierarchyid maintenance: in memory or SQL after save?" | SQL, one recompute statement in the persist batch; cycles validated in C#, guarded by `MAXRECURSION` (D9). |
| "Empty child collection deletes; missing is untouched" | Confirmed; `@syncedRoles` lists only parents whose collection was present (D4). |
| "Save admits a single entity" | The service takes an array; the UI sends one (D13, T6). |
| "Import reuses the pipeline; must remain very fast" | Same `SaveAsync` with `ReturnEntities = false`, natural keys translated in trip 1, large-batch plan lane (D5, D11). |
| "DeleteByQuery as the only way to undo a 10K import" | Capped, single-trip transactional script (D14). |
| "Rate limiting on entity counts" | `CrudStack.MaxSaveEntities` ⇒ `LimitExceededException` (T6 owns HTTP-level limits). |
| "Exceptions: enumerate or interface?" | A closed set of `TellmaException` subclasses with a `Code`; T6 maps (§3.4). |
| "Validation messages or codes?" | Codes plus arguments from the pipeline; the web layer localizes and sends both. |

---

## 6. Seams

1. **Batch abstraction (T2 owns).** Needed: `BatchScript` with `Add(sql, parameters, kind, mayRetry,
   writesTables, reader)`, `TableSource` for host-named list sources, `BeginTransaction`/`Commit`
   emitting T-SQL, `NextBatchOrdinal`; `IBatchExecutor` returning readers routed by statement;
   whole-batch retry with the eligibility rule and the verify probe of D11; the executor translates
   50xxx errors, 2601/2627, 547, 530 and 1205 before anything else. Concatenated text on one
   `SqlCommand`, never `SqlBatch`.
2. **Entity class vs wire shape (T2 owns).** Position: single entity class, `[NotMapped]` child
   collections with null/empty semantics, `[ServerOwned]`/`[WriteOnce]` markers; the pipeline
   guarantees overwrite (D13).
3. **One capability, declared once (this theme owns).** The `CrudCapability` descriptor of D8 lists,
   per capability, the columns (T2 applies), securable actions (T4 registers), operations (here),
   endpoints (T6 projects), default filters (metadata for T6/UI/MCP).
4. **Queryex schema per tenant configuration (T2 owns).** Needed: `IQueryexHost.Schema(ctx)` keyed on
   the tenant's `SettingsTag` (languages gate `Name2/3`) so a settings change rebuilds the schema and
   the engine caches roll naturally; the `SettingsTag` is part of the connect guard for that reason.
5. **Version tags (T3 owns).** Needed: `core.TenantTags` single row with `SettingsTag`,
   `SecurablesTag`, `LookupsTag`; user-level tags in `core.UserActivities`; `ITagRegistry.AppendBumps`
   called by the pipeline with the tables every persist statement declares; tags are
   `uniqueidentifier` regenerated with `NEWID()` (restore-safe, equality only). Stack-declared bumps
   (a role save bumps its members' `PermissionsTag`) are side-effect statements, not registry rules.
6. **Feature composition (T1 owns).** `CrudStack<TEntity>` is the feature's declaration; the
   composition registers the pipeline, resolves hooks along the base chain once, and audits that
   every stack's resource is in the securables registry.
7. **Natural keys (T2 owns, T9 consumes).** `CrudStack.NaturalKey` overrides T2's inference; the
   pipeline's import path uses `[KeyLookupList]` for bulk translation in trip 1.
8. **Background-task columns (T10 owns).** Not emitted by this pipeline; `TellmaSqlErrors.Leased`
   (50423) is reserved so a save that touches a leased task row can fail closed.
9. **Request context (T1 owns).** Needed members in §3.6; the pipeline never reads `HttpContext`.
10. **Platform exceptions (this theme owns; T6 maps).** §3.4 with suggested statuses: validation 422,
    not-found 404, forbidden 403, concurrency 409, in-use 409, limit 413, stale-context 503,
    user-inactive 403.
11. **Permission evaluation (T4 owns).** Needed: `Evaluate(ctx, resource, action) → (Allowed, Filter,
    PermissionsTag)` from the instance cache with no I/O; `IConnectStatementSource` emits the guard
    and performs the connect trip; the evaluator's `FilterTree` is conjoined by the pipeline, never
    concatenated.
12. **Blob staging tokens (T7 owns).** `IBlobStagingConfirmation.AppendConfirm` in the persist
    script; deletes after commit.
13. **Wire shapes (T6 owns).** The service shapes of §3.1 are what the wire mirrors; rows are
    positional arrays; errors carry `Path`, `Code`, `Arguments`, and a localized message.
14. **Telemetry (T2 owns the DB-call budget instruments).** This theme records
    `tellma.crud.roundtrips` from the executor's per-request counter; the executor's own
    `tellma.data.*` instruments stay the per-call view.
15. **Notification enqueue (T10 owns).** `IInboxEnqueue.AppendInserts` + `NudgeAsync` after commit.
16. **Connect-call collapse (T4/T5).** Position: collapse, asserted in T-SQL, with the failure modes
    of D3; the connect trip exists only for cold cache and stale tags.
17. **Vocabulary.** Four audit columns on every top-level entity; plural schema-qualified tables;
    `int` ids; "tag"; "securable"; `ModifiedAt` as the stamp; `core.UserActivities` for the churn
    columns; capability interface names `IActivatable`, `ITreeEntity`, `IMultilingual`.

---

## 7. Departures

- **Parent→child navigations.** The architecture forbids parent→child *EF* navigations; this design
  keeps that (no EF navigation, no Queryex collection) but puts `[NotMapped]` child collections on the
  entity class for the wire and the save. The reason is one class for storage, JSON and TVP with the
  pipeline enforcing the server-owned split; the enforcement is what the architecture's rule was
  protecting (no cartesian reads), and it is preserved because the EF model still has no such
  navigation.
- **"Context loading: C# → typed bulk queries (Queryex)".** Keyed context loads (by id, by parent
  id, by natural key) are model-emitted SQL, not Queryex text, because they have no expression to
  compile and must be seekable and plan-stable; Queryex remains the only path for user- or
  permission-authored text. The sentence in the architecture should read "Queryex or model-emitted
  keyed SQL".
- **Endpoint verbs.** Unchanged by this theme (the service is transport-agnostic); T6 records the
  all-POST web surface.
- **Catalog and feature-composition fidelity.** Not touched here beyond `CrudStack<TEntity>` acting
  as the feature declaration.
- **`SqlBuilder<T>` for tier-2 SQL.** The persist and details scripts are emitted by the platform
  from model metadata, not by `SqlBuilder<T>`; `SqlBuilder<T>` stays the pack author's tool for
  reports. The `Raw` context request is the analyzer-gated escape hatch the architecture describes.

---

## 8. Verification

Facts relied on, all verified 2026-09-01 in the research files unless marked:

- SqlClient's built-in retry skips any command with an ambient `TransactionScope` or attached
  `SqlTransaction`; `SqlBatch` has no `RetryLogicProvider`; `SqlBatch` sends a shared TVP once per
  command and exposes only the last statement to diagnostics (research §2–3; data-access §3.2).
- `SET XACT_ABORT ON` makes any runtime error, `THROW` included, roll back the whole transaction;
  `RAISERROR` does not honour it (research §2.5).
- Distributed transactions throw on Linux; `TransactionScope` defaults are Serializable, one minute,
  async flow suppressed (research §2.3–2.4).
- RCSI is on by default on Azure SQL Database and off on-premise; Azure SQL runs optimized locking
  (LAQ), disabled on statements carrying `UPDLOCK`/`HOLDLOCK`/`OUTPUT`; a C# uniqueness check is
  write-skew-prone and 2601/2627 are the guarantee (research §6).
- `MERGE` defects on temporal targets and under indexed views survive on SQL Server 2022 CU7
  (data-access §4); `NEXT VALUE FOR` is banned inside `MERGE` (§5).
- `sp_sequence_get_range` is a procedure with `OUTPUT` parameters that can ride any batch (§5).
- `hierarchyid` cannot ride a TVP without `Microsoft.SqlServer.Types`; the documented bulk pattern is
  a recursive CTE with `ROW_NUMBER` paths (core-gl-stacks §2.2); the depth-first unique index on
  `Node` backs subtree predicates (§2.3).
- Temporal tables write a history row on every `UPDATE` even when nothing changed (data-access §2.2)
  — the reason for the `EXCEPT` skip predicate.
- BCL `Validator.TryValidateObject(..., validateAllProperties: true)` validates the object's own
  properties and "does not recursively validate properties of the objects returned by the
  properties" — verified myself on learn.microsoft.com (page ms.date 2025-07-01, updated
  2026-07-01), hence per-entity and per-child invocation in D6.
- `SYSUTCDATETIME()` returns `datetime2(7)` with 100-nanosecond precision and is nondeterministic —
  verified myself (learn.microsoft.com, ms.date 2025-10-20); the design computes the stamp in C#
  anyway.
- Table-variable deferred compilation (compatibility level 150+) compiles a statement on first
  execution with that execution's row count, caches the plan, and "doesn't increase recompilation
  frequency" — verified myself (learn.microsoft.com Intelligent Query Processing details, ms.date
  2026-06-12). Whether the page names TVPs explicitly: it says "table variables"; TVPs are table-typed
  variables and the same behaviour is reported by community sources — treat the TVP applicability as
  inferred, and measure the two lanes on LocalDB.
- The DataLoader contract (coalesce, one batch function per tick, missing ⇒ explicit absent,
  manual dispatch) and GreenDonut's per-loader batching (research §4).
- `IHttpActivityFeature`, `IHttpMetricsTagsFeature`, `IMeterFactory`, and the repo's telemetry
  naming rules (research §5.4; ARCHITECTURE.md Observability).
- Queryex facts from the compiled source: `FilterTree.Or([])` is false; `@qx{b}_p{n}` namespacing;
  `QueryexLimits.MaxParameters = 512`; no list restriction and no `level()`; `CompiledQuery.Sql` is
  one text (`DECLARE` block then `SELECT`), which is why D10 asks for `Prologue`/`Statement`.
- `THROW` user error numbers must be 50000 or above; `STRING_AGG` is available on all targets
  (SQL Server 2017+, Azure SQL) — from general T-SQL knowledge, not re-verified today.

Still unverified: whether `OPTION (RECOMPILE)` on a TVP-sourced `UPDATE … EXCEPT` statement produces
the expected seek plans at 50k rows (measure); the relative cost of the `IsDescendantOf` counter
refresh versus a second recursive CTE at Center scale (measure); whether error 530 (`MAXRECURSION`
exceeded) is raised before any row of the recompute `UPDATE` is written (it is a statement-level
error and `XACT_ABORT ON` rolls back regardless, so the design does not depend on it); the exact
`ActivitySource` name of the SqlClient instrumentation; and the "Prologue/Statement" amendment's
acceptability against spec 0008's single-door rule (a spec 0011 decision).
