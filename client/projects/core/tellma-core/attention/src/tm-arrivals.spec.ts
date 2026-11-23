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

import { TmArrivals, tmNotificationToastsKey } from './tm-arrivals';
import { TmAttention } from './tm-attention';
import {
  type TmAttentionRig,
  tmAttentionRig,
  tmSettle,
  tmTestAssignment,
  tmTestNotification,
  tmVersion,
} from '../../src/attention/tm-attention.testing.util';

// The arrival engine in a whole test app entered into tenant 17, whose
// tenant shell starts it: the baseline, the arrival rules of notifications,
// assignments and the tenant's state, the replaced row, the visible and
// focused tab, the preference that turns arrivals off, grouping, the keys,
// the actions and the overflow pill.

/** The rig with the toasts' show spied on, the tab visible and focused, and each put settled. */
interface TmArrivalsRig extends TmAttentionRig {
  /** The toasts shown, in order. */
  readonly show: MockInstance<TmToasts['show']>;
  /** Whether the tab has focus. */
  readonly focus: MockInstance<() => boolean>;
  /** The router's navigations. */
  readonly navigate: MockInstance<Router['navigateByUrl']>;
}

/** Starts the app in tenant 17 with its arrival engine, in a visible, focused tab. */
async function setUp(): Promise<TmArrivalsRig> {
  const rig = await tmAttentionRig();
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
  const focus = vi.spyOn(document, 'hasFocus').mockReturnValue(true);
  const show = vi.spyOn(TestBed.inject(TmToasts), 'show');
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  TestBed.inject(TmArrivals);
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

/** Tenant 17's facts in a state. */
function tenant(state: TmTenantFacts['state']): TmTenantFacts {
  return { id: 17, category: 'Live', state, realtime: true };
}

describe('TmArrivals', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  describe('the baseline', () => {
    it('toasts nothing for copies written before the tab started, or for the first copy after', async () => {
      const rig = await setUp();
      // Written before the tab started: it toasts nothing, but it is the previous copy.
      await rig.put('inbox', inbox(notice(1, 0)), Date.now() - 60_000);
      // The baseline: what arrived before the tab started never pops up.
      await rig.put('inbox', inbox(notice(2, 5), notice(1, 0)));
      expect(rig.show).not.toHaveBeenCalled();

      await rig.put('inbox', inbox(notice(3, 10), notice(2, 5), notice(1, 0)));
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
    /** The assignments summary with the given rows, the assigner among its related users. */
    function assignments(...latest: ReturnType<typeof tmTestAssignment>[]): ReturnType<typeof tmFakeAssignmentsSummary> {
      return tmFakeAssignmentsSummary({ open: latest.length, new: latest.length, latest, related: { 'core.User': [{ id: 7, name: 'Sara Ahmed' }] } });
    }

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
