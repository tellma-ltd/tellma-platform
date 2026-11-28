// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ErrorHandler } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { TmClientCache, type TmClientCacheArea } from './tm-client-cache';

// Browser-mode vitest serves from localhost, a secure context, so the real
// CacheStorage, IndexedDB and web storage are available and every sweep is
// tested end to end. Every name the tests create carries a "test" marker,
// and the cleanup below removes whatever a failed test left behind.

/** The CacheStorage caches the tests create. */
const TEST_CACHES = ['tm-test-a', 'tm-test-b', 'not-tm-test', 'ngsw:/:db:test', 'tm.test-cache'];

/** The IndexedDB databases the tests create. */
const TEST_DATABASES = ['tm-test-db-a', 'tm-test-db-b', 'not-tm-test-db', 'tm-test-blocked'];

/** The web-storage keys the tests create, in both storages. */
const TEST_KEYS = ['tm.test-a', 'tm.test-b', 'not-tm.test', 'tm-test-dash'];

/** Creates an (empty) IndexedDB database and closes the connection. */
async function createDatabase(name: string): Promise<void> {
  const db = await openDatabase(name);
  db.close();
}

/** Opens an IndexedDB database, creating it when missing. */
function openDatabase(name: string): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(name);
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

/** Deletes an IndexedDB database, waiting for the delete to finish. */
function deleteDatabase(name: string): Promise<void> {
  return new Promise((resolve) => {
    const request = indexedDB.deleteDatabase(name);
    request.onsuccess = () => resolve();
    request.onerror = () => resolve();
  });
}

/** The names of the databases this origin holds. */
async function databaseNames(): Promise<string[]> {
  return (await indexedDB.databases()).map((database) => database.name ?? '');
}

/** Creates one store of each kind: matching and non-matching names. */
async function seedEveryArea(): Promise<void> {
  for (const name of TEST_CACHES) {
    await caches.open(name);
  }
  for (const name of TEST_DATABASES.filter((database) => database !== 'tm-test-blocked')) {
    await createDatabase(name);
  }
  for (const key of TEST_KEYS) {
    localStorage.setItem(key, 'value');
    sessionStorage.setItem(key, 'value');
  }
}

/** Resolves `true` when `promise` settles within `ms`, else `false`. */
async function settlesWithin(promise: Promise<unknown>, ms: number): Promise<boolean> {
  return Promise.race([
    promise.then(() => true),
    new Promise<boolean>((resolve) => setTimeout(() => resolve(false), ms)),
  ]);
}

