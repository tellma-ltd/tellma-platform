// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TmFakeStorage } from '../tm-storage.testing.util';
import type { TmStoreHost } from './tm-store-host';
import { TM_STORE_DATABASE, TM_STORE_DOCS, TM_STORE_KEY_PATH, TM_STORE_VERSION } from './tm-store-layout';

// The client store's browser, faked for the specs, and direct access to the
// real IndexedDB database, so a spec lays out records and reads the keys left
// without going through the store under test.

/**
 * A store host with no browser storage.
 *
 * @param localStorage - The local storage the boot record lives in.
 * @returns The host.
 */
export function tmMemoryStoreHost(localStorage: TmFakeStorage = new TmFakeStorage()): TmStoreHost {
  return {
    indexedDB: null,
    localStorage,
    caches: null,
    events: null,
    documentLanguage: () => ({ lang: 'en', dir: 'ltr' }),
    persist: () => undefined,
  };
}

/** One record of the `docs` store, as a spec writes it directly. */
export interface TmRawRecord {
  /** The partition's key. */
  readonly p: string;
  /** The stored name. */
  readonly n: string;
  /** The wire tag. */
  readonly tag: string | null;
  /** When the record was saved. */
  readonly savedAt: number;
  /** The document's value. */
  readonly data: unknown;
}

/**
 * Settles one IndexedDB request.
 *
 * @param request - The request.
 * @returns Its result.
 */
export function tmSettle<T>(request: IDBRequest<T> | IDBOpenDBRequest): Promise<T> {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result as T);
    request.onerror = () => reject(request.error);
  });
}

/**
 * Deletes the store's database; every open connection yields to it.
 *
 * @returns A promise that resolves once it is gone.
 */
export async function tmDeleteStoreDatabase(): Promise<void> {
  await tmSettle(indexedDB.deleteDatabase(TM_STORE_DATABASE));
}

/** Opens the store's database directly, creating the `docs` store when absent. */
async function openRaw(): Promise<IDBDatabase> {
  const request = indexedDB.open(TM_STORE_DATABASE, TM_STORE_VERSION);
  request.onupgradeneeded = () => request.result.createObjectStore(TM_STORE_DOCS, { keyPath: [...TM_STORE_KEY_PATH] });
  return tmSettle(request);
}

/**
 * Writes records into the store's database directly.
 *
 * @param records - The records.
 * @returns A promise that resolves once they commit.
 */
export async function tmWriteRawRecords(records: readonly TmRawRecord[]): Promise<void> {
  const db = await openRaw();
  const transaction = db.transaction(TM_STORE_DOCS, 'readwrite');
  for (const record of records) {
    transaction.objectStore(TM_STORE_DOCS).put(record);
  }
  await new Promise((resolve, reject) => {
    transaction.oncomplete = resolve;
    transaction.onabort = () => reject(transaction.error);
  });
  db.close();
}

/**
 * Reads every record of the store's database directly, in key order.
 *
 * @returns The records.
 */
export async function tmReadRawRecords(): Promise<TmRawRecord[]> {
  const db = await openRaw();
  const records = await tmSettle(db.transaction(TM_STORE_DOCS).objectStore(TM_STORE_DOCS).getAll());
  db.close();
  return records as TmRawRecord[];
}

/**
 * Reads the keys left in the store's database, in key order.
 *
 * @returns The keys, as `[p, n]` pairs.
 */
export async function tmReadRawKeys(): Promise<[string, string][]> {
  return (await tmReadRawRecords()).map(({ p, n }) => [p, n]);
}
