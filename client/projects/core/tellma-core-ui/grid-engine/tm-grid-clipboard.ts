// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signal, untracked, type Signal } from '@angular/core';

import {
  TM_PARSE_ERROR,
  type SignalLike,
  type TmLabelResolution,
  type TmPasteContext,
  type TmRowId,
} from '@tellma/core-ui/contracts';

import type { TmGridCellAnnotations, TmGridInvalidInputReason } from './tm-grid-cell-annotations';
import type { TmGridClipboardMeta } from './tm-grid-clipboard-serialize';
import type { TmGridDataModel } from './tm-grid-data-model';
import type { TmGridEngineHost } from './tm-grid-host';
import type { TmGridCellWrite, TmGridCompoundHandle, TmGridHistory } from './tm-grid-history';
import type { TmGridNav } from './tm-grid-nav';
import type { TmGridPasteSource } from './tm-grid-paste-source';
import type { TmGridSelectionModel } from './tm-grid-selection';
import type { TmRowCol } from './tm-grid-types';

/** What a copy extracts (serialization to TSV/HTML happens outside the engine). */
export interface TmGridCopyPayload {
  /** The display strings, row-major over the compacted selection. */
  readonly matrix: ReadonlyArray<readonly string[]>;
  /**
   * Per-cell raw values aligned with `matrix` (`undefined` = no raw value:
   * accessor columns and invalid-input cells — whose raw text must
   * round-trip as text, not as the cleared model value). Raw values are
   * kept regardless of copy size so a same-session paste of a large copy
   * stays typed; the clipboard's HTML flavor sheds them for oversize copies.
   */
  readonly rawValues: ReadonlyArray<ReadonlyArray<{ readonly value: unknown } | undefined>>;
  /** The copied rows' identities, present when the copy is full rows. */
  readonly rowIds?: readonly TmRowId[];
  /** The metadata for the HTML flavor. */
  readonly meta: TmGridClipboardMeta;
  /** The header labels row (only for copy-with-headers). */
  readonly headerRow?: readonly string[];
  /** Total copied cell count. */
  readonly cellCount: number;
}

/** One column's batched label-resolution request produced by a paste. */
export interface TmGridResolutionRequest {
  /** Request identity (hand back to `applyResolution`). */
  readonly id: number;
  /** The target column. */
  readonly columnId: string;
  /** The distinct unresolved labels, in first-seen order. */
  readonly labels: readonly string[];
  /** The context to pass to the column's resolver (carries the abort signal). */
  readonly context: TmPasteContext;
}

/** The synchronous outcome of a paste. */
export interface TmGridPasteResult {
  /** Cells written synchronously (typed or parsed). */
  readonly cellsWritten: number;
  /** Cells that became invalid inputs synchronously. */
  readonly errors: number;
  /** Rows materialized for overflow. */
  readonly rowsMaterialized: number;
  /** Overflow rows dropped because no row factory is bound. */
  readonly rowsDropped: number;
  /**
   * The batched label resolutions to run (one per column). The caller runs
   * each column's resolver and hands the outcome to `applyResolution`.
   */
  readonly resolutions: readonly TmGridResolutionRequest[];
}

/** The armed cut, awaiting a same-grid paste to become a move. */
export interface TmGridPendingCut {
  /** The cut rows, in view order at cut time (identity-tracked). */
  readonly rowIds: readonly TmRowId[];
  /** The cut columns' ids. */
  readonly columnIds: readonly string[];
  /** Whether the cut was full rows (a matching paste moves the rows). */
  readonly isFullRows: boolean;
  /** Fingerprint of the cut payload's text flavor (same-payload check). */
  readonly fingerprint: string;
}

/**
 * The range a plain copy marks with the dashed marquee (no deferred move —
 * unlike {@link TmGridPendingCut}, a copy never mutates on paste). Cleared by
 * the same gestures that disarm a cut: Esc, any edit, a fresh clipboard op.
 */
export interface TmGridMarquee {
  /** The copied rows, in view order at copy time (identity-tracked). */
  readonly rowIds: readonly TmRowId[];
  /** The copied columns' ids. */
  readonly columnIds: readonly string[];
}

