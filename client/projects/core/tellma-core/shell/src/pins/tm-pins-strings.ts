// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmLocalization } from '@tellma/core';

// The pinned bar's English strings, under `tm.pins` (spec 0027 §8): what the
// bar and its controls name and say in the first frame, the reasons the pin
// control is disabled, and what pinning and unpinning announce or toast. The
// bar's menus and popover, the Manage pins dialog and the pin commands of the
// launcher and the Shortcuts dialog register their own strings with their
// chunks. A language's table that holds a key wins over them.

/** The scope of the pinned bar's strings under `tm`. */
const SCOPE = 'pins';

/** The pinned bar's English strings, under `tm.pins`. */
export const TM_PINS_STRINGS_EN = {
  /** The bar's landmark and the list of pins. */
  bar: 'Pinned pages',
  /** The chip of the items that do not fit. */
  more: 'More',
  /** The pin control on an unpinned page, and the launcher's command. */
  pin: 'Pin this page',
  /** The pin control on a pinned page, and the launcher's command. */
  edit: 'Edit pin',
  /** The button that opens the Manage pins dialog, and the dialog's title. */
  manage: 'Manage pins',
  /** The bar with no pins, and its short form on a phone. */
  empty: 'Nothing pinned yet. Use the pin button to pin this page.',
  emptyShort: 'Nothing pinned yet.',
  /** Why the pin control is disabled: the page's link past the cap, and the pins at the cap. */
  tooLong: "This page's link is too long to pin",
  full: "You've pinned {count} pages. Unpin one to add another.",
  /** The toast of a pin whose chip does not show, and the announcement of one that does. */
  pinnedToast: 'Pinned {label}.',
  pinned: 'Pinned {label}',
  /** The announcement of a pin removed. */
  unpinned: 'Unpinned {label}',
  /** A revoked chip's name, and an unavailable one's. */
  noAccessChip: '{label}, no access',
  unavailableChip: '{label}, not available',
} as const;

/**
 * Registers the pinned bar's English strings under `tm.pins`, once.
 *
 * @param l10n - The localization service.
 */
export function tmRegisterPinsStrings(l10n: TmLocalization): void {
  l10n.ɵregisterStrings(SCOPE, TM_PINS_STRINGS_EN);
}
