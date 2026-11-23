// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TmFakeTelemetry } from '@tellma/core-testing';

import { TM_API_HOST } from '../api/tm-api-host';
import { TmFakeApiHost } from '../api/tm-api.testing.util';
import { TM_DEV_MODE } from '../app/tm-dev-mode';
import { TmTelemetry } from '../observe/tm-telemetry';
import { TmBanners, type TmBannerOptions } from './tm-banners';

// The banner service on a fake clock and a faked window: the precedence and
// the pager, dismissal per tab and per version, and the input-idle gate that
// the row's opening and closing wait for, while the head (what the host
// announces) follows every change at once.

/** The service, its faked window and its telemetry. */
interface TmBannersRig {
  readonly banners: TmBanners;
  readonly host: TmFakeApiHost;
  readonly telemetry: TmFakeTelemetry;
  /** Types a key, which ends the idle moment for 2 s. */
  type(): void;
  /** Lets time pass. */
  wait(ms: number): Promise<void>;
}

/** Creates the service in an idle tab, with the clock faked first. */
async function setUp(options: { readonly dev?: boolean } = {}): Promise<TmBannersRig> {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date', 'performance'] });
  const host = new TmFakeApiHost();
  TestBed.configureTestingModule({
    providers: [
      { provide: TM_API_HOST, useValue: host },
      { provide: TM_DEV_MODE, useValue: () => options.dev ?? true },
      TmFakeTelemetry,
      { provide: TmTelemetry, useExisting: TmFakeTelemetry },
    ],
  });
  const banners = TestBed.inject(TmBanners);
  const wait = async (ms: number): Promise<void> => {
    await vi.advanceTimersByTimeAsync(ms);
  };
  // The clock starts busy; 2 s later the tab is idle.
  await wait(2000);
  return { banners, host, telemetry: TestBed.inject(TmFakeTelemetry), type: () => host.fire('window', 'keydown'), wait };
}

/** A banner's options, with its message as its text. */
function banner(id: string, level: TmBannerOptions['level'], members: Partial<TmBannerOptions> = {}): TmBannerOptions {
  return { id, level, tone: 'info', message: signal(`${id} message`), dismissible: level > 2, ...members };
}

/** The id of a view's banner, or `null`. */
function idOf(view: { readonly options: TmBannerOptions } | null): string | null {
  return view?.options.id ?? null;
}

