// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { untracked } from '@angular/core';

import type { TmCellEdit, TmGridSelectionSnapshot, TmRowId } from '@tellma/core-ui/contracts';

import { TmGridCellAnnotations } from './tm-grid-cell-annotations';
import { TmGridClipboard } from './tm-grid-clipboard';
import { TmGridDataModel, type TmGridOrderSnapshot } from './tm-grid-data-model';
import { TmGridEditState } from './tm-grid-edit-state';
import type { TmGridEngineOptions } from './tm-grid-host';
import {
  TmGridHistory,
  type TmGridCellWrite,
  type TmGridHistoryReveal,
  type TmGridRowSnapshot,
} from './tm-grid-history';
import { TmGridNav } from './tm-grid-nav';
import { TmGridSelectionModel } from './tm-grid-selection';
import type { TmGridMotion, TmGridRange, TmRowCol } from './tm-grid-types';

/**
 * The engine facade: constructs and wires the engine's parts and owns the
 * invariants that span them — an edit disarms a pending cut, undo/redo
 * re-selects and reveals what it restored, external data changes reconcile
 * every identity-keyed piece of state in one pass. The parts stay
 * independently reachable (`model`, `nav`, `selection`, …) for everything
 * that needs no cross-cutting behavior.
 */
export class TmGridEngine<T = unknown> {
  private readonly options: TmGridEngineOptions<T>;
  /** The visible order as of the last reconcile — what remapping keys off. */
  private lastOrder: TmGridOrderSnapshot;

  /** The data model (rows × columns, tree flattening, placeholder). */
  readonly model: TmGridDataModel<T>;
  /** Per-cell annotations (invalid inputs, pending resolutions, tokens). */
  readonly annotations: TmGridCellAnnotations;
  /** The active cell and motion semantics. */
  readonly nav: TmGridNav;
  /** The selection ranges. */
  readonly selection: TmGridSelectionModel;
  /** The undo/redo stack. */
  readonly history: TmGridHistory<T>;
  /** The editing-session state machine. */
  readonly edit: TmGridEditState<T>;
  /** Clipboard semantics (copy/cut/paste/fill-down). */
  readonly clipboard: TmGridClipboard<T>;

  constructor(options: TmGridEngineOptions<T>) {
    this.options = options;
    this.model = new TmGridDataModel<T>({
      rows: options.rows,
      rowId: (row) => options.rowId(row),
      columns: options.columns,
      editable: options.editable,
      canAddRows: options.canAddRows,
      tree: options.tree,
      host: options.host,
    });
    this.annotations = new TmGridCellAnnotations();
    this.nav = new TmGridNav({
      model: this.model as TmGridDataModel,
      direction: options.direction,
      pageSize: options.pageSize,
      cellIsEmpty: (cell) => this.displayText(cell) === '',
      cellIsEditable: (cell) => this.model.isCellEditable(cell),
    });
    this.selection = new TmGridSelectionModel({ model: this.model as TmGridDataModel });
    this.history = new TmGridHistory<T>({
      model: this.model,
      annotations: this.annotations,
      writer: options.host.writer,
      editable: options.editable,
      host: options.host,
      capacity: options.historyCapacity,
      onReveal: (reveal, direction) => this.revealAfterHistory(reveal, direction),
      snapshotSelection: () =>
        this.selection.toSnapshot(untracked(() => this.nav.activeCell())),
    });
    this.edit = new TmGridEditState<T>({
      model: this.model,
      annotations: this.annotations,
      history: this.history,
      locale: options.locale,
      // Any edit disarms a pending cut (the deferred move dies on edits).
      // `clipboard` is assigned below; commits can only run after that.
      // The order snapshot syncs too: a commit can materialize the
      // placeholder row (a structural change with no reconcile of its
      // own), and the component layer's rows-changed reconcile must then
      // see a CURRENT baseline, not remap against a stale one.
      onEdit: () => {
        this.clipboard.cancelCut();
        this.syncOrder();
      },
    });
    this.clipboard = new TmGridClipboard<T>({
      model: this.model,
      selection: this.selection,
      nav: this.nav,
      annotations: this.annotations,
      history: this.history,
      displayText: (cell) => this.displayText(cell),
      editable: options.editable,
      canAddRows: options.canAddRows,
      locale: options.locale,
      tenant: options.tenant,
      parentIdKey: options.tree?.parentIdKey,
      host: options.host,
    });
    this.model.seedExpansion();
    this.lastOrder = this.model.captureOrder();
  }

