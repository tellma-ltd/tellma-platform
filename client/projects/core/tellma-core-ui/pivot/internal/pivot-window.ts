// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { ɵTmPivotColumns, ɵTmPivotHeaderCell } from './pivot-layout';

/**
 * One rendered stretch of an axis: an item, or a gap standing in for the
 * unrendered items between two rendered ones.
 */
export type ɵTmPivotSegment =
  | { readonly kind: 'item'; readonly index: number }
  | { readonly kind: 'gap'; readonly from: number; readonly to: number };

/**
 * The rendered stretches of an axis of `count` items: the window
 * `[start, end)`, plus one outlier kept rendered outside it (the active
 * row or column, so focus never lands on a removed element), with a gap
 * for every unrendered run between and around them.
 */
export function ɵtmPivotSegments(
  start: number,
  end: number,
  count: number,
  outlier: number | null,
): readonly ɵTmPivotSegment[] {
  const first = Math.max(0, Math.min(start, count));
  const last = Math.max(first, Math.min(end, count));
  const indices: number[] = [];
  if (outlier !== null && outlier >= 0 && outlier < count && (outlier < first || outlier >= last)) {
    indices.push(outlier);
  }
  const segments: ɵTmPivotSegment[] = [];
  let cursor = 0;
  /** Appends the gap before `index`, then the item. */
  const push = (index: number): void => {
    if (index > cursor) {
      segments.push({ kind: 'gap', from: cursor, to: index });
    }
    segments.push({ kind: 'item', index });
    cursor = index + 1;
  };
  if (indices.length === 1 && indices[0] < first) {
    push(indices[0]);
  }
  for (let index = first; index < last; index++) {
    push(index);
  }
  if (indices.length === 1 && indices[0] >= last) {
    push(indices[0]);
  }
  if (cursor < count) {
    segments.push({ kind: 'gap', from: cursor, to: count });
  }
  return segments;
}

/** Where a header cell sits among the rendered data columns: its first segment and the segments it spans. */
export interface ɵTmPivotPlacement {
  /** The first segment it occupies. */
  readonly first: number;
  /** How many segments it spans, inner gaps included. */
  readonly span: number;
}

/**
 * Places a range of data columns `[start, end)` over the rendered
 * segments: from the first rendered column in the range to the last, with
 * the gaps between them. `null` when no column of the range renders.
 */
export function ɵtmPivotPlace(
  segments: readonly ɵTmPivotSegment[],
  start: number,
  end: number,
): ɵTmPivotPlacement | null {
  let first = -1;
  let last = -1;
  for (let position = 0; position < segments.length; position++) {
    const segment = segments[position];
    if (segment.kind === 'item' && segment.index >= start && segment.index < end) {
      if (first === -1) {
        first = position;
      }
      last = position;
    }
  }
  return first === -1 ? null : { first, span: last - first + 1 };
}

/** A header cell placed in one header row, or a spacer filling segments no cell occupies there. */
export type ɵTmPivotHeaderPiece =
  | {
      readonly kind: 'cell';
      readonly cell: ɵTmPivotHeaderCell;
      readonly placement: ɵTmPivotPlacement;
    }
  | { readonly kind: 'spacer'; readonly key: string; readonly placement: ɵTmPivotPlacement };

/**
 * The header rows' data-side pieces, row by row: the cells that start on
 * the row, placed over the rendered segments, and a spacer for each run of
 * segments that no cell occupies on that row (one started on an earlier
 * row and spanning down occupies it too, and is not repeated).
 */
export function ɵtmPivotHeaderPieces(
  columns: ɵTmPivotColumns,
  segments: readonly ɵTmPivotSegment[],
): readonly (readonly ɵTmPivotHeaderPiece[])[] {
  const rows: ɵTmPivotHeaderPiece[][] = [];
  for (let row = 0; row < columns.headerRows; row++) {
    const occupied = new Uint8Array(segments.length);
    const starting: ɵTmPivotHeaderPiece[] = [];
    for (const cell of columns.rowCells[row]) {
      const placement = ɵtmPivotPlace(segments, cell.start, cell.end);
      if (placement === null) {
        continue;
      }
      occupied.fill(1, placement.first, placement.first + placement.span);
      if (cell.row === row) {
        starting.push({ kind: 'cell', cell, placement });
      }
    }
    // Spacers fill each unoccupied run, so every row adds up to the same
    // table columns.
    const pieces: ɵTmPivotHeaderPiece[] = [];
    let next = 0;
    let position = 0;
    while (position < segments.length) {
      if (occupied[position] === 1) {
        while (next < starting.length && starting[next].placement.first <= position) {
          pieces.push(starting[next++]);
        }
        position++;
        continue;
      }
      const from = position;
      while (position < segments.length && occupied[position] === 0) {
        position++;
      }
      pieces.push({ kind: 'spacer', key: `x${row}.${from}`, placement: { first: from, span: position - from } });
    }
    while (next < starting.length) {
      pieces.push(starting[next++]);
    }
    rows.push(pieces);
  }
  return rows;
}
