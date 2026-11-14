// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, inject, input } from '@angular/core';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';

import { TmCopyButton } from './tm-copy-button';

/**
 * A trace id or a version, shown so it can be quoted to support: the label
 * "Reference", the value in a sunken mono chip, then an icon-only "Copy
 * reference" button. The chip reads left to right in both directions and
 * is cut short with an ellipsis when narrow; the button always copies the
 * full value.
 *
 * @tmGroup indicator
 * @tmA11yNotes The label and the value are read as text, the value in
 *   full even when cut short on screen. The copy button is named "Copy
 *   reference" and announces "Copied".
 */
@Component({
  selector: 'tm-reference',
  imports: [TmCopyButton],
  // Label, chip, button: one line that shrinks at the chip.
  template: `
    <span class="tm-reference__label">{{ shownLabel() }}</span>
    <span class="tm-reference__value" dir="ltr">{{ value() }}</span>
    <tm-copy-button class="tm-reference__copy" [text]="value()" [label]="copyLabel()" size="sm" />
  `,
  styleUrl: './tm-reference.css',
  host: { class: 'tm-reference' },
})
export class TmReference {
  private readonly translate = inject(TM_UI_TRANSLATE);

  /** The trace id or version, shown and copied. */
  readonly value = input.required<string>();

  /** The label before the chip. Default "Reference". */
  readonly label = input<string | undefined>(undefined);

  /** The label shown: the consumer's, else "Reference". */
  protected readonly shownLabel = computed(
    () => this.label() ?? this.translate('reference.label')(),
  );

  /** The copy button's name. */
  protected readonly copyLabel = this.translate('reference.copy');
}
