// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { InjectionToken, signal, type Signal } from '@angular/core';

// The permission check of a `tmPage` route's gate (spec 0024 §2.4, §4.4).
// The route host asks it once the launch documents are current. Until the
// entity model's permission service is provided here, it allows everything:
// the server stays the authority, and a page it refuses shows the refusal.

/** The member of the permission service the route host's gate reads. */
export interface TmRoutePermissions {
  /**
   * Whether the user holds a permission.
   *
   * @param resource - The securable resource.
   * @param action - The action.
   * @returns Whether it is granted, following the access summary.
   */
  can(resource: string, action: string): Signal<boolean>;
}

/** The permission check the route host's securable gate reads. */
export const TM_ROUTE_PERMISSIONS = new InjectionToken<TmRoutePermissions>('TM_ROUTE_PERMISSIONS', {
  providedIn: 'root',
  factory: (): TmRoutePermissions => {
    const allowed = signal(true).asReadonly();
    return { can: () => allowed };
  },
});
