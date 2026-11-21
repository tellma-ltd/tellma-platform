// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { TmBootResult } from '../boot/tm-boot-types';
import { TmTelemetry } from '../observe/tm-telemetry';
import { TmClientStore } from '../state/store/tm-client-store';
import { TM_LOGIN_MARK_KEY, type TmLoginMark } from './tm-login-mark';
import { tmParseProfileCookie } from './tm-profile';
import { tmProfileCookies, tmProfileCookieValue } from './tm-profile.testing.util';
import { TmSession } from './tm-session';
import { TmSessionPaths } from './tm-session-paths';
import { TmFakeSessionHost, TmRecordingTelemetry, tmSessionTestingProviders } from './tm-session.testing.util';

const NOW = 1_700_000_000_000;
const FARAH = '0b4f';
const SARA = '7c1e';

/** Configures the session over a faked browser and returns the pieces. */
function setUp(options: { cookies?: string; boot?: Partial<TmBootResult> | null } = {}): {
  host: TmFakeSessionHost;
  telemetry: TmRecordingTelemetry;
  session: () => TmSession;
} {
  const host = new TmFakeSessionHost();
  host.cookieJar = options.cookies ?? tmProfileCookies(FARAH, 'ar');
  TestBed.configureTestingModule({ providers: tmSessionTestingProviders(host, options.boot ?? null, NOW) });
  return {
    host,
    telemetry: TestBed.inject(TmTelemetry) as TmRecordingTelemetry,
    session: () => TestBed.inject(TmSession),
  };
}

/** Whether a promise is still pending after the microtasks run. */
async function pending(promise: Promise<unknown>): Promise<boolean> {
  const marker = Symbol('pending');
  const winner = await Promise.race([promise, new Promise((resolve) => setTimeout(() => resolve(marker), 0))]);
  return winner === marker;
}

