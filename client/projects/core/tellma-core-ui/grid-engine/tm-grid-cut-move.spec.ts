// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmRowId } from '@tellma/core-ui/contracts';

import type { TmGridCopyPayload } from './tm-grid-clipboard';
import type { TmGridPasteSource } from './tm-grid-paste-source';
import { makeEngine, makeRows, type TestHarness } from './tm-grid-testing.util';

function requirePayload(payload: TmGridCopyPayload | null): TmGridCopyPayload {
  if (payload === null) {
    throw new Error('expected a copy payload');
  }
  return payload;
}

/** Reduces a copy payload to the paste source a same-session paste would use. */
function sourceOf(payload: TmGridCopyPayload): TmGridPasteSource {
  return {
    matrix: payload.matrix,
    meta: payload.meta,
    rawValues: payload.rawValues,
    rowIds: payload.rowIds,
  };
}

/**
 * Roots 1 and 3, child 2 under 1 and child 4 under 3 — fully expanded, so
 * the view order is 1, 2, 3, 4.
 */
function makeTreeHarness(): TestHarness {
  return makeEngine(
    [
      { id: 1, a: 'a1', parentId: null },
      { id: 2, a: 'a2', parentId: 1 },
      { id: 3, a: 'a3', parentId: null },
      { id: 4, a: 'a4', parentId: 3 },
    ],
    {
      columns: [{ key: 'a' }],
      tree: {
        parentId: (row) => row['parentId'] as TmRowId | null,
        parentIdKey: 'parentId',
      },
    },
  );
}

describe('TmGridClipboard cut', () => {
  it('arms the pending cut with the cut shape and the payload fingerprint', () => {
    const h = makeEngine(makeRows(3));
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.selection.extendActiveTo({ row: 1, col: 1 });
    const payload = requirePayload(h.engine.clipboard.cut((p) => `fp-${p.cellCount}`));
    expect(payload.matrix).toEqual([
      ['a1', 'b1'],
      ['a2', 'b2'],
    ]);
    expect(h.engine.clipboard.pendingCut()).toEqual({
      rowIds: [1, 2],
      columnIds: ['a', 'b'],
      isFullRows: false,
      fingerprint: 'fp-4',
    });
  });

  it('Esc disarms the pending cut and reports it handled', () => {
    const h = makeEngine(makeRows(2));
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.clipboard.cut(() => 'fp1');
    expect(h.engine.clipboard.pendingCut()).not.toBeNull();
    expect(h.engine.escape()).toBe(true);
    expect(h.engine.clipboard.pendingCut()).toBeNull();
    expect(h.engine.escape()).toBe(false);
  });

  it('clearSelection disarms the pending cut', () => {
    const h = makeEngine(makeRows(2));
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.clipboard.cut(() => 'fp1');
    h.engine.clearSelection();
    expect(h.engine.clipboard.pendingCut()).toBeNull();
  });
});