  /**
   * The cell's display text: the invalid-input raw text while the cell is
   * EDITABLE (the rejected text stays visible in place), the model's truth
   * otherwise. A readonly cell always shows the model value — a rejected input
   * it can no longer be corrected in place must never linger. This is what copy
   * exports, find searches, and emptiness tests read.
   */
  displayText(cell: TmRowCol): string {
    if (untracked(() => this.options.editable())) {
      const view = this.model.rowAt(cell.row);
      const column = this.model.columnAt(cell.col);
      if (view !== null && column !== undefined && this.model.isCellEditable(cell)) {
        const invalid = this.annotations.invalidInput(view.id, column.id);
        if (invalid !== undefined) {
          return invalid.rawText;
        }
      }
    }
    return this.model.cellText(cell);
  }

  // ---- Gesture-level intents ----

  /**
   * Arrow/Page/Home/End motion: plain motion re-activates and collapses the
   * selection; `extend` moves the active range's focus from its anchor,
   * leaving the active cell in place; `jump` applies the data-edge rule.
   */
  moveActive(motion: TmGridMotion, opts?: { extend?: boolean; jump?: boolean }): void {
    const active = untracked(() => this.nav.activeCell());
    if (active === null) {
      this.activateDefault();
      return;
    }
    this.nav.resetTabRun();
    if (opts?.extend) {
      const range = untracked(() => this.selection.activeRange());
      if (range === null) {
        // No range yet: anchor the extension at the active cell.
        this.selection.collapseTo(active);
      }
      const focus = range?.focus ?? active;
      // Excel's data-edge jump scans along the ACTIVE cell's axis (the active
      // cell stays put inside the selection): a vertical jump scans the
      // active column from the focus row, a horizontal one the active row
      // from the focus column. Only the moved axis is written to the focus,
      // so a diagonal selection keeps its other extent.
      const vertical =
        motion === 'up' || motion === 'down' || motion === 'pageUp' || motion === 'pageDown';
      const from =
        opts?.jump === true
          ? vertical
            ? { row: focus.row, col: active.col }
            : { row: active.row, col: focus.col }
          : focus;
      const jumped = this.nav.target(motion, from, opts?.jump === true);
      const target = vertical
        ? { row: jumped.row, col: focus.col }
        : { row: focus.row, col: jumped.col };
      this.selection.extendActiveTo(target);
      return;
    }
    const target = this.nav.target(motion, active, opts?.jump === true);
    this.nav.setActive(target);
    this.selection.collapseTo(target);
  }

  /** Pointer press on a cell (any open editor must be settled beforehand). */
  clickCell(cell: TmRowCol, opts?: { shift?: boolean; mod?: boolean }): void {
    this.nav.resetTabRun();
    if (opts?.shift) {
      this.selection.extendActiveTo(cell);
      return;
    }
    if (opts?.mod) {
      this.selection.addRange({ anchor: cell, focus: cell, kind: 'cells' });
      this.nav.setActive(cell);
      return;
    }
    this.nav.setActive(cell);
    this.selection.collapseTo(cell);
  }

  /** Pointer drag: extends the active range to the cell under the pointer. */
  dragTo(cell: TmRowCol): void {
    this.selection.extendActiveTo(cell);
  }

  /** Selects the active range's rows (Shift+Space) — full-width ranges. */
  selectActiveRows(additive = false): void {
    // Build from the active range's ANCHOR/FOCUS rows (not the normalized
    // rect), so a following Shift+motion still extends from the right corner.
    const active = untracked(this.selection.activeRange);
    if (active !== null) {
      this.selection.selectRows(active.anchor.row, active.focus.row, additive);
    }
  }

