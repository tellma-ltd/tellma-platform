// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { ChangeDetectionStrategy, Component, signal, type Provider } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router } from '@angular/router';
import type { RouterTestingHarness } from '@angular/router/testing';
import {
  TM_NOW,
  TmLocalization,
  TmPageContexts,
  TmUrls,
  tmEnglish,
  tmFeature,
  type TmFeature,
  type TmLanguageDefinition,
  type TmLaunchDocs,
  type TmClientMetadata,
  type TmLazyComponent,
} from '@tellma/core';
import { TmAttention } from '@tellma/core/attention';
import { TmRecents, type TmRecentPage } from '@tellma/core/recents';
import { TmHomePageHarness, tmFakeAssignmentsSummary } from '@tellma/core-testing';
import { TmIcon } from '@tellma/core-ui/icon';
import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';

import { tmTestAssignment } from '../../src/attention/tm-attention.testing.util';
import {
  TM_COLD_SYNC,
  TM_T17,
  TM_T17_DOCS,
  tmAppRig,
  tmAssignmentsTestRoute,
  tmGatedCoreWidget,
  tmNavigate,
  tmRouteTestFeature,
  tmSettle,
  tmSettleUntil,
  type TmAppRig,
} from '../../src/routes/tm-app.testing.util';
import { TmDashboardGrid } from './tm-dashboard-grid';
import { tmTimeOfDay, tmUntilNextBoundary } from './tm-home-greeting';
import { TM_HOME_WIDGETS } from './tm-home-widgets';

// The tenant home in a whole test app at /17, read through its harnesses:
// the route's home skeleton, then the greeting over the three home lists;
// the assignment lists from the stored document, with their counts, their
// empty, offline and loading states, and an arrival that changes only its
// own list; the page context and the visit's telemetry; the greeting's
// parts of the day, its name and its date in the user's zone and calendar,
// kept current by its timer and a return to the tab; the recent pages as
// links; and a feature's home hatch in the page's place, which takes the
// arrival focus once it renders and is timed as a hatch.

/** 09:00 on Thursday 26 November 2026 in Riyadh, tenant 17's zone. */
const MORNING = Date.UTC(2026, 10, 26, 6, 0, 0);

/** An hour, in milliseconds. */
const HOUR = 3_600_000;

/**
 * The hour of an instant in a time zone, on a 24-hour clock.
 *
 * @param at - The instant, in epoch milliseconds.
 * @param timeZone - The IANA zone.
 * @returns The hour, 0 to 23.
 */
function hourIn(at: number, timeZone: string): number {
  return Number(new Intl.DateTimeFormat('en-US', { timeZone, hourCycle: 'h23', hour: 'numeric' }).format(at));
}

/** The test app's clock, which a spec moves. */
let now = MORNING;

/** A pending result the spec settles. */
interface TmDeferred<T> {
  /** The result. */
  readonly promise: Promise<T>;
  /** Settles it. */
  readonly resolve: (value: T) => void;
}

/**
 * A result the spec settles.
 *
 * @returns The result and its settler.
 */
function deferred<T>(): TmDeferred<T> {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => (resolve = done));
  return { promise, resolve };
}

/** A feature's home page, in the platform's place. */
@Component({
  selector: 'tm-acme-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<h1 tmPageTitle>Acme home</h1>',
})
class TmAcmeHome {}

/**
 * Starts the test app at its clock, with more features or providers.
 *
 * @param options - The features beside the test feature, the providers, whether the entry is cold, tenant 17's launch documents, the languages and the Core chunk.
 * @returns The rig.
 */
function start(
  options: {
    readonly features?: readonly TmFeature[];
    readonly providers?: readonly Provider[];
    readonly cold?: boolean;
    readonly docs?: TmLaunchDocs;
    readonly languages?: readonly TmLanguageDefinition[];
    readonly metadata?: TmClientMetadata;
  } = {},
): Promise<TmAppRig> {
  return tmAppRig({
    metadata: options.metadata,
    cold: options.cold,
    docs: options.docs,
    languages: options.languages,
    features: [tmRouteTestFeature(), ...(options.features ?? [])],
    providers: [{ provide: TM_NOW, useValue: () => now }, ...(options.providers ?? [])],
  });
}

/**
 * The routed page's element.
 *
 * @param harness - The router harness.
 * @returns The page's root element.
 */
