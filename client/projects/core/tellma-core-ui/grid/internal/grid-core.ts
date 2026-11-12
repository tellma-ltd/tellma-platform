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
  ErrorHandler,
  InjectionToken,
  isDevMode,
  PendingTasks,
  signal,
  untracked,
  type DestroyRef,
  type Injector,
  type Signal,
  type TemplateRef,
  type Type,
  type ViewContainerRef,
  type WritableSignal,
} from '@angular/core';
import type { LiveAnnouncer } from '@angular/cdk/a11y';
import type { FieldTree, ValidationError } from '@angular/forms/signals';
import { ClipboardPaste, Copy, CopyPlus, CornerDownRight, Plus, Scissors, Trash2 } from 'lucide';

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
import { tmIsApplePlatform, tmKeysMatch, tmKeysText, type TmKeys } from '@tellma/core-ui/kbd';
import type {
  TmEntityId,
  TmEntityPickerPage,
  TmEntitySearchFn,
} from '@tellma/core-ui/entity-picker';
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
  TM_NUMBER_MAX_DIGITS,
  tmFormatDate,
  tmFormatNumber,
  tmGregorianCalendar,
  tmNumberDigitCount,
  tmParseDate,
  tmParseNumber,
  type TmCalendar,
} from '@tellma/core-ui/l10n';
import {
  ɵtmObserveLongPress,
  type TmMenu,
  type TmMenuEntry,
  type TmMenuItem,
} from '@tellma/core-ui/menu';
// Types only: the editing code itself arrives through import() (loadEditing).
import type {
  ɵTmGridEditing,
  ɵTmGridEditingHost,
  ɵTmGridEditorOpenOptions,
} from '@tellma/core-ui/grid-editing';

import type { TmGridColumn } from '../tm-grid-column';
import type {
  TmGridDisplayContext,
  TmGridDisplayDef,
  TmGridEditorDef,
  TmGridEmptyDef,
  TmGridHeaderContext,
  TmGridLoadingDef,
} from '../tm-grid-templates';
import type { TmGridStateHandle, TmGridStateStore } from '../tm-grid-state-store';
import type { TmGridRowActivation, TmGridSort } from '../tm-grid-types';
import { ɵTmGridAnnouncements } from './announcements';
import { ɵTmGridClipboardDom, ɵtmGridResolvePasteSource } from './clipboard-dom';
import { ɵTmGridColumnResize } from './column-resize';
import { ɵTmGridFieldWriter, ɵtmChildField, ɵtmRowField } from './field-writer';
import {
  TM_GRID_FIND_KEYS,
  tmResolveEditingKey,
  tmResolveGridKey,
  type TmGridEditingCommitTarget,
  type TmGridIntent,
} from './grid-keymap';

/** What the grid takes from its on-demand editing code. */
export interface ɵTmGridEditingCode {
  /** The editing controller class. */
  readonly ɵTmGridEditing: typeof ɵTmGridEditing;
  /**
   * The editable grid's status-bar component. Typed loosely, as the view's
   * component outlet takes it, so this seam names none of its internals.
   */
  readonly ɵTmGridStatusBar: Type<unknown>;
}

/**
 * Loads the grid's on-demand editing code. Tests replace it to see whether
 * and when a grid asks for that code, or to hold the load open; an app
 * never provides it.
 */
export const ɵTM_GRID_EDITING_LOADER = new InjectionToken<() => Promise<ɵTmGridEditingCode>>(
  'ɵTM_GRID_EDITING_LOADER',
  { providedIn: 'root', factory: () => () => import('@tellma/core-ui/grid-editing') },
);

/**
 * How long (ms) the grid waits for its editing code before it stops holding
 * the app unstable for it: the failure goes to the error handler (once per
 * grid), any draft stays open and usable, and the next edit or paste asks
 * again. Tests and the showcase change it.
 */
export const ɵTM_GRID_EDITING_LOAD_TIMEOUT = new InjectionToken<number>(
  'ɵTM_GRID_EDITING_LOAD_TIMEOUT',
  { providedIn: 'root', factory: () => 10_000 },
);

/**
 * The draft of a session the grid opened before its editing code arrived: a
 * plain input in the editing cell that holds the user's text until the
 * editor mounts with it.
 */
export interface ɵTmGridDraft {
  /** The input's accessible name: the column's header text. */
  readonly label: string;
  /** Whether the column takes numbers, so a touch keyboard offers digits. */
  readonly numeric: boolean;
}

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
  'date',
  'boolean',
  'enum',
]);

/**
 * Context-menu shortcut hints (display only) as key combinations;
 * the menu labels them for the platform (Ctrl+X, or ⌘X on Apple). The keys
 * mirror the keymap: cut/copy/paste are the native Mod+X/C/V; row ops are
 * the Alt-modified Mod+Alt+±.
 */
const GRID_MENU_SHORTCUTS = {
  cut: 'Mod+X',
  copy: 'Mod+C',
  paste: 'Mod+V',
  insertAbove: 'Mod+Alt+Plus',
  deleteRows: 'Mod+Alt+Minus',
} satisfies Record<string, TmKeys>;

/**
 * The context menu's built-in glyphs: Lucide data the menu draws through
 * `tm-icon`. None is directional, so none mirrors in right-to-left text.
 */
const GRID_MENU_ICONS = {
  cut: Scissors,
  copy: Copy,
  copyWithHeaders: CopyPlus,
  paste: ClipboardPaste,
  insertRows: Plus,
  insertChild: CornerDownRight,
  deleteRows: Trash2,
} as const;

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

/** Uniquifies the per-instance scroller id (a driver's `aria-controls` target). */
let nextScrollerId = 0;

/**
 * A second activation of the same row within this window is ignored, in ms,
 * so a double-click opens the row once. (A held key's repeats are consumed
 * before they reach it.)
 */
const ROW_ACTIVATION_GUARD_MS = 400;

/** Pointer travel under which a press and release is a click, not a drag, in px. */
const CLICK_SLOP_PX = 4;

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
      return (text, ctx) => tmParseNumber(text, ctx.locale, { sourceLocale: ctx.sourceLocale });
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
  /** The raw display-format override (the entity editor adapts it id-first). */
  readonly format: ((value: unknown, row: T) => string) | undefined;
  /** `entity` columns: the built-in picker's data seam; `search` absent ⇒ no built-in editor. */
  readonly entitySearch: TmEntitySearchFn<unknown> | undefined;
  readonly entityItemId: ((item: unknown) => TmEntityId) | undefined;
  readonly entityItemLabel: ((item: unknown) => string) | undefined;
  readonly entityAdvancedSearch: TmEntityPickerPage | undefined;
  readonly entityCreate: TmEntityPickerPage | undefined;
  readonly entityEdit: TmEntityPickerPage | undefined;
}

/** One rendered cell's view model. */
export interface ɵTmGridCellVm {
  /** Data-column index. */
  readonly colIndex: number;
  /** `aria-colindex` (offset by the row-header column). */
  readonly ariaColIndex: number;
  /** The cell's display text. */
  readonly text: string;
  /**
   * The text's own direction: `ltr` in `number` columns, so a minus shows at
   * the left of its digits in an RTL grid; `null` (inherited) elsewhere.
   */
  readonly dir: 'ltr' | null;
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
  /**
   * Whether this is the active row of a row-activation grid (the row tint
   * and the accent bar).
   */
  readonly activeRow: boolean;
  /** The row element's id: what a driver's `aria-activedescendant` names. */
  readonly elementId: string;
  /** The consumer's `rowClass` classes, space-separated (empty for none). */
  readonly rowClass: string;
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
 * A sortable column header's sort state (a sortable column with a key;
 * other headers have none).
 */
