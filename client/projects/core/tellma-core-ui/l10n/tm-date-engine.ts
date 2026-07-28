// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The date engine: formats and parses ISO 8601 date strings (the value
// currency) per locale × display calendar. Month and era names come from
// Intl/CLDR at runtime — no name tables ship in any package, and every
// calendar renders localized in every UI language.

import { TM_PARSE_ERROR, type TmParseError } from '@tellma/core-ui/contracts';

import { tmGregorianCalendar, ɵtmParseIsoDate, type TmCalendar } from './tm-calendar';

/**
 * The display style of a formatted date, mapped to a fixed CLDR skeleton —
 * `numeric`: yMd, `medium`: yMMMd, `long`: yMMMMd. The skeletons are fixed
 * in code (not configurable) so a server-side implementation reproduces
 * the same output from the same parameters.
 */
export type TmDateStyle = 'numeric' | 'medium' | 'long';

/** Options of {@link tmFormatDate}. */
export interface TmDateFormatOptions {
  /** The display calendar. Default Gregorian. */
  readonly calendar?: TmCalendar;
  /** The display style. Default `numeric`. */
  readonly dateStyle?: TmDateStyle;
}

/** Options of {@link tmParseDate}. */
export interface TmDateParseOptions {
  /** The display calendar segments are interpreted in. Default Gregorian. */
  readonly calendar?: TmCalendar;
  /**
   * The reference date (ISO) omitted trailing segments complete from and
   * two-digit years pivot around. Default: the calendar's `today()`.
   */
  readonly today?: string;
}

/** The supported ISO window: the intersection of ISO 8601 with SQL/BCL date types. */
const MIN_ISO = '0001-01-01';
const MAX_ISO = '9999-12-31';

/** The fixed skeleton per style, as Intl option bags. */
const STYLE_OPTIONS: Record<TmDateStyle, Intl.DateTimeFormatOptions> = {
  numeric: { year: 'numeric', month: 'numeric', day: 'numeric' },
  medium: { year: 'numeric', month: 'short', day: 'numeric' },
  long: { year: 'numeric', month: 'long', day: 'numeric' },
};

const formatterCache = new Map<string, Intl.DateTimeFormat>();

function formatterFor(locale: string, calendarId: string, style: TmDateStyle): Intl.DateTimeFormat {
  const key = `${locale}|${calendarId}|${style}`;
  let formatter = formatterCache.get(key);
  if (formatter === undefined) {
    formatter = new Intl.DateTimeFormat(locale, {
      ...STYLE_OPTIONS[style],
      calendar: calendarId,
      timeZone: 'UTC',
    });
    formatterCache.set(key, formatter);
  }
  return formatter;
}

/** A UTC Date at noon of the ISO date — immune to zone/DST edge shifts. */
function utcDateOf(year: number, month: number, day: number): Date {
  const date = new Date(Date.UTC(2000, month - 1, day, 12));
  date.setUTCFullYear(year); // Date.UTC maps years 0–99 into 1900–1999
  return date;
}

/**
 * Formats an ISO `YYYY-MM-DD` date for display in the given locale and
 * display calendar ('' for null/undefined/''; a malformed input is
 * returned as-is, mirroring the number codec's posture for non-values).
 */
export function tmFormatDate(
  iso: string | null | undefined,
  locale: string,
  options?: TmDateFormatOptions,
): string {
  if (iso === null || iso === undefined || iso === '') {
    return '';
  }
  const parts = ɵtmParseIsoDate(iso);
  if (parts === null) {
    return iso;
  }
  const calendar = options?.calendar ?? tmGregorianCalendar();
  return formatterFor(locale, calendar.id, options?.dateStyle ?? 'numeric').format(
    utcDateOf(parts.year, parts.month, parts.day),
  );
}

// ---- Parsing ----

/** Unicode format / bidi-control marks (the `Cf` category) — always invisible. */
const FORMAT_CONTROL = /\p{Cf}/gu;

