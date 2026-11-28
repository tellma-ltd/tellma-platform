// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { form, FormField, validate } from '@angular/forms/signals';

import { TmFormField } from '@tellma/core-ui/form-field';
import { TmSwitch } from '@tellma/core-ui/switch';

/**
 * tm-switch demo host: off and on, the label on either side, disabled off
 * and on, readonly, invalid in a field with its error, and a bare switch
 * named by aria-label.
 */
@Component({
  imports: [FormField, TmFormField, TmSwitch],
  template: `
    <h2>Switch</h2>

    <div class="stack">
      <tm-switch [(checked)]="notify" data-testid="switch-notify">Notify me by email</tm-switch>
      <tm-switch [checked]="true" data-testid="switch-on">Show totals</tm-switch>
      <tm-switch labelPosition="start" [(checked)]="archived" data-testid="switch-start">
        Show archived documents
      </tm-switch>
      <tm-switch disabled data-testid="switch-disabled">Locked setting</tm-switch>
      <tm-switch disabled [checked]="true" data-testid="switch-disabled-on"
        >Locked and on</tm-switch
      >
      <tm-switch readonly [checked]="true" data-testid="switch-readonly">Readonly and on</tm-switch>
      <tm-switch aria-label="Bare switch" data-testid="switch-bare" />

      <!-- Must be on, and touched from the start, so the error shows. -->
      <tm-form-field label="Terms" hint="Required to continue" data-testid="ff-terms">
        <tm-switch [formField]="f.accepted" data-testid="switch-terms">
          I accept the terms
        </tm-switch>
      </tm-form-field>
    </div>

    <output data-testid="model-json">{{ modelJson() }}</output>
  `,
  styles: `
    .stack {
      display: grid;
      justify-items: start;
      gap: 8px;
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
export class SwitchStory {
  /** The first switch's two-way state. */
  protected readonly notify = signal(false);
  /** The label-start switch's two-way state. */
  protected readonly archived = signal(false);

  /** The bound acceptance, which must be on. */
  protected readonly model = signal({ accepted: false });
  /** The form over the model. */
  protected readonly f = form(this.model, (path) => {
    validate(path.accepted, ({ value }) =>
      value() ? undefined : { kind: 'mustAccept', message: 'Turn this on to continue' },
    );
  });

  constructor() {
    // Touched from the start, so the error is on show.
    this.f.accepted().markAsTouched();
  }

  /** The switches' states, for the battery. */
  protected modelJson(): string {
    return JSON.stringify({ ...this.model(), notify: this.notify(), archived: this.archived() });
  }
}
