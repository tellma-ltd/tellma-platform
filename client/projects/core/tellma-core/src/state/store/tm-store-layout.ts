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

/** The lower and upper bound of a key range, both inclusive. */
export type TmKeyBounds = readonly [lower: readonly unknown[], upper: readonly unknown[]];

/**
 * The bounds of every document whose `p` starts with a prefix: from
 * `[prefix]` to `[prefix + '\uffff']`. Each key is `[p, n]`, so the bounds
 * compare against `p` alone.
 *
 * @param prefix - The key prefix, such as `u:<sub>/`.
 * @returns The lower and upper bound, both inclusive.
 */
export function tmPrefixKeyBounds(prefix: string): readonly [lower: readonly [string], upper: readonly [string]] {
  return [[prefix], [prefix + LAST]];
}

/**
 * The bounds of every document one user holds, in all of that user's
 * partitions: from `['u:<sub>/']` to `['u:<sub>/\uffff']`.
 *
 * @param sub - The user's subject identifier.
 * @returns The lower and upper bound, both inclusive.
 */
export function tmUserKeyBounds(sub: string): readonly [lower: readonly [string], upper: readonly [string]] {
  // Spelled out rather than through tmPrefixKeyBounds: the boot script
  // bundles this function, and its size is capped.
  const prefix = `u:${sub}/`;
  return [[prefix], [prefix + LAST]];
}

/**
 * The bounds of one partition's documents, and of no other partition's: from
 * `[p]` to `[p, []]`, since an array sorts after every string, so every
 * `[p, n]` falls between them and a longer `p` that starts with this one
 * does not.
 *
 * @param partition - The partition.
 * @returns The lower and upper bound, both inclusive.
 */
export function tmPartitionKeyBounds(partition: TmPartition): TmKeyBounds {
  const p = tmPartitionKey(partition);
  return [[p], [p, []]];
}

/** The key prefix of every user's documents, which a session's end deletes. */
export const TM_USERS_PREFIX = 'u:';

/**
 * The key prefix of one user's session partitions: `u:<sub>/s`.
 *
 * @param sub - The user's subject identifier.
 * @returns The prefix.
 */
export function tmSessionsPrefix(sub: string): string {
  return `u:${sub}/s`;
}

/**
 * The stored name of a document: its name and its client format, as in
 * `settings@1` or `entity:acme.Currency@1`. Each build reads and writes only
 * the stored names of its own formats.
 *
 * @param name - The document's name.
 * @param format - The document's client format.
 * @returns The stored name.
 */
export function tmStoredName(name: string, format: number): string {
  return `${name}@${format}`;
}
