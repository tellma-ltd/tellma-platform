// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { IslamicUmalquraCalendar } from '@internationalized/date';

import { ɵtmAdaptCalendar, type TmCalendar } from '@tellma/core-ui/l10n';

let singleton: TmCalendar | undefined;

/**
 * The Umm al-Qura display calendar (the Saudi civil calendar), backed by
 * the ICU-ported almanac tables. Register it as the app-ambient calendar
 * via `provideTmCalendar(tmUmalquraCalendar())`, or pass it per instance;
 * the backing values stay ISO (proleptic Gregorian) either way.
 *
 * Accuracy window: the underlying tables cover AH 1300–1600 (≈ 1882–2174
 * CE); outside that window the implementation degrades to the arithmetic
 * Islamic calendar, silently and continuously — everyday ERP dates live
 * comfortably inside the window, and the ISO model value is exact
 * regardless. Years are Anno Hegirae (AH); month and era names render
 * from Intl in the active UI language.
 */
export function tmUmalquraCalendar(): TmCalendar {
  singleton ??= ɵtmAdaptCalendar(new IslamicUmalquraCalendar(), 'islamic-umalqura');
  return singleton;
}
