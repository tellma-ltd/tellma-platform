// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The clipboard's DOM half: ClipboardEvent handling for copy/cut (cut arms
// the engine's deferred move under one shared serialization), the oversize
// escalation to the async Clipboard API (chunked serialization across
// frames), the module-scoped in-memory descriptor LRU, and the paste
// resolution ladder that reduces the richest available clipboard flavor to
// the engine's paste-source shape.

import type { TmRowId } from '@tellma/core-ui/contracts';
import {
  tmClipboardFingerprint,
  tmPasteSourceFromDescriptor,
  tmPasteSourceFromTsv,
  tmSerializeHtmlTable,
  tmSerializeTsv,
  tmSerializeTsvChunks,
  type TmGridClipboardMeta,
  type TmGridCopyPayload,
  type TmGridPasteSource,
} from '@tellma/core-ui/grid-engine';

import type { ɵTmGridAnnouncements } from './announcements';
import { ɵtmReduceClipboardHtml } from './clipboard-html';

/**
 * Cell count beyond which a copy escalates from the synchronous
 * ClipboardEvent path to `navigator.clipboard.write` with promise-backed
 * items, so serialization spreads across frames instead of blocking the
 * copy gesture. Passed to the engine too, so both layers share one number.
 */
export const ɵTM_GRID_OVERSIZE_COPY_CELLS = 100_000;

/** Rows serialized per chunk (per yielded frame) on the oversize path. */
const OVERSIZE_ROWS_PER_CHUNK = 2_000;

/** Capacity of the in-memory copy-descriptor LRU. */
const COPY_LRU_CAPACITY = 4;

/**
 * One copy's typed descriptor, kept in memory and keyed by the fingerprint
 * of its `text/plain` flavor. A same-session paste whose actual clipboard
 * payload fingerprints to a stored entry can use the raw values directly,
 * even when the browser stripped the custom attributes from the HTML flavor.
 */
export interface ɵTmGridCopyDescriptor {
  /** Fingerprint of the copy's TSV flavor (the LRU key). */
  readonly fingerprint: string;
  /** The clipboard metadata (tenant, locale, column keys/types). */
  readonly meta: TmGridClipboardMeta;
  /** The display-string matrix. */
  readonly matrix: ReadonlyArray<readonly string[]>;
  /** Per-cell raw values aligned with `matrix` (`undefined` = no raw value). */
  readonly rawValues: ReadonlyArray<ReadonlyArray<{ readonly value: unknown } | undefined>>;
  /** The copied rows' identities, present for full-row copies. */
  readonly rowIds?: readonly TmRowId[];
}

const copyDescriptors = new Map<string, ɵTmGridCopyDescriptor>();

function rememberDescriptor(descriptor: ɵTmGridCopyDescriptor): void {
  copyDescriptors.delete(descriptor.fingerprint);
  copyDescriptors.set(descriptor.fingerprint, descriptor);
  if (copyDescriptors.size > COPY_LRU_CAPACITY) {
    const oldest = copyDescriptors.keys().next().value;
    if (oldest !== undefined) {
      copyDescriptors.delete(oldest);
    }
  }
}

/**
 * The descriptor a paste's fast path consumes. Data-only: the clipboard
 * TSV may carry a prepended header row (copy-with-headers), but the
 * descriptor stores the data matrix with the headers flag stripped, so a
 * same-session paste never mistakes the first DATA row for a header.
 */
function descriptorFor(tsv: string, payload: TmGridCopyPayload): ɵTmGridCopyDescriptor {
  const meta: TmGridClipboardMeta =
    payload.meta.headers === undefined ? payload.meta : { ...payload.meta, headers: undefined };
  return {
    fingerprint: tmClipboardFingerprint(tsv),
    meta,
    matrix: payload.matrix,
    rawValues: payload.rawValues,
    rowIds: payload.rowIds,
  };
}

/**
 * The paste fast-path lookup: the stored descriptor whose TSV flavor
 * fingerprints to `fingerprint`, or `undefined`. A hit is touched to the
 * back of the eviction order.
 */