describe('TmSession', () => {
  describe('the profile', () => {
    it('is read from the cookie at the first injection when no boot script ran', () => {
      const { session } = setUp();
      expect(session().profile()).toMatchObject({ v: 2, sub: FARAH, locale: 'ar' });
      expect(session().state()).toBe('active');
    });

    it('is null without a valid cookie', () => {
      expect(setUp({ cookies: 'tellma.profile=%%%' }).session().profile()).toBeNull();
    });

    it('starts as the user the boot script read, so a cookie that moved since reads as another user', () => {
      const farah = tmParseProfileCookie(tmProfileCookies(FARAH, 'ar'));
      const { session } = setUp({ cookies: tmProfileCookies(SARA), boot: { profile: farah, mark: null } });
      expect(session().profile()).toEqual(farah);
      expect(session().check()).toBe('changed');
    });

    it('starts with no user when the boot script read none, whatever the cookie says now', () => {
      const { session } = setUp({ boot: { profile: null, mark: null } });
      expect(session().profile()).toBeNull();
      expect(session().check()).toBe('changed');
    });
  });

  describe('check', () => {
    it('reads the cookie now: the same user, no session, or another user', () => {
      const { host, session } = setUp();
      expect(session().check()).toBe('same');
      host.cookieJar = '';
      expect(session().check()).toBe('missing');
      host.cookieJar = tmProfileCookies(SARA);
      expect(session().check()).toBe('changed');
    });

    it('takes the same user with new details as the same, and updates the profile', () => {
      const { host, session } = setUp();
      host.cookieJar = `tellma.profile=${tmProfileCookieValue({ v: 2, sub: FARAH, sh: 'new', name: 'Farah N.', email: '', locale: 'en' })}`;
      expect(session().check()).toBe('same');
      expect(session().profile()).toEqual({ v: 2, sub: FARAH, sh: 'new', name: 'Farah N.', email: '', locale: 'en' });
    });

    it('tracks nothing, so a computed can check while the details change', () => {
      const { host, session } = setUp();
      let runs = 0;
      const checked = computed(() => {
        runs++;
        return session().check();
      });
      expect(checked()).toBe('same');
      host.cookieJar = `tellma.profile=${tmProfileCookieValue({ v: 2, sub: FARAH, sh: 'newer', name: 'Farah', email: '', locale: null })}`;
      // A fresh computed reads the changed details and updates the profile without throwing.
      expect(computed(() => session().check())()).toBe('same');
      expect(session().profile()?.sh).toBe('newer');
      // The first computed never depended on the profile, so the update left it alone.
      expect(checked()).toBe('same');
      expect(runs).toBe(1);
    });

    it('takes a cookie that appears in a tab that started without one as another user', () => {
      const { host, session } = setUp({ cookies: '' });
      expect(session().check()).toBe('missing');
      host.cookieJar = tmProfileCookies(FARAH);
      expect(session().check()).toBe('changed');
    });
  });

  describe('signIn', () => {
    it('writes the sign-in mark and leaves for the sign-in page in the document language', async () => {
      const { host, session } = setUp();
      const leaving = session().signIn();
      expect(host.sessionStorage.json(TM_LOGIN_MARK_KEY)).toEqual({
        at: NOW,
        expired: false,
        returnUrl: '/17/centers?x=1',
        auto: false,
        sub: FARAH,
        hash: '#d=abc',
      } satisfies TmLoginMark);
      expect(host.assigned).toEqual(['/bff/login?returnUrl=%2F17%2Fcenters%3Fx%3D1&ui_locales=ar']);
      expect(await pending(leaving)).toBe(true);
    });

    it('marks a return to the root automatic, and keeps the fragment only for the current page', () => {
      const { host, session } = setUp();
      void session().signIn({ returnUrl: '/' });
      expect(host.sessionStorage.json(TM_LOGIN_MARK_KEY)).toMatchObject({ returnUrl: '/', auto: true, hash: '' });
      void session().signIn({ returnUrl: '/17/centers?x=1' });
      expect(host.sessionStorage.json(TM_LOGIN_MARK_KEY)).toMatchObject({ auto: false, hash: '#d=abc' });
    });

    it('records no user when the tab had no session', () => {
      const { host, session } = setUp({ cookies: '' });
      void session().signIn();
      expect(host.sessionStorage.json(TM_LOGIN_MARK_KEY)).toMatchObject({ sub: null });
    });

    it('returns a current path no browser reads as local to the root', () => {
      const { host, session } = setUp();
      host.location.pathname = '//evil.example/x';
      host.location.search = '';
      void session().signIn();
      expect(host.assigned).toEqual(['/bff/login?returnUrl=%2F&ui_locales=ar']);
    });

    it('adds the step-up parameters and reports the step-up', () => {
      const { host, session, telemetry } = setUp();
      void session().signIn({ stepUp: { acr: 'mfa', maxAge: 300 } });
      void session().signIn({ returnUrl: '/17', stepUp: { acr: 'urn:acme:hw key', maxAge: null } });
      expect(host.assigned).toEqual([
        '/bff/login?returnUrl=%2F17%2Fcenters%3Fx%3D1&ui_locales=ar&acr_values=mfa&max_age=300',
        '/bff/login?returnUrl=%2F17&ui_locales=ar&acr_values=urn%3Aacme%3Ahw%20key',
      ]);
      expect(telemetry.events.map((event) => event.name)).toEqual(['tm.session.step-up', 'tm.session.step-up']);
    });

    it('refuses a return URL that is not a local path, and a malformed challenge', () => {
      const { host, session } = setUp();
      expect(() => session().signIn({ returnUrl: 'https://evil.example/' })).toThrowError(/not a local path/);
      expect(() => session().signIn({ returnUrl: '//evil.example' })).toThrowError(/not a local path/);
      expect(() => session().signIn({ stepUp: { acr: '', maxAge: 0 } })).toThrowError(/step-up/);
      expect(() => session().signIn({ stepUp: { acr: 'mfa', maxAge: -1 } })).toThrowError(/step-up/);
      expect(() => session().signIn({ stepUp: { acr: 'mfa', maxAge: 1.5 } })).toThrowError(/step-up/);
      expect(host.assigned).toEqual([]);
      expect(host.sessionStorage.getItem(TM_LOGIN_MARK_KEY)).toBeNull();
    });
  });

  describe('after a sign-in', () => {
    const mark: TmLoginMark = { at: NOW - 3_000, expired: true, returnUrl: '/17', auto: false, sub: SARA, hash: '' };

    it('starts the session, reports the expiry, and drops the previous user drafts', () => {
      const host = new TmFakeSessionHost();
      host.cookieJar = tmProfileCookies(FARAH);
      host.sessionStorage.setItem(`tm.draft:${SARA}:17:centers:4`, '{}');
      host.sessionStorage.setItem(`tm.draft:${SARA}:17:centers:5`, '{}');
      host.sessionStorage.setItem(`tm.draft:${SARA}9:17:centers:4`, '{}');
      host.sessionStorage.setItem(`tm.draft:${FARAH}:17:centers:4`, '{}');
      TestBed.configureTestingModule({ providers: tmSessionTestingProviders(host, { mark }, NOW) });
      const started = vi.spyOn(TestBed.inject(TmSessionPaths), 'started');
      TestBed.inject(TmSession);
      expect(started).toHaveBeenCalledWith(FARAH);
      expect((TestBed.inject(TmTelemetry) as TmRecordingTelemetry).events).toEqual([
        { name: 'tm.session.expired', props: { source: 'boot' } },
      ]);
      const keys = Array.from({ length: host.sessionStorage.length }, (_, i) => host.sessionStorage.key(i));
      expect(keys.sort()).toEqual([`tm.draft:${FARAH}:17:centers:4`, `tm.draft:${SARA}9:17:centers:4`]);
    });

    it('keeps the drafts of the same user, and reports nothing for a plain sign-in', () => {
      const host = new TmFakeSessionHost();
      host.cookieJar = tmProfileCookies(FARAH);
      host.sessionStorage.setItem(`tm.draft:${FARAH}:17:centers:4`, '{}');
      const own = { ...mark, expired: false, sub: FARAH };
      TestBed.configureTestingModule({ providers: tmSessionTestingProviders(host, { mark: own }, NOW) });
      TestBed.inject(TmSession);
      expect((TestBed.inject(TmTelemetry) as TmRecordingTelemetry).events).toEqual([]);
      expect(host.sessionStorage.length).toBe(1);
    });

    it('does nothing without a mark', () => {
      const host = new TmFakeSessionHost();
      host.cookieJar = tmProfileCookies(FARAH);
      TestBed.configureTestingModule({ providers: tmSessionTestingProviders(host, { mark: null }, NOW) });
      const started = vi.spyOn(TestBed.inject(TmSessionPaths), 'started');
      TestBed.inject(TmSession);
      expect(started).not.toHaveBeenCalled();
    });
  });
});

