# Spec: Report Definitions and Engine

- **Author:** Ahmad Akra
- **Date:** 20 October 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Tellma reports come in two forms:

- **Report definition** — data. A stored JSON document over one queryable entity, in one of four
  kinds: list, summary, pivot or chart. Every run compiles it to Queryex.
- **Coded report** — code. One `[ApiAction]` that returns the same result shape, for a report the
  document cannot express.

This spec ships the server side of both: the stack, the document, parameters, sharing and the
audience, the run engine, child query routes, coded reports, Excel export and seeded definitions.
The client half is spec 0034.

Sharing shares the definition, never the data. Every run is the viewer's own query under the
viewer's own permissions, so a definition shared with someone who cannot read its rows shows them
nothing. Dashboards (spec 0035) reuse the sharing model whole.

Other specs that carry parts of this design:

- spec 0010 §F.8–§F.15 — grouping sets, level navigations, omitted parameters, constant statements, `BindParameters`, `'uq'`, `Exists`, `ComparedWith`.
- spec 0011 §2.1, §6.2 — `FeatureContribution.ReportSeed` and the `core.report-seeds` step.
- spec 0012 §5.2, §11.1, §11.4 — `IDataBatch.EvaluateConstant`; `TreeLevels` and the `'uq'` calendar map in the schema; level paths refused in entity selects.
- spec 0013 §8.2, §10.1 — `core.CalendarMonths`; the Core string keys of reports.
- spec 0014 §2, §5.3 — `core.ReportDefinition`, `Share`, `Publish`, `Transfer`; sharing among the bespoke criteria.
- spec 0015 §2.2, §2.3, §2.7, §5.2, §5.3 — child query surfaces, narrowed reserved segments, child securables, the traversal rule, computed extras.
- spec 0016 §2.1–§2.4, §4.1, §8.2, §8.3, §11.5 — routes and retry classes, `Queryable`, `tellma-reports`, `tellma-long`, `tellma_query`.
- spec 0018 §1.4 — Acme's coded report, its seed and the role `Sales`.
- spec 0019 §2.1, §4.4, §12.2, §12.3 — `ExportKind.Report`, the table writer, the export job's report case and label.
- spec 0021 §2.3 — the `core.report.shared` catalogue row.
- spec 0029 §6.1, §8.2, §9 — column keys, `tmQx.raw`, `queryex/discover`.
- spec 0030 §2.5, §17 — `searchRead` for the reports page; the drill-through list.
- spec 0035 §3.1, §6.3 — the widget foreign key; the `IDashboardReadThrough` implementation.

## Goals / Non-goals

**Goals**

- One versioned document per definition, checked at save against the tenant's schema (§3, §4).
- Four kinds, each compiled to one Queryex statement (§8). Pivot subtotals and tree rollups come
  from one `GROUPING SETS` statement with no fan-out.
- Parameters with defaults; an omitted optional parameter imposes no restriction (§4).
- Sharing with users, roles and everyone; an audience that says who sees a definition and why (§5, §6).
- Self-describing results that renderers and the Excel writer read with no other input (§9).
- Reports over child entities (§10); coded reports that run and export with one `[ApiAction]` (§11).
- Excel export of every kind (§12) and seeded standard definitions (§13).

**Non-goals (explicitly out of scope)**

- Renderers, parameter bar, studio, reports page, drill-through list page, share dialog and
  `tmCodedReport` — spec 0034. Dashboards and widgets — spec 0035.
- Stored definitions in the launcher — spec 0027 lists one Reports entry.
- Running totals, scheduled delivery, favourites and MCP report tools — later. `ReportTerm.running`
  is reserved (§3.1), and `run` with a draft is the assistant's entry point today (§8.1).

## 1. Placement and vocabulary

Reports add no project and no package reference. Contracts live in `Tellma.Core.Abstractions`, the
runtime lives in `Tellma.Core`, and `CoreFeature` composes both.

Contract blocks are TypeScript and C# sketches: names and shapes are normative; imports,
documentation comments, bodies and cancellation tokens are omitted.

- **Contracts** — `src/core/Tellma.Core.Abstractions/`, namespace `Tellma.Core.Abstractions.Reports`.
  The entities and document records; for running, the requests, results, `IReportEngine` and
  `IReportResults`; for sharing, `IShareable`, `IShareRow`, `IShareCommands<>`, `IAudienceResolver`
  and `IDashboardReadThrough`; and `ReportOptions`, `ReportSeed`.
- **Runtime** — `src/core/Tellma.Core/Reports/`. The stack: `ReportDefinitionService`,
  `ReportDefinitionValidator`. Running and export: `ReportCompiler`, `CubeBuilder`,
  `ReportExcelWriter`. Sharing: `ShareableService<,>`, `ShareableAccessCriteria<,>`,
  `ShareCommands`, `AudienceResolver`. Seeds: `ReportSeedProvisioningStep`.
- **Composition** — `CoreFeature` registers `Entity<ReportDefinition, ReportDefinitionService>()`,
  `Shareable<ReportDefinition, ReportDefinitionShare>()` (§5.1), the notification type (§7), the
  seed step (§13), and `ReportOptions` bound from `Tellma:Reports` (§14).
- **Tests** — `test/core/Tellma.Core.Tests/Reports/` and
  `test/core/Tellma.Core.IntegrationTests/Reports/` (§15).

The document's C# records are the only code that reads or writes the document.
`JsonSchemaExporter` exports their schema.

**Vocabulary**

- *Report definition* — a stored `core.ReportDefinitions` row. Its JSON is the *definition
  document*, or just *document*.
- *Coded report* — a C# `[ApiAction]` that returns `ReportResult` (§11).
- *Draft* — an unsaved document sent to `run` or `export`.
- *Run* — one execution of a report definition (stored or draft) or of a coded report.
- *Root* — the entity a definition queries, named by its `Root` column.
- *Term* — a column, dimension, measure or attribute of the document (§3.1).
- *Dimension* — a grouping term of a pivot or chart. *Measure* — an aggregate term.
- *Attribute* — a column beside a pivot's only row dimension, with one value per member.
- *Aggregate kinds* — summary, pivot and chart. A *grouping key* is an expression they group by.
- *Member* — of a tenant or a role: a user (§5, §6). Of a dimension: one of its values on an axis,
  such as one customer or one quarter (§8.5, §9.3).
- *Level* — one grouping position: one for a flat dimension, one per tree depth for a tree.
- *Phantom child* — the row of a tree member that holds the facts on the member itself (§8.6).
- *Fan-out* — a join that repeats a fact row once per match, inflating sums and counts.
- *Content slot* — slot 1, 2 or 3 of a multilingual value: `Title`, `Title2`, `Title3` (spec 0013 §7.5).
- *Decision* — spec 0014's answer for one securable: `Unrestricted`, `Filtered` or `Denied`.
- *Owner* — the user a definition belongs to. *Grantee* — the user or role a share names.
- *Standard definition* — a seeded definition with no owner (§13).
- *Audience* — who sees a definition, and why (§6).
- *Child query surface* — spec 0015 §2.2's read-only `query` over a child entity (§10).

## 2. The report definition entity

`core.ReportDefinitions` holds one row per definition, and `core.ReportDefinitionShares` holds its
user and role grants. The stack's segment is `reports` and its resource is `core.ReportDefinition`.

### 2.1 Classes and columns

```csharp
// Tellma.Core.Abstractions.Reports
[Table("ReportDefinitions", Schema = "core"), TableType, Temporal]
[ApiResource(Segment = "reports"), Stack(Operations = Query | Details | Save | Delete | Enlist)] // Enlist: the seed step (§13)
[DisplayNameSource(nameof(Title))]
public class ReportDefinition : TopLevelEntity, IActivatable, IShareable
{
    [NaturalKey] public string? Code { get; set; }                  // seeds only; ≤ 50
    [Multilingual, Searchable] public string Title { get; set; }    // required; ≤ 255
    public string? Title2 { get; set; } public string? Title3 { get; set; }
    [Multilingual] public string? Description { get; set; }         // ≤ 1,024
    public string? Description2 { get; set; } public string? Description3 { get; set; }
    public string Root { get; set; }                                // an entity name: 'acme.SalesInvoiceLine'
    public ReportKind Kind { get; set; }
    [JsonColumn] public string DefinitionJson { get; set; }         // the document (§3); ≤ 256 KB
    [Derived] public int LanguageVersion { get; set; }              // the Queryex language version of its expressions
    [Derived] public int? OwnerId { get; set; }                     // IShareable (§5.1); null: a standard definition
    [ServerOwned] public bool IsPublic { get; set; }                // IShareable; written by publish only
    [WriteOnce] public int? BasedOnId { get; set; }                 // IShareable; the definition a "Save as" copied
    [ServerOwned] public bool IsActive { get; set; } = true;
    [NotMapped, MaxChildren(250)] public IReadOnlyList<ReportDefinitionShare> Shares { get; set; } // written by actions only (§5.4)
}

[Table("ReportDefinitionShares", Schema = "core"), TableType]       // temporal with its parent
public class ReportDefinitionShare : ChildEntity, IShareRow
{
    [ParentKey] public int ReportDefinitionId { get; set; }
    public int? UserId { get; set; } public int? RoleId { get; set; } // exactly one is set
    public ShareAccess Access { get; set; }
    public int SharedById { get; set; } public DateTimeOffset SharedAt { get; set; } // set by the action
}

public enum ReportKind { List, Summary, Pivot, Chart }
public enum ShareAccess { View, Edit }
```

