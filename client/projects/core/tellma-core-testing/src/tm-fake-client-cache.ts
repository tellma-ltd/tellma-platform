// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ErrorHandler, inject, Injectable, signal, type Signal } from '@angular/core';
import type { TmClientCache, TmClientCacheArea, TmClientCacheClearOptions } from '@tellma/core-ui';

// The browser's stores, as the global cleanup hook sees them, kept in memory:
// each area is a list of names the test adds, and a sweep deletes the names
// the naming rule matches, minus what its caller keeps, without reaching any
// browser storage. A sweep therefore settles in microtasks, which neither a
// task turn nor a fake clock has to wait for, and a test never sweeps the
// stores of the page that every spec of the run shares.

/**
 * Every public member of `TmClientCache`. The fake implements all of them, so
 * a member added to the cache breaks the build here instead of failing at run
 * time in a test that calls it.
 */
type TmClientCacheSurface = { [K in keyof TmClientCache]: TmClientCache[K] };

/** The areas, in the order a sweep visits them. */
const AREAS: readonly TmClientCacheArea[] = ['cache-storage', 'indexeddb', 'local-storage', 'session-storage'];

/**
 * Whether a name falls under its area's naming rule: `tm-…` for a
 * CacheStorage cache or an IndexedDB database, `tm.…` for a web-storage key.
 * It is the real cache's rule; the fake's spec checks the two against each
 * other in the browser.
 */
function matches(name: string, area: TmClientCacheArea): boolean {
  return name.startsWith(area === 'cache-storage' || area === 'indexeddb' ? 'tm-' : 'tm.');
}

/** One sweep the fake ran. */
export interface TmFakeClientCacheSweep {
  /** The names the sweep deleted, by area, each list in the order the names were added. */
  readonly deleted: Readonly<Record<TmClientCacheArea, readonly string[]>>;
}

/**
 * A `TmClientCache` over stores kept in memory: the test adds the names each
 * area holds, and {@link TmFakeClientCache.clearAll} deletes those the naming
 * rule matches (`tm-…` caches and databases, `tm.…` web-storage keys), minus
 * the ones its `keep` returns `true` for, as the real sweep does. It reaches
 * no browser storage, so a sweep settles in microtasks and never touches
 * another test's stores. Part of `provideTellmaTesting()` and
 * `provideTellmaStateTesting()`; a test that needs the browser's own stores
 * swept provides `TmClientCache` itself, after them.
 *
 * @example
 * ```ts
 * const cache = TestBed.inject(TmFakeClientCache);
 * cache.add('indexeddb', 'tm-app', 'tm-old');
 * await TestBed.inject(TmClientCache).clearAll({ keep: (name) => name === 'tm-app' });
 * expect(cache.names('indexeddb')).toEqual(['tm-app']);
 * expect(cache.sweeps()[0]?.deleted.indexeddb).toEqual(['tm-old']);
 * ```
 */
@Injectable()
export class TmFakeClientCache implements TmClientCacheSurface {
  /** Where a throwing `keep` is reported, as the real cache reports it. */
  readonly #errorHandler = inject(ErrorHandler);
  /** The names each area holds, in the order added. */
  readonly #stores: Record<TmClientCacheArea, string[]> = { 'cache-storage': [], indexeddb: [], 'local-storage': [], 'session-storage': [] };
  /** Every sweep run so far, oldest first: the writable side of {@link TmFakeClientCache.sweeps}. */
  readonly #sweeps = signal<readonly TmFakeClientCacheSweep[]>([]);

  /** Every sweep run so far, oldest first. */
  readonly sweeps: Signal<readonly TmFakeClientCacheSweep[]> = this.#sweeps.asReadonly();

  /**
   * What each sweep waits for, once its deletes are done, before it
   * settles: settled at once by default. A test that holds it open checks
   * what waits for the sweep.
   */
  clearResult: Promise<void> = Promise.resolve();

  /**
   * Adds stores to an area, as the features that create them would. A name
   * the area already holds is not added again.
   *
   * @param area - The area.
   * @param names - The cache, database or key names.
   */
  add(area: TmClientCacheArea, ...names: string[]): void {
    const held = this.#stores[area];
    for (const name of names) {
      if (!held.includes(name)) {
        held.push(name);
      }
    }
  }

  /**
   * The names an area holds now.
   *
   * @param area - The area.
   * @returns The names, in the order added.
   */
  names(area: TmClientCacheArea): readonly string[] {
    return [...this.#stores[area]];
  }

  /**
   * Deletes, in every area, the names the naming rule matches, minus those
   * `keep` returns `true` for; `keep` is asked once per matching name, and a
   * `keep` that throws is reported to the `ErrorHandler` and that name
   * deleted. The deletes are done, and the sweep recorded, before the call
   * returns. Never throws.
   *
   * @param options - What to keep.
   * @returns A promise that settles when {@link TmFakeClientCache.clearResult} does, fulfilled even when it rejects.
   */
  async clearAll(options?: TmClientCacheClearOptions): Promise<void> {
    const deleted: Record<TmClientCacheArea, readonly string[]> = { 'cache-storage': [], indexeddb: [], 'local-storage': [], 'session-storage': [] };
    for (const area of AREAS) {
      const held = this.#stores[area];
      const gone = held.filter((name) => matches(name, area) && this.#deletes(name, area, options));
      this.#stores[area] = held.filter((name) => !gone.includes(name));
      deleted[area] = gone;
    }
    this.#sweeps.update((sweeps) => [...sweeps, { deleted }]);
    // The real sweep never rejects, so neither does a held one.
    await this.clearResult.catch(() => undefined);
  }

  /** Whether the sweep deletes a matching name, as the real cache decides it: a throwing `keep` deletes. */
  #deletes(name: string, area: TmClientCacheArea, options: TmClientCacheClearOptions | undefined): boolean {
    try {
      return options?.keep?.(name, area) !== true;
    } catch (error) {
      this.#errorHandler.handleError(error);
      return true;
    }
  }
}
