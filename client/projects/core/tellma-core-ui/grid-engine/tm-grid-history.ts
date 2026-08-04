// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, signal, untracked, type Signal } from '@angular/core';

import type { SignalLike, TmCellEdit, TmRowId } from '@tellma/core-ui/contracts';

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
  /**
   * Opaque selection snapshot captured just BEFORE the op ran; undo restores
   * it verbatim. Without it, undo would infer the selection from the op's
   * written cells — wrong when those don't coincide with the prior selection
   * (fill-down never writes its source row; a cut-move writes both source and
   * target). Opaque so the history stays decoupled from the selection model.
   */
  selectionBefore?: unknown;
  /** Selection snapshot captured at undo time (the post-op state); redo restores it. */
  selectionAfter?: unknown;
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
  /**
   * The selection snapshot to restore (the pre-op state on undo, the post-op
   * state on redo). When present and resolvable, the reveal restores it
   * instead of inferring the range from `rowIds`/`columnIds`.
   */
  readonly selection?: unknown;
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
  /**
   * Whether the grid is editable right now. The writer stays bound across a
   * readonly view/edit toggle (so the stack survives), so undo/redo and the
   * public transaction channel gate on this instead — they must never write
   * while the grid is in readonly view mode.
   */
  readonly editable?: SignalLike<boolean>;
  /** Component-layer callbacks (undo/redo notices, warnings). */
  readonly host?: Pick<TmGridEngineHost<T>, 'onNotice' | 'onWarn'>;
  /** Called after undo/redo applies, with the affected identities. */
  onReveal?(reveal: TmGridHistoryReveal, direction: 'undo' | 'redo'): void;
  /**
   * Captures the current selection as an opaque snapshot, stamped onto each
   * entry so undo/redo restore the real prior selection (see
   * {@link HistoryEntry.selectionBefore}).
   */
  snapshotSelection?(): unknown;
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
    if (this.options.writer === undefined) {
      return;
    }
    const hasEffect = writes.some(
      (write) =>
        !Object.is(write.before, write.after) ||
        !sameInvalid(write.invalidBefore, write.invalidAfter),
    );
    if (!hasEffect) {
      // Nothing changes in the model — but a write onto a still-resolving
      // cell (a Delete or a manual edit over a pending paste) must supersede
      // it: bump the token so the late resolution is stale, and drop the
      // pending mark. No history entry: there is no model change to undo.
      const annotations = this.options.annotations;
      for (const write of writes) {
        if (annotations.isPending(write.rowId, write.columnId)) {
          annotations.bumpToken(write.rowId, write.columnId);
          annotations.setPending(write.rowId, write.columnId, false);
        }
      }
      return;
    }
    const entry = this.newEntry(kind);
    this.push(entry);
    this.applyWritesTo(entry, writes);
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
    const redoBefore = this.redoStack;
    const entry = this.newEntry('rowInsert');
    this.push(entry);
    const snapshots = this.insertRowsInto(entry, modelIndex, count, parentRowId ?? null);
    if (snapshots.length === 0) {
      // The factory created nothing: don't leave an empty entry (or the redo
      // stack that `push` cleared) behind.
      this.remove(entry);
      if (this.redoStack.length === 0) {
        this.redoStack = redoBefore;
        this.version.update((v) => v + 1);
      }
    }
    return snapshots;
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
    const redoBefore = this.redoStack;
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
        // must not linger as a no-op undo step — nor discard the redo stack
        // that opening it cleared (a no-op paste keeps the redo history).
        if (
          entry.writes.length === 0 &&
          entry.insertedRows.length === 0 &&
          entry.removedRows.length === 0 &&
          entry.move === null
        ) {
          this.remove(entry);
          if (this.redoStack.length === 0) {
            this.redoStack = redoBefore;
            this.version.update((v) => v + 1);
          }
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
    if (!this.mutationsAllowed()) {
      return; // readonly view mode never writes through the field tree
    }
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
    if (!this.mutationsAllowed()) {
      return false; // the stack survives a readonly flip but never writes there
    }
    const entry = this.undoStack.pop();
    if (entry === undefined) {
      return false;
    }
    const wasOpen = entry.open;
    if (entry.open) {
      entry.onCancel?.();
      entry.open = false;
      entry.onCancel = null;
    }
    // Capture the post-op selection (the current state) so a later redo can
    // restore it, then undo restores the pre-op selection (in applyEntry).
    entry.selectionAfter = this.options.snapshotSelection?.();
    this.version.update((v) => v + 1);
    const skipped = this.applyEntry(entry, 'undo');
    if (skipped === 'nothing') {
      // An open paste cancelled before any effective write (every cell was
      // still awaiting resolution) reports the cancellation, not the
      // "rows no longer exist" skip that a truly-vanished entry gets.
      this.options.host?.onNotice?.(
        wasOpen
          ? { kind: 'undoApplied', opKind: entry.kind, skippedRows: 0 }
          : { kind: 'undoSkippedMissingRows' },
      );
      return true;
    }
    this.redoStack.push(entry);
    this.version.update((v) => v + 1);
    this.options.host?.onNotice?.({ kind: 'undoApplied', opKind: entry.kind, skippedRows: skipped });
    return true;
  }

  /** Redoes the newest undone entry. Returns whether anything applied. */
  redo(): boolean {
    if (!this.mutationsAllowed()) {
      return false; // the stack survives a readonly flip but never writes there
    }
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

  /** Whether writes may flow now — editable when the flag is bound. */
  private mutationsAllowed(): boolean {
    return this.options.editable === undefined || untracked(() => this.options.editable!());
  }

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
    // Snapshot the selection BEFORE the op mutates (push runs ahead of every
    // writer call), so undo can restore exactly what was selected.
    entry.selectionBefore = this.options.snapshotSelection?.();
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
    const annotations = this.options.annotations;
    for (const write of writes) {
      const valueChanged = !Object.is(write.before, write.after);
      const invalidChanged = !sameInvalid(write.invalidBefore, write.invalidAfter);
      if (!valueChanged && !invalidChanged) {
        // A value/invalid no-op still supersedes an in-flight resolution on
        // the cell (a second paste onto a pending cell writes the same
        // cleared value): bump the token so the earlier request goes stale,
        // and drop the pending mark. Nothing is recorded for undo.
        if (annotations.isPending(write.rowId, write.columnId)) {
          annotations.bumpToken(write.rowId, write.columnId);
          annotations.setPending(write.rowId, write.columnId, false);
        }
        continue;
      }
      if (valueChanged) {
        writer.setCellValue(write.rowId, write.columnKey, write.after);
      }
      annotations.setInvalid(write.rowId, write.columnId, write.invalidAfter);
      annotations.bumpToken(write.rowId, write.columnId);
      // A real write onto a pending cell supersedes its resolution too.
      if (annotations.isPending(write.rowId, write.columnId)) {
        annotations.setPending(write.rowId, write.columnId, false);
      }
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
        // An external refresh may have re-added some of these ids: reinserting
        // them would duplicate the row. Reinsert only the still-missing ones
        // and fold the rest into the skip tally.
        const missing = entry.removedRows.filter((row) => model.modelIndexOfRow(row.id) === -1);
        for (const row of entry.removedRows) {
          if (model.modelIndexOfRow(row.id) !== -1) {
            skippedRowIds.add(row.id);
          }
        }
        if (missing.length > 0) {
          writer.reinsertRows(missing.map(({ row, modelIndex }) => ({ row, modelIndex })));
          const missingIds = new Set(missing.map((row) => row.id));
          for (const cell of entry.removedInvalid) {
            if (missingIds.has(cell.rowId)) {
              annotations.setInvalid(cell.rowId, cell.columnId, cell.entry);
            }
          }
          missing.forEach((row) => touchedRowIds.add(row.id));
          applied = true;
        }
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
        // Re-creation on redo skips ids an external refresh already restored.
        const missing = entry.insertedRows.filter((row) => model.modelIndexOfRow(row.id) === -1);
        for (const row of entry.insertedRows) {
          if (model.modelIndexOfRow(row.id) !== -1) {
            skippedRowIds.add(row.id);
          }
        }
        if (missing.length > 0) {
          writer.reinsertRows(missing.map(({ row, modelIndex }) => ({ row, modelIndex })));
          missing.forEach((row) => touchedRowIds.add(row.id));
          applied = true;
        }
      }
      for (const write of entry.writes) {
        applyWrite(write, true);
      }
    }

    if (!applied) {
      return 'nothing';
    }
    // Undo restores the pre-op selection; redo the post-op one. A missing
    // snapshot (older entry, or none captured) leaves the reveal to infer the
    // range from the touched cells.
    const selection = direction === 'undo' ? entry.selectionBefore : entry.selectionAfter;
    this.options.onReveal?.(
      { rowIds: [...touchedRowIds], columnIds: [...touchedColumnIds], selection },
      direction,
    );
    return skippedRowIds.size;
  }

  /** Invalidates every pending resolution touching a structurally-changed row. */
  private bumpRowTokens(rowId: TmRowId): void {
    this.options.annotations.bumpRowToken(rowId);
  }
}

function sameInvalid(a: TmGridInvalidInput | null, b: TmGridInvalidInput | null): boolean {
  if (a === null || b === null) {
    return a === b;
  }
  return a.rawText === b.rawText && a.reason === b.reason;
}
