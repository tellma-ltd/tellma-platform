// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, createEnvironmentInjector, DestroyRef, EnvironmentInjector, signal, type WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import {
  TM_NOW,
  TmClientStore,
  type TmMeResult,
  type TmPageContext,
  TmPageContexts,
  type TmPageKind,
  type TmPartition,
  type TmStoredDoc,
  type TmTenantFacts,
  ɵTM_API_HOST,
} from '@tellma/core';
import { provideTellmaStateTesting, provideTellmaTesting, TmFakeApi, TmFakeTabBus } from '@tellma/core-testing';

import { TmFakeApiHost } from '../../src/api/tm-api.testing.util';
import { tmAppRig, tmNavigate, TM_T17_DOCS } from '../../src/routes/tm-app.testing.util';
import { TM_RECENTS_NAVIGATION, TmRecents, type TmRecentsNavigation } from './tm-recents';

// Recent pages over the preferences, an in-memory store, routed page
// contexts the spec registers at the router's URL, a launcher the spec
// scripts, and faked timers; then a feature page's default context in a
// whole test app.

/** A page for every route. */
@Component({ template: '' })
class TmPage {}

const NOW = 1_792_400_000_000;
const T17: TmPartition = { kind: 'tenant', sub: 'test-user', tenantId: 17 };

/** What a spec drives and inspects. */
interface TmRig {
  readonly recents: TmRecents;
  readonly host: TmFakeApiHost;
  readonly api: TmFakeApi;
  /** Each URL's access, which the spec changes. */
  readonly access: WritableSignal<Readonly<Record<string, 'ok' | 'no-access' | 'unavailable'>>>;
  /** Navigates to a URL, and replaces the routed context with one of a kind, titled, on the list's template or another. */
  visit(url: string, kind: TmPageKind, title?: string, list?: boolean): Promise<void>;
  /** Registers another routed context on the same URL, which replaces the one before. */
  replace(kind: TmPageKind, title: string): void;
  /** The stored `nav.recent` value, as this tab shows it. */
  stored(): unknown;
}

/** Configures recents on `/17/centers`, with tenant 17's documents stored. */
async function setUp(bag: Readonly<Record<string, string>> = {}): Promise<TmRig> {
  const host = new TmFakeApiHost();
  const access = signal<Readonly<Record<string, 'ok' | 'no-access' | 'unavailable'>>>({});
  const navigation: TmRecentsNavigation = {
    entry: (href) => (href === '/17/centers' ? { label: signal('Centers').asReadonly(), icon: 'building' } : undefined),
    resolve: (href) => access()[href] ?? 'ok',
  };
  TestBed.configureTestingModule({
    providers: [
      provideTellmaTesting(),
      provideTellmaStateTesting(),
      provideRouter([{ path: '**', component: TmPage }]),
      { provide: ɵTM_API_HOST, useValue: host },
      { provide: TM_NOW, useValue: () => Date.now() },
      { provide: TM_RECENTS_NAVIGATION, useValue: navigation },
    ],
  });
  await TestBed.inject(Router).navigateByUrl('/17/centers');
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
  vi.setSystemTime(NOW);
  const stored = <T>(data: T): TmStoredDoc<T> => ({ tag: null, savedAt: NOW, data });
  const store = TestBed.inject(TmClientStore);
  await store.ɵwrite(T17, {
    settings: () => stored(TM_T17_DOCS.settings),
    me: () => stored<TmMeResult>({ ...TM_T17_DOCS.me, preferences: bag }),
    tenant: () => stored<TmTenantFacts>(TM_T17_DOCS.tenant),
    sync: () => stored({ at: NOW, entities: 'E1', entityTags: {} }),
  });
  const api = TestBed.inject(TmFakeApi);
  api.respond('users/me/preferences/set', (call: { body: unknown }) => ({
    preferences: Object.fromEntries((call.body as { items: { key: string; value: string }[] }).items.map((item) => [item.key, item.value])),
    preferencesTag: 'P2',
  }));
  const recents = TestBed.inject(TmRecents);
  await tick(0);

  // Each routed context lives in a lifetime of its own, which the next visit ends.
  const contexts = TestBed.inject(TmPageContexts);
  let lifetime: EnvironmentInjector | null = null;
  const register = (kind: TmPageKind, title: string, routeTemplate: string, end: boolean): void => {
    if (end) {
      lifetime?.destroy();
    }
    lifetime = createEnvironmentInjector([], TestBed.inject(EnvironmentInjector));
    const context: TmPageContext = {
      kind,
      routeTemplate,
      title: signal(title).asReadonly(),
      status: signal<'loading' | 'ready' | 'error'>('ready').asReadonly(),
      state: signal({}).asReadonly(),
      errors: signal([]).asReadonly(),
      actions: signal([]).asReadonly(),
    };
    contexts.register(context, lifetime.get(DestroyRef));
  };
  return {
    recents,
    host,
    api,
    access,
    visit: async (url, kind, title = url, list = false) => {
      await TestBed.inject(Router).navigateByUrl(url);
      register(kind, title, list ? '/:t/list/:segment' : '/:t/page', true);
    },
    replace: (kind, title) => register(kind, title, '/:t/page', false),
    stored: () => {
      const text = store.doc<TmMeResult>(T17, 'me')()?.data.preferences['nav.recent'];
      return text === undefined ? undefined : (JSON.parse(text) as unknown);
    },
  };
}

