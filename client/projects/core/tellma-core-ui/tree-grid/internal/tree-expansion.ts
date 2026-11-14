// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { effect, signal, untracked, type Injector, type WritableSignal } from '@angular/core';

import type { TmRowId } from '@tellma/core-ui/contracts';
import type { ɵTmGridTreeExpansion, ɵTmGridTreeHost } from '@tellma/core-ui/grid';

/**
 * The tree-grid bindings the expansion controller reads. Every member is
 * read lazily: the controller is built while the grid's base class
 * constructs, before the tree-grid's own input fields exist.
 */
export interface ɵTmTreeGridExpansionInputs<T> {
  /** Reads a row's parent id; `null` marks a root. */
  readonly parentId: () => (row: T) => TmRowId | null;
  /** Marks rows whose children may not be loaded yet. */
  readonly hasChildren: () => ((row: T) => boolean) | undefined;
  /** Loads the children of a batch of expanded rows. */
  readonly loadChildren: () => ((rows: readonly T[]) => Promise<void>) | undefined;
  /** Who keeps the expansion: the state store, or the `expandedIds` model. */
  readonly expansionSource: () => 'store' | 'model';
  /** The two-way expanded-row set the `'model'` source reads and writes. */
  readonly expandedIds: WritableSignal<ReadonlySet<TmRowId>>;
}

/**
 * Keeps a tree grid's expansion and loads lazy children in batches. It
 * lives with the tree grid, so a flat grid carries none of it: the grid
 * core calls it at its hook points (rows reconciled, content bound or
 * persisted, a user expand or collapse, a reveal).
 *
 * The kept expansion is the `expandedIds` model's set, or the state
 * store's slice, and is never pruned: an id whose row is not loaded yet (a
 * remembered grandchild, a row a search hides) waits in it until its row
 * arrives. The engine holds the present, expandable part of it.
 */
export class ɵTmTreeGridExpansion<T> implements ɵTmGridTreeExpansion {
  /**
   * The kept expansion of the bound content. `null` until the content
   * binds, and while a store-kept content without a remembered slice waits
   * for rows to seed `defaultExpandedDepth` over.
   */
  private kept: Set<TmRowId> | null = null;
  /** Whether the bound content's expansion is the `expandedIds` model's (read at content bind). */
  private modelSource = false;
  /** The set this grid last wrote to the `expandedIds` model. */
  private written: ReadonlySet<TmRowId> | null = null;
  /**
   * Set when a content bound over the outgoing content's rows: its loads
   * wait for the first rows array after the bind, so no outgoing row is
   * loaded for the incoming content.
   */
  private bindPending = false;
  /** Expanded rows waiting for the microtask that loads their children in one batch. */
  private queue: TmRowId[] | null = null;
  /** Kills in-flight child-load completions across a content switch or disposal. */
  private generation = 0;
  /** Whether the grid was disposed. */
  private destroyed = false;
  /** Rows whose children are loading (the reserved-slot spinner). */
  readonly loadingIds = signal<ReadonlySet<TmRowId>>(new Set());

  /**
   * Creates the controller over the grid core's tree face and the tree
   * grid's bindings.
   */
  constructor(
    private readonly host: ɵTmGridTreeHost<T>,
    private readonly inputs: ɵTmTreeGridExpansionInputs<T>,
  ) {}

  // ---- user and engine expansion ----

  /**
   * Expands or collapses a row at the user's request. An expand joins the
   * kept expansion at once — a `hasChildren` row with no loaded children
   * shows expanded with the reserved-slot spinner while its children load
   * in the microtask's batch. A collapse removes that row only (the engine
   * moves activation out of a collapsing subtree); descendants keep their
   * remembered expansion.
   */
  setRowExpanded(rowId: TmRowId, expanded: boolean): void {
    this.host.syncRows();
    this.host.engine.setExpanded(rowId, expanded);
    if (expanded) {
      this.syncExpansion();
      this.queueLoads([rowId]);
    } else if (this.kept?.delete(rowId) === true) {
      this.emit();
    }
  }

  /** Expands a row's ancestors for a reveal, keeping the active cell's row, and remembers them. */
  expandAncestorsOf(rowId: TmRowId): void {
    const engine = this.host.engine;
    this.keepActiveRow(() => engine.expandAncestorsOf(rowId));
    this.syncExpansion();
  }

  /**
   * Folds the engine's expansion into the kept one, so an expansion the
   * engine made itself (a find match, an error jump, an undo reveal, an
   * inserted child) is remembered and, in the `'model'` source, emitted.
   * The kept set only ever grows here; removals are explicit.
   */
  private syncExpansion(): void {
    const kept = this.kept;
    if (kept === null) {
      return;
    }
    let grew = false;
    for (const id of untracked(() => this.host.engine.model.expandedIds())) {
      if (!kept.has(id)) {
        kept.add(id);
        grew = true;
      }
    }
    if (grew) {
      this.emit();
    }
  }

