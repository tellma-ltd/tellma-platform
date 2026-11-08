# Spec: Report Renderers and Studio

- **Author:** Ahmad Akra
- **Date:** 20 October 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Spec 0033 stores reports and runs them on the server. Every run, whether of a stored report, an
inline report or a coded report, returns one self-describing `ReportResult`. This spec is the
client half:

- **Renderers.** It draws that result as a list, a summary, a pivot or a chart.
- **Hosts.** It shows a report on its own page, in a details card, on a dashboard tile or for a
  future assistant.
- **Pages.** It ships the reports page and the report studio.

Two generic components come with it, both `@tellma/core-ui` entry points that know nothing of
reports: `tm-pivot`, an Excel-style pivot grid, and `tm-chart`, the Tellma facade over Apache
ECharts. Everything else lives in three lazy `@tellma/core` entry points built on specs 0024–0031.

Other specs that carry parts of this design:

- spec 0033 §4.6, §8.1, §8.11, §9, §11.4, §12.1 — parameter displays, `reports/run`, measure
  errors, the result envelope, the `codedReports` metadata and the export routes.
- spec 0035 §9.1, §11 — the report widget and its refresh.
- spec 0030 §2.4, §2.5, §17 — named tabs, the `search` hatch and list mode.
- spec 0031 §3.7, §5, §8, §11, §13 — the `page` and `create` hatches with `edit: 'page'` and
  `refresh: 'page'`, the compact header, the save and its landing, drafts and the page-state
  snapshot.
- spec 0029 §3, §7, §9 — `TmDetailsDeclaration`, `TmPermissions` and discover's conjuncts.
- spec 0015 §5.3; spec 0016 §6.1 — the `permissions` extra; `Tellma-Priority`.
- spec 0024 §2.4, §4.4, §9.2, §9.4, Appendix A — `tmPage`'s `kind`, the `report` skeleton, the
  report routes, `TmUrls.list` and the build codes `coded-report-not-registered`,
  `coded-report-redeclared` and `coded-report-source`.
- spec 0025 §10, Appendix A — page state and the `report.<id|key>.params` key.
- spec 0026 §9.2, §9.3; spec 0027 §3.2, §9; spec 0028 §8.1 — the context registry and the
  context rows; launcher entries and `TmLauncher`; the report toasts and announcements.
- spec 0024 §7.9; spec 0029 §5.3 — text direction by markup, with chart canvas text as the one
  exception (§7.4); `tm-display-name`.
- spec 0023 §4.3–§4.4, §7.2, §7.3, §8.3–§8.4 — the generic components that this spec composes.
- spec 0010 §B.8, §C.1, §C.6, §C.7, §D.7, §D.15 — the `categorical` tones and `sequential` steps,
  and the grid and format amendments, that this spec composes.

## Goals / Non-goals

**Goals**

- **Little or no code.** A stored report renders, drills and exports with no code. A coded report
  is one C# action, placed by an optional patch. A report in a details card is one element over an
  inline document.
- **One result, four renderers.** All four read the same result, so coded reports get the same
  tables, pivots, charts and export.
- **Only data waits.** A parameter change dims the output in the same frame, and nothing moves
  afterwards.
- **Every number drills** to its rows, child entities included.
- **Authors build all four kinds** in the studio, by drag or by keyboard.

**Non-goals (explicitly out of scope)**

- Stored reports, coded-report declarations, execution, sharing, the audience and the export
  writers — spec 0033.
- The dashboard grid, tiles and scheduling — spec 0035.
- Assistant-only page actions — the assistant spec (§13).
- Running totals — reserved by spec 0033.
- Not in v1: scheduled delivery; cross-widget filtering; heatmap, treemap, gauge, scatter and map
  charts; chart image export; header sorting in report tables (§5.4).

## 1. Placement and vocabulary

The pivot and the chart are domain-free `@tellma/core-ui` entry points. The reports live in
`@tellma/core/reports`, the studio in `@tellma/core/report-studio` and the sharing dialogs in
`@tellma/core/sharing`. The `@tellma/core` primary holds only declarations.

Contract blocks are TypeScript and C# sketches: names and shapes are normative; imports,
documentation comments, bodies and cancellation tokens are omitted.

| Entry point | Exports | Loaded | Budget (gzip) |
|---|---|---|---|
| `@tellma/core-ui/pivot` | `TmPivot` (`tm-pivot`), `TmPivotModel`, `TmPivotMember`, `TmPivotCell`, `TmPivotWidths` | with the pivot renderer | 15 KB |
| `@tellma/core-ui/chart` | `TmChart` (`tm-chart`), `TmChartSpec` and its parts, `TM_CHART_SPEC_SCHEMA`, `tmValidateChartSpec` | with the chart renderer; the engine chunk on the first plotted spec | 9 KB; the engine per spec 0024 §12 |
| `@tellma/core` primary | `tmCodedReport` and its types (§2.2), `tmInlineReport`, `TmReportRef`, `TmReportArgs`, `tmShareDialog`, `tmTransferDialog`, the wire mirrors (types only), §2.3's declarations | eager | 0.6 KB |
| `@tellma/core/reports` | `TmReportHost`, `injectTmReportHost`, `TmReportDrillEvent`, `TmReportOverlay`, `TmReportParameterField`, `TmReportCell`, `tmReportArgsFromUrl`, `tmReportArgsToUrl`, `TmReportPage`, `TmReportsPage` | lazy | 22 KB |
| `@tellma/core/sharing` | `TmShareDialog`, `TmTransferDialog` | lazy, when a dialog opens | 5 KB |
| `@tellma/core/report-studio` | `TmReportStudio`, `TmReportStudioPage` | lazy, worker group `on-demand` (spec 0024 §5.3) | 28 KB |

- **Imports.** `/reports` imports the primary, `/entity`, `/search`, `/details` and both `core-ui`
  entry points. `/sharing` imports the primary and `/entity`, so a dashboard's Share loads no report
  code. `/report-studio` imports `/reports` and `/details`. Code reaches the studio and the dialogs
  only through `import()`, and `core-ui/chart` reaches ECharts through one `import()`.
- **The drill-through list** is spec 0030's search host in list mode. Its crumb and chips (§8.3,
  §8.4) are written in `@tellma/core/search`, to this spec's rules.
- **Folders:** `client/projects/core/tellma-core-ui/{pivot,chart}/` and
  `client/projects/core/tellma-core/{reports,report-studio,sharing}/`. **Telemetry areas:**
  `tm.report` and `tm.studio`.
- **Test helpers:** the harnesses `TmPivotHarness`, `TmChartHarness`, `TmReportHostHarness`,
  `TmReportStudioHarness` and `TmShareDialogHarness`, and the fixture `tmReportResultFixture`.
- **Wire mirrors.** `TmReportDocument` with its term types (`TmReportTerm`, `TmReportDimension`,
  `TmReportDisplay`, `TmReportFormat`), `TmReportParameter`, `TmReportResult` with `TmReportColumn`,
  and `TmCodedReportMetadata` mirror spec 0033's records in camelCase.

**Vocabulary**

- *Report* — what a host renders: a stored report, a coded report or an inline report.
- *Inline report* — a document run as `{ inline }`, with no stored row: the studio's preview, an
  assistant's, or one declared in code with `tmInlineReport` (§2.1). A declared one has a `name`.
- *Draft* — the studio's unsaved working copy, kept by `TmDrafts` (spec 0031 §11).
- *Run* — one `reports/run` call, or one call of a coded report's route.
- *Effective arguments* — the run's echoed `arguments`, each with its source: `Supplied`,
  `Default` or `Omitted`.
- *Bound arguments* — values the embedding page fixes; the parameter bar never shows them.
- *Output* — the box a renderer draws in. Its *held result* is what it shows while a newer run is
  pending or has failed.
- *Generation* — a counter the host bumps on each `run()`, so the same arguments run again.
- *Slot* — one of a tenant's three content languages (spec 0013 §7.5); `label`, `label2` and
  `label3` fill slots 1–3. The *active slot* is the viewer's. A label or text read in the active
  slot falls back to slot 1 when empty (spec 0033 §3.1).

## 2. What a distribution writes

Stored reports need no code. A coded report is one C# action, placed by an optional patch; a report
in a details card is one element over an inline document.

### 2.1 A report in a details card

```html
<!-- customers/customer-details.ts: the customer's sales by month, bound to the record -->
<tm-details-card titleKey="Acme_Customer_SalesByMonth" flush>
  <tm-report-host [report]="customerSales" [parameters]="{ Customer: details.entity()?.id ?? null }" />
</tm-details-card>
```

```ts
// customers/customer-sales.report.ts: an inline document, type-checked at build
export const customerSales = tmInlineReport('acme.customer-sales', {
  root: 'acme.SalesInvoiceLine', kind: 'Summary', titleKey: 'Acme_Customer_SalesByMonth',
  document: { formatVersion: 1, filter: 'Invoice.CustomerId = @Customer',
    parameters: [{ key: 'Customer', type: 'Numeric', entity: 'acme.Customer' }],
    summary: { columns: [{ expr: 'startOfMonth(Invoice.PostingDate)' }, { expr: 'sum(Amount)', labelKey: 'Acme_Report_Sales' }],
               orderBy: [{ term: 0, desc: true }], top: 12 } },
});
```

- **Bound.** The host sends `Customer` with every run and never shows it.
- **Inline.** The document ships with the code, so the card needs no stored row, grant or share
  after deployment. Its only gates are the root's `Read` and the traversal rule (spec 0033 §8.2):
  it shows to exactly the readers of its root, and a child root composes its owner's decision
  (spec 0014 §5.5). Over the card's own entity or one of its children, that is the readers of the
  record.
- **Checked per run.** The server validates an inline document on every call and plans it once
  (spec 0033 §8.10). A schema change that breaks it shows "This report no longer runs" (§3.3).
- **Create mode.** The record has no id yet, so `Customer` binds as `null`. In `card` mode on a
  details page in create mode (`injectTmDetails().mode()` is `'create'`), a bound `null` shows
  "Available after saving" and sends no run, since `null` means omitted (spec 0033 §4.4) and the
  card would otherwise show every customer's figures. On a saved record a bound `null` is an
  ordinary omission and runs.
- **A coded card.** When the document cannot express the report, the card embeds a coded report by
  key: `[report]="{ key: 'acme-credit-exposure' }"`. It shows to holders of the report's
  securable; an action on the entity's own service with `Action = "Read"` needs no new grant (spec
  0033 §11.1).
- **The same report twice** on one page sets `stateScope` on each host (§3.1).

### 2.2 `tmCodedReport`

The server declares every coded report and publishes it in the metadata's `codedReports` section
(spec 0033 §11). The client adds only its launcher placement and, for a hatch, its own code.

```ts
// @tellma/core (primary)
declare function tmCodedReport(key: string, patch: TmCodedReportPatch): TmCodedReportDeclaration;
interface TmCodedReportPatch {                            // patches the metadata entry of the same key
  readonly section?: string; readonly order?: number; readonly icon?: string; readonly synonymsKey?: string; readonly visibleWhen?: (context: TmNavContext) => boolean; // the launcher entry (spec 0027 §3.1, §3.2)
  readonly run?: (api: TmApi, args: TmReportArgs, options: { readonly signal: AbortSignal; readonly priority?: 'low' }) => Promise<TmReportResult>; // hatch: custom fetch; passes options to TmApi.read
  readonly component?: TmLazyComponent;                   // hatch, or beside route: replaces the output; reads injectTmReportHost()
}
interface TmCodedReportMetadata {                         // one codedReports entry (spec 0033 §11.4), types only
  readonly key: string; readonly feature: string; readonly route?: string; // route: the report action's; absent for a hatch
  readonly securable: TmSecurableRef;                     // gates the route and the launcher entry
  readonly kind: TmReportKind; readonly titleKey: string; readonly descriptionKey?: string;
  readonly export: 'Platform' | 'Custom' | 'None';        // §9
  readonly parameters: readonly TmReportParameter[];      // bar order: the C# record's constructor order
}
type TmReportKind = 'List' | 'Summary' | 'Pivot' | 'Chart';                                   // spec 0033 §2.1
type TmReportParameterType = 'Numeric' | 'String' | 'Bool' | 'Date' | 'DateTime' | 'DateTimeOffset' | 'Guid'; // spec 0033 §4.1
type TmReportArgs = Readonly<Record<string, TmJson>>;     // spec 0033 §4.4's encoding; null = omitted
```

