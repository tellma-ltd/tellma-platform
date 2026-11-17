// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmPivotMember } from '../tm-pivot-types';
import { ɵtmPivotAxis, ɵtmPivotColumns } from './pivot-layout';
import { ɵtmPivotHeaderPieces, ɵtmPivotPlace, ɵtmPivotSegments } from './pivot-window';

/** A segment list as a compact string: `3` for an item, `[3,7)` for a gap. */
function show(segments: ReturnType<typeof ɵtmPivotSegments>): string {
  return segments.map((segment) => (segment.kind === 'item' ? `${segment.index}` : `[${segment.from},${segment.to})`)).join(' ');
}

describe('ɵtmPivotSegments', () => {
  it('renders the window with gaps around it', () => {
    expect(show(ɵtmPivotSegments(3, 6, 10, null))).toBe('[0,3) 3 4 5 [6,10)');
    expect(show(ɵtmPivotSegments(0, 3, 3, null))).toBe('0 1 2');
  });

  it('keeps an outlier rendered before or after the window', () => {
    expect(show(ɵtmPivotSegments(5, 7, 10, 1))).toBe('[0,1) 1 [2,5) 5 6 [7,10)');
    expect(show(ɵtmPivotSegments(2, 4, 10, 9))).toBe('[0,2) 2 3 [4,9) 9');
    expect(show(ɵtmPivotSegments(2, 4, 10, 4))).toBe('[0,2) 2 3 4 [5,10)');
    // An outlier inside the window, or off the axis, adds nothing.
    expect(show(ɵtmPivotSegments(2, 4, 10, 3))).toBe('[0,2) 2 3 [4,10)');
    expect(show(ɵtmPivotSegments(2, 4, 10, 12))).toBe('[0,2) 2 3 [4,10)');
  });

  it('clamps a window past the axis', () => {
    expect(show(ɵtmPivotSegments(8, 20, 10, null))).toBe('[0,8) 8 9');
    expect(show(ɵtmPivotSegments(0, 0, 0, null))).toBe('');
  });
});

describe('ɵtmPivotPlace', () => {
  const segments = ɵtmPivotSegments(4, 6, 10, 1);

  it('spans a range from its first rendered column to its last, inner gaps included', () => {
    // Segments: [0,1) 1 [2,4) 4 5 [6,10).
    expect(ɵtmPivotPlace(segments, 0, 10)).toEqual({ first: 1, span: 4 });
    expect(ɵtmPivotPlace(segments, 4, 5)).toEqual({ first: 3, span: 1 });
    expect(ɵtmPivotPlace(segments, 6, 10)).toBeNull();
  });
});

describe('ɵtmPivotHeaderPieces', () => {
  // Two first-level members of four children each, both expanded, then the total.
  const members: TmPivotMember[] = [{ id: 0, parent: null, kind: 'total', label: 'Total' }];
  for (const parent of [1, 2]) {
    members.push({ id: parent, parent: 0, kind: 'member', label: `P${parent}` });
    for (let child = 1; child <= 4; child++) {
      members.push({ id: parent * 10 + child, parent, kind: 'member', label: `C${parent}${child}` });
    }
  }
  const columns = ɵtmPivotColumns(ɵtmPivotAxis(members), new Set([1, 2]), 1, 0);

  it('places every header over a full window', () => {
    const pieces = ɵtmPivotHeaderPieces(columns, ɵtmPivotSegments(0, columns.dataCount, columns.dataCount, null));
    expect(pieces[0].map((piece) => (piece.kind === 'cell' ? piece.cell.key : piece.key))).toEqual([
      'g1',
      's1',
      'g2',
      's2',
      't',
    ]);
    // The second row holds the children; the subtotals and the total span down into it.
    expect(pieces[1].map((piece) => (piece.kind === 'cell' ? piece.cell.key : piece.key))).toEqual([
      'm11',
      'm12',
      'm13',
      'm14',
      'm21',
      'm22',
      'm23',
      'm24',
    ]);
  });

  it('fills unrendered columns with spacers, so each row adds up to the same table columns', () => {
    // Slots: 11 12 13 14 1+ 21 22 23 24 2+ 0; window 6–8, the active column 0.
    const segments = ɵtmPivotSegments(6, 8, columns.dataCount, 0);
    const pieces = ɵtmPivotHeaderPieces(columns, segments);
    const rows = pieces.map((row) =>
      row.map((piece) => (piece.kind === 'cell' ? `${piece.cell.key}:${piece.placement.span}` : `_:${piece.placement.span}`)),
    );
    // Segments: 0 [1,6) 6 7 [8,11).
    expect(rows[0]).toEqual(['g1:1', '_:1', 'g2:2', '_:1']);
    expect(rows[1]).toEqual(['m11:1', '_:1', 'm22:1', 'm23:1', '_:1']);
    for (const row of pieces) {
      expect(row.reduce((sum, piece) => sum + piece.placement.span, 0)).toBe(segments.length);
    }
  });
});
