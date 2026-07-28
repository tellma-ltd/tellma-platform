// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The shared number codec: formatting via Intl.NumberFormat and parsing
// with symbols derived from it — formatToParts yields the locale's
// group/decimal separators and numbering-system digits, so localized
// separators and non-Latin numerals round-trip.

import { TM_PARSE_ERROR, type TmParseError } from '@tellma/core-ui/contracts';

/**
 * Options shared by {@link tmFormatNumber} and {@link tmParseNumber}. The
 * option surface is a deliberately small closed set — locale ×
 * `minDecimals`/`maxDecimals`/`percent` — reproducible by a server-side
 * implementation from the same parameters.
 */
export interface TmNumberFormatOptions {
  /** Minimum fraction digits (padded with zeros). Default `0`. */
  readonly minDecimals?: number;
  /**
   * Maximum fraction digits — display rounds half-away-from-zero at this
   * scale. Default `20` ("as many as needed"); floored at `minDecimals`
   * so an inverted pair can never make `Intl.NumberFormat` throw.
   */
  readonly maxDecimals?: number;
  /**
   * Percent mode: format a fraction as a percentage (`0.75` → `75%`, with
   * `minDecimals`/`maxDecimals` bounding the DISPLAYED percent digits) and
   * parse a percentage to a fraction (`75`, `75%` and `%75` all parse to
   * `0.75` — several locales format with a leading sign; a bare number is
   * never treated as an already-scaled fraction). Default `false`.
   */
  readonly percent?: boolean;
}

/** Options of {@link tmParseNumber}. */
export interface TmNumberParseOptions extends TmNumberFormatOptions {
  /**
   * The locale the text was FORMATTED in, when known (a paste's origin) —
   * tried before the active locale, so `1.234,56` pasted from a German
   * source parses as `1234.56` rather than mis-reading in the target
   * locale.
   */
  readonly sourceLocale?: string;
}

/**
 * The decimal digit-count ceiling ({@link tmNumberDigitCount}) inside
 * which every client `number` is exactly the decimal a server-side exact
 * decimal type parses: any decimal of at most 15 significant digits
 * round-trips string → IEEE-754 double → string unchanged.
 */
export const TM_NUMBER_MAX_DIGITS = 15;

interface LocaleNumberSymbols {
  readonly group: string;
  readonly decimal: string;
  readonly minus: string;
  /** Digit `0` of the locale's numbering system ('0' for latn). */
  readonly zeroCodePoint: number;
}

const formatterCache = new Map<string, Intl.NumberFormat>();
const symbolsCache = new Map<string, LocaleNumberSymbols>();

/** Intl's ceiling for fraction digits — the effective "unbounded" default. */
const MAX_FRACTION_DIGITS = 20;

function formatterFor(locale: string): Intl.NumberFormat {
  let formatter = formatterCache.get(locale);
  if (formatter === undefined) {
    formatter = new Intl.NumberFormat(locale, { maximumFractionDigits: MAX_FRACTION_DIGITS });
    formatterCache.set(locale, formatter);
  }
  return formatter;
}

// Display formatters (per locale × fraction-digit bounds × percent) are
// cached apart from the parse/symbol formatter above, which must always
// keep its full 20-digit precision so localized separators and numerals
// still round-trip.
const displayFormatterCache = new Map<string, Intl.NumberFormat>();

function displayFormatterFor(
  locale: string,
  min: number,
  max: number,
  percent: boolean,
): Intl.NumberFormat {
  const key = `${locale}|${min}|${max}|${percent ? 'p' : ''}`;
  let formatter = displayFormatterCache.get(key);
  if (formatter === undefined) {
    formatter = new Intl.NumberFormat(locale, {
      minimumFractionDigits: min,
      maximumFractionDigits: max,
      ...(percent ? { style: 'percent' as const } : {}),
    });
    displayFormatterCache.set(key, formatter);
  }
  return formatter;
}

function symbolsFor(locale: string): LocaleNumberSymbols {
  let symbols = symbolsCache.get(locale);
  if (symbols === undefined) {
    const parts = formatterFor(locale).formatToParts(-12345678.9);
    const group = parts.find((part) => part.type === 'group')?.value ?? ',';
    const decimal = parts.find((part) => part.type === 'decimal')?.value ?? '.';
    const minus = parts.find((part) => part.type === 'minusSign')?.value ?? '-';
    const zero = formatterFor(locale).format(0);
    symbols = { group, decimal, minus, zeroCodePoint: zero.codePointAt(0) ?? 48 };
    symbolsCache.set(locale, symbols);
  }
  return symbols;
}

