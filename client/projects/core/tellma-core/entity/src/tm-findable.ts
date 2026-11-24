// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmDataType, TmJsonObject } from '@tellma/core';
import { tmSearchNormalize } from '@tellma/core/search-normalize';

// The findable rule (spec 0029 §5.2), the client's mirror of the server's
// search lowering (QuerySearch): the id, an integer source or prefix, and
// the "prefix - name" split. The cached search and the paste resolver share
// it; the shared display-name golden pins both sides to the same rows.

/** The display name's fixed separator between its prefix and its name. Internal. */
export const ɵTM_SEPARATOR = ' - ';

/** One column the display name, the search or the paste reads: its path name, wire name and type. */
export interface ɵTmPickerTerm {
  /** The property's name, as Queryex spells it: `Name2`. */
  readonly name: string;
  /** The property's wire name: `name2`. */
  readonly json: string;
  /** The property's type; `null` when unknown. */
  readonly type: TmDataType | null;
}

/** A text column a search matches: from its start, or anywhere in it. Internal. */
export interface ɵTmSearchColumn extends ɵTmPickerTerm {
  /** Whether search text matches the column from its start, as the server matches a prefix-searchable column. */
  readonly prefix: boolean;
}

/**
 * Whether a row's text columns hold the search text, each column as it
 * matches: from its start, or anywhere. Internal: the cached search and
 * its tests read it.
 *
 * @param columns - The text columns.
 * @param values - The row's folded value of each column, `null` where it has none.
 * @param folded - The folded search text.
 * @returns Whether a column matches.
 */
export function ɵtmSearchMatches(columns: readonly ɵTmSearchColumn[], values: readonly (string | null)[], folded: string): boolean {
  return (
    folded !== '' &&
    columns.some((column, at) => {
      const value = values[at];
      return typeof value === 'string' && (column.prefix ? value.startsWith(folded) : value.includes(folded));
    })
  );
}

/**
 * What the findable rule reads of an entity: its key, its display name's
 * admitted slots in slot order (or its one scalar source), and its prefix.
 * Internal.
 */
export interface ɵTmFindablePlan {
  /** The key. */
  readonly key: ɵTmPickerTerm;
  /** The name's admitted slots, or the scalar source alone. */
  readonly slots: readonly ɵTmPickerTerm[];
  /** The prefix, when the rule declares one. */
  readonly prefix: ɵTmPickerTerm | null;
}

/** How the findable rule compares text: loosely for a search, exactly for a paste. */
export interface ɵTmFindableMatch {
  /** Whether a row's text equals the given text. */
  readonly equal: (value: string, text: string) => boolean;
  /** Whether a row's text starts with the given text; equality for a paste. */
  readonly startsWith: (value: string, text: string) => boolean;
  /** Whether the bare id matches, as a search's does; a paste matches `#{id}` alone. */
  readonly bareId: boolean;
}

/**
 * The digits of an integer written in Latin, Arabic-Indic or Extended
 * Arabic-Indic digits, as decimal text with no leading zeros. Internal:
 * the findable rule and the paste's filter read it.
 *
 * @param text - The text.
 * @param native - Whether Arabic-Indic digits read as Latin ones.
 * @returns The canonical digits, or `null` for text that is no integer.
 */
export function ɵtmIntegerOf(text: string, native: boolean): string | null {
  const latin = native
    ? text.replace(/[٠-٩۰-۹]/g, (digit) => String((digit.charCodeAt(0) - 0x0660) % 0x90))
    : text;
  return /^-?[0-9]+$/.test(latin) ? latin.replace(/^(-?)0+(?=\d)/, '$1').replace(/^-0$/, '0') : null;
}

/**
 * A wire value's integer text: a number, or the decimal text of a `long`.
 *
 * @param value - The wire value.
 * @returns The canonical digits, or `null` for a value that is no integer.
 */
function integerValue(value: unknown): string | null {
  if (typeof value === 'number') {
    return Number.isSafeInteger(value) ? String(value) : null;
  }
  return typeof value === 'string' ? ɵtmIntegerOf(value, false) : null;
}

/**
 * Whether a wire value is text the comparison accepts. Internal: the
 * findable rule and the paste's match read it.
 *
 * @param value - The wire value.
 * @param text - The text.
 * @param compare - The comparison.
 * @returns Whether it matches.
 */
export function ɵtmTextMatches(value: unknown, text: string, compare: (value: string, text: string) => boolean): boolean {
  return typeof value === 'string' && compare(value, text);
}

/**
 * The findable rule as a row predicate: text holding the
 * separator splits at its first one, the prefix equal to the left part and
 * the name in an admitted slot starting with (for a paste, equal to) the
 * right part; an integer source or prefix equal to the text, native digits
 * normalized; `#{id}`, and for a search the bare id, equal to the key. A
 * date source is never findable. Internal: the cached search and the paste
 * resolver share it.
 *
 * @param plan - What the rule reads of the entity.
 * @param text - The search or pasted text, without bidi controls and trimmed.
 * @param match - How text compares.
 * @returns Whether a row is found by the text.
 */
export function ɵtmFindable(plan: ɵTmFindablePlan, text: string, match: ɵTmFindableMatch): (row: TmJsonObject) => boolean {
  const tests: ((row: TmJsonObject) => boolean)[] = [];

  // --- The id: `#{id}`, or for a search the id itself, in Latin digits as the server reads it ---
  const idText = text.startsWith('#') ? text.slice(1) : match.bareId ? text : null;
  const id = idText === null ? null : ɵtmIntegerOf(idText, false);
  if (id !== null) {
    tests.push((row) => integerValue(row[plan.key.json]) === id);
  }

  // --- An integer source or prefix: equal to the text, native digits normalized ---
  const number = ɵtmIntegerOf(text, true);
  const source = plan.slots[0];
  const prefix = plan.prefix;
  if (number !== null && source?.type === 'integer') {
    tests.push((row) => integerValue(row[source.json]) === number);
  }
  if (number !== null && prefix?.type === 'integer') {
    tests.push((row) => integerValue(row[prefix.json]) === number);
  }

  // --- The split: "prefix - name", over a text source's admitted slots ---
  const at = text.indexOf(ɵTM_SEPARATOR);
  if (at >= 0 && prefix !== null && source?.type === 'string') {
    const left = text.slice(0, at).trim();
    const right = text.slice(at + ɵTM_SEPARATOR.length).trim();
    const leftNumber = prefix.type === 'integer' ? ɵtmIntegerOf(left, true) : null;
    const prefixTest =
      left === ''
        ? null
        : prefix.type === 'integer'
          ? leftNumber === null
            ? null
            : (row: TmJsonObject) => integerValue(row[prefix.json]) === leftNumber
          : prefix.type === 'string' || prefix.type === null
            ? (row: TmJsonObject) => ɵtmTextMatches(row[prefix.json], left, match.equal)
            : null;
    if (prefixTest !== null) {
      tests.push((row) => prefixTest(row) && plan.slots.some((slot) => ɵtmTextMatches(row[slot.json], right, match.startsWith)));
    }
  }
  return (row) => tests.some((test) => test(row));
}

/**
 * A search's comparison: the folded texts, as the platform's client-side
 * matching folds them. Internal: the cached search uses it.
 */
export const ɵTM_SEARCH_MATCH: ɵTmFindableMatch = {
  equal: (value, text) => tmSearchNormalize(value) === tmSearchNormalize(text),
  startsWith: (value, text) => tmSearchNormalize(value).startsWith(tmSearchNormalize(text)),
  bareId: true,
};
