// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, signal, untracked, type Signal } from '@angular/core';

import type { SignalLike, TmRowId } from '@tellma/core-ui/contracts';

import type { TmGridEngineHost } from './tm-grid-host';
import { tmFlattenVisible, tmResolveTree, type TmTreeStructure } from './tm-grid-tree-flatten';
import type {
  TmGridEngineColumn,
  TmGridRowView,
  TmGridTreeOptions,
  TmRowCol,
} from './tm-grid-types';

/** Construction inputs of {@link TmGridDataModel}. */
export interface TmGridDataModelOptions<T = unknown> {
  /** The bound rows, in model order. */
  readonly rows: SignalLike<readonly T[]>;
  /** Reads a row's stable identity. */
  rowId(row: T): TmRowId;
  /** The data columns, in display order. */
  readonly columns: SignalLike<ReadonlyArray<TmGridEngineColumn<T>>>;
  /** Whether the grid is editable. */
  readonly editable: SignalLike<boolean>;
  /** Whether new rows can be created — with `editable`, drives the placeholder row. */
  readonly canAddRows: SignalLike<boolean>;
  /** Tree configuration; absent for the flat grid. */
  readonly tree?: TmGridTreeOptions<T>;
  /** Irregularity reporting (dev-mode warnings in the component layer). */
  readonly host?: Pick<TmGridEngineHost<T>, 'onWarn'>;
}

/**
 * A snapshot of the visible-row order, captured before a data change so
 * identity-keyed consumers (selection, the active cell) can remap
 * themselves afterwards.
 */
export interface TmGridOrderSnapshot {
  /** The visible row ids, in view order. */
  readonly visibleIds: readonly TmRowId[];
  /** View index by row id. */
  readonly viewIndexById: ReadonlyMap<TmRowId, number>;
}

/**
 * The engine's data model: maps the consumer's flat row array (plus, for
 * trees, the derived hierarchy and expansion state) to the visible-row
 * sequence everything else operates on, and answers per-cell questions
 * (value, text, editability) in view-space coordinates.
 *
 * The visible sequence excludes the new-row placeholder; view indices that
 * equal `dataRowCount` while {@link hasPlaceholder} is on address the
 * placeholder row.
 */
export class TmGridDataModel<T = unknown> {
  private readonly options: TmGridDataModelOptions<T>;
  /** Irregularities already reported, so recomputes don't repeat them. */
  private readonly warned = new Set<string>();
  private readonly expandedSet = signal<ReadonlySet<TmRowId>>(new Set());

  /** Whether this model derives a tree (affects roles, indentation, motion). */
  readonly isTree: boolean;

  private readonly structure: Signal<TmTreeStructure<T>>;
  private readonly visibleIds: Signal<readonly TmRowId[]>;
  private readonly viewIndexById: Signal<ReadonlyMap<TmRowId, number>>;

  /** The visible data rows, in view order (placeholder excluded). */
  readonly viewRows: Signal<ReadonlyArray<TmGridRowView<T>>>;
  /** Count of visible data rows (placeholder excluded). */
  readonly dataRowCount: Signal<number>;
  /** Count of ALL model rows, hidden ones included (append positions). */
  readonly modelRowCount: Signal<number>;
  /** Whether the new-row placeholder is present (editable + row factory). */
  readonly hasPlaceholder: Signal<boolean>;
  /** Count of visible rows including the placeholder. */
  readonly viewRowCount: Signal<number>;
  /** The placeholder's view index, or -1 while absent. */
  readonly placeholderIndex: Signal<number>;
  /** Count of data columns. */
  readonly columnCount: Signal<number>;
  /** The expanded row ids (trees; empty for flat grids). */
  readonly expandedIds: Signal<ReadonlySet<TmRowId>>;

