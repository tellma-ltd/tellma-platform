# Entity contract and data access (spec 0011) — settled design

Self-contained: a reader who sees nothing else gets the complete design for this theme. Names are final. Contract blocks use the platform's contract notation (names are normative; shape is described, not transcribed); SQL is the exact shape to emit.

**Packages.** Three packages carry this theme:

- `Tellma.Core.Abstractions` (existing; BCL only, no EF, no Queryex) gains the **entity contract** under `Tellma.Core.Abstractions.Entities`: base classes, capability interfaces, and the annotations an entity author uses.
- `Tellma.Core.Data.Abstractions` (new; references `Tellma.Core.Abstractions` and `Tellma.Core.Queryex`, nothing else — the Queryex package itself has no references) holds the consumer-facing **data access API**: `ITenantDatabase`, `IDataBatch`, entity queries, entity metadata, the allocator interface, the meter names, the data exceptions. Modules (`Tellma.Module.Gl`) may reference it: it is an Abstractions package by the repository's naming rule and is EF-free.
- `Tellma.Core.Data` (new; references `Microsoft.Data.SqlClient`, `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.SqlServer.HierarchyId`, `Tellma.Core.EntityFrameworkCore`, `Tellma.Core.Queryex`) implements it: the batch executor, the save emitter, the delete and update emitters, the ID allocator, the schema adapter, the materializer, the tree statements, the lease statements, the model conventions. The distribution and `Tellma.Core` reference it; modules never do.

**Round-trip budget** (common case; permissions cached; the connect prologue riding the first round trip of the request):

| Operation | Round trips | What rides each |
|---|---|---|
| Grid page (flat or tree, with capped count and ancestors) | 1 | connect prologue, keys capture, page rows, capped count, ancestors |
| Details by id (entity, children, related, row echo) | 1 | connect prologue, root query restricted by ids, one child query per collection, row echo |
| Save (new or existing, with children) | 2 | (1) connect prologue, RLS pre-check, validation context, cycle-check projection, id reservation; (2) persist in one in-text transaction: guard, inserts, child sync, root update, tree re-path and recount, tag bumps, RLS post-check assertion, read-back |
| Import of N rows | 2 per chunk | as save; natural-key translation rides round trip 1 through `StringList` TVPs |
| Delete by ids / by query / with descendants | 1 | connect prologue, keys capture, RLS assertion, child deletes, root delete, recount, tag bumps |
| Activate / deactivate / any column action | 1 | connect prologue, stamped update restricted by ids and RLS, recount, tag bumps |
| Get by parent ids | 1 | one query restricted by a TVP |

No lock is held across C# code: the persist transaction opens and commits inside the text of one round trip; validation reads run untransacted in the round trip before it.

---

## 1. Critique

The brain dump's shape for this theme — one save emitter, one multi-statement builder and executor, app-assigned ids, bulk-only APIs, DB-call counting from day one — is right and stands. What needed correction is precision: several mechanisms cannot work as written on SQL Server, several columns are redundant or misplaced, and several facts were asserted that the sources do not support.

**Unworkable or inconsistent as stated**

