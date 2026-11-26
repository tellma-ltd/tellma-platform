// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The date engine's format half: formats ISO 8601 date strings (the value
// currency) per locale × display calendar. Month and era names come from
// Intl/CLDR at runtime — no name tables ship in any package, and every
// calendar renders localized in every UI language. The parse half lives in
// @tellma/core-ui/l10n-input, so code that only formats never loads it.

import { tmStripBidiControls } from './tm-bidi';
import {
  tmGregorianCalendar,
  ɵtmParseIsoDate,
  ɵtmToIsoDate,
  type TmCalendar,
} from './tm-calendar';
import { tmFormatNumber } from './tm-number-codec';

/**
 * The display style of a formatted date. Each maps to a fixed CLDR
 * skeleton, fixed in code (not configurable) so a server-side
 * implementation reproduces the same output from the same parameters:
 *
 * - `numeric`: yMd — "20/10/2026";
 * - `medium`: yMMMd — "20 Oct 2026";
 * - `long`: yMMMMd — "20 October 2026";
 * - `weekday`: yMMMEEEEd — "Tuesday, 20 Oct 2026";
 * - `month`: yMMM — "Oct 2026", the date's month;
 * - `year`: y — "2026", the date's year;
 * - `quarter`: the date's quarter, written through
 *   {@link TmDateFormatOptions.quarterPattern} — "Q4 2026".
 *
 * `month`, `quarter` and `year` name a period: every day of the period
 * formats to the same text.
 */
export type TmDateStyle = 'numeric' | 'medium' | 'long' | 'month' | 'quarter' | 'year' | 'weekday';

/** Options of {@link tmFormatDate}. */
export interface TmDateFormatOptions {
  /** The display calendar. Default Gregorian. */
  readonly calendar?: TmCalendar;
  /** The display style. Default `numeric`. */
  readonly dateStyle?: TmDateStyle;
  /**
   * The text of the `quarter` style: `{quarter}` stands for the quarter
   * number and `{year}` for the calendar year, both written in the
   * locale's digits. Default `Q{quarter} {year}`. Intl has no quarter
   * field, so the wording is the caller's to translate.
   */
  readonly quarterPattern?: string;
}

/** The styles Intl formats from a skeleton — every style but `quarter`. */
type SkeletonStyle = Exclude<TmDateStyle, 'quarter'>;

/** The fixed skeleton per style, as Intl option bags. */
const STYLE_OPTIONS: Record<SkeletonStyle, Intl.DateTimeFormatOptions> = {
  numeric: { year: 'numeric', month: 'numeric', day: 'numeric' },
  medium: { year: 'numeric', month: 'short', day: 'numeric' },
  long: { year: 'numeric', month: 'long', day: 'numeric' },
  weekday: { weekday: 'long', year: 'numeric', month: 'short', day: 'numeric' },
  month: { year: 'numeric', month: 'short' },
  year: { year: 'numeric' },
};

/** The quarter text when the caller passes no pattern. */
const DEFAULT_QUARTER_PATTERN = 'Q{quarter} {year}';

/**
 * The fixed Intl skeleton of a date style, for the formatters that add a
 * time of day to it. The `quarter` style has none (Intl has no quarter
 * field).
 *
 * @internal
 */
export function ɵtmDateSkeleton(
  style: Exclude<TmDateStyle, 'quarter'>,
): Intl.DateTimeFormatOptions {
  return STYLE_OPTIONS[style];
}

// ---- Day-first numeric dates ----

// CLDR 48 gives Oromo and Tigrinya the month-first numeric date `M/d/y`, a
// regression against every earlier release and against Amharic beside them.
// The server pins their short date to `dd/MM/yyyy` (spec 0013 §9.2
// culture data), so the client writes, and reads, the same order
// whatever the browser's CLDR.

/** The languages whose numeric dates are written day-first, as `dd/MM/yyyy`. */
const DAY_FIRST_LANGUAGES: ReadonlySet<string> = new Set(['om', 'ti']);

/** The slash between the fields of a day-first date. */
const SLASH: Intl.DateTimeFormatPart = { type: 'literal', value: '/' };

