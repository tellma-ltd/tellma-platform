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

/** Segment separators: slash, dot, dash, comma, Arabic date/decimal marks, spaces. */
const SEPARATORS = /[/.\-,،٫؍\s]+/u;

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

/** The locale's field order (subset of year/month/day), cached per locale × calendar. */
const fieldOrderCache = new Map<string, readonly ('year' | 'month' | 'day')[]>();

function fieldOrderFor(locale: string, calendarId: string): readonly ('year' | 'month' | 'day')[] {
  const key = `${locale}|${calendarId}`;
  let order = fieldOrderCache.get(key);
  if (order === undefined) {
    const parts = formatterFor(locale, calendarId, 'numeric').formatToParts(
      utcDateOf(2001, 2, 3),
    );
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

/** Case/diacritic-insensitive folding for month-name matching. */
function foldName(name: string): string {
  return name
    .normalize('NFD')
    .replace(/[\p{M}ـ.]/gu, '') // marks, tatweel, abbreviation dots
    .toLowerCase()
    .trim();
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
    for (const form of ['long', 'short'] as const) {
      const formatter = new Intl.DateTimeFormat(locale, {
        month: form,
        calendar: calendar.id,
        timeZone: 'UTC',
      });
      const months = calendar.monthsInYear(referenceYear);
      for (let month = 1; month <= months; month++) {
        const iso = calendar.fromParts({ year: referenceYear, month, day: 1 });
        if (iso === null) {
          continue;
        }
        const parts = ɵtmParseIsoDate(iso)!;
        const name = foldName(formatter.format(utcDateOf(parts.year, parts.month, parts.day)));
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

  // Era literals ("AH", "ዓ/ም") ride along in some calendars' formatted
  // output; they carry no information a segment slot needs, so they are
  // tolerated — formatted output must round-trip through the parser.
  const eraNames = eraNamesFor(locale, calendar.id);
  const segments = cleaned
    .split(SEPARATORS)
    .filter((segment) => segment !== '' && !eraNames.has(foldName(segment)));
  if (segments.length === 0 || segments.length > 3) {
    return TM_PARSE_ERROR;
  }

  const today = calendar.toParts(
    options?.today !== undefined && ɵtmParseIsoDate(options.today) !== null
      ? options.today
      : calendar.today(),
  );

  const numeric: number[] = [];
  const numericRaw: string[] = [];
  let namedMonth: number | null = null;
  for (const segment of segments) {
    if (/^\d{1,4}$/.test(segment)) {
      numeric.push(Number(segment));
      numericRaw.push(segment);
    } else if (namedMonth === null) {
      const month = monthNamesFor(locale, calendar, today.year).get(foldName(segment));
      if (month === undefined) {
        return TM_PARSE_ERROR;
      }
      namedMonth = month;
    } else {
      return TM_PARSE_ERROR; // a second word segment
    }
  }

  const order = fieldOrderFor(locale, calendar.id);
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
