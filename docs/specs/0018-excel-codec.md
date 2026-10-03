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
the entity as rectangular tables that round-trip: every editable property, every child collection
at every depth on its own sheet with each row linked to its owner row by that row's `Id`, every
foreign key expressed as a natural key of the referenced entity rather than a surrogate id, and a
hidden manifest that records how the file was written so that the file can be re-imported
unchanged into the same tenant, another tenant of the same distribution, or a distribution that
shares the entity schema. This spec ships the codec that writes both shapes and reads the editable
one back, the four operations that expose them on every eligible stack, the natural-key
conventions references depend on, and the hand-off of large files to background work.

The codec is a pure component: it plans workbooks from `EntityMetadata` (spec 0011's
`EntityMetadata`), encodes cells by property type, decodes cells by the mapped property's type,
and never opens a database connection. Rows reach it through spec 0014's `IExcelRowSource` and
leave it as entity instances handed to spec 0014's `SaveAsync` with `SaveSource = Import`; row
security, validation, concurrency, tree maintenance, and version-tag bumps are the pipeline's, not
the codec's. The codec adds what a spreadsheet user needs and the pipeline cannot know: sheet, row
and column coordinates on every error, language-aware column mapping, bulk translation of natural
keys into surrogate ids under the caller's read filter, links between sheets by `Id`, and in-chunk
binding of self-references to rows that are new in the same file.

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
import commits in chunks and checkpoints its position, the first uncommitted parent row, inside
each chunk's transaction, so a re-leased job resumes there and never re-applies or skips a row.
Notifications (`core.export.ready`, `core.import.completed`, `core.import.failed`) are spec 0020's.

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
- Ship the import semantics: modes `Insert | Update | Upsert`, row identity by `Id` (the default on
  a same-source file) or by natural key, hydration of partial sheets, blank-cell rules, child
  sheets at every depth linked to their owners by `Id`, bulk natural-to-surrogate resolution under
  the target's read filter, self-references (tree parents and any other) bound within a chunk with
  cycle detection, coordinates on every error, all-or-nothing synchronous imports.
- Ship the `Export` and `Import` entities over `core.Exports`/`core.Imports`, the `core.export`
  and `core.import` job handlers, chunked background import resumable from a position checkpoint,
  and the synchronous thresholds that decide between the two paths.
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
| Contracts | `src/core/Tellma.Core.Abstractions/`, namespace `Tellma.Core.Abstractions.Excel` | Requests, plans, outcomes, `ImportError`, `ImportMode`, `ExportKind`, `ExcelQuery`, `ExcelPage`, `IExcelRowSource`, `Export`/`Import` entities, `ExcelOptions`, `ExcelErrorCodes`, `ExcelTelemetryNames`, `ImportException`; `[ExcludeFromExcel]` lives in `Tellma.Core.Abstractions.Entities` (spec 0011). |
| Codec and operations | `src/core/Tellma.Core/`, namespace `Tellma.Core.Excel` (folder `Excel/`) | `ExcelOperations<TEntity>`, `IExcelExporter`/`IExcelImporter` and their implementations, `ExcelImportSession<TEntity>`, `ImportCheckpointState`, `ExportService`, `ImportService`, `ExportAccessCriteria`, `ImportAccessCriteria`, the two job handlers. Internal: `WorkbookWriter`, `WorkbookReader`, `SharedStringStore`, `NumberFormatBuilder`, `CellCodec`, `ExportPlanner`, `HeaderIndex`, `ImportMapper`, `ImportResolver`. |
| Package pin | `Directory.Packages.props` | `DocumentFormat.OpenXml` 3.5.1 (MIT), referenced by `Tellma.Core` only. |
| Unit tests | `test/core/Tellma.Core.Tests/Excel/` | Pure: no database, no network; the workbook corpus under `Excel/Corpus/`. |
| Integration tests | `test/core/Tellma.Core.IntegrationTests/Excel/` | spec 0011's fixture entities (`test/shared/Tellma.Testing.Entities`); `Category=Integration`. |

Dependency edges: `Tellma.Core.Abstractions` references `Tellma.Core.Queryex` and nothing else;
`Tellma.Core` references `Tellma.Core.Abstractions`, EF Core, SqlClient, `DocumentFormat.OpenXml`
and the other runtime pins named by spec 0010. The codec depends on the pipeline: it calls
`SaveAsync`, `GetByIdsAsync`, `ExcelRowSource`, `IAccessEvaluator`, `IBlobService` and `IJobQueue`,
and `ExcelOperations` enlists the `Import` rows it writes and the handlers the `Export` and `Import`
rows with the `IOpenWriteHost` of the frame each runs in, which persists them (spec 0014 §13.3).
The pipeline never depends on the codec — it knows only `IExcelRowSource` and the stack-companion
contribution. No module and no distribution references Open XML.

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

The stack feature's securable contributor registers each `[ApiAction]` pair (`<Resource>`, `Read` /
`Save`, `FilterRoot = Resource`); the pairs already exist for every stack, so Excel adds **no
securable** (no `Export` action; review flag 10). The blob kinds `export-file` and `import-file` are
spec 0016's; the codec names them.

Startup checks reported into the realised gate (spec 0010): a warning for every
`[ExcludeFromExcel]` on a server-owned property (redundant); the scratch directory exists and is
writable. Natural-key checks are spec 0011 §3.2's startup validation (§6.3).

### 1.3 Vocabulary

"Display shape" and "editable shape" (`ExportKind = Display | ForImport`); `Upsert`, never
"merge"; **parent sheet** for the sheet of the stack's own entity; **child sheet** for the sheet of
a child collection at any depth; **owner sheet** for the sheet of the rows a child sheet's rows
belong to (the parent sheet for a first-level collection, the child sheet one level up for a
grandchild collection); **collection path** for the dotted `ChildCollectionMetadata.Property` names
that lead from the stack's entity to a collection (`RoleMemberships`, `WidgetParts.WidgetPartNotes`;
§8.3); **link** for a child row's reference to its owner row by the owner row's `Id` within the
workbook (§9.5); **row key** for the column that identifies a parent row in `Update`/`Upsert`;
**reference key** for the target property a reference column is expressed in; **manifest** for the
hidden `_tellma` sheet; **stamp** for the concurrency-token column, whose header is `Stamp` and
whose value is `ModifiedAt`'s wire string exactly as spec 0015 encodes `DateTimeOffset` (ISO 8601,
`yyyy-MM-ddTHH:mm:ss.fffffffzzz`: seven fractional digits and the offset, `+00:00` for a stamp);
**same-source** for a file whose manifest names the running deployment and the target tenant
(§5.4); **lookup** for one resolution query per (target entity, key path); **chunk** for the unit
of commit, a run of parent rows with every descendant row (§12.4).

### 1.4 The two paths and their round trips

Every operation runs on the caller's behalf inside a tenant scope; the connect prologue rides the
first business round trip (spec 0013's guarded prologue), so no operation pays a connect call of
its own. Round trips per operation, warm caches:

| Operation | Round trips | What each carries |
|---|---|---|
| `export`, display shape, by query or by ids | `⌈rows / MaxTake⌉` reads (1 for `≤ MaxTake` rows) | the grid query, pages of `StackLimits.MaxTake` through spec 0014's row source, the first page of a by-query export carrying the capped count (§4.2); ids as a `KeySetRestriction` |
| `export-for-import`, by ids or by query | `⌈parents / MaxTake⌉` reads | each page carries the parent rows and every selected collection at every depth, restricted by the page's captured keys (§5.2); the first page of a by-query export carries the capped parent count; a collection with more than `MaxTake` rows under one page adds follow-up reads |
| `inspect-import` | 1 | the staged blob's `IBlobService.ResolveAsync` read (spec 0016's `Read` batch), the prologue riding it; the securable check and the plan computed from the file cost no further round trip |
| `import`, `Insert`, no reference columns | `1 + 2` | the staged blob's `ResolveAsync` read (as for `inspect-import`); RT1 validation context (ids assigned, before images none); persist |
| `import`, any mode with references, or `Update`/`Upsert` | `1 + L + H + 2` | the blob read; `L = 1` read for every lookup together (§10.1), 0 when there is none — a same-source `Id` row key with no reference column has none; `H = 1` hydration through `GetByIdsAsync` (0 for `Insert`); validation and persist; each concurrency retry of §11.2 adds 3 |
| `export/start`, `export-for-import/start` | 1 | the job enqueue through `IJobQueue.EnqueueAsync` (§12.2) |
| `import/start` | 2 | a validation round in which spec 0016's validator loads the staged upload's blob row, then the enlisted insert of the `Import` row and the job enqueue in one persist batch (§12.2) |
| background export (`core.export`) | `⌈rows / MaxTake⌉ + 1` | the pages as above in the job scope; one validation round for the enlisted insert of the `Export` row (§12.3), whose persist rides the partition's completion batch, atomic with the job outcome |
| background import (`core.import`), `N` chunks | `1 + N × (L + H + 2) + 1` | the blob read once in the job scope, then the same per chunk, `L` and `H` per chunk, the checkpoint riding the chunk's persist transaction (§12.5); one validation round for the enlisted update of the `Import` row (§12.6), whose persist rides the completion batch |

No lock or connection is held across file I/O: an export streams the parent sheet into the package
and spools each child sheet to its own temp file, releasing the connection before the first byte
reaches the response or the blob store; an import parses the whole file before its first database
call, and the persist transaction opens inside the pipeline after every file read has completed.

## 2. The four operations

### 2.1 Contract

```csharp
// Tellma.Core.Abstractions.Excel
public enum ImportMode { Insert, Update, Upsert }
public enum ExportKind { Display, ForImport }

public class ExportSourceRequest                            // Ids or the clauses (each optional), never both
{
    public IReadOnlyList<long>? Ids { get; set; }
    public string? Filter { get; set; }
    public string? OrderBy { get; set; }
    public string? Search { get; set; }
    public IReadOnlyDictionary<string, JsonElement>? Arguments { get; set; }
    public bool IncludeInactive { get; set; } = false;      // as the grid's query: lifts the activatable conjunct
}

public abstract record ExportSource                         // the service seam's source; one case per request
{
    public sealed record ByIds(IReadOnlyList<long> Ids, string? OrderBy) : ExportSource;   // rows in OrderBy order, else by Id
    public sealed record ByQuery(
        string? Filter, string? Search, string? OrderBy, IReadOnlyDictionary<string, JsonElement>? Arguments,
        bool IncludeInactive)
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
    public IReadOnlyList<string>? Columns { get; set; }            // subset of the parent's editable column paths; all when null
    // FK property -> target property (CenterId -> Code)
    public IReadOnlyDictionary<string, string> ReferenceKeys { get; set; }
    public IReadOnlyList<ExportCollection>? Children { get; set; } // every collection at every depth when null; none when empty
}

// one collection of the parent or of an enclosing collection; members as the request's, for its entity
public sealed record ExportCollection(
    string Collection, IReadOnlyList<string>? Columns, IReadOnlyDictionary<string, string>? ReferenceKeys,
    IReadOnlyList<ExportCollection>? Children);

public sealed record ExportOutcome(Stream Workbook, string FileName, int Rows);

// Column = the column letter (A, AB) as ImportPlanColumn.Column reports it; Path per §8.3; null = ignore
public sealed record ImportColumnMapping(string Sheet, string Column, string? Path);
// Collection: "" = the parent sheet; a collection path (§8.3) = that collection's sheet; null = ignore
public sealed record ImportSheetMapping(string Sheet, string? Collection);

public sealed class InspectImportRequest
{
    public int FileId { get; set; }             // required; a Staged blob of kind import-file
    public IReadOnlyList<ImportSheetMapping> SheetMappings { get; set; } = [];
    public IReadOnlyList<ImportColumnMapping> ColumnMappings { get; set; } = [];
}

public class ImportRequest
{
    public int FileId { get; set; }             // required
    public ImportMode Mode { get; set; } = ImportMode.Insert;
    // natural-key path or "Id"; required for Update/Upsert unless a default resolves (§9.2)
    public string? RowKey { get; set; }
    public IReadOnlyList<ImportSheetMapping> SheetMappings { get; set; } = [];
    public IReadOnlyList<ImportColumnMapping> ColumnMappings { get; set; } = [];
    public bool IgnoreUnmappedColumns { get; set; } = false;
    public bool IgnoreSheetStamps { get; set; } = false;
}

public sealed class StartImportRequest : ImportRequest
{
    public bool Atomic { get; set; } = false;   // one transaction for the whole file; ceilings in pass 1 (§12.4)
}

public sealed record ImportPlan(
    string? Entity, int ManifestVersion, bool ManifestPresent, bool SameSource, bool SchemaFingerprintMatches,
    IReadOnlyList<ImportPlanSheet> Sheets, IReadOnlyList<string> RowKeyCandidates, string? DefaultRowKey,
    long TotalRows, bool RequiresBackground, bool AtomicAllowed, IReadOnlyList<ImportError> Warnings);

public sealed record ImportPlanSheet(
    string Sheet, string? Collection, ImportSheetStatus Status, long Rows,
    IReadOnlyList<ImportPlanColumn> Columns);

public sealed record ImportPlanColumn(
    string Column, string Header, string? Path, ImportColumnStatus Status, string? Language,
    IReadOnlyList<string> Suggestions);

public enum ImportSheetStatus { Parent, Child, Manifest, Ignored, Duplicate }
public enum ImportColumnStatus { Mapped, Unmapped, Ignored, Duplicate, ServerOwned }

// CodedError: spec 0014 §7.3
public sealed record ImportError(
    string? Sheet, int? Row, string? Column, string? Header, string? Property,
    string Code, IReadOnlyDictionary<string, object?> Arguments)
    : CodedError(Code, Arguments);

public sealed record ImportOutcome(
    int Inserted, int Updated, int ChildrenInserted, int ChildrenUpdated, int ChildrenDeleted,
    IReadOnlyList<(int From, int To)> CommittedRowRanges, IReadOnlyList<ImportError> Warnings);

// 422 validation with coordinates; a member of spec 0014 §14.1's closed set; CommittedRowRanges empty for a synchronous import
public sealed class ImportException(
    IReadOnlyList<ImportError> Errors, int TotalErrors, IReadOnlyList<(int From, int To)> CommittedRowRanges)
    : TellmaException;

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
    public Task<JobAccepted> StartImportAsync(StartImportRequest request);
}
```

