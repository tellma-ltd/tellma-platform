// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signal, type Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By, Title } from '@angular/platform-browser';
import type { RouterTestingHarness } from '@angular/router/testing';

import type { TmApiUpdateCheck } from '../api/tm-api-seams';
import { TM_BOOT_RESULT } from '../boot/tm-boot-result';
import type { TmBootResult } from '../boot/tm-boot-types';
import { TmClientStore } from '../state/store/tm-client-store';
import { tmAppRig, tmLoadTestPage, tmNavigate, tmSettle, TM_COLD_SYNC, TmTestPage, type TmAppRig } from './tm-app.testing.util';
import { TM_ROUTE_ERROR_VIEWS, TmRouteError, type TmRouteErrorKind } from './tm-route-error';
import { TmRouteHost } from './tm-route-host';
import { TM_ROUTE_PERMISSIONS } from './tm-route-permissions';

/** A promise and the function that resolves it. */
function deferred<T>(): { readonly promise: Promise<T>; readonly resolve: (value: T) => void } {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((settle) => (resolve = settle));
  return { promise, resolve };
}

/** The routed host's element. */
function hostElement(harness: RouterTestingHarness): HTMLElement {
  return harness.fixture.debugElement.query(By.directive(TmRouteHost)).nativeElement as HTMLElement;
}

/** The routed host's instance. */
function hostOf(harness: RouterTestingHarness): TmRouteHost {
  return harness.fixture.debugElement.query(By.directive(TmRouteHost)).componentInstance as TmRouteHost;
}

/** The answer of a cold `client/sync` into tenant 18. */
const COLD_SYNC_18 = {
  ...TM_COLD_SYNC,
  tenant: { ...TM_COLD_SYNC.tenant, id: 18 },
  settings: { ...TM_COLD_SYNC.settings, tenantId: 18 },
};

/** A boot script's result for a returning user: the same user as the boot record's. */
const RETURNING: TmBootResult = { state: 'run', profile: null, record: null, cold: false, auto: null, mark: null, showPage: () => undefined };

