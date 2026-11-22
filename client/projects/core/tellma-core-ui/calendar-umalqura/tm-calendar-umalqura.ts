// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ɵtmParseIsoDate, ɵtmToIsoDate, type TmCalendar, type TmCalendarParts } from '@tellma/core-ui/l10n';

// Self-contained Umm al-Qura arithmetic that mirrors the server's
// calendar day for day, since the server is the calendar authority: the
// month lengths of .NET's UmAlQuraCalendar table for AH 1318–1500, and
// the tabular Islamic calendar (.NET's HijriCalendar with adjustment 0,
// which is Intl's islamic-tbla) before and after it. Intl's own
// islamic-umalqura is never used for conversion: ICU's table differs from
// .NET's by a day in most months outside AH 1420–1450, so a date the
// server writes as 1 Muharram 1400 would read as 30 Dhu al-Hijjah 1399
// here. The parity vectors beside this file, generated from .NET and
// checked by both test suites, pin every month start of the table and of
// the tabular years around it.

/** Milliseconds in a day. */
const DAY_MS = 86_400_000;

/** The first year of the table. */
const TABLE_FIRST_YEAR = 1318;

/** The last year of the table. */
const TABLE_LAST_YEAR = 1500;

/**
 * The table: one 12-bit month-length mask per year from AH 1318 to 1500,
 * three hexadecimal digits a year; bit `m - 1` is set when month `m` has
 * 30 days (29 otherwise). Generated from .NET's UmAlQuraCalendar.
 */
const TABLE =
  '2ea6e9ed2ea4d4aa96536ab5daaba4b49a9352ba574b6ab55aad55d2aa564ae95d2ec6d56aa5554ab95b2ba575bb2764' +
  '7496552ab55bada6d4ec9d92d25a4d2ad56db6ab52aa5a4b4979372b6575d6ad52a9692d25d4ddada5d4da9d52aaa4d6' +
  '9b63747697526a554baab55aad5dd2da4d49a9552da5d55aad56aa69552ba574ae97656cb55aaaa554ad95d2da5d9db2b' +
  'a4b4aa552b5575b6abd2bc4b89a9552d5adb6a6d4dc9d92aa69562ae56d36ab55aaa94d49d95d2ba5b55aad55a9a92e26' +
  'e55dada6d46a554ba9754eaae5acba9d92b2564bcab55ab556d2ea5e4aa9552daad36c7596d269552da5b4ba9ba3b4b69' +
  'b52aa64b696d2ec6d9eb2d54d2aa564ae96dd6ab54b29a9352ba57536ab56aae93';

/** 1 Muharram 1318, the table's first day: 30 April 1900, as days since 1970-01-01. */
const TABLE_START = -25_448;

/** 1 Muharram AH 1 of the tabular calendar: 18 July 622 (proleptic Gregorian), as days since 1970-01-01. */
const TABULAR_EPOCH = -492_149;

/** A date in the calendar, as numbers. */
type Hijri = TmCalendarParts;

/** Days since 1970-01-01 of a proleptic Gregorian date, safe for years below 100. */
function dayOf(year: number, month: number, day: number): number {
  const date = new Date(Date.UTC(2000, month - 1, day));
  date.setUTCFullYear(year);
  return Math.round(date.getTime() / DAY_MS);
}

/** Whether a tabular year has 355 days: 11 of every 30, the 2nd, 5th, 7th, 10th … of the cycle. */
function isTabularLeap(year: number): boolean {
  return (((14 + 11 * year) % 30) + 30) % 30 < 11;
}

/** The day number of the first day of a tabular month. */
function tabularStart(year: number, month: number): number {
  return (
    TABULAR_EPOCH +
    (year - 1) * 354 +
    Math.floor((3 + 11 * year) / 30) +
    29 * (month - 1) +
    Math.floor(month / 2)
  );
}

/** The length of a tabular month: odd months 30 days, even 29, the 12th 30 in a leap year. */
function tabularDays(year: number, month: number): number {
  return month % 2 === 1 || (month === 12 && isTabularLeap(year)) ? 30 : 29;
}

/** The tabular date of a day number (proleptic before the epoch: years 0 and below). */
function tabularOf(day: number): Hijri {
  // The closed form lands on the year or one beside it; the loops settle it.
  let year = Math.floor((30 * (day - TABULAR_EPOCH) + 10_646) / 10_631);
  while (tabularStart(year + 1, 1) <= day) {
    year++;
  }
  while (tabularStart(year, 1) > day) {
    year--;
  }
  let month = Math.min(12, Math.floor((day - tabularStart(year, 1)) / 29.5) + 1);
  while (month > 1 && tabularStart(year, month) > day) {
    month--;
  }
  while (month < 12 && tabularStart(year, month + 1) <= day) {
    month++;
  }
  return { year, month, day: day - tabularStart(year, month) + 1 };
}

/** The month starts of the table, built on first use. */
interface Table {
  /** The day number of each table month's first day, plus the day after the table. */
  readonly starts: Int32Array;
  /** The table's last day. */
  readonly end: number;
  /** The tabular month the table's last day falls in, and how many of its days the table took. */
  readonly overlap: Hijri;
}

let table: Table | undefined;

