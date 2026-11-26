// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, inject, signal, type Signal } from '@angular/core';
import { TM_NOW, TmLaunchCache, TmLocalization, TmSession } from '@tellma/core';
import { TmL10n } from '@tellma/core-ui';

import { tmPrepareDashboards } from './tm-dashboard-setup';

// The home page's greeting and date line (spec 0035 §15.2). The greeting
// follows the hour in the display time zone, the name is the user's whole
// name in the active content slot, isolated in a <bdi>, and the date is
// today's in that zone and the user's calendar. One timer re-renders at the
// next time-of-day boundary or midnight in the zone, and a return to a
// hidden tab re-renders too, since hidden tabs throttle timers. A language,
// calendar or zone change re-renders in place, and the one-line heading
// moves nothing.

/** The part of the day a greeting names. */
export type TmTimeOfDay = 'morning' | 'afternoon' | 'evening';

/** The hours at which the greeting changes, and midnight, where the date does. */
const BOUNDARIES = [0, 5, 12, 17];

/** A character no translation holds, which marks where the name goes in the greeting. */
const NAME_MARK = '';

/**
 * The hour, minute, second and millisecond of an instant in a time zone,
 * on a 24-hour clock.
 *
 * @param now - The instant, in epoch milliseconds.
 * @param timeZone - The IANA zone.
 * @returns The time of day, in milliseconds since the zone's midnight.
 */
function timeOfDayMs(now: number, timeZone: string): number {
  const parts = new Intl.DateTimeFormat('en-US', { timeZone, hourCycle: 'h23', hour: 'numeric', minute: 'numeric', second: 'numeric' }).formatToParts(now);
  const part = (type: Intl.DateTimeFormatPartTypes): number => Number(parts.find((candidate) => candidate.type === type)?.value ?? 0);
  return ((part('hour') * 60 + part('minute')) * 60 + part('second')) * 1000 + (((now % 1000) + 1000) % 1000);
}

/**
 * The part of the day an hour falls in: morning from 05:00 to 11:59,
 * afternoon from 12:00 to 16:59, evening otherwise.
 *
 * @param hour - The hour, 0 to 23.
 * @returns The part of the day.
 */
export function tmTimeOfDay(hour: number): TmTimeOfDay {
  return hour >= 5 && hour < 12 ? 'morning' : hour >= 12 && hour < 17 ? 'afternoon' : 'evening';
}

/**
 * How long until the next time-of-day boundary or midnight in a zone.
 *
 * @param now - The instant, in epoch milliseconds.
 * @param timeZone - The IANA zone.
 * @returns The delay in milliseconds, at least one.
 */
function untilNextBoundary(now: number, timeZone: string): number {
  const at = timeOfDayMs(now, timeZone);
  const next = BOUNDARIES.map((hour) => hour * 3_600_000).find((boundary) => boundary > at) ?? 24 * 3_600_000;
  return Math.max(1, next - at);
}

/** The home page's greeting, the page's title, and today's date under it. */
@Component({
  selector: 'tm-home-greeting',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The page's title, the focus target on arrival: the greeting, the name isolated within it. -->
    <h1 class="tm-home-greeting__title" tmPageTitle>
      <span class="tm-home-greeting__text"
        >{{ greeting()[0] }}<bdi>{{ name() }}</bdi>{{ greeting()[1] }}</span
      >
    </h1>
    <!-- Today, in the display zone and the user's calendar. -->
    <p class="tm-home-greeting__date">{{ date() }}</p>
  `,
  host: { class: 'tm-home-greeting' },
})
export class TmHomeGreeting {
  readonly #clock = inject(TM_NOW);
  readonly #l10n = inject(TmLocalization);
  readonly #formats = inject(TmL10n);
  readonly #launch = inject(TmLaunchCache);
  readonly #session = inject(TmSession);

  /** The instant the greeting reads, set again at each boundary and on each return to the tab. */
  readonly #now = signal(this.#clock());

  /** The part of the day, in the display zone. */
  protected readonly timeOfDay: Signal<TmTimeOfDay> = computed(() =>
    tmTimeOfDay(Math.floor(timeOfDayMs(this.#now(), this.#l10n.timeZone()) / 3_600_000)),
  );

  /**
   * The user's whole name in the active content slot, else the record's
   * first; while a cold launch's sync runs, the profile cookie's name.
   */
  protected readonly name: Signal<string> = computed(() => {
    const user = this.#launch.docs.me()?.user;
    if (user === undefined) {
      return this.#session.profile()?.name ?? '';
    }
    const slot = this.#l10n.contentSlot();
    const name = slot === 3 ? user.name3 : slot === 2 ? user.name2 : user.name;
    return name?.trim() || user.name;
  });

  /** The greeting's text before and after the name. */
  protected readonly greeting: Signal<readonly [string, string]> = computed(() => {
    const text = this.#l10n.ɵtext(`tm.home.greeting.${this.timeOfDay()}`, { name: NAME_MARK })();
    const at = text.indexOf(NAME_MARK);
    return at < 0 ? [text, ''] : [text.slice(0, at), text.slice(at + NAME_MARK.length)];
  });

  /** Today's date, with its weekday, in the display zone and the user's calendar. */
  protected readonly date: Signal<string> = computed(() =>
    this.#formats.formatDateTime(new Date(this.#now()).toISOString(), { dateStyle: 'weekday', timeStyle: 'none', zoneLabel: 'none' }),
  );

  /** Registers the strings, and keeps the greeting and the date current. */
  constructor() {
    tmPrepareDashboards();

    // --- One timer, to the next boundary in the display zone; a zone change sets it again ---
    effect((onCleanup) => {
      const delay = untilNextBoundary(this.#now(), this.#l10n.timeZone());
      const timer = setTimeout(() => this.#now.set(this.#clock()), delay);
      onCleanup(() => clearTimeout(timer));
    });

    // --- A return to the tab reads the clock again, since a hidden tab's timers are throttled ---
    const document = inject(DOCUMENT);
    const visible = (): void => {
      if (document.visibilityState === 'visible') {
        this.#now.set(this.#clock());
      }
    };
    document.addEventListener('visibilitychange', visible);
    inject(DestroyRef).onDestroy(() => document.removeEventListener('visibilitychange', visible));
  }
}
