// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, inject, Injectable, signal, type Signal } from '@angular/core';
import { email, form, FormField, required } from '@angular/forms/signals';

import { TM_DEVICE_TIME_ZONE, TM_NOW, TM_TIME_ZONE, TmL10n } from '@tellma/core-ui';
import { TmFormField } from '@tellma/core-ui/form-field';
import { TmInput } from '@tellma/core-ui/input';
import { tmFormatZoneName, type TmDateStyle } from '@tellma/core-ui/l10n';
import { TmOption, TmSelect } from '@tellma/core-ui/select';

/** Where the story's fake clock starts: 20 October 2026, 07:42 UTC. */
const STORY_START = Date.UTC(2026, 9, 20, 7, 42);

/**
 * The story's own display zone, device zone and clock. A real app reads
 * the device's zone and `Date.now()`; the story fixes all three so every
 * text on the page is the same on any machine, at any hour — and lets the
 * reader move them to see the texts follow.
 */
@Injectable()
export class I18nStoryClock {
  /** The display zone instants are shown in. */
  readonly timeZone = signal('Asia/Riyadh');
  /** The zone the reader's device is pretended to be in. */
  readonly deviceTimeZone = signal('Europe/London');
  /** The fake clock's reading, in epoch milliseconds. */
  readonly now = signal(STORY_START);
}

/** One sample instant of the story's table. */
interface StoryInstant {
  /** A stable id, for test ids. */
  readonly id: string;
  /** The instant, in its wire form. */
  readonly instant: string;
}

/**
 * Locale-pack and formatting demo host: runtime language switching
 * re-renders already-visible library strings; Arabic content pulls the
 * pack's font on demand (unicode-range); and instants, periods and names
 * format in the active language, the ambient calendar and a display time
 * zone, over a fake clock.
 *
 * The formatting half runs on the story's own `TmL10n`, bound to the
 * story's zones and clock, so nothing here depends on the machine.
 */
