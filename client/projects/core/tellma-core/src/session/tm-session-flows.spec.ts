// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Location } from '@angular/common';
import { Injectable, type Provider } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TmClientCache, type TmClientCacheArea } from '@tellma/core-ui';
import { TmBusyOverlay } from '@tellma/core-ui/busy-overlay';
import { TmConfirm, type TmConfirmYesNoOptions } from '@tellma/core-ui/confirm';
import { TmToasts } from '@tellma/core-ui/toast';
import { TmFakeClientCache, TmFakeSession } from '@tellma/core-testing';

import { TmApiError } from '../api/tm-api-error';
import { TM_NOW } from '../app/tm-now';
import { TM_BOOT_RESULT } from '../boot/tm-boot-result';
import type { TmBootResult } from '../boot/tm-boot-types';
import { TmLocalization } from '../l10n/tm-localization';
import { tmAppRig, tmNavigate, tmSettle, tmSettleUntil, tmWaitUntil, TM_T17, TM_TEST_SUB, type TmAppRig } from '../routes/tm-app.testing.util';
import type { TmBusMessage } from '../state/bus/tm-bus-messages';
import { TmTabBus } from '../state/bus/tm-tab-bus';
import { TmRecordingTabBus } from '../state/bus/tm-tab-bus.testing.util';
import { TM_DRAFT_KEY_PREFIX, TM_DRAFTS_STORAGE, tmDraftPrefix, tmStorageKeys } from '../state/memory/tm-draft-keys';
import { TM_PROFILE_COOKIE_DELETE, TmSessionPaths } from './tm-session-paths';
import { TM_SIGN_OUT_WAIT_MS, TM_SIGNING_OUT_LIFT_MS, TmSessionFlows } from './tm-session-flows';
import { tmProfileCookies } from './tm-profile.testing.util';
import { TM_SESSION_HOST, type TmSessionStorage } from './tm-session-host';
import { TmFakeSessionHost } from './tm-session.testing.util';

// The session's flows in a whole test app, signed in as the test user. The
// user's other tabs are scripted on the tab bus: each answers a drafts query
// with what the spec sets, and the spec plays their session messages. The
// clean-up sweeps the testing providers' client cache, whose stores are kept
// in memory; one spec at the end runs it over the browser's own stores.

/** The test user's other tabs: what each answers a drafts query with. */
interface TmOtherTab {
  dirty: number;
  busy: boolean;
}

/** A tab bus whose other tabs answer drafts queries as the spec scripts them. */
@Injectable()
class TmScriptedTabBus extends TmRecordingTabBus {
  /** The other tabs. */
  readonly tabs: TmOtherTab[] = [];

  /** Records a post; the other tabs answer a drafts query a task later. */
  override post(message: TmBusMessage): void {
    super.post(message);
    if (message.type === 'drafts.query') {
      for (const tab of this.tabs) {
        const reply: TmBusMessage = { type: 'drafts.reply', replyTo: message.replyTo, dirty: tab.dirty, busy: tab.busy };
        setTimeout(() => this.receive(reply));
      }
    }
  }

  /** The session messages and drafts queries and replies posted, oldest first. */
  session(): TmBusMessage[] {
    return this.posted.filter((posted) => posted.type.startsWith('session.') || posted.type.startsWith('drafts.'));
  }

  /** The types of the session messages and drafts queries and replies posted, oldest first. */
  types(): string[] {
    return this.session().map((posted) => posted.type);
  }
}

/** The confirm, recorded, answering what the spec sets. */
class TmRecordingConfirm {
  /** What each confirm asked. */
  readonly asked: TmConfirmYesNoOptions[] = [];
  /** The user's answer. */
  answer = true;

  /** Records the question and answers. */
  confirm(options: TmConfirmYesNoOptions): Promise<boolean> {
    this.asked.push(options);
    return Promise.resolve(this.answer);
  }
}

/** The pieces a spec drives. */
interface TmFlowsRig extends TmAppRig {
  readonly host: TmFakeSessionHost;
  readonly bus: TmScriptedTabBus;
  readonly confirm: TmRecordingConfirm;
  readonly paths: TmSessionPaths;
  readonly flows: TmSessionFlows;
  readonly drafts: TmSessionStorage;
  readonly cache: TmFakeClientCache;
}