/** Construction inputs of {@link TmGridClipboard}. */
export interface TmGridClipboardOptions<T = unknown> {
  /** The data model. */
  readonly model: TmGridDataModel<T>;
  /** The selection (copy shapes, paste anchor). */
  readonly selection: TmGridSelectionModel;
  /** The navigation state (paste anchor fallback). */
  readonly nav: TmGridNav;
  /** The annotation store (invalid inputs, pending marks, tokens). */
  readonly annotations: TmGridCellAnnotations;
  /** The history stack (every paste/cut/fill is one entry). */
  readonly history: TmGridHistory<T>;
  /** The cell's display text including the invalid-input overlay. */
  displayText(cell: TmRowCol): string;
  /** Whether the grid is editable. */
  readonly editable: SignalLike<boolean>;
  /** Whether overflow rows may materialize. */
  readonly canAddRows: SignalLike<boolean>;
  /** The active locale. */
  readonly locale: SignalLike<string>;
  /** The tenant id (metadata + cross-tenant guard). */
  readonly tenantId?: SignalLike<string | undefined>;
  /** The parent-id model key of an editable tree (row-move re-parenting). */
  readonly parentIdKey?: string;
  /** Component-layer callbacks. */
  readonly host?: Pick<TmGridEngineHost<T>, 'onNotice'>;
}

interface AwaitingCell {
  readonly rowId: TmRowId;
  readonly columnId: string;
  readonly columnKey: string;
  readonly label: string;
  readonly token: number;
}

/** One paste's open-entry accounting while its resolutions are in flight. */
interface OpenPaste {
  readonly handle: TmGridCompoundHandle;
  outstanding: number;
  resolved: number;
  errors: number;
}

interface OutstandingRequest {
  readonly id: number;
  readonly columnId: string;
  readonly cells: readonly AwaitingCell[];
  readonly controller: AbortController;
  readonly paste: OpenPaste;
}

/**
 * Clipboard semantics over the engine state: copy/cut extraction from the
 * (compacted) selection, the paste pipeline — header-row skip, target
 * shaping (fill/tile/anchor/overflow), the typed→parse→resolver conversion
 * ladder, one-undo-entry integrity across async resolutions with per-cell
 * sequence-token interleaving guards — the deferred cut-move, and
 * fill-down. Everything addresses rows by identity, so background data
 * changes never corrupt an in-flight operation.
 */
export class TmGridClipboard<T = unknown> {
  private readonly options: TmGridClipboardOptions<T>;
  private readonly pendingCutSignal = signal<TmGridPendingCut | null>(null);
  private readonly copyMarqueeSignal = signal<TmGridMarquee | null>(null);
  private readonly outstanding = new Map<number, OutstandingRequest>();
  private nextRequestId = 1;

  /** The armed cut, or `null`. */
  readonly pendingCut: Signal<TmGridPendingCut | null>;

  /**
   * The plain-copy marquee range, or `null`. Mutually exclusive with
   * {@link pendingCut} (a cut supersedes it, and vice versa) — the render
   * layer draws the marquee from whichever is set.
   */
  readonly copyMarquee: Signal<TmGridMarquee | null>;

  constructor(options: TmGridClipboardOptions<T>) {
    this.options = options;
    this.pendingCut = this.pendingCutSignal.asReadonly();
    this.copyMarquee = this.copyMarqueeSignal.asReadonly();
  }

  /**
   * Extracts the selection for copy. Returns `null` when the selection is
   * misaligned (refused, with a notice) or empty. `withHeaders` adds the
   * header row and flags it in the metadata.
   */
  copy(opts?: { withHeaders?: boolean }): TmGridCopyPayload | null {
    if (untracked(() => this.options.selection.ranges()).length === 0) {
      return null; // nothing selected — nothing to refuse or announce
    }
    // A fresh copy gesture supersedes any armed cut (spreadsheet rule):
    // copying the cut range then pasting must not perform the destructive move.
    this.cancelCut();
    const shape = this.options.selection.compactForCopy();
    if (shape === null) {
      this.options.host?.onNotice?.({ kind: 'copyRefusedMisaligned' });
      return null;
    }
    if (shape.rows.length === 0 || shape.cols.length === 0) {
      return null;
    }
    const model = this.options.model;
    // Mark the copied range with the dashed marquee (§9.5) — like a cut, but
    // with no deferred move. `cancelCut()` above cleared any prior marquee; a
    // later cut supersedes this one (it clears it after arming its own).
    this.copyMarqueeSignal.set({
      rowIds: shape.rows
        .map((row) => model.rowAt(row)?.id)
        .filter((id): id is TmRowId => id !== undefined),
      columnIds: shape.cols.map((col) => model.columnAt(col).id),
    });
    const cellCount = shape.rows.length * shape.cols.length;
    const matrix: string[][] = [];
    const rawValues: Array<Array<{ readonly value: unknown } | undefined>> = [];
    for (const row of shape.rows) {
      const textRow: string[] = [];
      const rawRow: Array<{ readonly value: unknown } | undefined> = [];
      const view = model.rowAt(row);
      for (const col of shape.cols) {
        const cell = { row, col };
        textRow.push(this.options.displayText(cell));
        if (view === null) {
          rawRow.push(undefined);
        } else {
          const column = model.columnAt(col);
          const invalid =
            view !== null && this.options.annotations.invalidInput(view.id, column.id);
          // An invalid input's raw text must round-trip as text — never as
          // the cleared model value a typed paste would silently write.
          rawRow.push(invalid ? undefined : { value: column.getValue(view.row) });
        }
      }
      matrix.push(textRow);
      rawValues.push(rawRow);
    }
    const ranges = untracked(() => this.options.selection.ranges());
    // Full rows: an explicit row/all selection, OR a cell selection that spans
    // every column (whole rows picked cell-by-cell). Both carry row identities
    // so a same-grid paste MOVES the rows instead of pasting values (§9.5).
    const spansEveryColumn = shape.cols.length === untracked(() => model.columnCount());
    const isFullRows =
      ranges.length > 0 &&
      (spansEveryColumn ||
        ranges.every((range) => range.kind === 'rows' || range.kind === 'all'));
    const rowIds = isFullRows
      ? shape.rows
          .map((row) => model.rowAt(row)?.id)
          .filter((id): id is TmRowId => id !== undefined)
      : undefined;
    const withHeaders = opts?.withHeaders === true;
    const meta: TmGridClipboardMeta = {
      v: 1,
      tenantId: untracked(() => this.options.tenantId?.()),
      locale: untracked(() => this.options.locale()),
      cols: shape.cols.map((col) => {
        const column = model.columnAt(col);
        return { key: column.key, type: column.type };
      }),
      ...(withHeaders ? { headers: true } : {}),
    };
    return {
      matrix,
      rawValues,
      rowIds,
      meta,
      headerRow: withHeaders
        ? shape.cols.map((col) => model.columnAt(col).headerLabel())
        : undefined,
      cellCount,
    };
  }

