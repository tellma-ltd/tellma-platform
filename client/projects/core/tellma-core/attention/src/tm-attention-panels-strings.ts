// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TmLocalization } from '@tellma/core';

// The English strings of the bell's and the tray's panels, under
// `tm.attentionPanels` (spec 0028 §6.2, §7.2). They load with the panels'
// chunk, never with the first frame; the panels' host registers them when it
// is created. A language's table that holds a key wins over them.

/** The scope of the panels' strings under `tm`. */
const SCOPE = 'attentionPanels';

/** The panels' English strings, under `tm.attentionPanels`. */
export const TM_ATTENTION_PANELS_STRINGS_EN = {
  /** The bell's panel. */
  bell: {
    heading: 'Notifications',
    markAllRead: 'Mark all as read',
    more: 'More options',
    settings: 'Notification settings',
    clearRead: 'Clear read notifications',
    clearReadTitle: 'Clear read notifications?',
    clearReadMessage: 'They are deleted for you.',
    clearReadConfirm: 'Clear',
    download: 'Download',
    clear: 'Clear',
    unread: 'unread',
    cleared: 'Notification cleared',
    // The pinned button over rows that arrived while the list was scrolled down.
    fresh: '{count, number} new',
    arrived: '{count, plural, one {# new notification} other {# new notifications}}',
    paused: 'Live updates paused · Updated {time}',
    refresh: 'Refresh',
    empty: "You're all caught up",
    seeAll: 'See all notifications',
  },
  /** The bell's In progress section. */
  jobs: {
    heading: 'In progress',
    more: '+{count, number} more in My jobs',
    waiting: 'Waiting to start',
    working: 'Working…',
    stopping: 'Stopping…',
    held: 'Held',
    heldLine: 'Paused after a service interruption',
    awaiting: 'Awaiting confirmation',
    awaitingLine: 'Waiting for you to confirm',
    review: 'Review',
    // Stop and its confirmation, by the job's kind: `import` for an import, else any other.
    stop: '{kind, select, import {Stop import} other {Stop}}',
    stopTitle: '{kind, select, import {Stop this import?} other {Stop this job?}}',
    stopMessage: "{kind, select, import {Rows already saved stay saved. The rest of the file won't be imported.} other {Work already saved stays saved.}}",
  },
  /** The tray's panel. */
  tray: {
    heading: 'My assignments',
    open: '{count, number} open',
    list: 'Assignments',
    empty: 'Nothing is waiting on you',
    seeAll: 'See all assignments',
  },
  /** The panels' page-context actions, as the assistant names them. */
  actions: {
    openItem: 'Open',
  },
} as const;

/**
 * Registers the panels' strings, once per app; a later call does nothing.
 *
 * @param l10n - The app's localization.
 */
export function tmRegisterAttentionPanelsStrings(l10n: TmLocalization): void {
  l10n.ɵregisterStrings(SCOPE, TM_ATTENTION_PANELS_STRINGS_EN);
}