| Member | Meaning |
|---|---|
| `ExportSourceRequest.Ids` | An `export` or `export-for-import` request carries `Ids` or the clauses (`Filter`, `OrderBy`, `Search`, `Arguments`, `IncludeInactive`), and `ExcelOperations` maps it to exactly one `ExportSource` case: `Ids` non-null to `ByIds` with `OrderBy` beside it; otherwise the clauses to `ByQuery`, each optional — with none, only the access filter and the activatable conjunct below apply and rows are in `Id` order (spec 0014 §15), the unfiltered, unsorted grid. `Ids` together with `Filter`, `Search`, `Arguments` or `IncludeInactive = true` is `BadRequestException`. With `Ids`: at most `StackLimits.MaxIds`; an empty list is valid for `ExportForImport` and yields the entity's template (headers, validation lists, manifest, no rows) and `BadRequestException` for `Export`. The clauses are the wire `QueryRequest`'s, interpreted by spec 0014's row source exactly as `query` interprets them (`Search` through `SearchFilter`, the access filter conjoined, and the activatable conjunct unless `IncludeInactive`, as the next row says). |
| `ExportSourceRequest.IncludeInactive` | As the grid's `query`, for both shapes: `false` (default) joins the activatable conjunct on an activatable stack, so an export by query carries the rows the grid shows; `true` lifts it. `ExcelOperations` passes it to `ByQuery`; a `ByIds` source applies no activatable conjunct, the ids naming the rows (spec 0014 §15). |
| `ExportSource` | The source `EntityService.ExcelRowSource` takes (spec 0014 §15 composes `ByIds` as a `KeySetRestriction`, `ByQuery` as the clauses, `All` as the access filter alone). `ByIds` and `ByQuery` come from a request; `All` never does: it is the importer's lookup source (§10.1). |
| `ExportRequest.Select` | The grid's select, verbatim; every item becomes one column. `Headers[i]` (each ≤ 255 characters) names column `i`; a null list derives headers (§4.1). |
| `ExportForImportRequest.Columns` | Property paths of the parent entity restricted to a subset; `Id`, `Stamp` and the default natural key column are always kept; a path that is not editable is `BadRequestException`. |
| `ExportForImportRequest.ReferenceKeys` | Per foreign-key property, the target property to express it in; overrides the target's default natural key; any scalar property of the target is allowed (a non-unique one makes import ambiguity possible, §10.3). |
| `ExportForImportRequest.Children` | The child collections that get a sheet (§5.2): null selects every collection at every depth, `[]` none, a list that subset; a collection left out has no sheet, so a re-import leaves it untouched (§9.5). |
| `ExportCollection` | One selected collection. `Collection` is a `ChildCollectionMetadata.Property` of its owner — `RoleMemberships`; under `WidgetParts`, `WidgetPartNotes` — and an unknown name or one repeated among siblings is `BadRequestException`. `Columns` and `ReferenceKeys` follow the request's rules for the collection's entity, a path that is not editable being `BadRequestException`; the parent reference, `Id` and the entity's default natural key are always kept. `Children` follows the same null/empty rule one level down. |
| `ExportOutcome` | The synchronous result: `Workbook` is a readable, seekable stream over the spooled temp file (deleted on dispose), `FileName` per §4.3, `Rows` the data rows written. |
| `ImportColumnMapping.Column` | The column letter (`A`..`XFD`, case-insensitive), as `ImportPlanColumn.Column` reports it; anything else is `BadRequestException` before the blob is read. Header text never addresses a column: a header may repeat, be blank, or be a valid letter itself (`Id`). |
| `StartExportAsync`, `StartExportForImportAsync`, `StartImportAsync` | The background path of the two exports and the import. The exports take their synchronous request, enqueue one `core.export` job and return spec 0015's `JobAccepted(JobId, null)`: the handler inserts the `Export` row when it has a file for the row to own (§12.3). `StartImportAsync` takes `StartImportRequest`, the `ImportRequest` plus `Atomic`, inserts the `Import` row and enqueues its job in one persist batch, and returns `JobAccepted(JobId, ImportId)` (§12.2). The web layer answers each with 202. |
| `ImportRequest.RowKey` | A member of the parent entity's `EntityMetadata.NaturalKeys`, or `"Id"`, which only a same-source file accepts (§9.2); any other value is `BadRequestException` before the blob is read. Null takes the default of §9.2 (`Id` on a same-source file). |
| `ImportRequest.IgnoreSheetStamps` | `false` (default): on a same-source file with a mapped `Stamp` column, a non-blank cell is the row's expected stamp, so a row edited since the export conflicts; `true`: the import runs as if the sheet carried no stamps — the `ModifiedAt` read at hydration is every row's expected stamp (§9.3, §11.2). Every import saves under `Check`. |
| `StartImportRequest.Atomic` | The whole file in one transaction. Its ceilings are checked in the handler's pass 1 (§12.4), since the `/start` path reads no file; a breach is `Excel.Import.TooLargeForAtomic` in the job's outcome. |
| `ImportPlan` | The mapping result before any row is decoded beyond the header row and the `dimension` element. A sheet's `Collection` is its collection path (§8.3, `""` for the parent sheet), `Duplicate` marks two sheets bound to one target (§8.1), and each column's `Path` is its resolved path (§8.3). `RequiresBackground` is true when the file is above any synchronous limit of §2.3, so only `import/start` can run it; `AtomicAllowed` is true when `TotalRows ≤ MaxAtomicImportRows`, the parent rows are within the parent ceiling (§2.3) and `TotalRows ≤ MaxRowsPerSave`, so a client offers `Atomic` only for a file that qualifies. |
| `ImportOutcome` | The result of `import` and, serialized, `Imports.ResultJson` of a completed background import (§12.6): the counts, the final checkpoint state's for a background import (whole-file across attempts), each `Children*` count summed over every depth; `ChildrenDeleted` counts the existing children the import dropped (§9.5), never the descendants deleted with a dropped child (spec 0011 §8.3); `CommittedRowRanges` lists the Excel rows (1-based, header excluded) of every parent row the import committed, across all its attempts, as maximal runs of consecutive rows (a fully blank row never breaks a run) — one run for a synchronous import; `Warnings` are `Excel.Import.UnconfiguredLanguageColumn` and `Excel.Import.IdentityColumnIgnored` and never errors. |
| `ImportException` | Thrown by `import` for any error in a synchronous import but the pipeline's `ForbiddenException`, which passes through as 403 (§13), and by `inspect-import` for a mapping that names a sheet, column or path the workbook or entity lacks (§8.1); `Errors` is capped at `ExcelOptions.MaxReportedErrors` and `TotalErrors` says how many exist; `CommittedRowRanges` is empty for a synchronous import and, in a failed background import's `ResultJson`, the checkpoint's runs (§12.6); spec 0015 §7.1 maps it to 422 with `sheet`, `row`, `column`, `header` and `property` on each item and `totalErrors` and `committedRowRanges` beside them. |

### 2.2 Projection and securables

Spec 0014's realizer projects the companion's seven methods as `[ApiAction]`s on every eligible
stack (§1.2); spec 0015 projects them to `POST /{tenantId}/api/web/{resource-segment}/{method}`,
where `{method}` is `export`, `export/start`, `export-for-import`, `export-for-import/start`,
`inspect-import`, `import` or `import/start`. The synchronous exports, `inspect-import` and
`import` run under the long request timeout and the per-user export/import concurrency limits of
`TellmaApiOptions`; the two export `/start` methods only enqueue a job, `import/start` only inserts
the `Import` row and enqueues its job (§12.2), and the three run under the ordinary web timeout.
Each method exists when its operation is declared — `export` and `export/start` under `Export`, the
other five under `Import` — and the securable of each (`Read` for the four export methods, `Save`
for the three import methods) is evaluated by spec 0014's `IApiActionInvoker` before the method
runs, so the methods hold no securable check of their own. The read decision's filter is applied
by the row source; the save decision is enforced by the pipeline's two-stage pre-check and
post-check on the rows the import hands it (§13).

### 2.3 Synchronous or background

The **parent ceiling** of a save is the smaller of the stack's `MaxSaveCount` and `MaxIds`:
hydration reads every updated parent of a save in one `GetByIdsAsync` (§9.3), which refuses more
than `MaxIds` ids.

| Operation | Synchronous when | Otherwise |
|---|---|---|
| `Export` | the query returns `≤ MaxSynchronousExportRows` rows | above the cap: `LimitExceededException("Excel.Export.RowLimitExceeded", rows, cap)` (413) — the export **fails**, never truncates; the message names `export/start`, which runs the same request in the background |
| `ExportForImport` | the same, counting parent rows; each sheet is additionally capped by `MaxExportRows` | the same; the message names `export-for-import/start` |
| `export/start`, `export-for-import/start` | never | the job fails a sheet above `MaxExportRows` (§4.2, §12.3) |
| `Import` | `TotalRows ≤ MaxSynchronousImportRows` (summed over every mapped sheet), the file fits one save (parent rows within the parent ceiling, `TotalRows ≤ MaxRowsPerSave`), the file `≤ MaxSynchronousImportFileBytes` and its parts' uncompressed sum (§7.2) `≤ MaxSynchronousImportUncompressedBytes` | above any limit: `ImportException` with one error `Excel.Import.TooLargeForSynchronous` (arguments `rows`, `maxRows` — the row limit crossed — and `bytes`, `maxBytes` — the byte limit crossed, compressed or uncompressed), whose message names `import/start`, which runs the same request in the background |
| `import/start` | never | `Atomic = true` above `MaxAtomicImportRows`, parent rows above the parent ceiling or above the stack's `MaxRowsPerSave` fails the job in pass 1 with `Excel.Import.TooLargeForAtomic` (§12.4) |

An export `/start` request enqueues its job alone; `import/start` inserts the `Import` row (§12.1)
and enqueues its job in the same persist batch (§12.2). The result is `JobAccepted`, carrying the
job id and, for an import, the row id (§2.1). A synchronous export or import never promotes itself
to background: the response shape would change mid-request. In either mode an owner row with more
child rows than its collection's `MaxCount`, or a parent row whose rows at every depth exceed the
stack's `MaxRowsPerSave`, fails as the child sheets are read (`Excel.Import.TooManyChildren`,
`Excel.Import.TooManyDescendants`, §8.1); no background chunk can hold it.

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
by Excel), and made unique with ` (2)`, ` (3)` suffixes; a child sheet's name is composed per §5.2.
Headers come from `ILabelProvider` in the request culture: `EntityLabel(entityType, plural: true)`
(spec 0012's `<Schema>_<Entity>_Plural` key, `Gl_Center_Plural`, which falls back to the singular
label) for sheet names, `PropertyLabel` for columns (twins carry ` (E)` / ` (ع)`), `PropertyLabel`
of the navigation and of the key joined by ` / ` for reference columns (`Center / Code`,
`Parent / Code`, `Role / Name (E)`). Headers that coincide within a sheet — two properties sharing
a label — get the technical path in parentheses (`Date (PostingDate)`, `Date (DueDate)`). Labels
and the header index built from them (§8.1) are cached per (entity, culture, settings tag); the
connect prologue reads the settings tag before the operation runs, so neither is older than the
request.

The header row is bold, frozen (`pane ySplit="1"`), and filterable (`autoFilter` over the used
range); column widths derive from the header length and the type (dates 12, numbers 14, text
`min(60, max(12, MaxLength / 2))`); a right-to-left sheet view (`sheetView rightToLeft="1"`) when
the request culture is right-to-left.

## 4. Display-shape export

### 4.1 The plan

`Export` runs exactly the query `query` would run for the same request — the same `Select`,
`Filter`, `OrderBy`, `Search`, `Arguments`, `IncludeInactive`, the same access composition,
`Skip = 0`, no ancestors — through `EntityService.ExcelRowSource(source)`, `source` the request's
`ExportSource` case (§2.1), with an `ExcelQuery` of `RootEntity = Descriptor.Resource`, the select
items as `SelectPaths`, `OrderBy = request.OrderBy`, no `Restriction`, no `Dependents` and
`Take = cap + 1`, the cap being `MaxSynchronousExportRows` (background: `MaxExportRows`).
A by-query export also sets `CountCap = cap`, so the first page's `Count` decides the cap before
any row is spooled (§4.2); a by-ids export sets no `CountCap`, since it holds at most `MaxIds`
rows. The export writes one data sheet plus the manifest sheet (`Shape = Display`):

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
    IReadOnlyList<object>? RestrictionValues, int? Take, int? CountCap, IReadOnlyList<ExcelQuery>? Dependents);

public sealed record ExcelPage(
    IReadOnlyList<IReadOnlyList<object?>> Rows, IReadOnlyList<ExcelPage> Dependents, int? Count);

// implemented by spec 0014 over IDataBatch.Rows with the caller's read filter
public interface IExcelRowSource
{
    IAsyncEnumerable<ExcelPage> Pages(ExcelQuery query);
    Task<IReadOnlyList<IReadOnlyList<IReadOnlyList<object?>>>> ReadAsync(IReadOnlyList<ExcelQuery> queries);
}

// Tellma.Core.Excel (runtime; the codec is pure — it never opens a connection)
public sealed record ExcelContext(
    int TenantId, string DeploymentId, DateTimeOffset Now, IReadOnlyList<string> TenantLanguages, CultureInfo Culture,
    ICalendarSystem Calendar, TimeZoneInfo TimeZone, ILabelProvider Labels, ILanguageCatalog Catalog);