/**
 * Segment separators: slash, dash, comma, Arabic date/decimal marks,
 * spaces, parentheses, and the CJK date-unit suffixes (`2026年7月27日`,
 * `2026년 7월 27일` tokenize to their numbers). Parentheses are separators
 * because several locales bracket a grammatical suffix or an era onto a
 * field — Basque `2026(e)ko … 20(a)`, Uzbek `1448 (hijriy)` — and the
 * bracketed word is then shed like any other particle. Dots are NOT
 * global separators — they split only between digits (`27.03.2026`),
 * because they also live inside month abbreviations (`Rab. I`, `juil.`)
 * and dotted era names (`ฮ.ศ.`).
 */
const SEPARATORS = /[/\-,،٫؍()（）\s年月日号號년월일]+/u;

/**
 * A dot acting as a date separator: one whose neighbours are not BOTH
 * letters. Abbreviation dots always sit inside or after a letter run
 * (`juil.`, `Rab. I`, `ฮ.ศ.`), so a dot with a digit on either side is a
 * separator — `27.03.2026`, and the German ordinal habit `5.März 2026`.
 */
const SEPARATING_DOT = /(?<=\d)\.(?=\s*[\p{L}\d])|(?<=\p{L})\.(?=\s*\d)/gu;

const digitZeroCache = new Map<string, number>();

/** The zero code point of the locale's default numbering system. */
function digitZeroFor(locale: string): number {
  let zero = digitZeroCache.get(locale);
  if (zero === undefined) {
    zero = new Intl.NumberFormat(locale).format(0).codePointAt(0) ?? 48;
    digitZeroCache.set(locale, zero);
  }
  return zero;
}

/** Maps the locale's numbering-system digits to ASCII; other chars pass through. */
function normalizeDigits(text: string, locale: string): string {
  const zero = digitZeroFor(locale);
  if (zero === 48) {
    return text;
  }
  let out = '';
  for (const ch of text) {
    const code = ch.codePointAt(0)!;
    out += code >= zero && code <= zero + 9 ? String.fromCharCode(48 + (code - zero)) : ch;
  }
  return out;
}

/**
 * The locale's field order (subset of year/month/day), cached per
 * locale × calendar × style. All three patterns can disagree (fa formats
 * numeric dates year-first but medium dates day-first; tt formats medium
 * dates year-first but long dates day-first), so all-numeric input reads
 * in the numeric order while named-month input reads in the order of the
 * style whose month spelling it matched.
 */
const fieldOrderCache = new Map<string, readonly ('year' | 'month' | 'day')[]>();

function fieldOrderFor(
  locale: string,
  calendarId: string,
  style: TmDateStyle,
): readonly ('year' | 'month' | 'day')[] {
  const key = `${locale}|${calendarId}|${style}`;
  let order = fieldOrderCache.get(key);
  if (order === undefined) {
    order = ['year', 'month', 'day'];
    try {
      const parts = formatterFor(locale, calendarId, style).formatToParts(utcDateOf(2001, 2, 3));
      const fields = parts
        .map((part) => part.type)
        .filter((type): type is 'year' | 'month' | 'day' =>
          type === 'year' || type === 'month' || type === 'day',
        );
      if (fields.length === 3) {
        order = fields;
      }
    } catch {
      // A locale × calendar combination the engine's ICU cannot pair
      // (some builds throw rather than fall back). ISO order is the
      // safest default; the caller still validates the result.
    }
    fieldOrderCache.set(key, order);
  }
  return order;
}

const isoCollisionCache = new Map<string, boolean>();

/**
 * Whether this locale × calendar renders its own NUMERIC dates in the ISO
 * shape (`1234-56-78`) but NOT in year-month-day order — the one case in
 * which an ISO-looking string is genuinely ambiguous. Kyrgyz is the only
 * such locale in CLDR today (`yyyy-dd-MM`), and there its own rendering
 * must win: otherwise every date the user sees round-trips transposed.
 * Everywhere else the fast path stays safe, because a locale that orders
 * fields differently also separates them differently (`09/23/2024`).
 */
function isoShapeIsAmbiguous(locale: string, calendarId: string): boolean {
  const key = `${locale}|${calendarId}`;
  let ambiguous = isoCollisionCache.get(key);
  if (ambiguous === undefined) {
    const order = fieldOrderFor(locale, calendarId, 'numeric');
    ambiguous =
      (order[0] !== 'year' || order[1] !== 'month') &&
      ISO_SHAPE.test(formatterFor(locale, calendarId, 'numeric').format(utcDateOf(2001, 2, 3)));
    isoCollisionCache.set(key, ambiguous);
  }
  return ambiguous;
}

