# Spec: Excel Codec — Export and Import

- **Author:** Ahmad Akra
- **Date:** 4 September 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Every entity stack the platform projects (spec 0014's `StackDescriptor`) has two spreadsheet
faces. The **display shape** is the grid as a sheet: the same columns, the same rows, the same
filter and ordering the user sees on the search page, without paging. The **editable shape** is
the entity as a rectangular table that round-trips: every editable property, every child
collection on its own sheet, every foreign key expressed as a natural key of the referenced entity
rather than a surrogate id, and a hidden manifest that records how the file was written so that
the file can be re-imported unchanged into the same tenant, another tenant of the same
distribution, or a distribution that shares the entity schema. This spec ships the codec that
writes both shapes and reads the editable one back, the four operations that expose them on every
eligible stack, the natural-key conventions references depend on, and the hand-off of large files
to background work.

The codec is a pure component: it plans workbooks from `EntityMetadata` (spec 0011's
`EntityMetadata`), encodes cells by property type, decodes cells by the mapped property's type,
and never opens a database connection. Rows reach it through spec 0014's `IExcelRowSource` and
leave it as entity instances handed to spec 0014's `SaveAsync` with `SaveSource = Import`; row
security, validation, concurrency, tree maintenance, and version-tag bumps are the pipeline's, not
the codec's. The codec adds what a spreadsheet user needs and the pipeline cannot know: sheet, row
and column coordinates on every error, language-aware column mapping, bulk translation of natural
keys into surrogate ids under the caller's read filter, and in-sheet resolution of parents that are
new in the same file.

The workbook is written and read with the Open XML SDK in forward-only mode, so memory stays flat
with row count in both directions. Values are encoded by type with text fallbacks where Excel's
fifteen significant digits would corrupt them (`long` keys, wide decimals, `Guid`s); dates are
serials with a per-column number format that carries the request culture and calendar, and text
for calendars Excel does not have (Ethiopian); import never reads a cell's format — the mapped
property decides. These rules are what make a file exported under an Arabic Um Al Qura request
import correctly under an English Gregorian one.

Files enter and leave through spec 0016's blob storage: an import file is a staged upload named by
its `FileId`, a background export's artifact is a blob owned by a `core.Exports` row. Background
work rides spec 0019's `core.Jobs` through two handlers, `core.export` and `core.import`; a large
import commits in chunks and checkpoints the chunk index inside each chunk's transaction, so a
re-leased job resumes after the last committed chunk and never re-applies one. Notifications
(`core.export.ready`, `core.import.completed`, `core.import.failed`) are spec 0020's.

Deliberately left to later specs: multi-entity workbooks, CSV, reference dropdowns backed by lookup
sheets, an errors workbook echoing the upload with an error column, partial success inside a
synchronous import, composite natural keys, per-row child action columns, and the MCP tools
`tellma_export`/`tellma_import` (reserved by spec 0015).

## Goals / Non-goals

**Goals**

- Ship the codec (`Tellma.Core.Excel`): the workbook writer and reader on Open XML SDK 3.5.1, the
  per-type encoding and decoding rules, the number-format construction, the manifest sheet, the
  planner for both shapes, the mapper, and the resolver.
- Ship the four operations `export`, `export-for-import`, `inspect-import`, `import`, contributed as
  `[ApiAction]`s on every stack with `Export` (the display export) or `Import` (the other three) in
  its operation set, with `export/start`, `export-for-import/start` and `import/start` as the
  background paths of the two exports and the import — zero lines in a distribution.
- Ship the import semantics: modes `Insert | Update | Upsert`, row identity by natural key or
  same-source `Id`, hydration of partial sheets, blank-cell and child-sheet rules, bulk
  natural-to-surrogate resolution under the target's read filter, tree import with in-sheet
  parents and cycle detection, coordinates on every error, all-or-nothing synchronous imports.
- Ship the `Export` and `Import` entities over `core.Exports`/`core.Imports`, the `core.export`
  and `core.import` job handlers, chunked and resumable background import, and the synchronous
  thresholds that decide between the two paths.
- Ship `ExcelOptions` (`Tellma:Excel`), `ExcelErrorCodes`, `ExcelTelemetryNames`, and the test
  corpus that pins every encoding, mapping and resolution rule.

**Non-goals (explicitly out of scope)**

- **The row source and the save** — spec 0014 implements `IExcelRowSource` over `IDataBatch.Rows`
  with the caller's read filter and owns `SaveAsync`, validation, concurrency, and the round-trip
  budget.
- **Natural-key metadata** — `[NaturalKey(Order?)]`, its unique-index validation and the inference
  order are spec 0011's `EntityMetadata.NaturalKeys`; this spec consumes them (§6).
- **Labels, calendars, negotiation** — spec 0012's `ILabelProvider` and `ICalendarSystem`.
- **Blob staging, the upload and download endpoints, sweep and reconcile** — spec 0016.
- **Job leasing, the worker, schedules, `IJobProgress`, retention handlers** (`core.file-retention`
  included) — spec 0019.
- **Notification types and the inbox** — spec 0020.
- **Endpoint projection, streaming, problem details, limits** — spec 0015.
- **Multi-entity workbooks, CSV, reference dropdowns, an errors workbook, partial success in a
  synchronous import, composite natural keys, per-row child `Action` columns, the MCP tools** —
  a later spec; each has a named seam here (manifest version, `ImportOutcome.CommittedRowRanges`,
  the coordinate map, `[NaturalKey(Order)]`).

## 1. Placement and architecture

### 1.1 Projects, packages, namespaces

| Piece | Location | Notes |
|---|---|---|
| Contracts | `src/core/Tellma.Core.Abstractions/`, namespace `Tellma.Core.Abstractions.Excel` | Requests, plans, outcomes, `ImportError`, `ImportMode`, `ExportKind`, `ExcelQuery`, `IExcelRowSource`, `Export`/`Import` entities, `ExcelOptions`, `ExcelErrorCodes`, `ExcelTelemetryNames`, `ImportException`; `[ExcludeFromExcel]` lives in `Tellma.Core.Abstractions.Entities` (spec 0011). |
| Codec and operations | `src/core/Tellma.Core/`, namespace `Tellma.Core.Excel` (folder `Excel/`) | `ExcelOperations<TEntity>`, `IExcelExporter`/`IExcelImporter` and their implementations, `ExportService`, `ImportService`, `ExportAccessCriteria`, `ImportAccessCriteria`, the two job handlers. Internal: `WorkbookWriter`, `WorkbookReader`, `SharedStringStore`, `NumberFormatBuilder`, `CellCodec`, `ExportPlanner`, `ImportMapper`, `ImportResolver`, `ImportCheckpointEffect<TEntity>`. |
| Package pin | `Directory.Packages.props` | `DocumentFormat.OpenXml` 3.5.1 (MIT), referenced by `Tellma.Core` only. |
| Unit tests | `test/core/Tellma.Core.Tests/Excel/` | Pure: no database, no network; the workbook corpus under `Excel/Corpus/`. |
| Integration tests | `test/core/Tellma.Core.IntegrationTests/Excel/` | spec 0011's fixture entities (`test/shared/Tellma.Testing.Entities`); `Category=Integration`. |

Dependency edges: `Tellma.Core.Abstractions` references `Tellma.Core.Queryex` and nothing else;
`Tellma.Core` references `Tellma.Core.Abstractions`, EF Core, SqlClient, `DocumentFormat.OpenXml`
and the other runtime pins named by spec 0010. The codec depends on the pipeline (it calls
`SaveAsync`, `GetByIdsAsync`, `ExcelRowSource`, `IAccessEvaluator`, `IBlobService`, `IJobQueue`, and
enlists the `Export`/`Import` rows it writes with the `IOpenWriteHost` of the frame it runs in,
which persists them, spec 0014 §13.3); the pipeline never depends on the codec — it knows only
`IExcelRowSource` and the stack-companion contribution. No module and no distribution references
Open XML.

**Contract blocks are C# sketches: names and shapes are normative; `using` directives, XML
documentation, cancellation-token parameters, method bodies and accessibility details are omitted,
so a block is never pasted into code.** SQL statements are the exact shape to emit.

### 1.2 Composition

The Excel feature is part of `CoreFeature` (spec 0010), added unconditionally by `AddTellma`. Its
contribution items, realised by `Tellma.Core`:

- For every stack whose `Operations` include `Export` or `Import`: `ExcelOperations<TEntity>` as a
  stack companion (spec 0014's `StackCompanionContributionItem`), attached through
  `contribution.EntityCompanion<TEntity, ExcelOperations<TEntity>>()`; the realizer closes it over
  the leaf and projects its `[ApiAction]`s under the stack's segment and resource — `export`
  (`Action = "Read"`, `Mutation = false`, `Idempotent = true`) and `export/start`
  (`Action = "Read"`, `Mutation = true`, `Idempotent = false`) on stacks with `Export`, and
  `export-for-import` and `export-for-import/start` (declared as those two), `inspect-import`
  (`Action = "Save"`, `Mutation = false`, `Idempotent = true`), `import` (`Action = "Save"`) and
  `import/start` (`Action = "Save"`, `Mutation = true`, `Idempotent = false`) on stacks with
  `Import`.
- For every stack whose `Operations` include `Import`: `ImportCheckpointEffect<TEntity>` as an
  `IPersistEffect<TEntity>` (§12.5).
- `contribution.Entity<Export, ExportService>()` and `contribution.Entity<Import, ImportService>()`
  with `[Stack(Operations = Query | Details | Delete | Enlist)]` on both entities (§12.1).
- `contribution.Singleton<IAccessCriteriaProvider, ExportAccessCriteria>()` and
  `contribution.Singleton<IAccessCriteriaProvider, ImportAccessCriteria>()` — the self-scope
  criteria of §12.1.
- `contribution.JobHandler<ExportJobHandler>()` and `contribution.JobHandler<ImportJobHandler>()`
  (§12.3, §12.4).
- `contribution.NotificationType(...)` for `core.export.ready`, `core.import.completed`,
  `core.import.failed` — the descriptors are spec 0020's; the Excel feature registers them.
- `ExcelOptions` bound from `Tellma:Excel`; `IExcelExporter`, `IExcelImporter` as singletons;
  `ExcelOperations<TEntity>` scoped.

The stack feature's securable contributor registers each `[ApiAction]` pair
(`<Resource>`, `Read` / `Save`, `FilterRoot = Resource`); the pairs already exist for every stack,
so Excel adds **no securable** (no `Export` action; review flag 10). The blob kinds `export-file`,
`import-file`, `import-result` are spec 0016's; the codec names them.

Startup checks reported into the realised gate (spec 0010): a warning for every
`[ExcludeFromExcel]` on a server-owned property (redundant); the scratch directory exists and is
writable. Natural-key checks are spec 0011 §3.2's startup validation (§6.3).

### 1.3 Vocabulary

"Display shape" and "editable shape" (`ExportKind = Display | ForImport`); `Upsert`, never
"merge"; **row key** for the column that identifies a row in `Update`/`Upsert`; **reference key**
for the natural key a foreign-key column is expressed in; **manifest** for the hidden `_tellma`
sheet; **stamp** for the concurrency-token column, whose header is `Stamp` and whose value is
`ModifiedAt`'s wire string exactly as spec 0015 encodes `DateTimeOffset` (ISO 8601,
`yyyy-MM-ddTHH:mm:ss.fffffffzzz`: seven fractional digits and the offset, `+00:00` for a stamp);
**same-source** for a file whose manifest names the running distribution and the target tenant
(§5.4); **lookup** for one resolution query per (target entity, key path); **chunk** for the
unit of commit in a background import.

### 1.4 The two paths and their round trips

Every operation runs on the caller's behalf inside a tenant scope; the connect prologue rides the
first business round trip (spec 0013's guarded prologue), so no operation pays a connect call of
its own. Round trips per operation, warm caches:

| Operation | Round trips | What each carries |
|---|---|---|
| `export`, display shape, by query or by ids | `⌈rows / MaxTake⌉` reads (1 for `≤ MaxTake` rows) | the grid query, pages of `StackLimits.MaxTake` through spec 0014's row source; ids as a `KeySetRestriction` |
| `export-for-import`, by ids | parent pages + one query per child collection per page of parent ids | parent flat query, then each child collection restricted by the page's parent ids |
| `export-for-import`, by query | the same | parent pages first (`Take = cap + 1`), then children by the collected parent ids |
| `inspect-import` | 1 | the staged blob's `IBlobService.ResolveAsync` read (spec 0016's `Read` batch), the prologue riding it; the securable check and the plan computed from the file cost no further round trip |
| `import`, `Insert`, no reference columns | `1 + 2` | the staged blob's `ResolveAsync` read (as for `inspect-import`); RT1 validation context (ids assigned, before images none); persist |
| `import`, any mode with references, or `Update`/`Upsert` | `1 + L + H + 2` | the blob read, then `L` lookup reads (one per distinct lookup, typically 1–3), `H = 1` hydration through `GetByIdsAsync` (0 for `Insert`), then validation and persist |
| `export/start`, `export-for-import/start`, `import/start` | 1 (export), 2 (import) | the enlisted insert of the `Export`/`Import` row and the job enqueue in one persist batch (§12.2); the import pays a validation round first, in which spec 0016's validator loads the staged upload's blob row |
| background export (`core.export`) | `⌈rows / MaxTake⌉ + 1` | the pages as above in the job scope; one validation round for the enlisted update of the `Export` row (§12.3), whose persist rides the partition's completion batch, atomic with the job outcome |
| background import (`core.import`), `N` chunks | `1 + N × (L + H + 2) + 1` | the blob read once in the job scope, then the same per chunk, the checkpoint riding the chunk's persist transaction; one validation round for the enlisted update of the `Import` row (§12.6), whose persist rides the completion batch |

No lock or connection is held across file I/O: an export drains each query into a temp file and
releases the connection before the first byte reaches the response or the blob store; an import
parses the whole file before its first database call, and the persist transaction opens inside
the pipeline after every file read has completed.

## 2. The four operations

### 2.1 Contract

```csharp
// Tellma.Core.Abstractions.Excel
public enum ImportMode { Insert, Update, Upsert }
public enum ExportKind { Display, ForImport }

public class ExportSourceRequest                            // either Ids or the clauses, never both
{
    public IReadOnlyList<long>? Ids { get; set; }
    public string? Filter { get; set; }
    public string? OrderBy { get; set; }
    public string? Search { get; set; }
    public IReadOnlyDictionary<string, JsonElement>? Arguments { get; set; }
}

public abstract record ExportSource                         // the service seam's source; one case per request
{
    public sealed record ByIds(IReadOnlyList<long> Ids, string? OrderBy) : ExportSource;   // rows in OrderBy order, else by Id
    public sealed record ByQuery(
        string? Filter, string? Search, string? OrderBy, IReadOnlyDictionary<string, JsonElement>? Arguments)
        : ExportSource;
    public sealed record All : ExportSource;                                                // no clauses: the access filter alone (the importer's lookups, §10.1)
}

public sealed class ExportRequest : ExportSourceRequest     // display shape
{
    public string Select { get; set; }                      // required; exactly as the grid uses it
    public IReadOnlyList<string>? Headers { get; set; }     // parallel to the select items; derived when null
}

public sealed class ExportForImportRequest : ExportSourceRequest   // editable shape
{
    public IReadOnlyList<string>? Columns { get; set; }   // subset of editable property paths; all when null
    public bool IncludeChildren { get; set; } = true;
    // FK property -> target property (CenterId -> Code)
    public IReadOnlyDictionary<string, string> ReferenceKeys { get; set; }
}

public sealed record ExportOutcome(Stream Workbook, string FileName, int Rows);

// Column = header text or letter; Path null = ignore
public sealed record ImportColumnMapping(string Sheet, string Column, string? Path);
// "" = parent sheet; null = ignore
public sealed record ImportSheetMapping(string Sheet, string? Collection);

public sealed class InspectImportRequest
{
    public int FileId { get; set; }             // required; a Staged blob of kind import-file
    public IReadOnlyList<ImportSheetMapping> SheetMappings { get; set; } = [];
    public IReadOnlyList<ImportColumnMapping> ColumnMappings { get; set; } = [];
}

public sealed class ImportRequest
{
    public int FileId { get; set; }             // required
    public ImportMode Mode { get; set; } = ImportMode.Insert;
    // natural-key path or "Id"; required for Update/Upsert unless the manifest supplies one
    public string? RowKey { get; set; }
    public IReadOnlyList<ImportSheetMapping> SheetMappings { get; set; } = [];
    public IReadOnlyList<ImportColumnMapping> ColumnMappings { get; set; } = [];
    public bool IgnoreUnmappedColumns { get; set; } = false;
    public ConcurrencyMode Concurrency { get; set; } = ConcurrencyMode.Check;
    public bool Atomic { get; set; } = false;   // import/start only: one transaction for the whole file
}

public sealed record ImportPlan(
    string? Entity, int ManifestVersion, bool ManifestPresent, bool SameSource, bool SchemaFingerprintMatches,
    IReadOnlyList<ImportPlanSheet> Sheets, IReadOnlyList<string> RowKeyCandidates, string? DefaultRowKey,
    long TotalRows, bool RequiresBackground, IReadOnlyList<ImportError> Warnings);

public sealed record ImportPlanSheet(
    string Sheet, string? Collection, ImportSheetStatus Status, long Rows,
    IReadOnlyList<ImportPlanColumn> Columns);

public sealed record ImportPlanColumn(
    string Column, string Header, string? Path, ImportColumnStatus Status, string? Language,
    string? ReferenceKey, IReadOnlyList<string> Suggestions);

public enum ImportSheetStatus { Parent, Child, Manifest, Ignored }
public enum ImportColumnStatus { Mapped, Unmapped, Ignored, Duplicate, ServerOwned }

// CodedError: spec 0014 §7.3
public sealed record ImportError(
    string? Sheet, int? Row, string? Column, string? Header, string? Property,
    string Code, IReadOnlyDictionary<string, object?> Arguments)
    : CodedError(Code, Arguments);

public sealed record ImportOutcome(
    int Inserted, int Updated, int ChildrenInserted, int ChildrenUpdated, int ChildrenDeleted,
    IReadOnlyList<(int From, int To)> CommittedRowRanges, IReadOnlyList<ImportError> Warnings);

// 422 validation with coordinates; a member of spec 0014 §14.1's closed set
public sealed class ImportException(IReadOnlyList<ImportError> Errors, int TotalErrors) : TellmaException;

// Tellma.Core.Excel (runtime)
// stack companion (spec 0014), closed over the leaf
public sealed class ExcelOperations<TEntity> where TEntity : class
{
    // [ApiAction] "export", Action = "Read", Mutation = false, Idempotent = true
    public Task<ExportOutcome> ExportAsync(ExportRequest request);
    // [ApiAction] "export/start", Action = "Read", Mutation = true, Idempotent = false
    public Task<JobAccepted> StartExportAsync(ExportRequest request);
    // [ApiAction] "export-for-import", Action = "Read", Mutation = false, Idempotent = true
    public Task<ExportOutcome> ExportForImportAsync(ExportForImportRequest request);
    // [ApiAction] "export-for-import/start", Action = "Read", Mutation = true, Idempotent = false
    public Task<JobAccepted> StartExportForImportAsync(ExportForImportRequest request);
    // [ApiAction] "inspect-import", Action = "Save", Mutation = false, Idempotent = true
    public Task<ImportPlan> InspectImportAsync(InspectImportRequest request);
    // [ApiAction] "import", Action = "Save"
    public Task<ImportOutcome> ImportAsync(ImportRequest request);
    // [ApiAction] "import/start", Action = "Save", Mutation = true, Idempotent = false
    public Task<JobAccepted> StartImportAsync(ImportRequest request);
}
```

| Member | Meaning |
|---|---|
| `ExportSourceRequest.Ids` | An `export` or `export-for-import` request carries `Ids` or the clauses (`Filter`, `OrderBy`, `Search`, `Arguments`), and `ExcelOperations` maps it to exactly one `ExportSource` case: `Ids` to `ByIds` with `OrderBy` beside it, the clauses to `ByQuery`; `Ids` together with `Filter`, `Search` or `Arguments`, or neither `Ids` nor a clause, is `BadRequestException`. With `Ids`: at most `StackLimits.MaxIds`; an empty list is valid for `ExportForImport` and yields the entity's template (headers, validation lists, manifest, no rows) and `BadRequestException` for `Export`. The clauses are the wire `QueryRequest`'s, interpreted by spec 0014's row source exactly as `query` interprets them (`Search` through `SearchFilter`, the access filter conjoined, no activatable conjunct). |
| `ExportSource` | The source `EntityService.ExcelRowSource` takes (spec 0014 §15 composes `ByIds` as a `KeySetRestriction`, `ByQuery` as the clauses, `All` as the access filter alone). `ByIds` and `ByQuery` come from a request; `All` never does: it is the importer's lookup source (§10.1). |
| `ExportRequest.Select` | The grid's select, verbatim; every item becomes one column. `Headers[i]` (each ≤ 255 characters) names column `i`; a null list derives headers (§4.1). |
| `ExportForImportRequest.Columns` | Property paths of the parent entity restricted to a subset; `Id`, `Stamp` and the default natural key column are always kept; a path that is not editable is `BadRequestException`. |
| `ExportForImportRequest.ReferenceKeys` | Per foreign-key property, the target property to express it in; overrides the target's default natural key; any scalar property of the target is allowed (a non-unique one makes import ambiguity possible, §10.3). |
| `ExportOutcome` | The synchronous result: `Workbook` is a readable, seekable stream over the spooled temp file (deleted on dispose), `FileName` per §4.3, `Rows` the data rows written. |
| `StartExportAsync`, `StartExportForImportAsync`, `StartImportAsync` | The background path of the two exports and the import, taking the same request record as the synchronous method: the `Export`/`Import` row is inserted and its job enqueued (§12.2), and the result is spec 0015's `JobAccepted(JobId, ExportId)` or `JobAccepted(JobId, ImportId)`, which the web layer answers with 202. |
| `ImportRequest.RowKey` | A property path of the parent entity that is in `EntityMetadata.NaturalKeys`, or `"Id"`; `"Id"` only for same-source files (§9.2). |
| `ImportRequest.Concurrency` | `Check` (default) compares each hydrated row's expected stamp inside the transaction; `Override` disables the comparison, never the existence check. |
| `ImportRequest.Atomic` | `import/start` only: the whole file in one transaction, under the ceilings of §2.3 (§12.4). `Atomic = true` on `import` is `BadRequestException`: a synchronous import is one save and atomic already. |
| `ImportPlan` | The mapping result before any row is decoded beyond the header row and the `dimension` element; `RequiresBackground` is true when the file is above any synchronous limit of §2.3, so only `import/start` can run it. |
| `ImportOutcome` | The result of `import` and the content of a completed background import's `ResultFileId` blob (§12.6): the counts; `CommittedRowRanges` lists parent-sheet row ranges (1-based Excel rows, header excluded) that committed — one range for a synchronous import, one per committed chunk for a background one; `Warnings` are the mapping warnings (`Excel.Import.UnconfiguredLanguageColumn`) and never errors. |
| `ImportException` | Thrown by `import` for any error in a synchronous import; `Errors` is capped at `ExcelOptions.MaxReportedErrors` and `TotalErrors` says how many exist; spec 0015 §7.1 maps it to 422 with `sheet`, `row`, `column`, `header` and `property` on each item and `totalErrors` beside them. |

### 2.2 Projection and securables

Spec 0014's realizer projects the companion's seven methods as `[ApiAction]`s on every eligible
stack (§1.2); spec 0015 projects them to `POST /{tenantId}/api/web/{resource-segment}/{method}`,
where `{method}` is `export`, `export/start`, `export-for-import`, `export-for-import/start`,
`inspect-import`, `import` or `import/start`. The synchronous exports, `inspect-import` and
`import` run under the long request timeout and the per-user export/import concurrency limits of
`TellmaApiOptions`; the three `/start` methods only insert a row and enqueue its job (§12.2) and
run under the ordinary web timeout. Each method exists when its operation is declared — `export`
and `export/start` under `Export`, the other five under `Import` — and the securable of each
(`Read` for the four export methods, `Save` for the three import methods) is evaluated by
spec 0014's `IApiActionInvoker` before the method runs, so the methods hold no securable check of
their own. The read decision's filter is applied by the row source; the save decision is enforced
by the pipeline's two-stage pre-check and post-check on the rows the import hands it (§13).

### 2.3 Synchronous or background

| Operation | Synchronous when | Otherwise |
|---|---|---|
| `Export` | the query returns `≤ MaxSynchronousExportRows` rows | above the cap: `LimitExceededException("Excel.Export.RowLimitExceeded", rows, cap)` (413) — the export **fails**, never truncates; the message names `export/start`, which runs the same request in the background |
| `ExportForImport` | the same, counting parent rows; each sheet is additionally capped by `MaxExportRows` | the same; the message names `export-for-import/start` |
| `export/start`, `export-for-import/start` | never | the job fails a sheet above `MaxExportRows` (§4.2, §12.3) |
| `Import` | `TotalRows ≤ MaxSynchronousImportRows` (summed over every mapped sheet), the file fits one save (parent rows `≤` the stack's `MaxSaveCount`, `TotalRows ≤ MaxRowsPerSave`) and the file `≤ MaxSynchronousImportFileBytes` | above any limit: `ImportException` with one error `Excel.Import.TooLargeForSynchronous` (arguments `rows`, `maxRows` — the row limit crossed — `bytes`, `maxBytes`), whose message names `import/start`, which runs the same request in the background |
| `import/start` | never | `Atomic = true` above `MaxAtomicImportRows`, or above the stack's `MaxSaveCount` parents or `MaxRowsPerSave` (§12.4), is `Excel.Import.TooLargeForSynchronous` with `maxRows` = the ceiling exceeded |

A `/start` request inserts an `Export` or `Import` row (§12.1) and enqueues the job in the same
persist batch (§12.2); the result is `JobAccepted`, carrying the job id and the row id (§2.1). A
synchronous export or import never promotes itself to background: the response shape would change
mid-request. In either mode a parent with more child rows than its collection's `MaxCount` is
`Excel.Import.TooManyChildren` as the child sheet is read (§8.1); no background chunk can hold it.

## 3. Cells: encoding, decoding, and localization

### 3.1 Encoding by type, decoding by mapped type

The codec encodes by the **property or column type**, never by value, so a column is homogeneous
and sortable; the importer decodes by the **mapped property's type**, never by the cell's format.

| Type | Display export | Editable export | Import accepts |
|---|---|---|---|
| `bool` | boolean cell | boolean cell + validation list `TRUE,FALSE` | boolean; text `true/false/yes/no/1/0` (ordinal-ignore-case); the localized boolean labels of the tenant languages and English |
| `byte`, `short`, `int` | number, format `#,##0` | number, format `0` | integral number; integral text parsed invariant |
| `long` | number (values above 15 significant digits lose precision; the display shape is not for round trips) | **text cell**, column format `@` | number (integral) or text |
| `decimal(p,s)` | number, format from scale; `General` when the column is a computed expression | number; **text** when the value needs more than 15 significant digits; column format from scale | number; text parsed invariant (`NumberStyles.Number` without thousands separators); scale beyond `s` is `Excel.Import.InvalidCell` |
| `double`, `float` | number, `General` | number, `General` | number or invariant text |
| `string` | inline string, column format `@` | inline string, `@`, `dataValidation textLength ≤ MaxLength` when `PropertyMetadata.MaxLength` is declared | any cell; numbers rendered invariant (`R` for doubles, plain for integers); trimmed unless `[PreserveWhitespace]`; empty after trim is null; length checked by the pipeline |
| enum (`EnumValues` non-null) | `ILabelProvider.EnumValueLabel` in the request culture | stored value + validation list of stored values when the comma-joined list is ≤ 255 characters | stored value (ordinal-ignore-case), or any label in a tenant language or English |
| `DateOnly` | serial + date format (§3.2); text for a calendar without a Windows calendar id | the same; the manifest records `DateEncoding` and `DatePattern` per column | integral serial (`date1904`-aware; `< 61` refused as suspect: `Excel.Import.InvalidCell`); ISO 8601 text; text in the column's manifest calendar and pattern; else the request calendar's short pattern through `ICalendarSystem.TryParse` |
| `DateTime` | serial with fraction + date-time format | the same | as above plus a time part; a serial's fraction is the time |
| `DateTimeOffset` | converted to the request display zone, serial + date-time format | **text**, ISO 8601 with offset, `@` | ISO 8601 text; a serial is interpreted in the request display zone |
| `TimeOnly` | fraction + `hh:mm:ss` | the same | fraction, or `HH:mm[:ss]` text |
| `Guid` | text | text, `@` | text (`D` format or braces) |
| `hierarchyid` | its string path, text | **excluded** | — |
| `byte[]`, `[JsonColumn]`, `SubtreeCount`/`ActiveSubtreeCount`, computed expressions | display only when selected: JSON as text, `byte[]` blank | **excluded** | — |
| audit columns, `JobId`, `[BlobReference]` columns, `[ExcludeFromExcel]`, `[Derived]` and database-computed properties | display only when selected, by their type (`[BlobReference]` as the blob id number) | **excluded** | `Excel.Import.ServerOwnedColumn` when mapped |
| null | empty cell | empty cell | blank ⇒ null (§9.4) |

A display-shape decimal column takes its scale from the property behind a bare path
(`PropertyMetadata.Scale`); a computed numeric column (`QueryexColumn.Path` null) uses `General`.
The display shape reads each `QueryexColumn.Type` tolerantly within spec 0008 §13.4's rule that
result columns carry the Queryex type, not the SQL type: a `Numeric` column whose reader value is
`int` is written as an integer.

Decoding by cell kind: `t="s"` resolves through the shared-string store; `inlineStr` and rich-text
runs are concatenated; `t="str"` and formula cells use the cached `<v>`, and a formula without a
cached value is blank; `t="b"`, `t="n"`, `t="d"` as their kinds; `t="e"` (`#N/A` and the other
Excel errors) is `Excel.Import.InvalidCell`. A non-integral serial in a `DateOnly` column is
`InvalidCell`. Every decode failure is `Excel.Import.InvalidCell` at the cell with arguments
`expected` (the type name) and `value` (the cell text, truncated to 100 characters).

Cells beginning with `=`, `+`, `-`, `@` need no escaping: inline string cells are never formulas
in SpreadsheetML (formulas are `<f>` elements), so the CSV-injection class does not exist for
`.xlsx`. The rule must be revisited if CSV is ever added.

### 3.2 Number formats and calendars

Number formats are built from the request context (culture, calendar) at export time and stored as
one `numFmt` per distinct string with one `cellXfs` entry each:

- **Numbers.** `#,##0` for integers in the display shape, `0` in the editable shape; `#,##0.`
  followed by `0` × scale for decimals (`0.` + `0` × scale in the editable shape). Separators in
  format codes are locale-independent placeholders; Excel renders them per the viewer's locale.
- **Dates.** The request culture's `ShortDatePattern` translated token by token (`d`→`d`,
  `dd`→`dd`, `M`→`m`, `MM`→`mm`, `MMM`→`mmm`, `MMMM`→`mmmm`, `yy`→`yy`, `yyyy`→`yyyy`, literal
  separators quoted); times `HH:mm:ss`→`hh:mm:ss`, `h:mm tt`→`h:mm AM/PM`. Prefixed with the
  locale tag `[$-CCLLLL]`: `LLLL` is the culture's LCID in hexadecimal (omitted for custom cultures
  reporting `0x1000`), `CC` the Windows calendar code — none for Gregorian (`gc`), `17` for Um Al
  Qura (`uq`). Example: `[$-170401]dd/mm/yyyy` for an Arabic Um Al Qura request. Serials are
  Gregorian day numbers from the 1899-12-30 epoch regardless of calendar; the tag only changes
  how Excel renders them.