describe('TmClientCache', () => {
  afterEach(async () => {
    for (const name of TEST_CACHES) {
      await caches.delete(name);
    }
    for (const name of TEST_DATABASES) {
      await deleteDatabase(name);
    }
    for (const key of TEST_KEYS) {
      localStorage.removeItem(key);
      sessionStorage.removeItem(key);
    }
  });

  it('clearAll deletes the matching stores of every area and nothing else', async () => {
    await seedEveryArea();

    await TestBed.inject(TmClientCache).clearAll();

    // CacheStorage: tm-* caches go; the service worker's and others stay.
    const cacheNames = await caches.keys();
    expect(cacheNames).not.toContain('tm-test-a');
    expect(cacheNames).not.toContain('tm-test-b');
    expect(cacheNames).toEqual(
      expect.arrayContaining(['not-tm-test', 'ngsw:/:db:test', 'tm.test-cache']),
    );
    // IndexedDB: tm-* databases go.
    const databases = await databaseNames();
    expect(databases).not.toContain('tm-test-db-a');
    expect(databases).not.toContain('tm-test-db-b');
    expect(databases).toContain('not-tm-test-db');
    // Web storage: tm.* keys go, in both storages; a tm- key is not tm.*.
    for (const storage of [localStorage, sessionStorage]) {
      expect(storage.getItem('tm.test-a')).toBeNull();
      expect(storage.getItem('tm.test-b')).toBeNull();
      expect(storage.getItem('not-tm.test')).toBe('value');
      expect(storage.getItem('tm-test-dash')).toBe('value');
    }
  });

  it('keep spares the names it returns true for, per area', async () => {
    await seedEveryArea();
    const asked: string[] = [];

    await TestBed.inject(TmClientCache).clearAll({
      keep: (name, area) => {
        asked.push(`${area}:${name}`);
        return (
          (area === 'cache-storage' && name === 'tm-test-a') ||
          (area === 'indexeddb' && name === 'tm-test-db-a') ||
          (area === 'local-storage' && name === 'tm.test-a') ||
          (area === 'session-storage' && name === 'tm.test-b')
        );
      },
    });

    const cacheNames = await caches.keys();
    expect(cacheNames).toContain('tm-test-a');
    expect(cacheNames).not.toContain('tm-test-b');
    const databases = await databaseNames();
    expect(databases).toContain('tm-test-db-a');
    expect(databases).not.toContain('tm-test-db-b');
    expect(localStorage.getItem('tm.test-a')).toBe('value');
    expect(localStorage.getItem('tm.test-b')).toBeNull();
    expect(sessionStorage.getItem('tm.test-a')).toBeNull();
    expect(sessionStorage.getItem('tm.test-b')).toBe('value');
    // Asked only about names the rule matched, each with its own area.
    expect(asked).toEqual(
      expect.arrayContaining<string>([
        'cache-storage:tm-test-a',
        'cache-storage:tm-test-b',
        'indexeddb:tm-test-db-a',
        'indexeddb:tm-test-db-b',
        'local-storage:tm.test-a',
        'local-storage:tm.test-b',
        'session-storage:tm.test-a',
        'session-storage:tm.test-b',
      ]),
    );
    expect(asked.some((entry) => /not-tm|ngsw|tm\.test-cache|tm-test-dash/.test(entry))).toBe(
      false,
    );
  });

  it('a delete blocked by a connection that ignores versionchange still resolves', async () => {
    // The blocker never closes on versionchange, so the delete stays queued.
    const blocker = await openDatabase('tm-test-blocked');
    blocker.onversionchange = null;

    const sweep = TestBed.inject(TmClientCache).clearAll();
    expect(await settlesWithin(sweep, 5000)).toBe(true);

    // The queued delete completes once the blocker closes.
    blocker.close();
    await expect.poll(databaseNames).not.toContain('tm-test-blocked');
  });

  it('skips IndexedDB where databases() is missing, and still sweeps the other areas', async () => {
    await seedEveryArea();
    // Shadows the prototype method on the instance, as an older browser lacks it.
    Object.defineProperty(indexedDB, 'databases', { value: undefined, configurable: true });
    try {
      await TestBed.inject(TmClientCache).clearAll();
    } finally {
      delete (indexedDB as { databases?: unknown }).databases;
    }

    expect(await databaseNames()).toContain('tm-test-db-a');
    expect(await caches.keys()).not.toContain('tm-test-a');
    expect(localStorage.getItem('tm.test-a')).toBeNull();
    expect(sessionStorage.getItem('tm.test-a')).toBeNull();
  });

  it('never touches the service worker caches, even with no keep', async () => {
    await caches.open('ngsw:/:db:test');
    await TestBed.inject(TmClientCache).clearAll();
    expect(await caches.keys()).toContain('ngsw:/:db:test');
  });

  it('a throwing keep is reported, and the sweep deletes that name and resolves', async () => {
    await seedEveryArea();
    const reported: unknown[] = [];
    TestBed.configureTestingModule({
      providers: [{ provide: ErrorHandler, useValue: { handleError: (e: unknown) => reported.push(e) } }],
    });
    const failure = new Error('keep failed');

    await expect(
      TestBed.inject(TmClientCache).clearAll({
        keep: (_name: string, area: TmClientCacheArea) => {
          if (area === 'local-storage') {
            throw failure;
          }
          return false;
        },
      }),
    ).resolves.toBeUndefined();

    expect(localStorage.getItem('tm.test-a')).toBeNull();
    expect(reported).toContain(failure);
  });

  it('clearAll resolves (never throws) when there is nothing to sweep', async () => {
    await expect(TestBed.inject(TmClientCache).clearAll()).resolves.toBeUndefined();
  });
});
