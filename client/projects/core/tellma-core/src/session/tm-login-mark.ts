// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The sign-in mark (spec 0024 §4.2, §8.3): `sessionStorage['tm.login']`,
// written just before the tab leaves for `/bff/login` and consumed by the
// boot script when the tab returns signed in. It guards the sign-in loop,
// records who chose the target, and keeps the fragment no server hop
// returns. Plain code with no Angular import: the boot script bundles it too.

import { tmIsText, tmParseJsonObject, tmReadItem, tmWriteItem, type TmStorageLike } from '../state/tm-storage-json';

/** The session-storage key of the sign-in mark. */
export const TM_LOGIN_MARK_KEY = 'tm.login';

/** The sign-in mark: what a tab knew when it left to sign in. */
export interface TmLoginMark {
  /** When the mark was written, in epoch milliseconds; the loop guard reads it. */
  readonly at: number;
  /** Whether the tab left because the session expired, for the expiry telemetry. */
  readonly expired: boolean;
  /** The local path and query the sign-in returns to. */
  readonly returnUrl: string;
  /** Whether the platform chose `returnUrl` rather than the user. */
  readonly auto: boolean;
  /** Whose session this was: the expired user, or the user the tab ran as; `null` when none. */
  readonly sub: string | null;
  /** The location's fragment when the mark was written, with its `#`, or empty. */
  readonly hash: string;
}

/**
 * Whether a URL is a path on this origin: it starts with one `/` that no
 * second `/` or `\` follows, and it holds no tab or line break, which a
 * browser strips before it reads a URL. So no browser reads it as another
 * host.
 *
 * @param url - The URL.
 * @returns Whether it is a local path.
 */
export function tmIsLocalPath(url: string): boolean {
  return /^\/(?![/\\])[^\t\n\r]*$/.test(url);
}

/**
 * The sign-in URL: `/bff/login` with the return path and the sign-in page's
 * language. A step-up appends its own parameters.
 *
 * @param returnUrl - The local path and query to return to.
 * @param language - The sign-in page's language.
 * @returns The URL.
 */
export function tmLoginUrl(returnUrl: string, language: string): string {
  return `/bff/login?returnUrl=${encodeURIComponent(returnUrl)}&ui_locales=${encodeURIComponent(language)}`;
}

/**
 * Reads the sign-in mark, or `null` when there is none, the storage refuses,
 * or the stored value is not a well-formed mark.
 *
 * @param storage - The tab's session storage, or `null` when it is unavailable.
 * @returns The mark, or `null`.
 */
export function tmReadLoginMark(storage: TmStorageLike | null): TmLoginMark | null {
  const mark = tmParseJsonObject(tmReadItem(storage, TM_LOGIN_MARK_KEY));
  return isLoginMark(mark) ? mark : null;
}

/**
 * Whether a stored value is a well-formed sign-in mark. Each member is
 * checked, so a stored value of another shape never steers a redirect: the
 * return path stays local and the fragment stays a fragment.
 */
function isLoginMark(value: object | null): value is TmLoginMark {
  const { at, expired, returnUrl, auto, sub, hash } = (value || {}) as Readonly<Record<string, unknown>>;
  return (
    Number.isFinite(at) &&
    typeof expired === 'boolean' &&
    typeof returnUrl === 'string' &&
    tmIsLocalPath(returnUrl) &&
    typeof auto === 'boolean' &&
    (sub === null || tmIsText(sub)) &&
    typeof hash === 'string' &&
    /^(#|$)/.test(hash)
  );
}

/**
 * Writes the sign-in mark.
 *
 * @param storage - The tab's session storage, or `null` when it is unavailable.
 * @param mark - The mark.
 * @returns Whether the mark was written.
 */
export function tmWriteLoginMark(storage: TmStorageLike | null, mark: TmLoginMark): boolean {
  return tmWriteItem(storage, TM_LOGIN_MARK_KEY, mark);
}

/**
 * Removes the sign-in mark; a refusing storage is ignored.
 *
 * @param storage - The tab's session storage, or `null` when it is unavailable.
 */
export function tmRemoveLoginMark(storage: TmStorageLike | null): void {
  tmWriteItem(storage, TM_LOGIN_MARK_KEY, null);
}
