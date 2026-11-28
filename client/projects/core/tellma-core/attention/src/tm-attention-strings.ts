// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TmLocalization } from '@tellma/core';

// The attention surfaces' English strings, under `tm.attention` (spec 0028
// §5.2, §8.1). They ship with this entry point rather than with the
// first frame; each service registers them when it is created, which merges
// them beside the primary's own `tm.attention.banners` keys. A language's
// table that holds a key wins over them.

/** The scope of the attention strings under `tm`. */
const SCOPE = 'attention';

/** The attention surfaces' English strings, under `tm.attention`. */
const STRINGS_EN = {
  /** The parts of the bell button's accessible name. */
  bell: {
    label: 'Notifications',
    // {count} arrives formatted, "99+" past the cap.
    new: '{count} new',
    attention: 'needs attention',
    jobs: '{count, plural, one {# job running} other {# jobs running}}',
  },
  /** The parts of the tray button's accessible name. */
  tray: {
    label: 'My assignments',
    open: '{count} open',
    new: '{count} new',
  },
  /** What joins the parts of an accessible name. */
  separator: ', ',
  /** The toasts' overflow pill, and the verbs' failure toast; the arrival toasts' own strings ship with them. */
  toasts: {
    more: '{count, number} more · Open notifications',
    failed: "Couldn't update your notifications. Try again.",
  },
  /** The outcome toasts the acting tab raises. */
  outcomes: {
    started: 'Started in the background.',
    track: 'Track',
    exporting: 'Exporting…',
    cancel: 'Cancel',
  },
} as const;

/**
 * Registers the attention strings, once per app; a later call does nothing.
 *
 * @param l10n - The app's localization.
 */
export function tmRegisterAttentionStrings(l10n: TmLocalization): void {
  l10n.ɵregisterStrings(SCOPE, STRINGS_EN);
}
