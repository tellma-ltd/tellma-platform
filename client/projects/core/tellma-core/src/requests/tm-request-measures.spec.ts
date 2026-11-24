// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmRequestRecord } from './tm-request-log';
import { TmRequestMeasures } from './tm-request-measures';

// The request outcomes (spec 0026 §5.3): one summary per route for each
// flush window, the 20 busiest; one slow event per slow request, as it
// settles or as the page is hidden while it is pending, at most 20 a tab per
// 10 minutes.

/** When every spec starts. */
const T0 = Date.parse('2026-11-23T10:00:00.000Z');

/**
 * A request in the log.
 *
 * @param traceId - Its trace id.
 * @param patch - What differs from a quick success on `centers/query` in tenant 17.
 * @returns The record.
 */
function record(traceId: string, patch: Partial<TmRequestRecord> = {}): TmRequestRecord {
  return {
    traceId,
    route: 'centers/query',
    tenantId: 17,
    method: 'POST',
    background: false,
    startedAt: T0,
    durationMs: 100,
    outcome: 'ok',
    status: 200,
    slow: false,
    ...patch,
  };
}

describe('TmRequestMeasures', () => {
  it("sums each route's settled requests for the window, by outcome and duration", () => {
    const measures = new TmRequestMeasures();
    const log = [
      record('a', { route: 'centers/query?top=25', durationMs: 1_000.4 }),
      record('b', { durationMs: 3_200, slow: true, background: true }),
      record('c', { outcome: 'cancelled', status: undefined }),
      record('d', { outcome: 'error', status: 500, code: 'server' }),
      record('e', { outcome: 'error', status: undefined }),
      record('f', { outcome: 'pending', durationMs: null, status: undefined }),
      record('g', { route: '/bff/user', tenantId: null, method: 'GET' }),
    ];
    measures.collect(log, T0 + 5_000);
    expect(measures.waiting).toBe(true);

    expect(measures.drain(T0 + 30_000)).toEqual([
      {
        name: 'tm.api.requests',
        at: T0 + 30_000,
        tenantId: 17,
        props: { api: 'centers/query', count: 5, ok: 2, failed: 1, cancelled: 1, network: 1, sumMs: 4_500, maxMs: 3_200, over1s: 2, over3s: 1, background: 1 },
      },
      {
        name: 'tm.api.requests',
        at: T0 + 30_000,
        tenantId: null,
        props: { api: '/bff/user', count: 1, ok: 1, failed: 0, cancelled: 0, network: 0, sumMs: 100, maxMs: 100, over1s: 0, over3s: 0, background: 0 },
      },
    ]);
    expect(measures.waiting).toBe(false);
    expect(measures.drain(T0 + 60_000)).toEqual([]);
  });

  it('counts each request once, and a pending one when it settles', () => {
    const measures = new TmRequestMeasures();
    const pending = record('a', { outcome: 'pending', durationMs: null, status: undefined });
    measures.collect([pending], T0);
    expect(measures.waiting).toBe(false);

    const settled = { ...pending, outcome: 'ok' as const, durationMs: 40, status: 200 };
    measures.collect([settled], T0 + 40);
    measures.collect([record('b'), settled], T0 + 50);
    expect(measures.drain(T0 + 60).map((event) => event.props['count'])).toEqual([2]);
    measures.collect([record('b'), settled], T0 + 70);
    expect(measures.drain(T0 + 80)).toEqual([]);
  });

  it('keeps a route per tenant, and the 20 busiest routes of a window', () => {
    const measures = new TmRequestMeasures();
    const log = Array.from({ length: 22 }, (_, route) =>
      Array.from({ length: route + 1 }, (_, i) => record(`r${route}-${i}`, { route: `route-${route}` })),
    ).flat();
    measures.collect([...log, record('t18', { route: 'route-21', tenantId: 18 })], T0);

    const events = measures.drain(T0);
    expect(events).toHaveLength(20);
    expect(events.map((event) => event.props['api'])).toEqual(Array.from({ length: 20 }, (_, i) => `route-${21 - i}`));
    expect(events.every((event) => event.tenantId === 17)).toBe(true);
  });

  it('reports a slow request once as it settles, with its trace id, status and code', () => {
    const measures = new TmRequestMeasures();
    const slow = record('4bf92f3577b34da6a3ce929d0e0e4736', {
      route: 'centers/save?x=1',
      startedAt: T0,
      durationMs: 4_810.6,
      outcome: 'error',
      status: 500,
      code: 'server',
      slow: true,
    });
    expect(measures.collect([slow, record('quick')], T0 + 6_000)).toEqual({
      events: [
        {
          name: 'tm.api.slow',
          at: T0 + 4_810.6,
          tenantId: 17,
          traceId: '4bf92f3577b34da6a3ce929d0e0e4736',
          props: { api: 'centers/save', durationMs: 4_811, outcome: 'error', status: 500, code: 'server' },
        },
      ],
      throttled: 0,
    });
    expect(measures.collect([slow, record('quick')], T0 + 7_000)).toEqual({ events: [], throttled: 0 });
  });

  it('admits 20 slow events a tab per 10 minutes, and counts the rest', () => {
    const measures = new TmRequestMeasures();
    const slow = (id: string): TmRequestRecord => record(id, { durationMs: 3_000, slow: true });
    const first = measures.collect(Array.from({ length: 22 }, (_, i) => slow(`a${i}`)), T0);
    expect([first.events.length, first.throttled]).toEqual([20, 2]);

    // Still within the window.
    expect(measures.collect([slow('b0')], T0 + 599_999)).toMatchObject({ events: [], throttled: 1 });
    // Ten minutes after the first admissions, the throttle has room again.
    expect(measures.collect([slow('b1')], T0 + 600_000).events).toHaveLength(1);
  });

  it('reports the slow requests still pending as the page is hidden, each once', () => {
    const measures = new TmRequestMeasures();
    const hanging = record('a', { startedAt: T0, durationMs: null, outcome: 'pending', status: undefined, slow: true });
    const young = record('b', { startedAt: T0 + 5_000, durationMs: null, outcome: 'pending', status: undefined });
    expect(measures.pending([young, hanging], T0 + 6_200).events).toEqual([
      { name: 'tm.api.slow', at: T0 + 6_200, tenantId: 17, traceId: 'a', props: { api: 'centers/query', durationMs: 6_200, outcome: 'pending' } },
    ]);
    expect(measures.pending([young, hanging], T0 + 7_000).events).toEqual([]);

    // When the page comes back and the request settles, it is summarised but not reported slow again.
    expect(measures.collect([{ ...hanging, durationMs: 8_000, outcome: 'ok', status: 200 }], T0 + 8_000).events).toEqual([]);
    expect(measures.drain(T0 + 9_000)).toHaveLength(1);
  });

  it('forgets the window when cleared', () => {
    const measures = new TmRequestMeasures();
    measures.collect([record('a')], T0);
    measures.clear();
    expect(measures.waiting).toBe(false);
    expect(measures.drain(T0)).toEqual([]);
  });
});
