// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Month and weekday names from their numbers: a report shows the month 3
// as "March" and the weekday 7 as "Sunday". The names come from Intl/CLDR
// at runtime, read off fixed reference dates, so no name table ships and
// every calendar's months are named in every UI language.

import { tmGregorianCalendar, ɵtmParseIsoDate, type TmCalendar } from './tm-calendar';
import { ɵtmPlainText } from './tm-date-engine';
import { tmFormatNumber } from './tm-number-codec';

/**
 * The fixed reference date: month names are read off the calendar year
 * this ISO date falls in, so the same number names the same month on
 * every day the function runs.
 */
const REFERENCE_ISO = '2026-01-01';

/** The Monday of the fixed reference week (1 January 2024), at UTC noon. */
const REFERENCE_MONDAY = Date.UTC(2024, 0, 1, 12);

/** Standalone-name formatters by locale × calendar × field. */
const formatters = new Map<string, Intl.DateTimeFormat>();

/** The cached formatter writing one standalone long name: a month's or a weekday's. */
function nameFormatter(
  locale: string,
  calendarId: string,
  field: 'month' | 'weekday',
): Intl.DateTimeFormat {
  const key = `${locale}|${calendarId}|${field}`;
  let formatter = formatters.get(key);
  if (formatter === undefined) {
    // A lone field is its STANDALONE form (CLDR's LLLL and cccc): the
    // nominative "март", never the in-date genitive "марта".
    formatter = new Intl.DateTimeFormat(locale, {
      ...(field === 'month' ? { month: 'long' as const } : { weekday: 'long' as const }),
      calendar: calendarId,
      timeZone: 'UTC',
    });
    formatters.set(key, formatter);
  }
  return formatter;
}

/**
 * The name of a month of the display calendar, from its 1-based number:
 * the standalone long name in the locale — `3` is "March" in Gregorian,
 * `9` is "Ramadan" in Umm al-Qura, `13` is "Pagumen" in Ethiopic. A
 * number that is no month of the calendar (0, 13 in a twelve-month
 * calendar, 2.5) formats as a number, in the locale's digits.
 */
export function tmFormatMonthName(
  month: number,
  locale: string,
  options?: { readonly calendar?: TmCalendar },
): string {
  const calendar = options?.calendar ?? tmGregorianCalendar();
  const year = calendar.toParts(REFERENCE_ISO).year;
  const iso =
    Number.isInteger(month) && month >= 1 && month <= calendar.monthsInYear(year)
      ? calendar.fromParts({ year, month, day: 1 })
      : null;
  const parts = iso === null ? null : ɵtmParseIsoDate(iso);
  if (parts === null) {
    return tmFormatNumber(month, locale);
  }
  return ɵtmPlainText(
    nameFormatter(locale, calendar.id, 'month').format(
      Date.UTC(parts.year, parts.month - 1, parts.day, 12),
    ),
  );
}

/**
 * The name of a day of the week, from its ISO number — 1 is Monday and 7
 * is Sunday: the standalone long name in the locale ("Sunday", "الأحد").
 * A number outside 1–7 formats as a number, in the locale's digits.
 */
export function tmFormatWeekdayName(weekday: number, locale: string): string {
  if (!Number.isInteger(weekday) || weekday < 1 || weekday > 7) {
    return tmFormatNumber(weekday, locale);
  }
  return ɵtmPlainText(
    // The week is a Gregorian one in every calendar, so the name is read
    // in the Gregorian calendar whatever the locale's default is.
    nameFormatter(locale, 'gregory', 'weekday').format(
      REFERENCE_MONDAY + (weekday - 1) * 86_400_000,
    ),
  );
}
