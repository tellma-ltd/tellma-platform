// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signal, untracked, type Signal } from '@angular/core';

import type { SignalLike } from '@tellma/core-ui/contracts';

import type { TmGridDataModel } from './tm-grid-data-model';
import type { TmGridMotion, TmRowCol } from './tm-grid-types';

/** Construction inputs of {@link TmGridNav}. */
export interface TmGridNavOptions {
  /** The data model (extents, placeholder). */
  readonly model: TmGridDataModel;
  /** The reading direction — physical arrows map through it. */
  readonly direction: SignalLike<'ltr' | 'rtl'>;
  /** Rows per viewport page (PageUp/PageDown size). */
  readonly pageSize: SignalLike<number>;
  /**
   * Whether a cell reads as empty for data-edge jumps — display-text
   * emptiness, including any invalid-input raw text overlay.
   */
  cellIsEmpty(cell: TmRowCol): boolean;
  /** Whether a cell is a Tab target (editable right now). */
  cellIsEditable(cell: TmRowCol): boolean;
}

/**
 * The active cell and its motion semantics: plain moves, data-edge jumps,
 * paging, row/grid extremes, the Tab traversal over editable cells, and the
 * Enter line-entry flow that returns to a Tab run's origin column.
 *
 * Positions are view-space. The placeholder row is navigable by plain
 * motion but excluded from data-edge jumps; `target` never returns an
 * out-of-range cell (motions clamp at the extents).
 */
export class TmGridNav {
  private readonly model: TmGridDataModel;
  private readonly options: TmGridNavOptions;

  private readonly activeCellSignal = signal<TmRowCol | null>(null);
  /** Where the current Tab run began, or `null` outside a run. */
  private tabRunOrigin: TmRowCol | null = null;

  /** The active cell, or `null` while the grid holds no activity. */
  readonly activeCell: Signal<TmRowCol | null>;

  constructor(options: TmGridNavOptions) {
    this.options = options;
    this.model = options.model;
    this.activeCell = this.activeCellSignal.asReadonly();
  }

  /** The Tab run's origin column, or `null` outside a run. */
  get tabRunOriginCol(): number | null {
    return this.tabRunOrigin?.col ?? null;
  }

  /**
   * Activates a cell (clamped to the current extents; `null` deactivates).
   * Any explicit activation ends a Tab run unless `keepTabRun` is set by
   * the Tab/Enter flows themselves.
   */
  setActive(cell: TmRowCol | null, opts?: { keepTabRun?: boolean }): void {
    if (!opts?.keepTabRun) {
      this.tabRunOrigin = null;
    }
    this.activeCellSignal.set(cell === null ? null : this.clamp(cell));
  }

  /** Re-clamps the active cell after the extents changed (reconcile). */
  reclamp(): void {
    const active = untracked(this.activeCellSignal);
    if (active === null) {
      return;
    }
    if (untracked(() => this.model.viewRowCount()) === 0 || untracked(() => this.model.columnCount()) === 0) {
      this.activeCellSignal.set(null);
      return;
    }
    const clamped = this.clamp(active);
    if (clamped.row !== active.row || clamped.col !== active.col) {
      this.activeCellSignal.set(clamped);
    }
  }

  /**
   * Computes the destination of a motion from a cell — no state change.
   * `jump` applies the data-edge semantics to the four arrow motions: from
   * inside a contiguous data run, its far edge; from its edge or from an
   * empty cell, the start of the next run; with nothing further, the extent.
   */
  target(motion: TmGridMotion, from: TmRowCol, jump: boolean): TmRowCol {
    const start = this.clamp(from);
    const lastRow = this.lastRowIndex();
    const lastCol = this.lastColIndex();
    switch (motion) {
      case 'rowStart':
        return { row: start.row, col: 0 };
      case 'rowEnd':
        return { row: start.row, col: lastCol };
      case 'gridStart':
        return { row: 0, col: 0 };
      case 'gridEnd':
        return { row: this.lastDataOrPlaceholderRow(), col: lastCol };
      case 'pageUp':
        return { row: Math.max(0, start.row - this.page()), col: start.col };
      case 'pageDown':
        return { row: Math.min(lastRow, start.row + this.page()), col: start.col };
      default: {
        const delta = this.arrowDelta(motion);
        return jump ? this.jumpTarget(start, delta) : this.stepTarget(start, delta);
      }
    }
  }

  /**
   * The Tab traversal: the next (or previous) editable cell in row-major
   * order, wrapping across rows — the placeholder row included. Returns
   * `'exit'` past the grid's last (or first) editable cell, and `null`
   * while no cell is active. Starts or extends the Tab run.
   */
  tab(backward: boolean): TmRowCol | 'exit' | null {
    const active = untracked(this.activeCellSignal);
    if (active === null) {
      return null;
    }
    const rowCount = untracked(() => this.model.viewRowCount());
    const colCount = untracked(() => this.model.columnCount());
    if (rowCount === 0 || colCount === 0) {
      return 'exit';
    }
    const step = backward ? -1 : 1;
    let { row, col } = this.clamp(active);
    for (;;) {
      col += step;
      if (col < 0) {
        row -= 1;
        col = colCount - 1;
      } else if (col >= colCount) {
        row += 1;
        col = 0;
      }
      if (row < 0 || row >= rowCount) {
        this.tabRunOrigin = null;
        return 'exit';
      }
      if (this.options.cellIsEditable({ row, col })) {
        this.tabRunOrigin ??= this.clamp(active);
        return { row, col };
      }
    }
  }