  /** Selects the active range's columns (Ctrl+Space). */
  selectActiveCols(additive = false): void {
    const active = untracked(this.selection.activeRange);
    if (active !== null) {
      this.selection.selectCols(active.anchor.col, active.focus.col, additive);
    }
  }

  /**
   * Delete/Backspace: clears every selected cell to its column's cleared
   * value (readonly cells and the placeholder untouched) and drops the
   * cells' invalid inputs — one undo entry. An armed cut is any edit's
   * casualty and disarms.
   */
  clearSelection(): void {
    if (!untracked(() => this.options.editable())) {
      return;
    }
    this.clipboard.cancelCut();
    const writes: TmGridCellWrite[] = [];
    const seen = new Set<string>();
    for (const rect of this.selection.rects()) {
      for (let row = rect.top; row <= rect.bottom; row++) {
        const view = this.model.rowAt(row);
        if (view === null) {
          continue; // the placeholder row is skipped by range operations
        }
        for (let col = rect.left; col <= rect.right; col++) {
          const column = this.model.columnAt(col);
          if (column === undefined || column.key === null) {
            continue;
          }
          // Type-prefixed key: numeric id 1 and string id '1' must not collide.
          const key = `${typeof view.id === 'number' ? '#' : '$'}${String(view.id)} ${column.id}`;
          if (seen.has(key) || !this.model.isCellEditable({ row, col })) {
            continue;
          }
          seen.add(key);
          writes.push({
            rowId: view.id,
            columnId: column.id,
            columnKey: column.key,
            before: column.getValue(view.row),
            after: column.clearedValue,
            invalidBefore: this.annotations.invalidInput(view.id, column.id) ?? null,
            invalidAfter: null,
          });
        }
      }
    }
    this.history.runCellWrites('clear', writes);
  }

  /**
   * Inserts `count` new rows above or below the selected rows (trees:
   * siblings of the reference row). Returns the created rows.
   */
  insertRows(where: 'above' | 'below', count?: number): readonly TmGridRowSnapshot<T>[] {
    if (!untracked(() => this.options.editable()) || !untracked(() => this.options.canAddRows())) {
      return [];
    }
    const spans = this.selection.rowsUnion();
    const active = untracked(() => this.nav.activeCell());
    let referenceViewRow: number;
    if (spans.length > 0) {
      referenceViewRow = where === 'above' ? spans[0].start : spans[spans.length - 1].end;
    } else if (active !== null && !this.model.isPlaceholder(active.row)) {
      referenceViewRow = active.row;
    } else {
      // No data-row reference (empty grid / placeholder active): append.
      const created = this.history.runRowInsert(
        untracked(() => this.model.modelRowCount()),
        count ?? 1,
        null,
      );
      this.afterRowInsert(created, active?.col ?? 0);
      return created;
    }
    const reference = this.model.rowAt(referenceViewRow);
    if (reference === null) {
      return [];
    }
    const rowCount =
      count ?? Math.max(1, spans.reduce((sum, span) => sum + (span.end - span.start + 1), 0));
    const modelIndex = where === 'above' ? reference.modelIndex : reference.modelIndex + 1;
    const created = this.history.runRowInsert(modelIndex, rowCount, reference.parentId);
    this.afterRowInsert(created, active?.col ?? 0);
    return created;
  }

  /**
   * Inserts one new row as the last child of a parent (trees), expanding
   * the parent and activating the new row's first editable cell.
   */
  insertChildRow(parentRowId: TmRowId): readonly TmGridRowSnapshot<T>[] {
    if (!untracked(() => this.options.editable()) || !untracked(() => this.options.canAddRows())) {
      return [];
    }
    if (this.model.modelIndexOfRow(parentRowId) === -1) {
      return [];
    }
    const created = this.history.runRowInsert(
      untracked(() => this.model.modelRowCount()),
      1,
      parentRowId,
    );
    if (created.length > 0) {
      this.model.setExpanded(parentRowId, true);
      const viewRow = this.model.viewIndexOfRow(created[0].id);
      if (viewRow !== -1) {
        let col = 0;
        const colCount = untracked(() => this.model.columnCount());
        while (col < colCount - 1 && !this.model.isCellEditable({ row: viewRow, col })) {
          col++;
        }
        this.nav.setActive({ row: viewRow, col });
        this.selection.collapseTo({ row: viewRow, col });
      }
      this.options.host.onNotice?.({ kind: 'rowsInserted', count: 1 });
      this.syncOrder();
    }
    return created;
  }

