// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmEthiopicCalendar } from '@tellma/core-ui/calendar-ethiopic';
import { tmUmalquraCalendar } from '@tellma/core-ui/calendar-umalqura';
import {
  TM_BIDI_CONTROLS,
  tmFormatDate,
  tmFormatDateTime,
  tmFormatMonthName,
  tmFormatNumber,
  tmFormatRelativeTime,
  tmFormatTime,
  tmFormatWeekdayName,
  tmFormatZoneName,
  tmFormatZoneOffset,
  tmGregorianCalendar,
  ɵtmParseInstant,
  ɵtmShowsZoneLabel,
  type TmCalendar,
  type TmDateStyle,
  type TmTimeStyle,
} from '@tellma/core-ui/l10n';

import golden from './l10n/format-golden.json';

/**
 * The committed formatting golden, asserted row by row in the browser's
 * ICU: every array, in every calendar. A failure here means an engine
 * update moved CLDR or time-zone data (or the generating environment
 * disagreed with the asserting one) — regenerate via
 * `pnpm run build && pnpm run golden:approve` and REVIEW the diff like an
 * API golden.
 *
 * (Root-level spec: it spans the l10n engine AND both calendar packs,
 * which the l10n entry point's dependency boundary keeps apart.)
 */
describe('format-golden.json', () => {
  const calendars = new Map<string, TmCalendar>([
    ['gregory', tmGregorianCalendar()],
    ['islamic-umalqura', tmUmalquraCalendar()],
    ['ethiopic', tmEthiopicCalendar()],
  ]);

  /** The display calendar a row names. */
  function calendarOf(id: string): TmCalendar {
    const calendar = calendars.get(id);
    if (calendar === undefined) {
      throw new Error(`The golden names the calendar '${id}', which this suite does not load.`);
    }
    return calendar;
  }

  /** The epoch milliseconds of an instant the golden holds. */
  function millisOf(instant: string): number {
    const millis = ɵtmParseInstant(instant);
    if (millis === null) {
      throw new Error(`The golden holds '${instant}', which is not an instant.`);
    }
    return millis;
  }

  /**
   * Every row whose formatted text differs from the row's `expected`, as
   * readable lines — so one run reports every drifted row, not the first.
   */
  function drift<Row extends { readonly expected: string }>(
    rows: readonly Row[],
    format: (row: Row) => string,
  ): string[] {
    const lines: string[] = [];
    for (const row of rows) {
      const actual = format(row);
      if (actual !== row.expected) {
        lines.push(`${JSON.stringify(row)} formatted as ${JSON.stringify(actual)}`);
      }
    }
    return lines;
  }

  it('covers every format with a meaningful row count', () => {
    expect(golden.number.length).toBeGreaterThanOrEqual(100);
    expect(golden.date.length).toBeGreaterThanOrEqual(600);
    expect(golden.monthName.length).toBeGreaterThanOrEqual(150);
    expect(golden.weekdayName.length).toBeGreaterThanOrEqual(30);
    expect(golden.dateTime.length).toBeGreaterThanOrEqual(600);
    expect(golden.time.length).toBeGreaterThanOrEqual(200);
    expect(golden.relative.length).toBeGreaterThanOrEqual(500);
    expect(golden.zoneLabel.length).toBeGreaterThanOrEqual(40);
  });

  it('covers the three calendars, the four zones and the labelled rows', () => {
    for (const rows of [golden.date, golden.monthName, golden.dateTime]) {
      expect(new Set(rows.map((row) => row.calendar))).toEqual(new Set(calendars.keys()));
    }
    for (const rows of [golden.dateTime, golden.time]) {
      const zones = new Set(rows.map((row) => row.timeZone));
      for (const zone of ['UTC', 'Asia/Riyadh', 'Africa/Addis_Ababa', 'Asia/Beirut']) {
        expect(zones.has(zone), zone).toBe(true);
      }
      // The labelled rows: three locales in Riyadh and in Kolkata (+5:30).
      const labelled = rows.filter((row) => row.zoneLabel === true);
      for (const locale of ['en-GB', 'ar-SA', 'ar-SA-u-nu-latn']) {
        for (const zone of ['Asia/Riyadh', 'Asia/Kolkata']) {
          expect(
            labelled.some((row) => row.locale === locale && row.timeZone === zone),
            `${locale} ${zone}`,
          ).toBe(true);
        }
      }
    }
    expect(golden.date.some((row) => row.dateStyle === 'quarter')).toBe(true);
    expect(golden.number.some((row) => !row.grouping)).toBe(true);
  });

  it('holds no bidirectional control character in any expected text', () => {
    for (const [name, rows] of Object.entries(golden)) {
      if (!Array.isArray(rows)) {
        continue; // the file's leading comment
      }
      const marked = rows.filter((row) => [...row.expected.matchAll(TM_BIDI_CONTROLS)].length > 0);
      expect(marked, name).toEqual([]);
    }
  });

  it('number rows', () => {
    expect(
      drift(golden.number, (row) =>
        tmFormatNumber(row.value, row.locale, {
          minDecimals: row.minDecimals,
          maxDecimals: row.maxDecimals,
          percent: row.percent,
          grouping: row.grouping,
        }),
      ),
    ).toEqual([]);
  });

  it('date rows', () => {
    expect(
      drift(golden.date, (row) =>
        tmFormatDate(row.iso, row.locale, {
          calendar: calendarOf(row.calendar),
          dateStyle: row.dateStyle as TmDateStyle,
          quarterPattern: row.quarterPattern,
        }),
      ),
    ).toEqual([]);
  });

  it('month name rows', () => {
    expect(
      drift(golden.monthName, (row) =>
        tmFormatMonthName(row.month, row.locale, { calendar: calendarOf(row.calendar) }),
      ),
    ).toEqual([]);
  });

  it('weekday name rows', () => {
    expect(
      drift(golden.weekdayName, (row) => tmFormatWeekdayName(row.weekday, row.locale)),
    ).toEqual([]);
  });

  it('date-time rows', () => {
    expect(
      drift(golden.dateTime, (row) =>
        tmFormatDateTime(row.instant, row.locale, {
          timeZone: row.timeZone,
          calendar: calendarOf(row.calendar),
          dateStyle: row.dateStyle as TmDateStyle,
          timeStyle: row.timeStyle as TmTimeStyle | 'none',
          zoneLabel: row.zoneLabel,
        }),
      ),
    ).toEqual([]);
  });

  it('time rows', () => {
    expect(
      drift(golden.time, (row) =>
        tmFormatTime(row.instant, row.locale, {
          timeZone: row.timeZone,
          timeStyle: row.timeStyle as TmTimeStyle,
          zoneLabel: row.zoneLabel,
        }),
      ),
    ).toEqual([]);
  });

  it('relative time rows', () => {
    expect(
      drift(golden.relative, (row) =>
        tmFormatRelativeTime(row.instant, row.locale, {
          now: row.now,
          timeZone: row.timeZone,
          style: row.style as 'long' | 'short' | 'narrow',
        }),
      ),
    ).toEqual([]);
  });

  it('zone label rows', () => {
    expect(
      drift(golden.zoneLabel, (row) =>
        ɵtmShowsZoneLabel(
          row.instant,
          row.timeZone,
          row.deviceTimeZone,
          row.mode as 'auto' | 'column' | 'none',
          // The instant a column's header was labelled at.
          row.header === null ? undefined : millisOf(row.header),
        )
          ? tmFormatZoneOffset(row.instant, row.locale, row.timeZone)
          : '',
      ),
    ).toEqual([]);
  });
});

