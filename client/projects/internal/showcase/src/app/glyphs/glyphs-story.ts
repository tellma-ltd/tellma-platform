// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';

import { TmCheckbox } from '@tellma/core-ui/checkbox';
import { TmFormField } from '@tellma/core-ui/form-field';
import { TmGrid, TmGridColumn } from '@tellma/core-ui/grid';
import { TmInput } from '@tellma/core-ui/input';
import { TmMultiSelect, TmOption, TmSelect } from '@tellma/core-ui/select';

/** One row of the selectable grid the checkbox glyphs are compared against. */
interface GlyphRow {
  /** The row id the grid selects by. */
  readonly id: number;
  /** The row's only text. */
  readonly name: string;
}

/**
 * The foundation's built-in glyphs side by side: the checkbox's check and
 * dash, the select's caret and option check, the multi-select's row
 * checkbox, and the form field's invalid mark, each in the state that
 * draws it, with a selectable grid whose row checkboxes must look exactly
 * like a tm-checkbox. The Playwright battery
 * pins every glyph's box and stroke here, in both directions, and compares
 * the checkbox against the grid's checkbox cell pixel for pixel.
 */
@Component({
  imports: [
    TmCheckbox,
    TmFormField,
    TmGrid,
    TmGridColumn,
    TmInput,
    TmMultiSelect,
    TmOption,
    TmSelect,
  ],
  template: `
    <h2>Foundation glyphs</h2>

    <!-- Checkbox: the check and the indeterminate dash. -->
    <section class="row">
      <tm-checkbox [checked]="true" data-testid="cb-checked">Checked</tm-checkbox>
      <tm-checkbox [indeterminate]="true" data-testid="cb-mixed">Mixed</tm-checkbox>
    </section>

    <!-- Select: the trigger caret; the option check shows once it opens. -->
    <section class="fields">
      <tm-form-field label="Country" data-testid="ff-select">
        <tm-select [(value)]="country" data-testid="select">
          <tm-option [value]="1" label="Saudi Arabia">Saudi Arabia</tm-option>
          <tm-option [value]="2" label="Ethiopia">Ethiopia</tm-option>
          <tm-option [value]="3" label="Jordan">Jordan</tm-option>
        </tm-select>
      </tm-form-field>

      <!-- Multi-select: a checkbox box on every row, ticked when selected. -->
      <tm-form-field label="Countries" data-testid="ff-multi">
        <tm-multi-select [(value)]="countries" data-testid="multi">
          <tm-option [value]="1" label="Saudi Arabia">Saudi Arabia</tm-option>
          <tm-option [value]="2" label="Ethiopia">Ethiopia</tm-option>
          <tm-option [value]="3" label="Jordan">Jordan</tm-option>
        </tm-multi-select>
      </tm-form-field>

      <!-- Form field: the invalid mark beside an error. -->
      <tm-form-field label="Account" error="Pick an account" data-testid="ff-error">
        <input tmInput value="4110" />
      </tm-form-field>
    </section>

    <!-- The grid's row checkboxes: select a row to draw the check, which
         also turns the header's select-all to the dash. -->
    <tm-grid
      class="grid"
      gridId="glyphs-grid"
      data-testid="grid"
      [data]="rows"
      [rowId]="rowId"
      selectable
      [(selectedIds)]="selectedIds"
    >
      <tm-grid-column key="name" header="Name" [flex]="1" />
    </tm-grid>
  `,
  styles: `
    .row {
      display: flex;
      gap: 24px;
      margin-block-end: 16px;
    }
    .fields {
      display: grid;
      gap: 16px;
      max-inline-size: 320px;
      margin-block-end: 16px;
    }
    .grid {
      display: block;
      block-size: 160px;
      max-inline-size: 320px;
    }
  `,
})
export class GlyphsStory {
  /** The select's committed value, preselected so the open panel shows a check. */
  readonly country = signal<number | undefined>(2);

  /** The multi-select's values, one of three selected. */
  readonly countries = signal<readonly number[]>([2]);

  /** The grid's three rows. */
  readonly rows: readonly GlyphRow[] = [
    { id: 1, name: 'First' },
    { id: 2, name: 'Second' },
    { id: 3, name: 'Third' },
  ];

  /** The grid's row identity. */
  readonly rowId = (row: GlyphRow): number => row.id;

  /** The grid's selection, empty until a row checkbox is pressed. */
  readonly selectedIds = signal<ReadonlySet<string | number>>(new Set());
}
