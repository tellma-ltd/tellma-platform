// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The bidi-control strip. Formatted text never carries an invisible
// direction mark: markup sets direction (a `dir` attribute, `<bdi>`), so a
// displayed value is exactly its characters, and a copied date or amount
// pastes into a spreadsheet as a date or a number.

/**
 * The invisible bidirectional control characters, as a global pattern: the
 * Arabic letter mark (U+061C), the left-to-right and right-to-left marks
 * (U+200E, U+200F), the embeddings, overrides and their terminator
 * (U+202A–U+202E) and the isolates and their terminator (U+2066–U+2069).
 * The zero-width joiner and non-joiner (U+200D, U+200C) are spelling, not
 * direction, and are not part of the set.
 *
 * The pattern is global, so it carries a `lastIndex`: use it with
 * `replace`, `replaceAll` or `matchAll`, which ignore that state, and
 * never with `test` or `exec`.
 */
export const TM_BIDI_CONTROLS = /[\u061C\u200E\u200F\u202A-\u202E\u2066-\u2069]/g;

/**
 * Removes every bidirectional control character ({@link TM_BIDI_CONTROLS})
 * from `text`. Every formatter of this entry point returns text that has
 * been through it; apply it to text that arrives from elsewhere (a paste,
 * a search box) before comparing or matching.
 */
export function tmStripBidiControls(text: string): string {
  return text.replace(TM_BIDI_CONTROLS, '');
}
