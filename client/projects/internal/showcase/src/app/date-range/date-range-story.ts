// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { form, FormField } from '@angular/forms/signals';

import { tmDateRangeOrder, TmDateRange, type TmDateRangeValue } from '@tellma/core-ui/date-range';

/**
 * tm-date-range demo host: a bound stacked range with the order validator,
 * an out-of-order range showing its error, the inline layout within bounds,
 * and the disabled and readonly states.
 */
@Component({
  imports: [FormField, TmDateRange],
  template: `
    <h2>Date range</h2>

    <div class="stack">
      <tm-date-range label="Posting period" [formField]="f.period" data-testid="range-period" />

      <!-- Out of order and touched: the error shows on "to". -->
      <tm-date-range label="Out of order" [formField]="f.reversed" data-testid="range-reversed" />

      <tm-date-range
        label="Fiscal 2026"
        layout="inline"
        minDate="2026-01-01"
        maxDate="2026-12-31"
        fromLabel="Start"
        toLabel="End"
        [formField]="f.fiscal"
        data-testid="range-inline"
      />

      <tm-date-range
        label="Disabled"
        layout="inline"
        disabled
        [value]="closed"
        data-testid="range-disabled"
      />
      <tm-date-range
        label="Readonly"
        layout="inline"
        readonly
        [value]="closed"
        data-testid="range-readonly"
      />
    </div>

    <output data-testid="model-json">{{ modelJson() }}</output>
  `,
  styles: `
    .stack {
      display: grid;
      gap: 24px;
      max-inline-size: 520px;
    }
    output {
      display: block;
      margin-block-start: 16px;
      font-size: 12px;
      color: var(--text-secondary);
    }
  `,
})
export class DateRangeStory {
  /** A closed range for the disabled and readonly states. */
  protected readonly closed: TmDateRangeValue = { from: '2026-01-01', to: '2026-03-31' };

  /** The bound ranges. */
  protected readonly model = signal<{
    period: TmDateRangeValue;
    reversed: TmDateRangeValue;
    fiscal: TmDateRangeValue;
  }>({
    period: { from: null, to: null },
    reversed: { from: '2026-05-10', to: '2026-05-01' },
    fiscal: { from: '2026-04-01', to: '2026-06-30' },
  });
  /** The form over the model; every range is ordered. */
  protected readonly f = form(this.model, (path) => {
    tmDateRangeOrder(path.period);
    tmDateRangeOrder(path.reversed);
    tmDateRangeOrder(path.fiscal);
  });

  constructor() {
    // Touched from the start, so the order error is on show.
    this.f.reversed().markAsTouched();
  }

  /** The bound model, for the battery. */
  protected modelJson(): string {
    return JSON.stringify(this.model());
  }
}
