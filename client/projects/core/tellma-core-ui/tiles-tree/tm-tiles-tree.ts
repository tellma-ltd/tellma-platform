// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import {
  computed,
  DestroyRef,
  Directive,
  effect,
  inject,
  input,
  model,
  signal,
  type Type,
  untracked,
} from '@angular/core';

import { TM_ACTIVE_LOCALE, TM_UI_TRANSLATE } from '@tellma/core-ui';
import type { TmRowId } from '@tellma/core-ui/contracts';
import {
  ɵtmFlattenVisible,
  ɵtmResolveTree,
  type ɵTmTreeStructure,
} from '@tellma/core-ui/grid-engine';
import {
  TmTiles,
  type ɵTmTilesRow,
  type ɵTmTilesTree,
  type ɵTmTilesTreePlace,
} from '@tellma/core-ui/tiles';

import { ɵTmTilesTreeAffordance } from './tree-affordance';

/** The tree over the bound rows: their structure and each row's place among its siblings. */
interface TreeInfo<T> {
  /** The resolved hierarchy. */
  readonly structure: ɵTmTreeStructure<T>;
  /** Each row's 1-based position among its siblings. */
  readonly posInSet: ReadonlyMap<TmRowId, number>;
}

/**
 * Makes a `tm-tiles` a single-column tree over its flat rows: bind
 * `[parentId]` on the tiles and import this directive with them. Each row
 * indents one step per level under a twisty; expansion is two-way through
 * `expandedIds`, and children load lazily in batches through
 * `loadChildren`. The tiles keep their own selection, activation, driver
 * and keyboard; this entry point adds only what a tree needs, so a flat
 * list never downloads it.
 *
 * ```html
 * <tm-tiles
 *   ariaLabel="Accounts"
 *   [data]="accounts()"
 *   [rowId]="accountId"
 *   [layout]="accountTile"
 *   [parentId]="accountParentId"
 *   [hasChildren]="accountHasChildren"
 *   [loadChildren]="loadAccountChildren"
 *   [(expandedIds)]="expanded"
 * />
 * ```
 *
 * @tmGroup collection
 * @tmA11yNotes The tiles become a `treegrid` of one cell per row: each row
 *   carries `aria-level`, `aria-expanded` (on rows that can expand),
 *   `aria-posinset` and `aria-setsize`, and a row whose children load is
 *   `aria-busy`. Right expands a collapsed row or enters an expanded one,
 *   and Left collapses an expanded row or leaves for its parent, both in
 *   the live direction. The twisty is a pointer target hidden from
 *   assistive technology, since the keys and `aria-expanded` carry the
 *   same state. A failed children load announces once, naming the rows.
 */
// Matched by the tiles' own spec'd `parentId` input rather than a tm-prefixed
// attribute: binding the parent reader is what makes a tiles a tree, as on
// tm-tree-grid, so the template syntax is the same with or without the split.
// eslint-disable-next-line @angular-eslint/directive-selector -- see above
@Directive({ selector: 'tm-tiles[parentId]' })
export class TmTilesTree<T> implements ɵTmTilesTree<T> {
  private readonly tiles = inject<TmTiles<T>>(TmTiles);
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly locale = inject(TM_ACTIVE_LOCALE);
  private readonly announcer = inject(LiveAnnouncer);

  // ---- inputs and models ----

  /**
   * Reads a row's parent id (`null` for a root). Bound to a function, the
   * tiles are a single-column tree over the flat rows; a parent id that
   * resolves to no row, or that closes a cycle, renders the row as a root.
   * Bound to `undefined`, the tiles are flat.
   */
  readonly parentId = input<((row: T) => TmRowId | null) | undefined>(undefined);
  /** Marks rows whose children may not be loaded yet, so they show a twisty before any child exists. */
  readonly hasChildren = input<((row: T) => boolean) | undefined>(undefined);
  /**
   * The expanded row ids, two-way: the tree's only expansion state. Ids of
   * rows that are not present are kept and expand when their rows arrive.
   */
  readonly expandedIds = model<ReadonlySet<TmRowId>>(new Set());
  /**
   * Loads the children of a batch of expanded rows: append the fetched rows
   * to the tiles' `data` and resolve. Every expanded `hasChildren` row with
   * no loaded children that one moment collects arrives in one call, in
   * data order — the first bind, a written `expandedIds`, the rows a
   * resolved batch brings (so a deep expansion loads a level per batch), or
   * the expands a user makes within one task. A row that loads no children
   * becomes a leaf, until a refresh binds a new object for it. A rejection
   * collapses the batch's rows and announces once.
   */
  readonly loadChildren = input<((rows: readonly T[]) => Promise<void>) | undefined>(undefined);

