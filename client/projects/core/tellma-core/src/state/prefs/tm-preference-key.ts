// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The preference keys (spec 0025 §9.1, Appendix A). A key names one entry of
// the user's per-tenant preference bag, the value it falls back to, and how
// soon a change reaches the server. Keys are declared once, at module level,
// so the grammar is checked where the key is written, in development only:
// production builds drop the check with its message.

declare const ngDevMode: unknown;

/**
 * How soon a changed preference reaches the server: `curated` at once (a
 * deliberate choice such as a pin), `write-behind` coalesced over a few
 * seconds (a by-product of use such as a column width).
 */
export type TmPreferenceKind = 'curated' | 'write-behind';

/**
 * One preference: its key in the bag, its kind, the value it reads as when
 * unset or invalid, and an optional check of a stored value's shape.
 */
export interface TmPreferenceKey<T> {
  /** The key in the bag, such as `acme.creditReview.threshold`. */
  readonly key: string;
  /** How soon a change reaches the server. */
  readonly kind: TmPreferenceKind;
  /** The value the preference reads as when unset, or when its stored value is invalid. */
  readonly fallback: T;
  /** Whether a stored value has the preference's shape; a value failing it reads as the fallback. */
  readonly validate?: (value: unknown) => value is T;
}

/** The key grammar the server enforces: dotted segments, the first lower-case-led, at most 128 characters. */
const KEY_GRAMMAR = /^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9][a-zA-Z0-9-]*)*$/;

/**
 * Declares a preference key. In development, a key that breaks the server's
 * grammar throws here, and a first segment that is neither a platform
 * segment nor a composed feature's key throws at its first use.
 *
 * @param key - The key in the bag, such as `acme.creditReview.threshold`.
 * @param kind - How soon a change reaches the server.
 * @param fallback - The value the preference reads as when unset or invalid.
 * @param validate - Whether a stored value has the preference's shape.
 * @returns The key.
 * @throws Error in development, when the key breaks the grammar.
 *
 * @example
 * ```ts
 * const thresholdKey = tmPreferenceKey<number>('acme.creditReview.threshold', 'write-behind', 90);
 * ```
 */
export function tmPreferenceKey<T>(
  key: string,
  kind: TmPreferenceKind,
  fallback: T,
  validate?: (value: unknown) => value is T,
): TmPreferenceKey<T> {
  if (typeof ngDevMode !== 'undefined' && ngDevMode && !(key.length <= 128 && KEY_GRAMMAR.test(key))) {
    throw new Error(
      `The preference key '${key}' breaks the key grammar: dotted segments, the first starting with a lower-case letter, at most 128 characters.`,
    );
  }
  return validate ? { key, kind, fallback, validate } : { key, kind, fallback };
}
