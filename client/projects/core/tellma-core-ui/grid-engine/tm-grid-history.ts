// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, signal, type Signal } from '@angular/core';

import type { TmCellEdit, TmRowId } from '@tellma/core-ui/contracts';

import type { TmGridCellAnnotations, TmGridInvalidInput } from './tm-grid-cell-annotations';
import type { TmGridDataModel } from './tm-grid-data-model';
import type { TmGridEngineHost, TmGridModelWriter, TmGridOpKind } from './tm-grid-host';

/**
 * One cell mutation as the history records it: the model write and the
 * invalid-input transition, both invertible.
 */
export interface TmGridCellWrite {
  /** The row's stable identity. */
  readonly rowId: TmRowId;
  /** The column's stable identity (annotation bookkeeping). */
  readonly columnId: string;
  /** The model property the write targets. */
  readonly columnKey: string;
  /** The value before the write (the undo value). */
  readonly before: unknown;
  /** The value the write sets. */
  readonly after: unknown;
  /** The cell's invalid input before the write, or `null`. */
  readonly invalidBefore: TmGridInvalidInput | null;
  /** The cell's invalid input after the write, or `null`. */
  readonly invalidAfter: TmGridInvalidInput | null;
}

/** A removed/inserted row as the history snapshots it. */
export interface TmGridRowSnapshot<T = unknown> {
  /** The exact row object (restored by reference on undo/redo). */
  readonly row: T;
  /** The row's identity. */
  readonly id: TmRowId;
  /** The row's model index at snapshot time. */
  readonly modelIndex: number;
}

interface HistoryEntry<T> {
  kind: TmGridOpKind;
  /** Cell writes, in application order. */
  writes: TmGridCellWrite[];
  /** Rows this op created (paste overflow, placeholder, row inserts). */
  insertedRows: TmGridRowSnapshot<T>[];
  /** Rows this op removed, ascending model index. */
  removedRows: TmGridRowSnapshot<T>[];
  /** Invalid inputs that were held by removed rows (restored on undo). */
  removedInvalid: ReadonlyArray<{ rowId: TmRowId; columnId: string; entry: TmGridInvalidInput }>;
  /** Row re-ordering (row moves): forward target + pre-move positions. */
  move: {
    rowIds: readonly TmRowId[];
    beforeRowId: TmRowId | null;
    snapshots: readonly TmGridRowSnapshot<T>[];
  } | null;
  /** Open while a compound op (paste) still awaits async resolutions. */
  open: boolean;
  /** Cancels outstanding async work when an open entry is undone. */
  onCancel: (() => void) | null;
}

/**
 * An open compound operation: everything added through the handle belongs
 * to one history entry (one undo), even work that lands asynchronously.
 */
export interface TmGridCompoundHandle {
  /** Executes and records cell writes as part of the compound. */
  applyWrites(writes: readonly TmGridCellWrite[]): void;
  /** Creates rows via the row factory as part of the compound. */
  insertRows(
    modelIndex: number,
    count: number,
    parentRowId?: TmRowId | null,
  ): readonly TmGridRowSnapshot[];
  /** Records the compound's row-move component (already-validated inputs). */
  setMove(rowIds: readonly TmRowId[], beforeRowId: TmRowId | null): void;
  /** Registers the canceler run when the open compound is undone. */
  onCancel(cancel: () => void): void;
  /** Closes the compound: later work must go through new entries. */
  finalize(): void;
  /** Whether the compound is still open. */
  readonly isOpen: boolean;
}

/** Where undo/redo landed, for the reveal/re-select behavior. */
export interface TmGridHistoryReveal {
  /** The affected row ids (ancestors are expanded before revealing). */
  readonly rowIds: readonly TmRowId[];
  /** The affected column ids (empty for purely structural ops). */
  readonly columnIds: readonly string[];
}

/**
 * An opaque undo/redo-stack snapshot for the grid state store. Held in
 * memory only; its internal shape is not part of any contract.
 */
export interface TmGridHistorySnapshot {
  /** Snapshot format discriminator. */
  readonly version: 1;
}

interface SnapshotInternals<T> extends TmGridHistorySnapshot {
  readonly undoStack: readonly HistoryEntry<T>[];
  readonly redoStack: readonly HistoryEntry<T>[];
}