**Example —** Acme's credit exposure report, the whole client side. Its key, title, kind,
securable, `AsOf` (required, defaulted in C#) and `Customer` come from spec 0033 §11.1's C#:

```ts
// app/reports/credit-exposure.report.ts
export const creditExposureReport = tmCodedReport('acme-credit-exposure', { section: 'sales' });
```

- **One union.** `TM_APP_DECLARATIONS.reports` (spec 0024) is built at boot from the `codedReports`
  entries plus the patches, merged by key in feature order. The launcher, the `reports/:key` route
  table (spec 0024 §9.2), `hide: ['entry:<key>']` and `dangling-hide` read that union.
- **A patch is optional.** A key with no patch gets a search-only launcher entry (spec 0027 §3.2),
  so a route-backed coded report can need no TypeScript at all. A patch never sets the title, kind,
  securable or parameters, so a pack's report cannot be retitled on the client.
- **Sources.** A route-backed report may add `component`, which renders the host's result. A hatch,
  which has no `route`, adds exactly one of `run` or `component`.
- **Build codes** (spec 0024 Appendix A): `coded-report-not-registered` for a patch whose key is not
  in `codedReports`; `coded-report-redeclared` for a patch that sets a server-owned member;
  `coded-report-source` for a route-backed patch with `run`, or a hatch with neither or both of
  `run` and `component`.
- **The call.** The host calls `TmApi.read(route, args, { signal })` with the keys in camelCase,
  such as `{ "asOf": "2026-10-20", "customer": 1012 }` (spec 0033 §11.1).
- **Long by declaration.** The action declares `Long = true`, so the Contracts chunk lists it and
  `TmApi` gives it the long watchdog (spec 0024 §3.4). A `run` hatch that calls a hand-mapped
  endpoint (spec 0016 §4.5) passes `long: true` itself.
- **A `component`** renders inside the page chrome and reads `injectTmReportHost()`. Without
  `route` it fetches its own data. It renders values through `tm-report-cell` (§5.1), so its cells
  match the built-in tables. A `component` beside `route` is the cheaper hatch: the platform
  exports it.
- **Derived from metadata and the patch:** the route `/{t}/reports/{key}` (keys per spec 0024
  §9.1), the launcher entry, the parameter bar, Export (§9), the page context and telemetry.
- **Access.** A coded report is an app feature: its route and launcher entry show when
  `TmPermissions.can` passes on its securable. It is never on the reports page (§11) and is never
  shared; a dashboard's share dialog only warns which viewers lack its securable (spec 0035 §7.1).

### 2.3 The platform's declaration

One entity declaration and one page, in the implicit core feature, give the reports page, the
report page, create mode and the studio route.

```ts
// @tellma/core primary
export const tmReports = tmEntity('core.Report', {
  section: 'analysis', order: 10, icon: 'chart-column', synonymsKey: 'Core_Report_Synonyms',
  columns: ['Title', 'Kind', 'Root', 'Owner', 'ModifiedAt'], orderBy: 'Title',
  tabs: [{ key: 'all', labelKey: 'tm.report.tabAll' },
         { key: 'mine', labelKey: 'tm.report.tabMine', queryex: 'OwnerId = me()' },
         { key: 'shared', labelKey: 'tm.report.tabShared', named: 'shared-with-me' }], // spec 0033 §5.5
  search: () => import('@tellma/core/reports').then((m) => m.TmReportsPage),
  details: { page: () => import('@tellma/core/reports').then((m) => m.TmReportPage),
             create: () => import('@tellma/core/report-studio').then((m) => m.TmReportStudio),
             edit: 'page', refresh: 'page', header: 'compact', // Edit opens the studio; Refresh is report.refresh (§10.2); one header row and a band (§10.1)
             createdUrl: (r, urls) => urls.page(`reports/${r.id}/studio`) }, // a create stays in the studio (§12.1)
  clone: (copy, source) => ({ ...copy, code: null, basedOnId: source.id }), // Clone is "Save as"
  actions: [{ name: 'share', visible: () => false, bulk: false, dialog: tmShareDialog('reports') }, // opened by report.share (§10.2)
            { name: 'publish', visible: () => false, bulk: false },               // the share dialog covers it
            { name: 'transfer', dialog: tmTransferDialog() }],
});
export const tmReportStudioPage = tmPage('reports/:id/studio', {
  component: () => import('@tellma/core/report-studio').then((m) => m.TmReportStudioPage), titleKey: 'tm.studio.title',
  kind: 'studio' });                                              // the first frame's route kind (spec 0024 §2.4)
```

`tmShareDialog(segment)` and `tmTransferDialog()` lazily load §10.4's and §10.3's dialogs from
`@tellma/core/sharing`. Spec 0035 passes `'dashboards'`.

## 3. The report host

`tm-report-host` runs one report and renders its parameter bar, output, states and footer. Every
placement is a `mode` of it, and whatever renders inside reads it through `injectTmReportHost()`.

### 3.1 Contract

```ts
// @tellma/core primary (types only)
type TmReportRef = { readonly id: number } | { readonly key: string } | { readonly inline: TmInlineReport; readonly name?: string };
interface TmInlineReport { readonly root: string; readonly kind: TmReportKind; readonly document: TmReportDocument; readonly titleKey?: string; readonly title?: string } // title: plain text, such as the studio's working title
declare function tmInlineReport(name: string, report: TmInlineReport): TmReportRef; // a frozen { inline, name }; name: 'acme.customer-sales'

// @tellma/core/reports
@Component({ selector: 'tm-report-host' })
declare class TmReportHost {
  readonly report: InputSignal<TmReportRef>;
  readonly mode: InputSignal<'page' | 'card' | 'widget' | 'assistant'>;  // default 'card' (§3.6)
  readonly stateScope: InputSignal<string | undefined>;                 // tells two placements of one report on a page apart; joins the grid ids (§5.1)
  readonly parameters: InputSignal<TmReportArgs | undefined>;           // bound arguments
  readonly showParameters: InputSignal<boolean | undefined>;            // default: true in page and assistant mode
  readonly declarations: InputSignal<readonly TmReportParameter[] | undefined>; // the bar's fields, when the page holds them
  readonly document: InputSignal<TmReportDocument | undefined>;         // the stored document, when the page holds it: labels a calculation error (§3.3)
  readonly held: InputSignal<TmReportResult | undefined>;               // shown first; no run until run() or an argument change
  readonly refresh: InputSignal<'spinner' | 'silent'>;                  // how a pending run looks (§3.4)
  readonly drillable: InputSignal<boolean>;                             // default true; false: nothing drills, as §6.4's cells that do not drill
  readonly dashboardId: InputSignal<number | undefined>;                // sent with each run (spec 0033 §8.2)
  readonly stateKey: InputSignal<TmPageStateKey | undefined>;           // page mode: where §3.7 remembers
  readonly overlay: InputSignal<TmReportOverlay | undefined>;           // the studio's presentation edits (§12.8)
  readonly asTable: ModelSignal<boolean>;                               // a chart shown as its table (§7.8)
  readonly expansion: ModelSignal<TmReportExpansion | undefined>;       // a pivot's expanded members (§6.1); undefined: §6.5's initial expansion
  readonly drill: OutputEmitterRef<TmReportDrillEvent>; readonly ran: OutputEmitterRef<TmReportResult>;
  readonly status: Signal<'waiting' | 'loading' | 'ready' | 'error'>;   // waiting: a required argument is empty
  readonly args: Signal<TmReportArgs>; readonly result: Signal<TmReportResult | undefined>; // the supplied values; the held result
  readonly error: Signal<TmApiError | null>; readonly executedAt: Signal<string | null>;
  readonly stale: Signal<boolean>;                                      // a silent refresh failed (§3.4)
  readonly generation: Signal<number>; readonly signal: Signal<AbortSignal>; // for a coded component
  setArgs(patch: TmReportArgs): void;                                   // the bar's commits; null = omitted
  run(options?: { clear?: boolean; background?: boolean }): void;      // a new generation; clear drops the held result first (§3.4)
  cancel(): void; export(): Promise<void>;                              // §9
  expandAll(axis?: 'rows' | 'columns'): void; collapseAll(axis?: 'rows' | 'columns'): void; // a pivot
}
interface TmReportExpansion { readonly rows: readonly number[]; readonly columns: readonly number[] } // member ids
declare function injectTmReportHost(): TmReportHost;
```

- **One `resource()`.** The host runs through one `resource()`, keyed by the report, the bound
  arguments, `args` and `generation`. A new key aborts the run in flight. Leaving aborts it through
  `DestroyRef`, all the way down to SQL (spec 0033 §8.9).
- **Transport.** `{ id }` and `{ inline, name }` call `TmApi.read('reports/run', { id | inline: {
  root, kind, document, name, title }, arguments, dashboardId, skip, take, includeCount: true }, {
  signal })`. `title` is `inline.title`, else the text of `inline.titleKey`, else absent. `{ key }`
  calls the metadata entry's `route` or the patch's `run`.
- **Priority.** In `widget` mode every run, foreground or silent, also passes `priority: 'low'`
  (spec 0024 §10.1). On `reports/run` and on a coded report's route, spec 0016 §6.1 puts it in the
  batch lane, so a dashboard never takes a report page's permit. The tile's Export keeps the
  normal priority, since the user asked for it.
- **Paging.** Only List and Summary send the paging members, and only for `{ id }` and
  `{ inline }`. `take` is the kind's page size (§5.1). A `{ key }` result is one page (spec 0033
  §11.3), so its footer shows the count and never a pager.
- **Long reads.** Every run is a retried, cancellable long read (spec 0024 §10.5). The Contracts
  chunk marks `reports/run` long and idempotent (spec 0024 §3.4).
- **Arguments sent** are `args` merged with the bound arguments; a bound value wins. The bar shows
  the effective arguments, so a defaulted parameter shows its default value.
- **No `get`**, with one exception. A host that shows the bar for `{ id }` and has no
  `declarations` reads `reports/get` once, alongside the run. `widget` mode never reads it.

### 3.2 Opening a report page

1. The route host draws the `report` skeleton in the click's frame (spec 0024 §4.4), for
   `/{t}/reports/{id}` and `/{t}/reports/{key}` alike: the compact header (§10.1) and the output's
   skeleton.
2. For a stored report, the details host reads `reports/get` with the `permissions` extra (spec
   0031 §3.3), and the report page starts the run at the same time. Each `p.*` value travels as its
   URL text, and `p.Key=` as `null` (§4.4), so the run never waits for the parameter types. The
   header's band appears when the record lands, because its height depends on the document.
3. For a coded report, the metadata is local, so the band draws at once and the run starts at
   once, unless a required parameter has neither a value nor a default (§4.3).
4. The result renders, the page rewrites its URL from the echo (§4.4), registers its context (§13)
   and records `tm.report.run`.

**Example —** `/17/reports/42` is a summary, and its URL has no `p.*`. So `reports/get` and this
run leave together: `reports/run { id: 42, arguments: {}, take: 1000, includeCount: true }`. The
result echoes `From = 2026-01-01` (`Default`) and `To` (`Omitted`), and the bar shows both.

### 3.3 Output states

The output shows one state at a time inside its own box, so the header, bar and footer never move.
The table lists the report's own states; any other read failure renders spec 0024 §11's
`tm-error-state` from `describe(error, { kind: 'read', entity })`.

| State | When | Shows | Action |
|---|---|---|---|
| first load | no held result | the kind's skeleton (below) | Cancel after 2 s ("Still running…") |
| reloading | a held result and a foreground run | the held output at 0.6 opacity, `inert`, `aria-busy`; a `tm-progress-bar` on its top edge (below) | Cancel after 2 s |
| waiting | a required parameter without value or default (§4.3), or `422 Report.ArgumentRequired` | `sliders-horizontal`, "Fill in the required parameters" with their labels | Fill in: focuses the first empty field |
| invalid argument | `422 Report.ArgumentInvalid` | the waiting state, and the field's error popover | — |
| not saved yet | `card` mode on a details page in create mode, a bound argument is `null` (§2.1) | `file-plus`, "Available after saving" | — |
| no data | an empty table, cube or series | `inbox`, "No data for these parameters" | — |
| too much data | `413 limit-exceeded` whose `limit` is `Report.TooManyRows`, `Report.TooManyPoints`, `Report.TooManyCells` or `MaxTake` (spec 0033 §8.8) | `circle-alert`, "This report returns too much data to show", "Add a filter or narrow the parameters." | Edit, on the report page when `save` is not refused (§10.2) |
| needs fixing | `400 query-invalid`; for a named inline report, a `422` at `inline.document` (spec 0033 §8.3) | `wrench`, "This report no longer runs", "A field it uses has changed." | Edit, on the report page when `save` is not refused (§10.2) |
| calculation error | `422 Report.MeasureFailed` (spec 0033 §8.11) | `circle-alert`, "A measure couldn't be calculated", and the reason (below) | as needs fixing |
| cancelled | Cancel with no held result | `circle-pause`, "The report was stopped" | Run again |

- **Skeletons.** List and Summary show `tm-skeleton shape="table"` with 12 rows. Pivot and Chart
  show a `block` frame. While the kind is unknown, the skeleton is `table`.
- **The reloading bar** follows the search page's rule (spec 0030 §3.3):
  - it shows in the frame that starts the run;
  - it holds while the held output is stale, so a run that supersedes another never blinks it off;
  - once shown, it stays at least 250 ms (spec 0023 §4.4).

  `aria-busy` follows the run itself.
- **The calculation error's reason** comes from the error's arguments:
  - *`divide-by-zero`* reads "{measure} divides by zero at {cell}. Guard it with if(…)."
  - *`overflow`* reads "{measure} is too large at {cell}."
  - *`{cell}`* joins the `row` captions, then the `column` captions, with " × ". Captions render
    as the server sends them (spec 0033 §8.11). An empty list reads "Total", and a null caption
    reads "(None)". For example: "Riyadh × 3", "Total × 3", or "Total" for a summary's totals row.
  - *Without `row`,* SQL raised the error: "at {cell}" reads "in some rows", and the divide-by-zero
    hint reads "Guard the division with if(…). If it is inside an aggregate, put the if inside:
    sum(if(Qty = 0, 0, Amount / Qty)).", since SQL evaluates an aggregate before an `if` around it.
  - *`{measure}`* is the label (§3.5) of the term at `pointer`. The host reads it from the inline
    document, the stored document or the held result. The stored document is the `document` input,
    or the document the host read itself (§3.1). With none at hand, or with an empty pointer, it
    reads "A calculation".
- **A developer's report** fails with a `500` instead: a standard report, a named inline report or
  a document built in C# (spec 0033 §8.11). It renders `tm-error-state` like any other failure. A
  named inline report's `422` at `inline.document` is a developer's bug too: the card shows "This
  report no longer runs" without Edit and reports it as a client error (spec 0026).
- **Waiting in `widget` mode** reads "This widget needs a value for {label}." and offers no Fill in.
- **States** are `tm-empty-state` with `live="polite"`. A cancel over a held result is silent.
  `forbidden` and `not-found` clear the held result in every mode, so revoked data never lingers.
- **The notice strip** is one 32 px row that overlays the output's top edge, like spec 0031 §5.3's
  status strip, so it takes no layout space. It holds one closable info `tm-alert`: a refused drill
  (§8.2) or "Preview is out of date" (§12.8).

### 3.4 Refresh modes

`refresh` decides how a pending run looks: `silent` for dashboards' scheduled runs, else `spinner`.

| Trigger | `spinner` (default) | `silent` |
|---|---|---|
| the first run, or no held result | the skeleton | the skeleton |
| `run()` | reloading | the held output, unchanged and without indicator, swapped on arrival; sent as a background run unless `run({ background: false })` |
| an argument change | reloading | reloading |
| `run({ clear: true })` | the held result is dropped; the skeleton at once | the same |
| a failure over a held result | the failure's state | `forbidden`, `not-found`: the state; any other: the held output stays and `stale()` is true |

- **The swap** is one change-detection pass, never animated. A background run sends
  `Tellma-Activity: background` (spec 0024 §10.2) and stamps no activity. A presenting dashboard
  runs with `background: false`, so its runs keep the session alive (spec 0035 §11.2).