/** The shape the ISO fast path claims: a 4-digit year, then 2 and 2. */
const ISO_SHAPE = /^\d{4}-\d{2}-\d{2}$/;

/**
 * The joiners both sides shed before comparing names: dots, spaces,
 * hyphens (separator splitting may cut a hyphenated month name like
 * `רביע אל-אוול` into pieces that re-join with spaces, so both sides
 * must compare joint-free), tatweel, and invisible format controls (some
 * era names embed a ZWJ/ZWNJ, and the input side strips Cf globally).
 */
const NAME_JOINERS = /[\p{Cf}ـ.\-־\s]/gu;

/**
 * Exact name folding: case-insensitive and joiner-free, but MARKS ARE
 * KEPT — Indic and Thai vowel signs are marks that carry the whole
 * distinction between month names (Bengali জুন vs জানু, Thai มี.ค. vs
 * ม.ค.), so mark-stripping must never be the primary match tier.
 */
function foldExact(name: string): string {
  return name.normalize('NFC').replace(NAME_JOINERS, '').toLowerCase();
}

/**
 * The Turkish dotless ı — the one letter neither of the other two folds
 * can bridge. A locale-insensitive `toLowerCase()` maps ASCII `I` to `i`
 * (so typing `MAYIS` yields `mayis`) while the table's own key holds `ı`
 * (`Mayıs`), and `ı` has no NFD decomposition for the mark-stripping fold
 * to strip. It is folded in the LOOSE tier only — the exact tier must
 * stay exact.
 */
const DOTLESS_I = /ı/gu;

/**
 * Diacritic-insensitive folding — the FALLBACK tier, so a Latin name
 * typed without its accents (`marz 2026`) still matches. Keys that
 * become AMBIGUOUS under this fold are dropped from the fallback table
 * rather than resolved by insertion order.
 */
function foldName(name: string): string {
  return name
    .normalize('NFD')
    .replace(/\p{M}/gu, '')
    .replace(DOTLESS_I, 'i')
    .replace(NAME_JOINERS, '')
    .toLowerCase();
}

/** Folded era names per locale × calendar — tolerated (skipped) in parsed input. */
const eraNameCache = new Map<string, ReadonlySet<string>>();

function eraNamesFor(locale: string, calendarId: string): ReadonlySet<string> {
  const key = `${locale}|${calendarId}`;
  let names = eraNameCache.get(key);
  if (names === undefined) {
    const set = new Set<string>();
    const reference = utcDateOf(2001, 2, 3);
    for (const form of ['long', 'short', 'narrow'] as const) {
      const parts = new Intl.DateTimeFormat(locale, {
        year: 'numeric',
        era: form,
        calendar: calendarId,
        timeZone: 'UTC',
      }).formatToParts(reference);
      for (const part of parts) {
        if (part.type === 'era') {
          const folded = foldName(part.value);
          if (folded !== '') {
            set.add(folded);
          }
        }
      }
    }
    names = set;
    eraNameCache.set(key, names);
  }
  return names;
}

/**
 * Folded literal WORDS of the locale's date patterns (`de`, `г`, `de`-style
 * connective particles) per locale × calendar — carried by formatted
 * output, informationless for the segment slots, so tolerated (skipped).
 * Words that collide with a month name are NOT included.
 */
const literalWordCache = new Map<string, ReadonlySet<string>>();

function literalWordsFor(
  locale: string,
  calendar: TmCalendar,
  referenceYear: number,
): ReadonlySet<string> {
  const key = `${locale}|${calendar.id}`;
  let words = literalWordCache.get(key);
  if (words === undefined) {
    const set = new Set<string>();
    const months = monthNamesFor(locale, calendar, referenceYear);
    const reference = utcDateOf(2001, 2, 3);
    for (const style of ['numeric', 'medium', 'long'] as const) {
      for (const part of formatterFor(locale, calendar.id, style).formatToParts(reference)) {
        if (part.type !== 'literal') {
          continue;
        }
        for (const match of part.value.matchAll(/\p{L}+/gu)) {
          const folded = foldName(match[0]);
          if (folded !== '' && !months.hasFolded(folded)) {
            set.add(folded);
          }
        }
      }
    }
    words = set;
    literalWordCache.set(key, words);
  }
  return words;
}