/** How a spec sets the test app up. */
interface TmFlowsOptions {
  /** The boot script's result, when the spec sets one. */
  readonly boot?: Partial<TmBootResult>;
  /** More providers, after the rig's. */
  readonly providers?: readonly Provider[];
}

/** Starts the test app signed in as the test user, with the flows loaded. */
async function setUp(options: TmFlowsOptions = {}): Promise<TmFlowsRig> {
  const host = new TmFakeSessionHost();
  host.cookieJar = tmProfileCookies(TM_TEST_SUB, 'en');
  const confirm = new TmRecordingConfirm();
  const rig = await tmAppRig({
    providers: [
      { provide: TM_SESSION_HOST, useValue: host },
      TmScriptedTabBus,
      { provide: TmTabBus, useExisting: TmScriptedTabBus },
      { provide: TmConfirm, useValue: confirm },
      // Read through the global, so a spec's fake clock moves it.
      { provide: TM_NOW, useValue: () => Date.now() },
      ...(options.boot === undefined ? [] : [{ provide: TM_BOOT_RESULT, useValue: options.boot }]),
      ...(options.providers ?? []),
    ],
  });
  // The first render loads the flows' chunk, as the app's does.
  await tmNavigate('/17/centers');
  // The navigation's own time is no wait for the chunk: once the shell is
  // loaded, a route activates in the same task. So this waits for the flows
  // themselves, which register their strings as they start, before a spec
  // fakes the clock, which would not move a load still running.
  const l10n = TestBed.inject(TmLocalization);
  await tmSettleUntil(() => l10n.ɵhas('tm.session.signingOut'), 'the session flows loaded');
  return {
    ...rig,
    host,
    bus: TestBed.inject(TmScriptedTabBus),
    confirm,
    paths: TestBed.inject(TmSessionPaths),
    flows: TestBed.inject(TmSessionFlows),
    drafts: TestBed.inject(TM_DRAFTS_STORAGE)!,
    cache: TestBed.inject(TmFakeClientCache),
  };
}

/** The names and properties of the session events recorded. */
function sessionEvents(rig: TmAppRig): { name: string; props: object }[] {
  return rig.telemetry
    .recent()
    .filter((item) => item.name.startsWith('tm.session.'))
    .map((item) => ({ name: item.name, props: item.props }));
}

/** The session-ended sweeps the store ran: its own sweep of other sessions' data aside. */
function sessionSweeps(sweep: { readonly mock: { readonly calls: unknown[][] } }): unknown[][] {
  return sweep.mock.calls.filter((call) => call[0] === 'session-ended');
}

/** Stores a draft of the user's. */
function draft(rig: TmFlowsRig, tail: string, sub = TM_TEST_SUB): void {
  rig.drafts.setItem(`${tmDraftPrefix(sub)}${tail}`, '{"v":1}');
}

/** How many drafts session storage holds, of any user. */
function drafts(rig: TmFlowsRig): number {
  return tmStorageKeys(rig.drafts, TM_DRAFT_KEY_PREFIX).length;
}

/** A draft key of the test user's, as session storage holds it. */
const DRAFT_KEY = `${tmDraftPrefix(TM_TEST_SUB)}17:gl.Center:5`;

/**
 * Gives the client cache a store of each kind the clean-up meets: the
 * images cache and the service worker's, the store's database and a
 * feature's, the boot record and a feature's key, and in session storage
 * the sign-in's key and a draft.
 */
function seedStores(rig: TmFlowsRig): void {
  rig.cache.add('cache-storage', 'tm-images-v1', 'ngsw:/:db:control');
  rig.cache.add('indexeddb', 'tm-app', 'tm-feature');
  rig.cache.add('local-storage', 'tm.boot', 'tm.feature');
  rig.cache.add('session-storage', 'tm.login', DRAFT_KEY);
}

/** What the client cache's stores hold now, by area. */
function stores(rig: TmFlowsRig): Record<TmClientCacheArea, readonly string[]> {
  return {
    'cache-storage': rig.cache.names('cache-storage'),
    indexeddb: rig.cache.names('indexeddb'),
    'local-storage': rig.cache.names('local-storage'),
    'session-storage': rig.cache.names('session-storage'),
  };
}

/** What a sign-out's clean-up leaves of the seeded stores: the store's database, the boot record and the service worker's cache. */
const SIGNED_OUT_STORES: Record<TmClientCacheArea, readonly string[]> = {
  'cache-storage': ['ngsw:/:db:control'],
  indexeddb: ['tm-app'],
  'local-storage': ['tm.boot'],
  'session-storage': [],
};

