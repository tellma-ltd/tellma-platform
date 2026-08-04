// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmRowId } from '@tellma/core-ui/contracts';

import { makeEngine, type TestHarness, type TestRow } from './tm-grid-testing.util';
import type { TmGridRowView, TmGridTreeOptions } from './tm-grid-types';

const parentIdOf = (row: TestRow) => row['parentId'] as TmRowId | null | undefined;

function treeRow(id: TmRowId, parentId: TmRowId | null = null): TestRow {
  return { id, parentId, a: `a-${String(id)}` };
}

/**
 * The standard fixture:
 *
 *     1 ── 2 ── 3
 *       └─ 4
 *     5 ── 6
 *
 * The model order interleaves the branches ([1, 5, 2, 4, 3, 6]) so the
 * flatten assertions prove DFS placement rather than array order.
 */
function standardRows(): TestRow[] {
  return [treeRow(1), treeRow(5), treeRow(2, 1), treeRow(4, 1), treeRow(3, 2), treeRow(6, 5)];
}

function makeTree(
  rows: readonly TestRow[] = standardRows(),
  tree: Omit<TmGridTreeOptions<TestRow>, 'parentId'> = {},
): TestHarness {
  return makeEngine(rows, { tree: { parentId: parentIdOf, ...tree } });
}

function viewIds(harness: TestHarness): TmRowId[] {
  return harness.engine.model.viewRows().map((view) => view.id);
}

function viewById(harness: TestHarness, id: TmRowId): TmGridRowView<TestRow> {
  const view = harness.engine.model.viewRows().find((candidate) => candidate.id === id);
  if (view === undefined) {
    throw new Error(`row ${String(id)} is not visible`);
  }
  return view;
}

describe('TmGridDataModel tree flattening', () => {
  it('flattens depth-first: roots in model order, children after their parents in model order', () => {
    const harness = makeTree();
    expect(viewIds(harness)).toEqual([1, 2, 3, 4, 5, 6]);
    expect(viewById(harness, 3).parentId).toBe(2);
    expect(viewById(harness, 4).parentId).toBe(1);
    expect(viewById(harness, 1).parentId).toBeNull();
  });

  it('assigns levels from the root down', () => {
    const harness = makeTree();
    expect(harness.engine.model.viewRows().map((view) => view.level)).toEqual([0, 1, 2, 1, 0, 1]);
  });

  it('flags expandable and expanded per row', () => {
    const harness = makeTree();
    for (const id of [1, 2, 5]) {
      expect(viewById(harness, id).expandable).toBe(true);
      expect(viewById(harness, id).expanded).toBe(true);
    }
    for (const id of [3, 4, 6]) {
      expect(viewById(harness, id).expandable).toBe(false);
      expect(viewById(harness, id).expanded).toBe(false);
    }
  });

  it('seeds fully expanded by default', () => {
    const harness = makeTree();
    expect(harness.engine.model.dataRowCount()).toBe(6);
    expect(harness.engine.model.expandedIds()).toEqual(new Set([1, 2, 5]));
  });

  it('seeds all collapsed at defaultExpandedDepth 0 — only roots visible', () => {
    const harness = makeTree(standardRows(), { defaultExpandedDepth: 0 });
    expect(viewIds(harness)).toEqual([1, 5]);
    expect(viewById(harness, 1).expandable).toBe(true);
    expect(viewById(harness, 1).expanded).toBe(false);
  });

  it('seeds roots expanded only at defaultExpandedDepth 1', () => {
    const harness = makeTree(standardRows(), { defaultExpandedDepth: 1 });
    expect(viewIds(harness)).toEqual([1, 2, 4, 5, 6]);
    expect(viewById(harness, 2).expandable).toBe(true);
    expect(viewById(harness, 2).expanded).toBe(false);
    expect(harness.engine.model.viewIndexOfRow(3)).toBe(-1);
  });
});

