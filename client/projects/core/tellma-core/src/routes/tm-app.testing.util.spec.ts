// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_WAIT_DEADLINE_MS, tmSettleUntil, tmWaitUntil } from './tm-app.testing.util';

// The waits on state the specs share: they return once the condition holds,
// and fail past a real deadline with the condition named, never waiting on.

/** The browser's own timer, taken before a spec fakes the timers. */
const realSetTimeout = globalThis.setTimeout;

/** A wait past the deadline, which leaves room under the runner's own timeout. */
const PAST_THE_DEADLINE = { timeout: TM_WAIT_DEADLINE_MS + 4_000 };

describe('the waits on state', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('returns once the condition holds', async () => {
    const state = { ready: false };
    realSetTimeout(() => (state.ready = true), 50);
    await tmSettleUntil(() => state.ready);
    expect(state.ready).toBe(true);
  });

  it('fails a condition that never holds by the deadline, naming it, and stops checking it', PAST_THE_DEADLINE, async () => {
    const state = { ready: false, checks: 0 };
    const condition = (): boolean => {
      state.checks++;
      return state.ready;
    };

    const started = performance.now();
    await expect(tmSettleUntil(condition)).rejects.toThrow(/did not hold within \d+ ms: [\s\S]*state\.ready/);
    expect(performance.now() - started).toBeGreaterThanOrEqual(TM_WAIT_DEADLINE_MS);

    // Nothing runs the condition once the wait has failed.
    const checked = state.checks;
    await new Promise((resolve) => realSetTimeout(resolve, 100));
    expect(state.checks).toBe(checked);
  });

  it('keeps the deadline in real time under faked timers, never moving them, and names a described condition', PAST_THE_DEADLINE, async () => {
    // Every timer faked, the page's clock (performance.now) among them.
    vi.useFakeTimers();
    const now = Date.now();
    await expect(tmWaitUntil(() => false, () => vi.advanceTimersByTimeAsync(0), 'the upload to be sent')).rejects.toThrow(
      /did not hold within \d+ ms: the upload to be sent$/,
    );
    expect(Date.now()).toBe(now);
  });
});
