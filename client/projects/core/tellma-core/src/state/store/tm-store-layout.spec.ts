// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  TM_IMAGES_CACHE,
  TM_STORE_DATABASE,
  TM_STORE_DOCS,
  TM_STORE_KEY_PATH,
  TM_STORE_VERSION,
  TM_USERS_PREFIX,
  tmPartitionKey,
  tmPartitionKeyBounds,
  tmPrefixKeyBounds,
  tmSessionsPrefix,
  tmStoredName,
  tmUserKeyBounds,
  type TmPartition,
} from './tm-store-layout';
import vectors from './tm-store-layout-vectors.json';

// The store layout against its shared vectors: the boot script's expiry
// delete and the client store must agree on every key.

describe('the client store layout', () => {
  it('names the database, the store, the key path and the image cache as the vectors do', () => {
    expect({
      database: TM_STORE_DATABASE,
      version: TM_STORE_VERSION,
      store: TM_STORE_DOCS,
      keyPath: TM_STORE_KEY_PATH,
      imagesCache: TM_IMAGES_CACHE,
    }).toEqual(vectors.layout);
  });

  for (const vector of vectors.partitions) {
    it(`keys ${vector.name} as ${vector.p}`, () => {
      expect(tmPartitionKey(vector.partition as TmPartition)).toBe(vector.p);
    });
  }

  for (const vector of vectors.storedNames) {
    it(`stores ${vector.name} at format ${vector.format} as ${vector.n}`, () => {
      expect(tmStoredName(vector.name, vector.format)).toBe(vector.n);
    });
  }

  it('lists the vector documents in the order IndexedDB sorts their keys', () => {
    const sorted = [...vectors.documents].sort((a, b) => indexedDB.cmp(a, b));
    expect(sorted).toEqual(vectors.documents);
  });

  for (const vector of vectors.partitionReads) {
    it(`bounds one partition's documents: ${vector.name}`, () => {
      const range = IDBKeyRange.bound(...tmPartitionKeyBounds(vector.partition as TmPartition));
      expect(vectors.documents.filter((key) => range.includes(key))).toEqual(vector.documents);
    });
  }

  it("bounds every user's documents, and a user's sessions, by their key prefixes", () => {
    const users = IDBKeyRange.bound(...tmPrefixKeyBounds(TM_USERS_PREFIX));
    expect(vectors.documents.filter((key) => !users.includes(key))).toEqual([['device', 'zone@1']]);
    const sessions = IDBKeyRange.bound(...tmPrefixKeyBounds(tmSessionsPrefix('0b4f')));
    expect(vectors.documents.filter((key) => sessions.includes(key)).map(([p]) => p)).toEqual([
      'u:0b4f/sY4kQ1x-_',
      'u:0b4f/sY4kQ1x-_X',
      'u:0b4f/sZ9',
    ]);
  });

  for (const vector of vectors.userDeletes) {
    it(`bounds a user's documents: ${vector.name}`, () => {
      const [lower, upper] = tmUserKeyBounds(vector.sub);
      const range = IDBKeyRange.bound(lower, upper);
      expect(vectors.documents.filter((key) => !range.includes(key))).toEqual(vector.remaining);
    });
  }
});