  /** Deletes the selected rows — in a tree, each row's subtree goes with it. */
  deleteSelectedRows(): void {
    if (!untracked(() => this.options.editable())) {
      return;
    }
    const ids: TmRowId[] = [];
    const seen = new Set<TmRowId>();
    const addRow = (row: number): void => {
      const view = this.model.rowAt(row);
      if (view === null) {
        return;
      }
      for (const member of this.model.subtreeRowIds(view.id)) {
        if (!seen.has(member)) {
          seen.add(member);
          ids.push(member);
        }
      }
    };
    const spans = this.selection.rowsUnion();
    if (spans.length > 0) {
      for (const span of spans) {
        for (let row = span.start; row <= span.end; row++) {
          addRow(row);
        }
      }
    } else {
      // A row delete leaves only the active cell at the successor row (the
      // selection remaps to empty); fall back to it so repeated Ctrl+Alt+Minus
      // keeps deleting down the list instead of stalling after the first row.
      const active = untracked(() => this.nav.activeCell());
      if (active !== null) {
        addRow(active.row);
      }
    }
    if (ids.length === 0) {
      return;
    }
    this.clipboard.cancelCut();
    this.history.runRowDelete(ids);
    this.options.host.onNotice?.({ kind: 'rowsDeleted', count: ids.length });
    this.reconcile();
  }

  /**
   * Expands/collapses a row: the selection collapses to the active cell
   * (predictability over cleverness), and collapsing an ancestor of the
   * active cell moves activation to that ancestor.
   */
  setExpanded(rowId: TmRowId, expanded: boolean): void {
    const active = untracked(() => this.nav.activeCell());
    const activeRowId = active === null ? null : (this.model.rowAt(active.row)?.id ?? null);
    if (!this.model.setExpanded(rowId, expanded)) {
      return;
    }
    if (activeRowId !== null && active !== null) {
      const newIndex = this.model.viewIndexOfRow(activeRowId);
      if (newIndex === -1) {
        // The active row disappeared into the collapsed subtree.
        const ancestorIndex = this.model.viewIndexOfRow(rowId);
        const cell = { row: Math.max(0, ancestorIndex), col: active.col };
        this.nav.setActive(cell);
        this.selection.collapseTo(cell);
      } else {
        const cell = { row: newIndex, col: active.col };
        this.nav.setActive(cell);
        this.selection.collapseTo(cell);
      }
    }
    this.syncOrder();
  }

  /**
   * Expands a row's ancestors so it becomes visible (find matches, reveals),
   * keeping the order snapshot current — a later reconcile remaps against a
   * fresh baseline, never a pre-expansion one.
   */
  expandAncestorsOf(rowId: TmRowId): void {
    this.model.expandAncestorsOf(rowId);
    this.syncOrder();
  }

  /**
   * Seeds expansion from the configured default depth (content restore with
   * no persisted set), syncing the order snapshot.
   */
  seedExpansion(): void {
    this.model.seedExpansion();
    this.syncOrder();
  }

  /** Restores a persisted expansion set (content restore), syncing the order. */
  restoreExpansion(ids: ReadonlySet<TmRowId>): void {
    this.model.restoreExpansion(ids);
    this.syncOrder();
  }

  /**
   * The Esc chain's first stage: disarms a pending cut or clears the copy
   * marquee. Returns whether it did (the component moves focus to the
   * container otherwise).
   */
  escape(): boolean {
    if (
      untracked(() => this.clipboard.pendingCut()) !== null ||
      untracked(() => this.clipboard.copyMarquee()) !== null
    ) {
      this.clipboard.cancelCut();
      return true;
    }
    return false;
  }

