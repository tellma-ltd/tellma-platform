// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal, type Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By, Title } from '@angular/platform-browser';
import type { RouterTestingHarness } from '@angular/router/testing';

import type { TmApiUpdateCheck } from '../api/tm-api-seams';
import { TM_BOOT_RESULT } from '../boot/tm-boot-result';
import type { TmBootResult } from '../boot/tm-boot-types';
import { TmPageActions } from '../context/tm-page-actions';
import { TmPageContexts } from '../context/tm-page-contexts';
import type { TmTelemetryItem } from '../observe/tm-telemetry';
import { TmClientStore } from '../state/store/tm-client-store';
import {
  tmAppRig,
  tmLoadTestPage,
  tmNavigate,
  tmSettle,
  tmSettleUntil,
  TM_COLD_SYNC,
  TM_T17_DOCS,
  tmTestMembership,
  TmTestPage,
  type TmAppRig,
} from './tm-app.testing.util';
import { TM_ROUTE_ERROR_VIEWS, TmRouteError, type TmRouteErrorKind } from './tm-route-error';
import { TmPageSkeleton } from './tm-page-skeleton';
import { TmRouteHost } from './tm-route-host';

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

/** A declared page that publishes a richer context than its host's, and an action of its own. */
@Component({ selector: 'tm-credit-review', changeDetection: ChangeDetectionStrategy.OnPush, template: '<h1 tmPageTitle>Credit review</h1>' })
class TmCreditReview {
  /** The threshold the page reviews customers over. */
  readonly threshold = signal(5);

