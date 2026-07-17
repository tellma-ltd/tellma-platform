// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, signal, untracked, type Signal } from '@angular/core';

import type { TmRowId } from '@tellma/core-ui/contracts';

import type { TmGridDataModel } from './tm-grid-data-model';

/**
 * Why a cell holds an invalid input instead of a value. `resolutionFailed`
 * is distinct from `notFound`: the resolver rejected (a transient failure),
 * so the label was never actually checked — the message invites a retry.
 */
export type TmGridInvalidInputReason = 'parse' | 'notFound' | 'ambiguous' | 'resolutionFailed';

/**
 * A raw text the grid holds for a cell whose content could not be turned
 * into a value. The text stays displayed in place (error-styled) while the
 * model field holds the column's cleared value.
 */
export interface TmGridInvalidInput {
  /** The rejected raw text, as typed or pasted. */
  readonly rawText: string;
  /** Why it was rejected. */
  readonly reason: TmGridInvalidInputReason;
}

/** A cell address by identity: row id + column id. */
export interface TmGridCellRef {
  /** The row's stable identity. */
  readonly rowId: TmRowId;
  /** The column's stable identity. */
  readonly columnId: string;
}

function cellKey(rowId: TmRowId, columnId: string): string {
  return `${typeof rowId === 'number' ? '#' : '$'}${String(rowId)} ${columnId}`;
}

/**
 * Per-cell bookkeeping that lives beside the data: invalid inputs (raw
 * texts that failed parse or resolution), pending async resolutions, and
 * the per-cell sequence tokens that guard against a late resolution
 * overwriting a newer write.
 */
export class TmGridCellAnnotations {
  private readonly invalidMap = signal<ReadonlyMap<string, TmGridInvalidInput & TmGridCellRef>>(
    new Map(),
  );
  private readonly pendingMap = signal<ReadonlyMap<string, TmGridCellRef>>(new Map());
  /** Per-cell sequence tokens, nested by row id so a delete prunes in O(1). */
  private readonly cellTokens = new Map<TmRowId, Map<string, number>>();
  /** Per-row structural tokens: one bump invalidates every cell of the row. */
  private readonly rowTokens = new Map<TmRowId, number>();
  private tokenCounter = 0;

  /** Count of cells holding an invalid input. */
  readonly invalidCount: Signal<number> = computed(() => this.invalidMap().size);
  /** Count of cells awaiting an async resolution. */
  readonly pendingCount: Signal<number> = computed(() => this.pendingMap().size);

  /** The invalid input held for a cell, if any. */
  invalidInput(rowId: TmRowId, columnId: string): TmGridInvalidInput | undefined {
    return this.invalidMap().get(cellKey(rowId, columnId));
  }

  /** Whether a cell awaits an async resolution. */
  isPending(rowId: TmRowId, columnId: string): boolean {
    return this.pendingMap().has(cellKey(rowId, columnId));
  }

  /** Every cell currently holding an invalid input, in insertion order. */
  invalidCells(): readonly TmGridCellRef[] {
    return [...this.invalidMap().values()].map(({ rowId, columnId }) => ({ rowId, columnId }));
  }

  /**
   * Bumps the cell's sequence token — every write to a cell does this, so
   * an async resolution issued before the write can recognize itself as
   * stale. Returns the new token.
   */
  bumpToken(rowId: TmRowId, columnId: string): number {
    const token = ++this.tokenCounter;
    let row = this.cellTokens.get(rowId);
    if (row === undefined) {
      row = new Map<string, number>();
      this.cellTokens.set(rowId, row);
    }
    row.set(columnId, token);
    return token;
  }

  /**
   * Bumps a whole row's structural token — one entry, consulted alongside
   * each cell token — so a row move or deletion invalidates every pending
   * resolution on the row without writing a token per column (which grew the
   * map by rowCount × columnCount on every delete).
   */
  bumpRowToken(rowId: TmRowId): number {
    const token = ++this.tokenCounter;
    this.rowTokens.set(rowId, token);
    return token;
  }

  /** The cell's current sequence token (0 before any write). */
  currentToken(rowId: TmRowId, columnId: string): number {
    return Math.max(
      this.cellTokens.get(rowId)?.get(columnId) ?? 0,
      this.rowTokens.get(rowId) ?? 0,
    );
  }

  /** Sets or clears (`null`) a cell's invalid input. */
  setInvalid(rowId: TmRowId, columnId: string, entry: TmGridInvalidInput | null): void {
    const key = cellKey(rowId, columnId);
    const current = untracked(this.invalidMap);
    if (entry === null) {
      if (!current.has(key)) {
        return;
      }
      const next = new Map(current);
      next.delete(key);
      this.invalidMap.set(next);
      return;
    }
    const next = new Map(current);
    next.set(key, { rowId, columnId, rawText: entry.rawText, reason: entry.reason });
    this.invalidMap.set(next);
  }

  /** Marks or unmarks a cell as awaiting an async resolution. */
  setPending(rowId: TmRowId, columnId: string, pending: boolean): void {
    const key = cellKey(rowId, columnId);
    const current = untracked(this.pendingMap);
    if (current.has(key) === pending) {
      return;
    }
    const next = new Map(current);
    if (pending) {
      next.set(key, { rowId, columnId });
    } else {
      next.delete(key);
    }
    this.pendingMap.set(next);
  }

  /**
   * Drops invalid-input and pending entries whose row or column no longer
   * exists — called after an external data change reconciles.
   */
  prune(model: Pick<TmGridDataModel, 'modelIndexOfRow' | 'columnIndexOf'>): void {
    const invalid = untracked(this.invalidMap);
    const survivingInvalid = new Map(
      [...invalid].filter(
        ([, entry]) =>
          model.modelIndexOfRow(entry.rowId) !== -1 && model.columnIndexOf(entry.columnId) !== -1,
      ),
    );
    if (survivingInvalid.size !== invalid.size) {
      this.invalidMap.set(survivingInvalid);
    }
    const pending = untracked(this.pendingMap);
    const survivingPending = new Map(
      [...pending].filter(
        ([, entry]) =>
          model.modelIndexOfRow(entry.rowId) !== -1 && model.columnIndexOf(entry.columnId) !== -1,
      ),
    );
    if (survivingPending.size !== pending.size) {
      this.pendingMap.set(survivingPending);
    }
    // Sequence tokens: drop vanished rows entirely (and vanished columns
    // within surviving rows) so the maps stay bounded across many deletes.
    for (const rowId of [...this.cellTokens.keys()]) {
      if (model.modelIndexOfRow(rowId) === -1) {
        this.cellTokens.delete(rowId);
        continue;
      }
      const columns = this.cellTokens.get(rowId)!;
      for (const columnId of [...columns.keys()]) {
        if (model.columnIndexOf(columnId) === -1) {
          columns.delete(columnId);
        }
      }
      if (columns.size === 0) {
        this.cellTokens.delete(rowId);
      }
    }
    for (const rowId of [...this.rowTokens.keys()]) {
      if (model.modelIndexOfRow(rowId) === -1) {
        this.rowTokens.delete(rowId);
      }
    }
  }
}
