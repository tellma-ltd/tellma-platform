// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmEntityType } from '../entity/tm-entity-types';
import type { TmDecimal, TmJson } from '../json/tm-json';
import type { TmLeaderDocument } from '../state/leader/tm-leader-documents';
import type { TmNotificationSeverity } from './tm-notification-metadata';

// The attention documents (spec 0028 §5.1): the user's inbox (spec 0021
// §5.2) and assignments (spec 0028 §4.3) summaries, which the leader tab
// keeps fresh in the store and every tab renders the bell, the tray and the
// home lists from, so a warm launch shows them before any call. The types
// are camelCase mirrors of the server's records; members the server leaves
// null are absent. Only the inbox is registered for now: the server maps no
// assignments/summary route until the assignments read side lands, and a
// registered read of a route the server lacks would report a contract
// mismatch on every leader refresh. Meanwhile `assignments` is a plain store
// format (TM_DOC_FORMATS), so the tray and the home widgets read it as
// absent; that read side registers it here, and drops the plain format.

/** One of the user's own jobs that is not over yet, as the inbox summary lists it. */
export interface TmInboxRunningJob {
  /** The job's id. */
  readonly id: number;
  /** Its handler's key. */
  readonly handlerKey: string;
  /** Where it stands; `Held` and `PendingConfirmation` wait on a person. */
  readonly status: 'Pending' | 'Running' | 'Held' | 'PendingConfirmation';
  /** Its progress, from 0 to 100, once it reports one. */
  readonly progressPercent?: number;
  /** The key of its progress message. */
  readonly progressKey?: string;
  /** The progress message's arguments, as JSON. */
  readonly progressArgumentsJson?: string;
  /** When someone asked it to stop. */
  readonly cancelRequestedAt?: string;
  /** When it started running. */
  readonly startedAt?: string;
  /** When it was created. */
  readonly createdAt: string;
  /** The key of its label; absent, the handler's label. */
  readonly labelKey?: string;
  /** The label's arguments, as JSON. */
  readonly labelArgumentsJson?: string;
}

/** The inbox document: the bell's summary of the user's notifications and running jobs. */
export interface TmInboxSummary {
  /** The notifications not seen yet, capped. */
  readonly unseen: number;
  /** The most severe of the unseen ones, while any is unseen. */
  readonly maxUnseenSeverity?: TmNotificationSeverity;
  /** The notifications not read yet, capped. */
  readonly unread: number;
  /** The newest notifications, newest first. */
  readonly latest: readonly TmEntityType<'core.Notification'>[];
  /** The user's jobs that are not over yet, newest first. */
  readonly running: readonly TmInboxRunningJob[];
  /** The rows the notifications refer to (their actors), by entity name. */
  readonly related: Readonly<Record<string, readonly TmJson[]>>;
  /** The inbox's version, a `long`: compare it with `TmDecimals.compare`. */
  readonly version: TmDecimal;
  /** When the user last opened the bell. */
  readonly seenAt?: string;
}

/** What an inbox verb (seen, read, clear) returns. */
export interface TmInboxVerbResult {
  /** How many rows it changed. */
  readonly count: number;
  /** The inbox's version after it: a copy at or above it contains the change. */
  readonly version: TmDecimal;
}

/** The assignments document: the tray's summary of what waits on the user, and what the user handed to others. */
export interface TmAssignmentsSummary {
  /** The open assignments to the user, capped. */
  readonly open: number;
  /** Those assigned since the user last opened the tray, capped. */
  readonly new: number;
  /** The open assignments the user handed to others, capped. */
  readonly openByMe: number;
  /** When the user last opened the tray. */
  readonly seenAt?: string;
  /** The newest open assignments to the user, newest first. */
  readonly latest: readonly TmEntityType<'core.Assignment'>[];
  /** The newest open assignments the user handed to others, newest first. */
  readonly latestByMe: readonly TmEntityType<'core.Assignment'>[];
  /** The users the rows refer to (assigners and assignees), by entity name. */
  readonly related: Readonly<Record<string, readonly TmJson[]>>;
}

/**
 * The attention documents, which `provideTellma` registers as leader
 * documents: the inbox, re-read on inbox and job events and polled while a
 * job runs, so a lost job event never strands the bell's progress ring.
 */
export const TM_ATTENTION_DOCUMENTS: readonly TmLeaderDocument[] = [
  {
    name: 'inbox',
    route: 'inbox/summary',
    events: ['inbox.changed', 'job.changed'],
    formatVersion: 1,
    pollWhile: (document) => (document as TmInboxSummary).running.some((job) => job.status === 'Pending' || job.status === 'Running'),
  },
];