/** A date formatter that writes its numeric date fields as `dd/MM/yyyy`. */
class TmDayFirstFormat extends Intl.DateTimeFormat {
  /**
   * The parts of a date, its day, month and year in that order.
   *
   * @param date - The date.
   * @returns The parts.
   */
  override formatToParts(date?: Date | number): Intl.DateTimeFormatPart[] {
    const parts = super.formatToParts(date);
    const fields = parts.filter((part) => part.type === 'day' || part.type === 'month' || part.type === 'year');
    if (fields.length !== 3) {
      return parts;
    }
    // The run from the first date field to the last becomes `dd/MM/yyyy`;
    // the day-first languages write Latin digits, padded with a Latin zero.
    const field = (type: 'day' | 'month' | 'year', width: number): Intl.DateTimeFormatPart => ({
      type,
      value: fields.find((part) => part.type === type)!.value.padStart(width, '0'),
    });
    return [
      ...parts.slice(0, parts.indexOf(fields[0])),
      field('day', 2),
      SLASH,
      field('month', 2),
      SLASH,
      field('year', 4),
      ...parts.slice(parts.indexOf(fields[2]) + 1),
    ];
  }

  /**
   * The text of a date, its day, month and year in that order.
   *
   * @param date - The date.
   * @returns The text.
   */
  override format(date?: Date | number): string {
    return this.formatToParts(date)
      .map((part) => part.value)
      .join('');
  }
}

/**
 * The language a locale formats for: the one its private-use subtag names,
 * as `en-US-x-om` does when the browser lacks Oromo, else its own.
 */
function requestedLanguage(locale: string): string {
  const subtags = locale.toLowerCase().split(/[-_]/);
  // `x` is the private-use singleton: no other subtag is a lone `x`.
  const privateUse = subtags.indexOf('x');
  return privateUse > 0 && privateUse < subtags.length - 1 ? subtags[privateUse + 1] : subtags[0];
}

/**
 * A date formatter: Intl's, except that a numeric date in a day-first
 * language writes its fields as `dd/MM/yyyy`, as the server does. The
 * language is the one the locale formats for, so a fallback locale such as
 * `en-US-x-om` writes day-first too.
 *
 * @param locale - The locale.
 * @param options - The fields and the zone.
 * @returns The formatter.
 * @internal Shared with the instant formatters.
 */
export function ɵtmDateTimeFormat(locale: string, options: Intl.DateTimeFormatOptions): Intl.DateTimeFormat {
  const numeric = options.year === 'numeric' && options.month === 'numeric' && options.day === 'numeric';
  return numeric && DAY_FIRST_LANGUAGES.has(requestedLanguage(locale))
    ? new TmDayFirstFormat(locale, options)
    : new Intl.DateTimeFormat(locale, options);
}

const formatterCache = new Map<string, Intl.DateTimeFormat>();

/**
 * The formatter of a locale, a display calendar and a skeleton style, in
 * UTC, cached. Internal to the platform: the date parser reads the
 * locale's field order and names from the same formatters.
 *
 * @param locale - The locale.
 * @param calendarId - The display calendar's Intl id.
 * @param style - The skeleton style.
 * @returns The formatter.
 */
export function ɵtmDateFormatter(
  locale: string,
  calendarId: string,
  style: Exclude<TmDateStyle, 'quarter'>,
): Intl.DateTimeFormat {
  const key = `${locale}|${calendarId}|${style}`;
  let formatter = formatterCache.get(key);
  if (formatter === undefined) {
    formatter = ɵtmDateTimeFormat(locale, {
      ...STYLE_OPTIONS[style],
      calendar: calendarId,
      timeZone: 'UTC',
    });
    formatterCache.set(key, formatter);
  }
  return formatter;
}

/**
 * A UTC Date at noon of the ISO date — immune to zone/DST edge shifts.
 * Internal to the platform.
 *
 * @param year - The Gregorian year.
 * @param month - The month, 1 to 12.
 * @param day - The day of the month.
 * @returns The date.
 */
export function ɵtmUtcDateOf(year: number, month: number, day: number): Date {
  const date = new Date(Date.UTC(2000, month - 1, day, 12));
  date.setUTCFullYear(year); // Date.UTC maps years 0–99 into 1900–1999
  return date;
}

