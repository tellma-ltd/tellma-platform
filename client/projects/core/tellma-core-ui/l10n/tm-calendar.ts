// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// TODO: re-evaluate replacing @internationalized/date with native Temporal
// once it ships in stable Safari — the TmCalendar seam keeps the swap an
// internal change (no public API impact).

import { CalendarDate, GregorianCalendar, toCalendar, type Calendar } from '@internationalized/date';

/** A date's parts in a display calendar — 1-based, in-calendar numbering. */
export interface TmCalendarParts {
  /** The year in the calendar's own (current-era) numbering. */
  readonly year: number;
  /** The 1-based month within the year (Ethiopic years have 13). */
  readonly month: number;
  /** The 1-based day within the month. */
  readonly day: number;
}

/**
 * A pluggable display calendar. The backing value everywhere is an ISO
 * 8601 date string (proleptic Gregorian); a calendar only changes how that
 * value is DISPLAYED and ENTERED. Dates are pure calendar dates — no
 * time-of-day, no time zone; `today()` is the user's local date.
 *
 * Parts are expressed in the calendar's current era (AD / AH / Amete
 * Mihret); dates before the era's epoch are outside the supported range.
 */
export interface TmCalendar {
  /** The Intl calendar identifier: 'gregory', 'islamic-umalqura', 'ethiopic'. */
  readonly id: string;
  /** Converts an ISO date to this calendar's parts. */
  toParts(iso: string): TmCalendarParts;
  /** Converts in-calendar parts to ISO, or `null` when they name no real date. */
  fromParts(parts: TmCalendarParts): string | null;
  /** The number of months in the given in-calendar year (13 for Ethiopic). */
  monthsInYear(year: number): number;
  /** The number of days in the given in-calendar month. */
  daysInMonth(year: number, month: number): number;
  /** Today as an ISO date, in the user's local time zone. */
  today(): string;
}

/** Parses a strict `YYYY-MM-DD` string into numeric parts, or `null`. */
export function ɵtmParseIsoDate(
  iso: string,
): { year: number; month: number; day: number } | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
  if (match === null) {
    return null;
  }
  return { year: Number(match[1]), month: Number(match[2]), day: Number(match[3]) };
}

/** Formats numeric Gregorian parts as a strict `YYYY-MM-DD` string. */
export function ɵtmToIsoDate(year: number, month: number, day: number): string {
  const y = String(year).padStart(4, '0');
  const m = String(month).padStart(2, '0');
  const d = String(day).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

const gregorian = new GregorianCalendar();

/**
 * The shared adapter: a {@link TmCalendar} over an `@internationalized/date`
 * calendar implementation. Calendar classes are imported directly by each
 * entry point (never through a factory, which would defeat tree-shaking).
 */
export function ɵtmAdaptCalendar(calendar: Calendar, id: string): TmCalendar {
  const toCalendarDate = (iso: string): CalendarDate | null => {
    const parts = ɵtmParseIsoDate(iso);
    if (parts === null) {
      return null;
    }
    // The library CONSTRAINS invalid fields (2026-02-31 → Feb 28) instead
    // of throwing; a mismatch after construction means the ISO string
    // named no real date.
    const date = new CalendarDate(gregorian, parts.year, parts.month, parts.day);
    if (date.year !== parts.year || date.month !== parts.month || date.day !== parts.day) {
      return null;
    }
    return toCalendar(date, calendar);
  };
  /** A probe date for month/day queries — any real date in the year/month. */
  const probe = (year: number, month: number): CalendarDate =>
    new CalendarDate(calendar, year, month, 1);
  return {
    id,
    toParts(iso: string): TmCalendarParts {
      const date = toCalendarDate(iso);
      if (date === null) {
        throw new Error(`TmCalendar(${id}): '${iso}' is not a YYYY-MM-DD date`);
      }
      return { year: date.year, month: date.month, day: date.day };
    },
    fromParts(parts: TmCalendarParts): string | null {
      if (
        !Number.isInteger(parts.year) ||
        !Number.isInteger(parts.month) ||
        !Number.isInteger(parts.day) ||
        parts.year < 1 ||
        parts.month < 1 ||
        parts.day < 1
      ) {
        return null;
      }
      // The library CONSTRAINS out-of-range fields instead of throwing; a
      // round-trip mismatch therefore means the parts named no real date.
      const date = new CalendarDate(calendar, parts.year, parts.month, parts.day);
      if (date.year !== parts.year || date.month !== parts.month || date.day !== parts.day) {
        return null;
      }
      const iso = toCalendar(date, gregorian);
      if (iso.era !== 'AD' || iso.year < 1 || iso.year > 9999) {
        return null; // outside the ISO/BCL/SQL-compatible window
      }
      return ɵtmToIsoDate(iso.year, iso.month, iso.day);
    },
    monthsInYear(year: number): number {
      return calendar.getMonthsInYear(probe(year, 1));
    },
    daysInMonth(year: number, month: number): number {
      return calendar.getDaysInMonth(probe(year, month));
    },
    today(): string {
      const now = new Date();
      return ɵtmToIsoDate(now.getFullYear(), now.getMonth() + 1, now.getDate());
    },
  };
}

let gregorianSingleton: TmCalendar | undefined;

/**
 * The built-in Gregorian display calendar — the default everywhere. The
 * display parts equal the ISO parts by construction.
 */
export function tmGregorianCalendar(): TmCalendar {
  gregorianSingleton ??= ɵtmAdaptCalendar(gregorian, 'gregory');
  return gregorianSingleton;
}