describe('the route host', () => {
  let rig: TmAppRig;

  afterEach(() => {
    tmLoadTestPage.mockReset();
    tmLoadTestPage.mockImplementation(() => Promise.resolve(TmTestPage));
    TestBed.resetTestingModule();
  });

  it('draws the page skeleton in the frame of the navigation, before the page loads', async () => {
    rig = await tmAppRig();
    const page = deferred<Type<unknown>>();
    tmLoadTestPage.mockImplementationOnce(() => page.promise);
    const harness = await tmNavigate('/17/credit-review');
    const host = hostElement(harness);
    expect(host.getAttribute('aria-busy')).toBe('true');
    expect(host.querySelector('tm-page-skeleton')).not.toBeNull();
    expect(host.querySelector('tm-test-page')).toBeNull();

    page.resolve(TmTestPage);
    await tmSettle();
    expect(host.getAttribute('aria-busy')).toBeNull();
    expect(host.querySelector('tm-page-skeleton')).toBeNull();
    expect(host.querySelector('tm-test-page')).not.toBeNull();
  });

  it('renders a warm entry while every call to the server is still unanswered', async () => {
    rig = await tmAppRig();
    for (const route of ['client/sync', '/bff/user', '/api/distribution-info']) {
      rig.api.respond(route, () => new Promise(() => undefined));
    }
    const harness = await tmNavigate('/17/credit-review');
    await tmSettle();
    expect(hostElement(harness).querySelector('tm-test-page')).not.toBeNull();
  });

  it('keeps a cold entry on its skeleton until the launch documents arrive', async () => {
    rig = await tmAppRig({ cold: true });
    const sync = deferred<unknown>();
    rig.api.respond('client/sync', () => sync.promise);
    const harness = await tmNavigate('/17/credit-review');
    await tmSettle();
    const host = hostElement(harness);
    // The page's chunk is loaded, but the page waits for the tenant's documents.
    expect(tmLoadTestPage).toHaveBeenCalled();
    expect(host.querySelector('tm-page-skeleton')).not.toBeNull();
    expect(host.querySelector('tm-test-page')).toBeNull();

    sync.resolve(TM_COLD_SYNC);
    await tmSettle();
    expect(host.querySelector('tm-test-page')).not.toBeNull();
  });

  it('enters another tenant afresh on the same route, on its skeleton until its cold sync answers', async () => {
    rig = await tmAppRig();
    const sync = deferred<unknown>();
    rig.api.respond('client/sync', () => sync.promise);
    const harness = await tmNavigate('/17/credit-review');
    await tmSettle();
    const before = hostOf(harness);
    expect(hostElement(harness).querySelector('tm-test-page')).not.toBeNull();

    // Tenant 18 has no documents in the store: its entry is cold.
    await rig.router.navigateByUrl('/18/credit-review');
    await tmSettle();
    expect(hostOf(harness)).not.toBe(before);
    expect(hostElement(harness).querySelector('tm-page-skeleton')).not.toBeNull();
    expect(hostElement(harness).querySelector('tm-test-page')).toBeNull();

    sync.resolve(COLD_SYNC_18);
    await tmSettle();
    expect(hostElement(harness).querySelector('tm-test-page')).not.toBeNull();
  });

  it('keeps the shell, and a host whose route stays, within a tenant', async () => {
    rig = await tmAppRig();
    const harness = await tmNavigate('/17/centers/5');
    await tmSettle();
    const shell = harness.fixture.debugElement.query(By.css('tm-tenant-shell')).componentInstance as unknown;
    const host = hostOf(harness);

    await rig.router.navigateByUrl('/17/centers/6');
    await tmSettle();
    expect(hostOf(harness)).toBe(host);
    await rig.router.navigateByUrl('/17/credit-review');
    await tmSettle();
    expect(harness.fixture.debugElement.query(By.css('tm-tenant-shell')).componentInstance).toBe(shell);
  });

  it('moves focus after a host is left while its failed cold entry waits', async () => {
    rig = await tmAppRig({ cold: true });
    rig.api.respond('client/sync', () => Promise.reject(new TypeError('Failed to fetch')));
    const harness = await tmNavigate('/tenants');
    await tmSettle();
    await rig.router.navigateByUrl('/17/credit-review');
    await tmSettle();
    expect(hostElement(harness).querySelector('tm-page-skeleton')).not.toBeNull();

    // Leaving destroys the waiting host, which gives its focus hold back.
    await rig.router.navigateByUrl('/signed-out');
    await tmSettle();
    expect((document.activeElement as HTMLElement | null)?.textContent).toContain("You're signed out");
  });

  it.each<[string, boolean, string]>([
    ['warm', false, 'tm.launch.warm'],
    ['cold', true, 'tm.launch.cold'],
  ])('times a returning user’s launch into a %s tenant', async (_entry, cold, timing) => {
    rig = await tmAppRig({ cold, providers: [{ provide: TM_BOOT_RESULT, useValue: RETURNING }] });
    await tmNavigate('/17/credit-review');
    await tmSettle();
    const launches = rig.telemetry.recent().filter((item) => item.name.startsWith('tm.launch.'));
    expect(launches.map((item) => [item.name, item.props['routeKind']])).toEqual([[timing, 'custom']]);
  });

  it('renders the forbidden view in place of a page whose permission the user lacks', async () => {
    const can = vi.fn(() => signal(false).asReadonly());
    rig = await tmAppRig({ providers: [{ provide: TM_ROUTE_PERMISSIONS, useValue: { can } }] });
    const harness = await tmNavigate('/17/approvals');
    await tmSettle();
    const error = harness.fixture.debugElement.query(By.directive(TmRouteError));
    expect((error.componentInstance as TmRouteError).view().title[0]).toBe('tm.errors.forbidden.title');
    expect((error.nativeElement as HTMLElement).textContent).toContain('acme.Approval · Read');
    expect(can).toHaveBeenCalledWith('acme.Approval', 'Read');
    expect(hostElement(harness).querySelector('tm-test-page')).toBeNull();
  });

  it.each<[TmApiUpdateCheck, TmRouteErrorKind]>([
    ['ready', 'client-outdated'],
    ['failed', 'client-outdated'],
    ['none', 'offline'],
    ['unreachable', 'offline'],
  ])('asks for an update when a chunk fails to load: %s shows %s', async (update, kind) => {
    rig = await tmAppRig({ update });
    tmLoadTestPage.mockImplementationOnce(() => Promise.reject(new TypeError('Failed to fetch dynamically imported module')));
    const harness = await tmNavigate('/17/credit-review');
    await tmSettle();
    const error = harness.fixture.debugElement.query(By.directive(TmRouteError));
    expect((error.componentInstance as TmRouteError).view()).toBe(TM_ROUTE_ERROR_VIEWS[kind]);
  });

  it('titles the document after the page and moves focus to its title on a path change', async () => {
    rig = await tmAppRig();
    await tmNavigate('/17');
    await tmSettle();
    expect(TestBed.inject(Title).getTitle()).toBe('Home · Acme');

    await rig.router.navigateByUrl('/17/centers');
    await tmSettle();
    expect(TestBed.inject(Title).getTitle()).toBe('Centers · Acme');
    const title = document.activeElement as HTMLElement | null;
    expect(title?.hasAttribute('tmPageTitle')).toBe(true);
    expect(title?.textContent).toContain('Centers');
  });

  it('remembers the tenant each navigation enters', async () => {
    rig = await tmAppRig();
    const store = TestBed.inject(TmClientStore);
    await tmNavigate('/tenants');
    expect(store.boot.read().lastTenantId).toBeNull();
    await rig.router.navigateByUrl('/17/centers');
    expect(store.boot.read().lastTenantId).toBe(17);
    // A first segment beyond the safe integers is no tenant, so it is never remembered.
    await rig.router.navigateByUrl('/99999999999999999999');
    expect(store.boot.read().lastTenantId).toBe(17);
  });
});
