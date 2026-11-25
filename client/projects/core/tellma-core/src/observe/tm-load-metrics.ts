// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, DOCUMENT, type Injector } from '@angular/core';

import { TmTelemetry, type TmTelemetryProps } from './tm-telemetry';

// The hard load's paint metrics (spec 0026 §5.2): a lazy chunk of the
// primary, which the app's startup loads at idle after the first render. It
// observes with `buffered: true`, so loading late loses no entry, and emits
// one sampled `tm.vitals.load` per document when the largest contentful
// paint finalises: at the first input, or when the page is hidden. A metric
// the browser does not expose is left out, never sent as zero. Times count
// from the navigation's activation, so a prerendered page reports what its
// user waited for. Whether a service worker served the page is read from the
// navigation's `workerStart`. Launch timings belong to spec 0024.

/** The navigation types the event reports. */
const NAVIGATION_TYPES: ReadonlySet<string> = new Set(['navigate', 'reload', 'back_forward', 'prerender']);

/** The inputs that finalise the largest contentful paint. */
const INPUTS = ['keydown', 'pointerdown'] as const;

/** What the collector reads of the page: the window in an app, a fake in a spec. */
export interface TmLoadMetricsHost {
  /** The page's window: its performance timeline, observers and events. */
  readonly window: Window & typeof globalThis;
  /** Records the event. */
  readonly emit: (props: TmTelemetryProps) => void;
}

/**
 * Collects the load's metrics and emits them once, when the largest
 * contentful paint finalises.
 *
 * @param host - The page and where the event goes.
 * @returns What stops the collector before it has emitted.
 */
export function tmCollectLoadMetrics({ window, emit }: TmLoadMetricsHost): () => void {
  const { document, performance } = window;
  const navigation = performance.getEntriesByType?.('navigation')[0] as PerformanceNavigationTiming | undefined;
  // A prerendered page's times count from its activation.
  const activation = (navigation as { readonly activationStart?: number } | undefined)?.activationStart ?? 0;
  const since = (time: number): number => Math.round(Math.max(0, time - activation));
  let fcp: number | undefined;
  let lcp: number | undefined;

  // --- The paints, with what happened before the chunk loaded ---
  const observers: (readonly [PerformanceObserver, (entry: PerformanceEntry) => void])[] = [];
  const observe = (type: string, take: (entry: PerformanceEntry) => void): void => {
    try {
      const observer = new window.PerformanceObserver((list) => list.getEntries().forEach(take));
      observer.observe({ type, buffered: true });
      observers.push([observer, take]);
    } catch {
      // The browser does not expose this metric: it stays out of the event.
    }
  };
  observe('paint', (entry) => {
    if (entry.name === 'first-contentful-paint') {
      fcp = entry.startTime;
    }
  });
  // The largest so far; the browser stops reporting candidates at the first input.
  observe('largest-contentful-paint', (entry) => (lcp = entry.startTime));

  // --- Finalised at the first input, or when the page is hidden ---
  let done = false;
  const stop = (): void => {
    done = true;
    INPUTS.forEach((type) => window.removeEventListener(type, finalise, true));
    document.removeEventListener('visibilitychange', hidden, true);
    observers.forEach(([observer]) => observer.disconnect());
  };
  const finalise = (): void => {
    if (done) {
      return;
    }
    // The entries the observers have not delivered yet still count.
    observers.forEach(([observer, take]) => observer.takeRecords().forEach(take));
    stop();
    const type = navigation?.type === 'navigate' && activation > 0 ? 'prerender' : navigation?.type;
    emit({
      ...(navigation === undefined || navigation.responseStart <= 0 ? {} : { ttfbMs: since(navigation.responseStart) }),
      ...(fcp === undefined ? {} : { fcpMs: since(fcp) }),
      ...(lcp === undefined ? {} : { lcpMs: since(lcp) }),
      ...(type === undefined || !NAVIGATION_TYPES.has(type) ? {} : { navigationType: type }),
      // A worker handled the navigation's request only when it stamped its start; a worker that claims the page later served nothing.
      ...(navigation?.workerStart === undefined ? {} : { controlled: navigation.workerStart > 0 }),
    });
  };
  const hidden = (): void => {
    if (document.visibilityState === 'hidden') {
      finalise();
    }
  };
  INPUTS.forEach((type) => window.addEventListener(type, finalise, true));
  document.addEventListener('visibilitychange', hidden, true);
  return stop;
}

/**
 * Starts the app's collector. Internal: the app's startup loads this chunk
 * at idle and calls it once.
 *
 * @param injector - The root injector.
 */
export function tmStartLoadMetrics(injector: Injector): void {
  const window = injector.get(DOCUMENT).defaultView;
  if (window?.PerformanceObserver === undefined) {
    return;
  }
  const telemetry = injector.get(TmTelemetry);
  const stop = tmCollectLoadMetrics({ window, emit: (props) => telemetry.event('tm.vitals.load', props) });
  injector.get(DestroyRef).onDestroy(stop);
}
