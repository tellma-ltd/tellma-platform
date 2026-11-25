// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmRequestRecord } from './tm-request-log';

// Request outcomes (spec 0026 §5.3), measured from the request log: part of
// the telemetry pipeline's lazy chunk. Each settled request joins its route's
// summary for the current flush window, and each flush turns the window into
// one `tm.api.requests` event per route for the 20 busiest. A slow request
// becomes one `tm.api.slow` event with its trace id when it settles, at most
// 20 a tab per 10 minutes; one still pending when the page is hidden for good
// reports `outcome: 'pending'`. A route is summarised per tenant, so a batch
// keeps one tenant's items. The log keeps the last 100 requests, and the
// pipeline reads it on every change, so a request is seen settled before it
// leaves the ring.

/** One measurement, ready for the pipeline to record as an event. */
export interface TmRequestMeasure {
  /** The event's name. */
  readonly name: 'tm.api.requests' | 'tm.api.slow';
  /** When it happened, in epoch milliseconds. */
  readonly at: number;
  /** The tenant of its requests, or `null` for tenantless routes. */
  readonly tenantId: number | null;
  /** The slow request's trace id. */
  readonly traceId?: string;
  /** The event's own properties. */
  readonly props: Readonly<Record<string, string | number>>;
}

/** What one collection produced: the slow events admitted, and how many the throttle held back. */
export interface TmRequestCollection {
  /** The slow events. */
  readonly events: readonly TmRequestMeasure[];
  /** How many slow events the throttle held back. */
  readonly throttled: number;
}

/** One route's counts in the current window. */
interface TmRouteCounts {
  /** The route's tenant. */
  readonly tenantId: number | null;
  /** The route, without its query. */
  readonly api: string;
  /** The counts, by the event's property names. */
  readonly counts: Record<'count' | 'ok' | 'failed' | 'cancelled' | 'network' | 'sumMs' | 'maxMs' | 'over1s' | 'over3s' | 'background', number>;
}

/** The busiest routes a window reports. */
const ROUTES = 20;

/** The most slow events a tab sends in the throttle's window. */
const SLOW_LIMIT = 20;

/** The slow events' throttle window, in milliseconds. */
const SLOW_WINDOW_MS = 10 * 60_000;

/**
 * The request measurements of one tab. Internal: the telemetry pipeline
 * collects into it on every change of the request log and drains it at
 * each flush.
 */
export class TmRequestMeasures {
  /** The trace ids already summarised, among those the log still holds. */
  #seen = new Set<string>();

  /** The trace ids already reported slow, among those the log still holds. */
  #reported = new Set<string>();

  /** When each slow event of the throttle's window was admitted. */
  #slowAt: number[] = [];

  /** The current window's counts, by tenant and route. */
  readonly #window = new Map<string, TmRouteCounts>();

  /** Whether the current window holds a request, so a flush is due. */
  get waiting(): boolean {
    return this.#window.size > 0;
  }

  /**
   * Takes the requests that settled since the last collection into the
   * window, and reports the slow ones among them.
   *
   * @param records - The request log, newest first.
   * @param now - The current time, in epoch milliseconds.
   * @returns The slow events, and how many the throttle held back.
   */
  collect(records: readonly TmRequestRecord[], now: number): TmRequestCollection {
    const seen = new Set<string>();
    const events: TmRequestMeasure[] = [];
    let throttled = 0;
    for (const record of records) {
      if (record.outcome === 'pending') {
        continue;
      }
      seen.add(record.traceId);
      if (this.#seen.has(record.traceId)) {
        continue;
      }
      this.#add(record);
      if (record.slow && !this.#reported.has(record.traceId)) {
        if (this.#admit(now)) {
          events.push(slowEvent(record, record.startedAt + (record.durationMs ?? 0)));
        } else {
          throttled++;
        }
        this.#reported.add(record.traceId);
      }
    }
    // Only what the log still holds can come back, so the rest is forgotten.
    this.#seen = seen;
    this.#reported = new Set(records.filter((record) => this.#reported.has(record.traceId)).map((record) => record.traceId));
    return { events, throttled };
  }