  /** Writes a copy of the kept expansion to the `expandedIds` model (the `'model'` source only). */
  private emit(): void {
    if (!this.modelSource || this.kept === null) {
      return;
    }
    this.written = new Set(this.kept);
    this.inputs.expandedIds.set(this.written);
  }

  /**
   * Applies the kept expansion to the engine, which keeps the present,
   * expandable part of it, and queues the children loads it needs: every
   * expanded row (`'all'`, a content bind), only the rows this application
   * newly expanded (`'new'`: rows that arrived, a consumer write), or none
   * (`'none'`: a bind over the outgoing rows). A store-kept content without
   * a remembered slice seeds `defaultExpandedDepth` over the rows bound
   * instead (its first rows, once any), and that seed loads like a bind.
   */
  private apply(loads: 'all' | 'new' | 'none'): void {
    const engine = this.host.engine;
    let before: ReadonlySet<TmRowId> | null = untracked(() => engine.model.expandedIds());
    if (this.kept === null) {
      if (untracked(() => engine.model.dataRowCount()) === 0) {
        return; // nothing to seed over yet; the content's first rows seed
      }
      this.keepActiveRow(() => engine.seedExpansion());
      this.kept = new Set(untracked(() => engine.model.expandedIds()));
      before = null; // a first seed loads like a bind
    } else {
      const kept = this.kept;
      this.keepActiveRow(() => engine.restoreExpansion(kept));
    }
    if (loads === 'none') {
      return;
    }
    const applied = untracked(() => engine.model.expandedIds());
    const prior = loads === 'all' ? null : before;
    this.queueLoads(prior === null ? applied : [...applied].filter((id) => !prior.has(id)));
  }

  /**
   * Runs an expansion change and keeps the active cell on its row — or, when
   * the row went under a collapsed ancestor, on the nearest visible
   * ancestor — since a restore re-orders the view without remapping it.
   */
  private keepActiveRow(change: () => void): void {
    const engine = this.host.engine;
    const active = untracked(() => engine.nav.activeCell());
    const rowId =
      active === null ? null : (untracked(() => engine.model.rowAt(active.row))?.id ?? null);
    change();
    if (active === null || rowId === null) {
      return;
    }
    for (const id of [rowId, ...engine.model.ancestorsOf(rowId)]) {
      const row = untracked(() => engine.model.viewIndexOfRow(id));
      if (row !== -1) {
        if (row !== active.row) {
          const cell = { row, col: active.col };
          engine.nav.setActive(cell);
          engine.selection.collapseTo(cell);
        }
        return;
      }
    }
  }

  /**
   * Re-applies the kept expansion over a reconciled rows array: what the
   * reconcile itself expanded (an editor's row revealed) is remembered
   * first, then remembered rows the new rows bring expand and their
   * children load. The first rows after a bind over the outgoing rows load
   * like the bind.
   */
  rowsChanged(): void {
    this.syncExpansion();
    const bound = this.bindPending;
    this.bindPending = false;
    this.apply(bound ? 'all' : 'new');
  }

  // ---- lazy child loading ----

  /**
   * Queues expanded rows for the next children batch: one microtask
   * collects every row queued in the same task, so a restore, a level of
   * loaded children, or a burst of user expands loads in one call.
   */
  private queueLoads(ids: Iterable<TmRowId>): void {
    if (untracked(this.inputs.loadChildren) === undefined) {
      return;
    }
    const queued = [...ids];
    if (queued.length === 0) {
      return;
    }
    if (this.queue === null) {
      this.queue = queued;
      const generation = this.generation;
      queueMicrotask(() => this.flushLoads(generation));
    } else {
      this.queue.push(...queued);
    }
  }

  /**
   * Calls `loadChildren` once with the queued rows that still need it —
   * present, expanded, `hasChildren`, not loading, and without a loaded
   * child — in model order, and shows their spinners until it settles.
   */
  private flushLoads(generation: number): void {
    if (this.destroyed || generation !== this.generation) {
      // Disposed, or the content switched before the batch formed: the
      // queue, if any, is the incoming content's, flushed by its own task.
      return;
    }
    const queue = this.queue;
    this.queue = null;
    const load = untracked(this.inputs.loadChildren);
    const hasChildren = untracked(this.inputs.hasChildren);
    if (queue === null || load === undefined || hasChildren === undefined) {
      return;
    }
    const host = this.host;
    host.syncRows();
    const engine = host.engine;
    const expanded = untracked(() => engine.model.expandedIds());
    const loading = untracked(this.loadingIds);
    // One pass over the rows finds every row with a loaded child, rather
    // than walking each candidate's subtree.
    const parentId = untracked(this.inputs.parentId);
    const parents = new Set<TmRowId>();
    for (const row of untracked(host.rows)) {
      const parent = parentId(row);
      if (parent !== null) {
        parents.add(parent);
      }
    }
    const batch = new Map<TmRowId, T>();
    for (const id of queue) {
      if (batch.has(id) || loading.has(id) || parents.has(id) || !expanded.has(id)) {
        continue;
      }
      const row = engine.model.rowById(id);
      if (row !== undefined && hasChildren(row)) {
        batch.set(id, row);
      }
    }
    if (batch.size === 0) {
      return;
    }
    const ids = [...batch.keys()].sort(
      (a, b) => engine.model.modelIndexOfRow(a) - engine.model.modelIndexOfRow(b),
    );
    const next = new Set(loading);
    for (const id of ids) {
      next.add(id);
    }
    this.loadingIds.set(next);
    const rows = ids.map((id) => batch.get(id)!);
    Promise.resolve()
      .then(() => load(rows))
      .then(
        () => this.settleLoads(ids, true, generation),
        () => this.settleLoads(ids, false, generation),
      );
  }

