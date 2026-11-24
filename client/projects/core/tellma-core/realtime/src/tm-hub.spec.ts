// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injector, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { TmClientEvent, TmHubSink, TmHubState } from '@tellma/core';
import { TmFakeTelemetry } from '@tellma/core-testing';

import { TmHub, tmHubDelay, tmHubEvent, type TmHubAttempt, type TmHubAttemptHandlers, type TmHubRefusal } from './tm-hub';

// The reconnect loop over a fake connection: the schedule and its jitter,
// the count that starts over after a stable minute, nudges and the offline
// wait, the terminal verdicts that stop it, the events it hands on or drops,
// the deadline of a hung attempt and the bounded stop.

/** One attempt the spec settles by hand. */
class TmFakeAttempt implements TmHubAttempt {
  starts = 0;
  stops = 0;
  refused: TmHubRefusal | null = null;
  /** What `stop()` returns: settled at once, unless the spec says otherwise. */
  stopResult: Promise<void> = Promise.resolve();
  #resolve!: () => void;
  #reject!: (error: Error) => void;
  readonly #started = new Promise<void>((resolve, reject) => {
    this.#resolve = resolve;
    this.#reject = reject;
  });

  constructor(readonly handlers: TmHubAttemptHandlers) {}

  start(): Promise<void> {
    this.starts++;
    return this.#started;
  }

  stop(): Promise<void> {
    this.stops++;
    return this.stopResult;
  }

  refusal(): TmHubRefusal | null {
    return this.refused;
  }

  /** The connection opens. */
  succeed(): void {
    this.#resolve();
  }

  /** The attempt fails, refused for good when a refusal is given. */
  fail(refusal: TmHubRefusal | null = null): void {
    this.refused = refusal;
    this.#reject(new Error('Failed to connect.'));
  }
}

describe('tmHubDelay', () => {
  it('waits for nothing before the first attempt, then 2 s, 10 s and 30 s, then doubling to 5 minutes', () => {
    const bases = [0, 2_000, 10_000, 30_000, 60_000, 120_000, 240_000, 300_000, 300_000];
    expect(bases.map((_, index) => tmHubDelay(index + 1, 0.5))).toEqual(bases);
    expect(tmHubDelay(40, 0.5)).toBe(300_000);
  });

  it('moves every wait but the first by at most a fifth either way', () => {
    for (let attempt = 1; attempt <= 12; attempt++) {
      const base = tmHubDelay(attempt, 0.5);
      for (const random of [0, 0.1, 0.25, 0.5, 0.75, 0.9, 0.999_999]) {
        const delay = tmHubDelay(attempt, random);
        expect(delay).toBeGreaterThanOrEqual(Math.floor(base * 0.8));
        expect(delay).toBeLessThanOrEqual(Math.ceil(base * 1.2));
      }
    }
    expect(tmHubDelay(1, 0)).toBe(0);
    expect(tmHubDelay(2, 0)).toBe(1_600);
    expect(tmHubDelay(2, 0.999_999)).toBe(2_400);
  });
});

describe('tmHubEvent', () => {
  it("reads an envelope's name, tenant and payload, and nothing else", () => {
    expect(tmHubEvent({ name: 'a.b', tenantId: 17, payload: { id: 4 }, extra: 1 })).toEqual({ name: 'a.b', tenantId: 17, payload: { id: 4 } });
    expect(tmHubEvent({ name: 'a.b', tenantId: 17 })).toEqual({ name: 'a.b', tenantId: 17, payload: null });
  });

  it('drops what is not an envelope', () => {
    for (const malformed of [null, undefined, 'a.b', 17, [], { tenantId: 17 }, { name: 5, tenantId: 17 }, { name: 'a', tenantId: '17' }, { name: 'a', tenantId: 1.5 }]) {
      expect(tmHubEvent(malformed)).toBeNull();
    }
  });
});