/** Moves the clock, running the effects and the timers due meanwhile. */
async function tick(ms: number): Promise<void> {
  TestBed.tick();
  await vi.advanceTimersByTimeAsync(ms);
  TestBed.tick();
  await vi.advanceTimersByTimeAsync(0);
}

/** The pages' URLs. */
function urls(rig: TmRig): string[] {
  return rig.recents.pages().map((page) => page.url);
}

describe('TmRecents', () => {
  afterEach(() => vi.useRealTimers());

  it('counts a page of a listed kind that stays current for 2 s', async () => {
    const rig = await setUp();
    await rig.visit('/17/centers?q=jed', 'search', 'Centers');
    await tick(1_999);
    expect(urls(rig)).toEqual([]);
    await tick(1);
    expect(urls(rig)).toEqual(['/17/centers']);
    expect(rig.recents.pages()[0]).toMatchObject({ kind: 'search', visitedAt: NOW + 2_000 });
  });

  it('counts the search, report, dashboard, settings, access and custom kinds only, and never the drill-through list', async () => {
    const rig = await setUp();
    const kinds: [string, TmPageKind][] = [
      ['/17/centers', 'search'],
      ['/17/reports/42', 'report'],
      ['/17/dashboards/7', 'dashboard'],
      ['/17/settings/general', 'settings'],
      ['/17/roles', 'access'],
      ['/17/credit-review', 'custom'],
      ['/17', 'home'],
      ['/17/centers/5', 'details'],
      ['/17/studio/reports/42', 'studio'],
    ];
    for (const [url, kind] of kinds) {
      await rig.visit(url, kind);
      await tick(2_000);
    }
    await rig.visit('/17/list/centers', 'search', 'Centers', true);
    await tick(2_000);
    expect(urls(rig)).toEqual(['/17/credit-review', '/17/roles', '/17/settings/general', '/17/dashboards/7', '/17/reports/42', '/17/centers']);
  });

  it('never counts a page left, or hidden, before 2 s; showing it again starts the 2 s afresh', async () => {
    const rig = await setUp();
    await rig.visit('/17/centers', 'search');
    await tick(1_500);
    await rig.visit('/17/agents', 'search');
    await tick(1_000);
    rig.host.visibleNow = false;
    rig.host.fire('document', 'visibilitychange');
    await tick(5_000);
    expect(urls(rig)).toEqual([]);
    rig.host.visibleNow = true;
    rig.host.fire('document', 'visibilitychange');
    await tick(1_999);
    expect(urls(rig)).toEqual([]);
    await tick(1);
    expect(urls(rig)).toEqual(['/17/agents']);
  });

  it('moves a revisited page to the front, and counts a query change on the same page once', async () => {
    const rig = await setUp();
    for (const url of ['/17/centers', '/17/agents', '/17/centers?q=a', '/17/centers?q=b']) {
      await rig.visit(url, 'search');
      await tick(2_000);
    }
    expect(urls(rig)).toEqual(['/17/centers', '/17/agents']);
    // Counted at the return from the agents, not again at the query change.
    expect(rig.recents.pages()[0]!.visitedAt).toBe(NOW + 6_000);
  });

  it('keeps one visit when a context replaces another on the same path, named by the context at its end', async () => {
    const rig = await setUp();
    await rig.visit('/17/centers', 'search', 'Centers');
    await tick(1_500);
    rig.replace('search', 'Cost centers');
    await tick(499);
    expect(urls(rig)).toEqual([]);
    await tick(1);
    expect(rig.recents.pages().map((page) => [page.url, page.visitedAt])).toEqual([['/17/centers', NOW + 2_000]]);
    // The stored title is the one displayed at the end of the 2 s; nav.recent is saved 2 s later.
    await tick(2_000);
    expect((rig.stored() as { n: string }[] | undefined)?.map((visit) => visit.n)).toEqual(['Cost centers']);
  });

  it('counts a new path under the same context as a visit of its own', async () => {
    const rig = await setUp();
    await rig.visit('/17/settings/general', 'settings');
    await tick(2_000);
    await TestBed.inject(Router).navigateByUrl('/17/settings/email');
    await tick(2_000);
    expect(urls(rig)).toEqual(['/17/settings/email', '/17/settings/general']);
  });

  it('keeps ten pages: the eleventh falls off', async () => {
    const rig = await setUp();
    for (let i = 1; i <= 11; i++) {
      await rig.visit(`/17/reports/${i}`, 'report');
      await tick(2_000);
    }
    expect(urls(rig)).toHaveLength(10);
    expect(urls(rig)[0]).toBe('/17/reports/11');
    expect(urls(rig)).not.toContain('/17/reports/1');
  });

  it("stores each visit's tenant-relative path, kind, time in seconds and title, in nav.recent", async () => {
    const rig = await setUp();
    await rig.visit('/17/reports/42?run=1', 'report', 'Monthly sales by region');
    await tick(2_000);
    // A write-behind key: flushed 2 s after its change.
    await tick(2_000);
    expect(rig.stored()).toEqual([{ p: 'reports/42', k: 'report', t: Math.floor((NOW + 2_000) / 1_000), n: 'Monthly sales by region' }]);
    expect(rig.api.calls().filter((call) => call.route === 'users/me/preferences/set')).toHaveLength(1);
  });

  it("titles a page by its launcher entry, else by the title displayed at the visit", async () => {
    const rig = await setUp({
      'nav.recent': JSON.stringify([
        { p: 'reports/42', k: 'report', t: 1_792_399_000, n: 'Monthly sales' },
        { p: 'centers', k: 'search', t: 1_792_398_000, n: 'Cost centers' },
      ]),
    });
    const [report, centers] = rig.recents.pages();
    expect([report!.title(), report!.icon, report!.visitedAt]).toEqual(['Monthly sales', null, 1_792_399_000_000]);
    expect([centers!.title(), centers!.icon]).toEqual(['Centers', 'building']);
  });

  it('skips a page the user cannot open, brings it back when access returns, and keeps nav.recent unchanged', async () => {
    const stored = [
      { p: 'reports/42', k: 'report', t: 1_792_399_000, n: 'Monthly sales' },
      { p: 'centers', k: 'search', t: 1_792_398_000, n: 'Centers' },
    ];
    const rig = await setUp({ 'nav.recent': JSON.stringify(stored) });
    rig.access.set({ '/17/centers': 'no-access', '/17/reports/42': 'unavailable' });
    expect(urls(rig)).toEqual([]);
    rig.access.set({});
    expect(urls(rig)).toEqual(['/17/reports/42', '/17/centers']);
    expect(rig.stored()).toEqual(stored);
    expect(rig.api.calls()).toEqual([]);
  });

  it("keeps this tab's unsaved visits when a tab opened later sets a list without them", async () => {
    const rig = await setUp();
    const bus = TestBed.inject(TmFakeTabBus);
    const at = (ms: number): number => Math.floor((NOW + ms) / 1_000);
    // Centers counts at 2 s and is saved 2 s later; nav.recent's 5 minutes then hold the next ones.
    await rig.visit('/17/centers', 'search', 'Centers');
    await tick(4_000);
    expect(rig.stored()).toEqual([{ p: 'centers', k: 'search', t: at(2_000), n: 'Centers' }]);
    await rig.visit('/17/agents', 'search', 'Agents');
    await tick(30_000);
    await rig.visit('/17/reports/42', 'report', 'Report 42');
    await tick(30_000);
    expect(urls(rig)).toEqual(['/17/reports/42', '/17/agents', '/17/centers']);

    // A tab opened since starts from the stored list, and its first visit sets it with Units in front.
    const other = [
      { p: 'units', k: 'search', t: at(64_000), n: 'Units' },
      { p: 'centers', k: 'search', t: at(2_000), n: 'Centers' },
    ];
    const posts = bus.posted().length;
    bus.receive({ type: 'prefs.changed', tenantId: 17, values: { 'nav.recent': JSON.stringify(other) } });
    await tick(0);
    expect(urls(rig)).toEqual(['/17/units', '/17/reports/42', '/17/agents', '/17/centers']);
    // This tab sets the merged list, which the other tab adopts as is, since it holds that tab's visit.
    const merged = bus.posted().slice(posts).filter((message) => message.type === 'prefs.changed');
    expect(merged).toHaveLength(1);
    expect(JSON.parse((merged[0] as { values: Record<string, string> }).values['nav.recent']!)).toEqual([
      other[0],
      { p: 'reports/42', k: 'report', t: at(36_000), n: 'Report 42' },
      { p: 'agents', k: 'search', t: at(6_000), n: 'Agents' },
      other[1],
    ]);
    // The merged list is what this tab saves once the 5 minutes are over.
    await tick(300_000);
    expect((rig.stored() as { p: string }[]).map((visit) => visit.p)).toEqual(['units', 'reports/42', 'agents', 'centers']);
  });

  it('reads an invalid stored list as empty', async () => {
    const rig = await setUp({ 'nav.recent': JSON.stringify([{ p: 'centers', k: 'details', t: 1, n: 'x' }]) });
    expect(urls(rig)).toEqual([]);
  });
});

