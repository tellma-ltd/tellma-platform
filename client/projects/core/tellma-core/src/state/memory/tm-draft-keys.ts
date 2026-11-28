// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, InjectionToken } from '@angular/core';

import { TM_SESSION_HOST, type TmSessionStorage } from '../../session/tm-session-host';

// The session storage of drafts (spec 0025 §11) and its keys,
// `tm.draft:<sub>:...`: plain code shared by the drafts service, which lives
// in a lazily loaded entry point, and the session, which removes a previous
// user's drafts after another user signs in, so neither imports the other.

/**
 * The session storage drafts live in: the tab's own unless a test provides
 * another. Internal.
 */
export const TM_DRAFTS_STORAGE = new InjectionToken<TmSessionStorage | null>('TM_DRAFTS_STORAGE', {
  providedIn: 'root',
  factory: () => inject(TM_SESSION_HOST).sessionStorage,
});

/** The prefix of every draft's session-storage key. */
export const TM_DRAFT_KEY_PREFIX = 'tm.draft:';

/**
 * The prefix of one user's draft keys.
 *
 * @param sub - The user's subject.
 * @returns `tm.draft:<sub>:`.
 */
export function tmDraftPrefix(sub: string): string {
  return `${TM_DRAFT_KEY_PREFIX}${sub}:`;
}

/**
 * A draft key without its prefix and user part, as the support dump lists it.
 *
 * @param key - The key, such as `tm.draft:<sub>:17:gl.Center:5`.
 * @returns The rest, such as `17:gl.Center:5`.
 */
export function tmDraftKeyTail(key: string): string {
  return key.slice(key.indexOf(':', TM_DRAFT_KEY_PREFIX.length) + 1);
}

/**
 * The keys of the session storage that start with a prefix; none when the
 * storage refuses.
 *
 * @param storage - The storage, or `null` when the browser refuses it.
 * @param prefix - The prefix, such as a user's draft prefix.
 * @returns The keys, in the storage's order.
 */
export function tmStorageKeys(storage: TmSessionStorage | null, prefix: string): string[] {
  const keys: string[] = [];
  try {
    for (let i = 0; storage !== null && i < storage.length; i++) {
      const key = storage.key(i);
      if (key?.startsWith(prefix)) {
        keys.push(key);
      }
    }
  } catch {
    // A storage that refuses holds nothing this tab can reach.
  }
  return keys;
}

/**
 * Removes a user's drafts from session storage; a refusing storage is ignored.
 *
 * @param storage - The storage, or `null` when the browser refuses it.
 * @param sub - The user's subject.
 */
export function tmRemoveDrafts(storage: TmSessionStorage | null, sub: string): void {
  // The keys are collected first, since removing an item shifts the indexes.
  for (const key of tmStorageKeys(storage, tmDraftPrefix(sub))) {
    try {
      storage?.removeItem(key);
    } catch {
      // Nothing to do: a storage that refuses holds no drafts this tab can reach.
    }
  }
}
