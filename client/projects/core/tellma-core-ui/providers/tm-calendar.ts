// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  InjectionToken,
  makeEnvironmentProviders,
  signal,
  type EnvironmentProviders,
  type Signal,
} from '@angular/core';

import { tmGregorianCalendar, type TmCalendar } from '@tellma/core-ui/l10n';

/**
 * The app-ambient DISPLAY calendar as a signal — dates re-render in place
 * when it switches; the backing values stay ISO (proleptic Gregorian)
 * regardless. Defaults to the built-in Gregorian calendar; opt-in
 * calendars ship as their own entry points and register through
 * {@link provideTmCalendar}.
 */
export const TM_CALENDAR = new InjectionToken<Signal<TmCalendar>>('TM_CALENDAR', {
  providedIn: 'root',
  factory: () => signal(tmGregorianCalendar()).asReadonly(),
});

/**
 * Nominates the app-ambient display calendar: a fixed calendar, or a
 * signal for apps that switch calendars at runtime (a user preference).
 */
export function provideTmCalendar(
  calendar: TmCalendar | Signal<TmCalendar>,
): EnvironmentProviders {
  return makeEnvironmentProviders([
    {
      provide: TM_CALENDAR,
      useValue: typeof calendar === 'function' ? calendar : signal(calendar).asReadonly(),
    },
  ]);
}