- **Ethiopian (`et`) and any calendar without a Windows calendar id.** Date cells are written as
  **text** rendered by `ICalendarSystem.Format(date, culture, DateStyle.Short)`; the manifest
  records `DateEncoding = Text`, the calendar code and the pattern for the column, and the importer
  parses that column with `ICalendarSystem.TryParse` under the same culture. The round trip is exact
  because export and import share the pattern.
- **Date system.** Export writes `workbookPr date1904="0"`; import reads `workbookPr/@date1904`
  and shifts serials by 1,462 days when set. Serial 60 (the 1900 leap-year phantom) is refused on
  import as part of the `< 61` rule.

Import never reads a cell's number format. The manifest's calendar and pattern influence only the
parsing of **text** date cells; a serial is a serial in every calendar.

### 3.3 Sheet names, headers, labels

Sheet names are scrubbed of `/ \ ? * : [ ]`, trimmed to 31 characters, never `History` (reserved
by Excel), and made unique with ` (2)`, ` (3)` suffixes. Headers come from `ILabelProvider` in the
request culture: `EntityLabel(entityType, plural: true)` (spec 0012's `<Schema>_<Entity>_Plural`
key, `Gl_Center_Plural`, which falls back to the singular label) for sheet names, `PropertyLabel`
for columns (twins carry ` (E)` / ` (ع)`), `PropertyLabel` of the navigation and of the key joined
by ` / ` for reference columns (`Center / Code`, `Parent / Code`, `Role / Name (E)`). Duplicate
headers within a sheet get the technical path in parentheses (`Name (Name2)`). Labels are cached
per (entity, culture, settings tag); the connect prologue reads the settings tag before the
operation runs, so labels are never older than the request.

