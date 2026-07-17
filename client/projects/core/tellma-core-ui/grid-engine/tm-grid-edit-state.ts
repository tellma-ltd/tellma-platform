// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signal, untracked, type Signal } from '@angular/core';

import { TM_PARSE_ERROR, type SignalLike, type TmRowId } from '@tellma/core-ui/contracts';

import type { TmGridCellAnnotations } from './tm-grid-cell-annotations';
import type { TmGridDataModel } from './tm-grid-data-model';
import type { TmGridCellWrite, TmGridHistory } from './tm-grid-history';
import type { TmRowCol } from './tm-grid-types';

/** The open editing session. */
export interface TmGridEditSession {
  /** The edited cell, in view space. */
  readonly cell: TmRowCol;
  /**
   * The edited row's identity, or `null` for a virtual session on the
   * new-row placeholder (the row materializes only at commit).
   */
  readonly rowId: TmRowId | null;
  /**
   * `edit` (opened by Enter/F2/double-click: horizontal arrows move the
   * caret) or `enter` (opened by typing: horizontal arrows commit and
   * move). F2 toggles.
   */
  readonly mode: 'edit' | 'enter';
  /** The type-to-edit seed the editor content starts from (enter mode). */
  readonly seedText?: string;
}

/** Construction inputs of {@link TmGridEditState}. */
export interface TmGridEditStateOptions<T = unknown> {
  /** The data model. */
  readonly model: TmGridDataModel<T>;
  /** The annotation store (invalid inputs). */
  readonly annotations: TmGridCellAnnotations;
  /** The history stack every commit records through. */
  readonly history: TmGridHistory<T>;
  /** The active locale (parse context). */
  readonly locale: SignalLike<string>;
  /** Called on every successful commit/toggle — any edit disarms a pending cut. */
  onEdit?(): void;
}

/**
 * The editing-session state machine: at most one session exists; opening
 * gates on cell editability; commits flow through the history stack (text
 * commits run the column's parse — unparseable text becomes an invalid
 * input with the model cleared); cancel leaves no trace. Sessions on the
 * placeholder row are virtual: the row materializes at commit, and the
 * materialization plus the first write form one undo entry.
 */
export class TmGridEditState<T = unknown> {
  private readonly options: TmGridEditStateOptions<T>;
  private readonly sessionSignal = signal<TmGridEditSession | null>(null);

  /** The open session, or `null`. */
  readonly session: Signal<TmGridEditSession | null>;

  constructor(options: TmGridEditStateOptions<T>) {
    this.options = options;
    this.session = this.sessionSignal.asReadonly();
  }

  /**
   * Opens a session on a cell. Returns `false` (no session) when the cell
   * is not editable right now or the column is `boolean` (those cells
   * toggle atomically instead of opening an editor).
   */
  openEdit(cell: TmRowCol, mode: 'edit' | 'enter', seedText?: string): boolean {
    const model = this.options.model;
    if (!model.isCellEditable(cell) || model.columnAt(cell.col)?.type === 'boolean') {
      return false;
    }
    const rowId = model.isPlaceholder(cell.row) ? null : (model.rowAt(cell.row)?.id ?? null);
    this.sessionSignal.set({ cell, rowId, mode, seedText });
    return true;
  }

  /** F2 while editing: toggles `edit` ↔ `enter` mode. */
  toggleMode(): void {
    const session = untracked(this.sessionSignal);
    if (session !== null) {
      this.sessionSignal.set({ ...session, mode: session.mode === 'edit' ? 'enter' : 'edit' });
    }
  }

  /**
   * Commits a typed value (editors that own a value channel). Clears any
   * invalid input on the cell. Returns whether a commit happened.
   */
  commitValue(value: unknown): boolean {
    return this.commitInternal((rowId, cell) => this.buildWrite(rowId, cell, value, null));
  }

  /**
   * Commits editor text through the column's parse. Unparseable text writes
   * the column's cleared value and records the raw text as an invalid input
   * — the model and the display never silently disagree. Columns without a
   * parse take the text as-is. Returns whether a commit happened.
   */
  commitText(text: string): boolean {
    return this.commitInternal((rowId, cell) => {
      const column = this.options.model.columnAt(cell.col);
      if (column.parse === undefined) {
        return this.buildWrite(rowId, cell, text, null);
      }
      const parsed = column.parse(text, { locale: untracked(() => this.options.locale()) });
      if (parsed === TM_PARSE_ERROR) {
        return this.buildWrite(rowId, cell, column.clearedValue, {
          rawText: text,
          reason: 'parse',
        });
      }
      return this.buildWrite(rowId, cell, parsed, null);
    });
  }

