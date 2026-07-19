// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, signal, untracked, type Signal } from '@angular/core';

import type {
  TmGridRangeSnapshot,
  TmGridSelectionSnapshot,
  TmRowId,
} from '@tellma/core-ui/contracts';

import type { TmGridDataModel, TmGridOrderSnapshot } from './tm-grid-data-model';
import type { TmGridRange, TmGridRect, TmRowCol } from './tm-grid-types';

/** Construction inputs of {@link TmGridSelectionModel}. */
export interface TmGridSelectionOptions {
  /** The data model (extents, identities, placeholder). */
  readonly model: TmGridDataModel;
}

/**
 * A copyable compaction of the selection: the distinct selected view rows ×
 * the distinct selected columns, both sorted — what a multi-range copy
 * exports when the ranges align into one rectangle.
 */
export interface TmGridCopyShape {
  /** The selected view-row indices, ascending, placeholder excluded. */
  readonly rows: readonly number[];
  /** The selected column indices, ascending. */
  readonly cols: readonly number[];
}

/**
 * The selection: an ordered list of rectangular ranges (the last one is the
 * active range) in view space. State is O(ranges) — a whole-grid selection
 * is one descriptor — and every membership question is answered from range
 * arithmetic, never per-cell bookkeeping.
 */
export class TmGridSelectionModel {
  private readonly model: TmGridDataModel;
  private readonly rangesSignal = signal<readonly TmGridRange[]>([]);

  /** The ranges, in creation order; the last is the active range. */
  readonly ranges: Signal<readonly TmGridRange[]>;
  /** The active range (the last one), or `null` when nothing is selected. */
  readonly activeRange: Signal<TmGridRange | null>;

  constructor(options: TmGridSelectionOptions) {
    this.model = options.model;
    this.ranges = this.rangesSignal.asReadonly();
    this.activeRange = computed(() => {
      const ranges = this.rangesSignal();
      return ranges.length > 0 ? ranges[ranges.length - 1] : null;
    });
  }

  /** Collapses the whole selection to one cell. */
  collapseTo(cell: TmRowCol): void {
    this.rangesSignal.set([{ anchor: cell, focus: cell, kind: 'cells' }]);
  }

  /** Clears the selection entirely. */
  clear(): void {
    this.rangesSignal.set([]);
  }

  /**
   * Moves the active range's focus (Shift+motion, Shift+Click, drag),
   * keeping its anchor and kind. Starts a fresh range when none exists.
   */
  extendActiveTo(cell: TmRowCol): void {
    const ranges = untracked(this.rangesSignal);
    if (ranges.length === 0) {
      this.rangesSignal.set([{ anchor: cell, focus: cell, kind: 'cells' }]);
      return;
    }
    const active = ranges[ranges.length - 1];
    this.rangesSignal.set([...ranges.slice(0, -1), { ...active, focus: cell }]);
  }

  /** Adds a discontiguous range (modifier click/drag); it becomes the active range. */
  addRange(range: TmGridRange): void {
    this.rangesSignal.set([...untracked(this.rangesSignal), range]);
  }

  /** Selects full rows (`additive` keeps the existing ranges). */
  selectRows(fromViewRow: number, toViewRow: number, additive: boolean): void {
    const lastCol = Math.max(0, untracked(() => this.model.columnCount()) - 1);
    const range: TmGridRange = {
      anchor: { row: fromViewRow, col: 0 },
      focus: { row: toViewRow, col: lastCol },
      kind: 'rows',
    };
    this.rangesSignal.set(additive ? [...untracked(this.rangesSignal), range] : [range]);
  }

  /** Selects full columns (`additive` keeps the existing ranges). */
  selectCols(fromCol: number, toCol: number, additive: boolean): void {
    const lastRow = Math.max(0, untracked(() => this.model.dataRowCount()) - 1);
    const range: TmGridRange = {
      anchor: { row: 0, col: fromCol },
      focus: { row: lastRow, col: toCol },
      kind: 'cols',
    };
    this.rangesSignal.set(additive ? [...untracked(this.rangesSignal), range] : [range]);
  }