/**
 * Formats an ISO `YYYY-MM-DD` date for display in the given locale and
 * display calendar ('' for null/undefined/''; a malformed input is
 * returned as-is, mirroring the number codec's posture for non-values).
 *
 * The calendar's era is never written, and the text carries no
 * bidirectional control character: where CLDR's patterns put a direction
 * mark between the parts of a date, it is removed, so a numeric date is
 * one run of digits and separators that markup orders. Its spaces are
 * plain spaces: where CLDR's patterns put a narrow no-break space (before
 * the Bulgarian year marker, say), a plain one is written, as browsers do,
 * so the text is the same in every engine.
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
  const style = options?.dateStyle ?? 'numeric';
  const date = ɵtmUtcDateOf(parts.year, parts.month, parts.day);
  // The ISO text is rebuilt from the Date, so a day past its month's end
  // rolls over in the calendar's parts exactly as it does in Intl's.
  const rolled = ɵtmToIsoDate(date.getUTCFullYear(), date.getUTCMonth() + 1, date.getUTCDate());
  if (style === 'quarter') {
    // The quarter and its year are the display calendar's, read through
    // the calendar seam.
    const inCalendar = calendar.toParts(rolled);
    // Three months to a quarter, capped at four: Ethiopic's thirteenth
    // month falls in the fourth quarter (spec 0010 §D.7).
    const quarter = tmFormatNumber(Math.min(4, Math.ceil(inCalendar.month / 3)), locale);
    const year = tmFormatNumber(inCalendar.year, locale, { grouping: false });
    return tmStripBidiControls(
      (options?.quarterPattern ?? DEFAULT_QUARTER_PATTERN).replace(
        /\{(quarter|year)\}/g,
        (_placeholder, name: string) => (name === 'quarter' ? quarter : year),
      ),
    );
  }
  return ɵtmFormatWithoutEra(ɵtmDateFormatter(locale, calendar.id, style), date, {
    calendar,
    iso: rolled,
  });
}

/**
 * The text contract of everything `Intl.DateTimeFormat` writes for the
 * page: no bidirectional control character, and a plain space for every
 * U+202F NARROW NO-BREAK SPACE. CLDR puts the narrow space before a day
 * period ("10:42 AM") and inside some date patterns; browsers replace it
 * with a plain space, because text copied with it is not read back as a
 * date or a time by spreadsheets and parsers, while other engines pass it
 * through. Writing the plain space everywhere keeps one value's text
 * equal across engines.
 *
 * @internal Shared with the instant formatters.
 */
export function ɵtmPlainText(text: string): string {
  return tmStripBidiControls(text).replace(/\u202F/g, ' ');
}

/**
 * The calendar date a formatter writes, for the calendars that date days
 * by their own rule (see {@link TmCalendar.ɵownConversion}).
 *
 * @internal Shared with the calendar popup and the instant formatters.
 */
export interface ɵTmOwnDate {
  /** The display calendar the formatter was built for. */
  readonly calendar: TmCalendar;
  /** The ISO date the formatted moment falls on, in the formatter's time zone. */
  readonly iso: string;
}

/** Milliseconds in a day. */
const DAY_MS = 86_400_000;

/** The part types an own-conversion calendar takes from Intl's view of its month. */
const MONTH_NAMED_PARTS: ReadonlySet<string> = new Set([
  'year',
  'relatedYear',
  'yearName',
  'month',
  'era',
]);

/** The number format of each formatter's day field, in its digits and padding. */
const dayFormats = new WeakMap<Intl.DateTimeFormat, Intl.NumberFormat>();

/**
 * The number format that writes a day the way `formatter` writes its day
 * field: in its numbering system, padded to two digits when its pattern
 * pads (`dd`). The padding is read off a day Intl numbers below 10, since
 * `resolvedOptions()` omits the fields of a `dateStyle` formatter.
 */
function dayFormatOf(formatter: Intl.DateTimeFormat): Intl.NumberFormat {
  let format = dayFormats.get(formatter);
  if (format === undefined) {
    const { locale, numberingSystem } = formatter.resolvedOptions();
    const plain = new Intl.NumberFormat(locale, { numberingSystem, useGrouping: false });
    const padded = new Intl.NumberFormat(locale, {
      numberingSystem,
      useGrouping: false,
      minimumIntegerDigits: 2,
    });
    format = plain;
    // Up to a month of probe days: one of the first nine days of a month
    // turns up within it, whatever the calendar.
    search: for (let offset = 0; offset < 31; offset++) {
      const day = formatter
        .formatToParts(Date.UTC(2001, 0, 1 + offset, 12))
        .find((part) => part.type === 'day');
      if (day === undefined) {
        break;
      }
      const value = tmStripBidiControls(day.value);
      for (let number = 1; number <= 9; number++) {
        if (value === padded.format(number)) {
          format = padded;
          break search;
        }
        if (value === plain.format(number)) {
          break search;
        }
      }
    }
    dayFormats.set(formatter, format);
  }
  return format;
}

/** The days from 1970-01-01 to an ISO date. */
function dayNumberOf(iso: string): number {
  const parts = ɵtmParseIsoDate(iso)!;
  return Math.round(ɵtmUtcDateOf(parts.year, parts.month, parts.day).getTime() / DAY_MS);
}

