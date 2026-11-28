// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  tmFormatDate,
  tmFormatDateTime,
  tmFormatMonthName,
  tmFormatRelativeTime,
} from '@tellma/core-ui/l10n';

import { tmEthiopicCalendar } from './tm-calendar-ethiopic';

describe('tmEthiopicCalendar', () => {
  const calendar = tmEthiopicCalendar();

  it('carries the Intl identifier and 13-month years', () => {
    expect(calendar.id).toBe('ethiopic');
    expect(calendar.monthsInYear(2016)).toBe(13);
  });

  it('matches known dates in Amete Mihret numbering', () => {
    // Ethiopian New Year: 1 Meskerem 2016 AM — 12 September 2023.
    expect(calendar.toParts('2023-09-12')).toEqual({ year: 2016, month: 1, day: 1 });
    expect(calendar.fromParts({ year: 2016, month: 1, day: 1 })).toBe('2023-09-12');
  });

  it('Pagume is a real 13th month: 5 days, 6 in leap years', () => {
    expect(calendar.daysInMonth(2016, 13)).toBe(5);
    expect(calendar.daysInMonth(2015, 13)).toBe(6); // leap
    expect(calendar.fromParts({ year: 2015, month: 13, day: 6 })).not.toBeNull();
    expect(calendar.fromParts({ year: 2016, month: 13, day: 6 })).toBeNull();
  });

  it('round-trips across sampled dates, Pagume included', () => {
    for (const iso of ['1950-01-15', '2023-09-10', '2026-07-28', '2090-12-31']) {
      expect(calendar.fromParts(calendar.toParts(iso))).toBe(iso);
    }
  });

  it('month names come from Intl in the active UI language', () => {
    const newYear = calendar.fromParts({ year: 2016, month: 1, day: 1 })!;
    expect(tmFormatDate(newYear, 'am-ET', { calendar, dateStyle: 'long' })).toContain('መስከረም');
    expect(tmFormatDate(newYear, 'en-US', { calendar, dateStyle: 'long' })).toContain('Meskerem');
  });

  it('names all thirteen months, and writes a number past them', () => {
    expect(tmFormatMonthName(1, 'en-GB', { calendar })).toBe('Meskerem');
    expect(tmFormatMonthName(13, 'en-GB', { calendar })).toBe('Pagumen');
    expect(tmFormatMonthName(1, 'am-ET', { calendar })).toBe('መስከረም');
    expect(tmFormatMonthName(14, 'en-GB', { calendar })).toBe('14');
    expect(tmFormatMonthName(0, 'en-GB', { calendar })).toBe('0');
  });

  it('writes the period styles in its own months and years', () => {
    // 8 September 2026 is 3 Pagume 2018, in the thirteenth month.
    expect(calendar.toParts('2026-09-08')).toEqual({ year: 2018, month: 13, day: 3 });
    expect(tmFormatDate('2026-09-08', 'en-GB', { calendar, dateStyle: 'month' })).toBe(
      'Pagumen 2018',
    );
    expect(tmFormatDate('2026-09-08', 'en-GB', { calendar, dateStyle: 'year' })).toBe('2018');
    expect(tmFormatDate('2026-09-08', 'en-GB', { calendar, dateStyle: 'weekday' })).toBe(
      'Tuesday, 3 Pagumen 2018',
    );
  });

  it('counts three months to a quarter, the thirteenth month falling in the fourth', () => {
    const quarterOf = (parts: { year: number; month: number; day: number }): string =>
      tmFormatDate(calendar.fromParts(parts), 'en-GB', { calendar, dateStyle: 'quarter' });
    expect(quarterOf({ year: 2018, month: 1, day: 1 })).toBe('Q1 2018');
    expect(quarterOf({ year: 2018, month: 3, day: 30 })).toBe('Q1 2018');
    expect(quarterOf({ year: 2018, month: 4, day: 1 })).toBe('Q2 2018');
    expect(quarterOf({ year: 2018, month: 10, day: 1 })).toBe('Q4 2018');
    expect(quarterOf({ year: 2018, month: 12, day: 30 })).toBe('Q4 2018');
    expect(quarterOf({ year: 2018, month: 13, day: 5 })).toBe('Q4 2018');
    expect(quarterOf({ year: 2019, month: 1, day: 1 })).toBe('Q1 2019');
  });

  it('dates an instant in the display zone, and a distant one by its date', () => {
    // 20 October 2026, 10:42 in Addis Ababa, is 10 Tekemt 2019.
    const at = '2026-10-20T07:42:00Z';
    const timeZone = 'Africa/Addis_Ababa';
    expect(tmFormatDateTime(at, 'en-GB', { timeZone, calendar })).toBe('10/02/2019, 10:42');
    expect(tmFormatDateTime(at, 'en-GB', { timeZone, calendar, timeStyle: 'none' })).toBe(
      '10/02/2019',
    );
    expect(
      tmFormatRelativeTime(at, 'en-GB', { now: Date.UTC(2026, 11, 1), timeZone, calendar }),
    ).toBe('10 Tekemt 2019');
  });

  it('agrees with Intl.DateTimeFormat numeric fields over sampled dates (the drift gate)', () => {
    const formatter = new Intl.DateTimeFormat('en-u-ca-ethiopic-nu-latn', {
      year: 'numeric',
      month: 'numeric',
      day: 'numeric',
      timeZone: 'UTC',
    });
    const check = (time: number): void => {
      const date = new Date(time);
      const iso = `${String(date.getUTCFullYear()).padStart(4, '0')}-${String(
        date.getUTCMonth() + 1,
      ).padStart(2, '0')}-${String(date.getUTCDate()).padStart(2, '0')}`;
      const parts = calendar.toParts(iso);
      const intl = Object.fromEntries(
        formatter
          .formatToParts(date)
          .filter((part) => ['year', 'month', 'day'].includes(part.type))
          .map((part) => [part.type, Number(part.value)]),
      );
      expect(parts, iso).toEqual({ year: intl['year'], month: intl['month'], day: intl['day'] });
    };
    const day = 24 * 3600 * 1000;
    for (let time = Date.UTC(1900, 0, 1, 12); time < Date.UTC(2100, 0, 1, 12); time += 97 * day) {
      check(time);
    }
    // The dense year-boundary sweep — the stretch where the upstream
    // @internationalized/date implementation was one day off in every
    // non-leap year (the bug that moved this calendar onto its own
    // arithmetic).
    for (let year = 1900; year < 2100; year++) {
      for (let d = 5; d <= 15; d++) {
        check(Date.UTC(year, 8, d, 12));
      }
    }
  });
});
