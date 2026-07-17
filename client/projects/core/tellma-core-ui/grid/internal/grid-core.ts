// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterRenderEffect,
  computed,
  effect,
  isDevMode,
  signal,
  untracked,
  type DestroyRef,
  type Injector,
  type Signal,
  type TemplateRef,
} from '@angular/core';
import type { LiveAnnouncer } from '@angular/cdk/a11y';
import type { FieldTree } from '@angular/forms/signals';

import {
  TM_PARSE_ERROR,
  type TmGridContentState,
  type TmGridScrollPosition,
  type TmParseContext,
  type TmParseError,
  type TmRowId,
} from '@tellma/core-ui/contracts';
import type { TmUiTranslateFn } from '@tellma/core-ui';
import { TM_CHECKBOX_CELL_DISPLAY } from '@tellma/core-ui/checkbox';
import {
  TmGridEngine,
  tmComputeAxisWindow,
  type TmGridColumnType,
  type TmGridEngineColumn,
  type TmGridHistorySnapshot,
  type TmRowCol,
} from '@tellma/core-ui/grid-engine';

import type { TmGridColumn } from '../tm-grid-column';
import type {
  TmGridDisplayContext,
  TmGridDisplayDef,
  TmGridEmptyDef,
  TmGridHeaderContext,
  TmGridLoadingDef,
} from '../tm-grid-templates';
import type { TmGridStateHandle, TmGridStateStore } from '../tm-grid-state-store';
import { ɵTmGridAnnouncements } from './announcements';
import { ɵTmGridClipboardDom, ɵTM_GRID_OVERSIZE_COPY_CELLS } from './clipboard-dom';
import { ɵTmGridColumnResize } from './column-resize';
import { tmResolveGridKey, type TmGridIntent } from './grid-keymap';
import { tmFormatNumber, tmParseNumber } from './tm-number-codec';

/** Row height fallback (px) while the `--grid-row-height` token is unresolvable. */
const DEFAULT_ROW_HEIGHT = 32;
/** Rows rendered beyond the visible slice on each side. */
const OVERSCAN_ROWS = 4;
/** Distance from a viewport edge (px) inside which a drag auto-scrolls. */
const EDGE_SCROLL_ZONE_PX = 24;
/** Auto-scroll speed per animation frame, in px. */
const EDGE_SCROLL_STEP_PX = 16;

/** Column types that carry a built-in editing path. */
const BUILT_IN_EDIT_TYPES: ReadonlySet<TmGridColumnType> = new Set([
  'text',
  'number',
  'boolean',
  'enum',
]);

/** ⌘ is the platform modifier on Apple platforms; Ctrl everywhere else. */
const IS_MAC_PLATFORM =
  typeof navigator !== 'undefined' && /Mac|iPod|iPhone|iPad/.test(navigator.platform);

/** Natively-focusable content consumers may project into cells/headers. */
const INTERACTIVE_CONTENT_SELECTOR = 'a[href], button, input, select, textarea, [tabindex]';

function cellSelector(cell: TmRowCol): string {
  return `[data-tm-cell][data-row="${cell.row}"][data-col="${cell.col}"]`;
}

function isHistorySnapshot(value: unknown): value is TmGridHistorySnapshot {
  return (
    typeof value === 'object' && value !== null && (value as { version?: unknown }).version === 1
  );
}

/** The built-in parse closure of a column type, or `undefined` when the type has none. */
function defaultParseFor(
  type: TmGridColumnType,
  enumLabels: ReadonlyMap<unknown, string> | null,
): ((text: string, ctx: TmParseContext) => unknown | TmParseError) | undefined {
  switch (type) {
    case 'text':
      return (text) => text;
    case 'number':
      return (text, ctx) => tmParseNumber(text, ctx.locale, ctx.sourceLocale);
    case 'boolean':
      return (text) => {
        const normalized = text.trim().toLowerCase();
        if (normalized === '') {
          return null;
        }
        if (normalized === 'true' || normalized === '1') {
          return true;
        }
        if (normalized === 'false' || normalized === '0') {
          return false;
        }
        return TM_PARSE_ERROR;
      };
    case 'enum':
      return (text) => {
        const trimmed = text.trim();
        if (trimmed === '') {
          return null;
        }
        if (enumLabels !== null) {
          for (const [value, label] of enumLabels) {
            if (label === trimmed) {
              return value;
            }
          }
          const lower = trimmed.toLowerCase();
          let match: unknown;
          let matches = 0;
          for (const [value, label] of enumLabels) {
            if (label.toLowerCase() === lower) {
              match = value;
              matches += 1;
            }
          }
          if (matches === 1) {
            return match;
          }
        }
        return TM_PARSE_ERROR;
      };
    default:
      return undefined;
  }
}

/**
 * One column's resolved view model: the type defaults folded with the
 * column definition's inputs. Deliberately free of the grid's row type so
 * the shared view component renders it without knowing `T`.
 */
export interface ɵTmGridColumnVm {
  /** Data-column index (0-based, display order). */
  readonly index: number;
  /** `aria-colindex` (1-based; the row-header column is 1). */
  readonly ariaColIndex: number;
  /** Stable identity: the model key, else a generated id. */
  readonly id: string;
  /** The model key, or `null` for accessor columns. */
  readonly key: string | null;
  /** The column's built-in type. */
  readonly type: TmGridColumnType;
  /** The header label. */
  readonly header: Signal<string>;
  /** Rich header template, when projected. */
  readonly headerTemplate: TemplateRef<TmGridHeaderContext> | undefined;
  /** Resolved cell alignment (type default folded with the `align` input). */
  readonly align: 'start' | 'end' | 'center' | 'left' | 'right';
  /** Declared minimum width in px, if any. */
  readonly minWidth: number | undefined;
  /** Declared fixed width in px, if any. */
  readonly width: number | undefined;
  /** Declared proportional share, if any. */
  readonly flex: number | undefined;
}

/** The column model plus the row-typed closures the core keeps to itself. */
interface ColumnInternal<T> extends ɵTmGridColumnVm {
  readonly engineColumn: TmGridEngineColumn<T>;
  readonly displayDef: TmGridDisplayDef<T, unknown> | undefined;
}

/** One rendered cell's view model. */
export interface ɵTmGridCellVm {
  /** Data-column index. */
  readonly colIndex: number;
  /** `aria-colindex` (offset by the row-header column). */
  readonly ariaColIndex: number;
  /** The cell's display text. */
  readonly text: string;
  /** Resolved alignment. */
  readonly align: 'start' | 'end' | 'center' | 'left' | 'right';
  /** Whether the cell lies inside a selection range. */
  readonly selected: boolean;
  /** Whether the cell is the active cell. */
  readonly active: boolean;
  /** Boolean columns: the token-driven glyph class; `undefined` otherwise. */
  readonly glyphClass: string | undefined;
  /** Custom display template, when the column projects one. */
  readonly displayTemplate: TemplateRef<TmGridDisplayContext<unknown, unknown>> | undefined;
  /** The custom display template's context. */
  readonly displayCtx: TmGridDisplayContext<unknown, unknown> | undefined;
}