function page(harness: RouterTestingHarness): HTMLElement {
  return harness.routeNativeElement as HTMLElement;
}

/**
 * The greeting's heading text.
 *
 * @param harness - The router harness.
 * @returns The heading.
 */
function greeting(harness: RouterTestingHarness): string {
  return page(harness).querySelector('.tm-home-greeting__title')?.textContent?.trim() ?? '';
}

/**
 * A recent page as the list shows it.
 *
 * @param url - Its URL.
 * @param title - Its title.
 * @param kind - Its family.
 * @param icon - Its launcher entry's icon, if any.
 * @returns The page.
 */
function recent(
  url: string,
  title: string,
  kind: TmRecentPage['kind'],
  icon: string | null,
): TmRecentPage {
  return { url, kind, title: signal(title), icon, visitedAt: MORNING };
}

/**
 * Starts the test app with assignments routed to their own page, and the
 * stored assignments document.
 *
 * @param assignments - The stored document's members.
 * @returns The rig.
 */
function startRouted(assignments: Parameters<typeof tmFakeAssignmentsSummary>[0]): Promise<TmAppRig> {
  const route = tmAssignmentsTestRoute();
  return start({
    metadata: route.metadata,
    features: [route.feature],
    docs: { ...TM_T17_DOCS, assignments: tmFakeAssignmentsSummary(assignments) },
  });
}

/**
 * Stored assignments with captions, numbered from one.
 *
 * @param count - How many.
 * @returns The assignments.
 */
function assignmentsOf(count: number): ReturnType<typeof tmTestAssignment>[] {
  return Array.from({ length: count }, (_, index) =>
    tmTestAssignment(index + 1, { captionJson: JSON.stringify({ code: `HQ-${String(index + 1).padStart(2, '0')}` }), comment: 'Please review' }),
  );
}

/**
 * Adds the tokens and the home tiles' nine-row cell, which the unit page's
 * missing global stylesheet would give: 9 rows of 30 px and 8 gaps of 10 px.
 *
 * @param listRow - The list row's height, as the stylesheet sets it at the viewport's width.
 * @returns The sheet, to remove after the test.
 */
function tileSheet(listRow: '31px' | '44px'): HTMLStyleElement {
  const sheet = document.createElement('style');
  sheet.textContent = `${tmEmitCss(tmTokensDefault)}
:root { --tm-dashboard-list-row: ${listRow}; }
.tm-dashboard-grid__cell { display: block; block-size: 350px; }`;
  document.head.appendChild(sheet);
  return sheet;
}

