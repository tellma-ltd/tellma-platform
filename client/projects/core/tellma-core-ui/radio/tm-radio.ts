// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { booleanAttribute, Component, input } from '@angular/core';

/**
 * One choice of a `tm-radio-group`: data only. The group renders the
 * native radio, its label and its hint in its own list; this element draws
 * nothing.
 *
 * @tmGroup form-control
 */
@Component({
  selector: 'tm-radio',
  template: ``,
})
export class TmRadio<T> {
  /** The value the group's model takes when this radio is checked. */
  readonly value = input.required<T>();
  /** The visible label, also the radio's accessible name. */
  readonly label = input.required<string>();
  /** A supporting line under the label, linked to the radio as its description. */
  readonly hint = input<string | undefined>(undefined);
  /** Disables this radio: it stays visible, cannot be checked, and the arrow keys skip it. */
  readonly disabled = input(false, { transform: booleanAttribute });
}