/** One rendered row's view model. */
export interface ɵTmGridRowVm {
  /** Identity for view reuse: the row id (type-prefixed) or the placeholder sentinel. */
  readonly rowKey: string;
  /** View-space row index. */
  readonly viewIndex: number;
  /** `aria-rowindex` (1-based, counting the header row). */
  readonly ariaRowIndex: number;
  /** Whether this is the new-row placeholder. */
  readonly isPlaceholder: boolean;
  /** Whether the row renders outside the window (the always-rendered active row). */
  readonly outlier: boolean;
  /** The outlier row's own translate, or `null` for in-window rows. */
  readonly outlierTransform: string | null;
  /** Whether the row carries the readonly zebra stripe. */
  readonly zebra: boolean;
  /** Whether the row header highlights (a selection range covers the row). */
  readonly headerHit: boolean;
  /** The row-header text (1-based row number, `*` on the placeholder). */
  readonly rowHeaderText: string;
  /** The row's cells, in column order. */
  readonly cells: readonly ɵTmGridCellVm[];
}

/**
 * Everything the core needs from its host component, gathered by
 * `ɵTmGridBase` (which owns the DI) and handed over as one object — the
 * core itself is a plain class so the flat grid and the future tree grid
 * share it without inheritance gymnastics.
 */
export interface ɵTmGridCoreDeps<T> {
  /** The grid component's host element (token measurement, `--grid-template`). */
  readonly host: HTMLElement;
  /** The host component's injector (effect scheduling). */
  readonly injector: Injector;
  /** The host component's destroy hook (persistence + disposal). */
  readonly destroyRef: DestroyRef;
  /** The reading direction. */
  readonly direction: Signal<'ltr' | 'rtl'>;
  /** The CDK live announcer. */
  readonly announcer: LiveAnnouncer;
  /** The library string resolver. */
  readonly translate: TmUiTranslateFn;
  /** The grid state store. */
  readonly store: TmGridStateStore;
  /** The active locale (formatting, parse context, clipboard metadata). */
  readonly locale: string;
  /** The grid definition's stable identity. */
  readonly gridId: Signal<string>;
  /** The content identity. */
  readonly contentKey: Signal<string | number | undefined>;
  /** The readonly rows binding. */
  readonly data: Signal<readonly T[] | undefined>;
  /** The editable field-tree binding. */
  readonly field: Signal<FieldTree<T[]> | undefined>;
  /** Reads a row's stable identity. */
  readonly rowId: Signal<(row: T) => TmRowId>;
  /** The `readonly` input (editable ⇔ field bound ∧ not readonly). */
  readonly readonlyInput: Signal<boolean>;
  /** The new-row factory (placeholder + paste overflow rows). */
  readonly newRow: Signal<((parent?: T) => T) | undefined>;
  /** The loading flag. */
  readonly loading: Signal<boolean>;
  /** Whether the find bar is enabled. */
  readonly searchable: Signal<boolean>;
  /** Whether row checkbox selection is enabled. */
  readonly selectable: Signal<boolean>;
  /** The density variant. */
  readonly size: Signal<'sm' | 'md' | 'lg'>;
  /** The projected column definitions, in display order. */
  readonly columns: Signal<ReadonlyArray<TmGridColumn<T, unknown>>>;
  /** The projected empty-state template, if any. */
  readonly emptyDef: Signal<TmGridEmptyDef | undefined>;
  /** The projected loading-state template, if any. */
  readonly loadingDef: Signal<TmGridLoadingDef | undefined>;
}

/**
 * The row-type-free face of {@link ɵTmGridCore} that the shared view
 * component (`ɵTmGridView`) renders from. The erasure is deliberate: the
 * view template touches only view models and DOM events, so one compiled
 * template serves `tm-grid<T>` (and, later, `tm-tree-grid<T>`) for every
 * `T` without variance friction.
 */
export interface ɵTmGridViewCore {
  /** Whether the grid is editable (field bound and not readonly). */
  readonly editable: Signal<boolean>;
  /** The loading flag. */
  readonly loading: Signal<boolean>;
  /** Whether the built-in empty state shows (readonly, loaded, zero rows). */
  readonly showEmpty: Signal<boolean>;
  /** The projected empty-state template, if any. */
  readonly emptyDef: Signal<TmGridEmptyDef | undefined>;
  /** The projected loading-state template, if any. */
  readonly loadingDef: Signal<TmGridLoadingDef | undefined>;
  /** The localized built-in loading text. */
  readonly loadingText: Signal<string>;
  /** The localized built-in empty text. */
  readonly emptyText: Signal<string>;
  /** `aria-rowcount`: all view rows plus the header row. */
  readonly ariaRowCount: Signal<number>;
  /** `aria-colcount`: the data columns plus the row-header column. */
  readonly ariaColCount: Signal<number>;
  /** The scroller's tabindex (0 only while it is the tab stop). */
  readonly containerTabindex: Signal<number>;
  /** Whether Esc parked focus on the container (cells leave the tab order). */
  readonly escaped: Signal<boolean>;
  /** The resolved column view models. */
  readonly columnModel: Signal<readonly ɵTmGridColumnVm[]>;
  /** Per-column header highlight (a selection range covers the column). */
  readonly hitCols: Signal<readonly boolean[]>;
  /** The spacer height in px (`rowCount × rowHeight`). */
  readonly totalHeight: Signal<number>;
  /** The window block's translate. */
  readonly windowTransform: Signal<string>;
  /** The rows to render: the window slice plus the active-row outlier. */
  readonly renderRows: Signal<readonly ɵTmGridRowVm[]>;
  /** The column-resize pointer controller. */
  readonly resize: ɵTmGridColumnResize;
  /** Hands the core the scroll container once the view rendered it. */
  attachScroller(element: HTMLElement): void;
  /** The scroller's scroll handler. */
  onScroll(event: Event): void;
  /** The scroller's keydown handler. */
  onKeydown(event: KeyboardEvent): void;
  /** The scroller's pointerdown handler (cells, row headers, drags). */
  onPointerDown(event: PointerEvent): void;
  /** The scroller's click handler (column headers, corner). */
  onClick(event: MouseEvent): void;
  /** The scroller's copy handler. */
  onCopy(event: ClipboardEvent): void;
  /** The scroller's cut handler. */
  onCut(event: ClipboardEvent): void;
  /** The scroller's paste handler. */
  onPaste(event: ClipboardEvent): void;
}

