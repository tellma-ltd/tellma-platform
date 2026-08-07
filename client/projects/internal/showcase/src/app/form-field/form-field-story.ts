// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { form, FormField, required } from '@angular/forms/signals';

import { TmInput } from '@tellma/core-ui/input';
import { TmFormField } from '@tellma/core-ui/form-field';

/**
 * tm-form-field demo host — the scaffold itself rather than any one control
 * inside it, so every state of the label / adornment / message chrome can be
 * read in one place.
 *
 * The validation section is the point of the page. Both fields there are
 * invalid; only the focused one shows its bubble, and the blurred one still
 * carries the invalid border and the in-field glyph.
 */
@Component({
  imports: [TmInput, TmFormField, FormField],
  template: `
    <h2>Form field</h2>

    <section>
      <h3>States</h3>
      <!-- One label per state, so a screenshot of the section reads without
           a legend: every field here says what it is demonstrating. -->
      <div class="grid">
        <tm-form-field label="Plain" data-testid="ff-default">
          <input tmInput placeholder="Search accounts" />
        </tm-form-field>

        <tm-form-field label="Required" data-testid="ff-required">
          <input tmInput [formField]="f.account" placeholder="Search accounts" />
        </tm-form-field>

        <tm-form-field
          label="With a hint"
          hint="Pick the posting account for this line."
          data-testid="ff-hint"
        >
          <input tmInput value="4110 · Account" />
        </tm-form-field>

        <tm-form-field data-testid="ff-nolabel">
          <input tmInput value="No label" />
        </tm-form-field>
      </div>
    </section>

    <section>
      <h3>Validation (focus the first to see its bubble)</h3>
      <div class="grid">
        <tm-form-field
          label="Invalid, with a hint"
          hint="Pick the posting account for this line."
          data-testid="ff-error-focus"
        >
          <input tmInput [formField]="f.touched" placeholder="Search accounts" />
        </tm-form-field>

        <tm-form-field label="Invalid, blurred" data-testid="ff-error-blur">
          <input tmInput [formField]="f.blurred" placeholder="Search accounts" />
        </tm-form-field>

        <tm-form-field label="Two errors at once" data-testid="ff-error-multi">
          <input tmInput [formField]="f.multi" placeholder="Search accounts" />
        </tm-form-field>
      </div>
    </section>

    <section>
      <h3>Adornments</h3>
      <div class="grid">
        <tm-form-field label="Amount" data-testid="ff-adorned">
          <span tmPrefix class="affix">SAR</span>
          <input tmInput class="tnum" value="1,250,000.00" />
          <span tmSuffix class="affix">kg</span>
        </tm-form-field>
      </div>
    </section>

    <section>
      <h3>Not editable</h3>
      <div class="grid">
        <tm-form-field label="Disabled" data-testid="ff-disabled">
          <input tmInput disabled value="4110 · Account" />
        </tm-form-field>

        <tm-form-field label="Readonly" data-testid="ff-readonly">
          <input tmInput readonly value="4110 · Account" />
        </tm-form-field>
      </div>
    </section>

    <section>
      <h3>Sizes</h3>
      <div class="grid">
        <tm-form-field label="Small" size="sm" data-testid="ff-sm">
          <input tmInput value="4110 · Account" />
        </tm-form-field>

        <tm-form-field label="Medium" size="md" data-testid="ff-md">
          <input tmInput value="4110 · Account" />
        </tm-form-field>

        <tm-form-field label="Large" size="lg" data-testid="ff-lg">
          <input tmInput value="4110 · Account" />
        </tm-form-field>
      </div>
    </section>
  `,
  styles: `
    .grid {
      display: grid;
      gap: 16px;
      max-inline-size: 360px;
      /* Room for the bubbles, which are overlays and so cannot push the
         section below them out of the way. */
      padding-block-end: 32px;
    }

    .affix {
      flex: none;
      font-size: var(--text-xs);
      color: var(--text-secondary);
    }

    .tnum {
      font-variant-numeric: tabular-nums;
      text-align: end;
    }
  `,
})
export class FormFieldStory {
  /** Every field on the page, so each tile can hold one state at a time. */
  protected readonly model = signal({
    account: '',
    touched: '',
    blurred: '',
    multi: '',
  });

  protected readonly f = form(this.model, (p) => {
    required(p.account);
    required(p.touched, { message: 'Select an account.' });
    required(p.blurred, { message: 'Select an account.' });
    // Two independent rules failing on the same empty value, so the bubble
    // has both to list.
    required(p.multi, { message: 'Select an account.' });
    required(p.multi, { message: 'The period must be open to post.' });
  });

  constructor() {
    // The validation tiles have to be showing an error before the user has
    // touched anything, or the page cannot demonstrate them: the display
    // policy needs touched-or-dirty, so mark them here.
    this.f.touched().markAsTouched();
    this.f.blurred().markAsTouched();
    this.f.multi().markAsTouched();
  }
}
