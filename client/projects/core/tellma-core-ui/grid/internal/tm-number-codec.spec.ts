// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_PARSE_ERROR } from '@tellma/core-ui/contracts';

import { tmFormatNumber, tmParseNumber } from './tm-number-codec';

/** U+200E LEFT-TO-RIGHT MARK — Intl prefixes RTL negatives with an invisible mark. */
const LRM = String.fromCodePoint(0x200e);
/** U+061C ARABIC LETTER MARK — the mark ar-EG emits before a negative. */
const ALM = String.fromCodePoint(0x061c);

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
    expect(tmParseNumber('1.234,56', 'en', 'de')).toBe(1234.56);
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
    expect(tmFormatNumber(5, 'en', 2)).toBe('5.00'); // min pads
    expect(tmFormatNumber(1.005, 'en', 0, 2)).toBe('1.01'); // max rounds
    expect(tmFormatNumber(1483.8, 'en', 2, 2)).toBe('1,483.80'); // both, with grouping
    expect(tmFormatNumber(1234.5, 'en', 0, 0)).toBe('1,235'); // integer display
  });

  it('defaults to 0…20 fraction digits (unbounded look)', () => {
    expect(tmFormatNumber(1.5, 'en')).toBe('1.5');
    expect(tmFormatNumber(42, 'en')).toBe('42');
  });

  it('floors maxDecimals at minDecimals so an inverted pair never throws', () => {
    expect(tmFormatNumber(1.23, 'en', 3, 1)).toBe('1.230'); // max clamped up to 3
  });
});
