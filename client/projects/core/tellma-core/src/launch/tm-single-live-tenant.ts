// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmMembership } from './tm-membership';

// The single-membership rule (spec 0024 §9.3, spec 0011 §5.5). The server's
// post-sign-in landing and the app's launch redirect both send a user with
// exactly one live tenant straight into it. One vector file,
// launch-landing-vectors.json beside this module, pins both implementations
// to the same answers; the server's test project links it.

/**
 * The id of the user's one live tenant: the one membership that is active,
 * whose tenant is live, and whose tenant serves reads (`Active`, `ReadOnly` or
 * `Frozen`). Sandboxes never count, and a tenant listed twice counts once.
 *
 * @param tenants - The user's memberships.
 * @returns The tenant's id, or `null` when no tenant or more than one qualifies.
 *
 * @example
 * ```ts
 * const id = tmSingleLiveTenant(tenants);
 * void router.navigateByUrl(id === null ? '/tenants' : `/${id}`, { replaceUrl: true });
 * ```
 */
export function tmSingleLiveTenant(tenants: readonly TmMembership[]): number | null {
  let found: number | null = null;
  for (const tenant of tenants) {
    const qualifies =
      tenant.isActive &&
      tenant.category === 'Live' &&
      (tenant.state === 'Active' || tenant.state === 'ReadOnly' || tenant.state === 'Frozen');
    if (!qualifies || found === tenant.id) {
      continue;
    }
    if (found !== null) {
      // A second live tenant: the choice is the user's.
      return null;
    }
    found = tenant.id;
  }
  return found;
}