/** The table, unpacked from its month-length masks on first use. */
function tableOf(): Table {
  if (table === undefined) {
    const months = (TABLE_LAST_YEAR - TABLE_FIRST_YEAR + 1) * 12;
    const starts = new Int32Array(months + 1);
    starts[0] = TABLE_START;
    for (let index = 0; index < months; index++) {
      const mask = parseInt(TABLE.slice(3 * Math.floor(index / 12), 3 * Math.floor(index / 12) + 3), 16);
      starts[index + 1] = starts[index] + 29 + ((mask >> index % 12) & 1);
    }
    const end = starts[months] - 1;
    // The tabular 1 Muharram 1501 falls on the table's last day: the table
    // wins it, so Muharram 1501 is that tabular month less the days taken.
    table = { starts, end, overlap: tabularOf(end) };
  }
  return table;
}

/** Whether a year and month are the tabular month the table overlaps. */
function isOverlap(year: number, month: number, overlap: Hijri): boolean {
  return year === overlap.year && month === overlap.month;
}

/** The Umm al-Qura date of a day number. */
function hijriOf(day: number): Hijri {
  const { starts, end, overlap } = tableOf();
  if (day >= TABLE_START && day <= end) {
    // The last month start on or before the day.
    let low = 0;
    let high = starts.length - 2;
    while (low < high) {
      const middle = (low + high + 1) >> 1;
      if (starts[middle] <= day) {
        low = middle;
      } else {
        high = middle - 1;
      }
    }
    return { year: TABLE_FIRST_YEAR + Math.floor(low / 12), month: (low % 12) + 1, day: day - starts[low] + 1 };
  }
  const tabular = tabularOf(day);
  return day > end && isOverlap(tabular.year, tabular.month, overlap)
    ? { ...tabular, day: tabular.day - overlap.day }
    : tabular;
}

/** The length of a month (0 for a month number outside 1–12). */
function daysIn(year: number, month: number): number {
  if (!Number.isInteger(year) || !Number.isInteger(month) || month < 1 || month > 12) {
    return 0;
  }
  if (year >= TABLE_FIRST_YEAR && year <= TABLE_LAST_YEAR) {
    const index = (year - TABLE_FIRST_YEAR) * 12 + month - 1;
    const { starts } = tableOf();
    return starts[index + 1] - starts[index];
  }
  const { overlap } = tableOf();
  return tabularDays(year, month) - (isOverlap(year, month, overlap) ? overlap.day : 0);
}

/** The day number of a valid year, month and day. */
function dayOfHijri(year: number, month: number, day: number): number {
  if (year >= TABLE_FIRST_YEAR && year <= TABLE_LAST_YEAR) {
    return tableOf().starts[(year - TABLE_FIRST_YEAR) * 12 + month - 1] + day - 1;
  }
  const { overlap } = tableOf();
  return tabularStart(year, month) + day - 1 + (isOverlap(year, month, overlap) ? overlap.day : 0);
}

let singleton: TmCalendar | undefined;

/**
 * The Umm al-Qura display calendar (the Saudi civil calendar). Register
 * it as the app-ambient calendar via
 * `provideTmCalendar(tmUmalquraCalendar())`, or pass it per instance; the
 * backing values stay ISO (proleptic Gregorian) either way.
 *
 * Its dates are the server's, day for day: the official Umm al-Qura table
 * from AH 1318 to 1500 (30 April 1900 to 16 November 2077), and the
 * tabular Islamic calendar before and after it, back to 1 Muharram AH 1
 * (18 July 622). The table wins the one day the two overlap, so Muharram
 * 1501 begins on 17 November 2077 and has 29 days; from Safar 1501 on,
 * every date is the tabular calendar's. Beyond the table every date is an
 * approximation of the observed calendar.
 *
 * Years are Anno Hegirae (AH); month and era names render from Intl in
 * the active UI language, while the year, month and day come from this
 * calendar. Dates before 1 Muharram AH 1 read with years of 0 and below,
 * outside the supported range.
 */
export function tmUmalquraCalendar(): TmCalendar {
  singleton ??= {
    id: 'islamic-umalqura',
    ɵownConversion: true,
    toParts(iso: string): TmCalendarParts {
      const parsed = ɵtmParseIsoDate(iso);
      const day = parsed === null ? NaN : dayOf(parsed.year, parsed.month, parsed.day);
      // A string naming no real date (2026-02-31) would roll over silently.
      if (parsed === null || ɵtmToIsoDate(...gregorianOf(day)) !== iso) {
        throw new Error(`TmCalendar(islamic-umalqura): '${iso}' is not a YYYY-MM-DD date`);
      }
      return hijriOf(day);
    },
    fromParts(parts: TmCalendarParts): string | null {
      if (
        !Number.isInteger(parts.year) ||
        parts.year < 1 ||
        !Number.isInteger(parts.day) ||
        parts.day < 1 ||
        parts.day > daysIn(parts.year, parts.month)
      ) {
        return null;
      }
      const [year, month, day] = gregorianOf(dayOfHijri(parts.year, parts.month, parts.day));
      if (year < 1 || year > 9999) {
        return null; // outside the ISO/BCL/SQL-compatible window
      }
      return ɵtmToIsoDate(year, month, day);
    },
    monthsInYear(): number {
      return 12;
    },
    daysInMonth(year: number, month: number): number {
      return daysIn(year, month);
    },
    today(): string {
      const now = new Date();
      return ɵtmToIsoDate(now.getFullYear(), now.getMonth() + 1, now.getDate());
    },
  };
  return singleton;
}

/** The proleptic Gregorian year, month and day of a day number. */
function gregorianOf(day: number): [number, number, number] {
  const date = new Date(day * DAY_MS);
  return [date.getUTCFullYear(), date.getUTCMonth() + 1, date.getUTCDate()];
}
