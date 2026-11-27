// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TmLocalization } from '@tellma/core';

// The English strings of the attention pages, under `tm.attentionPages`
// (spec 0028 §10.3): the Notifications section of My settings, with its
// pop-up card, its type × channel grid, its save status and its test
// delivery, and the section's page actions. They ship with this entry
// point, which loads with the page that shows them, never with the first
// frame; each component registers them when it is created. A language's
// table that holds a key wins over them. The labels of the categories, the
// types and the channels are the server's (NotificationCategory_*,
// NotificationType_*, NotificationChannel_*).

/** The scope of the attention pages' strings under `tm`. */
const SCOPE = 'attentionPages';

/** The attention pages' English strings, under `tm.attentionPages`. */
export const TM_ATTENTION_PAGES_STRINGS_EN = {
  /** The Notifications section of My settings. */
  settings: {
    // The pop-up card and its one switch.
    popups: 'Pop-up messages',
    popupsLabel: 'Show a pop-up when a notification or an assignment arrives',
    // The grid: the first column's header, a type's switch name, a locked switch and a channel-only type's inbox cell.
    type: 'Notification',
    cell: '{type}, {channel}',
    alwaysOn: 'Always on',
    noInbox: "Doesn't show in the app",
    // The save status in the header.
    saving: 'Saving…',
    saved: 'All changes saved',
    failed: "Couldn't save your changes",
    retry: 'Try again',
    // A read that failed while the error views could not load either.
    loadFailed: "Couldn't load the notification settings.",
  },
  /** The test delivery card. */
  test: {
    heading: 'Test messages',
    send: 'Send a test',
    noAddress: 'No address',
    sent: 'Sent',
    noEmail: 'Add an email address in your profile first.',
    noContact: 'Add a contact address in your profile first.',
    notConfigured: "{channel} isn't set up for this server.",
  },
  /** The section's page actions. */
  actions: {
    set: 'Turn a notification channel on or off',
    test: 'Send a test message',
  },
} as const;

/**
 * Registers the attention pages' strings, once per app; a later call does nothing.
 *
 * @param l10n - The app's localization.
 */
export function tmRegisterAttentionPagesStrings(l10n: TmLocalization): void {
  l10n.ɵregisterStrings(SCOPE, TM_ATTENTION_PAGES_STRINGS_EN);
}
