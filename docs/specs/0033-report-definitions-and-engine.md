# Spec: Report Definitions and Engine

- **Author:** Ahmad Akra
- **Date:** 20 October 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Tellma reports come in two forms:

- **Stored report** — data. A stored JSON document, the *definition document*, over one queryable
  entity, in one of four kinds: list, summary, pivot or chart. Every run compiles it to Queryex.
- **Coded report** — code. A report the document cannot express, declared once on the server and
  returning the same result shape.

This spec ships the server side of both: the stack, the document, parameters, sharing and the
audience, the run engine, child query routes, coded reports, export and seeded reports. The client
half is spec 0034.

Sharing shares the report, never the data. Every run is the viewer's own query under the viewer's
own permissions, so a report shared with someone who cannot read its rows shows them nothing. The
sharing model is the platform's, not the reports': dashboards (spec 0035) use it too. Coded reports
are app features: a role grant on their own securable gives access, and they are never shared.

Other specs that carry parts of this design:

- spec 0010 §D.15, §F.8–§F.15, §F.17–§F.20 — number grouping, which `format.grouping` maps to; omitted parameters, constant statements, `BindParameters`, `'uq'`, `Exists`, `ComparedWith`, measure partials and their evaluation in C#; `startOfQuarter`, `QueryexVersion`, `'et'`, `MeasureCondition`.
- spec 0011 §2.1, §6.2 — `FeatureContribution.ReportSeed`, `CodedReportContributionItem` and the per-seed `core.report-seeds/<Code>` steps.
- spec 0012 §2.2, §3.3, §5.2, §6.4, §11.1 — `[Defaulted]`; `QueryColumn.Precision`; `IDataBatch.EvaluateConstant`; `ArithmeticFailureException`; the `'uq'` calendar map.
- spec 0013 §7.7, §8.1, §8.2, §10.1 — value sets, which name enums beside C# enums; `ICalendarSystem.MonthName`; `core.CalendarMonths`; the Core string keys of reports.
- spec 0014 §2, §5.1, §5.3 — `core.Report`, `Share`, `Publish`, `Transfer`; `AccessFilters.CurrentMembership` and `CurrentMembershipSql`; sharing among the bespoke criteria.
- spec 0015 §2.2, §2.3, §2.7, §5.2, §5.3, §11.3, §13.1, §14.1 — child query surfaces with their export, narrowed reserved segments, `Long` and `AllowWhenReadOnly`, child securables, the traversal rule and named filters, computed extras and the `permissions` extra, `IApiActionInvoker`, persist participants, the `forbidden` reasons.
- spec 0016 §2.1–§2.4, §3.2, §3.9, §3.10, §4.1, §6.1, §8.2, §8.3, §11.5, §11.8 — routes and retry classes, decimals as JSON strings, streams, the contracts chunk's long routes, `Queryable`, `Tellma-Priority`, `tellma-reports`, `tellma-long`, `tellma_query`, the reserved `tellma_report`.
- spec 0018 §1.4 — Acme's coded report, its seed and the role `Sales`.
- spec 0019 §2.1, §4.4, §12.2, §12.3 — `ExportKind.Report`, the table writer, the export job's report case and label.
- spec 0021 §2.3, §2.4 — the `core.report.shared` catalogue row; `LocalizedText`.
- spec 0024 §3.1, §3.2, §9.1 — the Core metadata chunk; coded-report keys.
- spec 0027 §3.2 — coded reports' launcher entries.
- spec 0029 §2.2, §6.1, §8.2, §9 — named enums in metadata, column keys, `tmQx.raw`, `queryex/discover` with its `measures` clause.
- spec 0030 §2.4, §13.2, §17 — tabs with named filters; image columns left out of exports; the drill-through list.
- spec 0035 §3.1, §6.3 — the widget foreign key; the `IDashboardReadThrough` implementation.

## Goals / Non-goals

**Goals**

- One versioned document per stored report, checked at save against the tenant's schema (§3, §4).
- Four kinds, each compiled to one Queryex statement; a pivot adds one member statement per tree
  dimension (§8). A pivot groups at leaf grain in SQL, and the cube builder rolls up trees,
  subtotals and totals in C# with the engine's measure evaluator.
- Parameters with defaults and display formats; an omitted optional parameter imposes no
  restriction (§4).
- Sharing with users, roles and everyone; an audience that says who sees a report and why (§5, §6).
- Self-describing results that renderers and the export writers read with no other input (§9).
- Reports over child entities (§10); coded reports declared once on the server, run and exported
  by key (§11).
- Export of every kind through one layout, to `.xlsx` and Markdown (§12); seeded standard reports
  (§13).

**Non-goals (explicitly out of scope)**

- Renderers, parameter bar, studio, reports page, drill-through list page, share dialog and
  `tmCodedReport` — spec 0034. Dashboards and widgets — spec 0035.
- Stored reports in the launcher — spec 0027 lists one Reports entry.
- Running totals, scheduled delivery, favourites and MCP report tools — later. `ReportTerm.running`
  is reserved (§3.1), and `run` with an inline report is the assistant's entry point today (§8.1).
  The MCP tool name `tellma_report` is reserved (spec 0016 §11.8).
- Distinct counts, medians and percentiles — Queryex has none, and none of them rolls up from
  partials. Spec 0010 §F.14 refuses any aggregate it cannot decompose.
- A tree cut at one depth as a flat dimension, such as sales by region in a chart — later. A pivot
  opens a tree collapsed with `expand: false`.

## 1. Placement and vocabulary

Reports add no project and no package reference. Contracts live in `Tellma.Core.Abstractions`, the
runtime lives in `Tellma.Core`, and `CoreFeature` composes both.

Contract blocks are TypeScript and C# sketches: names and shapes are normative; imports,
documentation comments, bodies and cancellation tokens are omitted.

- **Contracts** — `src/core/Tellma.Core.Abstractions/`.
  - Namespace `Tellma.Core.Abstractions.Reports`: the entities and document records; for running,
    the requests, results, `IReportEngine` and `IReportResults`; for coded reports,
    `CodedReportAttribute`, `ReportParameterAttribute`, `ICodedReportRegistry` and its records;
    `IDashboardReadThrough`; `ReportsOptions`, `ReportSeed`.
  - Namespace `Tellma.Core.Abstractions.Sharing`: the sharing model (§5, §6), so that a pack or a
    distribution can make its own entity shareable. It holds `IShareable`, `IShareRow`,
    `ShareAccess`, the action records, `IShareCommands<>`, `ShareableService<,>`, the
    `Shareable<,>()` extension, `IAudienceResolver` with its records, the `shares` extra's records
    and `SharingOptions`.
- **Runtime** — `src/core/Tellma.Core/Reports/`. The stack: `ReportService`, `ReportValidator`.
  Running and export: `ReportCompiler`, `CubeBuilder`, `ReportLayout`, `ReportExcelWriter`. Coded
  reports: `CodedReportRegistry`, `CodedReportInterceptor`, `CodedReportClientAssetContributor`.
  Seeds: `ReportSeedProvisioningStep`, one instance per seed. Sharing lives in
  `src/core/Tellma.Core/Sharing/`: `ShareableAccessCriteria<,>`, `ShareCommands<>`,
  `AudienceResolver`, the realiser of `Shareable<,>()` and its startup checks.
  `ReportMarkdownWriter` lives in `Tellma.Core.Mcp` (§12.4).
- **Composition** — `CoreFeature` registers `Entity<Report, ReportService>()`,
  `Shareable<Report, ReportShare>("core.report.shared")` (§5.1, §7), the seed steps (§13), the
  coded-report registry with its startup check and metadata section (§11), `ReportsOptions` bound
  from `Tellma:Reports` and `SharingOptions` bound from `Tellma:Sharing` (§14).
- **Tests** — `Reports/` and `Sharing/` folders in `test/core/Tellma.Core.Tests/` and
  `test/core/Tellma.Core.IntegrationTests/` (§15).

The document's C# records are the only code that reads or writes the document.
`JsonSchemaExporter` exports their schema.

**Vocabulary**

- *Stored report* — a `core.Reports` row. Its JSON is the *definition document*, or just
  *document*.
- *Coded report* — a report declared in C# with `[CodedReport]` or `contribution.CodedReport<T>()`,
  found by its key (§11).
- *Inline report* — a document sent to `run` or `export` with no stored row: the studio's working
  copy, the assistant's, or one declared in code with `tmInlineReport` (spec 0034 §2.1).
- *Run* — one execution of a stored, inline or coded report.
- *Root* — the entity a document queries, named by its `Root` column.
- *Term* — a column, dimension, measure or attribute of the document (§3.1).
- *Dimension* — a grouping term of a pivot or chart. *Measure* — an aggregate term.
- *Attribute* — a column beside a pivot's only row dimension, with one value per member.
- *Aggregate kinds* — summary, pivot and chart. A *grouping key* is an expression they group by.
- *Member* — of a tenant or a role: a user (§5, §6). Of a dimension: one of its values on an axis,
  such as one customer or one quarter (§8.5, §9.3).
- *Level* — one depth of an axis's member tree. A flat dimension has one; a tree has one per depth
  that holds facts.
- *Leaf row* — one row of a pivot's leaf statement: the facts of one combination of every
  dimension's finest key (§8.5).
- *Partial* — a `sum`, `count`, `min` or `max` column that a measure is computed from (spec 0010
  §F.14).
- *Phantom child* — the row of a tree member that holds the facts on the member itself (§8.6).
- *Fan-out* — a join that repeats a fact row once per match, inflating sums and counts.
- *Content slot* — slot 1, 2 or 3 of a multilingual value: `Title`, `Title2`, `Title3` (spec 0013 §7.5).
- *Decision* — spec 0014's answer for one securable: `Unrestricted`, `Filtered` or `Denied`.
- *Owner* — the user a stored report belongs to. *Grantee* — the user or role a share names, or
  everyone.
- *Standard report* — a seeded stored report with no owner (§13).
- *Audience* — who sees a stored report, and why (§6).
- *Child query surface* — spec 0015 §2.2's read-only `query` over a child entity (§10).

## 2. The stored report entity

`core.Reports` holds one row per stored report, and `core.ReportShares` holds its grants beside it
(§5). The stack's segment is `reports` and its resource is `core.Report`.

### 2.1 Classes and columns

```csharp
// Tellma.Core.Abstractions.Reports
[Table("Reports", Schema = "core"), TableType, Temporal]
[ApiResource(Segment = "reports"), Stack(Operations = Query | Details | Save | Delete | Enlist)] // Enlist: the seed steps (§13)
[DisplayNameSource(nameof(Title))]
public class Report : TopLevelEntity, IActivatable, IShareable
{
    [NaturalKey] public string? Code { get; set; }                  // a natural key, ≤ 50; seed codes are reserved (§13)
    [Multilingual, Searchable, Defaulted] public string Title { get; set; } // ≤ 255; empty: the default title
    public string? Title2 { get; set; } public string? Title3 { get; set; }
    [Multilingual] public string? Description { get; set; }         // ≤ 1,024
    public string? Description2 { get; set; } public string? Description3 { get; set; }
    public string Root { get; set; }                                // an entity name: 'acme.SalesInvoiceLine'
    public ReportKind Kind { get; set; }
    [JsonColumn] public string DefinitionJson { get; set; }         // the document (§3); ≤ 256 KB
    [Derived] public int QueryexVersion { get; set; }               // the Queryex version of its expressions
    [Derived] public int? OwnerId { get; set; }                     // IShareable (§5.1); null: a standard report
    [WriteOnce] public int? BasedOnId { get; set; }                 // IShareable; the report a "Save as" copied
    [ServerOwned] public bool IsActive { get; set; } = true;
}

[Table("ReportShares", Schema = "core"), TableType, Temporal]       // no [ApiResource], no [Stack]
public class ReportShare : Entity<int>, IShareRow
{
    public int ReportId { get; set; }                               // FK → core.Reports
    public int? UserId { get; set; } public int? RoleId { get; set; } // at most one; neither: everyone
    public ShareAccess Access { get; set; }
    public int SharedById { get; set; } public DateTimeOffset SharedAt { get; set; } // set by the action
}

public enum ReportKind { List, Summary, Pivot, Chart }
```

**`core.Reports`** — temporal (`core.ReportsHistory`), sequence `core.sq_Reports`.

| Column | Type | Null | Constraints and notes |
|---|---|---|---|
| `Id` | `int` | no | PK; `CK_Reports_Id CHECK ([Id] > 0)` |
| `Code` | `nvarchar(50)` | yes | `UX_Reports_Code WHERE [Code] IS NOT NULL` |
| `Title`, `Title2`, `Title3` | `nvarchar(255)` | `Title` no | content slots (spec 0013 §7.5) |
| `Description`, `Description2`, `Description3` | `nvarchar(1024)` | yes | |
| `Root`; `Kind` | `varchar(128)`; `varchar(8)` | no | `IX_Reports_Root`; `List` … `Chart` |
| `DefinitionJson`; `QueryexVersion` | `nvarchar(max)`; `int` | no | |
| `OwnerId` | `int` | yes | `FK → core.Users`; `IX_Reports_OwnerId (OwnerId) INCLUDE (IsActive)` |
| `BasedOnId` | `int` | yes | no FK: lineage only, and the source may be deleted later |
| `IsActive` | `bit` | no | `DF 1` |
| audit set, period | | | spec 0012 §4.2 |

**`core.ReportShares`** — temporal (`core.ReportSharesHistory`), sequence `core.sq_ReportShares`,
with `CK_ReportShares_Target CHECK ([UserId] IS NULL OR [RoleId] IS NULL)` and
`CK_ReportShares_EveryoneView CHECK ([UserId] IS NOT NULL OR [RoleId] IS NOT NULL OR [Access] = 'View')`.

| Column | Type | Null | Constraints and notes |
|---|---|---|---|
| `Id` | `int` | no | PK; `CK_ReportShares_Id CHECK ([Id] > 0)` |
| `ReportId` | `int` | no | `FK → core.Reports`; `IX_ReportShares_ReportId (ReportId) INCLUDE (UserId, RoleId, Access)`; `UX_ReportShares_Everyone (ReportId) WHERE [UserId] IS NULL AND [RoleId] IS NULL` |
| `UserId` | `int` | yes | `FK → core.Users`; indexed; `UX_ReportShares_User (ReportId, UserId) WHERE [UserId] IS NOT NULL` |
| `RoleId` | `int` | yes | `FK → core.Roles`; indexed; the `Role` twin of `UX_ReportShares_User` |
| `Access` | `varchar(8)` | no | `View` or `Edit` |
| `SharedById`; `SharedAt` | `int`; `datetimeoffset(3)` | no | `SharedById` `FK → core.Users` |

- **No version tag.** The client caches no stored reports. The server's plan cache keys a stored
  report by its `ModifiedAt` (§8.10).
- **No `Export` or `Import` operation.** The routes `export` and `export/start` therefore belong to
  the report service's own actions (§12.1, spec 0015 §2.3).
- **`QueryexVersion`.** The service sets it after the before image loads (spec 0015 §6.2). It is
  `QueryexLanguage.Version` when any expression changed; otherwise it keeps the stored value. The
  expressions are `filter`, `having`, each term's `expr`, each `orderBy` expression and each
  parameter `default`; paths and `drillColumns` are column keys. Save's compile check (§3.4 rule
  5) runs under the version about to be stored.
- **Default title.** `Title` is `[Defaulted]` (spec 0012 §2.2): a save may leave it empty, and
  `PreprocessAsync` fills it, so a quick ad-hoc report needs no name.
  - The text is `Core_Report_DefaultTitle`, "{entity} – {kind}", such as "Sales invoice lines –
    chart", where `entity` is the root's plural label. Each language pack chooses its own order and
    separator. The result is cut to 255 characters.
  - An empty `Title` fills `Title` and every other empty configured slot, each in that slot's
    tenant language, as seeds do (§13). A slot the user typed keeps its text.
  - When `Root` or `Kind` changes, a slot that still equals the before image's default is derived
    again. A validator does this after RT1, since it reads the before image (spec 0015 §6.2).
  - Titles need not be unique.
- **Shares live beside the row.** `ReportShare` is no child collection: it has no route, no child
  query surface and no client metadata, and it is not a valid `Root` (§3.4 rule 1). It stays in the
  Queryex schema (spec 0012 §11.1), so `Exists("core.ReportShare", "Report", …)` binds through its
  foreign key. Saves and shares never touch each other (§5.1, §5.4).
- **An inactive report still runs.** Deactivation hides it from the reports page, whose `query`
  skips inactive rows unless it sets `IncludeInactive`. `run` by id, and every dashboard that shows
  it, keep working.

### 2.2 Securables, operations and routes

Every route is under `/{t}/api/web/reports/` and requires one securable on `core.Report`.

- **Retry classes** are spec 0016 §2.2's.
- **Long routes.** `run`, `export` and `audience` declare `Long = true`. They take the
  `tellma-long` timeout, and the contracts chunk lists them as long (spec 0015 §2.3, spec 0016
  §8.3). They also take the `tellma-reports` policy (spec 0016 §8.2).
- **Sensitive pairs.** No pair is sensitive by default. A distribution may mark `Publish` or
  `Transfer` with `MarkSensitive`.