**`core.ReportDefinitions`** — temporal (`core.ReportDefinitionsHistory`), sequence
`core.sq_ReportDefinitions`.

| Column | Type | Null | Constraints and notes |
|---|---|---|---|
| `Id` | `int` | no | PK; `CK_ReportDefinitions_Id CHECK ([Id] > 0)` |
| `Code` | `nvarchar(50)` | yes | `UX_ReportDefinitions_Code WHERE [Code] IS NOT NULL` |
| `Title`, `Title2`, `Title3` | `nvarchar(255)` | `Title` no | content slots (spec 0013 §7.5) |
| `Description`, `Description2`, `Description3` | `nvarchar(1024)` | yes | |
| `Root`; `Kind` | `varchar(128)`; `varchar(8)` | no | `IX_ReportDefinitions_Root`; `List` … `Chart` |
| `DefinitionJson`; `LanguageVersion` | `nvarchar(max)`; `int` | no | |
| `OwnerId` | `int` | yes | `FK → core.Users`; `IX_ReportDefinitions_OwnerId (OwnerId) INCLUDE (IsActive)` |
| `BasedOnId` | `int` | yes | no FK: lineage only, and the source may be deleted later |
| `IsPublic`, `IsActive` | `bit` | no | `DF 0`, `DF 1` |
| audit set, period | | | spec 0012 §4.2 |

**`core.ReportDefinitionShares`** — temporal, sequence `core.sq_ReportDefinitionShares`, with
`CK_ReportDefinitionShares_Target CHECK (([UserId] IS NULL) <> ([RoleId] IS NULL))`.

| Column | Type | Null | Constraints and notes |
|---|---|---|---|
| `Id` | `int` | no | PK; `CK_ReportDefinitionShares_Id CHECK ([Id] > 0)` |
| `ReportDefinitionId` | `int` | no | `FK → core.ReportDefinitions` |
| `UserId` | `int` | yes | `FK → core.Users`; indexed; `UX_ReportDefinitionShares_User (ReportDefinitionId, UserId) WHERE [UserId] IS NOT NULL` |
| `RoleId` | `int` | yes | `FK → core.Roles`; indexed; the `Role` twin of `UX_ReportDefinitionShares_User` |
| `Access` | `varchar(8)` | no | `View` or `Edit` |
| `SharedById`; `SharedAt` | `int`; `datetimeoffset(3)` | no | `SharedById` `FK → core.Users` |

- **No version tag.** The client caches no definitions.
- **No `Export` or `Import` operation.** The routes `export` and `export/start` therefore belong to
  the report service's own actions (§12.1, spec 0015 §2.3).
- **`LanguageVersion`.** The service sets it after the before image loads (spec 0015 §6.2). It is
  `QueryexLanguage.Version` when any expression changed; otherwise it keeps the stored value.
- **An inactive definition still runs.** Deactivation hides it from `browse` (§5.5). `run` by id,
  and every dashboard that shows it, keep working.

### 2.2 Securables, operations and routes

Every route is under `/{t}/api/web/reports/` and requires one securable on `core.ReportDefinition`.
Retry classes are spec 0016 §2.2's. `run`, `export` and `audience` take the long timeout and the
`tellma-reports` policy (spec 0016 §8.2, §8.3). No pair is sensitive by default; a distribution may
mark `Publish` or `Transfer` with `MarkSensitive`.

| Route | Kind | Securable (`core.ReportDefinition` ×) |
|---|---|---|
| `query`, `get`, `get-by-ids` | standard; `get` answers the `permissions` extra (§5.2) | `Read` |
| `save`; `delete`, `delete-by-query` | standard | `Save`; `Delete` |
| `activate`, `deactivate` | built-in actions | `Activate` |
| `share`, `publish`, `transfer` | `[EntityAction]` (§5.4) | `Share`, `Publish`, `Transfer` |
| `browse`, `run`, `export` | `[ApiAction]`, `Mutation = false`, `Idempotent` (§5.5, §8, §12); `export` streams `.xlsx` | `Read` |
| `audience` | `[ApiAction]`, `Mutation = false`, `Idempotent` (§6) | `Share` |
| `export/start` | `[ApiAction]`, `Mutation = true`; `202 JobAccepted` (§12) | `Read` |

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
  display?: ReportDisplay; enum?: string;    // §3.2; enum: the enum whose labels display 'enum' shows
  format?: ReportFormat;
  running?: never;                           // reserved for running totals
}
interface ReportFormat {
  decimals?: number; grouping?: boolean;     // Numeric
  dateStyle?: 'numeric' | 'medium' | 'long'; // date types; spec 0005 §2.2's names
  calendar?: 'gc' | 'uq' | 'et';             // display month, quarter, year; absent: the viewer's calendar
}
type ReportDisplay = 'number' | 'integer' | 'percent' | 'text' | 'enum' | 'date' | 'month' | 'quarter'
  | 'year' | 'datetime' | 'instant' | 'bool';
type ReportOrder = { term: number; desc?: boolean }   // term: a 0-based index into columns
  | { expr: string; desc?: boolean };
interface ReportList { columns: ReportTerm[]; orderBy?: ReportOrder[]; top?: number }
interface ReportSummary {
  columns: ReportTerm[]; orderBy?: ReportOrder[]; having?: string; top?: number;
  totals?: boolean;                          // adds a grand total row (§8.4)
}
interface ReportDimension extends ReportTerm {
  order?: 'asc' | 'desc';                    // member order; default asc
  expand?: boolean;                          // pivot: expanded when opened; default true
  tree?: boolean;                            // on a navigation to a tree entity; default true there
  levels?: number;                           // tree levels shown, 1–10; default min(MaxDepth, 6)
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

- **Strict.** Unknown JSON members are refused, so a typo never drops a filter.
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

| Queryex type | `display` (default first) | `format` members |
|---|---|---|
| `Numeric` | `number`, `integer`, `percent` (the value × 100 with `%`) | `decimals` 0–6, `grouping` |
| `String` | `text`, `enum` (the labels of the enum `enum` names) | — |
| `Date` | `date`, `month`, `quarter`, `year` | `dateStyle`; `calendar` for `month`, `quarter`, `year` |
| `DateTime`; `DateTimeOffset` | `datetime`, `date`; `instant`, `date` | `dateStyle` |
| `Bool`; `Guid` | `bool`; `text` | — |

- **Navigation and text terms** take no override.
- **`calendar`** names the calendar a month, quarter or year renders in (§8.7). **Example —** a
  definition that groups by `startOfMonth(d, 'uq')` sets `calendar: 'uq'`, so a viewer whose
  calendar is Gregorian still sees Umm Al-Qura month labels.

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

1. **Root.** `Root` names a stack with `Query`, or a child query surface (§10): `ReportDefinition.UnknownRoot`.
2. **Kind block.** The document holds exactly the block that `Kind` names: `ReportDefinition.KindMismatch`.
3. **Counts** (`ReportDefinition.TermCount`):
   - a list has 1–100 columns, and a summary 1–50 terms;
   - a pivot has 0–5 row dimensions, 0–3 column dimensions and 1–20 measures, and not both axes
     are empty;
   - `drillColumns` holds at most 50 keys.
4. **Terms.** Each term has exactly one of `path` and `expr`. A `path` must resolve
   (`ReportDefinition.UnknownPath`). `image` and `file` keys appear only in a list
   (`ReportDefinition.BlobTermNotInList`).
5. **The compile check.** The validator compiles the document as a run would (§8), then discards
   the SQL. It declares every parameter, omits none, and applies `ReportLimits`. Each engine
   diagnostic becomes `ReportDefinition.InvalidExpression`, with the arguments `code`, `start`,
   `length` and the engine's own.
6. **Roles.** Each term fits its role:
   - list terms are row level (QX4002);
   - summary terms are grouping keys or measures (QX4007), and at least one is a measure;
   - dimensions are grouping keys, and measures aggregate;
   - attributes are grouping keys whose paths start with their dimension's path
     (`ReportDefinition.AttributeNotOfDimension`), allowed only when the pivot has exactly one row
     dimension (`ReportDefinition.AttributesNeedOneRow`).
7. **`tree: true`** appears only on a navigation to a tree entity (`ReportDefinition.NotATree`).
8. **`having` and `top`.** `having` appears only on a summary, pivot or chart
   (`ReportDefinition.HavingNotAllowed`). `top` is 1–10,000 on a list and 1–1,000 on a summary or
   chart (`ReportDefinition.TopOutOfRange`).
9. **Chart.** `pie` and `doughnut` take 1 dimension; `column`, `bar`, `line` and `area` take 1 or
   2; `card` takes none (`ReportDefinition.ChartDimensions`). The measure is `Numeric`
   (`ReportDefinition.ChartMeasureNotNumeric`). A reserved type (§3.1) is
   `ReportDefinition.ChartTypeUnsupported`. Tree dimensions are flat in charts.
10. **`orderBy`** is a term index or an expression. In a summary it is a grouping key or a measure
    (QX4005).
11. **Display.** `display` follows §3.2 (`ReportDefinition.DisplayIncompatible`).
    `enum` names a known enum (`ReportDefinition.UnknownEnum`). `drillColumns` are paths of `Root`
    (`ReportDefinition.UnknownPath`).
12. **The rest.**
    - Parameters follow §4.5.
    - Grouping stays within §8.8's limits (`ReportDefinition.TooManyLevels`).
    - Unknown JSON members are refused (`ReportDefinition.UnknownMember`).
    - A higher format version is refused (`ReportDefinition.FormatUnsupported`).
    - A document over 256 KB is refused (`ReportDefinition.TooLarge`).

### 3.5 Examples

**Example — summary** over Acme's child entity `acme.SalesInvoiceLine` (owner navigation
`Invoice`, spec 0018 §1.4): sales by customer this year, the top 50, with totals. Later examples
store it as definition 42.

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
                    "default": "year(today())", "type": "Numeric" } ],
  "filter": "year(Invoice.PostingDate) = @Year",
  "pivot": {
    "rows": [ { "path": "Center", "levels": 4, "expand": false, "attributes": [ { "path": "Center.Code" } ] } ],
    "columns": [ { "expr": "quarter(Invoice.PostingDate)", "labelKey": "Acme_Report_Quarter" } ],
    "measures": [ { "expr": "sum(Amount)", "labelKey": "Acme_Report_Sales", "format": { "decimals": 0 } },
                  { "expr": "count()", "labelKey": "Acme_Report_Lines" } ],
    "rowTotals": true, "columnTotals": true } }
