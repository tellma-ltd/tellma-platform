# Spec: Search Page

- **Author:** Ahmad Akra
- **Date:** 20 October 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Every entity a distribution declares gets a search page: a grid or tiles over its rows, a search
field, ordering, column and advanced filters, a column editor, selection and bulk actions, paging,
export and import, and a tree view for tree entities. A distribution writes a declaration, never a
page: one host component renders every entity from its descriptor.

The page is built for speed and the keyboard, and its address bar always reproduces it. The same
host also serves the entity pickers' advanced search and the reports' drill-through list.

Other specs that carry parts of this design:

- spec 0015 §5.2, §5.4 — one leaf per `Filters` entry, the default order, `IncludeRoots` and the
  `MaxTake` cap.
- spec 0016 §3.3, §3.5 — the wire records.
- spec 0019 §2.1, §4.1 — `ExportColumn`, display-name export columns, `Filters` on the export source.
- spec 0010 §C.1–§C.10 — row activation, the driver, sort, header actions, trees, menus.
- spec 0010 §D.2, §D.3 — drawers, popover sizes.
- spec 0024 §9.5 — the URL codec.
- spec 0025 §10, §12, Appendix A — page state, tree expansion memory, the `search.*` preference keys.
- spec 0023 §4.4, §6.2 — the progress bar's hold; the search field's leading and trailing edges.
- spec 0029 §3, §6, §12.1, §13 — the declaration members' shapes, select plans, the zone suffix,
  filter controls.
- spec 0018 §6.3 — Acme's development data: about 60 customers, a 40-node center tree.

## Goals / Non-goals

**Goals**

- One host renders every entity's search page; the common case is a few declaration lines.
- Chrome from cached metadata on a warm launch, feedback in the frame of every key, and one
  cancellable read per query that never blocks.
- A URL that reproduces the page, and a return from a record that restores it with no request.
- Keyboard-only use end to end: a key typed anywhere lands in the search field, and the arrows and
  Enter act on the results from there.
- Full RTL and WCAG AA; trees that restore in one call.

**Non-goals (explicitly out of scope)**

- The details page and its paging through results (spec 0031); this spec hands it a snapshot (§15).
- The reports page body, the drill-through crumb and chips (spec 0034).
- The attention pages (spec 0028), toast copy and policy (spec 0028 §8), and the stock pages'
  declarations (spec 0032).
- Filter controls, the expression editor, the path tree picker, select plans and display names
  (spec 0029).
- Generic components (spec 0023) and changes to existing ones (spec 0010).
- **Deferred:** actions over "all N matching rows" (`delete-by-query`, by-query bulk actions);
  multi-column ordering; persisted column widths (the grid's in-memory store keeps them, spec 0004
  §12); saved views; an errors workbook for imports; a custom-filter component hatch.

## 1. Placement and vocabulary

The search page is the lazy route entry point `@tellma/core/search`. The declaration types it owns
live in the eager primary entry point (`@tellma/core`) as types only, so declarations and spec 0031
can name them.

Contract blocks are TypeScript and C# sketches: names and shapes are normative; imports,
documentation comments, bodies and cancellation tokens are omitted.

| Part | Exports | Loaded |
|---|---|---|
| host | `TmSearchHost` (`tm-search-host`), `injectTmSearch`, `TmSearchContext`, `TmSearchCell` | with the route (spec 0024 §4.4) |
| picker page | `TmSearchPickerPage` | by the picker binding's `import()` (spec 0029 §14.4) |
| import result | `TmImportResultView` (§13.3), also used by spec 0032 §11 | with the route |
| column editor, filters popup | internal | `@defer (on interaction)` from their buttons |
| import dialog | internal `TmImportDialog` | `import()` on the Import action |
| primary, types only | `TmTileDeclaration`, `TmCustomFilterDeclaration`, `TmSearchPageActionDeclaration`, `TmColumnPreference`, `TmSearchSnapshot` | zero bytes |

- **Folder** `client/projects/core/tellma-core/search/`, importing the primary and `/entity`.
- **Budget.** The route chunk's own weight is at most 16 KB gzip, and each deferred chunk's at most
  6 KB; each is its own budget line. The whole route stays inside spec 0024 §12's 110 KB.
- **Cached chunks.** Every chunk is in the service worker's `shell` group (spec 0024 §5.3), so a
  chunk deferred "on interaction" loads from the cache.
- **Testing exports** (`@tellma/core-testing`): `TmSearchPageHarness`, `TmColumnEditorHarness`,
  `TmImportDialogHarness`.
- **Strings and telemetry** use the scope and area `tm.search`.

**Vocabulary**

- *Chrome* — everything on the page but the rows: the title, toolbar, column headers, filter
  buttons, pager and view toggle.
- *Query state* — the resolved `TmSearchQuery` (spec 0024 §9.5) the page shows.
- *Canonical query* — the codec's URL query with defaults omitted (§4.3). It identifies a result.
- *Result* — the rows that answer a canonical query; in a search tree, also their ancestors.
- *Held rows* — the rows on screen from the last result. They stay while a new read runs.
- *Highlight* — the active row of the grid or the tiles (`activeRowId`, spec 0010 §C.2).
- *Target* — where a row opens: its `rowLink`, else its details page (§7.2).
- *Leaf* — one entry of a request's `Filters` (§5.1).
- *Fixed leaf* — a leaf the user neither sees as text nor removes: the picker's filters and the
  drill-through filter.
- *Browse tree* — a tree entity in tree view with no search text and no filter.
- *Search tree* — a tree entity in tree view with search text or a filter.
- *Context row* — in a search tree, an ancestor shown only because a descendant matched.
- *Leading group*, *trailing group* — the toolbar controls at the start of row 2 (after the tabs)
  and at its end (§3.1).
- *Page state* — the per-tab memory of spec 0025 §10, where this page keeps a snapshot (§15).
- *Mode* — `page`, `picker` (a picker's advanced search) or `list` (the drill-through list), §17.

## 2. What a distribution writes

The common case is one declaration. Spec 0029 §3 owns the members' shapes; the subsections below
give their meaning. Two members are covered with the feature they drive: `pageActions` (§3.1) and
`rowLink` (§7.2).

**Example —** the search members of Acme's customers declaration (spec 0029 §3 shows its launcher
and picker members):

```ts
export const customers = tmEntity('acme.Customer', {
  columns: ['Code', 'Name', 'CustomerType', 'Currency',
            { path: 'CreditLimit', display: () => import('./credit-cell') }, 'IsActive'],
  orderBy: 'Code',
  tile: { image: 'Image', title: 'Name', subtitle: 'Code', badge: 'IsActive' },
  filters: [{ key: 'mine', labelKey: 'Acme_Customer_CreatedByMe', queryex: 'CreatedById = me()' }],
  details: () => import('./customer-details'),
});
```

### 2.1 `columns` and `orderBy`

These two members set the columns a user sees first and the order the rows take until the user
picks one.

- **`columns`** is the default column list of the search page, the picker's advanced search, the
  export and the drill-through list. Each entry is a column key (spec 0029 §6.1): a path, a group
  anchor, a navigation (shown as its display name), or a declared `{ expression, labelKey, type? }`.
- **Without `columns`,** the list is the display-name columns (spec 0029 §5.1), the other natural
  keys, then `IsActive`.
- **An object entry `{ path, … }`** takes these options:
  - `width` fixes the width in px; without it, `tmGridColumnFor` (spec 0029 §12.1) sizes by type.
  - `select` names extra paths that a `display` cell or `rowLink` reads.
  - `filter` concerns editable cells only (spec 0029 §14.5).
- **`orderBy`** (`'Code'`, `'Name:desc'`) applies while the user has chosen no order. Without it,
  the metadata's `defaultOrderBy` applies. Either default shows no header indicator (§7.3).

### 2.2 The cell hatch: `{ path, display }`

A column whose cell needs custom rendering names a lazy component. The host loads the `display`
components of the shown columns in parallel with the data and renders each in `*tmGridDisplay`.

```ts
// @tellma/core/search
@Directive()
declare abstract class TmSearchCell<V = unknown> {
  readonly value: InputSignal<V>;                          // TmColumnPlan.value(row), spec 0029 §6.2; a tile: the row id
  readonly row: InputSignal<TmPlanRow>;
  readonly column: InputSignal<TmColumnPlan | undefined>;  // undefined in a tile (§2.3)
}
```

**Example —** §2's credit cell, a bar beside the limit:

```ts
@Component({ selector: 'acme-credit-cell', imports: [TmProgressBar],
  template: `<tm-progress-bar [value]="percent()" [label]="text()" />` })
export default class CreditCell extends TmSearchCell<TmDecimal> { /* percent, text: computed from value() through TmDecimals */ }
```

- **Rules.** A cell is static: no focusable content but a link, one line at the fixed row height,
  no fetching, and only selected paths read (`select`, §2.1).
- **Tiles never use `display`.** A custom tile body is `tile`'s `{ component }` (§2.3).

### 2.3 `tile`

`tile` maps entity paths to the parts of a tile: an image and four text corners. Instead, it may
name a component that draws the whole tile body.

```ts
// @tellma/core primary (types only)
type TmTileDeclaration<N extends string> =
  | { readonly image?: TmPath<N>; readonly title?: TmPath<N>; readonly subtitle?: TmPath<N>;
      readonly topEnd?: TmPath<N>; readonly badge?: TmPath<N> }  // the image and the four corners
  | { readonly component: TmLazyComponent };                      // a TmSearchCell tile body
```

| Member | `TmTileLayout` member (spec 0023 §8.5) | When `tile` is absent | Renders |
|---|---|---|---|
| `image` | `image` | the first image navigation (spec 0029 §4.3), else none | the thumbnail through `TmUrls.blob` (spec 0024 §9.4) |
| `title` | `title`, `href` = the row's target | the display name | a navigation or group: its display name; else `TmFormats.text` |
| `subtitle` | `bottomStart` | the first natural key the display name does not show | `TmFormats.text` |
| `topEnd` | `topEnd`, numeric for number types | none | `TmFormats.text` |
| `badge` | `bottomEnd`, a badge | `IsActive` when activatable | by type, below |

