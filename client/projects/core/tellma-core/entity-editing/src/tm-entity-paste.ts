// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmId, TmJsonObject } from '@tellma/core';
import type { TmLabelResolution } from '@tellma/core-ui/contracts';
import { tmStripBidiControls } from '@tellma/core-ui/l10n';

import { ɵtmFindable, ɵtmIntegerOf, ɵtmTextMatches, ɵTM_SEPARATOR, type ɵTmFindableMatch, type ɵTmFindablePlan } from './tm-findable';

// The paste resolver of a grid's entity column (spec 0029 §14.5): the pasted
// labels in one filter leaf per group, each label matched exactly against
// the rows the groups read. `TmEntityPickers` loads this module on the
// first paste, so a page that never pastes never downloads it.

/**
 * The server's Queryex ceilings a paste's filter leaf is sized against: the
 * ones the query pipeline compiles every filter under (the server's
 * `QueryexProfiles.PipelineLimits`, over `QueryexLimits.Default`), and the
 * longest number its lexer reads. The filter's length is the entity's own
 * ceiling, which the metadata sends. Internal: the paste resolver reads it,
 * and its tests.
 */
export const ɵTM_PIPELINE_LIMITS = {
  /** The most items in one comma-separated list, such as an `in` list (`MaxListItems`). */
  maxListItems: 512,
  /** The most parameter slots in a whole query, each literal taking one (`MaxParameters`). */
  maxParameters: 1024,
  /** The most tokens in one expression (`MaxTokens`). */
  maxTokens: 4096,
  /** The deepest nesting of a parsed expression, a connective chain counting toward it (`MaxSyntaxDepth`). */
  maxSyntaxDepth: 64,
  /** The most significant digits a number literal may have; the lexer refuses a longer one (the server's `Lexer`). */
  maxNumberDigits: 38,
} as const;

/**
 * The most literals one paste leaf writes. Each `in` list is a part of them,
 * so every list stays within the list ceiling; they take at most half of
 * the query's parameter slots, leaving the rest to the other leaves, the
 * access filter and the paging; and, a literal and its comma each, a
 * quarter of the token ceiling. The leaf's connectives are a fixed few
 * whatever the labels, so its depth stays far below the depth ceiling.
 */
const LITERALS = Math.min(ɵTM_PIPELINE_LIMITS.maxListItems, ɵTM_PIPELINE_LIMITS.maxParameters / 2);

/**
 * The most reads one paste keeps in flight. The server admits a tenant's
 * requests through a concurrency limiter of 64 permits that queues none, a
 * request beyond them refused at once (429); four overlap a large paste's
 * reads while leaving the tenant's other requests, and its other users',
 * nearly all the permits.
 */
const READS = 4;

/** A paste's filter leaf and the literals it writes. */
interface TmPasteLeaf {
  /** The leaf's text; empty when no label can match. */
  readonly text: string;
  /** The literals its lists hold, all lists together. */
  readonly literals: number;
}

/** Each locale's exact-match collator: case-insensitive, accent-sensitive, as the database compares. */
const EXACT = new Map<string, Intl.Collator>();

/**
 * The paste's comparison in a locale: equal ignoring case, not accents.
 *
 * @param locale - The locale.
 * @returns The match.
 */
function exactMatch(locale: string): ɵTmFindableMatch {
  let collator = EXACT.get(locale);
  if (collator === undefined) {
    EXACT.set(locale, (collator = new Intl.Collator(locale, { usage: 'search', sensitivity: 'accent' })));
  }
  const equal = (value: string, text: string): boolean => collator.compare(value, text) === 0;
  return { equal, startsWith: equal, bareId: false };
}

/**
 * Whether a row is the one a pasted name names: a text slot of the display
 * name, or a text prefix, equal to the name, or the findable rule with
 * equality in place of a prefix match; case is ignored, accents are not.
 * Internal: the paste resolver uses it.
 *
 * @param plan - What the rule reads of the entity.
 * @param text - The pasted name, without bidi controls and trimmed.
 * @param locale - The locale whose collation compares text.
 * @returns The row predicate.
 */
export function ɵtmPasteMatch(plan: ɵTmFindablePlan, text: string, locale: string): (row: TmJsonObject) => boolean {
  const match = exactMatch(locale);
  const findable = ɵtmFindable(plan, text, match);
  const prefix = plan.prefix;
  return (row) =>
    plan.slots.some((slot) => slot.type === 'string' && ɵtmTextMatches(row[slot.json], text, match.equal)) ||
    (prefix?.type === 'string' && ɵtmTextMatches(row[prefix.json], text, match.equal)) ||
    findable(row);
}

/**
 * The digits of an integer label as a Queryex number literal: the integer
 * the text reads, or `null` when it reads none or more digits than the
 * server's lexer accepts, which no integer column holds either.
 *
 * @param text - The text.
 * @param native - Whether Arabic-Indic digits read as Latin ones.
 * @returns The literal, or `null`.
 */
