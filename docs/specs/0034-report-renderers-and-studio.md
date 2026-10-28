# Spec: Report Renderers and Studio

- **Author:** Ahmad Akra
- **Date:** 20 October 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Spec 0033 stores report definitions and runs them on the server. Every run, whether of a stored
definition, a draft or a coded report, returns one self-describing `ReportResult`. This spec is the
client half:

- **Renderers.** It draws that result as a list, a summary, a pivot or a chart.
- **Hosts.** It shows a report on its own page, in a details card, on a dashboard tile or for a
  future assistant.
- **Pages.** It ships the reports page and the report studio.

Two generic components come with it, both `@tellma/core-ui` entry points that know nothing of
reports: `tm-pivot`, an Excel-style pivot grid, and `tm-chart`, the Tellma facade over Apache
ECharts. Everything else lives in two lazy `@tellma/core` entry points built on specs 0024–0031.

Other specs that carry parts of this design:

- spec 0033 §8.1, §8.11, §9, §12.1 — `reports/run`, measure errors, the result envelope and the
  export routes.
- spec 0035 §9.1, §11 — the report widget and its refresh.
- spec 0030 §2.5, §17 — `searchRead`, the `search` hatch and list mode.
- spec 0031 §3.1, §3.7, §8, §11 — `tm-details-page`, the `page` and `create` hatches with
  `edit: 'page'`, and the save and drafts the studio uses.
- spec 0024 §4.4, §9.2, Appendix A — the `report` skeleton, the report routes and the build code
  `coded-report-source`.
- spec 0025 §10, Appendix A — page state and the `report.<id|key>.params` key.
- spec 0026 §9.3; spec 0027 §3.2; spec 0028 §8.1 — the context rows; launcher entries; the report
  toasts.
- spec 0023 §4.3–§4.4, §7.3, §8.3–§8.4 — the generic components that this spec composes.
- spec 0010 §B.8, §C.1, §C.6, §C.7, §D.7 — the `categorical` tones and `sequential` steps, and
  the grid and format amendments, that this spec composes.

## Goals / Non-goals

**Goals**

- **Little or no code.** A stored definition renders, drills and exports with no code. A coded
  report is one declaration and one C# action. A report in a details card is one element.
- **One result, four renderers.** All four read the same result, so coded reports get the same
  tables, pivots, charts and export.
- **Only data waits.** A parameter change dims the output in the same frame, and nothing moves
  afterwards.
- **Every number drills** to its rows, child entities included.
- **Authors build all four kinds** in the studio, by drag or by keyboard.

**Non-goals (explicitly out of scope)**

- Definitions, execution, sharing, the audience and the Excel writer — spec 0033.
- The dashboard grid, tiles and scheduling — spec 0035.
- Assistant-only page actions — the assistant spec (§13).
- Running totals — reserved by spec 0033.
- Not in v1: scheduled delivery; cross-widget filtering; heatmap, treemap, gauge, scatter and map
  charts; chart image export; header sorting in report tables (§5.4).

## 1. Placement and vocabulary

The pivot and the chart are domain-free `@tellma/core-ui` entry points. The reports live in
`@tellma/core/reports` and the studio in `@tellma/core/report-studio`. The `@tellma/core` primary
holds only declarations.

Contract blocks are TypeScript and C# sketches: names and shapes are normative; imports,
documentation comments, bodies and cancellation tokens are omitted.

| Entry point | Exports | Loaded | Budget (gzip) |
|---|---|---|---|
| `@tellma/core-ui/pivot` | `TmPivot` (`tm-pivot`), `TmPivotModel`, `TmPivotMember`, `TmPivotCell` | with the pivot renderer | 14 KB |
| `@tellma/core-ui/chart` | `TmChart` (`tm-chart`), `TmChartSpec` and its parts, `TM_CHART_SPEC_SCHEMA`, `tmValidateChartSpec` | with the chart renderer; the engine chunk on the first plotted spec | 9 KB; the engine per spec 0024 §12 |
| `@tellma/core` primary | `tmCodedReport` and its types (§2.2), `TmReportRef`, `TmReportArgs`, `tmShareDialog`, the wire mirrors (types only), §2.3's declarations | eager | 0.5 KB |
| `@tellma/core/reports` | `TmReportHost`, `injectTmReportHost`, `TmReportDrillEvent`, `TmReportOverlay`, `TmReportParameterField`, `tmReportArgsFromUrl`, `tmReportArgsToUrl`, `TmShareDialog`, `TmReportPage`, `TmReportsPage`, `TmTransferDialog` | lazy | 24 KB |
| `@tellma/core/report-studio` | `TmReportStudio`, `TmReportStudioPage` | lazy, worker group `on-demand` (spec 0024 §5.3) | 28 KB |

- **Imports.** `/reports` imports the primary, `/entity`, `/search`, `/details` and both `core-ui`
  entry points. `/report-studio` imports `/reports` and `/details`. Code reaches the studio only
  through `import()`, and `core-ui/chart` reaches ECharts through one `import()`.
- **The drill-through list** is spec 0030's search host in list mode. Its crumb and chips (§8.3,
  §8.4) are written in `@tellma/core/search`, to this spec's rules.
- **Folders:** `client/projects/core/tellma-core-ui/{pivot,chart}/` and
  `client/projects/core/tellma-core/{reports,report-studio}/`. **Telemetry areas:** `tm.report`
  and `tm.studio`.
- **Test helpers:** the harnesses `TmPivotHarness`, `TmChartHarness`, `TmReportHostHarness`,
  `TmReportStudioHarness` and `TmShareDialogHarness`, and the fixture `tmReportResultFixture`.
- **Wire mirrors.** `TmReportDocument` with its term types (`TmReportTerm`, `TmReportDimension`),
  `TmReportParameter` and `TmReportResult` mirror spec 0033's records in camelCase.

**Vocabulary**

- *Report* — what a host renders: a stored definition, a coded report or a draft.
- *Draft* — an unsaved definition run as `{ draft }`: the studio's preview, or an assistant's.
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

Stored definitions need no code. A coded report is one declaration beside one C# action; a report
in a details card is one element.

### 2.1 A report in a details card

```html
<!-- customers/customer-details.ts: the customer's credit exposure, bound to the record -->
<tm-details-card titleKey="Acme_Customer_CreditExposure" flush>
  <tm-report-host [report]="{ key: 'acme-credit-exposure' }" [parameters]="{ Customer: details.entity()?.id ?? null }" />
</tm-details-card>
```

- **Bound.** The host sends `Customer` with every run and never shows it.
- **Create mode.** The record has no id yet, so `Customer` binds as `null`. `null` means omitted
  (spec 0033 §4.4), so the card shows the report unfiltered.
- **By key.** Cards embed coded reports by key, because a stored definition's id differs per tenant.

### 2.2 `tmCodedReport`

```ts
// @tellma/core (primary)
declare function tmCodedReport(key: string, report: TmCodedReportDefinition): TmCodedReportDeclaration;
interface TmCodedReportDefinition {
  readonly titleKey: string; readonly descriptionKey?: string;
  readonly securable: TmSecurableRef;                     // gates the route, the launcher entry and the reports page row
  readonly section?: string; readonly order?: number; readonly icon?: string; readonly synonymsKey?: string; // the launcher entry (spec 0027 §3.2)
  readonly kind: TmReportKind;                            // picks the skeleton before the first result
  readonly parameters?: Readonly<Record<string, TmCodedParameter>>; // bar order = declaration order
  readonly route?: string;                                // the common case: the [ApiAction] route; runs and exports
  readonly run?: (api: TmApi, args: TmReportArgs, signal: AbortSignal) => Promise<TmReportResult>; // hatch: custom fetch, no export
  readonly component?: TmLazyComponent;                   // hatch: replaces the output; reads injectTmReportHost()
}
interface TmCodedParameter {                              // defaults: type 'Numeric' with entity, else 'String'; labelKey `<titleKey>_<Key>`
  readonly type?: TmReportParameterType; readonly entity?: string; readonly required?: boolean; readonly labelKey?: string;
}
type TmReportKind = 'List' | 'Summary' | 'Pivot' | 'Chart';                                   // spec 0033 §2.1
type TmReportParameterType = 'Numeric' | 'String' | 'Bool' | 'Date' | 'DateTime' | 'DateTimeOffset' | 'Guid'; // spec 0033 §4.1
type TmReportArgs = Readonly<Record<string, TmJson>>;     // spec 0033 §4.4's encoding; null = omitted
```

