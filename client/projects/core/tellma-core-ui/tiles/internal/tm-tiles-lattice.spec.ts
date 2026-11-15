// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmComputeAxisWindow } from '@tellma/core-ui/grid-engine';

import {
  ɵtmTilesColumns,
  ɵtmTilesLineCount,
  ɵtmTilesMove,
  ɵtmTilesRenderedLines,
  type ɵTmTilesColumnOptions,
  type ɵTmTilesShape,
} from './tm-tiles-lattice';

/** The default theme's geometry: 220px tiles, a 10px gap, the 576px breakpoint. */
const FLAT: ɵTmTilesColumnOptions = { tree: false, minWidth: 220, gap: 10, breakpoint: 576 };

describe('ɵtmTilesColumns', () => {
  it('stacks one tile per line below the breakpoint', () => {
    expect(ɵtmTilesColumns(0, FLAT)).toBe(1);
    expect(ɵtmTilesColumns(375, FLAT)).toBe(1);
    expect(ɵtmTilesColumns(575, FLAT)).toBe(1);
  });

  it('fits floor((W + 10) / 230) tiles from the breakpoint up', () => {
    // 576 + 10 = 586 → 2; 680 + 10 = 690 → exactly 3; one px less → 2.
    expect(ɵtmTilesColumns(576, FLAT)).toBe(2);
    expect(ɵtmTilesColumns(679, FLAT)).toBe(2);
    expect(ɵtmTilesColumns(680, FLAT)).toBe(3);
    expect(ɵtmTilesColumns(1024, FLAT)).toBe(4);
    expect(ɵtmTilesColumns(1440, FLAT)).toBe(6);
  });

  it('never drops below one column when tiles are wider than the width', () => {
    expect(ɵtmTilesColumns(600, { ...FLAT, minWidth: 900 })).toBe(1);
  });

  it('keeps one column in tree mode at any width', () => {
    expect(ɵtmTilesColumns(1440, { ...FLAT, tree: true })).toBe(1);
  });

  it('treats an unmeasured (NaN) width as narrow', () => {
    expect(ɵtmTilesColumns(Number.NaN, FLAT)).toBe(1);
  });
});

describe('ɵtmTilesLineCount', () => {
  it('counts full lines plus a short last one', () => {
    expect(ɵtmTilesLineCount({ columns: 3, count: 0 })).toBe(0);
    expect(ɵtmTilesLineCount({ columns: 3, count: 9 })).toBe(3);
    expect(ɵtmTilesLineCount({ columns: 3, count: 10 })).toBe(4);
    expect(ɵtmTilesLineCount({ columns: 1, count: 10 })).toBe(10);
  });
});

describe('ɵtmTilesRenderedLines', () => {
  it('renders the window alone when the active line is inside it', () => {
    expect(ɵtmTilesRenderedLines(2, 6, 4)).toEqual([2, 3, 4, 5]);
    expect(ɵtmTilesRenderedLines(2, 6, null)).toEqual([2, 3, 4, 5]);
  });

  it('adds the active line before or after the window, keeping the order', () => {
    expect(ɵtmTilesRenderedLines(10, 13, 0)).toEqual([0, 10, 11, 12]);
    expect(ɵtmTilesRenderedLines(10, 13, 400)).toEqual([10, 11, 12, 400]);
  });

  it('windows lines at a 70px pitch with three lines of overscan', () => {
    // 10,000 tiles in 4 columns are 2,500 lines; a 700px viewport scrolled
    // to line 100 shows lines 100–109 and renders three more on each side.
    const window = tmComputeAxisWindow({
      scrollOffset: 100 * 70,
      viewportSize: 700,
      itemSize: 70,
      itemCount: ɵtmTilesLineCount({ columns: 4, count: 10_000 }),
      overscan: 3,
    });
    expect(window).toEqual({ start: 97, end: 113, leadOffset: 97 * 70, totalSize: 2500 * 70 });
    expect(ɵtmTilesRenderedLines(window.start, window.end, 0)).toHaveLength(17);
  });
});

