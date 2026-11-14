// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmClientEvent, TmHubState } from '../leader/tm-hub-types';

// The tab bus's message catalogue (spec 0025 §6.2). The bus carries these
// between the tabs of one browser and knows nothing of what they mean; each
// message's owner does. A tab ignores a type it does not know, so a newer
// build may add types while older tabs run beside it.

/** A stored document changed, or a sweep deleted a key prefix. */
export interface TmStoreChangedMessage {
  /** The message's type. */
  readonly type: 'store.changed';
  /** The partition's key (`p`), or, after a sweep, the deleted key prefix. */
  readonly partition: string;
  /** The stored names written, such as `settings@1`, or `null` after a sweep. */
  readonly names: readonly string[] | null;
}

/** The leader tab relays an event its hub received. */
export interface TmHubEventMessage {
  /** The message's type. */
  readonly type: 'hub.event';
  /** The tenant whose hub received the event. */
  readonly tenantId: number;
  /** The event, at most 4 KB. */
  readonly event: TmClientEvent;
}

/** The leader tab reports its hub connection. */
export interface TmHubStatusMessage {
  /** The message's type. */
  readonly type: 'hub.status';
  /** The tenant whose hub it is. */
  readonly tenantId: number;
  /** The connection's state. */
  readonly status: TmHubState;
  /** When the connection entered the state, in epoch milliseconds. */
  readonly since: number;
  /** Whether the leader tab is visible. */
  readonly leaderVisible: boolean;
}

/** A follower tab asks the leader to report. */
export interface TmLeaderProbeMessage {
  /** The message's type. */
  readonly type: 'leader.probe';
  /** The tenant whose leader is asked. */
  readonly tenantId: number;
}

/** A tab set preferences. */
export interface TmPrefsChangedMessage {
  /** The message's type. */
  readonly type: 'prefs.changed';
  /** The tenant whose preferences changed. */
  readonly tenantId: number;
  /** The stored strings, by key; `null` resets a key. */
  readonly values: Readonly<Record<string, string | null>>;
}

/** A signing-out tab asks every tab about its unsaved work. */
export interface TmDraftsQueryMessage {
  /** The message's type. */
  readonly type: 'drafts.query';
  /** The id of the query, which every reply repeats. */
  readonly replyTo: string;
}

/** A tab answers a drafts query. */
export interface TmDraftsReplyMessage {
  /** The message's type. */
  readonly type: 'drafts.reply';
  /** The id of the query answered. */
  readonly replyTo: string;
  /** How many unsaved drafts of the user the tab holds. */
  readonly dirty: number;
  /** Whether the tab has a write in flight. */
  readonly busy: boolean;
}

/** A session event, which every user of the browser hears. */
export interface TmSessionMessage {
  /** The message's type. */
  readonly type: 'session.signing-out' | 'session.signed-out' | 'session.expired' | 'session.started' | 'session.changed';
  /** The user whose session it is. */
  readonly sub: string;
}

/** One message between the tabs of a browser. */
export type TmBusMessage =
  | TmStoreChangedMessage
  | TmHubEventMessage
  | TmHubStatusMessage
  | TmLeaderProbeMessage
  | TmPrefsChangedMessage
  | TmDraftsQueryMessage
  | TmDraftsReplyMessage
  | TmSessionMessage;

/** The value a channel carries: one message, its envelope version and the tab that posted it. */
export interface TmBusEnvelope {
  /** The envelope's version; a tab ignores a version it does not know. */
  readonly v: 1;
  /** The posting tab's id. */
  readonly from: string;
  /** The message. */
  readonly message: TmBusMessage;
}

/** Every message type this build knows; a tab ignores the others. */
export const TM_BUS_MESSAGE_TYPES: ReadonlySet<string> = new Set<TmBusMessage['type']>([
  'store.changed',
  'hub.event',
  'hub.status',
  'leader.probe',
  'prefs.changed',
  'drafts.query',
  'drafts.reply',
  'session.signing-out',
  'session.signed-out',
  'session.expired',
  'session.started',
  'session.changed',
]);