  /**
   * Copy + arms the deferred move. `fingerprint` is the text flavor's
   * fingerprint (computed by the serialization layer) — a later paste in
   * this grid whose payload matches performs the move.
   */
  cut(fingerprint: (payload: TmGridCopyPayload) => string): TmGridCopyPayload | null {
    if (!untracked(() => this.options.editable())) {
      return this.copy();
    }
    const payload = this.copy();
    if (payload === null) {
      return null;
    }
    // The deferred move is a single rectangle: arm it over the ACTIVE range's
    // rect, not the whole compacted (possibly multi-range) selection — a
    // multi-range compaction would clear cells outside any real range.
    const rect = this.options.selection.activeRect();
    if (rect === null) {
      return payload;
    }
    const model = this.options.model;
    const rowIds: TmRowId[] = [];
    for (let row = rect.top; row <= rect.bottom; row++) {
      const id = model.rowAt(row)?.id;
      if (id !== undefined) {
        rowIds.push(id);
      }
    }
    const columnIds: string[] = [];
    for (let col = rect.left; col <= rect.right; col++) {
      const column = model.columnAt(col);
      if (column !== undefined) {
        columnIds.push(column.id);
      }
    }
    this.pendingCutSignal.set({
      rowIds,
      columnIds,
      isFullRows: payload.rowIds !== undefined,
      fingerprint: fingerprint(payload),
    });
    // The cut owns the marquee now; drop the copy marquee `copy()` just armed.
    this.copyMarqueeSignal.set(null);
    return payload;
  }

  /** Disarms the pending cut AND the copy marquee (Esc, any edit, mode flips). */
  cancelCut(): void {
    if (untracked(this.pendingCutSignal) !== null) {
      this.pendingCutSignal.set(null);
    }
    if (untracked(this.copyMarqueeSignal) !== null) {
      this.copyMarqueeSignal.set(null);
    }
  }

  /** Drops pending-cut rows that vanished; disarms when none survive. */
  reconcileCut(): void {
    const cut = untracked(this.pendingCutSignal);
    if (cut === null) {
      return;
    }
    const model = this.options.model;
    const surviving = cut.rowIds.filter((id) => model.modelIndexOfRow(id) !== -1);
    if (surviving.length === 0) {
      this.pendingCutSignal.set(null);
    } else if (surviving.length !== cut.rowIds.length) {
      this.pendingCutSignal.set({ ...cut, rowIds: surviving });
    }
  }

