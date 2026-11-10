// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_BIDI_CONTROLS } from './tm-bidi';
import {
  tmFormatDateTime,
  tmFormatRelativeTime,
  tmFormatTime,
  tmFormatZoneName,
  tmFormatZoneOffset,
  ɵtmParseInstant,
  ɵtmShowsZoneLabel,
  ɵtmZoneOffset,
} from './tm-instant';

/** 20 October 2026, 07:42 UTC — 10:42 in Riyadh. */
const AT = '2026-10-20T07:42:00Z';
const AT_MILLIS = Date.UTC(2026, 9, 20, 7, 42);

const SECOND = 1000;
const MINUTE = 60 * SECOND;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

// Beirut is at +2 in winter and +3 in summer; Riyadh is at +3 all year.
const WINTER = '2026-01-15T09:00:00Z';
const SUMMER = '2026-07-15T09:00:00Z';
const WINTER_MILLIS = Date.UTC(2026, 0, 15, 9);
const SUMMER_MILLIS = Date.UTC(2026, 6, 15, 9);

describe('ɵtmParseInstant', () => {
  it('reads Z and ±hh:mm offsets onto the same point of the time line', () => {
    expect(ɵtmParseInstant(AT)).toBe(AT_MILLIS);
    expect(ɵtmParseInstant('2026-10-20T10:42:00+03:00')).toBe(AT_MILLIS);
    expect(ɵtmParseInstant('2026-10-20T02:12:00-05:30')).toBe(AT_MILLIS);
    expect(ɵtmParseInstant('2026-10-20T07:42:00+00:00')).toBe(AT_MILLIS);
    // An offset can carry the instant onto another date.
    expect(ɵtmParseInstant('2026-10-21T01:42:00+18:00')).toBe(AT_MILLIS);
  });

  it('accepts a time to the minute, to the second, or with a fraction', () => {
    expect(ɵtmParseInstant('2026-10-20T07:42Z')).toBe(AT_MILLIS);
    expect(ɵtmParseInstant('2026-10-20T07:42:07Z')).toBe(AT_MILLIS + 7 * SECOND);
    expect(ɵtmParseInstant('2026-10-20T07:42:07.5Z')).toBe(AT_MILLIS + 7 * SECOND + 500);
    expect(ɵtmParseInstant('2026-10-20T07:42:07.04Z')).toBe(AT_MILLIS + 7 * SECOND + 40);
  });

  it('truncates a fraction longer than three digits to milliseconds, never rounding', () => {
    expect(ɵtmParseInstant('2026-10-20T07:42:00.1239999Z')).toBe(AT_MILLIS + 123);
    expect(ɵtmParseInstant('2026-10-20T07:42:00.9999999+00:00')).toBe(AT_MILLIS + 999);
    expect(ɵtmParseInstant('2026-10-20T10:42:00.1234567+03:00')).toBe(AT_MILLIS + 123);
  });

  it('reads years below 100 as written', () => {
    const expected = new Date(Date.UTC(2000, 2, 1));
    expected.setUTCFullYear(99);
    expect(ɵtmParseInstant('0099-03-01T00:00:00Z')).toBe(expected.getTime());
    // 29 February of a leap year below 100.
    const leap = new Date(Date.UTC(2000, 1, 29));
    leap.setUTCFullYear(4);
    expect(ɵtmParseInstant('0004-02-29T00:00:00Z')).toBe(leap.getTime());
  });

  it('rejects text with no offset, a malformed shape or a field out of range', () => {
    for (const text of [
      '',
      '2026-10-20',
      '2026-10-20T07:42:00', // no offset
      '2026-10-20 07:42:00Z', // a space for the T
      '2026-10-20T07:42:00z',
      '2026-10-20T07:42:00+0300',
      '2026-10-20T07:42:00+03',
      '2026-10-20T07Z',
      '2026-10-20T07:42:00.Z',
      '2026-13-20T07:42:00Z',
      '2026-00-20T07:42:00Z',
      '2026-02-30T07:42:00Z',
      '2025-02-29T07:42:00Z', // not a leap year
      '2026-04-31T07:42:00Z',
      '2026-10-00T07:42:00Z',
      '2026-10-20T24:00:00Z',
      '2026-10-20T07:60:00Z',
      '2026-10-20T07:42:60Z',
      '2026-10-20T07:42:00+24:00',
      '2026-10-20T07:42:00+03:60',
      '0000-10-20T07:42:00Z',
      '2026-10-20T07:42:00Z ',
      'yesterday',
    ]) {
      expect(ɵtmParseInstant(text), text).toBeNull();
    }
  });
});

