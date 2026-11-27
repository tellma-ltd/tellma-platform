// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ErrorHandler } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TmClientCache, type TmClientCacheArea, type TmClientCacheClearOptions } from '@tellma/core-ui';

import { provideTellmaStateTesting } from './provide-tellma-state-testing';
import { provideTellmaTesting } from './provide-tellma-testing';
import { TmFakeClientCache } from './tm-fake-client-cache';

// The fake against the real cache: both sweep the same names with the same
// keep, the real one in the browser's own stores of this test page (a secure
// context, so every area is there). Every name the spec creates carries a
// "fake-cache" marker, and the clean-up below removes what a failed test left.

/** The names the spec creates in each area: matching and non-matching ones. */
const NAMES: Readonly<Record<TmClientCacheArea, readonly string[]>> = {
  'cache-storage': ['tm-fake-cache-a', 'tm-fake-cache-b', 'tm-fake-cache-throws', 'tm.fake-cache-dot', 'ngsw:/:db:fake-cache'],
  indexeddb: ['tm-fake-cache-a', 'tm-fake-cache-b', 'not-tm-fake-cache'],
  'local-storage': ['tm.fake-cache-a', 'tm.fake-cache-b', 'tm-fake-cache-dash', 'not-tm.fake-cache'],
  'session-storage': ['tm.fake-cache-a', 'tm.fake-cache-b', 'tm.fake-cache-throws', 'tm-fake-cache-dash'],
};

/** The areas, in order. */
const AREAS: readonly TmClientCacheArea[] = ['cache-storage', 'indexeddb', 'local-storage', 'session-storage'];

/** Settles an IndexedDB request: its result, or `undefined` on an error or a blocked delete. */
function settle<T>(request: IDBRequest<T>): Promise<T | undefined> {
  return new Promise((resolve) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => resolve(undefined);
    if (request instanceof IDBOpenDBRequest) {
      request.onblocked = () => resolve(undefined);
    }
  });
}

/** The web storage of an area. */
function webStorage(area: 'local-storage' | 'session-storage'): Storage {
  return area === 'local-storage' ? localStorage : sessionStorage;
}

/** Creates the spec's names in the browser's own stores. */
async function seedBrowser(): Promise<void> {
  for (const name of NAMES['cache-storage']) {
    await caches.open(name);
  }
  for (const name of NAMES.indexeddb) {
    (await settle(indexedDB.open(name)))?.close();
  }
  for (const area of ['local-storage', 'session-storage'] as const) {
    for (const key of NAMES[area]) {
      webStorage(area).setItem(key, 'value');
    }
  }
}

/** Which of the spec's names the browser's own stores still hold, by area. */
async function browserNames(): Promise<Record<TmClientCacheArea, string[]>> {
  const caching = await caches.keys();
  const databases = (await indexedDB.databases()).map((database) => database.name);
  return {
    'cache-storage': NAMES['cache-storage'].filter((name) => caching.includes(name)),
    indexeddb: NAMES.indexeddb.filter((name) => databases.includes(name)),
    'local-storage': NAMES['local-storage'].filter((key) => localStorage.getItem(key) !== null),
    'session-storage': NAMES['session-storage'].filter((key) => sessionStorage.getItem(key) !== null),
  };
}

/** Which names the fake still holds, by area. */
function fakeNames(cache: TmFakeClientCache): Record<TmClientCacheArea, readonly string[]> {
  return {
    'cache-storage': cache.names('cache-storage'),
    indexeddb: cache.names('indexeddb'),
    'local-storage': cache.names('local-storage'),
    'session-storage': cache.names('session-storage'),
  };
}

/** An error handler that records what it is handed. */
class TmRecordingErrorHandler extends ErrorHandler {
  /** The errors, in order. */
  readonly errors: unknown[] = [];

  /** Records an error. */
  override handleError(error: unknown): void {
    this.errors.push(error);
  }
}

