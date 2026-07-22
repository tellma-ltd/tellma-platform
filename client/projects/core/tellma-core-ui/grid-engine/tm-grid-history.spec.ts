// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmRowId } from '@tellma/core-ui/contracts';

import type { TmGridInvalidInput } from './tm-grid-cell-annotations';
import type { TmGridCellWrite } from './tm-grid-history';
import { makeEngine, makeRows, type TestRow } from './tm-grid-testing.util';
import type { TmGridTreeOptions } from './tm-grid-types';

/** A cell write over the harness columns, whose column id equals the key. */
function write(
  rowId: TmRowId,
  key: string,
  before: unknown,
  after: unknown,
  invalid: {
    readonly before?: TmGridInvalidInput | null;
    readonly after?: TmGridInvalidInput | null;
  } = {},
): TmGridCellWrite {
  return {
    rowId,
    columnId: key,
    columnKey: key,
    before,
    after,
    invalidBefore: invalid.before ?? null,
    invalidAfter: invalid.after ?? null,
  };
}

const TREE: TmGridTreeOptions<TestRow> = {
  parentId: (row) => (row['parentId'] as TmRowId | null | undefined) ?? null,
  parentIdKey: 'parentId',
};

function treeRows(): TestRow[] {
  return [
    { id: 1, a: 'root1', b: 'b1', c: 'c1' },
    { id: 2, a: 'child2', b: 'b2', c: 'c2', parentId: 1 },
    { id: 3, a: 'child3', b: 'b3', c: 'c3', parentId: 1 },
    { id: 4, a: 'root4', b: 'b4', c: 'c4' },
  ];
}