  /** Registers consumer edits as one user-undoable operation. */
  applyTransaction(edits: readonly TmCellEdit[], opts?: { label?: string }): void {
    this.history.applyTransaction(edits, opts);
  }

  /**
   * Reconciles every identity-keyed piece of state after the rows array
   * changed in place: the selection remaps, the active cell follows its row
   * (falling back to the nearest row in the same column), an open editor
   * whose row vanished cancels with a notice, annotations prune, and the
   * pending cut revalidates. The component layer calls this whenever the
   * bound rows change for any reason other than the engine's own writes.
   */
  reconcile(): void {
    const before = this.lastOrder;
    const active = untracked(() => this.nav.activeCell());
    // The editor first. A virtual placeholder session (rowId === null)
    // follows the placeholder's new index (cancelling if it disappeared); a
    // session on a surviving row relocates, revealing it when it fell into a
    // collapsed subtree; one whose row vanished cancels with a notice.
    const session = untracked(() => this.edit.session());
    if (session !== null) {
      if (session.rowId === null) {
        const placeholder = this.model.placeholderIndex();
        if (placeholder === -1) {
          this.edit.cancel();
          this.options.host.onNotice?.({ kind: 'editorCancelledRowRemoved' });
        } else if (placeholder !== session.cell.row) {
          this.edit.relocateSession({ row: placeholder, col: session.cell.col });
        }
      } else if (this.model.modelIndexOfRow(session.rowId) === -1) {
        this.edit.cancel();
        this.options.host.onNotice?.({ kind: 'editorCancelledRowRemoved' });
      } else {
        let viewRow = this.model.viewIndexOfRow(session.rowId);
        if (viewRow === -1) {
          this.model.expandAncestorsOf(session.rowId);
          viewRow = this.model.viewIndexOfRow(session.rowId);
        }
        if (viewRow !== -1) {
          this.edit.relocateSession({ row: viewRow, col: session.cell.col });
        } else {
          this.edit.cancel();
          this.options.host.onNotice?.({ kind: 'editorCancelledRowRemoved' });
        }
      }
    }
    this.selection.remap(before);
    if (active !== null) {
      const oldId = before.visibleIds[active.row];
      const newIndex = oldId === undefined ? -1 : this.model.viewIndexOfRow(oldId);
      if (newIndex !== -1) {
        this.nav.setActive({ row: newIndex, col: active.col }, { keepTabRun: true });
      } else {
        // The active row vanished: mirror the selection's fallback — the
        // nearest surviving row in the old order, same column — instead of
        // holding the stale (now-clamped) view index.
        const fallback = this.nearestSurvivingIndex(active.row, before);
        if (fallback !== -1) {
          this.nav.setActive({ row: fallback, col: active.col }, { keepTabRun: true });
        } else {
          this.nav.reclamp();
        }
      }
    } else {
      this.nav.reclamp();
    }
    this.nav.remapTabRun((oldRow) => {
      const id = before.visibleIds[oldRow];
      return id === undefined ? -1 : this.model.viewIndexOfRow(id);
    });
    this.annotations.prune(this.model);
    this.clipboard.reconcileCut();
    this.syncOrder();
  }

  /**
   * The visible order as of the last reconcile. During a pending data change
   * (rows swapped but not yet reconciled) this is still the OUTGOING order —
   * what a persist must resolve the outgoing selection's view rows through.
   */
  get orderSnapshot(): TmGridOrderSnapshot {
    return this.lastOrder;
  }

  /**
   * Re-captures the order snapshot to the current model order. The component
   * calls it after restoring a switched content's state, so the reconcile the
   * concurrent rows change triggers remaps against a current baseline (an
   * identity no-op) instead of the stale outgoing order.
   */
  resyncOrder(): void {
    this.syncOrder();
  }

  /** Releases engine-held async work (outstanding resolutions). */
  dispose(): void {
    this.clipboard.abortResolutions();
  }

  // ---- internals ----

