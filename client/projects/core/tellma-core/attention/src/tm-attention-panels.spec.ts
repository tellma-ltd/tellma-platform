// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TestBed } from '@angular/core/testing';
import { type TmInboxRunningJob, TmLeader } from '@tellma/core';
import {
  TmBellPanelHarness,
  tmFakeAssignmentsSummary,
  tmFakeInboxSummary,
  type TmFakeApiCall,
  TmTrayPanelHarness,
} from '@tellma/core-testing';
import { TmConfirmHarness, TmModalHarness } from '@tellma/core-ui-testing';
import { page } from 'vitest/browser';

import { TmAttention } from './tm-attention';
import {
  type TmAttentionRig,
  tmAttentionRig,
  tmSettle,
  tmSettleUntil,
  tmTestAssignment,
  tmTestNotification,
  tmVersion,
} from '../../src/attention/tm-attention.testing.util';

// The bell's and the tray's panels in a whole test app entered into tenant
// 17, through the app bar's buttons: the badges from the stored documents
// with no request, the bell's job rows by status, Stop's confirmation by
// handler, the notification rows, the states that limit the verbs, the
// status line, the tray's rows, and the popover from lg against the end
// drawer below it.

/** A running job. */
function job(id: number, status: TmInboxRunningJob['status'], minute: number, members: Partial<TmInboxRunningJob> = {}): TmInboxRunningJob {
  return { id, handlerKey: 'core.export', status, createdAt: `2026-11-23T08:${String(minute).padStart(2, '0')}:00.000Z`, ...members };
}

/** An inbox with an unread export and a read flag, and the jobs given. */
function inbox(running: readonly TmInboxRunningJob[] = []): ReturnType<typeof tmFakeInboxSummary> {
  return tmFakeInboxSummary({
    unseen: 0,
    unread: 1,
    latest: [
      tmTestNotification(3, {
        argumentsJson: JSON.stringify({ fileId: 5, fileName: 'centers.xlsx', rowCount: 12 }),
        createdAt: '2026-11-23T09:00:00.000Z',
      }),
      tmTestNotification(2, {
        type: 'gl.center.flagged',
        severity: 'Warning',
        argumentsJson: JSON.stringify({ center: 'HQ', urgent: true, tags: 'a' }),
        readAt: '2026-11-23T08:40:00.000Z',
      }),
    ],
    running,
    version: tmVersion(5),
  });
}

/**
 * Starts the app at a width with the panels' and the server calls' chunks
 * loaded, and the panel's own read held.
 */
async function setUpAt(width: number, ...args: Parameters<typeof tmAttentionRig>): Promise<TmAttentionRig & { readonly attention: TmAttention }> {
  await page.viewport(width, 900);
  const rig = await tmAttentionRig(...args);
  await Promise.all([import('./tm-attention-calls'), import('./tm-attention-panels')]);
  // The panel's background read never answers, so each test reads the copy it stored.
  rig.api.respond('inbox/summary', () => new Promise(() => undefined));
  rig.api.respond('assignments/summary', () => new Promise(() => undefined));
  return { ...rig, attention: TestBed.inject(TmAttention) };
}

/** Starts the app at lg, where the panels open in a popover. */
function setUp(...args: Parameters<typeof tmAttentionRig>): Promise<TmAttentionRig & { readonly attention: TmAttention }> {
  return setUpAt(1280, ...args);
}

/** An attention button in the app bar. */
function button(slot: 'bell' | 'tray'): HTMLButtonElement {
  return document.querySelector<HTMLButtonElement>(`tm-app-bar [data-tm-slot="${slot}"] button`)!;
}

/** Opens the bell's panel from its button and returns its harness. */
async function openBell(rig: TmAttentionRig): Promise<TmBellPanelHarness> {
  button('bell').click();
  await tmSettleUntil(() => document.querySelector('tm-bell-panel') !== null, "the bell's panel renders");
  return TestbedHarnessEnvironment.documentRootLoader(rig.fixture).getHarness(TmBellPanelHarness);
}

/** Opens the tray's panel from its button and returns its harness. */
async function openTray(rig: TmAttentionRig): Promise<TmTrayPanelHarness> {
  button('tray').click();
  await tmSettleUntil(() => document.querySelector('tm-tray-panel') !== null, "the tray's panel renders");
  return TestbedHarnessEnvironment.documentRootLoader(rig.fixture).getHarness(TmTrayPanelHarness);
}