describe('TmGridDataModel expansion', () => {
  it('expands and collapses through setExpanded', () => {
    const harness = makeTree(standardRows(), { defaultExpandedDepth: 1 });
    expect(harness.engine.model.setExpanded(2, true)).toBe(true);
    expect(viewIds(harness)).toEqual([1, 2, 3, 4, 5, 6]);
    expect(harness.engine.model.setExpanded(2, false)).toBe(true);
    expect(viewIds(harness)).toEqual([1, 2, 4, 5, 6]);
  });

  it('returns false from setExpanded for non-expandable and absent rows', () => {
    const harness = makeTree();
    expect(harness.engine.model.setExpanded(4, true)).toBe(false); // a leaf
    expect(harness.engine.model.setExpanded('absent', true)).toBe(false);
    expect(viewIds(harness)).toEqual([1, 2, 3, 4, 5, 6]);
  });

  it('hides the whole subtree when an ancestor collapses, and restores it on re-expand', () => {
    const harness = makeTree();
    harness.engine.model.setExpanded(1, false);
    expect(viewIds(harness)).toEqual([1, 5, 6]);
    expect(harness.engine.model.viewIndexOfRow(2)).toBe(-1);
    expect(harness.engine.model.viewIndexOfRow(3)).toBe(-1);
    expect(harness.engine.model.viewIndexOfRow(4)).toBe(-1);
    // Row 2 stayed in the expansion set, so re-expanding 1 restores the deep rows too.
    harness.engine.model.setExpanded(1, true);
    expect(viewIds(harness)).toEqual([1, 2, 3, 4, 5, 6]);
  });

  it('makes a deep row visible via expandAncestorsOf', () => {
    const harness = makeTree(standardRows(), { defaultExpandedDepth: 0 });
    harness.engine.model.expandAncestorsOf(3);
    expect(viewIds(harness)).toEqual([1, 2, 3, 4, 5]);
    expect(harness.engine.model.viewIndexOfRow(3)).toBe(2);
    expect(harness.engine.model.viewIndexOfRow(6)).toBe(-1); // the other branch stays collapsed
  });

  it('restores a persisted expansion set, pruning unknown and non-expandable ids', () => {
    const harness = makeTree();
    harness.engine.model.restoreExpansion(new Set<TmRowId>([1, 3, 'ghost']));
    expect(harness.engine.model.expandedIds()).toEqual(new Set([1]));
    expect(viewIds(harness)).toEqual([1, 2, 4, 5]);
  });

  it('keeps hidden rows reachable by id while viewIndexOfRow reports -1', () => {
    const harness = makeTree();
    harness.engine.model.setExpanded(1, false);
    expect(harness.engine.model.viewIndexOfRow(3)).toBe(-1);
    expect(harness.engine.model.rowById(3)?.id).toBe(3);
    expect(harness.engine.model.modelIndexOfRow(3)).toBe(4);
  });
});

describe('TmGridDataModel tree queries', () => {
  it('lists ancestors nearest-first', () => {
    const harness = makeTree();
    expect(harness.engine.model.ancestorsOf(3)).toEqual([2, 1]);
    expect(harness.engine.model.ancestorsOf(1)).toEqual([]);
    expect(harness.engine.model.ancestorsOf('absent')).toEqual([]);
  });

  it('answers isDescendantOf', () => {
    const harness = makeTree();
    expect(harness.engine.model.isDescendantOf(3, 1)).toBe(true);
    expect(harness.engine.model.isDescendantOf(3, 2)).toBe(true);
    expect(harness.engine.model.isDescendantOf(3, 5)).toBe(false);
    expect(harness.engine.model.isDescendantOf(1, 3)).toBe(false);
  });

  it('lists the subtree self-first in depth-first order, collapsed or not', () => {
    const harness = makeTree();
    expect(harness.engine.model.subtreeRowIds(1)).toEqual([1, 2, 3, 4]);
    expect(harness.engine.model.subtreeRowIds(3)).toEqual([3]);
    expect(harness.engine.model.subtreeRowIds('absent')).toEqual([]);
    harness.engine.model.setExpanded(1, false);
    expect(harness.engine.model.subtreeRowIds(1)).toEqual([1, 2, 3, 4]);
  });
});

