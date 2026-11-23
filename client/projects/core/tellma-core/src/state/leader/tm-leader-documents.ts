// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, InjectionToken } from '@angular/core';

import { TM_DEV_MODE } from '../../app/tm-dev-mode';

// The leader documents (spec 0025 §8.6): tenant documents that only the leader
// tab refreshes, on the hub events each lists, on its (re)connects and polls,
// so a browser makes one call per change however many tabs show them. Every
// tab renders them from the store. The features that own them register them
// here, so no route or event name is hard-wired into the leader. A name is
// one stored document, so it is registered once: a second registration
// would double every read of it, and its format would be whichever came
// last.

/** One tenant document only the leader tab refreshes, on the hub events it lists and its polls. */
export interface TmLeaderDocument {
  /** The tenant-partition document's name, such as `acme.approvals`; each name is registered once. */
  readonly name: string;
  /** The read that returns it, such as `acme/approvals/summary`. */
  readonly route: string;
  /** The hub events that make the leader re-read it, such as `acme.approvals.changed`. */
  readonly events: readonly string[];
  /** The document's client format: the store keeps it under `<name>@<formatVersion>`. */
  readonly formatVersion: number;
  /** While true of the stored copy, the leader also re-reads it on its polls, at most once a minute. */
  readonly pollWhile?: (document: unknown) => boolean;
}

/**
 * The leader documents: a multi token, which the features that own them
 * provide, each value one document. The platform registers its own, such as
 * `inbox`; a name registered twice throws in development, and production
 * keeps the first.
 *
 * @example
 * ```ts
 * providers: [
 *   {
 *     provide: TM_LEADER_DOCUMENTS,
 *     multi: true,
 *     useValue: { name: 'acme.approvals', route: 'acme/approvals/summary', events: ['acme.approvals.changed'], formatVersion: 1 },
 *   },
 * ]
 * ```
 */
export const TM_LEADER_DOCUMENTS = new InjectionToken<readonly TmLeaderDocument[]>('TM_LEADER_DOCUMENTS', {
  providedIn: 'root',
  factory: () => [],
});

/**
 * The leader documents, one per name: what the store's formats, the
 * election and the leader read. A name registered twice throws in
 * development; production keeps its first registration. Internal.
 */
export const ɵTM_UNIQUE_LEADER_DOCUMENTS = new InjectionToken<readonly TmLeaderDocument[]>('ɵTM_UNIQUE_LEADER_DOCUMENTS', {
  providedIn: 'root',
  factory: () => {
    const devMode = inject(TM_DEV_MODE);
    const unique = new Map<string, TmLeaderDocument>();
    for (const doc of inject(TM_LEADER_DOCUMENTS)) {
      if (!unique.has(doc.name)) {
        unique.set(doc.name, doc);
      } else if (typeof ngDevMode !== 'undefined' && ngDevMode && devMode()) {
        // The message exists only in development builds, which fold it away with the throw.
        throw new Error(`The leader document '${doc.name}' is registered twice: register each name once.`);
      }
    }
    return [...unique.values()];
  },
});
