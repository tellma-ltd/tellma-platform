// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { form, FormField, required } from '@angular/forms/signals';

import { TmFormField } from '@tellma/core-ui/form-field';
import { TmRadio, TmRadioGroup } from '@tellma/core-ui/radio';

/**
 * tm-radio-group demo host: a bound group in a labelled field with hints
 * and a disabled radio, the horizontal layout, the disabled, readonly and
 * invalid states, and the three sizes. The model dump lets the Playwright
 * battery assert what the keys checked.
 */
@Component({
  imports: [FormField, TmFormField, TmRadio, TmRadioGroup],
  template: `
    <h2>Radio group</h2>

    <div class="stack">
      <!-- Bound in a labelled field: the field's label names the group. -->
      <tm-form-field label="Payment terms" hint="How the customer pays" data-testid="ff-terms">
        <tm-radio-group [formField]="f.terms" data-testid="radio-terms">
          <tm-radio value="cash" label="Cash" />
          <tm-radio value="net30" label="Net 30" hint="Due 30 days after the invoice" />
          <tm-radio value="net60" label="Net 60" disabled />
          <tm-radio value="card" label="Card" />
        </tm-radio-group>
      </tm-form-field>

      <!-- One row, named by ariaLabel. -->
      <tm-radio-group
        ariaLabel="Document state"
        orientation="horizontal"
        [(value)]="state"
        data-testid="radio-horizontal"
      >
        <tm-radio value="draft" label="Draft" />
        <tm-radio value="posted" label="Posted" />
        <tm-radio value="void" label="Void" />
      </tm-radio-group>

      <!-- Required and touched, so the error shows at once. -->
      <tm-form-field label="Delivery" data-testid="ff-required">
        <tm-radio-group [formField]="f.delivery" data-testid="radio-required">
          <tm-radio value="pickup" label="Pick up" />
          <tm-radio value="ship" label="Ship" />
        </tm-radio-group>
      </tm-form-field>

      <tm-radio-group ariaLabel="Disabled group" disabled value="b" data-testid="radio-disabled">
        <tm-radio value="a" label="Disabled" />
        <tm-radio value="b" label="Disabled and checked" />
      </tm-radio-group>

      <tm-radio-group ariaLabel="Readonly group" readonly value="b" data-testid="radio-readonly">
        <tm-radio value="a" label="Readonly" />
        <tm-radio value="b" label="Readonly and checked" />
      </tm-radio-group>

      <!-- The size ladder: the option-row heights. -->
      @for (size of sizes; track size) {
        <tm-radio-group
          [ariaLabel]="'Size ' + size"
          orientation="horizontal"
          [size]="size"
          value="one"
          [attr.data-testid]="'radio-size-' + size"
        >
          <tm-radio value="one" [label]="'Size ' + size" />
          <tm-radio value="two" label="Other" />
        </tm-radio-group>
      }
    </div>

    <output data-testid="model-json">{{ modelJson() }}</output>
  `,
  styles: `
    .stack {
      display: grid;
      gap: 16px;
      max-inline-size: 480px;
    }
    output {
      display: block;
      margin-block-start: 16px;
      font-size: 12px;
      color: var(--text-secondary);
    }
  `,
})
export class RadioStory {
  /** The three steps of the size ladder. */
  protected readonly sizes = ['sm', 'md', 'lg'] as const;
  /** The horizontal group's two-way value. */
  protected readonly state = signal<string | null>('draft');

  /** The bound fields: terms starts checked, delivery is required and empty. */
  protected readonly model = signal<{ terms: string | null; delivery: string | null }>({
    terms: 'net30',
    delivery: null,
  });
  /** The form over the model; `delivery` is required. */
  protected readonly f = form(this.model, (path) => {
    required(path.delivery, { message: 'Choose how the order is delivered' });
  });

  constructor() {
    // Touched from the start, so the required error is on show.
    this.f.delivery().markAsTouched();
  }

  /** The bound model and the horizontal value, for the battery. */
  protected modelJson(): string {
    return JSON.stringify({ ...this.model(), state: this.state() });
  }
}
