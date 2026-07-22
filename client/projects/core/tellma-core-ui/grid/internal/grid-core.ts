// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterNextRender,
  afterRenderEffect,
  ApplicationRef,
  computed,
  effect,
  isDevMode,
  signal,
  untracked,
  type DestroyRef,
  type Injector,
  type Signal,
  type TemplateRef,
  type ViewContainerRef,
  type WritableSignal,
} from '@angular/core';
import type { LiveAnnouncer } from '@angular/cdk/a11y';
import type { FieldTree, ValidationError } from '@angular/forms/signals';

import {
  TM_PARSE_ERROR,
  type SignalLike,
  type TmGridContentState,
  type TmGridScrollPosition,
  type TmLabelResolution,
  type TmParseContext,
  type TmParseError,
  type TmPasteContext,
  type TmRowId,
} from '@tellma/core-ui/contracts';
import {
  TM_ERROR_DISPLAY,
  tmResolveFieldErrors,
  type TmErrorDisplayPolicy,
  type TmErrorDisplayState,
  type TmUiTranslateFn,
} from '@tellma/core-ui';
import { TM_CHECKBOX_CELL_DISPLAY } from '@tellma/core-ui/checkbox';
import {
  TmGridEngine,
  tmComputeAxisWindow,
  type TmGridColumnType,
  type TmGridEditSession,
  type TmGridEngineColumn,
  type TmGridHistorySnapshot,
  type TmGridResolutionRequest,
  type TmGridTreeOptions,
  type TmRowCol,
} from '@tellma/core-ui/grid-engine';
import {
  ɵtmObserveLongPress,
  type TmMenu,
  type TmMenuEntry,
  type TmMenuItem,
} from '@tellma/core-ui/menu';

import type { TmGridColumn } from '../tm-grid-column';
import type {
  TmGridDisplayContext,
  TmGridDisplayDef,
  TmGridEditorContext,
  TmGridEditorDef,
  TmGridEmptyDef,
  TmGridHeaderContext,
  TmGridLoadingDef,
} from '../tm-grid-templates';
import type { TmGridStateHandle, TmGridStateStore } from '../tm-grid-state-store';
import { ɵTmGridAnnouncements } from './announcements';
import { ɵTmGridClipboardDom, ɵtmGridResolvePasteSource } from './clipboard-dom';
import { ɵTmGridColumnResize } from './column-resize';
import { ɵTmGridEditorSession, type ɵTmGridEditorMountConfig } from './editor-session';
import { ɵTmGridFieldWriter, ɵtmChildField, ɵtmRowField } from './field-writer';
import {
  tmResolveEditingKey,
  tmResolveGridKey,
  type TmGridEditingCommitTarget,
  type TmGridIntent,
} from './grid-keymap';
import type { ɵTmGridIconTemplates } from './icons';
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

/**
 * Context-menu shortcut hints (display only — §8.5), formatted for the
 * platform: Apple stacks glyphs (⌘⌥+), everything else joins with `+`
 * (Ctrl+Alt++). The keys mirror the keymap: cut/copy/paste are the native
 * Mod+X/C/V; row ops are the Alt-modified Mod+Alt+±.
 */
const GRID_MENU_SHORTCUTS = IS_MAC_PLATFORM
  ? { cut: '⌘X', copy: '⌘C', paste: '⌘V', insertAbove: '⌘⌥+', deleteRows: '⌘⌥−' }
  : {
      cut: 'Ctrl+X',
      copy: 'Ctrl+C',
      paste: 'Ctrl+V',
      insertAbove: 'Ctrl+Alt++',
      deleteRows: 'Ctrl+Alt+−',
    };

/** Natively-focusable content consumers may project into cells/headers. */
const INTERACTIVE_CONTENT_SELECTOR = 'a[href], button, input, select, textarea, [tabindex]';

/** Rows whose field nodes one error-tally warm-up slice touches (§16). */
const WARMUP_ROWS_PER_SLICE = 500;

/** How long the transient failure notice stays visible, in ms. */
const TRANSIENT_NOTICE_MS = 6_000;

/** Debounce between checked-set changes and the count announcement, in ms. */
const CHECKED_ANNOUNCE_DEBOUNCE_MS = 150;

/** Debounce between find-query changes and the scan (§8.7 pacing), in ms. */
const FIND_DEBOUNCE_MS = 250;

/** Cells one find-scan slice visits before yielding to the event loop (§16). */
const FIND_SCAN_CELLS_PER_SLICE = 2_000;

/** Uniquifies the per-instance error-overlay message element id. */
let nextErrorMsgId = 0;

function cellSelector(cell: TmRowCol): string {
  return `[data-tm-cell][data-row="${cell.row}"][data-col="${cell.col}"]`;
}

/**
 * The identity key of a cell for error-tally dedupe. The format mirrors the
 * engine annotation store's internal key (type-prefixed so numeric id 1 and
 * string id '1' never collide) — the two sources must dedupe to distinct
 * CELLS, so they must agree on the key.
 */
function errorCellKey(rowId: TmRowId, columnId: string): string {
  return `${typeof rowId === 'number' ? '#' : '$'}${String(rowId)} ${columnId}`;
}

/** The state a validation error's field feeds the workspace error-display policy. */
function fieldErrorState(field: {
  invalid(): boolean;
  touched(): boolean;
  dirty(): boolean;
  pending(): boolean;
}): TmErrorDisplayState {
  return {
    invalid: field.invalid(),
    touched: field.touched(),
    dirty: field.dirty(),
    pending: field.pending(),
  };
}

/** One field-validation-errored cell, by identity. */
interface FieldErrorCell {
  readonly rowId: TmRowId;
  readonly columnId: string;
}

/**
 * One find match, by cell identity — identity (not view coordinates)
 * because the match list spans the whole model, collapsed tree rows
 * included, and must survive expansion changes between scan and use.
 */
export interface ɵTmGridFindMatch {
  /** The matched row's id. */
  readonly rowId: TmRowId;
  /** The matched column's id. */
  readonly columnId: string;
}

const EMPTY_FIELD_ERRORS: ReadonlyMap<string, FieldErrorCell> = new Map();

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
  /** `aria-colindex` (1-based, after the row-header and checkbox chrome). */
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
  readonly editorDef: TmGridEditorDef<T, unknown> | undefined;
  /** `enum` columns: the raw options plus their label/value accessors. */
  readonly enumOptions: readonly unknown[] | undefined;
  readonly optionLabel: ((option: unknown) => string) | undefined;
  readonly optionValue: ((option: unknown) => unknown) | undefined;
  /** The column's batched paste-label resolver, when the consumer bound one. */
  readonly resolveLabels:
    | ((
        labels: string[],
        ctx: TmPasteContext,
      ) => Promise<ReadonlyMap<string, TmLabelResolution<unknown>>>)
    | undefined;
  /**
   * Seeds a text editor with the cell's full-precision value (number columns),
   * so a display `maxDecimals` rounding never writes back on edit. Absent when
   * the display text is already the edit text (every non-number column).
   */
  readonly editSeedText: ((value: unknown) => string) | undefined;
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
  /** Whether the cell lies inside a selection range (drives `aria-selected`). */
  readonly selected: boolean;
  /** Whether the cell paints the range fill — selected AND not a lone cell. */
  readonly fill: boolean;
  /** Whether the cell is the active cell. */
  readonly active: boolean;
  /** Boolean columns: the token-driven glyph class; `undefined` otherwise. */
  readonly glyphClass: string | undefined;
  /** Custom display template, when the column projects one. */
  readonly displayTemplate: TemplateRef<TmGridDisplayContext<unknown, unknown>> | undefined;
  /** The custom display template's context. */
  readonly displayCtx: TmGridDisplayContext<unknown, unknown> | undefined;
  /** Whether the open editor session sits on this cell (renders the outlet). */
  readonly editing: boolean;
  /** Whether the cell is in error state (invalid input or field-invalid). */
  readonly invalid: boolean;
  /** Whether the cell rejects writes while the grid is editable (readonly tint). */
  readonly readonly: boolean;
  /** Whether the cell awaits an async paste resolution (inline spinner). */
  readonly pending: boolean;
  /** Whether the cell lies inside the clipboard marquee range (a copy or a cut). */
  readonly inCutRange: boolean;
  /**
   * Which of the cut range's perimeter edges fall on this cell, as the tokens
   * `t`/`b`/`s`/`e` (block-start/-end, inline-start/-end); empty for an interior
   * cut cell. The marquee draws the dashes on exactly these edges, so a
   * multi-cell cut reads as one rectangle rather than a box per cell.
   */
  readonly cutEdges: string;
  /** Whether the cell is a find match (rendered-window highlight). */
  readonly findMatch: boolean;
  /** Whether the cell is the ACTIVE find match (outline). */
  readonly activeFindMatch: boolean;
  /** Whether the cell renders the tree affordance (trees, hierarchy column). */
  readonly hierarchy: boolean;
  /** Tree depth for the indent spacer (0 outside the hierarchy cell). */
  readonly level: number;
  /** The expander glyph state, or `null` when the row cannot expand. */
  readonly expander: 'expanded' | 'collapsed' | null;
  /** Whether the row's lazy children are loading (reserved-slot spinner). */
  readonly loadingChildren: boolean;
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
  /** Whether the row is checkbox-checked (row tint + row-level aria-selected). */
  readonly checked: boolean;
  /** Whether the row header highlights (a selection range covers the row). */
  readonly headerHit: boolean;
  /** The row-header text (1-based row number, `*` on the placeholder). */
  readonly rowHeaderText: string;
  /** `aria-level` (1-based tree depth), or `null` outside trees. */
  readonly ariaLevel: number | null;
  /** `aria-expanded`, or `null` when absent (flat grids, leaf rows). */
  readonly ariaExpanded: 'true' | 'false' | null;
  /** `aria-posinset` (1-based position among siblings), or `null`. */
  readonly ariaPosInSet: number | null;
  /** `aria-setsize` (the sibling count), or `null`. */
  readonly ariaSetSize: number | null;
  /** The row's cells, in column order. */
  readonly cells: readonly ɵTmGridCellVm[];
}

/**
 * The tree bindings a tree-grid shell contributes to the composition root.
 * Every member is a deferred read: the subclass's input signals do not
 * exist yet when the base class constructs the core, so the shell hands
 * over closures and the core reads them lazily.
 */
export interface ɵTmGridTreeConfig<T> {
  /** Reads a row's parent id; `null` marks a root. */
  readonly parentId: SignalLike<(row: T) => TmRowId | null>;
  /** The model property holding the parent id (editable-tree re-parenting). */
  readonly parentIdKey: SignalLike<string | undefined>;
  /** Marks rows whose children may not be loaded yet. */
  readonly hasChildren: SignalLike<((row: T) => boolean) | undefined>;
  /** Loads a row's children on first expand (§13.3 lazy loading). */
  readonly loadChildren: SignalLike<((row: T) => Promise<void>) | undefined>;
  /** How deep the tree starts expanded; `undefined` = fully expanded. */
  readonly defaultExpandedDepth: SignalLike<number | undefined>;
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
  /** The current tenant id (clipboard metadata + cross-tenant paste guard). */
  readonly tenantId: Signal<string | undefined>;
  /** The distribution key (metadata + guard) — tenant ids are unique only within one. */
  readonly distributionKey: string | undefined;
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
  /** The two-way checked-row set of a `selectable` grid. */
  readonly selectedIds: WritableSignal<ReadonlySet<TmRowId>>;
  /** The density variant. */
  readonly size: Signal<'sm' | 'md' | 'lg'>;
  /** Consumer context-menu items appended after the built-ins. */
  readonly extraMenuItems: Signal<readonly TmMenuItem[]>;
  /** The projected column definitions, in display order. */
  readonly columns: Signal<ReadonlyArray<TmGridColumn<T, unknown>>>;
  /** The projected empty-state template, if any. */
  readonly emptyDef: Signal<TmGridEmptyDef | undefined>;
  /** The projected loading-state template, if any. */
  readonly loadingDef: Signal<TmGridLoadingDef | undefined>;
  /** The tree bindings; absent for the flat grid. */
  readonly tree?: ɵTmGridTreeConfig<T>;
}

/**
 * The row-type-free face of {@link ɵTmGridCore} that the shared view
 * component (`ɵTmGridView`) renders from. The erasure is deliberate: the
 * view template touches only view models and DOM events, so one compiled
 * template serves `tm-grid<T>` (and, later, `tm-tree-grid<T>`) for every
 * `T` without variance friction.
 */
