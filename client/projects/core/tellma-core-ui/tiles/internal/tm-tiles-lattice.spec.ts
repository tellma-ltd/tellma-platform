// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ɵtmTilesAssignSlots,
  ɵtmTilesColumns,
  ɵtmTilesLineCount,
  ɵtmTilesMove,
  ɵtmTilesRenderedLines,
  ɵtmTilesSpan,
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
    // to line 100 shows lines 100–109 (110 in part) and renders three more
    // on each side.
    const count = ɵtmTilesLineCount({ columns: 4, count: 10_000 });
    const span = ɵtmTilesSpan(100 * 70, null, 70, count, 700);
    expect(span).toEqual({ start: 97, end: 114 });
    expect(ɵtmTilesRenderedLines(span.start, span.end, 0)).toHaveLength(18);
  });
});

describe('ɵtmTilesSpan', () => {
  /** 2,500 lines at a 70px pitch under a 420px viewport. */
  const SHAPE = { pitch: 70, count: 2500, viewport: 420 };

  /** The span at `offset` (from `from`) over a shape. */
  function spanAt(
    offset: number,
    from: number | null,
    { pitch, count, viewport }: typeof SHAPE = SHAPE,
  ): ReturnType<typeof ɵtmTilesSpan> {
    return ɵtmTilesSpan(offset, from, pitch, count, viewport);
  }

  /** The span `[start, end)`. */
  function span(start: number, end: number) {
    return { start, end };
  }

  /** How many lines a span holds. */
  function size({ start, end }: { readonly start: number; readonly end: number }): number {
    return end - start;
  }

  it('holds the lines the viewport can show, one more for its edges, and the overscan', () => {
    // 420px at a 70px pitch shows six lines, seven when part-lines show at
    // both edges; three more on each side.
    expect(size(spanAt(100 * 70, null))).toBe(13);
    expect(size(spanAt(100 * 70, null, { ...SHAPE, viewport: 400 }))).toBe(13);
    expect(size(spanAt(100 * 70, null, { ...SHAPE, viewport: 0 }))).toBe(7);
  });

  it('renders three lines above the first visible one and thirteen from there', () => {
    expect(spanAt(100 * 70, null)).toEqual(span(97, 110));
    expect(spanAt(100 * 70 + 69, null)).toEqual(span(97, 110));
  });

  it('clips the span to the lattice at either end', () => {
    expect(spanAt(0, null)).toEqual(span(0, 10));
    expect(spanAt(2494 * 70, null)).toEqual(span(2491, 2500));
    expect(spanAt(0, null, { ...SHAPE, count: 3 })).toEqual(span(0, 3));
    expect(spanAt(0, null, { ...SHAPE, count: 0 })).toEqual(span(0, 0));
  });

  it("moves a scroll's own span from where it started only as far as the visible lines need", () => {
    // From line 50 the span was 47–59. Down to line 60 it moves to 54–66,
    // so lines 60–66 show with the overscan below still to come.
    expect(spanAt(60 * 70, 50 * 70)).toEqual(span(54, 67));
    // Up to line 40 it moves to 40–52, the overscan above still to come.
    expect(spanAt(40 * 70, 50 * 70)).toEqual(span(40, 53));
    // A short scroll that the span already covers moves nothing.
    expect(spanAt(51 * 70, 50 * 70)).toEqual(span(47, 60));
    expect(spanAt(50 * 70, 50 * 70)).toEqual(spanAt(50 * 70, null));
  });
});

describe('ɵtmTilesAssignSlots', () => {
  /** Lines `[start, end)`, ascending. */
  function lines(start: number, end: number): number[] {
    return Array.from({ length: end - start }, (_, i) => start + i);
  }

  it('numbers the first lines from zero, in order', () => {
    expect([...ɵtmTilesAssignSlots(lines(97, 101), new Map())]).toEqual([
      [97, 0],
      [98, 1],
      [99, 2],
      [100, 3],
    ]);
  });

  it('keeps the slot of every line that stays', () => {
    const first = ɵtmTilesAssignSlots(lines(0, 8), new Map());
    const next = ɵtmTilesAssignSlots(lines(3, 11), first);
    for (const line of lines(3, 8)) {
      expect(next.get(line)).toBe(first.get(line));
    }
  });

  it('gives the lines that enter the slots of those that left, the last to leave first', () => {
    const first = ɵtmTilesAssignSlots(lines(0, 8), new Map());
    // Down three: lines 8, 9 and 10 take the slots of 2, 1 and 0.
    const down = ɵtmTilesAssignSlots(lines(3, 11), first);
    expect([8, 9, 10].map((line) => down.get(line))).toEqual([2, 1, 0]);
    // Back up three: lines 0, 1 and 2 take the slots of 10, 9 and 8.
    const up = ɵtmTilesAssignSlots(lines(0, 8), down);
    expect([0, 1, 2].map((line) => up.get(line))).toEqual([0, 1, 2]);
    expect(new Set(up.values()).size).toBe(8);
  });

  it('gives new slots past every slot in use when the lines outnumber the free ones', () => {
    // Lines 0–3 hold 0–3; 1–2 stay with 1–2, so 0 and 3 are free.
    const shrunk = ɵtmTilesAssignSlots([1, 2], ɵtmTilesAssignSlots(lines(0, 4), new Map()));
    expect([...shrunk]).toEqual([
      [1, 1],
      [2, 2],
    ]);
    // Six lines: 1–2 stay, 3–6 take new slots past the highest in use.
    const grown = ɵtmTilesAssignSlots(lines(1, 7), shrunk);
    expect(new Set(grown.values()).size).toBe(6);
    expect([1, 2].map((line) => grown.get(line))).toEqual([1, 2]);
  });

  it('keeps the active line on its slot while the window moves far away', () => {
    const first = ɵtmTilesAssignSlots(lines(0, 6), new Map());
    const far = ɵtmTilesAssignSlots([1, ...lines(900, 905)], first);
    expect(far.get(1)).toBe(1);
    expect(new Set(far.values()).size).toBe(6);
    const back = ɵtmTilesAssignSlots(lines(0, 6), far);
    expect(back.get(1)).toBe(1);
    expect(new Set(back.values()).size).toBe(6);
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

  it('clamps a remembered column into a lattice that lost columns', () => {
    // Four columns: down from tile 7 (column 3) lands on tile 9 and
    // remembers column 3. A resize leaves two columns, so tile 9 is on
    // line 4, and up goes to line 3's last column, tile 7.
    const down = ɵtmTilesMove({ columns: 4, count: 10 }, 7, { kind: 'lines', by: 1 }, null);
    expect(down).toEqual({ index: 9, column: 3 });
    expect(
      ɵtmTilesMove({ columns: 2, count: 10 }, down.index, { kind: 'lines', by: -1 }, down.column),
    ).toEqual({ index: 7, column: 1 });
  });

  it('restarts from the first tile when the active one is past a shrunken lattice', () => {
    expect(ɵtmTilesMove(SHORT_LAST, 40, { kind: 'lines', by: 1 }, null).index).toBe(0);
  });
});
