// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_PARSE_ERROR, type TmRowId } from '@tellma/core-ui/contracts';

import { makeEngine, makeRows, type TestRow } from './tm-grid-testing.util';
import type { TmGridTreeOptions } from './tm-grid-types';

const TREE: TmGridTreeOptions<TestRow> = {
  parentId: (row) => (row['parentId'] as TmRowId | null | undefined) ?? null,
};

describe('TmGridEngine', () => {
  describe('displayText', () => {
    it('overlays invalid raw text only while editable; the map survives the flip', () => {
      const h = makeEngine(makeRows(1));
      expect(h.engine.displayText({ row: 0, col: 0 })).toBe('a1');
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      h.engine.edit.commitText('BAD');
      expect(h.engine.displayText({ row: 0, col: 0 })).toBe('BAD');
      h.editable.set(false);
      expect(h.engine.displayText({ row: 0, col: 0 })).toBe(''); // the cleared model's truth
      expect(h.engine.annotations.invalidCount()).toBe(1); // the map survives the flip
      h.editable.set(true);
      expect(h.engine.displayText({ row: 0, col: 0 })).toBe('BAD');
    });
  });

  describe('moveActive', () => {
    it('activates 0,0 when nothing is active', () => {
      const h = makeEngine(makeRows(2));
      expect(h.engine.nav.activeCell()).toBeNull();
      h.engine.moveActive('down');
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 0 });
      expect(h.engine.selection.activeRange()).toEqual({
        anchor: { row: 0, col: 0 },
        focus: { row: 0, col: 0 },
        kind: 'cells',
      });
    });

    it('plain motion collapses the selection; extend moves only the focus', () => {
      const h = makeEngine(makeRows(3));
      h.engine.clickCell({ row: 0, col: 0 });
      h.engine.dragTo({ row: 1, col: 1 });
      h.engine.moveActive('down');
      expect(h.engine.nav.activeCell()).toEqual({ row: 1, col: 0 });
      expect(h.engine.selection.ranges()).toEqual([
        { anchor: { row: 1, col: 0 }, focus: { row: 1, col: 0 }, kind: 'cells' },
      ]);
      h.engine.moveActive('right', { extend: true });
      expect(h.engine.nav.activeCell()).toEqual({ row: 1, col: 0 }); // stays put
      expect(h.engine.selection.activeRange()).toEqual({
        anchor: { row: 1, col: 0 },
        focus: { row: 1, col: 1 },
        kind: 'cells',
      });
    });
  });

  describe('clearSelection', () => {
    it('clears editable cells across ranges as one entry, skips readonly and placeholder, disarms a cut', () => {
      const h = makeEngine(makeRows(3), {
        columns: [
          { key: 'a' },
          { key: 'b', cellReadonly: (row) => row.id === 1 },
          { key: 'c', clearedValue: 'X' },
        ],
      });
      h.engine.annotations.setInvalid(1, 'a', { rawText: 'zz', reason: 'parse' });
      h.engine.clickCell({ row: 0, col: 0 });
      h.engine.clickCell({ row: 0, col: 1 }, { shift: true });
      h.engine.clipboard.cut(() => 'fp');
      expect(h.engine.clipboard.pendingCut()).not.toBeNull();
      // A second range that reaches into the placeholder row (view index 3).
      h.engine.selection.addRange({
        anchor: { row: 2, col: 2 },
        focus: { row: 3, col: 2 },
        kind: 'cells',
      });
      h.engine.clearSelection();
      expect(h.rows()[0]['a']).toBeNull();
      expect(h.rows()[0]['b']).toBe('b1'); // readonly cell skipped
      expect(h.rows()[2]['c']).toBe('X'); // the column's cleared value
      expect(h.rows()).toHaveLength(3); // the placeholder never materialized
      expect(h.engine.annotations.invalidCount()).toBe(0); // invalid input dropped
      expect(h.engine.clipboard.pendingCut()).toBeNull(); // the cut disarmed
      h.engine.history.undo(); // ONE entry restores everything
      expect(h.rows()[0]['a']).toBe('a1');
      expect(h.rows()[2]['c']).toBe('c3');
      expect(h.engine.annotations.invalidInput(1, 'a')).toMatchObject({
        rawText: 'zz',
        reason: 'parse',
      });
      expect(h.engine.history.canUndo()).toBe(false);
    });
  });

  describe('escape', () => {
    it('disarms a pending cut once, then reports nothing to do', () => {
      const h = makeEngine(makeRows(2));
      h.engine.clickCell({ row: 0, col: 0 });
      h.engine.clipboard.cut(() => 'fp');
      expect(h.engine.escape()).toBe(true);
      expect(h.engine.clipboard.pendingCut()).toBeNull();
      expect(h.engine.escape()).toBe(false);
    });
  });

  describe('trees', () => {
    it('insertChildRow appends as the last child, expands the parent, activates the first editable cell', () => {
      const h = makeEngine(
        [
          { id: 1, a: 'root1', b: 'b1', c: 'c1' },
          { id: 2, a: 'child2', b: 'b2', c: 'c2', parentId: 1 },
          { id: 3, a: 'root3', b: 'b3', c: 'c3' },
        ],
        { tree: TREE, columns: [{ key: 'a', editable: false }, { key: 'b' }, { key: 'c' }] },
      );
      h.engine.setExpanded(1, false);
      const created = h.engine.insertChildRow(1);
      expect(created).toHaveLength(1);
      expect(h.rows()[3]).toBe(created[0].row); // appended to the model
      expect(h.engine.model.expandedIds().has(1)).toBe(true);
      expect(h.engine.model.viewIndexOfRow(created[0].id)).toBe(2); // last child of the parent
      expect(h.engine.nav.activeCell()).toEqual({ row: 2, col: 1 }); // first EDITABLE column
      expect(h.notices).toContainEqual({ kind: 'rowsInserted', count: 1 });
      h.engine.history.undo(); // one entry
      expect(h.rows().map((row) => row.id)).toEqual([1, 2, 3]);
      expect(h.engine.history.canUndo()).toBe(false);
    });

    it('insertRows below a nested row stamps the reference row parent (a sibling insert)', () => {
      const h = makeEngine(
        [
          { id: 1, a: 'root1', parentId: null },
          { id: 2, a: 'child2', parentId: 1 },
          { id: 3, a: 'child3', parentId: 1 },
          { id: 4, a: 'root4', parentId: null },
        ],
        { tree: TREE, columns: [{ key: 'a' }] },
      );
      // View order 1, 2, 3, 4 — select the level-2 row child 2.
      h.engine.clickCell({ row: 1, col: 0 });
      const created = h.engine.insertRows('below');
      expect(created).toHaveLength(1);
      // The created row is a SIBLING of the reference (same parent 1)...
      expect(created[0].row['parentId']).toBe(1);
      // ...inserted as the next sibling: view order 1, 2, <new>, 3, 4.
      expect(h.engine.model.viewIndexOfRow(created[0].id)).toBe(2);
      expect(h.notices).toContainEqual({ kind: 'rowsInserted', count: 1 });
    });

    it('deleteSelectedRows removes each selected row with its whole subtree', () => {
      const h = makeEngine(
        [
          { id: 1, a: 'r1' },
          { id: 2, a: 'c2', parentId: 1 },
          { id: 4, a: 'g4', parentId: 2 },
          { id: 3, a: 'c3', parentId: 1 },
          { id: 5, a: 'r5' },
        ],
        { tree: TREE, columns: [{ key: 'a' }] },
      );
      h.engine.clickCell({ row: 0, col: 0 });
      h.engine.deleteSelectedRows();
      expect(h.rows().map((row) => row.id)).toEqual([5]);
      expect(h.notices).toContainEqual({ kind: 'rowsDeleted', count: 4 });
    });

    it('deleteSelectedRows collapses the selection onto the successor, and chains on repeat', () => {
      const h = makeEngine(makeRows(4)); // ids 1,2,3,4
      h.engine.clickCell({ row: 1, col: 1 }); // select id 2
      h.engine.deleteSelectedRows();
      expect(h.rows().map((row) => row.id)).toEqual([1, 3, 4]);
      // Reconcile collapses the dropped range onto the moved-down active cell,
      // so its row/column headers stay highlighted (not an orphaned empty
      // selection) AND a repeat delete has a target.
      expect(h.engine.nav.activeCell()).toEqual({ row: 1, col: 1 });
      expect(h.engine.selection.rowIntersects(1)).toBe(true);
      expect(h.engine.selection.colIntersects(1)).toBe(true);
      h.engine.deleteSelectedRows();
      expect(h.rows().map((row) => row.id)).toEqual([1, 4]);
      h.engine.deleteSelectedRows();
      expect(h.rows().map((row) => row.id)).toEqual([1]);
    });

    it('collapsing an ancestor of the active cell moves activation to the ancestor', () => {
      const h = makeEngine(
        [
          { id: 1, a: 'r1' },
          { id: 2, a: 'c2', parentId: 1 },
          { id: 3, a: 'g3', parentId: 2 },
        ],
        { tree: TREE, columns: [{ key: 'a' }, { key: 'b' }] },
      );
      h.engine.clickCell({ row: 2, col: 1 }); // the grandchild
      h.engine.setExpanded(1, false);
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 1 });
      expect(h.engine.selection.activeRange()).toEqual({
        anchor: { row: 0, col: 1 },
        focus: { row: 0, col: 1 },
        kind: 'cells',
      });
    });
  });

  describe('reconcile', () => {
    it('remaps active cell, selection, annotations, and the pending cut in one pass', () => {
      const h = makeEngine(makeRows(4));
      h.engine.clickCell({ row: 0, col: 0 });
      h.engine.clickCell({ row: 1, col: 0 }, { shift: true });
      h.engine.clipboard.cut(() => 'fp'); // arms rows 1 and 2
      h.engine.clickCell({ row: 2, col: 1 });
      h.engine.clickCell({ row: 3, col: 1 }, { shift: true }); // anchor id 3, focus id 4
      h.engine.annotations.setInvalid(1, 'a', { rawText: 'x1', reason: 'parse' });
      h.engine.annotations.setInvalid(4, 'a', { rawText: 'x4', reason: 'parse' });
      const current = h.rows();
      h.externalChange([current[3], current[2], current[1]]); // drop id 1, reverse the rest
      // The active cell followed row id 3 to view index 1.
      expect(h.engine.nav.activeCell()).toEqual({ row: 1, col: 1 });
      // The selection endpoints followed their rows.
      expect(h.engine.selection.activeRange()).toEqual({
        anchor: { row: 1, col: 1 },
        focus: { row: 0, col: 1 },
        kind: 'cells',
      });
      // Annotations pruned to surviving rows only.
      expect(h.engine.annotations.invalidCount()).toBe(1);
      expect(h.engine.annotations.invalidInput(4, 'a')).toBeDefined();
      // The pending cut kept only its surviving row.
      expect(h.engine.clipboard.pendingCut()?.rowIds).toEqual([2]);
    });
  });

  describe('dispose', () => {
    it('aborts outstanding paste resolutions and clears the pending marks', () => {
      const h = makeEngine(makeRows(2), {
        columns: [{ key: 'a', hasResolver: true, parse: () => TM_PARSE_ERROR }],
      });
      h.engine.clickCell({ row: 0, col: 0 });
      const result = h.engine.clipboard.paste({ matrix: [['foo']] });
      expect(result.resolutions).toHaveLength(1);
      const signal = result.resolutions[0].context.signal;
      expect(signal.aborted).toBe(false);
      expect(h.engine.annotations.pendingCount()).toBe(1);
      h.engine.dispose();
      expect(signal.aborted).toBe(true);
      expect(h.engine.annotations.pendingCount()).toBe(0);
    });
  });

  describe('scale smoke', () => {
    it('constructs over 100k rows and basic motions stay correct', () => {
      const h = makeEngine(makeRows(100_000));
      expect(h.engine.model.viewRowCount()).toBe(100_001); // + the placeholder
      h.engine.clickCell({ row: 0, col: 0 });
      h.engine.moveActive('down');
      expect(h.engine.nav.activeCell()).toEqual({ row: 1, col: 0 });
      h.engine.moveActive('gridEnd');
      expect(h.engine.nav.activeCell()).toEqual({ row: 99_999, col: 2 });
      expect(h.engine.displayText({ row: 99_999, col: 0 })).toBe('a100000');
      h.engine.moveActive('pageUp'); // default page size is 10
      expect(h.engine.nav.activeCell()).toEqual({ row: 99_989, col: 2 });
    });
  });
});