/** Construction inputs of {@link TmGridHistory}. */
export interface TmGridHistoryOptions<T = unknown> {
  /** The data model (existence checks, reveal computation). */
  readonly model: TmGridDataModel<T>;
  /** The annotation store (invalid inputs, sequence tokens). */
  readonly annotations: TmGridCellAnnotations;
  /** The data writer; absent in readonly mode (every run is then a no-op). */
  readonly writer?: TmGridModelWriter<T>;
  /** Component-layer callbacks (undo/redo notices, warnings). */
  readonly host?: Pick<TmGridEngineHost<T>, 'onNotice' | 'onWarn'>;
  /** Called after undo/redo applies, with the affected identities. */
  onReveal?(reveal: TmGridHistoryReveal, direction: 'undo' | 'redo'): void;
  /** Stack depth cap. Defaults to 100. */
  readonly capacity?: number;
}

const DEFAULT_CAPACITY = 100;

/**
 * The undo/redo stack of committed, data-mutating operations. Every
 * mutation the engine performs flows through here: the history executes the
 * writes through the model writer, records the inverse keyed by row
 * identity, and replays either direction on demand. Entries whose rows no
 * longer exist apply to the surviving subset (a skip is reported); view
 * state (widths, scroll, expansion) never enters the stack.
 */
export class TmGridHistory<T = unknown> {
  private readonly options: TmGridHistoryOptions<T>;
  private readonly capacity: number;
  private undoStack: HistoryEntry<T>[] = [];
  private redoStack: HistoryEntry<T>[] = [];
  private readonly version = signal(0);

  /** Whether an undo target exists. */
  readonly canUndo: Signal<boolean>;
  /** Whether a redo target exists. */
  readonly canRedo: Signal<boolean>;

  constructor(options: TmGridHistoryOptions<T>) {
    this.options = options;
    this.capacity = Math.max(1, options.capacity ?? DEFAULT_CAPACITY);
    this.canUndo = computed(() => {
      this.version();
      return this.undoStack.length > 0;
    });
    this.canRedo = computed(() => {
      this.version();
      return this.redoStack.length > 0;
    });
  }

  /**
   * Executes cell writes as one entry. No-op writes (value and invalid
   * state both unchanged) are elided; an all-no-op batch records nothing.
   */
  runCellWrites(kind: TmGridOpKind, writes: readonly TmGridCellWrite[]): void {
    const effective = writes.filter(
      (write) =>
        !Object.is(write.before, write.after) ||
        !sameInvalid(write.invalidBefore, write.invalidAfter),
    );
    if (effective.length === 0 || this.options.writer === undefined) {
      return;
    }
    const entry = this.newEntry(kind);
    this.push(entry);
    this.applyWritesTo(entry, effective);
  }

  /** Creates rows via the row factory as one entry. Returns the snapshots. */
  runRowInsert(
    modelIndex: number,
    count: number,
    parentRowId?: TmRowId | null,
  ): readonly TmGridRowSnapshot<T>[] {
    const writer = this.options.writer;
    if (writer === undefined || count <= 0) {
      return [];
    }
    const entry = this.newEntry('rowInsert');
    this.push(entry);
    return this.insertRowsInto(entry, modelIndex, count, parentRowId ?? null);
  }

  /** Removes rows as one entry (snapshots them for undo). */
  runRowDelete(rowIds: readonly TmRowId[]): void {
    const writer = this.options.writer;
    if (writer === undefined || rowIds.length === 0) {
      return;
    }
    const model = this.options.model;
    const removedRows = rowIds
      .map((id) => ({ id, row: model.rowById(id), modelIndex: model.modelIndexOfRow(id) }))
      .filter((snapshot) => snapshot.row !== undefined && snapshot.modelIndex !== -1)
      .map((snapshot) => ({
        id: snapshot.id,
        row: snapshot.row as T,
        modelIndex: snapshot.modelIndex,
      }))
      .sort((a, b) => a.modelIndex - b.modelIndex);
    if (removedRows.length === 0) {
      return;
    }
    const removedIds = new Set(removedRows.map((row) => row.id));
    const removedInvalid = this.options.annotations
      .invalidCells()
      .filter((cell) => removedIds.has(cell.rowId))
      .map((cell) => ({
        rowId: cell.rowId,
        columnId: cell.columnId,
        entry: this.options.annotations.invalidInput(cell.rowId, cell.columnId)!,
      }));
    const entry = this.newEntry('rowDelete');
    entry.removedRows = removedRows;
    entry.removedInvalid = removedInvalid;
    this.push(entry);
    for (const cell of removedInvalid) {
      this.options.annotations.setInvalid(cell.rowId, cell.columnId, null);
    }
    for (const row of removedRows) {
      this.bumpRowTokens(row.id);
    }
    writer.removeRows(removedRows.map((row) => row.id));
  }

