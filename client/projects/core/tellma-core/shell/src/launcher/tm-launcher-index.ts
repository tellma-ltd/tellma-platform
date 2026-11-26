// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmNavItem } from '@tellma/core';
import { tmSearchNormalize } from '@tellma/core/search-normalize';

// The launcher's page search (spec 0027 §9.4): an index of normalized terms
// built once per change of its candidates, and a query over it that runs on
// every keystroke. Both the query and the terms fold through
// tmSearchNormalize. A candidate is an entry (its label and synonyms in the
// active language and in English), a pin no entry covers (its label and
// title) or a recent page neither covers (its title). The index keeps the
// candidates in their final order, so a query only sorts its matches into
// the two classes:
//
// - prefix: every query token starts a word of one term, in any order;
// - contains: every token of two or more characters occurs in one term.
//
// A candidate's best term decides its class. An Arabic word of four or more
// letters that starts with "ال" also counts without it, so "عملاء" starts
// "العملاء". The rows a query shows are capped at 50, the first three
// prefix-class entities that may be created each followed by a create row.

/** The query tokens a match uses at most. */
const MAX_TOKENS = 5;

/** The rows a query shows at most. */
export const TM_LAUNCHER_ROW_LIMIT = 50;

/** The create rows a query shows at most. */
export const TM_LAUNCHER_CREATE_LIMIT = 3;

/** The Arabic definite article a word variant drops. */
const ARTICLE = 'ال';

/** An Arabic letter, after normalization. */
const ARABIC = /^[ء-ي]+$/;

/**
 * What the index joins every term with to fold them all at once: a control
 * character that folding keeps, being no mark, space, punctuation or symbol.
 */
const JOINER = '\u0001';

/** Where a word of displayed text ends: the separators normalization turns into spaces. */
const SEPARATORS = /([\s\p{P}\p{S}]+)/u;

/** One thing the launcher's page search can find. */
export interface TmLauncherCandidate {
  /** What it is: an entry, a pin that no entry covers, or a recent page that neither covers. */
  readonly kind: 'entry' | 'pin' | 'recent';
  /** Its key, unique among the candidates. */
  readonly key: string;
  /** The URL it opens, in the routed tenant. */
  readonly href: string;
  /** The label it shows, in the active language. */
  readonly label: string;
  /** Its icon's registered name. */
  readonly icon: string;
  /** The muted line beside its label: an entry's section, "Pinned" or "Recently visited"; `null` for none. */
  readonly description: string | null;
  /** The texts it is found by, as displayed: the label first. */
  readonly terms: readonly string[];
  /** An entity's create page and that row's label, when the user may create one; else `null`. */
  readonly create: { readonly href: string; readonly label: string } | null;
}

/** A candidate's term, folded and split into the words its prefix match reads. */
interface TmIndexedTerm {
  /** The folded term. */
  readonly text: string;
  /** Its words, each word's "ال" variant beside it. */
  readonly words: readonly string[];
}

/** A candidate in the index. */
interface TmIndexed {
  /** The candidate. */
  readonly candidate: TmLauncherCandidate;
  /** Its distinct folded terms. */
  readonly terms: readonly TmIndexedTerm[];
}

/** The launcher's page index: its candidates in their final order, with their folded terms. */
export interface TmLauncherIndex {
  /** The indexed candidates, in order. */
  readonly entries: readonly TmIndexed[];
}

/** A candidate a query matched, and how. */
export interface TmLauncherMatch {
  /** The candidate. */
  readonly candidate: TmLauncherCandidate;
  /** Its best term's class. */
  readonly match: 'prefix' | 'contains';
}

/** One row of the typed state: a match, or the create row after an entity's match. */
export interface TmLauncherRow {
  /** A matched candidate, or a create row. */
  readonly kind: 'match' | 'create';
  /** The candidate, which a create row follows. */
  readonly candidate: TmLauncherCandidate;
  /** The match's class; a create row follows only a prefix match. */
  readonly match: 'prefix' | 'contains';
}

/** A run of displayed text, marked when a query token starts it. */
export interface TmLauncherSegment {
  /** The text. */
  readonly text: string;
  /** Whether it sits in a `<mark>`. */
  readonly mark: boolean;
}

/** Whether a folded word is an Arabic word of four or more letters that starts with "ال", which also counts without it. */
function hasArticle(word: string): boolean {
  return word.length >= 4 && word.startsWith(ARTICLE) && ARABIC.test(word);
}

/** A folded word's variants: itself, and without "ال" when {@link hasArticle} holds. */
function variants(word: string): readonly string[] {
  return hasArticle(word) ? [word, word.slice(ARTICLE.length)] : [word];
}

