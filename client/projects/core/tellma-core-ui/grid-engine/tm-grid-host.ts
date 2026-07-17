// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { SignalLike, TmRowId } from '@tellma/core-ui/contracts';

import type { TmGridEngineColumn, TmGridRange, TmGridTreeOptions, TmRowCol } from './tm-grid-types';

/**
 * The write primitives the engine mutates the bound data through. The
 * component layer implements them over the consumer's array or field tree
 * (cell writes go through the child field so validators run); the engine
 * itself never touches the rows it is handed.
 */
export interface TmGridModelWriter<T = unknown> {
  /** Writes one cell value, addressed by row identity and column key. */
  setCellValue(rowId: TmRowId, columnKey: string, value: unknown): void;
  /**
   * Creates `count` new rows via the consumer's row factory and inserts
   * them at `modelIndex`. `parentRowId` (trees) is handed to the factory so
   * it can stamp the parent id. Returns the created rows with their ids.
   */
  insertNewRows(
    modelIndex: number,
    count: number,
    parentRowId?: TmRowId | null,
  ): ReadonlyArray<{ readonly id: TmRowId; readonly row: T }>;
  /**
   * Re-inserts previously removed row objects at their recorded model
   * indexes (ascending, clamped to the array) — the undo of a delete and
   * the redo of an insert, which must restore the exact objects rather
   * than mint new rows.
   */
  reinsertRows(rows: ReadonlyArray<{ readonly row: T; readonly modelIndex: number }>): void;
  /** Removes the rows with the given ids. */
  removeRows(rowIds: readonly TmRowId[]): void;
  /**
   * Moves existing rows: re-splices them, in the order given, immediately
   * before the row `beforeRowId` (`null` appends at the end). Order only —
   * tree re-parenting is a separate `setCellValue` write of the parent-id
   * key, so it inverts through the normal write path.
   */
  moveRows(rowIds: readonly TmRowId[], beforeRowId: TmRowId | null): void;
}

/**
 * A semantic event the component layer localizes and announces. The engine
 * carries no strings — only facts.
 */
export type TmGridNotice =
  | { readonly kind: 'copyRefusedMisaligned' }
  | {
      readonly kind: 'pasteComplete';
      readonly cells: number;
      readonly errors: number;
      readonly pending: number;
      readonly rowsMaterialized: number;
      readonly rowsDropped: number;
    }
  | { readonly kind: 'resolutionComplete'; readonly resolved: number; readonly errors: number }
  | { readonly kind: 'undoApplied'; readonly opKind: TmGridOpKind; readonly skippedRows: number }
  | { readonly kind: 'redoApplied'; readonly opKind: TmGridOpKind; readonly skippedRows: number }
  | { readonly kind: 'undoSkippedMissingRows' }
  | { readonly kind: 'redoSkippedMissingRows' }
  | { readonly kind: 'moveIntoDescendantRejected' }
  | { readonly kind: 'editorCancelledRowRemoved' }
  | { readonly kind: 'rowsInserted'; readonly count: number }
  | { readonly kind: 'rowsDeleted'; readonly count: number }
  | { readonly kind: 'rowsMoved'; readonly count: number };

/** The op kinds the history stack records (used in undo/redo announcements). */
export type TmGridOpKind =
  | 'cellEdit'
  | 'clear'
  | 'paste'
  | 'fillDown'
  | 'cutMove'
  | 'rowInsert'
  | 'rowDelete'
  | 'rowMove'
  | 'transaction';

/** A data irregularity the component layer surfaces as a dev-mode warning. */
export type TmGridWarning =
  | { readonly kind: 'duplicateRowId'; readonly rowId: TmRowId }
  | { readonly kind: 'orphanParent'; readonly rowId: TmRowId }
  | { readonly kind: 'parentCycle'; readonly rowId: TmRowId }
  | { readonly kind: 'transactionRowMissing'; readonly rowId: TmRowId };

/** Where the component layer should scroll/activate after an engine-driven restore. */
export interface TmGridRevealTarget {
  /** The cell to activate and scroll into view. */
  readonly cell: TmRowCol;
  /** The range to re-select, when the restored operation had one. */
  readonly range?: TmGridRange;
}

/** The component-layer callbacks the engine reports through. */
export interface TmGridEngineHost<T = unknown> {
  /** The data writer; absent for a readonly data binding. */
  readonly writer?: TmGridModelWriter<T>;
  /** Semantic events to localize and announce. */
  onNotice?(notice: TmGridNotice): void;
  /** Scroll/activate after undo/redo restores or programmatic reveals. */
  onReveal?(target: TmGridRevealTarget): void;
  /** Data irregularities to surface as dev-mode warnings. */
  onWarn?(warning: TmGridWarning): void;
}

/** Everything the engine needs from the component layer, as reactive inputs. */
export interface TmGridEngineOptions<T = unknown> {
  /** The bound rows, in model order. */
  readonly rows: SignalLike<readonly T[]>;
  /** Reads a row's stable identity. */
  rowId(row: T): TmRowId;
  /** The data columns, in display order. */
  readonly columns: SignalLike<ReadonlyArray<TmGridEngineColumn<T>>>;
  /** Whether the grid is editable (a field is bound and readonly is off). */
  readonly editable: SignalLike<boolean>;
  /** Whether new rows can be created (a row factory is bound). */
  readonly canAddRows: SignalLike<boolean>;
  /** The active locale (clipboard metadata + parse context). */
  readonly locale: SignalLike<string>;
  /** The tenant identity (clipboard metadata + cross-tenant paste guard). */
  readonly tenant?: SignalLike<string | undefined>;
  /** The reading direction — physical arrow keys map through it. */
  readonly direction: SignalLike<'ltr' | 'rtl'>;
  /** Rows per viewport page (PageUp/PageDown motion size). */
  readonly pageSize: SignalLike<number>;
  /** Tree configuration; absent for the flat grid. */
  readonly tree?: TmGridTreeOptions<T>;
  /** The component-layer callbacks. */
  readonly host: TmGridEngineHost<T>;
  /** Undo-stack depth cap. Defaults to 100. */
  readonly historyCapacity?: number;
}
