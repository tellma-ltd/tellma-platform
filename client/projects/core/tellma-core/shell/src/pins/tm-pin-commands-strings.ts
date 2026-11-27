// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmLocalization } from '@tellma/core';

// The pin commands' English strings, under `tm.pinCommands` (spec 0027 §9.3,
// §11.2): the launcher's Manage pins… and the Shortcuts dialog's row of
// Alt+Shift+P. Only deferred chunks read them, so each registers them as it
// loads; registering twice keeps the first. A language's table that holds a
// key wins over them.

/** The scope of the pin commands' strings under `tm`. */
const SCOPE = 'pinCommands';

/** The pin commands' English strings, under `tm.pinCommands`. */
export const TM_PIN_COMMANDS_STRINGS_EN = {
  /** The launcher's command that opens the Manage pins dialog. */
  manage: 'Manage pins…',
  /** Alt+Shift+P's row in the Shortcuts dialog. */
  toggle: 'Pin or unpin this page',
} as const;

/**
 * Registers the pin commands' English strings under `tm.pinCommands`, once.
 *
 * @param l10n - The localization service.
 */
export function tmRegisterPinCommandsStrings(l10n: TmLocalization): void {
  l10n.ɵregisterStrings(SCOPE, TM_PIN_COMMANDS_STRINGS_EN);
}