  /**
   * The paste pipeline over an already-reduced source. Returns the
   * synchronous outcome plus the batched resolution requests the caller
   * must run; the paste's history entry stays open until every request is
   * answered (or the paste is undone, which aborts them).
   */
  paste(source: TmGridPasteSource, sourceFingerprint?: string): TmGridPasteResult {
    const empty: TmGridPasteResult = {
      cellsWritten: 0,
      errors: 0,
      rowsMaterialized: 0,
      rowsDropped: 0,
      resolutions: [],
    };
    if (!untracked(() => this.options.editable())) {
      return empty;
    }
    const anchor = this.pasteAnchor();
    if (anchor === null) {
      return empty;
    }

    // Header-row skip: the metadata flag decides when present; otherwise the
    // content heuristic runs against the target columns.
    let matrix = source.matrix;
    let rawValues = source.rawValues;
    let rowIds = source.rowIds;
    const hasHeader =
      source.hasHeaderRow ?? this.detectHeaderRow(matrix, anchor.col);
    if (hasHeader && matrix.length > 0) {
      matrix = matrix.slice(1);
      rawValues = rawValues?.slice(1);
      rowIds = rowIds?.slice(1);
    }
    if (matrix.length === 0 || matrix.every((row) => row.length === 0)) {
      return empty;
    }

    // A matching pending cut turns this paste into a move.
    const cut = untracked(this.pendingCutSignal);
    if (cut !== null && sourceFingerprint !== undefined && cut.fingerprint === sourceFingerprint) {
      this.pendingCutSignal.set(null);
      if (cut.isFullRows && rowIds !== undefined && rowIds.length > 0) {
        if (this.moveRows(rowIds, anchor) !== 'unresolved') {
          return empty; // moved, or a deliberate rejection — done either way
        }
        // The cut rows couldn't be resolved against the current model — e.g.
        // the HTML rung coerced string ids that look numeric. Fall back to a
        // cell paste so the gesture is never silently dropped.
      }
      return this.pasteCells(matrix, rawValues, source.meta, anchor, cut);
    }
    if (cut !== null) {
      // Any other paste is an edit — it disarms the cut (spreadsheet rule).
      this.pendingCutSignal.set(null);
    }
    return this.pasteCells(matrix, rawValues, source.meta, anchor, null);
  }

  /**
   * Hands one column's resolver outcome back to the paste that requested
   * it. Stale results (any later write bumped a cell's token, or the paste
   * was undone) are discarded per cell.
   */
  applyResolution(
    requestId: number,
    results: ReadonlyMap<string, TmLabelResolution<unknown>>,
    opts?: { readonly failed?: boolean },
  ): void {
    const request = this.outstanding.get(requestId);
    if (request === undefined) {
      return;
    }
    this.outstanding.delete(requestId);
    const paste = request.paste;
    paste.outstanding--;
    const model = this.options.model;
    const annotations = this.options.annotations;
    const writes: TmGridCellWrite[] = [];
    for (const cell of request.cells) {
      if (annotations.currentToken(cell.rowId, cell.columnId) !== cell.token) {
        // A later write/paste owns this cell now (its token moved on): this
        // result is stale. Leave the pending mark for whoever owns it — a
        // newer request will clear it — so pendingCount can't drop early.
        continue;
      }
      annotations.setPending(cell.rowId, cell.columnId, false);
      if (model.modelIndexOfRow(cell.rowId) === -1) {
        continue;
      }
      const row = model.rowById(cell.rowId);
      const colIndex = model.columnIndexOf(cell.columnId);
      const column = colIndex === -1 ? undefined : model.columnAt(colIndex);
      if (row === undefined || column === undefined) {
        continue;
      }
      const before = column.getValue(row as T);
      const invalidBefore = annotations.invalidInput(cell.rowId, cell.columnId) ?? null;
      // A rejected resolver never checked the label — it stays an invalid
      // input, but under a distinct reason (retryable), not a definitive
      // 'not found'.
      const fallback = opts?.failed === true ? ('resolutionFailed' as const) : ('notFound' as const);
      const outcome = results.get(cell.label) ?? { error: fallback };
      if ('value' in outcome) {
        writes.push({
          rowId: cell.rowId,
          columnId: cell.columnId,
          columnKey: cell.columnKey,
          before,
          after: outcome.value,
          invalidBefore,
          invalidAfter: null,
        });
        paste.resolved++;
      } else {
        writes.push({
          rowId: cell.rowId,
          columnId: cell.columnId,
          columnKey: cell.columnKey,
          before,
          after: column.clearedValue,
          invalidBefore,
          invalidAfter: { rawText: cell.label, reason: outcome.error },
        });
        paste.errors++;
      }
    }
    paste.handle.applyWrites(writes);
    if (paste.outstanding === 0) {
      paste.handle.finalize();
      this.options.host?.onNotice?.({
        kind: 'resolutionComplete',
        resolved: paste.resolved,
        errors: paste.errors,
      });
    }
  }