/**
 * Formats a number for display in the given locale ('' for
 * null/undefined/''). Fraction digits are bounded by
 * `minDecimals`/`maxDecimals` (default 0…20, i.e. "as many as needed");
 * in percent mode the value is a fraction and the bounds apply to the
 * displayed percent digits.
 */
export function tmFormatNumber(
  value: unknown,
  locale: string,
  options?: TmNumberFormatOptions,
): string {
  if (value === null || value === undefined || value === '') {
    return '';
  }
  const numeric = typeof value === 'number' ? value : Number(value);
  if (!Number.isFinite(numeric)) {
    return String(value);
  }
  const min = Math.max(0, options?.minDecimals ?? 0);
  const max = Math.max(min, options?.maxDecimals ?? MAX_FRACTION_DIGITS);
  return displayFormatterFor(locale, min, max, options?.percent === true).format(numeric);
}

/**
 * Parses localized numeric text: strips the locale's group separators,
 * normalizes its decimal separator, minus sign, and numbering-system
 * digits, then falls back to plain `Number` semantics. Tries the source
 * locale (paste origin) before the active locale. Empty text parses to
 * `null`; anything unrecognizable is a parse error.
 *
 * In percent mode, one percent sign is accepted at EITHER end (Turkish
 * and Basque format with a leading sign) and the result is divided by
 * 100; a bare number divides too — typed `75`, `75%` and `%75` all parse
 * to `0.75`.
 */
export function tmParseNumber(
  text: string,
  locale: string,
  options?: TmNumberParseOptions,
): number | null | TmParseError {
  let trimmed = text.trim();
  if (trimmed === '') {
    return null;
  }
  if (options?.percent === true) {
    // Strip invisible format/bidi-control marks BEFORE looking for the
    // sign — Intl surrounds it with them in RTL locales — then strip at
    // most one sign from either end.
    trimmed = trimmed.replace(FORMAT_CONTROL, '');
    if (trimmed.length > 0 && isPercentSign(trimmed[trimmed.length - 1])) {
      trimmed = trimmed.slice(0, -1).trim();
    } else if (trimmed.length > 0 && isPercentSign(trimmed[0])) {
      trimmed = trimmed.slice(1).trim();
    }
    if (trimmed === '') {
      return TM_PARSE_ERROR;
    }
  }
  const percent = options?.percent === true;
  const sourceLocale = options?.sourceLocale;
  const locales =
    sourceLocale !== undefined && sourceLocale !== locale ? [sourceLocale, locale] : [locale];
  for (const candidate of locales) {
    const normalized = normalizeWithLocale(trimmed, candidate);
    if (normalized !== TM_PARSE_ERROR) {
      return finishParse(normalized, percent);
    }
  }
  // Last resort: machine-formatted text — a strict decimal shape (optional
  // exponent), never JavaScript's full `Number` grammar: `0x1A` or
  // `Infinity` in a business numeric field must reject, not surprise.
  // Invisible format/bidi-control marks are stripped first — `String.trim`
  // leaves them, so `'‎-12'` would otherwise be unrecognizable.
  const plain = trimmed.replace(FORMAT_CONTROL, '');
  return MACHINE_DECIMAL.test(plain) ? finishParse(plain, percent) : TM_PARSE_ERROR;
}

/** The machine-text fallback shape: plain decimal, optional exponent. */
const MACHINE_DECIMAL = /^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$/;

/**
 * Converts a normalized machine string to the final number. Percent mode
 * divides by 100 TEXTUALLY — shifting the decimal point in the digit
 * string — because `Number('51.46') / 100` double-rounds one ulp off the
 * double nearest to `0.5146`, which both drifts the value and inflates
 * its digit count past precision ceilings.
 */
function finishParse(normalized: string, percent: boolean): number | TmParseError {
  const value = Number(percent ? shiftDecimalLeft(normalized, 2) : normalized);
  return Number.isFinite(value) ? value : TM_PARSE_ERROR;
}

/**
 * Shifts the decimal point of a plain decimal string `places` to the left
 * (`'51.46'` → `'0.5146'`). Exponent forms shift the exponent instead.
 */