export interface ɵTmGridViewCore {
  /** The container role: `treegrid` when the core carries a tree config. */
  readonly gridRole: Signal<'grid' | 'treegrid'>;
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
  /** Whether the row-checkbox chrome column renders (`selectable`, readonly). */
  readonly checkboxColumn: Signal<boolean>;
  /** The select-all checkbox's tri-state over ALL data rows. */
  readonly checkAllState: Signal<'all' | 'none' | 'mixed'>;
  /** The select-all checkbox's accessible name. */
  readonly selectAllLabel: Signal<string>;
  /** The row checkboxes' accessible name. */
  readonly selectRowLabel: Signal<string>;
  /** The new-row placeholder row-header's accessible name. */
  readonly newRowLabel: Signal<string>;
  /** Whether the find bar is open. */
  readonly findOpen: Signal<boolean>;
  /** The find query as typed. */
  readonly findQuery: Signal<string>;
  /** The localized match counter ('3 of 41' / no-matches; empty pre-scan). */
  readonly findCounterText: Signal<string>;
  /** Count of matches of the completed scan. */
  readonly findMatchCount: Signal<number>;
  /** Whether the primary pointer is coarse (renders the touch handles). */
  readonly coarsePointer: Signal<boolean>;
  /** The active range's corner cells for the touch handles, or `null` while hidden. */
  readonly selectionHandles: Signal<{ readonly start: TmRowCol; readonly end: TmRowCol } | null>;
  /** The spacer height in px (`rowCount × rowHeight`). */
  readonly totalHeight: Signal<number>;
  /** The window block's translate. */
  readonly windowTransform: Signal<string>;
  /** The rows to render: the window slice plus the active-row outlier. */
  readonly renderRows: Signal<readonly ɵTmGridRowVm[]>;
  /** The column-resize pointer controller. */
  readonly resize: ɵTmGridColumnResize;
  /** Count of cells in error state (invalid inputs ∪ field-invalid cells). */
  readonly errorCount: Signal<number>;
  /** Count of cells awaiting async paste resolutions. */
  readonly pendingCount: Signal<number>;
  /** The context-menu entries (built-ins + consumer extras). */
  readonly menuItems: Signal<readonly TmMenuEntry[]>;
  /** The error-overlay message element's id (`aria-describedby` target). */
  readonly errorMsgId: string;
  /** The active errored cell's element — the error overlay's anchor. */
  readonly errorAnchor: Signal<Element | null>;
  /** The active errored cell's localized message. */
  readonly errorMessage: Signal<string>;
  /**
   * A short-lived localized failure notice (an async clipboard write
   * rejected), or `null`. The status bar renders it in editable mode; the
   * view renders a block-end overlay strip for readonly grids.
   */
  readonly transientNotice: Signal<string | null>;
  /** Jumps to the next (+1) / previous (−1) errored cell, row-major, cycling. */
  gotoError(direction: 1 | -1): void;
  /** Updates the find query (the scan is debounced and chunked). */
  setFindQuery(query: string): void;
  /** Cycles to the next (+1) / previous (−1) match and ACTIVATES its cell. */
  findStep(direction: 1 | -1): void;
  /** Closes the find bar: clears the query, focus returns to the grid. */
  closeFind(): void;
  /** Hands the core the find bar's input (or `null` when the bar closes). */
  attachFindInput(element: HTMLElement | null): void;
  /** A touch handle's pointerdown: extends the range from the dragged corner. */
  beginHandleDrag(event: PointerEvent, edge: 'start' | 'end'): void;
  /** Hands the core the scroll container once the view rendered it. */
  attachScroller(element: HTMLElement): void;
  /** Hands the core the editing cell's editor outlet (or `null` when closed). */
  attachEditorOutlet(outlet: ViewContainerRef | null): void;
  /** Hands the core the context menu and the built-in icon templates. */
  attachMenu(menu: TmMenu | null, icons: ɵTmGridIconTemplates | null): void;
  /** The scroller's scroll handler. */
  onScroll(event: Event): void;
  /** The scroller's keydown handler. */
  onKeydown(event: KeyboardEvent): void;
  /** The scroller's pointerdown handler (cells, row headers, drags). */
  onPointerDown(event: PointerEvent): void;
  /** The scroller's click handler (column headers, corner). */
  onClick(event: MouseEvent): void;
  /** The scroller's dblclick handler (opens the editor in *edit* mode). */
  onDblClick(event: MouseEvent): void;
  /** The scroller's contextmenu handler (select target, open the menu). */
  onContextMenu(event: MouseEvent): void;
  /** The scroller's focusout handler (commit-on-blur). */
  onFocusOut(event: FocusEvent): void;
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
  private readonly appRef: ApplicationRef;
  private readonly fieldWriter: ɵTmGridFieldWriter<T>;
  private readonly editorSession: ɵTmGridEditorSession;
  private engineInstance: TmGridEngine<T> | null = null;
  private handle: TmGridStateHandle | null = null;
  private lastContentKey: string | number | undefined;
  private pendingScroll: TmGridScrollPosition | null = null;
  /**
   * Tree expansion deferred until rows first arrive: the idiomatic ERP flow
   * mounts empty then fetches, so a seed/restore at mount runs over zero rows
   * — it must re-run (un-pruned) on the first non-empty rows for the content.
   */
  private treeExpansionPending:
    | { readonly kind: 'restore'; readonly ids: ReadonlySet<TmRowId> }
    | { readonly kind: 'seed' }
    | null = null;
  /** A persisted selection to re-apply once the content's rows are present. */
  private pendingSelectionRestore: NonNullable<TmGridContentState['selection']> | null = null;
  private pendingGestureFocus = false;
  /**
   * Set when a structural notice (row move/insert/delete) just spoke, to drop
   * the ONE coarse selection announcement its reselection fires right after —
   * otherwise "3 × 4 selected" clobbers "1 row moved" in the live region.
   */
  private suppressSelectionAnnounce = false;
  /** Whether the most recent real focus landed inside the grid/its overlays. */
  private gridOwnsFocus = false;
  /** When the user last pressed outside the grid (guards focus reclaim). */
  private lastOutsidePointerDown = 0;
  private dragCleanup: (() => void) | null = null;
  private menuRef: TmMenu | null = null;
  /** Generation guard for the chunked error-tally warm-up chains. */
  private warmupGeneration = 0;
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
  private readonly iconTemplates = signal<ɵTmGridIconTemplates | null>(null);
  /** Whether the deferred error-tally warm-up has completed (§16). */
  private readonly errorWarmupDone = signal(false);
  private readonly errorAnchorSignal = signal<Element | null>(null);
  /** Whether an async clipboard read was denied (degrades menu Paste, §8.5). */
  private readonly pasteReadDenied = signal(false);
  private readonly transientNoticeSignal = signal<string | null>(null);
  private transientNoticeTimer: ReturnType<typeof setTimeout> | null = null;
  /** Rows whose lazy children are loading (reserved-slot spinner, §13.3). */
  private readonly loadingChildrenIds = signal<ReadonlySet<TmRowId>>(new Set());
  /**
   * Rows the user wants expanded once their lazy load lands. A collapse
   * during the load clears the row's entry — the load continues, but on
   * resolve the node stays collapsed and the children simply render on
   * the next expand (§13.3).
   */
  private readonly wantedExpansionIds = signal<ReadonlySet<TmRowId>>(new Set());
  /** Kills in-flight lazy-child-load completions across a content switch/destroy. */
  private lazyLoadGeneration = 0;
  /** Guards stray async completions after destroy. */
  private destroyed = false;
  /** The last individually toggled checkbox row — the Shift+click anchor. */
  private lastToggledRowId: TmRowId | null = null;
  /** Debounces the checked-count announcement across toggle bursts. */
  private checkedAnnounceTimer: ReturnType<typeof setTimeout> | null = null;
  /** Kills superseded find-scan chains (re-query, rows change, destroy). */
  private findGeneration = 0;
  /** Debounces query keystrokes into one scan. */
  private findDebounceTimer: ReturnType<typeof setTimeout> | null = null;
  /** The find bar's input, while the bar is open (Mod+F re-focus target). */
  private findInput: HTMLElement | null = null;

  private readonly findOpenSignal = signal(false);
  private readonly findQuerySignal = signal('');
  private readonly findMatchesSignal = signal<readonly ɵTmGridFindMatch[]>([]);
  private readonly findActiveIndexSignal = signal(-1);
  /** The query the current match list answers (counter gates on it). */
  private readonly findResultsFor = signal<string | null>(null);
  private readonly coarsePointerSignal = signal(false);
  /** The workspace's error-display policy (touched/dirty/pending gating). */
  private readonly errorDisplay: TmErrorDisplayPolicy;
  /** Fast match membership for the rendered window's highlight flags. */
  private readonly findMatchKeys: Signal<ReadonlySet<string>>;
  /** The active match, or `null`. */
  private readonly activeFindMatchSignal: Signal<ɵTmGridFindMatch | null>;

  private readonly columnsInternal: Signal<readonly ColumnInternal<T>[]>;
  private readonly engineColumns: Signal<ReadonlyArray<TmGridEngineColumn<T>>>;
  private readonly window: Signal<ReturnType<typeof tmComputeAxisWindow>>;
  /** The data-column index rendering the hierarchy (trees; -1 otherwise). */
  private readonly hierarchyColIndex: Signal<number>;
  /** Per visible row: 1-based position among its siblings + sibling count. */
  private readonly treeAria: Signal<
    ReadonlyMap<TmRowId, { readonly pos: number; readonly size: number }>
  >;
  /** Field-validation-errored cells keyed for dedupe with invalid inputs. */
  private readonly fieldErrorCells: Signal<ReadonlyMap<string, FieldErrorCell>>;
  /** Every errored cell by identity (collapsed ones included), the jump order. */
  private readonly errorCellRefs: Signal<readonly FieldErrorCell[]>;
  /** The active cell's raw field errors (feeds the localized overlay message). */
  private readonly activeCellFieldErrors: Signal<
    readonly ValidationError.WithOptionalFieldTree[]
  >;
  private readonly activeCellResolvedErrors: ReturnType<typeof tmResolveFieldErrors>;

  /** The bound rows: `data`, else the field's value, else empty. */
  readonly rows: Signal<readonly T[]>;
  /** The container role: `treegrid` when the core carries a tree config. */
  readonly gridRole: Signal<'grid' | 'treegrid'>;
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
  /** Whether the row-checkbox chrome column renders (`selectable`, readonly). */
  readonly checkboxColumn: Signal<boolean>;
  /** The select-all checkbox's tri-state over ALL data rows. */
  readonly checkAllState: Signal<'all' | 'none' | 'mixed'>;
  /** The select-all checkbox's accessible name. */
  readonly selectAllLabel: Signal<string>;
  /** The row checkboxes' accessible name. */
  readonly selectRowLabel: Signal<string>;
  /** The new-row placeholder row-header's accessible name. */
  readonly newRowLabel: Signal<string>;
  /** Whether the find bar is open. */
  readonly findOpen: Signal<boolean>;
  /** The find query as typed. */
  readonly findQuery: Signal<string>;
  /** The localized match counter. */
  readonly findCounterText: Signal<string>;
  /** Count of matches of the completed scan. */
  readonly findMatchCount: Signal<number>;
  /** Whether the primary pointer is coarse. */
  readonly coarsePointer: Signal<boolean>;
  /** The active range's corner cells for the touch handles, or `null`. */
  readonly selectionHandles: Signal<{ readonly start: TmRowCol; readonly end: TmRowCol } | null>;
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
  /** Count of cells in error state (invalid inputs ∪ field-invalid cells, distinct). */
  readonly errorCount: Signal<number>;
  /** Count of cells awaiting async paste resolutions. */
  readonly pendingCount: Signal<number>;
  /** The column-resize pointer controller. */
  readonly resize: ɵTmGridColumnResize;
  /** The context-menu entries (built-ins + consumer extras). */
  readonly menuItems: Signal<readonly TmMenuEntry[]>;
  /** The error-overlay message element's id (`aria-describedby` target). */
  readonly errorMsgId = `tm-grid-errmsg-${nextErrorMsgId++}`;
  /** The active errored cell's element — the error overlay's anchor. */
  readonly errorAnchor: Signal<Element | null>;
  /** The active errored cell's localized message. */
  readonly errorMessage: Signal<string>;
  /** A short-lived localized failure notice, or `null`. */
  readonly transientNotice: Signal<string | null>;