describe('TmBanners', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  describe('precedence and the pager', () => {
    it('shows the highest level first, then the order show first ran in', async () => {
      const { banners } = await setUp();
      banners.show(banner('acme.info', 5));
      expect(idOf(banners.current())).toBe('acme.info');

      banners.show(banner('acme.warning', 4));
      expect(idOf(banners.current())).toBe('acme.warning');

      // A banner of the same level only adds to the count.
      banners.show(banner('acme.second-warning', 4));
      expect(banners.current()).toMatchObject({ options: { id: 'acme.warning' }, index: 1, count: 3 });

      banners.show(banner('tm.offline', 1));
      expect(banners.current()).toMatchObject({ options: { id: 'tm.offline' }, index: 1, count: 4 });
    });

    it('pages forward and back, wrapping around', async () => {
      const { banners } = await setUp();
      banners.show(banner('acme.a', 4));
      banners.show(banner('acme.b', 4));
      banners.show(banner('acme.c', 5));

      banners.ɵpage(1);
      expect(banners.current()).toMatchObject({ options: { id: 'acme.b' }, index: 2, count: 3 });
      banners.ɵpage(1);
      banners.ɵpage(1);
      expect(banners.current()).toMatchObject({ options: { id: 'acme.a' }, index: 1 });
      banners.ɵpage(-1);
      expect(banners.current()).toMatchObject({ options: { id: 'acme.c' }, index: 3 });
    });

    it("keeps the user's page until that banner closes, or until a higher level takes the row", async () => {
      const { banners } = await setUp();
      banners.show(banner('acme.a', 4));
      const b = banners.show(banner('acme.b', 4));
      banners.ɵpage(1);

      // An equal or lower level leaves the user's choice alone.
      banners.show(banner('acme.c', 4));
      banners.show(banner('acme.d', 5));
      expect(idOf(banners.current())).toBe('acme.b');

      b.close();
      expect(idOf(banners.current())).toBe('acme.a');

      banners.ɵpage(1);
      expect(idOf(banners.current())).toBe('acme.c');
      banners.show(banner('tm.update', 3, { dismissible: true }));
      expect(idOf(banners.current())).toBe('tm.update');
    });

    it('moves the pager to a new banner that outranks the page, not back to the head', async () => {
      const { banners } = await setUp();
      banners.show(banner('tm.offline', 1));
      banners.show(banner('acme.tip', 5));
      banners.ɵpage(1);
      expect(idOf(banners.current())).toBe('acme.tip');

      banners.show(banner('acme.warning', 4));
      expect(banners.current()).toMatchObject({ options: { id: 'acme.warning' }, index: 2, count: 3 });
    });

    it("keeps the user's page when a live banner of a higher level is shown again", async () => {
      const { banners } = await setUp();
      banners.show(banner('acme.warning', 4));
      banners.show(banner('acme.tip', 5));
      banners.ɵpage(1);

      banners.show(banner('acme.warning', 4, { message: signal('A newer message') }));
      expect(banners.current()).toMatchObject({ options: { id: 'acme.tip' }, index: 2, count: 2 });
    });

    it("keeps the user's page when a dismissed banner of a higher level is shown again", async () => {
      const { banners } = await setUp();
      const warning = banners.show(banner('acme.warning', 4));
      banners.show(banner('acme.tip', 5));
      banners.show(banner('acme.other-tip', 5));
      banners.ɵdismiss('acme.warning');
      banners.ɵpage(1);
      expect(idOf(banners.current())).toBe('acme.other-tip');

      // Closed and shown anew, it is still dismissed, so it stays hidden.
      warning.close();
      banners.show(banner('acme.warning', 4));
      expect(banners.current()).toMatchObject({ options: { id: 'acme.other-tip' }, index: 2, count: 2 });
    });

    it('does not page a single banner', async () => {
      const { banners } = await setUp();
      banners.show(banner('acme.a', 4));
      banners.ɵpage(1);
      expect(banners.current()).toMatchObject({ options: { id: 'acme.a' }, index: 1, count: 1 });
    });
  });

  describe('show, update and close', () => {
    it('updates a live banner in place when its id is shown again', async () => {
      const { banners } = await setUp();
      const ref = banners.show(banner('acme.a', 4));
      const message = signal('Again');
      expect(banners.show(banner('acme.a', 4, { message, tone: 'warning' }))).toBe(ref);
      expect(banners.current()).toMatchObject({ options: { tone: 'warning' }, count: 1 });
      expect(banners.current()?.options.message()).toBe('Again');
    });

    it('updates a banner through its handle, but never its id or level', async () => {
      const { banners } = await setUp();
      const ref = banners.show(banner('acme.a', 4));
      ref.update({ tone: 'error', icon: 'wifi-off', id: 'acme.b', level: 1 } as Partial<TmBannerOptions>);
      expect(banners.current()?.options).toMatchObject({ id: 'acme.a', level: 4, tone: 'error', icon: 'wifi-off' });
    });

    it('closes a banner, and ignores the handle after that', async () => {
      const { banners } = await setUp();
      const a = banners.show(banner('acme.a', 4));
      banners.show(banner('acme.b', 4));
      a.close();
      expect(banners.current()).toMatchObject({ options: { id: 'acme.b' }, count: 1 });
      a.update({ tone: 'error' });
      a.close();
      expect(banners.current()).toMatchObject({ options: { id: 'acme.b', tone: 'info' }, count: 1 });
    });
  });

  describe('dismissal', () => {
    it('hides a dismissed banner for the rest of the tab, even when it is shown again', async () => {
      const { banners } = await setUp();
      banners.show(banner('acme.a', 4));
      const b = banners.show(banner('acme.b', 4));
      banners.ɵdismiss('acme.a');
      expect(banners.current()).toMatchObject({ options: { id: 'acme.b' }, index: 1, count: 1 });

      banners.show(banner('acme.a', 4));
      expect(banners.current()).toMatchObject({ options: { id: 'acme.b' }, count: 1 });
      b.close();
      expect(banners.ɵhead()).toBeNull();
    });

    it('never dismisses a banner that is not dismissible', async () => {
      const { banners } = await setUp();
      banners.show(banner('tm.offline', 1));
      banners.ɵdismiss('tm.offline');
      expect(idOf(banners.current())).toBe('tm.offline');
    });

    it('hides a dismissed update banner for its version only', async () => {
      const { banners } = await setUp();
      const update = banner('tm.update', 3, { dismissible: true });
      banners.ɵshow(update, '2.4.0');
      banners.ɵdismiss('tm.update');
      expect(banners.ɵhead()).toBeNull();

      banners.ɵshow(update, '2.4.0');
      expect(banners.ɵhead()).toBeNull();

      banners.ɵshow(update, '2.5.0');
      expect(idOf(banners.ɵhead())).toBe('tm.update');
    });

    it('lets the next banner take the row when the paged one is dismissed', async () => {
      const { banners } = await setUp();
      banners.show(banner('acme.a', 4));
      banners.show(banner('acme.b', 4));
      banners.show(banner('acme.c', 4));
      banners.ɵpage(1);
      banners.ɵdismiss('acme.b');
      expect(banners.current()).toMatchObject({ options: { id: 'acme.a' }, count: 2 });
    });
  });

  describe('the input-idle gate', () => {
    it('opens the row only after 2 s without input, while the head changes at once', async () => {
      const { banners, type, wait } = await setUp();
      type();
      banners.show(banner('tm.offline', 1));
      expect(idOf(banners.ɵhead())).toBe('tm.offline');
      expect(banners.visible()).toBe(false);
      expect(banners.current()).toBeNull();

      await wait(1500);
      type();
      await wait(1999);
      expect(banners.visible()).toBe(false);
      await wait(1);
      expect(banners.visible()).toBe(true);
      expect(idOf(banners.current())).toBe('tm.offline');
    });

    it('closes the row only after 2 s without input, keeping it open and empty meanwhile', async () => {
      const { banners, type, wait } = await setUp();
      const ref = banners.show(banner('acme.a', 4));
      expect(banners.visible()).toBe(true);

      type();
      ref.close();
      expect(banners.ɵhead()).toBeNull();
      expect(banners.visible()).toBe(true);
      expect(banners.current()).toBeNull();

      await wait(2000);
      expect(banners.visible()).toBe(false);
    });

    it('swaps the message of an open row at once', async () => {
      const { banners, type } = await setUp();
      const a = banners.show(banner('acme.a', 4));
      banners.show(banner('acme.b', 4));
      type();
      a.close();
      expect(idOf(banners.current())).toBe('acme.b');
      banners.show(banner('tm.offline', 1));
      expect(idOf(banners.current())).toBe('tm.offline');
    });

    it('fills an emptied row at once when a banner arrives before it closes', async () => {
      const { banners, type, wait } = await setUp();
      const a = banners.show(banner('acme.a', 4));
      type();
      a.close();
      banners.show(banner('acme.b', 4));
      expect(banners.visible()).toBe(true);
      expect(idOf(banners.current())).toBe('acme.b');
      // The close it undid never reaches the row at the idle moment.
      await wait(2000);
      expect(banners.visible()).toBe(true);
      expect(idOf(banners.current())).toBe('acme.b');
    });

    it('never opens the row for a banner that closed before the gate', async () => {
      const { banners, type, wait } = await setUp();
      type();
      const ref = banners.show(banner('acme.a', 4));
      expect(idOf(banners.ɵhead())).toBe('acme.a');
      ref.close();
      expect(banners.ɵhead()).toBeNull();

      await wait(2000);
      expect(banners.visible()).toBe(false);
      expect(banners.current()).toBeNull();
      await wait(10_000);
      expect(banners.visible()).toBe(false);
    });

    it("closes the row at once on the user's own dismiss", async () => {
      const { banners, type } = await setUp();
      banners.show(banner('acme.a', 4));
      type();
      banners.ɵdismiss('acme.a');
      expect(banners.visible()).toBe(false);
    });

    it('applies a change at once while the page is hidden', async () => {
      const { banners, host, type } = await setUp();
      type();
      host.visibleNow = false;
      const ref = banners.show(banner('acme.a', 4));
      expect(banners.visible()).toBe(true);
      ref.close();
      expect(banners.visible()).toBe(false);
    });

    it('applies a waiting change the moment the page turns hidden', async () => {
      const { banners, host, type } = await setUp();
      type();
      banners.show(banner('acme.a', 4));
      expect(banners.visible()).toBe(false);
      host.visibleNow = false;
      host.fire('document', 'visibilitychange');
      expect(banners.visible()).toBe(true);
    });
  });

  describe('checks', () => {
    it("throws in development for a distribution's banner at a platform level", async () => {
      const { banners } = await setUp();
      for (const level of [1, 2, 3] as const) {
        expect(() => banners.show(banner('acme.alert', level, { dismissible: false }))).toThrow(/levels 1-3 are the platform's/);
      }
      expect(() => banners.show(banner('tm.offline', 1, { dismissible: true }))).toThrow(/levels 1 and 2 never are/);
    });

    it('shows them in production, never dismissible at levels 1 and 2', async () => {
      const { banners } = await setUp({ dev: false });
      banners.show(banner('acme.alert', 1, { dismissible: true }));
      expect(banners.current()?.options).toMatchObject({ id: 'acme.alert', dismissible: false });
    });
  });

  it('reports the time from queued to shown once per banner', async () => {
    const { banners, telemetry, type, wait } = await setUp();
    type();
    banners.show(banner('tm.offline', 1));
    await wait(2000);
    banners.show(banner('acme.a', 4));
    banners.ɵpage(1);
    banners.ɵpage(1);
    expect(telemetry.recent().filter((item) => item.name === 'tm.attention.banner')).toEqual([
      expect.objectContaining({ kind: 'timing', props: { id: 'tm.offline', level: 1, ms: 2000 } }),
      expect.objectContaining({ kind: 'timing', props: { id: 'acme.a', level: 4, ms: 0 } }),
    ]);
  });
});
