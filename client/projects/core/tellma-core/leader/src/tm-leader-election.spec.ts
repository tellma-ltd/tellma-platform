// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmClientEvent } from '@tellma/core';

import { TmFakeApiHost } from '../../src/api/tm-api.testing.util';
import { tmDevModeProvider } from '../../src/app/tm-dev-mode.testing.util';
import {
  TM_JOBS_DOC,
  TM_LEADER_LOCK,
  TM_LEADER_T17,
  TM_LEADER_T18,
  TM_TASKS_DOC,
  tmAdvance,
  tmLeaderRig,
  tmPause,
  tmUntil,
  type TmLeaderRig,
  type TmLeaderTab,
} from './tm-leader.testing.util';

// The election between tabs of one browser: real Web Locks, one iframe per
// tab, and a linked tab bus. Lock grants cross to the browser and back in
// real time (tmUntil, tmPause), while the election's own waits run on faked
// timers (tmAdvance).

/**
 * A lock manager the test grants by hand, for a race the browser's own one
 * cannot be made to show on demand. The holder it reports is another tab.
 */
class TmScriptedLocks {
  /** Every request, oldest first, with what the test may do to it. */
  readonly requests: {
    readonly options: LockOptions;
    /** Grants the lock: the callback runs, and the request settles with what it returns. */
    readonly grant: () => void;
    /** Takes a granted lock away, as another request's steal does. */
    readonly steal: () => void;
  }[] = [];

  /** Reports the hub lock as held by another tab. */
  query(): Promise<LockManagerSnapshot> {
    return Promise.resolve({ held: [{ name: TM_LEADER_LOCK, mode: 'exclusive', clientId: 'other-tab' }], pending: [] });
  }

  /** Records a request, which the test settles. */
  request(name: string, options: LockOptions, callback: (lock: Lock | null) => unknown): Promise<unknown> {
    return new Promise((resolve, reject) => {
      this.requests.push({
        options,
        grant: () => void Promise.resolve(callback({ name, mode: 'exclusive' } as Lock)).then(resolve, reject),
        steal: () => reject(new DOMException('The lock was stolen.', 'AbortError')),
      });
    });
  }
}

