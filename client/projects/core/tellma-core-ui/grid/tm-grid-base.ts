// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  booleanAttribute,
  contentChild,
  contentChildren,
  DestroyRef,
  Directive,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  isDevMode,
  model,
  output,
  signal,
  type Signal,
} from '@angular/core';
import { LiveAnnouncer } from '@angular/cdk/a11y';
import { Directionality } from '@angular/cdk/bidi';
import type { FieldTree } from '@angular/forms/signals';

import type { TmCellEdit, TmRowId } from '@tellma/core-ui/contracts';
import {
  TM_ACTIVE_LOCALE,
  TM_CALENDAR,
  TM_FORM_FIELD_DEFAULTS,
  TM_UI_TRANSLATE,
  type TmControlSize,
} from '@tellma/core-ui';
import type { TmMenuItem } from '@tellma/core-ui/menu';

import { ɵTmGridCore, type ɵTmGridTreeConfig } from './internal/grid-core';
import { TmGridColumn } from './tm-grid-column';
import { TM_GRID_CONTEXT } from './tm-grid-context';
import type { TmGridDriverTarget } from './tm-grid-driver';
import {
  TmGridEmptyDef,
  TmGridLoadingDef,
  TmGridRowEndDef,
  TmGridRowEndHeaderDef,
} from './tm-grid-templates';
import { TmGridStateStore } from './tm-grid-state-store';
import type { TmGridRowActivation, TmGridSort } from './tm-grid-types';

/**
 * The shared shell of `tm-grid` and `tm-tree-grid`: declares the inputs,
 * content queries, and public members both components carry, and
 * constructs the grid's internal composition root from the host's
 * injected dependencies. Selector-less by design — each grid extends it
 * and renders the shared view. It is exported because its members are the
 * two grids' public API; it is not meant to be extended outside this
 * library.
 *
 * @sealed
 */
@Directive()
export abstract class TmGridBase<T> implements TmGridDriverTarget {
  private readonly sizeDefaults = inject(TM_FORM_FIELD_DEFAULTS);

  /**
   * Stable identity of this grid definition — the key column widths (and,
   * with `contentKey`, scroll/selection/undo state) are remembered under.
   * Two grids that are live at the same time must not share a `gridId`.
   */
  readonly gridId = input.required<string>();
  /**
   * Identity of the bound content (an invoice id, …). Scroll, selection,
   * and undo state are remembered per `gridId` + `contentKey`; a different
   * key starts fresh at the origin.
   */
  readonly contentKey = input<string | number | undefined>(undefined);
  /** The readonly rows binding. Exactly one of `data`/`field` must be bound. */
  readonly data = input<readonly T[] | undefined>(undefined);
  /**
   * The editable binding: a Signal Forms field tree over the rows array.
   * Rows come from its value; the grid is editable while `readonly` is off.
   */
  readonly field = input<FieldTree<T[]> | undefined>(undefined);
  /**
   * Reads a row's stable identity. Required: selection stability, undo,
   * view reuse, and state memory all key on it, never on the index.
   */
  readonly rowId = input.required<(row: T) => TmRowId>();
  /** With `field` bound, toggles view/edit of the same screen. */
  readonly readonly = input(false, { transform: booleanAttribute });
  /**
   * The new-row factory. Binding it enables the new-row placeholder and
   * paste-overflow row creation; new rows must carry client-side ids.
   */
  readonly newRow = input<((parent?: T) => T) | undefined>(undefined);
  /** Shows the loading overlay (headers stay rendered) and sets `aria-busy`. */
  readonly loading = input(false, { transform: booleanAttribute });
  /**
   * Enables the find bar: Mod+F while the grid has focus opens a floating
   * bar that searches every cell's text representation across the whole
   * model (collapsed tree rows included) and cycles through the matches.
   */
  readonly searchable = input(false, { transform: booleanAttribute });
  /**
   * Enables the row-checkbox column for bulk selection (list screens).
   * Readonly grids only — enabling it on an editable grid is a dev-mode
   * error. Checked rows live in the two-way `selectedIds` model.
   */
  readonly selectable = input(false, { transform: booleanAttribute });
  /** Extra context-menu items appended after the built-ins. */
  readonly extraMenuItems = input<readonly TmMenuItem[]>([]);
  /**
   * Row density. Defaults to the workspace default from
   * `TM_FORM_FIELD_DEFAULTS`, so a grid and the fields on the same screen
   * agree about how dense the app is.
   */
  readonly size = input<TmControlSize>(this.sizeDefaults.size);
  /**
   * `'row'` makes a readonly list open records: a click anywhere in a row,
   * Enter on it, a middle click or a touch tap emits `rowActivate` (Mod or
   * the middle button asking for a new tab), while drags, Shift+click and
   * links in cells keep their meanings. F2 reaches a cell's link or
   * button. Readonly grids only — `'row'` on an editable grid is a
   * dev-mode error. `'none'` keeps every other behavior unchanged.
   */
  readonly rowActivation = input<'none' | 'row'>('none');
  /**
   * Classes for a rendered row's element, from a pure function of the row
   * (it runs for the rendered window only). A class styles the row through
   * the custom properties its cells read — `--tm-grid-row-bg`,
   * `--tm-grid-row-ink` and `--tm-grid-row-weight` — and the shipped
   * `tm-grid__row--muted` and `tm-grid__row--emphasis` modifiers cover a
   * de-emphasized row and a totals row. Selection, checked and active
   * states paint over them.
   */
  readonly rowClass = input<((row: T) => string | readonly string[] | null) | undefined>(undefined);
  /**
   * Whether the row-number column (and the corner above it) renders. A
   * list without row numbers keeps the selections they offered by pointer
   * through Mod+A and Shift+Space.
   */
  readonly rowHeaders = input(true, { transform: booleanAttribute });