| Route | Kind | Securable (`core.Report` ×) |
|---|---|---|
| `query`, `get`, `get-by-ids` | standard; `query` takes the named filter `shared-with-me` (§5.5); `get` answers the `permissions` and `shares` extras (§5.2) | `Read` |
| `save`; `delete`, `delete-by-query` | standard | `Save`; `Delete` |
| `activate`, `deactivate` | built-in actions | `Activate` |
| `share`, `publish`, `transfer` | `[EntityAction]` (§5.4) | `Share`, `Publish`, `Transfer` |
| `run`, `export` | `[ApiAction]`, `Mutation = false`, `Idempotent`, `Long` (§8, §12); `export` streams `.xlsx` | `Read` |
| `audience` | `[ApiAction]`, `Mutation = false`, `Idempotent`, `Long` (§6) | `Share` |
| `export/start` | `[ApiAction]`, `Mutation = true`, `AllowWhenReadOnly`; `202 JobAccepted` (§12) | `Read` |

## 3. The definition document

The document is camelCase JSON. It holds a format version, parameters, a row filter, exactly one
kind block matching the `Kind` column, and drill-through columns. Save checks it in full (§3.4).

### 3.1 Shape

```ts
interface ReportDocument {
  formatVersion: 1;
  parameters?: ReportParameter[];            // §4
  filter?: string;                           // a Queryex predicate over Root, row level
  list?: ReportList; summary?: ReportSummary; pivot?: ReportPivot; chart?: ReportChart; // exactly one, matching Kind
  drillColumns?: string[];                   // column keys of Root (§3.3); summary, pivot and chart only
}
interface ReportTerm {                       // a column, dimension, measure or attribute
  path?: string;                             // a column key: scalar, group anchor, navigation, image or file
  expr?: string;                             // a Queryex value expression; exactly one of path and expr
  label?: string; label2?: string; label3?: string; // content slots 1–3, ≤ 255 each
  labelKey?: string;                         // a string-pack key instead of the three (seeds)
  display?: ReportDisplay;                   // §3.2; absent: the default display
  format?: ReportFormat;
  running?: never;                           // reserved for running totals
}
interface ReportFormat {                     // the parameters of a display
  decimals?: number; grouping?: boolean;     // Numeric; grouping absent: true, unless inferred (§3.2)
  dateStyle?: 'numeric' | 'medium' | 'long'; // date types; spec 0005 §2.2's names
  calendar?: 'gc' | 'uq' | 'et';             // display month, quarter, year, monthName; absent: §3.2
  enum?: string; choices?: ReportChoice[];   // display 'enum': a named enum's labels, or inline ones (§3.2)
}
interface ReportChoice {                     // one value's label under display 'enum'
  value: string | number | boolean;          // the term's value (§3.2)
  label?: string; label2?: string; label3?: string; labelKey?: string; // as a term's labels
}
type ReportDisplay = 'number' | 'integer' | 'percent' | 'monthName' | 'weekdayName' | 'text' | 'enum'
  | 'date' | 'month' | 'quarter' | 'year' | 'datetime' | 'instant' | 'bool';
type ReportOrder = { term: number; desc?: boolean }   // term: a 0-based index into columns
  | { expr: string; desc?: boolean };
interface ReportList { columns: ReportTerm[]; orderBy?: ReportOrder[]; top?: number }
interface ReportSummary {
  columns: ReportTerm[]; orderBy?: ReportOrder[]; having?: string; top?: number;
  totals?: boolean;                          // a totals row when the whole result fits on one page (§8.4)
}
interface ReportDimension extends ReportTerm {
  order?: 'asc' | 'desc';                    // member order; default asc
  expand?: boolean;                          // pivot: expanded when opened; default true
  tree?: boolean;                            // on a navigation to a tree entity; default true there; full depth
  attributes?: ReportTerm[];                 // pivot with one row dimension: columns before the measures
}
interface ReportPivot {
  rows: ReportDimension[]; columns: ReportDimension[]; measures: ReportTerm[];
  having?: string; rowTotals?: boolean; columnTotals?: boolean; // the grand total row; the grand total column
}
interface ReportChart {
  type: 'column' | 'bar' | 'line' | 'area' | 'pie' | 'doughnut' | 'card';
  dimensions: ReportDimension[];             // count per type (§3.4 rule 9)
  measure: ReportTerm;                       // exactly one, Numeric
  having?: string; top?: number; orderBy?: 'dimension' | 'measure'; order?: 'asc' | 'desc';
  options?: { stack?: 'none' | 'stacked' | 'normalized'; legend?: 'auto' | 'none' | 'top' | 'bottom' | 'start' | 'end';
    markers?: boolean; labels?: boolean; palette?: 'categorical' | 'sequential' | 'diverging';
    min?: number; max?: number; thresholds?: { value: number; tone: 'positive' | 'warning' | 'negative' }[] };
}
```

The C# records mirror the shape member for member, except the reserved `running`, which they leave
out so that `Disallow` refuses it:

```csharp
// Tellma.Core.Abstractions.Reports. Every record carries [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)];
// every enum member carries [JsonStringEnumMemberName] with the spelling above: MonthName → "monthName", DateTime → "datetime".
public sealed record ReportDocument(int FormatVersion, IReadOnlyList<ReportParameter>? Parameters, string? Filter,
    ReportList? List, ReportSummary? Summary, ReportPivot? Pivot, ReportChart? Chart, IReadOnlyList<string>? DrillColumns);
public record ReportTerm(string? Path, string? Expr, string? Label, string? Label2, string? Label3, string? LabelKey,
    ReportDisplay? Display, ReportFormat? Format);
public sealed record ReportDimension(string? Path, string? Expr, string? Label, string? Label2, string? Label3,
    string? LabelKey, ReportDisplay? Display, ReportFormat? Format, ReportSortOrder? Order, bool? Expand, bool? Tree,
    IReadOnlyList<ReportTerm>? Attributes) : ReportTerm(Path, Expr, Label, Label2, Label3, LabelKey, Display, Format);
public sealed record ReportFormat(int? Decimals, bool? Grouping, ReportDateStyle? DateStyle,
    string? Calendar, string? Enum, IReadOnlyList<ReportChoice>? Choices);       // Calendar: 'gc', 'uq' or 'et'
public sealed record ReportChoice(JsonElement Value, string? Label, string? Label2, string? Label3, string? LabelKey);
public sealed record ReportOrder(int? Term, string? Expr, bool? Desc);           // exactly one of Term and Expr
public sealed record ReportList(IReadOnlyList<ReportTerm> Columns, IReadOnlyList<ReportOrder>? OrderBy, int? Top);
public sealed record ReportSummary(IReadOnlyList<ReportTerm> Columns, IReadOnlyList<ReportOrder>? OrderBy,
    string? Having, int? Top, bool? Totals);
public sealed record ReportPivot(IReadOnlyList<ReportDimension> Rows, IReadOnlyList<ReportDimension> Columns,
    IReadOnlyList<ReportTerm> Measures, string? Having, bool? RowTotals, bool? ColumnTotals);
public sealed record ReportChart(ReportChartType Type, IReadOnlyList<ReportDimension> Dimensions, ReportTerm Measure,
    string? Having, int? Top, ReportChartOrderBy? OrderBy, ReportSortOrder? Order, ReportChartOptions? Options);
public sealed record ReportChartOptions(ReportStack? Stack, ReportLegend? Legend, bool? Markers, bool? Labels,
    ReportPalette? Palette, double? Min, double? Max, IReadOnlyList<ReportThreshold>? Thresholds);
public sealed record ReportThreshold(double Value, ReportTone Tone);
public sealed record ReportParameter(string Key, string? Label, string? Label2, string? Label3, string? LabelKey,
    bool? Required, string? Default, string? Type, string? Entity, ReportDisplay? Display, ReportFormat? Format); // §4.1

public enum ReportDisplay { Number, Integer, Percent, MonthName, WeekdayName, Text, Enum, Date, Month, Quarter, Year,
    DateTime, Instant, Bool }
public enum ReportDateStyle { Numeric, Medium, Long }
public enum ReportSortOrder { Asc, Desc }
public enum ReportChartType { Column, Bar, Line, Area, Pie, Doughnut, Card }
public enum ReportChartOrderBy { Dimension, Measure }
public enum ReportStack { None, Stacked, Normalized }
public enum ReportLegend { Auto, None, Top, Bottom, Start, End }
public enum ReportPalette { Categorical, Sequential, Diverging }
public enum ReportTone { Positive, Warning, Negative }
```

- **One options instance.** `TellmaJson.Options` (spec 0016 §3.1) reads and writes the document.
  The attributes above override its member handling and enum names for these types only, so
  `ReportResult` (§9) carries the same spellings as the document.
- **Strict.** `Disallow` refuses unknown JSON members, so a typo never drops a filter. The
  validator reports a binding failure under the rule that owns the member: `Report.UnknownMember`,
  `Report.ChartTypeUnsupported` for a reserved chart type, `Report.DisplayIncompatible` for an
  unknown display (§3.4).
- **Absent booleans** are false unless a comment says otherwise.
- **Format upgrades.** A newer format upgrades old documents in the migrator, never at read time.
- **A `path` is a column key** with spec 0029 §6.1's kinds: `value`, `text` (a group anchor),
  `navigation`, `image`, `file`. Save rewrites an `expr` that is lexically a bare path (spec 0008
  §4.2) as a `path`: `"expr": "Amount"` is stored as `"path": "Amount"`.
- **Labels** fill content slots 1–3 and show in the active slot, falling back to slot 1. A term
  with no label uses the path's slash label (spec 0029 §11.4) or the expression text.
- **Chart semantics** are spec 0034 §7's; this section fixes only the stored vocabulary. The names
  `heatmap`, `treemap` and `gauge` are reserved for later renderers and refused at save.

### 3.2 Display overrides

An override changes how a value renders, never the query. It must suit the term's Queryex type.

| Queryex type | `display` (type default first) | `format` members |
|---|---|---|
| `Numeric` | `number`, `integer`, `percent` (the value × 100 with `%`), `monthName` (1–12; 13 in Ethiopic), `weekdayName` (1 = Monday … 7 = Sunday), `enum` | `decimals` 0–6, `grouping`; `calendar` for `monthName`; `choices` for `enum` |
| `String` | `text`, `enum` | `enum` or `choices` for `enum` |
| `Date` | `date`, `month`, `quarter`, `year` | `dateStyle`; `calendar` for `month`, `quarter`, `year` |
| `DateTime`; `DateTimeOffset` | `datetime`, `date`; `instant`, `date` | `dateStyle` |
| `Bool`; `Guid` | `bool`, `enum`; `text` | `choices` for `enum`; — |

- **Navigation and text terms** take no override.
- **Default display.** A term's `path` or the outermost call of its `expr` chooses the display and
  the format members the term leaves absent, member by member. Any other absent display is the type
  default.

  | Path or outermost call | `display` | `format` |
  |---|---|---|
  | a path whose property names an enum | `enum` | `enum`: that enum |
  | `startOfYear`, `startOfQuarter`, `startOfMonth` (`d [, cal]`) | `year`, `quarter`, `month` | `calendar: cal` |
  | `month(d [, cal])`; `weekday(d)` | `monthName`; `weekdayName` | `calendar: cal` for `monthName` |
  | `year`, `quarter`, `day`, `week` | `integer` | `grouping: false` |

  An omitted `cal` infers `'gc'`, the calendar the expression groups by (spec 0008 §10.4).
  Defaults are never written to the document, so an edited expression takes its new default.
  `ReportColumn` carries the effective display and format (§9.1). `startOfQuarter` is spec 0010
  §F.17.
- **Default decimals.** An absent `decimals` sets both the minimum and the maximum: 0 under
  `integer`; otherwise the term's `Scale` (§9.1), or 6 when `Scale` is null (a floating result),
  less 2 under `percent`, kept within 0–6. A count is a `bigint` of `Scale` 0. `ReportColumn.Format`
  carries the effective value, so the screen, Excel and Markdown show the same digits. **Example —**
  `sum(Amount)` over `decimal(38,4)` shows 52,000.0000 unless the term states `"decimals": 0`.
- **`calendar`** names the calendar a month, quarter, year or month name renders in (§8.7). With
  none stated or inferred, a month name is Gregorian, since a month number carries no calendar of
  its own; a month, quarter or year is the viewer's. **Example —** a dimension
  `startOfMonth(d, 'uq')` infers `month` and `calendar: 'uq'`, so a viewer whose calendar is
  Gregorian still sees Umm Al-Qura month labels, and `startOfMonth(d)` keeps Gregorian labels for
  an Umm Al-Qura viewer.
- **`enum`** renders each value as a label, from exactly one source. `format.enum` names a *named
  enum*, a C# enum or a value set (spec 0013 §7.7), on a `String` term. `format.choices` lists labels
  inline:
  - A choice's `value` is in the platform scalar encoding of the term's type (spec 0016 §3.2). A
    `Numeric` value may also be a JSON number.
  - Its labels resolve as a term's do (§3.1).
  - A value matches by value: `Numeric` as a decimal, `String` ignoring case, as SQL groups it.
  - An unmatched value renders as it would without the override.
  - Labels never reorder: rows and members order by value (§9.3), so bands that must keep their
    order use numbers.

  **Example —** size bands in each language: `{ "expr": "if(Amount > 10000, 3, if(Amount > 1000,
  2, 1))", "display": "enum", "format": { "choices": [ { "value": 1, "label": "Small", "label2":
  "صغير" }, … ] } }`.

### 3.3 Drill-through columns

`drillColumns` lists the column keys of `Root` that the drill-through list shows (§9.4), at most 50.
When it is absent, the list shows the root's default columns (spec 0030 §2.1). A list kind has none:
its rows open their own record instead.

### 3.4 Validation

The validator checks the whole document against the tenant's schema at save. It needs no database
round trip: the schema and metadata are in memory, and the engine compiles without the database.

- **Error shape.** A failure is a `ValidationException` (spec 0015 §7.3) at `[i].DefinitionJson`.
  Its arguments are `pointer`, an RFC 6901 pointer into the document such as `/pivot/rows/0`, plus
  the rule's own arguments.
- **Codes** are Core string keys. The QX codes below are engine diagnostics, reported through
  rule 5.

1. **Root.** `Root` names a stack with `Query`, or a child query surface (§10): `Report.UnknownRoot`.
2. **Kind block.** The document holds exactly the block that `Kind` names: `Report.KindMismatch`.
3. **Counts** (`Report.TermCount`):
   - a list has 1–100 columns, and a summary 1–50 terms;
   - a pivot has 0–5 row dimensions, 0–3 column dimensions and 1–20 measures, and not both axes
     are empty;
   - `drillColumns` holds at most 50 keys.
4. **Terms.** Each term has exactly one of `path` and `expr`. A `path` must resolve
   (`Report.UnknownPath`). `image` and `file` keys appear only in a list
   (`Report.BlobTermNotInList`).
5. **The compile check.** The validator compiles the document as a run would (§8), then discards
   the SQL. It declares every parameter, omits none, and applies `ReportLimits`. Each engine
   diagnostic becomes `Report.InvalidExpression`, with the arguments `code`, `start`,
   `length` and the engine's own.
6. **Roles.** Each term fits its role:
   - list terms are row level (QX4002);
   - summary terms are grouping keys or measures (QX4007), and at least one is either
     (`Report.TermCount`). A summary of keys alone lists their distinct combinations.
     `totals: true` needs a measure (`Report.TotalsNeedMeasure` at `/summary/totals`);
   - dimensions are grouping keys, and measures aggregate;
   - pivot measures, and summary measures when `totals` is true, compile as `Measures` (spec 0010
     §F.14). A construct that C# cannot evaluate over rolled-up values is QX4010. A measure that
     reads a path outside an aggregation, or has no aggregation at all, is QX4011;
   - attributes are grouping keys whose paths start with their dimension's path
     (`Report.AttributeNotOfDimension`), allowed only when the pivot has exactly one row
     dimension (`Report.AttributesNeedOneRow`).
7. **`tree: true`** appears only on a navigation to a tree entity (`Report.NotATree`).
8. **`having` and `top`.** `having` appears only on a summary, pivot or chart
   (`Report.HavingNotAllowed`). `top` is 1–10,000 on a list and 1–1,000 on a summary or
   chart (`Report.TopOutOfRange`).
9. **Chart.** `pie` and `doughnut` take 1 dimension; `column`, `bar`, `line` and `area` take 1 or
   2; `card` takes none (`Report.ChartDimensions`). The measure is `Numeric`
   (`Report.ChartMeasureNotNumeric`), with the display `number`, `integer` or `percent`
   (`Report.DisplayIncompatible`): an axis cannot plot labels. A reserved type (§3.1) is
   `Report.ChartTypeUnsupported`. Tree dimensions are flat in charts.
10. **`orderBy`** is a term index or an expression. In a summary it is a grouping key or a measure
    (QX4005).
11. **Display.**
    - `display` follows §3.2 (`Report.DisplayIncompatible`).
    - `display: 'enum'` takes exactly one of `format.enum` and `format.choices`, and neither
      appears without it (`Report.EnumSourceInvalid`).
    - `format.enum` names a known named enum (`Report.UnknownEnum`).
    - `format.choices` holds 1–100 entries with labels of at most 255 characters. Their values
      convert to the term's type and are unique, text ignoring case (`Report.ChoiceInvalid`,
      with the argument `index`, at a pointer such as `/summary/columns/1/format/choices/3`).
    - `drillColumns` are paths of `Root` (`Report.UnknownPath`).
12. **The rest.**
    - Parameters follow §4.5.
    - Unknown JSON members, and the reserved `running`, are refused
      (`Report.UnknownMember`).
    - A higher format version is refused (`Report.FormatUnsupported`).
    - A document over 256 KB is refused (`Report.TooLarge`).

### 3.5 Examples

**Example — summary** over Acme's child entity `acme.SalesInvoiceLine` (owner navigation
`Invoice`, spec 0018 §1.4): sales by customer this year, the top 50, with totals. Later examples
store it as report 42.