/**
 * The grid's composition root: owns the engine instance, the column model
 * (directive inputs + type defaults → engine closures), virtualization
 * state, the keyboard/pointer/clipboard pipelines, roving focus, and the
 * state-store lifecycle. Constructed by `ɵTmGridBase` with the host's
 * dependencies; the view component (`ɵTmGridView`) renders its signals and
 * routes DOM events back into it.
 */
export class ɵTmGridCore<T> implements ɵTmGridViewCore {
  private readonly deps: ɵTmGridCoreDeps<T>;
  private readonly announcements: ɵTmGridAnnouncements;
  private readonly clipboardDom: ɵTmGridClipboardDom;
  private engineInstance: TmGridEngine<T> | null = null;
  private handle: TmGridStateHandle | null = null;
  private lastContentKey: string | number | undefined;
  private pendingScroll: TmGridScrollPosition | null = null;
  private pendingGestureFocus = false;
  private dragCleanup: (() => void) | null = null;
  /** Columns already warned about a missing required `format` (one warn each). */
  private readonly warnedColumns = new Set<string>();

  private readonly scrollerSignal = signal<HTMLElement | null>(null);
  private readonly scrollTop = signal(0);
  private readonly scrollLeft = signal(0);
  private readonly viewportHeight = signal(0);
  private readonly rowHeightSignal = signal(DEFAULT_ROW_HEIGHT);
  private readonly escapedSignal = signal(false);
  private readonly widthOverrides = signal<ReadonlyMap<string, number>>(new Map());
  private readonly focusRequest = signal(0);
  private readonly revealRequest = signal(0);
  private readonly scrollRestoreRequest = signal(0);

  private readonly columnsInternal: Signal<readonly ColumnInternal<T>[]>;
  private readonly engineColumns: Signal<ReadonlyArray<TmGridEngineColumn<T>>>;
  private readonly window: Signal<ReturnType<typeof tmComputeAxisWindow>>;

  /** The bound rows: `data`, else the field's value, else empty. */
  readonly rows: Signal<readonly T[]>;
  /** Whether the grid is editable (field bound and not readonly). */
  readonly editable: Signal<boolean>;
  /** The loading flag. */
  readonly loading: Signal<boolean>;
  /** Whether the built-in empty state shows. */
  readonly showEmpty: Signal<boolean>;
  /** The projected empty-state template, if any. */
  readonly emptyDef: Signal<TmGridEmptyDef | undefined>;
  /** The projected loading-state template, if any. */
  readonly loadingDef: Signal<TmGridLoadingDef | undefined>;
  /** The localized built-in loading text. */
  readonly loadingText: Signal<string>;
  /** The localized built-in empty text. */
  readonly emptyText: Signal<string>;
  /** `aria-rowcount`: all view rows plus the header row. */
  readonly ariaRowCount: Signal<number>;
  /** `aria-colcount`: the data columns plus the row-header column. */
  readonly ariaColCount: Signal<number>;
  /** The scroller's tabindex. */
  readonly containerTabindex: Signal<number>;
  /** Whether Esc parked focus on the container. */
  readonly escaped: Signal<boolean>;
  /** The resolved column view models. */
  readonly columnModel: Signal<readonly ɵTmGridColumnVm[]>;
  /** Per-column header highlight flags. */
  readonly hitCols: Signal<readonly boolean[]>;
  /** The resolved row height in px. */
  readonly rowHeight: Signal<number>;
  /** Rows per viewport page (PageUp/PageDown motion size). */
  readonly pageSize: Signal<number>;
  /** The spacer height in px. */
  readonly totalHeight: Signal<number>;
  /** The window block's translate. */
  readonly windowTransform: Signal<string>;
  /** The rows to render. */
  readonly renderRows: Signal<readonly ɵTmGridRowVm[]>;
  /** The `grid-template-columns` value bound on the host as `--grid-template`. */
  readonly gridTemplate: Signal<string>;
  /** Count of cells in error state (field validation errors arrive with the editing milestone). */
  readonly errorCount: Signal<number>;
  /** Count of cells awaiting async paste resolutions. */
  readonly pendingCount: Signal<number>;
  /** The column-resize pointer controller. */
  readonly resize: ɵTmGridColumnResize;

  constructor(deps: ɵTmGridCoreDeps<T>) {
    this.deps = deps;
    this.announcements = new ɵTmGridAnnouncements(deps.announcer, deps.translate);
    this.clipboardDom = new ɵTmGridClipboardDom({
      copy: () => this.engine.clipboard.copy(),
      announcements: this.announcements,
    });
    this.resize = new ɵTmGridColumnResize({
      widthOverrides: this.widthOverrides,
      direction: deps.direction,
      persist: () => this.persistWidths(),
    });

    this.rows = computed(() => {
      const data = deps.data();
      if (data !== undefined) {
        return data;
      }
      const field = deps.field();
      if (field !== undefined) {
        return field().value();
      }
      return [];
    });
    this.editable = computed(() => deps.field() !== undefined && !deps.readonlyInput());
    this.loading = deps.loading;
    this.emptyDef = deps.emptyDef;
    this.loadingDef = deps.loadingDef;
    this.loadingText = deps.translate('grid.loading');
    this.emptyText = deps.translate('grid.empty');
    this.rowHeight = this.rowHeightSignal.asReadonly();
    this.escaped = this.escapedSignal.asReadonly();

    this.columnsInternal = computed(() =>
      deps.columns().map((dir, index) => this.buildColumn(dir, index)),
    );
    this.columnModel = this.columnsInternal;
    this.engineColumns = computed(() =>
      this.columnsInternal().map((column) => column.engineColumn),
    );

    this.pageSize = computed(() => {
      const rowHeight = this.rowHeightSignal();
      const bodyHeight = Math.max(0, this.viewportHeight() - rowHeight);
      return Math.max(1, Math.floor(bodyHeight / rowHeight));
    });
    this.window = computed(() =>
      tmComputeAxisWindow({
        scrollOffset: this.scrollTop(),
        viewportSize: Math.max(0, this.viewportHeight() - this.rowHeightSignal()),
        itemSize: this.rowHeightSignal(),
        itemCount: this.engine.model.viewRowCount(),
        overscan: OVERSCAN_ROWS,
      }),
    );
    this.totalHeight = computed(() => this.window().totalSize);
    this.windowTransform = computed(() => `translateY(${this.window().leadOffset}px)`);

    this.ariaRowCount = computed(() => this.engine.model.viewRowCount() + 1);
    this.ariaColCount = computed(() => this.engine.model.columnCount() + 1);
    this.showEmpty = computed(
      () => !deps.loading() && !this.editable() && this.engine.model.viewRowCount() === 0,
    );
    this.containerTabindex = computed(() =>
      this.engine.nav.activeCell() === null || this.escapedSignal() ? 0 : -1,
    );
    this.hitCols = computed(() => {
      const engine = this.engine;
      engine.selection.ranges();
      return this.columnsInternal().map((column) => engine.selection.colIntersects(column.index));
    });
    this.renderRows = computed(() => this.buildRenderRows());
    this.gridTemplate = computed(() => {
      const overrides = this.widthOverrides();
      const tracks = ['var(--grid-row-header-width)'];
      for (const column of this.columnsInternal()) {
        const width = overrides.get(column.id) ?? column.width;
        if (width !== undefined) {
          tracks.push(`${Math.round(width)}px`);
        } else {
          const min =
            column.minWidth !== undefined ? `${column.minWidth}px` : 'var(--grid-min-col-width)';
          tracks.push(`minmax(${min}, ${column.flex ?? 1}fr)`);
        }
      }
      return tracks.join(' ');
    });
    this.errorCount = computed(() => this.engine.annotations.invalidCount());
    this.pendingCount = computed(() => this.engine.annotations.pendingCount());

    this.setupEffects();
    deps.destroyRef.onDestroy(() => this.onDestroy());
  }

