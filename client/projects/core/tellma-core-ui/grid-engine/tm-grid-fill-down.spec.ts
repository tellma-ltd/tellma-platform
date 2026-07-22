// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { makeEngine, makeRows } from './tm-grid-testing.util';

describe('TmGridClipboard fillDown', () => {
  it('copies the top row of a multi-row range into the rows below, per column, as one undo entry', () => {
    const h = makeEngine(makeRows(3));
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.selection.extendActiveTo({ row: 2, col: 1 });
    h.engine.clipboard.fillDown();
    expect(h.rows()[1]).toMatchObject({ a: 'a1', b: 'b1', c: 'c2' });
    expect(h.rows()[2]).toMatchObject({ a: 'a1', b: 'b1', c: 'c3' });
    expect(h.rows()[0]).toMatchObject({ a: 'a1', b: 'b1' });
    expect(h.engine.history.undo()).toBe(true);
    expect(h.rows()[1]).toMatchObject({ a: 'a2', b: 'b2' });
    expect(h.rows()[2]).toMatchObject({ a: 'a3', b: 'b3' });
    expect(h.engine.history.canUndo()).toBe(false);
  });

  it('undo restores the WHOLE filled range, not one row short', () => {
    const h = makeEngine(makeRows(4));
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.selection.extendActiveTo({ row: 2, col: 0 }); // rows 0..2, col 0
    h.engine.clipboard.fillDown();
    expect(h.engine.history.undo()).toBe(true);
    // Fill-down never writes its source row, so the old written-cells heuristic
    // dropped the top cell on undo (restoring 1..2); the captured snapshot keeps
    // the original 0..2 range.
    expect(h.engine.selection.activeRect()).toEqual({ top: 0, bottom: 2, left: 0, right: 0 });
  });

  it('copies the cell above into a single-cell selection', () => {
    const h = makeEngine(makeRows(2));
    h.engine.clickCell({ row: 1, col: 0 });
    h.engine.clipboard.fillDown();
    expect(h.rows()[1]['a']).toBe('a1');
    expect(h.rows()[1]['b']).toBe('b2');
  });

  it('is a no-op for a single cell on the first row', () => {
    const h = makeEngine(makeRows(2));
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.clipboard.fillDown();
    expect(h.rows()[0]['a']).toBe('a1');
    expect(h.rows()[1]['a']).toBe('a2');
    expect(h.engine.history.canUndo()).toBe(false);
  });

  it('skips readonly cells', () => {
    const h = makeEngine(makeRows(3), {
      columns: [{ key: 'a' }, { key: 'b', cellReadonly: (row) => row.id === 2 }, { key: 'c' }],
    });
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.selection.extendActiveTo({ row: 2, col: 1 });
    h.engine.clipboard.fillDown();
    expect(h.rows()[1]['a']).toBe('a1');
    expect(h.rows()[1]['b']).toBe('b2'); // readonly cell untouched, in place
    expect(h.rows()[2]['a']).toBe('a1');
    expect(h.rows()[2]['b']).toBe('b1');
  });

  it('skips the placeholder row without materializing it', () => {
    const h = makeEngine(makeRows(2));
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.selection.extendActiveTo({ row: 2, col: 0 }); // row 2 is the placeholder
    h.engine.clipboard.fillDown();
    expect(h.rows()).toHaveLength(2);
    expect(h.rows()[1]['a']).toBe('a1');
  });

  it('copies the cleared model value of an invalid-input source, never its raw text', () => {
    const h = makeEngine(makeRows(2));
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
    h.engine.edit.commitText('BAD');
    expect(h.engine.annotations.invalidInput(1, 'a')).toBeDefined();
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.selection.extendActiveTo({ row: 1, col: 0 });
    h.engine.clipboard.fillDown();
    expect(h.rows()[1]['a']).toBeNull();
    expect(h.engine.annotations.invalidInput(2, 'a')).toBeUndefined();
    expect(h.engine.displayText({ row: 1, col: 0 })).toBe('');
    // The source keeps its own invalid input.
    expect(h.engine.annotations.invalidInput(1, 'a')).toMatchObject({
      rawText: 'BAD',
      reason: 'parse',
    });
  });

  it('is a no-op on a readonly grid', () => {
    const h = makeEngine(makeRows(2), { editable: false });
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.selection.extendActiveTo({ row: 1, col: 1 });
    h.engine.clipboard.fillDown();
    expect(h.rows()[1]).toMatchObject({ a: 'a2', b: 'b2' });
    expect(h.engine.history.canUndo()).toBe(false);
  });
});
