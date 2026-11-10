// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  computed,
  DestroyRef,
  DOCUMENT,
  inject,
  Injectable,
  Injector,
  signal,
  type Signal,
} from '@angular/core';

import {
  tmFormatDate,
  tmFormatDateTime,
  tmFormatMonthName,
  tmFormatNumber,
  tmFormatRelativeTime,
  tmFormatTime,
  tmFormatWeekdayName,
  tmFormatZoneOffset,
  ɵtmShowsZoneLabel,
  type TmCalendar,
  type TmDateFormatOptions,
  type TmDateStyle,
  type TmNumberFormatOptions,
  type TmTimeStyle,
} from '@tellma/core-ui/l10n';

import { TM_ACTIVE_LOCALE } from '../i18n/tm-active-locale';
import { TM_UI_TRANSLATE } from '../i18n/tm-ui-translate';
import { TM_CALENDAR } from '../providers/tm-calendar';
import { TM_DEVICE_TIME_ZONE, TM_NOW, TM_TIME_ZONE } from '../providers/tm-time-zone';

/**
 * When a formatted date-time or time carries the display zone's GMT
 * offset ("GMT+3"):
 *
 * - `auto`: when the display zone's offset at the instant differs from
 *   the device's — a reader in another zone is told whose clock it is.
 * - `column`: for the cells of a column whose header shows
 *   `TmL10n.zoneLabel()` while `TmL10n.zoneMismatch()` is true. Under such
 *   a labelled header a cell stays bare, unless its own offset differs
 *   from the header's (a daylight-saving change between rows); under a
 *   bare header it labels as `auto` does.
 * - `none`: never.
 */
export type TmZoneLabelMode = 'auto' | 'column' | 'none';

/**
 * The reactive formatting facade: the pure `@tellma/core-ui/l10n` functions
 * bound to the app-ambient locale, calendar and time-zone signals, so
 * calling a method inside a `computed()` or a template makes the caller
 * re-render when the user switches language, calendar or time zone at
 * runtime.
 *
 * No pipes on purpose: a pure pipe would not re-evaluate on a locale
 * change and an impure one re-runs every change-detection pass — the
 * signal-bound facade gives the reactivity without the cost. For
 * locale-explicit work (exports, tests) call the `l10n` functions
 * directly.
 *
 * Formatted text carries no bidirectional control character: markup (a
 * `dir` attribute, `<bdi>`) orders it.
 */
@Injectable({ providedIn: 'root' })
export class TmL10n {
  private readonly injector = inject(Injector);
  /** The clock (see `TM_NOW`). */
  private readonly now = inject(TM_NOW);
  /**
   * Bumped whenever the page is hidden or shown again. No browser event
   * reports a clock or daylight-saving change, so returning to the page
   * is when everything that compares offsets "now" is computed afresh.
   */
  private readonly revisit = signal(0);
  /** The translated `quarter` pattern, resolved on first use (see {@link quarterPattern}). */
  private translatedQuarter: Signal<string> | undefined;

  /** The app-ambient formatting locale (see `TM_ACTIVE_LOCALE`). */
  readonly locale: Signal<string> = inject(TM_ACTIVE_LOCALE);
  /** The app-ambient display calendar (see `TM_CALENDAR`). */
  readonly calendar: Signal<TmCalendar> = inject(TM_CALENDAR);
  /** The display time zone instants are shown in (see `TM_TIME_ZONE`). */
  readonly timeZone: Signal<string> = inject(TM_TIME_ZONE);
  /** The device's time zone (see `TM_DEVICE_TIME_ZONE`). */
  readonly deviceTimeZone: Signal<string> = inject(TM_DEVICE_TIME_ZONE);

  /**
   * Whether the display zone's offset differs from the device's right
   * now — whether a column of instants labels its header. Compared by
   * offset, never by zone id: `Asia/Kuwait` and `Asia/Riyadh` never
   * mismatch. Recomputed when either zone changes and when the page is
   * hidden or shown again.
   */
  readonly zoneMismatch: Signal<boolean> = computed(() =>
    ɵtmShowsZoneLabel(this.revisitedNow(), this.timeZone(), this.deviceTimeZone()),
  );

  constructor() {
    const document = inject(DOCUMENT);
    const onVisibilityChange = (): void => this.revisit.update((count) => count + 1);
    document.addEventListener('visibilitychange', onVisibilityChange);
    inject(DestroyRef).onDestroy(() =>
      document.removeEventListener('visibilitychange', onVisibilityChange),
    );
  }

  /**
   * Formats a number in the active locale — reading `locale` first, so a
   * reactive caller re-renders on language switch. Delegates to
   * `tmFormatNumber`.
   */
  formatNumber(value: unknown, options?: TmNumberFormatOptions): string {
    return tmFormatNumber(value, this.locale(), options);
  }

  /**
   * Formats an ISO date in the active locale and the ambient display
   * calendar (an explicit `calendar` option overrides the ambient one) —
   * reading both signals first, so a reactive caller re-renders on either
   * switch. The `quarter` style is written through the translated pattern
   * (the `l10n.quarter` library string) unless `quarterPattern` is given.
   * Delegates to `tmFormatDate`.
   */
  formatDate(iso: string | null | undefined, options?: TmDateFormatOptions): string {
    return tmFormatDate(iso, this.locale(), {
      calendar: options?.calendar ?? this.calendar(),
      dateStyle: options?.dateStyle,
      quarterPattern: this.quarterPattern(options?.dateStyle, options?.quarterPattern),
    });
  }