/** What the candidates are gathered from on the first open: the entries, the pins, the recent pages and the texts around them. */
export interface TmLauncherSources {
  /** The active language's code. */
  readonly language: string;
  /** The visible entries, in the navigation registry's order. */
  readonly entries: readonly TmNavItem[];
  /** Each section's label, by its id. */
  readonly sections: ReadonlyMap<string, string>;
  /** The routed tenant's pins the user may open, in the bar's order. */
  readonly pins: readonly {
    /** The pin's id. */
    readonly id: string;
    /** The URL it opens. */
    readonly href: string;
    /** The label it shows. */
    readonly label: string;
    /** The page's title when it was pinned. */
    readonly title: string;
  }[];
  /** The recent pages, newest first. */
  readonly recents: readonly {
    /** The URL. */
    readonly url: string;
    /** The page's title. */
    readonly title: string;
    /** The page's icon. */
    readonly icon: string;
  }[];
  /** The description of a pin: "Pinned". */
  readonly pinned: string;
  /** The description of a recent page: "Recently visited". */
  readonly recent: string;
  /**
   * An entry's label and synonyms in a language.
   *
   * @param entry - The entry.
   * @param language - The language.
   * @returns The terms.
   */
  terms(entry: TmNavItem, language: string): { readonly label: string; readonly synonyms: readonly string[] };
  /**
   * The label of an entity's create row, such as "Center · Create".
   *
   * @param entity - The entity's name.
   * @returns The label.
   */
  createLabel(entity: string): string;
  /**
   * The entry a URL opens, which then covers it.
   *
   * @param href - The URL.
   * @returns The entry's icon, or `undefined` when no entry opens it.
   */
  entry(href: string): { readonly icon: string } | undefined;
}

/**
 * Gathers the launcher's candidates in their final order: the entries, with
 * their terms in the active language and in English, then the pins no entry
 * covers, then the recent pages neither covers, each of those two by label.
 *
 * @param sources - The entries, the pins, the recent pages and their texts.
 * @returns The candidates.
 */
