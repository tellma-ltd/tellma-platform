// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signal } from '@angular/core';

import { TmApiError } from '../api/tm-api-error';
import type { TmTelemetryCall, TmTelemetryItem, TmTelemetryProps } from './tm-telemetry';
import {
  type TmTelemetryBatch,
  TmTelemetryError,
  type TmTelemetryHost,
  type TmTelemetryPage,
  TmTelemetryPipeline,
  type TmTelemetrySettings,
  type TmTelemetryWireItem,
} from './tm-telemetry-pipeline';

/** When every spec starts. */
const T0 = Date.parse('2026-11-23T10:00:00.000Z');

/** A trace id. */
const TRACE_ID = '4bf92f3577b34da6a3ce929d0e0e4736';

/** The routed page in these specs. */
const PAGE: TmTelemetryPage = { route: '/:t/centers/:id', pageKind: 'details' };

/** What a spec sets of the host before and while it drives the pipeline. */
interface TmHostState {
  dev: boolean;
  draw: number;
  settings: TmTelemetrySettings | undefined;
  version: string | null;
  tenantId: number | null;
  page: TmTelemetryPage | null;
  held: boolean;
  traceIds: readonly string[];
  failSend: boolean;
}

/** The pipeline over a fake host, with what it sent and wrote to the console. */
interface TmPipelineRig {
  readonly state: TmHostState;
  readonly pipeline: TmTelemetryPipeline;
  readonly recent: () => readonly TmTelemetryItem[];
  readonly sent: TmTelemetryBatch[];
  readonly debugged: (readonly [string, TmTelemetryProps])[];
  readonly event: (...args: unknown[]) => void;
  readonly timing: (...args: unknown[]) => void;
  readonly error: (...args: unknown[]) => void;
}

/**
 * Creates the pipeline over a fake host.
 *
 * @param patch - The host's state, where not the defaults: production, telemetry known enabled at rate 1, tenant 17 on a details page.
 * @returns The rig.
 */
function rig(patch: Partial<TmHostState> = {}): TmPipelineRig {
  const state: TmHostState = {
    dev: false,
    draw: 0.5,
    settings: { enabled: true, sampleRate: 1 },
    version: '1.4.0',
    tenantId: 17,
    page: PAGE,
    held: false,
    traceIds: [],
    failSend: false,
    ...patch,
  };
  const recent = signal<readonly TmTelemetryItem[]>([]);
  const sent: TmTelemetryBatch[] = [];
  const debugged: (readonly [string, TmTelemetryProps])[] = [];
  const host: TmTelemetryHost = {
    recent,
    tabId: 'tab-1',
    origin: 'https://acme.example',
    devMode: () => state.dev,
    random: () => state.draw,
    now: () => Date.now(),
    settings: () => state.settings,
    version: () => state.version,
    tenantId: () => state.tenantId,
    page: () => state.page,
    held: () => state.held,
    recentTraceIds: () => state.traceIds,
    send: (batch) => {
      if (state.failSend) {
        throw new Error('send failed');
      }
      sent.push(batch);
    },
    debug: (name, props) => debugged.push([name, props]),
  };
  const pipeline = new TmTelemetryPipeline(host);
  const take = (method: TmTelemetryCall[0], args: unknown[]): void => pipeline.take([method, args, Date.now()]);
  return {
    state,
    pipeline,
    recent: recent.asReadonly(),
    sent,
    debugged,
    event: (...args) => take('event', args),
    timing: (...args) => take('timing', args),
    error: (...args) => take('error', args),
  };
}

/**
 * Every item sent, oldest batch first.
 *
 * @param sent - The batches.
 * @returns The items.
 */
function itemsOf(sent: readonly TmTelemetryBatch[]): readonly TmTelemetryWireItem[] {
  return sent.flatMap((batch) => batch.items);
}

/**
 * Every item name sent, oldest batch first.
 *
 * @param sent - The batches.
 * @returns The names.
 */
function namesOf(sent: readonly TmTelemetryBatch[]): readonly string[] {
  return itemsOf(sent).map((item) => item.name);
}

