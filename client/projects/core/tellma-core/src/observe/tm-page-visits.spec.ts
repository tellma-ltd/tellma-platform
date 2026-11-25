// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signal, type Type, type WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NavigationCancel, NavigationCancellationCode, NavigationEnd, NavigationStart, Router, type Event as RouterEvent } from '@angular/router';
import { TmFakeTelemetry } from '@tellma/core-testing';
import { Subject } from 'rxjs';

import type { TmPageContext } from '../context/tm-page-context';
import { TmPageContexts } from '../context/tm-page-contexts';
import { TmTestPage, tmAppRig, tmLoadTestPage, tmNavigate, tmSettleUntil, TM_TEST_SUB, tmTestMembership, type TmAppRig } from '../routes/tm-app.testing.util';
import { TmClientStore } from '../state/store/tm-client-store';
import { TmPageVisits } from './tm-page-visits';
import { TmTelemetry } from './tm-telemetry';

// Page visits (spec 0026 §5.1): one `tm.page.visit` per routed page, at the
// next navigation or at `pagehide`, with how it started, when its chrome
// painted and when its data arrived, or that it was left first. The page's
// data is held for a known time, so the stamps are checked against it. The
// startup's work after the first page runs once, past a redirect.

/** How long a held page waits for its data, in milliseconds. */
const DATA_MS = 150;

