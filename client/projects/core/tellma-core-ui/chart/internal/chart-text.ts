// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmStripBidiControls } from '@tellma/core-ui/l10n';

// Canvas text, the platform's one exception to markup-only bidi (spec 0024
// §7.9, spec 0034 §7.4). No markup reaches canvas pixels and nobody selects,
// copies or finds them, so each canvas-drawn string of the ECharts option
// carries its runs' directions as LRM and RLM marks. The canvas lays the
// whole string out from the page's base direction, which its element takes
// through CSS; a run whose direction differs from the base is wrapped as
// base mark, run mark, text, run mark, base mark. Only LRM and RLM are
// used: WebKit ignores the isolates in canvas, and embeddings and overrides
// are never used. Nothing else in the library writes these marks, and they
// never leave the option.

/** A text direction. */
export type ɵTmTextDirection = 'ltr' | 'rtl';

/**
 * One piece of a canvas string: a run (a member's part or label, a
 * formatted number, a measure or dimension label), which takes its own
 * direction, or `{ base }`, text in the page's language (a translated
 * template's words, a separator), which reads in the base direction.
 */
export type ɵTmCanvasPiece = string | { readonly base: string };

/** LEFT-TO-RIGHT MARK. */
const LRM = '\u200E';
/** RIGHT-TO-LEFT MARK. */
const RLM = '\u200F';

/** Letters of the right-to-left scripts, whose first occurrence makes a run right-to-left. */
const RTL_LETTER =
  /[\p{Script=Arabic}\p{Script=Hebrew}\p{Script=Syriac}\p{Script=Thaana}\p{Script=Nko}\p{Script=Samaritan}\p{Script=Mandaic}\p{Script=Adlam}\p{Script=Hanifi_Rohingya}\p{Script=Yezidi}]/u;
/** A letter of any script. */
const LETTER = /\p{L}/u;
/** The Arabic-Indic digits, of the bidi class AN (the extended ones, U+06F0–U+06F9, are EN). */
const ARABIC_INDIC_DIGIT = /[\u0660-\u0669]/;
/**
 * A character of a number that is neither a digit nor a letter nor a
 * separator that joins digits: a sign, a percent or per-mille sign, a space.
 * Beside an Arabic-Indic digit such a character takes the paragraph's
 * direction unless strong left-to-right text sits on both its sides.
 */
const NUMBER_NEUTRAL = /[^\p{Nd}\p{L}\p{M}.,\u066B\u066C]/gu;
/**
 * The start of a zrender rich-text token, `{name|`: text drawn with rich
 * styles (the tooltip's) would read it as markup, so the brace is kept apart
 * from the name by a mark of the text's own direction.
 */
const RICH_TOKEN = /\{(?=[A-Za-z0-9_]+\|)/g;

/**
 * A run's direction: that of its first strong character, a letter, and
 * left-to-right for text with none, such as a number.
 */
export function ɵtmRunDirection(text: string): ɵTmTextDirection {
  for (const character of text) {
    if (LETTER.test(character)) {
      return RTL_LETTER.test(character) ? 'rtl' : 'ltr';
    }
  }
  return 'ltr';
}

/** The mark of a direction. */
function markOf(direction: ɵTmTextDirection): string {
  return direction === 'rtl' ? RLM : LRM;
}

/** Keeps a rich-text token's brace apart from its name, with a mark of the text's direction. */
function unrich(text: string, direction: ɵTmTextDirection): string {
  return text.replace(RICH_TOKEN, `{${markOf(direction)}`);
}

/**
 * Builds one canvas-drawn string from its pieces for a base direction.
 * Every incoming bidi control is stripped first. A run whose direction
 * differs from the base is wrapped as base mark, run mark, text, run mark,
 * base mark; a run in the base direction and a `{ base }` piece are written
 * as they are. A left-to-right run holding Arabic-Indic digits, inside a
 * right-to-left base, also takes an LRM on each side of its signs, so a
 * minus or a percent sign stays where the run drawn on its own puts it.
 *
 * @param pieces - The string's runs and base-direction pieces, in logical order.
 * @param base - The page's direction, which the canvas lays the string out from.
 * @returns The string to hand the engine.
 */
export function ɵtmCanvasText(pieces: readonly ɵTmCanvasPiece[], base: ɵTmTextDirection): string {
  let text = '';
  for (const piece of pieces) {
    // Section: a base-direction piece is written as it is.
    if (typeof piece !== 'string') {
      text += unrich(tmStripBidiControls(piece.base), base);
      continue;
    }
    // Section: a run, wrapped when its direction differs from the base.
    const run = tmStripBidiControls(piece);
    const direction = ɵtmRunDirection(run);
    if (direction === base) {
      text += unrich(run, direction);
      continue;
    }
    let body = unrich(run, direction);
    if (direction === 'ltr' && ARABIC_INDIC_DIGIT.test(body)) {
      body = body.replace(NUMBER_NEUTRAL, (neutral) => `${LRM}${neutral}${LRM}`);
    }
    const outer = markOf(base);
    const inner = markOf(direction);
    text += `${outer}${inner}${body}${inner}${outer}`;
  }
  return text;
}
