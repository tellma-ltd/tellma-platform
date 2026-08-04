// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ɵtmParseIsoDate, ɵtmToIsoDate, type TmCalendar, type TmCalendarParts } from '@tellma/core-ui/l10n';

// Self-contained Ethiopic arithmetic, NOT the @internationalized/date
// adapter: that library's EthiopicCalendar maps TWO Gregorian days onto
// Pagume 5 in every non-leap year (its new year lands one day late — 151
// wrong days across 1900–2100), which the adapter↔Intl agreement gate
// caught against Chromium's ICU. Reported upstream as
// https://github.com/adobe/react-spectrum/issues/10390; revisit this pack
// if it lands. The calendar's rules are exact and tiny —
// twelve 30-day months plus Pagume (5 days, 6 when `year % 4 == 3`) — so
// the arithmetic lives here, anchored on an ICU-verified epoch and pinned
// by the agreement gate's year-boundary sweep.

/** ICU-verified anchor: 12 September 2023 (Gregorian) = 1 Meskerem 2016 AM. */
const ANCHOR_UTC = Date.UTC(2023, 8, 12);
const ANCHOR_YEAR = 2016;
const DAY_MS = 86_400_000;

/** Whether the Amete Mihret year has a 6-day Pagume. */
function isLeap(year: number): boolean {
  return ((year % 4) + 4) % 4 === 3;
}

/** The number of leap years in `[0, year)` — leap when `y % 4 == 3`. */
function leapsBefore(year: number): number {
  return Math.floor(year / 4);
}

/** Days from 1 Meskerem `ANCHOR_YEAR` to 1 Meskerem `year` (negative for earlier years). */
function daysToYearStart(year: number): number {
  return (year - ANCHOR_YEAR) * 365 + (leapsBefore(year) - leapsBefore(ANCHOR_YEAR));
}

/** A UTC timestamp at midnight of the Gregorian date, safe for years < 100. */
function utcOf(year: number, month: number, day: number): number {
  const date = new Date(Date.UTC(2000, month - 1, day));
  date.setUTCFullYear(year);
  return date.getTime();
}

let singleton: TmCalendar | undefined;

/**
 * The Ethiopic display calendar (Amete Mihret era numbering). Register it
 * as the app-ambient calendar via
 * `provideTmCalendar(tmEthiopicCalendar())`, or pass it per instance; the
 * backing values stay ISO (proleptic Gregorian) either way.
 *
 * Years have 13 months: Pagume, the 13th, is a real, selectable month of
 * 5 days (6 in leap years) — `monthsInYear`/`daysInMonth` report it with
 * no special-casing needed at call sites. Month and era names render from
 * Intl in the active UI language.
 *
 * Supported window: dates from the Amete Mihret epoch (ISO `0008-08-27`)
 * onward. Earlier ISO dates belong to the preceding Amete Alem era and
 * surface here with zero/negative year numbers — outside the supported
 * window; the picker treats them as out of range.
 */
export function tmEthiopicCalendar(): TmCalendar {
  singleton ??= {
    id: 'ethiopic',
    toParts(iso: string): TmCalendarParts {
      const parsed = ɵtmParseIsoDate(iso);
      if (parsed === null) {
        throw new Error(`TmCalendar(ethiopic): '${iso}' is not a YYYY-MM-DD date`);
      }
      const delta = Math.round((utcOf(parsed.year, parsed.month, parsed.day) - ANCHOR_UTC) / DAY_MS);
      let year = ANCHOR_YEAR + Math.floor(delta / 366);
      while (daysToYearStart(year + 1) <= delta) {
        year += 1;
      }
      while (daysToYearStart(year) > delta) {
        year -= 1;
      }
      const within = delta - daysToYearStart(year);
      const month = Math.floor(within / 30) + 1;
      const day = (within % 30) + 1;
      return { year, month, day };
    },
    fromParts(parts: TmCalendarParts): string | null {
      if (
        !Number.isInteger(parts.year) ||
        !Number.isInteger(parts.month) ||
        !Number.isInteger(parts.day) ||
        parts.year < 1 ||
        parts.month < 1 ||
        parts.month > 13 ||
        parts.day < 1 ||
        parts.day > this.daysInMonth(parts.year, parts.month)
      ) {
        return null;
      }
      const delta = daysToYearStart(parts.year) + (parts.month - 1) * 30 + (parts.day - 1);
      const date = new Date(ANCHOR_UTC + delta * DAY_MS);
      const year = date.getUTCFullYear();
      if (year < 1 || year > 9999) {
        return null; // outside the ISO/BCL/SQL-compatible window
      }
      return ɵtmToIsoDate(year, date.getUTCMonth() + 1, date.getUTCDate());
    },
    monthsInYear(): number {
      return 13;
    },
    daysInMonth(year: number, month: number): number {
      return month < 13 ? 30 : isLeap(year) ? 6 : 5;
    },
    today(): string {
      const now = new Date();
      return ɵtmToIsoDate(now.getFullYear(), now.getMonth() + 1, now.getDate());
    },
  };
  return singleton;
}
