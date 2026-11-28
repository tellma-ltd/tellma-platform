// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, makeEnvironmentProviders, type EnvironmentProviders, type Provider } from '@angular/core';
import {
  TmSession,
  TmTabBus,
  ɵTM_DRAFTS_STORAGE,
  ɵTM_LEADER_LOAD,
  ɵTM_STORE_HOST,
  ɵTM_UPDATES_LOAD,
  ɵTM_ZONE_CHECK_LOAD,
  ɵTmLocks,
  type ɵTmStoreHost,
} from '@tellma/core';
import { ɵTM_HUB_FACTORY } from '@tellma/core/leader';
import { TmClientCache } from '@tellma/core-ui';

import { TmFakeClientCache } from './tm-fake-client-cache';
import { TmFakeHub } from './tm-fake-hub';
import { TmFakeLocks } from './tm-fake-locks';
import { TmFakeSession } from './tm-fake-session';
import { TmFakeTabBus } from './tm-fake-tab-bus';

// The client store over no browser storage: no IndexedDB, so the store runs
// on its mirror alone, a boot record kept in a map, and drafts kept in
// another, with locks kept in memory too, and the session's clean-up sweeps
// stores kept in memory as well. A test therefore starts with an empty store,
// leaves nothing behind for the next one, and never sweeps the stores of the
// page the run's specs share. A test that runs the leader election itself
// gets a fake realtime connection.

/** A web storage held in a map. */
class TmMapStorage {
  readonly #items = new Map<string, string>();

  /** The number of items. */
  get length(): number {
    return this.#items.size;
  }

  /** The key at an index, or `null` past the end. */
  key(index: number): string | null {
    return [...this.#items.keys()][index] ?? null;
  }

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
    // In memory by design: the store opens nothing and reports nothing.
    memory: true,
    indexedDB: null,
    localStorage: new TmMapStorage(),
    caches: null,
    events: null,
    documentLanguage: () => ({ lang: 'en', dir: 'ltr' }),
    persist: () => undefined,
  };
}

/**
 * The providers of the client's storage in memory: `TmClientStore` keeps
 * every document in its mirror and the boot record in memory, and
 * `TmClientCache` is a {@link TmFakeClientCache}, which sweeps stores kept in
 * memory, so no test reads, writes or sweeps the browser's IndexedDB,
 * CacheStorage or web storage. Part of both `provideTellmaTesting()` and
 * {@link provideTellmaStateTesting}.
 *
 * @returns The providers.
 */
export function tmMemoryStorageProviders(): Provider[] {
  return [
    { provide: ɵTM_STORE_HOST, useFactory: memoryStoreHost },
    TmFakeClientCache,
    { provide: TmClientCache, useExisting: TmFakeClientCache },
  ];
}

/**
 * The test providers of the client state: an in-memory `TmClientStore`, a
 * {@link TmFakeTabBus} in place of `TmTabBus`, {@link TmFakeLocks} in place
 * of the browser's locks, a {@link TmFakeSession} in place of `TmSession`,
 * signed in and active, so the store's write guard passes until the test
 * changes the session, a {@link TmFakeClientCache} in place of
 * `TmClientCache`, so the session's clean-up sweeps stores kept in memory
 * and settles in microtasks, and `@tellma/core/drafts`' `TmDrafts` over a
 * session storage of its own, so no draft outlives the test. A test app starts no
 * leader election, since it has no other tabs and no hub: `TmLeader` reports
 * a follower until the test sets it; a test that starts the election itself
 * finds the leader's realtime connections in {@link TmFakeHub}. Nor does it
 * run a service worker: the update checks find updates disabled. Nor does
 * it check the device's time zone, which a test app never sees move:
 * `TmDeviceZone` keeps its option, and its check does nothing. Use it
 * beside `provideTellmaTesting()`.
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
    ...tmMemoryStorageProviders(),
    { provide: ɵTM_DRAFTS_STORAGE, useFactory: () => new TmMapStorage() },
    TmFakeTabBus,
    { provide: TmTabBus, useExisting: TmFakeTabBus },
    TmFakeLocks,
    { provide: ɵTmLocks, useExisting: TmFakeLocks },
    TmFakeSession,
    { provide: TmSession, useExisting: TmFakeSession },
    TmFakeHub,
    { provide: ɵTM_HUB_FACTORY, useFactory: () => inject(TmFakeHub).factory },
    { provide: ɵTM_LEADER_LOAD, useValue: () => Promise.resolve(null) },
    { provide: ɵTM_UPDATES_LOAD, useValue: () => Promise.resolve(null) },
    { provide: ɵTM_ZONE_CHECK_LOAD, useValue: () => Promise.resolve(null) },
  ]);
}
