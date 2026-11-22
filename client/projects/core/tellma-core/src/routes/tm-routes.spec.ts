// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, type Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { type ActivatedRouteSnapshot } from '@angular/router';
import type { RouterTestingHarness } from '@angular/router/testing';

import { tmAppRig, tmNavigate, tmSettle, type TmAppRig } from './tm-app.testing.util';
import { TM_ROUTE_CASES } from './tm-route-cases.testing.util';
import { TmLeaveChecks } from './tm-leave-checks';
import { TmRouteHost } from './tm-route-host';
import type { TmRouteDataInternal } from './tm-routes';

/** The deepest route the router activated. */
function deepest(rig: TmAppRig): ActivatedRouteSnapshot {
  let route = rig.router.routerState.snapshot.root;
  while (route.firstChild) {
    route = route.firstChild;
  }
  return route;
}

/** The deepest route that has a component, and that component's name. */
function routed(rig: TmAppRig): { readonly component: string; readonly route: ActivatedRouteSnapshot } {
  let route: ActivatedRouteSnapshot | null = deepest(rig);
  while (route && !route.component) {
    route = route.parent;
  }
  // The bundler may prefix a class's name with an underscore.
  return { component: ((route?.component as Type<unknown> | undefined)?.name ?? '').replace(/^_/, ''), route: route! };
}

/** What a URL routes to: the component, the page family and entity route, and the parameters. */
async function match(rig: TmAppRig, url: string): Promise<Record<string, unknown>> {
  await rig.router.navigateByUrl(url);
  const { component, route } = routed(rig);
  const data = route.data as { kind?: string } & TmRouteDataInternal;
  return { url: rig.router.url, component, kind: data.kind, entity: data.ɵentity, params: { ...route.params } };
}

describe('the route table', () => {
  let rig: TmAppRig;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    rig = await tmAppRig();
    harness = await tmNavigate('/17');
  });

  it.each(TM_ROUTE_CASES.map((row) => [row.url, row.routed] as const))('routes %s', async (url, routed) => {
    const { url: canonical, ...expected } = routed;
    expect(await match(rig, url)).toEqual({ url: canonical ?? url, kind: undefined, entity: undefined, ...expected });
  });

  it('renders the not-found page outside any shell in its own main landmark', async () => {
    await rig.router.navigateByUrl('/nope');
    await tmSettle();
    harness.detectChanges();
    expect(harness.fixture.nativeElement.querySelector('tm-not-found-page > main > tm-route-error')).not.toBeNull();
  });

  it('routes the tenantless pages to the shells', async () => {
    expect(await match(rig, '/tenants')).toMatchObject({ component: 'TmTenantPicker' });
    expect(await match(rig, '/signed-out')).toMatchObject({ component: 'TmSignedOutPage' });
    await rig.router.navigateByUrl('/17/centers');
    expect((rig.router.routerState.snapshot.root.firstChild?.component as Type<unknown>).name.replace(/^_/, '')).toBe('TmTenantShell');
  });

  it('serves a path with a trailing slash at its canonical form', async () => {
    expect(await match(rig, '/17/')).toMatchObject({ url: '/17', kind: 'home' });
    expect(await match(rig, '/17/centers//?q=jed#d=x')).toMatchObject({ url: '/17/centers?q=jed#d=x', entity: 'search' });
  });

  describe('the leave checks', () => {
    /** Registers a refusing check in the routed host and returns how often it ran. */
    function refuseLeaving(): { calls: number } {
      const counter = { calls: 0 };
      const host = harness.fixture.debugElement.query(By.directive(TmRouteHost));
      host.injector.get(TmLeaveChecks).add(() => {
        counter.calls++;
        return false;
      }, host.injector.get(DestroyRef));
      return counter;
    }

    it('keeps a settings tab change in place, with no check', async () => {
      await rig.router.navigateByUrl('/17/settings/general');
      const checks = refuseLeaving();
      expect(await rig.router.navigateByUrl('/17/settings/invoicing')).toBe(true);
      expect(checks.calls).toBe(0);
      expect(await rig.router.navigateByUrl('/17/centers')).toBe(false);
      expect(checks.calls).toBe(1);
      expect(rig.router.url).toBe('/17/settings/invoicing');
    });

    it('checks a move between My settings sections', async () => {
      await rig.router.navigateByUrl('/17/me/profile');
      const checks = refuseLeaving();
      expect(await rig.router.navigateByUrl('/17/me/region')).toBe(false);
      expect(checks.calls).toBe(1);
      expect(rig.router.url).toBe('/17/me/profile');
    });

    it('checks a step to another record on the same route', async () => {
      await rig.router.navigateByUrl('/17/centers/5');
      const checks = refuseLeaving();
      expect(await rig.router.navigateByUrl('/17/centers/6')).toBe(false);
      expect(checks.calls).toBe(1);
      // A query change keeps the page, so nothing is asked.
      expect(await rig.router.navigateByUrl('/17/centers/5?child=Lines:2')).toBe(true);
      expect(checks.calls).toBe(1);
    });
  });

  afterEach(() => TestBed.resetTestingModule());
});
