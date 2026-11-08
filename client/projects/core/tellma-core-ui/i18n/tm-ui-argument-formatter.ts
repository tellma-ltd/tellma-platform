// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { InjectionToken } from '@angular/core';

import { ɵtmIntl, ɵtmWarnOnce } from './tm-intl';

/** The ICU argument types a {@link TmUiArgumentFormatter} formats. */
export type TmUiArgumentType = 'number' | 'date' | 'time';

/**
 * Formats one typed ICU argument — `{n, number}`, `{d, date, medium}`,
 * `{t, time, short}`, and a plural's `#` (as a style-less number) — in the
 * formatting locale. `style` is the argument's style text, trimmed (`''`
 * when the message gives none).
 */
export type TmUiArgumentFormatter = (
  type: TmUiArgumentType,
  value: unknown,
  style: string,
  locale: string,
) => string;

/**
 * The default argument formatter. Numbers format with `Intl.NumberFormat`
 * in the locale, with the `integer`, `percent` and `currency:XXX` styles
 * (any other style formats as a plain number, with a development warning).
 * Dates and times render as ISO 8601 — `toISOString()` for a `Date`, the
 * raw value otherwise — never in the device's own locale or time zone: a
 * calendar- and zone-aware rendering is the app's to provide.
 */
export function tmDefaultUiArgumentFormatter(
  type: TmUiArgumentType,
  value: unknown,
  style: string,
  locale: string,
): string {
  if (type !== 'number') {
    return value instanceof Date && !Number.isNaN(value.getTime())
      ? value.toISOString()
      : String(value);
  }
  const currency = /^currency:\s*([a-z]{3})$/i.exec(style);
  let options: Intl.NumberFormatOptions = {};
  if (style === 'integer') {
    options = { maximumFractionDigits: 0 };
  } else if (style === 'percent') {
    options = { style: 'percent' };
  } else if (currency) {
    options = { style: 'currency', currency: currency[1].toUpperCase() };
  } else if (style !== '') {
    ɵtmWarnOnce(`Unsupported ICU number style "${style}".`);
    style = '';
  }
  return ɵtmIntl(`n${style}`, locale, (tag) => new Intl.NumberFormat(tag, options)).format(
    typeof value === 'bigint' ? value : Number(value),
  );
}

/**
 * The formatter every library message formats its typed arguments with.
 * Defaults to {@link tmDefaultUiArgumentFormatter}. An app replaces it to
 * render dates and times in its own calendar and time zone, delegating the
 * types it does not handle to the default.
 *
 * @example
 * ```ts
 * {
 *   provide: TM_UI_ARGUMENT_FORMATTER,
 *   useFactory: () => {
 *     const l10n = inject(TmL10n);
 *     return (type, value, style, locale) =>
 *       type === 'date'
 *         ? l10n.formatDate(String(value))
 *         : tmDefaultUiArgumentFormatter(type, value, style, locale);
 *   },
 * }
 * ```
 */
export const TM_UI_ARGUMENT_FORMATTER = new InjectionToken<TmUiArgumentFormatter>(
  'TM_UI_ARGUMENT_FORMATTER',
  { providedIn: 'root', factory: () => tmDefaultUiArgumentFormatter },
);