```

## 4. Parameters

A parameter is a named input that the document's expressions reference as `@Key`. It declares
labels, whether it is required, a type and an optional default. A run's arguments supply the
values, and an omitted optional parameter imposes no restriction (§4.4).

The `type` list of §4.1 is the platform's only parameter type vocabulary: `tmCodedReport` (spec
0034 §2.2) and dashboard parameters (spec 0035 §10.1) use it too.

### 4.1 Shape

```ts
interface ReportParameter {
  key: string;                               // ^[A-Za-z][A-Za-z0-9_]{0,63}$; matched ignoring case, as Queryex does
  label?: string; label2?: string; label3?: string; labelKey?: string; // default label: the key
  required?: boolean;
  default?: string;                          // a Queryex value expression with no path and no parameter (§4.3)
  type?: 'Numeric' | 'String' | 'Bool' | 'Date' | 'DateTime' | 'DateTimeOffset' | 'Guid'; // stored after save (§4.2)
  entity?: string;                           // set by save (§4.2 step 5): the entity whose key it is compared with
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
   `type` is stated, the error is `ReportDefinition.ParameterTypeRequired`. Conflicting inferences
   (QX3400) are an error.
4. A stated `type` must equal the inferred one, unless inference is unconstrained
   (`ReportDefinition.ParameterTypeIncompatible`). Queryex never coerces parameters (spec 0008
   §7.3).
5. `entity` is set from `ComparedWith` (spec 0010 §F.15) when the compared path ends at a key or a
   foreign key: `CustomerId = @Customer` gives `acme.Customer`. Otherwise it is removed.

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
  every applied default, through `IDataBatch.EvaluateConstant` (spec 0012 §5.2, spec 0010 §F.11).
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
  `"2026-10-01"` for a `Date` and a JSON number for a `Numeric`.
- **Bad values and keys.** A value that does not convert is `422 Report.ArgumentInvalid`, with the
  arguments `key` and `type`. A key that no parameter declares is ignored.
- **Omitted means no restriction.** The run compiles with `OmittedParameters` (spec 0010 §F.10).
  In a filter, a `having` or a measure's condition, every atom (one comparison, such as
  `CustomerId = @Customer`) that mentions an omitted parameter folds to no restriction:
  `sum(Amount, CustomerId = @Customer)` becomes `sum(Amount)`.
- **Elsewhere,** an omitted parameter is a typed absent value: `if(Amount > @Limit, 'Big', 'Small')`
  gives `'Small'`.
- **URL form.** Spec 0034 §4.4 carries arguments in the URL.

### 4.5 Parameter validation (save)

- **Keys** match §4.1's grammar (`ReportDefinition.ParameterKeyInvalid`) and are unique ignoring
  case (`ReportDefinition.ParameterKeyDuplicate`).
- **Count.** At most 20 parameters (`ReportDefinition.TermCount`).
- **Types** follow §4.2.
- **Defaults.** A `default` compiles as a constant statement item against the type. Its
  diagnostics point at `/parameters/<i>/default`.
- **`entity`** sits only on a `Numeric` parameter (`ReportDefinition.ParameterEntityNotNumeric`).

## 5. Sharing and visibility

A definition is visible to its owner, to the users and roles it is shared with, to everyone when
it is public, and to holders of a role grant on `core.ReportDefinition`. The model is generic over
an entity and its share rows, so dashboards reuse it whole (§5.7).

### 5.1 The shareable contract

```csharp
// Tellma.Core.Abstractions.Reports
public interface IShareable                                 // on a TopLevelEntity with one [NotMapped] share collection
{
    int? OwnerId { get; set; }                              // [Derived]; null: a standard row
    bool IsPublic { get; set; }                             // [ServerOwned]
    int? BasedOnId { get; set; }                            // [WriteOnce]
}
public interface IShareRow                                  // on that collection's ChildEntity
{
    int? UserId { get; set; } int? RoleId { get; set; }     // exactly one is set
    ShareAccess Access { get; set; }
    int SharedById { get; set; } DateTimeOffset SharedAt { get; set; }
}
public sealed record SharePermissions(bool Save, bool Delete, bool Activate, bool Share, bool Publish, bool Transfer);

// Tellma.Core — the base of every shareable stack's service; applies the ownership rules below
public abstract class ShareableService<TEntity, TShare> : EntityService<TEntity, int>
    where TEntity : TopLevelEntity, IShareable where TShare : ChildEntity, IShareRow;

// FeatureContribution extension (Tellma.Core). Registers the criteria (§5.3), IShareCommands (§5.4),
// the permissions extra (§5.2) and the audience source (§6), and runs the startup checks below.
public static FeatureContribution Shareable<TEntity, TShare>(this FeatureContribution contribution)
    where TEntity : TopLevelEntity, IShareable where TShare : ChildEntity, IShareRow;
```

- **Startup checks.** The entity has exactly one share collection, of `IShareRow` rows, and its
  service derives from `ShareableService<TEntity, TShare>`.
- **Owner.** A new row takes the caller's user id, or null in a system scope (§13). An existing row
  keeps its before image's value. Only `transfer` writes a new owner.
- **Saves never touch shares.** `ShareableService` sets the share collection to `null` in
  `PreprocessAsync`. A details payload sent back unchanged therefore saves, and the shares stay as
  they are (spec 0015 §6.5).
- **Standard rows are read-only.** Outside a system scope, a row with no owner refuses save,
  delete, activate, deactivate, `share` and `transfer` with `Sharing.StandardReadOnly` (argument
  `entity`). `publish` is allowed, so an administrator can publish a seed to more roles.
- **Lineage.** `BasedOnId` must name a row the caller can read (`Sharing.BasedOnInvisible`).

### 5.2 Who may do what

Each ability is one securable on `core.ReportDefinition`. The bespoke criterion (§5.3) grants it to
the people in the third column; role grants give it to others.

| Ability | Securable | Granted by the bespoke criterion to | Typical role grants |
|---|---|---|---|
| See and run | `Read` | the owner, any grantee, everyone when public | administrators: unfiltered `Read` sees all |
| Edit, Save as | `Save` | the owner; `Edit` grantees | report managers |
| Create | `Save` | every member (the caller owns the new row) | — |
| Delete, deactivate | `Delete`, `Activate` | the owner | administrators |
| Share with users; read the audience | `Share` | the owner | administrators |
| Share with roles; make public | `Publish` | — | administrators, module leads |
| Change the owner | `Transfer` | — | administrators |

- **`Edit` does not confer re-sharing.** A role grant of `Publish` filtered by `OwnerId = me()`
  lets a role's members publish their own definitions. A rule such as "only to roles I belong to"
  cannot be expressed.
- **The `permissions` extra.** `get` and `get-by-ids` answer one row per visible id:
  `{ id, save, delete, activate, share, publish, transfer }`, the caller's `SharePermissions` on
  that row. Spec 0031 §4.1's `can` reads it. It is a computed extra
  (`DetailsPlan.AddComputedExtra`, spec 0015 §5.3):
  - For each ability, an `Unrestricted` decision answers true for every row, and a `Denied` one
    false.
  - Each `Filtered` decision adds one statement to the details batch. It selects the visible ids
    that satisfy the decision's filter.
  - A standard row answers false for all but `publish`.

### 5.3 The access criteria

`ShareableAccessCriteria<TEntity, TShare>` is the `IAccessCriteriaProvider` (spec 0014 §5.1) for
the entity's resource. It returns constant filter trees: no per-user list and no database read. The
SQL is byte-identical for every user; only `me()` binds per request.

```text
Read  : Or( Leaf("OwnerId = me()"), Leaf("IsPublic = true"), Exists(Share, Parent, Grantee) )
Save  : Or( Leaf("OwnerId = me()"), Exists(Share, Parent, And( Leaf("Access = 'Edit'"), Grantee )) )
Delete, Activate, Share : Leaf("OwnerId = me()")
Grantee = Or( Leaf("UserId = me()"),
              Via("Role", And( Leaf("IsActive = true"),
                               Or( Leaf("IsPublic = true"),
                                   Exists("core.RoleMembership", "Role", Leaf("UserId = me()")) ))) )
```