describe('TmGridHistory', () => {
  describe('runCellWrites', () => {
    it('executes through the writer and records one undoable entry', () => {
      const h = makeEngine(makeRows(2));
      expect(h.engine.history.canUndo()).toBe(false);
      h.engine.history.runCellWrites('cellEdit', [write(1, 'a', 'a1', 'X')]);
      expect(h.rows()[0]['a']).toBe('X');
      expect(h.engine.history.canUndo()).toBe(true);
      expect(h.engine.history.canRedo()).toBe(false);
    });

    it('records nothing for an all-no-op batch', () => {
      const h = makeEngine(makeRows(2));
      const before = h.rows();
      h.engine.history.runCellWrites('cellEdit', [write(1, 'a', 'a1', 'a1')]);
      expect(h.rows()).toBe(before); // the writer was never touched
      expect(h.engine.history.canUndo()).toBe(false);
    });

    it('elides the no-op writes of a mixed batch', () => {
      const h = makeEngine(makeRows(2));
      const firstRow = h.rows()[0];
      h.engine.history.runCellWrites('cellEdit', [
        write(1, 'a', 'a1', 'a1'), // no-op: same value, same invalid state
        write(2, 'b', 'b2', 'Y'),
      ]);
      expect(h.rows()[0]).toBe(firstRow); // untouched by the writer
      expect(h.rows()[1]['b']).toBe('Y');
      h.engine.history.undo();
      expect(h.rows()[1]['b']).toBe('b2');
      expect(h.engine.history.canUndo()).toBe(false); // it was one entry
    });

    it('undo restores value and invalid state; redo re-applies; the signals track', () => {
      const h = makeEngine(makeRows(1));
      const bad: TmGridInvalidInput = { rawText: 'BAD', reason: 'parse' };
      h.engine.history.runCellWrites('cellEdit', [write(1, 'a', 'a1', null, { after: bad })]);
      expect(h.rows()[0]['a']).toBeNull();
      expect(h.engine.annotations.invalidInput(1, 'a')).toMatchObject(bad);

      expect(h.engine.history.undo()).toBe(true);
      expect(h.rows()[0]['a']).toBe('a1');
      expect(h.engine.annotations.invalidInput(1, 'a')).toBeUndefined();
      expect(h.engine.history.canUndo()).toBe(false);
      expect(h.engine.history.canRedo()).toBe(true);

      expect(h.engine.history.redo()).toBe(true);
      expect(h.rows()[0]['a']).toBeNull();
      expect(h.engine.annotations.invalidInput(1, 'a')).toMatchObject(bad);
      expect(h.engine.history.canUndo()).toBe(true);
      expect(h.engine.history.canRedo()).toBe(false);
    });
  });

  describe('capacity', () => {
    it('evicts the oldest entry beyond the cap', () => {
      const h = makeEngine(makeRows(1), { historyCapacity: 3 });
      const history = h.engine.history;
      history.runCellWrites('cellEdit', [write(1, 'a', 'a1', 'v1')]);
      history.runCellWrites('cellEdit', [write(1, 'a', 'v1', 'v2')]);
      history.runCellWrites('cellEdit', [write(1, 'a', 'v2', 'v3')]);
      history.runCellWrites('cellEdit', [write(1, 'a', 'v3', 'v4')]);
      expect(history.undo()).toBe(true);
      expect(history.undo()).toBe(true);
      expect(history.undo()).toBe(true);
      // The evicted first op is out of reach: its write stays applied.
      expect(h.rows()[0]['a']).toBe('v1');
      expect(history.canUndo()).toBe(false);
      expect(history.undo()).toBe(false);
    });

    it('a new op clears the redo stack', () => {
      const h = makeEngine(makeRows(1));
      const history = h.engine.history;
      history.runCellWrites('cellEdit', [write(1, 'a', 'a1', 'v1')]);
      history.undo();
      expect(history.canRedo()).toBe(true);
      history.runCellWrites('cellEdit', [write(1, 'a', 'a1', 'v2')]);
      expect(history.canRedo()).toBe(false);
      expect(history.redo()).toBe(false);
    });
  });

  describe('row insert', () => {
    it('above: creates rows via the factory before the first selected row', () => {
      const h = makeEngine(makeRows(3));
      h.engine.clickCell({ row: 1, col: 0 });
      const created = h.engine.insertRows('above');
      expect(created).toHaveLength(1);
      expect(h.rows()).toHaveLength(4);
      expect(h.rows()[1]).toBe(created[0].row); // at the reference row's model index
      expect(h.notices).toContainEqual({ kind: 'rowsInserted', count: 1 });
    });

    it('below: creates one row per selected row after the last selected row', () => {
      const h = makeEngine(makeRows(3));
      h.engine.clickCell({ row: 0, col: 0 });
      h.engine.clickCell({ row: 1, col: 0 }, { shift: true });
      const created = h.engine.insertRows('below');
      expect(created).toHaveLength(2);
      expect(h.rows().map((row) => row.id)).toEqual([1, 2, created[0].id, created[1].id, 3]);
      expect(h.notices).toContainEqual({ kind: 'rowsInserted', count: 2 });
    });

    it('undo removes the created rows; redo restores the exact same objects', () => {
      const h = makeEngine(makeRows(3));
      h.engine.clickCell({ row: 1, col: 0 });
      const created = h.engine.insertRows('above');
      h.engine.history.undo();
      expect(h.rows().map((row) => row.id)).toEqual([1, 2, 3]);
      h.engine.history.redo();
      expect(h.rows()[1]).toBe(created[0].row); // same object, not a re-minted row
      expect(h.rows()[1].id).toBe(created[0].id);
    });
  });

  describe('row delete', () => {
    it('snapshots; undo re-inserts the original objects at the original indexes', () => {
      const h = makeEngine(makeRows(3));
      const [first, second, third] = h.rows();
      h.engine.clickCell({ row: 1, col: 0 });
      h.engine.deleteSelectedRows();
      expect(h.rows().map((row) => row.id)).toEqual([1, 3]);
      expect(h.notices).toContainEqual({ kind: 'rowsDeleted', count: 1 });
      h.engine.history.undo();
      expect(h.rows()[0]).toBe(first);
      expect(h.rows()[1]).toBe(second);
      expect(h.rows()[2]).toBe(third);
    });

    it('invalid inputs held by deleted rows return on undo and clear on redo', () => {
      const h = makeEngine(makeRows(3));
      h.engine.annotations.setInvalid(2, 'b', { rawText: '??', reason: 'parse' });
      h.engine.clickCell({ row: 1, col: 0 });
      h.engine.deleteSelectedRows();
      expect(h.engine.annotations.invalidCount()).toBe(0);
      h.engine.history.undo();
      expect(h.engine.annotations.invalidInput(2, 'b')).toMatchObject({
        rawText: '??',
        reason: 'parse',
      });
      h.engine.history.redo();
      expect(h.engine.annotations.invalidCount()).toBe(0);
    });
  });

  describe('vanished rows', () => {
    it('consumes an entry whose rows all vanished, with a notice and no effect', () => {
      const h = makeEngine(makeRows(2));
      h.engine.history.runCellWrites('cellEdit', [write(2, 'a', 'a2', 'X')]);
      h.externalChange([h.rows()[0]]);
      expect(h.engine.history.undo()).toBe(true);
      expect(h.notices).toContainEqual({ kind: 'undoSkippedMissingRows' });
      expect(h.engine.history.canRedo()).toBe(false); // consumed, not re-queued
      expect(h.rows()[0]['a']).toBe('a1');
    });

    it('applies the surviving subset and reports the skipped rows', () => {
      const h = makeEngine(makeRows(2));
      h.engine.history.runCellWrites('cellEdit', [
        write(1, 'a', 'a1', 'X'),
        write(2, 'a', 'a2', 'Y'),
      ]);
      h.externalChange([h.rows()[0]]);
      expect(h.engine.history.undo()).toBe(true);
      expect(h.rows()[0]['a']).toBe('a1');
      expect(h.notices).toContainEqual({ kind: 'undoApplied', opKind: 'cellEdit', skippedRows: 1 });
      expect(h.engine.history.canRedo()).toBe(true);
    });
  });

  describe('applyTransaction', () => {
    it('writes the values and captures the inverses as one entry', () => {
      const h = makeEngine(makeRows(2));
      h.engine.history.applyTransaction([
        { rowId: 1, key: 'a', value: 'X' },
        { rowId: 2, key: 'b', value: 'Y' },
      ]);
      expect(h.rows()[0]['a']).toBe('X');
      expect(h.rows()[1]['b']).toBe('Y');
      h.engine.history.undo();
      expect(h.rows()[0]['a']).toBe('a1');
      expect(h.rows()[1]['b']).toBe('b2');
      expect(h.engine.history.canUndo()).toBe(false);
    });

    it('skips a missing row with a warning; the rest still applies as one entry', () => {
      const h = makeEngine(makeRows(2));
      h.engine.history.applyTransaction([
        { rowId: 999, key: 'a', value: 'Z' },
        { rowId: 1, key: 'a', value: 'X' },
      ]);
      expect(h.warnings).toContainEqual({ kind: 'transactionRowMissing', rowId: 999 });
      expect(h.rows()[0]['a']).toBe('X');
      h.engine.history.undo();
      expect(h.rows()[0]['a']).toBe('a1');
      expect(h.engine.history.canUndo()).toBe(false);
    });
  });

  describe('undo/redo reveal', () => {
    // The harness host wires no onReveal, so the engine-side restoration —
    // active cell + re-selected range — is what these assertions pin.
    it('undo re-activates and re-selects the affected range', () => {
      const h = makeEngine(makeRows(3));
      h.engine.history.runCellWrites('cellEdit', [
        write(1, 'a', 'a1', 'X'),
        write(2, 'b', 'b2', 'Y'),
      ]);
      h.engine.clickCell({ row: 2, col: 2 }); // move activity away first
      h.engine.history.undo();
      expect(h.engine.nav.activeCell()).toEqual({ row: 0, col: 0 });
      expect(h.engine.selection.activeRange()).toEqual({
        anchor: { row: 0, col: 0 },
        focus: { row: 1, col: 1 },
        kind: 'cells',
      });
    });
  });

  describe('clear and snapshots', () => {
    it('clear empties both stacks', () => {
      const h = makeEngine(makeRows(1));
      const history = h.engine.history;
      history.runCellWrites('cellEdit', [write(1, 'a', 'a1', 'X')]);
      history.runCellWrites('cellEdit', [write(1, 'a', 'X', 'Y')]);
      history.undo();
      expect(history.canUndo()).toBe(true);
      expect(history.canRedo()).toBe(true);
      history.clear();
      expect(history.canUndo()).toBe(false);
      expect(history.canRedo()).toBe(false);
    });

    it('toSnapshot/restore round-trips the stacks', () => {
      const h = makeEngine(makeRows(1));
      const history = h.engine.history;
      history.runCellWrites('cellEdit', [write(1, 'a', 'a1', 'A')]);
      const snapshot = history.toSnapshot();
      history.runCellWrites('cellEdit', [write(1, 'a', 'A', 'B')]);
      history.undo();
      expect(history.canRedo()).toBe(true);
      history.restore(snapshot);
      expect(history.canUndo()).toBe(true);
      expect(history.canRedo()).toBe(false); // the snapshot held no redo
      history.undo(); // applies the snapshotted op's inverse
      expect(h.rows()[0]['a']).toBe('a1');
      expect(history.canRedo()).toBe(true);
    });
  });

  describe('sequence-token guard (§9.4 integration)', () => {
    it('structural undo bumps the affected cells\' tokens', () => {
      const h = makeEngine(makeRows(2));
      h.engine.clickCell({ row: 0, col: 0 });
      const created = h.engine.insertRows('below');
      const before = h.engine.annotations.currentToken(created[0].id, 'a');
      h.engine.history.undo(); // removes the created row — a structural change
      expect(h.engine.annotations.currentToken(created[0].id, 'a')).toBeGreaterThan(before);
    });
  });

  describe('trees', () => {
    it('undo of a delete restores rows hidden in a collapsed subtree', () => {
      const h = makeEngine(treeRows(), { tree: TREE });
      h.engine.setExpanded(1, false);
      h.engine.clickCell({ row: 0, col: 0 });
      h.engine.deleteSelectedRows(); // the subtree 1, 2, 3 — 2 and 3 are hidden
      expect(h.rows().map((row) => row.id)).toEqual([4]);
      h.engine.history.undo();
      expect(h.rows().map((row) => row.id)).toEqual([1, 2, 3, 4]);
      expect(h.engine.model.viewIndexOfRow(2)).not.toBe(-1); // visible again
    });

    it('undo reveal expands the collapsed ancestors of an affected hidden row', () => {
      const h = makeEngine(treeRows(), { tree: TREE });
      h.engine.history.runCellWrites('cellEdit', [write(2, 'a', 'child2', 'X')]);
      h.engine.setExpanded(1, false);
      expect(h.engine.model.viewIndexOfRow(2)).toBe(-1);
      h.engine.history.undo();
      expect(h.rows()[1]['a']).toBe('child2');
      expect(h.engine.model.expandedIds().has(1)).toBe(true);
      expect(h.engine.model.viewIndexOfRow(2)).toBe(1);
    });
  });

  describe('readonly binding', () => {
    it('every run* is a no-op and canUndo stays false', () => {
      const h = makeEngine(makeRows(2), { readonlyBinding: true });
      const history = h.engine.history;
      history.runCellWrites('cellEdit', [write(1, 'a', 'a1', 'X')]);
      expect(history.runRowInsert(0, 1)).toEqual([]);
      history.runRowDelete([1]);
      history.runRowMove([1], null, []);
      history.applyTransaction([{ rowId: 1, key: 'a', value: 'X' }]);
      expect(h.rows().map((row) => row.id)).toEqual([1, 2]);
      expect(h.rows()[0]['a']).toBe('a1');
      expect(history.canUndo()).toBe(false);
    });
  });
});
