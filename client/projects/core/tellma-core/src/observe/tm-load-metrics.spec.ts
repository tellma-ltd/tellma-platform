// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmCollectLoadMetrics } from './tm-load-metrics';
import type { TmTelemetryProps } from './tm-telemetry';

// The hard load's paint metrics (spec 0026 §5.2), over a fake page: the
// buffered entries count, the event is emitted once at the first input or
// when the page is hidden, a metric the browser lacks stays out, and a
// prerendered page counts from its activation.

/** What a spec sets of the fake page. */
interface TmFakePage {
  /** The navigation entry, or `undefined` for none. */
  readonly navigation?: Partial<PerformanceNavigationTiming> & { readonly activationStart?: number };
  /** The entry types the browser observes. */
  readonly supported?: readonly string[];
  /** Whether a service worker controls the page. */
  readonly controlled?: boolean;
}

/** The fake page's handles. */
interface TmFakeRig {
  /** The window the collector reads. */
  readonly window: Window & typeof globalThis;
  /** The events emitted. */
  readonly emitted: TmTelemetryProps[];
  /** Delivers entries to the observers of their type, as the browser does in a later task. */
  readonly deliver: (type: string, ...entries: Partial<PerformanceEntry>[]) => void;
  /** Queues entries that only `takeRecords` hands over. */
  readonly queue: (type: string, ...entries: Partial<PerformanceEntry>[]) => void;
  /** Hides the page. */
  readonly hide: () => void;
}

/**
 * A fake page with a performance timeline whose buffered entries the spec
 * sets, and the collector started on it.
 *
 * @param page - The page's navigation, supported entry types and service worker.
 * @param buffered - The entries already in the timeline, by type.
 * @returns The rig.
 */
