// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { type TmInboxSummary, TmPreferences, type TmTenantFacts } from '@tellma/core';
import { tmFakeAssignmentsSummary, tmFakeInboxSummary } from '@tellma/core-testing';
import { type TmToast, TmToasts } from '@tellma/core-ui/toast';
import type { MockInstance } from 'vitest';

import { tmNotificationToastsKey } from './tm-arrivals';
import { TmAttention } from './tm-attention';
import {
  type TmAttentionRig,
  tmAttentionRig,
  tmEnterTestTenant,
  tmSettle,
  TM_T17,
  tmTestAssignment,
  tmTestNotification,
  tmVersion,
  tmWarmTestTenant,
} from '../../src/attention/tm-attention.testing.util';

// The arrival engine in a whole test app entered into tenant 17, whose
// tenant shell starts it, as no spec injects it: the first copy a tab
// observes and the entry's clock, a tenant switch, the arrival rules of
// notifications, assignments and the tenant's state, the replaced row, the
// visible and focused tab, the preference that turns arrivals off,
// grouping, the keys, the actions and the overflow pill.

/** The rig with the toasts' show spied on, the tab visible and focused, and each put settled. */
interface TmArrivalsRig extends TmAttentionRig {
  /** The toasts shown, in order. */
  readonly show: MockInstance<TmToasts['show']>;
  /** Whether the tab has focus. */
  readonly focus: MockInstance<() => boolean>;
  /** The router's navigations. */
  readonly navigate: MockInstance<Router['navigateByUrl']>;
}

/** Starts the app in tenant 17, whose shell starts the arrival engine, in a visible, focused tab. */
async function setUp(): Promise<TmArrivalsRig> {
  const rig = await tmAttentionRig();
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
  const focus = vi.spyOn(document, 'hasFocus').mockReturnValue(true);
  const show = vi.spyOn(TestBed.inject(TmToasts), 'show');
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  // The arrival toasts' chunk, loaded up front, so a copy's toasts show within the turns a put settles.
  await import('./tm-arrival-toasts');
  const put: TmAttentionRig['put'] = async (name, data, savedAt) => {
    await rig.put(name, data, savedAt);
    await tmSettle();
  };
  return { ...rig, put, show, focus, navigate };
}

/** The toasts shown, in order. */
function shown(rig: TmArrivalsRig): readonly TmToast[] {
  return rig.show.mock.calls.map(([toast]) => toast);
}

/** An inbox with the given rows, newest first. */
function inbox(...latest: TmInboxSummary['latest']): TmInboxSummary {
  return tmFakeInboxSummary({ unseen: latest.length, unread: latest.length, latest, version: tmVersion(latest.length) });
}

/** A notification created at the given minute past 08:00. */
function notice(id: number, minute: number, members: Readonly<Record<string, unknown>> = {}): TmInboxSummary['latest'][number] {
  return tmTestNotification(id, { createdAt: `2026-11-23T08:${String(minute).padStart(2, '0')}:00.000Z`, ...members });
}

/** A tenant's facts in a state; tenant 17's by default. */
function tenant(state: TmTenantFacts['state'], id = 17): TmTenantFacts {
  return { id, category: 'Live', state, realtime: true };
}

/** A notification created at the given epoch milliseconds. */
function noticeAt(id: number, at: number): TmInboxSummary['latest'][number] {
  return tmTestNotification(id, { createdAt: new Date(at).toISOString() });
}

/** The assignments summary with the given rows, the assigner among its related users. */
function assignments(...latest: ReturnType<typeof tmTestAssignment>[]): ReturnType<typeof tmFakeAssignmentsSummary> {
  return tmFakeAssignmentsSummary({ open: latest.length, new: latest.length, latest, related: { 'core.User': [{ id: 7, name: 'Sara Ahmed' }] } });
}

/** Stores a copy of another tenant's document, saved at the given time (now by default), and settles. */
async function putIn(rig: TmArrivalsRig, tenantId: number, name: string, data: unknown, savedAt = Date.now()): Promise<void> {
  await rig.store.put({ ...TM_T17, tenantId }, name, { tag: null, savedAt, data });
  await tmSettle();
}

