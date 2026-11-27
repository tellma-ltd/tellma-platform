// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TestBed } from '@angular/core/testing';
import { type TmInboxRunningJob, TmLeader, TmPageActions, TmPageContexts, TmSupport } from '@tellma/core';
import {
  TmBellPanelHarness,
  tmFakeAssignmentsSummary,
  tmFakeInboxSummary,
  type TmFakeApiCall,
  TmTrayPanelHarness,
} from '@tellma/core-testing';
import { TmConfirmHarness, TmModalHarness } from '@tellma/core-ui-testing';
import axe from 'axe-core';
import { page } from 'vitest/browser';

import { TmAttention } from './tm-attention';
import { TM_ATTENTION_PANELS_LOAD } from './tm-attention-buttons';
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
// 17, through the app bar's buttons and their shortcuts: the badges from
// the stored documents with no request, the bell's job rows by status,
// Stop's confirmation by the job's kind, the notification rows with their
// arrivals, withdrawals and replacements, the verbs and where focus goes
// when one removes its own control, the states that limit the verbs, the
// status line, Review, the tray's rows and their New marks, the overlay page
// contexts and their actions, the support dump's section, and the popover
// from lg against the end drawer below it.

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

/**
 * The confirmation once it shows: the dispatcher loads `TmConfirm` on its
 * first question, which a stable fixture does not wait for.
 */
async function confirmOf(rig: TmAttentionRig): Promise<TmConfirmHarness> {
  await tmSettleUntil(() => document.querySelector('tm-confirm-content') !== null, 'the confirmation shows');
  return TestbedHarnessEnvironment.documentRootLoader(rig.fixture).getHarness(TmConfirmHarness);
}

/** The WCAG levels the panels are held to. */
const WCAG = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'];

/** The widths the focus rules hold at: the popover from lg, the end drawer below it. */
const WIDTHS = [1280, 800] as const;

/** An inbox of unread notifications, ids `count` down to 1, the newest first. */
function many(count: number, version = 5): ReturnType<typeof tmFakeInboxSummary> {
  return tmFakeInboxSummary({
    unseen: 0,
    unread: count,
    latest: Array.from({ length: count }, (_, index) =>
      tmTestNotification(count - index, { createdAt: `2026-11-23T09:${String(59 - index).padStart(2, '0')}:00.000Z` }),
    ),
    version: tmVersion(version),
  });
}

/** The focused element. */
function focused(): Element | null {
  return document.activeElement;
}

/** The panel's own element, which takes focus in the drawer when its heading is the drawer's title. */
function panelOf(name: 'bell' | 'tray'): HTMLElement {
  return document.querySelector<HTMLElement>(`tm-${name}-panel`)!;
}

/** Presses a key on the focused element. */
function press(key: string): void {
  focused()?.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
}

