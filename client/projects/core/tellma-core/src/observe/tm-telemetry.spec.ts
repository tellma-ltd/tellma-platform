// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ErrorHandler, Injectable, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { RouterTestingHarness } from '@angular/router/testing';
import type { TmFakeApiCall } from '@tellma/core-testing';

import { TmRequestLog } from '../requests/tm-request-log';
import { tmAppRig, type TmAppRig, tmNavigate, tmSettle, tmSettleUntil, tmUnhandled } from '../routes/tm-app.testing.util';
import { TmTelemetry, TmTelemetryFront, type TmTelemetryItem } from './tm-telemetry';
import type { TmTelemetryBatch, TmTelemetryWireItem } from './tm-telemetry-pipeline';

describe('TmTelemetry', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  describe('outside an app', () => {
    it('is the front by default: it accepts every call, keeps none and never throws', () => {
      vi.spyOn(console, 'error').mockImplementation(() => undefined);
      const telemetry = TestBed.inject(TmTelemetry);
      expect(telemetry).toBeInstanceOf(TmTelemetryFront);
      expect(() => {
        telemetry.event('acme.report.run', { rows: 3 });
        telemetry.timing('acme.report.render', 12);
        telemetry.error(new Error('boom'), { where: 'test' });
        telemetry.flush();
      }).not.toThrow();
      expect(telemetry.recent()).toEqual([]);
    });

    it('logs each error to the console', () => {
      const logged = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      const failure = new TypeError('boom');
      TestBed.inject(TmTelemetry).error(failure);
      expect(logged).toHaveBeenCalledExactlyOnceWith(failure);
    });

    it('is one instance per root injector', () => {
      expect(TestBed.inject(TmTelemetry)).toBe(TestBed.inject(TmTelemetry));
    });

    it('is replaced by a provided implementation', () => {
      @Injectable()
      class Recording extends TmTelemetry {
        readonly #items = signal<readonly TmTelemetryItem[]>([]);
        override readonly recent = this.#items.asReadonly();
        override event(name: string): void {
          this.#items.update((items) => [...items, { kind: 'event', name, at: 0, tenantId: null, props: {} }]);
        }
        override timing(): void {}
        override error(): void {}
        override flush(): void {}
      }
      TestBed.configureTestingModule({ providers: [{ provide: TmTelemetry, useClass: Recording }] });

      TestBed.inject(TmTelemetry).event('acme.report.run');
      expect(TestBed.inject(TmTelemetry).recent().map((item) => item.name)).toEqual(['acme.report.run']);
    });
  });

  // The app's startup loads the pipeline at idle after the first render; these
  // specs wait for the state the load produces, never for a number of turns.
  describe('in an app', () => {
    let rig: TmAppRig;
    let front: TmTelemetryFront;

    /** The relay calls the fake API saw, oldest first. */
    const sends = (): readonly TmFakeApiCall[] => rig.api.calls().filter((call) => call.kind === 'send' && call.route === '/api/client-telemetry');

    /** Every item of every relay call, oldest first. */
    const sentItems = (): readonly TmTelemetryWireItem[] => sends().flatMap((call) => (call.body as TmTelemetryBatch).items);

    /** Every item name of every relay call, oldest first. */
    const sentNames = (): readonly string[] => sentItems().map((item) => item.name);

    /**
     * Hands an error to the app's error handler while an event is
     * dispatched, as the browser's global listeners do.
     *
     * @param event - The event.
     * @param error - The error.
     */
    function duringEvent(event: Event, error: unknown): void {
      const target = new EventTarget();
      target.addEventListener(event.type, () => TestBed.inject(ErrorHandler).handleError(error));
      target.dispatchEvent(event);
    }

    /**
     * Renders the app, which starts the pipeline, and waits for the pipeline
     * to take a call of the given name.
     *
     * @param name - The call's name; a marker call of that name is made after the render.
     * @returns The harness.
     */
    async function started(name = 'acme.spec.started'): Promise<RouterTestingHarness> {
      const harness = await tmNavigate('/17/centers/5');
      front.event(name);
      await tmSettleUntil(() => front.recent().some((item) => item.name === name), `the pipeline took ${name}`);
      return harness;
    }

    beforeEach(async () => {
      // Development builds write each item to the console; the specs read the ring instead.
      vi.spyOn(console, 'debug').mockImplementation(() => undefined);
      vi.spyOn(console, 'error').mockImplementation(() => undefined);
      rig = await tmAppRig({ providers: [{ provide: TmTelemetry, useFactory: () => new TmTelemetryFront() }] });
      front = TestBed.inject(TmTelemetry) as TmTelemetryFront;
    });

    it('takes the calls made before the pipeline loaded, in order, and every later one', async () => {
      front.event('acme.report.run', { rows: 3 });
      front.timing('acme.report.render', 12, { kind: 'summary' });
      expect(front.recent().some((item) => item.name === 'acme.report.run')).toBe(false);

      await started();
      const names = front.recent().map((item) => item.name);
      expect(names.indexOf('acme.report.run')).toBeLessThan(names.indexOf('acme.report.render'));
      expect(front.recent().find((item) => item.name === 'acme.report.render')?.props).toMatchObject({ kind: 'summary', ms: 12 });

      front.event('acme.report.closed');
      expect(front.recent().at(-1)?.name).toBe('acme.report.closed');
    });

    it('holds at most 1,000 calls before the pipeline loads', async () => {
      for (let index = 0; index < 1_100; index++) {
        front.event('acme.report.run', { index });
      }
      // The marker is past the cap, so the spec waits for the held calls instead.
      await started('acme.report.run');

      const indices = front.recent().flatMap((item) => (item.name === 'acme.report.run' ? [item.props['index'] as number] : []));
      expect(indices.length).toBeGreaterThan(0);
      expect(Math.max(...indices)).toBeLessThan(1_000);
    });

    it("sends an error held behind more than the queue's cap, and counts the overflow", async () => {
      for (let index = 0; index < 250; index++) {
        front.event('acme.report.run', { index });
      }
      front.error(new TypeError('held behind the events'));
      await started();

      await tmSettleUntil(
        () => sentNames().includes('tm.error.captured') && sentItems().some((item) => item.name === 'tm.telemetry.dropped' && item.properties['reason'] === 'overflow'),
        'the held error and the overflow count were sent',
      );
    });

    it('keeps the tenant a held call saw', async () => {
      front.event('acme.report.run');
      await started();

      expect(front.recent().find((item) => item.name === 'acme.report.run')?.tenantId).toBeNull();
      expect(front.recent().find((item) => item.name === 'acme.spec.started')?.tenantId).toBe(17);
    });

    it('records a held uncaught error as global, its message redacted', async () => {
      const failure = new TypeError('Invalid value "Farah Nasser" at row 42');
      duringEvent(new ErrorEvent('error', { error: failure }), failure);
      await started();

      expect(front.recent().find((item) => item.name === 'tm.error.captured')?.props).toMatchObject({
        source: 'global',
        errorClass: 'TypeError',
        message: 'Invalid value "…" at row #',
      });
    });

    it("counts a held global error from another origin's script as foreign", async () => {
      const foreign = new Error('injected');
      Object.defineProperty(foreign, 'stack', { value: 'Error: injected\n    at chrome-extension://abcdef/content.js:1:1' });
      duringEvent(new ErrorEvent('error', { error: foreign }), foreign);
      await started();
      front.flush();

      expect(front.recent().some((item) => item.name === 'tm.error.captured')).toBe(false);
      expect(sentItems().find((item) => item.name === 'tm.telemetry.dropped')?.properties).toMatchObject({ count: 1, reason: 'foreign' });
    });

    it('rethrows a malformed held call on its own in development, and replays the calls after it', async () => {
      // Held in a variable, since the lint rule rejects a malformed literal name.
      const malformed: string = 'Acme.Report';
      front.event(malformed);
      front.event('acme.report.after');
      const caught = await tmUnhandled(async () => {
        await started();
      });

      expect(caught).toEqual([expect.objectContaining({ name: 'TmTelemetryError' })]);
      expect(front.recent().some((item) => item.name === 'acme.report.after')).toBe(true);
    });

    it('sends through the API in the background, with the UI version and the tab', async () => {
      await started();
      front.event('acme.report.run');
      front.flush();
      // The launch's tenantless items go first, one tenant per batch, a second apart.
      await tmSettleUntil(() => sentNames().includes('acme.report.run'), 'the item was sent');

      const call = sends().find((sent) => (sent.body as TmTelemetryBatch).items.some((item) => item.name === 'acme.report.run'));
      expect(call?.options).toEqual({ background: true });
      expect(call?.body).toMatchObject({ version: expect.any(String), tabId: expect.any(String), tenantId: 17 });
    });

    it('flushes when the tab hides', async () => {
      await started();
      const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
      front.event('acme.report.run');
      const before = sends().length;
      document.dispatchEvent(new Event('visibilitychange'));
      visibility.mockRestore();

      expect(sends()).toHaveLength(before + 1);
      await tmSettleUntil(() => sentNames().includes('acme.report.run'), 'the item was sent');
    });

    it('flushes when the page is hidden', async () => {
      await started();
      front.event('acme.report.run');
      const before = sends().length;
      window.dispatchEvent(new PageTransitionEvent('pagehide'));

      expect(sends()).toHaveLength(before + 1);
      await tmSettleUntil(() => sentNames().includes('acme.report.run'), 'the item was sent');
    });

    it("measures each change of the request log, so a request the log's ring has since dropped still joins its route's summary", async () => {
      await started();
      const log = TestBed.inject(TmRequestLog);
      /** Begins and ends one request through the app's request log. */
      const request = (index: number, route: string): void =>
        log.begin({ traceId: index.toString(16).padStart(32, '0'), route, tenantId: 17, method: 'POST', background: false }).end({ outcome: 'ok', status: 200 });
      request(1, 'centers/query?top=25');
      // The log changed, so the startup's wiring measures it at the next render.
      await tmSettle();
      // A hundred later requests push the first out of the ring before the flush reads the log.
      for (let index = 2; index <= 101; index++) {
        request(index, 'agents/query');
      }
      front.flush();

      const summary = front.recent().find((item) => item.name === 'tm.api.requests' && item.props['api'] === 'centers/query');
      expect(summary).toMatchObject({ tenantId: 17, props: { api: 'centers/query', count: 1, ok: 1 } });
    });

    // An unload fires both events, in an order that depends on the browser and the unload.
    it.each([
      ['visibilitychange', 'pagehide'],
      ['pagehide', 'visibilitychange'],
    ])('sends one batch on an unload that fires %s, then %s', async (first, second) => {
      await started();
      front.event('acme.report.run');
      const before = sends().length;
      const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
      for (const type of [first, second]) {
        if (type === 'pagehide') {
          window.dispatchEvent(new PageTransitionEvent('pagehide'));
        } else {
          document.dispatchEvent(new Event('visibilitychange'));
        }
        front.event('acme.report.closed');
      }
      visibility.mockRestore();
      expect(sends()).toHaveLength(before + 1);

      // A page that stays sends the rest one batch a second.
      await tmSettleUntil(() => sentNames().includes('acme.report.closed'), 'the rest was sent');
    });

    it('holds the queue while offline, and sends it once back online', async () => {
      await started();
      front.flush();
      const before = sends().length;
      rig.api.setOnline(false);
      TestBed.tick();
      front.event('acme.report.run');
      front.flush();
      TestBed.tick();
      expect(sends()).toHaveLength(before);

      rig.api.setOnline(true);
      await tmSettleUntil(() => sentNames().includes('acme.report.run'), 'the held item was sent');
    });

    it("flushes on a tenant switch, sending the old tenant's items", async () => {
      const harness = await started();
      front.flush();
      front.event('acme.report.run');
      await harness.navigateByUrl('/18/centers/5');
      await tmSettleUntil(() => sentNames().includes('acme.report.run'), 'the tenant switch flushed');

      const batch = sends().find((call) => (call.body as TmTelemetryBatch).items.some((item) => item.name === 'acme.report.run'))?.body as TmTelemetryBatch;
      expect(batch.tenantId).toBe(17);
    });
  });
});
