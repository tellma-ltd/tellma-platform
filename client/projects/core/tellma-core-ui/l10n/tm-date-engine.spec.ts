// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_PARSE_ERROR } from '@tellma/core-ui/contracts';

import { tmDatePlaceholder, tmFormatDate, tmParseDate } from './tm-date-engine';
import { tmGregorianCalendar } from './tm-calendar';

const TODAY = '2026-07-15';

describe('tmFormatDate', () => {
  it('formats per locale in the three fixed styles', () => {
    expect(tmFormatDate('2026-03-05', 'en-US')).toBe('3/5/2026');
    expect(tmFormatDate('2026-03-05', 'en-GB')).toBe('05/03/2026');
    expect(tmFormatDate('2026-03-05', 'en-US', { dateStyle: 'medium' })).toBe('Mar 5, 2026');
    expect(tmFormatDate('2026-03-05', 'en-US', { dateStyle: 'long' })).toBe('March 5, 2026');
    expect(tmFormatDate('2026-03-05', 'de')).toBe('5.3.2026');
    expect(tmFormatDate('2026-03-05', 'de', { dateStyle: 'medium' })).toBe('5. März 2026');
  });

  it('renders localized month names and digits from Intl at runtime', () => {
    expect(tmFormatDate('2026-03-05', 'ar-SA', { dateStyle: 'long' })).toContain('مارس');
    expect(tmFormatDate('2026-03-05', 'fr', { dateStyle: 'long' })).toContain('mars');
  });

  it("''/null/undefined format as '' and a malformed input is returned as-is", () => {
    expect(tmFormatDate('', 'en')).toBe('');
    expect(tmFormatDate(null, 'en')).toBe('');
    expect(tmFormatDate(undefined, 'en')).toBe('');
    expect(tmFormatDate('garbage', 'en')).toBe('garbage');
  });

  it('formats years below 100 without the Date.UTC 1900-mapping trap', () => {
    expect(tmFormatDate('0044-03-15', 'en-US')).toBe('3/15/44');
  });
});

describe('tmParseDate', () => {
  const parse = (text: string, locale: string) => tmParseDate(text, locale, { today: TODAY });

  it('accepts the ISO shape in any locale (the fast path)', () => {
    for (const locale of ['en-US', 'en-GB', 'de', 'ar-SA']) {
      expect(parse('2026-03-05', locale)).toBe('2026-03-05');
    }
    expect(parse('2026-02-31', 'en')).toBe(TM_PARSE_ERROR); // not a real date
    expect(parse('0000-01-01', 'en')).toBe(TM_PARSE_ERROR); // below the floor
  });

  it('reads numeric segments in the locale field order', () => {
    expect(parse('5/3/2026', 'en-GB')).toBe('2026-03-05'); // day-month-year
    expect(parse('5/3/2026', 'ar-SA')).toBe('2026-03-05'); // day-month-year
    expect(parse('5/3/2026', 'en-US')).toBe('2026-05-03'); // month-day-year
  });

  it('is forgiving on separators', () => {
    expect(parse('5.3.2026', 'de')).toBe('2026-03-05');
    expect(parse('5-3-2026', 'en-GB')).toBe('2026-03-05');
    expect(parse('5 3 2026', 'en-GB')).toBe('2026-03-05');
  });

  it('accepts month names, long and short, case/diacritic-insensitively', () => {
    expect(parse('5 mar 2026', 'en-US')).toBe('2026-03-05');
    expect(parse('MARCH 5, 2026', 'en-US')).toBe('2026-03-05');
    expect(parse('5 März 2026', 'de')).toBe('2026-03-05');
    expect(parse('5 marz 2026', 'de')).toBe('2026-03-05'); // diacritic-folded
    expect(parse('5 مارس 2026', 'ar-SA')).toBe('2026-03-05');
    expect(parse('5 xyz 2026', 'en-US')).toBe(TM_PARSE_ERROR);
  });

  it('completes omitted trailing segments from today', () => {
    // One segment: that day of the current month.
    expect(parse('3', 'en-US')).toBe('2026-07-03');
    // Two segments: day and month in the locale's field order, current year.
    expect(parse('3/15', 'en-US')).toBe('2026-03-15'); // month-day
    expect(parse('15/3', 'en-GB')).toBe('2026-03-15'); // day-month
  });

  it('parses Arabic-Indic digits under an Arabic locale', () => {
    expect(parse('٥/٣/٢٠٢٦', 'ar-SA')).toBe('2026-03-05');
  });

  it('resolves two-digit years in the sliding [today − 80, today + 19] window', () => {
    expect(parse('5/3/26', 'en-GB')).toBe('2026-03-05');
    expect(parse('5/3/45', 'en-GB')).toBe('2045-03-05'); // 2045 ≤ 2026+19
    expect(parse('5/3/46', 'en-GB')).toBe('1946-03-05'); // 2046 > 2026+19 → last century
    expect(parse('5/3/99', 'en-GB')).toBe('1999-03-05');
    // Three or four digits are literal years, never pivoted.
    expect(parse('5/3/0999', 'en-GB')).toBe('0999-03-05');
  });

  it('rejects out-of-range segments — never reordering to force a match', () => {
    expect(parse('14/25/2026', 'en-US')).toBe(TM_PARSE_ERROR); // month 14 (even though day 14 / month … would fit swapped)
    expect(parse('31/2/2026', 'en-GB')).toBe(TM_PARSE_ERROR); // Feb 31
    expect(parse('0/1/2026', 'en-GB')).toBe(TM_PARSE_ERROR);
  });

  it('rejects extra segments, multiple names, and junk', () => {
    expect(parse('1/2/3/4', 'en')).toBe(TM_PARSE_ERROR);
    expect(parse('mar apr', 'en')).toBe(TM_PARSE_ERROR);
    expect(parse('hello', 'en')).toBe(TM_PARSE_ERROR);
    expect(parse('12345', 'en')).toBe(TM_PARSE_ERROR); // 5-digit segment
  });

  it('parses empty text as null', () => {
    expect(parse('', 'en')).toBeNull();
    expect(parse('   ', 'en')).toBeNull();
  });

  it('defaults today from the calendar when the option is absent', () => {
    const iso = tmParseDate('3', 'en-US');
    const now = new Date();
    const expected = `${String(now.getFullYear()).padStart(4, '0')}-${String(
      now.getMonth() + 1,
    ).padStart(2, '0')}-03`;
    expect(iso).toBe(expected);
  });
});

describe('tmDatePlaceholder', () => {
  it("reflects the locale's numeric field order", () => {
    expect(tmDatePlaceholder('en-US')).toBe('mm/dd/yyyy');
    expect(tmDatePlaceholder('en-GB')).toBe('dd/mm/yyyy');
    expect(tmDatePlaceholder('de')).toBe('dd.mm.yyyy');
  });

  it('accepts an explicit calendar', () => {
    expect(tmDatePlaceholder('en-GB', { calendar: tmGregorianCalendar() })).toBe('dd/mm/yyyy');
  });
});