  /** Selects everything — one descriptor regardless of grid size. */
  selectAll(): void {
    const lastRow = Math.max(0, untracked(() => this.model.dataRowCount()) - 1);
    const lastCol = Math.max(0, untracked(() => this.model.columnCount()) - 1);
    this.rangesSignal.set([
      { anchor: { row: 0, col: 0 }, focus: { row: lastRow, col: lastCol }, kind: 'all' },
    ]);
  }

  /** The normalized rectangle a range currently covers. */
  rectOf(range: TmGridRange): TmGridRect {
    const lastDataRow = Math.max(0, untracked(() => this.model.dataRowCount()) - 1);
    const lastCol = Math.max(0, untracked(() => this.model.columnCount()) - 1);
    const top = Math.min(range.anchor.row, range.focus.row);
    const bottom = Math.max(range.anchor.row, range.focus.row);
    // Columns have no reconcile of their own: clamp stored indices to the
    // current extent so a shrunk `columns` input can't hand copy an
    // out-of-range column (which then reads `undefined` and crashes).
    const left = Math.min(Math.min(range.anchor.col, range.focus.col), lastCol);
    const right = Math.min(Math.max(range.anchor.col, range.focus.col), lastCol);
    switch (range.kind) {
      case 'rows':
        return { top, bottom, left: 0, right: lastCol };
      case 'cols':
        return { top: 0, bottom: lastDataRow, left, right };
      case 'all':
        return { top: 0, bottom: lastDataRow, left: 0, right: lastCol };
      default:
        return { top, bottom, left, right };
    }
  }

  /** The normalized rectangles of every range, in range order. */
  rects(): readonly TmGridRect[] {
    return untracked(this.rangesSignal).map((range) => this.rectOf(range));
  }

  /** The active range's rectangle, or `null` when nothing is selected. */
  activeRect(): TmGridRect | null {
    const active = untracked(this.activeRange);
    return active === null ? null : this.rectOf(active);
  }

  /**
   * Whether the whole selection is a single cell — one range covering exactly
   * one cell. The range fill is suppressed in this case (only the active ring
   * shows), matching spreadsheets: a lone cell reads as a caret, not a range.
   */
  isSingleCellSelection(): boolean {
    const ranges = untracked(this.rangesSignal);
    if (ranges.length !== 1) {
      return false;
    }
    const rect = this.rectOf(ranges[0]);
    return rect.top === rect.bottom && rect.left === rect.right;
  }

  /** Whether a cell lies inside any range. */
  isCellSelected(cell: TmRowCol): boolean {
    return untracked(this.rangesSignal).some((range) => {
      const rect = this.rectOf(range);
      return (
        cell.row >= rect.top && cell.row <= rect.bottom && cell.col >= rect.left && cell.col <= rect.right
      );
    });
  }

  /** Whether any range covers the view row (row-header highlight). */
  rowIntersects(viewRow: number): boolean {
    return untracked(this.rangesSignal).some((range) => {
      const rect = this.rectOf(range);
      return viewRow >= rect.top && viewRow <= rect.bottom;
    });
  }

  /** Whether any range covers the column (column-header highlight). */
  colIntersects(col: number): boolean {
    return untracked(this.rangesSignal).some((range) => {
      const rect = this.rectOf(range);
      return col >= rect.left && col <= rect.right;
    });
  }

  /**
   * Compacts the selection for copy, per the spreadsheet alignment rule:
   * one range always compacts; several compact only when they share the
   * exact column span (stacked) or the exact row span (abreast) — the
   * result is the distinct selected rows × the distinct selected columns.
   * Misaligned selections return `null` (the copy is refused). The
   * placeholder row is excluded from the result.
   */
  compactForCopy(): TmGridCopyShape | null {
    const rects = this.rects();
    if (rects.length === 0) {
      return null;
    }
    const aligned =
      rects.length === 1 ||
      rects.every((rect) => rect.left === rects[0].left && rect.right === rects[0].right) ||
      rects.every((rect) => rect.top === rects[0].top && rect.bottom === rects[0].bottom);
    if (!aligned) {
      return null;
    }
    const rowSet = new Set<number>();
    const colSet = new Set<number>();
    const placeholderIndex = untracked(() => this.model.placeholderIndex());
    for (const rect of rects) {
      for (let row = rect.top; row <= rect.bottom; row++) {
        if (row !== placeholderIndex) {
          rowSet.add(row);
        }
      }
      for (let col = rect.left; col <= rect.right; col++) {
        colSet.add(col);
      }
    }
    return {
      rows: [...rowSet].sort((a, b) => a - b),
      cols: [...colSet].sort((a, b) => a - b),
    };
  }

