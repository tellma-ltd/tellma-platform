# Entity contract and data access (spec 0011) — design proposal

Written for a reader who has seen none of the other design files. Every name below is intended as final. Package names, C# shapes and SQL are concrete enough to write the spec from; the review flags mark the places where an equally plausible alternative exists and Ahmad should choose.

Two packages own this theme:

- `Tellma.Core.Abstractions` (existing; EF-free, BCL-only) gains the **entity contract**: base classes, capability interfaces and the attributes an entity author uses.
- `Tellma.Core.Data.Abstractions` (new; references `Tellma.Core.Abstractions` and `Tellma.Core.Queryex`, nothing else) holds the consumer-facing **data access API** that modules and the service pipeline program against: `IDataBatch`, entity queries, entity metadata, the meter names.
- `Tellma.Core.Data` (new; references SqlClient, EF Core relational, `Tellma.Core.EntityFrameworkCore`, Queryex) implements it: the batch executor, the save emitter, the ID allocator, the schema adapter, the materializer, the tree statements, the lease statement helpers.

Modules (`Tellma.Module.Gl`) reference only the two Abstractions packages; the distribution and `Tellma.Core` reference `Tellma.Core.Data`.

---

## 0. Lens checks

### Lens A — distro-author simplicity

**How many lines does a distro write to add an entity with IsActive and a tree?** Eleven, all mechanical, and one registration line:

```csharp
[Table("Centers", Schema = "gl"), TableType]
public sealed class Center : TreeEntity<Center>, IActivatable
{
    public CenterType Type { get; set; }
    [MaxLength(255)] public string Name { get; set; } = null!;
    [MaxLength(255)] public string? Name2 { get; set; }
    [MaxLength(255)] public string? Name3 { get; set; }
    [MaxLength(50)] public string? Code { get; set; }
    public bool IsActive { get; set; } = true;
}
```

`TreeEntity<Center>` supplies `Id`, the four audit columns, `ParentId`, `Node`, `SubtreeCount`, `ActiveSubtreeCount`, the `[ServerOwned]` markers on the computed columns and the metadata that makes the tree statements fire; `IActivatable` (one property, `IsActive`) supplies the activatable recipe; `CenterType` is an enum stored as `varchar(n)` by a platform convention with zero distro lines; `Name`/`Name2`/`Name3` form a multilingual group by naming convention; the natural key is inferred (`Code`, unique). The registration line is the feature's `entities.Add<Center>()` (owned by the host theme). Nothing else: no UDTT class, no SQL, no repository, no mapping of children, no tree maintenance code.

**Is every capability declared once?** Yes at the data-access layer: a capability is a base class or a one-property interface, and everything the data layer does for it (columns, server-owned markers, emitter statements, tree recompute, activatable counts, Queryex tree node) keys off that one declaration through `EntityMetadata`. The service, permission and endpoint halves of a capability are the pipeline theme's job, but they read the same `EntityMetadata` flags, so a capability is never re-declared.

**MCP consumer.** Nothing in this layer is MCP-visible, but the materializer's output (entities plus a related-entity dictionary keyed by entity name and id) is the same shape whether the consumer is the SPA, an import, or a tool result.

### Lens B — round trips, bulk shapes, locks

**DB calls per operation** (common case, permissions cached, connect folded into the first round trip):

| Operation | Round trips | What rides each |
|---|---|---|
| Query (grid page) | 1 | connect + tag reads, page query, capped count, ancestors query (tree) |
| Details by id | 1 | connect, entity query, one query per child collection, related-entity queries, row echo |
| Save (new or existing, with children) | 2 | (1) connect, RLS pre-check, validation context, cycle-check projection, **id reservation**; (2) persist in one transaction: guard, inserts, child sync, root update, tree recompute, tag bumps, RLS post-check, read-back |
| Import of N rows | 2 | as save; natural-key translation rides round trip 1 through TVPs |
| Delete by ids / by query / with descendants | 1 | connect + RLS-filtered delete + tag bumps |
| Activate/deactivate by ids | 1 | connect + stamped update + tree count recompute + tag bumps |
| Get by parent ids | 1 | one query with a TVP restriction |

**N+1s found and removed:** child collections are loaded with one statement per collection per batch (never per parent); tree maintenance is one statement per tree table per batch (never per row); id reservation is one procedure call per sequence per batch; related entities are resolved by joins in the same statement; concurrency is checked inside the UPDATE, not by a per-row SELECT.

**Locks held across I/O:** none. The transaction is opened and committed inside the text of one round trip; validation reads happen in an earlier, untransacted round trip; no C# code runs while a transaction is open. The one deliberate lock-holding construct is the lease acquisition statement (`UPDLOCK, READPAST, ROWLOCK`), which is single-statement.

**Plan-cache friendliness:** every batch is `sp_executesql` with typed parameters and TVPs of stable physical names; Queryex templates are cached in the engine; save statements are generated once per (entity type, statement kind) and cached by the emitter; the only per-request variation is parameter values. Retries re-bind identical text.

### Lens C — fail-closed, no silent loss, evolution

**Where could a stale cache leak data?** (1) The Queryex schema is cached per (EF model, language profile); a tenant gaining a third language yields a new profile key, so `Name3` cannot leak into or out of a schema built for two languages. (2) The allocator buffer is per (tenant database, sequence); a database restored to an older state is healed forward by the `MAX(Id)` comparison, and a buffer above the restored `MAX` only produces a gap. (3) Cached permissions are not this layer's cache, but the batch carries the tag read that lets the pipeline detect staleness in the same round trip it used them.

**Which write path bypasses the tag bump?** Every write goes through `IDataBatch`, and every batch consults the registered `IDataBatchContributor`s before execution, so the tag bumper sees every write with its declared table set. The two bypasses are closed structurally: EF `SaveChanges` is unavailable at runtime (the runtime context exposes no `DbSet`s and an analyzer forbids `SaveChanges` outside the migrator), and raw SQL that writes must declare `writes:` — an analyzer flags DML keywords in a raw statement with no declaration.

**Concurrency soundness:** the concurrency check is *inside* the UPDATE that takes the row's U/X lock, so it is correct under both locking read committed and RCSI; the early guard is only an optimisation. Cycles that slip past the C# check under write skew are caught by the bounded recursive recompute in the persist transaction, which fails the batch.

**Schema evolution:** UDTT binding is metadata-driven, so a pack adding a base-class column is a non-event; the emitter derives statements from the running app's own model, so an N−1 app writes the columns it knows and the expanded column keeps its default; `ModifiedAt` as the concurrency token survives every migration that keeps the audit columns, which the base class guarantees.

**What breaks a distro on the next platform minor?** Only public attribute and base-class names, which are semver-governed API. Column *order* changes are absorbed by the metadata API; new server-owned columns are ignored by old clients; a new required column on a platform base ships with a default and is stamped by the emitter.

---

## 1. Critique of the brain dump (this theme's parts)

**The general design is right and the round-trip discipline is the best part of it.** One save emitter, one multi-statement executor, app-assigned ids, bulk-only APIs, observability of DB calls from day one — none of that needs changing. What needs changing is mostly precision: several sentences describe a mechanism that cannot work as stated on SQL Server, and several tables carry columns that are either redundant or in the wrong place.

**Internally inconsistent or unworkable as stated**

