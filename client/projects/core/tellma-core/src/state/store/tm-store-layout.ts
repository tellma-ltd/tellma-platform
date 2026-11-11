// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The client store's layout (spec 0025 §2.2), frozen because the boot script
// deletes by it: an expired session's data goes before the app, and the store
// that wrote it, ever loads. The client store and the boot script both take
// the database, the object store and every key range from here, and both are
// tested over one vector file (tm-store-layout-vectors.json), so a change to
// the layout is a change to the boot script. Plain code with no Angular
// import: the boot script bundles it too.

/** One slice of the client store. */
export type TmPartition =
  | { readonly kind: 'device' }
  | { readonly kind: 'user'; readonly sub: string }
  | { readonly kind: 'tenant'; readonly sub: string; readonly tenantId: number }
  | { readonly kind: 'session'; readonly sub: string; readonly sessionHandle: string };

/** The client store's database. */
export const TM_STORE_DATABASE = 'tm-app';

/** The database's schema version. */
export const TM_STORE_VERSION = 1;

/** The object store that holds every document. */
export const TM_STORE_DOCS = 'docs';

/** The key path of a stored document: its partition, then its stored name. */
export const TM_STORE_KEY_PATH: readonly ['p', 'n'] = ['p', 'n'];

/** The CacheStorage cache of fetched images, swept with the user's data. */
export const TM_IMAGES_CACHE = 'tm-images-v1';

/**
 * The last code unit, which sorts after every character a partition holds:
 * an upper bound of `<prefix>\uffff` covers every partition under the prefix.
 */
const LAST = '\uffff';

/**
 * The `p` of a partition's documents: `device`, `u:<sub>/`,
 * `u:<sub>/t<tenantId>` or `u:<sub>/s<sessionHandle>`. The `/` after the
 * subject keeps one subject from matching a longer one that starts with it.
 *
 * @param partition - The partition.
 * @returns Its key prefix.
 */
export function tmPartitionKey(partition: TmPartition): string {
  switch (partition.kind) {
    case 'device':
      return 'device';
    case 'user':
      return `u:${partition.sub}/`;
    case 'tenant':
      return `u:${partition.sub}/t${partition.tenantId}`;
    case 'session':
      return `u:${partition.sub}/s${partition.sessionHandle}`;
  }
}

/**
 * The bounds of every document one user holds, in all of that user's
 * partitions: from `['u:<sub>/']` to `['u:<sub>/\uffff']`. Each key is
 * `[p, n]`, so the bounds compare against `p` alone, and an array sorts after
 * every string.
 *
 * @param sub - The user's subject identifier.
 * @returns The lower and upper bound, both inclusive.
 */
export function tmUserKeyBounds(sub: string): readonly [lower: readonly [string], upper: readonly [string]] {
  const prefix = `u:${sub}/`;
  return [[prefix], [prefix + LAST]];
}