```json
{ "formatVersion": 1,
  "parameters": [ { "key": "From", "label": "From", "label2": "من", "required": true, "default": "startOfYear(today())" },
                  { "key": "To", "label": "To", "label2": "إلى", "type": "Date" } ],
  "filter": "Invoice.PostingDate >= @From and Invoice.PostingDate <= @To",
  "summary": { "columns": [ { "path": "Invoice.Customer" },
                            { "expr": "sum(Amount)", "label": "Sales", "label2": "المبيعات" },
                            { "expr": "count()", "label": "Lines", "label2": "البنود" } ],
               "orderBy": [ { "term": 1, "desc": true } ], "having": "sum(Amount) > 0", "top": 50, "totals": true },
  "drillColumns": [ "Invoice.SerialNumber", "Invoice.PostingDate", "Center", "Amount" ] }
```

Save infers and adds `"type": "Date"` to `From`, because the filter compares it with a date (§4.2).

**Example — pivot:** sales by center (a tree) and quarter, the seed `acme.sales-by-center` (§13).

```json
{ "formatVersion": 1,
  "parameters": [ { "key": "Year", "labelKey": "Acme_Report_Year", "required": true,
                    "default": "year(today())", "type": "Numeric",
                    "display": "integer", "format": { "grouping": false } } ],
  "filter": "year(Invoice.PostingDate) = @Year",
  "pivot": {
    "rows": [ { "path": "Center", "expand": false, "attributes": [ { "path": "Center.Code" } ] } ],
    "columns": [ { "expr": "quarter(Invoice.PostingDate)", "labelKey": "Acme_Report_Quarter" } ],
    "measures": [ { "expr": "sum(Amount)", "labelKey": "Acme_Report_Sales", "format": { "decimals": 0 } },
                  { "expr": "count()", "labelKey": "Acme_Report_Lines" } ],
    "rowTotals": true, "columnTotals": true } }
```

## 4. Parameters

A parameter is a named input that the document's expressions reference as `@Key`. It declares
labels, whether it is required, a type, an optional default and a display. A run's arguments
supply the values, and an omitted optional parameter imposes no restriction (§4.4).

The `type` list of §4.1 and the displays of §4.6 are the platform's only parameter vocabulary:
coded reports' `[ReportParameter]` members (§11.1) and dashboard parameters (spec 0035 §10.1) use
them too.

### 4.1 Shape

```ts
interface ReportParameter {
  key: string;                               // ^[A-Za-z][A-Za-z0-9_]{0,63}$; matched ignoring case, as Queryex does
  label?: string; label2?: string; label3?: string; labelKey?: string; // default label: the key
  required?: boolean;
  default?: string;                          // a Queryex value expression with no path and no parameter (§4.3)
  type?: 'Numeric' | 'String' | 'Bool' | 'Date' | 'DateTime' | 'DateTimeOffset' | 'Guid'; // stored after save (§4.2)
  entity?: string;                           // stated, or inferred by save (§4.2 step 5): the entity whose key it is
  display?: ReportDisplay; format?: ReportFormat; // §4.6: the control and the text of its value
}
```

### 4.2 Inference and types

Save infers each parameter's type from how the expressions use it. The studio proposes parameters
through `queryex/discover` (spec 0029 §9). The validator runs the same discovery over every clause
and normalizes the document:

1. A referenced parameter missing from the list is added: optional, labelled with its key.
2. A listed parameter that no expression references is removed, since spec 0008 §6.3 forbids
   declaring it.
3. An absent `type` takes the inferred type. If inference leaves the type unconstrained and no
   `type` is stated, the error is `Report.ParameterTypeRequired`. Conflicting inferences
   (QX3400) are an error.
4. A stated `type` must equal the inferred one, unless inference is unconstrained
   (`Report.ParameterTypeIncompatible`). Queryex never coerces parameters (spec 0008
   §7.3).
5. `entity` follows the type's rule. Absent, it takes the entity that `ComparedWith` (spec 0010
   §F.13) implies when the compared path ends at a key or a foreign key: `CustomerId = @Customer`
   gives `acme.Customer`. Stated, it must equal that entity unless none is implied
   (`Report.ParameterEntityIncompatible`), and it names a top-level entity with `Query`
   (`Report.ParameterEntityInvalid`). Stating it serves the shapes inference cannot see,
   such as a parameter used inside `Exists` or compared with two paths.

At run time, every referenced parameter is declared with its stored `type`, as nullable. The
compiled query therefore never depends on inference.

### 4.3 Defaults

A default fills a parameter the request leaves out. The run evaluates it on the server, once, just
before the report itself.

- **Grammar.** A Queryex value expression with no path (QX4009) and no parameter, such as
  `today()`, `startOfYear(today())`, `addMonths(today(), -1)`, `startOfMonth(today(), 'uq')`,
  `me()`, `'East'` or `5`. It is checked against the parameter's type; a string literal coerces to
  a date type (spec 0008 §7.3).
- **When it applies.** An argument absent from the request takes the default; an explicit `null`
  never does (§4.4). On a dashboard, a required parameter needs a default, a non-null fixed-value
  binding or a required dashboard parameter (spec 0035 §5.3).
- **Evaluation.** Before the report statements, the run executes one constant statement that holds
  every applied default, through `IDataBatch.EvaluateConstant` (spec 0012 §5.2, spec 0010 §F.9).
  It binds the results as ordinary arguments and echoes them with source `Default` (§9.1).
- **One clock, one user.** `today()` and `me()` bind from the same request context in both
  statements, so a default and the query agree.

### 4.4 Arguments and omission

`ReportRunRequest.Arguments` is a JSON object keyed by parameter key, matched ignoring case.

| The request carries | Optional parameter | Required parameter |
|---|---|---|
| a value | that value | that value |
| `null` | omitted | `422 Report.ArgumentRequired` at `arguments.<Key>` |
| nothing, a default exists | the default | the default |
| nothing, no default | omitted | `422 Report.ArgumentRequired` |

- **Values** use the platform scalar encoding of the declared type (spec 0016 §3.2): for example
  `"2026-10-01"` for a `Date`, and `"5000"` for a `Numeric`, which travels as a `decimal`. A
  `Numeric` argument may also arrive as a JSON number. Either way, its echo is the string (§9).
- **URL text.** A value may also arrive as its URL text (spec 0034 §4.4) in a JSON string:
  `"true"` or `"false"` for a `Bool`, and a `DateTime` or `DateTimeOffset` with 0–7 fractional
  digits. The client therefore sends a link's arguments before it knows their types.
- **Bad values and keys.** A value that does not convert is `422 Report.ArgumentInvalid`, with the
  arguments `key` and `type`. A key that no parameter declares is ignored. A value outside a
  parameter's choices is accepted: a display never changes the query (§3.2).
- **Omitted means no restriction.** The run's `QueryArguments` marks the parameter omitted (§8.3
  step 5), which the compile reads as `OmittedParameters` (spec 0010 §F.8).
  In a filter, a `having` or a measure's condition, every atom (one comparison, such as
  `CustomerId = @Customer`) that mentions an omitted parameter folds to no restriction:
  `sum(Amount, CustomerId = @Customer)` becomes `sum(Amount)`.
- **Elsewhere,** an omitted parameter is a typed absent value: `if(Amount > @Limit, 'Big', 'Small')`
  gives `'Small'`.
- **URL form.** Spec 0034 §4.4 carries arguments in the URL.

### 4.5 Parameter validation (save)

- **Keys** match §4.1's grammar (`Report.ParameterKeyInvalid`) and are unique ignoring
  case (`Report.ParameterKeyDuplicate`).
- **Count.** At most 20 parameters (`Report.TermCount`).
- **Types** follow §4.2.
- **Defaults.** A `default` compiles as a constant statement item against the type. Its
  diagnostics point at `/parameters/<i>/default`.
- **`entity`** sits only on a `Numeric` parameter (`Report.ParameterEntityNotNumeric`)
  and follows §4.2 step 5.
- **Display and format** follow §4.6, with §3.4 rule 11's codes at `/parameters/<i>/display` and
  `/parameters/<i>/format`.

### 4.6 Display

A parameter's display chooses its control (spec 0034 §4.1) and the text of its value, as a term's
does (§3.2). Save infers none: the studio proposes displays as explicit values.

| Type | `display` (default first) | `format` |
|---|---|---|
| `Numeric` | `number`, `integer`, `percent`, `monthName`, `weekdayName`, `enum` | `decimals`, `grouping`; `calendar` for `monthName`; `choices` for `enum` |
| `Numeric` with `entity` | none: the entity picker | none |
| `String` | `text`, `enum` | `enum` or `choices` for `enum` |
| `Bool` | `bool`, `enum` | `choices` for `enum` |
| `Date` | `date` | `dateStyle`; `calendar`: the picker's calendar, else the viewer's |
| `DateTime`, `DateTimeOffset`, `Guid` | none | none |

**Example —** a `Year` parameter takes `"display": "integer", "format": { "grouping": false }`, so
it reads 2026, never 2,026 (§3.5).

## 5. Sharing and visibility

A stored report is visible to its owner, to the users and roles it is shared with, to everyone when
it is shared with everyone, and to holders of a role grant on `core.Report`. The model is generic
over an entity and its share rows: dashboards use it (§5.7), and so may a pack's or a
distribution's own entity. Coded reports never take part (§11).

### 5.1 The shareable contract

```csharp
// Tellma.Core.Abstractions.Sharing
public interface IShareable                                 // on a TopLevelEntity
{
    int? OwnerId { get; set; }                              // [Derived]; null: a standard row
    int? BasedOnId { get; set; }                            // [WriteOnce]
}
public interface IShareRow                                  // on the share entity beside it, an Entity<int>
{
    int? UserId { get; set; } int? RoleId { get; set; }     // at most one is set; neither: everyone
    ShareAccess Access { get; set; }
    int SharedById { get; set; } DateTimeOffset SharedAt { get; set; }
}
public enum ShareAccess { View, Edit }

// the base of every shareable stack's service; applies the ownership rules below. Like EntityService,
// it receives its runtime collaborators through its constructor.
public abstract class ShareableService<TEntity, TShare> : EntityService<TEntity, int>
    where TEntity : TopLevelEntity, IShareable where TShare : Entity<int>, IShareRow;

// FeatureContribution extension over a ShareableContributionItem, which Tellma.Core realises. Registers the
// criteria (§5.3), IShareCommands (§5.4), the shares extra (§5.2), the named filter shared-with-me (§5.5),
// the audience source (§6) and the notification type (§7), and runs the startup checks below.
public static FeatureContribution Shareable<TEntity, TShare>(this FeatureContribution contribution, string notificationType)
    where TEntity : TopLevelEntity, IShareable where TShare : Entity<int>, IShareRow;

public sealed class SharingOptions { public int MaxAudienceMembers { get; init; } = 2000; } // Tellma:Sharing (§14)
```

- **Startup checks.** `TShare` has exactly one foreign-key navigation to `TEntity`, and it is
  neither a stack nor a child collection. The entity's service derives from
  `ShareableService<TEntity, TShare>`.
- **Owner.** A new row takes the caller's user id, or null in a system scope (§13). An existing row
  keeps its before image's value. Only `transfer` writes a new owner.
- **Shares are not part of the row.** A save neither reads nor writes them, so a details payload
  sent back unchanged saves and the shares stay as they are.
- **Standard rows are read-only.** Outside a system scope, a row with no owner refuses save,
  delete, activate, deactivate and `transfer` with `Sharing.StandardReadOnly` (argument `entity`).
  `share` and `publish` are allowed, so an administrator shares a seed like any other report.
- **Lineage.** `BasedOnId` must name a row the caller can read (`Sharing.BasedOnInvisible`).

### 5.2 Who may do what

Each ability is one securable on `core.Report`. The bespoke criterion (§5.3) grants it to the
people in the third column; role grants give it to others.

| Ability | Securable | Granted by the bespoke criterion to | Typical role grants |
|---|---|---|---|
| See and run | `Read` | the owner and any grantee, the Everyone grant included | administrators: unfiltered `Read` sees all |
| Edit, Save as | `Save` | the owner; `Edit` grantees | report managers |
| Create | `Save` | every member (the caller owns the new row) | — |
| Delete, deactivate | `Delete`, `Activate` | the owner | administrators |
| Share with users; read the audience and the grants | `Share` | the owner | administrators |
| Share with roles and everyone | `Publish` | — | administrators, module leads |
| Change the owner | `Transfer` | — | administrators |

- **`Edit` does not confer re-sharing.** A role grant of `Publish` filtered by `OwnerId = me()`
  lets a role's members publish their own reports. A rule such as "only to roles I belong to"
  cannot be expressed.
- **Record permissions.** The platform's `permissions` extra (spec 0015 §5.3) answers the abilities
  `save`, `delete`, `activate`, `action:share`, `action:publish` and `action:transfer` per row.
  `ShareableService.RefuseOnRecord` refuses a standard row's `save`, `delete`, `activate` and
  `action:transfer` with `Sharing.StandardReadOnly`, and leaves `action:share` and
  `action:publish` open.
- **The `shares` extra.** A computed extra (`DetailsPlan.AddComputedExtra`, spec 0015 §5.3) that
  lists a row's grants:

  ```csharp
  // Tellma.Core.Abstractions.Sharing
  public sealed record ShareList(int Id, IReadOnlyList<ShareGrantView> Grants);
  public sealed record ShareGrantView(int Id, int? UserId, int? RoleId, ShareAccess Access, int SharedById, DateTimeOffset SharedAt);
  ```

  - It answers only for visible ids whose caller's `Share` or `Publish` decision admits the row.
    Other ids get no entry, never a 403.
  - It costs one statement in the details batch under those decisions, and none when both are
    `Denied`. User and role display projections come through `Related`.
  - Only the share dialog asks for it (spec 0034 §10.4), so readers of a report never see who else
    holds it.

### 5.3 The access criteria

`ShareableAccessCriteria<TEntity, TShare>` is the `IAccessCriteriaProvider` (spec 0014 §5.1) for
the entity's resource. Its criteria are constant filter trees: no per-user list and no database
read. The SQL is byte-identical for every user; only `me()` and `now()` bind per request.

```text
Read  : Or( Leaf("OwnerId = me()"), Exists(Share, Row, Grantee) )
Save  : Or( Leaf("OwnerId = me()"), Exists(Share, Row, And( Leaf("Access = 'Edit'"), Grantee )) )
Delete, Activate, Share : Leaf("OwnerId = me()")
Grantee = Or( Leaf("UserId is null and RoleId is null"),                       // the Everyone grant
              Leaf("UserId = me()"),
              Via("Role", And( Leaf("IsActive = true"),
                               Or( Leaf("IsPublic = true"),
                                   Exists("core.RoleMembership", "Role",
                                          And( Leaf("UserId = me()"), AccessFilters.CurrentMembership )) ))) )
```

- **In words.** A user reads a report they own, or one with a share that names everyone, them, or
  an active role that is public or that they currently belong to. They save one they own, or one
  where such a share has `Edit` access.
- **`Exists`** is spec 0010 §F.12's semi-join, so nothing fans out. `Share` is the share entity
  (`core.ReportShare`), and `Row` is its foreign-key navigation to the row (`Report`).
- **Current memberships.** `AccessFilters.CurrentMembership` is spec 0014's one window predicate
  over a role membership, evaluated with `now()`, so an expired or future membership grants
  nothing.
- **Reason.** Every criterion carries the reason `share`, so `access/check` (spec 0014 §10) answers
  "via sharing".
- **Everyone can start.** Every member's `Read` and `Save` decisions are `Filtered`, so the Reports
  entry and the New button show for all.

### 5.4 The sharing actions

`share` grants users, `publish` grants roles and everyone, and `transfer` changes the owner. Each
service declares the three actions and delegates them to `IShareCommands`.

```csharp
// ReportService : ShareableService<Report, ReportShare>
[EntityAction("share", Action = "Share", Description = "Shares reports with users.")]
public Task ShareAsync(ActionContext<Report, int> context, ShareArguments arguments);
[EntityAction("publish", Action = "Publish", Description = "Shares reports with roles or everyone.")]
public Task PublishAsync(ActionContext<Report, int> context, PublishArguments arguments);
[EntityAction("transfer", Action = "Transfer", Description = "Changes the owner of reports.")]
public Task TransferAsync(ActionContext<Report, int> context, TransferArguments arguments);

// Tellma.Core.Abstractions.Sharing
public sealed record ShareArguments(IReadOnlyList<UserGrant>? Set, IReadOnlyList<int>? Remove);   // Remove: user ids
public sealed record PublishArguments(IReadOnlyList<RoleGrant>? Set, IReadOnlyList<int>? Remove, bool? Everyone); // Remove: role ids; Everyone: the Everyone grant
public sealed record UserGrant([property: EntityRef("core.User")] int UserId, ShareAccess Access);
public sealed record RoleGrant([property: EntityRef("core.Role")] int RoleId, ShareAccess Access);
public sealed record TransferArguments([property: EntityRef("core.User")] int OwnerId);

public interface IShareCommands<TEntity> where TEntity : TopLevelEntity, IShareable // registered by Shareable<,>()
{
    Task ShareAsync(ActionContext<TEntity, int> context, ShareArguments arguments);
    Task PublishAsync(ActionContext<TEntity, int> context, PublishArguments arguments);
    Task TransferAsync(ActionContext<TEntity, int> context, TransferArguments arguments);
}
```

- **Merge, never replace.** `Set` adds a grant or changes its access, and `Remove` deletes one.
  `Everyone: true` adds the Everyone grant, and `false` removes it. Other grants are untouched, so
  two people sharing at once never overwrite each other. Every target row gets the change.
