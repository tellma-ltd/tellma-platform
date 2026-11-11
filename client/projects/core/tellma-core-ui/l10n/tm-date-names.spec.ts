// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_BIDI_CONTROLS } from './tm-bidi';
import { tmGregorianCalendar } from './tm-calendar';
import { tmFormatMonthName, tmFormatWeekdayName } from './tm-date-names';

describe('tmFormatMonthName', () => {
  it('names the twelve Gregorian months from their numbers', () => {
    expect(Array.from({ length: 12 }, (_, index) => tmFormatMonthName(index + 1, 'en-GB'))).toEqual(
      [
        'January',
        'February',
        'March',
        'April',
        'May',
        'June',
        'July',
        'August',
        'September',
        'October',
        'November',
        'December',
      ],
    );
  });

  it('names the month in the locale, in its standalone form', () => {
    expect(tmFormatMonthName(3, 'de')).toBe('März');
    expect(tmFormatMonthName(3, 'fr')).toBe('mars');
    expect(tmFormatMonthName(3, 'ar-SA')).toBe('مارس');
    // Russian's in-date form is the genitive "марта"; a lone name is not.
    expect(tmFormatMonthName(3, 'ru')).toBe('март');
  });

  it('defaults to the Gregorian calendar and accepts an explicit one', () => {
    expect(tmFormatMonthName(3, 'en-GB', {})).toBe('March');
    expect(tmFormatMonthName(3, 'en-GB', { calendar: tmGregorianCalendar() })).toBe('March');
  });

  it("writes a number that is no month of the calendar as a number, in the locale's digits", () => {
    expect(tmFormatMonthName(0, 'en-GB')).toBe('0');
    expect(tmFormatMonthName(13, 'en-GB')).toBe('13');
    expect(tmFormatMonthName(-1, 'en-GB')).toBe('-1');
    expect(tmFormatMonthName(2.5, 'en-GB')).toBe('2.5');
    expect(tmFormatMonthName(13, 'ar-SA')).toBe('١٣');
  });

  it('carries no bidirectional control character', () => {
    for (const locale of ['ar', 'ar-SA', 'fa', 'he', 'ur']) {
      for (let month = 0; month <= 13; month++) {
        const text = tmFormatMonthName(month, locale);
        expect([...text.matchAll(TM_BIDI_CONTROLS)], `${locale} ${month}`).toEqual([]);
      }
    }
  });
});

describe('tmFormatWeekdayName', () => {
  it('names the days from their ISO numbers: 1 is Monday, 7 is Sunday', () => {
    expect(Array.from({ length: 7 }, (_, index) => tmFormatWeekdayName(index + 1, 'en-GB'))).toEqual(
      ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'],
    );
  });

  it('names the day in the locale', () => {
    expect(tmFormatWeekdayName(7, 'de')).toBe('Sonntag');
    expect(tmFormatWeekdayName(7, 'ar-SA')).toBe('الأحد');
    expect(tmFormatWeekdayName(5, 'fr')).toBe('vendredi');
  });

  it('names the same days in a locale whose own calendar is not Gregorian', () => {
    // Thai defaults to the Buddhist calendar and Persian to the Persian
    // one; the week is the same seven days.
    expect(tmFormatWeekdayName(1, 'th')).toBe(
      new Intl.DateTimeFormat('th', { weekday: 'long', timeZone: 'UTC' }).format(
        Date.UTC(2024, 0, 1, 12),
      ),
    );
    expect(tmFormatWeekdayName(1, 'fa')).toBe('دوشنبه');
  });

  it("writes a number outside 1–7 as a number, in the locale's digits", () => {
    expect(tmFormatWeekdayName(0, 'en-GB')).toBe('0');
    expect(tmFormatWeekdayName(8, 'en-GB')).toBe('8');
    expect(tmFormatWeekdayName(1.5, 'en-GB')).toBe('1.5');
    expect(tmFormatWeekdayName(8, 'ar-SA')).toBe('٨');
  });

  it('carries no bidirectional control character', () => {
    for (const locale of ['ar', 'ar-SA', 'fa', 'he', 'ur']) {
      for (let weekday = 0; weekday <= 8; weekday++) {
        const text = tmFormatWeekdayName(weekday, locale);
        expect([...text.matchAll(TM_BIDI_CONTROLS)], `${locale} ${weekday}`).toEqual([]);
      }
    }
  });
});