describe('tmFormatDateTime', () => {
  it('formats the date and the time in the display zone', () => {
    expect(tmFormatDateTime(AT, 'en-GB', { timeZone: 'Asia/Riyadh' })).toBe('20/10/2026, 10:42');
    expect(tmFormatDateTime(AT, 'en-US', { timeZone: 'Asia/Riyadh' })).toBe('10/20/2026, 10:42 AM');
    expect(tmFormatDateTime(AT, 'en-GB', { timeZone: 'UTC' })).toBe('20/10/2026, 7:42');
    expect(tmFormatDateTime(AT, 'en-GB', { timeZone: 'Asia/Kolkata' })).toBe('20/10/2026, 13:12');
  });

  it('reads the same instant whatever offset it was written with', () => {
    expect(tmFormatDateTime('2026-10-20T02:12:00-05:30', 'en-GB', { timeZone: 'Asia/Riyadh' })).toBe(
      '20/10/2026, 10:42',
    );
  });

  it('puts an instant on the date the zone is at', () => {
    const lateEvening = '2026-12-31T22:30:00Z';
    expect(tmFormatDateTime(lateEvening, 'en-GB', { timeZone: 'UTC' })).toBe('31/12/2026, 22:30');
    expect(tmFormatDateTime(lateEvening, 'en-GB', { timeZone: 'Asia/Riyadh' })).toBe(
      '01/01/2027, 1:30',
    );
  });

  it('follows the date style and adds the second under the medium time style', () => {
    const options = { timeZone: 'Asia/Riyadh' } as const;
    expect(tmFormatDateTime(AT, 'en-GB', { ...options, dateStyle: 'medium' })).toBe(
      '20 Oct 2026, 10:42',
    );
    expect(tmFormatDateTime(AT, 'en-GB', { ...options, dateStyle: 'long' })).toBe(
      '20 October 2026 at 10:42',
    );
    expect(tmFormatDateTime(AT, 'en-GB', { ...options, dateStyle: 'weekday' })).toBe(
      'Tuesday, 20 Oct 2026, 10:42',
    );
    expect(tmFormatDateTime(AT, 'en-GB', { ...options, timeStyle: 'medium' })).toBe(
      '20/10/2026, 10:42:00',
    );
    expect(
      tmFormatDateTime('2026-10-20T07:42:07.999Z', 'en-GB', { ...options, timeStyle: 'medium' }),
    ).toBe('20/10/2026, 10:42:07');
  });

  it("writes the instant's date in the zone alone under timeStyle 'none'", () => {
    // One second apart, either side of midnight in Riyadh.
    const options = { timeZone: 'Asia/Riyadh', timeStyle: 'none' } as const;
    expect(tmFormatDateTime('2026-10-19T20:59:59Z', 'en-GB', options)).toBe('19/10/2026');
    expect(tmFormatDateTime('2026-10-19T21:00:00Z', 'en-GB', options)).toBe('20/10/2026');
    expect(
      tmFormatDateTime('2026-10-19T21:00:00Z', 'en-GB', { ...options, dateStyle: 'weekday' }),
    ).toBe('Tuesday, 20 Oct 2026');
  });

  it('writes a period style without a time, whatever the time style', () => {
    const options = { timeZone: 'Asia/Riyadh' } as const;
    expect(tmFormatDateTime(AT, 'en-GB', { ...options, dateStyle: 'month' })).toBe('Oct 2026');
    expect(tmFormatDateTime(AT, 'en-GB', { ...options, dateStyle: 'quarter' })).toBe('Q4 2026');
    expect(tmFormatDateTime(AT, 'en-GB', { ...options, dateStyle: 'year' })).toBe('2026');
    expect(
      tmFormatDateTime(AT, 'en-GB', { ...options, dateStyle: 'year', timeStyle: 'medium' }),
    ).toBe('2026');
    expect(
      tmFormatDateTime(AT, 'en-GB', {
        ...options,
        dateStyle: 'quarter',
        quarterPattern: '{year}/{quarter}',
      }),
    ).toBe('2026/4');
    // The period is the one the zone is in: new year has come in Riyadh.
    expect(
      tmFormatDateTime('2026-12-31T22:30:00Z', 'en-GB', { ...options, dateStyle: 'year' }),
    ).toBe('2027');
  });

  it("appends the zone's GMT offset when asked", () => {
    expect(tmFormatDateTime(AT, 'en-GB', { timeZone: 'Asia/Riyadh', zoneLabel: true })).toBe(
      '20/10/2026, 10:42 GMT+3',
    );
    expect(tmFormatDateTime(AT, 'en-GB', { timeZone: 'Asia/Kolkata', zoneLabel: true })).toBe(
      '20/10/2026, 13:12 GMT+5:30',
    );
    // The offset in force at the instant: Beirut's winter and summer differ.
    expect(tmFormatDateTime(WINTER, 'en-GB', { timeZone: 'Asia/Beirut', zoneLabel: true })).toBe(
      '15/01/2026, 11:00 GMT+2',
    );
    expect(tmFormatDateTime(SUMMER, 'en-GB', { timeZone: 'Asia/Beirut', zoneLabel: true })).toBe(
      '15/07/2026, 12:00 GMT+3',
    );
  });

  it('never labels a text without a time', () => {
    const options = { timeZone: 'Asia/Riyadh', zoneLabel: true } as const;
    expect(tmFormatDateTime(AT, 'en-GB', { ...options, timeStyle: 'none' })).toBe('20/10/2026');
    expect(tmFormatDateTime(AT, 'en-GB', { ...options, dateStyle: 'month' })).toBe('Oct 2026');
  });

  it("formats ''/null/undefined as ''", () => {
    const options = { timeZone: 'Asia/Riyadh' } as const;
    expect(tmFormatDateTime('', 'en-GB', options)).toBe('');
    expect(tmFormatDateTime(null, 'en-GB', options)).toBe('');
    expect(tmFormatDateTime(undefined, 'en-GB', options)).toBe('');
  });

  it('returns text that is not an instant as-is, warning once in development', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      const options = { timeZone: 'Asia/Riyadh' } as const;
      // No offset: a wall clock, not an instant.
      expect(tmFormatDateTime('2026-10-20T07:42:01', 'en-GB', options)).toBe('2026-10-20T07:42:01');
      expect(warn).toHaveBeenCalledTimes(1);
      expect(warn.mock.calls[0][0]).toContain('2026-10-20T07:42:01');
      // The same problem again is not reported again.
      expect(tmFormatDateTime('2026-10-20T07:42:01', 'en-GB', options)).toBe('2026-10-20T07:42:01');
      expect(tmFormatTime('2026-10-20T07:42:01', 'en-GB', options)).toBe('2026-10-20T07:42:01');
      expect(warn).toHaveBeenCalledTimes(1);
      // Another one is.
      expect(tmFormatDateTime('20 October', 'en-GB', options)).toBe('20 October');
      expect(warn).toHaveBeenCalledTimes(2);
    } finally {
      warn.mockRestore();
    }
  });

  it('shows an instant in UTC, with a development warning, when the zone is unknown', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      expect(tmFormatDateTime(AT, 'en-GB', { timeZone: 'Mars/Olympus_Mons' })).toBe(
        '20/10/2026, 7:42',
      );
      expect(warn).toHaveBeenCalledTimes(1);
      expect(warn.mock.calls[0][0]).toContain('Mars/Olympus_Mons');
      // The label names the offset actually shown.
      expect(
        tmFormatDateTime(AT, 'en-GB', { timeZone: 'Mars/Olympus_Mons', zoneLabel: true }),
      ).toBe(tmFormatDateTime(AT, 'en-GB', { timeZone: 'UTC', zoneLabel: true }));
      expect(ɵtmZoneOffset(AT_MILLIS, 'Mars/Olympus_Mons')).toBe(0);
    } finally {
      warn.mockRestore();
    }
  });

  it('carries no bidirectional control character', () => {
    for (const locale of ['ar', 'ar-SA', 'ar-EG', 'fa', 'he', 'ur']) {
      for (const dateStyle of ['numeric', 'medium', 'long', 'weekday'] as const) {
        const text = tmFormatDateTime(AT, locale, {
          timeZone: 'Asia/Riyadh',
          dateStyle,
          zoneLabel: true,
        });
        expect([...text.matchAll(TM_BIDI_CONTROLS)], `${locale} ${dateStyle}`).toEqual([]);
      }
    }
  });
});