function collect(page: TmFakePage = {}, buffered: Readonly<Record<string, Partial<PerformanceEntry>[]>> = {}): TmFakeRig & { readonly stop: () => void } {
  const supported = page.supported ?? ['paint', 'largest-contentful-paint'];
  const observers: { type: string; callback: PerformanceObserverCallback; queued: PerformanceEntry[]; connected: boolean }[] = [];
  const list = (entries: PerformanceEntry[]): PerformanceObserverEntryList => ({ getEntries: () => entries }) as PerformanceObserverEntryList;

  /** The browser's observer, reduced to what the collector calls. */
  class FakeObserver {
    readonly #callback: PerformanceObserverCallback;
    #observer: (typeof observers)[number] | undefined;
    /** Keeps the callback. */
    constructor(callback: PerformanceObserverCallback) {
      this.#callback = callback;
    }
    /** Observes one type, delivering its buffered entries. */
    observe({ type }: { type: string; buffered: boolean }): void {
      if (!supported.includes(type)) {
        throw new TypeError(`Unsupported ${type}`);
      }
      this.#observer = { type, callback: this.#callback, queued: [], connected: true };
      observers.push(this.#observer);
      const entries = (buffered[type] ?? []) as PerformanceEntry[];
      if (entries.length > 0) {
        this.#callback(list(entries), this as unknown as PerformanceObserver);
      }
    }
    /** Hands over the entries not delivered yet. */
    takeRecords(): PerformanceEntry[] {
      return this.#observer?.queued.splice(0) ?? [];
    }
    /** Stops observing. */
    disconnect(): void {
      if (this.#observer !== undefined) {
        this.#observer.connected = false;
      }
    }
  }

  const window = new EventTarget() as EventTarget & Record<string, unknown>;
  const document = new EventTarget() as EventTarget & { visibilityState: DocumentVisibilityState };
  document.visibilityState = 'visible';
  Object.assign(window, {
    document,
    performance: { getEntriesByType: (type: string) => (type === 'navigation' && page.navigation !== undefined ? [page.navigation] : []) },
    PerformanceObserver: FakeObserver,
    navigator: { serviceWorker: { controller: page.controlled === true ? {} : null } },
  });
  const emitted: TmTelemetryProps[] = [];
  const stop = tmCollectLoadMetrics({ window: window as unknown as Window & typeof globalThis, emit: (props) => emitted.push(props) });
  return {
    window: window as unknown as Window & typeof globalThis,
    emitted,
    stop,
    deliver: (type, ...entries) => {
      for (const observer of observers.filter((held) => held.type === type && held.connected)) {
        observer.callback(list(entries as PerformanceEntry[]), {} as PerformanceObserver);
      }
    },
    queue: (type, ...entries) => {
      for (const observer of observers.filter((held) => held.type === type && held.connected)) {
        observer.queued.push(...(entries as PerformanceEntry[]));
      }
    },
    hide: () => {
      document.visibilityState = 'hidden';
      document.dispatchEvent(new Event('visibilitychange'));
    },
  };
}

/** A navigation that took 180 ms to its first byte. */
const NAVIGATION = { type: 'navigate', responseStart: 180 } as const;

describe('tmCollectLoadMetrics', () => {
  it('emits the buffered and later paints once, at the first input', () => {
    const rig = collect({ navigation: NAVIGATION }, { paint: [{ name: 'first-paint', startTime: 300 }, { name: 'first-contentful-paint', startTime: 412.4 }] });
    rig.deliver('largest-contentful-paint', { startTime: 900 });
    rig.deliver('largest-contentful-paint', { startTime: 1_250.6 });
    expect(rig.emitted).toEqual([]);

    rig.window.dispatchEvent(new Event('pointerdown'));
    rig.window.dispatchEvent(new Event('keydown'));
    expect(rig.emitted).toEqual([{ ttfbMs: 180, fcpMs: 412, lcpMs: 1_251, navigationType: 'navigate', controlled: false }]);

    // Later candidates and inputs change nothing.
    rig.deliver('largest-contentful-paint', { startTime: 4_000 });
    rig.hide();
    expect(rig.emitted).toHaveLength(1);
  });

  it('emits when the page is hidden, with the entries not delivered yet', () => {
    const rig = collect({ navigation: { type: 'reload', responseStart: 90 }, controlled: true });
    rig.queue('paint', { name: 'first-contentful-paint', startTime: 200 });
    rig.queue('largest-contentful-paint', { startTime: 650 });
    rig.hide();
    expect(rig.emitted).toEqual([{ ttfbMs: 90, fcpMs: 200, lcpMs: 650, navigationType: 'reload', controlled: true }]);
  });

  it('counts from the activation of a prerendered page', () => {
    const rig = collect(
      { navigation: { type: 'navigate', responseStart: 120, activationStart: 2_000 } },
      { paint: [{ name: 'first-contentful-paint', startTime: 300 }], 'largest-contentful-paint': [{ startTime: 2_400 }] },
    );
    rig.window.dispatchEvent(new Event('keydown'));
    expect(rig.emitted).toEqual([{ ttfbMs: 0, fcpMs: 0, lcpMs: 400, navigationType: 'prerender', controlled: false }]);
  });

  it('leaves out what the browser does not expose, never sending zero', () => {
    const rig = collect({ navigation: { type: 'navigate', responseStart: 0 }, supported: ['paint'] });
    rig.hide();
    expect(rig.emitted).toEqual([{ navigationType: 'navigate', controlled: false }]);

    const bare = collect({ supported: [] });
    bare.window.dispatchEvent(new Event('keydown'));
    expect(bare.emitted).toEqual([{ controlled: false }]);
  });

  it('reports only the navigation types the event names', () => {
    const rig = collect({ navigation: { type: 'other' as NavigationTimingType, responseStart: 50 } });
    rig.hide();
    expect(rig.emitted).toEqual([{ ttfbMs: 50, controlled: false }]);
  });

  it('emits nothing once stopped', () => {
    const rig = collect({ navigation: NAVIGATION });
    rig.stop();
    rig.window.dispatchEvent(new Event('keydown'));
    rig.hide();
    expect(rig.emitted).toEqual([]);
  });
});
