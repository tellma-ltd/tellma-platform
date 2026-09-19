# Seams — the full contracts of the CRUD stack (specs 0010–0020)

Written 2026-09-04 from `ledger.md` §3 and the ten theme decisions files (§3 Contracts, §4 Schema), read as corrected by the ledger's §2, §4 and §8. Contract blocks use the platform's contract notation (`notation.md`): names are normative; shape is described, not transcribed. SQL is the exact shape to emit. Every type referenced by any block below is defined in this file, in `Tellma.Core.Queryex` (spec 0008, listed in §0.2), or in the BCL. Where the ledger left a name or a shape ambiguous, the choice made here is listed in §24 (Resolutions) so the ledger can be corrected.

## 0. Reading rules

### 0.1 Packages and namespaces

| Package | Namespaces used below | References |
|---|---|---|
| `Tellma.Core.Abstractions` | `.Entities`, `.Data`, `.Crud`, `.Validation`, `.Errors`, `.Api`, `.Access`, `.Settings`, `.Caching`, `.Localization`, `.Calendars`, `.Blobs`, `.Excel`, `.Jobs`, `.Notifications`, `.Realtime`, `.Tenancy`, `.Composition`, `.Identity`, `.TableTypes` | `Tellma.Core.Queryex` only (the one non-BCL edge; ledger §2.17) |
| `Tellma.Core` | `Tellma.Core.Composition`, `.Tenancy`, `.Data`, `.Crud`, `.Access`, `.Settings`, `.Caching`, `.Localization`, `.Users`, `.Identity`, `.Blobs`, `.Excel`, `.Jobs`, `.Notifications`, `.Provisioning` | Abstractions, `Tellma.Core.EntityFrameworkCore`, EF Core SqlServer + HierarchyId, SqlClient, `Microsoft.Extensions.*`, Cronos, OpenXml, MessageFormat — never `Tellma.Core.Imaging` or ImageSharp |
| `Tellma.Core.Imaging` | `Tellma.Core.Imaging` | Abstractions + `SixLabors.ImageSharp` only (licence-isolated; `src/core/Tellma.Core.Imaging/`); ships `ImageSharpImageProcessor : IImageProcessor` and `services.AddTellmaImageSharp()`; referenced by the reference distribution, swappable for a SkiaSharp package without touching `Tellma.Core` |
| `Tellma.Core.AspNetCore` | `Tellma.Core.AspNetCore` | `Tellma.Core` + ASP.NET Core + SignalR |
| `Tellma.Core.Mcp` | `Tellma.Core.Mcp` | `Tellma.Core.AspNetCore` + `ModelContextProtocol.AspNetCore` |
| `Tellma.Core.Migrator` | `Tellma.Core.Migrator` | `Tellma.Core` + EF design |
| `Tellma.Connector.AzureBlobs.Adapter` | `Tellma.Connector.AzureBlobs` | Abstractions + `Azure.Storage.Blobs` |
| `Tellma.Module.Gl.Abstractions` / `Tellma.Module.Gl` | `Tellma.Module.Gl` | Abstractions only / its own Abstractions |
| `Tellma.Core.Analyzers` | — | Roslyn (`TELLMA0001` hard-coded TVP ordinals, `TELLMA0002` `SaveChanges` banned, `TELLMA0003` raw SQL without declared writes / reserved prefixes) |

Every block states its namespace in a leading remark. A block whose namespace begins with `Tellma.Core.` (not `.Abstractions.`) is a runtime type; a distribution names it only through composition.

### 0.2 The Queryex edge and the framework boundary

`Tellma.Core.Abstractions` references `Tellma.Core.Queryex` and uses these of its public types in contracts: `QuerySpec`, `FilterTree`, `QueryexSchema`, `QueryexColumn`, `QueryexType`, `QueryexDiagnostic`, `QueryexParameterSlot`, `CompiledQuery`, `QueryCompilationOptions`, `QueryexEngine`, `EntityDescriptor`, and the amendment `KeySetRestriction` (seam 4). Nothing in `Tellma.Core.Abstractions` needs an EF Core, ASP.NET Core, or SqlClient type: the members that would (`SqlConnection`, `TellmaDbContext`, `RouteGroupBuilder`, `ITicketStore`, `TokenCredential`) are placed in `Tellma.Core`, `Tellma.Core.AspNetCore`, or the adapter, and the table below of each seam says so. Endpoint-metadata records (`SecurableEndpointMetadata`, `MemberEndpointMetadata`, `NoActivityStampMetadata`) are plain records in Abstractions; the framework attaches them.

Runtime types that contracts name but distributions never subclass except where stated: `TellmaDbContext` (`Tellma.Core.Data`; the platform-owned EF context, derivable only through `TellmaBuilder.UseDbContext<T>()`), `DataBatch` (the `IDataBatch` executor), `SaveEmitter`, `TableTypeBinder`, `TreeCycleValidator<T>`, `BlobReferenceValidator<T>`, `BlobReferenceEffect<T>`, `CoreFeature : ITellmaFeature` (`Tellma.Core.Composition`; `Name = "core"`; added unconditionally by `AddTellma`), `JobWorker`, `TellmaMigrator` (`Tellma.Core.Migrator`: `RunAsync(args, slug, compose) -> int`), `TellmaDesignTimeDbContextFactory<TContext>` (abstract `Compose(builder)`), `GlSampleCentersStep : ITenantProvisioningStep`.

### 0.3 Reserved SQL names

`@qx{b}_` (engine), `@tb{b}_` (per-statement platform names: `@tb{b}_t{i}` TVPs, `@tb{b}_p{i}` scalars, `@tb{b}_keys`, `@tb{b}_new`, `@tb{b}_saved`, `@tb{b}_touched`, `@tb{b}_cap{n}`, `@tb{b}_aff`, `@tb{b}_old`, `@tb{b}_paths`, `@tb{b}_anc`, `@tb{b}_claimed`, `@tb{b}_due`, `@tb{b}_fired`, `@tb{b}_jobIds`, `@tb{b}_blob_<Table>_<Column>`), `@tm_` (the fixed-text prologue, tag prelude, guard and bump, once per batch; `@tm_callerIds`, `@tm_userIds_<Column>`, `@tm_expectedTags`, `@tm_tagNames`). `{b}` is the statement's batch ordinal. Distribution raw SQL may use none of these; the analyzer `TELLMA0003` enforces it.

### 0.4 Conventions repeated from the ledger

Tables plural, classes and Queryex entity names singular; sequences `<schema>.sq_<Table> AS int START WITH 1000 INCREMENT BY 1 NO CYCLE` (band 1–999 reserved for `HasData`); constraint names `PK_<Table>`, `FK_<Table>_<Column>`, `UX_<Table>_<Cols>`, `IX_<Table>_<Cols>`, `CK_<Table>_<Name>`, `DF_<Table>_<Column>`; history tables `<Table>History`; enum columns `varchar(n)`, `n = max(8, longest member)`, no IN-list CHECK; audit timestamps `datetime2(7)`, every other timestamp `datetime2(3)` UTC; JSON columns `nvarchar(max)`; no IDENTITY, no `rowversion`, no triggers, no `ON DELETE CASCADE` except the user-sibling tables named below.

---

## 1. Seam 1 — The batch abstraction (owner 0011; consumers all)

One round trip = one `SqlCommand` of concatenated text walked with `NextResult()`; in-text transaction when any statement writes and `TransactionMode = Auto`; connections only through `ITenantConnectionProvider`; executor-owned retry (reported failures: the whole round trip, up to 3 attempts, jittered 50/200/800 ms; ambiguous failures: only when every statement is `Idempotent`, else the commit probe); contributors add the prologue (0013 connect at `Order` 100, 0012 tag read at `Order` 50) and the epilogue (0012 bumps); result sets preceding a `THROW` are read before the exception surfaces; every span carries `tellma.db.role = tenant | catalog`.

Retry classes, fixed: **reported transient** (the round trip is re-run after a rollback) = SQL errors 1205, 1222, 4060, 10928, 10929, 40197, 40501, 40613, 49918, 49919, 49920 and the platform's own `THROW 50503` (`TellmaSqlErrors.Transient`); **ambiguous** (re-run only when every statement is `Idempotent`, otherwise the commit probe) = 233, 64, -2 (timeout), 10053, 10054 and any failure after the command was sent with no server response; every other `THROW 50xxx` and every other error is final and never retried.

### 1.1 The database handle and the batch

```contract
// Tellma.Core.Abstractions.Data
service ITenantDatabase                       // scoped: one per request or job scope
  Schema: QueryexSchema                       // for the tenant's MultilingualShape
  Context: QueryexContextValues
  CreateBatch(purpose: BatchPurpose) -> IDataBatch   sync

service ITenantDatabaseFactory                // singleton; used by scope factories and the migrator
  Open(tenantId: int, context: QueryexContextValues, shape: MultilingualShape) -> ITenantDatabase

record QueryexContextValues(Today: DateOnly, Now: DateTimeOffset, UserId: int?, TimeZoneName: string)
    // Today and TimeZoneName are the tenant's (ledger §2.27); UserId binds me()

enum BatchPurpose = Read | Validate | Persist | Maintenance
enum TransactionMode = Auto | None | Explicit
enum ConcurrencyMode = Check | Override      // defined once, here; used by .Crud, .Api, .Excel

service IDataBatch
  Purpose: BatchPurpose
  TransactionMode: TransactionMode = Auto
  WrittenTables: set<TableName>               // union of every statement's declared writes
  Query<TEntity>(query: EntityQuery<TEntity>) -> BatchResult<EntityQueryResult<TEntity>>   sync
  Rows(spec: QuerySpec, arguments: QueryArguments?, options: RowQueryOptions?) -> BatchResult<QueryRowSet>   sync
  Count(spec: QuerySpec, arguments: QueryArguments?, cap: int = 10000) -> BatchResult<int>   sync
  Save<TEntity>(rows: list<TEntity>, concurrency: ConcurrencyMode = Check) -> BatchResult<SaveReceipt>   sync
  Update<TEntity>(spec: UpdateSpec<TEntity>) -> BatchResult<UpdateReceipt>   sync
  Delete<TEntity>(spec: DeleteSpec<TEntity>) -> BatchResult<DeleteReceipt>   sync
  Assert(countSpec: QuerySpec, arguments: QueryArguments?, expected: int, errorNumber: int, code: string)   sync
  Sql(sql: FormattableString, options: SqlOptions?) -> BatchResult<RawResult>   sync
  Tvp<TRow>(rows: list<TRow>) -> SqlIdentifier   sync
  DeclareIdTable() -> SqlIdentifier   sync
  DependsOn(dependencies: list<VersionTagDependency>)   sync
  FromCache<TEntity>() -> BatchResult<CachedEntitySet<TEntity>>   sync
  BumpVersionTag(name: string)   sync          // an explicit tenant-level bump joined to the epilogue's set
  OnCommitted(callback: (BatchOutcome) -> void)   sync
  ExecuteAsync() -> BatchOutcome

record BatchResult<T>                         Value: T   IsCompleted: bool      // Value throws until executed
record BatchOutcome(VersionTags: VersionTagSnapshot, RoundTrips: int)

contract IDataBatchContributor               // scoped DI; prologues ascending by Order, epilogues descending
  Order: int
  Contribute(batch: IDataBatch, stage: DataBatchStage)   sync
enum DataBatchStage = Prologue | Epilogue

record SqlOptions(Writes: set<TableName> = [], Idempotent: bool = false, ResultSets: int = 0, UserIds: SqlIdentifier? = null)
  ForCaller(writes: set<TableName>) -> SqlOptions   // static, sync: UserIds = the batch-local one-row table @tm_callerIds holding @tm_UserId
record TableName(Schema: string, Name: string)
record SqlIdentifier(Name: string)            // bracket-quoted by the batch; never a value
data QueryArguments                           // map<string, object?> of declared-parameter values
data RowQueryOptions
  CountCap: int?                              // adds the capped grand-total count
  Ancestors: FilterTree?                      // tree roots only: the access filter alone, for the ancestor rows
  CaptureKeys: bool = false                   // also writes the page's ids into a @tb{b}_keys table
data EntityQuery<TEntity>
  Select: string                              required; bare paths only
  Filter: FilterTree?
  OrderBy: string?
  Skip: int?
  Take: int?
  Arguments: QueryArguments?
  Restrictions: list<KeySetRestriction> = []
  Children: list<string> = []                 // [NotMapped] collection names; dotted for grandchildren
  Ancestors: FilterTree?
record EntityQueryResult<TEntity>(Entities: list<TEntity>, Related: RelatedEntities)
record SaveReceipt(Stamp: datetime2(7), StampedIds: set<object>, Inserted: int, Updated: int, Deleted: int, ChildDeltas: map<string, IdDelta>)
record IdDelta(Inserted: list<object>, Deleted: list<object>)   // per child collection: parent ids of synchronised rows
data UpdateSpec<TEntity>
  Ids: list<object>                           required
  Filter: FilterTree?                         // the caller's access filter
  Assignments: map<string, object?>           // property -> uniform value; editable or server-owned only
  Stamp: bool = true                          // false only for platform bookkeeping on non-entity rows
record UpdateReceipt(UpdatedIds: set<object>)
data DeleteSpec<TEntity>                      // exactly one shape
  ByIds(Ids: list<object>, Filter: FilterTree?, ExpectedStamps: list<datetime2(7)?>? = null)
  ByQuery(Filter: FilterTree, Arguments: QueryArguments?, Cap: int)
  WithDescendants(Ids: list<object>, Filter: FilterTree?)
record DeleteReceipt(DeletedIds: set<object>)
record RawResult(ResultSets: list<QueryRowSet>)
```

| Member | Meaning |
|---|---|
| `Query` | Compiles the entity query for `TEntity` (every mapped leaf is a root, seam 4), appends it with its child and related queries, and materialises entities plus a `RelatedEntities` dictionary after execution. |
| `Rows` | Appends one compiled `QuerySpec`; `Skip`/`Take` are parameter slots; returns a columnar `QueryRowSet`. |
| `Count` | `COUNT(*)` over `TOP (@cap)`; the result is `cap` when capped. |
| `Save` | Appends the emitter's statements for the root type and its supplied children (§1.3); `Override` disables the stamp comparison, never the existence check. |
| `Update` | One `UPDATE` under `Filter` over `Ids`; stamps `ModifiedAt`/`ModifiedById` when `Stamp`; the receipt lists the ids actually updated; tree tables append the recount. |
| `Delete` | §1.5; children first, explicit statements, never cascades. |
| `Assert` | Appends `IF (<count>) <> @expected THROW @errorNumber, N'<code>', 1;`. |
| `Sql` | Raw text; the analyzer requires `Writes` on any statement containing DML and refuses `EXEC`/`sp_executesql` in distribution text except the allow-listed `sp_sequence_get_range` and `sp_getapplock`; result sets counted by `ResultSets`. |
| `SqlOptions.UserIds` | Names a batch-local table or `IdList` TVP holding the user ids a raw statement affects; required (startup gate and `TELLMA0003`) when `Writes` touches a table that carries a user-level tag rule (seam 5), because the epilogue cannot derive affected users from raw text. `SqlOptions.ForCaller(writes)` is the self-service sugar. |
| `Tvp` | Binds a standalone or table-derived type through `TableTypeBinder` (metadata-driven ordinals) and returns its parameter name. |
| `DeclareIdTable` | Declares `@tb{b}_ids TABLE ([Id] int PRIMARY KEY)` for later statements to fill and read. |
| `DependsOn` | Declares cached inputs; the executor applies `VersionTagMismatchPolicy` after a read batch and emits the 50412 guard inside a persist transaction (seam 5). |
| `FromCache` | Returns the cached list when its tag matches the snapshot; otherwise appends the load statement after the prologue. On a `Validate` or `Persist` batch the dependency is declared with `Rerun`, never `Refresh`, so a validator never accepts a row from a list that changed under it. |
| `BumpVersionTag` | Adds a tenant-level tag name to the epilogue's bump set for this batch only; the way a service bumps a tag that no written table declares (`SettingsService.Save` bumps `permissions` when a language column changed). |
| `OnCommitted` | Runs after the round trip's commit, outside the transaction; failures are logged, never thrown. |
| `ExecuteAsync` | Runs contributors, assembles the text, executes with retry, reads every result set, replaces the tag snapshot, fires `OnCommitted`. |

### 1.2 Id allocation, scope, meters, error numbers

```contract
// Tellma.Core.Abstractions.Data
service IIdAllocator                          // singleton
  Reserve(batch: IDataBatch, entityType: Type, rows: list<object>) -> IdReservation   sync
  TakeAsync(entityType: Type, count: int) -> list<object>
contract IdReservation
  IsImmediate: bool                           // true when the buffer covered the deficit; Assign may run before ExecuteAsync
  Assign(rows: list<object>)   sync           // assigns Id to every new row, rewrites temporary negative ids in self-typed FKs and child parent keys
  Dispose()   sync                            // returns unassigned ranges to the buffer; a persist attempt never returns ids

data DataAccessScope                          // scoped; copied into job scopes
  Operation: string
  RoundTrips: int
  Retries: int

data DataTelemetryNames                       // constants; MeterName = "Tellma.Core"
  RoundTrips = "tellma.data.roundtrips"; RoundTripDuration = "tellma.data.roundtrip.duration"; Retries = "tellma.data.retries"
  IdRefills = "tellma.data.ids.refills"; IdHealed = "tellma.data.ids.healed"; TreeRecomputes = "tellma.data.tree.recomputes"; TreeRepairs = "tellma.data.tree.repairs"
  RowsSaved = "tellma.data.rows.saved"; ConcurrencyConflicts = "tellma.data.concurrency.conflicts"; CommitProbes = "tellma.data.commit.probes"
  PurposeTag = "purpose"; OutcomeTag = "outcome"; RoleTag = "tellma.db.role"

data TellmaSqlErrors                          // constants; THROW numbers, severity 16, state 1; the message is a stable code
  // platform band 50400–50599:
  CallerInvalid = 50401; RowSecurity = 50403; NotFound = 50404; Concurrency = 50409; StaleVersionTag = 50412
  LimitExceeded = 50413; Invariant = 50422; CountMismatch = 50428; Transient = 50503
  // distribution and pack band 50600–50699:
  DistroMin = 50600; DistroMax = 50699
```

THROW bands: the platform owns `50400–50599` (the numbers above); distributions and packs raise their own guards in `50600–50699`, which the executor maps to `BatchAssertionFailedException` → `ValidationException` with the message as the validation code. No number outside these two bands may be thrown from platform or distribution SQL; the analyzer `TELLMA0003` and the fixture tier enforce it.

`Reserve` computes the exact deficit (new rows plus new child rows per table), takes what the buffer holds, and appends one `EXEC sys.sp_sequence_get_range @sequence_name = N'<schema>.sq_<Table>', @range_size = @tb{b}_p0, @range_first_value = @tb{b}_first OUTPUT;` per short sequence; every batch the executor runs may carry a refill to the low-water mark (64 per sequence). Healing: a 2627 on insert whose violated constraint is the table's `PK_<Table>` consumes the gap by reserving `max(Id) − current + buffer` and retrying once (`tellma.data.ids.healed`); a 2601/2627 on any other index is never a heal and maps to the property by index name (§1.7). `TakeAsync` serves callers that hold no batch (rare, metered); system-written rows inserted by fixed-text statements (`core.Jobs`, `core.Notifications`) take their ids inside the statement from `sp_sequence_get_range` and never touch the buffer (seams 8 and 15).

Internal data-layer exceptions (in `Tellma.Core.Data`, never mapped by the web layer): `UniqueConstraintViolationException(IndexName)`, `ForeignKeyViolationException(ConstraintName)`, `TreeCycleException(EntityType)`, `TreeDepthExceededException(EntityType)`, `RowSecurityException`, `ConcurrencyConflictException(ConflictingIds, MissingIds, Truncated)`, `BatchAssertionFailedException(Number, Code)`, `DataAccessAmbiguousException(Inner)`. The pipeline translates them to seam 10's closed set.

### 1.3 `SaveEmitter` — the persist statements

Per table in topological order (roots first, then each supplied child collection, recursively) the emitter binds two TVPs of the table's own UDTT — `@tb{b}_t{i}` new rows (ids assigned), `@tb{b}_t{i+1}` existing rows — plus, per child collection, an `IdList` TVP of the parents whose collection was supplied (`null` collection = untouched; `[]` = delete all; list = synchronise). Scalars: `@tb{b}_p0` override flag (`bit`), `@tb{b}_p1` current user id. Three batch-local id sets per root table, each with a fixed meaning: `@tb{b}_new` (every inserted root id), `@tb{b}_saved` (every root id in the payload, new and existing — the set the row-level post-check runs over), `@tb{b}_touched` (parents of synchronised children). Exact text for a root with one child collection (`core.Users` with `core.RoleMemberships`), ordinal 3:

```sql
DECLARE @tb3_now datetime2(7) = SYSUTCDATETIME();
DECLARE @tb3_new TABLE ([Id] int NOT NULL PRIMARY KEY);
DECLARE @tb3_saved TABLE ([Id] int NOT NULL PRIMARY KEY);
DECLARE @tb3_touched TABLE ([Id] int NOT NULL);
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
    DECLARE @tb3_msg nvarchar(2048) = N'{"count":' + CAST((SELECT COUNT(*) FROM @tb3_conflicts) AS nvarchar(10))
        + N',"conflicts":[' + ISNULL((SELECT STRING_AGG(CAST([Id] AS nvarchar(20)), ',') FROM (SELECT TOP (100) [Id] FROM @tb3_conflicts WHERE [Reason] = 'C' ORDER BY [Id]) AS c), N'')
        + N'],"missing":['   + ISNULL((SELECT STRING_AGG(CAST([Id] AS nvarchar(20)), ',') FROM (SELECT TOP (100) [Id] FROM @tb3_conflicts WHERE [Reason] = 'M' ORDER BY [Id]) AS m), N'')
        + N']}';
    THROW 50409, @tb3_msg, 1;
END;

-- 2. Insert new roots; audit columns stamped here; other server-owned columns come from the reset in-memory row; write-once columns (IsActive, default 1) come from the payload.
INSERT INTO [core].[Users] ([Id], [Subject], [Email], [Name], [Name2], [Name3], [IsActive],
    [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
OUTPUT inserted.[Id] INTO @tb3_new ([Id])
SELECT s.[Id], s.[Subject], s.[Email], s.[Name], s.[Name2], s.[Name3], s.[IsActive],
    @tb3_now, @tb3_p1, @tb3_now, @tb3_p1
FROM @tb3_t0 AS s;

-- 3. Synchronise each supplied child collection (parents @tb3_t2 : IdList; new children @tb3_t3; existing @tb3_t4).
DELETE c OUTPUT deleted.[UserId] INTO @tb3_touched ([Id])
FROM [core].[RoleMemberships] AS c
WHERE c.[UserId] IN (SELECT [Id] FROM @tb3_t2)
  AND NOT EXISTS (SELECT 1 FROM @tb3_t3 AS s WHERE s.[Id] = c.[Id])
  AND NOT EXISTS (SELECT 1 FROM @tb3_t4 AS s WHERE s.[Id] = c.[Id]);
UPDATE c SET c.[RoleId] = s.[RoleId], c.[Notes] = s.[Notes]
OUTPUT inserted.[UserId] INTO @tb3_touched ([Id])
FROM [core].[RoleMemberships] AS c JOIN @tb3_t4 AS s ON s.[Id] = c.[Id]
WHERE c.[UserId] IN (SELECT [Id] FROM @tb3_t2)
  AND EXISTS (SELECT s.[RoleId], s.[Notes] EXCEPT SELECT c.[RoleId], c.[Notes]);
INSERT INTO [core].[RoleMemberships] ([Id], [UserId], [RoleId], [Notes])
OUTPUT inserted.[UserId] INTO @tb3_touched ([Id])
SELECT s.[Id], s.[UserId], s.[RoleId], s.[Notes] FROM @tb3_t3 AS s;

-- 4. Update existing roots last: editable columns only; changed or touched rows only; stamp.
UPDATE t SET t.[Name] = s.[Name], t.[Name2] = s.[Name2], t.[Name3] = s.[Name3],
    t.[ModifiedAt] = @tb3_now, t.[ModifiedById] = @tb3_p1
OUTPUT inserted.[Id]                                                          -- result set: stamped ids
FROM [core].[Users] AS t JOIN @tb3_t1 AS s ON s.[Id] = t.[Id]
WHERE EXISTS (SELECT s.[Name], s.[Name2], s.[Name3] EXCEPT SELECT t.[Name], t.[Name2], t.[Name3])
   OR t.[Id] IN (SELECT [Id] FROM @tb3_touched);

SELECT @tb3_now AS [Stamp];                                                    -- result set: the batch stamp
SELECT DISTINCT [Id] FROM @tb3_touched;                                        -- result set: ChildDeltas parents
```

Rules: `[ServerOwned]` and `[WriteOnce]` columns never appear in a `SET` list; the row image carries the client's `ModifiedAt` as the expected stamp; a child row whose parent id is not in the parents TVP is not touched (the pipeline reports `Entity.NotFound`); unchanged rows are skipped (`EXCEPT` treats `NULL = NULL`), so temporal tables get no no-op history row; a touched or changed root is stamped; children carry no audit columns; a `[Temporal]` child's unchanged rows are likewise skipped; a `50409` whose conflicts are all `'M'` surfaces as `NotFoundException` (404) — a row that never existed or was deleted under the caller is not a concurrency conflict — and as `ConcurrencyException` (409, carrying `IsMissing` per id) when any `'C'` is present; above `LargeBatchThreshold` (1,000 TVP rows in any TVP of the batch) every DML statement gets `OPTION (RECOMPILE)`; tree tables append §1.4 after step 4; the emitter caches generated text per (entity metadata, statement kind); import chunks at 10,000 root rows per round-trip pair.

Capture rule (SQL Server allows one `OUTPUT … INTO` and one plain `OUTPUT` per statement): every DML statement feeds at most one batch-local table directly. A statement that must feed two sets — a `[BlobReference]` capture beside `@tb{b}_new` or `@tb{b}_touched` — outputs into a per-statement capture table `@tb{b}_cap{n} ([Id] int NOT NULL, [ParentId] int NULL, [Old_<Column>] int NULL, [New_<Column>] int NULL, …)` carrying every captured column, and the sets are filled from it by `INSERT … SELECT` immediately after the statement. For every `[BlobReference]` column the emitter declares `@tb{b}_blob_<Table>_<Column> TABLE ([RowId] int NOT NULL, [OldBlobId] int NULL, [NewBlobId] int NULL)` before the table's statements and fills it — directly (`OUTPUT inserted.[Id], NULL, inserted.[<Column>]` on insert, `OUTPUT inserted.[Id], deleted.[<Column>], inserted.[<Column>]` on update, `OUTPUT deleted.[Id], deleted.[<Column>], NULL` on delete) when the statement feeds nothing else, through `@tb{b}_cap{n}` otherwise — for root and child statements and for query-driven deletes alike (seam 12). Blob kinds on child entities remain deferred only for the securable reason (ledger §7); the emitter shape is fixed now.

### 1.4 Tree statements (appended after a tree table's save; also after `Update` actions on activatable trees and after tree deletes)

New rows are inserted with the provisional node `/0/<Id>/` (unique; disjoint from every real path). For `gl.Centers`, ordinal 3 (`@tb3_t0` new, `@tb3_t1` existing):

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

After an `Update` action (activate/deactivate) the affected set is the target ids and T3b–T5b run alone (nodes are unchanged); after a delete, `@tb{b}_old` is filled from the deleted rows' nodes before the `DELETE` and T5a–T5b run after it. The whole-table form of T5 (every row, no scope) is the built-in weekly job `core.tree-verify`, which repairs and meters any drift (`tellma.data.tree.repairs`). Error 530 (recursion limit) → `TreeDepthExceededException` → `ValidationException(Tree.TooDeep)`; the T3 mismatch → `TreeCycleException` → `ValidationException(Tree.Cycle)`. `TreeCycleValidator<T>` (C#, RT1) loads `(Id, ParentId)` of the whole table through `Rows`, overlays the payload's parents, and reports a revisit on `ParentId`; the SQL fence is the guarantee.

### 1.5 Delete statements

```sql
DECLARE @tb4_keys TABLE ([Id] int PRIMARY KEY);
-- ByIds (@tb4_t0 : IdList, or IdStampList when ExpectedStamps was given): 404 before 403, both in SQL, all-or-nothing.
INSERT INTO @tb4_keys ([Id]) SELECT t.[Id] FROM [gl].[Centers] AS t WITH (UPDLOCK, ROWLOCK) JOIN @tb4_t0 AS s ON s.[Id] = t.[Id];
IF (SELECT COUNT(*) FROM @tb4_keys) <> (SELECT COUNT(*) FROM @tb4_t0) THROW 50404, N'Entity.NotFound', 1;
IF (SELECT COUNT(*) FROM (<keys body: Id IN @tb4_keys AND Filter>) AS q) <> (SELECT COUNT(*) FROM @tb4_keys)
    THROW 50403, N'RowSecurity', 1;
-- ByIds with stamps only (IdStampList carries ModifiedAt; a NULL stamp is Concurrency.StampRequired in C# before the batch):
IF EXISTS (SELECT 1 FROM [gl].[Centers] AS t JOIN @tb4_t0 AS s ON s.[Id] = t.[Id] WHERE t.[ModifiedAt] <> s.[ModifiedAt])
BEGIN  -- the message is built exactly as in §1.3 step 1 over the mismatched ids (all 'C'; 'M' is impossible after the 50404 check)
    THROW 50409, @tb4_msg, 1;
END;
-- ByQuery: the query conjoined with Filter, capped (Cap = MaxDeleteByQueryRows), then ExpectedCount verified:
INSERT INTO @tb4_keys ([Id]) SELECT TOP (@tb4_p0 + 1) [Id] FROM (<compiled keys query>) AS q;
IF (SELECT COUNT(*) FROM @tb4_keys) > @tb4_p0 THROW 50413, N'Delete.CapExceeded', 1;
IF (SELECT COUNT(*) FROM @tb4_keys) <> @tb4_p1 THROW 50428, N'Delete.CountMismatch', 1;          -- @tb4_p1 = ExpectedCount
-- WithDescendants: the closure by node, then asserted visible under Filter:
INSERT INTO @tb4_keys ([Id])
SELECT d.[Id] FROM [gl].[Centers] AS d
WHERE EXISTS (SELECT 1 FROM [gl].[Centers] AS a JOIN @tb4_t0 AS s ON s.[Id] = a.[Id]
              WHERE d.[Node].IsDescendantOf(a.[Node]) = 1);
IF (SELECT COUNT(*) FROM (<keys body: Id IN @tb4_keys AND Filter>) AS q) <> (SELECT COUNT(*) FROM @tb4_keys)
    THROW 50403, N'RowSecurity', 1;
-- children deepest first, explicit; then the root; blob capture OUTPUT … INTO where declared:
DELETE c FROM [core].[RoleMemberships] AS c WHERE c.[UserId] IN (SELECT [Id] FROM @tb4_keys);
DELETE t OUTPUT deleted.[Id] FROM [gl].[Centers] AS t WHERE t.[Id] IN (SELECT [Id] FROM @tb4_keys);
```

547 on a tree row whose children were not requested → `ForeignKeyViolationException` → `ValidationException(Fk.InUse)`. Tree tables append T5.

### 1.6 The persist round trip, assembled

