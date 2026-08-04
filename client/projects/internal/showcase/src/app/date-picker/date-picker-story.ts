// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, signal } from '@angular/core';
import { form, FormField, required } from '@angular/forms/signals';

import { tmMaxDate, tmMinDate } from '@tellma/core-ui';
import { TmDatePicker } from '@tellma/core-ui/date-picker';
import { TmFormField } from '@tellma/core-ui/form-field';
import { tmEthiopicCalendar } from '@tellma/core-ui/calendar-ethiopic';
import { tmUmalquraCalendar } from '@tellma/core-ui/calendar-umalqura';
import type { TmCalendar } from '@tellma/core-ui/l10n';

/**
 * tm-date-picker demo host — typed entry, the calendar popup, bounds,
 * validation kinds, and runtime display-calendar switching (Gregorian /
 * Umm al-Qura / Ethiopic) with the ISO model unchanged. Drives the
 * Playwright battery; the model dump lets the tests assert the ISO values.
 */
@Component({
  imports: [TmDatePicker, TmFormField, FormField],
  template: `
    <h2>Date picker</h2>

    <div class="row">
      <span>Display calendar:</span>
      <button type="button" data-testid="cal-gregory" (click)="calendarId.set('gregory')">
        Gregorian
      </button>
      <button type="button" data-testid="cal-umalqura" (click)="calendarId.set('islamic-umalqura')">
        Umm al-Qura
      </button>
      <button type="button" data-testid="cal-ethiopic" (click)="calendarId.set('ethiopic')">
        Ethiopic
      </button>
      <output data-testid="active-calendar">{{ calendarId() }}</output>
    </div>

    <div class="grid">
      <tm-form-field label="Due date" hint="Type or pick" data-testid="ff-due">
        <tm-date-picker
          [formField]="f.due"
          [calendar]="activeCalendar()"
          data-testid="picker-due"
        />
      </tm-form-field>

      <tm-form-field label="Delivery (March 2026 only)" data-testid="ff-bounded">
        <tm-date-picker
          [formField]="f.delivery"
          minDate="2026-03-01"
          maxDate="2026-03-31"
          [calendar]="activeCalendar()"
          data-testid="picker-bounded"
        />
      </tm-form-field>

      <tm-form-field label="Long style" data-testid="ff-long">
        <tm-date-picker
          [formField]="f.posted"
          dateStyle="long"
          [calendar]="activeCalendar()"
          data-testid="picker-long"
        />
      </tm-form-field>
    </div>

    <output data-testid="model-json">{{ modelJson() }}</output>
  `,
  styles: `
    .grid {
      display: grid;
      gap: 16px;
      max-inline-size: 420px;
    }
    .row {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-block-end: 16px;
    }
    output {
      display: block;
      margin-block-start: 16px;
      font-size: 12px;
      color: var(--text-secondary);
    }
  `,
})
export class DatePickerStory {
  readonly calendarId = signal<'gregory' | 'islamic-umalqura' | 'ethiopic'>('gregory');
  readonly activeCalendar = computed<TmCalendar | undefined>(() => {
    switch (this.calendarId()) {
      case 'islamic-umalqura':
        return tmUmalquraCalendar();
      case 'ethiopic':
        return tmEthiopicCalendar();
      default:
        return undefined; // the ambient TM_CALENDAR (Gregorian)
    }
  });

  readonly model = signal<{
    due: string | null;
    delivery: string | null;
    posted: string | null;
  }>({ due: '2026-03-05', delivery: null, posted: '2026-07-01' });

  readonly f = form(this.model, (p) => {
    required(p.due);
    tmMinDate(p.delivery, '2026-03-01');
    tmMaxDate(p.delivery, '2026-03-31');
  });

  modelJson(): string {
    return JSON.stringify(this.model());
  }
}
