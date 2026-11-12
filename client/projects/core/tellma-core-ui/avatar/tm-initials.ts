// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/** A grapheme that can stand for a word: one holding a letter or a digit. */
const WORD_LEAD = /[\p{L}\p{N}]/u;

/** One grapheme segmenter per locale, created on first use. */
const segmenters = new Map<string, Intl.Segmenter>();

/**
 * The locale to hand `Intl` and the case mappings: the given tag when it
 * is well formed, else none (the runtime default), so a malformed tag
 * degrades the case rules rather than throwing in a template.
 */
function safeLocale(locale: string): string | undefined {
  try {
    return Intl.getCanonicalLocales(locale)[0];
  } catch {
    return undefined;
  }
}

/** The first grapheme of `word` that holds a letter or a digit, else ''. */
function leadGrapheme(word: string, locale: string | undefined): string {
  const key = locale ?? '';
  let segmenter = segmenters.get(key);
  if (segmenter === undefined) {
    segmenter = new Intl.Segmenter(locale, { granularity: 'grapheme' });
    segmenters.set(key, segmenter);
  }
  for (const { segment } of segmenter.segment(word)) {
    if (WORD_LEAD.test(segment)) {
      return segment;
    }
  }
  return '';
}

/**
 * Whether a grapheme is a letter of an uncased script (Arabic, Ethiopic,
 * CJK, …), whose names give one initial. Digits belong to no script.
 */
function isUncasedLetter(grapheme: string, locale: string | undefined): boolean {
  return (
    /\p{L}/u.test(grapheme) &&
    grapheme.toLocaleUpperCase(locale) === grapheme.toLocaleLowerCase(locale)
  );
}

/**
 * The initials an avatar shows for a name, split into user-perceived
 * characters (graphemes), so a letter with combining marks or a
 * multi-unit character is never cut in half.
 *
 * - A name in a cased script gives the first grapheme of its first and
 *   last words, upper-cased in `locale`: "Farah Nasser" gives "FN", "ilker
 *   aydın" gives "İA" in Turkish.
 * - A name in an uncased script (Arabic, Ethiopic, CJK) gives one
 *   grapheme: "فرح ناصر" gives "ف", "王小明" gives "王". So does a
 *   single word, and a name whose last word is in an uncased script.
 * - Digits count as letters of no script: "3M Group" gives "3G".
 * - Words with no letter or digit (punctuation, emoji) are skipped, and
 *   so are leading marks within a word: "(Farah) Nasser" gives "FN".
 * - A name with no letter or digit gives `''`; `tm-avatar` then shows its
 *   person glyph.
 *
 * @param name The person's display name.
 * @param locale The formatting locale (BCP 47), for the case mapping.
 */
export function tmInitials(name: string, locale: string): string {
  const tag = safeLocale(locale);
  const words = name.split(/\s+/u).filter((word) => WORD_LEAD.test(word));
  if (words.length === 0) {
    return '';
  }
  // One initial when either end of the name is in an uncased script, so
  // a mixed-script name never pairs letters from two scripts.
  const first = leadGrapheme(words[0], tag);
  if (isUncasedLetter(first, tag)) {
    return first;
  }
  const last = words.length > 1 ? leadGrapheme(words[words.length - 1], tag) : '';
  return (isUncasedLetter(last, tag) ? first : first + last).toLocaleUpperCase(tag);
}

/**
 * The avatar tone a stable key picks: one of the eight `categorical`
 * slots. The hash is 32-bit FNV-1a over the key's UTF-16 code units, and
 * the tone is the hash modulo 8, plus 1. It never changes between
 * releases: `'core.User:17'` is 4 and `'core.User:18'` is 3.
 *
 * Pass a record key such as `{entity}:{id}`, never an email address or a
 * sign-in subject.
 *
 * @param key An opaque, stable key.
 */
export function tmAvatarTone(key: string): 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 {
  // FNV-1a: start at the offset basis, then XOR each unit in and multiply
  // by the FNV prime, modulo 2^32 (Math.imul keeps the product in 32 bits).
  let hash = 2166136261;
  for (let i = 0; i < key.length; i++) {
    hash = Math.imul(hash ^ key.charCodeAt(i), 16777619) >>> 0;
  }
  return ((hash % 8) + 1) as 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8;
}