describe('TmSessionPaths', () => {
  it('expires the session once, reporting what found the expiry', () => {
    const { session, telemetry } = setUp();
    const paths = TestBed.inject(TmSessionPaths);
    paths.expired('401');
    paths.expired('cookie');
    expect(session().state()).toBe('expired');
    expect(telemetry.events).toEqual([{ name: 'tm.session.expired', props: { source: '401' } }]);
    expect(telemetry.flushes).toEqual([1]);
  });

  it("takes an expired tab off the user's data: every user's stored data goes, the stale cookie too, and the tab signs in again, once", async () => {
    const { host, telemetry } = setUp();
    const store = TestBed.inject(TmClientStore);
    const tenant = { kind: 'tenant', sub: FARAH, tenantId: 17 } as const;
    await store.put(tenant, 'me', { tag: null, savedAt: NOW, data: { name: 'Farah' } });
    store.boot.patch({ sub: FARAH, lastTenantId: 17 });
    const sweep = vi.spyOn(store, 'sweep');
    const paths = TestBed.inject(TmSessionPaths);
    paths.expired('401');
    paths.expired('cookie');
    await vi.waitFor(() => expect(host.assigned).toHaveLength(1));
    expect(sweep.mock.calls).toEqual([['session-ended']]);
    expect(store.doc(tenant, 'me')()).toBeUndefined();
    expect(host.cookieWrites).toEqual(['tellma.profile=; Max-Age=0; Path=/; Secure']);
    expect(host.assigned).toEqual(['/bff/login?returnUrl=%2F17%2Fcenters%3Fx%3D1&ui_locales=ar']);
    expect(host.reloads).toBe(0);
    // The sign-in reports nothing again when the tab comes back: the mark
    // says no expiry, and names the user whose session ended.
    expect(host.sessionStorage.json(TM_LOGIN_MARK_KEY)).toMatchObject({ expired: false, sub: FARAH, returnUrl: '/17/centers?x=1' });
    expect(telemetry.events.map((event) => event.name)).toEqual(['tm.session.expired']);
    // The expiry keeps the boot record's user and last tenant (spec 0024 §8.6).
    expect(store.boot.read()).toMatchObject({ sub: FARAH, lastTenantId: 17 });
  });

  it('lets the next launch sweep when the expiry sweep runs past its time box', async () => {
    vi.useFakeTimers();
    try {
      const { host } = setUp();
      const store = TestBed.inject(TmClientStore);
      store.boot.patch({ sub: FARAH, lastTenantId: 17 });
      vi.spyOn(store, 'sweep').mockReturnValue(new Promise<boolean>(() => undefined));
      TestBed.inject(TmSessionPaths).expired('cookie');
      await vi.advanceTimersByTimeAsync(999);
      expect(host.assigned).toEqual([]);
      await vi.advanceTimersByTimeAsync(1);
      expect(host.assigned).toHaveLength(1);
      expect(store.boot.read()).toMatchObject({ sub: null, lastTenantId: 17 });
    } finally {
      vi.useRealTimers();
    }
  });

  it('lets the next launch sweep when the expiry sweep could not reach the stored data', async () => {
    const { host } = setUp();
    const store = TestBed.inject(TmClientStore);
    store.boot.patch({ sub: FARAH, lastTenantId: 17 });
    vi.spyOn(store, 'sweep').mockResolvedValue(false);
    TestBed.inject(TmSessionPaths).expired('cookie');
    await vi.waitFor(() => expect(host.assigned).toHaveLength(1));
    expect(store.boot.read()).toMatchObject({ sub: null, lastTenantId: 17 });
  });

  it('lets the next launch sweep when the expiry sweep fails', async () => {
    const { host } = setUp();
    const store = TestBed.inject(TmClientStore);
    store.boot.patch({ sub: FARAH, lastTenantId: 17 });
    vi.spyOn(store, 'sweep').mockRejectedValue(new DOMException('The write was aborted.', 'AbortError'));
    TestBed.inject(TmSessionPaths).expired('cookie');
    await vi.waitFor(() => expect(host.assigned).toHaveLength(1));
    expect(store.boot.read()).toMatchObject({ sub: null, lastTenantId: 17 });
  });

  it('shows the sign-in-failed page instead of signing in again within a minute of coming back from a sign-in', async () => {
    const host = new TmFakeSessionHost();
    host.cookieJar = tmProfileCookies(FARAH);
    const mark: TmLoginMark = { at: NOW - 59_999, expired: false, returnUrl: '/17', auto: false, sub: null, hash: '' };
    let retry: (() => void) | undefined;
    const showPage = vi.fn((_reason: string, again?: () => void) => void (retry = again));
    TestBed.configureTestingModule({ providers: tmSessionTestingProviders(host, { mark, showPage }, NOW) });
    const store = TestBed.inject(TmClientStore);
    const tenant = { kind: 'tenant', sub: FARAH, tenantId: 17 } as const;
    await store.put(tenant, 'me', { tag: null, savedAt: NOW, data: { name: 'Farah' } });
    const sweep = vi.spyOn(store, 'sweep');
    TestBed.inject(TmSessionPaths).expired('401');
    expect(TestBed.inject(TmSession).state()).toBe('expired');
    expect(showPage).toHaveBeenCalledWith('sign-in-failed', expect.any(Function));
    // The user's data goes before Try again: a tab closed on this page leaves none.
    expect(sweep.mock.calls).toEqual([['session-ended']]);
    expect(store.doc(tenant, 'me')()).toBeUndefined();
    await Promise.resolve();
    expect([host.assigned, host.reloads, host.cookieWrites]).toEqual([[], 0, []]);
    // Try again starts the next round: the cookie goes and the tab signs in.
    retry?.();
    await vi.waitFor(() => expect(host.assigned).toHaveLength(1));
    expect(host.cookieWrites).toEqual(['tellma.profile=; Max-Age=0; Path=/; Secure']);
  });

  it('signs in again as usual once the sign-in is a minute old', async () => {
    const host = new TmFakeSessionHost();
    host.cookieJar = tmProfileCookies(FARAH);
    const mark: TmLoginMark = { at: NOW - 60_000, expired: false, returnUrl: '/17', auto: false, sub: null, hash: '' };
    const showPage = vi.fn();
    TestBed.configureTestingModule({ providers: tmSessionTestingProviders(host, { mark, showPage }, NOW) });
    TestBed.inject(TmSessionPaths).expired('401');
    expect(showPage).not.toHaveBeenCalled();
    await vi.waitFor(() => expect(host.assigned).toHaveLength(1));
  });

  it('runs nothing for a 401 to a request sent under a session the cookie no longer names', async () => {
    const { host, session, telemetry } = setUp();
    const paths = TestBed.inject(TmSessionPaths);
    paths.expired('401', 'sh-of-an-older-session');
    await Promise.resolve();
    expect(session().state()).toBe('active');
    expect([host.cookieWrites, host.assigned, telemetry.events]).toEqual([[], [], []]);
    paths.expired('401', `sh-${FARAH}`);
    expect(session().state()).toBe('expired');
    await vi.waitFor(() => expect(host.assigned).toHaveLength(1));
  });

  it('restarts the app at the root for a changed user, after sending the telemetry', () => {
    const { host, telemetry } = setUp();
    TestBed.inject(TmSessionPaths).changedUser();
    expect(host.replaced).toEqual(['/']);
    expect(telemetry.events.map((event) => event.name)).toEqual(['tm.session.changed-user']);
    expect(telemetry.flushes).toEqual([1]);
    expect(host.reloads).toBe(0);
  });
});