**Example —** Acme's credit exposure report, the whole client side:

```ts
// app/reports/credit-exposure.report.ts
export const creditExposureReport = tmCodedReport('acme-credit-exposure', {
  titleKey: 'Acme_Report_CreditExposure', section: 'sales',
  securable: ['acme.CreditExposureReport', 'Read'], kind: 'Summary',
  parameters: { AsOf: { type: 'Date', required: true }, Customer: { entity: 'acme.Customer' } },
  route: 'acme-reports/credit-exposure',                  // C# applies AsOf ?? today and echoes it as Default
});
```

- **The call.** The host calls `TmApi.read(route, args, { signal })` with the keys in camelCase,
  such as `{ "asOf": "2026-10-20", "customer": 1012 }`. The C# body record's properties match
  those keys (spec 0033 §11). When a required value is missing and the method cannot supply it,
  the server answers `422 Report.ArgumentRequired`.
- **Long by declaration.** The route's action declares `Long = true` (spec 0033 §11), so the
  Contracts chunk lists it and `TmApi` gives it the long watchdog (spec 0024 §3.4). A `run` hatch
  that calls a hand-mapped endpoint (spec 0016 §4.5) passes `long: true` itself.
- **Sources.** A declaration sets exactly one of `route`, `run` or `component`. The one allowed pair
  is `component` with `route`: the host fetches, and the component renders the host's result. Any
  other combination fails the build with `coded-report-source`.
- **A `component`** renders inside the page chrome and reads `injectTmReportHost()`. Without
  `route` it fetches its own data, and the report offers no Export.
- **Derived from the declaration:** the route `/{t}/reports/{key}` (keys per spec 0024 §9.1), the
  launcher entry, the reports page row (§11), the page context and telemetry.

### 2.3 The platform's declaration

One entity declaration and one page, in the implicit core feature, give the reports page, the
report page, create mode and the studio route.

```ts
// @tellma/core primary
export const tmReportDefinitions = tmEntity('core.ReportDefinition', {
  section: 'analysis', order: 10, icon: 'chart-column', synonymsKey: 'Core_ReportDefinition_Synonyms',
  columns: ['Title', 'Kind', 'Root', 'Owner', 'ModifiedAt'], orderBy: 'Title',
  tabs: [{ key: 'all', labelKey: 'tm.report.tabAll' }, { key: 'mine', labelKey: 'tm.report.tabMine' },
         { key: 'shared', labelKey: 'tm.report.tabShared' }],        // no queryex: searchRead reads the tab
  searchRead: (request, query) => ({ route: 'reports/browse',
    body: { scope: query.tab === 'mine' ? 'Mine' : query.tab === 'shared' ? 'Shared' : 'All', query: request } }),
  search: () => import('@tellma/core/reports').then((m) => m.TmReportsPage),
  details: { page: () => import('@tellma/core/reports').then((m) => m.TmReportPage),
             create: () => import('@tellma/core/report-studio').then((m) => m.TmReportStudio),
             edit: 'page' },                                  // no in-place edit: Edit opens the studio (§10.2)
  clone: (copy, source) => ({ ...copy, code: null, basedOnId: source.id, shares: [] }), // Clone is "Save as"
  actions: [{ name: 'share', visible: () => false, dialog: tmShareDialog('reports') }, // opened by report.share (§10.2)
            { name: 'publish', visible: () => false },                            // the share dialog covers it
            { name: 'transfer', dialog: () => import('@tellma/core/reports').then((m) => m.TmTransferDialog) }],
});
export const tmReportStudioPage = tmPage('reports/:id/studio', {
  component: () => import('@tellma/core/report-studio').then((m) => m.TmReportStudioPage), titleKey: 'tm.studio.title' });
```

`tmShareDialog(segment)` lazily loads §10.4's dialog for a segment. Spec 0035 passes `'dashboards'`.

## 3. The report host

`tm-report-host` runs one report and renders its parameter bar, output, states and footer. Every
placement is a `mode` of it, and whatever renders inside reads it through `injectTmReportHost()`.

### 3.1 Contract

```ts
// @tellma/core primary (types only)
type TmReportRef = { readonly id: number } | { readonly key: string } | { readonly draft: TmReportDraft };
interface TmReportDraft { readonly root: string; readonly kind: TmReportKind; readonly document: TmReportDocument }

// @tellma/core/reports
@Component({ selector: 'tm-report-host' })
declare class TmReportHost {
  readonly report: InputSignal<TmReportRef>;
  readonly mode: InputSignal<'page' | 'card' | 'widget' | 'assistant'>;  // default 'card' (§3.6)
  readonly parameters: InputSignal<TmReportArgs | undefined>;           // bound arguments
  readonly showParameters: InputSignal<boolean | undefined>;            // default: true in page and assistant mode
  readonly declarations: InputSignal<readonly TmReportParameter[] | undefined>; // the bar's fields, when the page holds them
  readonly document: InputSignal<TmReportDocument | undefined>;         // the stored document, when the page holds it: labels a calculation error (§3.3)
  readonly held: InputSignal<TmReportResult | undefined>;               // shown first; no run until run() or an argument change
  readonly refresh: InputSignal<'spinner' | 'silent'>;                  // how a pending run looks (§3.4)
  readonly dashboardId: InputSignal<number | undefined>;                // sent with each run (spec 0033 §8.2)
  readonly stateKey: InputSignal<TmPageStateKey | undefined>;           // page mode: where §3.7 remembers
  readonly overlay: InputSignal<TmReportOverlay | undefined>;           // the studio's presentation edits (§12.8)
  readonly asTable: ModelSignal<boolean>;                               // a chart shown as its table (§7.8)
  readonly drill: OutputEmitterRef<TmReportDrillEvent>; readonly ran: OutputEmitterRef<TmReportResult>;
  readonly status: Signal<'waiting' | 'loading' | 'ready' | 'error'>;   // waiting: a required argument is empty
  readonly args: Signal<TmReportArgs>; readonly result: Signal<TmReportResult | undefined>; // the supplied values; the held result
  readonly error: Signal<TmApiError | null>; readonly executedAt: Signal<string | null>;
  readonly stale: Signal<boolean>;                                      // a silent refresh failed (§3.4)
  readonly generation: Signal<number>; readonly signal: Signal<AbortSignal>; // for a coded component
  setArgs(patch: TmReportArgs): void;                                   // the bar's commits; null = omitted
  run(options?: { clear?: boolean }): void;                             // a new generation; clear drops the held result first
  cancel(): void; export(): Promise<void>;                              // §9
  expandAll(axis?: 'rows' | 'columns'): void; collapseAll(axis?: 'rows' | 'columns'): void; // a pivot
}
declare function injectTmReportHost(): TmReportHost;
```

- **One `resource()`.** The host runs through one `resource()`, keyed by the report, the bound
  arguments, `args` and `generation`. A new key aborts the run in flight. Leaving aborts it through
  `DestroyRef`, all the way down to SQL (spec 0033 §8.9).
- **Transport.** `{ id }` and `{ draft }` call `TmApi.read('reports/run', { id | draft, arguments,
  dashboardId, skip, take, includeCount: true }, { signal })`. `{ key }` calls the declaration's
  `route` or `run`.