  /**
   * The checked row ids of a `selectable` grid. Fully independent of
   * cell-range selection: bulk actions read from it, ranges drive copy.
   * Every change lands as a FRESH `ReadonlySet` instance. Ids of rows no
   * longer bound (another page of results) stay checked: the select-all
   * checkbox adds or removes only the bound rows' ids.
   */
  readonly selectedIds = model<ReadonlySet<string | number>>(new Set());
  /**
   * The active cell's row, two-way. The grid writes every change of the
   * active row (a click, an arrow, a data change); a consumer write moves
   * the active cell to that row — in the current column, else the first —
   * and scrolls it into view without moving focus. An id the grid does not
   * hold is ignored, and the model is written back with the actual row
   * (`null` with none).
   */
  readonly activeRowId = model<TmRowId | null>(null);
  /**
   * The sorted column, two-way, set by clicks on `sortable` column
   * headers. The grid shows the sort and never reorders the rows: the
   * consumer sorts the data it binds.
   */
  readonly orderBy = model<TmGridSort | null>(null);

  /** Emits when the user opens a row of a `rowActivation="row"` grid, or `activateRow` is called. */
  readonly rowActivate = output<TmGridRowActivation<T>>();

  /** The projected column definitions, in display order. */
  readonly columns = contentChildren(TmGridColumn);
  /** The projected empty-state template, if any. */
  readonly emptyDef = contentChild(TmGridEmptyDef);
  /** The projected loading-state template, if any. */
  readonly loadingDef = contentChild(TmGridLoadingDef);
  /** The projected row-end column header template, if any. */
  readonly rowEndHeaderDef = contentChild(TmGridRowEndHeaderDef);
  /** The projected row-end cell template, if any. */
  readonly rowEndDef = contentChild(TmGridRowEndDef);

  /** The composition root the view renders from. */
  protected readonly core: ɵTmGridCore<T>;