/**
 * The month-name lookup for one locale × calendar: an EXACT tier
 * (mark-preserving) consulted first, then a diacritic-insensitive
 * fallback whose ambiguous keys have been removed.
 */
interface MonthTable {
  /** The month a segment names, or `undefined`. */
  lookup(segment: string): number | undefined;
  /**
   * The style whose IN-DATE spelling a segment matches, or `undefined`
   * when the segment does not pin one down (both styles spell the month
   * alike, or the spelling is a standalone form). The caller reads the
   * field order from it: a locale's medium and long patterns can order
   * fields differently (tt renders medium dates year-first and long dates
   * day-first), so the order must come from the style that actually
   * matched rather than from a fixed guess.
   */
  styleOf(segment: string): TmDateStyle | undefined;
  /** Whether a diacritic-folded word is a month name in either tier. */
  hasFolded(folded: string): boolean;
}

const monthNameCache = new Map<string, MonthTable>();

function monthNamesFor(
  locale: string,
  calendar: TmCalendar,
  referenceYear: number,
): MonthTable {
  const key = `${locale}|${calendar.id}`;
  let table = monthNameCache.get(key);
  if (table === undefined) {
    const exact = new Map<string, number>();
    const loose = new Map<string, number>();
    /** Loose keys that named more than one month — never guessed at. */
    const ambiguous = new Set<string>();
    /**
     * Per key: the single in-date style that spells the month that way,
     * or `null` once a second style claims the same key (the spelling
     * then pins no order down). Standalone forms never claim a key —
     * they belong to no pattern, so they must not blank an attribution.
     */
    const styleOfKey = new Map<string, TmDateStyle | null>();
    const noteStyle = (key: string, style: TmDateStyle): void => {
      const claimed = styleOfKey.get(key);
      styleOfKey.set(key, claimed === undefined || claimed === style ? style : null);
    };
    const standalone = (['long', 'short'] as const).map(
      (form) =>
        new Intl.DateTimeFormat(locale, { month: form, calendar: calendar.id, timeZone: 'UTC' }),
    );
    const months = calendar.monthsInYear(referenceYear);
    for (let month = 1; month <= months; month++) {
      const iso = calendar.fromParts({ year: referenceYear, month, day: 1 });
      if (iso === null) {
        continue;
      }
      const parts = ɵtmParseIsoDate(iso)!;
      const date = utcDateOf(parts.year, parts.month, parts.day);
      const candidates: { readonly name: string; readonly style: TmDateStyle | null }[] = [];
      for (const formatter of standalone) {
        candidates.push({ name: formatter.format(date), style: null });
      }
      // The IN-DATE forms too: several languages inflect the month inside
      // a date (ru genitive) or abbreviate it differently there (vi
      // 'Tháng 11' standalone vs 'thg 11' in a date) — formatted output
      // must round-trip, so the format-context spelling joins the table.
      for (const style of ['medium', 'long'] as const) {
        for (const part of formatterFor(locale, calendar.id, style).formatToParts(date)) {
          if (part.type === 'month') {
            candidates.push({ name: part.value, style });
          }
        }
      }
      for (const candidate of candidates) {
        // Digit-normalized exactly as the input side is: a few month
        // names CARRY a digit (ps 'جماد ۲'), and the parser has already
        // mapped the locale's digits to ASCII by the time it looks up.
        const name = normalizeDigits(candidate.name, locale);
        const exactKey = foldExact(name);
        if (exactKey !== '' && !exact.has(exactKey)) {
          exact.set(exactKey, month);
        }
        if (exactKey !== '' && candidate.style !== null) {
          noteStyle(exactKey, candidate.style);
        }
        const looseKey = foldName(name);
        if (looseKey !== '' && candidate.style !== null) {
          noteStyle(looseKey, candidate.style);
        }
        if (looseKey === '' || ambiguous.has(looseKey)) {
          continue;
        }
        const claimed = loose.get(looseKey);
        if (claimed === undefined) {
          loose.set(looseKey, month);
        } else if (claimed !== month) {
          // Mark-stripping collapsed two DIFFERENT months onto one key
          // (Bengali জুন/জানু, Thai มี.ค./ม.ค.) — drop it rather than let
          // insertion order silently pick a month.
          loose.delete(looseKey);
          ambiguous.add(looseKey);
        }
      }
    }
    table = {
      lookup: (segment) => exact.get(foldExact(segment)) ?? loose.get(foldName(segment)),
      styleOf: (segment) => {
        const exactKey = foldExact(segment);
        return styleOfKey.get(exact.has(exactKey) ? exactKey : foldName(segment)) ?? undefined;
      },
      hasFolded: (folded) => loose.has(folded) || ambiguous.has(folded) || exact.has(folded),
    };
    monthNameCache.set(key, table);
  }
  return table;
}

