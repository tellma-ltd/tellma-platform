// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The paste ladder's HTML rung: reduces a `text/html` clipboard payload to
// the engine's paste-source shape via DOMParser — the engine itself stays
// DOM-free. Recognizes the Tellma metadata woven into copied markup
// (`data-tm-grid`, `data-tm-v` + its `data-tm-h` integrity fingerprint,
// `data-tm-rowid`) and degrades gracefully on foreign tables (Excel, Sheets);
// Sheets' proprietary `data-sheets-*` attributes are deliberately ignored
// (undocumented, unstable).

import type { TmRowId } from '@tellma/core-ui/contracts';
import { tmClipboardFingerprint } from '@tellma/core-ui/grid-engine';
import type {
  TmGridClipboardMeta,
  TmGridColumnType,
  TmGridPasteSource,
} from '@tellma/core-ui/grid-engine';

/** A raw-value slot: present = a typed value travelled with the cell. */
type RawSlot = { readonly value: unknown } | undefined;

/**
 * Parses the `data-tm-grid` attribute JSON, validating the shape loosely:
 * anything that is not a `v: 1` object is treated as absent (foreign or
 * corrupted metadata never breaks a paste — the display strings still work).
 */
function parseMeta(raw: string | null): TmGridClipboardMeta | undefined {
  if (raw === null) {
    return undefined;
  }
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    return undefined;
  }
  if (typeof parsed !== 'object' || parsed === null) {
    return undefined;
  }
  const record = parsed as Record<string, unknown>;
  if (record['v'] !== 1) {
    return undefined;
  }
  let cols: Array<{ key: string | null; type: TmGridColumnType }> | undefined;
  if (Array.isArray(record['cols'])) {
    cols = record['cols'].map((entry: unknown) => {
      const col =
        typeof entry === 'object' && entry !== null ? (entry as Record<string, unknown>) : {};
      return {
        key: typeof col['key'] === 'string' ? col['key'] : null,
        // Loose by design: an unknown type string simply never matches a
        // target column's type, so the typed fast path stays off for it.
        type: (typeof col['type'] === 'string' ? col['type'] : 'text') as TmGridColumnType,
      };
    });
  }
  return {
    v: 1,
    tenant: typeof record['tenant'] === 'string' ? record['tenant'] : undefined,
    locale: typeof record['locale'] === 'string' ? record['locale'] : undefined,
    cols,
    headers: record['headers'] === true,
  };
}

/**
 * A cell's RENDERED text: a `<br>` becomes a hard newline (Excel emits it for
 * an in-cell newline), every other run of whitespace collapses to a single
 * space (the way `white-space: normal` lays a cell out), and the ends are
 * trimmed. Reading the rendered form, not raw `textContent`, is what lets an
 * Excel or Sheets round trip match what the grid wrote.
 */
function cellText(cell: HTMLTableCellElement): string {
  let raw: string;
  if (cell.querySelector('br') === null) {
    raw = cell.textContent ?? '';
  } else {
    // A <br> is a HARD line break; mark it with a sentinel that survives the
    // whitespace collapse below, then restore it to a real newline.
    const clone = cell.cloneNode(true) as HTMLElement;
    for (const br of clone.querySelectorAll('br')) {
      br.replaceWith('\uE000');
    }
    raw = clone.textContent ?? '';
  }
  // Excel and Sheets wrap long cell text across SOURCE lines (a newline plus
  // indentation) and weave in non-breaking spaces, none of it content.
  // textContent keeps it verbatim, so an entity label "Alice Green" comes back
  // wrapped: its data-tm-h fingerprint no longer matches (dropping the raw id)
  // and the resolver's exact match then fails ("no agent named 'Alice Green'").
  // Collapsing to the rendered form fixes both: the whitespace class covers the
  // source newlines, tabs, and nbsp; only the <br> sentinel becomes a break.
  return raw
    .replace(/\s+/g, ' ')
    .replace(/ ?\uE000 ?/g, '\n')
    .trim();
}

/**
 * The `data-tm-v` raw value, or `undefined` when absent, unparseable, or — the
 * tamper check — when the cell's current display `text` no longer matches the
 * `data-tm-h` fingerprint captured at copy time. A foreign editor (Excel,
 * Sheets) round-trips our `data-tm-v` verbatim while the user edits the visible
 * text, so trusting an unverified raw value would silently overwrite that edit
 * with the stale typed value. Absent or mismatched fingerprint → discard, so
 * the engine re-parses the (authoritative) display text.
 */
