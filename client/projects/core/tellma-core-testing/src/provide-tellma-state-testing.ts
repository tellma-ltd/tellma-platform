// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { makeEnvironmentProviders, type EnvironmentProviders, type Provider } from '@angular/core';
import { TmSession, TmTabBus, ɵTM_STORE_HOST, type ɵTmStoreHost } from '@tellma/core';

import { TmFakeSession } from './tm-fake-session';
import { TmFakeTabBus } from './tm-fake-tab-bus';

// The client store over no browser storage: no IndexedDB, so the store runs
// on its mirror alone, and a boot record kept in a map. A test therefore
// starts with an empty store and leaves nothing behind for the next one.

/** A local storage held in a map. */
class TmMapStorage {
  readonly #items = new Map<string, string>();

  /** Reads one item. */
  getItem(key: string): string | null {
    return this.#items.get(key) ?? null;
  }

  /** Writes one item. */
  setItem(key: string, value: string): void {
    this.#items.set(key, value);
  }

  /** Removes one item. */
  removeItem(key: string): void {
    this.#items.delete(key);
  }
}

/** The client store's browser in a test: no IndexedDB, CacheStorage or page events, and a fresh local storage. */
function memoryStoreHost(): ɵTmStoreHost {
  return {
    indexedDB: null,
    localStorage: new TmMapStorage(),
    caches: null,
    events: null,
    documentLanguage: () => ({ lang: 'en', dir: 'ltr' }),
    persist: () => undefined,
  };
}

/**
 * The provider of an in-memory client store: `TmClientStore` keeps every
 * document in its mirror and the boot record in memory, so no test reads or
 * writes the browser's IndexedDB or local storage. Part of both
 * `provideTellmaTesting()` and {@link provideTellmaStateTesting}.
 *
 * @returns The provider.
 */
export function tmMemoryStoreProvider(): Provider {
  return { provide: ɵTM_STORE_HOST, useFactory: memoryStoreHost };
}

/**
 * The test providers of the client state: an in-memory `TmClientStore`, a
 * {@link TmFakeTabBus} in place of `TmTabBus`, and a {@link TmFakeSession}
 * in place of `TmSession`, signed in and active, so the store's write guard
 * passes until the test changes the session. Use it beside
 * `provideTellmaTesting()`.
 *
 * @example
 * ```ts
 * TestBed.configureTestingModule({ providers: [provideTellmaTesting(), provideTellmaStateTesting()] });
 * const store = TestBed.inject(TmClientStore);
 * const user: TmPartition = { kind: 'user', sub: TestBed.inject(TmFakeSession).profile()!.sub };
 * await store.put(user, 'tenants', { tag: null, savedAt: Date.now(), data: [] });
 * expect(store.doc(user, 'tenants')()?.data).toEqual([]);
 * ```
 *
 * @returns The providers.
 */
export function provideTellmaStateTesting(): EnvironmentProviders {
  return makeEnvironmentProviders([
    tmMemoryStoreProvider(),
    TmFakeTabBus,
    { provide: TmTabBus, useExisting: TmFakeTabBus },
    TmFakeSession,
    { provide: TmSession, useExisting: TmFakeSession },
  ]);
}