  /**
   * The engine instance, created on first use (inputs are not readable at
   * construction time). Creation runs untracked: it must neither register
   * dependencies on whichever computed touches it first nor trip the
   * write-in-computed guard (the engine seeds its expansion signal).
   */
  get engine(): TmGridEngine<T> {
    return (this.engineInstance ??= untracked(() => this.createEngine()));
  }

  /** Hands the core the scroll container once the view rendered it. */
  attachScroller(element: HTMLElement): void {
    if (untracked(this.scrollerSignal) !== element) {
      this.scrollerSignal.set(element);
    }
  }

  /** Drops the user-visible undo history (consumer save/cancel moments). */
  clearHistory(): void {
    this.engineInstance?.history.clear();
    this.handle?.clearHistory();
  }

  /** Focuses the active cell, or the scroller when no cell is active. */
  focus(): void {
    const scroller = untracked(this.scrollerSignal);
    if (scroller === null) {
      return;
    }
    const element = this.activeCellElement();
    if (element !== null && !untracked(this.escapedSignal)) {
      element.focus();
    } else {
      scroller.focus();
    }
  }

  // ---- DOM event pipeline (wired by the view template) ----

  /** The scroller's scroll handler. */
  onScroll(event: Event): void {
    const element = event.currentTarget as HTMLElement;
    this.scrollTop.set(element.scrollTop);
    this.scrollLeft.set(element.scrollLeft);
  }

  /** The scroller's keydown handler: resolve to an intent, execute, consume. */
  onKeydown(event: KeyboardEvent): void {
    if (event.isComposing || event.keyCode === 229) {
      // IME composition: the editing milestone opens an unseeded editor
      // here; the readonly core stays out of the composition session.
      return;
    }
    const engine = this.engine;
    const active = untracked(() => engine.nav.activeCell());
    const activeIsBoolean =
      active !== null && untracked(this.columnsInternal)[active.col]?.type === 'boolean';
    const intent = tmResolveGridKey(event, {
      isMac: IS_MAC_PLATFORM,
      editable: untracked(this.editable),
      searchable: untracked(this.deps.searchable),
      // Row checkbox selection and the tree grid are later milestones.
      selectable: false,
      isTree: false,
      activeIsBoolean,
    });
    if (intent !== null && this.executeIntent(intent)) {
      event.preventDefault();
    }
  }

  /** The scroller's pointerdown handler: cell presses, row headers, drags. */
  onPointerDown(event: PointerEvent): void {
    if (event.button !== 0 || !(event.target instanceof Element)) {
      return;
    }
    const target = event.target;
    if (target.closest('[data-tm-resize]') !== null) {
      return; // the resize controller owns the gesture
    }
    const mod = IS_MAC_PLATFORM ? event.metaKey : event.ctrlKey;
    const cellElement = target.closest('[data-tm-cell]');
    if (cellElement !== null) {
      const cell = this.cellFromElement(cellElement);
      if (cell === null) {
        return;
      }
      // A press on interactive projected content (a record link) must keep
      // its native affordances — click activation, middle-click,
      // open-in-new-tab. Activate the cell but leave the event alone: no
      // preventDefault (it would suppress the compatibility click) and no
      // pointer capture (it would retarget the click to the scroller).
      // The cell itself carries a roving tabindex — only STRICT descendants
      // count as interactive content.
      const interactive = target.closest(INTERACTIVE_CONTENT_SELECTOR);
      if (interactive !== null && interactive !== cellElement && cellElement.contains(interactive)) {
        this.escapedSignal.set(false);
        this.engine.clickCell(cell, { shift: event.shiftKey, mod });
        return;
      }
      event.preventDefault();
      this.escapedSignal.set(false);
      this.engine.clickCell(cell, { shift: event.shiftKey, mod });
      this.requestFocusActive();
      this.beginDrag(event, 'cells');
      return;
    }
    const rowHeader = target.closest('[data-tm-rowhdr]');
    if (rowHeader !== null) {
      const row = Number(rowHeader.getAttribute('data-row'));
      if (!Number.isInteger(row)) {
        return;
      }
      event.preventDefault();
      this.escapedSignal.set(false);
      if (event.shiftKey) {
        this.engine.selection.extendActiveTo({ row, col: 0 });
      } else {
        this.engine.nav.setActive({ row, col: 0 });
        this.engine.selection.selectRows(row, row, mod);
        this.requestFocusActive();
      }
      this.beginDrag(event, 'rows');
    }
  }

  /** The scroller's click handler: column headers and the select-all corner. */
  onClick(event: MouseEvent): void {
    if (!(event.target instanceof Element)) {
      return;
    }
    const target = event.target;
    if (target.closest('[data-tm-resize]') !== null) {
      return;
    }
    if (target.closest('[data-tm-corner]') !== null) {
      this.engine.selection.selectAll();
      return;
    }
    const header = target.closest('[data-tm-colhdr]');
    if (header === null) {
      return;
    }
    // Interactive projected header content never triggers column selection.
    const interactive = target.closest('a, button, input, select, [tabindex]');
    if (interactive !== null && header.contains(interactive)) {
      return;
    }
    const col = Number(header.getAttribute('data-col'));
    if (!Number.isInteger(col)) {
      return;
    }
    const engine = this.engine;
    const mod = IS_MAC_PLATFORM ? event.metaKey : event.ctrlKey;
    this.escapedSignal.set(false);
    if (untracked(() => engine.model.viewRowCount()) > 0) {
      engine.nav.setActive({ row: 0, col });
    }
    engine.selection.selectCols(col, col, mod);
    this.requestFocusActive();
  }

