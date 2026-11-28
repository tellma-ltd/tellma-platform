// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Route, Router } from '@angular/router';

import { tmAppRig } from './tm-app.testing.util';
import { tmShellComponent } from './tm-route-hosts';

// The tenant route's first entry: the shell's module brings the tenant shell
// with it, so once a tenantless page such as the picker has loaded, the
// tenant route's component is the tenant shell itself, not a promise, and
// the first entry into a tenant paints its skeleton in the click's frame
// (spec 0024 §4.4), with no preload to win a race first. The case has its
// own file: the shell's components stay loaded for the rest of a spec file,
// and it needs the tenant shell not yet loaded.

/**
 * Whether a URL reaches a top-level route through its matcher.
 *
 * @param route - The route.
 * @param url - The URL.
 * @param router - The router that parses the URL.
 * @returns Whether the route's matcher consumes the URL's first segments.
 */
function reaches(route: Route, url: string, router: Router): boolean {
  const group = router.parseUrl(url).root.children['primary']!;
  return route.matcher!(group.segments, group, route) !== null;
}

describe('the tenant route', () => {
  it('returns the tenant shell itself on its first entry, for every tenant URL, once the picker has loaded', async () => {
    const { router } = await tmAppRig();
    const tenantRoute = router.config.find((route) => route.matcher !== undefined && route.loadComponent !== undefined)!;

    await tmShellComponent('TmTenantPicker')();
    const component = tenantRoute.loadComponent!();

    // The component itself, which the router activates within the click's task.
    expect(component).not.toBeInstanceOf(Promise);
    expect(component).toBe(await tmShellComponent('TmTenantShell')());
    // The home and every other tenant URL are this one route's; the picker's is not.
    for (const url of ['/17', '/17/settings', '/17/centers', '/17/centers/5']) {
      expect(reaches(tenantRoute, url, router)).toBe(true);
    }
    expect(reaches(tenantRoute, '/tenants', router)).toBe(false);
  });
});