export function ɵtmGridCopyDescriptor(fingerprint: string): ɵTmGridCopyDescriptor | undefined {
  const descriptor = copyDescriptors.get(fingerprint);
  if (descriptor !== undefined) {
    copyDescriptors.delete(fingerprint);
    copyDescriptors.set(fingerprint, descriptor);
  }
  return descriptor;
}

/** What the paste resolution ladder produced for one paste gesture. */
export interface ɵTmGridResolvedPaste {
  /** The reduced source the engine consumes. */
  readonly source: TmGridPasteSource;
  /**
   * Fingerprint of the payload's `text/plain` flavor — the engine matches
   * it against an armed cut regardless of which rung produced the source.
   */
  readonly fingerprint: string | undefined;
}

/**
 * The paste resolution ladder over the two clipboard flavors: (1) the
 * in-memory descriptor whose fingerprint matches the actual `text/plain`
 * payload (typed same-session fast path), (2) the `text/html` table
 * reduction, (3) the `text/plain` TSV parse. Returns `null` when both
 * flavors are empty (or the only HTML carries no table and no text exists).
 */
export function ɵtmGridResolvePasteSource(text: string, html: string): ɵTmGridResolvedPaste | null {
  const fingerprint = text.length > 0 ? tmClipboardFingerprint(text) : undefined;
  if (fingerprint !== undefined) {
    const descriptor = ɵtmGridCopyDescriptor(fingerprint);
    if (descriptor !== undefined) {
      return { source: tmPasteSourceFromDescriptor(descriptor), fingerprint };
    }
  }
  if (html.length > 0) {
    const reduced = ɵtmReduceClipboardHtml(html);
    if (reduced !== null) {
      return { source: reduced, fingerprint };
    }
  }
  if (text.length > 0) {
    return { source: tmPasteSourceFromTsv(text), fingerprint };
  }
  return null;
}

/** Yields control back to the event loop between serialization chunks. */
function nextFrame(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve));
}

function escapeHtmlText(text: string): string {
  return text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
}

/** A table cell's HTML: escaped, with in-cell newlines as `<br>` (Excel/Sheets). */
function escapeHtmlCellText(text: string): string {
  return escapeHtmlText(text)
    .replaceAll('\r\n', '<br>')
    .replaceAll('\n', '<br>')
    .replaceAll('\r', '<br>');
}

function escapeHtmlAttribute(text: string): string {
  return escapeHtmlText(text).replaceAll('"', '&quot;');
}

/** Whether the async Clipboard write API is usable in this context (secure, supported). */
function asyncClipboardWritable(): boolean {
  return (
    typeof navigator !== 'undefined' &&
    typeof navigator.clipboard?.write === 'function' &&
    typeof ClipboardItem !== 'undefined'
  );
}

/** Construction inputs of {@link ɵTmGridClipboardDom}. */
export interface ɵTmGridClipboardDomOptions {
  /** Extracts the current selection as a copy payload (the engine's copy). */
  copy(opts?: { withHeaders?: boolean }): TmGridCopyPayload | null;
  /**
   * The engine's cut: copies AND arms the deferred move under the
   * fingerprint the callback computes (in readonly mode the engine falls
   * back to a plain copy and never calls the callback).
   */
  cut(fingerprint: (payload: TmGridCopyPayload) => string): TmGridCopyPayload | null;
  /** The grid's live-region voice. */
  readonly announcements: ɵTmGridAnnouncements;
  /** Called when an async clipboard write rejects (transient visible notice). */
  onCopyFailed?(): void;
}

/**
 * Copy/cut ClipboardEvent handling for one grid instance: serializes the
 * engine's copy payload into both clipboard flavors, remembers the typed
 * descriptor in the module LRU, and escalates oversize copies to the async
 * Clipboard API — announcing success and failure either way (a failed copy
 * is never silent). Cut shares the exact serialization with copy, so the
 * fingerprint the engine arms its deferred move under always matches the
 * clipboard's actual `text/plain` payload; in the readonly core, cut
 * degrades to copy inside the engine.
 */
export class ɵTmGridClipboardDom {
  constructor(private readonly options: ɵTmGridClipboardDomOptions) {}

  /** The `copy` ClipboardEvent handler. */
  onCopy(event: ClipboardEvent): void {
    const payload = this.options.copy();
    if (payload === null) {
      // Nothing selected, or a misaligned multi-range selection — the
      // engine already emitted the refusal notice for the latter.
      return;
    }
    this.writeEvent(event, payload);
  }