export function tmLauncherCandidates(sources: TmLauncherSources): TmLauncherCandidate[] {
  const { language } = sources;
  const collator = new Intl.Collator(language);
  const byLabel = (a: TmLauncherCandidate, b: TmLauncherCandidate): number => collator.compare(a.label, b.label);
  const candidates: TmLauncherCandidate[] = [];
  // --- The entries, in the registry's order ---
  for (const entry of sources.entries) {
    const own = sources.terms(entry, language);
    const terms = [own.label, ...own.synonyms];
    if (language !== 'en') {
      const english = sources.terms(entry, 'en');
      terms.push(english.label, ...english.synonyms);
    }
    candidates.push({
      kind: 'entry',
      key: `entry:${entry.id}`,
      href: entry.href,
      label: entry.label,
      icon: entry.icon,
      description: entry.section === undefined ? null : (sources.sections.get(entry.section) ?? null),
      terms,
      create: entry.createHref !== null && entry.entity !== undefined ? { href: entry.createHref, label: sources.createLabel(entry.entity) } : null,
    });
  }
  // --- The pins no entry covers, then the recent pages neither covers ---
  const pins: TmLauncherCandidate[] = [];
  const pinHrefs = new Set<string>();
  for (const { id, href, label, title } of sources.pins) {
    pinHrefs.add(href);
    if (sources.entry(href) === undefined) {
      const icon = sources.entry(href.split(/[?#]/, 1)[0]!)?.icon ?? 'pin';
      pins.push({ kind: 'pin', key: `pin:${id}`, href, label, icon, description: sources.pinned, terms: [label, title], create: null });
    }
  }
  const recents: TmLauncherCandidate[] = [];
  for (const { url, title, icon } of sources.recents) {
    if (sources.entry(url) === undefined && !pinHrefs.has(url)) {
      recents.push({ kind: 'recent', key: `recent:${url}`, href: url, label: title, icon, description: sources.recent, terms: [title], create: null });
    }
  }
  candidates.push(...pins.sort(byLabel), ...recents.sort(byLabel));
  return candidates;
}

/**
 * Builds the launcher's page index. The candidates come in their final
 * order: the entries in the navigation registry's order, then the pins, then
 * the recent pages, each of those two by label.
 *
 * @param candidates - The candidates, in order.
 * @returns The index.
 */
export function tmLauncherIndex(candidates: readonly TmLauncherCandidate[]): TmLauncherIndex {
  // Plain loops: the index of a large distribution's launcher builds on the
  // first open, inside the press's frame.
  // --- Every term folded in one pass ---
  // A fold's fixed steps cost more than its characters, so the terms fold
  // joined. Folding acts on each character alone and keeps the joiner, so
  // each part is its term's fold but for the spaces at its ends. A term
  // that holds the joiner itself folds alone.
  const raw = candidates.flatMap((candidate) => candidate.terms);
  let folded = tmSearchNormalize(raw.join(JOINER)).split(JOINER);
  if (folded.length !== raw.length) {
    folded = raw.map((term) => tmSearchNormalize(term));
  }
  // --- Each candidate's distinct terms, split into words ---
  const entries: TmIndexed[] = [];
  let at = 0;
  for (const candidate of candidates) {
    const terms: TmIndexedTerm[] = [];
    for (let i = 0; i < candidate.terms.length; i++) {
      const text = folded[at++]!.trim();
      // A candidate has a handful of terms, so a scan beats a set.
      if (text === '' || terms.some((indexed) => indexed.text === text)) {
        continue;
      }
      const words: string[] = [];
      for (const word of text.split(' ')) {
        words.push(word);
        if (hasArticle(word)) {
          words.push(word.slice(ARTICLE.length));
        }
      }
      terms.push({ text, words });
    }
    entries.push({ candidate, terms });
  }
  return { entries };
}

/**
 * A query's tokens: the folded query's words, at most five.
 *
 * @param query - The query as typed.
 * @returns The tokens; none for a query with no letter or digit.
 */
export function tmLauncherTokens(query: string): readonly string[] {
  const text = tmSearchNormalize(query);
  return text === '' ? [] : text.split(' ').slice(0, MAX_TOKENS);
}

/**
 * The candidates a query matches, prefix matches first, each class in the
 * index's order.
 *
 * @param index - The index.
 * @param tokens - The query's tokens, from {@link tmLauncherTokens}.
 * @returns The matches.
 */
export function tmLauncherMatch(index: TmLauncherIndex, tokens: readonly string[]): readonly TmLauncherMatch[] {
  if (tokens.length === 0) {
    return [];
  }
  // A one-character token takes part in a prefix match only: on its own, it
  // would make every term that holds the letter a contains match.
  const long = tokens.filter((token) => token.length >= 2);
  const prefix: TmLauncherMatch[] = [];
  const contains: TmLauncherMatch[] = [];
  for (const { candidate, terms } of index.entries) {
    if (terms.some((term) => tokens.every((token) => term.words.some((word) => word.startsWith(token))))) {
      prefix.push({ candidate, match: 'prefix' });
    } else if (long.length > 0 && terms.some((term) => long.every((token) => term.text.includes(token)))) {
      contains.push({ candidate, match: 'contains' });
    }
  }
  return [...prefix, ...contains];
}

/**
 * The typed state's rows: the matches, a prefix-matched entity that may be
 * created followed by its create row (three at most), fifty rows at most.
 *
 * @param matches - The matches, in order.
 * @returns The rows.
 */
export function tmLauncherRows(matches: readonly TmLauncherMatch[]): readonly TmLauncherRow[] {
  const rows: TmLauncherRow[] = [];
  let creates = 0;
  for (const { candidate, match } of matches) {
    if (rows.length === TM_LAUNCHER_ROW_LIMIT) {
      break;
    }
    rows.push({ kind: 'match', candidate, match });
    if (match === 'prefix' && candidate.create !== null && creates < TM_LAUNCHER_CREATE_LIMIT && rows.length < TM_LAUNCHER_ROW_LIMIT) {
      creates++;
      rows.push({ kind: 'create', candidate, match });
    }
  }
  return rows;
}

/**
 * A label split into runs, the part of each word that a token starts marked:
 * the word's own start, or its start after "ال" for a token that starts the
 * word's variant. The runs join back into the label.
 *
 * @param label - The label as displayed.
 * @param tokens - The query's tokens.
 * @returns The runs; one unmarked run when no token starts a word.
 */
export function tmLauncherMarks(label: string, tokens: readonly string[]): readonly TmLauncherSegment[] {
  const segments: TmLauncherSegment[] = [];
  const push = (text: string, mark: boolean): void => {
    if (text === '') {
      return;
    }
    const last = segments[segments.length - 1];
    if (last !== undefined && last.mark === mark) {
      segments[segments.length - 1] = { text: last.text + text, mark };
    } else {
      segments.push({ text, mark });
    }
  };
  // The split keeps the separators, at the odd indexes.
  label.split(SEPARATORS).forEach((part, i) => {
    const word = i % 2 === 0 ? tmSearchNormalize(part) : '';
    // The longest token that starts the word, or its variant, wins.
    let from = -1;
    let length = 0;
    for (const token of tokens) {
      for (const [at, variant] of variants(word).map((text, v) => [v === 0 ? 0 : ARTICLE.length, text] as const)) {
        if (variant.startsWith(token) && token.length > length) {
          from = at;
          length = token.length;
        }
      }
    }
    if (from === -1) {
      push(part, false);
      return;
    }
    // Maps the folded span back to the displayed characters: the shortest
    // displayed prefixes whose folds reach the span's start and end.
    const reach = (folded: number): number => {
      let end = 0;
      while (end < part.length && tmSearchNormalize(part.slice(0, end)).length < folded) {
        end++;
      }
      return end;
    };
    const start = from === 0 ? 0 : reach(from);
    const end = reach(from + length);
    push(part.slice(0, start), false);
    push(part.slice(start, end), true);
    push(part.slice(end), false);
  });
  return segments;
}