  /**
   * The chrome each tree tile renders before its checkbox: the twisty and
   * the reserved loading slot.
   *
   * @internal
   */
  readonly affordance: Type<unknown> = ɵTmTilesTreeAffordance;

  // ---- children loading state ----

  /** Rows whose children batch is in flight. */
  private readonly inFlight = signal<ReadonlySet<TmRowId>>(new Set());
  /**
   * The row objects whose children batch resolved, by id: one that brought
   * no children is a leaf while that same object stays. A refresh brings new
   * row objects, which may load again; an append keeps them.
   */
  private readonly settled = signal<ReadonlyMap<TmRowId, T>>(new Map());
  /** Whether a batch flush is queued for the end of the task. */
  private flushQueued = false;
  /** Set on destroy: loads still in flight settle silently. */
  private destroyed = false;

  // ---- derived state ----

  /** The tree's resolved structure and sibling positions; `null` while `parentId` is unbound. */
  private readonly info = computed((): TreeInfo<T> | null => {
    const parentIdOf = this.parentId();
    if (parentIdOf === undefined) {
      return null;
    }
    const hasChildrenOf = this.hasChildren();
    const settled = this.settled();
    const rowIdOf = this.tiles.rowId();
    // A row whose children loaded and brought none is a leaf: no twisty, no
    // second load, until a refresh replaces the row object.
    const structure = ɵtmResolveTree(
      this.tiles.data(),
      rowIdOf,
      parentIdOf,
      hasChildrenOf === undefined
        ? null
        : (row: T) => hasChildrenOf(row) && settled.get(rowIdOf(row)) !== row,
    );
    const posInSet = new Map<TmRowId, number>();
    const number = (ids: readonly TmRowId[]): void => {
      for (let i = 0; i < ids.length; i++) {
        posInSet.set(ids[i], i + 1);
      }
    };
    number(structure.roots);
    for (const node of structure.nodes.values()) {
      number(node.children);
    }
    return { structure, posInSet };
  });

  /**
   * The rows on show in display order: the roots and the expanded rows'
   * children; `null` while `parentId` is unbound.
   *
   * @internal
   */
  readonly rows = computed((): readonly ɵTmTilesRow<T>[] | null => {
    const info = this.info();
    if (info === null) {
      return null;
    }
    const nodes = info.structure.nodes;
    return ɵtmFlattenVisible(info.structure, this.expandedIds()).map((id) => ({
      row: nodes.get(id)!.row,
      id,
    }));
  });

  /** Rows waiting on their children: expanded, on show, marked, with none loaded and a loader bound. */
  private readonly loadingIds = computed((): ReadonlySet<TmRowId> => {
    const info = this.info();
    const rows = this.rows();
    if (
      info === null ||
      rows === null ||
      this.hasChildren() === undefined ||
      this.loadChildren() === undefined
    ) {
      return new Set();
    }
    const expanded = this.expandedIds();
    const waiting = new Set<TmRowId>();
    for (const { id } of rows) {
      const node = info.structure.nodes.get(id)!;
      if (node.expandable && node.children.length === 0 && expanded.has(id)) {
        waiting.add(id);
      }
    }
    return waiting;
  });