  /** The scroller's copy handler. */
  onCopy(event: ClipboardEvent): void {
    this.clipboardDom.onCopy(event);
  }

  /** The scroller's cut handler (copy semantics in the readonly core). */
  onCut(event: ClipboardEvent): void {
    this.clipboardDom.onCut(event);
  }

  /** The scroller's paste handler — a deliberate no-op until the paste milestone. */
  onPaste(event: ClipboardEvent): void {
    void event;
  }

  // ---- internals ----

  private createEngine(): TmGridEngine<T> {
    return new TmGridEngine<T>({
      rows: () => this.rows(),
      rowId: (row) => this.deps.rowId()(row),
      columns: () => this.engineColumns(),
      editable: () => this.editable(),
      canAddRows: () => this.deps.newRow() !== undefined,
      locale: () => this.deps.locale,
      // Tenant identity wiring (cross-tenant paste guard) lands with paste.
      direction: () => this.deps.direction(),
      pageSize: () => this.pageSize(),
      oversizeCopyCellThreshold: ɵTM_GRID_OVERSIZE_COPY_CELLS,
      host: {
        // The field-tree writer lands with the editing milestone; with no
        // writer every engine mutation is a structural no-op, which is the
        // readonly contract.
        writer: undefined,
        onNotice: (notice) => this.announcements.notice(notice),
        onReveal: () => this.requestReveal(),
        onWarn: (warning) => {
          if (isDevMode()) {
            console.warn(
              `tm-grid[${untracked(this.deps.gridId)}]: data irregularity '${warning.kind}' ` +
                `(rowId: ${String(warning.rowId)})`,
            );
          }
        },
      },
    });
  }

  private buildColumn(dir: TmGridColumn<T, unknown>, index: number): ColumnInternal<T> {
    const locale = this.deps.locale;
    const key = dir.key() ?? null;
    const id = key ?? dir.generatedId;
    const type = dir.type();
    const accessor = dir.value();
    const format = dir.format();
    const customParse = dir.parse();
    const readonlyOption = dir.readonly();
    const defaultValue = dir.defaultValue();
    const hasResolver = dir.resolvePastedLabels() !== undefined;
    const displayDef = dir.displayDef();
    const editorDef = dir.editorDef();

    const getValue: (row: T) => unknown =
      key !== null
        ? (row) => (row as Record<string, unknown>)[key]
        : accessor !== undefined
          ? (row) => accessor(row)
          : () => null;

    let enumLabels: ReadonlyMap<unknown, string> | null = null;
    if (type === 'enum') {
      const options = dir.options();
      if (options !== undefined) {
        const optionLabel = dir.optionLabel() as ((option: unknown) => string) | undefined;
        const optionValue = dir.optionValue() as ((option: unknown) => unknown) | undefined;
        const labels = new Map<unknown, string>();
        for (const option of options) {
          const value = optionValue !== undefined ? optionValue(option) : option;
          if (!labels.has(value)) {
            labels.set(value, optionLabel !== undefined ? optionLabel(option) : String(option));
          }
        }
        enumLabels = labels;
      }
    }

    if (
      isDevMode() &&
      format === undefined &&
      (type === 'entity' || type === 'date') &&
      !this.warnedColumns.has(id)
    ) {
      this.warnedColumns.add(id);
      console.warn(
        `tm-grid: column "${id}" of type '${type}' has no [format]; ` +
          `cells fall back to String(value). Bind [format] to give the ` +
          `column a real text representation.`,
      );
    }

    const fallbackText = (value: unknown): string =>
      value === null || value === undefined ? '' : String(value);
    const typeText = (value: unknown): string => {
      switch (type) {
        case 'number':
          return tmFormatNumber(value, locale);
        case 'boolean':
          return TM_CHECKBOX_CELL_DISPLAY.formatValue((value ?? null) as boolean | null, locale);
        case 'enum':
          return enumLabels?.get(value) ?? fallbackText(value);
        default:
          return fallbackText(value);
      }
    };
    const getText: (row: T) => string =
      format !== undefined ? (row) => format(getValue(row), row) : (row) => typeText(getValue(row));

    const parse = customParse ?? defaultParseFor(type, enumLabels);
    const readonlyFn = typeof readonlyOption === 'function' ? readonlyOption : null;
    const alwaysReadonly = readonlyOption === true;

    const engineColumn: TmGridEngineColumn<T> = {
      key,
      id,
      type,
      headerLabel: dir.header,
      getValue,
      getText,
      editable:
        key !== null &&
        (BUILT_IN_EDIT_TYPES.has(type) || customParse !== undefined || editorDef !== undefined),
      // Per-cell field state (disabled/readonly child fields win over the
      // column setting) folds in with the editing milestone.
      isCellReadonly: (row) => alwaysReadonly || (readonlyFn !== null && readonlyFn(row)),
      ...(parse !== undefined ? { parse } : {}),
      hasResolver,
      clearedValue: defaultValue !== undefined ? defaultValue : type === 'boolean' ? false : null,
    };

    return {
      index,
      ariaColIndex: index + 2,
      id,
      key,
      type,
      header: dir.header,
      headerTemplate: dir.headerDef()?.template,
      align:
        dir.align() ??
        (type === 'number' || type === 'date' ? 'right' : type === 'boolean' ? 'center' : 'start'),
      minWidth: dir.minWidth(),
      width: dir.width(),
      flex: dir.flex(),
      engineColumn,
      displayDef,
    };
  }

