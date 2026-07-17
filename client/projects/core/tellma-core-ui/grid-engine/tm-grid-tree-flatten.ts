// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Internal tree derivation: adjacency-list rows → parent-resolved node maps
// → depth-first visible flattening. Not exported from the entry point; the
// data model is the only consumer.

import type { TmRowId } from '@tellma/core-ui/contracts';

/** One row's resolved tree placement (visible or not). */
export interface TmTreeNode<T> {
  readonly row: T;
  readonly id: TmRowId;
  readonly modelIndex: number;
  /** The EFFECTIVE parent id — orphans and cycle-broken rows become roots (null). */
  readonly parentId: TmRowId | null;
  readonly level: number;
  /** Loaded children, in model order. */
  readonly children: readonly TmRowId[];
  /** Whether the row can expand (loaded children, or a lazy-children marker). */
  readonly expandable: boolean;
}

/** The irregularities found while resolving the hierarchy. */
export interface TmTreeIrregularity {
  readonly kind: 'duplicateRowId' | 'orphanParent' | 'parentCycle';
  readonly rowId: TmRowId;
}

/** The resolved structure of one rows array. */
export interface TmTreeStructure<T> {
  /** Every row's node, keyed by id (first occurrence wins on duplicate ids). */
  readonly nodes: ReadonlyMap<TmRowId, TmTreeNode<T>>;
  /** Root ids in model order. */
  readonly roots: readonly TmRowId[];
  /** Irregularities, at most one per (kind, rowId) pair. */
  readonly irregularities: readonly TmTreeIrregularity[];
}

/**
 * Resolves the adjacency list into parent-resolved nodes: a `null`/missing
 * parent makes a root; a parent id that doesn't resolve makes the row a
 * root (orphan); a parent cycle is broken by turning the cycle member with
 * the smallest model index into a root (deterministic). Duplicate ids keep
 * the first occurrence in the maps; later occurrences are reported and
 * excluded from the structure.
 */
export function tmResolveTree<T>(
  rows: readonly T[],
  rowIdOf: (row: T) => TmRowId,
  parentIdOf: ((row: T) => TmRowId | null | undefined) | null,
  hasChildrenOf: ((row: T) => boolean) | null,
): TmTreeStructure<T> {
  const irregularities: TmTreeIrregularity[] = [];
  const modelIndexById = new Map<TmRowId, number>();
  const rowById = new Map<TmRowId, T>();
  const orderedIds: TmRowId[] = [];
  for (let i = 0; i < rows.length; i++) {
    const id = rowIdOf(rows[i]);
    if (rowById.has(id)) {
      irregularities.push({ kind: 'duplicateRowId', rowId: id });
      continue;
    }
    rowById.set(id, rows[i]);
    modelIndexById.set(id, i);
    orderedIds.push(id);
  }

  // Raw parent resolution: orphans and self-parents become roots up front.
  const parentById = new Map<TmRowId, TmRowId | null>();
  for (const id of orderedIds) {
    const raw = parentIdOf ? (parentIdOf(rowById.get(id)!) ?? null) : null;
    if (raw === null) {
      parentById.set(id, null);
    } else if (raw === id || !rowById.has(raw)) {
      irregularities.push({ kind: raw === id ? 'parentCycle' : 'orphanParent', rowId: id });
      parentById.set(id, null);
    } else {
      parentById.set(id, raw);
    }
  }

  // Multi-node cycle break: walk each unresolved parent chain; a chain that
  // re-enters itself is a cycle — its member with the smallest model index
  // becomes a root, which resolves every chain that fed into the cycle.
  const state = new Map<TmRowId, 1 | 2>(); // 1 = on the current walk, 2 = known to reach a root
  for (const startId of orderedIds) {
    if (state.get(startId) === 2) {
      continue;
    }
    const path: TmRowId[] = [];
    let current: TmRowId | null = startId;
    while (current !== null && state.get(current) !== 2) {
      if (state.get(current) === 1) {
        // Found a cycle: it is the tail of `path` from `current` onward.
        const cycleStart = path.indexOf(current);
        const cycle = path.slice(cycleStart);
        let breakId = cycle[0];
        for (const member of cycle) {
          if (modelIndexById.get(member)! < modelIndexById.get(breakId)!) {
            breakId = member;
          }
        }
        irregularities.push({ kind: 'parentCycle', rowId: breakId });
        parentById.set(breakId, null);
        break;
      }
      state.set(current, 1);
      path.push(current);
      current = parentById.get(current)!;
    }
    for (const visited of path) {
      state.set(visited, 2);
    }
  }

  // Children map + levels + nodes (model order throughout).
  const childrenById = new Map<TmRowId, TmRowId[]>();
  const roots: TmRowId[] = [];
  for (const id of orderedIds) {
    const parent = parentById.get(id)!;
    if (parent === null) {
      roots.push(id);
    } else {
      const siblings = childrenById.get(parent);
      if (siblings) {
        siblings.push(id);
      } else {
        childrenById.set(parent, [id]);
      }
    }
  }
  const levelById = new Map<TmRowId, number>();
  const resolveLevel = (id: TmRowId): number => {
    const known = levelById.get(id);
    if (known !== undefined) {
      return known;
    }
    const parent = parentById.get(id)!;
    const level = parent === null ? 0 : resolveLevel(parent) + 1;
    levelById.set(id, level);
    return level;
  };

  const nodes = new Map<TmRowId, TmTreeNode<T>>();
  for (const id of orderedIds) {
    const row = rowById.get(id)!;
    const children = childrenById.get(id) ?? [];
    nodes.set(id, {
      row,
      id,
      modelIndex: modelIndexById.get(id)!,
      parentId: parentById.get(id)!,
      level: resolveLevel(id),
      children,
      expandable: children.length > 0 || (hasChildrenOf ? hasChildrenOf(row) : false),
    });
  }

  return { nodes, roots, irregularities };
}

/**
 * Flattens the resolved structure to the visible-row id sequence: roots in
 * model order, each followed depth-first by its children while every
 * ancestor is expanded.
 */
export function tmFlattenVisible<T>(
  structure: TmTreeStructure<T>,
  expandedIds: ReadonlySet<TmRowId>,
): readonly TmRowId[] {
  const visible: TmRowId[] = [];
  const emit = (id: TmRowId): void => {
    visible.push(id);
    const node = structure.nodes.get(id)!;
    if (node.children.length > 0 && expandedIds.has(id)) {
      for (const child of node.children) {
        emit(child);
      }
    }
  };
  for (const root of structure.roots) {
    emit(root);
  }
  return visible;
}