describe('TmLeaderElection', () => {
  let rig: TmLeaderRig;

  beforeEach(async () => {
    rig = await tmLeaderRig(() => new TmFakeApiHost());
    rig.api.respond('acme/jobs/summary', { running: false });
    rig.api.respond('acme/tasks/summary', { open: 0 });
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    // No jitter before a steal, unless a test asks for one.
    vi.spyOn(Math, 'random').mockReturnValue(0);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  /** How many probes a tab posted. */
  const probes = (tab: TmLeaderTab): number => tab.bus.posted.filter((message) => message.type === 'leader.probe').length;

  /** Opens a tab and waits until it leads. */
  const leader = async (options?: Parameters<TmLeaderRig['tab']>[0]): Promise<TmLeaderTab> => {
    const tab = rig.tab(options);
    await tmUntil(() => tab.leader.isLeader());
    return tab;
  };

  /** Opens a tab, which follows: it probes the leader and mirrors its report. */
  const follower = async (options?: Parameters<TmLeaderRig['tab']>[0]): Promise<TmLeaderTab> => {
    const tab = rig.tab(options);
    await tmUntil(() => tab.bus.posted.some((message) => message.type === 'leader.probe'));
    await tmPause();
    expect(tab.leader.isLeader()).toBe(false);
    return tab;
  };

  // --- The grant and every release ---

  describe('grant and release', () => {
    it('grants the lock to the first tab of the tenant, which leads, and the next tab follows and mirrors its report', async () => {
      const a = await leader({ noHub: true });
      expect(a.events('tm.realtime.leader-acquired')).toEqual([{ how: 'granted' }]);
      expect(a.leader.hubStatus()).toEqual(expect.objectContaining({ state: 'connecting', leaderVisible: true }));
      const held = await a.frame.contentWindow!.navigator.locks.query();
      expect(held.held?.map((lock) => lock.name)).toEqual([TM_LEADER_LOCK]);

      const b = await follower();
      await tmUntil(() => b.leader.hubStatus().since === a.leader.hubStatus().since);
      expect(b.leader.hubStatus()).toEqual(a.leader.hubStatus());
      expect(b.events('tm.realtime.leader-acquired')).toEqual([]);
    });

    it('hands the lock to the queued tab when the leader leaves the tenant, and leads the tenant it enters', async () => {
      const a = await leader();
      const b = await follower();
      a.refresher.partition.set(TM_LEADER_T18);
      await tmUntil(() => b.leader.isLeader() && a.leader.isLeader());
      expect(a.events('tm.realtime.leader-released')).toEqual([{ why: 'tenant' }]);
      expect(b.events('tm.realtime.leader-acquired')).toEqual([{ how: 'granted' }]);
      const held = await b.frame.contentWindow!.navigator.locks.query();
      expect(held.held?.map((lock) => lock.name).sort()).toEqual([TM_LEADER_LOCK, 'tm:acme:test-user:18:hub']);
    });

    it('releases on pagehide and queues again on pageshow', async () => {
      const a = await leader();
      const b = await follower();
      a.fire('window', 'pagehide');
      await tmUntil(() => b.leader.isLeader());
      expect(a.leader.isLeader()).toBe(false);
      expect(a.events('tm.realtime.leader-released')).toEqual([{ why: 'pagehide' }]);

      // Back from the back-forward cache: a queues behind b, and takes over when b closes.
      a.fire('window', 'pageshow');
      await tmPause();
      expect(a.leader.isLeader()).toBe(false);
      b.close();
      await tmUntil(() => a.leader.isLeader());
    });

    it('releases on freeze and queues again on resume', async () => {
      const a = await leader();
      const b = await follower();
      a.fire('document', 'freeze');
      await tmUntil(() => b.leader.isLeader());
      expect(a.events('tm.realtime.leader-released')).toEqual([{ why: 'freeze' }]);
      a.fire('document', 'resume');
      b.close();
      await tmUntil(() => a.leader.isLeader());
    });

    it('releases on leaving for no tenant, as leaving the tenant', async () => {
      const a = await leader();
      a.refresher.partition.set(null);
      await tmUntil(() => !a.leader.isLeader());
      expect(a.events('tm.realtime.leader-released')).toEqual([{ why: 'tenant' }]);
      const held = await a.frame.contentWindow!.navigator.locks.query();
      expect(held.held).toEqual([]);
    });

    it('releases when the session is no longer active', async () => {
      const a = await leader();
      rig.session.setState('expired');
      await tmUntil(() => !a.leader.isLeader());
      expect(a.events('tm.realtime.leader-released')).toEqual([{ why: 'session' }]);
      const held = await a.frame.contentWindow!.navigator.locks.query();
      expect(held.held).toEqual([]);
      expect(held.pending).toEqual([]);
    });

    it('releases when another tab steals the lock, and queues again behind the thief', async () => {
      const a = await leader();
      const thief = document.createElement('iframe');
      document.body.appendChild(thief);
      try {
        let stolen!: () => void;
        void thief.contentWindow!.navigator.locks.request(TM_LEADER_LOCK, { steal: true }, () => new Promise<void>((resolve) => (stolen = resolve)));
        await tmUntil(() => !a.leader.isLeader());
        expect(a.events('tm.realtime.leader-released')).toEqual([{ why: 'stolen' }]);
        stolen();
        await tmUntil(() => a.leader.isLeader());
      } finally {
        thief.remove();
      }
    });

    it('hands the lock over when the leader crashes', async () => {
      const a = await leader();
      const b = await follower();
      a.crash();
      await tmUntil(() => b.leader.isLeader());
      expect(b.events('tm.realtime.leader-acquired')).toEqual([{ how: 'granted' }]);
    });

    it('aborts a queued request on freeze, and requests again on resume', async () => {
      const a = await leader();
      const b = await follower();
      b.fire('document', 'freeze');
      await tmPause();
      a.close();
      await tmPause(150);
      expect(b.leader.isLeader()).toBe(false);
      const free = await b.frame.contentWindow!.navigator.locks.query();
      expect(free.held).toEqual([]);
      expect(free.pending).toEqual([]);

      b.fire('document', 'resume');
      await tmUntil(() => b.leader.isLeader());
    });

    it('keeps one leadership when its steal races a grant of its own queued request', async () => {
      const locks = new TmScriptedLocks();
      const b = rig.tab({ locks: locks as unknown as LockManager, noHub: true });
      await tmUntil(() => locks.requests.length === 1);
      // Nobody answers the probe: after 5 s and the jitter, the tab steals.
      await tmAdvance(5_000);
      await tmUntil(() => locks.requests.length === 2, { step: 1 });
      const [queued, steal] = locks.requests;
      expect(steal!.options.steal).toBe(true);

      // The holder let go just before the steal: the queued request was
      // granted first, and the steal then took the lock from it.
      queued!.grant();
      steal!.grant();
      queued!.steal();
      await tmUntil(() => locks.requests.length === 3);
      locks.requests[2]!.grant();
      await tmUntil(() => b.leader.isLeader());
      expect(b.events('tm.realtime.leader-acquired')).toEqual([{ how: 'granted' }, { how: 'granted' }]);
      expect(b.events('tm.realtime.leader-released')).toEqual([{ why: 'stolen' }]);

      // One leadership is left, so one poll a minute.
      await tmAdvance(60_000);
      expect(b.refresher.triggers().filter((trigger) => trigger === 'poll')).toEqual(['poll']);
    });

    it('leads alone where the page has no Web Locks', async () => {
      const a = await leader({ noLocks: true });
      const b = await leader({ noLocks: true });
      expect([a.leader.isLeader(), b.leader.isLeader()]).toEqual([true, true]);
    });
  });

  // --- Stealing: only while visible, from a hung leader or a hidden disconnected one ---

  describe('steal', () => {
    it('steals from a leader that does not answer a probe within 5 s', async () => {
      const a = await leader();
      a.bus.close();
      const b = await follower();
      await tmAdvance(4_900);
      await tmPause();
      expect(b.leader.isLeader()).toBe(false);
      await tmAdvance(100);
      // The jitter's timer, set while the clock ticks, falls due a millisecond later.
      await tmUntil(() => b.leader.isLeader(), { step: 1 });
      expect(b.events('tm.realtime.leader-acquired')).toEqual([{ how: 'stolen-hung' }]);
      await tmUntil(() => !a.leader.isLeader());
      expect(a.events('tm.realtime.leader-released')).toEqual([{ why: 'stolen' }]);
    });

    it('never steals while hidden, however long the leader stays silent', async () => {
      await leader().then((a) => a.bus.close());
      const b = await follower({ hidden: true });
      await tmAdvance(120_000);
      await tmPause();
      expect(b.leader.isLeader()).toBe(false);
    });

    it('steals from a hidden leader whose hub has been down for 30 s', async () => {
      const a = await leader({ hidden: true, noHub: true });
      const b = await follower();
      await tmUntil(() => b.leader.hubStatus().leaderVisible === false);
      await tmAdvance(29_000);
      await tmPause();
      expect(b.leader.isLeader()).toBe(false);
      await tmAdvance(1_100);
      await tmUntil(() => b.leader.isLeader(), { step: 1 });
      expect(b.events('tm.realtime.leader-acquired')).toEqual([{ how: 'stolen-disconnected' }]);
      await tmUntil(() => a.events('tm.realtime.leader-released').length === 1);
    });

    it('never steals from a visible leader, however long its hub is down', async () => {
      await leader({ noHub: true });
      const b = await follower();
      await tmAdvance(180_000);
      await tmPause();
      expect(b.leader.isLeader()).toBe(false);
      expect(b.leader.hubStatus().state).toBe('connecting');
    });

    it('never steals from a hidden leader whose realtime is off', async () => {
      await rig.realtime(false);
      const a = await leader({ hidden: true });
      await tmUntil(() => a.leader.hubStatus().state === 'disabled');
      const b = await follower();
      await tmUntil(() => b.leader.hubStatus().state === 'disabled');
      await tmAdvance(180_000);
      await tmPause();
      expect(b.leader.isLeader()).toBe(false);
    });

    it('probes again instead of stealing when the holder changed during the jitter', async () => {
      vi.mocked(Math.random).mockReturnValue(0.99);
      const a = await leader();
      a.bus.close();
      // c queues first, hidden so it never steals, and silent once it leads.
      const c = await follower({ hidden: true });
      c.bus.close();
      const b = await follower();
      expect(probes(b)).toBe(1);
      await tmAdvance(5_000);
      await tmPause();
      // The leader crashes during b's jitter, and the lock goes to c.
      a.crash();
      await tmUntil(() => c.leader.isLeader());
      await tmAdvance(2_000);
      await tmUntil(() => probes(b) === 2);
      await tmPause();
      expect(b.leader.isLeader()).toBe(false);
      expect(c.leader.isLeader()).toBe(true);
      // The new probe goes unanswered too, so b steals from c in its turn,
      // once 5 s and the jitter have passed.
      await tmAdvance(5_000);
      await tmUntil(() => b.leader.isLeader(), { step: 50 });
    });
  });

  // --- The follower's probes, and what reaches it ---

  describe('follower', () => {
    it('probes every minute while visible and the hub is not connected, and not while hidden', async () => {
      await leader({ noHub: true });
      const b = await follower();
      expect(probes(b)).toBe(1);
      await tmAdvance(60_000);
      expect(probes(b)).toBe(2);
      b.hide();
      await tmAdvance(120_000);
      expect(probes(b)).toBe(2);
      // Shown again: a probe at once, then every minute.
      b.show();
      expect(probes(b)).toBe(3);
      await tmAdvance(60_000);
      expect(probes(b)).toBe(4);
    });

    it('does not probe while the hub is connected', async () => {
      await rig.realtime(true);
      const a = await leader();
      await tmUntil(() => a.hubs.length === 1);
      a.hubs[0]!.sink.state('connected');
      const b = await follower();
      await tmUntil(() => b.leader.hubStatus().state === 'connected');
      await tmAdvance(180_000);
      expect(probes(b)).toBe(1);
    });

    it('probes while connected when a leader document asks for polling', async () => {
      await rig.realtime(true);
      const a = await leader();
      await tmUntil(() => a.hubs.length === 1);
      a.hubs[0]!.sink.state('connected');
      const b = await follower();
      await rig.store.put(TM_LEADER_T17, 'acme.jobs', { tag: null, savedAt: Date.now(), data: { running: true } });
      await tmAdvance(60_000);
      expect(probes(b)).toBe(2);
    });

    it('delivers the events the leader relays, when its build knows them', async () => {
      await rig.realtime(true);
      const a = await leader();
      await tmUntil(() => a.hubs.length === 1);
      const b = await follower();
      const heard: TmClientEvent[] = [];
      b.leader.events('acme.jobs.changed').subscribe((event) => heard.push(event));
      b.leader.events('x.unknown').subscribe((event) => heard.push(event));
      a.hubs[0]!.sink.event({ name: 'x.unknown', tenantId: 17, payload: null });
      a.hubs[0]!.sink.event({ name: 'acme.jobs.changed', tenantId: 17, payload: { id: 4 } });
      await tmUntil(() => heard.length > 0);
      await tmPause();
      expect(heard).toEqual([{ name: 'acme.jobs.changed', tenantId: 17, payload: { id: 4 } }]);
    });

    it("syncs when the server ends the user's access to the tenant, and on no other event", async () => {
      await rig.realtime(true);
      const a = await leader();
      await tmUntil(() => a.hubs.length === 1);
      const b = await follower();
      const terminal = (): number => b.refresher.triggers().filter((trigger) => trigger === 'terminal').length;
      a.hubs[0]!.emit('acme.jobs.changed', { id: 4 });
      a.hubs[0]!.emit('session.ended', { reason: 'session-terminated' });
      a.hubs[0]!.emit('session.ended', { reason: 'access-revoked' }, 18);
      a.hubs[0]!.emit('session.ended', { reason: 'access-revoked' });
      await tmUntil(() => terminal() === 1);
      a.hubs[0]!.emit('session.ended', { reason: 'tenant-unavailable' });
      await tmUntil(() => terminal() === 2);
      await tmPause();
      expect(terminal()).toBe(2);
      expect(b.refresher.refreshes.filter((refresh) => refresh.trigger === 'terminal').map((refresh) => refresh.tenantId)).toEqual([17, 17]);
    });

    it('is answered by a hidden leader at once', async () => {
      const a = await leader({ hidden: true, noHub: true });
      const statuses = (): number => a.bus.posted.filter((message) => message.type === 'hub.status').length;
      const before = statuses();
      await follower();
      await tmUntil(() => statuses() === before + 1);
    });
  });
});

describe('TmLeaderElection with a leader document registered twice', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it("asks only a name's first registration whether to poll, in production", async () => {
    // The second registration of acme.tasks would ask for polling; production keeps the first, which never does.
    const twice = { ...TM_TASKS_DOC, route: 'acme/tasks/other', pollWhile: () => true };
    const rig = await tmLeaderRig(() => new TmFakeApiHost(), [TM_JOBS_DOC, TM_TASKS_DOC, twice], [tmDevModeProvider(false)]);
    rig.api.respond('acme/jobs/summary', { running: false });
    rig.api.respond('acme/tasks/summary', { open: 0 });
    rig.api.respond('acme/tasks/other', { open: 0 });
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.spyOn(Math, 'random').mockReturnValue(0);

    await rig.realtime(true);
    const a = rig.tab();
    await tmUntil(() => a.leader.isLeader() && a.hubs.length === 1);
    a.hubs[0]!.sink.state('connected');
    const b = rig.tab();
    await tmUntil(() => b.bus.posted.some((message) => message.type === 'leader.probe'));
    await tmPause();
    expect(b.leader.isLeader()).toBe(false);
    await rig.store.put(TM_LEADER_T17, 'acme.tasks', { tag: null, savedAt: Date.now(), data: { open: 1 } });

    await tmAdvance(60_000);
    expect(b.bus.posted.filter((message) => message.type === 'leader.probe')).toHaveLength(1);
  });
});
