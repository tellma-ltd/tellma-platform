// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The number codec's format half: formatting via Intl.NumberFormat. The
// parse half, whose symbols derive from the same formatter so localized
// separators and non-Latin numerals round-trip, lives in
// @tellma/core-ui/l10n-input, so code that only formats never loads it.

import { tmStripBidiControls } from './tm-bidi';

/**
 * Options of {@link tmFormatNumber}, which `tmParseNumber` of
 * `@tellma/core-ui/l10n-input` shares. The option surface is a
 * deliberately small closed set — locale ×
 * `minDecimals`/`maxDecimals`/`percent`/`grouping` — reproducible by a
 * server-side implementation from the same parameters.
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
  /**
   * Whether the formatted text carries group separators. Default `true`:
   * the locale's own grouping, which honours its minimum grouping digits
   * (Spanish writes `1234` and `12.345`). `false` writes none — a year, a
   * code or a serial number reads `2026`, never `2,026`. The digits stay
   * the locale's either way, and parsing accepts group separators
   * whatever this says.
   */
  readonly grouping?: boolean;
}

/** Intl's ceiling for fraction digits — the effective "unbounded" default. */
const MAX_FRACTION_DIGITS = 20;

// Display formatters, per locale × fraction-digit bounds × percent ×
// grouping. The parser keeps a formatter of its own at the full 20-digit
// precision, from which it reads the locale's symbols.
const displayFormatterCache = new Map<string, Intl.NumberFormat>();

/**
 * The display formatter of a locale and a set of bounds, cached.
 *
 * @param locale - The locale.
 * @param min - The minimum fraction digits.
 * @param max - The maximum fraction digits.
 * @param percent - Whether it formats a percentage.
 * @param grouping - Whether it writes group separators.
 * @returns The formatter.
 */
function displayFormatterFor(
  locale: string,
  min: number,
  max: number,
  percent: boolean,
  grouping: boolean,
): Intl.NumberFormat {
  const key = `${locale}|${min}|${max}|${percent ? 'p' : ''}|${grouping ? '' : 'u'}`;
  let formatter = displayFormatterCache.get(key);
  if (formatter === undefined) {
    formatter = new Intl.NumberFormat(locale, {
      minimumFractionDigits: min,
      maximumFractionDigits: max,
      ...(percent ? { style: 'percent' as const } : {}),
      // Grouping on is Intl's default ('auto'), which follows the locale's
      // minimum grouping digits; only "off" is ever stated.
      ...(grouping ? {} : { useGrouping: false }),
    });
    displayFormatterCache.set(key, formatter);
  }
  return formatter;
}

/**
 * Formats a number for display in the given locale ('' for
 * null/undefined/''). Fraction digits are bounded by
 * `minDecimals`/`maxDecimals` (default 0…20, i.e. "as many as needed");
 * in percent mode the value is a fraction and the bounds apply to the
 * displayed percent digits. `grouping: false` leaves the group separators
 * out. The text carries no bidirectional control character: where ICU
 * puts a direction mark beside a minus or a percent sign, it is removed,
 * so a container's markup (`dir`) decides the order.
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
  return tmStripBidiControls(
    displayFormatterFor(
      locale,
      min,
      max,
      options?.percent === true,
      options?.grouping !== false,
    ).format(numeric),
  );
}