  /** Registers the page's context and its action in the host's page scope. */
  constructor() {
    inject(TmPageContexts).register({
      kind: 'custom',
      routeTemplate: '/:t/credit-review',
      title: signal('Credit review'),
      status: signal('ready'),
      state: computed(() => ({ threshold: this.threshold() })),
      errors: signal([]),
      actions: signal([]),
    });
    inject(TmPageActions).register({
      id: 'acme.credit-review.suggest-limits',
      labelKey: 'Acme_CreditReview_SuggestLimits',
      group: 'primary',
      invoke: () => Promise.resolve({ outcome: 'done' }),
    });
  }
}

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

  it.each<[string, string]>([
    ['/17/centers/5', 'details'],
    ['/17/reports/5', 'report'],
    ['/17/dashboards/3', 'dashboard'],
  ])('draws the skeleton of the details page at %s: %s', async (url, skeleton) => {
    // A cold entry whose sync never answers keeps the skeleton.
    rig = await tmAppRig({ cold: true });
    rig.api.respond('client/sync', () => new Promise(() => undefined));
    const harness = await tmNavigate(url);
    await tmSettle();
    const element = harness.fixture.debugElement.query(By.directive(TmPageSkeleton));
    expect((element.componentInstance as TmPageSkeleton).kind()).toBe(skeleton);
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

  it('moves focus after a host is left while its cold entry waits', async () => {
    rig = await tmAppRig({ cold: true });
    // A cold sync that never answers keeps the host on its skeleton; a failed
    // one would replace the outlet with the tenant shell's own failure.
    rig.api.respond('client/sync', () => new Promise<never>(() => undefined));
    const harness = await tmNavigate('/tenants');
    await tmSettle();
    await rig.router.navigateByUrl('/17/credit-review');
    await tmSettle();
    expect(hostElement(harness).querySelector('tm-page-skeleton')).not.toBeNull();

    // Leaving destroys the waiting host, which gives its focus hold back.
    await rig.router.navigateByUrl('/signed-out');
    await tmSettleUntil(() => document.querySelector('tm-signed-out-page h1') !== null, 'the signed-out page renders');
    expect((document.activeElement as HTMLElement | null)?.textContent).toContain('You signed out');
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

  it.each(['/tenants', '/signed-out', '/17/nope'])('times no launch that lands on %s, nor the tenant page after it', async (url) => {
    rig = await tmAppRig({ providers: [{ provide: TM_BOOT_RESULT, useValue: RETURNING }] });
    await tmNavigate(url);
    await tmSettle();
    await rig.router.navigateByUrl('/17/credit-review');
    await tmSettle();
    expect(rig.telemetry.recent().filter((item) => item.name.startsWith('tm.launch.'))).toEqual([]);
  });

  it('times a launch at / at the tenant page it leads to', async () => {
    rig = await tmAppRig({ providers: [{ provide: TM_BOOT_RESULT, useValue: RETURNING }] });
    rig.api.respond('/bff/user', { tenants: [tmTestMembership(17)] });
    await tmNavigate('/');
    const launches = (): TmTelemetryItem[] => rig.telemetry.recent().filter((item) => item.name.startsWith('tm.launch.'));
    // The redirect follows the memberships' read, and the launch is timed once the
    // tenant page, which loads the shell's chunk, paints.
    await tmSettleUntil(() => launches().length > 0);
    expect(rig.router.url).toBe('/17');
    expect(launches().map((item) => [item.name, item.props['routeKind']])).toEqual([['tm.launch.warm', 'home']]);
  });

  it('renders the forbidden view in place of a page whose permission the user lacks', async () => {
    rig = await tmAppRig();
    const harness = await tmNavigate('/17/approvals');
    await tmSettle();
    const error = harness.fixture.debugElement.query(By.directive(TmRouteError));
    expect((error.componentInstance as TmRouteError).view().title).toBe('tm.errors.forbidden.title');
    // The securables section does not list the pair, so the view names it by its raw names.
    expect((error.nativeElement as HTMLElement).textContent).toContain('acme.Approval: Read');
    expect(hostElement(harness).querySelector('tm-test-page')).toBeNull();
  });

  it('renders a page whose permission the user holds', async () => {
    const securables = [{ resource: 'acme.Approval', action: 'Read', hasFilter: false }];
    rig = await tmAppRig({ docs: { ...TM_T17_DOCS, me: { ...TM_T17_DOCS.me, access: { ...TM_T17_DOCS.me.access, securables } } } });
    const harness = await tmNavigate('/17/approvals');
    await tmSettle();
    expect(harness.fixture.debugElement.query(By.directive(TmRouteError))).toBeNull();
    expect(hostElement(harness).querySelector('tm-test-page')).not.toBeNull();
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
    // An entity's page loads the Entities chunk's model first.
    await tmSettleUntil(() => document.activeElement?.hasAttribute('tmPageTitle') === true && document.activeElement.textContent.includes('Centers'));
    expect(TestBed.inject(Title).getTitle()).toBe('Centers · Acme');
    const title = document.activeElement as HTMLElement | null;
    expect(title?.hasAttribute('tmPageTitle')).toBe(true);
    expect(title?.textContent).toContain('Centers');
  });

  it('keeps focus where it is on a settings tab change, and moves it on the next path change', async () => {
    rig = await tmAppRig();
    await tmNavigate('/17/settings/general');
    await tmSettle();
    // A stand-in for the settings tab list's selected tab.
    const tab = document.body.appendChild(document.createElement('button'));
    try {
      tab.focus();
      await rig.router.navigateByUrl('/17/settings/invoicing');
      await tmSettle();
      expect(document.activeElement).toBe(tab);

      await rig.router.navigateByUrl('/17/credit-review');
      await tmSettle();
      expect((document.activeElement as HTMLElement | null)?.hasAttribute('tmPageTitle')).toBe(true);
    } finally {
      tab.remove();
    }
  });

  it('moves no focus on a navigation whose page places focus itself, and moves it on the next path change', async () => {
    rig = await tmAppRig();
    await tmNavigate('/17/settings/general');
    await tmSettle();
    // A stand-in for the control the new page focuses.
    const control = document.body.appendChild(document.createElement('button'));
    try {
      control.focus();
      await rig.router.navigateByUrl('/17/credit-review', { state: { tmPageFocus: false } });
      await tmSettle();
      expect(document.activeElement).toBe(control);

      await rig.router.navigateByUrl('/17/settings/general');
      await tmSettle();
      expect((document.activeElement as HTMLElement | null)?.hasAttribute('tmPageTitle')).toBe(true);
    } finally {
      control.remove();
    }
  });

  it('moves focus to the title on a Back and a Forward to the entry of a navigation that placed focus itself', async () => {
    rig = await tmAppRig();
    await tmNavigate('/17/settings/general');
    await tmSettle();
    const location = TestBed.inject(Location);
    // The router hears the history's steps, as a browser's Back and Forward.
    rig.router.setUpLocationChangeListener();
    // A stand-in for a control on the page being left.
    const control = document.body.appendChild(document.createElement('button'));
    /**
     * Goes Back or Forward from the control, and says whether focus reached the new page's title.
     *
     * @param move - The history step.
     * @param path - The path it lands on.
     * @returns Whether the title took focus.
     */
    const step = async (move: () => void, path: string): Promise<boolean> => {
      control.focus();
      move();
      await tmSettleUntil(() => location.path() === path, `the history step to ${path}`);
      await tmSettleUntil(() => document.activeElement?.hasAttribute('tmPageTitle') === true, `the title at ${path} takes focus`).catch(() => undefined);
      return (document.activeElement as HTMLElement | null)?.hasAttribute('tmPageTitle') === true;
    };
    try {
      // --- The entry: an app's navigation that places focus itself, its state kept in the history ---
      await rig.router.navigateByUrl('/17/credit-review', { state: { tmPageFocus: false } });
      await tmSettle();
      expect(location.getState()).toEqual(expect.objectContaining({ tmPageFocus: false }));
      await rig.router.navigateByUrl('/17/centers');
      await tmSettleUntil(() => document.activeElement?.hasAttribute('tmPageTitle') === true, 'the centers page takes focus');

      // --- Back to that entry, and Forward to it again: each moves focus to the title ---
      expect(await step(() => location.back(), '/17/credit-review')).toBe(true);
      expect(await step(() => location.back(), '/17/settings/general')).toBe(true);
      expect(await step(() => location.forward(), '/17/credit-review')).toBe(true);
    } finally {
      control.remove();
    }
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

  describe('the page context', () => {
    it("publishes a declared page's context, loading until the page paints, then ready", async () => {
      rig = await tmAppRig();
      const load = deferred<Type<unknown>>();
      tmLoadTestPage.mockImplementationOnce(() => load.promise);
      await tmNavigate('/17/credit-review');
      const contexts = TestBed.inject(TmPageContexts);
      const current = contexts.current()!;
      expect(current).toMatchObject({ kind: 'custom', routeTemplate: '/:t/credit-review' });
      expect([current.status(), current.title(), current.state(), current.errors(), current.actions()]).toEqual([
        'loading',
        'Acme_CreditReview',
        {},
        [],
        [],
      ]);
      load.resolve(TmTestPage);
      await tmSettle();
      expect(current.status()).toBe('ready');
    });

    it("lets the page's own context replace its host's, keeping the actions registered in the host's scope", async () => {
      rig = await tmAppRig();
      tmLoadTestPage.mockImplementationOnce(() => Promise.resolve(TmCreditReview));
      const harness = await tmNavigate('/17/credit-review');
      await tmSettle();
      const contexts = TestBed.inject(TmPageContexts);
      expect(contexts.current()!.state()).toEqual({ threshold: 5 });
      expect(contexts.current()!.actions().map((action) => action.id)).toEqual(['acme.credit-review.suggest-limits']);

      // Leaving the page ends both its context and its host's.
      await harness.navigateByUrl('/17/approvals');
      await tmSettle();
      expect(contexts.current()!.routeTemplate).toBe('/:t/approvals');
    });

    it('turns to error with a client error when the host renders an error view', async () => {
      rig = await tmAppRig();
      await tmNavigate('/17/approvals');
      await tmSettle();
      const current = TestBed.inject(TmPageContexts).current()!;
      expect(current.status()).toBe('error');
      expect(current.errors()).toEqual([{ source: 'client', code: 'forbidden', message: expect.any(String) }]);
      expect(current.errors()[0]!.message).not.toBe('');
    });

    it("publishes every page route's context, the entity's segment in its template, titled as the document is", async () => {
      rig = await tmAppRig();
      // The search page reads its rows: none here.
      rig.api.respond('centers/query', { rows: { columns: [], rows: [] }, count: 0 });
      const harness = await tmNavigate('/17/centers');
      const contexts = TestBed.inject(TmPageContexts);
      // An entity's page loads the Entities chunk's model first.
      await tmSettleUntil(() => contexts.current()?.status() === 'ready');
      const search = contexts.current()!;
      expect(search).toMatchObject({ kind: 'search', routeTemplate: '/:t/centers' });
      expect([search.status(), search.title()]).toEqual(['ready', 'Centers']);

      // A reused route showing another entity publishes that entity's context in place of the first.
      rig.api.respond('customers/query', { rows: { columns: [], rows: [] }, count: 0 });
      await harness.navigateByUrl('/17/customers');
      await tmSettle();
      expect(contexts.current()!.routeTemplate).toBe('/:t/customers');

      // The drill-through list keeps its own template; the others name their route.
      for (const [url, kind, template] of [
        ['/17/list/centers', 'search', '/:t/list/:segment'],
        ['/17/centers/5', 'details', '/:t/centers/:id'],
        ['/17/centers/new', 'details', '/:t/centers/new'],
        ['/17/settings/general', 'settings', '/:t/settings'],
        ['/17/me', 'settings', '/:t/me'],
        ['/17', 'home', '/:t'],
      ] as const) {
        await harness.navigateByUrl(url);
        await tmSettle();
        expect(contexts.current()).toMatchObject({ kind, routeTemplate: template });
      }
      // Only the routed page's context is live.
      expect(contexts.overlays()).toEqual([]);
    });
  });
});