export interface ɵTmGridHeaderSortVm {
  /** The column's current sort direction, or `null` when it is not the sorted column. */
  readonly direction: 'asc' | 'desc' | null;
  /** The direction a click moves to (`null`: the click clears the sort), for the hover preview. */
  readonly next: 'asc' | 'desc' | null;
  /** The header's `aria-sort`, or `null` on every header but the sorted one. */
  readonly ariaSort: 'ascending' | 'descending' | null;
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
 * `TmGridBase` (which owns the DI) and handed over as one object — the
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
  /**
   * The active locale (formatting, parse context, clipboard metadata) as a
   * signal — display text re-renders when the ambient locale switches;
   * parse contexts read the value at event time.
   */
  readonly locale: Signal<string>;
  /** The app-ambient display calendar — date cells repaint when it switches. */
  readonly calendar: Signal<TmCalendar>;
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
  /** Whether a readonly grid opens rows (`'row'`) or not (`'none'`). */
  readonly rowActivation: Signal<'none' | 'row'>;
  /** The consumer's per-row classes. */
  readonly rowClass: Signal<((row: T) => string | readonly string[] | null) | undefined>;
  /** Whether the row-number column renders. */
  readonly rowHeaders: Signal<boolean>;
  /** The two-way active row id. */
  readonly activeRowId: WritableSignal<TmRowId | null>;
  /** The two-way sort. */
  readonly orderBy: WritableSignal<TmGridSort | null>;
  /** Emits a row activation through the shell's output. */
  readonly rowActivate: (activation: TmGridRowActivation<T>) => void;
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
  /** The checkbox column's `aria-colindex` (after the row-number column, when one renders). */
  readonly checkColIndex: Signal<number>;
  /** Whether the row-number column and the corner render. */
  readonly rowHeaders: Signal<boolean>;
  /** Whether rows open on click and Enter (row activation on a readonly grid). */
  readonly rowMode: Signal<boolean>;
  /** The scroller's element id. */
  readonly scrollerId: string;
  /** The row-activation hint's element id (the scroller's `aria-describedby` in row mode). */
  readonly rowHintId: string;
  /** The localized row-activation hint. */
  readonly rowActivationHint: Signal<string>;
  /** Per column, the header's sort state; `undefined` on a header that does not sort. */
  readonly headerSort: Signal<readonly (ɵTmGridHeaderSortVm | undefined)[]>;
  /** The select-all checkbox's tri-state over the bound rows. */
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
  /**
   * The editable grid's status-bar component once the editing code has
   * loaded, or `null` until then; the view renders it in a slot that holds
   * its place meanwhile.
   */
  readonly statusBar: Signal<Type<unknown> | null>;
  /**
   * The draft of a session opened before the editing code arrived, or
   * `null`; the view renders its input in the editing cell until the editor
   * mounts.
   */
  readonly draft: Signal<ɵTmGridDraft | null>;
  /** The draft input's composition events: an IME composition starts or ends. */
  onDraftComposition(composing: boolean): void;
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
  /** Hands the core the context menu (or `null` when it is gone). */
  attachMenu(menu: TmMenu | null): void;
  /** The scroller's scroll handler. */
  onScroll(event: Event): void;
  /** The scroller's keydown handler. */
  onKeydown(event: KeyboardEvent): void;
  /** The scroller's pointerdown handler (cells, row headers, drags). */
  onPointerDown(event: PointerEvent): void;
  /** The scroller's click handler (column headers, corner). */
  onClick(event: MouseEvent): void;
  /** The scroller's auxclick handler (a middle click opens a row in a new tab). */
  onAuxClick(event: MouseEvent): void;
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
 * state-store lifecycle. Constructed by `TmGridBase` with the host's
 * dependencies; the view component (`ɵTmGridView`) renders its signals and
 * routes DOM events back into it.
 *
 * Editing lives in `@tellma/core-ui/grid-editing`, which an editable grid
 * loads on demand (`loadEditing`); this class implements the seam that code
 * works through (`ɵTmGridEditingHost`), whose members are public for that
 * reason only.
 */
export class ɵTmGridCore<T> implements ɵTmGridViewCore, ɵTmGridEditingHost<T> {
  private readonly deps: ɵTmGridCoreDeps<T>;
  private readonly announcements: ɵTmGridAnnouncements;
  private readonly clipboardDom: ɵTmGridClipboardDom;
  /** The application, ticked to render an editor's outlet before it mounts. */
  readonly appRef: ApplicationRef;
  private readonly fieldWriter: ɵTmGridFieldWriter<T>;
  /** The editing controller, once its on-demand code has arrived. */
  private editing: ɵTmGridEditing<T> | null = null;
  /** Whether a load of the editing code is in flight. */
  private editingRequested = false;
  /** Ends the load in flight early (grid disposal), or `null` while none is. */
  private endLoad: (() => void) | null = null;
  /** Cancels the idle preload still waiting to start, or `null`. */
  private cancelPreload: (() => void) | null = null;
  /** Whether a failed load was reported: a grid reports its first only. */
  private loadFailureReported = false;
  /**
   * The draft of a session opened before the editing code arrived: a plain
   * input in the editing cell, where the browser owns the text (modifiers,
   * dead keys, IME, the caret, undo, the touch keyboard). The user's commit
   * gestures write it through the engine, and the editor takes it over when
   * the code arrives.
   */
  readonly draft = signal<ɵTmGridDraft | null>(null);
  /** The draft's input element while a draft is open. */
  private draftInput: HTMLInputElement | null = null;
  /**
   * The text the draft opened with on an edit-mode or IME open: a draft
   * still equal to it commits nothing. `null` for type-to-edit, whose seed
   * is already an edit.
   */
  private draftBase: string | null = null;
  /** The text the draft opened with: an editor resuming it shows this much already. */
  private draftStart = '';
  /** The open options of the draft's session, applied when its editor mounts. */
  private draftOpts: ɵTmGridEditorOpenOptions | undefined;
  /** Whether an IME composition is in progress in the draft input. */
  private draftComposing = false;
  /** Editing code that arrived mid-composition, installed when it ends. */
  private arrivedCode: ɵTmGridEditingCode | null = null;
  /**
   * Entity commits made before the editing code arrived, pending in their
   * cells until it resolves them as the editor would.
   */
  private deferredLabels: { readonly request: TmGridResolutionRequest; readonly text: string }[] = [];
  /** The editing cell's editor outlet, or `null` while none renders. */
  editorOutlet: ViewContainerRef | null = null;
  /** The status-bar component, set when the editing code arrives. */
  readonly statusBar = signal<Type<unknown> | null>(null);
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

  /** The scroll container, once the view rendered it. */
  private readonly scrollerSignal = signal<HTMLElement | null>(null);
  private readonly scrollTop = signal(0);
  private readonly scrollLeft = signal(0);
  private readonly viewportHeight = signal(0);
  private readonly rowHeightSignal = signal(DEFAULT_ROW_HEIGHT);
  /** Whether Escape parked focus on the scroller (the mid-grid exit). */
  readonly escapedSignal = signal(false);
  private readonly widthOverrides = signal<ReadonlyMap<string, number>>(new Map());
  private readonly focusRequest = signal(0);
  private readonly revealRequest = signal(0);
  private readonly scrollRestoreRequest = signal(0);
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
  /** The last emitted row activation: the double-activation guard. */
  private lastActivation: { readonly rowId: TmRowId; readonly at: number } | null = null;
  /**
   * Whether the pointer press in progress fired the long-press (a pen held
   * still): its release then opens nothing, since the press asked for the
   * menu.
   */
  private pressLongPressed = false;
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

  /** The column model: directive inputs resolved with type defaults, in view order. */
  readonly columnsInternal: Signal<readonly ColumnInternal<T>[]>;
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
  private readonly activeCellFieldErrors: Signal<readonly ValidationError.WithOptionalFieldTree[]>;
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
  /** The checkbox column's `aria-colindex`. */
  readonly checkColIndex: Signal<number>;
  /** Whether the row-number column and the corner render. */
  readonly rowHeaders: Signal<boolean>;
  /** Whether rows open on click and Enter (row activation on a readonly grid). */
  readonly rowMode: Signal<boolean>;
  /** The scroller's element id. */
  readonly scrollerId = `tm-grid-${nextScrollerId++}`;
  /** The row-activation hint's element id. */
  readonly rowHintId = `${this.scrollerId}-hint`;
  /** The localized row-activation hint. */
  readonly rowActivationHint: Signal<string>;
  /** Per column, the header's sort state; `undefined` on a header that does not sort. */
  readonly headerSort: Signal<readonly (ɵTmGridHeaderSortVm | undefined)[]>;
  /** The active row element's id, or `null` with no active row. */
  readonly activeDescendantId: Signal<string | null>;
  /** The select-all checkbox's tri-state over the bound rows. */
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
    // Row activation is a readonly-grid affordance too; the shell throws in
    // dev mode on an editable grid, and it stays off in prod builds.
    this.rowMode = computed(() => deps.rowActivation() === 'row' && !this.editable());
    this.rowHeaders = deps.rowHeaders;
    this.checkColIndex = computed(() => (deps.rowHeaders() ? 2 : 1));
    this.rowActivationHint = deps.translate('grid.rowActivationHint');
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
      // the row header and the checkbox column, each when it renders.
      const chromeCols = (deps.rowHeaders() ? 1 : 0) + (this.checkboxColumn() ? 1 : 0);
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
      () =>
        this.engine.model.columnCount() +
        (deps.rowHeaders() ? 1 : 0) +
        (this.checkboxColumn() ? 1 : 0),
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
      const tracks: string[] = [];
      if (deps.rowHeaders()) {
        tracks.push('var(--grid-row-header-width)');
      }
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
    // The tri-state ranges over the BOUND rows (collapsed tree rows
    // included) — the current page of a paged list, not the viewport. Ids
    // checked on other pages do not count here (spec 0010 §C.5).
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

    // ---- sort headers (spec 0010 §C.3) ----
    // Separate from the column models so a sort change re-renders the
    // header indicators only, never the body's columns.
    this.headerSort = computed(() => {
      const sort = deps.orderBy();
      return deps.columns().map((column): ɵTmGridHeaderSortVm | undefined => {
        const key = column.key();
        if (!column.sortable() || key === undefined) {
          return undefined;
        }
        const direction = sort?.key === key ? sort.direction : null;
        return {
          direction,
          next: direction === null ? 'asc' : direction === 'asc' ? 'desc' : null,
          ariaSort: direction === null ? null : direction === 'asc' ? 'ascending' : 'descending',
        };
      });
    });

