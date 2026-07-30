// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import golden from './format-golden.json';

import { tmFormatNumber } from './tm-number-codec';
import { tmFormatDate, type TmDateStyle } from './tm-date-engine';
import { tmGregorianCalendar, type TmCalendar } from './tm-calendar';

/**
 * The committed cross-platform formatting golden, asserted row by row in
 * the browser's ICU. A failure here means an engine update moved CLDR
 * data (or a generation environment disagreed with the asserting one) —
 * regenerate via `pnpm run build && pnpm run golden:approve` and REVIEW
 * the diff like an API golden; the server-side implementation's tests
 * consume the same file verbatim.
 */
describe('format-golden.json', () => {
  const calendars = new Map<string, TmCalendar>([['gregory', tmGregorianCalendar()]]);

  it('covers both codecs with a meaningful row count', () => {
    expect(golden.number.length).toBeGreaterThanOrEqual(60);
    expect(golden.date.length).toBeGreaterThanOrEqual(40);
  });

  for (const row of golden.number) {
    it(`number ${row.locale} min=${row.minDecimals} max=${row.maxDecimals} percent=${row.percent} ${row.value}`, () => {
      expect(
        tmFormatNumber(row.value, row.locale, {
          minDecimals: row.minDecimals,
          maxDecimals: row.maxDecimals,
          percent: row.percent,
        }),
      ).toBe(row.expected);
    });
  }

  for (const row of golden.date) {
    it(`date ${row.locale} ${row.calendar} ${row.dateStyle} ${row.iso}`, () => {
      const calendar = calendars.get(row.calendar);
      if (calendar === undefined) {
        // Calendar entry points assert their own rows in their own suites.
        return;
      }
      expect(
        tmFormatDate(row.iso, row.locale, {
          calendar,
          dateStyle: row.dateStyle as TmDateStyle,
        }),
      ).toBe(row.expected);
    });
  }
});