  private buildRenderRows(): readonly ɵTmGridRowVm[] {
    const engine = this.engine;
    const win = this.window();
    const columns = this.columnsInternal();
    // Reactive dependencies read once; per-cell helpers below are untracked.
    engine.selection.ranges();
    engine.model.viewRows();
    const active = engine.nav.activeCell();
    const editable = this.editable();
    const rowHeight = this.rowHeightSignal();
    const isTree = engine.model.isTree;

    const rows: ɵTmGridRowVm[] = [];
    const pushRow = (viewIndex: number, outlier: boolean): void => {
      const model = engine.model;
      const isPlaceholder = model.isPlaceholder(viewIndex);
      const view = model.rowAt(viewIndex);
      if (!isPlaceholder && view === null) {
        return;
      }
      const rowKey = isPlaceholder
        ? ' placeholder'
        : `${typeof view!.id === 'number' ? '#' : '$'}${String(view!.id)}`;
      const cells = columns.map((column): ɵTmGridCellVm => {
        const cell: TmRowCol = { row: viewIndex, col: column.index };
        const isActive = active !== null && active.row === viewIndex && active.col === column.index;
        let displayTemplate: TemplateRef<TmGridDisplayContext<unknown, unknown>> | undefined;
        let displayCtx: TmGridDisplayContext<unknown, unknown> | undefined;
        if (!isPlaceholder && view !== null && column.displayDef !== undefined) {
          displayTemplate = column.displayDef.template;
          displayCtx = {
            $implicit: column.engineColumn.getValue(view.row),
            row: view.row,
            rowId: view.id,
            invalid: engine.annotations.invalidInput(view.id, column.id) !== undefined,
            readonly: !model.isCellEditable(cell),
          };
        }
        const glyphClass =
          displayTemplate === undefined && !isPlaceholder && column.type === 'boolean'
            ? TM_CHECKBOX_CELL_DISPLAY.displayClass!(
                (model.cellValue(cell) ?? null) as boolean | null,
              )
            : undefined;
        return {
          colIndex: column.index,
          ariaColIndex: column.ariaColIndex,
          text: engine.displayText(cell),
          align: column.align,
          selected: engine.selection.isCellSelected(cell),
          active: isActive,
          glyphClass,
          displayTemplate,
          displayCtx,
        };
      });
      rows.push({
        rowKey,
        viewIndex,
        ariaRowIndex: viewIndex + 2,
        isPlaceholder,
        outlier,
        outlierTransform: outlier
          ? `translateY(${(viewIndex - win.start) * rowHeight}px)`
          : null,
        zebra: !editable && !isTree && viewIndex % 2 === 1,
        headerHit: engine.selection.rowIntersects(viewIndex),
        rowHeaderText: isPlaceholder ? '*' : String(viewIndex + 1),
        cells,
      });
    };

    // The array stays sorted by view index so a scroll never REORDERS the
    // active row's view among survivors — reordering would move its DOM
    // node and drop focus; prepend/append keeps it in place.
    if (active !== null && active.row < win.start) {
      pushRow(active.row, true);
    }
    for (let viewIndex = win.start; viewIndex < win.end; viewIndex++) {
      pushRow(viewIndex, false);
    }
    if (active !== null && active.row >= win.end) {
      pushRow(active.row, true);
    }
    return rows;
  }

  private executeIntent(intent: TmGridIntent): boolean {
    const engine = this.engine;
    if (intent.kind !== 'escape') {
      this.escapedSignal.set(false);
    }
    switch (intent.kind) {
      case 'move':
        engine.moveActive(intent.motion, { extend: intent.extend, jump: intent.jump });
        this.requestReveal();
        this.requestFocusActive();
        return true;
      case 'tab': {
        const target = engine.nav.tab(intent.backward);
        if (target === null || target === 'exit') {
          // Every other cell is tabindex -1, so the browser's own Tab from
          // the focused active cell exits the grid — don't consume the key.
          return false;
        }
        engine.nav.setActive(target, { keepTabRun: true });
        engine.selection.collapseTo(target);
        this.requestReveal();
        this.requestFocusActive();
        return true;
      }
      case 'enter': {
        // Editable cells open an editor here once the editing milestone
        // lands; the readonly behavior applies to every cell until then.
        if (!intent.backward && this.activateCellLink()) {
          return true;
        }
        engine.moveActive(intent.backward ? 'up' : 'down');
        this.requestReveal();
        this.requestFocusActive();
        return true;
      }
      case 'clear':
        engine.clearSelection();
        return true;
      case 'fillDown':
        engine.clipboard.fillDown();
        return true;
      case 'undo':
        engine.history.undo();
        return true;
      case 'redo':
        engine.history.redo();
        return true;
      case 'deleteRows':
        engine.deleteSelectedRows();
        return true;
      case 'insertRowsAbove':
        engine.insertRows('above');
        return true;
      case 'selectRows':
        engine.selectActiveRows(false);
        return true;
      case 'selectCols':
        engine.selectActiveCols(false);
        return true;
      case 'selectAll':
        engine.selection.selectAll();
        return true;
      case 'escape':
        if (!engine.escape()) {
          // The mid-grid exit: the container becomes the single tab stop
          // (cells leave the tab order); any arrow re-enters at the active cell.
          this.escapedSignal.set(true);
          untracked(this.scrollerSignal)?.focus();
        }
        return true;
      default:
        // edit/toggleBoolean (editing milestone), menu (context-menu
        // milestone), find (find-bar milestone), toggleCheck /
        // toggleSelectAllCheckbox (row-checkbox milestone), expand /
        // collapse (tree milestone).
        return false;
    }
  }

  /** Enter on a readonly cell activates its first interactive child (a record link). */
  private activateCellLink(): boolean {
    const element = this.activeCellElement();
    const interactive = element?.querySelector<HTMLElement>('a, button') ?? null;
    if (interactive === null) {
      return false;
    }
    interactive.click();
    return true;
  }

  private cellFromElement(element: Element): TmRowCol | null {
    const row = Number(element.getAttribute('data-row'));
    const col = Number(element.getAttribute('data-col'));
    return Number.isInteger(row) && Number.isInteger(col) ? { row, col } : null;
  }

  private activeCellElement(): HTMLElement | null {
    const scroller = untracked(this.scrollerSignal);
    const active = untracked(() => this.engine.nav.activeCell());
    if (scroller === null || active === null) {
      return null;
    }
    return scroller.querySelector<HTMLElement>(cellSelector(active));
  }

  private requestFocusActive(): void {
    this.pendingGestureFocus = true;
    this.focusRequest.update((value) => value + 1);
  }

  private requestReveal(): void {
    this.revealRequest.update((value) => value + 1);
  }

  /**
   * Scrolls the active cell into view: vertical from the fixed row-height
   * math (accounting for the sticky header band), horizontal from rects
   * (proportional columns resolve in CSS, and rect deltas are direction-safe).
   */
  private revealActiveCell(): void {
    const scroller = untracked(this.scrollerSignal);
    const active = untracked(() => this.engine.nav.activeCell());
    if (scroller === null || active === null) {
      return;
    }
    const rowHeight = untracked(this.rowHeightSignal);
    const rowTop = active.row * rowHeight;
    const rowBottom = rowTop + rowHeight;
    const bodyViewport = Math.max(rowHeight, scroller.clientHeight - rowHeight);
    if (rowTop < scroller.scrollTop) {
      scroller.scrollTop = rowTop;
    } else if (rowBottom > scroller.scrollTop + bodyViewport) {
      scroller.scrollTop = rowBottom - bodyViewport;
    }
    const element = scroller.querySelector<HTMLElement>(cellSelector(active));
    if (element !== null) {
      const cellRect = element.getBoundingClientRect();
      const scrollerRect = scroller.getBoundingClientRect();
      if (cellRect.right > scrollerRect.right) {
        scroller.scrollLeft += cellRect.right - scrollerRect.right;
      }
      if (cellRect.left < scrollerRect.left) {
        scroller.scrollLeft += cellRect.left - scrollerRect.left;
      }
    }
    this.scrollTop.set(scroller.scrollTop);
    this.scrollLeft.set(scroller.scrollLeft);
  }

