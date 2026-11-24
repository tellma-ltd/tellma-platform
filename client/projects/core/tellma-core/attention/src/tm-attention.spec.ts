// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { type TmInboxRunningJob, TmLeader, type TmReadOptions } from '@tellma/core';
import { tmFakeAssignmentsSummary, tmFakeInboxSummary, type TmFakeApiCall } from '@tellma/core-testing';
import { TmToasts } from '@tellma/core-ui/toast';

import { TmAttention } from './tm-attention';
import {
  type TmAttentionRig,
  tmAttentionRig,
  tmEnterTestTenant,
  tmSettle,
  tmSettleUntil,
  tmTestNotification,
  tmVersion,
  tmWarmTestTenant,
  TM_T17,
} from '../../src/attention/tm-attention.testing.util';

// The attention model in a whole test app entered into tenant 17: the
// badges from the stored documents, the verbs' overlays and their release
// by the inbox version, the stop overlay, the seen overlays and their
// release by version and seenAt, the panel's one read, live updates and the
// states that limit the verbs.

/** A running job. */
function job(id: number, status: TmInboxRunningJob['status'], members: Partial<TmInboxRunningJob> = {}): TmInboxRunningJob {
  return { id, handlerKey: 'core.export', status, createdAt: '2026-11-23T08:00:00.000Z', ...members };
}

/** An inbox with two unread rows and one read row. */
function inbox(version: number, members: Parameters<typeof tmFakeInboxSummary>[0] = {}): ReturnType<typeof tmFakeInboxSummary> {
  return tmFakeInboxSummary({
    unseen: 2,
    unread: 2,
    seenAt: '2026-11-23T07:00:00.000Z',
    latest: [
      tmTestNotification(3, { createdAt: '2026-11-23T09:00:00.000Z' }),
      tmTestNotification(2, { createdAt: '2026-11-23T08:30:00.000Z' }),
      tmTestNotification(1, { createdAt: '2026-11-23T06:00:00.000Z', readAt: '2026-11-23T06:30:00.000Z' }),
    ],
    version: tmVersion(version),
    ...members,
  });
}

/** Starts the app with the model created. */
async function setUp(...args: Parameters<typeof tmAttentionRig>): Promise<TmAttentionRig & { readonly attention: TmAttention }> {
  const rig = await tmAttentionRig(...args);
  // The server calls' chunk, loaded up front, so a call that must not be made would be made within the turns a test settles.
  await import('./tm-attention-calls');
  return { ...rig, attention: TestBed.inject(TmAttention) };
}

/** The calls to a route. */
function callsTo(rig: TmAttentionRig, route: string): readonly TmFakeApiCall[] {
  return rig.api.calls().filter((call) => call.route === route);
}

/**
 * Settles until the server calls were made, a route once for each time it
 * is named. The calls go through the server calls' lazily loaded chunk,
 * which no count of turns bounds, so a spec waits on the calls themselves.
 */
async function called(rig: TmAttentionRig, ...routes: readonly string[]): Promise<void> {
  const wanted = (route: string): number => routes.filter((candidate) => candidate === route).length;
  await tmSettleUntil(() => routes.every((route) => callsTo(rig, route).length >= wanted(route)), `calls to ${routes.join(', ')}`);
}

/** The ids of the inbox's rows, and which are read. */
function rows(attention: TmAttention): readonly string[] {
  return (attention.inbox()?.latest ?? []).map((row) => `${String(row['id'])}${row['readAt'] ? ' read' : ''}`);
}

