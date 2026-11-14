// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The profile cookie (spec 0024 §8.1, spec 0011 §5.5): the script-readable
// mirror of the session, the browser's synchronous signed-in signal. Its value
// is the unpadded base64url of the UTF-8 JSON `{ v: 2, sub, sh, name, email,
// locale }`. This module is plain code with no Angular import, because the
// boot script bundles it too: the boot script and `TmSession` must agree on
// which cookie counts as a session.

import { tmIsText, tmParseJsonObject } from '../state/tm-storage-json';

/** The name of the profile cookie. */
export const TM_PROFILE_COOKIE = 'tellma.profile';

/**
 * The signed-in user, as the profile cookie carries it. It holds no token and
 * nothing tenant-scoped.
 */
export interface TmProfile {
  /** The shape's version; always 2. */
  readonly v: 2;
  /** The user's subject identifier. */
  readonly sub: string;
  /** The session handle: names the session's stored data, and is never a credential. */
  readonly sh: string;
  /** The user's name; empty when the identity token carries none. */
  readonly name: string;
  /** The user's email address; empty when the identity token carries none. */
  readonly email: string;
  /** The user's preferred locale, such as `ar-SA`, or `null` when the identity token carries none. */
  readonly locale: string | null;
}

/** Base64url text, optionally padded with up to two `=`. */
const BASE64URL = /^[\w-]*={0,2}$/;

/**
 * The profile a cookie value carries, or `null` when the value is not
 * base64url, not UTF-8 or not JSON, is not version 2, or lacks a subject or a
 * session handle. A missing name or email reads as empty, and a missing locale
 * as `null`.
 *
 * @param value - The cookie's value.
 * @returns The profile, or `null`.
 */
export function tmDecodeProfile(value: string): TmProfile | null {
  let text: string | null = null;
  try {
    // `atob` checks the rest: padding is optional, but when present it must
    // complete the last group, and a lone character in the last group fails.
    // `fatal` refuses an invalid UTF-8 sequence instead of replacing it.
    if (BASE64URL.test(value)) {
      const binary = atob(value.replace(/-/g, '+').replace(/_/g, '/'));
      text = new TextDecoder('utf-8', { fatal: true }).decode(Uint8Array.from(binary, (char) => char.charCodeAt(0)));
    }
  } catch {
    // Not base64, or not UTF-8.
  }
  const { v, sub, sh, name, email, locale } = tmParseJsonObject(text) || {};
  // A new object with known members only: nothing else the JSON carried
  // reaches the app.
  return v === 2 && tmIsText(sub) && tmIsText(sh)
    ? {
        v,
        sub,
        sh,
        // A missing or empty name or email is the empty string either way.
        name: tmIsText(name) ? name : '',
        email: tmIsText(email) ? email : '',
        locale: tmIsText(locale) ? locale : null,
      }
    : null;
}

/**
 * Reads the profile cookie out of a `document.cookie` string. The cookie
 * counts as absent when it is missing, fails to decode, is not version 2, or
 * has an empty subject or session handle.
 *
 * @param cookies - The cookie string, as `document.cookie` returns it.
 * @returns The signed-in user's profile, or `null` when no valid profile cookie is present.
 *
 * @example
 * ```ts
 * const profile = tmParseProfileCookie(document.cookie);
 * if (profile === null) {
 *   // Not signed in.
 * }
 * ```
 */
export function tmParseProfileCookie(cookies: string): TmProfile | null {
  // The first cookie of that name wins: the browser lists the most specific
  // path first, and the server writes the cookie on `/` only.
  for (const pair of cookies.split(';')) {
    const at = pair.indexOf('=');
    if (at >= 0 && pair.slice(0, at).trim() === TM_PROFILE_COOKIE) {
      return tmDecodeProfile(pair.slice(at + 1).trim());
    }
  }
  return null;
}
