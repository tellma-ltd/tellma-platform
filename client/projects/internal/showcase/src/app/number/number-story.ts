// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { form, FormField, max, min, required } from '@angular/forms/signals';

import { TmFormField } from '@tellma/core-ui/form-field';
import { TmNumber } from '@tellma/core-ui/number';

/**
 * tmNumber demo host — locale round-trips, display rounding, percent mode,
 * min/max kinds, the precision envelope, and the physical right alignment
 * under RTL. Drives the Playwright battery; the model dump lets the tests
 * assert the typed values.
 */
@Component({
  imports: [TmNumber, TmFormField, FormField],
  template: `
    <h2>Number input</h2>

    <div class="grid">
      <tm-form-field label="Amount" hint="Two decimals" data-testid="ff-amount">
        <input
          tmNumber
          [formField]="f.amount"
          [minDecimals]="2"
          [maxDecimals]="2"
          data-testid="input-amount"
        />
      </tm-form-field>

      <tm-form-field label="Quantity (0–100)" data-testid="ff-qty">
        <input tmNumber [formField]="f.qty" [maxDecimals]="0" data-testid="input-qty" />
      </tm-form-field>

      <tm-form-field label="Discount" hint="A fraction of 1" data-testid="ff-discount">
        <input
          tmNumber
          [formField]="f.discount"
          [percent]="true"
          [maxDecimals]="1"
          data-testid="input-discount"
        />
      </tm-form-field>

      <tm-form-field label="Free precision" hint="Envelope-guarded" data-testid="ff-free">
        <input tmNumber [formField]="f.free" data-testid="input-free" />
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
    output {
      display: block;
      margin-block-start: 16px;
      font-size: 12px;
      color: var(--text-secondary);
    }
  `,
})
export class NumberStory {
  readonly model = signal<{
    amount: number | null;
    qty: number | null;
    discount: number | null;
    free: number | null;
  }>({ amount: 1483.8, qty: 5, discount: 0.155, free: null });

  readonly f = form(this.model, (p) => {
    required(p.amount);
    min(p.qty, 0);
    max(p.qty, 100);
  });

  modelJson(): string {
    return JSON.stringify(this.model());
  }
}
