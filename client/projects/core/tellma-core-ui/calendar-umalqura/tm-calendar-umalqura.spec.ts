// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import golden from '../l10n/format-golden.json';
import { tmFormatDate, tmParseDate, type TmDateStyle } from '@tellma/core-ui/l10n';

import { tmUmalquraCalendar } from './tm-calendar-umalqura';

describe('tmUmalquraCalendar', () => {
  const calendar = tmUmalquraCalendar();

  it('carries the Intl identifier and 12-month years', () => {
    expect(calendar.id).toBe('islamic-umalqura');
    expect(calendar.monthsInYear(1447)).toBe(12);
  });

  it('matches known almanac dates inside the table window', () => {
    // 1 Ramadan 1445 AH — 11 March 2024 in the Umm al-Qura almanac.
    expect(calendar.toParts('2024-03-11')).toEqual({ year: 1445, month: 9, day: 1 });
    expect(calendar.fromParts({ year: 1445, month: 9, day: 1 })).toBe('2024-03-11');
    // 1 Muharram 1444 AH — 30 July 2022.
    expect(calendar.fromParts({ year: 1444, month: 1, day: 1 })).toBe('2022-07-30');
  });

  it('round-trips across sampled dates', () => {
    for (const iso of ['1900-06-15', '2026-07-28', '2074-01-01', '2170-12-31']) {
      expect(calendar.fromParts(calendar.toParts(iso))).toBe(iso);
    }
  });

  it('reports 29/30-day months and rejects parts naming no real date', () => {
    const days = calendar.daysInMonth(1447, 1);
    expect(days === 29 || days === 30).toBe(true);
    expect(calendar.fromParts({ year: 1447, month: 13, day: 1 })).toBeNull();
    expect(calendar.fromParts({ year: 1447, month: 1, day: 31 })).toBeNull();
  });

  it('drives the date engine: Hijri segments read in the display calendar', () => {
    // 15 Safar 1448 AH per the spec's own example.
    const iso = tmParseDate('15/2/1448', 'ar-SA', { calendar });
    expect(typeof iso).toBe('string');
    expect(calendar.toParts(iso as string)).toEqual({ year: 1448, month: 2, day: 15 });
    // Month names come from Intl in the UI language.
    expect(tmFormatDate(iso as string, 'ar-SA', { calendar, dateStyle: 'long' })).toContain('صفر');
    expect(tmFormatDate(iso as string, 'en-US', { calendar, dateStyle: 'long' })).toContain('Safar');
  });

  it('asserts its rows of the committed formatting golden', () => {
    const rows = golden.date.filter((row) => row.calendar === 'islamic-umalqura');
    expect(rows.length).toBeGreaterThanOrEqual(40);
    for (const row of rows) {
      expect(
        tmFormatDate(row.iso, row.locale, { calendar, dateStyle: row.dateStyle as TmDateStyle }),
        `${row.locale} ${row.dateStyle} ${row.iso}`,
      ).toBe(row.expected);
    }
  });

  it('agrees with Intl.DateTimeFormat numeric fields over sampled dates (the drift gate)', () => {
    const formatter = new Intl.DateTimeFormat('en-u-ca-islamic-umalqura-nu-latn', {
      year: 'numeric',
      month: 'numeric',
      day: 'numeric',
      timeZone: 'UTC',
    });
    const start = Date.UTC(1900, 0, 1, 12);
    const end = Date.UTC(2100, 0, 1, 12);
    for (let time = start; time < end; time += 97 * 24 * 3600 * 1000) {
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
    }
  });
});
