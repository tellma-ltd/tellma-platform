// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { InjectionToken } from '@angular/core';

// The leader documents (spec 0025 §8.6): tenant documents that only the leader
// tab refreshes, on the hub events each lists, on its (re)connects and polls,
// so a browser makes one call per change however many tabs show them. Every
// tab renders them from the store. The features that own them register them
// here, so no route or event name is hard-wired into the leader.

/** One tenant document only the leader tab refreshes, on the hub events it lists and its polls. */
export interface TmLeaderDocument {
  /** The tenant-partition document's name, such as `inbox`. */
  readonly name: string;
  /** The read that returns it, such as `inbox/summary`. */
  readonly route: string;
  /** The hub events that make the leader re-read it, such as `inbox.changed`. */
  readonly events: readonly string[];
  /** The document's client format: the store keeps it under `<name>@<formatVersion>`. */
  readonly formatVersion: number;
  /** While true of the stored copy, the leader also re-reads it on its polls, at most once a minute. */
  readonly pollWhile?: (document: unknown) => boolean;
}

/**
 * The leader documents: a multi token, which the features that own them
 * provide, each value one document.
 *
 * @example
 * ```ts
 * providers: [
 *   { provide: TM_LEADER_DOCUMENTS, multi: true, useValue: { name: 'inbox', route: 'inbox/summary', events: ['inbox.changed'], formatVersion: 1 } },
 * ]
 * ```
 */
export const TM_LEADER_DOCUMENTS = new InjectionToken<readonly TmLeaderDocument[]>('TM_LEADER_DOCUMENTS', {
  providedIn: 'root',
  factory: () => [],
});
