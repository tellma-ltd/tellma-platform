// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { email, form, FormField, required } from '@angular/forms/signals';

import { TmFormField } from '@tellma/core-ui/form-field';
import { TmInput } from '@tellma/core-ui/input';
import { TmOption, TmSelect } from '@tellma/core-ui/select';

/**
 * Locale-pack demo host (DoD 13): runtime language switching re-renders
 * already-visible library strings; Arabic content pulls the pack's font on
 * demand (unicode-range).
 */
@Component({
  imports: [TmInput, TmFormField, TmSelect, TmOption, FormField],
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
  `,
  styles: `
    .grid {
      display: grid;
      gap: 16px;
      max-inline-size: 360px;
    }
  `,
})
export class I18nStory {
  readonly status = signal<number | undefined>(undefined);

  readonly model = signal({ email: '' });
  readonly f = form(this.model, (p) => {
    required(p.email);
    email(p.email);
  });
}
