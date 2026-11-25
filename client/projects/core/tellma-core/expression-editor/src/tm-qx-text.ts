// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmQxLex } from '@tellma/core/entity';

// What the expression editor reads from and writes into Queryex text beside
// the lexer (spec 0029 §8.1, §8.2): the parameters a text names, grouped as
// the engine groups them, and the insertion of a path or a snippet at a
// caret without splitting a token. The lexer stays in `/entity`, which the
// select plans and filter leaves need on the first frame; these two serve
// only the editor, the path picker's consumers and report parameters, so
// they load with the editor.

/** One parameter of a text: its name and every place it is written. */
export interface TmQxParameterSites {
  /** The name, without `@`, as it is first spelled. */
  readonly name: string;
  /** Each occurrence's whole token, `@` included, in order. */
  readonly sites: readonly { readonly start: number; readonly length: number }[];
}

/** Where an insertion lands, and the text it adds there. Internal: the editor applies it as one native edit. */
export interface ɵTmQxInsertion {
  /** The new text. */
  readonly text: string;
  /** Where the added text goes in the old text: the caret, moved out of any token it fell inside. */
  readonly at: number;
  /** The added text: the insertion with the spaces that keep it apart from its neighbours. */
  readonly added: string;
  /** Where the insertion starts in the new text, spaces aside. */
  readonly start: number;
  /** Where the insertion ends in the new text, spaces aside. */
  readonly end: number;
}

/**
 * A name folded as the engine compares names without regard to case: each
 * code unit upper-cased by itself, except where that would turn a
 * non-ASCII letter into an ASCII one (the dotless i, the long s), which
 * the engine's ordinal comparison never does. Internal: the draft matches
 * parameter names by it.
 *
 * @param name - The name.
 * @returns The folded name.
 */
export function ɵtmQxFold(name: string): string {
  let folded = '';
  for (const unit of name.split('')) {
    const upper = unit.toUpperCase();
    folded += upper.length === 1 && !(unit > '\x7f' && upper <= '\x7f') ? upper : unit;
  }
  return folded;
}

/**
 * The parameters of a Queryex text, grouped by name as the engine matches
 * them, without regard to case, under the first spelling. Text inside a
 * string literal is never a parameter.
 *
 * @param text - The text.
 * @returns Each parameter and its sites, in order of first use.
 *
 * @example
 * ```ts
 * tmQxParameters("PostingDate >= @From and Memo != 'a@b' and @from < Due");
 * // [{ name: 'From', sites: [{ start: 15, length: 5 }, { start: 43, length: 5 }] }]
 * ```
 */
export function tmQxParameters(text: string): readonly TmQxParameterSites[] {
  const groups = new Map<string, { readonly name: string; readonly sites: { start: number; length: number }[] }>();
  for (const token of tmQxLex(text).tokens) {
    if (token.kind === 'parameter') {
      const folded = ɵtmQxFold(token.text);
      let group = groups.get(folded);
      if (group === undefined) {
        groups.set(folded, (group = { name: token.text, sites: [] }));
      }
      group.sites.push({ start: token.start, length: token.length });
    }
  }
  return [...groups.values()];
}

/**
 * Whether a character ends a token that a following word would run into:
 * a name's part, a digit, `)` or a bracketed name's `]`.
 *
 * @param char - The character, or `undefined` at the start.
 * @returns Whether a space must separate it from a word.
 */
function endsWord(char: string | undefined): boolean {
  return char !== undefined && /[\p{L}\p{N}_)\]]/u.test(char);
}

/**
 * Whether a character starts a token that a preceding word would run into:
 * a name's part, a digit, a bracketed name's `[` or a parameter's `@`.
 *
 * @param char - The character, or `undefined` at the end.
 * @returns Whether a space must separate it from a word.
 */
function startsWord(char: string | undefined): boolean {
  return char !== undefined && /[\p{L}\p{N}_[@]/u.test(char);
}

/**
 * Whether two characters read as fewer tokens together than apart: two
 * quotes that make one string, `<` and `=` that make `<=`, a point and a
 * digit that make a malformed number.
 *
 * @param left - The character before the boundary, or `undefined` at the start.
 * @param right - The character after it, or `undefined` at the end.
 * @returns Whether a space must keep them apart.
 */
function fuses(left: string | undefined, right: string | undefined): boolean {
  return left !== undefined && right !== undefined && tmQxLex(left + right).tokens.length < tmQxLex(left).tokens.length + tmQxLex(right).tokens.length;
}

/**
 * Plans an insertion at a caret: where it lands and the text it adds.
 * Internal: `tmQxInsert` and the editor's native edits share it.
 *
 * @param text - The text.
 * @param caret - The caret, in UTF-16 code units; clamped to the text.
 * @param insertion - The text to insert.
 * @returns The insertion's plan.
 */
export function ɵtmQxPlanInsert(text: string, caret: number, insertion: string): ɵTmQxInsertion {
  // --- The caret, out of any token it falls inside ---
  let at = Math.min(Math.max(Math.trunc(Number.isFinite(caret) ? caret : text.length), 0), text.length);
  for (const token of tmQxLex(text).tokens) {
    if (token.start < at && at < token.start + token.length) {
      at = token.start + token.length;
      break;
    }
  }
  if (insertion === '') {
    return { text, at, added: '', start: at, end: at };
  }

  // --- A space on each side where a word would otherwise run into its neighbour ---
  const before = text.slice(0, at);
  const after = text.slice(at);
  // The characters either side of each boundary, whole code points.
  const last = (part: string): string | undefined => Array.from(part.slice(-2)).at(-1);
  const first = (part: string): string | undefined => Array.from(part.slice(0, 2))[0];
  const apart = (left: string, right: string): boolean =>
    (endsWord(last(left)) && startsWord(first(right))) || fuses(last(left), first(right));
  const lead = apart(before, insertion) ? ' ' : '';
  const trail = apart(insertion, after) ? ' ' : '';
  const added = `${lead}${insertion}${trail}`;
  const start = at + lead.length;
  return { text: `${before}${added}${after}`, at, added, start, end: start + insertion.length };
}

/**
 * Inserts text at a caret without splitting a token: an insertion inside a
 * token moves to the token's end, and a space is added where the insertion
 * would touch a name, a digit or `)` beside it, or would run into the
 * token beside it.
 *
 * @param text - The text.
 * @param caret - The caret, in UTF-16 code units; clamped to the text.
 * @param insertion - The text to insert, such as a path.
 * @returns The new text and the range the insertion occupies in it, spaces aside.
 *
 * @example
 * ```ts
 * tmQxInsert('a and', 5, 'b');     // { text: 'a and b', start: 6, end: 7 }
 * tmQxInsert('Amount', 3, ' + 1'); // { text: 'Amount + 1', start: 6, end: 10 }
 * ```
 */
export function tmQxInsert(text: string, caret: number, insertion: string): { readonly text: string; readonly start: number; readonly end: number } {
  const { text: inserted, start, end } = ɵtmQxPlanInsert(text, caret, insertion);
  return { text: inserted, start, end };
}