describe('TmAttention', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  describe('the badges', () => {
    it('render without a count before any document', async () => {
      const { attention } = await setUp();
      expect(attention.inbox()).toBeUndefined();
      expect(attention.bell()).toEqual({ count: null, tone: 'accent', jobsRunning: 0, label: 'Notifications' });
      expect(attention.tray()).toEqual({ count: null, tone: 'neutral', label: 'My assignments' });
    });

    it("follow the bell's table: unseen, the attention tone and the jobs that do not wait on a person", async () => {
      const rig = await setUp();
      const running = [job(1, 'Running'), job(2, 'Held'), job(3, 'PendingConfirmation'), job(4, 'Pending')];
      await rig.put('inbox', tmFakeInboxSummary({ unseen: 3, maxUnseenSeverity: 'Warning', running }));
      expect(rig.attention.bell()).toEqual({
        count: 3,
        tone: 'attention',
        jobsRunning: 2,
        label: 'Notifications, 3 new, needs attention, 2 jobs running',
      });

      await rig.put('inbox', tmFakeInboxSummary({ unseen: 120, maxUnseenSeverity: 'Info', running: [job(1, 'Running')] }));
      expect(rig.attention.bell()).toEqual({ count: 120, tone: 'accent', jobsRunning: 1, label: 'Notifications, 99+ new, 1 job running' });

      // An error is as urgent as a warning; nothing unseen needs no attention.
      await rig.put('inbox', tmFakeInboxSummary({ unseen: 1, maxUnseenSeverity: 'Error' }));
      expect(rig.attention.bell().tone).toBe('attention');
      await rig.put('inbox', tmFakeInboxSummary({ unseen: 0 }));
      expect(rig.attention.bell()).toEqual({ count: 0, tone: 'accent', jobsRunning: 0, label: 'Notifications' });
    });

    it("follow the tray's table: open, accent while some are new", async () => {
      const rig = await setUp();
      await rig.put('assignments', tmFakeAssignmentsSummary({ open: 4, new: 1 }));
      expect(rig.attention.tray()).toEqual({ count: 4, tone: 'accent', label: 'My assignments, 4 open, 1 new' });
      await rig.put('assignments', tmFakeAssignmentsSummary({ open: 100, new: 0 }));
      expect(rig.attention.tray()).toEqual({ count: 100, tone: 'neutral', label: 'My assignments, 99+ open' });
    });
  });

  describe('the verbs', () => {
    it('apply read in the same frame, and keep it over a copy read before the verb committed but saved after its response', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox(5));
      rig.api.respond('inbox/read', { count: 1, version: tmVersion(6) });

      const done = rig.attention.read([3]);
      // The same frame: no tick, no response.
      expect(rows(rig.attention)).toEqual(['3 read', '2', '1 read']);
      expect(rig.attention.inbox()?.unread).toBe(1);
      await done;
      expect(callsTo(rig, 'inbox/read')[0]).toMatchObject({ kind: 'write', body: { ids: [3] }, options: { tenantId: 17 } });

      // A copy read before the read committed (version 5) lands after the response: the overlay holds.
      await rig.put('inbox', inbox(5));
      expect(rows(rig.attention)).toEqual(['3 read', '2', '1 read']);
      expect(rig.attention.inbox()?.unread).toBe(1);

      // The copy that contains it releases the overlay: the copy shows as stored.
      const released = inbox(6, { unread: 1, latest: [tmTestNotification(2)] });
      await rig.put('inbox', released);
      expect(rig.attention.inbox()).toBe(rig.store.doc(TM_T17, 'inbox')()?.data);
      expect(rig.telemetry.recent().filter((item) => item.name === 'tm.attention.verb').map((item) => item.props)).toEqual([
        { verb: 'read', outcome: 'ok' },
      ]);
    });

    it('apply read-all, clear and clear-read to the rows and the counts', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox(5));
      for (const route of ['inbox/read-all', 'inbox/clear', 'inbox/clear-read']) {
        rig.api.respond(route, () => new Promise(() => undefined));
      }

      void rig.attention.clear([2]);
      expect(rows(rig.attention)).toEqual(['3', '1 read']);
      expect(rig.attention.inbox()).toMatchObject({ unread: 1, unseen: 1 });

      void rig.attention.clearRead();
      expect(rows(rig.attention)).toEqual(['3']);
      expect(rig.attention.inbox()).toMatchObject({ unread: 1, unseen: 1 });

      void rig.attention.readAll();
      expect(rows(rig.attention)).toEqual(['3 read']);
      expect(rig.attention.inbox()).toMatchObject({ unread: 0, unseen: 1 });
      // The calls follow once the server calls' chunk loads.
      await called(rig, 'inbox/clear', 'inbox/clear-read', 'inbox/read-all');
      expect(callsTo(rig, 'inbox/clear')[0]?.body).toEqual({ ids: [2] });
      expect(callsTo(rig, 'inbox/clear-read')[0]?.body).toEqual({});
      expect(callsTo(rig, 'inbox/read-all')[0]?.body).toEqual({});
    });

    it('keep a capped count capped, since its true value is unknown', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox(5, { unread: 100, unseen: 100 }));
      rig.api.respond('inbox/clear', () => new Promise(() => undefined));
      void rig.attention.clear([3]);
      expect(rig.attention.inbox()).toMatchObject({ unread: 100, unseen: 100 });
    });

    it('drop a failed verb at once, toast it sticky, and try it again from the toast', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox(5));
      const show = vi.spyOn(TestBed.inject(TmToasts), 'show');
      rig.api.respond('inbox/read', () => Promise.reject(new Error('boom')));

      await rig.attention.read([3]);
      expect(rows(rig.attention)).toEqual(['3', '2', '1 read']);
      expect(show).toHaveBeenCalledOnce();
      const toast = show.mock.calls[0]![0];
      expect(toast).toMatchObject({ tone: 'error', title: "Couldn't update your notifications. Try again.", duration: 'sticky' });
      expect(toast.action?.label).toBe('Try again');
      expect(rig.telemetry.recent().some((item) => item.name === 'tm.attention.verb' && item.props?.['outcome'] === 'failed')).toBe(true);

      rig.api.respond('inbox/read', { count: 1, version: tmVersion(6) });
      toast.action?.run();
      expect(rows(rig.attention)).toEqual(['3 read', '2', '1 read']);
      await called(rig, 'inbox/read', 'inbox/read');
      expect(callsTo(rig, 'inbox/read')).toHaveLength(2);
    });

    it('do nothing while the tenant is frozen or the app is offline', async () => {
      const frozen = await setUp({ tenant: { id: 17, category: 'Live', state: 'Frozen', realtime: true } });
      await frozen.put('inbox', inbox(5));
      expect(frozen.attention.writable()).toBe(false);
      await frozen.attention.read([3]);
      await frozen.attention.stopJob(1);
      expect(rows(frozen.attention)).toEqual(['3', '2', '1 read']);
      expect(callsTo(frozen, 'inbox/read')).toHaveLength(0);
      expect(callsTo(frozen, 'jobs/cancel')).toHaveLength(0);
    });

    it('read as not writable offline, and as writable on a read-only tenant', async () => {
      const rig = await setUp({ tenant: { id: 17, category: 'Live', state: 'ReadOnly', realtime: true } });
      expect(rig.attention.writable()).toBe(true);
      rig.api.setOnline(false);
      expect(rig.attention.writable()).toBe(false);
      await rig.attention.clear([1]);
      expect(callsTo(rig, 'inbox/clear')).toHaveLength(0);
    });

    it('show a stopped job as stopping until a copy shows the request, or drops the job', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox(5, { running: [job(8, 'Running'), job(9, 'Pending')] }));
      rig.api.respond('jobs/cancel', {});

      const done = rig.attention.stopJob(8);
      expect(rig.attention.inbox()?.running.map((row) => row.cancelRequestedAt !== undefined)).toEqual([true, false]);
      await done;
      expect(callsTo(rig, 'jobs/cancel')[0]?.body).toEqual({ ids: [8] });

      // A copy without the request keeps the overlay.
      await rig.put('inbox', inbox(5, { running: [job(8, 'Running'), job(9, 'Pending')] }));
      expect(rig.attention.inbox()?.running[0]?.cancelRequestedAt).toBeDefined();

      // A copy with the request shows the copy's own.
      await rig.put('inbox', inbox(6, { running: [job(8, 'Running', { cancelRequestedAt: '2026-11-23T10:00:00.000Z' })] }));
      expect(rig.attention.inbox()?.running[0]?.cancelRequestedAt).toBe('2026-11-23T10:00:00.000Z');

      // A failed stop drops its overlay at once.
      rig.api.respond('jobs/cancel', () => Promise.reject(new Error('boom')));
      await rig.attention.stopJob(9);
      await rig.put('inbox', inbox(7, { running: [job(9, 'Pending')] }));
      expect(rig.attention.inbox()?.running[0]?.cancelRequestedAt).toBeUndefined();

      // The copy with the request released the overlay, so a later copy without it shows none.
      await rig.put('inbox', inbox(8, { running: [job(8, 'Running')] }));
      expect(rig.attention.inbox()?.running[0]?.cancelRequestedAt).toBeUndefined();
    });

    it('release a stopping overlay on a copy that drops the job', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox(5, { running: [job(8, 'Running')] }));
      rig.api.respond('jobs/cancel', {});
      await rig.attention.stopJob(8);
      await rig.put('inbox', inbox(6, { running: [] }));
      // The job listed again, as a stale copy would, shows no request: the overlay left with the copy that dropped it.
      await rig.put('inbox', inbox(7, { running: [job(8, 'Running')] }));
      expect(rig.attention.inbox()?.running[0]?.cancelRequestedAt).toBeUndefined();
    });
  });

  describe('a tenant switch', () => {
    it('drops the panel, the overlays and the seen hold of the tenant left', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox(5, { running: [job(8, 'Running')] }));
      rig.api.respond('inbox/read', () => new Promise(() => undefined));
      rig.api.respond('jobs/cancel', () => new Promise(() => undefined));
      rig.api.respond('inbox/seen', () => new Promise(() => undefined));
      rig.api.respond('inbox/summary', () => new Promise(() => undefined));
      void rig.attention.read([3]);
      void rig.attention.stopJob(8);
      rig.attention.toggle('bell', 'click');
      await tmSettle();
      expect(rows(rig.attention)).toEqual(['3 read', '2', '1 read']);
      expect(rig.attention.bell().count).toBeNull();

      await tmWarmTestTenant(rig, 18, { inbox: inbox(5, { running: [job(8, 'Running')] }) });
      await tmEnterTestTenant(18);
      expect(rig.attention.open()).toBeNull();
      expect(rows(rig.attention)).toEqual(['3', '2', '1 read']);
      expect(rig.attention.inbox()?.running[0]?.cancelRequestedAt).toBeUndefined();
      expect(rig.attention.bell().count).toBe(2);
    });
  });

  describe('the panels', () => {
    it('open the bell in the same frame, quiet its count, send the seen, then read the inbox once', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox(5, { unseen: 12 }), Date.now() - 60_000);
      rig.api.respond('inbox/seen', { count: 1, version: tmVersion(7) });
      let answer!: (value: unknown) => void;
      rig.api.respond('inbox/summary', () => new Promise((resolve) => (answer = resolve)));

      rig.attention.toggle('bell', 'click');
      expect(rig.attention.open()).toBe('bell');
      expect(rig.attention.bell().count).toBeNull();
      expect(rig.telemetry.recent().find((item) => item.name === 'tm.attention.panel-opened')?.props).toEqual({
        panel: 'bell',
        via: 'click',
        badge: '10+',
      });
      await called(rig, 'inbox/seen', 'inbox/summary');
      expect(callsTo(rig, 'inbox/seen')).toHaveLength(1);
      const read = callsTo(rig, 'inbox/summary')[0];
      expect(read?.options).toMatchObject({ background: true, tenantId: 17 });

      // A copy read before the seen committed, so sent before the panel's read, never brings the count back.
      await rig.put('inbox', inbox(6, { unseen: 12 }), Date.now() - 30_000);
      expect(rig.attention.bell().count).toBeNull();

      // The panel's own read contains it.
      answer(inbox(7, { unseen: 0 }));
      await tmSettle();
      expect(rig.attention.bell().count).toBe(0);
      expect(rig.store.doc(TM_T17, 'inbox')()?.data).toMatchObject({ version: '7' });
    });

    it('close on a second toggle, aborting the read', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox(5, { unseen: 0 }));
      rig.api.respond('inbox/summary', () => new Promise(() => undefined));
      rig.attention.toggle('bell', 'shortcut');
      await called(rig, 'inbox/summary');
      const signal = (callsTo(rig, 'inbox/summary')[0]?.options as TmReadOptions).signal;
      expect(signal.aborted).toBe(false);
      // No seen without something new.
      expect(callsTo(rig, 'inbox/seen')).toHaveLength(0);

      rig.attention.toggle('bell', 'click');
      expect(rig.attention.open()).toBeNull();
      expect(signal.aborted).toBe(true);
    });

    it('switch panels: opening the tray closes the bell', async () => {
      const rig = await setUp();
      rig.attention.toggle('bell', 'click');
      rig.attention.toggle('tray', 'click');
      expect(rig.attention.open()).toBe('tray');
    });

    it("quiet the tray until a copy at or after the seen's seenAt", async () => {
      const rig = await setUp();
      await rig.put('assignments', tmFakeAssignmentsSummary({ open: 3, new: 2, seenAt: '2026-11-23T07:00:00.000Z' }));
      rig.api.respond('assignments/seen', { seenAt: '2026-11-23T10:00:00.000+00:00' });

      rig.attention.toggle('tray', 'toast');
      expect(rig.attention.tray()).toMatchObject({ count: 3, tone: 'neutral' });
      await called(rig, 'assignments/seen');
      await tmSettle();
      // The assignments document is not read until the app registers it.
      expect(callsTo(rig, 'assignments/summary')).toHaveLength(0);

      await rig.put('assignments', tmFakeAssignmentsSummary({ open: 3, new: 2, seenAt: '2026-11-23T07:00:00.000Z' }));
      expect(rig.attention.tray().tone).toBe('neutral');
      await rig.put('assignments', tmFakeAssignmentsSummary({ open: 3, new: 1, seenAt: '2026-11-23T10:00:00.000Z' }));
      expect(rig.attention.tray()).toMatchObject({ tone: 'accent', label: 'My assignments, 3 open, 1 new' });
    });

    it('send no seen on a frozen tenant, and lift the overlay on the panel\'s own copy', async () => {
      const rig = await setUp({ tenant: { id: 17, category: 'Live', state: 'Frozen', realtime: true } });
      await rig.put('inbox', inbox(5, { unseen: 2 }), Date.now() - 1000);
      let answer!: (value: unknown) => void;
      rig.api.respond('inbox/summary', () => new Promise((resolve) => (answer = resolve)));

      rig.attention.toggle('bell', 'click');
      expect(rig.attention.bell().count).toBeNull();
      await called(rig, 'inbox/summary');
      expect(callsTo(rig, 'inbox/seen')).toHaveLength(0);

      answer(inbox(5, { unseen: 2 }));
      await tmSettle();
      expect(rig.attention.bell().count).toBe(2);
    });

    it("lift the overlay waiting on the panel's read on a copy saved after the read was sent, and not before", async () => {
      const rig = await setUp({ tenant: { id: 17, category: 'Live', state: 'Frozen', realtime: true } });
      const before = Date.now() - 1;
      await rig.put('inbox', inbox(5, { unseen: 2 }), before - 1000);
      rig.api.respond('inbox/summary', () => new Promise(() => undefined));

      rig.attention.toggle('bell', 'click');
      await called(rig, 'inbox/summary');
      expect(callsTo(rig, 'inbox/summary')).toHaveLength(1);

      // Another tab's copy, saved before this panel's read was sent.
      await rig.put('inbox', inbox(5, { unseen: 2 }), before);
      expect(rig.attention.bell().count).toBeNull();

      // Another tab's copy, saved after it: as fresh as the panel's own.
      await rig.put('inbox', inbox(5, { unseen: 2 }), Date.now() + 1);
      expect(rig.attention.bell().count).toBe(2);
    });

    it('bring the count back at once when the seen fails after the panel closed', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox(5, { unseen: 3 }), Date.now() - 60_000);
      let fail!: (error: unknown) => void;
      rig.api.respond('inbox/seen', () => new Promise((_resolve, reject) => (fail = reject)));
      rig.api.respond('inbox/summary', () => new Promise(() => undefined));

      rig.attention.toggle('bell', 'click');
      await called(rig, 'inbox/seen');
      expect(callsTo(rig, 'inbox/seen')).toHaveLength(1);
      rig.attention.toggle('bell', 'click');
      expect(rig.attention.bell().count).toBeNull();

      fail(new Error('boom'));
      await tmSettle();
      expect(rig.attention.bell().count).toBe(3);
      await rig.put('inbox', inbox(6, { unseen: 4 }));
      expect(rig.attention.bell().count).toBe(4);
      TestBed.inject(TmToasts).clear();
    });

    it('attempt nothing offline, and lift the overlay when the panel closes', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox(5, { unseen: 2 }));
      rig.api.setOnline(false);
      rig.attention.toggle('bell', 'click');
      await tmSettle();
      expect(rig.attention.bell().count).toBeNull();
      expect(callsTo(rig, 'inbox/seen')).toHaveLength(0);
      expect(callsTo(rig, 'inbox/summary')).toHaveLength(0);

      rig.attention.toggle('bell', 'click');
      expect(rig.attention.bell().count).toBe(2);
    });
  });

  describe('refresh', () => {
    it('reads the inbox and stores it stamped with the send time, and skips a document the app does not register', async () => {
      const rig = await setUp();
      rig.api.respond('inbox/summary', inbox(9));
      const before = Date.now();
      await rig.attention.refresh('inbox');
      const stored = rig.store.doc(TM_T17, 'inbox')();
      expect(stored?.data).toMatchObject({ version: '9' });
      expect(stored?.savedAt).toBeGreaterThanOrEqual(before);

      await rig.attention.refresh('assignments');
      expect(callsTo(rig, 'assignments/summary')).toHaveLength(0);
    });
  });

  describe('live', () => {
    it('pauses once the hub has been neither connected nor disabled for 5 s', async () => {
      const rig = await setUp();
      const leader = TestBed.inject(TmLeader);
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
      leader.ɵhubStatus.set({ state: 'connected', since: Date.now(), leaderVisible: true });
      TestBed.tick();
      expect(rig.attention.live()).toBe('live');

      leader.ɵhubStatus.set({ state: 'reconnecting', since: Date.now(), leaderVisible: true });
      TestBed.tick();
      expect(rig.attention.live()).toBe('live');
      vi.advanceTimersByTime(5000);
      expect(rig.attention.live()).toBe('paused');

      leader.ɵhubStatus.set({ state: 'disabled', since: Date.now(), leaderVisible: true });
      TestBed.tick();
      expect(rig.attention.live()).toBe('live');

      // A status already 5 s old reads paused at once.
      leader.ɵhubStatus.set({ state: 'offline', since: Date.now() - 6000, leaderVisible: true });
      TestBed.tick();
      expect(rig.attention.live()).toBe('paused');
    });
  });
});
