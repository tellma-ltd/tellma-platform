// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Location } from '@angular/common';
import { Injectable } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TmBusyOverlay } from '@tellma/core-ui/busy-overlay';
import { TmConfirm, type TmConfirmYesNoOptions } from '@tellma/core-ui/confirm';
import { TmToasts } from '@tellma/core-ui/toast';
import { TmFakeSession } from '@tellma/core-testing';

import { TmApiError } from '../api/tm-api-error';
import { TM_NOW } from '../app/tm-now';
import { TM_BOOT_RESULT } from '../boot/tm-boot-result';
import type { TmBootResult } from '../boot/tm-boot-types';
import { tmAppRig, tmNavigate, tmSettle, TM_T17, TM_TEST_SUB, type TmAppRig } from '../routes/tm-app.testing.util';
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
// with what the spec sets, and the spec plays their session messages.

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
}

/** Starts the test app signed in as the test user, with the flows loaded. */
async function setUp(options: { boot?: Partial<TmBootResult> } = {}): Promise<TmFlowsRig> {
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
    ],
  });
  // The first render loads the flows' chunk, as the app's does.
  await tmNavigate('/17/centers');
  await tmSettle();
  return {
    ...rig,
    host,
    bus: TestBed.inject(TmScriptedTabBus),
    confirm,
    paths: TestBed.inject(TmSessionPaths),
    flows: TestBed.inject(TmSessionFlows),
    drafts: TestBed.inject(TM_DRAFTS_STORAGE)!,
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

/** The open session-ended dialog, or `null`. */
function endedDialog(): HTMLElement | null {
  return document.querySelector<HTMLElement>('[role="alertdialog"]');
}

describe('TmSessionFlows', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  describe('the expiry path', () => {
    it("takes the user's data off the tab, keeps the drafts and the last tenant, and tells the other tabs", async () => {
      const rig = await setUp();
      rig.store.boot.patch({ sub: TM_TEST_SUB, lastTenantId: 17 });
      draft(rig, '17:gl.Center:5');
      const sweep = vi.spyOn(rig.store, 'sweep');
      rig.paths.expired('401');
      expect(rig.paths.state()).toBe('expired');
      await vi.waitFor(() => expect(rig.bus.types()).toContain('session.expired'));
      expect(sessionSweeps(sweep)).toEqual([['session-ended']]);
      expect(rig.store.doc(TM_T17, 'settings')()).toBeUndefined();
      expect(rig.host.cookieWrites).toEqual([TM_PROFILE_COOKIE_DELETE]);
      expect(rig.bus.session().at(-1)).toEqual({ type: 'session.expired', sub: TM_TEST_SUB });
      expect(drafts(rig)).toBe(1);
      expect(rig.store.boot.read()).toMatchObject({ sub: TM_TEST_SUB, lastTenantId: 17 });
      expect(sessionEvents(rig)).toEqual([{ name: 'tm.session.expired', props: { source: '401' } }]);
    });

    it('shows the session-ended alert dialog, focused on Sign in, which only Sign in leaves', async () => {
      const rig = await setUp();
      rig.paths.expired('cookie');
      await vi.waitFor(() => expect(endedDialog()).not.toBeNull());
      const dialog = endedDialog()!;
      expect(dialog.getAttribute('aria-label') ?? dialog.querySelector('h2')?.textContent).toContain('Your session ended');
      expect(document.getElementById(dialog.getAttribute('aria-describedby') ?? '')?.textContent).toBe(
        'Sign in to continue. Your work is kept.',
      );
      const button = dialog.querySelector<HTMLButtonElement>('button')!;
      await vi.waitFor(() => expect(document.activeElement).toBe(button));
      expect(button.textContent?.trim()).toBe('Sign in');
      // Escape does nothing, and there is no close button.
      button.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
      await new Promise((resolve) => setTimeout(resolve, 50));
      expect(endedDialog()).not.toBeNull();
      expect(dialog.querySelectorAll('button')).toHaveLength(1);
      button.click();
      await vi.waitFor(() => expect(TestBed.inject(TmFakeSession).signIns()).toEqual([{}]));
    });

    it('keeps the session-ended dialog open when the user goes Back', async () => {
      const rig = await setUp();
      await rig.router.navigateByUrl('/17/centers?page=2');
      await tmSettle();
      rig.paths.expired('cookie');
      await vi.waitFor(() => expect(endedDialog()).not.toBeNull());
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
        await vi.waitFor(() => expect(rig.host.cookieWrites).toHaveLength(1), { timeout: 2_000 });
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
      await vi.waitFor(() => expect(showPage).toHaveBeenCalledWith('sign-in-failed', expect.any(Function)));
      expect(endedDialog()).toBeNull();
      retry?.();
      await vi.waitFor(() => expect(TestBed.inject(TmFakeSession).signIns()).toEqual([{}]));
      expect(rig.host.cookieWrites).toEqual([TM_PROFILE_COOKIE_DELETE]);
    });

    it("shows another tab's expiry without cleaning up again", async () => {
      const rig = await setUp();
      draft(rig, '17:gl.Center:5');
      const sweep = vi.spyOn(rig.store, 'sweep');
      rig.bus.receive({ type: 'session.expired', sub: TM_TEST_SUB });
      await vi.waitFor(() => expect(endedDialog()).not.toBeNull());
      expect(rig.paths.state()).toBe('expired');
      expect([sessionSweeps(sweep), rig.host.cookieWrites, drafts(rig)]).toEqual([[], [], 1]);
    });

    it('reloads a tab that saw the session end once the user signs in again elsewhere', async () => {
      const rig = await setUp();
      rig.bus.receive({ type: 'session.started', sub: TM_TEST_SUB });
      await new Promise((resolve) => setTimeout(resolve));
      expect(rig.host.reloads).toBe(0);
      rig.bus.receive({ type: 'session.expired', sub: TM_TEST_SUB });
      await vi.waitFor(() => expect(rig.paths.state()).toBe('expired'));
      rig.bus.receive({ type: 'session.started', sub: TM_TEST_SUB });
      await vi.waitFor(() => expect(rig.host.reloads).toBe(1));
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
      await vi.waitFor(() => expect(rig.host.replaced).toEqual(['/']));
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
      await vi.waitFor(() => expect(rig.bus.session()).toEqual([{ type: 'drafts.reply', replyTo: 'q1', dirty: 2, busy: true }]));
    });

    it('answers with the drafts service once it exists', async () => {
      const rig = await setUp();
      const { TmDrafts } = await import('@tellma/core/memory');
      const service = TestBed.inject(TmDrafts);
      service.save(service.key(17, 'gl.Center', 5), { name: 'Unsaved' });
      rig.bus.receive({ type: 'drafts.query', replyTo: 'q2' });
      await vi.waitFor(() =>
        expect(rig.bus.session()).toEqual([{ type: 'drafts.reply', replyTo: 'q2', dirty: service.dirtyCount(), busy: false }]),
      );
      expect(service.dirtyCount()).toBeGreaterThan(0);
    });
  });

  describe('sign-out', () => {
    it('signs out with no question when nothing is at risk, every tab following', async () => {
      const rig = await setUp();
      rig.bus.tabs.push({ dirty: 0, busy: false });
      rig.store.boot.patch({ sub: TM_TEST_SUB, lastTenantId: 17 });
      rig.api.respond('/bff/logout', { redirectUrl: 'https://id.example/connect/endsession?id_token_hint=x' });
      const overlay = vi.spyOn(TestBed.inject(TmBusyOverlay), 'run');
      const sweep = vi.spyOn(rig.store, 'sweep');
      let settled = false;
      void rig.paths.ɵsignOut().finally(() => (settled = true));
      await vi.waitFor(() => expect(rig.host.assigned).toEqual(['https://id.example/connect/endsession?id_token_hint=x']));
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
      await vi.waitFor(() => expect(rig.host.assigned).toEqual(['/signed-out']), { timeout: 3_000 });
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
      await vi.waitFor(() => expect(label()).toBe('Waiting for a save to finish…'));
      other.busy = false;
      await vi.waitFor(() => expect(rig.host.assigned).toEqual(['/signed-out']), { timeout: 3_000 });
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
      await vi.advanceTimersByTimeAsync(100);
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
      const sweep = vi.spyOn(rig.store, 'sweep');
      rig.bus.receive({ type: 'session.signing-out', sub: TM_TEST_SUB });
      rig.bus.receive({ type: 'session.signed-out', sub: TM_TEST_SUB });
      await vi.waitFor(() => expect(rig.host.replaced).toEqual(['/signed-out']));
      expect(drafts(rig)).toBe(0);
      expect(sessionSweeps(sweep)).toEqual([]);
      expect(rig.host.cookieWrites).toEqual([TM_PROFILE_COOKIE_DELETE]);
      expect(sessionEvents(rig)).toEqual([{ name: 'tm.session.signed-out', props: { origin: 'other-tab' } }]);
    });

    it('signs every tab out when the session ended elsewhere: this tab cleans up for all and tells them', async () => {
      const rig = await setUp();
      rig.store.boot.patch({ sub: TM_TEST_SUB, lastTenantId: 17 });
      const sweep = vi.spyOn(rig.store, 'sweep');
      rig.paths.ɵsignedOutElsewhere();
      await vi.waitFor(() => expect(rig.host.replaced).toEqual(['/signed-out']));
      expect(sessionSweeps(sweep)).toEqual([['session-ended']]);
      expect(rig.store.boot.read()).toMatchObject({ sub: null, lastTenantId: null });
      expect(rig.bus.session()).toEqual([{ type: 'session.signed-out', sub: TM_TEST_SUB }]);
      expect(sessionEvents(rig)).toEqual([{ name: 'tm.session.signed-out', props: { origin: 'elsewhere' } }]);
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
      await vi.waitFor(() => expect(rig.host.replaced).toEqual(['/']));
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
});