  /**
   * The distinct view rows the selection spans, as sorted disjoint spans —
   * what row operations (insert/delete counts) act on. The placeholder row
   * is excluded.
   */
  rowsUnion(): ReadonlyArray<{ readonly start: number; readonly end: number }> {
    const placeholderIndex = untracked(() => this.model.placeholderIndex());
    const rows = new Set<number>();
    for (const rect of this.rects()) {
      for (let row = rect.top; row <= rect.bottom; row++) {
        if (row !== placeholderIndex) {
          rows.add(row);
        }
      }
    }
    const sorted = [...rows].sort((a, b) => a - b);
    const spans: Array<{ start: number; end: number }> = [];
    for (const row of sorted) {
      const last = spans[spans.length - 1];
      if (last !== undefined && row === last.end + 1) {
        last.end = row;
      } else {
        spans.push({ start: row, end: row });
      }
    }
    return spans;
  }

  /**
   * Remaps every range after the rows array changed, keying on row
   * identity: each endpoint follows its row's new view position; an
   * endpoint whose row vanished substitutes the nearest surviving row
   * inside the range's old span; a range with no surviving rows drops.
   */
  remap(before: TmGridOrderSnapshot): void {
    const ranges = untracked(this.rangesSignal);
    if (ranges.length === 0) {
      return;
    }
    const remapped: TmGridRange[] = [];
    for (const range of ranges) {
      if (range.kind === 'cols' || range.kind === 'all') {
        remapped.push(range); // extent-relative; rectOf re-derives the rows
        continue;
      }
      const top = Math.min(range.anchor.row, range.focus.row);
      const bottom = Math.max(range.anchor.row, range.focus.row);
      const anchorRow = this.remapRow(range.anchor.row, top, bottom, before);
      const focusRow = this.remapRow(range.focus.row, top, bottom, before);
      if (anchorRow === null || focusRow === null) {
        continue;
      }
      remapped.push({
        ...range,
        anchor: { row: anchorRow, col: range.anchor.col },
        focus: { row: focusRow, col: range.focus.col },
      });
    }
    this.rangesSignal.set(remapped);
  }

  /**
   * Persists the selection by identity (with the active cell) for the state
   * store. `order` resolves view rows to row ids through a specific snapshot
   * — a content switch persists the OUTGOING selection while the rows array
   * has already swapped, so the current model would resolve foreign ids.
   */
  toSnapshot(activeCell: TmRowCol | null, order?: TmGridOrderSnapshot): TmGridSelectionSnapshot {
    const rowIdAt =
      order === undefined
        ? (viewRow: number): TmRowId | null => this.rowIdAt(viewRow)
        : (viewRow: number): TmRowId | null => order.visibleIds[viewRow] ?? null;
    const ranges = untracked(this.rangesSignal).map((range): TmGridRangeSnapshot => {
      const rowless = range.kind === 'cols' || range.kind === 'all';
      const colless = range.kind === 'rows' || range.kind === 'all';
      return {
        anchorRowId: rowless ? null : rowIdAt(range.anchor.row),
        focusRowId: rowless ? null : rowIdAt(range.focus.row),
        anchorColumnKey: colless ? null : this.columnIdAt(range.anchor.col),
        focusColumnKey: colless ? null : this.columnIdAt(range.focus.col),
        kind: range.kind,
      };
    });
    return {
      ranges,
      activeRowId: activeCell === null ? null : rowIdAt(activeCell.row),
      activeColumnKey: activeCell === null ? null : this.columnIdAt(activeCell.col),
      ...(activeCell === null ? {} : { activeViewRow: activeCell.row }),
    };
  }

