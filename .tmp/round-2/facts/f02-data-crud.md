# Facts digest f02: data access layer (0012) and CRUD service pipeline (0015)

Status: complete. Sections 2A and 2B digest the two specs in order; 3 to 5 are the round-2 findings.

## 1. Sources read

- `docs/specs/0012-data-access-layer.md` (2260 lines), title "Spec: Entity Contract and Data Access".
  Header says "Ready for implementation. Frozen once merged"; per the round-2 README it is round 1
  (0010–0022), not implemented, changeable in place.
- `docs/specs/0015-crud-service-pipeline.md` (2541 lines), title "Spec: CRUD Service Pipeline and
  Capabilities".

## 2A. Key facts: spec 0012 (entity contract and data access)

### 2A.1 Placement (0012 §1)

- Contracts in `Tellma.Core.Abstractions` (namespaces `.Entities`, `.Data`, `.TableTypes`); it
  references `Tellma.Core.Queryex` (so `FilterTree`, `QuerySpec`, `QueryexColumn`, `QueryexSchema`,
  `KeySetRestriction` appear in contracts). Runtime in `Tellma.Core` (namespace `Tellma.Core.Data`):
  `DataBatch`, `SaveEmitter`, `TableTypeBinder`, `IdAllocator`, `QueryexSchemaProvider`,
  `EntityMaterializer`, `TreeStatements`, `TellmaDbContext` (0012 §1.1).
- No LINQ surface: `TellmaDbContext` is platform-internal; tables are read and written only
  through `IDataBatch`; analyzer `TELLMA0002` refuses EF queries/`SaveChanges` (0012 §1.3, §12.3).
