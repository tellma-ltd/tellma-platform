// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injectable } from '@angular/core';
import { type ActivatedRouteSnapshot, BaseRouteReuseStrategy } from '@angular/router';

// Route reuse across tenants (spec 0024 §4.4). The router reuses a route
// whose config is unchanged, and the tenant matcher and every route under it
// keep their config from one tenant to the next. A route into another tenant
// is therefore created anew, shell, route host, page scope and leave checks
// included, so the new tenant's page waits for its own cold entry and never
// renders on the old tenant's documents. Within a tenant, reuse is the
// router's own: the shell persists, and a route host absorbs a change of its
// parameters.

/** The router's reuse, except that no route survives a change of tenant. */
@Injectable()
export class TmRouteReuse extends BaseRouteReuseStrategy {
  /**
   * Whether a route of the next state reuses the current one: the same
   * route config, in the same tenant. Every route under the tenant segment
   * carries the tenant as its parameter `t`; a tenantless route has none.
   *
   * @param future - The route in the next state.
   * @param current - The route in the current state.
   * @returns Whether the route is reused.
   */
  override shouldReuseRoute(future: ActivatedRouteSnapshot, current: ActivatedRouteSnapshot): boolean {
    return super.shouldReuseRoute(future, current) && future.params['t'] === current.params['t'];
  }
}