  constructor(options: TmGridDataModelOptions<T>) {
    this.options = options;
    this.isTree = options.tree !== undefined;
    const tree = options.tree ?? null;

    this.structure = computed(() => {
      const resolved = tmResolveTree(
        options.rows(),
        (row) => options.rowId(row),
        tree ? (row) => tree.parentId(row) : null,
        tree?.hasChildren ? (row) => tree.hasChildren!(row) : null,
      );
      // Reporting from a derivation is deliberate: the engine has no effect
      // scheduler, and the deduplication set keeps each irregularity to one
      // report per model instance.
      untracked(() => {
        for (const irregularity of resolved.irregularities) {
          const key = `${irregularity.kind}:${String(irregularity.rowId)}`;
          if (!this.warned.has(key)) {
            this.warned.add(key);
            options.host?.onWarn?.({ kind: irregularity.kind, rowId: irregularity.rowId });
          }
        }
      });
      return resolved;
    });

    this.visibleIds = computed(() =>
      this.isTree
        ? tmFlattenVisible(this.structure(), this.expandedSet())
        : [...this.structure().roots],
    );

    this.viewRows = computed(() => {
      const structure = this.structure();
      const expanded = this.expandedSet();
      return this.visibleIds().map((id) => {
        const node = structure.nodes.get(id)!;
        return {
          row: node.row,
          id: node.id,
          modelIndex: node.modelIndex,
          level: node.level,
          parentId: node.parentId,
          expandable: node.expandable,
          expanded: node.expandable && expanded.has(id),
        };
      });
    });

    this.viewIndexById = computed(() => {
      const map = new Map<TmRowId, number>();
      const ids = this.visibleIds();
      for (let i = 0; i < ids.length; i++) {
        map.set(ids[i], i);
      }
      return map;
    });

    this.dataRowCount = computed(() => this.visibleIds().length);
    this.modelRowCount = computed(() => options.rows().length);
    this.hasPlaceholder = computed(() => options.editable() && options.canAddRows());
    this.viewRowCount = computed(() => this.dataRowCount() + (this.hasPlaceholder() ? 1 : 0));
    this.placeholderIndex = computed(() => (this.hasPlaceholder() ? this.dataRowCount() : -1));
    this.columnCount = computed(() => options.columns().length);
    this.expandedIds = this.expandedSet.asReadonly();
  }

  /** The row at a view index, or `null` for the placeholder row / out of range. */
  rowAt(viewIndex: number): TmGridRowView<T> | null {
    return this.viewRows()[viewIndex] ?? null;
  }

  /** Whether the view index addresses the new-row placeholder. */
  isPlaceholder(viewIndex: number): boolean {
    const index = this.placeholderIndex();
    return index !== -1 && viewIndex === index;
  }

  /** The row's view index, or -1 while hidden in a collapsed subtree or absent. */
  viewIndexOfRow(rowId: TmRowId): number {
    return this.viewIndexById().get(rowId) ?? -1;
  }

  /** The row's model-array index, or -1 when absent. */
  modelIndexOfRow(rowId: TmRowId): number {
    return this.structure().nodes.get(rowId)?.modelIndex ?? -1;
  }

  /** The row object by id, hidden rows included; `undefined` when absent. */
  rowById(rowId: TmRowId): T | undefined {
    return this.structure().nodes.get(rowId)?.row;
  }

  /** The column at a data-column index. */
  columnAt(col: number): TmGridEngineColumn<T> {
    return this.options.columns()[col];
  }

  /** The data-column index of a column id, or -1 when absent. */
  columnIndexOf(columnId: string): number {
    return this.options.columns().findIndex((column) => column.id === columnId);
  }

  /** The cell's model value (`null` on the placeholder row). */
  cellValue(cell: TmRowCol): unknown {
    const view = this.rowAt(cell.row);
    if (view === null) {
      return null;
    }
    return this.columnAt(cell.col)?.getValue(view.row) ?? null;
  }

  /** The cell's text representation (empty on the placeholder row). */
  cellText(cell: TmRowCol): string {
    const view = this.rowAt(cell.row);
    const column = this.columnAt(cell.col);
    if (view === null || column === undefined) {
      return '';
    }
    return column.getText(view.row);
  }