/**
 * Parses free-typed date text into an ISO `YYYY-MM-DD` string. The
 * algorithm is deterministic — forgiving on separators and completion,
 * strict on ambiguity:
 *
 * 1. An input already in the ISO shape is accepted in any locale — with
 *    one exception: where the locale's OWN numeric format is that same
 *    shape in a different field order (Kyrgyz writes `yyyy-dd-MM`), the
 *    locale's reading wins, because otherwise the text this function
 *    produces would not survive being read back.
 * 2. Numeric segments read in the locale's field order for the display
 *    calendar (`5/3` is day-month in en-GB and ar-SA, month-day in en-US).
 * 3. Segments are interpreted IN the display calendar, then converted to
 *    ISO (under an Umm al-Qura calendar, `15/2/1448` is 15 Safar 1448 AH).
 * 4. A month name (long or short, case/diacritic-insensitive) may replace
 *    the numeric month.
 * 5. Omitted trailing segments complete from today: one segment is that
 *    day of the current month; two are day and month — in the locale's
 *    field order — of the current year.
 * 6. Two-digit years resolve in the sliding window [today − 80, today + 19]
 *    years, in the display calendar's numbering.
 * 7. Anything else — a segment out of range for its slot, a name matching
 *    no month, extra segments — is a parse error. Segments are never
 *    reordered to force a match: predictability over cleverness.
 *
 * Empty text parses to `null`.
 */