  /**
   * Re-orders rows as one entry (row move); `parentWrites` carries the
   * re-parenting writes of a tree move so they invert with the order.
   */
  runRowMove(
    rowIds: readonly TmRowId[],
    beforeRowId: TmRowId | null,
    parentWrites: readonly TmGridCellWrite[],
  ): void {
    const writer = this.options.writer;
    if (writer === undefined || rowIds.length === 0) {
      return;
    }
    const model = this.options.model;
    const snapshots = rowIds
      .map((id) => ({ id, row: model.rowById(id), modelIndex: model.modelIndexOfRow(id) }))
      .filter((snapshot) => snapshot.row !== undefined && snapshot.modelIndex !== -1)
      .map((snapshot) => ({
        id: snapshot.id,
        row: snapshot.row as T,
        modelIndex: snapshot.modelIndex,
      }))
      .sort((a, b) => a.modelIndex - b.modelIndex);
    if (snapshots.length === 0) {
      return;
    }
    const entry = this.newEntry('rowMove');
    entry.move = { rowIds: [...rowIds], beforeRowId, snapshots };
    this.push(entry);
    writer.moveRows(rowIds, beforeRowId);
    this.applyWritesTo(entry, parentWrites);
  }

  /** Opens a compound entry (paste): one undo op across async boundaries. */
  beginCompound(kind: TmGridOpKind): TmGridCompoundHandle {
    const entry = this.newEntry(kind);
    entry.open = true;
    this.push(entry);
    return {
      applyWrites: (writes) => {
        this.applyWritesTo(entry, writes);
      },
      insertRows: (modelIndex, count, parentRowId) =>
        this.insertRowsInto(entry, modelIndex, count, parentRowId ?? null),
      setMove: (rowIds, beforeRowId) => {
        const model = this.options.model;
        entry.move = {
          rowIds: [...rowIds],
          beforeRowId,
          snapshots: rowIds
            .map((id) => ({
              id,
              row: model.rowById(id) as T,
              modelIndex: model.modelIndexOfRow(id),
            }))
            .filter((snapshot) => snapshot.row !== undefined && snapshot.modelIndex !== -1)
            .sort((a, b) => a.modelIndex - b.modelIndex),
        };
        this.options.writer?.moveRows(rowIds, beforeRowId);
      },
      onCancel: (cancel) => {
        entry.onCancel = cancel;
      },
      finalize: () => {
        entry.open = false;
        entry.onCancel = null;
        // A compound that ends empty (every write elided or invalidated)
        // must not linger as a no-op undo step.
        if (
          entry.writes.length === 0 &&
          entry.insertedRows.length === 0 &&
          entry.removedRows.length === 0 &&
          entry.move === null
        ) {
          this.remove(entry);
        }
      },
      get isOpen() {
        return entry.open;
      },
    };
  }

  /**
   * Registers consumer edits as one user-undoable entry — the public
   * transaction channel. Prior values are captured here; edits whose row no
   * longer exists are skipped with a warning.
   */
  applyTransaction(edits: readonly TmCellEdit[], opts?: { label?: string }): void {
    void opts;
    const model = this.options.model;
    const writes: TmGridCellWrite[] = [];
    for (const edit of edits) {
      const row = model.rowById(edit.rowId);
      if (row === undefined) {
        this.options.host?.onWarn?.({ kind: 'transactionRowMissing', rowId: edit.rowId });
        continue;
      }
      const colIndex = model.columnIndexOf(edit.key);
      const column = colIndex === -1 ? undefined : model.columnAt(colIndex);
      const before = column ? column.getValue(row) : (row as Record<string, unknown>)[edit.key];
      const columnId = column?.id ?? edit.key;
      writes.push({
        rowId: edit.rowId,
        columnId,
        columnKey: edit.key,
        before,
        after: edit.value,
        invalidBefore: this.options.annotations.invalidInput(edit.rowId, columnId) ?? null,
        invalidAfter: null,
      });
    }
    this.runCellWrites('transaction', writes);
  }

  /** Undoes the newest entry. Returns whether anything applied. */
  undo(): boolean {
    const entry = this.undoStack.pop();
    if (entry === undefined) {
      return false;
    }
    if (entry.open) {
      entry.onCancel?.();
      entry.open = false;
      entry.onCancel = null;
    }
    this.version.update((v) => v + 1);
    const skipped = this.applyEntry(entry, 'undo');
    if (skipped === 'nothing') {
      this.options.host?.onNotice?.({ kind: 'undoSkippedMissingRows' });
      return true;
    }
    this.redoStack.push(entry);
    this.version.update((v) => v + 1);
    this.options.host?.onNotice?.({ kind: 'undoApplied', opKind: entry.kind, skippedRows: skipped });
    return true;
  }

