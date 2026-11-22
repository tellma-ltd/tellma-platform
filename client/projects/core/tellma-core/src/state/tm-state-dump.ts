// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, InjectionToken, untracked } from '@angular/core';

import { TmClientStore, type TmStoredDocInfo } from './store/tm-client-store';
import { TmCacheRefresher } from './sync/tm-cache-refresher';

// The client state's section of the support dump (spec 0025 §15): names and
// tags, never values. The leader's member joins it with the leader, and the
// support service registers the section once it lands. The preferences, the
// drafts and the page state report through TM_STATE_PARTS when they are
// created, so reading the section never creates them: the drafts and the page
// state live in a lazily loaded entry point, and a tab that never loaded it
// holds none.

/** The client state's section of the support dump: names and tags, never values. Internal. */
export interface TmStateDumpSection {
  /** The store's mode, and every document this tab mirrors. */
  readonly store: { readonly mode: 'indexeddb' | 'memory'; readonly docs: readonly TmStoredDocInfo[] };
  /** When the routed tenant last synced, in epoch milliseconds, or `null`. */
  readonly refresher: { readonly lastSyncAt: number | null };
  /** The preference keys this tab set and has not yet saved. */
  readonly preferences: { readonly dirty: readonly string[] };
  /** This tab's draft keys, without the user part, such as `17:gl.Center:5`. */
  readonly drafts: readonly string[];
  /** The page-state keys this tab holds, least recently used first. */
  readonly pageState: readonly string[];
}

/**
 * The parts of the dump the services report once created, each a reader of
 * the service's names. Internal: each service sets its own member when it is
 * created, and a part no service set reads as empty.
 */
export interface TmStateParts {
  /** The preference keys this tab set and has not yet saved. */
  preferences?: () => readonly string[];
  /** This tab's draft keys, without the user part. */
  drafts?: () => readonly string[];
  /** The page-state keys this tab holds. */
  pageState?: () => readonly string[];
}

/** The parts of the dump the services report. Internal. */
export const TM_STATE_PARTS = new InjectionToken<TmStateParts>('TM_STATE_PARTS', {
  providedIn: 'root',
  factory: () => ({}),
});

/** Builds the client state's section of the support dump. Internal. */
@Injectable({ providedIn: 'root' })
export class TmStateDump {
  readonly #store = inject(TmClientStore);
  readonly #refresher = inject(TmCacheRefresher);
  readonly #parts = inject(TM_STATE_PARTS);

  /**
   * The section now.
   *
   * @returns The section, as plain JSON.
   */
  read(): TmStateDumpSection {
    const parts = this.#parts;
    return untracked(() => ({
      store: { mode: this.#store.mode, docs: this.#store.ɵlist() },
      refresher: { lastSyncAt: this.#refresher.lastSyncAt() },
      preferences: { dirty: parts.preferences?.() ?? [] },
      drafts: parts.drafts?.() ?? [],
      pageState: parts.pageState?.() ?? [],
    }));
  }
}
