// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, inject, input, isDevMode } from '@angular/core';

let nextUniqueId = 0;

/**
 * A heading over a run of options in a `tm-select` or `tm-multi-select`:
 * wrap the `tm-option`s that belong under it.
 *
 * Like `tm-option`, a group is a definition, not a rendered element: the
 * select draws each group in its panel as a `role="group"` named by its
 * heading, and the heading row takes an option's height, so the panel
 * measures the same with or without groups. Keyboard navigation and
 * typeahead skip the headings; only options are ever active.
 *
 * Groups nest one level: a group inside a group is refused in development,
 * and its options are drawn under the innermost group otherwise.
 *
 * @example
 * ```html
 * <tm-select>
 *   <tm-option-group label="Operating">
 *     <tm-option value="Service">Service</tm-option>
 *     <tm-option value="Operation">Operation</tm-option>
 *   </tm-option-group>
 *   <tm-option value="Sale">Sale</tm-option>
 * </tm-select>
 * ```
 *
 * @tmGroup form-control
 * @tmA11yNotes Rendered as `role="group"` with `aria-labelledby` pointing at
 *   the visible heading, so a screen reader announces the group's name on
 *   entering it. The heading itself is never focusable or selectable.
 */
@Component({
  selector: 'tm-option-group',
  // Nothing renders in place: the select reads the group as a definition
  // and draws its heading and options inside the panel.
  template: '',
})
export class TmOptionGroup {
  /** The visible heading, which is also the group's accessible name. */
  readonly label = input.required<string>();

  /** The id the rendered heading carries, which the group's `aria-labelledby` names. */
  readonly headingId = `tm-option-group-${nextUniqueId++}`;

  /** Refuses a nested group in development. */
  constructor() {
    // One level only (spec 0010 §B.4): a select's panel draws groups as a
    // flat list of headed runs, and a heading inside a heading has no row
    // shape that would still read as one list.
    if (isDevMode() && inject(TmOptionGroup, { optional: true, skipSelf: true }) !== null) {
      throw new Error(
        'tm-option-group: a group cannot sit inside another group. Option groups nest one ' +
          'level; list the inner options under their own group instead.',
      );
    }
  }
}