  /** Redoes the newest undone entry. Returns whether anything applied. */
  redo(): boolean {
    const entry = this.redoStack.pop();
    if (entry === undefined) {
      return false;
    }
    this.version.update((v) => v + 1);
    const skipped = this.applyEntry(entry, 'redo');
    if (skipped === 'nothing') {
      this.options.host?.onNotice?.({ kind: 'redoSkippedMissingRows' });
      return true;
    }
    this.undoStack.push(entry);
    this.version.update((v) => v + 1);
    this.options.host?.onNotice?.({ kind: 'redoApplied', opKind: entry.kind, skippedRows: skipped });
    return true;
  }

  /** Clears both stacks (content switches, the consumer's save/cancel). */
  clear(): void {
    this.undoStack = [];
    this.redoStack = [];
    this.version.update((v) => v + 1);
  }

  /** Snapshots both stacks for the state store (in-memory only). */
  toSnapshot(): TmGridHistorySnapshot {
    const snapshot: SnapshotInternals<T> = {
      version: 1,
      undoStack: [...this.undoStack],
      redoStack: [...this.redoStack],
    };
    return snapshot;
  }

  /** Restores a snapshot produced by {@link toSnapshot}. */
  restore(snapshot: TmGridHistorySnapshot): void {
    const internals = snapshot as SnapshotInternals<T>;
    this.undoStack = [...(internals.undoStack ?? [])];
    this.redoStack = [...(internals.redoStack ?? [])];
    this.version.update((v) => v + 1);
  }

  // ---- internals ----

  private newEntry(kind: TmGridOpKind): HistoryEntry<T> {
    return {
      kind,
      writes: [],
      insertedRows: [],
      removedRows: [],
      removedInvalid: [],
      move: null,
      open: false,
      onCancel: null,
    };
  }

  private push(entry: HistoryEntry<T>): void {
    this.undoStack.push(entry);
    this.redoStack = [];
    if (this.undoStack.length > this.capacity) {
      this.undoStack.shift();
    }
    this.version.update((v) => v + 1);
  }

  private remove(entry: HistoryEntry<T>): void {
    const index = this.undoStack.indexOf(entry);
    if (index !== -1) {
      this.undoStack.splice(index, 1);
      this.version.update((v) => v + 1);
    }
  }

  /** Executes writes forward and appends them to the entry. */
  private applyWritesTo(entry: HistoryEntry<T>, writes: readonly TmGridCellWrite[]): void {
    const writer = this.options.writer;
    if (writer === undefined) {
      return;
    }
    for (const write of writes) {
      if (Object.is(write.before, write.after) && sameInvalid(write.invalidBefore, write.invalidAfter)) {
        continue;
      }
      writer.setCellValue(write.rowId, write.columnKey, write.after);
      this.options.annotations.setInvalid(write.rowId, write.columnId, write.invalidAfter);
      this.options.annotations.bumpToken(write.rowId, write.columnId);
      entry.writes.push(write);
    }
  }

  private insertRowsInto(
    entry: HistoryEntry<T>,
    modelIndex: number,
    count: number,
    parentRowId: TmRowId | null,
  ): readonly TmGridRowSnapshot<T>[] {
    const writer = this.options.writer;
    if (writer === undefined || count <= 0) {
      return [];
    }
    const created = writer.insertNewRows(modelIndex, count, parentRowId);
    const snapshots = created.map((row, i) => ({
      row: row.row,
      id: row.id,
      modelIndex: modelIndex + i,
    }));
    entry.insertedRows.push(...snapshots);
    return snapshots;
  }