  /**
   * Fill down: copies the active range's top row into the rows below it; a
   * single-cell selection copies the cell above instead. Model values are
   * copied (never invalid-input raw texts); readonly cells and the
   * placeholder row are skipped. One undo entry.
   */
  fillDown(): void {
    if (!untracked(() => this.options.editable())) {
      return;
    }
    const model = this.options.model;
    const rect = this.options.selection.activeRect();
    if (rect === null) {
      return;
    }
    const single = rect.top === rect.bottom && rect.left === rect.right;
    const sourceRow = single ? rect.top - 1 : rect.top;
    const firstTarget = single ? rect.top : rect.top + 1;
    if (sourceRow < 0 || model.isPlaceholder(sourceRow) || model.rowAt(sourceRow) === null) {
      return;
    }
    const writes: TmGridCellWrite[] = [];
    for (let col = rect.left; col <= rect.right; col++) {
      const column = model.columnAt(col);
      if (column === undefined || column.key === null) {
        continue;
      }
      const value = model.cellValue({ row: sourceRow, col });
      for (let row = firstTarget; row <= rect.bottom; row++) {
        if (model.isPlaceholder(row) || !model.isCellEditable({ row, col })) {
          continue;
        }
        const view = model.rowAt(row);
        if (view === null) {
          continue;
        }
        writes.push({
          rowId: view.id,
          columnId: column.id,
          columnKey: column.key,
          before: column.getValue(view.row),
          after: value,
          invalidBefore: this.options.annotations.invalidInput(view.id, column.id) ?? null,
          invalidAfter: null,
        });
      }
    }
    this.options.history.runCellWrites('fillDown', writes);
  }

  /** Aborts every outstanding resolution (dispose, mode flips). */
  abortResolutions(): void {
    const pastes = new Set<OpenPaste>();
    for (const request of this.outstanding.values()) {
      request.controller.abort();
      for (const cell of request.cells) {
        this.options.annotations.setPending(cell.rowId, cell.columnId, false);
      }
      pastes.add(request.paste);
    }
    this.outstanding.clear();
    for (const paste of pastes) {
      paste.outstanding = 0;
      paste.handle.finalize();
    }
  }

  // ---- internals ----

  /** The paste anchor: the active range's top-start corner, else the active cell. */
  private pasteAnchor(): TmRowCol | null {
    const rect = this.options.selection.activeRect();
    if (rect !== null) {
      return { row: rect.top, col: rect.left };
    }
    return untracked(() => this.options.nav.activeCell());
  }

  /**
   * The header-row content heuristic: the first pasted row is a header when
   * its cells, compared position-wise against the target columns' header
   * labels (trimmed, case-insensitive), match in every non-empty cell
   * across at least two columns. Single-column pastes never trigger it.
   */
  private detectHeaderRow(matrix: ReadonlyArray<readonly string[]>, anchorCol: number): boolean {
    if (matrix.length === 0) {
      return false;
    }
    const first = matrix[0];
    if (first.length < 2) {
      return false;
    }
    const model = this.options.model;
    let matches = 0;
    for (let c = 0; c < first.length; c++) {
      const cellText = first[c].trim();
      if (cellText === '') {
        continue;
      }
      const column = model.columnAt(anchorCol + c);
      if (column === undefined) {
        continue; // beyond the last column — those cells drop anyway
      }
      if (cellText.toLowerCase() === column.headerLabel().trim().toLowerCase()) {
        matches++;
      } else {
        return false; // one non-empty mismatch disproves the header row
      }
    }
    return matches >= 2;
  }