- **In words.** A user reads a definition they own, a public one, or one with a share that names
  them or an active role that is public or that they belong to. They save one they own, or one
  where such a share has `Edit` access.
- **`Exists`** is spec 0010 §F.14's semi-join, so nothing fans out. `Share` is the share entity
  (`core.ReportDefinitionShare`), and `Parent` is its `[ParentKey]` navigation (`ReportDefinition`).
- **Reason.** Every criterion carries the reason `share`, so `access/check` (spec 0014 §10) answers
  "via sharing".
- **Everyone can start.** Every member's `Read` and `Save` decisions are `Filtered`, so the Reports
  entry and the New button show for all.

### 5.4 The sharing actions

`share` grants users, `publish` grants roles and everyone, and `transfer` changes the owner. A save
never writes shares. Each service declares the three actions and delegates them to `IShareCommands`.

```csharp
// ReportDefinitionService : ShareableService<ReportDefinition, ReportDefinitionShare>
[EntityAction("share", Action = "Share", LoadChildren = true, Description = "Shares definitions with users.")]
public Task ShareAsync(ActionContext<ReportDefinition, int> context, ShareArguments arguments);
[EntityAction("publish", Action = "Publish", LoadChildren = true, Description = "Shares definitions with roles or everyone.")]
public Task PublishAsync(ActionContext<ReportDefinition, int> context, PublishArguments arguments);
[EntityAction("transfer", Action = "Transfer", Description = "Changes the owner of definitions.")]
public Task TransferAsync(ActionContext<ReportDefinition, int> context, TransferArguments arguments);

public sealed record ShareArguments(IReadOnlyList<UserGrant>? Set, IReadOnlyList<int>? Remove);   // Remove: user ids
public sealed record PublishArguments(IReadOnlyList<RoleGrant>? Set, IReadOnlyList<int>? Remove, bool? IsPublic); // Remove: role ids
public sealed record UserGrant([property: EntityRef("core.User")] int UserId, ShareAccess Access);
public sealed record RoleGrant([property: EntityRef("core.Role")] int RoleId, ShareAccess Access);
public sealed record TransferArguments([property: EntityRef("core.User")] int OwnerId);

public interface IShareCommands<TEntity> where TEntity : TopLevelEntity, IShareable // registered by Shareable<,>()
{
    void Share(ActionContext<TEntity, int> context, ShareArguments arguments);
    void Publish(ActionContext<TEntity, int> context, PublishArguments arguments);
    void Transfer(ActionContext<TEntity, int> context, TransferArguments arguments);
}
```

- **Merge, never replace.** `Set` adds a grant or changes its access, and `Remove` deletes one.
  Other grants are untouched, so two people sharing at once never overwrite each other. Every
  target row gets the change.
- **Writes** (spec 0015 §11.2). The actions mark the rows through `ActionContext.Save`, and the
  emitter synchronizes the shares. `publish` sets `IsPublic` through `ActionContext.Update`. Each
  action stamps `ModifiedAt`. A new grant takes `SharedById` = the caller and `SharedAt` = the
  request's `Now`.
- **Validation.**
  - A user grantee is an active `Individual` member (`Sharing.UserInvalid`).
  - A role grantee is an active role (`Sharing.RoleInvalid`).
  - The owner is never a grantee (`Sharing.OwnerIsImplicit`).
  - `IsPublic` grants `View` only.
  - A row holds at most 200 user grants and 50 role grants (`Sharing.TooManyGrants`).
  - Grantees who cannot read the root are allowed; the audience counts them (§6).
- **`transfer`.** The new owner is an active `Individual` member (`Sharing.OwnerInvalid`). Their
  user grant, if any, is removed, and the old owner keeps nothing.
- **Notification.** A newly added user grant notifies the grantee (§7).

### 5.5 Browsing: Mine, Shared with me, All

`browse` serves the reports page's three tabs (`?tab=mine|shared|all`) through spec 0030 §2.5's
`searchRead`. It is a server action because the tabs depend on share rows, which client Queryex
cannot express.

```csharp
[ApiAction("browse", Action = "Read", Mutation = false, Idempotent = true)]
public Task<RowPage> BrowseAsync(ReportBrowseRequest request);
public sealed record ReportBrowseRequest(ReportScope Scope, QueryRequest Query); // run as query runs it (spec 0015 §5.2)
public enum ReportScope { Mine, Shared, All }
```

Each scope adds one leaf, joined by `and` to the query's filters and the access filter:

| Scope | Leaf |
|---|---|
| `Mine` | `OwnerId = me()` |
| `Shared` | `OwnerId` is null or not `me()`, and (`IsPublic` or `Exists(Share, Parent, Grantee)`). Standard rows are included. |
| `All` | none: everything the caller can read; for an administrator, every definition |

- **Inactive definitions** are hidden unless the query sets `IncludeInactive`.
- **Coded reports** are not rows, so `browse` never lists them (spec 0034 §11).

### 5.6 Lifecycle and delete

- **Delete removes the definition for everyone,** shares included, after the host's plain
  confirmation (spec 0034 §10.3). Deactivation is the soft path.
- **Save as** is a client create with `BasedOnId`. No share is copied.
- **A dashboard widget blocks the delete.** `FK_DashboardWidgets_ReportDefinitionId` is `NO ACTION`
  (spec 0035 §3.1). Before the persist, the service calls
  `IDashboardReadThrough.DashboardsUsing(ids)` and fails with `ReportDefinition.InUse`, naming up
  to five dashboards.
- **Deactivating the author changes nothing,** because no run uses the author's identity.
  Administrators find orphans by filtering `OwnerId` on inactive users, then `transfer` them.

### 5.7 Dashboards reuse the model

Spec 0035 gives dashboards the same model:

- `Dashboard : IShareable` and `DashboardShare : IShareRow`, composed with
  `Shareable<Dashboard, DashboardShare>()`;
- a service derived from `ShareableService<,>`, which declares the three actions over
  `IShareCommands<Dashboard>` and answers `audience` through `IAudienceResolver`;
- the notification type `core.dashboard.shared`.

In the other direction, reports call into dashboards through one interface that this spec owns:

```csharp
public interface IDashboardReadThrough                      // owned here; implemented by spec 0035 §6.3
{
    Task<bool> AllowsAsync(int dashboardId, int reportDefinitionId);   // the caller reads the dashboard and it shows the definition
    Task<IReadOnlyList<(int Id, string Title)>> DashboardsUsing(IReadOnlyList<int> reportDefinitionIds); // titles in the request's content slot
}
```

## 6. The audience resolver

`audience` answers "which users can see this definition, and why". It lists every member who can
see it, with each reason and their highest access. Per grant, it also counts the members who
cannot read the root entity, so the share dialog can warn that they will see an empty report.

### 6.1 Contract

```csharp
[ApiAction("audience", Action = "Share", Mutation = false, Idempotent = true)]
public Task<ShareAudience> AudienceAsync(IdRequest request);

public interface IAudienceResolver { Task<ShareAudience> ResolveAsync(string resource, int id, string? root); } // root null: no counts
public sealed record ShareAudience(
    int Total, bool Capped,                                 // distinct users; Members cut at MaxAudienceMembers
    IReadOnlyList<AudienceMember>? Members,                 // null unless the caller holds core.Role × Read; by display name
    IReadOnlyList<AudienceGrant> Grants,                    // one row per grant, for the share dialog
    RelatedEntities Related);                               // core.User, core.Role and core.Dashboard display projections
public sealed record AudienceMember(int UserId, AudienceAccess Access, IReadOnlyList<AudienceReason> Reasons);
public sealed record AudienceReason(AudienceReasonKind Kind, AudienceAccess Access, int? RoleId, int? SharedById,
    DateTimeOffset? SharedAt, int? PermissionId, bool Filtered, int? DashboardId); // ids that fit Kind; Filtered: the permission has a filter
public sealed record AudienceGrant(AudienceReasonKind Kind, int? UserId, int? RoleId, int? PermissionId, int? DashboardId,
    AudienceAccess Access, int Members, int? CannotReadRoot);   // CannotReadRoot: null when root is null
public enum AudienceReasonKind { Owner, UserShare, RoleShare, Everyone, Permission, Dashboard }
public enum AudienceAccess { View, Edit, Owner }
```

### 6.2 Resolution

1. **Authorize.** The row loads under the caller's `Share` decision; an invisible row is 404.
   Without `core.Role × Read`, `Members` is null and the answer carries only grants and counts.
2. **Grants,** all in one Read round trip:
   - the owner and the shares;
   - for each shared active role, its active `Individual` members (for a public role, every
     active member);
   - when `IsPublic`, every active member;
   - every stored permission of an active role, public roles included, with any action on the
     resource (`core.ReportDefinition`, `core.Dashboard`) or on `*` (spec 0014 §5.2), with the
     role's members. A filtered permission counts only when the row satisfies it: one count of
     `Id = @id and (<filter>)` per distinct filter text, under that filter's own language version.
   - **Root readers.** The same round trip counts, per grant, the members who cannot read the root.
     Such a member has no stored permission, with any action, on the root, on `*` or on an owner up
     the root's chain (spec 0014 §5.5), through any active role they belong to or any public role.
     Filters and bespoke criteria are not evaluated: the count warns, it never decides.