/** Leaves the router's navigations unmocked, so a spec can enter another tenant. */
function unmockNavigation(rig: TmArrivalsRig): void {
  rig.navigate.mockRestore();
}

/** Waits a few milliseconds, so a later instant differs from an earlier one. */
function pause(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 5));
}

describe('TmArrivals', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  describe('the tenant shell', () => {
    it('starts the engine on entering a tenant', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox());
      await rig.put('inbox', inbox(notice(1, 5)));
      expect(shown(rig).map((toast) => toast.key)).toEqual(['n:1']);
    });
  });

  describe('the first copy', () => {
    it('toasts a row created since the tab entered the tenant, after a copy saved before', async () => {
      const rig = await setUp();
      // Saved before the entry: it toasts nothing, but it is the previous copy, whatever its age.
      await rig.put('inbox', inbox(), Date.now() - 60_000);
      await rig.put('inbox', inbox(noticeAt(1, Date.now())));
      expect(shown(rig).map((toast) => toast.key)).toEqual(['n:1']);
    });

    it('toasts no row created before the tab entered the tenant, after a copy saved before', async () => {
      const rig = await setUp();
      const before = Date.now() - 3_600_000;
      await rig.put('inbox', inbox(), Date.now() - 60_000);
      await rig.put('inbox', inbox(noticeAt(1, before)));
      expect(rig.show).not.toHaveBeenCalled();

      // The next copy's previous copy was saved since the entry, so its rows count by the newest alone.
      await rig.put('inbox', inbox(noticeAt(2, before + 1), noticeAt(1, before)));
      expect(shown(rig).map((toast) => toast.key)).toEqual(['n:2']);
    });

    it("toasts the leader's first copy in a follower tab, whose first copy the leader saved before the entry", async () => {
      const rig = await setUp();
      await rig.put('assignments', assignments(), Date.now() - 60_000);
      await rig.put(
        'assignments',
        assignments(
          tmTestAssignment(5, { assignedAt: new Date().toISOString() }),
          tmTestAssignment(4, { assignedAt: new Date(Date.now() - 3_600_000).toISOString() }),
        ),
      );
      expect(shown(rig).map((toast) => toast.key)).toEqual(['a:5']);
    });

    it("toasts a change of the tenant's state from a copy saved since the entry, over the warm copy", async () => {
      const rig = await setUp();
      // The warm launch's copy, saved before the entry and active, is the previous copy.
      await rig.put('tenant', tenant('ReadOnly'));
      expect(shown(rig)).toEqual([expect.objectContaining({ tone: 'warning', title: 'Acme is now read-only. You can view data but not change it.' })]);
    });

    it("toasts no change of the tenant's state that only a copy saved before the entry shows", async () => {
      const rig = await setUp();
      unmockNavigation(rig);
      await tmWarmTestTenant(rig, 18, { tenant: tenant('Active', 18) }, Date.now() - 60_000);
      await tmEnterTestTenant(18);
      await putIn(rig, 18, 'tenant', tenant('Frozen', 18), Date.now() - 30_000);
      expect(rig.show).not.toHaveBeenCalled();

      await putIn(rig, 18, 'tenant', tenant('ReadOnly', 18));
      expect(shown(rig).map((toast) => toast.tone)).toEqual(['warning']);
      expect(shown(rig)[0]?.title).toContain('read-only');
    });
  });

  describe('a tenant switch', () => {
    it("toasts none of the new tenant's backlog, only the rows created since the entry", async () => {
      const rig = await setUp();
      unmockNavigation(rig);
      await rig.put('inbox', inbox());
      // Created while the tab was in tenant 17, so after its entry there, but before the entry into tenant 18.
      await pause();
      const backlog = Date.now();
      await tmWarmTestTenant(rig, 18, { inbox: inbox() });
      await pause();
      await tmEnterTestTenant(18);
      expect(rig.show).not.toHaveBeenCalled();

      await putIn(rig, 18, 'inbox', inbox(noticeAt(3, Date.now()), noticeAt(2, backlog)));
      expect(shown(rig).map((toast) => toast.key)).toEqual(['n:3']);
    });
  });

  describe('notifications', () => {
    it('toast only the unread rows of toasting types newer than the previous copy\'s newest', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox(notice(1, 0)));
      await rig.put(
        'inbox',
        inbox(
          notice(6, 30, { readAt: '2026-11-23T08:31:00.000Z' }),
          notice(5, 25, { type: 'gl.center.shared', severity: 'Info' }),
          notice(4, 20, { type: 'acme.unknown' }),
          notice(3, 15),
          // Older than the previous copy's newest: not an arrival, though new to the list.
          notice(2, 0),
          notice(1, 0),
        ),
      );
      expect(shown(rig).map((toast) => toast.key)).toEqual(['n:3']);
    });

    it('toast a replaced row again under its key, which replaces its open toast', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox(notice(1, 0)));
      await rig.put('inbox', inbox(notice(2, 5), notice(1, 0)));
      await rig.put('inbox', inbox(notice(2, 9), notice(1, 0)));
      expect(shown(rig).map((toast) => toast.key)).toEqual(['n:2', 'n:2']);
      await Promise.resolve();
      expect(rig.telemetry.recent().filter((item) => item.name === 'tm.attention.toast').map((item) => item.props)).toEqual([
        { kind: 'notification', outcome: 'replaced' },
      ]);
    });

    it("render the title and detail, tone by severity, a sticky error, and the type's action", async () => {
      const rig = await setUp();
      const clicked: string[] = [];
      vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
        clicked.push(`${this.getAttribute('href')} ${this.hasAttribute('download')}`);
      });
      await rig.put('inbox', inbox());
      await rig.put(
        'inbox',
        inbox(
          notice(3, 15, { type: 'gl.center.flagged', severity: 'Warning', targetResource: 'gl.Center', targetId: '5', argumentsJson: '{"center":"HQ","urgent":false,"tags":"none"}' }),
          notice(2, 10, { type: 'core.job.failed', severity: 'Error' }),
          notice(1, 5, { argumentsJson: JSON.stringify({ fileName: 'C.xlsx', rowCount: 3, fileId: 77 }), targetResource: 'core.Export', targetId: '4' }),
        ),
      );
      const [exported, failed, flagged] = shown(rig);
      expect(exported).toMatchObject({ tone: 'success', title: 'Export is ready', detail: 'C.xlsx · 3 rows', duration: 6000, key: 'n:1' });
      expect(failed).toMatchObject({ tone: 'error', title: 'A job failed', duration: 'sticky', key: 'n:2' });
      expect(failed?.action).toBeUndefined();
      expect(failed?.detail).toBeUndefined();
      expect(flagged).toMatchObject({ tone: 'warning', title: 'HQ was flagged', duration: 6000, key: 'n:3' });

      expect(exported?.action?.label).toBe('Download');
      exported?.action?.run();
      expect(clicked).toEqual(['/17/blobs/export-file/77?download=true true']);
      expect(flagged?.action?.label).toBe('View');
      flagged?.action?.run();
      expect(rig.navigate).toHaveBeenCalledWith('/17/notifications/3/open');
    });

    it('show more than three arrivals oldest first, so the newest three show and the pill opens the notifications', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox());
      await rig.put('inbox', inbox(notice(4, 20), notice(3, 15), notice(2, 10), notice(1, 5)));
      expect(shown(rig).map((toast) => toast.key)).toEqual(['n:1', 'n:2', 'n:3', 'n:4']);
      TestBed.tick();
      const pill = document.querySelector<HTMLButtonElement>('button.tm-toasts__pill');
      expect(pill?.textContent?.trim()).toBe('1 more · Open notifications');
      pill?.click();
      expect(rig.navigate).toHaveBeenCalledWith('/17/notifications');
      TestBed.inject(TmToasts).clear();
    });

    it('toast only in a visible, focused tab, and replay nothing when the tab gains focus', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox());
      rig.focus.mockReturnValue(false);
      await rig.put('inbox', inbox(notice(1, 5)));
      expect(rig.show).not.toHaveBeenCalled();

      rig.focus.mockReturnValue(true);
      await rig.put('inbox', inbox(notice(1, 5)), Date.now() + 1);
      expect(rig.show).not.toHaveBeenCalled();
      await rig.put('inbox', inbox(notice(2, 10), notice(1, 5)), Date.now() + 2);
      expect(shown(rig).map((toast) => toast.key)).toEqual(['n:2']);
    });

    it('toast no arrival while the user turned arrival toasts off', async () => {
      const rig = await setUp();
      TestBed.inject(TmPreferences).set(tmNotificationToastsKey, false);
      await rig.put('inbox', inbox());
      await rig.put('inbox', inbox(notice(1, 5)));
      await rig.put('assignments', tmFakeAssignmentsSummary());
      await rig.put('assignments', tmFakeAssignmentsSummary({ latest: [tmTestAssignment(1)] }));
      expect(rig.show).not.toHaveBeenCalled();
    });
  });

  describe('assignments', () => {
    it('toast one arrival with its caption, the assigner\'s comment and Open', async () => {
      const rig = await setUp();
      await rig.put('assignments', assignments());
      const row = tmTestAssignment(1874, { captionJson: '{"code":"HQ"}', comment: 'For your kind approval', assignedAt: '2026-11-23T09:00:00.000Z' });
      await rig.put('assignments', assignments(row));
      expect(shown(rig)).toEqual([
        expect.objectContaining({
          tone: 'accent',
          icon: 'inbox',
          title: 'Center HQ assigned to you',
          detail: 'Sara Ahmed: For your kind approval',
          duration: 6000,
          key: 'a:1874',
        }),
      ]);
      shown(rig)[0]?.action?.run();
      expect(rig.navigate).toHaveBeenCalledWith('/17/centers/23');
    });

    it('toast a return, with the caption or the assigner when there is no comment', async () => {
      const rig = await setUp();
      await rig.put('assignments', assignments());
      await rig.put('assignments', assignments(tmTestAssignment(2, { isReturn: true, assignedAt: '2026-11-23T09:00:00.000Z', captionJson: '{"detail":{"manager":"Omar"}}' })));
      await rig.put('assignments', assignments(tmTestAssignment(3, { assignedAt: '2026-11-23T09:05:00.000Z' })));
      expect(shown(rig).map(({ title, detail }) => ({ title, detail }))).toEqual([
        { title: 'Center #23 returned to you', detail: 'Managed by Omar' },
        { title: 'Center #23 assigned to you', detail: 'Assigned by Sara Ahmed' },
      ]);
    });

    it("toast nothing for the user's own assignment, and group several arrivals", async () => {
      const rig = await setUp();
      await rig.put('assignments', assignments());
      await rig.put('assignments', assignments(tmTestAssignment(1, { assignedById: 42, assignedAt: '2026-11-23T09:00:00.000Z' })));
      expect(rig.show).not.toHaveBeenCalled();

      await rig.put(
        'assignments',
        assignments(
          tmTestAssignment(3, { assignedAt: '2026-11-23T09:10:00.000Z' }),
          tmTestAssignment(2, { assignedById: null, assignedAt: '2026-11-23T09:05:00.000Z' }),
        ),
      );
      expect(shown(rig)).toEqual([expect.objectContaining({ tone: 'accent', title: '2 documents assigned to you', key: 'a:many' })]);
      const attention = TestBed.inject(TmAttention);
      shown(rig)[0]?.action?.run();
      expect(attention.open()).toBe('tray');
      // Run again with the tray open: it stays open.
      shown(rig)[0]?.action?.run();
      expect(attention.open()).toBe('tray');
    });
  });

  describe("the tenant's state", () => {
    it('toasts a tenant that turned read-only or frozen, even with arrival toasts off', async () => {
      const rig = await setUp();
      TestBed.inject(TmPreferences).set(tmNotificationToastsKey, false);
      await rig.put('tenant', tenant('Active'));
      await rig.put('tenant', tenant('ReadOnly'));
      await rig.put('tenant', tenant('ReadOnly'));
      await rig.put('tenant', tenant('Frozen'));
      await rig.put('tenant', tenant('Active'));
      expect(shown(rig)).toEqual([
        expect.objectContaining({ tone: 'warning', title: 'Acme is now read-only. You can view data but not change it.', duration: 6000 }),
        expect.objectContaining({
          tone: 'warning',
          title: 'Acme is now under maintenance. You can view data but not change it until it ends.',
        }),
      ]);
    });
  });
});
