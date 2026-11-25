// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { untracked, type Injector } from '@angular/core';

import { TM_NOW } from '../app/tm-now';
import { TmTelemetry } from '../observe/tm-telemetry';
import { TmSession } from '../session/tm-session';
import { TmClientStore } from '../state/store/tm-client-store';
import type { TmPartition } from '../state/store/tm-store-layout';
import type { TmZoneDoc, TmZoneUser } from './tm-device-zone';

// The writes of the device's zone document (spec 0024 §7.8), a chunk of its
// own: the per-device option loads it on its first change, and the check
// shares it. One entry per user who signed in on the device, keyed by
// subject, each stamped when written; the document keeps at most 20, and
// drops those written longest ago first.

/** What a write of the zone document changes in one user's entry. */
export interface TmZoneUserChange {
  /** The option's new state. */
  readonly auto?: boolean;
  /** A tenant's handled device zone: the tenant's id and the zone. */
  readonly seen?: { readonly tenantId: number; readonly zone: string };
}

/** The most users the zone document remembers. */
export const TM_ZONE_USERS_CAP = 20;

/** The store's device partition, where the zone document lives. */
export const TM_ZONE_PARTITION: TmPartition = { kind: 'device' };

/**
 * A user's entry in a stored zone document, read defensively: a malformed
 * document or entry reads as none. Internal: the check reads it.
 *
 * @param doc - The stored document, if any.
 * @param sub - The user's subject identifier.
 * @returns The entry, or `undefined`.
 */
export function ɵtmZoneUser(doc: TmZoneDoc | undefined, sub: string): TmZoneUser | undefined {
  const user = typeof doc?.users === 'object' && doc.users !== null ? doc.users[sub] : undefined;
  return typeof user === 'object' && user !== null && typeof user.seen === 'object' && user.seen !== null ? user : undefined;
}

/**
 * The zone document after one user's change: the entry is stamped `at`, and
 * the document keeps at most {@link TM_ZONE_USERS_CAP} users by dropping
 * those written longest ago, never the one just written. Internal: the
 * option and the check write the document through it.
 *
 * @param doc - The stored document, if any.
 * @param sub - The user's subject identifier.
 * @param at - Now, in epoch milliseconds.
 * @param change - What changes in the user's entry.
 * @returns The new document.
 */
export function ɵtmZoneDocWith(doc: TmZoneDoc | undefined, sub: string, at: number, change: TmZoneUserChange): TmZoneDoc {
  // --- The entries that read well, then this user's, changed and stamped ---
  const users: Record<string, TmZoneUser> = {};
  for (const other of Object.keys(typeof doc?.users === 'object' && doc.users !== null ? doc.users : {})) {
    const entry = ɵtmZoneUser(doc, other);
    if (entry !== undefined) {
      users[other] = entry;
    }
  }
  const current = users[sub];
  users[sub] = {
    auto: change.auto ?? current?.auto === true,
    seen: change.seen ? { ...current?.seen, [change.seen.tenantId]: change.seen.zone } : (current?.seen ?? {}),
    at,
  };

  // --- The cap: the users written longest ago go first, never this one ---
  const others = Object.keys(users)
    .filter((other) => other !== sub)
    .sort((a, b) => (users[a]!.at || 0) - (users[b]!.at || 0));
  for (const dropped of others.slice(0, Math.max(0, others.length + 1 - TM_ZONE_USERS_CAP))) {
    delete users[dropped];
  }
  return { users };
}

/**
 * Turns the signed-in user's option on or off on this device, and reports
 * the choice. Does nothing while no one is signed in. Internal: the body of
 * `TmDeviceZone.setAuto`.
 *
 * @param injector - The app's injector.
 * @param on - Whether later changes of the device's zone apply by themselves.
 * @param source - Where the user chose: the zone prompt's checkbox or My settings.
 * @returns A promise that resolves once the device's document holds the choice.
 * @throws TmStoreRefusedError when the session no longer allows the write.
 */
export async function ɵtmSetZoneAuto(injector: Injector, on: boolean, source: 'prompt' | 'settings'): Promise<void> {
  const sub = untracked(injector.get(TmSession).profile)?.sub;
  if (sub === undefined) {
    return;
  }
  const now = injector.get(TM_NOW);
  await injector.get(TmClientStore).patch<TmZoneDoc>(TM_ZONE_PARTITION, 'zone', (doc) => ɵtmZoneDocWith(doc, sub, now(), { auto: on }));
  injector.get(TmTelemetry).event('tm.l10n.zone-option', { on, source });
}
