// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/** Regions whose week starts on Saturday (a compact CLDR-derived fallback). */
const SATURDAY_REGIONS = new Set([
  'AE', 'AF', 'BH', 'DJ', 'DZ', 'EG', 'IQ', 'IR', 'JO', 'KW', 'LY', 'OM', 'QA', 'SD', 'SY',
]);

/**
 * Regions whose week starts on Sunday (a compact CLDR-derived fallback).
 * Saudi Arabia and Yemen sit here per current CLDR (the 2013 weekend
 * reform), not with the Saturday group.
 */
const SUNDAY_REGIONS = new Set([
  'AG', 'AS', 'BD', 'BR', 'BS', 'BT', 'BW', 'BZ', 'CA', 'CO', 'DM', 'DO', 'ET', 'GT', 'GU', 'HK',
  'HN', 'ID', 'IL', 'IN', 'JM', 'JP', 'KE', 'KH', 'KR', 'LA', 'MH', 'MM', 'MO', 'MT', 'MX', 'MZ',
  'NI', 'NP', 'PA', 'PE', 'PH', 'PK', 'PR', 'PT', 'PY', 'SA', 'SG', 'SV', 'TH', 'TT', 'TW', 'UM',
  'US', 'VE', 'VI', 'WS', 'YE', 'ZA', 'ZW',
]);

const cache = new Map<string, number>();

/**
 * The locale's first day of the week, ISO-numbered (1 = Monday … 7 =
 * Sunday) — the calendar day-grid's column order. Resolved via
 * `Intl.Locale`'s week info where the engine provides it, else a compact
 * region fallback table (Saturday for most MENA regions, Sunday for the
 * Americas and much of Asia, Monday otherwise).
 */
export function tmFirstDayOfWeek(locale: string): number {
  let first = cache.get(locale);
  if (first !== undefined) {
    return first;
  }
  let resolved: number | undefined;
  let region: string | undefined;
  try {
    const intlLocale = new Intl.Locale(locale);
    type WeekInfo = { readonly firstDay?: number };
    type WithWeekInfo = Intl.Locale & {
      getWeekInfo?: () => WeekInfo;
      readonly weekInfo?: WeekInfo;
    };
    const withInfo = intlLocale as WithWeekInfo;
    resolved = withInfo.getWeekInfo?.().firstDay ?? withInfo.weekInfo?.firstDay;
    region = intlLocale.region ?? intlLocale.maximize().region;
  } catch {
    // An unparsable tag falls to the Monday default below.
  }
  if (resolved === undefined && region !== undefined) {
    resolved = SATURDAY_REGIONS.has(region) ? 6 : SUNDAY_REGIONS.has(region) ? 7 : 1;
  }
  first = resolved !== undefined && resolved >= 1 && resolved <= 7 ? resolved : 1;
  cache.set(locale, first);
  return first;
}
