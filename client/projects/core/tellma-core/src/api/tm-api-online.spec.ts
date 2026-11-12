// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';

import { TM_API_HOST } from './tm-api-host';
import { TmApiOnline } from './tm-api-online';
import { tmAnswer, TmFakeApiHost } from './tm-api.testing.util';

// The server's reachability: false while the browser is offline or after a
// network failure, restored by a probe of `/health/live` that a visible tab
// sends every 5 s, backing off to 30 s.

/** Configures the probe over a faked browser and fakes the clock. */
function setUp(): { host: TmFakeApiHost; online: TmApiOnline } {
  const host = new TmFakeApiHost();
  TestBed.configureTestingModule({ providers: [{ provide: TM_API_HOST, useValue: host }] });
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
  return { host, online: TestBed.inject(TmApiOnline) };
}

/** The probe URLs sent so far. */
const probes = (host: TmFakeApiHost): string[] => host.requests.map((request) => request.url);

describe('TmApiOnline', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('follows the browser', () => {
    const { host, online } = setUp();
    expect(online.online()).toBe(true);
    host.onLineNow = false;
    host.fire('window', 'offline');
    expect(online.online()).toBe(false);
    host.onLineNow = true;
    host.fire('window', 'online');
    expect(online.online()).toBe(true);
  });

  it('probes every 5 s after a failure, backing off to 30 s, until any answer', async () => {
    const { host, online } = setUp();
    online.failed();
    expect(online.online()).toBe(false);
    const waits = [5_000, 10_000, 20_000, 30_000, 30_000];
    for (const [i, wait] of waits.entries()) {
      await vi.advanceTimersByTimeAsync(wait - 1);
      expect(host.requests).toHaveLength(i);
      await vi.advanceTimersByTimeAsync(1);
      expect(host.requests).toHaveLength(i + 1);
      expect(host.last().init).toMatchObject({ credentials: 'same-origin', cache: 'no-store' });
      host.last().fail();
    }
    expect(probes(host).every((url) => url === '/health/live')).toBe(true);
    await vi.advanceTimersByTimeAsync(30_000);
    // Even a refusal is an answer: the server is reachable.
    host.last().answer(tmAnswer(429));
    await vi.advanceTimersByTimeAsync(0);
    expect(online.online()).toBe(true);
    await vi.advanceTimersByTimeAsync(120_000);
    expect(host.requests).toHaveLength(waits.length + 1);
  });

  it('counts a probe that hangs for 10 s as a failure', async () => {
    const { host, online } = setUp();
    online.failed();
    await vi.advanceTimersByTimeAsync(5_000);
    expect(host.requests).toHaveLength(1);
    await vi.advanceTimersByTimeAsync(10_000);
    expect(host.last().aborted).toBe(true);
    await vi.advanceTimersByTimeAsync(10_000);
    expect(host.requests).toHaveLength(2);
    expect(online.online()).toBe(false);
  });

  it('ends the outage at the next answer of any call', async () => {
    const { host, online } = setUp();
    online.failed();
    online.answered();
    expect(online.online()).toBe(true);
    await vi.advanceTimersByTimeAsync(60_000);
    expect(host.requests).toHaveLength(0);
  });

  it('probes only while the page is visible', async () => {
    const { host, online } = setUp();
    host.visibleNow = false;
    online.failed();
    await vi.advanceTimersByTimeAsync(60_000);
    expect(host.requests).toHaveLength(0);
    host.visibleNow = true;
    host.fire('document', 'visibilitychange');
    await vi.advanceTimersByTimeAsync(5_000);
    expect(host.requests).toHaveLength(1);
  });

  it('stops probing while the browser is offline, and probes at once when it returns', async () => {
    const { host, online } = setUp();
    online.failed();
    host.onLineNow = false;
    host.fire('window', 'offline');
    await vi.advanceTimersByTimeAsync(60_000);
    expect(host.requests).toHaveLength(0);
    host.onLineNow = true;
    host.fire('window', 'online');
    expect(online.online()).toBe(false);
    expect(host.requests).toHaveLength(1);
    host.last().answer(tmAnswer(200));
    await vi.advanceTimersByTimeAsync(0);
    expect(online.online()).toBe(true);
  });
});