  /**
   * Reports the slow requests still pending, as the page is hidden for good.
   * Each is reported once, even if the page comes back and it settles.
   *
   * @param records - The request log, newest first.
   * @param now - The current time, in epoch milliseconds.
   * @returns The slow events, and how many the throttle held back.
   */
  pending(records: readonly TmRequestRecord[], now: number): TmRequestCollection {
    const events: TmRequestMeasure[] = [];
    let throttled = 0;
    for (const record of records) {
      if (record.outcome === 'pending' && record.slow && !this.#reported.has(record.traceId)) {
        this.#reported.add(record.traceId);
        if (this.#admit(now)) {
          events.push(slowEvent(record, now));
        } else {
          throttled++;
        }
      }
    }
    return { events, throttled };
  }

  /**
   * Ends the window: one `tm.api.requests` event per route for the 20
   * busiest, and a new, empty window.
   *
   * @param now - The current time, in epoch milliseconds.
   * @returns The events, busiest route first.
   */
  drain(now: number): readonly TmRequestMeasure[] {
    const routes = [...this.#window.values()].sort((a, b) => b.counts.count - a.counts.count).slice(0, ROUTES);
    this.#window.clear();
    return routes.map(({ tenantId, api, counts }) => ({ name: 'tm.api.requests', at: now, tenantId, props: { api, ...counts } }));
  }

  /** Forgets the window, as when telemetry is off. */
  clear(): void {
    this.#window.clear();
  }

  /**
   * Adds a settled request to its route's counts.
   *
   * @param record - The request.
   */
  #add(record: TmRequestRecord): void {
    const api = apiOf(record.route);
    const key = `${record.tenantId ?? ''} ${api}`;
    let route = this.#window.get(key);
    if (route === undefined) {
      route = {
        tenantId: record.tenantId,
        api,
        counts: { count: 0, ok: 0, failed: 0, cancelled: 0, network: 0, sumMs: 0, maxMs: 0, over1s: 0, over3s: 0, background: 0 },
      };
      this.#window.set(key, route);
    }
    const { counts } = route;
    const ms = Math.round(record.durationMs ?? 0);
    counts.count++;
    // A failure the server answered counts as failed; one that never got an answer, as network.
    counts[record.outcome === 'ok' ? 'ok' : record.outcome === 'cancelled' ? 'cancelled' : record.status === undefined ? 'network' : 'failed']++;
    counts.sumMs += ms;
    counts.maxMs = Math.max(counts.maxMs, ms);
    counts.over1s += ms >= 1000 ? 1 : 0;
    counts.over3s += ms >= 3000 ? 1 : 0;
    counts.background += record.background ? 1 : 0;
  }

  /**
   * Admits a slow event under the throttle: at most 20 a tab per 10 minutes.
   *
   * @param now - The current time, in epoch milliseconds.
   * @returns Whether the event may be sent.
   */
  #admit(now: number): boolean {
    this.#slowAt = this.#slowAt.filter((at) => now - at < SLOW_WINDOW_MS);
    if (this.#slowAt.length >= SLOW_LIMIT) {
      return false;
    }
    this.#slowAt.push(now);
    return true;
  }
}

/**
 * A request's route as the events name it: without its query or fragment.
 *
 * @param route - The route as the HTTP layer received it.
 * @returns The route, such as `centers/query` or `/bff/user`.
 */
function apiOf(route: string): string {
  return route.split(/[?#]/, 1)[0];
}

/**
 * The `tm.api.slow` event of a request.
 *
 * @param record - The request.
 * @param at - When it settled, or now for one still pending.
 * @returns The event.
 */
function slowEvent(record: TmRequestRecord, at: number): TmRequestMeasure {
  return {
    name: 'tm.api.slow',
    at,
    tenantId: record.tenantId,
    traceId: record.traceId,
    props: {
      api: apiOf(record.route),
      durationMs: Math.round(record.durationMs ?? at - record.startedAt),
      outcome: record.outcome,
      ...(record.status === undefined ? {} : { status: record.status }),
      ...(record.code === undefined ? {} : { code: record.code }),
    },
  };
}
