// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, signal, type Provider } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import type { RouterTestingHarness } from '@angular/router/testing';
import {
  TM_NOW,
  TmLocalization,
  TmUrls,
  tmFeature,
  type TmFeature,
  type TmLaunchDocs,
  type TmLazyComponent,
} from '@tellma/core';
import { TmRecents, type TmRecentPage } from '@tellma/core/recents';
import { TmIcon } from '@tellma/core-ui/icon';

import {
  TM_COLD_SYNC,
  TM_T17_DOCS,
  tmAppRig,
  tmNavigate,
  tmRouteTestFeature,
  tmSettle,
  tmSettleUntil,
  type TmAppRig,
} from '../../src/routes/tm-app.testing.util';
import { tmTimeOfDay, tmUntilNextBoundary } from './tm-home-greeting';
import { TM_HOME_WIDGETS } from './tm-home-widgets';

// The tenant home in a whole test app at /17: the route's home skeleton,
// then the greeting and the recently visited list, the only home widget
// whose type the app declares; the greeting's parts of the day, its name
// and its date, kept current by its timer and a return to the tab; the
// recent pages as links; and a feature's home hatch in the page's place,
// which takes the arrival focus once it renders.

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
 * @param options - The features beside the test feature, the providers, whether the entry is cold, and tenant 17's launch documents.
 * @returns The rig.
 */
function start(
  options: {
    readonly features?: readonly TmFeature[];
    readonly providers?: readonly Provider[];
    readonly cold?: boolean;
    readonly docs?: TmLaunchDocs;
  } = {},
): Promise<TmAppRig> {
  return tmAppRig({
    cold: options.cold,
    docs: options.docs,
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

describe('the tenant home', () => {
  afterEach(() => {
    now = MORNING;
    TestBed.resetTestingModule();
  });

  it("draws the home skeleton's greeting and frames, then the greeting over the recently visited list", async () => {
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
    expect(greeting(harness)).toBe('Good morning, Farah');
    const grid = root.querySelector('tm-dashboard-grid')!;
    expect(grid.getAttribute('aria-label')).toBe('Your work');
    const tiles = [...grid.querySelectorAll<HTMLElement>('tm-dashboard-tile')];
    expect(
      tiles.map((tile) => tile.querySelector('.tm-card__heading')?.textContent?.trim()),
    ).toEqual(['Recently visited']);
    expect(tiles[0]!.querySelector('.tm-empty-state__heading')?.textContent?.trim()).toBe(
      'Pages you visit show up here',
    );
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
    await start();
    const harness = await tmNavigate('/17');
    await tmSettleUntil(() => greeting(harness) !== '', 'the greeting renders');

    expect(TestBed.inject(TmLocalization).timeZone()).toBe('Asia/Riyadh');
    const title = page(harness).querySelector('h1')!;
    expect(title.hasAttribute('tmPageTitle')).toBe(true);
    expect(title.querySelector('bdi')?.textContent).toBe('Farah');
    expect(page(harness).querySelector('.tm-home-greeting__date')?.textContent).toContain(
      'Thursday',
    );
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

  it("lists the recent pages as links with their launcher icons, else their family's, and preloads one on hover or focus", async () => {
    const recents = {
      pages: signal<readonly TmRecentPage[]>([
        recent('/17/centers', 'Centers', 'search', 'building'),
        recent('/17/reports/5', 'Margins', 'report', null),
      ]),
    };
    await start({ providers: [{ provide: TmRecents, useValue: recents }] });
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
  });

  it("renders a feature's home hatch in the platform page's place", async () => {
    const hatch = vi.fn<TmLazyComponent>(() => Promise.resolve(TmAcmeHome));
    await start({ features: [tmFeature({ key: 'shop', home: hatch })] });
    const harness = await tmNavigate('/17');
    await tmSettleUntil(() => page(harness).querySelector('tm-acme-home') !== null, 'the hatch renders');

    expect(page(harness).querySelector('tm-acme-home')).not.toBeNull();
    expect(page(harness).querySelector('tm-home-page')).toBeNull();
    expect(hatch).toHaveBeenCalledTimes(1);
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
});