describe('the tenant home', () => {
  afterEach(() => {
    now = MORNING;
    TestBed.resetTestingModule();
  });

  it("draws the home skeleton's greeting and frames, then the greeting over the three home lists", async () => {
    // A cold entry whose sync the spec answers keeps the route on its skeleton.
    const rig = await start({ cold: true });
    const sync = deferred<unknown>();
    rig.api.respond('client/sync', () => sync.promise);
    const harness = await tmNavigate('/17');
    await tmSettle();
    const root = harness.fixture.nativeElement as HTMLElement;
    // The global stylesheet places the frames, which the tools' check holds to the home layout.
    const frames = [...root.querySelectorAll<HTMLElement>('tm-page-skeleton .tm-home-skeleton > .tm-dashboard-frame')];
    expect(frames).toHaveLength(TM_HOME_WIDGETS.length);
    for (const frame of frames) {
      expect(frame.querySelector('.tm-dashboard-frame__header tm-skeleton')).not.toBeNull();
      expect(frame.querySelectorAll('.tm-dashboard-frame__rows .tm-skeleton__bar')).toHaveLength(5);
    }
    expect(root.querySelector('tm-page-skeleton .tm-home-greeting .tm-skeleton__bar')).not.toBeNull();
    expect(root.querySelector('tm-home-page')).toBeNull();

    sync.resolve(TM_COLD_SYNC);
    await tmSettleUntil(
      () => root.querySelector('.tm-empty-state__heading') !== null,
      'the recently visited list renders',
    );
    expect(root.querySelector('tm-page-skeleton')).toBeNull();
    const home = await TestbedHarnessEnvironment.loader(harness.fixture).getHarness(TmHomePageHarness);
    expect(await home.getGreeting()).toBe('Good morning, Farah');
    const grid = (await home.getGrid())!;
    expect(await grid.getLabel()).toBe('Your work');
    expect(await grid.getTitles()).toEqual(['My assignments', 'Recently visited', 'Assigned by me']);
    // No assignments document yet: the lists show five skeleton rows each, and recents its empty state.
    const [toMe, recents, byMe] = await grid.getTiles();
    expect(await toMe!.getSkeletonRows()).toBe(5);
    expect(await byMe!.getSkeletonRows()).toBe(5);
    expect(await recents!.getState()).toBe('Pages you visit show up here');
    expect(await toMe!.getViewAllHref()).toBeNull();
    await tmSettleUntil(() => rig.telemetry.recent().some((item) => item.name === 'tm.home.rendered'), 'the visit is timed');
    expect(rig.telemetry.recent().find((item) => item.name === 'tm.home.rendered')?.props).toMatchObject({ warm: false, hatch: false, lists: 'skeleton' });
  });

  it('greets by the part of the day: morning from 5, afternoon from 12, evening from 17', () => {
    expect([0, 4, 5, 11, 12, 16, 17, 23].map(tmTimeOfDay)).toEqual([
      'evening',
      'evening',
      'morning',
      'morning',
      'afternoon',
      'afternoon',
      'evening',
      'evening',
    ]);
  });

  it("titles the page with the greeting, the user's name isolated, over today's date in the display zone", async () => {
    // 00:30 on Friday in Riyadh, still Thursday in UTC and in every zone west of UTC+3, so a
    // date read in UTC or in the machine's zone shows the wrong weekday.
    now = Date.UTC(2026, 10, 26, 21, 30);
    await start();
    const harness = await tmNavigate('/17');
    await tmSettleUntil(() => greeting(harness) !== '', 'the greeting renders');

    expect(TestBed.inject(TmLocalization).timeZone()).toBe('Asia/Riyadh');
    expect(new Intl.DateTimeFormat('en-US', { timeZone: 'UTC', weekday: 'long' }).format(now)).toBe('Thursday');
    const title = page(harness).querySelector('h1')!;
    expect(title.hasAttribute('tmPageTitle')).toBe(true);
    expect(title.querySelector('bdi')?.textContent).toBe('Farah');
    const date = page(harness).querySelector('.tm-home-greeting__date')?.textContent;
    expect(date).toContain('Friday');
    expect(date).not.toContain('Thursday');
  });

  it("shows the next day's date from midnight in the display zone", async () => {
    // Half a second before midnight in Riyadh; midnight there is 21:00 UTC, still Thursday in
    // UTC and in every zone west of UTC+3.
    now = Date.UTC(2026, 10, 26, 20, 59, 59, 500);
    await start();
    const harness = await tmNavigate('/17');
    await tmSettleUntil(() => greeting(harness) !== '', 'the greeting renders');
    const date = (): string => page(harness).querySelector('.tm-home-greeting__date')?.textContent ?? '';
    expect(date()).toContain('Thursday');

    now = Date.UTC(2026, 10, 26, 21, 0, 0);
    await new Promise((resolve) => setTimeout(resolve, 600));
    await tmSettle();
    expect(date()).toContain('Friday');
    expect(greeting(harness)).toBe('Good evening, Farah');
  });

  it("names the user by the name in the active content slot, else the record's first", async () => {
    // The tenant's second content language is the user's: the name is the record's second.
    const user = { ...TM_T17_DOCS.me.user, name: 'فرح', name2: 'Farah Second' };
    await start({ docs: { ...TM_T17_DOCS, me: { ...TM_T17_DOCS.me, user } } });
    const slot = vi.spyOn(TestBed.inject(TmLocalization), 'contentSlot').mockReturnValue(2);
    const harness = await tmNavigate('/17');
    await tmSettleUntil(() => greeting(harness) !== '', 'the greeting renders');
    expect(greeting(harness)).toBe('Good morning, Farah Second');
    expect(slot).toHaveBeenCalled();
  });

  it("names the user by the record's first name when the active slot's is blank", async () => {
    const user = { ...TM_T17_DOCS.me.user, name: 'Farah', name2: '  ' };
    await start({ docs: { ...TM_T17_DOCS, me: { ...TM_T17_DOCS.me, user } } });
    vi.spyOn(TestBed.inject(TmLocalization), 'contentSlot').mockReturnValue(2);
    const harness = await tmNavigate('/17');
    await tmSettleUntil(() => greeting(harness) !== '', 'the greeting renders');
    expect(greeting(harness)).toBe('Good morning, Farah');
  });

  it('greets anew at the next boundary in the display zone, and on a return to the tab', async () => {
    // Half a second before noon in Riyadh.
    now = Date.UTC(2026, 10, 26, 8, 59, 59, 500);
    await start();
    const harness = await tmNavigate('/17');
    await tmSettleUntil(() => greeting(harness) !== '', 'the greeting renders');
    expect(greeting(harness)).toBe('Good morning, Farah');

    now = Date.UTC(2026, 10, 26, 9, 0, 0);
    await new Promise((resolve) => setTimeout(resolve, 600));
    await tmSettle();
    expect(greeting(harness)).toBe('Good afternoon, Farah');

    // A hidden tab's timers are throttled; the return reads the clock again.
    now = Date.UTC(2026, 10, 26, 15, 0, 0);
    document.dispatchEvent(new Event('visibilitychange'));
    await tmSettle();
    expect(greeting(harness)).toBe('Good evening, Farah');
  });

  it.each([
    // 00:30 on the night Berlin springs from 02:00 to 03:00; 05:00 is 03:00 UTC.
    ['Europe/Berlin', Date.UTC(2026, 2, 28, 23, 30), Date.UTC(2026, 2, 29, 3, 0)],
    // 00:30 on the night New York springs from 02:00 to 03:00; 05:00 is 09:00 UTC.
    ['America/New_York', Date.UTC(2026, 2, 8, 5, 30), Date.UTC(2026, 2, 8, 9, 0)],
  ])('greets the morning at 05:00 after a night that springs forward in %s, waiting an hour at most', (zone, from, morning) => {
    // The timer's waits, one after another, until the greeting is the morning's.
    const waits: number[] = [];
    let at = from;
    while (tmTimeOfDay(hourIn(at, zone)) !== 'morning') {
      const wait = tmUntilNextBoundary(at, zone);
      waits.push(wait);
      at += wait;
    }
    expect(at).toBe(morning);
    expect(Math.max(...waits)).toBeLessThanOrEqual(HOUR);
  });

  it('waits for midnight in the display zone as the day ends, an hour at most', () => {
    // 23:30 and 23:59:59.5 in Riyadh, three hours ahead of UTC.
    expect(tmUntilNextBoundary(Date.UTC(2026, 10, 26, 20, 30), 'Asia/Riyadh')).toBe(HOUR / 2);
    expect(tmUntilNextBoundary(Date.UTC(2026, 10, 26, 20, 59, 59, 500), 'Asia/Riyadh')).toBe(500);
    // 22:00 waits its hour cap rather than the two hours to midnight.
    expect(tmUntilNextBoundary(Date.UTC(2026, 10, 26, 19, 0), 'Asia/Riyadh')).toBe(HOUR);
  });

  it("lists the recent pages as links with their launcher icons, else their family's, and preloads one on hover or focus", async () => {
    const recents = {
      pages: signal<readonly TmRecentPage[]>([
        recent('/17/centers', 'Centers', 'search', 'building'),
        recent('/17/reports/5', 'Margins', 'report', null),
      ]),
    };
    const rig = await start({ providers: [{ provide: TmRecents, useValue: recents }] });
    const preload = vi.spyOn(TestBed.inject(TmUrls), 'preload').mockImplementation(() => undefined);
    const harness = await tmNavigate('/17');
    await tmSettleUntil(
      () => page(harness).querySelector('.tm-recents-widget__link') !== null,
      'the recent pages render',
    );

    const links = [
      ...page(harness).querySelectorAll<HTMLAnchorElement>('.tm-recents-widget__link'),
    ];
    expect(links.map((link) => [link.getAttribute('href'), link.textContent?.trim()])).toEqual([
      ['/17/centers', 'Centers'],
      ['/17/reports/5', 'Margins'],
    ]);
    const icons = harness.fixture.debugElement
      .queryAll(By.css('.tm-recents-widget__icon'))
      .map((icon) => icon.injector.get(TmIcon).name());
    expect(icons).toEqual(['building', 'chart-column']);

    links[1]!.dispatchEvent(new PointerEvent('pointerenter'));
    links[0]!.focus();
    expect(preload.mock.calls.map(([url]) => url)).toEqual(
      expect.arrayContaining(['/17/reports/5', '/17/centers']),
    );

    // Opening a page reports the recents list and the bucket of its place.
    links[0]!.click();
    await tmSettle();
    expect(rig.telemetry.recent().find((item) => item.name === 'tm.home.item-opened')?.props).toEqual({ list: 'recents', position: '1' });
  });

  it("renders a feature's home hatch in the platform page's place, and times the visit as a hatch's", async () => {
    const hatch = vi.fn<TmLazyComponent>(() => Promise.resolve(TmAcmeHome));
    const rig = await start({ features: [tmFeature({ key: 'shop', home: hatch })] });
    const harness = await tmNavigate('/17');
    await tmSettleUntil(() => page(harness).querySelector('tm-acme-home') !== null, 'the hatch renders');
    const home = await TestbedHarnessEnvironment.loader(harness.fixture).getHarness(TmHomePageHarness);

    expect(await home.getView()).toBe('hatch');
    expect(await home.getGrid()).toBeNull();
    expect(page(harness).querySelector('tm-acme-home')).not.toBeNull();
    expect(hatch).toHaveBeenCalledTimes(1);
    expect(TestBed.inject(TmPageContexts).top()?.state()).toEqual({ hatch: true });
    await tmSettleUntil(() => rig.telemetry.recent().some((item) => item.name === 'tm.home.rendered'), 'the visit is timed');
    expect(rig.telemetry.recent().find((item) => item.name === 'tm.home.rendered')?.props).toMatchObject({ warm: true, hatch: true });
  });

  it("gives a feature's home hatch the arrival focus on an in-app entry, once the hatch renders", async () => {
    const chunk = deferred<typeof TmAcmeHome>();
    const rig = await start({ features: [tmFeature({ key: 'shop', home: () => chunk.promise })] });
    const harness = await tmNavigate('/17/settings');
    await tmSettle();

    // The home's host draws the skeleton while the hatch's chunk loads; the focus waits for the hatch.
    await rig.router.navigateByUrl('/17');
    await tmSettleUntil(() => page(harness).querySelector('tm-home-host tm-page-skeleton') !== null, 'the skeleton draws');
    chunk.resolve(TmAcmeHome);
    await tmSettleUntil(() => page(harness).querySelector('tm-acme-home') !== null, 'the hatch renders');
    await tmSettle();

    expect(document.activeElement).toBe(page(harness).querySelector('tm-acme-home h1'));
  });

  it("leaves the platform's home page in place of a hatch whose chunk fails to load", async () => {
    await start({
      features: [tmFeature({ key: 'shop', home: () => Promise.reject(new Error('chunk')) })],
    });
    const harness = await tmNavigate('/17');
    await tmSettleUntil(() => page(harness).querySelector('tm-home-page') !== null, 'the home page renders');

    expect(page(harness).querySelector('tm-home-page')).not.toBeNull();
    expect(greeting(harness)).toBe('Good morning, Farah');
  });

  it('lists the stored assignments both ways, with their counts, from stored documents and no request, and times the visit', async () => {
    const rig = await start({
      docs: {
        ...TM_T17_DOCS,
        assignments: tmFakeAssignmentsSummary({
          open: 2,
          latest: [
            tmTestAssignment(1, { captionJson: JSON.stringify({ code: 'HQ-01' }), comment: 'Please review' }),
            tmTestAssignment(2, { captionJson: JSON.stringify({ code: 'HQ-02' }), comment: 'Sign off' }),
          ],
        }),
      },
    });
    const harness = await tmNavigate('/17');
    await tmSettleUntil(() => page(harness).querySelector('[data-tm-row-control]') !== null, 'the assignment rows render');
    const home = await TestbedHarnessEnvironment.loader(harness.fixture).getHarness(TmHomePageHarness);
    const grid = (await home.getGrid())!;

    expect(await home.getView()).toBe('page');
    expect(await grid.getTitles()).toEqual(['My assignments', 'Recently visited', 'Assigned by me']);
    const toMe = await grid.getTile({ key: 'home.to-me' });
    expect(await toMe.getCount()).toBe('2');
    expect(await toMe.getRows()).toEqual(['HQ-01 · Please review', 'HQ-02 · Sign off']);
    expect(await toMe.hasMenu()).toBe(false);
    expect(await toMe.getStatusLine()).toBeNull();
    const byMe = await grid.getTile({ key: 'home.by-me' });
    expect(await byMe.getCount()).toBeNull();
    expect(await byMe.getState()).toBe('Nothing you assigned is still open');
    expect(rig.api.calls().filter((call) => call.route.includes('assignments'))).toEqual([]);

    // The visit is timed once, warm, with the lists' document.
    await tmSettleUntil(() => rig.telemetry.recent().some((item) => item.name === 'tm.home.rendered'), 'the visit is timed');
    const rendered = rig.telemetry.recent().filter((item) => item.name === 'tm.home.rendered');
    expect(rendered).toHaveLength(1);
    expect(rendered[0]!.props).toEqual({ warm: true, hatch: false, lists: 'data', ms: expect.any(Number) });

    // Opening a row reports its list and the bucket of its place.
    page(harness).querySelectorAll<HTMLElement>('[data-tm-row-control]')[1]!.click();
    await tmSettle();
    expect(rig.telemetry.recent().find((item) => item.name === 'tm.home.item-opened')?.props).toEqual({ list: 'to-me', position: '2-3' });
  });

  it("publishes the home's page context: its title, no hatch, and the lists' rows with the assignment ids", async () => {
    await start({
      docs: {
        ...TM_T17_DOCS,
        assignments: tmFakeAssignmentsSummary({
          open: 1,
          openByMe: 1,
          latest: [tmTestAssignment(1, { captionJson: JSON.stringify({ code: 'HQ-01' }) })],
          latestByMe: [tmTestAssignment(3, { captionJson: JSON.stringify({ code: 'HQ-03' }), assigneeId: 9, assignedById: 42 })],
        }),
      },
      providers: [{ provide: TmRecents, useValue: { pages: signal([recent('/17/centers', 'Centers', 'search', null)]) } }],
    });
    const harness = await tmNavigate('/17');
    await tmSettleUntil(() => page(harness).querySelector('[data-tm-row-control]') !== null, 'the assignment rows render');
    const context = TestBed.inject(TmPageContexts).top()!;

    expect(context).toMatchObject({ kind: 'home', routeTemplate: '/:t' });
    expect(context.title()).toBe('Home');
    expect(context.state()).toEqual({ hatch: false });
    const visible = context.visible!()!;
    expect(visible.columns.map((column) => column.path)).toEqual(['list', 'title', 'detail', 'when']);
    expect(visible.rows.map((row) => [row.id, row.cells['list']])).toEqual([
      [1, 'to-me'],
      [3, 'by-me'],
      [null, 'recents'],
    ]);
    expect(visible.rows[2]!.cells).toEqual({ list: 'recents', title: 'Centers', detail: '/17/centers', when: new Date(MORNING).toISOString() });
  });

  it('shows the empty states when nothing is assigned either way', async () => {
    await start({ docs: { ...TM_T17_DOCS, assignments: tmFakeAssignmentsSummary() } });
    const harness = await tmNavigate('/17');
    await tmSettleUntil(() => page(harness).querySelectorAll('.tm-empty-state__heading').length === 3, 'the empty states render');
    const grid = (await (await TestbedHarnessEnvironment.loader(harness.fixture).getHarness(TmHomePageHarness)).getGrid())!;

    expect(await Promise.all((await grid.getTiles()).map((tile) => tile.getState()))).toEqual([
      'Nothing is waiting on you',
      'Pages you visit show up here',
      'Nothing you assigned is still open',
    ]);
  });

  it('says the assignments show on reconnecting when offline with no document, and shows skeleton rows online', async () => {
    const rig = await start();
    rig.api.setOnline(false);
    const harness = await tmNavigate('/17');
    await tmSettleUntil(() => page(harness).querySelectorAll('.tm-empty-state__heading').length === 3, 'the offline states render');
    const grid = (await (await TestbedHarnessEnvironment.loader(harness.fixture).getHarness(TmHomePageHarness)).getGrid())!;
    const toMe = await grid.getTile({ key: 'home.to-me' });
    const byMe = await grid.getTile({ key: 'home.by-me' });

    expect(await toMe.getState()).toBe("You're offline. Your assignments show when you reconnect.");
    expect(await byMe.getState()).toBe("You're offline. Your assignments show when you reconnect.");

    rig.api.setOnline(true);
    await tmSettle();
    expect(await toMe.getState()).toBeNull();
    expect(await toMe.getSkeletonRows()).toBe(5);
    expect(await byMe.getSkeletonRows()).toBe(5);
  });

  it('updates only its own list and count when an assignment arrives, and moves no other tile', async () => {
    const rig = await start({
      docs: {
        ...TM_T17_DOCS,
        assignments: tmFakeAssignmentsSummary({ open: 1, latest: [tmTestAssignment(1, { captionJson: JSON.stringify({ code: 'HQ-01' }) })] }),
      },
    });
    const harness = await tmNavigate('/17');
    await tmSettleUntil(() => page(harness).querySelector('[data-tm-row-control]') !== null, 'the assignment rows render');
    const grid = (await (await TestbedHarnessEnvironment.loader(harness.fixture).getHarness(TmHomePageHarness)).getGrid())!;
    const placements = async (): Promise<unknown[]> => Promise.all((await grid.getTiles()).map((tile) => tile.getPlacement()));
    const before = await placements();
    const others = page(harness).querySelectorAll('tm-recents-widget, tm-dashboard-tile[data-tile="home.by-me"] tm-assignments-widget');

    await rig.store.put(TM_T17, 'assignments', {
      tag: null,
      savedAt: Date.now(),
      data: tmFakeAssignmentsSummary({
        open: 2,
        latest: [
          tmTestAssignment(2, { captionJson: JSON.stringify({ code: 'HQ-02' }) }),
          tmTestAssignment(1, { captionJson: JSON.stringify({ code: 'HQ-01' }) }),
        ],
      }),
    });
    await tmSettleUntil(() => page(harness).querySelectorAll('[data-tm-row-control]').length === 2, 'the arrival renders');

    const toMe = await grid.getTile({ key: 'home.to-me' });
    expect(await toMe.getCount()).toBe('2');
    expect(await placements()).toEqual(before);
    // The other lists' bodies are the same elements: nothing re-rendered them.
    expect([...page(harness).querySelectorAll('tm-recents-widget, tm-dashboard-tile[data-tile="home.by-me"] tm-assignments-widget')]).toEqual([...others]);
  });

  it("follows each list's View all in the app and reports it, leaving a modified click to the browser", async () => {
    const rig = await startRouted({ open: 1, latest: assignmentsOf(1) });
    const harness = await tmNavigate('/17');
    await tmSettleUntil(() => page(harness).querySelectorAll('.tm-dashboard-tile__view-all').length === 2, 'the lists link their pages');
    const grid = (await (await TestbedHarnessEnvironment.loader(harness.fixture).getHarness(TmHomePageHarness)).getGrid())!;
    const toMe = await grid.getTile({ key: 'home.to-me' });
    const byMe = await grid.getTile({ key: 'home.by-me' });
    expect(await toMe.getViewAllHref()).toBe('/17/assignments');
    expect(await byMe.getViewAllHref()).toBe('/17/assignments?tab=by-me');
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const views = (): unknown[] => rig.telemetry.recent().filter((item) => item.name === 'tm.home.view-all').map((item) => item.props);

    // A ctrl-click is reported and left to the browser, which this spec stops from opening a tab.
    const link = page(harness).querySelector<HTMLAnchorElement>('tm-dashboard-tile[data-tile="home.to-me"] .tm-dashboard-tile__view-all')!;
    let prevented: boolean | null = null;
    const browser = (event: Event): void => {
      prevented = event.defaultPrevented;
      event.preventDefault();
    };
    window.addEventListener('click', browser);
    try {
      link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, ctrlKey: true }));
    } finally {
      window.removeEventListener('click', browser);
    }
    await tmSettle();
    expect(prevented).toBe(false);
    expect(navigate).not.toHaveBeenCalled();
    expect(views()).toEqual([{ list: 'to-me' }]);

    // A plain click follows the list's page in the app: the by-me tab.
    await byMe.viewAll();
    await tmSettle();
    expect(navigate.mock.calls.map(([url]) => url)).toEqual(['/17/assignments?tab=by-me']);
    expect(views()).toEqual([{ list: 'to-me' }, { list: 'by-me' }]);
  });

  it('reads the assignments document again on a forced refresh only', async () => {
    await startRouted({ open: 1, latest: assignmentsOf(1) });
    const refresh = vi.spyOn(TestBed.inject(TmAttention), 'refresh').mockImplementation(() => Promise.resolve());
    const harness = await tmNavigate('/17');
    await tmSettleUntil(() => page(harness).querySelector('[data-tm-row-control]') !== null, 'the assignment rows render');
    await tmSettle();
    expect(refresh).not.toHaveBeenCalled();

    harness.fixture.debugElement.query(By.directive(TmDashboardGrid)).injector.get(TmDashboardGrid).refreshAll();
    await tmSettle();
    expect(refresh.mock.calls).toEqual([['assignments'], ['assignments']]);
  });

  it('times the visit over the shown tiles when an app hides a home type from the user', async () => {
    const rig = await start({
      docs: { ...TM_T17_DOCS, assignments: tmFakeAssignmentsSummary() },
      features: [tmGatedCoreWidget('core.recents')],
    });
    const harness = await tmNavigate('/17');
    await tmSettleUntil(() => page(harness).querySelectorAll('.tm-empty-state__heading').length === 2, 'the lists render');
    const grid = (await (await TestbedHarnessEnvironment.loader(harness.fixture).getHarness(TmHomePageHarness)).getGrid())!;

    expect(await grid.getTitles()).toEqual(['My assignments', 'Assigned by me']);
    await tmSettleUntil(() => rig.telemetry.recent().some((item) => item.name === 'tm.home.rendered'), 'the visit is timed');
    expect(rig.telemetry.recent().filter((item) => item.name === 'tm.home.rendered')).toHaveLength(1);
  });

  it('fits a full list under its count and View all from sm up, and scrolls the touch rows below it', async () => {
    let sheet = tileSheet('31px');
    try {
      await startRouted({ open: 12, latest: assignmentsOf(10) });
      const harness = await tmNavigate('/17');
      await tmSettleUntil(() => page(harness).querySelectorAll('[data-tm-row-control]').length === 10, 'the ten rows render');
      const tile = page(harness).querySelector<HTMLElement>('tm-dashboard-tile[data-tile="home.to-me"]')!;
      const header = tile.querySelector<HTMLElement>('.tm-card__header')!;
      const body = tile.querySelector<HTMLElement>('.tm-card__body')!;
      // The list scrolls inside the card's body, which neither overflows.
      const list = tile.querySelector<HTMLElement>('tm-assignment-list')!;
      expect(tile.getBoundingClientRect().height).toBe(350);
      expect(header.querySelector('.tm-dashboard-tile__view-all')).not.toBeNull();
      expect(header.textContent).toContain('12');
      // The header is 33 px, 34 with its 1 px rule, whatever its count and link.
      expect(header.getBoundingClientRect().height).toBe(34);
      expect(body.scrollHeight).toBeLessThanOrEqual(body.clientHeight);
      expect(list.scrollHeight).toBeLessThanOrEqual(list.clientHeight);

      // Below sm, the 44 px touch rows outgrow the tile, and its body scrolls.
      sheet.remove();
      sheet = tileSheet('44px');
      await tmSettle();
      expect(header.getBoundingClientRect().height).toBe(34);
      expect(body.scrollHeight).toBeLessThanOrEqual(body.clientHeight);
      expect(list.scrollHeight).toBeGreaterThan(list.clientHeight);
    } finally {
      sheet.remove();
    }
  });

  it("writes the date line in the user's calendar and language: Umm al-Qura in Arabic", async () => {
    await start({
      docs: {
        ...TM_T17_DOCS,
        settings: {
          ...TM_T17_DOCS.settings,
          languages: [
            ...TM_T17_DOCS.settings.languages,
            { code: 'ar', nativeName: 'العربية', symbol: 'ع', isRightToLeft: true },
          ],
          calendars: ['gc', 'uq'],
        },
      },
      languages: [tmEnglish(), { code: 'ar', dir: 'rtl', strings: () => Promise.resolve({ tm: {}, tmUi: {} }) }],
    });
    const harness = await tmNavigate('/17');
    await tmSettleUntil(() => greeting(harness) !== '', 'the greeting renders');
    const l10n = TestBed.inject(TmLocalization);
    l10n.setLanguage('ar');
    l10n.setCalendar('uq');
    await tmSettleUntil(() => l10n.language() === 'ar' && l10n.calendarCode() === 'uq', 'the locale switches');
    await tmSettle();
    const date = page(harness).querySelector('.tm-home-greeting__date')?.textContent ?? '';

    // Thursday 26 November 2026 is Thursday 16 Jumada II 1448 in Riyadh.
    expect(date).toContain('الخميس');
    expect(date).toContain('جمادى');
    expect(date).toContain('١٤٤٨');
  });
});
