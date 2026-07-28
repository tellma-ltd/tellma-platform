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
 * spaces, and the CJK date-unit suffixes (`2026年7月27日`, `2026년 7월 27일`
 * tokenize to their numbers). Dots are NOT global separators — they split
 * only between digits (`27.03.2026`), because they also live inside month
 * abbreviations (`Rab. I`, `juil.`) and dotted era names (`ฮ.ศ.`).
 */
const SEPARATORS = /[/\-,،٫؍\s年月日号號년월일]+/u;

/** A dot acting as a numeric date separator (digit on both sides). */
const NUMERIC_DOT = /(?<=\d)\.(?=\s*\d)/gu;

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
 * locale × calendar × style. The NUMERIC and NAMED-MONTH patterns can
 * disagree (fa formats numeric dates year-first but medium dates
 * day-first), so all-numeric input reads in the numeric order while
 * named-month input reads in the medium order.
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
    const parts = formatterFor(locale, calendarId, style).formatToParts(utcDateOf(2001, 2, 3));
    order = parts
      .map((part) => part.type)
      .filter((type): type is 'year' | 'month' | 'day' =>
        type === 'year' || type === 'month' || type === 'day',
      );
    if (order.length !== 3) {
      order = ['year', 'month', 'day'];
    }
    fieldOrderCache.set(key, order);
  }
  return order;
}

/**
 * Case/diacritic-insensitive folding for month/era-name matching. Spaces
 * and hyphens are removed entirely — separator splitting may have cut a
 * hyphenated month name (`רביע אל-אוול`) into pieces that re-join with
 * spaces, so both sides must compare joint-free. Invisible format
 * controls fold too (some era names embed a ZWJ/ZWNJ; the input side
 * strips Cf globally, so the table side must match).
 */
function foldName(name: string): string {
  return name
    .normalize('NFD')
    .replace(/[\p{M}\p{Cf}ـ.\-־\s]/gu, '')
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
          if (folded !== '' && !months.has(folded)) {
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

/** Folded month name (long + short forms) → month number, per locale × calendar. */
const monthNameCache = new Map<string, ReadonlyMap<string, number>>();

function monthNamesFor(
  locale: string,
  calendar: TmCalendar,
  referenceYear: number,
): ReadonlyMap<string, number> {
  const key = `${locale}|${calendar.id}`;
  let names = monthNameCache.get(key);
  if (names === undefined) {
    const table = new Map<string, number>();
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
      const candidates: string[] = [];
      for (const formatter of standalone) {
        candidates.push(formatter.format(date));
      }
      // The IN-DATE forms too: several languages inflect the month inside
      // a date (ru genitive) or abbreviate it differently there (vi
      // 'Tháng 11' standalone vs 'thg 11' in a date) — formatted output
      // must round-trip, so the format-context spelling joins the table.
      for (const style of ['medium', 'long'] as const) {
        for (const part of formatterFor(locale, calendar.id, style).formatToParts(date)) {
          if (part.type === 'month') {
            candidates.push(part.value);
          }
        }
      }
      for (const candidate of candidates) {
        const name = foldName(candidate);
        if (name !== '' && !table.has(name)) {
          table.set(name, month);
        }
      }
    }
    names = table;
    monthNameCache.set(key, names);
  }
  return names;
}

/**
 * Parses free-typed date text into an ISO `YYYY-MM-DD` string. The
 * algorithm is deterministic — forgiving on separators and completion,
 * strict on ambiguity:
 *
 * 1. An input already in the ISO shape is accepted in any locale.
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

  // (1) The ISO fast path — unambiguous by construction, bounds included.
  const isoParts = ɵtmParseIsoDate(cleaned);
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
  const rawTokens = cleaned
    .replace(NUMERIC_DOT, '/')
    .split(SEPARATORS)
    .map((token) => token.replace(/^\.+|\.+$/g, ''))
    .filter((token) => token !== '');
  const tokens: string[] = [];
  for (const token of rawTokens) {
    const folded = foldName(token);
    if (eraNames.has(folded) || literalWords.has(folded)) {
      continue;
    }
    if (!/^\d{1,4}$/.test(token)) {
      // An era glued onto a year: shed it, keep the digits.
      const glued = [...eraNames].find(
        (era) =>
          (folded.startsWith(era) && /^\d{1,4}$/.test(folded.slice(era.length))) ||
          (folded.endsWith(era) && /^\d{1,4}$/.test(folded.slice(0, -era.length))),
      );
      if (glued !== undefined) {
        tokens.push(
          folded.startsWith(glued) ? folded.slice(glued.length) : folded.slice(0, -glued.length),
        );
        continue;
      }
    }
    tokens.push(token);
  }
  // Re-join multi-word month names: adjacent word tokens merge into one
  // segment (after the filters above, at most one word field remains).
  const segments: string[] = [];
  for (const token of tokens) {
    const isWord = !/^\d{1,4}$/.test(token);
    const previous = segments.length === 0 ? null : segments[segments.length - 1];
    if (isWord && previous !== null && !/^\d{1,4}$/.test(previous)) {
      segments[segments.length - 1] = `${previous} ${token}`;
    } else {
      segments.push(token);
    }
  }
  // A month name that is itself word + number ('thg 7' in vi): when one
  // segment too many remains, a word/number pair that matches the month
  // table as a whole merges into the month segment.
  if (segments.length === 4) {
    const monthTable = monthNamesFor(locale, calendar, today.year);
    for (let i = 0; i < segments.length - 1; i += 1) {
      if (
        !/^\d{1,4}$/.test(segments[i]) &&
        /^\d{1,4}$/.test(segments[i + 1]) &&
        monthTable.has(foldName(`${segments[i]} ${segments[i + 1]}`))
      ) {
        segments.splice(i, 2, `${segments[i]} ${segments[i + 1]}`);
        break;
      }
    }
  }
  if (segments.length === 0 || segments.length > 3) {
    return TM_PARSE_ERROR;
  }

  const numeric: number[] = [];
  const numericRaw: string[] = [];
  let namedMonth: number | null = null;
  for (const segment of segments) {
    if (/^\d{1,4}$/.test(segment)) {
      numeric.push(Number(segment));
      numericRaw.push(segment);
    } else if (namedMonth === null) {
      const table = monthNamesFor(locale, calendar, today.year);
      const folded = foldName(segment);
      let month = table.get(folded);
      if (month === undefined) {
        // Some patterns GLUE a literal particle onto the month (Hebrew's
        // one-letter prepositions: 'ברביע' = 'ב' + the month) — shed one
        // leading literal word and retry.
        for (const word of literalWords) {
          if (folded.startsWith(word)) {
            month = table.get(folded.slice(word.length));
            if (month !== undefined) {
              break;
            }
          }
        }
      }
      if (month === undefined) {
        return TM_PARSE_ERROR;
      }
      namedMonth = month;
    } else {
      return TM_PARSE_ERROR; // a second word segment
    }
  }

  // All-numeric input reads in the NUMERIC pattern's field order; input
  // with a month name reads in the MEDIUM pattern's (they disagree in
  // some locales — fa is year-first numeric, day-first medium).
  const order = fieldOrderFor(locale, calendar.id, namedMonth === null ? 'numeric' : 'medium');
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
