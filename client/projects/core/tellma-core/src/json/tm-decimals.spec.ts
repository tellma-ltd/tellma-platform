// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TmDecimals } from './tm-decimals';
import type { TmDecimal } from './tm-json';

/** A decimal literal for the tests, cast as the wire would deliver it. */
const d = (text: string): TmDecimal => text as TmDecimal;

describe('TmDecimals.parse', () => {
  it('expands an exponent into plain digits', () => {
    expect(TmDecimals.parse('1e3')).toBe('1000');
    expect(TmDecimals.parse('1E3')).toBe('1000');
    expect(TmDecimals.parse('1.25e1')).toBe('12.5');
    expect(TmDecimals.parse('1.50e1')).toBe('15.0');
    expect(TmDecimals.parse('1e-3')).toBe('0.001');
    expect(TmDecimals.parse('-2.5e-2')).toBe('-0.025');
    expect(TmDecimals.parse('5e+2')).toBe('500');
  });

  it('drops the sign of zero and keeps its scale', () => {
    expect(TmDecimals.parse('-0.0')).toBe('0.0');
    expect(TmDecimals.parse('-0')).toBe('0');
    expect(TmDecimals.parse('+0.00')).toBe('0.00');
  });

  it('keeps all 30 digits of a long value', () => {
    expect(TmDecimals.parse('123456789012345678901234567890')).toBe('123456789012345678901234567890');
    expect(TmDecimals.parse('-12345678901234567890.1234567890')).toBe('-12345678901234567890.1234567890');
  });

  it('keeps the scale the text carries and drops redundant leading zeros and plus signs', () => {
    expect(TmDecimals.parse('1234.5000')).toBe('1234.5000');
    expect(TmDecimals.parse('007.10')).toBe('7.10');
    expect(TmDecimals.parse('+.5')).toBe('0.5');
    expect(TmDecimals.parse('5.')).toBe('5');
  });

  it('answers null for text that is not a number', () => {
    for (const text of ['', '.', '-', '+', 'e3', '1e', '1.2.3', ' 1', '1 ', 'Infinity', 'NaN', '0x10', '1,5', 'abc']) {
      expect(TmDecimals.parse(text), JSON.stringify(text)).toBeNull();
    }
  });

  it('answers null for an exponent too large to expand', () => {
    expect(TmDecimals.parse('1e1001')).toBeNull();
    expect(TmDecimals.parse('1e-1001')).toBeNull();
    expect(TmDecimals.parse('1e1000')).toBe(`1${'0'.repeat(1000)}`);
  });

  it('answers null for a value that is not a string', () => {
    expect(TmDecimals.parse(12 as unknown as string)).toBeNull();
  });
});

describe('TmDecimals.isDecimal', () => {
  it('accepts plain decimal text and refuses everything else', () => {
    for (const value of ['0', '-1', '1234.5000', '9007199254740993', '0.001']) {
      expect(TmDecimals.isDecimal(value), value).toBe(true);
    }
    for (const value of ['1e3', '+1', '.5', '5.', '', ' 1', 12, null, undefined, {}]) {
      expect(TmDecimals.isDecimal(value), String(value)).toBe(false);
    }
  });
});

describe('TmDecimals arithmetic at 28 digits', () => {
  // 28 significant digits: the precision of the server's decimal.
  const big = d('1234567890123456789012345678');
  const fraction = d('0.0000000000000000000000000001');

  it('adds exactly', () => {
    expect(TmDecimals.add(big, d('1'))).toBe('1234567890123456789012345679');
    expect(TmDecimals.add(d('0.1'), d('0.2'))).toBe('0.3');
    expect(TmDecimals.add(fraction, fraction)).toBe('0.0000000000000000000000000002');
    expect(TmDecimals.add(d('9999999999999999999999999999'), d('1'))).toBe('10000000000000000000000000000');
  });

  it('subtracts exactly, through zero', () => {
    expect(TmDecimals.subtract(big, big)).toBe('0');
    expect(TmDecimals.subtract(d('1'), fraction)).toBe('0.9999999999999999999999999999');
    expect(TmDecimals.subtract(d('1.50'), d('2.5'))).toBe('-1.00');
    expect(TmDecimals.subtract(d('-1'), d('-1'))).toBe('0');
  });

  it('keeps the larger scale of the operands', () => {
    expect(TmDecimals.add(d('1.50'), d('2.5'))).toBe('4.00');
    expect(TmDecimals.add(d('1234.5000'), d('10.25'))).toBe('1244.7500');
  });

  it('compares by value, whatever the scales', () => {
    expect(TmDecimals.compare(big, d('1234567890123456789012345677'))).toBe(1);
    expect(TmDecimals.compare(d('1234567890123456789012345677'), big)).toBe(-1);
    expect(TmDecimals.compare(d('1.50'), d('1.5'))).toBe(0);
    expect(TmDecimals.compare(d('9'), d('10'))).toBe(-1);
    expect(TmDecimals.compare(d('-0.0001'), d('0'))).toBe(-1);
    expect(TmDecimals.compare(fraction, d('0'))).toBe(1);
  });

  it('refuses an operand that is not decimal text', () => {
    expect(() => TmDecimals.add(d('1'), d('one'))).toThrowError(TypeError);
    expect(() => TmDecimals.compare(d(''), d('1'))).toThrowError(TypeError);
  });
});

describe('TmDecimals.toNumber', () => {
  it('converts to the nearest number', () => {
    expect(TmDecimals.toNumber(d('1234.5000'))).toBe(1234.5);
    expect(TmDecimals.toNumber(d('-0.25'))).toBe(-0.25);
    // Lossy beyond a double's precision: the nearest double, not the digits.
    expect(TmDecimals.toNumber(d('9007199254740993'))).toBe(9007199254740992);
  });

  it('refuses a value that is not decimal text', () => {
    expect(() => TmDecimals.toNumber(d('n/a'))).toThrowError(TypeError);
  });
});

describe('TmDecimals.fromNumber', () => {
  it('writes an editor number within 15 significant digits', () => {
    expect(TmDecimals.fromNumber(0.1 + 0.2)).toBe('0.3');
    expect(TmDecimals.fromNumber(1234.5)).toBe('1234.5');
    expect(TmDecimals.fromNumber(100)).toBe('100');
    expect(TmDecimals.fromNumber(-0.025)).toBe('-0.025');
    expect(TmDecimals.fromNumber(123456789012345.6)).toBe('123456789012346');
    expect(TmDecimals.fromNumber(1e21)).toBe('1000000000000000000000');
    expect(TmDecimals.fromNumber(1e-7)).toBe('0.0000001');
  });

  it('writes zero without a sign', () => {
    expect(TmDecimals.fromNumber(0)).toBe('0');
    expect(TmDecimals.fromNumber(-0)).toBe('0');
  });

  it('refuses a value that is not a finite number', () => {
    for (const value of [Number.NaN, Number.POSITIVE_INFINITY, Number.NEGATIVE_INFINITY]) {
      expect(() => TmDecimals.fromNumber(value)).toThrowError(RangeError);
    }
  });

  it('round-trips through parse and toNumber', () => {
    for (const value of [0.5, 42, -17.75, 3.14159265358979]) {
      expect(TmDecimals.toNumber(TmDecimals.fromNumber(value))).toBe(value);
    }
  });
});