  /**
   * The display zone's GMT offset at `instant` (an ISO 8601 instant;
   * default: now) when it differs from the device zone's offset there —
   * "GMT+3" — else `''`. A column of instants shows `zoneLabel()` in its
   * header while {@link zoneMismatch} is true.
   */
  zoneLabel(instant?: string | null): string {
    const locale = this.locale();
    const timeZone = this.timeZone();
    const at = instant || this.revisitedNow();
    return ɵtmShowsZoneLabel(at, timeZone, this.deviceTimeZone())
      ? tmFormatZoneOffset(at, locale, timeZone)
      : '';
  }

  /**
   * Formats an instant (an ISO 8601 string with `Z` or a `±hh:mm` offset)
   * as a date and a time in the active locale, the ambient calendar and
   * the display time zone — "20/10/2026, 10:42" — reading the three
   * signals first, so a reactive caller re-renders on any switch.
   * `timeStyle: 'none'` gives the instant's date in the zone alone.
   *
   * `zoneLabel` (default `auto`) decides whether the zone's GMT offset is
   * appended: "20/10/2026, 10:42 GMT+3" for a reader whose device is in
   * another offset. Delegates to `tmFormatDateTime`.
   */
  formatDateTime(
    instant: string | null | undefined,
    options?: {
      readonly dateStyle?: TmDateStyle;
      readonly timeStyle?: TmTimeStyle | 'none';
      readonly zoneLabel?: TmZoneLabelMode;
    },
  ): string {
    const locale = this.locale();
    const calendar = this.calendar();
    const timeZone = this.timeZone();
    return tmFormatDateTime(instant, locale, {
      timeZone,
      calendar,
      dateStyle: options?.dateStyle,
      timeStyle: options?.timeStyle,
      zoneLabel: this.labels(instant, timeZone, options?.zoneLabel),
      quarterPattern: this.quarterPattern(options?.dateStyle),
    });
  }

  /**
   * Formats an instant's time of day in the active locale and the display
   * time zone — "10:42" — reading both signals first. `zoneLabel` (default
   * `auto`) decides whether the zone's GMT offset is appended, as in
   * {@link formatDateTime}. Delegates to `tmFormatTime`.
   */
  formatTime(
    instant: string | null | undefined,
    options?: { readonly timeStyle?: TmTimeStyle; readonly zoneLabel?: TmZoneLabelMode },
  ): string {
    const locale = this.locale();
    const timeZone = this.timeZone();
    return tmFormatTime(instant, locale, {
      timeZone,
      timeStyle: options?.timeStyle,
      zoneLabel: this.labels(instant, timeZone, options?.zoneLabel),
    });
  }

  /**
   * Formats how far an instant lies from `now` (epoch milliseconds, the
   * caller's clock) in the active locale — "2 min ago", "yesterday" —
   * counting calendar days in the display time zone and falling back to
   * the date in the ambient calendar from seven days out. Never labelled
   * with a zone. Delegates to `tmFormatRelativeTime`.
   */
  formatRelativeTime(
    instant: string | null | undefined,
    options: { readonly now: number; readonly style?: 'long' | 'short' | 'narrow' },
  ): string {
    return tmFormatRelativeTime(instant, this.locale(), {
      now: options.now,
      timeZone: this.timeZone(),
      calendar: this.calendar(),
      style: options.style,
    });
  }

  /**
   * The name of a month from its 1-based number, in the active locale and
   * the ambient display calendar (an explicit `calendar` option overrides
   * it). Delegates to `tmFormatMonthName`.
   */
  formatMonthName(month: number, options?: { readonly calendar?: TmCalendar }): string {
    return tmFormatMonthName(month, this.locale(), {
      calendar: options?.calendar ?? this.calendar(),
    });
  }

  /**
   * The name of a day of the week from its ISO number (1 is Monday, 7 is
   * Sunday), in the active locale. Delegates to `tmFormatWeekdayName`.
   */
  formatWeekdayName(weekday: number): string {
    return tmFormatWeekdayName(weekday, this.locale());
  }

  /**
   * The clock's reading, as a reactive read: the caller is computed
   * afresh whenever the page is hidden or shown again.
   */
  private revisitedNow(): number {
    this.revisit();
    return this.now();
  }

  /** Whether `instant`, shown in `timeZone`, carries the zone label under `mode`. */
  private labels(
    instant: string | null | undefined,
    timeZone: string,
    mode?: TmZoneLabelMode,
  ): boolean {
    return ɵtmShowsZoneLabel(
      instant,
      timeZone,
      this.deviceTimeZone(),
      mode,
      // A column's header is labelled against the clock.
      mode === 'column' ? this.revisitedNow() : undefined,
    );
  }

  /**
   * The pattern the `quarter` style is written through: the caller's, else
   * the translated library string — read as a signal, so a language
   * switch re-renders. `undefined` for every other style.
   */
  private quarterPattern(style: TmDateStyle | undefined, explicit?: string): string | undefined {
    if (style !== 'quarter' || explicit !== undefined) {
      return explicit;
    }
    // Resolved on first use, never in the constructor: an app's message
    // argument formatter may itself inject this service, and the
    // translate function injects that formatter.
    this.translatedQuarter ??= this.injector.get(TM_UI_TRANSLATE)('l10n.quarter', {
      // The placeholders stand for themselves: the message resolves to the
      // pattern, which the pure formatter then fills.
      quarter: '{quarter}',
      year: '{year}',
    });
    return this.translatedQuarter();
  }
}