- **Paging.** Only List and Summary send the paging members. `take` is the kind's page size (§5.1).
- **Long reads.** Every run is a retried, cancellable long read (spec 0024 §10.5). The Contracts
  chunk marks `reports/run` long and idempotent (spec 0024 §3.4).
- **Arguments sent** are `args` merged with the bound arguments; a bound value wins. The bar shows
  the effective arguments, so a defaulted parameter shows its default value.
- **No definition read**, with one exception. A host that shows the bar for `{ id }` and has no
  `declarations` reads `reports/get` once, alongside the run. `widget` mode never reads it.

### 3.2 Opening a report page

1. The route host draws its skeleton in the click's frame (spec 0024 §4.4): `details` for
   `/{t}/reports/{id}`, `report` for `/{t}/reports/{key}`.
2. For a stored report, the details host reads `reports/get` with the `permissions` extra (spec
   0031 §3.3). With no `p.*` keys in the URL, the report page starts the run at the same time.
   With `p.*` keys, it starts when the record lands, since the codec needs the parameter types
   (§4.4).
3. For a coded report, the declaration is local, so the run starts at once.
4. The result renders, the page registers its context (§13) and records `tm.report.run`.

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
| waiting | a required parameter without value or default, or `422 Report.ArgumentRequired` | `sliders-horizontal`, "Fill in the required parameters" with their labels | Fill in: focuses the first empty field |
| invalid argument | `422 Report.ArgumentInvalid` | the waiting state, and the field's error popover | — |
| no data | an empty table, cube or series | `inbox`, "No data for these parameters" | — |
| too much data | `413` with `Report.TooManyRows`, `Report.TooManyPoints`, `Report.TooManyCells` or `limit-exceeded` | `circle-alert`, "This report returns too much data to show", "Add a filter or narrow the parameters." | Export, when available (§9) |
| needs fixing | `400 query-invalid` | `wrench`, "This report no longer runs", "A field it uses has changed." | Edit, with `permissions.save` |
| calculation error | `422 Report.MeasureFailed` (spec 0033 §8.11) | `circle-alert`, "A measure couldn't be calculated", and the reason (below) | Edit, with `permissions.save` |
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
  - *Without `row`,* SQL raised the error, and "at {cell}" reads "in some rows".
  - *`{measure}`* is the label (§3.5) of the term at `pointer`. The host reads it from the draft,
    the stored document or the held result. The stored document is the `document` input, or the
    definition the host read itself (§3.1). With none at hand, or with an empty pointer, it reads
    "A calculation".
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
| `run()` | reloading | the held output, unchanged and without indicator, swapped on arrival; sent `background: true` |
| an argument change | reloading | reloading |
| `run({ clear: true })` | the held result is dropped; the skeleton at once | the same |
| a failure over a held result | the failure's state | `forbidden`, `not-found`: the state; any other: the held output stays and `stale()` is true |

- **The swap** is one change-detection pass, never animated. A background run sends
  `Tellma-Activity: background` (spec 0024 §10.2) and stamps no activity.
- **The stale marker** is a 16 px warning `circle-alert` after the footer's time. Its accessible
  name is "Couldn't refresh. Showing data from {time}.", and its tooltip holds the trace id's
  `tm-reference`.

### 3.5 From result to renderer

- **The renderer follows `result.kind`.** Before the first result, it follows the kind of the coded
  declaration, the draft or `held`. As soon as the kind is known to be `Chart`, the engine's
  `import()` starts.
- **A coded `component`** replaces the renderers of §5–§7.
- **Column labels** are the first that exists: the active slot's `label…`, then `labelKey`, then
  the slash label of `path` from `result.root` (spec 0029 §11.4). A language switch re-labels with
  no run.
- **Values** format through `tmReportFormat(column, row)`:
  - navigations through `TmDisplayNames.of(entity, row)`, over the column's ordinals (spec 0033
    §9.2);
  - text in the active slot; `enum` through `TmEntities.enum(name)`;
  - `display` and `format` through `TmL10n`: spec 0005 §2.2's `formatNumber` and `formatDate`, and
    spec 0010 §D.4 for instants;
  - a `decimal` or `long` value from the exact string it arrives as (spec 0033 §9), never through
    `number` (spec 0024 §10.4);
  - `month`, `quarter` and `year` per spec 0010 §D.7, in `format.calendar`, else the viewer's
    calendar;
  - numbers LTR-isolated and aligned to the physical right.

### 3.6 Modes

The `mode` input fits the host to its placement. The table lists what each mode shows and does.

| Element | `page` | `card` | `widget` | `assistant` |
|---|---|---|---|---|
| title, description | the page header (§10.1) | the card's heading | the tile's (spec 0035) | a title line |
| parameter bar | shown | hidden unless `showParameters`; bound arguments never | hidden | compact |
| toolbar | the page's (§10.2) | a `…` menu in the card's actions: Refresh, Export, Show as table, Open report | none: the tile calls `run()` and `export()` | Open report, Export |
| footer | "1–200 of 1,214 rows · Updated 10:42" and the pager (§5.1) | "Updated 10:42" | none | as `card` |
| URL, remembered values, page state | the report pages write them (§4.4, §4.5); page state with `stateKey` (§3.7) | no | no | no |
| block size | fills `main` | 360 px (`--tm-report-height`), scrolling inside | fills the tile | 320 px |
| drill-through | navigates | navigates | navigates | emits only |
| page context | `report` (§13) | none: the details page's | the dashboard's | the assistant's |

- **"Updated {time}"** is an absolute time in a `<time>` element. For a run today in the display
  zone it is `TmL10n.formatTime(executedAt)`. Otherwise it is `formatDateTime(executedAt, {
  dateStyle: 'medium' })` (spec 0010 §D.4). Both add the zone label when the display zone's
  offset differs from the device's: "Updated 10:42 GMT+3".
- **A future search-page pivot view** is this host in `card` mode, fed a `{ draft }` built from the
  search page's query. Nothing else is reserved for it.

### 3.7 Page state

In `page` mode with a `stateKey`, the host saves `{ v: 1, args, result, scroll, expansion, asTable,
modifiedAt }` as page state when it is destroyed (spec 0025 §10). Results never touch disk.

- **Keys.** The report page saves under `tmPageStateKey('/{t}/reports/{id}/run')`, apart from the
  details host's own snapshot (spec 0031 §13). The coded page saves under its path.
- **Return.** Coming back renders the saved result as held, with no request. A changed
  `modifiedAt` discards the state. State that is older than 5 minutes or marked stale (`isStale`,
  spec 0025 §10) renders and also runs, as reloading.

## 4. Parameters and the parameter bar