The header row is bold, frozen (`pane ySplit="1"`), and filterable (`autoFilter` over the used
range); column widths derive from the header length and the type (dates 12, numbers 14, text
`min(60, max(12, MaxLength / 2))`); a right-to-left sheet view (`sheetView rightToLeft="1"`) when
the request culture is right-to-left.

## 4. Display-shape export

### 4.1 The plan

`Export` runs exactly the query `query` would run for the same request — the same `Select`,
`Filter`, `OrderBy`, `Search`, `Arguments`, the same access composition, `Skip = 0`, no count, no
ancestors — through `EntityService.ExcelRowSource(source)`, `source` the request's `ExportSource`
case (§2.1), with an `ExcelQuery` of `RootEntity = Descriptor.Resource`, the select items as
`SelectPaths`, `OrderBy = request.OrderBy`, no `Restriction` and a `Take` of
`MaxSynchronousExportRows + 1` (background: `MaxExportRows + 1`), and writes one data sheet plus the
manifest sheet (`Shape = Display`):

- Sheet name: the entity's plural label (§3.3).
- Header row: `request.Headers[i]` when supplied, else derived — the localized property-label chain
  for a column with a `Path` (`Center / Code`, `Name (E)`), the select item's source text for a
  computed expression.
- One column per `QueryexColumn` in select order, encoded from its `QueryexType` (§3.1) with the
  number format of §3.2.

```csharp
// Tellma.Core.Abstractions.Excel
public sealed record ExcelQuery(
    string RootEntity, IReadOnlyList<string> SelectPaths, string? OrderBy, KeySetRestriction? Restriction,
    IReadOnlyList<object>? RestrictionValues, int? Take);

// implemented by spec 0014 over IDataBatch.Rows with the caller's read filter
public interface IExcelRowSource
{
    IAsyncEnumerable<IReadOnlyList<object?>> Rows(ExcelQuery query);
}

// Tellma.Core.Excel (runtime; the codec is pure — it never opens a connection)
public sealed record ExcelContext(
    int TenantId, string DistributionSlug, IReadOnlyList<string> TenantLanguages, CultureInfo Culture,
    ICalendarSystem Calendar, TimeZoneInfo TimeZone, ILabelProvider Labels);

public sealed record ExcelColumnSpec(
    string Header, QueryexType Type, int? Scale, IReadOnlyList<string>? Path);

public sealed record ExcelWorkbookPlan(
    EntityMetadata Entity, ExportKind Shape, IReadOnlyList<ExcelSheetPlan> Sheets,
    IReadOnlyDictionary<string, string> Manifest);

public sealed record ExcelSheetPlan(
    string Name, string? Collection, IReadOnlyList<ExcelColumnSpec> Columns,
    IReadOnlyList<string?> NumberFormats, ExcelQuery Query);

public interface IExcelExporter
{
    ExcelWorkbookPlan PlanDisplay(
        EntityMetadata entity, IReadOnlyList<ExcelColumnSpec> columns, ExcelContext context);
    ExcelWorkbookPlan PlanEditable(
        EntityMetadata entity, ExportForImportRequest request, ExcelContext context);
    Task<int> WriteAsync(ExcelWorkbookPlan plan, IExcelRowSource rows, Stream destination);
}
```