  /**
   * The full-row move (a matching full-row cut pasted in the same grid).
   * `'moved'` on success, `'rejected'` for a deliberate no-op/refusal (paste
   * onto self, move into own subtree), `'unresolved'` when the cut rows can't
   * be resolved at all (foreign-rung id coercion) — the caller then falls
   * back to a cell paste rather than dropping the gesture.
   */
  private moveRows(
    cutRowIds: readonly TmRowId[],
    anchor: TmRowCol,
  ): 'moved' | 'rejected' | 'unresolved' {
    const model = this.options.model;
    const surviving = cutRowIds.filter((id) => model.modelIndexOfRow(id) !== -1);
    if (surviving.length === 0) {
      return 'unresolved';
    }
    const targetView = model.rowAt(anchor.row);
    const targetIsPlaceholder = model.isPlaceholder(anchor.row) || targetView === null;
    // Pasting onto one of the moved rows is a no-op.
    if (!targetIsPlaceholder && surviving.includes(targetView.id)) {
      return 'rejected';
    }
    // A row cannot move into its own subtree.
    if (
      !targetIsPlaceholder &&
      surviving.some(
        (id) => targetView.id === id || model.isDescendantOf(targetView.id, id),
      )
    ) {
      this.options.host?.onNotice?.({ kind: 'moveIntoDescendantRejected' });
      return 'rejected';
    }
    // Each moved row carries its whole subtree, in flattened order. Only the
    // cut ROOTS — cut rows with no cut ancestor — expand; a nested cut row
    // (a descendant of another cut row) travels once, inside its ancestor's
    // subtree. Walking every survivor would emit a shared descendant under
    // both its ancestor AND itself, duplicating the row in the move.
    const survivingSet = new Set(surviving);
    const roots = surviving.filter(
      (id) => !model.ancestorsOf(id).some((ancestor) => survivingSet.has(ancestor)),
    );
    const movedWithSubtrees: TmRowId[] = [];
    const seen = new Set<TmRowId>();
    for (const id of roots) {
      for (const member of model.subtreeRowIds(id)) {
        if (!seen.has(member)) {
          seen.add(member);
          movedWithSubtrees.push(member);
        }
      }
    }
    const beforeRowId = targetIsPlaceholder ? null : targetView.id;
    const newParentId = targetIsPlaceholder ? null : targetView.parentId;
    // Re-parenting writes for the moved ROOTS whose parent changes; nested
    // cut rows keep their (also-moving) ancestor as parent, so they are not
    // re-parented.
    const parentWrites: TmGridCellWrite[] = [];
    const parentKey = this.options.parentIdKey;
    if (model.isTree && parentKey !== undefined) {
      for (const id of roots) {
        const currentParent = model.ancestorsOf(id)[0] ?? null;
        if (currentParent !== newParentId) {
          parentWrites.push({
            rowId: id,
            columnId: parentKey,
            columnKey: parentKey,
            before: currentParent,
            after: newParentId,
            invalidBefore: null,
            invalidAfter: null,
          });
        }
      }
    }
    this.options.history.runRowMove(movedWithSubtrees, beforeRowId, parentWrites);
    // Select the moved rows at their new position (Excel/Sheets behavior); they
    // land contiguously, subtrees included, before the target row.
    const movedIndices = movedWithSubtrees
      .map((id) => model.viewIndexOfRow(id))
      .filter((index) => index !== -1);
    if (movedIndices.length > 0) {
      const top = Math.min(...movedIndices);
      this.options.nav.setActive({ row: top, col: 0 });
      this.options.selection.selectRows(top, Math.max(...movedIndices), false);
    }
    this.options.host?.onNotice?.({ kind: 'rowsMoved', count: surviving.length });
    return 'moved';
  }