  private beginDrag(event: PointerEvent, mode: 'cells' | 'rows'): void {
    const scroller = untracked(this.scrollerSignal);
    if (scroller === null) {
      return;
    }
    this.endDrag();
    const pointerId = event.pointerId;
    try {
      scroller.setPointerCapture(pointerId);
    } catch {
      // Synthetic events may carry no active pointer.
    }
    let lastX = event.clientX;
    let lastY = event.clientY;
    let frame = 0;

    const applyPoint = (): void => {
      const hit = document.elementFromPoint(lastX, lastY);
      const cellElement = hit?.closest('[data-tm-cell], [data-tm-rowhdr]') ?? null;
      if (cellElement === null) {
        return;
      }
      const row = Number(cellElement.getAttribute('data-row'));
      if (!Number.isInteger(row)) {
        return;
      }
      if (mode === 'rows') {
        this.engine.selection.extendActiveTo({ row, col: 0 });
        return;
      }
      const colAttribute = cellElement.getAttribute('data-col');
      const col = colAttribute === null ? 0 : Number(colAttribute);
      if (Number.isInteger(col)) {
        this.engine.dragTo({ row, col });
      }
    };

    const autoScrollStep = (): void => {
      frame = 0;
      const rect = scroller.getBoundingClientRect();
      const headerBand = untracked(this.rowHeightSignal);
      let dx = 0;
      let dy = 0;
      if (lastY < rect.top + headerBand + EDGE_SCROLL_ZONE_PX) {
        dy = -EDGE_SCROLL_STEP_PX;
      } else if (lastY > rect.bottom - EDGE_SCROLL_ZONE_PX) {
        dy = EDGE_SCROLL_STEP_PX;
      }
      if (lastX < rect.left + EDGE_SCROLL_ZONE_PX) {
        dx = -EDGE_SCROLL_STEP_PX;
      } else if (lastX > rect.right - EDGE_SCROLL_ZONE_PX) {
        dx = EDGE_SCROLL_STEP_PX;
      }
      if (dx !== 0 || dy !== 0) {
        scroller.scrollTop += dy;
        scroller.scrollLeft += dx;
        this.scrollTop.set(scroller.scrollTop);
        this.scrollLeft.set(scroller.scrollLeft);
        applyPoint();
        frame = requestAnimationFrame(autoScrollStep);
      }
    };

    const onMove = (move: PointerEvent): void => {
      lastX = move.clientX;
      lastY = move.clientY;
      applyPoint();
      if (frame === 0) {
        frame = requestAnimationFrame(autoScrollStep);
      }
    };
    const onEnd = (): void => this.endDrag();
    scroller.addEventListener('pointermove', onMove);
    scroller.addEventListener('pointerup', onEnd);
    scroller.addEventListener('pointercancel', onEnd);
    this.dragCleanup = () => {
      if (frame !== 0) {
        cancelAnimationFrame(frame);
      }
      scroller.removeEventListener('pointermove', onMove);
      scroller.removeEventListener('pointerup', onEnd);
      scroller.removeEventListener('pointercancel', onEnd);
      try {
        scroller.releasePointerCapture(pointerId);
      } catch {
        // Already released with the pointer.
      }
    };
  }

  private endDrag(): void {
    this.dragCleanup?.();
    this.dragCleanup = null;
  }

  // ---- state store ----

  private restoreState(initial: boolean): void {
    if (this.handle === null) {
      return;
    }
    if (initial) {
      const widths = this.handle.getWidths();
      if (widths !== undefined && Object.keys(widths).length > 0) {
        this.widthOverrides.set(new Map(Object.entries(widths)));
      }
    }
    const content = this.handle.getContentState();
    const engine = this.engine;
    if (content?.scroll !== undefined) {
      this.pendingScroll = content.scroll;
      this.scrollRestoreRequest.update((value) => value + 1);
    } else if (!initial) {
      // A different content starts at the origin.
      this.pendingScroll = { x: 0, y: 0 };
      this.scrollRestoreRequest.update((value) => value + 1);
    }
    if (content?.selection !== undefined) {
      const { restored, activeCell } = engine.selection.restore(content.selection);
      let active = activeCell;
      if (
        active === null &&
        untracked(() => engine.model.viewRowCount()) > 0 &&
        untracked(() => engine.model.columnCount()) > 0
      ) {
        // The engine already tried the row id and the clamped view index;
        // the last leg of the chain is the grid origin.
        active = { row: 0, col: 0 };
      }
      if (active !== null) {
        engine.nav.setActive(active);
        if (!restored) {
          engine.selection.collapseTo(active);
        }
      }
    } else if (!initial) {
      engine.selection.clear();
      engine.nav.setActive(null);
    }
    const history = content?.history;
    if (isHistorySnapshot(history)) {
      engine.history.restore(history);
    } else if (!initial) {
      engine.history.clear();
    }
    if (content?.expandedRowIds !== undefined) {
      engine.model.restoreExpansion(content.expandedRowIds);
    } else if (!initial) {
      engine.model.seedExpansion();
    }
  }

  private persistWidths(): void {
    if (this.handle === null) {
      return;
    }
    const overrides = untracked(this.widthOverrides);
    if (overrides.size === 0) {
      return;
    }
    const widths: Record<string, number> = {};
    for (const column of untracked(this.columnsInternal)) {
      const width = overrides.get(column.id);
      if (width !== undefined && column.key !== null) {
        // Accessor columns have no stable cross-session identity to persist.
        widths[column.key] = width;
      }
    }
    this.handle.setWidths(widths);
  }

  private persistContentState(): void {
    if (this.handle === null || this.engineInstance === null) {
      return;
    }
    const engine = this.engineInstance;
    // The scroll SIGNALS are the source here: at destroy time the scroller
    // may already be detached, and a detached element reports scrollTop 0.
    const state: TmGridContentState = {
      scroll: {
        x: untracked(this.scrollLeft),
        y: untracked(this.scrollTop),
      },
      selection: engine.selection.toSnapshot(untracked(() => engine.nav.activeCell())),
      history: engine.history.toSnapshot(),
      ...(engine.model.isTree
        ? { expandedRowIds: untracked(() => engine.model.expandedIds()) }
        : {}),
    };
    this.handle.setContentState(state);
  }

  // ---- effects ----