- **The badge** renders `IsActive` as "Active" (success) or "Inactive" (neutral), another bool as
  its label when true, and an enum as the value's label (neutral).
- **`image`** may name the navigation (`Image`) or its key (`ImageId`).
- **Tile paths** join the select (§5.1). A bad one is `unknown-path` (spec 0029 §3).
- **`{ component }`** replaces the tile body at the same size (`tmTileContent`). The component
  extends `TmSearchCell`.

### 2.4 `filters` and `tabs` (URL `x.{key}`, `tab=`)

Custom filters are declared predicates the user turns on in the Filters popup. Tabs are declared
views of the entity, one at a time. Each writes its own URL parameter and adds at most one leaf.

```ts
// @tellma/core primary (types only)
type TmCustomFilterDeclaration =
  | { readonly key: string; readonly labelKey: string; readonly queryex: string }  // a switch: on → x.{key}=1
  | { readonly key: string; readonly labelKey: string;                             // a radio group: x.{key}={value}
      readonly options: readonly { readonly value: string; readonly labelKey: string; readonly queryex: string }[] };
// tabs (spec 0029 §3): readonly { key: string; labelKey: string; queryex?: string }[]
```

- **Keys** of filters, options and tabs match `^[a-z][a-z0-9-]*$`.
- **A switch** applies its `queryex` while `x.{key}=1`.
- **Options** apply the named option's `queryex`. An unknown value is dropped.
- **Tabs** are a `tm-segmented` at the start of row 2 (§3.1).
  - The first tab is the default and is omitted from the URL. `tab={key}` selects another, and an
    unknown key falls back to the first.
  - A tab's `queryex` is a leaf. A tab without one adds none; it serves a page that reads the tab
    in `searchRead` (§2.5).
- **Leaves from code.** Custom filters and tabs come from code, so a `query-invalid` on one of
  their leaves is a distribution bug (§5.3).

**Example —** an options filter on customers, and tabs on sales invoices:

```ts
filters: [{ key: 'credit', labelKey: 'Acme_Customer_Credit', options: [
  { value: 'none', labelKey: 'Acme_Customer_Credit_None', queryex: 'CreditLimit = 0' },
  { value: 'high', labelKey: 'Acme_Customer_Credit_High', queryex: 'CreditLimit >= 500000' } ] }],
// → /17/customers?x.credit=high
tabs: [{ key: 'all', labelKey: 'Acme_SalesInvoice_TabAll' },
       { key: 'open', labelKey: 'Acme_SalesInvoice_TabOpen', queryex: 'IsClosed = false' }],
// → /17/sales-invoices?tab=open
```

### 2.5 The `search` hatch and `searchRead`

A page whose results need a custom body names a `search` component, `search: () => import(…)`. It
replaces the results region (row 3, §3.3) and nothing else.

- **The host keeps the rest:** rows 1 and 2, the URL, page state, preferences, page context and page
  actions. Only the view toggle and Columns go away.
- **The component drives the page** through `injectTmSearch()`. Cells may read the same context,
  and page actions receive it (§3.1).

```ts
// @tellma/core/search
declare function injectTmSearch(): TmSearchContext;
interface TmSearchContext {
  readonly mode: 'page' | 'picker' | 'list';                // §17
  readonly descriptor: TmEntityDescriptor;
  readonly query: Signal<TmSearchQuery>;                     // the query state (§4)
  readonly plan: Signal<TmSelectPlan>;                       // spec 0029 §6.2: columns ∪ tile and tree paths
  readonly rows: Signal<readonly TmPlanRow[]>;
  readonly ancestors: Signal<readonly TmPlanRow[]>;          // a search tree's ancestors (§16.4)
  readonly total: Signal<number | null>;
  readonly totalIsLowerBound: Signal<boolean>;               // a capped count, "10,000+" (§14)
  readonly status: Signal<'loading' | 'ready' | 'error'>;
  readonly error: Signal<TmApiError | null>;
  readonly selection: Signal<ReadonlySet<TmId>>;             // selected row ids (§12.1); TmId: spec 0024 §10.1
  readonly highlight: WritableSignal<TmId | null>;           // the highlighted row's id
  select(ids: Iterable<TmId>, mode: 'add' | 'remove' | 'replace'): void;
  update(patch: Partial<TmSearchQuery>): void;               // a query change under §4.4's rules
  refresh(): void;                                           // a re-read (§13.4)
  open(id: TmId, options?: { newTab?: boolean }): void;      // opens the row's target (§7.2)
  url(id: TmId): string | null;                              // the target's URL; null when there is none
}
```

**`searchRead(request, query)`** is platform-internal. It sends the page's rows read to another
route: given §5.1's body and the query state, it returns `{ route, body }`. Such a page has no tree
view, exports the selection only, and offers the details page no cross-page paging (§15.1).

**Example —** spec 0034 §11's reports page reads `reports/browse { scope, query }` through
`searchRead`, choosing the scope by `query.tab`. Its `search` component lists the coded reports,
then `ctx.rows()`, and opens a definition with `ctx.open(id)`.

## 3. Page anatomy and layout

The page is spec 0023's `tm-page-header` (rows 1 and 2) over a results region (row 3) that fills
the rest of `main`. The route host's `search` skeleton (spec 0024 §4.4) draws the same geometry, so
the page replaces it with no shift.

### 3.1 Rows 1 and 2

Row 1 holds the title, the search field and the Filters button. Row 2 is the toolbar: the tabs and
the leading group at the start, the trailing group at the end.

| Slot (spec 0023 §7.2) | Content, in DOM and visual order |
|---|---|
| `[tmPageBack]` | list mode only: the crumb (§17) |
| `[tmPageTitle]` | the `h1`: the entity's plural label (`TmEntities.label(name, true)`) |
| `[tmPageHeaderEnd]` | the search field (§6), the Filters button (§10) |
| `[tmPageToolbar]` | the tabs (§2.4); then the leading group: Create (§13.1), Export (§13.2), Import (§13.3), the declaration's page actions |
| `[tmPageToolbarEnd]` | the trailing group: Columns (§11), Refresh (§13.4), the pager and page size (§14), a separator, the view toggle (§3.4), the Tree/List toggle (§16.1) |

While rows are selected, the selection bar (§12.2) replaces the leading group.

Each `pageActions` entry becomes a page action in the page's scope (spec 0026 §9.4). Where it
shows depends on its `group` and `selection`:

- **`primary`:** a button in the leading group, after Import.
- **`menu`:** an item of a "More" menu at the end of the leading group.
- **`selection: true`:** an item of the selection bar's Actions menu instead (§12.2).

```ts
// @tellma/core primary (types only)
interface TmSearchPageActionDeclaration {
  readonly id: string;                          // spec 0026 §9.3's grammar: 'attention.notifications.read-all'
  readonly labelKey: string;
  readonly icon?: string;
  readonly group?: 'primary' | 'menu';          // default 'primary'
  readonly order?: number;
  readonly destructive?: boolean;
  // spec 0026 §9.4's shape; required when destructive
  readonly confirm?: { readonly titleKey: string; readonly messageKey: string; readonly args?: Signal<TmJsonObject> };
  readonly requires?: TmSecurableRef;           // shown only while TmPermissions.can(resource, action)
  // true: an Actions-menu item over the selection (§12.2); group is then ignored
  readonly selection?: boolean;
  // Runs in the host's injection context and refreshes what it changes.
  // ids: the selection when selection is true, else [].
  run(search: TmSearchContext, ids: readonly TmId[]): Promise<void>;
}
```

**Example —** spec 0028 §10.2's Notifications page opens each row through spec 0028's open route
and marks the selected rows read:

```ts
rowLink: (row) => inject(TmUrls).page(`notifications/${row.id}/open`),
pageActions: [{ id: 'attention.notifications.read', labelKey: 'tm.attention.markRead', icon: 'check', selection: true,
  run: async (search, ids) => { await inject(TmApi).write('inbox/read', { ids }); search.refresh(); } }],
```

### 3.2 Responsive behaviour

Breakpoints are spec 0010 §B.1's. Four elements change form with the width; the others keep theirs.

| Element | `xs` (< 576) | `sm` | `md`, `lg` | `xl`, `xxl` |
|---|---|---|---|---|
| search field | fills row 1 after the title, at least 160 px | 240 px | 240 px | 240 px; 320 px at `xxl` |
| Create, Export, Import, page actions | a `+` icon button; the rest in one `…` menu | labelled ("New customer") | labelled | labelled |
| Filters, Columns, page size | icons; no page size | icons; no page size | Filters labelled; Columns an icon below `lg`; "50" | labelled; "50 per page" |
| default view | tiles | tiles | grid | grid |

- **Fixed geometry.** Each row has a fixed block size, and row 2 is `nowrap`.
- **Labels by width only.** Labels show by container width (spec 0010 §B.1, `tm-segmented
  labels="xl"`), never by content. So a selection, a filter or a loading state never changes a width.
- **Below `sm`,** controls are 46 px (`lg`, spec 0010 §B.6), and every popup is a full-width end
  drawer (`TmModal` `placement: 'end'`, spec 0010 §D.2).
- **Reserved space.** The selection bar swaps the leading group's content in place, so the tabs and
  the trailing group never move. One exception: below `sm`, Columns (the trailing group's first
  item) hides while rows are selected. The Filters button reserves room for a two-digit
  `tm-counter` (spec 0023 §4.1).

### 3.3 Row 3: the results region

Row 3 shows the rows, as a grid or as tiles, or a state in their place.

- **Grid** (`tm-grid`; `tm-tree-grid` in tree view): full bleed, frozen header, zebra rows,
  `size: 'sm'`, the checkbox column first, no row headers, widths never data-dependent, a stable
  scrollbar gutter (spec 0010 §C.7). Its scroller is the page's only scrollbar.
