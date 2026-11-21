// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The instant formats: a date-time, a time of day and a relative time over
// an INSTANT (a point on the time line, the wire form of a server-side
// date-time-with-offset), each in an explicit display time zone — plus the
// zone's offset label and name. Nothing here reads the device's zone or
// clock: the zone and "now" are arguments, so every output is
// deterministic and golden-testable.

import { tmStripBidiControls } from './tm-bidi';
import { ɵtmToIsoDate, type TmCalendar } from './tm-calendar';
import {
  tmFormatDate,
  ɵtmDateSkeleton,
  ɵtmFormatWithoutEra,
  ɵtmPlainText,
  type TmDateStyle,
} from './tm-date-engine';

/**
 * Angular's development-mode flag, when the code runs inside an Angular
 * build (a production build replaces it with `false`, which removes the
 * warnings). Read through `typeof`, so plain Node — where nothing defines
 * it — never throws.
 */
declare const ngDevMode: unknown;

/** How much of the time of day a formatted instant shows. */
export type TmTimeStyle =
  /** The hour and the minute — "10:42". */
  | 'short'
  /** The hour, the minute and the second — "10:42:07". */
  | 'medium';

/** Options of {@link tmFormatDateTime}. */
export interface TmDateTimeFormatOptions {
  /** The display time zone, an IANA id (`Asia/Riyadh`). */
  readonly timeZone: string;
  /** The display calendar. Default Gregorian. */
  readonly calendar?: TmCalendar;
  /** The style of the date part. Default `numeric`. */
  readonly dateStyle?: TmDateStyle;
  /**
   * The style of the time part. Default `short`. `none` writes the
   * instant's calendar date in the zone, with no time.
   */
  readonly timeStyle?: TmTimeStyle | 'none';
  /**
   * `true` appends the zone's GMT offset at the instant, a space after the
   * text ("20/10/2026, 10:42 GMT+3"): the labelled text is the bare text
   * plus the label. A text without a time (see {@link tmFormatDateTime})
   * never carries it.
   */
  readonly zoneLabel?: boolean;
  /**
   * The text of the `quarter` date style, as in
   * {@link TmDateFormatOptions.quarterPattern}.
   */
  readonly quarterPattern?: string;
}

/** Options of {@link tmFormatTime}. */
export interface TmTimeFormatOptions {
  /** The display time zone, an IANA id (`Asia/Riyadh`). */
  readonly timeZone: string;
  /** The style of the time. Default `short`. */
  readonly timeStyle?: TmTimeStyle;
  /**
   * `true` appends the zone's GMT offset at the instant, a space after the
   * text ("10:42 GMT+3").
   */
  readonly zoneLabel?: boolean;
}

/** Options of {@link tmFormatRelativeTime}. */
export interface TmRelativeTimeFormatOptions {
  /** The caller's clock, in epoch milliseconds. */
  readonly now: number;
  /** The display time zone, an IANA id: it decides where calendar days begin. */
  readonly timeZone: string;
  /** The display calendar of the absolute date a distant instant falls back to. Default Gregorian. */
  readonly calendar?: TmCalendar;
  /** The width of the unit words. Default `short` ("2 min ago"). */
  readonly style?: 'long' | 'short' | 'narrow';
}

// ---- Parsing an instant ----

/**
 * The wire form: a date, `T`, a time to the minute or finer, and `Z` or a
 * `±hh:mm` offset. The fraction may be of any length.
 */
const INSTANT =
  /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2})(?:\.(\d+))?)?(Z|[+-]\d{2}:\d{2})$/;

/**
 * The epoch milliseconds of a UTC wall clock. `Date.UTC` maps the years
 * 0–99 into 1900–1999, so the year is set afterwards, on a leap year that
 * keeps 29 February intact until then.
 */
function utcMillis(
  year: number,
  month: number,
  day: number,
  hour: number,
  minute: number,
  second: number,
  millisecond: number,
): number {
  const date = new Date(Date.UTC(2000, month - 1, day, hour, minute, second, millisecond));
  date.setUTCFullYear(year);
  return date.getTime();
}

