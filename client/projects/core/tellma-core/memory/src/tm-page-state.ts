// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { effect, inject, Injectable, Injector, signal, untracked } from '@angular/core';
import {
  TmClientStore,
  type TmMeResult,
  type TmPartition,
  TmSession,
  type TmTenantSettingsForClient,
  TmUrls,
  ɵTM_STATE_PARTS,
  ɵtmRoutedTenant,
} from '@tellma/core';

// Page state (spec 0025 §10): what a page needs to look as it did when the
// user returns within the app, kept per tab and in memory only, by the page's
// canonical path. The container is bounded and least recently used; a write
// marks the entries it touches stale, so they render at once and refetch
// their rows, and a change of the tenant's grants or languages reaches every
// entry of the tenant.

/** A page-state key: a page's canonical path, without its query or fragment. */
export type TmPageStateKey = string & { readonly __tmPageStateKey: true };

/**
 * The page-state key of a canonical path. The query and fragment are state,
 * not identity, and are cut off.
 *
 * @param path - The page's canonical path, such as `/17/centers`.
 * @returns The key.
 *
 * @example
 * ```ts
 * const key = tmPageStateKey('/17/centers');
 * ```
 */
export function tmPageStateKey(path: string): TmPageStateKey {
  return path.split(/[?#]/, 1)[0] as TmPageStateKey;
}

/** How {@link TmPageState.remember} treats the entry it replaces. */
export interface TmRememberOptions {
  /**
   * Keeps the entry stale when it was: a host that remembers its state as
   * the user leaves keeps a stale mark a write set meanwhile.
   */
  readonly keepStale?: boolean;
}

/** One remembered page. */
interface TmPageEntry {
  /** The host's state, held by reference. */
  readonly state: unknown;
  /** Whether the rows must be refetched. */
  readonly stale: boolean;
}

/** The facts of a tenant whose change reaches its entries: its grants' tag and its languages. */
interface TmTenantMarks {
  /** The held `permissions` tag, while the envelope is held. */
  readonly permissions: string | undefined;
  /** The tenant's language codes in order, while its settings are held. */
  readonly languages: string | undefined;
}

/** How many pages a tab remembers. */
const CAPACITY = 20;

/**
 * Remembers, per tab and in memory only, what each page needs to look as it
 * did when the user returns: at most 20 pages, the least recently used
 * forgotten first. Each host decides what it remembers; values are held by
 * reference and must not change.
 *
 * @example
 * ```ts
 * const pageState = inject(TmPageState);
 * const key = tmPageStateKey('/17/centers');
 * pageState.remember(key, { query, rows, scroll });
 * // later, back on the page
 * const held = pageState.recall<CentersState>(key);
 * if (held !== undefined && pageState.isStale(key)) { refetch(held.query); }
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmPageState {
  readonly #injector = inject(Injector);
  readonly #urls = inject(TmUrls);
  readonly #store = inject(TmClientStore);

  /** The entries, least recently used first. */
  readonly #entries = new Map<string, TmPageEntry>();
  /** The tenants with entries, which the grants and languages watch follows. */
  readonly #tenants = signal<readonly number[]>([]);
  /** The grants' tag and languages each tenant's entries were last held under. */
  readonly #marks = new Map<number, TmTenantMarks>();

  /** Creates the service, which watches the grants and languages of every tenant it holds entries of. */
  constructor() {
    inject(ɵTM_STATE_PARTS).pageState = () => this.ɵkeys();

    // --- A permissions move marks the tenant's entries stale; a languages change forgets them ---
    effect(() => {
      for (const t of this.#tenants()) {
        const marks = this.#read(t);
        untracked(() => {
          // A document that is absent says nothing: the last one held stays the mark.
          const held = this.#marks.get(t);
          const moved = (name: keyof TmTenantMarks): boolean =>
            marks[name] !== undefined && held?.[name] !== undefined && marks[name] !== held[name];
          if (moved('languages')) {
            this.#forgetTenant(t);
          } else if (moved('permissions')) {
            this.markStale({ tenantId: t });
          }
          if (this.#marks.has(t)) {
            this.#marks.set(t, {
              permissions: marks.permissions ?? held?.permissions,
              languages: marks.languages ?? held?.languages,
            });
          }
        });
      }
    });
  }

  /**
   * Remembers a page's state, replacing what it held; the page becomes the
   * most recently used, and is no longer stale unless `keepStale` keeps it.
   *
   * @param key - The page's key.
   * @param state - The state, held by reference.
   * @param options - Whether a stale mark stays.
   */
  remember<T>(key: TmPageStateKey, state: T, options: TmRememberOptions = {}): void {
    const stale = options.keepStale === true && this.#entries.get(key)?.stale === true;
    this.#entries.delete(key);
    this.#entries.set(key, { state, stale });
    // The least recently used goes past the bound.
    if (this.#entries.size > CAPACITY) {
      this.#entries.delete(this.#entries.keys().next().value!);
    }
    const t = ɵtmRoutedTenant(key);
    if (t !== null && !this.#marks.has(t)) {
      // The tenant's facts now are the ones its entries were held under.
      this.#marks.set(t, untracked(() => this.#read(t)));
    }
    this.#follow();
  }

  /**
   * The state a page remembered, which makes it the most recently used.
   *
   * @param key - The page's key.
   * @returns The state, or `undefined` when none is held.
   */
  recall<T>(key: TmPageStateKey): T | undefined {
    const entry = this.#entries.get(key);
    if (entry !== undefined) {
      this.#entries.delete(key);
      this.#entries.set(key, entry);
    }
    return entry?.state as T | undefined;
  }

  /**
   * Whether a page's rows must be refetched; the rest of its state restores.
   *
   * @param key - The page's key.
   * @returns Whether the entry is stale; `false` when none is held.
   */
  isStale(key: TmPageStateKey): boolean {
    return this.#entries.get(key)?.stale === true;
  }

  /**
   * Marks entries stale after a write or a permissions move: with an
   * entity, the tenant's entries whose page shows that entity (its search,
   * details and list pages); without one, every entry of the tenant.
   *
   * @param scope - The tenant, and the entity a write changed.
   */
  markStale(scope: { readonly tenantId: number; readonly entity?: string }): void {
    for (const [key, entry] of this.#entries) {
      if (ɵtmRoutedTenant(key) === scope.tenantId && (scope.entity === undefined || this.#entity(key) === scope.entity)) {
        // Replaced in place, so the entry keeps its place in the order.
        this.#entries.set(key, { ...entry, stale: true });
      }
    }
  }

  /**
   * Forgets a page's state.
   *
   * @param key - The page's key.
   */
  forget(key: TmPageStateKey): void {
    this.#entries.delete(key);
    this.#follow();
  }

  /**
   * The keys of the pages remembered, least recently used first: the
   * dump's `pageState`. Internal.
   *
   * @returns The keys.
   */
  ɵkeys(): string[] {
    return [...this.#entries.keys()];
  }

  /** The entity a key's page shows; a hatch's key, which adds a segment, maps without it. */
  #entity(key: string): string | undefined {
    return (this.#urls.describe(key) ?? this.#urls.describe(key.slice(0, key.lastIndexOf('/'))))?.entity;
  }

  /** The tenant's held grants' tag and language codes, each absent while its document is; tracked. */
  #read(t: number): TmTenantMarks {
    const sub = this.#injector.get(TmSession).profile()?.sub;
    if (sub === undefined) {
      return { permissions: undefined, languages: undefined };
    }
    const partition: TmPartition = { kind: 'tenant', sub, tenantId: t };
    return {
      permissions: this.#store.doc<TmMeResult>(partition, 'me')()?.data.access.tag,
      languages: this.#store
        .doc<TmTenantSettingsForClient>(partition, 'settings')()
        ?.data.languages.map((language) => language.code)
        .join(),
    };
  }

  /** Forgets every entry of a tenant. */
  #forgetTenant(t: number): void {
    for (const key of [...this.#entries.keys()]) {
      if (ɵtmRoutedTenant(key) === t) {
        this.#entries.delete(key);
      }
    }
    this.#follow();
  }

  /** Points the watch at the tenants that have entries now; a tenant with none keeps no marks. */
  #follow(): void {
    const tenants = [...new Set([...this.#entries.keys()].map(ɵtmRoutedTenant))].filter((t): t is number => t !== null);
    for (const t of this.#marks.keys()) {
      if (!tenants.includes(t)) {
        this.#marks.delete(t);
      }
    }
    if (tenants.sort().join() !== untracked(this.#tenants).join()) {
      this.#tenants.set(tenants);
    }
  }
}