/**
 * An error with a stack of the app's origin at a position of the spec's choosing.
 *
 * @param column - The position's column, which tells fingerprints apart.
 * @param message - The error's message.
 * @returns The error.
 */
function appError(column: number, message = 'boom'): Error {
  const error = new TypeError(message);
  Object.defineProperty(error, 'stack', { value: `TypeError: ${message}\n    at f (https://acme.example/main-ABC.js:1:${column})` });
  return error;
}

/**
 * The time as the wire writes it.
 *
 * @param at - Epoch milliseconds.
 * @returns ISO 8601.
 */
function iso(at: number): string {
  return new Date(at).toISOString();
}

describe('TmTelemetryPipeline', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(T0);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  describe('envelope', () => {
    it('sends each item with the tab, the version, the tenant, the send time and the reserved properties', () => {
      const { sent, event } = rig();
      event('tm.session.expired', { source: '401' });
      vi.advanceTimersByTime(30_000);

      expect(sent).toEqual([
        {
          version: '1.4.0',
          tabId: 'tab-1',
          tenantId: 17,
          sentAt: iso(T0 + 30_000),
          items: [{ kind: 'event', name: 'tm.session.expired', at: iso(T0), properties: { route: '/:t/centers/:id', pageKind: 'details', source: '401' } }],
        },
      ]);
    });

    it("carries a timing's duration as ms, a sampled item's rate and an item's trace id", () => {
      const { sent, timing, pipeline } = rig();
      timing('acme.report.render', 12.5, { kind: 'summary' }, { traceId: TRACE_ID });
      pipeline.flush();

      expect(itemsOf(sent)).toEqual([
        {
          kind: 'timing',
          name: 'acme.report.render',
          at: iso(T0),
          traceId: TRACE_ID,
          properties: { route: '/:t/centers/:id', pageKind: 'details', ms: 12.5, sampleRate: 1, kind: 'summary' },
        },
      ]);
    });

    it('leaves out the page properties before the first page, and names no tenant outside one', () => {
      const { sent, event, pipeline } = rig({ page: null, tenantId: null });
      event('tm.launch.cold');
      pipeline.flush();

      expect(sent).toEqual([expect.objectContaining({ tenantId: null, items: [expect.objectContaining({ properties: {} })] })]);
    });

    it("sends an error's class, fingerprint, redacted message, reduced stack and recent trace ids", () => {
      const { sent, error, state } = rig({ traceIds: [TRACE_ID, 'a'.repeat(32)] });
      error(appError(7, 'Invalid value "Farah Nasser" at row 42'), { where: 'tm-leader' });
      state.traceIds = [];
      vi.advanceTimersByTime(1_000);

      expect(itemsOf(sent)).toEqual([
        {
          kind: 'error',
          name: 'tm.error.captured',
          at: iso(T0),
          properties: {
            route: '/:t/centers/:id',
            pageKind: 'details',
            source: 'render',
            errorClass: 'TypeError',
            fingerprint: 'TypeError@main-ABC.js:1:7',
            message: 'Invalid value "…" at row #',
            stack: 'at f (main-ABC.js:1:7)',
            recentTraceIds: `${TRACE_ID},${'a'.repeat(32)}`,
            where: 'tm-leader',
          },
        },
      ]);
    });

    it("sends a request error's kind, code, status and trace id", () => {
      const { sent, error } = rig();
      error(new TmApiError({ kind: 'server', status: 500, code: 'internal', traceId: TRACE_ID, message: 'Name "Farah"' }), { source: 'api' });
      vi.advanceTimersByTime(1_000);

      const [item] = itemsOf(sent);
      expect(item).toMatchObject({ traceId: TRACE_ID, properties: { source: 'api', errorClass: 'TmApiError', kind: 'server', code: 'internal', status: 500 } });
      expect(item?.properties).not.toHaveProperty('message');
    });
  });

  describe('error sources', () => {
    it('reads an error reported while the browser dispatches an error or a rejection as global', () => {
      const { recent, error } = rig();
      error(appError(1), undefined, new ErrorEvent('error'));
      error(appError(2), undefined, new PromiseRejectionEvent('unhandledrejection', { promise: Promise.resolve() }));
      error(appError(3), undefined, new Event('click'));
      error(appError(4), { source: 'api' }, new ErrorEvent('error'));

      expect(recent().map((item) => item.props['source'])).toEqual(['global', 'global', 'render', 'api']);
    });

    it('filters global noise and counts foreign errors', () => {
      const { recent, sent, error, pipeline } = rig();
      const foreign = new Error('x');
      Object.defineProperty(foreign, 'stack', { value: '    at chrome-extension://abcdef/content.js:1:1' });
      error(new TmApiError({ kind: 'cancelled' }), undefined, new PromiseRejectionEvent('unhandledrejection', { promise: Promise.resolve() }));
      error(new Error('ResizeObserver loop limit exceeded'), undefined, new ErrorEvent('error'));
      error(foreign, undefined, new ErrorEvent('error'));
      pipeline.flush();

      expect(recent()).toEqual([]);
      expect(itemsOf(sent)).toEqual([expect.objectContaining({ name: 'tm.telemetry.dropped', properties: expect.objectContaining({ count: 1, reason: 'foreign' }) })]);
    });

    it('throttles a fingerprint sent in the last 10 minutes, keeping it in the ring', () => {
      const { recent, sent, error } = rig();
      error(appError(1));
      error(appError(1, 'again'));
      vi.advanceTimersByTime(1_000);

      expect(recent()).toHaveLength(2);
      expect(itemsOf(sent).map((item) => [item.name, item.properties['reason']])).toEqual([
        ['tm.telemetry.dropped', 'throttled'],
        ['tm.error.captured', undefined],
      ]);
    });

    it('caps error items at 30 per 10 minutes', () => {
      const { sent, error } = rig();
      for (let column = 1; column <= 32; column++) {
        error(appError(column));
      }
      vi.advanceTimersByTime(10_000);

      expect(namesOf(sent).filter((name) => name === 'tm.error.captured')).toHaveLength(30);
      expect(itemsOf(sent).find((item) => item.name === 'tm.telemetry.dropped')?.properties).toMatchObject({ count: 2, reason: 'throttled' });
    });
  });

  describe('guard', () => {
    const BAD_NAMES: readonly unknown[] = [
      'acme.report',
      'Acme.report.run',
      'acme.report.run.',
      'acme.re_port.run',
      'acme.report.a.b.c.d.e',
      `acme.report.${'x'.repeat(60)}`,
      42,
    ];

    // Each case calls the pipeline with one malformed argument.
    const BAD_CALLS: readonly (readonly [string, (r: TmPipelineRig) => void])[] = [
      ['a key that is not camelCase', (r) => r.event('acme.report.run', { 'bad-key': 1 })],
      ['a key past 32 characters', (r) => r.event('acme.report.run', { [`k${'x'.repeat(32)}`]: 1 })],
      ['a value with a space', (r) => r.event('acme.report.run', { kind: 'two words' })],
      ['a value with a query', (r) => r.event('acme.report.run', { kind: '/centers?q=jed' })],
      ['a value past 64 characters', (r) => r.event('acme.report.run', { kind: 'x'.repeat(65) })],
      ['a value that is not finite', (r) => r.event('acme.report.run', { rows: Number.NaN })],
      ['a value that is an object', (r) => r.event('acme.report.run', { rows: {} })],
      ['a reserved key', (r) => r.event('acme.report.run', { route: 'x' })],
      ['a key past the 16th', (r) => r.event('tm.launch.warm', Object.fromEntries(Array.from({ length: 15 }, (_, index) => [`k${index}`, index])))],
      ['a duration that is not finite', (r) => r.timing('acme.report.render', Number.POSITIVE_INFINITY)],
      ['a trace id that is not one', (r) => r.event('acme.report.run', {}, { traceId: 'abc' })],
      ['an error source that is not one', (r) => r.error(new Error('x'), { source: 'bogus' })],
      ['an error context value that is free text', (r) => r.error(new Error('x'), { where: 'two words' })],
    ];

    it.each(BAD_NAMES)('throws on the name %j in development, and records nothing', (name) => {
      const r = rig({ dev: true });
      expect(() => r.event(name)).toThrow(TmTelemetryError);
      expect(r.recent()).toEqual([]);
    });

    it.each(BAD_CALLS)('throws on %s in development, and records nothing', (_, call) => {
      const r = rig({ dev: true });
      expect(() => call(r)).toThrow(TmTelemetryError);
      expect(r.recent()).toEqual([]);
    });

    it('names the malformed name in the error', () => {
      const r = rig({ dev: true });
      expect(() => r.event('acme.report')).toThrow('The telemetry name "acme.report" is malformed');
    });

    it('takes 16 properties, the reserved ones included', () => {
      const r = rig({ dev: true });
      r.event('tm.launch.warm', Object.fromEntries(Array.from({ length: 14 }, (_, index) => [`k${index}`, index])));
      expect(Object.keys(r.recent()[0]?.props ?? {})).toHaveLength(16);
    });

    it('drops a malformed name in production, and counts it', () => {
      const { sent, event, recent, pipeline } = rig();
      for (const name of BAD_NAMES) {
        expect(() => event(name)).not.toThrow();
      }
      pipeline.flush();

      expect(recent()).toEqual([]);
      expect(itemsOf(sent)).toEqual([
        expect.objectContaining({ kind: 'event', name: 'tm.telemetry.dropped', at: iso(T0), properties: { ...PAGE, count: BAD_NAMES.length, reason: 'name' } }),
      ]);
    });

    it('drops a malformed property in production, keeps the item, and counts it', () => {
      const r = rig();
      for (const [, call] of BAD_CALLS) {
        expect(() => call(r)).not.toThrow();
      }
      r.pipeline.flush();

      expect(r.recent()).toHaveLength(BAD_CALLS.length);
      expect(r.recent()[0]?.props).toEqual({ ...PAGE, sampleRate: 1 });
      expect(itemsOf(r.sent)[0]).toEqual(
        expect.objectContaining({ name: 'tm.telemetry.dropped', properties: { ...PAGE, count: BAD_CALLS.length, reason: 'prop' } }),
      );
    });

    it('never throws in production, even when sending fails', () => {
      const { event, pipeline, state } = rig({ failSend: true });
      event('tm.launch.warm');
      expect(() => pipeline.flush()).not.toThrow();
      state.dev = true;
      event('tm.launch.warm');
      expect(() => pipeline.flush()).toThrow('send failed');
    });

    it('writes every item to the console in development only', () => {
      const r = rig({ dev: true });
      r.event('tm.launch.warm', { rows: 3 });
      r.state.dev = false;
      r.event('tm.launch.cold');

      expect(r.debugged).toEqual([['tm.launch.warm', { ...PAGE, rows: 3 }]]);
    });
  });

  describe('flush triggers', () => {
    it('flushes 30 s after the first item', () => {
      const { sent, event } = rig();
      event('tm.launch.warm');
      vi.advanceTimersByTime(10_000);
      event('tm.launch.cold');
      vi.advanceTimersByTime(19_999);
      expect(sent).toEqual([]);

      vi.advanceTimersByTime(1);
      expect(namesOf(sent)).toEqual(['tm.launch.warm', 'tm.launch.cold']);
    });

    it('flushes at once at 50 items', () => {
      const { sent, event } = rig();
      for (let index = 0; index < 49; index++) {
        event('tm.launch.warm');
      }
      expect(sent).toEqual([]);

      event('tm.launch.warm');
      expect(sent).toHaveLength(1);
      expect(sent[0]?.items).toHaveLength(50);
    });

    it('flushes 1 s after the latest error', () => {
      const { sent, error, event } = rig();
      event('tm.launch.warm');
      error(appError(1));
      vi.advanceTimersByTime(500);
      error(appError(2));
      vi.advanceTimersByTime(999);
      expect(sent).toEqual([]);

      vi.advanceTimersByTime(1);
      expect(namesOf(sent)).toEqual(['tm.launch.warm', 'tm.error.captured', 'tm.error.captured']);
    });

    it('flushes on flush()', () => {
      const { sent, event, pipeline } = rig();
      event('tm.launch.warm');
      pipeline.flush();
      expect(namesOf(sent)).toEqual(['tm.launch.warm']);
    });

    it('sends nothing for an empty queue', () => {
      const { sent, pipeline } = rig();
      pipeline.flush();
      pipeline.flush('pagehide');
      expect(sent).toEqual([]);
    });

    it('cancels its timers on stop', () => {
      const { sent, event, pipeline } = rig();
      event('tm.launch.warm');
      pipeline.stop();
      vi.advanceTimersByTime(60_000);
      expect(sent).toEqual([]);
    });
  });

  describe('batches', () => {
    /**
     * Queues items while sending is held, then lifts the hold.
     *
     * @param r - The rig.
     * @param count - How many items.
     * @param props - Each item's properties.
     */
    function queueHeld(r: TmPipelineRig, count: number, props: TmTelemetryProps = {}): void {
      r.state.held = true;
      for (let index = 0; index < count; index++) {
        r.event('tm.launch.warm', { ...props, index });
      }
      r.state.held = false;
    }

    it('holds at most 100 items, and sends the rest a second later', () => {
      const r = rig();
      queueHeld(r, 150);
      r.pipeline.flush();
      expect(r.sent.map((batch) => batch.items.length)).toEqual([100]);

      // A regular flush during the pause joins it.
      r.pipeline.flush();
      vi.advanceTimersByTime(999);
      expect(r.sent).toHaveLength(1);
      vi.advanceTimersByTime(1);
      expect(r.sent.map((batch) => batch.items.length)).toEqual([100, 50]);
      expect(itemsOf(r.sent).map((item) => item.properties['index'])).toEqual(Array.from({ length: 150 }, (_, index) => index));
    });

    it('keeps each body within 16 KB', () => {
      const r = rig();
      const wide = Object.fromEntries(Array.from({ length: 13 }, (_, index) => [`k${index}`, 'x'.repeat(64)]));
      queueHeld(r, 40, wide);
      r.pipeline.flush();
      vi.advanceTimersByTime(10_000);

      expect(r.sent.length).toBeGreaterThan(2);
      expect(r.sent.every((batch) => new TextEncoder().encode(JSON.stringify(batch)).byteLength <= 16 * 1024)).toBe(true);
      expect(r.sent.slice(0, -1).every((batch) => new TextEncoder().encode(JSON.stringify(batch)).byteLength > 14 * 1024)).toBe(true);
      expect(itemsOf(r.sent)).toHaveLength(40);
    });

    it('sends one batch at most on pagehide', () => {
      const r = rig();
      queueHeld(r, 150);
      r.pipeline.flush('pagehide');
      vi.advanceTimersByTime(5_000);
      expect(r.sent.map((batch) => batch.items.length)).toEqual([100]);
    });

    it("sends one tenant's items per batch, the first item's tenant first", () => {
      const r = rig();
      r.state.held = true;
      r.event('tm.launch.warm', { index: 0 });
      r.state.tenantId = 18;
      r.event('tm.launch.warm', { index: 1 });
      r.state.tenantId = 17;
      r.event('tm.launch.warm', { index: 2 });
      r.state.held = false;
      r.pipeline.flush();
      vi.advanceTimersByTime(1_000);

      expect(r.sent.map((batch) => [batch.tenantId, batch.items.map((item) => item.properties['index'])])).toEqual([
        [17, [0, 2]],
        [18, [1]],
      ]);
    });

    it('caps the queue at 200, dropping the oldest item that is not an error', () => {
      const r = rig();
      r.state.held = true;
      r.error(appError(1));
      for (let index = 0; index < 205; index++) {
        r.event('tm.launch.warm', { index });
      }
      r.state.held = false;
      r.pipeline.flush();
      vi.advanceTimersByTime(5_000);

      const items = itemsOf(r.sent);
      expect(items[0]).toMatchObject({ name: 'tm.telemetry.dropped', properties: { count: 6, reason: 'overflow' } });
      expect(items[1]?.name).toBe('tm.error.captured');
      expect(items.slice(2).map((item) => item.properties['index'])).toEqual(Array.from({ length: 199 }, (_, index) => index + 6));
    });
  });

  describe('holds', () => {
    it('holds the queue while offline or without an active session, and sends it once the hold lifts', () => {
      const { sent, event, state } = rig({ held: true });
      event('tm.launch.warm');
      vi.advanceTimersByTime(90_000);
      expect(sent).toEqual([]);

      state.held = false;
      vi.advanceTimersByTime(30_000);
      expect(namesOf(sent)).toEqual(['tm.launch.warm']);
    });

    it('holds the queue until the UI version is known', () => {
      const { sent, event, state, pipeline } = rig({ version: null });
      event('tm.launch.warm');
      pipeline.flush();
      expect(sent).toEqual([]);

      state.version = '1.4.0';
      pipeline.flush();
      expect(sent[0]?.version).toBe('1.4.0');
    });

    it('queues nothing while disabled, yet fills the ring', () => {
      const { sent, event, error, recent, pipeline } = rig({ settings: { enabled: false, sampleRate: 1 } });
      event('tm.launch.warm');
      error(appError(1));
      pipeline.flush();
      vi.advanceTimersByTime(60_000);

      expect(sent).toEqual([]);
      expect(recent().map((item) => item.name)).toEqual(['tm.launch.warm', 'tm.error.captured']);
    });

    it('queues under the cold defaults, and drops that queue once the distribution turns telemetry off', () => {
      const r = rig({ settings: undefined });
      r.event('acme.report.run');
      expect(r.recent()[0]?.props).toEqual({ ...PAGE, sampleRate: 1 });

      r.state.settings = { enabled: false, sampleRate: 1 };
      r.pipeline.flush();
      r.state.settings = { enabled: true, sampleRate: 1 };
      r.pipeline.flush();
      expect(r.sent).toEqual([]);
    });

    it('keeps the last 100 items in the ring, newest last', () => {
      const { event, recent } = rig();
      for (let index = 0; index < 105; index++) {
        event('tm.launch.warm', { index });
      }
      expect(recent()).toHaveLength(100);
      expect(recent().at(-1)?.props['index']).toBe(104);
      expect(recent()[0]?.props['index']).toBe(5);
    });
  });

  describe('sampling', () => {
    it('sends nothing sampled at rate 0, and every other item', () => {
      const { sent, event, timing, error, recent, pipeline } = rig({ settings: { enabled: true, sampleRate: 0 } });
      event('tm.page.visit');
      timing('tm.vitals.load', 10);
      event('tm.action.invoked');
      event('acme.report.run');
      event('tm.launch.warm', {}, { sampled: true });
      event('tm.session.expired');
      event('acme.report.closed', {}, { sampled: false });
      error(appError(1));
      pipeline.flush();

      expect(recent()).toHaveLength(8);
      expect(namesOf(sent)).toEqual(['tm.session.expired', 'acme.report.closed', 'tm.error.captured']);
    });

    it('sends everything at rate 1, each sampled item carrying the rate', () => {
      const { sent, event, pipeline } = rig({ settings: { enabled: true, sampleRate: 1 } });
      event('tm.page.visit');
      event('acme.report.run');
      event('tm.session.expired');
      pipeline.flush();

      expect(itemsOf(sent).map((item) => [item.name, item.properties['sampleRate']])).toEqual([
        ['tm.page.visit', 1],
        ['acme.report.run', 1],
        ['tm.session.expired', undefined],
      ]);
    });

    it("decides by the tab's one draw against the rate", () => {
      const below = rig({ draw: 0.3, settings: { enabled: true, sampleRate: 0.5 } });
      const above = rig({ draw: 0.7, settings: { enabled: true, sampleRate: 0.5 } });
      for (const r of [below, above]) {
        r.event('acme.report.run');
        r.event('acme.report.run');
        r.pipeline.flush();
      }

      expect(itemsOf(below.sent).map((item) => item.properties['sampleRate'])).toEqual([0.5, 0.5]);
      expect(above.sent).toEqual([]);
    });

    it('clamps the rate to 0 to 1', () => {
      const { recent, event } = rig({ settings: { enabled: true, sampleRate: 1.5 } });
      event('acme.report.run');
      expect(recent()[0]?.props['sampleRate']).toBe(1);
    });
  });
});