  /**
   * The tail of a children batch: the spinners clear and the loaded rows
   * reconcile — their remembered children expand and queue the next level.
   * A rejected batch collapses its rows, drops them from the kept
   * expansion, and announces the failure once.
   */
  private settleLoads(ids: readonly TmRowId[], resolved: boolean, generation: number): void {
    if (this.destroyed || generation !== this.generation) {
      return; // disposed, or the content switched out from under the load
    }
    const loading = new Set(untracked(this.loadingIds));
    for (const id of ids) {
      loading.delete(id);
    }
    this.loadingIds.set(loading);
    this.host.syncRows();
    if (resolved) {
      return;
    }
    const engine = this.host.engine;
    let removed = false;
    for (const id of ids) {
      engine.setExpanded(id, false);
      removed = this.kept?.delete(id) === true || removed;
    }
    if (removed) {
      this.emit();
    }
    this.host.announcements.announce('grid.announce.lazyLoadFailed');
  }

  // ---- content lifecycle ----

  /**
   * Binds the tree expansion of the content being restored, reading
   * `expansionSource` now: the `expandedIds` model alone, or the store's
   * slice — or, without one, `defaultExpandedDepth` seeded over the first
   * rows. The engine applies the kept set's present part, and every
   * expanded row with unloaded children loads in one batch. Over the
   * outgoing content's rows (`outgoingRows`), that batch waits for the
   * content's first rows array.
   */
  bind(stored: ReadonlySet<TmRowId> | undefined, outgoingRows: boolean): void {
    this.modelSource = untracked(this.inputs.expansionSource) === 'model';
    if (this.modelSource) {
      const bound = untracked(this.inputs.expandedIds);
      this.kept = new Set(bound);
      this.written = bound; // the bound value is not a consumer write
    } else {
      this.kept = stored === undefined ? null : new Set(stored);
    }
    this.bindPending = outgoingRows;
    this.apply(outgoingRows ? 'none' : 'all');
  }

  /**
   * The expansion slice to persist: the kept set of a store-kept content,
   * or — when the `expandedIds` model keeps it — the slice as stored, which
   * the grid neither reads nor writes then.
   */
  persisted(stored: ReadonlySet<TmRowId> | undefined): {
    readonly expandedRowIds?: ReadonlySet<TmRowId>;
  } {
    if (this.modelSource) {
      return stored === undefined ? {} : { expandedRowIds: stored };
    }
    this.syncExpansion(); // an engine expansion the effect has not folded in yet
    return this.kept === null ? {} : { expandedRowIds: new Set(this.kept) };
  }

  /**
   * Drops the outgoing content's loads on a switch: spinners clear, a
   * batch still forming is discarded, and in-flight loads resolve into a
   * no-op, so none lands on the incoming content's same-id row.
   */
  reset(): void {
    if (untracked(this.loadingIds).size > 0) {
      this.loadingIds.set(new Set());
    }
    this.queue = null;
    this.generation += 1;
  }

  /** Disposes the controller: in-flight child loads complete silently. */
  destroy(): void {
    this.destroyed = true;
    this.queue = null;
  }

  // ---- effects ----

  /** Creates the controller's effects, after the core's rows effect. */
  setupEffects(injector: Injector): void {
    // The engine's own expansions (a find match, an error jump, an undo
    // reveal, an inserted child) join the kept expansion.
    effect(
      () => {
        this.host.engine.model.expandedIds();
        untracked(() => this.syncExpansion());
      },
      { injector },
    );

    // A consumer write to the `expandedIds` model after the bind replaces
    // the kept set and applies like a restore: rows it leaves out
    // collapse, rows it adds expand and load. It is not echoed back.
    effect(
      () => {
        const requested = this.inputs.expandedIds();
        untracked(() => {
          if (!this.modelSource || requested === this.written) {
            return;
          }
          this.written = requested;
          const kept = this.kept;
          if (
            kept !== null &&
            kept.size === requested.size &&
            [...requested].every((id) => kept.has(id))
          ) {
            return; // the same members: nothing to apply
          }
          // Reconcile first, so the active cell remaps; the write replaces
          // the kept set, so the old one does not re-apply first.
          this.host.syncRows(false);
          this.kept = new Set(requested);
          this.apply('new');
        });
      },
      { injector },
    );
  }
}
