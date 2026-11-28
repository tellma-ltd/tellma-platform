// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_BIDI_CONTROLS } from './tm-bidi';
import { tmFormatDate, ɵtmFormatWithoutEra, ɵtmPlainText } from './tm-date-engine';
import { tmGregorianCalendar, type TmCalendar } from './tm-calendar';
import { tmFormatDateTime } from './tm-instant';
import dayFirst from './day-first-vectors.json';

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
});

describe('day-first languages', () => {
  // Oromo and Tigrinya write numeric dates day-first, as the server's
  // culture data does, whatever the browser's CLDR says.
  it("writes each vector's numeric date as the server writes its short date", () => {
    for (const row of dayFirst.rows) {
      expect(tmFormatDate(row.date, row.locale), row.locale).toBe(row.expected);
    }
  });

  it("keeps the order on another language's locale that names a day-first language, as a browser that lacks it gets", () => {
    // A distribution whose default language is US English: the fallback is month-first on its own.
    expect(tmFormatDate('2026-03-05', 'en-US')).toBe('3/5/2026');
    expect(tmFormatDate('2026-03-05', 'en-US-x-om')).toBe('05/03/2026');
    expect(tmFormatDate('2026-11-22', 'en-US-u-nu-latn-x-ti')).toBe('22/11/2026');
    expect(tmFormatDateTime('2026-03-05T07:42:00Z', 'en-US-x-om', { timeZone: 'UTC' })).toMatch(/^05\/03\/2026\D/);
    // A private-use subtag naming another language changes nothing.
    expect(tmFormatDate('2026-03-05', 'en-US-x-ar')).toBe('3/5/2026');
  });

  it('writes the date of a numeric date-time day-first, and leaves the named styles alone', () => {
    expect(tmFormatDateTime('2026-03-05T07:42:00Z', 'om-ET', { timeZone: 'UTC' })).toMatch(/^05\/03\/2026\D/);
    expect(tmFormatDate('2026-03-05', 'om-ET', { dateStyle: 'long' })).toBe(
      new Intl.DateTimeFormat('om-ET', { year: 'numeric', month: 'long', day: 'numeric', timeZone: 'UTC' }).format(Date.UTC(2026, 2, 5, 12)),
    );
  });
});