The parameter bar renders one typed field per parameter of the stored document, the coded
declaration or the draft. A value applies when its field commits. The bar keeps the URL and the
remembered values in step.

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
interface TmReportParameter {                             // primary, types only (spec 0033 §4.1); a coded parameter maps to one with no default
  readonly key: string; readonly label?: string; readonly label2?: string; readonly label3?: string; readonly labelKey?: string; // labels per slot
  readonly required?: boolean; readonly default?: string; readonly type: TmReportParameterType; readonly entity?: string;
}
```

| Type | Control (in a 168 px `tm-form-field`) | Commits on | URL text |
|---|---|---|---|
| `Numeric` with `entity` | `tm-entity-field [entity]` (spec 0029 §14) | pick, clear | `p.Customer=1012` |
| `Numeric` | `input[tmNumber]` | Enter, blur | `p.Year=2026` |
| `String` | `input[tmInput]`, `dir="auto"` | Enter, blur | `p.Region=East` |
| `Bool` | `tm-select`: Any (optional only), Yes, No | selection | `p.Posted=true` |
| `Date` | `tm-date-picker` in the viewer's calendar | pick, Enter, blur | `p.AsOf=2026-10-01` |
| `DateTime` | `tm-date-picker`; the day at `T00:00:00` | as `Date` | `p.Since=2026-10-01T00:00:00` |
| `DateTimeOffset` | `tm-date-picker`; the day's first instant in the display zone (spec 0029 §13.2) | as `Date` | `p.Since=2026-09-30T21:00:00%2B00:00` |
| `Guid` | `input[tmInput]`, mono, `dir="ltr"` | Enter, blur | `p.Ref=…` |

- **Labels** are the active slot's `label…`, else `labelKey`, else the key. Required parameters
  carry the field's required marker. An entity parameter takes a single id.
- **Clearing** an optional field commits `null`, which removes the restriction. Clearing a required
  field shows "Required" and the waiting state, and sends nothing. A field with nothing supplied
  shows the run's effective value, hinted "Default".

### 4.2 Layout

- **The row.** Fields wrap in one row with 12 px gaps, ended by a ghost Reset button that is
  disabled while nothing is supplied. The row's height depends only on the field count and the
  width: errors are focus popovers (spec 0002), so nothing moves while typing.
- **`assistant` mode** puts the labels inside the fields.
- **Below `sm`,** the bar becomes a Parameters button and a one-line summary, such as "As of 20 Oct
  2026 · Customer: Olive Inc.". They open an end drawer (`TmModal` with `placement: 'end'`, spec
  0010 §D.2) whose Apply commits every field at once. Fields there are 46 px (`lg`) tall (spec 0010
  §B.6).

### 4.3 Commit and run

1. A field commits, and `setArgs({ Key: value })` replaces that key.
2. If a stored definition has a required parameter with neither a value nor a `default`, the host
   shows the waiting state and sends nothing. Otherwise the host runs. A coded report always runs,
   because its server decides.
3. The run in flight is aborted. The page writes the URL (§4.4) and the remembered values (§4.5).

- **Refresh**, and Mod+Enter in a field, re-run the same arguments under a new generation.

### 4.4 The URL: `p.{Key}`

```ts
// @tellma/core/reports — also used by spec 0035 over its dashboard parameters
declare function tmReportArgsFromUrl(params: URLSearchParams, parameters: readonly TmReportParameter[]): TmReportArgs | null; // null: no p.* key
declare function tmReportArgsToUrl(args: TmReportArgs, parameters: readonly TmReportParameter[]): readonly (readonly [string, string])[];
```

- **Text.** A value is written in its platform scalar encoding (spec 0016 §3.2) without JSON
  quotes, then encoded per spec 0024 §9.5. §4.1's table shows the text for each type.
- **Numbers.** The codec and the bar hold a `Numeric` value as a `number`, as editors do (spec 0024
  §10.4).
  - `p.Year=2026` reads as `2026`. A stored report's `decimal` and a coded report's `int` both
    accept it.
  - Text past 15 significant digits reads as a `TmDecimal`, which a `decimal` takes exactly.
  - Echoed values arrive as `TmDecimal` strings (spec 0033 §4.4). A field shows one through
    `TmDecimals.toNumber`.
- **Keys** match ignoring case. The writer uses the declaration's spelling and order.
- **What is written:**

  | Argument | URL |
  |---|---|
  | a supplied value | `p.Key=<text>` |
  | an explicit omission of a defaulted parameter | `p.Key=` |
  | a parameter at its default | nothing, so a shared link re-evaluates `today()` |

- **Updates** use `replaceUrl`. Invalid text or an unknown key is dropped, and the host records
  `tm.report.arg-dropped`.

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

- **What is kept.** The supplied values of parameters without a default. For a coded report, whose
  defaults live in C#, these are the parameters whose last source was not `Default`.
- **Reset.** An empty `args` resets the key. Bound arguments never read or write it.

## 5. List and Summary renderers

Both render a `ReportTable` (spec 0033 §9.2) in a readonly `tm-grid`. A list row opens its record;
a summary row drills through to its rows.

### 5.1 Binding

- **Grid.** `gridId` is `report.<id|key>`, with `rowActivation="row"` (spec 0010 §C.1) and
  `rowHeaders: false` (spec 0010 §C.6). Rows are zebra-striped and 30 px tall, under a frozen
  header, with the stable gutter (spec 0010 §C.7).
- **Columns.** One grid column per `ReportColumn`; each cell reads `table.columnOrdinals[i]` and
  formats through `tmReportFormat`. Widths never depend on data: text and navigations `flex 1.6`
  (at least 160 px), numbers 120 px, dates 110 px, booleans 72 px, images 56 px.
- **Instant columns.** A column that displays instants appends `TmFormats.zoneSuffix` (spec 0029
  §12.1) to its header, as spec 0030 §7.1 does: "Posted at (GMT+3)". Its cells format in
  `'column'` mode (spec 0010 §D.4).
- **Pages.** A list pages at 200 rows and a summary at 1,000, so every summary with `top` fits on
  one page and can show its totals (§5.3). `tm-grid` windows its rows, so the larger page costs
  nothing on screen.
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
  - *Look.* It takes the `rowClass` `tm-grid-row--emphasis` (spec 0010 §C.6) and shows "Total" in
    the first key column. It is not counted.
  - *Drill.* It drills with no member condition. Under `top` it has no drill (spec 0033 §9.4).
- **Omitted totals.** When `table.totalsOmitted` is true, the footer adds "Totals show when all
  rows fit on one page. Export includes them."
  - `widget` mode has no footer. The tile's status line carries a short note instead (spec 0035
    §8.5).
  - A `widget`-mode host without a status line shows no message; the report page explains.

### 5.4 Not in v1

Report tables have no header sorting, column editor or column filters. The definition decides
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
  readonly label: string;                               // display-ready; the pivot appends "(direct)" to a phantom
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
}
@Component({ selector: 'tm-pivot' })
declare class TmPivot {
  readonly model: InputSignal<TmPivotModel>;
  readonly expandedRows: ModelSignal<ReadonlySet<number>>;    // ids of expanded row members
  readonly expandedColumns: ModelSignal<ReadonlySet<number>>; // ids of expanded column members
  readonly activatable: InputSignal<boolean>;           // default true: value cells are drill targets
  readonly ariaLabel: InputSignal<string>; readonly size: InputSignal<TmControlSize | undefined>;
  readonly cellActivate: OutputEmitterRef<TmPivotCell>;
  expandAll(axis: 'rows' | 'columns'): void; collapseAll(axis: 'rows' | 'columns'): void; focus(): void;
}
interface TmPivotCell { readonly row: number; readonly column: number; readonly measure: number; readonly newTab: boolean }
```

- **Lazy text.** The pivot calls `text` only for rendered cells. A 500,000-cell cube costs nothing
  until shown, and a language switch reformats only the visible window. The caller owns the
  expansion sets.

### 6.2 Layout

- **Rows (compact form).** One row-header column indents members 16 px per depth. Parents carry a
  13 px twisty, a `tm-icon` with `mirrorInRtl` (spec 0023 §3.1). A parent's row carries its
  subtotal, and its children follow it. The `total` member renders last and never collapses.
- **Columns.** An expanded member's group holds its children, then its subtotal column "{label}
  total". The `total` member comes last, as "Total". Each group holds one data column per measure.
  Header rows are one per column level, plus a row of measure names for two or more measures.
- **Fixed geometry.** Widths depend only on the container and the column count. The row header is
  280 px (resizable 160–600 px; 160 px below `sm`), and attribute columns are 96 px. Data columns
  share the rest at 96 px or more each; when they cannot, each is 112 px and the pivot scrolls
  sideways.
- **Cells and windowing.** Cells are 30 px tall. Headers, the row-header column and the attribute
  columns are sticky, and gutters are stable. Body rows window through `tmComputeAxisWindow` (spec
  0004); columns window past 40.

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
- **Numbers** are tabular and LTR-isolated. A cell without facts is empty, never "0".
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

### 6.5 The Pivot renderer

- **Members** map from `cube.rowMembers` and `cube.columnMembers` (spec 0033 §9.3). Labels:
  `Member` through `tmReportFormat` (navigations from `display`); `None` as "(None)"; `Total` as
  "Total"; `Phantom` as its parent's label. `columnLevelLabels[j]` names a dimension on its first
  level only.