- **Writes** (spec 0015 §11.2). The targets' existing grants load through `context.Loader`. The
  writes go through `ActionContext.Batch`: one `DELETE` for the removals and one `MERGE` over a
  table-valued parameter for the grants set, each declaring the share table. `share` and `publish`
  make no `Save` and no `Update`, so they never stamp the row. A new grant takes `SharedById` = the
  caller and `SharedAt` = the request's `Now`. `transfer` writes `OwnerId` through
  `ActionContext.Update` and stamps, because it changes the row itself.
- **Validation.**
  - A user grantee is an active `Individual` member (`Sharing.UserInvalid`).
  - A role grantee is an active role (`Sharing.RoleInvalid`).
  - The owner is never a grantee (`Sharing.OwnerIsImplicit`).
  - The Everyone grant is `View` only (`CK_ReportShares_EveryoneView`).
  - A row holds at most 200 user grants and 50 role grants (`Sharing.TooManyGrants`).
  - Grantees who cannot read the root are allowed; the audience counts them (§6).

  These are grant rules: access still reaches service accounts, through their roles (§6.2).
- **`transfer`.** The new owner is an active `Individual` member (`Sharing.OwnerInvalid`). Their
  user grant, if any, is removed, and the old owner keeps nothing.
- **Notification.** A newly added user grant notifies the grantee (§7).

### 5.5 The reports page's tabs

The reports page reads `reports/query` like any search page (spec 0030 §2.4). Its tabs
(`?tab=mine|shared|all`) each add one filter, joined by `and` to the query's filters and the access
filter:

| Tab | Filter |
|---|---|
| Mine | the client leaf `OwnerId = me()` |
| Shared with me | the named filter `shared-with-me` (spec 0015 §3.2, §5.2): `And( Or( Leaf("OwnerId is null"), Leaf("OwnerId != me()") ), Exists(Share, Row, Grantee) )`. Standard rows shared with the caller are included. |
| All | none: everything the caller can read; for an administrator, every stored report |

- **`shared-with-me`** is contributed by `Shareable<,>()` to every shareable stack through
  `contribution.NamedFilters<,>()` (spec 0015 §3.2), as a constant tree with no value from the
  caller.
- **Inactive reports** are hidden unless the query sets `IncludeInactive`.
- **Coded reports** are not rows. They appear only in the launcher (§11), never on this page.

### 5.6 Lifecycle and delete

- **Delete removes the report for everyone,** shares included, after the host's plain
  confirmation (spec 0034 §10.3). Deactivation is the soft path. `ShareableService` deletes the
  share rows ahead of the row itself, as a persist participant (spec 0015 §13.1), for `delete`
  and `delete-by-query` alike.
- **Save as** is a client create with `BasedOnId`. No share is copied.
- **A dashboard widget blocks the delete.** `FK_DashboardWidgets_ReportId` is `NO ACTION`
  (spec 0035 §3.1). Before the persist, the service calls
  `IDashboardReadThrough.DashboardsUsing(ids)` and throws a `ValidationException` (422) with
  `Report.InUse` at `ids[i]` for each report that a widget shows. Its arguments are `dashboards`,
  the titles of up to five of that report's dashboards the caller can read, and `others`, the
  count of its other rows.
- **Deactivating the author changes nothing,** because no run uses the author's identity.
  Administrators find orphans by filtering `OwnerId` on inactive users, then `transfer` them.

### 5.7 Dashboards

Spec 0035 gives dashboards the same model:

- `Dashboard : IShareable` and `DashboardShare : Entity<int>, IShareRow`, composed with
  `Shareable<Dashboard, DashboardShare>("core.dashboard.shared")`;
- a service derived from `ShareableService<,>`, which declares the three actions over
  `IShareCommands<Dashboard>` and answers `audience` through `IAudienceResolver`.

In the other direction, reports call into dashboards through one interface that this spec owns:

```csharp
// Tellma.Core.Abstractions.Reports
public interface IDashboardReadThrough                      // owned here; implemented by spec 0035 §6.3
{
    // a tree rooted at core.Report: the reports a widget of dashboard @<dashboardParameter> shows, when the caller
    // reads that dashboard; null when the caller's dashboard Read is Denied
    Task<FilterTree?> ReadThroughAsync(string dashboardParameter);
    Task<IReadOnlyList<(int ReportId, int Id, string? Title)>> DashboardsUsing(IReadOnlyList<int> reportIds); // one row per report and dashboard; Title in the request's content slot, null for a dashboard the caller cannot read
}
```

- **The tree** is `Exists("core.DashboardWidget", "Report", And(Leaf("DashboardId = @d"),
  Via("Dashboard", <the caller's dashboard Read filter>)))`. When that decision is
  `Unrestricted`, the `Via` drops out.
- **Only at a run's load site** (§8.2, §12.1). It is never an access criterion on `core.Report`: a
  read-through admits a run, never the stored report, so `get`, `query` and Shared with me never
  see it.

## 6. The audience resolver

`audience` answers "which users can see this stored report, and why". It lists every member who can
see it, with each reason and their highest access. Per grant, it also counts the members who
cannot read its data, so the share dialog can warn that they will see an empty report. The
resolver is generic over shareables; dashboards call it too (spec 0035 §7.1).

### 6.1 Contract

```csharp
[ApiAction("audience", Action = "Share", Mutation = false, Idempotent = true, Long = true)]
public Task<ShareAudience> AudienceAsync(IdRequest request);

// Tellma.Core.Abstractions.Sharing
public interface IAudienceResolver { Task<ShareAudience> ResolveAsync(AudienceQuery query); }
public sealed record AudienceQuery(string Resource, int Id,
    IReadOnlyList<SecurableRef> Data,                       // what a member must hold to see data; empty: no counts
    string? ThroughResource, IReadOnlyList<int>? ThroughIds); // rows whose audience also sees this one
public sealed record ShareAudience(
    int Total, bool Capped,                                 // distinct audience users; Members cut at MaxAudienceMembers
    IReadOnlyList<int> CannotRead,                          // parallel to Data: distinct audience users who lack it, exact
    IReadOnlyList<AudienceMember>? Members,                 // null unless the caller holds core.Role × Read; by display name
    IReadOnlyList<AudienceGrant> Grants,                    // one row per grant, for the share dialog
    RelatedEntities Related);                               // core.User (with Kind), core.Role and ThroughResource display projections
public sealed record AudienceMember(int UserId, AudienceAccess Access, IReadOnlyList<AudienceReason> Reasons,
    IReadOnlyList<int> CannotRead);                         // indexes into Data that the member lacks
public sealed record AudienceReason(AudienceReasonKind Kind, AudienceAccess Access, int? RoleId, int? SharedById,
    DateTimeOffset? SharedAt, int? PermissionId, bool Filtered, int? ThroughId); // ids that fit Kind; Filtered: the permission has a filter
public sealed record AudienceGrant(AudienceReasonKind Kind, int? UserId, int? RoleId, int? PermissionId, int? ThroughId,
    AudienceAccess Access, int Members, IReadOnlyList<int> CannotRead); // CannotRead: parallel to Data
public enum AudienceReasonKind { Owner, UserShare, RoleShare, Everyone, Permission, Through }
public enum AudienceAccess { View, Edit, Owner }
```

- **The report service's query** is `("core.Report", id, [(Root, Read)], "core.Dashboard",
  DashboardsUsing([id]))`, with up to 20 dashboards. A dashboard passes its tiles' distinct data
  securables instead (spec 0035 §7.1).

### 6.2 Resolution

An **audience user** is an active `core.Users` row other than the system user, of any `Kind` and
any `State`. Every count and list below counts audience users only, and a role counts only its
current memberships (`AccessFilters.CurrentMembership`, spec 0014). A service account therefore
counts wherever §5.3's criterion admits it.

1. **Authorize.** The row loads under the caller's `Share` decision; an invisible row is 404.
   Without `core.Role × Read`, `Members` is null and the answer carries only grants and counts.
2. **Grants,** all in one Read round trip:
   - the owner and each user grant: the user, when an audience user. A grant of a deactivated user
     stays in `Grants` with `Members: 0`;
   - each role grant: the active role's members. For a public role, every audience user;
   - the Everyone grant: every audience user;
   - the permission grants: every stored permission of an active role, public roles included, with
     any action on `Resource` or on `*` (spec 0014 §5.2), since a grant of any action on a resource
     matches its `Read`. Its holders are selected in this round trip: the role's current members
     (`AccessFilters.CurrentMembershipSql`, spec 0014 §5.1), or every audience user for a public
     role. A filtered permission counts only when the row satisfies it: one count of `Id = @id and
     (<filter>)` per distinct filter text, under that filter's own Queryex version. The shareable's
     own criterion (§5.3) is not a permission grant; the owner and share bullets above cover it.
   - **Data readers.** The same round trip marks, per audience user and per `Data` securable,
     whether they can read it. A user can when they hold a stored permission, with any action, on
     the securable's resource, on `*` or on an owner up its chain (spec 0014 §5.5), through a role
     they belong to or a public role. A bespoke criterion with the action `Read` or `*` for that
     resource or an owner up its chain counts for every user: the test runs once per securable, and
     when it passes, no user lacks it. Filters are not evaluated, so the count warns about members
     who would be refused, never about members who see few rows.
3. **Through.** `ThroughIds`' grants resolve as in step 2, in one more round trip, and add the
   reason `Through` with the row's id. Nothing deeper is followed.
4. **Merge.** Each user becomes one member with every reason and its `CannotRead` indexes.
   `Access` is the highest: `Owner` > `Edit` > `View`, where `Edit` comes from an `Edit` grant or
   from a permission whose action is `Save` or `*`. `Members` is cut at `MaxAudienceMembers` (§14);
   `Total` and `CannotRead` stay exact.

**Example —** report 42 is shared with Omar (user 9) at `View` and with the role Sales (4) at
`Edit`. It answers the grants below. From them, spec 0034 §10.4's share dialog shows "Omar can't
read Sales invoice lines and will see an empty report." and "2 of 9 members of Sales can't read
Sales invoice lines."

```json
"grants": [ { "kind": "UserShare", "userId": 9, "access": "View", "members": 1, "cannotRead": [1] },
            { "kind": "RoleShare", "roleId": 4, "access": "Edit", "members": 9, "cannotRead": [2] } ]
```

## 7. The `core.report.shared` notification

A newly added user grant raises the shareable's notification type for the grantee, and removing the
grant withdraws it; for reports the type is `core.report.shared`. Role grants, the Everyone grant
and access changes raise nothing. Spec 0021 §2.3 owns the catalogue row; this section states what
`ShareCommands` sends.

- **One request per raise.** `title` is `LocalizedText.Names(row, "Title")` and `actorName` is
  `LocalizedText.Names(sharer)` (spec 0021 §2.4), so each grantee reads them in their own content
  slot. The sharer's `core.User` row (`Name`, `Name2`, `Name3`) loads through `context.Loader`
  beside the existing grants. `ActorUserId` is the sharer.
- **Dedup.** `DedupKey = '<resource>:<id>'`, such as `'core.Report:42'`, with
  `OnDuplicate = Replace`; removing the grant appends `WithdrawRequest('<resource>:<id>', [userId])`.
- **Text.** `Notification_core_report_shared` = "{actorName} shared {title} with you"; Arabic
  "شارك {actorName} معك {title}". The target opens `/{t}/reports/{id}` (spec 0024 §9.4).

## 8. Execution

`run` executes a stored or an inline report under the viewer's permissions. Each kind runs one
Queryex statement; a pivot adds one member statement per tree dimension. When defaults apply, one
constant statement runs first (§4.3). Coded reports run through their own actions (§11).

### 8.1 Contract

```csharp
[ApiAction("run", Action = "Read", Mutation = false, Idempotent = true, Long = true)]
public Task<ReportResult> RunAsync(ReportRunRequest request);

public sealed record ReportRunRequest(
    int? Id,                                                // a stored report, or
    ReportInline? Inline,                                   // an inline report; exactly one of the two
    IReadOnlyDictionary<string, JsonElement>? Arguments,    // §4.4
    int? DashboardId = null,                                // read-through (§8.2)
    int Skip = 0, int? Take = null, bool IncludeCount = false); // list and summary only; Take absent: DefaultTake
public sealed record ReportInline(string Root, ReportKind Kind, JsonElement Document,
    string? Name,                                           // a report declared in code (tmInlineReport); null: the studio or the assistant
    string? Title);                                         // in the request's content slot, cut to 255; null: the default title (§2.1)

public interface IReportEngine                              // run, export (§12) and IReportResults (§11.3) use it
{
    Task<ReportResult> RunAsync(ReportSource source, IReadOnlyDictionary<string, JsonElement>? arguments, ReportPage? page);
    // export (§12.1): every page of MaxTake rows in order; the first carries Count capped at countCap;
    // a summary with totals: only the last page's Table.Totals is set, over every page
    IAsyncEnumerable<ReportResult> PagesAsync(ReportSource source, IReadOnlyDictionary<string, JsonElement>? arguments, int countCap);
}
public abstract record ReportSource
{
    public sealed record Stored(int Id, int? DashboardId) : ReportSource;
    public sealed record Inline(string Root, ReportKind Kind, ReportDocument Document, string? Name, string? Title) : ReportSource;
    public sealed record Code(string Root, ReportKind Kind, ReportDocument Document) : ReportSource; // IReportResults.RunAsync
}
public sealed record ReportPage(int Skip, int Take, bool IncludeCount);
```

**Example —** the first open of §3.5's summary (id 42): `From` takes its default, `To` is omitted.

```json
→ POST /17/api/web/reports/run   { "id": 42, "arguments": {}, "take": 1000, "includeCount": true }
← { "id": 42, "kind": "Summary", "root": "acme.SalesInvoiceLine", "title": "Sales by customer",
    "arguments": [ { "key": "From", "value": "2026-01-01", "source": "Default" }, { "key": "To", "value": null, "source": "Omitted" } ],
    "columns": [ … ], "table": { "rows": { … }, "totals": { … }, "totalsOmitted": false, "count": 37, "countCapped": false },
    "drill": { … }, … }
