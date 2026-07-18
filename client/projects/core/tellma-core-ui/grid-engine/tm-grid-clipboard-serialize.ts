// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Clipboard serialization/parsing over plain string matrices. Deliberately
// shape-neutral (no grid coupling) so other tabular components can reuse it.

import type { TmGridColumnType } from './tm-grid-types';

/**
 * The machine-readable metadata a copied table carries in its HTML flavor
 * (`data-tm-grid` on the `<table>`), enabling typed paste between grids.
 */
export interface TmGridClipboardMeta {
  /** Format version. */
  readonly v: 1;
  /** The copying grid's tenant (cross-tenant paste guard). */
  readonly tenant?: string;
  /** The copying grid's locale (source-locale parse hint). */
  readonly locale?: string;
  /** The copied columns' keys and types, in copy order. */
  readonly cols?: ReadonlyArray<{ readonly key: string | null; readonly type: TmGridColumnType }>;
  /** Whether the first row is a header row (copy-with-headers). */
  readonly headers?: boolean;
}

/** Whether a TSV cell needs quoting (tab, any newline, or a quote inside). */
function needsQuoting(cell: string): boolean {
  return /["\t\r\n]/.test(cell);
}

function quoteCell(cell: string): string {
  return needsQuoting(cell) ? `"${cell.replaceAll('"', '""')}"` : cell;
}

/**
 * Serializes a string matrix as spreadsheet-interop TSV: cells containing a
 * tab, newline, or quote are quoted with quotes doubled; rows end with CRLF
 * (including the last — the spreadsheet clipboard convention).
 */
export function tmSerializeTsv(matrix: ReadonlyArray<readonly string[]>): string {
  let out = '';
  for (const row of matrix) {
    out += row.map(quoteCell).join('\t') + '\r\n';
  }
  return out;
}

/**
 * The chunked variant of {@link tmSerializeTsv} for oversize copies: yields
 * `rowsPerChunk` rows at a time so serialization can spread across frames.
 */
export function* tmSerializeTsvChunks(
  matrix: ReadonlyArray<readonly string[]>,
  rowsPerChunk: number,
): IterableIterator<string> {
  const step = Math.max(1, Math.floor(rowsPerChunk));
  for (let start = 0; start < matrix.length; start += step) {
    let chunk = '';
    const end = Math.min(matrix.length, start + step);
    for (let i = start; i < end; i++) {
      chunk += matrix[i].map(quoteCell).join('\t') + '\r\n';
    }
    yield chunk;
  }
}

/**
 * Parses TSV with spreadsheet quoting into a rectangular string matrix:
 * quoted fields may embed tabs, quotes (doubled), and newlines; rows split
 * on CRLF or LF; a lone trailing newline produces no phantom row; a leading
 * BOM is dropped; an unterminated quote runs to the end; ragged rows are
 * padded with empty strings.
 */
export function tmParseTsv(text: string): string[][] {
  let input = text;
  if (input.startsWith('\uFEFF')) {
    input = input.slice(1);
  }
  const rows: string[][] = [];
  let row: string[] = [];
  let field = '';
  let quoted = false;
  let i = 0;
  const endField = (): void => {
    row.push(field);
    field = '';
  };
  const endRow = (): void => {
    endField();
    rows.push(row);
    row = [];
  };
  while (i < input.length) {
    const ch = input[i];
    if (quoted) {
      if (ch === '"') {
        if (input[i + 1] === '"') {
          field += '"';
          i += 2;
          continue;
        }
        quoted = false;
        i += 1;
        continue;
      }
      field += ch;
      i += 1;
      continue;
    }
    if (ch === '"' && field === '') {
      quoted = true;
      i += 1;
      continue;
    }
    if (ch === '\t') {
      endField();
      i += 1;
      continue;
    }
    if (ch === '\r' || ch === '\n') {
      endRow();
      i += ch === '\r' && input[i + 1] === '\n' ? 2 : 1;
      continue;
    }
    field += ch;
    i += 1;
  }
  // Final row only when the text didn't end on a row separator (or holds a
  // pending field) — a trailing CRLF is a terminator, not an empty row.
  if (field !== '' || row.length > 0) {
    endRow();
  }
  const width = rows.reduce((max, r) => Math.max(max, r.length), 0);
  for (const r of rows) {
    while (r.length < width) {
      r.push('');
    }
  }
  return rows;
}

function escapeHtmlText(text: string): string {
  return text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
}

/**
 * A table cell's HTML: escaped, with in-cell newlines as `<br>` — Excel and
 * Sheets collapse a raw newline as layout whitespace, so a `\n` in a cell
 * would be lost on paste without the explicit break.
 */
function escapeHtmlCellText(text: string): string {
  return escapeHtmlText(text)
    .replaceAll('\r\n', '<br>')
    .replaceAll('\n', '<br>')
    .replaceAll('\r', '<br>');
}

function escapeHtmlAttribute(text: string): string {
  return escapeHtmlText(text).replaceAll('"', '&quot;');
}

/**
 * The canonical form of a cell's display text for its {@link
 * tmClipboardFingerprint} integrity check: hard line breaks kept as `'\n'`,
 * every other run of whitespace collapsed to a single space, and the ends
 * trimmed — the same RENDERED form the paste reducer reconstructs from a cell.
 * So a faithful round trip — even one an editor re-wrapped across source lines
 * (Excel/Sheets emit a newline + indentation) — hashes the same on both ends.
 */
function canonicalCellText(text: string): string {
  return text
    .replace(/\r\n?/g, '\n')
    .split('\n')
    .map((line) => line.replace(/\s+/g, ' ').trim())
    .join('\n')
    .trim();
}

/** Whether a raw value survives a JSON round trip losslessly. */
function isJsonSafe(value: unknown): boolean {
  return (
    value === null ||
    typeof value === 'string' ||
    typeof value === 'boolean' ||
    (typeof value === 'number' && Number.isFinite(value))
  );
}

/** Inputs of {@link tmSerializeHtmlTable}. */
export interface TmSerializeHtmlTableArgs {
  /** The display-string matrix (what foreign targets paste). */
  readonly matrix: ReadonlyArray<readonly string[]>;
  /** The machine-readable metadata woven into the markup. */
  readonly meta: TmGridClipboardMeta;
  /**
   * Per-cell raw values (`data-tm-v`), aligned with `matrix`; an `undefined`
   * slot means "no raw value" (distinct from a legitimate `null` value).
   * Only JSON-safe primitives are emitted — a value JSON would distort
   * (dates, objects) is omitted so a typed paste can't corrupt types.
   */
  readonly rawValues?: ReadonlyArray<ReadonlyArray<{ readonly value: unknown } | undefined>>;
  /** Per-row identities (`data-tm-rowid`), emitted for full-row copies. */
  readonly rowIds?: ReadonlyArray<string | number>;
  /** The header row prepended inside `<thead>` (copy-with-headers). */
  readonly headerRow?: readonly string[];
}

/**
 * Builds the `text/html` clipboard flavor as a string: a plain `<table>` of
 * display strings that Excel and Sheets both parse, carrying the Tellma
 * metadata (`data-tm-grid`, per-cell `data-tm-v` + its `data-tm-h` display
 * fingerprint, per-row `data-tm-rowid`) that lets another grid paste typed
 * values instead of re-parsing text — but only while the cell's text is
 * unchanged since copy, which `data-tm-h` lets the reducer verify.
 */
export function tmSerializeHtmlTable(args: TmSerializeHtmlTableArgs): string {
  const parts: string[] = [];
  parts.push(`<table data-tm-grid="${escapeHtmlAttribute(JSON.stringify(args.meta))}">`);
  if (args.headerRow !== undefined) {
    parts.push('<thead><tr>');
    for (const header of args.headerRow) {
      parts.push(`<th>${escapeHtmlCellText(header)}</th>`);
    }
    parts.push('</tr></thead>');
  }
  parts.push('<tbody>');
  for (let r = 0; r < args.matrix.length; r++) {
    const rowId = args.rowIds?.[r];
    parts.push(
      rowId === undefined ? '<tr>' : `<tr data-tm-rowid="${escapeHtmlAttribute(String(rowId))}">`,
    );
    const rawRow = args.rawValues?.[r];
    for (let c = 0; c < args.matrix[r].length; c++) {
      const raw = rawRow?.[c];
      const display = args.matrix[r][c];
      const text = escapeHtmlCellText(display);
      if (raw !== undefined && isJsonSafe(raw.value)) {
        // data-tm-h fingerprints the display text so a paste can distinguish a
        // faithful round trip from a tampered one: a foreign editor (Excel,
        // Sheets) preserves data-tm-v verbatim while the user edits the visible
        // text, so the reducer trusts the raw value only when the cell still
        // hashes to this fingerprint (otherwise it re-parses the text).
        const fingerprint = tmClipboardFingerprint(canonicalCellText(display));
        parts.push(
          `<td data-tm-v="${escapeHtmlAttribute(
            JSON.stringify(raw.value),
          )}" data-tm-h="${fingerprint}">${text}</td>`,
        );
      } else {
        parts.push(`<td>${text}</td>`);
      }
    }
    parts.push('</tr>');
  }
  parts.push('</tbody></table>');
  return parts.join('');
}

/**
 * A fast stable fingerprint (FNV-1a, 32-bit, hex) over a clipboard text
 * flavor — the key of the in-memory typed-copy fast path. A paste consults
 * that store only when the fingerprint of the actual clipboard payload
 * matches, so stale descriptors can never hijack a foreign paste.
 */
export function tmClipboardFingerprint(text: string): string {
  let hash = 0x811c9dc5;
  for (let i = 0; i < text.length; i++) {
    hash ^= text.charCodeAt(i);
    hash = Math.imul(hash, 0x01000193);
  }
  return `${text.length.toString(36)}-${(hash >>> 0).toString(16)}`;
}
