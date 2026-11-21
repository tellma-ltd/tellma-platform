// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmDecimal } from './tm-json';

// Exact arithmetic on decimal wire text (spec 0024 §10.4). Every operation
// works on the digits as a BigInt scaled by a power of ten, so no value ever
// passes through a binary double unless the caller asks for one (`toNumber`).

/**
 * What `parse` accepts: an optional sign, digits with an optional point (at
 * least one digit on either side of it), and an optional exponent. Groups:
 * 1 sign, 2 integer digits, 3 fraction digits, 4 exponent.
 */
const NUMBER_TEXT = /^([+-]?)(\d*)(?:\.(\d*))?(?:[eE]([+-]?\d+))?$/;

/** The wire grammar `isDecimal` checks: no plus sign, no exponent, no bare point. */
const WIRE_TEXT = /^-?\d+(?:\.\d+)?$/;

/**
 * The largest exponent `parse` expands. A longer expansion is refused rather
 * than allocated, so text such as `'1e999999999'` cannot exhaust memory.
 */
const MAX_EXPONENT = 1000;

/** The significant digits `fromNumber` keeps: what a double holds exactly. */
const NUMBER_DIGITS = 15;

/** A decimal as an integer count of units of `10^-scale`. */
interface Scaled {
  /** The value times `10^scale`. */
  readonly units: bigint;
  /** The number of fraction digits the text carries. */
  readonly scale: number;
}

/**
 * Canonical wire text for the number that `text` spells, or `null` when it
 * spells none. The exponent is expanded, the sign of zero and any plus sign
 * are dropped, redundant leading zeros go, and the fraction keeps every digit
 * it was given (the scale is part of a decimal's value on the wire).
 */
function canonical(text: string): string | null {
  const match = NUMBER_TEXT.exec(text);
  if (match === null) {
    return null;
  }
  const [, sign, integer, fraction = '', exponentText] = match;
  if (integer.length === 0 && fraction.length === 0) {
    return null;
  }
  const exponent = exponentText === undefined ? 0 : Number(exponentText);
  if (!Number.isSafeInteger(exponent) || Math.abs(exponent) > MAX_EXPONENT) {
    return null;
  }

  // Shift the point by the exponent: `digits` holds every digit, and `scale`
  // says how many of them sit after the point.
  let digits = integer + fraction;
  let scale = fraction.length - exponent;
  if (scale < 0) {
    digits += '0'.repeat(-scale);
    scale = 0;
  }
  if (digits.length <= scale) {
    digits = '0'.repeat(scale - digits.length + 1) + digits;
  }

  const whole = digits.slice(0, digits.length - scale).replace(/^0+(?=\d)/, '');
  const part = digits.slice(digits.length - scale);
  const negative = sign === '-' && /[1-9]/.test(digits);
  return `${negative ? '-' : ''}${whole}${scale > 0 ? `.${part}` : ''}`;
}

/** Reads canonical text into units and scale. */
function toScaled(text: string): Scaled {
  const point = text.indexOf('.');
  const scale = point === -1 ? 0 : text.length - point - 1;
  return { units: BigInt(point === -1 ? text : text.slice(0, point) + text.slice(point + 1)), scale };
}

/** Reads an operand, refusing a value that is not decimal text at run time. */
function operand(value: TmDecimal, operation: string): Scaled {
  const text = typeof value === 'string' ? canonical(value) : null;
  if (text === null) {
    throw new TypeError(`TmDecimals.${operation} received ${JSON.stringify(value)}, which is not a decimal.`);
  }
  return toScaled(text);
}

/** Writes units at a scale back to canonical text. */
function fromScaled(units: bigint, scale: number): TmDecimal {
  const negative = units < 0n;
  let digits = (negative ? -units : units).toString();
  if (digits.length <= scale) {
    digits = '0'.repeat(scale - digits.length + 1) + digits;
  }
  const whole = digits.slice(0, digits.length - scale);
  const part = digits.slice(digits.length - scale);
  return `${negative ? '-' : ''}${whole}${scale > 0 ? `.${part}` : ''}` as TmDecimal;
}

/** Brings two operands to the larger of their scales. */
function align(a: Scaled, b: Scaled): readonly [bigint, bigint, number] {
  const scale = Math.max(a.scale, b.scale);
  return [
    a.units * 10n ** BigInt(scale - a.scale),
    b.units * 10n ** BigInt(scale - b.scale),
    scale,
  ];
}