describe('tmFormatTime', () => {
  it("formats the time of day in the zone, in the locale's hour cycle and digits", () => {
    expect(tmFormatTime(AT, 'en-GB', { timeZone: 'Asia/Riyadh' })).toBe('10:42');
    expect(tmFormatTime(AT, 'en-US', { timeZone: 'Asia/Riyadh' })).toBe('10:42 AM');
    expect(tmFormatTime(AT, 'ar-SA', { timeZone: 'Asia/Riyadh' })).toBe('١٠:٤٢ ص');
    expect(tmFormatTime(AT, 'en-GB', { timeZone: 'Asia/Kolkata' })).toBe('13:12');
    expect(tmFormatTime(AT, 'en-US', { timeZone: 'Asia/Kolkata' })).toBe('1:12 PM');
  });

  it('adds the second under the medium style', () => {
    expect(tmFormatTime(AT, 'en-GB', { timeZone: 'Asia/Riyadh', timeStyle: 'medium' })).toBe(
      '10:42:00',
    );
    expect(
      tmFormatTime('2026-10-20T07:42:59.999Z', 'en-GB', {
        timeZone: 'Asia/Riyadh',
        timeStyle: 'medium',
      }),
    ).toBe('10:42:59');
  });

  it("appends the zone's GMT offset when asked", () => {
    expect(tmFormatTime(AT, 'en-GB', { timeZone: 'Asia/Riyadh', zoneLabel: true })).toBe(
      '10:42 GMT+3',
    );
  });

  it('separates the time from its day period with a plain space', () => {
    // CLDR's own separator is U+202F, which engines disagree on passing through.
    const narrow = String.fromCodePoint(0x202f);
    for (const locale of ['en-US', 'en-AU', 'es-US', 'en-IN', 'ar-SA', 'hi-IN']) {
      for (const text of [
        tmFormatTime(AT, locale, { timeZone: 'Asia/Riyadh', timeStyle: 'medium', zoneLabel: true }),
        tmFormatDateTime(AT, locale, { timeZone: 'Asia/Riyadh', dateStyle: 'weekday' }),
        tmFormatDateTime(AT, locale, { timeZone: 'Asia/Riyadh', zoneLabel: true }),
      ]) {
        expect(text.includes(narrow), `${locale}: ${text}`).toBe(false);
      }
    }
    expect(tmFormatTime(AT, 'en-US', { timeZone: 'Asia/Riyadh' })).toBe('10:42 AM');
  });

  it("formats ''/null/undefined as ''", () => {
    expect(tmFormatTime('', 'en-GB', { timeZone: 'UTC' })).toBe('');
    expect(tmFormatTime(null, 'en-GB', { timeZone: 'UTC' })).toBe('');
    expect(tmFormatTime(undefined, 'en-GB', { timeZone: 'UTC' })).toBe('');
  });
});

