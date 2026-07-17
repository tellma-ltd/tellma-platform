// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { makeEngine, makeRows, type TestHarness, type TestRow } from './tm-grid-testing.util';
import type { TmRowCol } from './tm-grid-types';

/**
 * Runs one Tab step the way the component layer does: computes the target,
 * then activates it while keeping the Tab run alive.
 */
function pressTab(harness: TestHarness, backward = false): TmRowCol | 'exit' | null {
  const target = harness.engine.nav.tab(backward);
  if (target !== null && target !== 'exit') {
    harness.engine.nav.setActive(target, { keepTabRun: true });
  }
  return target;
}

/**
 * Ten rows whose column `a` holds two content runs (rows 0-2 and 5-6) with
 * empty ground elsewhere; columns `b`/`c` stay fully filled.
 */
function gappedRows(): TestRow[] {
  const filled = new Set([0, 1, 2, 5, 6]);
  return makeRows(10).map((row, i) => (filled.has(i) ? row : { ...row, a: null }));
}

describe('TmGridNav (§8.2 keyboard motions)', () => {
  describe('plain motions', () => {
    it('clamps at all four extents', () => {
      const h = makeEngine(makeRows(3), { canAddRows: false });
      h.engine.clickCell({ row: 0, col: 0 });
      h.engine.moveActive('up');
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 0 });
      h.engine.moveActive('inlineStart');
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 0 });
      h.engine.clickCell({ row: 2, col: 2 });
      h.engine.moveActive('down');
      expect(h.engine.nav.activeCell()).toEqual({ row: 2, col: 2 });
      h.engine.moveActive('inlineEnd');
      expect(h.engine.nav.activeCell()).toEqual({ row: 2, col: 2 });
    });

    it('down from the last data row reaches the placeholder row and stops there', () => {
      const h = makeEngine(makeRows(3)); // editable + canAddRows → placeholder at view row 3
      h.engine.clickCell({ row: 2, col: 1 });
      h.engine.moveActive('down');
      expect(h.engine.nav.activeCell()).toEqual({ row: 3, col: 1 });
      expect(h.engine.model.isPlaceholder(3)).toBe(true);
      h.engine.moveActive('down');
      expect(h.engine.nav.activeCell()).toEqual({ row: 3, col: 1 });
    });

    it('moveActive with no active cell activates the default cell (0,0) without moving', () => {
      const h = makeEngine(makeRows(2));
      h.engine.moveActive('down');
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 0 });
      expect(h.engine.selection.activeRange()).toEqual({
        anchor: { row: 0, col: 0 },
        focus: { row: 0, col: 0 },
        kind: 'cells',
      });
    });
  });

  describe('RTL arrow mapping (§15)', () => {
    it('maps physical left/right through the direction: in RTL, left = inline-end (+1 col)', () => {
      const h = makeEngine(makeRows(2));
      h.engine.clickCell({ row: 0, col: 1 });
      h.direction.set('rtl');
      h.engine.moveActive('left');
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 2 });
      h.engine.moveActive('right');
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 1 });
      h.engine.moveActive('right');
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 0 });
      h.direction.set('ltr');
      h.engine.moveActive('right');
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 1 });
      h.engine.moveActive('left');
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 0 });
    });

    it('inlineStart/inlineEnd are unaffected by direction', () => {
      const h = makeEngine(makeRows(2));
      h.direction.set('rtl');
      h.engine.clickCell({ row: 0, col: 1 });
      h.engine.moveActive('inlineEnd');
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 2 });
      h.engine.moveActive('inlineStart');
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 1 });
    });
  });

  describe('paging', () => {
    it('PageDown/PageUp move by pageSize and clamp at the extents', () => {
      const h = makeEngine(makeRows(20), { pageSize: 7, canAddRows: false });
      h.engine.clickCell({ row: 0, col: 1 });
      h.engine.moveActive('pageDown');
      expect(h.engine.nav.activeCell()).toEqual({ row: 7, col: 1 });
      h.engine.moveActive('pageDown');
      expect(h.engine.nav.activeCell()).toEqual({ row: 14, col: 1 });
      h.engine.moveActive('pageDown');
      expect(h.engine.nav.activeCell()).toEqual({ row: 19, col: 1 }); // clamped at the last row
      h.engine.moveActive('pageUp');
      expect(h.engine.nav.activeCell()).toEqual({ row: 12, col: 1 });
      h.engine.clickCell({ row: 5, col: 1 });
      h.engine.moveActive('pageUp');
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 1 }); // clamped at row 0
    });

    it('respects the pageSize signal as it changes', () => {
      const h = makeEngine(makeRows(20), { canAddRows: false }); // default pageSize 10
      h.engine.clickCell({ row: 0, col: 0 });
      h.engine.moveActive('pageDown');
      expect(h.engine.nav.activeCell()).toEqual({ row: 10, col: 0 });
      h.pageSize.set(3);
      h.engine.moveActive('pageDown');
      expect(h.engine.nav.activeCell()).toEqual({ row: 13, col: 0 });
    });
  });

  describe('row and grid extremes', () => {
    it('rowStart/rowEnd move to the first/last cell of the row', () => {
      const h = makeEngine(makeRows(3));
      h.engine.clickCell({ row: 1, col: 1 });
      h.engine.moveActive('rowStart');
      expect(h.engine.nav.activeCell()).toEqual({ row: 1, col: 0 });
      h.engine.moveActive('rowEnd');
      expect(h.engine.nav.activeCell()).toEqual({ row: 1, col: 2 });
    });

    it('gridStart is (0,0); gridEnd is last data row × last col, placeholder excluded', () => {
      const h = makeEngine(makeRows(4)); // placeholder at view row 4
      h.engine.clickCell({ row: 2, col: 1 });
      h.engine.moveActive('gridStart');
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 0 });
      h.engine.moveActive('gridEnd');
      expect(h.engine.nav.activeCell()).toEqual({ row: 3, col: 2 });
      expect(h.engine.model.isPlaceholder(4)).toBe(true);
    });

    it('gridEnd lands on the placeholder row when there are zero data rows', () => {
      const h = makeEngine([]); // the placeholder is the only view row
      h.engine.nav.setActive({ row: 0, col: 0 });
      h.engine.moveActive('gridEnd');
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 2 });
      expect(h.engine.model.isPlaceholder(0)).toBe(true);
    });
  });

  describe('data-edge jumps (Mod+Arrow)', () => {
    it('from inside a content run moves to the far edge of the run', () => {
      const h = makeEngine(gappedRows());
      h.engine.clickCell({ row: 0, col: 0 });
      h.engine.moveActive('down', { jump: true });
      expect(h.engine.nav.activeCell()).toEqual({ row: 2, col: 0 });
    });

    it('from a run edge with a gap beyond moves to the start of the next run', () => {
      const h = makeEngine(gappedRows());
      h.engine.clickCell({ row: 2, col: 0 });
      h.engine.moveActive('down', { jump: true });
      expect(h.engine.nav.activeCell()).toEqual({ row: 5, col: 0 });
    });

    it('from empty ground moves to the first non-empty cell, in both directions', () => {
      const h = makeEngine(gappedRows());
      h.engine.clickCell({ row: 3, col: 0 });
      h.engine.moveActive('down', { jump: true });
      expect(h.engine.nav.activeCell()).toEqual({ row: 5, col: 0 });
      h.engine.clickCell({ row: 4, col: 0 });
      h.engine.moveActive('up', { jump: true });
      expect(h.engine.nav.activeCell()).toEqual({ row: 2, col: 0 });
    });

    it('with nothing further moves to the extent', () => {
      const h = makeEngine(gappedRows());
      h.engine.clickCell({ row: 6, col: 0 }); // rows 7-9 are empty in column a
      h.engine.moveActive('down', { jump: true });
      expect(h.engine.nav.activeCell()).toEqual({ row: 9, col: 0 });
    });

    it('vertical jumps never land on the placeholder; plain Down still reaches it', () => {
      const h = makeEngine(gappedRows()); // placeholder at view row 10
      h.engine.clickCell({ row: 9, col: 1 }); // column b is fully filled
      h.engine.moveActive('down', { jump: true });
      expect(h.engine.nav.activeCell()).toEqual({ row: 9, col: 1 }); // stays on the last data row
      h.engine.moveActive('down');
      expect(h.engine.nav.activeCell()).toEqual({ row: 10, col: 1 });
      expect(h.engine.model.isPlaceholder(10)).toBe(true);
    });

    it('Ctrl+Up from the placeholder lands on the last non-empty data cell of the column', () => {
      const h = makeEngine(gappedRows());
      h.engine.nav.setActive({ row: 10, col: 0 }); // placeholder; column a empty from row 7 down
      h.engine.moveActive('up', { jump: true });
      expect(h.engine.nav.activeCell()).toEqual({ row: 6, col: 0 });
      h.engine.nav.setActive({ row: 10, col: 1 }); // column b filled to the bottom
      h.engine.moveActive('up', { jump: true });
      expect(h.engine.nav.activeCell()).toEqual({ row: 9, col: 1 });
    });

    it('on a fully empty grid vertical jumps stay put without throwing', () => {
      const h = makeEngine([]); // placeholder-only grid
      h.engine.nav.setActive({ row: 0, col: 0 });
      h.engine.moveActive('down', { jump: true });
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 0 });
      h.engine.moveActive('up', { jump: true });
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 0 });
    });

    it('horizontal jumps along a row follow the same run/gap rules', () => {
      const h = makeEngine([{ id: 1, a: 'a1', b: 'b1', c: null, d: 'd1', e: null }], {
        columns: [{ key: 'a' }, { key: 'b' }, { key: 'c' }, { key: 'd' }, { key: 'e' }],
      });
      h.engine.clickCell({ row: 0, col: 0 });
      h.engine.moveActive('inlineEnd', { jump: true });
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 1 }); // far edge of the a-b run
      h.engine.moveActive('inlineEnd', { jump: true });
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 3 }); // start of the next run
      h.engine.moveActive('inlineEnd', { jump: true });
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 4 }); // nothing further: the extent
      h.engine.moveActive('inlineStart', { jump: true });
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 3 }); // empty ground → first non-empty
      h.engine.moveActive('inlineStart', { jump: true });
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 1 }); // run edge → previous run's start
    });
  });

  describe('Tab traversal', () => {
    /** Two data rows; column b is readonly on row id 1 only. */
    function tabHarness(): TestHarness {
      return makeEngine(makeRows(2), {
        columns: [{ key: 'a' }, { key: 'b', cellReadonly: (row) => row.id === 1 }, { key: 'c' }],
      });
    }

    it('skips readonly cells and wraps across rows', () => {
      const h = tabHarness();
      h.engine.nav.setActive({ row: 0, col: 0 });
      expect(pressTab(h)).toEqual({ row: 0, col: 2 }); // (0,1) is readonly
      expect(pressTab(h)).toEqual({ row: 1, col: 0 }); // wraps to the next row
      expect(pressTab(h)).toEqual({ row: 1, col: 1 }); // b is editable on row id 2
    });

    it('includes placeholder-row cells and exits past the last editable cell', () => {
      const h = tabHarness(); // placeholder at view row 2
      h.engine.nav.setActive({ row: 1, col: 2 });
      expect(pressTab(h)).toEqual({ row: 2, col: 0 });
      expect(h.engine.nav.tabRunOriginCol).toBe(2);
      expect(pressTab(h)).toEqual({ row: 2, col: 1 }); // placeholder cells ignore the readonly oracle
      expect(pressTab(h)).toEqual({ row: 2, col: 2 });
      expect(pressTab(h)).toBe('exit');
      expect(h.engine.nav.tabRunOriginCol).toBeNull(); // exiting ends the run
    });

    it('backward: skips readonly cells and exits past the first editable cell', () => {
      const h = tabHarness();
      h.engine.nav.setActive({ row: 0, col: 2 });
      expect(pressTab(h, true)).toEqual({ row: 0, col: 0 }); // (0,1) is readonly
      expect(pressTab(h, true)).toBe('exit');
    });

    it('exits immediately on a readonly grid', () => {
      const h = makeEngine(makeRows(2));
      h.editable.set(false);
      h.engine.nav.setActive({ row: 0, col: 0 });
      expect(h.engine.nav.tab(false)).toBe('exit');
      expect(h.engine.nav.tab(true)).toBe('exit');
    });

    it('returns null while no cell is active', () => {
      const h = makeEngine(makeRows(2));
      expect(h.engine.nav.tab(false)).toBeNull();
    });
  });

  describe('Tab-run origin and enterTarget', () => {
    it('Enter after a Tab run returns to the origin column one row below and ends the run', () => {
      const h = makeEngine(makeRows(3));
      h.engine.nav.setActive({ row: 0, col: 1 });
      expect(pressTab(h)).toEqual({ row: 0, col: 2 });
      expect(pressTab(h)).toEqual({ row: 1, col: 0 });
      expect(h.engine.nav.tabRunOriginCol).toBe(1);
      expect(h.engine.nav.enterTarget(false)).toEqual({ row: 1, col: 1 }); // origin row + 1
      // The run ended: the next Enter is a plain move from the active cell.
      expect(h.engine.nav.enterTarget(false)).toEqual({ row: 2, col: 0 });
    });

    it('without a run, Enter moves one row down (up when backward), clamped at the extents', () => {
      const h = makeEngine(makeRows(3)); // placeholder at view row 3
      h.engine.nav.setActive({ row: 0, col: 1 });
      expect(h.engine.nav.enterTarget(false)).toEqual({ row: 1, col: 1 });
      expect(h.engine.nav.enterTarget(true)).toEqual({ row: 0, col: 1 }); // clamped at row 0
      h.engine.nav.setActive({ row: 3, col: 1 });
      expect(h.engine.nav.enterTarget(false)).toEqual({ row: 3, col: 1 }); // clamped at the last row
    });

    it('resetTabRun clears the run', () => {
      const h = makeEngine(makeRows(3));
      h.engine.nav.setActive({ row: 0, col: 1 });
      expect(pressTab(h)).toEqual({ row: 0, col: 2 });
      h.engine.nav.resetTabRun();
      // Plain move from the active cell (0,2) — not a return to origin column 1.
      expect(h.engine.nav.enterTarget(false)).toEqual({ row: 1, col: 2 });
    });

    it('explicit setActive clears the run', () => {
      const h = makeEngine(makeRows(3));
      h.engine.nav.setActive({ row: 0, col: 1 });
      expect(pressTab(h)).toEqual({ row: 0, col: 2 });
      h.engine.nav.setActive({ row: 0, col: 2 }); // no keepTabRun
      expect(h.engine.nav.tabRunOriginCol).toBeNull();
      expect(h.engine.nav.enterTarget(false)).toEqual({ row: 1, col: 2 });
    });
  });

  describe('reclamp after external changes', () => {
    it('clamps the active cell when the rows shrink and nulls it on an empty grid', () => {
      const h = makeEngine(makeRows(5), { canAddRows: false });
      h.engine.clickCell({ row: 4, col: 2 });
      h.externalChange(makeRows(2)); // the active row's id vanished → clamp
      expect(h.engine.nav.activeCell()).toEqual({ row: 1, col: 2 });
      h.externalChange([]);
      expect(h.engine.nav.activeCell()).toBeNull();
    });
  });
});