The text the executor emits for a `Persist` batch on a caller's behalf, in order: (1) the connect prologue (seam 16, autocommit, `@tm_` names); (2) `IF @tm_Guard = 1 BEGIN`; (3) `SET XACT_ABORT ON; BEGIN TRAN;`; (4) the version-tag guard (seam 5, `THROW 50412`): both levels — the tenant rows in `@tm_expectedTags` (always including `permissions` on a caller batch) and the caller's `UserStamps.PermissionsTag` — read `WITH (REPEATABLEREAD, ROWLOCK)` so the shared locks are held to `COMMIT` and a concurrent permission bump waits for this persist rather than slipping past it; (5) `IF NOT EXISTS (SELECT 1 FROM [core].[Users] WHERE [Id] = @tm_UserId AND [IsActive] = 1) THROW 50401, N'CallerInvalid', 1;`; (6) `IAccessGuards` application lock (seam 11) whenever `WrittenTables` intersects `core.Users`, `core.Roles`, `core.RoleMemberships`, `core.Permissions` — in a save, an action or a delete alike; (7) the emitter's statements per table (§1.3) and the tree statements (§1.4); (8) capability statements — blob release/confirm (seam 12); (9) `ContributeAsync` statements — service hook then `ISaveEffect` components: raw `Sql` with declared writes (distribution guards `THROW 50600–50699`, mapped to `ValidationException` with the message as the code), notification inserts (seam 15), job inserts (seam 8); (10) `IAccessGuards` L1–L3 invariants (`THROW 50422`); (11) the row-level post-check over every saved root: `DECLARE @tm_visible int = (SELECT COUNT(*) FROM (<compiled: Root, Select = "Id", Restrictions = [KeySetRestriction("Id", @tb{b}_saved)], Filter = save grant>) AS q); IF @tm_visible <> (SELECT COUNT(*) FROM @tb{b}_saved) THROW 50403, N'RowSecurity', 1;` — inserted rows, edited rows and parents of synchronised children are all in `@tb{b}_saved`, so a caller cannot insert or edit a row out of the grant that permitted the save; (12) the read-back (entities, children, related, row echo) inside the transaction, bounded by `MaxEntitiesPerSave` and skipped when `ReturnEntities = false` (the default for `SaveSource = Import`, so bulk saves never hold their locks across a large result stream); (13) the tag bumps (seam 5, last before `COMMIT`); (14) `COMMIT;`; (15) `END;`. Post-commit callbacks and `ContributeDetails` extras run after the round trip.

A `Validate` batch (RT1 of a save) carries, per root table with existing rows in the payload: the before-image `Query<TEntity>` restricted to the existing ids under the caller's **`Read`** filter, and a `Count` of the same ids under the **`Save`** grant's filter. The pipeline compares in C#: an id absent from the before images is `NotFoundException` (404, indistinguishable from non-existence); a `Save` count below the loaded count is `ForbiddenException` (403, code `forbidden`, no ids named). This is the two-stage pre-check; inserts have no pre-check (step 11 covers them). Conformance tests: insert outside the filter → 403; edit a visible row so it leaves the filter → 403; save an invisible id → 404; save a visible-but-not-saveable id → 403.

### 1.7 Standard column sets

Keyed (`Entity<TKey>`): `Id int NOT NULL` (or `bigint`), `PK_<Table>` clustered, `sq_<Table>`.

Top-level audit (`TopLevelEntity<TKey>`):

| Column | Type | Null | Constraints |
|---|---|---|---|
| `CreatedAt` | `datetime2(7)` | no | server-stamped on insert |
| `CreatedById` | `int` | no | `FK_<Table>_CreatedById → core.Users(Id)` NO ACTION |
| `ModifiedAt` | `datetime2(7)` | no | concurrency token; no index |
| `ModifiedById` | `int` | no | `FK_<Table>_ModifiedById → core.Users(Id)` NO ACTION |

Temporal (`[Temporal]`): `ValidFrom`/`ValidTo datetime2(7) NOT NULL GENERATED ALWAYS AS ROW START/END` (shadow, excluded from the UDTT), `PERIOD FOR SYSTEM_TIME`, `SYSTEM_VERSIONING = ON (HISTORY_TABLE = <schema>.<Table>History)`; history keeps the default clustered `(ValidTo, ValidFrom)` index only; not on TPT roots; children follow the parent unless `[Temporal(false)]`.

Child (`ChildEntity<TKey>`): `<Parent>Id int NOT NULL`, `FK_<Table>_<Parent>Id → <ParentTable>(Id)` NO ACTION, `IX_<Table>_<Parent>Id (<Parent>Id) INCLUDE (Id)`; no audit columns.

Tree (`TreeEntity`): `ParentId int NULL FK_<Table>_ParentId → <Table>(Id)` NO ACTION (configured by the tree convention without a CLR navigation), `IX_<Table>_ParentId`; `Node hierarchyid NOT NULL` shadow, `UX_<Table>_Node`, excluded from the UDTT; `SubtreeCount int NOT NULL DF 1`; `ActiveSubtreeCount int NOT NULL DF 1` (`ActivatableTreeEntity` only). No `Level`, no `IsLeaf`.

Activatable (`IActivatable`): `IsActive bit NOT NULL DF_<Table>_IsActive (1)`; optional `IX_<Table>_Active (Id) WHERE IsActive = 1` on large tables.

Job entity (`IJobEntity`): `JobId int NULL`, `FK_<Table>_JobId → core.Jobs(Id) ON DELETE SET NULL`, `UX_<Table>_JobId (JobId) WHERE JobId IS NOT NULL`.

Uniqueness (`[Unique]`, `[NaturalKey]`): one `UX_<Table>_<Col>` per declaration, filtered `WHERE <Col> IS NOT NULL` for nullable columns; 2601/2627 map back to the property by index name; 547 maps by `FK_<Table>_<Column>`.

