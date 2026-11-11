// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_LOGIN_MARK_KEY, type TmLoginMark } from '../session/tm-login-mark';
import { tmProfileCookies } from '../session/tm-profile.testing.util';
import { TM_BOOT_RECORD_KEY, type TmBootRecord } from '../state/tm-boot-record';
import { TM_REFUSING_STORAGE, TmFakeStorage } from '../state/tm-storage.testing.util';
import { tmBoot } from './tm-boot';
import type { TmBootCaches, TmBootConfig, TmBootFeatures, TmBootRun } from './tm-boot-types';

// The boot decision over a faked environment, step by step: each case sets
// the address, the cookie, the boot record, the sign-in mark and the
// browser's facts, runs the decision, and reads what it wrote and where it
// went.

const CONFIG: TmBootConfig = {
  v: 1,
  languages: [
    { code: 'en', dir: 'ltr' },
    { code: 'ar', dir: 'rtl' },
    { code: 'am', dir: 'ltr' },
  ],
  defaultLanguage: 'en',
  strings: {
    en: {
      Distribution_DisplayName: 'Acme',
      Boot_Title: "{displayName} couldn't start",
      Boot_Unsupported: 'Update your browser to a current version of Chrome, Edge, Firefox or Safari.',
      Boot_Cookies: 'Turn on cookies for this site, then try again.',
      Boot_SignInFailed: "We couldn't sign you in. Try again in a moment.",
      Boot_LoadFailed: 'Check your connection and try again.',
      Boot_TryAgain: 'Try again',
    },
    ar: { Distribution_DisplayName: 'أكمي', Boot_TryAgain: 'حاول مرة أخرى' },
    am: {},
  },
};

const NOW = 1_700_000_000_000;
const FARAH = '0b4f';
const SARA = '7c1e';

/** Farah's warm record: dark, Arabic, last in tenant 17. */
const FARAH_RECORD: TmBootRecord = { v: 1, theme: 'dark', lang: 'ar', dir: 'rtl', sub: FARAH, lastTenantId: 17 };

/** What one case sets up. */
interface Setup {
  /** The address: a path with an optional query and fragment. */
  readonly url?: string;
  /** The cookie string. */
  readonly cookies?: string;
  /** The stored boot record, as a value or as raw text. */
  readonly record?: TmBootRecord | string | null;
  /** The stored sign-in mark. */
  readonly mark?: TmLoginMark | null;
  /** `navigator.cookieEnabled`. */
  readonly cookieEnabled?: boolean;
  /** `navigator.languages`. */
  readonly languages?: readonly string[];
  /** Whether the system prefers dark. */
  readonly prefersDark?: boolean;
  /** The required features; all present unless overridden. */
  readonly features?: Partial<TmBootFeatures>;
  /** Whether local storage refuses every call. */
  readonly refusingLocalStorage?: boolean;
  /** Whether session storage refuses every call. */
  readonly refusingSessionStorage?: boolean;
  /** The document's ready state; `complete` unless set. */
  readonly readyState?: DocumentReadyState;
}

/** One run of the decision and everything it touched. */
interface Outcome {
  /** The run. */
  readonly run: TmBootRun;
  /** The document. */
  readonly document: Document;
  /** Where `location.replace` went. */
  readonly replaced: string[];
  /** How many times the page reloaded. */
  readonly reloads: () => number;
  /** Each `history.replaceState` call: its state and its URL. */
  readonly rewrites: { state: unknown; url: string }[];
  /** Local storage. */
  readonly local: TmFakeStorage;
  /** Session storage. */
  readonly session: TmFakeStorage;
  /** The expiry delete's IndexedDB opens, by database name. */
  readonly opened: string[];
  /** The caches the expiry delete deleted. */
  readonly cachesDeleted: string[];
  /** The time boxes the run asked for, in milliseconds. */
  readonly waits: number[];
  /** Advances the clock. */
  readonly advance: (ms: number) => void;
}

/** The history entry's state, which every rewrite must keep. */
const HISTORY_STATE = { navigationId: 1 };

