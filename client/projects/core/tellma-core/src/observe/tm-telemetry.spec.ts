// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injectable, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { RouterTestingHarness } from '@angular/router/testing';
import type { TmFakeApiCall } from '@tellma/core-testing';

import { tmAppRig, type TmAppRig, tmNavigate, tmSettleUntil } from '../routes/tm-app.testing.util';
import { TmTelemetry, TmTelemetryFront, type TmTelemetryItem } from './tm-telemetry';
import type { TmTelemetryBatch } from './tm-telemetry-pipeline';

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

    /** Every item name of every relay call, oldest first. */
    const sentNames = (): readonly string[] => sends().flatMap((call) => (call.body as TmTelemetryBatch).items.map((item) => item.name));

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

    it('holds at most 200 calls before the pipeline loads', async () => {
      for (let index = 0; index < 250; index++) {
        front.event('acme.report.run', { index });
      }
      // The marker is past the cap, so the spec waits for the held calls instead.
      await started('acme.report.run');

      const indices = front.recent().flatMap((item) => (item.name === 'acme.report.run' ? [item.props['index'] as number] : []));
      expect(indices.length).toBeGreaterThan(0);
      expect(Math.max(...indices)).toBeLessThan(200);
    });

    it('sends through the API in the background, with the UI version and the tab', async () => {
      await started();
      front.event('acme.report.run');
      front.flush();

      const call = sends().at(-1);
      expect(call?.options).toEqual({ background: true });
      expect(call?.body).toMatchObject({ version: expect.any(String), tabId: expect.any(String), tenantId: 17 });
      expect(sentNames()).toContain('acme.report.run');
    });

    it('flushes when the tab hides', async () => {
      await started();
      const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
      front.event('acme.report.run');
      document.dispatchEvent(new Event('visibilitychange'));
      visibility.mockRestore();

      expect(sentNames()).toContain('acme.report.run');
    });

    it('flushes when the page is hidden', async () => {
      await started();
      front.event('acme.report.run');
      window.dispatchEvent(new PageTransitionEvent('pagehide'));

      expect(sentNames()).toContain('acme.report.run');
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
