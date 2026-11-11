// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  TM_IMAGES_CACHE,
  TM_STORE_DATABASE,
  TM_STORE_DOCS,
  tmUserKeyBounds,
} from '../state/store/tm-store-layout';
import type { TmBootCaches } from './tm-boot-types';

// The expiry delete (spec 0024 §4.2 step 3c, spec 0025 §2.2). When a session
// has expired, the boot script deletes that user's stored data before the tab
// leaves to sign in, so the next user of the device never finds it. It deletes
// by the store's frozen layout: the user's key range in `tm-app`, and the
// image cache. The client store's boot step sweeps every user's data again
// whenever the boot record names no user, so a delete cut short by the time
// box is completed there.

/**
 * Deletes one user's documents from the client store. The database is opened
 * without a version, so an existing one opens as it is; a missing one would
 * be created, and the upgrade that reports `oldVersion = 0` is aborted
 * instead, so the delete never creates the store it deletes from.
 *
 * @param indexedDB - IndexedDB.
 * @param sub - The user's subject identifier.
 * @returns A promise that settles when the delete commits or fails.
 */
function deleteUserDocuments(indexedDB: IDBFactory, sub: string): Promise<void> {
  // An `open` that throws rejects this promise; the caller swallows it.
  return new Promise<void>((resolve) => {
    const request = indexedDB.open(TM_STORE_DATABASE);
    request.onupgradeneeded = (event) => {
      // No database yet: nothing to delete, and none to create.
      if (event.oldVersion === 0 && request.transaction) {
        request.transaction.abort();
      }
    };
    // An open without a version never upgrades an existing database, so it
    // is never blocked; the aborted upgrade above ends in an error.
    request.onerror = () => resolve();
    request.onsuccess = () => {
      const db = request.result;
      const done = (): void => {
        db.close();
        resolve();
      };
      try {
        const [lower, upper] = tmUserKeyBounds(sub);
        const transaction = db.transaction(TM_STORE_DOCS, 'readwrite');
        transaction.objectStore(TM_STORE_DOCS).delete(IDBKeyRange.bound(lower, upper));
        // A failed delete aborts the transaction, so these two end every run.
        transaction.oncomplete = done;
        transaction.onabort = done;
      } catch {
        // No `docs` store, or a transaction the database refuses.
        done();
      }
    };
  });
}

/**
 * Deletes an expired user's stored data, the user's documents and the image
 * cache, within a time box. Every failure is swallowed: the boot must go on.
 *
 * @param indexedDB - IndexedDB, or `null` when the browser refuses it.
 * @param caches - CacheStorage, or `null` when the browser lacks it.
 * @param sub - The expired user's subject identifier.
 * @param box - A promise that resolves when the time box ends.
 * @returns A promise that settles when the delete is done or the box ends, whichever is first.
 */
export function tmDeleteExpiredUser(
  indexedDB: IDBFactory | null,
  caches: TmBootCaches | null,
  sub: string,
  box: Promise<void>,
): Promise<unknown> {
  const ignore = (): void => undefined;
  const work: Promise<unknown>[] = [];
  if (indexedDB) {
    work.push(deleteUserDocuments(indexedDB, sub).catch(ignore));
  }
  if (caches) {
    // `delete` may throw at once (no cache access) or reject later.
    work.push(Promise.resolve().then(() => caches.delete(TM_IMAGES_CACHE)).catch(ignore));
  }
  return Promise.race([Promise.all(work), box]);
}