3. **Dashboards** (`core.ReportDefinition` only). `DashboardsUsing([id])` lists up to 20
   dashboards. Their grants resolve as in step 2, in one more round trip, and add the reason
   `Dashboard` with the dashboard's id. Nothing deeper is followed.
4. **Merge.** Each user becomes one member with every reason. `Access` is the highest:
   `Owner` > `Edit` > `View`, where `Edit` comes from an `Edit` share or from a permission whose
   action is `Save` or `*`. The system user never appears. `Members` is cut at
   `MaxAudienceMembers` (§14); `Total` stays exact.

**Example —** definition 42 is shared with Omar (user 9) at `View` and with the role Sales (4) at
`Edit`. It answers the grants below. From them, spec 0034 §10.4's share dialog shows "Omar can't
read Sales invoice lines and will see an empty report." and "2 of 9 members of Sales can't read
Sales invoice lines."

```json
"grants": [ { "kind": "UserShare", "userId": 9, "access": "View", "members": 1, "cannotReadRoot": 1 },
            { "kind": "RoleShare", "roleId": 4, "access": "Edit", "members": 9, "cannotReadRoot": 2 } ]
```

## 7. The `core.report.shared` notification

A newly added user grant raises `core.report.shared` for the grantee, and removing the grant
withdraws it. Role grants, access changes and `IsPublic` raise nothing. Spec 0021 §2.3 owns the
catalogue row; this section states what the sharing action sends.

- **One request per content slot** of the grantees' preferred languages. Its `title` and
  `actorName` are in that slot, falling back to slot 1. `ActorUserId` is the sharer.
- **Dedup.** `DedupKey = 'core.ReportDefinition:<id>'`, `OnDuplicate = Replace`; removing the grant
  appends `WithdrawRequest('core.ReportDefinition:<id>', [userId])`.
- **Text.** `Notification_core_report_shared` = "{actorName} shared {title} with you"; Arabic
  "شارك {actorName} معك {title}". The target opens `/{t}/reports/{id}` (spec 0024 §9.4).

## 8. Execution

`run` executes a stored definition or a draft under the viewer's permissions. Each kind runs one
Queryex statement, or two for a summary with totals. When defaults apply, one constant statement
runs first (§4.3).

### 8.1 Contract

```csharp
[ApiAction("run", Action = "Read", Mutation = false, Idempotent = true)]
public Task<ReportResult> RunAsync(ReportRunRequest request);

public sealed record ReportRunRequest(
    int? Id,                                                // a stored definition, or
    ReportDraft? Draft,                                     // an unsaved document (studio, assistant); exactly one of the two
    IReadOnlyDictionary<string, JsonElement>? Arguments,    // §4.4
    int? DashboardId = null,                                // read-through (§8.2)
    int Skip = 0, int? Take = null, bool IncludeCount = false); // list and summary only; Take absent: DefaultTake
public sealed record ReportDraft(string Root, ReportKind Kind, JsonElement Document);

public interface IReportEngine                              // run, export (§12) and IReportResults (§11) use it
{
    Task<ReportResult> RunAsync(ReportSource source, IReadOnlyDictionary<string, JsonElement>? arguments, ReportPage? page);
}
public abstract record ReportSource
{
    public sealed record Stored(int Id, int? DashboardId) : ReportSource;
    public sealed record Draft(string Root, ReportKind Kind, ReportDocument Document) : ReportSource;
}
public sealed record ReportPage(int Skip, int Take, bool IncludeCount);
```

**Example —** the first open of §3.5's summary (id 42): `From` takes its default, `To` is omitted.

```json
→ POST /17/api/web/reports/run   { "id": 42, "arguments": {}, "take": 200, "includeCount": true }
← { "id": 42, "kind": "Summary", "root": "acme.SalesInvoiceLine", "title": "Sales by customer",
    "arguments": [ { "key": "From", "value": "2026-01-01", "source": "Default" }, { "key": "To", "value": null, "source": "Omitted" } ],
    "columns": [ … ], "table": { "rows": { … }, "totals": { … }, "count": 37, "countCapped": false }, "drill": { … }, … }
```

### 8.2 Authorization

A run first checks that the caller may see the definition, then that they may read its data.

| Source | Definition check | Refusal |
|---|---|---|
| `Id` | the row loads under the caller's `Read` decision on `core.ReportDefinition`; `IsActive` is not checked | 404 `not-found` |
| `Id` + `DashboardId` | as above; when the row is invisible, `IDashboardReadThrough.AllowsAsync(dashboardId, id)` loads it without its own filter | 404 |
| `Draft` | any member: every member's `Read` passes through §5.3's criteria | — |

Every source then passes two data checks. Each refuses with 403 `forbidden`, naming the entity.

- **The root.** `RequireAsync(Root, "Read")`. A child root composes its owner's decision (spec 0014
  §5.5).
- **Every path.** Spec 0015 §5.2's traversal rule checks every path of every clause.

The root decision's filter is joined by `and` into every statement, so a definition never widens
access. Every row, group, subtotal and chart point counts only rows the viewer may read, and
`me()` is always the viewer. A summary's grand total therefore equals the sum of the list rows the
same viewer sees.

### 8.3 The run, step by step

1. **Load** the row, or take the draft, and parse the document. A draft is validated in full
   (§3.4), with its errors at `draft.document`. A stored document is checked only by compiling it
   (step 5).
2. **Arguments.** Sort the request's arguments into supplied, defaulted and omitted sets, with
   §4.4's 422s.
3. **Defaults.** When any applies, evaluate them as §4.3 describes, under the definition's
   `LanguageVersion`, in one Read round trip.
4. **Plan** the `QuerySpec` for the kind (§8.4–§8.6), with
   `Filter = And(Leaf(document.filter, LanguageVersion), decision.Filter)`. Check every discovered
   path with the traversal rule (§8.2). No activatable conjunct is added: a report that means to
   skip inactive rows filters `IsActive` itself.
5. **Compile** under `ReportLimits`, with every referenced parameter declared (§4.2) and
   `OmittedParameters` (§4.4). A stored definition that no longer compiles answers
   `400 query-invalid`, where each diagnostic's `clause` is its document pointer, such as
   `/pivot/rows/0`. The run logs `ReportEvents.DefinitionInvalidAtRun`.
6. **Execute** one Read batch through `IGuardedBatchRunner` (spec 0014 §7.1) with `IDataBatch.Rows`
   (spec 0012 §11.3). The batch holds the main statement, with `CountCap` when `IncludeCount`, and
   a summary's totals statement. Then **shape** the result (§9).

**Round trips.** A draft without defaults takes one Read round trip. Loading a stored definition
adds one, applied defaults add one, and a dashboard read-through adds what `AllowsAsync` spends
(spec 0035 §6.3). A stored definition with defaults therefore takes three.

### 8.4 Terms to select items

The compiler turns each term into select items according to its column-key kind (spec 0029 §6.1).
A navigation expands through `IDisplayNames.Columns(entity, shape, navigationPath)` (spec 0012
§3.1), as on the search page.

| Term | List: select items | Aggregate kinds: grouping key | Aggregate kinds: label items |
|---|---|---|---|
| value path `Amount`, `Invoice.PostingDate` | the path | the path | — |
| text path `Name` (a group anchor) | `Name`, `Name2` (admitted slots) | every admitted slot, one key group | — |
| navigation `Invoice.Customer` | its `Id` and display-name columns | `Invoice.Customer.Id` | `max(Invoice.Customer.Name)`, `max(Invoice.Customer.Name2)` |
| image `Image`, file `File` | `ImageId`, `Image.Variants`; `FileId`, `File.FileName` | refused (§3.4 rule 4) | — |
| `expr` | the expression | the expression: a key, or a measure as written | — |

- **Labels as `max(...)`.** A navigation groups by its key alone. Its display columns depend on
  that key, so reading them as aggregates keeps one grouping expression per level, within SQL
  Server's limit (§8.8).
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
- **Totals.** `totals: true` adds a statement with the measures alone: no grouping keys, no
  `having`, no `top`. The total therefore covers every filtered row.
- **Chart.** One aggregate statement: grouping keys, label items and the measure. It is ordered by
  the measure when `orderBy: 'measure'`, else by the first dimension. Without `top`, it takes
  `MaxChartPoints + 1` rows; if the extra row arrives, the run fails with
  `413 Report.TooManyPoints`.

### 8.5 Pivot: levels, grouping sets, having

A pivot is one aggregate statement with grouping sets (spec 0010 §F.8). It returns every leaf
cell, every subtotal at every level of both axes, and the grand totals the definition asks for.

1. **Levels.** Each row dimension contributes one level, or `levels` levels for a tree (§8.6), in
   document order: R1 … Rp. Column dimensions do the same: C1 … Cq.
2. **Key groups.** One per level:
   - `[Center.Id]` for a flat navigation;
   - `[quarter(Invoice.PostingDate)]` for a value or an expression;
   - `[Name, Name2]` for a text term;
   - `[Center.Level{j}.Id]` for tree level j.
