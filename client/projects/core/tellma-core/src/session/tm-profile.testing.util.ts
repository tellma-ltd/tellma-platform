// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Profile cookies for the specs, encoded as the server writes them: the
// unpadded base64url of the UTF-8 JSON.

/**
 * The unpadded base64url of a text's UTF-8 bytes.
 *
 * @param text - The text.
 * @returns The encoded text.
 */
export function tmBase64Url(text: string): string {
  let binary = '';
  for (const byte of new TextEncoder().encode(text)) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/**
 * The profile cookie's value for a JSON value.
 *
 * @param value - The value the cookie carries.
 * @returns The cookie's value.
 */
export function tmProfileCookieValue(value: unknown): string {
  return tmBase64Url(JSON.stringify(value));
}

/**
 * A `document.cookie` string holding a profile cookie for a user, among
 * other cookies.
 *
 * @param sub - The user's subject identifier.
 * @param locale - The user's locale, or `null`.
 * @returns The cookie string.
 */
export function tmProfileCookies(sub: string, locale: string | null = null): string {
  const value = tmProfileCookieValue({ v: 2, sub, sh: `sh-${sub}`, name: 'Farah Nasser', email: 'farah@acme.example', locale });
  return `theme=dark; tellma.profile=${value}`;
}