@Component({
  imports: [TmInput, TmFormField, TmSelect, TmOption, FormField],
  providers: [
    I18nStoryClock,
    // The facade is provided here so it reads the story's zones and clock.
    TmL10n,
    { provide: TM_TIME_ZONE, useFactory: () => inject(I18nStoryClock).timeZone },
    { provide: TM_DEVICE_TIME_ZONE, useFactory: () => inject(I18nStoryClock).deviceTimeZone },
    {
      provide: TM_NOW,
      useFactory: () => {
        const clock = inject(I18nStoryClock);
        // Read as a signal, so moving the fake clock re-renders the labels.
        return () => clock.now();
      },
    },
  ],
  template: `
    <h2>i18n / locale packs</h2>

    <p>Switch the language from the header — visible strings re-render live.</p>

    <div class="grid">
      <tm-form-field label="Email" data-testid="ff-email">
        <input tmInput [formField]="f.email" data-testid="input-email" />
      </tm-form-field>

      <tm-form-field label="Status" data-testid="ff-status">
        <tm-select [(value)]="status" data-testid="select-status">
          <tm-option [value]="1" label="نشط">نشط</tm-option>
          <tm-option [value]="2" label="موقوف">موقوف</tm-option>
        </tm-select>
      </tm-form-field>
    </div>

    <!-- Instants: a date-time, a time and a relative time over a fake
         clock, in a display zone, labelled against a device zone. -->
    <h3>Instants and time zones</h3>

    <p>
      Instants show in the display zone. When the device sits at another offset, a time names the
      display zone's offset — compared at the instant shown, never by zone name.
    </p>

    <div class="controls">
      <label>
        Display zone
        <select data-testid="i18n-zone" (change)="clock.timeZone.set(valueOf($event))">
          @for (zone of zones; track zone) {
            <option [value]="zone" [selected]="zone === clock.timeZone()">{{ zone }}</option>
          }
        </select>
      </label>
      <label>
        Device zone
        <select data-testid="i18n-device-zone" (change)="clock.deviceTimeZone.set(valueOf($event))">
          @for (zone of zones; track zone) {
            <option [value]="zone" [selected]="zone === clock.deviceTimeZone()">{{ zone }}</option>
          }
        </select>
      </label>
    </div>

    <div class="controls">
      <span>
        Fake clock:
        <strong data-testid="i18n-now">{{
          l10n.formatDateTime(nowInstant(), {
            dateStyle: 'weekday',
            timeStyle: 'medium',
            zoneLabel: 'none',
          })
        }}</strong>
      </span>
      <button type="button" data-testid="i18n-advance-minute" (click)="advance(60_000)">
        +1 min
      </button>
      <button type="button" data-testid="i18n-advance-hour" (click)="advance(3_600_000)">
        +1 hour
      </button>
      <button type="button" data-testid="i18n-advance-day" (click)="advance(86_400_000)">
        +1 day
      </button>
      <button type="button" data-testid="i18n-reset-clock" (click)="resetClock()">Reset</button>
    </div>

    <dl class="facts">
      <dt>Zones mismatch now</dt>
      <dd data-testid="i18n-zone-mismatch">{{ l10n.zoneMismatch() ? 'yes' : 'no' }}</dd>
      <dt>Zone label now</dt>
      <dd data-testid="i18n-zone-label">{{ l10n.zoneLabel() || '—' }}</dd>
      <dt>Display zone name</dt>
      <dd data-testid="i18n-zone-name">{{ zoneName() }}</dd>
    </dl>

    <table class="instants" data-testid="i18n-instants">
      <thead>
        <tr>
          <th scope="col">Instant</th>
          <th scope="col">Date-time</th>
          <!-- A column of times: the header carries the label while the
               zones mismatch, and its cells stay bare under it. -->
          <th scope="col" data-testid="i18n-time-header">
            Time
            @if (l10n.zoneMismatch()) {
              <span class="zone">{{ l10n.zoneLabel() }}</span>
            }
          </th>
          <th scope="col">Relative</th>
          <th scope="col">Date</th>
        </tr>
      </thead>
      <tbody>
        @for (row of instants; track row.id) {
          <tr [attr.data-testid]="'i18n-instant-' + row.id">
            <td>
              <code>{{ row.instant }}</code>
            </td>
            <td data-cell="date-time">{{ l10n.formatDateTime(row.instant) }}</td>
            <td data-cell="time">{{ l10n.formatTime(row.instant, { zoneLabel: 'column' }) }}</td>
            <td data-cell="relative">
              {{ l10n.formatRelativeTime(row.instant, { now: clock.now() }) }}
            </td>
            <td data-cell="date">
              {{ l10n.formatDateTime(row.instant, { dateStyle: 'weekday', timeStyle: 'none' }) }}
            </td>
          </tr>
        }
      </tbody>
    </table>

    <!-- Periods and names: the date styles of the clock's date, and the
         ambient calendar's month and weekday names. -->
    <h3>Periods and names</h3>

    <p>
      The clock's date in every date style, then the month and weekday names — in the active
      language and the calendar picked in the header.
    </p>

    <dl class="facts">
      @for (style of dateStyles; track style) {
        <dt>{{ style }}</dt>
        <dd [attr.data-testid]="'i18n-date-' + style">
          {{ l10n.formatDateTime(nowInstant(), { dateStyle: style, timeStyle: 'none' }) }}
        </dd>
      }
    </dl>

    <ol class="names" data-testid="i18n-month-names">
      @for (month of months(); track month) {
        <li>{{ l10n.formatMonthName(month) }}</li>
      }
    </ol>

    <ol class="names" data-testid="i18n-weekday-names">
      @for (weekday of weekdays; track weekday) {
        <li>{{ l10n.formatWeekdayName(weekday) }}</li>
      }
    </ol>

    <!-- Numbers: grouping, and direction set by markup. -->
    <h3>Numbers</h3>

    <p>
      Formatted text carries no invisible direction marks: a left-to-right run keeps a minus at the
      left of its digits, in a right-to-left page too.
    </p>

    <dl class="facts">
      <dt>Grouped</dt>
      <dd>
        <span dir="ltr" data-testid="i18n-number-grouped">{{ l10n.formatNumber(-1234567.5) }}</span>
      </dd>
      <dt>Ungrouped (a year)</dt>
      <dd>
        <span dir="ltr" data-testid="i18n-number-ungrouped">{{
          l10n.formatNumber(2026, { grouping: false })
        }}</span>
      </dd>
    </dl>
  `,
  styles: `
    .grid {
      display: grid;
      gap: 16px;
      max-inline-size: 360px;
    }
    .controls {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 12px;
      margin-block: 12px;
    }
    .controls label {
      display: inline-flex;
      align-items: center;
      gap: 6px;
    }
    .facts {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 4px 16px;
      margin-block: 12px;
    }
    .facts dt {
      color: var(--text-secondary);
    }
    .facts dd {
      margin: 0;
    }
    .instants {
      border-collapse: collapse;
      margin-block: 12px;
    }
    .instants th,
    .instants td {
      padding: 4px 12px;
      border-block-end: 1px solid var(--border-default);
      text-align: start;
    }
    .zone {
      color: var(--text-secondary);
      font-weight: normal;
    }
    .names {
      display: flex;
      flex-wrap: wrap;
      gap: 4px 20px;
      margin-block: 12px;
      padding-inline-start: 24px;
    }
  `,
})
export class I18nStory {
  /** The story's zones and fake clock. */
  protected readonly clock = inject(I18nStoryClock);
  /** The formatting facade, bound to the story's zones and clock. */
  protected readonly l10n = inject(TmL10n);