/**
 * Parses an ISO 8601 instant — `2026-10-20T07:42:00Z`,
 * `2026-10-20T10:42:00.1234567+03:00` — to epoch milliseconds, or `null`
 * when the text is not one: no offset, a field out of range, a day its
 * month does not have. A fraction longer than three digits is truncated
 * to milliseconds, never rounded.
 *
 * @internal Shared with the reactive formatting facade.
 */
export function ɵtmParseInstant(text: string): number | null {
  const match = INSTANT.exec(text);
  if (match === null) {
    return null;
  }
  const [year, month, day, hour, minute] = match.slice(1, 6).map(Number);
  const second = match[6] === undefined ? 0 : Number(match[6]);
  const millisecond = match[7] === undefined ? 0 : Number(match[7].slice(0, 3).padEnd(3, '0'));
  if (year < 1 || month < 1 || month > 12 || day < 1 || hour > 23 || minute > 59 || second > 59) {
    return null;
  }
  const wallClock = utcMillis(year, month, day, hour, minute, second, millisecond);
  if (new Date(wallClock).getUTCDate() !== day) {
    return null; // the day overflowed its month (30 February)
  }
  let offsetMinutes = 0;
  if (match[8] !== 'Z') {
    const hours = Number(match[8].slice(1, 3));
    const minutes = Number(match[8].slice(4, 6));
    if (hours > 23 || minutes > 59) {
      return null;
    }
    offsetMinutes = (match[8].startsWith('-') ? -1 : 1) * (hours * 60 + minutes);
  }
  return wallClock - offsetMinutes * 60_000;
}

/**
 * Whether epoch milliseconds name an instant a `Date` can hold: within
 * 100 million days of the epoch. `NaN`, the infinities and a number past
 * that range do not, and formatting one would throw.
 */
function isInstant(at: number): boolean {
  return Math.abs(at) <= 8.64e15;
}

/** What already warned in development, so each problem warns once. */
const warned = new Set<string>();

/** Warns once per distinct problem, in an Angular development build only. */
function warnOnce(message: string): void {
  if (typeof ngDevMode !== 'undefined' && ngDevMode && !warned.has(message)) {
    // Bounded: a page that keeps feeding distinct bad values must not grow
    // this set without limit.
    if (warned.size < 100) {
      warned.add(message);
    }
    console.warn(`[tellma-ui] ${message}`);
  }
}

/**
 * The instant behind a formatter's input: epoch milliseconds, `''` for an
 * empty input, or `null` — after a development warning — for text that is
 * not an instant.
 */
function readInstant(instant: string | null | undefined): number | '' | null {
  if (instant === null || instant === undefined || instant === '') {
    return '';
  }
  const millis = ɵtmParseInstant(instant);
  if (millis === null) {
    warnOnce(
      `"${instant}" is not an ISO 8601 instant with "Z" or a "±hh:mm" offset; it is shown as-is.`,
    );
  }
  return millis;
}

// ---- Zones ----

/** Intl formatters by key; the key names every option that shapes one. */
const formatters = new Map<string, Intl.DateTimeFormat>();

/**
 * The cached formatter for `key`, built from `options` in `timeZone`. A
 * zone id this browser's time-zone data does not know (a zone newer than
 * the browser) falls back to UTC with a development warning, so one
 * unknown id never throws out of every instant on the page; the offset
 * label then names the offset actually shown.
 */
function formatterFor(
  key: string,
  locale: string,
  timeZone: string,
  options: Intl.DateTimeFormatOptions,
): Intl.DateTimeFormat {
  const cacheKey = `${locale}|${timeZone}|${key}`;
  let formatter = formatters.get(cacheKey);
  if (formatter === undefined) {
    try {
      formatter = new Intl.DateTimeFormat(locale, { ...options, timeZone });
    } catch (error) {
      if (!(error instanceof RangeError) || timeZone === 'UTC') {
        throw error;
      }
      // Either the zone or the locale is unknown. Retry in UTC: an unknown
      // locale throws again, from here, exactly as it does for a date.
      formatter = new Intl.DateTimeFormat(locale, { ...options, timeZone: 'UTC' });
      warnOnce(`Unknown time zone "${timeZone}"; instants in it are shown in UTC.`);
    }
    formatters.set(cacheKey, formatter);
  }
  return formatter;
}

/** A wall clock: the fields of an instant as a zone's clocks show them. */
interface WallClock {
  readonly year: number;
  readonly month: number;
  readonly day: number;
  readonly hour: number;
  readonly minute: number;
  readonly second: number;
}

