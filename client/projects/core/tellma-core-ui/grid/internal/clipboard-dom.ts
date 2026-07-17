// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The clipboard's DOM half: ClipboardEvent handling for copy/cut, the
// oversize escalation to the async Clipboard API (chunked serialization
// across frames), and the module-scoped in-memory descriptor LRU that the
// paste milestone consults for typed same-session Tellma→Tellma pastes.

import type { TmRowId } from '@tellma/core-ui/contracts';
import {
  tmClipboardFingerprint,
  tmSerializeHtmlTable,
  tmSerializeTsv,
  tmSerializeTsvChunks,
  type TmGridClipboardMeta,
  type TmGridCopyPayload,
} from '@tellma/core-ui/grid-engine';

import type { ɵTmGridAnnouncements } from './announcements';

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
 * The paste milestone's fast-path lookup: the stored descriptor whose TSV
 * flavor fingerprints to `fingerprint`, or `undefined`. A hit is touched to
 * the back of the eviction order.
 */
export function ɵtmGridCopyDescriptor(fingerprint: string): ɵTmGridCopyDescriptor | undefined {
  const descriptor = copyDescriptors.get(fingerprint);
  if (descriptor !== undefined) {
    copyDescriptors.delete(fingerprint);
    copyDescriptors.set(fingerprint, descriptor);
  }
  return descriptor;
}

/** Yields control back to the event loop between serialization chunks. */
function nextFrame(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve));
}

function escapeHtmlText(text: string): string {
  return text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
}

function escapeHtmlAttribute(text: string): string {
  return escapeHtmlText(text).replaceAll('"', '&quot;');
}

/** Construction inputs of {@link ɵTmGridClipboardDom}. */
export interface ɵTmGridClipboardDomOptions {
  /** Extracts the current selection as a copy payload (the engine's copy). */
  copy(opts?: { withHeaders?: boolean }): TmGridCopyPayload | null;
  /** The grid's live-region voice. */
  readonly announcements: ɵTmGridAnnouncements;
}

/**
 * Copy/cut ClipboardEvent handling for one grid instance: serializes the
 * engine's copy payload into both clipboard flavors, remembers the typed
 * descriptor in the module LRU, and escalates oversize copies to the async
 * Clipboard API — announcing success and failure either way (a failed copy
 * is never silent). In the readonly core, cut behaves exactly like copy;
 * the cut marquee arrives with the editing milestone.
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
    if (payload.cellCount <= ɵTM_GRID_OVERSIZE_COPY_CELLS) {
      this.writeSync(event, payload);
    } else {
      event.preventDefault();
      this.writeOversize(payload);
    }
  }

  /** The `cut` ClipboardEvent handler — copy semantics in the readonly core. */
  onCut(event: ClipboardEvent): void {
    this.onCopy(event);
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
    this.writeOversize(payload);
  }

  private writeSync(event: ClipboardEvent, payload: TmGridCopyPayload): void {
    // Copy-with-headers prepends the header row to BOTH flavors (foreign
    // targets read it as data; the HTML flavor additionally marks it so a
    // paste back into a grid skips it).
    const tsvMatrix =
      payload.headerRow === undefined ? payload.matrix : [payload.headerRow, ...payload.matrix];
    const tsv = tmSerializeTsv(tsvMatrix);
    const html = tmSerializeHtmlTable({
      matrix: payload.matrix,
      meta: payload.meta,
      rawValues: payload.rawValues,
      rowIds: payload.rowIds,
      headerRow: payload.headerRow,
    });
    rememberDescriptor(descriptorFor(tsv, payload));
    if (event.clipboardData === null) {
      return;
    }
    event.clipboardData.setData('text/plain', tsv);
    event.clipboardData.setData('text/html', html);
    event.preventDefault();
    this.options.announcements.announce('grid.announce.copied', { cells: payload.cellCount });
  }

  /**
   * The oversize path: promise-backed ClipboardItems started within the
   * user gesture; both flavors serialize in row bands with a yield between
   * bands so no frame blocks on millions of cells. Rejection (focus loss,
   * expired activation, permission) is announced — it cannot be retried
   * programmatically once the gesture is gone.
   */
  private writeOversize(payload: TmGridCopyPayload): void {
    const textPromise = this.serializeTsvChunked(payload).then(
      (tsv) => new Blob([tsv], { type: 'text/plain' }),
    );
    const htmlPromise = this.serializeHtmlChunked(payload).then(
      (html) => new Blob([html], { type: 'text/html' }),
    );
    navigator.clipboard
      .write([new ClipboardItem({ 'text/plain': textPromise, 'text/html': htmlPromise })])
      .then(
        () => this.options.announcements.announce('grid.announce.copied', { cells: payload.cellCount }),
        () => this.options.announcements.announce('grid.announce.copyFailed'),
      );
  }

  private async serializeTsvChunked(payload: TmGridCopyPayload): Promise<string> {
    const parts: string[] = [];
    const tsvMatrix =
      payload.headerRow === undefined ? payload.matrix : [payload.headerRow, ...payload.matrix];
    for (const chunk of tmSerializeTsvChunks(tsvMatrix, OVERSIZE_ROWS_PER_CHUNK)) {
      parts.push(chunk);
      await nextFrame();
    }
    const tsv = parts.join('');
    // Oversize copies drop raw values (engine contract), but the descriptor
    // still lets a same-session paste skip re-parsing the display strings.
    rememberDescriptor(descriptorFor(tsv, payload));
    return tsv;
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
        parts.push(`<th>${escapeHtmlText(header)}</th>`);
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
          parts.push(`<td>${escapeHtmlText(cell)}</td>`);
        }
        parts.push('</tr>');
      }
      await nextFrame();
    }
    parts.push('</tbody></table>');
    return parts.join('');
  }
}