  /** The selected status of the select demo. */
  readonly status = signal<number | undefined>(undefined);

  /** The model of the validation demo. */
  readonly model = signal({ email: '' });
  /** The form of the validation demo. */
  readonly f = form(this.model, (p) => {
    required(p.email);
    email(p.email);
  });

  /** The zones either selector offers. */
  protected readonly zones: readonly string[] = [
    'UTC',
    'Asia/Riyadh',
    'Asia/Kuwait',
    'Africa/Addis_Ababa',
    'Asia/Beirut',
    'Asia/Kolkata',
    'Europe/London',
  ];

  /**
   * The sample instants, around where the fake clock starts (07:42 UTC on
   * 20 October 2026), written with the offsets a server might send.
   */
  protected readonly instants: readonly StoryInstant[] = [
    { id: 'seconds', instant: '2026-10-20T07:41:30Z' },
    { id: 'minutes', instant: '2026-10-20T10:40:00+03:00' },
    { id: 'hours', instant: '2026-10-20T04:12:00Z' },
    // Past midnight in Riyadh, before it in UTC: a day's difference by zone.
    { id: 'yesterday', instant: '2026-10-18T21:30:00Z' },
    { id: 'days', instant: '2026-10-15T09:00:00Z' },
    { id: 'weeks', instant: '2026-09-28T12:00:00+03:00' },
    { id: 'soon', instant: '2026-10-20T07:47:00Z' },
    // Winter: Beirut is an hour behind Riyadh then, and level with it now.
    { id: 'winter', instant: '2026-01-15T09:00:00Z' },
  ];

  /** Every date style, in the order the story lists them. */
  protected readonly dateStyles: readonly TmDateStyle[] = [
    'numeric',
    'medium',
    'long',
    'weekday',
    'month',
    'quarter',
    'year',
  ];

  /** The ISO weekday numbers: 1 is Monday, 7 is Sunday. */
  protected readonly weekdays: readonly number[] = [1, 2, 3, 4, 5, 6, 7];

  /** The fake clock's reading, as an instant. */
  protected readonly nowInstant: Signal<string> = computed(() =>
    new Date(this.clock.now()).toISOString(),
  );

  /** The month numbers of the ambient calendar's current year. */
  protected readonly months: Signal<readonly number[]> = computed(() => {
    const calendar = this.l10n.calendar();
    const year = calendar.toParts(this.nowInstant().slice(0, 10)).year;
    return Array.from({ length: calendar.monthsInYear(year) }, (_, index) => index + 1);
  });

  /** The display zone's own name, in the active language. */
  protected readonly zoneName: Signal<string> = computed(() =>
    tmFormatZoneName(this.clock.timeZone(), this.l10n.locale(), this.clock.now()),
  );

  /** Moves the fake clock forward. */
  protected advance(milliseconds: number): void {
    this.clock.now.update((now) => now + milliseconds);
  }

  /** Puts the fake clock back where it starts. */
  protected resetClock(): void {
    this.clock.now.set(STORY_START);
  }

  /** The value a `change` event's select now holds. */
  protected valueOf(event: Event): string {
    return (event.target as HTMLSelectElement).value;
  }
}
