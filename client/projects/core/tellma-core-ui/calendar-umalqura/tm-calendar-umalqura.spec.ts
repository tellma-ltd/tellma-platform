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

  it('formatted output round-trips through the parser, era literal included', () => {
    // en-US numeric Umm al-Qura output carries an "AH" era literal — the
    // parser must tolerate it or a mere reformat would null the model.
    for (const locale of ['en-US', 'en-GB', 'ar-SA']) {
      const formatted = tmFormatDate('2026-03-05', locale, { calendar });
      expect(tmParseDate(formatted, locale, { calendar }), `${locale}: ${formatted}`).toBe(
        '2026-03-05',
      );
    }
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
    // The sweep runs into the LAST year of the documented window: the
    // final sample is 2173-10-23 = 15/11/1599 AH. It deliberately stops
    // short of AH 1600, where the upstream table hand-off is
    // discontinuous (a whole year off by one day) — keeping that year out
    // is exactly what the documented window is for.
    const start = Date.UTC(1900, 0, 1, 12);
    const end = Date.UTC(2173, 11, 7, 12);
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

  it('round-trips the last year of the documented window (AH 1599)', () => {
    // AH 1599 is the guarantee's edge; AH 1600 is known-broken upstream
    // (toParts/fromParts disagree by a day for the whole year), which is
    // why the documented window stops at 1599.
    for (const day of [1, 15, 29]) {
      for (const month of [1, 6, 12]) {
        const iso = calendar.fromParts({ year: 1599, month, day });
        expect(iso, `1599-${month}-${day}`).not.toBeNull();
        expect(calendar.toParts(iso!), iso!).toEqual({ year: 1599, month, day });
      }
    }
  });

  it('malformed upstream parts beyond the window still satisfy the parts contract', () => {
    // Inside the discontinuity the values may be WRONG (documented), but
    // they must stay integers — a fractional or null day would strand the
    // popup's roving focus.
    for (const iso of ['2173-12-07', '2174-06-15', '2320-01-01']) {
      const parts = calendar.toParts(iso);
      expect(Number.isInteger(parts.year), iso).toBe(true);
      expect(Number.isInteger(parts.month), iso).toBe(true);
      expect(Number.isInteger(parts.day), iso).toBe(true);
      expect(parts.day, iso).toBeGreaterThanOrEqual(1);
    }
  });
});