/** The heading focus falls back to: the panel's heading from lg, the panel itself in the drawer. */
function fallbackOf(name: 'bell' | 'tray', width: number): Element {
  return width >= 992 ? panelOf(name).querySelector('.tm-attention-panel__heading')! : panelOf(name);
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

    it("asks before every stop, in the job's kind's words, and moves focus to the row's title once Stop leaves", async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox([job(1, 'Running', 50), job(2, 'Running', 40, { handlerKey: 'core.import' })]));
      rig.api.respond('jobs/cancel', {});
      const bell = await openBell(rig);
      const loader = TestbedHarnessEnvironment.documentRootLoader(rig.fixture);

      // An import asks in an import's words; Cancel sends nothing and leaves the panel open.
      await bell.stopJob(2);
      let confirm = await confirmOf(rig);
      expect(await confirm.getTitle()).toBe('Stop this import?');
      expect(await confirm.getMessage()).toBe("Rows already saved stay saved. The rest of the file won't be imported.");
      expect(await confirm.getChoiceLabels()).toEqual(['Cancel', 'Stop import']);
      await confirm.choose('Cancel');
      await tmSettle();
      expect(callsTo(rig, 'jobs/cancel')).toHaveLength(0);
      expect(rig.attention.open()).toBe('bell');

      await bell.stopJob(2);
      await (await confirmOf(rig)).choose('Stop import');
      await tmSettleUntil(() => callsTo(rig, 'jobs/cancel').length === 1, 'the import is stopped');
      expect(callsTo(rig, 'jobs/cancel')[0]?.body).toEqual({ ids: [2] });

      // An export asks too, in a job's words.
      await bell.stopJob(1);
      confirm = await confirmOf(rig);
      expect(await confirm.getTitle()).toBe('Stop this job?');
      expect(await confirm.getMessage()).toBe('Work already saved stays saved.');
      expect(await confirm.getChoiceLabels()).toEqual(['Cancel', 'Stop']);
      await confirm.choose('Stop');
      await tmSettleUntil(() => callsTo(rig, 'jobs/cancel').length === 2, 'the export is stopped');
      expect(callsTo(rig, 'jobs/cancel')[1]?.body).toEqual({ ids: [1] });
      expect(await loader.getAllHarnesses(TmConfirmHarness)).toEqual([]);
    });

    for (const width of WIDTHS) {
      it(`keeps focus in the panel when a verb removes its own control, at ${width} px`, async () => {
        const rig = await setUpAt(width);
        await rig.put('inbox', inbox([job(1, 'Running', 50)]));
        rig.api.respond('jobs/cancel', {});
        rig.api.respond('inbox/read-all', () => new Promise(() => undefined));
        const bell = await openBell(rig);

        // Mark all as read leaves once nothing is unread: focus moves on to More options.
        await bell.markAllRead();
        await tmSettleUntil(() => callsTo(rig, 'inbox/read-all').length === 1, 'the read-all is sent');
        await tmSettle();
        expect(await bell.getHeaderVerbs()).toEqual(['More options']);
        expect(focused()?.getAttribute('aria-label')).toBe('More options');
        expect(rig.attention.open()).toBe('bell');

        // Stop with focus on it: once answered, Stop leaves its row, and focus moves to the row's title.
        const stop = panelOf('bell').querySelector<HTMLElement>('[data-tm-id="1"] .tm-attention-panel__hover')!;
        stop.focus();
        stop.click();
        await (await confirmOf(rig)).choose('Stop');
        await tmSettleUntil(() => callsTo(rig, 'jobs/cancel').length === 1, 'the export is stopped');
        await tmSettleUntil(() => (focused()?.closest('[data-tm-id="1"]') ?? null) !== null, "focus moves to the stopped row's title");
        expect(focused()?.classList.contains('tm-attention-row__title')).toBe(true);
        expect((await bell.getJobs())[0]?.controls).toEqual([]);
        expect(rig.attention.open()).toBe('bell');
      });

      it(`moves focus to the next row when Delete clears a row, and to the heading when the list empties, at ${width} px`, async () => {
        const rig = await setUpAt(width);
        await rig.put('inbox', many(2));
        rig.api.respond('inbox/clear', () => new Promise(() => undefined));
        const announce = vi.spyOn(TestBed.inject(LiveAnnouncer), 'announce');
        const bell = await openBell(rig);
        await tmSettle();
        expect(focused()?.closest('[data-tm-id="2"]')).not.toBeNull();

        // Delete clears the focused row, announces it, and focus moves to the row now in its place.
        press('Delete');
        await tmSettle();
        expect((await bell.getNotifications()).map((row) => row.id)).toEqual([1]);
        expect(announce).toHaveBeenCalledWith('Notification cleared', 'polite');
        expect(focused()?.closest('[data-tm-id="1"]')).not.toBeNull();

        // Clearing the last row leaves the empty state: focus moves to the heading, and the panel stays open.
        press('Delete');
        await tmSettle();
        expect(await bell.getEmptyText()).toBe("You're all caught up");
        expect(focused()).toBe(fallbackOf('bell', width));
        expect(rig.attention.open()).toBe('bell');
        await tmSettleUntil(() => callsTo(rig, 'inbox/clear').length === 2, 'both clears are sent');
        expect(callsTo(rig, 'inbox/clear').map((call) => call.body)).toEqual([{ ids: [2] }, { ids: [1] }]);
      });
    }

    it('moves focus to the row in its place when a copy withdraws the focused row, and shows a replaced row at the top, unread', async () => {
      const rig = await setUp();
      await rig.put('inbox', many(3));
      const bell = await openBell(rig);
      await tmSettle();
      panelOf('bell').querySelector<HTMLElement>('[data-tm-id="2"] .tm-attention-panel__link')!.focus();

      // A copy without row 2: focus moves to row 1, now in its place.
      const withdrawn = many(3, 6);
      await rig.put('inbox', { ...withdrawn, latest: withdrawn.latest.filter((row) => row['id'] !== 2) });
      await tmSettle();
      expect((await bell.getNotifications()).map((row) => row.id)).toEqual([3, 1]);
      expect(focused()?.closest('[data-tm-id="1"]')).not.toBeNull();

      // A copy that replaces row 1 with a newer one, unread: it moves to the top.
      const replaced = many(3, 7);
      await rig.put('inbox', {
        ...replaced,
        latest: [tmTestNotification(1, { createdAt: '2026-11-23T10:30:00.000Z' }), replaced.latest[0]!],
      });
      await tmSettle();
      const rows = await bell.getNotifications();
      expect(rows.map((row) => [row.id, row.unread])).toEqual([
        [1, true],
        [3, true],
      ]);
    });

    it('holds rows that arrive while the list is scrolled down under a pinned button, announcing them, and shows them on its press', async () => {
      const rig = await setUp();
      await rig.put('inbox', many(10));
      const announce = vi.spyOn(TestBed.inject(LiveAnnouncer), 'announce');
      const bell = await openBell(rig);
      await tmSettle();
      const body = panelOf('bell').querySelector<HTMLElement>('.tm-attention-panel__body')!;
      expect(body.scrollHeight).toBeGreaterThan(body.clientHeight);
      body.scrollTop = 40;
      body.dispatchEvent(new Event('scroll'));
      await tmSettle();

      // A newer copy with row 11: it waits, nothing moves, and it is announced.
      const arrived = many(11, 6);
      await rig.put('inbox', { ...arrived, latest: [tmTestNotification(11, { createdAt: '2026-11-23T10:00:00.000Z' }), ...many(10).latest] });
      await tmSettle();
      expect(await bell.getFresh()).toBe('1 new');
      expect((await bell.getNotifications()).map((row) => row.id)).not.toContain(11);
      expect(announce).toHaveBeenCalledWith('1 new notification', 'polite');

      // The pinned button scrolls to the top, where the row inserts, and focuses the first row.
      await bell.showFresh();
      await tmSettle();
      expect(await bell.getFresh()).toBeNull();
      expect((await bell.getNotifications())[0]?.id).toBe(11);
      expect(focused()?.closest('[data-tm-id="11"]')).not.toBeNull();
    });

    it("announces no arrival for the first copy's rows when it opened on skeletons", async () => {
      const rig = await setUp();
      const announce = vi.spyOn(TestBed.inject(LiveAnnouncer), 'announce');
      const bell = await openBell(rig);
      expect(await bell.isLoading()).toBe(true);
      await rig.put('inbox', many(2));
      await tmSettle();
      expect((await bell.getNotifications()).map((row) => row.id)).toEqual([2, 1]);
      expect(announce.mock.calls.map(([message]) => message)).not.toContain('2 new notifications');
    });

    it('asks before Clear read notifications, and clears them once confirmed', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox());
      rig.api.respond('inbox/clear-read', { count: 1, version: tmVersion(6) });
      const bell = await openBell(rig);

      await (await bell.openMore()).clickItem('Clear read notifications');
      const confirm = await confirmOf(rig);
      expect(await confirm.getTitle()).toBe('Clear read notifications?');
      expect(await confirm.getMessage()).toBe('They are deleted for you.');
      expect(await confirm.getChoiceLabels()).toEqual(['Cancel', 'Clear']);
      await confirm.choose('Clear');
      await tmSettleUntil(() => callsTo(rig, 'inbox/clear-read').length === 1, 'the clear-read is sent');
      expect((await bell.getNotifications()).map((row) => row.id)).toEqual([3]);
    });

    it("reviews a job that waits for confirmation on the record that owns it, closing the panel at once", async () => {
      const handlers = [
        { key: 'core.export', schedulable: false, commitsInSteps: false, labelKey: 'JobHandler_core_export', labelFallback: 'core.export' },
        { key: 'gl.center.post', schedulable: false, commitsInSteps: false, labelKey: 'JobHandler_gl_center_post', result: { entity: 'gl.Center', via: 'Job' } },
      ];
      const rig = await setUp({}, { jobHandlers: handlers });
      await rig.put('inbox', inbox([job(4, 'PendingConfirmation', 50, { handlerKey: 'gl.center.post' }), job(5, 'PendingConfirmation', 40)]));
      // The owner's lookup waits for the test's answer; the centers page's own list reads none.
      let answer: (value: unknown) => void = () => undefined;
      const empty = { rows: { columns: [], rows: [] }, count: 0 };
      rig.api.respond('centers/query', (call: { readonly body: unknown }) =>
        (call.body as { readonly filters?: unknown }).filters === undefined ? empty : new Promise((resolve) => (answer = resolve)),
      );
      const lookups = (): readonly TmFakeApiCall[] =>
        callsTo(rig, 'centers/query').filter((call) => (call.body as { readonly filters?: unknown }).filters !== undefined);
      let bell = await openBell(rig);

      // The owner's read runs after the panel closed; its row's page opens.
      await bell.reviewJob(4);
      await tmSettle();
      expect(rig.attention.open()).toBeNull();
      expect(lookups().map((call) => call.body)).toEqual([{ select: 'Id', filters: ['Job = 4'], take: 1 }]);
      answer({ rows: { columns: [{ name: 'Id', type: 'Numeric', nullable: false, groupingKey: false }], rows: [[23]] } });
      await tmSettleUntil(() => rig.router.url === '/17/centers/23' && rig.router.currentNavigation() === null, "the owner's page opens");
      // The page takes focus as it lands, which would close a panel opened meanwhile.
      await tmSettle();

      // A user who moves on while the owner is read stays where they went.
      bell = await openBell(rig);
      await bell.reviewJob(4);
      await tmSettleUntil(() => lookups().length === 2, "the owner's second lookup is sent");
      await rig.router.navigateByUrl('/17/centers/24');
      answer({ rows: { columns: [{ name: 'Id', type: 'Numeric', nullable: false, groupingKey: false }], rows: [[23]] } });
      await tmSettle();
      expect(rig.router.url).toBe('/17/centers/24');

      // A handler with no result entity: the job's page opens at once.
      bell = await openBell(rig);
      await bell.reviewJob(5);
      await tmSettleUntil(() => rig.router.url === '/17/jobs/5', "the job's page opens");
      expect(rig.attention.open()).toBeNull();
    });

    it('publishes its overlay page context while open: its rows, its state and its three actions, Stop asking first', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox([job(2, 'Running', 40, { handlerKey: 'core.import' })]));
      rig.api.respond('jobs/cancel', {});
      await openBell(rig);
      const contexts = TestBed.inject(TmPageContexts);
      const context = contexts.top()!;
      expect(context).toMatchObject({ kind: 'custom', routeTemplate: 'panel:bell', overlay: true });
      expect(context.title()).toBe('Notifications');
      expect(context.status()).toBe('ready');
      expect(context.state()).toEqual({
        unseen: 0,
        unread: 1,
        live: 'live',
        jobs: [{ id: 2, handlerKey: 'core.import', status: 'Running', stopping: false }],
      });
      expect(context.visible?.()?.rows).toEqual([
        { id: 3, cells: { Type: 'core.export.ready', Title: 'Export is ready', State: 'unread' } },
        { id: 2, cells: { Type: 'gl.center.flagged', Title: 'HQ was flagged', State: 'read' } },
      ]);
      expect(context.actions().map((action) => action.id)).toEqual(
        expect.arrayContaining(['attention.open-item', 'attention.read-all', 'attention.stop-job']),
      );

      // Stop through the dispatcher asks in an import's words, whoever invokes it.
      const actions = TestBed.inject(TmPageActions);
      const stopped = actions.invoke('attention.stop-job', { id: 2 }, 'assistant');
      const confirm = await confirmOf(rig);
      expect(await confirm.getTitle()).toBe('Stop this import?');
      await confirm.choose('Stop import');
      expect(await stopped).toEqual({ outcome: 'done' });

      // Open item opens the row and closes the panel, which takes its context with it.
      expect(await actions.invoke('attention.open-item', { id: 3 }, 'assistant')).toEqual({ outcome: 'done' });
      await tmSettleUntil(() => rig.attention.open() === null, 'the panel closes');
      expect(rig.router.url).toBe('/17/notifications/3/open');
      expect(contexts.overlays()).toEqual([]);
    });

    it("adds the attention section to the support dump", async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox([job(1, 'Running', 50)]));
      await openBell(rig);
      const section = TestBed.inject(TmSupport)
        .ɵsections()
        .find((candidate) => candidate.name === 'attention');
      expect(section?.read()).toEqual({
        open: 'bell',
        bell: { count: 0, tone: 'accent', jobs: 1 },
        tray: { count: null, tone: 'neutral' },
        banners: [],
      });
    });

    it('closes on a click outside the popover, and toggles on Alt+Shift+U and Alt+Shift+Q', async () => {
      const rig = await setUp();
      await rig.put('inbox', inbox());
      await openBell(rig);
      document.querySelector<HTMLElement>('main')!.click();
      await tmSettleUntil(() => rig.attention.open() === null, 'the panel closes');

      const shortcut = (code: string, key: string): void =>
        void document.body.dispatchEvent(new KeyboardEvent('keydown', { key, code, altKey: true, shiftKey: true, bubbles: true, cancelable: true }));
      shortcut('KeyU', 'U');
      await tmSettleUntil(() => document.querySelector('tm-bell-panel') !== null, "the bell's panel opens");
      expect(rig.attention.open()).toBe('bell');
      shortcut('KeyQ', 'Q');
      await tmSettleUntil(() => document.querySelector('tm-tray-panel') !== null, "the tray's panel opens");
      expect(rig.attention.open()).toBe('tray');
      shortcut('KeyQ', 'Q');
      await tmSettleUntil(() => rig.attention.open() === null, 'the tray closes');
    });

    it("closes the panel again when its chunk cannot load, so the next press tries again", async () => {
      let fail = true;
      const load = (): Promise<typeof import('./tm-attention-panels')> => (fail ? Promise.reject(new Error('The chunk is gone.')) : import('./tm-attention-panels'));
      const rig = await tmAttentionRig({}, {}, [{ provide: TM_ATTENTION_PANELS_LOAD, useValue: load }]);
      const attention = TestBed.inject(TmAttention);
      await tmSettle();
      button('bell').click();
      await tmSettleUntil(() => attention.open() === null && rig.telemetry.recent().some((item) => item.kind === 'error'), 'the press closes again');
      expect(button('bell').getAttribute('aria-expanded')).toBe('false');

      fail = false;
      button('bell').click();
      await tmSettleUntil(() => document.querySelector('tm-bell-panel') !== null, "the bell's panel opens");
      expect(attention.open()).toBe('bell');
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

    it('marks every row New, and reports it so, for a user who never opened the tray', async () => {
      const rig = await setUp();
      await rig.put(
        'assignments',
        tmFakeAssignmentsSummary({
          open: 2,
          new: 2,
          latest: [tmTestAssignment(1, { captionJson: JSON.stringify({ code: 'HQ-01' }) }), tmTestAssignment(2, { targetId: '24' })],
        }),
      );
      const tray = await openTray(rig);
      expect((await tray.getRows()).map((row) => row.new)).toEqual([true, true]);
      expect(
        TestBed.inject(TmPageContexts)
          .top()
          ?.visible?.()
          ?.rows.map((row) => row.cells['State']),
      ).toEqual(['new', 'new']);
    });

    it('marks rows New against the first copy it shows when it opened on skeletons', async () => {
      const rig = await setUp();
      const tray = await openTray(rig);
      expect(await tray.isLoading()).toBe(true);
      await rig.put(
        'assignments',
        tmFakeAssignmentsSummary({
          open: 2,
          new: 1,
          seenAt: '2026-11-23T08:30:00.000Z',
          latest: [tmTestAssignment(1, { assignedAt: '2026-11-23T09:00:00.000Z' }), tmTestAssignment(2, { targetId: '24', assignedAt: '2026-11-23T08:00:00.000Z' })],
        }),
      );
      await tmSettle();
      expect((await tray.getRows()).map((row) => row.new)).toEqual([true, false]);
    });

    it('publishes its overlay page context, whose Open opens a row', async () => {
      const rig = await setUp();
      await rig.put('assignments', tmFakeAssignmentsSummary({ open: 1, new: 0, seenAt: '2026-11-23T10:00:00.000Z', latest: [tmTestAssignment(1)] }));
      await openTray(rig);
      const context = TestBed.inject(TmPageContexts).top()!;
      expect(context).toMatchObject({ kind: 'custom', routeTemplate: 'panel:tray', overlay: true });
      expect(context.state()).toEqual({ open: 1, new: 0 });
      expect(context.visible?.()?.rows).toEqual([{ id: 1, cells: expect.objectContaining({ Resource: 'gl.Center', State: 'open' }) }]);
      const actions = TestBed.inject(TmPageActions);
      expect(await actions.invoke('attention.open-item', { id: 9 }, 'assistant')).toMatchObject({ outcome: 'failed' });
      expect(await actions.invoke('attention.open-item', { id: 1 }, 'assistant')).toEqual({ outcome: 'done' });
      await tmSettleUntil(() => rig.attention.open() === null, 'the panel closes');
      expect(rig.router.url).toBe('/17/centers/23');
    });

    it('meets the accessibility rules with its rows and their New marks', async () => {
      const rig = await setUp();
      await rig.put(
        'assignments',
        tmFakeAssignmentsSummary({
          open: 2,
          new: 1,
          seenAt: '2026-11-23T08:30:00.000Z',
          latest: [
            tmTestAssignment(1, { captionJson: JSON.stringify({ code: 'HQ-01' }), assignedAt: '2026-11-23T09:00:00.000Z', comment: 'Please review' }),
            tmTestAssignment(2, { targetId: '24', captionJson: JSON.stringify({ code: 'HQ-02' }), assignedAt: '2026-11-23T08:00:00.000Z' }),
          ],
        }),
      );
      const tray = await openTray(rig);
      expect((await tray.getRows()).map((row) => row.new)).toEqual([true, false]);
      const results = await axe.run(panelOf('tray'), { runOnly: { type: 'tag', values: WCAG } });
      expect(results.violations.map((violation) => violation.id)).toEqual([]);
    });

    for (const width of WIDTHS) {
      it(`moves focus to the heading when its last row leaves, and stays open, at ${width} px`, async () => {
        const rig = await setUpAt(width);
        await rig.put('assignments', tmFakeAssignmentsSummary({ open: 1, new: 0, latest: [tmTestAssignment(1)] }));
        await openTray(rig);
        await tmSettle();
        expect(focused()?.closest('[data-tm-id="1"]')).not.toBeNull();
        await rig.put('assignments', tmFakeAssignmentsSummary());
        await tmSettle();
        expect(focused()).toBe(fallbackOf('tray', width));
        expect(rig.attention.open()).toBe('tray');
      });
    }
  });
});