  /**
   * The `cut` ClipboardEvent handler: the engine arms the deferred move
   * under the TSV fingerprint computed here, and the SAME serialized text
   * is what lands on the clipboard — byte-identical, so a later paste of
   * that payload fingerprints back to the armed cut.
   */
  onCut(event: ClipboardEvent): void {
    let cachedTsv: string | undefined;
    const payload = this.options.cut((cutPayload) => {
      cachedTsv = this.serializeTsv(cutPayload);
      return tmClipboardFingerprint(cachedTsv);
    });
    if (payload === null) {
      return;
    }
    this.writeEvent(event, payload, cachedTsv);
  }

  /**
   * The context-menu copy path: no ClipboardEvent exists inside a menu
   * action, so both flavors go through the async Clipboard API
   * (`navigator.clipboard.write` succeeds everywhere within a user
   * gesture). Reuses the chunked serializers — correct at any size, and
   * the descriptor still lands in the same-session paste LRU.
   */
  copyAsync(opts?: { withHeaders?: boolean }): void {
    const payload = this.options.copy(opts);
    if (payload === null) {
      return; // nothing selected, or the engine already announced a refusal
    }
    this.writeAsync(payload);
  }

  /**
   * The context-menu cut path: arms the deferred move exactly like
   * {@link onCut} — the fingerprint is computed from the synchronously
   * serialized TSV, and that same text backs the async write.
   */
  cutAsync(): void {
    let cachedTsv: string | undefined;
    const payload = this.options.cut((cutPayload) => {
      cachedTsv = this.serializeTsv(cutPayload);
      return tmClipboardFingerprint(cachedTsv);
    });
    if (payload === null) {
      return;
    }
    this.writeAsync(payload, cachedTsv);
  }

  /** The TSV flavor: copy-with-headers prepends the header row as data. */
  private serializeTsv(payload: TmGridCopyPayload): string {
    const tsvMatrix =
      payload.headerRow === undefined ? payload.matrix : [payload.headerRow, ...payload.matrix];
    return tmSerializeTsv(tsvMatrix);
  }

  /**
   * The ClipboardEvent path: synchronous below the oversize threshold,
   * escalated to the async API above it. An armed cut passes its already-
   * serialized TSV so both paths write the exact fingerprinted text.
   */
  private writeEvent(event: ClipboardEvent, payload: TmGridCopyPayload, tsv?: string): void {
    // Escalate to the async API only when it actually exists — in a non-secure
    // context (an `http://` intranet) `navigator.clipboard` is undefined, and
    // preventing the default before an unguarded throw would leave the
    // clipboard empty with no failure path (a silent failed copy). Fall back
    // to a synchronous serialize onto the event instead.
    if (payload.cellCount <= ɵTM_GRID_OVERSIZE_COPY_CELLS || !asyncClipboardWritable()) {
      this.writeSync(event, payload, tsv);
    } else {
      event.preventDefault();
      this.writeAsync(payload, tsv);
    }
  }

  private writeSync(event: ClipboardEvent, payload: TmGridCopyPayload, tsv?: string): void {
    // Copy-with-headers prepends the header row to BOTH flavors (foreign
    // targets read it as data; the HTML flavor additionally marks it so a
    // paste back into a grid skips it).
    const text = tsv ?? this.serializeTsv(payload);
    const html = tmSerializeHtmlTable({
      matrix: payload.matrix,
      meta: payload.meta,
      rawValues: payload.rawValues,
      rowIds: payload.rowIds,
      headerRow: payload.headerRow,
    });
    rememberDescriptor(descriptorFor(text, payload));
    if (event.clipboardData === null) {
      return;
    }
    event.clipboardData.setData('text/plain', text);
    event.clipboardData.setData('text/html', html);
    event.preventDefault();
    this.options.announcements.announce('grid.announce.copied', { cells: payload.cellCount });
  }

