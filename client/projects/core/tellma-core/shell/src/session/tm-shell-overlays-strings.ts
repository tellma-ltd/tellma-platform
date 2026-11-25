// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmLocalization } from '@tellma/core';

// The English strings of the tenant shell's overlays, under `tm.shellOverlays`
// (spec 0027 §7.4, §10): the session menu's rows, the markers' popovers and
// the dialogs' titles. Only the overlays' chunk reads them, so they load with
// it rather than with the first frame; a language's table that holds a key
// wins over them.

/** The scope of the overlays' strings under `tm`. */
const SCOPE = 'shellOverlays';

/** The English strings of the tenant shell's overlays, under `tm.shellOverlays`. */
export const TM_SHELL_OVERLAYS_STRINGS_EN = {
  /** The session menu. */
  menu: {
    /** The menu's name. */
    label: 'Account',
    /** The tenant card's second line, then a suffix per marker. */
    tenant: 'Tenant {id}',
    tenantSandbox: ' · Sandbox',
    tenantReadOnly: ' · Read-only',
    tenantMaintenance: ' · Under maintenance',
    /** The tenant card's detail, and how assistive technology reads it. */
    switch: 'Switch',
    switchTenant: 'Switch tenant',
    mySettings: 'My settings',
    language: 'Language',
    calendar: 'Calendar',
    theme: 'Theme',
    themeSystem: 'System',
    themeLight: 'Light',
    themeDark: 'Dark',
    about: 'About',
    signOut: 'Sign out',
  },
  /** The markers' popovers. */
  markers: {
    sandbox: "You're in a sandbox. Changes here don't affect live data.",
    openLive: 'Open {name}',
    readOnly: '{tenant} is read-only. You can view data but not change it. Contact your administrator.',
    maintenance: '{tenant} is under maintenance. You can view data but not change it until it ends.',
  },
  /** The dialogs' titles, and the placeholder's status while a dialog loads. */
  dialogs: {
    about: 'About',
    loading: 'Loading…',
    failed: "Couldn't open this. Check your connection and try again.",
  },
} as const;

/**
 * Registers the overlays' English strings under `tm.shellOverlays`, once.
 *
 * @param l10n - The localization service.
 */
export function tmRegisterShellOverlaysStrings(l10n: TmLocalization): void {
  l10n.ɵregisterStrings(SCOPE, TM_SHELL_OVERLAYS_STRINGS_EN);
}
