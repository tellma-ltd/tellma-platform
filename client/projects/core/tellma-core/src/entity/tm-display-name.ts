// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, input, type InputSignal } from '@angular/core';

import type { TmDisplayNameParts } from './tm-display-names';

// A display name in markup (spec 0029 §5.2 step 4): the prefix and the name
// each in a `<bdi>`, so an English code beside an Arabic name keeps its order
// in either page direction. The text carries no bidi control; the isolates
// alone set direction.

/**
 * Renders a display name's parts inline: `<bdi>1101</bdi> - <bdi>Cash</bdi>`,
 * or the name alone. `null` renders nothing.
 *
 * @example
 * ```html
 * <tm-display-name [parts]="names.parts('gl.Center', center)()" />
 * ```
 */
@Component({
  selector: 'tm-display-name',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@if (parts(); as value) {@if (value.prefix !== null) {<bdi>{{ value.prefix }}</bdi> - }<bdi>{{ value.name }}</bdi>}`,
})
export class TmDisplayName {
  /** The parts to render; `null` renders nothing. */
  readonly parts: InputSignal<TmDisplayNameParts | null> = input<TmDisplayNameParts | null>(null);
}