describe('TmPageVisits', () => {
  let rig: TmAppRig;
  let telemetry: TmFakeTelemetry;

  /** The visits reported so far, oldest first. */
  const visits = (): readonly Readonly<Record<string, unknown>>[] =>
    telemetry
      .recent()
      .filter((item) => item.name === 'tm.page.visit')
      .map((item) => item.props);

  /** Waits for a real time, in milliseconds. */
  const wait = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms));

  /** Waits until the routed page's context is in the given status and its chrome has painted. */
  async function shown(status: 'ready' | 'error' = 'ready', url?: string): Promise<void> {
    const contexts = TestBed.inject(TmPageContexts);
    await tmSettleUntil(() => (url === undefined || rig.router.url === url) && contexts.current()?.status() === status, `the page is ${status}`);
    // The chrome's stamp lands one animation frame after the render.
    await new Promise((resolve) => requestAnimationFrame(resolve));
  }

  /** Holds the next page's data for {@link DATA_MS}, then shows the test page. */
  function holdNextPage(): void {
    tmLoadTestPage.mockImplementationOnce(() => wait(DATA_MS).then((): Type<unknown> => TmTestPage));
  }

  /** Reports the running visit, as the page hidden for good does. */
  function hide(): void {
    window.dispatchEvent(new Event('pagehide'));
  }

  beforeEach(async () => {
    rig = await tmAppRig({ tenants: [tmTestMembership(17)] });
    ({ telemetry } = rig);
  });

  afterEach(() => {
    // A held page the spec did not reach must not hold the next spec's.
    tmLoadTestPage.mockReset();
    tmLoadTestPage.mockImplementation(() => Promise.resolve(TmTestPage));
    TestBed.resetTestingModule();
  });

  it("starts the document's first visit at the time origin, and reports it at the next navigation", async () => {
    const before = performance.now();
    const harness = await tmNavigate('/17/centers');
    await shown();
    expect(visits()).toEqual([]);

    await harness.navigateByUrl('/17/centers/5');
    const [first] = visits();
    expect(first).toEqual({ entry: 'load', outcome: 'ready', ms: expect.any(Number), chromeMs: expect.any(Number), visitS: expect.any(Number) });
    // From the time origin, not from the first navigation: the spec's own setup came first.
    // The stamps against a held page's data are the next spec's.
    expect(first['ms']).toBeGreaterThanOrEqual(before);
    expect(first['chromeMs']).toBeGreaterThanOrEqual(before);
  });

  it("stamps a navigation's chrome before its data, its data when it arrives, and its length at pagehide", async () => {
    const harness = await tmNavigate('/17/centers');
    await shown();
    holdNextPage();
    await harness.navigateByUrl('/17/credit-review');
    await shown();
    await wait(200);
    hide();
    hide();

    expect(visits()).toHaveLength(2);
    const second = visits()[1];
    expect(second).toMatchObject({ entry: 'navigation', outcome: 'ready' });
    const ms = second['ms'] as number;
    const chromeMs = second['chromeMs'] as number;
    const visitS = second['visitS'] as number;
    // The chrome paints while the data is held; the data lands once it is released.
    expect(chromeMs).toBeLessThan(DATA_MS);
    expect(ms).toBeGreaterThanOrEqual(DATA_MS);
    expect(ms).toBeLessThan(DATA_MS + 1_000);
    // The visit ran on 200 ms past its data, rounded to tenths of a second.
    expect(visitS * 1000).toBeGreaterThanOrEqual(ms + 200 - 50);
    const tenths = visitS * 10;
    expect(Math.abs(tenths - Math.round(tenths))).toBeLessThan(1e-9);
  });

  it('reports a page that shows its error state with the error outcome', async () => {
    const harness = await tmNavigate('/17/centers');
    await shown();
    tmLoadTestPage.mockImplementationOnce(() => Promise.reject(new TypeError('Failed to fetch dynamically imported module')));
    await harness.navigateByUrl('/17/credit-review');
    await shown('error');
    hide();
    expect(visits()[1]).toMatchObject({ entry: 'navigation', outcome: 'error', ms: expect.any(Number) });
  });

  it('reports a page left before its data arrived as left, with how long it was waited for', async () => {
    const harness = await tmNavigate('/17/centers');
    await shown();
    const contexts = TestBed.inject(TmPageContexts);
    const search = contexts.current();
    tmLoadTestPage.mockImplementationOnce(() => new Promise<Type<unknown>>(() => undefined));
    await harness.navigateByUrl('/17/credit-review');
    await tmSettleUntil(() => contexts.current() !== search, 'the page registered its context');
    await wait(20);
    expect(contexts.current()?.status()).toBe('loading');

    await harness.navigateByUrl('/17/centers');
    const left = visits()[1];
    expect(left).toMatchObject({ entry: 'navigation', outcome: 'left' });
    expect(left['ms']).toBeGreaterThanOrEqual(20);
  });

  it("passes the launch redirect's start on: the page it leads to is the document's first visit", async () => {
    const before = performance.now();
    TestBed.inject(TmClientStore).boot.patch({ lastTenantId: 17, sub: TM_TEST_SUB });
    await tmNavigate('/');
    await shown('ready', '/17');
    hide();

    expect(visits()).toHaveLength(1);
    expect(visits()[0]).toMatchObject({ entry: 'load', outcome: 'ready' });
    // Timed from the time origin, before the spec began, not from the redirect's navigation.
    expect(visits()[0]['ms']).toBeGreaterThanOrEqual(before);
  });

  it('times a navigation that keeps its page, as a reused route does, against the page it kept', async () => {
    const harness = await tmNavigate('/17/centers/5');
    await shown();
    const contexts = TestBed.inject(TmPageContexts);
    const kept = contexts.current();
    await harness.navigateByUrl('/17/centers/6');
    await shown();
    expect(contexts.current()).toBe(kept);
    hide();

    expect(visits()).toHaveLength(2);
    expect(visits()[1]).toMatchObject({ entry: 'navigation', outcome: 'ready', ms: expect.any(Number), chromeMs: expect.any(Number) });
  });

  it('reports a navigation abandoned in a guard as left, and starts the next visit at its own navigation', async () => {
    const harness = await tmNavigate('/17/centers');
    await shown();
    rig.router.resetConfig([{ path: 'stuck', canActivate: [() => new Promise<boolean>(() => undefined)], children: [] }, ...rig.router.config]);
    void rig.router.navigateByUrl('/stuck');
    await wait(200);
    await harness.navigateByUrl('/17/credit-review');
    await shown();
    hide();

    expect(visits().map((visit) => [visit['entry'], visit['outcome']])).toEqual([
      ['load', 'ready'],
      ['navigation', 'left'],
      ['navigation', 'ready'],
    ]);
    expect(visits()[1]['ms']).toBeGreaterThanOrEqual(200);
    expect(visits()[2]['ms']).toBeLessThan(200);
  });

  // A navigation's settling lands one render after it ends. Here the router is scripted, so that
  // render is placed exactly: after the next navigation has started, as when a page navigates on
  // within the render that shows it.
  describe('with a scripted router', () => {
    /**
     * Replaces the app with the visits alone, over a router whose events the test sends.
     *
     * @returns The router's events and the routed page context.
     */
    function scripted(): { events: Subject<RouterEvent>; current: WritableSignal<TmPageContext | null> } {
      TestBed.resetTestingModule();
      const events = new Subject<RouterEvent>();
      const current = signal<TmPageContext | null>(null);
      TestBed.configureTestingModule({
        providers: [
          { provide: Router, useValue: { events } },
          { provide: TmPageContexts, useValue: { current } },
          TmFakeTelemetry,
          { provide: TmTelemetry, useExisting: TmFakeTelemetry },
        ],
      });
      return { events, current };
    }

    it("never lets an earlier navigation's settling adopt the page the next one keeps", () => {
      const { events, current } = scripted();
      TestBed.inject(TmPageVisits).ɵstart();
      const page: TmPageContext = {
        kind: 'details',
        routeTemplate: 't/centers/:id',
        title: signal('').asReadonly(),
        status: signal<'loading' | 'ready' | 'error'>('ready').asReadonly(),
        state: signal({}).asReadonly(),
        errors: signal([]).asReadonly(),
        actions: signal([]).asReadonly(),
      };

      // The first navigation shows the page.
      events.next(new NavigationStart(1, '/17/centers/5'));
      current.set(page);
      TestBed.tick();
      events.next(new NavigationEnd(1, '/17/centers/5', '/17/centers/5'));
      // The next navigation starts before the first one's render, then waits while the page stays shown.
      events.next(new NavigationStart(2, '/17/credit-review'));
      TestBed.tick();
      TestBed.tick();
      events.next(new NavigationStart(3, '/17/centers'));

      const outcomes = TestBed.inject(TmFakeTelemetry)
        .recent()
        .filter((item) => item.name === 'tm.page.visit')
        .map((item) => item.props['outcome']);
      expect(outcomes).toEqual(['ready', 'left']);
    });

    it("runs the first page's work once, in the render after the first navigation that ends for good", () => {
      const { events } = scripted();
      const firstPage = vi.fn();
      TestBed.inject(TmPageVisits).ɵstart(firstPage);

      // The app renders before the router has placed a page.
      TestBed.tick();
      expect(firstPage).not.toHaveBeenCalled();
      // A redirect hands the first navigation on before its render.
      events.next(new NavigationStart(1, '/'));
      events.next(new NavigationCancel(1, '/', 'redirected', NavigationCancellationCode.Redirect));
      events.next(new NavigationStart(2, '/17'));
      TestBed.tick();
      expect(firstPage).not.toHaveBeenCalled();
      // The navigation it hands on to ends, and its page renders.
      events.next(new NavigationEnd(2, '/17', '/17'));
      expect(firstPage).not.toHaveBeenCalled();
      TestBed.tick();
      expect(firstPage).toHaveBeenCalledTimes(1);
      // Later navigations run it no more.
      events.next(new NavigationStart(3, '/17/centers'));
      events.next(new NavigationEnd(3, '/17/centers', '/17/centers'));
      TestBed.tick();
      expect(firstPage).toHaveBeenCalledTimes(1);
    });
  });
});