describe('tmFormatRelativeTime', () => {
  /** The text for an instant `age` milliseconds before now (after it, when negative). */
  function relative(age: number, style?: 'long' | 'short' | 'narrow'): string {
    return tmFormatRelativeTime(new Date(AT_MILLIS - age).toISOString(), 'en-GB', {
      now: AT_MILLIS,
      timeZone: 'Asia/Riyadh',
      style,
    });
  }

  it('says "now" under 60 seconds, either way', () => {
    expect(relative(0)).toBe('now');
    expect(relative(1)).toBe('now');
    expect(relative(MINUTE - 1)).toBe('now');
    expect(relative(-(MINUTE - 1))).toBe('now');
  });

  it('counts whole minutes from 60 seconds to under 60 minutes, truncating', () => {
    expect(relative(MINUTE)).toBe('1 min ago');
    expect(relative(2 * MINUTE - 1)).toBe('1 min ago');
    expect(relative(2 * MINUTE)).toBe('2 min ago');
    expect(relative(HOUR - 1)).toBe('59 min ago');
    expect(relative(-MINUTE)).toBe('in 1 min');
    expect(relative(-(5 * MINUTE + 59 * SECOND))).toBe('in 5 min');
    expect(relative(-(HOUR - 1))).toBe('in 59 min');
  });

  it('counts whole hours from 60 minutes to under 24 hours, truncating', () => {
    expect(relative(HOUR)).toBe('1 hr ago');
    expect(relative(2 * HOUR - 1)).toBe('1 hr ago');
    expect(relative(3 * HOUR + 59 * MINUTE)).toBe('3 hr ago');
    expect(relative(DAY - 1)).toBe('23 hr ago');
    expect(relative(-HOUR)).toBe('in 1 hr');
    expect(relative(-(DAY - 1))).toBe('in 23 hr');
  });

  it("counts calendar days in the zone from 24 hours, not 24-hour blocks", () => {
    expect(relative(DAY)).toBe('yesterday');
    expect(relative(-DAY)).toBe('tomorrow');
    const options = { now: AT_MILLIS, timeZone: 'Asia/Riyadh' } as const;
    // Now is 10:42 on the 20th in Riyadh. One minute either side of the
    // midnight that began the 19th there, both about 34 hours ago:
    expect(tmFormatRelativeTime('2026-10-18T21:00:00Z', 'en-GB', options)).toBe('yesterday');
    expect(tmFormatRelativeTime('2026-10-18T20:59:00Z', 'en-GB', options)).toBe('2 days ago');
    expect(tmFormatRelativeTime('2026-10-17T07:42:00Z', 'en-GB', options)).toBe('3 days ago');
    expect(tmFormatRelativeTime('2026-10-23T07:42:00Z', 'en-GB', options)).toBe('in 3 days');
  });

  it('falls back to the date from seven calendar days out', () => {
    const options = { now: AT_MILLIS, timeZone: 'Asia/Riyadh' } as const;
    // Either side of the midnight that began the 14th in Riyadh.
    expect(tmFormatRelativeTime('2026-10-13T21:00:00Z', 'en-GB', options)).toBe('6 days ago');
    expect(tmFormatRelativeTime('2026-10-13T20:59:59Z', 'en-GB', options)).toBe('13 Oct 2026');
    // Either side of the midnight that ends the 26th.
    expect(tmFormatRelativeTime('2026-10-26T20:59:59Z', 'en-GB', options)).toBe('in 6 days');
    expect(tmFormatRelativeTime('2026-10-26T21:00:00Z', 'en-GB', options)).toBe('27 Oct 2026');
    expect(tmFormatRelativeTime('2019-05-01T12:00:00Z', 'en-GB', options)).toBe('1 May 2019');
  });

  it('writes the unit words in the requested width', () => {
    expect(relative(2 * MINUTE, 'long')).toBe('2 minutes ago');
    expect(relative(3 * HOUR, 'long')).toBe('3 hours ago');
    expect(relative(-(5 * MINUTE), 'long')).toBe('in 5 minutes');
    expect(relative(2 * MINUTE, 'narrow')).toBe('2 min ago');
    expect(relative(2 * MINUTE, 'short')).toBe(relative(2 * MINUTE));
  });

  it('lets the zone decide the days', () => {
    // 26 hours apart: one midnight between them in UTC, two in Riyadh.
    const now = Date.UTC(2026, 9, 20, 22, 30);
    const instant = '2026-10-19T20:30:00Z';
    expect(tmFormatRelativeTime(instant, 'en-GB', { now, timeZone: 'UTC' })).toBe('yesterday');
    expect(tmFormatRelativeTime(instant, 'en-GB', { now, timeZone: 'Asia/Riyadh' })).toBe(
      '2 days ago',
    );
  });

  it('counts dates across a daylight-saving day boundary', () => {
    // 29 March 2026 has 23 hours in Beirut: 24 hours after 23:30 on the
    // 28th is 00:30 on the 30th — two dates on.
    expect(
      tmFormatRelativeTime('2026-03-28T21:30:00Z', 'en-GB', {
        now: Date.UTC(2026, 2, 29, 21, 30),
        timeZone: 'Asia/Beirut',
      }),
    ).toBe('2 days ago');
    // 24 October 2026 has 25 hours there: 24 hours after 00:10 is 23:10 on
    // the same date.
    expect(
      tmFormatRelativeTime('2026-10-23T21:10:00Z', 'en-GB', {
        now: Date.UTC(2026, 9, 24, 21, 10),
        timeZone: 'Asia/Beirut',
      }),
    ).toBe('today');
    // An hour later it is the next date: the same instant is "yesterday".
    expect(
      tmFormatRelativeTime('2026-10-23T21:10:00Z', 'en-GB', {
        now: Date.UTC(2026, 9, 24, 22, 10),
        timeZone: 'Asia/Beirut',
      }),
    ).toBe('yesterday');
  });

  it("speaks the locale's own words and digits, without direction marks", () => {
    const text = tmFormatRelativeTime(new Date(AT_MILLIS - 5 * MINUTE).toISOString(), 'ar-SA', {
      now: AT_MILLIS,
      timeZone: 'Asia/Riyadh',
    });
    expect(text).toContain('٥');
    expect([...text.matchAll(TM_BIDI_CONTROLS)]).toEqual([]);
  });

  it('falls back to the date when the clock is not a number', () => {
    expect(tmFormatRelativeTime(AT, 'en-GB', { now: Number.NaN, timeZone: 'Asia/Riyadh' })).toBe(
      '20 Oct 2026',
    );
  });

  it("formats ''/null/undefined as '' and returns text that is not an instant as-is", () => {
    const options = { now: AT_MILLIS, timeZone: 'UTC' } as const;
    expect(tmFormatRelativeTime('', 'en-GB', options)).toBe('');
    expect(tmFormatRelativeTime(null, 'en-GB', options)).toBe('');
    expect(tmFormatRelativeTime(undefined, 'en-GB', options)).toBe('');
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      expect(tmFormatRelativeTime('2026-10-20', 'en-GB', options)).toBe('2026-10-20');
      expect(warn).toHaveBeenCalledTimes(1);
    } finally {
      warn.mockRestore();
    }
  });
});