function integerLiteral(text: string, native: boolean): string | null {
  const digits = ɵtmIntegerOf(text, native);
  return digits !== null && digits.replace('-', '').length <= ɵTM_PIPELINE_LIMITS.maxNumberDigits ? digits : null;
}

/**
 * A Queryex string literal: the text in single quotes, a quote written
 * twice.
 *
 * @param text - The text.
 * @returns The literal.
 */
function quote(text: string): string {
  return `'${text.replaceAll("'", "''")}'`;
}

/**
 * The one filter leaf of a paste's labels: every admitted text slot and a
 * text prefix `in` the labels, an integer source or prefix `in` the labels
 * that read as integers, the key `in` the `#{id}` labels, and the labels
 * holding the separator as one term, the prefix `in` their left halves and
 * the name `in` their right ones. The split term finds a superset of its
 * labels' rows, which the exact match narrows; one term keeps the leaf's
 * depth the same however many labels it holds. A number longer than the
 * server's lexer reads is left out, as no integer column can hold it.
 * Internal: the paste resolver writes it, and its tests read it.
 *
 * @param plan - What the rule reads of the entity.
 * @param texts - The labels, without bidi controls and trimmed.
 * @returns The leaf and its literals.
 */
export function ɵtmPasteLeaf(plan: ɵTmFindablePlan, texts: readonly string[]): TmPasteLeaf {
  const terms: string[] = [];
  let literals = 0;
  const list = (items: ReadonlySet<string>): string => {
    literals += items.size;
    return `(${[...items].join(', ')})`;
  };
  const strings = new Set(texts.map(quote));
  const numbers = new Set(texts.map((text) => integerLiteral(text, true)).filter((text) => text !== null));
  const ids = new Set(texts.filter((text) => text.startsWith('#')).map((text) => integerLiteral(text.slice(1), false)).filter((id) => id !== null));
  const textSlots = plan.slots.filter((slot) => slot.type === 'string');
  const source = plan.slots[0];
  const prefix = plan.prefix;

  // --- The display name's columns, each slot's and the prefix's ---
  for (const slot of textSlots) {
    terms.push(`${slot.name} in ${list(strings)}`);
  }
  if (prefix?.type === 'string') {
    terms.push(`${prefix.name} in ${list(strings)}`);
  }
  if (numbers.size > 0 && source?.type === 'integer') {
    terms.push(`${source.name} in ${list(numbers)}`);
  }
  if (numbers.size > 0 && prefix?.type === 'integer') {
    terms.push(`${prefix.name} in ${list(numbers)}`);
  }
  if (ids.size > 0) {
    terms.push(`${plan.key.name} in ${list(ids)}`);
  }

  // --- The split: every label's halves in one term, equality in place of a prefix match ---
  if (prefix !== null && source?.type === 'string') {
    const lefts = new Set<string>();
    const rights = new Set<string>();
    for (const text of texts) {
      const at = text.indexOf(ɵTM_SEPARATOR);
      const left = at < 0 ? '' : text.slice(0, at).trim();
      const side = left === '' ? null : prefix.type === 'integer' ? integerLiteral(left, true) : prefix.type === 'string' ? quote(left) : null;
      if (side !== null) {
        lefts.add(side);
        rights.add(quote(text.slice(at + ɵTM_SEPARATOR.length).trim()));
      }
    }
    if (lefts.size > 0) {
      const prefixes = list(lefts);
      const names = textSlots.map((slot) => `${slot.name} in ${list(rights)}`);
      terms.push(`(${prefix.name} in ${prefixes} and (${names.join(' or ')}))`);
    }
  }
  return { text: terms.join(' or '), literals };
}

/**
 * The labels in groups whose leaf fits: within the room its length has
 * beside the other leaves, and within the literals the server's ceilings
 * allow one filter. Each group is the longest run of the labels left whose
 * leaf fits, so the groups are as few as taking the labels one at a time
 * would make them. Internal: the paste resolver groups its labels with it,
 * and its tests.
 *
 * @param plan - What the rule reads of the entity.
 * @param texts - The distinct labels, without bidi controls and trimmed, each fitting a leaf alone.
 * @param room - The longest the leaf may be.
 * @returns The groups, in the labels' order.
 */
export function ɵtmPasteGroups(plan: ɵTmFindablePlan, texts: readonly string[], room: number): string[][] {
  const fits = (start: number, size: number): boolean => {
    const leaf = ɵtmPasteLeaf(plan, texts.slice(start, start + size));
    return leaf.text.length <= room && leaf.literals <= LITERALS;
  };
  const groups: string[][] = [];
  for (let start = 0; start < texts.length; ) {
    const left = texts.length - start;
    // --- The longest run that fits, found by doubling its size and then halving the gap ---
    // A leaf only grows as labels join it: each list is a set that gains
    // items, and a term appears once its list has any. So the sizes that fit
    // are all those below the first that does not, and a search finds that
    // one in a few leaves where trying each size would write one per label,
    // each as long as the group. A label fits a leaf alone, so a run holds
    // at least one; `over` is the least size known not to fit, past the
    // labels left until one is found, and the size doubles until then.
    let fit = 1;
    let over = left + 1;
    while (over - fit > 1) {
      const size = over > left ? Math.min(fit * 2, left) : (fit + over) >>> 1;
      if (fits(start, size)) {
        fit = size;
      } else {
        over = size;
      }
    }
    groups.push(texts.slice(start, start + fit));
    start += fit;
  }
  return groups;
}