- **The stale marker** is a 16 px warning `circle-alert` after the footer's time. Its accessible
  name is "Couldn't refresh. Showing data from {time}.", and its tooltip holds the trace id's
  `tm-reference`.

### 3.5 From result to renderer

- **The renderer follows `result.kind`.** Before the first result, it follows the kind of the coded
  report's metadata, the inline report or `held`. As soon as the kind is known to be `Chart`, the
  engine's `import()` starts.
- **A coded `component`** replaces the renderers of §5–§7.
- **Column labels** are the first that exists: the active slot's `label…`, then `labelKey`, then
  the slash label of `path` from `result.root` (spec 0029 §11.4), then `expression`. A language
  switch re-labels with no run.
- **Values** format through `tmReportFormat(column, row)`:
  - navigations through `TmDisplayNames.of(entity, row)`, over the column's ordinals (spec 0033
    §9.2);
  - text in the active slot;
  - the column's effective `display` and `format` (spec 0033 §3.2) through `TmL10n`: spec 0005
    §2.2's `formatNumber`, with `format.decimals` as both its minimum and maximum fraction digits
    and `grouping` (spec 0010 §D.15), and `formatDate`, and spec 0010 §D.4 for instants;
  - `enum` through the named enum's labels (`TmEntities.enum(format.enum)`, spec 0029 §2.2), or
    through `format.choices`, whose labels resolve as column labels do; an unmatched value formats
    as it would without the override;
  - `monthName` and `weekdayName` through spec 0010 §D.7's names, a month in `format.calendar`,
    else Gregorian;
  - a `decimal` or `long` value from the exact string it arrives as (spec 0033 §9), never through
    `number` (spec 0024 §10.4);
  - `month`, `quarter` and `year` per spec 0010 §D.7, in `format.calendar`, else the viewer's
    calendar;
  - numbers in a `dir="ltr"` isolate, aligned to the physical right.
- **Display names** in list and summary cells render `tm-display-name` from
  `TmDisplayNames.parts` (spec 0029 §5.3). Pivot members, copies, exports and accessible names take
  the plain text, and canvas labels take its parts (§7.4).

### 3.6 Modes

The `mode` input fits the host to its placement. The table lists what each mode shows and does.

| Element | `page` | `card` | `widget` | `assistant` |
|---|---|---|---|---|
| title, description | the page header (§10.1) | the card's heading | the tile's (spec 0035) | a title line |
| parameter bar | shown; on the report page, in the header's band (§10.1) | hidden unless `showParameters`; bound arguments never | hidden | compact |
| toolbar | the page's action bar (§10.2) | a `…` menu in the card's actions: Refresh, Export, Show as table, and Open report except for an inline report | none: the tile calls `run()` and `export()` | Open report, Export |
| footer | "1–200 of 1,214 rows · Updated 10:42" and the pager (§5.1) | "Updated 10:42" | none | as `card` |
| URL, remembered values, page state | the report pages write them (§4.4, §4.5); page state with `stateKey` (§3.7) | no | no | no |
| block size | fills `main` | 360 px (`--tm-report-height`), scrolling inside | fills the tile | 320 px |
| drill-through | navigates | navigates | navigates | emits only |
| page context | `report` (§13) | none: the details page's | the dashboard's | the assistant's |

- **"Updated {time}"** is an absolute time in a `<time>` element. For a run today in the display
  zone it is `TmL10n.formatTime(executedAt)`. Otherwise it is `formatDateTime(executedAt, {
  dateStyle: 'medium' })` (spec 0010 §D.4). Both add the zone label when the display zone's
  offset differs from the device's: "Updated 10:42 GMT+3".
- **A future search-page pivot view** is this host in `card` mode, fed an unnamed `{ inline }`
  built from the search page's query. Nothing else is reserved for it.

### 3.7 Page state

In `page` mode with a `stateKey`, the host saves `{ args, result, scroll, expansion, widths,
asTable, modifiedAt }` as page state when it is destroyed (spec 0025 §10). `widths` are a pivot's
(§6.2). Results never touch disk.

- **Keys.** The report page saves under `tmPageStateKey('/{t}/reports/{id}/run')`, apart from the
  details host's own snapshot (spec 0031 §13). The coded page saves under its path.
- **Return.** Coming back renders the saved result as held, with no request. A changed
  `modifiedAt` discards the state. State that is older than 5 minutes or marked stale (`isStale`,
  spec 0025 §10) renders and also runs, as reloading.

## 4. Parameters and the parameter bar

The parameter bar renders one typed field per parameter of the stored document, the coded report's
metadata or the inline document. A value applies when its field commits. The bar keeps the URL and
the remembered values in step.

### 4.1 Controls by type

```ts
// @tellma/core/reports — one typed field; the bar and spec 0035's dashboard bar compose it
@Component({ selector: 'tm-report-parameter-field' })
declare class TmReportParameterField {
  readonly parameter: InputSignal<TmReportParameter>;
  readonly value: InputSignal<TmJson | undefined>;        // undefined: not supplied
  readonly effective: InputSignal<TmJson | undefined>;    // the run's echoed value, shown when nothing is supplied
  readonly committed: OutputEmitterRef<TmJson | null>;    // a pick, Enter or blur; null: cleared (omitted)
}
interface TmReportParameter {                             // primary, types only (spec 0033 §4.1); a coded one comes from metadata
  readonly key: string; readonly label?: string; readonly label2?: string; readonly label3?: string; readonly labelKey?: string; // labels per slot
  readonly required?: boolean; readonly default?: string; // default: a stored or inline document's Queryex
  readonly hasDefault?: boolean;                          // codedReports entries only: the C# method applies a default; absent: default != null
  readonly type: TmReportParameterType; readonly entity?: string; readonly integer?: true; // integer: a coded int or long member (codedReports only)
  readonly display?: TmReportDisplay; readonly format?: TmReportFormat; // the control and its text (spec 0033 §4.6)
}
```

| Type and display | Control (in a 168 px `tm-form-field`) | Commits on | URL text |
|---|---|---|---|
| `Numeric` with `entity` | `tm-entity-field [entity]` (spec 0029 §14) | pick, clear | `p.Customer=1012` |
| `Numeric`: `number`, `integer`, `percent` | `input[tmNumber]`: `format.decimals` as its minimum and maximum (0 for `integer` or an `integer` parameter), `percent`, and `format.grouping` (spec 0010 §D.15) | Enter, blur | `p.Year=2026` |
| `enum`; `Numeric`: `monthName`, `weekdayName` | `tm-select` of the choices or the named enum's values in declaration order, or the twelve or thirteen months in `format.calendar` (else Gregorian), or the seven days; "Any" first when optional | selection | `p.Month=3` |
| `String`: `text` | `input[tmInput]`, `dir="auto"` | Enter, blur | `p.Region=East` |
| `Bool`: `bool` | `tm-select`: Any (optional only), Yes, No | selection | `p.Posted=true` |
| `Date` | `tm-date-picker` in `format.calendar`, else the viewer's calendar | pick, Enter, blur | `p.AsOf=2026-10-01` |
| `DateTime` | `tm-date-picker`; the day at `T00:00:00` | as `Date` | `p.Since=2026-10-01T00:00:00` |
| `DateTimeOffset` | `tm-date-picker`; the day's first instant in the display zone (spec 0029 §13.2) | as `Date` | `p.Since=2026-09-30T21:00:00%2B00:00` |
| `Guid` | `input[tmInput]`, mono, `dir="ltr"` | Enter, blur | `p.Ref=…` |

- **Labels** are the active slot's `label…`, else `labelKey`, else the key. Required parameters
  carry the field's required marker. An entity parameter takes a single id.
- **A value outside the choices,** from a link, still runs (spec 0033 §4.4), and the select shows it
  as its text.
- **Clearing** an optional field commits `null`, which removes the restriction. Clearing a required
  field shows "Required" and the waiting state, and sends nothing. A field with nothing supplied
  shows the run's effective value, formatted through its display and hinted "Default".

### 4.2 Layout

- **The bar.** On the report page it sits in the page header's band (§10.1), so it stays in view
  while the output scrolls. Fields wrap onto as many lines as they need, with 12 px gaps, ended by a
  ghost Reset button that is disabled while nothing is supplied. The bar's height depends only on
  the field count and the width: errors are focus popovers (spec 0002), so nothing moves while
  typing.
- **`assistant` mode** puts the labels inside the fields.
- **Below `sm`,** the bar becomes a Parameters button and a one-line summary, such as "As of 20 Oct
  2026 · Customer: Olive Inc.", whose values format through their displays. They open an end
  drawer (`TmModal` with `placement: 'end'`, spec 0010 §D.2) whose Apply commits every field at
  once. Fields there are 46 px (`lg`) tall (spec 0010 §B.6).

### 4.3 Commit and run

1. A field commits, and `setArgs({ Key: value })` replaces that key.
2. If a required parameter has neither a value nor a default (`hasDefault`, else `default`), the
   host shows the waiting state and sends nothing. Otherwise the host runs. The host knows this
   whenever it holds the parameters: for a coded report (from metadata), an inline report, a
   widget, and every commit on a stored report. A stored report's first run leaves with `get`
   (§3.2), and its `422 Report.ArgumentRequired` shows the same state.
3. The run in flight is aborted. The page writes the URL (§4.4) and the remembered values (§4.5).

- **Refresh**, and Mod+Enter in a field, re-run the same arguments under a new generation.

### 4.4 The URL: `p.{Key}`

```ts
// @tellma/core/reports — also used by spec 0035 over its dashboard parameters
declare function tmReportArgsFromUrl(params: URLSearchParams, parameters?: readonly TmReportParameter[]): TmReportArgs | null; // null: no p.* key; no parameters: each value as its text
declare function tmReportArgsToUrl(args: TmReportArgs, parameters: readonly TmReportParameter[]): readonly (readonly [string, string])[];
```

- **Text.** A value is written in its platform scalar encoding (spec 0016 §3.2) without JSON
  quotes, then encoded per spec 0024 §9.5. §4.1's table shows the text for each type.
- **A stored report reads raw text.** Its page opens before `get` lands, so it sends each `p.*`
  value as its URL text in a JSON string, `p.Key=` as `null`, and the server converts it by the
  declared type (spec 0033 §4.4): `p.Posted=true` sends `"Posted": "true"`. Text that does not
  convert answers `422 Report.ArgumentInvalid`, shown as the invalid-argument state, with the
  field's popover once `get` lands. When the result arrives, the page rewrites the URL from the
  echo (`tmReportArgsToUrl(result.arguments, …)`), in the declared spelling and order.
- **Typed elsewhere.** A coded or inline report, whose parameters are local, and a dashboard (spec
  0035 §10) read the URL through the typed codec. It holds a `Numeric` value as a `number`, as
  editors do (spec 0024 §10.4):
  - `p.Year=2026` reads as `2026`, which a `decimal` and an `integer` parameter both accept.
  - Text past 15 significant digits reads as a `TmDecimal`, which a `decimal` takes exactly.
  - Echoed values arrive as `TmDecimal` strings (spec 0033 §4.4). A field shows one through
    `TmDecimals.toNumber`.
- **Keys** match ignoring case. The writer uses the declaration's spelling and order.
- **What is written:**

  | Argument | URL |
  |---|---|
  | a supplied value | `p.Key=<text>` |
  | an explicit omission of a parameter with a default | `p.Key=` |
  | a parameter at its default | nothing, so a shared link re-evaluates `today()` |

- **Updates** use `replaceUrl`. An unknown key drops out, and so does invalid text in the typed
  codec; the host records `tm.report.arg-dropped`.

**Example —** `/17/reports/42?p.To=2026-06-30` runs `arguments: { "To": "2026-06-30" }`, and `From`
takes its default.

### 4.5 Precedence and remembered values

A report page takes its starting arguments from the first of these sources that applies:

1. **The URL.** Any `p.*` key makes the URL define every argument; absent keys take their defaults.
   This matches how spec 0030 §4.2 applies a query as a whole.
2. Else **page state** (§3.7).
3. Else **`report.<id|key>.params`** (spec 0025 Appendix A, write-behind).
4. Else **nothing:** the server applies the defaults.

**Remembered values** are stored as `{ v: 1, args }`.

- **What is kept.** The supplied values of parameters without a default (neither `hasDefault` nor
  `default`), for stored and coded reports alike.
- **Reset.** An empty `args` resets the key. Bound arguments never read or write it.

## 5. List and Summary renderers

Both render a `ReportTable` (spec 0033 §9.2) in a readonly `tm-grid`. A list row opens its record;
a summary row drills through to its rows.

### 5.1 Binding

- **Grid.** `gridId` is `report.<id|key|inline.name>[.<stateScope>]`, such as `report.42.d3.w118`;
  an unnamed inline report gets a per-instance id, so its widths are not remembered. It takes
  `rowActivation="row"` (spec 0010 §C.1) and `rowHeaders: false` (spec 0010 §C.6). Rows are
  zebra-striped and 30 px tall, under a frozen header, with the stable gutter (spec 0010 §C.7).
- **Columns.** One grid column per `ReportColumn`; each cell is a `tm-report-cell` over
  `table.columnOrdinals[i]`. Widths never depend on data: text and navigations `flex 1.6` (at least
  160 px), numbers 120 px, dates 110 px, booleans 72 px, images 56 px.
- **`tm-report-cell`** (`TmReportCell`, inputs `column: TmReportColumn` and `row: readonly
  TmJson[]`) renders one value as these grids do: `tmReportFormat`'s text, display names, numbers
  in isolates, enum labels and images. A coded `component` uses it to match them (§2.2).
- **Images.** An image cell renders a 24 × 24 `rect` `tm-image` in view mode, centred in its
  column. Its `src` is `TmUrls.blob(column.blobKind, imageId, { variant: 'thumb' })` when
  `Variants` lists `thumb`, else the primary; `etag` is the blob id, so a warm revisit uses no
  network, and `alt` is the column label. A null id leaves the cell empty. There is no hover
  preview: the row opens the record, which shows the image full size. A file cell shows its
  `FileName` as plain text.
- **Instant columns.** A column that displays instants appends `TmFormats.zoneSuffix` (spec 0029
  §12.1) to its header, as spec 0030 §7.1 does: "Posted at (GMT+3)". Its cells format in
  `'column'` mode (spec 0010 §D.4).
