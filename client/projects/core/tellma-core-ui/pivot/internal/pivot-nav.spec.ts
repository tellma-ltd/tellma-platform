// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmPivotMember } from '../tm-pivot-types';
import { ɵtmPivotAxis, ɵtmPivotColumns } from './pivot-layout';
import { ɵtmPivotClamp, ɵtmPivotCover, ɵtmPivotGridSize, ɵtmPivotMoveFrom, type ɵTmPivotGrid } from './pivot-nav';

// Columns: Q1 (expanded: Jan, Feb), Q2, the total; two measures. Header
// rows: Q1/Q2/Total, the months, the measures. Leading columns: the row
// header and one attribute. Data columns from 2: Jan ×2, Feb ×2, Q1 ×2,
// Q2 ×2, Total ×2.
/** The column members: two quarters, the first with two months, and the total. */
const MEMBERS: TmPivotMember[] = [
  { id: 0, parent: null, kind: 'total', label: 'Total' },
  { id: 1, parent: 0, kind: 'member', label: 'Q1' },
  { id: 11, parent: 1, kind: 'member', label: 'Jan' },
  { id: 12, parent: 1, kind: 'member', label: 'Feb' },
  { id: 2, parent: 0, kind: 'member', label: 'Q2' },
  { id: 21, parent: 2, kind: 'member', label: 'Apr' },
];
/** The grid over them: Q1 expanded, two measures, one attribute. */
const GRID: ɵTmPivotGrid = {
  columns: ɵtmPivotColumns(ɵtmPivotAxis(MEMBERS), new Set([1]), 2, 0),
  attributeCount: 1,
  bodyRows: 20,
};

describe('the pivot grid', () => {
  it('counts the header rows, the body rows, the leading and the data columns', () => {
    expect(ɵtmPivotGridSize(GRID)).toEqual({ rows: 3 + 20, columns: 2 + 10 });
  });

  it('covers a position with the cell that spans it', () => {
    // The group header Q1 spans Jan and Feb on row 0.
    expect(ɵtmPivotCover(GRID, { row: 0, column: 4 })).toEqual({ row: 0, rowSpan: 1, column: 2, columnSpan: 4 });
    // Q2 is collapsed: it spans the level rows over its two measures.
    expect(ɵtmPivotCover(GRID, { row: 1, column: 9 })).toEqual({ row: 0, rowSpan: 2, column: 8, columnSpan: 2 });
    // The attribute's corner spans every header row; the row-header corner does not.
    expect(ɵtmPivotCover(GRID, { row: 2, column: 1 })).toEqual({ row: 0, rowSpan: 3, column: 1, columnSpan: 1 });
    expect(ɵtmPivotCover(GRID, { row: 1, column: 0 })).toEqual({ row: 1, rowSpan: 1, column: 0, columnSpan: 1 });
    expect(ɵtmPivotCover(GRID, { row: 7, column: 5 })).toEqual({ row: 7, rowSpan: 1, column: 5, columnSpan: 1 });
  });

  it('steps past a spanning cell and comes back out at the column it came in by', () => {
    // Up from the Feb measure column 5 to Feb (1), then to Q1 (0).
    let at = ɵtmPivotMoveFrom(GRID, { row: 3, column: 5 }, 'up', 10);
    expect(at).toEqual({ row: 2, column: 5 });
    at = ɵtmPivotMoveFrom(GRID, at, 'up', 10);
    at = ɵtmPivotMoveFrom(GRID, at, 'up', 10);
    expect(at).toEqual({ row: 0, column: 5 });
    expect(ɵtmPivotCover(GRID, at).column).toBe(2);
    // Down again lands back on column 5's month.
    expect(ɵtmPivotMoveFrom(GRID, at, 'down', 10)).toEqual({ row: 1, column: 5 });
    // Right from Q1 leaves its span.
    expect(ɵtmPivotMoveFrom(GRID, at, 'right', 10)).toEqual({ row: 0, column: 6 });
    // Down from the attribute corner reaches the first body row.
    expect(ɵtmPivotMoveFrom(GRID, { row: 0, column: 1 }, 'down', 10)).toEqual({ row: 3, column: 1 });
  });

  it('stays put at an edge, and clamps pages, row ends and corners', () => {
    expect(ɵtmPivotMoveFrom(GRID, { row: 0, column: 0 }, 'up', 10)).toEqual({ row: 0, column: 0 });
    expect(ɵtmPivotMoveFrom(GRID, { row: 5, column: 0 }, 'left', 10)).toEqual({ row: 5, column: 0 });
    expect(ɵtmPivotMoveFrom(GRID, { row: 22, column: 11 }, 'right', 10)).toEqual({ row: 22, column: 11 });
    expect(ɵtmPivotMoveFrom(GRID, { row: 5, column: 3 }, 'pageDown', 10)).toEqual({ row: 15, column: 3 });
    expect(ɵtmPivotMoveFrom(GRID, { row: 18, column: 3 }, 'pageDown', 10)).toEqual({ row: 22, column: 3 });
    expect(ɵtmPivotMoveFrom(GRID, { row: 5, column: 3 }, 'pageUp', 10)).toEqual({ row: 0, column: 3 });
    expect(ɵtmPivotMoveFrom(GRID, { row: 5, column: 3 }, 'rowStart', 10)).toEqual({ row: 5, column: 0 });
    expect(ɵtmPivotMoveFrom(GRID, { row: 5, column: 3 }, 'rowEnd', 10)).toEqual({ row: 5, column: 11 });
    expect(ɵtmPivotMoveFrom(GRID, { row: 5, column: 3 }, 'first', 10)).toEqual({ row: 0, column: 0 });
    expect(ɵtmPivotMoveFrom(GRID, { row: 5, column: 3 }, 'last', 10)).toEqual({ row: 22, column: 11 });
    expect(ɵtmPivotClamp(GRID, { row: 99, column: -4 })).toEqual({ row: 22, column: 0 });
  });
});
