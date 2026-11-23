// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmStripBidiControls } from '@tellma/core-ui/l10n';

// Search normalization (spec 0027 §9.4). Matching folds a query and every
// term it is matched against through the same steps, in this order:
//
// 0. strip the bidi controls a paste can carry, which keeps ZWNJ and ZWJ;
// 1. decompose (NFKD), drop the Latin combining marks and the Arabic marks
//    (tashkeel, the superscript alef, the Quranic annotation signs), and
//    lowercase;
// 2. fold the Arabic letters that are spelt interchangeably, and drop tatweel;
// 3. fold Arabic-Indic and Extended Arabic-Indic digits to 0-9;
// 4. turn punctuation and symbols into spaces, collapse spaces and trim.
//
// NFKD already splits a hamza or madda carrier (أ إ آ ؤ ئ) into its base
// letter and a mark that step 1 drops; step 2 still lists them, so the fold
// does not depend on that.

// Step 1's marks: the Latin combining marks (U+0300-U+036F), the Arabic
// marks (U+064B-U+065F), the superscript alef (U+0670) and the Quranic
// annotation signs (U+06D6-U+06ED).
const MARKS = /[̀-ًͯ-ٰٟۖ-ۭ]/g;

// Step 2's folds: alef with hamza above or below, with madda and alef wasla
// to alef; alef maksura, Farsi yeh and yeh with hamza to yeh; teh marbuta to
// heh; waw with hamza to waw; keheh to kaf; tatweel to nothing. Then step 3's
// digits: Arabic-Indic (U+0660-U+0669) and Extended Arabic-Indic (U+06F0-U+06F9).
const FOLDS: Readonly<Record<string, string>> = {
  'أ': 'ا',
  'إ': 'ا',
  'آ': 'ا',
  'ٱ': 'ا',
  'ى': 'ي',
  'ی': 'ي',
  'ئ': 'ي',
  'ة': 'ه',
  'ؤ': 'و',
  'ک': 'ك',
  'ـ': '',
};
const LETTERS_AND_DIGITS = /[آأؤإئةـىٱکی٠-٩۰-۹]/g;

// Step 4's separators: every punctuation and symbol character, then runs of
// white space.
const PUNCTUATION = /[\p{P}\p{S}]/gu;
const SPACES = /\s+/g;

/** One step-2 or step-3 character, folded. */
function fold(character: string): string {
  const code = character.charCodeAt(0);
  // Each digit block runs 0 to 9 from its first code point.
  if (code >= 0x0660 && code <= 0x0669) {
    return String(code - 0x0660);
  }
  if (code >= 0x06f0 && code <= 0x06f9) {
    return String(code - 0x06f0);
  }
  return FOLDS[character];
}

/**
 * Folds text for matching: the same text typed with or without diacritics,
 * tashkeel or tatweel, in either spelling of the interchangeable Arabic
 * letters, with Arabic-Indic or Latin digits, or pasted with invisible
 * direction marks, folds to the same string. Matching compares the folded
 * query with folded terms; the folded text is never shown.
 *
 * Bidi controls are removed (the zero-width non-joiner and joiner stay, as
 * they are spelling). Text is decomposed, Latin combining marks and Arabic
 * marks are removed and the rest is lowercased. أ إ آ ٱ fold to ا, ى ی ئ to
 * ي, ة to ه, ؤ to و and ک to ك, and tatweel is removed. Arabic-Indic and
 * Extended Arabic-Indic digits become 0-9. Punctuation and symbols become
 * spaces, and runs of spaces collapse to one, with none at either end.
 *
 * @example
 * ```ts
 * tmSearchNormalize('Café — الفاتورة ١٢'); // 'cafe الفاتوره 12'
 * ```
 *
 * @param text - The text to fold.
 * @returns The folded text.
 */
export function tmSearchNormalize(text: string): string {
  return tmStripBidiControls(text)
    .normalize('NFKD')
    .replace(MARKS, '')
    .toLowerCase()
    .replace(LETTERS_AND_DIGITS, fold)
    .replace(PUNCTUATION, ' ')
    .replace(SPACES, ' ')
    .trim();
}