- **Pages.** A list pages at 200 rows and a summary at 1,000, so every summary with `top` fits on
  one page and can show its totals (§5.3). `tm-grid` windows its rows, so the larger page costs
  nothing on screen. A `{ key }` result arrives whole and is shown whole, with no pager (§3.1).
- **Footer.** It reads "{first}–{last} of {count} rows · Updated {time}", with `{count}+` when the
  count is capped. Past one page it adds a `tm-pager` in range mode. Each page is a run with a new
  `skip`, and paging pushes no history.

### 5.2 List

- **Activation** (click or Enter; with Mod, a new tab) opens the record `table.identity` names,
  which hover and focus preload:
  - a top-level root: `TmUrls.details(entity, id)`;
  - a child root: `TmUrls.details(ownerEntity, ownerId, { collection, id })`, which spec 0031 §14
    opens on the child row; for a grandchild, the intermediate child's id.
- **Navigation cells** link to their target when `TmPermissions.allows(target, 'details')`.

### 5.3 Summary

- **Activation** drills through the row's coordinate (§8.1). Key cells are never links.
- **Totals.** With `totals: true`, `table.totals` is the last row when the page holds the whole
  result (spec 0033 §8.4).
  - *Cells* read through the same `columnOrdinals` as the rows. The key cells are null (spec 0033
    §9.2).
  - *Look.* It takes the `rowClass` `tm-grid__row--emphasis` (spec 0010 §C.6) and shows "Total" in
    the first key column. It is not counted.
  - *Drill.* It drills with no member condition, only its measures' (§8.1). Under
    `drill.leavesOnly` it does not drill, as §6.4 describes for pivot cells (spec 0033 §9.4).
- **Omitted totals.** When `table.totalsOmitted` is true, the footer adds "Totals show when all
  rows fit on one page. Export includes them."
  - `widget` mode has no footer. The tile's status line carries a short note instead (spec 0035
    §8.5).
  - A `widget`-mode host without a status line shows no message; the report page explains.

### 5.4 Not in v1

Report tables have no header sorting, column editor or column filters. The document decides
these, and Edit opens the studio to change it. The drill-through list is a plain query, so it sorts.

## 6. `tm-pivot` and the Pivot renderer

`tm-pivot` renders two member trees as an Excel-style pivot in compact form. It knows nothing of
reports: the Pivot renderer maps a `ReportCube` onto it and formats its cells.

### 6.1 Contract

```ts
// @tellma/core-ui/pivot
interface TmPivotMember {
  readonly id: number; readonly parent: number | null;  // unique on its axis; null: a root (the total, or a first level without totals)
  readonly kind: 'member' | 'phantom' | 'none' | 'total'; // none: the "(None)" member (§6.5)
  readonly label: string;                               // display-ready plain text, rendered in a <bdi>; "(direct)" appended to a phantom
  readonly attributes?: readonly string[];              // rows only, parallel to attributeLabels
}
interface TmPivotModel {
  readonly rows: readonly TmPivotMember[];              // pre-order: a member, then its children in display order
  readonly columns: readonly TmPivotMember[];
  readonly measures: readonly string[];                 // at least one label
  readonly rowLabel: string;                            // the corner: the row dimension names, " / "-joined
  readonly columnLevelLabels: readonly string[];        // one per column level (§6.5)
  readonly attributeLabels: readonly string[];          // the attribute columns' headers
  text(row: number, column: number, measure: number): string | null; // formatted; null: no facts
  widest(measure: number): string | null;               // the measure's widest formatted value in the data (§6.2)
  activatable?(row: number, column: number): boolean;   // absent: every value cell; false: no drill target (§6.4)
}
interface TmPivotWidths {                               // px; undefined: the default (§6.2)
  readonly rowHeader?: number; readonly attributes?: readonly (number | undefined)[]; readonly measures?: readonly (number | undefined)[];
}
@Component({ selector: 'tm-pivot' })
declare class TmPivot {
  readonly model: InputSignal<TmPivotModel>;
  readonly expandedRows: ModelSignal<ReadonlySet<number>>;    // ids of expanded row members
  readonly expandedColumns: ModelSignal<ReadonlySet<number>>; // ids of expanded column members
  readonly widths: ModelSignal<TmPivotWidths>;          // the user's resizes
  readonly activatable: InputSignal<boolean>;           // default true: value cells are drill targets
  readonly ariaLabel: InputSignal<string>; readonly size: InputSignal<TmControlSize | undefined>;
  readonly cellActivate: OutputEmitterRef<TmPivotCell>;
  expandAll(axis: 'rows' | 'columns'): void; collapseAll(axis: 'rows' | 'columns'): void; focus(): void;
}
interface TmPivotCell { readonly row: number; readonly column: number; readonly measure: number; readonly newTab: boolean }
```

- **Lazy text.** The pivot calls `text` only for rendered cells. A 500,000-cell cube costs nothing
  until shown, and a language switch reformats only the visible window. The caller owns the
  expansion sets and the widths.

### 6.2 Layout

- **Rows (compact form).** One row-header column indents members 16 px per depth. Parents carry a
  13 px twisty, a `tm-icon` with `mirrorInRtl` (spec 0023 §3.1). A parent's row carries its
  subtotal, and its children follow it. The `total` member renders last and never collapses.
- **Columns.** An expanded member's group holds its children, then its subtotal column "{label}
  total". The `total` member comes last, as "Total". Each group holds one data column per measure.
  Header rows are one per column level, plus a row of measure names for two or more measures.
- **Default widths.** W is the pivot's inline size, from its own `ResizeObserver`. The row header
  is `clamp(round(0.4 × W), 120, 280)` px and an attribute column 96 px. A measure's columns are
  `max(96 px, fit)`, where fit is `widest(measure)` plus the cell padding, measured once in a
  hidden probe styled like a cell, since canvas measurement cannot request tabular figures. Data
  columns share any slack; when they cannot, each is `max(112 px, fit)` and the pivot scrolls
  sideways.
- **When widths are set.** Fit is computed in the frame where the first result replaces the
  skeleton, so nothing moves after paint. A later result only widens a measure, at its swap. A
  width the user set is never changed.
- **Resizing.** Every column resizes by pointer: the row header (120–600 px), each attribute
  (64–400 px) and each measure (64–600 px, applied to all of that measure's columns, so it survives
  a parameter change). Double-clicking a handle fits the column. A resize of a sticky column stops
  where the sticky block would exceed W − 112 px.
- **A value that still does not fit** is cut with the grid's ellipsis. Its full text stays in the
  cell's tooltip, its accessible text and what Mod+C copies.
- **Sticky.** Headers always stick to the top. The sticky block is the largest prefix of [row
  header, attribute 1, attribute 2, …] whose default widths fit in 40% of W, decided on default
  widths at every observed resize, so a drag never toggles it. Below a 300 px host nothing sticks
  and the row header is 120 px. With two attributes:

  | W | Sticky |
  |---|---|
  | ≥ 1,180 px | the row header and both attributes |
  | 940–1,179 px | the row header and the first attribute |
  | 300–939 px | the row header: 280 px down to 700 px, then 40% of W |
  | < 300 px | nothing |

- **Cells and windowing.** Cells are 30 px tall, and gutters are stable. Body rows window through
  `tmComputeAxisWindow` (spec 0004); columns window past 40.

### 6.3 Styles

| Style | Applies to | Look |
|---|---|---|
| `total` | the grand total row and column | sunken fill, weight 600, a 2 px `--border-strong` rule on its block-start (row) or inline-start (column) edge |
| `subtotal-1` | depth-1 members with children | sunken fill, weight 600, `--text-strong` |
| `subtotal-2` | depth-2 members with children | card fill, weight 600 |
| `leaf` | every other member, deeper parents included | card fill, weight 400, `--text-body` |
| `phantom` | a phantom member | `leaf` in `--text-secondary`, its label italic with "(direct)" |

- **Intersections.** A data cell takes the stronger of its row's and its column's style. Strength
  follows the table's order: `total` is strongest, then `subtotal-1`, `subtotal-2` and `leaf`.
- **Numbers** are tabular, in a `dir="ltr"` isolate. A cell without facts is empty, never "0".
- **Hover and focus.** A hovered or focused value cell shows the hover fill, `--color-primary` text,
  an underline and an accent ring.

### 6.4 Keyboard and ARIA