3. **Other items.** The label items and attributes of each level as `max(...)`, the measures, and
   one truncation probe per tree dimension (§8.6).
4. **Grouping sets.** One set R1…Ri ∪ C1…Cj per pair (i, j), listed finest first. i runs from 0
   to p (from 1 when `rowTotals` is false), and j from 0 to q (from 1 when `columnTotals` is
   false). An axis with no dimensions contributes only 0.
5. **Having** filters only the finest set's rows (spec 0010 §F.8), so a collapsed member never
   shows an empty total.
6. **Cap.** The statement takes `MaxPivotRows + 1` rows, ordered by the first grouping key. If the
   extra row arrives, the run fails with `413 Report.TooManyRows`.

The cube builder (§9.3) tells the sets apart by the level indicator (spec 0010 §F.8).

**Example —** §3.5's pivot with `levels: 2`, as the compiler hands it to the engine:

```text
Select       = Center.Level1.Id, Center.Level2.Id, quarter(Invoice.PostingDate), sum(Amount), count(),
               max(Center.Level1.Name), max(Center.Level1.Name2), max(Center.Level1.Code), max(Center.Level2.Name),
               max(Center.Level2.Name2), max(Center.Level2.Code), max(level(Center.Node))
KeyGroups    = [[0], [1], [2]]                                  -- R1, R2, C1
GroupingSets = [[0, 1, 2], [0, 1], [0, 2], [0], [2], []]        -- (i, j) = (2,1) (2,0) (1,1) (1,0) (0,1) (0,0); [] is the grand total
Filter       = year(Invoice.PostingDate) = @Year  and  <the root decision's filter>
```

### 8.6 Tree dimensions

A navigation to a tree entity with `tree: true` renders as the tree of the members that have facts.
Each parent aggregates its subtree, and a phantom child holds the parent's own facts.

- **Level navigations** (spec 0010 §F.9). `LevelJ` of a row is its ancestor at depth J, where a
  root has depth 1. It is the row itself when the row is at depth J, and null when the row is
  shallower. Each join seeks the unique `Node` index, so nothing fans out and every grouping set
  stays exact.
- **Decoding.** Take a grouping set that includes a tree dimension's levels L1 … Lj, and a result
  row whose levels are non-null up to Lm. The table says what the row is:

| m | The row is |
|---|---|
| j | the member at level j: its subtree's aggregate |
| j − 1 ≥ 1 | the phantom child of the level-m member: the facts on that member itself |
| j − 1 = 0 | the "(None)" member: facts with no value |
| below j − 1 | redundant (the same facts, one set up); dropped |

- **Leaves.** A member whose only child is its phantom is a leaf, and the phantom is dropped.
- **No ancestor query.** Level navigations reach every ancestor, so no separate query fetches
  ancestors.
- **`tree: false`** groups by the navigation itself (`Center.Id`), as one level.
- **Truncation.** Each tree dimension carries a probe such as `max(level(Center.Node))` (§8.5).
  When `levels` is below the entity's `MaxDepth`, a probe value above `levels` means deeper facts
  sit in the phantom of their ancestor at level `levels`. The cube reports this in `Truncated`
  (§9.3).

**Example —** with `levels: 2`, Head office (10) holds 2,000 of its own; its children Riyadh (11)
and Jeddah (12) hold 30,000 and 20,000:

| Set | Row (L1, L2) | m, j | Becomes |
|---|---|---|---|
| (L1, L2) | (10, null) | 1, 2 | Head office (own) 2,000 — the phantom child |
| (L1, L2) | (10, 11), (10, 12) | 2, 2 | Riyadh 30,000; Jeddah 20,000 |
| (L1) | (10) | 1, 1 | Head office 52,000 |
| () | () | — | Total 52,000 |

### 8.7 Calendars

A definition names its calendar in its expressions. The viewer's calendar changes how dates render,
never how a definition groups.

- **Labels follow the grouping.** `format.calendar` keeps a label in the grouping's calendar
  (§3.2).
- **Umm Al-Qura.** `'uq'` works in every calendar function through spec 0013 §8.2's month map. A
  date outside the map gives null. Its operand rules, QX3104 included, are spec 0010 §F.13's.

### 8.8 Limits

Every run is bounded. Named limits are `ReportOptions` members (§14), shown with their defaults;
the others are fixed, or owned by the spec cited.

| Limit | Default | When exceeded |
|---|---|---|
| list and summary page | `DefaultTake` 200, at most `MaxTake` 10,000 | 413 `limit-exceeded` |
| chart points | `MaxChartPoints` 10,000 | 413 `Report.TooManyPoints` |
| pivot statement rows | `MaxPivotRows` 250,000 | 413 `Report.TooManyRows` |
| pivot row members, column members, cells | `MaxPivotRowMembers` 20,000, `MaxPivotColumnMembers` 2,000, `MaxPivotCells` 500,000 | 413 `Report.TooManyCells` |
| grouping expressions | 32 (SQL Server, QX5008) | save: `ReportDefinition.TooManyLevels` |
| key groups of a pivot | 31 (the `int` level indicator) | save: `ReportDefinition.TooManyLevels` |
| `ReportLimits` (a `QueryexLimits` profile) | `MaxListItems` 512, `MaxJoins` 64, `MaxParameters` 1,024, `MaxTypedNodes` 8,192; the rest `Default` | diagnostics |
| command timeout | `CommandTimeout` 120 s | as any timeout (spec 0016 §7.1) |
| concurrent runs per user | 8, queue 16 (spec 0016 §8.2) | 429 |

### 8.9 Cancellation and background refresh

A run is a read: the client may retry it and may cancel it.

- **Aborts.** The client aborts a run on a parameter change, on navigation and on Cancel (spec 0024
  §10.5). The abort reaches SQL (spec 0016 §8.3). The server writes nothing and counts
  `tellma.reports.cancelled`.
- **Background refresh.** A dashboard's silent refresh sends `Tellma-Activity: background` (spec
  0016 §6.1).

### 8.10 Caching

The compiler caches plans per definition version, in an LRU cache of `PlanCacheSize` entries.
Drafts are planned on every call. The Queryex engine's own caches key on text and options (spec
0008 §16, spec 0010 §F.0).

## 9. Results

Every run — stored, draft or coded — answers one self-describing `ReportResult`: the effective
arguments, labelled columns, and a table or a cube. The renderers (spec 0034) and the Excel writer
(§12) read nothing else. Labels stay content in every slot, not rendered text, so a language
switch relabels without a new request.

### 9.1 The envelope

```csharp
public sealed record ReportResult(
    int? Id,                                             // the stored definition; null for a draft or a coded report
    ReportKind Kind,
    string? Root,                                        // the root entity; null for a coded report without one
    string? Title,                                       // in the request's content slot; a coded report's localized title
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
    QueryexType Type, int? Scale,
    string? Label, string? Label2, string? Label3, string? LabelKey, // as stored; all null: derive from Path
    string? Path,                                        // the column key, for derived labels and drill conditions
    string? Entity,                                      // a navigation's target: 'acme.Customer'
    ReportDisplay? Display, string? Enum, ReportFormat? Format,
    int? Dimension, int? Axis, bool? Expand);            // pivot: the dimension, 0 rows or 1 columns, expanded when opened
```

### 9.2 Tables (list, summary, chart)

```csharp
public sealed record ReportTable(
    QueryRowSet Rows,                                    // spec 0016 §3.5's row set
    IReadOnlyList<IReadOnlyList<int>> ColumnOrdinals,    // parallel to ReportResult.Columns: the row-set columns each holds
    ReportRowIdentity? Identity,                         // List: how a row opens its record
    QueryRowSet? Totals,                                 // Summary with totals: one row, measures only
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
- **Chart data** is the dimension columns, then the measure. The renderer splits series by the
  second dimension; that is layout, not aggregation.

### 9.3 The cube (pivot)

The cube builder decodes the grouping-set rows in C# into members and sparse cells, so the client
renders and never aggregates.

```csharp
public sealed record ReportCube(
    IReadOnlyList<CubeLevel> RowLevels, IReadOnlyList<CubeLevel> ColumnLevels,
    IReadOnlyList<CubeMember> RowMembers, IReadOnlyList<CubeMember> ColumnMembers,
    IReadOnlyList<CubeCell> Cells,                       // sparse: absent means no facts
    IReadOnlyList<CubeTruncation> Truncated);
public sealed record CubeLevel(int Dimension, int Level); // Level: 1… in a tree dimension, 0 in a flat one
public sealed record CubeMember(
    int Id, int? Parent, int Depth,                      // Depth 0: the axis total; 1: the first level
    CubeMemberKind Kind, int Dimension, int Level,
    JsonElement? Key,                                    // the id or the value; null for (None)
    JsonElement? Display,                                // a navigation's or text term's display row: { id, name, name2 }
    IReadOnlyList<JsonElement?>? Attributes);            // parallel to the dimension's attributes
