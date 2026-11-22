// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, DestroyRef, inject, Injectable, Injector, signal, untracked, type Signal, type WritableSignal } from '@angular/core';
import { TmClientStore, type TmId, type TmPartition, TmSession, TmStoreRefusedError, TmTelemetry, ɵTM_API_HOST } from '@tellma/core';

// Tree expansion memory (spec 0025 §12): a tree search page's expanded nodes,
// kept in the sign-in session's partition of the store, so every tab sees
// them and the next visit fetches them expanded in one call. Changes show at
// once and are written after 250 ms; a page keeps its 500 newest ids.

/** The stored expansion of one page. */
interface TmTreeDoc {
  /** The expanded ids, in expansion order, oldest first. */
  readonly ids: readonly TmId[];
}

/** One change not yet written: an id opened or closed, or the whole list replaced. */
type TmTreeChange =
  | { readonly open: boolean; readonly id: TmId }
  | { readonly ids: readonly TmId[] };

/** A page's view and its changes not yet written. */
interface TmTreePage {
  /** The changes not yet written, oldest first, including those a write has in flight. */
  readonly pending: WritableSignal<readonly TmTreeChange[]>;
  /** The pending changes a write has in flight, which the next flush leaves to it. */
  readonly sending: Set<TmTreeChange>;
  /** The expanded ids, as stored with the pending changes made. */
  readonly view: Signal<ReadonlySet<TmId>>;
}

/** How long a change waits for the next one before it is written. */
const DEBOUNCE_MS = 250;

/** How many ids a page keeps. */
const CAPACITY = 500;

/** A stored document's ids; none when it is absent or not a list. */
function idsOf(doc: TmTreeDoc | undefined): readonly TmId[] {
  return Array.isArray(doc?.ids) ? doc.ids : [];
}

/** The ids with the changes made, oldest first, capped to the newest. */
function apply(ids: readonly TmId[], changes: readonly TmTreeChange[]): TmId[] {
  let next = [...ids];
  for (const change of changes) {
    if ('ids' in change) {
      next = [...new Set(change.ids)];
    } else if (change.open) {
      if (!next.includes(change.id)) {
        next.push(change.id);
      }
    } else {
      next = next.filter((id) => id !== change.id);
    }
  }
  return next.slice(-CAPACITY);
}

/**
 * Remembers a tree search page's expanded nodes for the sign-in session, in
 * every tab: a new sign-in starts collapsed.
 *
 * @example
 * ```ts
 * const trees = inject(TmTreeExpansion);
 * const expanded = untracked(trees.expanded('/17/centers'));  // read once per load
 * trees.toggle('/17/centers', 5, true);
 * trees.replace('/17/centers', [...expanded].filter((id) => known.has(id)));
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmTreeExpansion {
  readonly #injector = inject(Injector);
  readonly #store = inject(TmClientStore);
  readonly #telemetry = inject(TmTelemetry);

  /** Each page's view and pending changes, by the page's path. */
  readonly #pages = new Map<string, TmTreePage>();
  /** The debounce's timer, while changes wait for it. */
  #timer: ReturnType<typeof setTimeout> | undefined;

  /** Creates the service, which writes what is pending when the page hides. */
  constructor() {
    const stop = inject(ɵTM_API_HOST).listen('window', 'pagehide', () => this.#flush());
    inject(DestroyRef).onDestroy(() => {
      stop();
      clearTimeout(this.#timer);
    });
  }

  /**
   * A page's expanded nodes, in expansion order, oldest first.
   *
   * @param page - The search page's canonical path, such as `/17/centers`.
   * @returns The ids' signal: empty with no signed-in user.
   */
  expanded(page: string): Signal<ReadonlySet<TmId>> {
    return this.#page(page).view;
  }

  /**
   * Opens or closes one node; shown at once and written after 250 ms.
   *
   * @param page - The search page's canonical path.
   * @param id - The node's id.
   * @param open - Whether the node is now expanded.
   */
  toggle(page: string, id: TmId, open: boolean): void {
    this.#change(page, { open, id });
  }

  /**
   * Replaces a page's expanded nodes in one write, as a collapse that drops
   * descendants, or the pruning of unknown ids, does.
   *
   * @param page - The search page's canonical path.
   * @param ids - The expanded ids, oldest first.
   */
  replace(page: string, ids: Iterable<TmId>): void {
    this.#change(page, { ids: [...ids] });
  }

  /** The signed-in session's partition, or `null`. */
  #partition(): TmPartition | null {
    const profile = this.#injector.get(TmSession).profile();
    return profile === null ? null : { kind: 'session', sub: profile.sub, sessionHandle: profile.sh };
  }

  /** A page's view and pending changes, made at its first use. */
  #page(page: string): TmTreePage {
    let entry = this.#pages.get(page);
    if (entry === undefined) {
      const pending = signal<readonly TmTreeChange[]>([]);
      const view = computed(() => {
        const partition = this.#partition();
        const stored = partition === null ? undefined : this.#store.doc<TmTreeDoc>(partition, `tree:${page}`)();
        return new Set(apply(idsOf(stored?.data), pending())) as ReadonlySet<TmId>;
      });
      entry = { pending, sending: new Set(), view };
      this.#pages.set(page, entry);
      // The partition is read at the first use, unless the boot step read it.
      const partition = untracked(() => this.#partition());
      if (partition !== null) {
        void this.#store.ɵreadOnce(partition);
      }
    }
    return entry;
  }

  /** Shows a change at once, and writes it after the debounce. */
  #change(page: string, change: TmTreeChange): void {
    if (untracked(() => this.#partition()) === null) {
      return;
    }
    this.#page(page).pending.update((pending) => [...pending, change]);
    clearTimeout(this.#timer);
    this.#timer = setTimeout(() => this.#flush(), DEBOUNCE_MS);
  }

  /**
   * Writes every page's pending changes that no write has in flight; each
   * patch makes them on the stored copy.
   */
  #flush(): void {
    clearTimeout(this.#timer);
    const partition = untracked(() => this.#partition());
    for (const [page, { pending, sending }] of this.#pages) {
      const changes = untracked(pending).filter((change) => !sending.has(change));
      if (partition === null || changes.length === 0) {
        continue;
      }
      // The changes leave the pending list once their own write settles, so
      // the view never shows the stored copy without them in between, and a
      // write that lands after a later flush started removes only its own.
      changes.forEach((change) => sending.add(change));
      const settle = (): void => {
        changes.forEach((change) => sending.delete(change));
        pending.update((list) => list.filter((change) => !changes.includes(change)));
      };
      this.#store
        .patch<TmTreeDoc>(partition, `tree:${page}`, (current) => ({ ids: apply(idsOf(current), changes) }))
        .then(settle, (error: unknown) => {
          settle();
          if (!(error instanceof TmStoreRefusedError)) {
            this.#telemetry.error(error, { source: 'tm-tree-expansion' });
          }
        });
    }
  }
}