1. *"Upsert for top-level entities"* reads as MERGE. MERGE is out (two live defects on temporal targets and on DELETE actions under indexed views, and `User`/`Role`/`RoleMembership`/`Permission` are exactly temporal). Separate INSERT/UPDATE/DELETE, and — because ids are app-assigned — two TVPs per table (new rows, existing rows) rather than one TVP with an existence test, so that "update a row someone deleted" is a conflict and not a silent resurrection.
2. *"Start the transaction (step 4) … validate (step 5) … save (step 7) … commit (step 11)"* holds a transaction across at least two round trips and across C# validation. On RCSI (Azure SQL default) that buys no protection against write skew, and on-prem it holds shared locks while C# runs. The transaction must live inside the persist round trip only; every check that needs "inside the transaction" semantics (concurrency stamp, RLS post-check, tree cycle) is expressed in SQL and fails the batch.
3. *"MayRetry, true for reads and idempotent writes"* conflates two different questions. The driver never retries anything inside a transaction, so retry is the executor's; and the interesting distinction is *reported* failures (the server told us the batch aborted and, under `XACT_ABORT`, rolled back — always safe to re-run the whole round trip, deadlocks included) versus *ambiguous* failures (connection lost, we do not know whether `COMMIT` ran — safe only if every statement is idempotent). The flag is renamed `Idempotent` and defined as "may run again even if the previous attempt might have committed".
4. *"Save the entities … load the response back in the same DB call … also include a query with the RLS filter … if RLS fails, roll back"* requires C# to decide after seeing results but before commit. With commit inside the batch text (the only one-round-trip shape) that is impossible; the post-check has to be a SQL assertion (`IF (count) <> @n THROW`), which needs a small Queryex amendment: `CompiledQuery` exposes its `SELECT` body separately from its hoisted declarations so it can be embedded.
5. *`User` carries `SavedAt`/`SavedById` and the temporal period, and `Center` carries four audit columns* — two vocabularies for one concept. One vocabulary: four audit columns on every top-level entity; temporal is an additive per-table capability, not a different base class. `ModifiedAt` is not redundant with `ValidFrom`: it is the concurrency token, it is app-visible without reading the period columns (which are shadow properties in EF 10 and cannot ride a TVP), and it does not move on the bookkeeping writes that a sibling table absorbs.
6. *`Center.IsLeaf`* is `SubtreeCount = 1`; *`Level`* is `level(Node)`; both are dropped. `SubtreeCount` and `ActiveSubtreeCount` are kept because the tree view's expander decision needs them for every visible row without a subquery.
7. *`Node` in the UDTT* cannot work: `hierarchyid` has no TVP binding without `Microsoft.SqlServer.Types`. `Node` is excluded from the row image and computed in SQL.
8. *"Reserve the IDs from the DB every time a new record is saved"* plus *"large ranges cause gaps"* — the tension dissolves once reservation is exact and rides the round trip the save already makes (validation context), with unused ranges returned to an in-process buffer. No background prefetch, no dedicated round trip, gaps only on process crash.
9. *"Relying on RowVersion … not all properties participate"* — the right observation, but the fix is not a hash. `ModifiedAt` becomes the token under one rule: user-visible mutations stamp it, bookkeeping never touches the entity row (sibling tables, lease columns on task rows). Evaluated against `rowversion` in D14.
10. *"Two UDTTs for create and update"* for write-once columns is the rejected `ForSave` pattern in disguise. The row image stays one; the emitter simply never puts a `[WriteOnce]` or `[ServerOwned]` column in an UPDATE's SET list. Zero extra types, zero drift.
11. *"Validate that the interface matches the DB (no needed columns are dropped)"* is impossible to violate for inherited properties (C# cannot un-inherit) and possible only through `[NotMapped]`/`Ignore()`; the metadata builder rejects a leaf whose capability-declared property is unmapped, at startup.

**Naming and vocabulary**

- Singular `core.User` conflicts with the architecture's plural `gl.Invoices`. Plural everywhere; the CLR type stays singular and is the Queryex entity name.
- "Weak entity" becomes **child entity** (`ChildEntity<TKey>`); "top-level" stays (`TopLevelEntity<TKey>`); "temporal" is `[Temporal]`.
- The emitter is **`SaveEmitter`**, its input a **`SaveSpec`**; the multi-statement builder/executor is **`DataBatch`** behind `IDataBatch` — one round trip, built then executed. `RoundTrip` was considered and rejected (a noun for the *cost*, not the *thing*); `SqlBatch`/`DbBatch` collide with ADO.NET types.

**Gaps the brain dump does not mention**

- How child collections travel on an entity class the architecture forbids from having them (D3).
- Which properties a client may set (D4).
- Natural keys (D5), enum storage (D6), multilingual groups (D7).
- The tenant-configuration dimension of the Queryex schema and its cache key (D17).
- Lease statements (D21), the ordinal-binding analyzer (D22), the fixture entities and test tiers (D23).

---

## 2. Decisions

### D1 — Keyed entities: `Entity<TKey>`, `int` by default, `long` by opt-in

**Decision.** Every entity derives from `Entity<TKey>` (`TKey : struct, IEquatable<TKey>`), which declares `TKey Id`. Two non-generic sugars exist for the common case: `TopLevelEntity` = `TopLevelEntity<int>`, `ChildEntity` = `ChildEntity<int>`. A `long` key is `TopLevelEntity<long>`; the allocator and the bulk lists (`BigIdList`) already handle it. `Guid` keys are not offered (no sequence source; no need surfaced).

**Rationale.** Generic services and the emitter constrain on `TKey` once; distros never see the generic in the common case.

**Rejected.** A non-generic `Entity` with `int Id` and a separate `BigEntity` (duplicates every capability base); `object Id` (boxing in every TVP row).

**Confidence.** High.

### D2 — Top-level entities carry four audit columns; temporal is an additive capability

**Decision.** `TopLevelEntity<TKey>` declares `CreatedAt datetime2(7) NOT NULL`, `CreatedById int NOT NULL` (FK to `core.Users`), `ModifiedAt datetime2(7) NOT NULL`, `ModifiedById int NOT NULL` (FK). All four are `[ServerOwned]`. `[Temporal]` on a top-level class makes the table system-versioned with period columns `ValidFrom`/`ValidTo` (shadow properties, EF 10) and history table `<Table>History` in the same schema; the platform's model convention applies it. Child entities carry no audit columns of their own; the root's audit columns describe the aggregate, and a child's history is its own temporal history when the parent is `[Temporal]` (the convention marks children temporal when their parent is, unless `[Temporal(false)]`).

**Rationale.** One vocabulary; creation is otherwise lost (a temporal history can reconstruct it only by scanning); `ModifiedAt` is the concurrency token (D14) and must be an app-visible column in the UDTT, which period columns cannot be.

**Rejected.** `SavedAt/SavedById` + period for temporal, four columns for the rest (two vocabularies, and `SavedById` on a temporal row is not the creator). `SavedById` on child rows (redundant with the root; the brain dump asked — answer: no).

**Confidence.** High. **Review flag:** `CreatedById`/`ModifiedById` as DB FKs to `core.Users` make every table depend on Core's user table; the architecture wants every reference FK-enforced, so the FK stands, but `ON DELETE NO ACTION` means users can never be hard-deleted — consistent with the deactivate-not-delete rule for users.

### D3 — Child collections are `[NotMapped]` properties on the parent class; the EF model keeps no parent→child navigation

**Decision.** A top-level entity declares its children as ordinary list properties marked `[NotMapped]`:

```csharp
[NotMapped] public List<RoleMembership>? RoleMemberships { get; set; }
```

The child class marks its parent foreign key with `[ParentKey]`:

```csharp
public class RoleMembership : ChildEntity
{
    [ParentKey] public int UserId { get; set; }
    public int RoleId { get; set; }
    [MaxLength(1024)] public string? Notes { get; set; }
}
```

`EntityMetadata` (D8) pairs each `[NotMapped]` list of a `ChildEntity` type with the EF foreign key from the child's `[ParentKey]` column to the parent's table; a child with two foreign keys to the same parent type disambiguates with `[Children(nameof(RoleMembership.UserId))]` on the collection. The EF model never sees the collection (no `Include`, no cartesian reads, Queryex has no collections); save and details payloads carry it; the emitter synchronises it. Nesting is recursive (a child may itself declare `[NotMapped]` grandchildren).

Null versus empty is significant: `null` means "not supplied — leave the children alone"; an empty list means "delete them all". The emitter receives, per parent row and per collection, whether the collection was supplied, and passes it as an `IdList` of "parents to synchronise".

**Rationale.** One class is both storage shape and wire shape, which is the least a distro can write; the architecture's ban is on *EF* navigations and its reasons (bulk-shape enforcement, no accidental `Include`) survive intact because EF does not know the property.

**Rejected.** A parallel DTO hierarchy per entity (doubles every class, needs a mapper, drifts); EF collection navigations with `AutoInclude(false)` (Queryex would have to model collections; `Include` becomes possible).

**Risks named.** A client can send values for server-owned columns and for children of children it should not touch; D4 and the pipeline's `ResetServerOwned` cover the former, and the emitter's "supplied collections only" rule the latter.

**Confidence.** High.

### D4 — Editable versus server-owned is declared with `[ServerOwned]` and `[WriteOnce]`; the emitter enforces it

**Decision.** Two attributes in `Tellma.Core.Abstractions.Entities`:

- `[ServerOwned]` — the client never sets it. On UPDATE the emitter excludes it from the SET list; on INSERT the emitter writes the in-memory value, which the pipeline has already reset to the platform's value (audit stamps, `IsActive = true`, `Node = null`, counts `0`). The platform bases apply it to `Id` (assigned, never edited), the four audit columns, `Node`, `SubtreeCount`, `ActiveSubtreeCount`, and `IsActive` (changed through the activate action, not through save).
- `[WriteOnce]` — the client sets it at creation only. Excluded from UPDATE SET lists; written on INSERT from the payload. Examples: `User.Subject`, `User.Email`.

`EntityMetadata.Properties[i].Ownership` is `Editable | WriteOnce | ServerOwned`; `EntityMetadata.ResetServerOwned(entity)` restores each server-owned property to the value a fresh instance has. Whether a silently-ignored change is *also* reported as a validation error is the pipeline's choice; the data layer guarantees it is never written.

**Rationale.** One row image, no second UDTT, no drift, and the guarantee lives in the layer that emits the SQL.

**Rejected.** Two UDTTs (create/update) — the `ForSave` failure mode; validation-only enforcement (a forgotten validator writes the column).

**Confidence.** High.

### D5 — Natural keys: `[NaturalKey]`, otherwise inferred from unique indexes; no natural key means surrogate-key export

**Decision.** `[NaturalKey]` on one property declares the entity's natural key (single-column in this release; composite natural keys are a later additive extension of the attribute with `Order`). Without it, `EntityMetadata.NaturalKey` is inferred in this order, over properties that are backed by a **unique** index in the EF model (uniqueness is read from the model, never assumed): (1) `Code`, (2) `Name`, (3) the first unique *required* string property in declaration order, (4) the first unique string property. If none qualifies, `NaturalKey` is null and export-for-import writes `Id` as the row key, which imports correctly within the same tenant and never across tenants — the Excel theme reports that as a warning on the sheet. Inference stops at uniqueness: a non-unique column cannot identify a row and would only produce ambiguity errors on import.

**Rationale.** The first consumer (Excel) needs a deterministic answer per entity and a way to override it in one attribute.

**Rejected.** Mandating a natural key on every entity (child tables and log-like entities have none); falling through to "first text column" (non-unique → ambiguous by construction).

**Confidence.** Medium. **Review flag:** `Code` before `Name` (the monolith's habit) versus `Name` first as the brain dump wrote.

### D6 — Enums are stored as strings by a platform convention, sized from the member names

**Decision.** The platform's `ConfigureConventions` applies `configurationBuilder.Properties<Enum>().HaveConversion<string>()`, and a finalizing convention (`EnumStringLengthConvention`) sets `IsUnicode(false)` and `HasMaxLength(n)` where `n` is the longest member name of the enum (minimum 8), unless the property already carries `[MaxLength]`/`[Unicode]` or fluent facets. The Queryex adapter declares the property as `QxString` with store type `QxVarChar(n)`. Nullable enums share the converter. A distro writes `public CenterType Type { get; set; }` and nothing else, and `Type = 'Service'` works in Queryex.

**Rationale.** Verified EF-recommended mechanism; zero distro lines; `varchar(n)` keeps seeks sargable against ASCII literals.

**Rejected.** Integer storage (Queryex filters would need magic numbers); per-enum configuration in each distro.

**Confidence.** High. The only unverified detail — whether `Properties<Enum>()` matches `TEnum?` — is pinned by a unit test in the fixture suite; if it does not, the convention enumerates nullable enum properties explicitly.

### D7 — Multilingual groups by naming convention: `X`, `X2`, `X3`

**Decision.** A string property `P` whose declaring entity also has string properties `P2` and `P3` forms the multilingual group `P` (`Name`, `Name2`, `Name3`; `Description`, `Description2`, `Description3`). `EntityMetadata.MultilingualGroups` lists them. Rules enforced at startup: `P2`/`P3` are nullable, all three share the same `MaxLength` and unicode facet. The settings theme uses the group list to gate `P2`/`P3` in the Queryex schema (D17); the Excel theme uses it to map "Arabic Name" columns.

**Rationale.** The convention is already the platform's; an attribute would be a second declaration of the same fact.

**Rejected.** `[Multilingual]` attribute (redundant); a JSON translations column (kills sargable filters on `Name2`).

**Confidence.** High.

### D8 — `EntityMetadata` is the single runtime description of an entity, built once from the EF model plus reflection

**Decision.** `IEntityMetadataProvider.Get(Type)` returns an immutable `EntityMetadata` built at startup for every mapped leaf: CLR type, table and schema, physical UDTT name (from `model.GetTableTypes()`), key property, ordered properties with column, store type, ownership, multilingual group, `IsUnique`; child collections (property, child metadata, parent-key property, supplied-flag accessor); natural key; flags `IsTemporal`, `IsTree`, `IsActivatable`; the tree properties; a topological table order for inserts and its reverse for deletes. Building it also validates: every capability-declared property is mapped; `[ParentKey]` has an FK to a top-level table; a `[NotMapped]` child list pairs with exactly one FK; tree entities have a unique index on `Node`; multilingual facets agree. Failures are aggregated into the host's startup validation.

**Rationale.** The emitter, the materializer, the allocator, the Excel codec, the pipeline and the capability recipes all need the same facts; computing them once and validating once is what makes "declare once" true.

**Confidence.** High.

### D9 — The ID allocator reserves exactly what a save needs, on a round trip the save already makes, and returns unused ranges to an in-process buffer

**Decision.** `IIdAllocator` (singleton, one instance per process) keeps a small buffer of contiguous ranges per (tenant database identity, sequence). The pipeline asks it, before the first round trip of a save, to contribute reservation statements to that round trip for each table's deficit (`rows with Id == default` minus `buffered`). The statement per sequence is:

```sql
DECLARE @tb1_f sql_variant, @tb1_l sql_variant;
EXEC sys.sp_sequence_get_range N'[gl].[sq_Centers]', @tb1_n, @tb1_f OUTPUT, @tb1_l OUTPUT;
SELECT CAST(@tb1_f AS int) AS [First], CAST(@tb1_l AS int) AS [Last],
       (SELECT MAX([Id]) FROM [gl].[Centers]) AS [MaxId];
```

The reservation is **not** consumed until the pipeline calls `AssignIds(rows)` after validation passed; a save that fails validation leaves the range in the buffer, so the next save on this instance consumes it and no gap is produced. `AssignIds` assigns ids to every new row in topological order and wires `[ParentKey]` values of new children to their new parents. A pipeline that has no pre-persist round trip (nothing to validate, nothing to pre-check) pays one dedicated reservation round trip; the pipeline theme decides whether that case exists.

**Self-healing.** If `First <= MaxId` the sequence is behind an out-of-band insert: the allocator discards the range, emits `ALTER SEQUENCE [gl].[sq_Centers] RESTART WITH <MaxId + 1>` plus a fresh reservation in one extra round trip, logs and raises `tellma.data.ids.desync` (a counter that should be alerted on). A persist that fails with a PK violation (2627) on a table the allocator serves triggers the same heal once and the persist is retried once with re-assigned ids (intra-batch parent keys rewired by `AssignIds`).

**Sizing.** No prefetch, no headroom: the deficit is the range size. The buffer holds only leftovers from failed saves and is bounded per sequence (default 1 024 ids; beyond that the allocator drops the excess and logs). Gaps therefore come from process crashes with leftovers (bounded by the buffer cap) and from the engine's own sequence cache on a SQL crash (default cache; a distro may `ALTER SEQUENCE … CACHE 50` in a raw migration).

**Who assigns.** The data layer (`IIdAllocator.AssignIds`); the pipeline decides *when* (after validation, before persist). A migrator seed uses the same path.

**Rationale.** Exact reservation on an existing round trip is the only design that has zero extra round trips *and* minimal gaps; a background prefetcher trades gaps for nothing once reservation is free.

**Rejected.** Background prefetch of large ranges (gaps on every restart, id exhaustion risk on `int`); `NEXT VALUE FOR … OVER (ORDER BY)` inside `INSERT … SELECT` (children need parent ids before the TVP is built; inserts would have to return mappings); `bigint` everywhere (storage and index cost for no benefit once gaps are small).

**Confidence.** High on the mechanism; medium on the "one dedicated round trip when nothing precedes persist" case, which depends on the pipeline's connect-collapse decision.

### D10 — `DataBatch`: one round trip, built from typed statements, executed as one command with concatenated text and `NextResult()`

**Decision.** `ITenantDatabase.CreateBatch()` returns an `IDataBatch`. Statements are added through typed methods (D11 lists the API), each yielding a `BatchResult<T>` handle whose `Value` is available after `ExecuteAsync`. The executor:

1. Asks every registered `IDataBatchContributor` (scoped DI; ordered) to add prologue statements (the user theme's connect and tag reads) and epilogue statements (the settings theme's tag bumps, computed from the batch's `WrittenTables`).
2. Assigns each statement a **batch ordinal** `b`; Queryex statements compile with `BatchOrdinal = b` (names `@qx{b}_…`); every other statement's parameters are named `@tb{b}_p{n}` (scalars) and `@tb{b}_t{n}` (TVPs) — `@tb` is the platform's second reserved prefix, next to Queryex's `@qx`.
3. Concatenates the texts. If any statement is a write (declares `WritesTables` or is a save/delete/lease statement) and `TransactionMode` is `Auto` (default), the text is wrapped: `SET XACT_ABORT ON; SET NOCOUNT ON; BEGIN TRAN; … COMMIT;`. `TransactionMode.None` leaves autocommit per statement (for idempotent bookkeeping such as lease renewal); `TransactionMode.Explicit` opens a `SqlTransaction` from C#, executes the batch inside it, and commits from C# — the escape hatch for a pipeline that must decide in C# before committing, at the cost of two more round trips.
4. Executes one `SqlCommand` (`CommandType.Text`, one `SqlParameter` per scalar, `SqlDbType.Structured` with the **physical** UDTT name per TVP, bound as streaming `IEnumerable<SqlDataRecord>` built from `TableTypeDefinition.Columns` order) and walks result sets with `NextResult()`, handing each to the statement that declared it. A statement declares how many result sets it produces; a mismatch at the end (`NextResult()` still true, or a reader starved) is an internal error.
5. Counts the round trip on the scoped `DataAccessScope` (D22) and records the meters.

**Retry.** Errors are classified from `SqlException.Number` using the driver's `SqlConfigurableRetryFactory` baseline transient list plus 1205 and 1222:

- *Reported* transient failure (the server returned an error; with `XACT_ABORT ON` the transaction is rolled back and the batch aborted): the whole round trip is re-executed, regardless of idempotence, up to 3 attempts with exponential backoff and jitter. Deadlock victims are the common case.
- *Ambiguous* failure (connection reset, timeout, `SqlException` with number 0/-2/10053/10054/233/997): re-executed only if **every** statement is `Idempotent`; otherwise the batch fails with `DataAccessAmbiguousException` carrying the original.
- `TransactionMode.None` batches are retried only if every statement is idempotent, in both classes (an autocommitted earlier statement cannot be un-run).

TVPs are re-enumerated on retry (the binder builds `SqlDataRecord`s from the source list each time).

**Error mapping.** The executor translates 2601/2627 into `UniqueConstraintViolationException(IndexName)`, 547 into `ForeignKeyViolationException(ConstraintName)`, 530 (recursion limit) into `TreeCycleException`, and the platform's own `THROW` numbers 50401 (concurrency) and 50403 (row-level security post-check) into `ConcurrencyConflictException(Ids)` and `RowSecurityException`. Mapping index names to field-level validation errors is the pipeline's job; the metadata exposes `EntityMetadata.UniqueIndexes` (name → properties) for it.

**Rationale.** Concatenated text sends a shared TVP once, gets one OpenTelemetry span with the whole text, allows the in-text transaction, and is what Queryex's `BatchOrdinal` was designed for. `SqlBatch` sends a TVP per command, has no retry provider, and shows only the last statement to telemetry.

**Rejected.** `SqlBatch`/`DbBatch` (above); `TransactionScope` (Serializable default, async flow suppressed, distributed on Linux throws); a stored-procedure façade (no logic in the database).

**Confidence.** High. **Review flag:** the 3-attempt reported-failure retry of a *non-idempotent* save relies on `XACT_ABORT ON` having rolled back; an error raised *after* `COMMIT` (impossible by construction because nothing follows `COMMIT` in the text) would break that reasoning, so the executor asserts at build time that `COMMIT` is the last statement.

### D11 — The `IDataBatch` API: typed statements, structural deduplication, declared writes

**Decision.** The consumer-facing interface (full C# in §3):

- `Query<TEntity>(EntityQuery<TEntity> query)` → `BatchResult<EntityQueryResult<TEntity>>` — a Queryex query materialised into entities plus a related-entity dictionary (D18).
- `Rows(QuerySpec spec, QueryArguments args)` → `BatchResult<RowSet>` — a Queryex query returned as arrays of arrays with `QueryexColumn` metadata (grids, exports).
- `Count(QuerySpec spec, QueryArguments args, int cap)` → `BatchResult<int>` — a capped grand total.
- `Save(SaveSpec spec)` → `BatchResult<SaveReceipt>` — D12.
- `Delete(DeleteSpec spec)` → `BatchResult<DeleteReceipt>` — D16.
- `Sql(FormattableString sql, IReadOnlySet<TableName> writes, bool idempotent, int resultSets)` → `BatchResult<RawResult>` — raw T-SQL whose interpolation holes become parameters (scalars, or TVPs for `IReadOnlyList<T>` of a `[TableType]` class) or identifiers (`SqlIdentifier`/`nameof`-helpers); the analyzer that polices this is D22.
- `Assert(QuerySpec countSpec, QueryArguments args, int expected, PlatformError error)` → a SQL assertion `IF (<count body>) <> @expected THROW <error>` inside the transaction.
- `Reserve(...)` is not public; the allocator adds its statements through the contributor mechanism.

**Deduplication.** `Query`, `Rows`, `Count` compute a structural key (root entity, select text, filter tree, having, order, skip, take, arguments by value, restrictions) and return the existing handle for an identical key — two validators asking for the same context share one statement. A `Query` whose select is a subset of an existing `Query` on the same root and filter is *not* merged (subset detection needs parsing the select; the engine's `Discover` makes it possible later and the key is designed to allow it). `Sql` dedups on (text, parameter values).

**Declared writes.** `WrittenTables` is the union of every statement's declared set; save/delete statements declare it from metadata; raw SQL must declare it. Contributors read it to append tag bumps.

**Confidence.** High on the shape; medium on the exact method set, which the pipeline theme will stretch.

### D12 — `SaveEmitter`: two TVPs per table, separate statements, aggregate-scoped concurrency, exact "touched" tracking

**Decision.** `SaveSpec` names the root entity type and the rows (`IReadOnlyList<TEntity>` with their `[NotMapped]` children populated or null). The emitter reads `EntityMetadata` and emits, per table in topological order (roots first, then each child collection), against **two TVPs** of the table's own UDTT — `@tb{b}_t{i}` for new rows (`Id` assigned, `IsNew` known in C#) and `@tb{b}_t{i+1}` for existing rows — plus, per child collection, an `IdList` TVP of the parents whose collection was supplied. The statements for a root table `T` with concurrency token `ModifiedAt`:

```sql
DECLARE @tb3_now datetime2(7) = SYSUTCDATETIME();
DECLARE @tb3_touched TABLE ([Id] int PRIMARY KEY);
DECLARE @tb3_conflicts TABLE ([Id] int PRIMARY KEY, [Reason] char(1));

-- 1. Early guard: rows the client says exist must exist with the stamp it loaded.
INSERT INTO @tb3_conflicts ([Id], [Reason])
SELECT s.[Id], CASE WHEN t.[Id] IS NULL THEN 'M' ELSE 'C' END
FROM @tb3_t1 s LEFT JOIN [gl].[Centers] t ON t.[Id] = s.[Id]
WHERE @tb3_override = 0 AND (t.[Id] IS NULL OR t.[ModifiedAt] <> s.[ModifiedAt]);
IF EXISTS (SELECT 1 FROM @tb3_conflicts)
    THROW 50401, /* JSON: {"ids":[…],"missing":[…]} capped at 2048 chars */ @tb3_msg, 1;

-- 2. Insert new roots (server-owned columns stamped here).
INSERT INTO [gl].[Centers] ([Id], [ParentId], [Type], [Name], …, [IsActive],
    [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById], [SubtreeCount], [ActiveSubtreeCount])
SELECT s.[Id], s.[ParentId], s.[Type], s.[Name], …, s.[IsActive],
    @tb3_now, @tb3_user, @tb3_now, @tb3_user, 0, 0
FROM @tb3_t0 s;

-- 3. Child collections (repeated per collection; parents = @tb3_t2 IdList):
DELETE c OUTPUT deleted.[ParentKeyColumn] INTO @tb3_touched
FROM [core].[RoleMemberships] c
WHERE c.[UserId] IN (SELECT [Id] FROM @tb3_t2)
  AND NOT EXISTS (SELECT 1 FROM @tb3_t3 s WHERE s.[Id] = c.[Id])
  AND NOT EXISTS (SELECT 1 FROM @tb3_t4 s WHERE s.[Id] = c.[Id]);
UPDATE c SET c.[RoleId] = s.[RoleId], c.[Notes] = s.[Notes]
OUTPUT inserted.[UserId] INTO @tb3_touched
FROM [core].[RoleMemberships] c JOIN @tb3_t4 s ON s.[Id] = c.[Id]
WHERE EXISTS (SELECT s.[RoleId], s.[Notes] EXCEPT SELECT c.[RoleId], c.[Notes]);
INSERT INTO [core].[RoleMemberships] ([Id], [UserId], [RoleId], [Notes])
OUTPUT inserted.[UserId] INTO @tb3_touched
SELECT s.[Id], s.[UserId], s.[RoleId], s.[Notes] FROM @tb3_t3 s;

-- 4. Root update, last: editable columns only; the stamp check is inside the UPDATE.
UPDATE t SET t.[ParentId] = s.[ParentId], t.[Type] = s.[Type], t.[Name] = s.[Name], …,
    t.[ModifiedAt] = @tb3_now, t.[ModifiedById] = @tb3_user
OUTPUT inserted.[Id]
FROM [gl].[Centers] t JOIN @tb3_t1 s ON s.[Id] = t.[Id]
WHERE (@tb3_override = 1 OR t.[ModifiedAt] = s.[ModifiedAt])
  AND (EXISTS (SELECT s.[ParentId], s.[Type], s.[Name], … EXCEPT SELECT t.[ParentId], t.[Type], t.[Name], …)
       OR t.[Id] IN (SELECT [Id] FROM @tb3_touched));
IF @@ROWCOUNT <> (SELECT COUNT(*) FROM @tb3_t1 s
                  WHERE EXISTS (SELECT s.[…] EXCEPT SELECT t.[…] FROM [gl].[Centers] t WHERE t.[Id] = s.[Id])
                     OR s.[Id] IN (SELECT [Id] FROM @tb3_touched))
    THROW 50401, N'{"ids":[]}', 1;   -- lost race between guard and update

SELECT @tb3_now;                       -- result set 1: the stamp
-- result set 2: the OUTPUT inserted.[Id] rows of statement 4 (ids that received the new stamp)
```

Rules encoded above:

- **Server-owned and write-once columns never appear in a SET list**; server-owned columns on INSERT are stamped (`CreatedAt`, `CreatedById`, `ModifiedAt`, `ModifiedById`) or taken from the in-memory row after the pipeline reset them.
- **Unchanged rows are skipped** (`EXCEPT` over the editable columns), so a temporal table gets no history row for a no-op; a child row that changed marks its parent *touched*, and a touched or changed root is stamped — so the aggregate root's `ModifiedAt` moves exactly when the aggregate changed, which is what makes the aggregate-level concurrency check sound.
- **The stamp is server time** (`SYSUTCDATETIME()` once per batch), read back as one scalar; the receipt tells C# which root ids received it, and the emitter stamps the in-memory entities (new rows: all four audit columns; stamped roots: `ModifiedAt`/`ModifiedById`), so the response can be served without re-reading audit columns.
- **The client's `ModifiedAt` is the expected stamp** carried in the existing-rows TVP's own `ModifiedAt` column; `@tb_override = 1` (the pipeline's concurrency override flag) disables both checks.
- **`OUTPUT … INTO` targets are table variables** (no triggers, no FKs), and every `OUTPUT` to the client comes from a table with no triggers — guaranteed by the no-logic-in-the-database rule.
- Tree tables add the recompute statements of D15 after step 4; activatable tree tables also recompute `ActiveSubtreeCount`.
- The emitter caches generated text per (entity metadata, statement kind); only parameter values vary.

**What it reads back:** the stamp, the stamped root ids, and — for the pipeline — nothing else by default; the pipeline appends its own read-back queries to the same batch.

**Rationale.** Separate statements sidestep every live MERGE defect; two TVPs make deleted-under-you a conflict; `EXCEPT` skipping is the only way to keep temporal history honest under import-merge; server-side stamping removes clock skew between instances.

**Rejected.** MERGE (defects); one TVP with `NOT EXISTS` (silent resurrection); always-update (history churn — 9 000 pointless history rows on a 10 000-row merge import with 1 000 changes); app-side stamp (skew); a standalone `(Id, ExpectedStamp)` TVP (the row image already carries the expected stamp in its `ModifiedAt` column, so a second TVP is redundant).

**Confidence.** High on shape; medium on the `THROW`-with-JSON conflict message (2 048-char cap — see D14 for the alternative).

### D13 — Bulk row binding is metadata-driven and streaming

**Decision.** `TableTypeBinder` (in `Tellma.Core.Data`) builds `SqlMetaData[]` once per `TableTypeDefinition` from its ordered `Columns` (store type, size, precision, scale, JSON flag) and yields `SqlDataRecord`s lazily from the entity list: each column reads the entity property through a compiled accessor keyed by column name; JSON-flagged columns serialise the property with the same STJ options EF's read-back uses; `hierarchyid` columns are excluded from tree UDTTs by `[ExcludeFromTableType]` on `Node`. Bulk-list TVPs (`IdList` etc.) bind through the same binder from their `[TableType]` class definitions. The binder addresses every type by its physical name from the app's own model. `SqlBulkCopy` is not used in this release; the import path chunks large saves at 10 000 root rows per round trip pair, and a staging-table variant is a later, measured addition.

**Confidence.** High.

### D14 — Optimistic concurrency: `ModifiedAt` is the token; no `rowversion`

**Decision.** The concurrency token of every top-level entity is `ModifiedAt` (`datetime2(7)`, UTC, server-stamped per D12). Rule: **user-visible mutations stamp it** (save; activate/deactivate; every capability action that changes the row), **bookkeeping never touches the entity row** (activity, tags, inbox tracking live in sibling tables; task lease columns live on the task row and lease statements update only those columns). The client echoes `ModifiedAt` as it loaded it; the wire format must preserve seven fractional digits (`"O"` round-trip format). Conflicts are reported as `ConcurrencyConflictException` with the conflicting ids and a `Missing` subset (rows deleted since load); the pipeline's override flag re-runs the save with `@tb_override = 1`. The UDTT's optional `rowversion` inclusion is unused because no platform table carries a `rowversion` column.

**Evaluation against `rowversion`.** `rowversion` bumps on *any* update, including the actions the brain dump wants excluded, and is opaque; it needs the same sibling-table discipline to be usable and still cannot let an action choose not to bump. Its one real advantage — uniqueness guaranteed by the server without clocks — `ModifiedAt` gets back by being server-stamped once per transaction (`SYSUTCDATETIME()` on one server, monotonic in practice; the astronomically unlikely equal-stamp case after a backwards clock step is accepted and documented). Both need a read-back of the new value (one scalar versus one column per row — `ModifiedAt` is cheaper). `ModifiedAt` is also user-visible data, so it costs no extra column.

**Rejected.** A hash of editable columns (recomputed on every read; breaks on column additions); `rowversion` (above); no token with last-writer-wins (silent loss).

**Confidence.** Medium-high. **Review flag:** the conflict ids travel in a `THROW` message (JSON, capped at 2 048 characters, `"truncated": true` beyond ~180 ids); the alternative — a conflict result set followed by `ROLLBACK; RETURN` — keeps every id but makes the executor's result-set walk stateful. The message route is chosen because a conflicting import is reported per row anyway after the pipeline re-queries the listed ids.

### D15 — Trees: `Node` computed in SQL from the id path, counts recomputed set-based, cycles validated in C# and fenced in SQL

**Decision.** `TreeEntity<TSelf>` (`TSelf` is the leaf, for the `Parent` navigation and the `List<TSelf>` ancestors the pipeline returns) declares `ParentId` (FK to self), `Node hierarchyid NOT NULL` (`[ServerOwned]`, `[ExcludeFromTableType]`, unique index `IX_<Table>_Node`), `SubtreeCount int NOT NULL` and — when the leaf is `IActivatable` — `ActiveSubtreeCount int NOT NULL` (both `[ServerOwned]`). `IsLeaf` and `Level` do not exist (`SubtreeCount = 1`, `level(Node)`). The `Node` of a row is **the path of ids**: `/<root id>/…/<id>/`, so a row's node depends only on its ancestor chain, siblings never renumber, and a moved subtree is the only thing that changes when a row is re-parented. Depth-first order by `Node` orders siblings by id.

After the save statements of a tree table, the emitter appends:

```sql
-- Re-path saved rows whose parent is not itself in the batch, and everything below them.
;WITH tb3_paths AS (
    SELECT t.[Id], CAST(ISNULL(p.[Node].ToString(), '/') + CAST(t.[Id] AS varchar(20)) + '/' AS nvarchar(4000)) AS [Path]
    FROM [gl].[Centers] t LEFT JOIN [gl].[Centers] p ON p.[Id] = t.[ParentId]
    WHERE t.[Id] IN (SELECT [Id] FROM @tb3_t0 UNION SELECT [Id] FROM @tb3_t1)
      AND (t.[ParentId] IS NULL OR t.[ParentId] NOT IN (SELECT [Id] FROM @tb3_t0 UNION SELECT [Id] FROM @tb3_t1))
    UNION ALL
    SELECT c.[Id], CAST(x.[Path] + CAST(c.[Id] AS varchar(20)) + '/' AS nvarchar(4000))
    FROM [gl].[Centers] c JOIN tb3_paths x ON c.[ParentId] = x.[Id]
)
UPDATE t SET t.[Node] = hierarchyid::Parse(x.[Path])
FROM [gl].[Centers] t JOIN tb3_paths x ON x.[Id] = t.[Id]
WHERE t.[Node] IS NULL OR t.[Node] <> hierarchyid::Parse(x.[Path])
OPTION (MAXRECURSION 32);

-- Recount every row whose counts changed (master-data sized tables; O(n log n) with the Node index).
UPDATE t SET t.[SubtreeCount] = x.[Cnt], t.[ActiveSubtreeCount] = x.[ActiveCnt]
FROM [gl].[Centers] t
CROSS APPLY (SELECT COUNT(*) AS [Cnt], SUM(CASE WHEN d.[IsActive] = 1 THEN 1 ELSE 0 END) AS [ActiveCnt]
             FROM [gl].[Centers] d WHERE d.[Node].IsDescendantOf(t.[Node]) = 1) x
WHERE t.[SubtreeCount] <> x.[Cnt] OR t.[ActiveSubtreeCount] <> x.[ActiveCnt];
```

New rows are inserted with `Node = hierarchyid::GetRoot()`-placeholder-free: the INSERT sets `Node` to a provisional `/-<id>/` value (unique, never equal to a real path) so the `NOT NULL` and unique constraints hold until the re-path statement runs in the same transaction. `MAXRECURSION` is the entity's `[Tree(MaxDepth = 32)]`; exceeding it (a cycle written under write skew, or a genuinely deeper tree) fails the batch with error 530, mapped to `TreeCycleException`, which rolls back — the fence that makes the C# check sufficient.

**C# cycle validation** (a validator the pipeline runs, shipped by this theme as `TreeCycleValidator<T>`): in the validation round trip it adds `Rows(Id, ParentId)` over the whole tree table (two ints per row; master data), overlays the batch's `ParentId` values, and walks up from every saved row; a revisit is a validation error on that row's `ParentId`. Delete-with-descendants and activate/deactivate recompute counts through the same second statement.

**Rationale.** Id paths remove `ROW_NUMBER` renumbering and make the affected set exactly "saved rows and their descendants"; the recursive CTE plus `ROW_NUMBER`-free build is the research recommendation; the recount is written to touch only rows whose values change, so temporal churn is zero for untouched rows.

**Rejected.** In-memory recomputation (needs the whole subtree in C# and per-row `GetDescendant`); `ROW_NUMBER` sibling ordinals (renumber on delete); `Level` and `IsLeaf` columns (derivable, no query they make cheaper that the `Node` index does not); a persisted computed `Level` column for a breadth-first index (children are found by `ParentId`, which is indexed by the FK).

**Confidence.** Medium-high. **Review flag:** whole-table recount versus an affected-ancestors recount; the former is simpler and correct, the latter matters only for trees above ~100 000 rows, which no reference entity approaches.

### D16 — Deletes: explicit child deletes, ids or query or descendants, count read back

**Decision.** `DeleteSpec` has three shapes — `ByIds(IReadOnlyList<TKey>)`, `ByQuery(QuerySpec filterOnlySpec)`, `WithDescendants(IReadOnlyList<TKey>)` (tree only). The emitter deletes child tables first (reverse topological order) then the root, every statement of the form `DELETE c FROM [core].[RoleMemberships] c WHERE c.[UserId] IN (SELECT [Id] FROM @tb4_t0)`, with the root `DELETE … OUTPUT deleted.[Id]` so the receipt reports which requested ids were actually deleted (missing = not found or filtered by row-level security, which the pipeline reports uniformly). `ByQuery` embeds the compiled query body (D19) as `IN (SELECT [Id] FROM (<body>) q)`; `WithDescendants` uses `WHERE EXISTS (SELECT 1 FROM [gl].[Centers] a JOIN @tb4_t0 s ON s.[Id] = a.[Id] WHERE t.[Node].IsDescendantOf(a.[Node]) = 1)` and appends the count recompute. No `ON DELETE CASCADE` anywhere: a delete of a tree row that still has children fails with 547 (`ForeignKeyViolationException`), which the pipeline turns into a validation error. Deletes bump the entity's cache tag through the same contributor path as saves.

**Confidence.** High.

### D17 — The Queryex schema is built from the EF model per language profile and cached by that key

**Decision.** `QueryexSchemaFactory.Get(SchemaProfile profile)` builds (once, then caches by `profile` value equality; the cache holds at most one schema per distinct profile, of which there are at most three per model) a `QueryexSchema` from the runtime `IModel`:

- **Entities:** every mapped leaf entity type, logical name = CLR type name (`User`, `RoleMembership`, `Center`), `Source` = `[schema].[Table]`, `Key` = the PK property.
- **Properties:** every mapped scalar property whose store type has a Queryex type, with `Column`, `IsNotNull` from the model, `IsUnique` when a single-column unique index or the PK covers it, `StoreType` structured from EF's `RelationalTypeMapping` (enums via their converter's store type, D6). `[NotMapped]` members, period shadow columns, `varbinary`, `json`, `time`, `float`/`real` are not declared. The multilingual members `P2`/`P3` are declared only when `profile.LanguageCount` is 2/3 respectively.
- **Navigations:** every many-to-one EF foreign key on the entity, named by the CLR navigation when one exists, otherwise by the FK property name minus its `Id` suffix (`CustomerId` → `Customer`; a collision with an existing member is a startup error). A `[ParentKey]` FK yields the child's navigation to its parent the same way (`RoleMembership.User`).
- **Tree node:** the `Node` property of a `TreeEntity`.

`SchemaProfile` is a record `(int LanguageCount)` today; the settings theme supplies it through the request context, and anything that later reshapes the schema per tenant (a hidden module, a disabled feature) is added as a field. Row-level-security composition and child-entity path rewriting are `FilterTree` transformations applied per request by the permissions theme and never change the schema. The engine's own caches key on the schema instance, so a profile that has never been seen builds a schema and warms its own cache; a profile already built hits warm caches immediately.

A `Dictionary<EntityDescriptor, EntityMetadata>` built alongside the schema lets the materializer map descriptors back to CLR types without an engine amendment.

**Rationale.** The schema is identity-cached inside the engine; building it per tenant would defeat every engine cache, building it once would leak `Name3` across tenants. Per profile is the smallest correct key.

**Confidence.** High.

### D18 — Entity queries and the materializer: bare paths, auto-appended keys, related entities by (entity, id)

**Decision.** Two host-level query shapes over one `QuerySpec`:

- `Rows(spec)` — anything Queryex compiles; returns `RowSet` (`IReadOnlyList<object?[]>` plus `IReadOnlyList<QueryexColumn>`), used by grids, exports and counts. Values are read tolerantly per `QueryexType` and normalised to one CLR type per Queryex type (`Numeric` → `decimal`, `Bool` → `bool`, dates as `DateOnly`/`DateTime`/`DateTimeOffset`).
- `Query<TEntity>(EntityQuery<TEntity>)` — `Select` is restricted to bare paths (a computed item is a caller error); the host first runs `Discover` on the select text (cached by the engine on text) to learn the paths, then appends, for every navigation prefix used, its key path (`Customer.Id`) and the FK on its owner (`CustomerId`, `Customer.CountryId`), plus the root `Id`. The materializer walks `QueryexColumn.Path`: depth-1 columns land on the root instance (converted through the property's EF value converter, so enum strings become enums); deeper columns land on a related instance of the CLR type behind the path's descriptor, keyed by (logical entity name, id) in `EntityQueryResult.Related`, deduplicated across rows. Related instances are partial (only selected columns populated); a partial instance never travels back into a save.
- `EntityQuery<TEntity>.Children` names child collections to load; for each, the host appends a second `Query` over the child entity restricted by `ParentKey ∈ @ids` (D19) when the root ids are known up front (details, get-by-ids, import hydration), and stitches children onto their parents after execution. When ids are not known up front (a filtered page), children are not loadable in the same round trip and the API refuses the combination at build time rather than issuing a hidden second round trip.
- Tree pages: `EntityQuery.IncludeAncestors = true` appends the `ancestorOf(Id, @k…)` query of spec 0008 §10.10 in the same batch only when the page's ids are known; otherwise the pipeline issues it as a second round trip — the one case where a grid read costs two.

**Rejected.** An `Expand` string in the Queryex sense (spec 0008 deliberately has no `*` expansion; a select list of paths already says what to load); loading children by a second round trip silently.

**Confidence.** High for materialisation; medium for the "refuse instead of a hidden round trip" rule (review flag: a pipeline author may prefer the convenience).

### D19 — Queryex engine amendments this theme needs (documented in spec 0011 because 0008 is frozen)

**Decision.** Four additive changes (no language-version bump; `QueryexLanguage.Version` stays 1):

1. **Key-set restriction through a TVP.** `QuerySpec.Restrictions : IReadOnlyList<KeySetRestriction>` where `KeySetRestriction(string Path, string TableParameter)`. The engine binds `Path` in Filter mode (bare path, joins as needed), validates `TableParameter` is a plain identifier outside the `@qx` namespace, and emits `AND <column> IN (SELECT [Id] FROM @<TableParameter>)` into the `WHERE` (conjoined after the filter tree). The host binds the TVP (`IdList`/`BigIdList`/`GuidList`/`StringList`). Restrictions participate in the L3 cache key. Used by get-by-ids, get-by-parent-ids, child loading, delete-by-ids, validation context, natural-key translation.
2. **`level(node: HierarchyId) -> Numeric`**, `NotNull` when its argument is, emitted as `.GetLevel()`. Additive registry entry.
3. **Body/prologue split.** `CompiledQuery` gains `Prologue` (the hoisted `DECLARE` lines; may be empty) and `Body` (the `SELECT` statement); `Sql` remains `Prologue + Body`. A host embeds `Body` as a derived table or inside `IF (…)` only when the query has no paging, which the engine reports through `CompiledQuery.IsEmbeddable`. Used by the row-security post-check assertion and delete-by-query.
4. **`QueryexColumn.Nullity` for restricted keys** — no change; noted only because restriction does not alter nullity.

**Rejected.** A language-level `x in (@ids)` multi-valued parameter (the brain dump's own preference for an engine feature stands; a language feature would need a version and a type for list parameters); a `descendantOf` over a TVP (not needed — deletes and tree loads use restrictions on `Id`/`ParentId`).

**Confidence.** High.

### D20 — Context values are bound from the request context; the data layer computes none of them

**Decision.** `ITenantDatabase` is created per request (or per background scope) from `IRequestContext` (host theme) and carries `QueryexContextValues { DateOnly Today; DateTimeOffset Now; int? UserId; string TimeZoneName; }`. The executor binds `Today`/`Now`/`UserId`/`TimeZone` slots from it and `Literal`/`Declared` slots from the compiled query and the caller's `QueryArguments` (a name→value dictionary; a declared parameter bound against several store types binds every slot with the same name from one value). `Today` is the calendar date in the *user's* zone as the web theme resolves it from headers; `TimeZoneName` is the tenant zone in the backend's naming as the settings theme supplies it. Background scopes copy the same record into their `ITenantDatabase`; nothing here uses `AsyncLocal`.

**Confidence.** High.

### D21 — Lease columns and lease statements the emitter can produce (semantics owned by the background-tasks theme)

**Decision.** `ILeasable` is a capability interface with four properties every task-shaped entity declares by inheriting `LeasableColumns` (a partial-friendly base is not possible across capabilities, so the interface lists them and the recipe validates them): `LeaseOwner uniqueidentifier NULL`, `LeaseExpiresAt datetime2(3) NULL`, `LeaseToken bigint NOT NULL DEFAULT 0` (fencing token, incremented on every acquisition), `Attempts int NOT NULL DEFAULT 0`. All four are `[ServerOwned]`, and — by the bookkeeping rule — the lease statements update only these columns and never `ModifiedAt`. `LeaseStatements<T>` (in `Tellma.Core.Data`) exposes four batch statements built from metadata:

```sql
-- Acquire: ordered, skip-locked, bounded, fenced. @tb0_p0 = batch size, @tb0_p1 = now, @tb0_p2 = owner, @tb0_p3 = lease until.
;WITH tb0_due AS (
    SELECT TOP (@tb0_p0) t.*
    FROM [core].[ExportJobs] t WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE t.[State] = 'Queued' AND (t.[LeaseExpiresAt] IS NULL OR t.[LeaseExpiresAt] < @tb0_p1)
    ORDER BY t.[DueAt], t.[Id])
UPDATE tb0_due SET [LeaseOwner] = @tb0_p2, [LeaseExpiresAt] = @tb0_p3,
    [LeaseToken] = [LeaseToken] + 1, [Attempts] = [Attempts] + 1
OUTPUT inserted.[Id], inserted.[LeaseToken];
-- Renew (idempotent): UPDATE … SET [LeaseExpiresAt] = @until WHERE [Id] IN (SELECT [Id] FROM @ids) AND [LeaseOwner] = @owner AND [LeaseToken] = … ;
-- Release / Fail: same fenced predicate; the State transition columns are the consumer's.
```

`Acquire` is `Idempotent = false`, `Renew` is `Idempotent = true`, and both declare `WritesTables` so tag contributors see them (the settings theme decides that task tables bump nothing). `READPAST` is paired with `UPDLOCK` because a bare `READPAST` is a no-op under RCSI; `ROWLOCK` limits escalation but does not prevent it, so the batch size is capped at 1 000 by the statement.

**Confidence.** Medium (the background theme owns the state column and may reshape the predicate).

### D22 — Instruments, the DB-call budget, and the two analyzers

**Decision.** Meter `Tellma.Core.Data` (`IMeterFactory`), instrument names as `const`s in `Tellma.Core.Data.Abstractions.DataMeter`:

| Instrument | Kind | Unit | Tags |
|---|---|---|---|
| `tellma.data.roundtrips` | histogram per scope | `{roundtrip}` | `operation` (the pipeline's operation name), `entity` |
| `tellma.data.roundtrip.duration` | histogram | `s` | `operation`, `retried` |
| `tellma.data.batch.statements` | histogram | `{statement}` | `operation` |
| `tellma.data.retries` | counter | `{retry}` | `class` (`reported`/`ambiguous`), `error` |
| `tellma.data.ids.reserved` / `tellma.data.ids.returned` / `tellma.data.ids.desync` | counters | `{id}` / `{id}` / `{event}` | `sequence` |
| `tellma.data.concurrency.conflicts` | counter | `{conflict}` | `entity` |
| `tellma.data.tree.recomputes` | counter | `{statement}` | `entity` |
| `tellma.data.rows.saved` / `tellma.data.rows.deleted` | counters | `{row}` | `entity` |

No per-tenant tags. `DataAccessScope` (scoped DI; copied into background scopes) accumulates the round-trip count for the current operation and stamps `tellma.data.roundtrips` on the request `Activity` when the scope ends; the budget assertion in tests uses `SqlConnection.RetrieveStatistics()["ServerRoundtrips"]` as an independent oracle.

Analyzers, shipped in `Tellma.Core.Analyzers` and referenced by every distribution through the platform targets:

- `TELLMA0001` — a `SqlDataRecord` is constructed, or `SqlDataRecord.Set*`/`GetOrdinal` is called, outside a type marked `[TableTypeBinder]`; hard-coded ordinal binding is the defect this catches.
- `TELLMA0002` — a raw `Sql(...)` batch statement whose text (parsed with `Microsoft.SqlServer.TransactSql.ScriptDom`) contains `INSERT`/`UPDATE`/`DELETE`/`MERGE`/`EXEC` and declares no `writes:`, or whose interpolation holes are not `SqlIdentifier`, `nameof`, or a value (which becomes a parameter). `MERGE` anywhere is its own diagnostic.
- `TELLMA0003` — `DbContext.SaveChanges`/`SaveChangesAsync` invoked outside the migrator project.

**Confidence.** High.

### D23 — Tests: fixture entities on LocalDB, three tiers

**Decision.** `test/core/Tellma.Core.Data.Tests` (unit: emitter golden SQL per statement kind against fixture metadata, allocator buffer arithmetic, metadata validation errors, materializer routing, natural-key inference, enum sizing, retry classification) and `test/core/Tellma.Core.Data.IntegrationTests` (`Category=Integration`; LocalDB on Windows, Testcontainers SQL Server on Linux, both through one connection-string environment variable) with a fixture `DbContext` and migrations for schema `fixture`: `Widgets` (top-level, `[Temporal]`, `IActivatable`, natural key `Code`, enum `Kind`, multilingual `Name`), `WidgetParts` (child of `Widgets`), `WidgetPartNotes` (grandchild), `Nodes` (`TreeEntity<Node>`, `IActivatable`), `Jobs` (`ILeasable`). Integration tests assert: round-trip budgets per operation (statistics oracle), skip-unchanged writes no history row, aggregate stamp moves when only a child changes, deleted-under-you is a conflict, override writes, sequence desync heals once, two racing saves produce exactly one conflict, cycle under write skew fails in SQL, id-path nodes survive re-parenting, `READPAST`+`UPDLOCK` acquisition never double-leases across two connections, `hierarchyid` never crosses the TVP boundary, `Properties<Enum>()` matches nullable enums.

**Confidence.** High.

---

## 3. Contracts

### 3.1 Entity contract — `Tellma.Core.Abstractions.Entities`

```csharp
namespace Tellma.Core.Abstractions.Entities;

/// <summary>The root of every persisted class: a keyed row image. Storage shape and wire shape are the same class.</summary>
public abstract class Entity<TKey> where TKey : struct, IEquatable<TKey>
{
    /// <summary>The surrogate key, assigned by the platform before persistence; default means "new".</summary>
    [Key, ServerOwned] public TKey Id { get; set; }
}

/// <summary>An aggregate root: the unit of save, permission and concurrency. Carries the four audit columns.</summary>
public abstract class TopLevelEntity<TKey> : Entity<TKey> where TKey : struct, IEquatable<TKey>
{
    /// <summary>UTC instant the row was inserted; stamped by the persist statement.</summary>
    [ServerOwned] public DateTime CreatedAt { get; set; }
    /// <summary>The user that inserted the row.</summary>
    [ServerOwned] public int CreatedById { get; set; }
    /// <summary>UTC instant of the last user-visible mutation; also the optimistic-concurrency token the client echoes.</summary>
    [ServerOwned, ConcurrencyToken] public DateTime ModifiedAt { get; set; }
    /// <summary>The user that performed the last user-visible mutation.</summary>
    [ServerOwned] public int ModifiedById { get; set; }
}

/// <summary><see cref="TopLevelEntity{TKey}"/> with an <see cref="int"/> key — the default.</summary>
public abstract class TopLevelEntity : TopLevelEntity<int> { }

/// <summary>A row owned by a top-level entity and saved only with it; carries no audit columns of its own.</summary>
public abstract class ChildEntity<TKey> : Entity<TKey> where TKey : struct, IEquatable<TKey> { }

/// <summary><see cref="ChildEntity{TKey}"/> with an <see cref="int"/> key — the default.</summary>
public abstract class ChildEntity : ChildEntity<int> { }

/// <summary>A self-referencing hierarchy. <typeparamref name="TSelf"/> is the leaf, so the parent navigation is typed to it.</summary>
public abstract class TreeEntity<TSelf> : TopLevelEntity where TSelf : TreeEntity<TSelf>
{
    /// <summary>The parent row, or null for a root.</summary>
    public int? ParentId { get; set; }
    /// <summary>The parent, for typed access in LINQ and Queryex.</summary>
    public TSelf? Parent { get; set; }
    /// <summary>The hierarchy node: the path of ids from the root. Computed in SQL; never bound into a table type.</summary>
    [ServerOwned, ExcludeFromTableType, TreeNode] public HierarchyId Node { get; set; } = null!;
    /// <summary>Count of this row and all its descendants; 1 for a leaf.</summary>
    [ServerOwned] public int SubtreeCount { get; set; }
    /// <summary>Count of active rows among this row and its descendants; present only when the leaf is <see cref="IActivatable"/>.</summary>
    [ServerOwned] public int ActiveSubtreeCount { get; set; }
}

/// <summary>The activatable capability: a row that can be switched off without being deleted.</summary>
public interface IActivatable
{
    /// <summary>Whether the row is active. Changed through the activate action, never through save.</summary>
    [ServerOwned] bool IsActive { get; set; }
}

/// <summary>The lease capability of a task-shaped row; the four columns are updated only by lease statements.</summary>
public interface ILeasable
{
    Guid? LeaseOwner { get; set; }
    DateTime? LeaseExpiresAt { get; set; }
    long LeaseToken { get; set; }
    int Attempts { get; set; }
}

/// <summary>Makes a top-level table system-versioned with <c>ValidFrom</c>/<c>ValidTo</c> and a <c>&lt;Table&gt;History</c> table; children follow their parent unless they opt out.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class TemporalAttribute(bool enabled = true) : Attribute { public bool Enabled { get; } = enabled; }

/// <summary>Marks the foreign key of a child entity that identifies its owning top-level row.</summary>
[AttributeUsage(AttributeTargets.Property)] public sealed class ParentKeyAttribute : Attribute { }

/// <summary>Disambiguates which child foreign key a <c>[NotMapped]</c> child collection follows when the child has several to the same parent.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ChildrenAttribute(string parentKeyProperty) : Attribute { public string ParentKeyProperty { get; } = parentKeyProperty; }

/// <summary>A column the client never sets: excluded from every UPDATE; on INSERT the platform's value is written.</summary>
[AttributeUsage(AttributeTargets.Property)] public sealed class ServerOwnedAttribute : Attribute { }

/// <summary>A column the client sets at creation only: excluded from every UPDATE.</summary>
[AttributeUsage(AttributeTargets.Property)] public sealed class WriteOnceAttribute : Attribute { }

/// <summary>The optimistic-concurrency token; exactly one per top-level entity, applied by the platform base.</summary>
[AttributeUsage(AttributeTargets.Property)] public sealed class ConcurrencyTokenAttribute : Attribute { }

/// <summary>Declares the property that identifies a row to humans and to import/export; overrides inference.</summary>
[AttributeUsage(AttributeTargets.Property)] public sealed class NaturalKeyAttribute : Attribute { }

/// <summary>Marks the hierarchy-node property of a tree entity.</summary>
[AttributeUsage(AttributeTargets.Property)] public sealed class TreeNodeAttribute : Attribute { }

/// <summary>Per-entity tree options.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class TreeAttribute : Attribute { /// <summary>Depth cap enforced by the recompute statement; default 32.</summary> public int MaxDepth { get; init; } = 32; }
```

`HierarchyId` is the type from `Microsoft.EntityFrameworkCore.SqlServer.Abstractions`; `Tellma.Core.Abstractions` takes that one package reference (it is EF-provider-free and BCL-shaped — review flag: the alternative is a platform-owned `TreePath` value type converted by EF, which keeps Abstractions pure at the cost of a converter). `ExcludeFromTableTypeAttribute` moves from `Tellma.Core.EntityFrameworkCore` to `Tellma.Core.Abstractions.TableTypes` (type-forwarded) so a platform base can apply it.

### 3.2 Data access API — `Tellma.Core.Data.Abstractions`

```csharp
namespace Tellma.Core.Data.Abstractions;

/// <summary>The current tenant's database for the current request or background scope: the door to every round trip.</summary>
public interface ITenantDatabase
{
    /// <summary>The Queryex schema for this tenant's language profile.</summary>
    QueryexSchema Schema { get; }
    /// <summary>The context values bound into <c>today()</c>, <c>now()</c>, <c>me()</c> and the tenant zone.</summary>
    QueryexContextValues Context { get; }
    /// <summary>Starts an empty batch; nothing touches the network until <see cref="IDataBatch.ExecuteAsync"/>.</summary>
    IDataBatch CreateBatch();
}

/// <summary>Creates <see cref="ITenantDatabase"/> instances outside a request (background scopes, the migrator's seeding).</summary>
public interface ITenantDatabaseFactory
{
    /// <summary>Opens the database of a tenant for a scope carrying the given context values.</summary>
    ITenantDatabase Open(TenantId tenant, QueryexContextValues context);
}

/// <summary>The values a host binds into a compiled query's context slots.</summary>
public sealed record QueryexContextValues(DateOnly Today, DateTimeOffset Now, int? UserId, string TimeZoneName);

/// <summary>Whether and how the batch text is wrapped in a transaction.</summary>
public enum TransactionMode
{
    /// <summary>Wrap in <c>SET XACT_ABORT ON; BEGIN TRAN … COMMIT</c> when any statement writes; otherwise no transaction. The default.</summary>
    Auto,
    /// <summary>No transaction: every statement autocommits. Only for batches whose statements are all idempotent.</summary>
    None,
    /// <summary>An explicit <c>SqlTransaction</c> opened and committed from C#; two extra round trips. The escape hatch for decisions that must be taken in C# before commit.</summary>
    Explicit,
}

/// <summary>One round trip under construction: typed statements whose results become available after execution.</summary>
public interface IDataBatch
{
    /// <summary>The transaction policy; <see cref="TransactionMode.Auto"/> unless changed before execution.</summary>
    TransactionMode TransactionMode { get; set; }
    /// <summary>Every table any statement declared it writes; contributors read this to append tag bumps.</summary>
    IReadOnlySet<TableName> WrittenTables { get; }
    /// <summary>Adds an entity query (bare paths, materialised) or returns the handle of a structurally identical one already added.</summary>
    BatchResult<EntityQueryResult<TEntity>> Query<TEntity>(EntityQuery<TEntity> query) where TEntity : class;
    /// <summary>Adds a row query (any select items, arrays of arrays), deduplicated structurally.</summary>
    BatchResult<RowSet> Rows(QuerySpec spec, QueryArguments? arguments = null);
    /// <summary>Adds a capped grand total over the spec's filter, deduplicated structurally.</summary>
    BatchResult<int> Count(QuerySpec spec, QueryArguments? arguments = null, int cap = 10_000);
    /// <summary>Adds the persist statements of a save: root upsert, child synchronisation, stamps, tree maintenance.</summary>
    BatchResult<SaveReceipt> Save(SaveSpec spec);
    /// <summary>Adds the delete statements of a delete spec: children first, then roots, ids read back.</summary>
    BatchResult<DeleteReceipt> Delete(DeleteSpec spec);
    /// <summary>Adds a SQL assertion evaluated inside the transaction: the count must equal <paramref name="expected"/> or the batch fails with <paramref name="error"/>.</summary>
    void Assert(QuerySpec countSpec, QueryArguments? arguments, int expected, PlatformError error);
    /// <summary>Adds raw T-SQL. Holes become parameters (scalars, or table-valued parameters for lists of table-type rows) or identifiers (<see cref="SqlIdentifier"/>); text is never concatenated with user values.</summary>
    BatchResult<RawResult> Sql(FormattableString sql, IReadOnlySet<TableName>? writes = null, bool idempotent = false, int resultSets = 0);
    /// <summary>Executes the round trip, with retry per the platform policy, and completes every handle.</summary>
    Task ExecuteAsync(CancellationToken cancellationToken);
}

/// <summary>A result handle; <see cref="Value"/> throws until the batch has executed.</summary>
public sealed class BatchResult<T>
{
    /// <summary>The value, once the batch has executed.</summary>
    public T Value { get; }
    /// <summary>Whether the batch has executed.</summary>
    public bool IsCompleted { get; }
}

/// <summary>Adds statements to every batch before it executes: connect and tag reads at the front, tag bumps at the back.</summary>
public interface IDataBatchContributor
{
    /// <summary>Ordering among contributors; prologues run ascending, epilogues descending.</summary>
    int Order { get; }
    /// <summary>Called once per batch after the caller's statements are in place and before text is assembled.</summary>
    void Contribute(IDataBatch batch, DataBatchStage stage);
}

/// <summary>Where a contributor is being asked to add statements.</summary>
public enum DataBatchStage { Prologue, Epilogue }

/// <summary>A schema-qualified table name; the unit of "what a statement writes".</summary>
public readonly record struct TableName(string Schema, string Name);

/// <summary>An identifier interpolated into raw SQL; bracket-quoted by the batch, never a value.</summary>
public readonly record struct SqlIdentifier(string Name);

/// <summary>Declared-parameter values for a Queryex query, by name.</summary>
public sealed class QueryArguments : Dictionary<string, object?> { }

/// <summary>A row-shaped result: values by column ordinal, with the compiled column metadata.</summary>
public sealed record RowSet(IReadOnlyList<QueryexColumn> Columns, IReadOnlyList<object?[]> Rows);

/// <summary>An entity-shaped query over one root.</summary>
public sealed record EntityQuery<TEntity> where TEntity : class
{
    /// <summary>Bare paths only; keys and foreign keys of navigated entities are appended automatically.</summary>
    public required string Select { get; init; }
    public FilterTree? Filter { get; init; }
    public string? OrderBy { get; init; }
    public int? Skip { get; init; }
    public int? Take { get; init; }
    public QueryArguments? Arguments { get; init; }
    /// <summary>Restrict the root to a key set carried by a table-valued parameter (ids, parent ids, codes).</summary>
    public IReadOnlyList<KeySetRestriction> Restrictions { get; init; } = [];
    /// <summary>Child collections to load in the same round trip; allowed only when a key-set restriction fixes the root ids.</summary>
    public IReadOnlyList<string> Children { get; init; } = [];
}

/// <summary>The result of an entity query: root instances plus every related instance the select reached, keyed by (entity, id).</summary>
public sealed record EntityQueryResult<TEntity>(IReadOnlyList<TEntity> Entities, RelatedEntities Related) where TEntity : class;

/// <summary>Related instances by logical entity name then key; partial (only selected columns populated).</summary>
public sealed class RelatedEntities : IReadOnlyDictionary<string, IReadOnlyDictionary<object, object>> { }

/// <summary>The rows of one save: aggregate roots with their supplied child collections.</summary>
public sealed record SaveSpec(Type EntityType, IReadOnlyList<object> Rows, bool OverrideConcurrency = false)
{
    /// <summary>Typed factory.</summary>
    public static SaveSpec For<TEntity>(IReadOnlyList<TEntity> rows, bool overrideConcurrency = false) where TEntity : class => new(typeof(TEntity), rows, overrideConcurrency);
}

/// <summary>What the persist statements reported: the stamp, which roots received it, and row counts.</summary>
public sealed record SaveReceipt(DateTime Stamp, IReadOnlySet<object> StampedIds, int Inserted, int Updated, int Deleted);

/// <summary>A bulk delete: by ids, by a filter-only query, or a tree's ids with their descendants.</summary>
public abstract record DeleteSpec(Type EntityType)
{
    public sealed record ByIds(Type EntityType, IReadOnlyList<object> Ids) : DeleteSpec(EntityType);
    public sealed record ByQuery(Type EntityType, FilterTree Filter, QueryArguments? Arguments) : DeleteSpec(EntityType);
    public sealed record WithDescendants(Type EntityType, IReadOnlyList<object> Ids) : DeleteSpec(EntityType);
}

/// <summary>The ids actually deleted; a requested id that is absent was missing or not visible.</summary>
public sealed record DeleteReceipt(IReadOnlySet<object> DeletedIds);

/// <summary>Result sets of a raw statement, as row sets in order.</summary>
public sealed record RawResult(IReadOnlyList<RowSet> ResultSets);

/// <summary>Allocates surrogate keys from per-table sequences; one instance per process.</summary>
public interface IIdAllocator
{
    /// <summary>Contributes reservation statements for the deficit of every table the rows need to the given batch; call before that batch executes.</summary>
    void Reserve(IDataBatch batch, Type entityType, IReadOnlyList<object> rows);
    /// <summary>Assigns ids to every new row (roots and supplied children, topologically) and wires child parent keys; consumes reservations.</summary>
    void AssignIds(Type entityType, IReadOnlyList<object> rows);
}

/// <summary>Runtime description of a mapped leaf entity: everything the data layer, the pipeline and the codecs need to know about it.</summary>
public sealed class EntityMetadata
{
    public Type ClrType { get; }
    public string Name { get; }
    public TableName Table { get; }
    public string TableTypePhysicalName { get; }
    public PropertyMetadata Key { get; }
    public IReadOnlyList<PropertyMetadata> Properties { get; }
    public IReadOnlyList<ChildCollectionMetadata> Children { get; }
    public PropertyMetadata? NaturalKey { get; }
    public IReadOnlyList<MultilingualGroup> MultilingualGroups { get; }
    public IReadOnlyList<UniqueIndexMetadata> UniqueIndexes { get; }
    public bool IsTopLevel { get; }
    public bool IsTemporal { get; }
    public bool IsActivatable { get; }
    public bool IsLeasable { get; }
    public TreeMetadata? Tree { get; }
    /// <summary>Restores every server-owned property to the value a fresh instance carries.</summary>
    public void ResetServerOwned(object entity);
}

/// <summary>One mapped scalar property.</summary>
public sealed record PropertyMetadata(string Name, string Column, Type ClrType, PropertyOwnership Ownership, bool IsNullable, bool IsUnique, string? MultilingualGroup, Func<object, object?> Getter, Action<object, object?> Setter);

/// <summary>Who may set a property.</summary>
public enum PropertyOwnership { Editable, WriteOnce, ServerOwned }

/// <summary>A <c>[NotMapped]</c> child collection and the foreign key that ties its rows to the parent.</summary>
public sealed record ChildCollectionMetadata(string Property, EntityMetadata Child, PropertyMetadata ParentKey, Func<object, IList?> Getter);

/// <summary>A multilingual property group (<c>Name</c>, <c>Name2</c>, <c>Name3</c>).</summary>
public sealed record MultilingualGroup(string Name, PropertyMetadata Primary, PropertyMetadata Secondary, PropertyMetadata Ternary);

/// <summary>A unique index, for mapping constraint violations to properties.</summary>
public sealed record UniqueIndexMetadata(string IndexName, IReadOnlyList<PropertyMetadata> Properties);

/// <summary>Tree properties of a hierarchical entity.</summary>
public sealed record TreeMetadata(PropertyMetadata ParentKey, PropertyMetadata Node, PropertyMetadata SubtreeCount, PropertyMetadata? ActiveSubtreeCount, int MaxDepth);

/// <summary>Resolves metadata for mapped leaf types; built once at startup and validated then.</summary>
public interface IEntityMetadataProvider
{
    EntityMetadata Get(Type entityType);
    IReadOnlyList<EntityMetadata> All { get; }
}

/// <summary>Per-operation accounting of round trips; copied into background scopes.</summary>
public sealed class DataAccessScope
{
    public string Operation { get; set; } = "";
    public int RoundTrips { get; }
    public int Retries { get; }
}

/// <summary>Meter and instrument names of the data layer.</summary>
public static class DataMeter
{
    public const string Name = "Tellma.Core.Data";
    public const string RoundTrips = "tellma.data.roundtrips";
    public const string RoundTripDuration = "tellma.data.roundtrip.duration";
    public const string BatchStatements = "tellma.data.batch.statements";
    public const string Retries = "tellma.data.retries";
    public const string IdsReserved = "tellma.data.ids.reserved";
    public const string IdsReturned = "tellma.data.ids.returned";
    public const string IdsDesync = "tellma.data.ids.desync";
    public const string ConcurrencyConflicts = "tellma.data.concurrency.conflicts";
    public const string TreeRecomputes = "tellma.data.tree.recomputes";
    public const string RowsSaved = "tellma.data.rows.saved";
    public const string RowsDeleted = "tellma.data.rows.deleted";
}

/// <summary>Exceptions the executor raises; the pipeline theme owns the platform exception hierarchy these derive from.</summary>
public sealed class ConcurrencyConflictException(IReadOnlyList<object> conflictingIds, IReadOnlyList<object> missingIds, bool truncated) : DataAccessException;
public sealed class UniqueConstraintViolationException(string indexName) : DataAccessException;
public sealed class ForeignKeyViolationException(string constraintName) : DataAccessException;
public sealed class TreeCycleException(Type entityType) : DataAccessException;
public sealed class RowSecurityException() : DataAccessException;
public sealed class DataAccessAmbiguousException(Exception inner) : DataAccessException;
```

`PlatformError` (used by `Assert`) is the pipeline theme's error identifier; the assertion maps it to a `THROW` number in the 50400–50499 range that the executor translates back.

### 3.3 Shapes needed from other themes

- **Host theme:** `IRequestContext` exposing `TenantId`, `UserId?`, the user's calendar date and time zone, the tenant zone, and `SchemaProfile`; a scoped-holder copy for background scopes; the feature composition hook where a feature registers entity leaf types (`entities.Add<Center>()`) so `IEntityMetadataProvider` and the runtime `DbContext` model see one list.
- **Settings theme:** `SchemaProfile(int LanguageCount)` on the request context; an `IDataBatchContributor` that reads `WrittenTables` and appends tag-bump statements; the tenant zone name.
- **Users/permissions theme:** an `IDataBatchContributor` (prologue) that adds the connect statement and tag reads; the `FilterTree` for row-level security applied by the pipeline to every `Query`/`Rows`/`Count`/`Delete.ByQuery`.
- **Pipeline theme:** the `DataAccessException` base and `PlatformError`; the operation name it sets on `DataAccessScope`; the validator API that receives an `IDataBatch` for context loads.
- **Background theme:** the `State`/`DueAt` columns the lease predicate reads (the statement template takes their names from metadata attributes it defines, e.g. `[LeaseState]`, `[LeaseDue]`).

---

## 4. Schema

This theme owns no production table; it owns the **column vocabulary** every capability projects onto a table, and the fixture tables of its own tests. The reference tables (`core.Users`, `gl.Centers`, …) are the user and reference-stack themes' to finalise; the example at the end shows what the contract projects for `gl.Centers`.

### 4.1 Capability column sets

```
Keyed (Entity<TKey>)
  Id                 int (or bigint)   NOT NULL   PK clustered PK_<Table>; app-assigned from sq_<Table>; no IDENTITY

Top-level audit (TopLevelEntity<TKey>)
  CreatedAt          datetime2(7)      NOT NULL   server-stamped on insert
  CreatedById        int               NOT NULL   FK FK_<Table>_CreatedById → core.Users(Id), NO ACTION
  ModifiedAt         datetime2(7)      NOT NULL   concurrency token; moves on user-visible mutations only
  ModifiedById       int               NOT NULL   FK FK_<Table>_ModifiedById → core.Users(Id), NO ACTION
  index IX_<Table>_ModifiedAt (ModifiedAt) — optional, for "recently changed" lists; not created by default

Temporal ([Temporal])
  ValidFrom          datetime2(7)      NOT NULL   GENERATED ALWAYS AS ROW START, shadow property
  ValidTo            datetime2(7)      NOT NULL   GENERATED ALWAYS AS ROW END, shadow property
  PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo); SYSTEM_VERSIONING = ON (HISTORY_TABLE = <schema>.<Table>History)
  history table: no nonclustered indexes (MERGE defect is moot, but the default clustered (ValidTo, ValidFrom) stays)

Child (ChildEntity<TKey> with [ParentKey])
  <Parent>Id         int               NOT NULL   FK FK_<Table>_<Parent>Id → <ParentTable>(Id), NO ACTION (children deleted explicitly)
  index IX_<Table>_<Parent>Id (<Parent>Id)

Tree (TreeEntity<TSelf>)
  ParentId           int               NULL       FK FK_<Table>_ParentId → <Table>(Id), NO ACTION
  Node               hierarchyid       NOT NULL   unique index IX_<Table>_Node (Node); excluded from the UDTT; value = '/'+id path
  SubtreeCount       int               NOT NULL   DEFAULT 0; recomputed set-based
  ActiveSubtreeCount int               NOT NULL   DEFAULT 0; only when IActivatable
  index IX_<Table>_ParentId (ParentId)

Activatable (IActivatable)
  IsActive           bit               NOT NULL   DEFAULT 1

Leasable (ILeasable)
  LeaseOwner         uniqueidentifier  NULL
  LeaseExpiresAt     datetime2(3)      NULL
  LeaseToken         bigint            NOT NULL   DEFAULT 0
  Attempts           int               NOT NULL   DEFAULT 0
  filtered index IX_<Table>_Lease (DueAt, Id) INCLUDE (LeaseExpiresAt) WHERE State = 'Queued' — shape owned by the background theme

Enum-valued property
  <Name>             varchar(n)        per nullability; n = longest member name (min 8); no CHECK constraint (the class is the truth)

Multilingual group P
  P                  nvarchar(k)       NOT NULL
  P2, P3             nvarchar(k)       NULL       same k and unicode as P

Sequence per table
  sq_<Table>         AS int START WITH 1000 INCREMENT BY 1 NO CYCLE   (StartsAt above the reserved seed band [1, 999])
```

### 4.2 Fixture tables (`fixture` schema, test-only)

```
fixture.Widgets  [Temporal] IActivatable, natural key Code
  Id int PK | Code varchar(50) NULL, unique filtered IX_Widgets_Code WHERE Code IS NOT NULL
  Kind varchar(8) NOT NULL (enum WidgetKind { Plain, Fancy })
  Name nvarchar(255) NOT NULL | Name2 nvarchar(255) NULL | Name3 nvarchar(255) NULL
  Subject nvarchar(64) NOT NULL [WriteOnce], unique IX_Widgets_Subject
  Price decimal(19,4) NULL | IsActive bit NOT NULL DEFAULT 1
  CreatedAt, CreatedById, ModifiedAt, ModifiedById | ValidFrom, ValidTo
  history: fixture.WidgetsHistory

fixture.WidgetParts  child of Widgets, temporal with its parent
  Id int PK | WidgetId int NOT NULL [ParentKey] FK → Widgets | Ordinal int NOT NULL | Notes nvarchar(1024) NULL
  unique IX_WidgetParts_WidgetId_Ordinal (WidgetId, Ordinal)

fixture.WidgetPartNotes  grandchild
  Id int PK | WidgetPartId int NOT NULL [ParentKey] FK → WidgetParts | Text nvarchar(max) NOT NULL

fixture.Nodes  TreeEntity<Node>, IActivatable
  Id int PK | ParentId int NULL FK → Nodes | Node hierarchyid NOT NULL unique | SubtreeCount int | ActiveSubtreeCount int
  Code varchar(50) NOT NULL unique | Name nvarchar(255) NOT NULL | IsActive bit NOT NULL | audit ×4

fixture.Jobs  TopLevelEntity, ILeasable
  Id int PK | State varchar(16) NOT NULL | DueAt datetime2(3) NOT NULL | Payload nvarchar(max) NULL
  LeaseOwner, LeaseExpiresAt, LeaseToken, Attempts | audit ×4
```

### 4.3 Example projection: `gl.Centers` (owned by the reference-stack theme; shown as the contract's output)

```
gl.Centers
  Id                 int            NOT NULL PK PK_Centers
  ParentId           int            NULL     FK FK_Centers_ParentId → gl.Centers(Id)
  Type               varchar(9)     NOT NULL (Service | Operation | Sale)
  Name               nvarchar(255)  NOT NULL
  Name2              nvarchar(255)  NULL
  Name3              nvarchar(255)  NULL
  Code               varchar(50)    NULL     unique filtered IX_Centers_Code
  IsActive           bit            NOT NULL DEFAULT 1
  Node               hierarchyid    NOT NULL unique IX_Centers_Node
  SubtreeCount       int            NOT NULL DEFAULT 0
  ActiveSubtreeCount int            NOT NULL DEFAULT 0
  CreatedAt          datetime2(7)   NOT NULL
  CreatedById        int            NOT NULL FK → core.Users
  ModifiedAt         datetime2(7)   NOT NULL
  ModifiedById       int            NOT NULL FK → core.Users
  UDTT gl.CentersList_<hash8>: every column above except Node, in table order
  sequence gl.sq_Centers
  (no IsLeaf, no Level)
```

---

## 5. Answers to the brain dump's open questions in this theme

| Brain-dump question (abridged) | Answer |
|---|---|
| "Write-once columns like Subject and Email: 2 UDTTs for create and update, or keep the data layer dumb and enforce at the service layer?" | Neither: one UDTT; `[WriteOnce]` columns are excluded from every UPDATE SET list by the emitter (D4). The service may additionally report the attempt. |
| "Do we need SavedById on the weak entities, or is it redundant?" | Redundant. Child rows carry no audit columns; the root's four describe the aggregate, and a child change stamps the root (D2, D12). |
| "Best way to keep the hierarchyId column up to date with ParentId while supporting bulk save — C# in memory or a SQL statement appended in the same transaction?" | SQL appended to the persist batch; `Node` is the id path so only saved rows and their descendants are re-pathed; counts recomputed set-based; cycles validated in C# and fenced by `MAXRECURSION` in SQL (D15). |
| "How to model CenterType? Stored as a string so Queryex can say `CenterType = 'Service'`." | An enum; a platform convention stores every enum as `varchar(n)` sized from member names; Queryex sees a string (D6). The column is named `Type` (the class already says Center). |
| "Better names for the SQL save emitter and the multi-statement builder/executor?" | `SaveEmitter` with `SaveSpec`; `DataBatch` behind `IDataBatch`, created by `ITenantDatabase.CreateBatch()` (Critique, D10). |
| "Does the emitter need to know whether it's upsert or synchronize?" | No. Roots are upserted, supplied child collections are synchronised, unsupplied ones untouched; the mode is structural, carried by null-versus-list (D3, D12). |
| "What should the multi-statement builder/executor API look like?" | D11 and §3.2: typed statements returning `BatchResult<T>`, structural dedup, declared writes, contributors, one `ExecuteAsync`. |
| "Minimise Id gaps without a dedicated round trip? Hitch a ride on OnConnect or the validation query?" | Yes: exact-deficit reservation rides the validation round trip; unused ranges return to the buffer; no prefetch (D9). |
| "Who maintains the reserved IDs — a thread-safe singleton?" | `IIdAllocator`, one singleton, buffers keyed by (tenant database, sequence), lock-guarded, no I/O under the lock (D9). |
| "If a save fails, un-consume the ids?" | Ids are consumed only after validation passes; a failed validation leaves the range buffered; a failed persist returns the range (D9). |
| "Who assigns ids, service or data layer?" | The data layer (`IIdAllocator.AssignIds`); the pipeline chooses the moment (D9). |
| "Queryex: restrict results where a column is IN a TVP-passed list" | `QuerySpec.Restrictions` with `KeySetRestriction(Path, TableParameter)` (D19). |
| "Add `level` as a Queryex function" | `level(node)` → `GetLevel()` (D19). |
| "A good alternative to RowVersion, implementable in C#?" | `ModifiedAt` as the token, server-stamped once per batch, checked inside the UPDATE, override flag honoured (D14). |
| "Distros must extend or replace entities while reusing service logic; validate that the interface matches the DB" | Generic services over leaf types with capability constraints (`where T : TopLevelEntity, IActivatable`); `EntityMetadata` validates at startup that every capability property is mapped (D8). |
| "Platform API that lets custom validators load context in the batch call; dedupe identical or overlapping context queries" | Validators receive the `IDataBatch` of the validation round trip; `Query`/`Rows`/`Count` dedupe by structural key; overlapping selects are not merged in this release (D11). The validator API itself is the pipeline theme's. |
| "Where should the transaction boundary begin and end?" | Inside the persist round trip's text only; validation reads run untransacted; C# never holds a transaction (D10). |
| "Collapse DB calls #1 and #2 optimistically when permissions are cached?" | Supported by the batch: connect statements are prologue contributions to whatever round trip runs first; safe for read round trips and for a save's validation round trip, never for the persist (§6 seam 16). |
| "Observability to count how many DB calls every entity in every distro makes" | `DataAccessScope` + `tellma.data.roundtrips` histogram tagged by operation and entity; test oracle via `RetrieveStatistics` (D22). |
| "Should we extend the schema per tenant languages (Name2/Name3 removed when unconfigured)?" | Yes: schema per `SchemaProfile(LanguageCount)` (D17). |
| "Natural key declaration — a Core attribute? Mandate one on every entity?" | `[NaturalKey]`; inference over unique indexes; no mandate — absence means surrogate-key export with a warning (D5). |

---

## 6. Positions on the cross-cutting seams

1. **Batch abstraction (owned here).** `IDataBatch` as in §3.2. Contributors (`IDataBatchContributor`) are the single hook for the connect prologue, tag reads, tag bumps, and id reservation; notification inserts are ordinary `Save` statements of the notification entity added by the pipeline; lease statements come from `LeaseStatements<T>`. `Idempotent` per statement; `WrittenTables` declared per statement; `TransactionMode` per batch.
2. **Entity class versus wire shape (owned here).** One class; `[NotMapped]` child collections; `PropertyOwnership` on every property from `EntityMetadata`; the web theme derives JSON from the same class and must round-trip `ModifiedAt` at seven fractional digits and treat `null` versus `[]` on child collections as different. Partial related instances (materializer output) are read-only projections and must not be accepted by save.
3. **One capability, declared once (consumed).** The data-layer half of a capability is a base class or a one-property interface plus `[ServerOwned]` markers; the pipeline reads `EntityMetadata.IsActivatable`/`Tree`/`IsLeasable` to project actions, permissions and routes. A capability must not require a second declaration in a service.
4. **Queryex schema per tenant configuration (owned here).** Cache key = `SchemaProfile` record (today `LanguageCount`); lifetime = process; row-level-security and child-path rewriting are `FilterTree` transformations at request time, never schema variants.
5. **Version tags (consumed).** A prologue contributor reads tags; an epilogue contributor bumps them from `WrittenTables`. This layer guarantees only that every write passes through a batch that exposes `WrittenTables`; which tables map to which tags is the settings theme's policy.
6. **Feature composition (consumed).** A feature contributes entity leaf types; the metadata provider and the runtime model are built from that one list after composition.
7. **Natural keys (owned here).** `[NaturalKey]` plus inference (D5); `EntityMetadata.NaturalKey` is the Excel theme's input; bulk natural-to-surrogate translation is a `Rows` query with a `StringList` restriction on the natural-key path.
8. **Background-task columns and lease statements (emitted here).** `ILeasable` columns and `LeaseStatements<T>` (D21); the background theme owns `State`/`DueAt` semantics, renewal cadence and poison handling.
9. **Request context (consumed).** `ITenantDatabase` is constructed from the request context's tenant, user, dates, zone and `SchemaProfile`; background scopes construct one through `ITenantDatabaseFactory.Open(tenant, contextValues)`. No `AsyncLocal`.
10. **Platform exceptions (consumed).** The executor's exceptions derive from the pipeline theme's `DataAccessException` base; HTTP mapping is the web theme's.
11. **Permission evaluation API (consumed).** The pipeline hands the resulting `FilterTree` to `Query`/`Rows`/`Count`/`Delete.ByQuery`; the RLS post-check after a save is `Assert(count, expected: n, RowSecurityError)` inside the persist transaction.
14. **Telemetry (owned here).** `DataMeter` names in §3.2; no per-tenant tags; every theme names its own instruments.
15. **Notification enqueue riding the save (consumed).** Nothing special: a second `Save` in the same batch.
16. **Connect-call collapse (position).** The batch supports it: the connect prologue rides whatever round trip runs first. Failure modes and rules — (a) *deactivated user*: the prologue's result is examined before any write round trip, and a read round trip's results are discarded if the user is inactive; (b) *stale permissions*: the prologue returns the permissions tag; a mismatch with the cached tag recomputes permissions and re-runs the round trip once (it was a read); (c) *RLS pre-check ordering*: the pre-check is a `Count` in the validation round trip using cached filters; under (b) it re-runs with fresh filters. Persist round trips never carry the connect prologue; they run only after C# has seen a fresh connect result.
17. **Vocabulary.** Four audit columns `CreatedAt/CreatedById/ModifiedAt/ModifiedById`; plural table names (`core.Users`, `gl.Centers`); schemas `core`, `gl`, `fixture`; `int` ids by default; "child entity" not "weak"; "top-level entity"; `Type` not `CenterType`; `Node` for the hierarchy column; `sq_<Table>` sequences.

---

## 7. Departures from ARCHITECTURE.md

1. **Child collections exist on the entity class as `[NotMapped]` lists.** The document says `Invoice.Lines` does not exist; it does exist as a non-EF property so that one class is both storage and wire shape. The reasons for the ban (no `Include`, no cartesian reads, no collections in Queryex) hold because EF never sees it.
2. **No background prefetch in the ID allocator.** The document describes background prefetch; exact-deficit reservation riding an existing round trip has zero extra round trips and smaller gaps, so prefetch is dropped. The "single-round-trip multi-sequence cold start via a dynamic batch" is kept as the shape of the reservation statements.
3. **No `rowversion`; `ModifiedAt` is the concurrency token.** The UDTT's rowversion inclusion (spec 0001) stays available but unused.
4. **Two new packages, `Tellma.Core.Data` and `Tellma.Core.Data.Abstractions`,** because the consumer-facing batch API mentions Queryex types and `Tellma.Core.Abstractions` is BCL-only; modules reference the Abstractions package only. (Review flag: fold `Tellma.Core.Data.Abstractions` into `Tellma.Core.Abstractions` by allowing it to reference `Tellma.Core.Queryex`.)
5. **`Tellma.Core.Abstractions` references `Microsoft.EntityFrameworkCore.SqlServer.Abstractions`** for the `HierarchyId` CLR type (review flag; alternative in §3.1).
6. **Temporal period columns are shadow properties**, an exception to "no shadow properties on mapped entities" forced by EF 10; the analyzer exempts them.
7. **Package pins:** `Microsoft.EntityFrameworkCore.SqlServer.HierarchyId` 10.0.11 added, which forces `Microsoft.Data.SqlClient` ≥ 6.1.6 (from 6.1.1) and EF Core SqlServer 10.0.11 (from 10.0.9).
8. **ID allocation and the EF-to-Queryex adapter land in spec 0011**, not the separate specs the document promised; its pointers change.
9. **`ExcludeFromTableTypeAttribute` moves to `Tellma.Core.Abstractions.TableTypes`** (type-forwarded from its current assembly) so platform bases can exclude `Node`.
10. **Tables are written only through `IDataBatch`; EF `SaveChanges` is banned at runtime** (analyzer), which the document implies ("runtime SQL emitted from the entity model") but does not enforce.

---

## 8. Verification

**Relied on from the research file (verified 2026-09-01):** MERGE's two surviving defects and the separate-statement recommendation; SqlClient retry never applies inside a transaction and `SqlBatch` has no retry provider, sends a TVP per command and shows only the last statement to telemetry; the driver's baseline transient list includes 1205 and 1222; `IEnumerable<SqlDataRecord>` streams; temporal UPDATE writes a history row even when nothing changed and `OUTPUT` has no temporal-specific restriction; period columns are shadow properties in EF 10 and only hierarchy roots can be temporal; `hierarchyid` cannot ride a TVP without `SqlHierarchyId`, the EF package is 10.0.11 and needs SqlClient ≥ 6.1.6; `sp_sequence_get_range` semantics, `NEXT VALUE FOR` restrictions, sequence cache loss on abnormal shutdown, and EF's inability to emit `CACHE`; RCSI on by default only on Azure SQL, `READPAST` a no-op under RCSI without `UPDLOCK`; `Properties<Enum>().HaveConversion<string>()` and finalizing-convention sizing; JSON columns stay `nvarchar(max)` without `UseAzureSql()`/compat 170; OpenTelemetry SqlClient 1.16 attributes and the `RetrieveStatistics` round-trip counter; the `< 1 000 rows` TVP heuristic.

**Own reasoning, not verified by a source:** `SqlConnection.BeginTransaction` and `Commit` each cost a network round trip (basis for `TransactionMode.Explicit` costing two extra); `THROW` message length cap of 2 048 characters; a `hierarchyid` label component accepts any `int` value and the encoding of large components stays small enough for master-data trees (measure in the fixture suite before relying on id paths for `bigint` keys); `SYSUTCDATETIME()` monotonicity across the clock steps Azure SQL performs; `OUTPUT inserted.[Id]` from an UPDATE on a system-versioned table (inferred legal by the research; a fixture test pins it); `IsDescendantOf` against a unique `Node` index seeks rather than scans in the `CROSS APPLY` recount (documented for the descendant direction by spec 0008 §10.10's rationale; measure).

**Still unverified and deliberately deferred:** whether `Properties<Enum>()` matches nullable enum properties (fixture unit test); the Managed Instance RCSI default (the migrator sets it regardless); the crossover row count at which a staging-table `SqlBulkCopy` beats a TVP for import (benchmark before adding the path).