/**
 * No formatter's output holds a bidirectional control character, in the
 * golden's locales and in further right-to-left ones, in every calendar:
 * markup orders the text, never invisible marks.
 */
describe('formatted text is free of bidirectional controls', () => {
  const calendars = [tmGregorianCalendar(), tmUmalquraCalendar(), tmEthiopicCalendar()];
  const locales = [
    ...new Set([...golden.date.map((row) => row.locale), 'fa', 'fa-IR', 'he', 'ur', 'ps', 'ckb']),
  ];
  const instant = '2026-10-20T07:42:07Z';
  const now = Date.UTC(2026, 9, 20, 9);

  /** Fails, naming the text's origin, when `text` holds a control. */
  function expectClean(text: string, origin: string): void {
    expect([...text.matchAll(TM_BIDI_CONTROLS)], `${origin}: ${JSON.stringify(text)}`).toEqual([]);
  }

  for (const locale of locales) {
    it(locale, () => {
      for (const value of [-1234.5, 1234567, -0.155]) {
        expectClean(tmFormatNumber(value, locale), 'number');
        expectClean(tmFormatNumber(value, locale, { percent: true, maxDecimals: 1 }), 'percent');
        expectClean(tmFormatNumber(value, locale, { grouping: false }), 'ungrouped');
      }
      for (const calendar of calendars) {
        for (const dateStyle of [
          'numeric',
          'medium',
          'long',
          'weekday',
          'month',
          'quarter',
          'year',
        ] as const) {
          expectClean(tmFormatDate('2026-03-05', locale, { calendar, dateStyle }), dateStyle);
          expectClean(
            tmFormatDateTime(instant, locale, {
              timeZone: 'Asia/Riyadh',
              calendar,
              dateStyle,
              timeStyle: 'medium',
              zoneLabel: true,
            }),
            `date-time ${dateStyle}`,
          );
        }
        for (let month = 1; month <= 13; month++) {
          expectClean(tmFormatMonthName(month, locale, { calendar }), 'month name');
        }
        expectClean(
          tmFormatRelativeTime('2026-01-05T07:42:00Z', locale, {
            now,
            timeZone: 'Asia/Riyadh',
            calendar,
          }),
          'relative date',
        );
      }
      for (let weekday = 1; weekday <= 7; weekday++) {
        expectClean(tmFormatWeekdayName(weekday, locale), 'weekday name');
      }
      expectClean(tmFormatTime(instant, locale, { timeZone: 'Asia/Riyadh', zoneLabel: true }), 'time');
      for (const style of ['long', 'short', 'narrow'] as const) {
        for (const past of ['2026-10-20T08:58:00Z', '2026-10-20T04:00:00Z', '2026-10-18T04:00:00Z']) {
          expectClean(
            tmFormatRelativeTime(past, locale, { now, timeZone: 'Asia/Riyadh', style }),
            `relative ${style}`,
          );
        }
      }
      expectClean(tmFormatZoneOffset(instant, locale, 'Asia/Kolkata'), 'zone offset');
      expectClean(tmFormatZoneName('Asia/Riyadh', locale, now), 'zone name');
    });
  }
});