  /**
   * The nearest surviving view index around an old view row (searching the
   * before-order outward), or -1 when nothing in that neighbourhood survived.
   */
  private nearestSurvivingIndex(oldRow: number, before: TmGridOrderSnapshot): number {
    const ids = before.visibleIds;
    for (let distance = 1; distance < ids.length; distance++) {
      // Successor before predecessor: on a tie the row that slid up into the
      // vacated position wins (the spreadsheet convention on a row delete).
      for (const candidate of [oldRow + distance, oldRow - distance]) {
        if (candidate < 0 || candidate >= ids.length) {
          continue;
        }
        const id = ids[candidate];
        if (id === undefined) {
          continue;
        }
        const index = this.model.viewIndexOfRow(id);
        if (index !== -1) {
          return index;
        }
      }
    }
    return -1;
  }

  /** First activation: cell 0,0 when the grid has any cell. */
  private activateDefault(): void {
    if (
      untracked(() => this.model.viewRowCount()) > 0 &&
      untracked(() => this.model.columnCount()) > 0
    ) {
      const cell = { row: 0, col: 0 };
      this.nav.setActive(cell);
      this.selection.collapseTo(cell);
    }
  }

  private afterRowInsert(created: readonly TmGridRowSnapshot<T>[], activeCol: number): void {
    if (created.length === 0) {
      return;
    }
    const viewRow = this.model.viewIndexOfRow(created[0].id);
    if (viewRow !== -1) {
      const cell = { row: viewRow, col: activeCol };
      this.nav.setActive(cell);
      this.selection.collapseTo(cell);
    }
    this.options.host.onNotice?.({ kind: 'rowsInserted', count: created.length });
    this.syncOrder();
  }

  /**
   * After undo/redo: expand the ancestors of every affected row, restore the
   * op's captured selection (or, failing that, re-select the affected bounding
   * range), and hand the component the reveal target.
   */
  private revealAfterHistory(reveal: TmGridHistoryReveal, direction: 'undo' | 'redo'): void {
    void direction;
    const { rowIds, columnIds } = reveal;
    this.syncOrder();
    for (const id of rowIds) {
      this.model.expandAncestorsOf(id);
    }
    // The op stamped the exact selection to restore (pre-op on undo, post-op on
    // redo). Restore it verbatim when it still resolves — the bounding-box
    // fallback below is wrong for ops whose written cells don't match the
    // selection (fill-down omits its source row; a cut-move writes both ends).
    if (reveal.selection !== undefined) {
      const restored = this.selection.restore(reveal.selection as TmGridSelectionSnapshot);
      if (restored.restored && restored.activeCell !== null) {
        this.nav.setActive(restored.activeCell);
        const range = untracked(() => this.selection.activeRange()) ?? {
          anchor: restored.activeCell,
          focus: restored.activeCell,
          kind: 'cells' as const,
        };
        this.options.host.onReveal?.({ cell: restored.activeCell, range });
        this.syncOrder();
        return;
      }
    }
    let top = Number.POSITIVE_INFINITY;
    let bottom = -1;
    for (const id of rowIds) {
      const index = this.model.viewIndexOfRow(id);
      if (index !== -1) {
        top = Math.min(top, index);
        bottom = Math.max(bottom, index);
      }
    }
    if (bottom === -1) {
      return;
    }
    let left = Number.POSITIVE_INFINITY;
    let right = -1;
    for (const id of columnIds) {
      const index = this.model.columnIndexOf(id);
      if (index !== -1) {
        left = Math.min(left, index);
        right = Math.max(right, index);
      }
    }
    if (right === -1) {
      left = 0;
      right = Math.max(0, untracked(() => this.model.columnCount()) - 1);
    }
    const cell = { row: top, col: left };
    this.nav.setActive(cell);
    this.selection.collapseTo(cell);
    this.selection.extendActiveTo({ row: bottom, col: right });
    const range: TmGridRange = {
      anchor: cell,
      focus: { row: bottom, col: right },
      kind: 'cells',
    };
    this.options.host.onReveal?.({ cell, range });
    this.syncOrder();
  }

  private syncOrder(): void {
    this.lastOrder = this.model.captureOrder();
  }
}