function cellRawValue(cell: HTMLTableCellElement, text: string): RawSlot {
  const raw = cell.getAttribute('data-tm-v');
  if (raw === null) {
    return undefined;
  }
  const fingerprint = cell.getAttribute('data-tm-h');
  if (fingerprint === null || fingerprint !== tmClipboardFingerprint(text)) {
    return undefined;
  }
  try {
    return { value: JSON.parse(raw) };
  } catch {
    return undefined;
  }
}

/**
 * A `data-tm-rowid` attribute back to a row identity. Serialization
 * stringified the id, so integer-looking strings are restored as numbers —
 * the pragmatic inverse (a grid whose STRING ids look like integers loses
 * the html-rung row-move; the in-memory fast path still carries exact ids).
 */
function parseRowId(raw: string): TmRowId {
  return /^-?\d{1,15}$/.test(raw) ? Number(raw) : raw;
}

/**
 * Reduces a `text/html` clipboard payload to a paste source, or `null` when
 * it holds no `<table>` (the caller falls to the TSV rung).
 *
 * - The first `<table>` in the payload is the source; `<thead>` rows are
 *   excluded from the matrix when the Tellma metadata says they are headers;
 *   a foreign table's `<thead>` rows stay IN the matrix with `hasHeaderRow`
 *   left undefined, so the engine's content heuristic decides (Excel
 *   round-trips strip our metadata but may emit a `<thead>` of their own).
 * - Each `<td>`/`<th>` is one matrix cell; `rowspan`/`colspan` are ignored
 *   (spanned cells are not re-expanded — spreadsheet clipboards never emit
 *   spans, and reconstructing a foreign layout table is not a paste's job).
 * - `data-tm-rowid` identities are returned only when EVERY data row
 *   carries one (a partial set cannot describe a row move).
 * - Ragged rows are padded with `''` so the matrix is rectangular.
 */
export function ɵtmReduceClipboardHtml(html: string): TmGridPasteSource | null {
  const doc = new DOMParser().parseFromString(html, 'text/html');
  const table = doc.querySelector('table');
  if (table === null) {
    return null;
  }
  const meta = parseMeta(table.getAttribute('data-tm-grid'));
  const headRows = new Set<HTMLTableRowElement>(table.tHead === null ? [] : table.tHead.rows);
  // Our own copies mark headers BOTH ways (<thead> + the metadata flag);
  // the marked rows leave the matrix here. A flag with no <thead> (markup
  // rewritten by an intermediary) falls back to the engine's slice of the
  // first matrix row.
  const excludeHead = meta?.headers === true && headRows.size > 0;

  const matrix: string[][] = [];
  const rawValues: RawSlot[][] = [];
  const rowIds: TmRowId[] = [];
  let sawRawValue = false;
  let everyRowHasId = true;
  for (const row of table.rows) {
    if (excludeHead && headRows.has(row)) {
      continue;
    }
    const textRow: string[] = [];
    const rawRow: RawSlot[] = [];
    for (const cell of row.cells) {
      const text = cellText(cell);
      textRow.push(text);
      const raw = cellRawValue(cell, text);
      sawRawValue ||= raw !== undefined;
      rawRow.push(raw);
    }
    const rowId = row.getAttribute('data-tm-rowid');
    if (rowId === null) {
      everyRowHasId = false;
    } else {
      rowIds.push(parseRowId(rowId));
    }
    matrix.push(textRow);
    rawValues.push(rawRow);
  }
  if (matrix.length === 0) {
    return null; // a degenerate table never beats the TSV flavor
  }
  const width = matrix.reduce((max, row) => Math.max(max, row.length), 0);
  if (width === 0) {
    return null;
  }
  for (let r = 0; r < matrix.length; r++) {
    while (matrix[r].length < width) {
      matrix[r].push('');
      rawValues[r].push(undefined);
    }
  }
  // The header rows left the matrix above, so the returned metadata must
  // not claim a header row remains; hasHeaderRow mirrors what the matrix
  // actually holds. Foreign tables (no metadata) leave it undefined — the
  // engine's content heuristic decides there.
  return {
    matrix,
    meta: meta === undefined ? undefined : excludeHead ? { ...meta, headers: undefined } : meta,
    rawValues: sawRawValue ? rawValues : undefined,
    rowIds: everyRowHasId && rowIds.length > 0 ? rowIds : undefined,
    hasHeaderRow: meta === undefined ? undefined : meta.headers === true && !excludeHead,
  };
}