- **Tiles** (`tm-tiles`) fill the region and scroll inside (§8). **States** (§5.3) replace the rows
  inside the region; rows 1 and 2 never move.
- **Loading.** An indeterminate `tm-progress-bar` overlays the region's top edge.
  - It turns `active` when a read starts. For text, a read starts when the field fires `search`
    (§6.1).
  - It stays active while the result is stale (§6.3), so it never blinks between keys.
  - Spec 0023 §4.4's 250 ms hold keeps a fast answer from flashing it.
  - `aria-busy` is set while a read runs.

### 3.4 Grid or tiles

The user switches between grid and tiles at any time. The choice lives in the URL and in one
preference for every width.

- **Toggle.** A `tm-segmented` with Grid (`table-2`) and Tiles (`layout-grid`), as spec 0023 §6.1's
  example. A change writes `view` to the URL (§4.4). It also writes the preference
  `search.<entity>.view`, whose value `{ view, list }` keeps its `list` half (§16.1).
- **Default and preference.** The width picks the view (§3.2) until the URL or the preference names
  one. The preference then holds at every width.
- **No request.** The select already holds the tile paths (§5.1). A toggle re-renders the held rows,
  and the highlight, selection and page carry over.

## 4. Query state: meaning, precedence, URL, history

The page's state is one `TmSearchQuery`, parsed and printed by spec 0024 §9.5's codec. This section
says what each field means, which source wins when the page opens, and how changes reach the URL
and history.

### 4.1 Fields

Each field has a URL parameter, a code default that the URL omits, and a part of the request it
drives.

| Field (URL) | Meaning | Code default, omitted from the URL | Request (§5.1) |
|---|---|---|---|
| `tab` | the declared tab (§2.4) | the first tab | the tab's leaf |
| `q` | search text, trimmed, at most `limits.maxSearchLength` | empty | `search` |
| `columnFilters` (`f.{key}`) | one filter per column key, spec 0029 §13.2's operators | none | one leaf each |
| `filter` | the advanced Queryex predicate (§10) | none | one leaf |
| `inactive` (`inactive=1`) | include inactive rows, on activatable entities | false | `includeInactive` |
| `custom` (`x.{key}`) | custom filters (§2.4) | none | one leaf each |
| `order` (`order=Name:desc`) | one column key and a direction | `orderBy`, else `defaultOrderBy` | `orderBy`, then `Id` |
| `cols` | path column keys (§4.3) | the declaration's `columns` | the select |
| `page`, `size` | 1-based page; page size | 1; 50 | `skip`, `take` |
| `view` | `grid` or `tiles` | by width (§3.4) | — |
| `list` (`list=1`) | a tree entity shown flat | false | the call (§16) |

- **Drops.** The host drops any value the entity cannot have. Every drop rewrites the URL (§4.3).
  - `inactive` on an entity that is not activatable, and `list` on one that is not a tree;
  - an unknown tab, custom key or option value;
  - a column key that does not resolve or is unreachable (spec 0029 §6.4), from `cols`, `order`
    and `f.`;
  - an `f.` value its control rejects (`tmFilterLeaf` returns null).

  The last two emit `tm.entity.column-dropped { source: 'url' }`.

### 4.2 Precedence when the page opens

When the page opens, the host takes the query state from the first of these sources that applies.

1. **A superseded URL.** When this page's snapshot has a `supersedes` equal to the URL's canonical
   query, the snapshot applies whole and the URL is replaced with its canonical form. Spec 0031
   §12.2 writes such snapshots when it pages past an edge.
2. **The URL.** When the URL carries any query-state parameter, it defines the whole query; its
   absent fields take steps 4 and 5.
3. **Page state.** Otherwise this page's snapshot (§15) applies its query whole.
4. **Preferences** for `cols`, `size`, `view` and `list` (§11.4, §14, §3.4).
5. **Code defaults** (§4.1).

What else the snapshot restores (selection, expansion, rows) is §15.2's.

**Example —** three ways to open Centers:

| Opened from | URL | Applies |
|---|---|---|
| the pinned bar, after searching "jed" earlier in this tab | `/17/centers` | page state: `q=jed`, its rows and scroll; no request |
| a pin with a filter | `/17/centers?f.CenterType=in:Service` | the URL: that filter, empty `q`, columns from the preference, selection from page state |
| a colleague's link | `/17/centers?q=jed&cols=Code,Name,Parent&page=2` | exactly that, whatever the reader's preference |

### 4.3 The canonical URL

The address bar always shows the canonical URL of the query state, so a copied URL reproduces the
page.

- **Written after every resolution.** The host compares `TmUrls.search(entity, state)` with the
  address bar and replaces the URL when they differ.
- **List mode** keeps the list path `/{t}/list/{segment}` and its fixed `filter` in the canonical
  URL (§17).
- **Pins and recents.** Pins store canonical strings (spec 0027 §8.2); recents keep the path only
  (spec 0025 §13).
- **`cols`** is written only when the shown columns differ from the code default and include no
  expression column. Expression columns, declared or the user's, never travel in a URL (spec 0029
  §6.1). So a URL with `cols` shows path columns only, and a URL without it shows the preference's
  columns, expressions included.
- **`view`** is written when it differs from the width default. It is recomputed when a resize
  crosses `md`.
- **Picker mode** never touches the URL (§17).

### 4.4 Changes and history

Every query change reaches the URL. Only a page change pushes a history entry; the rest replace
the current one.

| Change | URL | History | Page | Preference written |
|---|---|---|---|---|
| text (each `search` emission, §6.1) | yes | replace | → 1 | — |
| tab, column filter, advanced filter, Active only, custom filter, Clear all | yes | replace | → 1 | — |
| order | yes | replace | → 1 | — |
| columns (§11) | yes | replace | kept | `columns` |
| page | yes | **push** | — | — |
| page size | yes | replace | the page holding the first visible row (§14) | `pageSize` |
| view, list | yes | replace | kept; tree ↔ list → 1 | `view` |
| selection, highlight, expansion, scroll | no | — | — | — (page state; expansion memory, §16.3) |

- **Writes go through the router** (`router.navigate([], { queryParams, replaceUrl })`). A
  query-only navigation reuses the host and never moves focus (spec 0024 §4.4).
- **Back and Forward.** The URL's query may change without the host writing it: it then differs
  from the last canonical string the host wrote. The host re-resolves from §4.2's steps 1 and 2
  and loads, so Back steps through pages.
- **Opening a row** pushes the target with router `state: { tmFrom: <page-state key> }`. Angular
  keeps it in `history.state` across a reload, for spec 0031 §12.

## 5. Loading results

The page makes one read per resolved query and cancels it the moment it is superseded or the page
is left. The previous rows stay on screen until the new ones land.

### 5.1 The request