  /** The cell paste: shaping, conversion ladder, one open history entry. */
  private pasteCells(
    matrix: ReadonlyArray<readonly string[]>,
    rawValues: TmGridPasteSource['rawValues'],
    meta: TmGridClipboardMeta | undefined,
    anchor: TmRowCol,
    cut: TmGridPendingCut | null,
  ): TmGridPasteResult {
    const model = this.options.model;
    const srcHeight = matrix.length;
    const srcWidth = matrix.reduce((max, row) => Math.max(max, row.length), 0);
    if (srcHeight === 0 || srcWidth === 0) {
      return { cellsWritten: 0, errors: 0, rowsMaterialized: 0, rowsDropped: 0, resolutions: [] };
    }
    // Target shaping: a single value fills the whole selection; an exact
    // multiple tiles it; anything else pastes once from the anchor.
    const rect = this.options.selection.activeRect();
    let height = srcHeight;
    let width = srcWidth;
    if (rect !== null) {
      const rectHeight = rect.bottom - rect.top + 1;
      const rectWidth = rect.right - rect.left + 1;
      if (
        (srcHeight === 1 && srcWidth === 1) ||
        (rectHeight % srcHeight === 0 && rectWidth % srcWidth === 0)
      ) {
        height = rectHeight;
        width = rectWidth;
      }
    }
    // Column overflow drops; row overflow materializes when a factory exists.
    width = Math.min(width, model.columnCount() - anchor.col);
    const dataRows = untracked(() => model.dataRowCount());
    const existingCount = Math.max(0, Math.min(height, dataRows - anchor.row));
    // Overflow-only cap: trailing all-empty source rows never materialize NEW
    // rows. Google Sheets' "select all" copies the data table plus hundreds of
    // blank trailing rows (Excel copies only the used range); without this, each
    // blank row would spawn a blank grid row. Only the OVERFLOW tail is trimmed
    // (rows landing on existing rows still clear them, Excel parity), and only
    // when anchoring — a paste tiled into a selection keeps the chosen extent.
    if (height === srcHeight) {
      let end = height;
      while (end > existingCount && matrix[end - 1].every((cell) => cell === '')) {
        end--;
      }
      height = end;
    }
    let overflow = height - existingCount;
    let rowsDropped = 0;
    if (overflow > 0 && !untracked(() => this.options.canAddRows())) {
      rowsDropped = overflow;
      overflow = 0;
      height = existingCount;
    }

    // Capture the existing target rows' identities BEFORE any insertion, so
    // materialization can never shift what the writes address.
    const existingIds: Array<TmRowId | null> = [];
    const existingEditable: boolean[][] = [];
    for (let i = 0; i < existingCount; i++) {
      const viewRow = anchor.row + i;
      existingIds.push(model.rowAt(viewRow)?.id ?? null);
      const editableRow: boolean[] = [];
      for (let j = 0; j < width; j++) {
        editableRow.push(model.isCellEditable({ row: viewRow, col: anchor.col + j }));
      }
      existingEditable.push(editableRow);
    }

    const handle = this.options.history.beginCompound(cut !== null ? 'cutMove' : 'paste');
    // Overflow rows: appended to the model; in a tree they become siblings
    // of the last existing target row (its parent), roots otherwise.
    let materialized: ReadonlyArray<{ readonly id: TmRowId }> = [];
    if (overflow > 0) {
      const lastExisting = existingCount > 0 ? model.rowAt(anchor.row + existingCount - 1) : null;
      const parentRowId = lastExisting?.parentId ?? null;
      materialized = handle.insertRows(
        untracked(() => model.modelRowCount()),
        overflow,
        parentRowId,
      );
    }

    const targetRowId = (index: number): TmRowId | null => {
      if (index < existingCount) {
        return existingIds[index];
      }
      return materialized[index - existingCount]?.id ?? null;
    };

    const sourceLocale = meta?.locale;
    const sourceTenantId = meta?.tenantId;
    const tenantId = untracked(() => this.options.tenantId?.());
    const locale = untracked(() => this.options.locale());
    // Both-undefined tenants match: the tenant seam is optional, so the
    // default (unset) config must still reach the typed fast path — a
    // requiring-`!== undefined` guard left it permanently unreachable there.
    const sameTenant = sourceTenantId === tenantId;
    const writes: TmGridCellWrite[] = [];
    const collect = new Map<
      string,
      { columnKey: string; labels: string[]; seen: Set<string>; cells: AwaitingCell[] }
    >();
    let errors = 0;
    // Cells that received a definite value now (typed/empty/parsed) — the
    // announced paste count. Resolver placeholders (counted as pending),
    // parse errors, and the cut-source clears below are excluded.
    let valueWrites = 0;

    for (let i = 0; i < height; i++) {
      const rowId = targetRowId(i);
      if (rowId === null) {
        continue;
      }
      const isMaterialized = i >= existingCount;
      const sr = i % srcHeight;
      for (let j = 0; j < width; j++) {
        const col = anchor.col + j;
        const column = model.columnAt(col);
        if (column === undefined || column.key === null || !column.editable) {
          continue;
        }
        if (!isMaterialized && !existingEditable[i][j]) {
          continue; // readonly cells are skipped in place, never shifted around
        }
        const sc = j % srcWidth;
        const text = matrix[sr][sc] ?? '';
        const row = model.rowById(rowId);
        const before = row === undefined ? null : column.getValue(row as T);
        const invalidBefore = this.options.annotations.invalidInput(rowId, column.id) ?? null;
        const base = { rowId, columnId: column.id, columnKey: column.key, before, invalidBefore };

        // (1) Typed fast path: same column type + same tenant + a raw value.
        const raw = rawValues?.[sr]?.[sc];
        const metaCol = meta?.cols?.[sc];
        if (raw !== undefined && sameTenant && metaCol?.type === column.type) {
          writes.push({ ...base, after: raw.value, invalidAfter: null });
          valueWrites++;
          continue;
        }
        // (2) Empty text writes the cleared value (never hits the resolver).
        if (text === '') {
          writes.push({ ...base, after: column.clearedValue, invalidAfter: null });
          valueWrites++;
          continue;
        }
        // (3) The synchronous parse.
        if (column.parse !== undefined) {
          const parsed = column.parse(text, { locale, sourceLocale });
          if (parsed !== TM_PARSE_ERROR) {
            writes.push({ ...base, after: parsed, invalidAfter: null });
            valueWrites++;
            continue;
          }
        }
        // (4) The async resolver; without one, a definitive invalid input.
        if (column.hasResolver) {
          writes.push({ ...base, after: column.clearedValue, invalidAfter: null });
          let entry = collect.get(column.id);
          if (entry === undefined) {
            entry = { columnKey: column.key, labels: [], seen: new Set<string>(), cells: [] };
            collect.set(column.id, entry);
          }
          if (!entry.seen.has(text)) {
            entry.seen.add(text);
            entry.labels.push(text);
          }
          entry.cells.push({ rowId, columnId: column.id, columnKey: column.key, label: text, token: 0 });
          continue;
        }
        writes.push({
          ...base,
          after: column.clearedValue,
          invalidAfter: { rawText: text, reason: 'parse' satisfies TmGridInvalidInputReason },
        });
        errors++;
      }
    }

    // Cut source clearing (cell-rectangle move): cells not overwritten by
    // the paste itself clear to their column's cleared value, same entry.
    if (cut !== null) {
      // Type-prefixed keys: numeric id 1 and string id '1' must not collide.
      const keyOf = (rowId: TmRowId, columnId: string): string =>
        `${typeof rowId === 'number' ? '#' : '$'}${String(rowId)} ${columnId}`;
      const written = new Set(writes.map((write) => keyOf(write.rowId, write.columnId)));
      for (const rowId of cut.rowIds) {
        if (model.modelIndexOfRow(rowId) === -1) {
          continue;
        }
        const row = model.rowById(rowId);
        for (const columnId of cut.columnIds) {
          if (written.has(keyOf(rowId, columnId))) {
            continue;
          }
          const colIndex = model.columnIndexOf(columnId);
          const column = colIndex === -1 ? undefined : model.columnAt(colIndex);
          if (column === undefined || column.key === null || row === undefined) {
            continue;
          }
          // Readonly check by IDENTITY (not view coordinates): a cut-source
          // row hidden in a collapsed subtree has no view index, and must
          // get the same readonly protection as a visible one.
          if (!column.editable || column.isCellReadonly(row as T)) {
            continue;
          }
          writes.push({
            rowId,
            columnId,
            columnKey: column.key,
            before: column.getValue(row as T),
            after: column.clearedValue,
            invalidBefore: this.options.annotations.invalidInput(rowId, columnId) ?? null,
            invalidAfter: null,
          });
        }
      }
    }

    handle.applyWrites(writes);

    // Issue the batched resolutions: each awaiting cell's token is
    // established with an explicit bump here (not merely read), so a value
    // no-op paste-clear — which the history's write elision would skip — can
    // never leave the token where it was and let two requests share it. Any
    // later write (manual edit, delete, a second paste onto the cell) bumps
    // the token again, invalidating this resolution.
    const resolutions: TmGridResolutionRequest[] = [];
    let pendingCells = 0;
    if (collect.size === 0) {
      handle.finalize();
    } else {
      const paste: OpenPaste = { handle, outstanding: collect.size, resolved: 0, errors: 0 };
      for (const [columnId, entry] of collect) {
        const controller = new AbortController();
        const cells = entry.cells.map((cell) => ({
          ...cell,
          token: this.options.annotations.bumpToken(cell.rowId, cell.columnId),
        }));
        for (const cell of cells) {
          this.options.annotations.setPending(cell.rowId, cell.columnId, true);
        }
        pendingCells += cells.length;
        const request: OutstandingRequest = {
          id: this.nextRequestId++,
          columnId,
          cells,
          controller,
          paste,
        };
        this.outstanding.set(request.id, request);
        resolutions.push({
          id: request.id,
          columnId,
          labels: entry.labels,
          context: { locale, sourceLocale, sourceTenantId, signal: controller.signal },
        });
      }
      handle.onCancel(() => {
        for (const [id, request] of [...this.outstanding]) {
          if (request.paste === paste) {
            request.controller.abort();
            for (const cell of request.cells) {
              this.options.annotations.setPending(cell.rowId, cell.columnId, false);
            }
            this.outstanding.delete(id);
          }
        }
        paste.outstanding = 0;
      });
    }

    // Select the pasted block (Excel/Sheets behavior). A full-column paste thus
    // re-selects the pasted rows too; materialized overflow rows append
    // contiguously after the anchor in view order, so the block is
    // anchor..anchor+height-1 × anchor+width-1.
    if (height > 0 && width > 0) {
      const top: TmRowCol = { row: anchor.row, col: anchor.col };
      this.options.nav.setActive(top);
      this.options.selection.collapseTo(top);
      this.options.selection.extendActiveTo({
        row: anchor.row + height - 1,
        col: anchor.col + width - 1,
      });
    }

    const cellsWritten = valueWrites;
    this.options.host?.onNotice?.({
      kind: 'pasteComplete',
      cells: cellsWritten,
      errors,
      pending: pendingCells,
      rowsMaterialized: materialized.length,
      rowsDropped,
    });
    return {
      cellsWritten,
      errors,
      rowsMaterialized: materialized.length,
      rowsDropped,
      resolutions,
    };
  }
}