  /**
   * Restores a persisted selection. The ranges restore all-or-nothing: if
   * any range endpoint fails to resolve against the current content, the
   * ranges are dropped (`restored: false`). The active cell resolves
   * independently — the caller applies its own fallback chain when it comes
   * back `null`.
   */
  restore(snapshot: TmGridSelectionSnapshot): { restored: boolean; activeCell: TmRowCol | null } {
    let activeRow = this.resolveRow(snapshot.activeRowId);
    if (activeRow === null && snapshot.activeViewRow !== undefined) {
      // The row id no longer resolves: fall back to the persisted view
      // position, clamped to the DATA rows present now (the placeholder is
      // a landing spot only when no data row exists).
      const dataRows = untracked(() => this.model.dataRowCount());
      const bound = dataRows > 0 ? dataRows : untracked(() => this.model.viewRowCount());
      if (bound > 0) {
        activeRow = Math.min(Math.max(0, snapshot.activeViewRow), bound - 1);
      }
    }
    let activeCol = this.resolveCol(snapshot.activeColumnKey);
    if (activeCol === null && activeRow !== null && untracked(() => this.model.columnCount()) > 0) {
      activeCol = 0;
    }
    const active =
      activeRow !== null && activeCol !== null ? { row: activeRow, col: activeCol } : null;
    const ranges: TmGridRange[] = [];
    for (const persisted of snapshot.ranges) {
      const rowless = persisted.kind === 'cols' || persisted.kind === 'all';
      const colless = persisted.kind === 'rows' || persisted.kind === 'all';
      const anchorRow = rowless ? 0 : this.resolveRow(persisted.anchorRowId);
      const focusRow = rowless ? 0 : this.resolveRow(persisted.focusRowId);
      const anchorCol = colless ? 0 : this.resolveCol(persisted.anchorColumnKey);
      const focusCol = colless ? 0 : this.resolveCol(persisted.focusColumnKey);
      if (anchorRow === null || focusRow === null || anchorCol === null || focusCol === null) {
        return { restored: false, activeCell: active };
      }
      ranges.push({
        anchor: { row: anchorRow, col: anchorCol },
        focus: { row: focusRow, col: focusCol },
        kind: persisted.kind,
      });
    }
    this.rangesSignal.set(ranges);
    return { restored: true, activeCell: active };
  }

  // ---- internals ----

  private remapRow(
    oldRow: number,
    oldTop: number,
    oldBottom: number,
    before: TmGridOrderSnapshot,
  ): number | null {
    const direct = this.newIndexOfOldRow(oldRow, before);
    if (direct !== null) {
      return direct;
    }
    // The endpoint's row vanished: search the old span inward-then-outward
    // for the nearest surviving row.
    for (let distance = 1; distance <= oldBottom - oldTop; distance++) {
      for (const candidate of [oldRow - distance, oldRow + distance]) {
        if (candidate >= oldTop && candidate <= oldBottom) {
          const found = this.newIndexOfOldRow(candidate, before);
          if (found !== null) {
            return found;
          }
        }
      }
    }
    return null;
  }

  private newIndexOfOldRow(oldRow: number, before: TmGridOrderSnapshot): number | null {
    const id = before.visibleIds[oldRow];
    if (id === undefined) {
      return null;
    }
    const index = this.model.viewIndexOfRow(id);
    return index === -1 ? null : index;
  }

  private rowIdAt(viewRow: number): TmRowId | null {
    return this.model.rowAt(viewRow)?.id ?? null;
  }

  private columnIdAt(col: number): string | null {
    return this.model.columnAt(col)?.id ?? null;
  }

  private resolveRow(rowId: TmRowId | null): number | null {
    if (rowId === null) {
      return null;
    }
    const index = this.model.viewIndexOfRow(rowId);
    return index === -1 ? null : index;
  }

  private resolveCol(columnId: string | null): number | null {
    if (columnId === null) {
      return null;
    }
    const index = this.model.columnIndexOf(columnId);
    return index === -1 ? null : index;
  }
}
