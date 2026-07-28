// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import golden from '../l10n/format-golden.json';
import { tmFormatDate, type TmDateStyle } from '@tellma/core-ui/l10n';

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

  it('asserts its rows of the committed formatting golden', () => {
    const rows = golden.date.filter((row) => row.calendar === 'ethiopic');
    expect(rows.length).toBeGreaterThanOrEqual(40);
    for (const row of rows) {
      expect(
        tmFormatDate(row.iso, row.locale, { calendar, dateStyle: row.dateStyle as TmDateStyle }),
        `${row.locale} ${row.dateStyle} ${row.iso}`,
      ).toBe(row.expected);
    }
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
