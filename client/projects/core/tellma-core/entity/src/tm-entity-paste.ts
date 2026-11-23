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
 * `QueryexProfiles.PipelineLimits`, over `QueryexLimits.Default`). The
 * filter's length is the entity's own ceiling, which the metadata sends.
 * Internal: the paste resolver reads it, and its tests.
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
 * depth the same however many labels it holds. Internal: the paste
 * resolver writes it, and its tests read it.
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
  const numbers = new Set(texts.map((text) => ɵtmIntegerOf(text, true)).filter((text) => text !== null));
  const ids = new Set(texts.filter((text) => text.startsWith('#')).map((text) => ɵtmIntegerOf(text.slice(1), false)).filter((id) => id !== null));
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
      const side = left === '' ? null : prefix.type === 'integer' ? ɵtmIntegerOf(left, true) : prefix.type === 'string' ? quote(left) : null;
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
 * allow one filter. A label too long for a leaf of its own goes alone.
 *
 * @param plan - What the rule reads of the entity.
 * @param texts - The distinct labels, without bidi controls and trimmed.
 * @param room - The longest the leaf may be.
 * @returns The groups, in the labels' order.
 */
function groupsOf(plan: ɵTmFindablePlan, texts: readonly string[], room: number): string[][] {
  const groups: string[][] = [];
  let group: string[] = [];
  for (const text of texts) {
    if (group.length > 0) {
      const leaf = ɵtmPasteLeaf(plan, [...group, text]);
      if (leaf.text.length > room || leaf.literals > LITERALS) {
        groups.push(group);
        group = [];
      }
    }
    group.push(text);
  }
  if (group.length > 0) {
    groups.push(group);
  }
  return groups;
}

/**
 * Resolves pasted labels with as few reads as the server's filter limits
 * allow: one, unless the labels are too many for one filter. A label one
 * row matches exactly resolves to that row's key; none is `notFound`, and
 * several `ambiguous`. Internal: `TmEntityPickers` calls it on a paste.
 *
 * @param plan - What the rule reads of the entity.
 * @param labels - The pasted labels.
 * @param room - The longest a group's leaf may be beside the other leaves.
 * @param locale - The locale whose collation compares text.
 * @param read - Reads the rows a leaf finds, beside the other leaves.
 * @returns Each label's resolution.
 */
export async function ɵtmResolveLabels(
  plan: ɵTmFindablePlan,
  labels: readonly string[],
  room: number,
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
      texts.set(text, [...(texts.get(text) ?? []), label]);
    }
  }

  // --- One read per group; each text keeps the rows it alone matches ---
  const reads = groupsOf(plan, [...texts.keys()], room).map(async (group) => {
    const leaf = ɵtmPasteLeaf(plan, group).text;
    return { group, rows: leaf === '' ? [] : await read(leaf) };
  });
  for (const { group, rows } of await Promise.all(reads)) {
    for (const text of group) {
      const pasted = ɵtmPasteMatch(plan, text, locale);
      const ids = new Map<string, TmId>();
      for (const row of rows) {
        if (pasted(row)) {
          const id = row[plan.key.json] as TmId;
          ids.set(String(id), id);
        }
      }
      const resolution: TmLabelResolution<TmId> =
        ids.size === 1 ? { value: [...ids.values()][0]! } : { error: ids.size === 0 ? 'notFound' : 'ambiguous' };
      for (const label of texts.get(text)!) {
        resolutions.set(label, resolution);
      }
    }
  }
  return resolutions;
}