  /**
   * Whether the cell accepts writes right now: the grid is editable, the
   * column has an editing path, and — on a data row — the per-cell readonly
   * oracle allows it. Placeholder cells are editable whenever their column
   * is (no field state exists before materialization).
   */
  isCellEditable(cell: TmRowCol): boolean {
    if (!this.options.editable()) {
      return false;
    }
    const column = this.columnAt(cell.col);
    if (column === undefined || !column.editable) {
      return false;
    }
    if (this.isPlaceholder(cell.row)) {
      return true;
    }
    const view = this.rowAt(cell.row);
    return view !== null && !column.isCellReadonly(view.row);
  }

  // ---- Tree operations (no-ops / empty results on flat grids) ----

  /**
   * Seeds the expansion set from the configured default expanded depth
   * (fully expanded when unset). Called once per content bind; rows that
   * arrive later start collapsed.
   */
  seedExpansion(): void {
    if (!this.isTree) {
      return;
    }
    const depth = this.options.tree?.defaultExpandedDepth ?? Number.POSITIVE_INFINITY;
    const expanded = new Set<TmRowId>();
    for (const node of untracked(() => this.structure()).nodes.values()) {
      if (node.expandable && node.level < depth) {
        expanded.add(node.id);
      }
    }
    this.expandedSet.set(expanded);
  }

  /** Expands/collapses a row. Returns false when the row isn't expandable. */
  setExpanded(rowId: TmRowId, expanded: boolean): boolean {
    const node = untracked(() => this.structure()).nodes.get(rowId);
    if (!node?.expandable) {
      return false;
    }
    const current = untracked(this.expandedSet);
    if (current.has(rowId) === expanded) {
      return true;
    }
    const next = new Set(current);
    if (expanded) {
      next.add(rowId);
    } else {
      next.delete(rowId);
    }
    this.expandedSet.set(next);
    return true;
  }

  /** Expands every ancestor of a row so it becomes visible (find, reveals). */
  expandAncestorsOf(rowId: TmRowId): void {
    const ancestors = this.ancestorsOf(rowId);
    if (ancestors.length === 0) {
      return;
    }
    const next = new Set(untracked(this.expandedSet));
    for (const ancestor of ancestors) {
      next.add(ancestor);
    }
    this.expandedSet.set(next);
  }

  /** The row's ancestor ids, nearest first. */
  ancestorsOf(rowId: TmRowId): readonly TmRowId[] {
    const nodes = untracked(() => this.structure()).nodes;
    const ancestors: TmRowId[] = [];
    let current = nodes.get(rowId)?.parentId ?? null;
    while (current !== null) {
      ancestors.push(current);
      current = nodes.get(current)?.parentId ?? null;
    }
    return ancestors;
  }

  /** Whether `rowId` sits somewhere below `ancestorId`. */
  isDescendantOf(rowId: TmRowId, ancestorId: TmRowId): boolean {
    return this.ancestorsOf(rowId).includes(ancestorId);
  }

  /** The row and its whole loaded subtree, depth-first (row moves carry it). */
  subtreeRowIds(rowId: TmRowId): readonly TmRowId[] {
    const nodes = untracked(() => this.structure()).nodes;
    if (!nodes.has(rowId)) {
      return [];
    }
    // Iterative pre-order DFS (explicit stack, children pushed reversed to
    // pop in model order) — recursion would overflow on very deep subtrees.
    const result: TmRowId[] = [];
    const stack: TmRowId[] = [rowId];
    while (stack.length > 0) {
      const id = stack.pop()!;
      result.push(id);
      const children = nodes.get(id)!.children;
      for (let i = children.length - 1; i >= 0; i--) {
        stack.push(children[i]);
      }
    }
    return result;
  }

  /** Restores a persisted expansion set, pruning ids that no longer expand. */
  restoreExpansion(ids: ReadonlySet<TmRowId>): void {
    if (!this.isTree) {
      return;
    }
    const nodes = untracked(() => this.structure()).nodes;
    const next = new Set<TmRowId>();
    for (const id of ids) {
      if (nodes.get(id)?.expandable) {
        next.add(id);
      }
    }
    this.expandedSet.set(next);
  }

  /** Captures the current visible order for identity-keyed remapping. */
  captureOrder(): TmGridOrderSnapshot {
    return {
      visibleIds: untracked(this.visibleIds),
      viewIndexById: untracked(this.viewIndexById),
    };
  }
}