| Focus | Key or gesture | Effect |
|---|---|---|
| any cell | arrows (Left and Right follow the direction), Home, End, Mod+Home, Mod+End, PageUp, PageDown | move, headers included |
| a value cell | Enter, click; with Mod, `newTab` | `cellActivate` |
| an expandable header | Enter, Space, a twisty or label click | toggle |
| any cell of a row | Alt+ArrowRight, Alt+ArrowLeft | expand, collapse the row (spec 0004's tree-grid keys) |
| a column header | Alt+ArrowDown, Alt+ArrowUp | expand, collapse the column |
| any cell | Mod+C | copy its text |

- **Keys** match through `kbd` (spec 0023 §3.2), so Mod is Cmd on macOS and Ctrl elsewhere.
- **One tab stop.** The pivot is a `treegrid` with a roving tabindex, carrying `aria-readonly`,
  `aria-rowcount` and `aria-colcount`.
- **Headers and rows.** `columnheader` cells carry `aria-colindex`, `aria-colspan` and
  `aria-expanded`. Body rows carry `aria-rowindex`, `aria-level`, `aria-posinset`, `aria-setsize`
  and `aria-expanded`, and start with a `rowheader`.
- **Announcements.** Toggles announce politely, for example "Riyadh expanded, 3 rows".
- **Cells that do not drill.** A cell that `activatable` refuses gets no hover style and no link.
  It still takes focus; its tooltip, and a polite announcement on Enter, read "Only the finest
  cells open their rows in this report."

### 6.5 The Pivot renderer

- **Members** map from `cube.rowMembers` and `cube.columnMembers` (spec 0033 §9.3). Labels:
  `Member` through `tmReportFormat` (navigations from `display`); `None` as "(None)"; `Total` as
  "Total"; `Phantom` as its parent's label. `columnLevelLabels[j]` names a dimension on its first
  level only.
- **Cells** sit in one `Map` keyed by `row × stride + column`. `text` formats `values[measure]` with
  that measure's column.
- **The widest value** of a measure is the one with the most integer digits, a negative one on a
  tie: tabular figures make it the widest. The renderer finds it in one scan of the cells' exact
  strings and formats only that one, so `widest` costs no per-cell formatting.
- **Leaf cells.** Under `drill.leavesOnly`, `activatable` is true only for leaf cells (spec 0033
  §9.4): both members are deepest members of their axis.
- **Totals are the server's.** Every subtotal and total arrives in the cube. Its measures are
  computed over the leaf rows that passed `having` (spec 0033 §8.6), so totals match the cells
  shown. The renderer never computes one.
- **Initial expansion.** Members with children start expanded when their dimension's `expand` is
  true (`ReportColumn.Expand`, default true). Page state keeps the sets and the widths (§3.7).

### 6.6 Member order

Siblings sort as spec 0033 §9.3 orders them:

- **Values** by value, in the dimension's `order`.
- **Navigations and text** by label, through `Intl.Collator(locale, { numeric: true })`.
- **"(None)"** last, and the phantom first.

A language switch re-sorts by this rule with no run. Member ids stay the same, so the expansion
sets survive.

## 7. `tm-chart` and the Chart renderer

`tm-chart` takes a JSON spec and owns the palette, text, direction, legend, tooltip and table view.
ECharts draws the marks on a canvas, behind one internal adapter. The adapter loads only when a
chart plots.

### 7.1 Contract

```ts
// @tellma/core-ui/chart
type TmChartType = 'column' | 'bar' | 'line' | 'area' | 'pie' | 'doughnut' | 'card';
interface TmChartSpec {                                    // JSON; rows index the member lists; labels are text, never markup
  readonly type: TmChartType;
  readonly title: string;                                  // the accessible name; hosts draw visible titles
  readonly dimensions: readonly { readonly label: string; readonly members: readonly { readonly label: string; readonly parts?: readonly string[] }[] }[]; // 0–2, display order; parts: a display name's parts, joined they equal label (§7.4)
  readonly measure: { readonly label: string; readonly format?: { readonly minDecimals?: number; readonly maxDecimals?: number; readonly grouping?: boolean; readonly percent?: boolean } };
  readonly rows: readonly (readonly [d0: number | null, d1: number | null, value: number | null])[]; // one per point; d0, d1 index the members
  readonly options?: TmChartOptions;
}
interface TmChartOptions {                                 // the members of spec 0033 §3.1's chart options
  readonly stack?: 'none' | 'stacked' | 'normalized';      // normalized: shares of the category (§7.2)
  readonly legend?: 'auto' | 'none' | 'top' | 'bottom' | 'start' | 'end'; // logical sides (§7.5)
  readonly markers?: boolean; readonly labels?: boolean;
  readonly palette?: 'categorical' | 'sequential' | 'diverging'; // §7.7
  readonly min?: number; readonly max?: number;            // fix the value axis
  readonly thresholds?: readonly { readonly value: number; readonly tone: 'positive' | 'warning' | 'negative' }[];
}
@Component({ selector: 'tm-chart' })
declare class TmChart {
  readonly spec: InputSignal<TmChartSpec>;
  readonly asTable: ModelSignal<boolean>;                  // the table view (§7.8)
  readonly gridId: InputSignal<string | undefined>;        // the table view's grid id; absent: a per-instance id, so widths are not remembered
  readonly activatable: InputSignal<boolean>;              // default true: points and table rows drill
  readonly pointActivate: OutputEmitterRef<{ readonly members: readonly [number | null, number | null]; readonly newTab: boolean }>;
}
declare const TM_CHART_SPEC_SCHEMA: TmJsonSchema;
declare function tmValidateChartSpec(spec: unknown): readonly { readonly pointer: string; readonly code: string }[]; // schema, §7.2's counts, and 'bidi-control' for a label holding one; for agents and the MCP server
```

**Example —** monthly sales by customer type, stacked:

```json
{ "type": "column", "title": "Monthly sales",
  "dimensions": [ { "label": "Month", "members": [ { "label": "Sep 2026" }, { "label": "Oct 2026" } ] },
                  { "label": "Customer type", "members": [ { "label": "Corporate" }, { "label": "Retail" } ] } ],
  "measure": { "label": "Sales", "format": { "maxDecimals": 0 } },
  "rows": [ [0, 0, 365000], [0, 1, 120000], [1, 0, 298000], [1, 1, null] ], "options": { "stack": "stacked" } }
```

### 7.2 Types and dimensions

| Type | Dimensions | Drawn as | Notes |
|---|---|---|---|
| `column`, `bar` | 1–2 | vertical, horizontal bars over the categories | the second dimension makes series; `stack`; ECharts' `large` mode past 2,000 points |
| `line`, `area` | 1–2 | lines, filled lines; `markers` | `sampling: 'lttb'` past 2,000 points; `stack` on `area` |
| `pie`, `doughnut` | 1 | a disc, a ring (inner radius 55 %), clockwise from 12 o'clock | `labels` shows the member and its share |
| `card` | 0 | the value at 28 px, weight 600, over the measure label; no engine | a threshold the value reaches shows as a `tm-badge` with its value |

- **One measure, one value axis.** Thresholds draw on it as dashed mark lines in their tone,
  labelled with their value. Empty rows render the host's no-data state, never empty axes.
- **Normalized stacks** plot each value as its share of its category's plotted values. This is
  layout only: no computed total is shown. The tooltip gives both the share and the value.

### 7.3 The engine

- **Modules.** The adapter imports `echarts/core` with `BarChart`, `LineChart`, `PieChart`,
  `GridComponent`, `TooltipComponent`, `LegendComponent`, `MarkLineComponent`, `AriaComponent` and
  `CanvasRenderer`. It never uses the umbrella import, a wrapper library or the SVG renderer.
- **One chunk, one adapter.** The engine and the adapter load together on the first non-`card`
  spec, in the `on-demand` group, within spec 0024 §12's budget. The adapter is the only module
  that imports ECharts.
- **The instance** lives from the first render until destroy. Animations are off, and one coalesced
  `ResizeObserver` handles resizing.

### 7.4 Text and formatting

- **Numbers** in tooltips, labels and the table use `tmFormatNumber` (spec 0005 §2.2), with
  `TM_ACTIVE_LOCALE` and the spec's `measure.format`.
- **Axis ticks** on the value axis use `Intl.NumberFormat(locale, { notation: 'compact' })` with
  spec 0024 §7.9's set removed (`tmStripBidiControls`), such as "100K" or "١٠٠ ألف".
- **Canvas text** is the platform's one exception to markup-only bidi (spec 0024 §7.9): canvas
  pixels are never selected, copied or found as text, and no markup reaches them. Inside each run,
  the canvas lays text out from the page's base direction (§7.6). Between runs, one internal
  adapter function, `canvasText(runs, base)`, builds every canvas-drawn string of the ECharts
  option: category axis labels, series and legend names, data and pie labels, mark-line labels and
  tooltip lines.
  - It strips every incoming control (`tmStripBidiControls`), then wraps each run whose direction
    differs from the base as base mark, run mark, text, run mark, base mark. The marks are LRM and
    RLM (U+200E, U+200F) only, which every engine honours; WebKit ignores isolates in canvas, and
    embeddings and overrides are never used. A run's direction is its first strong character, and
    LTR for a number.
  - The runs are a member's `parts`, else its label; a formatted number; and a measure or
    dimension label. A tooltip line is one token, so zrender never splits a line across draws.
  - The marks never reach `TmChartSpec`, which `tmValidateChartSpec` refuses with any control
    (`bidi-control`), the table view (§7.8), the `card` type, the figure's name and description,
    titles, drill events, chips, page context, telemetry or exports.
  - The exception holds while four guards do, each pinned by a unit test of the adapter's option:
    the canvas renderer only (§7.3), the rich-text tooltip (§7.5), `aria.label.enabled: false`, and
    events matched by `dataIndex`, never by `name`.
- **The theme bridge.** On init and on every theme, direction or locale change, the facade reads
  the `chart` tokens (§7.7), `--text-body`, `--text-secondary`, `--divider` and `--font-ui` through
  `getComputedStyle`. No colour is written in code.

### 7.5 Tooltip, legend, Trusted Types

- **Tooltip.** ECharts' own, with `renderMode: 'richText'`, drawn on the canvas. Axis types show it
  per category; `pie` and `doughnut` show it per item.
- **Legend.** ECharts' own, at the logical side that `legend` names, with decal swatches. `auto`
  puts it at the bottom for two or more series and shows none for one.
- **Trusted Types.** The canvas renderer and the rich-text tooltip assign no `innerHTML`, so the
  production CSP holds with no policy. ECharts' generated aria label is off; §7.8 names the chart.

### 7.6 RTL

- **The plot does not mirror:** the value axis stays at the physical left, categories run left to
  right (`bar`: top to bottom) and pies run clockwise, in a `dir="ltr"` container.
- **Text follows the page:** the legend's side and item order, the tooltip, the table and the title.
  The canvas element takes the page's `direction` through CSS, so every canvas string starts from
  the page's base direction; ECharts never sets the context's `direction`. A direction change
  redraws, through the theme bridge (§7.4).

### 7.7 Palette and tokens

The `chart` token group joins the default preset the way spec 0023 §9's groups do. It sits beside
the `pivot` group, which holds §6.3's fills. Its series and its ramp read the semantic
`categorical` and `sequential` groups (spec 0010 §B.8), which switch with the scheme.

| Token | Value |
|---|---|
| `series1`–`series8` | `categorical.1.mark` … `categorical.8.mark`: teal, orange, violet, amber, magenta, green, blue, red |
| `sequential1`–`sequential7` | `sequential.1` … `sequential.7`, step 1 the faintest |
| `divergingNegative`, `divergingPositive` | `series2`, `series1` |
| `grid`, `axis`, `label` | `--divider`, `--text-secondary`, `--text-body` |

- **Re-branding.** A distribution re-brands the series and the ramp in the `categorical` and
  `sequential` groups. It re-brands the rest of the `chart` and `pivot` groups in
  `@layer tm.theme`. Series and ramp tokens keep a fixed order.
- **Validated.** A token unit test, run for any preset, checks the series colours in this order
  against `surface.card` in both schemes: chroma ≥ 0.1, adjacent colour-blind ΔE ≥ 8,
  normal-vision ΔE ≥ 15, contrast ≥ 3:1.
- **Palettes.** `categorical` (the default) gives series their slots in member order, so a colour
  follows its member; past eight series, slots repeat with a new decal per cycle. `sequential`
  spreads the ramp over the series. `diverging` colours one series' marks by sign.
- **Decals** (ECharts `aria.decal`) apply whenever colour alone separates series: two or more series
  in `column`, `bar` or `area`, and every `pie` or `doughnut`. They always apply under
  `forced-colors` or `prefers-contrast: more`. Lines differ by marker shape. Status tones mark
  thresholds only.

### 7.8 Accessibility

- **The table view is the keyboard path.** `asTable` swaps the plot for a readonly `tm-grid` in the
  same box, with one row per point: its members and formatted measure. Activating a row drills
  (Enter; with Mod, a new tab). The toggle sits in the toolbar (§10.2) and the card menu (§3.6).
- **The plot** is a `figure` holding a canvas with `role="img"`, out of the tab order. Its name is
  "{title}, {type} chart". Its description is "{n} points, from {min} to {max}. Use Show as table
  to read every value." Pointer users hover for the tooltip and click a point to drill.
- **Contrast.** Marks meet 3:1 (WCAG 1.4.11). Text uses text tokens, never series colours.

### 7.9 The Chart renderer

- **The spec** takes `type` and `options` from `result.chart`, dimensions from the dimension
  columns, and rows from the ordinals. Members keep their first-appearance order; labels go through
  `tmReportFormat`, and a navigation member's `parts` come from `TmDisplayNames.parts`. A plot
  needs numbers, so measure values convert with `TmDecimals.toNumber` (spec 0024 §10.1), exact to
  15 significant digits.
- **The table view's grid id** is the host's grid id (§5.1) plus `.table`.
- **Drill.** `pointActivate` and an activated table row drill through their members and the
  measure's condition (§8.1); a `card` drills on click with the measure's condition only.
- **A chart type the facade does not draw** (only a coded report can return one) renders as the
  table, with Show as chart disabled.

## 8. Drill-through

A summary row, a pivot cell or a chart point opens the list of the rows behind it. A list row opens
its own record instead (§5.2). The host builds the list's URL from the result alone.

### 8.1 From coordinate to filter

1. **The coordinate.** A summary row: every key column. A pivot cell: for each dimension, the
   deepest member among the cell's row and column members and their ancestors. A chart point: each
   dimension's member. Totals add nothing (spec 0033 §9.4).
2. **The measure.** The clicked measure adds its condition when it has one: the pivot cell's
   `measure`, or the chart's only measure. A summary row adds the `or` of its measures'
   conditions, and none when any measure has none.
3. **One condition per member and measure,** shaped as spec 0033 §9.4's table gives it. The host
   builds it with `tmQx` and `tmQx.literal` (spec 0029 §8.2); an expression member and a measure
   use `tmQx.raw(drill.terms[i])`. Nothing is concatenated.
4. **The filter** is `tmQxPrint(tmQx.and(tmQx.raw(drill.filter), …conditions))`, without the raw
   node when `drill.filter` is null.
5. **The URL** is `TmUrls.list(drill.segment, { filter, cols })` (spec 0024 §9.4):
   `/{t}/list/{drill.segment}#d=<payload>`, where the payload is the base64url, without padding,
   of the UTF-8 JSON `{ "filter": …, "cols": […] }` (spec 0033 §9.4). That alphabet survives the
   router's fragment decoding. The source report and the page to return to travel in navigation
   state (§8.2).

A result without `drill` offers no drill affordance. When there is one, every drill lists exactly
the rows its number aggregates (spec 0033 §9.4). Under `drill.leavesOnly` (`having`, or a summary's
`top`), only leaf cells, summary rows and chart points drill. A pivot's other cells and a summary's
totals row do not (§5.3, §6.4).

**Example —** Acme's `acme.sales-by-center` pivot runs with `Year` defaulted to 2026. Activating the
cell Riyadh × Q3, where Riyadh has no children in the cube (spec 0033 §8.6), gives the filter
`(year(Invoice.PostingDate) = 2026) and CenterId = 11 and (quarter(Invoice.PostingDate)) = 3`.
The chips read "Center: Riyadh · Quarter: 3 · Year: 2026".

### 8.2 The drill event

```ts
// @tellma/core/reports
interface TmReportDrillEvent {
  readonly url: string; readonly segment: string; readonly filter: string | null; readonly columns: readonly string[];
  readonly chips: readonly { readonly label: string; readonly value: string }[]; // members, "Measure" when its condition applies, then arguments, as displayed
  readonly newTab: boolean; preventDefault(): void; readonly defaultPrevented: boolean; // preventDefault: the host does not navigate
}
```

- **The default action** in the same tab is `router.navigateByUrl(url, { state: { tmDrill: { from,
  fromUrl, fromTitle, chips } } })`, kept in `history.state` across reloads. For `newTab` it is
  `window.open(url, '_blank', 'noopener')`. `assistant` mode only emits (§3.6).
- **`tmDrill`.** `from` is the report's id or key, or null for an inline report. `fromUrl` and
  `fromTitle` name the page the drill returns to:
  - in `page` mode, the report's canonical URL with its `p.*`, and the report's title;
  - in `card` and `widget` mode, the embedding page's: `Router.url` and the title of its routed
    page context (spec 0026 §9.2), such as the dashboard or the customer.
- **Too long.** The host refuses a filter longer than `limits.maxFilterLength` (spec 0029 §2.2), the
  pipeline's input limit that the list's `query` would refuse. Instead, the notice strip (§3.3)
  says "This selection is too detailed to open as a list. Narrow the report first."

### 8.3 The drill-through list

`/{t}/list/{segment}` is spec 0030's search host in list mode (spec 0030 §17), which reads `d` from
the raw `location.hash`. The payload's `filter` is a fixed leaf, and its `cols` name the columns.
The source page comes from `history.state.tmDrill` (§8.2). This section fixes what list mode
adds.

| Part | Content |
|---|---|
| route | `TmEntities.bySegment`, child segments included (spec 0029 §2.2); an unknown segment shows the tenant not-found page |
| row 1 | the crumb: `fromTitle` (§8.4), linking to `fromUrl`; the `h1`: the entity's plural label |
| row 2 | the chips (§8.4), "Open in {plural}" and Export (below) |
| invalid filter | a malformed `d`, or `400 query-invalid` on the fixed leaf: `circle-alert`, "This link's filter is not valid", without Edit filter |

- **Row 2's actions.** "Open in {plural}" shows for a top-level root with a search page and links to
  `TmUrls.search(entity, { filter, cols })`. Export follows spec 0030 §13.2 and shows when
  `allows(entity, 'export')`. A child root has it when its owner stack exports (spec 0033 §10).
- **Child roots.** The select is spec 0030 §5.1's, plus the owner's key (`Invoice.Id`), and for a
  grandchild also the top-level owner's. A top-level row opens its details. A child row opens its
  owner with `?child={collection}:{id}` (spec 0031 §14). A grandchild row opens its top-level owner
  on the intermediate child.
- **Back.** The crumb calls `history.back()` when the previous entry is the source page, that is
  when `history.state.tmDrill` is set and the tab can go back. The page then restores from its page
  state (§3.7, spec 0035 §11.4) with its arguments, active row and focused cell. Otherwise the
  crumb navigates to `fromUrl`. Pins and recents follow spec 0030 §17; a pin keeps the `#d=`
  payload (spec 0027 §8.2).

### 8.4 Chips and the crumb

Titles speak for the app and filters speak for the link: the crumb and the `h1` never come from the
URL, and a chip's value comes from the URL only through the server's parse of the very filter the
list runs, never from a member of its own.

- **From a report host,** in any mode, the chips and `fromTitle` come from the navigation state
  (§8.2), read from `history.state.tmDrill`.
- **From a bare link** (a new tab, a copied URL), the list makes one `queryex/discover` call (spec
  0029 §9) with the entity as root, `filters: [filter]` and `conjuncts: true`. Each conjunct
  becomes a chip:
  - *Label.* A path's slash label (spec 0029 §11.4), or an expression's text.
  - *Value.* A foreign key shows its target's display name through `TmDisplayNames`, under the
    viewer's own `Read`, or "#{id}". `descendantOf` reads "{name} and below". Dates and numbers
    format in the viewer's calendar and locale, and a null value reads "(None)". An operator other
    than `=` leads the value, as in "≥ 1 Jan 2026"; an `is null` arrives as `=` with a null value.
  - *Verbatim text,* a string literal or an expression, is stripped of bidi controls
    (`tmStripBidiControls`, spec 0024 §7.9) and cut to 80 characters with an ellipsis.
  - *Leftovers.* A path no conjunct covers, inside an `or`, a `not` or an `Exists`, gets a
    label-only chip after "Filtered by".

  The crumb reads "Reports" and links to `/{t}/reports`.
- **Rendering.** A chip projects its `label` and `value`, each in a `<bdi>` (spec 0023 §4.3); its
  name is the plain "{label}: {value}".

## 9. Export to Excel

Export asks the server for spec 0033 §12's workbook of the shown result. Lists and summaries export
flat, a pivot fully expanded, and a chart as its data; image columns are left out.

1. The body is `{ id | inline | key, arguments, dashboardId }`, with the shown result's effective
   arguments. A list or summary whose shown count is capped (`table.countCapped`, spec 0033 §9.2)
   goes straight to step 3, as the search page does (spec 0030 §13.2), unless it is a coded report
   whose `export` is `Custom`, which has no background path (spec 0033 §12.1). Otherwise the host
   calls `TmApi.download('reports/export', body, { signal, onProgress })`. The Contracts chunk
   marks the route long (spec 0024 §3.4).
2. A sticky "Exporting…" toast shows progress and Cancel (spec 0028 §8.1); leaving also aborts. The
   file saves through an object URL and an `<a download>`, under the server's file name.
3. For a capped count, or on `413 limit-exceeded` whose `limit` is `Excel.Export.RowLimitExceeded`
   (the fallback), the host makes one `TmApi.write('reports/export/start', body)` call, then shows
   "Started in the background." with Track (spec 0028 §8.1). A coded report takes this step only
   when its `export` is `Platform`. A pivot or a chart never takes this step: it always fits
   `export` (spec 0033 §12.1).
4. Any other failure goes to `TmErrorPresenter.present(e, { kind: 'action' })`: `413
   limit-exceeded` whose `limit` is `Report.ExportTooWide`, and a coded `Custom` export's `413`,
   which names no `export/start` (spec 0033 §12.1).

Every mode but `widget` offers Export; there, spec 0035's tile calls `export()`. A coded report
offers it unless its `export` is `None`, and the studio offers it for its inline report. The
drill-through list exports as a search page does.

## 10. The report page and its dialogs

A stored report's page is the details host with `TmReportPage` as its `page` hatch and the compact
header (spec 0031 §3.7, §5.4). The host keeps the header, record actions and page scope, and builds
no record pager. A coded report's page draws the same header on its own `tm-page-header`, with the
same `tm-page-action-bar` (spec 0031 §5.4).

### 10.1 Header and body

- **Row 1,** in order: on a stored report, the crumb "Reports", linking to `/{t}/reports`; the
  `h1`, the title in the active slot; the "Inactive" badge on an inactive report; the "About this
  report" button; and, at the row's end (`[tmPageHeaderEnd]`), the action bar (§10.2). A coded page
  has no crumb: its launcher section is its parent, and the reports page does not list it.
- **"About this report"** is a 16 px `circle-help` icon button opening a `tm-popover` (spec 0005
  §11), so touch and keyboard users reach it. It holds the kind, the description and, for a report
  with no owner, "Standard". On a coded page it holds "Built-in", says that access is granted
  through roles, names the securable (`TmPermissions.label`), and, for a caller who may read roles,
  links "Who has this" to the access page on that securable,
  `/{t}/access?resource={resource}&action={action}` (spec 0032 §4.9). It shows only when it has
  something to say.
- **The band** (`[tmPageHeaderBand]`, spec 0023 §7.2) holds the parameter bar (§4.2) when the report
  has parameters, so the bar stays in view while the output scrolls.
- **Body:** the output with its notice strip (§3.3), and the footer (§5.1). The details host's
  status strip anchors under the whole header (spec 0031 §5.4).
- **The host's inputs.** The hatch reads the record through `injectTmDetails()` and parses its
  `definitionJson`. It passes the document's parameters to the host as `declarations`, and the
  document itself as `document`.

### 10.2 The action bar

`tm-page-action-bar` lays out the page scope's actions (spec 0031 §5.4, spec 0026 §9.4). The page
registers its own actions; on a stored report, the details host adds the record's.

| Control | When | Action id | Group, order |
|---|---|---|---|
| Share (§10.4) | a stored report whose `permissions` extra leaves `action:share` or `action:publish` open (spec 0015 §5.3) | `report.share` | primary, 10: labelled from `md`, an icon below |
| Refresh | always | `report.refresh`; on a stored report it replaces the host's `details.refresh` (`refresh: 'page'`, spec 0031 §3.7) | primary: 25 on a stored report, the host's slot (spec 0031 §5.4); 20 on a coded page |
| Export (§9) | §9's conditions | `report.export` | primary, 30 |
| Show as table or chart | a chart | `report.toggle-table` | primary, 40, `aria-pressed` |
| Edit, to `/{t}/reports/{id}/studio` | a stored report whose `save` is not refused | `report.edit` | menu, first |
| Expand all, Collapse all | a pivot | `report.expand-all`, `report.collapse-all` | menu |
| Copy link: the canonical URL with its `p.*`, announced "Link copied" | always | `report.copy-link` | menu |

- **From the host** (spec 0031 §10.1), in the menu on a stored report: New; Clone, which is the
  report's Save as (§2.3); Activate or Deactivate; Change owner (§10.3); and Delete.
- **Refresh.** On a stored report, one click calls `injectTmDetails().refresh()`, which re-reads
  the report, and runs it under a new generation. A coded page only runs it.
- **No in-place edit.** The declaration's `edit: 'page'` (spec 0031 §3.7) removes the host's
  `details.edit`, so Edit opens the studio.
- **A read-only or frozen tenant** disables the write actions with spec 0029 §7's reason; Refresh,
  Export and Copy link stay.
- **A coded report** has no Share: its access comes from roles (§10.1).
- **Pins** are the shell's (spec 0027 §8.2) and keep the `p.*` parameters.

### 10.3 Delete and transfer

- **Delete** is the host's (spec 0031 §10.2): its confirmation, then the reports page and the
  polite announcement "Report deleted." (spec 0028 §8.1). When a dashboard shows the report, the
  server answers `422 Report.InUse`; this opens the 400 dialog, naming up to five dashboards and
  counting the others ("and 3 other dashboards"). Deactivating is the alternative.
- **Change owner** is the `transfer` action's `TmTransferDialog` (`@tellma/core/sharing`), a
  `TmModal` of size `sm`. It holds one `tm-entity-field entity="core.User"` of active individual
  members. It runs as `{ ownerId }` through `injectTmActionDialog().run` (spec 0031 §10.4). The
  reports page runs it in bulk (§11).

### 10.4 The share dialog

`TmShareDialog` (`@tellma/core/sharing`) is the `share` dialog of every shareable, reports and
dashboards included: a `TmModal` of size `md`, full screen below `sm`. On open it reads two things
in parallel: `{segment}/get { id, include: [...descriptor.extras, 'shares'] }`, every extra the
entity declares as the details host reads them (spec 0031 §3.3) plus `shares`, and, when
`action:share` is not refused, `{segment}/audience { id }` (spec 0033 §5.2, §6.1).

- **Sharing tab:**
  - *People* (when `action:share` is not refused): the owner, marked "Owner"; one row per user
    grant of the `shares` extra, with avatar, name, a Can view or Can edit select and a button
    named "Remove {name}"; and Add people (`tm-entity-multi-field entity="core.User"`), which
    offers active individual members only, as §10.3's field does. A grantee who was deactivated is
    marked "Inactive".
  - *Roles* (when `action:publish` is not refused): one row per role grant; Add roles; and the
    switch "Everyone in {tenant}", which reflects the Everyone grant, the share row that names
    neither a user nor a role, and sets `everyone` (view only).
  - *Warnings* come from each grant's `cannotRead` counts, one per data securable of the audience
    query, and never block. A report has one, its root. For example: "Omar can't read Sales invoice
    lines and will see an empty report." or "2 of 9 members of Sales can't read Sales invoice
    lines." A dashboard has one per securable in spec 0035 §7.1's order, and each warning names the
    tiles behind its index in that section's texts, from that read's `view` extra and the
    `codedReports` metadata. When the server lists members, a warning carries a "Show who" link
    to the next tab with the filter chip on.
  - *Avatars*, here and in the next tab, pass the record key `core.User:{id}` as `colorKey` (spec
    0023 §4.2).
- **"Who can see this" tab.** It shows the audience total, such as "431 people, 12 of them can't read
  Sales invoice lines" from the audience's own `cannotRead`. When the server lists members, which
  it does only to a caller who may read roles, a searchable list follows:
  - avatar, name, "Service account" for a service user, highest access and reasons, such as "Shared
    by Farah on 12 Oct", "Member of Sales (can edit)", "Everyone", "Administrator role permission"
    or, for a `Through` reason, "Through the dashboard Monthly KPIs" ("Through the {singular label}
    {title}");
  - for a member whose `cannotRead` names the data, the badge "Can't read {plural}", in text, with
    the tooltip "They'll see an empty report";
  - when any member has the badge, a filter chip "Can't read the data ({n})" above the list.

  A capped list says "Showing 2,000 of 2,431 people".
- **Save** applies the staged changes through `TmEntityActions.run`, then re-reads the audience:
  1. one `share { set, remove }`, when user grants changed;
  2. one `publish { set, remove, everyone }`, when roles or `everyone` changed (spec 0033 §5.4).

  A caller holding `Publish` but not `Share` therefore publishes with no `share` call. If the
  `publish` fails, the dialog stays open with the `share` applied and the error shown.
- **Cancel** with staged changes asks "Discard changes?".
- **Close** returns the last `entities` outcome, which the report page's host adopts (spec 0031
  §10.4), and announces "Sharing updated." politely (spec 0028 §8.1).

## 11. The reports page

`/{t}/reports` is the launcher's one Reports entry (spec 0027 §3.2). It is the `core.Report` search
page, with `TmReportsPage` as its results region (spec 0030 §2.5), and it lists stored reports
only. Everything else is the search host's: it reads `reports/query`.

- **Tabs.** All reports is the default and is absent from the URL. My reports is `tab=mine`, which
  sends `OwnerId = me()`, and Shared with me is `tab=shared`, which sends the named filter
  `shared-with-me` (spec 0033 §5.5, spec 0030 §2.4). A tab change is a query change: back to page
  1, with `replaceUrl`, keeping the selection.
- **Rows.** A `tm-grid` shows `ctx.rows()`, bound as spec 0030 §7.1 binds the page's grid, without
  the row-end column: the columns below are fixed.
- **Columns:** Title (the kind's icon `list`, `sigma`, `grid-3x3` or `chart-column`, a link, and the
  "Standard" badge), Kind, Root (plural label), Owner (avatar and name) and Modified
  (`tm-relative-time`). The Owner avatar's `colorKey` is the record key (spec 0029 §12.2). A
  standard report shows "—" for its owner.
- **Built-in reports are in the launcher.** The empty states and the footer carry one static line,
  "Built-in reports are in the launcher", whose link calls `TmLauncher.show('link')` (spec 0027
  §9) with no query.
- **Empty states.** All: "No reports yet". Mine: "You haven't created a report yet", with New
  report. Shared: "Nothing has been shared with you yet". No matches: spec 0030 §5.3.
- **Toolbar.** New report is the host's Create (`/{t}/reports/new`), open to every member (spec 0033
  §5.2). Bulk actions derive from the entity actions (spec 0030 §12): Activate, Deactivate, Delete
  and Change owner. The stack has no export, so neither does the page.

