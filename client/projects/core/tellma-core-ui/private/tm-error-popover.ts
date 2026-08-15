// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { afterRenderEffect, Component, ElementRef, inject, input } from '@angular/core';

/**
 * The validation-message bubble: a card that has gone red at the edge,
 * with an arrow pointing back at the thing it describes.
 *
 * Both `tm-form-field` and the grid's active-cell error render it, so a
 * field error and a cell error read as the same object. It is presentation
 * only — it carries no ARIA of its own, because the two hosts need
 * opposite semantics: the field already announces through a live region and
 * marks this bubble `aria-hidden`, while the grid points a cell's
 * `aria-describedby` at it and gives it `role="tooltip"`. Set whichever
 * applies on the `<tm-error-popover>` element itself.
 *
 * Position it with {@link tmCreateAnchoredOverlay}; tell it which way it
 * ended up landing through {@link above} so the arrow points back.
 *
 * @internal Shared library machinery; not part of the stable surface.
 */
@Component({
  selector: 'tm-error-popover',
  template: `
    <!-- The arrow is absolutely positioned, so it sits outside the flow of
         the message list. -->
    <span class="tm-error-popover__arrow"></span>
    <!-- One glyph PER MESSAGE, as a bullet. A single glyph beside a stack of
         two would read as decoration on the first line rather than a mark on
         each problem, and the reader could not tell where one message ends
         and the next begins. -->
    @for (message of messages(); track $index) {
      <span class="tm-error-popover__message">
        <svg
          class="tm-error-popover__icon"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.75"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <circle cx="12" cy="12" r="10" />
          <line x1="12" x2="12" y1="8" y2="12" />
          <line x1="12" x2="12.01" y1="16" y2="16" />
        </svg>
        <span class="tm-error-popover__text">{{ message }}</span>
      </span>
    }
  `,
  styleUrl: './tm-error-popover.css',
  host: {
    class: 'tm-error-popover',
    '[class.tm-error-popover--above]': 'above()',
    '[class.tm-error-popover--static]': '!selectable()',
  },
})
export class ɵTmErrorPopover {
  /** The messages to show, one per line. */
  readonly messages = input.required<readonly string[]>();
  /**
   * Whether the bubble landed above what it points at (the overlay flipped
   * because there was no room below), which moves the arrow to its lower
   * edge.
   */
  readonly above = input(false);

  /**
   * Whether the message text takes the pointer, so it can be selected and
   * copied. Default true, for a form field: the bubble hangs over the next
   * field, which the reader is unlikely to be reaching for while an error
   * they are still reading is up.
   *
   * A grid sets this false. There the bubble hangs over the NEXT ROW, and a
   * press meant for that cell landing on the message instead is a worse
   * trade than not being able to copy it — the cell would take two clicks
   * to reach, every time.
   */
  readonly selectable = input(true);

  private readonly hostElement = inject(ElementRef).nativeElement as HTMLElement;

  constructor() {
    // The CDK's overlay pane takes the pointer by default, so a bubble that
    // has made ITSELF transparent still stops the press one element short of
    // whatever it is covering. The pane is created by the CDK and carries no
    // encapsulation attribute, so no stylesheet of ours can select it —
    // hence the inline style.
    //
    // Always inert, for every host: a descendant re-enables pointer events
    // on its own, which is exactly how the message text stays selectable
    // (see the stylesheet) while the padding around it lets presses through.
    afterRenderEffect(() => {
      const pane = this.hostElement.closest<HTMLElement>('.cdk-overlay-pane');
      if (pane !== null) {
        pane.style.pointerEvents = 'none';
      }
    });
  }
}
