// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmLocalization } from '@tellma/core';

// The tenantless pages' English strings, under `tm.tenantless` (spec 0027
// §6): the tenantless app bar's menus, the tenant picker and the signed-out
// page. Only these pages read them, so they load with the pages' chunk
// rather than with the first frame of a tenant route; a language's table
// that holds a key wins over them. The picker's document title is the one
// key the primary's route table names, and it reads it only once the
// picker's chunk has loaded.

/** The scope of the tenantless pages' strings under `tm`. */
const SCOPE = 'tenantless';

/** The tenantless pages' English strings, under `tm.tenantless`. */
export const TM_TENANTLESS_STRINGS_EN = {
  /** The tenantless app bar's menus. */
  bar: {
    language: 'Language',
    account: 'Account',
    myAccount: 'My account',
    signOut: 'Sign out',
  },
  /** The tenant picker. */
  picker: {
    title: 'Choose a tenant',
    search: 'Search tenants',
    loading: 'Loading tenants…',
    tenant: 'Tenant {id}',
    connect: 'Connect',
    connectTo: 'Connect to {name}',
    /** The row badges. */
    badges: {
      lastUsed: 'Last used',
      sandbox: 'Sandbox',
      readOnly: 'Read-only',
      frozen: 'Under maintenance',
      suspended: 'Suspended',
      provisioning: 'Being set up',
      inactive: 'Deactivated',
    },
    /** Why a row's Connect is unavailable. */
    unavailable: {
      inactive: 'Your access to this tenant was removed.',
      suspended: 'Suspended. Contact your administrator.',
      provisioning: 'This tenant is being set up.',
    },
    /** The state of a user without memberships. */
    empty: {
      title: "You're not a member of any tenant yet",
      message: "Ask your administrator to invite you. You're signed in as {email}.",
    },
    noMatch: 'No tenants match “{query}”.',
    /** The notice of a tenant the user chose and can no longer open. */
    notice: {
      named: 'You can no longer open {name}.',
      unnamed: 'You can no longer open tenant {id}.',
      dismiss: 'Dismiss',
    },
  },
  /** The signed-out page. */
  signedOut: {
    title: 'You signed out',
    message: "You're signed out of {displayName} on this device.",
    signedInAs: "You're signed in as {name}.",
    continue: 'Continue',
  },
} as const;

/**
 * Registers the tenantless pages' strings, once per app; a later call does
 * nothing.
 *
 * @param l10n - The app's localization.
 */
export function tmRegisterTenantlessStrings(l10n: TmLocalization): void {
  l10n.ɵregisterStrings(SCOPE, TM_TENANTLESS_STRINGS_EN);
}