export function tmParseDate(
  text: string,
  locale: string,
  options?: TmDateParseOptions,
): string | null | TmParseError {
  const calendar = options?.calendar ?? tmGregorianCalendar();
  const cleaned = normalizeDigits(text.replace(FORMAT_CONTROL, ''), locale).trim();
  if (cleaned === '') {
    return null;
  }

  // (1) The ISO fast path — unambiguous by construction in every locale
  // but one (see `isoShapeIsAmbiguous`), bounds included.
  // Shape first: the ambiguity probe costs an Intl format, and only an
  // ISO-shaped input can be ambiguous in the first place.
  const shaped = ɵtmParseIsoDate(cleaned);
  const isoParts = shaped !== null && isoShapeIsAmbiguous(locale, calendar.id) ? null : shaped;
  if (isoParts !== null) {
    const iso = tmGregorianCalendar().fromParts({
      year: isoParts.year,
      month: isoParts.month,
      day: isoParts.day,
    });
    return iso !== null && iso >= MIN_ISO && iso <= MAX_ISO ? iso : TM_PARSE_ERROR;
  }

  const today = calendar.toParts(
    options?.today !== undefined && ɵtmParseIsoDate(options.today) !== null
      ? options.today
      : calendar.today(),
  );

  // Era literals ("AH", "ዓ/ም") and pattern particles ("de", "г") ride
  // along in some locales' formatted output; they carry no information a
  // segment slot needs, so they are tolerated — formatted output must
  // round-trip through the parser. Some engines glue the era to the year
  // ("AM2018"), so a mixed token sheds a leading/trailing era name. What
  // remains of a multi-word month name ("Rab. I") is re-joined.
  const eraNames = eraNamesFor(locale, calendar.id);
  const literalWords = literalWordsFor(locale, calendar, today.year);
  const monthTable = monthNamesFor(locale, calendar, today.year);
  const isNumber = (token: string): boolean => /^\d{1,4}$/.test(token);
  const rawTokens = cleaned
    .replace(SEPARATING_DOT, '/')
    .split(SEPARATORS)
    .map((token) => token.replace(/^\.+|\.+$/g, ''))
    .filter((token) => token !== '');

  // (a) Re-join multi-word month names FIRST — before any era/literal
  // filtering, because a name's own words can collide with them (the
  // French Umm al-Qura short month is 'dhou. h.' while the narrow era is
  // 'H'). Adjacent words always merge; a trailing NUMBER merges only when
  // the joint is itself a month name (Vietnamese 'thg 7').
  const isParticle = (token: string): boolean => {
    const folded = foldName(token);
    return eraNames.has(folded) || literalWords.has(folded);
  };
  const merged: string[] = [];
  for (const token of rawTokens) {
    const previous = merged.length === 0 ? null : merged[merged.length - 1];
    const joint = previous === null ? '' : `${previous} ${token}`;
    const jointNamesMonth = previous !== null && monthTable.lookup(joint) !== undefined;
    // Words merge freely, EXCEPT across a standalone particle (Spanish
    // '3 de enero de 2026' must not glue its `de`s onto the month) — and
    // a particle or a number joins only when the joint is itself a name.
    const mergeable =
      previous !== null &&
      !isNumber(previous) &&
      (jointNamesMonth ||
        (!isNumber(token) && !isParticle(token) && !isParticle(previous)));
    if (mergeable) {
      merged[merged.length - 1] = joint;
    } else {
      merged.push(token);
    }
  }

  // (b) Era literals ("AH", "ዓ/ም") and pattern particles ("de", "г") ride
  // along in formatted output; they carry no information a segment slot
  // needs, so they are tolerated — formatted output must round-trip
  // through the parser. Some engines glue the era onto the year
  // ("AM2018"): a mixed token sheds it and keeps the digits.
  const segments: string[] = [];
  for (const token of merged) {
    const folded = foldName(token);
    if (
      (eraNames.has(folded) || literalWords.has(folded)) &&
      monthTable.lookup(token) === undefined
    ) {
      continue;
    }
    // Only in multi-token input, which is the formatted-output shape the
    // shed exists for: in a lone token the digits would land in the DAY
    // slot and manufacture a plausible wrong completion ('AD26' is a year
    // to a human, not the 26th).
    if (!isNumber(token) && merged.length > 1) {
      const glued = [...eraNames].find(
        (era) =>
          (folded.startsWith(era) && isNumber(folded.slice(era.length))) ||
          (folded.endsWith(era) && isNumber(folded.slice(0, -era.length))),
      );
      if (glued !== undefined) {
        segments.push(
          folded.startsWith(glued) ? folded.slice(glued.length) : folded.slice(0, -glued.length),
        );
        continue;
      }
    }
    segments.push(token);
  }
  if (segments.length === 0 || segments.length > 3) {
    return TM_PARSE_ERROR;
  }

  const numeric: number[] = [];
  const numericRaw: string[] = [];
  let namedMonth: number | null = null;
  /** The style the matched spelling belongs to, when it pins one down. */
  let namedMonthStyle: TmDateStyle | undefined;
  for (const segment of segments) {
    if (/^\d{1,4}$/.test(segment)) {
      numeric.push(Number(segment));
      numericRaw.push(segment);
    } else if (namedMonth === null) {
      let matched = segment;
      let month = monthTable.lookup(matched);
      if (month === undefined) {
        // Some patterns GLUE a particle or era onto the month (Hebrew's
        // one-letter prepositions: 'ברביע' = 'ב' + the month) — shed one
        // affix and retry.
        const folded = foldName(segment);
        for (const word of [...literalWords, ...eraNames]) {
          if (folded.startsWith(word)) {
            matched = folded.slice(word.length);
            month = monthTable.lookup(matched);
          }
          if (month === undefined && folded.endsWith(word)) {
            matched = folded.slice(0, -word.length);
            month = monthTable.lookup(matched);
          }
          if (month !== undefined) {
            break;
          }
        }
      }
      if (month === undefined) {
        return TM_PARSE_ERROR;
      }
      namedMonth = month;
      namedMonthStyle = monthTable.styleOf(matched);
    } else {
      return TM_PARSE_ERROR; // a second word segment
    }
  }

  // All-numeric input reads in the NUMERIC pattern's field order; input
  // with a month name reads in the field order of the style whose
  // spelling matched, falling back to MEDIUM when the spelling is shared.
  // The three patterns disagree in some locales — fa is year-first
  // numeric and day-first medium, tt is year-first medium and day-first
  // long — so the order has to follow the pattern the text came from.
  const order = fieldOrderFor(
    locale,
    calendar.id,
    namedMonth === null ? 'numeric' : (namedMonthStyle ?? 'medium'),
  );
  let year: number | null = null;
  let month: number | null = namedMonth;
  let day: number | null = null;
  /** Whether the year slot was typed with at most two digits (pivot rule). */
  let shortYear = false;

  if (namedMonth === null) {
    // All-numeric: fill the locale's field-order slots front to back with
    // the omitted TRAILING fields completing from today. For fewer than
    // three segments the roles follow the relative order of the remaining
    // fields: one segment is the day; two are day and month in field order.
    const roles =
      numeric.length === 3
        ? order
        : numeric.length === 2
          ? order.filter((field) => field !== 'year')
          : (['day'] as const);
    for (let i = 0; i < numeric.length; i++) {
      const role = roles[i];
      if (role === 'year') {
        year = numeric[i];
        shortYear = numericRaw[i].length <= 2;
      } else if (role === 'month') {
        month = numeric[i];
      } else {
        day = numeric[i];
      }
    }
  } else {
    // A named month: the numeric segments fill day/year in their relative
    // field order (en-US `mar 5, 2026`: day then year; en-GB `5 mar 2026`:
    // day then year likewise — year almost universally trails).
    const roles = order.filter((field) => field !== 'month');
    if (numeric.length > roles.length) {
      return TM_PARSE_ERROR;
    }
    for (let i = 0; i < numeric.length; i++) {
      const role = roles[i];
      if (role === 'year') {
        year = numeric[i];
        shortYear = numericRaw[i].length <= 2;
      } else {
        day = numeric[i];
      }
    }
    // Completion is TRAILING-only: an omitted day is completable only where
    // the day slot follows the month in the locale's field order.
    if (day === null) {
      const dayIndex = order.indexOf('day');
      const monthIndex = order.indexOf('month');
      if (dayIndex < monthIndex) {
        return TM_PARSE_ERROR;
      }
    }
  }

  // Trailing completion from today, in the display calendar.
  year ??= today.year;
  month ??= today.month;
  day ??= today.day;

  // The two-digit-year pivot: [today − 80, today + 19] in the display
  // calendar's numbering. The window spans exactly 100 years, so a
  // two-digit value always lands inside it.
  if (shortYear) {
    const min = today.year - 80;
    let resolved = Math.floor(min / 100) * 100 + year;
    if (resolved < min) {
      resolved += 100;
    }
    year = resolved;
  }

  // In-calendar validation, then conversion — never reorder to force a match.
  if (year < 1 || month < 1 || month > calendar.monthsInYear(year)) {
    return TM_PARSE_ERROR;
  }
  if (day < 1 || day > calendar.daysInMonth(year, month)) {
    return TM_PARSE_ERROR;
  }
  const iso = calendar.fromParts({ year, month, day });
  return iso !== null && iso >= MIN_ISO && iso <= MAX_ISO ? iso : TM_PARSE_ERROR;
}

/**
 * The locale's numeric date pattern as a typing hint (e.g. `dd/mm/yyyy`) —
 * the field order and separators of the display calendar's numeric format.
 */
export function tmDatePlaceholder(
  locale: string,
  options?: { readonly calendar?: TmCalendar },
): string {
  const calendar = options?.calendar ?? tmGregorianCalendar();
  const parts = formatterFor(locale, calendar.id, 'numeric').formatToParts(utcDateOf(2001, 2, 3));
  let out = '';
  for (const part of parts) {
    if (part.type === 'year') {
      out += 'yyyy';
    } else if (part.type === 'month') {
      out += 'mm';
    } else if (part.type === 'day') {
      out += 'dd';
    } else {
      out += part.value.replace(FORMAT_CONTROL, '');
    }
  }
  return out.trim();
}
