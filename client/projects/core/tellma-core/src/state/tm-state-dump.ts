// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, untracked } from '@angular/core';

import { tmRoutedTenant } from '../launch/tm-routed-tenant';

import type { TmHubStatus } from './leader/tm-hub-types';
import { TmLeader } from './leader/tm-leader';
import { TM_DRAFT_KEY_PREFIX, TM_DRAFTS_STORAGE, tmDraftKeyTail, tmStorageKeys } from './memory/tm-draft-keys';
import { TmClientStore, type TmStoredDocInfo } from './store/tm-client-store';
import { TmCacheRefresher } from './sync/tm-cache-refresher';
import { TM_STATE_PARTS } from './tm-state-parts';

// The client state's section of the support dump (spec 0025 §15): names and
// tags, never values. The support facade registers the section when the
// support host's chunk lands, which is the only importer of this file, so
// the first frame does not carry it. The preferences, the drafts and the
// page state report through TM_STATE_PARTS when they are created, so reading
// the section never creates them: the drafts and the page state live in a
// lazily loaded entry point.
// A tab that never loaded it holds no page state, but may hold drafts in its
// session storage from before a reload, which the section lists from there.
// The leader's member reads the small TmLeader, which the election writes
// once its entry point loads. On a tenant's routes, the draft and page-state
// keys of the tab's other tenants are left out: the section may go into the
// routed tenant's support request.

/** The client state's section of the support dump: names and tags, never values. Internal. */
export interface TmStateDumpSection {
  /** The store's mode, and every document this tab mirrors. */
  readonly store: { readonly mode: 'indexeddb' | 'memory'; readonly docs: readonly TmStoredDocInfo[] };
  /** Whether this tab leads the routed tenant, and the leader's realtime connection. */
  readonly leader: { readonly isLeader: boolean; readonly hubStatus: TmHubStatus };
  /** When the routed tenant last synced, in epoch milliseconds, or `null`. */
  readonly refresher: { readonly lastSyncAt: number | null };
  /** The preference keys this tab set and has not yet saved. */
  readonly preferences: { readonly dirty: readonly string[] };
  /** This tab's draft keys, without the user part, such as `17:gl.Center:5`; on a tenant's routes, that tenant's alone. */
  readonly drafts: readonly string[];
  /** The page-state keys this tab holds, least recently used first; on a tenant's routes, that tenant's alone. */
  readonly pageState: readonly string[];
}

/** Builds the client state's section of the support dump. Internal. */
@Injectable({ providedIn: 'root' })
export class TmStateDump {
  readonly #store = inject(TmClientStore);
  readonly #refresher = inject(TmCacheRefresher);
  readonly #leader = inject(TmLeader);
  readonly #parts = inject(TM_STATE_PARTS);
  readonly #drafts = inject(TM_DRAFTS_STORAGE);

  /**
   * The section now.
   *
   * @param tenantId - The routed tenant, whose draft and page-state keys
   *   alone the section lists, with those that name no tenant; `null` lists
   *   every key.
   * @returns The section, as plain JSON.
   */
  read(tenantId: number | null = null): TmStateDumpSection {
    const parts = this.#parts;
    return untracked(() => {
      // --- On a tenant's routes, a key of another tenant is left out; one with no tenant stays ---
      const ours = (keyTenant: number | null): boolean => tenantId === null || keyTenant === null || keyTenant === tenantId;
      const drafts = parts.drafts?.() ?? tmStorageKeys(this.#drafts, TM_DRAFT_KEY_PREFIX).map(tmDraftKeyTail);
      return {
        store: { mode: this.#store.mode, docs: this.#store.ɵlist() },
        leader: { isLeader: this.#leader.isLeader(), hubStatus: this.#leader.hubStatus() },
        refresher: { lastSyncAt: this.#refresher.lastSyncAt() },
        preferences: { dirty: parts.preferences?.() ?? [] },
        drafts: drafts.filter((key) => ours(draftTenant(key))),
        pageState: (parts.pageState?.() ?? []).filter((key) => ours(tmRoutedTenant(key))),
      };
    });
  }
}

/**
 * The tenant a draft key names: its first part, such as 17 in `17:gl.Center:5`.
 *
 * @param key - The key, without its prefix and user part.
 * @returns The tenant's id, or `null` when the key names none.
 */
function draftTenant(key: string): number | null {
  const head = key.slice(0, key.indexOf(':'));
  return /^[1-9][0-9]*$/.test(head) ? Number(head) : null;
}