/**
 * Exact helpers for {@link TmDecimal} values. Sums and comparisons of
 * decimals go through these, never through `+`, `<` or the other operators,
 * which would join or compare the text.
 *
 * @example
 * ```ts
 * const total = TmDecimals.add(line.amount, line.tax);        // '1234.5000' + '10.25' → '1244.7500'
 * if (TmDecimals.compare(total, limit) > 0) { … }
 * ```
 */
export const TmDecimals = Object.freeze({
  /**
   * Reads number text into canonical decimal text, or `null` when the text is
   * not a number. Accepts an optional sign, an optional point and an optional
   * exponent (`'1e3'` → `'1000'`, `'+.5'` → `'0.5'`, `'-0.0'` → `'0.0'`), and
   * keeps every fraction digit given (`'1.50'` → `'1.50'`). Whitespace,
   * `Infinity`, `NaN`, hexadecimal and an exponent beyond 1000 in magnitude
   * give `null`.
   */
  parse(text: string): TmDecimal | null {
    return typeof text === 'string' ? (canonical(text) as TmDecimal | null) : null;
  },

  /**
   * Whether `value` is decimal wire text: a string of digits with an optional
   * leading minus and an optional point followed by digits, never an
   * exponent or a plus sign.
   */
  isDecimal(value: unknown): value is TmDecimal {
    return typeof value === 'string' && WIRE_TEXT.test(value);
  },

  /**
   * The exact sum. Its scale is the larger of the operands' scales
   * (`'1.50'` + `'2.5'` → `'4.00'`).
   *
   * @throws TypeError when an operand is not decimal text.
   */
  add(a: TmDecimal, b: TmDecimal): TmDecimal {
    const [x, y, scale] = align(operand(a, 'add'), operand(b, 'add'));
    return fromScaled(x + y, scale);
  },

  /**
   * The exact difference `a - b`. Its scale is the larger of the operands'
   * scales (`'1.50'` - `'2.5'` → `'-1.00'`).
   *
   * @throws TypeError when an operand is not decimal text.
   */
  subtract(a: TmDecimal, b: TmDecimal): TmDecimal {
    const [x, y, scale] = align(operand(a, 'subtract'), operand(b, 'subtract'));
    return fromScaled(x - y, scale);
  },

  /**
   * Compares the values exactly: `-1` when `a` is smaller, `1` when it is
   * larger, `0` when they are equal, whatever their scales (`'1.50'` equals
   * `'1.5'`).
   *
   * @throws TypeError when an operand is not decimal text.
   */
  compare(a: TmDecimal, b: TmDecimal): -1 | 0 | 1 {
    const [x, y] = align(operand(a, 'compare'), operand(b, 'compare'));
    return x < y ? -1 : x > y ? 1 : 0;
  },

  /**
   * The nearest JavaScript number. It is lossy beyond about 15 significant
   * digits, so it serves charts and other approximate uses, never sums.
   *
   * @throws TypeError when the value is not decimal text.
   */
  toNumber(value: TmDecimal): number {
    const { units, scale } = operand(value, 'toNumber');
    return Number(fromScaled(units, scale));
  },

  /**
   * The decimal an editor's number stands for, rounded to 15 significant
   * digits, the precision a double holds exactly, and with trailing zero
   * fraction digits dropped (`0.1 + 0.2` → `'0.3'`, `-0` → `'0'`).
   *
   * @throws RangeError when the value is not a finite number.
   */
  fromNumber(value: number): TmDecimal {
    if (typeof value !== 'number' || !Number.isFinite(value)) {
      throw new RangeError(`TmDecimals.fromNumber received ${String(value)}, which is not a finite number.`);
    }
    const text = canonical(value.toPrecision(NUMBER_DIGITS));
    if (text === null) {
      throw new RangeError(`TmDecimals.fromNumber could not represent ${String(value)}.`);
    }
    // toPrecision pads to 15 digits; the trailing zeros are not part of the value.
    const trimmed = text.includes('.') ? text.replace(/\.?0+$/, '') : text;
    return (trimmed === '-0' ? '0' : trimmed) as TmDecimal;
  },
});