```

### 8.2 Authorization

A run first checks that the caller may see the report, then that they may read its data.

| Source | Report check | Refusal |
|---|---|---|
| `Id` | the row loads under the caller's `Read` decision on `core.Report`; `IsActive` is not checked | 404 `not-found` |
| `Id` + `DashboardId` | the row loads under `And(Leaf("Id = @id"), Or(decision.Filter, readThrough))`, where `readThrough` is `ReadThroughAsync("@d")` (§5.7) with `@d` declared `Numeric` and bound to `DashboardId`. It is added only when the decision is `Filtered`. | 404 |
| `Inline` | any member: every member's `Read` passes through §5.3's criteria | — |

Every source then passes two data checks. Each refuses with 403 `forbidden`, which names the
permission and the reason (spec 0015 §14.1).

- **The root.** `RequireAsync(Root, "Read")`, refused with the reason `no-grant`. A child root
  composes its owner's decision (spec 0014 §5.5).
- **Every path.** Spec 0015 §5.2's traversal rule checks every path of every clause. A refusal
  carries the reason `restricted-path` and the `path`.

The root decision's filter is joined by `and` into every statement, so a report never widens
access. Every row, group, subtotal, total and chart point counts only rows the viewer may read,
and `me()` is always the viewer.

### 8.3 The run, step by step

1. **Load** the row, or take the inline document, and parse the document. An inline document is
   validated in full (§3.4), with its errors at `inline.document`. A stored document is checked
   only by compiling it (step 5). A `Code` document (§11.3) is validated and normalised in full as
   an inline one; a diagnostic fails the run with a 500, logged at Error, since only a developer can
   fix it (§8.11).
2. **Arguments.** Sort the request's arguments into supplied, defaulted and omitted sets, with
   §4.4's 422s.
3. **Defaults.** When any applies, evaluate them as §4.3 describes, under the report's
   `QueryexVersion` (an inline document's is the current `QueryexLanguage.Version`), in one Read
   round trip.
4. **Plan** the `QuerySpec` for the kind (§8.4–§8.6), with
   `Filter = And(Leaf(document.filter, QueryexVersion), decision.Filter)`. Check every discovered
   path with the traversal rule (§8.2). No activatable conjunct is added: a report that means to
   skip inactive rows filters `IsActive` itself. The plan cache (§8.10) holds this `QuerySpec`
   without `decision.Filter`; every run adds the filter and checks the paths.
5. **Compile** under `ReportLimits`, with one `QueryArguments` (spec 0012 §5.2) that declares every
   referenced parameter with its stored type (§4.2), holds the supplied and defaulted values, and
   marks the omitted ones (§4.4). A stored report that no longer compiles answers
   `400 query-invalid`, where each diagnostic's `clause` is its document pointer, such as
   `/pivot/rows/0`. The run logs `ReportEvents.DefinitionInvalidAtRun`.
6. **Execute** one Read batch through `IGuardedBatchRunner` (spec 0014 §7.1) with `IDataBatch.Rows`
   (spec 0012 §11.3). The batch sets `IDataBatch.Limits = ReportLimits`, and the kind's statement
   takes step 5's `QueryArguments`, so its columns and partials are those of step 5's
   `CompiledMeasures` (spec 0012 §11.3). The batch holds the kind's statement, with `CountCap`
   when `IncludeCount`, and a pivot's member statements. Then **shape** the result (§9). A pivot
   goes through the cube builder (§8.6), and C# computes a summary's totals row (§8.4).

**Round trips.** An inline report without defaults takes one Read round trip. Loading a stored
report adds one, and applied defaults add one. A dashboard read-through adds none, because it rides
the load (§8.2). A stored report with defaults therefore takes three, for direct and read-through
viewers alike.

### 8.4 Terms to select items

The compiler turns each term into select items according to its column-key kind (spec 0029 §6.1).
A navigation expands through `IDisplayNames.Columns(entity, shape, navigationPath)` (spec 0012
§3.1), as on the search page.

| Term | List: select items | Aggregate kinds: grouping key | Aggregate kinds: label items |
|---|---|---|---|
| value path `Amount`, `Invoice.PostingDate` | the path | the path | — |
| text path `Name` (a group anchor) | `Name`, `Name2` (admitted slots) | every admitted slot | — |
| navigation `Invoice.Customer` | its `Id` and display-name columns | `Invoice.Customer.Id` | `max(Invoice.Customer.Name)`, `max(Invoice.Customer.Name2)` |
| image `Image`, file `File` | `ImageId`, `Image.Variants`; `FileId`, `File.FileName` | refused (§3.4 rule 4) | — |
| `expr` | the expression | the expression: a key, or a measure as written | — |

- **Labels as `max(...)`.** A navigation groups by its key alone. Its display columns depend on
  that key, so reading them as aggregates keeps one grouping key per dimension.
- **Identity columns (list).** A list selects the root `Id` first, so a row can open its record
  (§9.2). A child root adds its owner's key through the owner navigation, such as `Invoice.Id`. A
  grandchild root adds its parent's key and its top-level owner's key.
- **Ordering.** `{ term: i }` orders a value by its path, and a text term by its active slot
  (`coalesce(Name2, Name)` at slot 2). A navigation orders by prefix, then by name in the active
  slot; in aggregate kinds, over its label items. The active slot is the request language's (spec
  0013 §9.2). Without `orderBy`, a list uses the root's `DefaultOrderBy` and a summary its first
  grouping key.
- **Top and paging.** A page is `Skip`/`Take` within `top`: the take is `min(Take, top − Skip)`,
  and `count` is capped at `top`. **Example —** with `top: 50`, `Skip` 40 and `Take` 20, the page
  holds rows 41–50.
- **Totals.** A summary with `totals: true` shows a totals row only when the page holds the whole
  result:
  1. the request's `Skip` is 0; and
  2. the statement returned every row. When `top` is absent or larger than the page, the compiler
     asks for `take + 1` rows. An extra row proves there are more and is dropped. When `top` is
     no larger than the page, no probe is needed.

  How the row is computed:

  - **Partials.** When `Skip` is 0, the statement also carries the measures as `Measures` (spec
    0010 §F.14). So does every page of an export, whatever its `Skip` (§12.2).
  - **Evaluation.** When every row came back, C# evaluates each measure over the combined partials
    of the returned rows, after `having` and `top`.
  - **Omission.** Otherwise `Totals` is null and `TotalsOmitted` is true (§9.2). An export's pages
    leave `TotalsOmitted` false, and the last page carries the totals (§8.1).
  - **`top` always fits.** The renderer pages summaries at 1,000 rows (spec 0034 §5.1), the
    largest `top` (§3.4 rule 8), so every summary with `top` fits.

  **Example —** §3.5's top 50 customers total those 50 rows. Its `sum(Amount)` and `count()` are
  already select items, so their partials add no column.
- **Chart.** One aggregate statement: grouping keys, label items and the measure. It is ordered by
  the measure when `orderBy: 'measure'`, else by the first dimension. Without `top`, it takes
  `MaxChartPoints + 1` rows; if the extra row arrives, the run fails with
  `413 Report.TooManyPoints`.

### 8.5 Pivot: the leaf statement

A pivot reads its facts at leaf grain: one row per combination of the finest key of every
dimension. SQL returns keys, labels and the measures' partials, never a subtotal. One member
statement per tree dimension runs in the same batch.

1. **Keys.** One grouping key per dimension, row dimensions first, in document order:
   - `Center.Id` for a navigation. A tree dimension groups by the fact's own node, never by its
     ancestors. `tree: false` groups the same way and renders flat;
   - `quarter(Invoice.PostingDate)` for a value or an expression;
   - every admitted slot for a text term (§8.4).
2. **Labels.** A flat navigation's label items and attributes read as `max(...)` (§8.4). A tree
   dimension's labels and attributes come from its member statement (step 5).
3. **Measures.** The document's measures go to `QuerySpec.Measures` (spec 0010 §F.14), never to
   `Select`. The engine appends their partial columns, with each shared partial once.
4. **Having** is a plain SQL `HAVING` on the leaf rows. A failing leaf row never leaves SQL.
5. **Member statements.** One per tree dimension, over the tree entity, with no row filter. Trees
   are master data, so it holds thousands of rows at most.
   - **Columns.** `Id` and `ParentId` are structural keys, read without a traversal check. The
     display columns and `Code` are the tree entity's related projection (spec 0012 §2.5), which
     any viewer may read through a navigation (spec 0014 §5.3). Attribute paths, made relative to
     the tree (`Center.Code` → `Code`), pass the traversal rule like every other path (§8.2).
   - **Rows.** The ancestors of a leaf row's node show their display columns even when the
     viewer's own `Read` filter on the tree entity would hide them, as a navigation's joined rows
     do (spec 0014 §5.3). The builder drops every node that is neither a leaf row's node nor an
     ancestor of one, so no other node reaches the result, the cache or the wire.
6. **Cap.** The leaf statement is ordered by the first key and takes `MaxPivotRows + 1` rows. If
   the extra row arrives, the run fails with `413 Report.TooManyRows`.

**Example —** §3.5's pivot, as the compiler hands it to the engine:

```text
Select    = Center.Id, quarter(Invoice.PostingDate)          -- one key per dimension: R1, C1
Measures  = sum(Amount), count()
Aggregate = true, OrderBy = Center.Id, Take = MaxPivotRows + 1
Filter    = year(Invoice.PostingDate) = @Year  and  <the root decision's filter>

Members of Center: Root = gl.Center, Select = Id, ParentId, Name, Name2, Code
```

### 8.6 The rollup

The cube builder rolls the leaf rows up in C#: it builds the member trees, folds the partials into
every subtotal and total, and evaluates each cell's measures. Totals are visual: they cover only
the leaf rows that survived `having`.

1. **Trees.** The builder reads each tree's parent map from its member statement. It marks every
   node with a leaf row on itself and every node with a leaf row below it.
2. **Chains.** Each leaf row gets one chain per axis: its deepest member, each ancestor, then the
   axis `Total`.
   - The deepest member is the node itself, or the node's phantom child when the node also has
     leaf rows below it.
   - A null key maps to "(None)".
   - With several dimensions on an axis, the chain climbs the inner dimension, then the outer
     ones (§9.3).
3. **Leaf cells.** Each leaf row loads its partials into one cell of a `PartialTable` (spec 0010
   §F.15): the cell of its deepest row member and deepest column member. A flat member's labels
   and attributes are equal on all its leaf rows, so the builder keeps the first.
4. **Fold.** Cells fold into their parents bottom-up, first along the row chains, then along the
   column chains. Every combine is associative and commutative, so the cost is one fold per cell
   and partial, whatever the depth.
   - `sum` and `count` add.
   - `min` and `max` keep the least or the greatest value. Text compares ignoring case, and
     hierarchy paths in tree order (spec 0010 §F.15).
   - Nulls are skipped.
5. **Evaluate.** Each cell evaluates every measure over its combined partials with the run's
   `MeasureEvaluator`, at SQL Server's types (spec 0010 §F.15). Leaf cells do too: SQL never
   computes a pivot measure.
6. **Emit.** Members are sorted (§9.3) and numbered after sorting, and cells are emitted in
   (row id, column id) order. The output is byte-identical whatever order SQL returned the rows in.

`rowTotals` and `columnTotals` decide only whether an axis's `Total` is built and emitted.

**`having` and visual totals.**

- **The finest cell is one leaf row**, where a tree's key is the fact's own node. `having` runs on
  it in SQL (§8.5), and a failing row counts nowhere.
- **Every other number is its measure over the surviving rows in its scope.** `sum` and `count`
  add what shows below. `avg` divides the total sum by the total count; it never averages
  averages. A ratio divides the rolled-up sums.
- **Members.** A member exists only when a surviving leaf row sits under it. An ancestor whose
  whole subtree failed disappears, so a collapsed member never shows an empty total.
- **The phantom child** exists only when the node's own row survived and something below it
  survived too. When the node's own row failed, its subtotal covers its surviving children alone.
- **Totals reconcile.** Row totals, column totals and the grand total come from one set of
  surviving rows. With column dimensions, a cell can drop inside a row: under
  `sum(Amount) > 1000`, a customer's weak quarter is blank, and the row total adds the others.

**While building.**

- **Caps.** `MaxPivotRowMembers`, `MaxPivotColumnMembers`, `MaxPivotCells` and
  `MaxPivotCellPartials` are checked as members and cells appear, so an oversized pivot fails fast
  with 413 (§8.8).
- **Cancellation.** The builder observes the request's cancellation token every 4,096 rows or
  cells, in reading, folding and evaluation alike.
- **Errors.** A measure that divides by zero or overflows in any cell fails the run (§8.11).

**Example —** rows by `Center` alone, with three measures: `sum(Amount)`, the corporate share
`sum(Amount, Invoice.Customer.CustomerType = 'Corporate') / sum(Amount)`, and `avg(Amount)`. They
need three partials: `sum(Amount)` serves all three measures, the share adds the conditional sum,
and `avg` adds `count(Amount)`. Head office (10) has facts of its own. Its children Riyadh (11)
and Jeddah (12) have the rest.

| Leaf row | `sum(Amount)` | `sum(Amount, …'Corporate')` | `count(Amount)` |
|---|---|---|---|
| 10 | 2,000.0000 | 500.0000 | 1 |
| 11 | 30,000.0000 | 9,000.0000 | 4 |
| 12 | 20,000.0000 | 4,000.0000 | 2 |

The sums are `decimal(38,4)` and the count `bigint`. Both divisions land on `decimal(38,6)`,
truncated as SQL Server truncates:

| Row | Sales | Corporate share | Average line |
|---|---|---|---|
| Head office | 52,000.0000 | 13,500 / 52,000 = 0.259615 | 52,000 / 7 = 7,428.571428 |
| — Head office (direct) | 2,000.0000 | 0.250000 | 2,000.000000 |
| — Riyadh | 30,000.0000 | 0.300000 | 7,500.000000 |
| — Jeddah | 20,000.0000 | 0.200000 | 10,000.000000 |

Averaging the children's shares would give 0.25, and averaging their averages would give 6,500.
With `having: "sum(Amount) >= 5000"`, leaf row 10 fails in SQL. Head office then has no phantom
child, and both Head office and the total show 50,000.

### 8.7 Calendars

A document names its calendar in its expressions, and an omitted `cal` is Gregorian. The viewer's
calendar changes how dates render, never how a report groups.

- **Labels follow the grouping.** `format.calendar` keeps a label in the grouping's calendar
  (§3.2).
- **Umm Al-Qura.** `'uq'` works in every calendar function through spec 0013 §8.2's month map. A
  date outside the map gives null. Its operand rules are spec 0010 §F.11's.
- **Ethiopic.** `'et'` works in every calendar function through spec 0010 §F.19's algorithmic
  date arithmetic, with 13 months.

### 8.8 Limits

Every run is bounded. Named limits are `ReportsOptions` members (§14), shown with their defaults;
the others are fixed, or owned by the spec cited.

| Limit | Default | When exceeded |
|---|---|---|
| list and summary page | `DefaultTake` 200, at most `MaxTake` 10,000 | 413 `limit-exceeded` |
| chart points | `MaxChartPoints` 10,000 | 413 `Report.TooManyPoints` |
| pivot leaf rows | `MaxPivotRows` 250,000 | 413 `Report.TooManyRows` |
| pivot row members, column members, cells | `MaxPivotRowMembers` 20,000, `MaxPivotColumnMembers` 2,000, `MaxPivotCells` 500,000 | 413 `Report.TooManyCells` |
| pivot cell partials | `MaxPivotCellPartials` 5,000,000: cells × partials, the cell cap with 10 partials | 413 `Report.TooManyCells` |
| summary totals | the first page, when it holds every row (§8.4) | the totals row is omitted |
| `ReportLimits` (a `QueryexLimits` profile) | `MaxListItems` 512, `MaxJoins` 64, `MaxParameters` 1,024, `MaxTypedNodes` 8,192; the rest `Default` | diagnostics |
| command timeout | `CommandTimeout` 120 s | as any timeout (spec 0016 §7.1) |
| concurrent runs per user | two lanes: interactive 4, queue 8; batch 6, queue 24 (spec 0016 §8.2) | 429 |

- **How a 413 travels.** Every refusal above throws `LimitExceededException` (spec 0015 §14.1):
  `413 limit-exceeded` whose `arguments.limit` names the limit, `MaxTake`, `Report.TooManyRows`,
  `Report.TooManyPoints`, `Report.TooManyCells` or `Report.ExportTooWide` (§12.3). Clients match
  on `arguments.limit`.

### 8.9 Cancellation, priority and background refresh

A run is a read: the client may retry it and may cancel it.

- **Aborts.** The client aborts a run on a parameter change, on navigation and on Cancel (spec 0024
  §10.5). The abort reaches SQL (spec 0016 §8.3). The server writes nothing and counts
  `tellma.reports.cancelled`.
- **Priority.** A dashboard tile's runs send `Tellma-Priority: low` (spec 0016 §6.1), so they take
  the batch lane and never hold a permit that a report page's run needs.
- **Background refresh.** A dashboard's silent refresh also sends `Tellma-Activity: background`,
  except in present mode (spec 0035 §11.2).

### 8.10 Caching

The compiler caches plans in one LRU cache of `PlanCacheSize` entries per process. A plan never
holds a viewer's part, so one entry serves every viewer of a report.

- **What a plan holds.** The kind's planned `QuerySpec` (§8.4–§8.6), the member statements' specs
  and the discovered paths. Every run adds `decision.Filter` (§8.3 step 4), runs the traversal
  check (§8.2), and binds the page, `CountCap`, the arguments and `OmittedParameters`. None of
  these enters a plan.
- **The key.** The tenant id comes first, because report ids repeat across tenants. Then:
  - a stored report: `(Id, ModifiedAt)`. Every save stamps `ModifiedAt`, the seed rewrite of §13
    included, so an edited report misses the cache;
  - an inline or a `Code` document: `(Root, Kind, hash)`, where the hash is the SHA-256 of the
    parsed `ReportDocument` re-serialised with `TellmaJson.Options` (spec 0016 §3.1). Whitespace
    and member order therefore never split the cache, and a card that runs on every record open is
    planned once. It is still validated on every call (§8.3);
  - every source: the tenant's `MultilingualShape` and the request's `ContentLanguageIndex`
    (spec 0013 §7.4, §9.2), which choose the admitted slots and the active slot (§8.4).
- **No invalidation.** A save, or a settings change that adds a language, changes the key; the old
  entry ages out of the LRU.
- **Queryex's own caches** key on text and options (spec 0008 §16, spec 0010 §F.0). Step 5's
  compile and its `CompiledMeasures` (spec 0010 §F.15) are cached there, not in a plan: an omitted
  parameter folds inside a partial's condition (§4.4), so the omitted set is part of that key.

Each run prepares its own `MeasureEvaluator` from a `MeasureContext` (spec 0010 §F.15) holding:

- the reader's precision and scale of each partial column (spec 0012 §3.3);
- the run's effective arguments, supplied and defaulted, with an omitted one absent (§4.4);
- the `Today`, `Now` and `UserId` values that its batch bound to the context slots (spec 0012
  §5.1, §11.3).

The evaluator and the statement therefore see one `today()`, one `me()` and one argument set
(§4.3).

### 8.11 Measure errors

A measure that divides by zero or overflows fails the whole run, as a SQL statement fails when one
group divides by zero. The status says who can fix the document:

- **422 `Report.MeasureFailed`** for a document a tenant user can edit: an unnamed inline report
  (the studio, the assistant) and a stored report with an owner, a clone of a standard report
  included. It logs `ReportEvents.MeasureFailed` at Information.
- **500** for a document only a developer can fix: a standard report (§5.1), a named inline report
  and a `ReportSource.Code` document. It logs `ReportEvents.MeasureFailed` at Error, with the id or
  name and the arguments below; the body carries only the trace id. The name only picks the status
  and the level, so trusting it from the request is harmless.

The failure comes from either side:

- **From C#.** The evaluator throws `QueryexEvaluationException` (QX6001 division by zero, QX6002
  overflow; spec 0010 §F.15) on a pivot cell or a summary's totals row.
- **From SQL.** Any report statement can raise SQL error 8134 or 8115. Examples are a summary ratio
  whose denominator sums to zero in one group, and a row-level division in the filter. The data
  layer throws `ArithmeticFailureException(Reason)` (spec 0012 §6.4), and the report engine maps it
  as above. The CRUD pipeline maps it by spec 0015 §14.2.
- **Arguments** of the 422, also logged with the 500:
  - `reason`: `divide-by-zero` or `overflow`;
  - `pointer`: the failing measure's document pointer, such as `/pivot/measures/1`. An error that
    SQL raised has the empty pointer `""` (the whole document), because SQL names neither the
    term nor the group;
  - `row` and `column`, only when C# evaluated the cell. Each lists one caption per dimension of
    that axis, outermost first, and stops at the cell's own member: first the member the cell
    sits under on each outer dimension, then the cell's member. A subtotal on an outer dimension
    therefore lists fewer captions than the axis has dimensions, and the axis total lists none. A
    summary's totals row gives `row: []` and no `column`.
- **Captions.** A navigation or text member's caption is its display text in the request's content
  slot. A phantom's caption is its node's. A value or expression member's caption is its value as
  text in spec 0016 §3.2's encoding (`"3"`, `"2026-03-01"`). `null` stands only for (None).
- **Several failures.** The answer names the first failing cell in member order (§9.3), so it is
  deterministic.

**Example —** if Jeddah's lines sum to zero, §8.6's corporate share fails with `pointer`
`/pivot/measures/1`, `reason` `divide-by-zero`, `row` `["Jeddah"]` and `column` `[]`. With rows
by `Center`, then `Customer`, and columns by `quarter(Invoice.PostingDate)`, a failure in Jeddah's
subtotal for the third quarter gives `row` `["Jeddah"]` and `column` `["3"]`.

## 9. Results

Every run — stored, inline or coded — answers one self-describing `ReportResult`: the effective
arguments, labelled columns, and a table or a cube. The renderers (spec 0034) and the export
layout (§12) read nothing else. Labels stay content in every slot, not rendered text, so a language
switch relabels without a new request.

`decimal` and `long` values travel as JSON strings at full scale, never as JSON numbers (spec 0016
§3.2). This holds in row sets, the totals row, cube cells and argument echoes alike. A pivot
measure keeps every digit the evaluator computed, up to 38.

### 9.1 The envelope

```csharp
public sealed record ReportResult(
    int? Id,                                             // the stored report; null for an inline or a coded report
    ReportKind Kind,
    string? Root,                                        // the root entity; null for a coded report without one
    string? Title,                                       // in the request's content slot: the stored report's, the inline report's Title, or a coded report's localized title
    IReadOnlyList<ReportArgumentValue> Arguments,        // every declared parameter, effective, in document order
    IReadOnlyList<ReportColumn> Columns,                 // one per term in document order (pivot: dimensions, attributes, measures)
    ReportTable? Table,                                  // List, Summary, Chart
    ReportCube? Cube,                                    // Pivot
    ReportChartLayout? Chart,                            // Chart: the document's type and options
    ReportDrill? Drill,                                  // Summary, Pivot, Chart; null otherwise
    DateTimeOffset ExecutedAt);                          // the request's Now
