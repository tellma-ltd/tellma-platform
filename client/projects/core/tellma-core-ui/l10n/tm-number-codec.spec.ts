// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmFormatNumber } from './tm-number-codec';

describe('tm-number-codec', () => {
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

  describe('grouping', () => {
    it('writes no group separators when grouping is off', () => {
      expect(tmFormatNumber(2026, 'en', { grouping: false })).toBe('2026');
      expect(tmFormatNumber(1234567.5, 'en', { grouping: false })).toBe('1234567.5');
      expect(tmFormatNumber(1234567, 'de', { grouping: false })).toBe('1234567');
      expect(tmFormatNumber(-1234567, 'en', { grouping: false })).toBe('-1234567');
      // The decimals and the percent mode still apply.
      expect(tmFormatNumber(1234.5, 'en', { grouping: false, minDecimals: 2 })).toBe('1234.50');
      expect(tmFormatNumber(12.5, 'en', { grouping: false, percent: true })).toBe('1250%');
    });

    it('groups by default, and under an explicit true', () => {
      expect(tmFormatNumber(2026, 'en')).toBe('2,026');
      expect(tmFormatNumber(2026, 'en', {})).toBe('2,026');
      expect(tmFormatNumber(2026, 'en', { grouping: true })).toBe('2,026');
      expect(tmFormatNumber(12.5, 'en', { percent: true })).toBe('1,250%');
    });

    it("keeps the locale's digits when grouping is off", () => {
      expect(tmFormatNumber(2026, 'ar-SA', { grouping: false })).toBe('٢٠٢٦');
      expect(tmFormatNumber(2026, 'ar-SA')).toBe('٢٬٠٢٦');
    });

    it("keeps the locale's minimum grouping digits when grouping is on", () => {
      // Spanish groups from five digits.
      expect(tmFormatNumber(1234, 'es')).toBe('1234');
      expect(tmFormatNumber(12345, 'es')).toBe('12.345');
      expect(tmFormatNumber(12345, 'es', { grouping: false })).toBe('12345');
    });

    it('formats the two settings apart, whichever was asked first', () => {
      // One cached formatter per setting: neither answers for the other.
      expect(tmFormatNumber(987654, 'fr', { grouping: false })).toBe('987654');
      expect(tmFormatNumber(987654, 'fr')).not.toBe('987654');
      expect(tmFormatNumber(987654, 'fr', { grouping: false })).toBe('987654');
    });
  });

  describe('bidirectional controls', () => {
    it('formats a negative with no mark beside its sign', () => {
      expect(tmFormatNumber(-1234.5, 'ar')).toBe('-1,234.5');
      expect(tmFormatNumber(-1234.5, 'ar-SA-u-nu-latn')).toBe('-1,234.5');
      expect(tmFormatNumber(-12, 'he')).toBe('-12');
    });
  });

  describe('percent mode', () => {
    it('formats a fraction as a percentage with the bounds on displayed digits', () => {
      expect(tmFormatNumber(0.75, 'en', { percent: true })).toBe('75%');
      expect(tmFormatNumber(0.756789, 'en', { percent: true, maxDecimals: 2 })).toBe('75.68%');
      expect(tmFormatNumber(0.5, 'en', { percent: true, minDecimals: 1 })).toBe('50.0%');
    });
  });
});