- **Cells** sit in one `Map` keyed by `row × stride + column`. `text` formats `values[measure]` with
  that measure's column.
- **Totals are the server's.** Every subtotal and total arrives in the cube. Its measures are
  computed over the leaf rows that passed `having` (spec 0033 §8.6), so totals match the cells
  shown. The renderer never computes one.
- **Initial expansion.** Members with children start expanded when their dimension's `expand` is
  true (`ReportColumn.Expand`, default true). Page state keeps the sets (§3.7).

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
  readonly dimensions: readonly { readonly label: string; readonly members: readonly { readonly label: string }[] }[]; // 0–2, display order
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
  readonly activatable: InputSignal<boolean>;              // default true: points and table rows drill
  readonly pointActivate: OutputEmitterRef<{ readonly members: readonly [number | null, number | null]; readonly newTab: boolean }>;
}
declare const TM_CHART_SPEC_SCHEMA: TmJsonSchema;
declare function tmValidateChartSpec(spec: unknown): readonly { readonly pointer: string; readonly code: string }[]; // schema and §7.2's counts; for agents and the MCP server
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
- **Axis ticks** on the value axis use `Intl.NumberFormat(locale, { notation: 'compact' })`, such as
  "100K" or "١٠٠ ألف". Canvas labels are wrapped in U+2068 … U+2069.
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
  `tmReportFormat`. A plot needs numbers, so measure values convert with `TmDecimals.toNumber`
  (spec 0024 §10.1), exact to 15 significant digits.
- **Drill.** `pointActivate` and an activated table row drill through their members (§8.1); a
  `card` drills on click with no member condition.
- **A chart type the facade does not draw** (only a coded report can return one) renders as the
  table, with Show as chart disabled.

## 8. Drill-through

A summary row, a pivot cell or a chart point opens the list of the rows behind it. A list row opens
its own record instead (§5.2). The host builds the list's URL from the result alone.

### 8.1 From coordinate to filter

1. **The coordinate.** A summary row: every key column. A pivot cell: for each dimension, the
   deepest member among the cell's row and column members and their ancestors. A chart point: each
   dimension's member. Totals add nothing, and the measure is ignored (spec 0033 §9.4).
2. **One condition per member,** shaped as spec 0033 §9.4's table gives it. The host builds it with
   `tmQx` and `tmQx.literal` (spec 0029 §8.2); an expression member uses
   `tmQx.raw(drill.terms[i])`. Nothing is concatenated.
3. **The filter** is `tmQxPrint(tmQx.and(tmQx.raw(drill.filter), …conditions))`, without the raw
   node when `drill.filter` is null.
4. **The URL** is `/{t}/list/{drill.segment}?filter=…&cols={drill.columns}`. The source report
   travels in navigation state (§8.2).

A result without `drill` offers no drill affordance. When there is one:

- **A leaf cell or a summary row** opens a list that holds exactly its rows.
- **A subtotal or a total** opens a list that may also hold rows that `having` hid. That list can
  add up to more than the number clicked (spec 0033 §9.4).

**Example —** Acme's `acme.sales-by-center` pivot runs with `Year` defaulted to 2026. Activating the
cell Riyadh × Q3 gives the filter `(year(Invoice.PostingDate) = 2026) and descendantOf(Center.Id,
11) and (quarter(Invoice.PostingDate)) = 3`. The chips read "Center: Riyadh · Quarter: 3 · Year:
2026".

### 8.2 The drill event

```ts
// @tellma/core/reports
interface TmReportDrillEvent {
  readonly url: string; readonly segment: string; readonly filter: string | null; readonly columns: readonly string[];
  readonly chips: readonly { readonly label: string; readonly value: string }[]; // members, then arguments, as displayed
  readonly newTab: boolean; preventDefault(): void; readonly defaultPrevented: boolean; // preventDefault: the host does not navigate
}
```

- **The default action** in the same tab is `router.navigateByUrl(url, { state: { tmDrill: { from,
  fromUrl, fromTitle, chips } } })`, kept in `history.state` across reloads. For `newTab` it is
  `window.open(url, '_blank', 'noopener')`. `assistant` mode only emits (§3.6).
- **`tmDrill`.** `from` is the report's id or key, or null for a draft. `fromUrl` is the report's
  canonical URL with its `p.*`.
- **Too long.** The host refuses a URL over 8,000 characters, because a cold load would exceed the
  server's 8 KB request line. Instead, the notice strip (§3.3) says "This selection is too detailed
  to open as a list. Narrow the report first."

### 8.3 The drill-through list

`/{t}/list/{segment}` is spec 0030's search host in list mode (spec 0030 §17). The URL's `filter`
is a fixed leaf, and `cols` names the columns. The source report comes from
`history.state.tmDrill` (§8.2). This section fixes what list mode adds.

| Part | Content |
|---|---|
| route | `TmEntities.bySegment`, child segments included (spec 0029 §2.2); an unknown segment shows the tenant not-found page |
| row 1 | the crumb: the source report's title (§8.4), linking to the report; the `h1`: the entity's plural label |
| row 2 | the chips (§8.4), "Open in {plural}" and Export (below) |
| invalid filter | `400 query-invalid` on the fixed leaf: `circle-alert`, "This link's filter is not valid", without Edit filter |

- **Row 2's actions.** "Open in {plural}" shows for a top-level root with a search page and links to
  `TmUrls.search(entity, { filter, cols })`. Export follows spec 0030 §13.2 and shows when
  `allows(entity, 'export')`, so never for a child root.
- **Child roots.** The select is spec 0030 §5.1's, plus the owner's key (`Invoice.Id`), and for a
  grandchild also the top-level owner's. A top-level row opens its details. A child row opens its
  owner with `?child={collection}:{id}` (spec 0031 §14). A grandchild row opens its top-level owner
  on the intermediate child.
- **Back.** The crumb calls `history.back()` when the previous entry is the source report, that is
  when `history.state.tmDrill` is set and the tab can go back. The report then restores from page
  state (§3.7) with its arguments, active row and focused cell. Otherwise the crumb navigates. Pins
  and recents follow spec 0030 §17.

### 8.4 Chips without URL text

- **From a report,** the chips and `fromTitle` come from the event, read from
  `history.state.tmDrill`.
- **From a bare link** (a new tab, a copied URL), the list makes one `queryex/discover` call (spec
  0029 §9) with the entity as root and `filters: [filter]`. Each distinct path becomes a chip after
  "Filtered by", showing its slash label (spec 0029 §11.4) without values. The crumb reads
  "Reports" and links to `/{t}/reports`.
- **No URL text is ever rendered,** the filter included: a crafted link chooses rows, never words.

## 9. Export to Excel

Export asks the server for spec 0033 §12's workbook of the shown result. Lists and summaries export
flat, a pivot fully expanded, and a chart as its data.

1. The host calls `TmApi.download('reports/export', { id | draft | route, arguments, dashboardId },
   { signal, onProgress })` with the shown result's effective arguments. The Contracts chunk marks
   the route long (spec 0024 §3.4).
2. A sticky "Exporting…" toast shows progress and Cancel (spec 0028 §8.1); leaving also aborts. The
   file saves through an object URL and an `<a download>`, under the server's file name.
3. On `413 Excel.Export.RowLimitExceeded`, the host makes one `TmApi.write('reports/export/start',
   same body)` call, then shows "Started in the background." with Track (spec 0028 §8.1).
4. Any other failure, `413 Report.ExportTooWide` included, goes to
   `TmErrorPresenter.present(e, { kind: 'action' })`.

Every mode but `widget` offers Export; there, spec 0035's tile calls `export()`. A coded report
offers it only with `route`, and the studio offers it for its draft. The drill-through list exports
as a search page does.

## 10. The report page and its dialogs