public sealed record ReportArgumentValue(string Key, JsonElement? Value, ReportArgumentSource Source);
public enum ReportArgumentSource { Supplied, Default, Omitted }
public sealed record ReportChartLayout(string Type, ReportChartOptions? Options);

public sealed record ReportColumn(
    ReportColumnRole Role,                               // Column, Dimension, Attribute, Measure
    ReportColumnKind Kind,                               // Value, Text, Navigation, Image, File, Expression
    QueryexType Type, int? Scale,                        // Scale: a Numeric term's; a pivot measure's from MeasureEvaluator.ResultTypes
    string? Label, string? Label2, string? Label3, string? LabelKey, // as stored; all null: derive from Path, else Expression
    string? Path,                                        // the column key, for derived labels and drill conditions
    string? Expression,                                  // an expr term's text as stored: the header when nothing else names it
    string? Entity,                                      // a navigation's target: 'acme.Customer'
    ReportDisplay? Display, ReportFormat? Format,        // effective: stated, else the default display (§3.2)
    string? BlobKind,                                    // Image and File: the blob kind its ids belong to (spec 0017)
    int? Dimension, int? Axis, bool? Expand);            // pivot: the dimension, 0 rows or 1 columns, expanded when opened
```

`BlobKind` comes from the `[BlobReference]` at the column's path, or from a coded table's
`ReportColumnSpec` (§11.3). The client builds an image's URL from it.

`Title` is never empty. An inline run without a `Title` takes `Core_Report_DefaultTitle` over its
`Root` and `Kind` (§2.1), so the export's sheet, file stem and job label always have one (§12.1).

### 9.2 Tables (list, summary, chart)

```csharp
public sealed record ReportTable(
    QueryRowSet Rows,                                    // spec 0016 §3.5's row set; the terms' columns only
    IReadOnlyList<IReadOnlyList<int>> ColumnOrdinals,    // parallel to ReportResult.Columns: the row-set columns each holds
    ReportRowIdentity? Identity,                         // List: how a row opens its record
    QueryRowSet? Totals,                                 // Summary with totals, when the page holds every row (§8.4): one row over Rows' columns
    bool TotalsOmitted,                                  // totals asked for, but the result is paged
    int? Count, bool CountCapped);                       // IncludeCount
public sealed record ReportRowIdentity(
    int IdOrdinal, string Entity,                        // the root's id column and entity
    int? OwnerIdOrdinal, string? OwnerEntity,            // child roots: the top-level owner's id column and entity
    string? Collection,                                  // child roots: the collection in the ?child= link
    int? ChildIdOrdinal);                                // ChildIdOrdinal: a grandchild root's parent id
```

**Ordinals by kind.** `ColumnOrdinals[i]` lists the row-set columns that hold column i:

| Term | Row-set columns |
|---|---|
| value, expression | one column |
| text | its slots, in order |
| navigation | `[Id, display-name columns…]` in `IDisplayNames.Columns` order, as `TmDisplayNames.of` reads it (spec 0029 §5.3) |
| image; file | `[ImageId, Variants]`; `[FileId, FileName]` |

- **Identity.** A list row opens its record:
  - over a top-level root, `/{t}/{segment}/{id}`;
  - over a child root, `/{t}/{ownerSegment}/{ownerId}?child={Collection}:{id}` (spec 0031 §14);
  - over a grandchild root, the intermediate child:
    `/{t}/{ownerSegment}/{ownerId}?child={Collection}:{childId}`, where `OwnerEntity` is the
    top-level owner.
- **Partial columns.** A summary's partial columns (§8.4) never reach `Rows`. The engine
  drops them after the totals row is computed, and `ColumnOrdinals` index what remains.
- **The totals row** has the same columns as `Rows`, so `ColumnOrdinals` index it too. Its measure
  cells hold the totals, and every other cell is null. The renderer and the export layout write
  "Total" in the first key column.
- **Chart data** is the dimension columns, then the measure. The renderer splits series by the
  second dimension; that is layout, not aggregation.

### 9.3 The cube (pivot)

The cube builder turns the leaf rows into members and sparse cells in C# (§8.6), so the client
renders and never aggregates.

```csharp
public sealed record ReportCube(
    IReadOnlyList<CubeLevel> RowLevels, IReadOnlyList<CubeLevel> ColumnLevels,
    IReadOnlyList<CubeMember> RowMembers, IReadOnlyList<CubeMember> ColumnMembers,
    IReadOnlyList<CubeCell> Cells);                      // sparse: absent means no surviving facts
public sealed record CubeLevel(int Dimension, int Level); // the levels present in the data; Level: 1… in a tree dimension, 0 in a flat one
public sealed record CubeMember(
    int Id, int? Parent, int Depth,                      // Depth 0: the axis total; 1: the first level
    CubeMemberKind Kind, int Dimension, int Level,
    JsonElement? Key,                                    // the id or the value; null for (None)
    JsonElement? Display,                                // a navigation's or text term's display row: { id, name, name2 }
    IReadOnlyList<JsonElement?>? Attributes);            // parallel to the dimension's attributes
public enum CubeMemberKind { Member, Phantom, None, Total }
public sealed record CubeCell(int Row, int Column, IReadOnlyList<JsonElement?> Values); // wire: [row, column, v0, v1, …]
```

- **One tree per axis.** `Parent` links a member to the member one level up. The next dimension's
  members hang under each deepest member of the previous one: a node with no children, or a
  phantom child.
- **Which members exist.** §8.6 decides, over the surviving leaf rows. The axis also has the
  `Total` member (id 0, depth 0) when it has totals or no dimension.
- **Values.** Each cell holds its measures, evaluated over the cell's combined partials (§8.6), in
  measure order.
- **Totals.** A cell on a `Total` member is that axis's subtotal, or the grand total.
- **Order.** Members follow their dimension's `order`: values by value, and navigations and text
  by display text in the request's content slot and culture. "(None)" comes last, and a phantom
  first among its siblings. The renderer re-sorts by the same rule after a language switch.

### 9.4 Drill-through

A click on a summary row, a pivot cell or a chart point opens the drill-through list
`/{t}/list/{segment}#d=<payload>` (spec 0030 §17, spec 0034 §8). The payload is the base64url of
the UTF-8 JSON `{ "filter": …, "cols": […] }`, without padding. The list is a plain `query` over
the root with `IncludeInactive` set (spec 0030 §17), so it counts the rows the report counted. Its
filter must be self-contained: no parameter, no omitted atom.

```csharp
public sealed record ReportDrill(
    string Segment,                                      // the root's query segment, a child segment included
    string? Filter,                                      // the document filter, arguments bound, omitted atoms folded
    IReadOnlyList<string?> Terms,                        // parallel to Columns: an expression dimension's bound text;
                                                         // a measure's bound condition; null otherwise
    IReadOnlyList<string> Columns,                       // drillColumns; empty: the root's default columns (§3.3)
    bool LeavesOnly);                                    // having, or a summary's top: only leaf cells, summary rows and chart points drill
```

The server binds `Filter` and `Terms` with `BindParameters` (spec 0010 §F.10), so they hold values
instead of parameters. A measure's condition comes from `MeasureCondition` (spec 0010 §F.20): the
`or` of its aggregates' bound conditions, deduplicated, and null when any aggregate has none or
folds to `true`. The client joins `(Filter)` by `and` to one condition per clicked coordinate and
measure, written through spec 0029 §8.2's `tmQx` (`raw` for the bound text):

| Coordinate | Condition |
|---|---|
| navigation member, flat | `CustomerId = 1012` (on the foreign key) |
| tree member with children in the cube | `descendantOf(Center.Id, 10)`: the member and its subtree |
| tree member without children in the cube; a phantom; (None) | `CenterId = 11`; `CenterId = 10`; `CenterId = null` |
| value member | `Region = 'East'`; a date `Invoice.PostingDate = cast('2026-03-01', 'date')` |
| text member | every slot it grouped by: `Name = 'Jeddah' and Name2 = 'جدة'` (`= null` for an empty slot) |
| expression member | `(<Terms[i]>) = <literal>`, such as `(quarter(Invoice.PostingDate)) = 3` |
| `Total` | none |
| the clicked measure (a pivot cell, a chart point) | `(<Terms[i]>)` when it has a condition |
| a summary row | the `or` of its measures' conditions; none when any has none, or the summary has no measure |

A drill therefore lists the rows its number aggregates:

- **`sum(Amount, Invoice.Customer.CustomerType = 'Corporate')`** drills to the corporate lines
  only. The corporate share `sum(Amount, …) / sum(Amount)` has no condition, since its denominator
  reads every row.
- **Only explicit conditions count.** `count(CustomerId)` adds no `CustomerId != null`, so its
  drill also lists the lines with no customer.
- **Children in the cube.** A tree member with no children in the cube drills on its own key, like a
  phantom. Under `having`, its children's rows may all have failed, and its number then covers its
  own rows alone. A collapsed member keeps its children in the cube and drills on `descendantOf`.
- **Leaves only.** `having` is group-level and cannot become a row filter, and a summary under `top`
  misses whole groups. With `LeavesOnly`, only leaf cells drill. A pivot cell is a leaf when both
  its row and column members are deepest members of their axis: a member of the axis's last
  dimension with no children in the cube, a phantom, "(None)", or the axis's `Total` when the axis
  has no dimension. Summary rows and chart points are leaves; a summary's totals row is not. A
  collapsed member is a subtotal and drills only once expanded.

**Example —** §8.1's run binds `Filter = "Invoice.PostingDate >= cast('2026-01-01', 'date')"`,
where the omitted `To` atom folded to `true` and was absorbed. A click on Olive Inc. (1012) opens
the list with this payload, before encoding:

```json
{ "filter": "(Invoice.PostingDate >= cast('2026-01-01', 'date')) and Invoice.CustomerId = 1012",
  "cols": [ "Invoice.SerialNumber", "Invoice.PostingDate", "Center", "Amount" ] }
```

## 10. Child-entity query routes

Reports over child entities, such as sales invoice lines, need a query route over the child. Every
child collection of a stack with `Query`, at any depth, gets one: a read-only child query surface
(spec 0015 §2.2). It needs no declaration; `[ApiResource(Queryable = false)]` opts out.

- **Route.** `POST /{t}/api/web/{child-segment}/query`, under the owner's `Read` decision rebased
  through `FilterTree.Via` (spec 0014 §5.5).
- **Securable and operations.** `(Child, Read)` with the owner pair (spec 0015 §2.7). The
  operation is `query`, through `IChildQueryService.QueryAsync(resource, request)` (spec 0015
  §5.2). When the owner stack has `Export`, the surface also serves `export` and `export/start` in
  the display shape, under the same `Read`; there is no export for import.
- **Traversal through the owner.** With no direct child grant, a path through the owner navigation
  is checked as from the owner. For example, `Invoice.PostingDate` passes under a filtered `Read`
  on invoices.
- **MCP.** A surface inherits its owner's `McpExposure` (spec 0016 §11.5).
- **The audit.** Surfaces are on by default, so the startup audit lists every one it projected
  (spec 0016 §4.6).
- **Client.** The child's summary carries its `segment` and `operations: ['query']`, plus
  `'export'` when exportable (spec 0029 §2.2). The drill-through list is the only page over it, and
  it exports as a search page does.

## 11. Coded reports

A coded report is the escape hatch for what the document cannot express. It has no row in
`core.Reports`. The server declares it once, in C#, and publishes the declaration in client
metadata (§11.4); the client adds only its launcher placement and, for a hatch, its own code (spec
0034 §2.2). It is an app feature: a role grant on its own securable gives access, it is never
shared, and it appears in the launcher at `/{t}/reports/{key}` (spec 0027 §3.2), never on the
reports page.

### 11.1 Declaration

A report action carries `[CodedReport]`. A hatch report, whose client fetches through `run` or
renders a `component` that fetches its own data, has no report action and registers in composition.

```csharp
// Tellma.Distro.Acme — the server side of Acme's credit exposure report (spec 0018 §1.4):
// per customer, the credit limit, open invoices posted on or before AsOf, the headroom and an over-limit flag
[ApiRoute("acme-reports")]
public sealed class AcmeReports(IGuardedBatchRunner runner, IReportResults results)
{
    [ApiAction("credit-exposure", Resource = "acme.CreditExposureReport", Action = "Read", Mutation = false,
        Idempotent = true, Long = true)]
    [CodedReport("acme-credit-exposure", "Acme_Report_CreditExposure", ReportKind.Summary)]
    public Task<ReportResult> CreditExposureAsync(CreditExposureArguments arguments);
}
public sealed record CreditExposureArguments(                   // positional: parameter order = constructor order
    [property: ReportParameter(required: true, hasDefault: true)] DateOnly? AsOf,   // the method applies AsOf ?? today
    [property: ReportParameter(required: false, hasDefault: false, Entity = "acme.Customer")] int? Customer);
// composition: contribution.ApiService<AcmeReports>() registers acme.CreditExposureReport × Read (spec 0014 §4.2)

// a hatch report, registered with its securable and its optional export action
contribution.CodedReport<StatementArguments>("acme-statement", "Acme_Report_Statement", ReportKind.List,
    securable: ("acme.StatementReport", "Read"), exportAction: (typeof(AcmeStatements), nameof(AcmeStatements.ExportAsync)));

// Tellma.Core.Abstractions.Reports
[AttributeUsage(AttributeTargets.Method)]
public sealed class CodedReportAttribute(string key, string titleKey, ReportKind kind) : Attribute
{
    public string? DescriptionKey { get; init; }
    public string? ExportAction { get; init; }                  // nameof a method on the same class: a custom export (§12.1)
}
[AttributeUsage(AttributeTargets.Property)]
public sealed class ReportParameterAttribute(bool required, bool hasDefault) : Attribute
{
    public string? LabelKey { get; init; }                      // default: <titleKey>_<Key>
    public string? Entity { get; init; }                        // Numeric only: the entity whose key it is
    public string? Display { get; init; }                       // a §4.6 display, such as "integer"
    public int Decimals { get; init; } = -1;                    // -1: unset
    public ReportGrouping Grouping { get; init; }               // Unset: the display's default
    public string? DateStyle { get; init; } public string? Calendar { get; init; }
    public string? Enum { get; init; }                          // a named enum (§3.2)
    public string[]? ChoiceValues { get; init; } public string[]? ChoiceLabelKeys { get; init; } // parallel arrays
}
public enum ReportGrouping { Unset, On, Off }

// FeatureContribution extension (Tellma.Core.Abstractions.Reports): a hatch report; adds spec 0011 §2.1's CodedReportContributionItem
public static FeatureContribution CodedReport<TArguments>(this FeatureContribution contribution, string key, string titleKey,
    ReportKind kind, (string Resource, string Action) securable, (Type Handler, string Method)? exportAction = null,
    string? descriptionKey = null);
```

- **Positional arguments.** `key`, `titleKey` and `kind` are positional, because `ReportKind.List`
  is 0 and an omitted named `Kind` would silently mean a list. `required` and `hasDefault` are
  positional, so a required parameter always states whether its method supplies a default. A
  member without `[ReportParameter]` is optional with no default.
- **Parameters come from the arguments record.** The record is positional, and the parameter order
  is the primary constructor's: reflection's property order is not guaranteed, and the metadata
  emit is deterministic (spec 0024 §3.2). A key is the member name (`AsOf`); the wire stays
  camelCase (`asOf`). Value-type members are nullable, and null means omitted. Each member type
  maps to §4.1's list:

  | Member type | Parameter type |
  |---|---|
  | `int`, `long` | `Numeric`, with the `integer` hint, so saves and URLs refuse `2.5` |
  | `decimal` | `Numeric` |
  | `string`; `bool`; `DateOnly` | `String`; `Bool`; `Date` |
  | `DateTime`; `DateTimeOffset`; `Guid` | the same names |