## 12. The report studio

The studio edits one stored report in a dark panel beside a live preview. One component,
`TmReportStudio`, is the details host's body in both modes, so every save goes through the host.

### 12.1 Routes and hosting

In both modes the details host owns the header, Save, Cancel, drafts, the leave guard, and the
conflict and deleted-record dialogs (spec 0031 §5, §8, §9, §11). The studio writes the host's model
through `injectTmDetails().update()` on every change: titles, descriptions, `code`, `root`, `kind`
and `definitionJson`.

| Aspect | Create: `/{t}/reports/new`, `?root={entity}`, `?clone={id}` | Edit: `/{t}/reports/{id}/studio` |
|---|---|---|
| host | the details host's create mode, with the studio as the `create` hatch (spec 0031 §3.7) | §2.3's `tmPage`: `TmReportStudioPage` renders `tm-details-page` (spec 0031 §3.1) with `TmReportStudio` as its layout |
| source | the entity source | `tmEntitySource` with its `draftKey` replaced by `TmDrafts.key(t, 'core.Report/studio', id)`, a key apart from the report page's |
| mode | create | edit: the studio calls `injectTmDetails().edit()` whenever the host is in view mode, `ready` and `can.edit`, on entry and after each save |
| after Save | the edit studio, through `createdUrl` with `replaceUrl` (§2.3, spec 0031 §8.3) | the studio stays, back in edit mode |
| crumb | "Reports" | the report's title, linking to `/{t}/reports/{id}` |

- **Query parameters.** `?root=` preselects the root; without it, the Root entity select opens
  focused. `?clone=` arrives through the host's Clone (§2.3), and the studio prefixes each title
  slot with "Copy of".
- **The first save keeps the workspace.** Before the host navigates, it remembers its snapshot under
  the edit route's page-state key, so the edit studio sends no `get` (spec 0031 §13). The create
  studio puts its own state, `{ splitter, rails, autoPreview, kindTerms, parameterCards, openItem,
  preview }`, into `TmReportStudioHandoff`, a root service of `/report-studio` that holds one entry
  keyed by the new id. The edit studio takes the entry once on mount, passes `preview` to its
  preview host as `held`, and starts no run. A clone lands the same way.
- **Read-only.** When the `permissions` extra refuses `save`, as on a standard report, the host
  stays in view mode. The panel is inert, and the notice "You can view this report. Save a copy to
  change it." shows. The host's Clone, which is the report's Save as (§2.3), makes the copy.

### 12.2 Layout

