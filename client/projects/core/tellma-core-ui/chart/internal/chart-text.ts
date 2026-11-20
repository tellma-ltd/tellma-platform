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
// base mark, run mark, text, run mark, base mark. Only marks are used (LRM
// and RLM, and the ALM that closes a word, below): WebKit ignores the
// isolates in canvas, and embeddings and overrides are never used. Nothing
// else in the library writes these marks, and they never leave the option.
//
// Every word of an AL-class script (Arabic, Syriac, Thaana, Hanifi Rohingya)
// is also closed by an ALM, a deviation from spec 0034 §7.4's "LRM and RLM
// only". WebKit (as
// Playwright's builds for Linux and Windows draw, through HarfBuzz) draws a
// canvas string as its bidi runs, left to right, each starting where the
// one before it left the pen. A right-to-left run's leftmost glyph is its
// logically last one. When that glyph carries a vertical offset (an Arabic
// final form joined cursively, such as the teh marbuta of "النقدية"),
// WebKit moves the whole run's baseline by it and carries the move into
// every run drawn after it: the Arabic word, and a Latin code or name drawn
// after it, sit a pixel or more off the line, three or more at 48px. A
// zero-width mark as the word's last character becomes the run's leftmost
// glyph and has no offset. It must be ALM, which the Arabic face draws: RLM,
// which the Latin face draws, stops the carry but leaves the word itself off
// the line. Of the same class as the letter before it, the mark changes no
// bidi resolution (a following number reads as it did, where RLM after an
// Arabic letter would reorder "35%"), it is transparent to cursive joining,
// and Chromium and Firefox draw the string pixel for pixel as without it.
// It goes after the word's harakat, tatweel and zero-width joiner (between a
// letter and its joiner it would break the join) and before a zero-width
// non-joiner, which ends the word. The words are closed once, on the whole
// string, so a word split across two pieces is closed only at its end.
// Hebrew and the other R-class scripts are left as they are: no face the
// platform ships draws them, and their mark (RLM) is drawn by the Latin face.
// A label the engine truncates (the side legend, a horizontal bar chart's
// category axis) loses its closing mark with the text it cuts, so in WebKit
// a cut Arabic word and the ellipsis after it can still sit off the line.
// An ALM at the start of the ellipsis would close the cut word, but it also
// parts a Latin label from its ellipsis, which then lose their kerning in
// every engine, so the engine's plain ellipsis is kept.

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
/** ARABIC LETTER MARK, of the bidi class AL like the Arabic letters. */
const ALM = '\u061C';

/**
 * Letters of the right-to-left scripts (Arabic, Hebrew, Syriac, Thaana,
 * N'Ko, Samaritan, Mandaic, Adlam, Hanifi Rohingya, Yezidi), whose first
 * occurrence makes a run right-to-left.
 */
const RTL_LETTER =
  /[\p{sc=Arab}\p{sc=Hebr}\p{sc=Syrc}\p{sc=Thaa}\p{sc=Nkoo}\p{sc=Samr}\p{sc=Mand}\p{sc=Adlm}\p{sc=Rohg}\p{sc=Yezi}]/u;
/** A letter of any script. */
const LETTER = /\p{L}/u;
/** The Arabic-Indic digits, of the bidi class AN (the extended ones, U+06F0–U+06F9, are EN). */
const ARABIC_INDIC_DIGIT = /[\u0660-\u0669]/;
/**
 * The end of a word in a script whose letters are of the bidi class AL
 * (Arabic, Syriac, Thaana, Hanifi Rohingya): its last letter, then the
 * combining marks, tatweel and zero-width joiner that belong to it, where
 * no more of the word follows. A mark there never parts a haraka from its
 * letter or a joiner from the letter it joins.
 */
const AL_WORD_END =
  /(?=\p{L})[\p{sc=Arab}\p{sc=Syrc}\p{sc=Thaa}\p{sc=Rohg}](?:[\p{M}\u0640]|\u200D)*(?!(?:[\p{M}\u0640]|\u200D)|(?=\p{L})[\p{sc=Arab}\p{sc=Syrc}\p{sc=Thaa}\p{sc=Rohg}])/gu;
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
  return RTL_LETTER.test(LETTER.exec(text)?.[0] ?? '') ? 'rtl' : 'ltr';
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
 * Closes every word of an AL-class script with an ALM, after its
 * combining marks, tatweel and joiner: the run's leftmost glyph wherever the
 * word ends a right-to-left run (see the header).
 */
function closeAlWords(text: string): string {
  return text.replace(AL_WORD_END, `$&${ALM}`);
}

/**
 * Builds one canvas-drawn string from its pieces for a base direction.
 * Every incoming bidi control is stripped first. A run whose direction
 * differs from the base is wrapped as base mark, run mark, text, run mark,
 * base mark; a run in the base direction and a `{ base }` piece are
 * otherwise written as they are. Every word of an AL-class script (Arabic)
 * is then closed by an ALM, which keeps WebKit from drawing the word and the
 * text after it off the baseline. A left-to-right run holding Arabic-Indic digits, inside a
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
  // Section: every Arabic-class word closed, once, on the whole string.
  return closeAlWords(text);
}