A stored report's page is the details host with `TmReportPage` as its `page` hatch (spec 0031
§3.7). The host keeps the header, record actions, pager and page scope. A coded report's page draws
the same anatomy on its own `tm-page-header`.

### 10.1 Header and body

- **Row 1,** in order: the crumb "Reports" (the host's, or on a coded page a link to
  `/{t}/reports`); the `h1`, the title in the active slot; the host's Active or Inactive badge; the
  kind's badge; and "Standard" for a report with no owner, or "Built-in" for a coded one.
- **Description.** A one-line description follows, with its full text in a tooltip.
- **Row 2** is the toolbar (§10.2).
- **Body:** the parameter bar (§4.2), the output with its notice strip (§3.3), and the footer
  (§5.1).
- **The host's inputs.** The hatch reads the record through `injectTmDetails()` and parses its
  `definitionJson`. It passes the document's parameters to the host as `declarations`, and the
  document itself as `document`.

### 10.2 Toolbar and Actions

The page registers its own actions. On a stored report, the details host adds the record's.

| Control | When | Action id | Group |
|---|---|---|---|
| Edit, to `/{t}/reports/{id}/studio` | a stored report with `permissions.save` | `report.edit` | primary |
| Share (§10.4) | a stored report with `permissions.share` or `permissions.publish` | `report.share` | primary |
| Export (§9) | §9's conditions | `report.export` | primary |
| Show as table or chart | a chart | `report.toggle-table` | primary |
| Refresh | a coded report | `report.refresh` | primary |
| Expand all, Collapse all | a pivot | `report.expand-all`, `report.collapse-all` | menu |
| Copy link: the canonical URL with its `p.*`, announced "Link copied" | always | `report.copy-link` | menu |

- **From the host** (spec 0031 §10.1), on a stored report: New; Clone, which is the report's Save
  as (§2.3); Activate or Deactivate; Delete; the pager; Change owner (§10.3); and Refresh. The
  host's Refresh re-reads the definition and re-runs the report under a new generation.
- **No in-place edit.** The declaration's `edit: 'page'` (spec 0031 §3.7) removes the host's
  `details.edit`, so Edit opens the studio. Below `md`, Edit and Share join the Actions menu.
- **Pins** are the shell's (spec 0027 §8) and keep the `p.*` parameters.

### 10.3 Delete and transfer

- **Delete** is the host's (spec 0031 §10.2): its confirmation, then "Report deleted." and the
  reports page. When a dashboard shows the report, the server answers `422
  ReportDefinition.InUse`; this opens the 400 dialog, naming up to five dashboards. Deactivating is
  the alternative.
- **Change owner** is the `transfer` action's `TmTransferDialog`, a `TmModal` of size `sm`. It holds
  one `tm-entity-field entity="core.User"` of active individual members. It runs as `{ ownerId }`
  through `injectTmActionDialog().run` (spec 0031 §10.4). The reports page runs it in bulk (§11).

### 10.4 The share dialog

`TmShareDialog` is the `share` dialog of reports and dashboards: a `TmModal` of size `md`, full
screen below `sm`. On open it reads two things in parallel: `{segment}/get { id, include:
['permissions'] }` and, with `permissions.share`, `{segment}/audience { id }` (spec 0033 §6.1).

- **Sharing tab:**
  - *People* (with `permissions.share`): the owner, marked "Owner"; one row per user grant, with
    avatar, name, a Can view or Can edit select and a button named "Remove {name}"; and Add people
    (`tm-entity-multi-field entity="core.User"`).
  - *Roles* (with `permissions.publish`): one row per role grant; Add roles; and the switch
    "Everyone in {tenant}", which sets `isPublic` (view only).
  - *Warnings* come from the audience's per-grant counts and never block. For example: "Omar can't
    read Sales invoice lines and will see an empty report." or "2 of 9 members of Sales can't read
    Sales invoice lines."
  - *Avatars*, here and in the next tab, pass the record key `core.User:{id}` as `colorKey` (spec
    0023 §4.2).
- **"Who can see this" tab.** It shows the audience total. When the server lists members, which it
  does only to a caller who may read roles, a searchable list follows: avatar, name, highest access
  and reasons, such as "Shared by Farah on 12 Oct", "Member of Sales (can edit)", "Everyone",
  "Administrator role permission" or "Through the dashboard Monthly KPIs". A capped list says
  "Showing 2,000 of 2,431 people".
- **Save** applies the staged changes through `TmEntityActions.run`, then re-reads the audience:
  1. one `share { set, remove }`;
  2. when roles or `isPublic` changed, one `publish { set, remove, isPublic }` (spec 0033 §5.4).

  If the `publish` fails, the dialog stays open with the `share` applied and the error shown.
- **Cancel** with staged changes asks "Discard changes?".
- **Close** returns the last `entities` outcome, which the report page's host adopts (spec 0031
  §10.4). The toast "Sharing updated." follows.

## 11. The reports page

`/{t}/reports` is the launcher's one Reports entry (spec 0027 §3.2). It is the
`core.ReportDefinition` search page, with `TmReportsPage` as its results region (spec 0030 §2.5).
Everything else is the search host's.

- **Tabs.** All reports is the default and is absent from the URL. My reports is `tab=mine`, and
  Shared with me is `tab=shared`. `searchRead` sends `reports/browse { scope, query }` (spec 0033
  §5.5). A tab change is a query change: back to page 1, with `replaceUrl`, keeping the selection.
- **Rows.** A `tm-grid` shows the coded rows, then `ctx.rows()`, bound as spec 0030 §7.1 binds the
  page's grid.
- **Columns:** Title (the kind's icon `list`, `sigma`, `grid-3x3` or `chart-column`, a link, and the
  "Standard" or "Built-in" badge), Kind, Root (plural label), Owner (avatar and name) and Modified
  (`tm-relative-time`). The Owner avatar's `colorKey` is the record key (spec 0029 §12.2). A coded
  or standard report shows "—" where it has no root or owner.
- **Coded reports lead page 1** of All and Shared with me: those whose `securable` passes, by title.
  The search text filters them by title and description (`tmSearchNormalize`, spec 0027); any other
  filter hides them.
- **Coded rows** carry negative ids. `rowClass` hides their checkbox, and the page drops them from
  the selection, so counts, bulk actions and the pager see definitions only.
- **Empty states.** All: "No reports yet". Mine: "You haven't created a report yet", with New
  report. Shared: "Nothing has been shared with you yet". No matches: spec 0030 §5.3.
- **Toolbar.** New report is the host's Create (`/{t}/reports/new`), open to every member (spec 0033
  §5.2). Bulk actions derive from the entity actions (spec 0030 §12): Activate, Deactivate, Delete
  and Change owner. The stack has no export, so neither does the page.

## 12. The report studio

The studio edits one definition in a dark panel beside a live preview. One component,
`TmReportStudio`, is the details host's body in both modes, so every save goes through the host.

### 12.1 Routes and hosting

In both modes the details host owns the header, Save, Cancel, drafts, the leave guard, and the
conflict and deleted-record dialogs (spec 0031 §5, §8, §9, §11). The studio writes the host's model
through `injectTmDetails().update()` on every change: titles, descriptions, `code`, `root`, `kind`
and `definitionJson`.

| Aspect | Create: `/{t}/reports/new`, `?root={entity}`, `?clone={id}` | Edit: `/{t}/reports/{id}/studio` |
|---|---|---|
| host | the details host's create mode, with the studio as the `create` hatch (spec 0031 §3.7) | §2.3's `tmPage`: `TmReportStudioPage` renders `tm-details-page` (spec 0031 §3.1) with `TmReportStudio` as its layout |
| source | the entity source | `tmEntitySource` with its `draftKey` replaced by `TmDrafts.key(t, 'core.ReportDefinition/studio', id)`, a key apart from the report page's |
| mode | create | edit: the studio calls `injectTmDetails().edit()` whenever the host is in view mode, `ready` and `can.edit`, on entry and after each save |
| after Save | the host opens the saved report at `/{t}/reports/{id}` | the studio stays, back in edit mode |