| Region | Content |
|---|---|
| top row | below `md`, a Definition / Preview `tm-segmented`; the problems badge (§12.7) |
| panel | a `data-theme="dark"` scope at the inline start: the fields column and the definition column |
| splitter | `tm-splitter` (spec 0023 §7.3): 560 px by default at `xl`, 500 px below; `min` 420, `maxFraction` 0.66, `otherMin` 340; the size lasts as long as the page, and across a create's first save (§12.1) |
| preview | `--surface-subtle`: a `page`-mode host over an unnamed `{ inline }`, titled by the Title field in the active slot, else slot 1, with its own bar, a toolbar (Refresh, the Auto-preview switch, Export) and the output |

- **Fields column.** It is 200 px wide at `xl` and 180 px below, and collapses to a 36 px "Fields"
  rail. It holds the Root entity `tm-select`, which lists readable stacks and child query surfaces
  by plural label ("Sales invoice lines · Sales invoices"), then `tm-path-picker mode="fields"
  draggable dateParts` (spec 0029 §11), so each date field opens into its parts, such as Month or
  Day of week. Changing the root while terms exist asks for confirmation, then
  clears paths and expressions.
- **Definition column.** It also collapses to a rail. It holds the Report type `tm-segmented`, then
  §12.3's sections. Below `md`, the top row's toggle swaps the panel and the preview.
- **Dark panel.** In both app themes, the dark scope's controls use their dark tokens, plus
  `--studio-panel-bg` (`ink-900`) and `--studio-fields-bg`.

### 12.3 Sections

| Kind | Sections |
|---|---|
| all | General (Title and Description per slot, Code under "More"), Parameters (§12.4), Filter |
| List | Columns, Order by, Top |
| Summary | Columns (measures tagged "Aggregate"), Having, Order by, Top, "Show totals row" |
| Pivot | Rows (with Attributes under a single row dimension), Columns, Measures, Having, "Show rows total", "Show columns total" |
| Chart | Chart type, Dimensions (0–2), Measure (one slot), Options, Having, Top, Order by |
| Summary, Pivot, Chart | Drill-through columns (`tm-path-picker mode="columns"`; empty means the root's default) |

- **The default title.** An empty Title field shows the default title (spec 0033 §2.1), "Sales
  invoice lines – chart", in the UI language as its placeholder, and Save may leave it empty.
- **"Show totals row"** stays visible but is disabled while the summary has no measure:
  `aria-disabled`, with the reason "Add a measure to show totals" (spec 0033 §3.4 rule 6).
- **Chart sections.** Chart type is a grouped `tm-select` of §7.2's types, in the groups
  Comparison, Trend, Part to whole and Other. Options holds legend, stacking, markers, labels,
  palette, min, max and thresholds.
- **Term lists** are `tm-reorder-list` with `accepts: ['tm.path']`. An item shows its grip, label,
  mono expression (`dir="ltr"`) and tags, such as Tree, Auto-expand, Aggregate, Descending or
  "0 decimals".
- **The item editor.** Activating an item expands its editor in place, one at a time. It holds
  Expression, Label per slot, and Display and Format (spec 0033 §3.2's options, with
  `format.calendar` for month, quarter, year and month name). Dimensions add Order, Auto-expand,
  Tree and Attributes. A tree always shows its full depth (spec 0033 §8.6).
- **Defaults show as placeholders.** An unset Display or Format control shows the default display
  as its placeholder, read from the preview's column (spec 0033 §9.1). Choosing a value stores it.
- **Labels.** Display "Labels" (`enum`) offers a named enum (`format.enum`) on a `String` term, or a
  Labels table (`format.choices`): Value and Label per slot, one row per choice. "Fill from preview"
  adds a row for each distinct value of the held page that has none, and stops at 100 choices with
  a notice (spec 0033 §3.4 rule 11).
- **Order by** items are `{ term }` or `{ expr }`, Ascending or Descending. **Top** is a
  `tmNumber`, hinted with spec 0033 §3.4 rule 8's range.

### 12.4 Parameters: live inference

- **Cards follow the text.** On each keystroke, `tmQxParameters` (spec 0029 §8.1) names every
  `@Key`, and each gets one card, in order of appearance. Types, conflicts and the compared entity
  merge in from the last check (spec 0029 §10.2). A vanished name keeps its settings for the
  session, unsaved.
- **A card** shows:
  - `@Key`, mono, weight 600;
  - a type tag, or a warning-toned `tm-select` when inference is unconstrained or conflicting;
  - Required (`tm-switch`), Label per slot, and Default (`tm-expression-editor clause="constant"`);
  - Display and Format, with spec 0033 §4.6's options for the type;
  - "Compared with Customer" when the compared entity is known, else an Entity select (spec 0033
    §4.2 step 5).
- **Pre-filled displays.** The server infers none (spec 0033 §4.6), so the card proposes them as
  stored values: `integer` without grouping when the Default's outermost call is `year`, `quarter`,
  `day` or `week`, and `enum` with the property's named enum when the parameter is compared with an
  enum property. The author may change either.
- **Confirmed types** feed the Queryex drafts' `parameters` (spec 0029 §10.1).

**Example —** Farah types the Filter `Invoice.PostingDate <= @AsOf and Center.Id = @Center`. Two
cards appear at once, `@AsOf` then `@Center`. After the check, `@AsOf` carries the type tag `Date`
and `@Center` reads "Compared with Center".

### 12.5 Drops and "Add to…"

| Target | Takes | Becomes |
|---|---|---|
| List and Summary columns, Rows, Columns, Dimensions | a `tm.path`, at the drop index | `{ path: node.key }`; a navigation shows its display name; a date part: `{ expr: node.expr }`, displayed by spec 0033 §3.2's default |
| Measures, the chart's Measure, Summary's "Drop to add its sum" end zone | any field but a date part (`accept`) | a numeric field: `{ expr: 'sum(<path>)' }`; any other: `{ expr: 'count(<foreign-key path>)' }` |
| Attributes | `accept`: paths under the dimension's path | `{ path }` |
| Filter, Having, an item's expression | the editor's native drop at the caret (spec 0029 §10.4) | the node's Queryex text |

- **Hints.** While a drag is current (`TmDragDrop.current`), accepting targets show "Drop to add".
  The hovered one tints `rgba(76,160,182,.07)` with a 2 px inline-start accent. Other targets
  refuse the drop.
- **The non-drag path.** Enter or double-click on a field opens an "Add to" `tm-menu` of the kind's
  targets, such as "Rows", "Measures (sum)" or "Filter". Filter and Having insert through
  `insertAtCaret`.

### 12.6 Kind switching

Switching the report type keeps General, Parameters, Filter and Drill-through columns. Each type
keeps its own terms for the session, and a type opened for the first time starts empty. Save writes
only the active type's block (spec 0033 §3.4 rule 2).

### 12.7 Validation feedback

1. **On every keystroke,** the studio runs the lexer (spec 0029 §8.1) and the structural rules
   mirrored from spec 0033: §3.4's rule 3 counts and rules 7–10, and §4.5's keys and limit of 20
   parameters.
2. **After 250 ms,** one shared `TmQueryexDraft` checks the expressions. It is built over the root,
   with `aggregate` for every kind but List, and with the confirmed parameter types. It holds:
   - Filter and Having;
   - the order expressions, as `orderBy`;
   - the pivot's measures, and a summary's measures when "Show totals row" is on, joined by ", "
     as `measures` (spec 0029 §10.2), so a measure that C# cannot roll up shows QX4010 (spec 0010
     §F.14, §F.15);
   - every other term, joined by ", ", as `select`.

   The studio maps each span back to its term by the offsets it joined at. The open item's editor
   checks on its own draft, and path terms resolve locally.
3. **After 600 ms,** if no problem is left and Auto-preview is on, the preview runs
   `reports/run { inline }`, unnamed. The server validates the whole document (spec 0033 §8.3). A
   `422` at `inline.document` maps by its `pointer`: `/pivot/rows/0` to that item; `/filter` with
   `start` and `length` to the span; `/parameters/1/default` or `/parameters/1/format` to that
   card; an unknown pointer to General.

- **The problems list.** An error `tm-badge` in the top row, such as "2 problems", opens a list
  whose items focus their source. Sections show their counts. F8 cycles within an editor (spec 0029
  §10.5).
- **Save always sends.** The server is the validator, and its `422` lands in the same list.

### 12.8 The preview

- **Query changes** re-run the preview as §12.7's step 3 describes, aborting the previous run. They
  are edits to terms, filter, having, order, top, kind, totals, tree, attributes and parameter
  types.
- **Presentation changes** re-render the held result through `overlay`, with no request. They are
  edits to labels, display, format, expansion, chart options, and a chart type with the same
  dimension count.
- **Auto-preview off.** Only Refresh runs, and the notice strip says "Preview is out of date".

```ts
// @tellma/core/reports
interface TmReportOverlay {
  readonly terms: readonly Partial<Pick<TmReportDimension, 'label' | 'label2' | 'label3' | 'labelKey' | 'display' | 'format' | 'expand'>>[]; // one entry per term, in document order
  readonly chart?: { readonly type: TmChartType; readonly options?: TmChartOptions };
}
```

### 12.9 Save, Cancel, Share

Both modes save through the details host (spec 0031 §8), so `reports/save` has one caller.

- **Save and Cancel** are the host's `details.save` and `details.cancel`. Cancel on the edit page
  discards the changes, and the studio re-enters edit mode on the saved report.
- **"Report saved."** A create's save lands on the edit studio (§12.1), which announces "Report
  saved." politely once it has taken the handoff (spec 0028 §8.1).
- **Errors.** The studio registers its fields with `registerField` (spec 0031 §4.1): General's
  controls by column, and `DefinitionJson`. It maps `DefinitionJson`'s `422` errors by their
  `pointer` argument into §12.7's list. `409`, `404` and other failures are the host's (spec 0031
  §8.6, §9).
- **Share** (edit page only) opens §10.4's dialog through
  `TmEntityActions.dialog('core.Report', 'share', [id])`. Shares never stamp the report and a save
  never writes them (spec 0033 §5.4), so sharing while editing meets no concurrency check.

## 13. Page context and page actions

The report pages and the studio publish, as their page context, state they already hold (spec 0026
§9.3). Embedded hosts publish nothing: a card belongs to its details page, a widget to its
dashboard.

| Kind | `state` | `visible` | `errors` |
|---|---|---|---|
| `report` | `{ report: id \| key, parameters: the effective arguments, reportKind, asTable }` | list and summary rows as displayed (≤ 200); a pivot's visible rows by leaf columns (≤ 200 × 50); a chart's table rows | argument and output errors, with trace ids |
| `studio` | `{ report: id \| null, root, reportKind, dirty, problems }`; `problems` is a count | the preview's, as for `report` | every §12.7 problem, with its pointer as `path` |
| `search` (the drill list) | spec 0030's, plus `from` from `history.state.tmDrill` (null from a bare link) | spec 0030's | spec 0030's |

- **Who registers.** The report page's hatch registers `report`. It replaces the host's `details`
  context and lists the host's actions after its own (spec 0026 §9.2). The studio registers
  `studio` the same way in both modes, with `report: null` in create mode. The coded page registers
  `report`.
- **Actions.** The report pages register §10.2's `report.*`. The studio registers `studio.preview`
  in both modes and `studio.share` on the edit page, both `primary`. Save and Cancel stay the
  host's (§12.9).
- **Only visible controls are actions.** Setting arguments or proposing a document needs actions
  with parameters, which the assistant spec adds without changing these contexts.

## 14. Testing

Every suite runs on PR.

- **`tm-pivot` unit** (vitest browser mode): compact layout, spans, subtotal and total columns, the
  styles and their intersections, flattening, both windows, §6.4's keys, ARIA, the phantom suffix,
  and `text` only for rendered cells; a measure's default width from `widest`, widened only by a
  later result and never over a user's width; a resize applied to every column of its measure, and
  double-click fit; a cut value's full text in the tooltip, the accessible name and the clipboard;
  an `activatable` refusal without hover, with its tooltip and announcement.
- **`tm-chart` unit:** spec validation, `bidi-control` included; the adapter's input per type and
  option, engine-free; palette slots, decals and normalized shares; `canvasText`'s marks, only LRM
  and RLM, per run and `parts`; the four guards of §7.4; Arabic-Indic ticks; the table view, its
  `gridId` and its row activation; no HTML tooltip or legend configured.
- **Host harness** (fake `TmApi`, fake clock):
  - `get` and `run` together with and without `p.*`, the link's text sent raw, `422
    Report.ArgumentInvalid` marking its field, and the URL rewritten from the echo; `422` →
    waiting;
  - a coded or inline report waiting with no request when a required parameter has no value and no
    default;
  - one run per commit, the superseded ones aborted;
  - `take` 200 for a list and 1,000 for a summary, and no `long` option on runs and exports;
  - `priority: 'low'` on every `widget`-mode run and never on its export; `run({ background:
    false })` sending no `Tellma-Activity`; `drillable` false drilling nothing;
  - the reloading bar from the commit's frame, held across a superseding run and at least 250 ms
    on screen;
  - §3.4's table, `held`, `stale` and the `forbidden` clear;
  - the calculation error from a pivot cell, an outer subtotal, a summary's totals row and an SQL
    row (empty pointer, no cell, the SQL hint), and on a stored report's first run labelled
    from `document`;
  - a named inline report's `422` at `inline.document` as needs fixing without Edit, a bound `null`
    in `card` mode in create mode as "Available after saving" with no run, and on a saved record
    running as an omission;
  - page-state restore, widths included, the `modifiedAt` discard, and the run after 5 minutes or
    on an `isStale` entry;
  - `card` and `widget` write no URL or preference; `widget` sends no `get`; a drill from either
    carries the embedding page's URL and title as `fromUrl` and `fromTitle`;
  - a stored report's Refresh: `report.refresh` registered and `details.refresh` not, one click
    re-reading the report and running it under a new generation;
  - two hosts of one report with different `stateScope`s: no dev-mode error, and each keeps its own
    scroll; two chart tables at once;
  - export: a capped count straight to `reports/export/start`, a known count and a pivot
    synchronous, the 413 fallback, no Export in the too-much-data state, a coded `Custom` export's
    413 presented, and no Export for `None`; deletes, saves and shares announced, not toasted.
- **Renderers:**
  - a coded `{ key }` list sending no paging members and showing its count with no pager;
  - a summary's totals row on a single page, the `totalsOmitted` footer and no notice strip for it
    in `widget` mode, no totals drill under `leavesOnly`;
  - an instant column's header suffix and `'column'` cells under a device zone that differs;
  - an unlabelled expression column headed by its expression text;
  - decimal strings formatted exactly beyond 15 digits, and `grouping: false`;
  - `format.choices` labels per slot and an unmatched value; month and weekday names per calendar,
    a month name without a calendar in Gregorian;
  - an image cell with and without `thumb`, a failed image, a null id, and an image in a coded
    table; a file cell's name.
- **Codecs and rules:** `p.{Key}` vectors per type for the typed codec, including a `Numeric` value
  past 15 digits read as a `TmDecimal`, and raw-text vectors for a stored report; §4.5's precedence
  and the remembered-value filter, coded reports included; drill filters against
  `drill-golden.json` (one vector per row of spec 0033 §9.4's table, measure conditions and leaf
  cells included, shared with its C# tests); the `#d=` payload surviving `navigateByUrl`; the
  `maxFilterLength` refusal.
