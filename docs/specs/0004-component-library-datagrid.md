# Spec: UI Component Library — Data Grid & Tree Grid

- **Author:** Ahmad Akra
- **Date:** 2 July 2026

**Status:** Frozen **historical** record of the design and its reasoning at authoring time. It is not
updated as the code or its dependencies evolve.

## Context

Phase 2 of the Tellma component library ([spec 0002](0002-component-library-foundation.md)) delivers
the ERP's data-entry backbone: an Excel-like grid that enterprise users operate for hours a day with
mouse, keyboard, and copy-paste. Four products are required — readonly data grid, editable data grid,
readonly tree grid, editable tree grid — shipped as **two components**:

| Component | Selector | Covers |
|---|---|---|
| Data grid | `tm-grid` | readonly + editable flat grid |
| Tree grid | `tm-tree-grid` | readonly + editable hierarchical grid |

Readonly vs. editable is a **mode of the same component** (§5): a readonly grid is the editable grid
with editing off, and they share ~80% of the machinery (virtualization, selection, clipboard-copy,
a11y, RTL). Flat vs. tree are **separate components**: they differ in ARIA role (`grid` vs.
`treegrid`), row model (list vs. flattened hierarchy), template structure (expander column), and
paste semantics — but both are thin shells over one shared core (§2). ERP list screens are readonly
grids and ERP documents are editable line grids, so every distribution ships both modes; a
plugin/feature-directive split to tree-shake the editing code was considered and rejected as
complexity without a realistic payoff.

The design target is **Excel / Google Sheets parity** for interaction (§8) and **high copy-paste
fidelity** with Excel, Google Sheets, and other Tellma grids (§9). Where Excel and Sheets disagree,
the more data-entry-friendly behavior is chosen and noted.

All foundation decisions of spec 0002 apply unchanged: Angular v22+, zoneless, signal-first,
Signal Forms only, CSS logical properties + `Directionality` for RTL, Transloco behind
`TM_UI_TRANSLATE`, token-driven styling with the `@layer tm.base, tm.theme` cascade, the showcase
app + vitest + Playwright, per-entry-point budgets and API goldens. Implementers must use the Angular CLI
MCP (`get_best_practices`, `search_documentation`) rather than memory for framework conventions.

## Goals / Non-goals

**Goals**

- Ship `tm-grid` and `tm-tree-grid` to production quality: virtualized, a11y-complete (WCAG 2.1 AA),
  RTL-complete, brand-themed, Signal-Forms-native, harness-tested.
- Excel/Sheets interaction parity: cell/range/row/column selection, the full keyboard model,
  two editing modes, cut/copy/paste, undo/redo, insert/delete rows, a materializing "new row".
- Clipboard interop: faithful copy to Excel/Sheets; typed paste between Tellma grids; label→value
  resolution (batched, async) when pasting foreign text into enum/entity columns; per-cell error
  states with a jump-to-error tally.
- A headless, DOM-free **grid engine** (navigation, selection, clipboard serialization, undo/redo,
  tree flattening) unit-testable without a browser.
- Harden the draft `TmCellEditor`/`TmCellDisplay` contracts from spec 0002 into their final form
  (§6.3), so any current or future `tm-*` control slots into a grid cell without grid changes.
- A state-memory pattern giving column widths, scroll position, expansion state, and the undo stack
  each its expected lifetime across component destroy/recreate (§12).

**Non-goals (explicitly out of scope)**

- Column re-ordering, pinning, hiding, or auto-fit UI; row drag-and-drop re-ordering (covered by
  full-row cut/paste, §9.6); user-resizable row heights.
- Sorting, filtering, grouping, aggregation UI — the consumer owns the array it binds.
- Horizontal (column) virtualization: ERP grids run 10–40 columns; vertical virtualization is the
  scaling axis. The DOM structure (§7) does not preclude adding it later.
- Cell fill-handle (drag-to-fill), merged cells / row- and col-spans, selection statistics
  (sum/count/avg) in the status bar, CSV/XLSX file export, printing.
- Server-side / infinite row models — the grid binds a client-side array.
- Touch *optimization* beyond §8.6 (which includes range-selection handles). Touch must be
  *usable*, not native-app-grade.
- A **pivot table** control. It is a future, separate component with its own engine: it
  virtualizes both axes, has multi-level (and tree-structured) headers on both axes, aggregates,
  and binds a different input model. The grid engine is deliberately **not** generalized for it —
  only two pieces are written shape-neutral so the pivot can reuse them: the clipboard TSV/HTML
  serializer (§9.2) and the single-axis windowing helper (§4).
- The `tm-entity-picker` control (future component). Entity columns work today with a
  consumer-supplied editor template (§6.2); numeric/date columns work with the built-in text editor
  plus per-column `parse`/`format` until dedicated controls ship.

## 1. Packages & entry points

Everything lives in `@tellma/core-ui` as secondary entry points (per-component entry points and
budgets, as in spec 0002):

| Entry point | Contents |
|---|---|
| `@tellma/core-ui/grid-engine` | The headless engine: pure TypeScript + `@angular/core` signals only — **no DOM, no DI, no components** (enforced by the same boundary lint that guards `contracts`). Navigation, selection, editing state machine, clipboard serialization (TSV and string-built HTML) + paste shaping over string matrices, undo/redo, tree flattening. Parsing foreign HTML needs `DOMParser`, so it lives in the `grid` layer (§9.3). |
| `@tellma/core-ui/grid` | `tm-grid`, the column directives, the built-in cell editors' wiring, the context menu, the status bar. Depends on `grid-engine`, `contracts`, CDK (`Directionality`, `LiveAnnouncer`), and the existing controls (`input`, `checkbox`, `select`, `spinner`) for built-in editors and pending affordances. |
| `@tellma/core-ui/tree-grid` | `tm-tree-grid` + tree-specific row model. Depends on `grid` (shared internals are exported through a private-by-convention `ɵ` surface, excluded from the API golden). |
| `@tellma/core-ui/menu` | `tm-menu` + `tmContextMenuTrigger` (§8.5) — a general-purpose menu component the grid consumes, built on `@angular/aria/menu` (`ngMenu`/`ngMenuItem`/`ngMenuTrigger`) + the CDK-overlay composition proven by `tm-select`. |
| `@tellma/core-ui/contracts` | Gains the hardened `TmCellEditor`/`TmCellDisplay` (§6.3) and the new `TmGridStateStore` types (§12). Stays free of Angular component/DI imports. |
| `@tellma/core-ui-testing` | `TmGridHarness`, `TmGridCellHarness`, `TmGridRowHarness`, `TmTreeGridHarness`. |

Spec 0002 reserved a headless engine "in its own package"; an entry point delivers the same
isolation and tree-shaking boundary without a second npm artifact to version, so the engine ships as
an entry point.

## 2. Why a bespoke engine (and not `@angular/aria`'s grid)

`@angular/aria` ships `ngGrid`/`ngGridRow`/`ngGridCell`/`ngGridCellWidget` (stable, v22). It was
evaluated against its source at `@angular/aria@22.0.3` (latest stable at authoring) and rejected as
the grid's foundation:

- **Its coordinate space is the rendered DOM.** Navigation, focus, and selection operate on the
  directive-registered cell collection. Under virtualization only ~30 of potentially 100k rows
  exist, so Ctrl+End, PageDown, select-column, and range selection cannot reach unrendered rows.
- **Selection is a per-cell boolean model** (`[(selected)]` per cell directive). Excel-style
  selection needs O(1) range descriptors over the *data*, independent of what is rendered.
- **The Excel range gestures are not wired.** As of 22.0.3 (and `22.1.0-next.2`), there is no
  Shift+Arrow anchor extension, no Shift+Click, and no drag selection (the pattern's `dragging`
  signal is unused; the `enableRangeSelection` input in the online docs is unreleased).
- **Its focus-restore heuristics fight virtualization**: unmounting the focused cell on scroll is
  indistinguishable, to the pattern, from a deletion.

The aria docs' virtualization accommodation — `focusMode="activedescendant"` ("better for virtual
scrolling") and the `rowIndex`/`colIndex` inputs feeding `aria-rowindex`/`aria-colindex` — keeps
the *ARIA attributes* coherent when the DOM is a window, but navigation and selection still operate
on rendered cells only; it does not change the analysis above.

What the aria grid does well is **adopted as design blueprint** rather than as a dependency: the
widget-activation model for editing (Enter/typing activates, Enter commits, Esc cancels, grid
navigation pauses while active), direction-aware arrow-key mapping, and the roving-focus management
recipes. ARIA semantics follow the W3C APG grid/treegrid patterns directly (§14). If a later
`@angular/aria` release ships virtualization-aware, range-capable grid behavior, the engine boundary
(§1) is the seam where it could be swapped in.

**Engine shape.** The engine is a set of small classes composed by the components, mirroring the
aria pattern layering: `GridDataModel` (rows × columns in *model space*, tree flattening),
`GridNav` (active cell, anchor, Excel motion including Ctrl+Arrow data-edge jumps),
`GridSelectionModel` (§8.1), `GridEditState` (§8.4), `GridClipboard` (§9), `GridHistory` (§11).
All state is signals; all inputs are `SignalLike`/`WritableSignalLike` from `contracts`, so the
engine is constructible in a plain vitest test with no TestBed.

