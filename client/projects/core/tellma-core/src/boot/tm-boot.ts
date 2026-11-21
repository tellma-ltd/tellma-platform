// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  tmIsLocalPath,
  tmLoginUrl,
  tmReadLoginMark,
  tmRemoveLoginMark,
  tmWriteLoginMark,
  type TmLoginMark,
} from '../session/tm-login-mark';
import { tmParseProfileCookie, type TmProfile } from '../session/tm-profile';
import {
  tmIsTenantId,
  tmReadBootRecord,
  tmWriteBootRecord,
  type TmBootRecord,
} from '../state/tm-boot-record';
import { tmDeleteExpiredUser } from './tm-boot-expiry';
import { tmShowBootPage } from './tm-boot-page';
import type {
  TmBootEnv,
  TmBootLanguage,
  TmBootPageReason,
  TmBootResult,
  TmBootRun,
  TmBootState,
} from './tm-boot-types';

// The boot decision (spec 0024 §4.2). Before the app starts, with no network,
// the boot script reads the profile cookie, the boot record and the sign-in
// mark, then decides the session, the language, the direction, the theme and
// the launch target. It writes `<html lang dir data-theme>` and the title
// before the first paint, and leaves its decision on `window.__tmBoot`.
//
// The decision steps run in order, and the first that applies ends it:
//   1  a required browser feature is missing   → the boot page, `unsupported`
//   2  the path is /signed-out                  → run, with no cookie needed
//   3a no valid cookie, and cookies are off     → the boot page, `cookies`
//   3b no valid cookie, a sign-in mark < 60 s   → the boot page, `sign-in-failed`
//   3c no valid cookie, the record names a user → the expiry delete, then 3d
//   3d no valid cookie                          → write the mark, leave to sign in
//   4  a valid cookie                           → consume the mark, run
//
// This module is a pure function of its environment: the generated script
// passes the real globals (tm-boot.script.ts), and tests pass fakes.

/** How long a sign-in mark guards against the sign-in loop. */
const LOOP_GUARD_MS = 60_000;

/** The time box of the expiry delete. */
const EXPIRY_BOX_MS = 1_000;

/** The path of the signed-out page, which runs with no session. */
const SIGNED_OUT = '/signed-out';

/** A path under a tenant: a positive integer without leading zeros as its first segment. */
const TENANT_PATH = /^\/([1-9]\d*)(?:\/|$)/;

/**
 * Runs the boot decision over an environment.
 *
 * @param env - What the script reads and acts on.
 * @returns The result, and a promise that settles once the deferred work is done.
 */