/**
 * The parts `formatter` writes for `date`, with an own-conversion
 * calendar's year, month and day in place of Intl's. Intl supplies the
 * pattern, the digits and the names: the month and year are read from its
 * view of the middle of the calendar's month, which lies in the same month
 * whichever rule dates it (the rules differ by a day or two), and the day
 * is written in the formatter's digits. The weekday and the time of day
 * are the real moment's.
 */
function ownDateParts(
  formatter: Intl.DateTimeFormat,
  date: Date,
  own: ɵTmOwnDate,
): Intl.DateTimeFormatPart[] {
  const parts = formatter.formatToParts(date);
  const { calendar, iso } = own;
  const inCalendar = calendar.toParts(iso);
  const middle = calendar.fromParts({
    year: inCalendar.year,
    month: inCalendar.month,
    day: Math.min(15, calendar.daysInMonth(inCalendar.year, inCalendar.month)),
  });
  if (middle === null) {
    return parts;
  }
  const anchor = formatter.formatToParts(
    new Date(date.getTime() + (dayNumberOf(middle) - dayNumberOf(iso)) * DAY_MS),
  );
  // One formatter writes one sequence of parts for every date.
  if (anchor.length !== parts.length) {
    return parts;
  }
  return parts.map((part, index): Intl.DateTimeFormatPart => {
    if (part.type === 'day') {
      return { type: 'day', value: dayFormatOf(formatter).format(inCalendar.day) };
    }
    return MONTH_NAMED_PARTS.has(part.type) && anchor[index].type === part.type
      ? anchor[index]
      : part;
  });
}

/**
 * Formats a date with the calendar's ERA SUFFIX removed (`9/16/1447 AH`
 * becomes `9/16/1447`).
 *
 * ICU appends an era to non-Gregorian calendars whether or not one was
 * requested, but inside a field whose calendar the user chose there is
 * only ever one era in play, so the suffix is noise that costs width in
 * every cell and input. The literal that joined it goes with it.
 *
 * The result also carries no bidirectional control character and only
 * plain spaces (see `ɵtmPlainText`).
 *
 * For a calendar that dates days by its own rule, pass `own`: the year,
 * month and day are then the calendar's, not Intl's.
 *
 * @internal Shared with the calendar popup, which builds its own headers,
 * and with the instant formatters.
 */
export function ɵtmFormatWithoutEra(
  formatter: Intl.DateTimeFormat,
  date: Date,
  own?: ɵTmOwnDate,
): string {
  const parts =
    own?.calendar.ɵownConversion === true
      ? ownDateParts(formatter, date, own)
      : formatter.formatToParts(date);
  if (!parts.some((part) => part.type === 'era')) {
    return ɵtmPlainText(parts.map((part) => part.value).join(''));
  }
  const isField = (part: Intl.DateTimeFormatPart | undefined): boolean =>
    part !== undefined && part.type !== 'literal' && part.type !== 'era';
  /** Whether a separator is more than spacing (a comma, a particle). */
  const isWord = (part: Intl.DateTimeFormatPart): boolean =>
    tmStripBidiControls(part.value).trim() !== '';
  const kept: Intl.DateTimeFormatPart[] = [];
  for (let index = 0; index < parts.length; index++) {
    const part = parts[index];
    if (part.type === 'era') {
      continue;
    }
    const leadsEra = parts[index + 1]?.type === 'era';
    const trailsEra = parts[index - 1]?.type === 'era';
    if (part.type === 'literal' && (leadsEra || trailsEra)) {
      // Some locales put the era BETWEEN two fields (Thai writes the
      // month, the era, then the year; a date-time writes the year, the
      // era, then the time). Dropping both of its separators would weld
      // those fields together — keep exactly one: the one before the
      // era, unless that one is plain spacing and the one after says
      // more ("1447 AH, 10:42" keeps the comma).
      const eraIndex = leadsEra ? index + 1 : index - 1;
      const joinsFields =
        parts.slice(0, eraIndex).some(isField) && parts.slice(eraIndex + 1).some(isField);
      const before = parts[eraIndex - 1];
      const after = parts[eraIndex + 1];
      const keepsAfter =
        before?.type !== 'literal' ||
        (after?.type === 'literal' && !isWord(before) && isWord(after));
      if (joinsFields && leadsEra !== keepsAfter) {
        kept.push(part);
      }
      continue;
    }
    kept.push(part);
  }
  return ɵtmPlainText(kept.map((part) => part.value).join('')).trim();
}