- **Format members are attribute constants.** Attribute arguments cannot be nullable value types
  or records, so `Decimals` −1 and `ReportGrouping.Unset` stand for unset, and choices are two
  parallel arrays. The registry reads them as one `ReportFormat` (§3.1).
- **Defaults are the method's.** The method applies them (`AsOf ?? today`) and echoes them with
  source `Default`. `hasDefault` tells the client, dashboards and the MCP tool schema that it will
  (spec 0034 §4.3, spec 0035 §5.3, spec 0016 §11.5).
- **The action contract.** A report action has `Mutation = false`, `Idempotent = true`,
  `Long = true`, one body record of scalars, and the result `ReportResult`. That is enough for the
  platform to run, cancel, retry and export it, with the long timeout and the `tellma-reports`
  policy a stored report gets (§2.2, spec 0016 §8.2).
  Its result is one page (§11.3).
  **Example —** Acme's credit exposure body is `{ "asOf": "2026-10-20", "customer": 1012 }`.
- **On an entity's own service.** A report action may sit on an entity's service with
  `Action = "Read"`. Its securable is then the entity's `Read`, which every reader of the record
  already holds, so a details card can show it without a new grant (spec 0034 §2.1).
- **Data access is the report's decision.** The recommended pattern reads under the viewer's
  `RequireAsync(root, "Read")` filter. Aggregating rows the viewer may not see is a deliberate,
  code-reviewed data grant, named by the report's own securable.

### 11.2 The registry and its startup check

```csharp
// Tellma.Core.Abstractions.Reports
public interface ICodedReportRegistry { IReadOnlyList<CodedReportDescriptor> Reports { get; } CodedReportDescriptor? Find(string key); }
public sealed record CodedReportDescriptor(string Key,
    string Feature,                                             // the contributing feature, as WidgetTypeDescriptor.Feature
    string? Route,                                              // the report action's route; null for a hatch
    SecurableRef Securable, ReportKind Kind, string TitleKey, string? DescriptionKey,
    IReadOnlyList<CodedReportParameter> Parameters,             // constructor order
    CodedReportExport Export, string? ExportRoute);             // ExportRoute: the export action's, for Custom
public sealed record CodedReportParameter(string Key, string Type, bool Integer, string? Entity, bool Required,
    bool HasDefault, string LabelKey, ReportDisplay? Display, ReportFormat? Format);
public enum CodedReportExport { Platform, Custom, None }        // Platform: a route and no export action; None: a hatch without one
```

The startup check `core.coded-reports` builds the registry from both forms, and
`contribution.CodedReport<T>()` registers its securable as an action does (spec 0014 §4.2). The
check refuses:

- an `[ApiAction]` that returns `ReportResult` and has no `[CodedReport]`, other than
  `reports/run`. A `run` hatch therefore calls a hand-mapped endpoint (spec 0016 §4.5), or an
  action that returns something else;
- a `[CodedReport]` action that breaks §11.1's action contract; a `MemberOnly` action, which has no
  securable for the launcher; an action on a stack companion, which every stack projects (spec 0015
  §11.3) and so would yield one key per stack;
- a duplicate key, a key that breaks spec 0024 §9.1's rule or is longer than 64 characters (the
  `ReportKey` column, spec 0035 §3.1), or one whose first segment is a reserved path. This is the
  server's twin of the client's `reserved-path`, which never sees a key that has no client patch;
- an arguments record with:
  - a member that has no primary-constructor parameter, or that carries `[JsonPropertyName]`;
  - a non-nullable value-type member, or a member of an unsupported type;
  - keys that break §4.1's grammar, or that collide ignoring case;
  - more than 20 parameters (§4.5);
  - `Entity` on a non-`Numeric` member, or naming an entity that is not queryable;
  - a display or format that breaks §4.6;
- an export action that does not exist, is not an `[ApiAction]` with `Mutation = false`,
  `Idempotent = true`, `Long = true` and `Mcp = McpExposure.Hidden` returning `ExportOutcome`, or
  differs from its report in arguments type, `Resource` or `Action`. Spec 0016 §11.6 keeps it off
  the MCP tools.

### 11.3 Running

`CodedReportInterceptor` wraps every `[CodedReport]` action in `IApiActionInvoker` (spec 0015
§11.3), after the securable check:

- **Required parameters.** A missing `required` parameter without `hasDefault` answers
  `422 Report.ArgumentRequired` before the method runs. Its path is the member's camelCase name on
  the report's own route, whose body is the arguments record (`asOf`), and `arguments.<Key>` on
  `reports/export` (`arguments.AsOf`).
- **Undeclared defaults.** An echo with source `Default` for a parameter declared without
  `hasDefault` logs `ReportEvents.DefaultUndeclared` (warning).
- **Telemetry.** It emits `tellma.reports.run.duration` with `source` `coded`, and the activity tag
  `report.key` (§14).
- **Title.** A result without a `Title` takes the descriptor's `TitleKey`, rendered in the
  request's content slot, so the export's file stem (§12) and its job label (§12.1) always have
  one.
- **Kind.** A result whose `Kind` differs from the descriptor's fails the run with a 500
  (`InvalidOperationException`), so a renderer never meets a shape it did not expect.

`IReportResults` builds results for report actions and export actions alike:

```csharp
// Tellma.Core.Abstractions.Reports
public interface IReportResults
{
    ReportResult Table(ReportKind kind, QueryRowSet rows, IReadOnlyList<ReportColumnSpec> columns,
        IReadOnlyList<ReportArgumentValue> arguments, string? titleKey = null); // List or Summary only; wraps rows the method read itself, at most MaxTake
    Task<ReportResult> RunAsync(string root, ReportKind kind, ReportDocument document,
        IReadOnlyDictionary<string, JsonElement>? arguments, string? titleKey = null); // a document built in code: ReportSource.Code; one page of at most MaxTake rows
    Task<ExportOutcome> ExportAsync(ReportResult result);      // the platform's workbook of a result (§12); title and file stem: its Title
}
public sealed record ReportColumnSpec(string? LabelKey, ReportColumnRole Role, ReportColumnKind Kind,
    IReadOnlyList<int> Ordinals,                                // the row-set columns, as ColumnOrdinals (§9.2)
    string? Entity = null, ReportDisplay? Display = null, ReportFormat? Format = null,
    string? BlobKind = null);                                   // Image and File columns
```

- **One page.** A coded result is one page: report actions and `IReportResults` take no
  `ReportPage`, and the result's `Count` is its row count with `CountCapped = false`. The host shows
  the count and no pager (spec 0034 §3.1), and a `Platform` export writes the result once (§12.1).
- **`Table` takes `List` and `Summary` only.** A coded pivot or chart builds a document and calls
  `RunAsync`, so the engine builds its cube or chart under §8.8's caps.
- **`Table` refuses** a `Pivot` or `Chart` kind, and an `Image` or `File` column with no
  `BlobKind`, an unregistered one or other than two ordinals. It throws `InvalidOperationException`,
  so the run fails with a 500. More than `MaxTake` rows throw
  `LimitExceededException("MaxTake", rows, MaxTake)`, a 413 `limit-exceeded`; a method that may
  exceed it reads `MaxTake + 1` rows.
- **`RunAsync`** runs one page of `MaxTake` rows with §8.4's `take + 1` probe. When the extra row
  arrives it throws the same 413. Its document is validated as an inline one (§8.3).
- **A custom export** builds the table it wants with `Table` and returns `ExportAsync`, so it writes
  no Open XML. `ExportAsync` applies the interceptor's title rule (above) to a result without a
  `Title`, using the descriptor that `export` resolved by `Key` (§12.1), so the workbook title and
  file stem are never empty. A workbook beyond spec 0019's table writer, such as several sheets,
  is the action's own, with any library the distribution references.

### 11.4 Client metadata

`CodedReportClientAssetContributor` owns the `codedReports` section of the Core metadata chunk
(spec 0024 §3.1, §3.2), as `WidgetTypeClientAssetContributor` owns `widgetTypes` (spec 0035 §4.1).
Each entry is
`{ key, feature, route?, securable, kind, titleKey, descriptionKey?, export, parameters: [{ key, type, integer?, entity?, required, hasDefault, labelKey, display?, format? }] }`.

- **Base declaration.** The entry is the declaration that spec 0034 §2.2's `tmCodedReport` patches
  with placement and hatch code. Title, kind, securable and parameters are the server's.
- **Never the export route.** The client exports by key (§12.1).
- **Small.** The Core chunk is on the first-render path, so entries carry string keys, never
  texts, and the section keeps to spec 0024 §12's budget.

## 12. Export

Every kind exports to one `.xlsx` sheet through spec 0019 §4.4's table writer. The export runs the
report exactly as `run` would, under the same permissions and arguments. One layout step turns
results into a sheet of typed cells, and two writers serialize it: `.xlsx` for people, and
Markdown for the MCP report tool, which waits for its own spec.

### 12.1 Routes

```csharp
[ApiAction("export", Action = "Read", Mutation = false, Idempotent = true, Long = true)] // an .xlsx stream
public Task<ExportOutcome> ExportAsync(ReportExportRequest request);
[ApiAction("export/start", Action = "Read", Mutation = true, Idempotent = false, AllowWhenReadOnly = true)] // 202 JobAccepted
public Task<JobAccepted> StartExportAsync(ReportExportRequest request);

public sealed record ReportExportRequest(
    int? Id, ReportInline? Inline,                       // a stored or an inline report, or
    string? Key,                                         // a coded report's key ('acme-credit-exposure'); exactly one of the three
    IReadOnlyDictionary<string, JsonElement>? Arguments, int? DashboardId = null);
```

- **Stored and inline reports** load as `run` loads them (§8.2), the dashboard read-through
  included, and read through `IReportEngine.PagesAsync` (§8.1).
- **Coded reports.** `Key` resolves through `ICodedReportRegistry`; an unknown key is
  `400 bad-request`. The descriptor's `Export` decides the rest:
  - `Platform`: the export invokes the report action through `IApiActionInvoker`, with `Arguments`
    deserialized into the action's arguments record under `TellmaJson.Options` (spec 0016 §3.1),
    keys matched ignoring case (§4.4) and an absent key leaving the member null, under the action's
    own securable, synchronously or in the background as below.
  - `Custom`: `export` invokes the export action with the same record and streams its
    `ExportOutcome`.
    Custom exports are synchronous: their `413` names no `export/start`, and `export/start`
    refuses such a key with `400 bad-request`.
  - `None`: both routes refuse with `400 bad-request`, and the client offers no Export.
- **Synchronous export.** `export` handles a list or summary of at most `MaxSynchronousExportRows`
  rows. A pivot or a chart always fits, because §8.8 already bounds it. Above the limit, `export`
  answers `413 Excel.Export.RowLimitExceeded`, naming `export/start`.
- **Background export.** `export/start` enqueues `core.export` with `ReportExportJobArguments`,
  whose `Resource` is `core.Report` for every report, coded ones included, and the label
  `Core_Job_ExportReport { title }` (spec 0019 §12.2). The handler re-runs the report as
  the run-as user (spec 0019 §12.3), resolving a `Key` through the registry the same way. It is
  allowed on a read-only tenant (spec 0015 §2.3), since it only reads.
- **Pages.** A stored or inline list or summary exports up to `MaxExportRows`, or
  `MaxSynchronousExportRows` through `export`, in pages of `MaxTake`. The first page's `Count`
  refuses a larger export before any row is written. A pivot or a chart is one page. A coded
  report's result is one page (§11.3): the export writes it and never pages.

### 12.2 The layout

```csharp
// Tellma.Core.Reports
public interface IReportLayout
{
    ReportSheet Plan(ReportResult first, ReportSheetShape shape);
    IAsyncEnumerable<ReportSheetRow> Rows(ReportSheet sheet, IAsyncEnumerable<ReportResult> pages);
}
public enum ReportSheetShape { Compact, Flat }                  // Compact: Excel; Flat: one header row, for Markdown
public sealed record ReportSheet(string Title, IReadOnlyList<IReadOnlyList<string?>> HeaderRows,
    IReadOnlyList<ReportSheetColumn> Columns, int FrozenColumns);
public sealed record ReportSheetColumn(string Header, string? Path, QueryexType Type, int? Scale,
    ReportDisplay? Display, ReportFormat? Format, ReportColumnRole Role); // the effective display and format (§9.1)
public sealed record ReportSheetRow(IReadOnlyList<object?> Cells, ReportRowKind Kind, int Depth);
public enum ReportRowKind { Normal, Phantom, Subtotal, Total }
```

The layout owns every rule about what a sheet holds; the writers only encode cells.

- **Header.** The label in the request's content slot, else the label key's text, else the slash
  label (spec 0019 §3.3), else the expression text.
- **Columns.** One per term, except image columns, which are left out, as the search page's export
  does (spec 0030 §13.2). A navigation shows its display name (`IDisplayNames.Render`), a text term
  its active slot, and a file its `FileName`. Every column carries its effective display and format.
- **Totals.** A summary with `totals: true` always ends with a "Total" row over every exported row,
  however many pages it took. The engine adds each page's partials as it reads, and the last page
  carries the totals (§8.1). A text or hierarchy `min` or `max` total is exact when the data did
  not change between pages.
- **Lists, summaries and charts** are one flat table in both shapes. A chart exports its dimension
  columns, then the measure; the chart image is not exported.

### 12.3 The pivot layouts

**Compact,** for Excel, exports the pivot fully expanded:

- **Rows.** One row-header column holds every row member, indented by `Depth − 1` and followed by
  its children, the phantom first. The grand total row comes last.
- **Columns.** After the row header come the attribute columns, then one data column per pair of
  column member and measure. Members follow the screen's order (spec 0034 §6.2): an expanded
  member's children, then its subtotal column. The "Total" column comes last.
- **Headers.** One header row per column level, plus a row of measure names when there are two or
  more measures. No cells are merged.
- **Size.** Beyond 16,384 columns or 1,048,576 rows, export fails with `413 Report.ExportTooWide`.

**Flat,** for Markdown, has one row per cube cell, so a reader never joins members to cells:

- **Columns.** A tree dimension gets one column per level present, and a flat dimension one column;
  then the attributes, then the measures.
- **Rolled-up rows** stay, with "(Total)" in each rolled-up position, so no total is computed by
  the reader.

### 12.4 The writers

**`ReportExcelWriter`** maps a sheet onto spec 0019 §4.4's `ExcelTableSpec` and rows. The sheet is
titled by the report, and panes freeze after the header rows and `FrozenColumns`.

- **Formats** follow the type, display and format (spec 0019 §3.2). `ExcelTableSpec.NumberFormats`
  holds one format per column:
  - `number` and `integer` write `#,##0` with `format.decimals` places (§3.2), or `0` in place of
    `#,##0` under `grouping: false`; `percent` is an Excel percent with those places;
  - `month` is a date serial formatted `[$-CCLLLL]mmm yyyy`, where `CC` is the Windows code of the
    column's calendar (spec 0019 §3.2). A calendar without one, such as `et`, writes text from
    `ICalendarSystem` (spec 0013 §8.1);
  - `quarter` and `year` write text in the column's calendar, as the Markdown writer does:
    `2026-Q2`, `2026`;
  - `date`, `datetime` and `instant` write as spec 0019 §3.1 writes `DateOnly`, `DateTime` and
    `DateTimeOffset`;
  - `monthName` and `weekdayName` write the name as text, from `ICalendarSystem.MonthName` (spec
    0013 §8.1) in the column's calendar and from the culture's day names;
  - `enum` writes the label as text, in the request's content slot.
- **Row styles** are spec 0019 §4.4's `ExcelRowStyle`: `Total` for the grand total; `Subtotal1`,
  `Subtotal2` or `Subtotal3` for a subtotal at depth 1, 2, or 3 and deeper; `Phantom` for a phantom
  child; `Normal` for any other row. `Depth − 1` gives `Indent`.

**`ReportMarkdownWriter`** lives in `Tellma.Core.Mcp`, beside `tellma_query`'s renderer (spec 0016
§11.5), and shares its escaping and size cap. It writes the Flat shape as one GFM table under a
column legend that names each header's path and type, so a reader can follow up with
`tellma_query`. Labels stay content in the request's slot; values are culture-invariant, in spec
0016 §3.2's encoding:

- `number` and `integer`: `format.decimals` places (§3.2), no grouping; `percent`: `12.5%`; `bool`:
  `true` or `false`;
- `date` and `datetime`: ISO; `instant`: ISO in the tenant zone with its offset;
- `month`, `quarter` and `year`: `2026-03`, `2026-Q1`, `2026` in the column's calendar, which the
  legend names;
- `monthName` and `weekdayName`: the number, so a follow-up filter can use `month(d) = 9`;
- `enum`: the label; a navigation: its display name, plus an `Id` column per navigation dimension;
- (None) prints as `(None)`, and a null as an empty cell. `|` escapes as `\|`, line breaks become
  spaces, and no bidi control character is inserted.

## 13. Seeded (standard) reports

A pack or a distribution ships standard reports as seeds. A provisioning step keeps them in every
tenant: with no owner, read-only but shareable (§5.1), and published to the roles each seed names.

```csharp
// contribution.ReportSeed(seed) (spec 0011 §2.1) adds a ReportSeedContributionItem
public sealed record ReportSeed(
    string Code, string Root, ReportKind Kind, string DocumentResource,   // an embedded JSON resource of the feature's assembly
    string TitleKey, string? DescriptionKey,
    IReadOnlyList<(string RoleCode, ShareAccess Access)> PublishTo, // role grants, written on insert only
    int Version);                                                   // raised when the document or titles change
```

