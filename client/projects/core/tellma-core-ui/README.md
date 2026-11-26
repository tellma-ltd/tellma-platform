# @tellma/core-ui

The Tellma UI component library: signal-first, zoneless Angular form controls
built on `@angular/cdk` + `@angular/aria`, Signal Forms native (`[formField]`
binds every control; the bound field is authoritative for
disabled/readonly/required).

## Entry points

| Import | Contents |
|---|---|
| `@tellma/core-ui` | `provideTellmaUi()` / `provideTellmaForms()` (with the translations loader, the theme seed and a live root `Directionality`), the `TM_UI_TRANSLATE` i18n seam + `TM_UI_MESSAGE_CONTEXT`, `TM_ACTIVE_LOCALE`, the `TmL10n` formatting facade (numbers, dates, instants with zone labels, relative times, month and weekday names), `TM_CALENDAR` + `provideTmCalendar()`, the display and device time zones (`TM_TIME_ZONE`, `TM_DEVICE_TIME_ZONE`) and the clock (`TM_NOW`), `TmBreakpoints` + `TM_BREAKPOINTS`, `TmTheme`, `TmClientCache`, the `tmMinDate`/`tmMaxDate` validators, field-error resolution, self-hosted Latin/Mono fonts (`fonts/fonts.css`) |
| `@tellma/core-ui/contracts` | Dependency-free contracts: `SignalLike`, `TmFormFieldControl`, the grid cell interfaces, and `TM_GRID_KEYS`, the grid's key combinations as plain data, which `./grid` re-exports |
| `@tellma/core-ui/l10n` | Framework-free number and date formats (`tmFormatNumber`, `tmFormatDate`, …), the instant formats in an explicit time zone (`tmFormatDateTime`, `tmFormatTime`, `tmFormatRelativeTime`, `tmFormatZoneOffset`, `tmFormatZoneName`), month and weekday names, `tmStripBidiControls` (no formatter writes a bidirectional control character), and the `TmCalendar` seam with the Gregorian default |
| `@tellma/core-ui/l10n-input` | The input side of l10n: `tmParseNumber` and `tmParseDate` (which read back what the formats write), the numeric precision helpers (`TM_NUMBER_MAX_DIGITS`, `tmNumberDigitCount`), `tmDatePlaceholder` and `tmFirstDayOfWeek`; its own entry point so a page that only displays values never loads the parsers |
| `@tellma/core-ui/calendar-umalqura` | `tmUmalquraCalendar()` — the Umm al-Qura (Hijri) calendar pack |
| `@tellma/core-ui/calendar-ethiopic` | `tmEthiopicCalendar()` — the Ethiopic (Amete Mihret) calendar pack |
| `@tellma/core-ui/input` | `tmInput` — a bare directive on the native `<input>` and `<textarea>` |
| `@tellma/core-ui/number` | `tmNumber` — locale-aware numeric input (percent mode, display rounding) |
| `@tellma/core-ui/date-picker` | `tm-date-picker` — calendar-aware date input + popup (ISO `YYYY-MM-DD` model) |
| `@tellma/core-ui/entity-picker` | `tm-entity-picker` — server-searched FK selector with advanced-search/create/edit modal pages (which receive an opaque `context`), and optional two-line rows (`itemDescription`) and code-first label parts (`itemLabelParts`); `tm-entity-multi-picker` — several FKs as one-line scrolling chips over the same search, with whole-selection advanced search and `maxItems` |
| `@tellma/core-ui/checkbox` | `tm-checkbox` — native-input tri-state checkbox |
| `@tellma/core-ui/radio` | `tm-radio-group` + `tm-radio` — one value from a visible list of native radios, with arrow keys mapped to the live direction |
| `@tellma/core-ui/segmented` | `tm-segmented` + `tm-segment` — the same native radios as a compact track of segments; icon segments drop their labels below a container width |
| `@tellma/core-ui/switch` | `tm-switch` — an on/off setting: a native checkbox with `role="switch"` |
| `@tellma/core-ui/search-field` | `tm-search-field` + `input[tmSearch]` — search-as-you-type with leading and trailing edges and an undoable clear (global stylesheet) |
| `@tellma/core-ui/date-range` | `tm-date-range` + `tmDateRangeOrder` — a from–to pair of coupled date pickers |
| `@tellma/core-ui/date-time` | `tm-time-field` + `tm-date-time-field` — a typed time of day, and an instant as a date and a time in an explicit zone that is always named |
| `@tellma/core-ui/form-field` | `tm-form-field` — label/hint/message chrome around any control. Validation messages are an anchored popover shown while the control holds focus, never a row under the field, so an error can never reflow the page; the words themselves live in a permanently rendered visually-hidden live region |
| `@tellma/core-ui/select` | `tm-select` over `tm-option` and `tm-option-group` — the overlay single-choice select with option headings, and the shell it shares with `tm-multi-select` |
| `@tellma/core-ui/multi-select` | `tm-multi-select` — the overlay multiple-choice select over the same options; its own entry point so a page with only single selects (a pager's page size) never loads it |
| `@tellma/core-ui/button` | `tmButton` on a `<button>` or a link — variants, sizes (sizing `tm-icon` children), the `pending` suppressed-activation state, and `disabledReason` (focusable, activation swallowed, the reason as tooltip and description) |
| `@tellma/core-ui/tabs` | `tm-tab-group` + `tm-tab` — active-only-DOM tabs with `preserveContent`, and `instantiate()` to render unselected tabs on request |
| `@tellma/core-ui/modal` | `TmModal` + `TmModalRef` — service-opened dialogs with a typed result channel: centered, as a drawer at the inline-end edge (`placement: 'end'`), or as an alert dialog (`role: 'alertdialog'`) |
| `@tellma/core-ui/popover` | `tm-popover` + `tmPopoverTriggerFor` — anchored non-modal dialog panels, content-sized or at a fixed `sm`/`md`/`lg` width |
| `@tellma/core-ui/tooltip` | `tmTooltip` — plain-text tooltips with an always-available description |
| `@tellma/core-ui/alert` | `tm-alert` — page/section status messaging with live announcement |
| `@tellma/core-ui/image` | `tm-image` — cached record images with an edit (replace/re-fit/delete) mode, and `placeholderOnError` for a placeholder fallback (an avatar's initials) |
| `@tellma/core-ui/blob-fetcher` | `TM_BLOB_FETCHER` — the seam `tm-image` fetches every blob through, with its default over `HttpClient`; its own entry point so an app's startup provides it without reaching `tm-image` (`@tellma/core-ui/image` re-exports it) |
| `@tellma/core-ui/files` | `tmFilePicker` + `tm-dropzone` — file selection over one guardrail engine |
| `@tellma/core-ui/file-preview` | `TmFilePreview` — the modal file viewer (download-only for HTML/unknown) |
| `@tellma/core-ui/menu` | `tm-menu` + `tmMenuTriggerFor` + `tmContextMenuTrigger` — data-driven menus opened from a menu button, a right-click or code. Entries are items (plain, checkbox or radio; with a description, a detail, a key-cap shortcut, `keepOpen`, `disabledReason`), links, one level of submenus, labelled groups and separators; `ng-template[tmMenuHeader]` adds a header block |
| `@tellma/core-ui/grid` | `tm-grid` — the editable data grid |
| `@tellma/core-ui/tree-grid` | `tm-tree-grid` — the hierarchical grid |
| `@tellma/core-ui/grid-engine` | The framework-free grid engine behind `tm-grid` |
| `@tellma/core-ui/pivot` | `tm-pivot` — an Excel-style pivot grid in compact form over two member trees: subtotals on parent rows and after a column's children, the totals last, five looks, text formatted lazily for the rendered cells only, widths fitted to each measure's widest value, sticky headers and leading columns, and a treegrid keyboard |
| `@tellma/core-ui/chart` | `tm-chart` — a chart from a JSON spec: column, bar, line, area, pie and doughnut plots drawn on canvas in the chart tokens' colours, a headline card, and a table view a toggle or a failed engine shows. The plot is named and described for assistive technology, never mirrors in right-to-left, and drills through `pointActivate`; its engine (Apache ECharts) loads with `import()` on the page's first plot, so a card or a table never downloads it. `TM_CHART_SPEC_SCHEMA` and `tmValidateChartSpec` check a spec |
| `@tellma/core-ui/grid-editing` | The grids' editing code — the cell editors, the editor session, the popup pane's keys and the status bar — which `tm-grid` and `tm-tree-grid` load themselves with `import()` once a grid is editable, so a readonly list never downloads it. Unstable wiring: no API goldens, never imported by app code |
| `@tellma/core-ui/spinner` | `tm-spinner` — the shared decorative pending/progress glyph |
| `@tellma/core-ui/icon` | `tm-icon` — a Lucide icon by registry name or from icon data, sized by `--icon-size`, mirrored in RTL when directional; `TmIconRegistry` + `provideTellmaIcons()` |
| `@tellma/core-ui/kbd` | The `TmKeys` notation (`'Mod+K'`), `tmKeysMatch` (layout-independent, AltGr-safe), `tmFormatKeys`/`tmKeysText`/`tmAriaKeyShortcuts`, `tmIsApplePlatform`, and `tm-kbd`'s key caps |
| `@tellma/core-ui/badge` | `tm-badge` — a status label in a colour tone, with an optional leading dot or icon; `tm-counter`, re-exported from `@tellma/core-ui/counter` |
| `@tellma/core-ui/counter` | `tm-counter` — a count inline or on its parent's corner, capped at `max` ("99+"); its own entry point so a first-frame surface paints counts without `tm-badge` |
| `@tellma/core-ui/avatar` | `tm-avatar` — a person as a round photo, or as initials on a tint a stable key picks; re-exports `tmInitials` and `tmAvatarTone` |
| `@tellma/core-ui/initials` | `tmInitials` and `tmAvatarTone` — the initials a name gives and the tone a stable key picks; their own entry point so a first-frame surface paints an avatar's face without reaching `tm-image` (`@tellma/core-ui/avatar` re-exports them) |
| `@tellma/core-ui/progress` | `tm-progress-bar` — a 4 px determinate or sweeping bar that stays at least 250 ms once shown; `tm-progress-ring` — the same as a ring around an icon button |
| `@tellma/core-ui/skeleton` | `tm-skeleton` — static line, block, circle and table placeholders with the final geometry of what replaces them |
| `@tellma/core-ui/relative-time` | `tm-relative-time` — "2 min ago", kept current by one shared minute ticker that stops while the page is hidden |
| `@tellma/core-ui/copy` | `tm-copy-button` — one-click copy with a "Press Ctrl+C to copy" fallback; `tm-reference` — a trace id or version as a mono chip with that button |
| `@tellma/core-ui/empty-state` | `tm-empty-state` — the one anatomy for "nothing to show" and inline failures: icon, heading, detail and actions |
| `@tellma/core-ui/chip` | `tm-chip` — a compact label in a badge tone, as text, a link or a button, optionally removable (Delete/Backspace); `tm-chip-set` — a list of chips with one tab stop and arrow keys, that wraps, collapses behind a "+N" chip, or scrolls in one line |
| `@tellma/core-ui/toast` | `TmToasts` + `provideTellmaToasts()` — transient messages in the block-start, inline-end corner: three on show behind an "N more" pill, six-second timers that pause on hover, focus and a hidden page, keyed replacement, `focus()` |
| `@tellma/core-ui/banner` | `tm-banner-bar` — a 32 px page-wide message bar in a tone, with an action, previous/next paging and dismiss |
| `@tellma/core-ui/busy-overlay` | `TmBusyOverlay` — `run(work)` holds the pointer and the keyboard from the calling task, shows a scrim, spinner and label after 200 ms (or at once), stays above the toasts, stands aside for a modal opened during the run (such as a confirmation the write asks for) until it closes, and returns focus when the work settles |
| `@tellma/core-ui/confirm` | `TmConfirm` — `ask()` puts a question with two or more answers in a small alert dialog and resolves to the chosen key (or `null` when dismissed); `confirm()` is the yes-or-no case, with a destructive mode that focuses Cancel |
| `@tellma/core-ui/error-dialog` | `TmErrorDialog` — a failure the user must acknowledge: the message, up to ten items and a count of the rest, a reference to copy, a Details disclosure with facts and actions, and caller footer buttons whose async `run` shows a pending state; a later `show` replaces the open dialog in place |
| `@tellma/core-ui/card` | `tm-card` — a framed panel with a heading, a count, subtitle and action slots, a body and a footer; optionally a disclosure (`collapsible`, `preserveContent`), with an error dot whose box is reserved so it never moves the header |
| `@tellma/core-ui/page-header` | `tm-page-header` — a page's title row (crumb link, `h1`, extras, end slot) and toolbar row on one line each, and a band below them that wraps |
| `@tellma/core-ui/description-list` | `dl[tmDescriptionList]` — label and value pairs, stacked or inline, in one, two or container-sized columns (global stylesheet) |
| `@tellma/core-ui/splitter` | `tm-splitter` + `tmSplitterPane` — two panes divided by a bar dragged with any pointer or moved by keyboard, the primary pane's size clamped to the container in CSS while `size` keeps the user's intent |
| `@tellma/core-ui/pager` | `tm-pager` — paging status and controls: range mode writes `page` and `pageSize`, item mode steps through single items; the status and the size select reserve their widest content, so paging never moves the buttons |
| `@tellma/core-ui/tree` | `tm-tree` + `ng-template[tmTreeNode]` — a tree on the aria tree pattern whose children load lazily into a reserved spinner slot, with none, single or multiple selection, context rows and draggable rows |
| `@tellma/core-ui/drag-drop` | `[tmDragSource]`, `[tmDropTarget]` and the `TmDragDrop` service — typed payloads over native drag and drop; a drag writes its text as `text/plain`, so text fields take it at the drop caret (global stylesheet) |
| `@tellma/core-ui/reorder-list` | `tm-reorder-list` + `tmReorderItem` / `tmReorderEmpty` — an ordered list rearranged by dragging, by a grab handle's keys or by the move menu its click opens, each step announced, that also takes drops from other sources |
| `@tellma/core-ui/tiles` | `tm-tiles` + `tmTileContent` / `tmTilesEmpty` / `tmTilesLoading` — rows as a virtualized lattice of fixed-size tiles (an image and four text corners each) that becomes a divided list on narrow containers, on the data grid's selection, activation and driver members |
| `@tellma/core-ui/tiles-tree` | `TmTilesTree` (`tm-tiles[parentId]`) — tree mode for `tm-tiles`: a single-column tree over flat rows with two-way expansion and batched lazy children, in its own entry point so a flat list never downloads it |
| `@tellma/core-ui/overlay` | `tmCreateAnchoredOverlay`, `tmLogicalPositions` and `tmPushEscapeDismissal` — the anchoring and the one Escape stack every Tellma overlay shares, for overlays built outside the library; also the `ɵ` top-layer wiring that orders the page-wide layers (modals, then the toasts, then a blocking busy overlay), and the `ɵ` touch and pen long-press observer the menu's context trigger, the tooltip and the grid share, here so a tooltip reaches it without the menu |
| `@tellma/core-ui/private` | Unstable wiring shared between Tellma libraries (the validation bubble, and the form-field seams a control provides or injects without loading the field, which `./form-field` re-exports as `TM_FORM_FIELD_CONTROL`) — no API goldens, never for app code |

## Consuming

A Tellma app imports all of these through `@tellma/core/styles.css`. Any other
application adds to its global styles (its `styles` array, or `@import` by
package name from its one stylesheet):

- `@tellma/core-ui-tokens`' emitted stylesheet (the token variables);
- this package's `fonts/fonts.css` (the self-hosted Latin/Mono faces);
- one stylesheet per BARE DIRECTIVE — `styles/tm-button.css`,
  `styles/tm-input.css`, `styles/tm-number.css`, `styles/tm-search.css`,
  `styles/tm-description-list.css`, `styles/tm-drop-target.css`. A
  directive has no view of its own to carry styles, so its appearance ships
  as a global sheet; skipping these leaves buttons, text/number/search
  inputs, description lists and drop targets unstyled. The
  authoritative list is `package.json`'s `"tellma".docs.globalStyles`, keyed
  by the entry point that needs it.

Icons draw from data, not from a font or a sprite. The library registers no
icon names: register the ones the application uses once, importing each
icon's data from `lucide` (a dependency of this package):

```ts
import { ChevronRight, HandCoins } from 'lucide';

providers: [
  provideTellmaIcons({
    'hand-coins': HandCoins,
    // Glyphs that point along the reading direction flip in RTL.
    'chevron-right': { node: ChevronRight, mirrorInRtl: true },
  }),
]
```

The build pipeline fingerprints the font binaries like any other
CSS-referenced asset (see the showcase's `angular.json` for the reference
wiring, and `@tellma/core-tools/fonts` for the post-build step that injects
`<link rel="preload">` tags for the emitted font URLs, which a distribution's
`tellma-client build` runs). Theming, sizing, and typography all flow from the token variables —
the components ship no hardcoded sizes or colors.

## Layout, theme, strings and direction

- **Breakpoints.** One closed set of widths (`sm` 576, `md` 768, `lg` 992,
  `xl` 1200, `xxl` 1440), kept once in `layout/breakpoints.json` and
  exported as `@tellma/core-ui/breakpoints.json`. Pages switch structure
  with `TmBreakpoints`; components adapt with container queries on an inner
  wrapper. Stylesheets spell width queries in range syntax at those values
  only — `(width >= 768px)`, `(width < 576px)`, `(576px <= width < 992px)` —
  which the `tm/allowed-breakpoints` stylelint rule enforces.
- **Touch sizes.** Below `sm`, a field, button or select whose `size` is
  unset renders at `lg` (46 px); an explicit size always wins, and grids
  keep their row size.
- **Theme.** `TmTheme` resolves System, Light or Dark and writes
  `<html data-theme>` (always `light` or `dark`). Seed it with
  `provideTellmaUi({ theme: { initial } })`; the app persists the choice.
- **Strings.** `provideTellmaUi({ translations })` registers the app's
  loader; loaded tables merge with the built-in English and installed locale
  packs. An entry point whose strings only it reads ships them itself (the
  pivot's `TM_PIVOT_STRINGS_EN`, the tiles tree's `TM_TILES_TREE_STRINGS_EN`,
  the chart's `TM_CHART_STRINGS_EN`)
  and registers them beside the core table, so they translate and fall
  back exactly as the core's do. ICU
  MessageFormat is interpreted, never compiled to code, so it runs under
  a Content Security Policy without `'unsafe-eval'`. Typed arguments
  (`number`, `date`, `time`) format through `TM_UI_ARGUMENT_FORMATTER`:
  numbers with `Intl`, dates and times as ISO 8601 by default, which an app
  replaces with its calendar-aware formatting.
- **Direction.** Flip `document.documentElement.dir` (and `lang`): the root
  `Directionality` follows live, so open modals mirror in place and every
  overlay opened afterwards takes the new direction.

## Authoring conventions

- The package root is the code root: every folder either is an entry point
  (has an `ng-package.json`) or belongs to the primary entry point.
- Entry points import shared code via `@tellma/core-ui` only — never a
  relative `../` path (each entry point is its own compilation unit).
- Component hosts must be `display: block`: an inline host wrapping a block
  child hit-tests above the child in Chromium, swallowing real clicks. Hosts
  that sit inside a line of text or beside a name are atomic inline boxes
  (`inline-flex`) instead, which hit-test like blocks: the glyphs
  (`tm-icon`, `tm-kbd`), `tm-badge`, `tm-counter`, `tm-avatar`,
  `tm-progress-ring`, `tm-copy-button`, `tm-reference` and `tm-chip`.
  `tm-relative-time` is plain `inline` text, since it wraps only inline
  content.
- `@tellma/core-ui/icon` and `/kbd` are the foundation every other entry
  point builds on, so they import no other `@tellma` entry point (lint
  enforces it). Library glyphs draw through `<tm-icon [svg]>` with data
  imported from `lucide`; key combinations are matched and labelled through
  the `kbd` helpers, the library's only platform check.
- Unit spec files share one browser page per run. A spec that changes the
  page irreversibly (a Content Security Policy, which can never be lifted)
  is named `*.csp.spec.ts`: the `test` target excludes that suffix and the
  `test-csp` target runs those files in a run of their own.
- Usage examples live in co-located `*.examples.ts` files (dependency-free
  template objects); they feed the docs pipeline and are compile-checked
  against the live components by `docs-examples.spec.ts`.