/** The open session-ended dialog, or `null`. */
function endedDialog(): HTMLElement | null {
  return document.querySelector<HTMLElement>('[role="alertdialog"]');
}

describe('TmSessionFlows', () => {
  // A path goes through chunks loaded on demand (the flows, the dialogs), so
  // each spec waits on the state the path produces, by a named condition.

  afterEach(() => {
    vi.useRealTimers();
  });

  describe('the expiry path', () => {
    it("takes the user's data off the tab, keeps the drafts and the last tenant, and tells the other tabs", async () => {
      const rig = await setUp();
      rig.store.boot.patch({ sub: TM_TEST_SUB, lastTenantId: 17 });
      draft(rig, '17:gl.Center:5');
      seedStores(rig);
      const sweep = vi.spyOn(rig.store, 'sweep');
      rig.paths.expired('401');
      expect(rig.paths.state()).toBe('expired');
      await tmSettleUntil(() => rig.bus.types().includes('session.expired'), 'the other tabs were told of the expiry');
      expect(sessionSweeps(sweep)).toEqual([['session-ended']]);
      expect(rig.store.doc(TM_T17, 'settings')()).toBeUndefined();
      expect(rig.host.cookieWrites).toEqual([TM_PROFILE_COOKIE_DELETE]);
      expect(rig.bus.session().at(-1)).toEqual({ type: 'session.expired', sub: TM_TEST_SUB });
      expect(drafts(rig)).toBe(1);
      // The browser's stores lose all but the store's database, the boot record and the drafts.
      expect(rig.cache.sweeps()).toHaveLength(1);
      expect(stores(rig)).toEqual({ ...SIGNED_OUT_STORES, 'session-storage': [DRAFT_KEY] });
      expect(rig.store.boot.read()).toMatchObject({ sub: TM_TEST_SUB, lastTenantId: 17 });
      expect(sessionEvents(rig)).toEqual([{ name: 'tm.session.expired', props: { source: '401' } }]);
    });

    it('shows the session-ended alert dialog, focused on Sign in, which only Sign in leaves', async () => {
      const rig = await setUp();
      rig.paths.expired('cookie');
      await tmSettleUntil(() => endedDialog() !== null, 'the session-ended dialog opened');
      const dialog = endedDialog()!;
      expect(dialog.getAttribute('aria-label') ?? dialog.querySelector('h2')?.textContent).toContain('Your session ended');
      expect(document.getElementById(dialog.getAttribute('aria-describedby') ?? '')?.textContent).toBe(
        'Sign in to continue. Your work is kept.',
      );
      const button = dialog.querySelector<HTMLButtonElement>('button')!;
      await tmSettleUntil(() => document.activeElement === button, 'Sign in has the focus');
      expect(button.textContent?.trim()).toBe('Sign in');
      // Escape does nothing, and there is no close button.
      button.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
      await new Promise((resolve) => setTimeout(resolve, 50));
      expect(endedDialog()).not.toBeNull();
      expect(dialog.querySelectorAll('button')).toHaveLength(1);
      button.click();
      await tmSettleUntil(() => TestBed.inject(TmFakeSession).signIns().length > 0, 'a sign-in started');
      expect(TestBed.inject(TmFakeSession).signIns()).toEqual([{}]);
    });

    it('keeps the session-ended dialog open when the user goes Back', async () => {
      const rig = await setUp();
      await rig.router.navigateByUrl('/17/centers?page=2');
      await tmSettle();
      rig.paths.expired('cookie');
      await tmSettleUntil(() => endedDialog() !== null, 'the session-ended dialog opened');
      TestBed.inject(Location).back();
      await tmSettle();
      await new Promise((resolve) => setTimeout(resolve, 50));
      const dialog = endedDialog();
      expect(dialog).not.toBeNull();
      expect(document.activeElement).toBe(dialog!.querySelector('button'));
    });

    it('lets the next launch sweep when the sweep runs past its time box, fails, or cannot reach the stored data', async () => {
      const sweeps = [
        () => new Promise<boolean>(() => undefined),
        () => Promise.reject(new DOMException('Aborted.', 'AbortError')),
        () => Promise.resolve(false),
      ];
      for (const sweep of sweeps) {
        const rig = await setUp();
        rig.store.boot.patch({ sub: TM_TEST_SUB, lastTenantId: 17 });
        vi.spyOn(rig.store, 'sweep').mockImplementation(sweep);
        rig.paths.expired('cookie');
        await tmSettleUntil(() => rig.host.cookieWrites.length > 0, 'the profile cookie was deleted');
        expect(rig.host.cookieWrites).toHaveLength(1);
        expect(rig.store.boot.read()).toMatchObject({ sub: null, lastTenantId: 17 });
        TestBed.resetTestingModule();
      }
    });

    it('shows the sign-in-failed page within a minute of a sign-in, whose Try again signs in after the clean-up', async () => {
      let retry: (() => void) | undefined;
      const showPage = vi.fn((_reason: string, again?: () => void) => void (retry = again));
      const mark = { at: Date.now() - 30_000, expired: false, returnUrl: '/17', auto: false, sub: null, hash: '' };
      const rig = await setUp({ boot: { mark, showPage, profile: null, cold: false, auto: null } });
      rig.paths.expired('401');
      await tmSettleUntil(() => showPage.mock.calls.length > 0, 'the boot page showed');
      expect(showPage).toHaveBeenCalledWith('sign-in-failed', expect.any(Function));
      expect(endedDialog()).toBeNull();
      retry?.();
      await tmSettleUntil(() => TestBed.inject(TmFakeSession).signIns().length > 0, 'a sign-in started');
      expect(TestBed.inject(TmFakeSession).signIns()).toEqual([{}]);
      expect(rig.host.cookieWrites).toEqual([TM_PROFILE_COOKIE_DELETE]);
    });

    it("shows another tab's expiry without cleaning up again", async () => {
      const rig = await setUp();
      draft(rig, '17:gl.Center:5');
      const sweep = vi.spyOn(rig.store, 'sweep');
      rig.bus.receive({ type: 'session.expired', sub: TM_TEST_SUB });
      await tmSettleUntil(() => endedDialog() !== null, 'the session-ended dialog opened');
      expect(rig.paths.state()).toBe('expired');
      expect([sessionSweeps(sweep), rig.cache.sweeps(), rig.host.cookieWrites, drafts(rig)]).toEqual([[], [], [], 1]);
    });

    it('reloads a tab that saw the session end once the user signs in again elsewhere', async () => {
      const rig = await setUp();
      rig.bus.receive({ type: 'session.started', sub: TM_TEST_SUB });
      await new Promise((resolve) => setTimeout(resolve));
      expect(rig.host.reloads).toBe(0);
      rig.bus.receive({ type: 'session.expired', sub: TM_TEST_SUB });
      await tmSettleUntil(() => rig.paths.state() === 'expired', 'the session expired');
      rig.bus.receive({ type: 'session.started', sub: TM_TEST_SUB });
      await tmSettleUntil(() => rig.host.reloads > 0, 'the tab reloaded');
      expect(rig.host.reloads).toBe(1);
    });
  });

  describe('the changed-user path', () => {
    it("drops the previous user's drafts, tells the other tabs of the new user, and restarts at the root, once", async () => {
      const rig = await setUp();
      draft(rig, '17:gl.Center:5');
      draft(rig, '17:gl.Center:6', 'someone-else');
      rig.host.cookieJar = tmProfileCookies('new-user', 'en');
      rig.flows.changedUser();
      rig.flows.changedUser();
      expect(rig.host.replaced).toEqual(['/']);
      expect(rig.bus.session()).toEqual([{ type: 'session.changed', sub: 'new-user' }]);
      expect(drafts(rig)).toBe(1);
      expect(sessionEvents(rig).map((event) => event.name)).toEqual(['tm.session.changed-user']);
    });

    it("follows another tab's announcement of another user, and ignores its own user's", async () => {
      const rig = await setUp();
      rig.bus.receive({ type: 'session.changed', sub: TM_TEST_SUB });
      await new Promise((resolve) => setTimeout(resolve));
      expect(rig.host.replaced).toEqual([]);
      rig.bus.receive({ type: 'session.started', sub: 'new-user' });
      await tmSettleUntil(() => rig.host.replaced.length > 0, 'the app restarted');
      expect(rig.host.replaced).toEqual(['/']);
    });
  });

  describe('drafts queries', () => {
    it('answers with the unsaved drafts in session storage before the drafts service exists, and whether a save runs', async () => {
      const rig = await setUp();
      draft(rig, '17:gl.Center:5');
      draft(rig, '17:gl.Center:6');
      draft(rig, '17:gl.Center:7', 'someone-else');
      rig.api.respond('centers/save', () => new Promise(() => undefined));
      void rig.api.write('centers/save', {});
      rig.bus.receive({ type: 'drafts.query', replyTo: 'q1' });
      await tmSettleUntil(() => rig.bus.session().length > 0, 'the drafts query was answered');
      expect(rig.bus.session()).toEqual([{ type: 'drafts.reply', replyTo: 'q1', dirty: 2, busy: true }]);
    });

    it('answers with the drafts service once it exists', async () => {
      const rig = await setUp();
      const { TmDrafts } = await import('@tellma/core/drafts');
      const service = TestBed.inject(TmDrafts);
      service.save(service.key(17, 'gl.Center', 5), { name: 'Unsaved' });
      rig.bus.receive({ type: 'drafts.query', replyTo: 'q2' });
      await tmSettleUntil(() => rig.bus.session().length > 0, 'the drafts query was answered');
      expect(rig.bus.session()).toEqual([{ type: 'drafts.reply', replyTo: 'q2', dirty: service.dirtyCount(), busy: false }]);
      expect(service.dirtyCount()).toBeGreaterThan(0);
    });
  });

  describe('sign-out', () => {
    it('signs out with no question when nothing is at risk, every tab following', async () => {
      const rig = await setUp();
      rig.bus.tabs.push({ dirty: 0, busy: false });
      rig.store.boot.patch({ sub: TM_TEST_SUB, lastTenantId: 17 });
      rig.api.respond('/bff/logout', { redirectUrl: 'https://id.example/connect/endsession?id_token_hint=x' });
      seedStores(rig);
      const overlay = vi.spyOn(TestBed.inject(TmBusyOverlay), 'run');
      const sweep = vi.spyOn(rig.store, 'sweep');
      let settled = false;
      void rig.paths.ɵsignOut().finally(() => (settled = true));
      await tmSettleUntil(() => rig.host.assigned.length > 0, 'the tab left for the sign-out');
      expect(rig.host.assigned).toEqual(['https://id.example/connect/endsession?id_token_hint=x']);
      expect(settled).toBe(false);
      expect(rig.confirm.asked).toEqual([]);
      // The overlay shows at once, labelled "Signing out…".
      expect(overlay.mock.calls[0]?.[1]).toMatchObject({ immediate: true });
      expect((overlay.mock.calls[0]?.[1]?.label as () => string)()).toBe('Signing out…');
      expect(rig.bus.types()).toEqual(['drafts.query', 'session.signing-out', 'session.signed-out']);
      expect(rig.paths.state()).toBe('signing-out');
      const logout = rig.api.calls().find((call) => call.route === '/bff/logout');
      expect(logout).toMatchObject({ kind: 'write', body: { scope: 'global' } });
      expect(sessionSweeps(sweep)).toEqual([['session-ended']]);
      expect(stores(rig)).toEqual(SIGNED_OUT_STORES);
      expect(rig.store.boot.read()).toMatchObject({ sub: null, lastTenantId: null });
      expect(sessionEvents(rig)).toEqual([{ name: 'tm.session.signed-out', props: { origin: 'self' } }]);
    });

    it('asks once about the unsaved changes of every tab, and stays signed in on Cancel', async () => {
      const rig = await setUp();
      draft(rig, '17:gl.Center:5');
      rig.bus.tabs.push({ dirty: 3, busy: false }, { dirty: 0, busy: false }, { dirty: 1, busy: false });
      rig.confirm.answer = false;
      await rig.paths.ɵsignOut();
      expect(rig.confirm.asked).toEqual([
        {
          title: 'Sign out and discard unsaved changes?',
          message: 'You have unsaved changes in 3 tabs.',
          confirmLabel: 'Sign out',
          destructive: true,
        },
      ]);
      expect(rig.bus.types()).toEqual(['drafts.query']);
      expect(rig.paths.state()).toBe('active');
      expect(rig.api.calls().some((call) => call.route === '/bff/logout')).toBe(false);

      // Confirmed, the sign-out discards the drafts.
      rig.confirm.answer = true;
      rig.api.respond('/bff/logout', {});
      void rig.paths.ɵsignOut();
      await tmSettleUntil(() => rig.host.assigned.length > 0, 'the tab left for the signed-out page');
      expect(rig.host.assigned).toEqual(['/signed-out']);
      expect(rig.confirm.asked).toHaveLength(2);
      expect(drafts(rig)).toBe(0);
    });

    it('waits for a running save, says so, and signs out with no question once it ends', async () => {
      const rig = await setUp();
      const other = { dirty: 0, busy: true };
      rig.bus.tabs.push(other);
      rig.api.respond('/bff/logout', {});
      const overlay = vi.spyOn(TestBed.inject(TmBusyOverlay), 'run');
      void rig.paths.ɵsignOut();
      const label = (): string => (overlay.mock.calls[0]?.[1]?.label as () => string)();
      await tmSettleUntil(() => overlay.mock.calls.length > 0 && label() === 'Waiting for a save to finish…', 'the overlay says it waits for a save');
      other.busy = false;
      await tmSettleUntil(() => rig.host.assigned.length > 0, 'the tab left for the signed-out page');
      expect(rig.host.assigned).toEqual(['/signed-out']);
      expect(rig.confirm.asked).toEqual([]);
      expect(rig.bus.types().filter((type) => type === 'drafts.query').length).toBeGreaterThan(1);
    });

    it('asks after a minute of a save that never ends, and does not wait for it again', async () => {
      const rig = await setUp();
      rig.bus.tabs.push({ dirty: 0, busy: true });
      rig.api.respond('/bff/logout', {});
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
      void rig.paths.ɵsignOut();
      await vi.advanceTimersByTimeAsync(TM_SIGN_OUT_WAIT_MS - 1_000);
      expect(rig.confirm.asked).toEqual([]);
      await vi.advanceTimersByTimeAsync(2_000);
      expect(rig.confirm.asked).toEqual([
        expect.objectContaining({
          title: 'Sign out while a save is running?',
          message: "A save is still running. If you sign out now, you won't see whether it finished.",
        }),
      ]);
      const queries = rig.bus.types().filter((type) => type === 'drafts.query').length;
      // The confirmed sign-out leaves once its clean-up has settled. An
      // advance of any length bounds that only by a count of real tasks (one,
      // and one more per timer it fires), which a clean-up over the browser's
      // own stores would outlast. So the spec waits on the leave itself, with
      // the fake clock held where the confirm left it: a second wait for the
      // save would stop at its first query's window, and the tab would never
      // leave.
      await tmWaitUntil(() => rig.host.assigned.length > 0, () => vi.advanceTimersByTimeAsync(0), 'the tab left for the signed-out page');
      expect(rig.host.assigned).toEqual(['/signed-out']);
      expect(rig.bus.types().filter((type) => type === 'drafts.query').length).toBe(queries);
    });

    it('asks one question about both when unsaved changes remain and a save still runs after a minute', async () => {
      const rig = await setUp();
      rig.bus.tabs.push({ dirty: 2, busy: true });
      rig.confirm.answer = false;
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
      const signOut = rig.paths.ɵsignOut();
      await vi.advanceTimersByTimeAsync(TM_SIGN_OUT_WAIT_MS + 1_000);
      await signOut;
      expect(rig.confirm.asked).toEqual([
        {
          title: 'Sign out and discard unsaved changes?',
          message: "You have unsaved changes in 1 tab. A save is still running. If you sign out now, you won't see whether it finished.",
          confirmLabel: 'Sign out',
          destructive: true,
        },
      ]);
      expect(rig.paths.state()).toBe('active');
    });

    it('restores the session and says so when the sign-out fails', async () => {
      const rig = await setUp();
      const failure = new TmApiError({ kind: 'offline', code: 'outcome-unknown' });
      rig.api.respond('/bff/logout', () => Promise.reject(failure));
      const toast = vi.spyOn(TestBed.inject(TmToasts), 'show');
      await expect(rig.paths.ɵsignOut()).rejects.toBe(failure);
      expect(rig.paths.state()).toBe('active');
      expect(toast).toHaveBeenCalledWith({
        tone: 'error',
        title: "Couldn't sign out. Check your connection and try again.",
        duration: 'sticky',
      });
      expect(rig.host.assigned).toEqual([]);
    });

    it("shows another tab's sign-out, and lifts it after 15 s without the signed-out message", async () => {
      const rig = await setUp();
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
      const overlay = vi.spyOn(TestBed.inject(TmBusyOverlay), 'run');
      rig.bus.receive({ type: 'session.signing-out', sub: TM_TEST_SUB });
      await vi.advanceTimersByTimeAsync(0);
      expect(rig.paths.state()).toBe('signing-out');
      expect(overlay).toHaveBeenCalledOnce();
      const lifted = overlay.mock.results[0]?.value as Promise<unknown>;
      await vi.advanceTimersByTimeAsync(TM_SIGNING_OUT_LIFT_MS);
      await lifted;
      expect(rig.paths.state()).toBe('active');
    });

    it("follows another tab's sign-out to the signed-out page, clearing its own session storage", async () => {
      const rig = await setUp();
      draft(rig, '17:gl.Center:5');
      seedStores(rig);
      const sweep = vi.spyOn(rig.store, 'sweep');
      rig.bus.receive({ type: 'session.signing-out', sub: TM_TEST_SUB });
      rig.bus.receive({ type: 'session.signed-out', sub: TM_TEST_SUB });
      await tmSettleUntil(() => rig.host.replaced.length > 0, 'the tab left for the signed-out page');
      expect(rig.host.replaced).toEqual(['/signed-out']);
      expect(drafts(rig)).toBe(0);
      expect(sessionSweeps(sweep)).toEqual([]);
      // The signing-out tab swept the shared stores; this tab sweeps its own session storage alone.
      expect(stores(rig)).toEqual({
        'cache-storage': ['tm-images-v1', 'ngsw:/:db:control'],
        indexeddb: ['tm-app', 'tm-feature'],
        'local-storage': ['tm.boot', 'tm.feature'],
        'session-storage': [],
      });
      expect(rig.host.cookieWrites).toEqual([TM_PROFILE_COOKIE_DELETE]);
      expect(sessionEvents(rig)).toEqual([{ name: 'tm.session.signed-out', props: { origin: 'other-tab' } }]);
    });

    it('signs every tab out when the session ended elsewhere: this tab cleans up for all and tells them', async () => {
      const rig = await setUp();
      rig.store.boot.patch({ sub: TM_TEST_SUB, lastTenantId: 17 });
      seedStores(rig);
      const sweep = vi.spyOn(rig.store, 'sweep');
      rig.paths.ɵsignedOutElsewhere();
      await tmSettleUntil(() => rig.host.replaced.length > 0, 'the tab left for the signed-out page');
      expect(rig.host.replaced).toEqual(['/signed-out']);
      expect(sessionSweeps(sweep)).toEqual([['session-ended']]);
      expect(stores(rig)).toEqual(SIGNED_OUT_STORES);
      expect(rig.store.boot.read()).toMatchObject({ sub: null, lastTenantId: null });
      expect(rig.bus.session()).toEqual([{ type: 'session.signed-out', sub: TM_TEST_SUB }]);
      expect(sessionEvents(rig)).toEqual([{ name: 'tm.session.signed-out', props: { origin: 'elsewhere' } }]);
    });

    it("sweeps the store, then the browser's stores, then clears the boot record and the drafts, and only then leaves", async () => {
      const rig = await setUp();
      rig.store.boot.patch({ sub: TM_TEST_SUB, lastTenantId: 17 });
      draft(rig, '17:gl.Center:5');
      rig.api.respond('/bff/logout', {});
      const sweep = vi.spyOn(rig.store, 'sweep');
      let release = (): void => undefined;
      rig.cache.clearResult = new Promise<void>((resolve) => (release = resolve));
      void rig.paths.ɵsignOut();
      await tmSettleUntil(() => rig.cache.sweeps().length > 0, 'the clean-up swept the stores');
      // Held at the browser's stores: the store's own sweep ran first; the
      // tab stays, the other tabs are not told yet, and the boot record and
      // the draft are still there.
      await tmSettle();
      expect(sessionSweeps(sweep)).toEqual([['session-ended']]);
      expect([rig.host.assigned, rig.bus.types().includes('session.signed-out'), rig.store.boot.read().sub, drafts(rig)]).toEqual([
        [],
        false,
        TM_TEST_SUB,
        1,
      ]);
      release();
      await tmSettleUntil(() => rig.host.assigned.length > 0, 'the tab left for the signed-out page');
      expect(rig.host.assigned).toEqual(['/signed-out']);
      expect(rig.store.boot.read()).toMatchObject({ sub: null, lastTenantId: null });
      expect(drafts(rig)).toBe(0);
      expect(rig.bus.types().at(-1)).toBe('session.signed-out');
    });
  });

  describe('re-checks', () => {
    it('runs the expiry path when the tab comes back from the back-forward cache without a session', async () => {
      const rig = await setUp();
      TestBed.inject(TmFakeSession).setCheck('missing');
      window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));
      expect(rig.paths.state()).toBe('expired');
      expect(sessionEvents(rig)).toEqual([{ name: 'tm.session.expired', props: { source: 'cookie' } }]);
    });

    it('runs the changed-user path when the tab comes into view under another user', async () => {
      const rig = await setUp();
      TestBed.inject(TmFakeSession).setCheck('changed');
      document.dispatchEvent(new Event('visibilitychange'));
      await tmSettleUntil(() => rig.host.replaced.length > 0, 'the app restarted');
      expect(rig.host.replaced).toEqual(['/']);
      expect(rig.bus.session()).toEqual([{ type: 'session.changed', sub: TM_TEST_SUB }]);
    });

    it("re-checks the cookie when another tab's sign-out overlay lifts", async () => {
      const rig = await setUp();
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
      rig.bus.receive({ type: 'session.signing-out', sub: TM_TEST_SUB });
      await vi.advanceTimersByTimeAsync(0);
      // The other tab's sign-out went through, but its signed-out message never came.
      TestBed.inject(TmFakeSession).setCheck('missing');
      await vi.advanceTimersByTimeAsync(TM_SIGNING_OUT_LIFT_MS);
      expect(rig.paths.state()).toBe('expired');
      expect(sessionEvents(rig)).toEqual([{ name: 'tm.session.expired', props: { source: 'cookie' } }]);
    });
  });

  describe("over the browser's own stores", () => {
    // The one spec whose clean-up runs the real TmClientCache, over this
    // test page's CacheStorage, IndexedDB and web storage. Its stores carry
    // a "test-flows" marker; the sweep also takes any tm store an earlier
    // spec left, as a real session's end would. Its answers come in real
    // time, so the spec waits on the state the path produces.
    const NAME = 'tm-test-flows';
    const KEY = 'tm.test-flows';
    const DRAFT = `${tmDraftPrefix(TM_TEST_SUB)}test-flows`;

    /** Creates an empty IndexedDB database and closes the connection. */
    function createDatabase(name: string): Promise<void> {
      return new Promise((resolve, reject) => {
        const request = indexedDB.open(name);
        request.onsuccess = () => {
          request.result.close();
          resolve();
        };
        request.onerror = () => reject(request.error);
      });
    }

    /** Deletes an IndexedDB database, settling however the delete ends. */
    function deleteDatabase(name: string): Promise<void> {
      return new Promise((resolve) => {
        const request = indexedDB.deleteDatabase(name);
        const settle = (): void => resolve();
        request.onsuccess = settle;
        request.onerror = settle;
        request.onblocked = settle;
      });
    }

    afterEach(async () => {
      await caches.delete(NAME);
      await deleteDatabase(NAME);
      localStorage.removeItem(KEY);
      sessionStorage.removeItem(KEY);
      sessionStorage.removeItem(DRAFT);
    });

    it("takes the expired user's stores off the browser, keeping the drafts", async () => {
      const rig = await setUp({ providers: [{ provide: TmClientCache, useClass: TmClientCache }] });
      await caches.open(NAME);
      await createDatabase(NAME);
      localStorage.setItem(KEY, 'value');
      sessionStorage.setItem(KEY, 'value');
      sessionStorage.setItem(DRAFT, '{"v":1}');

      rig.paths.expired('401');
      await tmSettleUntil(() => rig.bus.types().includes('session.expired'), 'the other tabs were told of the expiry');

      expect(await caches.has(NAME)).toBe(false);
      expect((await indexedDB.databases()).map((database) => database.name)).not.toContain(NAME);
      expect([localStorage.getItem(KEY), sessionStorage.getItem(KEY), sessionStorage.getItem(DRAFT)]).toEqual([null, null, '{"v":1}']);
      // The real cache ran, not the testing providers' fake.
      expect(rig.cache.sweeps()).toEqual([]);
    });
  });
});