/**
 * The wall clock of an instant in a zone, in the Gregorian calendar and
 * Latin digits whatever the display locale is: it feeds arithmetic, never
 * the screen.
 */
function wallClockOf(at: number, timeZone: string): WallClock {
  const fields: Record<string, number> = {};
  const formatter = formatterFor('wall', 'en-u-ca-gregory-nu-latn', timeZone, {
    year: 'numeric',
    month: 'numeric',
    day: 'numeric',
    hour: 'numeric',
    minute: 'numeric',
    second: 'numeric',
    // 'h23', never `hour12: false`: some engines write midnight as 24 under
    // the latter.
    hourCycle: 'h23',
  });
  for (const part of formatter.formatToParts(at)) {
    fields[part.type] = Number(part.value);
  }
  return fields as unknown as WallClock;
}

/**
 * The zone's offset from UTC at an instant, in minutes east (`180` for
 * `Asia/Riyadh`, `330` for `Asia/Kolkata`). Two zones show the same wall
 * clock at an instant exactly when their offsets there are equal, whatever
 * their ids.
 *
 * @internal Shared with the reactive formatting facade.
 */
export function ɵtmZoneOffset(at: number, timeZone: string): number {
  const clock = wallClockOf(at, timeZone);
  const asUtc = utcMillis(clock.year, clock.month, clock.day, clock.hour, clock.minute, clock.second, 0);
  // The wall clock has no milliseconds; neither may the instant.
  return (asUtc - Math.floor(at / 1000) * 1000) / 60_000;
}

/**
 * The instant's calendar date in the zone, as an ISO `YYYY-MM-DD` string.
 * East of UTC the last hours of the year 9999 fall in the year 10000,
 * which no date holds: such an instant gives the last date there is.
 */
function zoneDateOf(at: number, timeZone: string): string {
  const clock = wallClockOf(at, timeZone);
  return clock.year > 9999 ? '9999-12-31' : ɵtmToIsoDate(clock.year, clock.month, clock.day);
}

/** The whole days between two ISO dates (`to` − `from`). */
function daysBetween(from: string, to: string): number {
  return Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000);
}

/** One named part of a formatted instant (the zone's name), as plain text. */
function zonePartOf(
  at: number,
  locale: string,
  timeZone: string,
  timeZoneName: 'shortOffset' | 'longGeneric',
): string {
  if (!isInstant(at)) {
    return '';
  }
  const part = formatterFor(timeZoneName, locale, timeZone, { timeZoneName })
    .formatToParts(at)
    .find((candidate) => candidate.type === 'timeZoneName');
  return ɵtmPlainText(part?.value ?? '');
}

/**
 * The time zone's offset from GMT at an instant, in the locale's words and
 * digits: "GMT+3", "GMT+5:30", "غرينتش+٣". An offset, never an
 * abbreviation: "IST" and "CST" each name several zones, "GMT+5:30" names
 * one offset. `instant` is an ISO 8601 instant or epoch milliseconds; text
 * that is not an instant, or a number no `Date` can hold, gives `''`.
 */
export function tmFormatZoneOffset(
  instant: string | number,
  locale: string,
  timeZone: string,
): string {
  const at = typeof instant === 'number' ? instant : readInstant(instant);
  return typeof at === 'number' ? zonePartOf(at, locale, timeZone, 'shortOffset') : '';
}

/**
 * The time zone's own name in the locale, independent of daylight saving:
 * "United Kingdom Time" for `Europe/London`. `at` (epoch milliseconds)
 * picks the name in force then, for a zone whose name changed; a number
 * no `Date` can hold gives `''`.
 */
export function tmFormatZoneName(timeZone: string, locale: string, at: number): string {
  return zonePartOf(at, locale, timeZone, 'longGeneric');
}