| Member | Meaning |
|---|---|
| `ExcelQuery` | Flat rows over one root; `RootEntity` is an entity name (spec 0011's `EntityMetadata.Name`, `gl.Center`) — the stack's own, a child collection's, or a lookup target's; `Restriction` names a path and a table source the row source binds from `RestrictionValues` (`IdList`/`BigIdList`/`GuidList`/`StringList`/`DateList` by the path's key type); the row source conjoins the caller's read filter on the root's resource. |
| `ExcelContext` | Built by `ExcelOperations` from `RequestContext`: `TenantId`, `DistributionSlug = DeploymentIdentity.Application` (spec 0007), `TenantLanguages` from `TenantSettings.Languages` in slot order, `Culture = CultureInfo`, `Calendar = CalendarSystem`, `TimeZone` from the display zone, `Labels = ILabelProvider`. |
| `ExcelColumnSpec` | One display column: the header, the Queryex result type, the scale for a bare-path decimal, and the path (null for an expression). `ExcelOperations` builds the list from `QueryexEngine.Validate` over the request's select (spec 0008 §2) so the plan exists before the first row. |
| `ExcelWorkbookPlan` | Immutable; `Write` streams from it. `Manifest` is the key/value header block of §5.3. |
| `Write` | Writes every sheet in plan order, pulling each sheet's `Query` from `rows`; returns the data rows written; throws `LimitExceededException("Excel.Export.RowLimitExceeded", rows, cap)` when a sheet's query yields more than its cap (the plan's `Take` is `cap + 1`, so the `cap + 1`-th row is the signal). |

### 4.2 Spooling and caps

Rows drain from the row source into a temp file under `Tellma:ScratchPath` (spec 0010; default the
operating system's temp directory) opened with delete-on-close; the writer emits the sheet part
forward-only as rows arrive, so peak memory is one row plus the shared-string-free package
buffers. The connection is released when the last page completes (spec 0014's row source pages
through `MaxTake`, one read round trip per page). The file is then copied to the HTTP response
(synchronous) or uploaded through `IBlobService.StageAsync` (background). The workbook uses
inline strings (`t="inlineStr"`) throughout: no shared-string table is built, so export memory is
flat with row count.

The synchronous cap is `MaxSynchronousExportRows` (default 100,000); a background export is capped
at `MaxExportRows` (default 1,048,575 — one sheet under a header). Exceeding either fails the
export with `LimitExceededException("Excel.Export.RowLimitExceeded", rows, cap)`, whose message
names the sheet — never truncation, never multi-sheet chunking.

### 4.3 File name

`<Entity plural, ASCII-safe> <yyyy-MM-dd HHmm>.xlsx`, the timestamp in the request display zone;
the ASCII form drops every character outside `[A-Za-z0-9 _-]` and falls back to the entity's
technical name when the label leaves nothing. Spec 0015 adds the `filename*` UTF-8 form carrying
the unscrubbed label. A background export stores the same name in `Exports.FileName`.

## 5. Editable-shape export

### 5.1 The parent sheet

`ExportForImport` produces one parent sheet named as in §3.3 with these columns in order:

1. `Id` — number for `int` keys, text for `long` keys.
2. `Stamp` — hidden column (`<col hidden="1">`), `@`, the `ModifiedAt` wire string.
3. Every **editable** scalar property (`Ownership ∈ Editable`, plus `WriteOnce` properties; never
   `Derived`, which the service recomputes on import, nor `DatabaseOwned`) in declaration order,
   excluding `[ExcludeFromExcel]`, `[BlobReference]`, `[JsonColumn]`, `byte[]` and `hierarchyid`
   properties; each foreign key replaced by one **reference column** headed
   `<Navigation label> / <Key label>` (`Center / Code`, `Parent / Code`, `Role / Name (E)`); each
   `[Multilingual]` group expanded into one column per configured tenant language (`Name (E)`,
   `Name (ع)`; plain `Name` for a monolingual tenant).

The editable shape is, by construction, the set of paths the import plan maps (§8.1): a
`ForImport` file fed back to `import` maps every column, nothing has to be deleted from it, and
`Excel.Import.ServerOwnedColumn` can arise only from a display export or a hand-made sheet.

`request.Columns` restricts item 3 to the listed paths; `Id`, `Stamp` and the default natural key
column are always present. Validation lists (§3.1) go on boolean and enum columns; `@` on
text-typed columns; the header row is frozen and filterable; data sheets are **not** protected, so
users insert rows and sort.

Reference keys per foreign key: `request.ReferenceKeys[fkProperty]` names the target property;
otherwise the target's default natural key (`EntityMetadata.NaturalKey` of the target, §6); a
target without one is exported by surrogate id (`Center / Id`, number or text by key type) and the
manifest marks the column `KeyKind = Surrogate`. A multilingual target key is written in the slot
of the tenant's primary language and its language recorded in the manifest.

Property columns are queried as bare paths; reference columns as `<Navigation>.<Key>` paths
(`Parent.Code`), so the parent flat query is one `ExcelQuery` with `RootEntity = entity`,
`OrderBy = request.OrderBy ?? "Id"`, `Take = cap + 1` and the select paths
`[Id, ModifiedAt, …properties…, Parent.Code, Center.Code]`, drawn through
`EntityService.ExcelRowSource(source)` over the request's `ExportSource` case (§4.1).

### 5.2 Child sheets

One sheet per child collection (`EntityMetadata.Children`) when `request.IncludeChildren`, named
`<Parent plural> · <Collection label>` truncated to 31 characters (the manifest maps sheet names,
so truncation is safe; a post-truncation collision gets a ` (2)` suffix). Columns: the **parent
reference column** first (`User / Email` — the parent's default natural key, or `User / Id` flagged
`Surrogate` when the parent has none), then the child's `Id`, then the child's editable properties
and reference columns as in §5.1. Child rows are grouped under their parent in parent order.

Child rows are fetched per page of parent ids: after each parent page is written and its ids
collected, one `ExcelQuery(RootEntity = child entity, SelectPaths = [<ParentKey>, Id, …],
OrderBy = "<ParentKey>, Id", Restriction = KeySetRestriction("<ParentKey>", TVP),
RestrictionValues = the page's parent ids)` per collection — bounded by the parent cap, at most
`MaxIds` ids per query. The parent reference column's value is taken from the parent row already
written (the child query does not join the parent), so a child sheet never repeats a lookup.

Per-sheet cap: `MaxExportRows` per sheet, children included; exceeding it fails the export with
`Excel.Export.RowLimitExceeded` (§4.2).

### 5.3 The manifest sheet `_tellma`

A hidden sheet (`state="hidden"`, not `veryHidden`, so a curious user can inspect it), protected
without a password (`sheetProtection` with `sheet="1"`) against accidental edits, written for both
shapes. Column A is the key, columns B… the values. Rows 1–14 are the header block; the columns
table follows after one blank row.

```
Row  A                   B…
1    Tellma.Manifest     1                       -- manifest format version (int)
2    Shape               Editable | Display
3    Entity              gl.Center               -- entity name (EntityMetadata.Name)
4    Distribution        <distribution slug>     -- DeploymentIdentity.Application; part of the same-source gate
5    Platform            <Tellma.Core version>
6    SourceTenantId      <int>                   -- part of the same-source gate
7    ExportedAt          <ISO 8601, offset zero>
8    Culture             ar-SA
9    Calendar            uq                      -- CalendarCodes value
10   TimeZone            Asia/Riyadh             -- the display zone the file was written in
11   Languages           en, ar                  -- tenant slots 1..3 in order
12   SchemaFingerprint   <hex>                   -- EntityMetadata.SchemaFingerprint
13   Truncated           false                   -- reserved; always false (exports fail, never truncate)
14   RowKey              Code                    -- default row key for Update/Upsert (editable shape; blank when none)
15   (blank)
16   Sheet | Collection | Column | Header | Path | Type | Role | Language | ReferenceEntity | ReferenceKey | KeyKind | DateEncoding | DatePattern
17…  Centers |        | A | Id            | Id        | Numeric | Id        |    |        |      |         |        |
     Centers |        | B | Stamp         | Stamp     | String  | Stamp     |    |        |      |         |        |
     Centers |        | C | Code          | Code      | String  | Property  |    |        |      |         |        |
     Centers |        | D | Name (E)      | Name      | String  | Property  | en |        |      |         |        |
     Centers |        | E | Name (ع)      | Name2     | String  | Property  | ar |        |      |         |        |
     Centers |        | F | Parent / Code | ParentId  | String  | Reference |    | gl.Center | Code | Natural |        |
     Centers |        | G | Center Type   | CenterType| String  | Property  |    |        |      |         |        |
     Centers |        | H | Opened On     | OpenedOn  | Date    | Property  |    |        |      |         | Serial | dd/mm/yyyy
     Users · Role Memberships | RoleMemberships | A | User / Email | (parent) | String | ParentReference | | core.User | Email | Natural | |
```

`Role ∈ Id | Stamp | Property | Reference | ParentReference`; `KeyKind ∈ Natural | Surrogate`;
`DateEncoding ∈ Serial | Text`; `Type` is the column's Queryex type name (spec 0008 §7.1);
`Language` is the BCP 47 code of a multilingual column's slot; `Path` is the entity property path
(`(parent)` for the parent reference column). A display-shape manifest records every column with
`Role = Property` or `Reference` and `Path` = the select item's path (blank for an expression) so
that feeding a display export back yields precise `ServerOwnedColumn` errors rather than unmapped
columns.

The manifest is **untrusted input** on import: every path must exist on the entity and be
importable, every sheet and column reference must be in range, `Languages` must be BCP 47 codes,
and the same-source gate only decides whether `Id` and `Stamp` are *considered* — a manifest
never grants anything (§13).

### 5.4 The same-source gate

A file is **same-source** when the manifest's `Distribution` equals `DeploymentIdentity.Application`
and `SourceTenantId` equals the target tenant id. Only same-source files may use `Id` as a row
key, match children by `Id`, or supply `Stamp`; a sandbox export carries the sandbox's tenant id,
so an `Id` round trip into the live tenant is refused (`Excel.Import.IdKeyRequiresSameTenant`). The
gate uses the distribution slug because tenant ids are unique only within a distribution.

`SchemaFingerprintMatches` (the manifest's fingerprint equals `EntityMetadata.SchemaFingerprint`)
is informational in the plan; a mismatch changes nothing by itself — mapping proceeds by header
text and paths, and a column whose path the entity does not carry is `UnmappedColumn`.

## 6. Natural keys and reference keys

### 6.1 What this spec consumes

Natural keys are spec 0011's `EntityMetadata` facets; the members this spec uses, verbatim:

```csharp
// Tellma.Core.Abstractions.Entities (spec 0011)
public sealed class NaturalKeyAttribute(int Order = 0) : Attribute;     // on a scalar property with a list type in spec 0011 §4.4; implies [Unique]; composite keys reserved
public sealed class ExcludeFromExcelAttribute : Attribute;              // on property

// Tellma.Core.Abstractions.Data (spec 0011)
public sealed class EntityMetadata
{
    public IReadOnlyList<NaturalKeyMetadata> NaturalKeys { get; }
    public PropertyMetadata? NaturalKey { get; }                        // first of NaturalKeys
    public IReadOnlyList<ReferenceMetadata> References { get; }
    public IReadOnlyList<ChildCollectionMetadata> Children { get; }
    public IReadOnlyList<MultilingualGroup> MultilingualGroups { get; }
    public TreeMetadata? Tree { get; }
    public string SchemaFingerprint { get; }
}

public sealed record NaturalKeyMetadata(PropertyMetadata Property, bool IsDeclared, int Order);
public sealed record ReferenceMetadata(string Navigation, PropertyMetadata ForeignKey, EntityMetadata Target);
public sealed record PropertyMetadata(
    string Name, string Column, Type ClrType, PropertyOwnership Ownership, bool IsNullable, bool IsUnique,
    int? MaxLength, int? Precision, int? Scale, IReadOnlyList<string>? EnumValues, string? MultilingualGroup,
    bool IsSearchable, SearchKind? SearchKind, bool ExcludedFromExcel, Func<object, object?> Getter,
    Action<object, object?> Setter);
public enum PropertyOwnership { Editable, WriteOnce, ServerOwned, Derived, DatabaseOwned }
```

Rules spec 0011 guarantees and this spec relies on: a `[NaturalKey]` sits on any scalar property
whose CLR type has a key-list table type — `string`, `int`, `long`, `Guid`, `DateOnly`, the closed
set §10.1 restricts on — and spec 0011's realised gate fails a declared key on any other scalar; on
a top-level entity the key is backed by a single-column unique index (filtered
`WHERE [col] IS NOT NULL` when nullable); on a child entity by a unique index on
`(ParentKey, Property)`; without a declaration the inference order over unique-index-backed single
string columns is the `[Multilingual]` `Name` group, `Code`, required unique strings in declaration
order, nullable unique strings; declared keys precede inferred ones by `Order`; `NaturalKey` is the
first entry or null. Core declares `core.User.Email`, `core.Role.Code`, `gl.Center.Code`.

### 6.2 How keys are used

- A multilingual group is one natural key named after its primary member; the slot used for a
  file column is chosen per the column's language (§8.2).
- The **default reference key** of a target is `Target.NaturalKey`; `ExportForImportRequest.
  ReferenceKeys` and `ImportColumnMapping.Path` (`Center.Region`) override it per column with any
  scalar property of the target, unique or not; ambiguity is then `Excel.Import.ReferenceAmbiguous`
  (§10.3).
- **Row identity** (`ImportRequest.RowKey`) accepts only a member of `EntityMetadata.NaturalKeys`
  or `Id`. A key cell is rendered and parsed by its property's type like any other cell: a numeric
  serial number is a number, a date is a date, never text.
- **Child identity** within a parent uses the child's `NaturalKey` scoped by the parent key; a
  child without one is identified by `Id` (same-source) or replaced (§9.5).

### 6.3 Surrogate fallback

A target with no natural key is referenced by its surrogate `Id`, the column flagged
`KeyKind = Surrogate` in the manifest. Importing such a column into a non-same-source tenant is
`Excel.Import.SurrogateReferenceFromOtherTenant` at the column (row null) unless the caller remaps
it through `ColumnMappings`. Spec 0011 §3.2's startup validation warns for a referenced entity
without a natural key.

## 7. Import intake and parsing

### 7.1 Intake through staged blobs

Import files never travel in a JSON body. The client uploads the file through spec 0016's
`POST /{tenantId}/blobs/import-file?fileName=` and receives a `BlobDescriptor`; `FileId` names it.
The codec resolves the blob through `IBlobService.ResolveAsync("import-file", FileId, null)` —
which returns it only to its uploader while staged and unexpired (spec 0016's rule), so a
`FileId` that is not the caller's own staged upload is `NotFoundException("core.Blob", [FileId])`
— and opens the bytes through `IBlobStore.OpenReadAsync(tenantId, download.StorageName)`, copying
them to a temp file under `Tellma:ScratchPath` opened with delete-on-close. Every read of the
workbook is from that file: once for a synchronous import, twice for a background one (§12.4).

A synchronous import leaves the upload **staged**; spec 0016's `core.blob-sweep` reclaims it after
the 24-hour TTL. A background import attaches it to the `Import` row's `FileId` (§12.1), which
confirms it under the attach rule (the run-as user is the uploader) and keeps it for
`ImportFileRetentionDays`.

### 7.2 Package limits

Checked before any part is parsed, in this order; each failure is `ImportException` with one
error at (sheet null, row null):

| Check | Limit | Code |
|---|---|---|
| Compressed size | `min(MaxImportFileBytes, the import-file kind's MaxSize)` — 100 MiB with the defaults of §14.1 and of spec 0016 §2.4's `Attachment` preset; the synchronous byte limit is §2.3's | `Excel.Import.PackageTooLarge` (`bytes`, `max`) |
| Uncompressed size, sum of part lengths from the zip central directory | `MaxImportUncompressedBytes` (2 GB) | `Excel.Import.PackageTooLarge` |
| Any part read past its declared length (counting stream) | the declared length | `Excel.Import.PackageTooLarge` |
| Package structure: one workbook part, a `[Content_Types].xml`, no external links, no external relationships | — | `Excel.Import.MalformedWorkbook` (`reason`) |
| XML well-formedness of the parts read | — | `Excel.Import.MalformedWorkbook` |

A `.xlsm` is read as a workbook and its macro part ignored. The XML readers run with
`DtdProcessing.Prohibit` and no external resolver. Row counts for the synchronous decision come
from each sheet's `dimension` element, or from a forward count when it is absent.

### 7.3 Shared strings

The shared-string part streams into `SharedStringStore`: in memory up to
`MaxInMemorySharedStringBytes` (32 MB of UTF-16 text), beyond that an offset index over a temp file
(`tellma.excel.sst.spilled` incremented). Rich-text runs are flattened at store time. The store is
the only unbounded part of a user-saved file; bounding it by bytes rather than entries is what
keeps a web host serving several imports at once.

### 7.4 Sheet reading

Each sheet part is read forward-only with `OpenXmlReader`: the header row is row 1 of the used
range (the first row containing any non-blank cell; leading blank rows are skipped and the offset
recorded so error rows are Excel's row numbers); rows beyond the last non-blank cell of every
column are ignored; a fully blank data row is skipped without error. Cell references (`C7`) are the
coordinates every error carries; a cell without a reference attribute takes the next column in
sequence. Hidden sheets other than `_tellma` are read like any other; a `veryHidden` sheet is
`Ignored` in the plan (§8.1) and raises no warning.

## 8. Column and sheet mapping

### 8.1 The plan

Mapping runs at `inspect-import` and again at `import` time (the plan is not stored; the request
carries the overrides) and produces an `ImportPlan`:

1. **Sheets.** A sheet is matched to the parent entity or a child collection by the manifest's sheet
   table, else by its name against the entity's plural label and each collection's label in every
   tenant language and English, else against the technical collection name (`RoleMemberships`).
   Without a manifest, the first non-hidden sheet is the parent sheet. Unmatched sheets are
   `Ignored` in the plan, without a warning. `request.SheetMappings` overrides (`Collection = ""`
   for the parent sheet, `null` to ignore).
2. **Columns.** Each header cell is matched, in order: (a) the manifest's column table by header
   text (exact), yielding the path, the language of a multilingual column and the reference key
   of a reference column; (b) the header parsed as `<Label>`, `<Label> (<symbol>)`, or
   `<Navigation label> / <Key label>[ (<symbol>)]`, labels resolved in every tenant language and
   English through `ILabelProvider` and symbols through `LanguageInfo.Symbol` (spec 0012's
   `ILanguageCatalog`); (c) the technical path (`Name2`, `Parent.Code`, `CenterId`). A header
   `Name` without a symbol maps to the primary slot; `Id` and `Stamp` match by technical name.
3. **Override.** `request.ColumnMappings` (sheet + header text or column letter → path, or → null
   to ignore) wins over everything.
4. **Unmapped columns** are errors (`Excel.Import.UnmappedColumn`, with up to three `Suggestions`
   ranked by label similarity — Damerau-Levenshtein over the lower-cased header against every
   label and path) unless `request.IgnoreUnmappedColumns`, in which case they are `Ignored`.
5. **Duplicates.** Two columns mapping to the same path is `Excel.Import.DuplicateColumn` at both
   columns.
6. **Ownership.** A path whose ownership is `ServerOwned`, `Derived` or `DatabaseOwned`, or a
   `[BlobReference]`, `[JsonColumn]`, `[ExcludeFromExcel]`, `hierarchyid` or `byte[]` property, is
   `ServerOwned` in the plan and the error `Excel.Import.ServerOwnedColumn` at import;
   `WriteOnce` paths are mappable (§9.3).

`ImportPlan.RowKeyCandidates` lists `EntityMetadata.NaturalKeys` paths present in the parent
sheet plus `Id` when same-source; `DefaultRowKey` per §9.2; `TotalRows` sums every mapped sheet's
data rows; `RequiresBackground` per §2.3; `Entity` is the manifest's entity name (null without
one); `ManifestVersion` 0 without a manifest; a manifest naming a different entity than the stack
is `Excel.Import.MalformedWorkbook` (`reason = entity`).

At `import` time each child sheet's rows are also counted per parent-reference value as the sheet
is read (§7.4): a parent with more rows in one collection than the collection's `MaxCount` (spec
0011 §2.2) is `Excel.Import.TooManyChildren` (arguments `collection`, `max`, `count`) at the
parent-reference column of the first row beyond the cap — before any round trip and in either
mode, since no chunk (§12.4) can hold such a parent.

### 8.2 Language-aware mapping

A multilingual column maps to the tenant slot of the column's **language**: a file column
recorded (or parsed) as Arabic maps to whichever of `Name`/`Name2`/`Name3` is Arabic in the target
tenant (`TenantSettings.Languages` in slot order), so a file exported from a tenant whose languages
are `ar, en` imports correctly into one whose languages are `en, ar`. A column in a language the
target does not configure is **ignored with the warning `Excel.Import.UnconfiguredLanguageColumn`**
(the value has no home; review flag 5). A column that names a slot technically (`Name2`) maps to
that slot regardless of language and is subject to spec 0014's `Import.LanguageNotConfigured` when
the slot is gated off.

### 8.3 Reference columns

A reference column carries `Path` = the foreign-key property and `ReferenceKey` = the target
property the values are expressed in (from the manifest, the parsed key label, or the override
path `Center.Region`). The plan reports `ReferenceKey` per column. A reference column whose key is
multilingual resolves through the slot of the **column's** language, so the lookup path is `Name2`
rather than `Name` when that is the slot (§10.1).

## 9. Import semantics

### 9.1 Modes

| Mode | Parent rows | `Id` / `Stamp` columns |
|---|---|---|
| `Insert` | every row is new | ignored (the pipeline assigns ids); duplicates against the database surface through the pipeline's `[Unique]` validators and, for the race, 2601/2627 mapped to the column — and to the row only when the chunk carries one (spec 0014 §14.2) |
| `Update` | every row must resolve through `RowKey` to an existing, readable row; a miss is `Excel.Import.RowNotFound` at (row, key column) | `Id` is the row key when `RowKey = "Id"`; `Stamp` is the expected stamp when present |
| `Upsert` | resolved rows follow the `Update` path, the rest the `Insert` path | as `Update` for the resolved rows |

`IsActive` is server-owned: it is absent from the editable shape, every imported row is created
active, and deactivation is the action, never the sheet; the display export still includes it.

### 9.2 Row identity

`RowKey` defaults to the manifest's `RowKey` when that column is present in the sheet, else the
entity's `NaturalKey` when its column is present, else `Id` when the file is same-source; `Update`
and `Upsert` with no resolvable default and no `RowKey` in the request is `BadRequestException`.
`Id` as a row key on a non-same-source file is `Excel.Import.IdKeyRequiresSameTenant` (row null).
Before any database call, duplicate row-key values within the sheet (after §10.3 normalisation)
are `Excel.Import.DuplicateRowKey` at every duplicate row, and duplicate values in any `[Unique]`
column are reported the same way against both rows.

### 9.3 Hydration and write-once columns

For `Update` and `Upsert`, resolved ids are hydrated through `EntityService.GetByIdsAsync(ids,
DetailsRequest.None)` — the complete entity with its child collections, under the caller's read
filter, so a row the caller cannot read is absent and reported as `RowNotFound` (§10). The codec
overlays the mapped columns on the hydrated entity, so a partial sheet touches only the columns it
contains; the hydrated `ModifiedAt` stays on the entity as the expected stamp unless the sheet
carries `Stamp` on a same-source file, in which case the sheet's value is parsed and set instead
(an unparsable stamp is `Excel.Import.InvalidCell` at the cell).

`WriteOnce` paths are mappable in every mode; on an update a mapped value differing from the
hydrated value is `Excel.Import.WriteOnceChanged` at the cell (the codec's early form of the
pipeline's `WriteOnce` error, raised before the save so the row is reported with coordinates);
equal values are accepted — the natural key is often the write-once column.

### 9.4 Blank cells

A blank cell in a mapped column sets the property to null; a non-nullable property then fails the
pipeline's `Required` validation at that cell. To leave a column untouched, remove it from the
sheet or map it to null. No sentinel exists.

### 9.5 Children

- A child sheet **present** in the workbook means "these are the complete children of every parent
  row present in the parent sheet": each parent's collection is synchronised to its sheet rows.
  A child row whose parent reference matches no parent-sheet row is `Excel.Import.OrphanChildRow`
  at the child's parent column.
- A child sheet **absent** from the workbook leaves children untouched: the collection is passed
  as `null` (spec 0011's "untouched" value).
- A parent row present in the parent sheet with **no rows** in a present child sheet gets an
  empty list: its children are deleted (review flag 6).
- Existing children come from hydration (§9.3). Child rows match existing children by the child's
  `Id` column when same-source and present, else by the child's `NaturalKey` within the parent,
  else the collection is replaced (every existing child deleted, every sheet row inserted). A
  matched child keeps its id and is overlaid like a parent; an unmatched sheet row is new (`Id =
  0`); an unmatched existing child is dropped from the list and thereby deleted by the emitter.
- In `Insert` mode child sheets are inserts only; a child `Id` column is ignored.

### 9.6 Trees

For a tree entity (`EntityMetadata.Tree` non-null) the `Parent / <Key>` column is an ordinary
reference column. Resolution order: in-sheet match, then database lookup, then
`ReferenceNotFound`. After resolution the codec assigns every new row a temporary id (`-1, -2, …`,
unique within the payload), sets `ParentId` to the parent's id — the hydrated id for an existing
parent, the temporary id for an in-sheet new parent, the resolved id for a database parent — and
orders rows so that in-sheet parents precede their children (Kahn's algorithm over the in-sheet
graph). A cycle among sheet rows is `Excel.Import.ParentCycle` reported at every row in the cycle
(arguments `rows`), before any database call. The pipeline rewrites temporary ids in every
self-typed foreign key and validates cycles against the loaded ancestor chains (spec 0014's tree
capability); a single bulk `INSERT` from one TVP satisfies the self-referencing foreign key
regardless of order, so the ordering serves chunk boundaries (§12.4) and readable errors, not
insert correctness.

## 10. Bulk resolution

### 10.1 Lookups

The codec emits one `ExcelQuery` per distinct **(target entity, key path)** pair across every sheet
and column — the row key, every reference column, every child sheet's parent reference, tree parents
not found in-sheet — with `SelectPaths = [<KeyPath>, Id]`, a `KeySetRestriction("<KeyPath>", TVP)`
as `Restriction`, and `RestrictionValues` = the distinct sheet values of that key after §10.3
normalisation, in chunks of `MaxIds` values. `ExcelOperations` runs each through
`EntityService.ExcelRowSource(ExportSource.All)` of the **imported** stack (no clauses: the
restriction is the whole predicate): spec 0014 compiles a query whose `RootEntity` is a lookup
target on that entity under the caller's `Read` decision for its resource — `Denied` yields no rows,
so an unreadable reference resolves as not found. The compiled shape, illustratively (the row source
binds the TVP as `@tb{b}_t{i}` and names it as the restriction's `TableSource`):

```sql
SELECT [T].[Code], [T].[Id]
FROM   [gl].[Centers] AS [T]
WHERE  [T].[Code] IN (SELECT [Id] FROM @tb3_t0)          -- KeySetRestriction, StringList TVP
  AND  (<the caller's read predicate on gl.Center, compiled from its FilterTree>)
```

The TVP is `StringList` (`nvarchar(450)`), `IdList`, `BigIdList`, `GuidList` or `DateList` by the
key's CLR type; a key value longer than 450 characters is `Excel.Import.KeyTooLong` at the cell
before any query. A multilingual key restricts on the slot of the file column's language (`Name2`
when that is the Arabic slot).

### 10.2 In-sheet first

Before a lookup for a reference to the entity being imported (tree parents; any self-reference),
the value is matched against the sheet's own rows by the same key — the sheet's *mapped* column for
that key, after normalisation. A hit binds the reference to that row's entity (its temporary id
for a new row, its hydrated id for an existing one) and the value is excluded from the lookup's
TVP. Only misses go to the database.

### 10.3 Matching

Sheet values are trimmed and NFC-normalised; database values are grouped after the same
normalisation with `StringComparer.OrdinalIgnoreCase` (numeric and `Guid` keys compare by value).
Exactly one row ⇒ resolved; zero ⇒ `Excel.Import.ReferenceNotFound` at (row, column) — which is
also what a row hidden by the caller's read filter yields; more than one ⇒
`Excel.Import.ReferenceAmbiguous` with argument `count`. A database row the codec cannot attribute
to any sheet value (a collation equivalence beyond case: accent- or width-insensitivity) is
`ReferenceNotFound` with argument `collation = true` so the message can hint at it. The row key
lookup is a lookup like any other; its resolved ids feed hydration (§9.3).

### 10.4 The session

```csharp
// Tellma.Core.Excel (runtime)
public interface IExcelImporter
{
    Task<ImportPlan> InspectAsync(
        Stream workbook, EntityMetadata entity, InspectImportRequest request, ExcelContext context);
    Task<ExcelImportSession<TEntity>> ParseAsync<TEntity>(
        Stream workbook, EntityMetadata entity, ImportRequest request, ExcelContext context);
}

public sealed class ExcelImportSession<TEntity>
{
    // deduplicated by (entity, key path); RestrictionValues filled
    public IReadOnlyList<ExcelQuery> Lookups { get; }
    public int RowCount { get; }   // parent rows in topological order
    public ExcelChunk<TEntity> Resolve(
        int fromRow, int toRow,
        IReadOnlyDictionary<ExcelQuery, IReadOnlyList<IReadOnlyList<object?>>> lookupRows,
        Func<int, IReadOnlyList<object>> allocateIds);
}

public sealed record ExcelChunk<TEntity>(
    IReadOnlyList<TEntity> Entities, IReadOnlyDictionary<ValidationPath, ExcelCell> CoordinateMap,
    IReadOnlyList<ImportError> Errors);
public sealed record ExcelCell(string Sheet, int Row, string Column);
```

| Member | Meaning |
|---|---|
| `Parse` | Reads the whole workbook once: mapping (§8), decoding of every mapped cell (§3.1), in-file uniqueness (§9.2), the in-sheet parent graph and cycle check (§9.6), child grouping (§9.5). Any error is collected; `Parse` throws `ImportException` when errors exist, so no lookup runs for a file that cannot be saved. |
| `Lookups` | The queries of §10.1; `ExcelOperations` runs them and, for `Update`/`Upsert`, hydrates the row key's ids through `GetByIdsAsync`. |
| `Resolve` | For parent rows `fromRow..toRow` (0-based indexes into the topological order): binds lookup results, overlays hydrated entities (passed through `lookupRows` under the row-key query, one hydrated entity per resolved id), assigns temporary ids to new rows through `allocateIds` (the codec's own negative sequence; the allocator is never involved), attaches children, sets tree parent keys, and returns the chunk's entities with the coordinate map and the resolution errors (`ReferenceNotFound`, `ReferenceAmbiguous`, `RowNotFound`, `WriteOnceChanged`, `OrphanChildRow`). |
| `CoordinateMap` | `ValidationPath` segments → cell, matched segment by segment (shown here in rendered form): `[i]` → the parent row; `[i].<Property>` → its column when present in the sheet; `[i].<Collection>[j]` → the child row; `[i].<Collection>[j].<Property>` → its column. A path with no column maps to the row alone. |

## 11. The save, concurrency, and errors

### 11.1 The save

`ExcelOperations.Import` hands each chunk's entities to `EntityService.SaveAsync(entities,
SaveOptions { ReturnEntities = false, Details = None, Concurrency = request.Concurrency, Source =
Import })`. Entities carry `Id = 0` or a temporary id for inserts, the hydrated id and expected
`ModifiedAt` for updates, child collections as `null` (untouched), `[]` (delete all) or the
synchronised list; server-owned members at their defaults for the pipeline to overwrite. The
pipeline runs preprocessing, validation (the two-stage access pre-check on the hydrated ids,
`[Unique]` and foreign-key validators, tree cycle validation), persist and effects exactly as for a
JSON save; an import bumps whatever version tags the emitter bumps.

### 11.2 Concurrency

Every hydrated row saves under `Check` with its expected stamp — the sheet's `Stamp` on a
same-source file, else the `ModifiedAt` read at hydration — so a concurrent edit to any column
between hydration and persist is a conflict, never a silent overwrite. The pipeline's
`ConcurrencyException` is translated: each `ConcurrencyConflict.Id` maps through the resolved ids
to its sheet row and becomes `Excel.Import.ConcurrencyConflict` at (row, `Stamp` column when
present, else the row) with arguments `modifiedAt`, `modifiedBy`; a conflict marked `IsMissing`
becomes `Excel.Import.RowNotFound`. `Concurrency = Override` on the request disables the stamp
comparison for the whole import (`tellma.crud.concurrency.overrides` counts it), never the
existence check.

### 11.3 Error translation

Every pipeline `ValidationError` (`Path` as segments, `Code`, `Arguments`) is mapped through the
chunk's `CoordinateMap` — the segments are matched one by one, never parsed from a string — into an
`ImportError(Sheet, Row, Column, Header, Property, Code, Arguments)`: the cell's coordinates, the
CLR name of the path's last property segment as `Property` (null when the path ends at a row or is
the whole payload), and the pipeline's code and arguments unchanged (`Required`, `MaxLength`,
`Unique`, `Fk.NotFound`, `Tree.Cycle`, `WriteOnce`, `Import.LanguageNotConfigured`, …); a path the
map cannot place (a validator error on the whole payload) yields sheet null, row null. Codec errors
are already `ImportError`s with `ExcelErrorCodes`. `ImportException.Errors` is capped at
`MaxReportedErrors` in sheet, row, column order with `TotalErrors` uncapped.

A synchronous import is one transaction: any error anywhere — parse, resolution, validation,
persist — rolls back everything and returns `ImportException` (422). No partial success.

## 12. Background exports and imports

### 12.1 The `Export` and `Import` entities

```csharp
// Tellma.Core.Abstractions.Excel
// table core.Exports; [Stack(Operations = Query | Details | Delete | Enlist)]; resource core.Export
public sealed class Export : TopLevelEntity, IJobEntity
{
    public string Resource { get; set; }
    public ExportKind Kind { get; set; }
    public string? RequestJson { get; set; }
    public string? FileName { get; set; }
    public int? FileId { get; set; }
    public int? RowCount { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }           // datetimeoffset(3)
}

// table core.Imports; [Stack(Operations = Query | Details | Delete | Enlist)]; resource core.Import
public sealed class Import : TopLevelEntity, IJobEntity
{
    public string Resource { get; set; }
    public ImportMode Mode { get; set; }
    public string? RequestJson { get; set; }
    public int? FileId { get; set; }
    public int? ResultFileId { get; set; }
    public int? RowCount { get; set; }
    public int? ErrorCount { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }           // datetimeoffset(3)
}

// Tellma.Core.Excel (runtime)
// ContributeAsync enqueues core.export for every new row whose JobId is null
public sealed class ExportService : EntityService<Export>;
public sealed class ImportService : EntityService<Import>;   // the same for core.import
public sealed class ExportAccessCriteria : IAccessCriteriaProvider;   // Resource = core.Export; CreatedById = me() for Read and Delete, reason self
public sealed class ImportAccessCriteria : IAccessCriteriaProvider;   // the same for core.Import

// stored in Jobs.ArgumentsJson
public sealed record ExcelJobCulture(string Culture, string Calendar, string TimeZone, string Language);
public abstract record ExportJobArguments(string Resource, ExcelJobCulture Culture);   // JSON-polymorphic on "kind": Display | ForImport (ExportKind)
public sealed record DisplayExportJobArguments(string Resource, ExcelJobCulture Culture, ExportRequest Request)
    : ExportJobArguments(Resource, Culture);
public sealed record ForImportExportJobArguments(string Resource, ExcelJobCulture Culture, ExportForImportRequest Request)
    : ExportJobArguments(Resource, Culture);
public sealed record ImportJobArguments(string Resource, ExcelJobCulture Culture, ImportRequest Request);
```

The server-written columns — `Id`, the four audit columns and `JobId` — are `[ServerOwned]`;
`Resource`, `Kind`/`Mode`, `RequestJson` and `ExpiresAt` are `[WriteOnce]`, fixed when the row is
enqueued; `FileName`, `FileId`, `RowCount`, `ResultFileId` and `ErrorCount` are editable, which the
handlers need and no client can reach because the stacks project `query`, `get`, `get-by-ids`,
`delete`, `delete-by-query` and register `core.Export`/`core.Import` × `Read | Delete` — no `Save`
securable and no `save` endpoint. Both stacks declare `Enlist`, and every save of a row is an
enlisted save (spec 0014 §13.3): `ExcelOperations<TEntity> : IEnlists<Export>, IEnlists<Import>`
enlists the request path's insert with the invoker's frame (§12.2), and
`ExportJobHandler : IEnlists<Export>` and `ImportJobHandler : IEnlists<Import>` enlist the handlers'
writes with the job frame (§12.3, §12.4, §12.6), so each row runs its own stack's validators and
effects as a participant of the frame that authorises it. `ExportAccessCriteria` and
`ImportAccessCriteria` supply the self-scope criterion `CreatedById = me()` for `Read` and `Delete`
(spec 0013 §5.1), so every member sees and may delete their own rows ("My exports"); an
administrator with a stored grant sees all. Deleting a row releases its blobs through the emitter's
`[BlobReference]` capture (spec 0016).

**`core.Exports`** — carries `TopLevelEntity` and `IJobEntity`; non-temporal; `[TableType]`;
sequence `core.sq_Exports`.

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK clustered, `core.sq_Exports`; `CK_Exports_Id CHECK ([Id] > 0)` | |
| `Resource` | `varchar(128)` | no | | securable resource of the exported stack (`gl.Center`) |
| `Kind` | `varchar(9)` | no | | `Display` / `ForImport` |
| `RequestJson` | `nvarchar(max)` | yes | | the serialized request; ≤ 64 KB |
| `FileName` | `nvarchar(255)` | yes | | §4.3; the blob file-name length of spec 0016 §3.5 |
| `FileId` | `int` | yes | `FK_Exports_FileId → core.Blobs`, `UX_Exports_FileId WHERE FileId IS NOT NULL` | `[BlobReference("export-file", Attachment, ReadAccess = OwnerRead)]` |
| `RowCount` | `int` | yes | | data rows written |
| `ExpiresAt` | `datetimeoffset(3)` | no | `IX_Exports_ExpiresAt (ExpiresAt)` | set on insert from `RequestContext.Now` plus `ExportFileRetentionDays` |
| `JobId` | `int` | yes | `FK_Exports_JobId → core.Jobs ON DELETE SET NULL`, `UX_Exports_JobId WHERE JobId IS NOT NULL` | |
| audit set | | | `FK_Exports_CreatedById`, `FK_Exports_ModifiedById` | `IX_Exports_CreatedBy (CreatedById, CreatedAt DESC)` |

**`core.Imports`** — the same shape; sequence `core.sq_Imports`.

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK clustered, `core.sq_Imports`; `CK_Imports_Id CHECK ([Id] > 0)` | |
| `Resource` | `varchar(128)` | no | | |
| `Mode` | `varchar(8)` | no | | `Insert` / `Update` / `Upsert` |
| `RequestJson` | `nvarchar(max)` | yes | | the serialized request; ≤ 64 KB |
| `FileId` | `int` | yes | `FK_Imports_FileId → core.Blobs`, `UX_Imports_FileId WHERE FileId IS NOT NULL` | `[BlobReference("import-file", Attachment, ReadAccess = OwnerRead)]`; the uploaded workbook |
| `ResultFileId` | `int` | yes | `FK_Imports_ResultFileId → core.Blobs`, `UX_Imports_ResultFileId WHERE ResultFileId IS NOT NULL` | `[BlobReference("import-result", Attachment, ReadAccess = OwnerRead)]`; the `ImportOutcome` or `ImportException` as JSON |
| `RowCount` | `int` | yes | | parent rows in the file |
| `ErrorCount` | `int` | yes | | `TotalErrors` of a failed import; 0 on success |
| `ExpiresAt` | `datetimeoffset(3)` | no | `IX_Imports_ExpiresAt (ExpiresAt)` | set on insert from `RequestContext.Now` plus `ImportFileRetentionDays` |
| `JobId` | `int` | yes | `FK_Imports_JobId → core.Jobs ON DELETE SET NULL`, `UX_Imports_JobId WHERE JobId IS NOT NULL` | |
| audit set | | | | `IX_Imports_CreatedBy (CreatedById, CreatedAt DESC)` |

Both are Queryex roots (`core.Export`, `core.Import`) with the navigations `File`, `ResultFile`,
`Job`, `CreatedBy`, `ModifiedBy` derived from their foreign keys. Retention: spec 0019's
`core.file-retention` (daily) selects the ids of rows whose `ExpiresAt` has passed in pages of 500
and deletes them through `DeleteByIdsAsync` on both stacks as the system user; the capture releases
their blobs and `core.blob-sweep` reclaims the bytes.

### 12.2 Enqueue

On `export/start`, `export-for-import/start` and `import/start`, `ExcelOperations` builds one
`Export`/`Import` row — `Resource = Descriptor.Resource`, `Kind`/`Mode`, `RequestJson` (the
serialized request), `FileId = request.FileId` on an import (which attaches the staged upload), and
`ExpiresAt` computed from `RequestContext.Now` plus `ExportFileRetentionDays` or
`ImportFileRetentionDays` — enlists its insert (`EnlistSaveAsync`) with the `IOpenWriteHost` that
`IApiActionInvoker` registers in the scope for the frame it opens around the method, and awaits
`host.PersistAsync()` on it (spec 0014 §13.3). The service's `ContributeAsync` type-tests its
context as a `SavePersistContext` and, for every new row (its `Before(i)` null) whose `JobId` is
null, calls `IJobQueue.Enqueue(context.Batch, requests)` with one `JobRequest` per row —
`HandlerKey` `core.export` or `core.import`, `Arguments` the row's job arguments, `DueAt = null`,
`RequestedById` and `RunAsUserId` the caller, `Entity` the row — so the row insert, the job insert
and the `JobId` write-back are one transaction (spec 0019's enqueue statement). The arguments
(§12.1) are an `ExportJobArguments` of the row's `Kind` case or an `ImportJobArguments`, each
carrying the typed request deserialized from the row's `RequestJson` and an `ExcelJobCulture` of
`context.Context`'s negotiated `Culture`, `Calendar`, `TimeZone` (display zone) and `Language`, so
the job renders exactly as the request would have. The `/start` methods return
`JobAccepted(JobId, ExportId)` or `JobAccepted(JobId, ImportId)`, which spec 0015 answers with 202.
The client follows progress through `jobs/query` (self-scope) and the hub's `job.changed`, and the
artifact through `exports/get` and the blob GET.

### 12.3 The export handler

`ExportJobHandler : IEntityJobHandler<Export>, IEnlists<Export>` with `[JobHandler("core.export",
BatchSize = 1, LeaseSeconds = 600, MaxAttempts = 3, Schedulable = true)]`. Steps, in the job scope
of the run-as user, inside the job frame spec 0019's worker opens around `ExecuteAsync` (spec 0014
§13.3):

1. `Item` is the `Export` row and the job's `ArgumentsJson` its `ExportJobArguments` (§12.2). `Item`
   is null when a user schedule naming `core.export` fired the job with an `ExportJobArguments` JSON
   and its `kind` in `ArgumentsJson` (spec 0019 §14.1): the handler then builds the row from the
   arguments — `Resource`, `Kind` from the case, `RequestJson` from `Request`, `ExpiresAt` — and
   inserts it at step 5.
2. Re-evaluate `IAccessEvaluator.RequireAsync(Resource, "Read")`; a denial completes the item
   `Fail(JobError("forbidden", …))`.
3. Build the `ExcelContext` from the arguments' `ExcelJobCulture` (culture, calendar, zone,
   language); plan the case's `Request` in its shape (`PlanDisplay` or `PlanEditable`); stream the
   query through the row source in pages under the `MaxExportRows` cap; `Progress.Report` after each
   page (`percent = rows / cap`, message = rows written).
4. Stage the spooled file through `IBlobService.StageAsync(BlobStageRequest("export-file", stream,
   length, xlsx content type, FileName))`.
5. Enlist the row's write with the job frame (`EnlistSaveAsync`): when `Item` is set, an update of
   it with `FileId`, `FileName`, `RowCount` under `EnlistSaveOptions.Concurrency = Check` against
   the stamp the claim delivered; for a schedule-fired run, an insert of the row built at step 1
   with `FileId`, `FileName`, `RowCount` and `JobId = Job.Id`, naming `JobId` in
   `EnlistSaveOptions.ServerOwned`, which `ExportService.ContributeAsync` skips because `JobId` is
   set (§12.2). Awaiting `host.PersistAsync()` on the `IOpenWriteHost` spec 0019's worker registers
   for the job frame runs the row's validation round and appends the group to the partition's
   completion batch, so the row commits with the job outcome and the blob effect confirms the staged
   file inside the completion transaction (the run-as user is uploader and saver, spec 0016 §4.6).
6. `Item.NotifyOnSuccess(NotificationRequest("core.export.ready", [RunAsUserId], Arguments = {
   fileName, rowCount }, TargetResource = "core.Export", TargetId = ExportId))` — the argument
   names are spec 0020's catalogue entries for the type; `ExportId` is the row's id, which
   `EnlistedSave.Rows` carries for a schedule-fired run; `Succeed()`.

Re-runs are safe: a re-run re-stages and enlists the write again on the fresh claim, and the
capture releases a `FileId` an earlier attempt confirmed. A cap overflow completes the item
`Fail(JobError("Excel.Export.RowLimitExceeded", …))` without a notification of its own (spec
0019's `core.job.failed` covers it).

### 12.4 The import handler

`ImportJobHandler : IEntityJobHandler<Import>, IEnlists<Import>` with `[JobHandler("core.import",
BatchSize = 1, LeaseSeconds = 600, MaxAttempts = 1)]` — a partially committed import never re-runs
blindly; a crash mid-import surfaces as `attempts_exhausted` and spec 0019's `core.job.failed`; an
administrator's `retry` action (`core.Job × Retry`) resets attempts and the handler resumes from
its checkpoint. The handler is a host, not a participant of the target stack's pipeline (spec 0014
§13.3): each chunk's `SaveAsync` is the target stack's front door, and only the `Import` row's
completion write is enlisted (§12.6). Two passes over the local copy of `Import.FileId` (§7.1):

1. **Pass 1 (no database):** `Parse` of the job's `ImportJobArguments.Request` under an
   `ExcelContext` built from its `Culture` — mapping, decoding of every cell, in-file uniqueness,
   the in-sheet parent graph and cycle check, row counts. Any error completes the item with the
   outcome of §12.6 before any commit.
2. **Pass 2:** parent rows in topological order are processed in chunks of `ImportChunkRows`
   (default 10,000 parent rows plus their child rows; a tree parent's chunk never follows its
   child's). A chunk is closed early when it would otherwise exceed the stack's `MaxSaveCount`
   parents or its `StackLimits.MaxRowsPerSave` counted over every row at every depth — parents,
   children and grandchildren — so no chunk is refused by the save (spec 0014 §2.5, §6.8). Each
   chunk runs resolution (§10), hydration, `Resolve`, and `SaveAsync` as its own transaction.
   **The checkpoint rides the chunk's persist transaction** (§12.5): `StateJson =
   { ChunkIndex, RowsProcessed }`, `ProgressPercent = RowsProcessed / RowCount`. A job re-leased
   after a crash or lease loss re-runs pass 1 (the file is immutable) and resumes at
   `ChunkIndex + 1` — a committed chunk is never re-applied. A validation or persist error in chunk
   `k` stops the job: chunks `< k` stay committed; the outcome reports `CommittedRowRanges` and the
   errors of chunk `k`. Cancellation (`CancelRequestedAt`, lease loss, host stop) takes effect at a
   chunk boundary; an in-flight transaction rolls back with its checkpoint.

`ImportRequest.Atomic = true` runs the whole file as one chunk (one transaction) and is refused
above `MaxAtomicImportRows`, above the stack's `MaxSaveCount` parents, or above its
`MaxRowsPerSave`, at request time (§2.3); a parent above its collection's `MaxCount` is refused as
its child sheet is read, in either mode (§8.1). Progress phases (`ProgressMessage`): `Parsing`,
`Resolving`, `Validating`, `Saving`, `Uploading`; the worker renews the lease while the handler
runs. Lock escalation inside a 10,000-row chunk is accepted: the transaction is short and the
escalation is the one a 10,000-row JSON save incurs.

### 12.5 The checkpoint effect

`ImportCheckpointEffect<TEntity> : IPersistEffect<TEntity>` is registered by the Excel feature for
every stack with `Import`. It reads a scoped holder `ImportCheckpoint` (`Tellma.Core.Excel`, members
`Progress: IJobProgress?`, `Percent`, `Message`, `State`) that `ImportJobHandler` sets before each
chunk's `SaveAsync` and clears after; when set, `ContributeAsync(context)` calls
`ImportCheckpoint.Progress.Append(context.Batch, percent, message, state)` with the `Batch` of the
base `PersistContext<TEntity>` (no save-only member) — spec 0019's fenced checkpoint, which throws
`50422 Job.LeaseLost` inside the transaction when the lease is gone, so the chunk and its checkpoint
roll back together. Outside a background import the holder is empty and the effect is inert.
`AfterCommitAsync` does nothing.

### 12.6 Completion

On success the handler writes the `ImportOutcome` as JSON to a staged `import-result` blob and
enlists an update of the `Import` row with the job frame (`EnlistSaveAsync`, spec 0014 §13.3):
`ResultFileId`, `RowCount`, `ErrorCount = 0`, under `EnlistSaveOptions.Concurrency = Check` against
the stamp the claim delivered; `await host.PersistAsync()` on the injected `IOpenWriteHost` places
the row and its blob confirmation on the completion batch exactly as §12.3 step 5 does for the
`Export` row; then `NotifyOnSuccess` with a `NotificationRequest("core.import.completed", …)` to
`[RunAsUserId]` — arguments `fileName`, `rowCount`, `errorCount`, `TargetResource = "core.Import"`,
`TargetId = ImportId` — and `Succeed()`. On a deliberate failure (pass-1 errors, chunk `k` errors)
it writes the capped `ImportException` payload (errors, `TotalErrors`, `CommittedRowRanges`) to the
`import-result` blob, enlists the row's update the same way with `ResultFileId` and
`ErrorCount = TotalErrors` and awaits `host.PersistAsync()`, appends
`INotifier.Notify(batch.Batch, [request])` to the completion batch with a
`NotificationRequest("core.import.failed", …)` to `[RunAsUserId]` — arguments `fileName` and
`errorCode` (the first error's code), `TargetResource = "core.Import"`, `TargetId = ImportId` — and
`Fail(JobError("Excel.Import.Failed", message, details = null))`. The argument names of all three
are spec 0020's catalogue entries. The three notification types are `Mutable = false` (spec 0020):
the user cannot mute them, because the result is also reachable from the `imports` page and muting
would strand nothing, and the default stays on.

## 13. Access control and security

- `Export`/`ExportForImport` require `Read` on the stack and `InspectImport`/`Import` require
  `Save`, evaluated by the invoker before the method runs (§2.2); the read decision's filter is
  applied through the row source exactly as `query` and `get` apply it.
- The pipeline's pre-check runs on the hydrated rows (before images under `Read`, the `Save`-grant
  count beside them) and its post-check over `@tb{b}_saved` after the write; a row visible for
  reading but not writable is the pipeline's `ForbiddenException` translated to the row.
- Reference resolution applies the target's read filter (§10.1); a hidden row is
  `ReferenceNotFound`, so the importer is never an existence oracle. Foreign keys arriving as raw
  ids through JSON are validated under the same rule by spec 0014, so Excel and JSON agree.
- Ids from a sheet are never inserted; they select rows that hydration (under `Read`) and the
  pre-check (under `Save`) verify.
- The manifest is validated, never trusted (§5.3); the same-source gate is a mapping decision, not
  an authorisation.
- Package limits (§7.2), the shared-string byte budget (§7.3), prohibited DTDs and external
  resolvers bound the parser; the temp files under `Tellma:ScratchPath` are delete-on-close and
  never named after user input.
- A staged import file is readable only by its uploader (spec 0016's rule), so `FileId` cannot
  name another user's upload.
- An `Export`/`Import` row is saved only as an enlisted save (§12.1) under the authority of the
  frame that hosts it — the `export/start`, `export-for-import/start` or `import/start` action's own
  securable on the request path, the job's run-as scope in the handlers (spec 0014 §13.3) — and the
  stacks register no `Save` securable, so no request can save one.
- No `Export` securable action exists this release (review flag 10).

## 14. Configuration, error codes, telemetry

### 14.1 Options — `Tellma:Excel`

```csharp
// Tellma.Core.Abstractions.Excel
public sealed class ExcelOptions
{
    public int MaxSynchronousExportRows { get; set; } = 100000;
    public int MaxExportRows { get; set; } = 1048575;
    public int MaxSynchronousImportRows { get; set; } = 10000;
    public int MaxAtomicImportRows { get; set; } = 100000;
    public int ImportChunkRows { get; set; } = 10000;
    public long MaxSynchronousImportFileBytes { get; set; } = 16L * 1024 * 1024;        // 16 MB
    public long MaxImportFileBytes { get; set; } = 100L * 1024 * 1024;                  // 100 MiB
    public long MaxImportUncompressedBytes { get; set; } = 2L * 1024 * 1024 * 1024;     // 2 GB
    public long MaxInMemorySharedStringBytes { get; set; } = 32L * 1024 * 1024;         // 32 MB
    public int MaxReportedErrors { get; set; } = 1000;
    public int ExportFileRetentionDays { get; set; } = 7;
    public int ImportFileRetentionDays { get; set; } = 7;
}
```

Validated at startup (reported into the realised gate): every value positive;
`MaxSynchronousExportRows ≤ MaxExportRows ≤ 1,048,575`; `ImportChunkRows ≤
MaxAtomicImportRows`; `MaxSynchronousImportFileBytes ≤ MaxImportFileBytes ≤
MaxImportUncompressedBytes`.

### 14.2 Error codes

`ExcelErrorCodes` (constants; hosts localize through the request culture): the closed set in
Appendix A. Codes are dotted PascalCase resource keys; `Excel.Import.UnconfiguredLanguageColumn`
is the only warning.

### 14.3 Telemetry

`ExcelTelemetryNames` (constants; `MeterName = "Tellma.Core"`):

| Instrument | Kind | Tags |
|---|---|---|
| `tellma.excel.export.rows` | histogram | `entity`, `shape` (`display` / `editable`), `background` |
| `tellma.excel.export.duration` | histogram (s) | the same |
| `tellma.excel.export.rejected` | counter (cap exceeded) | `entity`, `shape` |
| `tellma.excel.import.rows` | histogram | `entity`, `mode`, `background` |
| `tellma.excel.import.duration` | histogram (s) | the same |
| `tellma.excel.import.lookups` | histogram (resolution queries per import) | `entity` |
| `tellma.excel.import.errors` | counter | `entity`, `code` |
| `tellma.excel.import.chunks` | histogram (chunks per background import) | `entity` |
| `tellma.excel.import.resumed` | counter | `entity` |
| `tellma.excel.sst.spilled` | counter | — |

Tag names as constants `EntityTag`, `ShapeTag`, `ModeTag`, `BackgroundTag`, `CodeTag`. No tenant
or user tags; tenant and user ids go to the log scope. Log events (`Tellma.Core.Excel` category):
`ExportCompleted` (entity, rows, ms), `ExportRejected` (entity, rows, cap), `ImportCompleted`
(entity, mode, inserted, updated, ms), `ImportFailed` (entity, mode, total errors, first code),
`ImportChunkCommitted` (import id, chunk index, rows), `ImportResumed` (import id, chunk index),
`SharedStringsSpilled` (bytes) — all at Information except `ImportFailed` (Warning).

## 15. Testing

Test projects mirror `src/`: `test/core/Tellma.Core.Tests/Excel/` (pure) and
`test/core/Tellma.Core.IntegrationTests/Excel/` (`Category=Integration`, LocalDB or the
Testcontainers SQL Server of spec 0011's fixture). No suite carries `Live=true`.

**Pure suite** (runs on every PR, Windows and Linux):

- The planner over spec 0011's fixture entities (`test/shared/Tellma.Testing.Entities`:
  `fixture.Widgets`, multilingual with three languages, with children `fixture.WidgetParts`, the
  tree `fixture.Nodes`, the `long`-keyed `fixture.Shipments`): column
  sets, header labels per culture, number formats, sheet names, truncation and collision suffixes,
  the `Columns` subset rule, reference-key defaults and overrides, surrogate fallback.
- Encoder/decoder round trips for every row of §3.1 including the 15-digit boundary for `long`
  and `decimal`, ISO 8601 with offsets, `TimeOnly` fractions; date systems (1900 and 1904 workbooks;
  serial 60 refused); every cell kind of §3.1 (shared, inline, rich text, `str`, cached formula,
  formula without a cached value, error cell).
- Number-format construction for `en-US`, `ar-SA` (`gc` and `uq`), `am-ET` (`et` → text with the
  manifest pattern), a custom culture with no LCID.
- Manifest write and read, the same-source gate, `SchemaFingerprintMatches`, a hostile manifest
  (out-of-range indexes, unknown paths, wrong entity).
- Mapping across tenant-language permutations (`en, ar` → `ar, en`; a language the target lacks;
  technical paths; header parsing with symbols; overrides by header and by letter; suggestions).
- In-file uniqueness, in-sheet resolution, topological ordering and cycle detection, the
  per-parent child cap (`TooManyChildren`); the coordinate map for every path shape of §10.4;
  error capping and `TotalErrors`.
- The workbook corpus under `Excel/Corpus/`: files saved by Excel, LibreOffice and Google Sheets
  (shared-string table present, `dimension` absent, renamed headers, inserted columns, a 1904
  workbook, a `veryHidden` sheet, an error cell, a formula without a cached value, an `.xlsm`);
  package-limit rejection with crafted zips (oversized part, external link, two workbook parts).
- Option validation.
- Request validation: an export body mapped to exactly one `ExportSource` case (`Ids` beside a
  filter clause, and a body with neither, refused; an empty `Ids` list the `ExportForImport`
  template); `Atomic = true` on `import` refused with `BadRequestException`; `ExportJobArguments`
  round-tripping through JSON on its `kind` for both cases.

**Integration suite** (PR on LocalDB; nightly on Testcontainers):

- Export → import round trip of the fixture entities in all three modes with children and the tree,
  same-source and cross-tenant (two fixture tenants), asserting row and child counts, `IsActive`
  absent from the editable shape and true after `Insert`, `WriteOnceChanged` on `Update`.
- Resolution under row-level security: a hidden reference is `ReferenceNotFound`; a readable but
  unwritable row is the pipeline's 403 translated to the row.
- Concurrency: a row edited between hydration and persist is `ConcurrencyConflict`; `Override`
  passes; a same-source `Stamp` older than the row conflicts.
- Synchronous caps: `MaxSynchronousExportRows + 1` rows rejected with a message naming
  `export/start` and no job enqueued; `TooLargeForSynchronous` with a message naming `import/start`.
- Background export through `export/start` and the job worker (spec 0019's test harness): the
  `Export` row completed in the job's completion transaction with its blob confirmed there, the
  notification, retention deleting the row and releasing the blob; a schedule-fired `core.export`
  run with `ForImportExportJobArguments` inserting its own row with `Kind = ForImport` and `JobId`
  set and enqueuing no second job.
- Chunked background import through `import/start` with `ImportChunkRows = 100`, the call answering
  `JobAccepted`: a forced failure in chunk 2 reports `CommittedRowRanges` for chunk 1; a simulated
  lease loss during chunk 3 rolls back the chunk and its checkpoint, and `retry` resumes at chunk 3
  without re-applying chunks 1–2 (`tellma.excel.import.resumed`); `Atomic` runs one transaction.
- The round-trip table of §1.4 asserted through `DataAccessScope` for each operation.
- One manual milestone check on Windows desktop Excel: `[$-170401]` renders Um Al Qura dates; the
  hidden manifest survives a save from Excel, LibreOffice and Google Sheets.

## 16. Definition of done

- **Projects**: `Tellma.Core.Abstractions` (namespace `.Excel`),
  `Tellma.Core` (namespace `Tellma.Core.Excel`), the pure and integration test folders — each
  project with a README stating purpose and usage, XML docs on every public member, building and
  testing on Windows and Linux under the repository's warnings-as-errors gates, wired into
  `Tellma.slnx`; `DocumentFormat.OpenXml` 3.5.1 pinned in central package management and referenced
  by `Tellma.Core` alone.
- **Behavior**: the operations and their projection (§2), cell encoding, decoding and number
  formats (§3), the display shape (§4), the editable shape and manifest (§5), natural-key usage
  (§6), intake, limits and parsing (§7), mapping (§8), the import semantics (§9), resolution
  (§10), the save, concurrency and error translation (§11), the entities, handlers, checkpoint and
  completion (§12), the security rules (§13), and the options (§14) — implemented and pinned by the
  suites of §15, green in CI.
- **Observability**: every instrument of §14.3 emitted and asserted at least once by the
  integration suite; the log events of §14.3 asserted through the test logger.
- **CI**: the pure suite on every PR on both operating systems; the integration suite on PR
  against LocalDB and nightly against Testcontainers; the corpus checked in under
  `test/core/Tellma.Core.Tests/Excel/Corpus/`.
- **Docs**: ARCHITECTURE.md updated where this spec touches it — the library-architecture package
  rows (`Tellma.Core` carries the Excel codec and the `DocumentFormat.OpenXml` reference; no
  separate Excel package), the reports-tier wording naming display-shape export as the
  within-stack export tier, the entity-contract row noting that `[NaturalKey]` and
  `[ExcludeFromExcel]` live in the entity contract rather than an Excel package, and the hosting
  row adding `Tellma:Excel` and `Tellma:ScratchPath` to the configuration sections. Public XML docs
  and error messages reference no `docs/` paths, per repo rule.
- **Not in scope of done**: the row source and `SaveAsync` (spec 0014), natural-key metadata and
  validation (spec 0011), labels and calendars (spec 0012), blob staging and endpoints (spec 0016),
  the worker, `IJobProgress` and `core.file-retention` (spec 0019), notification types and the
  inbox (spec 0020), endpoint projection and streaming (spec 0015), and every non-goal of the
  Goals section.

## Decisions record

The load-bearing decisions, where not already evident above:

1. **Open XML SDK 3.5.1 directly, forward-only** — the only MIT, Microsoft-maintained option that
   keeps memory flat both ways and can write validation lists, a hidden protected sheet, freeze
   panes and right-to-left views without a second library or a licence a distribution must hold
   (§1.1, §4.2, §7).
2. **The codec is a feature over the pipeline, never a dependency of it** — Open XML stays out of
   every module and the pipeline knows only `IExcelRowSource` and the stack-companion contribution
   (§1.1, §1.2).
3. **Four operations as two axes** — source (ids or clauses) × shape (display or editable) on one
   flat wire request each, mapped to one `ExportSource` case at the service seam so the row source
   never sees both; the empty-ids template falls out for free (§2.1).
4. **Fail at the cap, never truncate or promote; the background export and import are their own
   actions** — silent truncation is data loss the user cannot see, a mid-request switch to
   background would change the response shape, and a separate `/start` route gives each route one
   response shape and a static retry classification (§2.3, §4.2, §12.2).
5. **Encode by type, decode by mapped type, never by cell format** — homogeneous sortable columns,
   text fallbacks as the only lossless carrier for wide types, and no locale guesswork (§3.1).
6. **Per-column `[$-CCLLLL]` number formats; Ethiopian as text with the pattern in the manifest**
   — Excel renders Hijri and Um Al Qura natively and has no Ethiopian calendar (§3.2).
7. **`Id` and `Stamp` in the editable sheet, gated by the same-source manifest** — exact identity
   and change detection for the dominant export-fix-import loop, refused across tenants (§5.1,
   §5.4).
8. **One sheet per child collection keyed by the parent's natural key, a hidden protected manifest
   sheet** — rectangular tables sort and paste cleanly; a hidden sheet is the one manifest carrier
   every spreadsheet application round-trips (§5.2, §5.3).
9. **Natural keys are the entity contract's; references may use any target property on request**
   — uniqueness is a database guarantee, the surrogate fallback is visible, and ambiguity is an
   error rather than a first-match guess (§6).
10. **Staged-blob intake with package limits before parsing** — one intake path shared with
    attachments, a local file that can be read twice, and zip bombs stopped by the central
    directory (§7.1, §7.2).
11. **Manifest → header parse → labels in every tenant language → technical path, with a human
    override; unmapped columns are errors** — unchanged files map with zero input, renamed files
    still map, and the classic silent "column ignored" loss cannot happen (§8.1).
12. **Blank means null; a present child sheet means complete replacement; an absent one means
    untouched** — the save contract's own semantics, each statable in one sentence and never
    silently dropping data (§9.4, §9.5).
13. **Hydration through `GetByIdsAsync` and every hydrated row saved under `Check`** — a partial
    sheet touches only its columns, and a concurrent edit between hydration and persist is a
    conflict, never an overwrite (§9.3, §11.2).
14. **Temporary negative ids from the codec, not the allocator** — the pipeline already rewrites
    them in self-typed foreign keys and child parent keys, so in-sheet parents need no
    reservation round trip (§9.6, §10.4).
15. **Lookups as Queryex queries with a `KeySetRestriction` under the target's read filter** —
    row-level security composes only through `CompileQuery`; distinct-value TVPs make the cost
    O(distinct values) (§10.1).
16. **Chunked background commits with the checkpoint inside the chunk's transaction** — bounded
    transactions, honest committed-range reporting, and a retry that can never re-apply a chunk
    (§12.4, §12.5).
17. **`Export`/`Import` are server-owned entities on `Query | Details | Delete | Enlist` stacks with
    a self-scope criterion, saved only by enlisted saves** — "My exports" is a standard query, the
    row owns the blob, retention is an ordinary delete, and the request path and both handlers write
    through the entities' own pipelines as participants of the frame that authorises them, so a
    handler's completion row commits with the job outcome and its blob confirms inside the
    completion transaction (§12.1–§12.6).
18. **`core.import` with `MaxAttempts = 1`, resumed only by an explicit `retry`** — a partially
    committed import never re-runs blindly (§12.4).
19. **No `Export` securable** — bulk extraction is `Read`; a separate action is one registry line
    away when wanted (§13, review flag 10).
20. **Typed job arguments, polymorphic on the export kind** — a schedule-fired `core.export` has no
    `Export` row to read, so its arguments alone name the shape and carry the typed request; on
    the request path both services build the arguments from the row's `RequestJson` (§12.1–§12.3).

## Review flags

1. **Library: Open XML SDK directly** (§1.1; decision 1) versus MiniExcel 1.46 for the streaming
   core plus a small Open XML post-pass for the manifest sheet, validation lists and protection —
   ships faster with a second library and a two-pass write. Flips if the writer's first milestone
   overruns or Open XML SDK cannot write to the spooled stream shape.
2. **Synchronous export cap 100,000 rows** (§4.2) versus 50,000 for Azure SQL elastic pools.
   Flips on `tellma.excel.export.duration` at the 95th percentile exceeding the long request
   timeout on the smallest pool.
3. **`long` keys as text in the editable shape** (§3.1) — lossless but unlike numeric ids — versus
   number below 2^53 and text above (a heterogeneous column). Flips on user feedback that text ids
   break their sorting.
4. **`Id` and `Stamp` in the editable sheet** (§5.1; decision 7) versus a natural-keys-only file
   that loses bulk edits of the key columns themselves and change detection. Flips if the
   same-source gate proves confusing (sandbox → live refusals as support load).
5. **Columns in an unconfigured language ignored with a warning** (§8.2) versus a hard
   `UnmappedColumn` error forcing the user to remove the column. Flips if silent-warning imports
   generate "where did my Arabic names go" tickets.
6. **Child sheet present ⇒ complete replacement of the listed parents' children** (§9.5; decision
   12) — the sharpest edge in the design — versus a per-row `Action` column (`Keep | Delete`) on
   child sheets. Flips on the first accidental mass deletion of children through a partial child
   sheet.
7. **References resolved under the target's read filter, and the same rule applied to raw
   foreign-key ids on the JSON path** (§10.1, §13) versus Excel-only strictness with JSON saves
   posting any id. Flips if a legitimate workflow needs to reference rows the caller may not read
   (a write-only lookup), which would call for a `Reference` action rather than reverting.
8. **Staging-only intake** (§7.1) — one extra round trip for tiny synchronous imports — versus a
   multipart convenience endpoint. Flips if agent clients (MCP `tellma_import`) find two calls
   burdensome; the codec does not change either way.
9. **Chunked background commits, 10,000-row chunks, checkpoint through `IJobProgress.Append`**
   (§12.4, §12.5; decision 16) versus `Atomic` by default with a hard cap and no chunking, or
   5,000-row chunks under the lock-escalation threshold, or a dedicated checkpoint column on the
   `Imports` row instead of `Jobs.StateJson`. Flips on lock-escalation contention measured in the
   integration suite at 10,000 rows, or on operators needing the checkpoint visible on the import
   row itself.
10. **No `Export` securable action** (§13; decision 19) versus an `Export` action in the securables
    registry so administrators can forbid bulk extraction to users who may read on screen. Flips
    on the first customer asking to restrict exports; the change is one line in the stack feature's
    securable contributor plus `Action = "Export"` on four `[ApiAction]`s.
11. **Errors workbook deferred** (Non-goals) — the upload echoed with an error column is the most
    usable channel for thousands of errors and fits the coordinate map. Flips when `TotalErrors`
    routinely exceeds `MaxReportedErrors` in telemetry.
12. **By-query editable export in pages, children fetched per page of parent ids** (§5.2) versus
    one round trip by re-anchoring the parent filter through the child's parent navigation
    (`FilterTree.Via`, spec 0011 §11.2). Flips when a measured export shows the per-page child
    fetch dominating.
13. **One read round trip per lookup** (§1.4, §10.1) — `IExcelRowSource.Rows` takes one query —
    versus a `Rows(list<ExcelQuery>)` overload batching every lookup into one round trip with
    `NextResult()`. Flips on `tellma.excel.import.lookups` showing wide reference fans (more than
    three lookups per import is common).
14. **The `Exports` row owns the export blob and one daily `core.file-retention` schedule deletes
    expired `Exports` and `Imports`** (§12.1) versus the notification owning the artifact with its
    own retention. Flips if "My exports" is not wanted as a page.
15. **`core.import` at `MaxAttempts = 1` with resume through the administrator's `retry`** (§12.4;
    decision 18) versus `MaxAttempts = 3` with automatic resume from the checkpoint. Flips if
    transient failures (a lost lease on a busy instance) leave too many imports waiting on an
    administrator; the checkpoint already makes automatic resume safe.

## Appendix A — Error codes

All codes are constants on `ExcelErrorCodes`; coordinates are as listed (`—` = null).

| Code | Sheet / Row / Column | Arguments | Condition |
|---|---|---|---|
| `Excel.Export.RowLimitExceeded` | — / — / — | `limit` (this code), `actual` (rows), `maximum` (the cap) | a sheet's query yields more than its cap (§2.3, §4.2); raised as `LimitExceededException(Limit = this code, Actual, Maximum)` (413) and the job error code of an overflowing background export (§12.3) |
| `Excel.Import.TooLargeForSynchronous` | — / — / — | `rows`, `maxRows`, `bytes`, `maxBytes` | a synchronous or atomic request above its limits (§2.3) |
| `Excel.Import.PackageTooLarge` | — / — / — | `bytes`, `max` | §7.2 |
| `Excel.Import.MalformedWorkbook` | sheet? / — / — | `reason` | package structure, XML, or a manifest naming another entity (§7.2, §8.1) |
| `Excel.Import.UnmappedColumn` | sheet / 1 / column | `header`, `suggestions` | no mapping and `IgnoreUnmappedColumns = false` (§8.1) |
| `Excel.Import.DuplicateColumn` | sheet / 1 / column | `path` | two columns map to one path (§8.1) |
| `Excel.Import.ServerOwnedColumn` | sheet / 1 / column | `path` | a mapped server-owned, derived, database-computed or excluded path (§8.1) |
| `Excel.Import.UnconfiguredLanguageColumn` | sheet / 1 / column | `language` | warning; the column is ignored (§8.2) |
| `Excel.Import.WriteOnceChanged` | sheet / row / column | `path` | a write-once value differs from the hydrated one on update (§9.3) |
| `Excel.Import.RowNotFound` | sheet / row / key column | `key`, `value` | `Update` row key resolves to nothing readable (§9.1); also a missing-row concurrency conflict (§11.2) |
| `Excel.Import.DuplicateRowKey` | sheet / row / key column | `value` | duplicate row-key or unique-column value in the file (§9.2) |
| `Excel.Import.IdKeyRequiresSameTenant` | — / — / — | `distribution`, `sourceTenantId` | `RowKey = "Id"` on a non-same-source file (§9.2) |
| `Excel.Import.SurrogateReferenceFromOtherTenant` | sheet / — / column | `entity` | a `KeyKind = Surrogate` column on a non-same-source file (§6.3) |
| `Excel.Import.ReferenceNotFound` | sheet / row / column | `entity`, `key`, `value`, `collation`? | no readable target row (§10.3) |
| `Excel.Import.ReferenceAmbiguous` | sheet / row / column | `entity`, `key`, `value`, `count` | more than one target row (§10.3) |
| `Excel.Import.KeyTooLong` | sheet / row / column | `max` | a key value above 450 characters (§10.1) |
| `Excel.Import.OrphanChildRow` | child sheet / row / parent column | `value` | a child row whose parent is absent from the parent sheet (§9.5) |
| `Excel.Import.ParentCycle` | sheet / row / parent column | `rows` | a cycle among in-sheet parents (§9.6) |
| `Excel.Import.TooManyChildren` | child sheet / row / parent column | `collection`, `max`, `count` | more rows for one parent in a collection than its `MaxCount`, at the first row beyond the cap (§8.1) |
| `Excel.Import.InvalidCell` | sheet / row / column | `expected`, `value` | any decode failure (§3.1) |
| `Excel.Import.ConcurrencyConflict` | sheet / row / `Stamp` column or — | `modifiedAt`, `modifiedBy` | the row changed since hydration or since the sheet's stamp (§11.2) |
| `Excel.Import.Failed` | — / — / — | — | the `JobError.Code` of a failed background import (§12.6); `Imports.ErrorCount` carries the total |

Pipeline codes translated with coordinates keep their own names (`Required`, `MaxLength`,
`Unique`, `Fk.NotFound`, `Fk.InUse`, `Tree.Cycle`, `Tree.TooDeep`, `WriteOnce`, `Precision`,
`Import.LanguageNotConfigured`, `Entity.NotFound`, and every distribution validator code).
