// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, inject } from '@angular/core';
import { CircleX, Info, TriangleAlert } from 'lucide';

import { TmButton } from '@tellma/core-ui/button';
import { TmIcon } from '@tellma/core-ui/icon';
import { TM_MODAL_DATA, TmModalFooter, TmModalRef } from '@tellma/core-ui/modal';

import type { TmConfirmChoice, TmConfirmIcon } from '../tm-confirm';

/**
 * What `TmConfirm` hands its dialog content.
 * @internal
 */
export interface ɵTmConfirmData {
  /** The question. */
  readonly message: string;
  /** The message element's id, the dialog's `aria-describedby`. */
  readonly messageId: string;
  /** The glyph beside the message, or null for none. */
  readonly icon: TmConfirmIcon | null;
  /** The answers, in footer order. */
  readonly choices: readonly TmConfirmChoice<string>[];
  /** The index of the answer focused on open. */
  readonly focusIndex: number;
}

/** The glyph data for each disc tone. */
const GLYPHS = { warning: TriangleAlert, error: CircleX, info: Info } as const;

/**
 * The body and footer of a `TmConfirm` dialog: the message beside an
 * optional tinted disc, then one button per answer.
 *
 * @internal Opened exclusively by `TmConfirm`; never use directly.
 */
@Component({
  selector: 'tm-confirm-content',
  imports: [TmButton, TmIcon, TmModalFooter],
  template: `
    <!-- The message, the dialog's description, beside its glyph. -->
    <div class="tm-confirm">
      @if (data.icon; as icon) {
        <span class="tm-confirm__icon" [attr.data-tone]="icon">
          <tm-icon [svg]="glyphs[icon]" />
        </span>
      }
      <p class="tm-confirm__message" [id]="data.messageId">{{ data.message }}</p>
    </div>
    <!-- One button per answer; the focus trap starts on the marked one. -->
    <div tmModalFooter>
      @for (choice of data.choices; track $index) {
        <button
          tmButton
          type="button"
          class="tm-confirm__choice"
          [variant]="choice.variant"
          [attr.cdkFocusInitial]="$index === data.focusIndex ? '' : null"
          (click)="ref.close(choice.key)"
        >
          {{ choice.label }}
        </button>
      }
    </div>
  `,
  styleUrl: './tm-confirm-content.css',
})
export class ɵTmConfirmContent {
  /** The question and its answers. */
  protected readonly data = inject<ɵTmConfirmData>(TM_MODAL_DATA);

  /** The dialog, closed with the chosen key. */
  protected readonly ref = inject<TmModalRef<string>>(TmModalRef);

  /** The disc glyphs by tone. */
  protected readonly glyphs = GLYPHS;
}
