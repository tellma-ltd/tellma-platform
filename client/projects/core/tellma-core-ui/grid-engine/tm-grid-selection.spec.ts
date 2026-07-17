// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmRowId } from '@tellma/core-ui/contracts';

import { makeEngine, makeRows, type TestRow } from './tm-grid-testing.util';

/** Four rows forming the tree 1 → 2 → 3, with 4 as a second root. */
function treeRows(): TestRow[] {
  const parents: ReadonlyArray<number | null> = [null, 1, 2, null];
  return makeRows(4).map((row, i) => ({ ...row, parentId: parents[i] }));
}

describe('TmGridSelectionModel (§8.1 selection model)', () => {
  describe('range basics', () => {
    it('collapseTo replaces the whole selection with one cell range', () => {
      const h = makeEngine(makeRows(5));
      const sel = h.engine.selection;
      sel.collapseTo({ row: 1, col: 1 });
      expect(sel.ranges()).toHaveLength(1);
      expect(sel.activeRange()).toEqual({
        anchor: { row: 1, col: 1 },
        focus: { row: 1, col: 1 },
        kind: 'cells',
      });
      sel.addRange({ anchor: { row: 3, col: 0 }, focus: { row: 4, col: 1 }, kind: 'cells' });
      sel.collapseTo({ row: 0, col: 2 });
      expect(sel.ranges()).toHaveLength(1);
    });

    it('addRange appends; the active range is the last; extendActiveTo moves only its focus', () => {
      const h = makeEngine(makeRows(5));
      const sel = h.engine.selection;
      sel.collapseTo({ row: 0, col: 0 });
      sel.addRange({ anchor: { row: 2, col: 0 }, focus: { row: 2, col: 0 }, kind: 'cells' });
      expect(sel.ranges()).toHaveLength(2);
      expect(sel.activeRange()).toEqual({
        anchor: { row: 2, col: 0 },
        focus: { row: 2, col: 0 },
        kind: 'cells',
      });
      sel.extendActiveTo({ row: 3, col: 2 });
      expect(sel.ranges()[0]).toEqual({
        anchor: { row: 0, col: 0 },
        focus: { row: 0, col: 0 },
        kind: 'cells',
      });
      expect(sel.activeRange()).toEqual({
        anchor: { row: 2, col: 0 },
        focus: { row: 3, col: 2 },
        kind: 'cells',
      });
    });

    it('extendActiveTo with an empty selection starts a range at the cell', () => {
      const h = makeEngine(makeRows(5));
      h.engine.selection.extendActiveTo({ row: 1, col: 2 });
      expect(h.engine.selection.ranges()).toEqual([
        { anchor: { row: 1, col: 2 }, focus: { row: 1, col: 2 }, kind: 'cells' },
      ]);
    });
  });

  describe('facade gestures', () => {
    it('moveActive extend grows from the active range focus while the active cell stays', () => {
      const h = makeEngine(makeRows(5));
      h.engine.clickCell({ row: 1, col: 1 });
      h.engine.moveActive('down', { extend: true });
      expect(h.engine.selection.activeRange()).toEqual({
        anchor: { row: 1, col: 1 },
        focus: { row: 2, col: 1 },
        kind: 'cells',
      });
      expect(h.engine.nav.activeCell()).toEqual({ row: 1, col: 1 });
      // Repeated extends accumulate from the moving focus, not from the active cell.
      h.engine.moveActive('right', { extend: true });
      h.engine.moveActive('down', { extend: true });
      expect(h.engine.selection.activeRange()).toEqual({
        anchor: { row: 1, col: 1 },
        focus: { row: 3, col: 2 },
        kind: 'cells',
      });
      expect(h.engine.nav.activeCell()).toEqual({ row: 1, col: 1 });
      expect(h.engine.selection.ranges()).toHaveLength(1);
    });

    it('clickCell: shift extends, mod adds a range and moves the active cell, plain collapses', () => {
      const h = makeEngine(makeRows(5));
      const sel = h.engine.selection;
      h.engine.clickCell({ row: 0, col: 0 });
      h.engine.clickCell({ row: 2, col: 1 }, { shift: true });
      expect(sel.ranges()).toHaveLength(1);
      expect(sel.activeRange()).toEqual({
        anchor: { row: 0, col: 0 },
        focus: { row: 2, col: 1 },
        kind: 'cells',
      });
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 0 }); // shift leaves it in place
      h.engine.clickCell({ row: 4, col: 2 }, { mod: true });
      expect(sel.ranges()).toHaveLength(2);
      expect(sel.ranges()[0]).toEqual({
        anchor: { row: 0, col: 0 },
        focus: { row: 2, col: 1 },
        kind: 'cells',
      });
      expect(sel.activeRange()).toEqual({
        anchor: { row: 4, col: 2 },
        focus: { row: 4, col: 2 },
        kind: 'cells',
      });
      expect(h.engine.nav.activeCell()).toEqual({ row: 4, col: 2 });
      h.engine.clickCell({ row: 1, col: 1 });
      expect(sel.ranges()).toEqual([
        { anchor: { row: 1, col: 1 }, focus: { row: 1, col: 1 }, kind: 'cells' },
      ]);
      expect(h.engine.nav.activeCell()).toEqual({ row: 1, col: 1 });
    });

    it('dragTo extends the active range to the cell under the pointer', () => {
      const h = makeEngine(makeRows(5));
      h.engine.clickCell({ row: 0, col: 0 });
      h.engine.dragTo({ row: 2, col: 2 });
      expect(h.engine.selection.activeRange()).toEqual({
        anchor: { row: 0, col: 0 },
        focus: { row: 2, col: 2 },
        kind: 'cells',
      });
    });
  });

  describe('kind ranges and rects', () => {
    it('selectRows records kind rows and spans every column', () => {
      const h = makeEngine(makeRows(4));
      const sel = h.engine.selection;
      sel.selectRows(1, 2, false);
      expect(sel.ranges()[0].kind).toBe('rows');
      expect(sel.rectOf(sel.ranges()[0])).toEqual({ top: 1, bottom: 2, left: 0, right: 2 });
    });

    it('selectCols spans all data rows, placeholder excluded', () => {
      const h = makeEngine(makeRows(4)); // placeholder at view row 4
      const sel = h.engine.selection;
      sel.selectCols(1, 1, false);
      expect(sel.ranges()[0].kind).toBe('cols');
      expect(sel.rectOf(sel.ranges()[0])).toEqual({ top: 0, bottom: 3, left: 1, right: 1 });
    });

    it('selectAll is one all descriptor spanning data rows × columns', () => {
      const h = makeEngine(makeRows(4));
      const sel = h.engine.selection;
      sel.selectAll();
      expect(sel.ranges()).toHaveLength(1);
      expect(sel.ranges()[0].kind).toBe('all');
      expect(sel.rectOf(sel.ranges()[0])).toEqual({ top: 0, bottom: 3, left: 0, right: 2 });
    });

    it('selectActiveRows/selectActiveCols select the active range rows/columns', () => {
      const h = makeEngine(makeRows(4));
      const sel = h.engine.selection;
      h.engine.clickCell({ row: 1, col: 1 });
      h.engine.dragTo({ row: 2, col: 1 });
      h.engine.selectActiveRows();
      expect(sel.ranges()).toHaveLength(1);
      expect(sel.ranges()[0].kind).toBe('rows');
      expect(sel.rectOf(sel.ranges()[0])).toEqual({ top: 1, bottom: 2, left: 0, right: 2 });
      h.engine.selectActiveCols(true); // additive keeps the rows range
      expect(sel.ranges()).toHaveLength(2);
      expect(sel.ranges()[1].kind).toBe('cols');
      expect(sel.rectOf(sel.ranges()[1])).toEqual({ top: 0, bottom: 3, left: 0, right: 2 });
    });

    it('isCellSelected answers across multiple ranges', () => {
      const h = makeEngine(makeRows(5));
      const sel = h.engine.selection;
      sel.collapseTo({ row: 0, col: 0 });
      sel.extendActiveTo({ row: 1, col: 1 });
      sel.addRange({ anchor: { row: 3, col: 2 }, focus: { row: 4, col: 2 }, kind: 'cells' });
      expect(sel.isCellSelected({ row: 0, col: 1 })).toBe(true);
      expect(sel.isCellSelected({ row: 4, col: 2 })).toBe(true);
      expect(sel.isCellSelected({ row: 2, col: 1 })).toBe(false);
      expect(sel.isCellSelected({ row: 3, col: 0 })).toBe(false);
    });

    it('rowIntersects/colIntersects answer header highlighting', () => {
      const h = makeEngine(makeRows(5));
      const sel = h.engine.selection;
      sel.collapseTo({ row: 0, col: 0 });
      sel.extendActiveTo({ row: 1, col: 0 });
      sel.addRange({ anchor: { row: 3, col: 2 }, focus: { row: 4, col: 2 }, kind: 'cells' });
      expect(sel.rowIntersects(0)).toBe(true);
      expect(sel.rowIntersects(2)).toBe(false);
      expect(sel.rowIntersects(4)).toBe(true);
      expect(sel.colIntersects(0)).toBe(true);
      expect(sel.colIntersects(1)).toBe(false);
      expect(sel.colIntersects(2)).toBe(true);
    });
  });

  describe('compactForCopy (Excel alignment rule)', () => {
    it('a single range compacts to its rows × cols', () => {
      const h = makeEngine(makeRows(6));
      const sel = h.engine.selection;
      sel.collapseTo({ row: 1, col: 0 });
      sel.extendActiveTo({ row: 2, col: 1 });
      expect(sel.compactForCopy()).toEqual({ rows: [1, 2], cols: [0, 1] });
    });

    it('stacked ranges with identical column spans union their rows, sorted', () => {
      const h = makeEngine(makeRows(6));
      const sel = h.engine.selection;
      sel.addRange({ anchor: { row: 3, col: 0 }, focus: { row: 4, col: 1 }, kind: 'cells' });
      sel.addRange({ anchor: { row: 1, col: 1 }, focus: { row: 0, col: 0 }, kind: 'cells' });
      expect(sel.compactForCopy()).toEqual({ rows: [0, 1, 3, 4], cols: [0, 1] });
    });

    it('overlapping same-column-span ranges dedupe rows', () => {
      const h = makeEngine(makeRows(6));
      const sel = h.engine.selection;
      sel.addRange({ anchor: { row: 0, col: 0 }, focus: { row: 2, col: 1 }, kind: 'cells' });
      sel.addRange({ anchor: { row: 1, col: 0 }, focus: { row: 3, col: 1 }, kind: 'cells' });
      expect(sel.compactForCopy()).toEqual({ rows: [0, 1, 2, 3], cols: [0, 1] });
    });

    it('abreast ranges with identical row spans union their columns', () => {
      const h = makeEngine(makeRows(6));
      const sel = h.engine.selection;
      sel.addRange({ anchor: { row: 1, col: 0 }, focus: { row: 2, col: 0 }, kind: 'cells' });
      sel.addRange({ anchor: { row: 1, col: 2 }, focus: { row: 2, col: 2 }, kind: 'cells' });
      expect(sel.compactForCopy()).toEqual({ rows: [1, 2], cols: [0, 2] });
    });

    it('misaligned ranges (different row and column spans) refuse to compact', () => {
      const h = makeEngine(makeRows(6));
      const sel = h.engine.selection;
      sel.addRange({ anchor: { row: 0, col: 0 }, focus: { row: 1, col: 1 }, kind: 'cells' });
      sel.addRange({ anchor: { row: 3, col: 1 }, focus: { row: 4, col: 2 }, kind: 'cells' });
      expect(sel.compactForCopy()).toBeNull();
    });

    it('excludes the placeholder row', () => {
      const h = makeEngine(makeRows(6)); // placeholder at view row 6
      const sel = h.engine.selection;
      sel.collapseTo({ row: 5, col: 0 });
      sel.extendActiveTo({ row: 6, col: 1 });
      expect(sel.compactForCopy()).toEqual({ rows: [5], cols: [0, 1] });
    });

    it('kind rows ranges compact (the Mod-selected-rows case)', () => {
      const h = makeEngine(makeRows(6));
      const sel = h.engine.selection;
      sel.selectRows(0, 0, false);
      sel.selectRows(2, 2, true);
      expect(sel.compactForCopy()).toEqual({ rows: [0, 2], cols: [0, 1, 2] });
    });
  });

  describe('rowsUnion', () => {
    it('returns sorted disjoint spans and merges adjacent or overlapping ones', () => {
      const h = makeEngine(makeRows(6));
      const sel = h.engine.selection;
      sel.addRange({ anchor: { row: 3, col: 0 }, focus: { row: 4, col: 0 }, kind: 'cells' });
      sel.addRange({ anchor: { row: 0, col: 2 }, focus: { row: 1, col: 2 }, kind: 'cells' });
      expect(sel.rowsUnion()).toEqual([
        { start: 0, end: 1 },
        { start: 3, end: 4 },
      ]);
      sel.addRange({ anchor: { row: 2, col: 1 }, focus: { row: 3, col: 1 }, kind: 'cells' });
      expect(sel.rowsUnion()).toEqual([{ start: 0, end: 4 }]);
    });

    it('excludes the placeholder row', () => {
      const h = makeEngine(makeRows(6)); // placeholder at view row 6
      const sel = h.engine.selection;
      sel.collapseTo({ row: 4, col: 0 });
      sel.extendActiveTo({ row: 6, col: 2 });
      expect(sel.rowsUnion()).toEqual([{ start: 4, end: 5 }]);
    });
  });

  describe('remap after external row changes', () => {
    it('a deleted endpoint substitutes the nearest surviving row in the old span', () => {
      const rows = makeRows(5);
      const h = makeEngine(rows);
      const sel = h.engine.selection;
      sel.collapseTo({ row: 1, col: 0 });
      sel.extendActiveTo({ row: 3, col: 2 }); // rows of ids 2..4
      h.externalChange(rows.filter((row) => row.id !== 4)); // the focus endpoint's row
      expect(sel.ranges()).toEqual([
        { anchor: { row: 1, col: 0 }, focus: { row: 2, col: 2 }, kind: 'cells' },
      ]);
    });

    it('a range whose rows all vanished drops while other ranges survive', () => {
      const rows = makeRows(5);
      const h = makeEngine(rows);
      const sel = h.engine.selection;
      sel.collapseTo({ row: 1, col: 1 }); // single-row range on id 2
      sel.addRange({ anchor: { row: 3, col: 0 }, focus: { row: 4, col: 1 }, kind: 'cells' });
      h.externalChange(rows.filter((row) => row.id !== 2));
      expect(sel.ranges()).toHaveLength(1);
      expect(sel.rectOf(sel.ranges()[0])).toEqual({ top: 2, bottom: 3, left: 0, right: 1 });
    });

    it('reorder: endpoints follow their row ids and the active cell follows its row', () => {
      const rows = makeRows(5);
      const h = makeEngine(rows);
      h.engine.clickCell({ row: 1, col: 0 }); // id 2
      h.engine.dragTo({ row: 2, col: 1 }); // focus on id 3
      h.externalChange([...rows].reverse()); // ids now 5,4,3,2,1
      expect(h.engine.selection.ranges()).toEqual([
        { anchor: { row: 3, col: 0 }, focus: { row: 2, col: 1 }, kind: 'cells' },
      ]);
      expect(h.engine.nav.activeCell()).toEqual({ row: 3, col: 0 });
      expect(h.notices).toEqual([]); // an external reconcile announces nothing
    });

    it('cols and all ranges survive untouched', () => {
      const rows = makeRows(5);
      const h = makeEngine(rows);
      const sel = h.engine.selection;
      sel.selectCols(1, 1, false);
      sel.addRange({ anchor: { row: 0, col: 0 }, focus: { row: 4, col: 2 }, kind: 'all' });
      h.externalChange(rows.filter((row) => row.id !== 1)); // 4 data rows remain
      expect(sel.ranges().map((range) => range.kind)).toEqual(['cols', 'all']);
      expect(sel.rectOf(sel.ranges()[0])).toEqual({ top: 0, bottom: 3, left: 1, right: 1 });
      expect(sel.rectOf(sel.ranges()[1])).toEqual({ top: 0, bottom: 3, left: 0, right: 2 });
    });

    it('the active cell falls back to the nearest row in the same column when its row vanished', () => {
      const rows = makeRows(5);
      const h = makeEngine(rows);
      h.engine.clickCell({ row: 2, col: 1 }); // id 3
      h.externalChange(rows.filter((row) => row.id !== 3));
      expect(h.engine.nav.activeCell()).toEqual({ row: 2, col: 1 }); // now the row of id 4
      expect(h.engine.selection.ranges()).toEqual([]); // its single-row range dropped
    });
  });

  describe('toSnapshot / restore', () => {
    it('round-trips ranges of every kind and the active cell by identity', () => {
      const h = makeEngine(makeRows(5));
      const sel = h.engine.selection;
      sel.selectRows(1, 2, false);
      sel.addRange({ anchor: { row: 0, col: 0 }, focus: { row: 1, col: 1 }, kind: 'cells' });
      sel.selectCols(2, 2, true);
      sel.addRange({ anchor: { row: 0, col: 0 }, focus: { row: 4, col: 2 }, kind: 'all' });
      const rectsBefore = sel.rects();
      const snapshot = sel.toSnapshot({ row: 3, col: 1 });
      expect(snapshot).toEqual({
        ranges: [
          { anchorRowId: 2, focusRowId: 3, anchorColumnKey: null, focusColumnKey: null, kind: 'rows' },
          { anchorRowId: 1, focusRowId: 2, anchorColumnKey: 'a', focusColumnKey: 'b', kind: 'cells' },
          { anchorRowId: null, focusRowId: null, anchorColumnKey: 'c', focusColumnKey: 'c', kind: 'cols' },
          { anchorRowId: null, focusRowId: null, anchorColumnKey: null, focusColumnKey: null, kind: 'all' },
        ],
        activeRowId: 4,
        activeColumnKey: 'b',
      });
      sel.clear();
      const result = sel.restore(snapshot);
      expect(result).toEqual({ restored: true, activeCell: { row: 3, col: 1 } });
      expect(sel.rects()).toEqual(rectsBefore);
    });

    it('restore is all-or-nothing for ranges; the active cell resolves independently', () => {
      const rows = makeRows(5);
      const h = makeEngine(rows);
      const sel = h.engine.selection;
      sel.collapseTo({ row: 4, col: 0 }); // range endpoint on id 5
      const snapshot = sel.toSnapshot({ row: 1, col: 2 }); // active on id 2
      h.externalChange(rows.filter((row) => row.id !== 5));
      sel.clear();
      const result = sel.restore(snapshot);
      expect(result.restored).toBe(false);
      expect(result.activeCell).toEqual({ row: 1, col: 2 }); // id 2 still resolves
      expect(sel.ranges()).toEqual([]); // the failed restore left the selection alone
    });

    it('restore resolves the active cell to null independently of the ranges', () => {
      const rows = makeRows(5);
      const h = makeEngine(rows);
      const sel = h.engine.selection;
      sel.collapseTo({ row: 0, col: 0 }); // range on id 1
      const snapshot = sel.toSnapshot({ row: 4, col: 0 }); // active on id 5
      h.externalChange(rows.filter((row) => row.id !== 5));
      const result = sel.restore(snapshot);
      expect(result).toEqual({ restored: true, activeCell: null });
      expect(sel.rects()).toEqual([{ top: 0, bottom: 0, left: 0, right: 0 }]);
    });
  });

  describe('setExpanded collapses selection (tree grid)', () => {
    it('collapsing an ancestor of the active cell moves activation to that ancestor', () => {
      const h = makeEngine(treeRows(), {
        tree: { parentId: (row) => row['parentId'] as TmRowId | null },
      });
      h.engine.clickCell({ row: 2, col: 1 }); // id 3, the grandchild
      h.engine.dragTo({ row: 3, col: 2 });
      h.engine.setExpanded(1, false); // hides ids 2 and 3
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 1 }); // the collapsed ancestor
      expect(h.engine.selection.ranges()).toEqual([
        { anchor: { row: 0, col: 1 }, focus: { row: 0, col: 1 }, kind: 'cells' },
      ]);
    });

    it('with the active row still visible, selection collapses to it at its new index', () => {
      const h = makeEngine(treeRows(), {
        tree: { parentId: (row) => row['parentId'] as TmRowId | null },
      });
      h.engine.clickCell({ row: 3, col: 0 }); // id 4, the second root
      h.engine.dragTo({ row: 3, col: 2 });
      h.engine.setExpanded(4, false); // a leaf: not expandable → no-op
      expect(h.engine.selection.activeRange()).toEqual({
        anchor: { row: 3, col: 0 },
        focus: { row: 3, col: 2 },
        kind: 'cells',
      });
      h.engine.setExpanded(1, false); // id 4 shifts up to view row 1
      expect(h.engine.nav.activeCell()).toEqual({ row: 1, col: 0 });
      expect(h.engine.selection.ranges()).toEqual([
        { anchor: { row: 1, col: 0 }, focus: { row: 1, col: 0 }, kind: 'cells' },
      ]);
    });
  });
});
