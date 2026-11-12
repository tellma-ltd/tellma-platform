// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_IMAGES_CACHE, TM_STORE_DATABASE, TM_STORE_DOCS } from '../state/store/tm-store-layout';
import vectors from '../state/store/tm-store-layout-vectors.json';
import { tmDeleteExpiredUser } from './tm-boot-expiry';

// The expiry delete over the browser's real IndexedDB and CacheStorage, with
// the store laid out as the shared vectors lay it out.

/** Settles one IndexedDB request. */
function settle<T>(request: IDBRequest<T> | IDBOpenDBRequest): Promise<T> {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result as T);
    request.onerror = () => reject(request.error);
  });
}

/** Deletes the store's database, so each case starts with none. */
function deleteDatabase(): Promise<unknown> {
  return settle(indexedDB.deleteDatabase(TM_STORE_DATABASE));
}

/** Creates the store's database with the vector documents in it. */
async function seed(): Promise<void> {
  const request = indexedDB.open(TM_STORE_DATABASE, 1);
  request.onupgradeneeded = () => request.result.createObjectStore(TM_STORE_DOCS, { keyPath: ['p', 'n'] });
  const db = await settle(request);
  const transaction = db.transaction(TM_STORE_DOCS, 'readwrite');
  for (const [p, n] of vectors.documents) {
    transaction.objectStore(TM_STORE_DOCS).put({ p, n, value: 1 });
  }
  await new Promise((resolve) => (transaction.oncomplete = resolve));
  db.close();
}

/** The keys left in the store's database. */
async function remainingKeys(): Promise<IDBValidKey[]> {
  const db = await settle(indexedDB.open(TM_STORE_DATABASE));
  const keys = await settle(db.transaction(TM_STORE_DOCS).objectStore(TM_STORE_DOCS).getAllKeys());
  db.close();
  return keys;
}

/** A time box that never ends. */
const NEVER = new Promise<void>(() => undefined);

describe('tmDeleteExpiredUser', () => {
  beforeEach(deleteDatabase);
  afterAll(deleteDatabase);

  for (const vector of vectors.userDeletes) {
    it(`deletes the user's documents by the store's layout: ${vector.name}`, async () => {
      await seed();
      await tmDeleteExpiredUser(indexedDB, null, vector.sub, NEVER);
      expect(await remainingKeys()).toEqual(vector.remaining);
    });
  }

  it('deletes the image cache', async () => {
    await (await caches.open(TM_IMAGES_CACHE)).put('/img/a.png', new Response('a'));
    expect(await caches.has(TM_IMAGES_CACHE)).toBe(true);
    await tmDeleteExpiredUser(null, caches, '0b4f', NEVER);
    expect(await caches.has(TM_IMAGES_CACHE)).toBe(false);
  });

  it('creates no database when there is none', async () => {
    await tmDeleteExpiredUser(indexedDB, null, '0b4f', NEVER);
    const names = (await indexedDB.databases()).map((database) => database.name);
    expect(names).not.toContain(TM_STORE_DATABASE);
  });

  it('finishes when the database holds no document store', async () => {
    (await settle(indexedDB.open(TM_STORE_DATABASE, 1))).close();
    await expect(tmDeleteExpiredUser(indexedDB, null, '0b4f', NEVER)).resolves.toBeDefined();
  });

  it('yields its connection to a delete of the database instead of blocking it', async () => {
    await seed();
    const run = tmDeleteExpiredUser(indexedDB, null, '0b4f', NEVER);
    // Queued behind the expiry's open, the delete reaches its open connection.
    const request = indexedDB.deleteDatabase(TM_STORE_DATABASE);
    let blocked = false;
    request.onblocked = () => (blocked = true);
    await settle(request);
    await run;
    expect(blocked).toBe(false);
  });

  it('settles only after its delete commits when an upgrade asks for its connection', async () => {
    await seed();
    const events: string[] = [];
    let upgraded!: Promise<IDBDatabase>;
    // The moment the expiry starts its delete, another tab asks to upgrade
    // the database: the expiry's connection receives `versionchange` while
    // the delete is still running.
    const original = IDBDatabase.prototype.transaction;
    const spy = vi
      .spyOn(IDBDatabase.prototype, 'transaction')
      .mockImplementation(function (this: IDBDatabase, ...args: Parameters<IDBDatabase['transaction']>) {
        const transaction = original.apply(this, args);
        transaction.addEventListener('complete', () => events.push('committed'));
        transaction.addEventListener('abort', () => events.push('aborted'));
        const request = indexedDB.open(TM_STORE_DATABASE, 2);
        request.onupgradeneeded = () => events.push('upgraded');
        upgraded = settle(request);
        return transaction;
      });
    try {
      await tmDeleteExpiredUser(indexedDB, null, '0b4f', NEVER).then(() => events.push('settled'));
    } finally {
      spy.mockRestore();
    }
    (await upgraded).close();
    expect(events).toEqual(['committed', 'settled', 'upgraded']);
  });

  it('ends when the time box ends, even while the delete hangs', async () => {
    const hanging = { open: () => ({}) } as unknown as IDBFactory;
    const caching = { delete: () => NEVER.then(() => true) };
    let endBox!: () => void;
    const box = new Promise<void>((resolve) => (endBox = resolve));
    let ended = false;
    const run = tmDeleteExpiredUser(hanging, caching, '0b4f', box).then(() => (ended = true));
    await Promise.resolve();
    expect(ended).toBe(false);
    endBox();
    await run;
    expect(ended).toBe(true);
  });

  it('swallows every failure', async () => {
    const throwing = {
      open: () => {
        throw new DOMException('The operation is insecure.', 'SecurityError');
      },
    } as unknown as IDBFactory;
    const refusing = {
      delete: (): Promise<boolean> => {
        throw new DOMException('The operation is insecure.', 'SecurityError');
      },
    };
    const rejecting = { delete: () => Promise.reject(new Error('no cache access')) };
    await expect(tmDeleteExpiredUser(throwing, refusing, '0b4f', NEVER)).resolves.toBeDefined();
    await expect(tmDeleteExpiredUser(null, rejecting, '0b4f', NEVER)).resolves.toBeDefined();
  });
});
