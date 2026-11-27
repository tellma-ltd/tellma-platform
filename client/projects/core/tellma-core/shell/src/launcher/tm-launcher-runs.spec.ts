// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signal, type WritableSignal } from '@angular/core';
import type { TmLauncherItem, TmLauncherProvider, TmLauncherSearchContext, TmLocalization, TmUrls } from '@tellma/core';

import { type TmLauncherBucket, TmLauncherRuns, type TmLauncherRunsHost } from './tm-launcher-runs';

// The launcher's provider runs alone, on a fake clock, over a host of
// signals: the burst's two edges, the run in flight that the next keystroke
// aborts before the trailing edge, and the composition and held key that
// hold the runs, the trailing edge included, until they end.

/** The burst's quiet time, in milliseconds. */
const BURST_MS = 50;

/** One call of the test's provider. */
interface TmCall {
  /** The query as typed. */
  readonly query: string;
  /** The abort signal. */
  readonly signal: AbortSignal;
  /** Answers the call. */
  readonly answer: (items: readonly TmLauncherItem[]) => void;
}

/** A host of signals and a provider whose answers the test gives, with the runs over them. */
function rig(): {
  readonly runs: TmLauncherRuns;
  readonly query: WritableSignal<string>;
  readonly composing: WritableSignal<boolean>;
  readonly buckets: WritableSignal<readonly TmLauncherBucket[]>;
  readonly calls: TmCall[];
} {
  const calls: TmCall[] = [];
  const provider: TmLauncherProvider = {
    id: 'records',
    labelKey: 'Records',
    order: 10,
    search: (query: string, context: TmLauncherSearchContext) =>
      new Promise<readonly TmLauncherItem[]>((resolve) => calls.push({ query, signal: context.signal, answer: resolve })),
  };
  const query = signal('');
  const composing = signal(false);
  const buckets = signal<readonly TmLauncherBucket[]>([]);
  const host: TmLauncherRunsHost = {
    query,
    composing,
    buckets,
    l10n: { language: signal('en'), ɵtext: (key: string) => () => key } as unknown as TmLocalization,
    urls: { tenantId: signal(17) } as unknown as TmUrls,
    answered: () => undefined,
  };
  return { runs: new TmLauncherRuns([provider], host), query, composing, buckets, calls };
}

/** Lets the runs' promise chains settle. */
async function drain(): Promise<void> {
  for (let i = 0; i < 5; i++) {
    await Promise.resolve();
  }
}

describe('TmLauncherRuns', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("aborts the run in flight on the burst's next keystroke, before its trailing edge, and drops its answer", async () => {
    const { runs, query, buckets, calls } = rig();
    query.set('cen');
    runs.changed();
    await drain();
    expect(calls.map((call) => call.query)).toEqual(['cen']);
    const first = calls[0]!;
    query.set('cent');
    runs.changed();
    // Within the burst: no new run yet, and the old one is aborted at once.
    expect(first.signal.aborted).toBe(true);
    first.answer([{ id: 'x', href: '/17/x', label: 'Stale' }]);
    await drain();
    expect(buckets()).toEqual([]);
    vi.advanceTimersByTime(BURST_MS);
    await drain();
    expect(calls.map((call) => call.query)).toEqual(['cen', 'cent']);
    calls[1]!.answer([{ id: '1', href: '/17/centers/1', label: 'Center 1' }]);
    await drain();
    expect(buckets().map((bucket) => bucket.items.map((item) => item.label))).toEqual([['Center 1']]);
  });

  it('runs again on text typed back to that of an aborted run', async () => {
    const { runs, query, calls } = rig();
    query.set('cen');
    runs.changed();
    await drain();
    query.set('cent');
    runs.changed();
    query.set('cen');
    runs.changed();
    vi.advanceTimersByTime(BURST_MS);
    await drain();
    expect(calls.map((call) => [call.query, call.signal.aborted])).toEqual([
      ['cen', true],
      ['cen', false],
    ]);
  });

  it('never runs during a composition, not even on a trailing edge already due, and runs once it ends', async () => {
    const { runs, query, composing, calls } = rig();
    query.set('ce');
    runs.changed();
    await drain();
    query.set('cen');
    runs.changed();
    // The composition starts within the burst, its trailing edge still due.
    composing.set(true);
    query.set('cenق');
    vi.advanceTimersByTime(BURST_MS);
    await drain();
    expect(calls.map((call) => call.query)).toEqual(['ce']);
    // The end of the composition is the input it stood for.
    query.set('cenقا');
    composing.set(false);
    runs.changed();
    await drain();
    expect(calls.map((call) => call.query)).toEqual(['ce', 'cenقا']);
  });

  it('holds the runs while a key repeats, the trailing edge included, and runs on the release', async () => {
    const { runs, query, calls } = rig();
    query.set('ce');
    runs.changed();
    await drain();
    query.set('cen');
    runs.changed();
    runs.hold();
    query.set('cennnn');
    vi.advanceTimersByTime(BURST_MS);
    await drain();
    expect(calls.map((call) => call.query)).toEqual(['ce']);
    runs.release();
    await drain();
    expect(calls.map((call) => call.query)).toEqual(['ce', 'cennnn']);
  });
});