/** Runs the decision over a faked environment. */
function boot(setup: Setup = {}): Outcome {
  const document = window.document.implementation.createHTMLDocument('fallback title');
  document.body.appendChild(document.createElement('tm-app'));
  // A created document never finishes loading; a page that ran this script
  // has, unless the case says otherwise.
  Object.defineProperty(document, 'readyState', { value: setup.readyState ?? 'complete', configurable: true });

  const local = new TmFakeStorage();
  if (setup.record) {
    local.setItem(TM_BOOT_RECORD_KEY, typeof setup.record === 'string' ? setup.record : JSON.stringify(setup.record));
  }
  const session = new TmFakeStorage();
  if (setup.mark) {
    session.setItem(TM_LOGIN_MARK_KEY, JSON.stringify(setup.mark));
  }

  const url = new URL(setup.url ?? '/', 'https://acme.example');
  const replaced: string[] = [];
  let reloads = 0;
  const location = {
    pathname: url.pathname,
    search: url.search,
    hash: url.hash,
    replace: (target: string) => replaced.push(target),
    reload: () => reloads++,
  };
  const rewrites: { state: unknown; url: string }[] = [];
  const history = {
    state: HISTORY_STATE,
    replaceState: (state: unknown, _unused: string, target: string) => rewrites.push({ state, url: target }),
  };

  // The expiry delete's stand-ins: an IndexedDB whose open never answers,
  // and caches that record what they delete.
  const opened: string[] = [];
  const indexedDB = { open: (name: string) => (opened.push(name), {}) } as unknown as IDBFactory;
  const cachesDeleted: string[] = [];
  const caches: TmBootCaches = { delete: (name) => (cachesDeleted.push(name), Promise.resolve(true)) };
  const waits: number[] = [];

  let now = NOW;
  const run = tmBoot({
    config: CONFIG,
    document,
    location,
    history,
    cookies: setup.cookies ?? '',
    cookieEnabled: setup.cookieEnabled ?? true,
    languages: setup.languages ?? ['en-US', 'en'],
    prefersDark: setup.prefersDark ?? false,
    features: { locks: {}, broadcastChannel: function BroadcastChannel() {}, indexedDB: {}, ...setup.features },
    localStorage: setup.refusingLocalStorage ? TM_REFUSING_STORAGE : local,
    sessionStorage: setup.refusingSessionStorage ? TM_REFUSING_STORAGE : session,
    indexedDB,
    caches,
    now: () => now,
    // The time box ends at once, since the faked delete never does.
    wait: (ms) => (waits.push(ms), Promise.resolve()),
  });
  return {
    run,
    document,
    replaced,
    reloads: () => reloads,
    rewrites,
    local,
    session,
    opened,
    cachesDeleted,
    waits,
    advance: (ms) => (now += ms),
  };
}

/** The sign-in URL for a return path and a language. */
function login(returnUrl: string, language = 'en'): string {
  return `/bff/login?returnUrl=${encodeURIComponent(returnUrl)}&ui_locales=${language}`;
}

/** A sign-in mark written at a given time. */
function markAt(at: number, overrides: Partial<TmLoginMark> = {}): TmLoginMark {
  return { at, expired: false, returnUrl: '/17/centers', auto: false, sub: null, hash: '', ...overrides };
}