  /**
   * Applies an entry in either direction to the surviving rows. Returns the
   * count of skipped rows, or `'nothing'` when the entry touched only
   * vanished rows (the entry is then consumed without effect).
   */
  private applyEntry(entry: HistoryEntry<T>, direction: 'undo' | 'redo'): number | 'nothing' {
    const writer = this.options.writer;
    const model = this.options.model;
    const annotations = this.options.annotations;
    if (writer === undefined) {
      return 'nothing';
    }
    const skippedRowIds = new Set<TmRowId>();
    const touchedRowIds = new Set<TmRowId>();
    const touchedColumnIds = new Set<string>();
    let applied = false;

    const applyWrite = (write: TmGridCellWrite, forward: boolean): void => {
      if (model.modelIndexOfRow(write.rowId) === -1) {
        skippedRowIds.add(write.rowId);
        return;
      }
      writer.setCellValue(write.rowId, write.columnKey, forward ? write.after : write.before);
      annotations.setInvalid(
        write.rowId,
        write.columnId,
        forward ? write.invalidAfter : write.invalidBefore,
      );
      annotations.bumpToken(write.rowId, write.columnId);
      touchedRowIds.add(write.rowId);
      touchedColumnIds.add(write.columnId);
      applied = true;
    };

    if (direction === 'undo') {
      // Inverse order: writes first (their rows still exist), then undo the
      // structure (drop created rows, restore removed rows, revert moves).
      for (let i = entry.writes.length - 1; i >= 0; i--) {
        applyWrite(entry.writes[i], false);
      }
      const createdSurviving = entry.insertedRows.filter(
        (row) => model.modelIndexOfRow(row.id) !== -1,
      );
      if (createdSurviving.length > 0) {
        for (const row of createdSurviving) {
          this.bumpRowTokens(row.id);
        }
        writer.removeRows(createdSurviving.map((row) => row.id));
        createdSurviving.forEach((row) => touchedRowIds.delete(row.id));
        applied = true;
      }
      if (entry.removedRows.length > 0) {
        writer.reinsertRows(entry.removedRows.map(({ row, modelIndex }) => ({ row, modelIndex })));
        for (const cell of entry.removedInvalid) {
          annotations.setInvalid(cell.rowId, cell.columnId, cell.entry);
        }
        entry.removedRows.forEach((row) => touchedRowIds.add(row.id));
        applied = true;
      }
      if (entry.move !== null) {
        const surviving = entry.move.snapshots.filter(
          (row) => model.modelIndexOfRow(row.id) !== -1,
        );
        if (surviving.length > 0) {
          writer.removeRows(surviving.map((row) => row.id));
          writer.reinsertRows(surviving.map(({ row, modelIndex }) => ({ row, modelIndex })));
          surviving.forEach((row) => touchedRowIds.add(row.id));
          applied = true;
        }
        entry.move.snapshots
          .filter((row) => model.modelIndexOfRow(row.id) === -1)
          .forEach((row) => skippedRowIds.add(row.id));
      }
    } else {
      // Forward order: structure first (moves, deletions, creations), then
      // the writes that assumed it.
      if (entry.move !== null) {
        const surviving = entry.move.rowIds.filter((id) => model.modelIndexOfRow(id) !== -1);
        if (surviving.length > 0) {
          writer.moveRows(surviving, entry.move.beforeRowId);
          surviving.forEach((id) => touchedRowIds.add(id));
          applied = true;
        }
        entry.move.rowIds
          .filter((id) => model.modelIndexOfRow(id) === -1)
          .forEach((id) => skippedRowIds.add(id));
      }
      if (entry.removedRows.length > 0) {
        const surviving = entry.removedRows.filter((row) => model.modelIndexOfRow(row.id) !== -1);
        for (const cell of entry.removedInvalid) {
          annotations.setInvalid(cell.rowId, cell.columnId, null);
        }
        if (surviving.length > 0) {
          for (const row of surviving) {
            this.bumpRowTokens(row.id);
          }
          writer.removeRows(surviving.map((row) => row.id));
          applied = true;
        }
      }
      if (entry.insertedRows.length > 0) {
        writer.reinsertRows(entry.insertedRows.map(({ row, modelIndex }) => ({ row, modelIndex })));
        entry.insertedRows.forEach((row) => touchedRowIds.add(row.id));
        applied = true;
      }
      for (const write of entry.writes) {
        applyWrite(write, true);
      }
    }

    if (!applied) {
      return 'nothing';
    }
    this.options.onReveal?.(
      { rowIds: [...touchedRowIds], columnIds: [...touchedColumnIds] },
      direction,
    );
    return skippedRowIds.size;
  }

  /** Invalidates every pending resolution touching a structurally-changed row. */
  private bumpRowTokens(rowId: TmRowId): void {
    const model = this.options.model;
    const count = model.columnCount();
    for (let col = 0; col < count; col++) {
      this.options.annotations.bumpToken(rowId, model.columnAt(col).id);
    }
  }
}

function sameInvalid(a: TmGridInvalidInput | null, b: TmGridInvalidInput | null): boolean {
  if (a === null || b === null) {
    return a === b;
  }
  return a.rawText === b.rawText && a.reason === b.reason;
}