describe('zone offsets and names', () => {
  it('reads a zone offset in minutes east, at the instant', () => {
    expect(ɵtmZoneOffset(AT_MILLIS, 'UTC')).toBe(0);
    expect(ɵtmZoneOffset(AT_MILLIS, 'Asia/Riyadh')).toBe(180);
    expect(ɵtmZoneOffset(AT_MILLIS, 'Asia/Kolkata')).toBe(330);
    expect(ɵtmZoneOffset(WINTER_MILLIS, 'America/St_Johns')).toBe(-210);
    expect(ɵtmZoneOffset(WINTER_MILLIS, 'Asia/Beirut')).toBe(120);
    expect(ɵtmZoneOffset(SUMMER_MILLIS, 'Asia/Beirut')).toBe(180);
  });

  it('reads the offset of an instant with milliseconds, before 1970 too', () => {
    expect(ɵtmZoneOffset(AT_MILLIS + 999, 'Asia/Riyadh')).toBe(180);
    expect(ɵtmZoneOffset(-1, 'Asia/Kolkata')).toBe(330);
    expect(ɵtmZoneOffset(-86_400_001, 'UTC')).toBe(0);
  });

  it("formats the offset in the locale's words and digits", () => {
    expect(tmFormatZoneOffset(AT, 'en-GB', 'Asia/Riyadh')).toBe('GMT+3');
    expect(tmFormatZoneOffset(AT_MILLIS, 'en-GB', 'Asia/Riyadh')).toBe('GMT+3');
    expect(tmFormatZoneOffset(AT, 'en-GB', 'Asia/Kolkata')).toBe('GMT+5:30');
    expect(tmFormatZoneOffset(AT, 'en-GB', 'America/St_Johns')).toBe('GMT-2:30');
    expect(tmFormatZoneOffset(AT, 'ar-SA', 'Asia/Riyadh')).toBe('غرينتش+٣');
    expect(tmFormatZoneOffset(AT, 'ar-SA-u-nu-latn', 'Asia/Riyadh')).toBe('غرينتش+3');
    expect(tmFormatZoneOffset(WINTER, 'en-GB', 'Asia/Beirut')).toBe('GMT+2');
    expect(tmFormatZoneOffset(SUMMER, 'en-GB', 'Asia/Beirut')).toBe('GMT+3');
  });

  it("gives '' for an offset of text that is not an instant", () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      expect(tmFormatZoneOffset('tomorrow', 'en-GB', 'Asia/Riyadh')).toBe('');
      expect(tmFormatZoneOffset('', 'en-GB', 'Asia/Riyadh')).toBe('');
      expect(tmFormatZoneOffset(Number.NaN, 'en-GB', 'Asia/Riyadh')).toBe('');
    } finally {
      warn.mockRestore();
    }
  });

  it("names a zone in the locale, independent of daylight saving", () => {
    expect(tmFormatZoneName('Europe/London', 'en-GB', WINTER_MILLIS)).toBe('United Kingdom Time');
    expect(tmFormatZoneName('Europe/London', 'en-GB', SUMMER_MILLIS)).toBe('United Kingdom Time');
    expect(tmFormatZoneName('Asia/Riyadh', 'en-GB', Number.NaN)).toBe('');
    const arabic = tmFormatZoneName('Asia/Riyadh', 'ar-SA', AT_MILLIS);
    expect(arabic).not.toBe('');
    expect([...arabic.matchAll(TM_BIDI_CONTROLS)]).toEqual([]);
  });
});

