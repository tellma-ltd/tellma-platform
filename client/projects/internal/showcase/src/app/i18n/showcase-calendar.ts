// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, Injectable, signal, type Signal } from '@angular/core';

import { tmEthiopicCalendar } from '@tellma/core-ui/calendar-ethiopic';
import { tmUmalquraCalendar } from '@tellma/core-ui/calendar-umalqura';
import { tmGregorianCalendar, type TmCalendar } from '@tellma/core-ui/l10n';

/** The calendars the shell can switch between. */
export type ShowcaseCalendarId = 'gregory' | 'islamic-umalqura' | 'ethiopic';

/**
 * The shell's ambient-calendar choice, backing `TM_CALENDAR` for the whole
 * app. It exists so every story — not only the date picker's — can be seen
 * under a non-Gregorian calendar, which is where display and parsing
 * diverge most.
 *
 * The packs are instantiated once and kept: rebuilding one per switch
 * would hand every consumer a new object identity and force a re-render
 * of things that did not change.
 */
@Injectable({ providedIn: 'root' })
export class ShowcaseCalendar {
  private readonly packs: Record<ShowcaseCalendarId, TmCalendar> = {
    gregory: tmGregorianCalendar(),
    'islamic-umalqura': tmUmalquraCalendar(),
    ethiopic: tmEthiopicCalendar(),
  };

  /** The selected calendar id. */
  readonly id = signal<ShowcaseCalendarId>('gregory');

  /** The selected calendar, for `TM_CALENDAR`. */
  readonly calendar: Signal<TmCalendar> = computed(() => this.packs[this.id()]);
}
