// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  booleanAttribute,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  input,
  signal,
} from '@angular/core';

import { TM_NOW } from '@tellma/core-ui';

/** The fill colour of a {@link TmProgressBar}. */
export type TmProgressTone = 'accent' | 'success' | 'warning' | 'error';

/** The hold when `--progress-min-visible` cannot be read (no token stylesheet). */
const DEFAULT_MIN_VISIBLE_MS = 250;

/**
 * A CSS time ("250ms", "0.25s") in milliseconds, or `null` when it is not
 * one.
 */
function parseCssTime(value: string): number | null {
  const match = /^(\d+(?:\.\d+)?|\.\d+)(ms|s)$/.exec(value.trim());
  if (match === null) {
    return null;
  }
  const amount = Number(match[1]);
  return match[2] === 's' ? amount * 1000 : amount;
}

/**
 * A 4 px bar that shows how far a task has got, or, with a `null` value,
 * that it is running (a sweep from inline-start). It takes no layout space
 * of its own beyond its 4 px: consumers overlay it on the edge of the
 * region that is loading.
 *
 * Once shown, the bar stays for at least `--progress-min-visible`
 * (250 ms), so a fast answer never flashes it. The hold never delays
 * content: rows render the moment they arrive, and the consumer's
 * `aria-busy` follows the real state. Set `active` to `false` to hide the
 * bar; it goes once the hold ends.
 *
 * @tmGroup indicator
 * @tmA11yNotes `role="progressbar"`, named by `label` or `labelledBy`. A
 *   number sets `aria-valuenow` (0 to 100); `null` leaves it out, which
 *   reads as indeterminate. A hidden bar has no role. The sweep is static
 *   under reduced motion, and the fill takes the system highlight colour
 *   under forced colours.
 */
@Component({
  selector: 'tm-progress-bar',
  // The track clips the fill: a share of the track from inline-start for a
  // number, or the sweep for null.
  template: `
    @if (shown()) {
      <span class="tm-progress-bar__track">
        <span
          class="tm-progress-bar__fill"
          [class.tm-progress-bar__fill--sweep]="clamped() === null"
          [style.inline-size.%]="clamped()"
        ></span>
      </span>
    }
  `,
  styleUrl: './tm-progress-bar.css',
  host: {
    class: 'tm-progress-bar',
    '[class.tm-progress-bar--hidden]': '!shown()',
    '[attr.data-tone]': 'tone()',
    '[attr.role]': 'shown() ? "progressbar" : null',
    '[attr.aria-label]': 'shown() ? (label() ?? null) : null',
    '[attr.aria-labelledby]': 'shown() ? (labelledBy() ?? null) : null',
    '[attr.aria-valuemin]': 'shown() && clamped() !== null ? 0 : null',
    '[attr.aria-valuemax]': 'shown() && clamped() !== null ? 100 : null',
    '[attr.aria-valuenow]': 'shown() ? clamped() : null',
  },
})
export class TmProgressBar {
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly now = inject(TM_NOW);

  /** How far the task has got, 0 to 100; `null` shows the sweep. */
  readonly value = input<number | null>(null);

  /** The accessible name ("Loading invoices"). */
  readonly label = input<string | undefined>(undefined);

  /** Or the id of an element that names the bar. */
  readonly labelledBy = input<string | undefined>(undefined);

  /** The fill colour. Default `accent`. */
  readonly tone = input<TmProgressTone>('accent');

  /**
   * Whether the task is running. Default `true`; `false` hides the bar
   * once it has been shown for the minimum time.
   */
  readonly active = input(true, { transform: booleanAttribute });

  /** The value within 0–100, or `null` for the sweep (a non-finite value too). */
  protected readonly clamped = computed(() => {
    const value = this.value();
    return value === null || !Number.isFinite(value) ? null : Math.min(100, Math.max(0, value));
  });

  /** Whether the bar is still held on screen after `active` turned false. */
  private readonly holding = signal(false);

  /** Whether the bar is on screen: while active, then through the hold. */
  protected readonly shown = computed(() => this.active() || this.holding());

  /** When the bar last appeared (a `TM_NOW` reading), or `null` while hidden. */
  private shownSince: number | null = null;

  constructor() {
    // The hold. Turning active stamps the time the bar appeared; turning
    // inactive keeps it on screen until the minimum time has passed since
    // then. A bar that turns active again during the hold simply stays.
    effect((onCleanup) => {
      if (this.active()) {
        this.shownSince ??= this.now();
        this.holding.set(false);
        return;
      }
      if (this.shownSince === null) {
        return;
      }
      const remaining = this.minVisibleMs() - (this.now() - this.shownSince);
      if (remaining <= 0) {
        this.shownSince = null;
        this.holding.set(false);
        return;
      }
      this.holding.set(true);
      const timer = setTimeout(() => {
        this.shownSince = null;
        this.holding.set(false);
      }, remaining);
      onCleanup(() => clearTimeout(timer));
    });
  }

  /** The hold, read from the `--progress-min-visible` token on the host. */
  private minVisibleMs(): number {
    const token = getComputedStyle(this.element).getPropertyValue('--progress-min-visible');
    return parseCssTime(token) ?? DEFAULT_MIN_VISIBLE_MS;
  }
}