  /**
   * The async path (oversize escalations and every menu action):
   * promise-backed ClipboardItems started within the user gesture; both
   * flavors serialize in row bands with a yield between bands so no frame
   * blocks on millions of cells (an armed cut's TSV is already serialized
   * and is reused verbatim). Rejection (focus loss, expired activation,
   * permission) is announced AND surfaced as a transient visible notice —
   * it cannot be retried programmatically once the gesture is gone.
   */
  private writeAsync(payload: TmGridCopyPayload, tsv?: string): void {
    if (!asyncClipboardWritable()) {
      // A menu copy has no ClipboardEvent to fall back onto — a failed copy is
      // announced, never silent (and never a synchronous throw).
      this.options.announcements.announce('grid.announce.copyFailed');
      this.options.onCopyFailed?.();
      return;
    }
    const oversize = payload.cellCount > ɵTM_GRID_OVERSIZE_COPY_CELLS;
    const textPromise = (
      tsv !== undefined ? Promise.resolve(tsv) : this.serializeTsvChunked(payload)
    ).then((text) => {
      // Oversize copies drop raw values (engine contract), but the
      // descriptor still lets a same-session paste skip re-parsing.
      rememberDescriptor(descriptorFor(text, payload));
      return new Blob([text], { type: 'text/plain' });
    });
    // Below the threshold (menu copies of normal selections) the HTML
    // flavor keeps its per-cell raw values — only oversize payloads shed
    // them (§9.2), and only those need the chunked builder.
    const htmlPromise = (
      oversize
        ? this.serializeHtmlChunked(payload)
        : Promise.resolve(
            tmSerializeHtmlTable({
              matrix: payload.matrix,
              meta: payload.meta,
              rawValues: payload.rawValues,
              rowIds: payload.rowIds,
              headerRow: payload.headerRow,
            }),
          )
    ).then((html) => new Blob([html], { type: 'text/html' }));
    try {
      navigator.clipboard
        .write([new ClipboardItem({ 'text/plain': textPromise, 'text/html': htmlPromise })])
        .then(
          () =>
            this.options.announcements.announce('grid.announce.copied', {
              cells: payload.cellCount,
            }),
          () => {
            this.options.announcements.announce('grid.announce.copyFailed');
            this.options.onCopyFailed?.();
          },
        );
    } catch {
      // `new ClipboardItem` / `write` can throw synchronously on some engines.
      this.options.announcements.announce('grid.announce.copyFailed');
      this.options.onCopyFailed?.();
    }
  }

  private async serializeTsvChunked(payload: TmGridCopyPayload): Promise<string> {
    const parts: string[] = [];
    const tsvMatrix =
      payload.headerRow === undefined ? payload.matrix : [payload.headerRow, ...payload.matrix];
    for (const chunk of tmSerializeTsvChunks(tsvMatrix, OVERSIZE_ROWS_PER_CHUNK)) {
      parts.push(chunk);
      await nextFrame();
    }
    return parts.join('');
  }

  /**
   * Chunked HTML flavor for oversize copies. The engine's synchronous
   * serializer stays the fast path; this local builder mirrors its markup
   * minus per-cell raw values, which oversize payloads never carry.
   */
  private async serializeHtmlChunked(payload: TmGridCopyPayload): Promise<string> {
    const parts: string[] = [
      `<table data-tm-grid="${escapeHtmlAttribute(JSON.stringify(payload.meta))}">`,
    ];
    if (payload.headerRow !== undefined) {
      parts.push('<thead><tr>');
      for (const header of payload.headerRow) {
        parts.push(`<th>${escapeHtmlCellText(header)}</th>`);
      }
      parts.push('</tr></thead>');
    }
    parts.push('<tbody>');
    const matrix = payload.matrix;
    for (let start = 0; start < matrix.length; start += OVERSIZE_ROWS_PER_CHUNK) {
      const end = Math.min(matrix.length, start + OVERSIZE_ROWS_PER_CHUNK);
      for (let r = start; r < end; r++) {
        const rowId = payload.rowIds?.[r];
        parts.push(
          rowId === undefined
            ? '<tr>'
            : `<tr data-tm-rowid="${escapeHtmlAttribute(String(rowId))}">`,
        );
        for (const cell of matrix[r]) {
          parts.push(`<td>${escapeHtmlCellText(cell)}</td>`);
        }
        parts.push('</tr>');
      }
      await nextFrame();
    }
    parts.push('</tbody></table>');
    return parts.join('');
  }
}