describe('TmHub', () => {
  let attempts: TmFakeAttempt[];
  let reports: string[];
  let events: TmClientEvent[];
  let telemetry: TmFakeTelemetry;
  let online: ReturnType<typeof signal<boolean>>;
  let random: number;
  let hub: TmHub;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    attempts = [];
    reports = [];
    events = [];
    telemetry = new TmFakeTelemetry();
    online = signal(true);
    random = 0.5;
    const sink: TmHubSink = {
      event: (event) => events.push(event),
      state: (state: Exclude<TmHubState, 'disabled'>) => reports.push(state),
      connected: () => reports.push('sink.connected'),
      refused: (status) => reports.push(`refused ${status}`),
      ended: (reason) => reports.push(`ended ${reason}`),
    };
    hub = new TmHub(sink, {
      connect: (handlers) => {
        const attempt = new TmFakeAttempt(handlers);
        attempts.push(attempt);
        return attempt;
      },
      online,
      telemetry,
      now: () => Date.now(),
      random: () => random,
      injector: TestBed.inject(Injector),
    });
  });

  afterEach(async () => {
    await hub.stop();
    vi.useRealTimers();
  });

  /** Lets the timers due within `ms` run, with the promises they settle. */
  const advance = (ms: number): Promise<unknown> => vi.advanceTimersByTimeAsync(ms);

  /** The latest attempt. */
  const latest = (): TmFakeAttempt => attempts.at(-1)!;

  /** The props of the telemetry items of one name. */
  const items = (name: string): unknown[] => telemetry.recent().filter((item) => item.name === name).map((item) => item.props);

  /** Waits just short of the next attempt, finds none, then waits for it. */
  const expectNextAfter = async (ms: number): Promise<void> => {
    const count = attempts.length;
    await advance(ms - 1);
    expect(attempts).toHaveLength(count);
    await advance(1);
    expect(attempts).toHaveLength(count + 1);
  };

  // --- The schedule ---

  describe('schedule', () => {
    it('connects at once, then retries after 2 s, 10 s, 30 s and doubling waits up to 5 minutes', async () => {
      hub.start();
      await advance(0);
      expect(attempts).toHaveLength(1);
      expect(latest().starts).toBe(1);
      for (const wait of [2_000, 10_000, 30_000, 60_000, 120_000, 240_000, 300_000, 300_000]) {
        latest().fail();
        await expectNextAfter(wait);
      }
      // A failure is never terminal: the loop goes on, and reports `connecting` once.
      expect(reports).toEqual(['connecting']);
    });

    it('jitters each retry by the random number', async () => {
      random = 0;
      hub.start();
      await advance(0);
      latest().fail();
      await expectNextAfter(1_600);
      random = 0.999_999;
      latest().fail();
      await expectNextAfter(12_000);
    });

    it('reports the connection, its timing by attempt, and `reconnecting` once it drops', async () => {
      hub.start();
      await advance(0);
      latest().fail();
      await advance(2_000);
      await advance(500);
      latest().succeed();
      await advance(0);
      expect(reports).toEqual(['connecting', 'connected', 'sink.connected']);
      expect(items('tm.realtime.connected')).toEqual([{ attempt: '2-4', ms: 500 }]);

      latest().handlers.closed(new Error('Connection lost.'));
      expect(reports.at(-1)).toBe('reconnecting');
      expect(items('tm.realtime.disconnected')).toEqual([{ reason: 'error' }]);
      // A connection that lasted less than a minute does not start the count over.
      await expectNextAfter(10_000);
    });

    it('starts the count over after a connection that lasted a minute', async () => {
      hub.start();
      await advance(0);
      latest().fail();
      await advance(2_000);
      latest().fail();
      await advance(10_000);
      latest().succeed();
      await advance(60_000);
      latest().handlers.closed(undefined);
      expect(items('tm.realtime.disconnected')).toEqual([{ reason: 'closed' }]);
      // The next attempt is the first again: it waits for nothing.
      await advance(0);
      expect(attempts).toHaveLength(4);
      latest().fail();
      await expectNextAfter(2_000);
      latest().succeed();
      await advance(0);
      expect(items('tm.realtime.connected').map((props) => (props as { attempt: string }).attempt)).toEqual(['2-4', '2-4']);
    });

    it('counts the fifth attempt and later as such', async () => {
      hub.start();
      await advance(0);
      for (const wait of [2_000, 10_000, 30_000, 60_000]) {
        latest().fail();
        await advance(wait);
      }
      latest().succeed();
      await advance(0);
      expect(items('tm.realtime.connected')).toEqual([{ attempt: '5+', ms: 0 }]);
    });
  });

  // --- Nudges and the offline wait ---

  describe('nudges', () => {
    it('cut a wait short', async () => {
      hub.start();
      await advance(0);
      latest().fail();
      await advance(2_000);
      latest().fail();
      await advance(3_000);
      expect(attempts).toHaveLength(2);
      hub.nudge();
      expect(attempts).toHaveLength(3);
      // The count goes on: the next wait is the fourth attempt's.
      latest().fail();
      await expectNextAfter(30_000);
    });

    it('do nothing while an attempt runs or the connection is open', async () => {
      hub.start();
      await advance(0);
      hub.nudge();
      expect(attempts).toHaveLength(1);
      latest().succeed();
      await advance(0);
      hub.nudge();
      expect(attempts).toHaveLength(1);
    });

    it('do nothing before the start or after the stop', async () => {
      hub.nudge();
      expect(attempts).toHaveLength(0);
      hub.start();
      await advance(0);
      latest().fail();
      await hub.stop();
      hub.nudge();
      await advance(600_000);
      expect(attempts).toHaveLength(1);
    });

    it('come when the server becomes reachable, and the loop waits in `offline` until then', async () => {
      online.set(false);
      hub.start();
      await advance(0);
      expect(attempts).toHaveLength(0);
      expect(reports).toEqual(['offline']);
      // A nudge while unreachable keeps waiting.
      hub.nudge();
      await advance(600_000);
      expect(attempts).toHaveLength(0);
      online.set(true);
      TestBed.tick();
      expect(attempts).toHaveLength(1);
      expect(reports).toEqual(['offline', 'connecting']);
    });

    it('come when the server becomes reachable during a wait', async () => {
      hub.start();
      await advance(0);
      latest().fail();
      await advance(0);
      online.set(false);
      TestBed.tick();
      await advance(2_000);
      expect(reports.at(-1)).toBe('offline');
      online.set(true);
      TestBed.tick();
      expect(attempts).toHaveLength(2);
    });

    it('report a connection that dropped while unreachable as offline', async () => {
      hub.start();
      await advance(0);
      latest().succeed();
      await advance(0);
      online.set(false);
      latest().handlers.closed(new Error('Connection lost.'));
      expect(items('tm.realtime.disconnected')).toEqual([{ reason: 'offline' }]);
    });
  });

  // --- Terminal verdicts ---

  describe('terminal verdicts', () => {
    for (const status of [401, 403, 404] as const) {
      it(`stop the loop on a negotiate refused with ${status}`, async () => {
        hub.start();
        await advance(0);
        latest().fail(status);
        await advance(0);
        expect(reports).toEqual(['connecting', `refused ${status}`]);
        expect(items('tm.realtime.refused')).toEqual([{ status: `${status}` }]);
        await advance(3_600_000);
        hub.nudge();
        expect(attempts).toHaveLength(1);
      });
    }

    it('stop the loop on a user check that failed, which the leader hears as 401', async () => {
      hub.start();
      await advance(0);
      latest().fail('user');
      await advance(0);
      expect(reports.at(-1)).toBe('refused 401');
      expect(items('tm.realtime.refused')).toEqual([{ status: 'user-check' }]);
      await advance(3_600_000);
      expect(attempts).toHaveLength(1);
    });

    it('stop the loop when an open connection closes refused', async () => {
      hub.start();
      await advance(0);
      latest().succeed();
      await advance(0);
      latest().refused = 'user';
      latest().handlers.closed(new Error('The session no longer belongs to the user who connected.'));
      expect(reports.at(-1)).toBe('refused 401');
      await advance(3_600_000);
      expect(attempts).toHaveLength(1);
    });

    it('keep the attempt count through a refusal and the start that follows a sync', async () => {
      hub.start();
      await advance(0);
      for (const wait of [2_000, 10_000, 30_000]) {
        latest().fail();
        await advance(wait);
      }
      latest().fail(403);
      await advance(0);
      expect(attempts).toHaveLength(4);
      // The leader's sync succeeded: the loop starts again, waiting as the fifth attempt.
      hub.start();
      await expectNextAfter(60_000);
      expect(reports).toEqual(['connecting', 'refused 403', 'connecting']);
    });

    it("stop the loop when the server ends the user's access, after handing the event on", async () => {
      hub.start();
      await advance(0);
      latest().succeed();
      await advance(0);
      latest().handlers.event({ name: 'session.ended', tenantId: 17, payload: { reason: 'access-revoked' } });
      expect(events).toEqual([{ name: 'session.ended', tenantId: 17, payload: { reason: 'access-revoked' } }]);
      expect(reports.slice(-2)).toEqual(['reconnecting', 'ended access-revoked']);
      expect(latest().stops).toBe(1);
      expect(items('tm.realtime.disconnected')).toEqual([{ reason: 'ended' }]);
      // The close that follows is the stopped connection's, and is ignored.
      latest().handlers.closed(undefined);
      await advance(3_600_000);
      expect(attempts).toHaveLength(1);
      expect(items('tm.realtime.disconnected')).toHaveLength(1);
    });

    it('stop the loop when the server ends the session', async () => {
      hub.start();
      await advance(0);
      latest().succeed();
      await advance(0);
      latest().handlers.event({ name: 'session.ended', tenantId: 17, payload: { reason: 'session-terminated' } });
      expect(reports.at(-1)).toBe('ended session-terminated');
      await advance(3_600_000);
      expect(attempts).toHaveLength(1);
    });

    it('only hand on a session end whose reason this build does not know', async () => {
      hub.start();
      await advance(0);
      latest().succeed();
      await advance(0);
      latest().handlers.event({ name: 'session.ended', tenantId: 17, payload: { reason: 'later-reason' } });
      latest().handlers.event({ name: 'session.ended', tenantId: 17, payload: null });
      expect(events).toHaveLength(2);
      expect(reports.at(-1)).toBe('sink.connected');
      expect(latest().stops).toBe(0);
    });
  });

  // --- Events ---

  describe('events', () => {
    it('hands on the well-formed ones and drops the rest', async () => {
      hub.start();
      await advance(0);
      latest().succeed();
      await advance(0);
      latest().handlers.event({ name: 'acme.jobs.changed', tenantId: 17, payload: { id: 4 } });
      latest().handlers.event({ name: 'acme.jobs.changed', tenantId: '17' });
      latest().handlers.event('acme.jobs.changed');
      latest().handlers.event({ name: 'cache.changed', tenantId: 18, payload: { tag: 'settings' } });
      // Relaying, and dropping another tenant's events, is the leader's.
      expect(events).toEqual([
        { name: 'acme.jobs.changed', tenantId: 17, payload: { id: 4 } },
        { name: 'cache.changed', tenantId: 18, payload: { tag: 'settings' } },
      ]);
    });

    it("ignores a stopped connection's events", async () => {
      hub.start();
      await advance(0);
      latest().succeed();
      await advance(0);
      const stopped = latest();
      await hub.stop();
      stopped.handlers.event({ name: 'acme.jobs.changed', tenantId: 17, payload: null });
      expect(events).toEqual([]);
    });
  });

  // --- A hung attempt, and the stop ---

  describe('hung attempts and the stop', () => {
    it('abandon an attempt that has not connected in 100 s, and ignore its late outcome', async () => {
      hub.start();
      await advance(0);
      const hung = latest();
      await advance(100_000);
      expect(hung.stops).toBe(1);
      await expectNextAfter(2_000);
      hung.succeed();
      await advance(0);
      expect(reports).toEqual(['connecting']);
    });

    it('stop the loop and close the connection, once, however often asked', async () => {
      hub.start();
      await advance(0);
      latest().succeed();
      await advance(0);
      const first = hub.stop();
      const second = hub.stop();
      expect(second).toBe(first);
      await first;
      expect(latest().stops).toBe(1);
      await advance(3_600_000);
      expect(attempts).toHaveLength(1);
    });

    it('settle within 5 s when the connection never closes', async () => {
      hub.start();
      await advance(0);
      latest().stopResult = new Promise<void>(() => undefined);
      let settled = false;
      void hub.stop().then(() => (settled = true));
      await advance(4_999);
      expect(settled).toBe(false);
      await advance(1);
      expect(settled).toBe(true);
    });

    it('stop a wait before its attempt', async () => {
      hub.start();
      await advance(0);
      latest().fail();
      await advance(0);
      await hub.stop();
      await advance(3_600_000);
      expect(attempts).toHaveLength(1);
    });

    it('start again after a stop, keeping the count', async () => {
      hub.start();
      await advance(0);
      latest().fail();
      await advance(2_000);
      await hub.stop();
      hub.start();
      await expectNextAfter(10_000);
      // Starting a running loop does nothing.
      hub.start();
      await advance(0);
      expect(attempts).toHaveLength(3);
    });
  });
});