- **Query parameters.** `?root=` preselects the root; without it, the Root entity select opens
  focused. `?clone=` arrives through the host's Clone (§2.3), and the studio prefixes each title
  slot with "Copy of".
- **Read-only.** Without `permissions.save`, the host stays in view mode. The panel is inert, and
  the notice "You can view this definition. Save a copy to change it." shows. The host's Clone,
  which is the report's Save as (§2.3), makes the copy.

### 12.2 Layout

| Region | Content |
|---|---|
| top row | below `md`, a Definition / Preview `tm-segmented`; the problems badge (§12.7) |
| panel | a `data-theme="dark"` scope at the inline start: the fields column and the definition column |
| splitter | `tm-splitter` (spec 0023 §7.3): 560 px by default at `xl`, 500 px below; `min` 420, `maxFraction` 0.66, `otherMin` 340; the size lasts as long as the page |
| preview | `--surface-subtle`: a `page`-mode host over `{ draft }` with its own bar, a toolbar (Refresh, the Auto-preview switch, Export) and the output |

- **Fields column.** It is 200 px wide at `xl` and 180 px below, and collapses to a 36 px "Fields"
  rail. It holds the Root entity `tm-select`, which lists readable stacks and child query surfaces
  by plural label ("Sales invoice lines · Sales invoices"), then `tm-path-picker mode="fields"
  draggable` (spec 0029 §11). Changing the root while terms exist asks for confirmation, then
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

- **Chart sections.** Chart type is a grouped `tm-select` of §7.2's types, in the groups
  Comparison, Trend, Part to whole and Other. Options holds legend, stacking, markers, labels,
  palette, min, max and thresholds.
- **Term lists** are `tm-reorder-list` with `accepts: ['tm.path']`. An item shows its grip, label,
  mono expression (`dir="ltr"`) and tags, such as Tree, Auto-expand, Aggregate, Descending or
  "0 decimals".
- **The item editor.** Activating an item expands its editor in place, one at a time. It holds
  Expression, Label per slot, and Display and Format (spec 0033 §3.2's options, with
  `format.calendar` for month, quarter and year). Dimensions add Order, Auto-expand, Tree and
  Attributes. A tree always shows its full depth (spec 0033 §8.6).
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
  - "Compared with Customer" when the compared entity is known.
- **Confirmed types** feed the Queryex drafts' `parameters` (spec 0029 §10.1).

**Example —** Farah types the Filter `Invoice.PostingDate <= @AsOf and Center.Id = @Center`. Two
cards appear at once, `@AsOf` then `@Center`. After the check, `@AsOf` carries the type tag `Date`
and `@Center` reads "Compared with Center".

### 12.5 Drops and "Add to…"