- Contract blocks omit cancellation-token parameters by convention ("cancellation-token
  parameters ... are omitted", 0012 §1.1). See 2A.15 and 2B.25 for how the token flows.
- EF conventions (0012 §1.3) emit every constraint and index (`PK_`, `FK_<Table>_<Column>`,
  `UX_<Table>_<Cols>`, `IX_`, `CK_`, `DF_`), so a distribution names none; the error mapper later
  maps index and FK names back to properties. Sequences `<schema>.sq_<Table>` start at 1000;
  seeded rows use ids 1–999; `CK_<Table>_Id CHECK ([Id] > 0)` (0012 §1.3, §4.1).

### 2A.2 Entity bases and interfaces (0012 §2.1)

- `Entity<TKey>` (`TKey` is `int` or `long`; no `Guid` keys) with `Id` (server-owned; PK).
- `TopLevelEntity<TKey>`: `CreatedAt`, `CreatedById`, `ModifiedAt`, `ModifiedById`
  (`datetimeoffset(7)`, FKs → `core.Users`, all server-owned). `ModifiedAt` is the concurrency token
  (read as the expected stamp on save). No `rowversion` anywhere.
- `ChildEntity<TKey>`: no audit columns; saved only with its owner; never addressed by a service of
  its own; owning FK carries `[ParentKey]`. A child change stamps the parent.
- `TreeEntity<TKey> : TopLevelEntity<TKey>`: `ParentId` (self FK, no CLR navigation),
  `SubtreeCount` (server-owned; 1 for a leaf); shadow `Node hierarchyid` (never a CLR member).
- `ActivatableTreeEntity<TKey> : TreeEntity<TKey>, IActivatable`: `IsActive` (default `true`),
  `ActiveSubtreeCount`. No `Level` column (`level(Node)` instead), no `IsLeaf` (`SubtreeCount = 1`).
- Non-generic shorthands: `TopLevelEntity`, `ChildEntity`, `TreeEntity`, `ActivatableTreeEntity` (= `<int>`).
- `IActivatable { bool IsActive }`: server-owned; every row is created active; only the
  activate/deactivate actions (an `IDataBatch.Update`) change it; a client value on save/import is
  silently overwritten. Review flag 2 (0012) records the alternative (write-once / creatable inactive).
- `IJobEntity { int? JobId }`: server-owned link to `core.Jobs`.
- Wire id semantics: `Id = 0` new; `Id < 0` temporary id unique within the payload (rewritten by the
  allocator in self-typed FKs and child parent keys, §7.3); `Id > 0` update.
- System-written entities (`Job`, `Notification`, `Blob`) derive from `Entity<int>`, carry
  `CreatedAt` explicitly, never written through `Save`.
- Tree navigation `Parent` derives from the `ParentId` column name; `Parent.Name`, `descendantOf`,
  `ancestorOf`, `level(Node)` work; a details read carries the parent row in `Related` (§11.5).
- Extension: plain inheritance `MyCenter : Center` + `tellma.UseEntity<Center, MyCenter>()`;
  `TLeaf : TDefault` is the one constraint (0012 §2.1, §2.8).

### 2A.3 Annotations (0012 §2.2)

`[Temporal(enabled)]`, `[TableType]`, `[ExcludeFromTableType]`, `[Tree(MaxDepth = 32)]`,
`[ParentKey]`, `[MaxChildren(Count)]` (on a `[NotMapped]` child collection; default 10,000),
`[ServerOwned]`, `[WriteOnce]`, `[Derived]`, `[NaturalKey(Order = 0)]` (implies `[Unique]`;
composite reserved), `[Unique]`, `[Searchable(SearchKind Kind = Contains)]` (`SearchKind { Contains,
Prefix }`), `[PreserveWhitespace]`, `[JsonColumn]`, `[Multilingual]`, `[Cacheable(MaxRows = 0)]`,
`[BumpsVersionTag(name)]`, `[BumpsUserVersionTag(column, userIdProperty = "UserId")]`,
`[BlobReference]` (spec 0017 §2.3), `[ExcludeFromExcel]`, `[Sibling(navigation)]`.

- `[Stack]`, `[ApiResource]`, `[DefaultSelect]`, `[RelatedSelect]`, `[DetailsExpand]`,
  `[EntityAction]`, `[ApiAction]`, `[ApiRoute]` are declared by specs 0015 and 0016 (0012 §2.2).
- `[Searchable]`, `[PreserveWhitespace]`, `[ExcludeFromExcel]` are metadata facets only in 0012;
  search disjunction and trim normalisation are 0015's (0012 §2.2 table).
- **There is no label/caption/display-name annotation and no label key anywhere in 0012.** No
  `[Display]`, no `Gl_Center_Name`-style keys, no format hints. Labels are mentioned only as
  "labels and negotiation: spec 0013" for `[Multilingual]` (0012 §2.2 table).

### 2A.4 Child collections and `[MaxChildren]` (0012 §2.3)

- A child collection is any `[NotMapped]` `list<TChild>` on a parent with `TChild : ChildEntity<TKey>`;
  its property name is the child's table name (e.g. `RoleMemberships`). EF never sees it; no
  collections in Queryex.
- `EntityMetadata.Children` pairs it with the FK behind `[ParentKey]` and carries `MaxCount`
  (`[MaxChildren]`, default 10,000 rows per parent, enforced per parent at every depth by 0015 save
  step 1). Nesting is recursive (grandchildren).
- `null` vs `[]` is significant: `null` = not supplied, leave children alone; `[]` = delete all;
  a list = synchronise. A child row whose parent id is not among the payload's parents → `Entity.NotFound`.

### 2A.5 Property ownership (0012 §2.4)

`enum PropertyOwnership { Editable, WriteOnce, ServerOwned, Derived, DatabaseOwned }`.

| Ownership | Source | Behaviour |
|---|---|---|
| `ServerOwned` | `[ServerOwned]`; by derivation `Id`, four audit columns, `SubtreeCount`, `ActiveSubtreeCount`, `IsActive`, `JobId` | never in a `SET`; on insert reset to fresh-instance value (`ResetServerOwned`); on update before image wins silently |
| `WriteOnce` | `[WriteOnce]` | never in `SET`; written on insert from payload; changed value on update → pipeline error `WriteOnce` at the path |
| `Derived` | `[Derived]` | client value discarded (`ResetDerived` on every row before the preprocess hook); written on insert and update from the hook's value |
| `DatabaseOwned` | computed column (`HasComputedColumnSql`), no attribute | never written; read back; client value ignored silently (example: spec 0014's `User.State`) |
| `Editable` | everything else | in the `SET` list |

- Ownership from capability interfaces (`IsActive`, `JobId`) is derived by the metadata builder
  (attributes on interface members do not reach implementations).

### 2A.6 The related projection (0012 §2.5) — closest thing to a "display name"

- When an entity carries no `[RelatedSelect]`, it travels as a related entity with the fixed
  projection: `Id`, the `[Multilingual]` `Name` group (`Name`, `Name2`, `Name3`), `Code` when it has
  one, and every `[BlobReference]` column whose preset is `Avatar`. A declaration **replaces** the
  default rather than extending it.
- Core `User` → `Id, Name, Name2, Name3, ImageId`; `Role` → `Id, Name, Name2, Name3, Code`.
  `CreatedBy.Email` etc. travel only for a caller whose `Read` on `User` is unfiltered (traversal
  rule, 0015 §5.2).
- The materializer records each related set's projection (`RelatedEntitySet.Projection`).
- Review flag 19 (0012): default projection vs requiring `[RelatedSelect]` everywhere.

### 2A.7 Multilingual, enums, JSON (0012 §2.6)

- `[Multilingual]` on string `P` anchors group `P`; twins `P2`, `P3` found by name, must be nullable,
  same `MaxLength` and unicode. A twin without anchor is ordinary (`Address2`).
- Queryex schema declares `P2`/`P3` only when the tenant's `MultilingualShape` admits them
  (`Primary | PrimaryAndSecondary | All`, spec 0013); **the wire omits absent twins**.
- Enums: stored `varchar(n)`; Queryex type `String` with `VarChar(n)`; `CenterType = 'Service'`
  compiles. `PropertyMetadata.EnumValues` lists members.
- `[JsonColumn]`: `nvarchar(max)`, opaque, absent from the Queryex schema.

### 2A.8 Natural keys (0012 §2.7)

- `[NaturalKey(Order)]` on a scalar with a list type (`string`, `int`, `long`, `Guid`, `DateOnly`),
  backed by a single-column unique index. Composite keys reserved.
- Inferred when undeclared, over unique-indexed strings, in order: `[Multilingual]` `Name` group,
  `Code`, required unique strings, nullable unique strings (decision 10: `Name` before `Code`).
- `EntityMetadata.NaturalKeys` ordered list; `NaturalKey` = first; none → surrogate id is the export
  row key. Core declarations: `User.Email`, `Role.Code`, `Center.Code`.

### 2A.9 `EntityMetadata` (0012 §3.1)

Namespace `Tellma.Core.Abstractions.Data`. Members: `ClrType`, `Name`, `Table` (`TableName(Schema,
Name)`), `TableTypePhysicalName`, `Key`, `Properties`, `Children`, `References`, `NaturalKeys`,
`NaturalKey`, `MultilingualGroups`, `UniqueIndexes`, `IsTopLevel`, `IsTemporal`, `IsActivatable`,
`IsJobEntity`, `IsCacheable`, `Tree` (`TreeMetadata?`), `BlobReferences`, `SchemaFingerprint`,
`ResetServerOwned(object)`, `ResetDerived(object)`.

- `PropertyMetadata(Name, Column, ClrType, Ownership, IsNullable, IsUnique, MaxLength, Precision,
  Scale, EnumValues, MultilingualGroup, IsSearchable, SearchKind, ExcludedFromExcel, Getter, Setter)`.
  **No label, no display format, no Queryex type, no "is required for UI" flag beyond `IsNullable`.**
- `ChildCollectionMetadata(Property, Child, ParentKey, MaxCount, Getter)`.
- `ReferenceMetadata(Navigation, ForeignKey, Target)` — every many-to-one FK; navigation named by
  the CLR navigation when present, else FK column minus `Id` (`ParentId` → `Parent`,
  `CreatedById` → `CreatedBy`).
- `NaturalKeyMetadata(Property, IsDeclared, Order)`; `MultilingualGroup(Name, Primary, Secondary,
  Tertiary)`; `UniqueIndexMetadata(IndexName, Properties)`;
  `TreeMetadata(ParentKey, NodeColumn, SubtreeCount, ActiveSubtreeCount?, MaxDepth)`;
  `BlobReferenceMetadata(Property, Kind, Policy)` (`BlobKindPolicy`: `MaxSize`,
  `AllowedContentTypes`, `Image`, `Thumbnail`, `StagingTtl`, `ReadAccess`, spec 0017).
- `Name` = `<schema>.<CLR type name of the pack's default leaf>` (`core.User`, `gl.Center`); a
  distribution leaf (`MyCenter`) keeps the default's name. It is the Queryex entity name, the
  securable resource, the key of related-entity sets and the suffix of the `entity:<Name>` tag.
- `Properties`: every mapped scalar CLR property in declaration order (bases first); shadow columns
  (`Node`, period columns) are not properties.
- `SchemaFingerprint`: SHA-256 over `(Name, Column, store type, IsNullable, Ownership)` of every
  `Editable`/`WriteOnce` property and recursively children (spec 0019 manifest drift check).
- `IEntityMetadataProvider` (singleton): `Get(Type)` (accepts leaf or any base), `All`,
  `StorageFingerprint` (bound as `@tm_schema`).
- `EntityMetadata` is server-side only; nothing in 0012 serialises it to the wire.

### 2A.10 Startup validation (0012 §3.2)

Aggregated into spec 0011's startup gate (child pairing, tree `MaxDepth` 2..32, multilingual twins,
uniqueness backing, navigation-name collisions, `[BlobReference]` on `int?`, one ownership attribute,
one leaf per table). `EntityMetadata` is immutable after the gate.

### 2A.11 Columnar rows, `RowPage`, related sets (0012 §3.3)

- `QueryColumn(Name, QueryexType Type, int? Scale, bool Nullable, IReadOnlyList<string>? Path,
  bool GroupingKey)` — projection of spec 0008's `QueryexColumn`. `Scale` set on `Numeric` only
  (`0` for integers, decimal scale, `null` for float).
- `QueryRowSet`: `Columns`, `RowCount`, `GetBuffer(column)` (typed arrays), `IsNull(column,row)`,
  `Builder`.
- `RowPage(QueryRowSet Rows, int? Count, QueryRowSet? Ancestors)` — `Count` is the capped count
  when `CountCap` was given; **`cap + 1` means more than the cap**. Spec 0016 §3.5 serialises it.
- `RelatedEntities` (`Add<T>(entityName, entities, projection)`, `Sets`) and
  `RelatedEntitySet(EntityType, Entities, Projection)`; keyed by entity name.
- `GroupingKey` on columns exists because `QuerySpec` can be an aggregate (spec 0008); 0012's
  dedup key lists "having" (0012 §5.4), confirming aggregate specs reach `Rows`.

### 2A.12 Column vocabulary (0012 §4)

- Tables plural (`core.Users`, `gl.Centers`); CLR type and Queryex entity singular. Schemas
  lower-case (`core`, `gl`, `catalog`, `idsvr`, `fixture`).
- Every timestamp `datetimeoffset` at offset zero from `SYSUTCDATETIME()`; audit `(7)`, others
  `(3)`; Queryex type `DateTimeOffset`; "a calendar function needs `local()`" (0012 §4.1).
- N−1 rule: readers bind by name, writers list columns, expand then contract (0012 §4.3).
- Schema guard: every round trip starts with `IF NOT EXISTS (... __TellmaSchema WHERE Fingerprint =
  @tm_schema) THROW 50501, N'SchemaBehind'` → `503 tenant-schema-behind`, `Retry-After: 30`
  (0012 §4.3). The SPA will see this as a 503 on any call during a migration window.
- Standalone table types (0012 §4.4): `IdList`, `BigIdList`, `GuidList`, `StringList`, `DateList`,
  `IdStampList` (`Id`, `ModifiedAt`), `VersionTagList`, job/notification/preference/outbox lists.
  An assignments back end (round 2) would add its own here if it needs bulk statements.

### 2A.13 Tenant database and batch (0012 §5)

- `ITenantDatabase` (scoped): `Schema` (for the tenant's `MultilingualShape`), `Context`
  (`QueryexContextValues(Today, Now, UserId?, TimeZoneName)`), `CreateBatch(BatchPurpose)`.
  `ITenantDatabaseFactory.OpenAsync(tenantId)` for job scopes (0012 §5.1).
- `BatchPurpose { Read, Validate, Persist, Maintenance }`; `TransactionMode { Auto, None, Explicit }`;
  `ConcurrencyMode { Check, Override }` (defined once here, used by `.Crud`, `.Api`).
- `today()`/`TimeZone` slot bind the **tenant** zone (`TenantSettings.SqlServerTimeZoneName`); the
  display zone formats and never binds (0012 §5.1, decision 26).
- `IDataBatch` members (0012 §5.2): `Purpose`, `TransactionMode`, `WrittenTables`, `CallerAuthority`,
  `Query<TEntity>(EntityQuery<TEntity>)`, `Rows(QuerySpec, QueryArguments?, RowQueryOptions?)` →
  `RowPage`, `Count(QuerySpec, QueryArguments?, int cap = 10000)`, `Save<TEntity>(rows, concurrency)`
  → `SaveHandle`, `Update<TEntity>(UpdateSpec)` → `UpdateReceipt`, `Delete<TEntity>(DeleteSpec)` →
  `DeleteHandle`, `Assert(...)`, `Sql(FormattableString, SqlOptions?)` → `RawResult`, `Tvp<TRow>`,
  `DeclareIdTable()`, `DependsOn(...)`, `FromCache<TEntity>()`, `BumpVersionTag(name)`,
  `OnCommitted(Action<BatchOutcome>)`, `ExecuteAsync()`.
- `RowQueryOptions`: `CountCap` (int?; adds the capped grand total), `IncludeAncestors` (default
  false; tree roots only), `AncestorsFilter` (the access filter alone for ancestor rows; null when
  unrestricted), `CaptureKeys` (default false).
- `EntityQuery<TEntity>`: `Select` (required; **bare paths only**), `Filter` (`FilterTree?`),
  `OrderBy`, `Skip`, `Take`, `Arguments`, `Restrictions` (`KeySetRestriction` list), `Children`
  (collection names; dotted for grandchildren), `IncludeAncestors`, `AncestorsFilter`.
- `EntityQueryResult<TEntity>(Entities, Related)`.
- `SaveReceipt(DateTimeOffset Stamp, int Inserted, int Updated, int Deleted)`;
  `SaveHandle(Receipt, SavedIds, NewIds, TouchedIds, Captures)`.
- `UpdateSpec<TEntity>.ByIds(Ids, ReadFilter, Filter, Assignments, Stamp = true, AllOrNothing = false)`
  and `.ByQuery(Filter, Arguments, Assignments, Cap, ExpectedCount, Stamp = true)`;
  `UpdateReceipt(UpdatedIds)`.
- `DeleteSpec<TEntity>.ByIds(Ids, ReadFilter, Filter, ExpectedStamps = null)`, `.ByQuery(Filter,
  Arguments, Cap, ExpectedCount)`, `.WithDescendants(Ids, ReadFilter, Filter)`;
  `DeleteReceipt(DeletedIds)`.
- `Count`: `SELECT COUNT(*) FROM (<body with Select = "Id", OrderBy = "Id", Take = cap + 1>)`;
  result `cap + 1` = "more than cap" (0012 §5.2).
- `OnCommitted` callbacks run after commit, outside the transaction; failures logged
  (`DataBatch.OnCommittedFailed`), never thrown.
- Dedup: identical `Query`/`Rows`/`Count` keys return the same handle (0012 §5.4).
- Max 2,000 parameters per batch (TVP counts as one) (0012 §5.4).
- Caller channel: a `Save`/`Update`/`Delete`/`Sql` writing a table owned by another stack is
  refused at append (`CallerAuthority`) — writes into another stack go through enlistment
  (0012 §5.2; 0015 §13.3).

### 2A.14 Persist frame and budgets (0012 §5.5, §5.6)

- Persist frame order: `SET NOCOUNT ON` + schema guard + contributor prologues (connect prologue)
  → `IF @tm_Guard = 1 BEGIN` → `SET XACT_ABORT ON; BEGIN TRAN` → version-tag guard (`50412
  StaleVersionTag`) → caller re-check (`50401 CallerInvalid`) → caller statements → tag-bump
  epilogue → `COMMIT; END;`. `Read`/`Validate` batches: steps 1, 2, 6, `END` only (untransacted).
- Round-trip budgets (0012 §5.6): grid page (flat or tree, capped count, ancestors) **1**; details
  by id (entity, children, related) **1**; create **1**; update with children **2**; delete by
  ids/query/descendants **1**; activate/deactivate **1** (custom action 2); get by parent ids **1**;
  import per chunk 2 (+1 hydration). Cold/stale path +1 prologue-only round trip; each dependent
  validation round +1 (max rounds 3).

### 2A.15 Execution, cancellation, errors (0012 §6)

- One round trip = one `SqlCommand` (`CommandType.Text`) with "**the cancellation token of the
  scope**" (0012 §6.1). That is the only statement on cancellation in 0012; whether "the scope's"
  token is `HttpContext.RequestAborted` is not stated here (it is spec 0011's request scope). See
  2B.25 and gap 24.
- `DataOptions` (bound from `Tellma:Data`): `CommandTimeout` 30 s; `MaintenanceCommandTimeout`
  600 s; `RetryAttempts` 3 (four attempts); `RetryBaseDelay` 50 ms (50/200/800 ±25 %);
  `SlowRoundTripThreshold` 5 s; `IdBufferLowWater` 64; `LargeBatchThreshold` 1000 (adds
  `OPTION (RECOMPILE)`); `ConflictIdsInMessage` 100.
- Span per round trip: display name `"<operation> <purpose>"` e.g. `gl.Center:save persist`;
  attributes `tellma.db.role`, `tellma.data.purpose`, `tellma.data.operation`, `tellma.data.attempt`;
  slow round trip logs `DataBatch.SlowRoundTrip` (Information) with full batch text.
- `TellmaSqlErrors` (0012 §6.3): `CallerInvalid` 50401, `RowSecurity` 50403, `NotFound` 50404,
  `Concurrency` 50409, `StaleVersionTag` 50412, `LimitExceeded` 50413, `Invariant` 50422,
  `CountMismatch` 50428, `SchemaBehind` 50501, `Transient` 50503; distro band 50600–50699.
- Each number maps to an internal exception that 0015 §14.2 translates (see 2B.24); 50501 →
  `TenantUnavailableException` (503 `tenant-schema-behind`); 50503 → transient retry then
  `DependencyUnavailableException`. Provider errors (0012 §6.4): 2601/2627 → unique violation by
  index name; 547 → `Fk.NotFound` (save) / `Fk.InUse` (delete); 530 → `Tree.TooDeep`; deadlocks and
  throttling transient; connection drops ambiguous.
- `ConflictRow(Id, IsMissing, ModifiedAt, ModifiedById, Name, Name2, Name3)` — the conflict carries
  the modifier's multilingual name, so a client can say "changed by X at T" (0012 §6.4, §8.2).
- Statement-bound exceptions carry `StatementOrdinal` (0012 §6.4).
- Retry (0012 §6.5): reported transient → whole round trip re-run; ambiguous → re-run only if all
  statements idempotent, else commit probe (`Committed` → re-issue read-back; `NotCommitted` →
  re-run; `Unknown` → `DependencyUnavailableException` "with a reload instruction").

### 2A.16 Id allocation (0012 §7)

- `IIdAllocator.Reserve(batch, entityType, rows)` → `IdReservation` (`IsImmediate`, `Assign`,
  `Dispose`); `TakeAsync(entityType, count)`. Ids assigned **before validators run**; temporary ids
  (`Id < 0`) rewritten in self-typed FKs, child parent keys, grandchild parent keys; a temporary id
  referenced but never defined → `Entity.NotFound` at the referencing path (0012 §7.3).
- Validation failure returns ids to the buffer; a persist attempt never returns them (0012 §7.3).

### 2A.17 Save emitter (0012 §8)

- Order: concurrency guard (U locks; `'C'` stale / `'M'` missing; conflict rows then `THROW 50409`)
  → insert new roots → synchronise children (delete / update changed / insert; a removed child takes
  its descendants) → update changed or touched roots and stamp → receipt; tree statements after
  (0012 §8.1, §8.3).
- Expected stamp = the client's `ModifiedAt`; `Override` disables the comparison, never the
  existence check (no resurrection). Unchanged rows are skipped via `EXCEPT`; the root's
  `ModifiedAt` moves exactly when the aggregate changed; one server-time stamp per batch (0012 §8.2).

### 2A.18 Update and delete statements (0012 §9)

- `Update` is the statement for every column action (activate, deactivate, state transitions,
  spec 0020 `take-over`), by ids or by query; skips rows already in the target state (no stamp move);
  `AllOrNothing` = 404 under `ReadFilter` then 403 under `Filter` (0012 §9.1).
- `Delete`: `ByIds` 404 (missing or hidden — indistinguishable) before 403; with stamps 409;
  `ByQuery` capped (`Cap` = 0015's `MaxDeleteByQueryRows`) → 50413, `ExpectedCount` mismatch →
  50428; `WithDescendants` deletes the closure by node in one statement (0012 §9.2). A 547 on a
  referenced row → `Fk.InUse`. No `ON DELETE CASCADE`.

### 2A.19 Trees (0012 §10)

- `Node` = id path (`/15/342/1207/`); new rows provisional `/0/<Id>/`; re-path in the same
  transaction; siblings never renumber. **No sibling ordering column exists**: trees have no
  user-defined order among siblings.
- After save: affected set → paths (MAXRECURSION = MaxDepth) → cycle fence (`50422 Tree.Cycle`) →
  re-path (never stamps `ModifiedAt`) → scoped recount of `SubtreeCount`/`ActiveSubtreeCount`.
- `TreeCycleValidator<T>` in the validation round: loads `(Id, ParentId)` of the **whole table**,
  reports `Tree.Cycle` on `ParentId`, also own-id parent and `Tree.TooDeep` (0012 §10.4).
- `hierarchyid` result column is read as its **path string** (0012 §10.1, §11.3) — a client could
  derive depth and ancestry from the string if it is selected.
- Weekly `core.tree-verify` job repairs count drift (0012 §10.4).

### 2A.20 Queryex host integration (0012 §11)

- `IQueryexSchemaProvider.GetSchema(MultilingualShape)`, `Fingerprint`; at most three schemas per
  process (0012 §11.1).
- Schema rules: every mapped leaf, children included, is a root under `EntityMetadata.Name`;
  properties = mapped scalars with a Queryex type, `IsNotNull`, `IsUnique`, `StoreType`; absent:
  `[NotMapped]`, period columns, `[JsonColumn]`, `varbinary`, `time`, `float`/`real`,
  `Blob.StorageKey`, `Blob.Sha256`, `Job.StateJson`, `Job.LeaseToken`; navigations = every
  many-to-one FK (named like `References`); `[ParentKey]` gives child→parent navigation;
  `[Sibling]` gives a one-to-one left-join navigation; **no collections**; tree `Node` declared
  `HierarchyId`/`TreeNode`; read-only roots `core.UserStamp`, `core.Blob`.
- Seven engine amendments (0012 §11.2): `KeySetRestriction(Path, TableSource)`; `level(node)`;
  `CompiledQuery.Prologue/Body`; `Skip`/`Take` as slots `@qx{b}_skip`/`@qx{b}_take`; `today()`,
  `now()`, `me()` bind tenant-zone context; versioned leaves (`FilterTree.Leaf(text,
  languageVersion)`); `FilterTree.Via(navigation, inner)`. `QueryexLanguage.Version` stays 1. A
  list parameter `x in (@ids)` remains unbuilt.
- `Rows` binds `Declared` slots from `QueryArguments`; a missing argument is a compose-time
  `ArgumentException` (0015 binds every declared parameter, `null` when absent) (0012 §11.3).
- Ancestors (0012 §11.3): with `IncludeAncestors` the page keys are captured; `@tb{b}_anc` = rows
  that are proper ancestors of a page row and not themselves page rows; selected with the same
  select under `AncestorsFilter` (access filter only — ancestors need not match the user's filter),
  ordering kept, no paging; land in `RowPage.Ancestors` (or `Related` under the root entity name for
  `Query`, 0012 §11.4). **Nothing marks which rows matched vs are ancestors other than the two
  separate sets** — this is how the SPA distinguishes match rows from context rows.
- Entity queries (0012 §11.4): `Select` bare paths only; host appends root `Id`, each navigation's
  key path and FK; `Children` add one query per collection restricted by parent key.
- Materializer (0012 §11.5): related instances deduped by (entity name, id), partial, never
  assigned to navigation properties (no cycles on the wire); children stitched in child-key order.

### 2A.21 Telemetry (0012 §12)

- Meter `Tellma.Core`; instruments `tellma.data.roundtrips`, `.roundtrip.duration`,
  `.batch.statements`, `.retries`, `.commit.probes`, `.ids.*`, `.rows.saved`, `.rows.deleted`,
  `.concurrency.conflicts`, `.tree.recomputes`, `.tree.repairs`. No tenant/user tags.
- `DataAccessScope.Operation` named `"<Resource>:<operation>"` (`gl.Center:query`,
  `core.User:save`) or `"job:<key>"`.
- Analyzers `TELLMA0001`–`TELLMA0006` (0012 §12.3); `TELLMA0004` forbids calling `[ApiAction]`/
  `[EntityAction]` methods directly; `TELLMA0005` forbids nested pipelines from participants;
  `TELLMA0006` forbids writing another stack's table.

## 2B. Key facts: spec 0015 (CRUD service pipeline)

### 2B.1 Placement and principles (0015 §1)

- Contracts in `Tellma.Core.Abstractions` namespaces `.Crud`, `.Validation`, `.Errors`; runtime
  `Tellma.Core.Crud` (`EntityPipeline<,>` sealed, `QueryPlanner`, `DetailsPlanner`,
  `ActivatableRecipe`, `TreeRecipe`, `CacheableRecipe`, `ExcelRowSource<,>`, ...) (0015 §1.1).
  Conformance base `StackConformanceTests<TEntity, TKey>` in `Tellma.Core.Testing` (0015 §18.3).
- Principle 3 (0015 §1.2): "`StackDescriptor` is the only thing HTTP, MCP, **the SPA schema**,
  securable registration, Excel and the conformance tests read". 0015 names an "SPA schema"
  consumer but defines no such endpoint or shape (see gaps).
- Principle 6: validation outcomes are stable codes with named arguments; messages rendered by spec
  0016 under the request culture.
- Principle 7: bulk everywhere; the details page sends an array of one.
- Vocabulary (0015 §1.3): securable = `(Resource, Action)`; resource = entity name
  `<schema>.<Entity>`; actions PascalCase (`Read`, `Save`, `Delete`, `Activate`, `Invite`); display
  form `resource:action`.

### 2B.2 Stack declaration and `StackDescriptor` (0015 §2.1, §2.2)

- Registration: `contribution.Entity<TEntity>()` or `contribution.Entity<TEntity, TService>()`;
  substitution `tellma.UseEntity<TDefault, TLeaf>()`.
- `StackDescriptor(Resource, ResourceSegment, EntityType, ServiceType, KeyType, Description,
  Operations, Capabilities, Actions, Properties, Children, SearchableProperties, NaturalKeys,
  DefaultSelect, RelatedSelect, DetailsExpand, Limits, Mcp, ContractRevision)`.
  - `Resource` = `EntityMetadata.Name` (`gl.Center`), fork-stable.
  - `ResourceSegment` = kebab-case plural of the table (`centers`, `invoice-lines`), override
    `[ApiResource(Segment = …)]` (spec 0016); routing only.
  - `Capabilities` closed vocabulary: `activatable`, `tree`, `temporal`, `multilingual`,
    `cacheable`, `blobs`, `job-entity`, `searchable`, `excel` (0015 §12).
  - `Properties` = `EntityMetadata` properties with `PropertyOwnership` (no labels).
  - `Children`: `ChildCollectionDescriptor(Property, ChildType, ParentKeyProperty, MaxCount)`.
  - `SearchableProperties` `(string, SearchKind)` list; `NaturalKeys`.
  - **`DefaultSelect`** (`string`, Queryex select text): `[DefaultSelect]` text, else platform
    default `Id`, the natural keys, the `[Multilingual]` `Name` group, `Code`, `IsActive`, the four
    audit columns — "the columns **pickers** and the MCP query tool use when they pass nothing".
  - **`RelatedSelect`** (`IReadOnlySet<string>`): the projection visible through navigations
    (0012 §2.5 default: `Id`, `Name` group, `Code`, `Avatar`-preset blob columns).
  - **`DetailsExpand`**: `[DetailsExpand]` navigations, else every FK navigation of the entity and
    its children at depth 1.
  - `Description`: from `[ApiResource]` or the entity's XML summary. `Mcp` default `Full`;
    `ContractRevision` default 0, enters the contract fingerprint (spec 0016 §3.10).
- `ActionDescriptor(Name, Description, Mutation, Idempotent, Destructive, Mcp, ContractRevision,
  ResultType)`; `EntityActionDescriptor` adds `Action, SupportsFilter, LoadChildren, PostCheck,
  SingleTarget, IsBuiltIn, ArgumentsType`; `ApiActionDescriptor` adds `Securable` (`SecurableRef?`),
  `RequestType`, `HandlerType`. Order: built-ins, then `[EntityAction]`/`[ApiAction]` in declaration
  order, then stack companions' `[ApiAction]`s.
- `StackOperations` flags: `None 0, Query 1, Details 2, Save 4, Delete 8, Import 16, Export 32,
  Enlist 64`; `Read = Query | Details | Export`; `All = Read | Save | Delete | Import` (`Enlist`
  outside `All`). An absent operation is not projected and registers no securable.
- `IStackRegistry`: `Stacks`, `Services` (`ApiServiceDescriptor(Route, ServiceType, Actions)`),
  `Enlistments`, `Find(resource)`, `OwnerOf(TableName)`.
- `StackDescriptor` carries **no label key, no display-name template, no default ordering, no
  per-column UI hints, no tile layout, no per-language column metadata**.

### 2B.3 Annotations (0015 §2.3)

- `[Stack(Operations = All, MaxTake = 10000, CountCap = 10000, MaxSkipWindow = 100000,
  MaxSaveCount = 10000, MaxRowsPerSave = 100000, MaxIds = 10000, MaxDeleteByQueryRows = 10000,
  MaxExpandDepth = 3, MaxValidationRounds = 3, MaxSearchLength = 200, MaxExtras = 16,
  DefaultSelect = null)]` on entity type, inherited.
- `[EntityAction(Name, Action = null, SupportsFilter = true, LoadChildren = false, PostCheck = false,
  SingleTarget = false, Mutation = true, Idempotent = false, Destructive = false, Mcp = Full,
  Description = null, ContractRevision = 0)]` on a service method `M(ActionContext<TEntity, TKey>
  context [, TArguments arguments])` returning `Task`/`ValueTask`/`Task<TResult>`/`ValueTask<TResult>`.
- `[ApiAction(Name, Action = null, Resource = null, MemberOnly = false, Mutation = true,
  Idempotent = false, Destructive = false, Mcp = Full, Description = null, ContractRevision = 0)]`
  on a public method taking one body (or none) on an entity service, a stack companion or an
  `[ApiRoute]` service.
- `[ApiRoute(Route)]` on a non-entity service class (`"inbox"`, `"settings"`, `"access"`).
- `Name`: kebab-case segments joined by `/` (`invite`, `take-over`, `preferences/set`, `me/save`);
  unique per stack; reserved = standard segments. `Action` defaults to PascalCase of last segment.
- `Mutation` classification (0015 §2.3): not mutations — `query`, `get`, `get-by-ids`,
  `get-by-parent-ids`, `all`, `export`, `export-for-import`, `inspect-import`; mutations — `save`,
  `delete`, `delete-by-query`, `delete-with-descendants`, `activate`, `deactivate`, `import`,
  `export/start`, `export-for-import/start`, `import/start`. Read only by spec 0011's tenant-state
  verdict and spec 0016's MCP read-only gate.
- `Destructive` = "confirmation" (read by 0016; usable by a UI for confirm dialogs).
- `SingleTarget`: body is `IdRequest`/`IdRequest<TArguments>` (spec 0016 §3.3); must return own result.
- `[DefaultSelect]`, `[RelatedSelect]`, `[DetailsExpand]` attribute signatures are not given in
  0015's contract block (only `[Stack].DefaultSelect`); startup validates their text against the
  tenant-independent schema (0015 §2.6).

### 2B.4 Standard operations, segments, securables (0015 §2.4)

| Operation | Member | Exists when | Segment | Securable action |
|---|---|---|---|---|
| Query | `QueryAsync` | `Query` | `query` | `Read` |
| Details, one | `GetByIdAsync` | `Details` | `get` | `Read` |
| Details, many | `GetByIdsAsync` | `Details` | `get-by-ids` | `Read` |
| All cached | `GetAllCachedAsync` | `Details` + `cacheable` | `all` | `Read` (`FilterRoot = null`) |
| Get by parent ids | `GetByParentIdsAsync` | `Query` + `tree` | `get-by-parent-ids` | `Read` |
| Save | `SaveAsync` | `Save` | `save` | `Save` |
| Delete by ids | `DeleteByIdsAsync` | `Delete` | `delete` | `Delete` |
| Delete by query | `DeleteByQueryAsync` | `Delete`; web surface only | `delete-by-query` | `Delete` |
| Delete with descendants | `DeleteWithDescendantsAsync` | `Delete` + `tree` | `delete-with-descendants` | `Delete` |
| Activate/deactivate | `ActivateAsync`/`DeactivateAsync` | `Save` + `activatable` | `activate`, `deactivate` | `Activate` |
| Export | 0019 `ExcelOperations<T>` | `Export` | `export`, `export/start` | `Read` |
| Export for import | 0019 | `Import` | `export-for-import`, `export-for-import/start` | `Read` |
| Inspect import, import | 0019 | `Import` | `inspect-import`, `import`, `import/start` | `Save` |
| Custom bulk action | `[EntityAction]` | declared | its `Name` | its `Action` |
| Custom operation | `[ApiAction]` | declared | its `Name` | its `Action` |

- Segments belong to spec 0016's projection; HTTP verbs and route templates are spec 0016's. 0015
  shows only two: `POST /{tenantId}/api/web/<segment>/<name>` for `[ApiAction]`s (0015 §11.3) and
  `POST …/invoices/post` with body `{ ids, arguments }` for an `[EntityAction]` (0015 §11.2).
  Consult the 0016 digest for the rest.
- **No aggregate-specific operation or segment exists**; aggregation rides `query` via
  `QueryRequest.Aggregate` (2B.7).
- The "export menu" items in the brief map to `export` (grid), `export-for-import`, and the import
  template; the template's operation is spec 0019's, not listed here.

### 2B.5 Ceilings — `StackLimits` (0015 §2.5)

| Ceiling | Default | Bounds |
|---|---|---|
| `MaxTake` | 10,000 | `QueryRequest.Take`; **absent = 50** |
| `CountCap` | 10,000 | capped total; `RowPage.Count` = `cap + 1` above it |
| `MaxSkipWindow` | 100,000 | `Skip + Take` (`Limit = "skipWindow"`) |
| `MaxSaveCount` | 10,000 | entities per save |
| `MaxRowsPerSave` | 100,000 | rows at every depth |
| `MaxIds` | 10,000 | ids per get-by-ids/delete/action/get-by-parent-ids |
| `MaxDeleteByQueryRows` | 10,000 | rows a delete-by-query may match |
| `MaxExpandDepth` | 3 | navigation depth of the details projection |
| `MaxValidationRounds` | 3 | scheduler dispatches |
| `MaxSearchLength` | 200 | characters of `Search` |
| `MaxExtras` | 16 | extras per details request |

- Exceeding → `LimitExceededException(Limit, Actual, Maximum)` before any round trip; **never
  silently clamped** (clamping breaks paging arithmetic). Spec 0016 may only lower per surface:
  web `MaxEntitiesPerSave = 1000`, MCP `MaxTop = 500` (0015 §2.5).
- `[MaxChildren]` per collection: 10,000 per parent.
- `PipelineLimits` (0015 §5.2 step 3): the `QueryexLimits` profile of every pipeline compile —
  `MaxParameters = 1024`, `MaxJoins = 64`, others `QueryexLimits.Default` (spec 0008).
- The limits are on the descriptor but 0015 defines no wire exposure of them; the SPA's paging
  control and "last page" arithmetic need `MaxSkipWindow` and `CountCap` (gap).

### 2B.6 `EntityService<TEntity, TKey>` (0015 §3.1–§3.4)

- Non-virtual operations: `QueryAsync` → `RowPage`; `GetByIdAsync`, `GetByIdsAsync`,
  `GetAllCachedAsync`, `SaveAsync`, `ExecuteActionAsync` → `EntitiesResult<TEntity>` (or the
  action's `TResult`); `DeleteByIdsAsync(ids, expectedStamps = null)`, `DeleteByQueryAsync` →
  `AffectedResult`; `ExcelRowSource(ExportSource)`. Extensions gated by constraint: `ActivateAsync`/
  `DeactivateAsync` (`IActivatable`), `GetByParentIdsAsync` → `QueryRowSet` and
  `DeleteWithDescendantsAsync` (`TreeEntity<TKey>`).
- Hooks (empty defaults): `PreprocessAsync`, `ValidateAsync`, `ValidateDeleteAsync`,
  `ValidateActionAsync`, `ContributeAsync`, `AfterCommitAsync`, `ContributeDetails`,
  `SearchFilter(search)`. Components with multiplicity: `IEntityValidator<in T>`,
  `IPersistEffect<in T>`, `IDetailsContributor<in T> { Extras; Contribute }` (contravariant).
- `GetByIdAsync`: `NotFoundException` when absent or invisible (indistinguishable).
  `GetByIdsAsync`: partial — absent/invisible omitted, `EntitiesResult.Ids` = returned ids in
  request order; never throws for absence.
- Pack/distro extension (0015 §3.3): `CenterService<TCenter> : EntityService<TCenter> where
  TCenter : Center`; distro `MyCenter : Center` + `UseEntity`, or a derived service calling `base.`.
- Worked recipe (0015 §3.4): `[Multilingual]` on `Name` projects "`(E)`/`(ع)` labels" — the only
  mention of per-language label suffixes in these two specs; the label machinery is spec 0013's.

### 2B.7 Requests, options, results (0015 §4)

- `DetailsRequest { string? Select (row-echo select; null = no echo); IReadOnlyList<string> Include
  (extras by name; ≤ MaxExtras); static None }`.
- `SaveOptions { ReturnEntities = true (false by default when Source = Import); Details =
  DetailsRequest.None; Concurrency = Check; Source = Web; Action<IDataBatch>? OnPersist }`.
  `enum SaveSource { Web, PublicApi, Agent, Import, System }`. `OnPersist` never set from the wire.
- `ActionOptions(bool ReturnEntities = false, DetailsRequest? Details = null)`.
- Wire records consumed verbatim from spec 0016 (`Tellma.Core.Abstractions.Api`) (0015 §4.2):

```
QueryRequest { string? Select; string? Filter; string? Having; string? OrderBy; int Skip = 0;
  int? Take; string? Search; IReadOnlyDictionary<string, JsonElement>? Arguments;
  bool IncludeCount = false; bool IncludeAncestors = false; bool IncludeInactive = false;
  bool Aggregate = false }
ParentIdsRequest(IReadOnlyList<long>? ParentIds, string? Select, string? Filter,
  IReadOnlyDictionary<string, JsonElement>? Arguments, bool IncludeInactive = false)
DeleteByQueryRequest(string Filter, IReadOnlyDictionary<string, JsonElement>? Arguments, int ExpectedCount)
EntitiesResult<TEntity> { IReadOnlyList<long> Ids; IReadOnlyList<TEntity> Entities;
  RelatedEntities? Related; IReadOnlyDictionary<string, JsonElement>? Extras; QueryRowSet? Rows }
AffectedResult(int Count)
ExportSource.ByIds(Ids, OrderBy?) | .ByQuery(Filter?, Search?, OrderBy?, Arguments?, IncludeInactive) | .All
```

- **An aggregate query exists**: `QueryRequest.Aggregate` and `QueryRequest.Having` pass "as sent"
  into the `QuerySpec` (0015 §5.2 step 3); the result is the same `RowPage`, with
  `QueryColumn.GroupingKey` marking dimension columns (0012 §3.3). 0015 states nothing more:
  no grand totals or subtotal levels, no pivot shape, no rule for how `IncludeCount`,
  `IncludeAncestors`, `Search`, `DefaultSelect` or the activatable conjunct combine with an
  aggregate. Aggregate semantics are spec 0008's.
- `QueryRequest.Skip` default 0; `Take` default 50 when absent.
- **Wire ids are `long`** in request records (`ParentIds`, `EntitiesResult.Ids`, `ExportSource.ByIds`);
  narrowed to `TKey`; out of range → `BadRequestException` (0015 §4.2).
- `EntitiesResult.Ids` in input order (save) / request order (get-by-ids); `Entities` parallel;
  `Related` restricted to each entity's `RelatedSelect`; `Rows` = row echo, one row per entity in
  the same order, compiled from `DetailsRequest.Select`.
- `GetByParentIdsAsync` returns a bare `QueryRowSet` (no count, no ancestors).
- Arguments (0015 §4.3): JSON values keyed by parameter name; types inferred by `DiscoverQuery`
  (cached per `(schema fingerprint, clause texts)`); non-convertible → `BadRequestException`
  naming the parameter; unmentioned argument ignored; mentioned-but-missing → explicit `null`.
  `me()`, `today()`, `now()` and the time zone are bound from the tenant zone, never by the
  pipeline. The SPA can therefore send report parameters such as `@StartDate` with `query`; a
  missing required parameter silently becomes `null` (no "required" notion server-side).

### 2B.8 The guarded runner (0015 §5.1)

- Every batch through spec 0014's `IGuardedBatchRunner.RunAsync<TResult>(BatchPurpose, compose,
  read)`; connects from the `(TenantId, Subject)` cache or a cold prologue-only round trip.
- Outcomes: unknown/deactivated caller → `TenantNotFoundException`; stale permissions tag →
  recompose once, then `StaleContextException`; stale `settings` on a `Read` batch is served while
  fresh settings ride the round trip; on `Validate`/`Persist` it fails the guard like stale
  permissions. On a `ReadOnly` tenant, `Persist` batches are refused before composing
  (`TenantUnavailableException`).
- `DataAccessScope.Operation = "<Resource>:<operation>"` per operation.

### 2B.9 Query (0015 §5.2)

1. Type-level grant `RequireAsync(Resource, "Read")`.
2. Ceilings: `Take` absent = 50; `Take > MaxTake`, `Skip + Take > MaxSkipWindow`, `Search` >
   `MaxSearchLength` → `LimitExceededException`.
3. `QuerySpec`: `Root = Resource`; `Select = request.Select ?? DefaultSelect`; `Filter = And(user
   filter Leaf(request.Filter), activatable conjunct Leaf("IsActive = true") unless IncludeInactive,
   search filter, access filter)`; `Having`, `OrderBy`, `Aggregate` as sent; `Skip`/`Take` slots.
   Compiled under `PipelineLimits`; any diagnostic → `InvalidQueryException(Diagnostics)` before
   the round trip (diagnostics are the source of the advanced filter's validation messages).
4. Navigation-traversal rule: a path into entity `E` (not the root) may touch every column of `E`
   only when the caller's `Read` on `E` is unrestricted; a filtered or absent grant limits the path
   to `E`'s `RelatedSelect` in select, filter, order and having alike, else
   `ForbiddenException("forbidden", E, "Read")`. Joined rows are not RLS-filtered (filter at root
   only). `Geography` select items are refused: `InvalidQueryException` with
   `ValidationCodes.QueryGeographyNotSelectable`, `Location = "Select[i]"`, `Arguments = [("item", text)]`.
5. `Rows(spec, arguments, RowQueryOptions { CountCap = IncludeCount ? CountCap : null,
   IncludeAncestors, AncestorsFilter = access filter, CaptureKeys = IncludeAncestors })`.
6. Returns `RowPage`; `Ancestors` is a separate `QueryRowSet` in the same column shape, excluding
   page rows, filtered by the access filter only "so the tree renders complete branches" and "so
   the client distinguishes matches from context".

- **Column editor implication:** a user with a filtered `Read` on `gl.Center` who adds a column
  `Center / <column outside RelatedSelect>` gets a 403 for the whole query. The SPA needs to know,
  per navigation target and per user, which columns are reachable (gap).
- `IncludeAncestors` on a non-tree stack: behaviour not stated (0012 says "tree roots only").
- No default `OrderBy` is stated for `query`; `GetByParentIdsAsync` defaults to `OrderBy = "Id"`.
  Stable paging across pages without an order (or with a non-unique order) is not addressed.

### 2B.10 Search (0015 §5.2.1)

- Lowered to `FilterTree.Or` over `SearchableProperties` present in the tenant schema (absent twins
  omitted): `contains(P, @search)` or `startsWith(P, @search)`; plus `Id = @searchId` when the text
  parses as an integer and the key is integral.
- Convention when nothing is `[Searchable]`: `Name`, `Name2`, `Name3`, `Code` that exist, all
  `Contains`.
- `SearchFilter(search)` override replaces the disjunction. "There is no picker-versus-page hint:
  the select list already says what the client shows."

### 2B.11 Details (0015 §5.3)

- One round trip on `Read` after the `Read` check and `MaxIds`/`MaxExtras`.
- Plan: (1) main ids `Rows(Select = "Id", Filter = access filter, KeySetRestriction ids TVP,
  CaptureKeys)` — RLS decides visibility here only; **activatable conjunct not applied** (details
  shows inactive rows); (2) `Query<TEntity>` with every mapped property, every child collection
  (dotted for grandchildren), `RelatedEntities` for every navigation in `DetailsExpand` (depth ≤
  `MaxExpandDepth`, each related entity restricted to its `RelatedSelect`, **no row-level filter**
  on related); (3) extras per `request.Include` via `ContributeDetails`/`IDetailsContributor`
  (`DetailsPlan.AddExtra(name, sql, options)` or `AddExtra(name, QuerySpec, arguments)`; unknown
  name → `BadRequestException`; result in `EntitiesResult.Extras[name]` as `QueryRowSet` or raw
  first set); (4) row echo when `request.Select` is given — "the search page's cached row
  refreshed from the details read so the two never desync".
- `DetailsPlan<TEntity> { Request, Context (RequestContext), IdsSource (SqlIdentifier), AddExtra }`;
  `DetailsPlan<TEntity, TKey> { Ids }`. A second `AddExtra` for one name → `InvalidOperationException`.
- Same plan serves the save read-back (0015 §6.9) and spec 0019 hydration (`DetailsRequest.None`).
- **Details always selects every mapped property** of the root; there is no select for the entity
  itself, so a details page cannot ask for less (or for a navigation's non-projection columns).
- Extras are all-or-nothing per request, fetched in the same round trip; there is no lazy per-panel
  fetch operation other than calling `get` again with a different `Include` (which re-reads the
  entity).

### 2B.12 Get by parent ids, get all cached (0015 §5.4, §5.5)

- `GetByParentIdsAsync(ParentIdsRequest)`: one `Read` round trip; `Select ?? DefaultSelect`;
  filter = `Leaf(Filter)` + activatable conjunct unless `IncludeInactive` + access filter + for the
  roots `ParentId = null`; `KeySetRestriction("ParentId", parent ids TVP)` except for roots;
  `OrderBy = "Id"` "unless the select carries an `OrderBy`" (but `ParentIdsRequest` has no
  `OrderBy` member). Null/empty `ParentIds` = roots; > `MaxIds` → `LimitExceededException`.
- 0015 claims "the result renders the initial tree plus every expanded node in one call", but the
  request expresses either roots (null/empty) or children of the listed parents, not both; the
  combined call needs `ParentIds` to admit a roots marker or a second call.
- `GetAllCachedAsync`: `[Cacheable]` only; from spec 0013's `ICacheableEntities.GetAsync<TEntity>()`;
  no row filter; oversized → one ordinary `Query` on every call. Segment `all`.

### 2B.13 The save pipeline (0015 §6)

- Steps (0015 §6.1): (1) shape and ceilings, BCL `ValidationAttribute`s on every entity and child
  with **all** failures reported, id rules, duplicate id → `Entity.DuplicateId`; zero round trips
  on error; (2) `RequireAsync(Resource, "Save")`; (3) preprocess then `PreprocessAsync`; (4) id
  reservation (assignment before validators); (5) **RT1** context round (`Validate` batch: before
  images under `Read` filter, `Save`-grant count, child before images, tree chains, validator
  loads) — skipped for a pure create with nothing to load; (6) validate; (7) assemble persist
  batch; (8) **RT2** persist; (9) post-commit; (10) response.
- Common case: 1 round trip for a create with no loads, 2 for an update.
- Preprocessing (0015 §6.3): strings trimmed, empty nullable → `null` (`[PreserveWhitespace]`
  opts out); nested children get their parent key from the nesting; server-owned/derived reset;
  unconfigured twins dropped. The SPA should expect the read-back to differ from what it sent.
- Ownership (0015 §6.2): server-owned client values ignored silently (an old client may send them);
  `ModifiedAt` read as expected stamp first; write-once differing → `WriteOnce` at path (equal or
  default passes); derived discarded; database-owned ignored; an unconfigured multilingual twin is
  discarded silently, except `Source = Import` → `Import.LanguageNotConfigured`.
  `[BlobReference]` is `Editable` with spec 0017's attach validator.
- Child sync (0015 §6.5): `null` untouched; `[]` delete all; a list → `Id ≤ 0` insert, `Id > 0`
  update, absent deleted; a child `Id > 0` not among the parent's before-image children →
  `Entity.NotFound` at `[i].<Collection>[j].Id`; any child change stamps the parent.
- Validation order (0015 §6.6): existence → `NotFoundException(Resource, missing ids)` (404);
  `Save`-grant count below loaded → `ForbiddenException("forbidden", Resource, "Save")` (403, no
  ids); `Concurrency.StampRequired` at `[i].ModifiedAt` for a default stamp; early stamp mismatch →
  `ConcurrencyException`; `WriteOnce`; child identity; `Unique` at path (in-payload duplicates and a
  `ByKey` load); `Tree.Cycle`/`Tree.TooDeep` at `[i].ParentId`; validators. **All errors are
  collected**; nothing stops at the first.
- Persist batch (0015 §6.7): emitter → service `ContributeAsync` → effects (blob effect first) →
  access guards (`ContributeLock` before, `ContributeInvariants` after) → post-check over
  `@tb{b}_saved` → read-back when `ReturnEntities`. Enlisted groups at `BeforeHost`/`AfterHost`.
- Import (0015 §6.8): chunks ≤ min(`MaxSaveCount`, `MaxIds`) parents and ≤ `MaxRowsPerSave` rows;
  `ReturnEntities = false`, `Source = Import`; a sheet column mapped to `IsActive` is
  `Excel.Import.ServerOwnedColumn`.
- Read-back (0015 §6.9): the details plan inside the transaction over `@tb{b}_saved`; the response
  is exactly what committed; `Ids` in input order with final ids (temporary ids resolved).
- **Details page implication:** a successful save returns the saved entity in details shape (with
  `SaveOptions.Details` controlling echo and extras), so "reload after save" can be zero extra
  calls. `SaveOptions` is a service-level shape; whether the wire save body carries `Details`,
  `ReturnEntities` and `Concurrency` is spec 0016's.

### 2B.14 Validation contexts and the loader (0015 §7.1, §7.2)

- `SaveContext<TEntity>` (`Entities`, `Before(i)`, `Options`, `Loader`, `Errors`, `Round`, `Host`,
  `IsNew(i)`, `Changed(i, p)`), `DeleteContext`, `ActionContext` (adds `Batch`, `Persisted`,
  `Save`, `Update`, `Notify`, `Withdraw`). `IContextLoader` (`ByIds`, `ByKey`, `ByParentIds`,
  `Query`, `Visible`, `Rows`, `Exists`, `Count`, `Ancestors`, `Sql`, `LoadAsync`, `Prime`):
  validators park at `LoadAsync`, one batch per round, structural dedup, rounds ≤
  `MaxValidationRounds` (server-internal; no SPA impact).

### 2B.15 Validation errors and paths (0015 §7.3) — what the SPA receives (via 0016)

- `ValidationPath(IReadOnlyList<PathSegment> Segments)`; `PathSegment` = `IndexSegment(int Index)`
  | `PropertySegment(string Name)` (CLR property name); `ValidationPath.Root` = whole payload;
  `ToString()` = `"[3].Lines[1].Quantity"` (logs/tests only).
- Spec 0016 renders property segments through its JSON naming policy:
  **`entities[0].roleMemberships[2].roleId`** (camelCase, rooted at `entities`); spec 0019 maps
  through its coordinate map. A root-level name is a property then an index (`Ids[2]`).
- Child-row errors are addressed by **index into the payload arrays**, not by child id:
  `[i].<Collection>[j].<Property>`. The SPA must map indices back to the rows it sent (the save is
  an array; the details page sends one entity, so `[0]`).
- `ValidationErrors { HasErrors; Items; Add(path, code, arguments); Add<TEntity,TValue>(index,
  property, code, arguments) }`.
- `ValidationError(Path, Code, Arguments) : CodedError(Code, Arguments)`.
- `ValidationCodes`: `Required`, `MaxLength`, `Range`, `Unique`, `WriteOnce`, `Precision`,
  `Entity.NotFound`, `Entity.DuplicateId`, `Fk.NotFound`, `Fk.InUse`, `Tree.Cycle`, `Tree.TooDeep`,
  `Blob.NotAttachable`, `Blob.DuplicateReference`, `Concurrency.StampRequired`,
  `Import.LanguageNotConfigured`, `Query.GeographyNotSelectable`. Pack/distro codes beside them
  (`Centers.ParentMustBeGrouping`, `Users.NotService`); codes are dotted PascalCase resource keys;
  a platform minor may add codes, never rename.
- Arguments are named raw values (strings, numbers, booleans, `DateOnly`, `DateTimeOffset`), never
  pre-formatted, feeding ICU MessageFormat **twice**: on the server under the request culture, and
  **in the SPA from spec 0013's string pack in the language the user is viewing**; a property is
  named by its CLR name under the argument `property`, "which each renderer resolves to a label
  itself" — the SPA needs a property → label resolution (gap: no label metadata in 0012/0015).
- Spec 0016 returns code, arguments and rendered message.
- Enlisted-write errors are re-rooted through `MapPath` and carry `enlistedResource` (0015 §13.3).

### 2B.16 Uniqueness and foreign keys (0015 §7.4)

- Uniqueness: validator (`Unique` at path) + index; 2601/2627 on `UX_...` → `Unique`, located by
  index name.
- FKs validated under the target's `Read` filter via `Loader.Visible<TTarget>`: missing or
  invisible → `Fk.NotFound` at the property path with arguments `entity` (target entity name) and
  `id`. 547 → `Fk.NotFound` on save, `Fk.InUse` at `Ids[i]` on delete with `entity` (referencing
  entity name) and `property`. SQL Server names only the first violated constraint.

### 2B.17 Concurrency (0015 §8)

- Stamp `ModifiedAt datetimeoffset(7)`; one `SYSUTCDATETIME()` per persist batch.
- **On the wire the client echoes the `ModifiedAt` it loaded; "the string round-trips verbatim with
  seven fractional digits (the SPA keeps the raw string and parses a copy for display)"** — a
  direct SPA obligation (JS `Date` has millisecond precision; parsing then re-serialising breaks
  the check).
- Conflict outcome: all missing → `NotFoundException(Resource, ids)`; else
  `ConcurrencyException("concurrency-conflict", conflicts)` with per id `IsMissing` and, for existing
  rows, `ModifiedAt`, `ModifiedById`, `ModifiedByName` (resolved under the request culture through
  spec 0013's language shape, primary name when the twin is empty; "the wire carries one string").
  **"The UI offers 'overwrite' and resends with `Concurrency = Override`."**
- `Override` skips only the comparison; missing rows still conflict; metered
  `tellma.crud.concurrency.overrides`.
- "The web details-page save always sends `Check`" (0015 §8).
- Actions do not check stamps but stamp the rows they change. Delete accepts optional expected
  stamps per id. Children carry no stamp.
- Ambiguous failure: inserted ids probed; update-only → `DependencyUnavailableException` "with a
  reload instruction".

### 2B.18 Row-level security in the pipeline (0015 §9)

- Type level: `IAccessEvaluator.RequireAsync(Resource, Action)`; none → `ForbiddenException
  ("forbidden", Resource, Action)`; sensitive pair under-assured → `StepUpRequiredException`.
  Denials older than spec 0014's `FastDenyWindow` re-verified by one prologue-only round trip.
- `decision.Filter` conjoined into every read/update/delete; `AncestorsFilter` = access filter.
- Save pre-check: before images under `Read`, count under `Save` → 404 vs 403 (no ids named).
- Delete/actions: 50404 (hidden = missing, ids named) then 50403 (no ids named).
- Post-check: save always, actions with `PostCheck = true`; `50403` → `ForbiddenException`.
- Related entities in details are not RLS-filtered and are restricted to `RelatedSelect` (0015 §9.4).
- **The SPA learns nothing up front about whether the user may Save/Delete/Activate a given row**;
  type-level permission comes from spec 0014's permission set (if cached client-side), row-level
  outcomes only from attempting the write (gap for disabling buttons per row).

### 2B.19 Deletes (0015 §10)

- `delete` (by ids): `MaxIds`; `Delete` check; null stamp beside an id → `Concurrency.StampRequired`
  at `Ids[i]`; one `Persist` round trip without a delete hook (2 with `ValidateDeleteAsync`);
  **strictly all-or-nothing** — 50404 (missing/hidden, ids named) → `NotFoundException`; 50403 →
  `ForbiddenException`; 50409 → `ConcurrencyException`; 547 → `ValidationException(Fk.InUse)` at
  `Ids[i]` with `entity` and `property` (no C# reference pre-check). Review flag 4: best-effort
  per-id outcomes rejected; "flips if bulk-delete UX demands partial success" (0015 §10.1).
- `delete-by-query`: blank `Filter` → `BadRequestException`; compiled `And(Leaf(Filter), access
  filter)`, **no activatable conjunct** (inactive rows matched are deleted); capped `Count` then
  `Delete.ByQuery` with `Cap = MaxDeleteByQueryRows`, `ExpectedCount` from the request; 50413 →
  `LimitExceededException("MaxDeleteByQueryRows", actual, cap)` ("run it as a background job" — no
  such job is defined); 50428 → `CountMismatchException(expected, actual)` (409). Web surface only
  (`Mcp = Hidden`); "the web UI keeps it under an advanced menu". Returns `AffectedResult(count)`.
  The SPA must obtain the count first (a `query` with `IncludeCount`) and send it as `ExpectedCount`;
  above `CountCap` the count is unknown (`cap + 1`) (0015 §10.2).
- `delete-with-descendants` (trees): roots under `Read` (404), closure by node, every closed row
  under `Delete` (403; never a partial subtree) (0015 §10.3). A plain `delete` of a parent with
  children fails with `Fk.InUse` (0012 §9.2).

### 2B.20 Actions (0015 §11)

- Built-in `activate`/`deactivate`: `EntityActionDescriptor` with `Action = "Activate"`,
  `SupportsFilter = true`, `PostCheck = false`, `Idempotent = true`; SQL-only fast path (1 round
  trip) via `UpdateSpec.ByIds(..., { IsActive: target }, Stamp: true, AllOrNothing: true)`; rows
  already in state skipped (no stamp). One securable for both directions. A veto
  (`Users.CannotDeactivateSelf`) moves it to the general path (0015 §11.1).
- General path (0015 §11.2): `MaxIds`; `descriptor.Action` check; arguments as `TArguments` (web) or
  `JsonElement` deserialised (MCP, jobs), `ValidationAttribute`s walked; **RT1** loads target rows
  under the grant filter (missing → `NotFoundException`), with children when `LoadChildren`; then
  `ValidateActionAsync` + the method over `ActionContext` (`Save`, `Update`, `Batch`, `Notify`,
  `Withdraw`, `Host`); **RT2** persist (emitter under `Override`, update specs, post-check when
  `PostCheck`, read-back when `ReturnEntities` and no own result). Answer: `EntitiesResult` with
  target ids in request order, or the method's `TResult`. Common cost 2 round trips.
- `ActionContext.Persisted` lets a method read its own writes after commit.
- `PartialFailureException(Code, Results, Failed)` → **502 `partial-failure`**: RT2 still runs with
  what was composed; caller gets `Results` and `Failed` (spec 0018 `invite`) (0015 §11.2).
- Illustration: `[EntityAction("post", LoadChildren = true, ...)]` projects `(gl.Invoice, Post)`,
  `POST …/invoices/post` with body `{ ids, arguments }`, the MCP action, "and the UI button
  metadata" — the button metadata's shape and endpoint are not defined anywhere in 0015.
- `[ApiAction]`s (0015 §11.3): reached only through `IApiActionInvoker.InvokeAsync(action, body)`;
  spec 0016 projects **`POST /{tenantId}/api/web/<segment>/<name>`**; stack companions
  (`contribution.EntityCompanion<TEntity, TCompanion>()`) add `[ApiAction]`s under the stack's
  segment (spec 0019 `ExcelOperations<T>`); `[ApiRoute]` services via `contribution.ApiService<T>()`.

### 2B.21 Capability recipes (0015 §12)

| Capability | Declared by | Projects (abridged) |
|---|---|---|
| Keyed, audited | `TopLevelEntity<TKey>` | `Read`/`Save`/`Delete`; `query`, `get`, `get-by-ids`, `save`, `delete`, `delete-by-query`; concurrency; audit server-owned |
| Activatable | `IActivatable` | `Activate`; `activate`/`deactivate`; conjunct lifted by `IncludeInactive`; `IsActive` server-owned; **"the inactive-banner metadata"** (undefined); `ActiveSubtreeCount` |
| Tree | `TreeEntity<TKey>` | `get-by-parent-ids`, `delete-with-descendants`; `IncludeAncestors`; `ParentId` editable; cycle validation; recount; temporary ids in `ParentId` |
| Temporal | `[Temporal]` | skip-unchanged rows. **No history read operation** (no "edit history" extra or `as of` query) |
| Blob reference | `[BlobReference]` | `BlobReferenceValidator<T>`, `BlobReferenceEffect<T>`; excluded from Excel |
| Multilingual | `[Multilingual]` | twins gated by `MultilingualShape`; `(E)`/`(ع)` labels; search over configured columns |
| Cacheable | `[Cacheable(MaxRows)]` | `entity:<Name>` tag bumped by every write; `all`; `Read` with `FilterRoot = null`; **`settings/entity-tags` lists the tag** |
| Job entity | `IJobEntity` | `JobId` server-owned |
| Searchable | `[Searchable(Kind)]` | the `Search` disjunction |
| Excel | `Export`/`Import` | the 0019 companion operations; `ExcelRowSource` |

- Read-only stacks: `[Stack(Operations = Read)]` (0015 §12). A distribution cannot remove a pack
  capability from a leaf.
- Activatable conjunct applies to `query`, `get-by-parent-ids`, Excel `ByQuery`; never to details
  by id, delete by query, or actions (0015 §12.2). A create always produces an active row.

### 2B.22 Side effects, post-commit, hub events (0015 §13.1, §13.2)

- `PersistContext<TEntity> { Entities; Context; Batch; AffectedIds; Captures; Enlistment;
  Notify(NotificationRequest); Withdraw(WithdrawRequest) }`; kinds `SavePersistContext` (+ `Before`,
  `Options`, `NewIds`, `TouchedIds`), `ActionPersistContext` (+ `Action`, `Arguments`),
  `DeletePersistContext`. `ContributeAsync` runs for every persist on the stack's table.
- Participants may only append: raw `Sql` with declared `Writes` (+ `UserIds` where a user-level
  tag rule applies), `Tvp`, `DeclareIdTable`, readers read post-commit, `Notify`/`Withdraw` (spec
  0021's `INotifier`, ids assigned inside the statement, **the `inbox.changed` event registered
  through `OnCommitted`**), spec 0020's `IJobQueue.Enqueue(batch, …)`, spec 0017 confirm/release. No
  I/O. Distribution guard `THROW 50600–50699` → `ValidationException` at `ValidationPath.Root`.
- Tag bumps are never hand-written; derived from declared writes plus `BumpVersionTag`.
- `PersistOutcome<TEntity> { Entities; Stamp; Context; VersionTags; UserVersionTags }` with kinds
  `SavePersistOutcome`, `DeletePersistOutcome` (`DeletedIds`), `ActionPersistOutcome` (`Action`,
  `Arguments`, `Ids`).
- Post-commit (0015 §13.2): `AfterCommitAsync` then effects, then `OnCommitted` callbacks — named
  examples "**the hub nudge, `job.changed`**"; uses: "SignalR pushes", best-effort external calls,
  spec 0011's membership hint write-back. Failures logged and counted
  (`tellma.crud.effects.failures`), never change the response, never retried.
- **The only hub event names in these two specs are `inbox.changed` and `job.changed`**; there is
  no generic `entity.changed`/row-change push, no cache-invalidation event name (that is spec
  0013/0021's), and no assignment event.

### 2B.23 Enlisted writes (0015 §13.3), briefly

- Cross-stack writes run the target's pipeline stages inside the caller's frame (one transaction,
  one error list): `IEnlists<TTarget>` on the writer + `Enlist` in the target's `[Stack]`
  operations; `EnlistSaveAsync(host, rows, EnlistSaveOptions { Order = AfterHost; MapPath;
  Concurrency = Override; ServerOwned; Source })`, `EnlistDelete(host, ids, EnlistDeleteOptions
  { Order = BeforeHost; MapPath; Source })`; open frames (`[ApiAction]`, job, provisioning) persist
  through `IOpenWriteHost.PersistAsync()`.
- Enlisted writes skip the target's `Read`/`Save` filters, FK visibility pre-load, criteria
  providers, post-check and client stamp comparison; errors re-root through `MapPath` with
  `enlistedResource`. Allowed from validation hooks, `[EntityAction]` methods and validators, never
  from `PreprocessAsync`/`ContributeAsync`/`AfterCommitAsync`.
- Consumers (0015 §13.3 table): `import/start`, export and import handlers,
  `core.notification-delivery`, `users/invite`, a document's `post` into a ledger stack,
  `users/me/save` (3 round trips).
- Relevance to round 2: an assignments back end that documents write into (`assign` actions) is
  either an `Enlist` stack written through `IEnlists<Assignment>` by each document service, or a
  platform composer on the internal channel like `INotifier` (no enlistment, fixed-text
  statements). 0015 offers both precedents; neither is chosen for assignments.

### 2B.24 Exceptions and HTTP mapping (0015 §14)

| Exception | HTTP | Code |
|---|---|---|
| `ValidationException(Errors)` | 422 | `validation` |
| `NotFoundException(Resource, Ids)` | 404 | `not-found` |
| `ForbiddenException(Code, Resource, Action?)` | 403 | `forbidden` |
| `ConcurrencyException(Code, Conflicts)` | 409 | `concurrency-conflict` |
| `CountMismatchException(Expected, Actual)` | 409 | `count-mismatch` |
| `LimitExceededException(Limit, Actual, Maximum)` | 413 | `limit-exceeded` |
| `InvalidQueryException(Diagnostics)` | 400 | `query-invalid` |
| `BadRequestException(Detail?)` | 400 | `bad-request` |
| `IndividualRequiredException()` | 403 | `individual-required` |
| `DependencyUnavailableException(Dependency, RetryAfter?, Inner?)` | 503 | `dependency-unavailable` |
| `PartialFailureException(Code, Results, Failed)` | 502 | `partial-failure` |

- `ConcurrencyConflict(long Id, string? ModifiedAt, long? ModifiedById, string? ModifiedByName,
  bool IsMissing)`; `ModifiedAt` is the opaque stamp string.
- Owned elsewhere, same base `TellmaException { Code; Arguments }`: `StaleContextException` (0013,
  503), `TenantNotFoundException`, `TenantUnavailableException`, `TenantStateException`,
  `StepUpRequiredException` (0011), `BlobRejectedException` (0017), `ImportException` (0019).
  Everything else → 500 with a trace id; authoring bugs throw BCL exceptions (500).
- **Cancellation (0015 §14.1):** "An `OperationCanceledException` raised on the request's aborted
  token is neither mapped nor logged: the operation is abandoned, nothing is written to the
  response, and `tellma.crud.operations` counts it under `crud.outcome = cancelled`. A timeout the
  host imposes is spec 0016's 504."
- Translation table (0015 §14.2) adds: `DataAccessAmbiguousException` (inconclusive probe) and
  `DataAccessRetryExhaustedException` → `DependencyUnavailableException("database", RetryAfter =
  1 s)`; 50412/50401 → `StaleContextException`/`TenantNotFoundException` passed through;
  `TreeCycleException`/`TreeDepthExceededException` (SQL fence, 530) → `ValidationException` **at
  `ValidationPath.Root`** (the C# validator reports at `[i].ParentId`); unique race →
  `Unique` at `[i].<Property>` when exactly one payload row carries it, else at Root with
  `property` argument; blob unique index → `Blob.NotAttachable` (`reason = persist`).

### 2B.25 Cancellation, end to end (0012 §6.1, 0015 §14.1)

- The command runs with "the cancellation token of the scope" (0012 §6.1); 0015 treats an
  `OperationCanceledException` "on the request's aborted token" as `cancelled`. Read together they
  imply (not state) that an aborted HTTP request cancels the in-flight `SqlCommand`; neither spec
  names `HttpContext.RequestAborted` as the token's source or states it for every stage (the cold
  prologue-only round trip, the validation rounds, retry delays, the commit probe).
- `Read`/`Validate` batches are untransacted and idempotent, so cancelling them is safe.
- Cancelling a `Persist` round trip mid-flight is not addressed: under `XACT_ABORT` an attention
  before `COMMIT` rolls back, but an abort racing the commit leaves the client without an outcome;
  0015 §8's ambiguity handling covers connection drops, not client aborts.

### 2B.26 Excel row source (0015 §15)

- `IExcelRowSource { Pages(ExcelQuery); ReadAsync(queries) }` with `ExcelQuery(RootEntity,
  SelectPaths, OrderBy, Restriction, RestrictionValues, Take, CountCap, Dependents)`; `RootEntity`
  may be the stack's root, **a child root** (`core.RoleMembership`) as a dependent, or a lookup
  target under its own `Read`. `OrderBy = query ?? source ?? "Id"`. One `Read` batch per page of
  `MaxTake` roots, reconnected per page (a revocation mid-export stops the stream).
- `ExportSource` carries filter/search/order/arguments/`IncludeInactive` but **no select**: export
  columns are `ExcelQuery.SelectPaths`, chosen by spec 0019's codec.

### 2B.27 Telemetry and budget (0015 §16)

- Meter `Tellma.Core`; `CrudTelemetryNames`: `tellma.crud.operations` (tags `crud.operation` ∈
  `query, get, get_by_ids, all, save, delete, delete_by_query, delete_with_descendants, action,
  get_by_parent_ids, excel_rows`; `crud.outcome` ∈ `ok, validation, forbidden, not_found, conflict,
  limit, stale, unavailable, cancelled, error`; `crud.source`), `.operation.duration` (+
  `crud.lane` `small|large`), `.validation.rounds`, `.save.entities`, `.concurrency.conflicts`
  (`crud.kind` `stamp|missing|cycle`), `.concurrency.overrides`, `.effects.failures`,
  `.stale_context.reruns`, `.query.discover.duration`, `.enlistments`. No entity/tenant/user tags;
  entity is a span attribute (spec 0016 §10.1 `tellma.resource`, `tellma.operation`).
- Round-trip budget (0015 §16.2): query 1; details/get-by-parent-ids 1; all cached 0 warm; create
  1 (2 with unique property or validator load); update 2 (+1 per dependent round, max 4); deletes 1
  (2 with hook); activate 1; custom action 2; import chunk 2 (+1 hydration); Excel export 1 per page;
  +1 per retry. Cold connect cache, stale permission tag, or an aged denial each add one.
- Compatibility (0015 §17): minors may add hooks, members, codes, descriptor members; never rename
  codes, change path rendering, or change a default ceiling. "Servers ignore unknown JSON members; a
  web client built against a different contract is refused before anything runs (spec 0016 §3.10)."

## 3. Gaps for round 2

Capabilities the SPA (or the new back-end pieces) need that 0012 and 0015 do not provide. Some may
be covered by 0013 or 0016; the owning designer should confirm there before adding.

**Entity metadata for the client (spec 0029's territory)**

1. **No metadata endpoint or wire shape.** `EntityMetadata` and `StackDescriptor` are server-side
   records; 0015 §1.2 names "the SPA schema" as a descriptor consumer but nothing defines it. The
   search page, column editor, filter controls, entity picker and report studio all need property
   lists, types, navigations and capabilities client-side, cacheable for warm launch (keyed by the
   Queryex schema `Fingerprint` / contract fingerprint).
2. **No labels in metadata.** `PropertyMetadata` has no label key; the label-key convention
   (`<Schema>_<Entity>_<Property>`, e.g. `Gl_Center_Name`, `Gl_Center_Plural`, enum
   `Gl_CenterType_Abstract`) lives in spec 0013 §10.1 (found by grep, outside these sources). The
   column editor's "Center / Code" labels, the validation `property` argument ("each renderer
   resolves to a label itself", 0015 §7.3) and the details title all need key resolution
   client-side.
3. **No display-name concept.** The closest pieces are the related projection (`Id`, `Name` group,
   `Code`, avatar; 0012 §2.5) and `DefaultSelect` ("the columns pickers ... use", 0015 §2.2). There
   is no rule or template ("Code – Name", a `[DisplayName]` expression) for rendering any entity as
   a string in the user's language; the brief's details-page open question 2 is unanswered here.
4. **No client-visible Queryex type per property.** `PropertyMetadata` carries `ClrType`; the
   schema types enums as `String` (0012 §2.6), so a typed filter control cannot tell an enum from a
   string without `EnumValues`, which is server-only. `QueryColumn.Type` arrives only with results.
5. **No per-user reachability of navigation columns.** The traversal rule (0015 §5.2 step 4)
   403s a whole query that touches a non-`RelatedSelect` column through a navigation whose target
   `Read` is filtered or absent; the column editor cannot hide such columns without each target's
   `RelatedSelect` and the user's restricted/unrestricted `Read` state.
6. **No tile layout, default columns, default order, or form/details layout hints** on the
   descriptor (the brief puts default columns and tile layout "in the code per page", so this may
   stay client-side).
7. **Limits not exposed.** `StackLimits` (`MaxTake`, `CountCap`, `MaxSkipWindow`, `MaxSearchLength`,
   `MaxIds`) are on the descriptor only; the paging control and search box need them to avoid
   `LimitExceededException` (the server never clamps).
8. **Multilingual shape awareness.** `Name2`/`Name3` are absent from the schema on tenants that do
   not configure them; a select naming `Center.Name3` on a two-language tenant is an
   `InvalidQueryException`. The client select builder needs the tenant's `MultilingualShape` (spec
   0013) and must build selects per shape.

**Search page, trees, details**

9. **`get-by-parent-ids` cannot return roots and expanded children in one call** although 0015
   §5.4 claims it does; `ParentIdsRequest` has no `OrderBy` and no roots flag. Restoring remembered
   expansion needs either a request change or two calls.
10. **No default ordering contract for `query`** (stable paging, details-page prev/next across
    pages) when the client sends no `OrderBy`, or a non-unique one.
11. **No per-row capability flags** (may edit / delete / activate this row); only type-level grants
    (spec 0014) and attempt-and-fail. Matters for disabling the Edit and Actions menu entries.
12. **No action metadata on the wire** for custom `[EntityAction]`s ("the UI button metadata",
    0015 §11.2, and "the inactive-banner metadata", 0015 §12, are named but never shaped): the
    Actions menu needs name, label key, `Destructive`, `SingleTarget`, `ArgumentsType` schema and
    the `Action` securable.
13. **No history/audit read.** `[Temporal]` writes history; no operation or extra reads it (brief:
    "edit history" extra). An `IDetailsContributor` can add one, but nothing standard exists.
14. **No lazy extra fetch.** Extras ride the details read; a panel that loads later re-reads the
    entity through `get` with a different `Include`.
15. **Clone needs ownership on the client.** Stripping "Ids and all the server owned properties"
    (brief) requires `PropertyOwnership` per property client-side (`ServerOwned`,
    `DatabaseOwned`, `Derived` stripped; `WriteOnce` kept).
16. **Child-row drill-through targets.** Errors and reports address child rows by index or id;
    nothing maps a child id to its parent id and collection except a Queryex select over the child
    root's parent navigation (which itself needs a query surface, gap 17).

**Reports and dashboards (specs 0033, 0034, 0035)**

17. **No query surface over child entities.** Child entities are Queryex roots (0012 §11.1) and the
    Excel row source reads them as dependents (0015 §15), but stacks exist only for top-level
    entities and child entities are "never addressed by a service of [their] own" (0012 §2.1).
    Reports rooted at invoice lines (brief) need a `query` on child roots under the owner's `Read`
    grant (spec 0014 §5.5's child securables via `FilterTree.Via` are the hook).
18. **Aggregate query is minimal.** `Aggregate` + `Having` exist on `QueryRequest`, but there are
    no subtotals, rollups or grouping sets (the pivot needs SQL aggregation "for every (row,
    column) pair at every level"), no grand totals, no tree-dimension rollup (parents aggregating
    descendants via `Node`), no stated interplay with `IncludeCount`, `Search`,
    `IncludeAncestors`, `DefaultSelect` or the activatable conjunct, and a 10,000-row `MaxTake`
    ceiling on aggregate output.
19. **No multi-query request.** A dashboard or pivot that needs several queries pays one HTTP round
    trip each; `IDataBatch` can batch them but no wire operation does.
20. **No required-parameter notion.** A clause parameter without an argument binds `null` silently
    (0015 §4.3); report-level "Required" validation must live in the report engine.
21. **Report-definition and dashboard JSON** would be `[JsonColumn]`s: opaque, absent from Queryex
    (0012 §2.6), so neither searchable nor filterable inside the JSON. Sharing rows would be child
    collections (`[MaxChildren]` default 10,000) or a separate stack; per-user visibility would be an
    `IAccessCriteriaProvider` criterion (spec 0014 §5.1, cited in 0015 §9.1).

**Assignments, cross-tab, observability**

22. **No assignment entity, statements or hub event.** The only post-commit events named are
    `inbox.changed` and `job.changed` (0015 §13.1, §13.2). An assignments back end must pick
    between an `Enlist` stack and an `INotifier`-style internal composer (0015 §13.3, §13.1).
23. **No row-change push for open pages.** Nothing tells an open search or details page that a row
    changed; staleness surfaces only as a 409 on save.
24. **Cancellation is implicit.** 0012 §6.1 and 0015 §14.1 imply that an aborted request cancels
    its SQL, but neither names the token source, says whether validation rounds and retry delays
    observe it, or covers a client aborting a persist (the SPA should never abort saves).
25. **Trace id per operation** is promised for 500s (0015 §14.1), but the trace and slow-request
    surfacing is spec 0016's; nothing in 0012/0015 exposes server timing to the client
    (`DataBatch.SlowRoundTrip` is server log only).

## 4. Likely change points

| Section | Why a round-2 design will likely edit it |
|---|---|
| 0012 §2.2 (annotations) and 0015 §2.3 | Add a display-name or label declaration (`[DisplayName]`-style) or formally define `[DefaultSelect]`/`[RelatedSelect]`/`[DetailsExpand]`, whose signatures are referenced but never shown |
| 0012 §2.5 (related projection) | Decide whether the related projection *is* the display name; may need `Code` + `Name` ordering or a display template; brief wants nav columns to expand to `Id` + three names |
| 0012 §3.1 (`EntityMetadata`, `PropertyMetadata`) | Add label keys, Queryex type, enum member label keys if the client entity model (0029) is projected from it |
| 0015 §2.2 (`StackDescriptor`) | Expose a client projection ("the SPA schema"), action button metadata, the inactive-banner metadata, limits; possibly default order and tile defaults |
| 0015 §4.2 (`QueryRequest`, `ParentIdsRequest`) | Aggregate extensions for pivots and charts (levels/rollup/totals), multi-query, `ParentIdsRequest.OrderBy` and a roots-plus-children form; these are spec 0016's records, so edit 0016 in step |
| 0015 §5.2 (query) | Child-root queries for reports; traversal-rule interplay with column pickers; default ordering; `IncludeAncestors` semantics on non-tree stacks |
| 0015 §5.3 (details) | Standard history extra; child-row deep-link support; lazy extras |
| 0015 §5.4 (get-by-parent-ids) | Fix the roots-plus-expanded claim and the `OrderBy` wording |
| 0015 §7.3 (paths, codes) | How the SPA resolves `property` to a label and paths to controls; possibly a code for "required report parameter" |
| 0015 §8 (concurrency) | Pins the SPA's stamp handling (raw 7-digit string, "overwrite" re-send with `Override`); the details-page design must cite it |
| 0015 §11, §12 (actions, recipes) | Action metadata for the Actions menu; new capability rows if assignments or reports add a capability (e.g. "assignable") |
| 0015 §13.1–§13.3 (effects, hub, enlistment) | Assignment rows written from document actions; an `assignments.changed`-style hub event through `OnCommitted` |
| 0015 §14.1 (exceptions, cancellation) | Make cancellation explicit (token source, persist behaviour) to satisfy brief rule 2C; align with 0024's HTTP-layer error dialogs |
| 0015 §16.1 (telemetry) | Client correlation (trace ids, operation names) if 0026 wants server-side operation names surfaced |
| 0012 §11.2 (engine amendments) | Aggregate rollups or other Queryex extensions for 0033 would be added here or in 0010 (see contradiction 1) |

## 5. Contradictions and surprises

1. **Frozen-spec amendment precedent.** README: changes to 0001–0009 go into spec 0010. But 0012
   §11.2 already amends spec 0008 in place ("Spec 0008 is frozen; these additive changes are
   documented here"). A Queryex change for reports could land in either place; the architecture
   pass should pick one (and possibly move 0012 §11.2 into 0010).
2. **0012 and 0015 headers say "Ready for implementation. Frozen once merged"**, yet per the README
   they are round-1, unimplemented, and changeable. Not a real conflict, but easy to misread.
3. **"The SPA schema"** (0015 §1.2) is referenced as a `StackDescriptor` consumer but defined nowhere.
4. **`get-by-parent-ids`** (0015 §5.4): "renders the initial tree plus every expanded node in one
   call" is not expressible with `ParentIds` (null = roots only), and "unless the select carries an
   `OrderBy`" refers to a member `ParentIdsRequest` lacks.
5. **Excel export select (brief's search-page open question 1).** The brief says export "relies on
   the select parameter"; in 0015 `ExportSource.ByQuery` has no select, and the export columns are
   `ExcelQuery.SelectPaths` chosen by spec 0019 (0015 §4.2, §15). The question belongs to 0019.
6. **Save does not need a reload.** The brief says a successful save "simply reloads the entity";
   0015 returns the read-back inside the save transaction (`ReturnEntities = true` by default,
   0015 §6.9). A separate reload would waste a round trip and could show a later change.
7. **Error dialogs need more than 422/400/5xx.** The brief lists those three; 0015's closed set adds
   404, 403 (`forbidden`, `individual-required`, step-up), 409 (`concurrency-conflict` with
   "overwrite", `count-mismatch`), 413 `limit-exceeded`, 502 `partial-failure` with per-item
   results, 503s with `Retry-After` (`dependency-unavailable`, stale context, `tenant-schema-behind`
   with `Retry-After: 30` during a migration, 0012 §4.3) and 504 (spec 0016). 400 splits into
   `query-invalid` (Queryex diagnostics, for the advanced filter) and `bad-request`.
8. **Not every 422 error has a field.** Persist-time errors land at `ValidationPath.Root`:
   distribution guards (50600–50699), invariants (`Access.LastAdministrator`, `Blob.NotAttachable`),
   the unique race when ambiguous, and the SQL cycle fence (`Tree.Cycle` at Root, while the C#
   validator reports at `[i].ParentId`). The details page needs a non-field error area.
9. **Paths are index-based and JSON-cased.** Errors arrive as `entities[0].roleMemberships[2].roleId`
   (0015 §7.3 via 0016): the index is the position in the array the SPA sent, not a child id, and
   the root segment is the request's `entities` member. Reordering rows client-side between send and
   error display breaks the mapping.
10. **`ModifiedAt` must never pass through a JS `Date`.** Seven fractional digits; "the SPA keeps
    the raw string and parses a copy for display" (0015 §8). A default stamp on an update is
    `Concurrency.StampRequired`.
11. **`null` vs `[]` child collections** (0012 §2.3, 0015 §6.5): sending `[]` deletes every child;
    a details form that did not load a collection must send `null`. A child id not under that parent
    is `Entity.NotFound`, never a move.
12. **`IsActive` cannot be set by save or import.** A create always yields an active row; deactivate
    is a separate action under one `Activate` securable covering both directions (0012 §2.1, 0015
    §11.1, §12.2). A "create inactive" form option is impossible without a spec change.
13. **Details by id ignores the activatable conjunct** (shows inactive rows); `query`,
    `get-by-parent-ids` and Excel `ByQuery` apply it unless `IncludeInactive`; delete-by-query does
    not (it deletes inactive matches).
14. **Hidden equals missing.** Every invisible row reports as 404/`Entity.NotFound`; `get-by-ids`
    silently omits invisible ids (`EntitiesResult.Ids` = what came back) — details-page paging over
    search ids must tolerate gaps.
15. **The column tree is to-one only.** Queryex has no collections (0012 §2.3, §11.1), so the
    brief's "potentially infinite tree" of columns follows many-to-one navigations only, bounded by
    `PipelineLimits.MaxJoins = 64`; `MaxExpandDepth = 3` bounds only the details projection.
16. **Related entities in details are not row-filtered** (0015 §5.3, §9.4; review flag 13): a user
    sees the related projection of rows they cannot read directly; a picker search for the same
    entity would not show them.
17. **`DefaultSelect` is heavy for pickers.** The platform default includes `IsActive` and the four
    audit columns (0015 §2.2); a picker that passes no select pays for them.
18. **Count is capped, not exact.** `RowPage.Count = cap + 1` (10,001 by default) means "more than
    10,000"; `MaxSkipWindow = 100,000` forbids paging deeper than 100k rows; `Take` absent = 50.
19. **Search is server-side over declared columns only**, plus `Id = n` for an integer search; no
    picker-vs-page hint (0015 §5.2.1). The brief's per-entity "synonyms" for the global search are
    unrelated (launcher pages only).
20. **Wire ids are `long`** in request records (0015 §4.2); `bigint` stacks (`TopLevelEntity<long>`)
    beyond 2^53 would lose precision in JS numbers; spec 0016's JSON options decide the encoding.
21. **Non-member or deactivated caller is `404 tenant-not-found`** (0015 review flag 16), which the
    review flag itself says may need revisiting "if the SPA's session handling needs to distinguish
    the cases" — relevant to the tenant picker and the auth guard (spec 0024).
22. **Stale settings on reads are served** while fresh settings ride the same round trip (0015
    §5.1); on persists they fail the guard and re-run. Settings freshness for the warm-launch cache
    must come from tags (spec 0013), not from these responses.
