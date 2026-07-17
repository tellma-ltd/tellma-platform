// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The `number` column type's locale codec: formatting via Intl.NumberFormat
// and parsing with symbols derived from it — formatToParts yields the
// locale's group/decimal separators and numbering-system digits, so
// localized separators and non-Latin numerals round-trip. Internal: the
// column model bakes these into its format/parse closures.

import { TM_PARSE_ERROR, type TmParseError } from '@tellma/core-ui/contracts';

interface LocaleNumberSymbols {
  readonly group: string;
  readonly decimal: string;
  readonly minus: string;
  /** Digit `0` of the locale's numbering system ('0' for latn). */
  readonly zeroCodePoint: number;
}

const formatterCache = new Map<string, Intl.NumberFormat>();
const symbolsCache = new Map<string, LocaleNumberSymbols>();

function formatterFor(locale: string): Intl.NumberFormat {
  let formatter = formatterCache.get(locale);
  if (formatter === undefined) {
    formatter = new Intl.NumberFormat(locale, { maximumFractionDigits: 20 });
    formatterCache.set(locale, formatter);
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

/** Formats a number for display in the given locale ('' for null/undefined). */
export function tmFormatNumber(value: unknown, locale: string): string {
  if (value === null || value === undefined || value === '') {
    return '';
  }
  const numeric = typeof value === 'number' ? value : Number(value);
  if (!Number.isFinite(numeric)) {
    return String(value);
  }
  return formatterFor(locale).format(numeric);
}

/**
 * Parses localized numeric text: strips the locale's group separators,
 * normalizes its decimal separator, minus sign, and numbering-system
 * digits, then falls back to plain `Number` semantics. Tries the source
 * locale (paste origin) before the active locale. Empty text parses to
 * `null`; anything unrecognizable is a parse error.
 */
export function tmParseNumber(
  text: string,
  locale: string,
  sourceLocale?: string,
): number | null | TmParseError {
  const trimmed = text.trim();
  if (trimmed === '') {
    return null;
  }
  const locales = sourceLocale !== undefined && sourceLocale !== locale
    ? [sourceLocale, locale]
    : [locale];
  for (const candidate of locales) {
    const parsed = parseWithLocale(trimmed, candidate);
    if (parsed !== TM_PARSE_ERROR) {
      return parsed;
    }
  }
  // Last resort: plain JavaScript number syntax (machine-formatted text).
  // Strip invisible format/bidi-control marks first — `String.trim` leaves
  // them, so `Number('‎-12')` would otherwise be NaN.
  const plain = Number(trimmed.replace(FORMAT_CONTROL, ''));
  return Number.isFinite(plain) ? plain : TM_PARSE_ERROR;
}

/** Unicode format / bidi-control marks (the `Cf` category) — always invisible. */
const FORMAT_CONTROL = /\p{Cf}/gu;

function parseWithLocale(text: string, locale: string): number | TmParseError {
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
  const value = Number(normalized);
  return Number.isFinite(value) ? value : TM_PARSE_ERROR;
}