function shiftDecimalLeft(text: string, places: number): string {
  const exponentMatch = /^(.*?)[eE]([+-]?\d+)$/.exec(text);
  if (exponentMatch !== null) {
    const shifted = Number(exponentMatch[2]) - places;
    // An exponent past the safe-integer range would re-render in
    // scientific notation and synthesize a malformed literal; the value
    // is a zero/overflow either way, so shift the MANTISSA instead.
    return Number.isSafeInteger(shifted)
      ? `${exponentMatch[1]}e${shifted}`
      : `${shiftDecimalLeft(exponentMatch[1], places)}e${exponentMatch[2]}`;
  }
  let sign = '';
  let digits = text;
  if (digits.startsWith('-') || digits.startsWith('+')) {
    sign = digits[0] === '-' ? '-' : '';
    digits = digits.slice(1);
  }
  const dot = digits.indexOf('.');
  const integer = dot === -1 ? digits : digits.slice(0, dot);
  const fraction = dot === -1 ? '' : digits.slice(dot + 1);
  const pointAt = integer.length - places;
  const all = integer + fraction;
  const shifted =
    pointAt <= 0
      ? `0.${'0'.repeat(-pointAt)}${all}`
      : `${all.slice(0, pointAt)}.${all.slice(pointAt)}`;
  // Trim a trailing point ('75' → '0.75' never hits this; '7500' → '75.00'
  // keeps digits, harmless) and preserve the sign.
  return sign + (shifted.endsWith('.') ? shifted.slice(0, -1) : shifted);
}

/**
 * The decimal digit count of a number's canonical (non-grouped, plain
 * decimal) rendering: integer digits plus fraction digits present, sign
 * and separator excluded. `1234.5` counts 5; `0.25` counts 3 (the
 * leading integer zero counts). Values at most {@link TM_NUMBER_MAX_DIGITS}
 * digits round-trip exactly through an IEEE-754 double; non-finite input
 * counts as `Infinity` (never within any ceiling).
 *
 * The rendering comes from `Intl.NumberFormat` — never `String(value)`,
 * whose exponent forms (`1e21`) would miscount.
 */
export function tmNumberDigitCount(value: number): number {
  if (!Number.isFinite(value)) {
    return Number.POSITIVE_INFINITY;
  }
  // The full-precision 'en' formatter renders plain ASCII digits, grouped —
  // group separators are skipped along with the decimal point.
  const text = formatterFor('en').format(Math.abs(value));
  let count = 0;
  for (let i = 0; i < text.length; i++) {
    const code = text.charCodeAt(i);
    if (code >= 48 && code <= 57) {
      count += 1;
    }
  }
  return count;
}

/** Unicode format / bidi-control marks (the `Cf` category) — always invisible. */
const FORMAT_CONTROL = /\p{Cf}/gu;

/** The percent signs accepted in percent mode: ASCII, Arabic, fullwidth. */
function isPercentSign(ch: string): boolean {
  return ch === '%' || ch === '٪' || ch === '％';
}

/**
 * Normalizes localized numeric text to a plain machine digit string
 * (`'-1234.56'`) — digits mapped to ASCII, group separators dropped,
 * decimal and minus normalized. The NUMBER conversion happens later so
 * percent scaling can operate on the text (see {@link shiftDecimalLeft}).
 */
function normalizeWithLocale(text: string, locale: string): string | TmParseError {
  const symbols = symbolsFor(locale);
  // Drop invisible format/bidi-control marks up front. `Intl.NumberFormat`
  // prefixes a NEGATIVE with one in RTL / non-Latin-minus locales (ar/fa/he
  // emit U+200E, ar-EG emits U+061C as a leading literal part), and
  // `symbolsFor` never captures it — so committing an unchanged negative, or
  // pasting an Intl-formatted negative, would otherwise reject and clear it.
  const cleaned = text.replace(FORMAT_CONTROL, '');
  let normalized = '';
  for (const ch of cleaned) {
    const code = ch.codePointAt(0)!;
    // Numbering-system digits → ASCII.
    if (code >= symbols.zeroCodePoint && code <= symbols.zeroCodePoint + 9) {
      normalized += String.fromCharCode(48 + (code - symbols.zeroCodePoint));
      continue;
    }
    if (ch >= '0' && ch <= '9') {
      normalized += ch;
      continue;
    }
    if (ch === symbols.group || ch === ' ' || ch === ' ' || ch === ' ') {
      continue; // group separators (incl. space variants) drop out
    }
    if (ch === symbols.decimal) {
      normalized += '.';
      continue;
    }
    if (ch === symbols.minus || ch === '-' || ch === '−') {
      normalized += '-';
      continue;
    }
    if (ch === '+') {
      continue;
    }
    return TM_PARSE_ERROR;
  }
  if (normalized === '' || normalized === '-') {
    return TM_PARSE_ERROR;
  }
  return Number.isFinite(Number(normalized)) ? normalized : TM_PARSE_ERROR;
}