public enum CubeMemberKind { Member, Phantom, None, Total }
public sealed record CubeCell(int Row, int Column, IReadOnlyList<JsonElement?> Values); // wire: [row, column, v0, v1, …]
public sealed record CubeTruncation(int Dimension, int Levels, int Depth); // Levels: shown; Depth: the probe, the deepest level with facts
```

- **One tree per axis.** `Parent` links a member to the member one level up. The first level of
  the next dimension hangs under the last level of the previous one.
- **Which members exist.** One member per non-redundant row of every set (§8.6). The axis also has
  the `Total` member (id 0, depth 0) when it has totals or no dimension.
- **Totals.** A cell on a `Total` member is that axis's subtotal, or the grand total.
- **Order.** Members follow their dimension's `order`: values by value, and navigations and text
  by display text in the request's content slot and culture. "(None)" comes last, and a phantom
  first among its siblings. The renderer re-sorts by the same rule after a language switch.

### 9.4 Drill-through

A click on a summary row, a pivot cell or a chart point opens the drill-through list
`/{t}/list/{segment}?filter=…&cols=…` (spec 0030 §17, spec 0034 §8). The list is a plain
`query` over the root, so its filter must be self-contained: no parameter, no omitted atom.

```csharp
public sealed record ReportDrill(
    string Segment,                                      // the root's query segment, a child segment included
    string? Filter,                                      // the document filter, arguments bound, omitted atoms folded
    IReadOnlyList<string?> Terms,                        // parallel to Columns: an expression dimension's bound text; null otherwise
    IReadOnlyList<string> Columns);                      // drillColumns; empty: the root's default columns (§3.3)
```

The server binds `Filter` and `Terms` with `BindParameters` (spec 0010 §F.12), so they hold values
instead of parameters. The client joins `(Filter)` by `and` to one condition per clicked
coordinate, written through spec 0029 §8.2's `tmQx` (`raw` for the bound text):

| Coordinate | Condition |
|---|---|
| navigation member, flat | `CustomerId = 1012` (on the foreign key) |
| tree member at any level | `descendantOf(Center.Id, 11)`: the member and its subtree |
| phantom of a tree member; (None) | `CenterId = 10`; `CenterId = null` |
| value member | `Region = 'East'`; a date `Invoice.PostingDate = cast('2026-03-01', 'date')` |
| text member | every slot it grouped by: `Name = 'Jeddah' and Name2 = 'جدة'` (`= null` for an empty slot) |
| expression member | `(<Terms[i]>) = <literal>`, such as `(quarter(Invoice.PostingDate)) = 3` |
| `Total` | none |

Measure conditions and `having` are not applied to the drilled list.

**Example —** §8.1's run binds `Filter = "Invoice.PostingDate >= cast('2026-01-01', 'date') and
true"`, where the omitted `To` folded to `true`. A click on Olive Inc. (1012) then opens this URL,
URL-encoded:

```text
/17/list/sales-invoice-lines?filter=(Invoice.PostingDate >= cast('2026-01-01', 'date') and true) and Invoice.CustomerId = 1012&cols=Invoice.SerialNumber,…
```

## 10. Child-entity query routes

Reports over child entities, such as sales invoice lines, need a query route over the child. Every
child collection of a stack with `Query`, at any depth, gets one: a read-only child query surface
(spec 0015 §2.2). It needs no declaration; `[ApiResource(Queryable = false)]` opts out.

- **Route.** `POST /{t}/api/web/{child-segment}/query`, under the owner's `Read` decision rebased
  through `FilterTree.Via` (spec 0014 §5.5).
- **Securable and operation.** `(Child, Read)` with the owner pair (spec 0015 §2.7). The only
  operation is `query`, through `IChildQueryService.QueryAsync(resource, request)` (spec 0015
  §5.2).
- **Traversal through the owner.** With no direct child grant, a path through the owner navigation
  is checked as from the owner. For example, `Invoice.PostingDate` passes under a filtered `Read`
  on invoices.
- **MCP.** A surface inherits its owner's `McpExposure` (spec 0016 §11.5).
- **The audit.** Surfaces are on by default, so the startup audit lists every one it projected
  (spec 0016 §4.6).
- **Client.** The child's summary carries its `segment` and `operations: ['query']` (spec 0029
  §2.2). The drill-through list is the only page over it, and it has no export.

## 11. Coded reports

A coded report is the escape hatch for what the document cannot express. It is one `[ApiAction]`
that returns `ReportResult` under its own securable, with no row in `core.ReportDefinitions`. Its
client half is spec 0034 §2.2's `tmCodedReport`.

```csharp
// Tellma.Distro.Acme — the whole server side of Acme's aging report (spec 0018 §1.4)
[ApiRoute("acme-reports")]
public sealed class AcmeReports(IGuardedBatchRunner runner, IReportResults results)
{
    [ApiAction("aging", Resource = "acme.AgingReport", Action = "Read", Mutation = false, Idempotent = true)]
    public Task<ReportResult> AgingAsync(AgingArguments arguments);
}
public sealed record AgingArguments(DateOnly? AsOf, int? Customer);    // members named after the parameter keys
// composition: contribution.ApiService<AcmeReports>() registers acme.AgingReport × Read (spec 0014 §4.2)

public interface IReportResults
{
    ReportResult Table(ReportKind kind, QueryRowSet rows, IReadOnlyList<ReportColumnSpec> columns,
        IReadOnlyList<ReportArgumentValue> arguments, string? titleKey = null); // wraps rows the method read itself
    Task<ReportResult> RunAsync(string root, ReportKind kind, ReportDocument document,
        IReadOnlyDictionary<string, JsonElement>? arguments, string? titleKey = null); // a document built in code
    T Require<T>(T? value, string key) where T : struct;    // null: 422 Report.ArgumentRequired at arguments.<key>
    string Require(string? value, string key);
}
public sealed record ReportColumnSpec(string? LabelKey, ReportColumnRole Role, ReportColumnKind Kind,
    IReadOnlyList<int> Ordinals,                            // the row-set columns, as ColumnOrdinals (§9.2)
    string? Entity = null, ReportDisplay? Display = null, ReportFormat? Format = null);
```

- **The contract.** A coded report action has `Mutation = false`, `Idempotent = true`, one body
  record of scalars named after the parameter keys, and the result `ReportResult`. That is enough
  for the platform to run, cancel, retry and export it. **Example —** Acme's aging body is
  `{ "asOf": "2026-10-20", "customer": 1012 }`.
- **The allow-list.** The startup check `core.coded-reports` lists every such action. That list is
  the export allow-list (§12.1).
- **Defaults are the method's.** The method applies them (`AsOf ?? today`) and echoes them with
  source `Default`. The client declaration's `default` is only a placeholder.
- **Data access is the report's decision.** The recommended pattern reads under the viewer's
  `RequireAsync(root, "Read")` filter. Aggregating rows the viewer may not see is a deliberate,
  code-reviewed data grant, named by the report's own securable.

## 12. Excel export

Every kind exports to one `.xlsx` sheet through spec 0019 §4.4's table writer. The export runs the
report exactly as `run` would, under the same permissions and arguments.

### 12.1 Routes

```csharp
[ApiAction("export", Action = "Read", Mutation = false, Idempotent = true)]      // an .xlsx stream
public Task<ExportOutcome> ExportAsync(ReportExportRequest request);
[ApiAction("export/start", Action = "Read", Mutation = true, Idempotent = false)] // 202 JobAccepted
public Task<JobAccepted> StartExportAsync(ReportExportRequest request);

public sealed record ReportExportRequest(
    int? Id, ReportDraft? Draft,                         // a stored definition or a draft, or
    string? Route,                                       // a coded report's route ('acme-reports/aging'); exactly one of the three
    IReadOnlyDictionary<string, JsonElement>? Arguments, int? DashboardId = null);
```

- **Coded reports.** `Route` must name an action on the `core.coded-reports` list, else
  `400 bad-request`. The export invokes it through `IApiActionInvoker`, with `Arguments` as its
  body, under the action's own securable.
- **Synchronous export.** `export` handles a list or summary of at most `MaxSynchronousExportRows`
  rows. A pivot or a chart always fits, because §8.8 already bounds it. Above the limit, `export`
  answers `413 Excel.Export.RowLimitExceeded`, naming `export/start`.
- **Background export.** `export/start` enqueues `core.export` with `ReportExportJobArguments` and
  the label `Core_Job_ExportReport { title }` (spec 0019 §12.2). The handler re-runs the report as
  the run-as user (spec 0019 §12.3). A list exports up to `MaxExportRows`, in pages of `MaxTake`.

### 12.2 Flat layouts (list, summary, chart)

A list, a summary and a chart export as one flat table.

- **Header.** One row of labels: the label in the request's content slot, else the label key's
  text, else the slash label (spec 0019 §3.3).
- **Cells.** A navigation shows its display name (`IDisplayNames.Render`), a text term its active
  slot, and an image or a file its file name.
- **Formats** follow the type and `display` (spec 0019 §3.2). For example, `percent` becomes an
  Excel percent, and `month` becomes `mmm yyyy` in the column's calendar.
- **Totals and charts.** A summary with totals ends with a bold "Total" row. A chart exports its
  dimension columns, then the measure; the chart image is not exported.

### 12.3 The pivot layout

The pivot exports fully expanded, in compact form.

- **Rows.** One row-header column holds every row member, indented by `Depth − 1` and followed by
  its children, the phantom first. The grand total row comes last.
- **Columns.** After the row header come the attribute columns, then one data column per pair of
  column member and measure. The "Total" column comes last.
- **Headers.** One header row per column level, plus a row of measure names when there are two or
  more measures. No cells are merged.
