// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmLocalization } from '@tellma/core';

// The tenant shell's English strings, under `tm.tenantShell` (spec 0027 §7):
// the names the app bar's controls read in the first frame, and the line the
// shell shows when a cold sync fails while the error views cannot load,
// which must not wait for a chunk that may be what failed. The session menu,
// the marker popovers, the dialogs and the installed-mode history buttons
// register their own strings with their chunks; the overlays' chunk also
// reads the marker labels here, which the bar shares. A language's table
// that holds a key wins over them.

/** The scope of the tenant shell's strings under `tm`. */
const SCOPE = 'tenantShell';

/** The tenant shell's English strings, under `tm.tenantShell`. */
export const TM_TENANT_SHELL_STRINGS_EN = {
  /** The app bar. */
  bar: {
    /** The wordmark link's name. */
    home: '{name} home',
    /** The Menu button, which opens the launcher. */
    menu: 'Menu',
    /** The search field and the search button, which open the launcher. */
    search: 'Search',
    /** A slot button's name with a count. */
    slotCount: '{label}, {count}',
    /** The session button's name, then one suffix per marker that shows; without the tenant while a cold launch has no name for it. */
    account: 'Account: {user}, {tenant}',
    accountUser: 'Account: {user}',
    accountSandbox: ', sandbox',
    accountReadOnly: ', read-only',
    accountMaintenance: ', under maintenance',
    /** The markers' labels: the chip's text and the icon circle's name. */
    markers: {
      sandbox: 'Sandbox',
      readOnly: 'Read-only',
      maintenance: 'Maintenance',
    },
  },
  /** The line a failed cold sync shows while the error views cannot load. */
  failed: "Couldn't load this tenant.",
} as const;

/**
 * Registers the tenant shell's English strings under `tm.tenantShell`, once.
 *
 * @param l10n - The localization service.
 */
export function tmRegisterTenantShellStrings(l10n: TmLocalization): void {
  l10n.ɵregisterStrings(SCOPE, TM_TENANT_SHELL_STRINGS_EN);
}