/**
 * Whether an instant shown in the display zone carries its offset label.
 *
 * - `auto`: when the display zone's offset at the instant differs from
 *   the device zone's there.
 * - `column`: for the cells of a column whose header was labelled at
 *   `headerAt`. Under a labelled header (the two zones' offsets differ at
 *   `headerAt`), a cell labels when its own display offset differs from
 *   the header's, as across a daylight-saving change; under a bare header
 *   it labels as `auto` does.
 * - `none`: never.
 *
 * Offsets are compared at the instants shown, never zone ids: two zones
 * that share an offset show the same clock.
 *
 * `instant` is an ISO 8601 instant or epoch milliseconds; an empty value,
 * text that is not an instant or a number no `Date` can hold is never
 * labelled. `headerAt` is epoch milliseconds, read under `column` alone;
 * one no `Date` can hold counts as a bare header.
 *
 * @internal Shared with the reactive formatting facade.
 */
export function ɵtmShowsZoneLabel(
  instant: string | number | null | undefined,
  timeZone: string,
  deviceTimeZone: string,
  mode: 'auto' | 'column' | 'none' = 'auto',
  headerAt?: number,
): boolean {
  // The same zone id is the same clock at every instant: no offsets to read.
  if (mode === 'none' || timeZone === deviceTimeZone) {
    return false;
  }
  const at = typeof instant === 'number' ? instant : ɵtmParseInstant(instant ?? '');
  if (at === null || !isInstant(at)) {
    return false;
  }
  const offset = ɵtmZoneOffset(at, timeZone);
  if (mode === 'column' && headerAt !== undefined && isInstant(headerAt)) {
    const headerOffset = ɵtmZoneOffset(headerAt, timeZone);
    if (headerOffset !== ɵtmZoneOffset(headerAt, deviceTimeZone)) {
      return offset !== headerOffset; // a labelled header already names its own offset
    }
  }
  return offset !== ɵtmZoneOffset(at, deviceTimeZone);
}

// ---- The formats ----

/**
 * `text` with the zone's GMT offset at the instant appended when `label`
 * asks for it. Appended, never formatted in: a zone field in the skeleton
 * makes the engine pick another pattern, which rewrites the hour it
 * labels ("1:30" becomes "01:30 GMT+3").
 */
function withZoneLabel(
  text: string,
  label: boolean | undefined,
  at: number,
  locale: string,
  timeZone: string,
): string {
  return label === true ? `${text} ${zonePartOf(at, locale, timeZone, 'shortOffset')}` : text;
}

/** The Intl time fields of a time style. */
function timeFields(timeStyle: TmTimeStyle): Intl.DateTimeFormatOptions {
  return {
    hour: 'numeric',
    minute: '2-digit',
    ...(timeStyle === 'medium' ? { second: '2-digit' as const } : {}),
  };
}

/**
 * Formats an instant as a date and a time of day in the display zone and
 * calendar: "20/10/2026, 10:42" ('' for null/undefined/''; text that is
 * not an ISO 8601 instant with `Z` or a `±hh:mm` offset is returned as-is,
 * with a development warning).
 *
 * The date part follows `dateStyle`'s fixed skeleton and the time part
 * shows the hour and minute in the locale's hour cycle (`medium` adds the
 * second). The calendar's era is never written, the text carries no
 * bidirectional control character, and a plain space separates the time
 * from its day period ("10:42 AM").
 *
 * A text without a time is the instant's calendar date in the zone,
 * formatted as `tmFormatDate` does, and never carries the zone label. That
 * is `timeStyle: 'none'`, and also the period styles `month`, `quarter`
 * and `year` under any time style: a period names no day for a time to
 * belong to. An instant whose date in the zone lies past 31 December 9999
 * (the last hours of that year, shown east of UTC) gives that last date.
 */
export function tmFormatDateTime(
  instant: string | null | undefined,
  locale: string,
  options: TmDateTimeFormatOptions,
): string {
  const at = readInstant(instant);
  if (typeof at !== 'number') {
    return at === '' ? '' : (instant as string);
  }
  const dateStyle = options.dateStyle ?? 'numeric';
  const timeStyle = options.timeStyle ?? 'short';
  if (timeStyle === 'none' || dateStyle === 'month' || dateStyle === 'quarter' || dateStyle === 'year') {
    return tmFormatDate(zoneDateOf(at, options.timeZone), locale, {
      calendar: options.calendar,
      dateStyle,
      quarterPattern: options.quarterPattern,
    });
  }
  const calendarId = options.calendar?.id ?? 'gregory';
  return withZoneLabel(
    ɵtmFormatWithoutEra(
      formatterFor(`${calendarId}|${dateStyle}|${timeStyle}`, locale, options.timeZone, {
        ...ɵtmDateSkeleton(dateStyle),
        ...timeFields(timeStyle),
        calendar: calendarId,
      }),
      new Date(at),
    ),
    options.zoneLabel,
    at,
    locale,
    options.timeZone,
  );
}