  /** Closes the session without writing anything (Esc, mode flips). */
  cancel(): void {
    this.sessionSignal.set(null);
  }

  /**
   * Re-points the open session at a new view position after the rows moved
   * under it (external data changes) — the session's identity is its row
   * id; the coordinate is derived.
   */
  relocateSession(cell: TmRowCol): void {
    const session = untracked(this.sessionSignal);
    if (session !== null && (session.cell.row !== cell.row || session.cell.col !== cell.col)) {
      this.sessionSignal.set({ ...session, cell });
    }
  }

  /**
   * The boolean cell's atomic toggle — no session. On the placeholder row
   * the toggle materializes the row first (one undo entry). Returns whether
   * a write happened.
   */
  toggleBoolean(cell: TmRowCol): boolean {
    const model = this.options.model;
    if (!model.isCellEditable(cell) || model.columnAt(cell.col)?.type !== 'boolean') {
      return false;
    }
    if (model.isPlaceholder(cell.row)) {
      const handle = this.options.history.beginCompound('cellEdit');
      const created = handle.insertRows(untracked(() => model.modelRowCount()), 1, null);
      if (created.length === 0) {
        handle.finalize();
        return false;
      }
      const rowId = created[0].id;
      const row = model.rowById(rowId);
      const column = model.columnAt(cell.col);
      const before = row === undefined ? null : column.getValue(row as T);
      handle.applyWrites([
        {
          rowId,
          columnId: column.id,
          columnKey: column.key!,
          before,
          after: before !== true,
          invalidBefore: null,
          invalidAfter: null,
        },
      ]);
      handle.finalize();
      this.options.onEdit?.();
      return true;
    }
    const view = model.rowAt(cell.row);
    if (view === null) {
      return false;
    }
    const write = this.buildWrite(view.id, cell, model.cellValue(cell) !== true, null);
    if (write === null) {
      return false;
    }
    this.options.history.runCellWrites('cellEdit', [write]);
    this.options.onEdit?.();
    return true;
  }

  // ---- internals ----

  /**
   * Shared commit path: resolves (or materializes) the row, builds the
   * write, records it as one undo entry, closes the session.
   */
  private commitInternal(
    build: (rowId: TmRowId, cell: TmRowCol) => TmGridCellWrite | null,
  ): boolean {
    const session = untracked(this.sessionSignal);
    if (session === null) {
      return false;
    }
    const model = this.options.model;
    this.sessionSignal.set(null);
    if (session.rowId === null) {
      // Virtual placeholder session: materialize + write = one undo entry.
      const handle = this.options.history.beginCompound('cellEdit');
      const created = handle.insertRows(untracked(() => model.modelRowCount()), 1, null);
      if (created.length === 0) {
        handle.finalize();
        return false;
      }
      const write = build(created[0].id, session.cell);
      if (write !== null) {
        handle.applyWrites([write]);
      }
      handle.finalize();
      this.options.onEdit?.();
      return true;
    }
    if (model.modelIndexOfRow(session.rowId) === -1) {
      return false; // the row vanished mid-edit; reconcile already announced
    }
    const write = build(session.rowId, session.cell);
    if (write === null) {
      return false;
    }
    this.options.history.runCellWrites('cellEdit', [write]);
    this.options.onEdit?.();
    return true;
  }

  /** A write against the CURRENT cell state (before-values captured here). */
  private buildWrite(
    rowId: TmRowId,
    cell: TmRowCol,
    after: unknown,
    invalidAfter: { rawText: string; reason: 'parse' } | null,
  ): TmGridCellWrite | null {
    const model = this.options.model;
    const column = model.columnAt(cell.col);
    if (column === undefined || column.key === null) {
      return null;
    }
    const row = model.rowById(rowId);
    const before = row === undefined ? null : column.getValue(row as T);
    return {
      rowId,
      columnId: column.id,
      columnKey: column.key,
      before,
      after,
      invalidBefore: this.options.annotations.invalidInput(rowId, column.id) ?? null,
      invalidAfter,
    };
  }
}