- **Size.** Beyond 16,384 columns or 1,048,576 rows, export fails with `413 Report.ExportTooWide`.

### 12.4 The writer

`ReportExcelWriter` maps a result onto spec 0019 §4.4's `ExcelTableSpec` and rows. The sheet is
titled by the report, and panes freeze after the header rows and row-header columns.

**Row styles** are spec 0019 §4.4's `ExcelRowStyle`: `Total` for the grand total; `Subtotal1`,
`Subtotal2` or `Subtotal3` for a member with children at depth 1, 2, or 3 and deeper; `Phantom`
for a phantom child; `Normal` for any other row.

## 13. Seeded (standard) definitions

A pack or a distribution ships standard definitions as seeds. A provisioning step keeps them in
every tenant: with no owner, read-only (§5.1), and published to the roles each seed names.

```csharp
// FeatureContribution extension; the item is spec 0011 §2.1's ReportSeedContributionItem
public static FeatureContribution ReportSeed(this FeatureContribution contribution, ReportSeed seed);
public sealed record ReportSeed(
    string Code, string Root, ReportKind Kind, string DocumentResource,   // an embedded JSON resource of the feature's assembly
    string TitleKey, string? DescriptionKey,
    IReadOnlyList<(string RoleCode, ShareAccess Access)> PublishTo, // role grants, written on insert only
    int Version);                                                   // raised when the document or titles change
```

- **The step.** `core.report-seeds` runs at `Order` 900, after every pack and distribution step, so
  the roles a seed names exist. It upserts by `Code` as an enlisted save in its provisioning frame
  (spec 0011 §6.2). Each row has `OwnerId` null and titles from `TitleKey` in each tenant
  language.
- **Versions.** A seed whose `Version` grew rewrites its document and titles. The step's own
  `Version` is the sum of its seeds' versions, so a new or changed seed re-runs the step.
- **Grants** are written on insert only, so an administrator's later change sticks. A named role
  that the tenant lacks is skipped with the warning `ReportEvents.SeedRoleMissing`.
- **Acme** seeds `acme.sales-by-center`, §3.5's pivot, at `View` to the role `Sales`.

## 14. Configuration and telemetry

`ReportOptions` binds `Tellma:Reports` and is validated at startup. It holds §8.8's limits,
`MaxAudienceMembers` (default 2,000, §6.2) and `PlanCacheSize` (default 512, §8.10). The engine
emits the signals below.

| Instrument | Tags |
|---|---|
| `tellma.reports.run.duration` | `kind`; `source`: stored, draft or coded; `outcome`: ok, error or cancelled |
| `tellma.reports.rows` | `kind` |
| `tellma.reports.cancelled` | — |
| `tellma.reports.export.duration` | `kind`; `mode`: sync or background |
| `tellma.reports.audience.members` | — |

- **Logs.** `ReportEvents.DefinitionInvalidAtRun` (warning; the id and codes, never the text),
  `ReportEvents.LimitExceeded` (information) and `ReportEvents.SeedRoleMissing` (warning).
- **Activity.** `report run`, tagged `report.id` or `report.route`, and `report.kind`.

## 15. Testing

Unit suites pin the document, the compiler and the cube; integration suites run on real SQL. All
run on PR except the performance suite, which runs nightly.

- **Document, validator, corpus** (unit). Each §3.4 and §4.5 rule; normalization; checked-in
  documents with their expected diagnostics (`pointer`, code, span).
- **Compiler and SQL goldens** (unit). §8.4 per term kind and shape; key groups and grouping sets
  for flat, tree and mixed axes; identity columns; ordering per slot; one golden per kind and per
  Part F item used (spec 0010).
- **Cube builder** (unit). §8.6's decoding: phantom, (None), leaves, redundancy, truncation; member
  order; caps; byte-identical output for one input.
- **Runs** (integration). §4.4's table; defaults share the query's `today()`; `'uq'` grouping; a
  summary total equals the viewer's list rows; traversal 403; round-trip counts; an abort reaches SQL.
- **Sharing and audience** (integration). Every criterion and reason kind; `Edit` saves and `View`
  does not; standard rows; one SQL text for two users; the `permissions` extra; root counts; hidden
  `Members`; the cap.
- **Child routes, coded report, seeds** (integration). `Via` rebasing, the owner path,
  grandchildren; Acme's aging runs and exports under its securable; seed insert, rewrite and
  missing role.
- **Export** (integration). Each kind's workbook (headers, styles, indent, panes, RTL view,
  formats); the 413; the background job and `core.export.ready`.
- **Performance** (nightly). A pivot over 1M lines, with a 6-level tree and two column dimensions,
  runs under 2 s on the CI SQL tier.

## 16. Definition of done

Every section (§2–§14) ships with its securables, routes and codes, and passes §15's suites.

- **UI rules** are spec 0034's to meet. This spec serves them:
  - nothing of a report is a launch document;
  - instant feedback: a draft runs in one round trip, and results are self-describing (§8.3, §9);
  - non-blocking: every read is cancellable (§8.9);
  - stable layout: cube shape and member order are deterministic (§9.3);
  - RTL: exports set a right-to-left sheet view in an RTL culture.
- **Acme** (spec 0018 §1.4) adds `acme-reports/aging`, the seed and reports over a child root. Its
  end-to-end test shares a definition (the grantee's bell shows it), publishes one, reads the
  audience, runs and exports each kind, and drills through.

## Decisions record

1. **"Coded report" and "report definition"** — the names say what each is: code, or stored data.
   "Hardcoded" reads as a defect, "tier 2" is internal, and "built-in" also fits seeds (§1).
2. **Coded reports have no rows** — the client registry and the securable suffice. Read-only rows
   would duplicate the registry and drift with every deploy (§11).
3. **Kinds `List`, `Summary`, `Pivot`, `Chart`** — familiar words. "Non-aggregate, aggregate flat,
   aggregate pivot" is long; "tabular, grouped, crosstab" is less familiar (§3).
4. **One versioned document with exactly one kind block** — it is always read whole; `Root` and
   `Kind` stay columns for lists. Child tables for terms were rejected (§3.1).
5. **Terms are column keys or expressions; navigation labels read as `max(...)`** — a report expands
   like the search page and keeps one grouping expression per level (§3.1, §8.4).
6. **Pivots need eight additive engine items (spec 0010 §F.8–§F.15); trees use level navigations**
   — a query per level pair re-scans facts, a closure join counts each fact once per ancestor, and
   client aggregation is forbidden (§8.5, §8.6).
7. **An omitted optional parameter imposes no restriction** — atoms fold by polarity. Binding null
   returns no rows, and `@p is null or …` burdens every author (§4.4).
8. **Defaults run as a constant statement before the query** — one engine entry point. Per-use
   parameter expressions would save the round trip but need a second engine amendment (§4.3).
9. **Sharing is a constant criterion with an `Exists` semi-join** — one plan serves every user.
   Per-user id lists are unbounded, and a materialized audience goes stale (§5.3).
10. **Members author and share their own; `Publish` and `Transfer` are role grants; shares change
    only through actions** — every user can author, and an `Edit` grantee cannot re-share (§5.2, §5.4).
11. **The audience lists members with reasons and counts root readers per grant** — it answers "who
    and why" with no per-member permission evaluation (§6).
12. **Delete removes for everyone after the host's confirmation; a dashboard blocks it** — passing
    ownership to the system leaves content nobody answers for; soft delete adds a third state (§5.6).
13. **Drill-through uses server-bound filter text and optional `drillColumns`** — URL arguments would
    change `query`'s meaning; default columns alone miss the measure's source column (§3.3, §9.4).
14. **Running totals wait** — `running` is reserved and a window-function amendment is additive.
    Client accumulation is wrong across pages (§3.1).

## Review flags

1. **`Title` is required** (§2.1). Alternative: optional, shown as "#id". Flips if users want
   throwaway untitled reports more than readable pins and notifications.
2. **Default tree depth 6, at most 10** (§3.1). Alternative: always `MaxDepth`. Flips if real charts
   of accounts run deeper and the truncation notice shows often.
3. **An omitted parameter inside `or` lifts the disjunction** (§4.4, spec 0010 §F.10). Alternative:
   drop the atom from its connective. Flips on author confusion in practice.
4. **A stored run with defaults costs three round trips** (§8.3). Alternative: parameter
   expressions and an in-batch definition check, one round trip warm. Flips if dashboards measure slow.
5. **Drill ignores measure conditions and `having`** (§9.4). Alternative: carry a measure's
   condition into its column's drill. Flips if drilled rows disagree with conditional measures.
6. **Shares stamp `ModifiedAt`** (§5.4). Alternative: leave the row unstamped, so an open studio
   gets no 409 after a share. Flips if such conflicts show up.
7. **Root readability counts stored role grants only** (§6.2). Alternative: evaluate each member's
   decision. Flips if self-scoped roots make the warning misleading.
8. **Every member authors and shares; only administrators transfer** (§5.2). Alternatives: authoring
   through a role grant; owners handing over. Flips if tenants restrict authoring.
9. **Reports apply no activatable conjunct** (§8.3). Alternative: hide inactive master rows as the
   search page does. Flips if master-data reports surprise users.
10. **No export of a drill-through list over a child entity** (§10). Alternative: `export` on child
    query surfaces. Flips when users ask to export drilled invoice lines.
