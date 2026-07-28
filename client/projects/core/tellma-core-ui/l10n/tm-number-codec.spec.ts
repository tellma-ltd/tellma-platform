// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_PARSE_ERROR } from '@tellma/core-ui/contracts';

import {
  TM_NUMBER_MAX_DIGITS,
  tmFormatNumber,
  tmNumberDigitCount,
  tmParseNumber,
} from './tm-number-codec';

/** U+200E LEFT-TO-RIGHT MARK — Intl prefixes RTL negatives with an invisible mark. */
const LRM = String.fromCodePoint(0x200e);
/** U+061C ARABIC LETTER MARK — the mark ar-EG emits before a negative. */
const ALM = String.fromCodePoint(0x061c);
/** U+066A ARABIC PERCENT SIGN. */
const ARABIC_PERCENT = String.fromCodePoint(0x066a);

describe('tm-number-codec', () => {
  it('round-trips a negative number in RTL / non-Latin-minus locales', () => {
    // Intl.NumberFormat prefixes the negative with an invisible bidi-control
    // mark in these locales; the codec must not reject or clear it.
    for (const locale of ['ar', 'ar-EG', 'fa', 'he']) {
      const formatted = tmFormatNumber(-1234.5, locale);
      expect(tmParseNumber(formatted, locale)).toBe(-1234.5);
    }
  });

  it('parses a negative even when a leading bidi-control mark is present', () => {
    expect(tmParseNumber(`${LRM}-12`, 'ar')).toBe(-12);
    expect(tmParseNumber(`${ALM}-12`, 'ar-EG')).toBe(-12);
    // The last-resort machine-number path strips them too (String.trim keeps
    // them, so Number(`${LRM}-3.5`) alone would be NaN).
    expect(tmParseNumber(`${LRM}-3.5`, 'en')).toBe(-3.5);
  });

  it('round-trips localized group and decimal separators', () => {
    for (const value of [0, 1234.56, -1000000, 0.5, -0.25]) {
      for (const locale of ['en', 'de', 'fr', 'ar']) {
        expect(tmParseNumber(tmFormatNumber(value, locale), locale)).toBe(value);
      }
    }
  });

  it('parses a paste from another locale by trying the source locale first', () => {
    // A German-formatted number pasted into an English grid.
    expect(tmParseNumber('1.234,56', 'en', { sourceLocale: 'de' })).toBe(1234.56);
  });

  it('parses empty text as null and rejects genuinely unparseable text', () => {
    expect(tmParseNumber('', 'en')).toBeNull();
    expect(tmParseNumber('   ', 'en')).toBeNull();
    expect(tmParseNumber('abc', 'en')).toBe(TM_PARSE_ERROR);
    expect(tmParseNumber('-', 'en')).toBe(TM_PARSE_ERROR);
  });

  it('formats null / undefined / empty as an empty string', () => {
    expect(tmFormatNumber(null, 'en')).toBe('');
    expect(tmFormatNumber(undefined, 'en')).toBe('');
    expect(tmFormatNumber('', 'en')).toBe('');
  });

  it('pads to minDecimals and rounds at maxDecimals', () => {
    expect(tmFormatNumber(5, 'en', { minDecimals: 2 })).toBe('5.00'); // min pads
    expect(tmFormatNumber(1.005, 'en', { maxDecimals: 2 })).toBe('1.01'); // max rounds
    expect(tmFormatNumber(1483.8, 'en', { minDecimals: 2, maxDecimals: 2 })).toBe('1,483.80');
    expect(tmFormatNumber(1234.5, 'en', { maxDecimals: 0 })).toBe('1,235'); // integer display
  });

  it('defaults to 0…20 fraction digits (unbounded look)', () => {
    expect(tmFormatNumber(1.5, 'en')).toBe('1.5');
    expect(tmFormatNumber(42, 'en')).toBe('42');
  });

  it('floors maxDecimals at minDecimals so an inverted pair never throws', () => {
    expect(tmFormatNumber(1.23, 'en', { minDecimals: 3, maxDecimals: 1 })).toBe('1.230');
  });

  describe('percent mode', () => {
    it('formats a fraction as a percentage with the bounds on displayed digits', () => {
      expect(tmFormatNumber(0.75, 'en', { percent: true })).toBe('75%');
      expect(tmFormatNumber(0.756789, 'en', { percent: true, maxDecimals: 2 })).toBe('75.68%');
      expect(tmFormatNumber(0.5, 'en', { percent: true, minDecimals: 1 })).toBe('50.0%');
    });

    it('parses bare numbers, trailing signs, and leading signs alike', () => {
      expect(tmParseNumber('75', 'en', { percent: true })).toBe(0.75);
      expect(tmParseNumber('75%', 'en', { percent: true })).toBe(0.75);
      expect(tmParseNumber('%75', 'en', { percent: true })).toBe(0.75);
      expect(tmParseNumber(' 75 % ', 'en', { percent: true })).toBe(0.75);
    });

    it('round-trips formatted output in locales with a leading percent sign', () => {
      // Turkish formats percentages sign-first; the formatted text must
      // parse back to the same fraction.
      for (const locale of ['tr', 'eu', 'en', 'de']) {
        const formatted = tmFormatNumber(0.25, locale, { percent: true });
        expect(tmParseNumber(formatted, locale, { percent: true })).toBe(0.25);
      }
    });

    it('round-trips Arabic percent output and accepts Arabic-Indic digits', () => {
      const formatted = tmFormatNumber(0.75, 'ar-SA', { percent: true });
      expect(tmParseNumber(formatted, 'ar-SA', { percent: true })).toBe(0.75);
      expect(tmParseNumber(`٧٥${ARABIC_PERCENT}`, 'ar-SA', { percent: true })).toBe(0.75);
    });

    it('applies the sourceLocale ladder to the numeric remainder', () => {
      expect(tmParseNumber('1.234,5%', 'en', { percent: true, sourceLocale: 'de' })).toBe(12.345);
    });

    it('rejects a lone percent sign and keeps empty-to-null semantics', () => {
      expect(tmParseNumber('%', 'en', { percent: true })).toBe(TM_PARSE_ERROR);
      expect(tmParseNumber('', 'en', { percent: true })).toBeNull();
    });
  });

  describe('tmNumberDigitCount', () => {
    it('counts integer plus fraction digits of the canonical rendering', () => {
      expect(tmNumberDigitCount(1234.5)).toBe(5);
      expect(tmNumberDigitCount(-1234.5)).toBe(5); // sign excluded
      expect(tmNumberDigitCount(0.25)).toBe(3); // the integer zero counts
      expect(tmNumberDigitCount(0)).toBe(1);
      expect(tmNumberDigitCount(1000000)).toBe(7); // grouping never counts
    });

    it('places the documented envelope boundary', () => {
      expect(tmNumberDigitCount(0.12345678901234)).toBe(TM_NUMBER_MAX_DIGITS);
      expect(tmNumberDigitCount(123456789012345)).toBe(TM_NUMBER_MAX_DIGITS);
      expect(tmNumberDigitCount(1234567890123456)).toBe(TM_NUMBER_MAX_DIGITS + 1);
    });

    it('never renders exponent forms and treats non-finite values as unbounded', () => {
      expect(tmNumberDigitCount(1e21)).toBe(22);
      expect(tmNumberDigitCount(Number.POSITIVE_INFINITY)).toBe(Number.POSITIVE_INFINITY);
      expect(tmNumberDigitCount(Number.NaN)).toBe(Number.POSITIVE_INFINITY);
    });
  });
});
