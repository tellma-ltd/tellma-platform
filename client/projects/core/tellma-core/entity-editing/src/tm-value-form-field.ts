// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, input, type InputSignal } from '@angular/core';
import type { FieldTree } from '@angular/forms/signals';
import type { TmValueDescriptor } from '@tellma/core';
import { TmFormField } from '@tellma/core-ui/form-field';

import { TmValueEditor } from './tm-value-editor';

// One labelled field of a generated form: the value editor inside its
// `tm-form-field`. `@tellma/core/entity`'s action dialog loads it with
// `import()` and renders one per argument, so the dialog's chunk never
// reaches the editors statically; the form field and its control share this
// component's template, where the field's content query finds the control.

/**
 * A labelled value editor: `tm-value-editor` in a `tm-form-field`.
 * Internal: an action's generated dialog renders one per argument.
 */
@Component({
  selector: 'tm-value-form-field',
  imports: [TmFormField, TmValueEditor],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      display: block;
    }
  `,
  template: `
    <tm-form-field [label]="label()">
      <tm-value-editor [property]="property()" [field]="field()" />
    </tm-form-field>
  `,
})
export class ɵTmValueFormField {
  /** The field's label. */
  readonly label: InputSignal<string> = input.required<string>();

  /** The type facts of the value it edits. */
  readonly property: InputSignal<TmValueDescriptor> = input.required<TmValueDescriptor>();

  /** The Signal Forms field it edits. */
  readonly field: InputSignal<FieldTree<unknown>> = input.required<FieldTree<unknown>>();
}