- **Chips:** bare-link conjuncts against a fake discover: a foreign key's display name and "#id", a
  `descendantOf`, a formatted date, `null`, an operator, a string literal holding a bidi control
  and one past 80 characters, and label-only leftovers.
- **Studio harness:** §12.5's targets and "Add to…", date parts included; default displays as
  placeholders, the default title included; the disabled totals switch; parameter displays
  pre-filled; "Fill from preview" and its 100 cap; §12.6's kept blocks; §12.7's span and pointer
  mapping, with pivot measures and a totalled summary's measures sent as `measures` and QX4010
  mapped to its term; overlay edits send nothing; create and edit through the host; a create
  landing on the edit studio with no `get` and no `run`, its splitter, rails and terms kept; a
  save's `422` mapped by `pointer`; edit mode re-entered after Save and Cancel; restore under the
  studio key.
- **Share harness:** the `shares` extra read with `permissions`; staged sharing as at most one
  `share` and one `publish`, each only when its grants changed, and a Publish-only caller saving
  roles with no `share`; Add people offering active individual members only; the Everyone switch
  from its row; a failing second call; warnings from `cannotRead`, "Show who", the badge and its
  chip; "Inactive" and "Service account"; a capped audience; members hidden from a caller without
  role read.
- **Reports page:** the tabs' filter and named filter; no coded report on any tab; no Share or
  Publish in the bulk menu; the launcher line.
- **Conformance and tokens:** the wire mirrors against spec 0033's JSON Schema; the
  `TM_CHART_SPEC_SCHEMA` golden; §7.7's checks over the `categorical` marks in both schemes.
- **E2e on Acme** (Chromium; Firefox and WebKit `@cross-engine`): §15's Acme journeys; warm launch
  with requests held; every page under the production CSP, with a `securitypolicyviolation`
  listener; RTL at 390, 768, 992 and 1440 px; the geometry specs; `pivot-sticky.spec` at host
  widths 280, 375, 768, 1024 and 1280 px with zero, one and two attributes, in both directions,
  asserting the sticky set and no shift across thresholds; `chart-bidi.spec` on all three engines,
  a mixed label drawn by the facade equal in pixels to its runs drawn apart, in both page
  directions, a negative data label in `ar`, and no character of spec 0024 §7.9's set in the chart
  host's text, `aria-label`, `title` or the clipboard.
- **Visual, axe and budgets:** `tm-chart` per type and `tm-pivot`'s styles in light, dark and RTL
  (animations off, DPR 1, pinned fonts); axe on every §3.3 state, the table view, each dialog and
  each studio section; §1's budgets and spec 0024 §12's engine line.

## 15. Definition of done

- **Shipped:**
  - §1's six entry points, with their exports, harnesses, budgets, `pivot` and `chart` tokens and
    Arabic strings;
  - §2's declarations merged, with `coded-report-not-registered`, `coded-report-redeclared` and
    `coded-report-source` checked;
  - §3–§13 implemented, including §13's `report`, `studio` and drill-list `search` contexts;
  - §14's suites green.
- **UI rules:**

| Rule | How the design meets it | Pinning test |
|---|---|---|
| Instant warm launch | report routes draw header and skeleton from cached chunks and descriptors; only `get` and `run` wait; the chart engine loads beside the data; the studio chunk comes from the `on-demand` group, cached after first use | spec 0024's `warm-launch.spec` on `/17/reports/42`, `/17/reports/acme-credit-exposure` and `/17/reports` with every API request held: chrome and skeleton within 1 s, no shift to the loaded page |
| Instant feedback | a commit dims the output and starts the progress bar in the same frame; pivot toggles and the table view are local; presentation edits send nothing; a drop shows its item at once | `report-feedback.spec` (a frame probe after a commit, a toggle and a drop); `studio-overlay.spec` (no request for label and option edits) |
| Non-blocking | one `resource()` per host; superseded and abandoned runs aborted down to SQL; Cancel after 2 s; the bar never disabled; preview runs aborted per edit; writes never aborted | `run-supersede.spec` (three commits: one applied, two aborted); spec 0033 §15's `runs` suite ("an abort reaches SQL") for the server half |
| Stable layout | a band whose height depends only on the parameters and the width; states inside the output box; the notice strip as an overlay; a dimmed held output; a fixed card height; table widths independent of data, and pivot widths fixed before first paint and only widened at a swap; stickiness decided on default widths; stable gutters; a fixed chart frame | `report-geometry.spec` (header, band and output boxes equal across first load, reload, error, no data and a language switch); `pivot-geometry.spec` (expand, collapse, a refresh with wider values); `pivot-sticky.spec` (§14) |
| RTL | logical properties in tables, pivot and studio; numbers physically right; expressions in `dir="ltr"` isolates; display names in `tm-display-name`; no bidi control characters in text or exports (spec 0024 §7.9), and canvas text by §7.4's one exception; the plot unmirrored and its canvas in the page's direction (§7.6) | every e2e in `ar` at four widths; `pivot-rtl.spec`; `chart-rtl.spec` (legend side and order, Arabic-Indic ticks); `chart-bidi.spec` (§14) |
| WCAG AA | §6.4's keyboard model; §7.8's table view; §12.5's "Add to…"; named remove buttons; a run never moves focus, and drill-back restores the active row or cell | `report-keyboard.spec` (open, set every parameter type, drill from each kind, export, share); `studio-keyboard.spec` (build and save a pivot with "Add to…" and the reorder handle); axe as in §14 |

- **Telemetry** (closed property values, spec 0026 §3.2):

| Event | Kind | Properties |
|---|---|---|
| `tm.report.run` | timing | `kind`; `source` (`stored`, `coded`, `inline`); `mode`; `trigger` (`open`, `arguments`, `refresh`, `restore`, `silent`); `outcome` (`ok`, `empty`, `waiting`, `error`, `cancelled`) |
| `tm.report.engine-loaded` | timing | `cached` |
| `tm.report.drill` | event | `from` (`summary`, `pivot`, `chart`), `newTab`, `outcome` (`ok`, `too-long`) |
| `tm.report.open-record` | event, sampled | `child` |
| `tm.report.export` | event | `kind`, `mode` (`sync`, `background`), `outcome` |
| `tm.report.arg-dropped` | event | `reason` (`invalid`, `unknown`) |
| `tm.report.restored` | event | `stale` |
| `tm.report.share-saved` | event | `users` and `roles`, bucketed `0`, `1`, `2-5`, `6+`; `everyone` |
| `tm.studio.preview` | timing | `kind`, `outcome` (`ok`, `invalid`, `error`, `cancelled`) |
| `tm.studio.field-added` | event | `via` (`drag`, `menu`) and `target` |
| `tm.studio.kind-switched` | event | `from` and `to` |
| `tm.studio.draft-restored` | event | none |

- **Acme** adds `creditExposureReport`'s launcher patch (§2.2) and the Customer card over the
  inline `customerSales` (§2.1). Its stored report is the seed `acme.sales-by-center` (spec 0033
  §13). The e2e journeys: open the credit exposure report from the launcher and each other report;
  change parameters; drill a pivot leaf cell and check the drilled rows sum to it; build and save a
  summary over Sales invoice lines, stay in the studio, drill a row and open a line on its invoice
  (`?child=`); go back; export each kind; share with a user; build a pivot by drag and by keyboard
  and save it.

## Decisions record

1. **One host, four modes** — a page, a card, a tile and the assistant share one lifecycle — (§3).
2. **Renderers read only `ReportResult`** — coded reports render, drill and export for free — (§3.5).
3. **Coded reports are declared once, on the server; the client adds placement and the hatches;
   defaults live in C#** — the server then checks parameters, exports and dashboard bindings, and
   the client cannot evaluate `today()` — (§2.2).
4. **The first run always leaves with `get`, carrying the link's text** — no serial
   round trip per open; the server converts by the declared type — (§3.2, §4.4).
5. **Arguments apply on commit, not per keystroke or by a Run button** — Refresh re-runs — (§4.3).
6. **The URL holds supplied values and omissions only** — links re-evaluate `today()` — (§4.4).
7. **`spinner` dims, `clear` clears, and in `silent` mode only a plain `run()` stays silent** —
   nothing jumps — (§3.4).
8. **`tm-pivot` takes a member tree and a cell-text callback** — no report knowledge — (§6.1).
9. **Compact form: a row subtotal on its parent's row, a column subtotal after its children, five
   styles** — as the workbook does — (§6.2, §6.3).
10. **`tm-chart` is a JSON spec over `echarts/core`, canvas only, lazy, seven types** — (§7).
11. **ECharts' rich-text tooltip and legend; the table view is the keyboard path** — no `innerHTML`
    and no hit-testing of our own. Rejected: per-point keys and a hidden table, both too fragile —
    (§7.5, §7.8).
12. **The plot never mirrors in RTL; its text does** — as Excel and AG Charts do — (§7.6).
13. **The series read the eight validated `categorical` tones, led by teal, with decals; the ramp
    reads the seven `sequential` steps** — one accent cannot do it; semantic groups switch with the
    scheme, and avatars use the same `categorical` tones — (§7.7).
14. **Drill-through always opens the drill-through list**, child entities included, with "Open in
    {plural}" to the search page. Rejected: the search page itself (child entities have none, and
    editable filters would change the drill's meaning); a per-root choice (two behaviours) — (§8).
15. **Chips come from navigation state, or from the server's parse of a bare link's filter; the
    crumb never comes from the URL** — titles speak for the app, filters speak for the link — (§8.4).
16. **Export: one action per renderer; a capped count in the background at once, else synchronous
    with the 413 retry** — the search page's rule, so a large export never waits for a refusal
    (§9).
17. **The report page is a details hatch with `edit: 'page'` and the compact header; the studio is
    the details host's body in create mode and on its own edit page, where a create lands** — one
    save path, drafts and conflicts; edit mode never enters the report's URL; most visits set
    parameters and read, so they get the header's space; the author keeps her workspace after the
    first save — (§2.3, §10, §12.1).
18. **Share changes are staged into at most one `share` and one `publish`, each sent only when its
    grants changed** — no partial states, and a caller who holds one of the two securables can
    save — (§10.4).
19. **A summary pages at 1,000 rows and shows its totals row only when one page holds the whole
    result** — the total then covers exactly the rows shown, and every summary with `top` fits —
    (§5.1, §5.3).
20. **A calculation error names the measure and the cell** — the author learns where to guard the
    measure, without reading logs — (§3.3).
21. **Coded reports appear only in the launcher** — their access comes from roles, while stored
    reports are shared. Rejected: one merged list or a "Built-in" tab, which present two access
    models as one — (§2.2, §11).
22. **Details cards embed inline documents** — the report ships with the code and shows to exactly
    the readers of its root, with no stored row or grant after deployment — (§2.1).
23. **Drill payloads ride the URL fragment** — the browser never sends it, so no host's request
    limit applies and filter text stays out of server logs; only the list's own input limit
    remains. Rejected: a server drill token, which costs a write per drill and expires bookmarks —
    (§8.1, §8.2).
24. **Pivot widths fit the data before first paint, and every column resizes** — a number is not
    cut by default, and a resize applies per measure, so it survives a parameter change — (§6.2).
25. **The sticky block is a prefix that fits 40% of the host, down to 300 px** — a phone keeps its
    labels. Rejected: unsticking once the default header passes 40%, below 700 px — (§6.2).
26. **Chart canvas text takes the page's direction by CSS and per-run LRM and RLM marks** — the one
    place the platform inserts bidi characters, because canvas text is never copied and a
    misordered label is a bug; WebKit ignores isolates in canvas — (§7.4, §7.6).

## Review flags

1. **The drill-through list is the search host in list mode** (§8.3). Alternative: a dedicated
   read-only list page without the search toolbar. Flips if users want a toolbar-free drill view.
2. **The plot never mirrors in RTL** (§7.6). Alternative: reversed category axes. Flips if Arabic
   users read chart time right to left in testing.
3. **The reports page opens on All reports** (§11). Alternative: My reports. Flips if
   administrators find All cluttered; it lists every stored report to them.
4. **Remembered parameters skip defaulted ones** (§4.5). Alternative: remember all, or none. Flips
   if users expect a typed "As of" date to stick.
5. **Normalized stacks are computed by the facade** (§7.2). Alternative: refuse them, or server
   shares. Flips if presentation math is ruled aggregation.
6. **No header sorting in report tables** (§5.4). Alternative: an order override on `run`. Flips if
   users sort report tables more than they drill.
7. **Share edits are staged** (§10.4). Alternative: apply each change at once.
8. **The preview runs 600 ms after an edit** (§12.7). Alternative: manual preview. Flips if
   authoring loads production databases noticeably.
9. **The studio edits on its own route** (§12.1), and the report page declares `edit: 'page'`.
   Alternative: the studio as the report page's edit mode, as dashboards do, with no `/studio`
   route. Flips on preferring one page per report.
10. **Delete confirms without the report's reach** (§10.3). Alternative: read the audience first and
    add "It's shared with 3 people and 2 roles", which needs a confirmation hook in spec 0031 §10.2.
    Flips if users delete shared reports by mistake.
11. **Summaries page at 1,000 rows** (§5.1). Alternative: keep 200 and show the totals row less
    often. Flips if 1,000-row pages measure slow on phones.
12. **The launcher line opens the launcher empty** (§11). Alternative: an optional query argument on
    `TmLauncher.show`, so the line opens on the page's search text. Flips if users miss built-in
    reports after searching the reports page.
