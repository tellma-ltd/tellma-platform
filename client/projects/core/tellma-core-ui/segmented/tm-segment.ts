// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { booleanAttribute, Component, input } from '@angular/core';

/**
 * One segment of a `tm-segmented`: data only. The control renders the
 * native radio, the icon and the label in its own track; this element
 * draws nothing.
 *
 * @tmGroup form-control
 */
@Component({
  selector: 'tm-segment',
  template: ``,
})
export class TmSegment<T> {
  /** The value the control's model takes when this segment is checked. */
  readonly value = input.required<T>();
  /** The label: shown beside the icon, or as its tooltip while the icon shows alone; always the accessible name. */
  readonly label = input.required<string>();
  /** A registered icon name (see `provideTellmaIcons`); a segment with an icon may drop its label when narrow. */
  readonly icon = input<string | undefined>(undefined);
  /** Disables this segment: it stays visible, cannot be checked, and the arrow keys skip it. */
  readonly disabled = input(false, { transform: booleanAttribute });
}