  /**
   * The Enter advance: one row down (or up), same column — except after a
   * Tab run, where Enter returns to the run's origin column on the row
   * below the run's origin (the spreadsheet line-entry flow) and ends the
   * run. Returns `null` while no cell is active.
   */
  enterTarget(backward: boolean): TmRowCol | null {
    const active = untracked(this.activeCellSignal);
    if (active === null) {
      return null;
    }
    const lastRow = this.lastRowIndex();
    const origin = this.tabRunOrigin;
    if (origin !== null && !backward) {
      this.tabRunOrigin = null;
      return this.clamp({ row: Math.min(lastRow, origin.row + 1), col: origin.col });
    }
    this.tabRunOrigin = null;
    const row = backward ? Math.max(0, active.row - 1) : Math.min(lastRow, active.row + 1);
    return { row, col: active.col };
  }

  /** Ends the Tab run (non-Tab motion, click, Esc, editor cancel). */
  resetTabRun(): void {
    this.tabRunOrigin = null;
  }

  /**
   * Remaps the Tab-run origin's row after a reconcile that preserved the run,
   * or ends the run when the origin's row no longer resolves — so Enter after
   * a background data change returns to the right line, not a stale view row.
   */
  remapTabRun(resolveRow: (oldViewRow: number) => number): void {
    if (this.tabRunOrigin === null) {
      return;
    }
    const next = resolveRow(this.tabRunOrigin.row);
    if (next === -1) {
      this.tabRunOrigin = null;
    } else if (next !== this.tabRunOrigin.row) {
      this.tabRunOrigin = { row: next, col: this.tabRunOrigin.col };
    }
  }

  // ---- internals ----

  private page(): number {
    return Math.max(1, Math.floor(untracked(() => this.options.pageSize())));
  }

  private lastRowIndex(): number {
    return Math.max(0, untracked(() => this.model.viewRowCount()) - 1);
  }

  private lastColIndex(): number {
    return Math.max(0, untracked(() => this.model.columnCount()) - 1);
  }

  /**
   * The grid-end row: the last data row; with no data rows, index 0 (the
   * placeholder when present, the clamp floor otherwise).
   */
  private lastDataOrPlaceholderRow(): number {
    const dataRows = untracked(() => this.model.dataRowCount());
    return dataRows > 0 ? dataRows - 1 : 0;
  }

  private clamp(cell: TmRowCol): TmRowCol {
    return {
      row: Math.min(Math.max(0, cell.row), this.lastRowIndex()),
      col: Math.min(Math.max(0, cell.col), this.lastColIndex()),
    };
  }

  /** Physical/logical arrow → view-space row/col delta. */
  private arrowDelta(motion: TmGridMotion): TmRowCol {
    const rtl = untracked(() => this.options.direction()) === 'rtl';
    switch (motion) {
      case 'up':
        return { row: -1, col: 0 };
      case 'down':
        return { row: 1, col: 0 };
      case 'inlineStart':
        return { row: 0, col: -1 };
      case 'inlineEnd':
        return { row: 0, col: 1 };
      case 'left':
        return { row: 0, col: rtl ? 1 : -1 };
      case 'right':
        return { row: 0, col: rtl ? -1 : 1 };
      default:
        return { row: 0, col: 0 };
    }
  }

  private stepTarget(from: TmRowCol, delta: TmRowCol): TmRowCol {
    return this.clamp({ row: from.row + delta.row, col: from.col + delta.col });
  }

  /**
   * Data-edge jump along one axis. The search domain excludes the
   * placeholder row for vertical motion — a jump can land on it only by
   * already standing there.
   */
  private jumpTarget(from: TmRowCol, delta: TmRowCol): TmRowCol {
    if (delta.row === 0 && delta.col === 0) {
      return from;
    }
    const vertical = delta.row !== 0;
    const lastData = untracked(() => this.model.dataRowCount()) - 1;
    const limit = vertical
      ? delta.row < 0
        ? 0
        : lastData
      : delta.col < 0
        ? 0
        : this.lastColIndex();
    const position = vertical ? from.row : from.col;
    const step = vertical ? delta.row : delta.col;
    const at = (index: number): TmRowCol =>
      vertical ? { row: index, col: from.col } : { row: from.row, col: index };
    // Vertical jumps from the placeholder (or past the data extent) search
    // from the data edge as if standing on an empty cell.
    if (vertical && position > lastData) {
      if (step > 0) {
        return from;
      }
      if (lastData < 0) {
        return from;
      }
      const found = this.scanForContent(lastData, -1, 0, at);
      return found !== null ? at(found) : at(0);
    }
    if (lastData < 0 && vertical) {
      return from;
    }
    if (position === limit) {
      return at(limit);
    }
    const selfEmpty = this.options.cellIsEmpty(at(position));
    const nextEmpty = this.options.cellIsEmpty(at(position + step));
    if (!selfEmpty && !nextEmpty) {
      // Inside a run: move to its far edge.
      let index = position + step;
      while (index !== limit && !this.options.cellIsEmpty(at(index + step))) {
        index += step;
      }
      return at(index);
    }
    // At a run's edge or on empty ground: the start of the next run, else the extent.
    const found = this.scanForContent(position + step, step, limit, at);
    return found !== null ? at(found) : at(limit);
  }

  /** First non-empty index from `start` toward `limit` (inclusive), else null. */
  private scanForContent(
    start: number,
    step: number,
    limit: number,
    at: (index: number) => TmRowCol,
  ): number | null {
    for (let index = start; step > 0 ? index <= limit : index >= limit; index += step) {
      if (!this.options.cellIsEmpty(at(index))) {
        return index;
      }
    }
    return null;
  }
}