- **The steps.** The realiser of `ReportSeed` adds one provisioning step per seed, named
  `core.report-seeds/<Code>`, at `Order` 900, after every pack and distribution step, so the roles
  a seed names exist. Its `Version` is the seed's: a new seed runs once, a raised `Version` re-runs
  its step (spec 0011 §6.2), and a removed seed's row stays as it is.
- **The upsert.** A step upserts its row by `Code` as an enlisted save in its provisioning frame
  (spec 0011 §6.2), with `OwnerId` null and titles from `TitleKey` in each tenant language. A
  re-run rewrites the document and titles.
- **Reserved codes.** A seed's `Code` starts with its feature's `Name` and a dot
  (`acme.sales-by-center`); the realiser refuses any other as a composition problem. Outside a
  system scope, a save that sets `Code` on insert, or changes it, to one that starts with a
  composed feature's `Name` followed by `.`, ignoring case, is refused: `Report.CodeReserved` at
  `[i].Code`.
- **Owned rows are never adopted.** When the row that holds the seed's `Code` has an owner, the
  step writes nothing and logs `ReportEvents.SeedCodeTaken` (warning), so an upgrade never
  overwrites a user's report.
- **Grants** are written on insert only, by one declared-write batch statement into
  `core.ReportShares` in the step's frame, so an administrator's later shares stick. A named role
  that the tenant lacks is skipped with the warning `ReportEvents.SeedRoleMissing`.
- **Acme** seeds `acme.sales-by-center`, §3.5's pivot, at `View` to the role `Sales`.

## 14. Configuration and telemetry

`ReportsOptions` binds `Tellma:Reports` and is validated at startup. It holds §8.8's limits and
`PlanCacheSize` (default 512, §8.10). `SharingOptions` binds `Tellma:Sharing` and holds
`MaxAudienceMembers` (default 2,000, §6.2). The engine, the coded-report interceptor (§11.3) and
the audience resolver emit the signals below.

| Instrument | Tags |
|---|---|
| `tellma.reports.run.duration` | `kind`; `source`: stored, inline or coded; `outcome`: ok, error or cancelled |
| `tellma.reports.rows` | `kind` |
| `tellma.reports.cancelled` | — |
| `tellma.reports.export.duration` | `kind`; `mode`: sync or background |
| `tellma.sharing.audience.members` | `resource` |

- **Logs.** `ReportEvents.DefinitionInvalidAtRun` (warning; the id and codes, never the text),
  `ReportEvents.LimitExceeded` (information), `ReportEvents.MeasureFailed` (information for a 422,
  error for a 500 (§8.11); the id, name or key, `pointer` and `reason`, never the text; the cell
  only at error), `ReportEvents.DefaultUndeclared` (warning; the key and parameter, §11.3),
  `ReportEvents.SeedRoleMissing` (warning) and `ReportEvents.SeedCodeTaken` (warning; the code,
  §13).
- **Activity.** `report run`, tagged `report.id`, `report.key` or `report.inline` (an inline
  report's name), and `report.kind`.

## 15. Testing

Unit suites pin the document, the compiler and the cube; integration suites run on real SQL. All
run on PR except the performance suite, which runs nightly.

- **Document, validator, corpus** (unit). Each §3.4, §4.5 and §4.6 rule, `EnumSourceInvalid`,
  `ChoiceInvalid` and the `running` refusal included; normalization; a stated `entity` against an
  inferred one; checked-in documents with their expected diagnostics (`pointer`, code, span);
  QX4010 and QX4011 on a pivot measure and on a summary measure with totals, and neither without
  totals; a keys-only summary, its count and drill, and `TotalsNeedMeasure`; every enum value
  round-tripping in §3.1's spelling (`datetime` for `DateTime`) under `TellmaJson.Options`, and an
  unknown member refused.
- **Default display** (unit). One vector per row of §3.2's table, an omitted `cal` inferring
  `'gc'`, an enum path, a stated member overriding an inferred one member by member, an edited
  expression taking its new default, and default decimals from `Scale`: 0 for a count and under
  `integer`, `Scale` − 2 under `percent`, 6 for a floating result, and a `Scale` above 6 cut to 6.
- **Default title** (unit). Each slot in its tenant language, a typed slot kept, the 255 cut, and
  re-derivation on a `Root` or `Kind` change only while the slot equals the old default.
- **Compiler and SQL goldens** (unit). §8.4 per term kind and shape; a pivot's leaf, member
  and partial columns for flat, tree and mixed axes; a summary's partial columns and its
  `take + 1` probe; identity columns; ordering per slot; one golden per kind and per Part F item
  used (spec 0010).
- **Cube builder** (unit). Chains; the phantom on and off; (None); two trees on one axis; each
  combine rule; §8.6's composite measures; text ties; null partials; `having` per §8.6 (members,
  phantoms and totals over survivors); every cap, `MaxPivotCellPartials` included; member order;
  byte-identical output for shuffled rows.
- **Runs** (integration):
  - §4.4's table, with URL-text values of every type; defaults share the query's `today()`;
    `'uq'` and `'et'` grouping;
  - an omitted optional `@From` in `Invoice.PostingDate >= @From` returns every row, and a summary
    whose measure condition names it reads the partial columns of step 5's `CompiledMeasures`;
  - every cell of a two-tree pivot equals a brute-force `descendantOf` query per measure;
  - §8.4's totals rule at its boundary: exactly `take` rows, `take + 1`, `top` equal to and below
    `take`, and `Skip` above 0;
  - a summary total equals its rows, read through `ColumnOrdinals` with null key cells;
  - `422 Report.MeasureFailed` from a summary row (SQL 8134) and from a pivot cell (QX6001), with
    the same `reason`. The pivot's 422 carries its `pointer`, `row` and `column`, with cases for an
    outer subtotal's shorter `row` and a value member's caption;
  - the same failure as a 500, logged at Error, from a standard report, a named inline report and
    a `ReportSource.Code` document;
  - a measure over `today()` and an argument, evaluated with the statement's values;
  - traversal 403 with `restricted-path`; a tree pivot for a viewer whose `Read` on the tree
    entity is filtered: no `restricted-path`, the visible leaf nodes' ancestors labelled from the
    related projection, and no other node in the result; round-trip counts; an abort reaches SQL;
  - the plan cache: one report id in two tenants, and one report run by two viewers with different
    `Read` filters, each get their own rows; a save, a language added to the tenant and a request
    in another content language each miss the cache; an inline document re-sent with other
    whitespace and member order hits it;
  - the read-through load: shown, not shown, dashboard invisible and an `Unrestricted` dashboard
    decision, each equal to a two-step reference check;
  - drill payloads: a measure condition, the `or` of a summary row's, none when a partial has none
    or folds to `true`, `LeavesOnly` under `having` and under a summary's `top`, and a tree member
    whose children all failed `having` drilling on its own key.
- **Sharing and audience** (integration). Every criterion and reason kind; `Edit` saves and `View`
  does not; the Everyone row and its uniqueness; standard rows share but refuse save and transfer;
  a share never stamps the row; delete removes the shares; one SQL text for two users; an expired
  and a future membership; `shared-with-me`; the `permissions` and `shares` extras; `CannotRead`
  per grant and per member, a bespoke `Read` criterion counting for all; a service account in a
  shared role, a public role and a role with a permission; a deactivated grantee with
  `Members: 0`; hidden `Members`; the cap; a role permission with more than 500 holders counted
  exactly.
- **Child routes, coded reports, seeds** (integration). `Via` rebasing, the owner path,
  grandchildren, a child export; every `core.coded-reports` refusal of §11.2; both declaration
  forms in the registry and the `codedReports` emit; the interceptor's 422 and its warning; the
  interceptor's 500 on a result whose `Kind` differs from the descriptor's; `Table` refusing
  `Pivot` and `Chart` with a 500 and `MaxTake + 1` rows with a 413; a code document's list of
  `MaxTake + 1` rows refused with the same 413, and one with a §3.4 error failing with a 500; a
  `Platform` export of a coded report invoking its action once and writing its one result; Acme's
  credit exposure runs and exports by key under its securable; a `Custom` export, one whose table
  has no title taking the descriptor's `TitleKey`, its refused `export/start`, and a `None` key;
  seed insert, rewrite, grants on insert only, and missing role; a removed seed leaving its row
  while another seed's raised `Version` re-runs only its own step; a user's `Code` under a
  feature's prefix refused with `Report.CodeReserved`, and an owned row keeping such a code saving
  other edits; a seed whose `Code` an owned row holds skipped with `SeedCodeTaken`.
- **Export** (integration). The layout's sheets for each kind and shape, independent of bytes: image
  columns left out, a file's `FileName`, the Flat pivot's level columns and "(Total)" rows. Each
  kind's workbook (headers, styles, indent, panes, RTL view, formats, `enum`, `monthName`,
  `grouping: false`, default decimals, a `uq` month under a Gregorian request, an `et` month as
  text, and `quarter` and `year` as text); the Markdown encoding per display; the 413; the
  background job and `core.export.ready`; a summary's export Total equals the screen's whenever both
  exist, and a multi-page export totals every page.
- **Performance** (nightly). A pivot over 1M lines, with a 6-level tree and two column dimensions,
  runs under 2 s on the CI SQL tier. A pivot at the caps keeps the managed heap under 512 MB:
  250,000 leaf rows, a 6-deep tree × 3 column dimensions, and cells × partials at
  `MaxPivotCellPartials`.

## 16. Definition of done

Every section (§2–§14) ships with its securables, routes and codes, and passes §15's suites.

- **UI rules** are spec 0034's to meet. This spec serves them:
  - nothing of a report is a launch document;
  - instant feedback: an inline report runs in one round trip, and results are self-describing
    (§8.3, §9);
  - non-blocking: every read is cancellable (§8.9);
  - stable layout: cube shape and member order are deterministic (§9.3);
  - RTL: exports set a right-to-left sheet view in an RTL culture.
- **Acme** (spec 0018 §1.4) adds the coded report `acme-credit-exposure` at
  `acme-reports/credit-exposure`, the seed and reports over a child root. Its end-to-end test
  shares a stored report (the grantee's bell shows it), publishes one, reads the audience, runs and
  exports each kind, and drills through.
- **Numbers.** Every pivot cell and summary total equals SQL Server's own value and scale for the
  same rows (§8.6, §15's runs), and spec 0010 §F.15's differential suite passes.

## Decisions record

1. **"Stored report" and "coded report"; the table is `core.Reports`** — the names say what each
   is: stored data, or code. "Hardcoded" reads as a defect and "tier 2" is internal. "Definition"
   keeps one meaning, the JSON document. The UI marks a seeded row "Standard" and a coded report
   "Built-in" (spec 0034) (§1, §2).
2. **Coded reports have no rows and are declared once, on the server** — the server registry and
   the securable suffice, and the client adds only placement and hatch code. Read-only rows would
   duplicate the registry and drift with every deploy, and a client-only declaration could not be
   checked on dashboards or exported by key. Listing coded reports beside stored ones would present
   two access models, roles and sharing, as one, so they appear only in the launcher (§11).
3. **Kinds `List`, `Summary`, `Pivot`, `Chart`** — familiar words. "Non-aggregate, aggregate flat,
   aggregate pivot" is long; "tabular, grouped, crosstab" is less familiar (§3).
4. **One versioned document with exactly one kind block** — it is always read whole; `Root` and
   `Kind` stay columns for lists. Child tables for terms were rejected (§3.1).
5. **Terms are column keys or expressions; navigation labels read as `max(...)`** — a report expands
   like the search page and keeps one grouping key per dimension (§3.1, §8.4).
6. **Pivots group at leaf grain in SQL and roll up in C#** — on the hard shapes at 10M facts it is
   4–21× faster than grouping sets (0.8–2.1 s), with a steady ~100 MB memory grant. Trees show
   full depth, and no level syntax enters the language. Grouping sets over level navigations were
   the alternative (§8.5, §8.6).
7. **An omitted optional parameter imposes no restriction** — atoms fold by polarity. Binding null
   returns no rows, and `@p is null or …` burdens every author (§4.4).
8. **Defaults run as a constant statement before the query** — one engine entry point. Per-use
   parameter expressions would save the round trip but need a second engine amendment (§4.3).
9. **Sharing is a constant criterion with an `Exists` semi-join** — one plan serves every user.
   Per-user id lists are unbounded, and a materialized audience goes stale (§5.3).
10. **Members author and share their own; `Publish` and `Transfer` are role grants; shares change
    only through actions** — every user can author, and an `Edit` grantee cannot re-share (§5.2, §5.4).
11. **The audience lists members with reasons and counts data readers per grant and per member** —
    it answers "who and why" with no per-member permission evaluation. Stored grants and bespoke
    criteria count, filters do not, so the warning names members who would be refused (§6).
12. **Delete removes for everyone after the host's confirmation; a dashboard blocks it** — passing
    ownership to the system leaves content nobody answers for; soft delete adds a third state (§5.6).
13. **Drill-through uses server-bound filter text, measure conditions and optional `drillColumns`,
    in the URL fragment** — URL arguments would change `query`'s meaning, and default columns alone
    miss the measure's source column. The fragment never reaches the server, so a bookmarked or
    signed-out cold load meets no request-line or query-string limit. Under `having` only leaf
    cells drill, because a group-level condition cannot become a row filter (§3.3, §9.4).
14. **Running totals wait** — `running` is reserved and a window-function amendment is additive.
    Client accumulation is wrong across pages (§3.1).
15. **Pivot totals are visual** — `having` filters leaf rows in SQL, and every subtotal and total
    is recomputed over the survivors. Totals therefore add up to the cells shown. The alternative
    was SQL's semantics, where subtotals include every filtered row (§8.6).
16. **Summary totals come from C# over the returned rows, only when the page holds the whole
    result** — the total then matches the rows shown, after `having` and `top`. The alternative,
    a SQL totals statement over the surviving groups, needs a derived-table emission (§8.4).
17. **Arithmetic failures fail the run, from C# and SQL alike, and the status says who can fix the
    document** — a 422 when a tenant user can edit it, so the author guards the measure; a 500 and
    an Error log when only a developer can, since a user can do nothing with a 422 on a standard or
    code-declared report (§8.11).
18. **Shares live beside the row, and Everyone is a share row** — a share never stamps the row, so
    an open studio meets no 409 after someone shares, and only sharers read the grants: a child
    collection would have been queryable by every reader. An Everyone row is attributed like any
    grant, where a public flag on the row would stamp it (§2.1, §5.4).
19. **A dashboard read-through rides the report's load** — one `Or` in the load filter costs no
    round trip, where a separate dashboard check costs one on every tile run of a viewer without a
    share. It stays off the access criteria, so it admits runs, never the stored report (§5.7, §8.2).
20. **Default titles are stored, per slot in its tenant language** — search, sorting, pins and
    notifications read a real title; a title computed at read time would break them (§2.1).
21. **Plans hold no viewer's part and are keyed by tenant, stamp and slots** — one entry serves
    every viewer of a report, and a save or a settings change misses by key, so nothing is ever
    invalidated (§8.10).
22. **Coded results are one page of at most `MaxTake` rows** — a report action takes one record of
    scalars, so paging added later would break every pack's contract; a larger result belongs in a
    stored report, which the engine pages (§11.3).
23. **`Code` is an editable natural key; seed codes are reserved by their feature's prefix, and a
    seed never adopts an owned row** — `Code` is the `[NaturalKey]` that the seeds' enlisted upsert
    and any later import match rows by, so users keep it; the prefix stops a user taking a later
    seed's code, and the refusal keeps an upgrade from overwriting a user's report (§2.1, §13).

## Review flags

1. **Summary totals only when the page holds every row** (§8.4). Alternative: totals on long
   summaries from a SQL statement over the surviving groups, which needs a derived-table emission.
   Flips if users want totals on summaries longer than a page.
2. **An omitted parameter inside `or` lifts the disjunction** (§4.4, spec 0010 §F.8). Alternative:
   drop the atom from its connective. Flips on author confusion in practice.
3. **A stored run with defaults costs three round trips** (§8.3). Alternative: parameter
   expressions and an in-batch report check, one round trip warm. Flips if dashboards measure slow.
4. **Every member authors and shares; only administrators transfer** (§5.2). Alternatives: authoring
   through a role grant; owners handing over. Flips if tenants restrict authoring.
5. **Reports apply no activatable conjunct** (§8.3). Alternative: hide inactive master rows as the
   search page does. Flips if master-data reports surprise users.
6. **Every pivot cell comes from C#, leaf cells included** (§8.6): one code path, so a parent with
   one child shows exactly the child's number. Alternative: leaf cells from SQL beside the
   partials. Flips if spec 0010 §F.15's differential suite finds a class the evaluator cannot
   match.
7. **Exports always total** (§12.2). The screen totals a summary only when one page holds every
   row. An export reads every row anyway, so it always totals. Alternative: the screen's rule for
   exports too. Flips if an export should total only when the screen does.
8. **Share history loses who removed a grant** (§2.1). The share table's own history keeps when each
   grant ended, but SQL Server's temporal history records no user. Alternative: a removal log.
   Flips if audits ask who unshared a report.
9. **Coded results are one page** (§11.3; decision 22). Alternative: a `ReportPage` on report
   actions and on `IReportResults`, with the host's pager. Flips when a coded report needs more
   than `MaxTake` rows on screen or in an export.
10. **Pivot memory is bounded per run, not per instance** (§8.8, §15). One user may build ten cubes
    at once, four interactive and six batch, each up to `MaxPivotCellPartials`. Alternative: an
    instance-wide semaphore on cube builds. Flips if load tests or production show memory pressure
    from concurrent pivots.