/**
 * Formats an instant's time of day in the display zone, in the locale's
 * hour cycle and digits: "10:42", "10:42 AM", "١٠:٤٢ ص" ('' for
 * null/undefined/''; text that is not an ISO 8601 instant with `Z` or a
 * `±hh:mm` offset is returned as-is, with a development warning). The
 * text carries no bidirectional control character, and a plain space
 * separates the time from its day period.
 */
export function tmFormatTime(
  instant: string | null | undefined,
  locale: string,
  options: TmTimeFormatOptions,
): string {
  const at = readInstant(instant);
  if (typeof at !== 'number') {
    return at === '' ? '' : (instant as string);
  }
  const timeStyle = options.timeStyle ?? 'short';
  return withZoneLabel(
    ɵtmPlainText(
      formatterFor(`time|${timeStyle}`, locale, options.timeZone, timeFields(timeStyle)).format(at),
    ),
    options.zoneLabel,
    at,
    locale,
    options.timeZone,
  );
}

/** `Intl.RelativeTimeFormat`s by locale and style. */
const relativeFormatters = new Map<string, Intl.RelativeTimeFormat>();

/**
 * Formats how far an instant lies from `now`, in the locale's own words:
 * "now", "2 min ago", "in 5 min", "3 hr ago", "yesterday", "3 days ago"
 * ('' for null/undefined/''; text that is not an ISO 8601 instant with `Z`
 * or a `±hh:mm` offset is returned as-is, with a development warning).
 *
 * The distance is truncated, never rounded up — 119 seconds ago is "1 min
 * ago":
 *
 * - under 60 seconds: "now";
 * - under 60 minutes: whole minutes;
 * - under 24 hours: whole hours;
 * - from 24 hours, while fewer than 7 calendar days apart in the zone:
 *   days, counted as the difference between the two calendar dates there
 *   ("yesterday" is the previous date, however many hours ago);
 * - otherwise the absolute date: `tmFormatDate` with the `medium` style,
 *   in the zone and the calendar ("13 Oct 2026").
 *
 * An instant after `now` reads forward ("in 5 min", "tomorrow"). The
 * caller owns the clock: pass a fresh `now` to refresh the text.
 */
export function tmFormatRelativeTime(
  instant: string | null | undefined,
  locale: string,
  options: TmRelativeTimeFormatOptions,
): string {
  const at = readInstant(instant);
  if (typeof at !== 'number') {
    return at === '' ? '' : (instant as string);
  }
  const style = options.style ?? 'short';
  const key = `${locale}|${style}`;
  let formatter = relativeFormatters.get(key);
  if (formatter === undefined) {
    formatter = new Intl.RelativeTimeFormat(locale, { numeric: 'auto', style });
    relativeFormatters.set(key, formatter);
  }
  const elapsed = options.now - at;
  const distance = Math.abs(elapsed);
  // Intl counts the past as negative.
  const direction = elapsed > 0 ? -1 : 1;
  let text: string | undefined;
  if (distance < 60_000) {
    text = formatter.format(0, 'second');
  } else if (distance < 3_600_000) {
    text = formatter.format(direction * Math.floor(distance / 60_000), 'minute');
  } else if (distance < 86_400_000) {
    text = formatter.format(direction * Math.floor(distance / 3_600_000), 'hour');
  } else if (isInstant(options.now)) {
    // Calendar days in the zone, not 24-hour blocks: 23:00 two dates back
    // is "2 days ago" at 00:30, 25.5 hours later. On the one day a year
    // that lasts 25 hours, an instant 24 hours back can share today's
    // date; Intl then says "today".
    const days = daysBetween(zoneDateOf(options.now, options.timeZone), zoneDateOf(at, options.timeZone));
    if (Math.abs(days) < 7) {
      text = formatter.format(days, 'day');
    }
  }
  return text !== undefined
    ? tmStripBidiControls(text)
    : tmFormatDate(zoneDateOf(at, options.timeZone), locale, {
        calendar: options.calendar,
        dateStyle: 'medium',
      });
}
