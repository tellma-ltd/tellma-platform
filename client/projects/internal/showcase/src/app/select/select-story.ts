// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { form, FormField, required } from '@angular/forms/signals';

import { TmFormField } from '@tellma/core-ui/form-field';
import { TmMultiSelect, TmOption, TmOptionGroup, TmSelect } from '@tellma/core-ui/select';

interface Country {
  readonly id: number;
  readonly name: string;
}

const COUNTRIES: readonly Country[] = [
  { id: 1, name: 'Saudi Arabia' },
  { id: 2, name: 'United Arab Emirates' },
  { id: 3, name: 'Ethiopia' },
  { id: 4, name: 'Jordan' },
  { id: 5, name: 'Egypt' },
  { id: 6, name: 'Kuwait' },
  { id: 7, name: 'Qatar' },
  { id: 8, name: 'Bahrain' },
  { id: 9, name: 'Oman' },
];

/**
 * tm-select and tm-multi-select demo host — Playwright battery + visual
 * verification: a flat select in a clipping box, async options, disabled,
 * option groups in both selects, and a bottom-pinned select that flips up.
 */
@Component({
  imports: [TmSelect, TmMultiSelect, TmOption, TmOptionGroup, TmFormField, FormField],
  template: `
    <h2>Select</h2>

    <div class="grid">
      <!-- Inside an overflow:hidden ancestor: the top-layer panel must escape. -->
      <div class="clipbox">
        <tm-form-field label="Country" hint="Where the tenant operates" data-testid="ff-country">
          <tm-select [formField]="f.countryId" data-testid="select-country">
            @for (country of countries; track country.id) {
              <tm-option [value]="country.id" [label]="country.name">{{ country.name }}</tm-option>
            }
          </tm-select>
        </tm-form-field>
      </div>

      <tm-form-field label="Prepopulated (async options)" data-testid="ff-async">
        <tm-select
          [(value)]="asyncValue"
          [valueKey]="byId"
          [displayWith]="displayName"
          data-testid="select-async"
        >
          @for (country of asyncOptions(); track country.id) {
            <tm-option [value]="country" [label]="country.name">{{ country.name }}</tm-option>
          }
        </tm-select>
      </tm-form-field>
      <button type="button" data-testid="load-options" (click)="loadOptions()">
        Load options
      </button>

      <tm-form-field label="Disabled" data-testid="ff-disabled">
        <tm-select disabled [(value)]="disabledValue">
          <tm-option [value]="1" label="One">One</tm-option>
        </tm-select>
      </tm-form-field>

      <!-- Option groups: headings over runs of options, in both selects. -->
      <tm-form-field label="Center type (grouped)" data-testid="ff-grouped">
        <tm-select [(value)]="groupedValue" data-testid="select-grouped">
          @for (group of centerGroups; track group.label) {
            <tm-option-group [label]="group.label">
              @for (type of group.types; track type) {
                <tm-option [value]="type" [label]="type">{{ type }}</tm-option>
              }
            </tm-option-group>
          }
          <tm-option value="Other" label="Other">Other</tm-option>
        </tm-select>
      </tm-form-field>

      <tm-form-field label="Center types (multiple)" hint="Leave empty for all" data-testid="ff-multi">
        <tm-multi-select [(value)]="multiValue" data-testid="multi-grouped">
          @for (group of centerGroups; track group.label) {
            <tm-option-group [label]="group.label">
              @for (type of group.types; track type) {
                <tm-option [value]="type" [label]="type">{{ type }}</tm-option>
              }
            </tm-option-group>
          }
          <tm-option value="Other" label="Other">Other</tm-option>
          <tm-option value="Retired" label="Retired" disabled>Retired</tm-option>
        </tm-multi-select>
      </tm-form-field>
      <span data-testid="multi-value">{{ multiValue().join(' | ') }}</span>
    </div>

    <!-- Pinned near the viewport bottom: the panel must flip up. -->
    <div class="flip-anchor">
      <tm-select [(value)]="flipValue" placeholder="Flips up" aria-label="Flip demo" data-testid="select-flip">
        @for (country of countries; track country.id) {
          <tm-option [value]="country.id" [label]="country.name">{{ country.name }}</tm-option>
        }
      </tm-select>
    </div>
  `,
  styles: `
    .grid {
      display: grid;
      gap: 16px;
      max-inline-size: 360px;
    }
    .clipbox {
      block-size: 96px;
      overflow: hidden;
      border: 1px dashed #a8b7bc;
      padding: 4px;
    }
    .flip-anchor {
      position: fixed;
      inset-block-end: 8px;
      inset-inline-start: 24px;
      inline-size: 240px;
    }
  `,
})
export class SelectStory {
  readonly countries = COUNTRIES;

  readonly model = signal<{ countryId: number | null }>({ countryId: null });
  readonly f = form(this.model, (p) => {
    required(p.countryId);
  });

  // Prepopulated with a FRESH instance before options exist (edit screen).
  readonly asyncValue = signal<Country | undefined>({ id: 3, name: 'Ethiopia' });
  readonly asyncOptions = signal<readonly Country[]>([]);
  readonly byId = (c: Country) => c.id;
  readonly displayName = (c: Country) => c.name;

  readonly disabledValue = signal<number | undefined>(1);

  /** The center types under their group headings. */
  readonly centerGroups: readonly { readonly label: string; readonly types: readonly string[] }[] = [
    { label: 'Operating', types: ['Service', 'Operation'] },
    { label: 'Commercial', types: ['Sale', 'Marketing'] },
  ];
  readonly groupedValue = signal<string | undefined>(undefined);
  readonly multiValue = signal<readonly string[]>(['Sale']);
  readonly flipValue = signal<number | undefined>(undefined);

  loadOptions(): void {
    this.asyncOptions.set(COUNTRIES);
  }
}