  /**
   * Count of distinct cells in error state: held invalid inputs plus the
   * bound field's validation-errored cells. Consumers gate Save buttons on
   * it together with {@link TmGridBase.pendingCount}: a cell still awaiting
   * resolution counts in neither error nor value yet, so a Save must also
   * wait for `pendingCount` to reach 0.
   */
  readonly errorCount: Signal<number>;
  /**
   * Count of cells awaiting an asynchronous resolution: pasted labels, typed
   * entity commits waiting on the column's search or resolver, and entity
   * text typed while the grid's editing code is still loading. Until such a
   * cell settles, its model value is not yet written, so a Save must wait
   * for this count to reach 0 as well as for
   * {@link TmGridBase.errorCount} to be 0.
   */
  readonly pendingCount: Signal<number>;
  /** The scroll container's element id — what a driver's `aria-controls` names. */
  readonly controlsId: Signal<string>;
  /**
   * The active row element's id — what a driver's `aria-activedescendant`
   * names; `null` with no active row.
   */
  readonly activeDescendantId: Signal<string | null>;
  /** The visible row count: how far PageDown and PageUp move. */
  readonly pageStep: Signal<number>;

  constructor() {
    const elementRef = inject(ElementRef) as ElementRef<HTMLElement>;
    const injector = inject(Injector);
    const destroyRef = inject(DestroyRef);
    const directionality = inject(Directionality);
    const announcer = inject(LiveAnnouncer);
    const translate = inject(TM_UI_TRANSLATE);
    const store = inject(TmGridStateStore);
    const locale = inject(TM_ACTIVE_LOCALE);
    const calendar = inject(TM_CALENDAR);
    // Tenant identity is ambient (single tenant per app), not a per-grid input.
    const gridContext = inject(TM_GRID_CONTEXT);

    const direction = signal<'ltr' | 'rtl'>(directionality.value === 'rtl' ? 'rtl' : 'ltr');
    const directionSubscription = directionality.change.subscribe((value) =>
      direction.set(value === 'rtl' ? 'rtl' : 'ltr'),
    );
    destroyRef.onDestroy(() => directionSubscription.unsubscribe());

    this.core = new ɵTmGridCore<T>({
      host: elementRef.nativeElement,
      injector,
      destroyRef,
      direction: direction.asReadonly(),
      announcer,
      translate,
      store,
      locale,
      calendar,
      gridId: this.gridId,
      contentKey: this.contentKey,
      data: this.data,
      field: this.field,
      rowId: this.rowId,
      readonlyInput: this.readonly,
      tenantId: gridContext.tenantId,
      distributionKey: gridContext.distributionKey,
      newRow: this.newRow,
      loading: this.loading,
      searchable: this.searchable,
      selectable: this.selectable,
      selectedIds: this.selectedIds,
      rowActivation: this.rowActivation,
      rowClass: this.rowClass,
      rowHeaders: this.rowHeaders,
      activeRowId: this.activeRowId,
      orderBy: this.orderBy,
      rowActivate: (activation) => this.rowActivate.emit(activation),
      size: this.size,
      extraMenuItems: this.extraMenuItems,
      // The query token is the generic directive class, so the query signal
      // erases T; the rows these directives read are this grid's rows.
      columns: this.columns as Signal<ReadonlyArray<TmGridColumn<T, unknown>>>,
      emptyDef: this.emptyDef,
      loadingDef: this.loadingDef,
      rowEndHeaderDef: this.rowEndHeaderDef,
      rowEndDef: this.rowEndDef,
      tree: this.treeConfig(),
    });
    this.errorCount = this.core.errorCount;
    this.pendingCount = this.core.pendingCount;
    this.controlsId = signal(this.core.scrollerId).asReadonly();
    this.activeDescendantId = this.core.activeDescendantId;
    this.pageStep = this.core.pageSize;

    if (isDevMode()) {
      // Exactly one of data/field must be bound — a configuration error,
      // surfaced loudly in dev mode.
      effect(() => {
        const hasData = this.data() !== undefined;
        const hasField = this.field() !== undefined;
        if (hasData === hasField) {
          throw new Error(
            hasData
              ? 'tm-grid: [data] and [field] are both bound — bind exactly one.'
              : 'tm-grid: neither [data] nor [field] is bound — bind exactly one.',
          );
        }
      });
      // Row checkbox selection is a readonly-grid affordance: bulk
      // selection belongs to list screens, never to editable line grids.
      effect(() => {
        if (this.selectable() && this.field() !== undefined && !this.readonly()) {
          throw new Error(
            'tm-grid: [selectable] requires a readonly grid — bind [data], ' +
              'or keep [readonly] on while the [field] binding is selectable.',
          );
        }
      });
      // Row activation opens records from a LIST; an editable grid's click
      // and Enter belong to editing.
      effect(() => {
        if (this.rowActivation() === 'row' && this.field() !== undefined && !this.readonly()) {
          throw new Error('tm-grid: rowActivation="row" requires a readonly grid.');
        }
      });
      // A sort names its column by key, so a sortable column needs one.
      effect(() => {
        const columns = this.columns();
        const unkeyed = columns.findIndex((column) => column.sortable() && !column.key());
        if (unkeyed >= 0) {
          // A template-drawn header has no text: name the column by position.
          const name = columns[unkeyed].header() || `#${unkeyed + 1}`;
          throw new Error(`tm-grid: the sortable column "${name}" has no key.`);
        }
      });
    }
  }