describe('TmGridDataModel tree irregularities', () => {
  it('turns an orphan (unresolvable parent id) into a root and warns once', () => {
    const harness = makeTree([treeRow(1), treeRow(2, 'missing')]);
    expect(viewIds(harness)).toEqual([1, 2]);
    expect(viewById(harness, 2).level).toBe(0);
    expect(viewById(harness, 2).parentId).toBeNull();
    const orphanWarnings = () =>
      harness.warnings.filter((warning) => warning.kind === 'orphanParent');
    expect(orphanWarnings()).toEqual([{ kind: 'orphanParent', rowId: 2 }]);
    harness.externalChange(harness.rows().map((row) => ({ ...row })));
    harness.engine.model.viewRows();
    expect(orphanWarnings().length).toBe(1);
  });

  it('turns a self-parented row into a root and warns parentCycle once', () => {
    const harness = makeTree([treeRow(1, 1), treeRow(2)]);
    expect(viewIds(harness)).toEqual([1, 2]);
    expect(viewById(harness, 1).level).toBe(0);
    expect(viewById(harness, 1).parentId).toBeNull();
    expect(harness.warnings.filter((warning) => warning.kind === 'parentCycle')).toEqual([
      { kind: 'parentCycle', rowId: 1 },
    ]);
  });

  it('breaks a multi-node cycle at the member with the smallest model index', () => {
    // A→B→C→A, in model order [B, C, A]: B has the smallest model index, so
    // B becomes the root and the rest of the cycle hangs off it as a chain.
    const harness = makeTree([treeRow('B', 'C'), treeRow('C', 'A'), treeRow('A', 'B')]);
    expect(viewIds(harness)).toEqual(['B', 'A', 'C']);
    expect(viewById(harness, 'B').parentId).toBeNull();
    expect(viewById(harness, 'A').parentId).toBe('B'); // the other members keep their parents
    expect(viewById(harness, 'C').parentId).toBe('A');
    expect(harness.engine.model.viewRows().map((view) => view.level)).toEqual([0, 1, 2]);
    expect(harness.warnings.filter((warning) => warning.kind === 'parentCycle')).toEqual([
      { kind: 'parentCycle', rowId: 'B' },
    ]);
    expect(harness.warnings.filter((warning) => warning.kind === 'orphanParent')).toEqual([]);
  });

  it('resolves rows feeding into a cycle without extra warnings', () => {
    const harness = makeTree([
      treeRow('B', 'C'),
      treeRow('C', 'A'),
      treeRow('A', 'B'),
      treeRow('T', 'A'), // the tail hangs off cycle member A
    ]);
    expect(viewIds(harness)).toEqual(['B', 'A', 'C', 'T']);
    expect(viewById(harness, 'T').parentId).toBe('A');
    expect(viewById(harness, 'T').level).toBe(2);
    expect(harness.warnings).toEqual([{ kind: 'parentCycle', rowId: 'B' }]);
  });
});

describe('TmGridDataModel lazy children and late rows', () => {
  it('makes a childless row expandable via the hasChildren marker', () => {
    const harness = makeEngine([treeRow(10), treeRow(11)], {
      tree: { parentId: parentIdOf, hasChildren: (row) => row.id === 10 },
    });
    expect(viewById(harness, 10).expandable).toBe(true);
    expect(viewById(harness, 10).expanded).toBe(true); // the default seed covers it
    expect(viewById(harness, 11).expandable).toBe(false);
    expect(harness.engine.model.setExpanded(10, false)).toBe(true);
    expect(viewById(harness, 10).expanded).toBe(false);
    expect(harness.engine.model.setExpanded(11, true)).toBe(false);
  });

  it('starts later-arriving rows collapsed', () => {
    const harness = makeTree([treeRow(1), treeRow(2, 1)]);
    expect(viewIds(harness)).toEqual([1, 2]);
    harness.externalChange([treeRow(1), treeRow(2, 1), treeRow(7), treeRow(8, 7)]);
    expect(viewIds(harness)).toEqual([1, 2, 7]);
    expect(viewById(harness, 7).expandable).toBe(true);
    expect(viewById(harness, 7).expanded).toBe(false);
    expect(viewById(harness, 1).expanded).toBe(true); // the seeded expansion survives
    expect(harness.engine.model.viewIndexOfRow(8)).toBe(-1);
    expect(harness.engine.model.rowById(8)?.id).toBe(8);
    expect(harness.engine.model.modelIndexOfRow(8)).toBe(3);
  });
});