1. *"Upsert for top-level entities"* reads as `MERGE`. `MERGE` is out: two defects survive on SQL Server 2022 CU7 (temporal targets whose history table has a nonclustered index error out; `MERGE` with a `DELETE` action under an indexed view silently corrupts the view), and `User`, `Role`, `RoleMembership`, `Permission` are exactly temporal. The emitter writes separate `INSERT`, `UPDATE`, `DELETE` statements per table. Because ids are app-assigned, "new" versus "existing" is known in C#, so each table gets **two TVPs** (new rows, existing rows) rather than one TVP with an existence test — an update of a row someone deleted is then a conflict, never a silent resurrection.
2. *"Start the transaction (step 4) … validate (step 5) … save (step 7) … commit (step 11)"* holds a transaction across two round trips and across C# validation. Under read-committed snapshot (the Azure SQL default) that protects nothing against write skew; under locking read committed (on-prem) it holds shared locks while C# runs. The transaction lives inside the persist round trip's text only, and every check that needs "inside the transaction" semantics — the concurrency guard, the row-level-security post-check, the tree cycle fence — is a SQL statement that aborts the batch.
3. *"MayRetry, true for reads and idempotent writes"* conflates two questions. SqlClient's built-in retry never applies to a command inside a transaction and `SqlBatch` has no retry provider at all, so retry is the executor's job; and the distinction that matters is between **reported** failures (the server returned an error; with `SET XACT_ABORT ON` the transaction is rolled back; always safe to re-run) and **ambiguous** failures (the connection dropped; whether `COMMIT` ran is unknown; safe only when every statement can run twice). The flag is `Idempotent`, defined as "may run again even if the previous attempt might have committed".
4. *"Load the response back in the same DB call … include a query with the RLS filter … if RLS fails, roll back"* wants C# to decide after seeing results and before commit. With commit inside the text — the only one-round-trip shape — that is impossible, so the post-check is a SQL assertion (`IF (count) <> @n THROW`), which needs a small Queryex amendment: `CompiledQuery` exposes its `SELECT` body apart from its hoisted declarations so it can be embedded.
5. *`User` carries `SavedAt`/`SavedById` and the temporal period; `Center` carries four audit columns* — two vocabularies for one concept. One vocabulary: four audit columns on every top-level entity, with system-versioning as an additive per-table capability. `ModifiedAt` is not redundant with `ValidFrom`: it is the concurrency token, it is an ordinary column the client can echo (period columns are shadow properties in EF 10 and cannot ride a TVP), and it does not move on the bookkeeping writes that sibling tables absorb. `SavedById` on child rows is redundant with the root's audit columns.
6. *`Center.IsLeaf`* is `SubtreeCount = 1`; *`Level`* is `level(Node)` in Queryex; both are dropped. `SubtreeCount` and `ActiveSubtreeCount` stay because the tree view decides expanders for every visible row without a subquery.
7. *`Node` in the row image* cannot work: `hierarchyid` has no TVP binding without `Microsoft.SqlServer.Types.SqlHierarchyId`, whose Linux behaviour is undocumented. `Node` is not a CLR property at all — it is a shadow column the platform's tree convention adds, excluded from the UDTT, computed in SQL, and never bound from C#.
8. *"Reserve the IDs from the DB every time a new record is saved"* against *"large ranges cause gaps"* dissolves once reservation is exact (the batch's deficit), rides the validation round trip the save already makes, and unused ranges return to an in-process buffer. No background prefetch, no dedicated round trip; gaps only on process crash.
9. *"RowVersion … not all properties participate"* is the right observation; the fix is `ModifiedAt` as the token under one rule (user-visible mutations stamp it, bookkeeping never touches the entity row), not a hash.
10. *"Two UDTTs for create and update"* for write-once columns is the rejected `ForSave` pattern in disguise. The row image stays one; the emitter never puts a `[WriteOnce]` or `[ServerOwned]` column in an `UPDATE`'s `SET` list.
11. *"Validate that the interface matches the DB (no needed columns are dropped)"* can only be violated through `[NotMapped]`/`Ignore()` (C# cannot un-inherit); the metadata builder rejects a leaf whose capability-declared property is unmapped, at startup.
12. The tree re-path "from saved rows whose parent is not in the batch" is wrong when a saved row's parent is itself a descendant of another saved row: the anchor reads a stale parent node and the row is pathed twice. The affected set must be computed first (saved rows plus every descendant of a saved existing row by its pre-save node), and anchors are the affected rows whose parent lies outside that set. A `MAXRECURSION` cap does **not** detect cycles — a cycle is unreachable from any anchor — so the fence is "every affected row received a path".
13. Healing a desynchronised sequence with `ALTER SEQUENCE … RESTART` needs `ALTER` permission on the sequence, which the web identity must not hold. The allocator heals by **consuming** the gap through `sp_sequence_get_range` (which needs only `UPDATE` on the sequence).

**Naming and vocabulary**

- Table names are plural (`core.Users`, `gl.Centers`), matching the architecture; the CLR type stays singular and is the Queryex entity name.
- "Weak entity" becomes **child entity** (`ChildEntity`); "top-level entity" stays (`TopLevelEntity`); system-versioning is `[Temporal]`.
- The save emitter is **`SaveEmitter`**; the multi-statement builder and executor is **`DataBatch`** behind `IDataBatch`, obtained from `ITenantDatabase.CreateBatch()`. `RoundTrip` names the cost, not the thing; `SqlBatch`/`DbBatch` collide with ADO.NET.
- `CenterType` becomes `Type` (the class already says Center); the hierarchy column is `Node`; sequences are `sq_<Table>`.

**Gaps the brain dump does not mention**: how child collections travel on a class that must have no EF collection navigation (D3); which properties a client may set (D4); natural keys (D5); enum storage (D6); multilingual groups (D7); JSON columns (D8); the tenant-configuration dimension of the Queryex schema (D18); bulk column actions such as activate (D12); lease statements (D22); the ordinal-binding analyzer (D23); fixtures and test tiers (D24).

---

## 2. Decisions

### D1 — Keyed entities: `Entity<TKey>`, `int` by default, `long` by opt-in

**Decision.** Every persisted class derives from `Entity<TKey>` (`TKey` is `int` or `long`), which declares `Id: TKey`. Non-generic sugars cover the common case: `TopLevelEntity` = `TopLevelEntity<int>`, `ChildEntity` = `ChildEntity<int>`. A `long` key is `TopLevelEntity<long>`; the allocator and `BigIdList` already handle it. `Guid` keys are not offered (no sequence source, no need surfaced). `Id == default` (0) means "new"; the wire also accepts `null` for it.

**Rationale.** Generic services and the emitter constrain on `TKey` once; distributions never see the generic in the common case.

**Rejected.** A non-generic `Entity` plus a `BigEntity` (duplicates every capability base); `object Id` (boxing in every TVP row).

**Confidence.** High.

### D2 — Top-level entities carry four audit columns; system-versioning is an additive capability

**Decision.** `TopLevelEntity<TKey>` declares `CreatedAt datetime2(7) NOT NULL`, `CreatedById int NOT NULL` (FK `FK_<Table>_CreatedById` → `core.Users(Id)`, `NO ACTION`), `ModifiedAt datetime2(7) NOT NULL`, `ModifiedById int NOT NULL` (FK likewise). All four are server-owned. `[Temporal]` on a top-level class makes the table system-versioned: period columns `ValidFrom`/`ValidTo` as shadow properties (EF 10 cannot map them to CLR properties), history table `<Table>History` in the same schema with the default clustered `(ValidTo, ValidFrom)` index and no nonclustered indexes. Child entities of a `[Temporal]` root are temporal too unless marked `[Temporal(false)]`. Child entities carry no audit columns; the root's four describe the aggregate, and a child change stamps the root (D13). `[Temporal]` is rejected at startup on a TPT root or a TPT leaf (EF supports temporal only on leaf-mapped roots).

**Rationale.** One vocabulary; creation is otherwise lost or expensive to reconstruct from history; `ModifiedAt` is the concurrency token (D15) and must be an ordinary column in the UDTT, which period columns cannot be.

**Rejected.** `SavedAt/SavedById` plus period on temporal entities and four columns elsewhere; `SavedById` on child rows.

**Confidence.** High. **Review flag:** the audit FKs make every table depend on `core.Users` and mean users are never hard-deleted — consistent with deactivate-not-delete, but every distribution table now carries two FKs to Core.

### D3 — Child collections are `[NotMapped]` list properties on the parent; the EF model keeps no parent→child navigation

**Decision.** A top-level entity declares its children as `[NotMapped]` list properties (`Memberships: list<RoleMembership>?`). The child class marks its owning foreign key with `[ParentKey]`; when a child has several foreign keys to the same parent type, the collection disambiguates with `[Children(nameof(Child.ParentKeyProperty))]`. Nesting is recursive: a child may declare `[NotMapped]` grandchildren. `EntityMetadata` (D9) pairs every collection with the EF foreign key behind its `[ParentKey]`; the EF model never sees the collection (no `Include`, no cartesian reads, no collections in Queryex); save and details payloads carry it; the emitter synchronises it.

`null` versus empty is significant: `null` means "not supplied — leave the children alone"; an empty list means "delete them all". The emitter receives, per collection, an `IdList` TVP of the parents whose collection was supplied.

**Rationale.** One class is both storage shape and wire shape — the least a distribution can write; the architecture's ban is on *EF* navigations and its reasons survive intact because EF does not know the property.

**Rejected.** A parallel DTO hierarchy (doubles every class, needs a mapper, drifts); EF collection navigations with auto-include disabled (Queryex would need collections; `Include` becomes possible).

**Risks named.** A client can send values for server-owned columns and for children it should not touch; D4's ownership rule, the pipeline's reset of server-owned values, and the "supplied collections only" rule cover them.

**Confidence.** High.

### D4 — Editable, write-once, and server-owned are declared once and enforced by the emitter

**Decision.** Two annotations in `Tellma.Core.Abstractions.Entities`:

- `[ServerOwned]` — the client never sets it. Excluded from every `UPDATE`'s `SET` list; on `INSERT` the emitter writes either its own stamp (audit columns) or the in-memory value after the pipeline has reset it to the fresh-instance value (`IsActive = true`, counts `0`).
- `[WriteOnce]` — set at creation only. Excluded from every `UPDATE`'s `SET` list; written on `INSERT` from the payload. Examples: `User.Subject`, `User.Email`.

The platform bases apply `[ServerOwned]` to `Id`, the four audit columns, `SubtreeCount`, `ActiveSubtreeCount`. Ownership of a property declared by a capability interface (`IActivatable.IsActive`) is derived from the interface by the metadata builder, not from an attribute (attributes on interface members do not reach implementing properties). `IsActive` is server-owned: it changes through the activate and deactivate actions (`IDataBatch.Update`, D12), never through save; a new row is inserted active. `EntityMetadata.Properties[i].Ownership ∈ {Editable, WriteOnce, ServerOwned}`; `EntityMetadata.ResetServerOwned(entity)` restores each server-owned property to its fresh-instance value. Whether a silently ignored change is also reported as a validation error is the pipeline's choice; the data layer guarantees it is never written.

**Rationale.** One row image, no second UDTT, no drift; the guarantee lives in the layer that emits the SQL.

**Rejected.** Two UDTTs (the `ForSave` failure mode); validation-only enforcement (a forgotten validator writes the column).

**Confidence.** High. **Review flag:** `IsActive` server-owned means an import sheet cannot deactivate rows; the alternative is editable `IsActive` with the actions as a convenience.

### D5 — Natural keys: `[NaturalKey]`, otherwise inferred from unique indexes; none means surrogate-key export

**Decision.** `[NaturalKey]` on one property declares the entity's natural key (single-column in this release; composite keys are a later additive `Order` argument). Without it, `EntityMetadata.NaturalKey` is inferred over properties backed by a **unique** index in the EF model (uniqueness is read from the model, never assumed), in this order: `Code`, `Name`, the first unique required string property in declaration order, the first unique string property. If none qualifies, `NaturalKey` is null and export-for-import writes `Id` as the row key, which imports within the same tenant and never across tenants — the Excel codec reports that as a warning on the sheet. Inference stops at uniqueness: a non-unique column cannot identify a row.

**Rationale.** The first consumer (Excel) needs a deterministic answer per entity and a one-attribute override.

**Rejected.** Mandating a natural key on every entity (children and log-like entities have none); falling through to "first text column" (ambiguous by construction).

**Confidence.** Medium. **Review flag:** `Code` before `Name` versus `Name` first as the brain dump wrote.

### D6 — Enums are stored as `varchar(n)` by a platform convention sized from the member names

**Decision.** The platform's `ConfigureConventions` applies `Properties<Enum>().HaveConversion<string>()`, and a finalizing convention (`EnumStringLengthConvention`) sets non-unicode and `MaxLength = max(8, longest member name)` unless the property already carries `[MaxLength]`/`[Unicode]` or fluent facets. No CHECK constraint (the class is the truth). Nullable enums share the converter (pinned by a unit test; if `Properties<Enum>()` does not match `TEnum?`, the convention enumerates nullable enum properties explicitly). The Queryex adapter declares the property as `String` with store type `VarChar(n)`. A distribution writes `Type: CenterType` and nothing else; `Type = 'Service'` works in Queryex.

**Rationale.** The EF team's recommended mechanism; zero distribution lines; `varchar(n)` keeps seeks sargable against ASCII literals.

**Rejected.** Integer storage (magic numbers in filters); per-enum configuration in each distribution.

**Confidence.** High.

### D7 — Multilingual groups by naming convention: `P`, `P2`, `P3`

**Decision.** A string property `P` whose entity also declares string properties `P2` and `P3` forms the multilingual group `P` (`Name`/`Name2`/`Name3`, `Description`/`Description2`/`Description3`). `EntityMetadata.MultilingualGroups` lists them. Startup rules: `P2` and `P3` are nullable; all three share `MaxLength` and unicode. The settings theme gates `P2`/`P3` in the Queryex schema through the language profile (D18); the Excel codec maps "Arabic Name" columns through the group.

**Rationale.** The convention is already the platform's; an attribute would restate it.

**Rejected.** `[Multilingual]` (redundant); a JSON translations column (kills sargable filters on `Name2`).

**Confidence.** High.

### D8 — JSON columns are `nvarchar(max)` strings declared with `[JsonColumn]`; the compatibility level is pinned by the platform

**Decision.** A JSON-shaped column is a `string` property annotated `[JsonColumn]`; the platform convention maps it to `nvarchar(max)` explicitly (`HasColumnType`), so it never flips to the native `json` type when a host switches to `UseAzureSql()` or raises the compatibility level. The property is opaque text to the data layer (the owning service serializes); it is not declared to Queryex (no Queryex type for JSON). `ToJson()` owned types and primitive collections are not used in this release. The platform's `UseTellmaSqlServer(connectionString)` options helper (in `Tellma.Core.Data`) applies `UseSqlServer(...)`, `UseHierarchyId()`, `UseTableTypes()` and `UseCompatibilityLevel(160)` once, so every host and the migrator build the same model.

**Rationale.** The native `json` type is preview on SQL Server 2025 on-prem and EF auto-switches only under `UseAzureSql()`/compat 170; `nvarchar(max)` runs everywhere.

**Confidence.** High.

### D9 — `EntityMetadata` is the single runtime description of an entity, built once from the EF model plus reflection

**Decision.** `IEntityMetadataProvider.Get(Type)` returns an immutable `EntityMetadata` built at startup for every mapped leaf: CLR type, logical name, table, physical UDTT name (from `model.GetTableTypes()`), key, ordered properties with column, store type, ownership, multilingual group, `IsUnique`, accessors; child collections (property, child metadata, parent-key property, accessor); natural key; unique indexes (name → properties, for mapping 2601/2627 to fields); flags `IsTopLevel`, `IsTemporal`, `IsActivatable`, `IsLeasable`; tree metadata (parent key, node column name, counts, max depth); and the model-wide topological table order for inserts (reverse for deletes). Building validates: every capability-declared property is mapped; every `[ParentKey]` has an FK to a top-level table; every `[NotMapped]` child list pairs with exactly one FK; a tree entity has a unique index on `Node`; multilingual facets agree; `[Temporal]` is not on a TPT table; a top-level entity opted into a UDTT. Failures are aggregated into the host's startup validation.

**Rationale.** The emitter, materializer, allocator, Excel codec, pipeline and capability recipes all need the same facts; computing and validating them once is what makes "declare once" true.

**Confidence.** High.

### D10 — The ID allocator reserves exactly the deficit on a round trip the save already makes, holds it until validation passes, and heals by consuming

**Decision.** `IIdAllocator` (singleton) keeps a bounded buffer of contiguous ranges per (tenant database identity, sequence). Before the validation round trip of a save, the pipeline calls `Reserve(batch, entityType, rows)`, which computes each table's deficit (rows with `Id == default`, roots and supplied children, recursively, minus what the buffer can hold for this reservation), **holds** the buffered part immediately, and adds one statement per short sequence to the batch:

```sql
DECLARE @tb1_f sql_variant, @tb1_l sql_variant;
EXEC sys.sp_sequence_get_range N'[gl].[sq_Centers]', @tb1_p0, @tb1_f OUTPUT, @tb1_l OUTPUT;
SELECT CAST(@tb1_f AS int) AS [First], CAST(@tb1_l AS int) AS [Last],
       (SELECT MAX([Id]) FROM [gl].[Centers]) AS [MaxId];
```

The returned `IdReservation` is a hold: `Assign(rows)` (after validation passed) assigns ids in topological order and wires every new child's `[ParentKey]` to its new parent, consuming the hold; `Dispose()` without `Assign` returns every held range to the buffer, so a save that fails validation produces no gap. Concurrent saves on one instance never double-count a buffer: a hold removes ranges from the buffer at `Reserve` time.

**Self-healing.** If `First <= MaxId`, the sequence is behind an out-of-band insert. The allocator discards the range and, in one extra round trip, calls `sp_sequence_get_range` with `@range_size = (MaxId - First + 1) + deficit`, keeping only the tail above `MaxId` — consuming the gap needs only `UPDATE` on the sequence, never `ALTER`. A persist that fails with a PK violation (2627) on a table the allocator serves triggers the same heal once, reassigns ids to the in-memory batch (parent keys rewired), and the persist is retried once. Every heal logs and increments `tellma.data.ids.desync`, which should be alerted on.

**Sizing.** No prefetch, no headroom: the deficit is the range size. The buffer holds only returned leftovers and is capped per sequence (default 1 024; excess is dropped and logged). Gaps come from process crashes with leftovers, from a reservation whose round trip failed after the procedure ran (autocommitted; the range is lost), and from the engine's sequence cache on a SQL crash (a distribution may `ALTER SEQUENCE … CACHE 50` in a raw migration, since EF cannot emit `CACHE`).

**Who assigns.** The data layer (`IdReservation.Assign`); the pipeline chooses the moment. A pipeline with no pre-persist round trip pays one dedicated reservation round trip. Migrator seeds use the same path.

**Rationale.** Exact reservation on an existing round trip has zero extra round trips and minimal gaps; a background prefetcher trades gaps for nothing once reservation is free.

**Rejected.** Background prefetch of large ranges (gaps on every restart, exhaustion risk on `int`); `NEXT VALUE FOR … OVER (ORDER BY)` in `INSERT … SELECT` (children need parent ids before the TVP is built); `bigint` everywhere; `ALTER SEQUENCE RESTART` (DDL permission on the web identity).

**Confidence.** High. The web identity needs `UPDATE` on every `sq_<Table>` (host theme).

### D11 — `DataBatch`: one round trip of concatenated text, one `SqlCommand`, `NextResult()`, an in-text transaction, executor-owned retry

**Decision.** `ITenantDatabase.CreateBatch(purpose)` returns an `IDataBatch`. Statements are added through typed methods (D12), each returning a `BatchResult<T>` handle resolved after `ExecuteAsync`. The executor:

1. Asks every registered `IDataBatchContributor` (scoped DI; ordered) to add prologue statements (the users theme's connect and tag reads, guided by `Purpose`) and, after all caller statements are in place, epilogue statements (the settings theme's tag bumps computed from `WrittenTables`).
2. Assigns each statement a batch ordinal `b`. Queryex statements compile with `BatchOrdinal = b` (names `@qx{b}_…`); every other statement names its scalars `@tb{b}_p{n}`, its TVPs `@tb{b}_t{n}`, and its table variables `@tb{b}_<word>` — `@tb` is the platform's second reserved prefix beside `@qx`.
3. Concatenates the texts under `SET NOCOUNT ON;`. When any statement declares a write and `TransactionMode` is `Auto` (default), the text is wrapped `SET XACT_ABORT ON; BEGIN TRAN; … COMMIT;` with `COMMIT` asserted to be the last statement. `TransactionMode.None` leaves autocommit (lease renewal and similar idempotent bookkeeping only). `TransactionMode.Explicit` opens a `SqlTransaction` from C# and commits from C# — the escape hatch for a decision that must be taken in C# before commit, costing two more round trips.
4. Executes one `SqlCommand` (`CommandType.Text`; one `SqlParameter` per scalar; `SqlDbType.Structured` with the **physical** UDTT name per TVP, bound as a streaming `IEnumerable<SqlDataRecord>` built from `TableTypeDefinition.Columns` order) and walks result sets with `NextResult()`, handing each to the statement that declared it. Every statement declares its result-set count; a mismatch at the end is an internal error.
5. Counts the round trip on the scoped `DataAccessScope` and records the meters (D23).

**Retry.** Errors are classified from `SqlException.Number` using the driver's `SqlConfigurableRetryFactory` baseline transient list (read at startup) plus 1205 and 1222:

- *Reported* transient failure (the server returned an error; under `XACT_ABORT ON` the transaction is rolled back): the whole round trip is re-executed, regardless of idempotence, up to 3 attempts with exponential backoff and jitter. Deadlock victims are the common case.
- *Ambiguous* failure (connection reset, timeout; numbers 0, -2, 10053, 10054, 233, 997): re-executed only when **every** statement is `Idempotent`; otherwise `DataAccessAmbiguousException` carrying the original.
- `TransactionMode.None` batches are retried only when every statement is idempotent, in both classes.

TVPs are re-enumerated on retry.

**Error mapping.** 2601/2627 → `UniqueConstraintViolationException(IndexName)`; 547 → `ForeignKeyViolationException(ConstraintName)`; 530 → `TreeDepthExceededException`; the platform's own `THROW` numbers (`DataErrorNumbers`): 50401 → `ConcurrencyConflictException(ConflictingIds, MissingIds, Truncated)`, 50403 → `RowSecurityException`, 50430 → `TreeCycleException`, other 50400–50499 → `BatchAssertionFailedException(Number)`. Mapping index names to field-level validation errors is the pipeline's job, through `EntityMetadata.UniqueIndexes`.

**Rationale.** Concatenated text sends a shared TVP once, yields one OpenTelemetry span with the whole text, allows the in-text transaction, and is what Queryex's `BatchOrdinal` was designed for. `SqlBatch` sends a TVP per command, has no retry provider, and shows only the last statement to telemetry. `TransactionScope` is Serializable by default, suppresses async flow, and throws for distributed transactions on Linux.

**Confidence.** High.

### D12 — The `IDataBatch` API: typed statements, key capture, structural deduplication, declared writes

**Decision.** The consumer-facing statements (full shape in §3):

- `Query<TEntity>(EntityQuery<TEntity>)` → `EntityQueryResult<TEntity>` — a Queryex query materialised into entities plus a related-entity dictionary (D19).
- `Rows(QuerySpec, QueryArguments?, RowQueryOptions?)` → `RowSet` — arrays of arrays with `QueryexColumn` metadata (grids, exports); options request a capped count and ancestors in the same statement (D19).
- `Count(QuerySpec, QueryArguments?, cap)` → `int` — capped grand total: `Select = "Id"`, `OrderBy = "Id"`, `Take = cap + 1`, wrapped as `SELECT COUNT(*) FROM (<body>) AS q`; a result of `cap + 1` means "more than `cap`".
- `Save<TEntity>(rows, SaveOptions?)` → `SaveReceipt` — D13.
- `Update<TEntity>(UpdateSpec<TEntity>)` → `UpdateReceipt` — a stamped bulk column assignment (activate, deactivate, any state action): `UPDATE t SET [IsActive] = @tb5_p0, [ModifiedAt] = @tb5_now, [ModifiedById] = @tb5_user OUTPUT inserted.[Id] FROM [gl].[Centers] AS t WHERE t.[Id] IN (SELECT [Id] FROM @tb5_keys)`, where `@tb5_keys` is captured from a keys query (ids restriction conjoined with the row-level-security filter); assigned properties must be server-owned or editable, never write-once; tree tables append the recount (D16).
- `Delete<TEntity>(DeleteSpec<TEntity>)` → `DeleteReceipt` — D17.
- `Sql(FormattableString, SqlOptions)` → `RawResult` — raw T-SQL whose holes become parameters (scalars; TVPs for lists of `[TableType]` rows) or identifiers (`SqlIdentifier`); `SqlOptions` declares `Writes`, `Idempotent`, `ResultSets`.
- `Assert(QuerySpec countSpec, QueryArguments?, int expected, int errorNumber)` — `IF (SELECT COUNT(*) FROM (<body>) AS q) <> @tb_p0 THROW <errorNumber>, N'…', 1;` inside the transaction; used for the row-level-security post-check (50403) and delete visibility.
- `Purpose: BatchPurpose` (`Read | Validate | Persist | Maintenance`), set at creation; contributors consult it (the connect prologue never rides a `Persist` batch).

**Key capture.** Any `Query`/`Rows` statement may capture its root keys: the batch compiles a keys-only twin of the spec (`Select = "Id"`, same filter, ordering, paging) into `INSERT INTO @tb{b}_keys ([Id]) <body>`, then compiles the display query restricted to `Id IN @tb{b}_keys` with the ordering kept and paging dropped. Later statements name `@tb{b}_keys` as a `KeySetRestriction` source: child collections (`ParentKey IN @keys`), ancestors, the RLS post-check, delete-by-query, update-by-query. `Query` captures when `Children` is non-empty and no ids restriction fixes the roots; `Rows` captures when ancestors are requested; deletes and updates always capture.

**Deduplication.** `Query`, `Rows`, `Count` compute a structural key (root, select text, filter tree, having, order, skip, take, arguments by value, restrictions, options) and return the existing handle for an identical key. `Sql` dedups on (text, parameter values). A select that is a subset of another is not merged in this release.

**Declared writes.** `WrittenTables` is the union of every statement's declared set; save, update, delete and lease statements declare it from metadata; raw SQL declares it in `SqlOptions.Writes` (the analyzer of D23 flags DML without a declaration).

**Confidence.** High on the shape; medium on the exact method set, which the pipeline theme will stretch.

### D13 — `SaveEmitter`: two TVPs per table, separate statements, a locking guard, aggregate-scoped stamping, unchanged rows skipped

**Decision.** `Save<TEntity>(rows, options)` names the root type and the rows with their `[NotMapped]` children populated or null. Per table in topological order (roots first, then each child collection, recursively) the emitter binds **two TVPs** of the table's own UDTT — `@tb{b}_t{i}` new rows (`Id` assigned), `@tb{b}_t{i+1}` existing rows — plus, per child collection, an `IdList` TVP of the parents whose collection was supplied. For a root table with children (`core.Users` with `core.RoleMemberships`), batch ordinal 3:

```sql
DECLARE @tb3_now datetime2(7) = SYSUTCDATETIME();
DECLARE @tb3_touched TABLE ([Id] int PRIMARY KEY);
DECLARE @tb3_conflicts TABLE ([Id] int PRIMARY KEY, [Reason] char(1) NOT NULL);

-- 1. Guard. U locks on every existing root are held to COMMIT, so nothing below can race the check.
--    A missing row is a conflict even under override: override rewrites, it never resurrects.
INSERT INTO @tb3_conflicts ([Id], [Reason])
SELECT s.[Id], CASE WHEN t.[Id] IS NULL THEN 'M' ELSE 'C' END
FROM @tb3_t1 AS s
LEFT JOIN [core].[Users] AS t WITH (UPDLOCK, ROWLOCK) ON t.[Id] = s.[Id]
WHERE t.[Id] IS NULL OR (@tb3_p0 = 0 AND t.[ModifiedAt] <> s.[ModifiedAt]);   -- @tb3_p0 = override flag
IF EXISTS (SELECT 1 FROM @tb3_conflicts)
BEGIN
    DECLARE @tb3_msg nvarchar(2048) = N'{"count":' + CAST((SELECT COUNT(*) FROM @tb3_conflicts) AS nvarchar(10))
        + N',"conflicts":[' + ISNULL((SELECT STRING_AGG(CAST([Id] AS nvarchar(20)), ',') FROM (SELECT TOP (100) [Id] FROM @tb3_conflicts WHERE [Reason] = 'C' ORDER BY [Id]) AS c), N'')
        + N'],"missing":['   + ISNULL((SELECT STRING_AGG(CAST([Id] AS nvarchar(20)), ',') FROM (SELECT TOP (100) [Id] FROM @tb3_conflicts WHERE [Reason] = 'M' ORDER BY [Id]) AS m), N'')
        + N']}';
    THROW 50401, @tb3_msg, 1;
END;

-- 2. Insert new roots; server-owned columns are stamped here.
INSERT INTO [core].[Users] ([Id], [Subject], [Email], [Name], [Name2], [Name3], [IsActive],
    [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
SELECT s.[Id], s.[Subject], s.[Email], s.[Name], s.[Name2], s.[Name3], s.[IsActive],
    @tb3_now, @tb3_p1, @tb3_now, @tb3_p1                                       -- @tb3_p1 = current user id
FROM @tb3_t0 AS s;

-- 3. Synchronise each supplied child collection (parents in @tb3_t2, new children @tb3_t3, existing @tb3_t4).
DELETE c OUTPUT deleted.[UserId] INTO @tb3_touched ([Id])
FROM [core].[RoleMemberships] AS c
WHERE c.[UserId] IN (SELECT [Id] FROM @tb3_t2)
  AND NOT EXISTS (SELECT 1 FROM @tb3_t3 AS s WHERE s.[Id] = c.[Id])
  AND NOT EXISTS (SELECT 1 FROM @tb3_t4 AS s WHERE s.[Id] = c.[Id]);
UPDATE c SET c.[RoleId] = s.[RoleId], c.[Notes] = s.[Notes]
OUTPUT inserted.[UserId] INTO @tb3_touched ([Id])
FROM [core].[RoleMemberships] AS c JOIN @tb3_t4 AS s ON s.[Id] = c.[Id]
WHERE EXISTS (SELECT s.[RoleId], s.[Notes] EXCEPT SELECT c.[RoleId], c.[Notes]);
INSERT INTO [core].[RoleMemberships] ([Id], [UserId], [RoleId], [Notes])
OUTPUT inserted.[UserId] INTO @tb3_touched ([Id])
SELECT s.[Id], s.[UserId], s.[RoleId], s.[Notes] FROM @tb3_t3 AS s;

-- 4. Update existing roots last: editable columns only; changed or touched rows only; stamp.
UPDATE t SET t.[Name] = s.[Name], t.[Name2] = s.[Name2], t.[Name3] = s.[Name3],
    t.[ModifiedAt] = @tb3_now, t.[ModifiedById] = @tb3_p1
OUTPUT inserted.[Id]                                                          -- result set 1: stamped ids
FROM [core].[Users] AS t JOIN @tb3_t1 AS s ON s.[Id] = t.[Id]
WHERE EXISTS (SELECT s.[Name], s.[Name2], s.[Name3] EXCEPT SELECT t.[Name], t.[Name2], t.[Name3])
   OR t.[Id] IN (SELECT [Id] FROM @tb3_touched);

SELECT @tb3_now AS [Stamp];                                                    -- result set 2
```

`@tb3_touched` collects distinct parent ids (`INSERT` into a `PRIMARY KEY` table variable would reject duplicates, so the emitter declares it without the key and reads it through `DISTINCT`). Rules encoded:

- **Server-owned and write-once columns never appear in a `SET` list.** On `INSERT` the audit columns are stamped; other server-owned columns come from the in-memory row after the pipeline reset them.
- **The guard takes U locks** (`UPDLOCK, ROWLOCK`) on every existing root and holds them to `COMMIT`, so the stamp check is sound under both locking read committed and read-committed snapshot (a locking hint reads the latest committed version). Two saves of overlapping sets can deadlock; 1205 is retried.
- **Unchanged rows are skipped** (`EXCEPT` over the editable columns treats `NULL = NULL`), so a temporal table gets no history row for a no-op; a child change marks its parent touched; a touched or changed root is stamped. The root's `ModifiedAt` moves exactly when the aggregate changed, which is what makes aggregate-level concurrency sound.
- **The stamp is server time**, once per batch; the receipt reports which root ids received it and the emitter stamps the in-memory entities (new rows: all four audit columns; stamped roots: `ModifiedAt`/`ModifiedById`).
- **The client's `ModifiedAt` is the expected stamp**, carried in the existing-rows TVP's own column; `@tb_p0 = 1` (the pipeline's override flag) disables the stamp comparison, not the existence check.
- **`OUTPUT … INTO` targets are table variables** and every `OUTPUT` to the client comes from a table without triggers (the no-logic-in-the-database rule).
- Tree tables append D16's statements after step 4; the emitter caches generated text per (entity metadata, statement kind).
- Import chunks large saves at 10 000 root rows per round-trip pair; `SqlBulkCopy` into a staging table is a later, measured addition.

**Conflict message.** The `THROW` message is JSON (`count`, up to 100 `conflicts` ids, up to 100 `missing` ids; `Truncated` when `count` exceeds what was listed); the pipeline recovers a full list, when it needs one, with one read over the payload's ids and stamps.

**Rationale.** Separate statements sidestep every live `MERGE` defect; two TVPs make deleted-under-you a conflict; `EXCEPT` skipping keeps temporal history honest under merge imports (9 000 pointless history rows avoided on a 10 000-row import with 1 000 changes); server-side stamping removes clock skew between instances; U locks in the guard remove the guard-to-update race without a second count.

**Rejected.** `MERGE`; one TVP with `NOT EXISTS` (silent resurrection); always-update (history churn); app-side stamps (skew); a standalone `(Id, ExpectedStamp)` TVP (the row image already carries the stamp); a conflicts result set followed by `ROLLBACK; RETURN` (a stateful result-set walk).

**Confidence.** High.

### D14 — Bulk row binding is metadata-driven and streaming

**Decision.** `TableTypeBinder` (in `Tellma.Core.Data`, marked `[TableTypeBinder]` for the analyzer) builds `SqlMetaData[]` once per `TableTypeDefinition` from its ordered `Columns` (store type, size, precision, scale, JSON flag) and yields `SqlDataRecord`s lazily from the entity list through compiled accessors keyed by column name; `[JsonColumn]` strings bind as text; the shadow `Node` column is excluded from tree UDTTs by the tree convention's fluent `ExcludeFromTableType()`; period columns are not part of the row image (EF marks them generated). Bulk-list TVPs (`IdList`, `BigIdList`, `GuidList`, `StringList`) bind through the same binder from their `[TableType]` class definitions. Every type is addressed by its physical name from the app's own model.

**Confidence.** High.

### D15 — Optimistic concurrency: `ModifiedAt` is the token; no `rowversion`

**Decision.** The concurrency token of every top-level entity is `ModifiedAt` (`datetime2(7)`, UTC, server-stamped per D13). Rule: **user-visible mutations stamp it** (save; `Update` actions such as activate and deactivate), **bookkeeping never touches the entity row** (activity, tags, inbox tracking live in sibling tables; lease columns are updated only by lease statements, which never stamp). The client echoes `ModifiedAt` as loaded; the wire format preserves seven fractional digits. Conflicts surface as `ConcurrencyConflictException` with conflicting and missing ids; the pipeline's override flag re-runs the save with the flag set. Import hydrates `ModifiedAt` from the database in the validation round trip, so imported rows carry a real expected stamp. No platform table carries a `rowversion` column.

**Against `rowversion`.** It bumps on every update, including bookkeeping, and cannot let an action choose not to bump; it needs the same sibling-table discipline and is opaque. Its advantage — server-guaranteed uniqueness without clocks — `ModifiedAt` recovers by being stamped once per transaction from `SYSUTCDATETIME()` on one server; an equal stamp after a backwards clock step is accepted as astronomically unlikely and documented. `ModifiedAt` is also user-visible data, so it costs no extra column.

**Rejected.** A hash of editable columns; `rowversion`; last-writer-wins.

**Confidence.** Medium-high.

### D16 — Trees: `Node` is a shadow `hierarchyid` of the id path, re-pathed set-based over the affected set; cycles are fenced in SQL; counts recomputed set-based

**Decision.** `TreeEntity<TSelf>` (`TSelf` is the leaf, typing the `Parent` navigation) declares `ParentId: int?` (FK to self, `NO ACTION`), `Parent: TSelf?`, and `SubtreeCount: int` (server-owned). `ActivatableTreeEntity<TSelf>` adds `IsActive: bool` and `ActiveSubtreeCount: int` (server-owned). The platform's tree convention adds a **shadow** property `Node hierarchyid NOT NULL` (unique index `IX_<Table>_Node`, excluded from the UDTT); it is not a CLR member, so `Tellma.Core.Abstractions` stays EF-free and `hierarchyid` never crosses the TVP boundary. `Level` and `IsLeaf` do not exist (`level(Node)`; `SubtreeCount = 1`). The node is **the path of ids** — `/<root id>/…/<id>/` — so a row's node depends only on its ancestor chain, siblings never renumber, and a moved subtree is the only thing that changes when a row is re-parented. New rows are inserted with the provisional node `/-<id>/` (unique, never a real path) so `NOT NULL` and uniqueness hold until the re-path runs in the same transaction. `[Tree(MaxDepth = 32)]` sets the recursion cap.

After a tree table's save statements (new rows `@tb3_t0`, existing rows `@tb3_t1`):

```sql
-- T1. Affected set: every saved row, plus every descendant (by its pre-save node) of a saved existing row.
DECLARE @tb3_aff TABLE ([Id] int PRIMARY KEY);
INSERT INTO @tb3_aff ([Id])
SELECT [Id] FROM @tb3_t0
UNION SELECT [Id] FROM @tb3_t1
UNION SELECT d.[Id] FROM [gl].[Centers] AS d
      WHERE EXISTS (SELECT 1 FROM [gl].[Centers] AS b JOIN @tb3_t1 AS s ON s.[Id] = b.[Id]
                    WHERE d.[Node].IsDescendantOf(b.[Node]) = 1);

-- T2. Paths, top-down from the affected rows whose parent lies outside the set (those parents' nodes are final).
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
OPTION (MAXRECURSION 32);

-- T3. Cycle fence: an affected row no anchor reached lies on a cycle (a cycle's members all have parents inside the set).
IF (SELECT COUNT(*) FROM @tb3_paths) <> (SELECT COUNT(*) FROM @tb3_aff)
    THROW 50430, N'{"entity":"Center"}', 1;

-- T4. Re-path rows whose node changed.
UPDATE t SET t.[Node] = hierarchyid::Parse(x.[Path])
FROM [gl].[Centers] AS t JOIN @tb3_paths AS x ON x.[Id] = t.[Id]
WHERE t.[Node] <> hierarchyid::Parse(x.[Path]);

-- T5. Recount rows whose counts changed (master-data sized tables; the Node index makes each count a range seek).
UPDATE t SET t.[SubtreeCount] = x.[Cnt], t.[ActiveSubtreeCount] = x.[ActiveCnt]
FROM [gl].[Centers] AS t
CROSS APPLY (SELECT COUNT(*) AS [Cnt], SUM(CASE WHEN d.[IsActive] = 1 THEN 1 ELSE 0 END) AS [ActiveCnt]
             FROM [gl].[Centers] AS d WHERE d.[Node].IsDescendantOf(t.[Node]) = 1) AS x
WHERE t.[SubtreeCount] <> x.[Cnt] OR t.[ActiveSubtreeCount] <> x.[ActiveCnt];
```

For a `TreeEntity` that is not activatable, T5 assigns `SubtreeCount` only. Exceeding `MaxDepth` fails with error 530 → `TreeDepthExceededException`; the count mismatch in T3 → `TreeCycleException`; both roll back the batch, which is the fence that makes the C# check sufficient. T5 also runs after `Update` actions on activatable trees and after tree deletes.

**C# cycle validation** (a validator the pipeline runs; `TreeCycleValidator<T>` ships here): in the validation round trip it adds `Rows` of `(Id, ParentId)` over the whole tree table, overlays the batch's `ParentId` values, walks up from every saved row, and reports a revisit as a validation error on that row's `ParentId`. Under write skew the SQL fence catches what the C# check missed.

**Rationale.** Id paths remove `ROW_NUMBER` renumbering and make the affected set exactly "saved rows and their descendants"; the recursive path build per affected root is the research recommendation; the recount touches only rows whose values change, so temporal churn is zero elsewhere.

**Rejected.** In-memory recomputation (needs the subtree in C# and per-row `GetDescendant`); `ROW_NUMBER` sibling ordinals (renumber on delete); `Level`/`IsLeaf` columns; a persisted computed `Level` for a breadth-first index (children are found by `ParentId`, indexed by the FK); a CLR `Node` property (needs `HierarchyId` in Abstractions or a platform value type plus a converter).

**Confidence.** Medium-high. **Review flag:** whole-table recount versus an affected-ancestors recount; the latter matters only above roughly 100 000 rows, which no reference entity approaches.

### D17 — Deletes: keys captured, visibility asserted, children first, ids read back

**Decision.** `DeleteSpec<TEntity>` has three shapes, each with an optional `Filter: FilterTree?` (the pipeline's row-level-security tree): `ByIds(ids)`, `ByQuery(filter)`, `WithDescendants(ids)` (tree only). The emitter first fills `@tb4_keys` — from a keys query restricted to the ids (or the query) and conjoined with `Filter`; for `WithDescendants` the descendant closure is added and then asserted visible:

```sql
DECLARE @tb4_keys TABLE ([Id] int PRIMARY KEY);
INSERT INTO @tb4_keys ([Id])
SELECT d.[Id] FROM [gl].[Centers] AS d
WHERE EXISTS (SELECT 1 FROM [gl].[Centers] AS a JOIN @tb4_t0 AS s ON s.[Id] = a.[Id]
              WHERE d.[Node].IsDescendantOf(a.[Node]) = 1);
IF (SELECT COUNT(*) FROM (<keys body: Id IN @tb4_keys AND <Filter>>) AS q) <> (SELECT COUNT(*) FROM @tb4_keys)
    THROW 50403, N'', 1;
DELETE c FROM [core].[RoleMemberships] AS c WHERE c.[UserId] IN (SELECT [Id] FROM @tb4_keys);   -- per child table, deepest first
DELETE t OUTPUT deleted.[Id] FROM [gl].[Centers] AS t WHERE t.[Id] IN (SELECT [Id] FROM @tb4_keys);
```

The receipt reports the ids actually deleted; a requested id that is absent was missing or not visible, which the pipeline reports uniformly. No `ON DELETE CASCADE` anywhere: deleting a tree row whose children were not requested fails with 547 → `ForeignKeyViolationException`, which the pipeline turns into a validation error. Tree tables append the recount (D16 T5). Deletes bump tags through the contributor path like saves.

**Confidence.** High.

### D18 — The Queryex schema is built from the EF model per language profile and cached by that key

**Decision.** `QueryexSchemaFactory.Get(SchemaProfile)` builds once per distinct profile (value equality; at most three profiles per model) a `QueryexSchema` from the runtime `IModel` through `QueryexSchemaBuilder`:

- **Entities:** every mapped leaf entity type; logical name = CLR type name (`User`, `RoleMembership`, `Center`; a distribution leaf keeps the pack's name, which the naming rule for leaves already implies); `Source` = `[schema].[Table]`; `Key` = the PK property.
- **Properties:** every mapped scalar (CLR or shadow) whose store type has a Queryex type, with `Column`, `IsNotNull` from the model, `IsUnique` when the PK or a single-column unique index (filtered included) covers it, `StoreType` structured from EF's `RelationalTypeMapping` (enums through their converter's store type). `[NotMapped]` members, period columns, `[JsonColumn]` strings, `varbinary`, `time`, `float`/`real` are not declared. Multilingual members `P2`/`P3` are declared only when `profile.LanguageCount` is at least 2/3.
- **Navigations:** every many-to-one EF foreign key, named by the CLR navigation when one exists, else by the FK property name minus its `Id` suffix (`CustomerId` → `Customer`; a collision is a startup error). A `[ParentKey]` FK yields the child's navigation to its parent (`RoleMembership.User`).
- **Tree node:** the shadow `Node` of every tree entity, declared as `HierarchyId` and registered as `TreeNode`.

`SchemaProfile` is a record `(LanguageCount: int)` today, supplied by the settings theme through the request context; anything that later reshapes the schema per tenant is a new field. Row-level-security composition and child-entity path rewriting are `FilterTree` transformations at request time, never schema variants. A `map<EntityDescriptor, EntityMetadata>` built beside the schema lets the materializer map descriptors back to CLR types without an engine change. A cold instance needs the tenant's language count before its first schema — the settings read precedes it, one extra round trip on cold start.

**Rationale.** The engine caches by schema identity; building per tenant would defeat every cache, building once would leak `Name3` across tenants. Per profile is the smallest correct key.

**Confidence.** High.

### D19 — Entity queries and the materializer: bare paths, auto-appended keys, related entities by (entity, id), children and ancestors in the same round trip

**Decision.** Two host-level shapes over one `QuerySpec`:

- `Rows(spec, args, options)` — anything Queryex compiles; returns `RowSet` (`Columns`, `Rows`; values are the provider's CLR values, `DBNull` as null, a `HierarchyId` column read from its binary form and returned as its path string). `RowQueryOptions.Count(cap)` adds the capped count (D12) in the same statement; `RowQueryOptions.Ancestors(filter)` (tree entities) captures the page keys, fills `@tb{b}_anc` with the ancestors of the page rows that are not themselves on the page, and appends the same select restricted to `@tb{b}_anc` under `filter` (the access filter alone — ancestors need not satisfy the user's filter but must be visible), ordering kept, no paging; `RowSet.Ancestors` carries them.
- `Query<TEntity>(EntityQuery<TEntity>)` — `Select` is restricted to bare paths (a computed item is a caller error). The host runs `Discover` on the select text (cached by the engine) to learn the paths, then appends the root `Id`, and for every navigation prefix used its key path (`Customer.Id`) and the FK on its owner (`CustomerId`). The materializer walks `QueryexColumn.Path`: depth-1 columns land on the root instance (through the property's EF converter, so enum strings become enums); deeper columns land on a related instance of the CLR type behind the path's descriptor, keyed by (logical entity name, id) in `EntityQueryResult.Related`, deduplicated across rows and never assigned to navigation properties (no cycles on the wire). Related instances are partial (selected columns only) and never travel back into a save.
- `EntityQuery.Children` names collections to load. Each adds a `Query` over the child restricted by `ParentKey IN <source>`, where the source is the root's ids TVP when the root is restricted by ids, else the root's captured keys (D12). Children are stitched onto their parents after execution; grandchildren recurse. `EntityQuery.Ancestors` behaves as for `Rows`.

**Rejected.** An `Expand` string (a select list of paths already says what to load); loading children by a hidden second round trip; refusing children on filtered pages (key capture makes them one round trip).

**Confidence.** High.

### D20 — Queryex engine amendments this theme needs (documented in spec 0011; spec 0008 is frozen)

**Decision.** Three additive changes; `QueryexLanguage.Version` stays 1:

1. **Key-set restriction.** `QuerySpec.Restrictions: list<KeySetRestriction>` with `KeySetRestriction(Path: string, TableSource: string)`. The engine binds `Path` in Filter mode (bare path; joins as needed), validates `TableSource` as a plain `@`-identifier outside the `@qx` namespace, and emits `AND <column> IN (SELECT [Id] FROM @<TableSource>)` conjoined after the filter tree. The host binds a TVP (`IdList`, `BigIdList`, `GuidList`, `StringList`) or fills a batch table variable with an `[Id]` column. Restrictions participate in the L3 cache key and in structural deduplication. Consumers: get-by-ids, get-by-parent-ids, child loading, ancestors, deletes, updates, validation context, natural-key translation.
2. **`level(node: HierarchyId) -> Numeric`**, `NotNull` when its argument is, emitted as `.GetLevel()`. Additive registry entry.
3. **Body/prologue split.** `CompiledQuery` gains `Prologue` (hoisted `DECLARE`s; may be empty) and `Body` (the `SELECT`), with `Sql = Prologue + Body`. A host embeds `Body` as a derived table (`SELECT COUNT(*) FROM (<body>) AS q`; legal with `ORDER BY … OFFSET … FETCH`) or after `INSERT INTO @t ([Id])`; the prologue is emitted earlier in the same batch. Consumers: capped counts, key capture, assertions, delete-by-query.

**Rejected.** A language-level `x in (@ids)` list parameter (needs a version bump and a list type); `descendantOf` over a TVP (restrictions on `Id`/`ParentId` cover it).

**Confidence.** High.

### D21 — Context values are bound from the request context; the data layer computes none of them

**Decision.** `ITenantDatabase` is created per request (or per background scope) from the host theme's request context and carries `QueryexContextValues(Today: DateOnly, Now: DateTimeOffset, UserId: int?, TimeZoneName: string)`. The executor binds `Today`/`Now`/`UserId`/`TimeZone` slots from it and `Literal`/`Declared` slots from the compiled query and the caller's `QueryArguments` (a declared parameter bound against several store types fills every slot of that name from one value). Background scopes construct their `ITenantDatabase` through `ITenantDatabaseFactory.Open(tenant, context)`; nothing here uses `AsyncLocal`. Which calendar date `Today` is (tenant zone per spec 0008's wording, or the user's zone from a header) is the web theme's decision; the data layer binds what it is given.

**Confidence.** High.

### D22 — Lease columns and lease statements the emitter produces (semantics owned by the background-tasks theme)

**Decision.** `ILeasable` is a capability interface with four properties a task-shaped entity declares: `LeaseOwner: uniqueidentifier?`, `LeaseExpiresAt: datetime2(3)?`, `LeaseToken: bigint` (fencing token, incremented on every acquisition), `Attempts: int`. All four are server-owned; lease statements update only these columns and never `ModifiedAt`. `LeaseStatements<T>` (in `Tellma.Core.Data`) exposes batch statements built from metadata and from the consumer-declared state and due columns (`[LeaseState]`, `[LeaseDue]` annotations owned by the background theme):

```sql
-- Acquire: ordered, skip-locked, bounded, fenced. @tb0_p0 batch size (≤ 1 000), @tb0_p1 now, @tb0_p2 owner, @tb0_p3 lease until.
WITH tb0_due AS (
    SELECT TOP (@tb0_p0) t.[Id], t.[LeaseOwner], t.[LeaseExpiresAt], t.[LeaseToken], t.[Attempts]
    FROM [core].[ExportJobs] AS t WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE t.[State] = 'Queued' AND (t.[LeaseExpiresAt] IS NULL OR t.[LeaseExpiresAt] < @tb0_p1)
    ORDER BY t.[DueAt], t.[Id])
UPDATE tb0_due SET [LeaseOwner] = @tb0_p2, [LeaseExpiresAt] = @tb0_p3,
    [LeaseToken] = [LeaseToken] + 1, [Attempts] = [Attempts] + 1
OUTPUT inserted.[Id], inserted.[LeaseToken];
-- Renew (idempotent): UPDATE t SET [LeaseExpiresAt] = @tb1_p0 FROM … t WHERE t.[Id] IN (SELECT [Id] FROM @tb1_t0)
--   AND t.[LeaseOwner] = @tb1_p1 AND t.[LeaseToken] = <token per row, carried in a standalone (Id, Token) table type>;
-- Release / Fail: the same fenced predicate; state-column transitions are the consumer's.
```

`Acquire` is not idempotent; `Renew` is; both declare `WrittenTables`. `READPAST` is paired with `UPDLOCK` because a bare `READPAST` is a no-op under read-committed snapshot; `ROWLOCK` limits escalation but does not prevent it, hence the batch cap.

**Confidence.** Medium (the background theme may reshape the predicate).

### D23 — Instruments, the DB-call budget, and the analyzers

**Decision.** Meter `Tellma.Core.Data` through `IMeterFactory`; names as constants in `Tellma.Core.Data.Abstractions.DataMeter`:

| Instrument | Kind | Unit | Tags |
|---|---|---|---|
| `tellma.data.roundtrips` | histogram per scope | `{roundtrip}` | `operation`, `entity` |
| `tellma.data.roundtrip.duration` | histogram | `s` | `operation`, `purpose`, `retried` |
| `tellma.data.batch.statements` | histogram | `{statement}` | `operation` |
| `tellma.data.retries` | counter | `{retry}` | `class` (`reported`/`ambiguous`), `error` |
| `tellma.data.ids.reserved`, `tellma.data.ids.returned`, `tellma.data.ids.desync` | counters | `{id}`, `{id}`, `{event}` | `sequence` |
| `tellma.data.concurrency.conflicts` | counter | `{conflict}` | `entity` |
| `tellma.data.tree.recomputes` | counter | `{statement}` | `entity` |
| `tellma.data.rows.saved`, `tellma.data.rows.deleted` | counters | `{row}` | `entity` |

No per-tenant tags. `DataAccessScope` (scoped; copied into background scopes) accumulates round trips and retries for the current operation and stamps `tellma.data.roundtrips` on the request `Activity` when it ends; tests assert budgets with `SqlConnection.RetrieveStatistics()["ServerRoundtrips"]` as an independent oracle.

Analyzers in `Tellma.Core.Analyzers`, referenced by every distribution through the platform targets:

- `TELLMA0001` — `SqlDataRecord` constructed, or `SqlDataRecord.Set*`/`GetOrdinal` called, outside a type marked `[TableTypeBinder]` (hard-coded ordinal binding).
- `TELLMA0002` — a raw `Sql(...)` statement whose text (parsed with `Microsoft.SqlServer.TransactSql.ScriptDom`) contains `INSERT`/`UPDATE`/`DELETE`/`MERGE`/`EXEC` without `Writes` declared, or whose holes are neither `SqlIdentifier`, `nameof`, nor a value; `MERGE` anywhere is its own diagnostic.
- `TELLMA0003` — `DbContext.SaveChanges`/`SaveChangesAsync` outside the migrator project.

**Confidence.** High.

### D24 — Tests: fixture entities on LocalDB and Testcontainers, three tiers

**Decision.** `test/core/Tellma.Core.Data.Tests` (unit: emitter golden SQL per statement kind against fixture metadata, allocator hold arithmetic, metadata validation errors, materializer routing, natural-key inference, enum sizing, retry classification) and `test/core/Tellma.Core.Data.IntegrationTests` (`Category=Integration`; LocalDB on Windows, Testcontainers SQL Server on Linux, one connection-string environment variable) with a fixture `DbContext` and migrations for schema `fixture` (§4.2). Integration assertions: round-trip budgets per operation (statistics oracle); skip-unchanged writes no history row; the aggregate stamp moves when only a child changes; deleted-under-you is a conflict; override writes; sequence desync heals once by consuming; two racing saves produce exactly one conflict; a cycle written under write skew fails in SQL; a saved row whose parent is a descendant of another saved row is pathed once; id-path nodes survive re-parenting; `READPAST` + `UPDLOCK` acquisition never double-leases across two connections; `hierarchyid` never crosses the TVP boundary; `Properties<Enum>()` matches nullable enums; `OUTPUT inserted.[Id]` on a system-versioned table; capped count returns `cap + 1`.

**Confidence.** High.

### D25 — Package pins and the tenant-database isolation invariant

**Decision.** `Microsoft.EntityFrameworkCore.SqlServer.HierarchyId` 10.0.11 is added, which raises `Microsoft.EntityFrameworkCore.*` to 10.0.11 and `Microsoft.Data.SqlClient` to 6.1.6 (6.1.5 also fixed a transaction-zombie bug). Every tenant database is created with `READ_COMMITTED_SNAPSHOT ON` by the migrator at provisioning (a single-connection moment), so validation reads behave identically on Azure SQL and on-prem; the platform has one isolation story and the persist statements rely on locking hints, not on the database default. Connection strings carry an explicit `Application Name` so EF's injected name and the data layer's raw connections share one pool key.

**Confidence.** High.

---

## 3. Contracts

Contract blocks use the platform's contract notation: names are normative; shape is described, not transcribed.

### 3.1 Entity contract — `Tellma.Core.Abstractions.Entities`

```contract
base Entity<TKey>                       // TKey is int or long
  Id: TKey                              server-owned, PK; default means new

base TopLevelEntity<TKey> : Entity<TKey>
  CreatedAt: datetime2(7)               server-owned
  CreatedById: int                      server-owned, FK -> core.Users
  ModifiedAt: datetime2(7)              server-owned, concurrency token
  ModifiedById: int                     server-owned, FK -> core.Users

base TopLevelEntity : TopLevelEntity<int>
base ChildEntity<TKey> : Entity<TKey>   // saved only with its top-level owner; no audit columns
base ChildEntity : ChildEntity<int>

base TreeEntity<TSelf> : TopLevelEntity   where TSelf: TreeEntity<TSelf>
  ParentId: int?                        FK -> self
  Parent: TSelf?                        navigation, not a column
  SubtreeCount: int                     server-owned; 1 for a leaf
  // Node hierarchyid is a shadow column added by the platform's tree convention, never a member

base ActivatableTreeEntity<TSelf> : TreeEntity<TSelf>, IActivatable
  IsActive: bool = true                 server-owned
  ActiveSubtreeCount: int               server-owned

contract IActivatable
  IsActive: bool                        server-owned by derivation; changed through Update actions

contract ILeasable
  LeaseOwner: uniqueidentifier?         server-owned
  LeaseExpiresAt: datetime2(3)?         server-owned
  LeaseToken: bigint = 0                server-owned, fencing token
  Attempts: int = 0                     server-owned

annotation [Temporal(enabled = true)]  on type; inherited; children follow the parent unless [Temporal(false)]
annotation [Tree(MaxDepth = 32)]       on type; inherited
annotation [ParentKey]                 on property: the child's owning foreign key
annotation [Children(parentKey)]       on a [NotMapped] collection: which child foreign key it follows
annotation [ServerOwned]               on property
annotation [WriteOnce]                 on property
annotation [NaturalKey]                on property
annotation [JsonColumn]                on string property: nvarchar(max), opaque text
```

Illustration (a distribution's tree entity with activation):

```csharp
[Table("Centers", Schema = "gl"), TableType]
public sealed class Center : ActivatableTreeEntity<Center>
{
    public CenterType Type { get; set; }
    [MaxLength(255)] public string Name { get; set; } = null!;
    [MaxLength(255)] public string? Name2 { get; set; }
    [MaxLength(255)] public string? Name3 { get; set; }
    [MaxLength(50)] public string? Code { get; set; }
}
```

Nine lines plus the feature's `entities.Add<Center>()` registration (host theme): no UDTT class, no SQL, no repository, no child mapping, no tree code.

### 3.2 Data access API — `Tellma.Core.Data.Abstractions`

```contract
service ITenantDatabase                 // one per request or background scope
  Schema: QueryexSchema                 // for this tenant's language profile
  Context: QueryexContextValues
  CreateBatch(purpose: BatchPurpose) -> IDataBatch    sync

service ITenantDatabaseFactory
  Open(tenant: TenantId, context: QueryexContextValues, profile: SchemaProfile) -> ITenantDatabase

record QueryexContextValues(Today: DateOnly, Now: DateTimeOffset, UserId: int?, TimeZoneName: string)
record SchemaProfile(LanguageCount: int)
enum BatchPurpose = Read | Validate | Persist | Maintenance
enum TransactionMode = Auto | None | Explicit

service IDataBatch
  Purpose: BatchPurpose
  TransactionMode: TransactionMode = Auto
  WrittenTables: set<TableName>
  Query<TEntity>(query: EntityQuery<TEntity>) -> BatchResult<EntityQueryResult<TEntity>>     sync
  Rows(spec: QuerySpec, arguments: QueryArguments?, options: RowQueryOptions?) -> BatchResult<RowSet>   sync
  Count(spec: QuerySpec, arguments: QueryArguments?, cap: int = 10000) -> BatchResult<int>   sync
  Save<TEntity>(rows: list<TEntity>, options: SaveOptions?) -> BatchResult<SaveReceipt>      sync
  Update<TEntity>(spec: UpdateSpec<TEntity>) -> BatchResult<UpdateReceipt>                   sync
  Delete<TEntity>(spec: DeleteSpec<TEntity>) -> BatchResult<DeleteReceipt>                   sync
  Assert(countSpec: QuerySpec, arguments: QueryArguments?, expected: int, errorNumber: int)  sync
  Sql(sql: FormattableString, options: SqlOptions?) -> BatchResult<RawResult>                sync
  ExecuteAsync() -> void

record BatchResult<T>
  Value: T                              // throws until executed
  IsCompleted: bool

contract IDataBatchContributor          // scoped DI; prologues ascending by Order, epilogues descending
  Order: int
  Contribute(batch: IDataBatch, stage: DataBatchStage)    sync
enum DataBatchStage = Prologue | Epilogue

record TableName(Schema: string, Name: string)
record SqlIdentifier(Name: string)      // bracket-quoted by the batch; never a value
data QueryArguments                     // map<string, object?> of declared-parameter values
record RowSet(Columns: list<QueryexColumn>, Rows: list<object?[]>, Count: int?, Ancestors: RowSet?)
data RowQueryOptions
  CountCap: int?                        // adds the capped count
  Ancestors: FilterTree?                // tree roots only: the access filter for ancestor rows
  CaptureKeys: bool = false
data EntityQuery<TEntity>
  Select: string                        required; bare paths only
  Filter: FilterTree?
  OrderBy: string?
  Skip: int?
  Take: int?
  Arguments: QueryArguments?
  Restrictions: list<KeySetRestriction> = []
  Children: list<string> = []           // [NotMapped] collection names, recursive via dotted paths
  Ancestors: FilterTree?
record EntityQueryResult<TEntity>(Entities: list<TEntity>, Related: RelatedEntities)
data RelatedEntities                    // map<string entityName, map<object id, object partialInstance>>
record SaveOptions(OverrideConcurrency: bool = false)
record SaveReceipt(Stamp: DateTime, StampedIds: set<object>, Inserted: int, Updated: int, Deleted: int)
data UpdateSpec<TEntity>
  Ids: list<object>                     required
  Filter: FilterTree?                   // row-level security
  Assignments: map<string, object?>     // property -> uniform value; server-owned or editable only
  Stamp: bool = true
record UpdateReceipt(UpdatedIds: set<object>)
data DeleteSpec<TEntity>                // one of three shapes
  ByIds(Ids: list<object>, Filter: FilterTree?)
  ByQuery(Filter: FilterTree, Arguments: QueryArguments?)
  WithDescendants(Ids: list<object>, Filter: FilterTree?)
record DeleteReceipt(DeletedIds: set<object>)
record SqlOptions(Writes: set<TableName> = [], Idempotent: bool = false, ResultSets: int = 0)
record RawResult(ResultSets: list<RowSet>)

service IIdAllocator                    // singleton
  Reserve(batch: IDataBatch, entityType: Type, rows: list<object>) -> IdReservation    sync
contract IdReservation                  // disposable hold
  Assign(rows: list<object>)            sync; consumes the hold; wires child parent keys
  Dispose()                             sync; returns unassigned ranges to the buffer

data EntityMetadata
  ClrType: Type
  Name: string
  Table: TableName
  TableTypePhysicalName: string
  Key: PropertyMetadata
  Properties: list<PropertyMetadata>
  Children: list<ChildCollectionMetadata>
  NaturalKey: PropertyMetadata?
  MultilingualGroups: list<MultilingualGroup>
  UniqueIndexes: list<UniqueIndexMetadata>
  IsTopLevel: bool
  IsTemporal: bool
  IsActivatable: bool
  IsLeasable: bool
  Tree: TreeMetadata?
  ResetServerOwned(entity: object)      sync
record PropertyMetadata(Name, Column, ClrType, Ownership: PropertyOwnership, IsNullable, IsUnique, MultilingualGroup: string?, Getter, Setter)
enum PropertyOwnership = Editable | WriteOnce | ServerOwned
record ChildCollectionMetadata(Property: string, Child: EntityMetadata, ParentKey: PropertyMetadata, Getter)
record MultilingualGroup(Name, Primary: PropertyMetadata, Secondary: PropertyMetadata, Ternary: PropertyMetadata)
record UniqueIndexMetadata(IndexName: string, Properties: list<PropertyMetadata>)
record TreeMetadata(ParentKey: PropertyMetadata, NodeColumn: string, SubtreeCount: PropertyMetadata, ActiveSubtreeCount: PropertyMetadata?, MaxDepth: int)
service IEntityMetadataProvider
  Get(entityType: Type) -> EntityMetadata     sync
  All: list<EntityMetadata>

data DataAccessScope                    // scoped; copied into background scopes
  Operation: string
  RoundTrips: int
  Retries: int

data DataMeter                          // constants: Name = "Tellma.Core.Data" and the instrument names of D23
data DataErrorNumbers                   // constants: ConcurrencyConflict = 50401, RowSecurity = 50403, TreeCycle = 50430; range 50400–50499 reserved

// Exceptions (derive from the pipeline theme's DataAccessException base):
// ConcurrencyConflictException(ConflictingIds, MissingIds, Truncated), UniqueConstraintViolationException(IndexName),
// ForeignKeyViolationException(ConstraintName), TreeCycleException(EntityType), TreeDepthExceededException(EntityType),
// RowSecurityException, BatchAssertionFailedException(Number), DataAccessAmbiguousException(Inner)
```

`KeySetRestriction(Path: string, TableSource: string)` lives in `Tellma.Core.Queryex` (D20).

### 3.3 Shapes needed from other themes

- **Host theme:** a request context exposing tenant, user id, the user's calendar date and time zone, the tenant zone, and `SchemaProfile`; a scoped-holder copy for background scopes; the feature composition hook where a feature registers entity leaf types (`entities.Add<Center>()`) so the metadata provider and the runtime model see one list; `UPDATE` on every `sq_<Table>` for the web identity.
- **Settings theme:** `SchemaProfile` on the request context; an epilogue `IDataBatchContributor` mapping `WrittenTables` to tag bumps; the tenant zone name.
- **Users/permissions theme:** a prologue `IDataBatchContributor` for connect and tag reads that consults `Purpose`; the row-level-security `FilterTree` applied by the pipeline to every `Query`/`Rows`/`Count`/`Update`/`Delete`.
- **Pipeline theme:** the `DataAccessException` base; the operation name it sets on `DataAccessScope`; the validator API that receives the validation `IDataBatch`; the moment it calls `IdReservation.Assign`.
- **Background theme:** the `[LeaseState]`/`[LeaseDue]` annotations and the state values the lease predicate reads.

---

## 4. Schema

This theme owns no production table; it owns the column vocabulary every capability projects onto a table, and its own fixture tables.

### 4.1 Capability column sets

**Keyed (`Entity<TKey>`)**

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` (or `bigint`) | no | PK `PK_<Table>` clustered; sequence `sq_<Table>` | app-assigned; no IDENTITY |

**Top-level audit (`TopLevelEntity<TKey>`)**

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `CreatedAt` | `datetime2(7)` | no | | server-stamped on insert |
| `CreatedById` | `int` | no | FK `FK_<Table>_CreatedById` → `core.Users(Id)`, NO ACTION | |
| `ModifiedAt` | `datetime2(7)` | no | | concurrency token; moves on user-visible mutations only |
| `ModifiedById` | `int` | no | FK `FK_<Table>_ModifiedById` → `core.Users(Id)`, NO ACTION | |

**Temporal (`[Temporal]`)**: `ValidFrom`/`ValidTo datetime2(7) NOT NULL GENERATED ALWAYS AS ROW START/END` (shadow), `PERIOD FOR SYSTEM_TIME`, `SYSTEM_VERSIONING = ON (HISTORY_TABLE = <schema>.<Table>History)`; history table keeps the default clustered `(ValidTo, ValidFrom)` index and no nonclustered indexes; not on TPT tables.

**Child (`ChildEntity<TKey>` with `[ParentKey]`)**

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `<Parent>Id` | `int` | no | FK `FK_<Table>_<Parent>Id` → `<ParentTable>(Id)`, NO ACTION; index `IX_<Table>_<Parent>Id` | children deleted explicitly |

**Tree (`TreeEntity<TSelf>`)**

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `ParentId` | `int` | yes | FK `FK_<Table>_ParentId` → `<Table>(Id)`, NO ACTION; index `IX_<Table>_ParentId` | |
| `Node` | `hierarchyid` | no | unique `IX_<Table>_Node` | shadow; excluded from the UDTT; value = id path |
| `SubtreeCount` | `int` | no | default 0 | recomputed set-based |
| `ActiveSubtreeCount` | `int` | no | default 0 | `ActivatableTreeEntity` only |

**Activatable (`IActivatable`)**: `IsActive bit NOT NULL DEFAULT 1`.

**Leasable (`ILeasable`)**: `LeaseOwner uniqueidentifier NULL`, `LeaseExpiresAt datetime2(3) NULL`, `LeaseToken bigint NOT NULL DEFAULT 0`, `Attempts int NOT NULL DEFAULT 0`; filtered index `IX_<Table>_Lease (DueAt, Id) INCLUDE (LeaseExpiresAt) WHERE State = 'Queued'` (shape owned by the background theme).

**Enum-valued property**: `varchar(n)` per nullability, `n = max(8, longest member name)`, no CHECK. **Multilingual group `P`**: `P nvarchar(k)`, `P2`/`P3 nvarchar(k) NULL` with the same `k` and unicode. **JSON column**: `nvarchar(max)`. **Sequence**: `sq_<Table> AS int START WITH 1000 INCREMENT BY 1 NO CYCLE` (reserved seed band `[1, 999]`).

### 4.2 Fixture tables (`fixture` schema, test-only)

| Table | Shape | Columns beyond the capability sets |
|---|---|---|
| `fixture.Widgets` | `TopLevelEntity`, `[Temporal]`, `IActivatable`, natural key `Code` | `Code varchar(50) NULL` unique filtered `IX_Widgets_Code`; `Kind varchar(8) NOT NULL` (enum `WidgetKind { Plain, Fancy }`); `Name/Name2/Name3 nvarchar(255)`; `Subject nvarchar(64) NOT NULL [WriteOnce]` unique `IX_Widgets_Subject`; `Price decimal(19,4) NULL`; `Settings nvarchar(max) NULL [JsonColumn]`; history `fixture.WidgetsHistory` |
| `fixture.WidgetParts` | `ChildEntity` of Widgets, temporal with its parent | `WidgetId [ParentKey]`; `Ordinal int NOT NULL`; `Notes nvarchar(1024) NULL`; unique `IX_WidgetParts_WidgetId_Ordinal` |
| `fixture.WidgetPartNotes` | grandchild | `WidgetPartId [ParentKey]`; `Text nvarchar(max) NOT NULL` |
| `fixture.Nodes` | `ActivatableTreeEntity<Node>` | `Code varchar(50) NOT NULL` unique; `Name nvarchar(255) NOT NULL` |
| `fixture.Jobs` | `TopLevelEntity`, `ILeasable` | `State varchar(16) NOT NULL [LeaseState]`; `DueAt datetime2(3) NOT NULL [LeaseDue]`; `Payload nvarchar(max) NULL` |

### 4.3 Example projection: `gl.Centers` (owned by the reference-stack theme)

| Column | Type | Null | Constraints |
|---|---|---|---|
| `Id` | `int` | no | PK `PK_Centers`; sequence `gl.sq_Centers` |
| `ParentId` | `int` | yes | FK `FK_Centers_ParentId` → `gl.Centers(Id)`; index `IX_Centers_ParentId` |
| `Type` | `varchar(9)` | no | `Service | Operation | Sale` |
| `Name` / `Name2` / `Name3` | `nvarchar(255)` | no / yes / yes | |
| `Code` | `varchar(50)` | yes | unique filtered `IX_Centers_Code` |
| `IsActive` | `bit` | no | default 1 |
| `Node` | `hierarchyid` | no | unique `IX_Centers_Node`; shadow |
| `SubtreeCount` / `ActiveSubtreeCount` | `int` | no | default 0 |
| `CreatedAt`, `CreatedById`, `ModifiedAt`, `ModifiedById` | audit set | no | FKs → `core.Users` |

UDTT `gl.CentersList_<hash8>`: every column above except `Node`, in table order. No `IsLeaf`, no `Level`.

---

## 5. Answers

| Brain-dump question (abridged) | Answer |
|---|---|
| Write-once columns: two UDTTs for create and update, or enforce at the service layer? | Neither: one UDTT; `[WriteOnce]` columns are excluded from every `UPDATE` by the emitter (D4). |
| Do we need `SavedById` on the weak entities? | No. Children carry no audit columns; a child change stamps the root (D2, D13). |
| Keep `hierarchyid` in step with `ParentId` under bulk save — in memory or SQL appended in the transaction? | SQL in the persist transaction: affected set, id-path re-path, cycle fence, set-based recount (D16). |
| How to model `CenterType`, stored as a string for Queryex? | An enum; the platform convention stores every enum as `varchar(n)` (D6). Named `Type`. |
| Better names for the save emitter and the multi-statement builder/executor? | `SaveEmitter`; `DataBatch` behind `IDataBatch` from `ITenantDatabase.CreateBatch()` (D11). |
| Does the emitter need to know upsert versus synchronise? | No: roots are upserted, supplied collections synchronised, unsupplied left alone; `null` versus list carries the mode (D3, D13). |
| What should the executor API look like? | D12 and §3.2. |
| Minimise id gaps without a dedicated round trip? | Exact-deficit reservation rides the validation round trip; holds return to the buffer; no prefetch (D10). |
| Who maintains reserved ids — a thread-safe singleton? | `IIdAllocator`, buffers per (tenant database, sequence) (D10). |
| Un-consume ids when a save fails? | Ids are assigned only after validation; a failed validation returns the hold; a failed persist returns the range (D10). |
| Who assigns ids, service or data layer? | The data layer (`IdReservation.Assign`); the pipeline picks the moment (D10). |
| Queryex: restrict where a column is IN a TVP list | `QuerySpec.Restrictions` with `KeySetRestriction(Path, TableSource)` (D20). |
| Add `level` to Queryex | `level(node)` → `GetLevel()` (D20). |
| A good alternative to `RowVersion`, implementable in C#? | `ModifiedAt` as the token, server-stamped once per batch, guarded under U locks, override honoured (D13, D15). |
| Distros extend or replace entities while reusing service logic; validate the interface matches the DB | Generic services over leaf types with capability constraints; `EntityMetadata` validates at startup (D9). |
| Custom validators loading context in the batch call; deduplicating identical or overlapping queries | Validators receive the validation `IDataBatch`; structural dedup; overlapping selects not merged yet (D12). |
| Where does the transaction boundary begin and end? | Inside the persist round trip's text only (D11). |
| Collapse the connect call and the permission check optimistically? | The connect prologue rides any `Read`/`Validate` batch, never `Persist` (D12; §6 seam 16). |
| Count DB calls per entity per distro | `DataAccessScope` + `tellma.data.roundtrips`; statistics oracle in tests (D23). |
| Remove `Name2`/`Name3` from the schema when unconfigured? | Schema per `SchemaProfile(LanguageCount)` (D18). |
| Natural key: a Core attribute? Mandate one? | `[NaturalKey]`; inference over unique indexes; no mandate (D5). |
| Ancestors tagging along with a tree page? | Same round trip through key capture; reported as a separate row set (D19). |

---

## 6. Seams

1. **Batch abstraction (owned).** `IDataBatch` as in §3.2. Contributors are the single hook for connect prologue, tag reads, and tag bumps; id reservation adds statements through `IIdAllocator.Reserve`; notification inserts are an ordinary `Save` of the notification entity in the same batch; lease statements come from `LeaseStatements<T>`. `Idempotent` per statement, `WrittenTables` per statement, `TransactionMode` and `Purpose` per batch.
2. **Entity class versus wire shape (owned).** One class; `[NotMapped]` child collections; `PropertyOwnership` per property from `EntityMetadata`; JSON derives from the same class; `ModifiedAt` round-trips at seven fractional digits; `null` and `[]` on child collections differ; partial related instances are read-only projections that save must reject.
3. **One capability, declared once (consumed).** The data-layer half of a capability is a base class, a one-property interface, or an annotation; the pipeline reads `EntityMetadata.IsActivatable`/`Tree`/`IsLeasable` to project actions, permissions and routes; `IDataBatch.Update` is the statement every state action uses.
4. **Queryex schema per tenant configuration (owned).** Key = `SchemaProfile(LanguageCount)`; lifetime = process; row-level security and child-path rewriting are `FilterTree` transformations, never schema variants.
5. **Version tags (consumed).** A prologue contributor reads tags; an epilogue contributor bumps them from `WrittenTables`. Every write passes through a batch that exposes `WrittenTables`; the table-to-tag policy is the settings theme's.
6. **Feature composition (consumed).** A feature contributes leaf types; metadata and the runtime model are built from that one list.
7. **Natural keys (owned).** `[NaturalKey]` plus inference; `EntityMetadata.NaturalKey` is the Excel codec's input; natural-to-surrogate translation is a `Rows` query with a `StringList` restriction on the natural-key path.
8. **Background-task columns and lease statements (emitted here).** `ILeasable`, `LeaseStatements<T>`; the background theme owns state, due, renewal cadence and poison handling.
9. **Request context (consumed).** `ITenantDatabase` is built from it; background scopes use `ITenantDatabaseFactory.Open`; no `AsyncLocal`.
10. **Platform exceptions (consumed).** The executor's exceptions derive from the pipeline's `DataAccessException`; HTTP mapping is the web theme's.
11. **Permission evaluation (consumed).** The pipeline hands the resulting `FilterTree` to every read, update and delete; the post-save check is `Assert(count, n, 50403)` inside the persist transaction, over the saved ids and the fresh filter.
14. **Telemetry (owned).** `DataMeter` names; no per-tenant tags.
15. **Notification enqueue (consumed).** A second `Save` in the same batch.
16. **Connect-call collapse (position).** The batch supports it: the prologue rides any `Read` or `Validate` batch. Failure modes: a *deactivated user* — the prologue's result is examined before any write round trip and a read's results are discarded; *stale permissions* — the prologue returns the permissions tag; on mismatch the pipeline recomputes and re-runs the round trip once; *RLS pre-check ordering* — the pre-check is a `Count` in the validation round trip under cached filters and re-runs under fresh ones on mismatch. `Persist` batches never carry the prologue.
17. **Vocabulary.** `CreatedAt/CreatedById/ModifiedAt/ModifiedById`; plural tables; schemas `core`, `gl`, `fixture`; `int` ids by default; "child entity"; "top-level entity"; `Type` not `CenterType`; `Node`; `sq_<Table>`.

---

## 7. Departures

1. **Child collections exist on the entity class as `[NotMapped]` lists.** The document says `Invoice.Lines` does not exist; it exists as a non-EF property so one class is both storage and wire shape; the ban's reasons hold because EF never sees it.
2. **No background prefetch in the ID allocator**; exact-deficit reservation on an existing round trip; the multi-sequence single-round-trip shape is kept for the statements. **Healing consumes the gap** instead of jumping the sequence, so the web identity needs no `ALTER`.
3. **No `rowversion`; `ModifiedAt` is the concurrency token.** Spec 0001's rowversion inclusion stays available and unused.
4. **Two new packages, `Tellma.Core.Data.Abstractions` and `Tellma.Core.Data`**, because the consumer-facing batch API mentions Queryex types and `Tellma.Core.Abstractions` stays BCL-only.
5. **Shadow properties exist on mapped entities**: temporal period columns (forced by EF 10) and the tree `Node` (so `hierarchyid` stays out of Abstractions and out of TVPs). The "no shadow properties" analyzer exempts both platform conventions.
6. **Package pins:** `Microsoft.EntityFrameworkCore.SqlServer.HierarchyId` 10.0.11, EF Core 10.0.11, `Microsoft.Data.SqlClient` 6.1.6.
7. **ID allocation and the EF-to-Queryex adapter land in spec 0011**, not the separate specs the document promised.
8. **Tables are written only through `IDataBatch`; EF `SaveChanges` is banned at runtime** by analyzer.
9. **Every tenant database has `READ_COMMITTED_SNAPSHOT ON`**, set by the migrator at provisioning.
10. **The DbContext options are applied by one platform helper** (`UseTellmaSqlServer`) that pins the compatibility level, rather than by each distribution calling `UseSqlServer` directly.

---

## 8. Verification

**Relied on from the research file (verified 2026-09-01):** the two surviving `MERGE` defects and the separate-statement recommendation; SqlClient retry never applies inside a transaction, `SqlBatch` has no retry provider, sends a TVP per command and shows only the last statement to telemetry; the driver's baseline transient list includes 1205 and 1222; `IEnumerable<SqlDataRecord>` streams; temporal `UPDATE` writes a history row even when nothing changed; `OUTPUT` has no temporal-specific restriction; period columns are shadow properties in EF 10 and only leaf-mapped roots can be temporal; `hierarchyid` cannot ride a TVP without `SqlHierarchyId`; the EF hierarchyid package is 10.0.11 and needs SqlClient ≥ 6.1.6; `sp_sequence_get_range` needs `UPDATE` on the sequence, deducts nothing on 11732, and values are generated outside the transaction; `NEXT VALUE FOR` restrictions; sequence cache loss on abnormal shutdown; EF cannot emit `CACHE`; RCSI on by default only on Azure SQL; `READPAST` a no-op under RCSI without `UPDLOCK`; `UPDLOCK` reads take update locks on the latest committed row; `Properties<Enum>().HaveConversion<string>()` and finalizing-convention sizing; JSON columns stay `nvarchar(max)` without `UseAzureSql()`/compat 170 and EF injects `Application Name`; OpenTelemetry SqlClient 1.16 attributes and the `RetrieveStatistics` round-trip counter.

**Verified against the repository:** `Tellma.Core.Queryex` and `Tellma.Core.Abstractions` have no package or project references; the table-types fluent API has `ExcludeFromTableType()` on a property builder, so a shadow column can be excluded without an attribute; `QueryexSchemaBuilder` declares properties by (name, type, column, isNotNull, isUnique, storeType) and marks `TreeNode` by name; `IdList`/`BigIdList`/`GuidList`/`StringList` are plain `[Key]` classes.

**Own reasoning, not verified by a source:** `SqlConnection.BeginTransaction` and `Commit` each cost a round trip; the `THROW` message cap of 2 048 characters; `hierarchyid::Parse` accepting negative labels for the provisional node and the encoded size of large id labels staying small for master-data trees (measure before relying on id paths for `bigint` keys); `SYSUTCDATETIME()` monotonicity across clock steps; `OUTPUT inserted.[Id]` from an `UPDATE` on a system-versioned table (fixture test); `IsDescendantOf` against a unique `Node` index seeking in the `CROSS APPLY` recount (measure); `INSERT INTO @t … SELECT … ORDER BY … OFFSET … FETCH` and a derived table with `OFFSET`/`FETCH` being legal T-SQL (fixture test); reading a `hierarchyid` result column through the EF `HierarchyId` type's binary reader (fixture test; fallback is `Node.ToString()` emitted by the adapter as a view column).

**Still unverified and deliberately deferred:** `Properties<Enum>()` matching nullable enums; the Managed Instance RCSI default (the migrator sets it regardless); the crossover at which `SqlBulkCopy` beats a TVP for import (benchmark before adding the path).

---

## 9. Review flags

1. **Audit FKs to `core.Users` on every table** (D2) — versus no FK on audit columns (fewer dependencies, dangling creators possible).
2. **`IsActive` server-owned** (D4) — versus editable, letting import sheets deactivate rows.
3. **Natural-key inference order `Code` before `Name`** (D5) — versus `Name` first.
4. **`ActivatableTreeEntity<TSelf>` as a second base** (D16) — versus `TreeEntity` always carrying `ActiveSubtreeCount` (one base, one redundant column on non-activatable trees).
5. **Whole-table recount** (D16) — versus an affected-ancestors recount that matters only above ~100 000 rows.
6. **Conflict ids in a capped `THROW` JSON message** (D13) — versus a conflicts result set followed by `ROLLBACK; RETURN` (every id, stateful walk).
7. **U locks in the concurrency guard** (D13) — versus an unlocked guard plus a post-update count against a precomputed expected set (no U locks held, one more statement, deadlock-free but weaker).
8. **Healing by consuming the gap** (D10) — versus `ALTER SEQUENCE … RESTART` with `ALTER` granted to the web identity.
9. **`Tellma.Core.Data.Abstractions` as a separate package** — versus folding it into `Tellma.Core.Abstractions` by letting that package reference `Tellma.Core.Queryex` (which has no dependencies).
10. **Three-attempt retry of a non-idempotent persist on reported failures** (D11) — relies on `XACT_ABORT ON` having rolled back; the executor asserts `COMMIT` is the last statement.
11. **Key capture through a keys-only twin compile** (D12) — versus a second round trip for children of filtered pages and for tree ancestors (simpler, one more round trip on tree grids).
12. **`ModifiedAt` as the token** (D15) — versus `rowversion` with the same sibling-table discipline.
13. **`UseCompatibilityLevel(160)` pinned by the platform helper** (D8) — versus leaving the provider default (150) and revisiting when native `json` is wanted.

---

## 10. Conflicts

1. **`today()` binding (T6, T3).** Spec 0008 documents `today()` as the current date in the tenant's zone; the brain dump wants the user's date from a header. The data layer binds whatever `QueryexContextValues.Today` carries; the web and settings themes must agree who computes it.
2. **Connect prologue and batch purpose (T4, T5).** The prologue contributor must consult `BatchPurpose` and never ride `Persist`; the pipeline must create its batches with the right purpose and act on a deactivated-user result before any write.
3. **Tag bumps (T3).** The epilogue contributor maps `WrittenTables` to tags; task tables and sibling bookkeeping tables must be excluded from bumping by that policy, not by the data layer.
4. **Row-level-security filter on every read, update and delete (T4, T5).** The data layer applies whatever `FilterTree` it is handed; the pipeline must hand it to `Update` and `Delete` too, and must pass the access filter alone (not the user filter) as `Ancestors`.
5. **`SchemaProfile` before the first schema (T3, T1).** The tenant's language count must be readable before the first Queryex compilation on a cold instance.
6. **Sequence permission and RCSI (T1, T8).** The web identity needs `UPDATE` on every `sq_<Table>`; the migrator sets `READ_COMMITTED_SNAPSHOT ON` at provisioning and ships a raw `ALTER SEQUENCE … CACHE` where a distribution wants it.
7. **Lease state and due columns (T10).** `[LeaseState]`/`[LeaseDue]` and the `'Queued'` value are the background theme's; the acquire statement's predicate is templated from them.
8. **`IsActive` editability and the activate action (T5, T9).** If the pipeline or the Excel codec needs import to deactivate rows, D4's server-owned `IsActive` must change to editable.
9. **Users table and the system user (T4).** The first admin and any system user are inserted with `CreatedById`/`ModifiedById` pointing at themselves (legal within one statement); the tenant bootstrap must do so through `Save`.
10. **Package pins (all themes).** EF Core 10.0.11 and SqlClient 6.1.6 apply repo-wide through central package management; the identity server and email connectors build against them too.
