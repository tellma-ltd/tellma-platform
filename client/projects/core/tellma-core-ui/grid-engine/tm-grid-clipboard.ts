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
   * oversize copies, accessor columns, invalid-input cells — whose raw
   * text must round-trip as text, not as the cleared model value).
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
  /** The tenant identity (metadata + cross-tenant guard). */
  readonly tenant?: SignalLike<string | undefined>;
  /** The parent-id model key of an editable tree (row-move re-parenting). */
  readonly parentIdKey?: string;
  /** Component-layer callbacks. */
  readonly host?: Pick<TmGridEngineHost<T>, 'onNotice'>;
  /** Cell count beyond which copies omit raw values. Defaults to 100 000. */
  readonly oversizeCellThreshold?: number;
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

const DEFAULT_OVERSIZE_THRESHOLD = 100_000;

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
  private readonly outstanding = new Map<number, OutstandingRequest>();
  private nextRequestId = 1;

  /** The armed cut, or `null`. */
  readonly pendingCut: Signal<TmGridPendingCut | null>;

  constructor(options: TmGridClipboardOptions<T>) {
    this.options = options;
    this.pendingCut = this.pendingCutSignal.asReadonly();
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
    const shape = this.options.selection.compactForCopy();
    if (shape === null) {
      this.options.host?.onNotice?.({ kind: 'copyRefusedMisaligned' });
      return null;
    }
    if (shape.rows.length === 0 || shape.cols.length === 0) {
      return null;
    }
    const model = this.options.model;
    const cellCount = shape.rows.length * shape.cols.length;
    const oversize = cellCount > (this.options.oversizeCellThreshold ?? DEFAULT_OVERSIZE_THRESHOLD);
    const matrix: string[][] = [];
    const rawValues: Array<Array<{ readonly value: unknown } | undefined>> = [];
    for (const row of shape.rows) {
      const textRow: string[] = [];
      const rawRow: Array<{ readonly value: unknown } | undefined> = [];
      const view = model.rowAt(row);
      for (const col of shape.cols) {
        const cell = { row, col };
        textRow.push(this.options.displayText(cell));
        if (oversize || view === null) {
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
    const isFullRows =
      ranges.length > 0 && ranges.every((range) => range.kind === 'rows' || range.kind === 'all');
    const rowIds = isFullRows
      ? shape.rows
          .map((row) => model.rowAt(row)?.id)
          .filter((id): id is TmRowId => id !== undefined)
      : undefined;
    const withHeaders = opts?.withHeaders === true;
    const meta: TmGridClipboardMeta = {
      v: 1,
      tenant: untracked(() => this.options.tenant?.()),
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
    const shape = this.options.selection.compactForCopy();
    if (shape === null) {
      return payload;
    }
    const model = this.options.model;
    this.pendingCutSignal.set({
      rowIds: shape.rows
        .map((row) => model.rowAt(row)?.id)
        .filter((id): id is TmRowId => id !== undefined),
      columnIds: shape.cols.map((col) => model.columnAt(col).id),
      isFullRows: payload.rowIds !== undefined,
      fingerprint: fingerprint(payload),
    });
    return payload;
  }

  /** Disarms the pending cut (Esc, any edit, mode flips). */
  cancelCut(): void {
    if (untracked(this.pendingCutSignal) !== null) {
      this.pendingCutSignal.set(null);
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
        this.moveRows(rowIds, anchor);
        return empty;
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
  applyResolution(requestId: number, results: ReadonlyMap<string, TmLabelResolution<unknown>>): void {
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
      annotations.setPending(cell.rowId, cell.columnId, false);
      if (annotations.currentToken(cell.rowId, cell.columnId) !== cell.token) {
        continue; // a later write invalidated this cell — the result is stale
      }
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
      const outcome = results.get(cell.label) ?? { error: 'notFound' as const };
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

  /** The full-row move (a matching full-row cut pasted in the same grid). */
  private moveRows(cutRowIds: readonly TmRowId[], anchor: TmRowCol): boolean {
    const model = this.options.model;
    const surviving = cutRowIds.filter((id) => model.modelIndexOfRow(id) !== -1);
    if (surviving.length === 0) {
      return false;
    }
    const targetView = model.rowAt(anchor.row);
    const targetIsPlaceholder = model.isPlaceholder(anchor.row) || targetView === null;
    // Pasting onto one of the moved rows is a no-op.
    if (!targetIsPlaceholder && surviving.includes(targetView.id)) {
      return false;
    }
    // A row cannot move into its own subtree.
    if (
      !targetIsPlaceholder &&
      surviving.some(
        (id) => targetView.id === id || model.isDescendantOf(targetView.id, id),
      )
    ) {
      this.options.host?.onNotice?.({ kind: 'moveIntoDescendantRejected' });
      return false;
    }
    // Each moved row carries its whole subtree, in flattened order.
    const movedWithSubtrees: TmRowId[] = [];
    const topLevel = new Set(surviving);
    for (const id of surviving) {
      for (const member of model.subtreeRowIds(id)) {
        if (member !== id && topLevel.has(member)) {
          continue; // a cut row inside another cut row's subtree travels once
        }
        movedWithSubtrees.push(member);
      }
    }
    const beforeRowId = targetIsPlaceholder ? null : targetView.id;
    const newParentId = targetIsPlaceholder ? null : targetView.parentId;
    // Re-parenting writes for the top-level moved rows whose parent changes.
    const parentWrites: TmGridCellWrite[] = [];
    const parentKey = this.options.parentIdKey;
    if (model.isTree && parentKey !== undefined) {
      for (const id of surviving) {
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
    this.options.host?.onNotice?.({ kind: 'rowsMoved', count: surviving.length });
    return true;
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
    const sourceTenant = meta?.tenant;
    const tenant = untracked(() => this.options.tenant?.());
    const locale = untracked(() => this.options.locale());
    const sameTenant = sourceTenant !== undefined && sourceTenant === tenant;
    const writes: TmGridCellWrite[] = [];
    const collect = new Map<
      string,
      { columnKey: string; labels: string[]; cells: AwaitingCell[] }
    >();
    let errors = 0;

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
          continue;
        }
        // (2) Empty text writes the cleared value (never hits the resolver).
        if (text === '') {
          writes.push({ ...base, after: column.clearedValue, invalidAfter: null });
          continue;
        }
        // (3) The synchronous parse.
        if (column.parse !== undefined) {
          const parsed = column.parse(text, { locale, sourceLocale });
          if (parsed !== TM_PARSE_ERROR) {
            writes.push({ ...base, after: parsed, invalidAfter: null });
            continue;
          }
        }
        // (4) The async resolver; without one, a definitive invalid input.
        if (column.hasResolver) {
          writes.push({ ...base, after: column.clearedValue, invalidAfter: null });
          let entry = collect.get(column.id);
          if (entry === undefined) {
            entry = { columnKey: column.key, labels: [], cells: [] };
            collect.set(column.id, entry);
          }
          if (!entry.labels.includes(text)) {
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
          const viewIndex = model.viewIndexOfRow(rowId);
          if (viewIndex !== -1 && !model.isCellEditable({ row: viewIndex, col: colIndex })) {
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

    // Issue the batched resolutions: tokens are read AFTER the synchronous
    // writes bumped them, so only a LATER write invalidates a cell.
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
          token: this.options.annotations.currentToken(cell.rowId, cell.columnId),
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
          context: { locale, sourceLocale, sourceTenant, signal: controller.signal },
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

    const cellsWritten = writes.length - errors;
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