/** The calls to a route. */
function callsTo(rig: TmAttentionRig, route: string): readonly TmFakeApiCall[] {
  return rig.api.calls().filter((call) => call.route === route);
}

describe("the bell's and the tray's panels", () => {
  afterEach(async () => {
    vi.useRealTimers();
    await page.viewport(1280, 900);
  });

  describe('the buttons', () => {
    it('render their badges from the stored documents with no request', async () => {
      const rig = await setUp();
      await rig.put('inbox', tmFakeInboxSummary({ unseen: 3, maxUnseenSeverity: 'Warning', running: [job(1, 'Running', 0)] }));
      await rig.put('assignments', tmFakeAssignmentsSummary({ open: 2, new: 1 }));
      await tmSettle();

      expect(button('bell').getAttribute('aria-label')).toBe('Notifications, 3 new, needs attention, 1 job running');
      expect(button('bell').getAttribute('aria-keyshortcuts')).toBe('Alt+Shift+U');
      expect(button('bell').querySelector('tm-progress-ring')).not.toBeNull();
      expect(button('tray').getAttribute('aria-label')).toBe('My assignments, 2 open, 1 new');
      expect(button('tray').getAttribute('aria-keyshortcuts')).toBe('Alt+Shift+Q');
      expect(rig.api.calls().filter((call) => call.route.startsWith('inbox/') || call.route.startsWith('assignments/'))).toEqual([]);
    });

    it('open their panel in a popover from lg, and close it on a second press with focus on the button', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox());
      const bell = await openBell(rig);
      expect(await bell.getHeading()).toBe('Notifications');
      expect(button('bell').getAttribute('aria-expanded')).toBe('true');
      expect(button('bell').getAttribute('aria-controls')).toBe('tm-attention-bell-panel');
      expect(document.querySelector('.tm-popover__panel #tm-attention-bell-panel')).not.toBeNull();

      button('bell').click();
      await tmSettleUntil(() => document.querySelector('tm-bell-panel') === null, "the bell's panel closes");
      expect(rig.attention.open()).toBeNull();
      expect(button('bell').getAttribute('aria-expanded')).toBe('false');
    });

    it('open their panel in the end drawer below lg, whose title stands in for the heading', async () => {
      const rig = await setUpAt(800);
      await rig.put('assignments', tmFakeAssignmentsSummary());
      const tray = await openTray(rig);
      const drawer = await TestbedHarnessEnvironment.documentRootLoader(rig.fixture).getHarness(TmModalHarness);
      expect(await drawer.getPlacement()).toBe('end');
      expect(await drawer.getTitle()).toBe('My assignments');
      expect(await tray.getHeading()).toBeNull();

      await drawer.close();
      await tmSettleUntil(() => rig.attention.open() === null, 'the model closes with the drawer');
    });
  });

  describe('the bell', () => {
    it('lists the newest three running jobs with their status rows, then the rest as one link', async () => {
      const rig = await setUp();
      await rig.put(
        'inbox',
        inbox([
          job(1, 'Running', 50, { progressPercent: 40 }),
          job(2, 'Pending', 40),
          job(3, 'Held', 30),
          job(4, 'PendingConfirmation', 20, { handlerKey: 'core.import' }),
          job(5, 'Running', 10),
        ]),
      );
      const bell = await openBell(rig);
      expect(await bell.getJobs()).toEqual([
        { id: 1, title: 'core.export', href: '/17/jobs/1', badge: null, percent: '40%', progress: true, line: '', controls: ['Stop'] },
        { id: 2, title: 'core.export', href: '/17/jobs/2', badge: null, percent: null, progress: true, line: 'Waiting to start', controls: ['Stop'] },
        {
          id: 3,
          title: 'core.export',
          href: '/17/jobs/3',
          badge: 'Held',
          percent: null,
          progress: false,
          line: 'Paused after a service interruption',
          controls: ['Stop'],
        },
      ]);
      expect(await bell.getMoreJobs()).toBe('+2 more in My jobs');

      // The older two, once the newer ones end: confirmation awaits Review, a stop asked for shows Stopping….
      await rig.put('inbox', inbox([job(4, 'PendingConfirmation', 20, { handlerKey: 'core.import' }), job(5, 'Running', 10, { cancelRequestedAt: '2026-11-23T08:11:00.000Z' })]));
      expect(await bell.getJobs()).toEqual([
        {
          id: 4,
          title: 'Importing',
          href: '/17/jobs/4',
          badge: 'Awaiting confirmation',
          percent: null,
          progress: false,
          line: 'Waiting for you to confirm',
          controls: ['Review', 'Stop'],
        },
        { id: 5, title: 'core.export', href: '/17/jobs/5', badge: null, percent: null, progress: true, line: 'Stopping…', controls: [] },
      ]);
      expect(await bell.getMoreJobs()).toBeNull();
    });

    it("asks before stopping a job whose handler saves its work in steps, and stops an export at once", async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox([job(1, 'Running', 50), job(2, 'Running', 40, { handlerKey: 'core.import' })]));
      rig.api.respond('jobs/cancel', {});
      const bell = await openBell(rig);
      const loader = TestbedHarnessEnvironment.documentRootLoader(rig.fixture);

      // An import asks; keeping it running sends nothing and leaves the panel open.
      await bell.stopJob(2);
      const confirm = await loader.getHarness(TmConfirmHarness);
      expect(await confirm.getTitle()).toBe('Stop this import?');
      expect(await confirm.getMessage()).toBe("Rows already saved stay saved. The rest of the file won't be imported.");
      expect(await confirm.getChoiceLabels()).toEqual(['Keep running', 'Stop import']);
      await confirm.choose('Keep running');
      await tmSettle();
      expect(callsTo(rig, 'jobs/cancel')).toHaveLength(0);
      expect(rig.attention.open()).toBe('bell');

      await bell.stopJob(2);
      await (await loader.getHarness(TmConfirmHarness)).choose('Stop import');
      await tmSettleUntil(() => callsTo(rig, 'jobs/cancel').length === 1, 'the import is stopped');
      expect(callsTo(rig, 'jobs/cancel')[0]?.body).toEqual({ ids: [2] });

      // An export stops at once.
      await bell.stopJob(1);
      await tmSettleUntil(() => callsTo(rig, 'jobs/cancel').length === 2, 'the export is stopped');
      expect(callsTo(rig, 'jobs/cancel')[1]?.body).toEqual({ ids: [1] });
      expect(await loader.getAllHarnesses(TmConfirmHarness)).toEqual([]);
    });

    it('shows each notification as a link to its open route, with an export\'s Download, its time and Clear', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox());
      rig.api.respond('inbox/clear', { count: 1, version: tmVersion(6) });
      const bell = await openBell(rig);
      const [exported, flagged] = await bell.getNotifications();
      expect(exported).toMatchObject({
        id: 3,
        title: 'Export is ready',
        line: 'centers.xlsx · 12 rows',
        href: '/17/notifications/3/open',
        severity: 'Success',
        unread: true,
        clearable: true,
      });
      expect(exported?.label).toMatch(/^Export is ready, centers\.xlsx · 12 rows, .+, unread$/);
      expect(exported?.download).toContain('export-file');
      expect(flagged).toMatchObject({ id: 2, title: 'HQ was flagged', severity: 'Warning', unread: false, download: null });

      await bell.clearNotification(3);
      expect((await bell.getNotifications()).map((row) => row.id)).toEqual([2]);
      await tmSettleUntil(() => callsTo(rig, 'inbox/clear').length === 1, 'the clear is sent');
      expect(callsTo(rig, 'inbox/clear')[0]?.body).toEqual({ ids: [3] });
    });

    it('shows skeleton rows before any copy, and the empty state for an empty inbox', async () => {
      const rig = await setUp();
      const bell = await openBell(rig);
      expect(await bell.isLoading()).toBe(true);
      await rig.put('inbox', tmFakeInboxSummary());
      expect(await bell.isLoading()).toBe(false);
      expect(await bell.getEmptyText()).toBe("You're all caught up");
      expect(await bell.getSeeAllHref()).toBe('/17/notifications');
    });

    it('keeps every verb offline, each disabled with the reason', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox([job(1, 'Running', 50)]));
      rig.api.setOnline(false);
      const bell = await openBell(rig);
      expect(await bell.getHeaderVerbReason('Mark all as read')).toBe("You're offline");
      expect(await (await bell.getJobControl(1, 'Stop')).getDisabledReason()).toBe("You're offline");
      expect(await bell.getClearReason(3)).toBe("You're offline");
      const more = await bell.openMore();
      expect(await more.getDisabledReason('Clear read notifications')).toBe("You're offline");
    });

    it('shows every verb on a read-only tenant, and none on a frozen one', async () => {
      const readOnly = await setUp({ tenant: { id: 17, category: 'Live', state: 'ReadOnly', realtime: true } });
      await readOnly.put('inbox', inbox([job(1, 'Running', 50)]));
      let bell = await openBell(readOnly);
      expect(await bell.getHeaderVerbs()).toEqual(['Mark all as read', 'More options']);
      expect((await bell.getJobs())[0]?.controls).toEqual(['Stop']);
      expect((await bell.getNotifications()).every((row) => row.clearable)).toBe(true);
      expect(await (await bell.openMore()).getItemLabels()).toEqual(['Notification settings', 'Clear read notifications']);
      TestBed.resetTestingModule();

      const frozen = await setUp({ tenant: { id: 17, category: 'Live', state: 'Frozen', realtime: true } });
      await frozen.put('inbox', inbox([job(1, 'Running', 50)]));
      bell = await openBell(frozen);
      expect(await bell.getHeaderVerbs()).toEqual(['More options']);
      expect((await bell.getJobs())[0]?.controls).toEqual([]);
      expect((await bell.getNotifications()).some((row) => row.clearable)).toBe(false);
      expect(await (await bell.openMore()).getItemLabels()).toEqual(['Notification settings']);
    });

    it('shows the status line with Refresh while live updates are paused', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox());
      const bell = await openBell(rig);
      expect(await bell.getStatusLine()).toBeNull();

      TestBed.inject(TmLeader).ɵhubStatus.set({ state: 'offline', since: Date.now() - 6000, leaderVisible: true });
      await tmSettle();
      expect(await bell.getStatusLine()).toMatch(/^Live updates paused · Updated .+$/);
      const before = callsTo(rig, 'inbox/summary').length;
      await bell.refresh();
      await tmSettleUntil(() => callsTo(rig, 'inbox/summary').length > before, 'the refresh reads the inbox');
    });

    it('focuses its first row on opening and closes on a notification link, marking nothing itself', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox());
      const bell = await openBell(rig);
      await tmSettle();
      expect(await bell.getFocusedText()).toMatch(/^Export is ready, /);

      await bell.openNotification(3);
      await tmSettleUntil(() => rig.attention.open() === null, 'the panel closes');
      expect(rig.router.url).toBe('/17/notifications/3/open');
      expect(callsTo(rig, 'inbox/read')).toHaveLength(0);
    });
  });

  describe('the tray', () => {
    it('shows the open count and the rows, marking those assigned since the tray was last opened', async () => {
      const rig = await setUp();
      await rig.put(
        'assignments',
        tmFakeAssignmentsSummary({
          open: 2,
          new: 1,
          seenAt: '2026-11-23T08:30:00.000Z',
          latest: [
            tmTestAssignment(1, { captionJson: JSON.stringify({ code: 'HQ-01' }), assignedAt: '2026-11-23T09:00:00.000Z', comment: 'Please review' }),
            tmTestAssignment(2, { targetId: '24', captionJson: JSON.stringify({ code: 'HQ-02' }) }),
          ],
        }),
      );
      const tray = await openTray(rig);
      // The heading's count is drawn for the eye and spoken as "2 open".
      expect(await tray.getHeading()).toMatch(/^My assignments/);
      expect(await tray.getOpenCount()).toBe('2 open');
      const rows = await tray.getRows();
      expect(rows.map(({ title, href, new: fresh }) => ({ title, href, fresh }))).toEqual([
        { title: 'Center HQ-01', href: '/17/centers/23', fresh: true },
        { title: 'Center HQ-02', href: '/17/centers/24', fresh: false },
      ]);
      // The server routes no assignments yet, so the footer holds no link.
      expect(await tray.getSeeAllHref()).toBeNull();

      await tray.openRow(0);
      await tmSettleUntil(() => rig.attention.open() === null, 'the panel closes');
      expect(rig.router.url).toBe('/17/centers/23');
      expect(rig.telemetry.recent().find((item) => item.name === 'tm.attention.item-opened')?.props).toEqual({
        panel: 'tray',
        kind: 'assignment',
        position: '1',
      });
    });

    it('shows skeleton rows before any copy, and the empty state with nothing open', async () => {
      const rig = await setUp();
      const tray = await openTray(rig);
      expect(await tray.isLoading()).toBe(true);
      await rig.put('assignments', tmFakeAssignmentsSummary());
      expect(await tray.getEmptyText()).toBe('Nothing is waiting on you');
    });
  });
});
