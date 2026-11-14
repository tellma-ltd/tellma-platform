// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_PARSE_ERROR } from '@tellma/core-ui/contracts';

import {
  ɵtmFormatTimeOfDay,
  ɵtmIsTimeOfDay,
  ɵtmParseTimeOfDay,
  ɵtmStepTimeOfDay,
} from './tm-time-of-day';

describe('time of day', () => {
  describe('formatting', () => {
    it('writes the locale hour cycle and digits', () => {
      expect(ɵtmFormatTimeOfDay('14:30', 'en-GB')).toBe('14:30');
      expect(ɵtmFormatTimeOfDay('14:30', 'en-US')).toBe('2:30 PM');
      expect(ɵtmFormatTimeOfDay('14:30', 'ar-SA')).toBe('٢:٣٠ م');
      expect(ɵtmFormatTimeOfDay('14:30', 'ar-SA-u-nu-latn')).toBe('2:30 م');
    });

    it('writes midnight and noon in each hour cycle, as the instant formatter does', () => {
      expect(ɵtmFormatTimeOfDay('00:05', 'en-GB')).toBe('0:05');
      expect(ɵtmFormatTimeOfDay('09:05', 'en-GB')).toBe('9:05');
      expect(ɵtmFormatTimeOfDay('00:05', 'en-US')).toBe('12:05 AM');
      expect(ɵtmFormatTimeOfDay('12:00', 'en-US')).toBe('12:00 PM');
      expect(ɵtmFormatTimeOfDay('00:05', 'ar-SA')).toBe('١٢:٠٥ ص');
    });
  });

  describe('parsing', () => {
    it.each([
      ['14', '14:00'],
      ['9', '09:00'],
      ['930', '09:30'],
      ['1430', '14:30'],
      ['14:30', '14:30'],
      ['14.30', '14:30'],
      ['14 30', '14:30'],
      ['0', '00:00'],
      ['23:59', '23:59'],
    ])('reads %s in en-GB as %s', (text, expected) => {
      expect(ɵtmParseTimeOfDay(text, 'en-GB')).toBe(expected);
    });

    it.each([
      ['2:30 PM', '14:30'],
      ['2:30 pm', '14:30'],
      ['2:30pm', '14:30'],
      ['2:30 p.m.', '14:30'],
      ['2 p', '14:00'],
      ['12 am', '00:00'],
      ['12:15 AM', '00:15'],
      ['12 pm', '12:00'],
      ['14:30', '14:30'],
      ['230pm', '14:30'],
    ])('reads %s in en-US as %s', (text, expected) => {
      expect(ɵtmParseTimeOfDay(text, 'en-US')).toBe(expected);
    });

    it('reads Arabic-Indic and Latin digits and the Arabic day periods in ar-SA', () => {
      expect(ɵtmParseTimeOfDay('٢:٣٠ م', 'ar-SA')).toBe('14:30');
      expect(ɵtmParseTimeOfDay('٢:٣٠ ص', 'ar-SA')).toBe('02:30');
      expect(ɵtmParseTimeOfDay('2:30 م', 'ar-SA')).toBe('14:30');
      expect(ɵtmParseTimeOfDay('١٤٣٠', 'ar-SA')).toBe('14:30');
      expect(ɵtmParseTimeOfDay('14', 'ar-SA')).toBe('14:00');
      expect(ɵtmParseTimeOfDay('2:30 pm', 'ar-SA')).toBe('14:30');
    });

    it('reads both digit sets in ar-SA-u-nu-latn', () => {
      expect(ɵtmParseTimeOfDay('2:30 م', 'ar-SA-u-nu-latn')).toBe('14:30');
      expect(ɵtmParseTimeOfDay('٢:٣٠ م', 'ar-SA-u-nu-latn')).toBe('14:30');
      expect(ɵtmParseTimeOfDay('1430', 'ar-SA-u-nu-latn')).toBe('14:30');
    });

    it('reads its own output back in every pinned locale, for every quarter hour', () => {
      for (const locale of ['en-GB', 'en-US', 'ar-SA', 'ar-SA-u-nu-latn']) {
        for (let minutes = 0; minutes < 24 * 60; minutes += 15) {
          const time = `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;
          expect(
            ɵtmParseTimeOfDay(ɵtmFormatTimeOfDay(time, locale), locale),
            `${locale} ${time}`,
          ).toBe(time);
        }
      }
    });

    it('reads blank text as empty', () => {
      expect(ɵtmParseTimeOfDay('', 'en-GB')).toBeNull();
      expect(ɵtmParseTimeOfDay('   ', 'en-US')).toBeNull();
    });

    it.each([
      ['24', 'en-GB'],
      ['14:60', 'en-GB'],
      ['14:30:15', 'en-GB'],
      ['12345', 'en-GB'],
      ['14:5', 'en-GB'],
      ['abc', 'en-GB'],
      ['13 pm', 'en-US'],
      ['2 am pm', 'en-US'],
      ['pm', 'en-US'],
    ])('refuses %s in %s', (text, locale) => {
      expect(ɵtmParseTimeOfDay(text, locale)).toBe(TM_PARSE_ERROR);
    });
  });

  describe('stepping', () => {
    it('adds and subtracts minutes, wrapping at midnight both ways', () => {
      expect(ɵtmStepTimeOfDay('10:00', 15)).toBe('10:15');
      expect(ɵtmStepTimeOfDay('23:50', 15)).toBe('00:05');
      expect(ɵtmStepTimeOfDay('00:05', -15)).toBe('23:50');
      expect(ɵtmStepTimeOfDay('00:00', -1)).toBe('23:59');
      expect(ɵtmStepTimeOfDay('12:00', 24 * 60)).toBe('12:00');
    });

    it('recognizes the wire form', () => {
      expect(ɵtmIsTimeOfDay('09:30')).toBe(true);
      expect(ɵtmIsTimeOfDay('9:30')).toBe(false);
      expect(ɵtmIsTimeOfDay('24:00')).toBe(false);
    });
  });
});
