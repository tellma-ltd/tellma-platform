// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmBusMessage, TmClientEvent, ɵTmSyncDoc } from '@tellma/core';
import type { TmFakeApiCall } from '@tellma/core-testing';

import { TmFakeApiHost } from '../../src/api/tm-api.testing.util';
import {
  TM_LEADER_T17,
  TM_LEADER_T18,
  tmAdvance,
  tmLeaderRig,
  tmPause,
  tmUntil,
  type TmFakeHub,
  type TmLeaderRig,
  type TmLeaderTab,
} from './tm-leader.testing.util';

// The leader's work: the reads and the sync it runs on election, the hub
// connection it follows the realtime flag with, the events it relays and acts
// on, the leader documents' coalesced reads, and the polls of degraded mode.
// One tab leads; a second one, where a test needs it, follows.

describe('TmLeadership', () => {
  let rig: TmLeaderRig;

  beforeEach(async () => {
    rig = await tmLeaderRig(() => new TmFakeApiHost());
    rig.api.respond('acme/jobs/summary', { running: false });
    rig.api.respond('acme/tasks/summary', { open: 0 });
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.spyOn(Math, 'random').mockReturnValue(0);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  /** The API's reads of a route so far. */
  const reads = (route: string): TmFakeApiCall[] => rig.api.calls().filter((call) => call.route === route);

  /** The triggers of a tab's refreshes so far. */
  const triggers = (tab: TmLeaderTab): string[] => tab.refresher.triggers();

  /** What a tab posted of one type. */
  const posted = <T extends TmBusMessage['type']>(tab: TmLeaderTab, type: T): Extract<TmBusMessage, { type: T }>[] =>
    tab.bus.posted.filter((message): message is Extract<TmBusMessage, { type: T }> => message.type === type);

  /** Opens a tab and waits until it leads. */
  const leader = async (options?: Parameters<TmLeaderRig['tab']>[0]): Promise<TmLeaderTab> => {
    const tab = rig.tab(options);
    await tmUntil(() => tab.leader.isLeader());
    return tab;
  };

  /** Opens a leader of a tenant with realtime on, and waits for its connection. */
  const connecting = async (): Promise<{ readonly tab: TmLeaderTab; readonly hub: TmFakeHub }> => {
    await rig.realtime(true);
    const tab = await leader();
    await tmUntil(() => tab.hubs.length === 1 && tab.hubs[0]!.starts === 1);
    return { tab, hub: tab.hubs[0]! };
  };

  /** A promise and its resolver, for an answer the test lands later. */
  const deferred = <T>(): { readonly promise: Promise<T>; readonly resolve: (value: T) => void } => {
    let resolve!: (value: T) => void;
    const promise = new Promise<T>((done) => (resolve = done));
    return { promise, resolve };
  };

  // --- Election ---

  describe('on election', () => {
    it('revalidates the tenant and reads every leader document in the background, stamped with the time it was sent', async () => {
      const sentAt = Date.now();
      const tab = await leader({ noHub: true });
      await tmUntil(() => rig.store.doc(TM_LEADER_T17, 'acme.tasks')() !== undefined);
      expect(tab.refresher.refreshes).toEqual([{ tenantId: 17, docs: ['tenant'], trigger: 'leader' }]);
      expect(reads('acme/jobs/summary').map((call) => call.options)).toEqual([expect.objectContaining({ background: true, tenantId: 17 })]);
      expect(rig.store.doc(TM_LEADER_T17, 'acme.jobs')()).toEqual({ tag: null, savedAt: sentAt, data: { running: false } });
      expect(posted(tab, 'hub.status')).toEqual([{ type: 'hub.status', tenantId: 17, status: 'connecting', since: sentAt, leaderVisible: true }]);
    });

    it('adds no sync of its own when the page already saw one, as after a cold entry', async () => {
      const sync: ɵTmSyncDoc = { at: Date.now(), entities: null, entityTags: {} };
      await rig.store.put(TM_LEADER_T17, 'sync', { tag: null, savedAt: Date.now(), data: sync });
      const tab = await leader({ noHub: true });
      await tmUntil(() => rig.store.doc(TM_LEADER_T17, 'acme.tasks')() !== undefined);
      expect(tab.refresher.refreshes).toEqual([]);
      expect(reads('acme/jobs/summary').length).toBe(1);
    });

    it.each([
      ['window', 'pagehide', 'pageshow'],
      ['document', 'freeze', 'resume'],
    ] as const)('revalidates the tenant on a return after %s %s, though the page saw the last sync', async (target, away, back) => {
      const sync: ɵTmSyncDoc = { at: Date.now(), entities: null, entityTags: {} };
      await rig.store.put(TM_LEADER_T17, 'sync', { tag: null, savedAt: Date.now(), data: sync });
      const tab = await leader({ noHub: true });
      expect(triggers(tab)).toEqual([]);
      tab.fire(target, away);
      await tmUntil(() => !tab.leader.isLeader());
      await tmAdvance(10_000);
      tab.fire(target, back);
      await tmUntil(() => tab.leader.isLeader());
      expect(triggers(tab)).toEqual(['leader']);
    });

    it('revalidates the tenant when the last sync is stamped ahead of the clock', async () => {
      const sync: ɵTmSyncDoc = { at: Date.now() + 3_600_000, entities: null, entityTags: {} };
      await rig.store.put(TM_LEADER_T17, 'sync', { tag: null, savedAt: Date.now(), data: sync });
      const tab = await leader({ noHub: true });
      expect(triggers(tab)).toEqual(['leader']);
    });

    it('stamps a read that lands late with its send time, so a later copy stored meanwhile wins', async () => {
      const answer = deferred<unknown>();
      rig.api.respond('acme/jobs/summary', () => answer.promise);
      const sentAt = Date.now();
      await leader({ noHub: true });
      await tmAdvance(3_000);
      // Another tab stored a copy it read after this one was sent.
      await rig.store.put(TM_LEADER_T17, 'acme.jobs', { tag: null, savedAt: sentAt + 1_000, data: { running: true } });
      answer.resolve({ running: false });
      await tmPause();
      expect(rig.store.doc(TM_LEADER_T17, 'acme.jobs')()).toEqual({ tag: null, savedAt: sentAt + 1_000, data: { running: true } });
    });

    it('reports its visibility to the other tabs', async () => {
      const tab = await leader({ noHub: true });
      tab.hide();
      expect(posted(tab, 'hub.status').at(-1)).toEqual(expect.objectContaining({ leaderVisible: false }));
      expect(tab.leader.hubStatus().leaderVisible).toBe(false);
    });
  });

  // --- The connection ---

  describe('connection', () => {
    it('connects when the tenant has realtime, and reports the states the connection enters', async () => {
      const { tab, hub } = await connecting();
      expect(hub.tenantId).toBe(17);
      hub.sink.state('connected');
      expect(tab.leader.hubStatus().state).toBe('connected');
      expect(posted(tab, 'hub.status').at(-1)).toEqual(expect.objectContaining({ status: 'connected' }));
    });

    it('waits for the tenant facts before it connects', async () => {
      const tab = await leader();
      await tmPause();
      expect(tab.hubs).toEqual([]);
      await rig.realtime(true);
      await tmUntil(() => tab.hubs.length === 1);
    });

    it('stops the connection when realtime goes off, and connects again when it comes back on', async () => {
      const { tab, hub } = await connecting();
      await rig.realtime(false);
      await tmUntil(() => tab.leader.hubStatus().state === 'disabled');
      expect(hub.stops).toBe(1);
      await rig.realtime(true);
      await tmUntil(() => tab.hubs.length === 2 && tab.hubs[1]!.starts === 1);
      expect(tab.leader.hubStatus().state).toBe('connecting');
      // The stopped connection's late reports are ignored.
      hub.sink.state('connected');
      expect(tab.leader.hubStatus().state).toBe('connecting');
    });

    it('catches up after every connect: revalidates the tenant and reads every leader document', async () => {
      const { tab, hub } = await connecting();
      const before = reads('acme/jobs/summary').length;
      hub.sink.state('connected');
      hub.sink.connected();
      await tmPause();
      expect(triggers(tab)).toEqual(['leader', 'leader']);
      expect(reads('acme/jobs/summary').length).toBe(before + 1);
      expect(reads('acme/tasks/summary').length).toBe(before + 1);
    });

    it('starts the connection again after a refusal once a sync lands, and not when none does', async () => {
      const { tab, hub } = await connecting();
      tab.refresher.onRefresh = (trigger) => {
        if (trigger !== 'terminal') {
          return Promise.resolve();
        }
        const sync: ɵTmSyncDoc = { at: Date.now(), entities: null, entityTags: {} };
        return rig.store.put(TM_LEADER_T17, 'sync', { tag: null, savedAt: Date.now(), data: sync });
      };
      hub.sink.refused(401);
      await tmUntil(() => hub.starts === 2);
      expect(triggers(tab).at(-1)).toBe('terminal');

      // A sync that fails stores nothing, and the connection stays stopped.
      tab.refresher.onRefresh = () => Promise.resolve();
      await tmAdvance(1_000);
      hub.sink.refused(403);
      await tmPause();
      expect(hub.starts).toBe(2);
    });

    it('stops the connection when the server ends the session or the access, and lets a sync decide', async () => {
      const { tab, hub } = await connecting();
      hub.sink.ended('access-revoked');
      await tmPause();
      expect(hub.stops).toBe(1);
      expect(triggers(tab).at(-1)).toBe('terminal');
    });

    it('stops the connection when the tab stops leading', async () => {
      const { tab, hub } = await connecting();
      await rig.realtime(true, TM_LEADER_T18);
      tab.refresher.partition.set(TM_LEADER_T18);
      await tmUntil(() => hub.stops === 1);
      // The tenant it enters next gets a connection of its own.
      await tmUntil(() => tab.hubs.length === 2);
      expect(tab.hubs[1]!.tenantId).toBe(18);
    });
  });

  // --- Events ---

  describe('events', () => {
    it('relays every event of its tenant, and delivers the ones its build knows', async () => {
      const { tab, hub } = await connecting();
      const heard: TmClientEvent[] = [];
      tab.leader.events('x.unknown').subscribe((event) => heard.push(event));
      tab.leader.events('acme.tasks.changed').subscribe((event) => heard.push(event));
      hub.sink.event({ name: 'x.unknown', tenantId: 17, payload: null });
      hub.sink.event({ name: 'acme.tasks.changed', tenantId: 17, payload: null });
      hub.sink.event({ name: 'acme.tasks.changed', tenantId: 18, payload: null });
      expect(posted(tab, 'hub.event').map((message) => message.event.name)).toEqual(['x.unknown', 'acme.tasks.changed']);
      expect(heard).toEqual([{ name: 'acme.tasks.changed', tenantId: 17, payload: null }]);
    });

    it('hands a cache change to the refresher, and drops one whose payload is malformed', async () => {
      const { tab, hub } = await connecting();
      hub.sink.event({ name: 'cache.changed', tenantId: 17, payload: { tag: 'settings', value: '1.43' } });
      hub.sink.event({ name: 'cache.changed', tenantId: 17, payload: { value: 4 } });
      expect(tab.refresher.cacheChanges).toEqual([{ tenantId: 17, event: { tag: 'settings', value: '1.43' } }]);
    });

    it('coalesces a leader document events over 250 ms into one read, with one more queued behind a read in flight', async () => {
      const { hub } = await connecting();
      const before = reads('acme/jobs/summary').length;
      const answer = deferred<unknown>();
      rig.api.respond('acme/jobs/summary', () => answer.promise);

      hub.sink.event({ name: 'acme.jobs.changed', tenantId: 17, payload: null });
      await tmAdvance(100);
      hub.sink.event({ name: 'job.changed', tenantId: 17, payload: null });
      await tmAdvance(149);
      expect(reads('acme/jobs/summary').length).toBe(before);
      await tmAdvance(1);
      expect(reads('acme/jobs/summary').length).toBe(before + 1);
      expect(reads('acme/tasks/summary').length).toBe(before);

      // While that read is in flight, any number of events queue one more.
      for (let i = 0; i < 5; i++) {
        hub.sink.event({ name: 'acme.jobs.changed', tenantId: 17, payload: null });
        await tmAdvance(300);
      }
      expect(reads('acme/jobs/summary').length).toBe(before + 1);
      rig.api.respond('acme/jobs/summary', { running: false });
      answer.resolve({ running: false });
      await tmUntil(() => reads('acme/jobs/summary').length === before + 2);
      await tmPause();
      expect(reads('acme/jobs/summary').length).toBe(before + 2);
    });
  });

  // --- Degraded mode ---

  describe('polling', () => {
    it('polls every minute while realtime is off and the leader is visible', async () => {
      await rig.realtime(false);
      const tab = await leader();
      await tmUntil(() => tab.leader.hubStatus().state === 'disabled');
      expect(tab.events('tm.realtime.polling')).toEqual([{ cause: 'disabled' }]);
      await tmAdvance(59_000);
      expect(triggers(tab)).toEqual(['leader']);
      await tmAdvance(1_000);
      expect(triggers(tab)).toEqual(['leader', 'poll']);
      expect(reads('acme/jobs/summary').length).toBe(2);
      expect(reads('acme/tasks/summary').length).toBe(2);
      await tmAdvance(60_000);
      expect(triggers(tab)).toEqual(['leader', 'poll', 'poll']);

      // Hidden, it stops; shown again, it polls what is due.
      tab.hide();
      await tmAdvance(180_000);
      expect(triggers(tab)).toEqual(['leader', 'poll', 'poll']);
      tab.show();
      expect(triggers(tab)).toEqual(['leader', 'poll', 'poll', 'poll']);
      expect(tab.events('tm.realtime.polling')).toEqual([{ cause: 'disabled' }]);
    });

    it('polls for a visible follower while the leader is hidden, at most once a minute', async () => {
      await rig.realtime(false);
      const a = await leader({ hidden: true });
      const b = rig.tab();
      await tmUntil(() => b.leader.hubStatus().state === 'disabled');
      // The follower's first probe came within the leader's first minute: no poll yet.
      expect(triggers(a)).toEqual(['leader']);
      await tmAdvance(60_000);
      await tmPause();
      expect(triggers(a)).toEqual(['leader', 'poll']);
      // A probe on showing the follower again, within the minute, polls nothing.
      b.hide();
      b.show();
      await tmPause();
      expect(triggers(a)).toEqual(['leader', 'poll']);
    });

    it('polls once the hub has not connected for a minute, and stops once it connects', async () => {
      const { tab, hub } = await connecting();
      await tmAdvance(59_999);
      expect(tab.events('tm.realtime.polling')).toEqual([]);
      await tmAdvance(1);
      expect(tab.events('tm.realtime.polling')).toEqual([{ cause: 'hub-down' }]);
      expect(triggers(tab)).toEqual(['leader', 'poll']);

      hub.sink.state('connected');
      await tmAdvance(180_000);
      expect(triggers(tab)).toEqual(['leader', 'poll']);

      // A drop starts the minute again.
      hub.sink.state('reconnecting');
      await tmAdvance(59_000);
      expect(triggers(tab)).toEqual(['leader', 'poll']);
      await tmAdvance(1_000);
      expect(triggers(tab)).toEqual(['leader', 'poll', 'poll']);
      expect(tab.events('tm.realtime.polling')).toEqual([{ cause: 'hub-down' }, { cause: 'hub-down' }]);
    });

    it('polls with no realtime entry point once its minute is up', async () => {
      await rig.realtime(true);
      const tab = await leader({ noHub: true });
      await tmAdvance(60_000);
      expect(tab.events('tm.realtime.polling')).toEqual([{ cause: 'hub-down' }]);
      expect(tab.leader.hubStatus().state).toBe('connecting');
    });

    it('re-reads a leader document whose stored copy starts to ask for it while connected', async () => {
      const { hub } = await connecting();
      await tmUntil(() => rig.store.doc(TM_LEADER_T17, 'acme.jobs')() !== undefined);
      hub.sink.state('connected');
      await tmAdvance(30_000);
      // A panel stores a copy that asks for polling: the leader re-reads it a minute after its last read.
      await rig.store.put(TM_LEADER_T17, 'acme.jobs', { tag: null, savedAt: Date.now(), data: { running: true } });
      await tmAdvance(29_000);
      expect(reads('acme/jobs/summary').length).toBe(1);
      await tmAdvance(1_000);
      await tmPause();
      expect(reads('acme/jobs/summary').length).toBe(2);
    });

    it('re-reads a leader document whose own read starts to ask for it while connected', async () => {
      const { hub } = await connecting();
      await tmUntil(() => rig.store.doc(TM_LEADER_T17, 'acme.jobs')() !== undefined);
      hub.sink.state('connected');
      // A job starts: its event's read stores a copy that asks, and its later events are lost.
      rig.api.respond('acme/jobs/summary', { running: true });
      hub.sink.event({ name: 'job.changed', tenantId: 17, payload: null });
      await tmAdvance(250);
      await tmUntil(() => rig.store.doc<{ running: boolean }>(TM_LEADER_T17, 'acme.jobs')()?.data.running === true);
      expect(reads('acme/jobs/summary').length).toBe(2);
      await tmAdvance(60_000);
      await tmPause();
      expect(reads('acme/jobs/summary').length).toBe(3);
    });

    it('re-reads a leader document every minute while its stored copy asks for it, even while connected', async () => {
      rig.api.respond('acme/jobs/summary', { running: true });
      const { tab, hub } = await connecting();
      await tmUntil(() => rig.store.doc(TM_LEADER_T17, 'acme.jobs')() !== undefined);
      hub.sink.state('connected');
      rig.api.respond('acme/jobs/summary', { running: false });
      await tmAdvance(60_000);
      await tmPause();
      expect(reads('acme/jobs/summary').length).toBe(2);
      expect(reads('acme/tasks/summary').length).toBe(1);
      // The copy no longer asks for it.
      await tmAdvance(180_000);
      expect(reads('acme/jobs/summary').length).toBe(2);
      expect(triggers(tab)).toEqual(['leader']);
      expect(tab.events('tm.realtime.polling')).toEqual([]);
    });
  });
});
