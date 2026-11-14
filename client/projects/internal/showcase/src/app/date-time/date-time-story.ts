// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { form, FormField, required } from '@angular/forms/signals';

import { TmDateTimeField, TmTimeField } from '@tellma/core-ui/date-time';
import { TmFormField } from '@tellma/core-ui/form-field';

/**
 * tm-time-field and tm-date-time-field demo host: a bound time in a field,
 * an unreadable time, an instant on Beirut's clock (whose offset changes
 * with daylight saving), one on Adelaide's in a fixed-width column (whose
 * label changes width, "GMT+10:30" to "GMT+9:30"), a server instant with a fraction
 * on UTC's clock,
 * a one-part instant showing `dateTimeIncomplete`, and the disabled and
 * readonly states. The model dump lets the battery assert the writes.
 */
@Component({
  imports: [FormField, TmDateTimeField, TmFormField, TmTimeField],
  template: `
    <h2>Date and time</h2>

    <div class="stack">
      <tm-form-field label="Opens at" hint="Up and Down step by 15 minutes" data-testid="ff-opens">
        <tm-time-field [formField]="f.opens" data-testid="time-opens" />
      </tm-form-field>

      <tm-form-field label="Closes at" data-testid="ff-closes">
        <tm-time-field [formField]="f.closes" [step]="30" data-testid="time-closes" />
      </tm-form-field>

      <!-- Beirut: GMT+2 in winter, GMT+3 in summer. -->
      <tm-date-time-field
        label="Meeting starts"
        timeZone="Asia/Beirut"
        [formField]="f.startsAt"
        data-testid="dt-beirut"
      />

      <!-- Adelaide in a fixed-width column: the label changes width across
           daylight saving, and the date and time boxes must not move. -->
      <div class="fixed-column">
        <tm-date-time-field
          label="Call starts"
          timeZone="Australia/Adelaide"
          [formField]="f.callAt"
          data-testid="dt-adelaide"
        />
      </div>

      <!-- A server value with a fraction, on UTC's clock: re-reads write nothing. -->
      <tm-date-time-field
        label="Received"
        timeZone="UTC"
        [formField]="f.receivedAt"
        data-testid="dt-utc"
      />

      <!-- Required and touched: the empty instant shows its error. -->
      <tm-date-time-field label="Due" [formField]="f.dueAt" data-testid="dt-required" />

      <tm-date-time-field
        label="Disabled"
        timeZone="Asia/Riyadh"
        disabled
        value="2026-11-20T14:00:00+03:00"
        data-testid="dt-disabled"
      />
      <tm-date-time-field
        label="Readonly"
        timeZone="Asia/Riyadh"
        readonly
        value="2026-11-20T14:00:00+03:00"
        data-testid="dt-readonly"
      />
    </div>

    <output data-testid="model-json">{{ modelJson() }}</output>
    <output data-testid="received-dirty">{{ f.receivedAt().dirty() ? 'dirty' : 'clean' }}</output>
  `,
  styles: `
    .stack {
      display: grid;
      justify-items: start;
      gap: 20px;
    }
    .fixed-column {
      inline-size: 480px;
    }
    output {
      display: block;
      margin-block-start: 16px;
      font-size: 12px;
      color: var(--text-secondary);
    }
  `,
})
export class DateTimeStory {
  /** The bound values. */
  protected readonly model = signal<{
    opens: string | null;
    closes: string | null;
    startsAt: string | null;
    callAt: string | null;
    receivedAt: string | null;
    dueAt: string | null;
  }>({
    opens: '09:00',
    closes: null,
    startsAt: null,
    callAt: null,
    receivedAt: '2026-11-20T11:00:00.0000000+00:00',
    dueAt: null,
  });
  /** The form over the model; `dueAt` is required. */
  protected readonly f = form(this.model, (path) => {
    required(path.dueAt, { message: 'Enter when it is due' });
  });

  constructor() {
    // Touched from the start, so the required error is on show.
    this.f.dueAt().markAsTouched();
  }

  /** The bound model, for the battery. */
  protected modelJson(): string {
    return JSON.stringify(this.model());
  }
}