The host builds one request body from the query state (§4.1's last column).

- **Route.** `{segment}/query` through `TmApi.read(route, body, { signal })`, or `searchRead`
  (§2.5). A browse tree calls `get-by-parent-ids` instead (§16.2).
- **Select** = `tmSelectPlan(descriptor, columns ∪ tile paths ∪ tree keys)` (spec 0029 §6.2). `Id`
  comes first, and dropped keys are never sent. The tree keys are `ParentId`, `SubtreeCount` and,
  on an activatable tree, `ActiveSubtreeCount` (spec 0012 §2.1).
- **Filters.** Each source adds its own leaf, in the order below. The order lets §5.3 map a
  diagnostic back to its control.

| Leaf | Built by | Present when |
|---|---|---|
| the tab's `queryex` | the declaration | the tab has one |
| the advanced filter, verbatim | the user | `filter` is not empty |
| custom filters, in declaration order | the declaration | `x.{key}` resolves |
| column filters, in key order | `tmFilterLeaf` (spec 0029 §13.1) | `f.{key}` is valid |
| fixed leaves | `picker.filter` and the field's `filter` (spec 0029 §14.2); the drill-through `filter` | picker or list mode |

- **Order.** The order column's `TmColumnPlan.orderBy(direction)`, which reads the active language
  slot. With no order column, the default order (§2.1). Then `, Id` is appended unless the order
  already ends in `Id`, so paging is stable.
- **Paging.** `skip = (page − 1) × size`, `take = size`, `includeCount: true`.
- **Flags.** `includeInactive` from `inactive`; `includeAncestors: true` in a search tree (§16.4).

**Example —** Customers on an `en` + `ar` tenant, for a user reading Arabic:
`/17/customers?q=al&f.CustomerType=in:Corporate&filter=CreditLimit%20%3E%20100000&x.mine=1&order=Name`

```json
{ "select": "Id,Code,Name,Name2,CustomerType,Currency.Id,Currency.Name,Currency.Name2,CreditLimit,IsActive,ImageId,Image.Variants",
  "search": "al",
  "filters": ["CreditLimit > 100000", "CreatedById = me()", "CustomerType in ('Corporate')"],
  "orderBy": "coalesce(Name2, Name), Id", "skip": 0, "take": 50, "includeCount": true }
```

`ImageId` and `Image.Variants` come from the tile (§2.3); the order reads slot 2 because the active
language is Arabic (spec 0029 §6.1).

### 5.2 Lifecycle

One Angular `resource()` owns the read, from start to abort.

- **Key and signal.** The resource's key is the canonical query plus a refresh generation. Its
  `abortSignal` is the read's signal.
- **Aborts.** A new key aborts the read in flight. Leaving the page aborts it through the host's
  `DestroyRef`. The abort reaches the server (spec 0024 §10.5).
- **Never blocked.** Typing, filtering, sorting, paging, toggling and navigating stay available
  while a read runs. `TmApi` retries (spec 0024 §10.5) while the region stays in its loading state.
- **On success** the host builds the rows (`plan.rows(set)`) and seeds the highlight (§6.3). It
  also feeds `TmDisplayNames.remember(entity, set)` (spec 0029 §5.3), so a details title renders
  at once.
- **Announcements.** One polite live region announces "55 results", "No matches" or the error
  state's heading. It speaks once a result settles and no key was pressed for 500 ms, never once
  per keystroke.

### 5.3 States of the results region

Each state is a `tm-empty-state` (spec 0023 §5.3) with `live="polite"` inside the region. The table
holds the page's own states; any other read failure renders spec 0024 §11's error view
(`tm-error-state`), with that section's copy and actions.

| State | When | Shows | Actions |
|---|---|---|---|
| first load | no rows held | skeleton rows at row geometry (12 grid rows, or skeleton tiles); headers, checkboxes and filter buttons already drawn | — |
| reloading | rows held, the result stale (§6.3) | the held rows and the progress bar (§3.3) | all stay live |
| no data | 0 rows; no text, no column, advanced or custom filter, inactive rows excluded | `inbox`, "Nothing here yet", "Add one to get started." | New, when allowed |
| no matches | 0 rows otherwise | `search-x`, "No matches", "Clear the filters or widen the search." | Clear filters when any applies, else Clear search |
| invalid filter | `query-invalid` on the advanced filter | `circle-alert`, "This filter expression is not valid", the first diagnostic's message with its token in a mono chip | Edit filter (§10.2) |

- **Diagnostics map to their leaf** by §5.1's order:
  - on the advanced filter: the invalid-filter state above;
  - on a leaf from code: the same heading without Edit filter, and the host logs
    `tm.search.custom-filter-invalid`;
  - on the drill-through filter: spec 0034 owns the handling.
- **A failed reload** keeps the held rows when the error is `offline`; spec 0028 §9.5's banner says
  why nothing updates. Any other failure replaces them with the error view.
- **A `limit` error (413)** from a URL `page` past `limits.maxSkipWindow` loads page 1 instead. A
  browse tree's 413 is §16.2's fallback.
- **Copy is entity-neutral,** so no plural label is lower-cased mid-sentence.

### 5.4 Language and permission changes

Both changes update the open page in place, without a navigation.

- **Language switch.** Labels, display names, enum labels and formats re-render from the held rows
  with no request (spec 0029 §4.2). When the order column is `text` or `navigation`, its terms
  change with the slot, so the host re-reads.
- **Permissions or settings tag moved.** Descriptors rebuild (spec 0029 §4.1), and the plan
  re-resolves through `TmPermissions.reachable`. Newly unreachable columns drop (`source:
  'preference'` or `'url'`), toolbar gates re-render, and the host re-reads. At the same moment,
  spec 0025 §10 forgets the tenant's page state.

## 6. The search field and the keyboard model

The search field filters as the user types, moves the highlight with the arrow keys, and opens the
highlighted row on Enter, while focus stays in the field. A printable key typed elsewhere on the
page lands in the field.

### 6.1 The field

- **Parts.** An `input[tmInput]` with two directives: `tmSearch` (spec 0023 §6.2) and
  `[tmGridDriver]` (spec 0010 §C.2), which steers the highlight (§6.3). `tmSearch` fires at once on
  the first key after a pause, then again 50 ms after the last key of a burst.
- **Form field.** The input sits in a `tm-form-field` that draws the magnifier and the reserved clear
  slot as `tm-search-field` does (spec 0023 §6.2's custom-chrome case).
- **Attributes.** `aria-label` "Search"; `aria-describedby` the title and a hidden hint, "Arrow keys
  move through the results. Enter opens one."; `maxlength` `limits.maxSearchLength`; `dir="auto"`.
- **Clear and Escape.** The clear button clears, emits and keeps focus. Escape clears when the
  field has text, else it propagates.
- **Instant echo.** Typed characters show in the keystroke's frame. The first key after a pause
  also shows the progress bar in that frame (§3.3).

### 6.2 Type-to-search

A page-scoped `keydown` listener on `document` runs in the bubble phase, so focused components and
`TmShortcuts` (spec 0027 §4.2) see the key first. It captures a key when all of these hold:

1. **Untouched:** not `defaultPrevented`, not `isComposing`, `key` is not `Process`, not `repeat`.
2. **One printable character:** one grapheme, not a control character, not a space (Space toggles
   checkboxes and presses buttons).
3. **No command modifier:** no Ctrl, Meta or Alt. Shift is allowed, and AltGr
   (`getModifierState('AltGraph')`) counts as typing.
4. **Not editing:** the target is not an `input`, `textarea`, `select` or `[contenteditable]`, and
   no grid editor is open. A readonly grid, a tile, a button or the body qualifies.
5. **The page is on top:** `TmPageContexts.overlays()` is empty and no menu, popover or launcher
   pane is attached.

Then, in the same task, the host calls `preventDefault()`, focuses the field with the caret at the
end, and runs `document.execCommand('insertText', false, key)`. That fires `input`, so the search
runs as if typed, and one undo removes the key. When the command reports failure, the host calls
`setRangeText` at the end and dispatches a synthetic `input` event instead.

### 6.3 Highlight and Enter

The field moves the results' highlight, and Enter opens the highlighted row. Enter always acts on
the result for the text in the field, never on an older one.

- **The driver** steers a host-owned `TmGridDriverTarget`. The target forwards to the grid, or to
  `tm-tiles` in tiles view, and wraps `activateRow` with the stale rule below.
- **Seeding.** When a result lands while focus is in the field, the highlight moves to the first
  row (in a search tree, the first match). Otherwise the highlighted row stays while present.
- **Stale result.** A result is stale while a read runs, or while the field's trimmed text differs
  from the text it answers. Otherwise it is fresh.
- **Enter on a fresh result** opens the highlighted row; Mod+Enter opens it in a new tab.
- **Enter on a stale result** flushes the search and arms a pending open: when the result for the
  current text lands, its first row opens.
- **Disarming.** Another key in the field, a pointer press, focus leaving the field or a navigation
  disarms the pending open. An empty result disarms it and announces "No matches".

**Example —** Farah types "jed" in Centers, 30 ms per key, and presses Enter at once:

1. "j" fired the search at once, and "jed" still waits in the 50 ms window. The rows on screen
   answer older text, so the result is stale.
2. Enter flushes the search, which starts the read for "jed", and arms a pending open.
3. The result for "jed" lands, and its first row opens.

### 6.4 The keyboard model

The page works by keyboard alone. The table lists the keys by where focus is.

| Focus | Keys | Effect |
|---|---|---|
| the page, not editing | a printable character | type-to-search (§6.2) |
| search field | ArrowDown, ArrowUp, PageDown, PageUp | move the highlight; in tiles, by lattice line; ArrowDown with no highlight: the first row |
| search field | Enter, Mod+Enter | open the highlighted row (§6.3) |
| search field | Escape | clear the text (§6.1) |
| grid | spec 0004 §8.2's keys | move the active cell |
| grid | Enter, Mod+Enter, F2 | open the row, open it in a new tab, follow the active cell's link (spec 0010 §C.1, §7.2) |
| grid | Space, Shift+click, Ctrl+Shift+Space | select (spec 0010 §C.5) |
| grid header (ArrowUp from the first row) | Enter, Alt+ArrowDown | sort (§7.3), open the column filter (§9.1); spec 0010 §C.4 |
| tree grid | Alt+ArrowRight, Alt+ArrowLeft | expand, collapse (spec 0004 §13.3) |
| tiles | spec 0023 §8.5's keys; in a tree of tiles, ArrowRight and ArrowLeft | move; expand and collapse |
| a popup | Escape | close and return focus to the trigger |

- **Tab order** follows the visual order: the field, Filters, the tabs, the leading group, the
  trailing group, then the results as one stop.
- **The row context menu** adds "Open in new tab" and "Copy link" to the grid's copy items
  (`extraMenuItems`, spec 0004 §8.5).

## 7. Grid view: rows, links, ordering

The grid is the amended spec 0004 grid in readonly row-activation mode, with one column per column
plan. The host writes the binding; distributions never do.

### 7.1 The binding

The host binds the grid once, for every entity:

```html
<tm-grid [gridId]="'search.' + entity" [contentKey]="canonical()" [data]="rows()" [rowId]="byId"
         selectable [(selectedIds)]="selection" rowActivation="row" (rowActivate)="open($event)"
         [rowHeaders]="false" [(activeRowId)]="highlight" [(orderBy)]="sort" [extraMenuItems]="rowMenu()">
  @for (col of plan().columns; track col.key) {
    <tm-grid-column [key]="col.key" [header]="header(col)" [sortable]="col.orderBy('asc') !== null" …>
      <!-- filterable columns: spec 0010 §C.4's tmGridHeaderEnd filter button (§9.1) -->
      <ng-template tmGridDisplay let-row="row">…a link (§7.2) or a display hatch (§2.2)…</ng-template>
    </tm-grid-column>
  }
</tm-grid>
```

- **Identity.** `gridId` = `search.<entity>`, so widths survive navigation. `contentKey` = the
  canonical query, so returning to a result restores its scroll and active cell from the grid's
  state store, and a new query starts at the top (spec 0004 §12).
- **Columns** take type, alignment, width and format from `tmGridColumnFor(column)` (spec 0029
  §12.1), except a declared `width` (§2.1).
- **Headers.** `header(col)` is the slash label (spec 0029 §11.4), or a user's expression (§11.3),
  followed by `TmFormats.zoneSuffix(col)` (spec 0029 §12.1). For example, while the display zone's
  offset differs from the device's, an instant column reads "Created at (GMT+3)". Its cells stay
  bare.
- **Static cells.** Only links and `display` hatches use a cell template; every other cell is
  static, since templates cost more (spec 0004 §7).

### 7.2 Opening rows and links

A click or Enter on a row opens its target. Links inside the row make new tabs, copied links and
the status-bar URL work natively.

- **Row activation** (`rowActivation: 'row'`, spec 0010 §C.1). A click anywhere but the checkbox
  cell, a tap, or Enter opens the row's target. Mod+click, a middle click and Mod+Enter open it in
  a new tab (`window.open(url, '_blank', 'noopener')`). A drag still selects a range for copying.
- **The target** is `rowLink(row)` when declared, else the details page (`TmUrls.details`) when the
  entity has the `details` operation. A row with no target (`null`) never activates and renders no
  link.
- **`rowLink`** runs in the host's injection context, so it may `inject(TmUrls)`. It reads only
  selected paths (§2.1).
- **The display-name cell is a link.** The first shown column whose key is the display-name source
  or its prefix (spec 0029 §5.1) renders `<a [routerLink]>` to the target.
- **Navigation cells are links** to the referenced row's details page when `TmPermissions.allows`
  grants `details` on its entity (spec 0029 §12.2).
- **One tab stop.** Links are `tabindex="-1"`, keeping the grid's one tab stop; F2 follows the
  active cell's link.
- **Preloading.** Hovering or focusing a row or link calls `TmUrls.preload` for its target once per
  page, so the details chrome renders in the click's frame.
- **Opening** remembers page state (§15.1), then navigates with `state: { tmFrom }` (§4.4).
- **On return** the highlight is the opened row. Its active-row tint and accent bar (spec 0010
  §C.1) mark the record the user came back from.
- **Picker mode** renders no links (§17).

### 7.3 Ordering

The rows are ordered by at most one column, chosen from its header.

- **One column** (spec 0010 §C.3). A click on a sortable header cycles ascending, descending, none;
  another header replaces the order. The URL's `order` and the grid's `orderBy` are one value.
- **Sortable** columns are those the plan can order (`orderBy(direction) !== null`).
- **None** is the default order (§2.1), shown with no indicator, so the third click visibly clears.
- **A hidden column may order the rows** when the URL says so (`order=CreatedAt:desc`). The Filters
  popup lists that order as a removable chip (§10.1).
- **Tree view** sorts siblings (§16.5).

## 8. Tiles view

Tiles are spec 0023 §8.5's `tm-tiles`, fed the same rows, selection and highlight as the grid, with
a layout mapped from the declaration's `tile` (§2.3).

- **Bindings.** `[data]`, `[rowId]`, `[layout]`, `selectable` with `[(selectedIds)]`,
  `[(activeRowId)]` as the highlight, and `(rowActivate)` into the grid's handler (one payload
  type, spec 0010 §C.1). The title's `href` is the row's target.
- **Geometry** is spec 0023's. `[tmTilesLoading]` and `[tmTilesEmpty]` render §5.3's states.
- **On return,** `scrollTo` applies the remembered offset, else the tiles scroll to the highlight.
- **Tree of tiles:** `parentId`, `hasChildren`, `expandedIds` and the batched `loadChildren`, as
  the tree grid (§16). Context rows take `tm-tiles__item--context`.
- **Ordering and column filters** have no headers here. Both are in the Filters popup (§10.1), so a
  phone's default view loses nothing.

## 9. Column filters

Every filterable column header carries a filter button that opens spec 0029's `tm-column-filter`
for that column. An applied filter is one `f.{key}` URL entry and one leaf.

### 9.1 The header affordance

- **Which columns.** Those whose plan has a filter kind (`TmColumnPlan.filter`, spec 0029 §13.2).
- **The button.** Each such column declares a `button[tmGridHeaderAction]` (`funnel` icon) in
  `tmGridHeaderEnd`, exactly as spec 0010 §C.4's example. The slot is reserved, so applying a
  filter moves nothing.
- **The panel** is a `tm-popover` `size="sm"` holding `<tm-column-filter [column] [value]
  (applied)>`. Below `sm`, it is an end drawer (§3.2).
- **Name and keys.** The button is named after its column: "Filter Name", or "Filter Name, active"
  while applied. Alt+ArrowDown on the header cell opens the panel with focus in the operator
  select; Escape returns focus to the button.

### 9.2 Values

The panel's Apply and Clear change the query; a filter outlives its column.

- **Apply** sets `f.{key}` (operators and leaves: spec 0029 §13.2), and **Clear** removes it. Both
  come through `applied` (spec 0029 §13.1).
- **Either one** replaces the URL, goes to page 1 and reloads (§4.4).
- **A filter on a hidden column** stays applied. It comes from a link, or stays after its column
  was removed. The Filters popup lists it, editable and removable (§10.1).

## 10. The advanced filter popup

The Filters button opens one popup with the Queryex filter, the Active-only switch, the custom
filters and every applied column filter. Its edits are staged and apply together on Apply.

### 10.1 Layout

- **Surface.** A `tm-popover` `size="md"` (spec 0010 §D.3) anchored to the Filters button,
  end-aligned, headed "Filters". The button's `aria-expanded` follows it.
- **Sections, in order:**
  1. **Filter expression.** `<tm-expression-editor clause="filter" [root]="entity" [rows]="3">`
     (spec 0029 §10), labelled "Filter expression", placeholder "CreditLimit > 100000".
     Mod+Enter applies, matched through `kbd` (spec 0029 §10.5). Absent in list mode (§17).
  2. **Active only.** A `tm-switch` on activatable entities, on by default; off is `inactive=1`.
  3. **Custom filters.** A switch or a radio group per declaration (§2.4), in declaration order.
  4. **Applied.** A `tm-chip-set` with one removable chip per column filter.
     - **Chip text** comes from `tmDescribeFilter` (spec 0029 §13.1): "Name contains jed", "Type:
       Service, Sale". An order on a hidden column adds a chip "Ordered by Created at, descending".
     - **Editing.** A chip's label (its `activate`, spec 0023 §4.3) opens that column's
       `tm-column-filter` in place, with Back.
     - **Adding.** "Add column filter", a `tm-menu` of the filterable shown columns, does the same
       for a new one. This is how tiles view filters by column.
- **Footer.** "Clear all" (ghost) and "Apply" (primary). Clear all stages an empty filter
  expression, no column or custom filter, and Active only on.

### 10.2 Behaviour

- **Apply** turns the staged state into one query change: one URL write, one read, page 1. The
  popup closes, and focus returns to the Filters button in the click's frame.
- **Dismissing** (Escape, an outside click, the drawer's close button) discards staged edits.
- **Apply is disabled** only while the expression's last completed check, run on its current text,
  found problems. While a check runs, or when it is offline or failed, Apply stays enabled and the
  server decides.
- **Edit filter** (§5.3) opens the popup with the editor focused, where F8 selects the problem's
  span (spec 0029 §10.5).
- **The count.** The button's `tm-counter` counts the expression, each column filter, each custom
  filter, and the inclusion of inactive rows. The button's name is "Filters" or "Filters, 3
  active".
- **Invalid expression.** While the expression is invalid, the button takes the error tone, and its
  name adds ", the filter expression is not valid".

## 11. The column editor

The Columns button opens a two-pane editor: the root entity's path tree on one side, the ordered
shown columns on the other. Changes apply at once and become the user's preference.

### 11.1 Layout

- **Surface.** A `tm-popover` `size="lg"` anchored to the Columns button. Below `sm`, an end drawer
  with the panes as tabs (a `tm-segmented`: "Available", "Shown").
- **Header and footer.** The header reads "Choose columns" with "{n} shown". The footer holds
  "Reset to default" (a link button) and Done (primary).
- **Available.** `<tm-path-picker mode="columns" selection="multiple" [(selected)]>` over the root
  (spec 0029 §11), with its built-in search; below it, "Add expression" (§11.3).
- **Shown.** A `tm-reorder-list [(items)]` (spec 0023 §8.4): the handle, a checked checkbox
  (unchecking removes) and the slash label. It accepts `tm.path` drops from the tree (`accepts:
  ['tm.path']`) at the drop index.

### 11.2 Semantics

- **Checking** a node adds its key at the end of Shown:
  - a scalar or group anchor adds its path;
  - a navigation adds its display-name column (`Parent`);
  - a nested field adds its dotted path (`Parent.Code`, shown as "Parent center / Code").
- **Without dragging.** Enter or a double-click toggles a node: the non-drag path (WCAG 2.5.7).
- **Limits.** At least one column (the last checkbox is disabled) and at most 50. A 51st is
  refused with "You can show up to 50 columns", announced politely.
- **Live apply.** Each change updates the shown list, replaces the URL (§4.3) and writes the
  preference (write-behind, spec 0025 §9). An addition re-plans and re-reads; a removal or a move
  re-renders the held rows with no request.
- **Closing.** Done, Escape and an outside click only close.

### 11.3 Expression columns

A user may add a column computed from a Queryex expression. Such a column is labelled by its own
text.

- **"Add expression"** opens an inline form: `<tm-expression-editor clause="select" [root]
  [rows]="2">`, checked by `queryex/discover`, and Add, enabled once the check finds no problem.
- **Headed by its text.** Column labels are string keys, so a user's expression column has no label
  of its own. Its header, export header and page-context label are the expression itself:
  - shown in mono and LTR after an `ƒ` glyph, ellipsized, with the full text in a tooltip;
  - passed to the plan as `{ expression, labelKey: 'tm.search.expressionColumn' }`.
- **Editing.** An edit button on its Shown row reopens the form. It is ordered and removed like any
  column, and never travels in a URL (§4.3).

### 11.4 Defaults and the preference

The shown list is the user's preference until it matches the code default again.

```ts
// @tellma/core primary (types only): the value of search.<entity>.columns (spec 0025 Appendix A)
type TmColumnPreference = string | { readonly expression: string };   // a column key, or a user's expression column
```

- **Equal to the default deletes the key.** A shown list equal to the code default deletes the key
  (spec 0025 §9.1), so later code-default changes reach the user.
- **"Reset to default"** applies the code default, which deletes the key, and removes `cols` from
  the URL.
- **Stale keys** in a preference (a changed entity, a revoked permission) are dropped by the plan
  (`source: 'preference'`). The preference is rewritten on the next change only.

## 12. Selection and bulk actions

Checked rows form one selection that survives searching and paging. The selection bar acts on it.

### 12.1 Selection

- **One set** of ids, shared by the grid, the tiles and the tree (`selectedIds`).
- **Select-all** toggles the current rows and keeps the others (spec 0010 §C.5).
- **Survives queries.** Text, filter, tab, order, page and view changes never clear it.
- **Cap.** Beyond `limits.maxIds` the actions disable ("Select at most 10,000 items").
- **Lifetime.** Clear selection, a successful write action and a tenant change clear it. Page state
  keeps it (§15).

### 12.2 The selection bar

The selection bar takes the leading group's place (§3.1). Its Actions menu lists every action
that applies to the selected rows.

- **Contents** while the selection is not empty: "{n} items selected" (an ICU plural; "{n}
  selected" below `sm`), the Actions menu button and Clear selection (an X).
- **The Actions menu** (`button[tmMenuTriggerFor]`, spec 0010 §C.10) holds these groups in order.
  An item is present when its operation exists and `TmPermissions.allows` grants it (spec 0029 §7).
  A page action is present while its `requires` holds (§3.1).

| Group | Items | Request |
|---|---|---|
| export | Export, Export for import | by ids (§13.2) |
| state | Activate, Deactivate (activatable entities) | `activate` or `deactivate` with `{ ids, returnEntities: false }` |
| entity actions | each `entity` action that is neither built in nor `singleTarget` (spec 0029 §2.2) | `TmEntityActions` (spec 0031 §10.4), below |
| page actions | each `pageActions` entry with `selection: true`, in declaration order (§3.1) | its `run` with the selected ids, below |
| delete | Delete; on tree entities also Delete with descendants | `delete` or `delete-with-descendants` with `{ ids }` |

- **Entity actions** run at once when they take no arguments. Otherwise they open their dialog,
  generated from the argument schema or declared as `dialog`.
- **Page actions** confirm through their own `confirm`. They show no count toast, since the page
  shows the change.
- **`visible` is not evaluated here.** An action declaration's `visible` (spec 0029 §3) is
  ignored; the server refuses per row.

### 12.3 Running an action

Every bulk action follows the same steps:

1. **Invoke.** The item invokes its page action (`TmPageActions.invoke`, spec 0026 §9.5).
   Destructive ones confirm first: "Delete 3 items?" with "This cannot be undone.", or "Delete 3
   items and everything under them?".
2. **Busy.** The item shows its busy state at once. The page stays usable, and a second invoke is
   refused.
3. **Success.** A toast shows the count (spec 0028 §8.1: "3 items deactivated."). The selection
   clears, `TmPageState.markStale({ tenantId, entity })` runs (spec 0025 §10), and the page
   re-reads. The bar leaves with the selection, so focus moves to the results.
4. **Failure.** The selection stays.
   - A 404 (ids gone or hidden) or a 422 `Fk.InUse` at `ids[i]` is all-or-nothing (spec 0015
     §10.1). `TmErrorDialog` lists the affected rows' display names (`TmDisplayNames.label`).
   - Other errors go to `TmErrorPresenter` with `kind: 'action'`.

## 13. Create, export, import, refresh

Create is a link; export sends the displayed column model; import is one dialog over spec 0019's
operations; refresh re-reads in place.

### 13.1 Create

- **"New customer"** (primary; the entity's `newLabelKey`, spec 0029 §2.4) is a link button to
  `TmUrls.create(entity)`, so Mod+click opens a new tab.
- **It shows** when the entity has the `save` and `details` operations and `allows(entity, 'save')`
  holds.

### 13.2 Export

The Export menu offers three files. The grid export writes the columns the user sees, with their
headers as shown.

| Item | Route | Shows while `TmPermissions.allows` grants |
|---|---|---|
| Export grid | `export` | `export` |
| Export for import | `export-for-import` | `import` |
| Import template | `export-for-import` with `ids: []` (spec 0019 §2.1) | `import` |

- **Selection.** The Actions menu's two export items (§12.2) send the selection.
- **Columns.** The grid export sends the displayed column model, never the select: one
  `ExportColumn` per shown column (spec 0019 §2.1).
  - `expression` is the column key.
  - `header` is its header as displayed, in the UI language, without §7.1's zone suffix.
  - Image columns are left out.
- **Source.** The grid's query (`filters`, `search`, `orderBy`, `includeInactive`, with no paging),
  or `ids` for the selection.

**Example —** Centers in Arabic, `?q=jed&f.CenterType=in:Service,Sale&cols=Code,Name,Parent,Parent.Code`:

```json
POST /17/api/web/centers/export
{ "columns": [ { "expression": "Code", "header": "الرمز" }, { "expression": "Name", "header": "الاسم" },
               { "expression": "Parent", "header": "المركز الأب" },
               { "expression": "Parent.Code", "header": "المركز الأب / الرمز" } ],
  "filters": ["CenterType in ('Service', 'Sale')"], "search": "jed", "orderBy": "Code, Id", "includeInactive": false }
```

The server writes four columns: `Name` in the request language's slot, falling back to slot 1, and
`Parent` as the parent's display name (spec 0019 §4.1). `Id` appears only when shown.

- **Synchronous first.** Every export runs `TmApi.download(route, body, { signal, onProgress })`.
  It saves through an object URL and `<a download>` with the server's file name.
- **Time limit.** The Contracts chunk lists `export` and `export-for-import` as long, so `TmApi`
  allows them 6 minutes (spec 0024 §3.4).
- **Progress and Cancel.** A sticky "Exporting…" toast with progress and Cancel shows meanwhile
  (spec 0028 §8.1). Cancel and leaving the page abort the export.
- **The background on 413.** On `413 Excel.Export.RowLimitExceeded`, the host retries the same body
  once as `export/start` or `export-for-import/start`. The 202 answer toasts "Started in the
  background." with Track, which opens the job's details page. The file arrives with
  `core.export.ready` (spec 0019 §12).
- **Export for import** sends `ExportForImportRequest` with the same source, every editable column
  and every child collection. The template is always synchronous.

### 13.3 Import

`TmImportDialog` is the only import surface: a `TmModal` `size: 'lg'`, full screen below `sm`. The
Import button shows while `allows(entity, 'import')` holds. The dialog walks four steps over spec
0019 §2.1.

1. **File.** A `tm-dropzone` (spec 0005 §8.1) taking the `import-file` blob policy (spec 0029 §16),
   with a "Download the template" link. A pick uploads at once through `TmApi.upload('import-file',
   file, { fileName, signal, onProgress })`, with a progress bar; Cancel aborts.
2. **Check.** `inspect-import { fileId }` returns the `ImportPlan`. The step shows:
   - the sheets with their rows and status, and the mapped columns;
   - each unmapped column with a `tm-select` of its `Suggestions` plus "Ignore"; "Ignore every
     unmapped column" sets `ignoreUnmappedColumns`;
   - the mode, a `tm-radio-group`: Add new rows, Update existing rows, Add and update. The update
     modes pick the row key from `RowKeyCandidates` (default `DefaultRowKey`);
   - the warnings.

   A changed mapping re-inspects.
3. **Import.**
   - **In the foreground** (no `RequiresBackground`): "Import" calls `import`. It is a write, never
     aborted, and the dialog cannot be dismissed while it runs.
   - **In the background** (`RequiresBackground`): the step says "This file is large, so it imports
     in the background.", offers "All or nothing" (`atomic`) when `AtomicAllowed`, and calls
     `import/start`.
4. **Result.** An outcome or a 422 (`ImportException`) shows in `tm-import-result-view`.
   - A success marks the page stale and re-reads.
   - A 422 offers "Back" to step 2 and "Choose another file".
   - A 202 closes the dialog and toasts "Started in the background." with Track to the job.

- **Confirming identity.** When `TmPermissions.sensitive(entity, 'Save')` holds, step 1 notes
  "You'll be asked to confirm it's you."
- **The result view** is also used by the Imports details page (spec 0032 §11). It shows either:
  - "{inserted} added, {updated} updated", child counts and warnings; or
  - an errors table (Sheet, Row, Column, Message) with the first 100 errors, then "and {n} more",
    and "Copy errors" (tab-separated).

  A stopped background import adds "Rows {from}–{to} were saved before the import stopped."

```ts
// @tellma/core/search
declare class TmImportResultView {                    // tm-import-result-view
  readonly result: InputSignal<TmJsonObject | null>;  // an ImportOutcome, or ImportException's 422 body (spec 0016 §7.1)
  readonly running: InputSignal<boolean>;             // an indeterminate bar while the import runs
}
```

### 13.4 Refresh

The Refresh icon button re-runs the current query under a new refresh generation (§5.2). Page,
selection, scroll and highlight are kept, and the progress bar shows as for any read (§3.3).

## 14. Paging

Lists page on the server, with a counted query and a pager in the trailing group. A page change is
the one change that pushes history (§4.4); a click while a page loads supersedes its read.

- **Control.** A `tm-pager mode="range"` (spec 0023 §8.1) with previous and next. Its status reads
  "1–50 / 55", in isolated LTR, in the locale's digits (spec 0024 §7.7).
- **Page sizes** are 20, 50, 100 and 200, within `limits.maxTake`. The default is 50.
- **Count.** A capped count shows "1–50 / 10,000+" (`totalIsLowerBound`). Next stays enabled until
  a short page arrives or `page × size` reaches `limits.maxSkipWindow`.
- **A size change** keeps the first visible row, with `page = floor((page − 1) × old / new) + 1`.
  It writes `search.<entity>.pageSize`. For example, page 3 at 50 (rows 101–150) becomes page 6 at
  20 (rows 101–120).
- **A browse tree** is unpaged (§16.2). The pager shows "1–N / N", `disabled`, its geometry kept.

## 15. Page state and return

The host remembers what the page needs to look the same when the user returns within the tab, and
hands the same snapshot to the details page for paging.

### 15.1 What the host remembers

`TmPageState.remember(tmPageStateKey('/17/customers'), snapshot)` (spec 0025 §10) runs when the host
is destroyed and before it opens a row.

```ts
// @tellma/core primary (types only); written here, read and written by spec 0031 §12
interface TmSearchSnapshot {
  readonly v: 1;                                     // the snapshot's format version
  readonly entity: string;
  readonly query: TmSearchQuery;                     // the query state (§4)
  readonly canonical: string;                        // the canonical query: the result's identity
  readonly supersedes?: string;                      // a canonical query this snapshot replaces (§4.2 step 1)
  // The last rows request; null with searchRead and for a browse tree (§16.2).
  readonly request: { readonly route: string; readonly body: TmJsonObject } | null;
  // What that request returned; capped: total is a lower bound (§14).
  readonly result: { readonly set: TmQueryRowSet; readonly ancestors?: TmQueryRowSet;
                     readonly total: number | null; readonly capped: boolean } | null;
  readonly ids: readonly TmId[];                     // the shown rows, in display order
  readonly selection: readonly TmId[];
  readonly highlight: TmId | null;                   // the row last opened, else the highlighted row
  readonly expanded?: readonly TmId[];               // tree view
  readonly scroll?: number;                          // tiles only (the grid's store keeps its own)
}
```

### 15.2 Returning

The selection and the expansion restore whenever this page has a snapshot, whatever the query's
source. The rest depends on whether the snapshot answers the resolved query.

1. §4.2 resolves the query. When the snapshot's `canonical` equals it, its rows render at once with
   no request. The grid restores scroll and the active cell from its store (same `contentKey`), and
   tiles scroll to `scroll`.
2. When `TmPageState.isStale(key)` holds (a save, delete, import or action on the entity, spec 0025
   §10), the held rows still render at once, and a read refreshes them. Scroll and highlight keep
   their rows.
3. When the canonical query differs, nothing else restores. An evicted entry is a first load.

### 15.3 What spec 0031 reads

Spec 0031 §12 finds the snapshot through `history.state.tmFrom`, steps through `ids`, reads
`request` past an edge, and writes the next page back with `supersedes` (§4.2 step 1).

## 16. Trees

A tree entity (the `tree` capability, spec 0015 §12.1) shows a tree by default: roots first,
children loaded on expand, and the expansion remembered for the sign-in session. A search shows the
matches inside their ancestors.

### 16.1 Tree or list

- **Toggle.** A `tm-segmented` with Tree (`list-tree`) and List (`list`), on tree entities without
  `searchRead`. List writes `list=1` and the merged `{ view, list: true }` to `search.<entity>.view`.
- **List** is the flat, paged page of §5–§14.
- **Grid or tiles** stays independent: a tree grid, or a tree of tiles (the default below `md`).

### 16.2 Browse tree: roots first, one call

A browse tree loads in one unpaged call: the roots, plus the children of every node the user left
expanded.

**Example —** Centers, with three remembered expanded nodes:

```json
POST /17/api/web/centers/get-by-parent-ids
{ "parentIds": [12, 40, 41], "includeRoots": true, "orderBy": "Code, Id", "includeInactive": false,
  "select": "Id,Code,Name,Name2,ParentId,SubtreeCount,ActiveSubtreeCount,IsActive" }
```

- **`parentIds`** are the remembered expansion: `TmTreeExpansion.expanded(page)()`, read once at
  load (spec 0025 §12). The host sends at most the newest `limits.maxIds`; none means the roots
  only.
- **Pruning.** The answer holds the roots and the children of every listed parent. The host keeps a
  row only when it is a root or its parent is kept, so the grid never sees an orphan.
- **`hasChildren`** is `SubtreeCount > 1`; a leaf counts 1 (spec 0015 §12.1). On an activatable
  tree, while inactive rows are excluded, it is `ActiveSubtreeCount > 1`.
- **Empty parents.** A listed parent that gets no children back counts as loaded.
- **Unpaged, with a fallback.** A `413 limit-exceeded` (more than `MaxTake` rows, spec 0015 §5.4)
  shows this visit as List. A one-line notice above the region reads "This tree is too large to
  show at once, so it's shown as a list." No preference is written, and `tm.search.tree-fallback`
  is emitted.

### 16.3 Expanding, collapsing, remembering

Expanding loads children in batches. The expansion is remembered for the next visit, never pushed
into an open tree.

- **Expand.** The grid or the tiles call the batched `loadChildren(rows)` (spec 0010 §C.9): one
  `get-by-parent-ids { parentIds, includeRoots: false, … }`, whose rows the host appends. A failure
  collapses the batch and announces.
- **Memory.** `[expandedIds]` is seeded from the expansion memory at load, and
  `(expandedIdsChange)` writes back to it. An expand calls `toggle(page, id, true)`. A collapse
  calls `replace(page, remaining)`, where `remaining` also leaves out the node's loaded descendants.
- **Across tabs.** An open tree never re-expands when another tab changes the memory. That tab's
  next visit renders the same tree in one call.

### 16.4 Search tree: matches inside their ancestors

A search tree shows each match under its ancestors, fully expanded and static. Ancestors that did
not match are context rows, marked by more than colour.

- **The call** is §5.1's `query` with `includeAncestors: true`. Matches page as in a list; the
  ancestors of the page's matches ride the same answer (spec 0015 §5.2 step 5).
- **Rendering.** Rows are the matches and their ancestors, with every present parent expanded.
  Nothing loads lazily (`hasChildren` = has present children), and memory is not written.
- **Leaving.** Clearing the text and filters returns to the browse tree with its memory.
- **Context rows** take `rowClass` `tm-grid-row--muted` (spec 0010 §C.6; tiles
  `tm-tiles__item--context`). Their display-name cell adds a hidden "(ancestor of a match)", so the
  difference is not colour alone (WCAG 1.4.1). A one-line legend under the region reads "Grey rows
  are ancestors of matches."
- **Highlight** seeds on the first match (§6.3). **Selection** may include context rows. A match
  that is also another match's ancestor renders as a match.

### 16.5 Ordering a tree

An order sorts siblings and never switches to List. The browse call sends `orderBy`, the search call
orders the matches, and the tree keeps data order among siblings (spec 0010 §C.9).

## 17. The picker page and the drill-through list

`TmSearchHost` also runs as an entity picker's advanced search (`picker` mode) and as the reports'
drill-through list, `/{t}/list/{segment}` (`list` mode). The table lists what differs from page mode.

| Aspect | `picker` (`TmSearchPickerPage`, spec 0029 §14.4) | `list` (spec 0034 §8.3) |
|---|---|---|
| URL and page state | none: the state is the modal's own | §4 and §15, keyed by the list path; the URL `filter` is a fixed leaf |
| initial text | `TmEntityPickerPageData.query`, selected in the focused field | `q` |
| columns | the code default; no column editor | `cols`, else the code default; the editor writes the URL, never the preference |
| filters | all, plus fixed leaves: not shown, not removable, not counted | all but the filter expression |
| rows 1 and 2 | the title; the pager, page size and view toggle only | the crumb and chips (spec 0034 §8.3, §8.4); no Create or Import; "Open in {plural}" for an entity with a search page |
| links and activation | no links; activation closes with `{ id, label, item }`, the label being the display name | as page mode; a child entity's row opens its owner on that row (spec 0034 §8.3) |
| multiple choice (`multiple`) | checkboxes; "Done ({n})" closes with the new picks; the field's chosen ids show checked and disabled, "Already chosen" | — |
| page context | an overlay `search` context (spec 0026 §9.2) | the routed `search` context |
| type-to-search | off: the field has focus | on |

- **The picker** is a `TmModal` `lg`, full screen below `sm`. Its entity and filter ride the page's
  `context` (spec 0010 §E.3).
- **The list** is never pinned and never a recent (spec 0024 §9.4, spec 0025 §13).

## 18. Testing

Six suites pin the design, and all of them run on every PR.

- **Unit** (vitest): §4.2's vectors, `supersedes` included; the canonical URL (`cols` with
  expression columns, `view` by width, drops); history per change (§4.4); leaf order and diagnostic
  mapping; the type-to-search predicate over key, modifier and target vectors; stale results, the
  bar's `active` rule and the pending open; headers with and without the zone suffix, and export
  headers without it; tree pruning, and collapse through `replace`; page-size arithmetic.
- **Harness** (`TmSearchPageHarness`, a fake `TmApi`, a fake clock):
  - five keys 30 ms apart → two reads: the first key's read starts at once and is aborted, and the
    burst's read settles;
  - the progress bar shows on the first key, stays on through the burst, and holds at least 250 ms
    after a fast answer;
  - Enter before arrival opens the first row;
  - selection across pages; bulk success, 404 and 422;
  - export bodies (columns, filters, ids) and the 413 fallback; import steps with a plan, a 422 and
    a 202;
  - the column editor writes the preference and the URL, and a removal sends nothing;
  - a page-state restore sends nothing, and a stale one re-reads;
  - tabs and `searchRead`; picker mode closes with picks; list mode's fixed leaf.
- **e2e on Acme** (Playwright; Chromium, `@cross-engine` on Firefox and WebKit):
  - warm launch with requests held; the geometry spec;
  - type-to-search from the grid and the body, insertion and undo in three engines;
  - keyboard-only open and return, with scroll and highlight restored; a header filter by
    Alt+ArrowDown; tiles at 390 and 768;
  - a tree expand, then a second tab opening the same tree in one call; a search tree with muted
    ancestors;
  - an export download and the background path; a good and a bad import;
  - a restricted role: unreachable columns absent, and a URL naming one loads without `403`;
  - every flow in `ar` at 390, 768, 992 and 1440.
- **axe:** every §5.3 state, every popup, selection mode, a search tree, tiles; light and dark, LTR
  and RTL.
- **Budgets:** §1's figures from `stats.json` (spec 0024 §12).
- **Server:** the wire this page relies on (`Filters` leaves, `IncludeRoots` with the `MaxTake`
  cap, `ExportColumn`), through spec 0015 §18's and spec 0019's suites.

## 19. Definition of done

- **Behaviour.** §1–§17 hold, and §18's suites are green.
- **UI rules.** Each rule below names how the design meets it, then the test that pins it.
  - **Instant warm launch.** The chrome renders from descriptors, labels, access and preferences
    with no request. Only rows wait, behind skeleton rows (§5.3). *Test:* spec 0024's
    `warm-launch.spec` on `/17/customers` and `/17/centers` with every request held: chrome, headers
    and skeleton rows within 1 s, and no shift to the loaded page.
  - **Instant feedback.** Typing is native, and the first key after a pause searches and shows the
    progress bar in its frame (§3.3, §6.1). Enter, sort, filter, view toggle and paging change the
    screen in the click's frame. Hover preloads the target, and column removals re-render with no
    request. *Tests:* `search-feedback.spec` (a frame probe after each gesture and the first key);
    `view-toggle.spec` (zero requests).
  - **Non-blocking.** One `resource()` per query; superseded reads aborted to the server; nothing
    disabled while loading; exports cancellable; writes never aborted. *Tests:* harness
    `typing-aborts.spec`; spec 0024 §12's staging abort smoke.
  - **Stable layout.** Fixed rows 1 and 2; reserved counter, sort and filter slots; the pager's
    reserved width; the selection bar swaps the leading group only; the progress bar overlays; the
    scrollbar gutter; states inside the region. *Test:* `search-geometry.spec`: every row-1 and
    row-2 control keeps its box across typing, loading, selecting, filtering and an empty result,
    with scrollbars shown.
  - **RTL.** Logical properties; mirrored pager chevrons and twisties; unmirrored sort arrows,
    numbers (physically right) and paging status (isolated LTR); isolated display names and slash
    labels; drawers at the inline end; the field `dir="auto"`; Up and Down never mirror. *Tests:*
    every e2e in `ar` at four widths; `tree-rtl.spec`; `pager-rtl.spec`.
  - **WCAG AA.** §6.4's keyboard model; the driver's ARIA; labelled filter buttons; 24 px targets,
    46 px below `sm`; §5.2's announcements; popups return focus. Cues go beyond colour: the filter
    glyph, counter and name; the context-row text and legend; the sort arrow and `aria-sort`.
    *Tests:* `keyboard-only.spec` (type, arrows, Enter, Back, a header filter, a keyboard column
    reorder, a bulk deactivate, an import); the axe battery.
- **Page context** (kind `search`, spec 0026 §9.3):
  - `title`: the plural label;
  - `status`: follows the resource;
  - `state`: spec 0026 §9.3's `search` minimum;
  - `visible`: the shown columns (key, slash label) and at most 200 rows as displayed;
  - `errors`: the invalid-filter diagnostics and the read error with its trace id.
- **Page actions** (spec 0026 §9.4). The host's own controls invoke them; it never renders a
  generic list.
  - `primary`: `search.create`, `search.refresh`, `search.import` (opens the dialog).
  - `menu`:
    - `search.export { kind: 'grid' | 'for-import' | 'template', scope?: 'query' | 'selection' }`;
    - over the selection: `search.activate`, `search.deactivate`, and the destructive, confirmed
      `search.delete` and `search.delete-with-descendants`;
    - each entity action as `<schema>.<entity-kebab>.<action>`, with its argument schema.
  - **Without a control,** for shortcuts and the assistant: `search.query { q?, tab?, filter?,
    columnFilters?, inactive?, custom?, order?, page? }`, `search.set-columns { columns }`,
    `search.open { id }`, `search.select { ids, mode }`.
  - **Declared entries.** Each `pageActions` entry keeps its own id and `group`. A `selection`
    entry is a `menu` action over the selection.
- **Telemetry** (`tm.search.*`, closed properties, spec 0026 §3.2).
  - **Timings:** `query { source: load | typing | filter | order | page | refresh | restore, view,
    tree: browse | search | none, outcome: ok | empty | invalid | error }` (sampled); `tree-load
    { ids: 0 | 1-10 | 11+, kind: restore | expand }`.
  - **Navigation events:** `restored { stale, rows }`; `open { via: pointer | keyboard | link |
    pending, newTab }` (sampled); `type-to-search` (sampled); `view-changed { view, list }`.
  - **Query events:** `columns-changed { count: 1-5 | 6-10 | 11+, expressions }`; `filter-applied
    { kind: column | advanced | custom | inactive | tab }`; `tree-fallback`; `custom-filter-invalid`.
  - **Action events:** `export { kind, scope, mode: sync | background, outcome }`; `import { stage:
    inspected | imported | started | failed, mode, rows }`; `bulk { action, count, outcome }`.
- **Reference distribution.** Acme exercises the page through:
  - `customers` (§2, with §2.4's `credit` filter): tiles, custom filters, a `display` hatch, an
    enum, a decimal and a cacheable navigation;
  - `salesInvoices`: tabs, a scalar display name, export and import;
  - its `gl.Center` override (spec 0032 §6): a tree with a `Manager` column.

  Spec 0018 §6.3's data fills paging, tiles and a four-level tree.

## Decisions record

1. **A click anywhere on a row except its checkbox opens it, Enter opens the highlighted row, and
   display-name and navigation cells are real links** — the common case needs no precision, and
   links keep new tabs and reach other entities. Rejected: row click only (no native link
   affordances); a link column only (a small target for the commonest action) (§7.2).
2. **The export sends the displayed column model, never the grid's select** — one display-name or
   active-slot column per shown column, `Id` only when shown. Rejected: the select verbatim (four
   columns per navigation); a client-built select (a navigation still needs several terms); columns
   from the declaration (the server does not know the user's columns) (§13.2).
3. **`tabs`, `rowLink` and `pageActions` are declaration members, bulk actions derive from entity
   actions and `selection` page actions, and the `search` hatch replaces the results region only**
   — the attention and reports pages stay declarations, and every hatch keeps the platform's
   toolbar, URL and state (§2, §2.5).
4. **The field drives the grid's highlight, and type-to-search inserts the key with `insertText`** —
   focus, caret and IME context stay in the field, the same in every engine (§6.2, §6.3).
5. **Enter on a stale result opens the first row of the typed text's result** — a fast "type,
   Enter" never opens a row the user did not see (§6.3).
6. **The URL query applies whole; the address bar is canonical; only page changes push** — a pin
   never inherits a remembered search, a copied URL reproduces the page, Back steps by page (§4).
7. **`view` and `list` are two axes with one preference for every width** — a tree of tiles and a
   flat tiles list are both addressable (§3.4, §4.1).
8. **The select joins the column, tile and tree plans; every filter source is its own leaf** — a
   view toggle never re-reads, and a diagnostic points at the control the user wrote (§5.1).
9. **One sort column; the default order shows no indicator; `Id` breaks ties** — stable paging and
   a visible third click (§7.3).
10. **The browse tree is one unpaged call; an order sorts siblings; a search tree is static** —
    paged roots cost a request per restore, and lazy children would mix filtered rows (§16).
11. **Exports run synchronously first and move to the background on 413** — one rule (§13.2).
12. **The column editor applies live; expression columns stay out of URLs, and a user's is headed
    by its text** — no lost work, no crafted text in cells, labels stay keys (§4.3, §11).
13. **Filters apply on Apply; selection survives queries and clears after a write; tree deletes
    offer Delete with descendants** — one rule, no unseen selection, no surprise cascade (§10, §12).
14. **The first key after a pause searches at once, and the progress bar follows a stale result,
    not a running read** — feedback in the key's frame, with no blink between keys while each read
    is aborted and replaced. Rejected: a bar that waits 150 ms after a read starts, which leaves
    about 200 ms without feedback after the last key (§3.3, §6.1).

## Review flags

1. **The browse tree is unpaged, capped at `MaxTake`** (§16.2). Alternative: page the roots. Flips
   if a real tree has thousands of roots, or of children under one node.
2. **Trees keep their shape** (§16.4, §16.5). Alternatives: a flat list on any order; lazy
   children under matches. Flips if users expect a sorted flat view, or drill from matches.
3. **One view preference for every width** (§3.4). Alternative: one per width class. Flips if users
   moving between phone and desktop report the wrong view.
4. **The column editor applies live** (§11.2). Alternative: a draft applied on Done. Flips if live
   re-reads prove distracting.
5. **Exports try the synchronous path first** (§13.2). Alternative: the background at once for a
   capped count (10,000+). Flips if large exports that wait for the 413 feel slow.
6. **Expression columns stay out of URLs and carry no label of their own** (§4.3, §11.3).
   Alternatives: encode them under a safety rule; a free label, which needs spec 0029's plan to
   accept `{ expression, label }`. Flips if users share such views, or want named columns.
7. **The URL query applies whole** (§4.2). Alternative: merge it with page state field by field.
   Flips if a pin with a filter should keep the user's last text.
8. **Selection clears after a successful write** (§12.1). Alternative: keep it. Flips if users
   chain actions on one selection.
9. **Tree delete is two actions** (§12.2). Alternative: one cascading Delete. Flips if users always
   mean the subtree.
10. **A pending Enter opens the first result on arrival** (§6.3). Alternative: ignore Enter until
    results settle. Flips on reports of unexpected opens.
11. **Three layout choices** (§3.2, §5.3, §14): 50 rows per page; the view toggle stays in
    selection mode; an invalid filter shows on the Filters button and in the region, not in the
    field. Alternatives: 20 rows; hiding the toggle while rows are selected; the error in the field.
    Flips on a design review, or on response times for the page size.
12. **Space never starts type-to-search, and a dead key's accent is lost** (§6.2). Flips if users
    of accented layouts report it.
13. **Entity-neutral state copy** (§5.3): "No matches", not "No customers match this search".
    Alternative: per-entity keys. Flips on a preference for named copy.
14. **No custom-filter component hatch** (§2.4). Alternative: a component building its leaf with
    `tmQx`. Flips for a data-driven filter, such as spec 0032 §11's choice of entity.
15. **Tabs sit at the start of row 2** (§3.1). Alternative: their own fixed row above the results.
    Flips if long tab labels crowd row 2 at phone width.
16. **A `searchRead` page loses tree view, export by query and cross-page paging** (§2.5).
    Alternative: route those reads through `searchRead` too. Flips if the reports page needs them.
