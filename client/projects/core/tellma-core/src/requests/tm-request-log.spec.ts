// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';

import { TM_NOW } from '../app/tm-now';
import { TmAppVersion } from './tm-app-version';
import { type TmRequestStart, TmRequestLog } from './tm-request-log';

// The request log (spec 0026 §8.1): a ring of the last 100 requests, newest
// first, each slow from 3 s while pending, and the server build each answer
// reports.

describe('TmRequestLog', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(Date.UTC(2026, 9, 20, 11, 0, 0));
    TestBed.configureTestingModule({ providers: [{ provide: TM_NOW, useValue: () => Date.now() }] });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  /** A request to `centers/query` with the given trace id. */
  function start(traceId: string, patch: Partial<TmRequestStart> = {}): TmRequestStart {
    return { traceId, route: 'centers/query', tenantId: 17, method: 'POST', background: false, ...patch };
  }

  it('records a request as pending, then how it ended', () => {
    const log = TestBed.inject(TmRequestLog);
    const handle = log.begin(start('a1', { background: true }));
    expect(log.recent()).toEqual([
      {
        traceId: 'a1',
        route: 'centers/query',
        tenantId: 17,
        method: 'POST',
        background: true,
        startedAt: Date.now(),
        durationMs: null,
        outcome: 'pending',
        slow: false,
      },
    ]);

    vi.advanceTimersByTime(250);
    handle.end({ outcome: 'error', status: 422, code: 'validation' });
    expect(log.recent()[0]).toMatchObject({ durationMs: 250, outcome: 'error', status: 422, code: 'validation', slow: false });
  });

  it('keeps the last 100 requests, newest first', () => {
    const log = TestBed.inject(TmRequestLog);
    const handles = Array.from({ length: 101 }, (_, i) => log.begin(start(`t${i}`)));
    expect(log.recent()).toHaveLength(100);
    expect(log.recent()[0].traceId).toBe('t100');
    expect(log.recent().at(-1)?.traceId).toBe('t1');

    // A request that left the ring ends without coming back.
    handles[0].end({ outcome: 'ok', status: 200 });
    expect(log.recent().some((record) => record.traceId === 't0')).toBe(false);
    handles[100].end({ outcome: 'ok', status: 200 });
    expect(log.recent()[0]).toMatchObject({ traceId: 't100', outcome: 'ok' });
  });

  it('turns a pending request slow at 3 s, and keeps it slow once it ends', () => {
    const log = TestBed.inject(TmRequestLog);
    const handle = log.begin(start('a1'));
    vi.advanceTimersByTime(2_999);
    expect(log.recent()[0].slow).toBe(false);
    vi.advanceTimersByTime(1);
    expect(log.recent()[0]).toMatchObject({ slow: true, outcome: 'pending', durationMs: null });

    vi.advanceTimersByTime(1_810);
    handle.end({ outcome: 'ok', status: 200 });
    expect(log.recent()[0]).toMatchObject({ slow: true, outcome: 'ok', durationMs: 4_810 });
  });

  it('marks a request that ended at 3 s slow, and one that ended before it never', () => {
    const log = TestBed.inject(TmRequestLog);
    const quick = log.begin(start('quick'));
    vi.advanceTimersByTime(100);
    quick.end({ outcome: 'ok', status: 200 });
    vi.advanceTimersByTime(10_000);
    expect(log.recent()[0]).toMatchObject({ traceId: 'quick', slow: false, durationMs: 100 });

    // A clock that jumps past the timer still counts the duration.
    const jumped = log.begin(start('jumped'));
    vi.setSystemTime(Date.now() + 3_000);
    jumped.end({ outcome: 'cancelled' });
    expect(log.recent()[0]).toMatchObject({ traceId: 'jumped', slow: true, outcome: 'cancelled' });
  });

  it('ignores a second end', () => {
    const log = TestBed.inject(TmRequestLog);
    const handle = log.begin(start('a1'));
    handle.end({ outcome: 'ok', status: 200, serverBuild: '1.4.0' });
    vi.advanceTimersByTime(500);
    handle.end({ outcome: 'error', status: 500, serverBuild: '9.9.9' });
    expect(log.recent()[0]).toMatchObject({ outcome: 'ok', status: 200, durationMs: 0 });
    expect(TestBed.inject(TmAppVersion).server()).toBe('1.4.0');
  });

  it('hands each answer\'s server build to the app version', () => {
    const log = TestBed.inject(TmRequestLog);
    const version = TestBed.inject(TmAppVersion);
    log.begin(start('a1')).end({ outcome: 'error' });
    expect(version.server()).toBeNull();
    log.begin(start('a2')).end({ outcome: 'ok', status: 200, serverBuild: '1.5.0+9e8d7c6' });
    expect(version.server()).toBe('1.5.0+9e8d7c6');
  });

  it('has no diagnostics text before the diagnostics load', () => {
    const log = TestBed.inject(TmRequestLog);
    expect(log.diagnosticsText()).toBe('');
    expect(log.diagnosticsText(3)).toBe('');
  });

  it('is one instance per root injector', () => {
    expect(TestBed.inject(TmRequestLog)).toBe(TestBed.inject(TmRequestLog));
  });
});
