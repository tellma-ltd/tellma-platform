// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  tmFormatDate,
  tmFormatDateTime,
  tmFormatMonthName,
  tmFormatRelativeTime,
  tmParseDate,
} from '@tellma/core-ui/l10n';

import { tmUmalquraCalendar } from './tm-calendar-umalqura';
// Generated from the server's calendar, whose own suite checks the same
// file: every month start of the table and of the tabular years around
// it, a few far years, and single days at the edges.
import vectors from './umalqura-vectors.json';

/** One month of the vectors: the year, the month, its first day (ISO) and its length. */
type MonthVector = [number, number, string, number];

/** One day of the vectors: the ISO date and its parts, or null before the Islamic era. */
type DayVector = [string, [number, number, number] | null];

/** The ISO date `days` days after `iso`. */
function addDays(iso: string, days: number): string {
  const date = new Date(`${iso}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

/** The numeric year, month and day Intl gives a date in one of its calendars. */
function intlParts(calendarId: string, iso: string): { year: number; month: number; day: number } {
  const formatter = new Intl.DateTimeFormat(`en-u-ca-${calendarId}-nu-latn`, {
    year: 'numeric',
    month: 'numeric',
    day: 'numeric',
    timeZone: 'UTC',
  });
  const parts = Object.fromEntries(
    formatter
      .formatToParts(new Date(`${iso}T12:00:00Z`))
      .filter((part) => ['year', 'month', 'day'].includes(part.type))
      .map((part) => [part.type, Number(part.value)]),
  );
  return { year: parts['year'], month: parts['month'], day: parts['day'] };
}

describe('tmUmalquraCalendar', () => {
  const calendar = tmUmalquraCalendar();
  const months = vectors.months as MonthVector[];
  const days = vectors.days as DayVector[];

  it('carries the Intl identifier, converts by its own rule and has 12-month years', () => {
    expect(calendar.id).toBe('islamic-umalqura');
    expect(calendar.ɵownConversion).toBe(true);
    expect(calendar.monthsInYear(1447)).toBe(12);
  });

  it('matches published almanac dates', () => {
    // 1 Ramadan 1445 AH: 11 March 2024; 1 Muharram 1444 AH: 30 July 2022.
    expect(calendar.toParts('2024-03-11')).toEqual({ year: 1445, month: 9, day: 1 });
    expect(calendar.fromParts({ year: 1445, month: 9, day: 1 })).toBe('2024-03-11');
    expect(calendar.fromParts({ year: 1444, month: 1, day: 1 })).toBe('2022-07-30');
    // 1 Muharram 1400, the day of the Grand Mosque seizure: 20 November 1979.
    expect(calendar.toParts('1979-11-20')).toEqual({ year: 1400, month: 1, day: 1 });
  });

  it('starts every month on the day the server starts it, with the server length', () => {
    expect(months.length).toBeGreaterThan(2196);
    for (const [year, month, start, length] of months) {
      const label = `${year}-${month}`;
      expect(calendar.fromParts({ year, month, day: 1 }), label).toBe(start);
      expect(calendar.toParts(start), label).toEqual({ year, month, day: 1 });
      expect(calendar.daysInMonth(year, month), label).toBe(length);
      const last = addDays(start, length - 1);
      expect(calendar.toParts(last), label).toEqual({ year, month, day: length });
      expect(calendar.fromParts({ year, month, day: length }), label).toBe(last);
      expect(calendar.fromParts({ year, month, day: length + 1 }), label).toBeNull();
    }
  });

  it('dates the edge days as the server does', () => {
    for (const [iso, parts] of days) {
      if (parts === null) {
        // Before 1 Muharram AH 1: outside the supported range.
        expect(calendar.toParts(iso).year, iso).toBeLessThan(1);
      } else {
        const [year, month, day] = parts;
        expect(calendar.toParts(iso), iso).toEqual({ year, month, day });
        expect(calendar.fromParts({ year, month, day }), iso).toBe(iso);
      }
    }
  });

  it('gives Muharram 1501 the one day of 29 the table leaves it, then follows the tabular calendar', () => {
    expect(calendar.toParts('2077-11-16')).toEqual({ year: 1500, month: 12, day: 30 });
    expect(calendar.fromParts({ year: 1501, month: 1, day: 1 })).toBe('2077-11-17');
    expect(calendar.daysInMonth(1501, 1)).toBe(29);
    expect(calendar.toParts('2077-12-16')).toEqual({ year: 1501, month: 2, day: 1 });
    // From Safar 1501 on, and before AH 1318, every date is islamic-tbla's.
    for (const iso of ['2077-12-16', '2100-01-01', '2500-06-15', '9999-12-31', '1900-04-29', '1800-01-01', '0622-07-18']) {
      expect(calendar.toParts(iso), iso).toEqual(intlParts('islamic-tbla', iso));
    }
  });

  it('agrees with Intl inside AH 1420–1450, where ICU and the server share a table', () => {
    for (const [year, month, start, length] of months) {
      if (year >= 1420 && year <= 1450) {
        expect(intlParts('islamic-umalqura', start), `${year}-${month}`).toEqual({ year, month, day: 1 });
        expect(intlParts('islamic-umalqura', addDays(start, length - 1)), `${year}-${month}`).toEqual({
          year,
          month,
          day: length,
        });
      }
    }
  });

  it("formats the server's date where Intl's own calendar dates the day differently", () => {
    // ICU dates 20 November 1979 as 30 Dhu al-Hijjah 1399; the server and
    // this calendar as 1 Muharram 1400.
    expect(intlParts('islamic-umalqura', '1979-11-20')).toEqual({ year: 1399, month: 12, day: 30 });
    expect(tmFormatDate('1979-11-20', 'en-GB', { calendar, dateStyle: 'long' })).toBe(
      '1 Muharram 1400',
    );
    expect(tmFormatDate('1979-11-20', 'en-GB', { calendar })).toBe('01/01/1400');
    expect(tmFormatDate('1979-11-20', 'en-US', { calendar })).toBe('1/1/1400');
    expect(tmFormatDate('1979-11-20', 'ar-SA', { calendar })).toBe('١/١/١٤٠٠');
    expect(tmFormatDate('1979-11-20', 'en-GB', { calendar, dateStyle: 'weekday' })).toBe(
      'Tuesday, 1 Muh. 1400',
    );
    expect(tmFormatDate('1979-11-20', 'en-GB', { calendar, dateStyle: 'month' })).toBe('Muh. 1400');
    expect(
      tmFormatDateTime('1979-11-20T07:00:00Z', 'en-GB', { timeZone: 'Asia/Riyadh', calendar }),
    ).toBe('01/01/1400, 10:00');
    // And it reads its own text back to the same day.
    expect(tmParseDate('01/01/1400', 'en-GB', { calendar })).toBe('1979-11-20');
    expect(tmParseDate('1 Muharram 1400', 'en-GB', { calendar })).toBe('1979-11-20');
  });

  it('reports 29/30-day months and rejects parts naming no real date', () => {
    const length = calendar.daysInMonth(1447, 1);
    expect(length === 29 || length === 30).toBe(true);
    expect(calendar.daysInMonth(1447, 13)).toBe(0);
    expect(calendar.fromParts({ year: 1447, month: 13, day: 1 })).toBeNull();
    expect(calendar.fromParts({ year: 1447, month: 1, day: 31 })).toBeNull();
    expect(calendar.fromParts({ year: 0, month: 12, day: 1 })).toBeNull();
    expect(calendar.fromParts({ year: 1447, month: 1.5, day: 1 })).toBeNull();
    // The last ISO day is 3 Rabi' II 9666; the day after it is outside the window.
    expect(calendar.fromParts({ year: 9666, month: 4, day: 3 })).toBe('9999-12-31');
    expect(calendar.fromParts({ year: 9666, month: 4, day: 4 })).toBeNull();
    expect(() => calendar.toParts('2026-02-31')).toThrow();
    expect(() => calendar.toParts('20260101')).toThrow();
  });

  it('formatted output round-trips through the parser, era literal included', () => {
    // en-US numeric Umm al-Qura output carries an "AH" era literal: the
    // parser must tolerate it or a mere reformat would null the model.
    for (const locale of ['en-US', 'en-GB', 'ar-SA']) {
      for (const iso of ['2026-03-05', '1979-11-20', '2077-11-17']) {
        const formatted = tmFormatDate(iso, locale, { calendar });
        expect(tmParseDate(formatted, locale, { calendar }), `${locale}: ${formatted}`).toBe(iso);
      }
    }
  });

  it('drives the date engine: Hijri segments read in the display calendar', () => {
    // 15 Safar 1448 AH.
    const iso = tmParseDate('15/2/1448', 'ar-SA', { calendar });
    expect(typeof iso).toBe('string');
    expect(calendar.toParts(iso as string)).toEqual({ year: 1448, month: 2, day: 15 });
    // Month names come from Intl in the UI language.
    expect(tmFormatDate(iso as string, 'ar-SA', { calendar, dateStyle: 'long' })).toContain('صفر');
    expect(tmFormatDate(iso as string, 'en-US', { calendar, dateStyle: 'long' })).toContain('Safar');
  });

  it('names its twelve months, and writes a number past them', () => {
    expect(tmFormatMonthName(1, 'en-GB', { calendar })).toBe('Muharram');
    expect(tmFormatMonthName(9, 'en-GB', { calendar })).toBe('Ramadan');
    expect(tmFormatMonthName(9, 'ar-SA', { calendar })).toBe('رمضان');
    expect(tmFormatMonthName(13, 'en-GB', { calendar })).toBe('13');
  });

  it('writes the period styles in its own months and years, without the era', () => {
    // 5 March 2026 is 16 Ramadan 1447.
    expect(tmFormatDate('2026-03-05', 'en-GB', { calendar, dateStyle: 'month' })).toBe('Ram. 1447');
    expect(tmFormatDate('2026-03-05', 'en-GB', { calendar, dateStyle: 'quarter' })).toBe('Q3 1447');
    expect(tmFormatDate('2026-03-05', 'en-GB', { calendar, dateStyle: 'year' })).toBe('1447');
    expect(tmFormatDate('2026-03-05', 'en-GB', { calendar, dateStyle: 'weekday' })).toBe(
      'Thursday, 16 Ram. 1447',
    );
    expect(tmFormatDate('2026-03-05', 'en-US', { calendar, dateStyle: 'year' })).toBe('1447');
  });

  it('dates an instant in the display zone, the era dropped and its separator kept', () => {
    // 20 October 2026, 10:42 in Riyadh, is 9 Jumada I 1448.
    const at = '2026-10-20T07:42:00Z';
    const timeZone = 'Asia/Riyadh';
    expect(tmFormatDateTime(at, 'en-GB', { timeZone, calendar })).toBe('09/05/1448, 10:42');
    // en-US writes the era between the year and the time: dropping it
    // keeps the comma, never welding "1448" to "10:42".
    expect(tmFormatDateTime(at, 'en-US', { timeZone, calendar })).toBe('5/9/1448, 10:42 AM');
    expect(tmFormatDateTime(at, 'en-US', { timeZone, calendar, dateStyle: 'medium' })).toBe(
      'Jum. I 9, 1448, 10:42 AM',
    );
    expect(tmFormatDateTime(at, 'en-GB', { timeZone, calendar, timeStyle: 'none' })).toBe(
      '09/05/1448',
    );
    // The Hijri date turns at midnight in the zone, not in UTC.
    expect(
      tmFormatDateTime('2026-10-19T21:00:00Z', 'en-GB', { timeZone, calendar, timeStyle: 'none' }),
    ).toBe('09/05/1448');
    expect(
      tmFormatDateTime('2026-10-19T20:59:59Z', 'en-GB', { timeZone, calendar, timeStyle: 'none' }),
    ).toBe('08/05/1448');
    expect(
      tmFormatRelativeTime(at, 'en-GB', { now: Date.UTC(2026, 11, 1), timeZone, calendar }),
    ).toBe('9 Jum. I 1448');
  });
});
