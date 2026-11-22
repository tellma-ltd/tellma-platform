// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, untracked } from '@angular/core';
import { type CanMatchFn, RedirectCommand, Router } from '@angular/router';

import { TmSession } from '../session/tm-session';
import { TmLaunchCache } from '../state/launch/tm-launch-cache';
import { TmClientStore } from '../state/store/tm-client-store';
import { TmLaunch } from './tm-launch';
import type { TmMembership } from './tm-membership';
import { tmSingleLiveTenant } from './tm-single-live-tenant';

// The launch redirect at `/` (spec 0024 §9.3). The platform picks the tenant
// whenever the user did not: the last tenant, when the boot record names
// one for this user; else the one live tenant of the user's memberships;
// else the picker, which never redirects, so a stale hint cannot loop. Each
// pick of a tenant marks the entry automatic. With the memberships cached,
// the decision is the route's `canMatch` and nothing renders; without them,
// the route renders the shell's launch page, the picker's skeleton rows,
// while `/bff/user` is read.

/**
 * Where `/` leads over the user's memberships: their one live tenant,
 * marked automatic, else the picker. Internal: the launch route and the
 * shell's launch page decide alike.
 *
 * @param tenants - The memberships, or `undefined` when they could not be read.
 * @param launch - The launch mark.
 * @returns The URL.
 */
export function tmLaunchTarget(tenants: readonly TmMembership[] | undefined, launch: TmLaunch): string {
  const id = tenants === undefined ? null : tmSingleLiveTenant(tenants);
  if (id === null) {
    return '/tenants';
  }
  launch.markAutomatic(id);
  return `/${id}`;
}

/**
 * The launch route's `canMatch`: a known target replaces `/` at once;
 * otherwise the route matches and its component reads the memberships.
 */
export const tmLaunchMatch: CanMatchFn = () => {
  const launch = inject(TmLaunch);
  const record = inject(TmClientStore).boot.read();
  const sub = untracked(inject(TmSession).profile)?.sub;
  const tenants = untracked(inject(TmLaunchCache).tenants);
  let target: string;
  if (record.lastTenantId !== null && sub !== undefined && record.sub === sub) {
    // The last tenant, when the boot record names one for this user.
    launch.markAutomatic(record.lastTenantId);
    target = `/${record.lastTenantId}`;
  } else if (tenants !== undefined) {
    // The cached memberships: stale while the launch cache revalidates them.
    target = tmLaunchTarget(tenants, launch);
  } else {
    return true;
  }
  return new RedirectCommand(inject(Router).parseUrl(target), { replaceUrl: true });
};