Schema evolution (N−1 rule, binding on the platform and on every distribution's own tables): the emitter's `INSERT` lists columns explicitly and every reader binds by name, never by ordinal, so a database one migration ahead of the application keeps working; a new column is nullable or defaulted; a column is dropped only after one release in which no shipped application version writes or reads it (expand, then contract); renames are add-copy-drop; the UDTT window of spec 0001 applies to table types.

Enum property: `varchar(n)`; multilingual group `P`: `P nvarchar(k) NOT NULL`, `P2`/`P3 nvarchar(k) NULL`; `[JsonColumn]`: `nvarchar(max)`; `[BlobReference]`: `int NULL`, `FK_<Table>_<Column> → core.Blobs(Id)` NO ACTION, `IX_<Table>_<Column>`.

### 1.8 Standalone table types (`Tellma.Core.Abstractions.TableTypes`; physical `[dbo].[<Name>_<hash8>]`)

| Type | Columns | Owner / use |
|---|---|---|
| `IdList` (exists) | `Id int PK` | every id-shaped restriction and capture |
| `BigIdList`, `GuidList`, `StringList` (exist) | `Id bigint / uniqueidentifier / nvarchar(450) PK` | `KeySetRestriction`; `@tm_tagNames` uses `StringList` |
| `IdStampList` | `Id int PK`, `ModifiedAt datetime2(7)` | `DeleteSpec.ByIds` with `ExpectedStamps` (§1.5) |
| `JobRequestList` | `Ordinal int PK`, `HandlerKey nvarchar(64)`, `DueAt datetime2(3) NULL`, `ArgumentsJson nvarchar(max) NULL`, `TraceParent nvarchar(55) NULL`, `RunAsUserId int`, `RequestedById int NULL` | 0019 enqueue; ids assigned in SQL |
| `VersionTagList` | `Name nvarchar(128) PK`, `Tag uniqueidentifier` | 0012 write guard |
| `TenantMembershipRecord` → `[catalog].[TenantMembershipList]` | `TenantId int`, `Subject nvarchar(36)`, `IsActive bit` | 0010 catalog (registered on `CatalogDbContext`) |
| `UserPreferenceList` | `Key varchar(128) PK`, `Value nvarchar(max)` | 0013 self-service statement |
| `NotificationPreferenceList` | `Type nvarchar(64) PK`, `Inbox bit`, `Email bit` | 0020 self-service statement |
| `UserInvitationOutcomeList` | `UserId int PK`, `Subject varchar(255)`, `InviteStatus varchar(9)`, `LastInviteError nvarchar(1024)` | 0017 invite write-back |
| `JobOutcomeList` | `Id int PK`, `Status varchar(9)`, `Released bit`, `RetryAfterSeconds int NULL`, `ErrorCode nvarchar(64) NULL`, `ErrorMessage nvarchar(1024) NULL`, `ErrorDetails nvarchar(max) NULL`, `StateJson nvarchar(max) NULL` | 0019 completion |
| `JobProgressList` | `Id int PK`, `ProgressPercent tinyint NULL`, `ProgressMessage nvarchar(256) NULL`, `StateJson nvarchar(max) NULL` | 0019 renewal / `Append` |
| `JobLeaseList` | `Id uniqueidentifier PK`, `LeaseSeconds int` | 0019 renewal |
| `ScheduleNextList` | `ScheduleId int PK`, `NextDueAt datetime2(3) NULL`, `LastScheduledFor datetime2(3) NULL`, `Due bit` | 0019 tick |
| `NotificationRowList` | `Ordinal int PK`, `UserId int` | 0020 insert; ids assigned in SQL |

Entity UDTTs are derived per spec 0001 (`<Table>List_<hash8>` in the table's schema).

---

## 2. Seam 2 — Entity class versus wire shape (owner 0011; consumers 0014, 0015, 0018)

One class is storage shape and wire shape. Ids on the wire: `0` new, `< 0` temporary (unique within the payload, rewritten in every self-typed FK and child parent key), `> 0` update. JSON options (0015): `MaxDepth = 16`, strict numbers (`decimal(19,4)` as JSON numbers; the SPA parses losslessly), unknown members skipped and counted (`tellma.api.unknown_members`), enums as strings, `ModifiedAt` round-trips with seven fractional digits, multilingual twins absent when the tenant lacks the language.

### 2.1 Entity bases and capability interfaces

```contract
// Tellma.Core.Abstractions.Entities
base Entity<TKey>                             // TKey ∈ int | long
  Id: TKey                                    server-owned, PK; default means new
base TopLevelEntity<TKey> : Entity<TKey>
  CreatedAt: datetime2(7)                     server-owned
  CreatedById: int                            server-owned, FK -> core.Users
  ModifiedAt: datetime2(7)                    server-owned, concurrency token (read as the expected stamp on save)
  ModifiedById: int                           server-owned, FK -> core.Users
base TopLevelEntity : TopLevelEntity<int>
base ChildEntity<TKey> : Entity<TKey>         // saved only with its owner; no audit columns; owning FK marked [ParentKey]
base ChildEntity : ChildEntity<int>
base TreeEntity<TKey> : TopLevelEntity<TKey>
  ParentId: TKey?                             FK -> self; no CLR navigation (the Queryex navigation Parent derives from the column name)
  SubtreeCount: int                           server-owned; 1 for a leaf
  // Node hierarchyid is a shadow column added by the platform's tree convention; never a member
base TreeEntity : TreeEntity<int>
base ActivatableTreeEntity<TKey> : TreeEntity<TKey>, IActivatable
  IsActive: bool = true                       write-once
  ActiveSubtreeCount: int                     server-owned
base ActivatableTreeEntity : ActivatableTreeEntity<int>

contract IActivatable
  IsActive: bool                              write-once; settable on create (save with id 0, Insert import, the insert half of Upsert); afterwards changed only by the activate/deactivate actions
contract IJobEntity
  JobId: int?                                 server-owned, FK -> core.Jobs ON DELETE SET NULL, unique where not null
```

System-written entities (`Job`, `Notification`, `Blob`) derive from `Entity<int>`, carry `CreatedAt` explicitly, and are never written through `Save`.

No tree base declares a CLR `Parent` property. The tree convention configures the self-referencing FK without a navigation; the Queryex navigation `Parent` is derived from the FK column name (`ParentId` → `Parent`) exactly as every other navigation-less FK is, so `Parent.Name`, `descendantOf`, `ancestorOf` and `level(Node)` work unchanged. A details read carries the parent row in `Related` (the `Parent` navigation is in the default `[RelatedSelect]` projection), so the UI needs no CLR navigation; EF LINQ users of the `Linq<T>()` escape hatch join on `ParentId` explicitly. A tree entity is therefore extended by plain inheritance like `User` and `Role` (`MyCenter : Center`), and `tellma.UseEntity<TDefault, TLeaf>()` with `TLeaf : TDefault` is the one constraint for every entity kind.

### 2.2 Annotations

```contract
// Tellma.Core.Abstractions.Entities
annotation [Temporal(enabled: bool = true)]   on type; inherited; children follow the parent unless [Temporal(false)]
annotation [Tree(MaxDepth: int = 32)]         on type; inherited
annotation [ParentKey]                        on property: the child's owning foreign key
annotation [Children(parentKey: string)]      on a [NotMapped] list<TChild>; only to disambiguate two FKs to the parent type
annotation [ServerOwned]                      on property: client value overwritten from the before image (update) or the fresh default (insert)
annotation [WriteOnce]                        on property: excluded from every UPDATE; a changed value on update is the error WriteOnce
annotation [SelfEditable]                     on property: the column set a self-service save may change
annotation [NaturalKey(Order: int = 0)]       on property; implies [Unique]; composite keys reserved
annotation [Unique]                           on property: index + built-in validator + 2601/2627 mapping
annotation [Searchable(Kind: SearchKind = Contains)]   on string property
annotation [PreserveWhitespace]               on string property: opts out of trim-and-null normalisation
annotation [JsonColumn]                       on string property: nvarchar(max), opaque text
annotation [Multilingual]                     on the primary property of a P/P2/P3 group; twins found by name
annotation [Cacheable(MaxRows: int = 1000)]   on type; inherited
annotation [BumpsVersionTag(name: string)]    on type; repeatable; inherited
annotation [BumpsUserVersionTag(column: UserVersionTagNames, userIdProperty: string = "UserId")]   on type; repeatable; inherited
annotation [BlobReference(kind: string, preset: BlobPreset = Attachment, MaxSizeBytes: long? = null, ReadAccess: BlobReadAccess = OwnerRead)]   on int? property
annotation [ExcludeFromExcel]                 on property
annotation [Stack(...)], [ApiResource(...)], [DefaultSelect(...)], [RelatedSelect(...)], [DetailsExpand(...)]   on type — seam 3
enum SearchKind = Contains | Prefix
enum BlobPreset = Attachment | Avatar | Photo           // placed here because [BlobReference] names it
enum BlobReadAccess = OwnerRead | AnyMember
```

A child collection is any `[NotMapped]` `list<TChild>` property whose `TChild : ChildEntity<TKey>`; its name is the child's table name (`RoleMemberships`). Ownership of a property, as `EntityMetadata` reports it: `[ServerOwned]` → `ServerOwned`; `[WriteOnce]` → `WriteOnce`; `[SelfEditable]` → `SelfEditable`; otherwise `Editable`. Audit columns, `Id`, `SubtreeCount`, `ActiveSubtreeCount`, `JobId`, and every invitation-state column are server-owned by derivation; `IsActive` is `WriteOnce` by derivation — the emitter's `INSERT` takes it from the payload (default `true`) and never lists it in an `UPDATE`, and a changed value on update is the `WriteOnce` error at the path.

The related projection when `[RelatedSelect]` is absent is fixed and narrow: `Id`, the `[Multilingual]` `Name` group, `Code` when the entity has one, and every `[BlobReference]` column whose preset is `Avatar`. Anything wider must be declared; a declaration replaces the default rather than extending it. Core's `User` and `Role` carry no declaration and are projected by the default (`Id, Name, Name2, Name3, ImageId` and `Id, Name, Name2, Name3, Code`), so `CreatedBy.Email`, `CreatedBy.Subject` and the contact columns never travel through a navigation. The realised gate warns when an entity has a `[Searchable]` or `[NaturalKey]` string outside its projection and no declaration. Conformance test: `select CreatedBy.Email` by a caller without `core.User × Read` is refused.

Illustration (a distribution's complete entity):

```csharp
[Table("Centers", Schema = "gl"), TableType]
public sealed class MyCenter : Center { [MaxLength(50)] public string? Region { get; set; } }
```

### 2.3 Entity metadata

```contract
// Tellma.Core.Abstractions.Data
data EntityMetadata
  ClrType: Type   Name: string   Table: TableName   TableTypePhysicalName: string
  Key: PropertyMetadata   Properties: list<PropertyMetadata>
  Children: list<ChildCollectionMetadata>   References: list<ReferenceMetadata>
  NaturalKeys: list<NaturalKeyMetadata>   NaturalKey: PropertyMetadata?          // first of NaturalKeys
  MultilingualGroups: list<MultilingualGroup>   UniqueIndexes: list<UniqueIndexMetadata>
  IsTopLevel: bool   IsTemporal: bool   IsActivatable: bool   IsJobEntity: bool   IsCacheable: bool
  Tree: TreeMetadata?   BlobReferences: list<BlobReferenceMetadata>
  SchemaFingerprint: string                   // hex SHA-256 of the editable shape; the Excel manifest carries it
  ResetServerOwned(entity: object)   sync     // overwrites server-owned members from a fresh instance
record PropertyMetadata(Name: string, Column: string, ClrType: Type, Ownership: PropertyOwnership, IsNullable: bool, IsUnique: bool,
  MaxLength: int?, Precision: int?, Scale: int?, EnumValues: list<string>?, MultilingualGroup: string?,
  IsSearchable: bool, SearchKind: SearchKind?, ExcludedFromExcel: bool, Getter: (object) -> object?, Setter: (object, object?) -> void)
enum PropertyOwnership = Editable | WriteOnce | ServerOwned | SelfEditable
record ChildCollectionMetadata(Property: string, Child: EntityMetadata, ParentKey: PropertyMetadata, Getter: (object) -> list<object>?)
record ReferenceMetadata(Navigation: string, ForeignKey: PropertyMetadata, Target: EntityMetadata)
record NaturalKeyMetadata(Property: PropertyMetadata, IsDeclared: bool, Order: int)
record MultilingualGroup(Name: string, Primary: PropertyMetadata, Secondary: PropertyMetadata?, Ternary: PropertyMetadata?)
record UniqueIndexMetadata(IndexName: string, Properties: list<PropertyMetadata>)
record TreeMetadata(ParentKey: PropertyMetadata, NodeColumn: string, SubtreeCount: PropertyMetadata, ActiveSubtreeCount: PropertyMetadata?, MaxDepth: int)
record BlobReferenceMetadata(Property: PropertyMetadata, Kind: string, Policy: BlobKindPolicy)
service IEntityMetadataProvider
  Get(entityType: Type) -> EntityMetadata   sync
  All: list<EntityMetadata>

// Columnar rows (shape owned by 0015, placed here so .Data never references .Api; 0015 owns the JSON converter)
enum QueryColumnKind = Int32 | Int64 | Int16 | Byte | Decimal | Double | Boolean | String | Date | DateTime | DateTimeOffset | Time | Guid | Binary | HierarchyId
record QueryColumn(Name: string, Type: string, StoreType: string?, Kind: QueryColumnKind, Nullable: bool, Path: list<string>?, GroupingKey: bool)
contract QueryRowSet
  Columns: list<QueryColumn>
  RowCount: int
  GetBuffer(column: int) -> Array   sync      // int[], long[], decimal[], bool[], string?[], DateOnly[], DateTime[], DateTimeOffset[], byte[][], Guid[]
  IsNull(column: int, row: int) -> bool   sync
  Builder(columns: list<QueryColumn>, capacity: int)   // filled by the reader through typed getters, one AppendRow per row
contract RelatedEntities                     // typed sets restricted to each entity's [RelatedSelect] projection
  Add<T>(entityName: string, entities: list<T>, projection: set<string>)   sync
  Sets: map<string, RelatedEntitySet>
record RelatedEntitySet(EntityType: Type, Entities: list<object>, Projection: set<string>)
```

---

## 3. Seam 3 — One capability, declared once (owner 0014; consumers 0011, 0013, 0015, 0018, 0019)

The declaration is the entity's own shape plus `[EntityAction]`/`[ApiAction]` on the service; everything else is projected through `StackDescriptor`, the only thing HTTP, MCP, the SPA schema, the securables registration, the Excel feature and the conformance tests read.

| Capability | Declared by | Projects |
|---|---|---|
| Keyed, audited | `TopLevelEntity` | `Read`/`Save`/`Delete` securables; `query`, `get`, `get-by-ids`, `save`, `delete`, `delete-by-query`; `ModifiedAt` concurrency; `[Unique]`/`[NaturalKey]` validators |
| Activatable | `IActivatable` | `Activate` securable; `activate`, `deactivate` actions; `IncludeInactive` default filter (`IsActive = true` conjunct lifted by the flag); `IsActive` write-once (settable on create, afterwards only through the actions) |
| Tree | `TreeEntity` | `get-by-parent-ids`, `delete-with-descendants`; `IncludeAncestors`; `TreeCycleValidator`; §1.4 |
| Temporal | `[Temporal]` | skip-unchanged; history table |
| Blob reference | `[BlobReference]` | attach validator + confirm/release effect; download authorisation; Excel exclusion (seam 12) |
| Multilingual | `[Multilingual]` | gating by `MultilingualShape`; `(E)`/`(ع)` labels; search over the group |
| Cacheable | `[Cacheable]` | `entity:<Name>` tag; `all` operation (`GetAllCached`); `Read` securable registered with `FilterRoot = null` |
| Job entity | `IJobEntity` | the claim's entity join load (seam 8) |
| Searchable | `[Searchable]` | the `Search` disjunction (`LIKE`, `Contains`/`Prefix`) |
| Excel | `Query`/`Save` in `StackOperations` | `export`, `export-for-import` (`Read`); `inspect-import`, `import` (`Save`) — seam 19 |

### 3.1 The descriptor and registration

```contract
// Tellma.Core.Abstractions.Crud
record StackDescriptor(Entity: string, Resource: string, ResourceSegment: string, EntityType: Type, ServiceType: Type, KeyType: Type,
  Description: string?, Operations: StackOperations, Capabilities: set<string>, Actions: list<ActionDescriptor>,
  Properties: list<PropertyMetadata>, Children: list<ChildCollectionDescriptor>, SearchableProperties: list<(string, SearchKind)>,
  NaturalKeys: list<string>, DefaultFilter: string?, DefaultSelect: string, RelatedSelect: set<string>, DetailsExpand: list<string>,
  Limits: StackLimits, Mcp: McpExposure, Public: bool)
record ActionDescriptor(Name: string, Segment: string, Permission: string?, Kind: ActionKind, SupportsFilter: bool, ArgumentsType: Type?,
  RequestType: Type?, ResultType: Type, Description: string?, IsBuiltIn: bool, PostCheck: bool, MemberOnly: bool, Idempotent: bool,
  Destructive: bool, Mcp: McpExposure)
enum ActionKind = EntityAction | ApiAction
record ChildCollectionDescriptor(Property: string, ChildType: Type, ParentKeyProperty: string)
record StackLimits(MaxTake: int, CountCap: int, MaxSaveCount: int, MaxChildrenPerEntity: int, MaxIds: int, MaxDeleteByQueryRows: int,
  MaxExpandDepth: int, MaxValidationRounds: int, MaxSearchLength: int, MaxExtras: int, LargeBatchThreshold: int)
enum StackOperations = None | Query | Details | Save | Delete | Import | Export   // flags; Read = Query | Details | Export; All
record ApiServiceDescriptor(Route: string, ServiceType: Type, Actions: list<ActionDescriptor>)
service IStackRegistry
  Stacks: list<StackDescriptor>
  Services: list<ApiServiceDescriptor>
  Find(entity: string) -> StackDescriptor?   sync
  FindByResource(resource: string) -> StackDescriptor?   sync
record StackContributionItem(EntityType: Type, ServiceType: Type?, Operations: StackOperations?) : FeatureContributionItem
record ApiServiceContributionItem(ServiceType: Type) : FeatureContributionItem      // an [ApiRoute] service; its [ApiAction]s are registered as securables

annotation [Stack(Operations: StackOperations = All, MaxTake = 10000, CountCap = 10000, MaxSaveCount = 10000, MaxChildrenPerEntity = 10000,
  MaxIds = 10000, MaxDeleteByQueryRows = 10000, MaxExpandDepth = 3, MaxValidationRounds = 3, MaxSearchLength = 200, MaxExtras = 16,
  LargeBatchThreshold = 1000, DefaultFilter: string? = null, DefaultSelect: string? = null)]   on entity type; inherited
annotation [EntityAction(Name: string, Permission: string? = null, SupportsFilter = true, LoadChildren = false, PostCheck = false,
  Idempotent = false, Destructive = false, Mcp: McpExposure = Full, Description: string? = null)]
  on a method of an EntityService: M(context: ActionContext<TEntity, TKey> [, arguments: TArguments])
annotation [ApiAction(Name: string, Action: string? = null, Resource: string? = null, MemberOnly = false, Idempotent = false, Destructive = false,
  Mcp: McpExposure = Full, Description: string? = null)]   on a public method taking one body (or none) on an entity service or an [ApiRoute] service
annotation [ApiRoute(Route: string)]         on a non-entity service class ("inbox", "settings", "access")

// Tellma.Core.Abstractions.Api
annotation [ApiResource(Segment: string? = null, Description: string? = null, Mcp: McpExposure = Full, Public: bool = false)]   on entity type; inherited
annotation [DefaultSelect(Select: string)]   on entity type; inherited
annotation [RelatedSelect(Select: string)]   on entity type; inherited   // the projection visible through navigations
annotation [DetailsExpand(Navigations: list<string>)]   on entity type; inherited   // default: every FK navigation of the entity and its children
enum McpExposure = Full | ReadOnly | Hidden
```

| Member | Meaning |
|---|---|
| `[EntityAction].Name` | The kebab-case route segment and MCP action name (`invite`, `deactivate`). `Permission` defaults to its PascalCase form (`Invite`); several actions may share one (`activate`/`deactivate` → `Activate`). |
| `[EntityAction].SupportsFilter` | The securable is registered with `FilterRoot = Entity`; the action's rows are loaded under the grant's filter. |
| `[EntityAction].PostCheck` | Re-run the grant's filter over the rows after the write (off: actions commonly leave the filter that permitted them). |
| `[ApiAction].Action` | The securable action the operation requires; `MemberOnly = true` means any connected active member and no securable. On an entity service the pair `(Descriptor.Resource, Action, FilterRoot = Entity)` is registered automatically by the stack feature's contributor, exactly as every `[EntityAction]`'s permission is — a distribution declares no securable by hand. On an `[ApiRoute]` service `Resource` is required beside `Action` (`SettingsService.Save` names `core.Settings.General`), and `contribution.ApiService<TService>()` registers each pair with `FilterRoot = null`. Excel's four operations pass `Action = "Read"` / `"Save"` explicitly. |
| `[ApiResource]` | Optional. `contribution.Entity<T>()` projects the web and MCP endpoints regardless; the attribute only overrides `Segment`, `Description`, `Mcp` and `Public`. |
| `StackDescriptor.Resource` | The securable resource id `<schema>.<Entity>` (`core.User`); `ResourceSegment` is the kebab plural of the table (`users`). |
| `Capabilities` | Closed vocabulary: `activatable`, `tree`, `temporal`, `multilingual`, `cacheable`, `blobs`, `job-entity`, `searchable`, `excel`. |

Standard operations, their segments and securable actions: `query`, `get`, `get-by-ids`, `get-by-parent-ids`, `all`, `export`, `export-for-import` → `Read`; `save`, `inspect-import`, `import` → `Save`; `delete`, `delete-by-query`, `delete-with-descendants` → `Delete`; `activate`, `deactivate` → `Activate`. Composition: `contribution.Entity<Center, CenterService<Center>>()`; a distribution substitutes its leaf with `tellma.UseEntity<Center, MyCenter>()` (seam 6).

### 3.2 The service base, the pipeline, and the hooks

```contract
// Tellma.Core.Abstractions.Crud
base EntityService<TEntity, TKey>   where TEntity: Entity<TKey>   where TKey: struct
  Descriptor: StackDescriptor
  QueryAsync(request: QueryRequest) -> QueryResult
  GetByIdAsync(id: TKey, request: DetailsRequest) -> EntitiesResult<TEntity>                 // NotFoundException when absent or invisible
  GetByIdsAsync(ids: list<TKey>, request: DetailsRequest) -> EntitiesResult<TEntity>         // partial: absent ids are omitted
  GetAllCachedAsync(request: DetailsRequest) -> EntitiesResult<TEntity>                       // [Cacheable] stacks only
  SaveAsync(entities: list<TEntity>, options: SaveOptions) -> EntitiesResult<TEntity>
  DeleteByIdsAsync(ids: list<TKey>, expectedStamps: list<datetime2(7)?>? = null) -> AffectedResult
  DeleteByQueryAsync(request: DeleteByQueryRequest) -> AffectedResult
  ExecuteActionAsync(action: string, ids: list<TKey>, arguments: object?, options: ActionOptions) -> EntitiesResult<TEntity>
  // overridable hooks, empty defaults; the pipeline sees them through IEntityBehavior
  PreprocessAsync(context: SaveContext<TEntity, TKey>)
  ValidateAsync(context: SaveContext<TEntity, TKey>)
  ValidateDeleteAsync(context: DeleteContext<TEntity, TKey>)
  ValidateActionAsync(action: string, context: ActionContext<TEntity, TKey>)
  ContributeAsync(context: PersistContext<TEntity, TKey>)
  AfterCommitAsync(outcome: SaveOutcome<TEntity, TKey>)
  ContributeDetails(plan: DetailsPlan<TEntity, TKey>)   sync
  SearchFilter(search: string) -> FilterTree?   sync            // null keeps the platform disjunction over [Searchable] columns
  BespokeGrant(action: string, context: RequestContext) -> FilterTree?   sync   // disjoined with the stored grants as an AccessCriterion
base EntityService<TEntity> : EntityService<TEntity, int>

// Public so that EntityService (in Abstractions) can delegate to Tellma.Core; not for distributions (EditorBrowsable(Never) in code)
service IEntityPipeline<TEntity, TKey>       // the operations above, implemented in Tellma.Core.Crud; injected into EntityService's constructor
  Query / GetById / GetByIds / GetAllCached / Save / DeleteByIds / DeleteByQuery / ExecuteAction   // same signatures, plus the behavior: IEntityBehavior<TEntity, TKey>
contract IEntityBehavior<TEntity, TKey>      // the hooks as the pipeline sees them; EntityService implements it explicitly
  Preprocess / Validate / ValidateDelete / ValidateAction / Contribute / AfterCommit / ContributeDetails / SearchFilter / BespokeGrant

// extension methods; the constraint is the compile-time gate
ActivateAsync(service, ids: list<TKey>, options: ActionOptions?) -> EntitiesResult<TEntity>   where TEntity: IActivatable
DeactivateAsync(service, ids: list<TKey>, options: ActionOptions?) -> EntitiesResult<TEntity>   where TEntity: IActivatable
GetByParentIdsAsync(service, request: ParentIdsRequest) -> QueryResult   where TEntity: TreeEntity<TKey>
DeleteWithDescendantsAsync(service, ids: list<TKey>) -> AffectedResult   where TEntity: TreeEntity<TKey>

contract IEntityValidator<in TEntity>        // composable; resolved for the leaf and every base; registration order
  ValidateAsync(context: SaveContext<TEntity>)
  ValidateDeleteAsync(context: DeleteContext<TEntity>)
contract ISaveEffect<in TEntity>
  ContributeAsync(context: PersistContext<TEntity>)
  AfterCommitAsync(outcome: SaveOutcome<TEntity>)
contract IDetailsContributor<in TEntity>
  Extras: list<string>
  Contribute(plan: DetailsPlan<TEntity>, requested: set<string>)   sync

data DetailsRequest
  Select: string?                             // row-echo select; null = no echo
  Include: list<string> = []                  // extras by name
  None: DetailsRequest                        // static
data SaveOptions
  ReturnEntities: bool = true                 // false by default when Source = Import (the read-back stays inside the transaction)
  Details: DetailsRequest = DetailsRequest.None
  Concurrency: ConcurrencyMode = Check        // under Check a default ModifiedAt on an update is Concurrency.StampRequired
  Source: SaveSource = Web
  SelfService: bool = false                   // the SET list is the [SelfEditable] columns only; child collections are forced to null; ids other than the caller's are Users.SelfServiceOnly
enum SaveSource = Web | PublicApi | Agent | Import | System
record ActionOptions(ReturnEntities: bool = false, Details: DetailsRequest? = null)
data CrudTelemetryNames                       // constants; MeterName = "Tellma.Core"
  Operations = "tellma.crud.operations"; ValidationRounds = "tellma.crud.validation.rounds"; ConcurrencyOverrides = "tellma.crud.concurrency.overrides"; RoundTrips = "tellma.crud.roundtrips"
  OperationTag = "crud.operation"; SourceTag = "crud.source"; TagTag = "crud.tag"   // crud.tag ∈ permissions | settings
```

`IEntityPipeline<TEntity, TKey>` and `IEntityBehavior<TEntity, TKey>` are public contracts in `Tellma.Core.Abstractions.Crud` because `EntityService` lives in Abstractions and `Tellma.Module.Gl` must compile against it while the implementation lives in `Tellma.Core`; a distribution never names them — its service subclass passes the pipeline through to the base constructor and overrides hooks. Validators, effects and details contributors are registered through `contribution.Validator<TEntity, TValidator>()`, `SaveEffect<,>()`, `DetailsContributor<,>()` (seam 6); a component registered for a base (`Center`) runs for the leaf (`MyCenter`) by contravariance; "registration order" is feature order (the `Requires` closure) then declaration order within a feature. The web projection maps `GetRequest` → `GetByIdAsync`, `IdsRequest` → `GetByIdsAsync`/`DeleteByIdsAsync`/actions, `SaveRequest<T>` → `SaveAsync`, `ParentIdsRequest` → `GetByParentIdsAsync`.

### 3.3 Validation, persist, post-commit and details contexts

```contract
// Tellma.Core.Abstractions.Validation
data SaveContext<TEntity, TKey>
  Entities: list<TEntity>                     // after preprocessing, ids assigned, payload order
  Before(index: int) -> TEntity?   sync       // before image (null for inserts), loaded under the caller's Read filter; the Save-filter count is the second pre-check stage (§1.6)
  Children: ChildImages                       // child before images by child type then parent id
  Options: SaveOptions
  Context: RequestContext
  Loader: IContextLoader
  Errors: ValidationErrors
  Round: int                                  // 1-based; bounded by MaxValidationRounds
  IsNew(index: int) -> bool   sync
  Changed<TValue>(index: int, property: (TEntity) -> TValue) -> bool   sync   // false for inserts
data ChildImages                              // ByType(childType) -> map<parentId, list<object>>
data DeleteContext<TEntity, TKey>             Ids: list<TKey>   Entities: list<TEntity>   Context: RequestContext   Loader: IContextLoader   Errors: ValidationErrors
data ActionContext<TEntity, TKey>
  Entities: list<TEntity>                     // the visible target rows; children when LoadChildren
  Arguments: object?
  Context: RequestContext
  Loader: IContextLoader
  Errors: ValidationErrors
  Batch: IDataBatch                           // the persist batch
  Save(entities: list<TEntity>)   sync        // marks rows for the emitter with stamping
  Update(assignments: map<string, object?>)   sync   // one UpdateSpec over the target ids under the grant's filter
  Notify(request: NotificationRequest)   sync

service IContextLoader                        // DataLoader over the round's batch; dedup by structural key
  ByIds<T, TK>(ids: list<TK>, select: string?) -> ContextPromise<map<TK, T>>
  ByKey<T, TK>(key: string, values: list<TK>, select: string?) -> ContextPromise<map<TK, T>>          // unique property, TVP-restricted
  ByParentIds<TChild, TK>(parentIds: list<TK>, select: string?) -> ContextPromise<map<TK, list<TChild>>>
  Query<T>(filter: FilterTree, arguments: QueryArguments?, select: string?) -> ContextPromise<list<T>>
  Visible<T>(filter: FilterTree, arguments: QueryArguments?, select: string?) -> ContextPromise<list<T>>   // read grant conjoined
  Rows(spec: QuerySpec, arguments: QueryArguments?) -> ContextPromise<QueryRowSet>
  Exists(root: string, filter: FilterTree, arguments: QueryArguments?) -> ContextPromise<bool>
  Count(root: string, filter: FilterTree, cap: int, arguments: QueryArguments?) -> ContextPromise<int>
  Ancestors<T, TK>(ids: list<TK>) -> ContextPromise<map<TK, list<TK>>>
  Sql(sql: FormattableString, options: SqlOptions?) -> ContextPromise<RawResult>
  Prime<T, TK>(entity: T)   sync
record ContextPromise<T>                      // awaitable; IsLoaded; Value throws before the round executes
data ValidationErrors
  HasErrors: bool
  Items: list<ValidationError>
  Add(path: string, code: string, arguments: map<string, string>?)   sync
  Add<TEntity, TValue>(index: int, property: (TEntity) -> TValue, code: string, arguments: map<string, string>?)   sync   // "[index].Property"
record ValidationError(Path: string, Code: string, Arguments: map<string, string>)   // Path wire-cased by 0015: entities[0].roleMemberships[2].roleId
data ValidationCodes                          // constants
  Required | MaxLength | Range | Unique | WriteOnce | Precision | Entity.NotFound | Entity.DuplicateId | Fk.NotFound | Fk.InUse
  | Tree.Cycle | Tree.TooDeep | Blob.NotAttachable | Blob.DuplicateReference | Concurrency.StampRequired | Import.LanguageNotConfigured

// Tellma.Core.Abstractions.Crud
data PersistContext<TEntity, TKey>
  Entities: list<TEntity>
  Before(index: int) -> TEntity?   sync
  Context: RequestContext
  Options: SaveOptions
  Batch: IDataBatch
  SavedIds: SqlIdentifier                     // @tb{b}_saved
  NewIds: SqlIdentifier                       // @tb{b}_new
  TouchedIds: SqlIdentifier                   // @tb{b}_touched
  Notify(request: NotificationRequest)   sync // INotifier.Notify on Batch
data SaveOutcome<TEntity, TKey>
  Entities: list<TEntity>   Before(index: int) -> TEntity?   Ids: list<TKey>   DeletedIds: list<TKey>   Stamp: datetime2(7)   Context: RequestContext   Outcome: BatchOutcome
data DetailsPlan<TEntity, TKey>
  Ids: list<TKey>   Request: DetailsRequest   Context: RequestContext   IdsSource: SqlIdentifier   // @tb{b}_main
  AddExtra(name: string, sql: FormattableString, options: SqlOptions?)   sync
  AddExtra(name: string, spec: QuerySpec, arguments: QueryArguments?)   sync
```

Round-trip ledger (warm caches): read 1; create with no context loads 1; update 2; +1 per dependent validation round (max 3); +1 on a cold or stale path; delete/activate 1 (+1 when the service overrides the validation hook); import per chunk 2 (+1 hydration).

---

## 4. Seam 4 — The Queryex schema per tenant configuration (owner 0011; consumers 0012, 0013, 0018)

```contract
// Tellma.Core.Abstractions.Data
service IQueryexSchemaProvider               // singleton; at most three schemas per process, built from the EF model
  GetSchema(shape: MultilingualShape) -> QueryexSchema   sync
  Fingerprint: string                         // changes with the model; part of the engine's cache identity

// Tellma.Core.Queryex — amendments documented by 0011 (spec 0008 is frozen)
record KeySetRestriction(Path: string, TableSource: string)   // "<Path> IN (SELECT [Id] FROM <TableSource>)"; TableSource = a TVP parameter or a @tb{b}_ table
data QuerySpec                                Restrictions: list<KeySetRestriction> = []     // part of the cache key by shape, never by values
record CompiledQuery                          Prologue: string   Body: string                  // Prologue declares @qx{b}_ tables; Body is the SELECT
// level(node) -> Numeric emits [Node].GetLevel(); Skip/Take are parameter slots (Origin = Declared, names @qx{b}_skip/@qx{b}_take);
// today() and the TimeZone slot bind the tenant zone (QueryexContextValues); the host binds IdList/BigIdList/GuidList/StringList by the path's key type.
```

Schema rules: every mapped leaf (children included) is a root; navigations from EF FKs, named by the CLR navigation when one exists and derived from the FK column name otherwise (`ParentId` → `Parent` on tree entities, which declare no CLR navigation; `CreatedBy`, `ModifiedBy`, `User`, `Role`, `Image`, `Blob`); enums as string store types; `[JsonColumn]` members, `StorageKey`, `Sha256`, `Jobs.ErrorDetails`, period columns and `[NotMapped]` members are absent; `TreeNode` = the shadow `Node`; `Name2`/`Name3` present per `MultilingualShape`. RLS composition and child-entity path rewriting are `FilterTree` work (`FilterTree.Via` reserved by 0013 D16, not built). `[Cacheable]` entities and `UserStamp`, `Blob` are read-only roots reachable as documented in seams 11–12.

---

## 5. Seam 5 — Version tags and the versioned cache (owner 0012; consumers 0011, 0013, 0014, 0015)

### 5.1 Contracts

```contract
// Tellma.Core.Abstractions.Caching
record VersionTag(Value: Guid)                // equality only; Guid.Empty = never read
  None: VersionTag                            // static
  New() -> VersionTag                         // static, sync; application-generated
  ToWire(formatVersion: int) -> string   sync // "{formatVersion}.{Value:N}"
data VersionTagNames                          // constants
  Settings = "settings"; Permissions = "permissions"; Entities = "entities"; EntityPrefix = "entity:"; MaxNameLength = 128
  ForEntity(entityName: string) -> string     // static, sync
enum UserVersionTagNames = Permissions | Preferences   // columns PermissionsTag, PreferencesTag on core.UserStamps
record VersionTagSnapshot(TenantId: int, Tags: map<string, VersionTag>, ReadAtUtc: DateTime)
  this[name: string] -> VersionTag   sync     // None when absent
  Has(name: string) -> bool   sync            // false = the kind is uncacheable on this instance
service IVersionTagSnapshots
  Current(tenantId: int) -> VersionTagSnapshot   sync
  Replace(snapshot: VersionTagSnapshot)   sync    // by the executor after every batch
enum VersionTagMismatchPolicy = Rerun | Refresh
record VersionTagDependency(Name: string, ExpectedTag: VersionTag, OnMismatch: VersionTagMismatchPolicy)
service IVersionTagRegistry                   // built at composition from the EF model's attributes plus the platform's fixed rules for the two non-entity preference tables; validated at the startup gate
  Names: list<string>
  Resolve(writtenTables: set<TableName>) -> VersionTagEffects   sync
record VersionTagEffects(TenantNames: list<string>, UserRules: list<UserVersionTagRule>)
record UserVersionTagRule(Table: TableName, Column: UserVersionTagNames, UserIdColumn: string)   // core.UserPreferences and core.NotificationPreferences -> Preferences, "UserId" are fixed rules

enum CacheOutcome = Hit | Miss | Stale | Oversized | Uncached
record CacheResult<TValue>(Value: TValue, Tag: VersionTag, Outcome: CacheOutcome)
data TellmaCacheOptions                       // Tellma:Cache; ValidateOnStart
  SettingsEntries: int = 10000   PreferencesEntries: int = 50000   PermissionsEntries: int = 50000
  EntityRows: long = 2000000   EntityMaxRowsDefault: int = 1000   EntityMaxRowsCeiling: int = 10000
  EntityListMaxAge: TimeSpan = 15 minutes   TagSnapshots: int = 10000
record CachedEntitySet<TEntity>(Tag: VersionTag, LoadedAt: DateTimeOffset, Items: list<TEntity>, ById: map<int, TEntity>)
service ICacheableEntities
  GetAsync<TEntity>() -> CacheResult<CachedEntitySet<TEntity>>
  Peek<TEntity>() -> CachedEntitySet<TEntity>?   sync
  GetWireTagsAsync() -> map<string, string>   // entity name -> wire tag (settings/entity-tags)

// Tellma.Core.Caching (runtime base)
base VersionedCache<TKey, TValue>             // private bounded MemoryCache per kind, single-flight loads, meters
  ctor(kind: string, sizeLimit: long, maxAge: TimeSpan?, meters: IMeterFactory)
  GetAsync(key: TKey, currentTag: VersionTag) -> CacheResult<TValue>
  Set(key: TKey, value: TValue, tag: VersionTag)   sync
  LoadAsync(key: TKey) -> (Value: TValue, Tag: VersionTag, Size: long)   // abstract; one batch; tag from its prelude
```

Cache kinds and their keys — every key begins with the tenant id, because user ids are per-tenant integers and one subject belongs to several tenants: `settings` keyed `(TenantId)` (`TenantSettings`), `preferences` keyed `(TenantId, UserId)` (`UserProfile`, owned by 0013), `permissions` keyed `(TenantId, UserId)` (`UserAccess`, which carries `TenantId`), `entities` keyed `(TenantId, EntityName)` (`CachedEntitySet<T>`); the connect cache (seam 16) is keyed `(TenantId, Subject)`. Two-level permissions validation: an entry is valid when both the tenant `permissions` tag and the user's `PermissionsTag` match. `[Cacheable]` implies `entity:<Name>` and `entities`. Fixture test: two tenant databases sharing user ids and subjects never see each other's entries. Tags are opaque Guids and are exposed as such in `Tellma-Version-Tags` and `me`; they double as a coarse activity signal (the tenant `permissions` tag changes whenever any role changes), which is accepted and must not be "improved" into a counter.

### 5.2 Tables

**`core.VersionTags`** — non-temporal; not an entity; absent from the Queryex schema.

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Name` | `nvarchar(128)` | no | PK clustered | `settings`, `permissions`, `entities`, `entity:<Name>`, pack names |
| `Tag` | `uniqueidentifier` | no | `DF_VersionTags_Tag NEWID()` | application-generated on bump |

**`core.UserStamps`** — non-temporal; no UDTT; exposed to Queryex read-only as `UserStamp` (navigation `User`).

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `UserId` | `int` | no | PK clustered; `FK_UserStamps_UserId → core.Users(Id) ON DELETE CASCADE` | one row per user, inserted with the user |
| `LastActiveAt` | `datetime2(3)` | yes | | throttled stamp |
| `PermissionsTag` | `uniqueidentifier` | no | `DF NEWID()` | bumped by writes to the user's `RoleMemberships` |
| `PreferencesTag` | `uniqueidentifier` | no | `DF NEWID()` | bumped by writes to the user's own `Users` row, `UserPreferences`, `NotificationPreferences` |
| `InboxSeenAt` | `datetime2(3)` | yes | | set by `inbox/seen` (0020) |

`HasData`: `(1, NULL, 00000000-0000-0000-0000-000000000001, 00000000-0000-0000-0000-000000000002, NULL)`.

### 5.3 Statements

```sql
-- Prelude (prologue contributor at Order 50; the first statement of every tenant batch; on caller batches the connect prologue reads it instead — seam 16):
SELECT [Name], [Tag] FROM [core].[VersionTags];

-- Guard (first statement after BEGIN TRAN in every Persist batch on a caller's behalf; @tm_expectedTags : VersionTagList always holds 'permissions' plus declared dependencies;
-- REPEATABLEREAD holds the shared locks to COMMIT, so a concurrent bump of these rows waits for this transaction instead of committing under it):
IF EXISTS (SELECT 1 FROM [core].[VersionTags] AS t WITH (REPEATABLEREAD, ROWLOCK) JOIN @tm_expectedTags AS e ON e.[Name] = t.[Name] WHERE t.[Tag] <> e.[Tag])
   OR EXISTS (SELECT 1 FROM [core].[UserStamps] AS s WITH (REPEATABLEREAD, ROWLOCK) WHERE s.[UserId] = @tm_UserId AND s.[PermissionsTag] <> @tm_ExpectedUserPermissionsTag)
    THROW 50412, N'StaleVersionTag', 1;

-- Bump (executor epilogue, last before COMMIT; @tm_tag one Guid per batch; @tm_tagNames : StringList = declared writes' names + BumpVersionTag(name) calls; 'entities' added when any 'entity:*' name is present):
UPDATE [core].[VersionTags] SET [Tag] = @tm_tag WHERE [Name] IN (SELECT [Id] FROM @tm_tagNames);
IF @@ROWCOUNT <> (SELECT COUNT(*) FROM @tm_tagNames) THROW 50422, N'VersionTag.Missing', 1;   -- a registered name absent from the table is never silent; metered tellma.versiontags.missing
-- Only when a user-level rule fired; one statement per column named. @tm_userIds_<Column> : a batch-local id table the epilogue fills by UNION from every source:
-- the emitter's rows (the rule's UserIdColumn read from the TVPs) and every raw statement's SqlOptions.UserIds.
UPDATE u SET u.[PermissionsTag] = @tm_tag FROM [core].[UserStamps] AS u JOIN @tm_userIds_Permissions AS i ON i.[Id] = u.[UserId];
UPDATE u SET u.[PreferencesTag] = @tm_tag FROM [core].[UserStamps] AS u JOIN @tm_userIds_Preferences AS i ON i.[Id] = u.[UserId];

-- Migrator, every run per tenant database (@names : StringList from IVersionTagRegistry.Names), then wholesale after migrations. This is the only seed of core.VersionTags:
-- no HasData rows and no provisioning step, because only the registry knows the names a deployment needs.
INSERT INTO [core].[VersionTags] ([Name], [Tag]) SELECT n.[Id], NEWID() FROM @names AS n
 WHERE NOT EXISTS (SELECT 1 FROM [core].[VersionTags] AS t WHERE t.[Name] = n.[Id]);
UPDATE [core].[VersionTags] SET [Tag] = NEWID();
```

Mismatch policies after a read batch: `permissions` and `PermissionsTag` → `Rerun` (once, then `StaleContextException`); `settings`, `PreferencesTag`, `entity:<Name>` → `Refresh` (served; cache reloads). Tables that never bump: `core.Jobs`, `core.UserStamps`, `core.VersionTags`, `core.Blobs`, `core.UserPreferences`, `core.NotificationPreferences`, `core.ScheduleStates`, `core.JobWorkerState`, `core.Notifications`. Response header `Tellma-Version-Tags: settings=<wire>, permissions=<wire>, preferences=<wire>, entities=<wire>`. Fixture tier: change tracking on every LocalDB table; the executor in test mode compares `CHANGETABLE(CHANGES …)` with the declared write set after every batch.

---

## 6. Seam 6 — Feature composition (owner 0010; consumers all)

```contract
// Tellma.Core.Abstractions.Composition
contract ITellmaFeature
  Name: string                                // stable, unique, lowercase dotted: "core", "gl", "acme"
  Declare(declaration: FeatureDeclaration)   sync
  Contribute(contribution: FeatureContribution)   sync
annotation [Requires<TFeature>]   on type     // sugar for FeatureDeclaration.Requires<TFeature>()
data FeatureDeclaration
  Requires<TFeature>() -> FeatureDeclaration   sync   where TFeature: ITellmaFeature
  Options<TOptions>(configurationPath: string) -> FeatureDeclaration   sync   // bound and validated at startup
  RequiredFeatures: list<Type>
base FeatureContributionItem                  // every item is a record; Core realises each through IContributionRealizer<TItem>
enum ServiceLifetimeKind = Singleton | Scoped | Transient
record ServiceContributionItem(ServiceType: Type, ImplementationType: Type?, Factory: ((IServiceProvider) -> object)?, Lifetime: ServiceLifetimeKind) : FeatureContributionItem
record ModelContributionItem(ContributorType: Type) : FeatureContributionItem      // an EF IEntityTypeConfiguration<> by type only
record SecurablesContributionItem(Configure: (SecurableRegistryBuilder) -> void) : FeatureContributionItem
record SettingKeysContributionItem(DeclaringType: Type) : FeatureContributionItem
record CalendarContributionItem(CalendarType: Type) : FeatureContributionItem
record ProvisioningStepContributionItem(StepType: Type) : FeatureContributionItem
record JobHandlerContributionItem(HandlerType: Type) : FeatureContributionItem
record BuiltInScheduleContributionItem(HandlerKey: string, Cron: string, Arguments: object?, TimeZoneId: string?) : FeatureContributionItem
record NotificationTypeContributionItem(Descriptor: NotificationTypeDescriptor) : FeatureContributionItem
record ClientEventContributionItem(Name: string) : FeatureContributionItem
record BlobKindContributionItem(Kind: string, Policy: BlobKindPolicy) : FeatureContributionItem
record ValidatorContributionItem(EntityType: Type, ComponentType: Type, Kind: ComponentKind) : FeatureContributionItem
enum ComponentKind = Validator | SaveEffect | DetailsContributor
data FeatureContribution
  Add(item: FeatureContributionItem) -> FeatureContribution   sync
  Feature(feature: ITellmaFeature) -> FeatureContribution   sync           // nested feature; validated with the rest
  Singleton<TService, TImplementation>() / Scoped<,>() / Transient<,>() -> FeatureContribution   sync
  Model<TContributor>() -> FeatureContribution   sync
  Entity<TEntity>() -> FeatureContribution   sync                         // StackContributionItem with the default EntityService<TEntity>
  Entity<TEntity, TService>() -> FeatureContribution   sync
  ApiService<TService>() -> FeatureContribution   sync                    // an [ApiRoute] service; ApiServiceContributionItem
  Validator<TEntity, TValidator>() -> FeatureContribution   sync   where TValidator: IEntityValidator<TEntity>
  SaveEffect<TEntity, TEffect>() -> FeatureContribution   sync   where TEffect: ISaveEffect<TEntity>
  DetailsContributor<TEntity, TContributor>() -> FeatureContribution   sync   where TContributor: IDetailsContributor<TEntity>
  Securables(configure: (SecurableRegistryBuilder) -> void) -> FeatureContribution   sync
  SettingKeys(declaringType: Type) -> FeatureContribution   sync
  Calendar<TCalendar>() -> FeatureContribution   sync   where TCalendar: ICalendarSystem
  ProvisioningStep<TStep>() -> FeatureContribution   sync   where TStep: ITenantProvisioningStep
  JobHandler<THandler>() -> FeatureContribution   sync
  BuiltInSchedule(handlerKey: string, cron: string, arguments: object? = null, timeZoneId: string? = null) -> FeatureContribution   sync
  NotificationType(descriptor: NotificationTypeDescriptor) -> FeatureContribution   sync
  ClientEvent(name: string) -> FeatureContribution   sync
  BlobKind(kind: string, policy: BlobKindPolicy) -> FeatureContribution   sync
contract IStartupCheck                        // registered by any package; run by the realised gate
  Name: string
  CheckAsync(services: IServiceProvider) -> list<CompositionProblem>
record CompositionProblem(Source: string, Problem: string, Fix: string?)
record TellmaCompositionException(Problems: list<CompositionProblem>)   // exception; one message line per problem

// Tellma.Core.Composition (runtime)
service TellmaServiceCollectionExtensions
  AddTellma(services: IServiceCollection, slug: string, configuration: IConfiguration, environment: IHostEnvironment, compose: (TellmaBuilder) -> void) -> IServiceCollection   sync
data TellmaBuilder
  Services: IServiceCollection   Configuration: IConfiguration   Environment: IHostEnvironment
  AddFeature<TFeature>() -> TellmaBuilder   sync   where TFeature: ITellmaFeature, new()   // idempotent; CoreFeature is added unconditionally
  AddFeature(feature: ITellmaFeature) -> TellmaBuilder   sync
  UseDbContext<TContext>() -> TellmaBuilder   sync   where TContext: TellmaDbContext
  UseEntity<TDefault, TLeaf>() -> TellmaBuilder   sync   where TLeaf: TDefault      // leaf substitution; the stack, service and securables follow
  Languages(codes: list<string>) -> TellmaBuilder   sync
  AddLanguage(info: LanguageInfo) -> TellmaBuilder   sync
  AddMcp(configure: ((TellmaMcpOptions) -> void)? = null) -> TellmaBuilder   sync   // Tellma.Core.Mcp extension; a feature requiring the stack feature
  Blobs(configure: (BlobsBuilder) -> void) -> TellmaBuilder   sync
contract IContributionRealizer<TItem>   where TItem: FeatureContributionItem
  Realize(item: TItem, owner: ITellmaFeature, context: RealizationContext)   sync
data RealizationContext                       Services: IServiceCollection   Problems: list<CompositionProblem>
service TellmaComposition
  Validate(slug: string, configuration: IConfiguration, compose: (TellmaBuilder) -> void) -> list<CompositionProblem>   sync   // host-free
```

Two aggregated gates: the composition gate (declare → `Requires` closure → contribute → realise; every problem collected) and the realised gate (`IStartupCheck`s: securables registration audit, natural-key index backing, version-tag registry, language catalogue, calendar registry, blob kinds, job handler keys, the endpoint audit over `EndpointDataSource`). `GlFeature` declares `[Requires<CoreFeature>]`. Store registration stays on `Services` (`AddFileSystemBlobStore`, `AddAzureBlobStore`).

Illustration (a complete distribution composition):

```csharp
builder.AddTellma("acme", t => t.AddFeature<GlFeature>().UseEntity<Center, MyCenter>().Languages(["en", "ar"]).AddMcp());
```

---

## 7. Seam 7 — Natural keys (owner 0011; consumer 0018)

`[NaturalKey(Order)]` declares (single column; `(ParentKey, Property)` uniqueness on children); without it, inference over single-column unique-index-backed properties in this order: the `[Multilingual]` `Name` group, `Code`, required unique strings in declaration order, nullable unique strings. `EntityMetadata.NaturalKeys` is the ordered list (declared by `Order`, then inferred); `NaturalKey` is its first; none → surrogate-id export flagged `KeyKind = Surrogate` in the manifest and refused across tenants. The realised gate fails when a declared key lacks a unique index and warns for a referenced entity without any key. Core declarations: `User.Email`, `Role.Code`, `Center.Code`.

---

## 8. Seam 8 — Background jobs and lease statements (owner 0019; 0011 emits nothing job-specific beyond `IJobEntity`)

### 8.1 Contracts

```contract
// Tellma.Core.Abstractions.Jobs
enum JobStatus = Pending | Running | Succeeded | Failed | Cancelled | Held
data Job : Entity<int>                        // system-written; table core.Jobs
  HandlerKey: string   Status: JobStatus   DueAt: datetime2(3)   Attempts: int
  LeaseToken: Guid?   LeaseExpiresAt: datetime2(3)?   LeaseOwner: string?
  StartedAt: datetime2(3)?   CompletedAt: datetime2(3)?   CancelRequestedAt: datetime2(3)?
  ArgumentsJson: string?   StateJson: string?   ProgressPercent: byte?   ProgressMessage: string?
  ErrorCode: string?   ErrorMessage: string?   TraceParent: string?
  RunAsUserId: int   RequestedById: int?   ScheduleId: int?   ScheduledFor: datetime2(3)?   CreatedAt: datetime2(3)
  // ErrorDetails (stack traces) is a column but not a member of the Queryex entity or the wire shape; JobService.ErrorDetails exposes it under core.Job × Diagnose
annotation [JobHandler(Key: string, BatchSize = 1, LeaseSeconds = 300, MaxAttempts = 5, MaxConcurrency = 1, RetryBaseSeconds = 30, RetryMaxSeconds = 3600, Schedulable = false)]   on type
contract IJobHandler                          // arguments-only handlers; resolved from the job scope
  Execute(batch: JobBatch<Job>)               // the operation's token is the batch token
contract IEntityJobHandler<TEntity>   where TEntity: IJobEntity   // entity-backed handlers; the claim appends the entity join load
  Execute(batch: JobBatch<TEntity>)
record JobBatch<TItem>
  Items: list<JobItem<TItem>>
  Single: JobItem<TItem>                      // throws when Items has more than one
  Batch: IDataBatch                           // statements appended here run in the completion transaction
  RunAsUserId: int
  CancellationReason: JobCancellationReason
record JobItem<TItem>
  Job: Job   Item: TItem   CancellationToken   Progress: IJobProgress
  Arguments<TArgs>() -> TArgs   sync          // tolerant JSON
  State<TState>() -> TState?   sync
  Succeed()   sync
  Retry(after: TimeSpan? = null, error: JobError? = null)   sync
  Fail(error: JobError)   sync
  NotifyOnSuccess(request: NotificationRequest)   sync
service IJobProgress
  Report(percent: byte?, message: string?, state: object?)   sync   // buffered; rides the next renewal
  Flush()                                                            // writes now
  Append(batch: IDataBatch, percent: byte?, message: string?, state: object?)   sync   // fenced checkpoint inside the caller's transaction
record JobError(Code: string, Message: string, Details: string?)
enum JobCancellationReason = None | LeaseLost | CancelRequested | HostStopping
record JobFailedException(Error: JobError)    // exception; fails every unmarked item permanently
record JobRequest(HandlerKey: string, Arguments: object?, DueAt: DateTimeOffset?, RequestedById: int?, RunAsUserId: int?, Entity: IJobEntity?)
  // Entity: the owning IJobEntity row already in the batch (or in the table); the enqueue statement sets its JobId column and the in-memory member after execution
service IJobQueue
  Enqueue(batch: IDataBatch, requests: list<JobRequest>) -> BatchResult<list<int>>   sync   // ids assigned inside the statement (sp_sequence_get_range); insert appended; nudge via OnCommitted
  EnqueueAsync(requests: list<JobRequest>) -> list<int>                                    // own batch, own round trip
data JobsOptions                              // Tellma:Jobs
  Enabled: bool = true   MaxParallelBatches: int = 8   MinPollInterval: TimeSpan = 1s   MaxPollInterval: TimeSpan = 30s
  DrainTimeout: TimeSpan = 4s   GapThreshold: TimeSpan = 6h   SucceededRetention: TimeSpan = 30d   FailedRetention: TimeSpan = 180d
enum MissedPolicy = Coalesce | ReplayAll | Skip
enum OverlapPolicy = Skip | Allow
enum SchedulePausedReason = OwnerInactive | Exhausted
data Schedule : TopLevelEntity, IActivatable  // [Temporal]; [Multilingual] Name; stack resource core.Schedule
  Code: string?                               [Unique]; equals HandlerKey on built-ins
  Name: string   Name2: string?   Name3: string?
  HandlerKey: string                          [WriteOnce]
  ArgumentsJson: string?                      [JsonColumn]
  CronExpression: string                      // five-field Unix cron, Cronos dialect
  TimeZoneId: string?                         // null = tenant zone at fire time
  MissedPolicy: MissedPolicy = Coalesce
  OverlapPolicy: OverlapPolicy = Skip
  CatchUpWindowMinutes: int = 1440
  RunAsUserId: int                            [ServerOwned]; the creator, re-stamped only by the take-over action; the system user on built-ins
  IsBuiltIn: bool                             [ServerOwned]
  PausedReason: SchedulePausedReason?         [ServerOwned]
  IsActive: bool = true
data JobsTelemetryNames                       // constants; MeterName = "Tellma.Core"; ActivitySourceName = "Tellma.Jobs"
  Claimed = "tellma.jobs.claimed"; Completed = "tellma.jobs.completed"; Duration = "tellma.jobs.duration"; QueueLatency = "tellma.jobs.queue.latency"
  BatchFill = "tellma.jobs.batch_fill"; InFlight = "tellma.jobs.in_flight"; LeaseLost = "tellma.jobs.lease_lost"; BacklogAge = "tellma.jobs.backlog.age"
  PollDuration = "tellma.jobs.poll.duration"; Orphaned = "tellma.jobs.orphaned"
  SchedulesFired = "tellma.schedules.fired"; SchedulesMissed = "tellma.schedules.missed"; SchedulesOverlapSkipped = "tellma.schedules.overlap_skipped"; SchedulesGapDetected = "tellma.schedules.gap_detected"
  HandlerTag = "handler"; OutcomeTag = "outcome"; PolicyTag = "policy"

// Tellma.Core.Jobs (runtime stacks; resources core.Job and core.Schedule)
service JobService : EntityService<Job>       // Operations = Query | Details
  Retry(context)                              // [EntityAction] "retry", permission Retry
  Cancel(context)                             // [EntityAction] "cancel", permission Cancel, destructive
  Resume(context)                             // [EntityAction] "resume", permission Resume
  ErrorDetails(request: IdsRequest) -> map<int, string?>   // [ApiAction] "error-details", action Diagnose; the only reader of Jobs.ErrorDetails
service ScheduleService : EntityService<Schedule>   // full stack; ContributeAsync recomputes NextDueAt in core.ScheduleStates
  TakeOver(context)                           // [EntityAction] "take-over", permission Save: RunAsUserId := the caller; the explicit way to run another user's schedule as oneself
```

Schedule rules (`ScheduleService` validators): `HandlerKey` must name a registered handler with `Schedulable = true` (`Schedules.HandlerNotSchedulable`; Core marks only `core.export` schedulable in this release; built-ins are exempt); a save that changes `HandlerKey`, `ArgumentsJson`, `CronExpression` or `TimeZoneId` on a schedule whose `RunAsUserId` is not the caller is `Schedules.OwnedByAnotherUser` — the caller takes it over first, which re-stamps `RunAsUserId` visibly, or leaves it; renaming and (de)activating do not change the owner. Built-ins (`IsBuiltIn = 1`): only `Name*`, `CronExpression` and `TimeZoneId` are editable; `IsActive`, `MissedPolicy`, `OverlapPolicy`, `ArgumentsJson`, `HandlerKey`, `Code` are immutable (`Schedules.BuiltInImmutable`), delete is refused, and `deactivate` is `Schedules.BuiltInAlwaysActive` — sweeps and retention cannot be switched off from the tenant.

Handler keys (grammar `^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$`): `core.export` (schedulable), `core.import` (`MaxAttempts = 1`), `core.blob-sweep` (every 15 min), `core.blob-reconcile` (weekly), `core.job-retention` (`0 3 * * *`), `core.notification-retention` (`30 3 * * *`), `core.file-retention` (`0 4 * * *`), `core.tree-verify` (weekly; the whole-table recount of §1.4 per tree table). Jobs and schedules are tenant-scoped only in this release: nothing deployable rides `core.Jobs`; the catalog session sweep is a hosted timer in `Tellma.Core.AspNetCore` (seam 13.2). Built-in schedules are `HasData` rows in the reserved band with matching `core.ScheduleStates` rows. Workers poll only `Active` tenants (`ITenantStateListener`). Job scopes: `ITenantScopeFactory.CreateScopeAsync(snapshot)` with `(tenantId, RunAsUserId)`; locale from the run-as user's preferences or the tenant defaults; the connect is `IUserConnector.ConnectAsUser` (no activity stamp); an inactive run-as user completes the job `Failed` with `ErrorCode = 'user_inactive'`. Securables: `core.Job` × `Read | Retry | Cancel | Resume | Diagnose` (self-scope criterion `RequestedById = me()` for `Read` only; `Diagnose` is never bespoke), `core.Schedule` × `Read | Save | Delete | Activate`.

### 8.2 Tables

**`core.Jobs`** — non-temporal; `LOCK_ESCALATION = DISABLE`; `[TableType]` (all columns); sequence `core.sq_Jobs`.

| Column | Type | Null | Constraints |
|---|---|---|---|
| `Id` | `int` | no | PK clustered |
| `HandlerKey` | `nvarchar(64)` | no | |
| `Status` | `varchar(9)` | no | |
| `DueAt` | `datetime2(3)` | no | |
| `Attempts` | `int` | no | `DF 0` |
| `LeaseToken` | `uniqueidentifier` | yes | |
| `LeaseExpiresAt` | `datetime2(3)` | yes | |
| `LeaseOwner` | `nvarchar(128)` | yes | |
| `StartedAt`, `CompletedAt`, `CancelRequestedAt` | `datetime2(3)` | yes | |
| `ArgumentsJson`, `StateJson`, `ErrorDetails` | `nvarchar(max)` | yes | arguments ≤ 64 KB at enqueue |
| `ProgressPercent` | `tinyint` | yes | `CK_Jobs_Progress (ProgressPercent <= 100)` |
| `ProgressMessage` | `nvarchar(256)` | yes | |
| `ErrorCode` | `nvarchar(64)` | yes | `user_inactive`, `attempts_exhausted`, `entity_missing`, `held_after_gap`, handler codes |
| `ErrorMessage` | `nvarchar(1024)` | yes | |
| `TraceParent` | `nvarchar(55)` | yes | |
| `RunAsUserId` | `int` | no | `FK_Jobs_RunAsUserId → core.Users` |
| `RequestedById` | `int` | yes | `FK_Jobs_RequestedById → core.Users` |
| `ScheduleId` | `int` | yes | `FK_Jobs_ScheduleId → core.Schedules ON DELETE SET NULL` |
| `ScheduledFor` | `datetime2(3)` | yes | |
| `CreatedAt` | `datetime2(3)` | no | |

`CK_Jobs_Lease`: `(Status = 'Running' AND LeaseToken IS NOT NULL AND LeaseExpiresAt IS NOT NULL) OR (Status <> 'Running' AND LeaseToken IS NULL AND LeaseExpiresAt IS NULL)`. Indexes: `IX_Jobs_Available (HandlerKey, DueAt, Id) INCLUDE (LeaseExpiresAt) WHERE Status IN ('Pending', 'Running')`; `IX_Jobs_Lease (LeaseToken) INCLUDE (Status) WHERE LeaseToken IS NOT NULL`; `IX_Jobs_ScheduleActive (ScheduleId) WHERE Status IN ('Pending', 'Running') AND ScheduleId IS NOT NULL`; `IX_Jobs_RequestedBy (RequestedById, CreatedAt DESC) WHERE RequestedById IS NOT NULL`; `IX_Jobs_Retention (Status, CompletedAt) WHERE Status IN ('Succeeded', 'Failed', 'Cancelled')`; `IX_Jobs_Held (Id) WHERE Status = 'Held'`.

**`core.Schedules`** — carries `TopLevelEntity`, `IActivatable`, `[Temporal]` (`core.SchedulesHistory`), `[Multilingual] Name`; UDTT; sequence `core.sq_Schedules`: `Id int PK`; `Code nvarchar(64) NULL UX_Schedules_Code WHERE Code IS NOT NULL`; `Name nvarchar(256) NOT NULL`, `Name2`/`Name3 nvarchar(256) NULL`; `HandlerKey nvarchar(64) NOT NULL`, `IX_Schedules_HandlerKey`; `ArgumentsJson nvarchar(max) NULL`; `CronExpression nvarchar(128) NOT NULL`; `TimeZoneId varchar(64) NULL`; `MissedPolicy varchar(9) NOT NULL DF 'Coalesce'`; `OverlapPolicy varchar(8) NOT NULL DF 'Skip'`; `CatchUpWindowMinutes int NOT NULL DF 1440 CK_Schedules_CatchUp (> 0)`; `RunAsUserId int NOT NULL FK core.Users`; `IsBuiltIn bit NOT NULL DF 0`; `PausedReason varchar(13) NULL`; `IsActive bit NOT NULL DF 1`; audit set; period columns.

**`core.ScheduleStates`** — non-temporal sibling, one row per schedule, inserted with it: `ScheduleId int PK FK_ScheduleStates_ScheduleId → core.Schedules ON DELETE CASCADE`; `NextDueAt datetime2(3) NULL` (null while inactive or paused); `LastFiredAt`, `LastScheduledFor`, `LastSkippedAt datetime2(3) NULL`; `LastJobId int NULL FK → core.Jobs ON DELETE SET NULL`; `LeaseToken uniqueidentifier NULL`; `LeaseExpiresAt datetime2(3) NULL`; `IX_ScheduleStates_Due (NextDueAt) WHERE NextDueAt IS NOT NULL`.

**`core.JobWorkerState`** — single row: `Id int PK CK (Id = 1)`; `LastTickAt datetime2(3) NULL`; `LastTickOwner nvarchar(128) NULL`; seeded `(1, NULL, NULL)`.

### 8.3 The job statements (fixed text owned by 0019; executed through `IDataBatch.Sql` with declared writes; claim/tick/heartbeat at `TransactionMode.None`, `Idempotent = false`; completion and fire under `Auto`)

```sql
-- CLAIM (one per handler key with free slots; ordinal b; @tb{b}_p0 key, @tb{b}_p1 n ≤ 500, @tb{b}_p2 token, @tb{b}_p3 leaseSeconds, @tb{b}_p4 owner, @tb{b}_p5 MaxAttempts)
DECLARE @tb{b}_now datetime2(3) = SYSUTCDATETIME();
DECLARE @tb{b}_claimed TABLE ([Id] int NOT NULL PRIMARY KEY);
-- exhausted rows are failed set-based, never claimed:
UPDATE [j] SET [Status] = 'Failed', [LeaseToken] = NULL, [LeaseExpiresAt] = NULL, [LeaseOwner] = NULL, [CompletedAt] = @tb{b}_now,
    [ErrorCode] = COALESCE([j].[ErrorCode], N'attempts_exhausted')
FROM [core].[Jobs] AS [j] WITH (READPAST, UPDLOCK, ROWLOCK)
WHERE [j].[Status] IN ('Pending', 'Running') AND [j].[HandlerKey] = @tb{b}_p0 AND [j].[Attempts] >= @tb{b}_p5
  AND ([j].[LeaseExpiresAt] IS NULL OR [j].[LeaseExpiresAt] < @tb{b}_now);
WITH [due] AS (
    SELECT TOP (@tb{b}_p1) [j].*
    FROM [core].[Jobs] AS [j] WITH (READPAST, UPDLOCK, ROWLOCK)
    WHERE [j].[Status] IN ('Pending', 'Running')
      AND [j].[HandlerKey] = @tb{b}_p0
      AND [j].[DueAt] <= @tb{b}_now
      AND [j].[Attempts] < @tb{b}_p5
      AND ([j].[LeaseExpiresAt] IS NULL OR [j].[LeaseExpiresAt] < @tb{b}_now)
    ORDER BY [j].[DueAt], [j].[Id])
UPDATE [due]
SET [Status] = 'Running', [LeaseToken] = @tb{b}_p2, [LeaseExpiresAt] = DATEADD(second, @tb{b}_p3, @tb{b}_now),
    [LeaseOwner] = @tb{b}_p4, [Attempts] = [Attempts] + 1, [StartedAt] = COALESCE([StartedAt], @tb{b}_now)
OUTPUT [inserted].[Id] INTO @tb{b}_claimed;
SELECT [j].* FROM [core].[Jobs] AS [j] INNER JOIN @tb{b}_claimed AS [c] ON [c].[Id] = [j].[Id] ORDER BY [j].[DueAt], [j].[Id];
-- entity-backed handlers: the platform appends Query<TEntity> with Restrictions = [KeySetRestriction("JobId", "@tb{b}_claimed")]

-- RENEW (one per tenant per interval = min(LeaseSeconds)/5; @tb{b}_t0 : JobLeaseList, @tb{b}_t1 : JobProgressList)
DECLARE @tb{b}_now datetime2(3) = SYSUTCDATETIME();
UPDATE [j]
SET [LeaseExpiresAt] = DATEADD(second, [t].[LeaseSeconds], @tb{b}_now),
    [ProgressPercent] = COALESCE([p].[ProgressPercent], [j].[ProgressPercent]),
    [ProgressMessage] = COALESCE([p].[ProgressMessage], [j].[ProgressMessage]),
    [StateJson] = COALESCE([p].[StateJson], [j].[StateJson])
OUTPUT [inserted].[Id], [inserted].[LeaseToken], [inserted].[CancelRequestedAt]
FROM [core].[Jobs] AS [j]
INNER JOIN @tb{b}_t0 AS [t] ON [t].[Id] = [j].[LeaseToken]
LEFT JOIN @tb{b}_t1 AS [p] ON [p].[Id] = [j].[Id]
WHERE [j].[Status] = 'Running';

-- APPEND (IJobProgress.Append inside the caller's transaction; fenced by the token; @tb{b}_p0 token, @tb{b}_t0 : JobProgressList)
UPDATE [j] SET [ProgressPercent] = COALESCE([p].[ProgressPercent], [j].[ProgressPercent]),
    [ProgressMessage] = COALESCE([p].[ProgressMessage], [j].[ProgressMessage]), [StateJson] = COALESCE([p].[StateJson], [j].[StateJson])
FROM [core].[Jobs] AS [j] INNER JOIN @tb{b}_t0 AS [p] ON [p].[Id] = [j].[Id]
WHERE [j].[LeaseToken] = @tb{b}_p0 AND [j].[Status] = 'Running';
IF @@ROWCOUNT <> (SELECT COUNT(*) FROM @tb{b}_t0) THROW 50422, N'Job.LeaseLost', 1;

-- COMPLETE (one per batch, one READ COMMITTED transaction; the FIRST statement after BEGIN TRAN, before the handler's statements and the worker's notifications,
-- so that nothing the handler wrote survives a lost lease; @tb{b}_p0 token, @tb{b}_t0 : JobOutcomeList)
DECLARE @tb{b}_now datetime2(3) = SYSUTCDATETIME();
UPDATE [j]
SET [Status] = [o].[Status],
    [DueAt] = CASE WHEN [o].[RetryAfterSeconds] IS NULL THEN [j].[DueAt] ELSE DATEADD(second, [o].[RetryAfterSeconds], @tb{b}_now) END,
    [Attempts] = CASE WHEN [o].[Released] = 1 THEN [j].[Attempts] - 1 ELSE [j].[Attempts] END,
    [LeaseToken] = NULL, [LeaseExpiresAt] = NULL, [LeaseOwner] = NULL,
    [CompletedAt] = CASE WHEN [o].[Status] IN ('Succeeded', 'Failed', 'Cancelled') THEN @tb{b}_now ELSE NULL END,
    [ErrorCode] = [o].[ErrorCode], [ErrorMessage] = [o].[ErrorMessage], [ErrorDetails] = [o].[ErrorDetails],
    [StateJson] = COALESCE([o].[StateJson], [j].[StateJson]),
    [ProgressPercent] = CASE WHEN [o].[Status] = 'Succeeded' THEN 100 ELSE [j].[ProgressPercent] END
OUTPUT [inserted].[Id], [inserted].[Status], [inserted].[RequestedById], [inserted].[Attempts]
FROM [core].[Jobs] AS [j] INNER JOIN @tb{b}_t0 AS [o] ON [o].[Id] = [j].[Id]
WHERE [j].[LeaseToken] = @tb{b}_p0 AND [j].[Status] = 'Running';
IF @@ROWCOUNT <> (SELECT COUNT(*) FROM @tb{b}_t0) THROW 50422, N'Job.LeaseLost', 1;   -- the lease fence: XACT_ABORT rolls back the whole completion; the worker maps it to JobCancellationReason.LeaseLost and meters tellma.jobs.lease_lost

-- HEARTBEAT AND GAP-HOLD (first block of every poll; @tb{b}_p0 gapMinutes, @tb{b}_p1 owner)
DECLARE @tb{b}_now datetime2(3) = SYSUTCDATETIME();
DECLARE @tb{b}_last datetime2(3) = (SELECT [LastTickAt] FROM [core].[JobWorkerState] WHERE [Id] = 1);
IF @tb{b}_last IS NOT NULL AND @tb{b}_last < DATEADD(minute, -@tb{b}_p0, @tb{b}_now)
    UPDATE [j]
    SET [Status] = 'Held', [LeaseToken] = NULL, [LeaseExpiresAt] = NULL, [LeaseOwner] = NULL, [ErrorCode] = N'held_after_gap'
    FROM [core].[Jobs] AS [j]
    WHERE [j].[Status] IN ('Pending', 'Running')
      AND [j].[DueAt] < DATEADD(minute, -@tb{b}_p0, @tb{b}_now)
      AND ([j].[LeaseExpiresAt] IS NULL OR [j].[LeaseExpiresAt] < @tb{b}_now);
UPDATE [core].[JobWorkerState] SET [LastTickAt] = @tb{b}_now, [LastTickOwner] = @tb{b}_p1
WHERE [Id] = 1 AND ([LastTickAt] IS NULL OR [LastTickAt] < DATEADD(second, -60, @tb{b}_now));
SELECT @tb{b}_last AS [PreviousTickAt], @tb{b}_now AS [Now];

-- SCHEDULE TICK, step 1 (in the poll; @tb{b}_p0 tick token)
DECLARE @tb{b}_now datetime2(3) = SYSUTCDATETIME();
DECLARE @tb{b}_due TABLE ([ScheduleId] int NOT NULL PRIMARY KEY);
WITH [d] AS (
    SELECT TOP (50) [s].*
    FROM [core].[ScheduleStates] AS [s] WITH (READPAST, UPDLOCK, ROWLOCK)
    WHERE [s].[NextDueAt] IS NOT NULL AND [s].[NextDueAt] <= @tb{b}_now
      AND ([s].[LeaseExpiresAt] IS NULL OR [s].[LeaseExpiresAt] < @tb{b}_now)
    ORDER BY [s].[NextDueAt])
UPDATE [d] SET [LeaseToken] = @tb{b}_p0, [LeaseExpiresAt] = DATEADD(second, 30, @tb{b}_now)
OUTPUT [inserted].[ScheduleId] INTO @tb{b}_due;
SELECT [s].[Id], [s].[HandlerKey], [s].[ArgumentsJson], [s].[CronExpression], [s].[TimeZoneId], [s].[MissedPolicy],
       [s].[OverlapPolicy], [s].[CatchUpWindowMinutes], [s].[RunAsUserId], [s].[IsBuiltIn],
       [st].[NextDueAt], [st].[LastScheduledFor], [u].[IsActive] AS [OwnerIsActive], @tb{b}_now AS [Now]
FROM @tb{b}_due AS [d]
INNER JOIN [core].[Schedules] AS [s] ON [s].[Id] = [d].[ScheduleId]
INNER JOIN [core].[ScheduleStates] AS [st] ON [st].[ScheduleId] = [d].[ScheduleId]
INNER JOIN [core].[Users] AS [u] ON [u].[Id] = [s].[RunAsUserId];

-- SCHEDULE TICK, step 2 (second round trip, one transaction; @tb{b}_t0 : Jobs UDTT with ids reserved in this statement, @tb{b}_t1 : ScheduleNextList, @tb{b}_p0 tick token)
DECLARE @tb{b}_now datetime2(3) = SYSUTCDATETIME();
DECLARE @tb{b}_fired TABLE ([ScheduleId] int NOT NULL, [JobId] int NOT NULL);
-- tick-lease fence: every schedule in this step must still be leased by this ticker, else the whole step rolls back
IF (SELECT COUNT(*) FROM [core].[ScheduleStates] AS [st] WITH (UPDLOCK, ROWLOCK) INNER JOIN @tb{b}_t1 AS [n] ON [n].[ScheduleId] = [st].[ScheduleId]
    WHERE [st].[LeaseToken] = @tb{b}_p0 AND [st].[LeaseExpiresAt] >= @tb{b}_now) <> (SELECT COUNT(*) FROM @tb{b}_t1)
    THROW 50422, N'Schedule.TickLeaseLost', 1;
DECLARE @tb{b}_first sql_variant, @tb{b}_n int = (SELECT COUNT(*) FROM @tb{b}_t0);
IF @tb{b}_n > 0 EXEC sys.sp_sequence_get_range @sequence_name = N'core.sq_Jobs', @range_size = @tb{b}_n, @range_first_value = @tb{b}_first OUTPUT;
INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [Attempts], [ArgumentsJson], [RunAsUserId], [RequestedById], [ScheduleId], [ScheduledFor], [CreatedAt])
OUTPUT [inserted].[ScheduleId], [inserted].[Id] INTO @tb{b}_fired
SELECT CAST(@tb{b}_first AS int) + ROW_NUMBER() OVER (ORDER BY [r].[ScheduleId], [r].[ScheduledFor]) - 1,
       [r].[HandlerKey], 'Pending', [r].[DueAt], 0, [r].[ArgumentsJson], [r].[RunAsUserId], [r].[RequestedById], [r].[ScheduleId], [r].[ScheduledFor], @tb{b}_now
FROM @tb{b}_t0 AS [r]
INNER JOIN [core].[Schedules] AS [s] ON [s].[Id] = [r].[ScheduleId]
INNER JOIN [core].[ScheduleStates] AS [st] ON [st].[ScheduleId] = [r].[ScheduleId] AND [st].[LeaseToken] = @tb{b}_p0 AND [st].[LeaseExpiresAt] >= @tb{b}_now
WHERE [s].[OverlapPolicy] = 'Allow'
   OR NOT EXISTS (SELECT 1 FROM [core].[Jobs] AS [x] WHERE [x].[ScheduleId] = [r].[ScheduleId] AND [x].[Status] IN ('Pending', 'Running'));
UPDATE [st]
SET [NextDueAt] = [n].[NextDueAt],
    [LastScheduledFor] = COALESCE([n].[LastScheduledFor], [st].[LastScheduledFor]),
    [LastFiredAt] = CASE WHEN [f].[ScheduleId] IS NOT NULL THEN @tb{b}_now ELSE [st].[LastFiredAt] END,
    [LastJobId] = COALESCE([f].[JobId], [st].[LastJobId]),
    [LastSkippedAt] = CASE WHEN [n].[Due] = 1 AND [f].[ScheduleId] IS NULL THEN @tb{b}_now ELSE [st].[LastSkippedAt] END,
    [LeaseToken] = NULL, [LeaseExpiresAt] = NULL
FROM [core].[ScheduleStates] AS [st]
INNER JOIN @tb{b}_t1 AS [n] ON [n].[ScheduleId] = [st].[ScheduleId]
LEFT JOIN (SELECT [ScheduleId], MAX([JobId]) AS [JobId] FROM @tb{b}_fired GROUP BY [ScheduleId]) AS [f] ON [f].[ScheduleId] = [st].[ScheduleId]
WHERE [st].[LeaseToken] = @tb{b}_p0;

-- ENQUEUE (IJobQueue.Enqueue; @tb{b}_t0 : JobRequestList; ids reserved here, never from the allocator's buffer, so Notify/Enqueue stay synchronous; declares writes core.Jobs
-- plus each owner table named by JobRequest.Entity)
DECLARE @tb{b}_first sql_variant, @tb{b}_n int = (SELECT COUNT(*) FROM @tb{b}_t0);
DECLARE @tb{b}_jobIds TABLE ([Ordinal] int NOT NULL PRIMARY KEY, [JobId] int NOT NULL);
IF @tb{b}_n > 0 EXEC sys.sp_sequence_get_range @sequence_name = N'core.sq_Jobs', @range_size = @tb{b}_n, @range_first_value = @tb{b}_first OUTPUT;
INSERT INTO @tb{b}_jobIds ([Ordinal], [JobId]) SELECT [r].[Ordinal], CAST(@tb{b}_first AS int) + [r].[Ordinal] FROM @tb{b}_t0 AS [r];   -- Ordinal is 0-based
INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [Attempts], [ArgumentsJson], [TraceParent], [RunAsUserId], [RequestedById], [CreatedAt])
SELECT [i].[JobId], [r].[HandlerKey], 'Pending', COALESCE([r].[DueAt], SYSUTCDATETIME()), 0, [r].[ArgumentsJson], [r].[TraceParent], [r].[RunAsUserId], [r].[RequestedById], SYSUTCDATETIME()
FROM @tb{b}_t0 AS [r] INNER JOIN @tb{b}_jobIds AS [i] ON [i].[Ordinal] = [r].[Ordinal];
-- per JobRequest.Entity (owner table T, owner id @tb{b}_p{k}, ordinal o): the owner's JobId column, inside the same transaction
UPDATE [core].[Exports] SET [JobId] = [i].[JobId] FROM @tb{b}_jobIds AS [i] WHERE [i].[Ordinal] = o AND [core].[Exports].[Id] = @tb{b}_p{k};
SELECT [Ordinal], [JobId] FROM @tb{b}_jobIds ORDER BY [Ordinal];   -- result set: the BatchResult<list<int>> value
```

Admin actions (`[EntityAction]`s on `JobService`): `retry` (`Failed → Pending`, `Attempts = 0`, `DueAt = now`, error cleared), `cancel` (`Pending | Held → Cancelled`; `Running` → stamps `CancelRequestedAt`), `resume` (`Held → Pending`, `DueAt = now`, error cleared); each through `IDataBatch.Update` with `Stamp = false`.

---

## 9. Seam 9 — Request context and tenancy (owner 0010; consumers all)

```contract
// Tellma.Core.Abstractions.Tenancy
enum TenantCategory = Live | Sandbox
enum TenantState = Provisioning | Active | ReadOnly | Suspended | Retired
enum PrincipalKind = Anonymous | User | ServiceAccount | System
record TenantDescriptor(Id: int, Name: string, Category: TenantCategory, State: TenantState, LiveTenantId: int?)
record AuthenticationAssurance(Acr: string?, AuthTime: DateTimeOffset?, AcrAuthTime: DateTimeOffset?)
record RequestContext                         // immutable; one per unit of work
  Tenant: TenantDescriptor?                   // null for deployable work
  Kind: PrincipalKind = Anonymous
  Subject: string?                            // sub, a service account's client id, or "system"
  ClientId: string?
  SessionId: string?
  Assurance: AuthenticationAssurance?
  UserId: int?                                // late-bound by the connect step
  Language: string = "en"                     // messages; BCP 47, no extensions
  Culture: string = "en"                      // formatting; BCP 47, no extensions
  CultureInfo: CultureInfo                    // Gregorian-forced clone of Culture
  Calendar: string = "gc"                     // gc | uq | et
  CalendarSystem: ICalendarSystem
  ContentLanguageIndex: int = 1               // 1..3; the tenant content language matching Culture, else 1
  TimeZone: string = "UTC"                    // the display zone: header -> User.PreferredTimeZone -> tenant
  TenantTimeZone: string = "UTC"              // binds today() and the TimeZone slot
  Now: DateTimeOffset
  Today: DateOnly                             // in TenantTimeZone
  TenantSettings: TenantSettings?             // null for deployable work
  Client: string = "web"                      // web | cli | mcp
  OriginTraceParent: string?
  IsSandbox: bool                             // derived
  IsSystem: bool                              // derived: Kind = System
  IsServiceAccount: bool                      // derived: Kind = ServiceAccount
  RequireTenant() -> TenantDescriptor   sync  // throws when not tenant-scoped
  Deployable: RequestContext                  // static
record RequestContextSnapshot(TenantId: int, Kind: PrincipalKind, Subject: string?, ClientId: string?, UserId: int?, Language: string, Culture: string, Calendar: string, TimeZoneId: string, Client: string, TraceParent: string?)
service IRequestContextAccessor
  Current: RequestContext                     // Deployable when nothing set one
contract IRequestContextInitializer          // 0013 at Order 100 (connect), 0012 at Order 200 (negotiation)
  Order: int
  InitializeAsync(context: RequestContext, inputs: RequestContextInputs) -> RequestContext
record RequestContextInputs(AcceptLanguage: string?, RequestedCalendar: string?, RequestedTimeZone: string?, Client: string?)
record TenantMembership(Tenant: TenantDescriptor, IsActive: bool, UpdatedAt: DateTimeOffset)
data TenantMembershipRecord                   // [TableType] standalone -> [catalog].[TenantMembershipList]
  TenantId: int   Subject: nvarchar(36)   IsActive: bit
service ITenantMembershipDirectory
  ListAsync(subject: string) -> list<TenantMembership>       // navigation hint only; excludes Retired; one catalog query
  RecordAsync(records: list<TenantMembershipRecord>)        // best effort; called by UserService.AfterCommitAsync
contract ITenantStateListener                 // implemented by the job worker and TellmaHub
  OnStateChangedAsync(tenant: TenantDescriptor, previous: TenantState?)
contract ISessionTerminationListener          // implemented by TellmaHub's connection tracker
  SessionsTerminatedAsync(subject: string, sessionKeys: list<string>)
  TenantAccessRevokedAsync(tenantId: int, subject: string)
service ITenantScopeFactory
  CreateScopeAsync(snapshot: RequestContextSnapshot) -> TenantScope   // re-resolves the tenant; throws TenantUnavailableException for non-Active states
  Capture() -> RequestContextSnapshot   sync
record TenantScope(Services: IServiceProvider, Activity: Activity?)   // IAsyncDisposable
data TenancyTelemetryNames                    // constants; MeterName = "Tellma.Core"; tellma.tenancy.*

// Tellma.Core.Tenancy (runtime; SqlClient and EF types live here)
record TenantLocation(Server: string, Database: string, CredentialProfile: string)
record TenantInfo(Descriptor: TenantDescriptor, Location: TenantLocation, ConnectionString: string, Properties: string?, Version: Guid)
service ITenantRegistry                       // singleton snapshot; 15 s version poll; staleness bound
  Tenants: list<TenantInfo>   SnapshotVersion: Guid   SnapshotLoadedAt: DateTimeOffset?
  Find(tenantId: int) -> TenantInfo?   sync
  Get(tenantId: int) -> TenantInfo   sync     // TenantNotFoundException
  RefreshAsync(force: bool)
enum TenantRegistrationPolicy = SingleLive | MultiLive
record TenantRegistration(Name: string, Category: TenantCategory, LiveTenantId: int?, Location: TenantLocation?, Id: int?)
service ITenantCatalog                        // write side; one catalog transaction + version bump + local refresh per member
  RegistrationPolicy: TenantRegistrationPolicy
  RegisterAsync(registration: TenantRegistration, actor: string) -> TenantDescriptor
  SetStateAsync(tenantId: int, state: TenantState, reason: string?, actor: string) -> TenantDescriptor
  RenameAsync(tenantId: int, name: string)
  RelocateAsync(tenantId: int, location: TenantLocation, actor: string)
  ListAsync() -> list<TenantInfo>
service ITenantConnectionFactory              // singleton; explicit tenant
  GetConnectionString(tenantId: int) -> string   sync
  OpenAsync(tenantId: int) -> SqlConnection
  OpenCatalogAsync() -> SqlConnection
service ITenantConnectionProvider             // scoped; the executor's door to SQL
  Tenant: TenantInfo
  OpenAsync() -> SqlConnection
service ITenantDbContextFactory
  Create(tenantId: int) -> TellmaDbContext   sync
service IRequestContextHolder : IRequestContextAccessor   // scoped; platform-internal writer
  Set(context: RequestContext)   sync
service ITenantAccessGuard
  EnsureAccessAsync(requirement: TenantAccessRequirement)   // runs the initializers and the assurance check; throws on refusal
record TenantAccessRequirement(IsMutation: bool, Assurance: RequireAssuranceMetadata?)
record RequireAssuranceMetadata(Acr: string, MaxAge: TimeSpan?)
service ITenantProvisioningTrigger            StartAsync(tenantId: int)
service IAuthenticationPolicyProvider         GetLoginPolicyAsync(tenantId: int?) -> LoginPolicy
record LoginPolicy(AcrValues: string?, MaxAge: TimeSpan?, AllowedMethods: list<string>?)
```

`ISandboxContext` (spec 0007) is implemented over `RequestContext.IsSandbox`; an unbound (deployable) scope throws. Tenant-state verdicts: `Provisioning` → 503 `tenant-provisioning`; `ReadOnly` → mutations 403 `tenant-read-only`; `Suspended` → 403 `tenant-suspended`; `Retired` → 404 `tenant-not-found`; background scopes exist only for `Active`. `ReadOnly` means "the application refuses mutations and the database itself may be read-only" (a relocation, a replica, a restore): the connect initializer composes `ConnectPremises` with `StampActivity = false` and `AllowStateFlip = false` for a `ReadOnly` tenant, so the prologue writes nothing and every read succeeds against a database set `READ_ONLY` (conformance matrix row: `ReadOnly` read succeeds, `ReadOnly` mutation is 403). Catalog tables (`catalog.Tenants`, `catalog.CatalogState`, `catalog.TenantMemberships`, `catalog.Sessions`) are 0010 §4 as written (they keep `nvarchar(16)` + CHECK for enums and `datetime2(3)`).

---

## 10. Seam 10 — Platform exceptions and HTTP mapping (types 0014; mapping 0015)

```contract
// Tellma.Core.Abstractions.Errors
base TellmaException                          // Code: kebab-case problem code; Arguments: map<string, string> display data; the message is for logs
  Code: string   Arguments: map<string, string>
record ValidationException(Errors: list<ValidationError>)                                   : TellmaException   // 422 validation
record ImportException(Errors: list<ImportError>, TotalErrors: int)                          : ValidationException   // 422 validation with coordinates
record NotFoundException(Resource: string, Ids: list<string>)                                 : TellmaException   // 404 not-found
record ForbiddenException(Code: string, Resource: string, Action: string?)                   : TellmaException   // 403 forbidden
record ConcurrencyException(Code: string, Conflicts: list<ConcurrencyConflict>)               : TellmaException   // 409 concurrency-conflict
record ConcurrencyConflict(Id: long, ModifiedAt: string?, ModifiedById: long?, ModifiedByName: string?, IsMissing: bool)   // ModifiedAt = the opaque stamp string
record CountMismatchException(Expected: int, Actual: int)                                    : TellmaException   // 409 count-mismatch; THROW 50428
record LimitExceededException(Limit: string, Actual: long, Maximum: long)                    : TellmaException   // 413 limit-exceeded
record InvalidQueryException(Diagnostics: list<QueryexDiagnostic>)                           : TellmaException   // 400 query-invalid
record BadRequestException(Detail: string?)                                                  : TellmaException   // 400 bad-request
record StepUpRequiredException(Acr: string, MaxAge: TimeSpan?)                               : TellmaException   // 401 step-up-required + WWW-Authenticate
record HumanRequiredException()                                                              : TellmaException   // 403 human-required
record TenantNotFoundException(TenantId: int)                                                : TellmaException   // 404 tenant-not-found (also non-members and deactivated users)
record TenantUnavailableException(TenantId: int, State: TenantState?, Code: string, RetryAfter: TimeSpan?)   : TellmaException   // 503 tenant-provisioning | catalog-unavailable; 403 tenant-suspended | tenant-read-only
record StaleContextException(Dependencies: list<VersionTagDependency>)                       : TellmaException   // 503 stale-context, Retry-After: 1
record DependencyUnavailableException(Dependency: string, RetryAfter: TimeSpan?, Inner: Exception?)   : TellmaException   // 503 dependency-unavailable
record PartialFailureException(Code: string, Results: object, Failed: list<string>)          : TellmaException   // 502 partial-failure, partial results in the body
record BlobRejectedException(Code: string, Arguments: map<string, string>)                   : TellmaException   // by code: 413 Blob.TooLarge, 415 Blob.UnsupportedType, 404 Blob.UnknownKind, 411 Blob.LengthRequired, 422 otherwise (path Body)
```

Everything else is 500 `internal` with a trace id; CSRF failures are 403 `csrf-rejected`. Two data-layer translations are fixed here: a `50409` whose conflicts are all missing rows is `NotFoundException` (404), and `ConcurrencyException` (409) only when at least one row exists with a differing stamp; `50503` (`TellmaSqlErrors.Transient`, raised by the access applock timeout) is retried by the executor and, when exhausted, surfaces as `DependencyUnavailableException` with `Retry-After: 1` — never as a validation error. Problem body (RFC 9457): `type = https://tellma.com/problems/<code>`, `title`, `status`, `detail` (localized under the request culture), `instance` (trace id), `code`, `errors` (validation: `{ path, code, message, arguments }`; Excel adds `sheet`, `row`, `column`, `header`), `errorDetails` (conflicts, diagnostics, results). Validation codes are dotted PascalCase resource keys; problem codes kebab-case. Mapping runs in the exception middleware of `Tellma.Core.AspNetCore` and in the MCP request filter (tool errors carry the same `code`).

---

## 11. Seam 11 — Access: securables, evaluation, connect (owner 0013; consumers 0014, 0015, 0016, 0018, 0019)

### 11.1 Constants, enums, annotations

```contract
// Tellma.Core.Abstractions.Access
enum UserKind = Human | System | Service       // Service: value exists, creation deferred
enum UserState = New | Invited | Active
enum InviteStatus = Invited | Reinvited | Active
enum Gender = Female | Male
data WellKnownIds                             // constants
  SystemUserId = 1; AdministratorRoleId = 1; AdministratorPermissionId = 1; SystemAdministratorMembershipId = 1; ReservedIdBandEnd = 999
data AccessActions                            // constants
  Read = "Read"; Save = "Save"; Delete = "Delete"; Activate = "Activate"; Invite = "Invite"; Retry = "Retry"; Cancel = "Cancel"; Resume = "Resume"; Diagnose = "Diagnose"; Wildcard = "*"
data CoreResources                            // constants
  User = "core.User"; Role = "core.Role"; SettingsGeneral = "core.Settings.General"; SettingsPrefix = "core.Settings."
  Job = "core.Job"; Schedule = "core.Schedule"; Notification = "core.Notification"; Export = "core.Export"; Import = "core.Import"; Wildcard = "*"
record SecurableRef(Resource: string, Action: string)
record SecurableOwner(Resource: string, Navigation: string)   // reserved: weak entities as query roots
record SecurableDescriptor(Resource: string, Action: string, FilterRoot: string?, IsSensitive: bool, Feature: string, Owner: SecurableOwner?)
  SupportsFilter: bool                        // derived: FilterRoot is not null
service SecurableRegistryBuilder
  Add(resource: string, action: string, filterRoot: string?, isSensitive: bool = false, owner: SecurableOwner? = null) -> SecurableRegistryBuilder   sync
  Alias(oldResource: string, newResource: string) -> SecurableRegistryBuilder   sync
  MarkSensitive(resource: string, action: string) -> SecurableRegistryBuilder   sync
contract ISecurableContributor
  Contribute(builder: SecurableRegistryBuilder)   sync
service ISecurableRegistry
  All: list<SecurableDescriptor>   Resources: list<string>
  Find(resource: string, action: string) -> SecurableDescriptor?   sync   // aliases resolved; ordinal-ignore-case
  ForResource(resource: string) -> list<SecurableDescriptor>   sync
  Fingerprint: string                         // hex SHA-256; deployment-scoped; travels in me
record SecurableEndpointMetadata(Resource: string, Action: string)
record MemberEndpointMetadata()
record NoActivityStampMetadata()
```

Resource grammar: `<schema>.<LogicalEntityName>` for entities, `core.Settings.General` / `core.Settings.<Category>` for settings; `*` wildcard on either axis; display `resource:action`. Sensitive by default (step-up per `Tellma:Session:StepUp { Acr, MaxAge }`): `core.User` × `Save | Delete | Activate | Invite`, `core.Role` × `Save | Delete | Activate`, `core.Settings.General` × `Save`, `core.Schedule` × `Save | Delete | Activate`. The stack feature's `ISecurableContributor` reads `IStackRegistry` and registers every standard operation's action, every `[EntityAction]` permission and every `[ApiAction].Action` of an entity service as `(Resource, Action, FilterRoot = Entity)`; `contribution.ApiService<T>()` registers each `[ApiAction(Resource, Action)]` of an `[ApiRoute]` service with `FilterRoot = null`; `[Cacheable]` stacks register `Read` with `FilterRoot = null`. A distribution therefore writes no securable registration for its own entities and actions; `Securables(...)` exists for non-entity resources (settings categories, hand-mapped endpoints). The realised gate fails on an `[ApiAction]` whose pair is registered by nobody.

### 11.2 Evaluation

```contract
// Tellma.Core.Abstractions.Access
enum AccessOutcome = Denied | Filtered | Unrestricted
enum AccessGrantSource = Role | PublicRole | Bespoke | System
enum AccessProblemCode = UnknownResource | UnknownAction | FilterUnsupported | FilterInvalid | VersionUnsupported
record AccessGrant(Source: AccessGrantSource, RoleId: int?, RoleName: string?, PermissionId: int?, Resource: string, Action: string, Filter: string?, Reason: string?)
record AccessProblem(PermissionId: int, RoleId: int, Code: AccessProblemCode, Diagnostics: list<QueryexDiagnostic>)
record AccessDecision(Resource: string, Action: string, Outcome: AccessOutcome, Filter: FilterTree?, Grants: list<AccessGrant>, Problems: list<AccessProblem>)
  IsAllowed: bool                             // derived: Outcome != Denied; Filter non-null exactly when Filtered
record AccessCriterion(Action: string, Filter: string, Reason: string)   // bespoke; current language version
contract IAccessCriteriaProvider
  Resource: string
  GetCriteria(userId: int) -> list<AccessCriterion>
record UserAccess                             // the cached set; FormatVersion = 1; cache key (TenantId, UserId)
  TenantId: int   UserId: int   Tag: Guid   UserTag: Guid   ComputedAt: DateTime   ValidatedAt: DateTime   IsSystem: bool   Grants: list<AccessGrant>   Problems: list<AccessProblem>
  Decide(securable: SecurableDescriptor, bespoke: list<AccessCriterion>, queryRoot: string?) -> AccessDecision   sync
  System: UserAccess                          // static; unrestricted, never loaded
service IAccessEvaluator                      // scoped
  Evaluate(resource: string, action: string) -> AccessDecision              // ambient caller; records a witness
  Require(resource: string, action: string) -> AccessDecision               // ForbiddenException when Denied
  Evaluate(securables: list<SecurableRef>) -> list<AccessDecision>
  EvaluateFor(userId: int, resource: string, action: string) -> AccessDecision   // "can they, and why"; no witness
  TryDenyFast(resource: string, action: string) -> bool   sync              // cache-only; true only when the cached set was validated by a prologue within FastDenyWindow and denies
  EvaluateAll() -> list<AccessDecision>                                     // one per registered securable
  GetAccess() -> UserAccess
  Invalidate(tenantId: int, userId: int)   sync
data AccessOptions                            // Tellma:Access
  MaxPermissionsPerRole: int = 300   MaxRolesPerUser: int = 32   MaxFilterLength: int = 2048   MaxCachedUsers: int = 10000
  CacheSlidingExpiration: TimeSpan = 4h   PermissionsMaxAge: TimeSpan = 15min   ActivityStampInterval: TimeSpan = 60s   FastDenyWindow: TimeSpan = 5s
  MaxPreferenceKeys: int = 256   MaxPreferenceValueBytes: int = 32768   SecurityLockTimeout: TimeSpan = 5s
service IAccessGuards
  Contribute(batch: IDataBatch)   sync        // sp_getapplock 'tellma.access' first; L1–L3 after the writes, inside the transaction; for every Persist batch whose WrittenTables meets a security table, whatever the operation
service UserAccessRules<TUser>   where TUser: User     // IEntityValidator<TUser>: escalation, self-lockout, email lock, delete policy
service RoleAccessRules<TRole>   where TRole: Role     // IEntityValidator<TRole>: securable existence, filter validation, public-role rules
service IAdministratorDirectory
  GetAdministratorIds() -> list<int>          // members of the Administrator role, active, capped at 20
record TenantBootstrapRequest(Email: string, Name: string, PreferredLanguage: string?, Subject: string?)
contract ITenantBootstrapper
  BootstrapAdministrator(request: TenantBootstrapRequest) -> int   // idempotent on email; Subject only in Development
data AccessTelemetryNames                     // constants; MeterName = "Tellma.Core"
  Decisions = "tellma.access.decisions"; CacheHits = "tellma.access.cache.hits"; CacheMisses = "tellma.access.cache.misses"; CacheEntries = "tellma.access.cache.entries"
  SetBuildDuration = "tellma.access.set.build.duration"; SetGrants = "tellma.access.set.grants"; PermissionsUnresolved = "tellma.access.permissions.unresolved"
  GuardsTriggered = "tellma.access.guards.triggered"; WitnessMissing = "tellma.access.witness.missing"; ConnectResults = "tellma.access.connect.results"; ConnectPrologueDuration = "tellma.access.connect.prologue.duration"
  OutcomeTag = "outcome"; ReasonTag = "reason"; ProblemTag = "problem"; GuardTag = "guard"; ResultTag = "result"; VariantTag = "variant"
```

How `UserAccess.Decide` composes a decision for `(R, A)`: the candidate grants are the union of the caller's active-role memberships, every active `IsPublic` role, and the bespoke `AccessCriterion`s the resource's `IAccessCriteriaProvider` returns; a stored grant matches when its resource equals `R` or is `*` and its action equals `A` or is `*`; **a grant on `(R, A')` for any `A' ≠ Read` also matches `(R, Read)` with the same filter** (write implies read); inactive roles, memberships of inactive users, and drifted permissions contribute nothing; a public role's grants apply to every caller (the prologue unions them in). If any matching grant is unfiltered the outcome is `Unrestricted`; otherwise `Filter` is the `FilterTree.Or` of every matching grant's filter (bespoke criteria included, so a criterion alone yields `Filtered` with no stored grant — the self-scope of jobs, exports and notifications); no match is `Denied`. Rules the pipeline applies: `decision.Filter` is conjoined into every read, update and delete; the access filter alone is passed as `Ancestors`; navigation traversal is limited to the target's `[RelatedSelect]` when the caller lacks `Read` on it (0015 D13); a drifted permission (unknown resource/action, unsupported or uncompilable filter, `FilterLanguageVersion` below the engine minimum) grants nothing and is reported in `Problems`; the witness turns an operation that evaluated no securable into a 500 (`tellma.access.witness.missing`). `TryDenyFast` is bounded: it answers `true` only when the caller's cached `UserAccess` was confirmed by a prologue within `FastDenyWindow` (default 5 s) and denies; otherwise the endpoint filter falls through to the handler, whose prologue refreshes the set — a user who has just been granted a permission is never locked out for `PermissionsMaxAge`. Guard invariants (`IAccessGuards`): `Access.LastAdministrator`, `Access.PublicRoleHasMembers`, `Access.AdministratorRoleDamaged` (`THROW 50422`); an applock timeout is `THROW 50503, N'Access.LockTimeout'` (transient, retried, then 503). Validation codes: `Users.EmailLockedAfterInvite`, `Users.EmailTaken`, `Users.SubjectTaken`, `Users.SubjectMismatch`, `Users.AlreadyActive`, `Users.CannotDeactivateSelf`, `Users.CannotDeleteSelf`, `Users.CannotRemoveOwnAdministratorMembership`, `Users.SystemUserImmutable`, `Users.OnlyNewUsersDeletable`, `Users.MembershipEscalation`, `Users.TooManyRoles`, `Users.SelfServiceOnly`, `Users.SandboxRequiresExistingIdentity`, `Roles.PublicRoleHasMembers`, `Roles.AdministratorImmutable`, `Roles.HasMembers`, `Roles.TooManyPermissions`, `Permissions.UnknownSecurable`, `Permissions.FilterNotSupported`, `Permissions.FilterInvalid`, `Permissions.FilterTooLong`, `Permissions.WildcardResourceOnPublicRole`, `Permissions.EscalationBeyondSelf`, `Centers.ParentMustBeGrouping`, `Schedules.HandlerNotSchedulable`, `Schedules.OwnedByAnotherUser`, `Schedules.BuiltInImmutable`, `Schedules.BuiltInAlwaysActive`.

### 11.3 The entities

```contract
// Tellma.Core.Abstractions.Access — non-abstract, unsealed defaults; distributions derive leaves
data User : TopLevelEntity, IActivatable      // [Table("Users", Schema = "core")], [TableType], [Temporal], [BumpsUserVersionTag(Preferences, "Id")], [ApiResource]
  Kind: UserKind = Human                      [ServerOwned]
  Subject: string?                            [ServerOwned]
  Email: string?                              [NaturalKey], [Searchable]; required for Human; normalised; locked after invite
  State: UserState = New                      [ServerOwned]
  InvitedAt: datetime2(3)?                    [ServerOwned]
  InviteStatus: InviteStatus?                 [ServerOwned]
  LastInviteError: string?                    [ServerOwned]
  ActivatedAt: datetime2(3)?                  [ServerOwned]
  Name: string                                [Multilingual], [Searchable], [SelfEditable]
  Name2: string?   Name3: string?             [SelfEditable]
  ImageId: int?                               [BlobReference("user-image", Avatar, ReadAccess = AnyMember)], [SelfEditable]
  PreferredLanguage: string?                  [SelfEditable]
  PreferredCalendar: string?                  [SelfEditable]
  PreferredTimeZone: string?                  [SelfEditable]
  Gender: Gender?                             [SelfEditable]
  ContactEmail: string?                       [SelfEditable]
  ContactMobile: string?                      [SelfEditable]
  IsActive: bool = true
  RoleMemberships: list<RoleMembership>       [NotMapped] child collection
data RoleMembership : ChildEntity             // [Temporal], [BumpsUserVersionTag(Permissions, "UserId")]
  UserId: int                                 [ParentKey]
  RoleId: int                                 FK -> core.Roles
  Notes: string?
data Role : TopLevelEntity, IActivatable      // [Temporal], [BumpsVersionTag("permissions")]
  Name: string                                [Multilingual], [Unique], [Searchable]
  Name2: string?   Name3: string?             [Unique]
  Code: string?                               [NaturalKey]
  IsPublic: bool = false
  IsActive: bool = true
  Permissions: list<Permission>               [NotMapped] child collection
data Permission : ChildEntity                 // [Temporal], [BumpsVersionTag("permissions")]
  RoleId: int                                 [ParentKey]
  Resource: string                            // resource id or *
  Action: string                              // action or *
  Filter: string?                             // Queryex predicate over the securable's FilterRoot
  FilterLanguageVersion: int?                 [ServerOwned]; stamped only when Filter text is new or changed
  Notes: string?
```

Public permissions are the `IsPublic` flag on `Role`: a public role may have no memberships (`Roles.PublicRoleHasMembers`, and the `Access.PublicRoleHasMembers` guard) and no filtered wildcard-resource permission (`Permissions.WildcardResourceOnPublicRole`); the prologue unions every active public role's permissions into every caller's rows. No well-known public role is seeded and no separate table exists. `Settings` carries `[BumpsVersionTag("settings")]` only; `SettingsService.Save` bumps `permissions` explicitly when a language column changed (seam 22). `UserPreferences` and `NotificationPreferences` are not entities (self-service statements only).

### 11.4 Tables

**`core.Users`** — `[Temporal]` → `core.UsersHistory`; UDTT `UsersList`; sequence `core.sq_Users`.

| Column | Type | Null | Constraints |
|---|---|---|---|
| `Id` | `int` | no | PK clustered; 1 = system user |
| `Kind` | `varchar(8)` | no | `DF 'Human'` |
| `Subject` | `varchar(255)` | yes | `COLLATE Latin1_General_100_BIN2`; `UX_Users_Subject WHERE Subject IS NOT NULL INCLUDE (Kind, IsActive, State)` |
| `Email` | `nvarchar(255)` | yes | `UX_Users_Email WHERE Email IS NOT NULL` |
| `State` | `varchar(8)` | no | `DF 'New'` |
| `InvitedAt` | `datetime2(3)` | yes | |
| `InviteStatus` | `varchar(9)` | yes | |
| `LastInviteError` | `nvarchar(1024)` | yes | |
| `ActivatedAt` | `datetime2(3)` | yes | |
| `Name` | `nvarchar(255)` | no | |
| `Name2`, `Name3` | `nvarchar(255)` | yes | |
| `ImageId` | `int` | yes | `FK_Users_ImageId → core.Blobs(Id)`; `IX_Users_ImageId` |
| `PreferredLanguage` | `varchar(35)` | yes | |
| `PreferredCalendar` | `varchar(16)` | yes | |
| `PreferredTimeZone` | `varchar(64)` | yes | |
| `Gender` | `varchar(6)` | yes | |
| `ContactEmail` | `nvarchar(255)` | yes | |
| `ContactMobile` | `varchar(32)` | yes | E.164 |
| `IsActive` | `bit` | no | `DF 1` |
| audit set | | no | `FK_Users_CreatedById`, `FK_Users_ModifiedById → core.Users(Id)` |
| `ValidFrom`, `ValidTo` | `datetime2(7)` | no | period, shadow |

Checks: `CK_Users_HumanHasEmail (Kind <> 'Human' OR Email IS NOT NULL)`; `CK_Users_SystemHasNoSubject (Kind <> 'System' OR (Subject IS NULL AND Email IS NULL))`; `CK_Users_StateSubject (Kind <> 'Human' OR ((State = 'New') = (Subject IS NULL)))`; `CK_Users_StateInvitedAt (Kind <> 'Human' OR ((State = 'New') = (InvitedAt IS NULL)))`; `CK_Users_StateActivatedAt ((State = 'Active') = (ActivatedAt IS NOT NULL))`. Index `IX_Users_IsActive_Name (IsActive, Name)`.

**`core.Roles`** — temporal; UDTT `RolesList`; `core.sq_Roles`: `Id int PK` (1 = Administrator); `Name nvarchar(255) NOT NULL UX_Roles_Name`; `Name2`/`Name3 nvarchar(255) NULL UX_Roles_Name2/Name3 WHERE … IS NOT NULL`; `Code nvarchar(50) NULL UX_Roles_Code WHERE Code IS NOT NULL`; `IsPublic bit NOT NULL DF 0`, `IX_Roles_IsPublic WHERE IsPublic = 1`; `IsActive bit NOT NULL DF 1`; audit; period.

**`core.RoleMemberships`** — temporal; UDTT; `core.sq_RoleMemberships`: `Id int PK`; `UserId int NOT NULL FK_RoleMemberships_UserId → core.Users` NO ACTION; `RoleId int NOT NULL FK_RoleMemberships_RoleId → core.Roles` NO ACTION; `Notes nvarchar(1024) NULL`; `UX_RoleMemberships_UserId_RoleId (UserId, RoleId)`; `IX_RoleMemberships_RoleId_UserId (RoleId, UserId)`; period.

**`core.Permissions`** — temporal; UDTT; `core.sq_Permissions`: `Id int PK`; `RoleId int NOT NULL FK_Permissions_RoleId → core.Roles`; `Resource varchar(128) NOT NULL`; `Action varchar(32) NOT NULL`; `Filter nvarchar(2048) NULL`; `FilterLanguageVersion int NULL`; `Notes nvarchar(1024) NULL`; `CK_Permissions_WildcardHasNoFilter (Resource <> '*' OR Filter IS NULL)`; `CK_Permissions_FilterVersion ((Filter IS NULL) = (FilterLanguageVersion IS NULL))`; `IX_Permissions_RoleId (RoleId) INCLUDE (Resource, Action, Filter, FilterLanguageVersion)`; period.

**`core.UserPreferences`** — non-temporal; not an entity: `UserId int NOT NULL FK_UserPreferences_UserId → core.Users ON DELETE CASCADE`; `Key varchar(128) NOT NULL`; `Value nvarchar(max) NOT NULL` (≤ 32 KB); PK clustered `(UserId, Key)`; ≤ 256 keys per user. Self-service statement (`@tm_UserId` the connected user; `@tb{b}_t0 : UserPreferenceList`; declares writes `core.UserPreferences`; the epilogue bumps `PreferencesTag`):

```sql
DELETE P FROM [core].[UserPreferences] AS P
WHERE P.[UserId] = @tm_UserId AND NOT EXISTS (SELECT 1 FROM @tb{b}_t0 AS I WHERE I.[Key] = P.[Key]);
UPDATE P SET [Value] = I.[Value] FROM [core].[UserPreferences] AS P JOIN @tb{b}_t0 AS I ON I.[Key] = P.[Key]
WHERE P.[UserId] = @tm_UserId AND P.[Value] <> I.[Value];
INSERT [core].[UserPreferences] ([UserId], [Key], [Value])
SELECT @tm_UserId, I.[Key], I.[Value] FROM @tb{b}_t0 AS I
WHERE NOT EXISTS (SELECT 1 FROM [core].[UserPreferences] AS P WHERE P.[UserId] = @tm_UserId AND P.[Key] = I.[Key]);
-- me/preferences/set and me/preferences/delete run the UPDATE+INSERT or the DELETE half only.
```

**`core.NotificationPreferences`** — seam 15. `HasData` (reserved band): system user `(1, Kind = System, Subject NULL, Email NULL, Name 'System', IsActive 1, audit self-referencing)`; Administrator role `(1, Name 'Administrator', Code 'Administrator')`; permission `(1, RoleId 1, Resource '*', Action '*')`; membership `(1, UserId 1, RoleId 1)`; the `UserStamps` row. No `Public` role is seeded; `core.VersionTags` is seeded by the migrator only (seam 5).

Queryex names: `User`, `Role`, `RoleMembership`, `Permission`, `UserStamp` (read-only; navigation `User`); navigations `CreatedBy`/`ModifiedBy → User`, `Image → Blob`, `RoleMembership.User/.Role`, `Permission.Role`.

---

## 12. Seam 12 — Blobs and staging tokens (owner 0016; consumers 0014, 0017, 0018, 0019)

The token is the `int` id of a `Staged` row in `core.Blobs`. Attach rule: staged, unexpired, right kind, staged by the saving user, used at most once in the payload; an unchanged value is untouched; `null` releases. TTL 24 h.

### 12.1 Contracts

```contract
// Tellma.Core.Abstractions.Blobs
contract IBlobStore                           // singleton; bytes by name for one tenant per call; writes create-only
  WriteAsync(tenantId: int, writes: list<BlobWrite>)
  OpenReadAsync(tenantId: int, name: string) -> BlobContent?
  DeleteAsync(tenantId: int, names: list<string>)
  ListAsync(tenantId: int, prefix: string) -> list<BlobStoreEntry>      // async sequence; tooling and reconcile only
  PurgeIncompleteAsync(tenantId: int, olderThan: DateTimeOffset)
record BlobWrite(Name: string, ContentType: string, Content: Stream, Length: long)
record BlobContent(Content: Stream, Length: long)                      // disposable
record BlobStoreEntry(Name: string, Length: long, LastModified: DateTimeOffset)
service BlobName                                                       // static helper; grammar {kind}/{k0k1}/{key32}[.{variant}]
  IsValidKind(kind: string) -> bool   sync                             // ^[a-z][a-z0-9-]{1,39}$
  IsValidVariant(variant: string) -> bool   sync                       // ^[a-z0-9]{1,16}$
  IsValid(name: string) -> bool   sync
  Primary(kind: string, key32: string) -> string   sync
  Variant(primaryName: string, variant: string) -> string   sync
service IBlobService                          // scoped; tenant and user from RequestContext; usable in request and job scopes
  StageAsync(request: BlobStageRequest) -> BlobDescriptor
  ResolveAsync(kind: string, id: int, variant: string?) -> BlobDownload?   // null = 404; by state: Staged -> only the uploader (CreatedById = the caller) while unexpired; Committed -> the kind's ReadAccess through the owner row; Released | Deleting -> null
record BlobStageRequest(Kind: string, Content: Stream, Length: long, DeclaredContentType: string?, FileName: string?)
record BlobDescriptor(Id: int, Kind: string, ContentType: string, Size: long, Width: int?, Height: int?, FileName: string?, ExpiresAt: DateTimeOffset?)
record BlobDownload(Id: int, Kind: string, StorageName: string, ContentType: string, Size: long, FileName: string?, IsImage: bool, ETag: string)
enum ImageFit = Contain | CoverSquare
enum ImageFormat = WebP | Jpeg | Png
record BlobKindPolicy
  MaxSize: long   AllowedContentTypes: list<string>   Image: ImagePolicy?   StagingTtl: TimeSpan?   ReadAccess: BlobReadAccess = OwnerRead
  Attachment: BlobKindPolicy   Avatar: BlobKindPolicy   Photo: BlobKindPolicy      // static presets
record ImagePolicy(MaxDimension: int, Fit: ImageFit, ThumbnailSize: int?, Format: ImageFormat = WebP, MaxInputPixels: int = 50000000)
contract IImageProcessor
  ProcessAsync(input: bytes, policy: ImagePolicy) -> ProcessedImage
record ProcessedImage(Primary: bytes, Width: int, Height: int, Thumbnail: bytes?, ContentType: string)
data Blob : Entity<int>                       // system-written; table core.Blobs; Queryex entity Blob (no StorageKey, no Sha256)
  Kind: string   State: BlobState   ContentType: string   FileName: string?   Size: long   Width: int?   Height: int?   Variants: string
  CreatedAt: datetime2(3)   CreatedById: int   CommittedAt: datetime2(3)?   ExpiresAt: datetime2(3)?
enum BlobState = Staged | Committed | Released | Deleting
service IBlobKindRegistry
  Find(kind: string) -> BlobKindDescriptor?   sync
  All: list<BlobKindDescriptor>
record BlobKindDescriptor(Kind: string, Policy: BlobKindPolicy, OwnerResource: string, OwnerEntity: string, OwnerTable: TableName, OwnerColumn: string)
data BlobTelemetryNames                       // constants; MeterName = "Tellma.Blobs" for store instruments (tellma.blobs.store.*); the rest under "Tellma.Core" (tellma.blobs.uploads/downloads/sweep.*)

// Tellma.Core.Blobs (runtime)
data BlobOptions                              // Tellma:Blobs
  StagingTtl: TimeSpan = 24h   SweepInterval: TimeSpan = 15m   SweepBatchSize: int = 500   SweepReclaimAfter: TimeSpan = 60m
  MaxStagedPerUser: int = 200   MaxStagedBytesPerUser: long = 512 MiB   MaxUploadSize: long = 100 MiB
data FileSystemBlobStoreOptions               RootPath: string   required, absolute
service BlobsComposition
  AddFileSystemBlobStore(services: IServiceCollection, configure: (FileSystemBlobStoreOptions) -> void)   sync
data BlobsBuilder                             // tellma.Blobs(b => …)
  Kind(kind: string, policy: BlobKindPolicy) -> BlobsBuilder   sync      // replaces the attribute's preset; owner still from the attribute
  ImageProcessor<TProcessor>() -> BlobsBuilder   sync   where TProcessor: IImageProcessor   // any IImageProcessor; the alternative to AddTellmaImageSharp

// Tellma.Core.Imaging (separate package: Abstractions + SixLabors.ImageSharp only; never referenced by Tellma.Core)
service ImageSharpImageProcessor : IImageProcessor   // re-encodes to the policy's format, fit and dimensions; bounded by MaxInputPixels
service ImagingComposition
  AddTellmaImageSharp(services: IServiceCollection)   sync              // registers ImageSharpImageProcessor as the IImageProcessor

// Tellma.Connector.AzureBlobs (adapter)
data AzureBlobStoreOptions                    ServiceUri: Uri?   ConnectionString: string?   ContainerPrefix: string = "tellma-"   MaxParallelism: int = 8
service AzureBlobsComposition
  AddAzureBlobStore(services: IServiceCollection, configure: (AzureBlobStoreOptions) -> void, credential: TokenCredential?)   sync
```

Kinds shipped by Core: `user-image` (`Avatar`, `AnyMember`), `export-file`, `import-file`, `import-result` (`Attachment`, `OwnerRead`). `Blob.NotAttachable` and `Blob.DuplicateReference` are the validator's codes; `BlobRejectedException` codes: `Blob.TooLarge`, `Blob.UnsupportedType`, `Blob.UnknownKind`, `Blob.LengthRequired`, `Blob.StagingQuotaExceeded`, `Blob.ImageRejected`. `ImageRejectedException`, `BlobAlreadyExistsException`, `BlobStoreException` are internal to `Tellma.Core.Blobs` and the adapter.

### 12.2 Table `core.Blobs` — non-temporal; no UDTT; sequence `core.sq_Blobs`

| Column | Type | Null | Constraints |
|---|---|---|---|
| `Id` | `int` | no | PK clustered |
| `Kind` | `nvarchar(40)` | no | |
| `StorageKey` | `nvarchar(120)` | no | `UX_Blobs_StorageKey` |
| `State` | `varchar(9)` | no | |
| `ContentType` | `nvarchar(100)` | no | |
| `FileName` | `nvarchar(255)` | yes | sanitised; display only |
| `Size` | `bigint` | no | |
| `Sha256` | `binary(32)` | no | |
| `Width`, `Height` | `int` | yes | images only |
| `Variants` | `nvarchar(100)` | no | comma-separated; `''` for none |
| `CreatedAt` | `datetime2(3)` | no | |
| `CreatedById` | `int` | no | `FK_Blobs_CreatedById → core.Users(Id)`; the only user who may attach it |
| `CommittedAt` | `datetime2(3)` | yes | |
| `ExpiresAt` | `datetime2(3)` | yes | staging deadline / release time / reclaim time; NULL while `Committed` |

Indexes: `IX_Blobs_Expiry (ExpiresAt) INCLUDE (Kind, StorageKey, Variants, State) WHERE ExpiresAt IS NOT NULL`; `IX_Blobs_StagedBy (CreatedById) INCLUDE (Size) WHERE State = 'Staged'`. State machine: `Staged → Committed` (save tx) `→ Released` (save/delete tx, reconcile) `→ Deleting` (sweep claim) `→ row gone`; `Staged → Deleting` when `ExpiresAt` passes.

### 12.3 Statements (per `[BlobReference]` column; capture table `@tb{b}_blob_<Table>_<Column>` filled by the emitter's `OUTPUT … INTO`; `@tb{b}_p0` kind, `@tb{b}_p1` user id)

```sql
-- release blobs the change dereferenced (old value from OUTPUT, never from memory)
UPDATE b SET [State] = 'Released', [ExpiresAt] = SYSUTCDATETIME()
FROM [core].[Blobs] AS b INNER JOIN @tb{b}_blob_Users_ImageId AS c ON c.[OldBlobId] = b.[Id]
WHERE (c.[NewBlobId] IS NULL OR c.[NewBlobId] <> c.[OldBlobId]) AND b.[State] = 'Committed';
-- confirm newly attached blobs; kind, uploader and expiry re-checked inside the transaction
DECLARE @tb{b}_expected int = (SELECT COUNT(*) FROM @tb{b}_blob_Users_ImageId
                               WHERE [NewBlobId] IS NOT NULL AND ([OldBlobId] IS NULL OR [OldBlobId] <> [NewBlobId]));
UPDATE b SET [State] = 'Committed', [CommittedAt] = SYSUTCDATETIME(), [ExpiresAt] = NULL
FROM [core].[Blobs] AS b INNER JOIN @tb{b}_blob_Users_ImageId AS c ON c.[NewBlobId] = b.[Id]
WHERE (c.[OldBlobId] IS NULL OR c.[OldBlobId] <> c.[NewBlobId])
  AND b.[State] = 'Staged' AND b.[Kind] = @tb{b}_p0 AND b.[CreatedById] = @tb{b}_p1 AND b.[ExpiresAt] > SYSUTCDATETIME();
IF @@ROWCOUNT <> @tb{b}_expected THROW 50422, N'Blob.NotAttachable', 1;

-- core.blob-sweep claim (job scope; TransactionMode None; @tb{b}_p0 batch size, @tb{b}_p1 reclaim minutes)
DECLARE @tb{b}_now datetime2(3) = SYSUTCDATETIME();
WITH due AS (
    SELECT TOP (@tb{b}_p0) [Id], [Kind], [StorageKey], [Variants], [State], [ExpiresAt]
    FROM [core].[Blobs] WITH (ROWLOCK)
    WHERE [State] IN ('Staged', 'Released', 'Deleting') AND [ExpiresAt] < @tb{b}_now
    ORDER BY [ExpiresAt])
UPDATE due SET [State] = 'Deleting', [ExpiresAt] = DATEADD(minute, @tb{b}_p1, @tb{b}_now)
OUTPUT inserted.[Id], inserted.[Kind], inserted.[StorageKey], inserted.[Variants];
-- after IBlobStore.DeleteAsync (@tb{b}_t0 : IdList):
DELETE b FROM [core].[Blobs] AS b INNER JOIN @tb{b}_t0 AS i ON i.[Id] = b.[Id] WHERE b.[State] = 'Deleting';

-- core.blob-reconcile, per registered kind (@tb{b}_p0 kind):
UPDATE b SET [State] = 'Released', [ExpiresAt] = SYSUTCDATETIME() FROM [core].[Blobs] AS b
WHERE b.[Kind] = @tb{b}_p0 AND b.[State] = 'Committed' AND b.[CommittedAt] < DATEADD(day, -1, SYSUTCDATETIME())
  AND NOT EXISTS (SELECT 1 FROM <owner table> AS o WHERE o.<column> = b.[Id]);
```

Endpoints (0010's `Blobs` group, `Tellma.Core.AspNetCore`): `POST /{tenantId}/blobs/{kind}?fileName=` (raw body; `AcceptsBinaryMetadata(MaxUploadSize)`; CSRF header required; returns `BlobDescriptor`), `GET /{tenantId}/blobs/{kind}/{id}?variant=&download=` (`AllowMember` + `IBlobService.ResolveAsync`; `ETag "{id}"`; 304/412 via `Results.Stream`; `Cache-Control: private, max-age=31536000, immutable` for a committed blob — never `public` — and `Cache-Control: private, no-store` for a staged blob served to its uploader, which is how the details page previews a picked image before the save). `BlobReadAccess` has two values this release: `OwnerRead` (the caller must pass the owner row's `Read` filter) and `AnyMember`; a per-kind custom read filter is deferred (ledger §7). The capability contributes `BlobReferenceValidator<T>` (`IEntityValidator<T>`: context load `Id, State, Kind, CreatedById, ExpiresAt` for changed references, dedup key `("blob", ids)`) and `BlobReferenceEffect<T>` (`ISaveEffect<T>`: the statements above). Provisioning step `Order 20` pre-creates the tenant container (`tellma-<tenantId>`); lazy creation is the fallback. Image processing is licence-isolated: `ImageSharpImageProcessor` ships in `Tellma.Core.Imaging`, the reference distribution references that package and calls `services.AddTellmaImageSharp()`, and a swap to SkiaSharp is a package swap in the distribution; alternatively `tellma.Blobs(b => b.ImageProcessor<T>())` names any `IImageProcessor`. The realised startup gate fails when any blob kind with an `Image` policy exists and no `IImageProcessor` is registered.

---

## 13. Seam 13 — Wire shapes and the web/MCP host surface (owner 0015; consumers 0014, 0018, 0019, 0020)

### 13.1 Wire records — `Tellma.Core.Abstractions.Api`

```contract
data QueryRequest
  Select: string?                             // null = [DefaultSelect]
  Filter: string?                             // Queryex text; RLS conjoined server-side
  Having: string?                             // with Aggregate only
  OrderBy: string?
  Skip: int = 0                               // Skip + Take ≤ MaxSkipWindow
  Take: int?                                  // absent = 50; clamped to MaxTake
  Search: string?                             // ≤ MaxSearchLength
  Arguments: map<string, JSON>?
  IncludeCount: bool = false
  IncludeAncestors: bool = false              // tree stacks
  IncludeInactive: bool = false               // activatable stacks: lifts the IsActive = true default conjunct
  Aggregate: bool = false
record QueryResult(Rows: QueryRowSet, Count: int?, CountCapped: bool, Ancestors: QueryRowSet?)
record GetRequest(Id: long, Select: string?, Include: list<string>?)
data IdsRequest
  Ids: list<long>                             required; ≤ MaxIdsPerRequest
  ReturnEntities: bool = true
  Select: string?
  Include: list<string>?
record ParentIdsRequest(ParentIds: list<long>?, Select: string?, Filter: string?, Arguments: map<string, JSON>?)   // null or empty = roots
data SaveRequest<TEntity>
  Entities: list<TEntity>                     required; ≤ MaxEntitiesPerSave (1,000 on the web surface; 10,000 at the service)
  ReturnEntities: bool = true
  Select: string?
  Include: list<string>?
  Concurrency: ConcurrencyMode = Check        // the details page always sends Check
record DeleteByQueryRequest(Filter: string, Arguments: map<string, JSON>?, ExpectedCount: int)   // count verified inside the transaction; MaxDeleteByQueryRows enforced
data EntitiesResult<TEntity>
  Ids: list<long>                             // input order for save; requested order for get-by-ids
  Entities: list<TEntity>
  Related: RelatedEntities?
  Extras: map<string, JSON>?
  Rows: QueryRowSet?                          // row echo, one row per entity, when Select was given
record AffectedResult(Count: int)
record JobAccepted(JobId: int, ResourceId: int?)   // the 202 body; ResourceId = the Exports/Imports row
record MeResult(User: UserProfileView, PreferencesTag: string, Access: AccessSummary, Tags: map<string, string>, SecurablesFingerprint: string)
record UserProfileView(Id: int, Kind: UserKind, State: UserState, Name: string, Name2: string?, Name3: string?, Email: string?, ImageId: int?, PreferredLanguage: string?, PreferredCalendar: string?, PreferredTimeZone: string?)
record AccessSummary(Tag: string, FormatVersion: int, IsSystem: bool, Securables: list<SecurableSummary>, Problems: list<AccessProblem>)
record SecurableSummary(Resource: string, Action: string, HasFilter: bool)
record AccessCheckRequest(UserId: int?, Securables: list<SecurableRef>)   // -> list<AccessDecision>
record QueryDiagnostic(Clause: string, Code: string, Start: int, Length: int, Location: string?, Arguments: map<string, string>)   // the wire form of QueryexDiagnostic; Clause ∈ select | filter | orderBy | having
data TellmaHeaders                            // constants
  TimeZone = "Tellma-Time-Zone"; Calendar = "Tellma-Calendar"; Client = "Tellma-Client"; VersionTags = "Tellma-Version-Tags"; Build = "Tellma-Build"
data ApiTelemetryNames                        // constants; MeterName = "Tellma.Core.AspNetCore"
  RequestsRejected = "tellma.api.requests.rejected"; Problems = "tellma.api.problems"; OperationDuration = "tellma.api.operation.duration"
  OperationDbCalls = "tellma.api.operation.db_calls"; PermissionsStale = "tellma.api.permissions.stale"; UnknownMembers = "tellma.api.unknown_members"
  QueryDiscoverDuration = "tellma.api.query.discover.duration"; RealtimeEvents = "tellma.realtime.events"
  ResourceTag = "tellma.resource"; OperationTag = "tellma.operation"; ClientTag = "tellma.client"; ReasonTag = "reason"; EventTag = "event"
data McpTelemetryNames                        // constants; MeterName = "Tellma.Core.Mcp"
  ToolCalls = "tellma.mcp.tool.calls"; ToolDuration = "tellma.mcp.tool.duration"; ResultChars = "tellma.mcp.result.chars"; ToolTag = "tool"; OutcomeTag = "outcome"
```

Routes (all POST unless stated): `/{tenantId:int:min(1)}/api/web/{resource-segment}/{operation}` for stacks; `/{tenantId}/api/web/{route}/{action}` for `[ApiRoute]` services (`users/me`, `users/me/save`, `users/me/preferences/set`, `users/me/preferences/delete`, `users/me/test-notification`, `users/invite`, `users/invitation-status`, `access/check`, `settings/client`, `settings/entity-tags`, `settings/save`, `settings/refresh-caches`, `inbox/summary`, `inbox/seen`, `inbox/read`, `inbox/read-all`, `notification-preferences/save`, `jobs/{retry|cancel|resume}`); `/{tenantId}/hub`; `/{tenantId}/blobs/{kind}` (POST) and `/{tenantId}/blobs/{kind}/{id}` (GET); `/{tenantId}/mcp`; deployable `/api/distribution-info`, `/health/live`, `/health/ready`; `/bff/login`, `/bff/logout`, `/bff/user`. Request headers: `Accept-Language`, `Tellma-Calendar`, `Tellma-Time-Zone`, `Tellma-Client` (`<name>/<version>`, name ∈ `web | cli`; required on every cookie-authenticated POST; doubles as the CSRF control with an `Origin`/`Sec-Fetch-Site` check against `Tellma:PublicOrigin`). Two requests a browser cannot decorate with a custom header are exempt from `Tellma-Client` and rely on the `Origin`/`Sec-Fetch-Site` check alone: the blob `GET` (side-effect free) and the hub negotiate/connect at `/{tenantId}/hub`, which additionally rejects any cross-origin negotiate outright (cross-site WebSocket hijacking). Response headers: `Content-Language`, `Tellma-Calendar`, `Tellma-Version-Tags`, `Tellma-Build`, `Retry-After`. Query rows serialise as arrays of arrays with a `columns` header; `related` as `name -> array`. Wire ids: request records carry ids as `long` (a JSON number out of the entity's key range is a 400); result records carry the entity's own key type (`JobAccepted.JobId: int`, `UserProfileView.Id: int`, `InviteResult.Id: int` are correct as written).

### 13.2 Host surface — `Tellma.Core.AspNetCore` and `Tellma.Core.Mcp`

```contract
// Tellma.Core.AspNetCore
service TellmaWebApplicationBuilderExtensions   AddTellma(builder: WebApplicationBuilder, slug: string, compose: (TellmaBuilder) -> void) -> WebApplicationBuilder   sync
service TellmaApplicationBuilderExtensions      UseTellma(app: WebApplication) -> WebApplication   sync
service TellmaEndpointRouteBuilderExtensions    MapTellma(app: WebApplication) -> TellmaEndpoints   sync   // maps every surface, then runs the securable audit; any finding fails startup
data TellmaEndpoints
  Web: RouteGroupBuilder   Api: RouteGroupBuilder   Hub: RouteGroupBuilder   Blobs: RouteGroupBuilder   Deployable: RouteGroupBuilder
  AsDeployableEndpoint(builder, reason: string)   sync
  WithMutation(builder, isMutation: bool)   sync
  RequireSecurable(builder, resource: string, action: string)   sync    // hand-mapped endpoints
  AllowMember(builder)   sync
  AcceptsBinary(builder, maxBytes: long)   sync
data TellmaAuthentication                     // constants: SessionScheme "Tellma.Session", OidcScheme "Tellma.OpenIdConnect", BearerScheme "Tellma.Bearer", SessionCookieName "__Host-tellma.session", ProfileCookieName "tellma.profile", CsrfHeaderName "Tellma-Client"
data TellmaPolicies                           // constants: Web "Tellma.Web", Api "Tellma.Api", Mcp "Tellma.Mcp", ControlPlane "Tellma.ControlPlane"
enum TenantSurface = Web | Api | Mcp | Hub | Blobs
record TenantEndpointMetadata(Surface: TenantSurface, IsMutation: bool)
record DeployableEndpointMetadata(Reason: string)
record AcceptsBinaryMetadata(MaxBytes: long)
service ICatalogSessionStore : ITicketStore   // rows in catalog.Sessions; per-instance cache; revocation
  RevokeBySidAsync(sid: string)
  RevokeBySubjectAsync(subject: string)
  SweepAsync() -> int                         // invoked by SessionSweepService, a hosted timer in Tellma.Core.AspNetCore (Tellma:Session:SweepInterval, default 15 min); set-based, idempotent, safe on every instance at once — no lease, no job row
service StepUpChallenge
  Write(response: HttpResponse, acrValues: string, maxAge: int?)   sync   // 401 + WWW-Authenticate per spec 0003
record DistributionInfo(Slug: string, DisplayName: string, DeploymentId: string, PlatformVersion: string, DistributionVersion: string, Identity: IdentityInfo, Surfaces: map<string, string?>, Login: string, RegistrationPolicy: TenantRegistrationPolicy)
record IdentityInfo(Authority: string, Mode: string)
data TellmaApiOptions                         // Tellma:Api
  MaxJsonBodyBytes: long = 8 MB   MaxEntitiesPerSave: int = 1000   MaxIdsPerRequest: int = 10000   MaxTake: int = 10000   MaxCount: int = 10000   MaxSkipWindow: int = 100000
  RequestsPerMinutePerUser: int = 600   ConcurrentRequestsPerTenant: int = 64   AnonymousRequestsPerMinutePerIp: int = 60
  ConcurrentExportsPerUser: int = 2   ConcurrentImportsPerUser: int = 1   RequestTimeout: TimeSpan = 30 s   LongRequestTimeout: TimeSpan = 5 min
  EnableCompression: bool = true   ServerTiming: bool = false   ClientNames: set<string> = { "web", "cli" }   ProblemTypeBase: string = "https://tellma.com/problems/"
data HostTelemetryNames                       // constants; MeterName = "Tellma.Core.AspNetCore"; tellma.session.*, tellma.auth.*
service TellmaHub                             // SignalR hub at /{tenantId}/hub; groups t{tenantId} and t{tenantId}.u{userId}; implements ISessionTerminationListener and ITenantStateListener

// Tellma.Core.Mcp
data TellmaMcpOptions                         // Tellma:Mcp
  MaxToolResultChars: int = 60000   DefaultTop: int = 50   MaxTop: int = 500   MaxIdsPerCall: int = 100   ToolCallsPerMinutePerUser: int = 120
  ToolTimeout: TimeSpan = 60 s   ConfirmationLifetime: TimeSpan = 5 min   ListTtlMs: int = 300000
  AllowedOrigins: set<string> = { "https://claude.ai", "https://chatgpt.com" }   ToolTypes: list<Type>
```

Authorization posture: the application's fallback policy denies; each tenant route group calls `RequireAuthorization(TellmaPolicies.<Surface>)` (`Web` = session cookie, `Api` = bearer, `Mcp` = bearer with the per-tenant audience, `Hub` and `Blobs` = session cookie) and `ControlPlane` guards the admin surface; `AllowAnonymous` is permitted only on an endpoint carrying `DeployableEndpointMetadata` (`/api/distribution-info`, the health probes, the BFF login/logout); the `MapTellma` audit fails startup on any endpoint that has neither an authorization policy nor `DeployableEndpointMetadata`, and on any tenant endpoint without `SecurableEndpointMetadata` or `MemberEndpointMetadata`.

MCP: server name `tellma-tenant`, one stateless endpoint per tenant (`/{tenantId}/mcp`), audience `<PublicOrigin>/{tenantId}/mcp`, OAuth 2.1 resource server against the platform identity server (RFC 9728 metadata, 401 challenge); the request filter populates the same `IRequestContextHolder` with `Client = "mcp"`. The MCP surface is human-only this release (bearer tokens minted for a human `sub`); autonomous agents wait for service-account creation (ledger §7). Tools shipped by 0015: `tellma_whoami` (carries `securablesFingerprint`), `tellma_describe`, `tellma_query`, `tellma_get`, `tellma_save`, `tellma_delete`, `tellma_action`. Reserved names: `tellma_export`, `tellma_import`, `tellma_job`, `tellma_notifications`, `tellma_upload`, `tellma_check_access`. `Tellma:PublicOrigin` (0010) is the one public origin.

---

## 14. Seam 14 — Telemetry names and the DB-call budget (owner 0011; each spec names its own)

| Meter | Instrument prefixes | Constant holder (`Tellma.Core.Abstractions`) |
|---|---|---|
| `Tellma.Core` | `tellma.data.*` (`DataTelemetryNames`), `tellma.cache.*`/`tellma.versiontags.*`/`tellma.settings.*` (`CacheTelemetryNames`), `tellma.localization.*` (`LocalizationTelemetryNames`), `tellma.access.*` (`AccessTelemetryNames`), `tellma.crud.*` (`CrudTelemetryNames`: `operations`, `validation.rounds`, `concurrency.overrides`, `roundtrips`; tags `crud.operation`, `crud.source`, `crud.tag ∈ permissions | settings`), `tellma.users.*`/`tellma.identity.*` (`UsersTelemetryNames`), `tellma.tenancy.*` (`TenancyTelemetryNames`), `tellma.excel.*` (`ExcelTelemetryNames`), `tellma.jobs.*`/`tellma.schedules.*` (`JobsTelemetryNames`), `tellma.notifications.*` (`NotificationsTelemetryNames`), `tellma.blobs.uploads/downloads/sweep.*` (`BlobTelemetryNames`) | as named |
| `Tellma.Blobs` | `tellma.blobs.store.*` (shared with the Azure adapter) | `BlobTelemetryNames` |
| `Tellma.Core.AspNetCore` | `tellma.session.*`, `tellma.auth.*` (`HostTelemetryNames`), `tellma.api.*`, `tellma.realtime.*` (`ApiTelemetryNames`) | as named |
| `Tellma.Core.Mcp` | `tellma.mcp.*` | `McpTelemetryNames` |

Every holder exposes `MeterName`. `DataAccessScope` accumulates `RoundTrips` and `Retries` per operation; `tellma.api.operation.db_calls` reads it and the conformance tests assert the round-trip ledger. No tenant or user tags on any instrument; tenant and user ids go to the log scope and trace attributes.

---

## 15. Seam 15 — Notifications, the inbox and the hub (owner 0020; consumers 0014, 0017, 0018, 0019)

```contract
// Tellma.Core.Abstractions.Notifications
data Notification : Entity<int>               // system-written; table core.Notifications; immutable except ReadAt; stack resource core.Notification (self-scope UserId = me())
  UserId: int   Type: string   ArgumentsJson: string?   TargetResource: string?   TargetId: long?   ActorUserId: int?   DedupKey: string?
  CreatedAt: datetime2(3)   ReadAt: datetime2(3)?
data NotificationPreference                   // row shape of core.NotificationPreferences; not an entity
  UserId: int   Type: string   Inbox: bool = true   Email: bool = false
record NotificationTypeDescriptor(Key: string, Category: string, Mutable: bool, TargetResource: string?)
record NotificationRequest(Type: string, RecipientIds: list<int>, Arguments: object?, TargetResource: string?, TargetId: long?, ActorUserId: int?, DedupKey: string?)
service INotifier
  Notify(batch: IDataBatch, requests: list<NotificationRequest>)   sync   // appends the insert per request; ids assigned inside the statement (never the allocator's buffer); inbox.changed via OnCommitted
  NotifyAsync(requests: list<NotificationRequest>)                        // own round trip
service INotificationRenderer
  Render(notification: Notification, uiCulture: CultureInfo) -> string   sync   // the type's ICU template
record InboxSummary(Unseen: int, Unread: int, Latest: list<Notification>)   // counts capped at 100
data NotificationsTelemetryNames              // constants; MeterName = "Tellma.Core"; Created = "tellma.notifications.created"; TypeTag = "type"

// Tellma.Core.Notifications (runtime)
service InboxService                          // [ApiRoute("inbox")]; every action member-only
  Summary() -> InboxSummary
  Seen()
  Read(request: IdsRequest) -> AffectedResult
  ReadAll() -> AffectedResult
service NotificationPreferencesService        // [ApiRoute("notification-preferences")]
  Save(preferences: list<NotificationPreference>)   // the caller's rows only; UserId ignored

// Tellma.Core.Abstractions.Realtime
record ClientEvent(Name: string, UserIds: list<int>, Payload: object?)   // empty UserIds = every connected user of the tenant
service IClientEventPublisher
  Publish(batch: IDataBatch?, clientEvent: ClientEvent)   sync   // post-commit hook when a batch is given; immediate otherwise
  PublishAsync(clientEvent: ClientEvent)
```

| Member | Annotation |
|---|---|
| `InboxService.Summary` | `[ApiAction]` `summary`, member-only, idempotent |
| `InboxService.Seen` | `[ApiAction]` `seen`, member-only, idempotent |
| `InboxService.Read` | `[ApiAction]` `read`, member-only, idempotent |
| `InboxService.ReadAll` | `[ApiAction]` `read-all`, member-only, idempotent |
| `NotificationPreferencesService.Save` | `[ApiAction]` `save`, member-only; `SqlOptions.ForCaller` so the epilogue bumps the caller's `PreferencesTag` |

Types shipped by Core (registered through `FeatureContribution.NotificationType`): `core.export.ready` (`Mutable = false`, target `core.Export`), `core.import.completed`, `core.import.failed` (`Mutable = false`, target `core.Import`), `core.job.failed`, `core.job.held` (`Mutable = false`, target `core.Job`), `core.schedule.paused` (target `core.Schedule`), `core.scheduler.gap` (`Mutable = false`), `core.user.added` (mutable; the "you were added" notice). Hub events (registered through `ClientEvent`): `inbox.changed`, `job.changed { jobId }`, `cache.changed { tag }`, `session.ended`. The pipeline exposes `PersistContext.Notify` and `ActionContext.Notify`, both calling `INotifier.Notify` on the persist batch.

### 15.1 Tables

**`core.Notifications`** — non-temporal; written by `INotifier` only; `[TableType]`; sequence `core.sq_Notifications`: `Id int PK`; `UserId int NOT NULL FK_Notifications_UserId → core.Users`; `Type nvarchar(64) NOT NULL`; `ArgumentsJson nvarchar(4000) NULL`; `TargetResource varchar(128) NULL`; `TargetId bigint NULL`; `ActorUserId int NULL FK → core.Users`; `DedupKey nvarchar(128) NULL`; `CreatedAt datetime2(3) NOT NULL`; `ReadAt datetime2(3) NULL`. Indexes: `IX_Notifications_User (UserId, CreatedAt DESC) INCLUDE (ReadAt, Type)`; `IX_Notifications_Unread (UserId) WHERE ReadAt IS NULL`; `IX_Notifications_Dedup (UserId, DedupKey) WHERE DedupKey IS NOT NULL AND ReadAt IS NULL`; `IX_Notifications_Retention (ReadAt, CreatedAt)`.

**`core.NotificationPreferences`** — non-temporal; not an entity: `UserId int NOT NULL FK_NotificationPreferences_UserId → core.Users ON DELETE CASCADE`; `Type nvarchar(64) NOT NULL`; `Inbox bit NOT NULL DF 1`; `Email bit NOT NULL DF 0`; PK clustered `(UserId, Type)`. Written only by `notification-preferences/save` (`@tb{b}_t0 : NotificationPreferenceList`; synchronise like `UserPreferences`; `Inbox = 0` on a non-mutable type is the validation error `Notifications.CannotMute`; the epilogue bumps `PreferencesTag`).

### 15.2 Statements

```sql
-- INotifier.Notify, per request (@tb{b}_t0 : NotificationRowList, one recipient per ordinal; ids reserved here, never from the allocator's buffer;
-- @tb{b}_p0 type, p1 args, p2 targetResource, p3 targetId, p4 actorId, p5 dedupKey, p6 mutable)
DECLARE @tb{b}_first sql_variant, @tb{b}_n int = (SELECT COUNT(*) FROM @tb{b}_t0);
IF @tb{b}_n > 0 EXEC sys.sp_sequence_get_range @sequence_name = N'core.sq_Notifications', @range_size = @tb{b}_n, @range_first_value = @tb{b}_first OUTPUT;
INSERT INTO [core].[Notifications] ([Id], [UserId], [Type], [ArgumentsJson], [TargetResource], [TargetId], [ActorUserId], [DedupKey], [CreatedAt])
OUTPUT [inserted].[UserId]
SELECT CAST(@tb{b}_first AS int) + [r].[Ordinal], [r].[UserId], @tb{b}_p0, @tb{b}_p1, @tb{b}_p2, @tb{b}_p3, @tb{b}_p4, @tb{b}_p5, SYSUTCDATETIME()
FROM @tb{b}_t0 AS [r]
WHERE EXISTS (SELECT 1 FROM [core].[Users] AS [u] WHERE [u].[Id] = [r].[UserId] AND [u].[IsActive] = 1)
  AND (@tb{b}_p6 = 0 OR NOT EXISTS (SELECT 1 FROM [core].[NotificationPreferences] AS [p]
                                    WHERE [p].[UserId] = [r].[UserId] AND [p].[Type] = @tb{b}_p0 AND [p].[Inbox] = 0))
  AND (@tb{b}_p5 IS NULL OR NOT EXISTS (SELECT 1 FROM [core].[Notifications] AS [n]
                                        WHERE [n].[UserId] = [r].[UserId] AND [n].[DedupKey] = @tb{b}_p5 AND [n].[ReadAt] IS NULL));

-- inbox/summary (@tm_UserId the caller)
DECLARE @tb{b}_seenAt datetime2(3) = (SELECT [InboxSeenAt] FROM [core].[UserStamps] WHERE [UserId] = @tm_UserId);
SELECT
  (SELECT COUNT(*) FROM (SELECT TOP (100) 1 AS [x] FROM [core].[Notifications] WHERE [UserId] = @tm_UserId AND [CreatedAt] > COALESCE(@tb{b}_seenAt, '0001-01-01')) AS [a]) AS [Unseen],
  (SELECT COUNT(*) FROM (SELECT TOP (100) 1 AS [x] FROM [core].[Notifications] WHERE [UserId] = @tm_UserId AND [ReadAt] IS NULL) AS [b]) AS [Unread];
SELECT TOP (10) [Id], [Type], [ArgumentsJson], [TargetResource], [TargetId], [ActorUserId], [CreatedAt], [ReadAt]
FROM [core].[Notifications] WHERE [UserId] = @tm_UserId ORDER BY [CreatedAt] DESC, [Id] DESC;
-- inbox/seen
UPDATE [core].[UserStamps] SET [InboxSeenAt] = SYSUTCDATETIME() WHERE [UserId] = @tm_UserId;
-- inbox/read (@tb{b}_t0 : IdList); inbox/read-all drops the IN
UPDATE [core].[Notifications] SET [ReadAt] = SYSUTCDATETIME() WHERE [UserId] = @tm_UserId AND [ReadAt] IS NULL AND [Id] IN (SELECT [Id] FROM @tb{b}_t0);
```

---

## 16. Seam 16 — The connect prologue and the guarded runner (owners 0013 and 0014)

```contract
// Tellma.Core.Abstractions.Access
record UserProfile(Name: string, Name2: string?, Name3: string?, Email: string?, ImageId: int?, PreferredLanguage: string?, PreferredCalendar: string?, PreferredTimeZone: string?)
record ConnectedUser(UserId: int, Kind: UserKind, State: UserState, Profile: UserProfile, PreferencesTag: Guid, Access: UserAccess, TenantTags: map<string, Guid>, LastActivityStampedAt: DateTime?)
record ConnectPremises(Subject: string?, UserId: int?, ExpectedUserId: int?, ExpectedPermissionsTag: Guid?, ExpectedUserPermissionsTag: Guid?, ExpectedPreferencesTag: Guid?, ExpectedSettingsTag: Guid, StampActivity: bool, AllowStateFlip: bool, ReloadPermissions: bool)
  Cold(subject: string, expectedSettingsTag: Guid, stampActivity: bool, allowStateFlip: bool) -> ConnectPremises   // static, sync
  For(user: ConnectedUser, subject: string, stampActivity: bool, allowStateFlip: bool) -> ConnectPremises           // static, sync; both flags false on a ReadOnly tenant
enum ConnectPrologue = ForSubject | ForUser | System
record PermissionRow(PermissionId: int, RoleId: int, RoleName: string, IsPublic: bool, Resource: string, Action: string, Filter: string?, FilterLanguageVersion: int?)
record ConnectResult(UserId: int?, Kind: UserKind?, IsActive: bool, State: UserState?, PreferencesTag: Guid?, UserPermissionsTag: Guid?, TenantTags: map<string, Guid>,
  PermissionsStale: bool, ProfileStale: bool, SettingsStale: bool, GuardPassed: bool, PermissionRows: list<PermissionRow>?, Profile: UserProfile?)
service IUserConnector
  Connect() -> ConnectedUser                              // cache keyed (TenantId, Subject), or a cold prologue-only round trip; TenantNotFoundException for unknown or deactivated
  ConnectAsUser(userId: int) -> ConnectedUser             // job scopes; ConnectPrologue.ForUser
  ConnectAsSystem() -> ConnectedUser                      // ConnectPrologue.System; throws InvalidOperationException unless RequestContext.Kind = System (migrator, provisioning steps, system job scopes) — never reachable from a request scope
  Contribute(batch: IDataBatch, premises: ConnectPremises) -> BatchResult<ConnectResult>   sync   // the prologue contributor at Order 100
  Apply(result: ConnectResult) -> ConnectedUser   sync    // refreshes the cache and RequestContext.UserId
service IGuardedBatchRunner                   // the only way a service executes a batch on a caller's behalf
  Run<TResult>(purpose: BatchPurpose, compose: (ConnectedUser, IDataBatch) -> void, read: (ConnectedUser, BatchOutcome) -> TResult) -> TResult
```

`Run`: connects (cache or cold), composes, executes, reads; on `GuardPassed = false` applies the result (fresh permission rows, fresh profile; a `SettingsStale` triggers the settings reload) and recomposes **once**; a second failure raises `StaleContextException`. Parameters bound by the contributor: `@tm_Subject varchar(255)`, `@tm_Now datetime2(7)`, `@tm_StampActivity bit`, `@tm_AllowFlip bit`, `@tm_ExpectedUserId int`, `@tm_ExpectedPermissionsTag uniqueidentifier`, `@tm_ExpectedUserPermissionsTag uniqueidentifier`, `@tm_ExpectedPreferencesTag uniqueidentifier`, `@tm_ExpectedSettingsTag uniqueidentifier`, `@tm_ReloadPermissions bit`.

```sql
DECLARE @tm_UserId int, @tm_Kind varchar(8), @tm_IsActive bit, @tm_State varchar(8),
        @tm_PreferencesTag uniqueidentifier, @tm_UserPermissionsTag uniqueidentifier,
        @tm_PermissionsTag uniqueidentifier, @tm_SettingsTag uniqueidentifier,
        @tm_PermissionsStale bit = 0, @tm_ProfileStale bit = 0, @tm_SettingsStale bit = 0, @tm_Guard bit = 0;

-- Resolve the caller: one seek on the covering Subject index, one clustered seek on UserStamps.
SELECT @tm_UserId = U.[Id], @tm_Kind = U.[Kind], @tm_IsActive = U.[IsActive], @tm_State = U.[State],
       @tm_PreferencesTag = S.[PreferencesTag], @tm_UserPermissionsTag = S.[PermissionsTag]
FROM [core].[Users] AS U
JOIN [core].[UserStamps] AS S ON S.[UserId] = U.[Id]
WHERE U.[Subject] = @tm_Subject;                                   -- ForUser variant: WHERE U.[Id] = @tm_UserId

-- Tags are read before any permission rows (the safe direction under a racing bump).
SELECT @tm_PermissionsTag = [Tag] FROM [core].[VersionTags] WHERE [Name] = N'permissions';
SELECT @tm_SettingsTag    = [Tag] FROM [core].[VersionTags] WHERE [Name] = N'settings';

IF @tm_UserId IS NOT NULL AND @tm_IsActive = 1
BEGIN
    -- Throttled activity stamp (ForSubject only): matches zero rows on the common request.
    IF @tm_StampActivity = 1
        UPDATE [core].[UserStamps] SET [LastActiveAt] = @tm_Now
        WHERE [UserId] = @tm_UserId AND ([LastActiveAt] IS NULL OR [LastActiveAt] < DATEADD(second, -60, @tm_Now));

    -- First authenticated request on this tenant (ForSubject only; never on a ReadOnly tenant): Invited -> Active, once, ever. Never touches ModifiedAt.
    IF @tm_State = 'Invited' AND @tm_AllowFlip = 1
    BEGIN
        UPDATE [core].[Users] SET [State] = 'Active', [ActivatedAt] = @tm_Now WHERE [Id] = @tm_UserId AND [State] = 'Invited';
        SET @tm_State = 'Active';
    END;

    SET @tm_PermissionsStale = CASE WHEN @tm_ExpectedPermissionsTag IS NULL OR @tm_ExpectedPermissionsTag <> @tm_PermissionsTag
                                      OR @tm_ExpectedUserPermissionsTag IS NULL OR @tm_ExpectedUserPermissionsTag <> @tm_UserPermissionsTag THEN 1 ELSE 0 END;
    SET @tm_ProfileStale     = CASE WHEN @tm_ExpectedPreferencesTag IS NULL OR @tm_ExpectedPreferencesTag <> @tm_PreferencesTag THEN 1 ELSE 0 END;
    SET @tm_SettingsStale    = CASE WHEN @tm_SettingsTag <> @tm_ExpectedSettingsTag THEN 1 ELSE 0 END;
    SET @tm_Guard = CASE WHEN @tm_UserId = @tm_ExpectedUserId AND @tm_PermissionsStale = 0 THEN 1 ELSE 0 END;
END;

-- Result set 0: the connect row. Always present, exactly one row.
SELECT @tm_UserId AS [UserId], @tm_Kind AS [Kind], @tm_IsActive AS [IsActive], @tm_State AS [State],
       @tm_PreferencesTag AS [PreferencesTag], @tm_UserPermissionsTag AS [UserPermissionsTag],
       @tm_PermissionsStale AS [PermissionsStale], @tm_ProfileStale AS [ProfileStale], @tm_SettingsStale AS [SettingsStale], @tm_Guard AS [GuardPassed];

-- Result set 1: every tenant-level tag (the seam 5 prelude, absorbed here on caller batches).
SELECT [Name], [Tag] FROM [core].[VersionTags];

-- Result set 2: the caller's effective permission rows. Present iff PermissionsStale = 1 or a reload was asked.
IF (@tm_PermissionsStale = 1 OR @tm_ReloadPermissions = 1) AND @tm_UserId IS NOT NULL AND @tm_IsActive = 1 AND @tm_Kind <> 'System'
    SELECT P.[Id], P.[RoleId], R.[Name], R.[IsPublic], P.[Resource], P.[Action], P.[Filter], P.[FilterLanguageVersion]
    FROM [core].[Permissions] AS P JOIN [core].[Roles] AS R ON R.[Id] = P.[RoleId]
    WHERE R.[IsActive] = 1
      AND (R.[IsPublic] = 1 OR EXISTS (SELECT 1 FROM [core].[RoleMemberships] AS M WHERE M.[RoleId] = R.[Id] AND M.[UserId] = @tm_UserId));

-- Result set 3: the caller's profile. Present iff ProfileStale = 1.
IF @tm_ProfileStale = 1 AND @tm_UserId IS NOT NULL AND @tm_IsActive = 1
    SELECT [Name], [Name2], [Name3], [Email], [ImageId], [PreferredLanguage], [PreferredCalendar], [PreferredTimeZone]
    FROM [core].[Users] WHERE [Id] = @tm_UserId;

-- Cold settings load (0012 prologue contributor at Order 60), present iff the settings cache missed: the two statements of seam 22.

IF @tm_Guard = 1
BEGIN
    -- the batch body (§1.6 for Persist)
END;
```

Reader contract: sets 0 and 1 always; set 2 iff `PermissionsStale = 1` or a reload was requested (active non-system caller); set 3 iff `ProfileStale = 1`; the cold settings sets iff requested; the body's sets iff `GuardPassed = 1`. Variants: `ForUser` (by `U.[Id]`, no stamp, no flip; job scopes), `System` (no user lookup; tags only; `@tm_Guard = 1`; `UserAccess.System`). Nothing in the prologue is inside a transaction. A `settings` mismatch never fails the guard: the result is served and the settings cache refreshes. Failure modes: unknown subject or `IsActive = 0` → `TenantNotFoundException` (the cookie is untouched); stale permissions → one transparent recompose; stale profile → set 3 refreshes the cache; two guard failures → `StaleContextException` 503. Cold path: one prologue-only round trip per user per instance.

---

## 17. Seam 17 — Vocabulary

Ledger §2. Names that recur below and are final: `IDataBatch`, `SaveEmitter`, `DataBatch`, `IIdAllocator`, `EntityService`, `StackDescriptor`, `IStackRegistry`, `IAccessEvaluator`, `AccessDecision`, `ISecurableRegistry`, `IUserConnector`, `IGuardedBatchRunner`, `VersionTag`, `VersionedCache`, `TenantSettings`, `SettingKey<T>`, `RequestContext`, `ITenantScopeFactory`, `IBlobService`, `IBlobStore`, `INotifier`, `NotificationRequest`, `IJobQueue`, `JobRequest`, `IJobHandler`, `TellmaHub`, `IClientEventPublisher`, `IIdentityServerClient`, `ITenantProvisioningStep`, `ITenantBootstrapper`, `TellmaException`, `TellmaSqlErrors`, `DataAccessScope`.

---

## 18. Seam 18 — Provisioning steps and the bootstrap (owner 0010; consumers 0013, 0017)

```contract
// Tellma.Core.Abstractions.Tenancy
record TenantProvisioningContext(Tenant: TenantDescriptor, Services: IServiceProvider, IsNew: bool, AdminSubject: string?, AdminEmail: string?)
contract ITenantProvisioningStep              // registered through FeatureContribution.ProvisioningStep<T>(); runs in a System tenant scope through the pipeline
  Name: string                                // key in dbo.__TellmaProvisioning; platform "core.<step>", packs "<module>.<step>", distributions "<slug>.<step>"
  Order: int                                  // platform 0–99, packs 100–199, distributions 200+
  Version: int                                // the step re-runs when it exceeds the recorded version
  RunAsync(context: TenantProvisioningContext)
```

Platform steps: `10` `core.bootstrap-administrator` (`ITenantBootstrapper.BootstrapAdministrator` from `AdminEmail`/`AdminSubject`; `Tellma:Seed:AdminEmail` in Development, `admin@localhost` with the fixed dev subject; `provision --admin-email|--admin-subject` otherwise; a deployed admin is then invited through `UserService.Invite` in the migrator's system scope), `20` `core.blob-container`; GL: `100` `gl.sample-centers` (versioned). `core.VersionTags` needs no step: the migrator seeds it from the registry on every `migrate` (seam 5). Migrator commands: `migrate [--tenant <id> | --all-tenants]`, `provision --tenant <id> [--admin-email | --admin-subject]`, `set-state --tenant <id> --state <state> [--reason]`, `status`. Table in every tenant database: `dbo.__TellmaProvisioning (Step nvarchar(128) PK, Version int NOT NULL, CompletedAt datetime2(3) NOT NULL, PlatformVersion nvarchar(64) NOT NULL)`. `HasData` is used only for the reserved band: system user, Administrator role, permission and membership, the `UserStamps` row, the `core.Settings` placeholder row (`Id = 1`, `Name` = tenant name, `PrimaryLanguage` = the distribution default), built-in schedules and their `ScheduleStates`, `JobWorkerState`. Role and grants recomputed by the migrator after every run:

```sql
IF DATABASE_PRINCIPAL_ID('tellma_app') IS NULL CREATE ROLE [tellma_app];
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[<S>] TO [tellma_app];         -- every mapped schema
GRANT UPDATE ON OBJECT::[<S>].[sq_<Table>] TO [tellma_app];                     -- every sequence
-- EXECUTE ON TYPE is emitted by the table-type migrations (spec 0001)
-- provisioning only: CREATE USER [<identity>] FROM EXTERNAL PROVIDER (SaaS) or FOR LOGIN [tellma_app] (on-prem); ALTER ROLE [tellma_app] ADD MEMBER …;
-- provisioning only, fresh database: ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON;
EXEC sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 600000;   -- tenant migration lock
```

---

## 19. Seam 19 — Excel operations and the row source (owner 0018; consumers 0014, 0015)

```contract
// Tellma.Core.Abstractions.Excel
enum ImportMode = Insert | Update | Upsert
data ExportSourceRequest                      // either Ids or the clauses, never both
  Ids: list<long>?   Filter: string?   OrderBy: string?   Search: string?   Arguments: map<string, JSON>?   Background: bool = false
data ExportRequest : ExportSourceRequest      // display shape
  Select: string   required                   // exactly as the grid uses it
  Headers: list<string>?                      // parallel to the select items; derived when null
data ExportForImportRequest : ExportSourceRequest   // editable shape
  Columns: list<string>?                      // subset of editable property paths; all when null
  IncludeChildren: bool = true
  ReferenceKeys: map<string, string>          // FK property -> target property (CenterId -> Code)
record ExportOutcome(Workbook: Stream?, FileName: string?, Rows: int?, JobId: int?, ExportId: int?)
record ImportColumnMapping(Sheet: string, Column: string, Path: string?)   // Column = header text or letter; Path null = ignore
record ImportSheetMapping(Sheet: string, Collection: string?)              // "" = parent sheet; null = ignore
data InspectImportRequest
  FileId: int   required                      // a Staged blob of kind import-file
  SheetMappings: list<ImportSheetMapping> = []
  ColumnMappings: list<ImportColumnMapping> = []
data ImportRequest
  FileId: int   required
  Mode: ImportMode = Insert
  RowKey: string?                             // natural-key path or "Id"; required for Update/Upsert unless the manifest supplies one
  SheetMappings: list<ImportSheetMapping> = []
  ColumnMappings: list<ImportColumnMapping> = []
  IgnoreUnmappedColumns: bool = false
  Concurrency: ConcurrencyMode = Check        // the expected stamp is the sheet's Stamp column when present, else the ModifiedAt read at hydration; Override only when the caller says so
  Background: bool = false
  Atomic: bool = false                        // background only: one transaction for the whole file
record ImportPlan(Entity: string?, ManifestVersion: int, ManifestPresent: bool, SameSource: bool, SchemaFingerprintMatches: bool, Sheets: list<ImportPlanSheet>,
  RowKeyCandidates: list<string>, DefaultRowKey: string?, TotalRows: long, RequiresBackground: bool, Warnings: list<ImportError>)
record ImportPlanSheet(Sheet: string, Collection: string?, Status: ImportSheetStatus, Rows: long, Columns: list<ImportPlanColumn>)
record ImportPlanColumn(Column: string, Header: string, Path: string?, Status: ImportColumnStatus, Language: string?, ReferenceKey: string?, Suggestions: list<string>)
enum ImportSheetStatus = Parent | Child | Manifest | Ignored
enum ImportColumnStatus = Mapped | Unmapped | Ignored | Duplicate | ServerOwned
record ImportError(Sheet: string?, Row: int?, Column: string?, Header: string?, Path: string?, Code: string, Arguments: map<string, string>)   // extends ValidationError with coordinates
record ImportOutcome(Inserted: int, Updated: int, ChildrenInserted: int, ChildrenUpdated: int, ChildrenDeleted: int, CommittedRowRanges: list<(From: int, To: int)>, Warnings: list<ImportError>, JobId: int?, ImportId: int?)
data ExcelOptions                             // Tellma:Excel
  MaxSynchronousExportRows: int = 100000   MaxExportRows: int = 1048575   MaxSynchronousImportRows: int = 10000   MaxAtomicImportRows: int = 100000
  ImportChunkRows: int = 10000   MaxSynchronousImportFileBytes: long = 16 MB   MaxImportFileBytes: long = 256 MB   MaxImportUncompressedBytes: long = 2 GB
  MaxInMemorySharedStringBytes: long = 32 MB   MaxReportedErrors: int = 1000   ExportFileRetentionDays: int = 7   ImportFileRetentionDays: int = 7
data ExcelErrorCodes                          // constants: Excel.Export.RowLimitExceeded, Excel.Import.TooLargeForSynchronous, Excel.Import.UnmappedColumn, Excel.Import.DuplicateColumn,
  // Excel.Import.ServerOwnedColumn, Excel.Import.UnconfiguredLanguageColumn (warning), Excel.Import.WriteOnceChanged, Excel.Import.RowNotFound, Excel.Import.DuplicateRowKey,
  // Excel.Import.IdKeyRequiresSameTenant, Excel.Import.SurrogateReferenceFromOtherTenant, Excel.Import.ReferenceNotFound, Excel.Import.ReferenceAmbiguous, Excel.Import.KeyTooLong,
  // Excel.Import.OrphanChildRow, Excel.Import.ParentCycle, Excel.Import.InvalidCell, Excel.Import.ConcurrencyConflict, Excel.Import.PackageTooLarge, Excel.Import.MalformedWorkbook
data ExcelTelemetryNames                      // constants; MeterName = "Tellma.Core"; tellma.excel.*
data Export : TopLevelEntity, IJobEntity      // table core.Exports; [Stack(Operations = Query | Details | Delete)]; stack resource core.Export (self-scope CreatedById = me()); every column [ServerOwned]; rows written only by ExcelOperations and the handlers with SaveSource = System
  Resource: string   Kind: ExportKind   RequestJson: string?   FileName: string?   FileId: int?   RowCount: int?   ExpiresAt: datetime2(3)
enum ExportKind = Display | ForImport
data Import : TopLevelEntity, IJobEntity      // table core.Imports; [Stack(Operations = Query | Details | Delete)]; stack resource core.Import (self-scope); every column [ServerOwned]; same writers
  Resource: string   Mode: ImportMode   FileId: int?   ResultFileId: int?   RowCount: int?   ErrorCount: int?   ExpiresAt: datetime2(3)

// Tellma.Core.Excel (runtime; the codec is pure — it never opens a connection)
record ExcelQuery(RootEntity: string, SelectPaths: list<string>, OrderBy: string?, Restriction: KeySetRestriction?, RestrictionValues: list<object>?, Take: int?)
contract IExcelRowSource                      // implemented by 0014 over IDataBatch.Rows with the caller's read filter
  Rows(query: ExcelQuery) -> stream<list<object?>>
record ExcelContext(TenantId: int, DistributionSlug: string, TenantLanguages: list<string>, Culture: CultureInfo, Calendar: ICalendarSystem, TimeZone: TimeZoneInfo, Labels: ILabelProvider)
record ExcelColumnSpec(Header: string, Type: QueryexType, Scale: int?, Path: list<string>?)
record ExcelWorkbookPlan(Entity: EntityMetadata, Shape: ExportKind, Sheets: list<ExcelSheetPlan>, Manifest: map<string, string>)   // the codec's immutable plan; Write streams from it
record ExcelSheetPlan(Name: string, Collection: string?, Columns: list<ExcelColumnSpec>, NumberFormats: list<string?>, Query: ExcelQuery)
service IExcelExporter
  PlanDisplay(entity: EntityMetadata, columns: list<ExcelColumnSpec>, context: ExcelContext) -> ExcelWorkbookPlan   sync
  PlanEditable(entity: EntityMetadata, request: ExportForImportRequest, context: ExcelContext) -> ExcelWorkbookPlan   sync
  Write(plan: ExcelWorkbookPlan, rows: IExcelRowSource, destination: Stream) -> int
service IExcelImporter
  Inspect(workbook: Stream, entity: EntityMetadata, request: InspectImportRequest, context: ExcelContext) -> ImportPlan
  Parse<TEntity>(workbook: Stream, entity: EntityMetadata, request: ImportRequest, context: ExcelContext) -> ExcelImportSession<TEntity>
contract ExcelImportSession<TEntity>
  Lookups: list<ExcelQuery>                   // deduplicated by (entity, key path)
  RowCount: int                               // parent rows in topological order
  Resolve(fromRow: int, toRow: int, lookupRows: map<ExcelQuery, list<list<object?>>>, allocateIds: (int) -> list<object>) -> ExcelChunk<TEntity>   sync
record ExcelChunk<TEntity>(Entities: list<TEntity>, CoordinateMap: map<string, ExcelCell>, Errors: list<ImportError>)
record ExcelCell(Sheet: string, Row: int, Column: string)
service ExcelOperations<TEntity>              // contributed as [ApiAction]s on every stack with Query (export) or Save (import); each names its Action explicitly
  Export(request: ExportRequest) -> ExportOutcome                        // [ApiAction] "export", Action = "Read"
  ExportForImport(request: ExportForImportRequest) -> ExportOutcome      // [ApiAction] "export-for-import", Action = "Read"
  InspectImport(request: InspectImportRequest) -> ImportPlan             // [ApiAction] "inspect-import", Action = "Save"
  Import(request: ImportRequest) -> ImportOutcome                        // [ApiAction] "import", Action = "Save"
```

Hydration and concurrency: for `Update`/`Upsert` the pipeline hydrates each existing row through `GetByIdsAsync` and overlays the sheet's columns; the hydrated `ModifiedAt` is the expected stamp unless the sheet carries `Stamp`, so a concurrent edit between hydration and persist — to any column, mapped or not — is a `Excel.Import.ConcurrencyConflict` on that row, never a silent overwrite; `Override` applies only when the request says `Concurrency = Override`. Import saves run with `ReturnEntities = false`.

Tables: **`core.Exports`** (`TopLevelEntity`, `IJobEntity`; UDTT; `core.sq_Exports`): `Id int PK`; `Resource varchar(128) NOT NULL`; `Kind varchar(9) NOT NULL`; `RequestJson nvarchar(max) NULL`; `FileName nvarchar(256) NULL`; `FileId int NULL FK_Exports_FileId → core.Blobs` (`[BlobReference("export-file", Attachment, ReadAccess = OwnerRead)]`); `RowCount int NULL`; `ExpiresAt datetime2(3) NOT NULL`; `JobId` per §1.7; audit set; `IX_Exports_CreatedBy (CreatedById, CreatedAt DESC)`, `IX_Exports_ExpiresAt (ExpiresAt)`. **`core.Imports`** (same shape; `core.sq_Imports`): `Resource`; `Mode varchar(8)`; `FileId int NULL` (`import-file`); `ResultFileId int NULL` (`import-result`); `RowCount`, `ErrorCount int NULL`; `ExpiresAt`; `JobId`; audit; the same two indexes. `core.file-retention` deletes expired rows through the pipeline (blobs released, swept later). The web layer streams `ExportOutcome.Workbook` as `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` with `Content-Disposition` (`filename` ASCII + `filename*` UTF-8); `Background = true` returns 202 `JobAccepted(JobId, ExportId | ImportId)`; a background job's `Jobs.ArgumentsJson` carries the request plus the negotiated culture, calendar and zone; progress and the chunk index through `IJobProgress.Append`. The manifest sheet `_tellma` and the data-sheet layout are 0018 §4 as written (`KeyKind ∈ Natural | Surrogate`, `Stamp` = `ModifiedAt`'s wire string, `[BlobReference]` columns excluded), with `IsActive` in the editable shape: it is settable on `Insert` and on the insert half of `Upsert`, and a changed value on `Update` or on the update half of `Upsert` is the `WriteOnce` error reported with the sheet coordinates; the display export includes it as before.

---

## 20. Seam 20 — Session and tenant-state listeners, the hub (owners 0010 and 0020)

`ITenantStateListener` and `ISessionTerminationListener` (seam 9) are implemented by `TellmaHub`'s connection tracker in `Tellma.Core.AspNetCore` and, for state changes, by the job worker (pause polling for non-`Active` tenants). `UserService.AfterCommitAsync` calls `TenantAccessRevokedAsync(tenantId, subject)` after a deactivation commits (hub connections closed; the cookie is untouched — the user may belong to another tenant); `ICatalogSessionStore` calls `SessionsTerminatedAsync` on back-channel logout and revocation. Hub: cookie scheme at connect; `IUserIdProvider` returns `sub`; groups `t{tenantId}` and `t{tenantId}.u{userId}` assigned server-side; `CloseOnAuthenticationExpiration = true`; Azure SignalR when `Azure:SignalR:ConnectionString` is configured, Redis backplane (`AddStackExchangeRedis`, channel prefix `DeploymentIdentity.DeploymentId`) for on-prem multi-instance; events per seam 15.

---

## 21. Seam 21 — The identity-server client and the Core/GL stacks (owner 0017; consumers 0010, 0013)

```contract
// Tellma.Core.Abstractions.Identity
service IIdentityServerClient                 // one per distribution; machine-to-machine; never inside a transaction
  InviteAsync(invitations: list<IdentityInvitation>) -> list<IdentityInvitationResult>          // chunks at 1,000; input order; prefix on abort
  GetInvitationDeliveryAsync(subjects: list<string>) -> list<IdentityDeliveryStatus>
record IdentityInvitation(Email: string, DisplayName: string?, Locale: string?, Gender: Gender?, ReturnUrl: string?, ExistingOnly: bool = false)
  // ExistingOnly: get-by-email without ever sending mail — Active for a known subject, a per-user error for an unknown one (spec 0003 amendment); set on sandbox tenants
record IdentityInvitationResult(Email: string, Subject: string?, Status: InviteStatus?, Error: string?)
record IdentityDeliveryStatus(Subject: string, State: IdentityDeliveryState, ExpectsDeliveryEvents: bool, SentUtc: DateTimeOffset?, UpdatedUtc: DateTimeOffset?, Reason: string?)
enum IdentityDeliveryState = NotFound | Pending | Sent | Delivered | Bounced | Complained | Rejected | Abandoned | Accepted
data IdentityServerClientOptions              // Tellma:Identity; validated at startup
  Authority: Uri   required                   // issuer origin; also the token's resource
  ClientId: string   required                 // "<slug>-svc"
  ServiceClientSecret: string   required      // Key Vault reference in SaaS; shared by the web host and the migrator
  PathBase: string = ""                       // in-proc identity host

// Tellma.Core.Abstractions.Access — results of the user service's actions
record InviteResult(Id: int, Status: InviteStatus?, Error: string?)
record InvitationStatus(Id: int, State: UserState, InviteStatus: InviteStatus?, LastInviteError: string?, Delivery: IdentityDeliveryStatus?)
record KeyValueItem(Key: string, Value: string)
record PreferencesResult(Preferences: map<string, string>, PreferencesTag: string)
enum TestNotificationChannel = Email | Sms
enum TestNotificationResult = Sent | NoContactAddress | NotConfigured
data UserInvitationOutcome                    // [TableType] standalone -> UserInvitationOutcomeList; the invite write-back row (UserId key, Subject, InviteStatus, LastInviteError)
data UsersTelemetryNames                      // constants; MeterName = "Tellma.Core"; Invites = "tellma.users.invites"; IdentityCalls = "tellma.identity.calls"; IdentityCallDuration = "tellma.identity.call.duration"; OutcomeTag = "outcome"

// Tellma.Core.Users (runtime)
service UserService<TUser> : EntityService<TUser>   where TUser: User      // the users stack
  Invite(context: ActionContext<TUser, int>) -> list<InviteResult>
  GetInvitationStatus(request: IdsRequest) -> list<InvitationStatus>
  Me() -> MeResult
  SaveMe(request: SaveRequest<TUser>) -> EntitiesResult<TUser>
  SetMyPreferences(items: list<KeyValueItem>) -> PreferencesResult
  DeleteMyPreferences(keys: list<string>) -> PreferencesResult
  SendTestNotification(channel: TestNotificationChannel) -> TestNotificationResult
service RoleService<TRole> : EntityService<TRole>   where TRole: Role    // plugs RoleAccessRules<TRole>; validates filters with QueryexEngine.Validate and stamps FilterLanguageVersion on new or changed text
service AccessService                         // [ApiRoute("access")]
  Check(request: AccessCheckRequest) -> list<AccessDecision>

// Tellma.Module.Gl.Abstractions — namespace Tellma.Module.Gl
enum CenterType = Abstract | BusinessUnit | Service | Operation | Sale   // only Abstract and BusinessUnit may have children (Centers.ParentMustBeGrouping)
data Center : ActivatableTreeEntity           // non-abstract, unsealed default leaf; no generic base, no CLR Parent; [Table("Centers", Schema = "gl")], [TableType], [ApiResource]
  CenterType: CenterType
  Name: string                                [Multilingual], [Searchable]
  Name2: string?   Name3: string?
  Code: string                                [NaturalKey], [Searchable]
data GlResources                              // constants: Center = "gl.Center"
// Tellma.Module.Gl
service CenterService<TCenter> : EntityService<TCenter>   where TCenter: Center   // adds the parent-type validator; a distribution's leaf is MyCenter : Center registered with tellma.UseEntity<Center, MyCenter>()
service GlFeature : ITellmaFeature            // [Requires<CoreFeature>]; Name "gl"; contributes Entity<Center, CenterService<Center>>(), Securables, ProvisioningStep<GlSampleCentersStep>()
```

| Member | Annotation |
|---|---|
| `UserService.Invite` | `[EntityAction]` `invite`, permission `Invite`, idempotent |
| `UserService.GetInvitationStatus` | `[ApiAction]` `invitation-status`, action `Read` |
| `UserService.Me` | `[ApiAction]` `me`, member-only, idempotent |
| `UserService.SaveMe` | `[ApiAction]` `me/save`, member-only; runs the pipeline with `SaveOptions.SelfService = true`: `[SelfEditable]` columns only, every child collection forced to `null` before preprocessing (a payload carrying `RoleMemberships` never grants or strips anything), any id other than the caller's is `Users.SelfServiceOnly` (422); the epilogue bumps `PreferencesTag` through the declared attribute |
| `UserService.SetMyPreferences` / `DeleteMyPreferences` | `[ApiAction]` `me/preferences/set` / `me/preferences/delete`, member-only; the seam 11.4 statement with `SqlOptions.ForCaller` |
| `UserService.SendTestNotification` | `[ApiAction]` `me/test-notification`, member-only |
| `AccessService.Check` | `[ApiAction]` `check`, member-only, idempotent; another user's decisions require both `core.User × Read` on that user and `core.Role × Read` (effective grants are role-editor information) |

`Invite` flow: rows loaded under the grant; `Email` normalised and locked; `IIdentityServerClient.InviteAsync` after the validated load, with `ExistingOnly = true` when `RequestContext.IsSandbox` (a sandbox tenant never causes the identity server to email anyone; an unknown email is `Users.SandboxRequiresExistingIdentity` on that row); write-back through `Sql` with `UserInvitationOutcomeList` (`State = 'Invited'`, `Subject`, `InvitedAt`, `InviteStatus`, `LastInviteError`, `ModifiedAt` stamped; declares `Writes = core.Users` with `UserIds` = the invited ids so the epilogue bumps their `PreferencesTag`; a differing existing subject → `Users.SubjectMismatch`); `Status = Active` sends the `core.user.added` notice through `INotifier` and an `IEmailSender` send after commit; a partial identity-server failure → `PartialFailureException` 502 with the per-user results. Test: a `SaveMe` carrying a membership row is a 422, never a grant.

**`gl.Centers`** — `ActivatableTreeEntity`; non-temporal; UDTT `CentersList` excludes `Node`; `gl.sq_Centers`:

| Column | Type | Null | Constraints |
|---|---|---|---|
| `Id` | `int` | no | PK clustered |
| `ParentId` | `int` | yes | `FK_Centers_ParentId → gl.Centers(Id)`; `IX_Centers_ParentId` |
| `CenterType` | `varchar(12)` | no | |
| `Name` | `nvarchar(255)` | no | |
| `Name2`, `Name3` | `nvarchar(255)` | yes | |
| `Code` | `nvarchar(50)` | no | `UX_Centers_Code` |
| `IsActive` | `bit` | no | `DF 1` |
| `Node` | `hierarchyid` | no | `UX_Centers_Node`; shadow |
| `SubtreeCount`, `ActiveSubtreeCount` | `int` | no | `DF 1` |
| audit set | | no | FKs → `core.Users` |

---

## 22. Seam 22 — Settings, labels, calendars, negotiation (owner 0012; consumers 0014, 0015, 0017, 0018)

```contract
// Tellma.Core.Abstractions.Settings
enum SettingScope = Tenant                    // the only scope; user preferences are the opaque bag
enum SettingVisibility = Server | Client
enum MultilingualShape = Primary | PrimaryAndSecondary | All   // 1 | 2 | 3; the Queryex schema key
base SettingKey
  Name: string                                // dotted camelCase, ≥ 2 segments, ≤ 128 chars ("gl.posting.autoNumberOnPost")
  Category: string                            // default: first segment; securable core.Settings.<Category> × Save
  Scope: SettingScope   Visibility: SettingVisibility   ValueType: Type   DefaultJson: JsonElement
  LabelKey: string                            // "Setting_" + Name with '.' -> '_'
record SettingKey<T> : SettingKey             // declared once as a static field; registered through FeatureContribution.SettingKeys(typeof(Holder))
  ctor(name: string, defaultValue: T, category: string? = null, visibility: SettingVisibility = Server, validate: ((T) -> string?)? = null, typeInfo: JsonTypeInfo<T>? = null)
  DefaultValue: T   Validate: ((T) -> string?)?
service ISettingKeyRegistry
  Keys: list<SettingKey>   Find(name: string) -> SettingKey?   sync   Categories: list<string>
record TenantSettings                         // immutable; cached under the settings tag
  TenantId: int   Tag: VersionTag   Names: list<string?>   Languages: list<LanguageInfo>   Shape: MultilingualShape
  Calendars: list<ICalendarSystem>   TimeZone: TimeZoneInfo   SqlServerTimeZoneName: string   ModifiedAt: DateTime   Entries: map<string, JsonElement>
  Get<T>(key: SettingKey<T>) -> T   sync
  Today(clock: TimeProvider) -> DateOnly   sync
record TenantSettingsForClient(FormatVersion: int = 1, TenantId: int, Names: list<string?>, Languages: list<LanguageInfo>, Calendars: list<string>, TimeZone: string, Entries: map<string, JsonElement>)
service ITenantSettingsCache
  GetAsync(tenantId: int) -> CacheResult<TenantSettings>
  GetForClientAsync(tenantId: int) -> (Json: bytes, WireTag: string)
  Peek(tenantId: int) -> TenantSettings?   sync
record TenantSettingsPatch(Fields: list<string>, Name: string?, Name2: string?, Name3: string?, PrimaryLanguage: string?, SecondaryLanguage: string?, TernaryLanguage: string?,
  PrimaryCalendar: string?, SecondaryCalendar: string?, TimeZone: string?, Entries: map<string, JsonElement?>?, ExpectedModifiedAt: DateTime)   // field mask; a listed null clears
data Settings : TopLevelEntity                // table core.Settings; sealed; [Temporal], [TableType], [BumpsVersionTag("settings")], [Multilingual] Name
  TenantId: int   Name: string   Name2: string?   Name3: string?   PrimaryLanguage: string   SecondaryLanguage: string?   TernaryLanguage: string?
  PrimaryCalendar: string   SecondaryCalendar: string?   TimeZone: string
data SettingEntry : TopLevelEntity            // table core.SettingEntries; [Temporal], [TableType], [BumpsVersionTag("settings")]
  Key: string   [Unique]                      Value: string   [JsonColumn]
service SettingsService                       // [ApiRoute("settings")]
  Client(ifTag: string?) -> (Json, WireTag) | unchanged
  EntityTags() -> map<string, string>
  Save(patch: TenantSettingsPatch) -> TenantSettingsForClient
  RefreshCaches()

// Tellma.Core.Abstractions.Localization
record LanguageInfo(Code: string, EnglishName: string, NativeName: string, Symbol: string, IsRightToLeft: bool)
service ILanguageCatalog
  All: list<LanguageInfo>   Offered: list<LanguageInfo>   Find(code: string) -> LanguageInfo?   sync   IsOffered(code: string) -> bool   sync
data CoreStrings                              // constants: BaseName = "Tellma.Core.Resources.Strings"; Assembly = "Tellma.Core"
service ILabelProvider
  EntityLabel(entityType: Type) -> string   sync
  PropertyLabel(entityType: Type, propertyName: string) -> string   sync   // twins carry " (E)" / " (ع)"
  SettingLabel(key: SettingKey) -> string   sync
  EnumValueLabel(enumType: Type, value: string) -> string   sync
record LocalizationContext(Culture: CultureInfo, Calendar: ICalendarSystem, TimeZone: TimeZoneInfo, ContentLanguageIndex: int)   // copied onto RequestContext by the initializer at Order 200
record NegotiationInput(AcceptLanguage: string?, CalendarHeader: string?, TimeZoneHeader: string?, PreferredLanguage: string?, PreferredCalendar: string?, PreferredTimeZone: string?, Settings: TenantSettings)
service ILocalizationNegotiator
  Negotiate(input: NegotiationInput) -> LocalizationContext   sync   // header -> preference -> tenant -> distribution default; extensions stripped; never throws
data LocalizationHeaders                      // constants: Calendar = "Tellma-Calendar"; TimeZone = "Tellma-Time-Zone"; VersionTags = "Tellma-Version-Tags"
data CacheTelemetryNames                      // constants; MeterName = "Tellma.Core"; tellma.cache.requests/load.duration/entries/size/evictions; tellma.versiontags.bumps/stale/missing; tellma.settings.entries.invalid
  // tags: cache.kind ∈ settings | preferences | permissions | entities; cache.outcome; cache.reason; tag.kind; stale.phase
data LocalizationTelemetryNames               // constants; Missing = "tellma.localization.missing"; HeadersRejected = "tellma.localization.headers.rejected"; CultureTag = "culture"; HeaderTag = "header"

// Tellma.Core.Abstractions.Calendars
data CalendarCodes                            // constants: Gregorian = "gc"; UmAlQura = "uq"; Ethiopian = "et"
enum DateStyle = Short | Medium | Long | Full
contract ICalendarSystem
  Code: string   EnglishName: string   Calendar: System.Globalization.Calendar   MonthCount: int   MinSupported: DateOnly   MaxSupported: DateOnly
  Decompose(date: DateOnly) -> (Year: int, Month: int, Day: int)   sync
  Format(date: DateOnly, culture: CultureInfo, style: DateStyle) -> string   sync
  Format(instant: DateTimeOffset, zone: TimeZoneInfo, culture: CultureInfo, style: DateStyle, includeTime: bool = true) -> string   sync
  MonthName(month: int, culture: CultureInfo, abbreviated: bool) -> string   sync
  TryParse(text: string, culture: CultureInfo) -> DateOnly?   sync
service ICalendarRegistry
  Codes: list<string>   this[code: string] -> ICalendarSystem   sync   TryGet(code: string) -> ICalendarSystem?   sync
```

| Member | Annotation |
|---|---|
| `SettingsService.Client` | `[ApiAction]` `client`, member-only, idempotent |
| `SettingsService.EntityTags` | `[ApiAction]` `entity-tags`, member-only, idempotent |
| `SettingsService.Save` | `[ApiAction]` `save`, resource `core.Settings.General`, action `Save` for the typed members; `core.Settings.<Category> × Save` checked in code per entry; calls `batch.BumpVersionTag("permissions")` only when a language column changed (the Queryex schema shape follows the languages), so an ordinary settings edit never cold-paths every user |
| `SettingsService.RefreshCaches` | `[ApiAction]` `refresh-caches`, resource `core.Settings.General`, action `Save`, idempotent; sensitive; bumps every tag |

`IcuStringLocalizerFactory` (`Tellma.Core.Localization`) decorates the framework factory with ICU `MessageFormat`; resources `Resources/Strings.resx` per package, keys `<Entity>_<Property>`, `Setting_<key>`, validation codes; satellites ship with the owning package; fallback: culture parents → English; content falls back to the primary column; missing strings metered, never blank.

Tables: **`core.Settings`** (single row `Id int PK CK (Id = 1)`, no sequence; `[Temporal]` → `core.SettingsHistory`; UDTT `SettingsList`): `TenantId int NOT NULL`; `Name nvarchar(255) NOT NULL`; `Name2`, `Name3 nvarchar(255) NULL`; `PrimaryLanguage varchar(35) NOT NULL`; `SecondaryLanguage`, `TernaryLanguage varchar(35) NULL`; `PrimaryCalendar varchar(16) NOT NULL DF 'gc'`; `SecondaryCalendar varchar(16) NULL`; `TimeZone varchar(64) NOT NULL`; audit set; period. `CK_Settings_Languages`: `([SecondaryLanguage] IS NULL OR [SecondaryLanguage] <> [PrimaryLanguage]) AND ([TernaryLanguage] IS NULL OR ([SecondaryLanguage] IS NOT NULL AND [TernaryLanguage] <> [PrimaryLanguage] AND [TernaryLanguage] <> [SecondaryLanguage]))`; `CK_Settings_Calendars`: `([SecondaryCalendar] IS NULL OR [SecondaryCalendar] <> [PrimaryCalendar])`. **`core.SettingEntries`** (`[Temporal]` → `core.SettingEntriesHistory`; UDTT; `core.sq_SettingEntries`): `Id int PK`; `Key nvarchar(128) NOT NULL UX_SettingEntries_Key`; `Value nvarchar(max) NOT NULL`; audit; period.

```sql
-- Cold settings load (prologue contributor at Order 60, after the tag read; the loader asserts TenantId = the routed tenant, else a 500)
SELECT [Id], [TenantId], [Name], [Name2], [Name3], [PrimaryLanguage], [SecondaryLanguage], [TernaryLanguage],
       [PrimaryCalendar], [SecondaryCalendar], [TimeZone], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]
  FROM [core].[Settings];
SELECT [Key], [Value] FROM [core].[SettingEntries];
```

---

## 23. Per-spec contract matrix

| Spec | Defines (owner) | Consumes |
|---|---|---|
| **0010** host-tenancy | Seam 9 (`RequestContext` and every `.Tenancy` type, `ITenantScopeFactory`, `ITenantRegistry`, `ITenantCatalog`, connection factory/provider, `ITenantAccessGuard`, listeners), seam 6 (`ITellmaFeature`, `FeatureDeclaration`, `FeatureContribution` and every item record, `TellmaBuilder`, gates), seam 18 (`ITenantProvisioningStep`, `TenantProvisioningContext`, `dbo.__TellmaProvisioning`, migrator commands, role/grants), seam 13's host surface (`TellmaEndpoints`, `TellmaAuthentication`, `TellmaPolicies`, `TenantEndpointMetadata`, `DeployableEndpointMetadata`, `AcceptsBinaryMetadata`, `ICatalogSessionStore`, `StepUpChallenge`, `DistributionInfo`, `RequireAssuranceMetadata`), `TenantNotFoundException`/`TenantUnavailableException`/`StepUpRequiredException` (types in seam 10's set), `Tellma:*` configuration, catalog tables, `TenancyTelemetryNames`, `HostTelemetryNames` | seam 16 (`IUserConnector` for the connect initializer), seam 22 (`ILocalizationNegotiator`, `TenantSettings`), seam 11 (`ITenantBootstrapper`), seam 21 (`IIdentityServerClient` in the migrator), seam 20 (`TellmaHub` as listener); hosts `SessionSweepService` (the catalog session sweep timer) |
| **0011** data-access | Seam 1 (every `.Data` type, `SaveEmitter` rules, tree/delete statements, standard column sets, standalone types, `DataAccessScope`, `DataTelemetryNames`, `TellmaSqlErrors`, internal data exceptions, analyzers), seam 2 (`.Entities` bases, `IActivatable`, `IJobEntity`, every annotation except `[Stack]`/`[ApiResource]`/`[DefaultSelect]`/`[RelatedSelect]`/`[DetailsExpand]`/`[EntityAction]`/`[ApiAction]`/`[ApiRoute]`, `EntityMetadata` family, `QueryRowSet`/`QueryColumn`/`QueryColumnKind`/`RelatedEntities` placement), seam 4 (`IQueryexSchemaProvider`, `KeySetRestriction`, engine amendments), seam 7 (natural keys), seam 14 (meter policy), fixture tables | seam 9 (`RequestContext`, `ITenantConnectionProvider`), seam 5 (`VersionTagDependency`, `VersionTagSnapshot`, `IVersionTagRegistry` for the epilogue), seam 16 (the prologue contributor), seam 12 (`BlobKindPolicy` in metadata), seam 8 (`IJobEntity` column set) |
| **0012** settings-cache-l10n | Seam 5 (every `.Caching` type, `VersionTagList`, `core.VersionTags`, `core.UserStamps` tag columns, prelude/guard/bump/seed statements, `VersionedCache`, `ICacheableEntities`, `TellmaCacheOptions`, `CacheTelemetryNames`), seam 22 (every `.Settings`, `.Localization`, `.Calendars` type, `Settings`/`SettingEntry` entities and tables, `SettingsService`, `LocalizationHeaders`, `LocalizationTelemetryNames`, `IcuStringLocalizerFactory`), `MultilingualShape`, `[Multilingual]` semantics, `[Cacheable]`/`[BumpsVersionTag]`/`[BumpsUserVersionTag]` semantics, `StaleContextException` type | seam 1 (`IDataBatch`, `IDataBatchContributor`, `Sql`, `FromCache`, `DependsOn`), seam 9 (`IRequestContextInitializer` at 200, `RequestContextInputs`), seam 16 (`ConnectResult.SettingsStale`), seam 6 (`SettingKeys`, `Calendar<T>`, `Languages`, `AddLanguage`), seam 11 (`core.Settings.*` securables), seam 3 (`[ApiRoute("settings")]`) |
| **0013** users-roles-permissions | Seam 11 (every `.Access` type: enums, `WellKnownIds`, `AccessActions`, `CoreResources`, securables registry and descriptors, endpoint metadata records, evaluation types, `IAccessEvaluator`, `IAccessGuards`, `UserAccessRules`/`RoleAccessRules`, `IAdministratorDirectory`, `ITenantBootstrapper`, `TenantBootstrapRequest`, `AccessOptions`, `AccessTelemetryNames`, validation and guard codes), the `User`/`RoleMembership`/`Role`/`Permission` entities and tables, `core.UserPreferences` and its statement, `core.UserStamps` (shared with 0012), seam 16 (`IUserConnector`, `ConnectedUser`, `ConnectPremises`, `ConnectPrologue`, `PermissionRow`, `ConnectResult`, `UserProfile`, `IGuardedBatchRunner`, the prologue text), `AccessCheckRequest`, `MeResult` shape (with 0015), `HasData` rows | seam 1, seam 5 (two-level tags, `PreferencesTag`), seam 9 (`IRequestContextInitializer` at 100), seam 4 (`FilterTree` composition), seam 3 (`IStackRegistry` for securable registration), seam 12 (`ImageId`), seam 15 (`NotificationPreferences` bump), seam 6 (`Securables(...)`) |
| **0014** service-pipeline | Seam 3 (`.Crud`: `StackDescriptor`, `ActionDescriptor`, `ActionKind`, `StackLimits`, `StackOperations`, `ApiServiceDescriptor`, `IStackRegistry`, `StackContributionItem`, `[Stack]`, `[EntityAction]`, `[ApiAction]`, `[ApiRoute]`, `EntityService<,>`, `IEntityPipeline<,>`/`IEntityBehavior<,>`, hooks, `IEntityValidator`/`ISaveEffect`/`IDetailsContributor`, `DetailsRequest`, `SaveOptions`, `SaveSource`, `ActionOptions`, `PersistContext`, `SaveOutcome`, `DetailsPlan`; `.Validation`: `SaveContext`, `ChildImages`, `DeleteContext`, `ActionContext`, `IContextLoader`, `ContextPromise`, `ValidationErrors`, `ValidationError`, `ValidationCodes`), seam 10's exception types (`.Errors`: `TellmaException` and the closed set except the three tenancy/step-up types and `BlobRejectedException`/`ImportException`), the persist assembly (§1.6), `IExcelRowSource` implementation, `CrudTelemetryNames`, capability projection table, round-trip ledger | seam 1, seam 2, seam 4, seam 5 (`crud.tag`), seam 9, seam 11 (`IAccessEvaluator`, `AccessCriterion`), seam 12 (blob validator/effect registration), seam 13 (wire records as inputs/outputs), seam 15 (`INotifier` through `Notify`), seam 8 (`IJobQueue`), seam 6 (`Entity<>()`) |
| **0015** web-api-mcp | Seam 13 (`.Api`: `QueryRequest`, `QueryResult`, `GetRequest`, `IdsRequest`, `ParentIdsRequest`, `SaveRequest<T>`, `DeleteByQueryRequest`, `EntitiesResult<T>`, `AffectedResult`, `JobAccepted`, `MeResult`/`UserProfileView`/`AccessSummary`/`SecurableSummary`, `QueryDiagnostic`, `[ApiResource]`/`[DefaultSelect]`/`[RelatedSelect]`/`[DetailsExpand]`, `McpExposure`, `TellmaHeaders`, `ApiTelemetryNames`, `McpTelemetryNames`, JSON options and converters for `QueryRowSet`/`RelatedEntities`), seam 10's mapping and problem body, `MapTellma` projection, `TellmaApiOptions`, `TellmaMcpOptions`, `Tellma.Core.Mcp` (server, seven tools, auth), CSRF rules, limits, `BadRequestException`/`HumanRequiredException`/`PartialFailureException` types (with 0014), the spec 0003 amendment list | seam 3 (`IStackRegistry`, `StackDescriptor`), seam 9 (`TellmaEndpoints`, `ITenantAccessGuard`, `RequestContext`), seam 11 (`IAccessEvaluator.TryDenyFast`, `SecurableEndpointMetadata`, `MemberEndpointMetadata`, `NoActivityStampMetadata`, `ISecurableRegistry.Fingerprint`), seam 5 (`Tellma-Version-Tags` values), seam 22 (`ILocalizationNegotiator`, `IStringLocalizer` for messages), seam 12 (blob endpoints), seam 19 (Excel operations and streaming), seam 15/20 (`TellmaHub` hosting, `InboxSummary`), seam 8 (`JobAccepted`) |
| **0016** blobs | Seam 12 (every `.Blobs` type, `Blob` entity and `core.Blobs`, `BlobState`, `[BlobReference]` semantics with `BlobPreset`/`BlobReadAccess`, `BlobKindPolicy`, `IBlobStore`, `IBlobService`, `BlobName`, `IImageProcessor`, `BlobOptions`, `BlobsBuilder`, `IBlobKindRegistry`, `BlobKindDescriptor`, the adapter, the `Tellma.Core.Imaging` package (`ImageSharpImageProcessor`, `AddTellmaImageSharp`), statements, kinds, endpoints, `BlobRejectedException` type, `BlobTelemetryNames`, provisioning step 20, handlers `core.blob-sweep`/`core.blob-reconcile`) | seam 1 (capture tables, `Sql`, `Tvp`), seam 3 (`IEntityValidator`/`ISaveEffect` registration), seam 11 (`IAccessEvaluator` for downloads), seam 9, seam 8 (`[JobHandler]`, `BuiltInSchedule`), seam 13 (routes on the `Blobs` group), seam 6 (`Blobs(...)`, `BlobKind`) |
| **0017** core-gl-stacks | Seam 21 (`.Identity` client types, `IdentityServerClientOptions`, `UserService<TUser>`, `RoleService<TRole>`, `AccessService`, `InviteResult`, `InvitationStatus`, `KeyValueItem`, `PreferencesResult`, `TestNotificationChannel`, `TestNotificationResult`, `UserInvitationOutcome`, the invite flow, `Tellma.Module.Gl(.Abstractions)`: `CenterType`, `Center`, `GlResources`, `CenterService`, `GlFeature`, `gl.Centers`, `gl.sample-centers` step, `UsersTelemetryNames`), `taxonomy.json` `modules`, reference migrations, local-dev bootstrap | seam 3 (`EntityService`, `[EntityAction]`, `[ApiAction]`), seam 11 (`UserAccessRules`, `RoleAccessRules`, `ISecurableRegistry`, `IAdministratorDirectory`), seam 16, seam 12 (`user-image`), seam 15 (`core.user.added`), seam 22 (`SettingsService` replaces 0017's settings API), seam 18 (steps), seam 20 (`TenantAccessRevokedAsync`), seam 9 (`ITenantMembershipDirectory.RecordAsync`), seam 1/§1.4 (tree statements for `Centers`) |
| **0018** excel | Seam 19 (every `.Excel` type: requests, plans, outcomes, `ImportError`, `ImportMode`, `ExcelOptions`, `ExcelErrorCodes`, `ExcelTelemetryNames`, `Export`/`Import` entities and tables, `ExportKind`, codec contracts `IExcelExporter`/`IExcelImporter`/`ExcelImportSession`/`ExcelChunk`/`ExcelCell`/`ExcelQuery`/`ExcelContext`/`ExcelColumnSpec`, `ExcelOperations<T>`, manifest and sheet layout, handlers `core.export`/`core.import`, `[ExcludeFromExcel]`), `ImportException` type | seam 2 (`EntityMetadata` facets, `PropertyOwnership`, `SchemaFingerprint`), seam 7, seam 4 (`KeySetRestriction`), seam 3 (`SaveAsync` with `Source = Import`, `IExcelRowSource` from 0014, `[ApiAction]` contribution), seam 11 (`IAccessEvaluator` on lookups and the entity), seam 12 (`FileId`, `IBlobService`, `IBlobStore.OpenReadAsync`), seam 8 (`IJobHandler`, `IJobProgress.Append`, `IJobQueue`), seam 15 (`core.export.ready`, `core.import.*`), seam 22 (`ILabelProvider`, `ICalendarSystem`), seam 13 (`JobAccepted`, streaming, 422 coordinates), seam 9 (`DeploymentIdentity.Application`, `Tellma:ScratchPath`) |
| **0019** background jobs | Seam 8 (every `.Jobs` type, `Job`/`Schedule` entities, `SchedulePausedReason`, `core.Jobs`/`Schedules`/`ScheduleStates`/`JobWorkerState`, `JobOutcomeList`/`JobProgressList`/`JobLeaseList`/`ScheduleNextList`, the seven statements, `JobsOptions`, handler-key grammar and the built-in keys, `JobsTelemetryNames`, `JobService`/`ScheduleService` actions, the worker, trace links, credentials), `core.job.failed`/`core.job.held`/`core.schedule.paused`/`core.scheduler.gap` types, retention handlers | seam 1 (`Sql`, `Tvp`, `Query<T>` with `KeySetRestriction`, `OnCommitted`, `TransactionMode`), seam 9 (`ITenantScopeFactory`, `ITenantRegistry.Tenants`, `ITenantStateListener`), seam 16 (`IUserConnector.ConnectAsUser/AsSystem`), seam 11 (`IAccessEvaluator`, `IAdministratorDirectory`, `WellKnownIds`), seam 15 (`INotifier`, `IClientEventPublisher` for `job.changed`), seam 3 (`Schedule` as an activatable stack; `ContributeAsync` recomputes `NextDueAt`), seam 6 (`JobHandler<T>`, `BuiltInSchedule`), seam 12 (`user-image`-style ownership of export files through 0018) |
| **0020** notifications and hub | Seam 15 (every `.Notifications` and `.Realtime` type, `Notification` entity, `core.Notifications`, `core.NotificationPreferences` and its self-service statement, `NotificationRowList`, the insert and inbox statements, `InboxService` (`[ApiRoute("inbox")]`: `summary`, `seen`, `read`, `read-all`) and `notification-preferences/save`, Core notification types, hub events, `NotificationsTelemetryNames`, `core.notification-retention`), seam 20 (`TellmaHub`, groups, listeners implementation, Azure/Redis hosting) | seam 1 (`Sql`, `OnCommitted`, `IIdAllocator.TakeAsync`), seam 5 (`PreferencesTag`, `InboxSeenAt` on `UserStamps`), seam 9 (listeners), seam 11 (self-scope criterion, `core.Notification`), seam 3 (`[ApiRoute]`), seam 13 (`/{tenantId}/hub`, `InboxSummary` wire), seam 22 (`INotificationRenderer` under the request culture), seam 6 (`NotificationType`, `ClientEvent`) |

---

## 24. Resolutions

Each item names an ambiguity or collision in the ledger's first draft and the choice made here; the ledger has since absorbed every item (its seam entries and errata state the resolved shapes), so this list is an index of where the two documents once differed, not a pending correction.

1. **`SaveOptions` collision (seam 1 vs 0014 D5).** The ledger defined `record SaveOptions(Concurrency)` on `IDataBatch.Save` and `SaveOptions { ReturnEntities, Details, Concurrency, Source }` on the service. Resolved: `IDataBatch.Save<TEntity>(rows, concurrency: ConcurrencyMode = Check)` takes the enum directly; `SaveOptions` exists only in `Tellma.Core.Abstractions.Crud`.
2. **`EntityQuery` collision (seam 1 vs 0014 §3.3).** The batch-level `EntityQuery<TEntity>` (0011) keeps the name; the service's query input is the wire `QueryRequest` (0015), which gains `IncludeInactive: bool = false`; 0014's `QueryArgument(Name, Type, Value)` is dropped for 0011's `QueryArguments` map (the pipeline converts wire arguments through `Discover`).
3. **`ConcurrencyMode` placement.** Defined once in `Tellma.Core.Abstractions.Data`; `.Crud`, `.Api` and `.Excel` reference it.
4. **`QueryRowSet`, `QueryColumn`, `QueryColumnKind`, `RelatedEntities`, `RelatedEntitySet` placement.** Shapes owned by 0015 but placed in `Tellma.Core.Abstractions.Data` so `.Data` never references `.Api`; 0015 owns their JSON converters. `QueryDiagnostic` (wire) stays in `.Api` as the projection of `QueryexDiagnostic`.
5. **Telemetry constant holders.** Unified to `<Area>TelemetryNames` with a `MeterName` member: `DataTelemetryNames` (ledger: `DataMeter`), `ExcelTelemetryNames` (`ExcelMeters`), `JobsTelemetryNames` (`JobsTelemetry`), `NotificationsTelemetryNames` (`NotificationsTelemetry`), plus new `CrudTelemetryNames` and `UsersTelemetryNames`. The ledger's errata 0011 #18 ("`DataMeter` constants unchanged") and 0019 §3.1 need the rename.
6. **Delete by ids.** 0014's `DeleteRequest<TKey>(Ids, ExpectedStamps)` is not in the ledger; resolved as `DeleteByIdsAsync(ids, expectedStamps? = null)`; the web wire `IdsRequest` carries no stamps (a delete is a command over ids, like actions); `ExpectedStamps` remains reachable at the service level.
7. **`DetailsRequest` members.** `Extras` (0014) renamed `Include` to match the wire word; the result member stays `Extras`. `SaveOutcome.ResultOf<T>` dropped (read `BatchResult<T>.Value`); `ChildImages` named as the type of `SaveContext.Children`.
8. **`[EntityAction(Name)]` / `[ApiAction(Name)]` casing.** `Name` is the kebab-case segment (`invite`, `export-for-import`); the securable action defaults to its PascalCase form (`Invite`, `ExportForImport`), reconciling ledger §2.16 (PascalCase actions) with errata 0015 D8 (`[EntityAction("invite")]`).
9. **`me` envelope and `access/check`.** Named `MeResult(User: UserProfileView, PreferencesTag, Access: AccessSummary, Tags, SecurablesFingerprint)` and `AccessCheckRequest(UserId?, Securables: list<SecurableRef>) -> list<AccessDecision>`; the `inbox` member of 0013 §3.8's `me` is dropped (counts are capped queries served by `inbox/summary`, and the prologue no longer returns them). `SecurableRef(Resource, Action)` replaces the tuple in `IAccessEvaluator.Evaluate(list)`.
10. **`ConnectResult`/`ConnectedUser`.** Inbox counts removed; `SettingsStale` and `UserPermissionsTag` added; `ConnectPremises` gains `ExpectedUserPermissionsTag` so the two-level guard (`permissions` and the user's `PermissionsTag`, ledger §2.24) has its input; `UserAccess` gains `UserTag`. `UserProfile.ImageId` is `int?`.
11. **`50401 CallerInvalid` placement.** The prologue already refuses unknown and deactivated callers; the number is kept for the in-transaction re-check `IF NOT EXISTS (… IsActive = 1) THROW 50401` immediately after the 50412 guard in every `Persist` batch (the deactivation `IF` 0012 asked 0013 for).
12. **`Schedule.PausedReason`.** Made an enum `SchedulePausedReason = OwnerInactive | Exhausted` stored `varchar(13)` (the ledger left it a string with two literal values).
13. **`ExportOutcome`/`ImportOutcome`.** Gain `ExportId`/`ImportId` beside `JobId` so the 202 body `JobAccepted(JobId, ResourceId)` can be built; `ImportRequest.OverrideConcurrency` becomes `Concurrency: ConcurrencyMode?` (null = derived from the sheet per errata 0018 D9) so a caller may still force `Override`.
14. **Resource-id column types.** `varchar(128)` everywhere a securable resource id is stored (`Permissions.Resource` already; `Notifications.TargetResource` was `nvarchar(64)`; `Exports.Resource`/`Imports.Resource` were `nvarchar(64)`).
15. **Language/calendar/zone column types on `core.Settings`.** Aligned to 0013's `Users` columns: `varchar(35)` (BCP 47), `varchar(16)`, `varchar(64)`; 0012 §4 had `nvarchar(16)`/`nvarchar(8)`/`nvarchar(64)`.
16. **`BlobPreset`/`BlobReadAccess` placement.** Moved with `[BlobReference]` into `Tellma.Core.Abstractions.Entities` (the attribute names them); every other blob type stays in `.Blobs`. `BlobState` enum named for `Blob.State`.
17. **`Gender` enum.** One enum `Gender = Female | Male` in `.Access` used by both `User.Gender` and `IdentityInvitation.Gender` (0017's `IdentityGender` dropped).
18. **`IdentityInvitationResult.Status`** uses `InviteStatus` (0013) rather than a second `IdentityInvitationStatus` enum with identical members.
19. **`TestNotificationChannel = Email | Sms`** replaces 0017's `NotificationChannel { Email, Sms, Push }` (no push transport exists; the enum is used only by `me/test-notification`).
20. **`IJobProgress.Append` fencing.** The statement is fenced by the lease token and throws `50422 Job.LeaseLost` when the row count differs, so a checkpoint from a lost lease rolls back the chunk (the ledger named the member without its statement).
21. **`IGuardedBatchRunner.Run` signature.** `Run<TResult>(purpose, compose: (ConnectedUser, IDataBatch) -> void, read: (ConnectedUser, BatchOutcome) -> TResult)`; the runner creates the batch (0013's version had the composer return a builder).
22. **`ITenantScopeFactory.CreateScopeAsync`** throws `TenantUnavailableException` (not a bespoke error) for non-`Active` tenants; `RequestContextSnapshot` gains `Client`.
23. **`ContextPromise<T>` from `IContextLoader.Rows`** returns `QueryRowSet` (0014 had `list<object?[]>`); `IContextLoader.Sql` takes `FormattableString` + `SqlOptions` (0014's `SqlStatement`/`RowReader` are gone with the batch merge).
24. **`DeleteSpec.ByQuery` cap.** Gains `Cap: int` so the emitter can emit `TOP (cap + 1)` and `THROW 50413`; the pipeline passes `MaxDeleteByQueryRows`.
25. **`FeatureContribution` items.** Each sugar the ledger listed is backed by a named record (`SecurablesContributionItem`, `SettingKeysContributionItem`, `CalendarContributionItem`, `ProvisioningStepContributionItem`, `JobHandlerContributionItem`, `BuiltInScheduleContributionItem`, `NotificationTypeContributionItem`, `ClientEventContributionItem`, `BlobKindContributionItem`), realised by `Tellma.Core`; the ledger named only `StackContributionItem`.
26. **Provisioning step names.** `core.bootstrap-administrator`, `core.blob-container`, `gl.sample-centers` (the ledger gave orders, not keys; the version-tag step was dropped because the migrator seed is the only seed); the step-key grammar follows the handler-key grammar.
27. **Core notification type keys.** `core.user.added` added for the "you were added" notice (0017 D3 names the notice but no key); `Mutable` values fixed per 0020 D15.
28. **`AccessService` and `SettingsService`.** Named as the `[ApiRoute("access")]` and `[ApiRoute("settings")]` services (the ledger gave routes, not class names); `InboxService` for `[ApiRoute("inbox")]`; `JobService`/`ScheduleService` for the two stacks.
29. **`NotificationPreferenceList`** standalone type (`Type`, `Inbox`, `Email`) for `notification-preferences/save`, and `Notifications.CannotMute` as its validation code; `Type` is `nvarchar(64)` on both notification tables.
30. **`EntityService.GetAllCachedAsync`** is the service-side name of the `all` operation projected by `[Cacheable]` (the ledger says `GetAllCached` without a signature).