describe('ɵtmShowsZoneLabel', () => {
  it("auto: labels when the display offset differs from the device's at the instant", () => {
    expect(ɵtmShowsZoneLabel(SUMMER, 'Asia/Riyadh', 'Europe/London', 'auto')).toBe(true);
    expect(ɵtmShowsZoneLabel(SUMMER, 'Asia/Kolkata', 'Asia/Riyadh', 'auto')).toBe(true);
    // The default mode.
    expect(ɵtmShowsZoneLabel(SUMMER, 'Asia/Riyadh', 'Europe/London')).toBe(true);
    // Epoch milliseconds read the same.
    expect(ɵtmShowsZoneLabel(SUMMER_MILLIS, 'Asia/Riyadh', 'Europe/London')).toBe(true);
  });

  it('auto: compares offsets, never zone ids', () => {
    expect(ɵtmShowsZoneLabel(SUMMER, 'Asia/Riyadh', 'Asia/Kuwait', 'auto')).toBe(false);
    expect(ɵtmShowsZoneLabel(SUMMER, 'Asia/Riyadh', 'Asia/Riyadh', 'auto')).toBe(false);
    // Beirut on a Riyadh device: an hour apart in winter, equal in summer.
    expect(ɵtmShowsZoneLabel(WINTER, 'Asia/Beirut', 'Asia/Riyadh', 'auto')).toBe(true);
    expect(ɵtmShowsZoneLabel(SUMMER, 'Asia/Beirut', 'Asia/Riyadh', 'auto')).toBe(false);
    // UTC on a London device: equal in winter, an hour apart in summer.
    expect(ɵtmShowsZoneLabel(WINTER, 'UTC', 'Europe/London', 'auto')).toBe(false);
    expect(ɵtmShowsZoneLabel(SUMMER, 'UTC', 'Europe/London', 'auto')).toBe(true);
  });

  it("column: under a labelled header a cell is bare unless its offset differs from the header's", () => {
    // Riyadh never changes offset: every cell agrees with the header.
    expect(
      ɵtmShowsZoneLabel(WINTER, 'Asia/Riyadh', 'Europe/London', 'column', SUMMER_MILLIS),
    ).toBe(false);
    // Beirut under a header labelled in winter (GMT+2): a winter cell is
    // bare; a summer cell is at GMT+3 and labels — though +3 is the
    // device's own offset.
    expect(ɵtmShowsZoneLabel(WINTER, 'Asia/Beirut', 'Asia/Riyadh', 'column', WINTER_MILLIS)).toBe(
      false,
    );
    expect(ɵtmShowsZoneLabel(SUMMER, 'Asia/Beirut', 'Asia/Riyadh', 'column', WINTER_MILLIS)).toBe(
      true,
    );
  });

  it('column: under a bare header a cell labels as auto does', () => {
    // In summer Beirut and Riyadh agree, so the header is bare.
    expect(ɵtmShowsZoneLabel(WINTER, 'Asia/Beirut', 'Asia/Riyadh', 'column', SUMMER_MILLIS)).toBe(
      true,
    );
    expect(ɵtmShowsZoneLabel(SUMMER, 'Asia/Beirut', 'Asia/Riyadh', 'column', SUMMER_MILLIS)).toBe(
      false,
    );
    // No header instant given: the same.
    expect(ɵtmShowsZoneLabel(WINTER, 'Asia/Beirut', 'Asia/Riyadh', 'column')).toBe(true);
    expect(ɵtmShowsZoneLabel(SUMMER, 'Asia/Riyadh', 'Asia/Kuwait', 'column', SUMMER_MILLIS)).toBe(
      false,
    );
  });

  it('none: never labels', () => {
    expect(ɵtmShowsZoneLabel(SUMMER, 'Asia/Riyadh', 'Europe/London', 'none')).toBe(false);
  });

  it('never labels an empty value or text that is not an instant', () => {
    for (const instant of ['', null, undefined, '2026-07-15', Number.NaN]) {
      expect(ɵtmShowsZoneLabel(instant, 'Asia/Riyadh', 'Europe/London'), String(instant)).toBe(
        false,
      );
    }
  });
});
