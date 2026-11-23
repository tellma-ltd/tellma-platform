// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { effect } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { TM_API_HOST } from '../api/tm-api-host';
import { TmFakeApiHost } from '../api/tm-api.testing.util';
import { TmInputIdle } from './tm-input-idle';

// The input-idle clock on a fake clock and a faked window: idle 2 s after
// the last key, pointer, wheel or touch input, through passive capture
// listeners.

/** The inputs the clock listens to. */
const INPUTS = ['keydown', 'pointerdown', 'pointermove', 'wheel', 'touchstart'];

/** Creates the clock over a faked window, with the clock faked first. */
function setUp(): { host: TmFakeApiHost; idle: TmInputIdle } {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date', 'performance'] });
  const host = new TmFakeApiHost();
  TestBed.configureTestingModule({ providers: [{ provide: TM_API_HOST, useValue: host }] });
  return { host, idle: TestBed.inject(TmInputIdle) };
}

/** Whether a promise has settled, after the pending microtasks run. */
async function settled(promise: Promise<void>): Promise<boolean> {
  let done = false;
  void promise.then(() => (done = true));
  await vi.advanceTimersByTimeAsync(0);
  return done;
}

describe('TmInputIdle', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('listens to every input on the window through passive capture listeners', () => {
    const { host } = setUp();
    for (const type of INPUTS) {
      expect(host.listeners('window', type)).toBe(1);
      expect(host.listenOptions('window', type)).toEqual({ capture: true, passive: true });
    }
  });

  it('turns idle 2 s after it starts when no input comes', async () => {
    const { idle } = setUp();
    expect(idle.idle()).toBe(false);
    await vi.advanceTimersByTimeAsync(1999);
    expect(idle.idle()).toBe(false);
    await vi.advanceTimersByTimeAsync(1);
    expect(idle.idle()).toBe(true);
  });

  it.each(INPUTS)('ends the idle moment on %s, and turns idle again 2 s after the last input', async (type) => {
    const { host, idle } = setUp();
    await vi.advanceTimersByTimeAsync(2000);
    host.fire('window', type);
    expect(idle.idle()).toBe(false);
    // A stream of input keeps the tab busy until 2 s after its last event.
    for (let i = 0; i < 5; i++) {
      await vi.advanceTimersByTimeAsync(500);
      host.fire('window', type);
    }
    await vi.advanceTimersByTimeAsync(1999);
    expect(idle.idle()).toBe(false);
    await vi.advanceTimersByTimeAsync(1);
    expect(idle.idle()).toBe(true);
  });

  it('turns idle 2 s after the last input when the wall clock is set back', async () => {
    const { host, idle } = setUp();
    await vi.advanceTimersByTimeAsync(2000);
    host.fire('window', 'keydown');
    vi.setSystemTime(Date.now() - 3_600_000);
    await vi.advanceTimersByTimeAsync(1999);
    expect(idle.idle()).toBe(false);
    await vi.advanceTimersByTimeAsync(1);
    expect(idle.idle()).toBe(true);
  });

  it('never re-runs an effect that waits for an idle moment when the tab turns idle or busy', async () => {
    const { host, idle } = setUp();
    let runs = 0;
    TestBed.runInInjectionContext(() =>
      effect(() => {
        runs++;
        void idle.whenIdle();
      }),
    );
    TestBed.tick();
    for (let i = 0; i < 2; i++) {
      await vi.advanceTimersByTimeAsync(2000);
      TestBed.tick();
      host.fire('window', 'keydown');
      TestBed.tick();
    }
    expect(runs).toBe(1);
  });

  it('resolves whenIdle at once while idle', async () => {
    const { idle } = setUp();
    await vi.advanceTimersByTimeAsync(2000);
    expect(await settled(idle.whenIdle())).toBe(true);
  });

  it('resolves whenIdle only after 2 s without input', async () => {
    const { host, idle } = setUp();
    await vi.advanceTimersByTimeAsync(2000);
    host.fire('window', 'keydown');
    const first = idle.whenIdle();
    const second = idle.whenIdle();
    await vi.advanceTimersByTimeAsync(1500);
    host.fire('window', 'pointermove');
    await vi.advanceTimersByTimeAsync(1999);
    expect(await settled(first)).toBe(false);
    await vi.advanceTimersByTimeAsync(1);
    expect(await settled(first)).toBe(true);
    expect(await settled(second)).toBe(true);
  });

  it("hears the browser's own input on the window, even when a handler stops it", async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date', 'performance'] });
    const idle = TestBed.inject(TmInputIdle);
    await vi.advanceTimersByTimeAsync(2000);
    const target = document.body.appendChild(document.createElement('button'));
    const stop = (event: Event): void => event.stopPropagation();
    target.addEventListener('keydown', stop);
    try {
      target.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true }));
      expect(idle.idle()).toBe(false);
    } finally {
      target.remove();
    }
  });

  it('stops listening when the app ends', () => {
    const { host } = setUp();
    TestBed.resetTestingModule();
    for (const type of INPUTS) {
      expect(host.listeners('window', type)).toBe(0);
    }
  });
});