  constructor() {
    // The tiles render, window and navigate this tree's rows from here on.
    untracked(() => this.tiles.ɵtree.set(this));

    // Lazy children: rows waiting on a load are batched at the end of the
    // task, so a bind, a written expansion or a burst of expands is one call.
    effect(() => {
      const waiting = this.loadingIds();
      const flying = this.inFlight();
      if (this.flushQueued || ![...waiting].some((id) => !flying.has(id))) {
        return;
      }
      this.flushQueued = true;
      queueMicrotask(() => this.flushLoads());
    });

    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
    });
  }

  // ---- the tiles' tree seam ----

  /**
   * A row's place: its depth, its expansion, whether its children load, and
   * its position among its siblings.
   *
   * @internal
   */
  place(id: TmRowId): ɵTmTilesTreePlace {
    const info = this.info()!;
    const node = info.structure.nodes.get(id)!;
    const parent = node.parentId;
    return {
      level: node.level + 1,
      expanded: node.expandable ? this.expandedIds().has(id) : null,
      loading: this.loadChildren() === undefined ? null : this.loadingIds().has(id),
      posInSet: info.posInSet.get(id) ?? 1,
      setSize:
        parent === null
          ? info.structure.roots.length
          : (info.structure.nodes.get(parent)?.children.length ?? 1),
    };
  }

  /**
   * Right (`forward`) or Left on the active row: expand or enter, collapse
   * or leave. Answers the row focus moves to, `'first'` with no active row,
   * or `null` when the row expanded or collapsed in place (or nothing moves).
   *
   * @internal
   */
  arrow(id: TmRowId | null, forward: boolean): TmRowId | 'first' | null {
    const node = id === null ? undefined : this.info()?.structure.nodes.get(id);
    if (id === null || node === undefined) {
      return 'first';
    }
    const expanded = this.expandedIds().has(id);
    if (forward) {
      if (node.expandable && !expanded) {
        this.setExpanded(id, true);
      } else if (expanded && node.children.length > 0) {
        return node.children[0];
      }
    } else if (node.expandable && expanded) {
      this.setExpanded(id, false);
    } else if (node.parentId !== null) {
      return node.parentId;
    }
    return null;
  }

  /**
   * Expands a row's collapsed ancestors so it shows; `false` when no row
   * has the id.
   *
   * @internal
   */
  reveal(id: TmRowId): boolean {
    const nodes = this.info()?.structure.nodes;
    if (nodes?.has(id) !== true) {
      return false;
    }
    const expanded = this.expandedIds();
    const missing: TmRowId[] = [];
    for (
      let parent = nodes.get(id)!.parentId;
      parent !== null;
      parent = nodes.get(parent)!.parentId
    ) {
      if (!expanded.has(parent)) {
        missing.push(parent);
      }
    }
    if (missing.length > 0) {
      this.expandedIds.set(new Set([...expanded, ...missing]));
    }
    return true;
  }

  /**
   * Expands or collapses a row that can expand: its twisty was clicked.
   *
   * @internal
   */
  toggle(id: TmRowId): void {
    if (this.info()?.structure.nodes.get(id)?.expandable === true) {
      this.setExpanded(id, !this.expandedIds().has(id));
    }
  }

  /**
   * The nearest ancestor of a row that is on show, or `null`.
   *
   * @internal
   */
  shownAncestor(id: TmRowId, shown: ReadonlyMap<TmRowId, number>): TmRowId | null {
    const nodes = this.info()?.structure.nodes;
    let ancestor = nodes?.get(id)?.parentId ?? null;
    while (ancestor !== null && !shown.has(ancestor)) {
      ancestor = nodes?.get(ancestor)?.parentId ?? null;
    }
    return ancestor;
  }

  // ---- internals ----

  /** Expands or collapses one row through the two-way expansion. */
  private setExpanded(id: TmRowId, open: boolean): void {
    const next = new Set(this.expandedIds());
    if (open) {
      next.add(id);
    } else {
      next.delete(id);
    }
    this.expandedIds.set(next);
  }

  /**
   * Calls `loadChildren` once with every waiting row that is not in flight,
   * in data order. A resolved batch settles its rows (a row that brought no
   * children becomes a leaf); a rejected one collapses them and announces
   * once.
   */
  private flushLoads(): void {
    this.flushQueued = false;
    const load = this.loadChildren();
    const info = this.info();
    if (this.destroyed || load === undefined || info === null) {
      return;
    }
    const flying = this.inFlight();
    const batch = [...this.loadingIds()]
      .filter((id) => !flying.has(id))
      .map((id) => info.structure.nodes.get(id)!)
      .sort((a, b) => a.modelIndex - b.modelIndex);
    if (batch.length === 0) {
      return;
    }
    const ids = batch.map((node) => node.id);
    this.inFlight.set(new Set([...flying, ...ids]));
    const settle = (resolved: boolean): void => {
      if (this.destroyed) {
        return;
      }
      const remaining = new Set(this.inFlight());
      for (const id of ids) {
        remaining.delete(id);
      }
      this.inFlight.set(remaining);
      if (resolved) {
        const next = new Map(this.settled());
        for (const node of batch) {
          next.set(node.id, node.row);
        }
        this.settled.set(next);
        return;
      }
      // The failed rows collapse and leave the expansion; the next expand
      // tries again.
      const expanded = new Set(this.expandedIds());
      for (const id of ids) {
        expanded.delete(id);
      }
      this.expandedIds.set(expanded);
      const titles = batch.map((node) => this.tiles.layout().title(node.row));
      const label = new Intl.ListFormat(this.locale(), { type: 'conjunction' }).format(titles);
      void this.announcer.announce(this.translate('tiles.loadFailed', { label })(), 'polite');
    };
    // A loader that throws synchronously rejects the batch like one that rejects.
    Promise.resolve()
      .then(() => load(batch.map((node) => node.row)))
      .then(
        () => settle(true),
        () => settle(false),
      );
  }
}