  /**
   * The tree bindings handed to the core, or `undefined` for the flat
   * grid. The tree-grid subclass overrides this. Called once during the
   * BASE constructor — before subclass fields initialize — so an override
   * must return deferred closures over its inputs, never input values.
   */
  protected treeConfig(): ɵTmGridTreeConfig<T> | undefined {
    return undefined;
  }

  /**
   * Registers consumer edits as ONE user-undoable operation (programmatic
   * recalculations the user should be able to Ctrl+Z). No-ops in readonly
   * mode.
   */
  applyTransaction(edits: readonly TmCellEdit[], opts?: { label?: string }): void {
    this.core.engine.applyTransaction(edits, opts);
  }

  /**
   * Drops the undo/redo history — the consumer calls it at save/cancel
   * moments and on wholesale reloads.
   */
  clearHistory(): void {
    this.core.clearHistory();
  }

  /** Focuses the grid: the active cell when one exists, else the container. */
  focus(): void {
    this.core.focus();
  }

  /**
   * Moves the active row by a count, or to the first or last row: clamped,
   * never wrapping, keeping the active column. With no active row, a move
   * starts at the first row. Scrolls the row into view without moving
   * focus.
   */
  moveActiveRow(by: number | 'first' | 'last'): void {
    this.core.moveActiveRow(by);
  }

  /**
   * Opens a row — the active one when no id is given — emitting
   * `rowActivate` as a keyboard activation. Returns `false` when the grid
   * holds no such row. A second activation of the same row within 400 ms is
   * ignored (still returning `true`), so a quick double activation opens
   * once; a held Enter in the grid or a driver opens once however long it
   * repeats.
   */
  activateRow(rowId?: TmRowId, options?: { newTab?: boolean }): boolean {
    return this.core.activateRow(rowId, 'keyboard', options?.newTab === true);
  }

  /**
   * Brings a row into view. A tree grid expands the row's ancestors first
   * (remembered like a user expand; with `expansionSource="model"` they
   * join `expandedIds`, which emits). Returns `false` when the row is not
   * in the data — a lazy row whose parent has not loaded it yet included.
   *
   * Never moves focus into the grid. By default the active cell moves to
   * the row (`activate: false` leaves it), in the column with key `column`
   * or else the current one; an unknown key keeps the current column and
   * warns in dev mode. `align` places the row in the middle of the view
   * (`'center'`, the default) or just inside its nearest edge
   * (`'nearest'`). `flash` pulses the row's cells once with
   * `--grid-flash-bg` for `--grid-flash-duration`: a still tint for the same
   * time under reduced motion, an outline under forced colors.
   */
  scrollToRow(
    rowId: TmRowId,
    options?: {
      flash?: boolean;
      activate?: boolean;
      align?: 'nearest' | 'center';
      column?: string;
    },
  ): boolean {
    return this.core.scrollToRow(rowId, options ?? {});
  }
}