describe('TmGridClipboard cut-paste move', () => {
  it('moves a cell rectangle — clears the source and writes the target in one undo entry', () => {
    const h = makeEngine(makeRows(4));
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.selection.extendActiveTo({ row: 1, col: 1 });
    const payload = requirePayload(h.engine.clipboard.cut(() => 'fp'));
    h.engine.clickCell({ row: 2, col: 0 });
    h.engine.clipboard.paste(sourceOf(payload), 'fp');
    // Target written.
    expect(h.rows()[2]['a']).toBe('a1');
    expect(h.rows()[2]['b']).toBe('b1');
    expect(h.rows()[3]['a']).toBe('a2');
    expect(h.rows()[3]['b']).toBe('b2');
    // Source cleared; untouched columns keep their values.
    expect(h.rows()[0]['a']).toBeNull();
    expect(h.rows()[0]['b']).toBeNull();
    expect(h.rows()[1]['a']).toBeNull();
    expect(h.rows()[1]['b']).toBeNull();
    expect(h.rows()[0]['c']).toBe('c1');
    expect(h.engine.clipboard.pendingCut()).toBeNull();
    // One undo restores source and target alike.
    expect(h.engine.history.undo()).toBe(true);
    expect(h.rows()[0]['a']).toBe('a1');
    expect(h.rows()[1]['b']).toBe('b2');
    expect(h.rows()[2]['a']).toBe('a3');
    expect(h.rows()[3]['b']).toBe('b4');
    expect(h.engine.history.canUndo()).toBe(false);
  });

  it('handles overlapping source and target when shifting a block by one column', () => {
    const h = makeEngine(makeRows(2));
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.selection.extendActiveTo({ row: 1, col: 1 });
    const payload = requirePayload(h.engine.clipboard.cut(() => 'fp'));
    h.engine.clickCell({ row: 0, col: 1 });
    h.engine.clipboard.paste(sourceOf(payload), 'fp');
    expect(h.rows()[0]['a']).toBeNull();
    expect(h.rows()[0]['b']).toBe('a1');
    expect(h.rows()[0]['c']).toBe('b1');
    expect(h.rows()[1]['a']).toBeNull();
    expect(h.rows()[1]['b']).toBe('a2');
    expect(h.rows()[1]['c']).toBe('b2');
    expect(h.engine.history.undo()).toBe(true);
    expect(h.rows()[0]).toMatchObject({ a: 'a1', b: 'b1', c: 'c1' });
    expect(h.rows()[1]).toMatchObject({ a: 'a2', b: 'b2', c: 'c2' });
  });

  it('pastes plainly and leaves the source untouched when the fingerprint mismatches', () => {
    const h = makeEngine(makeRows(3));
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.selection.extendActiveTo({ row: 0, col: 1 });
    const payload = requirePayload(h.engine.clipboard.cut(() => 'fp'));
    h.engine.clickCell({ row: 2, col: 0 });
    h.engine.clipboard.paste(sourceOf(payload), 'other');
    expect(h.rows()[2]['a']).toBe('a1');
    expect(h.rows()[2]['b']).toBe('b1');
    expect(h.rows()[0]['a']).toBe('a1');
    expect(h.rows()[0]['b']).toBe('b1');
    expect(h.engine.clipboard.pendingCut()).toBeNull();
  });

  it('re-splices a full-row cut before the paste target row as one undo entry', () => {
    const h = makeEngine(makeRows(4));
    h.engine.selection.selectRows(0, 0, false);
    const payload = requirePayload(h.engine.clipboard.cut(() => 'fp'));
    expect(payload.rowIds).toEqual([1]);
    h.engine.clickCell({ row: 2, col: 0 }); // row id 3
    h.engine.clipboard.paste(sourceOf(payload), 'fp');
    expect(h.rows().map((row) => row.id)).toEqual([2, 1, 3, 4]);
    expect(h.rows()[1]['a']).toBe('a1'); // moved, not rewritten
    expect(h.notices).toContainEqual({ kind: 'rowsMoved', count: 1 });
    expect(h.engine.history.undo()).toBe(true);
    expect(h.rows().map((row) => row.id)).toEqual([1, 2, 3, 4]);
    expect(h.engine.history.canUndo()).toBe(false);
  });

  it('treats a move onto its own selection as a no-op', () => {
    const h = makeEngine(makeRows(3));
    h.engine.selection.selectRows(0, 1, false);
    const payload = requirePayload(h.engine.clipboard.cut(() => 'fp'));
    h.engine.clickCell({ row: 1, col: 0 }); // one of the cut rows
    h.engine.clipboard.paste(sourceOf(payload), 'fp');
    expect(h.rows().map((row) => row.id)).toEqual([1, 2, 3]);
    expect(h.rows()[0]['a']).toBe('a1');
    expect(h.rows()[1]['a']).toBe('a2');
    expect(h.engine.history.canUndo()).toBe(false);
    expect(h.engine.clipboard.pendingCut()).toBeNull();
  });

  it('moves a tree row with its whole subtree, re-parenting to the target row parent', () => {
    const h = makeTreeHarness();
    h.engine.selection.selectRows(0, 0, false); // view row 0 = root 1
    const payload = requirePayload(h.engine.clipboard.cut(() => 'fp'));
    h.engine.clickCell({ row: 3, col: 0 }); // view row 3 = row 4 (child of 3)
    h.engine.clipboard.paste(sourceOf(payload), 'fp');
    expect(h.rows().map((row) => row.id)).toEqual([3, 1, 2, 4]);
    expect(h.rows()[1]['parentId']).toBe(3); // row 1 re-parented to the target's parent
    expect(h.rows()[2]['parentId']).toBe(1); // its child travelled with it, untouched
    expect(h.notices).toContainEqual({ kind: 'rowsMoved', count: 1 });
    expect(h.engine.history.undo()).toBe(true);
    expect(h.rows().map((row) => row.id)).toEqual([1, 2, 3, 4]);
    expect(h.rows()[0]['parentId']).toBeNull();
  });

  it('rejects a move into the row own descendant with a notice and no change', () => {
    const h = makeTreeHarness();
    h.engine.selection.selectRows(0, 0, false); // root 1
    const payload = requirePayload(h.engine.clipboard.cut(() => 'fp'));
    h.engine.clickCell({ row: 1, col: 0 }); // row 2, a descendant of row 1
    h.engine.clipboard.paste(sourceOf(payload), 'fp');
    expect(h.notices).toContainEqual({ kind: 'moveIntoDescendantRejected' });
    expect(h.rows().map((row) => row.id)).toEqual([1, 2, 3, 4]);
    expect(h.rows()[0]['parentId']).toBeNull();
    expect(h.engine.history.canUndo()).toBe(false);
  });

  it('reconcileCut shrinks the pending cut to surviving rows and disarms when none survive', () => {
    const h = makeEngine(makeRows(3));
    h.engine.selection.selectRows(0, 1, false);
    h.engine.clipboard.cut(() => 'fp');
    h.externalChange(h.rows().filter((row) => row.id !== 1));
    expect(h.engine.clipboard.pendingCut()?.rowIds).toEqual([2]);
    h.externalChange(h.rows().filter((row) => row.id !== 2));
    expect(h.engine.clipboard.pendingCut()).toBeNull();
  });
});
