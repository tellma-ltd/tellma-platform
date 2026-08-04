// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmEthiopicCalendar } from '@tellma/core-ui/calendar-ethiopic';
import { tmUmalquraCalendar } from '@tellma/core-ui/calendar-umalqura';
import { tmFormatDate, tmGregorianCalendar, tmParseDate } from '@tellma/core-ui/l10n';

/**
 * The cross-calendar format→parse round-trip sweep — the identity the
 * date picker's canonicalization paths rely on. Every (locale × calendar
 * × style) cell must round-trip exactly: CJK glues unit suffixes to the
 * numbers, fa embeds a ZWJ in the Umm al-Qura era, th abbreviates it with
 * dots, es/fr/vi/de interleave particles and multi-word or dotted month
 * names, he glues one-letter prepositions onto hyphenated month names —
 * all previously fatal to a locale/calendar switch or popup selection.
 *
 * The same root-level position also hosts the parser behaviours that are
 * only observable OUTSIDE the Gregorian calendar — trailing completion
 * and the two-digit-year pivot, both of which happen in the display
 * calendar's own numbering.
 *
 * (Root-level spec: it spans the l10n engine AND both calendar packs,
 * which the l10n entry point's dependency boundary keeps apart.)
 */

const TODAY = '2026-07-15';

const LOCALES = [
  'en-US',
  'en-GB',
  'de',
  'fr',
  'es',
  'ar-SA',
  'fa',
  'th',
  'tr',
  'he',
  'hi',
  'vi',
  'ja',
  'zh-CN',
  'ko',
  // Indic scripts: their vowel signs are combining MARKS, so a
  // diacritic-insensitive fold collapses distinct month names onto one
  // key (Bengali জুন/জানু) — every month must be swept, not a sample.
  'bn',
  'ta',
  'te',
  'kn',
  'mr',
  'pa',
  'ru',
  'pl',
  'cs',
  'id',
];
const STYLES = ['numeric', 'medium', 'long'] as const;
const SAMPLES = ['1985-11-20', '2026-07-27', '2043-02-28'];

describe('format→parse round-trip across locales, calendars, and styles', () => {
  for (const calendar of [tmGregorianCalendar(), tmUmalquraCalendar(), tmEthiopicCalendar()]) {
    it(`round-trips every locale × style under ${calendar.id}`, () => {
      const failures: string[] = [];
      for (const locale of LOCALES) {
        for (const style of STYLES) {
          for (const iso of SAMPLES) {
            const text = tmFormatDate(iso, locale, { calendar, dateStyle: style });
            const back = tmParseDate(text, locale, { calendar, today: TODAY });
            if (back !== iso) {
              failures.push(`${locale} ${style} ${iso}: '${text}' → ${String(back)}`);
            }
          }
        }
      }
      expect(failures, failures.join('\n')).toEqual([]);
    });

    it(`round-trips EVERY month name under ${calendar.id}`, () => {
      // Sampled dates can't see a month-name collision (three samples
      // touch three months); a name table is only sound if every month
      // of the year round-trips in every locale and style.
      const year = calendar.toParts(TODAY).year;
      const failures: string[] = [];
      for (const locale of LOCALES) {
        for (const style of STYLES) {
          for (let month = 1; month <= calendar.monthsInYear(year); month += 1) {
            const iso = calendar.fromParts({ year, month, day: 3 });
            if (iso === null) {
              continue;
            }
            const text = tmFormatDate(iso, locale, { calendar, dateStyle: style });
            const back = tmParseDate(text, locale, { calendar, today: TODAY });
            if (back !== iso) {
              failures.push(`${locale} ${style} month ${month}: '${text}' → ${String(back)}`);
            }
          }
        }
      }
      expect(failures, failures.join('\n')).toEqual([]);
    });
  }
});

describe('completion and the two-digit-year pivot happen in the display calendar', () => {
  // TODAY is 1 Safar 1448 AH and 8 Hamle 2018 AM — deliberately a date
  // whose in-calendar month and year differ from the Gregorian ones, so a
  // completion or a pivot that leaked back to Gregorian cannot pass.
  const umalqura = tmUmalquraCalendar();
  const ethiopic = tmEthiopicCalendar();

  it('completes an omitted month and year from today IN the display calendar', () => {
    const hijri = tmParseDate('3', 'en-US', { calendar: umalqura, today: TODAY });
    expect(umalqura.toParts(hijri as string)).toEqual({ year: 1448, month: 2, day: 3 });
    expect(hijri).toBe('2026-07-17'); // NOT the Gregorian 2026-07-03

    const amete = tmParseDate('3', 'en-US', { calendar: ethiopic, today: TODAY });
    expect(ethiopic.toParts(amete as string)).toEqual({ year: 2018, month: 11, day: 3 });
    expect(amete).toBe('2026-07-10');
  });

  it('completes an omitted year from today IN the display calendar', () => {
    // Two segments are day and month in the locale's field order; only
    // the year completes — and it completes to the in-calendar year.
    const hijri = tmParseDate('5/3', 'en-GB', { calendar: umalqura, today: TODAY });
    expect(umalqura.toParts(hijri as string)).toEqual({ year: 1448, month: 3, day: 5 });

    const amete = tmParseDate('5/3', 'en-GB', { calendar: ethiopic, today: TODAY });
    expect(ethiopic.toParts(amete as string)).toEqual({ year: 2018, month: 3, day: 5 });
  });

  it("pivots a two-digit year inside the display calendar's own numbering", () => {
    // Hijri: the window is [1368, 1467], so '48' is 1448 AH — a Gregorian
    // pivot would have read 2048.
    expect(
      umalqura.toParts(tmParseDate('3/2/48', 'en-GB', {
        calendar: umalqura,
        today: TODAY,
      }) as string),
    ).toEqual({ year: 1448, month: 2, day: 3 });
    expect(
      umalqura.toParts(tmParseDate('3/2/26', 'en-GB', {
        calendar: umalqura,
        today: TODAY,
      }) as string),
    ).toEqual({ year: 1426, month: 2, day: 3 });

    // Ethiopic: the window is [1938, 2037], so '48' is 1948 AM and '18'
    // is 2018 AM — the same two digits land a century apart per calendar.
    expect(
      ethiopic.toParts(tmParseDate('3/2/48', 'en-GB', {
        calendar: ethiopic,
        today: TODAY,
      }) as string),
    ).toEqual({ year: 1948, month: 2, day: 3 });
    expect(
      ethiopic.toParts(tmParseDate('3/2/18', 'en-GB', {
        calendar: ethiopic,
        today: TODAY,
      }) as string),
    ).toEqual({ year: 2018, month: 2, day: 3 });
  });
});