export function tmBoot(env: TmBootEnv): TmBootRun {
  const { config, location } = env;
  const path = location.pathname;
  const now = env.now();

  // The offered language a requested one matches: the same code, else the
  // same primary subtag (a locale `ar-SA` matches the offered `ar`).
  const offered = (requested: string | null): TmBootLanguage | null => {
    let sameSubtag: TmBootLanguage | null = null;
    if (requested) {
      const wanted = requested.toLowerCase();
      for (const candidate of config.languages) {
        const code = candidate.code.toLowerCase();
        if (code === wanted) {
          return candidate;
        }
        if (!sameSubtag && code.split('-')[0] === wanted.split('-')[0]) {
          sameSubtag = candidate;
        }
      }
    }
    return sameSubtag;
  };

  // The inputs: the boot record, the user's preferred languages, the theme.
  let record: TmBootRecord | null = tmReadBootRecord(env.localStorage);
  const recordLanguage = offered(record && record.lang);
  // The browser's first offered language, else the default, the last resort
  // of every choice below.
  let browserLanguage: TmBootLanguage | null = null;
  for (const requested of env.languages) {
    browserLanguage = browserLanguage || offered(requested);
  }
  // The build checks that the default language is offered.
  browserLanguage = browserLanguage || (offered(config.defaultLanguage) as TmBootLanguage);
  const theme = record ? record.theme : 'system';

  // Steps 1-3 use the boot language: the record's, else the browser's.
  let language = recordLanguage || browserLanguage;
  let state: TmBootState = 'run';
  let reason: TmBootPageReason = 'unsupported';
  let profile: TmProfile | null = null;
  let mark: TmLoginMark | null = null;
  let cold = true;
  let auto: number | null = null;
  let fresh = record === null;
  let retry = (): void => location.reload();
  let settled = Promise.resolve();

  // Leaves to sign in: writes a sign-in mark stamped now, then replaces the
  // page with the sign-in URL in the boot language.
  const leave = (next: Omit<TmLoginMark, 'at'>): void => {
    tmWriteLoginMark(env.sessionStorage, Object.assign({}, next, { at: env.now() }));
    location.replace(tmLoginUrl(next.returnUrl, language.code));
  };

  // Rewrites the address before the app reads it. The URL is always a
  // local path, which no browser refuses.
  const rewrite = (url: string): void => env.history.replaceState(env.history.state, '', url);

  const { features } = env;
  if (!features.locks || !features.broadcastChannel || !features.indexedDB) {
    // Step 1: a browser the app cannot run in.
    state = 'page';
  } else if (path === SIGNED_OUT) {
    // Step 2: the signed-out page needs no session. Without a record, its
    // language is step 4's cold choice.
    profile = tmParseProfileCookie(env.cookies);
    language = recordLanguage || offered(profile && profile.locale) || browserLanguage;
  } else if ((profile = tmParseProfileCookie(env.cookies)) === null) {
    const previous = tmReadLoginMark(env.sessionStorage);
    if (!env.cookieEnabled) {
      // Step 3a: no session can ever arrive.
      state = 'page';
      reason = 'cookies';
    } else if (previous !== null && now - previous.at >= 0 && now - previous.at < LOOP_GUARD_MS) {
      // Step 3b: the last sign-in came back without a session. Try again
      // leaves for the same target, with the same fragment.
      state = 'page';
      reason = 'sign-in-failed';
      retry = () => leave(previous);
    } else {
      // Step 3c: a session the record names has expired. Its user fields
      // are cleared before the delete starts, so even a delete cut short
      // leaves a record that names no user, and the store's boot step then
      // sweeps every user's data.
      const expiredSub = record && record.sub;
      const lastTenantId = record && record.lastTenantId;
      if (record && expiredSub) {
        record = Object.assign({}, record, { sub: null, lastTenantId: null });
        tmWriteBootRecord(env.localStorage, record);
      }

      // Step 3d: the return path. At the root, the platform picks: the last
      // tenant of an expired session, else the root. Elsewhere, the path the
      // user asked for; a path no browser reads as local counts as the root.
      const target = path + location.search;
      const auto = path === '/' || !tmIsLocalPath(target);
      const returnUrl = auto ? `/${(expiredSub && lastTenantId) || ''}` : target;
      const go = (): void => leave({ expired: !!expiredSub, returnUrl, auto, sub: expiredSub, hash: location.hash });
      state = 'redirect';
      if (!expiredSub) {
        go();
      } else {
        settled = tmDeleteExpiredUser(env.indexedDB, env.caches, expiredSub, env.wait(EXPIRY_BOX_MS)).then(go);
      }
    }
  } else {
    // Step 4: a valid session. The sign-in mark, if any, is consumed now.
    mark = tmReadLoginMark(env.sessionStorage);
    tmRemoveLoginMark(env.sessionStorage);

    // No server hop returns the fragment: put the mark's back when the tab
    // landed exactly where the sign-in was sent, with no fragment of its own.
    if (mark && mark.hash && !location.hash && path + location.search === mark.returnUrl) {
      rewrite(mark.returnUrl + mark.hash);
    }

    // Warm when the record is this user's; else cold. The cold language
    // prefers the profile's locale.
    cold = !record || record.sub !== profile.sub;
    const profileLanguage = offered(profile.locale);
    language = (cold ? profileLanguage || recordLanguage : recordLanguage || profileLanguage) || browserLanguage;
    fresh = cold;

    const tenant = TENANT_PATH.exec(path);
    const tenantId = tenant && Number(tenant[1]);
    if (!cold && path === '/' && record && record.lastTenantId) {
      // A warm launch at the root returns to the last tenant at once.
      auto = record.lastTenantId;
      rewrite(`/${auto}`);
    } else if (
      tmIsTenantId(tenantId) &&
      mark &&
      ((mark.auto && path === `/${tenantId}`) || (mark.sub && mark.sub !== profile.sub))
    ) {
      // An automatic entry: the platform chose this tenant, or another
      // user's session chose the path.
      auto = tenantId;
    }
  }

  // The direction follows the language; the theme is the device's.
  const { code, dir } = language;
  if (fresh) {
    // A new record: the device's choices, and no user until the app writes one.
    record = { v: 1, theme, lang: code, dir, sub: null, lastTenantId: null };
    tmWriteBootRecord(env.localStorage, record);
  }

  // Before the first paint: the language, direction, theme and title.
  const { document } = env;
  const html = document.documentElement;
  html.lang = code;
  html.dir = dir;
  html.dataset['theme'] = theme === 'system' ? (env.prefersDark ? 'dark' : 'light') : theme;
  // The display name is the language's own, else English's.
  const name =
    (config.strings[code] || {}).Distribution_DisplayName ||
    (config.strings['en'] || {}).Distribution_DisplayName ||
    '';
  if (name) {
    document.title = name;
  }

  // The boot page is built once the document's body exists.
  const showPage = (shown: TmBootPageReason, onRetry: () => void = () => location.reload()): void => {
    const build = (): void => tmShowBootPage(document, config, language, name, shown, onRetry);
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', build);
    } else {
      build();
    }
  };
  if (state === 'page') {
    showPage(reason, retry);
  }

  const result: TmBootResult = { state, profile, record, cold, auto, mark, showPage };
  return { result, settled };
}