describe('TmFakeClientCache', () => {
  afterEach(async () => {
    // Removes whatever the browser still holds of the spec's names.
    for (const name of NAMES['cache-storage']) {
      await caches.delete(name);
    }
    for (const name of NAMES.indexeddb) {
      await settle(indexedDB.deleteDatabase(name));
    }
    for (const area of ['local-storage', 'session-storage'] as const) {
      for (const key of NAMES[area]) {
        webStorage(area).removeItem(key);
      }
    }
  });

  it('sweeps what the real cache sweeps, keeping what the same keep keeps and deleting what it throws on', async () => {
    const failure = new Error('keep failed');
    const realAsked: string[] = [];
    const fakeAsked: string[] = [];
    // Keeps one name per area, and throws on two.
    const keep = (log: string[]): TmClientCacheClearOptions => ({
      keep: (name, area) => {
        log.push(`${area}:${name}`);
        if (name.endsWith('-throws')) {
          throw failure;
        }
        return name.endsWith('-b') && area !== 'indexeddb';
      },
    });

    // The real cache, over the browser's own stores.
    const realErrors = new TmRecordingErrorHandler();
    TestBed.configureTestingModule({ providers: [{ provide: ErrorHandler, useValue: realErrors }] });
    await seedBrowser();
    await TestBed.inject(TmClientCache).clearAll(keep(realAsked));
    const real = await browserNames();
    TestBed.resetTestingModule();

    // The fake, over the same names.
    const fakeErrors = new TmRecordingErrorHandler();
    TestBed.configureTestingModule({ providers: [provideTellmaTesting(), { provide: ErrorHandler, useValue: fakeErrors }] });
    const cache = TestBed.inject(TmFakeClientCache);
    for (const area of AREAS) {
      cache.add(area, ...NAMES[area]);
    }
    await TestBed.inject(TmClientCache).clearAll(keep(fakeAsked));

    expect(fakeNames(cache)).toEqual(real);
    // The real sweep visits the areas in parallel, so only the set of names asked about is compared.
    expect(fakeAsked.slice().sort()).toEqual(realAsked.filter((entry) => entry.includes('fake-cache')).sort());
    expect([realErrors.errors, fakeErrors.errors]).toEqual([
      [failure, failure],
      [failure, failure],
    ]);
    // And the outcome itself, so the comparison cannot pass on two sweeps that both did nothing.
    expect(real).toEqual({
      'cache-storage': ['tm-fake-cache-b', 'tm.fake-cache-dot', 'ngsw:/:db:fake-cache'],
      indexeddb: ['not-tm-fake-cache'],
      'local-storage': ['tm.fake-cache-b', 'tm-fake-cache-dash', 'not-tm.fake-cache'],
      'session-storage': ['tm.fake-cache-b', 'tm-fake-cache-dash'],
    });
  });

  it('records what each sweep deleted, by area, and sweeps everything matching with no keep', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaTesting()] });
    const cache = TestBed.inject(TmFakeClientCache);
    cache.add('indexeddb', 'tm-app', 'tm-old', 'tm-app');
    cache.add('local-storage', 'tm.boot', 'other');
    await cache.clearAll({ keep: (name) => name === 'tm-app' });
    await cache.clearAll();

    expect(cache.names('indexeddb')).toEqual([]);
    expect(cache.names('local-storage')).toEqual(['other']);
    expect(cache.sweeps()).toEqual([
      { deleted: { 'cache-storage': [], indexeddb: ['tm-old'], 'local-storage': ['tm.boot'], 'session-storage': [] } },
      { deleted: { 'cache-storage': [], indexeddb: ['tm-app'], 'local-storage': [], 'session-storage': [] } },
    ]);
  });

  it("settles before the page's next task, and leaves the browser's own stores alone", async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaTesting(), provideTellmaStateTesting()] });
    localStorage.setItem('tm.fake-cache-a', 'value');
    TestBed.inject(TmFakeClientCache).add('local-storage', 'tm.fake-cache-a');

    expect(TestBed.inject(TmClientCache)).toBe(TestBed.inject(TmFakeClientCache));
    const sweep = TestBed.inject(TmClientCache).clearAll();
    const settledFirst = await Promise.race([
      sweep.then(() => true),
      new Promise<boolean>((resolve) => setTimeout(() => resolve(false))),
    ]);

    expect(settledFirst).toBe(true);
    expect(TestBed.inject(TmFakeClientCache).names('local-storage')).toEqual([]);
    expect(localStorage.getItem('tm.fake-cache-a')).toBe('value');
  });

  it('holds each sweep open until clearResult settles, deleting at once and never rejecting', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaTesting()] });
    const cache = TestBed.inject(TmFakeClientCache);
    cache.add('cache-storage', 'tm-images-v1');
    let release: (reason: unknown) => void = () => undefined;
    cache.clearResult = new Promise<void>((_resolve, reject) => (release = reject));
    let settled = false;

    const sweep = cache.clearAll().then(() => (settled = true));
    await new Promise((resolve) => setTimeout(resolve));
    expect([settled, cache.names('cache-storage'), cache.sweeps().length]).toEqual([false, [], 1]);

    release(new Error('held'));
    await expect(sweep).resolves.toBe(true);
  });
});