public sealed record ExcelColumnSpec(
    string Header, QueryexType Type, int? Scale, IReadOnlyList<string>? Path);

public sealed record ExcelWorkbookPlan(
    EntityMetadata Entity, ExportKind Shape, string FileName, IReadOnlyList<ExcelSheetPlan> Sheets,
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
| `ExcelQuery.Dependents` | Child-collection queries (`RootEntity` a child entity) that ride every page of the query they belong to; the row source restricts each to the children of that page's rows through the child entity's `[ParentKey]` and the keys the page captured (spec 0011 §5.4), at any depth; a dependent's `Restriction` and `RestrictionValues` are null. |
| `ExcelQuery.CountCap` | The capped count the first page of a root query carries (spec 0011's `RowQueryOptions.CountCap`; `cap + 1` means more than the cap); null on dependents. |
| `ExcelPage` | One page of a root query: its `Rows` in `SelectPaths` order; `Dependents` parallel to the query's, each the complete rows of that collection under this page's rows with its own `Dependents`; `Count` on the first page of a query with `CountCap`, else null. |
| `Pages` | One Read round trip per page of `StackLimits.MaxTake` root rows, every dependent at every depth riding it; a dependent with more than `MaxTake` rows under one page is completed by follow-up reads before the page is yielded. |
| `ReadAsync` | Independent unpaged queries (the importer's lookups, §10.1) in one Read round trip; results parallel to the input; a result that reaches `MaxTake` rows is completed by follow-up reads. |
| `ExcelContext` | Built by `ExcelOperations` from `RequestContext`: `TenantId`, `DeploymentId = DeploymentIdentity.DeploymentId` (spec 0007 §1.6), `Now = RequestContext.Now` (spec 0010 §4.1; in the handlers, the job scope's), `TenantLanguages` from `TenantSettings.Languages` in slot order, `Culture = CultureInfo`, `Calendar = CalendarSystem`, `TimeZone` from the display zone, `Labels = ILabelProvider`, `Catalog = ILanguageCatalog` (spec 0012, for language symbols, §8.1). `Now` is the codec's only instant — the manifest's `ExportedAt` and the file name's timestamp (§4.3); the codec reads no clock. |
| `ExcelColumnSpec` | One display column: the header, the Queryex result type, the scale for a bare-path decimal, and the path (null for an expression). `ExcelOperations` builds the list from `QueryexEngine.Validate` over the request's select (spec 0008 §2) so the plan exists before the first row. |
| `ExcelWorkbookPlan` | Immutable; `Write` streams from it. `FileName` is the download name of §4.3; `Manifest` is the key/value header block of §5.3. |
| `Write` | Writes every sheet in plan order, pulling the parent sheet's `Query` through `rows.Pages` with each child sheet's query among its `Dependents` at its depth (§4.2); returns the data rows written; throws `LimitExceededException("Excel.Export.RowLimitExceeded", rows, cap)` when a by-query root's first-page `Count` is `cap + 1`, before any row is spooled (`ExportForImport` counts parent rows), and when a sheet without a count — a by-ids root, a child sheet — reaches its `cap + 1`-th row. |

### 4.2 Spooling and caps

The package is written to a temp file under `Tellma:ScratchPath` (spec 0010; default the operating
system's temp directory) opened with delete-on-close. The parent sheet streams into the package
forward-only as each page arrives, and each child sheet spools to a temp file of its own that is
copied into the package after the parent sheet, so peak memory is one page and its dependents plus
the package buffers. The connection is released when the last page completes (one read round trip
per page, §4.1), before the first byte reaches the HTTP response (synchronous) or the blob store
through `IBlobService.StageAsync` (background). The workbook uses inline strings
(`t="inlineStr"`) throughout: no shared-string table is built, so export memory is flat with row
count.

The synchronous cap is `MaxSynchronousExportRows` (default 100,000); a background export is capped
at `MaxExportRows` (default 1,048,575 — one sheet under a header). Exceeding either fails the
export with `LimitExceededException("Excel.Export.RowLimitExceeded", rows, cap)`, whose message
names the sheet — on a by-query export at the first page, before any row is spooled — never by
truncation, never by multi-sheet chunking.

### 4.3 File name

The planner sets `ExcelWorkbookPlan.FileName` from `ExcelContext.Now` in the request display zone:
`<Entity plural, ASCII-safe> <yyyy-MM-dd HHmm>.xlsx`, whose ASCII form drops every character
outside `[A-Za-z0-9 _-]` and falls back to the entity's technical name when the label leaves
nothing. `ExportOutcome.FileName` and the export handler's staging (§12.3) read it, and a
background export stores it in `Exports.FileName`; spec 0015 adds the `filename*` UTF-8 form that
carries the unscrubbed label.

## 5. Editable-shape export

### 5.1 The parent sheet

`ExportForImport` produces one parent sheet named as in §3.3 with these columns in order:

1. `Id` — number for `int` keys, text for `long` keys; the row's identity on a same-source file
   (§5.4) and the link its child sheets' parent references name (§5.2).
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

Property columns are queried as bare paths and reference columns as their column paths of §8.3
(`Parent.Code`), so the parent query is one `ExcelQuery` with `RootEntity = entity`,
`OrderBy = request.OrderBy ?? "Id"`, the `Take` and `CountCap` of §4.1, the select paths
`[Id, ModifiedAt, …properties…, Parent.Code, Center.Code]` and the child sheets' queries as its
`Dependents` (§5.2), drawn through `EntityService.ExcelRowSource(source)` over the request's
`ExportSource` case (§4.1).

### 5.2 Child sheets

One sheet per collection path the request selects (`ExportForImportRequest.Children`, §2.1), at
every depth. Its name joins the parent's plural label and the label of each path segment with ` · `
(`Widgets · Widget Parts · Widget Part Notes`); over 31 characters, leading segments are dropped
first (never the last), then the name is trimmed; a collision gets ` (2)` (§3.3). The manifest maps
sheet names, so this is safe.

Columns: the **parent reference** first, headed `<Owner navigation label> / Id` with path
`<Navigation>.Id` (`User / Id`, `User.Id`), its value the owner row's `Id` — the link of §9.5; then
the sheet's own `Id`; then the collection's editable properties and reference columns as in §5.1,
restricted by its `ExportCollection.Columns` and keyed per its `ReferenceKeys`.

Rows: the collection's query (`SelectPaths = [<ParentKey>, Id, …]`, `OrderBy = "<ParentKey>, Id"`)
is a `Dependent` of its owner's query, riding the parent page's round trip (§4.1); a collection with
selected collections of its own captures its keys for them (spec 0011 §5.4). Rows are written
grouped under their owner in that order. The parent reference value is the child row's
`<ParentKey>`, which is the owner's `Id`, so no lookup is needed.

Per-sheet cap: `MaxExportRows` per sheet at every depth; exceeding it fails the export with
`Excel.Export.RowLimitExceeded` (§4.2).

### 5.3 The manifest sheet `_tellma`

A hidden sheet (`state="hidden"`, not `veryHidden`, so a curious user can inspect it), protected
without a password (`sheetProtection` with `sheet="1"`) against accidental edits, written for both
shapes. Column A is the key, columns B… the values. Rows 1–13 are the header block; the columns
table follows after one blank row.

```
Row  A                   B…
1    Tellma.Manifest     1                       -- manifest format version (int)
2    Shape               Editable | Display
3    Entity              gl.Center               -- entity name (EntityMetadata.Name)
4    Deployment          <deployment id>         -- DeploymentIdentity.DeploymentId; part of the same-source gate
5    Platform            <Tellma.Core version>
6    SourceTenantId      <int>                   -- part of the same-source gate
7    ExportedAt          <ISO 8601, offset zero> -- ExcelContext.Now
8    Culture             ar-SA
9    Calendar            uq                      -- CalendarCodes value
10   TimeZone            Asia/Riyadh             -- the display zone the file was written in
11   Languages           en, ar                  -- tenant slots 1..3 in order
12   SchemaFingerprint   <hex>                   -- EntityMetadata.SchemaFingerprint
13   Truncated           false                   -- reserved; always false (exports fail, never truncate)
14   (blank)
15   Sheet | Collection | Column | Header | Path | Type | Role | Language | ReferenceEntity | KeyKind | DateEncoding | DatePattern
16…  Centers |        | A | Id            | Id          | Numeric | Id        |    |           |         |        |
     Centers |        | B | Stamp         | Stamp       | String  | Stamp     |    |           |         |        |
     Centers |        | C | Code          | Code        | String  | Property  |    |           |         |        |
     Centers |        | D | Name (E)      | Name        | String  | Property  | en |           |         |        |
     Centers |        | E | Name (ع)      | Name        | String  | Property  | ar |           |         |        |
     Centers |        | F | Parent / Code | Parent.Code | String  | Reference |    | gl.Center | Natural |        |
     Centers |        | G | Center Type   | CenterType  | String  | Property  |    |           |         |        |
     Centers |        | H | Opened On     | OpenedOn    | Date    | Property  |    |           |         | Serial | dd/mm/yyyy
     Users · Role Memberships | RoleMemberships | A | User / Id | User.Id | Numeric | ParentReference | | core.User | | |
     Widgets · Widget Parts · Widget Part Notes | WidgetParts.WidgetPartNotes | A | Widget Part / Id | WidgetPart.Id | Numeric | ParentReference | | fixture.WidgetPart | | |
```

`Role ∈ Id | Stamp | Property | Reference | ParentReference`; `KeyKind ∈ Natural | Surrogate`, set
on `Reference` rows only; `DateEncoding ∈ Serial | Text`; `Type` is the column's Queryex type name
(spec 0008 §7.1); `Language` is the BCP 47 code of a multilingual column's slot; `Collection` is the
collection path (blank for the parent sheet); `Path` is the column path of §8.3, a multilingual
column or reference key written language-relative (`Name` with `Language = ar`, never `Name2`). A
display-shape manifest records every column with `Role = Reference` for a `<Navigation>.<Key>` path
and `Property` otherwise, and `Path` = the select item's path (blank for an expression), so that
feeding a display export back yields precise `ServerOwnedColumn` errors rather than unmapped
columns.

The manifest is **untrusted input** on import: every path must exist on the entity and be
importable, every sheet and column reference must be in range, `Languages` must be BCP 47 codes,
and the same-source gate only decides whether `Id` and `Stamp` are *considered* — a manifest
never grants anything (§13).

### 5.4 The same-source gate

A file is **same-source** when the manifest's `Deployment` equals `DeploymentIdentity.DeploymentId`
(ordinal) and its `SourceTenantId` equals the target tenant id. Tenant ids are allocated per
catalog, that is per deployment (spec 0010 §3.2), so a staging deployment's tenant 5 and
production's tenant 5 are unrelated, and a sandbox tenant has an id of its own. Only a same-source
file may use `Id` as row identity or supply `Stamp`; on any other file `Id` only links rows between
sheets (§9.5) and `Stamp` is ignored, each reported with `Excel.Import.IdentityColumnIgnored`
(§8.1), never silently.

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
- The **default reference key** of a target is `Target.NaturalKey`; an export's `ReferenceKeys`
  (§2.1) and `ImportColumnMapping.Path` (`Center.Region`, §8.3) override it per column with any
  scalar property of the target, unique or not; ambiguity is then `Excel.Import.ReferenceAmbiguous`
  (§10.3).
- **Row identity** (`ImportRequest.RowKey`) is `Id` or a member of `EntityMetadata.NaturalKeys`,
  `Id` by default on a same-source file (§9.2). A key cell is rendered and parsed by its property's
  type like any other cell: a numeric serial number is a number, a date is a date, never text.
- **Child identity** is within its owner: `Id` on a same-source `Update`/`Upsert` under a hydrated
  owner, else the child's `NaturalKey`, else the collection is replaced (§9.5).

### 6.3 Surrogate fallback

A target with no natural key is referenced by its surrogate `Id` (`Center.Id`, §8.3), the column
flagged `KeyKind = Surrogate` in the manifest. A `Reference` column of that kind on a file that is
not same-source (§5.4) is `Excel.Import.SurrogateReferenceFromOtherTenant` at the column (row null)
unless the caller remaps it through `ColumnMappings`; a parent reference is matched within the
workbook (§9.5), never looked up, so it never raises it. Spec 0011 §3.2's startup validation warns
for a referenced entity without a natural key.

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
| Uncompressed size, sum of part lengths from the zip central directory | `MaxImportUncompressedBytes` (2 GB); the synchronous limit is §2.3's | `Excel.Import.PackageTooLarge` |
| Any part read past its declared length (counting stream) | the declared length | `Excel.Import.PackageTooLarge` |
| Package structure: one workbook part, a `[Content_Types].xml`, no external links, no external relationships | — | `Excel.Import.MalformedWorkbook` (`reason`) |
| XML well-formedness of the parts read | — | `Excel.Import.MalformedWorkbook` |

A `.xlsm` is read as a workbook and its macro part ignored. The XML readers run with
`DtdProcessing.Prohibit` and no external resolver. Row counts for the synchronous decision come
from each sheet's `dimension` element, or from a forward count when it is absent.

### 7.3 Shared strings

The shared-string part streams into `SharedStringStore`. Each entry is charged its UTF-16 text
bytes plus a fixed 16 bytes against `MaxInMemorySharedStringBytes` (32 MB), so a flood of empty
entries (`<si/>`) counts like text; beyond the budget the text and a fixed-width offset index both
spill to a temp file under `Tellma:ScratchPath` (`tellma.excel.sst.spilled` incremented), so
resident memory stays bounded whatever the entry count. A part with more than
`MaxSharedStringEntries` entries is `Excel.Import.TooManySharedStrings` (arguments `entries`,
`max`; sheet and row null). Rich-text runs are flattened at store time; `inspect-import` reads the
whole part under the same limits. The byte budget and the entry cap are what keep a web host
serving several imports at once.

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
carries the overrides) and produces an `ImportPlan`. Before the blob is read, both operations
refuse with `BadRequestException` the request faults judged against `EntityMetadata` alone: two
`SheetMappings` for one sheet; two `ColumnMappings` for one (sheet, column); two `SheetMappings`
mapping to one target (the same collection path, `""` included); a `Collection` that is neither
`""`, null nor a collection path of the entity (§8.3); a malformed column letter (§2.1); a
`RowKey` that is neither `"Id"` nor a member of `EntityMetadata.NaturalKeys`. Faults against the
workbook are the errors of the steps below, thrown as `ImportException` by both operations.

1. **Sheets.** A sheet is matched to the parent entity or a collection path by the manifest's sheet
   table, else by name: against the generated sheet name of the parent and of every collection
   path (§5.2's naming, with labels in every tenant language and English), then against the
   technical collection path (`RoleMemberships`, `WidgetParts.WidgetPartNotes`). Without a
   manifest, when no sheet matched the parent, the parent sheet is the first non-hidden sheet that
   matched nothing. Other sheets are `Ignored`, without a warning (`_tellma` is `Manifest`).
   `request.SheetMappings` overrides (§2.1); a `SheetMappings` or `ColumnMappings` entry naming a
   sheet the workbook lacks is `Excel.Import.UnknownSheet`. Two sheets bound to one target by any
   mix of manifest, name and override are both `Duplicate` in the plan and
   `Excel.Import.DuplicateSheet` (argument `collection`) at both sheets at import — never a first
   match.
2. **Columns.** Each header cell is matched, in order: (a) the manifest's column table by header
   text (exact), yielding the path and the language of a multilingual column; (b) the **header
   index** of the sheet's entity: every header the editable or display export could write for each
   column path, importable or not (step 6 decides), plus the technical paths (§8.3), in every
   candidate culture — the request culture, each tenant language and English: property labels; twin
   labels with every symbol of spec 0012's language catalogue (`ExcelContext.Catalog`; a symbol of a
   language the target does not configure maps to that language for §8.2's warning); reference
   headers `<Navigation label> / <Key label>` and `<Navigation label> / <Key path>` for every
   key-typed scalar of the target, both halves in one culture; the parent reference
   `<Owner navigation label> / Id`; the disambiguated form `<Label> (<Path>)`. `Id` and `Stamp` are
   candidates by technical name and label. Each candidate is normalised — NFC, trimmed, whitespace
   runs collapsed to one space, no space next to `/`, `(` or `)`, compared ordinal-ignore-case — and
   a header normalised the same way is matched exactly; no header is parsed. A normalised key that
   two different column paths produce is removed from the index, and the header becomes
   `UnmappedColumn` with both paths among its suggestions — never a first-match guess. Labels in
   another culture are read through `ILabelProvider` under a `CultureInfo.CurrentUICulture` scope
   set to that culture (spec 0012 §7.6 resolves labels under the current culture). The index is
   built once per (entity, settings tag, request culture) and cached with the labels (§3.3); it
   holds a few hundred keys for a typical entity and about 5,000 for a wide one.
3. **Override.** `request.ColumnMappings` (sheet + column letter → path, or → null to ignore) wins
   over everything. A letter with no header cell, or on a sheet the plan ignores, is
   `Excel.Import.UnknownColumn`; a `Path` that is not a column path of the sheet's entity under
   §8.3 is `Excel.Import.UnknownPath` (argument `path`).
4. **Unmapped columns** are errors (`Excel.Import.UnmappedColumn`, with up to three `Suggestions`:
   the header-index keys nearest the normalised header by Damerau-Levenshtein distance) unless
   `request.IgnoreUnmappedColumns`, in which case they are `Ignored`.
5. **Duplicates.** Two columns of one sheet mapping to the same path is
   `Excel.Import.DuplicateColumn` at both columns.
6. **Ownership.** A path whose ownership is `ServerOwned`, `Derived` or `DatabaseOwned`, or a
   `[BlobReference]`, `[JsonColumn]`, `[ExcludeFromExcel]`, `hierarchyid` or `byte[]` property, is
   `ServerOwned` in the plan and the error `Excel.Import.ServerOwnedColumn` at import;
   `WriteOnce` paths are mappable (§9.3).
7. **Identity columns.** On a file that is not same-source (§5.4), every mapped `Id` and `Stamp`
   column of every sheet gets the warning `Excel.Import.IdentityColumnIgnored`, in the plan and at
   import (`reason = ForeignSource` with the manifest's `deployment` and `sourceTenantId`, or
   `NoManifest`); a `Stamp` column is `Ignored`, and an `Id` column stays `Mapped` when it links a
   present child sheet (§9.5), else `Ignored`. The `InsertMode` case arises at `import` alone
   (§9.1), so `inspect-import` needs no `Mode`; `IgnoreSheetStamps = true` raises no warning for a
   `Stamp` column (the caller asked).

`ImportPlan.RowKeyCandidates` lists `Id` first when the file is same-source and the parent sheet
maps an `Id` column, then the `EntityMetadata.NaturalKeys` paths mapped in the parent sheet;
`DefaultRowKey` per §9.2; `TotalRows` sums every mapped sheet's data rows; `RequiresBackground` per
§2.3 and `AtomicAllowed` per §2.1; `Entity` is the manifest's entity name (null without one);
`ManifestVersion` 0 without a manifest; a manifest naming a different entity than the stack is
`Excel.Import.MalformedWorkbook` (`reason = entity`).

At `import` time each child sheet's rows are also counted per owner row (the row they link to,
§9.5) as the sheet is read (§7.4), at every depth: more rows under one owner row than the
collection's `MaxCount` (spec 0011 §2.2) is `Excel.Import.TooManyChildren` (arguments
`collection` as the collection path, `max`, `count`) at the parent reference column of the first
row beyond the cap; a parent-sheet row that, with its rows at every depth, exceeds the stack's
`MaxRowsPerSave` is `Excel.Import.TooManyDescendants` (arguments `rows`, `max`) at that parent
row. Both arise before any round trip and in either mode, since no chunk (§12.4) can hold such a
parent.

### 8.2 Language-aware mapping

A multilingual column maps to the tenant slot of the column's **language**: a file column recorded
in the manifest or identified by its symbol as Arabic maps to whichever of `Name`/`Name2`/`Name3` is
Arabic in the target tenant (`TenantSettings.Languages` in slot order), so a file exported from a
tenant whose languages are `ar, en` imports correctly into one whose languages are `en, ar`; the
plan reports the resolved slot (§8.3). A column in a language the target does not configure is
**ignored with the warning `Excel.Import.UnconfiguredLanguageColumn`** (the value has no home;
review flag 5). A column that names a slot technically (`Name2`) maps to that slot regardless of
language and is subject to spec 0014's `Import.LanguageNotConfigured` when the slot is gated off.

### 8.3 Column and collection paths

One grammar names columns and sheets in the manifest (§5.3), the plan (§8.1) and the overrides
(`ImportColumnMapping.Path`, `ImportSheetMapping.Collection`, §2.1).

- A **column path** is relative to its sheet's entity. `<Property>` is a scalar; a twin name
  (`Name2`) is that slot; the group's primary name (`Name`) is the primary slot unless a language
  accompanies it (the manifest's `Language`, a header symbol), in which case it is that language's
  slot in the target tenant (§8.2). `<Navigation>.<Key>` is a reference column: the foreign key
  behind the navigation (`EntityMetadata.References`) expressed in the target's scalar `Key`
  (`Parent.Code`, `Center.Region`, `Role.Name2`); `<Navigation>.Id` is the surrogate form
  (`Center.Id`). A reference key that is multilingual resolves through the slot of the
  **column's** language, so the lookup path is `Name2` rather than `Name` when that is the slot
  (§10.1). A child sheet's **parent reference** is the reference whose foreign key is the
  collection's `ParentKey`, always in the form `<Navigation>.Id` (`User.Id`, `WidgetPart.Id`) and
  matched within the workbook (§9.5). A bare foreign-key name (`CenterId`) is accepted as input
  for `<Navigation>.Id` and never written by the manifest or the plan.
- A **collection path** is `<Collection>(.<Collection>)*`: the `ChildCollectionMetadata.Property`
  names from the stack's entity (`RoleMemberships`, `WidgetParts.WidgetPartNotes`), in the dotted
  form spec 0011 uses for nested collections; `""` is the parent sheet.
- The plan reports every column's **resolved** path — the target slot of a multilingual column,
  the key of a reference — so any plan column echoed back as an override keeps its meaning.

## 9. Import semantics

### 9.1 Modes

| Mode | Parent rows | `Id` / `Stamp` columns |
|---|---|---|
| `Insert` | every row is new | `Id` only links rows between sheets (§9.5) and `Stamp` is ignored, both reported with `Excel.Import.IdentityColumnIgnored` (`reason = InsertMode`) in the outcome; duplicates against the database surface through the pipeline's `[Unique]` validators and, for the race, 2601/2627 mapped to the column — and to the row only when the chunk carries one (spec 0014 §14.2) |
| `Update` | every row must resolve through the row key (§9.2) to an existing, readable row; a miss is `Excel.Import.RowNotFound` at (row, key column) | `Id` is the default row key on a same-source file (§9.2); `Stamp` per §9.3 |
| `Upsert` | resolved rows follow the `Update` path, the rest the `Insert` path; with `RowKey = "Id"` only a negative or blank `Id` is a new row | as `Update` for the resolved rows |

`IsActive` is server-owned: it is absent from the editable shape, every imported row is created
active, and deactivation is the action, never the sheet; the display export still includes it.

### 9.2 Row identity

`RowKey` defaults to `Id` when the file is same-source (§5.4) and the parent sheet maps an `Id`
column, else to the first member of `EntityMetadata.NaturalKeys` whose column is mapped; `Update`
and `Upsert` with no default and no `RowKey` in the request is `BadRequestException`. With
`RowKey = "Id"`, a positive value hydration does not return is `Excel.Import.RowNotFound` in both
modes — a vanished row is never re-inserted — and a negative or blank value is a new row in
`Upsert` and `Excel.Import.RowNotFound` in `Update`. `Id` as a row key on a non-same-source file is
`Excel.Import.IdKeyRequiresSameTenant` (row null). Before any database call, duplicate row-key
values, duplicate values in any `[Unique]` column of the parent sheet, and duplicate `Id` values
on any sheet that owns a present child sheet (§9.5) are `Excel.Import.DuplicateRowKey` at every
duplicate row, compared after §10.3 normalisation.

### 9.3 Hydration and write-once columns

For `Update` and `Upsert`, the ids the row key resolved are hydrated through
`EntityService.GetByIdsAsync(ids, DetailsRequest.None)` — the complete entity with its child
collections at every depth (spec 0014's details plan), under the caller's read filter, so a row the
caller cannot read is absent and reported as `RowNotFound` (§10). The codec overlays the mapped
columns on the hydrated entity, so a partial sheet touches only the columns it contains. The
hydrated `ModifiedAt` stays on the entity as the expected stamp unless the file is same-source
(§5.4), the `Stamp` column is mapped, `ImportRequest.IgnoreSheetStamps` is false and the cell is
not blank; then the sheet's value is parsed and set instead (an unparsable stamp is
`Excel.Import.InvalidCell` at the cell).

`WriteOnce` paths are mappable in every mode; on an update a mapped value differing from the
hydrated value is `Excel.Import.WriteOnceChanged` at the cell (the codec's early form of the
pipeline's `WriteOnce` error, raised before the save so the row is reported with coordinates);
equal values are accepted — the natural key is often the write-once column.

### 9.4 Blank cells

A blank cell in a mapped column sets the property to null; a non-nullable property then fails the
pipeline's `Required` validation at that cell. To leave a column untouched, remove it from the
sheet or map it to null. No sentinel exists.

### 9.5 Children

- A child sheet **present** in the workbook means "these are the complete children of every owner
  row present in its owner sheet": each owner's collection is synchronised to its sheet rows, at
  every depth.
- **Link:** a parent reference cell (§8.3) is matched within the workbook against the owner
  sheet's `Id` column (after §10.3 normalisation), never looked up. A row whose value matches no
  owner row is `Excel.Import.OrphanChildRow` at its parent reference column; an orphan's own
  descendants raise nothing further. An owner row with a blank `Id` has no child rows. The owner
  sheet's `Id` values must be unique when it owns a present child sheet (`DuplicateRowKey`, §9.2).
- A child sheet whose owner sheet is absent or ignored, or whose owner sheet maps no `Id` column,
  is `Excel.Import.OwnerSheetAbsent` (child sheet / — / —, `collection`, `owner`).
- A child sheet **absent** from the workbook leaves that collection untouched at its depth: it is
  passed as `null` (spec 0011's "untouched" value). An owner row with **no rows** in a present
  child sheet gets an empty list: its children are deleted (review flag 6).
- **`Id` values:** a positive `Id` identifies an existing row only on a same-source file in
  `Update` or `Upsert` (and, for a child row, under an owner that was hydrated); a negative `Id`
  marks a new row; a blank `Id` is a new row nothing in the workbook can link to. On a
  foreign-source file and in `Insert` mode every `Id` only links rows between sheets, and every
  child row is new.
- **Matching the children of an existing owner** (hydrated at every depth, §9.3): by `Id` per the
  rule above (a positive `Id` that is not an existing child of that owner is
  `Excel.Import.RowNotFound`); else by the child's `NaturalKey` within the owner when its column is
  mapped; else the collection is **replaced**: every existing child and its descendants deleted
  (spec 0011 §8.3), every sheet row inserted. Replacement requires every editable column of the
  child mapped and every descendant collection's sheet present, else
  `Excel.Import.IncompleteChildSheet` (child sheet / — / —, `collection`, `missingColumns`,
  `missingCollections`), whatever produced the file — a user can delete columns by hand. A matched
  child keeps its id and is overlaid like a parent; an unmatched sheet row is new (`Id = 0` in the
  payload, or the codec's temporary id); an unmatched existing child is dropped from the list and
  deleted with its descendants.

### 9.6 Trees and self-references

Every self-typed reference (an `EntityMetadata.References` entry whose `Target` is the entity: the
tree parent of a tree entity and any other) from a sheet row to a **new** sheet row is an edge of
the in-sheet graph; the column itself is an ordinary reference column (§8.3). At parse the graph
orders the parent rows — Kahn's algorithm, ready rows taken in ascending Excel row, so the order is
a pure function of the file and the mapping — and detects cycles: a cycle among new rows through
any self-typed reference is `Excel.Import.ParentCycle` (arguments `rows`, `path`) at every row in
it, before any database call.

Binding is per chunk (§12.4; the whole file is one chunk in a synchronous or `Atomic` import): a
reference to a new row of the same chunk takes that row's temporary id (`-1, -2, …`, the codec's
own sequence, restarted per chunk). A self-reference naming, by its negative `Id` (`Parent.Id`,
§8.3), a new row an earlier chunk committed binds to the id `CommittedNewIds` records for it
(§12.5); a resumed job reads the map back with the rest of the state, so the rule holds across a
crash. Every other self-reference — an existing row, or an earlier chunk's row named by its key —
resolves through the chunk's lookup (§10.1), the referenced row being committed by then. Resolution
order for a self-reference: in-chunk match (§10.2), then the map for a negative `Id`, which is never
looked up, and the lookup for any other value, then `ReferenceNotFound`. The pipeline rewrites
temporary ids in every self-typed foreign key and validates cycles against the loaded ancestor
chains (spec 0014's tree capability); a single bulk `INSERT` from one TVP satisfies the
self-referencing foreign key regardless of order, so the order serves chunk boundaries and readable
errors, not insert correctness.

## 10. Bulk resolution

### 10.1 Lookups

Lookups are per chunk: the codec emits one `ExcelQuery` per distinct **(target entity, key path)**
pair across the chunk's sheets and columns — every reference column, the row key when it is a
natural key, and the self-references not bound within the chunk or through the checkpoint's map
(§9.6), never a parent reference (§9.5) nor a self-reference's negative `Id`, which names no
database row — with `SelectPaths = [<KeyPath>, Id]`, a `KeySetRestriction("<KeyPath>", TVP)` as
`Restriction`, and `RestrictionValues` = the distinct values of that key in the chunk after §10.3
normalisation, each group of `MaxIds` values a separate query. `ExcelOperations` reads them all in
one round trip through `ReadAsync` over `EntityService.ExcelRowSource(ExportSource.All)` of the
**imported** stack (no clauses: the restriction is the whole predicate): spec 0014 compiles a query
whose `RootEntity` is a lookup target on that entity under the caller's `Read` decision for its
resource — `Denied` yields no rows, so an unreadable reference resolves as not found. The compiled
shape of one query, illustratively (the row source binds the TVP as `@tb{b}_t{i}` and names it as
the restriction's `TableSource`):

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

### 10.2 In-chunk first

Before a lookup for a self-reference (§9.6), the value is matched against the rows of the same
chunk by the same key — the sheet's *mapped* column for that key, after normalisation. A hit binds
the reference to that row's entity (its temporary id for a new row, its hydrated id for an existing
one) and the value is excluded from the lookup's TVP; every other value binds by §9.6's resolution
order.

### 10.3 Matching

Sheet values are trimmed and NFC-normalised; database values are grouped after the same
normalisation with `StringComparer.OrdinalIgnoreCase` (numeric and `Guid` keys compare by value).
Exactly one row ⇒ resolved; zero ⇒ `Excel.Import.ReferenceNotFound` at (row, column) — which is
also what a row hidden by the caller's read filter yields; more than one ⇒
`Excel.Import.ReferenceAmbiguous` with argument `count`. A database row the codec cannot attribute
to any sheet value (a collation equivalence beyond case: accent- or width-insensitivity) is
`ReferenceNotFound` with argument `collation = true` so the message can hint at it. A natural-key
row key is a lookup like any other; its resolved ids feed hydration (§9.3); an `Id` row key feeds
hydration directly.

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
    public int RowCount { get; }                       // parent rows in topological order
    public IReadOnlyList<int> SheetRows { get; }       // the Excel row of each position
    public string OrderHash { get; }                   // over SheetRows; a resumed job compares it (§12.4)
    public IReadOnlyList<ExcelQuery> LookupsFor(int fromRow, int toRow);
    public IReadOnlyList<object> HydrationIds(
        int fromRow, int toRow, IReadOnlyDictionary<ExcelQuery, IReadOnlyList<IReadOnlyList<object?>>> lookupRows);
    public ExcelChunk<TEntity> Resolve(
        int fromRow, int toRow,
        IReadOnlyDictionary<ExcelQuery, IReadOnlyList<IReadOnlyList<object?>>> lookupRows,
        IReadOnlyList<TEntity> hydrated, IReadOnlyDictionary<long, long> committedNewIds);
    public IReadOnlyDictionary<long, long> LaterReferencedIds(int fromRow, int toRow, ExcelChunk<TEntity> chunk);
}

public sealed record ExcelChunk<TEntity>(
    IReadOnlyList<TEntity> Entities, IReadOnlyDictionary<ValidationPath, ExcelCell> CoordinateMap,
    IReadOnlyList<ImportError> Errors);
public sealed record ExcelCell(string Sheet, int Row, string Column);
```

| Member | Meaning |
|---|---|
| `Parse` | Reads the whole workbook once: mapping (§8), decoding of every mapped cell (§3.1), in-file uniqueness (§9.2), links and grouping at every depth (§9.5), per-owner counts (§8.1), the self-reference order and cycle check (§9.6). Any error is collected; `Parse` throws `ImportException` when errors exist, so no lookup runs for a file that cannot be saved. |
| `LookupsFor` | The chunk's lookups of §10.1 for positions `fromRow..toRow` (0-based indexes into the topological order); `ExcelOperations` reads them in one round trip. |
| `HydrationIds` | For `Update`/`Upsert`, the ids of the chunk's rows that name existing rows: the sheet's `Id` under an `Id` row key, the row-key lookup's ids otherwise; `ExcelOperations` hydrates them through `GetByIdsAsync` (§9.3). |
| `Resolve` | Binds lookup results, overlays the hydrated entities, matches children (§9.5), assigns temporary ids (§9.6), sets self-references — one naming, by its negative `Id`, a row an earlier chunk committed through `committedNewIds` (§9.6, §12.5) — and returns the chunk's entities with the coordinate map and the resolution errors (`ReferenceNotFound`, `ReferenceAmbiguous`, `RowNotFound`, `WriteOnceChanged`, `IncompleteChildSheet`); deterministic for a range, so §11.2's retry re-invokes it with refreshed hydrated entities. |
| `LaterReferencedIds` | For the chunk's new rows that a row at a later position references by a negative `Id`, that sheet value mapped to the id the pipeline assigned to the row (spec 0014 §6.4), read from `chunk.Entities`; the entries the checkpoint adds to `CommittedNewIds` (§12.5). |
| `CoordinateMap` | `ValidationPath` segments → cell, matched segment by segment at any depth (shown here in rendered form): `[i]` → the parent row; `[i].<Property>` → its column when mapped; `[i].<C1>[j]` → the row on the `C1` sheet; `[i].<C1>[j].<C2>[k]` → the row on the `C1.C2` sheet; a property after any row → its column when mapped. A path with no column maps to the row alone. |

## 11. The save, concurrency, and errors

### 11.1 The save

`ExcelOperations.Import` hands each chunk's entities to `EntityService.SaveAsync` with
`SaveOptions { ReturnEntities = false, Details = None, Concurrency = Check, Source = Import }`, plus
`OnPersist` set to the checkpoint of §12.5 on a background chunk. Entities carry `Id = 0` or a
temporary id for inserts, the hydrated id and expected `ModifiedAt` for updates, child collections
at every depth as `null` (untouched), `[]` (delete all) or the synchronised list; server-owned
members at their defaults for the pipeline to overwrite. The pipeline runs preprocessing, validation
(the two-stage access pre-check on the hydrated ids, `[Unique]` and foreign-key validators, tree
cycle validation), persist and effects exactly as for a JSON save; an import bumps whatever version
tags the emitter bumps.

### 11.2 Concurrency

Every hydrated row saves under `Check` with its expected stamp — the sheet's `Stamp` or the
`ModifiedAt` read at hydration, under §9.3's rule — and there is no override, so a concurrent edit
to any column between hydration and persist is a conflict, never a silent overwrite. When
`SaveAsync` throws `ConcurrencyException`, each `ConcurrencyConflict.Id` maps through the chunk's
resolved ids to its sheet row. When every conflict has `IsMissing = false` and every conflicting
row's expected stamp came from hydration, the codec re-hydrates the conflicting ids through
`GetByIdsAsync`, re-runs `Resolve` for the same rows with the refreshed entities and saves again, at
most `ExcelOptions.ImportConcurrencyRetries` times (`tellma.excel.import.retries` counts each); the
lookups are not re-run. The pipeline cannot do this itself: only the codec knows which columns the
sheet carried. A conflict on a row whose expected stamp came from the sheet is the user's stale
file, so that save's conflicts are reported at once, as are those that remain after the last retry:
each is `Excel.Import.ConcurrencyConflict` at (row, the `Stamp` column when the sheet's stamp was
the expected stamp, else the row) with arguments `modifiedAt`, `modifiedBy`, and a conflict marked
`IsMissing` is `Excel.Import.RowNotFound`. A `NotFoundException` from `SaveAsync` — an updated row
deleted, or hidden from the caller, between hydration and the save (spec 0014 §6.6) — is
`Excel.Import.RowNotFound` at each id's (row, key column), never retried.

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

A synchronous import is one transaction: any error anywhere — parse, resolution, validation, persist
— rolls back everything and returns `ImportException` (422) with an empty `CommittedRowRanges`, or
the pipeline's `ForbiddenException` (403) unchanged (§13). No partial success.

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
    public string? ResultJson { get; set; }
    public int? RowCount { get; set; }
    public int? ErrorCount { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }           // datetimeoffset(3)
}

// Tellma.Core.Excel (runtime)
public sealed class ExportService : EntityService<Export>;   // no enqueue: the export handler inserts the row (§12.3)
// ContributeAsync enqueues core.import for every new row whose JobId is null
public sealed class ImportService : EntityService<Import>;
public sealed class ExportAccessCriteria : IAccessCriteriaProvider;   // Resource = core.Export; CreatedById = me() for Read and Delete, reason self
public sealed class ImportAccessCriteria : IAccessCriteriaProvider;   // the same for core.Import

// stored in Jobs.ArgumentsJson
public sealed record ExcelJobCulture(string Culture, string Calendar, string TimeZone, string Language);
public abstract record ExportJobArguments(string Resource, ExcelJobCulture Culture);   // JSON-polymorphic on "kind": Display | ForImport (ExportKind)
public sealed record DisplayExportJobArguments(string Resource, ExcelJobCulture Culture, ExportRequest Request)
    : ExportJobArguments(Resource, Culture);
public sealed record ForImportExportJobArguments(string Resource, ExcelJobCulture Culture, ExportForImportRequest Request)
    : ExportJobArguments(Resource, Culture);
public sealed record ImportJobArguments(string Resource, ExcelJobCulture Culture, StartImportRequest Request);
```

`Export` declares `[DefaultSelect]` over `Id`, `Resource`, `Kind`, `FileName`, `FileId`, `RowCount`,
`ExpiresAt`, `JobId` and the four audit columns, and `Import` over `Id`, `Resource`, `Mode`,
`FileId`, `RowCount`, `ErrorCount`, `ExpiresAt`, `JobId` and the four audit columns — never
`RequestJson` or `ResultJson`, which a client selects on the details page (spec 0014 §2.2's
`DefaultSelect`).

The server-written columns — `Id`, the four audit columns and `JobId` — are `[ServerOwned]`;
`Resource`, `Kind`/`Mode`, `RequestJson` and `ExpiresAt` are `[WriteOnce]`, fixed when the row is
inserted; `FileName`, `FileId`, `RowCount`, `ResultJson` and `ErrorCount` are editable, which the
handlers need and no client can reach because the stacks project `query`, `get`, `get-by-ids`,
`delete`, `delete-by-query` and register `core.Export`/`core.Import` × `Read | Delete` — no `Save`
securable and no `save` endpoint. A row exists when it has a file to own: an `Import` row from the
request, owning the upload, and an `Export` row from the run's completion, owning the produced file.
Both stacks declare `Enlist`, and every save of a row is an enlisted save (spec 0014 §13.3):
`ExcelOperations<TEntity> : IEnlists<Import>` enlists `import/start`'s insert with the invoker's
frame (§12.2), and `ExportJobHandler : IEnlists<Export>` (the insert at the end of a run) and
`ImportJobHandler : IEnlists<Import>` (the completion update) enlist with the job frame (§12.3,
§12.6), so each row runs its own stack's validators and effects as a participant of the frame that
authorises it. `ExportAccessCriteria` and `ImportAccessCriteria` supply the self-scope criterion
`CreatedById = me()` for `Read` and `Delete` (spec 0013 §5.1), so every member sees and may delete
their own rows ("My exports"); an administrator with a stored grant sees all. Deleting a row
releases its blobs through the emitter's `[BlobReference]` capture (spec 0016).

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
| `ExpiresAt` | `datetimeoffset(3)` | no | `IX_Exports_ExpiresAt (ExpiresAt)` | set on insert from the job scope's `RequestContext.Now` plus `ExportFileRetentionDays` |
| `JobId` | `int` | yes | `FK_Exports_JobId → core.Jobs ON DELETE SET NULL`, `UX_Exports_JobId WHERE JobId IS NOT NULL` | set by the handler's insert (§12.3) |
| audit set | | | `FK_Exports_CreatedById`, `FK_Exports_ModifiedById` | `IX_Exports_CreatedBy (CreatedById, CreatedAt DESC)` |

**`core.Imports`** — the same shape; sequence `core.sq_Imports`.

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK clustered, `core.sq_Imports`; `CK_Imports_Id CHECK ([Id] > 0)` | |
| `Resource` | `varchar(128)` | no | | |
| `Mode` | `varchar(8)` | no | | `Insert` / `Update` / `Upsert` |
| `RequestJson` | `nvarchar(max)` | yes | | the serialized request; ≤ 64 KB |
| `FileId` | `int` | yes | `FK_Imports_FileId → core.Blobs`, `UX_Imports_FileId WHERE FileId IS NOT NULL` | `[BlobReference("import-file", Attachment, ReadAccess = OwnerRead)]`; the uploaded workbook |
| `ResultJson` | `nvarchar(max)` | yes | | the `ImportOutcome` when `ErrorCount = 0`, else the `ImportException`'s `errors`, `totalErrors` and `committedRowRanges`, serialized with the platform JSON options; about 300 bytes per reported error |
| `RowCount` | `int` | yes | | parent rows in the file |
| `ErrorCount` | `int` | yes | | `TotalErrors` of a failed import; 0 on success |
| `ExpiresAt` | `datetimeoffset(3)` | no | `IX_Imports_ExpiresAt (ExpiresAt)` | set on insert from `RequestContext.Now` plus `ImportFileRetentionDays` |
| `JobId` | `int` | yes | `FK_Imports_JobId → core.Jobs ON DELETE SET NULL`, `UX_Imports_JobId WHERE JobId IS NOT NULL` | |
| audit set | | | | `IX_Imports_CreatedBy (CreatedById, CreatedAt DESC)` |

Both are Queryex roots (`core.Export`, `core.Import`) with the navigations `File`, `Job`,
`CreatedBy` and `ModifiedBy` derived from their foreign keys. Retention: spec 0019's
`core.file-retention` (daily) selects the ids of rows whose `ExpiresAt` has passed in pages of 500
and deletes them through `DeleteByIdsAsync` on both stacks as the system user; the capture releases
their blobs and `core.blob-sweep` reclaims the bytes.

### 12.2 Enqueue

`export/start` and `export-for-import/start` enqueue one `core.export` job through
`IJobQueue.EnqueueAsync` with no entity — `HandlerKey` `core.export`, `Arguments` the
`ExportJobArguments` of the request's case (§12.1), `DueAt = null`, `RequestedById` and
`RunAsUserId` the caller — and return `JobAccepted(JobId, null)`: no `Export` row exists until the
handler has a file for it to own (§12.3). `import/start` builds one `Import` row —
`Resource = Descriptor.Resource`, `Mode`, `RequestJson` (the serialized request),
`FileId = request.FileId` (which attaches the staged upload) and `ExpiresAt` from
`RequestContext.Now` plus `ImportFileRetentionDays` — enlists its insert (`EnlistSaveAsync`) with
the `IOpenWriteHost` that `IApiActionInvoker` registers in the scope for the frame it opens around
the method, and awaits `host.PersistAsync()` on it (spec 0014 §13.3). `ImportService`'s
`ContributeAsync` type-tests its context as a `SavePersistContext` and, for every new row (its
`Before(i)` null) whose `JobId` is null, calls `IJobQueue.Enqueue(context.Batch, requests)` with one
`JobRequest` per row — `HandlerKey` `core.import`, `Arguments` the row's `ImportJobArguments`
carrying the `StartImportRequest` deserialized from its `RequestJson`, `DueAt = null`,
`RequestedById` and `RunAsUserId` the caller, `Entity` the row — so the row insert, the job insert
and the `JobId` write-back are one transaction (spec 0019's enqueue statement); the method returns
`JobAccepted(JobId, ImportId)`. Both kinds of arguments carry the typed request and an
`ExcelJobCulture` of the request's negotiated `Culture`, `Calendar`, `TimeZone` (display zone) and
`Language`, so the job renders exactly as the request would have; spec 0015 answers `JobAccepted`
with 202.

The client follows a job through `jobs/query` (self-scope) and the hub's `job.changed`, and may
cancel a job it requested (spec 0019 §11.2); it finds a finished export's row through
`exports/query` on `JobId` or the `core.export.ready` notification's `TargetId`, a finished import's
result in its row's `ResultJson` (`imports/get`), and either file through the blob GET.

### 12.3 The export handler

`ExportJobHandler : IJobHandler, IEnlists<Export>` carries `[JobHandler]` with key `core.export`,
`BatchSize = 1`, `LeaseSeconds = 600`, `MaxAttempts = 3` and `Schedulable = true`. A run that
`export/start` enqueued and a run that a user schedule fired (spec 0019 §14.1) are the same: the
job's `ArgumentsJson` is an `ExportJobArguments` and no `Export` row exists yet. Steps, in the job
scope of the run-as user, inside the job frame spec 0019's worker opens around `ExecuteAsync` (spec
0014 §13.3):

1. Read the item's `ExportJobArguments` (`Arguments<ExportJobArguments>()`, JSON-polymorphic on
   `kind`).
2. Re-evaluate `IAccessEvaluator.RequireAsync(Resource, "Read")`; a denial completes the item
   `Fail(JobError("forbidden", { resource, action }))`, the code and arguments of the denial.
3. Build the `ExcelContext` from the arguments' `ExcelJobCulture` (culture, calendar, zone,
   language) and the job scope's `RequestContext.Now`; plan the case's `Request` in its shape
   (`PlanDisplay` or `PlanEditable`); stream the root query through `Pages` with
   `CountCap = MaxExportRows`, a first-page `Count` of `cap + 1` failing the item before any row is
   written; after each page, `Progress.Report` with `percent = min(99, 100 × rows / total)`, where
   `total` is the first page's `Count` (by query) or `request.Ids.Count` (by ids), and the message
   `Excel.Export.Progress` with `{ rows }`, the rows written so far. The handler never reports 100:
   spec 0019 §5.4's completion statement writes it.
4. Stage the spooled file through `IBlobService.StageAsync` with a `BlobStageRequest` of kind
   `export-file`, the stream, its length, the xlsx content type and `plan.FileName`.
5. Enlist the **insert** of the `Export` row with the job frame (`EnlistSaveAsync`): `Resource`,
   `Kind` from the case, `RequestJson` from `Request`, `FileId`, `FileName`, `RowCount`, `ExpiresAt`
   from the job scope's `RequestContext.Now` plus `ExportFileRetentionDays`, and `JobId = Job.Id`,
   naming `JobId` in `EnlistSaveOptions.ServerOwned`. Awaiting `host.PersistAsync()` on the
   `IOpenWriteHost` spec 0019's worker registers for the job frame runs the row's validation round
   and appends the group to the partition's completion batch, so the row commits with the job
   outcome and the blob effect confirms the staged file inside the completion transaction (the
   run-as user is uploader and saver, spec 0016 §4.6).
6. The item's `NotifyOnSuccess` with a `NotificationRequest("core.export.ready", …)` to the item's
   `Job.RequestedById` — arguments `fileName`, `rowCount` (spec 0020's catalogue entries for the
   type), `TargetResource = "core.Export"`, `TargetId` the inserted row's id, which
   `EnlistedSave.Rows` carries — and `Succeed()`.

Re-runs are safe: an attempt that did not complete inserted no row, and its staged file is never
confirmed (spec 0016's sweep reclaims it). A cap overflow, at the first page or on a child sheet
(§4.2), completes the item `Fail(JobError("Excel.Export.RowLimitExceeded", …))` with the arguments
of Appendix A and without a notification of its own (spec 0019's `core.job.failed` covers it).

### 12.4 The import handler

`ImportJobHandler : IEntityJobHandler<Import>, IEnlists<Import>` carries `[JobHandler]` with key
`core.import`, `BatchSize = 1`, `LeaseSeconds = 600` and `MaxAttempts = 1` — a partially committed
import never re-runs blindly; a crash mid-import surfaces as `Jobs.AttemptsExhausted` and spec
0019's `core.job.failed`; an administrator's `retry` action (`core.Job × Retry`) resets attempts
and the handler resumes from its checkpoint. The handler is a host, not a participant of the target
stack's pipeline (spec 0014 §13.3): each chunk's `SaveAsync` is the target stack's front door, and
only the `Import` row's completion write is enlisted (§12.6). Two passes over the local copy of
`Import.FileId` (§7.1):

1. **Pass 1 (no database):** `Parse` of the job's `ImportJobArguments.Request` under an
   `ExcelContext` built from its `Culture` and the job scope's `RequestContext.Now` (§10.4). With
   `Atomic = true`, a file above `MaxAtomicImportRows` rows, the parent ceiling (§2.3) in parent
   rows or the stack's `MaxRowsPerSave` rows is `Excel.Import.TooLargeForAtomic` (`rows`,
   `maxRows`): the `/start` path reads no file, so these ceilings are checked here. When the item
   carries a checkpoint (§12.5), a session `OrderHash` that differs from the checkpoint's is
   `Excel.Import.ResumeMismatch` (`expected`, `actual`). Any error completes the item with the
   outcome of §12.6 before this attempt commits anything.
2. **Pass 2:** from the checkpoint's `NextRow` (0 on a first run), parent rows in topological order
   are processed in chunks of `ImportChunkRows` (default 10,000 parent rows with every descendant
   row). A chunk is closed early when it would otherwise exceed the parent ceiling (§2.3) or the
   stack's `StackLimits.MaxRowsPerSave` counted over every row at every depth, so no chunk is
   refused by its hydration or its save (spec 0014 §2.5, §6.8). Each chunk reads its lookups in one
   round trip (`LookupsFor`, §10.1), hydrates (`HydrationIds`, §9.3), runs `Resolve` and runs
   `SaveAsync` as its own transaction with §11.2's retries; a self-reference binds within the
   chunk, through the checkpoint's map or by lookup (§9.6). **The checkpoint rides the chunk's
   persist transaction** (§12.5), with `ProgressPercent = min(99, 100 × NextRow / RowCount)`. A
   re-leased or retried job re-runs pass 1 (the file is immutable) and resumes at `NextRow`,
   re-chunked under the current `ImportChunkRows`, parent ceiling and `MaxRowsPerSave` — a
   committed row is never re-applied and none is skipped. A validation or persist error in a chunk,
   after §11.2's retries, stops the job: the earlier chunks stay committed; the outcome reports the
   checkpoint's `CommittedRowRanges` and the chunk's errors. Cancellation (`CancelRequestedAt` from
   the requester's or an administrator's `cancel`, lease loss, host stop) takes effect at a chunk
   boundary; an in-flight transaction rolls back with its checkpoint.

`StartImportRequest.Atomic = true` runs the whole file as one chunk (one transaction) under the
ceilings of pass 1; a parent above its collection's `MaxCount`, or with more rows at every depth
than `MaxRowsPerSave`, is refused as its child sheets are read, in either mode (§8.1). The progress
messages are the phase keys `Excel.Import.Parsing`, `Excel.Import.Resolving`,
`Excel.Import.Validating` and `Excel.Import.Saving`; the worker renews the lease while the handler
runs. Lock escalation inside a 10,000-row chunk is accepted: the transaction is short and the
escalation is the one a 10,000-row JSON save incurs.

### 12.5 The checkpoint

```csharp
// Tellma.Core.Excel (runtime); the StateJson of a core.import job
public sealed record ImportCheckpointState(
    int NextRow, string OrderHash, int Inserted, int Updated, int ChildrenInserted, int ChildrenUpdated,
    int ChildrenDeleted, IReadOnlyList<(int From, int To)> CommittedRowRanges,
    IReadOnlyDictionary<long, long> CommittedNewIds);
```

| Member | Meaning |
|---|---|
| `NextRow` | The position in the topological order (§10.4) of the first uncommitted parent row. |
| `OrderHash` | The session's `OrderHash` (§10.4) when the checkpoint was written; a resumed job compares its own with it (§12.4). |
| `Inserted` … `ChildrenDeleted` | Running totals over every committed chunk, children counted at every depth; `ChildrenDeleted` counts the children synchronisation dropped, never the descendants deleted with them. |
| `CommittedRowRanges` | The Excel rows of every committed parent row as maximal runs of consecutive rows in ascending order; a fully blank row never breaks a run. |
| `CommittedNewIds` | For every committed new row that a row at a later position references by its negative `Id`, that sheet value mapped to the id the row was committed with (§9.6); rows referenced only within their own chunk are not entered. |

Each background chunk's `SaveAsync` carries `SaveOptions.OnPersist` (spec 0014 §4.1), a delegate
that calls the job item's `Progress.Append(batch, percent, message, state)` with the
`Excel.Import.Saving` `JobMessage` and the chunk's new `ImportCheckpointState` — spec 0019's
fenced checkpoint (spec 0019 §5.3), which throws `50422 Job.LeaseLost` inside the transaction when
the lease is gone, so the chunk and its checkpoint commit or roll back together. The delegate
builds the new state with the ids the pipeline assigned to the chunk's new rows before the persist
(spec 0014 §6.4), adding the session's `LaterReferencedIds` for the chunk to `CommittedNewIds`
(§10.4), so the map commits with the chunk and the next chunk's `Resolve` receives it. A re-save of
§11.2 carries the same delegate; a synchronous import passes no `OnPersist`. A resumed job reads
the state back through `State<ImportCheckpointState>()` (spec 0019 §3.3).

### 12.6 Completion

On success the handler builds the `ImportOutcome` from the final checkpoint state — the counts and
`CommittedRowRanges` across every attempt — and the warnings, and enlists an update of the `Import`
row with the job frame (`EnlistSaveAsync`, spec 0014 §13.3): `ResultJson` (the outcome, §12.1),
`RowCount`, `ErrorCount = 0`, under `EnlistSaveOptions.Concurrency = Check` against the stamp the
claim delivered. Awaiting `host.PersistAsync()` on the injected `IOpenWriteHost` runs the row's
validation round and places the row on the completion batch, so it commits with the job outcome;
then the handler calls `NotifyOnSuccess` with a `NotificationRequest("core.import.completed", …)`
to the item's `Job.RequestedById` — arguments `fileName`, `rowCount`, `errorCount`,
`TargetResource = "core.Import"`, `TargetId = ImportId` — and `Succeed()`. On a deliberate failure
(pass-1 errors, a chunk's errors) it enlists the same update with `ResultJson` holding the errors
capped at `MaxReportedErrors`, `TotalErrors` and the checkpoint's `CommittedRowRanges` (empty when
no chunk committed) and with `ErrorCount = TotalErrors`, and awaits `host.PersistAsync()`; it then
appends `INotifier.Notify(batch.Batch, [request])` to the completion batch with a
`NotificationRequest("core.import.failed", …)` to the item's `Job.RequestedById` — arguments
`fileName` and `errorCode` (the first error's code), `TargetResource = "core.Import"`,
`TargetId = ImportId` — and completes the item `Fail(JobError("Excel.Import.Failed", { }))`, a code
with no arguments. The argument names of all three are spec 0020's catalogue entries; the
`fileName` of both import notifications is the uploaded workbook's `FileName` from the blob the
handler resolved (§7.1). The three notification types are `Mutable = false` (spec 0020): the user
cannot mute them, because the result is also reachable from the `imports` page and muting would
strand nothing, and the default stays on.

## 13. Access control and security

- `Export`/`ExportForImport` require `Read` on the stack and `InspectImport`/`Import` require
  `Save`, evaluated by the invoker before the method runs (§2.2); the read decision's filter is
  applied through the row source exactly as `query` and `get` apply it.
- The pipeline's pre-check runs on the hydrated rows (before images under `Read`, the `Save`-grant
  count beside them) and its post-check over `@tb{b}_saved` after the write. A row visible for
  reading but not writable fails a synchronous import with the pipeline's `ForbiddenException`
  (403), which names no rows (spec 0014 §6.6); in a background import it stops the job like a chunk
  error, reported as one `ImportError` at (sheet null, row null) carrying the exception's code
  (§11.3, §12.6).
- Reference resolution applies the target's read filter (§10.1); a hidden row is
  `ReferenceNotFound`, so the importer is never an existence oracle. Foreign keys arriving as raw
  ids through JSON are validated under the same rule by spec 0014, so Excel and JSON agree. A child
  sheet's parent reference is matched within the workbook, never against the database (§9.5).
- Ids from a sheet are never inserted: a positive `Id` on a same-source `Update` or `Upsert`
  selects a row that hydration (under `Read`) and the pre-check (under `Save`) verify; every other
  `Id` only links rows between sheets (§9.2, §9.5).
- The manifest is validated, never trusted (§5.3); the same-source gate is a mapping decision, not
  an authorisation.
- Package limits (§7.2), the shared-string budget and entry cap (§7.3), prohibited DTDs and
  external resolvers bound the parser; the temp files under `Tellma:ScratchPath` are delete-on-close
  and never named after user input.
- A staged import file is readable only by its uploader (spec 0016's rule), so `FileId` cannot
  name another user's upload.
- An `Export` row is saved only by the export handler's enlisted insert and an `Import` row only
  by `import/start`'s enlisted insert and the import handler's enlisted update (§12.1), under the
  authority of the frame that hosts it — `import/start`'s own securable on the request path, the
  job's run-as scope in the handlers (spec 0014 §13.3) — and the stacks register no `Save`
  securable, so no request can save one.
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
    public int ImportConcurrencyRetries { get; set; } = 2;
    public long MaxSynchronousImportFileBytes { get; set; } = 16L * 1024 * 1024;                // 16 MB
    public long MaxSynchronousImportUncompressedBytes { get; set; } = 512L * 1024 * 1024;       // 512 MiB
    public long MaxImportFileBytes { get; set; } = 100L * 1024 * 1024;                          // 100 MiB
    public long MaxImportUncompressedBytes { get; set; } = 2L * 1024 * 1024 * 1024;             // 2 GB
    public long MaxInMemorySharedStringBytes { get; set; } = 32L * 1024 * 1024;                 // 32 MB
    public int MaxSharedStringEntries { get; set; } = 16000000;
    public int MaxReportedErrors { get; set; } = 100;
    public int ExportFileRetentionDays { get; set; } = 7;
    public int ImportFileRetentionDays { get; set; } = 7;
}
```

Validated at startup (reported into the realised gate): every value positive except
`ImportConcurrencyRetries` (`≥ 0`); `MaxSynchronousExportRows` ≤ `MaxExportRows` ≤ 1,048,575;
`ImportChunkRows` ≤ `MaxAtomicImportRows`; `MaxSynchronousImportFileBytes` ≤ `MaxImportFileBytes` ≤
`MaxImportUncompressedBytes`; `MaxSynchronousImportFileBytes` ≤
`MaxSynchronousImportUncompressedBytes` ≤ `MaxImportUncompressedBytes`.

### 14.2 Error codes

`ExcelErrorCodes` (constants; hosts localize through the request culture): the closed set in
Appendix A. Codes are dotted PascalCase resource keys; the warnings are
`Excel.Import.UnconfiguredLanguageColumn` and `Excel.Import.IdentityColumnIgnored`; every other
code is an error.

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
| `tellma.excel.import.retries` | counter (chunk re-saves after a hydration-stamp conflict) | `entity` |
| `tellma.excel.sst.spilled` | counter | — |

Tag names as constants `EntityTag`, `ShapeTag`, `ModeTag`, `BackgroundTag`, `CodeTag`. No tenant
or user tags; tenant and user ids go to the log scope. Log events (`Tellma.Core.Excel` category):
`ExportCompleted` (entity, rows, ms), `ExportRejected` (entity, rows, cap), `ImportCompleted`
(entity, mode, inserted, updated, ms), `ImportFailed` (entity, mode, total errors, first code),
`ImportChunkCommitted` (import id, next row, rows), `ImportChunkRetried` (import id or none, first
row, attempt, conflicts), `ImportResumed` (import id, next row), `SharedStringsSpilled` (bytes) —
all at Information except `ImportFailed` (Warning).

## 15. Testing

Test projects mirror `src/`: `test/core/Tellma.Core.Tests/Excel/` (pure) and
`test/core/Tellma.Core.IntegrationTests/Excel/` (`Category=Integration`, LocalDB or the
Testcontainers SQL Server of spec 0011's fixture). No suite carries `Live=true`.

**Pure suite** (runs on every PR, Windows and Linux):

- The planner over spec 0011's fixture entities (`test/shared/Tellma.Testing.Entities`:
  `fixture.Widgets`, multilingual with three languages, with children `fixture.WidgetParts` and
  grandchildren `fixture.WidgetPartNotes`, the tree `fixture.Nodes`, the `long`-keyed
  `fixture.Shipments`): column sets, header labels per culture, number formats, sheet names at
  every depth with leading segments dropped, truncation and collision suffixes, the `Columns` and
  `Children` subset rules at every depth, reference-key defaults and overrides, surrogate fallback.
- Encoder/decoder round trips for every row of §3.1 including the 15-digit boundary for `long`
  and `decimal`, ISO 8601 with offsets, `TimeOnly` fractions; date systems (1900 and 1904 workbooks;
  serial 60 refused); every cell kind of §3.1 (shared, inline, rich text, `str`, cached formula,
  formula without a cached value, error cell).
- Number-format construction for `en-US`, `ar-SA` (`gc` and `uq`), `am-ET` (`et` → text with the
  manifest pattern), a custom culture with no LCID.
- Manifest write and read; the same-source gate, including a manifest from another deployment of
  the same application (`etpharma-staging` into `etpharma`) that is not same-source;
  `SchemaFingerprintMatches`; a fixed `ExcelContext.Now` giving a deterministic `ExportedAt` and
  file name; a hostile manifest (out-of-range indexes, unknown paths, wrong entity).
- Mapping across tenant-language permutations (`en, ar` → `ar, en`; a language the target lacks;
  technical paths); the header index (every candidate kind, normalisation, collisions, catalogue
  symbols, the disambiguated form); overrides by letter; suggestions; every plan column echoed as
  an override is a fixed point across `en, ar` → `ar, en`; `IdentityColumnIgnored` on a
  foreign-source and on a manifest-less file.
- Invalid mappings: every request fault refused with `BadRequestException` before the blob is read;
  `UnknownSheet`, `UnknownColumn` and `UnknownPath`; a plan with two `Duplicate` sheets.
- In-file uniqueness, in-chunk resolution, the stable topological order and `OrderHash`, cycle
  detection through self-references beyond the tree parent; links at depth 2 (negative, duplicate
  and blank `Id`s, `OrphanChildRow`, `OwnerSheetAbsent`); `IncompleteChildSheet` for a missing
  column and for a missing descendant sheet; `TooManyChildren` at depth 2 and `TooManyDescendants`;
  the coordinate map for every path shape of §10.4 at depth 2; error capping and `TotalErrors`.
- The workbook corpus under `Excel/Corpus/`: files saved by Excel, LibreOffice and Google Sheets
  (shared-string table present, `dimension` absent, renamed headers, inserted columns, a 1904
  workbook, a `veryHidden` sheet, an error cell, a formula without a cached value, an `.xlsm`);
  package-limit rejection with crafted zips (oversized part, external link, two workbook parts);
  the shared-string entry charge and `TooManySharedStrings` with a crafted `<si/>` flood.
- Option validation.
- Request validation: an export body mapped to exactly one `ExportSource` case (`Ids` beside a
  filter clause refused; an empty body mapped to `ByQuery` with every clause null; an empty `Ids`
  list the `ExportForImport` template); a display and an editable request each mapped to `ByQuery`
  with its own `IncludeInactive` (false by default, excluding inactive rows); `Ids` beside
  `IncludeInactive = true` refused; `ExportCollection` validation (unknown and repeated collections,
  non-editable paths); `ExportJobArguments` round-tripping through JSON on its `kind` for both
  cases.

**Integration suite** (`Category=Integration`; every PR, per spec 0010 §10):

- Export → import round trip of the fixture entities in all three modes with the tree and
  `fixture.Widgets` → `WidgetParts` → `WidgetPartNotes`, same-source and cross-tenant (two fixture
  tenants), asserting row and child counts at every depth, `IsActive` absent from the editable
  shape and true after `Insert`, `WriteOnceChanged` on `Update`; replacing a part deletes its
  notes; a same-source `Update` renaming `Code` updates the row; a copied row with a stale `Id` is
  `DuplicateRowKey`.
- Resolution under row-level security: a hidden reference is `ReferenceNotFound`; a readable but
  unwritable row fails a synchronous import with the pipeline's 403 and a background chunk with one
  error at (sheet null, row null).
- Concurrency: a row edited between hydration and persist is re-hydrated and saved on one retry
  (`tellma.excel.import.retries` = 1); a same-source `Stamp` older than the row conflicts at once;
  `IgnoreSheetStamps = true` saves it.
- Synchronous caps: a by-query export of `MaxSynchronousExportRows + 1` rows rejected at the first
  page (one round trip, no temp file) with a message naming `export/start` and no job enqueued;
  `TooLargeForSynchronous` with a message naming `import/start`.
- Background export through `export/start` and the job worker (spec 0019's test harness): the call
  answering `JobAccepted(JobId, null)`; the `Export` row inserted in the job's completion
  transaction with `JobId` set and its blob confirmed there; no progress of 100 reported by the
  handler; the notification; retention deleting the row and releasing the blob; a schedule-fired
  `core.export` run with `ForImportExportJobArguments` behaving identically, its row inserted with
  `Kind = ForImport`; the requester cancelling their own pending export.
- Chunked background import through `import/start` with `ImportChunkRows = 100`, the call answering
  `JobAccepted(JobId, ImportId)`: a tree whose new parent lands in chunk 1 and its child in chunk 2
  imports; a tree without a natural key whose new parent (negative `Id`) lands in chunk 1 and whose
  child, referencing it by `Parent.Id`, lands in chunk 2 imports, and does so across a simulated
  crash between the two chunks; a forced failure in chunk 2 reports `CommittedRowRanges` for the
  first chunk; a simulated lease loss during chunk 3 rolls back the chunk and its checkpoint, and
  `retry` resumes without re-applying a committed row (`tellma.excel.import.resumed`); a resume
  under a changed `ImportChunkRows` (100 → 37) commits every remaining row exactly once; a changed
  order is `ResumeMismatch`; `ResultJson` carries the outcome; `Atomic` runs one transaction, and
  above its ceiling fails the job with `TooLargeForAtomic`.
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
- **CI**: the pure suite on every PR on both operating systems; the integration suite on every PR
  (spec 0010 §10); the corpus checked in under `test/core/Tellma.Core.Tests/Excel/Corpus/`.
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
   never sees both; an empty request is the unfiltered query, and the empty-ids template falls out
   for free (§2.1).
4. **Fail at the cap, never truncate or promote; the background export and import are their own
   actions** — silent truncation is data loss the user cannot see, a mid-request switch to
   background would change the response shape, and a separate `/start` route gives each route one
   response shape and a static retry classification (§2.3, §4.2, §12.2).
5. **Encode by type, decode by mapped type, never by cell format** — homogeneous sortable columns,
   text fallbacks as the only lossless carrier for wide types, and no locale guesswork (§3.1).
6. **Per-column `[$-CCLLLL]` number formats; Ethiopian as text with the pattern in the manifest**
   — Excel renders Hijri and Um Al Qura natively and has no Ethiopian calendar (§3.2).
7. **`Id` and `Stamp` in the editable sheet, gated by the same-source manifest** — exact identity,
   change detection and the default row key for the dominant export-fix-import loop; on any other
   file `Id` only links rows between sheets (§5.1, §5.4, §9.2).
8. **One sheet per collection path at every depth, each row naming its owner by the owner's `Id`,
   and a hidden protected manifest sheet** — rectangular tables sort and paste cleanly; `Id` links
   work for new rows, owners without a natural key and nullable keys; a hidden sheet is the one
   manifest carrier every spreadsheet application round-trips (§5.2, §5.3, §9.5).
9. **Natural keys are the entity contract's; references may use any target property on request**
   — uniqueness is a database guarantee, the surrogate fallback is visible, and ambiguity is an
   error rather than a first-match guess (§6).
10. **Staged-blob intake with package limits before parsing** — one intake path shared with
    attachments, a local file that can be read twice, and zip bombs stopped by the central
    directory (§7.1, §7.2).
11. **Manifest → generated header index in every tenant language and English → technical path,
    with a human override; unmapped columns are errors** — unchanged files map with zero input,
    renamed files still map, and the classic silent "column ignored" loss cannot happen (§8.1).
12. **Blank means null; a present child sheet means the complete children of its owners; an absent
    one means untouched** — the save contract's own semantics, each statable in one sentence;
    replacing a collection requires every editable column and every descendant sheet
    (`IncompleteChildSheet`), so it never drops data the file did not carry (§9.4, §9.5).
13. **Hydration through `GetByIdsAsync`, every hydrated row saved under `Check`, and a
    hydration-window conflict retried by the codec** — a partial sheet touches only its columns, a
    concurrent edit is never overwritten, and a race no user can see never fails an import (§9.3,
    §11.2).
14. **Temporary negative ids from the codec, within a chunk** — the pipeline rewrites them in
    self-typed foreign keys and child parent keys, so in-chunk new rows need no reservation round
    trip; a row of an earlier chunk is committed and resolves by key, or by its negative `Id`
    through the checkpoint's `CommittedNewIds` (§9.6, §10.2, §12.5).
15. **Lookups as Queryex queries with a `KeySetRestriction` under the target's read filter, read
    together in one round trip** — row-level security composes only through `CompileQuery`;
    distinct-value TVPs make the cost O(distinct values) (§10.1).
16. **Chunked background commits with the checkpoint inside the chunk's transaction** — bounded
    transactions, honest committed-range reporting, and a position checkpoint that can never
    re-apply or skip a row, written through `SaveOptions.OnPersist` and `IJobProgress.Append`
    (§12.4, §12.5).
17. **`Export`/`Import` are server-owned entities on `Query | Details | Delete | Enlist` stacks with
    a self-scope criterion, saved only by enlisted saves** — "My exports" is a standard query, the
    row owns the blob and exists only when it has a file to own, retention is an ordinary delete,
    and `import/start` and both handlers write through the entities' own pipelines as participants
    of the frame that authorises them, so a handler's row commits with the job outcome and its blob
    confirms inside the completion transaction (§12.1–§12.6).
18. **`core.import` with `MaxAttempts = 1`, resumed only by an explicit `retry`** — a partially
    committed import never re-runs blindly (§12.4).
19. **No `Export` securable** — bulk extraction is `Read`; a separate action is one registry line
    away when wanted (§13, review flag 10).
20. **Typed job arguments, polymorphic on the export kind** — no `core.export` run has an `Export`
    row to read; its arguments alone name the shape and carry the typed request, so a run
    `export/start` enqueued and a schedule-fired run are one path (§12.1–§12.3).
21. **One round trip per page at any depth, and one for every lookup** — a page carries its child
    collections at every depth through captured keys and the importer reads all its lookups
    together, so neither collections nor reference columns multiply round trips (§1.4, §4.1, §10.1).
22. **`IgnoreSheetStamps`, never a concurrency override** — an import writes the whole hydrated row,
    so an override would revert columns the sheet never carried; ignoring the sheet's stamps keeps
    the hydration check and lets the sheet's columns win (§9.3, §11.2).

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
   same-source gate proves confusing despite the `IdentityColumnIgnored` warning (sandbox → live
   support load).
5. **Columns in an unconfigured language ignored with a warning** (§8.2) versus a hard
   `UnmappedColumn` error forcing the user to remove the column. Flips if silent-warning imports
   generate "where did my Arabic names go" tickets.
6. **Child sheet present ⇒ the complete children of every owner row present, at every depth**
   (§9.5; decision 12) — the sharpest edge in the design, guarded on replacement by
   `IncompleteChildSheet` — versus a per-row `Action` column (`Keep | Delete`) on child sheets.
   Flips on the first accidental mass deletion of children through a partial child sheet.
7. **References resolved under the target's read filter, and the same rule applied to raw
   foreign-key ids on the JSON path** (§10.1, §13) versus Excel-only strictness with JSON saves
   posting any id. Flips if a legitimate workflow needs to reference rows the caller may not read
   (a write-only lookup), which would call for a `Reference` action rather than reverting.
8. **Staging-only intake** (§7.1) — one extra round trip for tiny synchronous imports — versus a
   multipart convenience endpoint. Flips if agent clients (MCP `tellma_import`) find two calls
   burdensome; the codec does not change either way.
9. **Chunked background commits, 10,000-row chunks, checkpoint through `SaveOptions.OnPersist` and
   `IJobProgress.Append`** (§12.4, §12.5; decision 16) versus `Atomic` by default with a hard cap
   and no chunking, or 5,000-row chunks under the lock-escalation threshold, or a dedicated
   checkpoint column on the `Imports` row instead of `Jobs.StateJson`. Flips on lock-escalation
   contention measured in the integration suite at 10,000 rows, or on operators needing the
   checkpoint visible on the import row itself.
10. **No `Export` securable action** (§13; decision 19) versus an `Export` action in the securables
    registry so administrators can forbid bulk extraction to users who may read on screen. Flips
    on the first customer asking to restrict exports; the change is one line in the stack feature's
    securable contributor plus `Action = "Export"` on four `[ApiAction]`s.
11. **Errors workbook deferred** (Non-goals) — the upload echoed with an error column is the most
    usable channel for thousands of errors and fits the coordinate map. Flips when `TotalErrors`
    routinely exceeds `MaxReportedErrors` (100) in telemetry.
12. **The `Exports` row owns the export blob and one daily `core.file-retention` schedule deletes
    expired `Exports` and `Imports`** (§12.1) versus the notification owning the artifact with its
    own retention. Flips if "My exports" is not wanted as a page.
13. **`core.import` at `MaxAttempts = 1` with resume through the administrator's `retry`** (§12.4;
    decision 18) versus `MaxAttempts = 3` with automatic resume from the checkpoint. Flips if
    transient failures (a lost lease on a busy instance) leave too many imports waiting on an
    administrator; the checkpoint already makes automatic resume safe.

## Appendix A — Error codes

All codes are constants on `ExcelErrorCodes`; coordinates are as listed (`—` = null).

| Code | Sheet / Row / Column | Arguments | Condition |
|---|---|---|---|
| `Excel.Export.RowLimitExceeded` | — / — / — | `limit` (this code), `actual` (rows), `maximum` (the cap) | a sheet's query yields more than its cap (§2.3, §4.2); raised as `LimitExceededException(Limit = this code, Actual, Maximum)` (413) and the job error code of an overflowing background export (§12.3) |
| `Excel.Import.TooLargeForSynchronous` | — / — / — | `rows`, `maxRows`, `bytes`, `maxBytes` | a synchronous request above its limits (§2.3) |
| `Excel.Import.TooLargeForAtomic` | — / — / — | `rows`, `maxRows` | an `Atomic` background import above `MaxAtomicImportRows`, the parent ceiling (§2.3) or the stack's `MaxRowsPerSave`, found in pass 1 (§12.4); the job's outcome carries it as its one error |
| `Excel.Import.PackageTooLarge` | — / — / — | `bytes`, `max` | §7.2 |
| `Excel.Import.TooManySharedStrings` | — / — / — | `entries`, `max` | the shared-string part holds more than `MaxSharedStringEntries` entries (§7.3) |
| `Excel.Import.MalformedWorkbook` | sheet? / — / — | `reason` | package structure, XML, or a manifest naming another entity (§7.2, §8.1) |
| `Excel.Import.UnknownSheet` | sheet / — / — | — | a `SheetMappings` or `ColumnMappings` entry names a sheet the workbook lacks (§8.1) |
| `Excel.Import.DuplicateSheet` | sheet / — / — | `collection` | two sheets bound to one target by any mix of manifest, name and override; raised at both sheets (§8.1) |
| `Excel.Import.OwnerSheetAbsent` | child sheet / — / — | `collection`, `owner` | a present child sheet whose owner sheet is absent or ignored, or maps no `Id` column (§9.5) |
| `Excel.Import.UnmappedColumn` | sheet / 1 / column | `header`, `suggestions` | no mapping and `IgnoreUnmappedColumns = false` (§8.1) |
| `Excel.Import.UnknownColumn` | sheet / 1 / column | — | a `ColumnMappings` letter with no header cell, or on a sheet the plan ignores (§8.1) |
| `Excel.Import.UnknownPath` | sheet / 1 / column | `path` | an override `Path` that is not a column path of the sheet's entity (§8.3) |
| `Excel.Import.DuplicateColumn` | sheet / 1 / column | `path` | two columns map to one path (§8.1) |
| `Excel.Import.ServerOwnedColumn` | sheet / 1 / column | `path` | a mapped server-owned, derived, database-computed or excluded path (§8.1) |
| `Excel.Import.UnconfiguredLanguageColumn` | sheet / 1 / column | `language` | warning; the column is ignored (§8.2) |
| `Excel.Import.IdentityColumnIgnored` | sheet / 1 / column | `path` (`Id` or `Stamp`), `reason` (`ForeignSource`, `NoManifest` or `InsertMode`), `deployment`?, `sourceTenantId`? | warning: the column is mapped but not used as row identity or expected stamp — the file is not same-source (§5.4; plan and import) or the mode is `Insert` (§9.1; import only); an `Id` column still links rows between sheets (§9.5) |
| `Excel.Import.WriteOnceChanged` | sheet / row / column | `path` | a write-once value differs from the hydrated one on update (§9.3) |
| `Excel.Import.RowNotFound` | sheet / row / key column | `key`, `value` | an `Update` row key that resolves to nothing readable (§9.1); under `RowKey = "Id"`, a positive `Id` hydration does not return, or a negative or blank `Id` in `Update` (§9.2); a positive child `Id` that is not an existing child of its owner (§9.5); a missing-row concurrency conflict, or a row that vanished before the save (§11.2) |
| `Excel.Import.DuplicateRowKey` | sheet / row / key column | `value` | a duplicate row-key or `[Unique]`-column value in the parent sheet, or a duplicate `Id` on a sheet that owns a present child sheet, after §10.3 normalisation (§9.2) |
| `Excel.Import.IdKeyRequiresSameTenant` | — / — / — | `deployment`, `sourceTenantId` | `RowKey = "Id"` on a non-same-source file (§9.2) |
| `Excel.Import.SurrogateReferenceFromOtherTenant` | sheet / — / column | `entity` | a `Reference` column with `KeyKind = Surrogate` on a non-same-source file (§6.3); never a parent reference, which is matched within the workbook (§9.5) |
| `Excel.Import.ReferenceNotFound` | sheet / row / column | `entity`, `key`, `value`, `collation`? | no readable target row (§10.3) |
| `Excel.Import.ReferenceAmbiguous` | sheet / row / column | `entity`, `key`, `value`, `count` | more than one target row (§10.3) |
| `Excel.Import.KeyTooLong` | sheet / row / column | `max` | a key value above 450 characters (§10.1) |
| `Excel.Import.OrphanChildRow` | child sheet / row / parent reference column | `value` | a child row whose parent reference matches no `Id` in its owner sheet (§9.5) |
| `Excel.Import.IncompleteChildSheet` | child sheet / — / — | `collection`, `missingColumns`, `missingCollections` | a collection to be replaced (neither `Id` nor natural key matches its children, §9.5) whose sheet lacks an editable column of the child or whose descendant collections' sheets are not all present |
| `Excel.Import.ParentCycle` | sheet / row / self-reference column | `rows`, `path` | a cycle among new sheet rows through any self-typed reference — the tree parent or another — which no order can satisfy (§9.6) |
| `Excel.Import.TooManyChildren` | child sheet / row / parent reference column | `collection`, `max`, `count` | more rows under one owner row in a collection than its `MaxCount`, at any depth, at the first row beyond the cap; `collection` is the collection path (§8.1) |
| `Excel.Import.TooManyDescendants` | parent sheet / row / — | `rows`, `max` | more rows under one parent row, at every depth together, than the stack's `MaxRowsPerSave` (§8.1) |
| `Excel.Import.InvalidCell` | sheet / row / column | `expected`, `value` | any decode failure (§3.1) |
| `Excel.Import.ConcurrencyConflict` | sheet / row / `Stamp` column when the sheet's stamp was the expected stamp, else — | `modifiedAt`, `modifiedBy` | the row changed since the sheet's stamp, or since hydration on every attempt of §11.2's retry |
| `Excel.Import.ResumeMismatch` | — / — / — | `expected`, `actual` | a resumed job's recomputed `OrderHash` differs from its checkpoint's (§12.4) |
| `Excel.Import.Failed` | — / — / — | — | the `JobError.Code` of a failed background import (§12.6); `Imports.ErrorCount` carries the total |

Pipeline codes translated with coordinates keep their own names (`Required`, `MaxLength`,
`Unique`, `Fk.NotFound`, `Fk.InUse`, `Tree.Cycle`, `Tree.TooDeep`, `WriteOnce`, `Precision`,
`Import.LanguageNotConfigured`, `Entity.NotFound`, and every distribution validator code).
