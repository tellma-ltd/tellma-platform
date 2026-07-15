// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, signal } from '@angular/core';
import { form, FormField, validate } from '@angular/forms/signals';

import { TmCheckbox } from '@tellma/core-ui/checkbox';
import { TmFormField } from '@tellma/core-ui/form-field';

/** tm-checkbox demo host — Playwright battery + visual verification. */
@Component({
  imports: [TmCheckbox, TmFormField, FormField],
  template: `
    <h2>Checkbox</h2>

    <div class="stack">
      <tm-checkbox [(checked)]="simple" data-testid="cb-simple">Email me updates</tm-checkbox>

      <tm-checkbox
        [checked]="allSelected()"
        [indeterminate]="someSelected()"
        (checkedChange)="toggleAll($event)"
        data-testid="cb-parent"
      >
        Select all ({{ selectedCount() }}/3)
      </tm-checkbox>
      <div class="children">
        @for (row of rows(); track row.id) {
          <tm-checkbox
            [checked]="row.selected"
            (checkedChange)="toggleRow(row.id, $event)"
            [attr.data-testid]="'cb-row-' + row.id"
          >
            {{ row.name }}
          </tm-checkbox>
        }
      </div>

      <tm-checkbox [checked]="true" disabled data-testid="cb-disabled">
        Locked (disabled)
      </tm-checkbox>

      <tm-checkbox data-testid="cb-bare" aria-label="Bare checkbox"></tm-checkbox>

      <tm-form-field label="Terms" hint="You must accept to continue" data-testid="ff-terms">
        <tm-checkbox [formField]="f.accepted" data-testid="cb-terms">
          I accept the terms of service
        </tm-checkbox>
      </tm-form-field>
    </div>
  `,
  styles: `
    .stack {
      display: grid;
      gap: 12px;
      max-inline-size: 420px;
    }
    .children {
      display: grid;
      gap: 4px;
      margin-inline-start: 28px;
    }
  `,
})
export class CheckboxStory {
  readonly simple = signal(false);

  readonly rows = signal([
    { id: 1, name: 'Invoice 1001', selected: true },
    { id: 2, name: 'Invoice 1002', selected: false },
    { id: 3, name: 'Invoice 1003', selected: false },
  ]);
  readonly selectedCount = computed(() => this.rows().filter((r) => r.selected).length);
  readonly allSelected = computed(() => this.selectedCount() === this.rows().length);
  readonly someSelected = computed(
    () => this.selectedCount() > 0 && this.selectedCount() < this.rows().length,
  );

  readonly model = signal({ accepted: false });
  readonly f = form(this.model, (p) => {
    validate(p.accepted, ({ value }) =>
      value() ? undefined : { kind: 'mustAccept', message: 'Please accept the terms' },
    );
  });

  toggleAll(checked: boolean): void {
    this.rows.update((rows) => rows.map((row) => ({ ...row, selected: checked })));
  }

  toggleRow(id: number, checked: boolean): void {
    this.rows.update((rows) =>
      rows.map((row) => (row.id === id ? { ...row, selected: checked } : row)),
    );
  }
}