  private setupEffects(): void {
    const injector = this.deps.injector;

    // Store registration + restore; content switches snapshot-out/load-in.
    effect(
      () => {
        const gridId = this.deps.gridId();
        const contentKey = this.deps.contentKey();
        untracked(() => {
          if (this.handle === null) {
            // gridId is the definition's identity — the first value wins.
            this.handle = this.deps.store.register(gridId, contentKey);
            this.lastContentKey = contentKey;
            this.restoreState(true);
          } else if (contentKey !== this.lastContentKey) {
            this.persistContentState();
            this.handle.switchContent(contentKey);
            this.lastContentKey = contentKey;
            this.restoreState(false);
          }
        });
      },
      { injector },
    );

    // External data changes reconcile identity-keyed state. With no writer
    // bound (readonly core) every rows-array change is external; the
    // editing milestone adds the own-write suppression.
    let firstRows = true;
    effect(
      () => {
        this.rows();
        untracked(() => {
          if (firstRows) {
            firstRows = false;
            return;
          }
          this.engine.reconcile();
        });
      },
      { injector },
    );

    // Coarse selection announcements: multi-cell shapes only, no arrow spam.
    let lastSelectionKey: string | undefined;
    effect(
      () => {
        const engine = this.engine;
        const ranges = engine.selection.ranges();
        untracked(() => {
          const activeRange = ranges.length > 0 ? ranges[ranges.length - 1] : null;
          const rect = activeRange === null ? null : engine.selection.rectOf(activeRange);
          const isAll = activeRange?.kind === 'all';
          const rows = rect === null ? 0 : rect.bottom - rect.top + 1;
          const cols = rect === null ? 0 : rect.right - rect.left + 1;
          const key = activeRange === null ? 'none' : isAll ? 'all' : `${rows}x${cols}`;
          const first = lastSelectionKey === undefined;
          const changed = key !== lastSelectionKey;
          lastSelectionKey = key;
          if (first || !changed) {
            return;
          }
          if (isAll) {
            this.announcements.announce('grid.announce.selectionAll');
          } else if (rows * cols > 1) {
            this.announcements.announce('grid.announce.selection', { rows, cols });
          }
        });
      },
      { injector },
    );

    // Loading / empty transitions.
    let lastLoading: boolean | undefined;
    effect(
      () => {
        const loading = this.deps.loading();
        untracked(() => {
          if (lastLoading !== undefined && loading !== lastLoading) {
            if (loading) {
              this.announcements.announce('grid.announce.loading');
            } else {
              this.announcements.announce('grid.announce.loaded', {
                count: untracked(() => this.engine.model.dataRowCount()),
              });
            }
          }
          lastLoading = loading;
        });
      },
      { injector },
    );
    let lastEmpty: boolean | undefined;
    effect(
      () => {
        const empty = this.showEmpty();
        untracked(() => {
          if (lastEmpty !== undefined && empty && !lastEmpty) {
            this.announcements.announce('grid.empty');
          }
          lastEmpty = empty;
        });
      },
      { injector },
    );

    // Viewport tracking on the scroller.
    effect(
      (onCleanup) => {
        const scroller = this.scrollerSignal();
        if (scroller === null) {
          return;
        }
        untracked(() => this.viewportHeight.set(scroller.clientHeight));
        const observer = new ResizeObserver(() =>
          this.viewportHeight.set(scroller.clientHeight),
        );
        observer.observe(scroller);
        onCleanup(() => observer.disconnect());
      },
      { injector },
    );

    // Row height from the token, re-measured when the density changes.
    afterRenderEffect(
      () => {
        this.deps.size();
        untracked(() => {
          const raw = getComputedStyle(this.deps.host).getPropertyValue('--grid-row-height');
          const parsed = Number.parseFloat(raw);
          this.rowHeightSignal.set(
            Number.isFinite(parsed) && parsed > 0 ? parsed : DEFAULT_ROW_HEIGHT,
          );
        });
      },
      { injector },
    );

    // Roving focus: the active cell takes real focus after render whenever
    // activity changed while the grid owned focus, or a gesture asked for it.
    afterRenderEffect(
      () => {
        const active = this.engine.nav.activeCell();
        this.focusRequest();
        const escaped = this.escapedSignal();
        untracked(() => {
          if (escaped) {
            this.pendingGestureFocus = false;
            return;
          }
          const scroller = this.scrollerSignal();
          if (scroller === null || active === null) {
            this.pendingGestureFocus = false;
            return;
          }
          const shouldFocus = this.pendingGestureFocus || scroller.contains(document.activeElement);
          this.pendingGestureFocus = false;
          if (shouldFocus) {
            scroller.querySelector<HTMLElement>(cellSelector(active))?.focus({ preventScroll: true });
          }
        });
      },
      { injector },
    );

    // Reveal-after-render: keyboard moves and engine reveals scroll the
    // active cell into view once its element exists.
    afterRenderEffect(
      () => {
        this.revealRequest();
        untracked(() => this.revealActiveCell());
      },
      { injector },
    );

    // Single tab stop: interactive content projected into cells and headers
    // (record links, rich-header widgets) is pulled out of the tab order —
    // it stays reachable through cell navigation + Enter, and links keep
    // their native pointer affordances. Runs per render so recycled window
    // rows are re-stamped. Editor content is exempt (it must hold focus).
    afterRenderEffect(
      () => {
        this.renderRows();
        const scroller = this.scrollerSignal();
        if (scroller === null) {
          return;
        }
        untracked(() => {
          const interactive = scroller.querySelectorAll(
            ['[role="gridcell"]', '[role="columnheader"]', '[role="rowheader"]']
              .map((scope) => INTERACTIVE_CONTENT_SELECTOR.split(', ').map((s) => `${scope} ${s}`))
              .flat()
              .join(', '),
          );
          for (const element of interactive) {
            if (
              element.closest('[data-tm-editor]') === null &&
              element.getAttribute('tabindex') !== '-1'
            ) {
              element.setAttribute('tabindex', '-1');
            }
          }
        });
      },
      { injector },
    );

    // Persisted-scroll restore, applied once the scroller and content exist.
    afterRenderEffect(
      () => {
        this.scrollRestoreRequest();
        this.totalHeight();
        const scroller = this.scrollerSignal();
        untracked(() => {
          const pending = this.pendingScroll;
          if (scroller === null || pending === null) {
            return;
          }
          this.pendingScroll = null;
          scroller.scrollLeft = pending.x;
          scroller.scrollTop = pending.y; // the browser clamps to the extent
          this.scrollTop.set(scroller.scrollTop);
          this.scrollLeft.set(scroller.scrollLeft);
        });
      },
      { injector },
    );
  }

  private onDestroy(): void {
    this.endDrag();
    if (this.handle !== null) {
      this.persistWidths();
      this.persistContentState();
      this.handle.release();
      this.handle = null;
    }
    this.engineInstance?.dispose();
  }
}