describe('ɵtmTilesMove', () => {
  /** Ten tiles in three columns: lines 0–2 are full, line 3 holds tile 9 alone. */
  const SHORT_LAST: ɵTmTilesShape = { columns: 3, count: 10 };

  it('starts at the first tile from no active tile, or the last for last', () => {
    expect(ɵtmTilesMove(SHORT_LAST, null, { kind: 'lines', by: 1 }, null).index).toBe(0);
    expect(ɵtmTilesMove(SHORT_LAST, null, { kind: 'step', by: -1 }, null).index).toBe(0);
    expect(ɵtmTilesMove(SHORT_LAST, null, { kind: 'last' }, null).index).toBe(9);
  });

  it('moves nowhere on an empty lattice', () => {
    expect(ɵtmTilesMove({ columns: 3, count: 0 }, null, { kind: 'first' }, null)).toEqual({
      index: null,
      column: null,
    });
  });

  it('steps across lines in reading order, clamped at both ends', () => {
    expect(ɵtmTilesMove(SHORT_LAST, 2, { kind: 'step', by: 1 }, null).index).toBe(3);
    expect(ɵtmTilesMove(SHORT_LAST, 3, { kind: 'step', by: -1 }, null).index).toBe(2);
    expect(ɵtmTilesMove(SHORT_LAST, 0, { kind: 'step', by: -1 }, null).index).toBe(0);
    expect(ɵtmTilesMove(SHORT_LAST, 9, { kind: 'step', by: 1 }, null).index).toBe(9);
  });

  it('keeps the column moving down and up between full lines', () => {
    expect(ɵtmTilesMove(SHORT_LAST, 1, { kind: 'lines', by: 1 }, null)).toEqual({
      index: 4,
      column: 1,
    });
    expect(ɵtmTilesMove(SHORT_LAST, 7, { kind: 'lines', by: -1 }, null)).toEqual({
      index: 4,
      column: 1,
    });
  });

  it('clamps onto the short last line and returns to the column moving back up', () => {
    // Down from tile 8 (column 2) lands on tile 9, the last line's only tile;
    // up again goes back to column 2, not column 0.
    const down = ɵtmTilesMove(SHORT_LAST, 8, { kind: 'lines', by: 1 }, null);
    expect(down).toEqual({ index: 9, column: 2 });
    expect(ɵtmTilesMove(SHORT_LAST, down.index, { kind: 'lines', by: -1 }, down.column)).toEqual({
      index: 8,
      column: 2,
    });
  });

  it('clamps pages at the first and last lines, keeping the column', () => {
    expect(ɵtmTilesMove(SHORT_LAST, 4, { kind: 'lines', by: 10 }, null).index).toBe(9);
    expect(ɵtmTilesMove(SHORT_LAST, 7, { kind: 'lines', by: -10 }, null).index).toBe(1);
  });

  it('goes to the ends of the active line, short or full', () => {
    expect(ɵtmTilesMove(SHORT_LAST, 4, { kind: 'lineStart' }, null).index).toBe(3);
    expect(ɵtmTilesMove(SHORT_LAST, 4, { kind: 'lineEnd' }, null).index).toBe(5);
    expect(ɵtmTilesMove(SHORT_LAST, 9, { kind: 'lineEnd' }, null).index).toBe(9);
  });

  it('goes to the first and last tile of all', () => {
    expect(ɵtmTilesMove(SHORT_LAST, 5, { kind: 'first' }, null).index).toBe(0);
    expect(ɵtmTilesMove(SHORT_LAST, 5, { kind: 'last' }, null).index).toBe(9);
  });

  it('forgets the remembered column on any move that is not vertical', () => {
    expect(ɵtmTilesMove(SHORT_LAST, 9, { kind: 'step', by: -1 }, 2).column).toBeNull();
  });

  it('restarts from the first tile when the active one is past a shrunken lattice', () => {
    expect(ɵtmTilesMove(SHORT_LAST, 40, { kind: 'lines', by: 1 }, null).index).toBe(0);
  });
});