## 3. Anatomy

```
┌─ tm-grid ────────────────────────────────────────────────────┐
│ ┌─ corner ─┬─ column headers (sticky top) ──┬─ find bar ───┐ │
│ ├──────────┼────────────────────────────────┴──────────────┤ │
│ │ row hdrs │  cell viewport (vertically virtualized)       │ │
│ │          │  · static display cells                       │ │
│ │          │  · ≤1 live editor (edited cell only)          │ │
│ │          │  · new-row placeholder (editable mode, §5.5)  │ │
│ ├──────────┴───────────────────────────────────────────────┤ │
│ │ status bar (editable): error tally chip (§10) · live rgn │ │
│ └───────────────────────────────────────────────────────────┘│
└──────────────────────────────────────────────────────────────┘
```

One scroll container owns both axes. The header row (including the corner) is
`position: sticky; top: 0`. The row-header column is deliberately **not** sticky: its content is
just row numbers, so pinning it buys little on wide monitors and wastes horizontal space on
phones — it scrolls with the content. The find bar (§8.7) floats at the top inline-end corner when
enabled; the status bar sits outside the scroll container and renders in editable mode only.

## 4. Rendering & virtualization

- **Div-based DOM with grid roles**, not `<table>` (tables fight sticky positioning, virtualization
  transforms, and per-column width control). Each row is `display: grid` with
  `grid-template-columns: var(--grid-template)`; a column resize updates that one custom property
  on the host — one style write restyles every row.
- **Bespoke fixed-row-height windowing**, not CDK `<cdk-virtual-scroll-viewport>`. Row height is
  fixed and token-known (§7), so the math is trivial: total height = a spacer of
  `rowCount × rowHeight`; the window renders the visible rows ± an overscan (default 4 rows each
  side) positioned by a single translate. CDK's viewport was rejected because its rendered-content
  transform breaks `position: sticky` headers sharing the scroll container, it brings measurement
  machinery this fixed-height case doesn't need, and it owns the scroll element the grid needs for
  two-axis layout.
- **The active row is always rendered**, even when scrolled out of the window. This guarantees the
  focused cell (or open editor) is never unmounted by scrolling — focus is never lost, and edit
  state survives. Scrolling alone never closes or moves an editor; a further editor keystroke
  scrolls the edited cell back into view first (the Excel behavior — the user never types blind).
- **Cells are static DOM.** The default cell renders the column's formatted text in a single
  element; `boolean` columns render a token-styled glyph. No Angular component per cell. A
  `*tmGridDisplay` template (§6.1) instantiates an embedded view per rendered cell — documented as
  the costlier path, to be kept static and cheap. Exactly one live editor exists grid-wide (§8.4).
- `@for` over the window tracks by `rowId(row)` (§5.2), so scrolling reuses row views.
- Row heights are not user-resizable; content is single-line with `text-overflow: ellipsis`.
- Column widths: fixed `width` (px) **or** proportional `flex` (a fraction share of leftover
  space, emitted as `minmax(minWidth, ‹flex›fr)` — native to the `grid-template-columns` scheme,
  so proportional columns re-flow on container resize for free). User resize via a drag handle on
  the header's trailing edge (pointer events, live update, ≥24px hit area) converts that column to
  a px width (Excel behavior); widths are view state remembered per §12. Grid-wide default minimum
  from tokens.

## 5. Data model & Signal Forms binding

### 5.1 Inputs

```html
<!-- readonly -->
<tm-grid gridId="invoice-lines" [data]="lines()" [rowId]="r => r.id"> … </tm-grid>

<!-- editable: bound to a Signal Forms FieldTree over the array -->
<tm-grid gridId="invoice-lines" [field]="form.lines" [rowId]="r => r.id"
         [newRow]="makeLine" [readonly]="!editing()"> … </tm-grid>
```

- `data: Signal`-compatible input of `readonly T[]` — readonly binding.
- `field: FieldTree<T[]>` — editable binding. Rows come from `field().value()`; cell writes go
  through the child field (`field[i][key]().value.set(...)`), so the consumer's `applyEach` schema
  validates every write and per-cell state (`invalid`, `errors`, `disabled`, `readonly`) is read
  from the child field. Exactly one of `data`/`field` must be bound.
- `readonly: boolean` (default `false`) — with `field` bound, toggles view/edit of the same screen.
  **Editable ⇔ `field` bound ∧ `!readonly`.**
- The grid never defines validators and never blocks a commit on validation — invalid values are
  written and displayed in error state (§10), the Excel-like non-modal flow.
- Field-level `disabled`/`readonly` win over column-level editability (same precedence rule as
  spec 0002: the field is authoritative when bound).

**Mode transitions.** Flipping `readonly` never mutates data: an open editor **cancels** (its
in-progress text is discarded, never committed — the flip usually follows a Save/Cancel action,
and a mode change must not write as a side effect), a pending cut clears, and in-flight §9.4
resolutions abort. The placeholder row disappears (the active cell clamps to the nearest data
row), the status bar hides, and invalid-input raw texts stop rendering — readonly always shows the
model's truth — while the invalid-input map, undo stack, selection, and scroll survive the flip
and return with edit mode. Focus follows the editor-close path back to the active cell.

### 5.2 Row identity

`rowId: (row: T) => string | number` is **required**. Undo/redo, selection stability across
inserts/deletes, view reuse, and error-state bookkeeping all key on it, never on the index. New
unsaved rows must get client-side temporary ids from the consumer's `newRow` factory.

### 5.3 External data changes

The consumer owns the array; the grid reconciles in-place changes (server recalculations,
refreshed rows) by `rowId`: selection ranges remap to the rows' new positions (a range whose rows
vanished shrinks or drops); the active cell follows its row, falling back to the nearest row in
the same column; an open editor is cancelled (with an announcement) if its row disappears, and
otherwise keeps editing — its commit wins over the background write; invalid-input entries (§10)
survive refreshes and drop with their row; undo entries apply by `rowId`, and one whose rows no
longer exist is skipped with an announcement. A wholesale reload is the consumer's
`clearHistory()` moment (§12); programmatic edits that should be user-undoable go through
`applyTransaction()` (§11).

### 5.4 Empty & loading states

Grid-owned, so every Tellma screen behaves alike: while `loading` is set the grid sets
`aria-busy`, keeps the column headers rendered, and shows a `tm-spinner` overlay with a localized
string (`*tmGridLoading` overrides the content). A bound, non-loading, zero-row readonly grid
shows a centered localized empty message (`*tmGridEmpty` overrides). In editable mode the new-row
placeholder (§5.5) is the empty state. Transitions are announced.

### 5.5 New-row placeholder (editable mode)

With `newRow: (parent?: T) => T` bound, the grid appends one placeholder row after the last data
row — visually distinct, an asterisk in its row header, **not** part of the bound array or field
tree. Typing (or pasting, or committing an editor) in any of its cells *materializes* it: the grid
calls `newRow()`, pushes the result into the model through the field, and the commit lands on the
now-real row's field; a fresh placeholder appears beneath. The placeholder cannot be deleted, no
row can be inserted below it, and it is skipped by range operations (copy/delete). Without
`newRow`, there is no placeholder and no paste-overflow row creation (`canAddRows ⇔ newRow`
bound).

## 6. Column model

### 6.1 Column definition

Columns are declared as content children — definition-only directives, in display order:

```html
<tm-grid …>
  <tm-grid-column key="quantity" type="number" header="Qty" [width]="90" align="end" />
  <tm-grid-column key="isPosted" type="boolean" header="Posted" [readonly]="true" />
  <tm-grid-column key="gender" type="enum" header="Gender"
                  [options]="genders" [optionLabel]="g => g.label" [optionValue]="g => g.code" />
  <tm-grid-column key="agentId" type="entity" header="Agent"
                  [format]="agentLabel" [resolvePastedLabels]="resolveAgents">
    <ng-template tmGridEditor let-cell> … consumer editor hosting a TmCellEditor … </ng-template>
  </tm-grid-column>
  <tm-grid-column type="custom" header="Total" [value]="r => r.qty * r.price" /> <!-- accessor ⇒ readonly -->
</tm-grid>
```

Key inputs of `tm-grid-column<T, V>`:

| Input | Meaning |
|---|---|
| `key?: keyof T & string` | Model property; also the child-field key in editable mode. Omitted ⇒ accessor column via `value: (row: T) => V`, always readonly. |
| `type` | `'text' \| 'number' \| 'boolean' \| 'date' \| 'enum' \| 'entity' \| 'custom'` — selects the built-in formatter, parser, and editor (§6.2). |
| `header` | String label, or a `*tmGridHeader` template for rich/interactive headers (interactive children don't trigger column selection, §8.3). |
| `format?: (value: V, row: T) => string` | Display-string override. This string is what copy exports (§9). |
| `parse?: (text: string, ctx: TmParseContext) => V \| TmParseError` | Text→value for typed paste and text-editor commit (types in §6.3; `ctx` carries the active locale and, during paste, the source-locale hint). Required for `date` (until a date adapter exists). |
| `defaultValue?: V` | The column's **cleared value** — what Delete and error-clearing write. Absent: `false` for `boolean` columns, else `null`. |
| `options?`, `optionLabel?`, `optionValue?` | `enum` columns: the option list and its label/value accessors — drives display, the `tm-select` editor, and synchronous label→value paste matching. |
| `resolvePastedLabels?: (labels: string[], ctx: TmPasteContext) => Promise<ReadonlyMap<string, TmLabelResolution<V>>>` | Batched async label→value resolution for `enum`/`entity` paste (§9.4); each result is a value, `notFound`, or `ambiguous` (§6.3). |
| `readonly?: boolean \| ((row: T) => boolean)` | Column- or per-cell-level editability (field state still wins, §5.1). |
| `width?`, `flex?`, `minWidth?` | Fixed px width or proportional share (§4). |
| `align?` | `'start' \| 'end' \| 'center' \| 'left' \| 'right'` — logical **or** physical. Physical values exist because numerals stay right-aligned in RTL locales too (Excel's own RTL behavior). Defaults by `type`: `number`/`date` → `right`, `boolean` → `center`, else `start`. |
| `*tmGridDisplay` template | Custom static display DOM (context: row, value, field state). |
| `*tmGridEditor` template | Custom editor hosting a control that implements `TmCellEditor` (§6.3). |

**What `type` buys.** `type` is a *defaults bundle*: it selects the built-in formatter, parser,
editor, alignment, and clipboard/paste behavior (`enum` = synchronous label matching, `entity` =
the async resolver pipeline) in one word — most columns are one-liners. `format`, `parse`,
`*tmGridDisplay`, and `*tmGridEditor` are per-concern overrides of that bundle.

Built-in defaults by `type`: `text` pass-through; `number` formats via `Intl.NumberFormat` and
parses with symbols derived from it — `formatToParts` yields the locale's group/decimal separators
and numbering-system digits, so localized separators and e.g. Eastern-Arabic numerals round-trip,
with `ctx.sourceLocale` honored before the active locale (§6.3) — replaced internally by
`TmNumberAdapter` when it ships, no API change; `boolean` glyph
(never text; copies as `TRUE`/`FALSE`, parses those case-insensitively plus `1`/`0`); `enum` maps
through `options`/`optionLabel`/`optionValue` synchronously (its labels resolve without a server);
`entity` requires `format` (id→label) and resolves pasted labels per §9.4; `date` requires
consumer `format`/`parse` until the date adapter exists. **`custom` assumes nothing:** display
falls back to `String(value)` unless `format` is given; the column is editable only if `parse` or
`*tmGridEditor` is provided (a `parse`-less, editor-less custom column is effectively readonly).

**Display precedence:** a `*tmGridDisplay` template wins for the rendered DOM, but `format` (or
the type default) still defines the cell's **text representation** — what copy exports, what the
find bar (§8.7) searches, and what announcements speak. The two compose; they don't compete.

Template context guards (`ngTemplateContextGuard`) give
`*tmGridDisplay`/`*tmGridEditor`/`*tmGridHeader` full row/value typing.

### 6.2 Built-in editors

| Column type | Editor |
|---|---|
| `text` | bare `<input tmInput>` |
| `number`, `date`, `custom` | bare `<input tmInput>` + column `parse`/`format` |
| `boolean` | none — the cell **toggles directly** (Space/click/Enter), no edit mode |
| `enum` | `tm-select` populated from `options`, panel anchored to the cell rect |
| `entity` | none built-in — consumer `*tmGridEditor` (future `tm-entity-picker` becomes the default) |

**Why `boolean` doesn't mount `tm-checkbox`.** Three reasons: the static-DOM rule (§4) forbids a
component instance per cell, and a toggle is atomic — there is no editing *session* to open,
commit, or cancel; and nesting a real checkbox would put a second interactive control inside the
`gridcell` semantics. The glyph is rendered from `tm-checkbox`'s own `TmCellDisplay`
implementation (`formatValue`/`displayClass`, §6.3), so the visual stays in lock-step with the
real control, and toggles register in undo like any cell write.

**Future controls slot in without grid changes**: when `tm-date-picker` ships (a calendar-dropdown
control on the same overlay infrastructure as `tm-select`, per spec 0002's roadmap), it registers
via `TM_CELL_EDITOR_HOST` (§6.3) and becomes the `date` default — typing stays available through
its input; the calendar button is part of the control, its panel anchored to the cell rect. Same
path for `tm-entity-picker`.

### 6.3 `TmCellEditor` / `TmCellDisplay` — hardened contracts

The spec-0002 drafts are finalized as follows (this spec is their owning definition; the
`contracts` entry point is updated accordingly):

```ts
/** Implemented by any control mountable as a grid cell editor. */
export interface TmCellEditor<T> {
  /** The grid owns the value channel; commit/cancel write/restore through it. */
  readonly value: WritableSignalLike<T>;
  /**
   * Editor's committed-text view of its current content, or null when the content is not
   * representable as T (grid records it as an invalid input, §10).
   */
  readonly text: SignalLike<string | null>;
  commit(): void;                       // flush pending text→value into the value channel
  cancel(): void;                       // restore the value present at open
  focus(): void;                        // text editors place the caret at the end
  /** Seed for type-to-edit: replace content with `text`, caret at end. */
  seed?(text: string): void;
}

/**
 * Pure display path — no component instance; paints thousands of readonly cells.
 * A contract IMPLEMENTED BY tm-* controls (checkbox, select, …) so the grid's built-in column
 * types render control-faithful static cells; distinct from the consumer-facing *tmGridDisplay
 * template, which is the per-column custom-DOM override (§6.1).
 */
export interface TmCellDisplay<T> {
  formatValue(value: T, locale: string): string;
  displayClass?(value: T): string;      // token-driven glyph class (boolean box, etc.)
}

/** Sentinel a column `parse` returns for unparseable text (distinct from a legitimate null value). */
export const TM_PARSE_ERROR: unique symbol = Symbol('TM_PARSE_ERROR');
export type TmParseError = typeof TM_PARSE_ERROR;

/** Context handed to a column `parse` (§6.1). */
export interface TmParseContext {
  readonly locale: string;         // the grid's active locale
  readonly sourceLocale?: string;  // during paste: the copying grid's locale, from clipboard metadata
}

/** Context handed to `resolvePastedLabels` (§9.4). */
export interface TmPasteContext extends TmParseContext {
  readonly sourceTenant?: string;  // from clipboard metadata — drives the cross-tenant guard (§9.4)
  readonly signal: AbortSignal;    // aborts when every cell awaiting this call has been invalidated
}

/** One label's outcome from `resolvePastedLabels` (§9.4). */
export type TmLabelResolution<V> = { value: V } | { error: 'notFound' | 'ambiguous' };

/**
 * Registration sink the grid provides to editor templates. A token *provided by* a control inside
 * a dynamically created embedded view is not reachable through public query APIs, so discovery is
 * inverted: the grid passes an injector carrying this host to the view it creates, and the editor
 * control registers itself. (The injection token lives in the primary `@tellma/core-ui` entry
 * point — component-free, and already a dependency of every control — so controls need no import
 * from `/grid` and no control↔grid entry-point cycle arises.)
 */
export interface TmCellEditorHost {
  register(editor: TmCellEditor<unknown>): void;
}
```

Changes vs. the drafts, and why: `onKeydown` is **dropped** — keys reach the editor by normal DOM
focus and bubble to the grid, which acts only on keys the editor did not `preventDefault` (this is
how the two-stage Esc of spec 0002 composes: `tm-select` consumes Esc №1 to close its panel; Esc №2
bubbles and the grid calls `cancel()`). `text` and `seed` are **added** for type-to-edit and
invalid-input capture. `readonlyClass` is renamed `displayClass`.
Editor discovery is by **self-registration**: the grid instantiates `*tmGridEditor` templates (and
its built-in editors) with a cell-scoped injector providing the `TM_CELL_EDITOR_HOST` token
(`TmCellEditorHost` above); every `tm-*` form control — and any consumer control implementing
`TmCellEditor` — injects it optionally and registers itself on construction. New controls slot in
without grid changes.

## 7. Styling & theming

- New `grid` (and `menu`) groups under `TmTokens.component`, emitted as `--grid-*` / `--menu-*`
  custom properties per the foundation's component-token convention (`--checkbox-box-size`, …),
  validated by the schema + missing-ref gate; color contrast is verified by the axe battery over
  the grid's rendered states (§14) — the foundation's contrast posture. The `grid` group covers:
  row heights per density (`sm`/`md`/`lg` via the grid's `size` input, matching the field-height
  scale), header background/text, gridline color, selection fill (translucent brand teal) +
  selection border, active-cell ring (reuses `--focus-ring`), error tint + error badge,
  readonly-cell tint, new-row glyph, cut-marquee (dashed border), find-match highlight +
  active-match outline, touch selection handles, zebra stripe for readonly grids.
- **Readonly mode restyles**: vertical gridlines removed; alternating row background in `tm-grid`
  only — `tm-tree-grid` keeps a uniform background (striping fights the indentation the eye uses
  to read hierarchy). Editable mode: full gridlines, no zebra.
- All sizing from tokens (the stylelint no-hardcoded-sizes rule applies); glyphs are inline SVG per
  spec 0002's icon posture.
- Forced-colors and reduced-motion honored and Playwright-gated (selection/active states must
  survive `forced-colors: active` via system colors, not tint alone).

## 8. Interaction model

Throughout: **Mod** = Ctrl on Windows/Linux, ⌘ on macOS (detected once, CDK `Platform`).
All horizontal semantics are logical (RTL-aware): "next cell" = inline-end direction.

### 8.1 Selection model

Selection lives in the engine, in **view space** (visible-row index × column index — for the tree
grid, over the flattened visible rows):

```ts
interface TmGridRange { anchor: RowCol; focus: RowCol; kind: 'cells' | 'rows' | 'cols' | 'all'; }
// selection = TmGridRange[]  (last = active range) + activeCell + tab-run origin column
```

- Ranges are rectangles; `kind` records whether the user selected full rows/columns/all (drives
  context-menu contents and row-move semantics) — a `rows` range always spans every column.
- Multiple discontiguous ranges accumulate via Mod+Click / Mod+Drag (Excel model). **Multi-range
  operations**: Delete clears every range; row operations count the union of rows the ranges span;
  paste and cut use the active range only; copy follows Excel's alignment rule — ranges that
  compact into one rectangle (identical column span stacked, or identical row span abreast — the
  Mod-selected-rows case) copy as that compacted block, anything else is refused with an
  announcement.
- Headers are never *in* a range; they highlight when their row/column intersects one.
- Row inserts/deletes remap ranges by `rowId`; expand/collapse in the tree grid collapses selection
  to the active cell (predictability over cleverness).
- Selection state is O(ranges), never per-cell — select-all on 100k rows is one descriptor.

### 8.2 Keyboard reference

Navigation & selection (grid focused, no editor open):

| Keys | Action |
|---|---|
| Arrows | Move active cell; collapses selection to it |
| Mod+Arrow | Jump to edge of contiguous data block (Excel) |
| PageUp / PageDown | Move one viewport page |
| Home / End | First / last cell in row |
| Mod+Home / Mod+End | First / last cell of grid |
| Shift + any motion above | Extend active range from anchor (incl. Mod+Shift+Arrow, Shift+PageDown, …) |
| Shift+Space / Ctrl+Space | Select row(s) / column(s) of the active range (Excel). Column select is **Ctrl literally on every platform** — ⌘+Space is Spotlight, and Excel for Mac uses Ctrl+Space too |
| Ctrl+Shift+Space | `selectable` grids (§8.8): toggle the select-all checkbox (all ↔ none; Ctrl literal, as above) |
| Mod+A | Select all |
| Tab / Shift+Tab | Editable grid: next/previous **editable** cell, wrapping across rows; from the last cell of the placeholder row (or last row), Tab exits the grid. Readonly grid: exits the grid immediately (APG-conformant single tab stop). Mid-grid exit: Esc → Tab (below). |
| Enter | Editable cell: open editor (*edit mode*, caret at end — Sheets behavior); `boolean` cells toggle instead (§6.2). Readonly cell: activate its first interactive element when it has one (a first-column record link — the APG rule); else move down (never a dead key) |
| Shift+Enter | Move up (editable and readonly alike) |
| F2 | Open editor (*edit mode*); while editing, toggles *enter mode* ↔ *edit mode* (Excel) |
| Any printable char | Open editor (*enter mode*) seeded with the char, replacing content |
| Delete / Backspace | Clear selected range(s) to each column's cleared value (§6.1; readonly cells untouched; one undo op) |
| Esc | Dismiss in order: pending cut marquee (§9.5) → else move focus to the **grid container itself**, from which Tab/Shift+Tab leave the grid natively (any arrow or Enter re-enters the cells). This is the practical mid-grid exit — a 3,000-row grid cannot ask for Tab-to-the-end (WCAG 2.1.2) |
| Menu key / Shift+F10 | Open context menu at active cell |
| Mod+F | Focus the find bar (§8.7; only when `searchable` — otherwise the browser keeps it) |

Editing (editor open; keys the editor doesn't consume):

| Keys | Action |
|---|---|
| Enter / Shift+Enter | Commit; move down / up. After a Tab run, Enter returns to the run's origin column on the next row (Excel line-entry flow) |
| Tab / Shift+Tab | Commit; move the selection to the next / previous editable cell — no editor opens (Excel behavior; the follow-on edit is one keystroke via type-to-edit) |
| Esc | Cancel edit, restore value |
| ←/→ arrows | *Enter mode* (opened by typing): commit + move. *Edit mode* (Enter/F2/double-click): move the caret inside the editor |
| ↑/↓ arrows | Commit + move up/down in **both** modes — single-line cells have no vertical caret motion (Excel/Sheets behavior). An editor that needs them (an open dropdown, a future multi-line editor) consumes them (§6.3) and the grid stays out |
| PageUp / PageDown | Commit + move one viewport page (both modes) |
| Alt+ArrowDown | On enum/entity cells: open the dropdown (also opens the editor from navigation state) |

Clipboard, history, rows:

| Keys | Action |
|---|---|
| Mod+C / Mod+X / Mod+V | Copy / cut / paste (§9) — native clipboard events |
| Mod+D | Fill down (Excel): copy the active range's top row into the rows below it; single-cell selection copies the cell above; readonly cells skipped; one undo op |
| Mod+Z / Mod+Y, Mod+Shift+Z | Undo / redo (§11) |
| Mod+Alt+Minus | Delete selected row(s) — the Sheets binding. Excel's bare Mod+Minus / Mod+Shift+Plus are deliberately **not** bound: browsers reserve them for page zoom (the same conflict that made Sheets pick Alt) |
| Mod+Alt+Plus | Insert row above active/selected row(s) |

Tree grid additions (§13):

| Keys | Action |
|---|---|
| Alt+ArrowRight / Alt+ArrowLeft | Expand / collapse the active row (any column). `preventDefault`'d — browsers honor it for their Alt+Arrow history navigation; on macOS Option+Arrow is free |

### 8.3 Mouse & pointer

| Gesture | Action |
|---|---|
| Click cell | Activate + select it (commits any open editor first) |
| Drag from a cell | Rectangular range selection (pointer capture; auto-scroll near viewport edges) |
| Shift+Click | Extend range from anchor |
| Mod+Click / Mod+Drag | Add a discontiguous range; on row headers, non-contiguous rows |
| Click row header / drag across headers | Select row(s); Shift/Mod compose as above |
| Click column header | Select column — only when the press lands on the header background/label, never on interactive projected content |
| Click corner | Select all |
| Double-click cell | Open editor (*edit mode*), caret at end (Sheets behavior) |
| Right-click | If the target is outside the selection, select it first; then open the context menu (native menu suppressed) |
| Header edge drag | Column resize (live) |

### 8.4 Editing lifecycle

At most one editor exists. Opening: Enter/F2/double-click (*edit mode* — caret at end, arrows move
the caret) or typing (*enter mode* — content replaced by the seed, arrows commit-and-move); F2
toggles the mode. The editor mounts inside the cell box, receives real focus, and the value channel
is grid-owned per `TmCellEditor`. Commit paths: Enter/Tab/arrow-in-enter-mode, clicking another
cell, or the grid losing focus (commit-on-blur — safer for forms than Excel's keep-editing).
Cancel: Esc (after any editor-internal Esc stage, §6.3). Commit writes through the Signal Forms
field; validation errors mark the cell (§10) but never block. Unparseable text (per column `parse`)
is recorded as an invalid input (§10) with the model field cleared to the column's cleared value
(§6.1) — the model and the display are never silently out of sync.

**IME composition.** Type-to-edit must not eat CJK input: a `keydown` that is part of composition
(`isComposing`, or the legacy keyCode 229) opens the editor **unseeded** and moves focus into its
input immediately, so the composition session proceeds — and commits — inside the editor, never
against the non-editable cell. A Playwright CJK spec pins this.

While an editor is open, grid navigation keys are paused (the aria widget-activation model);
clipboard events target the editor (plain text editing inside the cell, Excel parity).

### 8.5 Context menu — on the reusable `tm-menu`

Context menus are a general library need (grids, list screens, nav trees), and both ecosystems the
library benchmarks against treat them as first-class reusable components (PrimeNG's `ContextMenu`;
Material's menu + the CDK's dedicated context-menu-trigger primitives). So the menu ships as its
own component, **`tm-menu`** in `@tellma/core-ui/menu`, and the grid is merely its first consumer:

- Built on `@angular/aria/menu` (`ngMenu`/`ngMenuItem`/`ngMenuTrigger` own the keyboard model,
  typeahead, roles) composed with the CDK-overlay pattern `tm-select` established
  (`usePopover: 'inline'`, position at a point or an anchor rect, `Directionality`-mirrored).
- Scope for this phase: flat items + separators, disabled state, a leading **icon** slot, item
  shape `TmMenuItem { id, label | labelKey, icon?, disabled?, action }`. Submenus (aria supports
  them) are deferred until a consumer needs one.
- `tmContextMenuTrigger` opens it at the pointer on right-click / Menu key / long-press.

The grid's menu: built-in items (localized through `TM_UI_TRANSLATE`, each with a built-in
inline-SVG icon per spec 0002's static-glyph posture — Lucide-derived, `tm-`-classed,
`aria-hidden`; grayed per mode/permissions): Cut, Copy, **Copy with headers**, Paste, Insert N
row(s) above, Insert N row(s) below, Delete N row(s). The row items appear in editable mode with
any selection — N = the union of rows the selection spans (§8.1), the Sheets behavior. Tree grid
adds Insert child row (§13.4). Consumers extend via `extraMenuItems: TmMenuItem[]` (consumer icons
are inline-SVG `TemplateRef`s — the icon registry remains a future concern per spec 0002).

Menu Copy/Cut/Paste use the async Clipboard API: writes succeed everywhere in a user gesture;
reads prompt in Chromium (permission) and Firefox 127+ (paste popup) and use WebKit's platform
paste UI in Safari. Where the read is unavailable, the Paste item shows the "use Mod+V" hint
instead — the Sheets-established fallback. Keyboard clipboard never depends on any of this (§9.1).

### 8.6 Touch

Tap = activate/select; double-tap = edit; long-press = context menu; native pan scrolling
(finger-drag on cells never selects ranges — that's what makes scrolling usable). **Range selection
on touch uses selection handles**, the Sheets/Excel-mobile pattern: on coarse-pointer devices a
tapped selection shows two round drag handles at its start/end corners; dragging a handle extends
the range (pointer capture + edge auto-scroll) while plain pans keep scrolling. Resize and
selection handles expose ≥24px hit areas. Conformance target is the WCAG 2.2 AA 24px rule with the
dense-data exceptions, per spec 0002's sizing posture.

### 8.7 Find in grid

Opt-in via `searchable` (readonly and editable alike). A find bar floats at the grid's top
inline-end corner (Sheets-style overlay, mirrors in RTL): a text field, a match counter
("3 of 41"), next/previous buttons.

- Matching is a case-insensitive substring test against each cell's **text representation** — the
  same `format` output copy exports (§6.1), so what you can see and copy is what find searches.
  The scan is debounced and chunked so large grids produce no long tasks (§16).
- Matches highlight (rendered window only; the match *list* spans the whole model); the nearest
  match is scrolled into view and outlined. Enter / Shift+Enter (and the buttons) cycle matches —
  navigating **activates** the match's cell, so grid operations apply to it. Esc clears the query
  and returns focus to the grid at the current match.
- Mod+F focuses the find bar while the grid has focus (browser find is intentionally shadowed
  there — the Sheets trade-off; the page-level Mod+F is untouched when focus is outside the grid).
- Tree grid: the scan includes rows hidden inside collapsed subtrees; navigating to such a match
  expands its ancestors.
- The counter is announced via the live region; the field is labelled from a library string.

### 8.8 Row checkbox selection (list screens)

List screens **activate** records through consumer-rendered links in the first column
(`*tmGridDisplay` — native href affordances: open-in-new-tab, middle-click, copy-link; keyboard
activation via Enter, §8.2) and **select** records for bulk actions through a grid-provided
checkbox column, opted in via `selectable`. `selectable` is supported on **readonly grids only** —
bulk selection is a list-screen affordance; enabling it on an editable grid is a dev-mode error:

- A checkbox column renders at the inline-start of the data columns (after the row header), with
  a tri-state select-all checkbox in its header; Shift+click checks a range (the Gmail model).
  Space toggles the active row's checkbox; Ctrl+Shift+Space toggles the select-all checkbox — the
  keyboard path to a header widget that sits outside the cell coordinate space.
- Selection state is the two-way `selectedIds` model (a `ReadonlySet` of `rowId`s), fully
  independent of cell-range selection: bulk actions read exclusively from it, ranges drive copy —
  the two never interact, and their styling is distinct (persistent row tint vs. the translucent
  range fill).
- The checkbox column lives **outside the cell coordinate space**: ranges never include it, copy
  and find never see it, arrows never land on it — like the row header, it is chrome.
- Checked rows carry `aria-selected` (range `aria-selected` stays cell-scoped); changes announce
  "N of M selected". The placeholder row (§5.5) has no checkbox; in the tree grid, checking a
  parent does not cascade to descendants.

## 9. Clipboard

### 9.1 Transport

Copy/cut/paste ride the **native `ClipboardEvent`s** (`copy`/`cut`/`paste` fire on the focused
cell and bubble to the grid host): synchronous, permission-free, works in every engine. Oversize
copies (beyond ~100k cells) escalate, within the same user gesture, to `navigator.clipboard.write`
with promise-backed `ClipboardItem`s so serialization chunks across frames instead of blocking the
copy event; where that path is unavailable the event path serializes synchronously under a busy
cursor. **A failed copy is never silent**: if the async write rejects (focus loss, permission
revocation, expired user activation), the grid announces the failure through the live region and
shows a transient failure notice on the §10 overlay surface, inviting a retry — nothing can be
re-attempted programmatically once the gesture is gone. The async Clipboard API is otherwise used
only by the context menu (§8.5). During editing, events go to the editor untouched.

### 9.2 Written formats (copy/cut)

Both flavors are always written:

- **`text/plain`** — TSV, Excel quoting rules (quote cells containing tab/newline/quote; CRLF row
  separators). Cell content = the column's `format` output — exactly what the user sees.
- **`text/html`** — a `<table>` of the same display strings (Excel and Sheets both parse it), plus
  Tellma metadata woven into the markup: on `<table
  data-tm-grid='{"v":1,"tenant":…,"locale":…,"cols":[{key,type}…]}'`, per-cell
  `data-tm-v` (JSON raw value) paired with `data-tm-h` (a fingerprint of the cell's display text,
  §9.3), and per-row `data-tm-rowid` (emitted when the range is full rows —
  a row *move* only needs to identify rows the grid already holds, §9.6; full records are never
  serialized onto the clipboard, where they would leak non-column fields into any HTML-preserving
  paste target). Oversize copies (the §9.1 threshold) omit `data-tm-v` (and its `data-tm-h`) as
  well — per-cell JSON would double-serialize millions of cells; `data-tm-grid` and `data-tm-rowid`
  stay, and typed same-session paste still rides the fast path.

Headers are **not** copied by default (Excel parity; pasting anywhere doesn't drag header junk
along); "Copy with headers" in the context menu prepends the column-header row — never the
row-header numbers column — for report-style export. In the HTML flavor that header row is marked
(`<thead>` + a `headers: true` flag in `data-tm-grid`) so a paste back into a Tellma grid skips it
(§9.3).

An in-memory fast path — a small fingerprint-keyed LRU of recent copy descriptors, so copies from
two grids on one page coexist — lets same-session Tellma→Tellma pastes use the raw objects
directly even if a browser strips custom attributes from the HTML flavor; a paste consults it only
when a fingerprint matches the actual clipboard payload.

### 9.3 Paste resolution ladder

On paste the grid picks the richest available source: (1) in-memory fast path → (2) `text/html`
with `data-tm-grid` metadata (typed) → (3) foreign `text/html` table (display strings) → (4)
`text/plain` TSV (quoted-field parse). The `grid` layer reduces HTML payloads to a string matrix +
metadata via `DOMParser` and hands that to the engine (which stays DOM-free, §1). Sheets'
proprietary `data-sheets-value` attributes are ignored (undocumented, unstable).

A cell's `data-tm-v` raw value is trusted only when its `data-tm-h` fingerprint still matches the
cell's display text. A foreign editor (Excel, Sheets) round-trips our `data-tm-v` verbatim while
the user edits the *visible* text, so an unverified raw value would silently overwrite that edit
with the stale typed value; the fingerprint catches this, and the edited text is re-parsed instead.
A faithful round trip hashes identically, so the typed fast path and its precision survive intact.

**Header-row detection.** A pasted payload's leading header row is skipped, detected two ways:
the Tellma metadata flag (§9.2) when present; otherwise — covering the copy-with-headers →
Excel → edit → copy → paste-back round trip, where Excel's re-copy strips foreign attributes — a
content heuristic: if the first pasted row's cells, compared position-wise against the target
columns' display header labels (trimmed, case-insensitive), match in **every non-empty cell across
at least two columns**, the row is treated as headers. Single-column pastes never trigger the
heuristic (a lone value can coincidentally equal a header); only the metadata flag decides there.
An invisible corner-marker character was rejected: it pollutes user-visible cell text in Excel and
dies on retype.

Target shaping (all Excel semantics): single value → fills every cell of the selection; range →
pasted anchored at the active range's top-start corner; if the target selection is an exact
multiple of the source shape, the source **tiles** it. Overflow beyond the last row materializes
new rows through the placeholder machinery (§5.5, requires `newRow`); overflow beyond the last
column is dropped; readonly/disabled cells inside the paste rectangle are skipped in place (values
are not shifted around them). The whole paste — including materialized rows and async resolutions —
is **one undo op**.

Per-cell value conversion, in order: (1) same `type` + same tenant with a raw value present →
typed write, no parsing; (2) else the display string goes through the column's **`parse`** (built
in or consumer — enum's built-in parse matches `optionLabel` output synchronously); (3) a string
that `parse` can't handle (or no `parse`) falls to **`resolvePastedLabels`** when the column has
one — the async pipeline of §9.4; (4) only definitive failures — parse error with no resolver, or
a resolver `notFound`/`ambiguous` — become invalid inputs (§10). So `parse` is the cheap
synchronous path and the resolver is the server-backed fallback; a column may carry both.

### 9.4 Async label→value resolution (entity/enum paste)

Pasting foreign text into an entity column means mapping labels ("Adam Brown") back to ids —
possibly thousands at once, possibly ambiguous, possibly written in a different locale:

1. The grid collects the **unique** labels per column across the whole paste and issues **one**
   `resolvePastedLabels(labels, ctx)` call per column (parallel across columns) — 1,000 pasted
   cells with 40 distinct labels cost one request of 40 labels. `ctx` carries the source locale and
   tenant parsed from clipboard metadata (if any), so the resolver can match against localized
   names and refuse cross-tenant raw ids (raw entity ids are only trusted when the source tenant
   matches; otherwise labels are re-resolved).
2. Affected cells show a pending affordance (a subtle inline `tm-spinner`, the foundation's shared
   spinner glyph) but the grid stays fully interactive; the paste op holds its undo entry open
   until resolution lands.
3. The resolver returns `Map<label, TmLabelResolution<V>>` (§6.3): a value is written through the
   field; `notFound` and `ambiguous` (the resolver must not guess between two "Adam Brown"s) both
   put the cell in invalid-input state with the pasted label as its raw text (§10), each with its
   own localized message — *"No ‹Agent› named ‹label›"* vs. *"‹label› matches more than one
   ‹Agent›"*.
4. The contract is a pure per-column async function, so a distribution implements it with one
   server endpoint (`POST /resolve-labels { collection, labels[] }`) shared by all its grids.
5. **Interleaving guards.** Every pending cell holds a sequence token; any later write to it — a
   manual edit, a second paste, undo — invalidates the token and the late resolver result is
   discarded. Undoing the paste cancels its outstanding resolutions and restores pre-paste state;
   cancellation aborts `ctx.signal` (§6.3) so the consumer can cancel the server round-trip
   (`fetch`'s native contract) — honoring it is an optimization, since discarded-on-arrival is the
   correctness guarantee. Pending cells are not errors: the grid exposes `pendingCount` alongside
   the error count (§10), and consumers gate Save on both reaching zero.

### 9.5 Cut semantics

Cut = Excel's deferred move: the source range gets the marching-ants marquee style; the clipboard
receives a normal copy payload; a subsequent paste **in the same grid** performs the move (clears
source cells, one undo op with the write); Esc — or any edit — cancels the pending move. A paste
into any *other* target (another grid, Excel) behaves as copy and leaves the source untouched,
which matches Excel's cross-application behavior and sidesteps the impossibility of observing an
external paste.

### 9.6 Full-row cut/paste = row move

When the cut range is full rows (`kind: 'rows'`) and the paste lands in the same grid, the
operation is a **row move**: the rows — identified by the fast path or `data-tm-rowid`, and
resolved against the grid's own data, which still holds them — are re-inserted above the paste
target row and removed from their old positions, as one undo op. This is the sanctioned replacement for drag-and-drop row re-ordering. In the tree
grid the move carries each row's whole subtree, re-parents the moved rows to the target row's
parent, and rejects (no-op + announcement) a move of a row into its own descendant.

## 10. Cell error states & the error tally

A cell is *in error* when either:

- **Field-invalid** — its Signal Forms child field reports `invalid()` (consumer validators), or
- **Invalid input** — the grid holds a raw text for it that failed `parse`/resolution (§8.4,
  §9.3–9.4). The raw text **is displayed in the cell**, styled with the error tint — after a
  1,000-cell paste with three rejects, the user must see *what* was rejected in place, not hunt
  the source cell down in Excel. The model field meanwhile holds the column's cleared value
  (§6.1) — never a stale prior value a save could silently persist — and the cell's message spells
  the split out: *"‘Foo' is not a valid ‹Quantity›; the field is empty until corrected."* Copying
  such a cell copies the raw text (the annotation round-trips out). Invalid inputs are grid state
  keyed by `(rowId, columnKey)` and are cleared by: undo, committing a valid value, or Delete
  (which may of course surface a `required` field error next).

Error styling: error-tint background + ring from `--grid-*` tokens; the active errored cell sets
`aria-invalid` and `aria-describedby` → a message element rendered for the active cell only (the
field's localized error text, or the invalid-input library string above). The message renders in a
top-layer overlay anchored to the cell — the same overlay infrastructure the editors' panels use —
so errors appearing or clearing **never shift the grid's or the page's layout**; the status bar is
fixed-height for the same reason.

**The tally**: a status-bar chip (the status bar renders in editable mode only) showing the total
error count — `field().errorSummary()` (the framework's aggregated descendant errors, deduplicated
to distinct cells) plus the invalid-input map size — flanked by **previous/next arrow buttons**
that jump — scroll and activate — through the errored cells in row-major order, cycling in either
direction (clicking the chip itself = next). While §9.4 resolutions are in flight the chip shows a
`tm-spinner` with the pending count. The chip is a live-region-announced summary, and the
`errorCount`/`pendingCount` signals are exposed on the component API so consumers can gate their
Save buttons.

## 11. Undo / redo

- Engine-level command stack of **committed, data-mutating operations**: cell writes (single edits,
  range clears, pastes with their materialized rows and async resolutions), row
  inserts/deletes/moves. Each entry stores inverse data keyed by `rowId` (values before/after,
  invalid-input raw texts before/after, row snapshots + positions for structural ops).
- In-editor typing has the input's native undo; the grid stack only sees the commit.
- Mod+Z / Mod+Y / Mod+Shift+Z when the grid has focus and no editor is open. Depth capped at 100
  ops.
- Undo/redo restores data, invalid-input states, row existence/order — and re-selects + scrolls to
  the affected range (Excel behavior), expanding collapsed ancestors first in the tree grid, as
  find navigation does (§8.7). It does **not** manage view state (column widths, expansion,
  scroll) — those are not data operations.
- Programmatic edits join the same stack via the public transaction API:
  `grid.applyTransaction(edits: TmCellEdit[], opts?: { label?: string })`, where
  `TmCellEdit = { rowId: string | number; key: string; value: unknown }` (the grid captures the
  prior values for the inverse) — the documented channel for consumer features like auto-fill, so
  external edits are user-undoable.
- View-state changes (expand/collapse) do not clear the stack; changing `contentKey` (§12) resets
  it.

## 12. State memory (`TmGridStateStore`)

Grids get destroyed and recreated (tab panels, route reuse); users expect different pieces of state
to have different lifetimes. One root-provided store keeps them, keyed by two consumer inputs:
`gridId` (stable identity of the grid *definition*, required) and `contentKey` (identity of the
*content*, e.g. the invoice id; optional).

| State | Key | Lifetime / reset |
|---|---|---|
| Column widths | `gridId` | Survives navigation and `contentKey` changes (user expectation 2) |
| Scroll x/y | `gridId + contentKey` | Restored on remount for the same content; a different `contentKey` starts at 0,0 (expectation 1) |
| Active cell + selection | `gridId + contentKey` | Same as scroll (restores the working position on tab-return) |
| Undo/redo stack | `gridId + contentKey` | Survives remounts while editing the same record; cleared on `contentKey` change or explicit `grid.clearHistory()` — the consumer calls it on save/cancel (expectation 3) |
| Tree expansion set | `gridId + contentKey` | Same as scroll |

The store is in-memory (app session) and **LRU-bounded** so long sessions can't grow it without
limit: per-content slices (scroll, selection, undo, expansion) keep the most recently used
`gridId + contentKey` pairs (default 50), width slices the most recently used `gridId`s (default
200); an evicted slice simply means defaults on next mount. Widths are additionally exposed as
`store.serializeWidths(gridId)` / `store.restoreWidths(gridId, blob)` so a distribution can persist
them to user settings; the library itself never touches storage.

Registering a second **live** grid under an already-live `gridId` throws in dev mode (prod: a
console warning; the instances share the width slice and last-writer-wins on per-content slices).
Restores clamp to the current content: scroll clamps to the content extent; the active cell
restores by `rowId`, else by clamped view index, else 0,0; selection restores only if every range
endpoint's `rowId` resolves, else it is dropped. A `contentKey` change while mounted snapshots the
outgoing key's slices, cancels any open editor, and loads the incoming key's slices (or defaults).

## 13. Tree grid (`tm-tree-grid`)

### 13.1 Hierarchy model

The rows stay a **flat array** (the ERP's natural shape) plus one accessor:
`parentId: (row: T) => string | number | null`. The grid derives the hierarchy: roots are rows with
`null`/missing parents; a row whose parent id doesn't resolve is treated as a root (dev-mode warn);
a parent cycle is broken by treating the offending row as a root (dev-mode warn). Nested-children
inputs were rejected: adjacency lists match Tellma models and SQL, and paste/undo/Signal-Forms all
operate on one flat array.

### 13.2 Rendering & navigation

The engine flattens the hierarchy to the **visible-row sequence** (depth-first, respecting the
expansion set) and everything from §4–§12 operates on that sequence unchanged (virtualization,
selection, clipboard — a copied range exports visible rows in visible order, which is also what
Excel gets). The hierarchy renders in the first column by default (or the column marked
`hierarchy`): indentation = `level × --grid-indent` (logical padding, mirrors in RTL) + an
expander button (`tabindex="-1"`, pointer-only — keyboard uses Alt+Arrows) + the cell content.

### 13.3 Expand / collapse & lazy loading

- Expansion state lives in the engine + state store (§12); default expanded depth via input.
- Expand: expander click or Alt+ArrowRight; collapse: Alt+ArrowLeft (collapsing an ancestor of the
  active cell moves activation to that ancestor).
- **Lazy children:** `hasChildren?: (row: T) => boolean` marks expandable rows whose children may
  not be loaded; on expand, the grid calls `loadChildren: (row: T) => Promise<void>` and shows a
  `tm-spinner` in space reserved beside the expander — the expander stays visible and interactive
  (the user may re-collapse while loading; the load continues and the children simply render on
  next expand), and neither the spinner's appearance nor its removal shifts layout. The
  **consumer** appends the fetched rows to its own array/field (the grid never writes rows it
  didn't create); when the promise resolves the node expands over whatever children now exist.
  Rejection restores the collapsed state and announces the failure string.

### 13.4 Adding rows in a tree

Per-node placeholder rows were rejected (one ghost row under every expanded node is noise, and no
spreadsheet user expects it). Instead:

- The single root-level placeholder row (§5.5) appears at the bottom, exactly as in `tm-grid`
  (`newRow()` called with no parent).
- **Insert child row** in the context menu (and Insert sibling above/below from the shared items):
  materializes `newRow(parentRow)` — the factory sets the parent id — inserts it as the last child,
  expands the parent if needed, and activates the new row's first editable cell.

### 13.5 Tree paste specifics

Cell-rectangle pastes map onto visible rows exactly like the flat grid (indentation is not encoded
in cell text either direction). Row-overflow materialization creates **siblings of the last target
row** (same parent) — continuing the list being pasted into. Full-row moves follow §9.6 (subtree
moves, descendant-target rejection).

## 14. Accessibility

Target WCAG 2.1 AA — axe-core static floor + behavioral Playwright specs, per the spec-0002
posture (Playwright is the standardized runner; screen-reader verification beyond the ARIA
mechanism is a manual pass outside the DoD).

- **Roles**: container `role="grid"` / `role="treegrid"` with `aria-multiselectable="true"`,
  `aria-rowcount`/`aria-colcount` (full model counts — the DOM only holds the window);
  rows `role="row"` + `aria-rowindex` (1-based, counting the header row); cells
  `role="gridcell"`/`columnheader`/`rowheader` + `aria-colindex`, `aria-selected` on cells of
  selected ranges (checkbox-checked rows carry row-level `aria-selected`, §8.8); tree rows add
  `aria-level`, `aria-expanded`, `aria-posinset`, `aria-setsize`.
- **Focus**: roving `tabindex` with real DOM focus on the active cell element (broadest AT support;
  the always-rendered active row, §4, is what makes roving safe under virtualization). The grid
  host is the single tab stop when no cell is active. The editor receives real focus while editing;
  focus returns to the cell on commit/cancel. A Playwright spec pins focus retention across
  scroll-driven recycling.
- **Announcements** (CDK `LiveAnnouncer`, localized): selection changes ("R × C selected"), paste
  results ("N cells pasted, M errors"), undo/redo descriptions, row insert/delete/move, async
  resolution completion, oversize-copy completion and copy failures (§9.1), lazy-load failures,
  the error-tally jump target.
- Editors get `aria-label` from the column header text; the active errored cell wires
  `aria-invalid` + `aria-describedby` (§10).
- Keyboard-trap audit: Tab always has an exit (§8.2); every action is keyboard-reachable (context
  menu via Menu/Shift+F10; expander via Alt+Arrows; resize has no keyboard path — column widths are
  a pointer nicety with no data consequence, noted as a known limitation).
- Focus ring from `--focus-ring`; forced-colors keeps selection/active/error distinguishable.

## 15. RTL & i18n

- Direction from CDK `Directionality`; all geometry via logical properties (indentation,
  alignment — except the deliberate physical `left`/`right` alignment values of §6.1 — and the
  find bar's inline-end anchoring). Column template order is logical: first column renders at
  inline-start.
- Arrow keys are direction-mapped in the engine (physical ArrowRight = inline-end in LTR,
  inline-start in RTL — same mapping the aria grid uses); Tab/Home/End/Enter-run semantics are
  logical by construction.
- All built-in strings (context-menu items, announcements, error-tally text, "couldn't interpret"
  message) resolve through `TM_UI_TRANSLATE`; English ships in-package, Arabic lands in
  `@tellma/locale-ar` — extending the reference pack is part of this phase's DoD.
- Number formatting/parsing per active locale (§6.1). Copy exports display strings, so exports are
  locale-faithful by construction; paste parses with the *target* grid's locale plus the source
  locale hint (§9.4).

## 16. Performance budget

Measured in CI (Playwright traces on a mid-tier mobile CPU throttle profile), not aspirational:

- **Rendering**: DOM = header + window(±4) + status bar regardless of row count; 100k × 30 grid
  mounts without measurable proportional cost. Scroll produces no long task > 50ms; keydown→paint
  for navigation ≤ 16ms at the window sizes above.
- **Interaction costs**: selection ops are O(ranges); column resize is one custom-property write;
  paste of 10k cells (excluding server resolution) < 1s with one undo entry; copy serialization
  ≤ 150ms up to ~100k cells on the sync path, and a whole-grid select-all (3M formatted cells) via
  the §9.1 escalation completes ≤ 2s with no frame blocked longer than 50ms; the find scan (§8.7)
  is debounced and chunked — a full 100k × 30 scan completes with no long task > 50ms.
- **Signal Forms cost model** (verified against `@angular/forms@22.0.5`: field nodes materialize
  lazily, one level per first access): the `data` binding creates **no** field nodes; the `field`
  binding materializes the array's row nodes on first index access and each row's *column* nodes
  only when that row is touched — so the grid's display path forces only the rendered window plus
  edited/pasted cells. Reading the error tally (`errorSummary()`) does force validated rows'
  nodes — the grid defers that first read to post-mount idle and chunks the warm-up, so the spike
  never blocks a frame; after it, the signal graph keeps the summary incremental. Guidance, stated
  in the docs: the editable binding targets entry-scale data (hundreds to low thousands of rows —
  ERP document lines); the 100k-row scale is the readonly `data` path.
- **Bundle ceilings** — declared per entry point in the package's `"tellma".budgetsInKb` (the
  foundation's mechanism) and calibrated to its scale (`tm-select`, carrying the overlay/listbox
  wiring, ships under 8 KB): `grid-engine` ≤ 12 KB, `grid` ≤ 24 KB, `tree-grid` ≤ 8 KB
  incremental, `menu` ≤ 6 KB — gzipped self-weight ratchets, inspected and tightened once real
  builds land.
- Memory: undo capped (§11); invalid-input map is sparse; no per-cell listeners (all pointer/key
  handling is delegated on the grid host).

## 17. Testing

- **Engine (vitest, no DOM)** — the bulk of the matrix: navigation motions incl. data-edge jumps
  and Enter-run origin; selection algebra (extend, multi-range incl. compaction/refusal, row/col
  kinds, remap on insert/delete); TSV/HTML serialize + quoted parse round-trips + header-row
  detection (metadata flag and content heuristic); paste shaping (fill, tile, overflow, readonly
  skip, fill-down); cut/move; undo/redo inverses incl. structural ops; tree flatten, expansion,
  cycle/orphan handling; invalid-input bookkeeping.
- **Component (TestBed + harnesses)**: `TmGridHarness` (get cell text/state, select, type, open
  editor, invoke menu), field binding + validation display, placeholder materialization, editor
  contracts against `tmInput`/`tm-checkbox`/`tm-select` (the §6.3 host registration),
  mode-transition semantics (§5.1), external-data reconciliation (§5.3), empty/loading states
  (§5.4), resolver race guards + `ctx.signal` abort, tally navigation, row checkbox selection
  (§8.8: `selectedIds`, tri-state header, range-check, exclusion from ranges/copy/find),
  state-store lifetimes incl. the duplicate-`gridId` guard and restore clamping.
- **Playwright (against the showcase app's story pages)**: full keyboard matrix incl. mode-dependent
  arrows; IME composition (CJK fixture); drag selection with auto-scroll; real-clipboard
  round-trips (Chromium with granted permissions; Firefox/WebKit via synthetic `ClipboardEvent`
  dispatch for parse/serialize paths); Excel/Sheets fixture payloads (recorded real clipboard
  HTML/TSV from both apps, kept as fixtures); focus retention across virtualization; editor
  scroll-back on keystroke; error-overlay layout stability; Enter-activates-link; row-checkbox
  flows; find-bar behavior; axe on populated/editing/error/loading/empty/selectable states; RTL
  mirror specs; forced-colors/reduced-motion; touch long-press menu and selection handles.
- Clipboard fixtures pin the **format contract**: TSV quoting, HTML table shape, `data-tm-*`
  round-trip, cross-tenant guard.
- API goldens + the `api:approve` gate cover the new entry points; co-located `*.examples.ts` feed
  the docs pipeline, and `components.json`/`llms.txt`/MCP pick up the new components and the column
  directives.

## 18. Definition of done

1. `tm-grid` renders 100k × 30 within the §16 budgets; all cells static DOM; exactly one live
   editor; active row survives scroll-out (focus + edit state retained, Playwright-pinned).
2. Full keyboard matrix (§8.2) green, including RTL arrow mapping, Enter-run origin return, both
   edit modes with mode-dependent arrow behavior, IME composition opening the editor unseeded
   (§8.4, CJK spec), and the exit paths (end-of-grid Tab; Esc-to-container then Tab).
3. Mouse/touch matrix (§8.3, §8.6) green, including drag-select with auto-scroll, header
   selections, interactive-header non-selection, resize (fixed and proportional columns),
   long-press menu, and touch selection handles extending a range while pan still scrolls.
4. Editable binding: commits write through `FieldTree`, `applyEach` validators surface as cell
   errors, field `disabled`/`readonly` override column editability, `readonly` toggle flips the
   same instance between modes with the §5.1 transition semantics (an open editor cancels — never
   commits — and grid state survives the round trip), placeholder row materializes via `newRow`
   (typing, editor commit, and paste overflow) and re-appears beneath.
5. Clipboard: copy/cut produce spec-shape TSV + HTML (fixture-pinned); paste ladder honors the
   §9.3 order; Excel and Sheets fixture payloads paste correctly; fill/tile/overflow/readonly-skip
   semantics and Mod+D fill-down hold; aligned multi-range copy compacts, misaligned is refused
   with an announcement (§8.1); cut is a deferred move with Esc cancel; full-row cut/paste moves
   records with identity; "Copy with headers" round-trips — the header row is skipped on
   paste-back via the metadata flag *and*, through an Excel round trip, via the §9.3 content
   heuristic; context-menu paste degrades to the shortcut hint where reads are blocked; a
   rejected oversize-copy write is announced and visibly surfaced, never silent.
6. Entity-paste pipeline: one batched `resolvePastedLabels` call per column per paste (deduped
   labels), pending affordance, `notFound`/`ambiguous` → invalid-input state with distinct
   messages, cross-tenant raw ids refused, whole paste = one undo op; the §9.4 interleaving guards
   hold under undo/edit races; `pendingCount` exposed.
7. Error machinery: invalid inputs display raw text (error tint) with the model cleared to the
   column's cleared value (and copy exports the raw text); field + invalid-input errors aggregate
   into the tally; the tally arrows navigate errored cells in both directions; error message
   overlays and status-bar changes cause no layout shift; clearing rules (undo / valid commit /
   Delete) hold; counts exposed on the API.
8. Undo/redo: all §11 op kinds invert correctly (values, structure, invalid-input state,
   selection+scroll restore); 100-op cap; `applyTransaction` registers; in-editor undo stays
   native.
9. `tm-tree-grid`: adjacency-list hierarchy with orphan/cycle tolerance; flatten + expansion +
   virtualization compose; Alt+Arrow expand/collapse; lazy `loadChildren` spinner (no layout
   shift, expander stays interactive) and failure paths; insert-child/sibling menu items; subtree
   row moves with descendant-target rejection; tree paste per §13.5; `role="treegrid"` semantics
   complete.
10. State memory: each §12 row observes its specified lifetime across destroy/recreate;
    `clearHistory()` and width serialization work; duplicate live `gridId` throws in dev mode;
    restores clamp per §12 when content shrank or ids vanished.
11. A11y: axe clean on readonly/editable/editing/error/tree states; APG semantics verified
    (roles, counts/indices, `aria-selected`, tree attributes); announcements fire (localized);
    forced-colors + reduced-motion Playwright-gated.
12. RTL: mirrored rendering (indent, find-bar corner, logical alignment — with `number` columns
    staying right-aligned), direction-mapped arrows, RTL clipboard round-trip — all
    Playwright-verified under `dir="rtl"`.
13. i18n: every built-in string resolves through `TM_UI_TRANSLATE`; `@tellma/locale-ar` extended
    with the grid strings; live locale switch re-renders visible grid strings.
14. Contracts v2 (§6.3) shipped in `@tellma/core-ui/contracts`; the three existing controls
    register with `TM_CELL_EDITOR_HOST` when present; a custom consumer editor story page proves
    the registration path; `grid-engine` passes the no-DOM/no-DI boundary lint.
15. Find in grid (§8.7): match scan over display text (chunked, no long tasks), highlight +
    counter + Enter/Shift+Enter/button navigation activating matches, Mod+F focus, Esc restore,
    tree deep-search with ancestor auto-expand, announcements — readonly and editable.
16. `tm-menu`: aria-menu keyboard model (arrows/Home/End/typeahead/Esc), icons, pointer-position
    context trigger incl. long-press, `Directionality` mirroring, axe clean — verified standalone
    (it is a public component, not grid chrome).
17. Budgets (`"tellma".budgetsInKb`), API goldens (`api:approve`), `*.examples.ts` →
    `components.json`/MCP/`llms.txt`, showcase story pages, and worktree port-isolation all green
    per the spec-0002 pipeline.
18. External data changes and states: in-place refreshes reconcile selection/active-cell/editor/
    invalid-inputs/undo per §5.3; the `loading` overlay sets `aria-busy` with headers preserved;
    the readonly empty state renders with `*tmGridEmpty`/`*tmGridLoading` overrides; transitions
    announced. Paste-resolution cancellation aborts `ctx.signal` (§9.4).
19. Row checkbox selection (§8.8): `selectedIds` two-way model; tri-state header; Shift+click
    range-check; Space toggle and Ctrl+Shift+Space select-all toggle; `selectable` on an editable
    grid fails with a dev-mode error; the checkbox column excluded from ranges, copy, find, and
    arrow navigation; row `aria-selected` + count announcements; Enter activates a readonly cell's
    interactive content (§8.2).

## Decisions record

Answers to the questions the design brief left open, where not already evident above:

1. **Component count** — two components, mode flag for editable (see Context).
2. **Column type vs. template** — both: `type` selects built-in format/parse/editor; templates
   override display/editor per column (§6).
3. **Tree relationship input** — flat rows + `parentId` accessor (§13.1).
4. **Add-row-under-parent UX** — context menu + `newRow(parent)` factory; single root placeholder;
   no per-node ghost rows (§13.4).
5. **Grid memory** — `TmGridStateStore` keyed by `gridId`/`contentKey` with per-slice lifetimes
   (§12).
6. **Enter commits downward** (with Tab-run origin return), not rightward — Excel/Sheets parity
   serves the line-entry flow through the Tab path.
7. **Enter on a readonly cell moves down** rather than doing nothing — no dead keys.
8. **Copy excludes headers by default**; "Copy with headers" is the explicit affordance.
9. **Paste-error cells clear the model field to the column's cleared value** while displaying the
   raw text in the error treatment — the rejected text stays visible in place, and the display
   and a persisted save can never disagree.
10. **Readonly grids are a single tab stop** (APG); editable grids rove Tab through editable cells
    with guaranteed exits.
11. **`@angular/aria`'s grid is a blueprint, not a dependency** (§2); revisit at the engine seam if
    it gains virtualization-aware range selection.
12. **Find in grid is in scope** (§8.7) — the display-text index the clipboard already needs makes
    it cheap, and ERP users need it constantly.
13. **The pivot table is a separate future component** with its own engine; only the clipboard
    serializer and the windowing helper are written shape-neutral for it (Non-goals).
14. **Row headers are not sticky** — numbers-only content doesn't earn the horizontal space it
    would pin on phones.
15. **The context menu is the reusable `tm-menu`** (`@tellma/core-ui/menu`), not grid-private
    chrome — the PrimeNG/CDK precedent (§8.5).
16. **Touch range selection uses corner drag handles** (the Sheets/Excel-mobile pattern), keeping
    finger pan as scroll (§8.6).
17. **Tab while editing commits and moves the selection without opening the next editor** —
    strict Excel; type-to-edit makes the follow-on edit a single keystroke.
18. **List screens: consumer links activate, a grid-provided checkbox column selects** (§8.8) —
    links keep native href affordances and leave clicks free for range-copy; checkboxes are
    sticky (no lost-selection-on-stray-click) and never entangle with the cell coordinate space.
    A row-selection mode (`selectionUnit: 'row'`) was rejected as redundant with it.