| Target | Takes | Becomes |
|---|---|---|
| List and Summary columns, Rows, Columns, Dimensions | a `tm.path`, at the drop index | `{ path: node.key }`; a navigation shows its display name |
| Measures, the chart's Measure, Summary's "Drop to add its sum" end zone | any field (`accept`) | a numeric field: `{ expr: 'sum(<path>)' }`; any other: `{ expr: 'count(<foreign-key path>)' }` |
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
   `reports/run { draft }`. The server validates the whole document (spec 0033 §8.3). A `422` maps
   by its `pointer`: `/pivot/rows/0` to that item; `/filter` with `start` and `length` to the span;
   `/parameters/1/default` to that card; an unknown pointer to General.

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
  readonly terms: readonly Partial<Pick<TmReportDimension, 'label' | 'label2' | 'label3' | 'labelKey' | 'display' | 'enum' | 'format' | 'expand'>>[]; // one entry per term, in document order
  readonly chart?: { readonly type: TmChartType; readonly options?: TmChartOptions };
}
```

### 12.9 Save, Cancel, Share

Both modes save through the details host (spec 0031 §8), so `reports/save` has one caller.

- **Save and Cancel** are the host's `details.save` and `details.cancel`. Cancel on the edit page
  discards the changes, and the studio re-enters edit mode on the saved definition.
- **"Report saved."** A create's save opens the report page (§12.1), so the studio shows the toast
  "Report saved." (spec 0028 §8.1) as it leaves.
- **Errors.** The studio registers its fields with `registerField` (spec 0031 §4.1): General's
  controls by column, and `DefinitionJson`. It maps `DefinitionJson`'s `422` errors by their
  `pointer` argument into §12.7's list. `409`, `404` and other failures are the host's (spec 0031
  §8.6, §9).
- **Share** (edit page only) opens §10.4's dialog through
  `TmEntityActions.dialog('core.ReportDefinition', 'share', [id])`. A share stamps `ModifiedAt`
  (spec 0033 §5.4), so a later Save meets the concurrency dialog (spec 0031 §9.1). Overwrite keeps
  both changes, since a save never writes shares.

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
  and `text` only for rendered cells.
- **`tm-chart` unit:** spec validation; the adapter's input per type and option, engine-free;
  palette slots, decals and normalized shares; isolates and Arabic-Indic ticks; the table view and
  its row activation; no HTML tooltip or legend configured.
- **Host harness** (fake `TmApi`, fake clock):
  - `get` and `run` together without `p.*`, and in sequence with them; `422` → waiting;
  - one run per commit, the superseded ones aborted;
  - `take` 200 for a list and 1,000 for a summary, and no `long` option on runs and exports;
  - the reloading bar from the commit's frame, held across a superseding run and at least 250 ms
    on screen;
  - §3.4's table, `held`, `stale` and the `forbidden` clear;
  - the calculation error from a pivot cell, an outer subtotal, a summary's totals row and an SQL
    row (empty pointer, no cell), and on a stored report's first run labelled from `document`;
  - page-state restore, the `modifiedAt` discard, and the run after 5 minutes or on an `isStale`
    entry;
  - `card` and `widget` write no URL or preference; `widget` reads no definition.
- **Renderers:**
  - a summary's totals row on a single page, the `totalsOmitted` footer and no notice strip for it
    in `widget` mode, no Total drill under `top`;
  - an instant column's header suffix and `'column'` cells under a device zone that differs;
  - decimal strings formatted exactly beyond 15 digits.
- **Codecs and rules:** `p.{Key}` vectors per type, including a `Numeric` value past 15 digits
  read as a `TmDecimal`; §4.5's precedence and the remembered-value filter; drill filters against
  `drill-golden.json` (one vector per row of spec 0033 §9.4's table, shared with its C# tests); the
  8,000-character refusal.
- **Studio harness:** §12.5's targets and "Add to…"; §12.6's kept blocks; §12.7's span and pointer
  mapping, with pivot measures and a totalled summary's measures sent as `measures` and QX4010
  mapped to its term; overlay edits send nothing; create and edit through the host; a save's `422`
  mapped by `pointer`; edit mode re-entered after Save and Cancel; restore under the studio key.
- **Share harness:** staged sharing as one `share` and one `publish`; a failing second call;
  warnings; a capped audience; members hidden from a caller without role read.
- **Conformance and tokens:** the wire mirrors against spec 0033's JSON Schema; the
  `TM_CHART_SPEC_SCHEMA` golden; §7.7's checks over the `categorical` marks in both schemes.
- **E2e on Acme** (Chromium; Firefox and WebKit `@cross-engine`): §15's Acme journeys; warm launch
  with requests held; every page under the production CSP, with a `securitypolicyviolation`
  listener; RTL at 390, 768, 992 and 1440 px; the geometry specs.
- **Visual, axe and budgets:** `tm-chart` per type and `tm-pivot`'s styles in light, dark and RTL
  (animations off, DPR 1, pinned fonts); axe on every §3.3 state, the table view, each dialog and
  each studio section; §1's budgets and spec 0024 §12's engine line.

## 15. Definition of done

- **Shipped:**
  - §1's five entry points, with their exports, harnesses, budgets, `pivot` and `chart` tokens and
    Arabic strings;
  - §2's declarations merged, with `coded-report-source` checked;
  - §3–§13 implemented, including §13's `report`, `studio` and drill-list `search` contexts;
  - §14's suites green.
- **UI rules:**

| Rule | How the design meets it | Pinning test |
|---|---|---|
| Instant warm launch | report routes draw header and skeleton from cached chunks and descriptors; only `get` and `run` wait; the chart engine loads beside the data; the studio chunk comes from the `on-demand` group, cached after first use | spec 0024's `warm-launch.spec` on `/17/reports/42`, `/17/reports/acme-credit-exposure` and `/17/reports` with every API request held: chrome and skeleton within 1 s, no shift to the loaded page |
| Instant feedback | a commit dims the output and starts the progress bar in the same frame; pivot toggles and the table view are local; presentation edits send nothing; a drop shows its item at once | `report-feedback.spec` (a frame probe after a commit, a toggle and a drop); `studio-overlay.spec` (no request for label and option edits) |
| Non-blocking | one `resource()` per host; superseded and abandoned runs aborted down to SQL; Cancel after 2 s; the bar never disabled; preview runs aborted per edit; writes never aborted | `run-supersede.spec` (three commits: one applied, two aborted); spec 0033 §15's `runs` suite ("an abort reaches SQL") for the server half |
| Stable layout | a fixed bar row; states inside the output box; the notice strip as an overlay; a dimmed held output; a fixed card height; data-independent widths; stable gutters; a fixed chart frame | `report-geometry.spec` (header, bar and output boxes equal across first load, reload, error, no data and a language switch); `pivot-geometry.spec` (expand, collapse) |
| RTL | logical properties in tables, pivot and studio; numbers physically right; expressions as LTR isolates; the plot unmirrored (§7.6) | every e2e in `ar` at four widths; `pivot-rtl.spec`; `chart-rtl.spec` (legend side and order, isolates, Arabic-Indic ticks) |
| WCAG AA | §6.4's keyboard model; §7.8's table view; §12.5's "Add to…"; named remove buttons; a run never moves focus, and drill-back restores the active row or cell | `report-keyboard.spec` (open, set every parameter type, drill from each kind, export, share); `studio-keyboard.spec` (build and save a pivot with "Add to…" and the reorder handle); axe as in §14 |

- **Telemetry** (closed property values, spec 0026 §3.2):

| Event | Kind | Properties |
|---|---|---|
| `tm.report.run` | timing | `kind`; `source` (`stored`, `coded`, `draft`); `mode`; `trigger` (`open`, `arguments`, `refresh`, `restore`, `silent`); `outcome` (`ok`, `empty`, `waiting`, `error`, `cancelled`) |
| `tm.report.engine-loaded` | timing | `cached` |
| `tm.report.drill` | event | `from` (`summary`, `pivot`, `chart`), `newTab`, `outcome` (`ok`, `too-long`) |
| `tm.report.open-record` | event, sampled | `child` |
| `tm.report.export` | event | `kind`, `mode` (`sync`, `background`), `outcome` |
| `tm.report.arg-dropped` | event | `reason` (`invalid`, `unknown`) |
| `tm.report.restored` | event | `stale` |
| `tm.report.share-saved` | event | `users` and `roles`, bucketed `0`, `1`, `2-5`, `6+`; `public` |
| `tm.studio.preview` | timing | `kind`, `outcome` (`ok`, `invalid`, `error`, `cancelled`) |
| `tm.studio.field-added` | event | `via` (`drag`, `menu`) and `target` |
| `tm.studio.kind-switched` | event | `from` and `to` |
| `tm.studio.draft-restored` | event | none |

- **Acme** adds `creditExposureReport` (§2.2) and its Customer details card (§2.1). Its stored
  report is the seed `acme.sales-by-center` (spec 0033 §13). The e2e journeys: open each report;
  change parameters; drill a pivot leaf cell and check the drilled rows sum to it; build and save
  a summary over Sales invoice lines, drill a row and open a line on its invoice (`?child=`); go
  back; export each kind; share with a user; build a pivot by drag and by keyboard and save it.

## Decisions record

1. **One host, four modes** — a page, a card, a tile and the assistant share one lifecycle — (§3).
2. **Renderers read only `ReportResult`** — coded reports render, drill and export for free — (§3.5).
3. **Coded reports declare `route`; `run` and `component` are hatches; defaults live in C#** — export
   needs the route, and the client cannot evaluate `today()` — (§2.2).
4. **The first run leaves with the definition read** — no serial round trip per open — (§3.2).
5. **Arguments apply on commit, not per keystroke or by a Run button** — Refresh re-runs — (§4.3).
6. **The URL holds supplied values and omissions only** — links re-evaluate `today()` — (§4.4).
7. **`spinner` dims, `clear` clears, and in `silent` mode only a plain `run()` stays silent** —
   nothing jumps — (§3.4).
8. **`tm-pivot` takes a member tree and a cell-text callback** — no report knowledge — (§6.1).
9. **Compact rows, subtotals after children, four styles** — as the workbook does — (§6.2, §6.3).
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
15. **Chips come from navigation state or discover, never URL text** — links choose rows — (§8.4).
16. **Export: one action per renderer, synchronous first, background on 413** — (§9).
17. **The report page is a details hatch with `edit: 'page'`; the studio is the details host's body
    in create mode and on its own edit page** — one save path, drafts and conflicts; edit mode never
    enters the report's URL — (§2.3, §12.1).
18. **Share changes are staged into one `share` and one `publish`** — no partial states — (§10.4).
19. **A summary pages at 1,000 rows and shows its totals row only when one page holds the whole
    result** — the total then covers exactly the rows shown, and every summary with `top` fits —
    (§5.1, §5.3).
20. **A calculation error names the measure and the cell** — the author learns where to guard the
    measure with `if`, without reading logs — (§3.3).

## Review flags

1. **The drill-through list is the search host in list mode** (§8.3). Alternative: a dedicated
   read-only list page without the search toolbar. Flips if users want a toolbar-free drill view.
2. **The plot never mirrors in RTL** (§7.6). Alternative: reversed category axes. Flips if Arabic
   users read chart time right to left in testing.
3. **The reports page opens on All reports** (§11). Alternative: My reports. Flips if
   administrators find All cluttered; it lists every definition to them.
4. **Coded reports lead page 1** (§11). Alternative: a fourth "Built-in" tab. Flips past about 20
   coded reports.
5. **Remembered parameters skip defaulted ones** (§4.5). Alternative: remember all, or none. Flips
   if users expect a typed "As of" date to stick.
6. **Normalized stacks are computed by the facade** (§7.2). Alternative: refuse them, or server
   shares. Flips if presentation math is ruled aggregation.
7. **A drill URL over 8,000 characters is refused** (§8.2). Alternative: navigation state or a
   server drill token. Flips if real filters reach that size.
8. **No header sorting in report tables** (§5.4). Alternative: an order override on `run`. Flips if
   users sort report tables more than they drill.
9. **Share edits are staged** (§10.4). Alternative: apply each change at once.
10. **The preview runs 600 ms after an edit** (§12.7). Alternative: manual preview. Flips if
    authoring loads production databases noticeably.
11. **The studio edits on its own route** (§12.1), and the report page declares `edit: 'page'`.
    Alternative: the studio as the report page's edit mode, as dashboards do, with no `/studio`
    route. Flips on preferring one page per report.
12. **Delete confirms without the report's reach** (§10.3). Alternative: read the audience first and
    add "It's shared with 3 people and 2 roles", which needs a confirmation hook in spec 0031 §10.2.
    Flips if users delete shared reports by mistake.
13. **Summaries page at 1,000 rows** (§5.1). Alternative: keep 200 and show the totals row less
    often. Flips if 1,000-row pages measure slow on phones.