    // ---- the driver's active descendant (spec 0010 §C.2) ----
    // The active row is always rendered (as an outlier outside the window),
    // so the id never dangles.
    this.activeDescendantId = computed(() => {
      const active = this.engine.nav.activeCell();
      return active === null || this.engine.model.rowAt(active.row) === null
        ? null
        : `${this.scrollerId}-r${active.row}`;
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
      // Cells show dates in the ambient locale and calendar; so must the
      // messages about them.
      (iso) => tmFormatDate(iso, deps.locale(), { calendar: deps.calendar() }),
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
          case 'precision':
            return deps.translate('grid.cellErrors.precision', {
              text: invalid.rawText,
              maxDigits: TM_NUMBER_MAX_DIGITS,
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
        target instanceof Element && (deps.host.contains(target) || this.inOverlay(target));
      // Focus that moved on to the page cancels a still-pending focus
      // request: the next render must not pull it back. (A drop to <body>
      // when the render removes the focused node fires no focusin, so the
      // request that re-lands focus after a delete survives.)
      this.pendingGestureFocus &&= this.gridOwnsFocus;
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
    this.editorOutlet = outlet;
  }

  /** Hands the core the context menu (or `null` when it is gone). */
  attachMenu(menu: TmMenu | null): void {
    this.menuRef = menu;
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

  // ---- the active row and row activation (spec 0010 §C.1, §C.2) ----

  /**
   * Moves the active row by a count, or to the first or last data row:
   * clamped, never wrapping, keeping the active column (the first one from
   * no active row, where every move starts at the first row). Collapses the
   * selection onto the new cell and scrolls it into view; focus stays
   * wherever it is (a driving search field keeps it).
   */
  moveActiveRow(by: number | 'first' | 'last'): void {
    const engine = this.engine;
    const target = untracked((): TmRowCol | null => {
      let last = engine.model.viewRowCount() - 1;
      if (last >= 0 && engine.model.isPlaceholder(last)) {
        last -= 1; // the new-row placeholder is not a row to open
      }
      if (last < 0) {
        return null;
      }
      const active = engine.nav.activeCell();
      const row =
        by === 'first'
          ? 0
          : by === 'last'
            ? last
            : active === null
              ? 0
              : Math.min(last, Math.max(0, active.row + Math.trunc(by)));
      return { row, col: active?.col ?? 0 };
    });
    if (target === null) {
      return;
    }
    engine.nav.setActive(target);
    engine.selection.collapseTo(target);
    this.requestReveal();
  }

  /**
   * Emits a row activation for a row (the active one when no id is given).
   * Returns `false` when the grid holds no such row. A second activation of
   * the same row within the guard window is swallowed — still `true`, so a
   * driver keeps consuming the key — and a double-click opens once.
   */
  activateRow(rowId: TmRowId | undefined, via: 'pointer' | 'keyboard', newTab: boolean): boolean {
    const engine = this.engine;
    const id = untracked((): TmRowId | null => {
      if (rowId !== undefined) {
        return rowId;
      }
      const active = engine.nav.activeCell();
      return active === null ? null : (engine.model.rowAt(active.row)?.id ?? null);
    });
    const row = id === null ? undefined : untracked(() => engine.model.rowById(id));
    if (id === null || row === undefined) {
      return false;
    }
    const now = Date.now();
    const last = this.lastActivation;
    if (last !== null && last.rowId === id && now - last.at < ROW_ACTIVATION_GUARD_MS) {
      return true;
    }
    this.lastActivation = { rowId: id, at: now };
    this.deps.rowActivate({ row, rowId: id, via, newTab });
    return true;
  }

  /**
   * The data cell a pointer gesture on `target` would open the row from, or
   * `null`: only in row mode, and never from interactive cell content (a
   * link keeps its native click), the tree expander, an open editor or a
   * cell-anchored overlay. Chrome cells (row header, checkbox) are not data
   * cells, so they never qualify. Carries the row's id.
   */
  private activationCell(target: Element): (TmRowCol & { readonly id: TmRowId }) | null {
    if (!untracked(this.rowMode) || this.inOverlay(target)) {
      return null;
    }
    const cellElement = target.closest('[data-tm-cell]');
    if (cellElement === null || target.closest('[data-tm-expander], [data-tm-editor]') !== null) {
      return null;
    }
    // The cell itself carries a roving tabindex: only STRICT descendants
    // count as interactive content.
    const interactive = target.closest(INTERACTIVE_CONTENT_SELECTOR);
    if (interactive !== null && interactive !== cellElement && cellElement.contains(interactive)) {
      return null;
    }
    const cell = this.cellFromElement(cellElement);
    const view = cell === null ? null : untracked(() => this.engine.model.rowAt(cell.row));
    return cell === null || view === null ? null : { ...cell, id: view.id };
  }

  /** The `a[href]` inside a data cell that `target` sits in, or `null`. */
  private cellLink(target: EventTarget | null): Element | null {
    if (!(target instanceof Element) || this.inOverlay(target)) {
      return null;
    }
    const link = target.closest('a[href]');
    return link !== null && link.closest('[data-tm-cell]') !== null ? link : null;
  }

  // ---- sort (spec 0010 §C.3) ----

  /**
   * A click on a sortable header: cycles its column asc → desc → unsorted,
   * or starts another column at asc, and announces the change. The grid
   * never reorders — the consumer sorts the data it binds.
   */
  private cycleSort(col: number): void {
    const column = untracked(this.deps.columns)[col];
    const key = column === undefined ? undefined : untracked(column.key);
    if (key === undefined || !untracked(column!.sortable)) {
      return;
    }
    const current = untracked(this.deps.orderBy);
    const next: TmGridSort | null =
      current?.key !== key
        ? { key, direction: 'asc' }
        : current.direction === 'asc'
          ? { key, direction: 'desc' }
          : null;
    this.deps.orderBy.set(next);
    if (next === null) {
      this.announcements.announce('grid.announce.sortCleared');
    } else {
      // A header drawn by a template has no header text: name it by key.
      const header = untracked(this.columnsInternal)[col]?.header;
      this.announcements.announce('grid.announce.sorted', {
        column: (header === undefined ? '' : untracked(header)) || key,
        direction: next.direction,
      });
    }
  }

  // ---- DOM event pipeline (wired by the view template) ----

  /** The scroller's scroll handler. */
  onScroll(event: Event): void {
    const element = event.currentTarget as HTMLElement;
    this.scrollTop.set(element.scrollTop);
    this.scrollLeft.set(element.scrollLeft);
  }

  /**
   * Whether a node sits inside an overlay surface. Every overlay puts its
   * content in a `.cdk-overlay-pane`, wherever the surface is hosted —
   * top-layer overlays render IN PLACE, so a popup anchored to a cell (the
   * date editor's calendar) is a child of that cell and its events bubble
   * through the scroller. They belong to the overlay: a press on a
   * calendar day is not a click-away, and a key inside the calendar is not
   * a grid key. The overlay's own dismiss logic owns the gesture.
   */
  private inOverlay(node: Element | null): boolean {
    return node !== null && node.closest('.cdk-overlay-pane') !== null;
  }

  /** The scroller's keydown handler: resolve to an intent, execute, consume. */
  onKeydown(event: KeyboardEvent): void {
    if (event.target instanceof Element && this.inOverlay(event.target)) {
      return;
    }
    // A Tab either moves within the grid, which requests focus anew, or
    // leaves it — possibly to no element at all (the end of the document
    // fires no focusin). Either way a focus request still pending from an
    // earlier gesture is void: honoured on the next render, it would pull
    // focus straight back into the grid the user just tabbed out of.
    if (event.key === 'Tab') {
      this.pendingGestureFocus = false;
    }
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
      apple: tmIsApplePlatform(),
      editable: untracked(this.editable),
      searchable: untracked(this.deps.searchable),
      selectable: untracked(this.checkboxColumn),
      isTree: engine.model.isTree,
      activeIsBoolean,
      rowActivation: untracked(this.rowMode),
    });
    // After Esc parked focus on the container, Tab/Shift+Tab must leave the
    // grid natively — the engineered mid-grid exit. On an editable grid the
    // keymap still resolves Tab (it means commit-and-move mid-edit), so guard
    // it here rather than re-entering the grid at the next editable cell.
    if (intent?.kind === 'tab' && untracked(this.escapedSignal)) {
      return;
    }
    // A held Enter or Mod+Enter opens the row once: key repeats outlast the
    // double-activation guard, so they are consumed without opening again.
    if (event.repeat && event.key === 'Enter' && !event.shiftKey && untracked(this.rowMode)) {
      event.preventDefault();
      return;
    }
    if (intent !== null && this.executeIntent(intent)) {
      event.preventDefault();
    }
  }

  /**
   * The keydown branch while an editor session is open, its editor mounted
   * or its draft still standing in. Any editing keystroke on a
   * scrolled-away cell scrolls it back first, a key the editor consumed
   * stays its own, and Mod+F opens the grid's find. The rest resolves
   * through the editing keys; a key they do not name stays with the input,
   * the draft's as much as the editor's, so the grid never acts on it.
   */
  private onEditingKeydown(event: KeyboardEvent, session: TmGridEditSession): void {
    // Any editing keystroke on a scrolled-away cell scrolls it back first (§4).
    this.revealActiveCell();
    const editing = this.editing;
    if (editing === null) {
      // A key in the draft is a gesture: a load that failed asks again.
      this.loadEditing();
    }
    if (event.defaultPrevented) {
      return;
    }
    // Mod+F opens the grid find even from inside an editor — the editor input
    // must not let the browser's find dialog shadow it: commit, then open.
    if (untracked(this.deps.searchable) && tmKeysMatch(event, TM_GRID_FIND_KEYS)) {
      event.preventDefault();
      this.commitEditor({ refocus: false });
      this.openFind();
      return;
    }
    const resolved = tmResolveEditingKey(event, {
      mode: session.mode,
      isDropdownOpen: editing?.isDropdownOpen() ?? false,
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
        if (editing === null) {
          // The draft's editor opens showing its choices when it mounts.
          this.draftOpts = { ...this.draftOpts, dropdown: true };
          event.preventDefault();
        } else if (editing.openDropdown()) {
          event.preventDefault();
        }
        return;
      case 'commitMove':
        this.commitAndMove(resolved.target, event);
        return;
    }
  }

  /**
   * The text a draft opens with: the type-to-edit seed; nothing under an
   * IME composition, which supplies its own; otherwise what the cell's
   * editor would open with. An enum's panel and a consumer template take
   * typing as a fresh search or value instead, so theirs opens empty.
   */
  private draftStartText(cell: TmRowCol, seedText: string | undefined, ime: boolean): string {
    if (seedText !== undefined) {
      return seedText;
    }
    const column = untracked(this.columnsInternal)[cell.col];
    return ime || column === undefined || column.editorDef !== undefined || column.type === 'enum'
      ? ''
      : this.editTextOf(cell);
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
    if (!(event.target instanceof Element)) {
      return;
    }
    if (event.button === 1) {
      // A middle press on a data cell of a row-activation grid moves the
      // active cell now (the instant highlight) and keeps the browser's
      // autoscroll off; its auxclick opens the row in a new tab.
      const pressed = this.activationCell(event.target);
      if (pressed !== null && event.pointerType !== 'touch') {
        event.preventDefault();
        this.escapedSignal.set(false);
        this.engine.clickCell(pressed);
        this.requestFocusActive();
      }
      return;
    }
    if (event.button !== 0) {
      return;
    }
    const target = event.target;
    if (this.inOverlay(target)) {
      return; // a cell-anchored overlay surface (see `inOverlay`)
    }
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
    // The pointer's Mod (add a range, add rows or columns): ⌘ on Apple
    // platforms, Ctrl elsewhere; the other of the two is no modifier the
    // grid gives a meaning.
    const apple = tmIsApplePlatform();
    const mod = apple ? event.metaKey : event.ctrlKey;
    const otherControl = apple ? event.ctrlKey : event.metaKey;
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
      if (
        interactive !== null &&
        interactive !== cellElement &&
        cellElement.contains(interactive)
      ) {
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
      // Row activation (spec 0010 §C.1): a press and release under the
      // click slop opens the row — with Mod, in a new tab. Shift (extend),
      // Alt and the non-Mod control key keep the press a selection gesture;
      // a drag past the slop is a range drag as ever.
      const opensRow =
        !event.shiftKey && !event.altKey && !otherControl ? this.activationCell(target) : null;
      this.beginDrag(
        event,
        'cells',
        opensRow === null
          ? undefined
          : { onClick: () => this.activateRow(opensRow.id, 'pointer', mod) },
      );
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
      if (!event.shiftKey && !mod && !event.altKey && untracked(this.headerSort)[col]) {
        // A sortable header (spec 0010 §C.3): a click sorts and leaves the
        // selection (and focus) alone; only a drag past the slop turns the
        // press into the column selection a plain header makes on press.
        this.beginDrag(event, 'cols', {
          onDragStart: () => this.pressSelectColumn(col, false),
          onClick: () => this.cycleSort(col),
        });
        return;
      }
      if (event.shiftKey) {
        this.engine.selection.extendActiveTo({ row: 0, col });
      } else {
        this.pressSelectColumn(col, mod);
      }
      this.beginDrag(event, 'cols');
    }
  }

  /**
   * A header press's column selection: the column's first cell becomes
   * active (when there are rows), the column is selected (added to the
   * selection with Mod), and the active cell takes focus.
   */
  private pressSelectColumn(col: number, add: boolean): void {
    if (untracked(() => this.engine.model.viewRowCount()) > 0) {
      this.engine.nav.setActive({ row: 0, col });
    }
    this.engine.selection.selectCols(col, col, add);
    this.requestFocusActive();
  }

  /**
   * The scroller's auxclick handler: a middle click on a data cell of a
   * row-activation grid opens the row in a new tab (its press already moved
   * the active cell). A middle click on a link stays the browser's own.
   */
  onAuxClick(event: MouseEvent): void {
    if (event.button !== 1 || !(event.target instanceof Element)) {
      return;
    }
    const opens = this.activationCell(event.target);
    if (opens !== null) {
      this.activateRow(opens.id, 'pointer', true);
    }
  }

  /** The scroller's click handler: column headers and the select-all corner. */
  onClick(event: MouseEvent): void {
    if (!(event.target instanceof Element)) {
      return;
    }
    const target = event.target;
    if (this.inOverlay(target)) {
      return; // a cell-anchored overlay surface (see `inOverlay`)
    }
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
      // A tap opens the row in row mode — not one on a link or button in the
      // cell, whose own click is the tap's meaning.
      const opens = this.activationCell(target);
      if (opens !== null) {
        this.activateRow(opens.id, 'pointer', false);
      }
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
      if (untracked(this.headerSort)[col]) {
        this.cycleSort(col); // a tap on a sortable header sorts, like a click
        return;
      }
      this.pressSelectColumn(col, false);
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
    if (this.inOverlay(hit)) {
      return; // a cell-anchored overlay surface (see `inOverlay`)
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
    if (event.target instanceof Element && this.inOverlay(event.target)) {
      return; // a cell-anchored overlay surface keeps its native menu
    }
    const keyboardSourced = event.clientX === 0 && event.clientY === 0;
    const link = keyboardSourced ? null : this.cellLink(event.target);
    if (link !== null) {
      // A link in a cell keeps the browser's menu (open in a new tab, copy
      // the link): move the active cell there and leave the event alone
      // (spec 0010 §C.1). The Menu key and Shift+F10 still open the grid's.
      const cellElement = link.closest('[data-tm-cell]');
      const cell = cellElement === null ? null : this.cellFromElement(cellElement);
      if (cell !== null) {
        // Settle an open editor first (clickCell's contract); no refocus, so
        // the browser's menu stays on the link.
        if (untracked(() => this.engine.edit.session()) !== null) {
          this.commitEditor({ refocus: false });
        }
        this.escapedSignal.set(false);
        this.engine.clickCell(cell);
      }
      return;
    }
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
    this.pressLongPressed = true;
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
   * then acts on every selected row/column). Focus is not asked for: the
   * menu that opens next takes it, and returns it to the active cell as it
   * closes.
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
    }
  }

  /**
   * Whether a focus drop to NOWHERE is a re-render artifact rather than the
   * user leaving. Swapping views inside an open overlay — picking a month
   * in the calendar — destroys the very element that had focus, and the
   * browser then drops focus to `<body>` with no `relatedTarget`: at the
   * event itself, indistinguishable from a click onto inert page content.
   * The tell is the press that precedes a real departure — a click-away
   * lands outside the host first, and the document-level capture records
   * it. (A window switch is not one either: the document loses focus.)
   */
  private focusDroppedByRerender(): boolean {
    return (
      this.gridOwnsFocus && document.hasFocus() && Date.now() - this.lastOutsidePointerDown > 200
    );
  }

  /**
   * Commit-on-blur (§8.4): when focus leaves the grid — and lands outside
   * every overlay surface (select panel, error overlay, context menu,
   * calendar popup) — an open editor commits. Safer for forms than
   * Excel's keep-editing. A draft owns no overlay, so focus moving into
   * one commits a draft too.
   */
  onFocusOut(event: FocusEvent): void {
    const engine = this.engineInstance;
    if (engine === null || untracked(() => engine.edit.session()) === null) {
      return;
    }
    const next = event.relatedTarget;
    if (next instanceof Element) {
      if (this.deps.host.contains(next) || (this.editing !== null && this.inOverlay(next))) {
        return; // focus stayed inside the grid or one of its editor's overlay surfaces
      }
    } else if (this.focusDroppedByRerender()) {
      return; // the editor's own overlay re-rendered under the pointer
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
   * While an editor or a draft is open the event stays with its input
   * untouched (plain text editing inside the cell).
   */
  onPaste(event: ClipboardEvent): void {
    const engine = this.engineInstance;
    if (engine !== null && untracked(() => engine.edit.session()) !== null) {
      return; // native paste into the open editor's or draft's input
    }
    // An editable grid whose editing code failed to load asks again: the
    // status bar it brings is where an editable grid reports paste errors.
    if (this.editing === null && untracked(this.editable)) {
      this.loadEditing();
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
  runResolutions(
    requests: readonly TmGridResolutionRequest[],
    fallback?: TmLabelResolution<unknown>,
  ): void {
    for (const request of requests) {
      const column = untracked(this.columnsInternal).find(
        (candidate) => candidate.id === request.columnId,
      );
      const resolver = column?.resolveLabels;
      if (resolver === undefined) {
        this.engine.clipboard.applyResolution(
          request.id,
          this.withFallback(request, new Map(), fallback),
        );
        continue;
      }
      Promise.resolve()
        .then(() => resolver([...request.labels], request.context))
        .then(
          (results) =>
            this.engine.clipboard.applyResolution(
              request.id,
              this.withFallback(request, results, fallback),
            ),
          () => this.engine.clipboard.applyResolution(request.id, new Map(), { failed: true }),
        );
    }
  }

  /**
   * Substitutes a caller's verdict for labels the resolver could not name.
   * Used when the editor's own search already reached a conclusion the
   * resolver was merely being given a chance to improve on: the resolver
   * wins wherever it names something, and the search's verdict stands where
   * it answers "I don't know this" — a `notFound` from an identity lookup
   * does not contradict "the search matched several", and the search's
   * message is the more useful of the two. A resolver that FAILED never
   * lands here: that path is retryable and must stay so.
   */
  private withFallback(
    request: TmGridResolutionRequest,
    results: ReadonlyMap<string, TmLabelResolution<unknown>>,
    fallback: TmLabelResolution<unknown> | undefined,
  ): ReadonlyMap<string, TmLabelResolution<unknown>> {
    if (fallback === undefined) {
      return results;
    }
    const merged = new Map(results);
    for (const label of request.labels) {
      const answer = merged.get(label);
      if (answer === undefined || (!('value' in answer) && answer.error === 'notFound')) {
        merged.set(label, fallback);
      }
    }
    return merged;
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
      locale: () => this.deps.locale(),
      calendar: () => this.deps.calendar().id,
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

    let entitySearch: TmEntitySearchFn<unknown> | undefined;
    let entityItemId: ((item: unknown) => TmEntityId) | undefined;
    let entityItemLabel: ((item: unknown) => string) | undefined;
    let entityAdvancedSearch: TmEntityPickerPage | undefined;
    let entityCreate: TmEntityPickerPage | undefined;
    let entityEdit: TmEntityPickerPage | undefined;
    if (type === 'entity') {
      entitySearch = dir.search();
      entityItemId = dir.itemId() as ((item: unknown) => TmEntityId) | undefined;
      entityItemLabel = dir.itemLabel() as ((item: unknown) => string) | undefined;
      entityAdvancedSearch = dir.advancedSearch();
      entityCreate = dir.create();
      entityEdit = dir.edit();
    }

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

    if (isDevMode() && format === undefined && type === 'entity' && !this.warnedColumns.has(id)) {
      this.warnedColumns.add(id);
      console.warn(
        `tm-grid: column "${id}" of type '${type}' has no [format]; ` +
          `cells fall back to String(value). Bind [format] to give the ` +
          `column a real text representation.`,
      );
    }

    const minDecimals = dir.minDecimals();
    const maxDecimals = dir.maxDecimals();
    const calendar = this.deps.calendar;
    const fallbackText = (value: unknown): string =>
      value === null || value === undefined ? '' : String(value);
    // `locale`/`calendar` are read INSIDE the closures (not captured as
    // values): the cell view model is computed-driven, so display text
    // re-renders in place when the ambient locale or calendar switches.
    const typeText = (value: unknown): string => {
      switch (type) {
        case 'number':
          return tmFormatNumber(value, locale(), { minDecimals, maxDecimals });
        case 'boolean':
          return TM_CHECKBOX_CELL_DISPLAY.formatValue((value ?? null) as boolean | null, locale());
        case 'enum':
          return enumLabels?.get(value) ?? fallbackText(value);
        case 'date':
          return typeof value === 'string' && value !== ''
            ? tmFormatDate(value, locale(), { calendar: calendar() })
            : fallbackText(value);
        default:
          return fallbackText(value);
      }
    };
    const getText: (row: T) => string =
      format !== undefined ? (row) => format(getValue(row), row) : (row) => typeText(getValue(row));
    // A number column's editor opens on the FULL-PRECISION value, not the
    // (possibly rounded) display text, so the user edits what the cell
    // holds rather than a rendering of it. The COMMIT still rounds to the
    // column's display scale (see `normalizeValue` below) — only a
    // programmatic write keeps precision the display drops. A custom
    // [format] owns display AND edit text.
    const editSeedText: ((value: unknown) => string) | undefined =
      type === 'number' && format === undefined
        ? (value) => tmFormatNumber(value, locale())
        : undefined;

    // The built-in date parse, in three rungs. A payload from OUTSIDE the
    // app is Gregorian unless the ambient calendar reads it too, in which
    // case it is ambiguous and errors. A payload from another Tellma grid
    // is read in the SOURCE's display calendar (Hijri `23/9/1445` copied
    // from an ar-SA grid is a valid-looking Gregorian date; reading it as
    // Gregorian would silently write year 1445), and a known-but-
    // unavailable source calendar errors rather than guess. Everything
    // else — typed commits above all — reads in the ambient display
    // calendar. Column-level [parse] still overrides all of it.
    const dateParse =
      type === 'date'
        ? (text: string, ctx: TmParseContext): unknown | TmParseError => {
            const active = untracked(calendar);
            if (ctx.foreignSource === true && active.id !== 'gregory') {
              // Outside the app, dates are Gregorian — spreadsheets and
              // editors have no other calendar — so a foreign payload is
              // read that way first, and only falls back to the ambient
              // calendar if Gregorian cannot read it at all.
              //
              // KNOWN AMBIGUITY: text that BOTH calendars can read is
              // decided in Gregorian's favour. That is right for the
              // spreadsheet case this rung exists for, but wrong if the
              // payload is really this grid's own display text pasted
              // back through a route that dropped the metadata (a mobile
              // keyboard, a clipboard manager, a hop through a text
              // editor). Nothing in the text distinguishes the two.
              const asGregorian = tmParseDate(text, ctx.locale, {
                calendar: tmGregorianCalendar(),
              });
              if (typeof asGregorian === 'string' || asGregorian === null) {
                return asGregorian;
              }
            }
            if (ctx.sourceLocale !== undefined) {
              const sourceLocale = ctx.sourceLocale;
              const sourceCalendar =
                ctx.sourceCalendar === undefined || ctx.sourceCalendar === 'gregory'
                  ? tmGregorianCalendar()
                  : ctx.sourceCalendar === active.id
                    ? active
                    : null;
              if (
                sourceCalendar !== null &&
                (sourceLocale !== ctx.locale || sourceCalendar.id !== active.id)
              ) {
                const foreign = tmParseDate(text, sourceLocale, {
                  calendar: sourceCalendar,
                });
                if (typeof foreign === 'string' || foreign === null) {
                  return foreign;
                }
              }
              if (sourceCalendar === null && ctx.sourceCalendar !== undefined) {
                // The source's calendar pack is not installed here — the
                // active-calendar rung below would misread its digits.
                return TM_PARSE_ERROR;
              }
            }
            return tmParseDate(text, ctx.locale, { calendar: active });
          }
        : undefined;
    const parse = customParse ?? dateParse ?? defaultParseFor(type, enumLabels);
    // The model-equals-display invariant for number columns: user commits
    // and pastes round to the column's display scale, and a committed
    // value must fit the numeric precision envelope (an IEEE-754 double
    // round-trips at most 15 significant digits against an exact decimal).
    // Custom-parse columns own their values; programmatic writes never
    // pass through here.
    const normalizeValue: ((value: unknown) => unknown | TmParseError) | undefined =
      type === 'number' && customParse === undefined
        ? (value) => {
            if (typeof value !== 'number' || !Number.isFinite(value)) {
              return value;
            }
            const activeLocale = locale();
            const bounds = { minDecimals, maxDecimals };
            const rounded = tmParseNumber(
              tmFormatNumber(value, activeLocale, bounds),
              activeLocale,
              bounds,
            );
            if (typeof rounded !== 'number') {
              return value;
            }
            return tmNumberDigitCount(rounded) > TM_NUMBER_MAX_DIGITS ? TM_PARSE_ERROR : rounded;
          }
        : undefined;
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
        (BUILT_IN_EDIT_TYPES.has(type) ||
          customParse !== undefined ||
          editorDef !== undefined ||
          entitySearch !== undefined),
      // The bound field's per-cell disabled/readonly state WINS over the
      // column setting (§5.1 — the field is authoritative when bound).
      isCellReadonly: (row) =>
        alwaysReadonly ||
        (readonlyFn !== null && readonlyFn(row)) ||
        (key !== null && this.isFieldCellReadonly(row, key)),
      ...(parse !== undefined ? { parse } : {}),
      ...(normalizeValue !== undefined ? { normalizeValue } : {}),
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
      format,
      entitySearch,
      entityItemId,
      entityItemLabel,
      entityAdvancedSearch,
      entityCreate,
      entityEdit,
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
    // The per-cell display text is deliberately NOT one of them: it runs
    // tracked, through the column's text closure, which reads the ambient
    // locale and display calendar at call time. That is the only thing that
    // re-renders every formatted cell in place when the user switches
    // language or calendar — wrapping the cell-text path in `untracked()`
    // would silently freeze each cell at whatever was in force when its row
    // was last rendered for some other reason.
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
    // Row mode tints the active row; `rowClass` styles every rendered row.
    const activeRow = this.rowMode() && active !== null ? active.row : -1;
    const rowClassOf = this.deps.rowClass();
    const fieldErrors = editable ? this.fieldErrorCells() : EMPTY_FIELD_ERRORS;
    // The clipboard marquee (§9.5): the armed cut (editable only) OR a plain
    // copy (any grid, incl. readonly — you can copy from a readonly grid).
    // Identity sets so windowed rendering marks exactly the marquee cells
    // whatever the current scroll position.
    const marquee =
      (editable ? engine.clipboard.pendingCut() : null) ?? engine.clipboard.copyMarquee();
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
            (colPos === columns.length - 1 || !cutColumnIds!.has(columns[colPos + 1].id)
              ? 'e'
              : '');
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
          // Formatted numbers carry no direction marks (spec 0010 §D.14):
          // markup keeps the sign and the digits in their LTR order.
          dir: column.type === 'number' ? 'ltr' : null,
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
          loadingChildren: isHierarchy && view !== null && loadingChildren!.has(view.id),
        };
      });
      const siblingPlace = view === null ? undefined : treeAria?.get(view.id);
      const classes = view === null || rowClassOf === undefined ? null : rowClassOf(view.row);
      rows.push({
        rowKey,
        viewIndex,
        activeRow: viewIndex === activeRow,
        elementId: `${this.scrollerId}-r${viewIndex}`,
        rowClass: classes === null ? '' : typeof classes === 'string' ? classes : classes.join(' '),
        ariaRowIndex: viewIndex + 2,
        isPlaceholder,
        outlier,
        outlierTransform: outlier ? `translateY(${(viewIndex - win.start) * rowHeight}px)` : null,
        zebra: !editable && !isTree && viewIndex % 2 === 1,
        checked: checkedIds !== null && view !== null && checkedIds.has(view.id),
        headerHit: engine.selection.rowIntersects(viewIndex),
        rowHeaderText: isPlaceholder ? '*' : String(viewIndex + 1),
        // The tree placeholder is a root-level ghost: level 1, no set place.
        ariaLevel: isTree ? (view?.level ?? 0) + 1 : null,
        ariaExpanded:
          isTree && view !== null && view.expandable ? (view.expanded ? 'true' : 'false') : null,
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
        if (!intent.backward && untracked(this.rowMode)) {
          // Row activation: Enter opens the active row (spec 0010 §C.1); F2
          // is the key that reaches a link in the cell.
          return this.activateRow(undefined, 'keyboard', false);
        }
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
        if (untracked(this.rowMode)) {
          // F2 in a row-activation grid actuates the cell's first link or
          // button (Enter opens the row there instead).
          return this.activateCellLink();
        }
        // F2 / Alt+ArrowDown / type-to-edit. A readonly cell is a NO-OP,
        // not a swallow — the key may still mean something to the browser.
        const active = untracked(() => engine.nav.activeCell());
        return (
          active !== null &&
          this.openEditor(active, intent.mode, intent.seed, { dropdown: intent.dropdown === true })
        );
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
      case 'activateRow':
        return untracked(this.rowMode) && this.activateRow(undefined, 'keyboard', intent.newTab);
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
   * The select-all checkbox (header click / Ctrl+Shift+Space) over the
   * BOUND rows — hidden-by-collapse rows included, the data rather than the
   * viewport, and the current page rather than every page: `all` unchecks
   * the bound rows, `none` and `mixed` check them. Ids checked on other
   * pages stay checked either way.
   */
  private toggleSelectAllCheckbox(): void {
    const rowIdOf = untracked(this.deps.rowId);
    const uncheck = untracked(this.checkAllState) === 'all';
    const next = new Set<TmRowId>(untracked(this.deps.selectedIds));
    for (const row of untracked(this.rows)) {
      if (uncheck) {
        next.delete(rowIdOf(row));
      } else {
        next.add(rowIdOf(row));
      }
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

  // ---- editor session (§8.4): the on-demand editing code ----

  /**
   * Preloads the editing code once an editable grid has rendered, when the
   * browser is next idle, so the code is normally in place before the first
   * keystroke and the draft stays a rare fallback. The wait is a pending
   * task, so a test's or the app's stability covers it; an open or a paste
   * meanwhile starts the load at once.
   */
  private preloadEditing(): void {
    if (this.editing !== null || this.editingRequested || this.cancelPreload !== null) {
      return;
    }
    const removeTask = this.deps.injector.get(PendingTasks).add();
    const start = (): void => {
      this.cancelPreload = null;
      this.loadEditing(); // takes its own pending task before this one ends
      removeTask();
    };
    // Safari has no idle callback; a zero timeout still yields to the render.
    const idle = typeof requestIdleCallback === 'function';
    const handle = idle ? requestIdleCallback(start, { timeout: 2000 }) : setTimeout(start);
    this.cancelPreload = () => {
      (idle ? cancelIdleCallback : clearTimeout)(handle as number);
      removeTask();
    };
  }

  /**
   * Loads the editing code, one attempt at a time. The attempt is a pending
   * task, bounded by the load timeout. On arrival, even after the timeout,
   * the code is installed (see `installEditing`). A failure or a timeout
   * leaves any draft open and usable, reports the error once per grid, and
   * lets the next gesture ask again.
   */
  private loadEditing(): void {
    if (this.editing !== null || this.editingRequested || this.destroyed) {
      return;
    }
    this.editingRequested = true;
    const injector = this.deps.injector;
    const removeTask = injector.get(PendingTasks).add();
    // A preload still waiting for idle has nothing left to start.
    this.cancelPreload?.();
    this.cancelPreload = null;
    const timeoutMs = injector.get(ɵTM_GRID_EDITING_LOAD_TIMEOUT);
    let open = true;
    const timer = setTimeout(() => {
      end();
      this.reportLoadFailure(new Error(`tm-grid: no editing code after ${timeoutMs} ms.`));
    }, timeoutMs);
    // Ends this attempt once: its timer, its pending task, the in-flight flag.
    const end = (): void => {
      if (open) {
        open = false;
        clearTimeout(timer);
        removeTask();
        this.editingRequested = false;
        this.endLoad = null;
      }
    };
    this.endLoad = end;
    let loading: Promise<ɵTmGridEditingCode>;
    try {
      loading = injector.get(ɵTM_GRID_EDITING_LOADER)();
    } catch (error) {
      loading = Promise.reject(error);
    }
    loading.then(
      (code) => {
        end();
        this.installEditing(code);
      },
      (error: unknown) => {
        end();
        this.reportLoadFailure(error);
      },
    );
  }

  /** Reports a failed load to the error handler: the first only, and never after disposal. */
  private reportLoadFailure(error: unknown): void {
    if (this.destroyed || this.loadFailureReported) {
      return;
    }
    this.loadFailureReported = true;
    this.deps.injector.get(ErrorHandler).handleError(error);
  }

  /**
   * Builds the editing controller from the code that arrived and hands the
   * view its status bar. An open draft is handed over: while its input has
   * focus, the editor mounts with the draft's text and caret and takes
   * focus; otherwise the draft commits as it stands, so the arrival never
   * pulls focus or opens a panel where the user is not. An IME composition
   * in progress finishes in the draft first. Entity commits waiting on the
   * code are resolved last.
   */
  private installEditing(code: ɵTmGridEditingCode): void {
    if (this.destroyed || this.editing !== null) {
      return;
    }
    const input = untracked(this.draft) !== null ? this.draftInput : null;
    if (input !== null && this.draftComposing) {
      this.arrivedCode = code;
      return;
    }
    this.arrivedCode = null;
    const editing = new code.ɵTmGridEditing<T>(this, this.deps.injector);
    this.statusBar.set(code.ɵTmGridStatusBar);
    if (input === null) {
      this.editing = editing;
    } else if (document.activeElement === input) {
      const text = input.value;
      // The editor mounts and takes focus while the draft input is still in
      // place, so focus moves from one to the other and never drops.
      this.editing = editing;
      try {
        editing.resume(this.draftOpts, {
          text,
          changed: text !== this.draftStart,
          // A text input always reports its selection.
          selectionStart: input.selectionStart!,
          selectionEnd: input.selectionEnd!,
          selectionDirection: input.selectionDirection as 'forward' | 'backward' | 'none',
        });
      } finally {
        this.closeDraft();
      }
    } else {
      this.commitDraft();
      this.editing = editing;
    }
    this.drainDeferredLabels();
  }

  /** Resolves the entity commits made before the editing code arrived (see `commitDraft`). */
  private drainDeferredLabels(): void {
    const editing = this.editing;
    if (editing === null) {
      return;
    }
    const queued = this.deferredLabels;
    this.deferredLabels = [];
    for (const { request, text } of queued) {
      // An undo, a readonly flip or a disposal aborted it meanwhile.
      if (!request.context.signal.aborted) {
        editing.resolveLabel(request, text);
      }
    }
  }

  /** The draft input's composition events: the code waiting on a composition installs when it ends. */
  onDraftComposition(composing: boolean): void {
    this.draftComposing = composing;
    if (!composing) {
      this.installArrivedCode();
    }
  }

  /**
   * Installs editing code that waited on a composition in the draft, once
   * nothing holds it: after the composition's own last input event.
   */
  private installArrivedCode(): void {
    const code = this.arrivedCode;
    if (code !== null) {
      queueMicrotask(() => {
        if (this.arrivedCode === code) {
          this.installEditing(code);
        }
      });
    }
  }

  /**
   * Whether a cell's column can host an editor: every column but an
   * `entity` one without a `*tmGridEditor` template or the built-in
   * picker's three facilities. Such a cell never opens a session (the
   * editing code reports the misconfiguration in development).
   */
  hasEditor(cell: TmRowCol): boolean {
    const column = untracked(this.columnsInternal)[cell.col];
    return (
      column !== undefined &&
      (column.editorDef !== undefined ||
        column.type !== 'entity' ||
        (column.entitySearch !== undefined &&
          column.entityItemId !== undefined &&
          column.entityItemLabel !== undefined))
    );
  }

  /**
   * The text an edit-mode open edits: the cell's display text, except that
   * a number column edits its full-precision value — while an errored cell
   * edits the raw text the user typed, which the display text already is.
   */
  editTextOf(cell: TmRowCol): string {
    const engine = this.engine;
    const column = untracked(this.columnsInternal)[cell.col];
    const view = untracked(() => engine.model.rowAt(cell.row));
    const invalid =
      view !== null &&
      column !== undefined &&
      untracked(() => engine.annotations.invalidInput(view.id, column.id)) !== undefined;
    return column?.editSeedText !== undefined && !invalid
      ? column.editSeedText(untracked(() => engine.model.cellValue(cell)))
      : untracked(() => engine.displayText(cell));
  }

  /**
   * Opens an editor session on a cell (see `ɵTmGridEditing.open`). Before
   * the editing code arrives, the session opens in the engine with a draft:
   * a plain input in the cell, focused at once (inside the gesture, so a
   * touch keyboard rises) and holding the seed or the text the editor would
   * open with. A draft already open is never replaced: it commits first.
   * Returns whether a session opened.
   */
  private openEditor(
    cell: TmRowCol,
    mode: 'edit' | 'enter',
    seedText?: string,
    opts?: ɵTmGridEditorOpenOptions,
  ): boolean {
    if (this.editing !== null) {
      return this.editing.open(cell, mode, seedText, opts);
    }
    if (!this.hasEditor(cell)) {
      return false;
    }
    const engine = this.engine;
    if (untracked(() => engine.edit.session()) !== null) {
      this.commitEditor({ refocus: false });
    }
    if (!engine.edit.openEdit(cell, mode, seedText)) {
      return false;
    }
    this.escapedSignal.set(false);
    // The opening keystroke on a scrolled-away cell scrolls it back (§4).
    this.revealActiveCell();
    // `hasEditor` above vouched for the column.
    const column = untracked(this.columnsInternal)[cell.col]!;
    const start = this.draftStartText(cell, seedText, opts?.ime === true);
    this.draftOpts = opts;
    this.draftStart = start;
    this.draftBase = seedText === undefined ? start : null;
    this.draftComposing = false;
    this.draft.set({ label: untracked(column.header), numeric: column.type === 'number' });
    // Render the input now, so focus (and an IME composition's first
    // keydown) lands in it within this task, as with an editor's mount.
    this.appRef.tick();
    const input =
      untracked(this.scrollerSignal)?.querySelector<HTMLInputElement>('input[data-tm-draft]') ??
      null;
    this.draftInput = input;
    if (input !== null) {
      input.value = start;
      input.setSelectionRange(start.length, start.length);
      input.focus({ preventScroll: true });
    }
    this.loadEditing();
    return true;
  }

  /** Closes the draft: its input goes with the next render. */
  private closeDraft(): void {
    this.draft.set(null);
    this.draftInput = null;
    this.draftComposing = false;
    this.installArrivedCode();
  }

  /**
   * Commits the open session (see `ɵTmGridEditing.commit`); before the
   * editing code arrives, the draft commits (see `commitDraft`). Without a
   * session there is nothing to commit.
   */
  private commitEditor(opts: { refocus: boolean }): void {
    if (this.editing !== null) {
      this.editing.commit(opts);
      return;
    }
    if (untracked(this.draft) !== null) {
      this.commitDraft();
    }
    if (opts.refocus) {
      this.requestFocusActive();
    }
  }

  /**
   * Commits the draft's text through the engine, as the editor would
   * commit it. A draft still equal to the text it opened with writes
   * nothing. A built-in entity picker's text goes pending in its cell until
   * the editing code resolves it with the picker's own search
   * (`drainDeferredLabels`); a template editor's entity text takes the
   * label resolution at once; any other column's text takes its parse.
   */
  private commitDraft(): void {
    const text = this.draftInput?.value ?? this.draftStart;
    const base = this.draftBase;
    this.closeDraft();
    const engine = this.engine;
    const session = untracked(() => engine.edit.session());
    if (session === null) {
      return;
    }
    const column = untracked(this.columnsInternal)[session.cell.col];
    if (text === base) {
      engine.edit.cancel();
    } else if (column?.type === 'entity') {
      const viaSearch = column.editorDef === undefined;
      const request = engine.commitEditorLabel(text, { deferToHost: viaSearch });
      if (request === null) {
        return; // an earlier rung settled it
      }
      if (viaSearch) {
        this.deferredLabels.push({ request, text });
        this.drainDeferredLabels();
      } else {
        this.runResolutions([request]);
      }
    } else {
      engine.edit.commitText(text);
    }
  }

  /**
   * Cancels the open session without writing the model (see
   * `ɵTmGridEditing.cancel`); before the editing code arrives, the draft
   * goes with it.
   */
  private cancelEditor(opts: { refocus: boolean }): void {
    if (this.editing !== null) {
      this.editing.cancel(opts);
      return;
    }
    if (untracked(this.draft) !== null) {
      this.closeDraft();
      this.engine.edit.cancel();
    }
    if (opts.refocus) {
      this.requestFocusActive();
    }
  }

  // ---- context menu (§8.5) ----

  private buildMenuItems(): readonly TmMenuEntry[] {
    const translate = this.deps.translate;
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
        icon: GRID_MENU_ICONS.cut,
        shortcut: GRID_MENU_SHORTCUTS.cut,
        disabled: !editable,
        action: () => this.clipboardDom.cutAsync(),
      },
      {
        id: 'copy',
        label: translate('grid.menu.copy')(),
        icon: GRID_MENU_ICONS.copy,
        shortcut: GRID_MENU_SHORTCUTS.copy,
        action: () => this.clipboardDom.copyAsync(),
      },
      {
        id: 'copyWithHeaders',
        label: translate('grid.menu.copyWithHeaders')(),
        icon: GRID_MENU_ICONS.copyWithHeaders,
        action: () => this.clipboardDom.copyAsync({ withHeaders: true }),
      },
      canReadClipboard
        ? {
            id: 'paste',
            label: translate('grid.menu.paste')(),
            icon: GRID_MENU_ICONS.paste,
            shortcut: GRID_MENU_SHORTCUTS.paste,
            disabled: !editable,
            action: () => void this.pasteFromAsyncClipboard(),
          }
        : {
            id: 'paste',
            // The hint is a sentence, so the combination is written out as
            // text for the platform ("Ctrl+V", "⌘V") rather than as caps.
            label: translate('grid.menu.pasteHint', {
              shortcut: tmKeysText(GRID_MENU_SHORTCUTS.paste),
            })(),
            icon: GRID_MENU_ICONS.paste,
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
          icon: GRID_MENU_ICONS.insertRows,
          shortcut: GRID_MENU_SHORTCUTS.insertAbove,
          disabled: !canAddRows,
          action: () => this.engine.insertRows('above'),
        },
        {
          id: 'insertBelow',
          label: translate('grid.menu.insertBelow', { count })(),
          icon: GRID_MENU_ICONS.insertRows,
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
          icon: GRID_MENU_ICONS.insertChild,
          disabled: !canAddRows || activeView === null,
          action: () => this.insertChildAtActive(),
        });
      }
      items.push({
        id: 'deleteRows',
        label: translate('grid.menu.deleteRows', { count })(),
        icon: GRID_MENU_ICONS.deleteRows,
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

  /**
   * Actuates the active cell's first interactive projected child: Enter on
   * a readonly cell, or F2 under row activation.
   */
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

  /** The active cell's element, or `null` when none is active or rendered. */
  activeCellElement(): HTMLElement | null {
    const scroller = untracked(this.scrollerSignal);
    const active = untracked(() => this.engine.nav.activeCell());
    if (scroller === null || active === null) {
      return null;
    }
    return scroller.querySelector<HTMLElement>(cellSelector(active));
  }

  /** Focuses the active cell: now when it is rendered, and again after render. */
  requestFocusActive(): void {
    this.pendingGestureFocus = true;
    this.focusRequest.update((value) => value + 1);
    // Land focus SYNCHRONOUSLY when the active cell is already rendered — a
    // pointer gesture always lands on one, and an in-window keyboard move
    // keeps it rendered. The deferred roving-focus effect above would
    // otherwise not focus it until the next render, so a key (or clipboard
    // shortcut) fired right after the gesture would reach an unfocused grid
    // and be dropped. A move to a not-yet-rendered cell (paging past the
    // window) leaves the element null and falls through to that effect, which
    // also owns DOM-move focus reclaim. Skipped while escaped or while an
    // editor or a draft owns focus — the effect resolves those; this only
    // ever brings focus forward in time, never changes which cell it lands on.
    if (
      untracked(this.escapedSignal) ||
      (this.editing?.current() ?? null) !== null ||
      untracked(this.draft) !== null
    ) {
      return;
    }
    this.activeCellElement()?.focus({ preventScroll: true });
  }

  /** Scrolls the active cell into view after the next render. */
  private requestReveal(): void {
    this.revealRequest.update((value) => value + 1);
  }

  /**
   * Scrolls the active cell into view: vertical from the fixed row-height
   * math (accounting for the sticky header band), horizontal from rects
   * (proportional columns resolve in CSS, and rect deltas are direction-safe).
   */
  revealActiveCell(): void {
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
      // The rect is the BORDER box, so it counts the vertical scrollbar's
      // gutter as visible space — the last column would come to rest half
      // underneath it. Classic scrollbars take the inline-end edge (the
      // start edge under RTL); overlay scrollbars take none, and the
      // difference is then zero.
      const gutter = scroller.offsetWidth - scroller.clientWidth;
      const rtl = untracked(this.deps.direction) === 'rtl';
      const viewLeft = scrollerRect.left + (rtl ? gutter : 0);
      const viewRight = scrollerRect.right - (rtl ? 0 : gutter);
      if (cellRect.right > viewRight) {
        scroller.scrollLeft += cellRect.right - viewRight;
      }
      if (cellRect.left < viewLeft) {
        scroller.scrollLeft += cellRect.left - viewLeft;
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

  /**
   * The shared pointer-drag pipeline: pointer capture on the scroller,
   * document-level move/end listeners, hit-testing under the pointer, and
   * edge auto-scroll. With `click` handlers the press is a click until the
   * pointer travels past the click slop: no range extension (and no
   * `onDragStart`) before that, and a release still under it calls
   * `onClick` instead.
   */
  private beginDrag(
    event: PointerEvent,
    mode: 'cells' | 'rows' | 'cols',
    click?: { readonly onClick: () => void; readonly onDragStart?: () => void },
  ): void {
    const scroller = untracked(this.scrollerSignal);
    if (scroller === null) {
      return;
    }
    this.endDrag();
    this.pressLongPressed = false;
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
    // Without click handlers every press drags from the start.
    let dragging = click === undefined;

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
      if (!dragging) {
        if (Math.hypot(lastX - event.clientX, lastY - event.clientY) < CLICK_SLOP_PX) {
          return; // still a click
        }
        dragging = true;
        click?.onDragStart?.();
      }
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
      // A press that fired the long-press (a pen held still) asked for the
      // menu: its release is no click.
      if (!dragging && end.type === 'pointerup' && !this.pressLongPressed) {
        click?.onClick();
      }
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

    // The two-way active row (spec 0010 §C.2). Created after the reconcile
    // effect above, so a data change has remapped the active cell before
    // this compares it with the model. `synced` is the model value this
    // effect last left behind: a model that differs from it was written by
    // the consumer, and wins; otherwise the grid's own active row is
    // written out.
    let synced: TmRowId | null = null;
    effect(
      () => {
        const requested = this.deps.activeRowId();
        const active = this.engine.nav.activeCell();
        this.engine.model.viewRows();
        untracked(() => {
          const engine = this.engine;
          if (
            requested !== synced &&
            requested !== null &&
            engine.model.rowById(requested) !== undefined
          ) {
            // A consumer write: the active cell moves to that row, in the
            // current column (else the first), and scrolls into view —
            // focus stays wherever it is.
            if (engine.model.isTree) {
              engine.expandAncestorsOf(requested);
            }
            const row = engine.model.viewIndexOfRow(requested);
            if (row !== -1) {
              const target = { row, col: active?.col ?? 0 };
              engine.nav.setActive(target);
              engine.selection.collapseTo(target);
              this.requestReveal();
            }
          }
          // Write back the actual row: the grid's own change, or the
          // consumer's write as applied (an unknown id reads back as the
          // row that stayed active, `null` when there is none).
          const current = engine.nav.activeCell();
          const actual = current === null ? null : (engine.model.rowAt(current.row)?.id ?? null);
          synced = actual;
          if (requested !== actual) {
            this.deps.activeRowId.set(actual);
          }
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

    // The editing code preloads once the grid is editable, when the browser
    // is next idle, ahead of the first keystroke; a readonly grid never asks
    // for it.
    effect(
      () => {
        if (this.editable()) {
          untracked(() => this.preloadEditing());
        }
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
        const observer = new ResizeObserver(() => this.viewportHeight.set(scroller.clientHeight));
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
          // A pending gesture request is consumed by this render, whatever
          // happens next.
          const pending = this.pendingGestureFocus;
          this.pendingGestureFocus = false;
          const scroller = this.scrollerSignal();
          if (escaped || scroller === null || active === null) {
            return;
          }
          const droppedByDomMove =
            this.focusDroppedByRerender() && document.activeElement === document.body;
          const shouldFocus =
            pending || scroller.contains(document.activeElement) || droppedByDomMove;
          if (!shouldFocus) {
            return;
          }
          const editor = this.editing?.current()?.editor;
          if (editor !== undefined) {
            // An open editor owns focus. Only reclaim after a DOM-move drop
            // — and into the EDITOR, never the cell under it.
            if (droppedByDomMove) {
              editor.focus();
            }
            return;
          }
          // A draft's input owns focus the same way.
          const draftInput = untracked(this.draft) === null ? null : this.draftInput;
          if (draftInput !== null) {
            if (droppedByDomMove) {
              draftInput.focus({ preventScroll: true });
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
    // exempt (it must hold focus), and so is anything inside a cell-anchored
    // overlay — a top-layer surface renders in place, and its own roving
    // tabindex must survive this pass.
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
              !this.inOverlay(element) &&
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
        onCleanup(
          ɵtmObserveLongPress(scroller, (point) => this.onLongPress(point), {
            // A press on a link in a cell keeps the platform's own link
            // menu: no timer, no grid menu (spec 0010 §C.1).
            enabled: (press) => this.cellLink(press.target) === null,
          }),
        );
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
            this.announceCheckedCount();
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

  /**
   * Speaks the checked count. With ids checked outside the bound rows
   * (other pages of a list), it says both: how many are checked here and in
   * all.
   */
  private announceCheckedCount(): void {
    const selected = untracked(this.deps.selectedIds);
    const rows = untracked(this.rows);
    const rowIdOf = untracked(this.deps.rowId);
    let onPage = 0;
    for (const row of rows) {
      if (selected.has(rowIdOf(row))) {
        onPage += 1;
      }
    }
    if (onPage < selected.size) {
      this.announcements.announce('grid.announce.checkedCountAcross', {
        onPage,
        total: rows.length,
        selected: selected.size,
      });
    } else {
      this.announcements.announce('grid.announce.checkedCount', {
        selected: selected.size,
        total: rows.length,
      });
    }
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
    // The editing code's load and its preload end here, with no report.
    this.endLoad?.();
    this.cancelPreload?.();
    this.cancelPreload = null;
    this.arrivedCode = null;
    this.deferredLabels = [];
    this.draft.set(null);
    this.draftInput = null;
    this.editing?.destroy();
    if (this.handle !== null) {
      this.persistWidths();
      this.persistContentState();
      this.handle.release();
      this.handle = null;
    }
    this.engineInstance?.dispose();
  }
}
