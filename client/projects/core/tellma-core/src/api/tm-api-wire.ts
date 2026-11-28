// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStepUpChallenge } from '../session/tm-session';
import type { TmApiErrorKind, TmProblem } from './tm-api-error';

// The HTTP layer's pure wire rules (spec 0024 §10.2, §10.6; spec 0016 §5.5,
// §6.2, §7): which status and code make which kind, and how the response
// headers the layer reads are parsed. Only the HTTP layer imports this file;
// every other part of the app sees a TmApiError.

/** The internal marker of a response the outdated-client verdict judges: `412 client-outdated` or `404 route-not-found`. */
export const TM_OUTDATED = 'outdated';

/**
 * The kind a refused response maps to, or {@link TM_OUTDATED} when the
 * outdated-client verdict decides it.
 *
 * @param status - The HTTP status.
 * @param code - The problem code, or `null` without a problem body.
 * @returns The kind, or the outdated marker.
 */
export function tmKindOf(status: number, code: string | null): TmApiErrorKind | typeof TM_OUTDATED {
  // Spec 0024 §10.6, row by row. A blob rejection is a validation failure at
  // whatever status its code carries (spec 0017 §8.2).
  if (code === 'blob-rejected') {
    return 'validation';
  }
  switch (`${status} ${code}`) {
    case '400 query-invalid':
      return 'query-invalid';
    case '400 bad-request':
      return 'bad-request';
    case '401 unauthenticated':
      return 'unauthenticated';
    case '401 step-up-required':
      return 'step-up';
    case '403 forbidden':
      return 'forbidden';
    case '403 tenant-read-only':
    case '403 tenant-frozen':
      return 'read-only';
    case '403 tenant-suspended':
      return 'tenant-unavailable';
    case '404 tenant-not-found':
      return 'tenant-not-found';
    case '404 not-found':
      return 'not-found';
    case '404 route-not-found':
    case '412 client-outdated':
      return TM_OUTDATED;
    case '409 concurrency-conflict':
    case '409 count-mismatch':
      return 'conflict';
    case '409 confirmation-required':
      return 'confirmation';
    case '413 limit-exceeded':
      return 'limit';
    case '422 validation':
      return 'validation';
    case '429 rate-limited':
    case '504 timeout':
      return 'unavailable';
    case '502 partial-failure':
      return 'partial-failure';
  }
  return status === 503 ? 'unavailable' : 'server';
}

/**
 * The problem a response body carries: a JSON object with a string `code`,
 * else `null`.
 *
 * @param text - The body's text.
 * @returns The problem, or `null`.
 */
export function tmParseProblem(text: string): TmProblem | null {
  try {
    const value: unknown = JSON.parse(text);
    return typeof value === 'object' && value !== null && !Array.isArray(value) && typeof (value as TmProblem).code === 'string'
      ? (value as TmProblem)
      : null;
  } catch {
    return null;
  }
}

/**
 * The wait a `Retry-After` value asks for, in milliseconds: whole seconds or
 * an HTTP date.
 *
 * @param value - The header's value.
 * @param now - The current time, in epoch milliseconds.
 * @returns The wait, at least 0, or `null` when the header is absent or unreadable.
 */
export function tmRetryAfterMs(value: string | null, now: number): number | null {
  if (value === null || value.trim() === '') {
    return null;
  }
  const text = value.trim();
  if (/^\d+$/.test(text)) {
    return Number(text) * 1000;
  }
  // An HTTP date names its day and month; the engine's lenient parser would
  // otherwise read stray text such as `1.5` as a year.
  const at = /[A-Za-z]/.test(text) ? Date.parse(text) : Number.NaN;
  return Number.isNaN(at) ? null : Math.max(0, at - now);
}

/**
 * The version tags of a `Tellma-Version-Tags` header, such as
 * `settings=1.9f04, permissions=1.3f2a`, by name; `null` when the header is
 * absent or names none.
 *
 * @param value - The header's value.
 * @returns The tags, or `null`.
 */
export function tmParseVersionTags(value: string | null): Readonly<Record<string, string>> | null {
  const tags: Record<string, string> = {};
  let found = false;
  for (const member of (value ?? '').split(',')) {
    const at = member.indexOf('=');
    const name = member.slice(0, at).trim();
    if (at > 0 && name !== '') {
      tags[name] = member.slice(at + 1).trim();
      found = true;
    }
  }
  return found ? tags : null;
}

/**
 * The stronger sign-in a `401 step-up-required` asks for, from its
 * `WWW-Authenticate` header (`Bearer error="insufficient_user_authentication",
 * acr_values="mfa", max_age=300`).
 *
 * @param value - The header's value.
 * @returns The challenge, or `null` when the header names no such challenge.
 */
export function tmParseStepUp(value: string | null): TmStepUpChallenge | null {
  if (value === null || !/error="insufficient_user_authentication"/.test(value)) {
    return null;
  }
  const acr = /acr_values="([^"]+)"/.exec(value)?.[1];
  const maxAge = /max_age=(\d+)/.exec(value)?.[1];
  return acr === undefined ? null : { acr, maxAge: maxAge === undefined ? null : Number(maxAge) };
}

/**
 * Random lowercase hex digits that are not all zero, as W3C trace context
 * requires of a trace id and a span id.
 *
 * @param bytes - How many random bytes to draw (16 for a trace id, 8 for a span id).
 * @param random - Fills an array with random bytes, as `crypto.getRandomValues` does.
 * @returns The hex text, two digits per byte.
 */
export function tmRandomHex(bytes: number, random: (array: Uint8Array<ArrayBuffer>) => Uint8Array<ArrayBuffer>): string {
  const array = random(new Uint8Array(bytes));
  // An all-zero id is invalid; one forced bit keeps it valid without bias that matters.
  if (array.every((byte) => byte === 0)) {
    array[bytes - 1] = 1;
  }
  return Array.from(array, (byte) => byte.toString(16).padStart(2, '0')).join('');
}