describe('tmBoot', () => {
  describe('step 1, the feature gate', () => {
    for (const missing of ['locks', 'broadcastChannel', 'indexedDB'] as const) {
      it(`shows the unsupported page without ${missing}, even signed in`, () => {
        const outcome = boot({ cookies: tmProfileCookies(FARAH), features: { [missing]: undefined } });
        expect(outcome.run.result.state).toBe('page');
        expect(outcome.document.querySelector('.tm-boot-page p')?.textContent).toBe(
          'Update your browser to a current version of Chrome, Edge, Firefox or Safari.',
        );
        expect(outcome.replaced).toEqual([]);
        outcome.document.querySelector('button')!.click();
        expect(outcome.reloads()).toBe(1);
      });
    }
  });

  describe('step 2, the signed-out page', () => {
    it('runs with no cookie, in the record language', () => {
      const outcome = boot({ url: '/signed-out', record: { ...FARAH_RECORD, sub: null, lastTenantId: null } });
      expect(outcome.run.result.state).toBe('run');
      expect(outcome.run.result.profile).toBeNull();
      expect(outcome.replaced).toEqual([]);
      expect(outcome.document.documentElement.lang).toBe('ar');
    });

    it('takes the cold choice without a record: the profile locale, else the browser', () => {
      expect(boot({ url: '/signed-out', cookies: tmProfileCookies(FARAH, 'am') }).document.documentElement.lang).toBe('am');
      expect(boot({ url: '/signed-out', languages: ['ar-EG'] }).document.documentElement.lang).toBe('ar');
    });

    it('leaves a sign-in mark in place', () => {
      const outcome = boot({ url: '/signed-out', mark: markAt(NOW - 1_000) });
      expect(outcome.session.getItem(TM_LOGIN_MARK_KEY)).not.toBeNull();
      expect(outcome.run.result.mark).toBeNull();
    });
  });

  describe('step 3a, cookies off', () => {
    it('shows the cookies page and never leaves to sign in', () => {
      const outcome = boot({ url: '/17', cookieEnabled: false });
      expect(outcome.run.result.state).toBe('page');
      expect(outcome.document.querySelector('p')?.textContent).toBe('Turn on cookies for this site, then try again.');
      expect(outcome.replaced).toEqual([]);
      expect(outcome.session.getItem(TM_LOGIN_MARK_KEY)).toBeNull();
    });
  });

  describe('step 3b, the sign-in loop guard', () => {
    it('shows the sign-in-failed page for a mark under 60 s old', () => {
      const outcome = boot({ url: '/17', mark: markAt(NOW - 59_999) });
      expect(outcome.run.result.state).toBe('page');
      expect(outcome.document.querySelector('p')?.textContent).toBe("We couldn't sign you in. Try again in a moment.");
      expect(outcome.replaced).toEqual([]);
    });

    it('leaves to sign in again for a mark 60 s old, or one from the future', () => {
      for (const at of [NOW - 60_000, NOW + 1]) {
        const outcome = boot({ url: '/17', mark: markAt(at) });
        expect(outcome.run.result.state).toBe('redirect');
        expect(outcome.replaced).toEqual([login('/17')]);
      }
    });

    it('retries with the old mark target, flags and fragment, under a fresh time', () => {
      const old = markAt(NOW - 5_000, { returnUrl: '/17/centers?x=1', auto: true, expired: true, sub: FARAH, hash: '#d=abc' });
      const outcome = boot({ url: '/', record: { ...FARAH_RECORD, lang: 'ar' }, mark: old });
      outcome.advance(30_000);
      outcome.document.querySelector('button')!.click();
      expect(outcome.replaced).toEqual([login('/17/centers?x=1', 'ar')]);
      expect(outcome.session.json(TM_LOGIN_MARK_KEY)).toEqual({ ...old, at: NOW + 30_000 });
    });
  });

  describe('step 3c, the expiry delete', () => {
    it('clears the record user, deletes that user data in a 1 s box, then leaves to sign in', async () => {
      const outcome = boot({ url: '/17/centers?x=1#d=abc', record: FARAH_RECORD });
      expect(outcome.run.result.state).toBe('redirect');
      expect(outcome.local.json(TM_BOOT_RECORD_KEY)).toEqual({ ...FARAH_RECORD, sub: null, lastTenantId: null });
      expect(outcome.run.result.record).toEqual({ ...FARAH_RECORD, sub: null, lastTenantId: null });
      expect(outcome.opened).toEqual(['tm-app']);
      expect(outcome.waits).toEqual([1_000]);
      // The page leaves only once the delete or its box is done.
      expect(outcome.replaced).toEqual([]);
      await outcome.run.settled;
      expect(outcome.cachesDeleted).toEqual(['tm-images-v1']);
      expect(outcome.replaced).toEqual([login('/17/centers?x=1', 'ar')]);
      expect(outcome.session.json(TM_LOGIN_MARK_KEY)).toEqual({
        at: NOW,
        expired: true,
        returnUrl: '/17/centers?x=1',
        auto: false,
        sub: FARAH,
        hash: '#d=abc',
      });
    });

    it('deletes nothing when the record names no user', () => {
      const outcome = boot({ url: '/17', record: { ...FARAH_RECORD, sub: null, lastTenantId: null } });
      expect(outcome.opened).toEqual([]);
      expect(outcome.waits).toEqual([]);
      expect(outcome.replaced).toEqual([login('/17', 'ar')]);
    });
  });

  describe('step 3d, the return URL', () => {
    it('returns an expired session at the root to its last tenant, automatically', async () => {
      const outcome = boot({ url: '/', record: FARAH_RECORD });
      await outcome.run.settled;
      expect(outcome.replaced).toEqual([login('/17', 'ar')]);
      expect(outcome.session.json(TM_LOGIN_MARK_KEY)).toMatchObject({ returnUrl: '/17', auto: true, expired: true });
    });

    it('returns to the root, automatically, without a last tenant', () => {
      const outcome = boot({ url: '/?x=1' });
      expect(outcome.replaced).toEqual([login('/')]);
      expect(outcome.session.json(TM_LOGIN_MARK_KEY)).toEqual({
        at: NOW,
        expired: false,
        returnUrl: '/',
        auto: true,
        sub: null,
        hash: '',
      });
    });

    it('returns any other path with its query, not automatically, keeping the fragment', () => {
      const outcome = boot({ url: '/17/centers?x=1&y=2#d=abc', languages: ['ar-SA'] });
      expect(outcome.replaced).toEqual([login('/17/centers?x=1&y=2', 'ar')]);
      expect(outcome.session.json(TM_LOGIN_MARK_KEY)).toMatchObject({ auto: false, hash: '#d=abc' });
    });

    it('returns a path no browser reads as local as the root', () => {
      const outcome = boot({ url: 'https://acme.example//evil.example/x' });
      expect(outcome.replaced).toEqual([login('/')]);
    });

    it('still leaves to sign in when session storage refuses the mark', () => {
      const outcome = boot({ url: '/17', refusingSessionStorage: true });
      expect(outcome.replaced).toEqual([login('/17')]);
    });
  });

  describe('step 4, a valid cookie', () => {
    it('consumes the sign-in mark', () => {
      const mark = markAt(NOW - 3_000, { returnUrl: '/17' });
      const outcome = boot({ url: '/17', cookies: tmProfileCookies(FARAH), mark });
      expect(outcome.run.result.state).toBe('run');
      expect(outcome.run.result.mark).toEqual(mark);
      expect(outcome.session.getItem(TM_LOGIN_MARK_KEY)).toBeNull();
      expect(outcome.run.result.profile?.sub).toBe(FARAH);
    });

    it('restores the fragment only when the landing equals the return URL and has none', () => {
      const mark = markAt(NOW - 3_000, { returnUrl: '/17/centers?x=1', hash: '#d=abc' });
      const restored = boot({ url: '/17/centers?x=1', cookies: tmProfileCookies(FARAH), mark });
      expect(restored.rewrites).toEqual([{ state: HISTORY_STATE, url: '/17/centers?x=1#d=abc' }]);
      const elsewhere = boot({ url: '/17/centers', cookies: tmProfileCookies(FARAH), mark });
      expect(elsewhere.rewrites).toEqual([]);
      const ownHash = boot({ url: '/17/centers?x=1#other', cookies: tmProfileCookies(FARAH), mark });
      expect(ownHash.rewrites).toEqual([]);
    });

    it('is cold without a record, writing a fresh one in the profile language', () => {
      const outcome = boot({ url: '/17', cookies: tmProfileCookies(FARAH, 'ar-SA'), prefersDark: true });
      expect(outcome.run.result.cold).toBe(true);
      const fresh = { v: 1, theme: 'system', lang: 'ar', dir: 'rtl', sub: null, lastTenantId: null };
      expect(outcome.local.json(TM_BOOT_RECORD_KEY)).toEqual(fresh);
      expect(outcome.run.result.record).toEqual(fresh);
      const html = outcome.document.documentElement;
      expect([html.lang, html.dir, html.dataset['theme']]).toEqual(['ar', 'rtl', 'dark']);
    });

    it('is cold for another user record, keeping the device theme', () => {
      const outcome = boot({ url: '/17', cookies: tmProfileCookies(SARA, 'en'), record: FARAH_RECORD });
      expect(outcome.run.result.cold).toBe(true);
      expect(outcome.local.json(TM_BOOT_RECORD_KEY)).toEqual({
        v: 1,
        theme: 'dark',
        lang: 'en',
        dir: 'ltr',
        sub: null,
        lastTenantId: null,
      });
    });

    it('orders the cold language: profile locale, record, browser, default', () => {
      const other = { ...FARAH_RECORD, sub: SARA, lang: 'am', dir: 'ltr' as const };
      const lang = (setup: Setup): string => boot({ url: '/17', ...setup }).document.documentElement.lang;
      expect(lang({ cookies: tmProfileCookies(FARAH, 'ar'), record: other })).toBe('ar');
      expect(lang({ cookies: tmProfileCookies(FARAH, 'fr'), record: other })).toBe('am');
      expect(lang({ cookies: tmProfileCookies(FARAH), languages: ['fr-FR', 'ar-EG', 'am'] })).toBe('ar');
      expect(lang({ cookies: tmProfileCookies(FARAH), languages: ['fr-FR'] })).toBe('en');
    });

    it('keeps the record language when warm', () => {
      const outcome = boot({ url: '/17', cookies: tmProfileCookies(FARAH, 'am'), record: FARAH_RECORD });
      expect(outcome.run.result.cold).toBe(false);
      expect(outcome.document.documentElement.lang).toBe('ar');
      expect(outcome.local.json(TM_BOOT_RECORD_KEY)).toEqual(FARAH_RECORD);
    });

    it('rewrites a warm root to the last tenant, marking it automatic', () => {
      const outcome = boot({ url: '/', cookies: tmProfileCookies(FARAH), record: FARAH_RECORD });
      expect(outcome.rewrites).toEqual([{ state: HISTORY_STATE, url: '/17' }]);
      expect(outcome.run.result.auto).toBe(17);
    });

    it('leaves a root alone when cold, or warm without a last tenant', () => {
      const cold = boot({ url: '/', cookies: tmProfileCookies(SARA), record: FARAH_RECORD });
      expect(cold.rewrites).toEqual([]);
      expect(cold.run.result.auto).toBeNull();
      const noTenant = boot({ url: '/', cookies: tmProfileCookies(FARAH), record: { ...FARAH_RECORD, lastTenantId: null } });
      expect(noTenant.rewrites).toEqual([]);
      expect(noTenant.run.result.auto).toBeNull();
    });

    it('marks a tenant automatic when the mark chose exactly that tenant', () => {
      const chosen = markAt(NOW - 3_000, { returnUrl: '/', auto: true });
      expect(boot({ url: '/17', cookies: tmProfileCookies(FARAH), mark: chosen }).run.result.auto).toBe(17);
      expect(boot({ url: '/17/centers', cookies: tmProfileCookies(FARAH), mark: chosen }).run.result.auto).toBeNull();
      expect(boot({ url: '/tenants', cookies: tmProfileCookies(FARAH), mark: chosen }).run.result.auto).toBeNull();
    });

    it('marks a tenant automatic when another user session chose the path', () => {
      const farahs = markAt(NOW - 3_000, { returnUrl: '/17/centers', sub: FARAH, expired: true });
      expect(boot({ url: '/17/centers', cookies: tmProfileCookies(SARA), mark: farahs }).run.result.auto).toBe(17);
      expect(boot({ url: '/17/centers', cookies: tmProfileCookies(FARAH), mark: farahs }).run.result.auto).toBeNull();
    });

    it('marks nothing without a mark', () => {
      expect(boot({ url: '/17', cookies: tmProfileCookies(FARAH) }).run.result.auto).toBeNull();
    });
  });

  describe('what every run writes', () => {
    it('titles the document with the display name, falling back to English', () => {
      expect(boot({ cookies: tmProfileCookies(FARAH, 'ar') }).document.title).toBe('أكمي');
      expect(boot({ cookies: tmProfileCookies(FARAH, 'am') }).document.title).toBe('Acme');
    });

    it('sets the theme from the record, else the system', () => {
      const theme = (setup: Setup): string | undefined =>
        boot({ cookies: tmProfileCookies(FARAH), ...setup }).document.documentElement.dataset['theme'];
      expect(theme({ record: { ...FARAH_RECORD, theme: 'light' }, prefersDark: true })).toBe('light');
      expect(theme({ record: FARAH_RECORD })).toBe('dark');
      expect(theme({ prefersDark: true })).toBe('dark');
      expect(theme({ prefersDark: false })).toBe('light');
    });

    it('ignores an invalid record and runs when local storage refuses', () => {
      const invalid = boot({ url: '/17', cookies: tmProfileCookies(FARAH), record: '{"v":2}' });
      expect(invalid.run.result.cold).toBe(true);
      const refused = boot({ url: '/17', cookies: tmProfileCookies(FARAH), refusingLocalStorage: true });
      expect(refused.run.result.state).toBe('run');
      expect(refused.run.result.cold).toBe(true);
    });

    it('builds the boot page once the document has loaded', () => {
      const outcome = boot({ url: '/17', cookieEnabled: false, readyState: 'loading' });
      expect(outcome.document.querySelector('.tm-boot-page')).toBeNull();
      outcome.document.dispatchEvent(new Event('DOMContentLoaded'));
      expect(outcome.document.querySelector('.tm-boot-page')).not.toBeNull();
    });

    it('lets the app show the load-failed page, which reloads', () => {
      const outcome = boot({ url: '/17', cookies: tmProfileCookies(FARAH, 'ar') });
      outcome.run.result.showPage('load-failed');
      const page = outcome.document.querySelector('.tm-boot-page')!;
      expect(page.getAttribute('lang')).toBe('ar');
      expect(page.querySelector('p')?.textContent).toBe('Check your connection and try again.');
      page.querySelector('button')!.click();
      expect(outcome.reloads()).toBe(1);
    });
  });
});
