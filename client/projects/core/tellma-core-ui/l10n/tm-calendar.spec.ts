// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmGregorianCalendar, ɵtmParseIsoDate, ɵtmToIsoDate } from './tm-calendar';

describe('tmGregorianCalendar', () => {
  const calendar = tmGregorianCalendar();

  it('is the identity mapping between parts and ISO', () => {
    expect(calendar.id).toBe('gregory');
    expect(calendar.toParts('2026-03-05')).toEqual({ year: 2026, month: 3, day: 5 });
    expect(calendar.fromParts({ year: 2026, month: 3, day: 5 })).toBe('2026-03-05');
  });

  it('round-trips across leap years and month edges', () => {
    for (const iso of ['2024-02-29', '2026-12-31', '2026-01-01', '0100-01-01', '9999-12-31']) {
      expect(calendar.fromParts(calendar.toParts(iso))).toBe(iso);
    }
  });

  it('fromParts rejects parts that name no real date (the library constrains, we detect)', () => {
    expect(calendar.fromParts({ year: 2026, month: 2, day: 31 })).toBeNull();
    expect(calendar.fromParts({ year: 2026, month: 13, day: 1 })).toBeNull();
    expect(calendar.fromParts({ year: 2026, month: 0, day: 1 })).toBeNull();
    expect(calendar.fromParts({ year: 2026, month: 1, day: 1.5 })).toBeNull();
  });

  it('reports month and day counts, leap February included', () => {
    expect(calendar.monthsInYear(2026)).toBe(12);
    expect(calendar.daysInMonth(2024, 2)).toBe(29);
    expect(calendar.daysInMonth(2026, 2)).toBe(28);
    expect(calendar.daysInMonth(2026, 4)).toBe(30);
  });

  it("today() is the user's local date in ISO shape", () => {
    const now = new Date();
    expect(calendar.today()).toBe(
      ɵtmToIsoDate(now.getFullYear(), now.getMonth() + 1, now.getDate()),
    );
  });

  it('the ISO helpers are strict about shape', () => {
    expect(ɵtmParseIsoDate('2026-3-05')).toBeNull();
    expect(ɵtmParseIsoDate('2026-03-05T00:00')).toBeNull();
    expect(ɵtmParseIsoDate('2026-03-05')).toEqual({ year: 2026, month: 3, day: 5 });
    expect(ɵtmToIsoDate(44, 3, 5)).toBe('0044-03-05');
  });
});

describe('adapter ↔ Intl agreement gate (Gregorian)', () => {
  it('toParts matches Intl.DateTimeFormat numeric fields over sampled dates', () => {
    const calendar = tmGregorianCalendar();
    const formatter = new Intl.DateTimeFormat('en-u-ca-gregory-nu-latn', {
      year: 'numeric',
      month: 'numeric',
      day: 'numeric',
      timeZone: 'UTC',
    });
    // Every 97 days across 1900–2100 — arithmetic runs in
    // @internationalized/date while names/formatting come from the
    // browser's ICU; separate codebases that must not drift.
    const start = Date.UTC(1900, 0, 1, 12);
    const end = Date.UTC(2100, 0, 1, 12);
    for (let time = start; time < end; time += 97 * 24 * 3600 * 1000) {
      const date = new Date(time);
      const iso = ɵtmToIsoDate(
        date.getUTCFullYear(),
        date.getUTCMonth() + 1,
        date.getUTCDate(),
      );
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
