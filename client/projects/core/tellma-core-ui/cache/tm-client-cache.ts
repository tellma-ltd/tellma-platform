// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ErrorHandler, inject, Injectable } from '@angular/core';

/** One of the browser stores {@link TmClientCache.clearAll} sweeps. */
export type TmClientCacheArea = 'cache-storage' | 'indexeddb' | 'local-storage' | 'session-storage';

/** Options of {@link TmClientCache.clearAll}. */
export interface TmClientCacheClearOptions {
  /**
   * Returns `true` for a store the sweep must leave in place: a CacheStorage
   * cache or IndexedDB database by its name, or a `localStorage` or
   * `sessionStorage` key. Called once per matching name. A predicate that
   * throws is reported to the `ErrorHandler`, and that name is deleted.
   */
  readonly keep?: (name: string, area: TmClientCacheArea) => boolean;
}

/**
 * The global cleanup hook for the library's persistent client-side state.
 *
 * Convention replaces registration: every browser store a library or app
 * feature creates follows one naming rule, so there is nothing to register,
 * no init-order dependency, and state left by features that never loaded
 * this session, or by prior sessions, is swept all the same:
 * - CacheStorage caches and IndexedDB databases are named `tm-…`;
 * - `localStorage` and `sessionStorage` keys are named `tm.…`.
 *
 * The Angular service worker's `ngsw:…` caches never match the rule, so the
 * sweep leaves the installed app shell alone.
 *
 * An app calls {@link clearAll} when a session ends. Tenant switching clears
 * nothing: cache keys carry tenant identity by construction (the tenant id
 * is part of every API URL), so entries are tenant-scoped already. Features
 * treat their stores as re-populatable accelerators, so clearing mid-flight
 * is always safe.
 *
 * Every opener of a `tm-*` IndexedDB database must close its connection on
 * `versionchange`. A delete that an open connection blocks stays queued in
 * the browser, and every later open of that database waits behind it.
 */
@Injectable({ providedIn: 'root' })
export class TmClientCache {
  /** Where a throwing `keep` predicate is reported. */
  private readonly errorHandler = inject(ErrorHandler);

  /**
   * Deletes every store the naming rule matches, minus those `keep`
   * returns `true` for: the `tm-*` CacheStorage caches, the `tm-*` IndexedDB
   * databases, and the `tm.*` keys of `localStorage` and of this tab's
   * `sessionStorage` (other tabs clear their own).
   *
   * The four areas are swept in parallel, and each is best effort: an area
   * the browser does not offer or refuses (an insecure context, blocked
   * storage, no `indexedDB.databases()` to enumerate with) is skipped. A
   * database delete that an open connection blocks resolves its part at
   * once; the browser completes it when that connection closes. Never
   * throws.
   *
   * @param options - What to keep.
   * @returns A promise that resolves when every area's sweep has settled.
   */
  async clearAll(options?: TmClientCacheClearOptions): Promise<void> {
    // Whether the sweep deletes a name the naming rule matched.
    const deletes = (name: string, area: TmClientCacheArea): boolean => {
      try {
        return options?.keep?.(name, area) !== true;
      } catch (error) {
        // A sweep ends a session, so it never leaves behind what it was not
        // clearly told to keep.
        this.errorHandler.handleError(error);
        return true;
      }
    };
    await Promise.allSettled([
      sweepCacheStorage(deletes),
      sweepIndexedDb(deletes),
      sweepWebStorage('local-storage', deletes),
      sweepWebStorage('session-storage', deletes),
    ]);
  }
}

/** Decides whether a matched name in an area is deleted. */
type TmSweepPredicate = (name: string, area: TmClientCacheArea) => boolean;

/** Deletes the `tm-*` CacheStorage caches. */
async function sweepCacheStorage(deletes: TmSweepPredicate): Promise<void> {
  // Absent in an insecure context or a restrictive browser profile.
  if (typeof caches === 'undefined') {
    return;
  }
  const names = await caches.keys();
  await Promise.allSettled(
    names
      .filter((name) => name.startsWith('tm-') && deletes(name, 'cache-storage'))
      .map((name) => caches.delete(name)),
  );
}

/** Deletes the `tm-*` IndexedDB databases. */
async function sweepIndexedDb(deletes: TmSweepPredicate): Promise<void> {
  // Without databases() there is no way to enumerate, so the area is skipped.
  if (typeof indexedDB === 'undefined' || typeof indexedDB.databases !== 'function') {
    return;
  }
  const databases = await indexedDB.databases();
  await Promise.allSettled(
    databases
      .map((database) => database.name)
      .filter(
        (name): name is string =>
          name !== undefined && name.startsWith('tm-') && deletes(name, 'indexeddb'),
      )
      .map(
        (name) =>
          // Settles on success, error or blocked: a blocked delete stays
          // queued in the browser and completes once the blocker closes.
          new Promise<void>((resolve) => {
            const request = indexedDB.deleteDatabase(name);
            const settle = (): void => resolve();
            request.onsuccess = settle;
            request.onerror = settle;
            request.onblocked = settle;
          }),
      ),
  );
}

/** Deletes the `tm.*` keys of `localStorage` or of this tab's `sessionStorage`. */
async function sweepWebStorage(
  area: 'local-storage' | 'session-storage',
  deletes: TmSweepPredicate,
): Promise<void> {
  // The getter itself throws where storage is blocked.
  const storage = area === 'local-storage' ? globalThis.localStorage : globalThis.sessionStorage;
  if (storage === undefined) {
    return;
  }
  // Collected first: removing while iterating by index would skip keys.
  const keys: string[] = [];
  for (let index = 0; index < storage.length; index++) {
    const key = storage.key(index);
    if (key !== null && key.startsWith('tm.')) {
      keys.push(key);
    }
  }
  for (const key of keys) {
    if (deletes(key, area)) {
      storage.removeItem(key);
    }
  }
}