/**
 * A pool that runs at most a number of tasks at once, the others waiting
 * their turn in order.
 *
 * @param size - The most tasks running at once.
 * @returns Runs a task in the pool.
 */
function pool(size: number): <T>(task: () => Promise<T>) => Promise<T> {
  let running = 0;
  const waiting: (() => void)[] = [];
  return async <T>(task: () => Promise<T>): Promise<T> => {
    if (running < size) {
      running++;
    } else {
      await new Promise<void>((resolve) => waiting.push(resolve));
    }
    try {
      return await task();
    } finally {
      // The slot passes straight to the next task waiting, so no newcomer takes it in between.
      const next = waiting.shift();
      if (next === undefined) {
        running--;
      } else {
        next();
      }
    }
  };
}

/**
 * Resolves pasted labels with as few reads as the server's filter limits
 * allow: one, unless the labels are too many for one filter, and a few at
 * once. A label one row matches exactly resolves to that row's key; none
 * is `notFound`, and several `ambiguous`. A label no filter can carry is
 * `notFound` with no read. The labels of a read that fails are
 * `resolutionFailed`, which a paste again retries, while the other reads'
 * labels resolve. A full page may stop short of a label's rows, so it
 * decides only the labels it shows ambiguous; the others are read again in
 * halves, and a lone label whose own page is full is `ambiguous`. Internal:
 * `TmEntityPickers` calls it on a paste.
 *
 * @param plan - What the rule reads of the entity.
 * @param labels - The pasted labels.
 * @param room - The longest a group's leaf may be beside the other leaves.
 * @param take - The most rows a read returns, a full page holding this many.
 * @param locale - The locale whose collation compares text.
 * @param read - Reads the rows a leaf finds, beside the other leaves.
 * @returns Each label's resolution.
 */
export async function ɵtmResolveLabels(
  plan: ɵTmFindablePlan,
  labels: readonly string[],
  room: number,
  take: number,
  locale: string,
  read: (leaf: string) => Promise<readonly TmJsonObject[]>,
): Promise<ReadonlyMap<string, TmLabelResolution<TmId>>> {
  // --- Each distinct text once, without bidi controls; an empty one is found nowhere ---
  const resolutions = new Map<string, TmLabelResolution<TmId>>();
  const texts = new Map<string, string[]>();
  for (const label of labels) {
    const text = tmStripBidiControls(label).trim();
    if (text === '') {
      resolutions.set(label, { error: 'notFound' });
    } else {
      // Added to in place: a column pasting one name in every row adds it once per row.
      const same = texts.get(text);
      if (same === undefined) {
        texts.set(text, [label]);
      } else {
        same.push(label);
      }
    }
  }
  const decide = (text: string, resolution: TmLabelResolution<TmId>): void => {
    for (const label of texts.get(text)!) {
      resolutions.set(label, resolution);
    }
  };

  // --- A text too long for a leaf of its own is one the server never accepts: found nowhere, unread ---
  const sendable = [...texts.keys()].filter((text) => {
    const fits = ɵtmPasteLeaf(plan, [text]).text.length <= room;
    if (!fits) {
      decide(text, { error: 'notFound' });
    }
    return fits;
  });

  // --- One read per group, a few at once; a failed read leaves its own texts to a retry ---
  const limit = pool(READS);
  const settle = async (group: readonly string[]): Promise<void> => {
    const leaf = ɵtmPasteLeaf(plan, group).text;
    let rows: readonly TmJsonObject[];
    try {
      rows = leaf === '' ? [] : await limit(() => read(leaf));
    } catch {
      group.forEach((text) => decide(text, { error: 'resolutionFailed' }));
      return;
    }

    // --- Each text keeps the rows it alone matches; a full page decides only what it shows ambiguous ---
    const full = rows.length >= take;
    const open: string[] = [];
    for (const text of group) {
      const pasted = ɵtmPasteMatch(plan, text, locale);
      const ids = new Map<string, TmId>();
      for (const row of rows) {
        if (pasted(row)) {
          const id = row[plan.key.json] as TmId;
          ids.set(String(id), id);
        }
      }
      if (ids.size > 1 || (full && group.length === 1)) {
        decide(text, { error: 'ambiguous' });
      } else if (full) {
        open.push(text);
      } else {
        decide(text, ids.size === 1 ? { value: [...ids.values()][0]! } : { error: 'notFound' });
      }
    }

    // --- The texts a full page left open, read again in halves ---
    const half = Math.ceil(open.length / 2);
    await Promise.all([open.slice(0, half), open.slice(half)].filter((part) => part.length > 0).map(settle));
  };
  await Promise.all(ɵtmPasteGroups(plan, sendable, room).map(settle));
  return resolutions;
}