describe('the routed page and the launcher, until their owners land', () => {
  afterEach(() => vi.useRealTimers());

  it('counts a feature page by the default context its host registers', async () => {
    await tmAppRig();
    // Running before the navigation, as the shell starts it.
    const recents = TestBed.inject(TmRecents);
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    const navigated = tmNavigate('/17/credit-review');
    for (let i = 0; i < 5; i++) {
      await tick(0);
    }
    await navigated;
    const current = TestBed.inject(TmPageContexts).current()!;
    expect(current).toMatchObject({ kind: 'custom', routeTemplate: '/:t/credit-review' });
    expect(recents.pages()).toEqual([]);
    await tick(2_000);
    expect(recents.pages().map((page) => [page.url, page.kind, page.title()])).toEqual([['/17/credit-review', 'custom', current.title()]]);
  });

  it('counts a search page by the default context its host registers, and never the drill-through list', async () => {
    await tmAppRig();
    const recents = TestBed.inject(TmRecents);
    // The home page, which never counts, renders the router outlet.
    await tmNavigate('/17');
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    /** Navigates the app, and lets the page render. */
    const go = async (url: string): Promise<void> => {
      const navigated = TestBed.inject(Router).navigateByUrl(url);
      for (let i = 0; i < 5; i++) {
        await tick(0);
      }
      await navigated;
    };
    await go('/17/centers?q=jed');
    await tick(2_000);
    await go('/17/list/centers');
    await tick(2_000);
    expect(recents.pages().map((page) => [page.url, page.kind, page.title()])).toEqual([['/17/centers', 'search', 'Centers']]);
  });

  it('finds no launcher entry, and calls a URL no route reaches unavailable', async () => {
    await tmAppRig();
    const navigation = TestBed.inject(TM_RECENTS_NAVIGATION);
    expect(navigation.entry('/17/centers')).toBeUndefined();
    expect(navigation.resolve('/17/centers')).toBe('ok');
    expect(navigation.resolve('/17/nothing-here/5/6')).toBe('unavailable');
  });
});
