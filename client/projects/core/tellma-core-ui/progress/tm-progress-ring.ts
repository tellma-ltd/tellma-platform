// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterNextRender,
  booleanAttribute,
  Component,
  computed,
  ElementRef,
  inject,
  input,
  isDevMode,
  numberAttribute,
} from '@angular/core';

/**
 * A thin ring that shows how far a job has got, or, with a `null` value,
 * a spinning arc while it runs. It is drawn around an icon button (the
 * app bar's job indicator), so the host is an inline box `diameter` wide.
 *
 * The ring is decorative by default: the button it surrounds carries the
 * state in its own name ("Jobs, 1 running"). Clear `decorative` to make
 * the ring itself a progress bar, and name it with `aria-label`.
 *
 * @tmGroup indicator
 * @tmA11yNotes Decorative by default (`aria-hidden`). Not decorative, it is
 *   `role="progressbar"` with `aria-valuenow` for a number, named by the
 *   consumer's `aria-label`. The spin is static under reduced motion, and
 *   the arc takes the system highlight colour under forced colours.
 */
@Component({
  selector: 'tm-progress-ring',
  // A full track, then the arc on top. pathLength="100" makes the dash
  // lengths percentages, and the arc starts at 12 o'clock (rotated in CSS).
  template: `
    <svg class="tm-progress-ring__svg" fill="none" aria-hidden="true">
      <circle class="tm-progress-ring__track" pathLength="100" />
      <circle
        class="tm-progress-ring__arc"
        [class.tm-progress-ring__arc--spin]="clamped() === null"
        [class.tm-progress-ring__arc--empty]="clamped() === 0"
        pathLength="100"
        [attr.stroke-dasharray]="dash()"
      />
    </svg>
  `,
  styleUrl: './tm-progress-ring.css',
  host: {
    class: 'tm-progress-ring',
    '[style.inline-size.px]': 'diameter()',
    '[style.block-size.px]': 'diameter()',
    '[attr.aria-hidden]': 'decorative() ? "true" : null',
    '[attr.role]': 'decorative() ? null : "progressbar"',
    '[attr.aria-valuemin]': '!decorative() && clamped() !== null ? 0 : null',
    '[attr.aria-valuemax]': '!decorative() && clamped() !== null ? 100 : null',
    '[attr.aria-valuenow]': 'decorative() ? null : clamped()',
  },
})
export class TmProgressRing {
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  /** How far the job has got, 0 to 100; `null` spins an arc. */
  readonly value = input<number | null>(null);

  /** The ring's outer diameter in px. Default 30. */
  readonly diameter = input(30, { transform: numberAttribute });

  /**
   * Whether the ring is hidden from assistive technology because its
   * surroundings carry the state. Default `true`.
   */
  readonly decorative = input(true, { transform: booleanAttribute });

  /** The value within 0–100, or `null` for the spinning arc. */
  protected readonly clamped = computed(() => {
    const value = this.value();
    return value === null || !Number.isFinite(value) ? null : Math.min(100, Math.max(0, value));
  });

  /** The arc's dash pattern: the share, then the gap; a quarter while spinning. */
  protected readonly dash = computed(() => {
    const share = this.clamped() ?? 25;
    return `${share} 100`;
  });

  constructor() {
    if (isDevMode()) {
      afterNextRender(() => {
        const named =
          this.element.hasAttribute('aria-label') || this.element.hasAttribute('aria-labelledby');
        if (!this.decorative() && !named) {
          console.warn(
            'tm-progress-ring: a ring that is not decorative needs a name — add aria-label or ' +
              'aria-labelledby.',
            this.element,
          );
        }
      });
    }
  }
}
