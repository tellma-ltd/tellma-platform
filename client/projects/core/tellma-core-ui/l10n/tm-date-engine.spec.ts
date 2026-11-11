// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_PARSE_ERROR } from '@tellma/core-ui/contracts';

import { TM_BIDI_CONTROLS } from './tm-bidi';
import {
  tmDatePlaceholder,
  tmFormatDate,
  tmParseDate,
  ɵtmFormatWithoutEra,
  ɵtmPlainText,
} from './tm-date-engine';
import { tmGregorianCalendar, type TmCalendar } from './tm-calendar';

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

  it('writes the weekday style: the weekday before the medium date', () => {
    expect(tmFormatDate('2026-11-02', 'en-GB', { dateStyle: 'weekday' })).toBe(
      'Monday, 2 Nov 2026',
    );
    expect(tmFormatDate('2026-11-02', 'en-US', { dateStyle: 'weekday' })).toBe(
      'Monday, Nov 2, 2026',
    );
    expect(tmFormatDate('2026-11-02', 'de', { dateStyle: 'weekday' })).toBe(
      'Montag, 2. Nov. 2026',
    );
  });

  it('writes the month and year period styles', () => {
    expect(tmFormatDate('2026-11-02', 'en-GB', { dateStyle: 'month' })).toBe('Nov 2026');
    expect(tmFormatDate('2026-11-30', 'en-GB', { dateStyle: 'month' })).toBe('Nov 2026');
    expect(tmFormatDate('2026-11-02', 'de', { dateStyle: 'month' })).toBe('Nov. 2026');
    expect(tmFormatDate('2026-11-02', 'en-GB', { dateStyle: 'year' })).toBe('2026');
    expect(tmFormatDate('2026-11-02', 'ar-SA', { dateStyle: 'year' })).toBe('٢٠٢٦');
  });

  it('writes the quarter style through the pattern, three months to a quarter', () => {
    const quarter = (iso: string): string => tmFormatDate(iso, 'en-GB', { dateStyle: 'quarter' });
    expect(quarter('2026-01-01')).toBe('Q1 2026');
    expect(quarter('2026-03-31')).toBe('Q1 2026');
    expect(quarter('2026-04-01')).toBe('Q2 2026');
    expect(quarter('2026-06-30')).toBe('Q2 2026');
    expect(quarter('2026-07-01')).toBe('Q3 2026');
    expect(quarter('2026-09-30')).toBe('Q3 2026');
    expect(quarter('2026-10-01')).toBe('Q4 2026');
    expect(quarter('2026-12-31')).toBe('Q4 2026');
  });

  it("fills a caller's quarter pattern with the locale's digits, the year ungrouped", () => {
    expect(
      tmFormatDate('2026-11-02', 'ar-SA', {
        dateStyle: 'quarter',
        quarterPattern: 'الربع {quarter} {year}',
      }),
    ).toBe('الربع ٤ ٢٠٢٦');
    expect(
      tmFormatDate('2026-11-02', 'en', { dateStyle: 'quarter', quarterPattern: '{year}-Q{quarter}' }),
    ).toBe('2026-Q4');
    // A placeholder may repeat or be absent; other braces are literal text.
    expect(
      tmFormatDate('2026-11-02', 'en', { dateStyle: 'quarter', quarterPattern: 'Q{quarter}' }),
    ).toBe('Q4');
    expect(
      tmFormatDate('2026-11-02', 'en', {
        dateStyle: 'quarter',
        quarterPattern: '{quarter}/{quarter} {day}',
      }),
    ).toBe('4/4 {day}');
    // The pattern is read by the quarter style alone.
    expect(tmFormatDate('2026-11-02', 'en-GB', { quarterPattern: 'Q{quarter}' })).toBe('02/11/2026');
  });

  it("counts the quarter in the display calendar's months, capped at four", () => {
    // A thirteen-month calendar: the thirteenth month is in the fourth quarter.
    const thirteen: TmCalendar = {
      ...tmGregorianCalendar(),
      toParts: () => ({ year: 2018, month: 13, day: 3 }),
    };
    expect(tmFormatDate('2026-09-08', 'en-GB', { calendar: thirteen, dateStyle: 'quarter' })).toBe(
      'Q4 2018',
    );
  });

  it('returns a malformed input as-is in the period styles too', () => {
    for (const dateStyle of ['weekday', 'month', 'quarter', 'year'] as const) {
      expect(tmFormatDate('garbage', 'en', { dateStyle })).toBe('garbage');
      expect(tmFormatDate('', 'en', { dateStyle })).toBe('');
    }
  });

  it('carries no bidirectional control character', () => {
    expect(tmFormatDate('2026-11-02', 'ar-SA')).toBe('٢/١١/٢٠٢٦');
    for (const locale of ['ar', 'ar-SA', 'ar-EG', 'fa', 'he', 'ur']) {
      for (const dateStyle of [
        'numeric',
        'medium',
        'long',
        'weekday',
        'month',
        'quarter',
        'year',
      ] as const) {
        const text = tmFormatDate('2026-11-02', locale, { dateStyle });
        expect([...text.matchAll(TM_BIDI_CONTROLS)], `${locale} ${dateStyle}`).toEqual([]);
      }
    }
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

  it('matches all-caps Turkish month names across the dotless ı', () => {
    // Uppercasing 'Mayıs' yields 'MAYIS', and lowercasing that back
    // locale-insensitively yields 'mayis' — a different letter from the
    // table's 'ı', which has no decomposition to strip either.
    expect(parse('20 MAYIS 2026', 'tr')).toBe('2026-05-20');
    expect(parse('20 KASIM 2026', 'tr')).toBe('2026-11-20');
    expect(parse('20 ARALIK 2026', 'tr')).toBe('2026-12-20');
    // The fold must not blur two months into one another.
    expect(parse('20 MART 2026', 'tr')).toBe('2026-03-20');
  });

  it('treats parentheses as segment separators', () => {
    // Several locales bracket a grammatical suffix or the era onto a
    // field ('2026(e)ko … 20(a)' in Basque, '1448 (hijriy)' in Uzbek);
    // once split off, the bracketed word is shed like any other particle.
    expect(parse('5 (mar) 2026', 'en-US')).toBe('2026-03-05');
    expect(parse('5/3/2026 (AD)', 'en-GB')).toBe('2026-03-05');
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

describe('plain spaces', () => {
  const narrow = String.fromCodePoint(0x202f);

  /**
   * Runs `body` on an engine that writes U+202F NARROW NO-BREAK SPACE for
   * every space between the fields of a date, as an engine that passes
   * CLDR's own separators through does. The browsers the suite runs in
   * replace it themselves, so the rule is only testable against a stand-in.
   */
  function onNarrowSpaceEngine(body: () => void): void {
    const realFormatToParts = Intl.DateTimeFormat.prototype.formatToParts;
    const formatToParts = vi
      .spyOn(Intl.DateTimeFormat.prototype, 'formatToParts')
      .mockImplementation(function (this: Intl.DateTimeFormat, date) {
        return realFormatToParts
          .call(this, date)
          .map((part) =>
            part.type === 'literal' ? { ...part, value: part.value.replaceAll(' ', narrow) } : part,
          );
      });
    try {
      body();
    } finally {
      formatToParts.mockRestore();
    }
  }

  it('writes a plain space for a narrow no-break space, and no bidirectional control', () => {
    const mark = String.fromCodePoint(0x200f);
    expect(ɵtmPlainText(`10:42${narrow}AM`)).toBe('10:42 AM');
    expect(ɵtmPlainText(`5.03.2026${narrow}${mark}x`)).toBe('5.03.2026 x');
    // The no-break space of a number's grouping is not this function's.
    const noBreak = String.fromCodePoint(0xa0);
    expect(ɵtmPlainText(`1${noBreak}234`)).toBe(`1${noBreak}234`);
  });

  it('formats a date with plain spaces, whatever the engine writes', () => {
    // Bulgarian puts a space before its year marker in every style.
    const cases = (): string[] =>
      ['bg', 'mk', 'en-US', 'fr-FR', 'ar-SA'].flatMap((locale) =>
        (['numeric', 'medium', 'long', 'weekday', 'month', 'year'] as const).map((dateStyle) =>
          tmFormatDate('2026-03-05', locale, { dateStyle }),
        ),
      );
    const expected = cases();
    expect(expected.some((text) => text.includes(' '))).toBe(true);
    expect(expected.join('').includes(narrow)).toBe(false);
    onNarrowSpaceEngine(() => expect(cases()).toEqual(expected));
  });

  it('drops an era with plain spaces around what stays', () => {
    const formatter = new Intl.DateTimeFormat('en-US', {
      calendar: 'islamic-umalqura',
      dateStyle: 'long',
      timeZone: 'UTC',
    });
    const date = new Date(Date.UTC(2026, 2, 5));
    const expected = ɵtmFormatWithoutEra(formatter, date);
    expect(expected).toBe('Ramadan 16, 1447');
    onNarrowSpaceEngine(() => expect(ɵtmFormatWithoutEra(formatter, date)).toBe(expected));
  });

  it('hints with plain spaces, as the value it hints at', () => {
    const expected = tmDatePlaceholder('bg');
    expect(expected.includes(narrow)).toBe(false);
    onNarrowSpaceEngine(() => expect(tmDatePlaceholder('bg')).toBe(expected));
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

  it('carries no bidirectional control character, as the value it hints at', () => {
    expect(tmDatePlaceholder('ar-SA')).toBe('dd/mm/yyyy');
    for (const locale of ['ar', 'ar-SA', 'ar-EG', 'fa', 'he', 'ur']) {
      expect([...tmDatePlaceholder(locale).matchAll(TM_BIDI_CONTROLS)], locale).toEqual([]);
    }
  });
});