  constructor(deps: ɵTmGridCoreDeps<T>) {
    this.deps = deps;
    this.announcements = new ɵTmGridAnnouncements(deps.announcer, deps.translate);
    this.appRef = deps.injector.get(ApplicationRef);
    this.errorDisplay = deps.injector.get(TM_ERROR_DISPLAY);
    this.editorSession = new ɵTmGridEditorSession(deps.injector);
    this.fieldWriter = new ɵTmGridFieldWriter<T>({
      field: () => this.deps.field(),
      newRow: () => this.deps.newRow(),
      rowId: (row) => this.deps.rowId()(row),
      modelIndexOfRow: (rowId) => this.engine.model.modelIndexOfRow(rowId),
      rowById: (rowId) => this.engine.model.rowById(rowId),
    });
    this.clipboardDom = new ɵTmGridClipboardDom({
      copy: (opts) => this.engine.clipboard.copy(opts),
      cut: (fingerprint) => this.engine.clipboard.cut(fingerprint),
      announcements: this.announcements,
      // A failed copy is never silent (§9.1): the live region announced it;
      // this surfaces the visible transient notice alongside.
      onCopyFailed: () => this.showTransientNotice('grid.announce.copyFailed'),
    });
    this.transientNotice = this.transientNoticeSignal.asReadonly();
    this.resize = new ɵTmGridColumnResize({
      widthOverrides: this.widthOverrides,
      direction: deps.direction,
      // A thunk: `columnsInternal` is assigned further down the constructor,
      // and the controller only reads it at drag time.
      columns: () => untracked(this.columnsInternal),
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
    // Row checkbox selection is a readonly-grid affordance (§8.8); the
    // shell throws in dev mode when `selectable` meets an editable grid,
    // and the chrome column stays off in prod builds.
    this.checkboxColumn = computed(() => deps.selectable() && !this.editable());
    this.gridRole = computed(() => (this.engine.model.isTree ? 'treegrid' : 'grid'));
    this.loading = deps.loading;
    this.emptyDef = deps.emptyDef;
    this.loadingDef = deps.loadingDef;
    this.loadingText = deps.translate('grid.loading');
    this.emptyText = deps.translate('grid.empty');
    this.rowHeight = this.rowHeightSignal.asReadonly();
    this.escaped = this.escapedSignal.asReadonly();

    this.columnsInternal = computed(() => {
      // Chrome columns preceding the data columns in `aria-colindex` space:
      // the row header, plus the checkbox column when it renders.
      const chromeCols = this.checkboxColumn() ? 2 : 1;
      return deps.columns().map((dir, index) => this.buildColumn(dir, index, chromeCols));
    });
    this.columnModel = this.columnsInternal;
    this.engineColumns = computed(() =>
      this.columnsInternal().map((column) => column.engineColumn),
    );
    this.hierarchyColIndex = computed(() => {
      if (!this.engine.model.isTree) {
        return -1;
      }
      const marked = deps.columns().findIndex((column) => column.hierarchy());
      return marked === -1 ? 0 : marked;
    });
    // aria-posinset/-setsize count SIBLINGS. Every visible row's siblings
    // are visible too (the shared parent is expanded, or all are roots), so
    // one pass over the visible sequence grouped by parent id is exact.
    this.treeAria = computed(() => {
      const result = new Map<TmRowId, { pos: number; size: number }>();
      if (!this.engine.model.isTree) {
        return result;
      }
      const views = this.engine.model.viewRows();
      const sizeByParent = new Map<TmRowId | null, number>();
      const posById = new Map<TmRowId, number>();
      for (const view of views) {
        const pos = (sizeByParent.get(view.parentId) ?? 0) + 1;
        sizeByParent.set(view.parentId, pos);
        posById.set(view.id, pos);
      }
      for (const view of views) {
        result.set(view.id, { pos: posById.get(view.id)!, size: sizeByParent.get(view.parentId)! });
      }
      return result;
    });

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
    this.ariaColCount = computed(
      () => this.engine.model.columnCount() + (this.checkboxColumn() ? 2 : 1),
    );
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
      if (this.checkboxColumn()) {
        tracks.push('var(--grid-check-col-width)');
      }
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
    // ---- row checkbox selection (§8.8) ----
    this.selectAllLabel = deps.translate('grid.selectAll');
    this.selectRowLabel = deps.translate('grid.selectRow');
    this.newRowLabel = deps.translate('grid.newRow');
    // The tri-state ranges over ALL data rows (collapsed tree rows
    // included): select-all is all↔none over the DATA, not the viewport.
    this.checkAllState = computed(() => {
      const selected = deps.selectedIds();
      const rows = this.rows();
      if (rows.length === 0 || selected.size === 0) {
        return 'none';
      }
      const rowIdOf = deps.rowId();
      let checked = 0;
      for (const row of rows) {
        if (selected.has(rowIdOf(row))) {
          checked += 1;
        }
      }
      return checked === 0 ? 'none' : checked === rows.length ? 'all' : 'mixed';
    });

    // ---- find (§8.7) ----
    this.findOpen = this.findOpenSignal.asReadonly();
    this.findQuery = this.findQuerySignal.asReadonly();
    this.findMatchCount = computed(() => this.findMatchesSignal().length);
    this.findMatchKeys = computed(() => {
      const keys = new Set<string>();
      for (const match of this.findMatchesSignal()) {
        keys.add(errorCellKey(match.rowId, match.columnId));
      }
      return keys;
    });
    this.activeFindMatchSignal = computed(
      () => this.findMatchesSignal()[this.findActiveIndexSignal()] ?? null,
    );
    this.findCounterText = computed(() => {
      const query = this.findQuerySignal();
      if (!this.findOpenSignal() || query === '' || this.findResultsFor() !== query) {
        return ''; // no counter before the first completed scan of this query
      }
      const count = this.findMatchesSignal().length;
      if (count === 0) {
        return deps.translate('grid.find.noMatches')();
      }
      return deps.translate('grid.find.counter', {
        index: this.findActiveIndexSignal() + 1,
        count,
      })();
    });

    // ---- touch selection handles (§8.6) ----
    this.coarsePointer = this.coarsePointerSignal.asReadonly();
    if (typeof matchMedia === 'function') {
      const coarseQuery = matchMedia('(pointer: coarse)');
      this.coarsePointerSignal.set(coarseQuery.matches);
      const onCoarseChange = (event: MediaQueryListEvent): void =>
        this.coarsePointerSignal.set(event.matches);
      coarseQuery.addEventListener('change', onCoarseChange);
      deps.destroyRef.onDestroy(() => coarseQuery.removeEventListener('change', onCoarseChange));
    }
    this.selectionHandles = computed(() => {
      if (!this.coarsePointerSignal()) {
        return null;
      }
      const engine = this.engine;
      if (engine.edit.session() !== null) {
        return null; // handles hide while an editor is open
      }
      const active = engine.selection.activeRange();
      if (active === null) {
        return null;
      }
      engine.model.viewRows(); // row/column extents feed the rect
      const rect = engine.selection.rectOf(active);
      return {
        start: { row: rect.top, col: rect.left },
        end: { row: rect.bottom, col: rect.right },
      };
    });

    // ---- error tally (§10) ----
    // Field-invalid cells come from per-row errorSummary reads; the first
    // read is deferred behind the chunked warm-up (§16, wired in
    // setupEffects) so mounting a large editable grid never pays the whole
    // field-tree materialization in one frame.
    this.fieldErrorCells = computed(() => {
      if (!this.errorWarmupDone()) {
        return EMPTY_FIELD_ERRORS;
      }
      const tree = this.deps.field();
      if (tree === undefined) {
        return EMPTY_FIELD_ERRORS;
      }
      const rows = this.rows();
      const columns = this.columnsInternal();
      if (columns.length === 0 || rows.length === 0) {
        return EMPTY_FIELD_ERRORS;
      }
      const rowIdOf = this.deps.rowId();
      const map = new Map<string, FieldErrorCell>();
      for (let i = 0; i < rows.length; i++) {
        const rowField = ɵtmRowField(tree, i);
        if (rowField === undefined) {
          continue;
        }
        const summary = rowField().errorSummary();
        if (summary.length === 0) {
          continue;
        }
        const rowId = rowIdOf(rows[i]);
        for (const error of summary) {
          // The workspace's error-display policy decides when an error shows
          // (the default holds it until the field is touched or dirty), so a
          // freshly materialized row stays quiet until its cells are edited.
          const field = error.fieldTree();
          if (!this.errorDisplay(fieldErrorState(field))) {
            continue;
          }
          const column = this.attributeErrorColumn(error, columns);
          // A readonly cell is never an error (can't be fixed in place).
          if (!this.errorCellEditable(rowId, column.id)) {
            continue;
          }
          map.set(errorCellKey(rowId, column.id), { rowId, columnId: column.id });
        }
      }
      return map;
    });
    this.errorCount = computed(() => {
      const fieldErrors = this.fieldErrorCells();
      let count = fieldErrors.size;
      for (const ref of this.engine.annotations.invalidCells()) {
        if (
          this.errorCellEditable(ref.rowId, ref.columnId) &&
          !fieldErrors.has(errorCellKey(ref.rowId, ref.columnId))
        ) {
          count += 1;
        }
      }
      return count;
    });
    this.pendingCount = computed(() => this.engine.annotations.pendingCount());
    this.errorCellRefs = computed(() => {
      const engine = this.engine;
      const seen = new Set<string>();
      const refs: FieldErrorCell[] = [];
      const push = (rowId: TmRowId, columnId: string): void => {
        const key = errorCellKey(rowId, columnId);
        if (seen.has(key)) {
          return;
        }
        seen.add(key);
        refs.push({ rowId, columnId });
      };
      for (const ref of engine.annotations.invalidCells()) {
        if (this.errorCellEditable(ref.rowId, ref.columnId)) {
          push(ref.rowId, ref.columnId);
        }
      }
      for (const cell of this.fieldErrorCells().values()) {
        push(cell.rowId, cell.columnId);
      }
      // A stable order over ALL errored cells — collapsed-subtree rows
      // included, so the jump can reach them (the chip's count already does)
      // — by row model order, then column index.
      return refs.sort((a, b) => {
        const rowDelta =
          engine.model.modelIndexOfRow(a.rowId) - engine.model.modelIndexOfRow(b.rowId);
        return rowDelta !== 0
          ? rowDelta
          : engine.model.columnIndexOf(a.columnId) - engine.model.columnIndexOf(b.columnId);
      });
    });

    // ---- active-cell error overlay ----
    this.errorAnchor = this.errorAnchorSignal.asReadonly();
    this.activeCellFieldErrors = computed(() => {
      const tree = this.deps.field();
      const active = this.engine.nav.activeCell();
      if (tree === undefined || active === null || !this.editable()) {
        return [];
      }
      const view = this.engine.model.rowAt(active.row);
      const column = this.columnsInternal()[active.col];
      if (view === null || column === undefined) {
        return [];
      }
      const rowField = ɵtmRowField(tree, view.modelIndex);
      if (rowField === undefined) {
        return [];
      }
      const columns = this.columnsInternal();
      // The row's aggregated errors, narrowed to the ones this milestone
      // attributes to the active cell (same attribution as the tally) — and
      // only touched fields, so a fresh new row's active cell stays quiet
      // until edited (matches the tally's touched gate).
      return rowField()
        .errorSummary()
        .filter((error) => {
          const field = error.fieldTree();
          return (
            this.errorDisplay(fieldErrorState(field)) &&
            this.attributeErrorColumn(error, columns).id === column.id
          );
        });
    });
    this.activeCellResolvedErrors = tmResolveFieldErrors(
      this.activeCellFieldErrors,
      deps.translate,
    );
    this.errorMessage = computed(() => {
      const active = this.engine.nav.activeCell();
      // A readonly active cell is never errored: no popover, whatever stale
      // annotation or validator the underlying field may still hold.
      if (active === null || !this.editable() || !this.engine.model.isCellEditable(active)) {
        return '';
      }
      const view = this.engine.model.rowAt(active.row);
      const column = this.columnsInternal()[active.col];
      if (view === null || column === undefined) {
        return '';
      }
      const invalid = this.engine.annotations.invalidInput(view.id, column.id);
      if (invalid !== undefined) {
        const header = column.header();
        switch (invalid.reason) {
          case 'parse':
            return deps.translate('grid.cellErrors.invalidInput', {
              text: invalid.rawText,
              column: header,
            })();
          case 'notFound':
            return deps.translate('grid.cellErrors.notFound', {
              collection: header,
              label: invalid.rawText,
            })();
          case 'ambiguous':
            return deps.translate('grid.cellErrors.ambiguous', {
              collection: header,
              label: invalid.rawText,
            })();
          case 'resolutionFailed':
            return deps.translate('grid.cellErrors.resolutionFailed', {
              collection: header,
              label: invalid.rawText,
            })();
        }
      }
      return this.activeCellResolvedErrors()[0]?.message ?? '';
    });

    // ---- context menu (§8.5) ----
    this.menuItems = computed(() => this.buildMenuItems());

    this.setupEffects();

    // Focus-drop bookkeeping: when the repeater MOVES the focused row's DOM
    // node (outlier ↔ window transitions during a scroll), the browser drops
    // focus to <body> — the roving-focus effect reclaims it, but only when the
    // grid genuinely owned focus and the user didn't just press outside.
    //
    // A DELIBERATE click-away to non-focusable page content also drops focus to
    // <body>; the giveaway is the outside POINTERDOWN that precedes it (a
    // DOM-move drop has none). So the press outside is where ownership is
    // released — not a focusout, which also fires on the transient DOM-move
    // blur and would wrongly cancel the reclaim (arrows would then scroll the
    // page instead of moving the active cell). Document-level captures so
    // nothing inside the page can hide the signal.
    const onDocumentPointerDown = (event: Event): void => {
      if (event.target instanceof Node && !deps.host.contains(event.target)) {
        this.lastOutsidePointerDown = Date.now();
        this.gridOwnsFocus = false; // the user is interacting outside the grid
      }
    };
    const onDocumentFocusIn = (event: Event): void => {
      const target = event.target;
      this.gridOwnsFocus =
        target instanceof Element &&
        (deps.host.contains(target) || target.closest('.cdk-overlay-container') !== null);
    };
    document.addEventListener('pointerdown', onDocumentPointerDown, true);
    document.addEventListener('focusin', onDocumentFocusIn, true);
    deps.destroyRef.onDestroy(() => {
      document.removeEventListener('pointerdown', onDocumentPointerDown, true);
      document.removeEventListener('focusin', onDocumentFocusIn, true);
      this.onDestroy();
    });
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

  /** Hands the core the editing cell's editor outlet (or `null` when closed). */
  attachEditorOutlet(outlet: ViewContainerRef | null): void {
    this.editorSession.attachOutlet(outlet);
  }

  /** Hands the core the context menu and the built-in icon templates. */
  attachMenu(menu: TmMenu | null, icons: ɵTmGridIconTemplates | null): void {
    this.menuRef = menu;
    if (untracked(this.iconTemplates) !== icons) {
      this.iconTemplates.set(icons);
    }
  }

  /**
   * Jumps to the next (+1) / previous (−1) errored cell in row-major order,
   * cycling in either direction — the status-bar tally's navigation. An
   * open editor commits first (the jump is a navigation gesture).
   */
  gotoError(direction: 1 | -1): void {
    const engine = this.engine;
    if (untracked(() => engine.edit.session()) !== null) {
      this.commitEditor({ refocus: false });
    }
    const refs = untracked(this.errorCellRefs);
    if (refs.length === 0) {
      return;
    }
    const active = untracked(() => engine.nav.activeCell());
    const index = this.nextErrorIndex(refs, active, direction);
    const target = refs[index];
    // Reveal a collapsed-subtree error before landing on it (mirrors find),
    // so the arrows reach every cell the count includes.
    if (engine.model.isTree) {
      engine.expandAncestorsOf(target.rowId);
    }
    const row = engine.model.viewIndexOfRow(target.rowId);
    const col = engine.model.columnIndexOf(target.columnId);
    if (row === -1 || col === -1) {
      return;
    }
    engine.nav.setActive({ row, col });
    engine.selection.collapseTo({ row, col });
    this.requestReveal();
    this.announcements.announce('grid.announce.errorJump', {
      index: index + 1,
      count: refs.length,
    });
  }

  /**
   * The next (+1) / previous (−1) errored cell's index in {@link errorCellRefs},
   * cycling, relative to the active cell's position in the same row-model /
   * column-index order the refs are sorted by.
   */
  private nextErrorIndex(
    refs: readonly FieldErrorCell[],
    active: TmRowCol | null,
    direction: 1 | -1,
  ): number {
    if (active === null) {
      return direction === 1 ? 0 : refs.length - 1;
    }
    const engine = this.engine;
    const activeView = engine.model.rowAt(active.row);
    const activeRowOrder = activeView === null ? Number.POSITIVE_INFINITY : activeView.modelIndex;
    const after = (ref: FieldErrorCell): boolean => {
      const rowOrder = engine.model.modelIndexOfRow(ref.rowId);
      return (
        rowOrder > activeRowOrder ||
        (rowOrder === activeRowOrder && engine.model.columnIndexOf(ref.columnId) > active.col)
      );
    };
    const before = (ref: FieldErrorCell): boolean => {
      const rowOrder = engine.model.modelIndexOfRow(ref.rowId);
      return (
        rowOrder < activeRowOrder ||
        (rowOrder === activeRowOrder && engine.model.columnIndexOf(ref.columnId) < active.col)
      );
    };
    if (direction === 1) {
      const found = refs.findIndex(after);
      return found === -1 ? 0 : found;
    }
    const reversed = [...refs].reverse().findIndex(before);
    return reversed === -1 ? refs.length - 1 : refs.length - 1 - reversed;
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
    const engine = this.engine;
    const session = untracked(() => engine.edit.session());
    // `keyCode` is deprecated as a key IDENTIFIER (layout-tied), but 229 is the
    // IME-composition status sentinel, not a key lookup — and the most reliable
    // first-keydown IME signal: `isComposing` is false on the initiating
    // keydown, and `key === 'Process'` is unset in WebKit. Read it through a
    // non-deprecated view so the sentinel read doesn't trip the deprecation hint.
    const imeKeyCode = (event as { readonly keyCode: number }).keyCode;
    if (event.isComposing || imeKeyCode === 229) {
      // IME composition (§8.4): the very first composing keydown opens an
      // UNSEEDED editor and moves focus into its input synchronously, so
      // the whole composition session — and its commit — happens inside
      // the editor, never against the non-editable cell. Never
      // preventDefault: the composition must proceed untouched.
      if (session === null) {
        const active = untracked(() => engine.nav.activeCell());
        if (
          active !== null &&
          untracked(this.editable) &&
          untracked(this.columnsInternal)[active.col]?.type !== 'boolean' &&
          untracked(() => engine.model.isCellEditable(active))
        ) {
          this.openEditor(active, 'edit', undefined, { ime: true });
        }
      }
      return;
    }
    if (session !== null) {
      this.onEditingKeydown(event, session);
      return;
    }
    const active = untracked(() => engine.nav.activeCell());
    const activeIsBoolean =
      active !== null && untracked(this.columnsInternal)[active.col]?.type === 'boolean';
    const intent = tmResolveGridKey(event, {
      isMac: IS_MAC_PLATFORM,
      editable: untracked(this.editable),
      searchable: untracked(this.deps.searchable),
      selectable: untracked(this.checkboxColumn),
      isTree: engine.model.isTree,
      activeIsBoolean,
    });
    // After Esc parked focus on the container, Tab/Shift+Tab must leave the
    // grid natively — the engineered mid-grid exit. On an editable grid the
    // keymap still resolves Tab (it means commit-and-move mid-edit), so guard
    // it here rather than re-entering the grid at the next editable cell.
    if (intent?.kind === 'tab' && untracked(this.escapedSignal)) {
      return;
    }
    if (intent !== null && this.executeIntent(intent)) {
      event.preventDefault();
    }
  }

  /**
   * The keydown branch while an editor session is open. The handler runs
   * at the scroller (after the editor's own listeners), so it acts only on
   * keys the editor did not `preventDefault` — this is how the two-stage
   * Esc composes: the select's aria layer consumes Esc №1 closing its
   * panel; Esc №2 reaches here and cancels the session.
   */
  private onEditingKeydown(event: KeyboardEvent, session: TmGridEditSession): void {
    // Any editing keystroke on a scrolled-away cell scrolls it back first (§4).
    this.revealActiveCell();
    if (event.defaultPrevented) {
      return;
    }
    // Mod+F opens the grid find even from inside an editor — the editor input
    // must not let the browser's find dialog shadow it: commit, then open.
    const mod = IS_MAC_PLATFORM ? event.metaKey : event.ctrlKey;
    if (mod && !event.altKey && event.key.toLowerCase() === 'f' && untracked(this.deps.searchable)) {
      event.preventDefault();
      this.commitEditor({ refocus: false });
      this.openFind();
      return;
    }
    const mounted = this.editorSession.current();
    const resolved = tmResolveEditingKey(event, {
      mode: session.mode,
      isDropdownOpen: mounted?.isDropdownOpen() ?? false,
    });
    if (resolved === null) {
      return;
    }
    switch (resolved.kind) {
      case 'cancel':
        this.cancelEditor({ refocus: true });
        event.preventDefault();
        return;
      case 'toggleMode':
        this.engine.edit.toggleMode();
        event.preventDefault();
        return;
      case 'openDropdown':
        if (mounted !== null && mounted.kind === 'enum') {
          mounted.openDropdown();
          event.preventDefault();
        }
        return;
      case 'commitMove':
        this.commitAndMove(resolved.target, event);
        return;
    }
  }

  /** Commits the session, then executes the key's move. */
  private commitAndMove(target: TmGridEditingCommitTarget, event: KeyboardEvent): void {
    const engine = this.engine;
    this.commitEditor({ refocus: true });
    switch (target) {
      case 'tabNext':
      case 'tabPrev': {
        const next = engine.nav.tab(target === 'tabPrev');
        if (next === null || next === 'exit') {
          // Committed, but the key is not consumed: the browser's own Tab
          // from the re-focused cell exits the grid (§8.2).
          return;
        }
        // Selection moves only — no editor opens on the target (Excel).
        engine.nav.setActive(next, { keepTabRun: true });
        engine.selection.collapseTo(next);
        break;
      }
      case 'enterRun':
      case 'enterRunBack': {
        const next = engine.nav.enterTarget(target === 'enterRunBack');
        if (next !== null) {
          engine.nav.setActive(next);
          engine.selection.collapseTo(next);
        }
        break;
      }
      default:
        engine.moveActive(target);
        break;
    }
    event.preventDefault();
    this.requestReveal();
    this.requestFocusActive();
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
    if (target.closest('[data-tm-handle]') !== null) {
      return; // the touch handle owns its gesture (beginHandleDrag)
    }
    if (target.closest('[data-tm-editor]') !== null) {
      return; // presses inside the open editor keep their native semantics
    }
    if (target.closest('[data-tm-checkcell], [data-tm-checkhdr]') !== null) {
      // Checkbox chrome (§8.8) toggles on CLICK (a pan never synthesizes
      // one). Mouse presses preventDefault so focus stays where it is;
      // touch keeps the event native so panning stays possible.
      if (event.pointerType !== 'touch') {
        event.preventDefault();
      }
      return;
    }
    if (event.pointerType === 'touch') {
      // §8.6: native pan must win on touch — no preventDefault, no pointer
      // capture, no drag-selection. The tap's synthesized click activates
      // the cell (onClick), and a double-tap's dblclick opens the editor
      // through the shared handler. Range selection rides the handles.
      return;
    }
    if (untracked(() => this.engine.edit.session()) !== null) {
      // A press on the scroller's own scrollbar gutter keeps the editor open
      // (dragging the scrollbar is not a click-away commit); every other
      // press commits first, then activation follows the press.
      const scroller = untracked(this.scrollerSignal);
      const onScrollbar =
        scroller !== null &&
        target === scroller &&
        ((untracked(this.deps.direction) === 'rtl'
          ? event.offsetX < scroller.offsetWidth - scroller.clientWidth
          : event.offsetX >= scroller.clientWidth) ||
          event.offsetY >= scroller.clientHeight);
      if (!onScrollbar) {
        this.commitEditor({ refocus: false });
      }
    }
    const mod = IS_MAC_PLATFORM ? event.metaKey : event.ctrlKey;
    const cellElement = target.closest('[data-tm-cell]');
    if (cellElement !== null) {
      const cell = this.cellFromElement(cellElement);
      if (cell === null) {
        return;
      }
      // The tree expander: activate its cell WITH the roving focus (so
      // Alt+Arrows work right after a twisty press) and suppress the
      // button's own focus; the click handler performs the toggle. No
      // drag — a twisty press is never a range-selection gesture.
      if (target.closest('[data-tm-expander]') !== null) {
        event.preventDefault();
        this.escapedSignal.set(false);
        this.engine.clickCell(cell);
        this.requestFocusActive();
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
      // A boolean cell toggles on a plain click of the GLYPH itself (§6.2) —
      // activation above already committed any editor. A press on the empty
      // cell space around the glyph is not a toggle: it starts a range drag
      // like any other cell, so a selection can begin from a boolean column.
      if (
        !event.shiftKey &&
        !mod &&
        target.closest('.tm-grid-bool') !== null &&
        untracked(this.editable) &&
        untracked(this.columnsInternal)[cell.col]?.type === 'boolean'
      ) {
        this.engine.edit.toggleBoolean(cell);
        return;
      }
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
      return;
    }
    // Column headers mirror row headers (§6.4): press-select the column,
    // shift-extend the column span, and drag across headers to select a range
    // of columns. Interactive projected header content keeps its own click.
    const colHeader = target.closest('[data-tm-colhdr]');
    if (colHeader !== null) {
      const interactive = target.closest(INTERACTIVE_CONTENT_SELECTOR);
      if (interactive !== null && colHeader.contains(interactive)) {
        return;
      }
      const col = Number(colHeader.getAttribute('data-col'));
      if (!Number.isInteger(col)) {
        return;
      }
      event.preventDefault();
      this.escapedSignal.set(false);
      if (event.shiftKey) {
        this.engine.selection.extendActiveTo({ row: 0, col });
      } else {
        if (untracked(() => this.engine.model.viewRowCount()) > 0) {
          this.engine.nav.setActive({ row: 0, col });
        }
        this.engine.selection.selectCols(col, col, mod);
        this.requestFocusActive();
      }
      this.beginDrag(event, 'cols');
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
    const expander = target.closest('[data-tm-expander]');
    if (expander !== null) {
      // Pointer path of expand/collapse (§13.3). The pointerdown already
      // activated the cell through the interactive-content branch; the
      // toggle folds in a pending wanted-expansion so a click during a
      // lazy load re-collapses instead of re-requesting.
      const cellElement = expander.closest('[data-tm-cell]');
      const cell = cellElement === null ? null : this.cellFromElement(cellElement);
      const view = cell === null ? null : untracked(() => this.engine.model.rowAt(cell.row));
      if (view !== null && view.expandable) {
        const wanting = untracked(this.wantedExpansionIds).has(view.id);
        this.setRowExpanded(view.id, !(view.expanded || wanting));
      }
      return;
    }
    if (target.closest('[data-tm-corner]') !== null) {
      this.engine.selection.selectAll();
      return;
    }
    // Row-checkbox chrome (§8.8): the select-all header and the row cells
    // toggle on click — pointerdown already pinned focus for mouse presses,
    // and a touch pan never synthesizes a click.
    if (target.closest('[data-tm-checkhdr]') !== null) {
      this.toggleSelectAllCheckbox();
      return;
    }
    const checkCell = target.closest('[data-tm-checkcell]');
    if (checkCell !== null) {
      const row = Number(checkCell.getAttribute('data-row'));
      if (Number.isInteger(row)) {
        this.toggleRowCheckAt(row, event.shiftKey);
      }
      return;
    }
    // Column headers select on POINTERDOWN now (mouse drag + shift-extend, see
    // onPointerDown) — a touch tap still routes through onTouchTap below.
    // Touch taps (§8.6): the pointerdown deliberately did nothing (native
    // pan), so the synthesized click is where a tap activates its target.
    if (event instanceof PointerEvent && event.pointerType === 'touch') {
      this.onTouchTap(target);
    }
  }

  /** A touch tap on a cell or row header: activation without a drag. */
  private onTouchTap(target: Element): void {
    if (target.closest('[data-tm-editor]') !== null) {
      return; // taps inside the open editor keep their native semantics
    }
    const cellElement = target.closest('[data-tm-cell]');
    if (cellElement !== null) {
      const cell = this.cellFromElement(cellElement);
      if (cell === null) {
        return;
      }
      if (untracked(() => this.engine.edit.session()) !== null) {
        // Tapping another cell commits the open editor first (§8.4).
        this.commitEditor({ refocus: false });
      }
      this.escapedSignal.set(false);
      this.engine.clickCell(cell);
      this.requestFocusActive();
      return;
    }
    const rowHeader = target.closest('[data-tm-rowhdr]');
    if (rowHeader !== null) {
      const row = Number(rowHeader.getAttribute('data-row'));
      if (!Number.isInteger(row)) {
        return;
      }
      this.escapedSignal.set(false);
      this.engine.nav.setActive({ row, col: 0 });
      this.engine.selection.selectRows(row, row, false);
      this.requestFocusActive();
      return;
    }
    const colHeader = target.closest('[data-tm-colhdr]');
    if (colHeader !== null) {
      const interactive = target.closest(INTERACTIVE_CONTENT_SELECTOR);
      if (interactive !== null && colHeader.contains(interactive)) {
        return;
      }
      const col = Number(colHeader.getAttribute('data-col'));
      if (!Number.isInteger(col)) {
        return;
      }
      this.escapedSignal.set(false);
      if (untracked(() => this.engine.model.viewRowCount()) > 0) {
        this.engine.nav.setActive({ row: 0, col });
      }
      this.engine.selection.selectCols(col, col, false);
      this.requestFocusActive();
    }
  }

  /** The scroller's dblclick handler: opens the editor in *edit* mode (§8.3). */
  onDblClick(event: MouseEvent): void {
    if (!untracked(this.editable)) {
      return;
    }
    // The pointerdown capture retargets the click pair at the scroller
    // (pointerup's target becomes the capture element, and dblclick's
    // target is the pair's common ancestor) — the pressed CELL must be
    // resolved from the pointer position, not from `event.target`.
    const hit = document.elementFromPoint(event.clientX, event.clientY);
    if (hit === null) {
      return;
    }
    if (hit.closest('[data-tm-editor]') !== null) {
      return; // double-clicks inside the editor select words natively
    }
    if (hit.closest('[data-tm-expander]') !== null) {
      return; // rapid expander toggling must never open an editor
    }
    const cellElement = hit.closest('[data-tm-cell]');
    if (cellElement === null) {
      return;
    }
    const cell = this.cellFromElement(cellElement);
    // The first click of the pair already committed any session and
    // activated the cell; boolean cells toggled on that click path instead.
    if (cell !== null && untracked(this.columnsInternal)[cell.col]?.type !== 'boolean') {
      this.openEditor(cell, 'edit');
    }
  }

  /**
   * The scroller's contextmenu handler: suppresses the native menu, selects
   * the press target when it lies outside the selection (Excel), and opens
   * the grid menu at the pointer (or at the active cell for keyboard-
   * sourced events, which carry no real coordinates).
   */
  onContextMenu(event: MouseEvent): void {
    event.preventDefault();
    const menu = this.menuRef;
    if (menu === null) {
      return;
    }
    // The keyboard Menu key opens the menu from its keydown AND emits a native
    // contextmenu; if that one lands here (focus still on the scroller), just
    // swallow it — the menu is already open, reopening would flicker/re-anchor.
    if (untracked(menu.isOpen)) {
      return;
    }
    if (untracked(() => this.engine.edit.session()) !== null) {
      this.commitEditor({ refocus: true });
    }
    if (event.target instanceof Element) {
      this.selectMenuTarget(event.target);
    }
    const element = this.activeCellElement();
    const keyboardSourced = event.clientX === 0 && event.clientY === 0;
    const anchor =
      keyboardSourced && element !== null
        ? element.getBoundingClientRect()
        : { x: event.clientX, y: event.clientY };
    menu.open(anchor, element !== null ? { restoreFocus: element } : undefined);
  }

  /**
   * A touch/pen long-press (§8.6): the same path as right-click — select
   * the pressed cell when it lies outside the selection, then open the
   * context menu at the press point. The long-press observer suppresses
   * the trailing synthetic click/contextmenu burst itself.
   */
  private onLongPress(point: { x: number; y: number }): void {
    const menu = this.menuRef;
    if (menu === null) {
      return;
    }
    if (untracked(() => this.engine.edit.session()) !== null) {
      this.commitEditor({ refocus: true });
    }
    const hit = document.elementFromPoint(point.x, point.y);
    this.selectMenuTarget(hit);
    const element = this.activeCellElement();
    menu.open(point, element !== null ? { restoreFocus: element } : undefined);
  }

  /**
   * Right-click/long-press target outside the selection becomes it (Excel).
   * A row/column header selects its whole row/column first, so the menu acts
   * on that header; a header already inside the selection keeps it (the menu
   * then acts on every selected row/column).
   */
  private selectMenuTarget(target: Element | null): void {
    if (target === null) {
      return;
    }
    const engine = this.engine;
    const rowHeader = target.closest('[data-tm-rowhdr]');
    if (rowHeader !== null) {
      const row = Number(rowHeader.getAttribute('data-row'));
      if (Number.isInteger(row) && !untracked(() => engine.selection.rowIntersects(row))) {
        this.escapedSignal.set(false);
        engine.nav.setActive({ row, col: 0 });
        engine.selection.selectRows(row, row, false);
        this.requestFocusActive();
      }
      return;
    }
    const colHeader = target.closest('[data-tm-colhdr]');
    if (colHeader !== null) {
      const col = Number(colHeader.getAttribute('data-col'));
      if (Number.isInteger(col) && !untracked(() => engine.selection.colIntersects(col))) {
        this.escapedSignal.set(false);
        if (untracked(() => engine.model.viewRowCount()) > 0) {
          engine.nav.setActive({ row: 0, col });
        }
        engine.selection.selectCols(col, col, false);
        this.requestFocusActive();
      }
      return;
    }
    const cellElement = target.closest('[data-tm-cell]');
    if (cellElement === null) {
      return;
    }
    const cell = this.cellFromElement(cellElement);
    if (cell !== null && !untracked(() => engine.selection.isCellSelected(cell))) {
      this.escapedSignal.set(false);
      engine.clickCell(cell);
      this.requestFocusActive();
    }
  }

  /**
   * Commit-on-blur (§8.4): when focus leaves the grid — and lands outside
   * every owned overlay surface (select panel, error overlay, context
   * menu; the CDK keeps popover panes inside its overlay container) — an
   * open editor commits. Safer for forms than Excel's keep-editing.
   */
  onFocusOut(event: FocusEvent): void {
    const engine = this.engineInstance;
    if (engine === null || untracked(() => engine.edit.session()) === null) {
      return;
    }
    const next = event.relatedTarget;
    if (next instanceof Element) {
      if (this.deps.host.contains(next) || next.closest('.cdk-overlay-container') !== null) {
        return; // focus stayed inside the grid or one of its overlay surfaces
      }
    }
    this.commitEditor({ refocus: false });
  }

  /** The scroller's copy handler. */
  onCopy(event: ClipboardEvent): void {
    const engine = this.engineInstance;
    if (engine !== null && untracked(() => engine.edit.session()) !== null) {
      return; // an open editor keeps its native copy (the selected substring)
    }
    this.clipboardDom.onCopy(event);
  }

  /** The scroller's cut handler (arms the deferred move; copy in readonly). */
  onCut(event: ClipboardEvent): void {
    const engine = this.engineInstance;
    if (engine !== null && untracked(() => engine.edit.session()) !== null) {
      return; // an open editor keeps its native cut — not a grid range cut-move
    }
    this.clipboardDom.onCut(event);
  }

  /**
   * The scroller's paste handler: reduces the richest available clipboard
   * flavor through the resolution ladder (§9.3) and hands it to the engine.
   * While an editor is open the event stays with the editor untouched
   * (plain text editing inside the cell, §8.4).
   */
  onPaste(event: ClipboardEvent): void {
    const engine = this.engineInstance;
    if (engine !== null && untracked(() => engine.edit.session()) !== null) {
      return; // native paste into the open editor
    }
    event.preventDefault();
    const data = event.clipboardData;
    if (data === null) {
      return;
    }
    this.pasteFlavors(data.getData('text/plain'), data.getData('text/html'));
  }

  // ---- internals ----

  /**
   * The shared tail of both paste paths (ClipboardEvent and the menu's
   * async read): ladder reduction, the engine paste, driving the batched
   * label resolutions, and re-focusing the (possibly moved) active cell.
   */
  private pasteFlavors(text: string, html: string): void {
    const resolved = ɵtmGridResolvePasteSource(text, html);
    if (resolved === null) {
      return;
    }
    const result = this.engine.clipboard.paste(resolved.source, resolved.fingerprint);
    // Re-baseline the order so the reconcile the paste's own rows-change
    // triggers is an identity no-op — it must not remap (and thus drop) the
    // pasted-block selection the engine just set.
    this.engine.resyncOrder();
    this.runResolutions(result.resolutions);
    this.requestReveal();
    this.requestFocusActive();
  }

  /**
   * Runs each column's batched label resolver (§9.4) and hands the outcome
   * back to the engine. A rejected (or synchronously throwing) resolver marks
   * its awaiting labels as a RETRYABLE resolution failure (distinct from a
   * definitive `notFound`); a column without a resolver (unreachable: the
   * engine only collects for resolver-carrying columns) short-circuits with
   * an empty answered map.
   */
  private runResolutions(requests: readonly TmGridResolutionRequest[]): void {
    for (const request of requests) {
      const column = untracked(this.columnsInternal).find(
        (candidate) => candidate.id === request.columnId,
      );
      const resolver = column?.resolveLabels;
      if (resolver === undefined) {
        this.engine.clipboard.applyResolution(request.id, new Map());
        continue;
      }
      Promise.resolve()
        .then(() => resolver([...request.labels], request.context))
        .then(
          (results) => this.engine.clipboard.applyResolution(request.id, results),
          () => this.engine.clipboard.applyResolution(request.id, new Map(), { failed: true }),
        );
    }
  }

  /**
   * The menu Paste action: reads both flavors through the async Clipboard
   * API and funnels them into the shared paste tail. A rejected read
   * (permission denied, dismissed prompt) degrades the menu item to the
   * keyboard-shortcut hint on subsequent opens (§8.5) — nothing else.
   */
  private async pasteFromAsyncClipboard(): Promise<void> {
    let text = '';
    let html = '';
    try {
      const items = await navigator.clipboard.read();
      for (const item of items) {
        if (text === '' && item.types.includes('text/plain')) {
          text = await (await item.getType('text/plain')).text();
        }
        if (html === '' && item.types.includes('text/html')) {
          html = await (await item.getType('text/html')).text();
        }
      }
    } catch {
      this.pasteReadDenied.set(true);
      return;
    }
    this.pasteFlavors(text, html);
  }

  /**
   * Shows a localized transient notice for {@link TRANSIENT_NOTICE_MS}
   * (a fresh notice restarts the clock; destroy clears the timer).
   */
  private showTransientNotice(key: string): void {
    if (this.transientNoticeTimer !== null) {
      clearTimeout(this.transientNoticeTimer);
    }
    this.transientNoticeSignal.set(untracked(this.deps.translate(key)));
    this.transientNoticeTimer = setTimeout(() => {
      this.transientNoticeTimer = null;
      this.transientNoticeSignal.set(null);
    }, TRANSIENT_NOTICE_MS);
  }

  /**
   * The engine's tree options over the shell's deferred input closures, or
   * `undefined` for the flat grid. `parentId`/`hasChildren` read the
   * CURRENT input inside the engine's derivations (rebinds recompute the
   * structure); the scalar members are getters read per use, untracked
   * (they are consumed from event pipelines and seeding, never reactively).
   */
  private buildTreeOptions(): TmGridTreeOptions<T> | undefined {
    const config = this.deps.tree;
    if (config === undefined) {
      return undefined;
    }
    return {
      parentId: (row: T) => config.parentId()(row),
      hasChildren: (row: T) => config.hasChildren()?.(row) ?? false,
      get parentIdKey(): string | undefined {
        return untracked(config.parentIdKey);
      },
      get defaultExpandedDepth(): number | undefined {
        return untracked(config.defaultExpandedDepth);
      },
    };
  }

  private createEngine(): TmGridEngine<T> {
    return new TmGridEngine<T>({
      rows: () => this.rows(),
      rowId: (row) => this.deps.rowId()(row),
      columns: () => this.engineColumns(),
      editable: () => this.editable(),
      canAddRows: () => this.deps.newRow() !== undefined,
      locale: () => this.deps.locale,
      tenantId: () => this.deps.tenantId(),
      distributionKey: this.deps.distributionKey,
      direction: () => this.deps.direction(),
      pageSize: () => this.pageSize(),
      tree: this.buildTreeOptions(),
      host: {
        // The field binding gets the field-tree writer; the data binding
        // stays writer-less, so every engine mutation is a structural
        // no-op — the readonly contract.
        writer: untracked(this.deps.field) !== undefined ? this.fieldWriter : undefined,
        onNotice: (notice) => {
          this.announcements.notice(notice);
          // A refused multi-range copy is otherwise silent for a sighted user
          // (nothing lands on the clipboard) — surface the reason visibly too.
          if (notice.kind === 'copyRefusedMisaligned') {
            this.showTransientNotice('grid.announce.copyRefused');
          }
          // A structural op reselects (the moved/inserted rows, a delete's
          // fallback cell); its spoken notice IS the event, so mute the coarse
          // selection announcement that reselection would otherwise fire an
          // instant later and overwrite it with in the live region.
          if (
            notice.kind === 'rowsMoved' ||
            notice.kind === 'rowsInserted' ||
            notice.kind === 'rowsDeleted'
          ) {
            this.suppressSelectionAnnounce = true;
          }
        },
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

  private buildColumn(
    dir: TmGridColumn<T, unknown>,
    index: number,
    chromeCols: number,
  ): ColumnInternal<T> {
    const locale = this.deps.locale;
    const key = dir.key() ?? null;
    const id = key ?? dir.generatedId;
    const type = dir.type();
    const accessor = dir.value();
    const format = dir.format();
    const customParse = dir.parse();
    const readonlyOption = dir.readonly();
    const defaultValue = dir.defaultValue();
    const resolveLabels = dir.resolvePastedLabels();
    const displayDef = dir.displayDef();
    const editorDef = dir.editorDef();

    const getValue: (row: T) => unknown =
      key !== null
        ? (row) => (row as Record<string, unknown>)[key]
        : accessor !== undefined
          ? (row) => accessor(row)
          : () => null;

    let enumLabels: ReadonlyMap<unknown, string> | null = null;
    let enumOptions: readonly unknown[] | undefined;
    let optionLabel: ((option: unknown) => string) | undefined;
    let optionValue: ((option: unknown) => unknown) | undefined;
    if (type === 'enum') {
      enumOptions = dir.options();
      if (enumOptions !== undefined) {
        optionLabel = dir.optionLabel() as ((option: unknown) => string) | undefined;
        optionValue = dir.optionValue() as ((option: unknown) => unknown) | undefined;
        const labels = new Map<unknown, string>();
        for (const option of enumOptions) {
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

    const minDecimals = dir.minDecimals();
    const maxDecimals = dir.maxDecimals();
    const fallbackText = (value: unknown): string =>
      value === null || value === undefined ? '' : String(value);
    const typeText = (value: unknown): string => {
      switch (type) {
        case 'number':
          return tmFormatNumber(value, locale, minDecimals, maxDecimals);
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
    // A number column's editor opens on the FULL-PRECISION value, not the
    // (possibly rounded) display text, so `maxDecimals` never writes its
    // rounding back to the model. A custom [format] owns display AND edit text.
    const editSeedText: ((value: unknown) => string) | undefined =
      type === 'number' && format === undefined ? (value) => tmFormatNumber(value, locale) : undefined;

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
      // The bound field's per-cell disabled/readonly state WINS over the
      // column setting (§5.1 — the field is authoritative when bound).
      isCellReadonly: (row) =>
        alwaysReadonly ||
        (readonlyFn !== null && readonlyFn(row)) ||
        (key !== null && this.isFieldCellReadonly(row, key)),
      ...(parse !== undefined ? { parse } : {}),
      hasResolver: resolveLabels !== undefined,
      clearedValue: defaultValue !== undefined ? defaultValue : type === 'boolean' ? false : null,
    };

    return {
      index,
      ariaColIndex: index + chromeCols + 1,
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
      editorDef,
      enumOptions,
      optionLabel,
      optionValue,
      resolveLabels,
      editSeedText,
    };
  }

  /**
   * Whether the bound field marks the row's `key` child disabled or
   * readonly — the field is authoritative over column-level editability.
   * A `data` binding (no field tree) never restricts anything here.
   */
  private isFieldCellReadonly(row: T, key: string): boolean {
    const tree = this.deps.field();
    if (tree === undefined) {
      return false;
    }
    const index = this.engine.model.modelIndexOfRow(this.deps.rowId()(row));
    if (index === -1) {
      return false;
    }
    const rowField = ɵtmRowField(tree, index);
    if (rowField === undefined) {
      return false;
    }
    const child = ɵtmChildField(rowField, key);
    if (child === undefined) {
      return false;
    }
    const state = child();
    return state.disabled() || state.readonly();
  }

  /**
   * The column a field validation error is attributed to: the error
   * field's `keyInParent` when it names a column key (a leaf error under
   * the row), else the first column — row-level errors and deeper-nested
   * ones still count in the tally and remain reachable by the error jump.
   */
  /**
   * Whether an errored cell (addressed by identity) may currently carry an
   * error: a readonly cell never can — it can't be fixed in place, so it must
   * not tint, count, or be jumped to. Mirrors {@link TmGridDataModel.isCellEditable}
   * by identity, using the same column oracle.
   */
  private errorCellEditable(rowId: TmRowId, columnId: string): boolean {
    if (!this.editable()) {
      return false;
    }
    const column = this.columnsInternal().find((c) => c.id === columnId);
    if (column === undefined || !column.engineColumn.editable) {
      return false;
    }
    const row = this.engine.model.rowById(rowId);
    return row !== undefined && !column.engineColumn.isCellReadonly(row);
  }

  private attributeErrorColumn(
    error: ValidationError.WithFieldTree,
    columns: readonly ColumnInternal<T>[],
  ): ColumnInternal<T> {
    const keyInParent = error.fieldTree().keyInParent();
    if (typeof keyInParent === 'string') {
      const match = columns.find((column) => column.key === keyInParent);
      if (match !== undefined) {
        return match;
      }
    }
    return columns[0];
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

    const hierarchyCol = isTree ? this.hierarchyColIndex() : -1;
    const treeAria = isTree ? this.treeAria() : null;
    const loadingChildren = isTree ? this.loadingChildrenIds() : null;
    const wantedExpansion = isTree ? this.wantedExpansionIds() : null;

    // Checked rows (§8.8) and find highlights (§8.7) for the window.
    const checkedIds = this.checkboxColumn() ? this.deps.selectedIds() : null;
    const findKeys = this.findOpenSignal() ? this.findMatchKeys() : null;
    const activeFind = this.findOpenSignal() ? this.activeFindMatchSignal() : null;

    const session = editable ? engine.edit.session() : null;
    const fieldErrors = editable ? this.fieldErrorCells() : EMPTY_FIELD_ERRORS;
    // The clipboard marquee (§9.5): the armed cut (editable only) OR a plain
    // copy (any grid, incl. readonly — you can copy from a readonly grid).
    // Identity sets so windowed rendering marks exactly the marquee cells
    // whatever the current scroll position.
    const marquee = (editable ? engine.clipboard.pendingCut() : null) ?? engine.clipboard.copyMarquee();
    const cutRowIds = marquee === null ? null : new Set(marquee.rowIds);
    const cutColumnIds = marquee === null ? null : new Set(marquee.columnIds);
    // A lone selected cell shows only the active ring — no range fill (§8.6).
    const suppressFill = engine.selection.isSingleCellSelection();

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
      // Clipboard marquee (§9.5): draw the dashed border only on the marquee
      // RANGE's outer edges, so a multi-cell copy/cut reads as one rectangle,
      // not a box per cell. A cell sits on a perimeter edge when the neighbour
      // across that edge — the row above/below in view order, the previous/next
      // column — is NOT itself in the range. Row adjacency is resolved once per
      // row (shared by every column); column adjacency, once per column below.
      const rowInCut = cutRowIds !== null && view !== null && cutRowIds.has(view.id);
      let aboveInCut = false;
      let belowInCut = false;
      if (rowInCut) {
        const aboveId = engine.model.rowAt(viewIndex - 1)?.id;
        const belowId = engine.model.rowAt(viewIndex + 1)?.id;
        aboveInCut = aboveId !== undefined && cutRowIds!.has(aboveId);
        belowInCut = belowId !== undefined && cutRowIds!.has(belowId);
      }
      const cells = columns.map((column, colPos): ɵTmGridCellVm => {
        const inCutRange = rowInCut && cutColumnIds!.has(column.id);
        // Perimeter edge = the neighbour across it is NOT cut (or absent).
        const cutEdges = !inCutRange
          ? ''
          : (aboveInCut ? '' : 't') +
            (belowInCut ? '' : 'b') +
            (colPos === 0 || !cutColumnIds!.has(columns[colPos - 1].id) ? 's' : '') +
            (colPos === columns.length - 1 || !cutColumnIds!.has(columns[colPos + 1].id) ? 'e' : '');
        const cell: TmRowCol = { row: viewIndex, col: column.index };
        const selected = engine.selection.isCellSelected(cell);
        const isActive = active !== null && active.row === viewIndex && active.col === column.index;
        const invalid =
          !isPlaceholder &&
          view !== null &&
          editable &&
          model.isCellEditable(cell) && // a readonly cell is never errored
          (engine.annotations.invalidInput(view.id, column.id) !== undefined ||
            fieldErrors.has(errorCellKey(view.id, column.id)));
        let displayTemplate: TemplateRef<TmGridDisplayContext<unknown, unknown>> | undefined;
        let displayCtx: TmGridDisplayContext<unknown, unknown> | undefined;
        if (!isPlaceholder && view !== null && column.displayDef !== undefined) {
          displayTemplate = column.displayDef.template;
          displayCtx = {
            $implicit: column.engineColumn.getValue(view.row),
            row: view.row,
            rowId: view.id,
            invalid,
            readonly: !model.isCellEditable(cell),
          };
        }
        // Boolean cells keep their checkbox glyph on the placeholder row too
        // (an unchecked box inviting the materializing toggle) — only a
        // custom display template suppresses it (it cannot render there: no
        // row exists yet, so the placeholder cell stays blank instead).
        const glyphClass =
          column.displayDef === undefined && column.type === 'boolean'
            ? TM_CHECKBOX_CELL_DISPLAY.displayClass!(
                (model.cellValue(cell) ?? null) as boolean | null,
              ) +
              // A readonly boolean is display-only: a bare check/empty, no box
              // (the box invites a click the cell won't accept).
              (model.isCellEditable(cell) ? '' : ' tm-grid-bool--readonly')
            : undefined;
        const isHierarchy = isTree && column.index === hierarchyCol;
        // The expander glyph reflects the PENDING intent during a lazy
        // load (wanted expansion), so a click while loading re-collapses.
        const expander =
          isHierarchy && view !== null && view.expandable
            ? view.expanded || wantedExpansion!.has(view.id)
              ? ('expanded' as const)
              : ('collapsed' as const)
            : null;
        return {
          colIndex: column.index,
          ariaColIndex: column.ariaColIndex,
          text: engine.displayText(cell),
          align: column.align,
          selected,
          fill: selected && !suppressFill,
          active: isActive,
          glyphClass,
          displayTemplate,
          displayCtx,
          editing:
            session !== null && session.cell.row === viewIndex && session.cell.col === column.index,
          invalid,
          // The placeholder row included: its cells in readonly COLUMNS are
          // just as uneditable before materialization as after, and styling
          // them editable-looking until the row materializes misleads.
          readonly: editable && !model.isCellEditable(cell),
          pending: view !== null && engine.annotations.isPending(view.id, column.id),
          inCutRange,
          cutEdges,
          findMatch:
            findKeys !== null &&
            view !== null &&
            findKeys.size > 0 &&
            findKeys.has(errorCellKey(view.id, column.id)),
          activeFindMatch:
            activeFind !== null &&
            view !== null &&
            activeFind.rowId === view.id &&
            activeFind.columnId === column.id,
          hierarchy: isHierarchy,
          level: isHierarchy && view !== null ? view.level : 0,
          expander,
          loadingChildren:
            isHierarchy && view !== null && loadingChildren!.has(view.id),
        };
      });
      const siblingPlace = view === null ? undefined : treeAria?.get(view.id);
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
        checked: checkedIds !== null && view !== null && checkedIds.has(view.id),
        headerHit: engine.selection.rowIntersects(viewIndex),
        rowHeaderText: isPlaceholder ? '*' : String(viewIndex + 1),
        // The tree placeholder is a root-level ghost: level 1, no set place.
        ariaLevel: isTree ? (view?.level ?? 0) + 1 : null,
        ariaExpanded:
          isTree && view !== null && view.expandable
            ? view.expanded
              ? 'true'
              : 'false'
            : null,
        ariaPosInSet: siblingPlace?.pos ?? null,
        ariaSetSize: siblingPlace?.size ?? null,
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
        if (!intent.backward) {
          // Enter on an editable non-boolean cell opens the editor in
          // *edit* mode (§8.2); the keymap already routed editable boolean
          // cells to the toggle.
          const active = untracked(() => engine.nav.activeCell());
          if (
            active !== null &&
            untracked(this.editable) &&
            untracked(() => engine.model.isCellEditable(active)) &&
            this.openEditor(active, 'edit')
          ) {
            return true;
          }
          if (this.activateCellLink()) {
            return true;
          }
        }
        engine.moveActive(intent.backward ? 'up' : 'down');
        this.requestReveal();
        this.requestFocusActive();
        return true;
      }
      case 'edit': {
        // F2 / Alt+ArrowDown / type-to-edit. A readonly cell is a NO-OP,
        // not a swallow — the key may still mean something to the browser.
        const active = untracked(() => engine.nav.activeCell());
        return active !== null && this.openEditor(active, intent.mode, intent.seed);
      }
      case 'toggleBoolean': {
        const active = untracked(() => engine.nav.activeCell());
        if (active !== null && engine.edit.toggleBoolean(active)) {
          return true;
        }
        // A readonly boolean cell can't toggle: fall back to Enter semantics
        // (activate a projected link, else move down) so the key is never
        // dead and Space never scrolls the grid instead.
        if (this.activateCellLink()) {
          return true;
        }
        engine.moveActive('down');
        this.requestReveal();
        this.requestFocusActive();
        return true;
      }
      case 'menu': {
        const menu = this.menuRef;
        const element = this.activeCellElement();
        if (menu === null || element === null) {
          return false;
        }
        menu.open(element.getBoundingClientRect(), { restoreFocus: element });
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
        // The deleted rows took the focused cell's DOM node with them; refocus
        // the surviving active cell so focus never falls out of the grid (a
        // later Mod+Z would otherwise land in whatever page control got focus).
        this.requestFocusActive();
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
      case 'escape': {
        // A copy marquee has no pending cut behind it — announce the right one.
        const clearedCopy =
          untracked(() => engine.clipboard.pendingCut()) === null &&
          untracked(() => engine.clipboard.copyMarquee()) !== null;
        if (engine.escape()) {
          // Stage one of the Esc chain cleared the clipboard marquee (§9.5).
          this.announcements.announce(
            clearedCopy ? 'grid.announce.marqueeCleared' : 'grid.announce.cutCancelled',
          );
        } else {
          // The mid-grid exit: the container becomes the single tab stop
          // (cells leave the tab order); any arrow re-enters at the active cell.
          this.escapedSignal.set(true);
          untracked(this.scrollerSignal)?.focus();
        }
        return true;
      }
      case 'expand':
      case 'collapse': {
        // Alt+Arrow acts on the ACTIVE ROW from any column, and is always
        // consumed in a tree — the browser would otherwise navigate history.
        const active = untracked(() => engine.nav.activeCell());
        const view = active === null ? null : untracked(() => engine.model.rowAt(active.row));
        if (view !== null) {
          this.setRowExpanded(view.id, intent.kind === 'expand');
          this.requestReveal();
          this.requestFocusActive();
        }
        return true;
      }
      case 'toggleCheck': {
        // Space on the active row's checkbox (§8.8); the placeholder and
        // an inactive grid fall through to the browser.
        const active = untracked(() => engine.nav.activeCell());
        if (active === null || untracked(() => engine.model.rowAt(active.row)) === null) {
          return false;
        }
        this.toggleRowCheckAt(active.row, false);
        return true;
      }
      case 'toggleSelectAllCheckbox':
        this.toggleSelectAllCheckbox();
        return true;
      case 'find':
        this.openFind();
        return true;
      default:
        return false; // unreachable: every intent kind is handled above
    }
  }

  // ---- tree expand/collapse + lazy loading (§13.3) ----

  /**
   * Expands or collapses a row, composing lazy child loading: expanding a
   * `hasChildren` row with no loaded children calls `loadChildren` and
   * shows the reserved-slot spinner; the node expands when the load lands
   * (unless the user re-collapsed meanwhile), restores collapsed with an
   * announcement on rejection, and a repeat expand while loading just
   * re-marks the wanted expansion. Everything else goes straight to the
   * engine (which also moves activation out of a collapsing subtree).
   */
  private setRowExpanded(rowId: TmRowId, expanded: boolean): void {
    const engine = this.engine;
    if (!expanded) {
      this.removeFrom(this.wantedExpansionIds, rowId);
      engine.setExpanded(rowId, false);
      return;
    }
    const config = this.deps.tree;
    const loadChildren = config === undefined ? undefined : untracked(config.loadChildren);
    const hasChildren = config === undefined ? undefined : untracked(config.hasChildren);
    const row = engine.model.rowById(rowId);
    const needsLoad =
      loadChildren !== undefined &&
      hasChildren !== undefined &&
      row !== undefined &&
      hasChildren(row) &&
      engine.model.subtreeRowIds(rowId).length <= 1; // no loaded children yet
    if (!needsLoad) {
      engine.setExpanded(rowId, true);
      return;
    }
    this.addTo(this.wantedExpansionIds, rowId);
    if (untracked(this.loadingChildrenIds).has(rowId)) {
      return; // already in flight; the resolve path expands
    }
    this.addTo(this.loadingChildrenIds, rowId);
    const generation = this.lazyLoadGeneration;
    Promise.resolve()
      .then(() => loadChildren(row))
      .then(
        () => this.finishChildLoad(rowId, true, generation),
        () => this.finishChildLoad(rowId, false, generation),
      );
  }

  /** The tail of a lazy child load: clear the spinner, then expand/restore. */
  private finishChildLoad(rowId: TmRowId, resolved: boolean, generation: number): void {
    if (this.destroyed || generation !== this.lazyLoadGeneration) {
      return; // disposed, or the content switched out from under the load
    }
    this.removeFrom(this.loadingChildrenIds, rowId);
    const wanted = untracked(this.wantedExpansionIds).has(rowId);
    this.removeFrom(this.wantedExpansionIds, rowId);
    if (!resolved) {
      // The node never expanded during the load; make the restore explicit
      // and tell the user why nothing appeared.
      this.engine.setExpanded(rowId, false);
      this.announcements.announce('grid.announce.lazyLoadFailed');
      return;
    }
    if (wanted) {
      this.engine.setExpanded(rowId, true);
      this.requestReveal();
    }
  }

  /** Adds one id to a set signal (copy-on-write). */
  private addTo(target: WritableSignal<ReadonlySet<TmRowId>>, rowId: TmRowId): void {
    const current = untracked(target);
    if (!current.has(rowId)) {
      const next = new Set(current);
      next.add(rowId);
      target.set(next);
    }
  }

  /** Removes one id from a set signal (copy-on-write). */
  private removeFrom(target: WritableSignal<ReadonlySet<TmRowId>>, rowId: TmRowId): void {
    const current = untracked(target);
    if (current.has(rowId)) {
      const next = new Set(current);
      next.delete(rowId);
      target.set(next);
    }
  }

  // ---- row checkbox selection (§8.8) ----

  /**
   * Toggles the checkbox of the row at a view index. With `shift`, applies
   * the Gmail range model instead: every row between the last toggled row
   * (the anchor) and this one is set to the ANCHOR's current state. The
   * `selectedIds` model always receives a fresh `Set` (two-way binding
   * consumers diff by identity).
   */
  private toggleRowCheckAt(viewRow: number, shift: boolean): void {
    const view = untracked(() => this.engine.model.rowAt(viewRow));
    if (view === null) {
      return; // the placeholder row has no checkbox
    }
    const selected = untracked(this.deps.selectedIds);
    const anchorId = this.lastToggledRowId;
    if (shift && anchorId !== null) {
      const anchorRow = untracked(() => this.engine.model.viewIndexOfRow(anchorId));
      if (anchorRow !== -1) {
        const anchorState = selected.has(anchorId);
        const from = Math.min(anchorRow, viewRow);
        const to = Math.max(anchorRow, viewRow);
        const next = new Set(selected);
        for (let row = from; row <= to; row++) {
          const member = untracked(() => this.engine.model.rowAt(row));
          if (member === null) {
            continue;
          }
          if (anchorState) {
            next.add(member.id);
          } else {
            next.delete(member.id);
          }
        }
        this.deps.selectedIds.set(next);
        this.lastToggledRowId = view.id;
        return;
      }
    }
    const next = new Set(selected);
    if (next.has(view.id)) {
      next.delete(view.id);
    } else {
      next.add(view.id);
    }
    this.deps.selectedIds.set(next);
    this.lastToggledRowId = view.id;
  }

  /**
   * The select-all checkbox (header click / Ctrl+Shift+Space): `all` clears
   * to none; `none` and `mixed` check every data row — hidden-by-collapse
   * rows included, select-all ranges over the DATA, not the viewport.
   */
  private toggleSelectAllCheckbox(): void {
    if (untracked(this.checkAllState) === 'all') {
      this.deps.selectedIds.set(new Set());
      return;
    }
    const rowIdOf = untracked(this.deps.rowId);
    const next = new Set<TmRowId>();
    for (const row of untracked(this.rows)) {
      next.add(rowIdOf(row));
    }
    this.deps.selectedIds.set(next);
  }

  // ---- find (§8.7) ----

  /** Opens the find bar (Mod+F) and focuses its input; re-focuses when open. */
  private openFind(): void {
    if (!untracked(this.deps.searchable)) {
      return;
    }
    if (!untracked(this.findOpenSignal)) {
      this.findOpenSignal.set(true);
      // DOM handlers run outside a tick in zoneless Angular; force the bar
      // to render so its input exists to focus (same device as openEditor).
      this.appRef.tick();
    }
    this.findInput?.focus();
  }

  /** Updates the find query; the scan effect debounces and chunks the scan. */
  setFindQuery(query: string): void {
    this.findQuerySignal.set(query);
  }

  /** The find bar registers its input element (or `null` on close). */
  attachFindInput(element: HTMLElement | null): void {
    this.findInput = element;
  }

  /**
   * Cycles to the next (+1) / previous (−1) match and ACTIVATES its cell —
   * grid operations then apply to it — while focus stays in the find input
   * (the caller is the bar's Enter/Shift+Enter or its buttons).
   */
  findStep(direction: 1 | -1): void {
    const matches = untracked(this.findMatchesSignal);
    if (matches.length === 0) {
      return;
    }
    const current = untracked(this.findActiveIndexSignal);
    const next =
      current === -1
        ? direction === 1
          ? 0
          : matches.length - 1
        : (current + direction + matches.length) % matches.length;
    this.findActiveIndexSignal.set(next);
    this.activateFindMatch(matches[next]);
    this.announceFindCounter();
  }

  /**
   * Closes the find bar (Esc in the input, the close button): the query
   * clears, and focus returns to the grid at the current match — which is
   * activated first so grid operations continue from it.
   */
  closeFind(): void {
    const match = untracked(this.activeFindMatchSignal);
    if (match !== null) {
      this.activateFindMatch(match);
    }
    this.findGeneration += 1; // kills any in-flight scan
    if (this.findDebounceTimer !== null) {
      clearTimeout(this.findDebounceTimer);
      this.findDebounceTimer = null;
    }
    this.findQuerySignal.set('');
    this.findMatchesSignal.set([]);
    this.findActiveIndexSignal.set(-1);
    this.findResultsFor.set(null);
    this.findOpenSignal.set(false);
    // The focused input unmounts with the bar; the roving-focus effect
    // then lands focus on the (possibly newly rendered) active cell.
    this.requestReveal();
    if (untracked(() => this.engine.nav.activeCell()) === null) {
      // No cell to land on (no match, empty query): focus the container so
      // focus never falls to <body> (mirrors the Esc escape-intent path).
      untracked(this.scrollerSignal)?.focus();
    } else {
      this.requestFocusActive();
    }
  }

  /**
   * Scans the WHOLE MODEL (collapsed tree rows included) for the query in
   * time-sliced chunks — large grids produce no long tasks (§16). Matching
   * is a case-insensitive substring test against each cell's text
   * representation: the column's `getText`, overridden by an invalid
   * input's raw text exactly like the display and copy paths.
   */
  private runFindScan(query: string, generation: number): void {
    const rows = untracked(this.rows);
    const columns = untracked(this.columnsInternal);
    const editable = untracked(this.editable);
    const rowIdOf = untracked(this.deps.rowId);
    const needle = query.toLowerCase();
    const matches: ɵTmGridFindMatch[] = [];
    const rowsPerSlice = Math.max(
      1,
      Math.floor(FIND_SCAN_CELLS_PER_SLICE / Math.max(1, columns.length)),
    );
    const scanSlice = (start: number): void => {
      if (generation !== this.findGeneration) {
        return; // superseded by a re-query, a rows change, or destroy
      }
      const end = Math.min(rows.length, start + rowsPerSlice);
      for (let i = start; i < end; i++) {
        const row = rows[i];
        const rowId = rowIdOf(row);
        for (const column of columns) {
          const invalid = editable
            ? untracked(() => this.engine.annotations.invalidInput(rowId, column.id))
            : undefined;
          const text = invalid !== undefined ? invalid.rawText : column.engineColumn.getText(row);
          if (text.toLowerCase().includes(needle)) {
            matches.push({ rowId, columnId: column.id });
          }
        }
      }
      if (end < rows.length) {
        setTimeout(() => scanSlice(end), 0);
      } else {
        this.finishFindScan(query, matches);
      }
    };
    scanSlice(0);
  }

  /**
   * Lands a completed scan: the nearest match — the first at/after the
   * active cell in view order, else the first — becomes current and is
   * scrolled into view (without activating; navigation activates), and the
   * counter is announced.
   */
  private finishFindScan(query: string, matches: readonly ɵTmGridFindMatch[]): void {
    const engine = this.engine;
    // Match cycling follows VIEW order, not the rows-array (scan) order — a
    // tree whose flat array is not authored depth-first would otherwise cycle
    // around unpredictably. A hidden (collapsed) match orders by its nearest
    // visible ancestor, so it slots near where it will appear once expanded.
    const viewOrderKey = (match: ɵTmGridFindMatch): number => {
      const direct = engine.model.viewIndexOfRow(match.rowId);
      if (direct !== -1) {
        return direct;
      }
      for (const ancestor of engine.model.ancestorsOf(match.rowId)) {
        const view = engine.model.viewIndexOfRow(ancestor);
        if (view !== -1) {
          return view;
        }
      }
      return Number.POSITIVE_INFINITY;
    };
    const sorted = [...matches].sort((a, b) => {
      const va = viewOrderKey(a);
      const vb = viewOrderKey(b);
      return va !== vb
        ? va - vb
        : engine.model.columnIndexOf(a.columnId) - engine.model.columnIndexOf(b.columnId);
    });
    this.findMatchesSignal.set(sorted);
    this.findResultsFor.set(query);
    let index = sorted.length > 0 ? 0 : -1;
    if (sorted.length > 0) {
      const active = untracked(() => engine.nav.activeCell());
      if (active !== null) {
        for (let i = 0; i < sorted.length; i++) {
          const row = engine.model.viewIndexOfRow(sorted[i].rowId);
          if (row === -1) {
            continue; // hidden in a collapsed subtree; navigation expands
          }
          const col = engine.model.columnIndexOf(sorted[i].columnId);
          if (row > active.row || (row === active.row && col >= active.col)) {
            index = i;
            break;
          }
        }
      }
    }
    this.findActiveIndexSignal.set(index);
    if (index !== -1) {
      this.revealFindRow(sorted[index].rowId);
    }
    this.announceFindCounter();
  }

  /**
   * Activates a match's cell: ancestors expand first (deep tree matches,
   * §8.7), then activation + collapse + reveal. Focus is NOT pulled — the
   * find input keeps it while the bar is open.
   */
  private activateFindMatch(match: ɵTmGridFindMatch): void {
    const engine = this.engine;
    if (engine.model.isTree) {
      // Through the engine (not the model) so the order snapshot syncs —
      // a later external-data reconcile then remaps against a fresh baseline.
      engine.expandAncestorsOf(match.rowId);
    }
    const row = engine.model.viewIndexOfRow(match.rowId);
    const col = engine.model.columnIndexOf(match.columnId);
    if (row === -1 || col === -1) {
      return; // the match's row/column no longer exists
    }
    engine.nav.setActive({ row, col });
    engine.selection.collapseTo({ row, col });
    this.requestReveal();
  }

  /** Scrolls a match's row into the viewport without touching activation. */
  private revealFindRow(rowId: TmRowId): void {
    const viewRow = this.engine.model.viewIndexOfRow(rowId);
    const scroller = untracked(this.scrollerSignal);
    if (viewRow === -1 || scroller === null) {
      return;
    }
    const rowHeight = untracked(this.rowHeightSignal);
    const rowTop = viewRow * rowHeight;
    const rowBottom = rowTop + rowHeight;
    const bodyViewport = Math.max(rowHeight, scroller.clientHeight - rowHeight);
    if (rowTop < scroller.scrollTop) {
      scroller.scrollTop = rowTop;
    } else if (rowBottom > scroller.scrollTop + bodyViewport) {
      scroller.scrollTop = rowBottom - bodyViewport;
    }
    this.scrollTop.set(scroller.scrollTop);
  }

  /** Announces the counter ('3 of 41' / no matches) through the live region. */
  private announceFindCounter(): void {
    const count = untracked(this.findMatchesSignal).length;
    if (count === 0) {
      this.announcements.announce('grid.find.noMatches');
    } else {
      this.announcements.announce('grid.find.counter', {
        index: untracked(this.findActiveIndexSignal) + 1,
        count,
      });
    }
  }

  // ---- editor session (§8.4) ----

  /**
   * Opens an editor session on a cell and mounts its editor SYNCHRONOUSLY.
   * The outlet only exists once the session signal has rendered, so the
   * core forces one render pass via `ApplicationRef.tick()` — DOM event
   * handlers always run outside a tick in zoneless Angular, so the call is
   * re-entrancy-safe, and the synchronous mount is what lets an IME
   * composition land inside the editor's input within the same task
   * (`opts.ime`); every other open path shares it for one behavior.
   * Returns whether a session opened.
   */
  private openEditor(
    cell: TmRowCol,
    mode: 'edit' | 'enter',
    seedText?: string,
    opts?: { ime?: boolean },
  ): boolean {
    const engine = this.engine;
    const column = untracked(this.columnsInternal)[cell.col];
    if (column === undefined) {
      return false;
    }
    if (column.editorDef === undefined && column.type === 'entity') {
      if (isDevMode() && untracked(() => engine.model.isCellEditable(cell))) {
        throw new Error(
          `tm-grid: column "${column.id}" of type 'entity' has no built-in editor — ` +
            `project a *tmGridEditor template hosting a control that implements TmCellEditor.`,
        );
      }
      return false;
    }
    if (!engine.edit.openEdit(cell, mode, seedText)) {
      return false;
    }
    this.escapedSignal.set(false);
    // The opening keystroke on a scrolled-away cell scrolls it back (§4).
    this.revealActiveCell();
    const view = untracked(() => engine.model.rowAt(cell.row));
    const valueAtOpen = untracked(() => engine.model.cellValue(cell));
    const displayText = untracked(() => engine.displayText(cell));
    // Number columns edit on the full-precision value (see `editSeedText`), but
    // an invalid-input cell must still show the raw text the user typed — which
    // `displayText` already returns for it.
    const hasInvalidInput =
      view !== null &&
      untracked(() => engine.annotations.invalidInput(view.id, column.id)) !== undefined;
    const editText =
      column.editSeedText !== undefined && !hasInvalidInput
        ? column.editSeedText(valueAtOpen)
        : displayText;
    const header = untracked(column.header);

    let config: ɵTmGridEditorMountConfig;
    if (column.editorDef !== undefined) {
      config = {
        kind: 'template',
        template: column.editorDef.template as TemplateRef<TmGridEditorContext<unknown, unknown>>,
        context: { $implicit: valueAtOpen, row: view?.row },
      };
    } else if (column.type === 'enum') {
      config = {
        kind: 'enum',
        label: header,
        options: column.enumOptions ?? [],
        optionLabel: column.optionLabel,
        optionValue: column.optionValue,
        onActivation: () => this.onEnumActivation(),
      };
    } else {
      config = { kind: 'text', label: header };
    }

    this.editorSession.stage(config);
    this.appRef.tick(); // render the outlet now (see the method TSDoc)
    const mounted = this.editorSession.mountIfReady();
    if (mounted === null) {
      // Nothing registered (prod fallback of the dev-mode throw).
      engine.edit.cancel();
      return false;
    }

    const editor = mounted.editor;
    editor.focus();
    if (opts?.ime === true) {
      // IME opens UNSEEDED: the composition itself supplies the content.
    } else if (mounted.kind === 'enum') {
      editor.value.set(valueAtOpen);
      if (seedText !== undefined) {
        // Type-to-edit: the select opens its panel seeding the typeahead.
        editor.seed?.(seedText);
      } else {
        mounted.openDropdown();
      }
    } else if (seedText !== undefined) {
      if (editor.seed !== undefined) {
        editor.seed(seedText);
      } else {
        editor.value.set(seedText);
      }
    } else if (mounted.kind === 'text') {
      // Edit mode edits the cell's CURRENT DISPLAY TEXT (Excel edits the
      // formatted text; for an invalid-input cell that is the raw text) — but a
      // number column edits its full-precision value, not the rounded display.
      if (editor.seed !== undefined) {
        editor.seed(editText);
      } else {
        editor.value.set(editText);
      }
    } else {
      // Consumer template editor: the grid owns the value channel and
      // seeds it with the raw cell value (also carried in the context).
      editor.value.set(valueAtOpen);
    }
    return true;
  }

  /** An enum option was activated: commit and CLOSE, no move (Sheets). */
  private onEnumActivation(): void {
    if (untracked(() => this.engine.edit.session()) !== null) {
      this.commitEditor({ refocus: true });
    }
  }

  /**
   * Commits the open session through the engine: the enum select commits
   * its VALUE; text-path editors commit their text through the column's
   * parse — unless `text()` is `null` (content not representable as text),
   * which commits the value channel directly.
   */
  private commitEditor(opts: { refocus: boolean }): void {
    const engine = this.engine;
    const mounted = this.editorSession.current();
    if (mounted === null) {
      engine.edit.cancel();
    } else if (mounted.kind === 'enum') {
      engine.edit.commitValue(untracked(() => mounted.editor.value()));
    } else {
      const text = untracked(() => mounted.editor.text());
      if (text === null) {
        engine.edit.commitValue(untracked(() => mounted.editor.value()));
      } else {
        engine.edit.commitText(text);
      }
    }
    this.closeEditor(opts);
  }

  /** Cancels the open session: the model is never written (§8.2 Esc, §5.1). */
  private cancelEditor(opts: { refocus: boolean }): void {
    this.editorSession.current()?.editor.cancel();
    this.engine.edit.cancel();
    this.closeEditor(opts);
  }

  private closeEditor(opts: { refocus: boolean }): void {
    this.editorSession.destroy();
    if (opts.refocus) {
      // Synchronously, so focus never falls to <body> (a body-focus frame
      // would look like a grid blur to outside observers) — then again
      // after render, when the move (if any) re-targets the active cell.
      this.activeCellElement()?.focus({ preventScroll: true });
      this.requestFocusActive();
    }
  }

  // ---- context menu (§8.5) ----

  private buildMenuItems(): readonly TmMenuEntry[] {
    const translate = this.deps.translate;
    const icons = this.iconTemplates();
    const editable = this.editable();
    const canAddRows = this.deps.newRow() !== undefined;
    const engine = this.engine;
    engine.selection.ranges(); // recompute the row tally as selection changes
    const spans = engine.selection.rowsUnion();
    const count = Math.max(
      1,
      spans.reduce((sum, span) => sum + (span.end - span.start + 1), 0),
    );
    // Menu Paste rides the async read API. Where the read is unavailable —
    // or once a read was denied — the item degrades to the keyboard-
    // shortcut hint instead (§8.5, the Sheets-established fallback).
    const canReadClipboard =
      typeof navigator.clipboard?.read === 'function' && !this.pasteReadDenied();
    const items: TmMenuEntry[] = [
      {
        id: 'cut',
        label: translate('grid.menu.cut')(),
        icon: icons?.cut,
        shortcut: GRID_MENU_SHORTCUTS.cut,
        disabled: !editable,
        action: () => this.clipboardDom.cutAsync(),
      },
      {
        id: 'copy',
        label: translate('grid.menu.copy')(),
        icon: icons?.copy,
        shortcut: GRID_MENU_SHORTCUTS.copy,
        action: () => this.clipboardDom.copyAsync(),
      },
      {
        id: 'copyWithHeaders',
        label: translate('grid.menu.copyWithHeaders')(),
        icon: icons?.copyPlus,
        action: () => this.clipboardDom.copyAsync({ withHeaders: true }),
      },
      canReadClipboard
        ? {
            id: 'paste',
            label: translate('grid.menu.paste')(),
            icon: icons?.clipboard,
            shortcut: GRID_MENU_SHORTCUTS.paste,
            disabled: !editable,
            action: () => void this.pasteFromAsyncClipboard(),
          }
        : {
            id: 'paste',
            label: translate('grid.menu.pasteHint', {
              shortcut: GRID_MENU_SHORTCUTS.paste,
            })(),
            icon: icons?.clipboard,
            disabled: true,
            action: () => undefined,
          },
    ];
    if (editable) {
      items.push(
        { separator: true },
        {
          id: 'insertAbove',
          label: translate('grid.menu.insertAbove', { count })(),
          icon: icons?.listPlus,
          shortcut: GRID_MENU_SHORTCUTS.insertAbove,
          disabled: !canAddRows,
          action: () => this.engine.insertRows('above'),
        },
        {
          id: 'insertBelow',
          label: translate('grid.menu.insertBelow', { count })(),
          icon: icons?.listPlus,
          disabled: !canAddRows,
          action: () => this.engine.insertRows('below'),
        },
      );
      if (engine.model.isTree) {
        // Insert child row (§13.4): needs the row factory and an active
        // DATA row to parent under (the placeholder has no identity yet).
        const active = engine.nav.activeCell();
        const activeView = active === null ? null : engine.model.rowAt(active.row);
        items.push({
          id: 'insertChild',
          label: translate('grid.menu.insertChild')(),
          icon: icons?.listPlus,
          disabled: !canAddRows || activeView === null,
          action: () => this.insertChildAtActive(),
        });
      }
      items.push({
        id: 'deleteRows',
        label: translate('grid.menu.deleteRows', { count })(),
        icon: icons?.listMinus,
        shortcut: GRID_MENU_SHORTCUTS.deleteRows,
        // Refocus the surviving active cell: the menu's restoreFocus targets
        // the now-deleted cell, so focus would otherwise fall out of the grid.
        action: () => {
          this.engine.deleteSelectedRows();
          this.requestFocusActive();
        },
      });
    }
    const extras = this.deps.extraMenuItems();
    return extras.length > 0 ? [...items, { separator: true }, ...extras] : items;
  }

  /**
   * The menu's Insert-child action: materializes `newRow(parent)` as the
   * active row's last child through the engine (which expands the parent
   * and activates the new row's first editable cell, §13.4).
   */
  private insertChildAtActive(): void {
    const engine = this.engine;
    const active = untracked(() => engine.nav.activeCell());
    const view = active === null ? null : untracked(() => engine.model.rowAt(active.row));
    if (view !== null) {
      engine.insertChildRow(view.id);
      this.requestReveal();
      this.requestFocusActive();
    }
  }

  /** Enter on a readonly cell activates its first interactive projected child. */
  private activateCellLink(): boolean {
    const element = this.activeCellElement();
    const interactive = element?.querySelector<HTMLElement>(INTERACTIVE_CONTENT_SELECTOR) ?? null;
    if (interactive === null) {
      return false;
    }
    // Actuate links/buttons; focus inputs/selects/[tabindex] — the single
    // tab-stop sweep pulled every projected control out of the tab order, so
    // Enter is the only keyboard route to reach them.
    if (interactive.matches('a[href], button')) {
      interactive.click();
    } else {
      interactive.focus();
    }
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
    // Land focus SYNCHRONOUSLY when the active cell is already rendered — a
    // pointer gesture always lands on one, and an in-window keyboard move
    // keeps it rendered. The deferred roving-focus effect above would
    // otherwise not focus it until the next render, so a key (or clipboard
    // shortcut) fired right after the gesture would reach an unfocused grid
    // and be dropped. A move to a not-yet-rendered cell (paging past the
    // window) leaves the element null and falls through to that effect, which
    // also owns DOM-move focus reclaim. Skipped while escaped or an editor
    // owns focus — the effect resolves those; this only ever brings focus
    // forward in time, never changes which cell it lands on.
    if (untracked(this.escapedSignal) || untracked(() => this.engine.edit.session()) !== null) {
      return;
    }
    this.activeCellElement()?.focus({ preventScroll: true });
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

  /**
   * A touch handle's pointerdown (§8.6): the dragged corner becomes the
   * active range's focus (the OPPOSITE corner re-anchors, so the start
   * handle effectively moves the anchor), then the shared drag pipeline —
   * elementFromPoint tracking plus edge auto-scroll — extends the range
   * under the finger. Pointer capture sits on the HANDLE; its events
   * bubble to the scroller listeners.
   */
  beginHandleDrag(event: PointerEvent, edge: 'start' | 'end'): void {
    const handles = untracked(this.selectionHandles);
    if (handles === null || !event.isPrimary) {
      return;
    }
    event.preventDefault(); // the handle gesture must never become a pan
    // Keep the press off the scroller's long-press observer, so holding a
    // handle extends the selection instead of opening the context menu.
    event.stopPropagation();
    const fixed = edge === 'start' ? handles.end : handles.start;
    const dragged = edge === 'start' ? handles.start : handles.end;
    const engine = this.engine;
    engine.selection.collapseTo(fixed);
    engine.selection.extendActiveTo(dragged);
    this.beginDrag(event, 'cells');
  }

  private beginDrag(event: PointerEvent, mode: 'cells' | 'rows' | 'cols'): void {
    const scroller = untracked(this.scrollerSignal);
    if (scroller === null) {
      return;
    }
    this.endDrag();
    const pointerId = event.pointerId;
    // Capture on the SCROLLER (never the handle): a touch-handle drag whose
    // corner scrolls out of the virtual window unmounts the handle div, and
    // capturing there would lose the pointer — the scroller never unmounts.
    const captureElement = scroller;
    try {
      captureElement.setPointerCapture(pointerId);
    } catch {
      // Synthetic events may carry no active pointer.
    }
    let lastX = event.clientX;
    let lastY = event.clientY;
    let frame = 0;

    const applyPoint = (): void => {
      const cellElement = this.dragHitElement(lastX, lastY);
      if (cellElement === null) {
        return;
      }
      // Column headers carry data-col but no data-row, so resolve the 'cols'
      // drag before the row check below (which would reject a header hit).
      if (mode === 'cols') {
        const col = Number(cellElement.getAttribute('data-col'));
        if (Number.isInteger(col)) {
          this.engine.selection.extendActiveTo({ row: 0, col });
        }
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
      if (move.pointerId !== pointerId) {
        return;
      }
      lastX = move.clientX;
      lastY = move.clientY;
      applyPoint();
      if (frame === 0) {
        frame = requestAnimationFrame(autoScrollStep);
      }
    };
    const onEnd = (end: PointerEvent): void => {
      if (end.pointerId !== pointerId) {
        return;
      }
      this.endDrag();
    };
    // Listeners on the DOCUMENT so the drag's pointerup is seen even after
    // capture is lost (the handle unmounting past the edge) — otherwise
    // `endDrag` never runs and `autoScrollStep` reschedules forever.
    document.addEventListener('pointermove', onMove);
    document.addEventListener('pointerup', onEnd);
    document.addEventListener('pointercancel', onEnd);
    this.dragCleanup = () => {
      if (frame !== 0) {
        cancelAnimationFrame(frame);
        frame = 0;
      }
      document.removeEventListener('pointermove', onMove);
      document.removeEventListener('pointerup', onEnd);
      document.removeEventListener('pointercancel', onEnd);
      try {
        captureElement.releasePointerCapture(pointerId);
      } catch {
        // Already released with the pointer.
      }
    };
  }

  /**
   * The drag target under a point. `elementsFromPoint` (plural) pierces
   * the touch handles — during a handle drag the finger sits ON the
   * handle, and the cell beneath it is the one that matters.
   */
  private dragHitElement(x: number, y: number): Element | null {
    const scroller = untracked(this.scrollerSignal);
    for (const element of document.elementsFromPoint(x, y)) {
      const hit = element.closest(
        '[data-tm-cell], [data-tm-rowhdr], [data-tm-colhdr], [data-tm-checkcell]',
      );
      // Only THIS grid's cells: a point over another grid must fall through
      // to null (so the edge auto-scroll governs), never select its cells.
      if (hit !== null && (scroller === null || scroller.contains(hit))) {
        return hit;
      }
    }
    return null;
  }

  private endDrag(): void {
    this.dragCleanup?.();
    this.dragCleanup = null;
  }

  // ---- state store ----

  /**
   * Drops content-scoped transient state on a switch, so a spinner, a forced
   * collapse, or an in-flight lazy-load failure/find scan from the OUTGOING
   * content never lands on the incoming content's same-id row.
   */
  private resetContentTransientState(): void {
    if (untracked(this.loadingChildrenIds).size > 0) {
      this.loadingChildrenIds.set(new Set());
    }
    if (untracked(this.wantedExpansionIds).size > 0) {
      this.wantedExpansionIds.set(new Set());
    }
    this.lazyLoadGeneration += 1; // in-flight child loads resolve into a no-op
    this.findGeneration += 1; // in-flight find scans die quietly
  }

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
      if (untracked(() => engine.model.dataRowCount()) === 0) {
        // No rows yet (async fetch pending): resolving the selection now would
        // fall to the grid origin and lose what §12 remembered — defer it to
        // the first non-empty rows for this content.
        this.pendingSelectionRestore = content.selection;
      } else {
        this.applySelectionSnapshot(content.selection);
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
    if (engine.model.isTree) {
      const dataRows = untracked(() => engine.model.dataRowCount());
      const ids = content?.expandedRowIds;
      if (ids !== undefined) {
        if (dataRows === 0) {
          // Keep the stored set UN-pruned until rows exist — pruning it now
          // (against zero rows) would persist it back as empty.
          this.treeExpansionPending = { kind: 'restore', ids };
        } else {
          engine.restoreExpansion(ids);
          this.treeExpansionPending = null;
        }
      } else if (dataRows === 0) {
        // The engine seeded over an empty model in its constructor; re-seed
        // once rows arrive so `defaultExpandedDepth` actually takes effect.
        this.treeExpansionPending = { kind: 'seed' };
      } else if (!initial) {
        engine.seedExpansion();
        this.treeExpansionPending = null;
      }
    }
    this.collapseUnloadedLazyRows();
  }

  /** Applies a persisted selection snapshot with the active-cell fallback chain. */
  private applySelectionSnapshot(snapshot: NonNullable<TmGridContentState['selection']>): void {
    const engine = this.engine;
    const { restored, activeCell } = engine.selection.restore(snapshot);
    let active = activeCell;
    if (
      active === null &&
      untracked(() => engine.model.viewRowCount()) > 0 &&
      untracked(() => engine.model.columnCount()) > 0
    ) {
      // The engine already tried the row id and the clamped view index; the
      // last leg of the chain is the grid origin.
      active = { row: 0, col: 0 };
    }
    if (active !== null) {
      engine.nav.setActive(active);
      if (!restored) {
        engine.selection.collapseTo(active);
      }
    }
  }

  /**
   * Applies restores deferred at mount because the content had no rows yet
   * (the idiomatic mount-empty-then-fetch flow): the remembered tree
   * expansion and selection land on the first non-empty rows. No-op once
   * nothing is pending or while rows are still absent.
   */
  private applyPendingRestores(): void {
    if (this.treeExpansionPending === null && this.pendingSelectionRestore === null) {
      return;
    }
    if (untracked(() => this.engine.model.dataRowCount()) === 0) {
      return;
    }
    const engine = this.engine;
    if (this.treeExpansionPending !== null) {
      const pending = this.treeExpansionPending;
      this.treeExpansionPending = null;
      if (pending.kind === 'restore') {
        engine.restoreExpansion(pending.ids);
      } else {
        engine.seedExpansion();
      }
      this.collapseUnloadedLazyRows();
    }
    if (this.pendingSelectionRestore !== null) {
      const snapshot = this.pendingSelectionRestore;
      this.pendingSelectionRestore = null;
      this.applySelectionSnapshot(snapshot);
      this.requestReveal();
    }
  }

  /**
   * Collapses lazy rows a seed/restore left "expanded" with no loaded
   * children: such a node renders nothing beneath and would never trigger
   * its load — it must start collapsed so the first expand fetches
   * (§13.3). Runs after every seed/restore; no-op without lazy loading.
   */
  private collapseUnloadedLazyRows(): void {
    const config = this.deps.tree;
    if (config === undefined) {
      return;
    }
    const hasChildren = untracked(config.hasChildren);
    if (hasChildren === undefined || untracked(config.loadChildren) === undefined) {
      return;
    }
    const engine = this.engine;
    for (const id of untracked(() => engine.model.expandedIds())) {
      const row = engine.model.rowById(id);
      if (row !== undefined && hasChildren(row) && engine.model.subtreeRowIds(id).length <= 1) {
        // Through the engine so the order snapshot syncs (this runs after a
        // seed/restore that itself synced, and mutates expansion further).
        engine.setExpanded(id, false);
      }
    }
  }

  private persistWidths(): void {
    if (this.handle === null) {
      return;
    }
    const overrides = untracked(this.widthOverrides);
    // Merge onto the stored widths rather than rebuilding from the current
    // columns only — a column temporarily absent from the set (conditionally
    // rendered) keeps its persisted width; restore tolerates unknown keys.
    const widths: Record<string, number> = { ...(this.handle.getWidths() ?? {}) };
    for (const column of untracked(this.columnsInternal)) {
      if (column.key === null) {
        continue; // accessor columns have no stable cross-session identity
      }
      const width = overrides.get(column.id);
      if (width !== undefined) {
        widths[column.key] = width;
      } else {
        delete widths[column.key]; // a reset column drops its stored width
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
      // Resolve the OUTGOING selection through the engine's outgoing order:
      // on a content switch the rows array has already swapped, so the current
      // model would map these view rows to the new content's row ids.
      selection: engine.selection.toSnapshot(
        untracked(() => engine.nav.activeCell()),
        engine.orderSnapshot,
      ),
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
            // A content switch cancels any open editor (a later commit would
            // otherwise write into the NEW record's same-id row), persists the
            // outgoing state, drops the outgoing content's in-flight transient
            // state, then loads the incoming.
            if (untracked(() => this.engine.edit.session()) !== null) {
              this.cancelEditor({ refocus: false });
            }
            this.persistContentState();
            this.handle.switchContent(contentKey);
            this.lastContentKey = contentKey;
            this.resetContentTransientState();
            this.restoreState(false);
            // Re-baseline the order so the reconcile the concurrent rows
            // change triggers is an identity no-op — not a remap against the
            // outgoing order that would drop the just-restored selection.
            this.engine.resyncOrder();
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
          } else {
            this.engine.reconcile();
          }
          // Async-loaded content: apply the tree expansion / selection that
          // mount deferred because the rows had not arrived yet.
          this.applyPendingRestores();
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
          // A structural op (move/insert/delete) already spoke; skip the one
          // announcement its reselection triggers so its notice isn't clobbered.
          if (this.suppressSelectionAnnounce) {
            this.suppressSelectionAnnounce = false;
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

    // Mode flip (§5.1): editable → readonly never mutates data. The open
    // editor CANCELS (a flip usually follows Save/Cancel — a mode change
    // must not write as a side effect), a pending cut clears, in-flight
    // resolutions abort, and the active cell clamps (the placeholder row
    // is gone). Selection, undo, scroll, and the invalid-input map all
    // survive and return with edit mode.
    let lastEditable: boolean | undefined;
    effect(
      () => {
        const editable = this.editable();
        untracked(() => {
          if (lastEditable === true && !editable) {
            const engine = this.engine;
            const scroller = untracked(this.scrollerSignal);
            const hadFocus = scroller !== null && scroller.contains(document.activeElement);
            if (untracked(() => engine.edit.session()) !== null) {
              this.cancelEditor({ refocus: false });
            }
            engine.clipboard.cancelCut();
            engine.clipboard.abortResolutions();
            engine.nav.reclamp();
            if (hadFocus) {
              // Focus returns via the editor-close path: to the active cell.
              this.requestFocusActive();
            }
          }
          lastEditable = editable;
        });
      },
      { injector },
    );

    // Error-tally warm-up (§16): the first `errorSummary` read forces every
    // row's field nodes, so it is deferred off the mount path — a post-
    // render start, then idle-chunked slices (~500 rows per macrotask)
    // touching each row's summary — before the tally computed first
    // evaluates. A field rebind restarts the chain; the generation guard
    // kills superseded chains.
    effect(
      () => {
        const tree = this.deps.field();
        untracked(() => {
          this.warmupGeneration += 1;
          const generation = this.warmupGeneration;
          this.errorWarmupDone.set(false);
          if (tree === undefined) {
            return;
          }
          afterNextRender(() => this.warmupErrorSlice(tree, 0, generation), { injector });
        });
      },
      { injector },
    );

    // The error overlay's anchor: the ACTIVE cell's element while that cell
    // is errored, no editor is open, and the grid is editable. Re-resolved
    // after every render (the element is recycled by virtualization).
    afterRenderEffect(
      () => {
        this.renderRows();
        const active = this.engine.nav.activeCell();
        const editable = this.editable();
        const session = this.engine.edit.session();
        this.fieldErrorCells();
        untracked(() => {
          let element: Element | null = null;
          if (editable && session === null && active !== null && this.isCellInError(active)) {
            element = this.activeCellElement();
          }
          if (untracked(this.errorAnchorSignal) !== element) {
            this.errorAnchorSignal.set(element);
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
              // The record count is every loaded row, collapsed-subtree rows
              // included — not just the currently-visible ones.
              this.announcements.announce('grid.announce.loaded', {
                count: untracked(() => this.engine.model.modelRowCount()),
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
    // activity changed while the grid owned focus, or a gesture asked for
    // it. Also tracks the rendered window: when the repeater MOVES the
    // focused row's DOM node (outlier ↔ window transitions) the browser
    // silently drops focus to <body> without any focusout — the render
    // that moved the node re-runs this effect, and focus is reclaimed as
    // long as the grid owned it and the drop wasn't the user pressing
    // outside.
    afterRenderEffect(
      () => {
        const active = this.engine.nav.activeCell();
        this.focusRequest();
        this.renderRows();
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
          const droppedByDomMove =
            this.gridOwnsFocus &&
            document.hasFocus() &&
            document.activeElement === document.body &&
            Date.now() - this.lastOutsidePointerDown > 200;
          const shouldFocus =
            this.pendingGestureFocus ||
            scroller.contains(document.activeElement) ||
            droppedByDomMove;
          this.pendingGestureFocus = false;
          if (!shouldFocus) {
            return;
          }
          if (untracked(() => this.engine.edit.session()) !== null) {
            // An open editor owns focus. Only reclaim after a DOM-move drop
            // — and into the EDITOR, never the cell under it.
            if (droppedByDomMove) {
              this.editorSession.current()?.editor.focus();
            }
            return;
          }
          scroller.querySelector<HTMLElement>(cellSelector(active))?.focus({ preventScroll: true });
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

    // Single tab stop: interactive content projected into data cells is
    // pulled out of the tab order — it stays reachable through cell
    // navigation + Enter, and links keep their native pointer affordances.
    // Column/row HEADERS are excluded: the active cell never ranges over the
    // header row and there is no key to activate into it, so neutralizing
    // their content would make a rich/interactive header pointer-only. Runs
    // per render so recycled window rows are re-stamped. Editor content is
    // exempt (it must hold focus).
    afterRenderEffect(
      () => {
        this.renderRows();
        const scroller = this.scrollerSignal();
        if (scroller === null) {
          return;
        }
        untracked(() => {
          const interactive = scroller.querySelectorAll(
            INTERACTIVE_CONTENT_SELECTOR.split(', ')
              .map((s) => `[role="gridcell"] ${s}`)
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
          // Wait for the content to have height before restoring a non-origin
          // scroll — consuming it against a zero-height spacer clamps it to 0
          // and loses it (async-loaded content). The origin restore is free.
          if ((pending.x !== 0 || pending.y !== 0) && untracked(this.totalHeight) === 0) {
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

    // Touch/pen long-press opens the context menu (§8.6), same path as
    // right-click; the observer suppresses the trailing click/contextmenu.
    effect(
      (onCleanup) => {
        const scroller = this.scrollerSignal();
        if (scroller === null) {
          return;
        }
        onCleanup(ɵtmObserveLongPress(scroller, (point) => this.onLongPress(point)));
      },
      { injector },
    );

    // Checked-count announcement (§8.8), debounced so a Shift+click range
    // or a toggle burst speaks once.
    let lastChecked: ReadonlySet<TmRowId> | undefined;
    effect(
      () => {
        const selected = this.deps.selectedIds();
        const enabled = this.checkboxColumn();
        untracked(() => {
          const first = lastChecked === undefined;
          const changed = selected !== lastChecked;
          lastChecked = selected;
          if (first || !changed || !enabled) {
            return;
          }
          if (this.checkedAnnounceTimer !== null) {
            clearTimeout(this.checkedAnnounceTimer);
          }
          this.checkedAnnounceTimer = setTimeout(() => {
            this.checkedAnnounceTimer = null;
            this.announcements.announce('grid.announce.checkedCount', {
              selected: untracked(this.deps.selectedIds).size,
              total: untracked(this.rows).length,
            });
          }, CHECKED_ANNOUNCE_DEBOUNCE_MS);
        });
      },
      { injector },
    );

    // The find scan (§8.7): debounced behind keystrokes, re-armed when the
    // rows or columns change under an open query, generation-guarded so a
    // superseded scan's slices die quietly. A computed cannot chunk — this
    // effect only schedules; the imperative scanner does the walking.
    effect(
      () => {
        const open = this.findOpenSignal();
        const query = this.findQuerySignal();
        this.rows();
        this.columnsInternal();
        // Find searches an invalid-input cell's raw text, so a change to the
        // invalid-input map (a paste, an edit) must re-arm the scan too.
        this.engine.annotations.invalidCount();
        untracked(() => {
          this.findGeneration += 1;
          if (this.findDebounceTimer !== null) {
            clearTimeout(this.findDebounceTimer);
            this.findDebounceTimer = null;
          }
          if (!open || query === '') {
            this.findMatchesSignal.set([]);
            this.findActiveIndexSignal.set(-1);
            this.findResultsFor.set(null);
            return;
          }
          const generation = this.findGeneration;
          this.findDebounceTimer = setTimeout(() => {
            this.findDebounceTimer = null;
            this.runFindScan(query, generation);
          }, FIND_DEBOUNCE_MS);
        });
      },
      { injector },
    );
  }

  /** One error-tally warm-up slice; chains the next via a macrotask. */
  private warmupErrorSlice(tree: FieldTree<T[]>, start: number, generation: number): void {
    if (generation !== this.warmupGeneration) {
      return; // superseded by a rebind or disposal
    }
    const total = untracked(this.rows).length;
    const end = Math.min(total, start + WARMUP_ROWS_PER_SLICE);
    untracked(() => {
      for (let i = start; i < end; i++) {
        ɵtmRowField(tree, i)?.().errorSummary();
      }
    });
    if (end < untracked(this.rows).length) {
      setTimeout(() => this.warmupErrorSlice(tree, end, generation), 0);
    } else {
      this.errorWarmupDone.set(true);
    }
  }

  /** Whether a cell is errored (invalid input or field-invalid), untracked. */
  private isCellInError(cell: TmRowCol): boolean {
    return untracked(() => {
      const view = this.engine.model.rowAt(cell.row);
      const column = this.columnsInternal()[cell.col];
      if (view === null || column === undefined) {
        return false;
      }
      return (
        this.engine.annotations.invalidInput(view.id, column.id) !== undefined ||
        this.fieldErrorCells().has(errorCellKey(view.id, column.id))
      );
    });
  }

  private onDestroy(): void {
    this.destroyed = true; // in-flight lazy child loads complete silently
    this.endDrag();
    this.warmupGeneration += 1; // kills any in-flight warm-up chain
    this.findGeneration += 1; // kills any in-flight find-scan chain
    if (this.transientNoticeTimer !== null) {
      clearTimeout(this.transientNoticeTimer);
      this.transientNoticeTimer = null;
    }
    if (this.checkedAnnounceTimer !== null) {
      clearTimeout(this.checkedAnnounceTimer);
      this.checkedAnnounceTimer = null;
    }
    if (this.findDebounceTimer !== null) {
      clearTimeout(this.findDebounceTimer);
      this.findDebounceTimer = null;
    }
    this.editorSession.destroy();
    if (this.handle !== null) {
      this.persistWidths();
      this.persistContentState();
      this.handle.release();
      this.handle = null;
    }
    this.engineInstance?.dispose();
  }
}
